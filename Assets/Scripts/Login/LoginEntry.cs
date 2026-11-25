#if !UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seedfall
{
    /// <summary>
    /// ★★ 只在**打包出来的播放器**里生效的"入口闸门"：一开游戏必须先进登录界面 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、它解决的到底是什么问题
    /// ══════════════════════════════════════════════════════════════════════
    /// Unity 打包后的**启动场景 = Build Settings 的第 0 条**。
    /// 之前那条是 `SeedfallArena.unity`（战斗），`Login.unity` 压根不在列表里
    /// ⇒ 播放器一起来就是战斗画面，登录界面**从未被加载**（不是画错，是没跑）✗
    /// 修法有两层，缺一层都不稳：
    ///   ① 把 `Login.unity` 放进 Build Settings 第 0 条（结构修法，见 `Tools/Seedfall/★ 登录界面…` 菜单）
    ///   ② **本文件**：万一以后有人又在 Build Settings 里调了顺序/换了首图，播放器仍然会自己纠正回登录界面 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、为什么整块包在 `#if !UNITY_EDITOR` 里（这一条是关键）
    /// ══════════════════════════════════════════════════════════════════════
    /// 编辑器里的 Play 是"**跑当前打开的那张图**"：自检、各路探针、以及"在
    /// `SeedfallArena.unity` 里直接 Play 看战斗"这些流程，全都靠这个语义。
    /// 如果这道闸门在编辑器里也生效，那些流程会被**顶到登录界面**，自检直接跑不起来 ✗✗
    /// ⇒ 用编译器把这段从编辑器程序集里**整段剔掉**：编辑器的行为与改动前逐位相同 ✓
    ///   （`Application.CanStreamedLevelBeLoaded` / `SceneManager.LoadScene` 都是运行期 API，
    ///     在播放器里没有编辑器依赖，不会因为剔除而缺少引用）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、什么时候"不"切
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 已经在 `Login` 场景 ⇒ 什么都不做（不产生第二次 LoadScene）✓
    ///   · Build Settings 里没有 `Login` ⇒ 只报一条 Error，**不猜**路径、不崩（让人去看构建设置）✓
    ///   · 用 `LoadScene`（同步单场景）而不是加法加载：启动那一刻场上不该有别人的场景活着 ✓
    /// </summary>
    public static class LoginEntry
    {
        /// <summary>必须与 `LoginBootstrap.LoginSceneName` 一致（那边是 `const`，改一处要改两处，注释钉住）。</summary>
        private const string LoginScene = "Login";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureLoginFirst()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.name == LoginScene)
            {
                Debug.Log($"[Login] 启动场景 = {LoginScene}（正常）");
                return;
            }

            string activeName = active.IsValid() ? active.name : "（无效场景）";
            if (!Application.CanStreamedLevelBeLoaded(LoginScene))
            {
                Debug.LogError($"[Login] 启动场景是 {activeName}，而 Build Settings 里没有 {LoginScene} 场景 ⇒ " +
                               "停在当前场景（请把 Assets/Scenes/Login.unity 加进 Build Settings 并放到第 1 条）");
                return;
            }

            Debug.Log($"[Login] 启动场景是 {activeName}，不是登录界面 ⇒ 切到 {LoginScene}（Build Settings 第 0 条应该是它）");
            SceneManager.LoadScene(LoginScene);
        }
    }
}
#endif
