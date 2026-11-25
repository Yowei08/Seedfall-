using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>主城当前打开的面板（一次只开一个）。</summary>
    public enum TownPanel
    {
        None = 0,
        Warehouse = 1,
        SeedPicker = 2,
        /// <summary>杂货商店（道具 / 插件 / 成品 —— **不卖种子**）。</summary>
        Shop = 3,
        /// <summary>服装店（**只卖皮肤**）。与商店是两栋楼、两个锚点、两个面板。</summary>
        Clothing = 4,
        /// <summary>家 → 图鉴（植物/道具/插件 三页 ＋ 成就占位页）。</summary>
        Codex = 5,
        /// <summary>
        /// ★ v252：**传送门前的难度选择**（低 / 中 / 高）。
        /// 用户原话：「在传送门进入战斗前增加难度选择功能和对应界面，开放低、中、高」。
        /// 与其它面板的区别：它不是"看/买"面板，而是"这一局的规则"——
        /// 所以里面那颗"开始战斗"按钮才是真正调 `GoBattle()` 的地方，
        /// 传送门按 E 只把这块面板打开（不再是"按 E 直接进战斗"）✓
        /// </summary>
        Difficulty = 6,
    }

    /// <summary>
    /// ★★ 主城的 IMGUI 界面：金币条 + 交互提示 + 四个面板 + 2D 小地图 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是 `OnGUI`（而不是另起一套 UIToolkit）
    /// ══════════════════════════════════════════════════════════════════════
    /// 工程现有的全部界面都在 `ImGuiHud`（`OnGUI`）里，用户对"主城用现有 OnGUI 风格"有明确要求。
    /// 而且 `ImGuiHud` 与 `SelfCheck` 是**禁止改动**的文件 ⇒ 主城**不能**往里面加东西，
    /// 只能另起一个同样风格的 HUD。风格对齐的两点：
    ///   ① 字体：与 `ImGuiHud` 同一套 `Font.CreateDynamicFontFromOSFont`（工程里没有任何 .ttf，
    ///      中文只能靠系统动态字体；缺这一步会全是方块）✓
    ///   ② 配色：奶油底 + 墨棕字 + 木色框，与 `UiSkin` 那套九宫格贴图的色系一致 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、坐标系（虚拟像素 + 手动缩放）
    /// ══════════════════════════════════════════════════════════════════════
    /// 界面按 **1600×900 虚拟像素** 排布，实际绘制时逐坐标乘 `_k = Screen.height / 900`。
    /// 为什么不用 `GUI.matrix` 缩放：矩阵缩放会把字形也一起拉伸，中文在 0.5 倍下会糊成一团；
    /// 而**字号先乘好再交给 `GUIStyle.fontSize`（int）**，字是清晰的、只有位置变了 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、四个面板的分工（★ 这一轮的关键：商店与服装店彻底分开 ★）
    /// ══════════════════════════════════════════════════════════════════════
    ///   `Warehouse`  站在仓库前按 E：36 块地的种子库存。**仓库空了会有一行红字**
    ///                "种子只能靠打 BOSS 掉落"（用户点名要的提示）✓
    ///   `SeedPicker` 站在耕地上按 E：只列出**仓库里有货**的种子 ✓
    ///   `Shop`       站在杂货商店前按 E：**道具 / 插件 / 成品**，一颗种子都不卖 ✓
    ///   `Clothing`   站在服装店前按 E：**只有皮肤**（买 / 穿 / 卸）✓
    /// 上一版是"商业街一个面板里两个页签（种子铺 / 皮肤衣柜）"—— 那正是用户要拆开的东西 ✗
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、2D 小地图（圆形）
    /// ══════════════════════════════════════════════════════════════════════
    /// 整城是同心圆 ⇒ 小地图也用**同心圆盘**画（`MapDisc`），而不是上一版的方形色块：
    ///   草地圆 → 外环路 → 田圃环 → 内环路 → 草坪环 → 广场，依次画"更小的圆"把环带露出来，
    ///   然后 36 块地按真实世界坐标落点、五栋楼按方位角落块、玩家是亮白点 + 朝向刻线。
    /// 读数与小地图**同一份常量**（都取 `TownLayout`）⇒ 图上画的圆就是脚下的圆 ✓
    /// </summary>
    public static class TownHud
    {
        // ── 配色（与 UiSkin 的奶油/木色系一致）──
        public static readonly Color Cream = new Color(0.98f, 0.95f, 0.86f, 0.96f);
        public static readonly Color CreamDark = new Color(0.90f, 0.86f, 0.75f, 0.96f);
        public static readonly Color Ink = new Color(0.22f, 0.16f, 0.12f, 1f);
        public static readonly Color InkSoft = new Color(0.42f, 0.34f, 0.26f, 1f);
        public static readonly Color Wood = new Color(0.55f, 0.40f, 0.26f, 1f);
        public static readonly Color Gold = new Color(0.98f, 0.78f, 0.24f, 1f);
        public static readonly Color Bad = new Color(0.80f, 0.28f, 0.24f, 1f);
        public static readonly Color Good = new Color(0.32f, 0.62f, 0.30f, 1f);

        private static float _k = 1f;
        private static float _vw = 1600f, _vh = 900f;
        /// <summary>
        /// ★ 上一次建 `GUIStyle` 时的 `_k` ★
        ///
        /// 病根（用户报「分辨率变化时字号不跟着变」）：`Style()` / `WrapStyle()` 原来**按设计字号**
        /// (`size`) 做缓存键，而 `fontSize = size * _k` 是在**建的那一刻**算死的 ⇒ 换分辨率后
        /// `_k` 变了但缓存命中，样式手里仍是**旧像素字号** ⇒ 坐标跟着缩、字不跟着缩，于是
        /// 文字要么挤出框、要么挤在一起（重叠的真相）✗
        /// ⇒ 现在缓存键带上**像素字号**（`px`），并在 `_k` 变化时整体清一次（顺带解决无限增长）✓
        /// </summary>
        private static float _styleK = -1f;
        private static Texture2D _white;
        private static Font _font;
        /// <summary>建字体那一刻的语言代数（≠ `L10n.FontGeneration` ⇒ 下一帧重建，见 `Begin`）。</summary>
        private static int _fontGen = -1;
        private static readonly Dictionary<int, GUIStyle> Styles = new Dictionary<int, GUIStyle>();

        public static void Begin()
        {
            _scrollShift = Vector2.zero;      // ★ 每帧从"不在滚动区里"开始（异常中断也不会把偏移留给下一帧）
            _k = Mathf.Clamp(Screen.height / 900f, 0.55f, 2.2f);
            _vw = Screen.width / _k;
            _vh = Screen.height / _k;
            // ★ `_k` 变了 ⇒ 所有按像素字号缓存的样式**全部作废**（见 `_styleK` 的注释）★
            if (!Mathf.Approximately(_styleK, _k))
            {
                _styleK = _k;
                Styles.Clear();
                WrapStyles.Clear();
            }
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                _white.hideFlags = HideFlags.HideAndDontSave;
            }
            // ★ 字体跟着语言走 ★ 切语言时 `L10n.FontGeneration` 加一 ⇒ 下一帧在这里重建：
            //   ① 换成该语言的系统字体（YaHei 没有 Hangul / Arabic ⇒ 韩文/乌尔都文不换就是空白）
            //   ② 把按字号缓存的 GUIStyle 一起清掉 —— 它们手里握着**旧 Font**，不清等于没换 ✓
            if (_font == null || _fontGen != L10n.FontGeneration)
            {
                _fontGen = L10n.FontGeneration;
                _font = null;
                string[] candidates = L10n.Fonts;
                for (int i = 0; i < candidates.Length; i++)
                {
                    Font f = Font.CreateDynamicFontFromOSFont(candidates[i], 16);
                    if (f != null) { _font = f; break; }
                }
                Styles.Clear();
                WrapStyles.Clear();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 基础绘制件
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 虚拟坐标 → 屏幕坐标。
        /// ★ 滚动区里要再加一笔 <see cref="_scrollShift"/>：`GUI.BeginScrollView` 的内容原点在视口左上角，
        ///   补上这一笔之后，滚动区里的每一行**照旧按绝对虚拟坐标画**，坐标一个字都不用改 ✓
        /// ★ 区外 `_scrollShift` 恒为 0 ⇒ 面板框 / 标题 / 页脚的像素与改前完全一致 ✓
        /// </summary>
        private static Rect S(float x, float y, float w, float h)
            => new Rect(x * _k + _scrollShift.x, y * _k + _scrollShift.y, w * _k, h * _k);

        private static Rect S(Rect r) => S(r.x, r.y, r.width, r.height);

        /// <summary>填一块纯色。`c.a < 1` 时用 `GUI.color` 的 alpha 混合。</summary>
        public static void Fill(Rect vr, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(S(vr), _white);
            GUI.color = prev;
        }

        /// <summary>画一个框（四条边）。</summary>
        public static void Frame(Rect vr, Color c, float thickness = 2f)
        {
            Fill(new Rect(vr.x, vr.y, vr.width, thickness), c);
            Fill(new Rect(vr.x, vr.y + vr.height - thickness, vr.width, thickness), c);
            Fill(new Rect(vr.x, vr.y, thickness, vr.height), c);
            Fill(new Rect(vr.x + vr.width - thickness, vr.y, thickness, vr.height), c);
        }

        /// <summary>奶油底 + 木色框的面板。</summary>
        public static void PanelBox(Rect vr)
        {
            Fill(vr, Cream);
            Fill(new Rect(vr.x, vr.y, vr.width, 6f), Wood);
            Frame(vr, Wood, 3f);
        }

        private static GUIStyle Style(int size, TextAnchor anchor, bool bold)
        {
            // ★ 键用**像素字号** `px`（= 设计字号 × `_k`），不用设计字号：
            //   样式里只有 `fontSize` 与 `size` 有关，其余（对齐/粗体）都在键里 ⇒ 用 `px` 做键
            //   既不会撞车，又保证换分辨率后拿到的是**按新 `_k` 建的新样式** ✓
            int px = Mathf.Max(8, Mathf.RoundToInt(size * _k));
            int key = (px << 4) | ((int)anchor << 1) | (bold ? 1 : 0);
            if (Styles.TryGetValue(key, out GUIStyle st) && st != null) return st;
            st = new GUIStyle(GUI.skin.label)
            {
                fontSize = px,
                alignment = anchor,
                wordWrap = false,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
            };
            if (_font != null) st.font = _font;
            Styles[key] = st;
            return st;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ★ v253：文字**不许**跟着鼠标变色（用户原话："移除各种文字鼠标放上去就变色的效果，
        //          只保留按键、能展开详细的"）★
        //
        //  ── 这条"变色"到底从哪来（不是本文件写的，是继承来的）──
        //  本文件所有 `GUIStyle` 都是 `new GUIStyle(GUI.skin.label)` 克隆的（见 `Style` / `WrapStyle`），
        //  克隆**只**显式设了 `normal.textColor`，而内置皮肤 label 的 `hover` / `active` / `focused`
        //  文字色是**浅色** ✗ ⇒ 这种样式一旦交给 `GUI.Button`，鼠标压上去字就变浅、发虚。
        //  （同一个坑在登录界面被实测记录过，见 `LoginHud.Prepare` 的注释："那顆的字就變淺 ✗"）
        //  `GUI.Label` 只用 `normal`，所以普通文字本来就不受影响 —— 真正会变色的**只有按钮上的字**。
        //
        //  ── 改法（只管表现，交互一个字没动）──
        //   · 普通文字（`Text` / `TextWrap`）：八个状态全钉成同一个颜色 ⇒ 悬停/按下都不变色 ✓
        //   · 按钮（`Button`）：**保留**悬停变色（用户点名保留"按键"），但换成一个读得出来的木色
        //     `Wood`，而不是继承来的近白 ✗；不可点的按钮一律钉成 `InkSoft`（灰件不该有任何反应）✓
        //   · 能展开详情的：那不是文字色，而是"鼠标停上去就出详情框/浮框"（`ImGuiHud` 的
        //     `_offerShown` / `RequestTip`），**一行都没动** ✓
        //   · 按钮底色的"悬停加亮"（见 `Button` 里那句 `Fill`）与光标变化（`GardenCursor`）
        //     **也照旧** —— 那是点击反馈，不是文字变色 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 把 <paramref name="st"/> 的八个状态文字色钉住：`normal` / `focused` / `onNormal` / `onFocused`
        /// 用静止色，`hover` / `active` / `onHover` / `onActive` 用悬停色
        /// （普通文字两个色传同一个 ⇒ 等于"悬停/按下都不变色"）。
        ///
        /// 为什么 `focused` 必须钉成**静止色**而不是悬停色：`GUI.Button` 被点过之后会拿到键盘焦点，
        /// 鼠标移开之后焦点还在 ⇒ 若 `focused` 用悬停色，那颗按钮的字就会"卡"在悬停色上，看起来像坏了 ✗
        ///
        /// 为什么必须显式钉：本文件的样式全是 `new GUIStyle(GUI.skin.label)` 克隆来的，
        /// 克隆只显式设了 `normal.textColor` ⇒ `hover` / `active` / `focused` 仍是内置皮肤 label 的浅色，
        /// 交给 `GUI.Button` 用就是"鼠标一压上去字就变浅、发虚" ✗
        /// </summary>
        private static void PinStateText(GUIStyle st, Color normal, Color hover)
        {
            st.normal.textColor = normal;
            st.hover.textColor = hover;
            st.active.textColor = hover;
            st.focused.textColor = normal;
            st.onNormal.textColor = normal;
            st.onHover.textColor = hover;
            st.onActive.textColor = hover;
            st.onFocused.textColor = normal;
        }

        /// <summary>一行字。<paramref name="vr"/> 是虚拟坐标的矩形（文字在其中按 anchor 摆）。</summary>
        public static void Text(Rect vr, string text, int size, Color color,
                                TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false)
        {
            text = L10n.S(text);        // ★ 本地化：主城/登录/各面板的文字都从这里出去（中文原样返回）
            GUIStyle st = Style(size, anchor, bold);
            Color prev = GUI.color;
            Color prevText = st.normal.textColor;
            GUI.color = Color.white;
            PinStateText(st, color, color);     // ★ v253：普通文字 —— 悬停/按下都不变色
            GUI.Label(S(vr), text, st);
            st.normal.textColor = prevText;
            GUI.color = prev;
        }

        /// <summary>自动换行的多行字（`Text` 为了排版精度关掉了 wordWrap，长说明必须走这个）。</summary>
        public static void TextWrap(Rect vr, string text, int size, Color color,
                                    TextAnchor anchor = TextAnchor.UpperLeft)
        {
            text = L10n.S(text);        // ★ 同上：图鉴的整段说明也过表
            GUIStyle st = WrapStyle(size, anchor);
            Color prev = GUI.color;
            Color prevText = st.normal.textColor;
            GUI.color = Color.white;
            PinStateText(st, color, color);     // ★ v253：整段说明（长文本）同样不许悬停变色
            GUI.Label(S(vr), text ?? "", st);
            st.normal.textColor = prevText;
            GUI.color = prev;
        }

        /// <summary>量字宽用的复用 `GUIContent`（每帧几十次量，别每次都 new 一个）。</summary>
        private static readonly GUIContent Measure = new GUIContent();

        /// <summary>
        /// 量一行字的**虚拟像素**宽度（与 `Text` 用的是同一份缓存样式 ⇒ 量出来的就是画出来的）。
        /// 给"写死宽度的 rect 里塞动态文本"用：先量、再定宽 / 再缩字号 ✓
        /// </summary>
        public static float TextWidth(string text, int size, bool bold = false)
        {
            text = L10n.S(text);
            if (string.IsNullOrEmpty(text) || _k <= 0f) return 0f;
            GUIStyle st = Style(size, TextAnchor.MiddleLeft, bold);
            Measure.text = text;
            return st.CalcSize(Measure).x / _k;
        }

        /// <summary>
        /// ★ 防重叠：**一行**字挤进一个写死宽度的 rect ★
        ///
        /// 用途（用户报「部分文字重叠 / 超框」）：金币数字、价格、存档名、提示语、落盘路径 ——
        /// 这些文本的长度随数据变，而 rect 是按最坏情况写死的 220 / 460 / 520 …
        /// 原来 `Style` 的 clipping 是 `Overflow` ⇒ 字直接从 rect 里**画出去**，压到旁边的按钮上 ✗
        ///
        /// 做法：先 `CalcSize`，超了就**逐级降设计字号**（下限 `minSize`，默认 9），
        /// 量到装得下再画 —— 字号本身仍是"设计值 × `_k`"（见 `Style`），**没有第二套缩放** ✓
        /// </summary>
        public static void TextFit(Rect vr, string text, int size, Color color,
                                   TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false, int minSize = 9)
        {
            text = L10n.S(text);        // 与 `Text` 一致（重复调用是幂等的）
            if (string.IsNullOrEmpty(text) || vr.width <= 1f) return;
            float limit = vr.width * _k - 1f;          // 换算到**屏幕 px**：`CalcSize` 的单位是屏幕 px
            int use = Mathf.Max(minSize, size);
            Measure.text = text;
            while (use > minSize)
            {
                GUIStyle probe = Style(use, anchor, bold);
                if (probe.CalcSize(Measure).x <= limit) break;
                use--;
            }
            Text(vr, text, use, color, anchor, bold);
        }

        private static readonly Dictionary<int, GUIStyle> WrapStyles = new Dictionary<int, GUIStyle>();

        private static GUIStyle WrapStyle(int size, TextAnchor anchor)
        {
            // ★ 同 `Style`：键用像素字号，换分辨率后重新建（否则整段说明也是旧字号 ⇒ 挤出一行）★
            int px = Mathf.Max(8, Mathf.RoundToInt(size * _k));
            int key = (px << 4) | (int)anchor;
            if (WrapStyles.TryGetValue(key, out GUIStyle st) && st != null) return st;
            st = new GUIStyle(GUI.skin.label)
            {
                fontSize = px,
                alignment = anchor,
                wordWrap = true,
                fontStyle = FontStyle.Normal,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
            };
            if (_font != null) st.font = _font;
            WrapStyles[key] = st;
            return st;
        }

        /// <summary>一个按钮。<paramref name="enabled"/> 为 false 时灰掉且点不动。</summary>
        public static bool Button(Rect vr, string text, bool enabled = true, int size = 15)
        {
            text = L10n.S(text);        // ★ 按钮上的字同样过表（"关闭 (E)" / "解锁" / "金币不足" …）
            bool prev = GUI.enabled;
            GUI.enabled = enabled;
            GUIStyle st = Style(size, TextAnchor.MiddleCenter, true);
            Color prevText = st.normal.textColor;
            // ★ v253：按钮**保留**悬停变色（用户点名保留"按键"），但钉成读得出来的木色；
            //   不能再用继承来的浅色 —— 那会让字在奶油底上"变白/发虚"（见 `PinStateText` 的注释）
            Color ink = enabled ? Ink : InkSoft;
            PinStateText(st, ink, enabled ? Wood : InkSoft);

            // 底：可点 = 亮奶油 + 木框；不可点 = 灰底。悬停加亮 —— 用鼠标位置自己判，不依赖 GUIStyle 的 hover 态
            bool hover = enabled && S(vr).Contains(Event.current.mousePosition);
            Fill(vr, enabled ? (hover ? new Color(1f, 0.99f, 0.90f, 1f) : CreamDark) : new Color(0.85f, 0.83f, 0.78f, 0.85f));
            Frame(vr, enabled ? Wood : InkSoft, 2f);
            bool clicked = GUI.Button(S(vr), text, st);
            st.normal.textColor = prevText;
            GUI.enabled = prev;
            return clicked && enabled;
        }

        /// <summary>一条进度条（0~1）。</summary>
        public static void Bar(Rect vr, float t, Color fill, Color back)
        {
            Fill(vr, back);
            float w = vr.width * Mathf.Clamp01(t);
            if (w > 0f) Fill(new Rect(vr.x, vr.y, w, vr.height), fill);
            Frame(vr, InkSoft, 1.5f);
        }

        /// <summary>圆形近似（小地图光点用）：一个方块 + 稍大的半透明方块做外圈光晕。</summary>
        public static void Dot(Vector2 center, float radius, Color core, Color halo)
        {
            Fill(new Rect(center.x - radius * 2.2f, center.y - radius * 2.2f, radius * 4.4f, radius * 4.4f), halo);
            Fill(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), core);
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ v217：滚动区（商店 / 服装店 / 图鉴 三处共用）★
        // ══════════════════════════════════════════════════════════════════
        //
        //  用户原话：「商店、服装店、图鉴增加滑动条，内容不越界」。
        //
        //  ── 为什么必须用 `GUI.BeginScrollView`（`Rect` 那版），而不是 `GUILayout` 那版 ──
        //  `GUILayout.BeginScrollView` 只按**布局**裁切；本文件所有面板都是**手算 `Rect`** 画的
        //  （`box.x + 24` 那种），布局系统根本不知道这些矩形存在 ⇒ 换成 `GUILayout` 版**不会裁**，
        //  内容照样画到面板外面，那正是"越界" ✗
        //  `GUI.BeginScrollView(position, scroll, viewRect)` 建立的是一个**真裁剪区**：
        //  视口四边之外一律不画、也不收鼠标事件 ⇒ 越界在机制上不可能 ✓
        //
        //  ── 三种滚法 ──
        //   ① 滚轮：`GUI.BeginScrollView` 自己吃（鼠标在视口内才吃）✓
        //   ② 拖动：它自己画的右侧滑条可拖（`GUIStyle.verticalScrollbar`）✓
        //   ③ 键盘：它**不管** `PageUp`/`PageDown`/`Home`/`End` ⇒ 在 `ScrollBegin` 里自己接 ✓

        /// <summary>视口右侧留给滚动条的宽度（虚拟 px）。内容按 `视口宽 − 它` 排 ⇒ 不会冒出横向条。</summary>
        public const float ScrollbarW = 20f;

        /// <summary>
        /// 滚动区内 `S()` 追加的**屏幕 px** 偏移 = 滚动量 − 视口左上角。
        /// 只在 `ScrollBegin` … `ScrollEnd` 之间非零。
        /// </summary>
        private static Vector2 _scrollShift = Vector2.zero;

        /// <summary>三处的滚动量（**虚拟 px**，与坐标同一套单位 ⇒ 换分辨率不会跳）。按页 / 页签分开记。</summary>
        private static readonly float[] ScrollShopPage = new float[4];
        private static readonly float[] ScrollCodexPage = new float[4];
        private static float _scrollClothing;

        /// <summary>最近一次画三处面板时的滚动区读数（探针 / 交付引用；面板每帧重写，不进日志）。</summary>
        public static string ScrollProbe = "(还没画过)";
        private static string _lastProbe;

        /// <summary>
        /// ★ v237：每个面板键的**结构化读数**（`内容高` / `视口高` / `可滚余量` / `当前 scroll`）。
        /// 面板每帧重写这一份；探针与验收直接读它 —— 不用去解析 <see cref="ScrollProbe"/> 那句话 ✓
        /// </summary>
        public struct ScrollReading
        {
            public string Key;
            public float ContentH;
            public float ViewH;
            public float ScrollY;
            /// <summary>这一帧真正加到 `S()` 上的屏幕 px 偏移（= −scroll.y × `_k`）；0 ⇒ 内容一格没动。</summary>
            public float ShiftY;
            /// <summary>可滚余量 = max(0, 内容高 − 视口高)。**这一项为 0 ⇒ 滚动条一格都动不了。**</summary>
            public float Room => Mathf.Max(0f, ContentH - ViewH);
            public bool Scrollable => Room > 0.5f;
        }

        /// <summary>最近一次 `ScrollBegin` 算出的屏幕偏移（给读数用；`ScrollEnd` 会把它清回 0）。</summary>
        private static float _lastShiftY;

        /// <summary>滚轮兜底是否已经报过一次（每次运行只报一条，别把控制台刷爆）。</summary>
        private static bool _wheelFallbackLogged;

        private static readonly Dictionary<string, ScrollReading> Readings =
            new Dictionary<string, ScrollReading>(8);

        /// <summary>取某个面板键最近一次的读数（没画过 ⇒ false）。</summary>
        public static bool TryReading(string key, out ScrollReading r) => Readings.TryGetValue(key, out r);

        /// <summary>三类面板的读数快照（一行一条，供探针 / 验收打印）。</summary>
        public static string ReadingsReport()
        {
            var keys = new List<string>(Readings.Keys);
            keys.Sort(string.CompareOrdinal);
            var sb = new System.Text.StringBuilder(512);
            for (int i = 0; i < keys.Count; i++)
            {
                ScrollReading r = Readings[keys[i]];
                if (i > 0) sb.Append('\n');
                sb.Append(r.Key).Append("｜内容高=").Append(r.ContentH.ToString("0.#"))
                  .Append("｜视口高=").Append(r.ViewH.ToString("0.#"))
                  .Append("｜可滚=").Append(r.Room.ToString("0.#"))
                  .Append("｜scroll.y=").Append(r.ScrollY.ToString("0.#"))
                  .Append("｜偏移=").Append(r.ShiftY.ToString("0.#"))
                  .Append(r.Scrollable ? "｜✔可滚" : "｜✗没有可滚余量");
            }
            return sb.Length == 0 ? "(还没有任何面板画过)" : sb.ToString();
        }

        /// <summary>
        /// 开一个**纵向**滚动区，返回这一帧实际的滚动量（虚拟 px）。
        /// <paramref name="view"/> = 视口（**虚拟坐标**，调用方保证它整个落在面板框内）；
        /// <paramref name="contentH"/> = 内容总高（**虚拟坐标**，≥ 视口高就出可拖的条）。
        /// </summary>
        private static float ScrollBegin(Rect view, float scroll, float contentH)
        {
            float max = Mathf.Max(0f, contentH - view.height);

            // ③ 键盘：一次翻一屏（留 48 的上下文，不会"翻完全看不出自己在哪"）
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                float step = Mathf.Max(40f, view.height - 48f);
                if (e.keyCode == KeyCode.PageDown) { scroll += step; e.Use(); }
                else if (e.keyCode == KeyCode.PageUp) { scroll -= step; e.Use(); }
                else if (e.keyCode == KeyCode.Home) { scroll = 0f; e.Use(); }
                else if (e.keyCode == KeyCode.End) { scroll = max; e.Use(); }
            }
            scroll = Mathf.Clamp(scroll, 0f, max);

            // ① ②：`S(view)` 必须在偏移归零时算（此时还没进区）
            Rect sv = S(view);

            // ★★ v254：**滚轮只在这里吃一次**（用户报「滑动条灵敏度异常」）★★
            //
            //  ── 改前为什么敏感到没法用 ──
            //   ① `GUI.BeginScrollView` 自己也吃滚轮，而且按 **Unity 的滚动单位**滚
            //      （≈20 单位/格，与 `_k` 无关）⇒ 一格只走 ~15 虚拟 px（几乎不动）；
            //   ② 下面原有一段"兜底"，按 `WheelStep` 再补一次（≈50~110 px/格）；
            //   ③ 快速滚时 `delta.y` 可能是 3、6 ⇒ 又整倍放大。
            //   三者叠加 ⇒ 同一页在不同时刻速度差 3~7 倍，手感是"要么不动要么猛冲" ✗
            //
            //  ── 改法 ──
            //   在这里**先**把事件吃掉并算好 `scroll`，`BeginScrollView` 拿到的就是结果值
            //   （事件已 `Use()` ⇒ Unity 那套滚轮不再参与）⇒ 全程只有一条滚法 ✓
            //   `delta.y` 夹到 ±3 格：再快的滚也只是一次 3 格，不会越过一整页 ✓
            //   拖条仍然有效：`BeginScrollView` 之后回读 `sp.y`（见下）✓
            {
                Event w0 = Event.current;
                if (w0 != null && w0.type == EventType.ScrollWheel && sv.Contains(w0.mousePosition))
                {
                    float notches = Mathf.Clamp(w0.delta.y, -3f, 3f);
                    scroll = Mathf.Clamp(scroll + notches * WheelStep(view.height), 0f, max);
                    w0.Use();
                    if (!_wheelFallbackLogged)
                    {
                        _wheelFallbackLogged = true;
                        Log.Info("[Town] 滚轮改由 `ScrollBegin` 统一处理（一格 = 视口 10%，夹 48~160 虚拟 px），" +
                                 $"视口 {sv.width:0}×{sv.height:0}，内容高 {contentH:0.#}。");
                    }
                }
            }
            // 右侧让出一根条：`20` 是虚拟 px，`18` 是屏幕 px 的下限（`_k` 小到 0.55 时默认条仍有 ~16px）
            float contentW = Mathf.Max(8f, sv.width - Mathf.Max(ScrollbarW * _k, 18f));
            Vector2 sp = GUI.BeginScrollView(sv, new Vector2(0f, scroll * _k),
                new Rect(0f, 0f, contentW, Mathf.Max(sv.height, contentH * _k)));

            // ★★★ 这一行是"滑动条动了、商品不动"的病根 ★★★
            //
            //  —— 病因 ——
            //   `GUI.BeginScrollView` 的内容坐标系**已经把滚动量减掉了**：官方例子
            //   `GUI.BeginScrollView(new Rect(10,300,100,100), pos, new Rect(0,0,220,200))`
            //   里面画的是 `new Rect(0,180,…)` —— **内容从 0 开始，偏移由 Unity 自己加**。
            //   本文件所有绘制都按**屏幕绝对虚拟坐标**写（`box.x + 24` 那种）⇒ 需要把
            //   "绝对 → 内容坐标"换算一次：`S(v) = v·k − 视口屏幕原点`。
            //   ✗ 改前写的是 `sp − sv`（**多加了滚动量**）：Unity 再减一次 ⇒ 两次抵消 ⇒
            //     内容**原地不动**，而滑条照动 —— 正是用户报的"滑动后商品没有变化/下面全空白"✗
            //   ✓ 改后只减视口原点（`−sv`）：Unity 减滚动量 ⇒ 内容真的跟着走 ✓
            //
            //  ⚠️ 上一版那套"读数对得上公式"是**算在纸上的**：`lastRowBottom − max ≤ 视口高`
            //     与滚动量无关（恒成立）⇒ 读数绿、画面死。见 `ReportScroll` 的改法 ✓
            _scrollShift = new Vector2(-sv.x, -sv.y);
            _lastShiftY = -sp.y;                      // 真正作用到屏幕上的偏移（负 = 内容上移）

            // ★ v237：**滚轮兜底** —— 在 `BeginScrollView` 之后再补一次 ★
            //   为什么要有这一笔：`GUI.BeginScrollView` 自己吃滚轮，但它只在"事件还是新鲜的时候"吃；
            //   一旦这一颗 `ScrollWheel` 在别处被 `Event.Use()` 过一次（本工程的 `Event.current` 上
            //   已经不止一处读鼠标），它就把整颗事件跳过 ⇒ 用户看到的是"条在、拖不动、滚不动" ✗
            //   兜底条件写得很窄：**事件仍是 `ScrollWheel`（= 上面那步没吃）且鼠标落在视口里** ✓
            //   顺序为什么必须在 `BeginScrollView` **之后**：在它之前 `Use()` 会让它拿到 `Used` 事件、
            //   直接 early-return ⇒ 那一帧的裁剪区与滑条都不会建立（等于整块不画）✓
            // ★ v254：只回读"拖动滑条 / 钳位"之后的真实位置（滚轮已在上面算完，不再有第二套）✓
            float target = Mathf.Clamp(sp.y / _k, 0f, max);
            return target;
        }

        /// <summary>
        /// 滚轮一格走多少虚拟 px。
        /// ★ v254：从 `max(24, 视口×12%)` 改成 **`clamp(视口×10%, 48, 160)`** ★
        ///   下限 48（≈ 一行 60px 商品的一多半：一格一定看得出动了）；
        ///   上限 160（再大的面板也不会"一格跳过一整屏"）；
        ///   与 `PageDown`（整屏 − 48）**仍然是两个步长**：滚轮细调、翻页粗调 ✓
        /// </summary>
        public static float WheelStep(float viewH) => Mathf.Clamp(viewH * 0.10f, 48f, 160f);

        /// <summary>关滚动区，并立刻把偏移归零 —— 随后画的页脚、关闭按钮照常按绝对坐标走 ✓</summary>
        private static void ScrollEnd()
        {
            GUI.EndScrollView();
            _scrollShift = Vector2.zero;
        }

        /// <summary>
        /// ★ 借给别的 HUD 用 ★（`LoginHud` 的设置面板要给 13 行键位加滚动，而那一段是用
        /// `TownHud.Text/Fill/Frame` 画的 ⇒ 必须让它们也吃到同一份偏移）。
        /// 语义与 `ScrollBegin` 内部完全一致：**屏幕 px**，等于 `−视口屏幕原点`
        /// （`GUI.BeginScrollView` 自己会再减滚动量，所以这里**不能**再加滚动量 ✓）。
        /// 用完记得 `SetScrollOffset(Vector2.zero)` 复位。
        /// </summary>
        public static void SetScrollOffset(Vector2 screenOffset)
        {
            _scrollShift = screenOffset;
        }

        /// <summary>
        /// 记一行**四边读数**，并只在读数变化时（换面板 / 换页签 / 改分辨率）写一条日志 —— 每帧写会把控制台刷爆。
        ///
        /// 判据（★ 硬 ★，v238 改成**真的能判"最后一条看不看得见"**）：
        ///   · 视口四边都落在面板框内 ⇒ 视口 ⊂ 面板；
        ///   · 视口是**真裁剪区** ⇒ 画进视口的内容再多也出不去（四边都裁）；
        ///   · **滑到底**（`scroll = contentH − view.height`）时，最后一条的下沿在**内容坐标系**里
        ///     是 `lastRowBottom`，减掉可滚余量 `max` 就是它在**视口里的 y**；它 ≤ 视口高 ⇒ 整条可见 ✓
        ///
        /// ⚠️ 改前写的是 `lastRowBottom − max ≤ 视口高`，但 `lastRowBottom` 里含着 `view.y`、
        ///   而 `max` 里含着 `padTop` —— 两边的参照系不同，算出来恒等于 `view.yMax` ⇒ **永远绿** ✗
        ///   那正是"读数通过、用户说无效"。现在 `lastRowBottom` 一律按**内容坐标**传（0 = 内容顶）✓
        /// </summary>
        private static void ReportScroll(string key, Rect panel, Rect view, float contentH, float lastRowBottom,
                                         float scrollY)
        {
            bool inX = view.x >= panel.x - 0.5f && view.xMax <= panel.xMax + 0.5f;
            bool inY = view.y >= panel.y - 0.5f && view.yMax <= panel.yMax + 0.5f;
            float max = Mathf.Max(0f, contentH - view.height);
            // 滑到底时，最后一条的下沿落在视口坐标里的 y（0 = 视口顶）
            float bottomInView = lastRowBottom - max;
            bool lastFull = bottomInView <= view.height + 0.01f;
            Readings[key] = new ScrollReading
            {
                Key = key,
                ContentH = contentH,
                ViewH = view.height,
                ScrollY = scrollY,
                ShiftY = _lastShiftY,
            };
            ScrollProbe = $"{key}｜面板=({panel.x:0.#},{panel.y:0.#},{panel.width:0.#},{panel.height:0.#})" +
                          $"｜视口=({view.x:0.#},{view.y:0.#},{view.width:0.#},{view.height:0.#})" +
                          $"｜内容高={contentH:0.#}｜视口高={view.height:0.#}｜可滚={max:0.#}｜scroll.y={scrollY:0.#}" +
                          $"｜偏移={_lastShiftY:0.#}" +
                          $"｜四边={(inX && inY ? "OK" : "✗越界")}" +
                          $"｜滑到底时末条下沿(视口坐标)={bottomInView:0.#} vs 视口高={view.height:0.#}" +
                          $" ⇒ {(lastFull ? "末条完整可见" : "✗末条被裁")}";
            if (_lastProbe == ScrollProbe) return;
            _lastProbe = ScrollProbe;
            Log.Info("[Town] SCROLL " + ScrollProbe);
        }

        // ══════════════════════════════════════════════════════════════════
        // 常驻：金币条 / 交互提示
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 左上角：**只显示金币数字 ＋ 两行按键提示**。
        ///
        /// ★ v216：用户原话「UI重新设计左上角**只显示金币数量**和**主城按键提示**（移动、互动）」★
        /// ⇒ 上一版那个 372×156 的四行面板（金币/每秒/田地/皮肤/BOSS 掉落）**收掉**。
        ///   那几行的信息没丢：田地与成熟在小地图上，BOSS 掉落/解锁在仓库与商店面板里 ✓
        /// </summary>
        public static void DrawTopBar(TownState s, int plotCount)
        {
            if (s == null) return;
            var bar = new Rect(18f, 18f, 306f, 120f);
            PanelBox(bar);

            // 金币：一个"金"字牌 ＋ 大号数字
            Fill(new Rect(bar.x + 14f, bar.y + 14f, 50f, 50f), Gold);
            Fill(new Rect(bar.x + 21f, bar.y + 21f, 36f, 36f), new Color(1f, 0.92f, 0.55f, 1f));
            Text(new Rect(bar.x + 8f, bar.y + 8f, 62f, 62f), "金", 28, new Color(0.55f, 0.38f, 0.05f),
                 TextAnchor.MiddleCenter, true);
            // ★ 金币是动态位数（"1,234,567,890" 在 32 号下就超过 226）⇒ 量过再画，绝不压到右边框上
            TextFit(new Rect(bar.x + 74f, bar.y + 10f, 226f, 40f), s.Coins.ToString("N0"), 32, Ink,
                    TextAnchor.MiddleLeft, true);
            Text(new Rect(bar.x + 76f, bar.y + 48f, 220f, 18f), "主城金币", 12, InkSoft, TextAnchor.MiddleLeft);

            Fill(new Rect(bar.x + 14f, bar.y + 70f, bar.width - 28f, 1.5f), CreamDark);

            // 两行按键提示（用户点名的"移动 / 互动"）—— 行高 24，两行恰好落在 120 高的框内 ✓
            Text(new Rect(bar.x + 16f, bar.y + 73f, 284f, 22f), "W A S D　移动", 14, Ink, TextAnchor.MiddleLeft, true);
            Text(new Rect(bar.x + 16f, bar.y + 95f, 284f, 22f), "E　互动 / 关面板　Esc 关闭", 13, InkSoft,
                 TextAnchor.MiddleLeft);
        }

        /// <summary>
        /// ★ v216 ⑤：**世界空间名字标签**（用户："在地图上标注功能性建筑的名字"）★
        /// 把 `TownBuildings.Labels` 里的世界坐标投影到屏幕上，画一小块奶油底木框 + 名字 + 一根指线。
        /// </summary>
        public static void DrawWorldLabels(Camera cam)
        {
            if (cam == null) return;
            var labels = TownBuildings.Labels;
            for (int i = 0; i < labels.Count; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(labels[i].World);
                if (sp.z <= 0f) continue;                          // 在相机背后
                float x = sp.x / _k;
                float y = _vh - sp.y / _k;                          // 屏幕(y 向上) → 虚拟像素(y 向下)
                if (x < -200f || x > _vw + 200f || y < -60f || y > _vh + 60f) continue;

                // ★ 名字牌宽度**真的量一遍**（原写法 `t.Length * 15f` 是按中文每字 15px 估的：
                //   本地化成英文/日文后同一个名字长度差一倍 ⇒ 牌太短、字画到框外；太长又挡住了旁边的建筑牌）✓
                string t = L10n.S(labels[i].Text);
                float w = Mathf.Clamp(TextWidth(t, 15, true) + 22f, 60f, 320f);
                var box = new Rect(x - w * 0.5f, y - 13f, w, 26f);
                Fill(box, new Color(0.98f, 0.95f, 0.86f, 0.90f));
                Frame(box, Wood, 2f);
                Text(box, t, 15, Ink, TextAnchor.MiddleCenter, true);
                Fill(new Rect(x - 1f, y + 13f, 2f, 10f), Wood);     // 指线
            }
        }

        /// <summary>底部中间的交互提示。`text` 为 null ⇒ 不画。</summary>
        public static void DrawHint(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            // ★ 提示语是**动态**的（本地化后长度差很多）⇒ 框宽按文字量出来，再钳在屏内：
            //   原来写死 680，英文/日文一长就从框里画出去（超框），窄屏时框本身还会出界 ✗
            float w = Mathf.Clamp(TextWidth(text, 20, true) + 64f, 260f, Mathf.Max(260f, _vw - 24f));
            var box = new Rect(_vw * 0.5f - w * 0.5f, _vh - 108f, w, 52f);
            PanelBox(box);
            Text(box, text, 20, Ink, TextAnchor.MiddleCenter, true);
        }

        // ══════════════════════════════════════════════════════════════════
        // 面板：传送门前的难度选择（低 / 中 / 高）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v252：难度选择面板。返回 `PickClose` = 关掉它（等价于按 E）、`PickBattle` = 进战斗、
        /// ★ v258：`PickTraining` = 进训练场（自由练习，与三档难度并列的第四颗按钮）。
        ///
        /// ── 为什么这个面板不做成"点一下档位就立刻进战斗" ──
        ///   `GoBattle()` 是**同步** `LoadScene`（见 `TownBootstrap.GoBattle` 与 `LoginLoading` 的说明：
        ///   切场景那一帧回不来）⇒ 若档位按钮同时承担"选"与"走"，玩家点错一次就没有撤销的机会，
        ///   只能等一整局打完 ✗ 所以按钮分工是：**点档位 = 选（可改）**，**点"开始战斗" = 走** ✓
        ///
        /// ── 数值从哪来 ──
        ///   三个档位的名字与乘数**全部**来自 `RunDifficulty`（唯一出处），
        ///   面板里一个数字都不自己写 —— 否则"界面上写 ×1.2、实际乘的是 ×1.15"这种错永远不会被发现 ✗
        /// </summary>
        public static int DrawDifficulty()
        {
            Rect box = Centered(900f, 560f);
            PanelBox(box);

            Text(new Rect(box.x + 28f, box.y + 16f, 600f, 44f), L10n.T("difficulty.title"), 30, Ink, TextAnchor.MiddleLeft, true);
            TextWrap(new Rect(box.x + 28f, box.y + 62f, box.width - 56f, 56f),
                     L10n.T("difficulty.sub"), 15, InkSoft, TextAnchor.UpperLeft);

            RunDifficulty.Level cur = RunDifficulty.Current;
            float y = box.y + 132f;
            for (int i = 0; i < 3; i++)
            {
                RunDifficulty.Level lv = RunDifficulty.At(i);
                bool sel = lv == cur;
                var row = new Rect(box.x + 28f, y, box.width - 56f, 92f);

                // 选中行：暗底 + 金框（与商店"已买"那种灰底形成对比，一眼看出当前是哪一档）
                Fill(row, sel ? new Color(1f, 0.96f, 0.82f, 1f) : new Color(1f, 0.98f, 0.92f, 0.55f));
                Frame(row, sel ? Wood : InkSoft, sel ? 3f : 1.5f);

                // 档位色块（低=绿 / 中=黄 / 高=红，与下面的名字无关，纯识别）
                Color chip = lv == RunDifficulty.Level.High ? Bad
                           : (lv == RunDifficulty.Level.Mid ? new Color(0.86f, 0.62f, 0.16f) : Good);
                Fill(new Rect(row.x + 16f, row.y + 26f, 40f, 40f), chip);
                Frame(new Rect(row.x + 16f, row.y + 26f, 40f, 40f), InkSoft, 1.5f);

                Text(new Rect(row.x + 72f, row.y + 8f, 260f, 38f), RunDifficulty.NameOf(lv), 22, Ink, TextAnchor.MiddleLeft, true);
                Text(new Rect(row.x + 72f, row.y + 46f, 560f, 36f), RunDifficulty.EffectOf(lv), 15, InkSoft);
                Text(new Rect(row.x + row.width - 136f, row.y, 120f, 92f),
                     sel ? L10n.T("difficulty.current") : L10n.T("difficulty.pick"), 16,
                     sel ? Wood : InkSoft, TextAnchor.MiddleCenter, true);

                // 整行都可点（比只有一颗小按钮好按）。★ 这里**不能**用 `Button()` 铺满整行：
                //   它会先画一层奶油底 + 木框，把上面刚画好的名字/说明**整个盖掉** ✗
                //   ⇒ 自己判一次鼠标按下（与 `DrawWarehouse` 的"点外面关掉"用的是同一个事件）✓
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                    && S(row).Contains(Event.current.mousePosition) && !sel)
                {
                    RunDifficulty.Set(lv);
                    Log.Info("[Town] 难度选定：" + RunDifficulty.NameOf(lv) +
                             $"（血量 ×{RunDifficulty.HpMult:0.00} / 伤害 ×{RunDifficulty.DmgMult:0.00}）");
                }
                y += 100f;
            }

            // ── ★ v258：训练场（自由练习）★ ──
            //  用户原话：「把训练场模式加入到传送门选择中」。
            //
            //  ── 为什么**单独一颗按钮**，不塞进上面那个三档循环 ──
            //   上面三行是 `RunDifficulty.At(i)` 按**难度档位下标**渲染的（`i` 直接喂给 `RunDifficulty.Set`），
            //   把"训练场"插进这个循环就等于给难度枚举加了第四档 ⇒ `RunDifficulty.Current` 会被写成非法值，
            //   血量/伤害乘数跟着崩 ✗ 所以它只是并列的第四颗按钮，**不碰 `RunDifficulty` 一个字段** ✓
            //
            //  ── 为什么放在这个坐标 ──
            //   面板 `Centered(900,560)` 的纵向占用：三行档位到 `box.y+424` 结束，
            //   底部「开始战斗 / 关闭」从 `box.y+482` 起，中间正好空着 58px。
            //   按钮放在这条空档里 ⇒ **面板尺寸、字号、其它按钮一个像素都不用动** ✓
            //   （横向只占左边 28…288，「开始战斗」在 600 之后，两者不可能重叠）
            bool train = Button(new Rect(box.x + 28f, box.y + 432f, 260f, 44f),
                                "训练场（自由练习）", true, 18);
            Text(new Rect(box.x + 300f, box.y + 432f, 290f, 44f),
                 "不存档 · 只练操作 · 随时返回", 14, InkSoft);

            Text(new Rect(box.x + 28f, box.y + box.height - 76f, box.width - 340f, 40f),
                 L10n.T("difficulty.hint"), 14, InkSoft);

            bool start = Button(new Rect(box.x + box.width - 300f, box.y + box.height - 78f, 168f, 46f),
                                L10n.T("difficulty.start"), true, 18);
            bool close = Button(new Rect(box.x + box.width - 122f, box.y + box.height - 78f, 94f, 46f), "关闭 (E)");

            if (Event.current.type == EventType.MouseDown)
            {
                Vector2 mp = Event.current.mousePosition;
                if (!S(box).Contains(mp)) close = true;
            }
            if (close) return PickClose;
            if (start) return PickBattle;
            if (train) return PickTraining;      // ★ v258：训练场（与三档难度并列，各走各的出口）★
            return PickNone;
        }

        // ══════════════════════════════════════════════════════════════════
        // 面板：种子仓库
        // ══════════════════════════════════════════════════════════════════

        /// <summary>种子仓库面板。返回 true = 用户要关掉它。</summary>
        public static bool DrawWarehouse(TownState s)
        {
            Rect box = Centered(780f, 610f);
            PanelBox(box);
            Text(new Rect(box.x + 24f, box.y + 14f, 500f, 40f), "种子仓库", 28, Ink, TextAnchor.MiddleLeft, true);

            // ★ 仓库空着 ⇒ 用户点名要的提示（§⑥）★
            //   为什么放在最上面而不是最后一行：这一版开局仓库是 **全 0**（不再白送种子），
            //   玩家第一次打开这个面板时"什么都没有"是最需要被解释的状态 ✓
            bool empty = s.WarehouseTotal <= 0;
            if (empty)
            {
                Fill(new Rect(box.x + 24f, box.y + 48f, box.width - 48f, 34f), new Color(0.86f, 0.42f, 0.36f, 0.22f));
                Frame(new Rect(box.x + 24f, box.y + 48f, box.width - 48f, 34f), Bad, 2f);
                Text(new Rect(box.x + 34f, box.y + 48f, box.width - 68f, 34f),
                    "仓库是空的 —— 主城的种子只能靠【打 BOSS 掉落】，商店不卖种子。",
                    16, Bad, TextAnchor.MiddleLeft, true);
            }
            else
            {
                Text(new Rect(box.x + 24f, box.y + 50f, 700f, 24f),
                    "种子默认存在这里；站到耕地上按 E 会从「这里有货的种子」里挑一个种下。", 14, InkSoft);
            }

            // 表头
            float hx = box.x + 26f;
            float hy = box.y + 92f;
            Text(new Rect(hx + 58f, hy, 160f, 24f), "种子", 14, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 226f, hy, 90f, 24f), "品质/等级", 14, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 336f, hy, 96f, 24f), "成熟(秒)", 14, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 444f, hy, 110f, 24f), "金币/秒", 14, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 566f, hy, 110f, 24f), "仓库数量", 14, InkSoft, TextAnchor.MiddleLeft, true);
            Fill(new Rect(hx, hy + 26f, box.width - 52f, 2f), CreamDark);

            float rowY = hy + 36f;
            for (int i = 0; i < TownData.Seeds.Length; i++)
            {
                TownSeedDef d = TownData.Seeds[i];
                int have = s.Warehouse[i];
                var row = new Rect(hx, rowY, box.width - 52f, 40f);
                if ((i & 1) == 0) Fill(row, new Color(1f, 0.98f, 0.92f, 0.55f));
                Fill(new Rect(row.x + 12f, row.y + 9f, 24f, 24f), TownData.TintAt(i));
                Frame(new Rect(row.x + 12f, row.y + 9f, 24f, 24f), InkSoft, 1.5f);

                Text(new Rect(row.x + 58f, row.y, 160f, 40f), d.name, 16, Ink, TextAnchor.MiddleLeft, true);
                Text(new Rect(row.x + 226f, row.y, 90f, 40f), d.grade + " / " + d.level + " 级", 15, InkSoft);
                Text(new Rect(row.x + 336f, row.y, 96f, 40f), d.GrowSeconds.ToString("0") + " 秒", 15, InkSoft);
                Text(new Rect(row.x + 444f, row.y, 110f, 40f), d.GoldPerSecond.ToString("0.0"), 15, Good, TextAnchor.MiddleLeft, true);
                Text(new Rect(row.x + 566f, row.y, 110f, 40f), have.ToString(), 16,
                    have > 0 ? Ink : Bad, TextAnchor.MiddleLeft, true);
                rowY += 42f;
            }

            // ★ 这一行是**动态拼接**（合计 / BOSS 次数 / 累计掉落，位数会涨）⇒ 量过再画，别压到"关闭"按钮上
            TextFit(new Rect(box.x + 26f, box.y + box.height - 50f, box.width - 200f, 36f),
                $"仓库合计 {s.WarehouseTotal} 颗｜BOSS 已击败 {TownDrops.BossKills} 次，累计掉落 {TownDrops.DroppedTotal} 颗",
                14, InkSoft);

            bool close = Button(new Rect(box.x + box.width - 140f, box.y + box.height - 56f, 116f, 40f), "关闭 (E)");
            // 面板外点一下也关（避免鼠标没路可走）
            if (Event.current.type == EventType.MouseDown)
            {
                Vector2 mp = Event.current.mousePosition;
                if (!S(box).Contains(mp)) close = true;
            }
            return close;
        }

        // ══════════════════════════════════════════════════════════════════
        // 面板：田里选种 / 换种
        // ══════════════════════════════════════════════════════════════════

        /// <summary>选种面板的返回值。</summary>
        public const int PickNone = -1;
        public const int PickClear = -2;
        public const int PickClose = -3;        /// <summary>★ 收获：收走这块成熟的地，作物离开田位、那颗种子回仓库（`TownState.Harvest`）★</summary>
        public const int PickHarvest = -4;
        /// <summary>★ v252：难度面板里的"开始战斗"（唯一会调 `GoBattle()` 的面板返回值）。</summary>
        public const int PickBattle = -5;
        /// <summary>
        /// ★ v258：难度面板里的「训练场（自由练习）」。
        /// 由 `TownBootstrap.OnGUI` 的 `TownPanel.Difficulty` 分支接住 ⇒ `GoTraining()` ⇒ `LoadScene("Training")`。
        /// **它的出现不代表难度枚举多了一档**：训练场不读也不写 `RunDifficulty.Current`（见 `DrawDifficulty` 的注释）。
        /// </summary>
        public const int PickTraining = -6;

        /// <summary>
        /// 站在耕地上按 E 打开的面板。
        /// 返回：`>= 0` = 选中该种子（种下 / 换种）；<see cref="PickClear"/> = 铲除；<see cref="PickClose"/> = 关闭；<see cref="PickNone"/> = 没操作。
        /// **只有仓库里有货的种子才出现** —— 这就是用户说的"自动识别仓库中拥有的种子进行选择种植"。
        /// 仓库全空时（这一版的开局就是）整张面板会带一行红字指向"打 BOSS"。
        /// </summary>
        public static int DrawSeedPicker(TownState s, int plot, Vector2 plotCenter)
        {
            Rect box = Centered(760f, 620f);
            PanelBox(box);

            int cur = s.PlotSeed[plot];
            bool curMature = cur >= 0 && s.PlotMature[plot];
            string title = cur < 0 ? "种下种子" : (curMature ? "收获 / 更换种植物" : "更换种植物");
            Text(new Rect(box.x + 24f, box.y + 14f, 500f, 40f), title, 28, Ink, TextAnchor.MiddleLeft, true);
            Text(new Rect(box.x + 24f, box.y + 50f, 700f, 24f),
                $"第 {plot + 1} 号耕地（{plotCenter.x:0.0}, {plotCenter.y:0.0}）" +
                (cur < 0 ? " · 空地" : $" · 现在种的是 {TownData.SeedAt(cur)?.name}"), 14, InkSoft);

            if (!s.HasAnySeed())
            {
                Fill(new Rect(box.x + 24f, box.y + 76f, box.width - 48f, 32f), new Color(0.86f, 0.42f, 0.36f, 0.22f));
                Frame(new Rect(box.x + 24f, box.y + 76f, box.width - 48f, 32f), Bad, 2f);
                Text(new Rect(box.x + 34f, box.y + 76f, box.width - 68f, 32f),
                    "仓库里一颗种子都没有 —— 回战斗里击败 BOSS 才能掉种子（商店不卖）。",
                    15, Bad, TextAnchor.MiddleLeft, true);
            }

            float rowY = box.y + 114f;
            int clicked = PickNone;
            for (int i = 0; i < TownData.Seeds.Length; i++)
            {
                TownSeedDef d = TownData.Seeds[i];
                int have = s.Warehouse[i];
                bool isCurrent = cur == i;
                bool usable = have > 0 && !isCurrent;
                var row = new Rect(box.x + 26f, rowY, box.width - 52f, 44f);
                Fill(row, isCurrent ? new Color(0.98f, 0.90f, 0.70f, 1f) : new Color(1f, 0.98f, 0.92f, 0.55f));
                Fill(new Rect(row.x + 10f, row.y + 10f, 24f, 24f), TownData.TintAt(i));
                Frame(new Rect(row.x + 10f, row.y + 10f, 24f, 24f), InkSoft, 1.5f);
                // ★ 名字列只有 130、说明列 250 ⇒ 本地化后的种子名/单位说明都会超 ⇒ 量过再画（防重叠）
                TextFit(new Rect(row.x + 46f, row.y, 130f, 44f), d.name, 16, Ink, TextAnchor.MiddleLeft, true);
                TextFit(new Rect(row.x + 180f, row.y, 250f, 44f),
                    $"{d.grade}/{d.level}级 · 成熟 {d.GrowSeconds:0}秒 · {d.GoldPerSecond:0.0}金/秒", 13, InkSoft);
                Text(new Rect(row.x + 432f, row.y, 80f, 44f), "×" + have, 15,
                    have > 0 ? Good : Bad, TextAnchor.MiddleLeft, true);
                if (isCurrent)
                {
                    // ★ 成熟 ⇒ 这一行给一个「收获」按钮（收走作物 + 种子回仓库，走 `TownState.Harvest`）★
                    //   没成熟时仍然只有"正在生长" —— 没长成不能收（`Harvest` 自己也会拒）。
                    if (curMature)
                    {
                        if (Button(new Rect(row.x + 500f, row.y + 5f, 116f, 34f), "收获", true, 14))
                            clicked = PickHarvest;
                    }
                    else
                    {
                        Text(new Rect(row.x + 516f, row.y, 90f, 44f), "正在生长", 14, Wood, TextAnchor.MiddleLeft, true);
                    }
                }
                else if (Button(new Rect(row.x + 512f, row.y + 5f, 104f, 34f), have > 0 ? "种下" : "无货", usable, 14))
                    clicked = i;
                rowY += 46f;
            }

            float footY = box.y + box.height - 58f;
            if (cur >= 0)
            {
                // 成熟时把"收获"也放到页脚，跟"铲除"并排 —— 玩家不用先去种子行里找 ✓
                if (curMature && Button(new Rect(box.x + 166f, footY, 130f, 42f), "收获（种子回仓）"))
                    clicked = PickHarvest;
                if (Button(new Rect(box.x + 26f, footY, 130f, 42f), "铲除作物")) clicked = PickClear;
            }
            if (Button(new Rect(box.x + box.width - 260f, footY, 116f, 42f), "关闭 (E)")) clicked = PickClose;
            if (Event.current.type == EventType.MouseDown && !S(box).Contains(Event.current.mousePosition))
                clicked = PickClose;
            return clicked;
        }

        // ══════════════════════════════════════════════════════════════════
        // 面板：杂货商店（★ 解锁 道具/插件/植物 ＋ 收种子 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>杂货商店的三个**解锁页** + 一个**回收页**。</summary>
        public enum ShopPage
        {
            Plants = 0,
            Items = 1,
            Plugins = 2,
            Recycle = 3,
        }

        /// <summary>商店当前停在哪一页。**静态**：关掉面板再打开还是上次那一页。</summary>
        public static ShopPage ShopTab = ShopPage.Items;

        /// <summary>最近一次「收种子」的一行说明（面板就地显示 + `TownStatus` 带出去）。</summary>
        public static string LastSellLine = "(还没卖过种子)";

        /// <summary>
        /// 杂货商店。返回 <see cref="PickClose"/> = 关掉，<see cref="PickNone"/> = 没操作。
        ///
        /// ★ 这一版商店的职责（用户原话）★
        ///   「商店卖的是**道具、插件、植物**，买了之后**才会在战斗刷新这些**」
        ///   「商店可以让玩家把**不要的主城种子换金币**」
        /// ⇒ 三个解锁页（植物 / 道具 / 插件）＋ 一个回收页（收种子）。
        ///   **一个种子的"卖"在这里，但"买"不在** —— 种子只能打 BOSS 掉（§⑥）✓
        ///
        /// ★ 成交就地完成（`TownUnlocks.TryUnlock` / `TownState.SellSeed`），不通过返回值交给宿主 ★
        /// 与服装店同一理由：返回值只有"关 / 不关"两种语义，把它当"买了第几条"用会让宿主去猜
        /// 这张表是种子表还是商品表 —— 那正是上一版的错 ✗
        /// </summary>
        public static int DrawShop(TownState s)
        {
            Rect box = Centered(940f, 660f);
            PanelBox(box);
            Text(new Rect(box.x + 24f, box.y + 12f, 420f, 40f), "杂货商店", 28, Ink, TextAnchor.MiddleLeft, true);
            // ★ 说明文字在本地化后会变长 ⇒ 量过再画（原来 14 号直接塞进一行的宽度里，会出框）
            TextFit(new Rect(box.x + 24f, box.y + 48f, box.width - 48f, 24f),
                "买下来 = 在战斗里「解锁」：之后战斗刷新才会出现它。本店【不卖种子】（种子只能打 BOSS 掉），但【收】主城种子。",
                14, Wood, TextAnchor.MiddleLeft, true);

            // ── 四个页签 ──
            float tabY = box.y + 78f;
            Tab(box.x + 24f, tabY, "植物", ShopTab == ShopPage.Plants, () => ShopTab = ShopPage.Plants);
            Tab(box.x + 152f, tabY, "道具", ShopTab == ShopPage.Items, () => ShopTab = ShopPage.Items);
            Tab(box.x + 280f, tabY, "插件", ShopTab == ShopPage.Plugins, () => ShopTab = ShopPage.Plugins);
            Tab(box.x + 408f, tabY, "收种子", ShopTab == ShopPage.Recycle, () => ShopTab = ShopPage.Recycle);

            int clicked = PickNone;
            // ★ v217：正文进了滚动区 ⇒ 视口 = 页签下沿 ~ 页脚上沿。
            //   页脚那三行（最近一次成交 / 现有金币 / 关闭）**留在区外**，坐标与改前逐像素相同 ✓
            Rect body = new Rect(box.x + 24f, box.y + 150f, box.width - 48f, box.height - 262f);
            if (!TownUnlocks.Ready && !TownUnlocks.Load())
            {
                Text(new Rect(box.x + 24f, box.y + 140f, box.width - 48f, 60f),
                    "解锁表载入失败：" + (TownUnlocks.LastError ?? "(未知)"), 16, Bad);
            }
            else if (ShopTab == ShopPage.Recycle)
            {
                DrawRecyclePage(s, box, body, ref ScrollShopPage[(int)ShopPage.Recycle]);
            }
            else
            {
                int ti = (int)ShopTab;
                DrawUnlockPage(s, box, body, KindOfPage(ShopTab), ref ScrollShopPage[ti]);
            }

            // 最近一次成交 / 失败
            float infoY = box.y + box.height - 100f;
            // ★ 成交回执里带种子名 + 两个金币数字（长度随数据变）⇒ 量过再画，别顶到右边
            TextFit(new Rect(box.x + 24f, infoY, box.width - 48f, 22f),
                ShopTab == ShopPage.Recycle ? LastSellLine : TownUnlocks.LastLine, 13, InkSoft);
            if (!string.IsNullOrEmpty(TownUnlocks.LastError) && ShopTab != ShopPage.Recycle)
                Text(new Rect(box.x + 24f, infoY + 22f, box.width - 48f, 22f), "✗ " + TownUnlocks.LastError,
                    13, Bad, TextAnchor.MiddleLeft, true);

            float footY = box.y + box.height - 56f;
            // ★ v252：这一行是**两段拼接**（"现有金币：N · 已解锁 X / Y 件上架"）⇒ 英文/日文界面里
            //   它一直是中文（截图 `v252-shop-grow-en` 底部就是证据）。改成一个具名键 + 两个参数 ✓
            // ★ 这一行是具名键 + 三个动态数字（现有金币 / 已解锁 / 上架）⇒ 量过再画：
            //   英文/日文模板更长、金币位数也会涨，原来会顶到右边"关闭"按钮上（重叠）✗
            TextFit(new Rect(box.x + 24f, footY, 560f, 40f),
                L10n.T("shop.footer", s.Coins.ToString("N0"), TownUnlocks.OwnedCount.ToString(),
                       TownUnlocks.OnSaleCount.ToString()), 17, Ink, TextAnchor.MiddleLeft, true);
            if (Button(new Rect(box.x + box.width - 140f, footY, 116f, 40f), "关闭 (E)")) clicked = PickClose;
            if (Event.current.type == EventType.MouseDown && !S(box).Contains(Event.current.mousePosition))
                clicked = PickClose;
            return clicked;
        }

        /// <summary>页签按钮（选中的画灰 = "你已经在这里"）。</summary>
        private static void Tab(float x, float y, string label, bool active, System.Action onClick)
        {
            if (Button(new Rect(x, y, 118f, 36f), label, !active, 16)) onClick();
        }

        private static string KindOfPage(ShopPage p)
            => p == ShopPage.Plants ? TownUnlockDef.KindPlant
             : p == ShopPage.Plugins ? TownUnlockDef.KindPlugin
             : TownUnlockDef.KindItem;

        /// <summary>
        /// 一个解锁页：**列出这一类登记过的每一条**（上架的能买，未上架的画成灰行、不藏起来）。
        ///
        /// ★ v237：为什么从"只列上架"改成"全列 + 两行" ★
        ///   改前植物页只有 4 条上架 ⇒ 内容高 = 6 + 4×38 + 34 = **192**，而视口有 **398**
        ///   ⇒ 可滚余量 **0** ⇒ 用户报的"滑动器其实无效，无法实际更新界面"就是这么来的
        ///   （条画出来了，但一格都滚不动）。只把行数算进来是不够的，行高也得装得下真内容 ✓
        ///   ⇒ 每行 60：第一行名称/状态/价格/按钮，第二行是**这条东西到底是什么**（取自战斗三张表，
        ///     与图鉴同一份 `TownCodex.DetailOf`，不在这里另写一份文案）✓
        /// 表头（名称 / 买下之后 / 价格）留在**区外**：滚列表时列名一直看得见 ✓
        /// </summary>
        private static void DrawUnlockPage(TownState s, Rect box, Rect view, string kind, ref float scroll)
        {
            float hx = box.x + 24f;

            // ── 表头（区外）──
            float hy = box.y + 124f;
            Text(new Rect(hx + 40f, hy, 260f, 22f), "名称", 13, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 330f, hy, 300f, 22f), "买下之后", 13, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 660f, hy, 90f, 22f), "价格", 13, InkSoft, TextAnchor.MiddleLeft, true);
            Fill(new Rect(hx, hy + 22f, box.width - 48f, 2f), CreamDark);

            // ── 先数一遍（`contentH` 必须在开滚动区**之前**就知道：条的长短由它定）──
            int onSale = 0, rows = 0;
            for (int i = 0; i < TownUnlocks.Count; i++)
            {
                TownUnlockDef d0 = TownUnlocks.At(i);
                if (d0 == null || d0.kind != kind) continue;
                if (d0.onSale) onSale++;
                // ★ v284：买过的**下架**（只留在图鉴里）★ 用户原话：「购买后下架并加入到图鉴」
                if (TownUnlocks.Owned(i)) continue;
                rows++;
            }
            if (rows == 0)
            {
                // ★ v284：分两种 —— 「一条都没登记」和「这一页已经全买完了」★
                //   复用下面那句「还没有任何登记条目」会让买空之后显示成配表出错，是假话。
                {
                    int total = 0;
                    for (int i = 0; i < TownUnlocks.Count; i++)
                    {
                        TownUnlockDef dz = TownUnlocks.At(i);
                        if (dz != null && dz.kind == kind) total++;
                    }
                    if (total > 0)
                    {
                        Text(new Rect(hx, view.y + 16f, view.width, 30f),
                            "这一类 " + total + " 件已经全部解锁 —— 全部转到「图鉴」里去了，这里不再重复列出。",
                            15, InkSoft, TextAnchor.MiddleLeft, true);
                        ReportScroll("商店/" + kind, box, view, view.height + 1f, view.height * 0.5f, 0f);
                        return;
                    }
                }
                // 这一类一条都没登记 ⇒ 也要给一句真话，而不是留一块空白（空白的 contentH 会小于视口 ⇒ 又变回假条）
                Text(new Rect(hx, view.y + 16f, view.width, 30f),
                    "这一类还没有任何登记条目（town_unlocks.json 里没有 kind = " + kind + " 的行）。", 15, Bad,
                    TextAnchor.MiddleLeft, true);
                ReportScroll("商店/" + kind, box, view, view.height + 1f, view.height * 0.5f, 0f);
                return;
            }

            const float pitch = 60f;                                 // 两行：标题行 34 + 说明行 22 + 4 间隙
            const float padTop = 6f;
            const float noteH = 34f;                                 // 区内最后那句"共 N 条 / 上架 M 条"
            float top = view.y + padTop;
            float contentH = padTop + rows * pitch + noteH;
            scroll = ScrollBegin(view, scroll, contentH);

            float rowY = top;
            int zebra = 0;
            for (int i = 0; i < TownUnlocks.Count; i++)
            {
                TownUnlockDef d = TownUnlocks.At(i);
                if (d == null || d.kind != kind) continue;
                // ★ v284：与上面那次 prepass 同一条规则 —— 买过的下架 ★
                if (TownUnlocks.Owned(i)) continue;

                bool owned = TownUnlocks.Owned(i);
                bool afford = s.Coins >= d.price;
                var row = new Rect(hx, rowY, box.width - 48f, pitch - 4f);
                if (owned) Fill(row, new Color(0.90f, 0.94f, 0.88f, 0.95f));
                else if ((zebra & 1) == 0) Fill(row, new Color(1f, 0.98f, 0.92f, 0.55f));
                if (!d.onSale) Frame(row, CreamDark, 1.5f);          // 未上架：只描一圈浅框，读起来是"灰的"

                Fill(new Rect(row.x + 8f, row.y + 7f, 20f, 22f), d.Tint);
                TextFit(new Rect(row.x + 40f, row.y, 260f, 34f), LocalizedSource(d.name), 16, Ink, TextAnchor.MiddleLeft, true);
                Text(new Rect(row.x + 330f, row.y, 320f, 34f),
                    owned ? "已解锁 · 战斗刷新池里已有它"
                          : d.onSale ? "战斗刷新池里加上它（永久）" : "暂未上架（表里有、还没开卖）", 13,
                    owned ? Good : (d.onSale ? InkSoft : Bad));
                Text(new Rect(row.x + 660f, row.y, 90f, 34f), d.onSale ? d.price + " 金" : "—", 15,
                    owned ? InkSoft : (afford && d.onSale ? Gold : InkSoft), TextAnchor.MiddleLeft, true);

                // ── 第二行：这件东西到底是什么（战斗三张表的原文，截断到一行）──
                Text(new Rect(row.x + 40f, row.y + 34f, row.width - 60f, 20f),
                    ShortDetail(d), 12, InkSoft, TextAnchor.MiddleLeft);

                var btn = new Rect(row.x + 760f, row.y + 3f, 100f, 30f);
                if (owned)
                {
                    Text(new Rect(row.x + 760f, row.y, 100f, 34f), "已解锁", 14, Good, TextAnchor.MiddleCenter, true);
                }
                else if (!d.onSale)
                {
                    Text(new Rect(row.x + 760f, row.y, 100f, 34f), "未上架", 14, InkSoft, TextAnchor.MiddleCenter, true);
                }
                else if (Button(btn, afford ? "解锁" : "金币不足", afford, 14))
                {
                    TownUnlocks.TryUnlock(i);
                }
                rowY += pitch;
                zebra++;
            }

            Text(new Rect(hx, top + rows * pitch + 6f, view.width, 24f),
                $"本页共 {rows} 条（上架 {onSale} 条）" +
                (rows > onSale ? $"　·　另 {rows - onSale} 条暂未上架：能看见，但还不能买" : ""),
                13, InkSoft);

            ScrollEnd();
            ReportScroll("商店/" + kind, box, view, contentH, padTop + rows * pitch, scroll);
        }

        /// <summary>一行的"这是什么"：取 `TownCodex` 那三张表的原文，压成一行（超长截断加省略号）。</summary>
        /// <summary>
        /// ★★ v252：把一段**中文原文**翻成当前语言，**并把原文里的数字回填进去** ★★
        ///
        /// ── 为什么是 `S()` 而不是 `T()`（这一条踩了两次，写清楚）──
        ///   · `T(key)` 只返回**译文模板**：表里存的是 `"…近战伤害 +§（§⇒§）…"`，
        ///     `T` 不填数 ⇒ 屏幕上一串 `§`（截图 `v252-shop-ja3` 就是证据）✗
        ///   · `S(原文)` 才会先做"形状归一 + 数字回填" ⇒ 译文里的 § 被换成原文的数字 ✓
        ///   · 而 `S()` 必须拿**未截断的原文**去查（形状要一模一样）——
        ///     所以这里的顺序是"先 `S`、后截断"，不是反过来 ✓
        /// </summary>
        private static string LocalizedSource(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            string s = L10n.S(source);
            return string.IsNullOrEmpty(s) ? source : s;
        }

        private static string ShortDetail(TownUnlockDef d)
        {
            string text = TownCodex.DetailOf(d == null ? null : d.id);
            if (string.IsNullOrEmpty(text)) return "（战斗表里还没有这一条的说明）";
            // ★ v252：先翻译（含数字回填），再截断 ✓
            text = LocalizedSource(text);
            text = text.Replace('\n', ' ');
            const int Max = 78;
            return text.Length <= Max ? text : text.Substring(0, Max) + "…";
        }

        /// <summary>
        /// 回收页：把仓库里的主城种子换成主城金币（回收价来自 `town_seeds.json` 的 `sellPrice`）。
        /// ★ v217：22 种种子 × 42px = 924px，一屏只有 ~398 ⇒ 改前**画到面板外 400 多像素**（越界）★
        /// </summary>
        private static void DrawRecyclePage(TownState s, Rect box, Rect view, ref float scroll)
        {
            float hx = box.x + 24f;

            // ── 表头（区外）──
            float hy = box.y + 124f;
            Text(new Rect(hx + 44f, hy, 200f, 22f), "种子", 13, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 250f, hy, 120f, 22f), "仓库数量", 13, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 400f, hy, 130f, 22f), "回收价/颗", 13, InkSoft, TextAnchor.MiddleLeft, true);
            Text(new Rect(hx + 560f, hy, 280f, 22f), "操作", 13, InkSoft, TextAnchor.MiddleLeft, true);
            Fill(new Rect(hx, hy + 22f, box.width - 48f, 2f), CreamDark);

            int n = TownData.Seeds.Length;
            bool anySeed = false;
            for (int i = 0; i < n; i++) if (s.Warehouse[i] > 0) { anySeed = true; break; }

            const float pitch = 42f;
            const float padTop = 6f;
            float top = view.y + padTop;
            float contentH = padTop + n * pitch + (anySeed ? 44f : 74f);
            scroll = ScrollBegin(view, scroll, contentH);

            float rowY = top;
            for (int i = 0; i < TownData.Seeds.Length; i++)
            {
                TownSeedDef d = TownData.Seeds[i];
                int have = s.Warehouse[i];

                var row = new Rect(hx, rowY, box.width - 48f, 40f);
                if ((i & 1) == 0) Fill(row, new Color(1f, 0.98f, 0.92f, 0.55f));
                Fill(new Rect(row.x + 12f, row.y + 9f, 22f, 22f), TownData.TintAt(i));
                Frame(new Rect(row.x + 12f, row.y + 9f, 22f, 22f), InkSoft, 1.5f);

                Text(new Rect(row.x + 44f, row.y, 200f, 40f), d.name, 16, Ink, TextAnchor.MiddleLeft, true);
                Text(new Rect(row.x + 250f, row.y, 120f, 40f), have + " 颗", 15,
                    have > 0 ? Ink : Bad, TextAnchor.MiddleLeft, true);
                Text(new Rect(row.x + 400f, row.y, 130f, 40f), d.SellPrice + " 金", 15, Gold,
                    TextAnchor.MiddleLeft, true);

                bool canSell = have > 0;
                if (Button(new Rect(row.x + 556f, row.y + 5f, 84f, 30f), "卖 1 颗", canSell, 13))
                    DoSell(i, 1);
                if (Button(new Rect(row.x + 648f, row.y + 5f, 96f, 30f), "全卖", canSell, 13))
                    DoSell(i, have);
                if (Button(new Rect(row.x + 752f, row.y + 5f, 108f, 30f), "卖 10 颗", canSell && have >= 10, 13))
                    DoSell(i, 10);
                rowY += pitch;
            }

            float noteY = top + n * pitch + 6f;
            if (!anySeed)
            {
                Text(new Rect(hx, noteY, view.width, 30f),
                    "仓库里一颗种子都没有 —— 种子只能靠打 BOSS 掉落（北面传送门回战斗）。", 15, Bad,
                    TextAnchor.MiddleLeft, true);
                noteY += 36f;
            }
            Text(new Rect(hx, noteY, view.width, 22f),
                "回收价写在 town_seeds.json 的 sellPrice 里（原卖价的 30~50%）；卖掉不影响田里正在长的作物。",
                13, InkSoft);

            ScrollEnd();
            ReportScroll("商店/收种子", box, view, contentH, padTop + n * pitch, scroll);
        }

        /// <summary>真的卖一次（走 `TownState.SellSeed`：扣种子 + 加金币 + 重算一步到位）。</summary>
        private static void DoSell(int seedIndex, int count)
        {
            TownState s = TownState.Ensure();
            int before = s.Warehouse[seedIndex];
            long gain = s.SellSeed(seedIndex, count);
            if (gain <= 0)
            {
                LastSellLine = $"{TownData.SeedAt(seedIndex)?.name} 没卖出去（仓库只有 {before} 颗）";
                return;
            }
            LastSellLine = $"卖掉 {TownData.SeedAt(seedIndex)?.name} ×{(before - s.Warehouse[seedIndex])} ⇒ +{gain} 金" +
                           $"（仓库剩 {s.Warehouse[seedIndex]} 颗 · 现有金币 {s.Coins}）";
            Log.Info("[Town] " + LastSellLine);
        }

        // ══════════════════════════════════════════════════════════════════
        // 面板：服装店（★ 只有皮肤 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 服装店：**只有皮肤**（买 / 穿 / 卸就地生效）。返回语义同 <see cref="DrawShop"/>。
        /// 这是从上一版"商业街 = 种子铺 + 皮肤衣柜 两个页签"里**拆出来**的那一半（§④ §⑤）：
        /// 现在它在自己的一栋塔楼里、有自己的锚点、自己的面板 —— 与商店没有任何共用行 ✓
        /// </summary>
        public static int DrawClothing(TownState s)
        {
            Rect box = Centered(880f, 620f);
            PanelBox(box);
            Text(new Rect(box.x + 24f, box.y + 14f, 480f, 40f), "服装店 · 皮肤衣柜", 28, Ink, TextAnchor.MiddleLeft, true);
            Text(new Rect(box.x + 24f, box.y + 52f, 830f, 24f),
                "皮肤是加在角色骨骼上的**真模型** —— 买来「穿上」，立刻就长在身上（不用重开场景）。本店只卖皮肤。",
                14, InkSoft);

            int clicked = PickNone;
            if (!TownSkins.Ready && !TownSkins.TryLoad())
            {
                Text(new Rect(box.x + 26f, box.y + 130f, 800f, 60f),
                    "皮肤表载入失败：" + (TownSkins.LastError ?? "(未知)"), 16, Bad);
            }
            else
            {
                // ★ v217：衣柜名单进滚动区。视口 = 副标题下面 ~ "全部卸下 / 已买 N 件"那行上面；
                //   那一行与页脚（现有金币 / 关闭）**留在区外**，坐标与改前逐像素相同 ✓
                // ★ v237：行高 48 → 60（名称一行 + 槽位/骨骼一行），并在**区内**末尾加一句说明 ——
                //   改前 8 件 × 48 = 392 < 视口 412 ⇒ 可滚余量 0 ⇒"滑动器无效"（与商店植物页同一个病）✓
                Rect view = new Rect(box.x + 24f, box.y + 86f, box.width - 48f, box.height - 86f - 122f);
                const float pitch = 60f;
                const float padTop = 2f;
                const float noteH = 30f;
                float top = view.y + padTop;
                float contentH = padTop + TownSkins.Items.Length * pitch + noteH + 6f;
                _scrollClothing = ScrollBegin(view, _scrollClothing, contentH);

                float rowY = top;
                for (int i = 0; i < TownSkins.Items.Length; i++)
                {
                    TownSkinDef d = TownSkins.Items[i];
                    bool owned = TownSkins.Owned(i);
                    bool worn = TownSkins.IsEquipped(i);
                    bool afford = s.Coins >= d.price;

                    var row = new Rect(box.x + 26f, rowY, box.width - 52f, 56f);
                    if (worn) Fill(row, new Color(0.99f, 0.90f, 0.68f, 0.95f));
                    else if ((i & 1) == 0) Fill(row, new Color(1f, 0.98f, 0.92f, 0.55f));

                    Fill(new Rect(row.x + 10f, row.y + 16f, 24f, 24f), d.Tint);
                    Frame(new Rect(row.x + 10f, row.y + 16f, 24f, 24f), InkSoft, 1.5f);

                    // ★ 皮肤名 + 槽位/骨骼说明都是动态串，原来直接塞进 220/260 ⇒ 量过再画
                    TextFit(new Rect(row.x + 46f, row.y + 2f, 220f, 30f), d.name, 16, Ink, TextAnchor.MiddleLeft, true);
                    TextFit(new Rect(row.x + 46f, row.y + 30f, 260f, 22f),
                        TownSkins.SlotName(d.Slot) + " · " + d.bones, 12, InkSoft);

                    string state = worn ? "穿戴中" : owned ? "已买" : d.price + " 金";
                    Color stateCol = worn ? Good : owned ? InkSoft : (afford ? Gold : Bad);
                    Text(new Rect(row.x + 510f, row.y, 110f, 56f), state, 16, stateCol, TextAnchor.MiddleLeft, true);

                    var btn = new Rect(row.x + 640f, row.y + 11f, 110f, 34f);
                    if (worn)
                    {
                        if (Button(btn, "卸下", true, 14)) TownSkins.Unequip(d.Slot);
                    }
                    else if (owned)
                    {
                        if (Button(btn, "穿上", true, 14)) TownSkins.Equip(i);
                    }
                    else
                    {
                        if (Button(btn, afford ? "购买" : "金币不足", afford, 14)) TownSkins.TryBuy(i);
                    }
                    rowY += pitch;
                }

                // 区内末尾的一句说明（不是装饰：它把"为什么滚"讲清楚，同时让 contentH 装着真内容）
                Text(new Rect(box.x + 26f, top + TownSkins.Items.Length * pitch + 4f, view.width - 8f, 24f),
                    "皮肤只改外观：不涨数值、不影响战斗。价格写在 town_skins.json 的 price 里。", 13, InkSoft);

                ScrollEnd();
                ReportScroll("服装店", box, view, contentH,
                             padTop + TownSkins.Items.Length * pitch, _scrollClothing);

                float opY = box.y + box.height - 106f;
                if (TownSkins.EquippedCount > 0)
                {
                    if (Button(new Rect(box.x + 26f, opY, 130f, 36f), "全部卸下", true, 14))
                        TownSkins.UnequipAll();
                }
                Text(new Rect(box.x + 170f, opY, box.width - 360f, 36f),
                    "已买 " + TownSkins.OwnedCount + " / " + TownSkins.Items.Length, 15, InkSoft);
            }

            float footY = box.y + box.height - 58f;
            // ★ 金币位数会涨 ⇒ 量过再画（右边 140 处是"关闭"按钮，写死 460 迟早顶上去）
            TextFit(new Rect(box.x + 26f, footY, box.width - 200f, 42f), "现有金币：" + s.Coins.ToString("N0"), 18, Ink,
                TextAnchor.MiddleLeft, true);
            if (Button(new Rect(box.x + box.width - 140f, footY, 116f, 42f), "关闭 (E)")) clicked = PickClose;
            if (Event.current.type == EventType.MouseDown && !S(box).Contains(Event.current.mousePosition))
                clicked = PickClose;
            return clicked;
        }

        // ══════════════════════════════════════════════════════════════════
        // 面板：家 → 图鉴（★ v216 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>图鉴当前停在哪一页（静态：关掉再开还是上次那一页）。</summary>
        public static TownCodex.Page CodexTab = TownCodex.Page.Items;

        /// <summary>
        /// 图鉴面板：**植物 / 道具 / 插件** 三页（只列**已解锁**的，每条给完整介绍）
        /// ＋ **成就**一页（先留空，写"敬请期待"）。
        /// 返回 <see cref="PickClose"/> = 关掉，<see cref="PickNone"/> = 没操作。
        ///
        /// ★ v217：**翻页去掉了，改成一页到底滚动**（用户："图鉴增加滑动条，内容不越界"）★
        ///   为什么删而不是"翻页＋滚动"并存：每页 4 条时内容只有 384px、视口有 ~390px
        ///   ⇒ 那根条**一步都滚不动**，等于挂了一根假条 ✗ 二选一，选滚动：
        ///   道具 56 条本来要翻 14 页（点 14 次），滚动是它唯一合理的读法 ✓
        ///   `TownCodex.PageSize / PageCount / TryGet` 保留未动（`TownBootstrap.CodexInfo` 仍在读，
        ///   删了会连累那条调试命令），只是 HUD 不再用它们。
        /// </summary>
        public static int DrawCodex(TownState s)
        {
            // ⚠️ 不用 `Centered`：v216 的左上角面板只有 100 高（18~118），
            //    图鉴如果垂直居中（y=110）会压住它的第三行 —— 所以图鉴**下移**到 128 起 ✓
            // ★ v217：高度再按屏幕钳一次 —— 窗口很矮时（`_vh` 被拉到 788 以下）原来固定的 660
            //   会让面板下沿出屏；钳到 `_vh - 148` 之后面板**自己**先不越界 ✓
            Rect box = new Rect((_vw - 960f) * 0.5f, 128f, 960f, Mathf.Clamp(_vh - 148f, 480f, 660f));
            PanelBox(box);

            Text(new Rect(box.x + 24f, box.y + 12f, 420f, 40f), "图鉴 · 家", 28, Ink, TextAnchor.MiddleLeft, true);
            Text(new Rect(box.x + 24f, box.y + 48f, box.width - 48f, 22f),
                "只收录【已经解锁】的条目：老货一开始就有，新货要在杂货商店买下来。", 14, Wood,
                TextAnchor.MiddleLeft, true);

            // ── 四个页签 ──
            float tabY = box.y + 76f;
            CodexTabButton(box.x + 24f, tabY, "植物", TownCodex.Page.Plants);
            CodexTabButton(box.x + 152f, tabY, "道具", TownCodex.Page.Items);
            CodexTabButton(box.x + 280f, tabY, "插件", TownCodex.Page.Plugins);
            CodexTabButton(box.x + 408f, tabY, "成就", TownCodex.Page.Achievements);

            int clicked = PickNone;
            TownCodex.Page page = CodexTab;
            int tabIndex = (int)page;

            if (page == TownCodex.Page.Achievements)
            {
                // ★ 用户："还有'成就'，先留空" ★
                var panel = new Rect(box.x + 24f, box.y + 128f, box.width - 48f, box.height - 260f);
                Fill(panel, new Color(1f, 0.98f, 0.92f, 0.55f));
                Frame(panel, InkSoft, 1.5f);
                Text(panel, "成　就", 30, InkSoft, TextAnchor.MiddleCenter, true);
                Text(new Rect(panel.x, panel.y + panel.height * 0.5f, panel.width, 34f),
                    "敬请期待", 22, Wood, TextAnchor.MiddleCenter, true);
                Text(new Rect(panel.x, panel.y + panel.height * 0.5f + 40f, panel.width, 26f),
                    "（这一页按用户要求先留空，等成就系统落地再填）", 14, InkSoft, TextAnchor.MiddleCenter);
            }
            else
            {
                float hx = box.x + 24f;
                float hy = box.y + 122f;

                // 三条列表都直接读 `TownCodex` 那三个公开数组（不再走分页游标）
                TownCodex.Entry[] list = page == TownCodex.Page.Plants ? TownCodex.Plants
                                      : page == TownCodex.Page.Plugins ? TownCodex.Plugins
                                      : TownCodex.Items;

                Text(new Rect(hx, hy, box.width - 48f, 22f),
                    $"共 {list.Length} 条 · 一页到底：滚轮 / 右侧条可拖 / PageUp·PageDown", 13, InkSoft);

                // ★ 正文视口：表头下面 ~ 降级提示上面（滚动到这里为止，四边都被裁）★
                Rect body = new Rect(hx, box.y + 148f, box.width - 48f, box.height - 148f - 124f);
                const float RowH = 96f;
                const float padTop = 6f;
                float top = body.y + padTop;
                float contentH = padTop + list.Length * RowH + 6f;

                float sc = ScrollBegin(body, ScrollCodexPage[tabIndex], contentH);
                ScrollCodexPage[tabIndex] = sc;

                for (int r = 0; r < list.Length; r++)
                {
                    float rowY = top + r * RowH;
                    // 视口外的条目整条不画：56 条 × 每条一次 `TextWrap`（断行要算字宽）会白烧每帧的排版时间
                    if (rowY + RowH < body.y + sc || rowY > body.y + sc + body.height) continue;

                    TownCodex.Entry e = list[r];
                    var row = new Rect(hx, rowY, box.width - 48f, RowH - 8f);
                    Fill(row, (r & 1) == 0 ? new Color(1f, 0.98f, 0.92f, 0.55f) : new Color(0f, 0f, 0f, 0f));
                    Frame(row, CreamDark, 1.5f);
                    Fill(new Rect(row.x + 10f, row.y + 10f, 26f, 26f),
                         TownUnlocks.IsUnlocked(e.Id) ? Good : InkSoft);
                    TextFit(new Rect(row.x + 46f, row.y + 8f, 320f, 28f), e.Name, 19, Ink, TextAnchor.MiddleLeft, true);
                    Text(new Rect(row.x + 46f, row.y + 34f, 320f, 20f), e.Id, 12, InkSoft);
                    // 完整介绍（自动换行；`Text` 关掉了 wordWrap，所以这里用专用的 `TextWrap`）
                    TextWrap(new Rect(row.x + 380f, row.y + 8f, row.width - 400f, RowH - 24f), e.Detail, 13, Ink);
                }

                if (list.Length == 0)
                    Text(new Rect(hx, top, body.width, 30f),
                        "这一类还没有任何已解锁的条目。", 16, Bad, TextAnchor.MiddleLeft, true);

                ScrollEnd();
                ReportScroll("图鉴/" + page, box, body, contentH, padTop + list.Length * RowH, sc);
            }

            if (!string.IsNullOrEmpty(TownCodex.LastError))
                Text(new Rect(box.x + 24f, box.y + box.height - 118f, box.width - 48f, 20f),
                    "（降级：" + TownCodex.LastError + "）", 12, Wood);

            float footY = box.y + box.height - 58f;
            TextFit(new Rect(box.x + 24f, footY, box.width - 200f, 40f),
                $"金币 {s.Coins:N0}　·　已解锁 {TownUnlocks.OwnedCount}/{TownUnlocks.OnSaleCount} 件上架", 16, Ink,
                TextAnchor.MiddleLeft, true);
            if (Button(new Rect(box.x + box.width - 140f, footY, 116f, 40f), "关闭 (E)")) clicked = PickClose;

            if (Event.current.type == EventType.MouseDown && !S(box).Contains(Event.current.mousePosition))
                clicked = PickClose;
            return clicked;
        }

        private static void CodexTabButton(float x, float y, string label, TownCodex.Page page)
        {
            int n = TownCodex.CountOf(page);
            string text = page == TownCodex.Page.Achievements ? label : label + " " + n;
            if (Button(new Rect(x, y, 118f, 36f), text, CodexTab != page, 15)) CodexTab = page;
        }

        // ══════════════════════════════════════════════════════════════════
        // 2D 小地图（★ 同心圆 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>小地图在世界空间的取景矩形（x/z），由 `TownBootstrap.MapWorldRect` 提供。</summary>
        public static Rect MapWorldRect = new Rect(-15.5f, -15.5f, 31f, 31f);

        /// <summary>世界 (x,z) → 小地图屏幕矩形（虚拟像素）。</summary>
        public static Vector2 WorldToMap(Vector2 world, Rect mapBox)
        {
            Rect w = MapWorldRect;
            float u = Mathf.InverseLerp(w.xMin, w.xMax, world.x);
            float v = Mathf.InverseLerp(w.yMin, w.yMax, world.y);
            return new Vector2(mapBox.x + u * mapBox.width, mapBox.y + (1f - v) * mapBox.height);
        }

        /// <summary>
        /// 右上角的 2D 小地图（**圆形城界 + 放射主街 + 西侧三区**）。
        /// 画法：从大到小依次画"实心圆盘"，后画的盖住外面 ⇒ 中间那几圈自然变成环带；
        /// 矩形区（农田/公园/商业街/放射街）用 `MapRect` 铺上去 ✓
        /// 读数与小地图**同一份常量**（都取 `TownLayout`）⇒ 图上画的圆就是脚下的圆 ✓
        /// </summary>
        public static void DrawMinimap(TownBootstrap boot, TownState s)
        {
            const float Size = 260f;
            var outer = new Rect(_vw - Size - 20f, 18f, Size, Size + 34f);
            PanelBox(outer);
            var map = new Rect(outer.x + 8f, outer.y + 24f, Size - 16f, Size - 34f);
            Text(new Rect(outer.x + 12f, outer.y + 2f, 200f, 22f), "主城地图", 14, Ink, TextAnchor.MiddleLeft, true);

            Fill(map, new Color(0.42f, 0.56f, 0.34f, 1f));   // 城外草地
            Frame(map, Wood, 2f);

            // ── 圆：城界 → 内环 → 广场 → 喷泉 ──
            MapDisc(map, TownLayout.TownRadius, new Color(0.24f, 0.44f, 0.26f, 1f));       // 灌木环以内
            MapDisc(map, TownLayout.GreenBeltOuter, new Color(0.30f, 0.56f, 0.32f, 1f));   // 内区草地
            MapDisc(map, TownLayout.InnerRoadOut, new Color(0.66f, 0.63f, 0.56f, 1f));     // 内环路
            MapDisc(map, TownLayout.InnerRoadIn, new Color(0.30f, 0.56f, 0.32f, 1f));
            MapDisc(map, TownLayout.PlazaRadius, new Color(0.74f, 0.71f, 0.64f, 1f));      // 中央广场
            MapDisc(map, 1.30f, new Color(0.38f, 0.66f, 0.82f, 1f));                       // 喷泉水面

            // ── 路网（v217）：环岛 + 南向进城街 + 右侧商业街 + 短接 ──
            Color roadCol = new Color(0.66f, 0.63f, 0.56f, 1f);
            MapRect(map, new Rect(-TownLayout.StreetHalf, TownLayout.StreetEndSouth, TownLayout.StreetHalf * 2f,
                                  -TownLayout.InnerRoadOut - TownLayout.StreetEndSouth), roadCol);
            MapRect(map, new Rect(TownLayout.ShopStreetXMin, TownLayout.ShopStreetZMin,
                                  TownLayout.ShopStreetXMax - TownLayout.ShopStreetXMin,
                                  TownLayout.ShopStreetZMax - TownLayout.ShopStreetZMin), roadCol);
            MapRect(map, new Rect(TownLayout.InnerRoadOut, -TownLayout.StreetHalf,
                                  TownLayout.ShopStreetXMin - TownLayout.InnerRoadOut,
                                  TownLayout.StreetHalf * 2f), roadCol);

            // ── 西侧三区 ──
            MapRect(map, new Rect(TownLayout.FarmCenter.x - TownLayout.FarmHalf, TownLayout.FarmCenter.y - TownLayout.FarmHalf,
                                  TownLayout.FarmHalf * 2f, TownLayout.FarmHalf * 2f),
                    new Color(0.46f, 0.33f, 0.22f, 1f));                                   // 农田土块
            MapRect(map, new Rect(TownLayout.ParkCenter.x - TownLayout.ParkHalf, TownLayout.ParkCenter.y - TownLayout.ParkHalf,
                                  TownLayout.ParkHalf * 2f, TownLayout.ParkHalf * 2f),
                    new Color(0.22f, 0.52f, 0.28f, 1f));                                   // 公园草坪

            // ── 36 块耕地（★ 6×6 方格 ★）──
            for (int i = 0; i < TownState.PlotCount; i++)
            {
                Vector2 c = TownLayout.PlotCenter(i);
                int si = s.PlotSeed[i];
                Color cell = si < 0 ? new Color(0.58f, 0.42f, 0.29f, 1f) : TownData.TintAt(si);
                MapSquare(map, c, TownLayout.PlotWidth * 0.46f, cell);
            }

            // ── 六座建筑（位置 = `TownLayout` 的 Pos）──
            MapBuilding(map, TownLayout.PortalPos, 3.6f, 1.0f, new Color(0.38f, 0.86f, 1.00f, 1f));      // 传送门
            MapBuilding(map, TownLayout.ClothPos, 1.8f, 1.7f, new Color(0.88f, 0.62f, 0.55f, 1f));       // 服装店
            MapBuilding(map, TownLayout.ShopPos, 2.2f, 2.2f, new Color(0.98f, 0.86f, 0.42f, 1f));        // 商店
            MapBuilding(map, TownLayout.WarePos, 1.7f, 3.1f, new Color(0.62f, 0.44f, 0.28f, 1f));        // 仓库
            MapBuilding(map, TownLayout.TentPos, 2.0f, 2.0f, new Color(0.78f, 0.34f, 0.30f, 1f));        // 帐篷
            MapBuilding(map, TownLayout.HomePos, 2.3f, 2.0f, new Color(0.92f, 0.72f, 0.62f, 1f));        // 家
            MapBuilding(map, TownLayout.GatePos, 2.2f, 0.5f, new Color(0.55f, 0.57f, 0.62f, 1f));        // 南门

            // 传送门特效挂点（美术那路做的黑洞就在这）—— 图上点一个青色小十字
            Vector2 fx = WorldToMap(new Vector2(TownLayout.PortalPos.x,
                                                TownLayout.PortalPos.y - TownLayout.BossPortalFxLocal.z), map);
            Fill(new Rect(fx.x - 4f, fx.y - 1f, 8f, 2f), new Color(0.40f, 0.86f, 1.00f, 1f));
            Fill(new Rect(fx.x - 1f, fx.y - 4f, 2f, 8f), new Color(0.40f, 0.86f, 1.00f, 1f));

            // ── 玩家光点（+ 朝向刻线）──
            Vector2 p = boot.PlayerMapPosition;
            Vector2 mp = WorldToMap(p, map);
            Vector2 f2 = boot.PlayerFacing;
            Vector2 tip = mp + new Vector2(f2.x, -f2.y) * 9f;
            for (int i = 1; i <= 6; i++)
            {
                Vector2 q = Vector2.Lerp(mp, tip, i / 6f);
                Fill(new Rect(q.x - 1.2f, q.y - 1.2f, 2.4f, 2.4f), new Color(1f, 1f, 1f, 0.85f));
            }
            Dot(mp, 3.4f, new Color(1f, 1f, 1f, 1f), new Color(1f, 0.95f, 0.45f, 0.42f));

            Text(new Rect(outer.x + 12f, outer.y + outer.height - 22f, 240f, 20f),
                $"({p.x:0.0}, {p.y:0.0}) · 半径 {p.magnitude:0.0}", 13, InkSoft, TextAnchor.MiddleLeft);
        }

        /// <summary>把一个**实心世界圆**画进小地图（横向分带近似，24 条足够在 250 像素内看不出锯齿）。</summary>
        private static void MapDisc(Rect mapBox, float worldRadius, Color c)
        {
            const int Bands = 26;
            Rect w = MapWorldRect;
            float scale = mapBox.width / w.width;                 // 像素/世界米（x、z 同比例）
            Vector2 center = WorldToMap(Vector2.zero, mapBox);
            float r = worldRadius * scale;
            for (int i = 0; i < Bands; i++)
            {
                float y0 = -r + (i / (float)Bands) * 2f * r;
                float y1 = -r + ((i + 1) / (float)Bands) * 2f * r;
                float ym = (y0 + y1) * 0.5f;
                float half = Mathf.Sqrt(Mathf.Max(0f, r * r - ym * ym));
                if (half <= 0.2f) continue;
                Fill(new Rect(center.x - half, center.y + y0, half * 2f, Mathf.Max(1f, y1 - y0)), c);
            }
        }

        /// <summary>把一个世界方块（中心 + 半边长）画进小地图。</summary>
        private static void MapSquare(Rect mapBox, Vector2 worldCenter, float halfWorld, Color c)
        {
            Vector2 a = WorldToMap(new Vector2(worldCenter.x - halfWorld, worldCenter.y - halfWorld), mapBox);
            Vector2 b = WorldToMap(new Vector2(worldCenter.x + halfWorld, worldCenter.y + halfWorld), mapBox);
            Fill(new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                          Mathf.Max(1f, Mathf.Abs(b.x - a.x)), Mathf.Max(1f, Mathf.Abs(b.y - a.y))), c);
        }

        /// <summary>把一栋楼画成"世界方向上的一个矩形"（宽 = x 方向、深 = z 方向）。</summary>
        private static void MapBuilding(Rect mapBox, Vector2 worldCenter, float halfX, float halfZ, Color c)
        {
            Vector2 a = WorldToMap(new Vector2(worldCenter.x - halfX, worldCenter.y - halfZ), mapBox);
            Vector2 b = WorldToMap(new Vector2(worldCenter.x + halfX, worldCenter.y + halfZ), mapBox);
            Fill(new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                          Mathf.Max(1.5f, Mathf.Abs(b.x - a.x)), Mathf.Max(1.5f, Mathf.Abs(b.y - a.y))), c);
        }

        /// <summary>把一个世界矩形（x/z）画进小地图。</summary>
        private static void MapRect(Rect mapBox, Rect worldRect, Color c)
        {
            Vector2 a = WorldToMap(new Vector2(worldRect.xMin, worldRect.yMin), mapBox);
            Vector2 b = WorldToMap(new Vector2(worldRect.xMax, worldRect.yMax), mapBox);
            float x = Mathf.Min(a.x, b.x), y = Mathf.Min(a.y, b.y);
            float w = Mathf.Abs(b.x - a.x), h = Mathf.Abs(b.y - a.y);
            if (w < 1f) w = 1f;
            if (h < 1f) h = 1f;
            Fill(new Rect(x, y, w, h), c);
        }

        // ══════════════════════════════════════════════════════════════════
        // 载入失败时的红字
        // ══════════════════════════════════════════════════════════════════

        public static void DrawError(string msg)
        {
            Rect box = Centered(900f, 200f);
            PanelBox(box);
            Fill(new Rect(box.x, box.y, box.width, 8f), Bad);
            Text(new Rect(box.x + 24f, box.y + 24f, box.width - 48f, 44f), "主城配置表载入失败", 26, Bad, TextAnchor.MiddleLeft, true);
            Text(new Rect(box.x + 24f, box.y + 76f, box.width - 48f, 100f), msg, 15, Ink);
        }

        /// <summary>
        /// 居中面板框。★ v217：先按屏幕钳一次 ★
        /// 窗口比面板矮时（`_k` 有 0.55 的下限 ⇒ `_vh` 会被拉到 900 以下，比如 727），
        /// 原来的 `(_vh - 620) * 0.5` 仍能居中，但 660 高的商店会在 727 的屏上擦到下沿 ⇒
        /// 钳到 `_vh - 16` 之后，**面板框本身**也不会画到屏幕外 ✓
        /// </summary>
        private static Rect Centered(float w, float h)
        {
            w = Mathf.Min(w, _vw - 16f);
            h = Mathf.Min(h, _vh - 16f);
            return new Rect((_vw - w) * 0.5f, (_vh - h) * 0.5f, w, h);
        }
    }
}
