using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    public struct SimResult
    {
        public BotStrategy Strategy;
        public ulong Seed;
        /// <summary>
        /// v168.1：这一局是**不死模式**（每固定步把玩家补满血）—— 只用于验证"行为通不通"，
        /// **不作任何平衡结论** ✓（理由见 <see cref="RunSimulator.RunImmortal"/>）
        /// </summary>
        public bool KeepAlive;
        // v165：`Victory` 已删除 ✗
        // 它唯一的来源是 `session.Phase == PhaseState.Victory`，而那个枚举成员、
        // 以及设置它的"守住全部 N 波"判定都已删除 ⇒ 这个字段**恒为 false**。
        // 一个恒 false 的"胜利"标志比没有更糟：读报告的人会以为"这局没赢过"是数值问题。
        // 计时制只有一个结局：`TimedOut == false` 就是玩家死了。
        public bool TimedOut;
        public int Wave;
        public int Kills;
        public int Gold;
        public int Level;
        /// <summary>耕地**格**数（6 列 × 当前行数）。不是"能种几株"。</summary>
        public int MaxPlots;

        /// <summary>
        /// ★ v168 修正口径 ★ 存活**植株** = 占着植物的**列数**（一列 = 一株）。
        ///
        /// 旧口径是 `World.Plants.Count`（`List&lt;PlantAlly&gt;`）✗ —— 那是**会参战的植物帮手**，
        /// 而 v165 已经把"收割即变成战斗植物"整条删掉了（`World.HarvestPlot` 里只剩金币与经验）✓
        /// ⇒ 那个列表**恒为空** ⇒ 字段恒为 0 ✓
        /// 于是报告里的"存活植物 0"根本不是"种地失败"，而是**读了一个死指标** ✗
        /// </summary>
        public int PlantsAlive;
        /// <summary>v168：缺水的格数（`GrowthPaused`，即此刻完全不长）。</summary>
        public int PlantsThirsty;
        /// <summary>v168：成熟待收的格数（不收割就一直是 0 产出）。</summary>
        public int PlantsRipe;

        public int Steps;
        public float Duration;
        /// <summary>
        /// ★ v168 修正口径 ★ **Bot 的耕作意图秒数**（收割 / 浇水 / 施肥 / 去车买种）。
        ///
        /// 旧口径是 `session.FarmActionSeconds` ✗ —— 它只在 `input.Interact` 完成一次
        /// `FarmPlot.TickAction`（掘地 / 播种 / 洒水 / 施肥）时累加 ✓
        /// 而 v23 之后"种下即生长"：地块永远是 Growing ⇒ `NextAction` 只剩 Fertilize，
        /// 掘地/播种/洒水**根本不会再发生** ⇒ 该计数器几乎恒为 0 ✓
        /// 更要紧的是：新玩法的浇水（激光补水）与收割（锄头）走的是**攻击**路径，
        /// 两条路都**不进**这个计数器 ⇒ 旧口径下"耕作占比 0.0%"是**必然**，与策略无关 ✗
        /// </summary>
        public float FarmSeconds;
        /// <summary>v168：Bot 的战斗意图秒数。</summary>
        public float FightSeconds;
        /// <summary>v8：耕作期间被击中次数。</summary>
        public int FarmHits;
        /// <summary>战斗阶段的总时长（分母；含耕作与战斗，所以占比 ≤ 100%）。</summary>
        public float CombatSeconds;

        // ── v168：种地这条线的产出（全部由 Bot 侧观测）──
        /// <summary>收割次数（成熟格 → 重新生长）。</summary>
        public int Harvests;
        /// <summary>实际浇进地里的水（按每格蓄水增量累计）。</summary>
        public int WaterDelivered;
        /// <summary>买到的种子数（= 播种的唯一入口）。</summary>
        public int SeedsBought;
        /// <summary>进商店次数（= 商店车停靠窗的利用次数）。</summary>
        public int ShopVisits;
        /// <summary>v168.3：**可行动的**停靠窗次数（门槛全过的窗口）——区分"没看到窗口"与"看到没走到"。</summary>
        public int ShopWindows;
        /// <summary>★ v168.5：进店时发现**货架为空**的次数 ★ —— 判据是**恒为 0** ✓
        /// 不为 0 ⇒ "车那条路又不刷新货架了" ⇒ 真信号 ✓（而不是被"重抽一次"悄悄绕过去的异常 ✗）</summary>
        public int EmptyShelfVisits;
        /// <summary>施肥完成次数。</summary>
        public int Fertilizes;
        /// <summary>丢失的耕地格数（成熟/生长中直接回到空地——不是收益）。</summary>
        public int PlotsLost;
        /// <summary>收割累计金币（World.HarvestPlot 的 price/5 逐次累加）。</summary>
        public int HarvestGold;
        /// <summary>收割累计经验（price/10）。</summary>
        public int HarvestExp;
        /// <summary>世界累计金币（含击杀掉落），用于算"收益里收割占多少"。</summary>
        public int WorldGoldEarned;
        /// <summary>世界累计经验（含击杀）。</summary>
        public float WorldExpEarned;
        /// <summary>
        /// ★ 本轮新增 ★ 玩家整局**实际承受的伤害总量**（= `World.DamagedByEnemies`）。
        ///
        /// 为什么值得单独成一个字段：它是"活得久不久"的**原因**，而 `Duration` 只是**结果** ✓
        /// 两次跑局若 `Duration` 不同，这个字段立刻能区分
        /// "敌人打得更疼了" 还是 "同一套伤害但挨了更多下" ✓
        /// 它也是死亡诊断里"归因之和 vs 权威总量"的核对基准 ✓
        /// </summary>
        public float DamageTaken;

        /// <summary>耕作时间占**战斗阶段总时长**的比例（口径见 FarmSeconds）。</summary>
        public float FarmRatio => CombatSeconds <= 0f ? 0f : FarmSeconds / CombatSeconds;
        /// <summary>金币里来自收割的比例。</summary>
        public float HarvestGoldShare => WorldGoldEarned <= 0 ? 0f : HarvestGold / (float)WorldGoldEarned;
        /// <summary>经验里来自收割的比例。</summary>
        public float HarvestExpShare => WorldExpEarned <= 0f ? 0f : HarvestExp / WorldExpEarned;

        public string ToLine()
        {
            string tag = KeepAlive ? "［不死模式·只验行为］" : "";
            return $"{tag}策略 {Strategy,-8} seed={Seed} 结果={(KeepAlive ? "跑满上限" : TimedOut ? "超时（跑满上限仍未死）" : "失败（玩家死亡）")} " +
                   $"第 {Wave} 分钟 击杀 {Kills} 金币 {Gold} 等级 {Level} 耕地 {MaxPlots} 存活植株 {PlantsAlive} " +
                   $"用时 {Duration:0.0}s 耕作占比 {FarmRatio * 100f:0.0}% (耕 {FarmSeconds:0.0}s / 战 {CombatSeconds:0.0}s) 耕作挨打 {FarmHits} 次" +
                   "\n" +
                   $"            种地：收割 {Harvests} 次（+{HarvestGold} 金 +{HarvestExp} 经验）｜浇水 {WaterDelivered} 水｜买种 {SeedsBought} 份｜" +
                   $"进店 {ShopVisits} 次（可行动窗口 {ShopWindows} 次｜空货架 {EmptyShelfVisits} 次）｜施肥 {Fertilizes} 次｜丢地 {PlotsLost} 格｜缺水池 {PlantsThirsty}｜熟待收 {PlantsRipe}" +
                   "\n" +
                   $"            收益归属：收割金币 {HarvestGold}/{WorldGoldEarned}（{HarvestGoldShare * 100f:0.0}%）｜" +
                   $"收割经验 {HarvestExp}/{WorldExpEarned:0}（{HarvestExpShare * 100f:0.0}%）";
        }
    }

    /// <summary>
    /// 无头跑局：固定步长 + Bot 输入，不依赖引擎时间。
    /// 同一 seed 两次跑局必须得到完全相同的结果（确定性回归）。
    /// </summary>
    public static class RunSimulator
    {
        public const float Dt = GameClock.FixedDt;

        /// <summary>
        /// 无头跑局。
        /// <paramref name="observe"/> 是**只读观测钩子**（本轮新增，默认为 null ⇒ 零行为变化）：
        /// 每固定步、在 `FixedTick` **之前**调用一次，用来采时间线与承伤归因 ✓
        /// 放在前面是因为"死亡那一帧已经把尸体清出 `World.Enemies` 了"——
        /// 在步前采样，本步开始时还在场上的敌人全都看得见 ✓
        /// 它**必须只读**：观测里掷一次随机数就会移动随机流、改变跑局结果 ✗
        /// </summary>
        public static SimResult Run(BotStrategy strategy, ulong seed, float maxSeconds = 1500f, bool keepAlive = false,
                                    System.Action<float, RunSession, RunBot> observe = null)
        {
            TableDatabase.Load();
            var session = new RunSession();
            session.Start(seed);
            var bot = new RunBot(session, strategy);

            int maxSteps = Mathf.CeilToInt(maxSeconds / Dt);
            int steps = 0;
            while (!session.IsOver && steps < maxSteps)
            {
                if (keepAlive) TopUpPlayer(session);   // 必须在 FixedTick **之前**补：死是按帧判的
                if (observe != null) observe(session.RunTime, session, bot);
                FrameInput input = bot.Tick(Dt);
                // v53：与真实帧循环保持一致——帧级一次性输入（滚轮 / 刷新）先消费再进固定步
                session.ConsumeFrameEdges(ref input);
                session.FixedTick(Dt, input);
                steps++;
            }
            // 收尾采样：把"死亡瞬间/跑满上限那一帧"的状态也记上，否则最后一格最多差 0.5 秒
            if (observe != null) observe(session.RunTime, session, bot);

            var result = new SimResult
            {
                Strategy = strategy,
                Seed = seed,
                KeepAlive = keepAlive,
                // v165：`Victory = session.Phase == PhaseState.Victory` 已删除（那个阶段不存在了）
                TimedOut = !session.IsOver,
                Wave = session.WaveIndex,
                Kills = session.World.KillCount,
                Gold = session.Gold,
                Level = session.Progress.Level,
                MaxPlots = session.World.Farm.MaxPlots,
                // v168：存活植株 = 占着的列数（旧口径 World.Plants.Count 恒为 0，理由见字段注释）
                PlantsAlive = session.World.Farm.PlantCount,
                PlantsThirsty = CountThirsty(session.World.Farm),
                PlantsRipe = CountRipe(session.World.Farm),
                Steps = steps,
                Duration = session.RunTime,
                // v168：耕作/战斗秒数改为**Bot 的意图时间**（RunSession.FarmActionSeconds 在新玩法下恒为 0）
                FarmSeconds = bot.FarmIntentSeconds,
                FightSeconds = bot.FightIntentSeconds,
                FarmHits = session.FarmHits,
                CombatSeconds = session.CombatSeconds,
                Harvests = bot.Harvests,
                WaterDelivered = bot.WaterDelivered,
                SeedsBought = bot.SeedsBought,
                ShopVisits = bot.ShopVisitsLog,
                ShopWindows = bot.ShopWindows,
                EmptyShelfVisits = bot.EmptyShelfVisits,
                Fertilizes = bot.Fertilizes,
                PlotsLost = bot.PlotsLost,
                HarvestGold = bot.HarvestGold,
                HarvestExp = bot.HarvestExp,
                WorldGoldEarned = session.World.GoldEarned,
                WorldExpEarned = session.World.ExpEarned,
                // ★ 必须在 session.Dispose() 之前读（它挂在 World 上）★
                DamageTaken = session.World.DamagedByEnemies
            };
            session.Dispose();
            return result;
        }

        /// <summary>
        /// v168.1：**不死模式** —— 每固定步把玩家补满（生命 + 护盾）。
        ///
        /// ★ 为什么必须有它 ★
        /// 当前数值下四种策略都在**第 1 分钟**内倒下（`grunt` 每 0.8 秒 1 点、玩家共 12 点血），
        /// 而**商店车第 60 秒才来** ⇒ 常规矩阵**永远够不到"扩张植株"这条路** ✗
        /// ⇒ "Bot 会不会买种 / 能不能养 4~6 株 / 硬资源窗口有没有被用到"必须单独验 ✓
        ///
        /// ★ 它证明什么、不证明什么 ★
        ///   ✓ 证明：行为链通不通（进店 → 买种 → 自动种下 → 补 4~6 株 → 反复收割）
        ///   ✗ 不证明：平衡。**任何"能活多久"的结论都不许引它** ✓
        /// </summary>
        public static SimResult RunImmortal(BotStrategy strategy, ulong seed, float seconds)
            => Run(strategy, seed, seconds, true);

        /// <summary>
        /// ★ 本轮新增：**带死亡诊断的跑局** ★ —— 跑一局并产出"它到底怎么死的"报告。
        ///
        /// 它**不改任何玩法数值、不改任何分支**：只是给 `Run` 挂上只读观测钩子 ✓
        /// 谁要调平衡，先看这份报告 —— 否则就是把
        /// 「敌人变多了」「敌人变强了」「Bot 卡住了」「成长跟不上」四种病当成一种治 ✗
        /// </summary>
        public static SimResult RunDiagnosed(BotStrategy strategy, ulong seed, float maxSeconds, out string report)
        {
            DeathDiagnostics.Reset();
            SimResult r = Run(strategy, seed, maxSeconds, false, DeathDiagnostics.Observe);
            report = DeathDiagnostics.BuildReport(r, r.DamageTaken);
            return r;
        }

        private static void TopUpPlayer(RunSession s)
        {
            World w = s.World;
            if (w == null || w.Player == null) return;
            HealthPool hp = w.Player.Health;
            if (hp.Hp < hp.MaxHp) hp.Hp = hp.MaxHp;
            if (hp.Shield < hp.MaxShield) hp.Shield = hp.MaxShield;
        }

        private static int CountThirsty(FarmGrid grid)        {
            int n = 0;
            for (int i = 0; i < grid.Slots.Length; i++)
            {
                FarmPlot p = grid.Slots[i];
                if (p != null && p.State == PlotState.Growing && p.GrowthPaused) n++;
            }
            return n;
        }

        private static int CountRipe(FarmGrid grid)
        {
            int n = 0;
            for (int i = 0; i < grid.Slots.Length; i++)
            {
                FarmPlot p = grid.Slots[i];
                if (p != null && p.IsRipe) n++;
            }
            return n;
        }

        public static List<SimResult> RunMatrix(ulong baseSeed, params BotStrategy[] strategies)
        {
            var list = new List<SimResult>();
            foreach (BotStrategy s in strategies) list.Add(Run(s, baseSeed));
            return list;
        }

        /// <summary>同 seed 两次跑局是否逐字段一致（确定性验收）。</summary>
        public static bool VerifyDeterminism(BotStrategy strategy, ulong seed, out string report)
        {
            SimResult a = Run(strategy, seed);
            SimResult b = Run(strategy, seed);
            var sb = new StringBuilder();
            bool ok = true;
            void Cmp(string name, object x, object y)
            {
                bool same = Equals(x, y);
                if (!same) ok = false;
                sb.AppendLine($"  {name,-14} {x} vs {y} {(same ? "OK" : "不一致")}");
            }
            // v165：比对项从 `Victory`（恒 false，已删）换成 `TimedOut` —— 它才是有信息量的那个：
            //       false = 玩家死了、true = 跑满上限还活着，两者的分支完全不同。
            Cmp("timedOut", a.TimedOut, b.TimedOut);
            Cmp("wave", a.Wave, b.Wave);
            Cmp("kills", a.Kills, b.Kills);
            Cmp("gold", a.Gold, b.Gold);
            Cmp("level", a.Level, b.Level);
            Cmp("steps", a.Steps, b.Steps);
            Cmp("duration", a.Duration, b.Duration);
            Cmp("farmSeconds", a.FarmSeconds, b.FarmSeconds);
            // v168：种地这条线的每个计数都要比对——否则"确定性"只覆盖了战斗那一半
            Cmp("harvests", a.Harvests, b.Harvests);
            Cmp("water", a.WaterDelivered, b.WaterDelivered);
            Cmp("seeds", a.SeedsBought, b.SeedsBought);
            Cmp("plants", a.PlantsAlive, b.PlantsAlive);
            Cmp("harvestGold", a.HarvestGold, b.HarvestGold);
            report = sb.ToString();
            return ok;
        }

        /// <summary>策略矩阵 + 确定性校验，返回可直接落盘的文本报告。</summary>
        public static string BuildRegressionReport(ulong seed, out bool determinismOk)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Seedfall 守园者 · 无头回归报告");
            sb.AppendLine($"种子 seed={seed}　Unity {Application.unityVersion}　生成时间 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"配置：种子 {TableDatabase.Seeds.Length}｜变异 {TableDatabase.Mutations.Length}｜敌人 {TableDatabase.Enemies.Length}｜波次参数 {TableDatabase.Waves.Length} 条｜道具 {TableDatabase.Items.Length}");
            sb.AppendLine();
            sb.AppendLine("## 策略矩阵（同一种子，四种操作策略各跑满 10 分钟）");
            var results = new List<SimResult>();
            foreach (BotStrategy s in new[] { BotStrategy.Idle, BotStrategy.Farmer, BotStrategy.Balanced, BotStrategy.Rush })
            {
                SimResult r = Run(s, seed);
                results.Add(r);
                sb.AppendLine(r.ToLine());
            }
            sb.AppendLine();
            sb.AppendLine("## 不死模式行为验证（每步补满血；★ 只验行为链，不作平衡结论 ★）");
            sb.AppendLine("为什么单独跑：当前数值下四种策略都在第 1 分钟内倒下，而**商店车第 60 秒才来**");
            sb.AppendLine("⇒ 常规矩阵永远够不到「扩张植株」这条路 ⇒ 那一段必须在不死模式下验 ✓");
            var harness = new List<SimResult>();
            foreach (BotStrategy hs in new[] { BotStrategy.Farmer, BotStrategy.Balanced, BotStrategy.Rush })
            {
                SimResult h = RunImmortal(hs, seed, hs == BotStrategy.Rush ? 180f : 300f);
                harness.Add(h);
                sb.AppendLine(h.ToLine());
            }
            sb.AppendLine();
            sb.AppendLine("## 种地验收判据（v168：机器人到底会不会种地）");
            sb.Append(Verdict(results, harness));
            sb.AppendLine();
            determinismOk = VerifyDeterminism(BotStrategy.Balanced, seed, out string det);
            sb.AppendLine("## 确定性校验（同种子两次跑局逐字段比对）");
            sb.AppendLine($"结果：{(determinismOk ? "PASS" : "FAIL")}");
            sb.Append(det);
            return sb.ToString();
        }

        /// <summary>
        /// v168：把"机器人会不会种地"写成可读的判据，而不是让人自己从数字里猜。
        /// 判据来自任务书：Balanced 活到第 5 分钟、存活植株 ≥ 1、耕作占比 &gt; 10%、
        /// 金币/经验主要来自收割、以及四个策略必须**跑得出差别** ✓
        ///
        /// v168.1 补两类：
        ///   · 「活过 70 秒 ⇒ 应该进过至少 1 次商店」—— 验的是**硬资源窗口**被用上 ✓
        ///     而**没活到 70 秒时记 SKIP 而不是 FAIL**：那是平衡问题，不是 Bot 问题 ✗
        ///   · 扩张判据（植株数 Farmer &gt; Balanced &gt; Rush）在**不死模式**下评估 ✓
        /// </summary>
        private static string Verdict(List<SimResult> rs, List<SimResult> harness)
        {
            var sb = new StringBuilder();
            SimResult idle = rs[0], farmer = rs[1], balanced = rs[2], rush = rs[3];
            SimResult hFarmer = harness[0], hBalanced = harness[1], hRush = harness[2];
            Line("Balanced 活到第 5 分钟（Duration ≥ 300s）",
                balanced.Duration >= 300f, $"{balanced.Duration:0.0}s");
            Line("Balanced 存活植株 ≥ 1", balanced.PlantsAlive >= 1, $"{balanced.PlantsAlive} 株");
            Line("Balanced 耕作占比 > 10%", balanced.FarmRatio > 0.10f, $"{balanced.FarmRatio * 100f:0.0}%");
            Line("Balanced 有收割收益（次数 > 0）", balanced.Harvests > 0, $"{balanced.Harvests} 次 / +{balanced.HarvestGold} 金");
            Line("金币主要来自收割（占比 > 50%）", balanced.HarvestGoldShare > 0.50f, $"{balanced.HarvestGoldShare * 100f:0.0}%");
            Line("经验主要来自收割（占比 > 50%）", balanced.HarvestExpShare > 0.50f, $"{balanced.HarvestExpShare * 100f:0.0}%");
            Line("Farmer 比 Balanced 种得多（植株数 ≥）", farmer.PlantsAlive >= balanced.PlantsAlive,
                $"Farmer {farmer.PlantsAlive} / Balanced {balanced.PlantsAlive}");
            Line("Rush 几乎不种（植株 ≤ 1）", rush.PlantsAlive <= 1, $"{rush.PlantsAlive} 株");
            Line("Idle 仍然什么都不做（收割 = 0）", idle.Harvests == 0, $"{idle.Harvests} 次");
            // 可区分性：四个策略的"签名"不能有两行完全相同
            int distinct = DistinctSignatures(rs);
            Line("四个策略可区分（签名互不相同）", distinct == rs.Count, $"{distinct}/{rs.Count} 种不同签名");

            sb.AppendLine("  ── 硬资源窗口 + 扩张（扩张部分在不死模式下评估）──");
            Skip("矩阵里 Balanced 活过 70 秒 ⇒ 应进过 ≥1 次商店",
                balanced.Duration >= 70f, balanced.ShopVisits >= 1,
                $"活 {balanced.Duration:0.0}s / 进店 {balanced.ShopVisits} 次",
                $"只活了 {balanced.Duration:0.0}s，而商店车第 60 秒才来 ⇒ 本条无法评估（平衡问题，不是 Bot 问题）");
            Line("不死 Farmer 进过商店", hFarmer.ShopVisits >= 1, $"{hFarmer.ShopVisits} 次");
            // ★ v168.5：货架接线回归 ★ —— 真修复落在 `TickShopTruck` 之后，这条应恒为 0 ✓
            //   不为 0 ⇒ "车那条路又不刷新货架了" ⇒ 真信号 ✓（我不会再用重抽把它盖掉 ✗）
            Line("空货架次数 = 0（车进店必须刷新货架）",
                hFarmer.EmptyShelfVisits == 0 && hBalanced.EmptyShelfVisits == 0,
                $"Farmer {hFarmer.EmptyShelfVisits} / Balanced {hBalanced.EmptyShelfVisits}");
            Line("不死 Farmer 买到过种子", hFarmer.SeedsBought >= 1, $"{hFarmer.SeedsBought} 份");
            Line("不死 Farmer 植株数 ≥ 4（扩张成功）", hFarmer.PlantsAlive >= 4, $"{hFarmer.PlantsAlive} 株 / 目标 6");
            Line("不死 Balanced 植株数 3~5（档位生效）",
                hBalanced.PlantsAlive >= 3 && hBalanced.PlantsAlive <= 5, $"{hBalanced.PlantsAlive} 株 / 目标 4");
            Line("不死 Rush 仍然 ≤ 1 株（不买种）", hRush.PlantsAlive <= 1, $"{hRush.PlantsAlive} 株");
            Line("不死 Farmer 比 Balanced 种得多", hFarmer.PlantsAlive >= hBalanced.PlantsAlive,
                $"Farmer {hFarmer.PlantsAlive} / Balanced {hBalanced.PlantsAlive}");
            Line("不死 Farmer 持续收割（5 分钟内 > 20 次）", hFarmer.Harvests > 20, $"{hFarmer.Harvests} 次");
            return sb.ToString();

            void Line(string name, bool ok, string detail)
                => sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}　→ {detail}");

            void Skip(string name, bool evaluable, bool ok, string detail, string why)
            {
                if (!evaluable) { sb.AppendLine($"  SKIP  {name}　→ {why}"); return; }
                Line(name, ok, detail);
            }
        }

        /// <summary>策略签名：只取"行为"字段（不含 seed），两行相同就是矩阵失去了意义。</summary>
        private static int DistinctSignatures(List<SimResult> rs)
        {
            var set = new HashSet<string>();
            foreach (SimResult r in rs)
                set.Add($"{r.Kills}|{r.PlantsAlive}|{r.Harvests}|{r.WaterDelivered}|{r.SeedsBought}|{r.Duration:0.0}");
            return set.Count;
        }

        /// <summary>把报告写到 &lt;工程&gt;/Docs/reports/，返回文件绝对路径。</summary>
        public static string WriteReport(string content, string fileName = null)
        {
            string root = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "Docs", "reports"));
            System.IO.Directory.CreateDirectory(root);
            if (string.IsNullOrEmpty(fileName)) fileName = $"sim-{System.DateTime.Now:yyyyMMdd-HHmmss}.txt";
            string path = System.IO.Path.Combine(root, fileName);
            System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
    }
}
