// ★ v275：把 Unity 编辑器自身的那条噪音警告从控制台里过滤掉。
//
// 背景（已两轮取证，结论确定）：
//   `Deleting invalid font reference.` 来自 Unity 编辑器内部
//   `TextSettings.InitializeFontReferenceLookup()` ← `EditorTextSettings.CreateDefaultEditorFontAsset()`
//   每次**域重载**后，编辑器为它自己的 IMGUI 文本框（`EditorGUI.s_RecycledEditor`）重建默认字体，
//   发现内置字体引用表里有几条指向已不存在的字体 —— Unity 会自动删掉它们（这就是 "Deleting" 的意思）。
//
//   工程侧无法修复：
//     · Assets 里零字体资产、零字体引用（`m_Font|m_FontData|font:` 全库 0 命中）
//     · 没装 TextMeshPro / TextCore 包
//     · ProjectSettings 的 23 个 .asset 里没有任何字体字段，也没有 TextSettings.asset
//     · Library 里没有这套设置的缓存 —— 源在 Unity 安装目录，清缓存无效
//   运行时 UI 走 `Font.CreateDynamicFontFromOSFont`，根本不碰这套设置 ⇒ 对游戏零影响。
//
// 所以只能**在日志层拦**：只拦这一条精确前缀，其余任何一条日志（包括所有 error/warning）
// 都原样转交给 Unity 原本的 handler，不做任何过滤。想恢复原样就把本文件删掉。

#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Seedfall.EditorTools
{
    [UnityEditor.InitializeOnLoad]
    internal static class EditorLogFilter
    {
        /// <summary>要屏蔽的**唯一**一条消息前缀（精确匹配开头，不做模糊匹配）。</summary>
        private const string MutedPrefix = "Deleting invalid font reference.";

        /// <summary>v275 起屏蔽次数（Console 里可以 `EditorLogFilter.MutedCount` 查，证明确实拦到了）。</summary>
        internal static int MutedCount { get; private set; }

        static EditorLogFilter()
        {
            Install();
        }

        private static void Install()
        {
            ILogHandler inner = Debug.unityLogger.logHandler;
            if (inner is MutingHandler) return;              // 已装过（域重载后静态字段会清空，这里兜底防重复包装）
            Debug.unityLogger.logHandler = new MutingHandler(inner);
        }

        private sealed class MutingHandler : ILogHandler
        {
            private readonly ILogHandler _inner;

            internal MutingHandler(ILogHandler inner) { _inner = inner; }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                if (IsMuted(format)) { MutedCount++; return; }
                _inner.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                // 异常一律原样转交：这条过滤器只针对上面那条已知噪音，绝不吞异常。
                _inner.LogException(exception, context);
            }

            private static bool IsMuted(string format)
            {
                return !string.IsNullOrEmpty(format)
                       && format.StartsWith(MutedPrefix, StringComparison.Ordinal);
            }
        }

        [UnityEditor.MenuItem("Tools/Seedfall/日志过滤：报告已屏蔽条数")]
        private static void Report()
        {
            Debug.Log($"[Seedfall] EditorLogFilter 已屏蔽 `{MutedPrefix}` × {MutedCount} 条（本会话）。");
        }
    }
}
#endif
