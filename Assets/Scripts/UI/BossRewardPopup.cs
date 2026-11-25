using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v213：BOSS 被击败时的**奖励动画**（种子掉落演出）★★
    ///
    /// 用户原话：「在战斗中，击败boss时会弹出奖励动画，掉落1个种子（主城种子）」
    ///           ＋「战斗里也要能看见"获得了什么"」。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是一个自己建自己的小组件，而不是往 `ImGuiHud` / `GameBootstrap` 里加
    /// ══════════════════════════════════════════════════════════════════════
    /// `ImGuiHud` 与 `SelfCheck` 本轮**禁止改动**；`GameBootstrap` 属于战斗侧的宿主，
    /// 往它的 `OnGUI` 里加一行就是第三个"战斗侧改动点"，与"只动两处"的要求冲突 ✗
    /// ⇒ 由 `TownDrops.OnBossKilled`（= ①，那个唯一的战斗侧接线点）调
    ///   <see cref="Push"/>，本类**第一次被需要时才**建一个同名 GameObject 挂上自己：
    ///     · 没有 `[RuntimeInitializeOnLoadMethod]` ⇒ 不进 play 就什么都不会建（对实机零影响）✓
    ///     · 不是 `DontDestroyOnLoad` ⇒ 离开战斗场景就跟着场景一起消失，不会在主城里冒出来 ✓
    ///     · `Application.isPlaying == false`（批处理 / 自检 / 编辑模式）⇒ **只留读数、不建物体** ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、演出的时间线（全部用 **unscaled** 时间 —— 升级抽卡会暂停游戏，卡片不能跟着冻住）
    /// ══════════════════════════════════════════════════════════════════════
    ///   0.00 ~ 0.34s  卡片从屏幕中心**放大**弹出：0.30 倍 → 1.08 倍（过冲）→ 1.00 倍
    ///   0.00 ~ 全程  品质光晕：外圈 5 层同色半透明方块，亮度按 sin 脉动
    ///   0.10 ~ 0.40s  名字/品质/成熟秒/金币每秒四行**依次淡入**（每行错开 0.06s）
    ///   2.80s         自动收（用户要求"2~3 秒后自动收"）
    ///   任何时刻点击  立刻收（用户要求"可点确认"）
    ///   多张排队      一张收完再放下一张（BossSchedule 的间隔 ≥ 8 分钟，正常不会撞，
    ///                 但探针会连推几张，排队比"后一张盖掉前一张"诚实）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、风格
    /// ══════════════════════════════════════════════════════════════════════
    /// 与 `TownHud` / `ImGuiHud` 同一套：奶油底 + 墨棕字 + 木色框 + 系统动态字体
    ///（工程里没有任何 .ttf，中文只能靠 `Font.CreateDynamicFontFromOSFont`，
    ///  缺这一步整张卡会全是方块）。按 1600×900 虚拟像素排布，逐坐标乘 `Screen.height/900`。
    /// **零 3D 资源**：整张卡都是 `GUI.DrawTexture` + `GUI.Label`，没有一处 `new Material` ✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossRewardPopup : MonoBehaviour
    {
        // ── 时间线（秒）──
        private const float AppearSeconds = 0.34f;
        private const float AutoCloseSeconds = 2.80f;
        private const float FadeInStart = 0.10f;
        private const float FadeInStep = 0.06f;
        private const float FadeInLen = 0.24f;
        private const float CloseFadeSeconds = 0.18f;

        // ── 卡片尺寸（未缩放的"卡内坐标"，单位 = 虚拟像素）──
        private const float CW = 640f;
        private const float CH = 400f;

        /// <summary>一张卡的内容。</summary>
        private sealed class Card
        {
            public int SeedIndex;
            public int Added;
            public string BossName = "";
            public float Age;          // unscaled 秒
            public bool Closing;
            public float CloseAge;
        }

        private static readonly List<Card> Queue = new List<Card>(4);
        private static BossRewardPopup _instance;

        // ── 探针/报告可读的静态读数 ──
        /// <summary>最近一次推送的种子下标（-1 = 没有）。</summary>
        public static int LastPushedSeed { get; private set; } = -1;
        /// <summary>累计推送过几张卡。</summary>
        public static int PushedTotal { get; private set; }
        /// <summary>正在演的那张卡还剩几秒（没有卡 = 0）。</summary>
        public static float TimeLeft => Queue.Count > 0 ? Mathf.Max(0f, AutoCloseSeconds - Queue[0].Age) : 0f;
        /// <summary>此刻屏上有卡在演。</summary>
        public static bool IsShowing => _instance != null && Queue.Count > 0;
        /// <summary>一句话状态（报告用）。</summary>
        public static string Describe()
            => $"奖励动画：累计推 {PushedTotal} 张｜排队 {Queue.Count}｜剩余 {(IsShowing ? TimeLeft.ToString("0.00") : "0.00")}s"
             + $"｜最近种子 #{LastPushedSeed}｜实例 {(_instance != null ? "在" : "无")}";

        // ── 绘制缓存（全工程一份）──
        private static Texture2D _white;
        private static Font _font;
        /// <summary>建字体那一刻的语言代数（≠ `L10n.FontGeneration` ⇒ 换字体，见 `Begin`）。</summary>
        private static int _fontGen = -1;
        private static readonly Dictionary<int, GUIStyle> Styles = new Dictionary<int, GUIStyle>();
        private float _k = 1f;
        private float _vw = 1600f, _vh = 900f;

        // ══════════════════════════════════════════════════════════════════
        // 推送
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 推一张奖励卡。**永不抛**；非 play 环境（批处理 / 自检 / 编辑模式）不建物体、只留读数。
        /// </summary>
        public static void Push(int seedIndex, int added, string bossName)
        {
            LastPushedSeed = seedIndex;
            PushedTotal++;

            if (!Application.isPlaying)
            {
                // 编辑模式 / 批处理：**故意不建 GameObject**（那会把物体留在没有 play 的场景里）
                Log.Info($"[奖励动画] 非 play 环境，只记录不播放：种子 #{seedIndex} ×{added}（{bossName}）");
                return;
            }
            if (seedIndex < 0 || TownData.SeedAt(seedIndex) == null)
            {
                Log.Warn($"[奖励动画] 种子下标 {seedIndex} 不合法，这张卡不播");
                return;
            }

            EnsureInstance();
            Queue.Add(new Card { SeedIndex = seedIndex, Added = added, BossName = bossName ?? "" });
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            // 先找场景里有没有别人建好的（防重入；本类自己的实例也可能活过一帧）
            // ★ 用 `FindAnyObjectByType` 而不是 `FindFirstObjectByType` ★
            //   后者在 6000.4 里已标记 obsolete（"依赖 InstanceID 排序"），用它会给本工程
            //   的警告清单多加一条 —— 而这一条的收益是零（这里只需要"有没有一个实例"）✓
            BossRewardPopup found = Object.FindAnyObjectByType<BossRewardPopup>();
            if (found != null) { _instance = found; return; }

            var go = new GameObject("BossRewardPopup");
            _instance = go.AddComponent<BossRewardPopup>();
        }

        /// <summary>立刻收掉所有卡（离开战斗 / 探针复位用）。</summary>
        public static void ClearAll()
        {
            Queue.Clear();
            if (_instance != null) Destroy(_instance.gameObject);
            _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // 计时
        // ══════════════════════════════════════════════════════════════════

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;      // ★ 升级抽卡会把 timeScale 打成 0，卡片不能跟着冻住
            if (Queue.Count == 0) return;

            Card c = Queue[0];
            if (c.Closing)
            {
                c.CloseAge += dt;
                if (c.CloseAge >= CloseFadeSeconds) Queue.RemoveAt(0);
                return;
            }
            c.Age += dt;
            if (c.Age >= AutoCloseSeconds) Close(c);
        }

        private static void Close(Card c)
        {
            if (c == null || c.Closing) return;
            c.Closing = true;
            c.CloseAge = 0f;
        }

        // ══════════════════════════════════════════════════════════════════
        // 绘制
        // ══════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (Queue.Count == 0) return;
            Card c = Queue[0];
            TownSeedDef def = TownData.SeedAt(c.SeedIndex);
            if (def == null) return;

            Begin();
            float anim = AnimOf(c);
            float alpha = c.Closing ? Mathf.Clamp01(1f - c.CloseAge / CloseFadeSeconds) : 1f;

            Color qc = QualityColor(def.grade);

            // ① 轻轻压暗全屏 —— 只有 0.18，不遮战场（2.8 秒就没了）
            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0.06f, 0.05f, 0.04f, 0.18f * alpha));

            // ② 卡片的"卡内坐标系"：以屏幕中心为锚，按 anim 缩放
            var center = new Vector2(_vw * 0.5f, _vh * 0.5f);
            Vector2 o = new Vector2(center.x - CW * anim * 0.5f, center.y - CH * anim * 0.5f);
            _o = o;
            _s = anim;

            // ③ 品质光晕：外圈 5 层同色方块，越外越淡；亮度按 sin 脉动
            float pulse = 0.55f + 0.45f * Mathf.Sin(c.Age * 7.2f);
            for (int i = 5; i >= 1; i--)
            {
                float pad = 7f * i;
                float a = (0.20f / i) * pulse * alpha;
                Fill(LR(-pad, -pad, CW + pad * 2f, CH + pad * 2f), new Color(qc.r, qc.g, qc.b, a));
            }

            // ④ 卡体
            Fill(LR(2f, 6f, CW, CH), new Color(0f, 0f, 0f, 0.22f * alpha));          // 投影
            Fill(LR(0f, 0f, CW, CH), Fade(Cream, alpha));
            Fill(LR(0f, 0f, CW, 10f), Fade(qc, alpha));                               // 顶部品质色带
            Frame(LR(0f, 0f, CW, CH), Fade(Wood, alpha), 4f);

            // ⑤ 四行内容依次淡入
            float a0 = RowAlpha(c, 0) * alpha;
            float a1 = RowAlpha(c, 1) * alpha;
            float a2 = RowAlpha(c, 2) * alpha;
            float a3 = RowAlpha(c, 3) * alpha;

            Text(LR(30f, 16f, 420f, 30f), "BOSS 奖励 · 主城种子", 18, Fade(InkSoft, a0),
                TextAnchor.MiddleLeft, true);
            Text(LR(CW - 210f, 16f, 180f, 30f), string.IsNullOrEmpty(c.BossName) ? "" : "击败 " + c.BossName,
                15, Fade(InkSoft, a0), TextAnchor.MiddleRight);

            // 种子色块（田里那株的颜色 ⇐ 与 HUD 同源）
            Rect icon = LR(30f, 56f, 92f, 92f);
            Fill(icon, Fade(TownData.TintAt(c.SeedIndex), a1));
            Frame(icon, Fade(Wood, a1), 3f);
            Text(icon, def.grade, 40, Fade(Ink, a1), TextAnchor.MiddleCenter, true);

            Text(LR(140f, 52f, 470f, 50f), def.name, 36, Fade(Ink, a1), TextAnchor.MiddleLeft, true);
            Text(LR(140f, 106f, 470f, 26f), $"品质 {def.grade} 档 · {def.level} 级", 18,
                Fade(qc, a1), TextAnchor.MiddleLeft, true);

            Fill(LR(30f, 166f, CW - 60f, 2f), Fade(CreamDark, a2));
            Text(LR(30f, 174f, 290f, 30f), $"成熟 {def.GrowSeconds:0} 秒", 20, Fade(Ink, a2), TextAnchor.MiddleLeft, true);
            Text(LR(320f, 174f, 290f, 30f), $"金币 {def.GoldPerSecond:0.0} / 秒", 20, Fade(Good, a2),
                TextAnchor.MiddleLeft, true);

            // ⑥ 落袋结论（**不静默**：仓库满就如实说没装下）
            string landed = c.Added > 0
                ? $"已存入主城仓库 +{c.Added}"
                : "主城仓库已满，这次没装下";
            Text(LR(30f, 218f, CW - 60f, 34f), landed, 22,
                Fade(c.Added > 0 ? Good : Bad, a3), TextAnchor.MiddleLeft, true);

            // ⑦ 倒计时条 + 提示
            float left = Mathf.Clamp01(1f - c.Age / AutoCloseSeconds);
            Fill(LR(30f, 330f, CW - 60f, 10f), Fade(CreamDark, alpha));
            Fill(LR(30f, 330f, (CW - 60f) * left, 10f), Fade(qc, alpha));
            Frame(LR(30f, 330f, CW - 60f, 10f), Fade(Wood, alpha), 1.5f);
            Text(LR(30f, 348f, CW - 60f, 26f), "点一下收取（2.8 秒后自动收）", 15,
                Fade(InkSoft, alpha), TextAnchor.MiddleCenter);

            // ⑧ 点一下就收
            if (Event.current.type == EventType.MouseDown) Close(c);
        }

        // ── 动画与淡入的小工具 ──

        /// <summary>弹出缩放：0.30 → 1.08（过冲）→ 1.00；收卡时按 close 进度缩回。</summary>
        private static float AnimOf(Card c)
        {
            float u = Mathf.Clamp01(c.Age / AppearSeconds);
            float s;
            if (u < 0.62f) s = Mathf.Lerp(0.30f, 1.08f, Mathf.SmoothStep(0f, 1f, u / 0.62f));
            else s = Mathf.Lerp(1.08f, 1.00f, Mathf.SmoothStep(0f, 1f, (u - 0.62f) / 0.38f));

            if (c.Closing)
            {
                float k = Mathf.Clamp01(c.CloseAge / CloseFadeSeconds);
                s *= Mathf.Lerp(1f, 0.94f, k);
            }
            return s;
        }

        /// <summary>第 <paramref name="row"/> 行的淡入进度（从 `FadeInStart + row*FadeInStep` 起算）。</summary>
        private static float RowAlpha(Card c, int row)
        {
            float t0 = FadeInStart + row * FadeInStep;
            return Mathf.Clamp01((c.Age - t0) / FadeInLen);
        }

        private static Color QualityColor(string grade)
        {
            switch ((grade ?? "").Trim().ToUpperInvariant())
            {
                case "A": return new Color(0.78f, 0.42f, 0.92f);   // 紫
                case "B": return new Color(0.30f, 0.55f, 0.90f);   // 蓝
                case "C": return new Color(0.28f, 0.66f, 0.38f);   // 绿
                default: return new Color(0.58f, 0.55f, 0.50f);    // D：灰
            }
        }

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(a));

        // ── 配色（与 TownHud 同一套奶油/木色）──

        private static readonly Color Cream = new Color(0.98f, 0.95f, 0.86f, 0.98f);
        private static readonly Color CreamDark = new Color(0.90f, 0.86f, 0.75f, 0.98f);
        private static readonly Color Ink = new Color(0.22f, 0.16f, 0.12f, 1f);
        private static readonly Color InkSoft = new Color(0.42f, 0.34f, 0.26f, 1f);
        private static readonly Color Wood = new Color(0.52f, 0.37f, 0.24f, 1f);
        private static readonly Color Good = new Color(0.30f, 0.60f, 0.28f, 1f);
        private static readonly Color Bad = new Color(0.80f, 0.28f, 0.24f, 1f);

        // ══════════════════════════════════════════════════════════════════
        // 基础绘制（虚拟像素 + 手动缩放，与 TownHud 同一手法）
        // ══════════════════════════════════════════════════════════════════

        private float _s = 1f;
        private Vector2 _o = Vector2.zero;

        /// <summary>卡内坐标（未缩放）→ 虚拟屏幕坐标。</summary>
        private Rect LR(float x, float y, float w, float h)
            => new Rect(_o.x + x * _s, _o.y + y * _s, w * _s, h * _s);

        private void Begin()
        {
            _k = Mathf.Clamp(Screen.height / 900f, 0.55f, 2.2f);
            _vw = Screen.width / _k;
            _vh = Screen.height / _k;
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            // ★ 字体跟着语言走（切语言 ⇒ 换字体 + 清按字号缓存的 GUIStyle，否则 ko/ur 空白）
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
            }
        }

        private Rect Sc(Rect vr) => new Rect(vr.x * _k, vr.y * _k, vr.width * _k, vr.height * _k);

        private void Fill(Rect vr, Color c)
        {
            if (c.a <= 0.001f) return;
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(Sc(vr), _white);
            GUI.color = prev;
        }

        private void Frame(Rect vr, Color c, float thickness)
        {
            Fill(new Rect(vr.x, vr.y, vr.width, thickness), c);
            Fill(new Rect(vr.x, vr.y + vr.height - thickness, vr.width, thickness), c);
            Fill(new Rect(vr.x, vr.y, thickness, vr.height), c);
            Fill(new Rect(vr.x + vr.width - thickness, vr.y, thickness, vr.height), c);
        }

        private void Text(Rect vr, string text, int size, Color color, TextAnchor anchor, bool bold = false)
        {
            if (string.IsNullOrEmpty(text) || color.a <= 0.003f) return;
            text = L10n.S(text);        // ★ 本地化：BOSS 奖励弹窗（"击败…" / "点一下收取"）在这里过表
            GUIStyle st = Style(size, anchor, bold);
            Color prevText = st.normal.textColor;
            st.normal.textColor = color;
            GUI.Label(Sc(vr), text, st);
            st.normal.textColor = prevText;
        }

        private GUIStyle Style(int size, TextAnchor anchor, bool bold)
        {
            int px = Mathf.Max(8, Mathf.RoundToInt(size * _s * _k));
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
    }
}
