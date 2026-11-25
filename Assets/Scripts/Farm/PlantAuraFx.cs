using UnityEngine;
using UnityEngine.Rendering;

namespace Seedfall
{
    /// <summary>
    /// ★★ 机制植物的**触发特效** —— 一株植物"真的起作用了"的那一下，屏幕上必须有东西动 ★★
    ///
    /// 用户原话（本轮）：「为所有提供特殊效果的植物加上触发效果时的特效，
    /// 比如站上面获得加成，就给角色提供特效」
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、这个文件只做一件事：把**已经发生的机制**画出来
    /// ══════════════════════════════════════════════════════════════════════
    /// 判据一律**读现有的解释器**，本文件**一个机制数值都不重写**：
    ///   · 机制名        `PlantMechanics.Has(def, PlantMechanics.StandingDodge)` 等（`Farm/PlantMechanics.cs`）
    ///   · 触发阈值      `PlantMechanics.ThirstGoldRatio`（水分 50%）、`PlantMechanics.ElderStepSeconds`（60 秒）
    ///   · 触发条件      `FarmPlot.Water01` / `FarmPlot.GrowthPaused`（地块的公开读数）
    ///   · 天气触发      `ProgMechanics.WeatherCount` / `WeatherSourceId`（`Prog/ProgMechanics.cs`）
    ///   · 判定余量      `ProgMechanics.PlotStandMargin`（0.15m，与站田闪避**同一个常量**）
    /// ⇒ 机制改平衡 ⇒ 特效跟着改，**不需要同步改这个文件** ✓
    /// ⚠️ 唯一被复制的是那句矩形判定 `|dx|≤half && |dy|≤half` —— 因为它在
    ///    `ProgMechanics.InsidePlot` 里是 **private**。复制的是**形状**，不是常量
    ///    （半边长仍由 `PlotStandMargin` 算出来）⇒ 调余量时两边一起动 ✓
    ///    最小改动方案（本轮**没有**采用，因为本轮规定不碰现有文件）：
    ///    把 `ProgMechanics.InsidePlot` 由 `private static` 改成 `public static`（一个关键字），
    ///    然后删掉本文件的 <see cref="InsidePlot"/>，改成调用它 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、两类反馈（14 株机制植物全部覆盖）
    /// ══════════════════════════════════════════════════════════════════════
    /// **A 类（持续型，画在玩家脚下）** —— 站在它的地块上、条件成立时**一直亮着**：
    ///   · `StandingDodge` 庇护藤（无条件：站上去就有）
    ///   · `ThirstBloom`   空藤  （`plot.Water01 &lt; 0.5` 才亮，与金币加成同一条判据）
    ///   · `ThirstTolerant` 沙掌 （`plot.GrowthPaused` 才亮 —— 那正是"缺水仍生长"生效的时刻）
    ///   ⇒ 一圈呼吸的贴地光环 + 3 个环绕光点（见 `Halo*` 常量）。
    ///   ⇒ **离开地块立刻隐藏**：每帧重算，没有淡出延迟（"看不见 = 没生效"必须逐帧一致）。
    ///
    /// **B 类（瞬时型，画在触发点）** —— 一次性爆一下（6~10 个上飘粒子 + 一圈 0.2→1.2 米 / 0.35 秒的扩散环），
    /// 适用的 11 条机制与各自的触发源见 <see cref="Poll"/> 的六条判据。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、为什么 B 类用**轮询**而不是事件（这是本文件唯一的妥协，写清理由）
    /// ══════════════════════════════════════════════════════════════════════
    /// `grep Bus.Publish` 全工程 40 个事件类型，**收割 / 浇水 / 地块状态变化一个都没有**
    /// （最接近的是 `TreeMatureEvent`，但它只在"树成熟"时发，且不带品种信息）；
    /// 天气用 `ProgMechanics.WeatherCount` 这个**公开计数**读得到，所以天气那条没有走轮询。
    /// ⇒ 剩下的触发只能从**地块状态**反推：每 `PollSeconds`（0.15s）读一遍 `Farm.Slots`，
    ///   拿"这一格与上一次相比变了什么"当触发信号（见 `Poll` 里的 ①②③④⑤⑥ 六条）。
    /// ⇒ **为什么不新增一个事件**：那要改 `World.HarvestPlot` / `FarmPlot` 等现有文件，
    ///   本轮硬性要求"不碰任何现有文件" ⇒ 加法事件这条路是关掉的 ✓
    /// ⇒ 轮询的代价：最坏晚 0.15 秒才爆（肉眼不可辨），每 0.15 秒遍历 ≤36 个地块（零分配）。
    ///   **不需要像素级同步**：这些都是"事后再看也成立"的表现，不是判定的一部分 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、性能：全部对象池，稳态零分配
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 池在 `Awake` 里**建一次**：4 光环 ×(1 环 + 3 点) + 64 粒子 + 8 扩散环 = 88 个 Renderer，
    ///     之后**运行期 0 次 `Instantiate` / 0 次 `Destroy`** ✓
    ///   · 网格 3 份、材质 1 份（全场共用，颜色走 `MaterialPropertyBlock`）⇒ 不产生材质实例 ✓
    ///   · 每帧分配：0（无闭包、无 LINQ、无字符串拼接、无 `foreach` 在接口上）
    ///     唯一会分配的地方是"地块数变化"（长出一排新地）时的快照数组重建，一局最多几次 ✓
    ///   · 每帧写入量：1 个光环 ⇒ 4 次 transform + 4 次 MPB；未激活的粒子/环**不写**（`SetActive(false)`）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、不加美术资产
    /// ══════════════════════════════════════════════════════════════════════
    /// 网格全部程序化（单位环 / 单位圆盘 / 单位方片），着色器走 `Shader.Find` 兜底链
    /// （`Sprites/Default` → `Universal Render Pipeline/Unlit` → `Standard`，
    ///  与 `Bootstrap/PlayerFacingMarker.cs` 的 `BuildMat` 同一套顺序），
    /// 颜色走 `MaterialPropertyBlock` ⇒ 运行期 0 个新 Material、0 张新贴图 ✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlantAuraFx : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════
        // A 类：持续型光环（画在玩家脚下）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>贴地高度（米）。地面/耕地上表面 y ≈ 0.02 ⇒ 留 1cm 不 z-fighting。</summary>
        public const float HaloY = 0.03f;
        /// <summary>光环基准半径（米）。需求 0.7~0.9 ⇒ 起点 0.70。</summary>
        public const float HaloBaseRadius = 0.70f;
        /// <summary>
        /// 同屏多株同时生效时的**半径错开量**（米）：0.70 / 0.76 / 0.82 / 0.88
        /// ⇒ 4 个同心环而不是 4 个重叠环（重叠会因深度相等而闪烁）。
        /// ★ 上界仍是 0.88 ≤ 需求的 0.9 ✓
        /// </summary>
        public const float HaloRadiusStep = 0.06f;
        /// <summary>呼吸幅度：半径 ±8%。</summary>
        public const float HaloBreathAmp = 0.08f;
        /// <summary>呼吸周期（秒）。</summary>
        public const float HaloPeriod = 1.2f;
        /// <summary>呼吸的 α 中点（±`HaloAlphaSwing` ⇒ 0.18~0.32）。</summary>
        public const float HaloAlphaMid = 0.25f;
        /// <summary>呼吸的 α 摆幅（0.25 ± 0.07 = 0.18~0.32，需求给定）。</summary>
        public const float HaloAlphaSwing = 0.07f;

        /// <summary>每个光环上环绕的光点数（需求 2~3 ⇒ 取 3）。</summary>
        public const int HaloDotCount = 3;
        /// <summary>光点环绕角速度（度/秒，需求 ~90°/s）。</summary>
        public const float HaloDotDegPerSec = 90f;
        /// <summary>光点直径尺度（米）—— 贴地小圆盘的半径。</summary>
        public const float HaloDotSize = 0.075f;
        /// <summary>光点的 α（比光环实，读作"在上面转的亮点"）。</summary>
        public const float HaloDotAlpha = 0.55f;
        /// <summary>光点所在的半径比例（环中线：0.93 = 内 0.86 与外 1.0 的中线）。</summary>
        public const float HaloDotRadiusFrac = 0.93f;
        /// <summary>光点比光环再抬的高度（米）—— 配 `sortingOrder` 保证"骑在环上"。</summary>
        public const float HaloDotLiftY = 0.004f;

        /// <summary>
        /// 光环池大小。每株最多 1 个光环 + 3 光点 ⇒ 池里放 4 株的量（同一块地重叠时最多 4 个同心环）。
        /// 实际同屏几乎总是 1 个（一个玩家只站在一两块地重叠带上）。
        /// </summary>
        public const int HaloPoolSize = 4;

        // ══════════════════════════════════════════════════════════════════
        // B 类：瞬时爆发（画在触发点）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>轮询间隔（秒）。见类注释第三节：全工程没有收割/浇水事件，只能反推。</summary>
        public const float PollSeconds = 0.15f;

        /// <summary>粒子池大小。</summary>
        public const int ParticlePoolSize = 64;
        /// <summary>扩散环池大小。</summary>
        public const int RingPoolSize = 8;
        /// <summary>一次爆发的粒子数下限。</summary>
        public const int BurstParticleMin = 6;
        /// <summary>一次爆发的粒子数上限。</summary>
        public const int BurstParticleMax = 10;
        /// <summary>粒子尺寸下限（米，方片边长）。</summary>
        public const float ParticleSizeMin = 0.06f;
        /// <summary>粒子尺寸上限（米）。</summary>
        public const float ParticleSizeMax = 0.12f;
        /// <summary>粒子寿命下限（秒）。</summary>
        public const float ParticleLifeMin = 0.5f;
        /// <summary>粒子寿命上限（秒）。</summary>
        public const float ParticleLifeMax = 0.8f;
        /// <summary>粒子上升速度下限（米/秒）。</summary>
        public const float ParticleRiseMin = 0.9f;
        /// <summary>粒子上升速度上限（米/秒）。</summary>
        public const float ParticleRiseMax = 1.5f;
        /// <summary>粒子横向散布（米/秒）。</summary>
        public const float ParticleSpreadMps = 0.35f;
        /// <summary>粒子出生高度下限（米，相对地面）。</summary>
        public const float ParticleSpawnYMin = 0.12f;
        /// <summary>粒子出生高度上限（米）。</summary>
        public const float ParticleSpawnYMax = 0.30f;
        /// <summary>粒子出生时的水平散布半径（米）。</summary>
        public const float ParticleSpawnRadius = 0.18f;
        /// <summary>粒子 α 的起点（之后线性淡到 0）。</summary>
        public const float ParticleAlpha = 0.95f;

        /// <summary>扩散环寿命（秒，需求 0.35s）。</summary>
        public const float RingLife = 0.35f;
        /// <summary>扩散环起始半径（米）。</summary>
        public const float RingRadiusStart = 0.2f;
        /// <summary>扩散环终止半径（米）。</summary>
        public const float RingRadiusEnd = 1.2f;
        /// <summary>扩散环 α 的起点（线性淡到 0）。</summary>
        public const float RingAlpha = 0.75f;
        /// <summary>扩散环贴地高度（米）。</summary>
        public const float RingY = 0.04f;

        /// <summary>一次轮询里最多放几次爆发（= 环池大小：再多彩也看不出来，只会互相盖住）。</summary>
        public const int MaxBurstsPerPoll = 8;
        /// <summary>同一格的爆发冷却（秒）—— 防止"同一次状态变化被两条判据各算一次"。</summary>
        public const float BurstCooldownSeconds = 0.25f;

        // ══════════════════════════════════════════════════════════════════
        // 几何 / 渲染
        // ══════════════════════════════════════════════════════════════════
        /// <summary>单位环的段数。</summary>
        public const int RingSegments = 48;
        /// <summary>单位环的内径比例（外径 = 1.0 ⇒ 线宽 = 14%，扩散时看得见一圈细环）。</summary>
        public const float RingInnerFrac = 0.86f;
        /// <summary>单位圆盘的段数（光点）。</summary>
        public const int DiscSegments = 12;
        /// <summary>透明队列起点（不透明之后）。</summary>
        public const int TransparentQueue = 3000;

        // ══════════════════════════════════════════════════════════════════
        // 读数（探针 / 交付报告用；**不起任何字符串**，避免每帧分配）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>存活的宿主实例数（**必须恒为 1**）。</summary>
        public static int Instances;
        /// <summary>池被建起来的次数（= 1）。</summary>
        public static int PoolBuilds;
        /// <summary>累计触发过几次 B 类爆发。</summary>
        public static int Bursts;
        /// <summary>因粒子池满而丢掉的粒子数（性能读数：正常应当长期为 0）。</summary>
        public static int ParticlesDropped;
        /// <summary>当前亮着的 A 类光环数（0 = 玩家没站在任何"条件满足的机制植物"地块上）。</summary>
        public static int HaloShown;
        /// <summary>轮询次数（= 会话时长 / `PollSeconds`）。</summary>
        public static int Polls;
        /// <summary>最近一次爆发的"触发源"编号（见 `Event*` 常量；0 = 还没有过）。</summary>
        public static int LastEventKind;
        /// <summary>最近一次爆发的触发地块索引（-1 = 还没有过）。</summary>
        public static int LastEventPlot = -1;

        // ── `LastEventKind` 的取值（**数字**，不是字符串：拼字符串会分配）──
        /// <summary>收获（`HarvestAtkMult` / `HarvestAtkSpeed` / `RichHarvest` / `ChainHarvest` / `DewDrop` / `ThirstBloom`）。</summary>
        public const int EventHarvest = 1;
        /// <summary>`Haste`：这一株成熟（提速兑现的那一刻）。</summary>
        public const int EventMature = 2;
        /// <summary>`WaterSave`：漏水时钟扣了一次水（省下的那一半就是它）。</summary>
        public const int EventWaterLeak = 3;
        /// <summary>`ElderGrowth`：存活满 60 秒的台阶。</summary>
        public const int EventElderStep = 4;
        /// <summary>`SunnyBloom` / `RainCloud`：天气由它触发。</summary>
        public const int EventWeather = 5;
        /// <summary>`FarmLuck`：场上发生了收割，幸运参与了掷骰。</summary>
        public const int EventLuck = 6;

        // ══════════════════════════════════════════════════════════════════
        // 共享资源（全场一份）
        // ══════════════════════════════════════════════════════════════════
        private static PlantAuraFx _instance;
        private static Mesh _ringMesh;      // 单位环（XZ 平面，半径 1）
        private static Mesh _discMesh;      // 单位圆盘（XZ 平面，半径 1）
        private static Mesh _quadMesh;      // 单位方片（XY 平面，1×1）—— 粒子用，每帧朝相机
        private static Material _fxMat;
        private static MaterialPropertyBlock _mpb;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ══════════════════════════════════════════════════════════════════
        // 实例状态
        // ══════════════════════════════════════════════════════════════════
        private GameObject _root;
        private Camera _cam;
        private float _camTimer;
        private bool _rootOn;

        private RunSession _session;
        private float _time;
        private float _pollTimer;

        // ── A 类：光环池 ──
        private GameObject[] _haloRing;
        private Transform[] _haloRingT;
        private MeshRenderer[] _haloRingR;
        private GameObject[][] _haloDot;
        private Transform[][] _haloDotT;
        private MeshRenderer[][] _haloDotR;
        private bool[] _haloOn;
        /// <summary>上一次给这个光环用的档位色（换了一株 ⇒ 光点重新上色；颜色没变就不写）。</summary>
        private Color[] _haloCol;

        // ── B 类：粒子池 ──
        private GameObject[] _pGo;
        private Transform[] _pT;
        private MeshRenderer[] _pR;
        private bool[] _pLive;
        private Vector3[] _pPos;
        private Vector3[] _pVel;
        private float[] _pAge;
        private float[] _pLife;
        private float[] _pSize;
        private Color[] _pCol;
        private int _pCursor;

        // ── B 类：扩散环池 ──
        private GameObject[] _rGo;
        private Transform[] _rT;
        private MeshRenderer[] _rR;
        private bool[] _rLive;
        private Vector3[] _rPos;
        private float[] _rAge;
        private Color[] _rCol;
        private int _rCursor;

        // ── 轮询快照（与 `Farm.Slots` 同长；只在地块数变化时重建）──
        private int[] _snapState;
        private int[] _snapWater;
        private int[] _snapWaterMax;
        private string[] _snapId;
        private float[] _cool;
        private bool _snapValid;
        private int _elderStep = -1;
        private int _weatherSeen = -1;

        /// <summary>确定性小随机（爆发粒子的方向/尺寸/寿命）。**不碰 `World.Rng`**（那是逻辑流）。</summary>
        private uint _rng = 0x51ED270Bu;

        // ══════════════════════════════════════════════════════════════════
        // 自举
        // ══════════════════════════════════════════════════════════════════

        /// <summary>进 play 模式时自己起一个 DDOL 宿主（与 `PlayerFacingMarker` / `GlobalJuice` 同一套自举）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("PLANT_AURA_FX");
            _instance = go.AddComponent<PlantAuraFx>();
        }

        private void Awake()
        {
            // 主城 / 战斗是两个场景，切换走 `LoadScene` ⇒ 宿主必须跨场景活着
            DontDestroyOnLoad(gameObject);
            Instances++;
            BuildPools();
        }

        private void OnDestroy()
        {
            Instances--;
            if (_instance == this) _instance = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // 每帧
        // ══════════════════════════════════════════════════════════════════
        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;                 // 暂停 / 命中冻结：整片特效跟着停（不推进、不读状态）
            _time += dt;

            RunSession s = LiveSession();
            if (s == null || s.World == null || s.World.Farm == null || s.World.Player == null)
            {
                EndSession();
                return;
            }
            if (!ReferenceEquals(s, _session)) BeginSession(s);

            SetRoot(true);

            CameraRefresh(dt);
            TickHalo(s);

            _pollTimer -= dt;
            if (_pollTimer <= 0f)
            {
                _pollTimer = PollSeconds;
                Poll(s);
            }

            TickBursts(dt);
        }

        /// <summary>
        /// ★ 找当前这一局：遍历 `GameBootstrap.Live`（与 `GlobalJuice.ReadPlayerDamage` 逐字相同的取法）★
        /// 不缓存引用 —— 退出战斗再进一局时 `Session` 是**新对象**，缓存会让特效画在上一局的地块上 ✗
        /// </summary>
        private static RunSession LiveSession()
        {
            var live = GameBootstrap.Live;
            for (int i = 0; i < live.Count; i++)
            {
                GameBootstrap b = live[i];
                if (b == null) continue;
                RunSession s = b.Session;
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>相机（粒子的朝向）。没有相机就每秒重试一次，`Camera.main` 不每帧调。</summary>
        private void CameraRefresh(float dt)
        {
            if (_cam != null && _cam.isActiveAndEnabled) return;
            _camTimer -= dt;
            if (_camTimer > 0f) return;
            _camTimer = 1f;
            _cam = Camera.main;
        }

        private void SetRoot(bool on)
        {
            if (_root == null || _rootOn == on) return;
            _rootOn = on;
            _root.SetActive(on);
        }

        // ══════════════════════════════════════════════════════════════════
        // A 类：光环
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 每帧：找"玩家脚下的机制地块"，条件满足的按顺序领取光环。
        ///
        /// ★ 判据与站田闪避**逐条一致**（`ProgMechanics.PlantPresenceDodge`）★
        ///   `plot.Unlocked && plot.CropDef != null` ＋ 矩形命中（半边长 `tileSize/2 + PlotStandMargin`）
        ///   ⇒ 重叠带上**多块都算命中**，所以这里也是"遍历全部命中地块"而不是"只取脚下最近那一块" ✓
        /// ★ 与那一处唯一的区别：那边取"闪避最高的那一份"，这边**每一株各领一个光环** ★
        ///   理由：特效要说的是"哪一株在庇护你"，不是"一共加了多少闪避" ✓
        /// </summary>
        private void TickHalo(RunSession s)
        {
            World w = s.World;
            FarmGrid farm = w.Farm;
            FarmPlot[] slots = farm.Slots;
            Vector2 pp = w.Player.Pos;
            float half = farm.TileSize * 0.5f + ProgMechanics.PlotStandMargin;

            int used = 0;
            for (int i = 0; i < slots.Length && used < HaloPoolSize; i++)
            {
                FarmPlot plot = slots[i];
                if (plot == null || !plot.Unlocked || plot.CropDef == null) continue;
                if (!InsidePlot(plot, pp, half)) continue;
                if (!HaloCondition(plot)) continue;

                Color col = TierColor(plot.CropDef.Grade);
                ShowHalo(used, pp, col);
                used++;
            }
            for (int i = used; i < HaloPoolSize; i++) HideHalo(i);
            HaloShown = used;
        }

        /// <summary>
        /// ★ A 类三株的"条件满足"判据 —— **全部读现有公开读数，一行机制数值都不重写** ★
        ///
        /// · `StandingDodge`（庇护藤）：无条件。站在它的田地上就有闪避（`PlantPresenceDodge` 的原话）。
        /// · `ThirstBloom`（空藤）：`plot.Water01 &lt; PlantMechanics.ThirstGoldRatio`
        ///   —— 与金币 ×1.6 的判据**同一个常量、同一个属性**（`PlantMechanics.HarvestGoldMult`）✓
        /// · `ThirstTolerant`（沙掌）：`plot.GrowthPaused`（= `Thirsty || NeedFullRefill`）
        ///   —— 那正是 `FarmPlot.TickGrowth2` 里 `thirstyGrowMult` 生效的闸门 ✓
        ///     （不缺水时它和别的植物长得一样，这条机制"什么都没做" ⇒ 不该亮）
        /// </summary>
        private static bool HaloCondition(FarmPlot plot)
        {
            SeedDef def = plot.CropDef;
            if (def == null) return false;
            if (PlantMechanics.Has(def, PlantMechanics.StandingDodge)) return true;
            if (PlantMechanics.Has(def, PlantMechanics.ThirstBloom))
                return plot.Water01 < PlantMechanics.ThirstGoldRatio;
            if (PlantMechanics.Has(def, PlantMechanics.ThirstTolerant)) return plot.GrowthPaused;
            return false;
        }

        /// <summary>亮起第 `i` 个光环：呼吸（半径 ±8%、α 0.18~0.32，周期 1.2s）+ 3 个环绕光点。</summary>
        private void ShowHalo(int i, Vector2 at, Color col)
        {
            bool wasOn = _haloOn[i];
            if (!wasOn)
            {
                _haloOn[i] = true;
                _haloRing[i].SetActive(true);
                for (int k = 0; k < HaloDotCount; k++) _haloDot[i][k].SetActive(true);
            }
            // 光点的 α 恒定 ⇒ 只在"刚亮起"或"换了另一株（颜色变了）"时写（呼吸只写在环上）
            if (!wasOn || _haloCol[i] != col)
            {
                _haloCol[i] = col;
                Color dot = col; dot.a = HaloDotAlpha;
                for (int k = 0; k < HaloDotCount; k++) WriteColor(_haloDotR[i][k], dot);
            }

            // 呼吸：全部光环同一相位 + 每个光环一个偏移（`HaloPoolSize` 个环不会一起"心跳"）
            float phase = _time * (Mathf.PI * 2f / HaloPeriod) + i * 0.9f;
            float breath = Mathf.Sin(phase);
            float radius = (HaloBaseRadius + HaloRadiusStep * i) * (1f + HaloBreathAmp * breath);
            float alpha = HaloAlphaMid + HaloAlphaSwing * breath;

            Transform rt = _haloRingT[i];
            rt.position = new Vector3(at.x, HaloY, at.y);
            rt.localScale = new Vector3(radius, 1f, radius);

            Color ringCol = col; ringCol.a = alpha;
            WriteColor(_haloRingR[i], ringCol);

            // 光点：沿环中线以 90°/s 绕行，等分 120°
            float baseAng = _time * HaloDotDegPerSec + i * 40f;
            float rr = radius * HaloDotRadiusFrac;
            for (int k = 0; k < HaloDotCount; k++)
            {
                float ang = (baseAng + k * (360f / HaloDotCount)) * Mathf.Deg2Rad;
                Transform dot = _haloDotT[i][k];
                dot.position = new Vector3(at.x + Mathf.Cos(ang) * rr, HaloY + HaloDotLiftY, at.y + Mathf.Sin(ang) * rr);
                dot.localScale = new Vector3(HaloDotSize, 1f, HaloDotSize);
            }
        }

        private void HideHalo(int i)
        {
            if (!_haloOn[i]) return;
            _haloOn[i] = false;
            _haloRing[i].SetActive(false);
            for (int k = 0; k < HaloDotCount; k++) _haloDot[i][k].SetActive(false);
        }

        // ══════════════════════════════════════════════════════════════════
        // B 类：轮询（触发点反推）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 每 `PollSeconds` 跑一次：把 `Farm.Slots` 与上一份快照比一遍，六条判据各自决定要不要爆。
        /// 六条判据与 11 个 B 类机制的对应关系：
        ///   ① 收获（`Grown` → `Growing`）⇒ `HarvestAtkMult` / `HarvestAtkSpeed` / `RichHarvest` /
        ///      `ChainHarvest` / `DewDrop` / `ThirstBloom`（后者还要 `Water01 &lt; 0.5`，与加成同一条判据）
        ///   ② 成熟（非 `Grown` → `Grown`）⇒ `Haste`（提速兑现的那一刻）
        ///   ③ 漏水（`Water` 下降且仍在生长）⇒ `WaterSave`
        ///   ④ 存活满 60 秒的台阶（`World.Time / ElderStepSeconds` 的整数位变化）⇒ `ElderGrowth`
        ///   ⑤ 天气计数变化（`ProgMechanics.WeatherCount`）⇒ `SunnyBloom` / `RainCloud`
        ///      （只在**触发它的那一株**脚下爆，触发者读 `ProgMechanics.WeatherSourceId`）
        ///   ⑥ 本 poll 里发生过收获（任意格）⇒ `FarmLuck`（幸运参与的是每一次收割掷骰）
        ///
        /// ★ 为什么"收获"能这样反推 ★
        ///   `FarmPlot` 里 `Grown → Growing` **只有** `Harvest(f)` 一条路（`RemoveGrowing` 要求 `Growing`，
        ///   `OnPlantDied` 走 `ResetToEmpty` 到 `Empty`）⇒ 这条状态变化与"收割成功"一一对应 ✓
        ///   `HarvestPlot` 每格各调一次 ⇒ **连锁收获**的同列每一格都会各自爆一次 ✓
        /// </summary>
        private void Poll(RunSession s)
        {
            Polls++;
            FarmPlot[] slots = s.World.Farm.Slots;
            EnsureSnapshots(slots.Length);

            int budget = MaxBurstsPerPoll;
            bool anyHarvest = false;

            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot p = slots[i];
                if (p == null) continue;

                SeedDef def = p.CropDef;
                int st = (int)p.State;
                int water = p.Water;
                int waterMax = p.WaterMax;
                string id = def != null ? def.Id : null;

                if (def != null && _snapValid && i < _snapId.Length && _snapId[i] == id)
                {
                    int prevState = _snapState[i];
                    int prevWater = _snapWater[i];
                    int prevWaterMax = _snapWaterMax[i];

                    // ① 收获
                    if (prevState == (int)PlotState.Grown && st == (int)PlotState.Growing)
                    {
                        anyHarvest = true;
                        if (HarvestBurst(p, prevWater, prevWaterMax))
                            Burst(p, TierColor(def.Grade), EventHarvest, ref budget);
                    }
                    // ② 成熟（`Haste`）
                    else if (prevState != (int)PlotState.Grown && st == (int)PlotState.Grown)
                    {
                        if (PlantMechanics.Has(def, PlantMechanics.Haste))
                            Burst(p, TierColor(def.Grade), EventMature, ref budget);
                    }
                    // ③ 漏水（`WaterSave`）
                    if (st == (int)PlotState.Growing && prevWater > 0 && water < prevWater
                        && PlantMechanics.Has(def, PlantMechanics.WaterSave))
                        Burst(p, TierColor(def.Grade), EventWaterLeak, ref budget);
                }

                if (i < _snapId.Length)
                {
                    _snapState[i] = st;
                    _snapWater[i] = water;
                    _snapWaterMax[i] = waterMax;
                    _snapId[i] = id;
                }
            }
            _snapValid = true;

            // ④ 古木：存活每满 60 秒的台阶（与 `PlantMechanics.GrowSpeedMult` 的 `Floor(Time/60)` 同一口径）
            int step = Mathf.FloorToInt(Mathf.Max(0f, s.World.Time) / PlantMechanics.ElderStepSeconds);
            if (_elderStep < 0) _elderStep = step;
            else if (step != _elderStep)
            {
                _elderStep = step;
                for (int i = 0; i < slots.Length && budget > 0; i++)
                {
                    FarmPlot p = slots[i];
                    if (p == null || p.CropDef == null) continue;
                    if (!PlantMechanics.Has(p.CropDef, PlantMechanics.ElderGrowth)) continue;
                    Burst(p, TierColor(p.CropDef.Grade), EventElderStep, ref budget);
                }
            }

            // ⑤ 天气：由这一株触发 ⇒ 在**它的**地块中心爆
            if (_weatherSeen < 0) _weatherSeen = ProgMechanics.WeatherCount;
            else if (ProgMechanics.WeatherCount != _weatherSeen)
            {
                _weatherSeen = ProgMechanics.WeatherCount;
                string src = ProgMechanics.WeatherSourceId;
                if (!string.IsNullOrEmpty(src))
                {
                    for (int i = 0; i < slots.Length && budget > 0; i++)
                    {
                        FarmPlot p = slots[i];
                        if (p == null || p.CropDef == null) continue;
                        if (p.CropDef.Id != src) continue;
                        SeedDef def = p.CropDef;
                        if (!PlantMechanics.Has(def, PlantMechanics.SunnyBloom)
                            && !PlantMechanics.Has(def, PlantMechanics.RainCloud)) continue;
                        Burst(p, TierColor(def.Grade), EventWeather, ref budget);
                    }
                }
            }

            // ⑥ 幸运：本 poll 里发生过收割 ⇒ 每一株祥瑞各爆一次（幸运参与的就是这些掷骰）
            if (anyHarvest)
            {
                for (int i = 0; i < slots.Length && budget > 0; i++)
                {
                    FarmPlot p = slots[i];
                    if (p == null || p.CropDef == null) continue;
                    if (!PlantMechanics.Has(p.CropDef, PlantMechanics.FarmLuck)) continue;
                    Burst(p, TierColor(p.CropDef.Grade), EventLuck, ref budget);
                }
            }
        }

        /// <summary>
        /// 这一格的这一株，收割时是否该爆。
        /// `ThirstBloom` 还要求"够渴" —— 判据用**收割前那份快照**的水位算 `Water01`：
        ///   `Harvest()` 不动水，但雨天每 0.5 秒会补满全场 ⇒ 若用"现在"的水位，
        ///   恰好在这 0.15 秒窗口里下过雨就会把一次真的加成判成"没触发" ✗
        ///   用上一份快照的水位 = 收割那一刻（或更早 0.15 秒内）的水位，方向永远偏"更渴" ✓
        /// </summary>
        private static bool HarvestBurst(FarmPlot p, int prevWater, int prevWaterMax)
        {
            SeedDef def = p.CropDef;
            if (def == null) return false;
            if (PlantMechanics.Has(def, PlantMechanics.HarvestAtkMult)) return true;
            if (PlantMechanics.Has(def, PlantMechanics.HarvestAtkSpeed)) return true;
            if (PlantMechanics.Has(def, PlantMechanics.RichHarvest)) return true;
            if (PlantMechanics.Has(def, PlantMechanics.ChainHarvest)) return true;
            if (PlantMechanics.Has(def, PlantMechanics.DewDrop)) return true;
            if (PlantMechanics.Has(def, PlantMechanics.ThirstBloom))
            {
                float w01 = prevWaterMax > 0 ? Mathf.Clamp01(prevWater / (float)prevWaterMax) : p.Water01;
                return w01 < PlantMechanics.ThirstGoldRatio;
            }
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        // B 类：爆发（池化）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>在 `plot` 的中心放一次爆发：6~10 个粒子 + 1 个扩散环。池满就少放，绝不再建对象。</summary>
        private void Burst(FarmPlot plot, Color col, int kind, ref int budget)
        {
            if (plot == null || budget <= 0) return;
            int idx = plot.Index;
            if (idx >= 0 && idx < _cool.Length)
            {
                if (_time - _cool[idx] < BurstCooldownSeconds) return;
                _cool[idx] = _time;
            }
            budget--;
            Bursts++;
            LastEventKind = kind;
            LastEventPlot = idx;

            int span = BurstParticleMax - BurstParticleMin;
            int count = BurstParticleMin + Mathf.Clamp(Mathf.FloorToInt(Next01() * (span + 1)), 0, span);
            for (int k = 0; k < count; k++) SpawnParticle(plot.World, col);
            SpawnRing(plot.World, col);
        }

        private void SpawnParticle(Vector2 at, Color col)
        {
            int i = NextFreeParticle();
            if (i < 0) return;

            float a = Next01() * Mathf.PI * 2f;
            float rad = Next01() * ParticleSpawnRadius;
            _pPos[i] = new Vector3(at.x + Mathf.Cos(a) * rad,
                                   Mathf.Lerp(ParticleSpawnYMin, ParticleSpawnYMax, Next01()),
                                   at.y + Mathf.Sin(a) * rad);
            _pVel[i] = new Vector3((Next01() * 2f - 1f) * ParticleSpreadMps,
                                   Mathf.Lerp(ParticleRiseMin, ParticleRiseMax, Next01()),
                                   (Next01() * 2f - 1f) * ParticleSpreadMps);
            _pAge[i] = 0f;
            _pLife[i] = Mathf.Lerp(ParticleLifeMin, ParticleLifeMax, Next01());
            _pSize[i] = Mathf.Lerp(ParticleSizeMin, ParticleSizeMax, Next01());
            _pCol[i] = col;
            _pLive[i] = true;
            _pT[i].localScale = new Vector3(_pSize[i], _pSize[i], _pSize[i]);
            _pGo[i].SetActive(true);
        }

        private void SpawnRing(Vector2 at, Color col)
        {
            int i = NextFreeRing();
            if (i < 0) return;
            _rPos[i] = new Vector3(at.x, RingY, at.y);
            _rAge[i] = 0f;
            _rCol[i] = col;
            _rLive[i] = true;
            _rT[i].position = _rPos[i];
            _rT[i].localScale = new Vector3(RingRadiusStart, 1f, RingRadiusStart);
            _rGo[i].SetActive(true);
        }

        private int NextFreeParticle()
        {
            for (int k = 0; k < ParticlePoolSize; k++)
            {
                int i = _pCursor + k;
                if (i >= ParticlePoolSize) i -= ParticlePoolSize;
                if (_pLive[i]) continue;
                _pCursor = i + 1; if (_pCursor >= ParticlePoolSize) _pCursor = 0;
                return i;
            }
            ParticlesDropped++;
            return -1;
        }

        private int NextFreeRing()
        {
            for (int k = 0; k < RingPoolSize; k++)
            {
                int i = _rCursor + k;
                if (i >= RingPoolSize) i -= RingPoolSize;
                if (_rLive[i]) continue;
                _rCursor = i + 1; if (_rCursor >= RingPoolSize) _rCursor = 0;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// 兜底朝向：**拿不到相机时**让方片躺平朝上（`Euler(90,0,0)` 把 XY 方片转成 XZ）。
        /// 为什么必须有它：`Camera.main` 依赖相机上的 `MainCamera` tag，
        /// 而本工程多处在 `Camera.main == null` 时才**补建**一台（`GameBootstrap.cs:513`）——
        /// 若那台相机迟到一帧，粒子会以 identity 朝向出现（法线朝 −Z ⇒ 俯视时几乎看不见）✗
        /// 躺平之后任何俯视镜头都读得到，最坏情况是"变成了贴地的光斑"，不会变成"什么都没发生" ✓
        /// </summary>
        private static readonly Quaternion FlatBillboard = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>推进所有活着的粒子与环：上升 + 淡出；环从 0.2 扩到 1.2 米并在 0.35 秒内消失。</summary>
        private void TickBursts(float dt)
        {
            // 与 `CombatSunDisc` 同一条 billboard 口径：`rotation = 相机 rotation`
            Quaternion billboard = _cam != null ? _cam.transform.rotation : FlatBillboard;

            for (int i = 0; i < ParticlePoolSize; i++)
            {
                if (!_pLive[i]) continue;
                _pAge[i] += dt;
                if (_pAge[i] >= _pLife[i])
                {
                    _pLive[i] = false;
                    _pGo[i].SetActive(false);
                    continue;
                }
                float t = _pAge[i] / _pLife[i];
                _pPos[i] += _pVel[i] * dt;

                Transform tr = _pT[i];
                tr.position = _pPos[i];
                tr.rotation = billboard;                       // 方片永远面对相机
                Color c = _pCol[i];
                c.a = ParticleAlpha * (1f - t);
                WriteColor(_pR[i], c);
            }

            for (int i = 0; i < RingPoolSize; i++)
            {
                if (!_rLive[i]) continue;
                _rAge[i] += dt;
                if (_rAge[i] >= RingLife)
                {
                    _rLive[i] = false;
                    _rGo[i].SetActive(false);
                    continue;
                }
                float t = _rAge[i] / RingLife;
                float ease = 1f - (1f - t) * (1f - t);          // 先快后慢
                float r = Mathf.Lerp(RingRadiusStart, RingRadiusEnd, ease);
                _rT[i].localScale = new Vector3(r, 1f, r);
                Color c = _rCol[i];
                c.a = RingAlpha * (1f - t);
                WriteColor(_rR[i], c);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 会话生命周期
        // ══════════════════════════════════════════════════════════════════

        private void BeginSession(RunSession s)
        {
            _session = s;
            _snapValid = false;
            _elderStep = -1;
            _weatherSeen = ProgMechanics.WeatherCount;
            _pollTimer = 0f;
            EnsureSnapshots(s.World.Farm != null ? s.World.Farm.Slots.Length : 0);
            for (int i = 0; i < HaloPoolSize; i++) HideHalo(i);
            ClearBursts();
            HaloShown = 0;
        }

        /// <summary>没有会话（主城 / 结算 / 退出中）：整片隐藏，不推进、不轮询。</summary>
        private void EndSession()
        {
            if (_session == null && !_rootOn) return;
            _session = null;
            _snapValid = false;
            for (int i = 0; i < HaloPoolSize; i++) HideHalo(i);
            HaloShown = 0;
            ClearBursts();
            SetRoot(false);
        }

        private void ClearBursts()
        {
            for (int i = 0; i < ParticlePoolSize; i++)
            {
                if (!_pLive[i]) continue;
                _pLive[i] = false;
                _pGo[i].SetActive(false);
            }
            for (int i = 0; i < RingPoolSize; i++)
            {
                if (!_rLive[i]) continue;
                _rLive[i] = false;
                _rGo[i].SetActive(false);
            }
        }

        /// <summary>快照数组与地块数对齐。**只在地块数变化时分配**（长出一排新地，一局最多几次）。</summary>
        private void EnsureSnapshots(int count)
        {
            if (_snapState != null && _snapState.Length == count) return;
            _snapState = new int[count];
            _snapWater = new int[count];
            _snapWaterMax = new int[count];
            _snapId = new string[count];
            _cool = new float[count];
            for (int i = 0; i < count; i++) _cool[i] = -999f;
            _snapValid = false;
        }

        // ══════════════════════════════════════════════════════════════════
        // 建池（**只跑一次**，之后运行期零 Instantiate / 零 Destroy）
        // ══════════════════════════════════════════════════════════════════

        private void BuildPools()
        {
            _root = new GameObject("PlantAuraFxRoot");
            _root.transform.SetParent(transform, false);
            _rootOn = true;

            Mesh ring = RingMesh();
            Mesh disc = DiscMesh();
            Mesh quad = QuadMesh();
            Material mat = FxMaterial();

            // ── 光环池 ──
            _haloRing = new GameObject[HaloPoolSize];
            _haloRingT = new Transform[HaloPoolSize];
            _haloRingR = new MeshRenderer[HaloPoolSize];
            _haloDot = new GameObject[HaloPoolSize][];
            _haloDotT = new Transform[HaloPoolSize][];
            _haloDotR = new MeshRenderer[HaloPoolSize][];
            _haloOn = new bool[HaloPoolSize];
            _haloCol = new Color[HaloPoolSize];
            for (int i = 0; i < HaloPoolSize; i++)
            {
                _haloRing[i] = AddPiece("PlantAuraHalo" + i, _root.transform, ring, mat, 0);
                _haloRingT[i] = _haloRing[i].transform;
                _haloRingR[i] = _haloRing[i].GetComponent<MeshRenderer>();
                _haloRing[i].SetActive(false);

                _haloDot[i] = new GameObject[HaloDotCount];
                _haloDotT[i] = new Transform[HaloDotCount];
                _haloDotR[i] = new MeshRenderer[HaloDotCount];
                for (int k = 0; k < HaloDotCount; k++)
                {
                    _haloDot[i][k] = AddPiece("PlantAuraHaloDot" + i + "_" + k, _root.transform, disc, mat, 1);
                    _haloDotT[i][k] = _haloDot[i][k].transform;
                    _haloDotR[i][k] = _haloDot[i][k].GetComponent<MeshRenderer>();
                    _haloDot[i][k].SetActive(false);
                }
                _haloOn[i] = false;
            }

            // ── 粒子池 ──
            _pGo = new GameObject[ParticlePoolSize];
            _pT = new Transform[ParticlePoolSize];
            _pR = new MeshRenderer[ParticlePoolSize];
            _pLive = new bool[ParticlePoolSize];
            _pPos = new Vector3[ParticlePoolSize];
            _pVel = new Vector3[ParticlePoolSize];
            _pAge = new float[ParticlePoolSize];
            _pLife = new float[ParticlePoolSize];
            _pSize = new float[ParticlePoolSize];
            _pCol = new Color[ParticlePoolSize];
            for (int i = 0; i < ParticlePoolSize; i++)
            {
                _pGo[i] = AddPiece("PlantAuraMote" + i, _root.transform, quad, mat, 3);
                _pT[i] = _pGo[i].transform;
                _pR[i] = _pGo[i].GetComponent<MeshRenderer>();
                _pGo[i].SetActive(false);
            }

            // ── 扩散环池 ──
            _rGo = new GameObject[RingPoolSize];
            _rT = new Transform[RingPoolSize];
            _rR = new MeshRenderer[RingPoolSize];
            _rLive = new bool[RingPoolSize];
            _rPos = new Vector3[RingPoolSize];
            _rAge = new float[RingPoolSize];
            _rCol = new Color[RingPoolSize];
            for (int i = 0; i < RingPoolSize; i++)
            {
                _rGo[i] = AddPiece("PlantAuraBurst" + i, _root.transform, ring, mat, 2);
                _rT[i] = _rGo[i].transform;
                _rR[i] = _rGo[i].GetComponent<MeshRenderer>();
                _rGo[i].SetActive(false);
            }

            PoolBuilds++;
        }

        /// <summary>一片贴地/空中部件：共享网格、共享材质、不投影、不吃阴影、无 Collider。</summary>
        private static GameObject AddPiece(string name, Transform parent, Mesh mesh, Material mat, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // 贴地的特效不投影（否则地上糊出第二片）
            mr.receiveShadows = false;                      // 也不吃阴影（否则被压暗）
            mr.sortingOrder = sortingOrder;                 // 环 0 / 点 1 / 爆发环 2 / 粒子 3
            return go;
        }

        // ══════════════════════════════════════════════════════════════════
        // 网格（各建一次，静态缓存）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>单位环（XZ 平面，外径 1、内径 0.86）⇒ 用 `localScale = r` 就是半径 r 的环。</summary>
        private static Mesh RingMesh()
        {
            if (_ringMesh != null) return _ringMesh;

            int segs = RingSegments;
            var v = new Vector3[segs * 4];
            var tris = new int[segs * 6];
            for (int i = 0; i < segs; i++)
            {
                float a0 = (float)i / segs * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / segs * Mathf.PI * 2f;
                int b = i * 4;
                v[b + 0] = new Vector3(Mathf.Sin(a0) * RingInnerFrac, 0f, Mathf.Cos(a0) * RingInnerFrac);
                v[b + 1] = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                v[b + 2] = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                v[b + 3] = new Vector3(Mathf.Sin(a1) * RingInnerFrac, 0f, Mathf.Cos(a1) * RingInnerFrac);
            }
            int n = 0;
            for (int i = 0; i < segs; i++)
            {
                int b = i * 4;
                tris[n++] = b + 0; tris[n++] = b + 1; tris[n++] = b + 2;
                tris[n++] = b + 0; tris[n++] = b + 2; tris[n++] = b + 3;
            }
            _ringMesh = BuildFlatMesh("PlantAuraRing", v, tris, Vector3.up);
            return _ringMesh;
        }

        /// <summary>单位圆盘（XZ 平面，半径 1）⇒ 光点。</summary>
        private static Mesh DiscMesh()
        {
            if (_discMesh != null) return _discMesh;

            int segs = DiscSegments;
            var v = new Vector3[segs + 1];
            var tris = new int[segs * 3];
            v[0] = Vector3.zero;
            for (int k = 0; k < segs; k++)
            {
                float a = (float)k / segs * Mathf.PI * 2f;
                v[1 + k] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
            int n = 0;
            for (int k = 0; k < segs; k++)
            {
                tris[n++] = 0;
                tris[n++] = 1 + k;
                tris[n++] = 1 + (k + 1) % segs;
            }
            _discMesh = BuildFlatMesh("PlantAuraDisc", v, tris, Vector3.up);
            return _discMesh;
        }

        /// <summary>单位方片（**XY 平面**，-0.5..0.5）⇒ 粒子。朝向由每帧的 `transform.rotation` 给（朝相机）。</summary>
        private static Mesh QuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;

            var v = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };
            _quadMesh = BuildFlatMesh("PlantAuraQuad", v, tris, Vector3.back);
            return _quadMesh;
        }

        /// <summary>
        /// 一片极简网格：法线写死（不 `RecalculateNormals` —— 双面绕序会互相抵消成零向量，
        /// 与 `PlayerFacingMarker.BuildFlatMesh` 同一条注记）；UV 全 (0.5,0.5)（采样 1×1 白图，颜色只来自 MPB）。
        /// </summary>
        private static Mesh BuildFlatMesh(string name, Vector3[] verts, int[] tris, Vector3 normal)
        {
            var uv = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            var colors = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                uv[i] = new Vector2(0.5f, 0.5f);
                normals[i] = normal;
                colors[i] = Color.white;
            }
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uv);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ══════════════════════════════════════════════════════════════════
        // 材质 / 上色
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 一份材质、全场共用；颜色一律走 `MaterialPropertyBlock`
        /// （= 运行期 0 个 Material 实例、破坏不了合批）。
        /// 着色器兜底链与 `PlayerFacingMarker.BuildMat` 逐条相同：
        /// `Sprites/Default` → `Universal Render Pipeline/Unlit` → `Standard`。
        /// </summary>
        private static Material FxMaterial()
        {
            if (_fxMat != null) return _fxMat;

            Shader sh = Shader.Find("Sprites/Default");
            bool sprite = sh != null;
            if (!sprite)
            {
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Standard");
            }
            var m = new Material(sh) { name = sprite ? "M_PlantAuraFx" : "M_PlantAuraFxFALLBACK" };
            m.mainTexture = Texture2D.whiteTexture;    // 采样 1×1 白图（颜色只来自 MPB）
            m.color = Color.white;
            // `Sprites/Default` 的顶点阶段还有 `_RendererColor`（平时由 SpriteRenderer 送进来）。
            // 用 MeshRenderer 画 ⇒ 没人写它。这里是"一旦它是黑的、整片会被乘成黑"的那一行防御 ✓
            m.SetColor("_RendererColor", Color.white);
            m.renderQueue = TransparentQueue;
            if (!sprite)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // URP：Transparent
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // URP：Alpha
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            }
            _fxMat = m;
            return _fxMat;
        }

        /// <summary>把一个颜色写进某个 Renderer 的 MPB（**不动材质**）。两个属性名都写：内置 `_Color` / URP `_BaseColor`。</summary>
        private static void WriteColor(Renderer r, Color c)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetColor(ColorId, c);
            _mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        // ══════════════════════════════════════════════════════════════════
        // 档位配色 / 小工具
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 档位配色（需求给定：S = 金 / A = 紫金 / B = 青绿；C/D 本轮没有机制植物，给银蓝兜底）。
        /// 取的是**高饱和、中等明度**的一档：贴在深色泥土上仍然读得出颜色。
        /// </summary>
        public static Color TierColor(SeedGrade grade)
        {
            switch (grade)
            {
                case SeedGrade.S: return new Color(1.00f, 0.82f, 0.30f);   // 金
                case SeedGrade.A: return new Color(0.78f, 0.55f, 0.98f);   // 紫金
                case SeedGrade.B: return new Color(0.30f, 0.88f, 0.66f);   // 青绿
                default: return new Color(0.62f, 0.80f, 0.94f);            // 银蓝（C / D）
            }
        }

        /// <summary>
        /// `pos` 是否落在 `plot` 那块地的矩形内。
        /// ★ 与 `ProgMechanics.InsidePlot`（private）逐字相同的形状，半边长由 `PlotStandMargin` 算 ⇒
        ///   调余量时两边一起动。见类注释第一节末尾的最小改动方案（把它改成 public 就能删掉这段）。
        /// </summary>
        private static bool InsidePlot(FarmPlot plot, Vector2 pos, float half)
        {
            Vector2 d = plot.World - pos;
            return Mathf.Abs(d.x) <= half && Mathf.Abs(d.y) <= half;
        }

        /// <summary>确定性小随机 [0,1)（xorshift32）。**不碰 `World.Rng` / `VisualRng`**（那是逻辑/表现流）。</summary>
        private float Next01()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFFu) / 16777216f;
        }

        /// <summary>一行读数（探针 / 截图叠字用；只在被调用时拼字符串，不进每帧路径）。</summary>
        public static string Status()
        {
            return $"脉冲光环={HaloShown}/{HaloPoolSize}（半径 {HaloBaseRadius:0.00}±{HaloBreathAmp * 100f:0}%," +
                   $" α {HaloAlphaMid - HaloAlphaSwing:0.00}~{HaloAlphaMid + HaloAlphaSwing:0.00}," +
                   $" 周期 {HaloPeriod:0.0}s, 光点 {HaloDotCount} 个 @{HaloDotDegPerSec:0}°/s）" +
                   $"｜爆发={Bursts} 丢粒子={ParticlesDropped} 轮询={Polls}@{PollSeconds:0.00}s" +
                   $" 最近=事件{LastEventKind}/地块{LastEventPlot}" +
                   $"｜池：光环{HaloPoolSize} 粒子{ParticlePoolSize} 环{RingPoolSize}" +
                   $" 实例={Instances} 建池={PoolBuilds}";
        }
    }
}