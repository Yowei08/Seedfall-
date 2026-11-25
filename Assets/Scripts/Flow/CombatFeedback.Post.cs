using System;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v224：**战斗反馈的"炫酷"层**（用户原话：「为画面加一些炫酷的效果」）★★
    ///
    /// 只做两件事，**不改玩法、不加资产、不碰 `ImGuiHud`**：
    ///   ① **爆炸白闪**：爆炸那一瞬间全屏一层暖白（0.085s 线性渐隐，峰值 α 0.24×距离衰减）
    ///   ② **受击红晕/色差**：玩家掉血时四周泛红 + 全屏一点点红调（0.25s 渐隐）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么走 IMGUI（`GUI.DrawTexture` + `Texture2D.whiteTexture`）
    /// ══════════════════════════════════════════════════════════════════════
    /// ① 工程已有的全屏遮罩就是这条路：`UiEntrance.OnGUI`（`GUI.color` 染色 + `Texture2D.whiteTexture`
    ///    铺满 `Screen.width/height`）、`LoginLoading`（`GUI.depth = -1000` 盖在所有 OnGUI 之上）。
    ///    照着做 ⇒ **零资产、零 shader、零材质、零相机改动** ✓
    /// ② 备选的"在相机上挂全屏 Quad/Canvas"要新建材质或 Canvas，渲染顺序、URP 前后处理、
    ///    分辨率适配全是新的失败面；而屏效只有 0.1~0.25 秒 ⇒ 不值得为它开一套渲染路径 ✓
    /// ③ 工程里的 `SeedfallScreenOutline`（`Art/ScreenOutlineFeature.cs`）是 **URP RenderGraph
    ///    `ScriptableRendererFeature`**，它画的是"深度/法线突变的墨线"，参数只有
    ///    `_Strength / _NormalThreshold / _Thickness`（全屏描边用）——
    ///    **没有"红色渐隐/白闪"这种可驱动的输入**，要复用它就得改 shader 与 Feature
    ///    ⇒ 超出"只改这两个文件"的授权范围，故**不复用**，按上面这条路自己画 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、为什么红晕是"一圈套一圈的矩形"，不是一张渐变贴图
    /// ══════════════════════════════════════════════════════════════════════
    /// 贴图渐变要处理 **UV 上下方向**（`Texture2D` 的第 0 行在底、IMGUI 的 Rect 原点在左上），
    /// 而且本机没跑过 Unity（本轮不编译/不运行）⇒ 方向搞反了也没人能立刻发现 ✗
    /// 用 `VignetteBands` 层**嵌套矩形框**（每层只有一条边、纯白贴图染色）迭出来：
    ///   · 方向无关（四条边各自算，没有 UV）✓
    ///   · 每层 α 递减 ⇒ 从边上往里淡出 ✓
    ///   · 只有 0.25 秒、每帧 4×24+1 次 `GUI.DrawTexture`（≈100 个 IMGUI 批次，跟 HUD 一个量级）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、`GUI.depth = 2`：**盖住 3D、不盖住 HUD 的字**
    /// ══════════════════════════════════════════════════════════════════════
    /// IMGUI 里 `depth` **越小越靠上**（`LoginLoading` 用 -1000 压在最上层）。
    /// `ImGuiHud` 没有改过 `GUI.depth`（= 默认 0）⇒ 本层取 `2` ⇒ 画在 HUD 之下、
    /// 3D 画面之上：白闪不会把血条/弹字糊掉，这是有意的 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、时间口径：**一律未缩放**
    /// ══════════════════════════════════════════════════════════════════════
    /// boss 死亡时 `CombatFeedback.HitStopBoss` 会把 `timeScale` 压到 0.05；
    /// 屏效用 `Time.deltaTime` 就会变成"慢放 20 倍的闪光" ✗ ⇒ 全部用 `unscaledDeltaTime` ✓
    /// （与 `CombatFeedback.Tick` 同一个纪律）
    ///
    /// ⚠️ 本类不读也不写 `World.Rng` ⇒ 一局的确定性不变 ✓
    /// ⚠️ 编辑模式（`Application.isPlaying == false`）**不画、不计时** ⇒ 自检跑战斗路径也不会多出任何东西 ✓
    /// </summary>
    public static class CombatFeedbackScreen
    {
        // ── ① 爆炸白闪 ──
        /// <summary>白闪时长（秒，未缩放）。要求 0.06~0.10，取中偏长一点：0.085。</summary>
        public const float FlashSeconds = 0.085f;
        /// <summary>白闪峰值 α（要求 0.18~0.28，取 0.24）。远端爆炸会按距离衰减再打折。</summary>
        public const float FlashAlpha = 0.24f;
        /// <summary>暖白（纯白会发灰、发冷；+3% 红、-10% 蓝读起来像"爆光的余温"）。</summary>
        public static readonly Color FlashColor = new Color(1.00f, 0.97f, 0.90f, 1f);

        // ── ② 受击红晕 / 色差 ──
        /// <summary>红晕时长（秒，未缩放）。用户要求 0.25s 渐隐。</summary>
        public const float HurtSeconds = 0.25f;
        /// <summary>最外圈的峰值 α（往里逐层递减到 0）。</summary>
        public const float HurtEdgeAlpha = 0.55f;
        /// <summary>全屏红调（"色差"那一半）：只要一点点，0.07 已经明显但不会挡住画面。</summary>
        public const float HurtTintAlpha = 0.07f;
        /// <summary>暗红（比纯红低一档亮度 ⇒ 是"血光"而不是"警报灯"）。</summary>
        public static readonly Color HurtColor = new Color(0.86f, 0.11f, 0.09f, 1f);
        /// <summary>红晕分几层（层数越多越平滑，代价是 IMGUI 批次）。</summary>
        public const int VignetteBands = 24;
        /// <summary>红晕的总宽度 = 短边 × 这个比例。</summary>
        public const float VignetteSpan = 0.22f;

        /// <summary>玩家累计承伤的"涨了才算受伤"的门槛（`World.DamagedByEnemies` 是 float）。</summary>
        private const float DamageEpsilon = 0.0001f;

        // ── 状态 ──
        private static float _flashLeft;
        private static float _flashTotal = FlashSeconds;
        private static float _flashStrength = 1f;
        private static float _hurtLeft;
        private static float _hurtTotal = HurtSeconds;
        private static float _lastPlayerDamage = -1f;

        /// <summary>白闪触发过几次（读数用）。</summary>
        public static int FlashCount { get; private set; }
        /// <summary>红晕触发过几次（读数用）。</summary>
        public static int HurtCount { get; private set; }

        /// <summary>这一帧有没有东西要画（没有就直接不进 `OnGUI` 的绘制分支）。</summary>
        public static bool Active => _flashLeft > 0f || _hurtLeft > 0f;

        /// <summary>当前白闪 α（0..1）；探针可读。</summary>
        public static float FlashAlphaNow =>
            _flashLeft <= 0f ? 0f : FlashAlpha * _flashStrength * (_flashLeft / Mathf.Max(0.001f, _flashTotal));

        /// <summary>当前红晕 α（0..1，最外圈）；探针可读。</summary>
        public static float HurtAlphaNow =>
            _hurtLeft <= 0f ? 0f : HurtEdgeAlpha * (_hurtLeft / Mathf.Max(0.001f, _hurtTotal));

        /// <summary>
        /// 请一次爆炸白闪。`strength` = 距离衰减系数（0.35~1.00）——
        /// 炸在脚边是满闪，炸在屏幕另一头只是"亮了一下" ✓
        /// </summary>
        public static void RequestFlash(float strength)
        {
            if (!Application.isPlaying) return;        // 编辑模式：不画
            _flashLeft = FlashSeconds;
            _flashTotal = FlashSeconds;
            _flashStrength = Mathf.Clamp(strength, 0.2f, 1f);
            FlashCount++;
        }

        /// <summary>请一次受击红晕（玩家掉血时）。</summary>
        public static void RequestHurt()
        {
            if (!Application.isPlaying) return;
            _hurtLeft = HurtSeconds;
            _hurtTotal = HurtSeconds;
            HurtCount++;
        }

        /// <summary>推进两个计时（**未缩放**）。由 `CombatFeedbackFxHost.LateUpdate` 每帧调一次。</summary>
        internal static void Tick(float unscaledDt)
        {
            if (unscaledDt < 0f) unscaledDt = 0f;
            if (_flashLeft > 0f) _flashLeft = Mathf.Max(0f, _flashLeft - unscaledDt);
            if (_hurtLeft > 0f) _hurtLeft = Mathf.Max(0f, _hurtLeft - unscaledDt);
        }

        /// <summary>
        /// 喂当前这一局的**玩家累计承伤**（`World.DamagedByEnemies`）。只在"涨了"时触发红晕。
        ///
        /// 为什么不订阅事件：玩家掉血在 `World.DealDamage` 里结算，没有一条"玩家受伤"的 Bus 事件
        /// （查证：`grep DamagedByEnemies` 只有累加与读数两处）。而它是**单调累加**的
        /// ⇒ 每帧比一下就够了，零侵入、不改 `RunSession` ✓
        /// 换局时它会**掉回 0** ⇒ 这里把基线拉回去、且**不**触发红晕（否则一开新局就红一下）✓
        /// </summary>
        internal static void OnPlayerDamageTotal(float total)
        {
            if (total < 0f) return;                                       // 没有活会话
            if (_lastPlayerDamage < 0f) { _lastPlayerDamage = total; return; }   // 首次采样：只对齐基线
            if (total < _lastPlayerDamage - DamageEpsilon) { _lastPlayerDamage = total; return; } // 换局/重开
            if (total > _lastPlayerDamage + DamageEpsilon)
            {
                _lastPlayerDamage = total;
                RequestHurt();
            }
        }

        /// <summary>换局/重开时把屏效状态清干净。</summary>
        public static void Reset()
        {
            _flashLeft = 0f;
            _hurtLeft = 0f;
            _flashStrength = 1f;
            _lastPlayerDamage = -1f;
        }

        /// <summary>把这一帧的屏效画出来。**必须在 `OnGUI` 里调**（`GUI.*` 只在 OnGUI 内有效）。</summary>
        internal static void Draw()
        {
            DrawFlash();
            DrawHurt();
        }

        // ── 白闪：一整块全屏矩形 ──
        private static void DrawFlash()
        {
            if (_flashLeft <= 0f) return;
            float k = _flashLeft / Mathf.Max(0.001f, _flashTotal);
            float a = FlashAlpha * _flashStrength * k;
            if (a <= 0.002f) return;

            Color save = GUI.color;
            GUI.color = new Color(FlashColor.r, FlashColor.g, FlashColor.b, a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = save;
        }

        // ── 红晕：全屏红调 + 四条边逐层嵌套的矩形框 ──
        private static void DrawHurt()
        {
            if (_hurtLeft <= 0f) return;
            float k = _hurtLeft / Mathf.Max(0.001f, _hurtTotal);
            // 平方包络：前半段退得快、尾巴几乎看不见 ⇒ 读起来是"一下血红、马上收"
            float env = k * k;

            Color save = GUI.color;
            float w = Screen.width;
            float h = Screen.height;

            // ① 全屏色差（很淡的一层红）
            float tint = HurtTintAlpha * env;
            if (tint > 0.002f)
            {
                GUI.color = new Color(HurtColor.r, HurtColor.g, HurtColor.b, tint);
                GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            }

            // ② 贴边红晕：`VignetteBands` 层嵌套的"口"字框，越靠外越浓
            float span = Mathf.Min(w, h) * VignetteSpan;
            if (span > 1f)
            {
                float thickness = span / VignetteBands;
                for (int i = 0; i < VignetteBands; i++)
                {
                    float t = i / (float)VignetteBands;               // 0 = 最外圈
                    float a = HurtEdgeAlpha * env * (1f - t);
                    if (a <= 0.002f) break;
                    GUI.color = new Color(HurtColor.r, HurtColor.g, HurtColor.b, a);

                    float o = i * thickness;                          // 离屏幕边的距离
                    float iw = w - o * 2f;                            // 这一圈的内宽/内高
                    float ih = h - o * 2f;
                    if (iw <= 0f || ih <= 0f) break;

                    GUI.DrawTexture(new Rect(o, o, iw, thickness), Texture2D.whiteTexture);            // 上
                    GUI.DrawTexture(new Rect(o, h - o - thickness, iw, thickness), Texture2D.whiteTexture); // 下
                    GUI.DrawTexture(new Rect(o, o, thickness, ih), Texture2D.whiteTexture);            // 左
                    GUI.DrawTexture(new Rect(w - o - thickness, o, thickness, ih), Texture2D.whiteTexture); // 右
                }
            }

            GUI.color = save;
        }
    }

    /// <summary>
    /// 屏效宿主：**播放一开始就自己起一个**（`RuntimeInitializeOnLoadMethod`），不依赖场景里有没有人挂它。
    ///
    /// 为什么不用 `CombatFeedbackRig` 兼任：
    ///   那个 rig 是"第一次抖屏"时才 `EnsureRig` 造出来的（见 `CombatFeedback.EnsureRig`），
    ///   而**受击红晕可能比第一次爆炸更早**（玩家先挨一下）⇒ 不能等它 ✓
    /// 为什么放在这里而不是改场景：本轮只允许动 `Flow/CombatFeedback*.cs`
    ///   ⇒ 用 `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` 在运行时自建，零场景改动 ✓
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class CombatFeedbackFxHost : MonoBehaviour
    {
        private Action<EnemyAttackFieldEvent> _onField;
        private Action<FertilizerBlastEvent> _onBlast;
        private bool _subscribed;

        /// <summary>进 play 模式后（首场景加载完）自动建宿主。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("CombatFeedbackFx");
            DontDestroyOnLoad(go);                       // 跨场景活一整个 play 会话
            go.AddComponent<CombatFeedbackFxHost>();
        }

        private void Awake()
        {
            Subscribe();
        }

        /// <summary>
        /// 订阅两种"有爆心"的既有事件（**只读、不改任何逻辑**）：
        ///   · `EnemyAttackFieldEvent`：`RunSession.BlastDamageAt` 主爆结算末尾发的就是它
        ///   · `FertilizerBlastEvent`：肥料那一路的爆点（同一个 center）
        /// `CombatFeedback.NoteBlastOrigin` 只在"本帧有一记爆炸正等着爆心"时收下
        /// ⇒ 敌人自己的范围技事件不会被误当成玩家这次爆炸的爆心 ✓
        /// </summary>
        private void Subscribe()
        {
            if (_subscribed) return;
            _onField = OnField;
            _onBlast = OnBlast;
            Bus.Subscribe(_onField);
            Bus.Subscribe(_onBlast);
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (!_subscribed) return;
            Bus.Unsubscribe(_onField);
            Bus.Unsubscribe(_onBlast);
            _subscribed = false;
        }

        private static void OnField(EnemyAttackFieldEvent e) => CombatFeedback.NoteBlastOrigin(e.Pos);
        private static void OnBlast(FertilizerBlastEvent e) => CombatFeedback.NoteBlastOrigin(e.Pos);

        /// <summary>
        /// 每帧一次：推进屏效计时 + 采样玩家承伤。
        /// 用 `LateUpdate` 而不是 `Update`：`CombatFeedback.Tick` 也在 `LateUpdate`
        /// （rig 是 `[DefaultExecutionOrder(30000)]`，本宿主 32000 ⇒ 之后的那个先跑完再轮到它）
        /// ⇒ 本帧的爆炸白闪请求一定已经发出，`OnGUI` 一定画得出来 ✓
        /// </summary>
        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            CombatFeedbackScreen.Tick(Time.unscaledDeltaTime);
            CombatFeedbackScreen.OnPlayerDamageTotal(ReadPlayerDamage());
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (!CombatFeedbackScreen.Active) return;
            // IMGUI 一帧会被调好几次（Layout / Repaint / 各种输入事件）⇒ 只画 Repaint 那一趟
            if (Event.current != null && Event.current.type != EventType.Repaint) return;

            Matrix4x4 prevMatrix = GUI.matrix;
            int prevDepth = GUI.depth;
            Color prevColor = GUI.color;
            try
            {
                // `ImGuiHud.Draw` 会把 `GUI.matrix` 设成"虚拟分辨率缩放"再还原；
                // 这里显式置单位矩阵 ⇒ 本层用的是**真屏幕像素**，与 HUD 的缩放无关 ✓
                GUI.matrix = Matrix4x4.identity;
                // 2 = 在 HUD(0) 之下、3D 画面之上（IMGUI 里 depth 越小越靠上）✓
                GUI.depth = 2;
                CombatFeedbackScreen.Draw();
            }
            finally
            {
                GUI.color = prevColor;
                GUI.matrix = prevMatrix;
                GUI.depth = prevDepth;
            }
        }

        /// <summary>当前这一局玩家的累计承伤；没有活会话返回 -1（`OnPlayerDamageTotal` 会忽略它）。</summary>
        private static float ReadPlayerDamage()
        {
            var live = GameBootstrap.Live;
            for (int i = 0; i < live.Count; i++)
            {
                GameBootstrap b = live[i];
                if (b == null) continue;
                RunSession s = b.Session;
                if (s == null || s.World == null) continue;
                return s.World.DamagedByEnemies;
            }
            return -1f;
        }

        /// <summary>桥/探针入口：屏效读数（`unity_drive send CombatFeedbackFx CombatFeedbackFxHost Report`）。</summary>
        public string Report()
            => "COMBATFEEDBACKSCREEN"
             + "｜白闪 α=" + CombatFeedbackScreen.FlashAlphaNow.ToString("0.000")
             + "（峰值 " + CombatFeedbackScreen.FlashAlpha.ToString("0.00") + "，时长 " + CombatFeedbackScreen.FlashSeconds.ToString("0.000") + "s，共 " + CombatFeedbackScreen.FlashCount + " 次）"
             + "｜红晕 α=" + CombatFeedbackScreen.HurtAlphaNow.ToString("0.000")
             + "（峰值 " + CombatFeedbackScreen.HurtEdgeAlpha.ToString("0.00") + "，时长 " + CombatFeedbackScreen.HurtSeconds.ToString("0.00") + "s，共 " + CombatFeedbackScreen.HurtCount + " 次）"
             + "｜active=" + CombatFeedbackScreen.Active
             + "｜GUI.depth=2（画在 HUD(0) 之下、3D 之上，见文件头第三节）";
    }
}
