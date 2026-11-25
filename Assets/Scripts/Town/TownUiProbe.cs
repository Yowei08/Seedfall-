using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v237：**主城三处滚动面板的实机读数探针**（只读：不改任何游戏状态）★★
    ///
    /// ── 它回答什么问题 ──
    ///   用户原话：「商店的滑动器其实无效，无法实际更新界面」。
    ///   要证明"修好了"，必须同时给出三组数：
    ///     ① 每处的 **内容高 / 视口高 / 可滚余量**（余量 = 0 ⇒ 条在、拖不动）；
    ///     ② 一颗真的 **滚轮事件** 之后 `scroll.y` 变了多少；
    ///     ③ 一颗 **PageDown / End** 之后 `scroll.y` 变了多少（这条走 `ScrollBegin` 自己的键盘分支）。
    ///
    /// ── 为什么用"注入 `Event.current`"而不是移动真鼠标 ──
    ///   被验的机制是 `GUI.BeginScrollView` 自己的滚轮分支：它只在 `Event.current.type == ScrollWheel`
    ///   且鼠标落在视口矩形里时才吃。把一颗**同形状的事件**放进 `Event.current` 再走同一条路径，
    ///   进的就是 Unity 那段代码本身（不是我在探针里复算一遍算术）✓
    ///   代价（写明）：这**不是**物理鼠标 —— 操作系统级的滚轮投递本轮没有验（见交付说明）✓
    ///
    /// ── 怎么用 ──
    ///   把它挂在场景里任何一个物体上（或 `TownUiProbe.Run()` 自动建一个），进 Play，调 `RunNow("")`。
    ///   它会在接下来的十几帧里依次画：商店植物页 → 商店道具页 → 收种子页 → 服装店 → 图鉴，
    ///   每一处都打一行 `[TownProbe]` 读数（内容高/视口高/可滚/scroll.y/偏移），最后一次打总表。
    ///   不动 `TownState` / 不买东西 / 不写盘 ✓
    /// </summary>
    public sealed class TownUiProbe : MonoBehaviour
    {
        private static TownUiProbe _inst;

        /// <summary>探针当前在跑哪一步（0 = 空闲）。</summary>
        private int _step;
        private readonly List<string> _lines = new List<string>(32);
        private bool _done;

        /// <summary>取一个探针实例（没有就建一个；跨场景存活）。</summary>
        public static TownUiProbe Ensure()
        {
            if (_inst != null) return _inst;
            var go = new GameObject("TownUiProbe") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            _inst = go.AddComponent<TownUiProbe>();
            return _inst;
        }

        /// <summary>桥/命令入口：开始一轮读数（参数忽略）。</summary>
        public static void Run()
        {
            TownUiProbe p = Ensure();
            p._step = 1;
            p._done = false;
            p._lines.Clear();
            Log.Info("[TownProbe] 开始：画面 " + Screen.width + "×" + Screen.height + "，逐帧画三处滚动面板");
        }

        /// <summary>`unity_drive send` 的入口（参数忽略）。</summary>
        public void RunNow(string ignored) => Run();

        /// <summary>
        /// ★ v237：**② 刷新池 / ③ 默认插件的读数**（只读，不掷随机数、不发钱）★
        /// 桥/控制台都能直接调（真正的入口在 `TownBootstrap.UiPoolProbe`）。
        /// </summary>
        public static string PoolsReadout()
        {
            TableDatabase.Load();          // 幂等；没进过战斗时由这里补一次
            TownUnlocks.Load();
            TownCodex.Load();
            var sb = new System.Text.StringBuilder(1024);
            sb.Append("② ").Append(ShopSystem.SeedPoolReport());
            sb.Append("\n② 插件表（plugins.json 解析后）：").Append(TableDatabase.Plugins.Length).Append(" 条");
            sb.Append("\n③ 默认拥有清单：").Append(WeaponSystem.DefaultOwnedPlugins.Length).Append(" 件 → ")
              .Append(string.Join("、", WeaponSystem.DefaultOwnedPlugins));
            for (int i = 0; i < WeaponSystem.DefaultOwnedPlugins.Length; i++)
                sb.Append("\n③ ").Append(DescribeDefaultPlugin(WeaponSystem.DefaultOwnedPlugins[i]));
            string text = sb.ToString();
            Log.Info("[TownProbe] " + text);
            return text;
        }

        /// <summary>
        /// 一件默认插件"到底有没有用"的实机读数：**装之前 vs 装之后**的 `WeaponModifiers` 差在哪一项。
        /// 走的是真的 `WeaponSystem.Install` + `WeaponSystem.Aggregate`（不是我在探针里另算一遍）。
        /// </summary>
        private static string DescribeDefaultPlugin(string id)
        {
            PluginAsset asset = null;
            PluginAsset[] all = TableDatabase.Plugins;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].id == id) { asset = all[i]; break; }
            if (asset == null) return id + "｜**表里找不到**（id 拼错？）";
            if (asset.allowedWeapons == null || asset.allowedWeapons.Length == 0)
                return id + "｜允许任意武器，探针不指定 ⇒ 跳过";

            WeaponSlot slot = asset.allowedWeapons[0];
            var goods = new PlayerGoods();
            int granted = WeaponSystem.GrantDefaultOwned(goods);
            WeaponModifiers before = WeaponSystem.Aggregate(goods, slot);

            bool ok = WeaponSystem.Install(goods, asset, slot, out string why);
            WeaponModifiers after = WeaponSystem.Aggregate(goods, slot);

            string delta = DescribeDelta(asset.effect, before, after);
            return $"{id}（{asset.displayName}｜{asset.quality}｜{asset.effect}｜发给 {granted} 件）" +
                   $"｜装到 {slot}：{(ok ? "成功" : "失败：" + why)}｜{delta}";
        }

        private static string DescribeDelta(PluginEffectKind k, in WeaponModifiers b, in WeaponModifiers a)
        {
            switch (k)
            {
                case PluginEffectKind.SwordWave: return Tick("剑气道数", b.SwordWaveCount, a.SwordWaveCount);
                case PluginEffectKind.BurnOnHit:
                    return Tick("灼烧层数", b.BurnLayersPerHit, a.BurnLayersPerHit) + "、" +
                           F("灼烧每秒比例", b.BurnRatioPerLayer, a.BurnRatioPerLayer);
                case PluginEffectKind.DoubleStrike: return Tick("额外刀数", b.ExtraSwings, a.ExtraSwings);
                case PluginEffectKind.Lifesteal: return F("吸血比例", b.LifestealPct, a.LifestealPct);
                case PluginEffectKind.MultiJet: return Tick("额外水柱", b.ExtraJets, a.ExtraJets);
                case PluginEffectKind.SlowOnHit:
                    return Tick("减速层数", b.SlowLayersPerHit, a.SlowLayersPerHit) + "、" +
                           F("每层减速", b.SlowPctPerLayer, a.SlowPctPerLayer) + "、" +
                           F("减速每秒比例", b.SlowDamageRatio, a.SlowDamageRatio);
                case PluginEffectKind.ThickJet: return F("柱宽增比", b.BeamWidthPct, a.BeamWidthPct);
                case PluginEffectKind.AutoCharge: return F("每秒回水", b.AutoChargePerSecond, a.AutoChargePerSecond);
                case PluginEffectKind.SubBurst:
                    return Tick("子爆个数", b.SubBurstCount, a.SubBurstCount) + "、" +
                           F("子爆半径比", b.SubBurstRadiusRatio, a.SubBurstRadiusRatio);
                case PluginEffectKind.PoisonZone:
                    return Tick("毒区层数", b.PoisonLayers, a.PoisonLayers) + "、" +
                           F("毒区每秒比例", b.PoisonRatioPerLayer, a.PoisonRatioPerLayer);
                case PluginEffectKind.FreeBurst: return F("免耗概率", b.FreeBurstChance, a.FreeBurstChance);
                case PluginEffectKind.ChainBurst: return Tick("连环次数", b.ChainBurstCount, a.ChainBurstCount);
                default: return "效果 = " + k + "（没有消费分支）";
            }
        }

        private static string Tick(string name, int b, int a)
            => b == a ? name + " 没变（**没生效**）" : $"{name} {b} → {a}";

        private static string F(string name, float b, float a)
            => Mathf.Abs(b - a) < 1e-5f ? name + " 没变（**没生效**）" : $"{name} {b:0.###} → {a:0.###}";

        private void Awake()
        {
            _inst = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnGUI()
        {
            if (_step <= 0 || _done) return;
            // 只在 Repaint 那一趟推进（Unity 每个事件都会调 OnGUI；用 Layout 推进会一步走三遍）
            if (Event.current.type != EventType.Repaint) return;
            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                Log.Error("[TownProbe] 第 " + _step + " 步抛异常：" + e);
                _done = true;
            }
        }

        private void Step()
        {
            TownHud.Begin();
            TownState s = TownState.Ensure();

            switch (_step)
            {
                // ── ① 商店 · 植物（默认页签，用户最常看到的那一页）──
                case 1:
                    TownHud.ShopTab = TownHud.ShopPage.Plants;
                    DrawShop(s, "① 商店/植物（scroll=0）");
                    break;
                case 2:
                    TownHud.ShopTab = TownHud.ShopPage.Plants;
                    DrawShopWithWheel(s, -3f, "② 商店/植物（注入滚轮 −3）");
                    break;
                case 3:
                    TownHud.ShopTab = TownHud.ShopPage.Plants;
                    DrawShopWithKey(s, KeyCode.PageDown, "③ 商店/植物（注入 PageDown）");
                    break;
                case 4:
                    TownHud.ShopTab = TownHud.ShopPage.Plants;
                    DrawShopWithKey(s, KeyCode.End, "④ 商店/植物（注入 End → 滚到底）");
                    break;

                // ── ② 商店 · 道具（条目最多的一页）──
                case 5:
                    TownHud.ShopTab = TownHud.ShopPage.Items;
                    DrawShop(s, "⑤ 商店/道具（scroll=0）");
                    break;
                case 6:
                    TownHud.ShopTab = TownHud.ShopPage.Items;
                    DrawShopWithKey(s, KeyCode.End, "⑥ 商店/道具（注入 End）");
                    break;

                // ── ③ 商店 · 收种子 ──
                case 7:
                    TownHud.ShopTab = TownHud.ShopPage.Recycle;
                    DrawShop(s, "⑦ 商店/收种子（scroll=0）");
                    break;
                case 8:
                    TownHud.ShopTab = TownHud.ShopPage.Recycle;
                    DrawShopWithWheel(s, -3f, "⑧ 商店/收种子（注入滚轮 −3）");
                    break;

                // ── ④ 服装店 ──
                case 9:
                    TownHud.DrawClothing(s);
                    Note("服装店", "⑨ 服装店（scroll=0）");
                    break;
                case 10:
                    InjectKey(KeyCode.End);
                    TownHud.DrawClothing(s);
                    Note("服装店", "⑩ 服装店（注入 End）");
                    break;

                // ── ⑤ 图鉴 · 道具 ──
                case 11:
                    TownHud.CodexTab = TownCodex.Page.Items;
                    TownHud.DrawCodex(s);
                    Note("图鉴/Items", "⑪ 图鉴/道具（scroll=0）");
                    break;
                case 12:
                    TownHud.CodexTab = TownCodex.Page.Items;
                    InjectKey(KeyCode.PageDown);
                    TownHud.DrawCodex(s);
                    Note("图鉴/Items", "⑫ 图鉴/道具（注入 PageDown）");
                    break;

                default:
                    _done = true;
                    Log.Info("[TownProbe] 总表\n" + TownHud.ReadingsReport());
                    return;
            }
            _step++;
        }

        // ─────────────────────────── 画 + 记 ───────────────────────────

        /// <summary>当前商店页签对应的**读数键**（必须与 `TownHud.ReportScroll` 用的键逐字一致）。</summary>
        private static string ShopKey()
            => TownHud.ShopTab == TownHud.ShopPage.Recycle ? "商店/收种子"
             : TownHud.ShopTab == TownHud.ShopPage.Plants ? "商店/" + TownUnlockDef.KindPlant
             : TownHud.ShopTab == TownHud.ShopPage.Plugins ? "商店/" + TownUnlockDef.KindPlugin
             : "商店/" + TownUnlockDef.KindItem;

        private void DrawShop(TownState s, string label)
        {
            TownHud.DrawShop(s);
            Note(ShopKey(), label);
        }

        private void DrawShopWithWheel(TownState s, float dy, string label)
        {
            InjectWheel(dy);
            TownHud.DrawShop(s);
            Note(ShopKey(), label);
        }

        private void DrawShopWithKey(TownState s, KeyCode key, string label)
        {
            InjectKey(key);
            TownHud.DrawShop(s);
            Note(ShopKey(), label);
        }

        /// <summary>注入一颗滚轮事件（**只在这一次 OnGUI 调用内有效**：画完立刻把原事件还回去）。</summary>
        private void InjectWheel(float dy)
        {
            Event prev = Event.current;
            var e = new Event
            {
                type = EventType.ScrollWheel,
                mousePosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                delta = new Vector2(0f, dy),
            };
            Event.current = e;
            _restore = prev;
        }

        /// <summary>注入一颗按键事件（`ScrollBegin` 自己接 PageUp/PageDown/Home/End）。</summary>
        private void InjectKey(KeyCode key)
        {
            Event prev = Event.current;
            var e = new Event { type = EventType.KeyDown, keyCode = key };
            Event.current = e;
            _restore = prev;
        }

        private Event _restore;

        /// <summary>记一行读数。<paramref name="key"/> 是"这一页的键"，与 `TownHud.Readings` 的键一致。</summary>
        private void Note(string key, string label)
        {
            if (_restore != null) { Event.current = _restore; _restore = null; }   // 事件还回去（别把 Unity 的 Repaint 换掉）

            TownHud.ScrollReading r;
            if (!TownHud.TryReading(key, out r)) { _lines.Add(label + "｜(这一页没写过读数)"); return; }
            _lines.Add($"{label}｜内容高={r.ContentH:0.#}｜视口高={r.ViewH:0.#}｜可滚={r.Room:0.#}" +
                       $"｜scroll.y={r.ScrollY:0.#}｜偏移={r.ShiftY:0.#}");
            Log.Info("[TownProbe] " + _lines[_lines.Count - 1]);
        }
    }
}
