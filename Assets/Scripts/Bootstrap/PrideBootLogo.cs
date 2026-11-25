using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 启动 Logo 动画：Unity 标之后的独立一段「Pride狮群」展示（黑底 + 金色光扫）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、结构（与同目录 PlayerFacingMarker.cs 同一套自举写法）
    /// ══════════════════════════════════════════════════════════════════════
    /// ```
    /// [RuntimeInitializeOnLoadMethod(AfterSceneLoad)]
    ///   └─ PRIDE_BOOT_LOGO  (DontDestroyOnLoad，唯一宿主，唯一组件)
    ///        └─ OnGUI 直接画：黑底矩形 / logo 贴图 / 金色光扫 / 文字
    /// ```
    /// 全程**不新建 Canvas / Prefab / 场景**，不引用任何其它脚本，不动任何现有资源：
    /// 唯一的资产依赖是 `Resources.Load&lt;Texture2D&gt;("Logo/Pride")`
    /// （= `Assets/Resources/Logo/Pride.jpg` 的复制件；`Assets/Logo/Pride.jpg` 原文件保持不动）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、只播一次（这是本文件唯一的"状态"设计）
    /// ══════════════════════════════════════════════════════════════════════
    /// `AfterSceneLoad` **每个进程只回调一次**（进主城 / 进战斗走 `SceneManager.LoadScene`，
    /// 不会再次触发它），再加一道静态标志 `Played` 兜住：
    ///   · 播完（或跳过）→ `Played = true`，宿主 `Destroy`；
    ///   · 万一有第二条路径又调到 `Install`，`Played` 已为真 → 直接返回，**不重播**。
    /// 用静态 bool 而不是 PlayerPrefs：需求是"同一进程内只播一次"，
    /// 落盘会让玩家**重开游戏**也看不到（那不是本次要的）。静态 bool 在
    /// 编辑器 Enter Play Mode 时随域重载复位 → 每次 Play 都能再看一遍 ✔
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、时间轴（全部 unscaled，秒；`TotalDuration = 2.70`）
    /// ══════════════════════════════════════════════════════════════════════
    /// ```
    /// 0.00 ─ 0.25  黑底 alpha 0→1（淡入）
    /// 0.00 ─ 0.60  logo：scale 0.92→1.00、alpha 0→1（居中，边长 min(w,h)*0.55）
    /// 0.40 ─ 0.90  文字「Pride狮群」alpha 0→1，rect 同时从 +14px 浮到 0
    /// 0.60 ─ 1.40  金色光扫：竖直光带（宽 = logo 宽 *12%）从左到右扫过一次
    /// 1.40 ─ 2.20  停留（全要素 alpha = 1）
    /// 2.20 ─ 2.70  整体 alpha 1→0 淡出
    /// 2.70         宿主销毁
    /// ```
    /// 段与段之间**不留空档也不重叠冗余**：2.7s 是最晚结束的一段 + 停留 0.8s + 淡出 0.5s
    /// （需求写的"约 2.9s"是估算，这里按段严格排布后为 2.70s，改常量即可调）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、颜色只有三档（"颜色风格统一"= 不加第四种色相）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 黑底   (0,0,0)
    ///   · 金     (1.00, 0.86, 0.55)  → 光扫、文字
    ///   · 暖白   (1.00, 0.97, 0.92)  → 光扫纹理本身的白（乘金色后仍是金色系）
    /// 本文件里**没有第四种颜色**：所有矩形/贴图都是 `GUI.color = 三档之一 * alpha`。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、跳过
    /// ══════════════════════════════════════════════════════════════════════
    /// 任意键 / 鼠标按下 → 立刻 `Destroy`（不是快进：需求是"立即跳过"）。
    /// 两条通道都查，因为本工程装了 Input System 包：
    ///   ① `OnGUI` 里读 `Event.current`（MouseDown / KeyDown）——不依赖任何输入后端，最稳；
    ///   ② `Update` 里 `Input.anyKeyDown`——包在 try/catch 里，若工程把
    ///      Old Input 关掉（只留新系统），这句会抛 `InvalidOperationException`，
    ///      捕获后忽略即可，① 仍然有效。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 六、生成物与生命周期
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 光扫渐变贴图：1×64、`HideFlags.DontSave`、**整个进程只建一次**（静态缓存），
    ///     `OnDestroy` 里 `Destroy`（同时清静态引用，避免下一次 Play 拿到已销毁对象）。
    ///   · CJK 动态字体：`Font.CreateDynamicFontFromOSFont` 建（内置 GUI 字体不一定有汉字），
    ///     同样是静态缓存，`OnDestroy` 里销毁。
    ///   · `Resources.Load` 回来的 logo 贴图是**工程资产**，绝不销毁（只放弃引用）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrideBootLogo : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────
        // 时间轴常量（秒）——改这里就能改节奏，类注释里的表跟着改
        // ────────────────────────────────────────────────────────────────
        /// <summary>黑底 alpha 0→1 用时。</summary>
        public const float FadeInDur = 0.25f;
        /// <summary>logo scale 0.92→1.00 且 alpha 0→1 用时。</summary>
        public const float LogoInDur = 0.60f;
        /// <summary>文字相对 logo **晚 0.4s** 开始。</summary>
        public const float TextDelay = 0.40f;
        /// <summary>文字 alpha 0→1 用时。</summary>
        public const float TextInDur = 0.50f;
        /// <summary>金色光扫开始时刻（logo 淡入结束 = 0.60s）。</summary>
        public const float SweepStart = 0.60f;
        /// <summary>金色光扫从左扫到右用时（只扫一次）。</summary>
        public const float SweepDur = 0.80f;
        /// <summary>停留时长（所有动画结束后）。</summary>
        public const float HoldDur = 0.80f;
        /// <summary>整体 alpha 1→0 用时。</summary>
        public const float FadeOutDur = 0.50f;

        /// <summary>淡出起点 = 所有动画中最晚结束的那一段（光扫 0.60+0.80）+ 停留 0.80。</summary>
        public const float FadeOutStart = SweepStart + SweepDur + HoldDur;   // 2.20
        /// <summary>总时长 = 淡出结束时刻；到时销毁宿主。</summary>
        public const float TotalDuration = FadeOutStart + FadeOutDur;        // 2.70

        // ────────────────────────────────────────────────────────────────
        // 画面常量
        // ────────────────────────────────────────────────────────────────
        /// <summary>logo 起始缩放（0.92 → 1.00 的"推近"）。</summary>
        public const float LogoScaleFrom = 0.92f;
        /// <summary>logo 起始缩放（终值）。</summary>
        public const float LogoScaleTo = 1.00f;
        /// <summary>logo 边长 = min(屏宽, 屏高) × 0.55（正方形图，1280×1280）。</summary>
        public const float LogoSizeFactor = 0.55f;
        /// <summary>光带宽度 = logo 宽 × 0.12。</summary>
        public const float SweepWidthFactor = 0.12f;
        /// <summary>光带峰值 alpha（纹理再乘它）。</summary>
        public const float SweepPeakAlpha = 0.35f;
        /// <summary>文字所在行的额外间距 = min(屏宽,屏高) × 0.03（在 logo 下方）。</summary>
        public const float TextGapFactor = 0.03f;
        /// <summary>文字上浮的起点偏移（像素，→ 0 = 浮起感）。</summary>
        public const float TextRisePx = 14f;
        /// <summary>文字字号 = 屏宽 × 0.032。</summary>
        public const float FontSizeFactor = 0.032f;
        /// <summary>文字字号下限。</summary>
        public const int FontSizeMin = 18;

        // ────────────────────────────────────────────────────────────────
        // 颜色：只许三档（见类注释第四节）
        // ────────────────────────────────────────────────────────────────
        /// <summary>黑底。</summary>
        private static readonly Color ColorBlack = new Color(0f, 0f, 0f, 1f);
        /// <summary>金（光扫 / 文字）。</summary>
        private static readonly Color ColorGold = new Color(1.00f, 0.86f, 0.55f, 1f);
        /// <summary>暖白（光扫纹理的白，乘金色后仍落在金色系里）。</summary>
        private static readonly Color ColorWarmWhite = new Color(1.00f, 0.97f, 0.92f, 1f);

        /// <summary>要画的文字。</summary>
        private const string Label = "Pride狮群";
        /// <summary>logo 的 Resources 相对路径（不带扩展名）。</summary>
        private const string LogoResourcePath = "Logo/Pride";
        /// <summary>`GUI.depth`：越小越靠前，-1000 = 压在其它 IMGUI 之上。</summary>
        private const int GuiDepth = -1000;

        /// <summary>汉字字体候选（内置 GUI 字体不一定带 CJK 字形，按顺序取第一个可用的 OS 字体）。</summary>
        private static readonly string[] CjkFontNames =
        {
            "Microsoft YaHei", "微软雅黑", "SimHei", "黑体",
            "Noto Sans CJK SC", "PingFang SC", "Segoe UI"
        };

        // ────────────────────────────────────────────────────────────────
        // 静态状态
        // ────────────────────────────────────────────────────────────────
        /// <summary>本次进程是否已经播过（播完 / 跳过都算）。**只播一次的唯一凭据**。</summary>
        public static bool Played;
        /// <summary>当前存活的宿主数（恒为 0 或 1）。</summary>
        public static int Instances;
        /// <summary>光扫渐变贴图的构建次数（正常恒为 1 —— 静态缓存）。</summary>
        public static int TextureBuilds;

        private static PrideBootLogo _instance;
        private static Texture2D _sweepTex;    // 1×64 竖向渐变，DontSave
        private static Font _labelFont;        // CJK 动态字体

        // ────────────────────────────────────────────────────────────────
        // 实例状态
        // ────────────────────────────────────────────────────────────────
        private Texture2D _logo;               // Resources 里的工程资产，**不销毁**
        private GUIStyle _labelStyle;          // 缓存（字号或屏宽变化才重建）
        private int _styleFontSize;            // 上面这个 style 用的字号
        private int _styleScreenWidth;         // 上面这个 style 建时的屏宽
        private float _time;                   // 累计 unscaled 时间
        private bool _skip;                    // OnGUI 里收到"键/鼠标"事件 → Update 收尾
        private bool _done;                    // 已请求销毁（防重入）

        // ────────────────────────────────────────────────────────────────
        // 自举
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 进 Play 后第一个场景加载完时自建宿主（与 `PlayerFacingMarker.Install` 同一套写法）。
        /// **每个进程只走一次**；`Played` 已真则什么都不做（不重播）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Played) return;              // 已经播过 → 不重播
            if (_instance != null) return;   // 宿主已在（双保险）

            var go = new GameObject("PRIDE_BOOT_LOGO");
            _instance = go.AddComponent<PrideBootLogo>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);   // 跨场景活着（本段动画在切场景之前就结束并销毁）
            Instances++;

            _logo = Resources.Load<Texture2D>(LogoResourcePath);   // ← 复制件；加载失败只是没有图，文字照画
            _sweepTex = SweepTexture();                            // ← 全程只建一次
        }

        private void OnDestroy()
        {
            Instances--;
            if (_instance == this) _instance = null;

            // 生成的资源要自己销毁；`Resources.Load` 回来的资产不能碰
            if (_sweepTex != null)
            {
                Destroy(_sweepTex);
                _sweepTex = null;
            }
            if (_labelFont != null)
            {
                Destroy(_labelFont);
                _labelFont = null;
            }
            _logo = null;
            _labelStyle = null;
            Played = true;                   // 不管是怎么结束的，本进程都不再播
        }

        // ────────────────────────────────────────────────────────────────
        // 计时：全用 unscaled（暂停 / 顿帧都不影响这段动画）
        // ────────────────────────────────────────────────────────────────
        private void Update()
        {
            if (_done) return;

            float dt = Time.unscaledDeltaTime;
            if (dt > 0.1f) dt = 0.1f;        // 首帧 / 断点回来的大 delta 别把整段动画一口吃完
            _time += dt;

            if (_skip || SkipPressed() || _time >= TotalDuration) Finish();
        }

        /// <summary>立即结束：销毁宿主（`OnDestroy` 里置 `Played`）。</summary>
        private void Finish()
        {
            if (_done) return;
            _done = true;
            Played = true;
            Destroy(gameObject);
        }

        /// <summary>
        /// 键 / 鼠标跳过。`Input.anyKeyDown` 在"只启用新输入系统"的工程里会抛异常，
        /// 所以包一层 try；`OnGUI` 的 `Event.current` 通道不受输入后端影响（见类注释第五节）。
        /// </summary>
        private static bool SkipPressed()
        {
            try
            {
                if (Input.anyKeyDown) return true;
            }
            catch (System.InvalidOperationException)
            {
                // 旧输入后端被关掉 → 这条通道不可用，交给 OnGUI 的 Event.current
            }
            return false;
        }

        // ────────────────────────────────────────────────────────────────
        // 画（全部 IMGUI，不建 Canvas）
        // ────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (_done) return;

            GUI.depth = GuiDepth;            // 压在其它 IMGUI 之上

            // ① 键 / 鼠标：优先在这里判（不挑输入后端）
            Event e = Event.current;
            if (e != null && (e.type == EventType.MouseDown || e.type == EventType.KeyDown))
            {
                // 本帧仍然照画（不 return）：跳过发生在 Update 的销毁里，
                // 提前 return 会让这一帧黑底整块消失、露出后面的游戏画面，闪一下。
                _skip = true;
            }

            float t = _time;
            float w = Screen.width;
            float h = Screen.height;
            if (w <= 0f || h <= 0f) return;

            float fadeIn = Mathf.Clamp01(t / FadeInDur);                        // 黑底
            float logoIn = Mathf.Clamp01(t / LogoInDur);                        // logo
            float textIn = Mathf.Clamp01((t - TextDelay) / TextInDur);          // 文字
            float sweep = Mathf.Clamp01((t - SweepStart) / SweepDur);           // 光扫 0→1
            float global = 1f - Mathf.Clamp01((t - FadeOutStart) / FadeOutDur);  // 整体淡出

            // ── 黑底（全屏，alpha 0→1 淡入；之后随整体淡出）────────────────
            Color keep = GUI.color;
            GUI.color = WithAlpha(ColorBlack, fadeIn * global);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);

            // ── logo 矩形（居中，边长 min(w,h)*0.55，scale 0.92→1.00）─────
            float minSide = Mathf.Min(w, h);
            float side = minSide * LogoSizeFactor * Mathf.Lerp(LogoScaleFrom, LogoScaleTo, EaseOut(logoIn));
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            var logoRect = new Rect(cx - side * 0.5f, cy - side * 0.5f, side, side);
            float logoAlpha = logoIn * global;

            if (_logo != null && logoAlpha > 0f)
            {
                GUI.color = WithAlpha(ColorWarmWhite, logoAlpha);
                GUI.DrawTexture(logoRect, _logo, ScaleMode.ScaleToFit, true);
            }

            // ── 金色光扫（裁在 logo 矩形内，只扫一次）─────────────────────
            float sweepAlpha = SweepAlphaAt(t);          // 进/出各留一点点软边，避免"啪"地出现
            if (_sweepTex != null && sweepAlpha > 0f && sweep > 0f)
            {
                float bandW = side * SweepWidthFactor;
                // 起点在 logo 左边外面 → 终点在右边外面，保证"完整扫进去又扫出去"
                float x = Mathf.Lerp(-bandW, side, sweep);

                GUI.BeginGroup(logoRect);                // 组 = 裁剪矩形（光带不出界）
                GUI.color = WithAlpha(ColorGold, sweepAlpha * global);
                GUI.DrawTexture(new Rect(x, 0f, bandW, logoRect.height), _sweepTex);
                GUI.EndGroup();
            }

            // ── 文字（logo 下方居中，晚 0.4s 起，alpha 0→1，+14px → 0 上浮）──
            if (textIn > 0f)
            {
                GUIStyle st = LabelStyle((int)w);
                if (st != null)
                {
                    float fontPx = Mathf.Max(FontSizeMin, Mathf.RoundToInt(w * FontSizeFactor));
                    float lineH = fontPx * 1.5f;
                    float top = logoRect.yMax + minSide * TextGapFactor
                                + Mathf.Lerp(TextRisePx, 0f, EaseOut(textIn));   // 轻微上移
                    st.normal.textColor = WithAlpha(ColorGold, textIn * global);
                    GUI.color = Color.white;         // 颜色全在 style 里（上面那行）
                    GUI.Label(new Rect(0f, top, w, lineH), Label, st);
                }
            }

            GUI.color = keep;
        }

        /// <summary>
        /// 光扫的 alpha 包络：整段时间里线性扫，但**最前 10% / 最后 10%** 把 alpha 收一收，
        /// 免得光带在边缘处突然出现 / 突然消失（峰值仍是 `SweepPeakAlpha`）。
        /// </summary>
        private static float SweepAlphaAt(float t)
        {
            float p = (t - SweepStart) / SweepDur;
            if (p <= 0f || p >= 1f) return 0f;
            float edge = Mathf.Min(Mathf.Clamp01(p / 0.1f), Mathf.Clamp01((1f - p) / 0.1f));
            return SweepPeakAlpha * edge;
        }

        /// <summary>缓出（推近、浮起都用它，比线性更像"到位"）。</summary>
        private static float EaseOut(float p)
        {
            p = Mathf.Clamp01(p);
            return 1f - (1f - p) * (1f - p);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = Mathf.Clamp01(a);
            return c;
        }

        // ────────────────────────────────────────────────────────────────
        // 生成物
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 1×64 竖向渐变（"白→透明→白"的软边光带：**中心最白、上下两端收敛到透明**）。
        /// 只建一次（静态缓存），`HideFlags.DontSave`，`OnDestroy` 销毁。
        /// </summary>
        private static Texture2D SweepTexture()
        {
            if (_sweepTex != null) return _sweepTex;

            const int h = 64;
            // linear = false：这是 IMGUI 画的一张 alpha 遮罩，按 sRGB 存（linear 存会让渐变发灰）
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false, false)
            {
                name = "PrideBootSweepGradient",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var px = new Color32[h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;                    // 0 = 下边，1 = 上边
                float a = Mathf.Sin(v * Mathf.PI);           // 两端 0、中心 1
                px[y] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            TextureBuilds++;
            return tex;
        }

        /// <summary>
        /// 文字样式：字号随屏宽（`w * 0.032`，最小 18）。**只在字号或屏宽变化时重建**，
        /// 避免每帧 new GUIStyle / new Font。
        /// </summary>
        private GUIStyle LabelStyle(int screenWidth)
        {
            int size = Mathf.Max(FontSizeMin, Mathf.RoundToInt(screenWidth * FontSizeFactor));
            if (_labelStyle != null && _styleFontSize == size && _styleScreenWidth == screenWidth)
                return _labelStyle;

            if (_labelFont == null)
            {
                // 内置 GUI 字体不一定有汉字字形 → 从系统字体里挑一个（中文 Windows 上就是雅黑）
                _labelFont = Font.CreateDynamicFontFromOSFont(CjkFontNames, size);
            }

            var st = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                clipping = TextClipping.Overflow,
                wordWrap = false,
                richText = false
            };
            if (_labelFont != null) st.font = _labelFont;
            st.fontSize = size;
            st.normal.textColor = ColorGold;

            _labelStyle = st;
            _styleFontSize = size;
            _styleScreenWidth = screenWidth;
            return st;
        }

        /// <summary>一行读数（自测 / 截图叠字用，与项目里其它 Status() 同一风格）。</summary>
        public static string Status()
        {
            return $"played={Played} 实例={Instances} 光扫贴图={TextureBuilds}" +
                   $" 总时长={TotalDuration:0.00}s 金=({ColorGold.r:0.00},{ColorGold.g:0.00},{ColorGold.b:0.00})";
        }
    }
}
