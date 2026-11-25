using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 登录 / 存档查看 / 设置 三个界面的 IMGUI 绘制（只画，不改状态）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么复用 `TownHud` 的绘制件
    /// ══════════════════════════════════════════════════════════════════════
    /// `TownHud.Begin/Fill/Frame/PanelBox/Text/Button/Bar` 已经把**字体（系统动态中文字体）**、
    /// **虚拟像素缩放（1600×900 → 实际分辨率）**、**奶油底 + 墨棕字 + 木框的配色**三件事解决了。
    /// 登录界面另起一套只会让"主城里的界面"和"进主城前的界面"看起来像两个游戏 ✗
    /// ⇒ 这里只补 `TownHud` 没有的三个件：**换行文本**（`Wrap`，它把 `wordWrap` 关了）、
    ///   **滑条**（音量）、**成对的居中矩形**（它的 `Centered` 是 private）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、坐标口径
    /// ══════════════════════════════════════════════════════════════════════
    /// 与 `TownHud` 完全一致：按 **1600×900 虚拟像素**排版，`K = Screen.height/900` 缩放。
    /// `K` 的公式必须与 `TownHud.Begin` 逐字相同（含 `0.55~2.2` 的夹取），
    /// 否则两个界面在极端分辨率下会错位 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、这一层"只画不改"
    /// ══════════════════════════════════════════════════════════════════════
    /// 三个 `Draw*` 的返回值就是"玩家点了什么"（`LoginAction` / `SaveClick` / `SettingsEvent`），
    /// 真正的状态切换、读档、落盘、改键全部在 `LoginBootstrap` 里做。
    /// 好处：`OnGUI` 一帧会被调用 2~3 次（Layout/Repaint/事件），把副作用放在这里会被重复执行 ✓
    /// </summary>
    public static class LoginHud
    {
        public enum LoginAction
        {
            None = 0,
            Continue = 1,
            NewGame = 2,
            SaveList = 3,
            Settings = 4,
            Quit = 5,
            /// <summary>
            /// ★ 训练场（额外模式）★ 与"继续/新的游戏"并列的第三条入口。
            /// 不读存档、不写存档、不进战斗流程：点了就是一次 `LoadScene("Training")`（见 `LoginBootstrap.ActTraining`）。
            /// </summary>
            Training = 6,
        }

        public enum SaveAction
        {
            None = 0,
            Close = 1,
            Select = 2,
            Delete = 3,
            Copy = 4,
            /// <summary>取消"确认删除"（以前这里错回成 `None` ⇒ 取消按钮没反应 ✗）</summary>
            CancelDelete = 5,
            /// <summary>
            /// 确认态里再点一次「确认删除」—— **真的删**。
            /// ★ 这就是"确认删除无效"的真根因 ★ 这一格以前回的是 `Delete`，而 `Delete` 只负责
            /// "进入确认态" ⇒ 确认按钮点一万次也只是**重新进入确认态**，永远不会调 `SaveStore.Delete` ✗
            /// （自动化走的是 `Slot(3,"delete_confirm")` → 直接调 `DeleteSlot`，所以一直"看起来能用" ✓）
            /// </summary>
            DeleteConfirm = 6,
        }

        public struct SaveClick
        {
            public SaveAction Action;
            public int Slot;
        }

        public struct SettingsEvent
        {
            public bool Close;
            public bool ResetBindings;
            public bool BeginRebind;
            public InputContext RebindContext;
            public string RebindId;
            public bool AudioChanged;
            public bool QualityChanged;
            public bool FrameRateChanged;
            public bool ResolutionChanged;
            /// <summary>用户点了"返回开始界面"（回 `Login` 场景；`armed` = 已进入二次确认态）。</summary>
            public bool ReturnToTitle;
        }

        // ══════════════════════════════════════════════════════════════════
        // 坐标系（与 TownHud 同一公式）
        // ══════════════════════════════════════════════════════════════════

        private static float K => Mathf.Clamp(Screen.height / 900f, 0.55f, 2.2f);
        public static float ViewWidth => Screen.width / K;
        public static float ViewHeight => Screen.height / K;

        /// <summary>
        /// 滚动区的**屏幕 px** 偏移（= −视口屏幕原点）。**平时恒为 `(0,0)`**，
        /// 只在 `DrawSettings` 的键位列表那一段被临时挂上，画完立刻复位。
        ///
        /// ⚠️ 为什么是 `−视口原点` 而不是 `−滚动量`：`GUI.BeginScrollView` 的内容坐标系
        /// **自己会减滚动量**（官方例子里内容从 `(0,0)` 开始画就对了）⇒ 这里再加滚动量
        /// 就是**减两次**，内容会原地不动（正是"滑条动了、商品不动"那个病）✓
        /// </summary>
        private static Vector2 _scrollShift = Vector2.zero;

        private static Rect S(Rect r) => new Rect(r.x * K + _scrollShift.x, r.y * K + _scrollShift.y,
                                                 r.width * K, r.height * K);

        /// <summary>键位列表的滚动量（**虚拟 px**）。</summary>
        private static float _bindScroll;
        /// <summary>最近一次键位列表的读数（内容高 / 视口高），供日志与验收读。</summary>
        public static string BindScrollProbe = "(还没画过)";
        private static string _lastBindProbe;

        private static Rect Centered(float w, float h)
            => new Rect((ViewWidth - w) * 0.5f, (ViewHeight - h) * 0.5f, w, h);

        /// <summary>
        /// ★ 居中卡片，且**先按当前虚拟视口钳一次** ★
        ///
        /// 病根（用户报「部分文字超框」）：存档面板 1140×800、设置面板 1180×840 是写死的，
        /// 而 `ViewWidth = Screen.width / K`。窄窗口（4:3、或 `K` 被夹到 0.55 下限时）算出的
        /// `ViewWidth` 可能只有 ~1091 ⇒ `(ViewWidth - 1180) * 0.5` 是**负数**，
        /// 面板左右各出屏 44px，里面的右列控件整块看不见 ✗
        /// ⇒ 宽高都先钳到"视口 − 16"，再居中。**1600×900 这一档算出来与原值逐像素相同**
        ///   （1180 / 1140 / 800 / 840 都小于 1600−16、900−16）⇒ 16:9 下观感零变化 ✓
        /// </summary>
        private static Rect Card(float w, float h)
        {
            w = Mathf.Min(w, ViewWidth - 16f);
            h = Mathf.Min(h, ViewHeight - 16f);
            return new Rect((ViewWidth - w) * 0.5f, (ViewHeight - h) * 0.5f, w, h);
        }

        // ══════════════════════════════════════════════════════════════════
        // 每帧一次的准备
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// `TownHud` 的按钮样式是 `new GUIStyle(GUI.skin.label)` 来的：它只显式设了 `normal.textColor`，
        /// 而 **hover / active 的文字色仍然继承内置皮肤的浅色** ⇒ 鼠标停在哪颗按钮上，那颗的字就变浅 ✗
        /// （实测：登录界面的"存档查看"正好被鼠标压住，字就虚了）
        /// 这里在每帧画之前把内置 `label` 各状态的文字色钉成墨棕 —— `TownHud` 每次 new 出来的样式都会继承它 ✓
        /// 不动 `TownHud.cs`（那在另一路的地盘上）。
        /// ★ v253：`TownHud` 现在自己收口了 —— `TownHud.PinStateText` 在每次画字/画按钮时把八个状态钉死
        ///   （普通文字恒等于静止色，按钮悬停走木色 `Wood`）⇒ 本方法从"唯一补丁"退成"兜底"，
        ///   留着仍然有用：它保证**任何别的代码**从 `GUI.skin.label` 克隆出来的样式也不会带浅色 hover 字 ✓
        /// </summary>
        public static void Prepare()
        {
            GUIStyle label = GUI.skin.label;
            if (label == null) return;
            label.normal.textColor = TownHud.Ink;
            label.hover.textColor = TownHud.Ink;
            label.active.textColor = TownHud.Ink;
            label.focused.textColor = TownHud.Ink;
            label.onNormal.textColor = TownHud.Ink;
            label.onHover.textColor = TownHud.Ink;
            label.onActive.textColor = TownHud.Ink;
            label.onFocused.textColor = TownHud.Ink;
        }

        // ══════════════════════════════════════════════════════════════════
        // 换行文本（TownHud.Text 关了 wordWrap，长读数必须靠这个）
        // ══════════════════════════════════════════════════════════════════

        private static Font _font;
        /// <summary>建字体那一刻的语言代数（≠ `L10n.FontGeneration` ⇒ 换字体，见 `EnsureFont`）。</summary>
        private static int _fontGen = -1;
        private static readonly Dictionary<int, GUIStyle> WrapStyles = new Dictionary<int, GUIStyle>();

        /// <summary>
        /// ★ 上一次建样式时的 `K` ★
        ///
        /// 本文件两处样式缓存（`WrapStyles` / `SkinButtonStyles`）的键里已经**带了像素字号**
        /// （`px = size * K`）⇒ 换分辨率不会拿到旧字号（这一点与 `TownHud` 原来的坑不同）。
        /// 但旧 `_k` 的那批条目**永远不会再被命中**，只在字典里烂着 ⇒ `K` 一变就整体清掉：
        /// 既省掉随分辨率切换无限增长的样式/字体图集，也让"字号跟着分辨率变"这件事有单一时机 ✓
        /// </summary>
        private static float _cacheK = -1f;

        private static void EnsureK()
        {
            float k = K;
            if (Mathf.Approximately(_cacheK, k)) return;
            _cacheK = k;
            WrapStyles.Clear();
            SkinButtonStyles.Clear();
        }

        /// <summary>
        /// 字体跟着语言走：切语言后 `L10n.FontGeneration` 加一 ⇒ 换本语言的系统字体，
        /// 并把按字号缓存的 `GUIStyle` 清掉（它们握着旧 `Font`）。
        /// `Microsoft YaHei` 没有 Hangul / Arabic ⇒ 不清就是韩文/乌尔都文一片空白 ✓
        /// </summary>
        private static void EnsureFont()
        {
            if (_font != null && _fontGen == L10n.FontGeneration) return;
            _fontGen = L10n.FontGeneration;
            _font = null;
            string[] candidates = L10n.Fonts;
            for (int i = 0; i < candidates.Length; i++)
            {
                Font f = Font.CreateDynamicFontFromOSFont(candidates[i], 16);
                if (f != null) { _font = f; break; }
            }
            WrapStyles.Clear();
            SkinButtonStyles.Clear();
        }

        private static GUIStyle WrapStyle(int size)
        {
            EnsureK();
            int px = Mathf.Max(8, Mathf.RoundToInt(size * K));
            if (WrapStyles.TryGetValue(px, out GUIStyle st) && st != null) return st;
            EnsureFont();
            st = new GUIStyle(GUI.skin.label)
            {
                fontSize = px,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip,
            };
            if (_font != null) st.font = _font;
            WrapStyles[px] = st;
            return st;
        }

        /// <summary>自动换行的多行文本（给它一个足够高的矩形）。</summary>
        public static void Wrap(Rect vr, string text, int size, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = L10n.S(text);        // ★ 本地化：登录/设置面板里的整段说明走这条
            GUIStyle st = WrapStyle(size);
            Color prevText = st.normal.textColor;
            st.normal.textColor = color;
            GUI.Label(S(vr), text, st);
            st.normal.textColor = prevText;
        }

        /// <summary>一条横线：细金线 + 下方 1px 阴影 —— 比一条平灰线"有层次"得多，成本是零。</summary>
        private static void Divider(float x, float y, float w)
        {
            TownHud.Fill(new Rect(x, y, w, 1f), new Color(0.98f, 0.78f, 0.24f, 0.85f));
            TownHud.Fill(new Rect(x, y + 1f, w, 1f), new Color(0.30f, 0.24f, 0.18f, 0.28f));
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ v214：登录界面的皮肤层（**近黑墨线 + 金内衬**，与 `Art/UI/*.png` 同一支笔）
        //
        // 为什么要在 LoginHud 里另开一层，而不是改 `TownHud`：
        //   `TownHud.cs` 在 `Scripts/Town/**`（另一条线的地盘，本轮禁止修改）。
        //   而"全局 UI 美观升级"里最能看见的那一半就在这里 —— 登录是玩家的**第一屏**。
        //   ⇒ 这里用 `UiSkin` 的九宫格（那批图本轮重画成了墨线+金线+纸纹），
        //     按钮/面板的观感就与战斗 HUD 统一了；主城 HUD 仍是它自己的木框
        //     （要统一它得改 `TownHud.cs`：见交付说明里那一处三行改动）。
        // ══════════════════════════════════════════════════════════════════

        private static readonly Dictionary<int, GUIStyle> SkinButtonStyles = new Dictionary<int, GUIStyle>();

        /// <summary>按字号缓存的九宫格按钮样式（**绝不每帧 new GUIStyle** —— 那是 IMGUI 的经典 GC 坑）。</summary>
        private static GUIStyle SkinButtonStyle(int size, bool enabled)
        {
            EnsureK();
            int px = Mathf.Max(8, Mathf.RoundToInt(size * K));
            int key = (px << 1) | (enabled ? 1 : 0);
            if (SkinButtonStyles.TryGetValue(key, out GUIStyle cached) && cached != null) return cached;

            UiSkin.Ensure();
            EnsureFont();
            var st = new GUIStyle(GUI.skin.button)
            {
                fontSize = px,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                // border 与 `UiSkin.ButtonStyle` 一致（14）：九宫格的"不拉伸区"
                border = new RectOffset(14, 14, 14, 14),
                padding = new RectOffset(10, 10, 4, 4),
            };
            if (_font != null) st.font = _font;

            Texture2D n = UiSkin.Button, h = UiSkin.ButtonHover, a = UiSkin.ButtonPress;
            if (n != null)
            {
                st.normal.background = n;
                st.hover.background = h != null ? h : n;
                st.active.background = a != null ? a : n;
                st.focused.background = h != null ? h : n;
            }
            st.normal.textColor = TownHud.Ink;
            st.hover.textColor = TownHud.Ink;
            st.active.textColor = TownHud.Ink;
            st.focused.textColor = TownHud.Ink;
            SkinButtonStyles[key] = st;
            return st;
        }

        /// <summary>
        /// 一个按钮：有九宫格贴图时走它（木底 + 墨线 + 金内衬），没有就退回 `TownHud.Button`
        /// ⇒ **美术资源缺失不会让界面消失**，只是回到旧观感（与 `UiSkin` 的设计口径一致）✓
        /// </summary>
        private static bool Button(Rect vr, string text, bool enabled = true, int size = 15)
        {
            text = L10n.S(text);        // ★ 本地化（缺贴图退回 TownHud.Button 时那边也会再过一次，幂等）
            UiSkin.Ensure();
            if (UiSkin.Button == null) return TownHud.Button(vr, text, enabled, size);

            GUIStyle st = SkinButtonStyle(size, enabled);
            bool prev = GUI.enabled;
            GUI.enabled = enabled;
            bool clicked = GUI.Button(S(vr), text, st);
            GUI.enabled = prev;

            // 不可点：压一层白纱（九宫格贴图没有"灰态"那一张，而 `GUI.enabled` 不改文字色）
            if (!enabled) TownHud.Fill(vr, new Color(0.96f, 0.94f, 0.90f, 0.55f));
            return clicked && enabled;
        }

        /// <summary>一个面板：九宫格奶油底 + 近黑墨线 + 顶部木色标题带（缺图时退回 `TownHud.PanelBox`）。</summary>
        private static void SkinPanel(Rect vr)
        {
            UiSkin.Ensure();
            if (UiSkin.PanelStyle == null || UiSkin.PanelStyle.normal.background == null)
            {
                TownHud.PanelBox(vr);
                return;
            }
            GUI.Box(S(vr), GUIContent.none, UiSkin.PanelStyle);
            TownHud.Fill(new Rect(vr.x + 6f, vr.y + 6f, vr.width - 12f, 5f), TownHud.Wood);
        }

        /// <summary>0~1 的滑条（左边一条进度条 + 盖在上面的系统滑条 + 右边百分比）。</summary>
        private static float Slider(Rect vr, float value, string label)
        {
            TownHud.Text(new Rect(vr.x, vr.y, 300f, 26f), label, 15, TownHud.Ink, TextAnchor.MiddleLeft, true);

            float barW = vr.width - 96f;
            float barY = vr.y + 32f;
            TownHud.Bar(new Rect(vr.x, barY, barW, 12f), value, TownHud.Gold, new Color(0.86f, 0.82f, 0.70f, 1f));

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            float v = GUI.HorizontalSlider(S(new Rect(vr.x, barY - 6f, barW, 24f)), value, 0f, 1f);
            GUI.color = prev;

            TownHud.Text(new Rect(vr.x + barW + 6f, barY - 8f, 90f, 28f),
                (v * 100f).ToString("0") + "%", 16, TownHud.Ink, TextAnchor.MiddleRight, true);
            return v;
        }

        /// <summary>带"选中"金框的按钮。</summary>
        private static bool Choice(Rect vr, string text, bool selected, int size = 15)
        {
            bool clicked = Button(vr, text, true, size);
            if (selected) TownHud.Frame(vr, TownHud.Gold, 3f);
            return clicked;
        }

        // ══════════════════════════════════════════════════════════════════
        // ① 登录界面
        // ══════════════════════════════════════════════════════════════════

        public static LoginAction DrawLogin(bool hasSave, string newestLine, string status)
        {
            // ── 左上角标题牌（半透明，不挡住主城）──
            var brand = new Rect(30f, 32f, 430f, 104f);
            TownHud.Fill(brand, new Color(0.98f, 0.95f, 0.86f, 0.88f));
            TownHud.Frame(brand, TownHud.Wood, 3f);
            TownHud.Text(new Rect(brand.x + 18f, brand.y + 6f, 400f, 56f), "SEEDFALL", 40, TownHud.Ink,
                         TextAnchor.MiddleLeft, true);
            TownHud.Text(new Rect(brand.x + 20f, brand.y + 62f, 400f, 30f), "守园者 · 主城登录", 17, TownHud.InkSoft,
                         TextAnchor.MiddleLeft, true);

            // ★ 左下角原来那条"背景 = …… · 数据目录 C:/Users/…" 是**开发者说明 + 本机绝对路径**，
            //   对玩家无意义（也泄露了本机用户名）⇒ 整条连同底衬色带一起去掉，不占版面 ✓

            // ── 右侧登录卡（★ 宽高按视口钳一次：窄窗口时不再画到屏幕外，16:9 下与原值相同）──
            float cw = Mathf.Min(620f, ViewWidth - 24f);
            float ch = Mathf.Min(640f, ViewHeight - 40f);
            float cx = Mathf.Clamp(ViewWidth - cw - 40f, 8f, Mathf.Max(8f, ViewWidth - cw - 8f));
            float cy = Mathf.Clamp(150f, 8f, Mathf.Max(8f, ViewHeight - ch - 8f));
            var card = new Rect(cx, cy, cw, ch);
            SkinPanel(card);
            TownHud.Fill(new Rect(card.x, card.y, card.width, 8f), TownHud.Wood);

            TownHud.Text(new Rect(card.x + 30f, card.y + 16f, 400f, 46f), "登录", 30, TownHud.Ink,
                         TextAnchor.MiddleLeft, true);
            TownHud.Text(new Rect(card.x + 30f, card.y + 60f, 560f, 26f),
                hasSave ? "检测到存档，可以继续上次的主城。" : "还没有任何存档 —— 先开一局新的游戏。",
                14, hasSave ? TownHud.Good : TownHud.InkSoft, TextAnchor.MiddleLeft);
            Divider(card.x + 30f, card.y + 96f, card.width - 60f);

            LoginAction result = LoginAction.None;
            float bx = card.x + 30f, bw = card.width - 60f, by = card.y + 110f;
            const float BH = 62f, GAP = 10f;

            // ① 继续游戏 —— 没有存档就灰掉（用户明确要求：灰色不可按）
            if (Button(new Rect(bx, by, bw, BH), "继续游戏", hasSave, 20)) result = LoginAction.Continue;
            by += BH + GAP;
            // ② 新的游戏
            if (Button(new Rect(bx, by, bw, BH), "新的游戏", true, 20)) result = LoginAction.NewGame;
            by += BH + GAP;
            // ③ 存档查看
            if (Button(new Rect(bx, by, bw, BH), "存档查看（6 位）", true, 20)) result = LoginAction.SaveList;
            by += BH + GAP;
            // ④ 设置（= 主城里按 Esc 的那个面板）
            if (Button(new Rect(bx, by, bw, BH), "设置（Esc）", true, 20)) result = LoginAction.Settings;
            by += BH + GAP;
            // ⑤ 退出游戏 —— 直接关进程
            if (Button(new Rect(bx, by, bw, BH), "退出游戏", true, 20)) result = LoginAction.Quit;

            // ══════════════════════════════════════════════════════════════════
            // ★ v302：**登录界面的训练场按钮已移除** ★
            //
            // 用户原话：「训练模式进入不了，直接报错了，移除登录界面进训练模式的按钮」。
            // 点这颗按钮会 LoadScene("Training") 然后报错 ⇒ 入口先摘掉（不再让玩家点到）。
            // 训练场的入口只剩**主城传送门**那一条（`TownHud.DrawDifficulty` 里的
            // 「训练场（自由练习）」按钮 → `TownBootstrap.GoTraining()`）。
            //
            // 枚举值 `LoginAction.Training` 与 `LoginBootstrap.ActTraining()` 都**保留**：
            //   它们是"场景加载"与"入口按钮"两件事，删掉会让恢复变麻烦；
            //   现在没有任何 UI 会把 `result` 设成 `Training`，所以那条 case 走不到。
            // ══════════════════════════════════════════════════════════════════

            // 底部：存档概况 + 上一步动作的回执
            float fy = card.y + card.height - 128f;
            Divider(card.x + 30f, fy - 8f, card.width - 60f);
            // ★ v252：拼接出来的 "存档：3 / 6" 进不了词表 ⇒ 改具名键（英/日界面里那行原本是中文）✓
            TownHud.Text(new Rect(card.x + 30f, fy, 560f, 26f),
                         L10n.T("login.slotUsage", SaveStore.UsedCount.ToString(), SaveStore.SlotCount.ToString()),
                         14, TownHud.Ink, TextAnchor.MiddleLeft, true);
            Wrap(new Rect(card.x + 30f, fy + 24f, card.width - 60f, 52f), newestLine, 13, TownHud.InkSoft);
            if (!string.IsNullOrEmpty(status))
                Wrap(new Rect(card.x + 30f, card.y + card.height - 54f, card.width - 60f, 44f), status, 13, TownHud.Wood);

            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        // ② 存档查看（6 位）
        // ══════════════════════════════════════════════════════════════════

        public static SaveClick DrawSaveList(int confirmSlot, string status, int selectedSlot = -1)
        {
            SaveListDraws++;
            LastSaveListConfirmSlot = confirmSlot;
            var click = new SaveClick { Action = SaveAction.None, Slot = -1 };
            var card = Card(1140f, 800f);
            SkinPanel(card);
            TownHud.Fill(new Rect(card.x, card.y, card.width, 8f), TownHud.Wood);

            TownHud.Text(new Rect(card.x + 24f, card.y + 14f, 700f, 46f), "存档查看 · 6 个存档位", 30, TownHud.Ink,
                         TextAnchor.MiddleLeft, true);
            TownHud.Text(new Rect(card.x + 24f, card.y + 56f, 1080f, 26f),
                "选择 = 读入并进主城 · 删除 = 二次确认后删除 · 复制 = 占用一个空存档位", 14, TownHud.InkSoft,
                TextAnchor.MiddleLeft);
            Divider(card.x + 24f, card.y + 92f, card.width - 48f);

            float ry = card.y + 108f;
            for (int i = 0; i < SaveStore.SlotCount; i++)
            {
                SlotInfo info = SaveStore.Infos[i];
                var row = new Rect(card.x + 20f, ry, card.width - 40f, 100f);
                bool confirming = confirmSlot == i;

                TownHud.Fill(row, info.Exists
                    ? new Color(1f, 0.98f, 0.91f, 0.92f)
                    : new Color(0.88f, 0.86f, 0.80f, 0.55f));
                // 键盘选中的那一位：金框（用户要的"选择存档的按键"要看得见选到哪了 ✓）
                if (i == selectedSlot) TownHud.Frame(row, TownHud.Gold, 3f);
                TownHud.Frame(row, confirming ? TownHud.Bad : (info.Exists ? TownHud.Wood : TownHud.InkSoft), 2f);

                // ★ 本地化：`存档位 {0}` 走具名键 + `{0}` 占位（日文語順不同也能整句换）
                TownHud.Text(new Rect(row.x + 16f, row.y + 8f, 130f, 30f), L10n.T("login.slotNo", i + 1), 18,
                             info.Exists ? TownHud.Ink : TownHud.InkSoft, TextAnchor.MiddleLeft, true);
                TownHud.Text(new Rect(row.x + 16f, row.y + 40f, 130f, 24f),
                             info.Exists ? "有存档" : "空位", 13,
                             info.Exists ? TownHud.Good : TownHud.InkSoft, TextAnchor.MiddleLeft);

                if (info.Exists)
                {
                    Wrap(new Rect(row.x + 152f, row.y + 10f, 566f, 82f), info.Summary ?? "", 13, TownHud.Ink);
                }
                else
                {
                    TownHud.Text(new Rect(row.x + 152f, row.y, 560f, 100f), "—— 这个位置还没有存档 ——",
                                 14, TownHud.InkSoft, TextAnchor.MiddleLeft);
                }

                if (confirming)
                {
                    if (TownHud.Button(new Rect(row.x + 744f, row.y + 26f, 108f, 48f), "确认删除", true, 14))
                    {
                        click.Action = SaveAction.DeleteConfirm;   // ← 真的删（不是再确认一次）
                        click.Slot = i;
                    }
                    // 记下这颗按钮的屏幕矩形（自动化要报 Rect，见 `SaveReport`）
                    LastConfirmButtonScreen = new Rect((row.x + 744f) * K, (row.y + 26f) * K, 108f * K, 48f * K);
                    if (TownHud.Button(new Rect(row.x + 860f, row.y + 26f, 108f, 48f), "取消", true, 14))
                    {
                        // ★ 这里必须回一个"取消删除"的语义 ★
                        // 以前回的是 `None` ⇒ `HandleSaveList` 什么都不做 ⇒ `_confirmDelete` 一直是这一位
                        // ⇒ 按"取消"没反应（这一行永远停在确认态）✗ 用户报的"取消没管用"就是这个
                        click.Action = SaveAction.CancelDelete;
                        click.Slot = i;
                    }
                    TownHud.Text(new Rect(row.x + 976f, row.y, 120f, 100f), "删除后不可恢复", 12, TownHud.Bad,
                                 TextAnchor.MiddleCenter);
                }
                else
                {
                    if (TownHud.Button(new Rect(row.x + 744f, row.y + 26f, 108f, 48f), "选择", info.Exists, 14))
                    {
                        click.Action = SaveAction.Select;
                        click.Slot = i;
                    }
                    if (TownHud.Button(new Rect(row.x + 860f, row.y + 26f, 108f, 48f), "删除", info.Exists, 14))
                    {
                        click.Action = SaveAction.Delete;      // Bootstrap 看到这个会先切到"确认"态
                        click.Slot = i;
                    }
                    // 复制：只有"这一位有存档"且"还有空位"才可点。
                    // 空位显示「复制」（灰）而不是「位已满」—— 那是两件事，别把玩家绕进去 ✓
                    bool hasFree = SaveStore.FirstFree >= 0;
                    bool canCopy = info.Exists && hasFree;
                    string copyLabel = !info.Exists ? "复制" : (hasFree ? "复制" : "位已满");
                    if (TownHud.Button(new Rect(row.x + 976f, row.y + 26f, 108f, 48f), copyLabel, canCopy, 14))
                    {
                        click.Action = SaveAction.Copy;
                        click.Slot = i;
                    }
                }

                ry += 108f;
            }

            float fy = card.y + card.height - 62f;
            Divider(card.x + 24f, fy - 10f, card.width - 48f);
            TownHud.Text(new Rect(card.x + 24f, fy - 30f, card.width - 200f, 24f),
                "键盘：↑↓ 选位　Enter/Space 选择　Del 删除（再按一次确认）　C 复制　Esc 取消/返回",
                13, TownHud.Wood, TextAnchor.MiddleLeft);
            // ★ 这一行是**动态**的：要么是一句状态语，要么是「落盘目录：<本机绝对路径>」——
            //   路径长度完全不可控，原来 13 号直接塞进 760 宽的 rect 里、右侧 140 处就是"关闭"按钮
            //   ⇒ 量过再画（超长就逐级降字号），不再压到按钮上 ✓
            TownHud.TextFit(new Rect(card.x + 24f, fy, card.width - 220f, 44f),
                string.IsNullOrEmpty(status) ? ("落盘目录：" + SaveStore.Dir) : status,
                13, string.IsNullOrEmpty(status) ? TownHud.InkSoft : TownHud.Wood);
            if (Button(new Rect(card.x + card.width - 164f, fy, 140f, 44f), "关闭 (Esc)", true, 15))
                click.Action = SaveAction.Close;

            return click;
        }

        // ══════════════════════════════════════════════════════════════════
        // ③ 设置（Esc 面板）
        // ══════════════════════════════════════════════════════════════════

        public static SettingsEvent DrawSettings(bool inTown, string[] probeLines, string status)
        {
            var ev = new SettingsEvent();
            GameSettings s = LoginSettings.Current;

            var card = Card(1180f, 840f);
            SkinPanel(card);
            TownHud.Fill(new Rect(card.x, card.y, card.width, 8f), TownHud.Wood);

            TownHud.Text(new Rect(card.x + 24f, card.y + 12f, 600f, 46f), "设置", 30, TownHud.Ink,
                         TextAnchor.MiddleLeft, true);

            // ────────────── ★ 语言切换：6 颗按钮，点一下**立刻**生效 ★ ──────────────
            //   `L10n.Set()` 只做三件事：写 `PlayerPrefs`、`FontGeneration++`、清解析缓存。
            //   文字不用"重建"——IMGUI 每帧重跑 `OnGUI`，已显示的界面**下一帧就是新语言**，
            //   **不需要重进场景** ✓
            //   字体要重建：`TownHud.Begin` / `EnsureFont` 认 `FontGeneration`，不同就换字体并清
            //   按字号缓存的 `GUIStyle` ⇒ 韩文/乌尔都文不会画成空白 ✓
            //   位置：标题右侧 (x+592..x+940)。副标题在 y+54 以下、标题字只到 x+84、
            //   「返回开始界面」在 x+960 起 ⇒ **与任何控件都不相交** ✓
            TownHud.Text(new Rect(card.x + 592f, card.y + 8f, 44f, 36f), L10n.T("ui.settings.langShort"),
                         14, TownHud.Ink, TextAnchor.MiddleLeft, true);
            for (int li = 0; li < L10n.LangCount; li++)
            {
                Lang lang = L10n.At(li);
                if (Choice(new Rect(card.x + 640f + li * 51f, card.y + 8f, 48f, 36f),
                           L10n.Short(lang), L10n.Current == lang, 12))
                    L10n.Set(lang);
            }

            // 副标题收窄到 x+924：右上角 (x+960..x+1160) 留给「返回开始界面」⇒ **两个矩形不相交** ✓
            // ★ v253：删掉"改动即时生效并写入 settings.json。"（写盘细节对玩家无关）；
            //   "在主城里按 Esc 打开 / 关闭"是**玩家操作提示**且别处没有（主城只有 TownHud 的
            //   "Esc 关闭"，没说能按 Esc **打开**设置）⇒ 这半句保留 ✓
            TownHud.Text(new Rect(card.x + 24f, card.y + 54f, 900f, 26f),
                inTown ? "（在主城里按 Esc 打开 / 关闭）" : "（登录界面：只能从“设置”按钮进入；Esc 只在主城生效）",
                14, TownHud.InkSoft, TextAnchor.MiddleLeft);
            Divider(card.x + 24f, card.y + 86f, card.width - 48f);

            // ────────────── 左列：音频 / 画质 / 帧率 / 分辨率 ──────────────
            float lx = card.x + 24f, lw = 520f, ly = card.y + 98f;

            TownHud.Text(new Rect(lx, ly, lw, 26f), "音乐 / 音效", 18, TownHud.Ink, TextAnchor.MiddleLeft, true);
            float music = Slider(new Rect(lx, ly + 28f, lw, 60f), s.musicVolume, "音乐音量");
            float sfx = Slider(new Rect(lx, ly + 90f, lw, 60f), s.sfxVolume, "音效音量");
            if (!Mathf.Approximately(music, s.musicVolume)) { s.musicVolume = Mathf.Clamp01(music); ev.AudioChanged = true; }
            if (!Mathf.Approximately(sfx, s.sfxVolume)) { s.sfxVolume = Mathf.Clamp01(sfx); ev.AudioChanged = true; }

            ly += 162f;
            TownHud.Text(new Rect(lx, ly, lw, 26f), "画质（实时改阴影 / 抗锯齿 / URP 渲染缩放）", 18, TownHud.Ink,
                         TextAnchor.MiddleLeft, true);
            for (int t = 0; t < 3; t++)
            {
                var r = new Rect(lx + t * 172f, ly + 30f, 160f, 46f);
                if (Choice(r, LoginSettings.TierName(t), s.qualityTier == t))
                {
                    s.qualityTier = t;
                    ev.QualityChanged = true;
                }
            }

            ly += 90f;
            TownHud.Text(new Rect(lx, ly, lw, 26f), "帧率上限（关垂直同步）", 18, TownHud.Ink, TextAnchor.MiddleLeft, true);
            int[] fps = { 30, 60, 120 };
            for (int i = 0; i < fps.Length; i++)
            {
                var r = new Rect(lx + i * 172f, ly + 30f, 160f, 46f);
                if (Choice(r, fps[i] + " FPS", s.frameRate == fps[i]))
                {
                    s.frameRate = fps[i];
                    ev.FrameRateChanged = true;
                }
            }

            ly += 90f;
            TownHud.Text(new Rect(lx, ly, lw, 26f), "分辨率", 18, TownHud.Ink, TextAnchor.MiddleLeft, true);
            int[,] res =
            {
                { 1280, 720 }, { 1600, 900 },
                { 1920, 1080 }, { 2560, 1440 },
            };
            for (int i = 0; i < 4; i++)
            {
                int w = res[i, 0], h = res[i, 1];
                var r = new Rect(lx + (i % 2) * 266f, ly + 30f + (i / 2) * 52f, 254f, 44f);
                bool selected = s.resolutionWidth == w && s.resolutionHeight == h;
                if (Choice(r, w + "×" + h, selected, 14))
                {
                    s.resolutionWidth = w;
                    s.resolutionHeight = h;
                    ev.ResolutionChanged = true;
                }
            }

            ly += 136f;
            TownHud.Text(new Rect(lx, ly, lw, 26f), "显示模式", 18, TownHud.Ink, TextAnchor.MiddleLeft, true);
            if (Choice(new Rect(lx, ly + 30f, 254f, 44f), "窗口", !s.fullScreen, 14))
            {
                s.fullScreen = false;
                ev.ResolutionChanged = true;
            }
            if (Choice(new Rect(lx + 266f, ly + 30f, 254f, 44f), "全屏", s.fullScreen, 14))
            {
                s.fullScreen = true;
                ev.ResolutionChanged = true;
            }

            // ────────────── 右列：按键绑定（两套上下文）──────────────
            float rx = card.x + 590f, rw = 566f, ry = card.y + 98f;

            // ★ v253：标题尾巴上的"（Input System）"是**输入后端的技术标注**，对玩家无意义 ⇒ 去掉 ✓
            TownHud.Text(new Rect(rx, ry, rw, 26f), "按键绑定 —— 主城 / 战斗 各一套", 18, TownHud.Ink,
                         TextAnchor.MiddleLeft, true);
            if (Choice(new Rect(rx, ry + 30f, 180f, 40f), "主城", _bindContext == InputContext.Town, 15))
                _bindContext = InputContext.Town;
            if (Choice(new Rect(rx + 190f, ry + 30f, 180f, 40f), "战斗", _bindContext == InputContext.Combat, 15))
                _bindContext = InputContext.Combat;
            TownHud.Text(new Rect(rx + 386f, ry + 30f, 180f, 40f),
                LoginInput.Rebinding != null ? "请按键…（Esc 取消）" : "", 13, TownHud.Bad, TextAnchor.MiddleLeft, true);

            ry += 80f;

            // ────────────── ★ 键位列表进滚动区（v238）★ ──────────────
            //  用户截图里战斗那套有 13 行 ⇒ 行高 60 × 13 = 780，而这块只有 ~470 高
            //  ⇒ 改前直接画到面板外（"下面全部空白"就是这么来的：越界的行被窗口截掉，
            //    而视口里剩下的又只有前几行）✗
            //
            //  ★ 坐标怎么算（这条错了就是"滑条动、内容不动"）★
            //    `GUI.BeginScrollView` 的内容坐标 = 视口左上角起、**已减掉滚动量**；
            //    本文件的行是按**屏幕绝对虚拟坐标**算的 ⇒ 进区前把偏移设成 `−视口屏幕原点`，
            //    由 `S()` 与 `TownHud.SetScrollOffset()` 一起吃这一份偏移 ✓
            //    画完立刻两边都复位 —— 下面的"恢复默认按键 / 读数 / 关闭"照旧按绝对坐标走 ✓
            int rows = 0;
            for (int i = 0; i < LoginInput.Count; i++)
            {
                BindableAction a0 = LoginInput.At(i);
                if (a0 == null || a0.Context != _bindContext || SkipInTown(a0)) continue;
                rows++;
            }

            float listViewH = Mathf.Max(80f, card.y + 646f - ry);
            float contentH = rows * 60f + 6f;
            float max = Mathf.Max(0f, contentH - listViewH);

            // 键盘：翻页 / 头尾（鼠标滚轮与拖动由 `BeginScrollView` 自己吃）
            Event evt = Event.current;
            if (evt != null && evt.type == EventType.KeyDown)
            {
                float step = Mathf.Max(40f, listViewH - 48f);
                if (evt.keyCode == KeyCode.PageDown) { _bindScroll += step; evt.Use(); }
                else if (evt.keyCode == KeyCode.PageUp) { _bindScroll -= step; evt.Use(); }
                else if (evt.keyCode == KeyCode.Home) { _bindScroll = 0f; evt.Use(); }
                else if (evt.keyCode == KeyCode.End) { _bindScroll = max; evt.Use(); }
            }
            _bindScroll = Mathf.Clamp(_bindScroll, 0f, max);

            Rect bindView = new Rect(rx, ry, rw, listViewH);
            Rect sv = S(bindView);                       // ← 此时 `_scrollShift` 必须是 0
            float contentW = Mathf.Max(8f, sv.width - 20f);
            Vector2 sp = GUI.BeginScrollView(sv, new Vector2(0f, _bindScroll * K),
                new Rect(0f, 0f, contentW, Mathf.Max(sv.height, contentH * K)));
            _bindScroll = Mathf.Clamp(sp.y / K, 0f, max);            // 回读：拖动/滚轮之后的真实值

            Vector2 shift = new Vector2(-sv.x, -sv.y);               // ★ −视口原点，不是 −滚动量
            _scrollShift = shift;
            TownHud.SetScrollOffset(shift);                          // 行里的 Fill/Frame/Text 也吃这份偏移

            int rowIndex = 0;
            for (int i = 0; i < LoginInput.Count; i++)
            {
                BindableAction a = LoginInput.At(i);
                if (a == null || a.Context != _bindContext || SkipInTown(a)) continue;

                var row = new Rect(rx, ry + rowIndex * 60f, rw, 56f);
                TownHud.Fill(row, new Color(1f, 0.98f, 0.91f, 0.75f));
                TownHud.Frame(row, a.IsDefault ? new Color(0.86f, 0.82f, 0.72f, 1f) : TownHud.Gold, 1.5f);

                // ★ `a.Label` 只有 200、`a.Path` 只有 220 ⇒ 键名与 Input System 路径都是动态长度
                //   （`<Keyboard>/leftShift` 这种就很长）⇒ 量过再画，防重叠
                TownHud.TextFit(new Rect(row.x + 12f, row.y, 200f, 56f), a.Label, 15, TownHud.Ink,
                             TextAnchor.MiddleLeft, true);
                TownHud.TextFit(new Rect(row.x + 212f, row.y, 220f, 56f), a.Path, 13,
                             a.Resolves ? TownHud.InkSoft : TownHud.Bad, TextAnchor.MiddleLeft);
                bool waiting = LoginInput.Rebinding == a;
                if (Button(new Rect(row.x + 440f, row.y + 8f, 112f, 40f), waiting ? "按键…" : "改键", !waiting, 14))
                {
                    ev.BeginRebind = true;
                    ev.RebindContext = a.Context;
                    ev.RebindId = a.Id;
                }
                rowIndex++;
            }

            // 复位：区外的控件仍按屏幕绝对坐标画
            _scrollShift = Vector2.zero;
            TownHud.SetScrollOffset(Vector2.zero);
            GUI.EndScrollView();

            // 读数（判据是"滑到底时最后一行能不能完整看见"，**不是**"公式算得对"）
            float bottomInView = rows * 60f - max;       // 滑到底时最后一行下沿在视口里的 y
            BindScrollProbe = $"键位列表｜行数={rows}｜视口高={listViewH:0.#}｜内容高={contentH:0.#}" +
                              $"｜可滚={max:0.#}｜scroll.y={_bindScroll:0.#}｜屏幕偏移={shift.y:0.#}" +
                              $"｜滑到底末行下沿(视口坐标)={bottomInView:0.#} vs 视口高={listViewH:0.#}" +
                              $" ⇒ {(bottomInView <= listViewH + 0.01f ? "末行完整可见" : "✗末行被裁")}";
            if (_lastBindProbe != BindScrollProbe)
            {
                _lastBindProbe = BindScrollProbe;
                Log.Info("[Login] SCROLL " + BindScrollProbe);
            }

            if (Button(new Rect(rx, card.y + 660f, 240f, 46f), "恢复默认按键（两套）", true, 14))
                ev.ResetBindings = true;

            // ────────────── 底部：五项读数（"真的生效"的证据）──────────────
            Wrap(new Rect(card.x + 24f, card.y + 700f, 540f, 132f),
                 probeLines != null ? string.Join("\n", probeLines) : "", 12, TownHud.InkSoft);
            if (!string.IsNullOrEmpty(status))
                Wrap(new Rect(card.x + 590f, card.y + 716f, 380f, 116f), status, 12, TownHud.Wood);

            if (TownHud.Button(new Rect(card.x + card.width - 180f, card.y + 700f, 156f, 46f), "关闭 (Esc)", true, 15))
                ev.Close = true;
            // ★ 「返回开始界面」★ 二次确认：第一次点只改文案，第二次才真的换场景。
            // ★ 位置从"底部 700 那一排"挪到**标题栏右侧** ★ 旧位置 (x+w-384, y+700, 192×46) 与左侧
            //   探针读数块 (x+24..x+564, y+700..y+832) 的**状态文字块** (x+590..x+970, y+716..y+832) 横向重叠 ⇒ 被遮 ✓
            //   新位置在标题行右侧，标题文字只到 x+624，副标题在 y+54 之下 ⇒ **与任何控件都不重叠** ✓
            var returnRect = new Rect(card.x + card.width - 220f, card.y + 8f, 200f, 40f);
            LastReturnButtonScreen = new Rect(returnRect.x * K, returnRect.y * K, returnRect.width * K, returnRect.height * K);
            if (TownHud.Button(returnRect, ReturnToTitleArmed ? "确认返回？" : "返回开始界面", true, 14))
                ev.ReturnToTitle = true;

            return ev;
        }

        private static InputContext _bindContext = InputContext.Town;

        /// <summary>"返回开始界面"是否已进入二次确认态（由 `LoginBootstrap` 每帧喂进来）。</summary>
        public static bool ReturnToTitleArmed;

        /// <summary>最近一帧"确认删除"那颗按钮的**屏幕像素**矩形（自动化判"按钮有没有尺寸/有没有被盖"用）。</summary>
        public static Rect LastConfirmButtonScreen;

        /// <summary>最近一帧"返回开始界面"按钮的屏幕像素矩形（用来验证不遮挡）。</summary>
        public static Rect LastReturnButtonScreen;

        /// <summary>`DrawSaveList` 被调过多少次（0 ⇒ 面板根本没画；用来排除"控件没尺寸/没画"这类误判）。</summary>
        public static int SaveListDraws;
        /// <summary>最近一次画存档面板时传进来的 `confirmSlot`（= `_confirmDelete`）。</summary>
        public static int LastSaveListConfirmSlot = -1;

        /// <summary>
        /// 主城那一套里**用不到的**绑定 ⇒ 设置面板里不列出来
        /// （用户原话："主城键位用不到的攻击键绑定直接删除设置"）。
        ///
        /// ★ 只是**不画** ★ `LoginInput` 里的数据一行都没删 —— 切到「战斗」页签，
        ///   攻击 / 切换武器照样在、照样能改；主城那套也照旧被 `LoginInput.Town` 读到 ✓
        ///   （"按场景过滤显示"而不是"删数据"：删了数据，以后主城加战斗就要重新加回来 ✗）
        /// </summary>
        private static bool SkipInTown(BindableAction a)
        {
            if (a == null || a.Context != InputContext.Town) return false;
            return a.Id == "attack" || a.Id == "switch_weapon";
        }
    }
}
