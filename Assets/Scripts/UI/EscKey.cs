using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Seedfall
{
    /// <summary>
    /// ★★ v251：**ESC 的单点判定**（用户报"部分电脑上 esc 失灵"）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 为什么不能直接用 `Keyboard.current.escapeKey.wasPressedThisFrame`
    /// ══════════════════════════════════════════════════════════════════════
    ///   本工程 `ProjectSettings.asset` 的 `activeInputHandler` = **1**（只启用新输入系统）
    ///   ⇒ `Input.GetKeyDown(KeyCode.Escape)`（旧 Input）**不可用**，没有第二条兜底路。
    ///   剩下的两个真实失效点：
    ///
    ///   ① **`Keyboard.current == null`**
    ///      丢焦点 / 远程桌面 / 输入法拦截 / 设备热插拔之后，`Keyboard.current` 会是 null，
    ///      而新输入系统不会自己立刻补回来 ⇒ `kb != null && ...` 直接整条短路，按什么都没反应。
    ///
    ///   ② **`wasPressedThisFrame` 的"当帧"语义太窄**
    ///      它只在**那一次输入系统更新**里为真。若同一帧里：
    ///        · 有别的代码/组件把 `InputSystem` 更新了不止一次；
    ///        · 或者帧率极低、一次更新跨了两个渲染帧；
    ///        · 或者该帧有 IMGUI 的 `Event.current` 抢走了键盘焦点；
    ///      则这一帧读不到、下一帧已不是"当帧" ⇒ **整次按键丢失**（表现为"按了没反应，要按第二次"）。
    ///
    /// ── 做法 ──
    ///   用**上升沿锁存**而不是"当帧"：自己记住上一帧 `isPressed`，本帧为按下、上帧为未按下 ⇒ 触发一次。
    ///   这样只要按键**至少持续到某一次更新**就不会丢；同时保留 `wasPressedThisFrame` 做并集，
    ///   保证极短点击（一帧内按下并抬起）也不漏。
    ///
    /// ── 为什么必须是单点 ──
    ///   原来三处各写一遍 `kb.escapeKey.wasPressedThisFrame`
    ///   （`GameBootstrap.cs:308` / `:421`、`TownBootstrap.cs:329`）⇒ 判定口径不一致、修一处漏两处。
    ///   现在三处都调 `EscKey.Pressed()`，口径只有一个。
    /// </summary>
    public static class EscKey
    {
        private static bool _wasPressed;   // 上一帧的按住状态（上升沿用）

        /// <summary>
        /// 本帧是否**新按下**了 ESC。
        /// 同帧内多次调用**都返回 true**（不做消费，避免调用顺序依赖）；
        /// 跨帧只在"按下那一帧"为 true。
        /// </summary>
        public static bool Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                var key = kb.escapeKey;
                if (key == null) return false;

                bool down = key.isPressed;
                // 并集：上升沿（不漏长按）+ 当帧（不漏极短点击）
                bool edge = (down && !_wasPressed) || key.wasPressedThisFrame;
                _wasPressed = down;
                if (edge) return true;
            }
            else
            {
                // 设备暂时拿不到 ⇒ 清掉锁存，避免"卡在按下"导致下次永远上升沿失败
                _wasPressed = false;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            // 旧 Input 兜底（本工程当前 `activeInputHandler = 1`，这段不参与编译；
            // 若以后改成 0 或 2，它自动生效，不用再改这个文件 ✓）
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        /// <summary>
        /// 读数：当前是否被按住（用于探针/自检，不参与判定）。
        /// </summary>
        public static bool Held
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.escapeKey != null) return kb.escapeKey.isPressed;
#endif
                return false;
            }
        }

        /// <summary>读数：输入设备是否可用（`false` ⇒ 就是"ESC 失灵"的那类机器）。</summary>
        public static bool DeviceOk
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && kb.escapeKey != null;
#else
                return true;
#endif
            }
        }

        /// <summary>场景切换/暂停时清锁存，避免上一场景的按键状态漏到下一场景。</summary>
        public static void Reset()
        {
            _wasPressed = false;
        }
    }
}
