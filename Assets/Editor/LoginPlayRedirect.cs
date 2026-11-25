#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Seedfall.EditorTools
{
    /// <summary>
    /// ★ 让「在编辑器里按 Play」直接等于「进登录界面」——**带开关，默认开** ★
    ///
    /// 为什么需要它（用户反复报的那个 bug）：
    ///   `ProjectSettings/EditorBuildSettings.asset` 里登录场景确实是第 1 条 ✓，
    ///   可那**只决定打包后**从哪张图启动；
    ///   编辑器里按 Play 跑的永远是**当前打开的那张场景** ✗
    ///   ⇒ 用户开 `Town` / `SeedfallArena` 时按 Play，永远看不到登录界面。
    ///   上一轮留下的 `Tools/Seedfall/★ 登录界面：打开并运行` 是**一次性**的：
    ///   点一次就完了，下次再按 Play 又回到老样子 ⇒ 他要的是「按 Play 就该看到登录」。
    ///
    /// 所以这里补一个**持久**的开关：
    ///   · 开 ⇒ `ExitingEditMode` 时若当前场景不是 `Login`，先切到 `Login` 再进 Play；
    ///          退出 Play 再还原回原来那张场景（记住的是路径，只存本次编辑器会话）。
    ///   · 关 ⇒ **一个字都不做**，Play 的语义与 Unity 默认完全一致。
    ///          ★ 退路仍在：真要在某个场景里手动 Play 跑探针，点一次菜单关掉即可，不用重启编辑器。
    ///
    /// ★ 为什么**默认开**（用户硬要求：「登录永远是第一」）★
    ///   用户每次在编辑器里按 Play 都落在战斗/主城 ⇒「登录界面不加载」反复报；
    ///   而真正需要「Play 就停当前场景」的场合是**无头自检** —— 它走
    ///   `-executeMethod Seedfall.SelfCheckRunner.RunBatch` 批处理，**根本不进编辑器的 Play**，
    ///   `playModeStateChanged` 一次都不会触发 ⇒ 默认开**不会**把自检踢走 ✓
    ///   编辑器里手动 Play 跑自检是偶发，用户每次 Play 都被踢是经常 ⇒ 经常的那个优先。
    ///
    /// ⚠️ 已存过旧默认值（false）的机器：`EditorPrefs` 里**存的值优先于默认值**，
    ///    只改默认值等于没改 ⇒ 这里用 `DefaultOnAppliedKey` 做一次性纠正，把开关落成「开」。
    ///    只纠正**一次**：之后用户手动关掉就永久有效，退路不会被反复覆盖。
    ///
    /// 不做的事（硬约束）：
    ///   · 不碰 `Assets/Scripts/**`（运行期闸门 `LoginEntry` 的 `#if !UNITY_EDITOR` 原样保留）；
    ///   · 不碰 `ProjectSettings/**`（不改 Build Settings，不改任何工程设置）；
    ///   · 不写场景文件（场景有未保存修改时只弹标准「是否保存」框，取消了就不换场景）。
    /// </summary>
    [InitializeOnLoad]
    public static class LoginPlayRedirect
    {
        /// <summary>登录场景路径（与 `LoginSceneTools` 同一份事实）。</summary>
        public const string LoginScenePath = "Assets/Scenes/Login.unity";

        /// <summary>
        /// ★ 开关的 `EditorPrefs` 键名 ★ —— 取不到这个键时按 **true（开）** 处理，
        /// 这就是「默认开」的实现方式。
        /// </summary>
        public const string EnabledKey = "Seedfall.Login.PlayRedirect";

        /// <summary>
        /// 「默认开」的一次性落地标记：旧版本把开关存成 false 的机器，升级到这里也纠正成开。
        /// 只纠正一次 ⇒ 之后用户手动关掉就永久有效（退路不被覆盖）。
        /// </summary>
        private const string DefaultOnAppliedKey = "Seedfall.Login.PlayRedirect.DefaultOnApplied";

        /// <summary>跳转前那张场景的路径，退出 Play 后据此还原（`SessionState`：关掉编辑器即失效）。</summary>
        private const string ReturnSceneKey = "Seedfall.Login.ReturnScene";

        /// <summary>「这次 Play 的跳转是本工具做的」标记，避免别人换的场景被我们还原掉。</summary>
        private const string RedirectedKey = "Seedfall.Login.Redirected";

        /// <summary>菜单路径（带勾选态；再点一次就切回来，不用重启编辑器）。</summary>
        private const string MenuPath = "Tools/Seedfall/登录取向：Play 时自动进登录（开/关）";

        private const string Tag = "[Login] ";

        /// <summary>开关本体：键不存在 ⇒ true（默认开）✓</summary>
        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(EnabledKey, true); }
            set { EditorPrefs.SetBool(EnabledKey, value); }
        }

        static LoginPlayRedirect()
        {
            ApplyDefaultOnOnce();

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// 把「默认开」落到**已经存过 false 的机器**上（否则改默认值没用：存的值优先）。
        /// 只做一次 —— 用 `DefaultOnAppliedKey` 记住做过，之后尊重用户手动关掉的选择。
        /// </summary>
        private static void ApplyDefaultOnOnce()
        {
            if (EditorPrefs.GetBool(DefaultOnAppliedKey, false)) return;

            bool before = EditorPrefs.GetBool(EnabledKey, true);
            EditorPrefs.SetBool(EnabledKey, true);
            EditorPrefs.SetBool(DefaultOnAppliedKey, true);

            Debug.Log(Tag + "登录取向默认值 = **开**（此前存的值是 " + (before ? "开" : "关")
                      + "）⇒ 现在按 Play 会先进 " + LoginScenePath + "。\n"
                      + Tag + "        无头自检（-executeMethod）不经编辑器 Play，不受这个开关影响；\n"
                      + Tag + "        某次要在原地 Play 跑探针时，菜单「" + MenuPath + "」点一下关掉即可，只关这一次。");
        }

        [MenuItem(MenuPath, false, 13)]
        public static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled)
            {
                Debug.Log(Tag + "登录取向 = **开** ⇒ 从现在起按 Play 会先打开 " + LoginScenePath
                          + " 再进播放模式（" + EnabledKey + " = true）。\n"
                          + Tag + "        想回到「按 Play 就跑当前场景」，再点一次本菜单即可，不用重启编辑器。\n"
                          + Tag + "        注意：开着的时候，在别的场景里按 Play 会被切到登录场景 —— "
                          + "跑那些「在当前场景自检」的探针前，请先关掉它。");
            }
            else
            {
                Debug.Log(Tag + "登录取向 = **关** ⇒ 按 Play 回到 Unity 默认行为：跑当前打开的那张场景（"
                          + EnabledKey + " = false）。\n"
                          + Tag + "        这是**手动退路**，会一直保持；想恢复「Play 先进登录」再点一次本菜单。\n"
                          + Tag + "        无头自检（-executeMethod 批处理）本来就不经编辑器 Play，开关开或关都不影响它。");
            }
        }

        [MenuItem(MenuPath, true, 13)]
        public static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) HandleBeforePlay();
            else if (state == PlayModeStateChange.EnteredEditMode) HandleAfterStop();
        }

        /// <summary>按 Play 之前：开关开着且不在登录场景 ⇒ 先切场景。</summary>
        private static void HandleBeforePlay()
        {
            if (!Enabled) return;

            string from = ActiveScenePath();
            if (IsLogin(from))
            {
                Debug.Log(Tag + "开关 = 开，但当前就是 " + LoginScenePath + " ⇒ 不用跳");
                return;
            }
            if (!File.Exists(LoginScenePath))
            {
                Debug.LogError(Tag + "开关 = 开，但找不到 " + LoginScenePath + " ⇒ 这次按当前场景播放");
                return;
            }

            var active = EditorSceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning(Tag + "开关 = 开，但当前场景有未保存修改且你取消了保存 ⇒ 这次按当前场景播放");
                    return;
                }
            }

            SessionState.SetString(ReturnSceneKey, from);
            SessionState.SetBool(RedirectedKey, true);

            EditorSceneManager.OpenScene(LoginScenePath, OpenSceneMode.Single);

            Debug.Log(Tag + "开关 = 开 ⇒ 已把场景从 " + Describe(from) + " 切到 " + LoginScenePath
                      + " 再进 Play（退出 Play 后会还原回去）");
        }

        /// <summary>退出 Play 之后：把我们换掉的场景还回去（只还原，不制造改动）。</summary>
        private static void HandleAfterStop()
        {
            if (!SessionState.GetBool(RedirectedKey, false)) return;
            SessionState.EraseBool(RedirectedKey);

            string back = SessionState.GetString(ReturnSceneKey, string.Empty);
            SessionState.EraseString(ReturnSceneKey);

            if (string.IsNullOrEmpty(back) || !File.Exists(back))
            {
                Debug.Log(Tag + "退出 Play：原来没有可还原的场景（无标题/空场景）⇒ 停在 " + Describe(ActiveScenePath()));
                return;
            }
            if (!IsLogin(ActiveScenePath()))
            {
                Debug.Log(Tag + "退出 Play：当前场景已经不是 Login ⇒ 有人自己换了场景，不还原");
                return;
            }

            var active = EditorSceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
            {
                Debug.Log(Tag + "退出 Play：Login 场景有未保存修改 ⇒ 不自动还原（避免丢改动）");
                return;
            }

            EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            Debug.Log(Tag + "退出 Play：已把场景还原到 " + back);
        }

        private static bool IsLogin(string path)
        {
            return string.Equals(path, LoginScenePath, StringComparison.OrdinalIgnoreCase);
        }

        private static string ActiveScenePath()
        {
            var active = EditorSceneManager.GetActiveScene();
            return active.IsValid() ? active.path : string.Empty;
        }

        private static string Describe(string path)
        {
            return string.IsNullOrEmpty(path) ? "(无标题/空场景)" : path;
        }
    }
}
#endif
