using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v197：**批处理入口（只读）** —— 让 `自检` 与 `v197 探针` 能在无头模式下跑。
    ///
    ///   Unity.exe -batchmode -quit -projectPath &lt;工程&gt; -executeMethod Seedfall.V197Batch.Run
    ///
    /// 为什么另开一个而不是复用 `V195Batch`：
    ///   `V195Batch` 会顺带跑 `ProbeV195Num`（v195 那一轮的读数），与本轮无关；
    ///   本轮要跑的是 `SelfCheck.RunAll` + `ProbeV197`。两个入口各自只读、互不覆盖输出文件 ✓
    ///
    /// ★ 只读 ★ 只做三件事：`TableDatabase.Load`、`SelfCheck.RunAll`、`ProbeV197.Run`，
    /// 结果写进 `Library/yowei-v197-probe.txt`（工程里已被 `.gitignore` 忽略的目录）。
    /// 不写 `Assets/**`、不改场景、不进 Play ✓
    ///
    /// 退出码：自检全绿 = 0，有红 = 1（`unity_build` 的 exit code 能直接看出来）
    /// </summary>
    public static class V197Batch
    {
        public static void Run()
        {
            int exit = 0;
            try
            {
                var all = new StringBuilder();
                all.AppendLine("═══ v197 批处理（只读）═══");

                TableDatabase.Load(true);
                List<CheckResult> results = SelfCheck.RunAll();
                int total = results.Count, pass = 0;
                for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
                all.AppendLine($"自检 {(pass == total ? "PASS" : "FAIL")}：{pass}/{total} 通过");
                all.AppendLine(SelfCheck.Report(results));
                if (pass != total) exit = 1;

                all.AppendLine(ProbeV197.Run());

                string dir = Path.Combine(Path.Combine(Application.dataPath, ".."), "Library");
                Directory.CreateDirectory(dir);
                string outPath = Path.Combine(dir, "yowei-v197-probe.txt");
                File.WriteAllText(outPath, all.ToString(), new UTF8Encoding(false));
                Debug.Log(all.ToString());
                Debug.Log("V197BATCH 结果已写入 " + outPath);
            }
            catch (Exception e)
            {
                Debug.LogError("V197BATCH CRASH " + e.GetType().Name + ": " + e.Message + "\n" + (e.StackTrace ?? ""));
                exit = 2;
            }
            EditorApplication.Exit(exit);
        }
    }
}
