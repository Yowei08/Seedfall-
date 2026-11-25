#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v193 只读取证探针：**谁在每帧跑？（点名 `EditorApplication.update` 的订阅者）** ★★
    ///
    /// ── 为什么需要它 ──
    /// Editor.log 里那条 `Access version should be odd when acquiring lock` 的速率
    /// 与 `EditorApplication.update` 的**调用次数 1:1**（实测：窗口最小化 9 次/秒 ⇒ 9 行/秒；
    /// 窗口获得焦点后 `fps=17086` ⇒ 16640 行/秒）⇒ 有某个 update 回调**每帧**做一次会取
    /// 资产锁的调用。而全工程 `Assets/` 下的订阅点只有 `YoweiBridge.Tick`（节流 1 秒）
    /// 与 `YoweiBridge.DrainMainThread`（只是取队列），第三方包 `com.coplaydev.unity-mcp`
    /// 里的几处也都各自节流 —— **靠读代码已经无法继续收敛** ⇒ 直接把名单打出来 ✓
    ///
    /// ── 它做什么 ──
    /// **只读**：反射取出 `EditorApplication.update` 这个事件背后的委托，遍历
    /// `GetInvocationList()`，把每一项的「声明类型.方法名（目标对象类型）」打成一行。
    /// 不改任何订阅、不调用任何业务 API、不写工程文件。
    /// 报告走 `Log.Info`（⇒ 桥 `/log` 与 Editor.log 都能读到）✓
    ///
    /// ── 怎么开 ──
    /// `<工程>/Library/yowei_v193_who.on` 存在时，**每次域重载**跑一次（延迟 2 秒，
    /// 等各包自己的 `[InitializeOnLoad]` 注册完）✓  删掉它 ⇒ 静默 ✓
    /// </summary>
    internal static class ProbeV193Who
    {
        private static string ProjRoot => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei_v193_who.on");

        private static int _dumpRound;

        [InitializeOnLoadMethod]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            // 域重载刚结束时，别的包可能还没注册完；延迟 2 秒再取名单
            EditorApplication.delayCall += () => { if (!File.Exists(FlagPath)) return; Dump("延迟 2 秒"); };
        }

        private static void Dump(string when)
        {
            var sb = new StringBuilder();
            _dumpRound++;
            sb.AppendLine($"═══ PROBEV193WHO #{_dumpRound}（{when}）═══");
            AppendEvent(sb, "update");
            AppendEvent(sb, "delayCall");
            string text = sb.ToString();
            Log.Info(text);
            try { File.WriteAllText(Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei-v193-who.txt"), text); }
            catch { /* 只是留档，失败不影响日志那条 */ }
        }

        /// <summary>把 `EditorApplication.&lt;eventName&gt;` 的订阅者逐条打出来（只读）。</summary>
        private static void AppendEvent(StringBuilder sb, string eventName)
        {
            FieldInfo field = typeof(EditorApplication).GetField(
                eventName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (field == null)
            {
                sb.AppendLine($"  {eventName}：**反射不到背后的委托字段**（本版 Unity 的字段名可能不同）");
                return;
            }

            Delegate handler = field.GetValue(null) as Delegate;
            if (handler == null)
            {
                sb.AppendLine($"  {eventName}：0 个订阅者");
                return;
            }

            Delegate[] list = handler.GetInvocationList();
            sb.AppendLine($"  {eventName}：{list.Length} 个订阅者");

            // 同名同目标的重复订阅会把这个回调的执行次数乘上去 ⇒ 顺便统计重复
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (Delegate d in list)
            {
                string target = d.Target == null ? "(静态)" : d.Target.GetType().FullName;
                string key = (d.Method.DeclaringType != null ? d.Method.DeclaringType.FullName : "?")
                             + "." + d.Method.Name + "  @ " + target;
                if (!seen.ContainsKey(key)) { seen[key] = 0; order.Add(key); }
                seen[key]++;
            }

            int shown = 0;
            foreach (string key in order)
            {
                if (shown >= 40) { sb.AppendLine("    …（还有更多，已截断）"); break; }
                sb.AppendLine($"    · {key}" + (seen[key] > 1 ? $"   ×{seen[key]}（重复订阅！）" : ""));
                shown++;
            }
        }
    }
}
#endif
