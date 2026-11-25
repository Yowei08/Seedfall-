using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v251：**Windows Player 的无头打包入口**（给 `unity_build` 用）★★
    ///
    /// 为什么新开这个文件：工程里原本**没有任何 `BuildPipeline.BuildPlayer` 入口**
    ///   ⇒ `unity_build -executeMethod` 无方法可调 ⇒ 只能手动点菜单，没法做"打包→跑一次→看 Player.log"的闭环 ✓
    ///
    /// 用法：
    ///   `unity_build method=Seedfall.BuildScript.PerformBuild target=StandaloneWindows64`
    /// 产物：`Build\Windows\Seedfall守园者.exe`
    ///
    /// 只做三件事：设场景列表 → `BuildPlayer` → 把结果写进日志并给出退出码。
    /// 不改任何播放器设置（那些是 `ProjectSettings/` 的事）。
    /// </summary>
    public static class BuildScript
    {
        private const string OutDir = "Build/Windows";
        private const string ExeName = "Seedfall守园者.exe";

        public static void PerformBuild()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string outPath = Path.Combine(projectRoot, OutDir, ExeName);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            // 场景：用 `EditorBuildSettings` 里登记的那三个（Login → Arena → Town），顺序照登记
            var scenes = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s != null && s.enabled) scenes.Add(s.path);
            Debug.Log($"[Build] 场景 {scenes.Count} 个：{string.Join(", ", scenes)}");
            Debug.Log($"[Build] 输出：{outPath}");

            var opts = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var summary = report.summary;
            Debug.Log($"[Build] 结果 = {summary.result} · 大小 {summary.totalSize / (1024 * 1024)} MB · " +
                      $"错误 {summary.totalErrors} · 警告 {summary.totalWarnings}");

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Build] 打包失败 ⇒ 退出码 1");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            else
            {
                Debug.Log("[Build] 打包成功 ⇒ 退出码 0");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        }
    }
}
