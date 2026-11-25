using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v189：本轮五项改动的**只读探针**（每项一张表 / 一组配对读数）★
    ///
    /// ── 为什么是**一个新文件**，而不是往 `DebugApi.cs` 里加方法 ──
    /// 同一个工程里有第二条并行改动线正在**反复追加** `DebugApi.cs`（实测 60 秒内写了两次）✗
    /// 两边同时读-改-写同一个 5000 行文件，是"谁最后写谁赢"的经典丢改动场景 ✓
    /// 而这里要加的只是"能被执行到的一段只读代码" ⇒ 独立文件 + 自举组件**零争用**，
    /// 也不碰对方的任何一行 ✓（`DebugApi.cs` 那边只需一行转发就能复用，见文件末尾的说明）
    ///
    /// ── 它怎么被调到 ──
    /// `[RuntimeInitializeOnLoadMethod]` 在 play 模式自动建一个 `ProbeV189` 对象（与
    /// `DebugApi.AutoCreate` 同一个套路），之后桥就可以直接：
    ///   `send ProbeV189 ProbeV189 ProbeDotStack`
    ///   `send ProbeV189 ProbeV189 ProbeRerollPrice`
    ///   `send ProbeV189 ProbeV189 ProbeShopCap`
    ///   `send ProbeV189 ProbeV189 ProbeSwordBalance`
    ///   `send ProbeV189 ProbeV189 ProbeSubBurstGap`
    /// 五个方法都**只读**：只建临时 `RunSession` 并 `Dispose`，不改当前局的任何状态 ✓
    ///
    /// ── 为什么读数必须走"真的施加路径" ──
    /// 往 `PluginDotTable` 里手塞数只能证明"表算得对"，证明不了"插件把层数传进来了" ✗
    /// 所以每一格都是：装 N 层插件 → 真挥一刀 / 真喷一束 / 真炸一次 → 读表 ✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV189 : MonoBehaviour
    {
        /// <summary>play 模式自举（与 `DebugApi.AutoCreate` 同一条纪律）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV189>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV189");
            go.AddComponent<ProbeV189>();
        }

        // ════════════════════════════════════════════════════════════════════
        // 夹具：与自检 `PluginBench` / 探针 `PluginReworkBench` 逐字同一套
        // （属性各 +2、暴击率 0、满水满肥、玩家站原点朝 +X）⇒ 读数可与既有表对照 ✓
        // ════════════════════════════════════════════════════════════════════

        private static RunSession Bench(ulong seed, out PlayerActor p)
        {
            var s = new RunSession();
            s.Start(seed);
            s.StartWave(1);
            s.World.Enemies.Clear();
            p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);
            const float BenchStat = 2f;
            s.Progress.Stats.AddModifier(new StatModifier(StatId.MeleeDamage, BenchStat, ModOp.Add, "probe189"));
            s.Progress.Stats.AddModifier(new StatModifier(StatId.RangedDamage, BenchStat, ModOp.Add, "probe189"));
            s.Progress.Stats.AddModifier(new StatModifier(StatId.BlastDamage, BenchStat, ModOp.Add, "probe189"));
            s.WaterAmmo = RunSession.WaterAmmoMax;
            s.FertAmmo = RunSession.FertAmmoMax;
            s.AimWorldLast = new Vector2(3f, 0f);
            return s;
        }

        private static EnemyActor Put(RunSession s, Vector2 pos, float hp = 1e5f)
        {
            s.World.SpawnEnemy("grunt", pos, 1);
            EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
            e.Pos = pos;
            e.Health.Setup(hp, 0f);
            return e;
        }

        private static PluginAsset Plug(string id)
        {
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                if (TableDatabase.Plugins[i] != null && TableDatabase.Plugins[i].id == id)
                    return TableDatabase.Plugins[i];
            return null;
        }

        /// <summary>装 <paramref name="layers"/> 层某插件（走真的 `WeaponSystem.Install`）。</summary>
        private static int Install(RunSession s, string id, WeaponSlot slot, int layers)
        {
            PluginAsset a = Plug(id);
            if (a == null) return 0;
            int done = 0;
            for (int i = 0; i < layers; i++)
            {
                if (!WeaponSystem.Install(s.Goods, a, slot, out _)) break;
                done++;
            }
            return done;
        }

        /// <summary>表里有几行"这个种类、这个敌人"（① 要回答的就是"是 1 行还是 3 行"）。</summary>
        private static int CountRows(PluginDotTable table, DotKind kind, int enemyId)
        {
            int n = 0;
            for (int i = 0; i < table.Rows.Count; i++)
                if (table.Rows[i].Kind == kind && table.Rows[i].EnemyId == enemyId) n++;
            return n;
        }

        /// <summary>表里半径最大的那块毒区 = 主毒区（子毒区比它小一号）。</summary>
        private static DotRow MainZone(PluginDotTable table)
        {
            DotRow best = default;
            float r = -1f;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DotRow row = table.Rows[i];
                if (row.Kind != DotKind.PoisonZone) continue;
                if (row.Radius > r) { r = row.Radius; best = row; }
            }
            return best;
        }

        private static string Emit(StringBuilder sb)
        {
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        // ════════════════════════════════════════════════════════════════════
        // ① 三种持续效果在"重复拥有"时的叠加读数
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ① **灼烧 / 减速 / 毒区 × 1/2/3 层**：每秒 / 层数 / 行数 / 剩余秒 / 推 1 秒实测掉血。
        ///
        /// ★ 只读数，不改任何行为 ★ —— "该不该叠加"是用户提的**疑问**，
        /// 所以这里只把现状摆出来，改不改由用户决定 ✓
        /// </summary>
        public string ProbeDotStack(string spec)
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.Append("PROBE_DOTSTACK｜同一只敌人、同一件插件装 1/2/3 层（每格一个干净会话，走真的施加路径）\n");
            sb.Append("  口径：每秒 = 表里那一行结算的量｜层 = 该敌人累计层数｜行 = 该敌人这一种类的行数\n");

            sb.Append("  ── 灼烧 hoe_burn（挥一刀 ⇒ PluginPostHit ⇒ ApplyBurn）──\n");
            sb.Append("     伪层数 | 每秒 | 层 | 行 | 剩(s) | 推 1 秒掉血\n");
            for (int layers = 1; layers <= 3; layers++)
            {
                RunSession s = Bench(30301UL + (ulong)layers, out PlayerActor p);
                int installed = Install(s, "hoe_burn", WeaponSlot.Hoe, layers);
                WeaponModifiers m = s.ModsFor(WeaponSlot.Hoe);
                EnemyActor e = Put(s, new Vector2(2f, 0f));
                s.DebugAttackOnce(WeaponSlot.Hoe);
                int rows = CountRows(s.Dots, DotKind.Burn, e.Id);
                float per = s.Dots.BurnPerSecond(e.Id);
                int stacks = s.Dots.BurnStacks(e.Id);
                float remain = s.Dots.BurnRemain(e.Id);
                float hp0 = e.Health.Hp;
                s.Dots.Tick(1f, s.World, p);            // 只推持续表 ⇒ 掉的就是 DoT 本身
                float drop = hp0 - e.Health.Hp;
                sb.Append("     装").Append(installed).Append("层 ⇒ ").Append(per.ToString("0.000")).Append("/秒｜")
                  .Append(stacks).Append(" 层｜").Append(rows).Append(" 行｜")
                  .Append(remain.ToString("0.00")).Append("s｜").Append(drop.ToString("0.0000"))
                  .Append("｜插件层数→").Append(m.BurnLayersPerHit).Append(" 层/命中，每层比 ")
                  .Append(m.BurnRatioPerLayer.ToString("0.00")).Append('\n');
                s.Dispose();
            }

            sb.Append("  ── 减速 can_slow（喷一束 ⇒ PluginPostHit ⇒ ApplySlow）──\n");
            sb.Append("     伪层数 | 每秒 | 层 | 行 | 剩(s) | 移速× | 推 1 秒掉血\n");
            for (int layers = 1; layers <= 3; layers++)
            {
                RunSession s = Bench(30311UL + (ulong)layers, out PlayerActor p);
                int installed = Install(s, "can_slow", WeaponSlot.Can, layers);
                EnemyActor e = Put(s, new Vector2(5f, 0f));
                s.DebugAttackOnce(WeaponSlot.Can);
                int rows = CountRows(s.Dots, DotKind.Slow, e.Id);
                float per = s.Dots.SlowPerSecond(e.Id);
                int stacks = s.Dots.SlowStacks(e.Id);
                float remain = s.Dots.SlowRemain(e.Id);
                float speed = s.Dots.SlowFactor(e.Id);
                float hp0 = e.Health.Hp;
                s.Dots.Tick(1f, s.World, p);
                float drop = hp0 - e.Health.Hp;
                sb.Append("     装").Append(installed).Append("层 ⇒ ").Append(per.ToString("0.000")).Append("/秒｜")
                  .Append(stacks).Append(" 层｜").Append(rows).Append(" 行｜")
                  .Append(remain.ToString("0.00")).Append("s｜×").Append(speed.ToString("0.00"))
                  .Append("｜").Append(drop.ToString("0.0000")).Append('\n');
                s.Dispose();
            }

            sb.Append("  ── 毒区 fert_poison（炸一次 ⇒ AddPoisonZone）──\n");
            sb.Append("     伪层数 | 每块每秒 | 层 | 块数 | 剩(s) | 半径 | 推 1 秒掉血\n");
            for (int layers = 1; layers <= 3; layers++)
            {
                RunSession s = Bench(30321UL + (ulong)layers, out PlayerActor p);
                int installed = Install(s, "fert_poison", WeaponSlot.Fertilizer, layers);
                EnemyActor e = Put(s, new Vector2(3f, 0f));       // 爆点在 (3,0)
                s.DebugAttackOnce(WeaponSlot.Fertilizer);
                DotRow z = MainZone(s.Dots);
                int zones = s.Dots.PoisonZoneCount;
                float hp0 = e.Health.Hp;
                s.Dots.Tick(1f, s.World, p);
                float drop = hp0 - e.Health.Hp;
                sb.Append("     装").Append(installed).Append("层 ⇒ ").Append(z.PerSecond.ToString("0.000")).Append("/秒｜")
                  .Append(z.Stacks).Append(" 层｜").Append(zones).Append(" 块｜")
                  .Append(z.Remain.ToString("0.00")).Append("s｜r").Append(z.Radius.ToString("0.00"))
                  .Append("｜").Append(drop.ToString("0.0000")).Append('\n');
                s.Dispose();
            }

            sb.Append("  读法：层数与每秒**线性叠加**（N 层 = N × 单层每秒）；");
            sb.Append("**行数恒为 1**（同一敌人一行、毒区同一位置一块）、**时长恒为 ")
              .Append(PluginDotTable.BurnSeconds.ToString("0"))
              .Append("s**（再点火只加层不续时）⇒ 即\"数值叠加、次数不叠加\"\n");
            return Emit(sb);
        }

        // ════════════════════════════════════════════════════════════════════
        // ② 刷新价（商店刷新 + 抽卡重抽）
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ② **刷新价**：波次 × 次数 的表 + "基础值随波次涨"的判据。
        /// 外加两个**活会话**读数（HUD 那个单参数入口 / 抽卡重抽），证明显示与扣费同一个数。
        /// </summary>
        public string ProbeRerollPrice(string spec)
        {
            TableDatabase.Load(true);
            TuningDef t = TableDatabase.Tuning;
            var sb = new StringBuilder();
            int[] waves = { 1, 5, 10, 15 };
            sb.Append("PROBE_REROLLPRICE｜刷新价 = round( 基础值(波次) × 2^次数 ) − 道具减免，下限 1\n");
            sb.Append("  基础值(波次) = round(").Append(t.shop.rerollBase).Append(" × (1+")
              .Append(t.shop.priceWaveStep.ToString("0.###")).Append(")^(波-1))｜倍数封顶 ")
              .Append(ShopSystem.RerollMaxMult).Append("｜商店与抽卡**同一个式子** ✓\n");

            sb.Append("  [商店刷新] base=").Append(t.shop.rerollBase).Append('\n');
            sb.Append("    波次 | 基础值 | 第1次 | 第2次 | 第3次 | 第4次\n");
            for (int i = 0; i < waves.Length; i++)
            {
                int w = waves[i];
                sb.Append("    ").Append(w.ToString().PadLeft(4)).Append(" | ")
                  .Append(ShopSystem.RerollBaseAt(t, w).ToString().PadLeft(6));
                for (int n = 0; n < 4; n++)
                    sb.Append(" | ").Append(ShopSystem.RerollPrice(t, t.shop.rerollBase, w, n, 0).ToString().PadLeft(5));
                sb.Append('\n');
            }
            sb.Append("  [抽卡重抽] base=").Append(t.levelReward.rerollBasePrice).Append("（同一个 RerollPrice）\n");
            sb.Append("    波次 | 基础值 | 第1次 | 第2次 | 第3次 | 第4次\n");
            for (int i = 0; i < waves.Length; i++)
            {
                int w = waves[i];
                int basis = Mathf.Max(1, Mathf.RoundToInt(t.levelReward.rerollBasePrice * t.shop.WavePriceMult(w)));
                sb.Append("    ").Append(w.ToString().PadLeft(4)).Append(" | ").Append(basis.ToString().PadLeft(6));
                for (int n = 0; n < 4; n++)
                    sb.Append(" | ").Append(ShopSystem.RerollPrice(t, t.levelReward.rerollBasePrice, w, n, 0).ToString().PadLeft(5));
                sb.Append('\n');
            }

            int b1 = ShopSystem.RerollBaseAt(t, 1), b10 = ShopSystem.RerollBaseAt(t, 10);
            int c1 = ShopSystem.RerollPrice(t, t.shop.rerollBase, 1, 0, 0);
            int c10 = ShopSystem.RerollPrice(t, t.shop.rerollBase, 10, 0, 0);
            sb.Append("  判据（首次刷新价）：第 1 波 ").Append(c1).Append(" 金 → 第 10 波 ").Append(c10)
              .Append(" 金（×").Append(c1 > 0 ? (c10 / (float)c1).ToString("0.00") : "—").Append("）｜基础值 ")
              .Append(b1).Append(" → ").Append(b10)
              .Append(c10 > c1 ? " ⇒ **基础值随波次涨** ✓\n" : " ⇒ **没涨** ✗\n");

            // 活会话：HUD 走的是**单参数**入口 `s.Shop.RerollCost(s.T)` ⇒ 与扣费必须同一个数
            RunSession s = new RunSession();
            s.Start(424242UL);
            s.WaveIndex = 10;
            s.ForceShopForDebug();
            int hud = s.Shop.RerollCost(s.T);
            int expect = ShopSystem.RerollPrice(t, t.shop.rerollBase, 10, 0, 0);
            int reward = s.RewardRerollCost;
            int rewardExpect = ShopSystem.RerollPrice(t, t.levelReward.rerollBasePrice, 10, 0, 0);
            sb.Append("  活会话（波次 10、进店后 HUD 入口 `Shop.RerollCost(T)`）读到 ").Append(hud)
              .Append(" 金｜式子值 ").Append(expect)
              .Append(hud == expect ? " ⇒ 显示与扣费同一个数 ✓\n" : " ⇒ **分叉** ✗\n");
            sb.Append("  活会话 抽卡重抽（波次 10、第 1 次）：").Append(reward).Append(" 金｜式子值 ")
              .Append(rewardExpect).Append(reward == rewardExpect ? " ⇒ 同一套 ✓\n" : " ⇒ **不同套** ✗\n");
            s.Dispose();
            return Emit(sb);
        }

        // ════════════════════════════════════════════════════════════════════
        // ③ 满上限 ⇒ 货架不再出现
        // ════════════════════════════════════════════════════════════════════

        /// <summary>数 <paramref name="opens"/> 次刷新里每个插件 id 出现在货架上的次数。</summary>
        private static System.Collections.Generic.Dictionary<string, int> CountAllPlugins(
            RunSession s, int opens, out int slots, out int plugSlots)
        {
            var map = new System.Collections.Generic.Dictionary<string, int>();
            slots = 0; plugSlots = 0;
            for (int i = 0; i < opens; i++)
            {
                s.Shop.Refresh(s);
                for (int k = 0; k < s.Shop.Offers.Count; k++)
                {
                    ShopOffer o = s.Shop.Offers[k];
                    slots++;
                    if (o.Kind != OfferKind.Plugin || o.Plugin == null) continue;
                    plugSlots++;
                    map.TryGetValue(o.Plugin.id, out int n);
                    map[o.Plugin.id] = n + 1;
                }
            }
            return map;
        }

        /// <summary>数 <paramref name="opens"/> 次刷新里每个道具 id 出现的次数。</summary>
        private static System.Collections.Generic.Dictionary<string, int> CountAllItems(RunSession s, int opens)
        {
            var map = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < opens; i++)
            {
                s.Shop.Refresh(s);
                for (int k = 0; k < s.Shop.Offers.Count; k++)
                {
                    ShopOffer o = s.Shop.Offers[k];
                    if (o.Kind != OfferKind.Item || o.Item == null) continue;
                    map.TryGetValue(o.Item.Id, out int n);
                    map[o.Item.Id] = n + 1;
                }
            }
            return map;
        }

        private static string TopOf(System.Collections.Generic.Dictionary<string, int> map, out int count)
        {
            string best = null; count = 0;
            foreach (var kv in map)
                if (kv.Value > count) { best = kv.Key; count = kv.Value; }
            return best;
        }

        /// <summary>
        /// ③ **满上限之后那件商品还会不会上货架**：同一随机种子、连开 N 次商店。
        ///
        /// ★ 为什么不是"随便挑一件插件来数" ★
        /// 第一版就是这么写的，结果是 `hoe_burn` 在 40 个货位里出现 **0** 次（12 件插件里抽到
        /// 某一特定件本来就只有 1/12 × 14 次 ≈ 1 次的机会）⇒ "满上限后 0 次"与"本来就 0 次"
        /// 分不开，读数**空转** ✗
        /// 现在分两步：① 先在**未满**的局里把每一件的出现次数都数出来，挑**出现最多的那一件**
        /// 当被试（它一定 &gt; 0）；② 把它装满再数同一件 ⇒ 期望 0 ✓
        /// 这样"未满会出 / 满了不出"是**同一件商品**的前后对比，不会被稀有度稀释 ✓
        /// </summary>
        public string ProbeShopCap(string spec)
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            const int opens = 200;
            sb.Append("PROBE_SHOPCAP｜同一随机种子、连开 ").Append(opens)
              .Append(" 次商店（每次 ").Append(TableDatabase.Tuning.shop.slots).Append(" 个货位）\n");

            // ── 插件 ──
            RunSession s0 = new RunSession();
            s0.Start(90210UL);
            s0.WaveIndex = 6;
            var plugMap = CountAllPlugins(s0, opens, out int slots0, out int plug0);
            s0.Dispose();
            string plugId = TopOf(plugMap, out int plugBefore);
            if (plugId != null)
            {
                PluginAsset asset = Plug(plugId);
                int cap = asset != null ? Mathf.Max(1, asset.maxStack) : 3;
                RunSession c = new RunSession();
                c.Start(90210UL);
                c.WaveIndex = 6;
                int installed = Install(c, plugId, FirstSlotOf(asset), cap);
                PluginInstance pi = c.Goods.Find(asset, FirstSlotOf(asset));
                var cappedMap = CountAllPlugins(c, opens, out int slots1, out int plug1);
                cappedMap.TryGetValue(plugId, out int plugAfter);
                sb.Append("  插件 ").Append(plugId).Append("（maxStack ").Append(cap).Append("）：\n")
                  .Append("    未满：").Append(opens).Append(" 次刷新共 ").Append(slots0).Append(" 个货位（插件 ")
                  .Append(plug0).Append(" 个）⇒ 它出现 **").Append(plugBefore).Append("** 次")
                  .Append("（12 件里最多的一件）\n")
                  .Append("    装满 ").Append(installed).Append(" 件 = ").Append(pi != null ? pi.Stacks : 0)
                  .Append(" 层：").Append(opens).Append(" 次刷新共 ").Append(slots1).Append(" 个货位（插件 ")
                  .Append(plug1).Append(" 个）⇒ 它出现 **").Append(plugAfter).Append("** 次")
                  .Append(plugAfter == 0 && plugBefore > 0 ? " ⇒ 满上限后彻底不再出现 ✓" : " ⇒ **仍会出现** ✗")
                  .Append('\n');
                c.Dispose();
            }

            // ── 道具 ──
            RunSession a0 = new RunSession();
            a0.Start(90210UL);
            a0.WaveIndex = 6;
            var itemMap = CountAllItems(a0, opens);
            a0.Dispose();
            string itemId = TopOf(itemMap, out int itemBefore);
            if (itemId != null)
            {
                ItemDef def = null;
                for (int i = 0; i < TableDatabase.Items.Length; i++)
                    if (TableDatabase.Items[i] != null && TableDatabase.Items[i].Id == itemId)
                    { def = TableDatabase.Items[i]; break; }
                RunSession b = new RunSession();
                b.Start(90210UL);
                b.WaveIndex = 6;
                b.ItemStacks[itemId] = Mathf.Max(1, def != null ? def.MaxStack : 1);
                var afterMap = CountAllItems(b, opens);
                afterMap.TryGetValue(itemId, out int itemAfter);
                sb.Append("  道具 ").Append(itemId).Append("（MaxStack ")
                  .Append(def != null ? Mathf.Max(1, def.MaxStack) : 1).Append("）：")
                  .Append("未满时出现 **").Append(itemBefore).Append("** 次｜叠满后出现 **")
                  .Append(itemAfter).Append("** 次")
                  .Append(itemAfter == 0 && itemBefore > 0 ? " ⇒ 满上限后彻底不再出现 ✓" : " ⇒ **仍会出现** ✗")
                  .Append('\n');
                b.Dispose();
            }

            // ── 种子：包里已有同种 3 级 ──
            if (TableDatabase.Seeds.Length > 0)
            {
                SeedDef d = TableDatabase.Seeds[0];
                RunSession c2 = new RunSession();
                c2.Start(90210UL);
                c2.WaveIndex = 6;
                bool added = c2.Bag.AddSeed(d, null, d.RarityTier, out _, SeedSlot.MaxLevel);
                bool capped = ShopSystem.SeedAtCap(c2, d);
                sb.Append("  种子 ").Append(d.Id).Append("：塞一件 ").Append(SeedSlot.MaxLevel).Append(" 级进包=")
                  .Append(added).Append("｜判据 SeedAtCap=").Append(capped)
                  .Append(capped ? " ⇒ 该种子不再上货架 ✓" : " ✗").Append('\n');
                c2.Dispose();
            }
            return Emit(sb);
        }

        /// <summary>这件插件适用哪把武器（`allowedWeapons[0]`，本工程 12 件全是单元素）。</summary>
        private static WeaponSlot FirstSlotOf(PluginAsset a)
        {
            if (a == null || a.allowedWeapons == null || a.allowedWeapons.Length == 0) return WeaponSlot.Hoe;
            return (WeaponSlot)a.allowedWeapons[0];
        }

        // ════════════════════════════════════════════════════════════════════
        // ④ 剑气 50% / 多次挥舞 ×2/3 + 不重复结算
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ④ **0/1/2/3 层每刀伤害表** + 两组"不重复结算"的配对读数。
        ///   (a) 近战框里的敌人：空手一刀 M ⇒ 装 N 道剑气后**还是 M**（剑气跳过他）
        ///   (b) 只在剑气框里的敌人：空手 0 ⇒ 装 N 道剑气后 **恰好 1 份剑气伤害**（不是 N 份）
        /// </summary>
        public string ProbeSwordBalance(string spec)
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.Append("PROBE_SWORDBALANCE｜剑气 = 这一刀 ×").Append(WeaponSystem.SwordWaveDamageRatio.ToString("0.00"))
              .Append("｜多次挥舞 = 每层 ×").Append(WeaponSystem.DoubleStrikeDamageRatioPerLayer.ToString("0.###"))
              .Append("（累计相乘）\n");

            float bare;
            float radius;
            {
                RunSession s = Bench(40401UL, out PlayerActor _);
                EnemyActor e = Put(s, new Vector2(2f, 0f));
                radius = e.Radius;
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                bare = hp0 - e.Health.Hp;
                s.Dispose();
            }
            float safe = Mathf.Max(1e-6f, bare);
            sb.Append("  空手一刀（近战框内 2.0m）= ").Append(bare.ToString("0.0000"))
              .Append("（敌人半径 ").Append(radius.ToString("0.00")).Append("）\n");

            PluginAsset ds = Plug("hoe_doublestrike");
            int dsCap = ds != null ? Mathf.Max(1, ds.maxStack) : 2;
            sb.Append("  ── 多次挥舞 hoe_doublestrike（数据 maxStack=").Append(dsCap).Append("）──\n");
            sb.Append("     层 | 刀数 | 每刀伤害 | 每刀/空手 | 一次挥击总伤害 | 总/空手 | 来源\n");
            for (int layers = 0; layers <= 3; layers++)
            {
                if (layers > dsCap)
                {
                    float pure = WeaponSystem.DoubleStrikeDamage(bare, layers);
                    sb.Append("     ").Append(layers).Append(" | ").Append(1 + layers).Append(" | ")
                      .Append(pure.ToString("0.0000")).Append(" | ").Append((pure / safe).ToString("0.000"))
                      .Append(" | ").Append((pure * (1 + layers)).ToString("0.0000")).Append(" | ")
                      .Append((pure * (1 + layers) / safe).ToString("0.000"))
                      .Append(" | 纯算式（装不满，maxStack=").Append(dsCap).Append("）\n");
                    continue;
                }
                RunSession s = Bench(40411UL + (ulong)layers, out PlayerActor _);
                if (layers > 0) Install(s, "hoe_doublestrike", WeaponSlot.Hoe, layers);
                EnemyActor e = Put(s, new Vector2(2f, 0f));
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);          // 只量**第一刀**
                float per = hp0 - e.Health.Hp;
                s.DebugAdvanceAttack(0.5f);                 // 把余下的刀推完
                float total = hp0 - e.Health.Hp;
                sb.Append("     ").Append(layers).Append(" | ").Append(1 + layers).Append(" | ")
                  .Append(per.ToString("0.0000")).Append(" | ").Append((per / safe).ToString("0.000"))
                  .Append(" | ").Append(total.ToString("0.0000")).Append(" | ")
                  .Append((total / safe).ToString("0.000")).Append(" | 实测\n");
                s.Dispose();
            }

            // 落点 (1.0, 3.0)：距玩家 3.16m（≤ range 3.2 + 半径），但角度 71.6° > 扇形 55° ⇒
            //   近战打不到；|侧偏| 3.0 ≤ 近战框半宽 + 半径 ⇒ 剑气的矩形框打得到 ✓
            Vector2 corner = new Vector2(1.0f, 3.0f);
            float bareCorner, waveDmg;
            {
                RunSession s = Bench(40421UL, out PlayerActor _);
                EnemyActor e = Put(s, corner);
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                s.DebugAdvanceAttack(0.5f);
                bareCorner = hp0 - e.Health.Hp;
                s.Dispose();
            }
            {
                RunSession s = Bench(40422UL, out PlayerActor _);
                Install(s, "hoe_swordwave", WeaponSlot.Hoe, 1);
                EnemyActor e = Put(s, corner);
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                s.DebugAdvanceAttack(0.5f);
                waveDmg = hp0 - e.Health.Hp;
                s.Dispose();
            }
            float safeWave = Mathf.Max(1e-6f, waveDmg);
            sb.Append("  ── 剑气（1 道，只在剑气框里的敌人 @(1.0,3.0)）──\n")
              .Append("     空手 = ").Append(bareCorner.ToString("0.0000"))
              .Append("（近战打不到 ⇒ 应为 0）｜装 1 道剑气 = ").Append(waveDmg.ToString("0.0000"))
              .Append("｜应为 一刀 × 0.5 = ").Append((bare * WeaponSystem.SwordWaveDamageRatio).ToString("0.0000"))
              .Append("｜实测/空手一刀 = ").Append((waveDmg / safe).ToString("0.000")).Append('\n');

            sb.Append("  ── (a) **近战框内**的敌人：近战 + N 道剑气是否重复计算 ──\n");
            sb.Append("     剑气道数 | 总掉血 | 空手一刀 | 比值 | 判据\n");
            for (int waves = 1; waves <= 3; waves++)
            {
                RunSession s = Bench(40431UL + (ulong)waves, out PlayerActor _);
                Install(s, "hoe_swordwave", WeaponSlot.Hoe, waves);
                EnemyActor e = Put(s, new Vector2(2f, 0f));
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                s.DebugAdvanceAttack(0.5f);
                float total = hp0 - e.Health.Hp;
                float ratio = total / safe;
                sb.Append("     ").Append(waves).Append(" 道 | ").Append(total.ToString("0.0000"))
                  .Append(" | ").Append(bare.ToString("0.0000")).Append(" | ").Append(ratio.ToString("0.000"))
                  .Append(" | ").Append(ratio <= 1.02f ? "只结算一份（内层打过 ⇒ 剑气跳过）✓" : "**重复计算** ✗")
                  .Append('\n');
                s.Dispose();
            }

            sb.Append("  ── (b) **只在剑气框里**的敌人：N 道剑气是否对同一敌人多次结算 ──\n");
            sb.Append("     剑气道数 | 总掉血 | 1 份剑气 | 份数 | 判据\n");
            for (int waves = 1; waves <= 3; waves++)
            {
                RunSession s = Bench(40441UL + (ulong)waves, out PlayerActor _);
                Install(s, "hoe_swordwave", WeaponSlot.Hoe, waves);
                EnemyActor e = Put(s, corner);
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                s.DebugAdvanceAttack(0.5f);
                float total = hp0 - e.Health.Hp;
                float parts = total / safeWave;
                sb.Append("     ").Append(waves).Append(" 道 | ").Append(total.ToString("0.0000"))
                  .Append(" | ").Append(waveDmg.ToString("0.0000")).Append(" | ")
                  .Append(parts.ToString("0.00")).Append(" | ")
                  .Append(parts <= 1.02f ? "只结算一份（多道共享命中名单）✓" : "**打了两遍以上** ✗")
                  .Append('\n');
                s.Dispose();
            }
            return Emit(sb);
        }

        // ════════════════════════════════════════════════════════════════════
        // ⑤ 子爆间距 + 子爆也铺毒
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⑤ **子爆与主爆的间距**（改前 0.90 → 改后 1.75 个主爆半径）+ 子爆也铺毒 ⇒ 块数 = 主 1 + 子 N。
        /// 半径与圆心距全部从 `PluginDotTable` 的**实测行**反推（不在探针里另算一遍）。
        /// </summary>
        public string ProbeSubBurstGap(string spec)
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.Append("PROBE_SUBBURSTGAP｜子爆落点距主爆心 = 主爆半径 × ")
              .Append(WeaponSystem.SubBurstRingRatio.ToString("0.00"))
              .Append("（改前 0.90）\n");
            sb.Append("  几何：主爆 r=R、子爆 r=0.7R ⇒ 不重叠要圆心距 ≥ 1.7R；")
              .Append("改前 0.9R（子爆整个压在主爆里），改后 1.75R（落在主爆外面）\n");

            RunSession s = Bench(50501UL, out PlayerActor p);
            int subLayers = Install(s, "fert_subburst", WeaponSlot.Fertilizer, 2);
            int poiLayers = Install(s, "fert_poison", WeaponSlot.Fertilizer, 1);
            WeaponModifiers m = s.ModsFor(WeaponSlot.Fertilizer);
            EnemyActor e = Put(s, new Vector2(3f, 0f));          // 爆点在 (3,0)
            s.DebugAttackOnce(WeaponSlot.Fertilizer);

            sb.Append("  实测（子爆 ").Append(subLayers).Append(" 层 + 毒素 ").Append(poiLayers)
              .Append(" 层，一次肥料爆炸）\n")
              .Append("    子爆个数 = ").Append(m.SubBurstCount)
              .Append("｜子爆半径比 = ").Append(m.SubBurstRadiusRatio.ToString("0.00"))
              .Append("｜毒区块数 = ").Append(s.Dots.PoisonZoneCount)
              .Append("（应为 主 1 + 子 ").Append(m.SubBurstCount).Append(" = ").Append(1 + m.SubBurstCount)
              .Append("）\n");

            DotRow main = MainZone(s.Dots);
            float blastR = WeaponSystem.PoisonRadiusRatio > 0f ? main.Radius / WeaponSystem.PoisonRadiusRatio : 0f;
            sb.Append("    主毒区半径 = ").Append(main.Radius.ToString("0.000"))
              .Append(" ⇒ 主爆半径 R = ").Append(blastR.ToString("0.000")).Append('\n');
            sb.Append("    逐块：\n");
            for (int i = 0; i < s.Dots.Rows.Count; i++)
            {
                DotRow r = s.Dots.Rows[i];
                if (r.Kind != DotKind.PoisonZone) continue;
                bool isMain = Mathf.Abs(r.Radius - main.Radius) < 1e-4f
                              && (r.Center - main.Center).sqrMagnitude < 1e-6f;
                float d = Vector2.Distance(r.Center, main.Center);
                sb.Append("      毒区@(").Append(r.Center.x.ToString("0.00")).Append(',')
                  .Append(r.Center.y.ToString("0.00")).Append(") r=").Append(r.Radius.ToString("0.000"))
                  .Append(" 每秒=").Append(r.PerSecond.ToString("0.000"))
                  .Append(" 层=").Append(r.Stacks)
                  .Append(" 距主爆心=").Append(d.ToString("0.000")).Append("m（=R×")
                  .Append(blastR > 1e-4f ? (d / blastR).ToString("0.00") : "—").Append("）")
                  .Append(isMain ? "  ← 主" : "  ← 子").Append('\n');
            }
            sb.Append("    子毒区半径 = 主毒区 × 子爆半径比 = ").Append(main.Radius.ToString("0.000")).Append(" × ")
              .Append(Mathf.Max(0f, m.SubBurstRadiusRatio).ToString("0.00")).Append(" = ")
              .Append(WeaponSystem.SubPoisonRadius(blastR, m).ToString("0.000")).Append('\n');
            float hp0 = e.Health.Hp;
            sb.Append("    推 1 秒：站在爆点上的敌人合计掉 ");
            s.Dots.Tick(1f, s.World, p);
            sb.Append((hp0 - e.Health.Hp).ToString("0.000"))
              .Append("（多块毒区各自结算）\n");
            s.Dispose();
            return Emit(sb);
        }

        /// <summary>
        /// ⑥ **自充 12 → 10**：装 1 层 → 清空水箱 → 跑 2 秒 / 3 秒，看回了多少点。
        /// 判据：2 秒 = 2 × amount（改前 24，改后 **20**）✓
        /// </summary>
        public string ProbeAutoCharge(string spec)
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            PluginAsset ac = Plug("can_autocharge");
            RunSession s = Bench(60601UL, out PlayerActor _);
            string note;
            if (ac != null && WeaponSystem.Install(s.Goods, ac, WeaponSlot.Can, out _))
                note = "已装 can_autocharge（表内 amount=" + ac.amount.ToString("0.##") + "）";
            else
                note = "装不上 can_autocharge（表里没有这一行？）";
            WeaponModifiers m = s.ModsFor(WeaponSlot.Can);
            sb.Append("PROBE_AUTOCHARGE｜自充回水速率（表值 → 聚合 → 实测）\n");
            sb.Append("  ").Append(note)
              .Append("｜聚合 AutoChargePerSecond = ").Append(m.AutoChargePerSecond.ToString("0.###"))
              .Append("/秒｜层数 = ").Append(m.AutoChargeLayers).Append('\n');
            s.WaterAmmo = 0;
            var idle = new FrameInput { SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1 };
            int ticks2 = Mathf.RoundToInt(2f / GameClock.FixedDt);
            for (int i = 0; i < ticks2; i++) s.FixedTick(GameClock.FixedDt, idle);
            int w2 = s.WaterAmmo;
            int ticks1 = Mathf.RoundToInt(1f / GameClock.FixedDt);
            for (int i = 0; i < ticks1; i++) s.FixedTick(GameClock.FixedDt, idle);
            int w3 = s.WaterAmmo;
            sb.Append("  水箱 0 → 2.0s = ").Append(w2).Append(" 点（改前 2 秒回 24，改后应 **20**）\n")
              .Append("  再 1.0s → 共 3.0s = ").Append(w3).Append(" 点（应 30）\n")
              .Append("  实测速率 = ").Append((w2 / 2f).ToString("0.0")).Append(" 点/秒")
              .Append(w2 == 20 ? " ⇒ 12 → 10 已生效 ✓\n" : "（对照 24 = 仍是 12/秒）\n");
            s.Dispose();
            return Emit(sb);
        }
    }
}
