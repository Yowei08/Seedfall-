// ★★ v251：**改成"局部"条件编译**（原来整文件包 `#if UNITY_EDITOR` 是错的）★★
// 为什么整文件包是错的：`Probe/ProbeV199SelfCheck.cs` 与 `Probe/ProbeV211HoeWindow.cs` 都在**运行时程序集**里
//   直接调 `SelfCheckRunner` ⇒ 整文件被条件编译掉时它们报 `CS0103: 'SelfCheckRunner' 不存在` ✗
//   ⇒ **类本身必须留在运行时**；只把"真正依赖 `UnityEditor` 的两处"包起来：
//      ① `using UnityEditor;`
//      ② `if (Application.isBatchMode) EditorApplication.Exit(code);`（连 `if` 一起包，不能只包半句）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v197：**自检的无头入口**（CI / batchmode 用）★★
    ///
    /// ── 为什么要这个文件 ──
    ///   自检的交互入口是 `DebugApi.SelfCheck()`（控制台命令），而它在
    ///   **编辑器主线程空闲**时才跑得起来；本轮桥的口子在主线程上有别的活
    ///   ⇒ `unity_build`（真正的 `-batchmode -executeMethod`）成了唯一稳的跑法 ✓
    ///   本文件就是那个 `-executeMethod` 的静态入口：加载表 → 跑 `SelfCheck.RunAll()`
    ///   → 把报告写到 `Library/yowei-v197-selfcheck.txt` → 按结果设退出码。
    ///
    /// ── 为什么退出码要区分"失败"与"跑不起来" ──
    ///   `-executeMethod` 抛异常时 Unity 自己会返回非 0，但那时**报告是空的** ——
    ///   分不清"自检里有断言红了"（2）与"进程崩了/表加载不了"（1）。
    ///   分开写，读日志的人一眼知道该看报告还是看 crash ✓
    ///
    /// ⚠️ 它**只读**：不写任何工程文件（报告落在 `Library/` —— 那是 Unity 的临时目录，
    ///    不进版本库、也不参与打包）✓
    /// </summary>
    public static class SelfCheckRunner
    {
        /// <summary>报告落盘路径（相对工程根）。</summary>
        public const string ReportPath = "Library/yowei-v197-selfcheck.txt";

        public static void RunBatch()
        {
            int code = Run();
            // ★ 只包这一句（`if` 与 `EditorApplication.Exit` 必须一起包，不能只包半句）★
            //   运行时（Player）里没有 `EditorApplication` ⇒ 这段不参与编译 ⇒ 类本体仍可在运行时被调用 ✓
#if UNITY_EDITOR
            if (Application.isBatchMode) EditorApplication.Exit(code);
#endif
        }

        /// <summary>跑一次并返回"红了几条"（-1 = 跑不起来）。报告同时写盘。</summary>
        public static int Run()
        {
            List<CheckResult> results;
            try
            {
                results = SelfCheck.RunAll();
            }
            catch (Exception ex)
            {
                string crash = "SELFCHECK CRASH: " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
                Debug.LogError(crash);
                TryWrite(crash);
                return -1;
            }

            var sb = new StringBuilder();
            sb.Append(SelfCheck.Report(results)).Append('\n');
            int fail = 0;
            for (int i = 0; i < results.Count; i++) if (!results[i].Passed) fail++;

            // 红的名字逐条列出来（报告里已经有明细，这里再列一遍是为了"只看这一行就知道哪几条"）
            sb.Append("\n================ 失败清单 ================\n");
            if (fail == 0) sb.Append("（无）\n");
            for (int i = 0; i < results.Count; i++)
                if (!results[i].Passed) sb.Append("FAIL  ").Append(results[i].Name).Append('\n');

            string text = sb.ToString();
            Debug.Log("SELFCHECK " + (results.Count - fail) + "/" + results.Count);
            TryWrite(text);
            return fail;
        }

        private static void TryWrite(string text)
        {
            try
            {
                string root = Directory.GetCurrentDirectory();
                string path = Path.Combine(root, ReportPath);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, text, new UTF8Encoding(false));
                Debug.Log("SELFCHECK 报告已写入 " + path);
            }
            catch (Exception ex)
            {
                Debug.LogError("SELFCHECK 报告写盘失败：" + ex.Message);
            }
        }

        /// <summary>
        /// ★ v252 诊断入口：**插件货位的档位分布**。无头跑：
        ///   `unity_build method=Seedfall.SelfCheckRunner.DumpPluginTiers`
        /// 为什么要有它：自检里那条"新表里的插件真的会被商店抽到"红了，报的是
        /// "6 件 S 档一件都没抽到"。而按权重算（S 0.5/100、40000 次刷新 ≈ 38000 个插件货位）
        /// **应当抽到近 200 次** ⇒ 要么档位压根掷不出 4，要么 S 档在候选池里被过滤掉了。
        /// 这条只读探针把"掷出来的档位直方图 / S 档候选数 / S 档实际命中数"打成一张表，
        /// 用数据分辨这两种可能 ✓（不写任何工程文件，只写一份 Library 下的报告）
        /// </summary>
        public static void DumpPluginTiers()
        {
            const string path = "Library/yowei-v252-plugin-tiers.txt";
            var sb = new StringBuilder(2048);
            try
            {
                TableDatabase.Load(true);
                PluginAsset[] all = TableDatabase.Plugins;
                sb.Append("插件表 ").Append(all.Length).Append(" 件｜候选池（过解锁门）")
                  .Append(ShopSystem.PluginCandidates().Length).Append(" 件\n");
                int[] byTier = new int[5];
                for (int i = 0; i < all.Length; i++) byTier[(int)all[i].quality]++;
                sb.Append("表内各档件数 D/C/B/A/S = ");
                for (int i = 0; i < 5; i++) sb.Append(byTier[i]).Append(i < 4 ? "/" : "\n");

                PluginAsset[] pool = ShopSystem.PluginCandidates();
                int[] poolTier = new int[5];
                for (int i = 0; i < pool.Length; i++) poolTier[(int)pool[i].quality]++;
                sb.Append("候选池各档件数 D/C/B/A/S = ");
                for (int i = 0; i < 5; i++) sb.Append(poolTier[i]).Append(i < 4 ? "/" : "\n");
                for (int i = 0; i < pool.Length; i++)
                    if ((int)pool[i].quality == 4) sb.Append("  S 档候选：").Append(pool[i].id).Append('\n');

                // 真空跑：建一局、刷 4000 次，统计 plugin 货位的档位与 id
                var s = new RunSession();
                s.Start(12345UL);
                var seen = new System.Collections.Generic.Dictionary<string, int>();
                int[] gotTier = new int[5];
                int pluginSlots = 0;
                for (int r = 0; r < 4000; r++)
                {
                    s.Shop.Refresh(s);
                    for (int k = 0; k < s.Shop.Offers.Count; k++)
                    {
                        if (s.Shop.Offers[k].Kind != OfferKind.Plugin) continue;
                        PluginAsset p = s.Shop.Offers[k].Plugin;
                        if (p == null) continue;
                        pluginSlots++;
                        gotTier[(int)p.quality]++;
                        seen.TryGetValue(p.id, out int n);
                        seen[p.id] = n + 1;
                    }
                }
                sb.Append("4000 次刷新、插件货位 ").Append(pluginSlots).Append(" 个｜实抽各档 D/C/B/A/S = ");
                for (int i = 0; i < 5; i++) sb.Append(gotTier[i]).Append(i < 4 ? "/" : "\n");
                sb.Append("抽到过的不同插件 ").Append(seen.Count).Append(" 种\n");
                foreach (var kv in seen) sb.Append("  ").Append(kv.Key).Append(" ×").Append(kv.Value).Append('\n');
            }
            catch (Exception ex)
            {
                sb.Append("CRASH: ").Append(ex);
            }
            Debug.Log(sb.ToString());
            try
            {
                string full = Path.Combine(Directory.GetCurrentDirectory(), path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Debug.LogError("写盘失败：" + ex.Message); }
        }
    }
}

