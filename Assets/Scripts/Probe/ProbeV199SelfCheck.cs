using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v199：**自检的一次性触发探针（只读，默认不生效）** ★★
    ///
    /// ── 为什么需要它 ──
    ///   自检的正常入口是 `DebugApi.SelfCheck()`（控制台命令）与
    ///   `SelfCheckRunner.RunBatch()`（`-executeMethod` 无头入口）。
    ///   而本轮编辑器**正开着这个工程**（`Temp/UnityLockfile` 被占），
    ///   batchmode 打不开同一份工程 ⇒ 无头入口用不了；
    ///   控制台命令又需要一个输入通道，桥没有。
    ///   ⇒ 唯一的通道是"进播放模式时跑一次"：`RuntimeInitializeOnLoadMethod` ✓
    ///
    /// ── 与 `SelfCheckRunner` 的分工 ──
    ///   `SelfCheckRunner.Run()` 只在失败时把明细写进报告（`SelfCheck.Report` 的既定行为）。
    ///   本轮要交的证据里包含"**改过的那几条断言的运行读数**"，所以这里多写一份
    ///   `Library/yowei-v199-selfcheck.txt`：全量计数 + 所有 `SEED-` 断言的原文与读数 ✓
    ///   两份报告都在 `Library/`（Unity 的临时目录，不进版本库、不进包）✓
    ///
    /// ── 为什么默认不生效（这一条最重要）──
    ///   它跑的是**全量自检（472 条）**，每次进播放都跑会拖慢每一次试玩 ✗
    ///   ⇒ 只有 `Library/.yowei-run-selfcheck` **存在**时才跑，跑完就删掉那个旗标文件 ⇒
    ///     · 平时它对游戏**完全没有影响**（一次 `File.Exists` 就返回）✓
    ///     · 要跑自检：让 `Library/.yowei-run-selfcheck` 存在 ⇒ 进播放 ⇒ 读报告 ✓
    ///
    /// ── 只读性 ──
    ///   它只调 `SelfCheck.RunAll()`：纯粹的断言集合，**不写任何工程文件**，
    ///   只往 `Library/` 写报告 ✓ 它**不**改场景、不建物体、不碰存档 ——
    ///   所以不需要往场景里放任何东西 ✓
    /// </summary>
    public static class ProbeV199SelfCheck
    {
        /// <summary>旗标文件（相对工程根）。存在 ⇒ 本次进播放跑一次自检，然后删掉它。</summary>
        public const string FlagPath = "Library/.yowei-run-selfcheck";

        /// <summary>本轮证据报告（相对工程根）。</summary>
        public const string EvidencePath = "Library/yowei-v199-selfcheck.txt";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            try
            {
                string root = Directory.GetCurrentDirectory();
                string flag = Path.Combine(root, FlagPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(flag)) return;          // ← 常态：直接返回，零影响
                File.Delete(flag);                        // ← 一次性：跑完就撤旗，不会重复跑

                // ① 走既有的正式入口（写标准报告 + 打 SELFCHECK n/N 到控制台）
                int fail = SelfCheckRunner.Run();

                // ② 再取一份"改过的那几条断言"的原文与读数（不重复跑 RunAll：这一次自己跑）
                List<CheckResult> results = SelfCheck.RunAll();
                var sb = new StringBuilder();
                int bad = 0;
                for (int i = 0; i < results.Count; i++) if (!results[i].Passed) bad++;
                sb.AppendLine($"v199 自检：{results.Count - bad}/{results.Count} 通过（失败 {bad} 条）");
                sb.AppendLine("—— SEED-* 断言原文与运行读数（本轮动过的是 SEED-ARCHETYPE / SEED-TIER-RATE）——");
                for (int i = 0; i < results.Count; i++)
                    if (results[i].Name != null && results[i].Name.StartsWith("SEED-", StringComparison.Ordinal))
                        sb.AppendLine("  " + results[i]);
                sb.AppendLine("—— 失败清单 ——");
                if (bad == 0) sb.AppendLine("  （无）");
                for (int i = 0; i < results.Count; i++)
                    if (!results[i].Passed) sb.AppendLine("  " + results[i]);

                string path = Path.Combine(root, EvidencePath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Debug.Log($"PROBE-V199 证据已写入 {EvidencePath}（失败 {bad} 条；SelfCheckRunner 侧失败 {fail} 条）");
            }
            catch (Exception ex)
            {
                // 探针自己出错不能污染自检结论 —— 单独一条前缀，一眼区分
                Debug.LogError("PROBE-V199 异常：" + ex.GetType().Name + "：" + ex.Message);
            }
        }
    }
}
