using UnityEngine;
using UnityEngine.InputSystem;

namespace Seedfall
{
    /// <summary>
    /// ★★ v203 园丁光标（用户原话："为游戏设计一个特别的鼠标，替换现有鼠标"）★★
    ///
    /// 三态：默认（小园艺铲）／悬停（铲子发光）／按下（铲子下压 2px）。
    /// 贴图由 `Tools/icon_gen/gen_cursor.py` 生成，落在 `Assets/Resources/Art/UI/cursor_*.png`。
    ///
    /// ══════════════════════════════════════════════════════════════════════════
    ///  ★ 为什么必须在 `Update` 里设，不能在 `OnGUI` 里设 ★
    ///    IMGUI 每次 `OnGUI` 回调都会把 `Cursor` 的状态**重置回默认**（一帧里 Layout / Repaint /
    ///    各种输入事件会把 OnGUI 调多次）。在 `OnGUI` 里 `Cursor.SetCursor(...)` 的效果是
    ///    "设了、被下一次回调盖掉、又被下一次盖掉"，最终以最后一次回调为准 ⇒ **时灵时不灵**。
    ///    `Update` 一帧只跑一次、且在 `OnGUI` 之前 ⇒ 设进去的值能活到这一帧结束 ✓
    ///    ⚠️ 本文件里**没有** `OnGUI` 方法；`Apply()` 还带了一条自证（见 `SetCursorFromOnGui`）：
    ///       `OnGUI` 期间 `Event.current` 一定非空、`Update` 期间一定为空 ⇒
    ///       "从 OnGUI 里误调的次数" 是个**可以读出来的 0**，不是一句承诺 ✓
    ///
    ///  ★ 为什么只在**状态变化时**设 ★
    ///    `Cursor.SetCursor` 不是改一个字段：它要过一遍引擎 → 平台层（Windows 下是
    ///    `SetCursor(LoadCursorFromFile / CreateIconIndirect)` 那条路）。每帧无条件调，
    ///    等于每帧重建一次系统光标对象 ⇒ 白掉的 CPU，且高 DPI 下可能看到闪烁。
    ///    ⇒ 缓存 `_applied`，只在 `want != _applied` 时调；`SetCursorCalls` 就是"真的调了几次" ✓
    ///
    ///  ★ 热点（hotspot）★
    ///    `Cursor.SetCursor` 的 hotspot 是**像素坐标、原点左上**（Unity 文档原话：
    ///    "The offset in pixels from the top-left of the texture to the cursor's target"）。
    ///    本光标的热点是**铲尖**：`(4, 6)` —— 见 `HotspotX/HotspotY` 与 `gen_cursor.py` 里
    ///    `tip_pixel()`：它是**整张图最靠前（沿工具轴）的不透明像素**，由脚本从图上读出来，
    ///    不是拍脑袋写的常数；`ProbeV203Cursor` 会再从这里反核一遍 ✓
    ///    为什么在铲尖：光标要做的事是"**指向哪儿就作用在哪儿**"。铲子的工作点是它的**尖端**
    ///    （刨地、戳土都是尖先进去），热点放尖端 ⇒ 玩家看到铲尖压在哪一格，点下去就是那一格。
    ///    放在图像正中或木柄上，点下去的落点与眼里看到的**尖端**差 8~20px，每一铲都偏 ✗
    ///
    ///  ★ 三态之间热点**不变** ★
    ///    按下态只是把像素整体下沉 2px（生成器保证"与默认态逐像素相同、只平移"），
    ///    热点仍然是 (4, 6) ⇒ **点下去的那一瞬间指针不会跳**。这是光标最容易被做坏的一处：
    ///    按下换一张热点不同的图，玩家永远点不准小按钮 ✓
    ///
    ///  ★ 离开窗口要还回去 ★
    ///    `OnApplicationFocus(false)` ⇒ 立刻 `SetCursor(null, ...)` 恢复系统光标。
    ///    否则 alt-tab 出去之后，桌面/别的窗口上会一直挂着一把铲子（这是真的会发生的事）✓
    /// ══════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GardenCursor : MonoBehaviour
    {
        public enum State
        {
            /// <summary>系统默认光标（没进游戏 / 失了焦点 / 贴图缺失）。</summary>
            System = -1,
            Default = 0,
            Hover = 1,
            Press = 2,
        }

        /// <summary>三张贴图的 Resources 路径（顺序 = `State` 的 0/1/2）。</summary>
        private static readonly string[] ResPaths =
        {
            "Art/UI/cursor_default",
            "Art/UI/cursor_hover",
            "Art/UI/cursor_press",
        };

        /// <summary>★ 热点 = 铲尖像素（原点左上）。与 `gen_cursor.py` 的 `tip_pixel()` 同一个数。</summary>
        public const int HotspotX = 4;
        public const int HotspotY = 6;
        public static readonly Vector2 Hotspot = new Vector2(HotspotX, HotspotY);

        /// <summary>按下态相对默认态的整体下移量（仅用于读数/自证，运行时不参与计算）。</summary>
        public const int PressDropPixels = 2;

        /// <summary>HUD（悬停判定从它那里读，见 `ImGuiHud.UiHover`）。由 `GameBootstrap` 装配。</summary>
        public ImGuiHud Hud;

        private readonly Texture2D[] _tex = new Texture2D[3];
        private bool _focused = true;
        private bool _hasApplied;
        private State _applied = State.System;

        // ── 读数（只读探针 / 交付报告用；全是 public static，便于 `unity_drive` 直接读）──
        /// <summary>`Cursor.SetCursor` **真的被调用**的次数。状态不变它就不涨。</summary>
        public static int SetCursorCalls;
        /// <summary>当前已应用的状态。</summary>
        public static State Applied = State.System;
        /// <summary>当前已应用的贴图名（系统光标时是 "(系统默认)"）。</summary>
        public static string AppliedTexture = "(未初始化)";
        /// <summary>当前已应用的热点。</summary>
        public static Vector2 AppliedHotspot = Vector2.zero;
        /// <summary>★ 自证：从 `OnGUI` 里误调的次数。必须恒为 0。</summary>
        public static int SetCursorFromOnGui;
        /// <summary>贴图缺失张数（0 才算正常）。</summary>
        public static int MissingTextures;

        private void Awake()
        {
            // 静态读数每次进播放都重置（工程若关了 Domain Reload，静态量会跨局残留）
            SetCursorCalls = 0;
            SetCursorFromOnGui = 0;
            Applied = State.System;
            AppliedTexture = "(未初始化)";
            AppliedHotspot = Vector2.zero;

            // ★ 可见性：本项目**不隐藏**光标（用户要求 `Cursor.visible` 保持 true）★
            UnityEngine.Cursor.visible = true;

            LoadTextures();
            _focused = Application.isFocused;
        }

        /// <summary>取三张贴图，并在**加载时**就把两个会静默失效的坑报出来。</summary>
        public void LoadTextures()
        {
            MissingTextures = 0;
            for (int i = 0; i < _tex.Length; i++)
            {
                _tex[i] = Resources.Load<Texture2D>(ResPaths[i]);
                if (_tex[i] == null) MissingTextures++;
            }
            if (MissingTextures > 0)
            {
                // 不抛异常、不假装换了光标：退回系统默认，界面照常能玩
                Log.Error($"[Cursor] 光标贴图缺 {MissingTextures}/3 张（{string.Join(", ", ResPaths)}）" +
                          " ⇒ 退回系统光标。先跑 python Tools/icon_gen/gen_cursor.py");
                return;
            }
            // ★ Read/Write 必须开 ★
            //   Unity 文档（Cursor.SetCursor）："To use a texture, import it with Read/Write enabled."
            //   而本工程的 `UiTextureImport` 对 `Assets/Resources/Art/UI/**` 统一按 Default/非可读导入
            //   ⇒ 若不为 cursor_*.png 单独放行，`SetCursor` **不报错、也不生效**（最难查的那一类）
            for (int i = 0; i < _tex.Length; i++)
            {
                if (!_tex[i].isReadable)
                    Log.Error($"[Cursor] {_tex[i].name} 没有开 Read/Write ⇒ Cursor.SetCursor 会静默失效" +
                              "（见 Assets/Scripts/Art/UiTextureImport.cs 的 cursor_ 分支）");
            }
        }

        private void Update()
        {
            // ★ 唯一设置光标的地方（一帧一次，且在 OnGUI 之前）★
            Apply(Resolve());
        }

        /// <summary>当前"应该"是哪个状态（纯函数式，不碰引擎状态）。</summary>
        private State Resolve()
        {
            if (!_focused) return State.System;
            if (_tex[0] == null) return State.System;
            // ★ 悬停判定**完全来自 HUD 已有的命中测试**（见 `ImGuiHud.Btn` / `RequestTip`）★
            //   这里不自己拿鼠标坐标去比矩形：那会变成"第二套命中判定"，
            //   与 `GUI.Button` 的真实可点区域一旦差 1px，光标就会在按钮边缘说反话 ✗
            bool hover = Hud != null && Hud.UiHover;
            if (!hover) return State.Default;
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed ? State.Press : State.Hover;
        }

        private void Apply(State want)
        {
            // ★ 只在状态变化时调 ★（见文件头"为什么只在状态变化时设"）
            if (_hasApplied && want == _applied) return;

            Texture2D tex = want == State.System ? null : _tex[(int)want];
            if (want != State.System && tex == null) { want = State.System; tex = null; }

            // 自证：OnGUI 期间 `Event.current` 非空；Update 期间必为 null（本项目也从不在 OnGUI 里调本方法）
            if (Event.current != null) SetCursorFromOnGui++;

            UnityEngine.Cursor.SetCursor(tex, want == State.System ? Vector2.zero : Hotspot, CursorMode.Auto);
            UnityEngine.Cursor.visible = true;      // ★ 保持 true，不隐藏光标

            SetCursorCalls++;
            _hasApplied = true;
            _applied = want;

            Applied = want;
            AppliedTexture = tex == null ? "(系统默认)" : tex.name;
            AppliedHotspot = want == State.System ? Vector2.zero : Hotspot;
        }

        /// <summary>失焦 ⇒ 立刻把系统光标还回去；回焦 ⇒ 清缓存，让下一帧无条件重设一次。</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            _focused = hasFocus;
            if (!hasFocus)
            {
                Apply(State.System);
            }
            else
            {
                // 光靠 `_applied == System` 不够：alt-tab 回来时**系统可能已经把光标复原**
                // ⇒ 必须清掉缓存，强制重设一次（这一帧的那次调用是必要的，不是浪费）
                _hasApplied = false;
            }
        }

        /// <summary>组件被关掉/对象销毁 ⇒ 别把玩家一个人留在自定义光标上。</summary>
        private void OnDisable()
        {
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _hasApplied = false;
            _applied = State.System;
            Applied = State.System;
            AppliedTexture = "(系统默认)";
            AppliedHotspot = Vector2.zero;
        }

        /// <summary>一行读数（探针/截图叠字用）。</summary>
        public static string Status()
        {
            return $"state={Applied} tex={AppliedTexture} hotspot=({AppliedHotspot.x},{AppliedHotspot.y}) " +
                   $"SetCursor() 次数={SetCursorCalls} 从OnGUI误调={SetCursorFromOnGui} " +
                   $"visible={UnityEngine.Cursor.visible} focused={Application.isFocused}";
        }

        /// <summary>取某态贴图（探针画 4× 预览用；缺失返回 null）。</summary>
        public Texture2D TextureOf(State s)
        {
            int i = (int)s;
            return i >= 0 && i < _tex.Length ? _tex[i] : null;
        }
    }
}
