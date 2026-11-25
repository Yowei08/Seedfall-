using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v214：**只读探针 —— 主城解锁表有没有真的接进战斗刷新池** ★★
    ///
    /// ── 它为什么是一个组件，而不是自动入口 ──
    ///   它**没有** `[RuntimeInitializeOnLoadMethod]`、**没有** `Awake/Start` ——
    ///   必须在场景里显式建一个带本组件的空物体、再由桥调 <see cref="Audit"/> 才跑 ✓
    ///   这样它不会在任何人跑游戏/无头构建时自己动一下（探针不该改变被观测的东西）✓
    ///
    /// ── 它验什么（对应本轮验收 ①②③）──
    ///   ① 三类的**候选池大小**（`ShopSystem.ItemCandidates/PluginCandidates/SeedCandidates`）：
    ///      老货多少 / 解锁后多少，以及**两者的差**恰好等于买下的件数 ✓
    ///   ② **同一个 id 买前/买后各查一次**：未买 ⇒ 池里没有它；买了 ⇒ 池里有它 ✓
    ///   ③ 抽样层：真的掷 <see cref="Refreshes"/> 次刷新 —— 未买时它**一次都不出现**、
    ///      买后**出现** ⇒ 证明过滤落在"抽样之前"（而不只是池子好看）✓
    ///
    /// ── 它碰了什么、没碰什么 ──
    ///   只读 `TableDatabase` / 只买 `TownUnlocks`（花的是主城金币，钱靠卖仓里的 town_crystal 造）；
    ///   **不写任何 `Data/*.json`、不写盘、不改存档** —— 买下来的是运行期静态，退出 play 即清空 ✓
    /// </summary>
    public sealed class ProbeV214UnlockPool : MonoBehaviour
    {
        /// <summary>抽样次数（每次刷新 4 个货位 ⇒ 400 次 = 1600 个货位）。</summary>
        private const int Refreshes = 400;

        /// <summary>被查的三件（都是**上架**的新货：未买 ⇒ 应当完全刷不出来）。</summary>
        /// <remarks>★ v252：`ProbeItem` 从 `clay_trowel` 换成 `plank_shoes` —— 前者是去重时删掉的
        /// 重复道具（陶铲与磨刀石完全同效），探针拿一个不存在的 id 读到的只会是 false。</remarks>
        private const string ProbeItem = "plank_shoes";     // 道具 · 240 金
        private const string ProbePlugin = "hoe_edgewave";  // 插件 · 216 金
        private const string ProbeSeed = "dusk_berry";      // 植物 · 720 金

        /// <summary>跑一次，返回**可直接贴进报告**的多行文本（同时打一份到控制台）。</summary>
        public string Audit()
        {
            var sb = new StringBuilder(2048);
            TableDatabase.Load();

            sb.Append("[PROBE v214] 主城解锁表 → 战斗刷新候选池\n");

            // ── ⓪ 门没生效时的那一面（`fail-open` 的现场）────────────────────
            //   `TownUnlocks` 是运行期静态：域重载后、Load() 之前，它对一切 id 返回 true。
            //   本探针**先报这一刻的读数**，再 Load() —— 两种现场都能留证 ✓
            if (!TownUnlocks.Ready)
            {
                sb.Append("⓪ 门未生效（表还没载入）：IsUnlocked(\"").Append(ProbePlugin).Append("\")=")
                  .Append(TownUnlocks.IsUnlocked(ProbePlugin))
                  .Append(" ⇒ 这就是那个已知的 fail-open（全解锁），本实现在这一刻**不设防**：\n");
                PoolLine(sb, "   此刻候选池");
                TownUnlocks.Load();
                sb.Append("   ⇒ 载入后：").Append(TownUnlocks.Describe()).Append('\n');
            }
            else
            {
                sb.Append("⓪ 门已生效（主城 Awake 已载入表）：").Append(TownUnlocks.Describe()).Append('\n');
            }
            if (!TownUnlocks.Ready)
            {
                sb.Append("!! 解锁表载入失败：").Append(TownUnlocks.LastError)
                  .Append(" ⇒ 无法继续（池子会停在“不设防”那一面）\n");
                Log.Info(sb.ToString());
                return sb.ToString();
            }

            // ── ① 买前：候选池 = 老货 ─────────────────────────────────────
            sb.Append("① 买前（表已生效 · 一件没买）\n");
            PoolLine(sb, "   候选池");
            sb.Append("   表内共 道具 ").Append(TableDatabase.Items.Length)
              .Append(" / 插件 ").Append(TableDatabase.Plugins.Length)
              .Append(" / 植物 ").Append(TableDatabase.Seeds.Length).Append('\n');

            // ── ② 买前：同一个 id 在不在池里 + 抽样出现几次 ────────────────
            int hitItem, hitPlugin, hitSeed;
            SampleHits(out hitItem, out hitPlugin, out hitSeed);
            sb.Append("② 买前逐件（同一个 id 买前一次、买后一次）\n");
            RowLine(sb, "道具", ProbeItem, hitItem);
            RowLine(sb, "插件", ProbePlugin, hitPlugin);
            RowLine(sb, "植物", ProbeSeed, hitSeed);

            // ── ③ 买下这三件（钱靠卖仓里的 town_crystal 造，不碰存档）─────
            sb.Append("③ 买下这三件（主城金币买的是“从此能在战斗里刷出来”）\n");
            sb.Append("   ").Append(Buy(ProbeItem)).Append('\n');
            sb.Append("   ").Append(Buy(ProbePlugin)).Append('\n');
            sb.Append("   ").Append(Buy(ProbeSeed)).Append('\n');

            // ── ④ 买后：池子 +1，抽样真的出现 ───────────────────────────
            sb.Append("④ 买后（同一次运行内）\n");
            PoolLine(sb, "   候选池");
            SampleHits(out hitItem, out hitPlugin, out hitSeed);
            RowLine(sb, "道具", ProbeItem, hitItem);
            RowLine(sb, "插件", ProbePlugin, hitPlugin);
            RowLine(sb, "植物", ProbeSeed, hitSeed);

            Log.Info(sb.ToString());
            return sb.ToString();
        }

        /// <summary>把自己从场景里删掉（探针不该在别人的场景里留垃圾）。</summary>
        public void DestroySelf()
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        // ══════════════════════════════════════════════════════════════════
        // 读数
        // ══════════════════════════════════════════════════════════════════

        /// <summary>一行池子大小（"X/Y" = 现在能刷出几件 / 表里共几件）。</summary>
        private static void PoolLine(StringBuilder sb, string tag)
        {
            sb.Append(tag)
              .Append("：道具 ").Append(ShopSystem.ItemCandidates().Length).Append('/').Append(TableDatabase.Items.Length)
              .Append("｜插件 ").Append(ShopSystem.PluginCandidates().Length).Append('/').Append(TableDatabase.Plugins.Length)
              .Append("｜植物 ").Append(ShopSystem.SeedCandidates().Length).Append('/').Append(TableDatabase.Seeds.Length)
              .Append("（门=").Append(TownUnlocks.Ready ? "生效" : "未生效").Append("）\n");
        }

        /// <summary>一行"这一件在不在池里 + N 次刷新里出现几次"。</summary>
        private static void RowLine(StringBuilder sb, string kind, string id, int hits)
        {
            bool inPool = InPool(kind, id);
            sb.Append("   ").Append(kind).Append(' ').Append(id.PadRight(14))
              .Append(" 在池里=").Append(inPool ? "是" : "否")
              .Append("｜").Append(Refreshes).Append(" 次刷新里出现 ").Append(hits).Append(" 次")
              .Append("｜IsUnlocked=").Append(TownUnlocks.IsUnlocked(id)).Append('\n');
        }

        private static bool InPool(string kind, string id)
        {
            if (kind == "道具")
            {
                foreach (ItemDef it in ShopSystem.ItemCandidates()) if (it != null && it.Id == id) return true;
                return false;
            }
            if (kind == "插件")
            {
                foreach (PluginAsset p in ShopSystem.PluginCandidates()) if (p != null && p.id == id) return true;
                return false;
            }
            foreach (SeedDef sd in ShopSystem.SeedCandidates()) if (sd != null && sd.Id == id) return true;
            return false;
        }

        /// <summary>真的掷 <see cref="Refreshes"/> 次刷新，数三件各自在货架上出现了几次。</summary>
        private static void SampleHits(out int item, out int plugin, out int seed)
        {
            item = plugin = seed = 0;
            var s = new RunSession();
            s.Start(90210UL);
            s.WaveIndex = 6;
            for (int i = 0; i < Refreshes; i++)
            {
                s.Shop.Refresh(s);
                for (int k = 0; k < s.Shop.Offers.Count; k++)
                {
                    ShopOffer o = s.Shop.Offers[k];
                    if (o.Kind == OfferKind.Item && o.Item != null && o.Item.Id == ProbeItem) item++;
                    else if (o.Kind == OfferKind.Plugin && o.Plugin != null && o.Plugin.id == ProbePlugin) plugin++;
                    else if (o.Kind == OfferKind.Seed && o.Seed != null && o.Seed.Id == ProbeSeed) seed++;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 买（主城金币那条真路，不绕开任何检查）
        // ══════════════════════════════════════════════════════════════════

        private static string Buy(string id)
        {
            int idx = TownUnlocks.IndexOf(id);
            if (idx < 0) return id + "：解锁表里**没有**这一条（应当有 ⇒ 表与数据对不上）✗";
            TownUnlockDef d = TownUnlocks.At(idx);
            if (!EnsureCoins(d.price)) return $"{id}：凑不够金币（需要 {d.price}）✗";
            bool ok = TownUnlocks.TryUnlock(idx);
            return ok
                ? $"{id}（{d.price} 金）买成功 ⇒ {TownUnlocks.LastLine}"
                : $"{id}：买失败 —— {TownUnlocks.LastError} ✗";
        }

        /// <summary>
        /// 造够金币。主城金币的公开入口只有"卖仓里的成品种子"与"地块产出"两条，
        /// 这里走**卖**那条：`AddSeed` + `SellSeed`（两者都是既有公开 API，不碰存档）✓
        /// </summary>
        private static bool EnsureCoins(long need)
        {
            TownState st = TownState.Ensure();
            for (int i = 0; i < 8 && st.Coins < need; i++)
            {
                int added = st.AddSeed(7, 40);      // 7 = town_crystal（售价最高的一档）
                if (added <= 0) break;
                st.SellSeed(7, added);
            }
            return st.Coins >= need;
        }
    }
}
