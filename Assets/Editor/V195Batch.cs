using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v195：**批处理入口**（只读）—— 让 `自检` 与 `v195 探针` 能在无头模式下跑。
    ///
    ///   Unity.exe -batchmode -quit -projectPath &lt;工程&gt; -executeMethod Seedfall.V195Batch.Run
    ///
    /// ★ 为什么需要它 ★ 编辑器窗口里的桥（`Assets/Editor/YoweiBridge.cs`）要靠
    /// `EditorApplication.update` 排空主线程队列；本轮实测那个主线程**不再排空**
    /// （`/command` 返回 `{"queued":…}` 之后 `[YoweiBridge] ran …` 一行日志都不出现，
    /// `/log?since=` 的 seq 也不涨）⇒ 窗口里跑不动 ⇒ 改走**批处理**这条路 ✓
    ///
    /// ★ 它是**只读**的 ★ 只做三件事：`TableDatabase.Load`、`SelfCheck.RunAll`、
    /// `ProbeV195Num.Run`，然后把结果写进 `Library/`（工程已经被 `.gitignore` 忽略的目录）。
    /// 不写 `Assets/**`、不改场景、不进 Play ✓
    ///
    /// 退出码：自检全绿 = 0，有红 = 1（让 CI 与 `unity_build` 的 exit code 都能看出来）
    /// </summary>
    public static class V195Batch
    {
        public static void Run()
        {
            int exit = 0;
            try
            {
                int pass = 0, total = 0;
                StringBuilder all = new StringBuilder();
                all.AppendLine("═══ v195 批处理（只读）═══");

                TableDatabase.Load(true);
                List<CheckResult> results = SelfCheck.RunAll();
                total = results.Count;
                for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
                all.AppendLine($"自检 {(pass == total ? "PASS" : "FAIL")}：{pass}/{total} 通过");
                all.AppendLine(SelfCheck.Report(results));
                if (pass != total) exit = 1;

                all.AppendLine(ProbeV195Num.Run());

                string path = Path.Combine(Path.Combine(Application.dataPath, ".."), "Library");
                Directory.CreateDirectory(path);
                string outPath = Path.Combine(path, "yowei-v195-probe.txt");
                File.WriteAllText(outPath, all.ToString(), new UTF8Encoding(false));
                Debug.Log(all.ToString());
                Debug.Log("V195BATCH 结果已写入 " + outPath);
            }
            catch (Exception e)
            {
                Debug.LogError("V195BATCH CRASH " + e.GetType().Name + ": " + e.Message + "\n" + (e.StackTrace ?? ""));
                exit = 2;
            }
            EditorApplication.Exit(exit);
        }
    }
}
