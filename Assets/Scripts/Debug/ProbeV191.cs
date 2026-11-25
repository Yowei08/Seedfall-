using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v191 只读探针 ★ —— 回答用户五项需求"到底成没成"，每项打印**读数**而不是结论。
    ///
    /// ★ 它为什么存在 ★
    ///   这些读数要"可复现地打一次给人看"：1000 次收割的触发率、暴击比、倍率对比、
    ///   面板两行的原文、抽卡池的张数 —— 全都是**数字**，不是"我觉得对"✓
    ///   而自检只给 PASS/FAIL；探针给的是"实际是多少"（判据错了的时候，这两者不一样）✓
    ///
    /// ★ 它是只读的，而且默认不跑 ★
    ///   `Library/yowei_v191_probe.on` 存在时才在进 Play 时自动执行一次 ——
    ///   删掉那个文件就彻底静默（不需要改代码、不需要重编译）✓
    ///   它**不写任何工程文件**：全部读表 + 内存里造世界，跑完就地销毁 ✓
    ///
    /// ★ 为什么不用 batch mode 跑 ★
    ///   工程已被编辑器独占（`Temp/UnityLockfile`），再起一个 `-batchmode` 实例会被 Unity 拒绝 ✗
    ///   所以探针挂在 Play 入口上，用编辑器自己的 domain 跑 ✓
    /// </summary>
    public static class ProbeV191
    {
        /// <summary>开关文件（在 <工程>/Library/ 下）。存在 ⇒ 进 Play 时跑一次。</summary>
        private static string FlagPath
            => Path.Combine(Path.Combine(Application.dataPath, ".."), "Library", "yowei_v191_probe.on");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!File.Exists(FlagPath)) return;
            Log.Info("PROBEV191 BEGIN");
            try
            {
                Log.Info("\n" + Run());
            }
            catch (Exception e)
            {
                // 探针自己崩了也要看得见 —— 否则"没有输出"会被读成"没问题"
                Log.Error("PROBEV191 CRASH " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }
            Log.Info("PROBEV191 END");
        }

        public static string Run()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.AppendLine("═══ v191 探针（只读）═══");

            AppendSelfCheck(sb);
            AppendLuck(sb);
            AppendCritDmg(sb);
            AppendPigRate(sb);
            AppendMultipliers(sb);
            AppendPanelAndPool(sb);
            return sb.ToString();
        }

        // ────────────────────────────────────────────────── 自检
        private static void AppendSelfCheck(StringBuilder sb)
        {
            sb.AppendLine("── 自检 ──");
            List<CheckResult> results;
            try { results = SelfCheck.RunAll(); }
            catch (Exception e)
            {
                sb.AppendLine($"自检 CRASH：{e.GetType().Name}: {e.Message}");
                return;
            }
            int pass = 0;
            for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
            sb.AppendLine($"自检 {(pass == results.Count ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过");
            for (int i = 0; i < results.Count; i++)
                if (!results[i].Passed) sb.AppendLine("  " + results[i]);
        }

        // ────────────────────────────────────────────────── ① 幸运值
        private static void AppendLuck(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            PlayerBaseDef b = t.player;
            sb.AppendLine("── ① 幸运值 ──");

            var st = new StatBlock(t.caps, t.player);
            sb.AppendLine($"StatId.Luck = {(int)StatId.Luck}｜StatIds.Count = {StatIds.Count}"
                          + $"｜IsFractionStat(Luck) = {TableDatabase.IsFractionStat(StatId.Luck)}"
                          + $"｜CritRate = {TableDatabase.IsFractionStat(StatId.CritRate)}"
                          + $"｜PigRate = {TableDatabase.IsFractionStat(StatId.PigRate)}");
            sb.AppendLine($"基底 tuning.player.luck = {b.luck * 100f:0.#}%"
                          + $"｜面板开局 = 「幸运值 {st.Get(StatId.Luck) * 100f:0.#}%（{ImGuiHud.AddPoints(st.Get(StatId.Luck), b.luck)}）」");

            // 抽一张 +3% 之后（与 PickRewardCard 的 IsFractionStat 分支同一写法）
            st.AddModifier(new StatModifier(StatId.Luck, 3f / 100f, ModOp.Add, "probe"));
            sb.AppendLine($"抽 1 张「幸运值 +3%」后 = {st.Get(StatId.Luck) * 100f:0.#}%"
                          + $"（面板 {ImGuiHud.AddPoints(st.Get(StatId.Luck), b.luck)}）；再抽 1 张 = "
                          + $"{Mathf.Min(1f, st.Get(StatId.Luck) + 0.03f) * 100f:0.#}%（上限 caps 1.0 = 100%）");

            // ★ 1000 次**真实收割**的触发率（不是直接掷签 —— 走完整的 HarvestPlot 路径）★
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            int cellGold = SeedEconomy.CellGold(radish);
            int cellExp = SeedEconomy.CellExp(radish);

            World w = MakeWorld(424242UL, out PlayerActor p);
            p.Progress.Stats.SetBase(StatId.Luck, 0.05f);
            FarmPlot plot = w.Farm.Slot(0, 0);
            plot.BeginPlant(t.farm, radish, 1000, 0);       // 水 1000 / 耗水 0 ⇒ 不会因缺水停长
            plot.TickGrowth(999f);
            int triggers = 0, doubledGold = 0;
            const int Trials = 1000;
            for (int i = 0; i < Trials; i++)
            {
                int g0 = w.GoldEarned;
                w.HarvestPlot(plot);
                int got = w.GoldEarned - g0;
                if (got > cellGold) { triggers++; doubledGold = got; }
                plot.TickGrowth(999f);                     // 回到成熟，继续下一次
            }
            sb.AppendLine($"【1000 次收割 × 幸运 5%】触发 {triggers} 次 = **{triggers / 10f:0.0}%**"
                          + $"（期望 ≈5% ⇒ 约 50 次）｜单格基础 {cellGold} 金，触发时到账 {doubledGold} 金"
                          + $"｜累计 {w.GoldEarned} 金 / {w.ExpEarned:0.#} 验");

            // 弹字（需要 RunSession 才看得见 Popups）
            sb.AppendLine($"未触发弹字：{SampleHarvestTexts(0f)}");
            sb.AppendLine($"必触发弹字：{SampleHarvestTexts(1f)}");
        }

        /// <summary>在**指定幸运值**下收一格，返回「金币… ｜ 经验…」两条弹字原文。</summary>
        private static string SampleHarvestTexts(float luck)
        {
            TuningDef t = TableDatabase.Tuning;
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            var s = new RunSession();
            s.Start(31415UL);
            s.StartWave(1);
            s.Progress.Stats.SetBase(StatId.Luck, luck);
            FarmPlot plot = s.World.Farm.Slot(0, 0);
            plot.BeginPlant(t.farm, radish, 1000, 0);
            plot.TickGrowth(999f);
            s.Popups.Clear();
            int g0 = s.World.GoldEarned;
            float e0 = s.World.ExpEarned;
            s.World.HarvestPlot(plot);
            string gold = "(无)", exp = "(无)";
            for (int i = 0; i < s.Popups.Count; i++)
            {
                string text = s.Popups[i].Text;
                if (text == null) continue;
                if (text.StartsWith("金币")) gold = text;
                else if (text.StartsWith("经验")) exp = text;
            }
            string line = $"{gold} / {exp}　到账 {s.World.GoldEarned - g0} 金 / {s.World.ExpEarned - e0:0.#} 验";
            s.Dispose();
            return line;
        }

        // ────────────────────────────────────────────────── ② 暴击伤害
        private static void AppendCritDmg(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            var st = new StatBlock(t.caps, t.player);
            float critDmg = st.Get(StatId.CritDmg);
            float plain = DamageResolver.WeaponDamage(4f, 0f, 1f, false, critDmg, 0f);
            float crit = DamageResolver.WeaponDamage(4f, 0f, 1f, true, critDmg, 0f);
            sb.AppendLine("── ② 初始暴击伤害 ──");
            sb.AppendLine($"tuning.player.critDmg = {t.player.critDmg:0.###}"
                          + $"｜同一刀 不暴击 {plain:0.##} → 暴击 {crit:0.##} ⇒ **比值 ×{crit / plain:0.###}**"
                          + $"（改前 2.0 时 ×2.000）｜面板 {ImGuiHud.AddRelative(critDmg, t.player.critDmg)}");
            var st2 = new StatBlock(t.caps, t.player);
            st2.AddModifier(new StatModifier(StatId.CritDmg, 1f + 3f / 100f, ModOp.Mul, "probe"));
            sb.AppendLine($"抽一张「暴击伤害 +3%」（Percent 乘法）= {critDmg * 100f:0.#}% → {st2.Get(StatId.CritDmg) * 100f:0.#}%"
                          + $"（面板 {ImGuiHud.AddRelative(st2.Get(StatId.CritDmg), t.player.critDmg)}）");
        }

        // ────────────────────────────────────────────────── ③ 小猪刷新率
        private static void AppendPigRate(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("── ③ 小猪刷新率 ──");
            var parts = new StringBuilder();
            for (int lv = 0; lv <= TuningDef.PigRateMaxLevel; lv++)
                parts.Append($"{lv}={TuningDef.PigLevelNameOf(lv)}({TuningDef.PigRateOf(lv) * 100f:0}%) ");
            sb.AppendLine($"等级表：{parts}｜与爱心同值："
                          + $"{TuningDef.PigRateByLevel[0] == TuningDef.HeartDropRateByLevel[0] && TuningDef.PigRateByLevel[3] == TuningDef.HeartDropRateByLevel[3]}"
                          + $"｜爱心表 {TuningDef.HeartDropRateByLevel[0] * 100f:0}%/{TuningDef.HeartDropRateByLevel[3] * 100f:0}%");

            // 面板那一行 + RunSession 真正用的那条换算（同一表达式，不是另写一份）
            var st = new StatBlock(t.caps, t.player);
            for (int lv = 0; lv <= TuningDef.PigRateMaxLevel; lv++)
            {
                var sl = new StatBlock(t.caps, t.player);
                sl.AddModifier(new StatModifier(StatId.PigRate, lv, ModOp.Add, "probe"));
                int readLv = Mathf.RoundToInt(sl.Get(StatId.PigRate));
                sb.AppendLine($"  等级 {readLv}：面板「小猪刷新率　{TuningDef.PigLevelNameOf(readLv)}（{TuningDef.PigRateOf(readLv) * 100f:0}%）」"
                              + $" ⇒ `WaveDirector` 用的概率 = {TuningDef.PigRateOf(readLv):0.###}");
            }
            // 叠到上限：4 张道具之后仍钳在 MAX
            var cap = new StatBlock(t.caps, t.player);
            for (int i = 0; i < 6; i++) cap.AddModifier(new StatModifier(StatId.PigRate, 1f, ModOp.Add, "probe"));
            sb.AppendLine($"叠 6 张 +1 级道具后：等级被硬钳在 **{cap.Get(StatId.PigRate):0}**（MAX）⇒ "
                          + $"概率 {TuningDef.PigRateOf(Mathf.RoundToInt(cap.Get(StatId.PigRate))) * 100f:0}%；"
                          + $"开局（0 级）{TuningDef.PigRateOf(0) * 100f:0}%，面板「{TuningDef.PigLevelNameOf(0)}（{TuningDef.PigRateOf(0) * 100f:0}%）」");
        }

        // ────────────────────────────────────────────────── ④ 货币 / 经验倍率
        private static void AppendMultipliers(StringBuilder sb)
        {
            sb.AppendLine("── ④ 货币 / 经验倍率进收割 ──");
            sb.AppendLine($"GoldMult 1.0 ⇒ {SampleHarvestTexts(0f)}");
            sb.AppendLine($"GoldMult 1.5 ⇒ {SampleMultiplierHarvest(1.5f)}");
        }

        private static string SampleMultiplierHarvest(float mult)
        {
            TuningDef t = TableDatabase.Tuning;
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            var s = new RunSession();
            s.Start(31415UL);
            s.StartWave(1);
            s.Progress.Stats.SetBase(StatId.Luck, 0f);       // 关掉幸运 ⇒ 只看倍率
            s.Progress.Stats.SetBase(StatId.GoldMult, mult);
            s.Progress.Stats.SetBase(StatId.ExpMult, mult);
            FarmPlot plot = s.World.Farm.Slot(0, 0);
            plot.BeginPlant(t.farm, radish, 1000, 0);
            plot.TickGrowth(999f);
            s.Popups.Clear();
            int g0 = s.World.GoldEarned;
            float e0 = s.World.ExpEarned;
            s.World.HarvestPlot(plot);
            string gold = "(无)", exp = "(无)";
            for (int i = 0; i < s.Popups.Count; i++)
            {
                string text = s.Popups[i].Text;
                if (text == null) continue;
                if (text.StartsWith("金币")) gold = text;
                else if (text.StartsWith("经验")) exp = text;
            }
            string line = $"{gold} / {exp}　到账 {s.World.GoldEarned - g0} 金 / {s.World.ExpEarned - e0:0.#} 验"
                          + $"（基础 {SeedEconomy.CellGold(radish)} 金 / {SeedEconomy.CellExp(radish)} 验 × {mult}）";
            s.Dispose();
            return line;
        }

        // ────────────────────────────────────────────────── ⑤ 面板 + 抽卡池
        private static void AppendPanelAndPool(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            var st = new StatBlock(t.caps, t.player);
            sb.AppendLine("── ⑤ 属性界面 / 抽卡界面 ──");
            sb.AppendLine($"面板「主要属性」新增一行：幸运值　{ImGuiHud.AddPoints(st.Get(StatId.Luck), t.player.luck)}"
                          + $"（当前 {st.Get(StatId.Luck) * 100f:0.#}%，增量 0%）");
            int pl = Mathf.RoundToInt(st.Get(StatId.PigRate));
            sb.AppendLine($"面板「次要属性」新增一行：小猪刷新率　{TuningDef.PigLevelNameOf(pl)}（{TuningDef.PigRateOf(pl) * 100f:0}%）");

            RewardStatDef[] pool = t.levelReward.stats;
            sb.AppendLine($"抽卡池 {pool.Length} 张（v190 = 12 ⇒ 现在应为 13）：");
            for (int i = 0; i < pool.Length; i++)
            {
                RewardStatDef d = pool[i];
                if (d == null) continue;
                var card = new RunSession.LevelRewardCard { Def = d, Amount = d.amount, Copies = 1 };
                sb.AppendLine($"  {i + 1,2}. {card.Label}　(mode={d.mode} amount={d.amount})");
            }
        }

        // ────────────────────────────────────────────────── 夹具
        private static World MakeWorld(ulong seed, out PlayerActor player)
        {
            TuningDef t = TableDatabase.Tuning;
            var w = new World(t, seed);
            var p = new PlayerActor
            {
                Id = w.NextActorId(),
                Team = Team.Player,
                Progress = new PlayerProgress(t),
                Pos = Vector2.zero,
                Radius = 0.5f
            };
            p.Health.Setup(p.Progress.Stats.Get(StatId.MaxHp), p.Progress.Stats.Get(StatId.Shield));
            w.Player = p;
            player = p;
            return w;
        }
    }
}
