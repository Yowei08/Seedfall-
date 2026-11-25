using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Seedfall
{
    /// <summary>
    /// ★ v256：**晴天太阳**（天空圆盘 + 光晕 + 星芒）★★
    ///
    /// 用户原话："修改晴天效果：太阳在正中心天空，且不要遮挡角色，加入光效"。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、本轮之前的晴天长什么样（先说现状，因为改动方向由它决定）
    /// ══════════════════════════════════════════════════════════════════════
    /// 晴天 = `ProgMechanics.Weather == WeatherKind.Sunny`（`ProgMechanics.cs:37`，
    /// 由 `PlantMechanics.SunnyBloom` 在整点掷骰，`ProgMechanics.cs:370-391`），
    /// **表现层只有一个东西**：`WorldView.SyncWeatherFx` 里那颗 1 米棱面的世界球
    /// `ProgSun`（`WorldView.Prog.cs:563-594`）—— 固定在场地坐标
    /// `(-half*0.30, 13.5, -half*0.30)`、随强度放大到 `1.05~1.60`，外加挂在它下面的
    /// 一盏 `Point` 光（`WorldView.Prog.cs:570-578`）。
    ///   ⇒ 它是**世界里的一颗球**，摆在"靠镜头这一侧"（z 取负，见 `:587-589` 的注记）
    ///   ⇒ 用户看到的问题正是这个：球离镜头近、又被放大 ⇒ 玩家走到那一侧时，
    ///     这颗"太阳"会**压在角色身上**（它是透明材质，画在角色之后）✗
    ///   ⇒ 而且它不是"天空里的太阳"，是一颗悬在场上空的球 ✗
    /// 它**不是**平行光的颜色/天空盒开关：主平行光归 `NightLighting`，天空是相机
    /// `clearFlags = SolidColor`（`GameBootstrap.cs:521-522`）—— 本作**没有天空盒**。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、本文件的做法：一块**跟着相机走的世界四边形**（不是 Canvas）★
    /// ══════════════════════════════════════════════════════════════════════
    /// ```
    /// [DontDestroyOnLoad] COMBAT_SUN_DISC      ← 只有本组件 + 一块 1×1 四边形网格
    ///    └ SunRoot                            ← 显隐开关只动这一层（组件本身必须活着）
    ///         ├ SunBeam  散射光束 26 世界单位 暖白偏金 α 0.20（★v310，缓慢摆动+呼吸）
    ///         ├ SunHalo  光晕 14 世界单位  金色 α 0.35
    ///         ├ SunRays  星芒 14 世界单位  金色 α 0.18（绕屏幕法线缓慢自转）
    ///         ├ SunDisc  圆盘  5 世界单位  暖白 α 0.95
    ///         ├ SunGlow  柔光幕 11 世界单位 暖白偏金 α 0.12（呼吸）
    ///         └ SunPulse 脉冲  每 4 秒一次（0.85s，α 0.20 → 0）
    /// ```
    /// · **不依赖 Canvas / IMGUI / PostProcessing**：六块 `MeshRenderer` + **四张**
    ///   **代码生成的 128×128 贴图**（`Texture2D`，`HideFlags.DontSave`，静态缓存）。
    ///   ★ v310 之前是三张（圆盘 / 光晕 / 星芒）—— 第四张是**散射光束扇**（`BeamTexture`）✓
    /// · **billboard**：`transform.rotation = Camera.main.transform.rotation`
    ///   ⇒ 四边形平面恒等于相机的近平面平面 ⇒ 永远是"正对着镜头的一片"，看不出是四边形 ✓
    /// · **每帧跟随相机**：太阳是"无限远"的 ⇒ 位置必须相对相机固定，与玩家位置无关
    ///   （玩家怎么跑，太阳在画面上的位置一动不动）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、位置公式与**为什么不是需求里那组数**（这一节是本轮唯一的偏离，证据在此）
    /// ══════════════════════════════════════════════════════════════════════
    /// 公式（本文件）：`cam.pos + cam.forward*60 + cam.up*24 − cam.right*6`
    ///   · 60：距离。相机 far = 200（`GameBootstrap.cs:524`）⇒ 65 的长度安全在裁剪面内 ✓
    ///   · **24 而不是需求给的 48**：需求那组数在**本作的相机参数下会跑到画面外**。
    ///     实测参数（`tuning.json:467-476`）：`fov = 62`、`pitch = 60`、`distance = 22`、
    ///     `orthographic = false`。屏幕竖直半高在 60 距离处 = `60 × tan(31°) = 36.1` 世界单位，
    ///     而 `up*48` 对应 `atan(48/60) = 38.7° > 31°` ⇒ **太阳整个被推到画面上缘之外** ✗
    ///     取 24 ⇒ `atan(24/60) = 21.8°`，落在画面上方 **0.83 视口高度**（半高的 67% 处）
    ///     ⇒ 是"上方正中的天空"，而不是"贴边的一条"✓
    ///   · −6 向左：视口上约 −0.047 宽 ⇒ 略偏左，不显得像一张贴纸 ✓
    /// · 尺寸（世界单位 ⇒ 屏幕占比，同一套相机参数下）：
    ///     屏幕竖直全长 = `2 × 36.1 = 72.1` ⇒ 光晕 14 占 **19.4%**、圆盘 5 占 **6.9%**。
    ///     ⚠️ 需求写"14 单位 ≈ 屏幕高度 12~16%"，那是按 ≈84° FOV 估的；
    ///        本作 fov 62 ⇒ 同为 14 单位是 19.4%。**尺寸按需求给的 14/5 落地**，
    ///        百分比以这里的实测值为准（要更小就只改 `HaloWorldHeight` 一个数）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、★ "不遮挡角色"的两道结构性保证（★ v310 已改写，含一条必须说清的结论）★
    /// ══════════════════════════════════════════════════════════════════════
    /// ① **屏幕位置**：太阳恒在视口 0.83 高度、偏左 0.047 —— 角色（follow 镜头
    ///    `followLerp = 10`，`tuning.json:474`）永远在视口中央 0.5 附近；
    ///    ★ v310 复核过这个数（v256 写的"要 13 米"是估的，按角度算是 **5.1 米**）：
    ///      屏幕 y 只由"视线与相机轴的夹角"决定。圆心在 21.8° 上方（= 视口 0.83），
    ///      玩家要到 0.795 就得爬到 20.5° 上方 ⇒ 它所在的地面点要从镜头 11m 处推到 **16.1m** 处
    ///      ⇒ 镜头得落后焦点 **5.1m**（= 玩家以 ≈51 m/s 跑，实战不可能）✓
    /// ② **每帧矩形相交守卫**（`PlayerUnderSun`，★ v310 放宽并改写）：
    ///    **太阳圆盘**的屏幕矩形 × **玩家身体**（脚底→头顶+左右半宽）的屏幕矩形，
    ///    两边**各扩 20px**，相交 ⇒ 全部六层 α 再乘 **0.50**（0.15s 过渡）。
    ///    ★ v313：太阳本身**默认已经乘 0.50**（`SunAlphaScale`）⇒ 相交时净 α = 基准 × **0.25**。
    ///    ⚠️ 结论：**在本作稳态下它仍然不会触发**（数学见 `PlayerUnderSun` 的注释）——
    ///    它是"万一有别的系统把玩家推上去 / 把镜头冻住"的保险。
    ///    要验证它真的工作，用 `DebugSetUpOffset`（只改太阳自己的上抬量，不影响游戏逻辑）：
    ///      `unity_drive send COMBAT_SUN_DISC CombatSunDisc DebugSetUpOffset 4`
    ///    ⇒ 太阳落到视口 0.56，与玩家矩形相交 ⇒ `GuardHides` 涨、`_vis` → 0.50，
    ///      `CombatSunDisc.Status()` 里能看到两个矩形的实际数值 ✓
    /// ③ **无 Collider / 无影子 / 不写深度**：`shadowCastingMode = Off`、`receiveShadows = false`、
    ///    从不 `AddComponent<BoxCollider>` ⇒ 既不挡视线碰撞，也不参与物理（`PlayerFacingMarker.cs:86-88`
    ///    同一条纪律：多一个碰撞体就会把玩家的胶囊扫掠挡住）✓
    ///
    /// ⚠️ **偏离：renderQueue 不是 1990~2000，而是 3000~3002**。理由是可复现的：
    ///   1990~2000 落在**不透明队列之前** ⇒ 会被不透明物体**整块盖掉**。本作恰好是
    ///   "不透明铺满全屏"的极端情形 —— 地面是一张 `Plane` 缩放 `half*0.6` 的网格
    ///   （`WorldView.cs:724-726`，Unity 的 Plane 是 10×10 ⇒ 边长 120 米，中心在原点，
    ///   覆盖 ±60），而相机俯角 60°、半 FOV 31° ⇒ **画面上缘仍在水平线之下 29°**，
    ///   视线在 19.05/sin(29°) ≈ 39 米处就落到地面上 ⇒ **整张画面都是地面，没有天空**。
    ///   太阳摆在 60 单位 ⇒ 深度比地面远 ⇒ 1990 队列里画完立刻被地面覆盖 ⇒ **一个像素都看不见** ✗
    ///   ⇒ 只能画在地面之后（≥3000）。"不遮挡角色"改由上面的 ①②③ 保证 ✓
    ///   顺带说明：旧的 `ProgSun` 也是透明材质（`MatCached(..., true)`）、同样画在不透明之后 ——
    ///   本文件只是把"会盖住角色的那颗球"换成了"恒定在画面上方的一片" ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、颜色：只用**暖白**与**金色**两档（与工程 UI 同一套语汇）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 圆盘 `(1.00, 0.95, 0.78)` α 0.95 —— 暖白，最亮的一档
    ///   · 光晕 `(1.00, 0.86, 0.55)` α 0.35 —— 金
    ///   · 星芒 `(1.00, 0.86, 0.55)` α 0.18 —— 同金、更淡（"更退后"）
    /// 不引入第四种色相（红/青/紫一律不用）；贴图本身是**纯白 + α 形状**，
    /// 颜色只在材质 `_Color` 里给 ⇒ 三张图共用同一条生成管线 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 六、只在晴天显示（只读入口，每 0.5 秒读一次）
    /// ══════════════════════════════════════════════════════════════════════
    /// `ShouldShow()` 的三个条件，全部**只读**：
    ///   ① `ProgMechanics.Weather == WeatherKind.Sunny`（`ProgMechanics.cs:37`）
    ///   ② `ProgMechanics.WeatherIntensity01 > 0.01`（`ProgMechanics.cs:52-65`，淡入/淡出）
    ///   ③ **主城/登录时不显示**：`SceneManager.GetSceneByName("Town").isLoaded == false`
    ///      —— 主城是一个**独立场景**（`LoginBootstrap.cs:68` 的 `TownSceneName`，
    ///      由 `LoginBootstrap.cs:194` 以 additive 叠在 `Login` 上）；进战斗是
    ///      `LoadScene` 单场景切换（`GameBootstrap.cs:212` 的退出方向同理）
    ///      ⇒ "Town 已加载" = "我在主城/登录"，那里没有战斗天气（`TownLife.ForceSunny`
    ///      `TownLife.cs:59` 走的是**昼夜**那套，与本天气系统无关）✓
    ///   为什么不每帧读：`Weather` 的翻转是整点掷骰（45 秒窗口），0.5 秒的轮询延迟
    ///   相对这个时间尺度可以忽略，换来的是**稳态 0 次静态属性读**（与
    ///   `PlayerFacingMarker.ColorRefreshSeconds` 同一种"低频读状态"的写法）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 七、性能与读数
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 宿主 1 个（`Instances` 恒 1）、网格 1 份、贴图 3 张、材质 3 份：**建一次**（静态缓存）
    ///   · 每帧写入：`position` 1 次 + 相机 `rotation` 1 次（billboard）+ 星芒 `localRotation` 1 次
    ///   · 顶点数 4（两面共 12 索引）；无 Collider、无阴影、无 ZWrite
    ///   · `Status()` 一行读数（探针 / 截图叠字用）
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatSunDisc : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════
        // 一、位置（相对相机；见类注释第三节）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>到相机的距离（世界单位）。须 &lt; 相机 far（200）。</summary>
        public const float CameraDistance = 60f;
        /// <summary>沿相机 up 抬起的量。★ 不是 48 —— 那会跑出画面上缘（类注释第三节）★</summary>
        public const float UpOffset = 24f;
        /// <summary>沿相机 right 向左的量（"略偏左，不像贴纸"）。</summary>
        public const float LeftOffset = 6f;

        // ══════════════════════════════════════════════════════════════════
        // 二、尺度（世界单位。四边形网格是 1×1 ⇒ localScale 直接等于世界尺寸）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>光晕的世界高度（≈ 屏幕高度 19.4%，见类注释第三节）。</summary>
        public const float HaloWorldHeight = 14f;
        /// <summary>圆盘的世界直径（= 光晕的 5/14 ⇒ 视觉上"核 + 晕"）。</summary>
        public const float DiscWorldHeight = 5f;
        /// <summary>星芒的世界高度（与光晕同尺度，藏在晕里）。</summary>
        public const float RaysWorldHeight = 14f;
        /// <summary>★ v308：**柔光幕**的世界直径 = 圆盘 × `GlowScale`（用户：「给圆盘外面加一层更大的柔光幕，
        /// 例如直径 ×2.2，α 0.12，随时间轻微呼吸」）⇒ 5 × 2.2 = **11**。
        /// 它夹在圆盘（5）与光晕（14）之间 ⇒ 画面上是"核 → 软幕 → 晕 → 芒"四档过渡，
        /// 而不是"一个实心圆 + 一圈突然变淡" ✓</summary>
        public const float GlowScale = 2.2f;
        public const float GlowWorldHeight = DiscWorldHeight * GlowScale;
        /// <summary>★ v308：柔光幕的呼吸幅度（±20%）与速度（弧度/秒）。</summary>
        public const float GlowBreathAmp = 0.20f;
        public const float GlowBreathSpeed = 1.05f;
        /// <summary>★ v308：**光线脉冲**的周期（秒）—— 用户指定"每 4 秒一次"。</summary>
        public const float PulsePeriod = 4f;
        /// <summary>★ v308：单次脉冲的时长（秒）与峰值 α（用户指定 α 0.2 → 0）。</summary>
        public const float PulseSeconds = 0.85f;
        public const float PulseAlpha = 0.20f;
        /// <summary>★ v308：脉冲的起止直径（× 圆盘直径）—— 从核外一点扩到光晕之外。</summary>
        public const float PulseFrom = 1.10f;
        public const float PulseTo = 3.00f;
        /// <summary>
        /// ★ v313：**太阳的整体透明度倍数 = 0.50**（用户原话「太阳改为默认透明度 50%」）。
        ///
        /// ── 它乘在哪（只有一个地方）──
        ///   `LateUpdate` 里 `float sunA = SunAlphaScale * _vis;`，然后 **六层**（圆盘 / 光晕 / 星芒 /
        ///   柔光幕 / 脉冲 / 散射光束）各用自己的基准 α 乘 `sunA` ⇒ 换这一个数就是"整颗太阳变透明"，
        ///   不必再去翻六个常量（改前那六个 α 是写死在 `DiscColor / GoldColor / RaysColor /
        ///   GlowColor / PulseAlpha / BeamAlpha` 里的，逐层改会漏、会漂）。
        ///
        /// ── 为什么是"乘 0.5"而不是"另写一套 50% 的常量"──
        ///   保留下来的每一层**相对亮度差**与改前**逐位相同**（圆盘仍是晕的 2.7 倍、晕仍是芒的 1.9 倍）
        ///   ⇒ 画面读起来还是"核 → 幕 → 晕 → 芒 → 光"那套层次，只是整体淡了一档。
        ///   另写一套常量则会顺手把层次关系改掉（那就是两个改动了，不是用户要的那一个）✓
        ///
        /// ── 为什么"默认就降"是对的（用户意图：确保玩家可见）──
        ///   改前是"只有被玩家挡住才降"（`OccludedVisibility`），而本作相机跟随玩家
        ///   ⇒ 玩家恒在视口 0.50、太阳圆盘恒在 0.83 ⇒ **那条判据实战里永不命中**（见 `PlayerUnderSun`
        ///   的推导），也就是"降透明度"这件事**从来没发生过**。改成默认 0.50 之后
        ///   玩家在任何位置看到的都是一颗"退到后面去的太阳"✓
        /// </summary>
        public const float SunAlphaScale = 0.50f;
        /// <summary>★ v308：**遮挡时在 `SunAlphaScale` 的基础上再乘的倍数**。
        /// ★ v313：语义从"绝对可见度"改成"**相对倍数**"（0.40 原本是"整颗太阳降到 40%"，
        ///   现在太阳已经默认 50% 了，若仍取 0.40 会降到 **20%** —— 比用户要的"再降一半"更淡、更容易看不见）。
        ///   取 **0.50** ⇒ 遮挡时净 α = 基准 × 0.50 × 0.50 = **基准的 25%**（正好是"50% 再砍一半"）✓
        /// </summary>
        public const float OccludedVisibility = 0.50f;
        /// <summary>★ v308：可见度过渡时长（秒）。★ v310：0.20 → **0.15**（用户指定的 0.15 秒）。
        /// 落地方式见 `LateUpdate`：速率 = `(1 − OccludedVisibility) / 本值` = 0.50 / 0.15 ≈ 3.33 /秒 ⇒
        /// 1.00 → 0.50 用 0.15s，0.50 → 1.00 也用 0.15s（**双向同速**，不会"淡出慢、弹回快"）✓</summary>
        public const float VisibilityBlendSeconds = 0.15f;

        // ══════════════════════════════════════════════════════════════════
        // ★ v310：**散射光束（god rays）** —— 用户：「晴天的太阳增加可见的散射光束」
        //   做法：**一张 128×128 的"锥形光束扇"贴图 + 一块比光晕更大的方片**（不是网格堆叠）。
        //   为什么不用"多个拉长的菱形"：那是 8 个物体、8 次矩阵，还要自己算朝向；
        //   一张贴图 + 一块方片 = 1 个渲染体、1 次 UV 采样，而且**光的软边天生就在贴图里** ✓
        //   光束条数 / 张角 / 长短 / 亮度差别全部烘进贴图（`BeamTexture`），
        //   运行期只做两件事：**整体缓慢摆动**（±`BeamSwayDeg`）+ **呼吸**（α ±22%）✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>光束条数（用户给的是 6~10 条，取 **8**）。</summary>
        public const int BeamCount = 8;
        /// <summary>光束层的世界直径 —— **26**（> 光晕的 14）⇒ 光束伸出光晕之外，读作"射出来的光"。</summary>
        public const float BeamWorldHeight = 26f;
        /// <summary>贴图归一化半径：这以内不画（被圆盘 / 柔光幕压住，否则中心糊成一片白）。</summary>
        public const float BeamInnerR = 0.14f;
        /// <summary>光束在太阳中心的半张角（度）→ 到外缘再张开 `BeamHalfAngleGrow` ⇒ **锥形** ✓</summary>
        public const float BeamHalfAngle0 = 3.0f;
        public const float BeamHalfAngleGrow = 6.0f;
        /// <summary>光束的峰值 α（用户给的是 0.20 → 0）：中心最亮 0.20，外缘收到 0。</summary>
        public const float BeamAlpha = 0.20f;
        /// <summary>整体摆动的幅度（度）与角频率（弧度/秒，周期 ≈ 18s）—— 慢到不抢戏、快过静止。</summary>
        public const float BeamSwayDeg = 3.0f;
        public const float BeamSwaySpeed = 0.35f;
        /// <summary>α 呼吸的幅度与角频率（周期 ≈ 7.8s）。</summary>
        public const float BeamBreathAmp = 0.22f;
        public const float BeamBreathSpeed = 0.80f;
        /// <summary>光束队列：压在柔光幕（2998）之上、光晕（3000）之下。</summary>
        public const int BeamQueue = 2999;

        // ══════════════════════════════════════════════════════════════════
        // 三、颜色（只有暖白与金两档）+ 队列
        // ══════════════════════════════════════════════════════════════════
        /// <summary>圆盘：暖白、几乎不透明。</summary>
        public static readonly Color DiscColor = new Color(1f, 0.95f, 0.78f, 0.95f);
        /// <summary>光晕：金、半透明（"光"的那一层）。</summary>
        public static readonly Color GoldColor = new Color(1f, 0.86f, 0.55f, 0.35f);
        /// <summary>星芒：同金、更淡（"更退后"）。</summary>
        public static readonly Color RaysColor = new Color(1f, 0.86f, 0.55f, 0.18f);
        /// <summary>★ v308：柔光幕：暖白偏金、**很淡**（α 0.12）—— 它只负责"发光感"，不负责形状。</summary>
        public static readonly Color GlowColor = new Color(1f, 0.90f, 0.62f, 0.12f);
        /// <summary>★ v308：光线脉冲的峰值颜色（α 由 `PulseAlpha` 逐帧乘进去）。</summary>
        public static readonly Color PulseColor = new Color(1f, 0.88f, 0.60f, 0.20f);

        /// <summary>光晕队列（画在地面之后，见类注释第四节）。</summary>
        public const int HaloQueue = 3000;
        /// <summary>星芒队列（压在光晕上）。</summary>
        public const int RaysQueue = 3001;
        /// <summary>圆盘队列（最上层 ⇒ 核永远清晰）。</summary>
        public const int DiscQueue = 3002;
        /// <summary>★ v308：柔光幕队列 —— 在所有太阳层**之后画**（先铺底），但仍在不透明物体之后。</summary>
        public const int GlowQueue = 2998;
        /// <summary>★ v308：光线脉冲队列（再往后退一层，免得盖住圆盘核心）。</summary>
        public const int PulseQueue = 2997;

        // ══════════════════════════════════════════════════════════════════
        // 四、时间与贴图
        // ══════════════════════════════════════════════════════════════════
        /// <summary>星芒转一圈的秒数（需求 20~30 秒）。</summary>
        public const float RaysSpinSeconds = 25f;
        /// <summary>天气状态轮询周期（秒）。</summary>
        public const float WeatherPollSeconds = 0.5f;
        /// <summary>贴图边长（128×128，需求指定）。</summary>
        public const int TextureSize = 128;
        /// <summary>圆盘边缘的软化起始半径（0~1，贴图归一化半径）。</summary>
        public const float DiscCoreR = 0.72f;
        /// <summary>星芒的"四道放射亮条"的锐度（越大越细）。</summary>
        public const float RaysLobePower = 14f;

        // ── 角色投影守卫 ──
        /// <summary>玩家**身体的高度范围**（0 = 脚底、这个值 = 头顶）—— 判据不再只看"头那一点"，
        /// 而是把脚底 / 头顶 / 左右半宽四个投影点围成一个屏幕矩形（见 `PlayerUnderSun`）。</summary>
        public const float PlayerTopHeight = 1.60f;
        /// <summary>玩家的身体半宽（米）—— 用来量屏幕矩形的左右边（0.40 ≈ 玩家胶囊半径）。</summary>
        public const float PlayerHalfWidth = 0.40f;
        /// <summary>★ v310：判据余量（**像素**）。用户：「留一点余量（例如各扩 20px）」——
        /// 太阳矩形与玩家矩形**各扩 20px** 后再判相交 ⇒ 边缘蹭到也会降档 ✓</summary>
        public const float GuardMarginPx = 20f;
        /// <summary>找不到玩家时的重试周期（秒）。</summary>
        public const float PlayerRetrySeconds = 1f;

        private static readonly string[] PlayerNames = { "MDL_farmer", "Player", "TownPlayer" };

        // ══════════════════════════════════════════════════════════════════
        // 读数
        // ══════════════════════════════════════════════════════════════════
        /// <summary>存活的宿主实例数（恒 1）。</summary>
        public static int Instances;
        /// <summary>渲染层被搭起来的次数（恒 1：宿主 DDOL，跨场景不重建）。</summary>
        public static int Builds;
        /// <summary>天气轮询次数。</summary>
        public static int WeatherPolls;
        /// <summary>轮询里判定为"晴天 ⇒ 显示"的次数。</summary>
        public static int SunnyPolls;
        /// <summary>角色投影守卫把太阳**降档**的次数（★ v308：不再是"隐藏次数"，而是"再降一档的次数"；
        /// ★ v313：现在"再降一档" = 在默认 50% 的基础上再乘 `OccludedVisibility`(0.50) ⇒ 净 25%）。</summary>
        public static int GuardHides;
        /// <summary>上一次判定为可见/不可见的原因（读数）。</summary>
        public static string LastReason = "(未判定)";
        /// <summary>实际用到的着色器名（带 FALLBACK 后缀 = 退回 URP 不透明）。</summary>
        public static string LastShader = "(未建)";
        /// <summary>上一次画出的太阳世界坐标。</summary>
        public static Vector3 LastPos;
        /// <summary>★ v310：上一帧的**太阳圆盘屏幕矩形**（视口坐标 `(x0,y0,x1,y1)`）—— 探针读数用。</summary>
        public static Vector4 LastSunRect;
        /// <summary>★ v310：上一帧的**玩家屏幕矩形**（含身体高度范围与 20px 余量）—— 探针读数用。</summary>
        public static Vector4 LastPlayerRect;
        /// <summary>
        /// ★ v310：**只给验证用的**上抬量覆盖（0 = 正常运行 ⇒ 用 `UpOffset` 的 24）。
        /// 为什么需要它：本作相机跟随玩家 ⇒ 玩家稳态下恒在视口 0.50，而太阳圆盘在 0.83，
        /// 20px 余量补不上 0.2 视口 ⇒ **"挡住玩家"在实战里永远不会发生**（见 `PlayerUnderSun` 的推导）。
        /// 要让这条判据真的被走到一次，唯一的办法就是把太阳"拉下来"：
        ///   `unity_drive send COMBAT_SUN_DISC CombatSunDisc DebugSetUpOffset 4`
        /// ⇒ 圆心视口 y ≈ 0.56，与玩家矩形相交 ⇒ `GuardHides` 涨、`_vis` → 0.50（净 α = 基准 × 0.25）✓
        /// 还原：传 0 或 `"0"`。**它不在任何游戏逻辑里被读，只影响太阳自己的位置** ✓
        /// </summary>
        public static float DebugUpOffset = 0f;

        // ══════════════════════════════════════════════════════════════════
        // 共享资源（全场一份，静态缓存 ⇒ 换场景不重造）
        // ══════════════════════════════════════════════════════════════════
        private static CombatSunDisc _instance;
        private static Mesh _quadMesh;
        private static Texture2D _discTex;
        private static Texture2D _haloTex;
        private static Texture2D _raysTex;
        /// <summary>★ v310：散射光束的锥形光束扇贴图（代码生成 128×128，与另外三张同一套管线）。</summary>
        private static Texture2D _beamTex;
        private static Material _discMat;
        private static Material _haloMat;
        private static Material _raysMat;
        private static Material _glowMat;
        private static Material _pulseMat;
        private static Material _beamMat;

        // ── 实例状态 ──
        private GameObject _root;
        private GameObject _halo;
        private GameObject _rays;
        private GameObject _disc;
        private GameObject _glow;
        private GameObject _pulse;
        /// <summary>★ v310：散射光束那一层（比光晕更大的一块方片）。</summary>
        private GameObject _beam;
        private Transform _player;
        private float _playerRetry;
        private float _pollTimer;
        private float _spinDeg;
        private float _lifeT;          // ★ v308：太阳自己的时间（呼吸 / 脉冲都用它，不受 timeScale 影响）
        private float _vis = 1f;       // ★ v308：当前可见度倍数（1 = 正常，0.5 = 被玩家挡住）
                                       //   ★ v313：它是**相对倍数**，最终 α = 基准 × `SunAlphaScale` × `_vis`
        private bool _occluded;        // ★ v308：本帧是否命中"挡住玩家"
        private bool _sunny;
        private bool _visible;

        // ══════════════════════════════════════════════════════════════════
        // 自举（与 `PlayerFacingMarker.Install` 同一套写法）
        // ══════════════════════════════════════════════════════════════════
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("COMBAT_SUN_DISC");
            _instance = go.AddComponent<CombatSunDisc>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Instances++;
        }

        private void OnDestroy()
        {
            Instances--;
            if (_instance != this) return;
            _instance = null;
            DestroyShared();
        }

        // ══════════════════════════════════════════════════════════════════
        // 每帧：位置跟相机 + 星芒自转 + 低频读天气
        // ══════════════════════════════════════════════════════════════════
        private void LateUpdate()
        {
            // 天气与星芒都不该被 `Time.timeScale` 冻住（暂停菜单里太阳还是太阳）
            float dt = Time.unscaledDeltaTime;

            Camera cam = Camera.main;
            if (cam == null)
            {
                LastReason = "无 Camera.main";
                SetVisible(false);
                return;
            }

            _pollTimer -= dt;
            if (_pollTimer <= 0f)
            {
                _pollTimer = WeatherPollSeconds;
                _sunny = ShouldShow(out LastReason);
                WeatherPolls++;
                if (_sunny) SunnyPolls++;
            }

            // 主城是**正交**相机（`TownBootstrap.cs:952`）：太阳是透视 gadget ⇒ 不在那里画
            if (!_sunny || cam.orthographic)
            {
                if (cam.orthographic) LastReason = "主城/正交相机";
                SetVisible(false);
                return;
            }

            if (_root == null) Build();

            Transform ct = cam.transform;
            // ★ 太阳是"无限远"的 ⇒ 每帧相对相机重算，与玩家位置无关（画面位置恒定）★
            // ★ v310：`DebugUpOffset > 0` 时用覆盖值（**只**为了能验证"挡住玩家"那条判据，
            //   正常游戏里它恒为 0 ⇒ 仍用 `UpOffset`(24)，画面位置逐位不变）✓
            float upOff = DebugUpOffset > 0f ? DebugUpOffset : UpOffset;
            Vector3 pos = ct.position + ct.forward * CameraDistance
                        + ct.up * upOff - ct.right * LeftOffset;
            transform.position = pos;
            transform.rotation = ct.rotation;      // billboard：本地 +Z 对齐相机 forward
            LastPos = pos;

            // 星芒自转（只写这一层的 localRotation；父级的 billboard 已把平面摆正）
            _spinDeg = Mathf.Repeat(_spinDeg + 360f / RaysSpinSeconds * dt, 360f);
            _rays.transform.localRotation = Quaternion.Euler(0f, 0f, _spinDeg);

            _lifeT += dt;

            // ══════════════════════════════════════════════════════════════
            // ★ v310：③ **散射光束**的缓慢摆动 + 呼吸（每帧 3 次写入，不动材质以外的东西）
            //   摆动：整层绕屏幕法线转 `BeamSwayDeg·sin` ⇒ 8 条光柱一起极慢地偏，读作"光在动"；
            //         再叠一个 ±3% 的缩放呼吸 ⇒ 光柱像被大气折射着轻微伸缩 ✓
            //   呼吸：α 在 `BeamAlpha × (1 ± 22%)` 之间来回（实际写入在下面"乘 `sunA`"那一段）
            // ══════════════════════════════════════════════════════════════
            if (_beam != null)
            {
                float sway = BeamSwayDeg * Mathf.Sin(_lifeT * BeamSwaySpeed);
                _beam.transform.localRotation = Quaternion.Euler(0f, 0f, sway);
                float bs = 1f + 0.03f * Mathf.Sin(_lifeT * BeamBreathSpeed + 1.1f);
                float bd = BeamWorldHeight * bs;
                _beam.transform.localScale = new Vector3(bd, bd, 1f);
            }

            // ══════════════════════════════════════════════════════════════
            // ★ v308：① 柔光幕呼吸  ② 每 4 秒一次的光线脉冲
            // ══════════════════════════════════════════════════════════════
            // 呼吸：α 在 0.12 × (1 ± 0.20) 之间来回 ⇒ "有生命的光"，不是一张贴纸。
            // （α 的实际写入在下面"乘 `sunA`"那一段，这里只算呼吸系数）
            float breath = 1f + GlowBreathAmp * Mathf.Sin(_lifeT * GlowBreathSpeed);

            // 脉冲：`PulsePeriod` 秒一次，单次 `PulseSeconds`；直径 `PulseFrom→PulseTo`、α 0.2 → 0
            float pt = Mathf.Repeat(_lifeT, PulsePeriod);
            if (pt < PulseSeconds)
            {
                float ph = Mathf.Clamp01(pt / PulseSeconds);
                float d = DiscWorldHeight * Mathf.Lerp(PulseFrom, PulseTo, ph);
                _pulse.transform.localScale = new Vector3(d, d, 1f);
                // α 的写入统一在下面"乘 `sunA`"那一段（(1 − ph)² 的包络也在那里算）
                if (!_pulse.activeSelf) _pulse.SetActive(true);
            }
            else if (_pulse.activeSelf) _pulse.SetActive(false);

            // ══════════════════════════════════════════════════════════════
            // ★ v310：③ "挡住玩家 ⇒ 整颗太阳再降一档"（用户原话「确保玩家可见」）
            //   改前这里是把整颗太阳**藏起来**（"落进去就不画"）—— 太粗暴：太阳会整片消失，
            //   玩家读到的是"太阳没了"，而不是"太阳退到后面去了" ✗
            //   现在：命中 ⇒ `_vis` 走 `OccludedVisibility`(0.50)，
            //   过渡按"整段匀速"走（见下一段），不再用旧的 0.5 / 0.20s 组合 ✓
            // ★ v313：**这一档的语义变了** —— 太阳已经**默认 50%**（`SunAlphaScale`），
            //   所以 `_vis` 现在是一个**相对倍数**：命中 ⇒ 在 50% 的基础上再砍一半 ⇒ 净 **25%**。
            //   机制保留（不是删掉）的理由：本轮之后"玩家可见"的兜底仍然是它 ——
            //   一旦换相机 / 换构图（或调试把太阳拉下来），太阳压到玩家身上时唯一会自己退让的就是这条；
            //   删掉它就等于把"太阳可以糊住玩家"这个风险重新放回画面里，而它一年也只在调试路径上跑一次，
            //   留着零成本 ✓（`DebugSetUpOffset` 就是专门用来把这条路径真的走一遍的入口）
            // ══════════════════════════════════════════════════════════════
            if (_player == null) AcquirePlayer(dt);
            _occluded = _player != null && PlayerUnderSun(cam);
            if (_occluded) GuardHides++;                 // 读数：命中过多少次（不再是"隐藏次数"）
            LastReason = _occluded ? "太阳圆盘与玩家的屏幕矩形相交 ⇒ 整体再降一半（净 25%）"
                                   : "大晴天 ⇒ 默认 50%";

            // ★ v310：过渡 0.20s → **0.15s**，换成"整段匀速"（v310 当时系数取 0.40；
            //   ★v313 起它变成"在 50% 之上再乘的倍数"，取 0.50 ⇒ 净 25%）：
            //   `rate = (1 − OccludedVisibility) / VisibilityBlendSeconds` = 0.50/0.15 ≈ **3.33 /秒**
            //   ⇒ 1.00 → 0.50 恰好 **0.15 秒**，0.50 → 1.00 也恰好 0.15 秒（双向同速）。
            //   旧写法是 `dt/0.20 = 5.0/秒`（那是"1→0 用 0.2s"，1→0.5 其实只用 0.1s）⇒ 与"0.15s 过渡"不符 ✗
            float target = _occluded ? OccludedVisibility : 1f;
            float rate = (1f - OccludedVisibility) / Mathf.Max(0.01f, VisibilityBlendSeconds);
            _vis = Mathf.MoveTowards(_vis, target, rate * dt);

            // ══════════════════════════════════════════════════════════════
            // ★★ v313：**六层的 α 统一乘 `SunAlphaScale`（0.50）** ★★
            //   `sunA = SunAlphaScale × _vis`：
            //     · 正常（`_vis` = 1.00）  ⇒ sunA = **0.50**  ← 用户要的"默认透明度 50%"
            //     · 被玩家挡住（`_vis` → 0.50）⇒ sunA = 0.50 × 0.50 = **0.25**（在 50% 基础上再降一半）
            //   ⇒ 每一层仍然是"**自己的基准 α** × 同一个 `sunA`"，互相不吞没；
            //     以后要整体调透明度，**只改 `SunAlphaScale` 这一个数**就够（六层一起动）✓
            //   基准 α（`DiscColor.a` 等）**一个字节都没改** ⇒ 层与层的亮度比例与改前完全一致
            // ══════════════════════════════════════════════════════════════
            float sunA = SunAlphaScale * _vis;
            SetAlpha(_discMat, DiscColor.a * sunA);
            SetAlpha(_haloMat, GoldColor.a * sunA);
            SetAlpha(_raysMat, RaysColor.a * sunA);
            SetAlpha(_glowMat, GlowColor.a * Mathf.Clamp(breath, 0.4f, 1.4f) * sunA);
            float pa = _pulse != null && _pulse.activeSelf
                     ? PulseAlpha * Mathf.Pow(1f - Mathf.Clamp01(Mathf.Repeat(_lifeT, PulsePeriod) / PulseSeconds), 2f) * sunA
                     : 0f;
            SetAlpha(_pulseMat, pa);
            // ★ v310：**散射光束** —— 峰值 `BeamAlpha`(0.20) × 呼吸(1 ± 22%) × `sunA`
            float beamBreath = 1f + BeamBreathAmp * Mathf.Sin(_lifeT * BeamBreathSpeed);
            SetAlpha(_beamMat, BeamAlpha * Mathf.Clamp(beamBreath, 0.4f, 1.4f) * sunA);

            SetVisible(true);
        }

        /// <summary>把一份材质的 `_Color` 改成指定 α（RGB 保持原值）。找不到属性就静默跳过。</summary>
        private static void SetAlpha(Material m, float a)
        {
            if (m == null) return;
            Color c = m.color;
            c.a = Mathf.Clamp01(a);
            m.color = c;
        }

        /// <summary>显示/隐藏只动 `SunRoot` 这一层 —— 组件本身必须活着才能再显示回来。</summary>
        private void SetVisible(bool on)
        {
            if (_root == null) return;
            if (_visible == on) return;
            _visible = on;
            _root.SetActive(on);
        }

        /// <summary>
        /// ★ 只读的"现在该不该画"★（三个条件的证据见类注释第六节）。
        /// `reason` 只在判定为 false 时有意义（探针读数用）。
        /// </summary>
        private static bool ShouldShow(out string reason)
        {
            if (ProgMechanics.Weather != WeatherKind.Sunny)
            {
                reason = "天气=" + ProgMechanics.Weather;
                return false;
            }
            if (ProgMechanics.WeatherIntensity01 <= 0.01f)
            {
                reason = "晴天强度≈0（淡入未开始 / 淡出已结束）";
                return false;
            }
            // 主城/登录：`Town` 场景 additive 叠在 `Login` 上 ⇒ isLoaded = 我在主城
            if (SceneManager.GetSceneByName(LoginBootstrap.TownSceneName).isLoaded)
            {
                reason = "主城（Town 场景已加载，无战斗天气）";
                return false;
            }
            reason = "大晴天 ⇒ 显示";
            return true;
        }

        /// <summary>找玩家根（名字表与 `PlayerFacingMarker.PlayerNames` 同源），1 秒重试一次。</summary>
        private void AcquirePlayer(float dt)
        {
            _playerRetry -= dt;
            if (_playerRetry > 0f) return;
            _playerRetry = PlayerRetrySeconds;
            for (int i = 0; i < PlayerNames.Length; i++)
            {
                GameObject go = GameObject.Find(PlayerNames[i]);
                if (go == null) continue;
                _player = go.transform;
                return;
            }
        }

        /// <summary>
        /// ★ v310：**太阳圆盘与玩家的屏幕矩形是否相交**（用户：「确保玩家可见」）。
        ///
        /// ── 改前（v308）为什么被判"几乎不会触发" ──
        ///   旧判据是"玩家的**头那一个点**是否落进**光晕矩形**（14 单位）"。三个毛病：
        ///     ① 只取一个点 ⇒ 只要那一点在矩形外就判"没挡住"，而玩家的身体可能正好在太阳上 ✗
        ///     ② 用**光晕**（14 单位 = 屏幕高 19.4%）当矩形 ⇒ 前置条件太苛刻
        ///     ③ 余量只有 0.01 视口（≈7px at 720p）⇒ 边缘蹭到不算
        ///
        /// ── 现在四条 ──
        ///   ① 太阳那一侧：**圆盘**（`DiscWorldHeight` 5）投到视口，量出半宽 / 半高
        ///      （用 `WorldToViewportPoint(c ± right/up × half)` 反推 ⇒ 不假设 fov / 宽高比）
        ///   ② 玩家那一侧：**四个点**围成矩形 —— 脚底、头顶（`PlayerTopHeight`）、
        ///      半身处左右各 `PlayerHalfWidth` ⇒ 覆盖"身体高度范围"而不只是一个点 ✓
        ///   ③ 两个矩形**各扩 `GuardMarginPx`(20) 像素**（换算成视口单位：÷ `pixelWidth/Height`）
        ///   ④ 矩形相交 ⇒ 命中（`AABB` 四条判据）
        ///
        /// ── ⚠️ 数学上它在本作**稳态下仍然不会触发**（这一点必须说清楚，不能假装修好了）──
        ///   本作相机是**跟随**的（`followLerp = 10`）：玩家恒在镜头焦点 ⇒ 屏幕 y ≈ **0.50**，
        ///   而太阳圆盘矩形恒在 y ∈ **[0.795, 0.865]**（圆心 0.83、半径 3.5%）⇒
        ///   竖向差 0.20 视口 ≈ 每帧 145px（720p），20px 的余量补不上 ✗
        ///   要玩家自己爬到 0.795，镜头必须**落后焦点约 5 米**（= 玩家以 ≈51 m/s 跑），实战不可能。
        ///   ⇒ 这条判据是**结构性保险**（"万一有别的系统把玩家推上去/把镜头冻住"），
        ///     不是"平时就能看到"的效果。**要验证它真的工作**，用下面的构造场景（`DebugSetUpOffset`）：
        ///       `unity_drive send COMBAT_SUN_DISC CombatSunDisc DebugSetUpOffset 4`
        ///     `UpOffset 24 → 4` ⇒ 太阳的仰角从 21.8° 降到 3.8° ⇒ 圆心视口 y ≈ **0.56**、
        ///     圆盘矩形 y ∈ [0.527, 0.596]，与玩家矩形（≈ [0.42, 0.58]）**相交** ✓
        ///     此时 `GuardHides` 会涨、`_vis` → 0.50（★v313：太阳已默认 50% ⇒ 净 α = 基准 × 0.25）。
        ///     还原：`DebugSetUpOffset 0` ✓
        /// </summary>
        private bool PlayerUnderSun(Camera cam)
        {
            Vector3 c = transform.position;
            Vector3 vc = cam.WorldToViewportPoint(c);
            if (vc.z <= 0f) return false;

            // ① 太阳**圆盘**的屏幕矩形（不是光晕：挡人的是那个圆）
            float half = DiscWorldHeight * 0.5f;
            Vector3 vx = cam.WorldToViewportPoint(c + cam.transform.right * half);
            Vector3 vy = cam.WorldToViewportPoint(c + cam.transform.up * half);
            float mw = GuardMarginPx / Mathf.Max(1f, cam.pixelWidth);
            float mh = GuardMarginPx / Mathf.Max(1f, cam.pixelHeight);
            float sw = Mathf.Abs(vx.x - vc.x) + mw;
            float sh = Mathf.Abs(vy.y - vc.y) + mh;

            // ② 玩家的屏幕矩形：脚底 / 头顶 / 半身左右两侧（含身体高度范围）
            Vector3 vFoot = cam.WorldToViewportPoint(_player.position);
            Vector3 vHead = cam.WorldToViewportPoint(_player.position + Vector3.up * PlayerTopHeight);
            Vector3 vMid = _player.position + Vector3.up * (PlayerTopHeight * 0.5f);
            Vector3 vLeft = cam.WorldToViewportPoint(vMid - cam.transform.right * PlayerHalfWidth);
            Vector3 vRight = cam.WorldToViewportPoint(vMid + cam.transform.right * PlayerHalfWidth);
            if (vFoot.z <= 0f || vHead.z <= 0f || vLeft.z <= 0f || vRight.z <= 0f) return false;

            float px0 = Mathf.Min(vLeft.x, vRight.x) - mw;
            float px1 = Mathf.Max(vLeft.x, vRight.x) + mw;
            float py0 = Mathf.Min(vFoot.y, vHead.y) - mh;
            float py1 = Mathf.Max(vFoot.y, vHead.y) + mh;

            LastSunRect = new Vector4(vc.x - sw, vc.y - sh, vc.x + sw, vc.y + sh);
            LastPlayerRect = new Vector4(px0, py0, px1, py1);

            // ③ 矩形相交（AABB 四条）—— 两个矩形都已各扩 `GuardMarginPx` 像素 ✓
            return LastSunRect.x <= px1 && px0 <= LastSunRect.z
                && LastSunRect.y <= py1 && py0 <= LastSunRect.w;
        }

        // ══════════════════════════════════════════════════════════════════
        // 搭建（建一次）
        // ══════════════════════════════════════════════════════════════════
        private void Build()
        {
            _root = new GameObject("SunRoot");
            _root.transform.SetParent(transform, false);

            // ★ v310：**散射光束**（最先建、队列 2999 ⇒ 压在柔光幕之上、光晕之下）
            //   尺寸 26 世界单位 > 光晕 14 ⇒ 8 条光柱伸出晕外，读作"从太阳射出来的光" ✓
            _beam = AddPiece("SunBeam", _root.transform, BeamWorldHeight, BeamMaterial());
            _halo = AddPiece("SunHalo", _root.transform, HaloWorldHeight, HaloMaterial());
            _rays = AddPiece("SunRays", _root.transform, RaysWorldHeight, RaysMaterial());
            _disc = AddPiece("SunDisc", _root.transform, DiscWorldHeight, DiscMaterial());
            // ★ v308：柔光幕（贴在最外层垫底）与光线脉冲（再退一层）
            _glow = AddPiece("SunGlow", _root.transform, GlowWorldHeight, GlowMaterial());
            _pulse = AddPiece("SunPulse", _root.transform, DiscWorldHeight * PulseFrom, PulseMaterial());
            _pulse.SetActive(false);   // 平时关着：只有脉冲那 0.85s 才亮

            _root.SetActive(false);     // 先关着，第一次判定为晴天时再亮（避免开局闪一下）
            _visible = false;
            Builds++;
        }

        /// <summary>
        /// 一片"贴在相机近平面上"的方片：1×1 网格 + `localScale = 世界尺寸`。
        /// **不投影、不吃阴影、无 Collider、不写深度**（队列在材质上给）。
        /// </summary>
        private static GameObject AddPiece(string name, Transform parent, float worldHeight, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(worldHeight, worldHeight, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// 1×1 的方片（**双面**：正面 2 三角 + 背面 2 三角，共 4 顶点 / 12 索引）。
        /// 双面是为了兜底着色器换了剔除模式也不会突然看不见（`Sprites/Default` 本身 `Cull Off`）✓
        /// </summary>
        private static Mesh QuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;

            var v = new Vector3[4];
            v[0] = new Vector3(-0.5f, -0.5f, 0f);
            v[1] = new Vector3(-0.5f, 0.5f, 0f);
            v[2] = new Vector3(0.5f, 0.5f, 0f);
            v[3] = new Vector3(0.5f, -0.5f, 0f);

            var uv = new Vector2[4];
            uv[0] = new Vector2(0f, 0f);
            uv[1] = new Vector2(0f, 1f);
            uv[2] = new Vector2(1f, 1f);
            uv[3] = new Vector2(1f, 0f);

            var nrm = new Vector3[4];
            var col = new Color[4];
            for (int i = 0; i < 4; i++)
            {
                nrm[i] = new Vector3(0f, 0f, -1f);   // 面向相机的那一侧（相机在 −Z 方向看过来）
                col[i] = Color.white;
            }

            var tris = new int[12]
            {
                0, 1, 2,   0, 2, 3,        // 正面
                0, 2, 1,   0, 3, 2         // 背面
            };

            var m = new Mesh { name = "CombatSunQuad", hideFlags = HideFlags.DontSave };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetNormals(nrm);
            m.SetColors(col);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            _quadMesh = m;
            return m;
        }

        // ══════════════════════════════════════════════════════════════════
        // 材质（三份，各带自己的贴图与队列）
        // ══════════════════════════════════════════════════════════════════
        private static Material HaloMaterial()
        {
            if (_haloMat != null) return _haloMat;
            _haloMat = BuildMat("M_CombatSunHalo", HaloTexture(), GoldColor, HaloQueue);
            return _haloMat;
        }

        private static Material RaysMaterial()
        {
            if (_raysMat != null) return _raysMat;
            _raysMat = BuildMat("M_CombatSunRays", RaysTexture(), RaysColor, RaysQueue);
            return _raysMat;
        }

        private static Material DiscMaterial()
        {
            if (_discMat != null) return _discMat;
            _discMat = BuildMat("M_CombatSunDisc", DiscTexture(), DiscColor, DiscQueue);
            return _discMat;
        }

        /// <summary>★ v308：柔光幕材质 —— **复用光晕贴图**（整片径向渐变），只是尺寸更大、α 更低。
        /// 不额外生成第三张贴图：光晕那张 `T_CombatSunHalo` 的形状正是"软幕"需要的 ✓</summary>
        private static Material GlowMaterial()
        {
            if (_glowMat != null) return _glowMat;
            _glowMat = BuildMat("M_CombatSunGlow", HaloTexture(), GlowColor, GlowQueue);
            return _glowMat;
        }

        /// <summary>★ v308：光线脉冲材质 —— 复用**星芒贴图**（四道放射亮条），
        /// 缩放变大 + α 衰减 ⇒ 读作"光向外扩散了一波" ✓</summary>
        private static Material PulseMaterial()
        {
            if (_pulseMat != null) return _pulseMat;
            _pulseMat = BuildMat("M_CombatSunPulse", RaysTexture(), PulseColor, PulseQueue);
            return _pulseMat;
        }

        /// <summary>★ v310：**散射光束**材质 —— 自己的贴图（`BeamTexture`）、暖白偏金、峰值 α 0.20。
        /// 颜色 `(1.00, 0.90, 0.72)` 与光晕 `(1,0.86,0.55)` 同色系但更白一档 ⇒
        /// 光束读作"被大气散开的白光"，而不是"第二层金光"✓</summary>
        private static Material BeamMaterial()
        {
            if (_beamMat != null) return _beamMat;
            _beamMat = BuildMat("M_CombatSunBeam", BeamTexture(),
                                new Color(1.00f, 0.90f, 0.72f, BeamAlpha), BeamQueue);
            return _beamMat;
        }

        /// <summary>
        /// 着色器兜底链：`Sprites/Default`（本工程已验证可用，`PlayerFacingMarker.cs:603`）
        /// → `Universal Render Pipeline/Unlit` → `Unlit/Transparent`。
        /// 颜色/α 全在材质 `_Color` 上，贴图只提供形状（纯白 + α）✓
        /// </summary>
        private static Material BuildMat(string name, Texture2D tex, Color color, int queue)
        {
            Shader sh = Shader.Find("Sprites/Default");
            bool sprite = sh != null;
            if (!sprite)
            {
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Transparent");
                if (sh == null) sh = Shader.Find("Standard");
            }

            var m = new Material(sh) { name = sprite ? name : name + "FALLBACK" };
            m.mainTexture = tex;
            m.color = color;
            // `Sprites/Default` 的顶点阶段还有一项 `_RendererColor`（由 `SpriteRenderer` 用 MPB 送进来）。
            // 本文件用 `MeshRenderer` 画它 ⇒ 这一项没人写。显式给白：一旦它是黑，整片会被乘成黑
            // （与 `PlayerFacingMarker.cs:614-618` 的同一条防御）✓
            m.SetColor("_RendererColor", Color.white);
            m.renderQueue = queue;

            if (!sprite)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);        // URP：Transparent
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);            // URP：Alpha
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            }

            LastShader = m.shader != null ? m.shader.name : "(null)";
            return m;
        }

        // ══════════════════════════════════════════════════════════════════
        // 贴图（代码生成 128×128，纯白 + α 形状；整局只生成一次）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>圆盘：中心实心 → `DiscCoreR` 之后软化到边缘全透明（"亮白 → 透明"）。</summary>
        private static Texture2D DiscTexture()
        {
            if (_discTex != null) return _discTex;
            _discTex = MakeRadial("T_CombatSunDisc", (r) =>
            {
                // r ≤ DiscCoreR ⇒ 1（实心）；DiscCoreR → 1 之间 smoothstep 收到 0（软边）
                // ⚠️ `Mathf.SmoothStep(from,to,t)` 是**把值从 from 插到 to**，不是"从 from 之后开始软"
                //    ⇒ 必须先把 r 归一化成 t 再用，否则中心会被算成 0.28（曾经就是这么写错的）
                float t = Mathf.Clamp01((r - DiscCoreR) / Mathf.Max(1e-4f, 1f - DiscCoreR));
                return 1f - Mathf.SmoothStep(0f, 1f, t);
            });
            return _discTex;
        }

        /// <summary>光晕：整片的径向渐变（中心 1 → 边缘 0），衰减比圆盘更"软"。</summary>
        private static Texture2D HaloTexture()
        {
            if (_haloTex != null) return _haloTex;
            _haloTex = MakeRadial("T_CombatSunHalo", (r) =>
            {
                float a = Mathf.Clamp01(1f - r);
                // 1.5 次方衰减：比"平方"在半径中段留得住光 ⇒ 光晕在草地上读得出来
                // （平方衰减实测太淡：`(0.5)^2 × α0.35 = 0.09`，几乎看不见）
                return Mathf.Pow(a, 1.5f);
            });
            return _haloTex;
        }

        /// <summary>
        /// 星芒：**四道放射亮条**（0° / 90° / 180° / 270°）。
        /// `|cos(2θ)|^RaysLobePower` 在四个轴向上取 1、在对角线取 0 ⇒ 恰好四个花瓣；
        /// 再乘径向衰减 ⇒ 中心最亮、向外收尖 ✓
        /// </summary>
        private static Texture2D RaysTexture()
        {
            if (_raysTex != null) return _raysTex;
            _raysTex = MakeRadial("T_CombatSunRays", (r, angDeg) =>
            {
                float ang = angDeg * Mathf.Deg2Rad;
                float lobe = Mathf.Pow(Mathf.Abs(Mathf.Cos(2f * ang)), RaysLobePower);
                float radial = Mathf.Clamp01(1f - r);
                return lobe * radial * radial;
            }, withAngle: true);
            return _raysTex;
        }

        /// <summary>只按半径取 α 的生成器（圆盘 / 光晕）。</summary>
        private static Texture2D MakeRadial(string name, System.Func<float, float> alphaOfR)
        {
            return MakeRadial(name, (r, _) => alphaOfR(r), false);
        }

        /// <summary>
        /// ★ v310：**散射光束扇**（god rays）—— 8 条从中心射出的**锥形**光束，烧进一张 128×128 贴图。
        ///
        /// ── 为什么是"一张贴图"而不是"8 个拉长的菱形网格" ──
        ///   拉长的菱形要么再建一份程序化 Mesh（还要给 8 个物体各算一次朝向/缩放），
        ///   要么把 8 次变换每帧算一遍；而这张贴图**一次烘好**，运行期只用 1 个渲染体、
        ///   1 次 UV 采样，光的软边、锥度、长短差别全在 α 通道里 ✓
        ///   （用户给的第二个方案"叠加多个拉长的菱形"就是这个形状的显式版本 ——
        ///     贴图是同一个几何的**采样形式**，且省掉 8 个物体与 8 次矩阵）
        ///
        /// ── 每一条长什么样 ──
        ///   · 中心角 `22.5° + k×45°`（k=0..7）⇒ 8 条**与星芒的 4 道错开**45°/2，
        ///     免得两条亮线叠在同一条线上变成一个更亮的花瓣
        ///   · 半张角 `w(r) = 3.0° + 6.0°·r` ⇒ **中心窄、外缘宽** = 锥形（用户：「宽度从中心向外变宽」）
        ///   · 切向强度 `(1 − |Δθ|/w)²`  ⇒ 中轴最亮、两侧平方收边 ⇒ 软边光柱，不是硬边三角
        ///   · 每条亮度乘一个**固定**的 0.72~1.00 系数（`((k·37) mod 7)/6`）⇒ 八条不齐高，
        ///     不呆板；纯整数运算 ⇒ 每次生成完全一样（可复现）✓
        ///   · 径向包络：`r ≤ BeamInnerR(0.14)` 这一段**不衰减**（它整个被圆盘/柔光幕压住，看不出来），
        ///     之后 `0.14 → 1.0` 用 `(1−t)^1.15` 一路收到 **0** ⇒ α 从 `BeamAlpha`(0.20) 降到 0 ✓
        /// </summary>
        private static Texture2D BeamTexture()
        {
            if (_beamTex != null) return _beamTex;
            _beamTex = MakeRadial("T_CombatSunBeam", (r, angDeg) =>
            {
                // 径向包络：内圈留空 → 外缘收 0
                float t = Mathf.Clamp01((r - BeamInnerR) / Mathf.Max(1e-4f, 1f - BeamInnerR));
                float radial = Mathf.Pow(1f - t, 1.15f);
                if (radial <= 0.001f) return 0f;

                // 这一半径处的半张角（锥形：越往外越宽）
                float w = BeamHalfAngle0 + BeamHalfAngleGrow * Mathf.Clamp01(r / 0.85f);
                float best = 0f;
                float step = 360f / BeamCount;
                for (int k = 0; k < BeamCount; k++)
                {
                    float c = 22.5f + k * step;
                    float d = Mathf.Abs(Mathf.DeltaAngle(angDeg, c));
                    if (d >= w) continue;
                    float a = 1f - d / w;
                    a *= a;                                     // 平方收边 ⇒ 软光柱
                    // 每条一个固定强度（0.85…1.00）：`0.85 + 0.15·((k·37 mod 7)/6)`
                    //   ⇒ 最亮那一条的峰值 α 正好是 `BeamAlpha`(0.20)，其余 0.17~0.19
                    //   （用 0.72…1.00 的话峰值只有 0.144，达不到用户给的 0.20 ✗）
                    a *= 0.85f + 0.15f * (((k * 37) % 7) / 6f);
                    if (a > best) best = a;
                }
                return best * radial;
            }, withAngle: true);
            return _beamTex;
        }

        /// <summary>
        /// 128×128 的白色 + α 贴图。`uv` 归一化到 `[-1,1]`（中心 = (0,0)，半径 1 触边）。
        /// `HideFlags.DontSave` ⇒ 不进资源库、不随场景保存；静态缓存 ⇒ 整局只生成一次 ✓
        /// </summary>
        private static Texture2D MakeRadial(string name, System.Func<float, float, float> alphaOfR,
                                            bool withAngle)
        {
            int n = TextureSize;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = ((x + 0.5f) / n) * 2f - 1f;
                    float v = ((y + 0.5f) / n) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float angDeg = withAngle ? Mathf.Atan2(v, u) * Mathf.Rad2Deg : 0f;
                    float a = r >= 1f ? 0f : Mathf.Clamp01(alphaOfR(r, angDeg));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ══════════════════════════════════════════════════════════════════
        // 收尾
        // ══════════════════════════════════════════════════════════════════
        /// <summary>宿主销毁时把静态缓存一并释放（`HideFlags.DontSave` 的资源不会自己回收）。</summary>
        private static void DestroyShared()
        {
            if (Application.isPlaying)
            {
                if (_quadMesh != null) Destroy(_quadMesh);
                if (_discTex != null) Destroy(_discTex);
                if (_haloTex != null) Destroy(_haloTex);
                if (_raysTex != null) Destroy(_raysTex);
                if (_beamTex != null) Destroy(_beamTex);
                if (_discMat != null) Destroy(_discMat);
                if (_haloMat != null) Destroy(_haloMat);
                if (_raysMat != null) Destroy(_raysMat);
                if (_glowMat != null) Destroy(_glowMat);
                if (_pulseMat != null) Destroy(_pulseMat);
                if (_beamMat != null) Destroy(_beamMat);
            }
            _quadMesh = null;
            _discTex = null;
            _haloTex = null;
            _raysTex = null;
            _beamTex = null;
            _discMat = null;
            _haloMat = null;
            _raysMat = null;
            _glowMat = null;
            _pulseMat = null;
            _beamMat = null;
        }

        /// <summary>
        /// ★ v310：**验证入口** —— 把太阳的上抬量临时换成一个更小的值，让"太阳挡住玩家"这条判据
        /// 真的被走到一次（数学上它在本作稳态下不可能触发，见 `PlayerUnderSun` 的推导）。
        /// 用法：`unity_drive send COMBAT_SUN_DISC CombatSunDisc DebugSetUpOffset 4`
        ///       → 太阳圆心视口 y ≈ 0.56，与玩家矩形（≈[0.42,0.58]）相交 ⇒ `GuardHides` 涨、`_vis`→0.50；
        ///       （★v313 起太阳默认已乘 `SunAlphaScale`(0.50) ⇒ 命中时**净 α = 基准 × 0.25**，
        ///        `Status()` 的"整体α"一格会从 0.50 掉到 0.25 —— 这就是这条路径被走到的证据）
        ///       读 `CombatSunDisc.Status()` 能看到两个屏幕矩形与实际可见度；
        ///       传 `0` 还原（**不影响任何游戏逻辑**，只改太阳自己的位置）✓
        /// 参数收 `string`（桥把 `value` 按字符串递进来），解析失败就当成"不变"。
        /// </summary>
        public void DebugSetUpOffset(string v)
        {
            float f;
            if (!float.TryParse(v, out f)) return;
            DebugUpOffset = Mathf.Clamp(f, 0f, 40f);
            LastReason = DebugUpOffset > 0f
                       ? "调试：上抬量覆盖为 " + DebugUpOffset.ToString("0.0")
                       : "调试：上抬量已还原（UpOffset 24）";
        }

        /// <summary>一行读数（探针 / 截图叠字用）。</summary>
        public static string Status()
        {
            float gapY = LastPlayerRect.w - LastSunRect.y;   // >0 = 玩家矩形整个在太阳矩形下方（没挡住）
            return $"可见={(_instance != null && _instance._visible)}｜原因={LastReason}"
                 + $"｜pos=({LastPos.x:0.00},{LastPos.y:0.00},{LastPos.z:0.00})"
                 + $"｜尺寸 圆盘{DiscWorldHeight:0.0}/柔光幕{GlowWorldHeight:0.0}/光束{BeamWorldHeight:0.0}/晕{HaloWorldHeight:0.0}/芒{RaysWorldHeight:0.0}"
                 + $"｜α 基准=({DiscColor.a:0.00}/{GlowColor.a:0.00}/{GoldColor.a:0.00}/{RaysColor.a:0.00}/光束{BeamAlpha:0.00})"
                 + $"｜整体α×{SunAlphaScale:0.00} 遮挡再×{OccludedVisibility:0.00}"
                 + $"｜有效α=({DiscColor.a * SunAlphaScale * (_instance != null ? _instance._vis : 1f):0.000}"
                 + $"/{GoldColor.a * SunAlphaScale * (_instance != null ? _instance._vis : 1f):0.000}"
                 + $"/{RaysColor.a * SunAlphaScale * (_instance != null ? _instance._vis : 1f):0.000})"
                 + $"｜可见度={(_instance != null ? _instance._vis : 1f):0.00}"
                 + $"（{VisibilityBlendSeconds:0.00}s 过渡；脉冲 {PulsePeriod:0.0}s/α{PulseAlpha:0.00}）"
                 + $"｜光束 {BeamCount} 条(张角 {BeamHalfAngle0:0.0}°→+{BeamHalfAngleGrow:0.0}°·摆±{BeamSwayDeg:0.0}°·呼吸±{BeamBreathAmp * 100f:0}%)"
                 + $"｜屏幕矩形 太阳=[{LastSunRect.x:0.000},{LastSunRect.y:0.000}]-[{LastSunRect.z:0.000},{LastSunRect.w:0.000}]"
                 + $" 玩家=[{LastPlayerRect.x:0.000},{LastPlayerRect.y:0.000}]-[{LastPlayerRect.z:0.000},{LastPlayerRect.w:0.000}]"
                 + $" 竖向间隙={gapY:0.000}(>0 即没挡住)"
                 + (DebugUpOffset > 0f ? $" ⚠调试上抬量覆盖={DebugUpOffset:0.0}" : "")
                 + $"｜队列=(柔光{GlowQueue}/脉冲{PulseQueue}/光束{BeamQueue}/{HaloQueue}/{RaysQueue}/{DiscQueue})"
                 + $"｜shader={LastShader}"
                 + $"｜实例={Instances} 搭建={Builds} 轮询={WeatherPolls}(晴 {SunnyPolls}) 守卫降档={GuardHides} 层数=6";
        }
    }
}
