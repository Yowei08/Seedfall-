using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 训练场（v300）：**只做"多出来的那一半"** ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、它不启动游戏，它只是"给战斗那一套加几样东西"
    /// ══════════════════════════════════════════════════════════════════════
    ///   `Training.unity` 里**同时**挂着 `GameBootstrap` 与本组件（同一个 GameObject）。
    ///
    ///   · 游戏怎么开局、怎么读输入、怎么画 HUD、模型怎么动、相机怎么跟、
    ///     耕地怎么排、商店车怎么跑 —— **全部由 `GameBootstrap` 那一套跑，
    ///     一个字节都没有复制** ✓ 这就是"手感/操作/UI/动画完全一致"的唯一可靠来源。
    ///   · 本组件只做三件事：
    ///       ① 在 `GameBootstrap.Awake` **之前**把 <see cref="TrainingMode"/> 的开关置位
    ///          （`[DefaultExecutionOrder(-1000)]` 保证顺序，见下）；
    ///       ② 等会话起来后，往世界里放 9 个九宫格稻草人 + 1 个巨人稻草人；
    ///       ③ 画一个**只属于训练场**的伤害计数器（DPS / 累计 / 10 秒归零），
    ///          以及把稻草人的外观画成稻草人（战斗表现层对它们让路，见 ④）。
    ///
    ///   ⇒ 上一轮的 `TrainingGame`（自包含实现）已删除。它与战斗的差别是结构性的
    ///     （自己一套移动/攻击/相机/HUD），不可能"一模一样" ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、为什么开关必须在 `GameBootstrap.Awake` 之前置位
    /// ══════════════════════════════════════════════════════════════════════
    ///   `GameBootstrap.Start()` 调 `StartRun(seed)` ⇒ `new RunSession()` ⇒ `Session.Start(seed)`
    ///   ⇒ `World` / `Farm` / 玩家 / 金币（`Gold = startGold`）全部在这一刻成形。
    ///   若开关晚一步（例如在 `Start` 里），第一帧的 `FixedTick` 已经按"战斗模式"跑过一次
    ///   ⇒ `Gold` 会被战斗的取值覆盖一帧，且 `Waves.StartWave` 已经刷了第一只怪 ✓
    ///   `UnityEngine.DefaultExecutionOrder(-1000)` 让本组件的 `Awake` 一定排在
    ///   `GameBootstrap.Awake`（默认 0）之前 ⇒ 顺序是**语义保证**，不是"通常如此" ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、布置（世界 +X = 屏幕右、+Z = 屏幕上，与战斗/主城同向）
    /// ══════════════════════════════════════════════════════════════════════
    ///   战斗的耕地是 **6 列 × 6 行、格 2.5m**，居中于原点
    ///   ⇒ 它占 x ∈ [−6.25, 6.25]、z ∈ [−6.25, 6.25]（`FarmGrid.WorldOf`，FarmGrid.cs:567-572）
    ///   ⇒ 稻草人**必须放在耕地之外**，否则会被耕地压住、也挡着玩家种地 ✗
    ///   所以九宫格整体搬到耕地**北侧**：
    ///
    ///           z = 16.5        ◆ 巨人稻草人（×2.5）
    ///           z = 13          ○   ○   ○
    ///           z = 10          ○   ○   ○     ← 九宫格中心 (0,10)，间距 3.0m
    ///           z =  7          ○   ○   ○
    ///           z ∈ [−6.25, 6.25]        耕地（6 列 × 6 行，战斗同款）
    ///           z = −7                     ▲ 玩家出生（`RunSession` 里写死的 (0,−7)）
    ///
    ///   ★ v312：**靶场稻草人**（会开枪的那个）在 **(+13, 0)** —— 画面**右半区**、耕地东沿之外 5.5m。
    ///     位置/可见性的完整算法与四点视锥表在常量块 <see cref="GunnerPos"/> 上方（**算过的**）✓
    ///
    ///   竞技场半幅 `farm.arenaHalf = 20` ⇒ z = 16.5 仍在场内（`World.ClampToArena` 之后
    ///   实际落点见 `Spawn` 里的日志）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、稻草人的"身体"为什么不归战斗表现层画
    /// ══════════════════════════════════════════════════════════════════════
    ///   稻草人的数据是 `enemies.json` 里两条 `kind = "Wander"` 的敌人（见交付报告 ①）。
    ///   而战斗表现层对 `Wander` 有**硬编码分支**：
    ///   `ProceduralBugs.SyncEnemies`（ProceduralBugs.cs:3001）
    ///   `if (e.Def.Kind == EnemyKind.Wander) { SyncPig(...); continue; }` —— 它画的是**小猪 FBX**。
    ///   ⇒ 不处理的话，训练场里站着的会是 10 只小猪，而不是稻草人 ✗
    ///   ⇒ 本组件把 `TrainingMode.HideDummyBody` 置真，那一行守卫跳过稻草人；
    ///     稻草人由本文件 <see cref="BuildScarecrow"/> **程序化**建（杆/横梁/草头/草帽/手臂），
    ///     并且**复用战斗的受击反馈**：`EnemyActor.HitFlash`（闪白）与
    ///     `Actor.OnHitLanded` 弹出的伤害数字（`ImGuiHud.DrawPopups` 那一套）一个字都没改 ✓
    ///
    ///   ★ v312：**靶场稻草人走的是同一条"藏身体 + 自己画"的路，理由却不一样** ★
    ///     它的 `kind = "Shooter"`，战斗表现层会把它交给**程序化虫怪**（`Acquire` + `Species`），
    ///     画的是一只紫色的虫 —— 一样不是稻草人 ⇒ 它也被 `TrainingMode.IsDummyId` 认下来，
    ///     由本文件 <see cref="BuildScarecrow"/> 画（见 <see cref="SpawnAll"/> 那一段的 ⚠️ 说明）。
    ///     **两条守卫 + 一个画师**必须同时成立：少画师 ⇒ 它在地图上完全看不见（只剩子弹飞出来）；
    ///     少守卫 ⇒ 一只虫叠在一个稻草人身上。本文件的两处改动就是这两半 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、伤害统计（用户点名：每秒伤害 / 总共伤害 / 没有攻击 10 秒后清除归 0）
    /// ══════════════════════════════════════════════════════════════════════
    ///   **不挂钩任何战斗事件**（那样就要改 `RunSession` / `World` 的事件表）——
    ///   而是每帧读稻草人的 `Health.Hp` 差值：
    ///     `dmg = 上一帧 Hp − 这一帧 Hp`（> 0 才算一次伤害）
    ///   ⇒ 它与"玩家实际打掉多少血"是**同一个数**，且零侵入 ✓
    ///   统计本身用 <see cref="TrainingStats"/>（环形缓冲的 1 秒 DPS 窗 + 10 秒无伤归零，
    ///   上一轮已经写好的纯 C# 类，与任何 UI 解耦 ⇒ 本轮原样复用）。
    ///
    ///   ⚠️ 精度（如实记）：稻草人 `baseHp = 1e6`（第 1 分钟系数 0.65 ⇒ 实际 65 万）。
    ///   float32 在 6.5e5 量级的分辨率约 **0.0625**，而玩家单击是 4~8 点 ⇒ 相对误差 < 1.6%，
    ///   计数与 DPS 读数不受影响 ✓ 之所以不写 `1e9`：那会**丢失小伤害**（见交付报告 ①）。
    ///   另外血量掉到 50% 以下时立刻补满 ⇒ "无限血量"是**结构性**的，不靠大数硬撑 ✓
    ///   ★ v312 补充：**靶场稻草人例外**（`Dummy.Refill = false`）—— 它的血量从 1950 一路掉到 0,
    ///   掉血同样计进 DPS / 累计伤害（"打它也是在练习"），但它**不补血、会被打死** ✓
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class TrainingBootstrap : MonoBehaviour
    {
        private const string Tag = "[训练场] ";

        // ══════════════════════════════════════════════════════════════════
        // ★ 布置参数（交付报告 ⑤ 报的每一个数都在这一段）★
        // ══════════════════════════════════════════════════════════════════

        /// <summary>九宫格边长（3 ⇒ 3×3 = 9 个稻草人）。</summary>
        public const int GridSide = 3;

        /// <summary>九宫格间距（米）。3×3 时整体占地 6 × 6 米。</summary>
        public const float GridSpacing = 3.0f;

        /// <summary>九宫格中心（世界 XZ）。放在耕地北侧 —— 耕地占 z ∈ [−6.25, +6.25]。</summary>
        public static readonly Vector2 GridCenter = new Vector2(0f, 10f);

        /// <summary>巨人稻草人位置（九宫格北侧一格之外，不挤在格子里）。</summary>
        public static readonly Vector2 BossPos = new Vector2(0f, 16.5f);

        /// <summary>巨人稻草人的体型倍数（"巨人" ⇒ 2.5 倍，与战斗的 BOSS 体型单位同档）。</summary>
        public const float BossScale = 2.5f;

        /// <summary>普通稻草人的体型倍数。</summary>
        public const float DummyScale = 1.0f;

        // ══════════════════════════════════════════════════════════════════
        // ★ v312：**靶场稻草人**（用户原话："训练场在地图右侧增加一个可攻击稻草人，
        //        靠近会发射子弹攻击玩家"）—— 位置与可见性
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// 靶场稻草人的位置：**地图右侧**（世界 +X = 屏幕右）。
        ///
        /// ── 为什么是 (+13, 0) ──
        ///   · 耕地占 `x, z ∈ [−7.5, +7.5]`（训练场里 6 列 × 6 行、格 2.5m ⇒ ±6.25，
        ///     含地块外框按 7.5 算，与 `TrainingWarehouse` 顶部那张间距表同一口径）
        ///     ⇒ `x = 13` 在耕地东沿**之外 5.5m**，既不压地也不挡种地 ✓
        ///   · 场地逻辑边界 `farm.arenaHalf = 20` ⇒ `13 + 0.45（radius）= 13.45 < 20`，
        ///     离墙 6.55m —— 它又站桩不动，不会被钳制推走 ✓
        ///   · 那句"靠近会发射"需要**玩家真的会走过去**：它在 (13,0)，玩家从出生点 (0,−7)
        ///     走直线约 14.8m 就到 12m 射程内 ⇒ 是一个"往右拐一下"的动作，不是满地图跑 ✓
        ///   · ⚠️ 别往 +14.4 挪：那是**商店车绕场环线的右直边**（`ShopTruck` 的 `0.72 × arenaHalf`）。
        ///     训练场里商店车本来就被 `TrainingMode.NoShopTruck` 关掉了，但位置表照旧留 1.4m 余量 ✓
        ///
        /// ── 可见性（**算过的**，不是估的；算式与 `TrainingWarehouse` 二 那一段同源）──
        ///   镜头（`tuning.json` 的 `camera` 段实际值 + `GameBootstrap.UpdateCamera:575-601`）：
        ///     `fov = 62`、`pitch = 60`、`distance = 22`、`orthographic = false`、`clampToArena = false`
        ///     机位 = 焦点 + (0, sin60, −cos60) × 22；进训练场首帧焦点 = 玩家出生点 (0, −7)
        ///     ⇒ 相机 `C = (0, 19.0526, −18.0000)`
        ///   相机基：前 `f = (0, −0.86603, 0.5)`、右 `r = (1, 0, 0)`、上 `u = (0, 0.5, 0.86603)`
        ///     `x_c = d.x`；`y_c = 0.5·d.y + 0.86603·d.z`；`z_c = −0.86603·d.y + 0.5·d.z`（`d = P − C`）
        ///     可见 ⇔ `|y_c|/z_c ≤ tan(31°) = 0.60086` **且** `|x_c|/z_c ≤ 0.60086 × aspect`
        ///   稻草人轮廓取四处极值（体宽 ±0.81m = 横梁 1.62m 的一半；高 2.54m = 帽顶）：
        ///     | 轮廓点（世界）              | x_c    | y_c    | z_c    | \|x_c\|/z_c | \|y_c\|/z_c |
        ///     |-----------------------------|--------|--------|--------|-------------|-------------|
        ///     | 脚底  (13.00, 0.00, 0)      | 13.000 | 6.0622 | 25.5000| 0.5098      | 0.2377      |
        ///     | 帽顶  (13.00, 2.54, 0)      | 13.000 | 7.3322 | 23.3003| 0.5579      | **0.3147**  |
        ///     | 东臂  (13.81, 1.60, 0)      | 13.810 | 6.8622 | 24.1144| **0.5727**  | 0.2846      |
        ///     | 西臂  (12.19, 1.60, 0)      | 12.190 | 6.8622 | 24.1144| 0.5055      | 0.2846      |
        ///   ⇒ 垂直方向最大 0.3147 < **0.60086** ✓（余量 48%）
        ///   ⇒ 水平方向最紧一点是**东臂** 0.5727 ⇒ 要求窗口宽高比 ≥ 0.5727 / 0.60086 = **0.9531**
        ///     ⇒ 16:9 (1.7778) ✓、16:10 (1.6000) ✓、4:3 (1.3333) ✓、甚至正方形 (1.0000) ✓
        ///     —— **比仓库那栋楼（1.4655）宽松得多**，因为它在场地中轴附近、离镜头更近 ✓
        ///   ⇒ **一进训练场它整只都在画面内**，且 `x_c = +13 > 0` ⇒ 落在画面**右半区** ✓
        ///   （上表由 `x_c = d.x`、`y_c = 0.5·d.y + 0.86603·d.z`、`z_c = −0.86603·d.y + 0.5·d.z`
        ///    四点逐一代入算出，`d = P − C`、`C = (0, 19.05256, −18.00000)` ✓
        ///    ⚠️ 首帧焦点不是"精确" (0,−7)：`_camFocus` 从 (0,0) 起步、`Start` 里以 `dt=1` 追 ⇒
        ///    `k = 1−e^{−10} = 0.9999546` ⇒ 焦点 (0, −6.99968)、机位 z = −17.99968
        ///    ⇒ 上表每个比值的变化 < 0.003%，结论一位不变 ✓）
        /// </summary>
        public static readonly Vector2 GunnerPos = new Vector2(13f, 0f);

        /// <summary>
        /// 靶场稻草人的体型倍数 = **1.0**（与那 9 个普通稻草人同尺）。
        /// 为什么不放大：它是"同一批稻草人里的一个，只是会开枪"⇒ 体型一致才读得出来这件事；
        /// 放大反而会让玩家以为它是新的 BOSS 类目标 ✓
        /// </summary>
        public const float GunnerScale = 1.0f;

        /// <summary>
        /// 被打死之后"倒下去"的倾角（度）。0 = 站着。
        /// 它**只用于靶场稻草人**（那 10 个刷回满血、永远不会死 ⇒ 这个分支对它们恒不成立）：
        /// 没有这一下，打死之后原地还站着一只一模一样的稻草人 ⇒ 玩家无法判断"我到底打死了没有" ✗
        /// 绕**局部 X 轴** +78° ⇒ 顶部朝它**自己面朝的方向**倒（它面朝玩家 ⇒ 朝玩家倒）
        /// </summary>
        public const float FallenPitch = 78f;

        /// <summary>稻草人朝玩家出生点（−Z 方向）的朝向。</summary>
        public const float DummyYaw = 180f;

        /// <summary>血量掉到这个比例以下就补满（"无限血量"的判据，见类注释 ⑤）。</summary>
        public const float RefillBelowPct = 0.5f;

        /// <summary>生成时用的 wave 号。1 = 曲线起点（`WaveGrowth(1) = 1`）。</summary>
        public const int SpawnWave = 1;

        // ── 运行期 ──
        private GameBootstrap _boot;
        private RunSession _bound;                  // 这一批稻草人属于哪个会话（重开局就重建）
        private readonly List<Dummy> _dummies = new List<Dummy>(GridSide * GridSide + 2);

        /// <summary>★ v312：靶场稻草人那一条记录（`_dummies` 里同时也有它；这里只是"那一个"的快捷引用，
        /// 给 HUD 读它的血量 —— 它是**唯一能被打死**的目标，看不见剩多少血就没法练）。</summary>
        private Dummy _gunner;
        private readonly TrainingStats _stats = new TrainingStats();
        private Transform _root;
        private GUIStyle _panel;
        private GUIStyle _big;
        private GUIStyle _small;

        /// <summary>
        /// 受击闪白用的属性块（全进程一份，每帧复用 ⇒ 零分配）。
        ///
        /// ⚠️ **不能写成 `static readonly ... = new MaterialPropertyBlock()`** ★ v302 修复 ★
        ///   `MaterialPropertyBlock` 是 Unity 对象，Unity 禁止在**类型静态初始化器 / MonoBehaviour
        ///   构造函数 / 实例字段初始化器**里创建任何 Unity 对象，否则抛
        ///   `UnityException: CreateImpl is not allowed to be called from a MonoBehaviour constructor
        ///   (or instance field initializer)`，并进一步炸成
        ///   `TypeInitializationException: The type initializer for 'Seedfall.TrainingBootstrap'
        ///   threw an exception.` —— 整个类型初始化失败 ⇒ **训练场一进就报错、根本进不去**。
        ///   （这是线上实际发生过的故障，不是理论风险。）
        ///   改成**懒创建**：第一次用到时才 new（那时已经在运行期，完全合法）✓
        /// </summary>
        private static MaterialPropertyBlock _mpb;

        /// <summary>懒创建 <see cref="_mpb"/>（只在运行期调用，绝不在静态初始化器里）。</summary>
        private static MaterialPropertyBlock Mpb()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            return _mpb;
        }
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>一个稻草人：逻辑实体 + 它的外观。</summary>
        private sealed class Dummy
        {
            public EnemyActor Enemy;
            public GameObject Go;
            public Renderer[] Parts;
            public Color[] BaseColors;
            public float LastHp;
            public bool IsGiant;

            /// <summary>
            /// true = 掉到 <see cref="RefillBelowPct"/> 以下就**补满**（"无限血量"）。**默认 true**
            /// ⇒ 那 9 个普通稻草人与巨人的行为一个字都不变 ✓
            ///
            /// ★ v312：靶场稻草人置 **false** ★
            ///   它是要被**打死**的（`enemies.json` 的 `training_gunner.baseHp = 3000`）。
            ///   不排除的话，`SyncDummies` 会在它掉到 50% 的那一刻把血补满
            ///   ⇒ 它又变回一只 1e6 血那种"无敌稻草人"，用户点名要的"可打死"当场失效 ✗
            /// </summary>
            public bool Refill = true;

            /// <summary>
            /// true = 每帧**转向玩家**（炮塔式）。
            ///
            /// ★ v312：只有靶场稻草人是 true ★
            ///   查证结论（`ShooterEnemy.Behaviour`，Enemies.cs:237-264）：逻辑层**没有任何朝向字段** ——
            ///   它只用 `dir = (target − Pos).normalized` 决定走位与开枪方向，`Actor` 上也没有
            ///   `Yaw` / `Facing` 这种状态；战斗表现层的朝向是**从位移方向反推**的
            ///   （`ProceduralBugs.Moved(id, pos, dt, out dir, out step)` ⇒ `SetFacing(Atan2(dir.x, dir.z))`）。
            ///   而本目标的 `moveSpeed = 0` ⇒ **位移恒为 0** ⇒ 那条路永远给出"没有方向"。
            ///   ⇒ 朝向只能由训练场自己给：站着不动的炮塔**必须一直面向玩家**，
            ///     否则玩家绕到它背后时，画面上一只稻草人拿背对着你却在开枪 ✗
            ///   （那 10 个稻草人保持 `DummyYaw = 180°` 面向出生点，与改前逐字节相同 ✓）
            /// </summary>
            public bool FacePlayer;
        }

        // ══════════════════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            // ★ 第一件事：在 `GameBootstrap.Awake` / `Start` 之前把开关置位（见类注释 二）★
            TrainingMode.Enable();
            Log.Info(Tag + "训练开关已置位（本组件 DefaultExecutionOrder=-1000，早于 GameBootstrap.Awake）："
                         + TrainingMode.Describe());
            _boot = GetComponent<GameBootstrap>();
            if (_boot == null)
            {
                Log.Error(Tag + "同一个 GameObject 上没有 GameBootstrap ⇒ 训练场不会有一局游戏（"
                               + "检查 Assets/Scenes/Training.unity 是否同时挂了两个组件）");
            }

            // ★ v303：常驻仓库面板（用户原话"训练场的仓库没有做"）★
            //   为什么在这里 AddComponent、而不是上一版那样用 `[RuntimeInitializeOnLoadMethod]` 自建：
            //   自建的对象**在战斗场景里也会存在**（每局多一个 Update + 一个隐藏物体），
            //   而挂在这里 ⇒ `Training.unity` 之外**根本没有这个组件**
            //   ⇒ "不影响正常战斗"是结构性的，不依赖任何开关自觉 ✓
            if (GetComponent<TrainingVault>() == null) gameObject.AddComponent<TrainingVault>();

            // ★ v304：**实体仓库**（看得见的建筑 + E 键互动）★
            //   用户原话：「还是没有看到仓库，实际模型，和 E 键互动进行列表购买内容」
            //   ⇒ 上一版只有一个"按 B 才存在"的 IMGUI 面板，场景里零物体 ⇒ 玩家看不到。
            //   挂在这里的理由与上一行完全相同：`Training.unity` 之外**没有这个组件** ✓
            if (GetComponent<TrainingWarehouse>() == null) gameObject.AddComponent<TrainingWarehouse>();
        }

        private void Update()
        {
            if (_boot == null) return;
            RunSession s = _boot.Session;
            if (s == null || !_boot.Ready) return;

            // 会话换了（Shift+R 重开一局 / 从主城再进来）⇒ 稻草人重建
            if (!ReferenceEquals(_bound, s))
            {
                _bound = s;
                SpawnAll(s);
            }

            _stats.Tick(Mathf.Min(Time.deltaTime, 0.25f));
            SyncDummies();
        }

        private void OnGUI()
        {
            if (!TrainingMode.Active) return;
            if (_panel == null)
            {
                _panel = new GUIStyle(GUI.skin.box);
                _big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                _big.normal.textColor = new Color(1f, 0.97f, 0.88f);
                _small.normal.textColor = new Color(0.92f, 0.94f, 0.90f);
            }

            const float w = 300f;
            // ★ v312：116 → **140**（多一行"靶场稻草人还剩多少血"，见下面 y+106 那一行）
            const float h = 140f;
            var box = new Rect(Screen.width - w - 14f, Screen.height - h - 14f, w, h);
            GUI.Box(box, GUIContent.none, _panel);
            GUI.Label(new Rect(box.x + 12f, box.y + 8f, w - 24f, 28f),
                $"DPS {_stats.Dps:0.0}", _big);
            GUI.Label(new Rect(box.x + 12f, box.y + 40f, w - 24f, 20f),
                $"累计伤害 {_stats.Total:0}　命中 {_stats.Hits}", _small);
            GUI.Label(new Rect(box.x + 12f, box.y + 62f, w - 24f, 20f),
                $"峰值 DPS {_stats.PeakDps:0.0}", _small);
            GUI.Label(new Rect(box.x + 12f, box.y + 84f, w - 24f, 20f),
                _stats.IsIdleCounting
                    ? $"无攻击 {_stats.IdleTime:0.0}s ⇒ {_stats.SecondsToReset:0.0}s 后归零"
                    : $"稻草人 {_dummies.Count} 个（9 普通 + 1 巨人 + 1 靶场）", _small);
            // ★ v312：靶场稻草人的血量（它是**唯一能被打死**的目标 ⇒ 必须看得见进度）★
            GUI.Label(new Rect(box.x + 12f, box.y + 106f, w - 24f, 20f), GunnerLine(), _small);
        }

        /// <summary>
        /// ★ v312：靶场稻草人的血量读数。
        /// 「已击倒」这一行**必须存在** —— 它同时也是"它确实被打死了"的唯一画面证据
        /// （身体会倒下去，但倒下去之后剩多少血这件事只有这里说得清）✓
        /// </summary>
        private string GunnerLine()
        {
            EnemyActor g = _gunner != null ? _gunner.Enemy : null;
            if (g == null) return "靶场稻草人：未生成";
            if (!g.Alive) return "靶场稻草人：已击倒 ✓";
            return $"靶场稻草人 {g.Health.Hp:0}/{g.Health.MaxHp:0}（{g.Health.HpPct * 100f:0}%）";
        }

        private void OnDestroy()
        {
            // 静态开关跨场景残留 ⇒ 离开训练场必须复位（见 `TrainingMode` 类注释 二）
            TrainingMode.Disable();
            Log.Info(Tag + "离开训练场：训练开关已复位 → " + TrainingMode.Describe());
        }

        // ══════════════════════════════════════════════════════════════════
        // 生成稻草人
        // ══════════════════════════════════════════════════════════════════

        private void SpawnAll(RunSession s)
        {
            ClearDummies();
            if (_root == null)
            {
                _root = new GameObject("TRAINING_Dummies").transform;
            }

            World w = s.World;
            if (w == null) return;

            // ── 9 个九宫格稻草人：3×3，间距 3.0m，行/列偏移 {−3, 0, +3} ──
            for (int r = 0; r < GridSide; r++)
            {
                for (int c = 0; c < GridSide; c++)
                {
                    float x = GridCenter.x + (c - (GridSide - 1) * 0.5f) * GridSpacing;
                    float z = GridCenter.y + (r - (GridSide - 1) * 0.5f) * GridSpacing;
                    SpawnOne(w, TrainingMode.DummyId, new Vector2(x, z), DummyScale, false,
                             "DUMMY_" + (r + 1) + "_" + (c + 1));
                }
            }

            // ── 1 个巨人稻草人：同一套数据，体型 ×2.5 ──
            SpawnOne(w, TrainingMode.DummyBossId, BossPos, BossScale, true, "DUMMY_GIANT");

            // ── ★ v312：1 个**靶场稻草人**（会开枪；`kind = "Shooter"`，站桩，面向玩家）★ ──
            //   ⚠️ `refill: false` 是**这条链的命门**：它必须能被真的打死
            //      （那 10 个的 `refill` 默认 true ⇒ 行为一个字没变）✓
            //   ⚠️ 它**必须在这里被生成**：`TrainingMode.IsDummyId` 认了它 ⇒ 战斗表现层
            //      （`ProceduralBugs` / `WorldView`）对它的身体**让路** ⇒ 本文件不画 = 它看不见 ✗
            //      所以这条语句不是"再加一个装饰"，而是"身体被藏掉之后唯一的画师" ✓
            _gunner = SpawnOne(w, TrainingMode.GunnerId, GunnerPos, GunnerScale, false,
                               "DUMMY_GUNNER", refill: false, facePlayer: true);

            Log.Info(Tag + $"已生成 {_dummies.Count} 个稻草人（{GridSide}×{GridSide} 九宫格，间距 "
                         + $"{GridSpacing:0.0}m，中心 ({GridCenter.x:0.0},{GridCenter.y:0.0})）"
                         + $"＋ 巨人 1 个（×{BossScale:0.0}，位于 ({BossPos.x:0.0},{BossPos.y:0.0})）"
                         + $"＋ 靶场稻草人 1 个（会开枪，位于 ({GunnerPos.x:0.0},{GunnerPos.y:0.0})）"
                         + $"｜耕地 {w.Farm.Columns} 列 × {w.Farm.Rows} 行（上限 {w.Farm.MaxRows} 行）"
                         + $"｜金币 {s.Gold}（每帧补到 {TrainingMode.GoldTopUp}）");
        }

        /// <summary>
        /// 生成一只稻草人并记一行日志。**返回**它的记录（失败返回 null）——
        /// 靶场稻草人要靠这个返回值存进 <see cref="_gunner"/> 给 HUD 读血量 ✓
        ///
        /// ★ v312：新增两个**带默认值**的参数 ★
        ///   `refill`（默认 true = 击不死的无限血靶子）、`facePlayer`（默认 false = 固定朝 180°）。
        ///   两个默认值就是**改前那 10 个稻草人的实际取值** ⇒ 原有的两个调用点一个字都不用改，
        ///   行为逐字节相同 ✓（新增的只有靶场稻草人那一个调用点）
        /// </summary>
        private Dummy SpawnOne(World w, string id, Vector2 pos, float scale, bool giant, string name,
                               bool refill = true, bool facePlayer = false)
        {
            // ★ 唯一的生成入口就是战斗自己的那一个（不另造刷怪路径）★
            //   `World.SpawnEnemy(id, pos, wave, bossIndex)`：wave = 1 ⇒ 曲线起点；
            //   `bossIndex` 只有 `EnemyKind.Boss` 才吃（我们这几条是 Wander / Shooter）
            //   ⇒ 传 1 与不传等价，写出来只是让读数可复现 ✓
            //   ★ v312：靶场稻草人也是**走这一条**（`kind = "Shooter"` ⇒ `Actors.cs:1799`
            //     的 switch 会 `new ShooterEnemy()`）—— 子弹/冷却/索敌全部由战斗那套负责，
            //     本文件只负责"把它摆在哪、长什么样、打不打得死" ✓
            w.SpawnEnemy(id, pos, SpawnWave, 1);

            // 取**刚刚生成的那一只**：`SpawnEnemy` 只会 `Enemies.Add(e)` 到末尾，
            // 我们从后往前找第一条 id 相符的就是它 ✓
            // ⚠️ 不要用"位置相等"来找 —— `SpawnEnemy` 会先过一遍
            //   `World.ClampToArena(pos, def.Radius, arenaHalf)`（Actors.cs:1751），
            //   贴边的格子上被钳过之后坐标就与原值不同了 ✗
            EnemyActor e = null;
            for (int i = w.Enemies.Count - 1; i >= 0; i--)
            {
                EnemyActor cand = w.Enemies[i];
                if (cand != null && cand.Def != null && cand.Def.Id == id) { e = cand; break; }
            }
            if (e == null)
            {
                Log.Error(Tag + $"生成 {id} 之后在 World.Enemies 里找不到它（enemies.json 缺这条？）");
                return null;
            }

            var d = new Dummy
            {
                Enemy = e, LastHp = e.Health.Hp, IsGiant = giant,
                Refill = refill, FacePlayer = facePlayer      // ★ v312：默认 = 改前的取值 ✓
            };
            d.Go = BuildScarecrow(_root, name, scale, out d.Parts, out d.BaseColors);
            d.Go.transform.position = new Vector3(e.Pos.x, 0f, e.Pos.y);
            d.Go.transform.rotation = Quaternion.Euler(0f, DummyYaw, 0f);
            _dummies.Add(d);

            Log.Info(Tag + $"{name}: {id} hp={e.Health.MaxHp:0}（基表 {e.Def.BaseHp:0} × 第{SpawnWave}分钟曲线）"
                         + $" moveSpeed={e.Def.MoveSpeed:0.##} touchDamage={e.Def.TouchDamage:0.##}"
                         + $" 半径={e.Radius:0.00} 落点=({e.Pos.x:0.00},{e.Pos.y:0.00})"
                         + $" 补血={(refill ? "是" : "否（可打死）")} 朝向={(facePlayer ? "跟玩家" : "固定180°")}");
            return d;
        }

        private void ClearDummies()
        {
            for (int i = 0; i < _dummies.Count; i++)
            {
                if (_dummies[i].Go != null) Destroy(_dummies[i].Go);
            }
            _dummies.Clear();
            _gunner = null;          // ★ v312：旧记录随身体一起作废（否则 HUD 会读到上一局的靶场稻草人）
            _stats.Reset();
        }

        // ══════════════════════════════════════════════════════════════════
        // 逐帧：位置跟随 + 受击闪白 + 伤害记账 + 补血
        // ══════════════════════════════════════════════════════════════════

        private void SyncDummies()
        {
            for (int i = _dummies.Count - 1; i >= 0; i--)
            {
                Dummy d = _dummies[i];
                EnemyActor e = d.Enemy;
                if (e == null || d.Go == null) { _dummies.RemoveAt(i); continue; }

                d.Go.transform.position = new Vector3(e.Pos.x, 0f, e.Pos.y);

                // ── 朝向（★ v312）──
                //   `d.FacePlayer` 为假（那 10 个）⇒ 恒 `DummyYaw = 180°`，与改前**逐字节相同** ✓
                //   `d.FacePlayer` 为真（靶场稻草人）⇒ 每帧朝玩家：
                //     稻草人的脸建在**局部 +Z**（`BuildScarecrow` 的 Eye / Mouth 都在 z > 0），
                //     而根节点绕 Y 转 yaw 之后，局部 +Z 指向世界 `(sin yaw, 0, cos yaw)`
                //     ⇒ 要它"看着玩家"就取 `yaw = atan2(Δx, Δz)`（注意 `Vector2.y` **就是世界 Z**）✓
                //   死了就"倒下去"：绕**局部 X 轴** +78°（`FallenPitch`）⇒ 顶部朝它面朝的方向倒。
                //   `转向 × 倾倒` 的合成顺序是先绕自身 X 倒、再整体转向（`A * B` 里 B 先作用）✓
                float yaw = DummyYaw;
                if (d.FacePlayer && PlayerPos(out Vector2 tp))
                    yaw = Mathf.Atan2(tp.x - e.Pos.x, tp.y - e.Pos.y) * Mathf.Rad2Deg;
                Quaternion upright = Quaternion.Euler(0f, yaw, 0f);
                d.Go.transform.rotation = e.Alive
                    ? upright
                    : upright * Quaternion.Euler(FallenPitch, 0f, 0f);

                // ── 伤害记账：Hp 掉了多少就是这一帧吃了多少伤害 ──
                float hp = e.Health.Hp;
                if (hp < d.LastHp) _stats.AddDamage(d.LastHp - hp);

                // ── 无限血量：掉到 50% 以下就补满（结构性，不靠大数硬撑）──
                //   ★ v312：**只对 `Refill` 为真的目标生效** —— 靶场稻草人是要被打死的，
                //     补血会把它变回"无敌"（那正是本轮要避免的事）✓
                if (d.Refill && hp < e.Health.MaxHp * RefillBelowPct) e.Health.Hp = e.Health.MaxHp;

                d.LastHp = e.Health.Hp;

                // ── 受击闪白：读战斗唯一的那个信号 `Actor.HitFlash`（不改它，只读）──
                //   走 `MaterialPropertyBlock` 而不是 `renderer.material`：
                //   后者会**克隆一份材质实例**（每次访问都克隆）⇒ 10 个稻草人 × 15 个零件
                //   就是 150 份泄漏的材质 ✗ 而 MPB 只是"这一次绘制的覆盖值"，零克隆 ✓
                bool flash = e.HitFlash > 0f;
                // ★ v302：走 `Mpb()` 懒创建 —— `MaterialPropertyBlock` 是 Unity 对象，
                //   不能在类型静态初始化器里 new（会抛 CreateImpl not allowed，
                //   进而 TypeInitializationException ⇒ 训练场一进就报错）★
                MaterialPropertyBlock mpb = Mpb();
                for (int p = 0; p < d.Parts.Length; p++)
                {
                    Renderer r = d.Parts[p];
                    if (r == null) continue;
                    Color c = flash ? Color.Lerp(d.BaseColors[p], Color.white, 0.85f) : d.BaseColors[p];
                    mpb.Clear();
                    mpb.SetColor(BaseColorId, c);
                    mpb.SetColor(ColorId, c);
                    r.SetPropertyBlock(mpb);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 稻草人外观（程序化；只在生成那一刻建一次）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v312：取玩家当前的世界坐标（`Vector2.y` 就是世界 Z）。
        /// 返回 false = 现在没有玩家 ⇒ **调用方保持原朝向**，不要拿 (0,0) 去算角度
        /// （拿原点算会让稻草人朝地图中心转一下，而那一刻场上根本没有可看的目标）✓
        /// </summary>
        private bool PlayerPos(out Vector2 pos)
        {
            World w = _bound != null ? _bound.World : null;
            if (w != null && w.Player != null) { pos = w.Player.Pos; return true; }
            pos = Vector2.zero;
            return false;
        }

        /// <summary>建一个稻草人：木杆 + 横梁 + 草垛身体 + 麻布袋头 + 草帽 + 两条手臂。</summary>
        private static GameObject BuildScarecrow(Transform parent, string name, float scale,
                                                 out Renderer[] parts, out Color[] colors)
        {
            var list = new List<Renderer>(16);
            var cols = new List<Color>(16);

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * scale;

            Material wood = Mat(new Color(0.42f, 0.30f, 0.17f));
            Material straw = Mat(new Color(0.86f, 0.72f, 0.33f));
            Material sack = Mat(new Color(0.78f, 0.68f, 0.49f));
            Material cloth = Mat(new Color(0.35f, 0.47f, 0.31f));
            Material dark = Mat(new Color(0.14f, 0.12f, 0.10f));

            // 主杆：从地面到 2.4m（圆柱图元默认高 2m ⇒ localScale.y = 高度/2）
            Part(root.transform, "Pole", PrimitiveType.Cylinder, new Vector3(0f, 1.20f, 0f),
                 new Vector3(0.16f, 1.20f, 0.16f), wood, list, cols);
            // 横梁（肩膀）
            Part(root.transform, "Crossbar", PrimitiveType.Cube, new Vector3(0f, 1.72f, 0f),
                 new Vector3(1.62f, 0.13f, 0.13f), wood, list, cols);
            // 草垛身体（十字交叉的草束）
            Part(root.transform, "Body", PrimitiveType.Cylinder, new Vector3(0f, 1.16f, 0f),
                 new Vector3(0.62f, 0.52f, 0.62f), cloth, list, cols);
            Part(root.transform, "BodyStraw", PrimitiveType.Cube, new Vector3(0f, 0.72f, 0f),
                 new Vector3(0.72f, 0.20f, 0.72f), straw, list, cols);
            // 两条手臂：从横梁两端垂下
            for (int side = -1; side <= 1; side += 2)
            {
                Part(root.transform, "Arm" + side, PrimitiveType.Cylinder,
                     new Vector3(side * 0.74f, 1.52f, 0f), new Vector3(0.11f, 0.22f, 0.11f),
                     straw, list, cols);
            }
            // 头：麻布袋 + 头顶草束
            Part(root.transform, "Head", PrimitiveType.Sphere, new Vector3(0f, 2.06f, 0f),
                 new Vector3(0.56f, 0.56f, 0.56f), sack, list, cols);
            Part(root.transform, "HeadStraw", PrimitiveType.Cylinder, new Vector3(0f, 2.30f, 0f),
                 new Vector3(0.40f, 0.06f, 0.40f), straw, list, cols);
            // 草帽：宽檐 + 帽顶
            Part(root.transform, "HatBrim", PrimitiveType.Cylinder, new Vector3(0f, 2.32f, 0f),
                 new Vector3(0.92f, 0.035f, 0.92f), straw, list, cols);
            Part(root.transform, "HatTop", PrimitiveType.Cylinder, new Vector3(0f, 2.44f, 0f),
                 new Vector3(0.36f, 0.10f, 0.36f), straw, list, cols);
            // 脸：两只眼睛 + 一条缝（朝向 +Z；父节点整体转了 180° 面向玩家）
            for (int side = -1; side <= 1; side += 2)
            {
                Part(root.transform, "Eye" + side, PrimitiveType.Cube,
                     new Vector3(side * 0.12f, 2.10f, 0.26f), new Vector3(0.09f, 0.09f, 0.04f),
                     dark, list, cols);
            }
            Part(root.transform, "Mouth", PrimitiveType.Cube, new Vector3(0f, 1.95f, 0.27f),
                 new Vector3(0.22f, 0.045f, 0.04f), dark, list, cols);
            // 底座：一小堆土（告诉玩家"它是钉在地上的"）
            Part(root.transform, "Base", PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f),
                 new Vector3(0.86f, 0.03f, 0.86f), Mat(new Color(0.34f, 0.27f, 0.19f)), list, cols);

            parts = list.ToArray();
            colors = cols.ToArray();
            return root;
        }

        private static GameObject Part(Transform parent, string name, PrimitiveType type,
                                       Vector3 pos, Vector3 scale, Material mat,
                                       List<Renderer> parts, List<Color> colors)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.name = name;
            Transform t = g.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.identity;
            t.localScale = scale;

            Collider col = g.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);   // 判定走敌人逻辑（`World` 的圆形距离），不要物理体

            Renderer r = g.GetComponent<Renderer>();
            if (r != null)
            {
                if (mat != null) r.sharedMaterial = mat;
                parts.Add(r);
                colors.Add(mat != null && mat.HasProperty("_BaseColor")
                    ? mat.GetColor("_BaseColor")
                    : (mat != null && mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white));
            }
            return g;
        }

        private static readonly Dictionary<Color, Material> Mats = new Dictionary<Color, Material>(8);
        private static Shader _shader;
        private static bool _shaderTried;

        /// <summary>按颜色取共享材质（同色只建一次；URP Lit 主色是 `_BaseColor`，兜底链同 TrainingKit）。</summary>
        private static Material Mat(Color c)
        {
            if (Mats.TryGetValue(c, out Material m) && m != null) return m;
            if (!_shaderTried)
            {
                _shaderTried = true;
                _shader = Shader.Find("Universal Render Pipeline/Lit");
                if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (_shader == null) _shader = Shader.Find("Standard");
                if (_shader == null) _shader = Shader.Find("Sprites/Default");
                if (_shader == null) Log.Warn(TrainingWarning);
            }
            m = _shader != null ? new Material(_shader) : null;
            if (m == null) return null;
            m.name = "TRAIN_Mat";
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            Mats[c] = m;
            return m;
        }

        private const string TrainingWarning =
            "[训练场] 找不到任何可用着色器（连 Sprites/Default 都没有）⇒ 稻草人会是默认材质";
    }
}