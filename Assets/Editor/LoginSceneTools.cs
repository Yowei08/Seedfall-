#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seedfall.EditorTools
{
    /// <summary>
    /// ★★ 登录界面的**编辑器侧**工具：把它钉成"启动场景"，并一键打开运行 ★★
    ///
    /// 背景（用户报的阻塞级问题）：`Assets/Scenes/Login.unity` 上一轮做完了，
    /// 但 **Build Settings 里没有它**（当时只有 `SeedfallArena` 第 1、`Town` 第 2）
    /// ⇒ 播放器一启动就落在战斗场景，登录界面**从未被加载**。
    ///
    /// 这个文件只做三件"人点一下就好"的事，**不改任何别人的文件**：
    ///   ① 设为启动场景 —— 用 `EditorBuildSettings.scenes` 写（Unity 自己会落盘 `ProjectSettings/EditorBuildSettings.asset`）
    ///   ② 打开并运行 —— 编辑器 Play 是"跑当前打开的那张图"，所以先 `OpenScene(Login)` 再 `isPlaying = true`
    ///   ③ 打印顺序 —— 一眼核对，不用去翻 Build Settings 窗口
    ///
    /// ⚠️ 与 `LoginEntry`（`#if !UNITY_EDITOR` 的运行期闸门）分工：
    ///    编辑器里的 Play 语义**保持不变**（跑当前场景）——自检与各路探针都靠它；
    ///    播放器里的启动场景由 ① 决定，并由 `LoginEntry` 兜底纠正 ✓
    /// </summary>
    public static class LoginSceneTools
    {
        private const string LoginScenePath = "Assets/Scenes/Login.unity";

        [MenuItem("Tools/Seedfall/★ 登录界面：设为启动场景（Build Settings 第 1 条）", false, 10)]
        public static void MakeLoginFirst()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            EditorBuildSettingsScene login = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].path, LoginScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    login = list[i];
                    list.RemoveAt(i);
                    break;
                }
            }
            if (login == null) login = new EditorBuildSettingsScene(LoginScenePath, true);
            login.enabled = true;
            list.Insert(0, login);

            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[Login] Build Settings 已更新：\n" + Describe(list));
        }

        [MenuItem("Tools/Seedfall/★ 登录界面：打开并运行", false, 11)]
        public static void OpenAndPlay()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Login] 已经在 Play 中，先 Stop 再点这个菜单");
                return;
            }
            if (!System.IO.File.Exists(LoginScenePath))
            {
                Debug.LogError("[Login] 找不到 " + LoginScenePath);
                return;
            }
            EditorSceneManager.OpenScene(LoginScenePath, OpenSceneMode.Single);
            Debug.Log("[Login] 已打开 " + LoginScenePath + "，开始 Play");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/Seedfall/登录界面：打印 Build Settings 顺序", false, 12)]
        public static void PrintBuildOrder()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            string text = Describe(list);
            if (list.Count == 0 || list[0].path != LoginScenePath)
                Debug.LogWarning("[Login] ⚠ 第 1 条不是登录场景 ⇒ 打包后一开游戏不会出现登录界面\n" + text);
            else
                Debug.Log("[Login] ✔ 第 1 条是登录场景 ⇒ 打包后先进登录界面\n" + text);
        }

        private static string Describe(List<EditorBuildSettingsScene> list)
        {
            var sb = new StringBuilder(256);
            sb.Append("Build Settings 共 ").Append(list.Count).Append(" 条：");
            for (int i = 0; i < list.Count; i++)
                sb.Append('\n').Append("  ").Append(i).Append(": ").Append(list[i].path)
                  .Append(list[i].enabled ? "" : "（未勾选）")
                  .Append(i == 0 ? "   ← 启动场景" : "");
            return sb.ToString();
        }
    }
}
#endif
