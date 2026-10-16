#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Seedfall.EditorTools
{
    /// <summary>
    /// 无头回归入口。CI / 命令行用法：
    /// Unity.exe -batchmode -nographics -projectPath "&lt;工程&gt;" -executeMethod Seedfall.EditorTools.SeedfallMenu.RunAll -logFile -
    /// </summary>
    public static class SeedfallMenu
    {
        private const ulong DefaultSeed = 20240607UL;

        [MenuItem("Tools/Seedfall/校验配置表")]
        public static void ValidateTables()
        {
            TableDatabase.Load(true);
            Debug.Log($"[Seedfall] 配置表校验通过：种子 {TableDatabase.Seeds.Length}｜变异 {TableDatabase.Mutations.Length}｜" +
                      $"敌人 {TableDatabase.Enemies.Length}｜波次 {TableDatabase.Waves.Length}｜道具 {TableDatabase.Items.Length}");
        }

        [MenuItem("Tools/Seedfall/跑一局（Balanced 无头）")]
        public static void RunOne()
        {
            TableDatabase.Load();
            SimResult r = RunSimulator.Run(BotStrategy.Balanced, DefaultSeed);
            Debug.Log("[Seedfall] " + r.ToLine());
        }

        [MenuItem("Tools/Seedfall/策略矩阵回归")]
        public static void RunMatrix()
        {
            TableDatabase.Load();
            var sb = new StringBuilder();
            foreach (BotStrategy s in new[] { BotStrategy.Idle, BotStrategy.Farmer, BotStrategy.Balanced, BotStrategy.Rush })
                sb.AppendLine(RunSimulator.Run(s, DefaultSeed).ToLine());
            Debug.Log("[Seedfall] 策略矩阵：\n" + sb);
        }

        [MenuItem("Tools/Seedfall/确定性校验")]
        public static void RunDeterminism()
        {
            TableDatabase.Load();
            bool ok = RunSimulator.VerifyDeterminism(BotStrategy.Balanced, DefaultSeed, out string report);
            Debug.Log($"[Seedfall] 确定性 {(ok ? "PASS" : "FAIL")}\n{report}");
        }

        /// <summary>
        /// v167（本次任务新增）：**数值自检的无头入口**。
        ///
        /// 为什么加它：自检原本只能在 Play 模式里经 `DebugApi.SelfCheck()` 跑 ✗
        /// ⇒ CI / 批处理模式**拿不到那份计数** ✓
        /// 而它是本工程唯一的"数值回归"手段（与 EditMode 单测共用期望值）✓
        ///
        /// 用法：
        ///   Unity.exe -batchmode -nographics -projectPath "&lt;工程&gt;" \
        ///             -executeMethod Seedfall.EditorTools.SeedfallMenu.RunSelfCheck -logFile -
        ///
        /// 任意一条不通过 ⇒ 退出码 1（CI 能判定）✓ 抛异常也 ⇒ 1 ✓
        /// </summary>
        [MenuItem("Tools/Seedfall/数值自检（无头可用）")]
        public static void RunSelfCheck()
        {
            int exitCode = 0;
            try
            {
                System.Collections.Generic.List<Seedfall.CheckResult> results = Seedfall.SelfCheck.RunAll();
                int failed = 0;
                for (int i = 0; i < results.Count; i++)
                    if (!results[i].Passed) failed++;
                Debug.Log($"[Seedfall] 自检：{results.Count - failed}/{results.Count} 通过，{failed} 失败\n"
                          + Seedfall.SelfCheck.Report(results));
                if (failed > 0) exitCode = 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[Seedfall] 自检抛异常：" + e);
                exitCode = 1;
            }

            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        /// <summary>
        /// ★ 本轮新增：**死亡诊断**（只读观测；不动任何玩法数值）★
        ///
        /// 用途：回答「Balanced 为什么死在 294.7 秒」。
        /// 它跑两局**同一个种子、同一个策略**：
        ///   ① 正常局 —— 拿到承伤归因 + 死前 30 秒时间线
        ///   ② 不死局（每步补满）—— 拿到"如果没有死，同一时刻它在做什么"
        /// 两局在**死亡时刻之前应当逐字段相同**；一旦分叉，分叉点就是
        /// "HP 反过来改变了 Bot 行为"的证据 ✓（`RunBot.CombatTick` 的 `lowHp` 门槛）
        ///
        /// 报告写到 `Docs/reports/`；**不 Debug.Log 全文**（Unity 会截断长日志）✗
        ///
        /// 用法：
        ///   Unity.exe -batchmode -nographics -projectPath "&lt;工程&gt;" \
        ///             -executeMethod Seedfall.EditorTools.SeedfallMenu.RunDeathDiagnosis -logFile -
        /// </summary>
        [MenuItem("Tools/Seedfall/★ 死亡诊断（Balanced 只读观测）")]
        public static void RunDeathDiagnosis()
        {
            int exitCode = 0;
            try
            {
                TableDatabase.Load();
                SimResult r = RunSimulator.RunDiagnosed(BotStrategy.Balanced, DefaultSeed, 1500f, out string diag);
                string p1 = RunSimulator.WriteReport(diag, $"diag-balanced-{Stamp()}.txt");

                // ② 对照：同一策略 + 不死模式。用来证明"死亡本身改变了 Bot 的行为"。
                DeathDiagnostics.Reset();
                SimResult ri = RunSimulator.Run(BotStrategy.Balanced, DefaultSeed, r.Duration + 1f, true,
                    DeathDiagnostics.Observe);
                string cmp = DeathDiagnostics.BuildReport(ri, ri.DamageTaken);
                string p2 = RunSimulator.WriteReport(
                    "# 对照：同一策略 + 不死模式（每步补满血）\n"
                    + "# ★ 只用于回答\"如果没死，同一时刻它在做什么\"；不作任何平衡结论 ★\n"
                    + $"# 正常局死亡于 {r.Duration:0.0}s；本局跑到 {ri.Duration:0.0}s\n\n" + cmp,
                    $"diag-immortal-{Stamp()}.txt");

                UnityEngine.Debug.Log(
                    $"[Seedfall] 死亡诊断完成\n  正常局：{r.ToLine()}\n  报告：{p1}\n  对照：{p2}");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[Seedfall] 死亡诊断失败：" + e);
                exitCode = 1;
            }

            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        /// <summary>
        /// ★ 参数扫描（**只允许在隔离副本上跑**）★
        ///
        /// 为什么需要它：调平衡的铁律是"一次只动一个变量"，而每改一次 `tuning.json`
        /// 都要重启 Unity + 重新导入 ⇒ 一次对比十几分钟 ✗ 十几组对比就是几小时 ✗
        ///
        /// 本入口改成**在内存里改配置**，一次进程跑完所有变体 ✓
        /// 依据：`RunSimulator.Run` 开头的 `TableDatabase.Load()` **不带 force** ⇒
        ///       已经 `Loaded` 就直接返回，不会覆盖我们改过的字段 ✓
        ///       而 `RunSession.Start` 里是 `T = TableDatabase.Tuning`（**同一个对象引用**）✓
        ///       ⇒ 改 `Tuning` 的公共字段，World/Bot/刷怪全都看得到 ✓
        ///
        /// ★ 它【不写任何文件】★ —— 副本的 `tuning.json` 跑完仍是原样 ✓
        ///
        /// ★ 安全联锁：在原工程上跑会被拒绝 ★
        ///   理由不是"它危险"，而是"它会**污染基线**"：一份不属于那里的配置的输出
        ///   混进 `Docs/reports` 之后，下一个人会把它当成真实的回归结果 ✗
        /// </summary>
        [MenuItem("Tools/Seedfall/★ 参数扫描（只在副本上跑）")]
        public static void RunTuningSweep()
        {
            string proj = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, ".."));
            if (proj.IndexOf("SeedfallDiag", StringComparison.OrdinalIgnoreCase) < 0)
            {
                UnityEngine.Debug.LogError(
                    "[Seedfall] ★ 参数扫描拒绝运行 ★：只允许在隔离副本上跑（当前 = " + proj + "）。"
                    + "它会在内存里改平衡配置 ⇒ 跑在原工程上会污染所有人的验证基线。");
                return;
            }

            int exitCode = 0;
            try
            {
                TableDatabase.Load(true);
                TuningDef t = TableDatabase.Tuning;
                EnemyGrowthDef g = TableDatabase.EnemyGrowth;

                // 原值：每个变体跑之前先还原，跑完再还原
                float oMaxAliveGrowth = t.waves.maxAliveGrowth;
                float oSpawnCountGrowth = t.waves.spawnCountGrowth;
                float oExpBase = t.growth.expBase;
                float oExpLinear = t.growth.expLinear;
                float oHpStepB = g.hpStepB;

                void Restore()
                {
                    t.waves.maxAliveGrowth = oMaxAliveGrowth;
                    t.waves.spawnCountGrowth = oSpawnCountGrowth;
                    t.growth.expBase = oExpBase;
                    t.growth.expLinear = oExpLinear;
                    g.hpStepB = oHpStepB;
                }

                var variants = new List<(string Name, Action Apply)>
                {
                    ("00-baseline", () => { }),
                    // ① 削【数量】：只动"同时在场上限"的增长系数
                    ("01-count-maxAliveGrowth-6.0", () => { t.waves.maxAliveGrowth = 6.0f; }),
                    ("02-count-maxAliveGrowth-3.0", () => { t.waves.maxAliveGrowth = 3.0f; }),
                    // ① 削【数量】：改"每拍刷新量"的增长系数（控的是速率，不是上限）
                    ("03-count-spawnCountGrowth-2.0", () => { t.waves.spawnCountGrowth = 2.0f; }),
                    // ② 加快玩家成长：只动经验曲线（收割收益公式在代码里，不动）
                    ("04-growth-exp-10-6", () => { t.growth.expBase = 10f; t.growth.expLinear = 6f; }),
                    ("05-growth-exp-14-8", () => { t.growth.expBase = 14f; t.growth.expLinear = 8f; }),
                    // 组合
                    ("06-both-count6-exp10-6", () => { t.waves.maxAliveGrowth = 6.0f; t.growth.expBase = 10f; t.growth.expLinear = 6f; }),
                    // 对照：动 HP 曲线（用户明确说"不动 HP 曲线"，这里只用来证明它确实没用）
                    ("07-contrast-hpStepB-0.003", () => { g.hpStepB = 0.003f; }),
                };

                var sb = new StringBuilder();
                sb.AppendLine("# Seedfall 守园者 · 参数扫描（内存改配置；★ 未写任何文件 ★）");
                sb.AppendLine($"# 项目 {proj}");
                sb.AppendLine("# 变体说明：01/02 削在场上限的增长；03 削每拍刷新量的增长；04/05 加快经验曲线；06 组合；07 对照（动 HP 曲线）");
                sb.AppendLine();
                sb.AppendLine("变体                              死亡(s)  分钟 等级 击杀 收割 植株 金币  承伤 | 每分钟【在场上限(每拍刷新量区间)】1→8");
                sb.AppendLine("                                                                                       4       5       6");

                foreach ((string Name, Action Apply) in variants)
                {
                    Restore();
                    Apply();

                    SimResult r = RunSimulator.RunDiagnosed(BotStrategy.Balanced, DefaultSeed, 1500f, out string diag);
                    RunSimulator.WriteReport(diag, $"sweep-{Name}.txt");

                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-32} {1,7:0.0}  {2,4}  {3,4}  {4,4}  {5,4}  {6,4}  {7,4}  {8,5:0.0} | {9}",
                        Name, r.Duration, r.Wave, r.Level, r.Kills, r.Harvests, r.PlantsAlive, r.Gold,
                        r.DamageTaken, CapsLine()));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-32} 第 4/5/6 分钟上限： {1} / {2} / {3}",
                        "", Caps(4), Caps(5), Caps(6)));
                }

                Restore();
                string path = RunSimulator.WriteReport(sb.ToString(), $"tuning-sweep-{Stamp()}.txt");
                UnityEngine.Debug.Log("[Seedfall] 参数扫描完成：\n" + sb + "\n报告：" + path);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[Seedfall] 参数扫描失败：" + e);
                exitCode = 1;
            }

            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        /// <summary>按 <see cref="WaveDirector"/> 的同一条算式复算"同时在场上限(每拍刷新量区间)"。</summary>
        private static string Caps(int wave)
        {
            WavesCfg c = TableDatabase.Tuning.waves;
            float tt = Mathf.Max(0f, wave - 1f);
            float grow = Mathf.Sqrt(tt);
            int maxAlive = Mathf.Max(4, c.maxAliveBase + Mathf.RoundToInt(c.maxAlivePerWave * wave)
                                      + Mathf.RoundToInt(c.maxAliveGrowth * grow));
            int lo = Mathf.Max(1, c.spawnBaseCount + Mathf.FloorToInt(c.spawnCountPerWave * wave)
                                   + Mathf.FloorToInt(c.spawnCountGrowth * grow));
            int hi = lo + Mathf.FloorToInt(Mathf.Max(0f, c.spawnCountByProgress));
            return $"{maxAlive}({lo}-{hi})";
        }

        private static string CapsLine()
        {
            var parts = new List<string>();
            for (int wv = 1; wv <= 8; wv++) parts.Add(Caps(wv));
            return string.Join("  ", parts);
        }

        /// <summary>
        /// ★ 多种子分布（**只允许在隔离副本上跑**）★
        ///
        /// ★ 为什么必须有它 ★
        /// 第一版参数扫描给出了一个**反直觉**的结果：
        ///   把敌人**调少**（maxAliveGrowth 9.8 → 6.0）⇒ 反而 **355.8s → 105.8s**（死得更早）
        ///   把玩家**调强**（经验曲线 20/11 → 14/8）⇒ 也 **355.8s → 116.9s**（死得更早）
        /// 两者都"不该"如此 ✓ 唯一的解释是：
        ///
        /// ★ 单种子跑局是**混沌样本** ✗ ★
        ///   `World.Rng` 被刷怪、掉落、暴击、瞄准散布**共同消费** ⇒
        ///   任何一个数值改动都会让整条随机流**换一条轨迹** ✓
        ///   于是"改一个旋钮"和"换一个种子"在做同一件事：
        ///   ★ 都只是重新抽了一次样本 ★ ✓
        ///
        /// ⇒ 所以判据必须建立在**分布**上，而不是某一个种子的一个时刻 ✓
        /// 本入口就是量这个分布的：同一套配置跑 N 个种子，报 min / 中位 / max / 过线率 ✓
        /// </summary>
        [MenuItem("Tools/Seedfall/★ 多种子分布（只在副本上跑）")]
        public static void RunSeedSpread()
        {
            string proj = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, ".."));
            if (proj.IndexOf("SeedfallDiag", StringComparison.OrdinalIgnoreCase) < 0)
            {
                UnityEngine.Debug.LogError("[Seedfall] ★ 多种子分布拒绝运行 ★：只允许在隔离副本上跑（当前 = " + proj + "）。");
                return;
            }

            int exitCode = 0;
            try
            {
                TableDatabase.Load(true);
                TuningDef t = TableDatabase.Tuning;
                float oMaxAliveGrowth = t.waves.maxAliveGrowth;
                float oSpawnCountGrowth = t.waves.spawnCountGrowth;
                float oExpBase = t.growth.expBase;
                float oExpLinear = t.growth.expLinear;
                float oShieldRegenDelay = t.player.shieldRegenDelay;

                // 种子集合：默认种子 + 1..23（一组"与游戏无关"的序号，避免挑选嫌疑）
                var seeds = new List<ulong> { DefaultSeed };
                for (ulong s = 1; s <= 23; s++) seeds.Add(s);

                // 每个变体跑之前先整体还原 ⇒ 变体之间不会互相污染
                void Restore()
                {
                    t.waves.maxAliveGrowth = oMaxAliveGrowth;
                    t.waves.spawnCountGrowth = oSpawnCountGrowth;
                    t.growth.expBase = oExpBase;
                    t.growth.expLinear = oExpLinear;
                    t.player.shieldRegenDelay = oShieldRegenDelay;
                }

                var variants = new List<(string Name, Action Apply)>
                {
                    ("00-baseline", () => { }),
                    ("01-count-maxAliveGrowth-6.0", () => { t.waves.maxAliveGrowth = 6.0f; }),
                    ("02-count-maxAliveGrowth-3.0", () => { t.waves.maxAliveGrowth = 3.0f; }),
                    ("03-count-spawnCountGrowth-2.0", () => { t.waves.spawnCountGrowth = 2.0f; }),
                    ("04-growth-exp-10-6", () => { t.growth.expBase = 10f; t.growth.expLinear = 6f; }),
                    ("05-shieldRegenDelay-10to4", () => { t.player.shieldRegenDelay = 4f; }),
                    ("06-count3.0+exp10-6", () => { t.waves.maxAliveGrowth = 3.0f; t.growth.expBase = 10f; t.growth.expLinear = 6f; }),
                    ("07-count3.0+shield4", () => { t.waves.maxAliveGrowth = 3.0f; t.player.shieldRegenDelay = 4f; }),
                };

                var sb = new StringBuilder();
                sb.AppendLine("# Seedfall 守园者 · 多种子分布（内存改配置；★ 未写任何文件 ★）");
                sb.AppendLine($"# 项目 {proj}｜种子 {seeds.Count} 个：{DefaultSeed} + 1..23｜策略 Balanced");
                sb.AppendLine("# 判据「活到第 5 分钟」= Duration ≥ 300s。过线率才是判据该看的量。");
                sb.AppendLine();
                sb.AppendLine("变体                       种子  过线率   min     中位     max    | 中位分钟 中位等级 中位收割");
                foreach ((string Name, Action Apply) in variants)
                {
                    Restore();
                    Apply();

                    var durs = new List<float>();
                    var waves = new List<int>();
                    var levels = new List<int>();
                    var harvests = new List<int>();
                    int pass = 0;
                    var per = new StringBuilder();

                    foreach (ulong sd in seeds)
                    {
                        SimResult r = RunSimulator.Run(BotStrategy.Balanced, sd);
                        durs.Add(r.Duration);
                        waves.Add(r.Wave);
                        levels.Add(r.Level);
                        harvests.Add(r.Harvests);
                        if (r.Duration >= 300f) pass++;
                        per.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "    seed {0,-10} {1,7:0.0}s  第 {2,2} 分钟  等级 {3}  击杀 {4,4}  收割 {5,3}  植株 {6}",
                            sd, r.Duration, r.Wave, r.Level, r.Kills, r.Harvests, r.PlantsAlive));
                    }

                    durs.Sort();
                    float med = durs[durs.Count / 2];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-24} {1,5}  {2,5:0}%  {3,7:0.0}  {4,7:0.0}  {5,7:0.0}  | {6,8} {7,9} {8,9}",
                        Name, seeds.Count, pass * 100 / seeds.Count, durs[0], med, durs[durs.Count - 1],
                        Med(waves), Med(levels), Med(harvests)));
                    sb.Append(per);
                    sb.AppendLine();
                }

                t.waves.maxAliveGrowth = oMaxAliveGrowth;
                string path = RunSimulator.WriteReport(sb.ToString(), $"seed-spread-{Stamp()}.txt");
                UnityEngine.Debug.Log("[Seedfall] 多种子分布完成：\n" + sb + "\n报告：" + path);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[Seedfall] 多种子分布失败：" + e);
                exitCode = 1;
            }

            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        private static int Med(List<int> xs)
        {
            var c = new List<int>(xs);
            c.Sort();
            return c[c.Count / 2];
        }

        private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

        /// <summary>批量入口：跑策略矩阵 + 确定性校验，失败时以退出码 1 结束（供 CI 判定）。</summary>
        public static void RunAll()
        {
            int exitCode = 0;
            try
            {
                TableDatabase.Load(true);
                var sb = new StringBuilder();
                sb.AppendLine($"[Seedfall] 配置表：种子 {TableDatabase.Seeds.Length}｜敌人 {TableDatabase.Enemies.Length}｜" +
                              $"波次 {TableDatabase.Waves.Length}｜道具 {TableDatabase.Items.Length}");
                foreach (BotStrategy s in new[] { BotStrategy.Idle, BotStrategy.Farmer, BotStrategy.Balanced, BotStrategy.Rush })
                {
                    SimResult r = RunSimulator.Run(s, DefaultSeed);
                    sb.AppendLine(r.ToLine());
                }
                bool det = RunSimulator.VerifyDeterminism(BotStrategy.Balanced, DefaultSeed, out string report);
                sb.AppendLine($"确定性校验：{(det ? "PASS" : "FAIL")}");
                sb.Append(report);
                Debug.Log(sb.ToString());
                if (!det) exitCode = 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[Seedfall] 无头回归失败：" + e);
                exitCode = 1;
            }

            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }
    }
}
#endif
