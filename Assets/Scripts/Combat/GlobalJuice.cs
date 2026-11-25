using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Seedfall
{
    /// <summary>
    /// ★★ v261：**全局"炫酷动态"层** —— 只做视觉，不改玩法 ★★
    ///
    /// 用户原话：「全局效果加强，增加更多炫酷的动态」。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 零、这一轮**只新增这一个文件**（四个已交付的特效文件一行都没动）
    /// ══════════════════════════════════════════════════════════════════════
    /// `AttackVfx.cs` / `PluginHitFx.cs` / `CombatSunDisc.cs` / `CombatFeedback.Post.cs`
    /// 是别人刚交付的 ⇒ 本文件**只读**它们已经 `public` 的读数（`CombatFeedbackScreen.HurtColor`
    /// / `FlashColor` / `CombatFeedback.LastExplosionPeak` / `HitStopCount`），不订阅、不改写 ✗
    ///
    /// 与既有层的层次关系（避免"同一个视觉效果做两遍"）：
    ///   · 已有 **震屏 ****（trauma²）/ **爆炸白闪**（全屏）/ **受伤红晕**（vignette 嵌套框）
    ///     / **弹字** / **刀光** / **激光** / **冲击环 + 烟雾** / **晴日太阳** —— 全部保留不动；
    ///   · 本文件补的是它们**没有的那一类**：**屏边脉冲光带**（B 段）、**地面微风微粒**（C 段）
    ///     ＋ 大爆炸的**慢动作强调**（D 段，复用既有 hitstop）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、结构：一个自托管宿主 + 两条独立的视觉通道
    /// ══════════════════════════════════════════════════════════════════════
    /// ```
    /// [DontDestroyOnLoad] GLOBAL_JUICE   ← 本文件的唯一宿主（Instances 恒为 1）
    ///    ├ LateUpdate：采样承伤 / 爆炸计数 → 触发脉冲与慢动作（**未缩放时间**）
    ///    ├ OnGUI     ：屏边四条渐变光带（GUI.depth = 3 ⇒ 在既有屏效(2) 与 HUD(0) **之下**）
    ///    └ WindRig   ：`WindCount` 个极小方块（**启动时建一次，之后只改 pos/scale/rot**）
    /// ```
    /// 自举方式与 `PlayerFacingMarker` / `CombatFeedbackFxHost` 同一套：
    /// `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` + `DontDestroyOnLoad` ⇒ **零场景改动** ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、屏边脉冲为什么也用"嵌套矩形框"而不是渐变贴图
    /// ══════════════════════════════════════════════════════════════════════
    /// 与 `CombatFeedbackScreen.DrawHurt` 同一条结论（`CombatFeedback.Post.cs` 文件头第二节）：
    /// 贴图渐变要处理 `Texture2D` 第 0 行在**底**、IMGUI 的 Rect 原点在**左上**这层翻转，
    /// 而本机这一轮**不跑 Unity**（只 `dotnet build`）⇒ 方向搞反了没人能当场发现 ✗
    /// 嵌套矩形**没有 UV**：四条边各自算、每层 α 递减 ⇒ 从边到内天然淡出 ✓
    /// **v262：`PulseBands` 10 → 5** ⇒ 一条边 5 层、四条边 **20 次** `GUI.DrawTexture`/帧
    /// （改前 40 次；既有红晕是 4×24+1 = 97 次，`CombatFeedback.Post.cs:35`）。
    /// 层数砍半 visually 几乎无损：α 按 (1-t)² 递减，第 6~10 层的 α 已经 < 峰值的 16%
    /// 且外侧被更亮的层完全覆盖；而 DrawTexture 的每一次都是一份**全屏宽/高的四边形**
    /// （上/下边 w×thickness、左右边 thickness×h）⇒ 砍下来的正是屏边像素覆盖量的一半 ✓
    ///
    /// **`GUI.depth = 3`**（IMGUI 里 depth **越小越靠上**）：HUD = 0、既有屏效 = 2、本层 = 3
    /// ⇒ 光带压在 3D 画面之上，但**不会盖住** HUD 的字，也不会和既有白闪/红晕抢最上层 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、强度来源：**每帧比一下 `World.DamagedByEnemies`**（零事件、零侵入）
    /// ══════════════════════════════════════════════════════════════════════
    /// 玩家承伤没有一条 Bus 事件（`grep DamagedByEnemies` 只有累加与读数两处），
    /// 而它**单调累加** ⇒ 每帧做差就能拿到"这一下掉了多少"，与 `CombatFeedbackScreen`
    /// 的取法**逐字相同**（`ReadPlayerDamage` → `GameBootstrap.Live[i].Session.World`）。
    /// 差出来的每一笔进一个**定长环形缓冲**（`DamageSlots` 个），每帧把"1 秒内的"求和
    /// ⇒ 就是需求里的"最近 1 秒内伤害量"，**零分配**（数组建一次，永不 new）✓
    /// 换局时它**掉回 0** ⇒ 环清空 + 基线拉回、**不**触发脉冲（否则一开新局闪一下）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、慢动作：**v262 已关闭（"爆炸卡顿"的主因）**
    /// ══════════════════════════════════════════════════════════════════════
    /// ── 用户报告：「爆炸出现了异常卡顿」→ 取证结论就是这个分支 ──
    /// 本文件 v261 曾在大爆炸时调 `CombatFeedback.HitStopBoss()`。下面四个数一乘就知道它必然
    /// **每次爆炸都触发**，且频率远高于那条 1.5s 冷却能拦住的范围：
    ///   · `LastExplosionPeak`（`CombatFeedback.cs:372`）= `1.0² × ExplosionAmp 0.42` × 距离衰减
    ///     ⇒ 满额 **0.42**；`BigBlastPeak` = **0.30** ⇒ 只要衰减系数 ≥ 0.30/0.42 = **0.714** 就触发；
    ///   · 衰减到 0.714 对应的爆心距离 = `BlastNear 4m` 起算的 **≈10.4m**（`CombatFeedback.cs:220`
    ///     的 lerp/smoothstep）⇒ 而肥料 `range` = **6.5m**（`tuning.json:172`）
    ///     ⇒ **玩家扔出的每一记主爆都在门槛以内（6.5m 处 atten ≈ 0.945 ⇒ 峰值 ≈ 0.397 ≥ 0.30）**；
    ///   · 肥料 `cooldown` = **1.2s**（`tuning.json:170`），而 `SlowMoCooldown` = **1.5s**
    ///     ⇒ 这条"防滥用冷却"**只能隔一次拦一次**，实际节奏是"每 2.4 秒冻 0.32 秒"；
    ///   · 0.32s / 2.4s = **13.3% 的墙钟时间停在 `timeScale = 0.05`**，而 `AttackVfx.Tick(dt)`
    ///     用的是**缩放后**的 `dt`（`AttackVfx.cs:1138` `if (dt <= 0f) return;`）
    ///     ⇒ 爆炸烟雾在爆开的第一帧就被冻住 0.32 秒再动 ⇒ 玩家读到的正是"爆炸卡住了一下"。
    /// ⇒ **结论：hitstop 是给"Boss 击杀"那一个叙事节拍用的**（`RunSession.cs:2758` 仍在调它），
    ///   不能挂在一个 1.2 秒就重复一次的常规动作上。本文件**不再请求 hitstop**
    ///   （`SlowMoOnBigBlast = false`），只保留读数与"宿主销毁时不留冻结"的兜底。
    /// 为什么不选"每局首次重爆炸一次"：那只是把 13.3% 的冻结换成"开局第一次投肥突然卡一下"，
    ///   仍然是个**玩家无法预期、也无法从画面里读出意义**的停顿 ⇒ 不如彻底交回原有逻辑 ✓
    /// **恢复保证**：`OnDisable` / `OnDestroy` 里查一次 —— 若 `Time.timeScale` 还停在 hitstop
    ///    那一档，就写回 `1f`（正常路径由 `CombatFeedback.Tick` 的 unscaled 计时恢复）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、地面微风微粒：**启动时建一次，运行期零 `Instantiate` / 零 `Destroy`**
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 数量 `WindCount = 12`（需求 8~14）；每颗是一份**共享网格**（1 个四边形 4 顶点）
    ///     ＋ 共享材质 ⇒ 12 个 `MeshRenderer`（同屏新增渲染体 = 12，上限 40）✓
    ///   · 行为：沿固定风向水平漂移 + `sin` 上下浮动；**绕玩家在一个矩形盒里 wrap**
    ///     ⇒ 永远在玩家附近（= 永远在画面里），不需要"出屏就销毁重建" ✓
    ///   · `renderQueue = 2100`（**背景级**：紧跟不透明队列之后、远在既有特效的 3000 之前）、
    ///     `shadowCastingMode = Off`、`receiveShadows = false`、**无任何 Collider** ✓
    ///   · 只在**战斗场景**（场景名含 `Arena`）显示；主城/登录把它 `SetActive(false)`（每帧零写入）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 六、性能账（交付时按这个核对；★ = v262 改过的数）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 宿主 **恒 1 个**（DDOL；`Boot` 与 `Awake` 双重防重入，见第七节）
    ///   · 微粒 12 个渲染体；**网格 1 份、材质 2 份，建一次**（静态缓存）
    ///   · 运行期 `Instantiate` / `Destroy` / `new` = **0**（数组与 GameObject 全在 `Awake` 建好）
    ///   · 每帧分配 ≈ **0**（没有字符串拼接、没有 LINQ、没有闭包、没有 `GetComponentsInChildren`）
    ///   · 每帧写入：12 次 position / 12 次 rotation（只在战斗场景且 `_windOn`）
    ///   · ★ 脉冲：`PulseBands` **5** × 4 边 = **20** 次 `GUI.DrawTexture`/帧（改前 40）
    ///     ★ 且**只在 `_pulseLeft > 0` 的 0.2 秒里**画，其余帧 `OnGUI` 直接返回；
    ///     ★ 且**受伤 / 爆炸共用一条脉冲通道**（`MergeRestartRatio`），不会同时叠两条 ⇒
    ///       连续受击也不会把 0.2s 无限续期（改前每挨一下都重置计时 ⇒ 挨打期间**每帧 40 次**）
    ///   · ★ 慢动作 = **0 次对 `Time.timeScale` 的影响**（爆炸不再调 `HitStopBoss`）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 七、宿主防重入（v262 新增）
    /// ══════════════════════════════════════════════════════════════════════
    /// `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 每次进 play 只跑一次
    /// （`ProjectSettings/EditorSettings.asset` 的 `m_EnterPlayModeOptions: 0` ⇒ 域重载照常，
    ///  静态字段每局归零），**但**这条保证靠的是工程设置，不是本文件自己的纪律：
    ///   · `Boot` 先查静态 `_host`，再查场景里有没有同名 `GLOBAL_JUICE`（残留自上一轮）；
    ///   · `Awake` 里若已有别的宿主 ⇒ 自我 `Destroy`，**绝不做第二份 12 颗微粒**；
    ///   ⇒ `GlobalJuice.Instances` 恒为 1（`Status()` 会印出来）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 八、没做的那一项（需求 ④ UI 呼吸）—— 明确跳过
    /// ══════════════════════════════════════════════════════════════════════
    /// 顶部时间栏 / BOSS 血条由 `ImGuiHud`（**IMGUI，`OnGUI` 里 `GUI.color` 逐层染色**）绘制，
    /// 条体颜色与矩形是它 `OnGUI` 里的**局部变量**，没有任何 `public` 的"整体亮度"输入口
    /// （`grep GUI.color ImGuiHud.cs` 全是画时现算）。
    /// ⇒ 不改 HUD 文件就**没有**能让它整体 ×0.97~1.03 的入口；按需求"做不了就跳过并说明"处理 ✗
    ///
    /// ⚠️ 本文件不读也不写 `World.Rng`；不碰伤害/移动/命中判定 ⇒ 一局的确定性不变 ✓
    /// </summary>
    public static class GlobalJuice
    {
        // ══════════════════════════════════════════════════════════════════
        // ① 屏边脉冲（战斗强度反馈）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>一次脉冲的时长（秒，未缩放）。</summary>
        public const float PulseSeconds = 0.20f;
        /// <summary>光带宽度占屏幕短边的**下限**（弱脉冲）。需求 0.02~0.05。</summary>
        public const float PulseWidthMin = 0.020f;
        /// <summary>光带宽度占屏幕短边的**上限**（满强度）。</summary>
        public const float PulseWidthMax = 0.050f;
        /// <summary>峰值 α（贴边那一层）。既有红晕是 0.55；脉冲是"一条带"不是"整圈雾"，取 0.52。</summary>
        public const float PulseMaxAlpha = 0.52f;
        /// <summary>一条边分几层（层数越多越像渐变，代价是 GUI 批次）。v262：10 → **5**。</summary>
        public const int PulseBands = 5;
        /// <summary>
        /// ★ v262：一条脉冲通道的**合并阈值** ★
        /// 已经在响的时候，只有"强度 ≥ 当前 × 1.15"才重开计时；弱的那一路请求直接丢弃。
        /// 改前：受伤与爆炸各写一份状态、每次都把 `_pulseLeft` 重置成 0.2s
        /// ⇒ 连续受击期间脉冲**永不结束** ⇒ 每帧 40 次 `GUI.DrawTexture` 常驻。
        /// </summary>
        public const float MergeRestartRatio = 1.15f;
        /// <summary>强度曲线指数：&gt;1 ⇒ 弱脉冲收得更快，读起来"有明确的主次"。</summary>
        public const float PulseFalloff = 1.60f;
        /// <summary>"最近 1 秒伤害量"到满强度的分母（伤害单位）。取 40：一记普通受击 ≈ 0.2~0.4 强度。</summary>
        public const float DamageCap = 40f;
        /// <summary>强度下限：再小的一下也要看得见（不发光带等于没做）。</summary>
        public const float PulseFloor = 0.28f;

        /// <summary>"最近 1 秒"窗口（秒）。</summary>
        public const float DamageWindow = 1.0f;
        /// <summary>承伤环形缓冲的槽数（1 秒内最多记这么多笔，超出覆盖最旧的）。</summary>
        public const int DamageSlots = 24;
        /// <summary>承伤"涨了才算"的门槛（float 累加的比较容差，与 `CombatFeedbackScreen` 同口径）。</summary>
        private const float DamageEpsilon = 0.0001f;

        // ══════════════════════════════════════════════════════════════════
        // ② 慢动作（v262：**已关闭**，见文件头第四节）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// ★ v262：爆炸是否请求慢动作。**false = 完全交回原有逻辑（只有 Boss 击杀 stop）** ★
        /// 置回 `true` 就恢复 v261 的行为（每 2.4 秒冻 0.32s，实测就是用户报的"爆炸卡顿"）。
        /// </summary>
        public const bool SlowMoOnBigBlast = false;
        /// <summary>触发慢动作的最低爆炸峰值（世界单位，= `trauma²×幅度` 的**衰减后**值）。</summary>
        public const float BigBlastPeak = 0.30f;
        /// <summary>两次慢动作之间的最短间隔（秒，未缩放时间）。</summary>
        public const float SlowMoCooldown = 1.50f;

        // ══════════════════════════════════════════════════════════════════
        // ③ 地面微风微粒
        // ══════════════════════════════════════════════════════════════════
        /// <summary>微粒数量（需求 8~14）。</summary>
        public const int WindCount = 12;
        /// <summary>wrap 盒的半宽（米）：玩家左右各 11m。</summary>
        public const float WindBoxHalfX = 11f;
        /// <summary>wrap 盒的半深（米）：玩家前后各 6m。</summary>
        public const float WindBoxHalfZ = 6f;
        /// <summary>离地最低高度（米，相对玩家根）。</summary>
        public const float WindMinY = 0.10f;
        /// <summary>离地最高高度（米）。</summary>
        public const float WindMaxY = 1.10f;
        /// <summary>微小方块的边长范围（米）—— "极小"，远了看不见也不该抢戏。</summary>
        public const float WindMinSize = 0.045f;
        public const float WindMaxSize = 0.100f;
        /// <summary>水平漂移速度范围（米/秒）—— 缓慢。</summary>
        public const float WindMinSpeed = 0.16f;
        public const float WindMaxSpeed = 0.42f;
        /// <summary>上下浮动的幅度范围（米）与角频率（弧度/秒）。</summary>
        public const float WindBobMin = 0.04f;
        public const float WindBobMax = 0.14f;
        public const float WindBobOmegaMin = 0.55f;
        public const float WindBobOmegaMax = 1.30f;
        /// <summary>风向（世界 XZ 平面内，单位化后使用）。</summary>
        public static readonly Vector3 WindDir = new Vector3(0.92f, 0f, 0.39f).normalized;
        /// <summary>背景级透明队列：紧跟不透明队列（2000）之后、远早于既有特效（3000）。</summary>
        public const int WindQueue = 2100;
        /// <summary>微粒 α（两档交替，避免一整片同色"像脏点"）。</summary>
        public const float WindAlphaA = 0.17f;
        public const float WindAlphaB = 0.10f;

        /// <summary>战斗场景名判据（`PlayerFacingMarker` 文件头第一节写明战斗场景是 `SeedfallArena`）。</summary>
        public const string ArenaSceneKey = "Arena";
        /// <summary>找不到玩家时的重试周期（秒）—— 别每帧 `GameObject.Find`。</summary>
        public const float AcquireRetrySeconds = 0.5f;

        // ══════════════════════════════════════════════════════════════════
        // 读数（探针 / 交付报告用；与 `PlayerFacingMarker.Status` 同一套风格）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>存活宿主数。**必须恒为 1**。</summary>
        public static int Instances;
        /// <summary>脉冲触发过几次。</summary>
        public static int PulseCount;
        /// <summary>最近一次脉冲的峰值 α（读数用）。</summary>
        public static float LastPulseAlpha;
        /// <summary>最近一次脉冲的强度（0..1）与宽度（像素）。</summary>
        public static float LastPulseStrength;
        public static float LastPulseWidthPx;
        /// <summary>"最近 1 秒伤害量"的当前值（读数用）。</summary>
        public static float LastDamageWindow;
        /// <summary>慢动作被本文件请求过几次（计数的是对 `HitStopBoss` 的调用）。</summary>
        public static int SlowMoRequests;
        /// <summary>最近一次慢动作的理由（读数用）。</summary>
        public static string LastSlowMoReason = "(无)";
        /// <summary>微粒的渲染体数量与那份网格/材质的建立次数（必须各为 1）。</summary>
        public static int WindRenderers;
        public static int WindMeshBuilds;
        public static int WindMaterialBuilds;
        /// <summary>微粒当前是否可见（只在战斗场景为真）。</summary>
        public static bool WindVisible;
        /// <summary>当前跟随的目标名（"(无)" = 还没找到玩家）。</summary>
        public static string LastTarget = "(无)";

        // ══════════════════════════════════════════════════════════════════
        // 状态
        // ══════════════════════════════════════════════════════════════════
        // ── 脉冲 ──
        private static float _pulseLeft;
        private static float _pulseTotal = PulseSeconds;
        private static float _pulseStrength = 1f;
        private static Color _pulseColor = Color.white;
        // ── 承伤窗口（定长环形缓冲，**零分配**）──
        private static readonly float[] _dmgTime = new float[DamageSlots];
        private static readonly float[] _dmgAmt = new float[DamageSlots];
        private static int _dmgHead;
        private static float _lastPlayerDamage = -1f;
        private static int _lastExplosionCount;
        private static int _lastHitStopCount;
        private static float _slowMoNextAt = -1f;

        /// <summary>这一帧有没有东西要画（没有就直接不进绘制分支）。</summary>
        public static bool PulseActive => _pulseLeft > 0f;
        /// <summary>当前脉冲的峰值 α（探针可读）。</summary>
        public static float PulseAlphaNow =>
            _pulseLeft <= 0f ? 0f
            : PulseMaxAlpha * _pulseStrength * Mathf.Pow(_pulseLeft / Mathf.Max(0.001f, _pulseTotal), 0.65f);

        /// <summary>
        /// 请一次屏边脉冲。`strength` 0..1 = "最近 1 秒伤害量 / `DamageCap`"（已夹住下限）。
        /// **受伤与爆炸共用这一条通道**（v262）：连续触发**不叠加**，也不无限续期 ——
        ///   · 已在响、而新强度 ≤ 当前 × `MergeRestartRatio` ⇒ **只更新颜色、不重置计时**；
        ///   · 明显更强 ⇒ 重开一个完整 `PulseSeconds`（以"更强的那一路"为准）。
        /// 改前：两个来源各写一份字段、每次都重置 0.2s ⇒ 挨打期间脉冲永不结束（每帧 40 次绘制）。
        /// </summary>
        public static void RequestPulse(float strength, Color color)
        {
            if (!Application.isPlaying) return;                 // 编辑模式：不画
            strength = Mathf.Clamp(strength, PulseFloor, 1f);
            if (_pulseLeft > 0f && strength < _pulseStrength * MergeRestartRatio)
            {
                // ★ 合并：不重开一条脉冲、不续期 ★（弱请求不许把 0.2s 一直续下去）
                if (strength > _pulseStrength) _pulseColor = color;
                return;
            }
            _pulseStrength = strength;
            _pulseColor = color;
            _pulseLeft = PulseSeconds;
            _pulseTotal = PulseSeconds;
            PulseCount++;
            LastPulseStrength = strength;
            LastPulseAlpha = PulseMaxAlpha * strength;
        }

        /// <summary>推进脉冲计时（**未缩放**）。由宿主 `LateUpdate` 每帧调一次。</summary>
        internal static void Tick(float unscaledDt)
        {
            if (unscaledDt < 0f) unscaledDt = 0f;
            if (_pulseLeft > 0f) _pulseLeft = Mathf.Max(0f, _pulseLeft - unscaledDt);
        }

        /// <summary>换局/重开：清脉冲与承伤窗口（**不**触发任何视觉）。</summary>
        public static void Reset()
        {
            _pulseLeft = 0f;
            _pulseStrength = 1f;
            _pulseColor = Color.white;
            Array.Clear(_dmgTime, 0, _dmgTime.Length);
            Array.Clear(_dmgAmt, 0, _dmgAmt.Length);
            _dmgHead = 0;
            _lastPlayerDamage = -1f;
            LastDamageWindow = 0f;
        }

        /// <summary>喂"玩家累计承伤"（`World.DamagedByEnemies`）：只在涨了时记一笔并触发脉冲。</summary>
        internal static void OnPlayerDamageTotal(float total, float now)
        {
            if (total < 0f) return;                                            // 没有活会话
            if (_lastPlayerDamage < 0f) { _lastPlayerDamage = total; return; } // 首次采样：只对齐基线
            if (total < _lastPlayerDamage - DamageEpsilon)                     // 换局/重开：环清空，不闪
            {
                _lastPlayerDamage = total;
                Array.Clear(_dmgTime, 0, _dmgTime.Length);
                Array.Clear(_dmgAmt, 0, _dmgAmt.Length);
                _dmgHead = 0;
                return;
            }
            float delta = total - _lastPlayerDamage;
            if (delta <= DamageEpsilon) return;                                // 没涨：什么都不做
            _lastPlayerDamage = total;

            // 记一笔（覆盖最旧的槽 ⇒ 零分配）
            _dmgTime[_dmgHead] = now;
            _dmgAmt[_dmgHead] = delta;
            _dmgHead = (_dmgHead + 1) % DamageSlots;

            float window = DamageWindowSum(now);
            LastDamageWindow = window;
            RequestPulse(Mathf.Clamp01(window / DamageCap), CombatFeedbackScreen.HurtColor);
        }

        /// <summary>把"最近 `DamageWindow` 秒内"的承伤加起来（`DamageSlots` 次循环，无分配）。</summary>
        private static float DamageWindowSum(float now)
        {
            float cutoff = now - DamageWindow;
            float sum = 0f;
            for (int i = 0; i < DamageSlots; i++)
            {
                if (_dmgAmt[i] <= 0f) continue;
                if (_dmgTime[i] < cutoff) continue;
                sum += _dmgAmt[i];
            }
            return sum;
        }

        /// <summary>
        /// 爆炸 → 一路暖白脉冲（**走与受伤同一条 `RequestPulse` 通道** ⇒ 同一刻只画一条）。
        /// ★ v262：**不再请求慢动作**（`SlowMoOnBigBlast = false`，理由见文件头第四节）★
        /// </summary>
        internal static void OnExplosion(float peak, float now)
        {
            float k = Mathf.Clamp01(peak / Mathf.Max(0.0001f, CombatFeedback.ExplosionTrauma * CombatFeedback.ExplosionTrauma * CombatFeedback.ExplosionAmp));
            // 爆炸的强度以"爆炸自身峰值"为准（与承伤窗口无关：远处爆炸不掉血但该亮一下）
            float strength = Mathf.Clamp(PulseFloor + (1f - PulseFloor) * k, PulseFloor, 1f);
            RequestPulse(strength, CombatFeedbackScreen.FlashColor);

            // ★ v262：慢动作整段关闭。写成**一个合取条件**而不是"先 return 再写"，
            //   是为了让 `SlowMoOnBigBlast` 保持 `const` 的同时不会引出 CS0162（无法访问的代码）★
            if (SlowMoOnBigBlast && peak >= BigBlastPeak && now >= _slowMoNextAt && Application.isPlaying)
            {
                _slowMoNextAt = now + SlowMoCooldown;
                SlowMoRequests++;
                LastSlowMoReason = "大爆炸（峰值 " + peak.ToString("0.000") + " ≥ " + BigBlastPeak.ToString("0.00") + "）";
                CombatFeedback.HitStopBoss();   // 可回滚的开关：置 `SlowMoOnBigBlast = true` 即恢复 v261 行为
            }
        }

        /// <summary>宿主被禁用/销毁时的时间兜底：只在"我们请求过、且 scale 还停在 hitstop 那一档"时写回 1。</summary>
        internal static void RestoreTimeScaleFallback()
        {
            if (!Application.isPlaying) return;
            if (!Mathf.Approximately(Time.timeScale, CombatFeedback.HitStopScale)) return;
            Time.timeScale = 1f;                // 正常路径由 CombatFeedback 的 unscaled 计时恢复，这里是兜底
        }

        /// <summary>把这一帧的屏边光带画出来。**必须在 `OnGUI` 里调**。</summary>
        internal static void DrawPulse()
        {
            if (_pulseLeft <= 0f) return;
            float k = _pulseLeft / Mathf.Max(0.001f, _pulseTotal);
            float env = Mathf.Pow(k, PulseFalloff);                  // 前段退得快、尾巴几乎看不见
            float a = PulseMaxAlpha * _pulseStrength * env;
            if (a <= 0.002f) return;

            float w = Screen.width;
            float h = Screen.height;
            float band = Mathf.Min(w, h) * Mathf.Lerp(PulseWidthMin, PulseWidthMax, _pulseStrength);
            if (band < 1f) return;
            LastPulseWidthPx = band;

            Color save = GUI.color;
            float thickness = band / PulseBands;
            for (int i = 0; i < PulseBands; i++)
            {
                float t = i / (float)PulseBands;                     // 0 = 最贴边
                float ai = a * (1f - t) * (1f - t);                  // 二次衰减 ⇒ 像一条"光带"而非一块色
                if (ai <= 0.002f) break;
                GUI.color = new Color(_pulseColor.r, _pulseColor.g, _pulseColor.b, ai);
                float o = i * thickness;
                // 四条边各自一条带（**没有 UV**，方向无关 ⇒ 不可能画反）
                GUI.DrawTexture(new Rect(0f, o, w, thickness), Texture2D.whiteTexture);                        // 上
                GUI.DrawTexture(new Rect(0f, h - o - thickness, w, thickness), Texture2D.whiteTexture);         // 下
                GUI.DrawTexture(new Rect(o, 0f, thickness, h), Texture2D.whiteTexture);                        // 左
                GUI.DrawTexture(new Rect(w - o - thickness, 0f, thickness, h), Texture2D.whiteTexture);          // 右
            }
            GUI.color = save;
        }

        /// <summary>一行读数（探针 / 交付报告用）。</summary>
        public static string Status()
        {
            return "GLOBALJUICE"
                 + "｜脉冲 " + PulseCount + " 次（当前 α=" + PulseAlphaNow.ToString("0.000")
                 + "，上次强度=" + LastPulseStrength.ToString("0.00")
                 + "，带宽=" + LastPulseWidthPx.ToString("0.0") + "px）"
                 + "｜1秒承伤=" + LastDamageWindow.ToString("0.00")
                 + "｜慢动作=【v262 已关闭】请求 " + SlowMoRequests + " 次（开关 SlowMoOnBigBlast=" + SlowMoOnBigBlast
                 + "，原门槛 " + BigBlastPeak.ToString("0.00") + "/冷却 " + SlowMoCooldown.ToString("0.00") + "s）"
                 + "｜既有 hitstop 次数=" + CombatFeedback.HitStopCount
                 + "｜timeScale=" + Time.timeScale.ToString("0.####")
                 + "｜微粒 " + WindRenderers + " 个（可见=" + WindVisible + "，Mesh=" + WindMeshBuilds + "，Mat=" + WindMaterialBuilds + "）"
                 + "｜目标=" + LastTarget + "｜实例=" + Instances;
        }
    }

    /// <summary>
    /// 全局动态宿主：**播放一开始就自己起一个**（`RuntimeInitializeOnLoadMethod`），
    /// 不依赖场景里有没有人挂它（与 `CombatFeedbackFxHost` 同一套自举方式）。
    ///
    /// 执行顺序 `32001`：在既有屏效宿主（32000）**之后**跑 ⇒ 本帧的爆炸计数/承伤一定已经更新，
    /// 且 `OnGUI` 画的时候用的是本帧最新强度 ✓
    /// </summary>
    [DefaultExecutionOrder(32001)]
    public sealed class GlobalJuiceFxHost : MonoBehaviour
    {
        // ── 微粒（全部在 `Awake` 建好，运行期只改 transform）──
        private Transform _windRoot;
        private Transform[] _wind;          // 12 个渲染体
        private float[] _windSpeed;         // 每颗的漂移速度
        private float[] _windBobAmp;
        private float[] _windBobOmega;
        private float[] _windBobPhase;
        private float[] _windBaseY;         // 盒内的基准高度（相对玩家根）
        private Vector3[] _windPos;         // 盒内坐标（相对玩家根），wrap 在这里做
        private Transform _target;
        private Camera _cam;
        private float _retryTimer;
        private bool _windOn;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>当前唯一的宿主（防重入用；`null` = 还没有）。</summary>
        private static GlobalJuiceFxHost _host;

        /// <summary>
        /// 进 play 模式后（首场景加载完）自动建宿主。
        /// ★ v262：**三重防重入**（见文件头第七节）★
        ///   `[RuntimeInitializeOnLoadMethod]` 正常一局只跑一次，但这条保证来自工程设置
        ///   （`m_EnterPlayModeOptions: 0`）而不是本文件自己的纪律 ⇒ 不靠它：
        ///   ① 静态 `_host` 已存在 ⇒ 直接返回；
        ///   ② 场景里已有同名 `GLOBAL_JUICE`（域重载被关掉的残留）⇒ 认领它、不再建第二个；
        ///   ③ `Awake` 里再比一次 `_host`，晚了就自我销毁（双保险）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_host != null) return;                                  // ①
            GameObject existing = GameObject.Find("GLOBAL_JUICE");       // ②
            if (existing != null)
            {
                var c = existing.GetComponent<GlobalJuiceFxHost>();
                if (c != null) { _host = c; return; }
            }
            var go = new GameObject("GLOBAL_JUICE");
            DontDestroyOnLoad(go);                       // 跨场景活一整个 play 会话
            go.AddComponent<GlobalJuiceFxHost>();
        }

        private void Awake()
        {
            if (_host != null && _host != this)          // ③ 已经是第二个 ⇒ 立刻自己拆掉
            {
                Destroy(gameObject);
                return;
            }
            _host = this;
            DontDestroyOnLoad(gameObject);
            GlobalJuice.Instances++;
            BuildWind();
        }

        private void OnDisable()
        {
            // ★ 时间恢复兜底（见 `GlobalJuice` 文件头第四节）★
            GlobalJuice.RestoreTimeScaleFallback();
        }

        private void OnDestroy()
        {
            if (_host == this) _host = null;
            GlobalJuice.Instances--;
            GlobalJuice.RestoreTimeScaleFallback();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            float udt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            // ★ v263：安全网（第二道）—— 本宿主是 `RuntimeInitializeOnLoadMethod` 自举的常驻 DDOL，
            //   比 `CombatFeedbackRig`（按需创建）更早、更稳：即使揉抖宿主被销毁/禁用，
            //   这里每帧仍会把"没有停帧却偏低"的 `timeScale` 拉回 1
            //   （症状：爆炸后玩家/敌人全冻、HUD/时钟照走 ⇒ 就是它）✓
            CombatFeedback.GuardTimeScale();

            // ① 脉冲计时
            GlobalJuice.Tick(udt);

            // ② 玩家承伤 → "最近 1 秒伤害量" → 脉冲
            GlobalJuice.OnPlayerDamageTotal(ReadPlayerDamage(), now);

            // ③ 爆炸计数 → 暖白脉冲（v262：**不再**顺带请求 hitstop，见文件头第四节）
            int ex = CombatFeedback.ExplosionCount;
            if (ex != _lastExplosionSeen)
            {
                _lastExplosionSeen = ex;
                GlobalJuice.OnExplosion(CombatFeedback.LastExplosionPeak, now);
            }
            // boss 击杀那一路由 `RunSession.cs:2730` 直接调 `HitStopBoss()` ⇒ 本文件只把它当读数
            // （`GlobalJuice.Status()` 印 `HitStopCount`），**不重复触发**、也不写 timeScale

            // ④ 地面微风微粒
            TickWind(now, udt);
        }

        private int _lastExplosionSeen;

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (!GlobalJuice.PulseActive) return;
            // IMGUI 一帧会被调好几次（Layout / Repaint / 各种输入事件）⇒ 只画 Repaint 那一趟
            if (Event.current != null && Event.current.type != EventType.Repaint) return;

            Matrix4x4 prevMatrix = GUI.matrix;
            int prevDepth = GUI.depth;
            Color prevColor = GUI.color;
            try
            {
                // 真屏幕像素（HUD 会在自己的 OnGUI 里设虚拟分辨率缩放，这里与它无关）
                GUI.matrix = Matrix4x4.identity;
                // 3 = 在 HUD(0) 与既有屏效(2) **之下**（IMGUI 里 depth 越小越靠上）
                GUI.depth = 3;
                GlobalJuice.DrawPulse();
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

        // ══════════════════════════════════════════════════════════════════
        // 地面微风微粒（建一次，之后只改 transform）
        // ══════════════════════════════════════════════════════════════════

        private void BuildWind()
        {
            var root = new GameObject("GLOBAL_JUICE_Wind");
            root.transform.SetParent(transform, false);
            _windRoot = root.transform;

            Mesh mesh = SharedQuadMesh();
            Material matA = WindMaterial("M_GlobalJuiceWindA", GlobalJuice.WindAlphaA);
            Material matB = WindMaterial("M_GlobalJuiceWindB", GlobalJuice.WindAlphaB);

            _wind = new Transform[GlobalJuice.WindCount];
            _windSpeed = new float[GlobalJuice.WindCount];
            _windBobAmp = new float[GlobalJuice.WindCount];
            _windBobOmega = new float[GlobalJuice.WindCount];
            _windBobPhase = new float[GlobalJuice.WindCount];
            _windBaseY = new float[GlobalJuice.WindCount];
            _windPos = new Vector3[GlobalJuice.WindCount];

            // 用**确定性**的伪随机（不加 System.Random ⇒ 不引入新状态、不碰 World.Rng）
            uint seed = 0x9E3779B9u;
            for (int i = 0; i < GlobalJuice.WindCount; i++)
            {
                var go = new GameObject("WindGrain" + i);
                go.transform.SetParent(_windRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = (i % 2 == 0) ? matA : matB;
                mr.shadowCastingMode = ShadowCastingMode.Off;    // 不投影
                mr.receiveShadows = false;                        // 不吃阴影
                mr.sortingOrder = 0;
                _wind[i] = go.transform;

                _windSpeed[i] = Lerp(GlobalJuice.WindMinSpeed, GlobalJuice.WindMaxSpeed, Next01(ref seed));
                _windBobAmp[i] = Lerp(GlobalJuice.WindBobMin, GlobalJuice.WindBobMax, Next01(ref seed));
                _windBobOmega[i] = Lerp(GlobalJuice.WindBobOmegaMin, GlobalJuice.WindBobOmegaMax, Next01(ref seed));
                _windBobPhase[i] = Next01(ref seed) * Mathf.PI * 2f;
                float size = Lerp(GlobalJuice.WindMinSize, GlobalJuice.WindMaxSize, Next01(ref seed));
                _windBaseY[i] = Lerp(GlobalJuice.WindMinY, GlobalJuice.WindMaxY, Next01(ref seed));
                _windPos[i] = new Vector3(
                    (Next01(ref seed) * 2f - 1f) * GlobalJuice.WindBoxHalfX,
                    _windBaseY[i],
                    (Next01(ref seed) * 2f - 1f) * GlobalJuice.WindBoxHalfZ);
                _wind[i].localScale = new Vector3(size, size, 1f);
            }

            GlobalJuice.WindRenderers = GlobalJuice.WindCount;
            SetWindVisible(false);  // 先关：等确认在战斗场景再亮
        }

        /// <summary>每帧：wrap + 漂移 + 上下浮动 + 面向相机（**零分配**）。</summary>
        private void TickWind(float now, float udt)
        {
            if (_wind == null) return;

            // 目标（玩家）与相机：都低频重试，别每帧 Find
            if (_target == null) AcquireTarget(udt);
            if (_cam == null) _cam = Camera.main;

            bool want = _target != null && IsArenaScene();
            if (want != _windOn) SetWindVisible(want);   // 只在状态变化时 SetActive
            if (!want) return;

            Quaternion face = _cam != null ? _cam.transform.rotation : Quaternion.identity;
            Vector3 origin = _target.position;
            float halfX = GlobalJuice.WindBoxHalfX;
            float halfZ = GlobalJuice.WindBoxHalfZ;

            float dx = GlobalJuice.WindDir.x;
            float dz = GlobalJuice.WindDir.z;

            for (int i = 0; i < _wind.Length; i++)
            {
                Vector3 p = _windPos[i];
                // ① 漂移（沿固定风向）
                float step = _windSpeed[i] * udt;
                p.x += dx * step;
                p.z += dz * step;
                // ② 绕玩家 wrap（**不出盒 ⇒ 不出屏**；不销毁不重建）
                if (p.x > halfX) p.x -= halfX * 2f;
                else if (p.x < -halfX) p.x += halfX * 2f;
                if (p.z > halfZ) p.z -= halfZ * 2f;
                else if (p.z < -halfZ) p.z += halfZ * 2f;
                p.y = _windBaseY[i] + Mathf.Sin(now * _windBobOmega[i] + _windBobPhase[i]) * _windBobAmp[i];
                _windPos[i] = p;

                // ③ 写世界（3 次 transform 写入；rotation 让它面向相机 = 一段方片）
                Transform t = _wind[i];
                t.position = new Vector3(origin.x + p.x, origin.y + p.y, origin.z + p.z);
                t.rotation = face;
            }
        }

        private void SetWindVisible(bool on)
        {
            _windOn = on;
            GlobalJuice.WindVisible = on;
            if (_windRoot != null && _windRoot.gameObject.activeSelf != on) _windRoot.gameObject.SetActive(on);
        }

        private bool IsArenaScene()
        {
            return SceneManager.GetActiveScene().name.IndexOf(GlobalJuice.ArenaSceneKey, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>找玩家根（沿用 `PlayerFacingMarker` 的三个候选名）。找不到按 `AcquireRetrySeconds` 重试。</summary>
        private void AcquireTarget(float udt)
        {
            _retryTimer -= udt;
            if (_retryTimer > 0f) return;
            _retryTimer = GlobalJuice.AcquireRetrySeconds;
            string[] names = { "TownPlayer", "MDL_farmer", "Player" };
            for (int i = 0; i < names.Length; i++)
            {
                GameObject go = GameObject.Find(names[i]);
                if (go == null) continue;
                _target = go.transform;
                GlobalJuice.LastTarget = go.name;
                return;
            }
            GlobalJuice.LastTarget = "(无)";
            _target = null;
        }

        /// <summary>共享网格：1×1 的四边形（顶点色白；颜色只来自材质）。**只建一次**。</summary>
        private static Mesh _quadMesh;

        private static Mesh SharedQuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;
            var v = new Vector3[4];
            v[0] = new Vector3(-0.5f, -0.5f, 0f);
            v[1] = new Vector3(0.5f, -0.5f, 0f);
            v[2] = new Vector3(0.5f, 0.5f, 0f);
            v[3] = new Vector3(-0.5f, 0.5f, 0f);
            var uv = new Vector2[4];
            uv[0] = new Vector2(0f, 0f);
            uv[1] = new Vector2(1f, 0f);
            uv[2] = new Vector2(1f, 1f);
            uv[3] = new Vector2(0f, 1f);
            var nrm = new Vector3[4];
            for (int i = 0; i < 4; i++) nrm[i] = Vector3.back;
            var tris = new[] { 0, 1, 2, 0, 2, 3 };

            var m = new Mesh { name = "GlobalJuiceWindQuad", hideFlags = HideFlags.DontSave };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetNormals(nrm);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            _quadMesh = m;
            GlobalJuice.WindMeshBuilds++;
            return m;
        }

        /// <summary>
        /// 微粒材质：`Sprites/Default`（本工程已验证的透明顶点色着色器，`PlayerFacingMarker` 同款），
        /// `renderQueue = 2100`（**背景级**）、不投影、不吃阴影。建两份（两档 α）⇒ `MaterialBuilds` 只 +2。
        /// </summary>
        private static Material _windMatA;
        private static Material _windMatB;

        private static Material WindMaterial(string name, float alpha)
        {
            bool a = name.EndsWith("A", StringComparison.Ordinal);
            if (a && _windMatA != null) return _windMatA;
            if (!a && _windMatB != null) return _windMatB;

            Shader sh = Shader.Find("Sprites/Default");
            bool sprite = sh != null;
            if (!sprite)
            {
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Standard");
            }
            var m = new Material(sh) { name = sprite ? name : name + "FALLBACK" };
            m.mainTexture = Texture2D.whiteTexture;
            m.color = new Color(1f, 1f, 1f, alpha);        // 颜色只来自材质（顶点色白）
            m.SetColor("_RendererColor", Color.white);     // `Sprites/Default` 的第三项乘数，显式给白
            m.renderQueue = GlobalJuice.WindQueue;         // ★ 背景级 ★
            if (!sprite)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, Color.white);
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // URP：Transparent
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // URP：Alpha
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                if (m.HasProperty(ColorId)) m.SetColor(ColorId, Color.white);
            }
            if (a) _windMatA = m; else _windMatB = m;
            GlobalJuice.WindMaterialBuilds++;
            return m;
        }

        // ── 极小的确定性伪随机（xorshift；不引入 `System.Random`、不碰 `World.Rng`）──
        private static float Next01(ref uint s)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            if (s == 0u) s = 0x9E3779B9u;
            return (s & 0x00FFFFFFu) / 16777215f;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t);
    }
}
