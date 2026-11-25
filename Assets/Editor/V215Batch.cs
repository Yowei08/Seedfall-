using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v215：**批处理入口（只读）** —— 让 `自检` 与 `v215 探针` 能在无头模式下跑。
    ///
    ///   Unity.exe -batchmode -quit -projectPath &lt;工程&gt; -executeMethod Seedfall.V215Batch.Run
    ///
    /// 为什么另开一个而不是复用 `V197Batch`：
    ///   `V197Batch` 跑的是 `ProbeV197`（上一轮的读数），与本轮的换种无关；
    ///   两个入口各自只读、各写各的输出文件 ✓
    ///
    /// ★ 只读 ★ 只做三件事：`TableDatabase.Load`、`SelfCheck.RunAll`、`ProbeV215SeedSwap.RunReport`，
    /// 结果写进 `Library/yowei-v215-probe.txt`（`Library/` 不参与版本管理，不进 `Assets/**`）。
    /// 不写 `Assets/**`、不改场景、不进 Play ✓
    ///
    /// 自检一起跑的理由：本轮改了 `FarmGrid`（新增 `DetachColumnSeed`）与 `RunSession`
    /// （新增换种；`FrameInput` 多一个字段）⇒ 既有断言必须仍然是绿的，否则就是回归 ✓
    ///
    /// 退出码：自检全绿 = 0，有红 = 1；探针崩溃 = 2（`unity_build` 的 exit code 直接可读）
    /// </summary>
    public static class V215Batch
    {
        public static void Run()
        {
            int exit = 0;
            try
            {
                var all = new StringBuilder();
                all.AppendLine("═══ v215 批处理（只读）═══");

                TableDatabase.Load(true);
                List<CheckResult> results = SelfCheck.RunAll();
                int total = results.Count, pass = 0;
                for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
                all.AppendLine($"自检 {(pass == total ? "PASS" : "FAIL")}：{pass}/{total} 通过");
                all.AppendLine(SelfCheck.Report(results));
                if (pass != total) exit = 1;

                all.AppendLine(ProbeV215SeedSwap.RunReport());

                string dir = Path.Combine(Path.Combine(Application.dataPath, ".."), "Library");
                Directory.CreateDirectory(dir);
                string outPath = Path.Combine(dir, "yowei-v215-probe.txt");
                File.WriteAllText(outPath, all.ToString(), new UTF8Encoding(false));
                Debug.Log(all.ToString());
                Debug.Log("V215BATCH 结果已写入 " + outPath);
            }
            catch (Exception e)
            {
                Debug.LogError("V215BATCH CRASH " + e.GetType().Name + ": " + e.Message + "\n" + (e.StackTrace ?? ""));
                exit = 2;
            }
            EditorApplication.Exit(exit);
        }
    }
}
