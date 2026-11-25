using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ 本地化自检探针 ★ —— 每次进 play 打两行，供自动化与交付报告取数。
    ///
    /// 为什么要常驻一个探针、而不是"手工跑一次"：
    ///   · 这是**唯一**能在"没有 UI 入口"的当前版本里证明 `L10n` 真的加载到了词表、
    ///     回退链真的按 当前→En→Zh 走、数字占位符真的回填的手段（`SelfTest()` 会
    ///     把 6 门语言各切一次，然后切回玩家原来的选择）；
    ///   · 工程里已经有 `Scripts/Probe/**` 一整套同样口径的探针（`ProbeV19x`），
    ///     这条与它们同风格：**只读、不改游戏状态、每次运行打一行**。
    ///
    /// 一行是覆盖读数（缺多少条），一行是同一 key 在 6 门语言下的实际输出。
    /// </summary>
    internal static class L10nProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Report()
        {
            try
            {
                Debug.Log(L10n.Report());
                Debug.Log(L10n.SelfTest());
                MaybeRunSelfCheck();
            }
            catch (System.Exception e)
            {
                // 自检绝不能拦住开局
                Debug.LogWarning("[L10n] 自检探针抛异常（已吞，游戏照常跑）：" + e.Message);
            }
        }

        /// <summary>
        /// 一次性自检入口（**只有旗标文件存在时才跑**）：`Library/.l10n-run-selfcheck`
        ///
        /// 为什么根目录从 `Application.dataPath` 反推、而不是 `Directory.GetCurrentDirectory()`：
        /// 编辑器的 CWD **不一定**是工程根（本机实测不是），照 CWD 拼路径会"旗标看不见、探针静默返回"。
        /// 这也正是既有的 `ProbeV199SelfCheck`（按 CWD 拼）这一轮**没被触发**的原因 ——
        /// 那不在本次可改范围，所以这里自己带一条能跑的。
        ///
        /// 产出：`Library/l10n-selfcheck.txt` 里的 `SELFCHECK n/N`，并打一行日志供 `unity_log` 直接读。
        /// </summary>
        private static void MaybeRunSelfCheck()
        {
            string root = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
            if (string.IsNullOrEmpty(root)) return;
            string flag = System.IO.Path.Combine(root, "Library", ".l10n-run-selfcheck");
            if (!System.IO.File.Exists(flag)) return;      // 常态：一行代价，直接返回
            try { System.IO.File.Delete(flag); } catch { /* 删不掉也要继续跑 */ }

            var list = Seedfall.SelfCheck.RunAll();
            int bad = 0;
            for (int i = 0; i < list.Count; i++) if (!list[i].Passed) bad++;
            string head = "SELFCHECK " + (list.Count - bad) + "/" + list.Count + "（失败 " + bad + " 条）";
            var sb = new System.Text.StringBuilder(8192);
            sb.AppendLine(head);
            sb.AppendLine(Seedfall.SelfCheck.Report(list));
            for (int i = 0; i < list.Count; i++)
                if (!list[i].Passed) sb.AppendLine("  FAIL " + list[i]);
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Library", "l10n-selfcheck.txt"),
                                        sb.ToString(), new System.Text.UTF8Encoding(false));
            Debug.Log("[L10n] " + head);
        }
    }
}
