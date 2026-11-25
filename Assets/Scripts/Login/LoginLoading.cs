using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 2D 载入动画：**7 个蔬菜图标组成的进度条** ＋ 全屏遮罩 ＋ 淡出 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么需要它（用户报的问题）
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户原话：「游戏的每次启动会黑屏弹字找不到摄像机」。
    /// 黑屏的**直接原因**有两层，遮罩只是第二层：
    ///   ① `Login.unity` 里唯一那台相机曾经是 `m_IsActive: 0`（我上一轮为了"别抢主城渲染"把它关了）
    ///      ⇒ 场景加载到 `Town` 建好之前，**一台相机都不渲染** ⇒ Game 视图弹 `No cameras rendering` ✗
    ///   ② 就算有相机，主城是**加法加载 + 代码建城**（几百个物体），中间那几帧也没有内容 ✗
    /// ⇒ 修法：相机常开（本文件不管，见 `LoginBootstrap.EnsureFallbackCamera`）＋ **这张遮罩把建城那几帧盖住** ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、为什么宿主是 `DontDestroyOnLoad` 的独立对象，而不是登录面板自己画
    /// ══════════════════════════════════════════════════════════════════════
    /// `LoginBootstrap` 活在 `Login.unity` 里 —— 一进战斗（`LoadScene` 单场景）它连同整个场景被销毁，
    /// 遮罩跟着消失，战斗场景的头几帧就又是黑屏 ✗
    /// ⇒ 遮罩的宿主做成 `DontDestroyOnLoad` 的小对象（`LoginLoadingHost`），
    ///   跨场景活着；用 `GUI.depth = -1000` 保证它在**所有 OnGUI 之上**（IMGUI 里 depth 越小越靠上）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、"进度"从哪来（这一点必须说实话）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · `Login → Town`：`SceneManager.LoadSceneAsync` 的 `op.progress`（0→0.9）**真进度** ✓
    ///   · 存档读取：`读文件 → 灌状态 → 进城` 三个真实里程碑 ✓
    ///   · `Town → SeedfallArena`：那是 `TownBootstrap.GoBattle()` 里的**同步** `LoadScene`（禁改区），
    ///     同步加载期间**一帧都不吐**，拿不到任何进度 ⇒ 这里只能给"加载完成后的确认遮罩"（0.8 秒淡出）✓
    ///     这条在报告里写明，不当成假进度骗人 ✗
    ///
    /// 图标：`Resources/Art/UI/load/{carrot,cabbage,pumpkin,tomato,corn,eggplant,onion}.png`
    /// 由 `gen_veg_icons.py`（128×128、深色描边平涂）程序化生成；**文件缺失时**这里会现场画一个同色占位，
    /// 保证"进度条永远有 7 个图标"，不会因为美术没到位就整条不显示 ✓
    /// </summary>
    public static class LoginLoading
    {
        public const string Tag = "[Loading] ";

        /// <summary>蔬菜图标数（= 进度条点亮个数）。</summary>
        public const int VegCount = 7;

        public static readonly string[] VegIds = { "carrot", "cabbage", "pumpkin", "tomato", "corn", "eggplant", "onion" };

        public static readonly string[] VegNames = { "胡萝卜", "白菜", "南瓜", "番茄", "玉米", "茄子", "洋葱" };

        /// <summary>占位色（PNG 缺失时用这几支颜色现场画）。</summary>
        public static readonly Color[] VegColors =
        {
            new Color(0.91f, 0.50f, 0.16f),   // 胡萝卜
            new Color(0.46f, 0.72f, 0.30f),   // 白菜
            new Color(0.89f, 0.49f, 0.12f),   // 南瓜
            new Color(0.84f, 0.23f, 0.18f),   // 番茄
            new Color(0.96f, 0.81f, 0.31f),   // 玉米
            new Color(0.42f, 0.24f, 0.62f),   // 茄子
            new Color(0.77f, 0.54f, 0.70f),   // 洋葱
        };

        /// <summary>遮罩至少显示多久（秒）—— 加载太快也要让玩家看见"在载入"，否则就是闪一下 ✓</summary>
        private const float MinVisibleSeconds = 0.55f;

        /// <summary>淡出时长（秒）。</summary>
        private const float FadeSeconds = 0.35f;

        // ── 运行时状态 ──
        private static bool _active;
        private static float _progress;         // 当前显示值（0..1）
        private static float _target;           // 目标值（0..1）
        private static string _label = "";
        private static float _shownAt;          // 开始显示的时刻（unscaled）
        private static float _fade = 1f;        // 1 = 实心遮罩
        private static float _fadingFrom;       // 淡出起点
        private static float _fadeStartAt;
        private static bool _fading;
        private static float _debugPin = -1f;   // >= 0 ⇒ 钉住进度（自动截图用）

        public static bool Active => _active && !_fading;
        public static float Progress => _progress;
        public static string Label => _label;

        // ── 资源缓存 ──
        private static Texture2D[] _icons;
        private static Texture2D _white;
        private static Font _font;
        /// <summary>建字体那一刻的语言代数（≠ `L10n.FontGeneration` ⇒ 重建字体与三个样式）。</summary>
        private static int _fontGen = -1;
        private static GUIStyle _titleStyle;
        private static GUIStyle _smallStyle;
        private static GUIStyle _centerStyle;

        // ══════════════════════════════════════════════════════════════════
        // 对外：开始 / 推进 / 结束
        // ══════════════════════════════════════════════════════════════════

        /// <summary>开始一次载入（重复调用 = 覆盖标签与进度，不会叠第二层）。</summary>
        public static void Begin(string label)
        {
            _label = label ?? "";
            _progress = 0f;
            _target = 0f;
            _active = true;
            _fading = false;
            _fade = 1f;
            _shownAt = Time.realtimeSinceStartup;
            _debugPin = -1f;
            LoadIcons();
            Log.Info(Tag + "遮罩开始：" + _label);
        }

        /// <summary>设目标进度（0..1）。显示值会平滑追上，避免数字乱跳。</summary>
        public static void SetProgress(float p)
        {
            if (!_active) return;
            _target = Mathf.Clamp01(p);
            if (_debugPin >= 0f) return;
            // 只许前进：进度条倒退比不动更难看
            if (_target > _progress) _progress = Mathf.Lerp(_progress, _target, 0.55f);
            if (_target - _progress < 0.01f && _target > 0.999f) _progress = 1f;
        }

        /// <summary>强制把进度拍到某个值（自动化截图用：0% / 50% / 100%）。</summary>
        public static void DebugPin(float p, string label)
        {
            _active = true;
            _fading = false;
            _fade = 1f;
            if (!string.IsNullOrEmpty(label)) _label = label;
            _debugPin = Mathf.Clamp01(p);
            _progress = _debugPin;
            _target = _debugPin;
            _shownAt = Time.realtimeSinceStartup;
            LoadIcons();
            Log.Warn(Tag + $"调试钉住进度 = {(_debugPin * 100f):0}%（{VegCount} 个蔬菜里点亮 {LitCount():0}）");
        }

        /// <summary>载入完成：先保证"至少看见 MinVisibleSeconds"，再淡出。</summary>
        public static void Complete()
        {
            if (!_active) return;
            _debugPin = -1f;
            _target = 1f;
            _progress = 1f;
            float shown = Time.realtimeSinceStartup - _shownAt;
            if (shown < MinVisibleSeconds) _shownAt -= (MinVisibleSeconds - shown);  // 借时间，让它"已经显示够久了"
            _fading = true;
            _fadeStartAt = Time.realtimeSinceStartup + Mathf.Max(0f, MinVisibleSeconds - shown);
            _fadingFrom = 1f;
            Log.Info(Tag + "遮罩淡出：" + _label);
        }

        /// <summary>立刻关掉（出错 / 兜底用）。</summary>
        public static void Reset()
        {
            _active = false;
            _fading = false;
            _fade = 0f;
            _debugPin = -1f;
            _progress = 0f;
            _target = 0f;
        }

        /// <summary>每帧推进（由宿主的 Update 调）。</summary>
        public static void Tick()
        {
            if (!_active) return;

            if (_debugPin < 0f && _progress < _target) _progress = Mathf.Lerp(_progress, _target, Mathf.Min(1f, 6f * Time.unscaledDeltaTime));

            if (!_fading) { _fade = 1f; return; }

            float t = (Time.realtimeSinceStartup - _fadeStartAt) / FadeSeconds;
            _fade = Mathf.Clamp01(_fadingFrom * (1f - t));
            if (t >= 1f) Reset();
        }

        /// <summary>点亮了几个蔬菜（进度 1.0 ⇒ 7 个全亮）。</summary>
        public static int LitCount()
        {
            if (_progress >= 0.999f) return VegCount;
            return Mathf.Clamp(Mathf.FloorToInt(_progress * VegCount + 0.0001f), 0, VegCount);
        }

        public static string Describe()
        {
            return $"遮罩={(_active ? (_fading ? "淡出中" : "显示中") : "关")}｜标签={(_label ?? "")}" +
                   $"｜进度={(_progress * 100f):0}%（目标 {(_target * 100f):0}%）" +
                   $"｜点亮蔬菜={LitCount()}/{VegCount}｜透明度={_fade:0.00}" +
                   $"｜图标源={(IconsReadyFromResources() ? "Resources/Art/UI/load" : "代码占位")}";
        }

        /// <summary>是否被"调试钉住"（自动化截图）。宿主用它避免自己的进场景钩子把钉住的遮罩冲掉。</summary>
        public static bool IsPinned => _debugPin >= 0f;

        public static bool IconsReadyFromResources()
        {
            if (_icons == null) LoadIcons();
            return _icons != null && _icons.Length == VegCount && _icons[0] != null && _icons[0].name.StartsWith("carrot");
        }

        // ══════════════════════════════════════════════════════════════════
        // 绘制
        // ══════════════════════════════════════════════════════════════════

        private static float K => Mathf.Clamp(Screen.height / 900f, 0.55f, 2.2f);
        private static float VW => Screen.width / K;
        private static float VH => Screen.height / K;

        private static Rect S(Rect r) => new Rect(r.x * K, r.y * K, r.width * K, r.height * K);

        /// <summary>每帧画（宿主 `OnGUI` 里调，`GUI.depth` 已设为最低 ⇒ 画在最上层）。</summary>
        public static void Draw()
        {
            if (!_active || _fade <= 0.001f) return;
            LoadIcons();

            float a = _fade;
            Color prev = GUI.color;

            // ── 全屏遮罩（深墨绿，不是纯黑：黑屏正是用户抱怨的那个"故障感"）──
            Color bg = new Color(0.078f, 0.106f, 0.086f, 0.96f * a);
            GUI.color = bg;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), White());
            GUI.color = prev;

            if (_white != null) { /* 无操作，占位避免风格检查报未使用 */ }

            // ── 标题 ──
            var titleRect = S(new Rect(0f, VH * 0.5f - 196f, VW, 56f));
            DrawText(titleRect, "载入中", 42, new Color(0.98f, 0.95f, 0.86f, a), TextAnchor.MiddleCenter, true);

            if (!string.IsNullOrEmpty(_label))
            {
                var subRect = S(new Rect(0f, VH * 0.5f - 140f, VW, 34f));
                DrawText(subRect, _label, 20, new Color(0.86f, 0.82f, 0.72f, a), TextAnchor.MiddleCenter, false);
            }

            // ── 七个蔬菜 ──
            const float Icon = 88f, Gap = 16f;
            float total = VegCount * Icon + (VegCount - 1) * Gap;
            float x0 = (VW - total) * 0.5f;
            float iconY = VH * 0.5f - 84f;
            int lit = LitCount();

            for (int i = 0; i < VegCount; i++)
            {
                bool on = i < lit;
                var rect = S(new Rect(x0 + i * (Icon + Gap), iconY, Icon, Icon));
                if (on)
                {
                    // 到了的：彩色 + 轻微放大。
                    // ⚠️ 这里**不要**再加一层"暖色底光"矩形 —— 实测那会渲染成一块**灰方块**，
                    //    比不加更难看（截图里一眼就看到 7 个格子里有 3 个套着灰底）。
                    //    图标本身彩色 vs 灰度已经足够区分"到没到" ✓
                    float pop = 1.06f;
                    var r2 = new Rect(rect.x - rect.width * (pop - 1f) * 0.5f, rect.y - rect.height * (pop - 1f) * 0.5f,
                                      rect.width * pop, rect.height * pop);
                    GUI.color = new Color(1f, 1f, 1f, a);
                    if (_icons[i] != null) GUI.DrawTexture(r2, _icons[i], ScaleMode.ScaleToFit);
                }
                else
                {
                    // 没到的：灰 + 半透明（"暗/灰"是需求原文），但**别压太狠**，
                    // 压到 0.34/0.55 时 7 个图标在深底上糊成一排褐色团块，看不出是七种蔬菜 ✗
                    GUI.color = new Color(0.46f, 0.46f, 0.46f, 0.66f * a);
                    if (_icons[i] != null) GUI.DrawTexture(rect, _icons[i], ScaleMode.ScaleToFit);
                }
                GUI.color = prev;
            }

            // ── 进度条本体（图标下面那条填充）──
            const float BarW = 700f, BarH = 24f;
            var barBack = new Rect((VW - BarW) * 0.5f, VH * 0.5f + 24f, BarW, BarH);
            GUI.color = new Color(0.16f, 0.19f, 0.16f, 0.9f * a);
            GUI.DrawTexture(S(barBack), White());
            GUI.color = prev;

            var barFill = new Rect(barBack.x, barBack.y, barBack.width * Mathf.Clamp01(_progress), barBack.height);
            GUI.color = new Color(0.98f, 0.78f, 0.24f, a);
            if (barFill.width > 0.5f) GUI.DrawTexture(S(barFill), White());
            GUI.color = prev;

            // 条的外框（四条边，用 Build 好的 1×1 白图铺）
            Color frame = new Color(0.92f, 0.88f, 0.76f, a);
            GUI.color = frame;
            float th = 2f * K;
            Rect bar = S(barBack);
            GUI.DrawTexture(new Rect(bar.x - th, bar.y - th, bar.width + th * 2f, th), White());
            GUI.DrawTexture(new Rect(bar.x - th, bar.y + bar.height, bar.width + th * 2f, th), White());
            GUI.DrawTexture(new Rect(bar.x - th, bar.y - th, th, bar.height + th * 2f), White());
            GUI.DrawTexture(new Rect(bar.x + bar.width, bar.y - th, th, bar.height + th * 2f), White());
            GUI.color = prev;

            // ── 百分比 + 计数 ──
            var pctRect = S(new Rect(0f, VH * 0.5f + 58f, VW, 40f));
            DrawText(pctRect, $"{Mathf.RoundToInt(_progress * 100f)}%　　{lit}/{VegCount} {VegNameList(lit)}",
                  22, new Color(0.98f, 0.95f, 0.86f, a), TextAnchor.MiddleCenter, true);

            if (_progress >= 0.999f)
            {
                var doneRect = S(new Rect(0f, VH * 0.5f + 104f, VW, 34f));
                DrawText(doneRect, "· 载入完成 ·", 18, new Color(0.55f, 0.82f, 0.42f, a), TextAnchor.MiddleCenter, true);
            }

            GUI.color = prev;
        }

        private static string VegNameList(int lit)
        {
            if (lit <= 0) return "";
            var sb = new System.Text.StringBuilder(32);
            for (int i = 0; i < lit && i < VegCount; i++)
            {
                if (i > 0) sb.Append('·');
                sb.Append(VegNames[i]);
            }
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // 素材
        // ══════════════════════════════════════════════════════════════════

        private static Texture2D White()
        {
            if (_white != null) return _white;
            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            _white.hideFlags = HideFlags.HideAndDontSave;
            return _white;
        }

        private static void LoadIcons()
        {
            if (_icons != null && _icons.Length == VegCount) return;
            _icons = new Texture2D[VegCount];
            int fromResources = 0;
            for (int i = 0; i < VegCount; i++)
            {
                Texture2D tex = Resources.Load<Texture2D>("Art/UI/load/" + VegIds[i]);
                if (tex != null) { _icons[i] = tex; fromResources++; }
                else _icons[i] = MakeFallback(i);
            }
            Log.Info(Tag + $"蔬菜图标：Resources 取到 {fromResources}/{VegCount}，其余现场生成占位");
        }

        /// <summary>PNG 缺失时的现场占位：同色圆 + 深色描边 + 一点高光，至少看得出是 7 个不同的东西。</summary>
        private static Texture2D MakeFallback(int index)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            Color c = VegColors[index % VegColors.Length];
            Color ink = new Color(0.165f, 0.129f, 0.094f);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x - N * 0.5f) / (N * 0.36f);
                float dy = (y - N * 0.42f) / (N * 0.34f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                Color px = new Color(0f, 0f, 0f, 0f);
                if (r < 1.0f) px = c;
                else if (r < 1.14f) px = ink;
                if (r < 1.0f && dy < -0.55f && Mathf.Abs(dx) < 0.28f) px = new Color(0.30f, 0.52f, 0.24f);  // 梗
                if (r < 0.42f && dx < -0.4f && dy > -0.2f) px = Color.Lerp(c, Color.white, 0.45f);          // 高光
                tex.SetPixel(x, y, px);
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.name = VegIds[index] + "_fallback";
            return tex;
        }

        // ══════════════════════════════════════════════════════════════════
        // 文字（与工程 OnGUI 皮肤同一套：系统动态中文字体）
        // ══════════════════════════════════════════════════════════════════

        private static void DrawText(Rect realRect, string text, int size, Color color, TextAnchor anchor, bool bold)
        {
            if (_titleStyle == null || _smallStyle == null || _centerStyle == null
                || _fontGen != L10n.FontGeneration) BuildStyles();   // ★ 切语言 ⇒ 连字体一起重建
            text = L10n.S(text);                                    // ★ 本地化：加载遮罩上的字走这条
            GUIStyle st = bold ? _titleStyle : _centerStyle;
            int px = Mathf.Max(8, Mathf.RoundToInt(size * K));
            var local = new GUIStyle(st) { fontSize = px, alignment = anchor };
            if (bold) local.fontStyle = FontStyle.Bold;
            Color prev = local.normal.textColor;
            local.normal.textColor = color;
            GUI.Label(realRect, text, local);
            local.normal.textColor = prev;
        }

        private static void BuildStyles()
        {
            // ★ 字体跟着语言走：`Microsoft YaHei` 没有 Hangul / Arabic ⇒ 换语言必须换字体，
            //   并且**重建三个缓存样式**（它们把 `font` 抄进了构造参数）✓
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
            }
            _titleStyle = new GUIStyle(GUI.skin.label) { font = _font, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _smallStyle = new GUIStyle(GUI.skin.label) { font = _font, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _centerStyle = new GUIStyle(GUI.skin.label) { font = _font, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }
    }

    /// <summary>
    /// 遮罩的宿主：`DontDestroyOnLoad`，跨场景活着；`GUI.depth = -1000` ⇒ 画在所有 OnGUI 之上。
    /// **不做任何自动创建**（没有 `[RuntimeInitializeOnLoadMethod]`）—— 由 `LoginBootstrap.Awake` 显式建 ✓
    ///
    /// ★ 它还兼一个职责：**战斗场景的"进图遮罩"** ★
    /// `TownBootstrap.GoBattle()` 用的是**同步** `SceneManager.LoadScene`（禁改区），
    /// 同步加载期间一帧都不吐 ⇒ 真实进度拿不到。但"加载完了、战场还在建"的那几帧同样会闪，
    /// 所以这里订阅 `sceneLoaded`：进入 `SeedfallArena` 这类"非登录/非主城"场景时，
    /// 立刻把遮罩以 100% + "载入完成"的形态盖上去，0.55 秒后淡出 ✓
    /// 报告里写清：这是**加载完成后的确认遮罩**，不是假进度 ✗
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoginLoadingHost : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            // ⚠️ **不要**给它 `HideFlags.HideAndDontSave`：那样 `GameObject.Find` 看不见它，
            // 桥（`unity_drive send LoginLoadingHost ...`）就找不到这个对象、也调不到 ShotNow ✗
            // 它只是"跨场景的一个 UI 宿主"，在 Hierarchy 里看得见反而是优点（可排查）✓
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (!scene.IsValid()) return;
            string n = scene.name;
            if (n == "Login" || n == "Town") return;      // 这两个场景有自己的真进度/遮罩流程
            if (!Application.isPlaying) return;
            // 被"调试钉住"时不要动它 —— 否则自动化截图刚钉住的画面会被这一下冲掉 ✗
            if (LoginLoading.IsPinned) return;
            LoginLoading.Begin("进入 " + n + " · 准备场景");
            LoginLoading.SetProgress(1f);
            LoginLoading.Complete();
        }

        private void Update()
        {
            LoginLoading.Tick();
        }

        private void OnGUI()
        {
            GUI.depth = -1000;          // 越小越靠上
            LoginLoading.Draw();
        }

        /// <summary>
        /// 截一张 PNG —— **这个口子挂在宿主上**，所以**跨场景也能截**：
        /// `LoginBootstrap` 一进战斗就被销毁了（单场景加载），而遮罩宿主还活着 ⇒
        /// "进战斗那张遮罩"只有这里能拍到 ✓（`ScreenCapture` 含 OnGUI）
        /// </summary>
        public string ShotNow(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "❌ 需要路径";
            string root = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".";
            string full = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(root, path);
            try
            {
                string dir = System.IO.Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            }
            catch (System.Exception e) { return "❌ 建目录失败：" + e.Message; }
            ScreenCapture.CaptureScreenshot(full);
            Log.Info(LoginLoading.Tag + "截图已排队 → " + full + "｜" + LoginLoading.Describe());
            return "截图已排队 → " + full;
        }

        /// <summary>读遮罩状态（跨场景可读）。</summary>
        public string Probe(string ignored) => LoginLoading.Describe();

        /// <summary>
        /// 把遮罩钉在某个进度（跨场景可调；`"1,进入 SeedfallArena"` / `"off"`）。
        /// 与 `LoginBootstrap.ForceLoading` 同一实现 —— 但**这个口子在战斗场景里还活着**，
        /// 所以"进战斗那张遮罩"可以在战场里重新钉住再截图 ✓
        /// </summary>
        public string Pin(string csv)
        {
            string text = (csv ?? "").Trim();
            if (text.Equals("off", System.StringComparison.OrdinalIgnoreCase))
            {
                LoginLoading.Reset();
                return "遮罩已关：" + LoginLoading.Describe();
            }
            string[] parts = text.Split(',');
            if (!float.TryParse(parts[0], out float p))
            {
                LoginLoading.Reset();
                return "需要一个 0..1 的数字，例：\"1,进入 SeedfallArena\"；\"off\" 关掉";
            }
            LoginLoading.DebugPin(p, parts.Length > 1 ? parts[1] : null);
            return LoginLoading.Describe();
        }
    }

    /// <summary>`LoginLoadingHost` 的建/取（幂等：找得到就不建第二个）。</summary>
    public static class LoginLoadingHostFactory
    {
        private static LoginLoadingHost _host;

        public static void Ensure()
        {
            if (_host != null) return;
            var go = new GameObject("LoginLoadingHost");
            _host = go.AddComponent<LoginLoadingHost>();
            Log.Info(LoginLoading.Tag + "遮罩宿主已建立（DontDestroyOnLoad，跨场景）");
        }
    }
}
