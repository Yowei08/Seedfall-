using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城商店的**解锁集合**：买了才在战斗里刷新（用户原话）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、这一层在回答什么问题
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户原话：「商店卖的是**道具、插件、植物**，**买了之后才会在战斗刷新这些**」。
    /// ⇒ 商店卖的不是"一件消耗品"，而是"让这件东西**有机会出现在战斗刷新池里**"这件事本身。
    ///   所以数据形状不是 `数量`，而是 `已解锁 / 未解锁` 两态 +（上架时的）价格 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、默认解锁 + 只登记"需要解锁的那一批"（关键设计决定）
    /// ══════════════════════════════════════════════════════════════════════
    /// `Data/town_unlocks.json` **只列新批次**（30 道具 / 24 插件 / 8 植物），
    /// 老的 56 道具 / 12 插件 / 22 植物**一条都不写**进表。
    /// `IsUnlocked(id)` 对"表里查不到的 id"返回 `true` ⇒ 默认全部解锁 ✓
    ///
    /// 为什么不是"把全部 152 条都列进表、用 defaultUnlocked 标"：
    ///   ① 表一旦漏一条，那种后果是**灾难性的**：全部老道具突然不能刷新，而没有任何报错 ✗
    ///      本规则下漏一条的后果只是"它默认能刷新"——和加这张表之前一模一样 ✓
    ///   ② 战斗侧的表随时会加条目（另一路正在改数据）；"新加的东西默认可用"是唯一不会
    ///      让别人的活在本轮之后突然失效的方向 ✓
    /// ⇒ 代价写在这里：**要拦一件东西，必须显式登记它**。这是有意的取舍。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、谁来读
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 主城商店面板（本工程）：`At(i)` / `Owned(i)` / `TryUnlock(i)`
    ///   · **战斗侧的刷新池**（另一路接线，本轮不碰 `Flow/RunSession.cs` / `ShopSystem`）：
    ///     只读 <see cref="IsUnlocked(string)">这个静态方法</see> —— 那是本类对外的**唯一约定**：
    ///
    /// ```csharp
    /// // 战斗侧（另一路，在抽商品/抽植物之前各加一句）：
    /// if (!TownUnlocks.IsUnlocked(item.id)) continue;     // 没解锁的东西不进刷新池
    /// ```
    ///
    /// ⚠️ 边界：解锁状态是**运行期静态**的（与 `TownSkins` / `TownDrops` 同口径：一次运行内有效，
    ///    退出 play 清空）。没有落盘 —— 落盘属于登录/存档那一路（`Assets/Scripts/Login/**`，
    ///    本轮禁改）。所以"买了之后换场景"仍然是解锁的，"关掉游戏再开"要等存档那路接上 ✓
    /// </summary>
    public static class TownUnlocks
    {
        /// <summary>解锁表在 Resources 下的路径。</summary>
        public const string TablePath = "Data/town_unlocks";

        /// <summary>表里的全部条目（★ 只含"需要解锁"的那一批 ★）。</summary>
        public static TownUnlockDef[] Items { get; private set; } = Array.Empty<TownUnlockDef>();

        /// <summary>表加载成功没有（失败时 `LastError` 有原文，面板直接显示它）。</summary>
        public static bool Ready { get; private set; }

        /// <summary>最近一次失败原因。</summary>
        public static string LastError { get; private set; }

        /// <summary>最近一次成交/失败的一行说明（面板就地显示）。</summary>
        public static string LastLine { get; private set; } = "(还没买过解锁)";

        /// <summary>本次运行里买下来的 id（顺序无关，只判存在）。</summary>
        private static readonly HashSet<string> Purchased = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>条目数。</summary>
        public static int Count => Items.Length;

        /// <summary>上架（现在能买）的条目数。</summary>
        public static int OnSaleCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Items.Length; i++) if (Items[i].onSale) n++;
                return n;
            }
        }

        /// <summary>已经买下来的条目数。</summary>
        public static int OwnedCount => Purchased.Count;

        /// <summary>取第 <paramref name="index"/> 条；越界返回 null。</summary>
        public static TownUnlockDef At(int index)
            => index >= 0 && index < Items.Length ? Items[index] : null;

        /// <summary>
        /// ★ **本类对外的唯一约定**：这件东西在战斗里该不该出现 ★
        /// 表里查不到 ⇒ `true`（= 老的那批，默认解锁）；查得到 ⇒ 看这次运行里买过没有。
        /// </summary>
        public static bool IsUnlocked(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (Purchased.Contains(id)) return true;
            int idx = IndexOf(id);
            return idx < 0;                       // 表里没登记 = 不在"需要解锁"的名单里 = 默认解锁
        }

        /// <summary>按 id 找条目下标；找不到返回 -1。</summary>
        public static int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id) || Items.Length == 0) return -1;
            for (int i = 0; i < Items.Length; i++)
                if (string.Equals(Items[i].id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// ★★ v252：这一件**现在正在上架卖**吗（`onSale` 且价格有效）★★
        ///
        /// 用途只有一个：战斗商店车的候选池要包含"主城开放购买的内容"——
        /// 用户原话「将主城商店中开放购买的内容在删除重复后全部加入实际战斗的商店车中」。
        /// `TownUnlocks.Load` 已经把"上架却没价"的那几条按未上架处理（见 `Load` 里的兜底），
        /// 所以这里只需要读 `onSale` ✓ 表里查不到（老货）⇒ 返回 false（它们本来就不受本表管）✓
        /// </summary>
        public static bool OnSale(string id)
        {
            int idx = IndexOf(id);
            return idx >= 0 && Items[idx].onSale;
        }

        /// <summary>这一条买了没有。</summary>
        public static bool Owned(int index)
        {
            TownUnlockDef d = At(index);
            return d != null && Purchased.Contains(d.id);
        }

        // ══════════════════════════════════════════════════════════════════
        // 载入
        // ══════════════════════════════════════════════════════════════════

        /// <summary>载入解锁表。失败**不抛**（主城不能因为这张表跑了半张图就整体起不来）。</summary>
        public static bool Load()
        {
            if (Ready) return true;
            LastError = null;
            TextAsset asset = Resources.Load<TextAsset>(TablePath);
            if (asset == null)
            {
                LastError = $"缺少配置表 Assets/Resources/{TablePath}.json";
                Log.Error("[Town] 解锁表：" + LastError);
                return false;
            }
            TownUnlockTable table;
            try
            {
                table = JsonUtility.FromJson<TownUnlockTable>(asset.text);
            }
            catch (Exception e)
            {
                LastError = $"{TablePath}.json 解析失败：{e.Message}";
                Log.Error("[Town] 解锁表：" + LastError);
                return false;
            }
            if (table == null || table.items == null)
            {
                LastError = $"{TablePath}.json 里没有 items 数组";
                Log.Error("[Town] 解锁表：" + LastError);
                return false;
            }

            var list = new List<TownUnlockDef>(table.items.Length);
            for (int i = 0; i < table.items.Length; i++)
            {
                TownUnlockDef d = table.items[i];
                if (d == null || string.IsNullOrWhiteSpace(d.id)) continue;
                if (IndexOf(list, d.id) >= 0) continue;      // 重复 id 只留第一条，不炸
                if (string.IsNullOrWhiteSpace(d.name)) d.name = d.id;
                if (d.onSale && d.price <= 0) d.onSale = false;   // 上架却没价 = 表里的错，按"未上架"处理
                list.Add(d);
            }
            Items = list.ToArray();
            Ready = true;
            Log.Info($"[Town] 解锁表已载入：{Items.Length} 条待解锁（上架 {OnSaleCount} 条 · " +
                     $"植物 {CountOf(TownUnlockDef.KindPlant)} / 道具 {CountOf(TownUnlockDef.KindItem)} / " +
                     $"插件 {CountOf(TownUnlockDef.KindPlugin)}）");
            return true;
        }

        private static int CountOf(string kind)
        {
            int n = 0;
            for (int i = 0; i < Items.Length; i++) if (Items[i].kind == kind) n++;
            return n;
        }

        private static int IndexOf(List<TownUnlockDef> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        // ══════════════════════════════════════════════════════════════════
        // 成交
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 买下第 <paramref name="index"/> 条的解锁权：够钱 ⇒ 扣主城金币并**永久解锁**（本次运行内）。
        /// 失败时把原因写进 <see cref="LastError"/>，**不静默**。
        /// </summary>
        public static bool TryUnlock(int index)
        {
            LastError = null;
            TownUnlockDef d = At(index);
            if (d == null) { LastError = "解锁条目下标越界：" + index; return false; }
            if (!d.onSale) { LastError = d.name + " 暂未上架"; return false; }
            if (Owned(index)) { LastError = d.name + " 已经解锁过了"; return false; }

            TownState state = TownState.Ensure();
            if (state.Coins < d.price)
            {
                LastError = $"主城金币不足：{d.name} 需要 {d.price}，现有 {state.Coins}";
                return false;
            }
            if (!state.TrySpend(d.price))
            {
                LastError = "扣金币失败（余额在两次读之间变了？）";
                return false;
            }
            Purchased.Add(d.id);
            LastLine = $"解锁「{d.name}」（{d.kind} · {d.price} 金）⇒ 从此能在战斗里刷出来";
            Log.Info("[Town] " + LastLine + $"｜IsUnlocked(\"{d.id}\") = " + IsUnlocked(d.id));
            return true;
        }

        /// <summary>清空"买过什么"（重开一局 / 自动化用例之间重置）。**不退钱**。</summary>
        public static void Reset()
        {
            Purchased.Clear();
            LastLine = "(还没买过解锁)";
            LastError = null;
        }

        /// <summary>一句话读数（`TownStatus` 与验收都读它）。</summary>
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append("解锁表 ").Append(Items.Length).Append(" 条（上架 ").Append(OnSaleCount)
              .Append(" 条）｜已解锁 ").Append(OwnedCount).Append(" 条");
            sb.Append("｜老货默认解锁：IsUnlocked(\"iron_hoe\")=").Append(IsUnlocked("iron_hoe"))
              .Append(" IsUnlocked(\"pea_shooter\")=").Append(IsUnlocked("pea_shooter"))
              .Append(" IsUnlocked(\"hoe_swordwave\")=").Append(IsUnlocked("hoe_swordwave"));
            // ★ v252：示例 id 从 `clay_trowel` 换成 `plank_shoes` —— 前者是去重时删掉的重复件
            //   （陶铲与磨刀石完全同效）⇒ 留着它当示例 id 会让这一行读数永远读 false（不存在的东西）。
            sb.Append("｜新货默认锁：IsUnlocked(\"plank_shoes\")=").Append(IsUnlocked("plank_shoes"))
              .Append(" IsUnlocked(\"dusk_berry\")=").Append(IsUnlocked("dusk_berry"))
              .Append(" IsUnlocked(\"hoe_edgewave\")=").Append(IsUnlocked("hoe_edgewave"));
            sb.Append("｜").Append(LastLine);
            if (!string.IsNullOrEmpty(LastError)) sb.Append("｜失败：").Append(LastError);
            return sb.ToString();
        }
    }

    /// <summary>表结构（字段名必须与 JSON 逐字一致 —— JsonUtility 按字段名匹配）。</summary>
    [Serializable]
    public sealed class TownUnlockTable
    {
        public TownUnlockDef[] items;
    }

    /// <summary>
    /// 一条"需要解锁的东西"。
    /// `kind` 只影响面板分组（植物 / 道具 / 插件）与色块，**不参与任何判定**；
    /// 真正的判定只有一条：<see cref="TownUnlocks.IsUnlocked"/>。
    /// </summary>
    [Serializable]
    public sealed class TownUnlockDef
    {
        public const string KindPlant = "植物";
        public const string KindItem = "道具";
        public const string KindPlugin = "插件";

        public string id = "";
        public string kind = KindItem;
        public string name = "";
        public int price = 0;
        public bool onSale = true;

        /// <summary>分组色（面板左侧的小色块，纯展示）。</summary>
        public Color Tint
        {
            get
            {
                if (kind == KindPlant) return new Color(0.42f, 0.78f, 0.38f);
                if (kind == KindPlugin) return new Color(0.98f, 0.72f, 0.28f);
                return new Color(0.52f, 0.68f, 0.92f);
            }
        }
    }
}
