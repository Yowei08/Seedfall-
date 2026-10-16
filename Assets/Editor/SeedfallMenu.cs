#if UNITY_EDITOR
using System;
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
