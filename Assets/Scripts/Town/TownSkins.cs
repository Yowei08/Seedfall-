using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>皮肤穿戴槽位。**一个槽同时只穿一件**；换 = 顶掉同槽的旧件，卸 = 回到空。</summary>
    public enum SkinSlot
    {
        Hat = 0,
        Scarf = 1,
        Backpack = 2,
        Belt = 3,
        Cape = 4,
        Mask = 5,
        Glasses = 6,
        Pauldron = 7,
    }

    /// <summary>
    /// ★★ v213：主城的**皮肤商店**（数据 + 拥有/穿戴状态 + 实时套到玩家模型上）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是一个新的静态类，而不是往 `TownBootstrap` / `TownState` 里加
    /// ══════════════════════════════════════════════════════════════════════
    /// `TownBootstrap` 本轮**禁止改动**（另一路在用）；`TownState` 是金币/田地/仓库的账本，
    /// 把"买了哪顶帽子"塞进去会让那份"三个数组 + 两个标量"的快照说明失真。
    /// ⇒ 皮肤自成一份状态：`_owned[]` / `_equipped[槽]` / `Version`，
    ///   与仓库、田地、金币**没有一处共享的字段**，唯一的耦合是"买的时候向 `TownState.TrySpend` 要钱"✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、"实时改变"是怎么做到的（用户原话：「玩家皮肤的**实时改变**」）
    /// ══════════════════════════════════════════════════════════════════════
    /// 改一次装备 ⇒ `Version++`。玩家每一帧走 `TownAvatar.Tick`，它在末尾调一次
    /// <see cref="Refresh"/>；`Refresh` 的**第一件事是一个整数比较**（`_appliedVersion == Version`）
    /// ⇒ 没换装备的那一帧只花一次 int 比较、零分配、不遍历、不建物体 ✓
    /// 换了装备的那一帧才走 <see cref="Rebuild"/>：拆掉旧的一批物体、按当前穿戴重建。
    /// ⇒ 玩家在主城里点「穿上」，**下一帧模型就变了**，不需要重开场景、不需要重开面板 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、共享资源契约（与 `PlayerBean` 同一条纪律）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 网格：内建图元走 `TownKit.Prim`（静态缓存）；自建的两个（环面 / 圆锥）在
    ///     `SkinModels` 里按形状键缓存 ⇒ **不随"穿了几件"或"换了几次"增长** ✓
    ///   · 材质：一律 `ToonLook.Get(色, 描边)`（共享池，键 = 底色 + 描边）
    ///     ⇒ 8 件商品最多 16 份材质，与实例数无关 ✓ **本文件不出现任何 `new Material`** ✓
    ///   · 本类**不改颜色属性**：受击闪白仍由 `ModelView.SetTint` 用 MPB 写，装饰件也吃同一份 MPB ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、失败模式（**不静默，但也不让主城停摆**）
    /// ══════════════════════════════════════════════════════════════════════
    /// 表缺失 / 解析失败 / id 重复 / slot 不认识 / price ≤ 0 ⇒ `Log.Error` + 抛（与 `TownData` 同口径：
    /// 商品表坏了就不该"降级成一家空店"）。
    /// 但骨骼找不到、某件几何建不出来 ⇒ **只记一条 Warning 并跳过那一件** ——
    /// 一件装饰消失不该让玩家连主城都进不去 ✓
    /// </summary>
    public static class TownSkins
    {
        public const string TablePath = "Data/town_skins";

        /// <summary>槽位数量（枚举成员数）。</summary>
        public const int SlotCount = (int)SkinSlot.Pauldron + 1;

        public static TownSkinDef[] Items { get; private set; } = Array.Empty<TownSkinDef>();

        /// <summary>表载入成功过没有。</summary>
        public static bool Ready { get; private set; }

        /// <summary>载入失败原文（HUD/探针直接显示，而不是显示一家空店）。</summary>
        public static string LastError { get; private set; }

        /// <summary>只在第一次 `Load()` 失败时记一条错误，避免每帧刷屏。</summary>
        private static bool _loadFailed;

        // ── 拥有 / 穿戴 ──
        private static bool[] _owned = Array.Empty<bool>();
        private static int[] _equipped = new int[SlotCount];

        /// <summary>装备变化计数。**只增不减**，`Refresh` 靠它判"这一帧要不要重建"。</summary>
        public static int Version { get; private set; }

        /// <summary>最近一次重建出来的一句话（探针/报告引用）。</summary>
        public static string LastBuild { get; private set; } = "(还没建过)";

        /// <summary>当前挂在玩家身上的装饰物件（重建前逐个销毁）。</summary>
        private static readonly List<GameObject> Spawned = new List<GameObject>(16);

        /// <summary>`Refresh` 记下的"已经套在谁身上 / 套的是哪一版"。</summary>
        private static GameObject _appliedRoot;
        private static int _appliedVersion = -1;

        /// <summary>已买过几件。</summary>
        public static int OwnedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _owned.Length; i++) if (_owned[i]) n++;
                return n;
            }
        }

        /// <summary>此刻穿着几件。</summary>
        public static int EquippedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SlotCount; i++) if (_equipped[i] >= 0) n++;
                return n;
            }
        }

        /// <summary>此刻挂在玩家身上的装饰**物体**数（探针用它证明"真的建出来了"）。</summary>
        public static int SpawnedCount => Spawned.Count;

        // ══════════════════════════════════════════════════════════════════
        // 载入
        // ══════════════════════════════════════════════════════════════════

        /// <summary>读表（幂等）。失败抛一次，之后 `Ready` 仍为 false、`LastError` 保留原文。</summary>
        public static void Load()
        {
            if (Ready || _loadFailed) return;
            try
            {
                TextAsset asset = Resources.Load<TextAsset>(TablePath);
                if (asset == null) throw Fail($"缺少配置表 Assets/Resources/{TablePath}.json");

                TownSkinTable table;
                try
                {
                    table = JsonUtility.FromJson<TownSkinTable>(asset.text);
                }
                catch (Exception e)
                {
                    throw Fail($"{TablePath}.json 解析失败：{e.Message}");
                }
                if (table == null || table.items == null || table.items.Length == 0)
                    throw Fail("town_skins.json 的 items 数组为空");

                var items = new TownSkinDef[table.items.Length];
                for (int i = 0; i < table.items.Length; i++)
                {
                    TownSkinDef d = table.items[i];
                    if (d == null) throw Fail($"town_skins.json 第 {i} 条为 null");
                    if (string.IsNullOrWhiteSpace(d.id)) throw Fail($"town_skins.json 第 {i} 条缺少 id");
                    for (int j = 0; j < i; j++)
                        if (items[j].id == d.id) throw Fail($"town_skins.json 的 id 重复：{d.id}");
                    if (d.price <= 0) throw Fail($"皮肤 {d.id} 的 price 必须 > 0（现 {d.price}）");
                    if (!TryParseSlot(d.slot, out SkinSlot slot))
                        throw Fail($"皮肤 {d.id} 的 slot「{d.slot}」不认识（可用：{SlotKeys}）");
                    if (string.IsNullOrWhiteSpace(d.bones))
                        throw Fail($"皮肤 {d.id} 的 bones 为空（必须写 PlayerBean 的节点名）");
                    if (string.IsNullOrWhiteSpace(d.name)) d.name = d.id;
                    d.BindSlot(slot);
                    items[i] = d;
                }

                Items = items;
                _owned = new bool[items.Length];
                for (int i = 0; i < SlotCount; i++) _equipped[i] = -1;
                Ready = true;
                LastError = null;
                Log.Info($"主城皮肤表载入：{items.Length} 件（槽位 {SlotCount} 个）");
            }
            catch (Exception e)
            {
                _loadFailed = true;
                LastError = e.Message;
                Ready = false;
                throw;
            }
        }

        /// <summary>不抛的载入（HUD / 每帧路径用）：失败时返回 false 并把原文留在 `LastError`。</summary>
        public static bool TryLoad()
        {
            if (Ready) return true;
            if (_loadFailed) return false;
            try { Load(); return Ready; }
            catch (Exception e)
            {
                // `Load()` 已经把原文 `Log.Error` 出去了；这里补一条"是谁在调、什么时候"，
                // 然后把失败原样交给调用方（**不静默吞掉**）✓
                LastError = e.Message;
                Log.Warn("主城皮肤表载入失败（本次调用方将按「身上没有装饰」继续）：" + e.Message);
                return false;
            }
        }

        private static InvalidOperationException Fail(string msg)
        {
            LastError = msg;
            Log.Error("主城皮肤表：" + msg);
            return new InvalidOperationException(msg);
        }

        // ══════════════════════════════════════════════════════════════════
        // 买 / 装 / 卸
        // ══════════════════════════════════════════════════════════════════

        public static bool Owned(int index)
            => index >= 0 && index < _owned.Length && _owned[index];

        /// <summary>这个槽此刻穿的是第几件（-1 = 空）。</summary>
        public static int EquippedOf(SkinSlot slot)
        {
            int s = (int)slot;
            return s >= 0 && s < SlotCount ? _equipped[s] : -1;
        }

        public static bool IsEquipped(int index)
        {
            if (index < 0 || index >= Items.Length) return false;
            return _equipped[(int)Items[index].Slot] == index;
        }

        /// <summary>
        /// 花主城金币买第 <paramref name="index"/> 件。返回是否成功。
        /// 失败的含义：索引非法 / 表没载入 / 已经买了 / 金币不够。
        /// **买 = 永久拥有**，不自动穿上（穿上要玩家再点一下，两件事分开）。
        /// </summary>
        public static bool TryBuy(int index)
        {
            if (!TryLoad()) return false;
            if (index < 0 || index >= Items.Length) return false;
            if (_owned[index]) return false;

            TownState state;
            try
            {
                // `Ensure` 会顺带把 `TownData` 载进来（见它的注释）；表坏了就**别扣钱也别记拥有**，
                // 一行 Error 说清楚，商店其它行照常可点（**不静默、也不让面板炸掉**）✓
                state = TownState.Ensure();
            }
            catch (Exception e)
            {
                Log.Error("主城皮肤：主城状态建不出来，这次买不了（" + e.Message + "）");
                return false;
            }
            if (!state.TrySpend(Items[index].price)) return false;

            _owned[index] = true;
            Version++;                       // 商店那一行的「未买 → 已买」要立刻变
            Log.Info($"主城皮肤：买了「{Items[index].name}」（{Items[index].price} 金，余额 {state.Coins}）");
            return true;
        }

        /// <summary>穿上第 <paramref name="index"/> 件（必须已买）。同槽的旧件自动卸下。</summary>
        public static bool Equip(int index)
        {
            if (!TryLoad()) return false;
            if (index < 0 || index >= Items.Length) return false;
            if (!_owned[index]) return false;

            int slot = (int)Items[index].Slot;
            if (_equipped[slot] == index) return false;      // 已经穿着了
            _equipped[slot] = index;
            Version++;
            Log.Info($"主城皮肤：穿上「{Items[index].name}」（槽 {SlotName(Items[index].Slot)}）");
            return true;
        }

        /// <summary>卸下这个槽。没穿也返回 false（不产生空推进）。</summary>
        public static bool Unequip(SkinSlot slot)
        {
            int s = (int)slot;
            if (s < 0 || s >= SlotCount) return false;
            if (_equipped[s] < 0) return false;
            string was = Items.Length > 0 && _equipped[s] < Items.Length ? Items[_equipped[s]].name : "?";
            _equipped[s] = -1;
            Version++;
            Log.Info($"主城皮肤：卸下「{was}」（槽 {SlotName(slot)}）");
            return true;
        }

        /// <summary>一键脱光（探针复位 / 玩家想看清原型时用）。</summary>
        public static int UnequipAll()
        {
            int n = 0;
            for (int s = 0; s < SlotCount; s++) if (_equipped[s] >= 0) { _equipped[s] = -1; n++; }
            if (n > 0) Version++;
            return n;
        }

        /// <summary>把拥有/穿戴状态清空（"重开一局"的口子，与 `TownState.Discard` 同性质）。</summary>
        public static void Reset()
        {
            if (_owned.Length > 0) Array.Clear(_owned, 0, _owned.Length);
            for (int s = 0; s < SlotCount; s++) _equipped[s] = -1;
            Version++;
            _appliedVersion = -1;
        }

        // ══════════════════════════════════════════════════════════════════
        // 实时套到玩家身上
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 每帧调一次（`TownAvatar.Tick` 末尾）。**没换装备时只做一次 int 比较**；
        /// 换了装备的那一帧才重建。传 null 是空操作。
        /// </summary>
        public static void Refresh(GameObject playerRoot)
        {
            if (playerRoot == null) return;
            if (ReferenceEquals(_appliedRoot, playerRoot) && _appliedVersion == Version) return;

            _appliedRoot = playerRoot;
            _appliedVersion = Version;
            Rebuild(playerRoot);
        }

        /// <summary>强制重建（探针复位时用；正常路径不要调它）。</summary>
        public static void InvalidateApplied() => _appliedVersion = int.MinValue;

        private static void Rebuild(GameObject playerRoot)
        {
            for (int i = 0; i < Spawned.Count; i++) Kill(Spawned[i]);
            Spawned.Clear();

            if (!TryLoad())
            {
                LastBuild = "皮肤表没载入 ⇒ 身上没有任何装饰";
                return;
            }

            var sb = new System.Text.StringBuilder(160);
            sb.Append("身上的装饰：");
            for (int s = 0; s < SlotCount; s++)
            {
                int idx = _equipped[s];
                if (idx < 0 || idx >= Items.Length) continue;
                List<GameObject> made = SkinModels.Build(Items[idx], playerRoot.transform);
                if (made == null || made.Count == 0) continue;
                for (int k = 0; k < made.Count; k++) if (made[k] != null) Spawned.Add(made[k]);
                sb.Append($"「{Items[idx].name}」@{Items[idx].bones}×{made.Count} ");
            }
            if (Spawned.Count == 0) sb.Append("（无）");
            sb.Append($" ⇒ 物件 {Spawned.Count} 个，共用材质 {ToonLook.MaterialPoolCount} 份");
            LastBuild = sb.ToString();
        }

        private static void Kill(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }

        /// <summary>把装饰全拆掉（离开主城 / 换模型时用）。</summary>
        public static void DetachAll()
        {
            for (int i = 0; i < Spawned.Count; i++) Kill(Spawned[i]);
            Spawned.Clear();
            _appliedRoot = null;
            _appliedVersion = -1;
            LastBuild = "已经全部拆掉";
        }

        // ══════════════════════════════════════════════════════════════════
        // 槽位名 / 文案
        // ══════════════════════════════════════════════════════════════════

        /// <summary>JSON 里能写的槽位键（错误提示直接列它）。</summary>
        public const string SlotKeys = "hat, scarf, backpack, belt, cape, mask, glasses, pauldron";

        public static string SlotName(SkinSlot slot)
        {
            switch (slot)
            {
                case SkinSlot.Hat: return "头饰";
                case SkinSlot.Scarf: return "围巾";
                case SkinSlot.Backpack: return "背包";
                case SkinSlot.Belt: return "腰带";
                case SkinSlot.Cape: return "斗篷";
                case SkinSlot.Mask: return "面具";
                case SkinSlot.Glasses: return "眼镜";
                case SkinSlot.Pauldron: return "肩甲";
                default: return slot.ToString();
            }
        }

        public static bool TryParseSlot(string key, out SkinSlot slot)
        {
            switch ((key ?? "").Trim().ToLowerInvariant())
            {
                case "hat": slot = SkinSlot.Hat; return true;
                case "scarf": slot = SkinSlot.Scarf; return true;
                case "backpack": slot = SkinSlot.Backpack; return true;
                case "belt": slot = SkinSlot.Belt; return true;
                case "cape": slot = SkinSlot.Cape; return true;
                case "mask": slot = SkinSlot.Mask; return true;
                case "glasses": slot = SkinSlot.Glasses; return true;
                case "pauldron": slot = SkinSlot.Pauldron; return true;
                default: slot = SkinSlot.Hat; return false;
            }
        }

        /// <summary>此刻穿着的名字（顿号分隔），给 HUD 那行用。没穿返回空串。</summary>
        public static string EquippedLine()
        {
            var sb = new System.Text.StringBuilder(64);
            for (int s = 0; s < SlotCount; s++)
            {
                int idx = _equipped[s];
                if (idx < 0 || idx >= Items.Length) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(Items[idx].name);
            }
            return sb.ToString();
        }

        /// <summary>一句话状态（HUD / 探针共用）。</summary>
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder(200);
            sb.Append($"皮肤：货 {Items.Length} 件｜已买 {OwnedCount}｜穿着 {EquippedCount}｜物件 {Spawned.Count}｜ver {Version}｜材质池 {ToonLook.MaterialPoolCount}");
            for (int s = 0; s < SlotCount; s++)
            {
                int idx = _equipped[s];
                if (idx < 0 || idx >= Items.Length) continue;
                sb.Append($"｜{SlotName((SkinSlot)s)}={Items[idx].name}");
            }
            return sb.ToString();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // 表结构（字段名必须与 JSON 逐字一致 —— JsonUtility 按字段名匹配）
    // ══════════════════════════════════════════════════════════════════════

    [Serializable]
    public sealed class TownSkinTable
    {
        public TownSkinDef[] items;
    }

    /// <summary>一件角色装饰品：买（price）/ 挂在哪根骨骼（bones）/ 用什么色（color + color2）。</summary>
    [Serializable]
    public sealed class TownSkinDef
    {
        public string id = "";
        public string name = "";
        public string slot = "";
        public string bones = "";
        public int price = 1;
        public string color = "#FFFFFF";
        public string color2 = "#FFFFFF";
        public string desc = "";

        /// <summary>`slot` 字符串解析出来的枚举（载入时绑定，**不参与 JSON 序列化**）。</summary>
        [NonSerialized] private SkinSlot _slot;

        public SkinSlot Slot => _slot;

        internal void BindSlot(SkinSlot slot) => _slot = slot;

        public Color Tint => ParseHex(color);
        public Color Tint2 => ParseHex(color2);

        /// <summary>`bones` 拆成节点名数组（去空白、丢空项）。</summary>
        public string[] BoneNames()
        {
            if (string.IsNullOrEmpty(bones)) return Array.Empty<string>();
            string[] raw = bones.Split(',');
            var outList = new List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                string b = raw[i].Trim();
                if (b.Length > 0) outList.Add(b);
            }
            return outList.ToArray();
        }

        private static Color ParseHex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color c)) return c;
            return new Color(0.55f, 0.55f, 0.55f);
        }
    }
}
