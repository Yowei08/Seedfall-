using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 世界表现层：把逻辑状态脏值同步到 GameObject。
    /// 只读 RunSession，绝不回写逻辑（架构案 §数据流）。
    /// </summary>
    public sealed partial class WorldView
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private sealed class Marker
        {
            public GameObject Go;
            public float Ttl;
            public float Total;
            /// <summary>v176：逐实例材质。**到期必须显式销毁** —— 它不在任何缓存里，没人替它收 ✓</summary>
            public Material Mat;
        }

        // ★ T2：`_plants` / `_plantModels` 两个字典已删除 ✗ —— 它们只被 `SyncPlants` 读写 ✓
        //   （植物实体的表现已整段下线，见下方注释 ✓）
        private readonly Dictionary<int, GameObject> _enemies = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _pickups = new Dictionary<int, GameObject>();
        private readonly List<Marker> _markers = new List<Marker>();
        private readonly List<int> _scratch = new List<int>();

        // v59：模型化的表现体。有模型的用模型，没有的（BOSS / 小猪）继续走图元，
        // 这样"资源没做完"不会阻塞玩法，也不会让场上出现空洞。
        private readonly Dictionary<int, ModelView> _enemyModels = new Dictionary<int, ModelView>();
        // ★ T2：`_plantModels` 已删除（同上）✗
        private readonly Dictionary<int, Vector3> _lastPos = new Dictionary<int, Vector3>();
        private ModelView _playerModel;

        /// <summary>
        /// v64：模型显示放大系数。
        /// 模型是按"玩法尺度"建模的（小恶魔 0.67m），但俯视跟随相机下角色只有几十像素，
        /// 细节看不清、辨认度低。放大的是**表现**，碰撞半径仍是玩法数据，不受影响。
        /// v68：按用户反馈再放大一档（1.35 → 1.75）。
        /// </summary>
        private const float CharScale = 2.60f;
        private const float PlantScale = 2.50f;

        /// <summary>
        /// 玩家模型的额外放大倍率（在 CharScale 之上再乘）。
        /// 需求："玩家模型大于普通怪物模型一点"。
        /// 建模本身玩家就比小恶魔高（0.961m vs 0.673m，约 1.43×），
        /// 这里再补 1.2× —— 合起来约 1.7×，既明显是"主角"又不至于大到挡住战场。
        /// </summary>
        private const float PlayerScale = 0.627f;   // v129：1.20 → 0.627，见下方说明

        /// <summary>敌人霓虹描边：红。保留黑色墨线，这层套在墨线外面。
        /// v92：alpha 0.85→1.00（需求"加强怪物红色霓虹光描边"）——半透明的描边在暗色夜里会被背景吃掉。
        /// ★ v208：色值改成**高饱和纯红** `#FF1F3D`（用户给的参考色）★
        /// 旧值 (1.00,0.13,0.10) 偏橙红，在暖色日光（方向光 (1.00,0.96,0.90)、强度 1.45）下
        /// 与草地的高光混成一团；提高蓝分量到 0.24 让它**在暖背景里偏冷**，对比更强 ✓</summary>
        private static readonly Color NeonEnemy = new Color(1.00f, 0.12f, 0.24f, 1.00f);

        /// <summary>
        /// ★ v208：玩家霓虹描边 = **白** ★
        /// 为什么玩家也要有：用户原话"玩家附加同样高亮的白色描边"。
        /// 白霓虹不与敌人红抢语言（红=威胁、白=自己），也不与作物品质五色抢 ✓
        /// </summary>
        private static readonly Color NeonPlayer = new Color(1.00f, 1.00f, 1.00f, 1.00f);

        // ── v208：三套霓虹的**粗细/亮度**，一处集中，改观感只改这里 ──
        // 单位：像素（屏幕空间下限）与倍率（颜色强度，>`1` 就是 HDR 加色）
        //
        // ★★★ v210：三个数**全部按实测重定**（v208 那一组在屏幕上是看不出来的）★★★
        // 实测（v210 探针 · 玩法机位 · 1080p）：
        //   相机到敌人 **20.84 米**｜1 屏幕像素 ≈ **2.32 厘米**
        //   敌人霓虹 2.0 像素 ⇒ 环宽 4.64 厘米；而敌人**74 像素**高 ⇒ 只占 2.7%
        //   玩家霓虹 6.00 厘米 ⇒ 近处（8.5 米）是 **6.3 像素**、远处靠 2.2 像素兜底
        //   ⇒ 敌人太细、玩家太粗，两边都不对；而且加色混合把红冲成米黄（见 shader 的 Blend 注释）
        // 现在：敌人/玩家 4.0 像素、作物 2.5 像素 —— 敌人 ≈ 5.5% 身高、作物 ≈ 3% 株高 ✓
        private const float NeonPxChar = 4.0f;      // 敌人 / 玩家：要"一眼可见"
        private const float NeonPxPlant = 2.5f;     // 作物：细一圈，别把植株吞掉
        private const float NeonBoostChar = 1.60f;  // 暗处也亮（`[HDR]`，替换混合下直接就是环的颜色）
        private const float NeonBoostPlant = 1.35f;

        /// <summary>
        /// ★ v210：玩家霓虹的**米数**（显式给，不再走 `RawHeight × 5%` 的默认值）★
        /// 默认值对 1.1 米高的玩家会算出 **5.5 厘米**，在近处（8.5 米、每像素 0.95 厘米）
        /// 画成 **6.3 像素宽的一条白带** —— 本轮基线抓图里那几块"白斑"就是它 ✗
        /// 3.0 厘米在近处约 3.2 像素、远处由 4.0 像素兜底 ⇒ 一圈干净的白边 ✓
        /// </summary>
        private const float NeonPlayerWidthMeters = 0.030f;

        /// <summary>
        /// ★★ v209：**夜间敌人发光的开关**（需求原话："为夜晚的敌人增加发光"）★★
        ///
        /// 关掉它 ⇒ 每个敌人每帧拿到的系数恒为 `0` ⇒ 霓虹与边缘光**与 v208 逐位相同** ✓
        /// （`ModelView.SetNightGlow(0)` 在系数已经是 0 时直接早退，一次 MPB 都不写）
        ///
        /// ★ 它只影响**敌人** ★
        /// 这个开关只出现在敌人同步循环里；玩家（`SyncPlayer`）与作物（`ApplyCropNeon`）
        /// **从不调用** `SetNightGlow` ⇒ 它们的系数恒为 0 ⇒ 观感一个像素都不变 ✓
        /// 这正是判据要的"敌人明显发亮、玩家/植物不那么亮" ✓
        ///
        /// ★ 系数的来源不是这里 ★
        /// 读的是 `NightLighting.NightGlow01`，而它每帧由 `DayNightCycle.Sample(t).Glow` 写入
        /// ⇒ "现在是不是夜里"全场只有**一个**判据，本类不自己再算一遍时间 ✓
        /// </summary>
        public bool NightGlowEnabled = true;

        /// <summary>
        /// 品质色 → 霓虹色。品质色本身偏暗（拿来做卡通底色刚好），
        /// 直接拿来发光会"亮不起来"，所以这里提亮 + 拉饱和。
        /// </summary>
        private static Color NeonOf(Color grade)
        {
            float max = Mathf.Max(grade.r, Mathf.Max(grade.g, grade.b));
            // ★ v208：改成**按比例**提亮，**不再 Clamp01** ★
            //
            // 旧式子 `Clamp01(grade.rgb * Min(2.6, 1.60 / max))` 会把通道逐个截到 1.0，
            // 于是高饱和的品质色**被截成同一个白**：
            //   B 蓝 (0.35,0.65,1.00) ×1.6 = (0.56, 1.04→**1.0**, 1.60→**1.0**) = **青白**
            //   A 紫 (0.85,0.55,1.00) ×1.6 = (**1.0**, 0.88, **1.0**)          = **粉白**
            // ⇒ "五色品质描边"里有两档读出来几乎分不开 ✗
            // 现在把**最亮通道**抬到 1.15（`[HDR]` 允许 >1，霓虹 Pass 又是 `Blend One One` 加色），
            // 其余通道按同一比例走 ⇒ **色相原样保留**，只是更亮 ✓
            // 读数（v208 探针打印）：D 白 / C 绿 / B 蓝 / A 紫 / S 金，五档互不相同 ✓
            float boost = max > 0.01f ? Mathf.Min(3.0f, 1.15f / max) : 1f;
            return new Color(grade.r * boost, grade.g * boost, grade.b * boost, 1.00f);
        }

        /// <summary>
        /// ★ v208：作物霓虹的**唯一装配点**（4 处调用点全部改成走这里）★
        ///
        /// 三个数从哪来：
        ///   · 颜色 = `NeonOf(品质色)` —— D 白/C 绿/B 蓝/A 紫/S 金（见 `Projectile.GradeColor`）
        ///   · 米数 = 株高 × 3%，夹 6mm~2.5cm（**不是** `SetNeon` 默认的 5%、夹 1.8~9cm）
        ///   · 像素下限 = `NeonPxPlant`(0.8)，**不是**敌人那档 2.2
        ///
        /// 为什么作物必须比敌人细（实测量级，不是估计）：
        ///   游戏机位 26 米、1080p ⇒ 1 屏幕像素 ≈ 2.9 厘米。
        ///   敌人用 2.2 像素 ⇒ 外扩 ~6.4cm，配 1.5m 身高 = 看得清的红圈 ✓
        ///   作物同一档 2.2 像素 ⇒ 同样 6.4cm，而植株只有 0.3~1.1m，
        ///   **壳比株宽还大** ⇒ 整株被自己的霓虹壳包住，读出来就是一坨纯色 ✗
        ///   （B 档品质色是蓝 ⇒ 一片 B 档作物看起来"全部变蓝"—— 这正是用户报的那个症状的形态）
        ///   ⇒ 作物压到 0.8 像素 ≈ 2.3cm，是"一圈细亮边"而不是"一坨" ✓
        /// </summary>
        private static void ApplyCropNeon(ModelView mv, Color neon)
        {
            if (mv == null) return;
            float w = Mathf.Clamp(mv.RawHeight * 0.030f, 0.006f, 0.025f);
            mv.SetNeon(neon, w, NeonPxPlant, NeonBoostPlant);
        }

        /// <summary>敌人类型 → 角色模型 id。BOSS 按需求没有模型，返回 null 走图元。</summary>
        private static string EnemyModelId(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Grunt: return "imp";
                case EnemyKind.Shooter: return "imp_archer";
                case EnemyKind.Jumper: return "brute";
                // v76：小猪接上模型。它是中立单位（撞到掉肥料），不是敌人，
                // 所以**不给霓虹红描边**——红边是"敌人"的语言，套在它身上会误导玩家。
                case EnemyKind.Wander: return "pig";
                default: return null;        // Boss：留空
            }
        }

        /// <summary>本帧是否在移动（给走/待机动画切换用）。阈值 5cm/s，避免抖动导致抽搐。</summary>
        private bool Moved(int id, Vector3 pos, float dt)
        {
            bool moved = false;
            Vector3 dir = Vector3.zero;
            if (_lastPos.TryGetValue(id, out Vector3 prev) && dt > 0.0001f)
            {
                Vector3 d = pos - prev;
                float v2 = d.sqrMagnitude / (dt * dt);
                // ★ v180：**速度判据之外再加一道位移下限** ★
                //
                // 只用"速度 = 位移 ÷ dt"时，dt 越小越容易误判：
                // 探针环境下编辑器能跑到 **1~2 万帧/秒**（窗口不渲染，`update` 狂跑），
                // dt ≈ 5e-5s ⇒ 哪怕位置只差 **0.05 毫米**，也算出 1 m/s ⇒ `moved = true` ✗
                // 实测：站定不动打锄头时读数在 Idle 与 Walk 之间来回跳，
                // 下半身偏差从 0.3° 一路抖到 29.5°（= Walk 的腿摆）——
                // 那不是"攻击带动身体"，而是**这个判据在高帧率下失效** ✓
                // 加一条"这一帧真的挪了 ≥5 毫米"的硬下限：60 帧下相当于 0.3 m/s，
                // 比最慢的中立单位（小猪 2.0 m/s）低一个数量级 ⇒ **不会把真的在走判成静止** ✓
                moved = d.sqrMagnitude > 0.000025f && v2 > 0.0025f;
                // 顺手记下**移动方向**。必须在这里记，因为下一行就会覆写 _lastPos——
                // 在外面读 _lastPos 差分会永远得到 0（这个坑我踩过一次）。
                if (moved) dir = d;
            }
            _lastPos[id] = pos;
            _lastDir[id] = dir;
            return moved;
        }

        /// <summary>上一帧的移动方向（未移动时为 0）。由 Moved() 维护。</summary>
        private readonly Dictionary<int, Vector3> _lastDir = new Dictionary<int, Vector3>();

        /// <summary>从 from 看向 to 的偏航角（度）。配合 ModelView.SetFacing 使用——
        /// 不要直接构造 Quaternion 赋给 transform.rotation，那会抹掉模型必需的 Z-up→Y-up 转换。</summary>
        private static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.sqrMagnitude > 0.0004f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : 0f;
        }

        private Transform _root;
        private GameObject[] _plotTiles;
        private GameObject[] _plotSoils;
        /// <summary>v12：树用立方体占位（生长中显示）。</summary>
        private GameObject[] _plotTrees;
        /// <summary>v68：耕地上的作物——成熟形态 / 幼苗形态（各一块模型）。</summary>
        private ModelView[] _plotCrops;
        private ModelView[] _plotYoungs;
        /// <summary>v103：「成长中」阶段（旧版非拟人化造型，`&lt;id&gt;_grown`）。
        /// ★ v166：**三阶段**（不再是四阶段）★
        ///     幼苗(&lt;55%) → 成长中(55%~100%，用 `_grown` 造型) → 成体(长熟，用完整造型) ✓
        /// 原注释里的第 4 阶段「**植物帮手（收割后）**」已删除 ✗
        /// —— 收割不再产生场上植物（v165 删实现 / v166 删掉各处残留），
        ///    所以"地里作物 → 场上帮手"这条外形切换**不存在了** ✓
        /// 之前只有两档，于是 55% 处会从"小芽"**直接跳成"会走路的小人"**，
        /// 中间少了"长出了什么"这一步。</summary>
        private ModelView[] _plotGrowns;
        /// <summary>v106：品质霓虹色对照表只打一次。</summary>
        private static bool _gradeDumpDone;
        private float[] _plotGrowPhase;
        /// <summary>每格当前长的是什么种子——换品种时要重建模型。</summary>
        private string[] _plotSeedIds;

        /// <summary>
        /// ★ v182：**成熟 / 缺水的常驻箭头**（每格各一个）★
        ///
        /// ★ 需求（用户原话）★
        ///   "对已经成熟的植物做一个常驻标记，对缺水的植物也做常驻标记
        ///    （非弹字，在对象旁边分别用对应弹字的颜色指一个箭头）"
        ///   ⇒ 四条：① 成熟常驻 ② 缺水常驻 ③ **不是弹字** ④ 颜色 = 各自弹字的颜色 ✓
        ///
        /// ★ 为什么必须另做一套，不能复用弹字 ✗ ★
        ///   `RunSession.Popups` 是**一次性**的：`World.Simulate` 只在"刚成熟 / 刚断水"的
        ///   **那一帧**发一条（`Actors.cs:1407` / `:1416`），1.15 秒后就被 `TickPopups` 删掉 ✓
        ///   玩家错过那一眼，这一株就**再也没有任何提示**，只剩他自己去记哪块地熟了 ✗
        ///   需求要的是"条件成立就一直指在那儿"，所以判据必须**每帧重新算**，而不是等事件 ✓
        ///
        /// ★ 为什么是 `GameObject[]` + `SetActive`，而不是每帧建/删 ★
        ///   耕地格数上限 36（`farm.maxRows` × 列数），一株两个箭头 ⇒ 最多 72 个 ✓
        ///   全在 `EnsurePlotTiles` 里一次建好、一直隐藏，逐帧只做 `SetActive` 与 `position` ✓
        ///   网格与材质**全局各一份**（`StatusArrowMesh` / `VfxMatCached`）⇒
        ///   一次 Rebuild 之后**运行期 0 次 `new Mesh` / `new Material`** ✓（见两个方法的注释）
        ///
        /// ⚠️ 索引与 `_plotTiles` 一一对应：`EnsurePlotTiles` 重建数组时按索引搬旧的 ✓
        /// </summary>
        private GameObject[] _matureArrows;
        private GameObject[] _thirstArrows;

        /// <summary>
        /// 诊断用：某一格的三档作物模型（幼苗 / 成长中 / 成体）。
        /// 越界或没建出来返回 null —— 不抛异常，因为调用者只用它来**报告**而不是驱动表现 ✓
        ///
        /// 为什么需要它：`_plotCrops` 这一族是 private，而"作物被摆到田外"这类问题
        /// 只有拿得到**这两个数**才能定性：`RawHeight`（归一后的实测高度）与 `localScale`
        /// （= `BaseScale × 系数`）✓ 少了它们只能靠推断 ✓
        /// </summary>
        public ModelView CropStageYoung(int i) => At(_plotYoungs, i);
        public ModelView CropStageGrown(int i) => At(_plotGrowns, i);
        public ModelView CropStageMature(int i) => At(_plotCrops, i);

        private static ModelView At(ModelView[] arr, int i)
            => arr != null && i >= 0 && i < arr.Length ? arr[i] : null;
        private GameObject _player;
        private GameObject _aimTip;
        private GameObject _reticle;
        private Material _reticleMat;
        private Material _playerMat;
        private Shader _shader;
        private Shader _vfxShader;

        public void Build(RunSession s)
        {
            Clear();
            _root = new GameObject("SeedfallWorld").transform;

            _shader = Shader.Find("Universal Render Pipeline/Lit");
            if (_shader == null) _shader = Shader.Find("Standard");

            BuildArena(s);
            BuildScenery(s);          // v64：场地外圈的装饰（树/灌木/石/草/水塘/风车）
            EnsurePlotTiles(s);
            BuildPlayer(s);
            BuildBlastReticle();
            // ★ v191·fx3 / v196·fx4：三套表现的**池**（灰尘 24 / 溅射片 40 / 血渍 8 / 回血 4 段）★
            //   刻意在 `Build` 里一次建好：运行期只 `SetActive`，不再 `new GameObject` ✓
            EnsureFx3Pools();

            Bus.Subscribe<JumpTelegraphEvent>(OnTelegraph);
            Bus.Subscribe<SpawnWarningEvent>(OnSpawnWarning);   // v20：敌人出生前的原地预警标记
            Bus.Subscribe<EnemyAttackFieldEvent>(OnField);
            Bus.Subscribe<BossEvent>(OnBoss);
            // ★ v201：新 BOSS 技能的表现（9 条，全部由 `Enemies.cs` 单向发出）★
            Bus.Subscribe<BossRoarEvent>(OnBossRoar);
            Bus.Subscribe<BossBlastChargeEvent>(OnBossBlastCharge);
            Bus.Subscribe<BossBlastEvent>(OnBossBlast);
            Bus.Subscribe<MothBarrageEvent>(OnMothBarrage);
            Bus.Subscribe<MothDashEvent>(OnMothDash);
            Bus.Subscribe<MothLaserEvent>(OnMothLaser);
            Bus.Subscribe<MothVanishEvent>(OnMothVanish);
            Bus.Subscribe<MothSlamEvent>(OnMothSlam);
            Bus.Subscribe<MothFormEvent>(OnMothForm);
            Bus.Subscribe<MeleeSwingEvent>(OnMeleeSwing);
            Bus.Subscribe<BeamEvent>(OnBeam);              // v38：水壶激光
            Bus.Subscribe<SwordWaveEvent>(OnSwordWave);    // v185：会飞的弯月剑气
            Bus.Subscribe<FertilizerBlastEvent>(OnFertBlast);   // v38：肥料爆炸
            Bus.Subscribe<FertilizerTelegraphEvent>(OnFertTelegraph);   // v91：爆炸前摇的区域标记
            Bus.Subscribe<TreeMatureEvent>(OnPlotMature);  // v38：成熟提示
            Bus.Subscribe<EnemyKilledEvent>(OnKilledShake);
            // ★ 需求④：自充的水球（起飞 / 命中两段，与剑气同一套 `_fxMeshes` 通道）★
            Bus.Subscribe<WaterBallEvent>(OnWaterBall);
            Bus.Subscribe<WaterBallHitEvent>(OnWaterBallHit);
            // ★ v191·fx3：两件新表现的事件源 ★
            //   ① 每分钟整点回血 ⇒ 全屏回血特效（`RunSession.ResetPlayerForNewWave` 里发）
            //   ③ 敌人死亡     ⇒ 原地绿色痕迹（`Combat.cs` 既有的 `EnemyKilledEvent`，
            //      与上面 `OnKilledShake` **同一事件的两个订阅者** —— `Bus` 是委托相加，不覆盖 ✓）
            Bus.Subscribe<MinuteHealEvent>(OnMinuteHeal);
            Bus.Subscribe<EnemyKilledEvent>(OnEnemyKilledSplat);
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ 用户需求④：**水球特效**（`can_autocharge` 每次耗水发射的 2 颗/层水球）★
        //
        // ── 这一段只做表现，不参与判定 ──
        // 打谁、打多少、有没有打中，全在 `RunSession.FireWaterBalls / TickWaterBalls` 里算完 ✓
        // 这里只拿两件事：**一次起飞**（`WaterBallEvent`）与**一次命中**（`WaterBallHitEvent`）。
        //
        // ── 为什么"飞行"由表现层自己推，而不是每帧问逻辑层位置 ──
        // 与 v185 弯月剑气同一条纪律：逻辑层给的是**速度与距离**，表现层用
        // `Vel = Dir × Dist / life` 反算 ⇒ 抓图慢放（`DebugFxHold`）把 `life` 拉长时，
        // 它**仍然正好飞 `Dist`** ⇒ "画到的落点 = 判定打到的落点" ✓
        // 若按真实速度给 `Vel`，慢放下球会提前飞过判定点，抓到的图就是假的 ✗
        //
        // ── 观感三件 ──
        //   ① 本体：水绿偏白的实体球 + 两枚缩小的残影（同一份材质缓存，**只靠缩放**，不改 alpha）
        //   ② 起飞：一记 `IlluminateRamp` 亮光（与挥击弧/剑气共用那套写法）
        //   ③ 命中：一圈**溅射环**（半径随伤害缩放）+ 几颗水花（`SpawnSparks`）
        //
        // ★ 颜色不写死：走 `PluginFx.Or(e.Fx, WeaponSlot.Can)` ★
        // 装了自充/Rim 水绿、装了多柱会带环、装了加粗会更大 ⇒ "装了什么插件"在画面上读得出来 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>水球半径（米）。判定半径是 `RunSession.WaterBallRadius = 0.32`，这里取同量级 ⇒ 看得见的大小与打得中的大小一致。</summary>
        private const float WaterBallScale = 0.30f;

        /// <summary>水球的飞行高度（与剑气 0.55 同一层，读起来是"贴着地面飞过去"）。</summary>
        private const float WaterBallHeight = 0.62f;

        /// <summary>一颗水球起飞：本体 + 2 枚残影 + 一记亮光。</summary>
        private void OnWaterBall(WaterBallEvent e)
        {
            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Can);
            float dist = Mathf.Max(0.05f, e.Dist);
            float speed = Mathf.Max(0.5f, e.Speed);
            // 寿命 = 距离 / 速度（**派生**），慢放时同比例拉长；速度跟着除 ⇒ 永远是"正好飞完 dist"✓
            float life = Mathf.Max(0.04f, dist / speed) * Mathf.Max(1f, DebugFxHold);
            float travel = dist / Mathf.Max(0.01f, life);

            Vector2 dir2 = e.Dir.sqrMagnitude > 1e-4f ? e.Dir.normalized : Vector2.up;
            Vector3 dir3 = new Vector3(dir2.x, 0f, dir2.y);
            Vector3 pos = new Vector3(e.From.x, WaterBallHeight, e.From.y);

            // ① 本体：往白里提一点（水球读起来是"发亮的水"，不是一坨实心色块）
            Color core = Color.Lerp(fx.Core, Color.white, 0.30f);
            GameObject go = Prim(PrimitiveType.Sphere, "WaterBall", pos,
                                 Vector3.one * WaterBallScale, VfxMatCached(core, fx.Rim));
            _fxMeshes.Add(new MeshBit
            {
                Go = go,
                Vel = dir3 * travel,
                Gravity = 0f,
                T = 0f,
                Total = life,
                S0 = 1f,
                S1 = 0.55f          // 收尾只靠缩小淡出（共用材质**永不逐实例改写 alpha**）
            });

            // ① 残影：两枚更小更暗的球跟在后面 —— 读起来是"一串水珠飞过去"而不是"一颗弹珠"
            for (int t = 1; t <= 2; t++)
            {
                GameObject echo = Prim(PrimitiveType.Sphere, "WaterBallTrail",
                                       pos - dir3 * (0.26f * t),
                                       Vector3.one * (WaterBallScale * (1f - 0.20f * t)),
                                       VfxMatCached(fx.Rim, fx.Core));
                _fxMeshes.Add(new MeshBit
                {
                    Go = echo,
                    Vel = dir3 * travel,
                    Gravity = 0f,
                    T = -0.015f * t,     // 错开起跳 ⇒ 残影跟在后面（不是同时出现）
                    Total = life,
                    S0 = 1f,
                    S1 = 0.5f
                });
            }

            // ② 起飞亮光
            IlluminateRamp(pos, Color.Lerp(fx.Core, fx.Rim, 0.35f),
                           Mathf.Max(0.9f, 1.1f * fx.Scale), 1.4f * fx.Glow, 0.12f, 3);
        }

        /// <summary>水球命中：溅射环（半径随伤害）+ 水花。</summary>
        private void OnWaterBallHit(WaterBallHitEvent e)
        {
            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Can);
            Vector3 pos = new Vector3(e.Pos.x, WaterBallHeight, e.Pos.y);
            // 半径随伤害放大、但有上下限 ⇒ 大伤害读得出"更重的一下"，小伤害也不会缩成一点
            float r = Mathf.Clamp(0.55f + Mathf.Max(0f, e.Damage) * 0.20f, 0.55f, 2.4f) * fx.Scale;
            AddRingMarker(e.Pos, r, FxLife(0.22f), new Color(fx.Core.r, fx.Core.g, fx.Core.b, 1f));
            SpawnSparks(pos, fx, 3 + fx.Sparks, r * 1.1f, 2.6f);
        }

        public void Clear()
        {
            Bus.Unsubscribe<JumpTelegraphEvent>(OnTelegraph);
            Bus.Unsubscribe<SpawnWarningEvent>(OnSpawnWarning);
            Bus.Unsubscribe<EnemyAttackFieldEvent>(OnField);
            Bus.Unsubscribe<BossEvent>(OnBoss);
            // ★ v201：与订阅一一对应（少一条就会在场景重建后重复执行）★
            Bus.Unsubscribe<BossRoarEvent>(OnBossRoar);
            Bus.Unsubscribe<BossBlastChargeEvent>(OnBossBlastCharge);
            Bus.Unsubscribe<BossBlastEvent>(OnBossBlast);
            Bus.Unsubscribe<MothBarrageEvent>(OnMothBarrage);
            Bus.Unsubscribe<MothDashEvent>(OnMothDash);
            Bus.Unsubscribe<MothLaserEvent>(OnMothLaser);
            Bus.Unsubscribe<MothVanishEvent>(OnMothVanish);
            Bus.Unsubscribe<MothSlamEvent>(OnMothSlam);
            Bus.Unsubscribe<MothFormEvent>(OnMothForm);
            Bus.Unsubscribe<MeleeSwingEvent>(OnMeleeSwing);
            Bus.Unsubscribe<BeamEvent>(OnBeam);
            Bus.Unsubscribe<SwordWaveEvent>(OnSwordWave);
            Bus.Unsubscribe<FertilizerBlastEvent>(OnFertBlast);
            Bus.Unsubscribe<FertilizerTelegraphEvent>(OnFertTelegraph);
            Bus.Unsubscribe<TreeMatureEvent>(OnPlotMature);
            Bus.Unsubscribe<EnemyKilledEvent>(OnKilledShake);
            Bus.Unsubscribe<WaterBallEvent>(OnWaterBall);
            Bus.Unsubscribe<WaterBallHitEvent>(OnWaterBallHit);
            Bus.Unsubscribe<MinuteHealEvent>(OnMinuteHeal);
            Bus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilledSplat);
            // ★ v191·fx3：**池里那份不入任何缓存的材质**必须显式销毁（血渍 1 份 + 回血 4 份）★
            //   物件本身是 `_root` 的子物体，下面的 `Destroy(_root)` 会带走它们 ⇒ 这里只断引用 ✓
            //   （血渍那 8 份网格相反：它们进的是静态 `_fxMeshCache`，下一局直接复用，**不能**销毁）
            ClearFx3Pools();
            // ★ v197：机制植物 / 胡萝卜 / 兔子 / 盾环 / 太阳 / 雨丝的引用一起断（实现在 `WorldView.Prog.cs`）★
            //   物件同样是 `_root` 的子物体 ⇒ 只断引用；材质都在 `MatCached` 缓存里，**不能**销毁 ✓
            ClearProgVisuals();

            _enemies.Clear();
            _projectiles.Clear();
            _pickups.Clear();
            _markers.Clear();
            // v183：血条是 `_root` 的子物体（上面那句 Destroy 会带走它）⇒ 引用必须一起断，
            // 否则下一帧 `_bossBar.Root.SetActive` 会撞 MissingReference ✓
            // 字体与四边形网格是静态缓存，**刻意不清** —— 它们与"这一局"无关 ✓
            _bossBar = null;
            _cam = null;
            _plotTiles = null;
            _plotSoils = null;
            _plotTrees = null;
            _plotCrops = null;
            _plotYoungs = null;
            _plotGrowns = null;
            _plotGrowPhase = null;
            _plotSeedIds = null;
            _matureArrows = null;
            _thirstArrows = null;
            _fenceSegs.Clear();
            _scenery.Clear();
            _sceneryStatic.Clear();
            _ruinWalls.Clear();
            _sceneryTime = 0f;
            for (int i = 0; i < _swings.Count; i++)
                if (_swings[i].Go != null) Object.Destroy(_swings[i].Go);
            _swings.Clear();
            // v176：插件特效的装饰件（火花/冲击环）也要一起收，
            // 且**它们自己持有的材质必须显式销毁** —— 不销毁就是换局之后的一份残留 ✓
            for (int i = 0; i < _fxBits.Count; i++)
            {
                FxBit b = _fxBits[i];
                if (b.Go != null) Object.Destroy(b.Go);
                // 火花共用 `VfxMatCached` 的材质 ⇒ 这里**不能**销毁材质，
                // 否则会把缓存里那份也毁掉，下一局所有火花都变成 Missing ✓
            }
            _fxBits.Clear();
            // ★ v188：持续效果的表现件（毒区三件套 / 减速箭头 / 逐敌人火星节拍）★
            // 毒区与箭头都是 `_root` 的子物体，`_root` 下面会被销毁 ⇒ 引用必须一起断，
            // 否则下一局读 `a.Go.activeSelf` 会撞 MissingReference（与 `_bossBar` 同一个坑）✓
            ClearDotFx();
            for (int i = 0; i < _blasts.Count; i++)
            {
                BlastFx b = _blasts[i];
                if (b.Go != null) Object.Destroy(b.Go);
                if (b.Mat != null) Object.Destroy(b.Mat);
            }
            _blasts.Clear();
            for (int i = 0; i < BeamFxSlots; i++) _beamFxCd[i] = 0f;
            _player = null;
            _aimTip = null;
            _reticle = null;
            _reticleMat = null;
            // v172：武器引用与"已显示索引"必须一起清 ✗
            // `_tools` 是 `_root` 的子物体（挂在手骨下），`_root` 已在上面的 Destroy 里被销毁；
            // 不清的话这里留下的是**已销毁的引用**，下一局的 SetActive 会撞 MissingReference ✓
            _tools = null;
            _shownWeapon = -1;
            _pendingAttack = -1;

            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
                _root = null;
            }
            else
            {
                // 兼容场景里遗留的对象
                GameObject stale = GameObject.Find("SeedfallWorld");
                if (stale != null) Object.Destroy(stale);
            }
        }

        private Material Mat(Color c, bool emissive = false)
        {
            var m = new Material(_shader);
            m.color = c;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
            if (emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 0.6f);
            }
            return m;
        }

        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");

        /// <summary>
        /// v70：战斗特效材质——永远画在最上层（Queue=Overlay + ZTest Always + ZWrite Off）。
        /// 用在子弹 / 激光 / 挥击弧 / 爆炸环上。rim 是霓虹边缘色（敌人子弹红、我方青）。
        /// </summary>
        private Material VfxMat(Color baseColor, Color rim)
        {
            return VfxMat(baseColor, rim, VfxCoreBoost);
        }

        /// <summary>
        /// v176：**可以单独调中心提亮**的版本。
        ///
        /// ★ 为什么需要这个参数（一个真实的观感缺陷）★
        /// `SeedfallVfx.shader` 的 `_CoreBoost` 默认 **1.35**：
        /// `col = _BaseColor.rgb * _CoreBoost` —— 于是任何**已经很亮的颜色都会被推到 1.0 之后截掉** ✓
        /// 对弹丸/爆炸这种"越亮越好"的东西没问题；
        /// 但**挥击弧是一整片贴着地面的面板**，它 1.35 之后整片饱和成黄白，
        /// 插件给它的色相（冰蓝 / 炭红）就被压没了 ⇒ "装了插件看不出区别" ✗
        /// 弧这类"大面积、要读颜色"的特效用 1.0 保留色相，弹丸/爆炸继续用 1.35 保留亮度 ✓
        /// </summary>
        private Material VfxMat(Color baseColor, Color rim, float coreBoost)
        {
            if (_vfxShader == null) _vfxShader = Shader.Find("Seedfall/Vfx");
            if (_vfxShader == null) return Mat(baseColor, true);      // 兜底：找不到就用普通材质
            var m = new Material(_vfxShader);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, baseColor);
            else if (m.HasProperty(ColorId)) m.SetColor(ColorId, baseColor);
            m.SetColor(RimColorId, rim);
            m.SetFloat(RimPowerId, 2.2f);
            if (m.HasProperty(CoreBoostId)) m.SetFloat(CoreBoostId, coreBoost);
            return m;
        }

        /// <summary>弹丸 / 爆炸 / 激光的默认中心提亮（沿用着色器原值）。</summary>
        private const float VfxCoreBoost = 1.35f;
        /// <summary>挥击弧这类**大面积、要读色相**的特效用 1.0，避免亮部截断吃掉颜色。</summary>
        public const float VfxCoreBoostFlat = 1.0f;
        private static readonly int CoreBoostId = Shader.PropertyToID("_CoreBoost");

        /// <summary>
        /// v176：`VfxMat` 的**缓存版** —— 给"每帧都可能新建一个"的特效用（水壶是持续激光，约 60 根/秒）。
        ///
        /// ★ 这是一个真实的内存缺陷，不是洁癖 ★
        /// `VfxMat` 每次调用都 `new Material(...)`，而它的宿主对象是靠
        /// `Object.Destroy(go, 0.12f)` 销毁的 —— **GameObject 没了，材质不回收** ✗
        /// 材质是原生对象、不归 GC 管 ⇒ 激光按住不放就是 60 份/秒的净增长，
        /// 跑十分钟约三万份。旧的 `Mat()` 走同一条路，只是激光那一路用的是它 ✓
        ///
        /// ★ 能进这个缓存的前提：**这类材质永远不会被逐实例改写** ★
        /// 激光 / 挥击弧的颜色自始至终是常量 ⇒ 共享安全 ✓
        /// 会淡出的（爆炸环 / 爆炸核心 / 冲击环）**绝不能**进这里 ——
        /// 它们必须各自持有一份自己的材质（见 `BlastFx.Mat`），否则一个淡出会把同色的一起淡掉 ✗
        /// </summary>
        private static readonly Dictionary<int, Material> VfxMatCache = new Dictionary<int, Material>();

        private Material VfxMatCached(Color core, Color rim) => VfxMatCached(core, rim, VfxCoreBoost);

        private Material VfxMatCached(Color core, Color rim, float coreBoost)
        {
            int key = ((QuantRgb(core) * 397) ^ QuantRgb(rim)) * 31
                    + Mathf.RoundToInt(coreBoost * 100f);
            if (VfxMatCache.TryGetValue(key, out Material cached) && cached != null) return cached;
            Material m = VfxMat(core, rim, coreBoost);
            VfxMatCache[key] = m;
            return m;
        }

        /// <summary>颜色 → 24 位整数（只用于缓存键）。</summary>
        private static int QuantRgb(Color c)
            => (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16)
             | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8)
             |  Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);

        /// <summary>
        /// v176：`Mat` 的缓存版 —— 给"颜色恒定、但会不断新建"的物件用（拾取物 / 刷怪预警）。
        ///
        /// ★ 判据是**调用频率**，不是"看到 new Material 就改" ★
        /// `Mat()` 建场时用一次无所谓（地面、墙体、玩家）。但拾取物是**持续掉落**的、
        /// 刷怪预警是**每波不断**的 —— 每一个都 `new Material` 且随 `Object.Destroy(go)` 一起
        /// 丢掉 Gameobject 而**留下材质** ⇒ 一局下来是稳定增长的原生内存 ✓
        /// 而这些物件的颜色**永远不变**（按类型固定）⇒ 共享一份材质完全安全 ✓
        ///
        /// 会逐实例改颜色的（标记环、爆炸环）**不能**走这里，见 `BlastFx.Mat` / `Marker.Mat` ✓
        /// </summary>
        private static readonly Dictionary<int, Material> MatCache = new Dictionary<int, Material>();

        private Material MatCached(Color c, bool emissive = false)
        {
            int key = (QuantRgb(c) * 397) ^ (emissive ? 1 : 0);
            if (MatCache.TryGetValue(key, out Material cached) && cached != null) return cached;
            Material m = Mat(c, emissive);
            MatCache[key] = m;
            return m;
        }

        /// <summary>
        /// v176：给 `Seedfall/Vfx` 的材质**正确**设置颜色。
        ///
        /// ★ 为什么不能用 `Material.color` ★
        /// `Material.color` 读写的是 **`_Color`**，而 `Seedfall/Vfx` 的属性叫
        /// **`_BaseColor`**（`[MainColor]`）—— `Assets/Shaders/SeedfallVfx.shader:18` ✓
        /// 写错属性的后果不是报错停下，而是**静默无效**：
        /// 旧的 `TickBlasts` 每帧 `rend.material.color = c` 想淡出爆炸环，
        /// 实际上一帧都没淡出过 —— 环是**硬消失**的 ✓
        /// 所以这里统一走 `_BaseColor` / `_RimColor` 两个 ID ✓
        /// </summary>
        private static void SetVfxColor(Material m, Color core, Color rim)
        {
            if (m == null) return;
            m.SetColor(BaseColorId, core);
            m.SetColor(RimColorId, rim);
        }

        /// <summary>
        /// v70：贴图材质。用于地面与墙体。
        /// tiling 必须给得够大，否则一张 256 的图铺在 40m 的地面上会糊成一片。
        /// </summary>
        private Material MatTextured(string texName, Color tint, float tiling)
        {
            var m = new Material(_shader);
            Texture2D tex = Resources.Load<Texture2D>("Art/Textures/" + texName);
            if (tex != null && m.HasProperty(BaseMapId))
            {
                m.SetTexture(BaseMapId, tex);
                m.SetTextureScale(BaseMapId, new Vector2(tiling, tiling));
                m.EnableKeyword("_NORMALMAP");     // 让 URP 走带贴图的变体
            }
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, tint);
            m.mainTexture = tex;
            m.mainTextureScale = new Vector2(tiling, tiling);
            return m;
        }

        private GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Transform parent = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent == null ? _root : parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private void BuildArena(RunSession s)
        {
            float half = s.T.farm.arenaHalf;
            // v70：地面与墙体改用贴图。tiling 按"每米几张贴图"给——
            // 地面 40m 铺 24 张（约 1.7m 一张），墙体窄长所以按长度折算。
            //
            // ★★ v214：战斗地面换成**主城同一张卡通草贴图**（用户原话：
            //    "战斗场景的草贴图也换掉，要卡通的风格，带点小草纹理，像主城一样的"）★★
            //
            // ── 走的是"单建"而不是 `ToonLook` 的池，为什么 ──
            // 战斗地面在这里就**已经是单建的**（`MatTextured` 里 `new Material(_shader)`）：
            // 它走的是 **URP/Lit** 而不是 `Seedfall/Toon` 系，因为它要当"被踩的地表"，
            // 需要 URP 的阴影接收/自发光/SRP Batcher 那套。全工程只有 1 块地面 + 几段墙，
            // 单建 2~5 份材质不构成"每实例 new Material"（那条禁令针对的是几百个装饰）✓
            // ⇒ 所以**不**去改 `ToonLook` 的默认贴图：那会影响几百个装饰的观感，
            //   为了换一块地面而动全局是错的 ✓ 这里只改**这一处**的贴图名与 tiling。
            //
            // ── 两个参数为什么这么给 ──
            // ① 贴图名 `grass` → **`toon_grass`**：与主城、草地、装饰完全同一张（64×64 卡通、
            //    竖向草叶、已归一到平均 0.92）⇒ 观感统一 ✓
            // ② tint = 主城草色 (0.46,0.62,0.34)：`toon_grass` 是**乘算贴图**（平均 0.92，
            //    接近白），直接当 Lit 的 albedo 会偏白 ⇒ 必须给底色把它压成草绿 ✓
            // ③ tiling `half*0.6` → **`half*4`**：Unity 的 Plane 是 10×10、缩放 `half*0.6`，
            //    所以边长 = `half*6` 米。旧 tiling 只有 `half*0.6` ⇒ **每张图铺 10 米**，
            //    64 像素摊到 10 米上 = 1 texel 15 厘米，屏幕上一片纯绿、一根草叶都看不见 ✗
            //    改成 `half*4` ⇒ 每张约 1.5 米 ⇒ 草叶在玩法机位下约 2~3 像素，读得出纹理 ✓
            Material ground = MatTextured("toon_grass", new Color(0.46f, 0.62f, 0.34f), half * 4f);
            // v75：地面从"刚好铺满场地"改成**延伸到场地外**。
            // 旧版 plane 的缩放是 half*0.2（Unity 平面是 10×10 → 边长正好 2*half），
            // 于是围墙之外是悬空的（相机一拉远就看到地板边缘）。现在铺 3 倍，
            // 配合外面的树林，视野拉出去时看到的是"这片农场的周边"而不是"世界的尽头"。
            GameObject plane = Prim(PrimitiveType.Plane, "Ground", new Vector3(0, 0, 0),
                new Vector3(half * 0.6f, 1f, half * 0.6f), ground);
            plane.isStatic = true;

            // v76：边界墙改用 Blender 做的**烂红砖墙**模型段（高低不平 + 塌口 + 落砖），
            // 替掉原来的四个整块 Cube——一整条平滑的方块墙读不出"这是个废弃农场"。
            // 段长 2.00m（模型实测沿局部 X 严格 ±1.000），和 fence 的 yaw 约定一致，
            // 所以直接复用 FenceRun 那套"摆在段中点 + yaw"。
            // 模型局部 -Y 侧有落砖（多出 0.37m），**让 -Y 朝场外**，否则碎砖会啃进场地。
            BuildRuinWall(half);
        }

        private static readonly string[] RuinVariants = { "wall_ruin", "wall_ruin_b", "wall_ruin_c" };

        /// <summary>
        /// 沿场地四边铺一圈烂砖墙。三个变体交替，避免一眼看出重复。
        /// 起点/终点各留半段，四角自然交叠。
        /// </summary>
        private void BuildRuinWall(float half)
        {
            const float Seg = 2.0f;
            int perSide = Mathf.CeilToInt(half * 2f / Seg);
            int idx = 0;
            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i < perSide; i++)
                {
                    float u = -half + Seg * (i + 0.5f);
                    Vector3 pos;
                    float yaw;
                    switch (side)
                    {
                        case 0: pos = new Vector3(u, 0f, half); yaw = 0f; break;      // 北：-Y 朝 +Z（场外）
                        case 1: pos = new Vector3(u, 0f, -half); yaw = 180f; break;   // 南
                        case 2: pos = new Vector3(half, 0f, u); yaw = 90f; break;     // 东
                        default: pos = new Vector3(-half, 0f, u); yaw = 270f; break;  // 西
                    }
                    ModelView mv = ModelView.Spawn("Environment", RuinVariants[idx % RuinVariants.Length],
                        _root, false, true);
                    if (mv == null) continue;
                    mv.Go.name = "RuinWall";
                    mv.SetFacing(yaw);
                    mv.SetScaleFactor(1f);
                    mv.Go.transform.position = pos;
                    _ruinWalls.Add(mv.Go);
                    idx++;
                }
            }
        }

        private readonly List<GameObject> _ruinWalls = new List<GameObject>();

        /// <summary>v6：耕地按需生成——只给"已获得"的地块建模型，新地块加入时补建并闪一下。</summary>
        private void EnsurePlotTiles(RunSession s)
        {
            FarmGrid grid = s.World.Farm;
            int want = grid.Slots.Length;
            if (_plotTiles != null && _plotTiles.Length == want) return;

            var tiles = new GameObject[want];
            var soils = new GameObject[want];
            var trees = new GameObject[want];
            var crops = new ModelView[want];
            var youngs = new ModelView[want];
            var grown = new ModelView[want];
            var phases = new float[want];
            var seedIds = new string[want];
            // v182：每格两个常驻箭头（成熟 / 缺水）。与 _plotTiles 同索引、同生命周期。
            var matureArrows = new GameObject[want];
            var thirstArrows = new GameObject[want];
            for (int i = 0; i < want; i++)
            {
                if (_plotTiles != null && i < _plotTiles.Length)
                {
                    tiles[i] = _plotTiles[i];
                    soils[i] = _plotSoils[i];
                    trees[i] = _plotTrees != null && i < _plotTrees.Length ? _plotTrees[i] : null;
                    crops[i] = _plotCrops != null && i < _plotCrops.Length ? _plotCrops[i] : null;
                    youngs[i] = _plotYoungs != null && i < _plotYoungs.Length ? _plotYoungs[i] : null;
                    grown[i] = _plotGrowns != null && i < _plotGrowns.Length ? _plotGrowns[i] : null;
                    phases[i] = _plotGrowPhase != null && i < _plotGrowPhase.Length ? _plotGrowPhase[i] : 0f;
                    seedIds[i] = _plotSeedIds != null && i < _plotSeedIds.Length ? _plotSeedIds[i] : null;
                    // v182：已有格的箭头原样搬过来（**不重建**）——扩建耕地不该让旧格的箭头闪一下
                    matureArrows[i] = _matureArrows != null && i < _matureArrows.Length ? _matureArrows[i] : null;
                    thirstArrows[i] = _thirstArrows != null && i < _thirstArrows.Length ? _thirstArrows[i] : null;
                    // 兜底：若旧数组缺失（理论上不会），补建
                    if (matureArrows[i] == null) matureArrows[i] = MakeStatusArrow(PopupKind.PlantMature, $"StatusArrow_Mature_{i}", grid.TileSize);
                    if (thirstArrows[i] == null) thirstArrows[i] = MakeStatusArrow(PopupKind.PlantThirst, $"StatusArrow_Thirst_{i}", grid.TileSize);
                    continue;
                }
                FarmPlot plot = grid.Slots[i];
                Vector3 p = new Vector3(plot.World.x, 0.0f, plot.World.y);
                // v59：耕地改用美术模型（木框 + 松土 + 垄）。
                // 模型边长 1.69m 而 FarmGrid.TileSize = 1.6m，所以按 TileSize/1.6 缩放对齐格距；
                // 缩放系数写在这里而不是改模型——格距是玩法参数，模型是表现。
                ModelView plotModel = ModelView.Spawn("Environment", "farm_plot", _root, false);
                if (plotModel != null)
                {
                    tiles[i] = plotModel.Go;
                    // 模型边长 1.69m，缩放到与 FarmGrid 的格距一致（乘在原始缩放上）。
                    // 这个 1.69 必须跟 gen_environment.PLOT 的实际产出对齐——写 1.6 会让耕地比格距大 5%。
                    plotModel.SetScaleFactor(grid.TileSize / 1.69f);
                    // ★ 耕地也必须按几何中心对齐 ★
                    // farm_plot 的网格中心是 (0, -0.8, 0)（Blender 坐标），经 270° 轴向转换后
                    // 在世界里是 **(0, 0, 0.8)** —— 模型比枢轴偏了 0.8 米。
                    // 只设 transform.position 摆的是**枢轴**，于是木框整体歪掉；
                    // 而作物是按逻辑中心摆的、位置是对的，两边一叠加看起来就像"植物没长在耕地中心"。
                    // 实测证据：MeshProbe 报 farm_plot 的网格 center = (0, -0.8, 0)。
                    plotModel.CenterOn(p);
                    soils[i] = null;      // 模型自带松土，不再叠一层
                }
                else
                {
                    tiles[i] = Prim(PrimitiveType.Cube, $"PlotTile_{i}", p + new Vector3(0, 0.04f, 0),
                        new Vector3(grid.TileSize * 0.94f, 0.08f, grid.TileSize * 0.94f), Mat(new Color(0.36f, 0.25f, 0.16f)));
                    soils[i] = Prim(PrimitiveType.Cube, $"PlotSoil_{i}", p + new Vector3(0, 0.07f, 0),
                        new Vector3(grid.TileSize * 0.8f, 0.06f, grid.TileSize * 0.8f), Mat(new Color(0.30f, 0.20f, 0.13f)));
                }
                // v12：树的占位模型（立方体），生长中显示
                trees[i] = Prim(PrimitiveType.Cube, $"PlotTree_{i}", p + new Vector3(0f, 0.75f, 0f),
                    new Vector3(grid.TileSize * 0.5f, 1.5f, grid.TileSize * 0.5f), Mat(new Color(0.28f, 0.42f, 0.20f)));
                trees[i].SetActive(false);
                AddMarker(plot.World, 1.5f, 0.7f, new Color(0.55f, 1f, 0.6f, 1f));   // 新耕地提示
                // v182：两个常驻箭头（建好即隐藏，判据成立时由 SyncStatusArrows 开）
                matureArrows[i] = MakeStatusArrow(PopupKind.PlantMature, $"StatusArrow_Mature_{i}", grid.TileSize);
                thirstArrows[i] = MakeStatusArrow(PopupKind.PlantThirst, $"StatusArrow_Thirst_{i}", grid.TileSize);
            }
            _plotTiles = tiles;
            _plotSoils = soils;
            _plotTrees = trees;
            _plotCrops = crops;
            _plotYoungs = youngs;
            _plotGrowns = grown;
            _plotGrowPhase = phases;
            _plotSeedIds = seedIds;
            _matureArrows = matureArrows;
            _thirstArrows = thirstArrows;
            UpdateFence(s);
        }

        /// <summary>
        /// v64：场景装饰。
        ///
        /// 三条纪律：
        ///   ① 只摆在**场地外圈**——中心是玩法区，装饰不参与碰撞但会挡视线、干扰读图
        ///   ② 用独立的固定种子 Pcg32，不碰 World.Rng / World.VisualRng——装饰不得扰动玩法随机序列
        ///   ③ 全部挂在 _root 下，Clear() 一并清掉，不存在跨局残留
        /// </summary>
        private void BuildScenery(RunSession s)
        {
            float half = s.T.farm.arenaHalf;
            var rng = new Pcg32(20261015UL, 7UL);

            // ① 外圈树林与灌木：沿周长均匀撒点，半径带在墙内侧 1.0~2.6m
            //    高个子只放外圈——中场放树会挡住敌人，读不了战场
            int ring = 46;            for (int i = 0; i < ring; i++)
            {
                float a = (i / (float)ring) * Mathf.PI * 2f + rng.Range(-0.03f, 0.03f);
                float r = half - rng.Range(1.0f, 2.6f);
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                string id = rng.Chance(0.34f) ? "tree" : (rng.Chance(0.5f) ? "bush" : "rock_a");
                float yaw = rng.Range(0f, 360f);
                float sway = id == "tree" ? 1.0f : (id == "bush" ? 0.6f : 0f);
                SpawnScenery(id, p, yaw, sway, rng.Range(0f, 6.28f));
            }

            // ② 草丛：铺满**耕地之外**的场地（这是地面的"底料"，密度最高）
            //    注意：密度是**可玩性**问题，不是美术问题——铺太满，敌人和足迹就读不出来了。
            //    实测 320 丛时整屏都是草，压到 170。
            //    v174：下界 0 → FarmlandEdge()。草与下面三段一样会被读成"长在田里的植物"✗
            //          （用户原话："有些植物存在异常，没有种植在耕地上"）。田里应当是空地：
            //          作物才是玩家要读的目标，杂草只会干扰读图 ✓
            int tufts = 170;
            for (int i = 0; i < tufts; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = rng.Range(FarmlandEdge(), half - 0.8f);
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                SpawnScenery("grass", p, rng.Range(0f, 360f), 1.0f, rng.Range(0f, 6.28f));
            }

            // ③ 碎石：**大幅减量**。石头在俯视图里是最大的地面对比物，
            //    铺多了像满地鹅卵石滩，把角色淹没掉。14 块点缀足够。
            //    v174：下界 0 → FarmlandEdge()——碎石不再铺进田里（与 ② 同一条纪律）
            for (int i = 0; i < 14; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = rng.Range(FarmlandEdge(), half - 1.0f);
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                SpawnScenery(rng.Chance(0.5f) ? "rock_b" : "rock_c", p, rng.Range(0f, 360f), 0f, 0f);
            }

            // ④ 地标：风车小屋固定在西北角外，水塘在东南 —— 固定位置才有"这是个地方"的感觉
            SpawnScenery("windmill", new Vector3(-half + 3.2f, 0f, half - 3.4f), 135f, 0f, 0f);
            SpawnScenery("pond", new Vector3(half - 3.6f, 0f, -half + 3.8f), 0f, 0f, 0f);
            SpawnScenery("pond", new Vector3(half - 4.2f, 0f, half - 4.6f), 0f, 0f, 0f);

            // ⑤ v64 新增装饰：花 / 蘑菇 / 芦苇 / 树桩 / 倒木 / 木桶
            //    花草量大、单体小，负责把地面的"空"填掉；树桩木桶少量，负责讲"有人来过"的故事。
            //    ★ v174：下界 0 → FarmlandEdge() ★ —— 花与蘑菇是"**与作物同形**"的小植物，
            //    撒进田里就会读成"没种在耕地上的异常植物"（用户原话）✗
            //    它们连同草/碎石一起改从耕地外沿起算，外圈林带（`Range(rIn, rOut)` 那两段）保持不动 ✓
            for (int i = 0; i < 90; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = rng.Range(FarmlandEdge(), half - 1.0f);
                SpawnScenery(rng.Chance(0.5f) ? "flower_a" : "flower_b",
                    new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                    rng.Range(0f, 360f), 1.0f, rng.Range(0f, 6.28f));
            }
            for (int i = 0; i < 24; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = rng.Range(FarmlandEdge(), half - 1.0f);
                SpawnScenery("mushroom", new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                    rng.Range(0f, 360f), 0f, 0f);
            }
            // 芦苇贴着两处水塘
            for (int i = 0; i < 9; i++)
            {
                SpawnScenery("reeds", new Vector3(half - 3.6f + rng.Range(-2.2f, 2.2f), 0f,
                    -half + 3.8f + rng.Range(-2.2f, 2.2f)), rng.Range(0f, 360f), 1.0f, rng.Range(0f, 6.28f));
            }
            for (int i = 0; i < 5; i++)
            {
                SpawnScenery("reeds", new Vector3(half - 4.2f + rng.Range(-2.0f, 2.0f), 0f,
                    half - 4.6f + rng.Range(-2.0f, 2.0f)), rng.Range(0f, 360f), 1.0f, rng.Range(0f, 6.28f));
            }
            for (int i = 0; i < 7; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = half - rng.Range(2.4f, 7.5f);
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                string id = rng.Chance(0.34f) ? "stump" : (rng.Chance(0.5f) ? "log" : "barrel");
                SpawnScenery(id, p, rng.Range(0f, 360f), 0f, 0f);
            }

            // ⑥ 一圈路灯：等距摆放，给场地划出边界感（比一堵墙读起来舒服）
            int lamps = 12;
            for (int i = 0; i < lamps; i++)
            {
                float a = (i / (float)lamps) * Mathf.PI * 2f;
                float r = half - 0.6f;
                SpawnScenery("lantern", new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), 0f, 0f, 0f);
            }

            // ⑦ v75：围墙**之外**再铺一圈树林。
            // 相机拉远 + 地面延伸之后，墙外如果空着，看到的就是一片无边界的草地；
            // 用树林把视野收住，整个农场才像"坐落在一片林子里"。
            // 分两层错落：内层密一点做过渡，外层稀一点做天际线。
            for (int layer = 0; layer < 2; layer++)
            {
                // v101：再加量（64/90 → 110/150）并外扩（第二层 20 → 30）。
                // 上一轮只做了"贴墙一圈矮植被"，玩家仍觉得外围太空——
                // 因为视野里能看到的**只有离墙最近的那一层**，外圈多远处有多少其实看不见。
                // 所以要同时做两件事：**加密近层**（看得见的）+ **外扩远层**（垫出纵深）。
                int count = layer == 0 ? 110 : 150;
                float rIn = layer == 0 ? half + 1.2f : half + 6.0f;
                float rOut = layer == 0 ? half + 5.0f : half + 30.0f;
                for (int i = 0; i < count; i++)
                {
                    float a = rng.Range(0f, Mathf.PI * 2f);
                    float r = rng.Range(rIn, rOut);
                    Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    // 外层多放树、内层混灌木，视觉上才有前后层次
                    bool tree = layer == 1 || rng.Chance(0.62f);
                    SpawnScenery(tree ? "tree" : "bush", p, rng.Range(0f, 360f),
                        tree ? 1.0f : 0.6f, rng.Range(0f, 6.28f));
                }
            }

            // ⑨ v80：紧贴墙外再补一圈**矮植被与石头**。
            //    上面那两圈是"树林"（高大、成片），负责把视野收住；
            //    但树与墙之间会留出一条光秃秃的草带，看着像没人打理。
            //    这一圈贴着墙根撒花/草/石，把过渡补上——三层高度差才是自然的林地边缘。
            // v101：150 → 240，并把石头与花的比重调高（需求"放置石头和花"）。
            for (int i = 0; i < 240; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = half + rng.Range(0.35f, 3.4f);
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float roll = rng.NextFloat();
                string id = roll < 0.34f ? (rng.Chance(0.5f) ? "flower_a" : "flower_b")
                          : roll < 0.60f ? "grass"
                          : roll < 0.72f ? "bush"
                          : roll < 0.86f ? (rng.Chance(0.5f) ? "rock_a" : "rock_b")
                          : (rng.Chance(0.5f) ? "rock_c" : "stump");
                SpawnScenery(id, p, rng.Range(0f, 360f), id == "grass" || id.StartsWith("flower") ? 1f : 0f,
                    rng.Range(0f, 6.28f));
            }

            // ⑧ v76：农村装饰——讲"这里有人住过"的故事。
            //    位置有讲究：生活类的东西（水井、鸡舍、晾衣绳）放在**墙外**，
            //    田间的东西（稻草人、独轮车、草垛、陶罐）放在**墙内靠边**，
            //    这样场地中央仍然留给玩法，但视野拉远时周边是"活的"。
            string[] outside = { "well", "chicken_coop", "laundry_line", "crate_stack", "birdhouse" };
            for (int i = 0; i < outside.Length; i++)
            {
                float a = (i / (float)outside.Length) * Mathf.PI * 2f + 0.4f;
                float r = half + rng.Range(3.5f, 8.5f);
                SpawnScenery(outside[i], new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                    rng.Range(0f, 360f), 0f, 0f);
            }
            string[] inside = { "haystack", "wheelbarrow", "clay_jars", "scarecrow" };
            for (int i = 0; i < inside.Length; i++)
            {
                float a = (i / (float)inside.Length) * Mathf.PI * 2f + 1.1f;
                float r = half - rng.Range(3.0f, 6.5f);
                SpawnScenery(inside[i], new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                    rng.Range(0f, 360f), 0f, 0f);
            }
            // 园门摆在南侧正对场地中心——它就该是"入口"的位置
            SpawnScenery("garden_gate", new Vector3(0f, 0f, -half), 180f, 0f, 0f);
        }

        /// <summary>
        /// ★ v174：耕地外沿到场地中心的距离 ★ —— 装饰撒点的**半径下界**就取它 ✓
        ///
        /// 为什么需要这个常量：装饰的半径原先一律从 `0` 起算 ✗ ⇒ 花/蘑菇/草/碎石
        /// **撒进了耕地里** ✗ ⇒ 田里站着与作物同形的小植物，看起来就是
        /// "有些植物没有种植在耕地上"（用户原话）✗
        ///
        /// ⚠️ 地头到底在哪，别再用"格子中心"当"外沿"：
        ///   `FarmGrid.WorldOf` 的格心 = `(gx − (Size−1)/2) × TileSize` ⇒ 6 列时格心在 **±6.25** ✓
        ///   但每格自己有 `TileSize`（2.5）宽 ⇒ **整块地要再往外扩半格 1.25** ✓
        ///   ⇒ 耕地外沿 = 6.25 + 1.25 = **7.5** ✓（= `gridSize × tileSize ÷ 2` = 6 × 2.5 ÷ 2 ✓ 两条算法互为验算）
        /// 再留 1.25m 余量（≈ 半格）⇒ 撒点下界 **8.75**，装饰不会贴着地块边缘压线 ✓
        ///
        /// 只给**地面装饰**用。外圈林带（`Range(rIn, rOut)` 那两段）不在此列 ✗
        /// —— 那些是收视野边界的设计，本来就在墙外 ✓
        /// </summary>
        private static float FarmlandEdge()
        {
            FarmDef f = TableDatabase.Tuning.farm;
            float gridHalf = (Mathf.Max(2, f.gridSize) - 1) * 0.5f * f.tileSize;   // 最外列/行的格心
            float edge = gridHalf + f.tileSize * 0.5f;                              // 再加半格厚度 = 外沿
            return edge + f.tileSize * 0.5f;                                        // 留半格余量
        }

        /// <summary>
        /// 装饰的显示放大系数。
        /// 小草小花建模时只有 0.2~0.5m —— 在俯视相机下是几个像素，等于白放。
        /// 这里按"能不能看见"统一放大，而不是按真实比例。
        /// </summary>
        private static float SceneryScale(string id)
        {
            switch (id)
            {
                case "grass": return 1.9f;
                case "flower_a":
                case "flower_b": return 2.1f;
                case "mushroom": return 1.9f;
                case "reeds": return 1.7f;
                case "rock_c": return 1.3f;
                case "rock_b": return 1.05f;
                case "bush": return 1.25f;
                case "stump": return 1.3f;
                // v80：树与房子放大。它们是视野里最大的参照物，偏小会让整个农场显得"迷你"；
                //      相机拉远之后尤其明显——树只有 4.2m，在 40m 的场地边上像灌木。
                //      树 1.6×（4.2→6.8m）、风车 1.5×（2.7→4.0m），仍然低于相机高度不会挡视野。
                case "tree": return 1.60f;
                case "windmill": return 1.50f;
                case "chicken_coop": return 1.30f;
                case "crate_stack": return 1.25f;
                default: return 1f;
            }
        }

        private void SpawnScenery(string id, Vector3 pos, float yaw, float sway, float phase)
        {
            // simple: true —— 装饰走轻量材质（无墨线无霓虹）。
            // 场上常驻 400+ 个装饰，每个三个 Pass 会把 drawCall 推到 ~2000；
            // 而每根草都勾黑边近看就是一团黑线，砍掉是性能与观感同时受益。
            ModelView mv = ModelView.Spawn("Environment", id, _root, false, true);
            if (mv == null) return;
            mv.Go.name = "SCN_" + id;
            mv.Go.transform.position = pos;
            mv.SetFacing(yaw);
            mv.SetScaleFactor(SceneryScale(id));
            if (sway > 0.001f)
                _scenery.Add(new SceneryItem { View = mv, BaseYaw = yaw, Sway = sway, Phase = phase });
            else
                _sceneryStatic.Add(mv.Go);
        }

        /// <summary>风：装饰轻微摆动。摆的是表现，与玩法无关，所以用渲染帧 dt。</summary>
        private void SyncScenery(float dt)
        {
            if (_scenery.Count == 0) return;
            _sceneryTime += dt;
            for (int i = 0; i < _scenery.Count; i++)
            {
                SceneryItem it = _scenery[i];
                if (it.View == null) continue;
                float w = Mathf.Sin(_sceneryTime * 1.35f + it.Phase) * 2.6f * it.Sway;
                // 摆动也走 SetFacing：它会把摆动叠加在"原始旋转"之上，
                // 直接赋 Quaternion.Euler 会把 Z-up→Y-up 的转换一起抹掉。
                it.View.SetFacing(it.BaseYaw + w);
            }
        }

        private struct SceneryItem
        {
            public ModelView View;
            public float BaseYaw;
            public float Sway;
            public float Phase;
        }

        private readonly List<SceneryItem> _scenery = new List<SceneryItem>();
        private readonly List<GameObject> _sceneryStatic = new List<GameObject>();
        private float _sceneryTime;

        /// <summary>围栏只圈住"已有耕地"的范围，不预示未来的地块。</summary>
        private void UpdateFence(RunSession s)
        {
            FarmGrid grid = s.World.Farm;
            if (grid.Slots.Length == 0) return;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < grid.Slots.Length; i++)
            {
                FarmPlot p = grid.Slots[i];
                float hx = p.World.x, hz = p.World.y;
                minX = Mathf.Min(minX, hx); maxX = Mathf.Max(maxX, hx);
                minY = Mathf.Min(minY, hz); maxY = Mathf.Max(maxY, hz);
            }
            float pad = grid.TileSize * 0.5f + 0.1f;
            float cx = (minX + maxX) * 0.5f, cz = (minY + maxY) * 0.5f;
            float sx = (maxX - minX) * 0.5f + pad, sz = (maxY - minY) * 0.5f + pad;

            // v59：栅栏改用美术模型（每段 2.0m，首尾相接）。
            // 旧版是 4 个拉长的长方体，看不出是栅栏。
            _fencePlan.Clear();
            FenceRun(new Vector3(cx - sx, 0f, cz + sz), new Vector3(cx + sx, 0f, cz + sz), 0f);
            FenceRun(new Vector3(cx + sx, 0f, cz - sz), new Vector3(cx - sx, 0f, cz - sz), 0f);
            FenceRun(new Vector3(cx + sx, 0f, cz + sz), new Vector3(cx + sx, 0f, cz - sz), 90f);
            FenceRun(new Vector3(cx - sx, 0f, cz - sz), new Vector3(cx - sx, 0f, cz + sz), 90f);

            // 按需增删，避免每帧重建
            while (_fenceSegs.Count < _fencePlan.Count)
            {
                ModelView mv = ModelView.Spawn("Environment", "fence", _root, false);
                if (mv == null) break;
                _fenceSegs.Add(mv);
            }
            for (int i = 0; i < _fenceSegs.Count; i++)
            {
                bool used = i < _fencePlan.Count;
                if (_fenceSegs[i].Go.activeSelf != used) _fenceSegs[i].Go.SetActive(used);
                if (!used) continue;
                _fenceSegs[i].Go.transform.position = _fencePlan[i].Pos;
                _fenceSegs[i].SetFacing(_fencePlan[i].Yaw);
                // 栅栏段模型本身就是 2.15m 长，和段距 2.0m 基本对齐，用原始大小即可
                //
                // ★ v149：**回退 v148 的错误修改** ★
                //
                // 我曾把这里改成 `ScaleToHeight(1.1f)`，理由是"审计报围栏 81 米高"✗
                // 实测结果是**更糟**：
                //   v148 前：世界高 81.00m   scale=100.0   位置 -99.5
                //   v148 后：世界高 110.00m  scale=135.8   位置 -135.3   ← 更大、更外
                //
                // 反推：`ScaleToHeight(1.1)` 得出 135.8 倍 ⇒ `RawHeight ≈ 0.0081`，
                // 也就是**围栏网格本来就是 0.0081 单位高** ✓
                // 那么 `SetScaleFactor(1f)`（= BaseScale 100 × 1）给出 0.81 米 —— **原本就是对的** ✓
                //
                // ★ 所以真正有问题的是【审计的数字】，不是这段代码 ★
                // 审计用 `Renderer.bounds.size.y`，而它报的 110 / 135.8 = 0.81 ——
                // 恰好等于"网格高度"，说明它**没有正确反映最终世界尺寸** ✗
                // 我据一个不可信的读数改了一处本来正确的代码，还改坏了 ✗
                //
                // 教训：**改之前要先确认测量可信**。这次的代价是一轮返工 + 一次更糟的状态。
                _fenceSegs[i].SetScaleFactor(1f);
            }
        }

        private readonly List<ModelView> _fenceSegs = new List<ModelView>();
        private readonly List<(Vector3 Pos, float Yaw)> _fencePlan = new List<(Vector3, float)>();

        /// <summary>沿一条边按 2.0m 分段排栅栏。</summary>
        private void FenceRun(Vector3 a, Vector3 b, float yaw)
        {
            const float Seg = 2.0f;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.05f) return;
            Vector3 dir = d / len;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / Seg));
            for (int i = 0; i < n; i++)
            {
                Vector3 mid = a + dir * ((i + 0.5f) * (len / n));
                _fencePlan.Add((new Vector3(mid.x, 0f, mid.z), yaw));
            }
        }

        /// <summary>
        /// v145 起：**把三把武器挂到右手**；v198 起挂点换成程序化豆豆人的掌心。
        ///
        /// ★ 挂点在哪（现在是确定的，不是"找找看")★
        /// 豆豆人由 `PlayerBean` 在代码里建，右手掌心有一根叫 **`Bone_Hand_R`** 的空节点
        ///（名字沿用旧 FBX 骨架的那一根，所以下面那段"按名字找挂点"的检查**一行没改**就还有效 ✓）。
        /// 三把武器的挂载参数（局部旋转/缩放/偏移）集中在 `PlayerBean.ToolGripEuler/ToolScale/ToolOffset`，
        /// 本方法只是"按武器索引要一把、挂上去、开局只显示锄头"。
        ///
        /// ★ 为什么参数搬进了 `PlayerBean` ★
        /// 验收台 `PlayerShowcase` 也要挂武器拍照，若两处各写一份角度，
        /// 照片上的握法与游戏里的握法迟早分叉 —— 那是"图上看对了、实机不对"的典型来源 ✗
        /// 一条路就只有一个真值 ✓
        ///
        /// ★ 三把的握持朝向为什么**不是同一个数** ★
        /// 旧版给三把填同一个 `(0,180,0)`，理由是"握持朝向烘进了网格顶点、`GRIP` 都在原点"
        ///（`Tools/AssetPipeline/manifest.json`）。这条在旧骨架上成立 ✓
        /// 而豆豆人的掌心朝下、本地 +Z 是身前 ⇒ 要以 X 轴转 −90° 才把"手柄沿 +Z"竖起来；
        /// 三把武器的长度不同（`PlayerBean.ProbeTools()` 报世界包围盒）⇒ 缩放也必须逐把给 ✓
        /// </summary>
        private void AttachTools()
        {
            if (_player == null) return;

            // v146：**按名字递归查找**，不再按路径 ✗
            //
            // 上一版用 `transform.Find("Armature/Bone_Hand_R")`（按路径），实机报
            //   "找不到 Armature/Bone_Hand_R，武器不挂载"
            // 原因是 `ModelView.Spawn` 是**两层结构**（外层摆位 + 内层动画）✓
            // 而 FBX 导出的层级里还有它自己的根节点，实际路径比交接单描述的更长 ✗
            //
            // 按【名字】查找对层级差异免疫 —— 骨骼名是稳定的（交接单列过 19 根），
            // 而"它在第几层"取决于导出设置与两层包装 ✓ 后者不该被写死在代码里 ✗
            //
            // 顺带把找到的层级打出来，下次再出问题一眼能看出结构 ✓
            Transform hand = null;
            var all = _player.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "Bone_Hand_R") { hand = all[i]; break; }
            }
            if (hand == null)
            {
                // 仍然找不到：把实际见到的骨骼名打出来，而不是只说"没找到" ✗
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < all.Length && sb.Length < 400; i++)
                    if (all[i] != null && all[i].name.StartsWith("Bone_")) sb.Append(all[i].name).Append(' ');
                Debug.LogWarning("[Seedfall] 找不到骨骼 Bone_Hand_R，武器不挂载。"
                                 + $"模型内骨骼名：{(sb.Length == 0 ? "（一个都没有）" : sb.ToString())}");
                return;
            }

            string[] ids = { "tool_hoe", "tool_can", "tool_fert" };
            _tools = new GameObject[ids.Length];
            int ok = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                // 挂载（父节点/旋转/缩放/偏移）由豆豆人自己完成 —— 见本方法注释最后一段
                _tools[i] = _playerModel.MountTool(i);
                if (_tools[i] != null) ok++;
                SetToolVisible(i, i == 0);                                     // 开局显示锄头
            }
            Debug.Log($"[Seedfall] 武器挂载完成：{ok}/{_tools.Length}"
                      + $"（挂点 {hand.name}，掌心局部系）");
        }

        /// <summary>显示/隐藏某把武器。按索引操作，与 `Actors.WeaponHoe/Can/Fertilizer` 对齐。</summary>
        private void SetToolVisible(int index, bool on)
        {
            if (_tools == null || index < 0 || index >= _tools.Length) return;
            if (_tools[index] != null) _tools[index].SetActive(on);
        }

        private GameObject[] _tools;

        /// <summary>
        /// v172：当前**已经显示**在手上的武器索引。换武器靠它做"变化检测"——
        /// 每帧无条件 `SetActive` 有开销，而且会反复打断动画状态 ✓
        /// -1 = 还没挂过任何武器（`AttachTools` 之前）✓
        /// </summary>
        private int _shownWeapon = -1;

        /// <summary>
        /// v172：本帧是否有"玩家出手了"的信号。由事件处理器在 `FixedTick` 里置位，
        /// 由 `SyncPlayer` 在 `View.Sync` 里消费（帧序：`GameBootstrap` 先 `_clock.Advance` 再 `Sync`）✓
        /// -1 = 没有。值就是触发这次攻击的武器索引 ✓
        /// </summary>
        private int _pendingAttack = -1;

        /// <summary>
        /// ★★ v204：这次 `_pendingAttack` 是**按住持续**投递的（水壶激光每个固定帧一次），
        /// 还是**一次离散出手**（锄头 / 肥料）★★
        ///
        /// 消费处的判据靠它分流（见 `SyncPlayer` 里"判据从…改成…"那一段）：
        ///   `true`  ⇒ 只在上一段播完时才重开（否则持续激光把动画钉在第 0 帧）
        ///   `false` ⇒ **无条件重开**（每次出手都从第 0 帧起播，上一次没播完也算）
        /// 写者只有 `OnBeam` 一处（置 true）；其余出手事件不写它 ⇒ 默认 false ✓
        /// 消费后一律复位成 false，避免"上一帧是水壶"污染"这一帧是锄头"✓
        /// </summary>
        private bool _pendingAttackRepeat;

        /// <summary>
        /// ★ v204：**攻击动画被重开的次数**（只读，给探针/自检核对"20 刀 = 20 次重开"）★
        /// 与 `PlayerAttackStarts` 同一个数，但这个挂在实例上、不需要拿静态量做差。
        /// </summary>
        public int AttackAnimStarts => _attackAnimStarts;
        private int _attackAnimStarts;

        /// <summary>
        /// ★ v204：**上一次交给模型库的攻击动画时长（秒）** —— 探针用它证明
        /// "时长 = cooldown / 攻速，且有 0.06s 下限"（见 `MinAttackAnimSeconds`）★
        /// </summary>
        private float _lastAttackAnimSeconds;
        public float LastAttackAnimSeconds => _lastAttackAnimSeconds;

        /// <summary>★ v204：玩家模型当前状态与状态时间（只读；探针据此判断"动画有没有在走/有没有被重置"）★</summary>
        public ModelView.State PlayerAnimState =>
            _playerModel != null ? _playerModel.Current : ModelView.State.Idle;
        public float PlayerAnimTime => _playerModel != null ? _playerModel.StateTime : 0f;

        /// <summary>★ v204：玩家豆豆人的一句话姿态描述（`armR=` 等；探针用它读"抬到头顶 → 砸下"的幅度）★</summary>
        public string PlayerBeanPose => _playerModel != null && _playerModel.Bean != null
            ? _playerModel.Bean.Describe() : "（没有豆豆人）";

        /// <summary>
        /// ★ v204：玩家模型的**右手挂点**（`PlayerBean.HandMount`）与模型根节点 ★
        ///
        /// 为什么探针要拿它、而不是继续读 `armR` 欧拉角 ✗
        ///   欧拉角在 **x ≈ ±90°** 附近是**奇异点**（ZXY 分解在那里有两种等价解）——
        ///   实测（`ProbeV204Anim` 第一版）：锄头挥到 -101° 时 `localEulerAngles.x` 读回来是 -78°，
        ///   于是"手臂行程"看起来中途自己弹回去 ✗ 那是**读者的**假象，不是姿态坏了 ✓
        ///   而手的**世界位置**没有这种歧义：抬到头顶就高、砸下去就低 ✓
        ///   ⇒ 判"有没有走完一整段前摇→砸下"，用手的位移才是可靠的 ✓
        /// </summary>
        public Transform PlayerHandMount =>
            _playerModel != null && _playerModel.Bean != null ? _playerModel.Bean.HandMount : null;

        /// <summary>★ v204：玩家模型的根节点（探针用它把手的**世界位置**换回**模型本地**）★</summary>
        public GameObject PlayerModelGo => _playerModel != null ? _playerModel.Go : null;

        private static int _playerAttackStarts;   // 见 `PlayerAttackStarts`
        /// <summary>
        /// ★ v204：**全局**的攻击动画重开计数（三个 `WorldView` 实例共用一条）——
        /// 探针在"一次会话只建一个世界"的前提下用它做前后差 ✓
        /// 需要分实例读的用 `AttackAnimStarts`（同一个数、挂在实例上）。
        /// </summary>
        public static int PlayerAttackStarts => _playerAttackStarts;

        /// <summary>
        /// v172：给自动化/调试读的一份表现层状态快照。
        ///
        /// 存在的理由：缺陷①②③④全都是**表现层**的，玩法断言（血量/伤害/冷却）一条都测不出来 ✗
        /// "动作接上没有"只能靠这种可观测的量来判断 ✓
        /// 它**只读**，不改变任何状态 —— 不引入新的运行时依赖 ✓
        /// </summary>
        public string ProbePlayerAnim(RunSession s)
        {
            var sb = new System.Text.StringBuilder();
            PlayerActor p = s != null && s.World != null ? s.World.Player : null;
            sb.Append($"weapon={(p != null ? p.WeaponIndex : -1)} shown={_shownWeapon} ");
            sb.Append($"pending={_pendingAttack} windup={(p != null ? p.SwitchWindup : 0f):0.00} ");
            sb.Append("tools=[");
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_tools != null && i < _tools.Length && _tools[i] != null
                    ? (_tools[i].activeSelf ? "ON" : "off")
                    : "缺失");
            }
            sb.Append("] ");
            if (_playerModel == null) { sb.Append("model=占位图元（无动画）"); return sb.ToString(); }
            sb.Append($"state={_playerModel.Current} clip={_playerModel.CurrentClipName} ");
            // ★ v174：`speed=` 改成报**当前真正生效的**倍率 ✗ ★
            // 原来报的是 `PlaybackSpeed` —— 它**恒为 Walk 那一档** ✗ ⇒ 攻击期间恒显 `1.00`
            // ⇒ 看起来像"攻速倍率没接上"✗ 而实际生效在另一路 ✓
            // 两个分量一起打出来 ⇒ 一句里就能分清"现在是谁在起作用"✓
            sb.Append($"t={_playerModel.StateTime:0.000} speed={_playerModel.EffectivePlaybackSpeed:0.00} ");
            sb.Append($"(walk={_playerModel.PlaybackSpeed:0.00} atk={_playerModel.AttackPlaybackSpeed:0.00}) ");
            sb.Append($"atkClips=[{_playerModel.DescribeAttackClips()}]");
            // ★ v180：分层姿势的**实测读数**（攻击时到底动了哪些节点 / 身体有没有被带动）★
            sb.Append(" | ").Append(_playerModel.DescribeLayeredPose());
            return sb.ToString();
        }

        /// <summary>
        /// v152：是否创建玩家模型（**用于隔离遮挡问题的开关**）。
        /// 置 false 时玩家退化为占位胶囊，但世界其余部分正常 ——
        /// 用它一次就能判断"遮挡是玩家模型造成的，还是别的" ✓
        /// </summary>
        public static bool PlayerModelEnabled = true;    // v153：实验结束，恢复玩家模型

        private void BuildPlayer(RunSession s)
        {
            // ★ v152：**玩家模型开关**（用于隔离"遮挡画面"的元凶）★
            //
            // 现象：画面被巨大的灰色/棕色模型完全挡住 ✗ 而高度排行显示
            //   ANIM_player 7825m / ANIM_tool_* 2108833m / ANIM_tree 676m
            // 三组都超标，所以无法断定是哪一组造成遮挡 ✗
            //
            // 用户建议的做法是对的：**先移除嫌疑对象，看症状是否消失** ✓
            // 这是最快的排除法 —— 比继续在缩放链上推理可靠得多 ✓
            //
            // 置 false 之后玩家模型不创建，`_player` 会走下面的占位胶囊分支 ✓
            // 于是"世界是否正常"与"玩家模型是否有问题"被彻底分开 ✓
            // ★ v198：玩家换成**程序化 Q 版豆豆人**（农夫）★
            //
            // 改前：`ModelView.Spawn("Characters","player")` —— 19 根骨骼的 FBX ＋ 7 段
            //       每段 6 关键帧的合并片段（`Art/AnimMerged/player_*.asset`）
            // 改后：`ModelView.SpawnPlayer` —— `PlayerBean` 在代码里按尺寸表建几何、按时间解析求姿态
            //
            // 换的理由（三条，都指向同一件事：这条路上没有可调的东西）：
            //   ① 头身比要 1:1 —— FBX 的骨架是照人形绑的（约 1:1.6），改比例要重绑+重蒙皮；
            //   ② "抽搐"来自 6 关键帧的三次 Hermite，重采样是**恒等变换**（同值同斜率）⇒ 改不动；
            //   ③ 挥击弧、壶嘴朝向这类**按武器算出来**的东西，片段里根本没有。
            // 而 `ModelView` 的其余 27 处接口（朝向/缩放/染色/状态守卫）与"模型是不是片段驱动"正交，
            // 所以只换模型、不动它们 ⇒ 玩法逻辑零改动 ✓
            _playerModel = PlayerModelEnabled ? ModelView.SpawnPlayer(_root) : null;
            if (_playerModel != null)
            {
                _player = _playerModel.Go;
                // 缩放沿用同一个系数（CharScale 2.60 × PlayerScale 0.627 = 1.6302）
                // ⇒ **场上体型与改前同量级**（改前世界高 1.8584m、改后 ≈ 1.91m），
                //    相机取景、命中判定、敌人间距都不受影响 ✓
                // 豆豆人自身的米制高是 1.17m（`PlayerBean.TotalHeight`）—— 那个 1:1 的头身比在**模型**上，
                // 与"场上多大"是两个旋钮，别把它们混在一起 ✓
                _playerModel.SetScaleFactor(CharScale * PlayerScale);   // v85：玩家比普通怪再大一点
                AttachTools();                                          // v198：三把武器挂到豆豆人的掌心

                // ★ v198：攻击片段表**不再需要** ★
                // v172 那段 `SetAttackClips(new[]{ LoadClip("player","attack_hoe"), ... })` 是给
                // "片段驱动"的模型用的；豆豆人的挥击是解析姿态，改由 `PlayerBean.SetWeapon`
                // （经 `ModelView.SetAttackWeapon` 转发）决定走哪一套 ✓
                // 留着那三行不会报错，但会去 `Resources` 里白加载三张 300KB 的片段资产 ✗
                // 对齐"已经显示"的那一把：`AttachTools` 显示的是 0 号（锄头）✓
                _shownWeapon = 0;
                _pendingAttack = -1;
                _playerModel.SetAttackWeapon(0);

                // ★ v208：玩家 = **白色霓虹描边**（用户原话"玩家附加同样高亮的白色描边"）★
                // 放在这里（模型刚建好）而不是每帧：`SetNeon` 内部会 `PushBlock()` 逐渲染器写 MPB，
                // 而 MPB 的 `_NeonColor` 会被之后的 `SetTint`（每帧的受击闪白/无敌）**原样保留** ——
                // 因为 `PushBlock` 先 `GetPropertyBlock` 再写，只覆盖它自己那三个 id ✓
                // 宽度：★ v210 改成**显式 3 厘米**（不再走 `RawHeight*0.05 ≈ 5.9cm` 的默认值）★
                // 5.9 厘米在近处（8.5 米、每像素 0.95 厘米）会画成 6.3 像素宽的白带 ——
                // 本轮基线抓图里玩家脚下那几块"白斑"就是它 ✗ 见 `NeonPlayerWidthMeters` 的说明。
                // 像素下限仍与敌人同档（`NeonPxChar`）—— 判据就是"玩家白、敌人红，一眼分得开" ✓
                _playerModel.SetNeon(NeonPlayer, NeonPlayerWidthMeters, NeonPxChar, NeonBoostChar);
            }
            else
            {
                _playerMat = Mat(new Color(0.35f, 0.62f, 0.95f));
                _player = Prim(PrimitiveType.Capsule, "Player", Vector3.zero, new Vector3(0.9f, 1.0f, 0.9f), _playerMat);
            }
            // v156：商店车（模型在 Art/Props/ 下，subagent 交付时已确认车头朝 Unity +Z ✓）
            _truckModel = ModelView.Spawn("Props", "shop_truck", _root);
            if (_truckModel == null)
                Debug.LogWarning("[Seedfall] 取不到 Props/shop_truck，商店车不会出现 ✗"
                                 + "（检查 Assets/Resources/Art/Props/shop_truck.fbx）");
            else
                _truckModel.Go.SetActive(false);      // 开局隐藏，由状态机决定何时出现 ✓

            _aimTip = Prim(PrimitiveType.Sphere, "AimTip", Vector3.zero, Vector3.one * 0.28f,
                Mat(new Color(1f, 0.9f, 0.4f), true));
        }

        /// <summary>v46：肥料落点准星（平时隐藏，手持肥料时才显示）。</summary>
        private void BuildBlastReticle()
        {
            _reticleMat = Mat(new Color(1f, 0.58f, 0.16f), true);
            _reticle = Prim(PrimitiveType.Cylinder, "BlastReticle", Vector3.zero,
                new Vector3(1f, 0.02f, 1f), _reticleMat);
            _reticle.SetActive(false);
        }

        /// <summary>
        /// v46：把"鼠标位置被射程钳制后到底炸在哪、炸多大"画在地上。
        /// 鼠标超出投掷射程时准星会停在射程边界并变红——玩家因此能直接看出射程有没有生效。
        /// </summary>
        private void SyncBlastReticle(RunSession s)
        {
            if (_reticle == null) return;
            PlayerActor p = s.World.Player;
            bool show = p != null && p.Alive && !s.IsOver
                        && p.WeaponIndex == PlayerActor.WeaponFertilizer;
            if (_reticle.activeSelf != show) _reticle.SetActive(show);
            if (!show) return;

            Vector2 center = s.CurrentBlastCenter();
            // v111：准星半径必须与**真实爆炸**同一个算法（含武器自己的基础爆炸范围），
            // 否则玩家按准星判断"这块田炸得到"，结果炸不到——标记就成了骗人的。
            float radius = RunSession.BlastBaseRadius(TableDatabase.Tuning.tools.fertilizer.baseExplodeRange)
                           * s.Progress.Stats.Get(StatId.ExplodeRange);
            _reticle.transform.position = new Vector3(center.x, 0.07f, center.y);
            _reticle.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);

            if (_reticleMat != null)
            {
                Color c = s.IsBlastOutOfRange ? new Color(1f, 0.30f, 0.20f) : new Color(1f, 0.58f, 0.16f);
                if (_reticleMat.HasProperty(BaseColorId)) _reticleMat.SetColor(BaseColorId, c);
                if (_reticleMat.HasProperty(ColorId)) _reticleMat.SetColor(ColorId, c);
            }
        }

        public void Sync(RunSession s, float dt)
        {
            if (_root == null || s.World == null) return;
            World w = s.World;

            SyncPlots(s);
            // ★ v197：机制植物 / 胡萝卜 / 兔子 / 盾环 / 太阳 / 下雨（实现在 `WorldView.Prog.cs`）★
            //   **必须在 `SyncPlots` 之后**：`SyncPlots` 每帧会把占位方块重新打开，
            //   而机制植物没有 fbx ⇒ 要靠这一行在它之后把方块压下去、换成程序化植株 ✓
            SyncProgVisuals(s, dt);
            SyncScenery(dt);          // v64：装饰随风摆动
            TickSwings(dt);           // v66：挥击弧的扫过动画
            SyncPlayer(s, dt);
            SyncShopTruck(s, dt);      // v157：商店车（只读会话状态，不持有状态机）
            // v74：火焰光跟随玩家（夜里唯一的稳定近场光源）
            if (NightLighting.Instance != null)
                NightLighting.Instance.FollowPlayer(new Vector3(w.Player.Pos.x, 0f, w.Player.Pos.y), dt);
            SyncBlastReticle(s);
            SyncEnemies(w, dt);
            SyncDotFx(s, dt);          // ★ v188：毒区地面 / 灼烧火星 / 减速箭头（必须在敌人之后：要用它们的实时位置）
            SyncBossBar(w);            // v183：BOSS 头顶血条（必须在 `SyncEnemies` 之后 —— 要量它的模型高度）
            TickBlasts(dt);
            TickFxBits(dt);            // v176：插件特效的火花
            TickFxMeshes(dt);          // v179：三种攻击的实体颗粒（碎片/棱台段/球壳/湿痕）
            // ★ v191·fx3：三套新表现的推进 ★
            //   灰尘在 `SyncPlayer` 里推进（它要知道"这一帧玩家在不在动"）
            TickSplat(dt);             // ③ 死亡血液痕迹（飞散 + 3s 缓慢消失）
            TickHealFx(dt);            // ① 整点回血四段（世界空间，1.5s 播完自己灭）
            TickLightRamps(dt);        // v179：会衰减的点光
            // ★ v307：`_beamFxCd` / `_wetCd` 都改成"每道一格"，统一在 `TickLightRamps` 那趟里递减
            TickWarnMarkers(dt);
            TickBossSkillFx(dt);        // ★ v201：新 BOSS 技能的环形波 / 光柱 / 预警圈 ★
            SyncProjectiles(w);
            _lastWorld = w;
            SyncPickups(w);
            SyncMarkers(dt);
        }

        /// <summary>
        /// v68：按种子 id 建出耕地上的两块作物模型（幼苗 + 成熟），并各套上品质霓虹描边。
        /// 同一块地可能先种豌豆后种辣椒，所以种子变了必须重建——这里用 _plotSeedIds 记住上一轮的 id。
        /// </summary>
        private void EnsurePlotCropModels(int i, string seedId)
        {
            if (string.IsNullOrEmpty(seedId)) return;
            if (_plotSeedIds[i] == seedId) return;      // 已经是这个品种，不重建

            // 换品种：先把旧的拆掉，否则会出现两块模型叠在同一格
            if (_plotCrops[i] != null) { Object.Destroy(_plotCrops[i].Go); _plotCrops[i] = null; }
            if (_plotYoungs[i] != null) { Object.Destroy(_plotYoungs[i].Go); _plotYoungs[i] = null; }
            if (_plotGrowns[i] != null) { Object.Destroy(_plotGrowns[i].Go); _plotGrowns[i] = null; }
            _plotSeedIds[i] = seedId;

            Color neon = NeonOf(Projectile.GradeColor(SeedGradeOf(seedId)));
            ModelView mature = ModelView.Spawn("Plants", seedId, _root, false);
            if (mature != null) { ApplyCropNeon(mature, neon); _plotCrops[i] = mature; }
            ModelView young = ModelView.Spawn("Plants", seedId + "_young", _root, false);
            if (young != null) { ApplyCropNeon(young, neon); _plotYoungs[i] = young; }
            // v103：第三档「成长中」。取不到就留 null —— 选择逻辑会退回用成体，
            // 也就是**旧行为**，不会因为缺这一个模型就整块地不显示。
            ModelView grownMv = ModelView.Spawn("Plants", seedId + "_grown", _root, false);
            if (grownMv != null) { ApplyCropNeon(grownMv, neon); _plotGrowns[i] = grownMv; }

            // v104 诊断：报告三档到底建出来几个。
            // 前一轮我用对象探针查 `MDL_<id>_grown` 查不到，但无法区分
            // "没建出来"和"建出来了但探针找不到"——所以不再猜，直接让程序报。
            // 三种结果的含义：
            //   young=OK grown=OK mature=OK  → 三档齐全，问题只在验证手段
            //   grown=null                   → 资源没被加载（路径/导入问题）
            //   young=null mature=null       → 连旧的两档都没了，是更早的回归
            // v106：把**全部五档**的品质霓虹色一次性打出来。
            //
            // 上一轮我是"等游戏碰巧种出不同品质"，结果一整局只出了一个 D 档，
            // 拿到的 `grade=D neon=(1,1,1)` 无法区分"品质色被用上了"和"永远是白的"。
            // 与其等，不如**直接枚举**——五档全打，一眼就能看出颜色是否真的随品质变化。
            // 需求要的是"成长中的植物加**品质对应**的霓虹"，判据就是这五行的 RGB 必须互不相同。
            if (!_gradeDumpDone)
            {
                _gradeDumpDone = true;
                var sb = new System.Text.StringBuilder("[Seedfall] 品质霓虹色对照：");
                for (int g = 0; g <= 4; g++)
                {
                    Color nc = NeonOf(Projectile.GradeColor((SeedGrade)g));
                    sb.Append($" {Rarity.Names[Mathf.Clamp(g, 0, Rarity.Names.Length - 1)]}=({nc.r:0.00},{nc.g:0.00},{nc.b:0.00})");
                }
                UnityEngine.Debug.Log(sb.ToString());
            }

            UnityEngine.Debug.Log(
                $"[Seedfall] 作物模型 {seedId}：young={(young != null ? "OK" : "null")} " +
                $"grown={(grownMv != null ? "OK" : "null")} mature={(mature != null ? "OK" : "null")} " +
                // v105：把「品质等级」与算出来的**霓虹色**一起打出来，用来验证需求
                // "为成长中的植物同样增加品质对应的颜色霓虹光描边"。
                // 判据：**不同品质必须得到不同颜色**。若两个不同 tier 打出同一个 RGB，
                // 说明品质色没被真正用上（例如 NeonOf 的提亮把颜色压成了同一个白）。
                // 这比截图可靠：田里的作物太小，描边颜色在画面上根本分辨不出来。
                $"grade={SeedGradeOf(seedId)} neon=({neon.r:0.00},{neon.g:0.00},{neon.b:0.00})");
        }

        /// <summary>取种子品质。GetSeed 找不到会抛异常，所以先查表再取，缺表按 D 处理而不是崩。</summary>
        private static SeedGrade SeedGradeOf(string seedId)
        {
            try { return TableDatabase.GetSeed(seedId).Grade; }
            catch (System.Exception) { return SeedGrade.D; }
        }

        private void SyncPlots(RunSession s)
        {
            EnsurePlotTiles(s);
            FarmGrid grid = s.World.Farm;
            for (int i = 0; i < _plotTiles.Length; i++)
            {
                FarmPlot plot = grid.Slots[i];
                Color c;
                switch (plot.State)
                {
                    case PlotState.Locked: c = new Color(0.17f, 0.17f, 0.16f); break;
                    case PlotState.Empty: c = new Color(0.42f, 0.30f, 0.19f); break;
                    case PlotState.Dug: c = new Color(0.30f, 0.20f, 0.13f); break;
                    case PlotState.Growing:
                        c = plot.Watered ? new Color(0.22f, 0.20f, 0.16f) : new Color(0.34f, 0.24f, 0.15f);
                        break;
                    default: c = new Color(0.26f, 0.22f, 0.15f); break;
                }
                SetColor(_plotTiles[i], c);
                if (_plotSoils[i] != null)
                    SetColor(_plotSoils[i], c * 0.85f);
                _plotTiles[i].SetActive(true);
                if (_plotTrees != null && i < _plotTrees.Length && _plotTrees[i] != null)
                {
                    bool isTree = plot.State == PlotState.Growing || plot.State == PlotState.Grown;
                    _plotTrees[i].SetActive(isTree);
                    if (isTree)
                    {
                        // v68：耕地上的作物分两个阶段显示——
                        //   幼苗（<55%）用 <种子id>_young 模型，成熟用完整模型。
                        //   为什么要两个模型：只有一个模型时"成长"只能靠缩放，看不出"长出了什么"；
                        //   而且幼苗阶段本来就不该认出品种，只保留配色线索。
                        // 尺寸基准：耕地方格边长 x 0.72，保证成熟形态落在格子内、不出框。
                        // v166：作物外观按**这一格上的作物**取品种，而不是"驱动这一列的种子" ——
                        // 一列里每一格都是同一株的一份作物，只有列首登记 SeedId（1:1 契约，见 FarmPlot.SeedId）。
                        EnsurePlotCropModels(i, plot.CropSeedId);
                        float gp = Mathf.Clamp01(plot.GrowthProgress01);
                        // v103：**三档**选择 —— 幼苗 / 成长中 / 成体。
                        // 旧版只有两档，55% 处会从"小芽"直接跳成"会走路的小人"，
                        // 中间少了"长出了什么"这一步。现在中间那段用旧版非拟人化造型。
                        // 取不到 `_grown` 时**退回成体**（旧行为）——缺一个模型不该让地块空着。
                        //
                        // ★ v254：修「成熟模型永远不出现」★
                        //   旧判据是 `mid = !young && _plotGrowns[i] != null` —— **不含上界**。
                        //   而 `gp ∈ [0.55, 1.00]` 且 fbx 三档齐全 ⇒ `mid` 在整段都成立 ⇒
                        //   `_plotCrops[i]`（`<id>.fbx` 成熟体）**一辈子取不到**，
                        //   24 个有 fbx 的品种从 55% 到收获一直是 `_grown` ✗
                        //   （证据：这条链上 `show` 只可能是 young/grown，没有任何分支能落到 mature）
                        //   现在补上 0.85 上界 —— 与 `WorldView.Prog.cs` 的 `ProgStageStart[3]`
                        //   是**同一个切点**，两条路径（程序化 / fbx）的生长阶段到这里对齐 ✓
                        //   取不到 `_crops` 时退回 `_grown`、再退回 `_young`，不让地块空着 ✓
                        // ★ v256：修「换种 / 新种下之后，田里要过很久才看得出是新植物」★
                        //   取证：`SyncPlots` 每帧都跑，`EnsurePlotCropModels`（:1663）在品种变化的
                        //   **那一帧**就重建三档模型 ⇒ 状态与模型本身是**同帧**的，没有节流。
                        //   玩家看到的"延迟"完全来自**这一行的切点**：
                        //     旧值 `young = gp < 0.55f` ⇒ 0~55% 都放**同一张**幼苗模型。
                        //     growTime 30~40s 的品种 ⇒ 换种后 **16~22 秒**画面上一动不动的同一根小芽，
                        //     品种身份要等 gp ≥ 0.55 才第一次改变外形 ⇒ 被读成"没刷新 / 刷新很慢"。
                        //   程序化那条链是 4 档、第一切点在 25%（`WorldView.Prog.cs:114`
                        //   `ProgStageStart = {0, 0.25, 0.55, 0.85}`）⇒ 同一件事只等 8~10 秒。
                        //   两条链的切点本来就该对齐（上一轮只对齐了 0.85 那一端）⇒ 这里补上 0.25 端 ✓
                        //   ⚠️ 缺 `_grown` 模型时**保持旧的 0.55** —— 否则那段会直接跳到成体（回归）✓
                        //   ⚠️ 高度不从这里来（读统一曲线 `CropHeightFrac01(gp)`，:1716）⇒
                        //      改切点**不会**造成切模型时的高度跳变（C0 连续，与 v255 的结论一致）✓
                        float youngEnd = _plotGrowns[i] != null ? 0.25f : 0.55f;
                        bool young = gp < youngEnd;
                        bool mid = !young && gp < 0.85f && _plotGrowns[i] != null;
                        ModelView show = young ? _plotYoungs[i] : (mid ? _plotGrowns[i] : _plotCrops[i]);
                        if (show == null && _plotGrowns[i] != null) show = _plotGrowns[i];
                        if (show == null) show = _plotYoungs[i];
                        // 没被选中的两档全部关掉
                        ModelView[] allStages = { _plotYoungs[i], _plotGrowns[i], _plotCrops[i] };
                        for (int k = 0; k < allStages.Length; k++)
                        {
                            if (allStages[k] == null || allStages[k] == show) continue;
                            if (allStages[k].Go.activeSelf) allStages[k].Go.SetActive(false);
                        }

                        if (show != null)
                        {
                            if (!show.Go.activeSelf) show.Go.SetActive(true);
                            Vector3 p = new Vector3(plot.World.x, 0f, plot.World.y);
                            show.Go.transform.position = p;

                            // 成长动画：幼苗只在 0.45→0.75 之间长，成熟从 0.75 长到 1.0；
                            // 再叠一个轻微呼吸，让"正在生长"这件事在画面上是动的。
                            // ★ v254：非幼苗段的分母从 0.45 改成 0.30 ★
                            //   因为 `_grown` 这一段现在只走到 0.85（见上面的上界），
                            //   分母仍是 0.45 的话 gp 到 0.85 时 k 只有 0.93，
                            //   切到成熟体那一下会**突然弹大 7%**（正是要避免的闪烁）✓
                            //   改成 0.30 ⇒ gp = 0.85 时 k = 1.00，与成熟段首尾相接 ✓
                            // ★ v255：**高度改读统一曲线**（`WorldView.Prog.cs` 的 `CropHeightFrac01`）★
                            //   旧写法是两条 `Mathf.Lerp` 分支：幼苗 `gp/0.55 → 0.45…0.75`、
                            //   非幼苗 `(gp−0.55)/0.30 → 0.78…1.00`
                            //   ⇒ 两个分母不同 ⇒ **0.55 处 0.75 跳到 0.78（+4%）**，
                            //     切模型那一下的高度是断的（上一轮只把非幼苗段的分母从
                            //     0.45 改成 0.30 对齐了尾端，首端这 4% 还在）✗
                            //   现在两段都从**同一条锚点曲线**取（v279 的最终锚点 =
                            //   0.16 / 0.34 / 0.62 / 0.92 / 1.00，见 `CropHeightAnchorFrac`），
                            //   0.55 处只有一个值 0.62 ⇒ 断点消失 ✓
                            //   判据：这条曲线与程序化链（`SyncProgPlants`）是**同一个函数**，
                            //   所以 fbx 品种与程序化品种在同一 `gp` 下高度一致 ✓
                            float heightK = CropHeightFrac01(gp);
                            _plotGrowPhase[i] += 0.9f * Time.deltaTime;
                            float breath = 1f + 0.035f * Mathf.Sin(_plotGrowPhase[i]);
                            float tile = grid.TileSize;
                            // v75：**统一生长中的大小**——按目标高度缩放，而不是按各自模型尺寸乘系数。
                            // 旧做法（SetScaleFactor）会让大株植物在幼苗期就比小株的大一圈，
                            // 一排地看上去参差不齐；现在所有品种在同一生长进度下高度一致。
                            // （ScaleToHeight 内部就是重设缩放，不需要先 SetScaleFactor）
                            // v78：耕地上的作物**不乘 PlantScale**。
                            // 标准是"以耕地范围为准"（tile × 0.72 ≈ 1.8m），
                            // 之前多乘了一次显示放大系数 2.5，作物长到 4.5m —— 比耕地本身还大，
                            // 俯视特写下能明显看到它溢出格子。PlantScale 是给**场上参战的植物帮手**用的，
                            // 地里的作物属于场景陈设，两者不是一回事。
                            show.ScaleToHeight(tile * 0.72f * heightK * breath);
                            // v101：**成长中的作物也加品质色霓虹**（需求）。
                            // 旧版只有场上的参战帮手有霓虹，地里的作物没有——
                            // 于是玩家站在自己田边，看不出哪一株是高品质种子长出来的，
                            // 要等收割后才在帮手身上看到颜色。品质信息应该**在种下去的那一刻**就可读。
                            ApplyCropNeon(show, NeonOf(Projectile.GradeColor(SeedGradeOf(plot.CropSeedId))));
                            show.SetFacing(35f * Mathf.Sin(_plotGrowPhase[i] * 0.35f));
                            // v75：按**几何中心**对齐耕地中心（枢轴不一定在几何中心）
                            show.CenterOn(new Vector3(plot.World.x, 0f, plot.World.y));
                            _plotTrees[i].SetActive(false);   // 有模型就不再显示占位方块
                        }
                        else
                        {
                            float g = Mathf.Lerp(0.18f, 1f, gp);
                            _plotTrees[i].transform.localScale = new Vector3(0.7f, 1.1f * g, 0.7f);
                            _plotTrees[i].transform.position = new Vector3(plot.World.x, 0.55f * g, plot.World.y);
                        }
                    }
                    else
                    {
                        if (_plotCrops[i] != null && _plotCrops[i].Go.activeSelf) _plotCrops[i].Go.SetActive(false);
                        if (_plotYoungs[i] != null && _plotYoungs[i].Go.activeSelf) _plotYoungs[i].Go.SetActive(false);
                        if (_plotGrowns[i] != null && _plotGrowns[i].Go.activeSelf) _plotGrowns[i].Go.SetActive(false);
                    }
                }
                // v182：成熟 / 缺水的**常驻**箭头。放在这里（不受 `_plotTrees` 是否建出来影响），
                //        因为标记是**地块状态**的可读性，跟占位方块/模型解耦 ✓
                SyncStatusArrows(i, plot, grid.TileSize);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // v182：成熟 / 缺水的常驻箭头
        //
        // ★ 用户要的四条，逐条对应 ★
        //   ① 成熟的植物 → 常驻标记：判据 `plot.IsRipe`（`State == PlotState.Grown`）✓
        //   ② 缺水的植物 → 常驻标记：判据 `plot.State == Growing && plot.Thirsty`（`Water <= 0`）✓
        //      —— 与弹字判据**同一对谓词**：`Actors.cs:1376` 的 `if (plot.State != Growing) continue`
        //         加上 `:1413` 的 `else if (plot.Thirsty && !plot.ThirstyNotified)` ⇒
        //         弹字只可能在"生长中且蓄水为 0"时出现，箭头用同一条件 ⇒ 两者永远一致 ✓
        //         （刻意**不用** `ThirstyNotified`：它是"弹过一次"的一次性闸门，
        //           常驻标记要的是"现在仍然缺水"，用它会变成只显示一次 ✗）
        //   ③ **不是弹字**：这里一个 `FloatingTextEvent` 都没发，也不读 `RunSession.Popups` ✓
        //   ④ 颜色 = 对应弹字的颜色：**直接调 `PopupStyle.ColorOf(...)`**，
        //      即弹字自己用的那个函数（`RunSession.cs:1792` `Color = PopupStyle.ColorOf(e.Kind)`）✓
        //      ⇒ 颜色只有**一处定义**（`Actors.cs:50` Red / `:53` Yellow 两个常量），
        //        箭头与弹字共用同一份，不是抄两份 ✓ 将来改弹字配色，箭头自动跟着变 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// v182：逐帧把"成熟 / 缺水"两个箭头摆到这一格旁边。
        ///
        /// ★ 位置怎么分（成熟 vs 缺水）★
        ///   · 成熟 → **正上方**，居中（`x = plot.World.x`），比缺水高一档
        ///   · 缺水 → **左侧偏上**（`x = plot.World.x − tile×0.55`），略低于成熟那支
        ///   两支都是**尖端朝下**指着植物 ✓ 两者同时成立也不会重叠（一上一下、一左一中）✓
        ///   抬到 `tile × 0.72`（= 作物最大高度）之上 —— 这一项是刻意的：
        ///   水壶激光 / 肥料爆炸 / 虫怪 / 商店车都在同一片场地上，箭头埋进模型里就白做了 ✓
        ///
        /// ★ 常驻语义 ★
        ///   条件成立：**每帧**都在（`SetActive(true)` + 摆位置）
        ///   条件消失：**当帧**隐藏（`SetActive(false)`）——没有 TTL、没有淡出、不留残影 ✓
        /// </summary>
        private void SyncStatusArrows(int i, FarmPlot plot, float tile)
        {
            if (plot == null) return;
            bool haveMature = _matureArrows != null && i < _matureArrows.Length;
            bool haveThirst = _thirstArrows != null && i < _thirstArrows.Length;
            if (!haveMature && !haveThirst) return;

            // 作物顶点高度：与 `SyncPlots` 里 `show.ScaleToHeight(tile * 0.72f * …)` 同一把尺子 ✓
            float topY = tile * 0.72f;

            bool mature = plot.IsRipe;
            bool thirsty = plot.State == PlotState.Growing && plot.Thirsty;

            if (haveMature)
            {
                PlaceStatusArrow(_matureArrows[i], mature,
                    new Vector3(plot.World.x, topY + 0.75f, plot.World.y));
            }
            if (haveThirst)
            {
                PlaceStatusArrow(_thirstArrows[i], thirsty,
                    new Vector3(plot.World.x - tile * 0.55f, topY + 0.42f, plot.World.y));
            }
        }

        /// <summary>
        /// v182：开/关一支箭头。**不建不删**，只切 `activeSelf` 与写位置。
        /// 隐藏时不挪位置：省一次赋值，也避免"隐藏中的对象还在动"这种噪声。
        /// </summary>
        private static void PlaceStatusArrow(GameObject go, bool on, Vector3 pos)
        {
            if (go == null) return;
            if (on)
            {
                go.transform.position = pos;
                if (!go.activeSelf) go.SetActive(true);
            }
            else if (go.activeSelf)
            {
                go.SetActive(false);      // 条件消失 ⇒ 当帧就没了（不是淡出）✓
            }
        }

        /// <summary>
        /// v182：建一支箭头 GameObject（建好即隐藏）。
        ///
        /// ★ 颜色来源是这里最关键的一行 ★
        ///   `PopupStyle.ColorOf(kind)` —— 与 `RunSession.OnFloatingText`（`:1792`）**同一个函数** ✓
        ///   ⇒ 成熟 = `PopupKind.PlantMature` → `Yellow` = (1.00, 0.88, 0.22)
        ///     缺水 = `PopupKind.PlantThirst` → `Red`    = (1.00, 0.24, 0.22)
        ///   （两个常量定义在 `Actors.cs:50` / `:53`，是 `PopupStyle` 的 private 字段，
        ///     对外只经 `ColorOf` 暴露 ⇒ 我按**唯一的公开出口**取，而不是另抄一份 RGB ✗）
        ///
        /// ★ 材质复用 ★ 走 `VfxMatCached` 的静态缓存（`MatCache` 那一族的 Vfx 版）：
        ///   同 (core, rim, coreBoost) 只 `new Material` **一次**，之后 36 格共用同一份 ✓
        ///   这里刻意传 `VfxCoreBoostFlat`(=1.0) 而不是默认 1.35：
        ///   `SeedfallVfx` 的 `col = _BaseColor.rgb * _CoreBoost`，1.35 会把黄色推到 (1.35,1.19,0.30)
        ///   再被截到 (1,1,0.3) —— **色相变了**，就不再"和弹字同色"了 ✗ 1.0 才是原色 ✓
        ///   rim 取 core 压暗一半（`_RimColor.a` 也压到 0.45），只给一点霓虹边，不改变读到的色相 ✓
        /// </summary>
        private GameObject MakeStatusArrow(PopupKind kind, string name, float tile)
        {
            Color c = PopupStyle.ColorOf(kind);
            Color rim = new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 0.45f);
            GameObject go = MeshObj(StatusArrowMesh(), VfxMatCached(c, rim, VfxCoreBoostFlat), name);
            // 大小跟着格距走：`tileSize` 是玩法参数（tuning.json），箭头是表现 ⇒ 按它缩放，
            // 不写死米数（本工程格距历史上从 1.6 变到 2.5，写死就会在下次改格距时错位）
            // 乘 1.35：实测 1.0 倍时箭头在 60° 俯视下只有约 40 像素，压在霓虹描边上就看不出来；
            // 1.35 倍后高约 1.15m（作物本身 1.8m）⇒ 一眼能认出是哪一格，又不至于盖住作物 ✓
            float k = (tile > 0f ? tile : 2.5f) * 1.35f;
            go.transform.localScale = new Vector3(k, k, k);
            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// v182：箭头网格 —— **4 个顶点的小金字塔，尖端朝下**（`Cull Off` ⇒ 背面也画）。
        /// 按"格距 1 米"建模，实际大小由 `localScale = tile` 缩放（见上）。
        ///
        /// ★ 为什么是"金字塔"而不是一张平面三角形 ✗ ★
        ///   平面三角形的法线只有一个方向，而本工程的相机不止一个角度：
        ///     · 正常游玩机位 `tuning.camera.pitch = 60°`（从 −Z 斜上方看）
        ///     · 诊断/拍图用的 `DebugApi.FocusFarm` 是 **88° 近乎垂直俯视**
        ///   平面三角形在 88° 俯视下只剩一条边 —— 看着像"没画出来"✗
        ///   金字塔的 4 个面朝向不同 ⇒ **任何角度至少有一个面朝向相机** ✓
        ///   顶点数仍是 4（尖端 + 3 个底角），符合"小的实心箭头"这条要求 ✓
        ///
        /// 面片展开走 `AddTri`（每面独占 3 顶点 ⇒ `BuildFlat` 重算法线后是**硬边**，
        /// 与场上其它特效颗粒同一套低面数语言 ✓）。
        /// 走 `_fxMeshCache`：键 5400 与既有 5100+ / 5500 / 5620-… 都不撞 ⇒ 全程只建一次 ✓
        /// </summary>
        private static Mesh StatusArrowMesh()
        {
            const int key = 5400;
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            // 尖端（朝下 = 指着下面的植物）与底面三个角（朝上，形成一个"箭头"轮廓）
            Vector3 tip = new Vector3(0f, -0.20f, 0f);
            Vector3[] b =
            {
                new Vector3(0f, 0.14f, 0.155f),
                new Vector3(-0.134f, 0.14f, -0.078f),
                new Vector3(0.134f, 0.14f, -0.078f),
            };
            var tri = new List<Vector3>();
            for (int i = 0; i < 3; i++)
            {
                Vector3 a0 = b[i], a1 = b[(i + 1) % 3];
                AddTri(tri, tip, a0, a1);      // 侧面
                AddTri(tri, a1, a0, tip);      // 反向面（Cull Off 下不影响观感，只为法线朝向健全）
            }
            AddTri(tri, b[0], b[1], b[2]);     // 顶盖

            Mesh m = BuildFlat("StatusArrow", tri);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>
        /// `SetColor` 专用的 MaterialPropertyBlock（复用它，**不逐帧 new**）。
        /// `MaterialPropertyBlock` 是托管对象但 `SetPropertyBlock` 会把值拷进渲染器，
        /// 所以一份可以反复用 ✓ 同理不放进 `MonoBehaviour` 的字段初始化器也不会踩
        /// "CreateImpl 不许在构造期调用"那条（它不 CreateImpl，见 v207 的事故记录）✓
        /// </summary>
        private static readonly MaterialPropertyBlock _setColorMpb = new MaterialPropertyBlock();

        private static void SetColor(GameObject go, Color c)
        {
            if (go == null) return;
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            // ★★ v208：**只写 MaterialPropertyBlock，绝不写 `sharedMaterial`** ★★
            //
            // 旧实现是：
            //     Material m = r.sharedMaterial;
            //     if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            //     m.color = c;
            // 而 `sharedMaterial` 在"卡通材质池"语义下是**全场共用**的一份
            // （`ToonLook.Get` 按 (颜色,描边,simple) 缓存，同色同宽的所有对象都指向同一个 Material）
            // ⇒ 这里改一次，**全世界跟着变** —— 正是"改了耕地一格、整片田都变"的机制 ✗
            //
            // ── 实测：它今天是**惰性**的，但这条隐患必须现在就封掉 ──
            // 传进来的 `_plotTiles[i]` 是 `ModelView` 的根（`MDL_farm_plot`），
            // 而 Renderer 挂在子节点 `ANIM_farm_plot/...` 上 ⇒ `GetComponent<Renderer>()` 返回 null
            // ⇒ 这个函数在耕地上**一次都没生效过**（v208 探针读数：`M_Toon_B08A5A_27mm`
            //   的 `_BaseColor` 在这 20 秒里 (0.69,0.54,0.35) → (0.69,0.54,0.35)，纹丝不动）。
            // 也就是说：**耕地那套"空格/已挖/生长中"的地块配色是死的**。
            // ⚠️ 本轮**没有**顺手把它修活 —— 修活会让 `farm_plot` 的木框与松土被同一种颜色刷平
            //    （`Update` 只改一个 `_BaseColor`，而模型是"木框 + 松土"两个材质槽），
            //    观感会比现在更差。要修活必须走"两个槽分别取色"，那是另一件事 ✓
            //
            // 现在改成 MPB：**逐渲染器**覆盖 `_BaseColor`，共享材质一个字节都不动 ✓
            // 对每实例自有材质的图元（`Prim`）行为完全等价 ⇒ 观感零变化，隐患消失 ✓
            _setColorMpb.SetColor(BaseColorId, c);
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(ColorId)) _setColorMpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_setColorMpb);
        }

        /// <summary>
        /// v157：**商店车的表现层**（只读）。
        ///
        /// ★ 它不再持有状态机 ★ —— 状态机、清扫、交互都迁到了 `RunSession.TickShopTruck` ✓
        /// 理由：那些都在改会话/世界状态，属于逻辑 ✗ 而这里只做两件事：
        ///   ① 把车摆到 `PointAt(进度)` 的位置
        ///   ② 按前进方向设朝向
        ///
        /// 这就是本工程既有的分层：`RunSession`/`World` 是纯逻辑，`WorldView` 是表现 ✓
        /// 我上一轮把状态机放进这里，是分层错误 —— 症状是"表现层改 `World.Enemies`" ✗
        /// </summary>
        private void SyncShopTruck(RunSession s, float dt)
        {
            if (_truckModel == null || s == null) return;
            ShopTruck t = s.Truck;
            if (t == null) return;

            // ★★ v306 纯插入守卫：训练场**完全不显示**商店车（第二道，见 `RunSession.TickShopTruck`）★★
            //
            //  ── 守卫为 false（= 正常战斗）──
            //    `TrainingMode.NoShopTruck` 初值 false，只由 `TrainingMode.Enable()` 置真，
            //    而 `Enable()` 的唯一调用者是训练场景里的 `TrainingBootstrap.Awake`
            //    ⇒ 战斗模式下这一段等于 `if (false) { … }` ⇒ 下面每一行原样执行 ✓
            //
            //  ── 守卫为 true（训练场）──
            //    第一道守卫已经让 `t.Current` 恒为 `Idle`（下面那行 `visible` 自会算成 false）。
            //    这一段是**结构性**的第二道：即便将来有别的路径把 `Current` 推进到非 Idle
            //    （调试入口 `ForceShopForDebug` 之类），车也仍然是隐藏的 ——
            //    用户的验收口径是"**不要出现**商店车"，不是"车不在正确的位置" ✓
            //    已经在隐藏态时**不写 `SetActive`**（避免每帧一次引擎调用）✓
            if (TrainingMode.NoShopTruck)
            {
                if (_truckModel.Go.activeSelf) _truckModel.Go.SetActive(false);
                return;
            }

            bool visible = t.Current != ShopTruck.Phase.Idle;
            if (_truckModel.Go.activeSelf != visible) _truckModel.Go.SetActive(visible);
            if (!visible) return;

            // 路径是归一化坐标（-1..1），乘场地半宽得到世界坐标 ✓
            // ★ 必须与 `RunSession.TickShopTruck` 用**同一个取位入口** ★ ——
            //   否则会出现"看得见的车在 A、能交互的车在 B" ⇒ 界面在撒谎 ✗
            //   停靠期 `SamplePoint` 返回 `DockPoint`（车真的停住），
            //   而 `PointAt(PhaseT)` 会让它又跑一整圈 ✗（见 `ShopTruck.SamplePoint`）
            //
            // ★ v175：改走 `RunSession.TruckWorldPos`（会话层的**唯一**取位属性）★
            //   新时序给车加了**高度**（停靠期悬在战场正上方 6m ✓）⇒ "车在哪"从此是 3D 的 ✗
            //   若这里仍自己拼 `new Vector3(np.x*half, 0f, np.y*half)`，
            //   就会变成"车模型贴地、而 HUD 的浮动「E」悬在半空"这种**两个位置**的假象 ✓
            //   ⇒ 位置算式只留一处（`ShopTruck.SampleWorld`），本方法与 HUD 都读它 ✓
            // ★ v179：朝向改走 `ShopTruck.Facing` ✗ 不再用 `DirAt(SampleT(...))` ✗
            //   `DirAt` 只是**路径切线**，而落场之后入场/离场都**不走 `Path`**（各一条直线）✓
            //   ⇒ 再用 `DirAt` 会让车在离场时仍按路径切线扭头（"一边开走一边转向"）✗
            //   而用户要的是"**直接向左转向、开走**"⇒ 离场期朝向恒定 -X ✓
            _truckModel.Go.transform.position = s.TruckWorldPos;

            // 车头朝 Unity +Z（与 Characters/ 一致），SetFacing 组合出朝向 ✓
            Vector2 d = t.Facing(s.Clock);
            if (d.sqrMagnitude > 0.001f)
                _truckModel.SetFacing(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
        }

        private ModelView _truckModel;

        /// <summary>
        /// ★ v174：某一把武器"几秒出手一次" ✗ —— 与玩法层**同一个式子** ✓
        ///
        /// ── 式子从哪来 ✗（★ 不是我在这里另定的 ★）──
        /// `RunSession` 里玩家两处出手都写这一式：
        ///   · 锄头 `SwingHoe`     `:3635`  `WeaponCds[Hoe] = hoe.cooldown ÷ (AttackSpeed × (1+插件攻速))`
        ///   · 水壶/肥料 `UseTool` `:3959` / `:3968` 同一式 ✓
        /// 武器框上那句 `攻速 N.NNs/次` 也是它（`ImGuiHud` 的工具行 `:1736`）✓
        /// ⇒ 用户读到的 `0.70 / 0.80 / 1.20` 就是它 ✗ 表现层照抄它才叫"对得上"✓
        ///
        /// ⚠️ 为什么**不**直接读 `p.WeaponCds[slot]` ✗ ★（★ 那看着更"权威" ✗ 实际是错的 ★）★
        ///   · 水壶走的是**持续激光**（`TickBeam` ✗ 每 `0.5s` 扣一次水 ✗ `:3789`），
        ///     它【从来不写】`WeaponCds[Can]` ⇒ 那格恒为 `0`
        ///     ⇒ 读它会退成 1 倍原速 ✗（`RunSession.UseTool` 的注释里写着那段"水壶点按"是死代码 ✓）
        ///   · 而 `WeaponCds` 在 `PlayerActor.Tick` 里**每固定帧递减**（`Actors.cs:337`）✗
        ///     ⇒ 读到的是"interval − 已经过去的那几帧" ✗ 随帧序漂 ✓
        ///   两条合起来：读它会把稳定的 `0.80s` 变成"有时 0 有时 0.78"✓
        ///
        /// ⚠️ 攻速**属性**与**插件**都要算进去 ✗ ★（★ 漏了就是又造一个不一致 ★）★
        ///   玩家把攻速堆上去之后出手变快、动作不变 ⇒ 那正是本任务要修的那个现象 ✓
        /// </summary>
        /// <summary>
        /// ★★ v204：**攻击动画的最短时长（秒）** ★★
        ///
        /// 用户原话："玩家攻击动画优化，随攻速加快，且每次攻击可以重置动画，保证永远能播放"
        ///
        /// ── 为什么必须有下限 ──
        ///   动画时长 = `tool.cooldown / 攻速`（见 `AttackSeconds`）。攻速是**属性**，可以堆到很高：
        ///   攻速 10 ⇒ 锄头 0.070s、攻速 20 ⇒ 0.035s ⇒ 60fps 下一帧就播完（0.0167s × 2 帧），
        ///   屏幕上只剩"毫秒级的一闪" ⇒ 玩家读到的信息是"**根本没在打**"✗
        ///   0.06s ≈ 60fps 下 3.6 帧 —— 这是"看得出有前摇→砸下"的下限（再短就只剩一帧跳变）✓
        ///
        /// ⚠️ 它**不改变出手节奏** ✗：出手间隔仍由 `WeaponCds = tool.cooldown / 攻速` 决定，
        ///    这里只夹"**表现**要播多久"。攻速极高时动画会**播不完就被下一刀重开**（见消费处的规则），
        ///    那正是"每次攻击重置"这条要求的直接结果 —— 看得见每一刀的开头，比"等它播完"重要 ✓
        /// </summary>
        public const float MinAttackAnimSeconds = 0.06f;

        private static float AttackSeconds(RunSession s, PlayerActor p, int weaponIndex)
        {
            TuningDef t = s != null ? s.T : null;
            if (t == null || t.tools == null) return 0f;
            bool can = weaponIndex == PlayerActor.WeaponCan;
            bool fert = weaponIndex == PlayerActor.WeaponFertilizer;
            ToolDef tool = can ? t.tools.can : fert ? t.tools.fertilizer : t.tools.hoe;
            if (tool == null) return 0f;
            // ★ v179：插件**不再改出手间隔** ★
            // `WeaponModifiers` 的数值区（`AttackSpeedPct` 就在里面）已整体删除 ——
            // 阶段 3 的规格是"插件只改攻击方式，不改玩家属性"⇒
            // 出手间隔只由 `TuningDef` × `StatBlock` 决定（与装插件之前逐字相同）✓
            // 旧写法 `p.AttackSpeed * (1f + s.ModsFor(slot).AttackSpeedPct)` 现在编译不过，
            // 而被删掉的正是"插件也能加攻速"那条规则本身 ✓
            float fireRate = Mathf.Max(0.05f, p.AttackSpeed);
            // ★ v204：**夹一个下限**（见 `MinAttackAnimSeconds`）—— 攻速极高时动画不能一帧播完
            return Mathf.Max(MinAttackAnimSeconds, tool.cooldown / fireRate);
        }

        private void SyncPlayer(RunSession s, float dt)
        {
            World w = s.World;
            PlayerActor p = w.Player;
            if (p == null || _player == null) return;

            if (_playerModel != null)
            {
                Vector3 pos = new Vector3(p.Pos.x, 0f, p.Pos.y);
                bool moving = Moved(-1, pos, dt);
                // ★ v191·fx3 ②：移动轨迹的灰尘块 ★
                //   必须在**这里**调：`moving` 就在上一行算出来，而 `Moved()` 每帧只能调一次
                //   （它内部会覆写 `_lastPos`，调两次就永远读到"没动"）✓
                TickDust(dt, pos, moving);
                _player.transform.position = pos;
                // 模型正面是 Unity +Z（Blender -Y 经导出烘焙而来），SetFacing 组合出朝向
                Vector3 dir = new Vector3(p.AimDir.x, 0f, p.AimDir.y);
                if (dir.sqrMagnitude > 0.001f)
                    _playerModel.SetFacing(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);

                // ══════════════════════════════════════════════════════════════════
                // ★ v172 缺陷①③：换武器 ⇒ 手上的模型要换 + 播切换动作 ★
                //
                // ── 缺陷① 的根因 ──
                // `SetToolVisible` 全工程**只有** `AttachTools` 里那一次调用
                //（`SetToolVisible(i, i == 0)`，开局只开锄头），而 `AttachTools` 只在
                // `BuildPlayer` 里调一次 ✗
                // ⇒ `_tools[1]` / `_tools[2]` 永远 `SetActive(false)`
                // ⇒ 切到水壶/肥料，手上**仍然是锄头** ✓
                //
                // 修法：每帧比对武器索引，**变了才写** ✓
                // 不每帧无条件 SetActive：它不只是开销，还会在物体被重新激活时
                // 打断自身的动画/重置状态（本工程的模型带 SampleAnimation）✗
                // ══════════════════════════════════════════════════════════════════
                if (p.WeaponIndex != _shownWeapon)
                {
                    SetToolVisible(_shownWeapon, false);
                    SetToolVisible(p.WeaponIndex, true);
                    _shownWeapon = p.WeaponIndex;
                    // 攻击片段也跟着武器走（缺陷②的另一半）
                    _playerModel.SetAttackWeapon(p.WeaponIndex);
                    // 缺陷③：`player_swap` 播在这里 ✓
                    //
                    // 时机是**对的**，不是将就：`SwitchWeapon` 会把 `SwitchWindup` 置成
                    // `tuning.tools.switchWindup`（当前 0.5s）并禁止攻击 ⇒
                    // 切换动作正好覆盖这段"换武器前摇"✓
                    // 玩家因此**看得见**"在换武器、这会儿还打不了"，而不再是一段无反馈的硬直 ✓
                    _playerModel.PlayForced(ModelView.State.Swap);
                }

                // ══════════════════════════════════════════════════════════════════
                // ★ v172 缺陷②（断点 B）：消费"玩家出手"信号，播攻击动作 ★
                //
                // 三个攻击事件在 `FixedTick` 里投递（帧序在 `Sync` 之前），
                // 由 `OnMeleeSwing` / `OnBeam` / `OnFertTelegraph` 置位 `_pendingAttack` ✓
                //
                // ★★ v204：**判据从"上一段播完了吗"改成"这是一次新的出手吗"** ★★
                //
                // ── 改前的规则（只在一个方向上是错的）──
                //   `if (_playerModel.Current != ModelView.State.Attack)` ⇒ 上一段没播完就**整条吞掉**
                //   （连 `_pendingAttack = -1` 都吞，事件等于没发生过）✗
                //   它对**水壶**是对的：壶是按住持续的，`FireBeam` 每固定帧投递一次 ⇒ 无条件重开
                //   会把 `_stateTime` 永远钉在 0（看起来就是"不动"）✗
                //   它对**锄头 / 肥料**是错的：出手间隔 = 动画时长（两边都是 `cooldown / 攻速`），
                //   而 `ModelView` 要等 `_stateTime >= len` **才**回 Idle —— 下一刀的事件往往正好
                //   落在"还差半帧"的那一帧上 ⇒ 被吞 ⇒ 玩家看到"这一刀没动作"✗
                //   攻速越高、事件越密，被吞的概率越大 ⇒ 用户说的"攻击动画有时不播放" ✓
                //
                // ── 现在的规则（用户要求"每次攻击可以重置动画，保证永远能播放"）──
                //   · `_pendingAttackRepeat == false`（锄头 / 肥料 = **离散出手**）⇒ **无条件重开**：
                //     `PlayForced` 把 `_stateTime` 归零 ⇒ 上一次没播完也从头开始，
                //     "每一刀都有一段完整的前摇→砸下"由结构保证 ✓
                //   · `_pendingAttackRepeat == true`（水壶 = **按住持续**）⇒ 保留旧规则（播完再开），
                //     否则持续激光会把动画钉在第 0 帧 ✓
                //   ⇒ "永远能播放"与"水壶不卡死"两条同时成立，靠的是一个**事件语义**标记，
                //     而不是靠"时间上凑巧" ✓
                // ══════════════════════════════════════════════════════════════════
                if (_pendingAttack >= 0)
                {
                    bool startNew = !_pendingAttackRepeat
                                    || _playerModel.Current != ModelView.State.Attack;
                    if (startNew)
                    {
                        _playerModel.SetAttackWeapon(_pendingAttack);
                        // ★ v174：**攻击动画的真实时长 = 这一把的攻速** ★
                        //
                        // ★ 与那条「让位守卫」的关系（★ 这是本条最容易踩空的地方 ★）★
                        // 守卫在**下面**（`oneShot` 那段）：它保证 Attack/Swap 期间
                        // 不被每帧无条件的 `Play(Walk)` 掐掉 ✗
                        // ⇒ 攻击片段因此**能活到播完** ✓
                        // 而这里设的是"片段**播多快**"，不是"播哪一段"✗
                        // ⇒ 两者是**两件事**，倍率必须落在"守卫保住的这段生命期"之内才看得见 ✓
                        // ⇒ 顺序也必须是：**先** `SetAttackWeapon`（定下是哪一段 ✗ 长度才对 ✓）
                        //    **再** `SetAttackDuration`（按那段长度算倍率 ✓）
                        //    **最后** `PlayForced`（把 `_stateTime` 归零起播 ✓）
                        //    反了就会拿上一把的片段长度算倍率 ✗ 而它看起来**仍然在动** ✗
                        //    正是那种"改了没生效"最难查的形态 ✓
                        float secs = AttackSeconds(s, p, _pendingAttack);
                        _lastAttackAnimSeconds = secs;
                        _playerModel.SetAttackDuration(secs);
                        _playerModel.PlayForced(ModelView.State.Attack);
                        // ★ v204：给探针/自检留一个**可复核的计数**（"这条分支真的走了几次"）
                        _playerAttackStarts++;
                        _attackAnimStarts++;
                    }
                    _pendingAttack = -1;
                    _pendingAttackRepeat = false;
                }

                // ══════════════════════════════════════════════════════════════════
                // ★ v172 缺陷④：Walk 的播放倍率跟着**移速**走（修滑步）★
                //
                // 现象：升级项 `MoveSpeed +3%` / 上限 `caps.moveSpeed 9.0` 都会改移速，
                // 而动画恒按 1 倍速推进 ⇒ 位移变快、脚步不变 = 滑步 ✓
                //
                // 基准**从数据读**（`tuning.player.moveSpeed`），不硬编码 ✗
                // 硬编码会在策划改基准值之后**静默播错速**（而且看起来仍然"动了"，
                // 比完全不动更难发现）✓
                // `MoveSpeed` 的初始值就取自同一个 `player.moveSpeed`
                //（`Stats` 用 `PlayerBaseDef.moveSpeed` 填 `StatId.MoveSpeed`）
                // ⇒ 开局比值恒为 1，不会一进游戏就变速 ✓
                float baseSpeed = w.Tuning != null && w.Tuning.player != null ? w.Tuning.player.moveSpeed : 0f;
                _playerModel.SetPlaybackSpeed(baseSpeed > 0.01f ? p.MoveSpeed / baseSpeed : 1f);

                // ★ v180：把"这一帧在不在移动"喂给分层动画 ★
                // 攻击期间下半身由 locomotion 驱动：站着取 Idle、跑动取 Walk。
                // ⇒ 攻击本身**对下半身零贡献**（不再把腿强行压回站立 = 不再卡脚 / 滑步）✓
                _playerModel.SetLocomotion(moving);

                // ★ v246：**战斗侧的脚步灰尘**（与主城同一个 `TownDust`，1 份材质 + 1 份 Quad 网格）★
                //   接口是主城那轮就留好的：`TownDust.Tick(脚底位置, 是否在移动, dt)`
                //   第一次调用会自己懒建池（24 个预建、循环复用）；`_root` 是战斗场景的根 ✓
                TownDust.Build(_root);
                TownDust.Tick(new Vector3(p.Pos.x, TownAvatar.GroundY, p.Pos.y), moving, dt);

                _playerModel.Tick(dt);
                // v85：玩家**不要**用 CenterOn。
                //
                // 我一度给它加过 CenterOn（想对齐几何中心），结果模型被推到 (44.3, -25.3)——
                // 场地只有 ±20，直接飞到墙外。原因：CenterOn 依赖
                // "MeshLocalCenter = 网格包围盒中心换算到根节点本地空间"这个假设，
                // 它对**单网格模型**成立（作物、耕地都靠它修好了），
                // 但对**木偶层级**不成立——各关节节点分散摆放、还各带自己的缩放，
                // 累加出来的"中心"是个没有物理意义的大数（实测偏了约 46 单位）。
                //
                // 层级模型的根节点本来就在角色脚底原点，直接设 position 才是对的。
                _player.transform.position = pos;

                // ★ v172：Attack / Swap 是**一次性动作**，播完由 `ModelView` 自己回 Idle
                //（`ModelView.Tick` 里"一次性动作播完回待机"那段）★
                //
                // 在这里无条件 `Play(Walk)` 会把它们**当帧掐掉** ✗
                // 表现上完全等同于"片段没接上" —— 这正是缺陷②难查的原因之一：
                // 就算把片段接对了，这一行也会让攻击动作永远看不见 ✓
                // 所以只在 Idle / Walk 这两个**持续状态**之间做迁移 ✓
                ModelView.State cur = _playerModel.Current;
                bool oneShot = cur == ModelView.State.Attack || cur == ModelView.State.Swap;
                if (!oneShot)
                {
                    if (moving) _playerModel.Play(ModelView.State.Walk);
                    else if (cur == ModelView.State.Walk) _playerModel.Play(ModelView.State.Idle);
                }
                if (p.HitFlash > 0f) _playerModel.PlayForced(ModelView.State.Hit);
                // v88：受击膨胀脉动（闪白走下面的 SetTint）
                _playerModel.SetHitFlash(p.HitFlash > 0f, dt);
                // ★ v198：闪避（`PlayerActor.EvadeGhost` 那 0.40s）喂给姿态层 ★
                // 它**不覆盖**虚化残影：残影是 `ImGuiHud.DrawEvadeGhost` 用 `GUI.color` 画的第二个我，
                // 这里只让本体前倾一下 —— 两件事叠加，谁也没抢谁 ✓
                _playerModel.SetEvade(p.EvadeGhost);

                Color mul = Color.white;
                if (p.HitFlash > 0f) mul = Color.white * 1.7f;
                else if (p.IsInvulnerable) mul = Color.white * 1.25f;
                _playerModel.SetTint(mul);
                return;
            }

            _player.transform.position = new Vector3(p.Pos.x, 0.5f, p.Pos.y);
            Vector3 dir0 = new Vector3(p.AimDir.x, 0f, p.AimDir.y);
            if (dir0.sqrMagnitude > 0.001f)
                _player.transform.rotation = Quaternion.LookRotation(dir0, Vector3.up);
            // 玩家受击闪白 / 无敌帧提示
            if (_playerMat != null)
            {
                Color pc = new Color(0.35f, 0.62f, 0.95f);
                if (p.HitFlash > 0f) pc = Color.white;
                else if (p.IsInvulnerable) pc = Color.Lerp(pc, Color.white, 0.35f);
                if (_playerMat.HasProperty(BaseColorId)) _playerMat.SetColor(BaseColorId, pc);
                if (_playerMat.HasProperty(ColorId)) _playerMat.SetColor(ColorId, pc);
            }
            if (_aimTip != null)
                _aimTip.transform.position = new Vector3(p.Pos.x, 0.6f, p.Pos.y) + dir0.normalized * 1.3f;
        }

        // ★ T2（2026-10）：植物**表现层**整段删除 ✗ ★
        //
        // 删掉的是三样（它们只服务"场上会参战的植物帮手"这一个东西）：
        //   ① `const float AllyPlantHeight = 1.70f` —— 帮手显示身高
        //   ② `NearestEnemyDir(w, pos, out hasFoe)` —— 帮手的**索敌朝向**辅助
        //   ③ `SyncPlants(w, dt)` —— 帮手模型的生成 / 移动 / 动画 / 受击脉动 / 淡出
        //
        // 判据（与本轮的其余删除同一条）：`World.Plants` **已无生产者**（`SpawnPlantFromPlot` 已删）✓
        //   ⇒ `SyncPlants` 的 `for` 循环体**永远不会执行** ⇒ 上面三样全是死代码 ✓
        //   ⇒ 连带 `_plants` / `_plantModels` 两个字典也失去全部读写者 ⇒ 一并删除 ✓
        //
        // ⚠️ **没有**连带删除的（都还有别的读者，属于别的地界 ✓）：
        //   · `RemoveStale` / `RemoveStaleModels` —— 敌人同步（`SyncEnemies`）仍在用 ✓
        //   · `ModelView.Spawn("Plants", ...)` —— `SyncPlotCrops` 画**地块上的作物**仍在用 ✓
        //     ★ 注意区分：作物长在 `FarmPlot` 上（新玩法），帮手是 `PlantAlly`（旧玩法）★
        //   · 「植物」这个玩法概念**没有消失** —— 它现在是地块上的作物（`FarmPlot` / `PlantMechanics`）✓
        //     消失的只是"会移动、会索敌、会被打的植物**实体**" ✓

        /// <summary>模型字典的同步清理（和 RemoveStale 同一思路，只是元素是 ModelView）。</summary>
        private void RemoveStaleModels<T>(Dictionary<int, ModelView> map, List<T> live, System.Func<T, int> idOf)
        {
            if (map.Count == 0) return;
            _scratch.Clear();
            foreach (var kv in map) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++)
            {
                int id = _scratch[i];
                bool found = false;
                for (int j = 0; j < live.Count; j++)
                {
                    if (idOf(live[j]) == id) { found = true; break; }
                }
                if (!found) map.Remove(id);
            }
        }

        private void SyncEnemies(World w, float dt)
        {
            // ★ v181：虫怪接手（**唯一的挂钩点，一行**）★
            //   `ProceduralBugs.SyncEnemies` 把 `w.Enemies` 全部画成虫、并把死掉的还回对象池；
            //   返回 false 只在**首次**调用时 `Seedfall/Toon` 着色器还没就绪 ⇒ 落到下面原来那套
            //   FBX / 图元路径，不把画面赌在一个新系统上 ✓（判断是一次性的，不会来回切）
            if (ProceduralBugs.SyncEnemies(_root, w, dt, CharScale))
            {
                // ★ v186：虫怪接手时，BOSS 入场的缩放由**这一支**负责（见 `SyncBossEntranceBug`）★
                //   原因：`return` 之后 WorldView 自己那段敌人同步整段不执行 ⇒
                //   挂在那边（`SyncBossEntrance`）的入场动画**对真机上的 BOSS 一格都放不出来** ✗
                //   实测依据：抓图 `boss_B_start`（刷出后 63ms）里 BOSS 已是满体型 —— 说明缩放没生效 ✓
                SyncBossEntranceBug(w);
                return;
            }
            Vector3 playerPos = w.Player != null ? new Vector3(w.Player.Pos.x, 0f, w.Player.Pos.y) : Vector3.zero;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];

                // ★★ v301：训练场的稻草人 ⇒ **本文件也不给它画身体** ★★
                //   病因（读代码即可复现）：`EnemyModelId(EnemyKind.Wander)` 返回 `"pig"`
                //   （WorldView.cs:165）⇒ 下面会为稻草人建一只**小猪模型**，并在
                //   `_enemyModels` 那一支里每帧驱动它的动画 / 霓虹 / 受击闪白 ✗
                //   而稻草人的身体由 `TrainingBootstrap.BuildScarecrow` 程序化绘制
                //   ⇒ 不跳过的话训练场里是"小猪 + 稻草人"两份身体叠在同一格上 ✗
                //
                //   为什么上一轮只在 `ProceduralBugs` 加了守卫（ProceduralBugs.cs:3009）还不够：
                //   本文件是**兜底路径** —— 上面第 2303 行 `ProceduralBugs.SyncEnemies(...)`
                //   返回 false（`Seedfall/Toon` 着色器没就绪 / 缺失）时，控制流就落到这个 for 循环，
                //   而那条守卫在 `ProceduralBugs` 内部，这里一行都不会执行 ✗
                //
                //   关掉开关时（战斗场景 `HideDummyBody == false`）这一行**不成立**，
                //   下一个语句仍是原来的 `Vector3 pos = ...` ⇒ 战斗行为逐字节相同 ✓
                if (TrainingMode.HideDummyBody && TrainingMode.IsDummyId(e.Def.Id)) continue;

                Vector3 pos = new Vector3(e.Pos.x, 0f, e.Pos.y);

                if (!_enemies.TryGetValue(e.Id, out GameObject go))
                {
                    ModelView mv = null;
                    string modelId = EnemyModelId(e.Def.Kind);
                    if (modelId != null) mv = ModelView.Spawn("Characters", modelId, _root);
                    if (mv != null)
                    {
                        // 模型已按玩法尺度建模（小恶魔 0.67m / 大恶魔 1.47m），
                        // 这里只乘一个统一的显示放大系数——碰撞半径不受影响。
                        _enemyModels[e.Id] = mv;
                        go = mv.Go;
                        // ★ v177 清理 ✗：这里曾叠了两个 `SetScaleFactor(CharScale)` ✗
                        //   而它们都被下面那处【与单位无关】的 `ScaleToHeight` 覆盖 ✓
                        //   ⇒ 留着会让下一个读代码的人以为"缩放由 CharScale 决定"✗ 而那是错的 ✓
                        // ★★ v177 敌人专属修 ✗：上面那行在"根缩放被压回 1"之后会让敌人【小 100 倍】★★
                        //
                        // 机制：`SetScaleFactor(k)` 给的是 `localScale = BaseScale × k` ✗
                        //   而 `BaseScale` 在 `ModelView.Spawn` 里已被压成 1（它把 FBX 根上的 100 抹掉了）✗
                        //   ⇒ 世界高 = 网格自身高度 × k ✗ 而敌人网格是 1/100 存储 ⇒ **只有几厘米 ⇒ 整片看不见** ✓
                        //   而【作物不受影响】✗ 因为它接着走 `ScaleToHeight` ⇒ 被重新缩放到目标高 ✓
                        //
                        // 所以这里改用【与单位无关】的入口：`ScaleToHeight(h)` 保证世界高恰好 h ✓
                        //   ⇒ 传它自己的米制高 × CharScale ⇒ 得到"模型米制高 × 2.60" ✓
                        //   ⇒ 那正是 `SetScaleFactor(CharScale)` 原本想要的 ✓ 而与根缩放是否被抹掉无关 ✓
                        //
                        // ★ 只改敌人这一处 ✗ —— 玩家 / 装饰 / 栅栏走的是各自的入口 ✓ 不动 ✓
                        // ★ v177 清理 ✗：这里曾叠了两个 `SetScaleFactor(CharScale)` ✗
                        //   而它们都被下面那处【与单位无关】的 `ScaleToHeight` 覆盖 ✓
                        //   ⇒ 留着会让下一个读代码的人以为"缩放由 CharScale 决定"✗ 而那是错的 ✓
                        // ★★ v177 真因 ✗ 实测（★ 不是猜 ✓）★★
                        //   敌人模型【看得见但只有几厘米】✗ 而读数显示一切"正常"：
                        //     `MDL_imp`  scale = 2.6  ✗  `MDL_player` scale = 1.8584  ✗
                        //     `ANIM_imp` scale = 1    ✗（玩家同结构 ✓）
                        //   ⇒ 缩放与外层结构【都没问题】⇒ 问题在【网格本身多大】✓
                        //   ⇒ 导入设置给出了答案 ✗：
                        //     `Anim\imp_idle.fbx.meta`    → `useFileScale: 1`  ✗ 用 FBX 自带单位（厘米）
                        //     `Anim\player_idle.fbx.meta` → `useFileScale: 0`  ✓ ⇒ 玩家是米制 ⇒ 可见
                        //   ⇒ 所以敌人网格只有 1/100 大 ⇒ 2.6 × 0.01 ≈ **2.6 厘米** ⇒ 看不见 ✓
                        //
                        // ★ 正解在资源层 ✗ 已改那 4 个敌人 FBX 的 meta（`useFileScale: 0`）✓
                        //   ⇒ 所以这里【保持原样】✗ 不要乘 100（★ 否则会变成 100 倍大 ✓）
                        //     正解已落在资源层：那 4 个敌人 FBX 的 meta 改成 `useFileScale: 0` ✓
                        //   敌人网格走的是【`useFileScale: 1`】⇒ Unity 按 FBX 自带单位（厘米）缩成 1/100：
                        //     `Anim\imp_idle.fbx.meta`   → `useFileScale: 1`  ✗
                        //     `Anim\player_idle.fbx.meta`→ `useFileScale: 0`  ✓ ⇒ 玩家是米制 ⇒ 可见
                        //   ⇒ 世界尺寸 = 网格(1/100) × localScale(2.6) ≈ **2.6 厘米** ⇒ 看不见 ✓
                        //   而两者的【根缩放都是 1】⇒ 任何"根缩放判据"都动不了它 ✓
                        // ⇒ 所以这里补回那 100 倍（与 `useFileScale` 的作用同一件事）✓
                        //   ★ 根上的正解是改那 4 个敌人 FBX 的 meta（`useFileScale: 0`）⇒ 但那要重新导入 ✓
                        //     这里先用代码补 ✗ 只影响敌人 ✓
                        mv.ScaleToHeight(mv.RawHeight * CharScale);
                        // v76：只有真正的敌人才套霓虹红描边。
                        // 小猪（Wander）是中立的肥料来源，给它红边会让玩家以为要打它。
                        // ★ v208：敌人 = **最粗最亮**那一档（`NeonPxChar` / `NeonBoostChar`）★
                        // 用户原话"敌人的高亮红色霓虹光描边没加，要清晰可见，保证突出敌人" ✓
                        if (e.Def.Kind != EnemyKind.Wander) mv.SetNeon(NeonEnemy, 0f, NeonPxChar, NeonBoostChar);
                        else mv.SetNeon(new Color(0f, 0f, 0f, 0f));   // 关掉霓虹
                    }
                    else
                    {
                        go = Prim(e.Def.Kind == EnemyKind.Boss ? PrimitiveType.Cube : PrimitiveType.Capsule,
                            "Enemy_" + e.Def.Id + "_" + e.Id, Vector3.zero, Vector3.one, Mat(e.Def.Color));
                    }
                    _enemies[e.Id] = go;
                }

                bool moving = Moved(e.Id, pos, dt);
                if (_enemyModels.TryGetValue(e.Id, out ModelView view))
                {
                    go.transform.position = pos;                       // 模型脚底在 y=0，直接贴地
                    // v80：小猪（Wander）是**中立单位**，它不追玩家——让它面向"追玩家"是错的，
                    // 看起来像在盯着你。改成**永远面向自己的移动方向**，这才符合"在场上闲逛的动物"。
                    // 方向从上一帧位置推出来（_lastPos 已经在 Moved() 里维护），
                    // 不依赖任何敌人特有的速度字段，对任何 Actor 都适用。
                    float yawToPlayer = YawTo(pos, playerPos);
                    if (e.Def.Kind == EnemyKind.Wander)
                    {
                        Vector3 mv = _lastDir.TryGetValue(e.Id, out Vector3 d) ? d : Vector3.zero;
                        if (mv.sqrMagnitude > 0.0004f)
                            view.SetFacing(Mathf.Atan2(mv.x, mv.z) * Mathf.Rad2Deg);
                        // 没在动就保持上一帧朝向，不要归零（否则站着的小猪会抽风式乱转）
                    }
                    else
                    {
                        view.SetFacing(yawToPlayer);       // 敌人面向玩家（组合，不覆盖）
                    }
                    view.Tick(dt);
                    if (moving) view.Play(ModelView.State.Walk);
                    else if (view.Current == ModelView.State.Walk) view.Play(ModelView.State.Idle);
                    if (e.HitFlash > 0f) view.PlayForced(ModelView.State.Hit);
                    view.SetHitFlash(e.HitFlash > 0f, dt);   // v88：受击膨胀脉动
                    SyncBossEntrance(e, view);               // ★ v186：BOSS 入场（缩放/落地/复位）

                    // 状态染色改成"乘法"——见 ModelView.SetTint 的说明
                    Color mul = Color.white;
                    if (e.Status.Frozen) mul = new Color(0.55f, 0.85f, 1.35f);
                    else if (e.Status.Slowed) mul = new Color(0.72f, 0.92f, 1.18f);
                    else if (e.Status.Burning) mul = new Color(1.35f, 0.70f, 0.40f);
                    else if (e.Status.Poisoned) mul = new Color(0.70f, 1.20f, 0.55f);
                    float dark = Mathf.Lerp(0.45f, 1f, e.Health.HpPct);
                    mul *= dark;
                    if (e.HitFlash > 0f) mul = Color.Lerp(mul, Color.white * 1.7f, 0.85f);
                    view.SetTint(mul);

                    // ★ v209：**夜间发光** —— 只在夜里亮、白天归零 ★
                    // 系数来自 `NightLighting.NightGlow01`（= `DayNightCycle.Sample(t).Glow`）
                    // ⇒ 全场只有一个"天亮/天黑"判据，这里不再算一遍时间 ✓
                    // 小猪（Wander）没被设置霓虹 ⇒ `SetNightGlow` 内部直接早退，它不会被点亮 ✓
                    view.SetNightGlow(NightGlowEnabled ? NightLighting.NightGlow01 : 0f);
                }
                else
                {
                    float scale = e.Radius * 2f;
                    go.transform.localScale = new Vector3(scale, scale * 1.6f, scale);
                    go.transform.position = new Vector3(e.Pos.x, scale * 0.8f, e.Pos.y);

                    Color c = e.Def.Color;
                    if (e.Status.Frozen) c = Color.Lerp(c, new Color(0.55f, 0.8f, 1f), 0.7f);
                    else if (e.Status.Slowed) c = Color.Lerp(c, new Color(0.55f, 0.8f, 1f), 0.35f);
                    else if (e.Status.Burning) c = Color.Lerp(c, new Color(1f, 0.5f, 0.15f), 0.5f);
                    else if (e.Status.Poisoned) c = Color.Lerp(c, new Color(0.5f, 0.9f, 0.3f), 0.5f);
                    c *= Mathf.Lerp(0.5f, 1f, e.Health.HpPct);
                    if (e.HitFlash > 0f) c = Color.Lerp(c, Color.white, 0.85f);   // 受击闪白
                    SetColor(go, c);
                }
            }
            RemoveStale(_enemies, w.Enemies, a => a.Id);
            RemoveStaleModels(_enemyModels, w.Enemies, a => a.Id);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  v183：BOSS 血条 —— 世界空间 / 悬在头顶 / 面向相机
        //
        //  ★ 它是**世界空间**的一组 MeshRenderer，不是 IMGUI 覆盖层 ★
        //    ⇒ 相机推拉转动时它跟着 BOSS 走，和屏幕固定的 HUD 毫无关系 ✓
        //
        //  ★ 朝向为什么选"面向相机(billboard)"而不是"朝上平铺" ★
        //    读得清才是唯一判据，而本工程**有两个机位**：
        //      · 正常游玩：`tuning.json` 的 `camera.pitch = 60°`
        //        （`SelfCheck.cs:513` 有一条断言「镜头与地面 60 度」把它钉住了）
        //      · 诊断拍图：`DebugApi.FocusFarm` 把机位压到 **88°** 近乎垂直俯视
        //    朝上平铺的四边形在 60° 下被压缩 sin60° ≈ 0.866（数字变扁），88° 下不压缩
        //    ⇒ **同一块牌在两个机位读到的大小不同** ✗ 而这条血条恰恰要被截图验收 ✗
        //    面向相机则任何机位都是 1:1 像素、文字永远正立（`camera.up` 就是屏幕向上）✓
        //    代价只有每帧一次 `rotation = cam.rotation`（一次结构体赋值，零分配）——
        //    比"按机位反算倾角"更省，也不会在相机插值中间帧抖动 ✓
        //
        //  ★ 复用（硬约束 5：只建一次、逐帧零分配）★
        //    · `Back` / `Fill` 两个物件 + 两段文字：**各建一次**；
        //      之后每帧只写 `localScale` / `position` / `rotation` / `SetActive` ✓
        //    · 网格：`_fxMeshCache[5700]`（左锚点四边形）**全局一份** ⇒ 运行期 0 次 `new Mesh` ✓
        //    · 材质：`VfxMatCached(...)` 的静态缓存 ⇒ 运行期 0 次 `new Material` ✓
        //    · 字体：`_bossBarFont` **全进程一份** ✓
        //    · 逐帧分配 = 0：血量字符串只在**整数血量变化**那一帧生成（稳态下不生成）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>BOSS 血条的全部引用（一个实例 = 一条血条，建一次后反复用）。</summary>
        private sealed class BossBar
        {
            public GameObject Root;
            /// <summary>填充条。**锚点在左边**、原点不动 ⇒ 只改 `localScale.x` 就是从左往右涨。</summary>
            public Transform Fill;
            public TextMesh HpText;
            public TextMesh NameText;
            public int BossId = int.MinValue;      // 换了一只 BOSS ⇒ 重测体型、重建血条
            public float Width, Height;            // 米
            public float HeadY;                    // 条底的世界高度 = 实测头顶 + 抬升
            public bool Shown;
            public int ShownHp = int.MinValue, ShownMax = int.MinValue;
            public string ShownName;
        }

        private BossBar _bossBar;
        private static Font _bossBarFont;          // 全进程一份（`Clear` 之后继续用）
        /// <summary>建这份字体时的语言代数（≠ `L10n.FontGeneration` ⇒ 下次取字体时重建）。</summary>
        private static int _bossBarFontGen = -1;
        private Camera _cam;

        // ★ v183：血条的**复用计数**（只给 `DebugApi.BossBarProbe` 读）★
        //   复用与"逐帧零分配"不能靠嘴说，要给出两个数与一个比值：
        //     `BossBarBuilds`    —— 血条建过几次（应当 ≪ 同步帧数：物件只建一次）
        //     `BossBarSyncFrames`—— 逐帧同步跑了多少帧
        //     `BossBarHpTextWrites` —— 血量字符串被重写几次（应当 ≈ 血量变化次数，而**不是**帧数
        //                               ⇒ 稳态下每帧 0 次 `string` 分配 ✓）
        public static int BossBarBuilds;
        public static int BossBarSyncFrames;
        public static int BossBarHpTextWrites;

        private static readonly Color BossBarBackColor = new Color(0.055f, 0.045f, 0.062f, 1f);
        private static readonly Color BossBarFillColor = new Color(0.93f, 0.13f, 0.10f, 1f);
        private static readonly Color BossBarHpColor = new Color(1.00f, 0.97f, 0.92f, 1f);
        private static readonly Color BossBarNameColor = new Color(0.92f, 0.86f, 1.00f, 1f);

        // ══════════════════════════════════════════════════════════════════════
        //  ★ 血条为什么用 `MatCached`（URP Lit / **写深度**）而不是 `VfxMatCached` ✗ ★
        //
        //  `VfxMatCached` 走 `Seedfall/Vfx`：Queue=Overlay、ZTest Always、**ZWrite Off**
        //  （那三条是为了"子弹永远画在最上层"，见 `SeedfallVfx.shader:6-13` 的说明）✓
        //  第一版血条就用了它 —— 结果 BOSS 的**墨线描边**被画在了血条上面 ✗
        //
        //  根因不在队列，而在**深度**：
        //    本工程的描边是全屏后处理（`ScreenOutlineFeature`，`renderPassEvent =
        //    AfterRenderingPostProcessing`，`ScreenOutlineFeature.cs:47`），
        //    它按 `_CameraDepthTexture` 找边缘 ⇒ 而 `ZWrite Off` 的物件**不进深度图** ✗
        //    ⇒ 血条那一片像素的深度里躺的仍是**它下面的 BOSS** ⇒ 描边照着 BOSS 的深度画墨线 ✓
        //    ⇒ 屏幕上就是"BOSS 的触角/上颚的墨线横穿血条"（第一版抓图里能直接看到）✓
        //  抬高条子躲开（试过 +1.15m）治不好：BOSS 一抬姿势墨线又追上来 ✓
        //
        //  改成 `MatCached`（不透明、`ZWrite On`、Queue=Geometry）之后：
        //    · 血条自己进深度图 ⇒ 描边在血条**边界**画一圈墨线（正好与本工程全体物件的墨线语言一致）✓
        //    · 血条像素的深度是血条自己 ⇒ BOSS 的墨线**不可能**再穿进条里 ✓
        //    · 填充条开 `emissive` ⇒ 夜里 `DayNightCycle` 压暗之后仍然读得清比例 ✓
        //  代价：血条不再是"永远置顶"的 Overlay —— 但它悬在 BOSS 头顶上方、后面没有任何几何体，
        //        本来就没什么能挡住它 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>条高 / 条宽。宽度由 BOSS 体型算（见 `MeasureBossBounds`），高度按这个比例派生。</summary>
        private const float BossBarHeightRatio = 0.145f;
        /// <summary>
        /// 条底离**实测头顶**的距离（米）—— 就是需求里的"在 Boss 顶端显示"。
        /// 0.60 ≈ 条高的 2/3：读起来像"长在它头顶上"，又不至于压住 BOSS 自己的墨线轮廓 ✓
        /// </summary>
        private const float BossBarLift = 0.60f;
        /// <summary>量不到模型时的兜底（本工程 BOSS 半径 2.23m ⇒ 宽 4.2 / 高 0.61）。</summary>
        private const float BossBarFallbackWidth = 4.2f;
        private const float BossBarFallbackHeadY = 3.2f;

        // ══════════════════════════════════════════════════════════════════════
        //  ★ v186：BOSS 入场（**本段之外一行都没动**）★
        //
        //  用户原话："为 boss 增加入场动画，boss 不再清除敌人" ✓
        //  逻辑侧只有两个**只读**量：`BossEnemy.InEntrance` / `BossEnemy.EntranceAge`
        //  （见 `Enemies.cs`；逻辑层不新增任何"表现计时器"）
        //  本段只组合**已经有的四件表现**：`ModelView.SetScaleFactor` / `Shake` /
        //  `SpawnShockRing` / `NightLighting.Illuminate` ⇒ 没有新起一套特效系统 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>入场起始缩放（贴地浮现，几乎从一点长出来）。</summary>
        private const float BossEntryStartScale = 0.08f;
        /// <summary>入场过冲（先长到比目标大 18%，再收回 —— "砸下来"的手感来自这一下）。</summary>
        private const float BossEntryOvershoot = 1.18f;
        /// <summary>过冲开始回收的进度（0~1）。也就是**落地冲击**发生的那一刻。</summary>
        private const float BossEntrySettle = 0.78f;
        private static readonly Color BossEntryCore = new Color(0.78f, 0.42f, 1f, 0.95f);
        private static readonly Color BossEntryRim = new Color(0.48f, 0.16f, 0.92f, 0.92f);

        /// <summary>正在播入场的那只 BOSS（按 `Id` 记；换 BOSS ⇒ 重新播）。</summary>
        private int _bossEntryId = int.MinValue;
        /// <summary>这一次入场的"落地"那一下放过没有（一次性）。</summary>
        private bool _bossEntryImpact;
        /// <summary>虫怪那一支里这只 BOSS 的根 `Transform`（名字是 `BUG_&lt;defId&gt;`，见下）。</summary>
        private Transform _bossEntryBug;
        private int _bossEntryBugId = int.MinValue;
        /// <summary>"找不到虫怪对象"只报一次（不刷屏）。</summary>
        private bool _bossEntryWarned;

        /// <summary>
        /// 入场的**缩放曲线**（0~1 的进度 → 缩放倍率）。
        /// 两条渲染路径（虫怪 / FBX）都读它 ⇒ "入场长什么样"只有一份定义 ✓
        /// </summary>
        private static float BossEntryScaleAt(float age)
        {
            float total = Mathf.Max(0.01f, BossEnemy.EntranceSeconds);
            float u = Mathf.Clamp01(age / total);
            float ease = 1f - Mathf.Pow(1f - u, 3f);                       // easeOutCubic
            float k = Mathf.Lerp(BossEntryStartScale, BossEntryOvershoot, ease);
            if (u > BossEntrySettle)                                        // 最后一段：收回 1.00
                k = Mathf.Lerp(k, 1f, Mathf.Clamp01((u - BossEntrySettle) / (1f - BossEntrySettle)));
            return k;
        }

        /// <summary>
        /// **落地**那一下（一次性）：震屏 + 一圈紫环 + 一盏紫光 + 地面紫标。
        /// 两条渲染路径共用 ⇒ 换渲染路径不会让"它来了"的手感变掉 ✓
        ///
        /// ★ 尺寸是按**实测抓图**收过的（v186 第二版）★
        /// 第一版取 `span = Radius × 3.4`（BOSS 半径 2.23m ⇒ 7.6m），冲击环再 ×1.7 ⇒ 直径 25m ——
        /// 抓图里那一圈直接铺满整个场地、把耕地与小怪全盖住 ✗
        /// 需求要的是"看得出来它来了"，不是"盖住整张图" ⇒ 收到 BOSS 体型的 1.5 倍左右 ✓
        /// </summary>
        private void PlayBossEntryLanding(EnemyActor e)
        {
            if (e == null) return;
            float span = Mathf.Max(1.6f, e.Radius * 1.5f);
            Vector3 at = new Vector3(e.Pos.x, 0.06f, e.Pos.y);
            FxSpec fx = default;
            fx.Valid = true;
            fx.Core = BossEntryCore;
            fx.Rim = BossEntryRim;
            fx.Scale = 1f;
            fx.Glow = 1.8f;
            fx.Shake = 1f;
            Shake(0.34f, 0.30f);                                              // 震屏
            SpawnShockRing(at, fx, span * 0.5f, span * 1.6f, 0.45f, 0.06f);    // 一圈扩散的环
            NightLighting.Illuminate(new Vector3(e.Pos.x, 0.85f, e.Pos.y),
                BossEntryRim, span * 3f, 8f, 0.45f);                           // 落地那一下照亮脚下
            AddMarker(e.Pos, span, 0.5f, new Color(0.62f, 0.26f, 0.95f, 0.75f)); // 地面紫标
        }

        /// <summary>
        /// ★ v186：**虫怪那一支的 BOSS 入场**（真机上走的就是这一支）★
        ///
        /// ★ 为什么必须有这一个方法 ★
        /// `SyncEnemies` 的第一句是 `if (ProceduralBugs.SyncEnemies(...)) return;` ——
        /// 虫怪着色器在位时**整段 WorldView 敌人同步都不执行** ⇒ 挂在那边的那段
        /// 入场动画在真机上一格都放不出来 ✗
        ///   实测：抓图 `boss_B_start`（刷出后 63ms）里 BOSS 已是满体型 ✓
        ///
        /// ★ 为什么只能靠"名字 + 位置"找它 ★
        /// `ProceduralBugs` 是 `Model/**`（本轮**不许改**）⇒ 没有"按敌人 id 取视图"的公开入口；
        /// 它建对象时用的是 `new GameObject("BUG_" + id)`（`ProceduralBugs.cs:566`），
        /// 由 `WorldView._root` 挂在手底下 ✓
        /// ⇒ 判据取**两个**：名字等于 `BUG_&lt;defId&gt;` **且** 世界位置与逻辑位置重合（0.3m 内）。
        ///   两个一起用才不会被"池里那只没复活的同名对象"骗到（回收时只是 `SetActive(false)`）✓
        /// ★ 找不到会怎样 ★ 只**少一次缩放**，冲击环/震屏/紫光照样放（降级，不静默：
        ///   第一次找不到会打一行警告，方便"模型换了名字"这种情况被立刻发现）✓
        /// </summary>
        private void SyncBossEntranceBug(World w)
        {
            BossEnemy boss = null;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || !e.Alive || e.Def == null) continue;
                if (!(e is BossEnemy b)) continue;
                boss = b;
                break;
            }
            if (boss == null) { _bossEntryBug = null; _bossEntryBugId = int.MinValue; return; }

            if (!boss.InEntrance)
            {
                if (_bossEntryBugId == boss.Id)
                {
                    // 播完 ⇒ 把缩放写回虫怪自己建它时的那个值（`Vector3.one * CharScale`）✓
                    if (_bossEntryBug != null) _bossEntryBug.localScale = Vector3.one * CharScale;
                    _bossEntryBug = null;
                    _bossEntryBugId = int.MinValue;
                    _bossEntryImpact = false;
                }
                return;
            }

            if (_bossEntryBugId != boss.Id)
            {
                _bossEntryBugId = boss.Id;
                _bossEntryImpact = false;
                _bossEntryBug = null;
            }
            if (_bossEntryBug == null) _bossEntryBug = FindBugTransform(boss);
            if (_bossEntryBug != null)
                _bossEntryBug.localScale = Vector3.one * (CharScale * BossEntryScaleAt(boss.EntranceAge));
            else if (!_bossEntryWarned)
            {
                _bossEntryWarned = true;
                Log.Warn("[Seedfall] BOSS 入场：没找到虫怪对象（BUG_" + boss.Def.Id
                         + "）⇒ 只放冲击环/震屏，不缩放入场");
            }

            if (_bossEntryImpact || boss.EntranceAge / Mathf.Max(0.01f, BossEnemy.EntranceSeconds) < BossEntrySettle) return;
            _bossEntryImpact = true;
            PlayBossEntryLanding(boss);
        }

        /// <summary>在 `_root` 的直接子件里找这只 BOSS 的虫怪根节点（判据见 `SyncBossEntranceBug`）。</summary>
        private Transform FindBugTransform(EnemyActor boss)
        {
            if (_root == null || boss == null || boss.Def == null) return null;
            string want = "BUG_" + boss.Def.Id;
            Vector3 at = new Vector3(boss.Pos.x, 0f, boss.Pos.y);
            for (int i = 0; i < _root.childCount; i++)
            {
                Transform t = _root.GetChild(i);
                if (t == null || !t.gameObject.activeSelf) continue;
                if (!string.Equals(t.name, want, System.StringComparison.Ordinal)) continue;
                if ((t.position - at).sqrMagnitude > 0.09f) continue;    // 0.3m 内 = 就是这一只 ✓
                return t;
            }
            return null;
        }

        /// <summary>
        /// ★ v186：**FBX 路径的 BOSS 入场**（虫怪着色器不在时才会走到这里）★
        /// 时间线（总长 = `BossEnemy.EntranceSeconds` = 1.2 秒）：
        ///
        ///     0.00 → 0.78   缩放 0.08 → 1.18（easeOutCubic：越接近尾声长越快）
        ///     0.78          **落地**：震屏 + 一圈紫环 + 一盏紫光 + 地面标记（"它来了"就在这一下）
        ///     0.78 → 1.00   缩放 1.18 → 1.00（收回到正常大小）
        ///
        /// ★ 为什么走 `SetScaleFactor`，不直接写 `localScale` ★
        /// `ModelView` 的缩放是"`BaseScale` × 系数 × 受击脉动"三合一，直接写 `localScale`
        /// 会在**同一帧稍后**被 `SetHitFlash → ApplyScale` 覆盖掉（那份代码的注释里就写着这条 ✗）
        /// ⇒ 走它的入口，三件事才不会互相踩 ✓
        /// ★ 入场结束后必须把系数**写回 `CharScale`** ★
        /// 那是 `SyncEnemies` 建模型时 `ScaleToHeight(RawHeight * CharScale)` 得到的同一个数；
        /// 不写回就会永远停在最后一帧的缩放上（BOSS 比正常小一圈）✗
        /// ⚠️ 入场期间 BOSS **无敌** —— 那条判据在逻辑侧（`World.DealDamage`），不在这里 ✓
        /// </summary>
        private void SyncBossEntrance(EnemyActor e, ModelView view)
        {
            if (view == null || !(e is BossEnemy boss)) return;

            if (!boss.InEntrance)
            {
                // 刚播完（同一只）⇒ 复位到正常大小。只做一次，之后本段每帧直接返回 ✓
                if (_bossEntryId == e.Id)
                {
                    view.SetScaleFactor(CharScale);
                    _bossEntryId = int.MinValue;
                    _bossEntryImpact = false;
                }
                return;
            }

            if (_bossEntryId != e.Id)          // 换了一只 BOSS ⇒ 从头播
            {
                _bossEntryId = e.Id;
                _bossEntryImpact = false;
            }

            view.SetScaleFactor(CharScale * BossEntryScaleAt(boss.EntranceAge));

            float u = Mathf.Clamp01(boss.EntranceAge / Mathf.Max(0.01f, BossEnemy.EntranceSeconds));
            if (_bossEntryImpact || u < BossEntrySettle) return;
            _bossEntryImpact = true;
            PlayBossEntryLanding(e);
        }

        /// <summary>
        /// 每帧同步：位置跟随 / 朝向 / 填充比例 / 文字。
        /// BOSS 不存在或已死亡（`Actor.Alive` 由 `Hp &gt; 0` 派生）⇒ **当帧**隐藏。
        /// </summary>
        private void SyncBossBar(World w)
        {
            EnemyActor boss = null;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || !e.Alive || e.Def == null) continue;
                if (e.Def.Kind != EnemyKind.Boss) continue;
                boss = e;
                break;
            }

            if (boss == null)
            {
                if (_bossBar != null && _bossBar.Shown)
                {
                    if (_bossBar.Root != null) _bossBar.Root.SetActive(false);
                    _bossBar.Shown = false;
                }
                return;
            }

            // ★ v186：入场期间**先不建血条** ★（与上面 `SyncBossEntrance` 同一件事的两面）
            // `BuildBossBar` 会按**当前模型**量一次身高（`MeasureBossBounds` ⇒ `HeadY`），
            // 而这个值**在这只 BOSS 死之前不再重测** —— 若在入场期量，量到的是 8%~118% 的体型
            // ⇒ 血条整场都悬在错误的高度 ✗ 入场结束再建 ⇒ 量到的是正常体型 ✓
            // 顺带也更顺：血条就是"登场那一刻"出现的 ✓
            if (boss is BossEnemy entering && entering.InEntrance)
            {
                if (_bossBar != null && _bossBar.Shown)
                {
                    if (_bossBar.Root != null) _bossBar.Root.SetActive(false);
                    _bossBar.Shown = false;
                }
                return;
            }

            if (_bossBar == null || _bossBar.Root == null || _bossBar.BossId != boss.Id)
            {
                DestroyBossBar();
                BuildBossBar(boss);
                if (_bossBar == null || _bossBar.Root == null) return;
            }

            BossBar bar = _bossBar;
            BossBarSyncFrames++;

            // ── ① 跟随：Boss 位置 + 头顶偏移（每帧一次结构体写入，零分配）──
            bar.Root.transform.position = new Vector3(boss.Pos.x, bar.HeadY, boss.Pos.y);

            // ── ② 朝向：面向相机（billboard）──
            if (_cam == null) _cam = Camera.main;
            if (_cam != null) bar.Root.transform.rotation = _cam.transform.rotation;

            // ── ③ 填充：`localScale.x = hp / maxHp`，锚点在左边 ⇒ 从右往左缩，不是从中心缩 ──
            float pct = boss.Health.MaxHp > 0f ? Mathf.Clamp01(boss.Health.Hp / boss.Health.MaxHp) : 0f;
            bar.Fill.localScale = new Vector3(bar.Width * pct, bar.Height, 1f);

            // ── ④ 文字：`当前HP / 最大HP`，只在整数血量变化时重写（稳态零分配）──
            int hp = Mathf.CeilToInt(boss.Health.Hp);
            int max = Mathf.CeilToInt(boss.Health.MaxHp);
            if (hp != bar.ShownHp || max != bar.ShownMax)
            {
                bar.ShownHp = hp;
                bar.ShownMax = max;
                bar.HpText.text = hp + " / " + max;
                BossBarHpTextWrites++;
            }
            if (!string.Equals(bar.ShownName, boss.Def.Name))
            {
                bar.ShownName = boss.Def.Name;
                bar.NameText.text = boss.Def.Name;
            }

            // ── ⑤ 显隐：只在变化时才 `SetActive`（不是每帧都调）──
            if (!bar.Shown)
            {
                bar.Root.SetActive(true);
                bar.Shown = true;
            }
        }

        private void DestroyBossBar()
        {
            if (_bossBar != null && _bossBar.Root != null) Object.Destroy(_bossBar.Root);
            _bossBar = null;
        }

        /// <summary>
        /// `TextMesh` 的世界字高公式（本工程**实测标定**出来的，见 `MakeBossBarText` 的说明）：
        ///   世界字高 ≈ `characterSize × fontSize / BossBarTextK`
        /// 标定过程：`characterSize=0.08 / fontSize=64 / localScale=0.61` 时量到 0.35m
        /// ⇒ `K = 0.08 × 64 × 0.61 / 0.35 ≈ 8.93` ✓
        /// </summary>
        private const float BossBarTextK = 8.93f;
        /// <summary>字体的栅格化字号（像素）。只影响清晰度，不影响世界尺寸（世界尺寸走 `characterSize`）。</summary>
        private const int BossBarFontPx = 64;

        /// <summary>
        /// 建血条 —— **整个运行期只在（第一次出现 BOSS 的那一帧）建一次**。
        /// 三个物件（底条 / 填充条 / 两段文字）都是"建好就不再建"，之后由 `SyncBossBar` 逐帧改值。
        /// </summary>
        private void BuildBossBar(EnemyActor boss)
        {
            if (_root == null || boss == null || boss.Def == null) return;

            if (!MeasureBossBounds(boss, out float top, out float span))
            {
                top = BossBarFallbackHeadY;
                span = 0f;
            }
            float width = Mathf.Clamp(span > 0.1f ? span * 1.05f : BossBarFallbackWidth, 2.6f, 6.4f);
            float height = width * BossBarHeightRatio;

            var bar = new BossBar
            {
                BossId = boss.Id,
                Width = width,
                Height = height,
                HeadY = top + BossBarLift,
            };
            bar.Root = new GameObject("BOSS_BAR");
            bar.Root.transform.SetParent(_root, false);
            Transform rt = bar.Root.transform;

            Mesh quad = BossBarQuadMesh();

            // 底条：整条（深色 / 近黑），放在**远端**（局部 +Z 是相机前方 ⇒ 先画）
            // ★ 端点必须左移到 −W/2 ★ —— 这根四边形是**左锚点**的（原点在左边缘），
            //   放在 0 的话它只盖住右半根、还会往右多伸半根（实测就抓到过：右侧多出一截黑条）✗
            // ★ v183：材质用 `MatCached`（URP Lit，**写深度**）而不是 `VfxMatCached` ✗→✓ 见下方长注释
            GameObject back = MeshObj(quad, MatCached(BossBarBackColor), "BossBarBack");
            back.transform.SetParent(rt, false);
            back.transform.localPosition = new Vector3(-width * 0.5f, 0f, 0.02f);
            back.transform.localScale = new Vector3(width, height, 1f);

            // 填充条：同一个**左锚点**四边形，原点在左边缘 ⇒ `localScale.x` 直接从左往右涨
            // 自发光版：夜里（`DayNightCycle` 压暗后）也要读得清"还剩多少" ✓
            GameObject fill = MeshObj(quad, MatCached(BossBarFillColor, true), "BossBarFill");
            fill.transform.SetParent(rt, false);
            fill.transform.localPosition = new Vector3(-width * 0.5f, 0f, 0f);
            fill.transform.localScale = new Vector3(width, height, 1f);
            bar.Fill = fill.transform;

            // 文字：`当前HP / 最大HP` 压在条上；名字在条上方（可选项，需求里是"可选"）
            bar.HpText = MakeBossBarText("BossBarHp", new Vector3(0f, 0f, -0.02f), rt,
                                         BossBarHpColor, height * 0.66f, "0 / 0");
            bar.NameText = MakeBossBarText("BossBarName",
                                           new Vector3(0f, height * 0.5f + height * 0.62f, -0.02f), rt,
                                           BossBarNameColor, height * 0.52f,
                                           string.IsNullOrEmpty(boss.Def.Name) ? boss.Def.Id : boss.Def.Name);

            _bossBar = bar;
            BossBarBuilds++;
        }

        private TextMesh MakeBossBarText(string name, Vector3 localPos, Transform parent, Color color,
                                         float worldHeight, string text)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            TextMesh tm = go.AddComponent<TextMesh>();
            Font font = BossBarFont();
            if (font != null)
            {
                tm.font = font;
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = font.material;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
            tm.fontSize = BossBarFontPx;
            // ★ 字号换算：`TextMesh` 的世界字高 ≈ `characterSize × fontSize / BossBarTextK` ★
            //   直接给 `characterSize = worldHeight` 是**错**的：两者差一个 ~9 倍的比例（实测）✗
            //   这样 `localScale` 就固定为 1，只有一处可调 —— 少一个会互相干扰的自由度 ✓
            tm.characterSize = worldHeight * BossBarTextK / BossBarFontPx;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.richText = false;
            // ★ 本地化：BOSS 血条上的 3D 名字（世界空间 TextMesh）也过表 ✓
            //   它是"世界内文字"那一类，IMGUI 的漏斗管不到 ⇒ 必须在建 TextMesh 这一处接。
            tm.text = L10n.S(text);
            go.transform.localScale = Vector3.one;
            return tm;
        }

        /// <summary>
        /// BOSS 血条用的字体 —— **全进程一份**（`_bossBarFont`），换局不重建。
        /// 候选表与 `ImGuiHud.Font`（`ImGuiHud.cs:492`）同一套：血条里的名字是中文，
        /// 而系统字体缺字时 `TextMesh` 会画成空白 ⇒ 必须优先取带 CJK 的字体 ✓
        /// </summary>
        private static Font BossBarFont()
        {
            if (_bossBarFont != null && _bossBarFontGen == L10n.FontGeneration) return _bossBarFont;
            _bossBarFontGen = L10n.FontGeneration;
            _bossBarFont = null;
            // 候选表跟着语言走（`L10n.Fonts`）：`Microsoft YaHei` 没有 Hangul / Arabic
            string[] candidates = L10n.Fonts;
            for (int i = 0; i < candidates.Length; i++)
            {
                Font f = Font.CreateDynamicFontFromOSFont(candidates[i], 64);
                if (f != null) { _bossBarFont = f; break; }
            }
            if (_bossBarFont == null)
            {
                Log.Warn("BOSS血条：系统里取不到中文字体 ⇒ 血条数字仍在，名字可能显示为空白");
                return null;
            }
            // ★ 底条/填充条走 `Seedfall/Vfx`（Queue=Overlay=4000）★
            //   文字默认在 Transparent(3000) ⇒ **会被条盖住**（队列顺序优先于深度，抬高 z 也没用）✗
            //   这份字体是本类**独有**的一份（`CreateDynamicFontFromOSFont` 每次都是新资源）
            //   ⇒ 把它的 Queue 抬到 4010 是安全的，且只写一次 ✓
            if (_bossBarFont.material != null) _bossBarFont.material.renderQueue = 4010;
            return _bossBarFont;
        }

        /// <summary>
        /// 血条的四边形：**左锚点**、1×1（x∈[0,1]，y∈[-0.5,0.5]）、法线朝 −Z（面向相机的那一面）。
        ///
        /// ★ 为什么不用 Unity 内置 Quad ✗ ★
        ///   内置 Quad 的枢轴在**正中心** ⇒ `localScale.x = pct` 会让填充条**从中心往两边缩**，
        ///   满血与残血时左边缘会来回跑，读不出"还剩多少"✗ 而左锚点四边形的左边缘**永远不动** ✓
        /// ★ 法线必须是 −Z ★
        ///   `Seedfall/Vfx` 的菲涅尔用 `dot(N,V)`：牌面正对相机时 `dot = 1` ⇒ 读到的是**平色** ✓
        ///   法线若朝 +Z，`dot = −1` ⇒ `fres = 1` ⇒ 整条只剩霓虹边色（深色底看起来就是"没画出来"）✗
        /// ★ 全局一份（`_fxMeshCache` 键 5700，与既有 5100+/5200+/5400/5500/5600-5620 都不撞）★
        ///   ⇒ 运行期 0 次 `new Mesh` ✓
        /// </summary>
        private static Mesh BossBarQuadMesh()
        {
            const int key = 5700;
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var tri = new List<Vector3>(6)
            {
                new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0.5f, 0f), new Vector3(1f, -0.5f, 0f),
                new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0.5f, 0f),
            };
            Mesh m = BuildFlat("BossBarQuad", tri);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>
        /// 量一次 BOSS 的**模型世界包围盒**：头顶高度（离地，米）与水平跨度。
        ///
        /// 为什么要量：BOSS 的模型由 `ProceduralBugs` 按 id **程序化生成**（`ShapeOf(id, boss)`，
        /// 体型 = `BossUnit 2.5 × crc 抖动 × CharScale 2.6`）⇒ 任何写死的米数在下次调参数时就错位 ✗
        /// 量到的是"这一只现在到底多高"，血条自然贴到它头顶上方 ✓
        ///
        /// 找不到模型（表现层还没接手 / 走了图元兜底）时返回 false，调用方使用常量兜底。
        /// ⚠️ 这里 `GetComponentsInChildren` 会分配一次 —— **只在建条那一帧**，不在逐帧路径上 ✓
        /// </summary>
        private bool MeasureBossBounds(EnemyActor boss, out float top, out float span)
        {
            top = 0f;
            span = 0f;
            if (_root == null || boss == null || boss.Def == null) return false;

            Transform target = null;
            string wanted = "BUG_" + boss.Def.Id;                 // `ProceduralBugs` 的命名（BUG_boss）
            for (int i = 0; i < _root.childCount; i++)
            {
                Transform c = _root.GetChild(i);
                if (c != null && c.name == wanted) { target = c; break; }
            }
            if (target == null && _enemies.TryGetValue(boss.Id, out GameObject go) && go != null)
                target = go.transform;                            // 图元/模型兜底路径
            if (target == null) return false;

            Renderer[] rs = target.GetComponentsInChildren<Renderer>(true);
            if (rs == null || rs.Length == 0) return false;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
                if (rs[i] != null) b.Encapsulate(rs[i].bounds);
            if (b.max.y <= 0.05f) return false;
            top = b.max.y;                                        // 物件贴地(y=0) ⇒ 世界 AABB 顶 = 离地高度
            span = Mathf.Max(b.size.x, b.size.z);
            return true;
        }

        private void SyncProjectiles(World w)
        {
            for (int i = 0; i < w.Projectiles.Count; i++)
            {
                Projectile pr = w.Projectiles[i];
                if (!_projectiles.TryGetValue(i, out GameObject go))
                {
                    // v10：统一子弹颜色——我方（玩家/植物）青色，敌方橙红
                    // v70：改走 VFX 材质 —— 永远画在最上层，且带霓虹边缘。
                    //      敌人子弹的霓虹边是**红色**（需求），我方是青色，混战里一眼分敌我。
                    bool foe = pr.OwnerTeam == Team.Enemy;
                    Color bulletColor = foe
                        ? new Color(1f, 0.42f, 0.22f)
                        : new Color(0.35f, 0.95f, 0.95f);
                    Color rim = foe
                        ? new Color(1f, 0.16f, 0.12f, 1f)
                        : new Color(0.55f, 1f, 1f, 0.85f);
                    go = Prim(PrimitiveType.Sphere, "Projectile", Vector3.zero,
                        Vector3.one * (pr.Radius * 2f), VfxMat(bulletColor, rim));
                    _projectiles[i] = go;
                }
                go.transform.position = new Vector3(pr.Pos.x, 0.55f, pr.Pos.y);
                float s = pr.Radius * 2f;
                go.transform.localScale = new Vector3(s, s, s);
                // v74：子弹投射光影。
                // 不逐颗点灯（数量不可控），走统一的光池——池满时抢占最早过期的那盏。
                // 我方青、敌方红，于是夜里的弹幕会在地面和植物上留下对应颜色的掠影。
                NightLighting.Illuminate(go.transform.position,
                    pr.OwnerTeam == Team.Enemy ? new Color(1f, 0.35f, 0.22f) : new Color(0.55f, 0.9f, 1f),
                    2.6f, 1.5f, 0.09f);
            }
            // 弹道每帧重建索引，多余的对象回收
            for (int i = w.Projectiles.Count; i < _projectiles.Count; i++)
            {
                if (_projectiles.TryGetValue(i, out GameObject extra)) Object.Destroy(extra);
            }
            _scratch.Clear();
            foreach (var kv in _projectiles)
                if (kv.Key >= w.Projectiles.Count) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++)
            {
                if (_projectiles.TryGetValue(_scratch[i], out GameObject dead)) Object.Destroy(dead);
                _projectiles.Remove(_scratch[i]);
            }
        }

        private void SyncPickups(World w)
        {
            for (int i = 0; i < w.Pickups.Count; i++)
            {
                Pickup p = w.Pickups[i];
                // v38：按类型区分外观——经验球（金色八面体）/ 雨滴（蓝色小球）/ 爱心（红色）
                // v71：雨滴加大到 0.42（原 0.26）——它是"没水了就靠它续命"的资源，
                //      俯视图里 0.26 的球只有几个像素，玩家根本找不到。同时改走 VFX 材质置顶。
                Color tint = p.IsRain ? new Color(0.35f, 0.7f, 1f)
                           : p.IsHeart ? new Color(1f, 0.3f, 0.45f)
                           : new Color(1f, 0.85f, 0.25f);   // 经验球
                float size = p.IsRain ? 0.42f : (p.IsHeart ? 0.34f : 0.36f);
                if (!_pickups.TryGetValue(i, out GameObject go))
                {
                    Material m = p.IsRain
                        ? VfxMatCached(new Color(0.45f, 0.80f, 1f, 0.95f), new Color(0.35f, 0.85f, 1f, 1f))
                        : MatCached(tint, true);
                    go = Prim(p.IsRain ? PrimitiveType.Sphere : PrimitiveType.Cube,
                        "Pickup", Vector3.zero, Vector3.one * size, m);
                    _pickups[i] = go;
                }
                go.transform.position = new Vector3(p.Pos.x, p.IsRain ? 0.6f : 0.35f, p.Pos.y);
                go.transform.Rotate(Vector3.up, (p.IsRain ? 40f : 140f) * Time.deltaTime, Space.Self);
                // 经验球与雨滴都脉动，好认
                if (!p.IsHeart)
                {
                    float pulse = 1f + 0.18f * Mathf.Sin(Time.time * (p.IsRain ? 4f : 6f) + i);
                    // ★ v199：雨滴落地 5 秒就消失（`Pickup.RainLifeSeconds`）——
                    //   最后 1.2 秒**缩着淡出**，让"快没了"看得见 ✓
                    //   ⚠️ 只乘缩放，**不动材质/alpha**：这个 GameObject 的材质是 `VfxMatCached` 的
                    //      **共享实例** ⇒ 改 alpha 会让场上所有雨滴一起变透明 ✗
                    float fade = p.RainFadeScale;
                    go.transform.localScale = Vector3.one * (size * pulse * fade);
                }
            }
            _scratch.Clear();
            foreach (var kv in _pickups)
                if (kv.Key >= w.Pickups.Count) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++)
            {
                if (_pickups.TryGetValue(_scratch[i], out GameObject dead)) Object.Destroy(dead);
                _pickups.Remove(_scratch[i]);
            }
        }

        /// <summary>v38：水壶激光——用一根细长发光条从起点延伸到终点（0.12 秒）。</summary>
        private void OnBeam(BeamEvent e)
        {
            // ★ v172 缺陷②：水壶激光 → 水壶的攻击动作 ★
            //
            // ★ 不能"见到 BeamEvent 就播攻击" ✗ —— 同一个事件有**三个来源** ★
            //   ① `FireBeam`（玩家的水壶）      `From = p.Pos`        ← 只有这个该播 ✓
            //   ② `FireSwordWaves`（锄头剑气）  `From = p.Pos`        ← 起点与①**完全相同** ✗
            //   ③ `PluginChain`（共振连锁）     `From = 敌人位置`      ← 逐个跳，起点不是玩家 ✓
            // 所以用**两条一起**判：
            //   · 当前手持水壶 —— 排掉②（剑气只在挥锄时产生）
            //   · 起点 ≈ 玩家位置 —— 排掉③（水壶打中敌人也会引发连锁，光看武器不够）
            // 两条缺一不可 ✓
            //
            // ⚠️ 必须放在下面 `len < 0.2f` 那个提前 return **之前** ✗
            //    否则零长度的那一次（起手第一帧，从玩家指向很近的目标）会被漏掉 ✓
            World lw = _lastWorld;
            PlayerActor lp = lw != null ? lw.Player : null;
            if (lp != null && lp.WeaponIndex == PlayerActor.WeaponCan)
            {
                Vector2 away = e.From - lp.Pos;
                if (away.sqrMagnitude < 0.25f)
                {
                    _pendingAttack = PlayerActor.WeaponCan;
                    // ★ v204：水壶是**按住持续**的（`FireBeam` 每固定帧一次）⇒ 打上"重复事件"标记，
                    //   消费处只在上一段播完时重开（见 `_pendingAttackRepeat` 与消费处的注释）✓
                    _pendingAttackRepeat = true;
                }
            }

            Vector3 origin = new Vector3(e.From.x, 0.7f, e.From.y);
            Vector3 to = new Vector3(e.To.x, 0.7f, e.To.y);
            float span = Vector3.Distance(origin, to);
            if (span < 0.2f) return;

            // ★★ v307：**这一道是第几道**（多柱时 0..N−1；非多柱来源恒为 0）★★
            //   后面所有"每帧一次"的装饰（地面湿痕 / 命中火花）都按它**各算一份**——
            //   改前它们共用一个计时变量，而 N 个事件是**同一帧**发出来的 ⇒ 只有第 0 道过得去 ✗
            int beamSlot = e.Slot >= 0 ? e.Slot % BeamFxSlots : 0;
            // ★ v307：**逐道分摊**节流（总道数越多、每道越稀 ⇒ 每道都有、总量不变）。
            //   没有这一条的话，N 道各自按原间隔出火花/湿痕就是 **N 倍**物件 ✗
            int beamFan = Mathf.Max(1, e.SlotCount);

            // ══════════════════════════════════════════════════════════════════
            // ★ v180：水柱起点**沿瞄准方向外推**，拒绝和玩家重合 ★
            //
            // 需求原话："从距离玩家一点距离的位置开始生成特效，拒绝和玩家重合"✓
            // 旧版 `from = e.From`（= `p.Pos`，玩家脚底中心）⇒ 柱子的第一截**长在角色身上**：
            // 俯视时那一段完全被角色自己挡住（读起来像"水从胸口冒出来"），
            // 而且宽度方向还会盖住角色轮廓 ✗
            //
            // ★ 0.75m 是怎么来的（不是随手挑的数）★
            //   · 玩家模型的世界高度 = 1.8584m（`MDL_player.localScale`，实测），
            //     肩宽约 0.78m × 缩放，**半个身体 + 手里那把工具的伸出量** ≈ 0.7~0.8m
            //   · 柱体可视半宽 `visualW` 上限 0.30m（见下）⇒ 还要再让出 0.3m
            //   两者相加约 0.7~1.0，取中间 **0.75m**：刚好把"角色 + 手里工具"整个让开，
            //   又不至于把短射程的水柱吃掉一大截 ✓
            // ★ 短射程保护：偏移最多吃 `span` 的 45% ★
            //   否则对着贴脸的敌人射时 from 会跑到 to 后面 ⇒ 柱子反向、长度算成负数 ✗
            // ══════════════════════════════════════════════════════════════════
            Vector3 aimAxis = (to - origin) / span;
            float startOff = Mathf.Min(JetStartOffset, span * 0.45f);
            Vector3 from = origin + aimAxis * startOff;
            float len = span - startOff;
            Vector3 mid = (from + to) * 0.5f;
            // v47：粗细与判定共用 tools.can.beamWidth（半宽 → 直径），并把 y/z 两个方向都撑到判定宽度，
            //      这样"看到的宽度 = 打得到的宽度"。旧版写死 0.16，比判定宽度 0.8 细得多。
            float w = Mathf.Max(0.06f, e.HalfWidth > 0f ? e.HalfWidth * 2f : 0.16f);

            // v99：**可视宽度与判定宽度解耦**。
            //
            // v47 当初把可视宽度撑到判定宽度（1.4×2 = 2.8m 粗），理由是"看到的宽度 = 打得到的宽度"。
            // 那在水壶是"点按、每次闪 0.12 秒"的时候没问题——一闪而过，玩家只会觉得"激光很粗"。
            // 但 v90 把水壶改成**连续激光**之后：每固定帧发一次（60/s）、每次存活 0.12s，
            // 于是场上**永远同时有约 7 块 2.8m 宽的立方体**，连起来就是一块盖住半个场地的大蓝板。
            // 玩家看到的就是"场景里出现巨大的异常蓝色平板"。
            //
            // 一条连续光束本来就该是**细而亮**的；"看到的宽度=判定宽度"这个诉求，
            // 对持续存在的表现不成立——那会让光束变成一堵墙。
            //
            // ══════════════════════════════════════════════════════════════════
            // ★★ v200：**加粗必须在画面上真的变粗**（用户："水柱的加粗没有产生效果，
            //    确保肉眼可见的效果，包含特效变粗"）★★
            //
            // ── 改前为什么"完全看不出来"（算得出，不是感觉）✗ ──
            //   可视半宽 = `Clamp(HalfWidth×2×0.16, 0.07, 0.30)`：
            //     没装加粗：1.4×2×0.16 = **0.448** ⇒ 被夹到 **0.30**
            //     装了 1 层：2.8×2×0.16 = **0.896** ⇒ 也被夹到 **0.30**
            //   ⇒ 两次算出来的可视宽度**是同一个数**，屏幕上自然一个像素都不差 ✓
            //   （判定宽度是真翻倍了 —— 所以症状正是"打得更宽，但看着一样粗"。）
            //
            // ── 现在怎么算 ✓ ──
            //   ① 先按老口径算出**未加粗**的可视半宽（含它原有的上限 0.30）
            //   ② 再乘 `1 + ThickenPct`（= `can_thickjet` 的 `BeamWidthPct`，1 层 = ×2、2 层 = ×3）
            //   ③ 新上限 0.90：给的就是"叠满 2 层"的位置（0.30 × 3 = 0.90），**再加粗就会撞上限**
            //   ⇒ 1 层 0.30 → **0.60**（图上宽度翻倍，肉眼可辨）、2 层 → **0.90**
            //   ⚠️ 只乘在**宽度**上：`len` / `blockLen` / 射程一个字都没动 ⇒ 仍是"只改宽不改长" ✓
            //   ⚠️ 柱体由 `JetBlock` 小方块堆出来（`sz = rr × 0.78`，`rr` 由 `visualW` 派生）
            //      ⇒ 方块自己跟着变粗 = **特效变粗**，且**不新建任何 Mesh / Material**（仍是 `mat` 一份）✓
            // ══════════════════════════════════════════════════════════════════
            float visualW = JetVisualHalfWidth(w, Mathf.Max(0f, e.ThickenPct));
            // 只读读数（探针 `ProbeV200` / 交付报告用）：判定半宽 / 加粗比例 / 实际可视半宽
            LastJetHalfWidth = e.HalfWidth;
            LastJetThickenPct = e.ThickenPct;
            LastJetVisualHalfWidth = visualW;

            // ★ v176：激光从此吃**插件外观** ★（在此之前它连 `VfxMat` 都不走，用的是 `Mat(...,true)`）
            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Can);
            float life = FxLife(0.12f);

            // ★ `Scale` 不乘到 `len` / `visualW` 上 ★
            // 逻辑层已经把 `RangePct` 算进 `FireBeam` 的射程、把 `beamWidth` 算进判定宽度了；
            // 这里再乘一次就是**双重计费**，而且会让"看到的线"比"打得到的线"长 ✗
            // ⇒ `Scale` 只作用于装饰件（火花 / 冲击环 / 光照半径）✓
            Material mat = VfxMatCached(fx.Core, fx.Rim);

            // ══════════════════════════════════════════════════════════════════
            // ★ v180：水柱 = **多个方块粒子**拼成的柱体（重做"方形判定框"）★
            //
            // ── 被替掉的是什么 ──
            // 旧版（v70 → v179）水壶那一路一直是一根/几段**长方体**：
            // v70 是 `Prim(Cube)` 一根等宽方条，v179 换成 4~6 段六棱台面片。
            // 两者在俯视图里都是"**一块发光的面片/方板**"——玩家反馈的"方形判定框"就是它 ✓
            //
            // ── 现在是什么 ──
            // 沿轴向若干"站"，每站绕轴一圈**小方块的集合** ⇒ 柱体由粒子堆出来：
            //   · 有**截面**（绕轴一圈的方块），所以任何角度都不是薄片
            //   · 每站绕轴**错开相位** ⇒ 方块连线是一条螺旋，不是三根直杆
            //   · 方块自己的轴向沿水流方向（局部 Z 对齐 `dirN`）⇒ 读得出"在流"
            // 数量：3 站 × 2 块 = **6 块/次**（与旧版 4~6 段同一量级，不增加单帧物件数）
            //      每块活 `life`（0.12s）⇒ 常驻约 6 × 60 × 0.12 ≈ **43 个**（旧版约 36）
            // ★ 材质只有一份：`mat`（`VfxMatCached`）★ 方块用 Unity 内置 cube 网格
            //   （`Prim` 走 `GameObject.CreatePrimitive`）⇒ **不新建任何 Mesh，也不新建 Material** ✓
            // ══════════════════════════════════════════════════════════════════
            Vector3 dirN = (to - from).normalized;
            Vector3 perp = Vector3.Cross(Vector3.up, dirN);
            if (perp.sqrMagnitude < 0.0001f) perp = Vector3.right;
            perp.Normalize();
            Vector3 upAxis = Vector3.Cross(dirN, perp).normalized;   // 与 perp / dirN 正交的"上"

            int stations = 3;                                        // 沿轴几站
            int lobe = 2;                                            // 每站绕轴几块
            float blockLen = len / stations * 0.92f;
            for (int s = 0; s < stations; s++)
            {
                float u = (s + 0.5f) / stations;
                Vector3 centre = Vector3.Lerp(from, to, u);
                float rr = visualW * Mathf.Lerp(1.00f, 0.66f, u);     // 越远越细
                float phase = s * (Mathf.PI * 0.5f);                 // 逐站错开 90° ⇒ 螺旋
                for (int k = 0; k < lobe; k++)
                {
                    float ang = phase + (k / (float)lobe) * Mathf.PI * 2f;
                    Vector3 radial = perp * Mathf.Cos(ang) + upAxis * Mathf.Sin(ang);
                    float sz = rr * 0.78f;
                    GameObject block = Prim(PrimitiveType.Cube, "JetBlock",
                        centre + radial * (rr * 0.62f),
                        new Vector3(sz, sz, blockLen), mat);
                    // 局部 Z 对齐水流方向 ⇒ 方块是"一段水"，不是"一块砖"
                    block.transform.rotation = Quaternion.LookRotation(dirN)
                                             * Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg);
                    Object.Destroy(block, life);
                }
            }

            // ★ v177：激光也要投光（需求③"所有特效具有照亮效果"里原先漏掉的一个）★
            // 水壶是**持续激光**（每固定帧发一次），所以用 NightLighting 的**专用光束灯**，
            // 不占 8 格光池 —— 否则激光一个人就把池子占满，弹幕和爆炸反而抢不到光。
            // ★ v179：颜色改吃插件（原来写死青蓝，于是"装了冷凝烟也还是同一根蓝柱"）★
            // ★ v180：光源点也从 `mid`（含被让开的那一截）挪到**新起点**↔终点之间 ✓
            NightLighting.IlluminateBeam(mid, len, Color.Lerp(fx.Core, fx.Rim, 0.35f), 2.6f);
            // 起点那一小段现在**没有柱子**了（让开角色），补一盏近场小光，
            // 否则"水从胸前冒出来"变成"水凭空出现"，反而更怪 ✓
            IlluminateRamp(from, Color.Lerp(fx.Core, fx.Rim, 0.5f),
                Mathf.Max(0.9f, 1.5f * fx.Scale), 1.4f * fx.Glow, 0.14f, 2);

            // ── v179：**地面湿痕** —— 落点下方一张贴地圆盘 ──
            // 水打到地面会在那一块留下湿的痕迹；它是"固体的一薄片"，**不是 billboard** ✓
            // ★ 必须节流 ★ 水壶每秒 60 根，不节流就是每秒 60 张贴花（画面糊成一片、且白建物件）
            if (_wetCd[beamSlot] <= 0f)
            {
                _wetCd[beamSlot] = 0.18f * beamFan;
                float wetR = Mathf.Clamp(visualW * 6.5f, 0.35f, 1.15f) * fx.Scale;
                GameObject wet = MeshObj(FxDiscMesh(5400, 18),
                    VfxMatCached(new Color(0.24f, 0.52f, 0.50f, 0.85f), fx.Rim), "WetDecal");
                wet.transform.position = new Vector3(e.To.x, 0.035f, e.To.y);
                wet.transform.rotation = Quaternion.identity;     // 圆盘本来就躺在 XZ 平面
                _fxMeshes.Add(new MeshBit
                {
                    Go = wet,
                    Vel = Vector3.zero,
                    Spin = Vector3.zero,
                    Gravity = 0f,
                    T = 0f,
                    Total = FxLife(0.55f),
                    S0 = wetR * 0.45f,          // 先"溅开"……
                    S1 = wetR
                });
            }

            // v47：删掉旧版在激光正中点叠的那枚半径 0.6 的青色圆盘（AddMarker）——
            //      它既不在起点也不在终点，看起来像"凭空出现的圆形判定区"，是玩家反馈的"异常圆形"。

            // ── 拖尾（插件 `Trail` 通道）──
            // ★ v180：残影也必须是**颗粒**，不能是"一根更细的长方板"★
            // 旧版这里是一根 `Prim(Cube, ..., len*0.94)` 的长条 —— 它比柱子本身还像"方形判定框"✗
            // 现在改成沿轴**几块小方块**，与柱体同一套语言（方块粒子）✓
            int echoes = Mathf.RoundToInt(fx.Trail * 3f);
            if (echoes > 0)
            {
                for (int t = 1; t <= echoes; t++)
                {
                    float fade = 1f - 0.28f * t;
                    Color c = fx.Core; c.r *= fade; c.g *= fade; c.b *= fade; c.a = fx.Core.a * 0.55f;
                    Color r = fx.Rim; r.r *= fade; r.g *= fade; r.b *= fade; r.a = fx.Rim.a * 0.45f;
                    Material echoMat = VfxMatCached(c, r);
                    float side = visualW * 1.6f * t * ((t & 1) == 0 ? 1f : -1f);
                    Vector3 off = perp * side;
                    for (int s = 0; s < 3; s++)
                    {
                        float u = (s + 0.5f) / 3f;
                        float sz = visualW * 0.42f;
                        GameObject echoBlock = Prim(PrimitiveType.Cube, "JetEcho",
                            Vector3.Lerp(from, to, u) + off,
                            new Vector3(sz, sz, len / 3f * 0.78f), echoMat);
                        echoBlock.transform.rotation = Quaternion.LookRotation(dirN);
                        Object.Destroy(echoBlock, life * 0.86f);
                    }
                }
            }

            // ── 命中端：**只保留水花**（★ v307 删掉那圈"水波"）──
            //
            // ══════════════════════════════════════════════════════════════════
            // ★★ v307：**用户要删的那道"末端水波"就是这里的 `SpawnShockRing`** ★★
            //
            // ── 它是谁、为什么读成"水波" ──
            //   `SpawnShockRing` 是**贴地向外张开的扩散环**（`Prim` 圆盘 + 半径从 0.16m
            //   涨到 0.75~1.30m、0.26~0.33s 淡出）。它在语义上是"爆炸冲击波"，
            //   与「海浪」「冲击环」「泥点」共用同一套几何 —— 落在水柱末端时，
            //   玩家看到的就是"水柱打到地上又荡开一圈波纹" ✗
            //   而水柱末端该有的只有**溅起来的水花**（§ 下面是 `SpawnSparks`）✓
            //
            // ── 为什么"只有部分插件有" ──
            //   它由 `fx.Rings > 0` 驱动（插件外观通道），只有配了 `Rings` 的插件才出
            //   ⇒ 正好是用户说的"**末端部分插件**会附带一个水波" ✓
            //
            // ── 为什么"只有第一根水柱有" ──
            //   改前 `_beamFxCd` 是**全局面共用一个**计时变量，而 `FireBeam` 在同一帧
            //   连发 N 个 `BeamEvent` ⇒ 第 0 道把它置成 0.10 之后，第 1..N−1 道全被挡掉。
            //   用户那句"只有第一个水柱有特效"里，"特效"指的就是这两样（水波 + 火花）✗
            //   ⇒ a) 波删掉（本段）；b) 每道各一份节流（`_beamFxCd[beamSlot]`）✓
            // ══════════════════════════════════════════════════════════════════
            if (_beamFxCd[beamSlot] <= 0f && fx.Sparks > 0)
            {
                _beamFxCd[beamSlot] = 0.10f * beamFan;
                Vector3 hit = new Vector3(e.To.x, 0.7f, e.To.y);
                SpawnSparks(hit, fx, Mathf.Min(6, fx.Sparks), 0.40f, 3.4f);
                if (NightLighting.Instance != null && fx.Glow > 1.02f)
                    NightLighting.Illuminate(hit, fx.Core,
                        1.6f * fx.Scale, 1.1f * fx.Glow, 0.10f);
            }
        }

        /// <summary>
        /// v176：激光装饰件的节流计时（秒）。见 `OnBeam`。
        /// ★★ v307：从**一个 float** 改成**每道一格** ★★
        ///   `FireBeam` 在同一帧连发 N 个 `BeamEvent`，改前这里只有一个计时变量 ⇒
        ///   第 0 道置成 0.10 之后，第 1..N−1 道的"命中火花"全部被挡掉
        ///   ⇒ 屏幕上是"三根水柱里只有第一根有火花"。用户报的就是这个 ✗
        /// </summary>
        private readonly float[] _beamFxCd = new float[BeamFxSlots];

        // ── v200：水柱宽度的**三个读数**（只读，给探针/交付/自检读；本类内部只在 OnBeam 里写）──
        //
        // 为什么要在表现层留读数（而不是只在 `WeaponSystem.BeamHalfWidth` 上断言）✗
        //   用户报的是"加粗没有产生效果"，而 `BeamHalfWidth` 那条纯算式**本来就是对的**
        //   （1.4 → 2.8，v197 的自检也一直绿）—— 坏的是**表现层把它夹掉了**。
        //   ⇒ 只断言纯算式，等于"测了没坏的那一半"。这三个读数量的才是玩家眼睛看到的那一半 ✓
        /// <summary>最近一根水柱的**判定半宽**（已含加粗，来自 `WeaponSystem.BeamHalfWidth`）。</summary>
        public float LastJetHalfWidth { get; private set; }
        /// <summary>最近一根水柱的**加粗比例**（0 = 没装；1 层 `can_thickjet` = 1.0 = +100%）。</summary>
        public float LastJetThickenPct { get; private set; }
        /// <summary>最近一根水柱的**可视半宽**（= 屏幕上那根柱子的实际粗细，单位米）。</summary>
        public float LastJetVisualHalfWidth { get; private set; }

        /// <summary>
        /// ★ v200：水柱**可视半宽**（米）—— 屏幕上那根柱子的实际粗细。
        ///
        /// 抽成纯函数，因为它是"加粗到底看得见吗"这句话的**唯一判据**：
        ///   · 旧口径 = `Clamp(判定直径 × 0.16, 0.07, 0.30)` —— 基础直径 2.8 时算得 0.448，
        ///     **已经撞在 0.30 上限上**；加粗把直径推到 5.6 后算得 0.896，**照样夹回 0.30** ✗
        ///     ⇒ 两次数值**完全相同** ⇒ 玩家看不到任何变化（这就是"加粗没有产生效果"）✓
        ///   · 新口径 = 旧口径 × `(1 + 加粗比例)`，上限抬到 0.90（= 叠满 2 层的位置）
        ///     ⇒ 0 层 0.30｜1 层 **0.60**｜2 层 **0.90** —— 一层就翻倍，肉眼可辨 ✓
        /// ⚠️ 它**不**乘到长度上（`len` / `blockLen` 用别的量）⇒ "只改宽不改长"仍然成立 ✓
        /// </summary>
        /// <param name="judgeDiameter">判定直径（= `BeamEvent.HalfWidth × 2`，已含加粗的判定口径）。</param>
        /// <param name="thickenPct">加粗比例（0 = 没装；1.0 = +100%）。</param>
        public static float JetVisualHalfWidth(float judgeDiameter, float thickenPct)
        {
            float baseHalf = Mathf.Clamp(Mathf.Max(0.06f, judgeDiameter) * 0.16f, 0.07f, 0.30f);
            return Mathf.Clamp(baseHalf * (1f + Mathf.Max(0f, thickenPct)), 0.07f, 0.90f);
        }

        /// <summary>
        /// v200：水柱宽度读数（一行，给探针与交付报告直接引用）。
        /// 格式：`判定半宽 2.80 米｜加粗 +100%｜可视半宽 0.60 米（直径 1.20）`
        /// </summary>
        public string BeamFxReport()
        {
            return "判定半宽 " + LastJetHalfWidth.ToString("0.00") + " 米"
                 + "｜加粗 +" + (LastJetThickenPct * 100f).ToString("0") + "%"
                 + "｜可视半宽 " + LastJetVisualHalfWidth.ToString("0.00") + " 米"
                 + "（直径 " + (LastJetVisualHalfWidth * 2f).ToString("0.00") + "）";
        }

        /// <summary>v38：肥料炸弹爆炸——橙色扩散圈。</summary>
        /// <summary>
        /// ★ v185：**一发会飞的弯月剑气** ★（`RunSession.FireSwordWaves` 投递，一次一道）
        ///
        /// ── 被替掉的是什么 ──
        /// 旧版走 `BeamEvent`：`OnBeam` 沿整条走廊画一根 0.12 秒的柱体。
        /// 与当时的"瞬时判定"是对得上的，但判定的形状是**矩形**，画出来的却是**柱体**，
        /// 而且一瞬间铺满全程 —— 玩家读到的信息里**没有"它在飞"** ✗
        ///
        /// ── 现在是什么 ──
        /// 一条**弯月**（纯月牙，两端刃尖），从玩家脚下飞出去：
        ///   · 形状 = `FxCrescentMesh`（外径归一 1.0）
        ///   · 尺寸 = **判定框本身**：`SclAxis = (半宽/sin55°, 1, 半长)`，再乘 `Lerp(S0,S1,k)` ✓
        ///     ⇒ 弯月横向最宽处 = `HalfWidth`、朝前最深处 = `HalfLen`，
        ///       **与 `JudgeSwordWaveBox` 判的那两个数逐字相同**（看到的 = 打得到的）✓
        ///   · 朝向 = 飞行方向（局部 +Z 对齐 `Dir`）⇒ 凸面朝前，"刀刃带着弧推过去" ✓
        ///   · 微旋 = 绕飞行轴 `±28°/s` 的滚转角 ⇒ 不是一张死板的面片在平移 ✓
        ///   · 淡出 = **只靠缩放**（`S0 = 1.06 → S1 = 0.62`），**不碰 alpha** ✓
        ///     （`VfxMatCached` 的共享材质契约是"永不逐实例改写"，见那份注释）
        ///   · 光 = `IlluminateRamp` 起手 3 段衰减（与挥击弧同一套写法）✓
        ///
        /// ── 飞行由谁推进 ──
        /// 复用 `_fxMeshes` 那条逐帧循环（`TickFxMeshes`）。**关键点**：`MeshBit` 的寿命
        /// 会被 `DebugFxHold`（抓图慢放）拉长，而速度不会 ⇒ 直接给
        /// `Vel = 速度` 会在慢放时**飞过头**（表现比判定远）✗
        /// 所以反过来算：`Vel = Dir × Dist / Total` ⇒ 无论慢放几倍，它**总是正好飞 `Dist`** ✓
        /// 于是"判定飞多远、弯月就飞多远"在慢放通道里也成立（抓图证据才不是骗人的）✓
        /// </summary>
        private void OnSwordWave(SwordWaveEvent e)
        {
            // ★ v172 缺陷②：锄头挥击带出的剑气 → 锄头的攻击动作 ★
            // 全工程只有 `FireSwordWaves` 投递这个事件，而它只在 `DoHoeSwing` 里被调用 ⇒ 不需额外判据 ✓
            // v204：**离散出手** ⇒ 清掉"重复事件"标记，消费处必须无条件重开动画 ✓
            _pendingAttack = PlayerActor.WeaponHoe;
            _pendingAttackRepeat = false;

            float halfLen = Mathf.Max(0.10f, e.HalfLen);
            float halfWidth = Mathf.Max(0.10f, e.HalfWidth);
            float dist = Mathf.Max(0.05f, e.Dist);
            float speed = Mathf.Max(0.5f, e.Speed);
            Vector2 dir2 = e.Dir.sqrMagnitude > 1e-4f ? e.Dir.normalized : Vector2.up;

            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Hoe);
            // 寿命 = 飞行距离 / 速度（**派生**）⇒ 与逻辑层的 `SwordWaveLife` 同一个式子
            // ★ v185：慢放走**专用**旋钮 `DebugWaveHold`，不借用 `DebugFxHold` ★
            //   借用时（实测）：整场特效一起被拉长 20 倍 ⇒ 挥击弧的冲击环 / 碎片也挂 6 秒，
            //   屏幕上同时有 6 个环 + 一堆碎片，"哪一个是剑气"读不出来 ✗
            //   剑气现在要**单独**慢放才能拍出"它在这一帧、下一帧在别处" ✓
            float life = Mathf.Max(0.05f, dist / speed) * Mathf.Max(1f, DebugWaveHold);
            float travel = dist / Mathf.Max(0.01f, life);   // 米/秒：慢放多久都正好飞完 dist

            Vector3 dir3 = new Vector3(dir2.x, 0f, dir2.y);
            float yaw = Mathf.Atan2(dir2.x, dir2.y) * Mathf.Rad2Deg;

            GameObject go = MeshObj(FxCrescentMesh(), VfxMatCached(fx.Core, fx.Rim, VfxCoreBoostFlat),
                                    "SwordWave");
            go.transform.position = new Vector3(e.Origin.x, 0.55f, e.Origin.y);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // 局部 +Z 是"飞行方向"⇒ 外径(1.0) → 半长、横向(±sin55°) → 半宽
            float lateral = halfWidth / Mathf.Sin(CrescentHalfDeg * Mathf.Deg2Rad);
            _fxMeshes.Add(new MeshBit
            {
                Go = go,
                Vel = dir3 * travel,
                // 滚转：`MeshBit.Spin` 是逐轴自转（`Space.Self`）⇒ 给 Z 就是绕**刃面法线**转。
                // 幅度压到 20°/s：读起来是"刃面带一点点侧倾地推过去"，
                // 再大就会像风车翻面（那是"在翻滚"，不是"在飞"）✗
                Spin = new Vector3(0f, 0f, 20f),
                Gravity = 0f,
                T = 0f,
                Total = life,
                S0 = 1.06f,
                S1 = 0.62f,
                SclAxis = new Vector3(lateral, 1f, halfLen)
            });

            // ── 光效 ──
            // 起手一记照亮（与挥击弧同一套 `IlluminateRamp`：3 段递减，复用一个光池槽）
            IlluminateRamp(go.transform.position,
                Color.Lerp(fx.Core, fx.Rim, 0.40f),
                Mathf.Max(1.2f, halfWidth * 1.15f) * fx.Scale,
                1.9f * fx.Glow, 0.16f, 3);

            // 拖尾（插件 `Trail` 通道）：沿飞行轴**后方**留一道薄残影，读起来是"划过去"；
            // 与主弯月同网格、同材质缓存，只是更小更暗（靠**缩放**小一号，不改 alpha）✓
            // ★ 偏移取 `半长 × 0.55`，不是 `× 1.35` ★
            //   1.35 时残影落在 2.6m 之外 ⇒ 屏幕上像**第二个物体**（"两片叶子"），
            //   不是"一道残影"✗ 0.55 让它与主弯月叠在一起，读起来才是同一道剑气在拖 ✓
            int echoes = Mathf.RoundToInt(fx.Trail * 3f);
            for (int t = 1; t <= echoes; t++)
            {
                Vector3 back = -dir3 * (halfLen * 0.55f * t);
                GameObject echo = MeshObj(FxCrescentMesh(), VfxMatCached(fx.Rim, fx.Core, VfxCoreBoostFlat),
                                          "SwordWaveEcho");
                echo.transform.position = go.transform.position + back;
                echo.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                float shrink = 1f - 0.16f * t;
                _fxMeshes.Add(new MeshBit
                {
                    Go = echo,
                    Vel = dir3 * travel,
                    Gravity = 0f,
                    T = -0.02f * t,                    // 错开起跳 ⇒ 残影跟在后面
                    Total = life,
                    S0 = 1.06f * shrink,
                    S1 = 0.62f * shrink,
                    SclAxis = new Vector3(lateral, 1f, halfLen)
                });
            }
        }

        /// <summary>
        /// v91：肥料爆炸前摇的区域标记。
        /// 需求是"增加爆炸前摇（0.5 秒标记区域）"——标记必须**和真正的爆炸同半径**，
        /// 否则玩家按标记判断"这块田炸得到"，结果炸不到，标记就成了骗人的。
        /// 半径由逻辑层算好传进来（BlastRadiusNow），表现层只负责画。
        /// </summary>
        private void OnFertTelegraph(FertilizerTelegraphEvent e)
        {
            // ★ v172 缺陷②：肥料投掷 → 肥料的攻击动作 ★
            // 全工程只有玩家的 `ArmBlast` 投递**这个**事件（一次投掷的多个落点会投递多次，
            // 同一个值重复赋值无所谓）✓
            // ⚠️ 另一个事件 `FertilizerBlastEvent` **不能**拿来当判据 ✗ ——
            //    它除了玩家的爆炸（`ExplodeAt`）之外，**投射物自爆**（`Projectile.Explode`）也投递，
            //    挂上去会变成"敌人的弹幕爆炸时玩家挥了一下肥料" ✓
            //
            // 用**投掷瞬间**而不是爆炸瞬间：v91 给爆炸加了 0.5s 前摇，
            // 挂在爆炸上会让"扔"这个动作迟半秒，看起来像先炸后扔 ✗
            _pendingAttack = PlayerActor.WeaponFertilizer;
            _pendingAttackRepeat = false;      // v204：离散出手 ⇒ 消费处无条件重开动画（见 `_pendingAttackRepeat`）

            // v176：前摇标记的颜色也吃插件 —— 与真炸是**同一份** `FxSpec`
            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Fertilizer);
            // ★ v180：填充圆盘 → **细环**（半径不变，仍是真实爆炸半径）★
            AddRingMarker(e.Pos, e.Radius, e.Duration, new Color(fx.Core.r, fx.Core.g, fx.Core.b, 1f));
        }

        private void OnFertBlast(FertilizerBlastEvent e)
        {
            // v176：颜色 / 环数 / 火花 / 发光 / 震屏全部改由**肥料这一路的插件**决定 ✓
            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Fertilizer);
            var blastAt = new Vector3(e.Pos.x, 0.05f, e.Pos.y);

            // ══════════════════════════════════════════════════════════════════
            // ★ v180：肥料爆炸 = **蘑菇云**（重做"圆形判定框"）★
            //
            // ── 被替掉的是什么（三样都是"圆"）──
            //   ① `AddMarker(...)`         —— 一个**填充圆盘**（`Prim(Cylinder)` 铺满整个判定半径）
            //   ② `BlastRing` / `BlastRingX` —— 三个**实心圆柱环**，从 0 放大到 1.35×半径
            //   ③ `BlastCore`              —— 一个**球**
            //   三者叠起来就是"地上亮起一个大圆斑 + 三圈更大的圆环"——
            //   玩家在俯视图里读到的就是"圆形判定框"，而且它**比爆炸本身还显眼** ✗
            //
            // ── 现在是什么（三层，天然"下窄上宽"）──
            //   ① **茎**   ：8 块细方柱，半径 0.16R~0.26R，从地面升到 0.55R
            //   ② **帽**   ：14 块方块在一个环上（半径 0.72R、高 0.92R），向外 + 向上翻滚
            //   ③ **翻卷** ：10 块更靠外（0.90R）+ 高速自转，读作"顶部边缘在卷"✓
            //   再叠一层**压扁的球壳**当帽内体积（`FxIcoShellMesh`，Y 压到 0.62）
            // ⇒ 俯视图里读到的顺序是"细茎 → 宽帽 → 卷边"，不再是"一个圆"✓
            //
            // ★ 判定半径仍然可见 ★ 但不再用填充圆盘：
            //   改成**一圈细环**（`FxRingMesh`，环宽 14% 半径）——它读作"范围边界"，
            //   而不是"这一片都被判定覆盖"⇒ 仍然是"看得到 = 打得到"，但不抢画面 ✓
            // ★ 半径一律**不乘** `fx.Scale` ★
            //   `ExplodeAt` 已经把 `ExplodeRadiusPct` 算进 `blastRadius`；再乘一次就是双重计费，
            //   而且会让"看到的圈"大于"真炸到的圈" —— v91 明令禁止 ✓
            // ★ 全部走 `VfxMatCached` ★ 方块用 Unity 内置 cube 网格 ⇒ 不新建 Mesh / Material ✓
            // ══════════════════════════════════════════════════════════════════
            float R = Mathf.Max(0.30f, e.Radius);

            // ── 判定边界：一圈细环（替代原来的填充圆盘）──
            SpawnGroundRim(e.Pos, R, fx, 0.42f);

            // ── ①~④ 蘑菇云本体（茎 / 帽 / 翻卷 / 帽内体积）──
            // ★ v188：从"0.5 秒的爆"改成"**持续 1 秒、渐渐消散**" ★
            //   实现放在 `SpawnFertMushroom`，主爆与子爆**走同一个函数** ⇒ 子爆自动继承 ✓
            SpawnFertMushroom(blastAt, R, fx);

            // ⑤ 火花（插件 `Sparks` 通道）
            float spread = Mathf.Clamp(e.Radius * 0.8f, 0.8f, 4.5f);
            SpawnSparks(new Vector3(e.Pos.x, 0.5f, e.Pos.y), fx,
                Mathf.Min(10, fx.Sparks * 2), spread, 5.5f);

            // ══════════════ 实体颗粒（碎片 / 白闪）══════════════

            // ④c **12~18 块碎片**飞散 —— 与球壳是**同一套颗粒**（八面体碎块），
            //     只是这次被炸飞了出去 ⇒ 三种特效的颗粒语言在这里对上 ✓
            int pieceN = Mathf.Clamp(12 + fx.Sparks, 12, 18);
            Material pieceMat = VfxMatCached(fx.Core, fx.Rim);
            Material pieceMatHi = VfxMatCached(Color.Lerp(fx.Core, Color.white, 0.40f), fx.Rim);
            float pieceR0 = Mathf.Clamp(e.Radius * 0.22f, 0.10f, 0.55f);
            for (int i = 0; i < pieceN; i++)
            {
                // 球面均分（黄金角）—— 爆炸是**球壳**向外炸，不能只在水平面上撒 ✓
                float a = i * 2.39996f;
                float y = 1f - 2f * (i + 0.5f) / pieceN;
                float rr = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                var dir = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
                GameObject pg = MeshObj(FxShardMesh((i * 5 + 2) % 6),
                    ((i & 1) == 0) ? pieceMat : pieceMatHi, "BlastShard");
                pg.transform.position = blastAt + dir * pieceR0;
                pg.transform.rotation = Quaternion.Euler(
                    Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                float ps = pieceR0 * Random.Range(0.55f, 1.05f);
                _fxMeshes.Add(new MeshBit
                {
                    Go = pg,
                    Vel = dir * Random.Range(3.2f, 7.5f) + Vector3.up * 1.2f,
                    Spin = new Vector3(Random.Range(-540f, 540f),
                                       Random.Range(-540f, 540f),
                                       Random.Range(-540f, 540f)),
                    Gravity = 13f,
                    T = -0.008f * i,
                    Total = FxLife(0.55f),
                    S0 = ps,
                    S1 = ps * 0.25f
                });
            }

            // ④d **1 帧白闪**
            //     复用同一个球壳网格、换一份**已缓存的纯白材质**（不是去改当前那份）✓
            //     `Total = 1/60` 在正常速度下**正好一帧**；抓图时 `DebugFxHold` 会把它拉长到可见
            //     （否则桥接往返一次就 1 秒，白闪永远拍不到 —— 见 `FxLife` 的说明）✓
            GameObject flash = MeshObj(FxIcoShellMesh(1), VfxMatCached(Color.white, Color.white), "BlastFlash");
            // v180：白闪放在**帽**的高度（`R*0.8`），不是贴地 —— 蘑菇云的亮度中心在顶部 ✓
            flash.transform.position = blastAt + Vector3.up * (R * 0.80f);
            float flashR = Mathf.Max(0.30f, e.Radius * 0.62f);
            _fxMeshes.Add(new MeshBit
            {
                Go = flash,
                Vel = Vector3.zero,
                Spin = Vector3.zero,
                Gravity = 0f,
                T = 0f,
                Total = FxLife(1f / 60f),
                S0 = flashR,
                S1 = flashR * 1.18f
            });

            // ④e 白闪那一刻的**点光**（很短、很白）——
            //     与下面 `NightLighting.Illuminate` 的暖光分工：这盏是"闪"，那盏是"照亮战场"
            IlluminateRamp(blastAt, Color.white, e.Radius * 1.2f, 7.5f * fx.Glow, 0.10f, 2);

            // v69：拟声词特效。在爆炸点**旁边**而不是正中——
            // 正中会被冲击环和中心闪光盖住，字就白做了。偏移量按爆炸半径给，小炸弹也不会偏太远。
            float off = Mathf.Clamp(e.Radius * 0.45f, 0.6f, 2.2f);
            // v122：爆炸拟声词改**全大写**（需求）。
            // 大写在视觉上是"更响"的：小写 bomb 混在其他小字里会被当成普通文字扫过去，
            // 全大写 + 放大 + 全角叹号才读得出"轰"的那一下。
            Bus.Publish(new FloatingTextEvent
            {
                Pos = e.Pos + new Vector2(off, off * 0.6f),
                Text = "BOMB！！！",
                Kind = PopupKind.Bomb
            });

            // v176：震屏与照明也吃插件（`Knockback` 抬震屏 / `Glow` 抬照明）
            Shake(0.22f * Mathf.Max(0.5f, fx.Shake), 0.16f);

            // v74：爆炸是全场最亮的一瞬——大范围暖光，夜里这一下能照亮半个场地
            NightLighting.Illuminate(new Vector3(e.Pos.x, 0.8f, e.Pos.y),
                Color.Lerp(fx.Core, fx.Rim, 0.25f), e.Radius * 1.6f * fx.Scale,
                6.5f * fx.Glow, 0.32f);
        }

        /// <summary>
        /// ★ v188：**肥料蘑菇云本体** —— 主爆与子爆共用这一个入口 ★
        ///
        /// ── 用户原话 ──
        ///   "修改肥料爆炸特效（产生蘑菇云，持续 1 秒渐渐消散，子爆炸同样继承）"
        ///
        /// ── "持续 1 秒"是怎么落到代码上的 ──
        ///   v180 的三层（茎 8 / 帽 12~18 / 翻卷 10）寿命只有 0.46 / 0.62 / 0.70 秒，
        ///   而且**所有块同时出现** ⇒ 画面上是一记"炸"，不是一朵"云"✗
        ///   现在：每块的 `Total` 拉到 **0.92~1.00 秒**，并按 `i` **错开起跳**
        ///   （`T` 从负数开始 ⇒ 第 i 块晚 `i × 0.04s` 才出现）✓
        ///   ⇒ 整朵云从"爆开"到"最后一块消失"横跨 1 秒，且**任何时刻都还有块在** ✓
        ///
        /// ── "渐渐消散"是怎么做的（不碰 alpha）──
        ///   共享材质的契约是**永不逐实例改写**（`VfxMatCached` 的说明），所以淡出不能靠 `_BaseColor.a` ✗
        ///   改用三条**看得见**的变化代替：
        ///     ① **缩放**：每块 `S1` 收到 `S0 × 0.10~0.30` ⇒ 越到后面越"空、稀"✓
        ///     ② **上浮**：速度全给 +Y（茎 0.55R/s，帽 1.4~2.4 m/s，体积壳 0.95R/s）⇒ 云在升高 ✓
        ///     ③ **错峰消失**：`T` 错开 ⇒ 块是**一块一块消失**的，不是"整朵一起熄灭"✓
        ///   三者叠起来，1 秒里读到的就是"越升越高、越来越淡、越来越稀"✓
        ///
        /// ── 零分配 ──
        ///   方块用 Unity 内置 cube（`Prim`），球壳走 `FxIcoShellMesh` 的 `_fxMeshCache`，
        ///   材质走 `VfxMatCached` ⇒ 运行期没有 `new Mesh` / `new Material` ✓
        ///   （每块 `Prim` 会建一个 GameObject —— 那是**每次爆炸一次**，不是每帧；与 v180 相同）
        /// </summary>
        private void SpawnFertMushroom(Vector3 blastAt, float R, in FxSpec fx)
        {
            Material stemMat = VfxMatCached(fx.Core, fx.Rim);
            Material stemMatHi = VfxMatCached(Color.Lerp(fx.Core, Color.white, 0.35f), fx.Rim);

            // ── ① 茎：下窄（细方柱从地面升起）—— 云的"柱"，走得最慢、留得最久 ──
            int stemN = 8;
            for (int i = 0; i < stemN; i++)
            {
                float a = (i / (float)stemN) * Mathf.PI * 2f;
                float rr = R * Mathf.Lerp(0.16f, 0.26f, (i % 3) / 2f);
                float h = R * Mathf.Lerp(0.28f, 0.55f, (i % 4) / 3f);
                float thick = R * 0.10f;
                Vector3 at = blastAt + new Vector3(Mathf.Cos(a) * rr, h * 0.5f, Mathf.Sin(a) * rr);
                GameObject block = Prim(PrimitiveType.Cube, "BlastStem", at,
                    new Vector3(thick, h, thick), ((i & 1) == 0) ? stemMat : stemMatHi);
                block.transform.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
                _fxMeshes.Add(new MeshBit
                {
                    Go = block,
                    Vel = new Vector3(0f, R * 0.55f, 0f),          // 缓慢上升（蘑菇的茎在长）
                    Spin = new Vector3(0f, Random.Range(-60f, 60f), 0f),
                    Gravity = 0f,
                    T = -0.020f * i,                               // v188：错开（0 → −0.14s）
                    Total = FxLife(0.81f),                         // 结束时刻 = 0.81 + 0.02i ⇒ 最晚 0.95s
                    S0 = 1f,
                    S1 = 0.22f                                     // v188：0.70 → 0.22（收得干净）
                });
            }

            // ── ② 帽：上宽（一圈方块向外 + 向上翻）—— 云的"伞盖" ──
            int capN = Mathf.Clamp(12 + fx.Rings * 2, 12, 18);
            float capR = R * 0.72f, capY = R * 0.92f;
            for (int i = 0; i < capN; i++)
            {
                float a = (i / (float)capN) * Mathf.PI * 2f + 0.21f;
                var outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 at = blastAt + outw * (capR * 0.42f) + Vector3.up * (capY * 0.55f);
                float sz = R * Random.Range(0.16f, 0.24f);
                GameObject block = Prim(PrimitiveType.Cube, "BlastCap", at,
                    Vector3.one * sz, ((i & 1) == 0) ? stemMatHi : stemMat);
                block.transform.rotation = Quaternion.Euler(
                    Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                _fxMeshes.Add(new MeshBit
                {
                    Go = block,
                    Vel = outw * Random.Range(1.6f, 3.0f) + Vector3.up * Random.Range(1.4f, 2.4f),
                    Spin = new Vector3(Random.Range(-320f, 320f),
                                       Random.Range(-320f, 320f),
                                       Random.Range(-320f, 320f)),
                    Gravity = 2.6f,                                 // 先冲上去，再被"压"下来 ⇒ 帽形的翻卷
                    T = -0.025f * i,                                // v188：错开（0 → −0.375s）
                    Total = FxLife(0.62f),                          // 结束时刻 = 0.62 + 0.025i ⇒ 最晚 1.00s ★整朵云在 1 秒内清空★
                    S0 = sz,
                    S1 = sz * 0.12f                                 // v188：0.35 → 0.12（越后越稀）
                });
            }

            // ── ③ 翻卷：顶部边缘向外滚（明显更靠外、更水平）──
            int curlN = 10;
            float curlR = R * 0.90f;
            for (int i = 0; i < curlN; i++)
            {
                float a = (i / (float)curlN) * Mathf.PI * 2f + 0.5f;
                var outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var tangent = new Vector3(-outw.z, 0f, outw.x);
                Vector3 at = blastAt + outw * (curlR * 0.55f) + Vector3.up * (R * 1.02f);
                float sz = R * Random.Range(0.12f, 0.19f);
                GameObject block = Prim(PrimitiveType.Cube, "BlastCurl", at,
                    new Vector3(sz * 1.5f, sz * 0.7f, sz), stemMat);
                _fxMeshes.Add(new MeshBit
                {
                    Go = block,
                    Vel = outw * Random.Range(2.4f, 3.6f) + tangent * Random.Range(0.8f, 1.8f),
                    Spin = new Vector3(0f, Random.Range(360f, 620f), 0f),   // 绕竖轴快速自转 = 翻卷
                    Gravity = 1.4f,
                    T = -0.030f * i,                                // v188：错开（0 → −0.27s）
                    Total = FxLife(0.73f),                          // 结束时刻 = 0.73 + 0.03i ⇒ 最晚 1.00s
                    S0 = sz,
                    S1 = sz * 0.10f                                 // v188：0.30 → 0.10
                });
            }

            // ── ④ 帽内体积：两层球壳 ──
            //  ★ v188：拆成"先撑开 + 后消散"两个壳 ★
            //   一个壳做不到：它要么在长大（= 云在成形）、要么在缩小（= 云在散），
            //   而用户要的是**1 秒里先成形、再渐渐散** ⇒ 两段交给两块不同的壳 ✓
            //   ① `BlastDomeOpen`：0.30 秒内 R×0.55 → R×1.05，向上 0.7R/s（云头撑开）
            //   ② `BlastDomeFade`：0.55 秒后才起（`T = -0.55`），从 R×1.00 收到 R×0.30
            //      并继续上浮 0.95R/s ⇒ 云的"余体"慢慢升空、变小、消失 ✓
            //   两块都走 `_fxMeshCache`（`FxIcoShellMesh(1, 0.58)` 压扁的云帽）与 `VfxMatCached` ✓
            Mesh domeMesh = FxIcoShellMesh(1, 0.58f);          // Y 压扁 = 云帽（烘进网格，见那个函数）

            GameObject dome = MeshObj(domeMesh, VfxMatCached(fx.Rim, fx.Core), "BlastDomeOpen");
            dome.transform.position = blastAt + Vector3.up * (R * 0.80f);
            dome.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            _fxMeshes.Add(new MeshBit
            {
                Go = dome,
                Vel = new Vector3(0f, R * 0.70f, 0f),
                Spin = new Vector3(0f, Random.Range(-70f, 70f), 0f),
                Gravity = 0f,
                T = 0f,
                Total = FxLife(0.34f),                             // 撑开段
                S0 = R * 0.55f,
                S1 = R * 1.05f
            });

            GameObject fade = MeshObj(domeMesh, VfxMatCached(Color.Lerp(fx.Rim, Color.black, 0.35f), fx.Core),
                "BlastDomeFade");
            fade.transform.position = blastAt + Vector3.up * (R * 0.95f);
            fade.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            _fxMeshes.Add(new MeshBit
            {
                Go = fade,
                Vel = new Vector3(0f, R * 0.95f, 0f),              // 消散段：继续上浮
                Spin = new Vector3(0f, Random.Range(-40f, 40f), 0f),
                Gravity = 0f,
                T = -0.40f,                                        // ★ 晚 0.40 秒再出现（错峰）★
                Total = FxLife(0.60f),                             // 0.40 + 0.60 = **整 1.00 秒** ⇒ 云在 1 秒收完 ✓
                S0 = R * 1.00f,
                S1 = R * 0.30f                                     // 收到 30% ⇒ "渐渐消散"✓
            });
        }

        /// <summary>
        /// v176：一个**会淡出**的爆炸件（冲击环 / 中心闪光 / 冲击环回声）。
        ///
        /// ★ 为什么不再用三个平行的 List&lt;float&gt; ★
        /// 旧版是 `_blasts`（GameObject）+ `_blastT` + `_blastR` 三张平行表，
        /// 每加一个要变的量就多一张表，且 RemoveAt 三处必须同步 —— 少一处就是**错位** ✗
        /// 而材质必须逐实例持有（要淡出），所以得连 Mat 一起记 ✓
        /// </summary>
        private struct BlastFx
        {
            public GameObject Go;
            /// <summary>逐实例材质。**结束时必须 `Object.Destroy(Mat)`** —— 见 `VfxMatCached` 的说明。</summary>
            public Material Mat;
            public Color Core;
            public Color Rim;
            public float T;
            public float Total;
            public float S0, S1;      // 水平起止直径
            public float Thick;       // 竖直厚度（环片是扁的）
        }

        private readonly List<BlastFx> _blasts = new List<BlastFx>();

        // ── v176：插件特效的"装饰件"（火花） ──
        //
        // `FxSpec.Sparks` 是**个数**，画面上必须对应到真的会飞的小物件。
        // 它们只活 0.3 秒、纯表现、与判定无关 ⇒ 不进确定性重放 ✓
        //
        // ★ 火花只用**缩小**、不做 alpha 淡出 ★
        // 因为它共用 `VfxMatCached` 的材质（那份缓存是"永不改写"的契约）；
        // 要淡出就必须逐实例持材质 ⇒ 每次攻击多一次 `new Material`。
        // 火花数量本来就多（一件插件 +2~+3），为它破缓存不值 ✓
        private struct FxBit
        {
            public GameObject Go;
            public Vector3 Vel;
            public float T;
            public float Total;
            public float S0, S1;
        }

        private readonly List<FxBit> _fxBits = new List<FxBit>();

        /// <summary>
        /// v176：**验收用的慢放倍率**。
        ///
        /// 水壶激光只活 0.12 秒、挥击弧 0.20 秒 —— 而桥接往返一次就 1 秒以上，
        /// 抓到的时候早就消失了。`DebugSwingHold` 当年为此而存在（只对挥击弧生效）。
        /// 这里把它推广到**所有**特效件的存活时长，这样"插件到底改变了什么"才拍得下来 ✓
        /// 正常游戏里恒为 1 ⇒ **不改变任何手感** ✓
        /// </summary>
        public static float DebugFxHold = 1f;

        /// <summary>
        /// ★ v185：**剑气专属慢放倍率**（只给验收抓图用，正常 1）★
        ///
        /// 为什么不复用它旁边的 `DebugFxHold`：那个是"整场特效"的总旋钮，
        /// 调大之后挥击弧的冲击环、碎片、湿痕一起被拉长 ⇒ 抓图里全是别的特效，
        /// 而剑气只占其中一小块，读不出"它在飞"✗
        /// 这里只乘**弯月的寿命**（速度同步除以它）⇒ 它飞得慢、别的特效照常 ✓
        /// </summary>
        public static float DebugWaveHold = 1f;

        /// <summary>按慢放倍率缩放一个存活时长。</summary>
        private static float FxLife(float normal) => normal * Mathf.Max(0.02f, DebugFxHold);

        /// <summary>
        /// v176：在 <paramref name="center"/> 周围撒一圈**火花**（插件 `Sparks` 通道）。
        /// 用极坐标均分 + 抖动，保证不会出现"都往一个方向飞"或"叠在一点"。
        /// </summary>
        private void SpawnSparks(Vector3 center, in FxSpec fx, int count, float spread, float speed)
        {
            if (count <= 0) return;
            Material mat = VfxMatCached(fx.Rim, fx.Core);
            float size = Mathf.Max(0.05f, 0.13f * fx.Scale);
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.4f, 0.4f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                GameObject go = Prim(PrimitiveType.Sphere, "FxSpark",
                    center + dir * Random.Range(0.05f, Mathf.Max(0.06f, spread * 0.3f)),
                    Vector3.one * size, mat);
                _fxBits.Add(new FxBit
                {
                    Go = go,
                    Vel = dir * speed * Random.Range(0.7f, 1.3f)
                        + Vector3.up * Random.Range(0.6f, 1.7f),
                    T = 0f,
                    Total = FxLife(0.30f),
                    S0 = size,
                    S1 = size * 0.15f
                });
            }
        }

        /// <summary>
        /// v176：一圈**冲击环**（插件 `Rings` 通道）。
        /// `r0 → r1` 扩散并淡出；材质逐实例持有，结束销毁。
        ///
        /// ★ v180：从**实心圆柱**改成**细环**★
        /// 旧版 `Prim(Cylinder)` 是一枚**实心圆片**，扩散时读作"圆形判定框在变大"——
        /// 与这次被重做掉的那三种"圆"是同一类东西 ✗
        /// 换成 `FxRingMesh`（环宽 14%）之后：扩散读作"一道波"，边界仍然精确等于 `r1` ✓
        /// </summary>
        private void SpawnShockRing(Vector3 center, in FxSpec fx, float r0, float r1, float life, float y)
        {
            float d0 = Mathf.Max(0.08f, r0 * 2f);
            Material mat = VfxMat(fx.Rim, fx.Core);
            GameObject go = MeshObj(FxRingMesh(5610, 30, 0.86f), mat, "FxRing");
            go.transform.position = new Vector3(center.x, y, center.z);
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = new Vector3(d0, 1f, d0);
            _blasts.Add(new BlastFx
            {
                Go = go, Mat = mat, Core = fx.Rim, Rim = fx.Core,
                T = 0f, Total = FxLife(Mathf.Max(0.06f, life)),
                S0 = d0, S1 = Mathf.Max(0.12f, r1 * 2f), Thick = 0.05f
            });
        }

        /// <summary>v42 + v176：爆炸/冲击环的扩散 + **真正生效的**淡出。</summary>
        private void TickBlasts(float dt)
        {
            for (int i = _blasts.Count - 1; i >= 0; i--)
            {
                BlastFx b = _blasts[i];
                b.T += dt;
                float k = Mathf.Clamp01(b.T / Mathf.Max(0.01f, b.Total));

                if (b.Go != null)
                {
                    float s = Mathf.Lerp(b.S0, b.S1, k);
                    b.Go.transform.localScale = new Vector3(s, Mathf.Max(0.02f, b.Thick * (1f - k)), s);
                }
                // ★ v176：淡出走 `_BaseColor` 而不是 `Material.color` ★
                // 旧版写的是 `rend.material.color`（= `_Color`），
                // 而这个着色器只有 `_BaseColor`（`SeedfallVfx.shader:18`）
                // ⇒ 赋值**静默无效**，爆炸环一直是"硬消失"的 ✓
                // 顺带省掉 `rend.material`：那是**每次访问都可能复制一份材质**的接口，
                // 原来在每帧、每个爆炸件上各调两次 ✓
                if (b.Mat != null)
                {
                    Color core = b.Core; core.a = Mathf.Clamp01(core.a) * (1f - k);
                    Color rim = b.Rim; rim.a = Mathf.Clamp01(rim.a) * (1f - k);
                    SetVfxColor(b.Mat, core, rim);
                }

                _blasts[i] = b;
                if (k < 1f) continue;
                if (b.Go != null) Object.Destroy(b.Go);
                if (b.Mat != null) Object.Destroy(b.Mat);   // ★ 必须显式销毁，否则泄漏
                _blasts.RemoveAt(i);
            }
        }

        /// <summary>v176：火花的飞行与缩小。</summary>
        private void TickFxBits(float dt)
        {
            for (int i = _fxBits.Count - 1; i >= 0; i--)
            {
                FxBit b = _fxBits[i];
                if (b.Go == null) { _fxBits.RemoveAt(i); continue; }
                b.T += dt;
                float k = Mathf.Clamp01(b.T / Mathf.Max(0.01f, b.Total));
                b.Go.transform.position += b.Vel * dt;
                b.Vel += new Vector3(0f, -12f * dt, 0f);      // 抛物线下落，才像"火星"
                float s = Mathf.Lerp(b.S0, b.S1, k);
                b.Go.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
                _fxBits[i] = b;
                if (k < 1f) continue;
                Object.Destroy(b.Go);
                _fxBits.RemoveAt(i);
            }
        }

        /// <summary>v38：土地成熟——绿色提示环。</summary>
        private void OnPlotMature(TreeMatureEvent e)
        {
            if (_lastWorld == null || _lastWorld.Farm == null) return;
            FarmPlot plot = _lastWorld.Farm.Plot(e.PlotIndex);
            if (plot != null) AddMarker(plot.World, 1.4f, 0.6f, new Color(0.5f, 1f, 0.5f, 1f));
        }

        /// <summary>v38：击杀轻震。</summary>
        private void OnKilledShake(EnemyKilledEvent e)
        {
            Shake(0.06f, e.EnemyId == "boss" ? 0.5f : 0.04f);
        }

        // ── 震屏 ──
        private World _lastWorld;
        private float _shakeTimer;
        private float _shakeAmp;

        /// <summary>v38：相机震动（收割/爆炸/击杀反馈）。</summary>
        public void Shake(float amp, float seconds)
        {
            _shakeAmp = Mathf.Max(_shakeAmp, amp);
            _shakeTimer = Mathf.Max(_shakeTimer, seconds);
        }

        private Vector3 ShakeOffset(float dt)
        {
            if (_shakeTimer <= 0f) { _shakeAmp = 0f; return Vector3.zero; }
            _shakeTimer -= dt;
            float a = _shakeAmp * Mathf.Clamp01(_shakeTimer * 8f);
            return new Vector3(Random.Range(-a, a), Random.Range(-a, a), 0f);
        }

        private void SyncMarkers(float dt)
        {
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                Marker m = _markers[i];
                m.Ttl -= dt;
                if (m.Ttl <= 0f || m.Go == null)
                {
                    if (m.Go != null) Object.Destroy(m.Go);
                    // v176：逐实例材质必须跟着走 —— 否则每个标记留一份永久材质 ✗
                    if (m.Mat != null) Object.Destroy(m.Mat);
                    _markers.RemoveAt(i);
                    continue;
                }
                float k = Mathf.Clamp01(m.Ttl / Mathf.Max(0.01f, m.Total));
                // v176：直接把 alpha 写到自己的材质上。
                // 旧版绕了一圈 `GetComponent<Renderer>().sharedMaterial.color` → `SetColor(go, c)`，
                // 一次淡出要做 2 次 GetComponent + 3 次属性写入，而且中间那行 localScale
                // 是 `s → s` 的**无操作**（历史残留）✓
                if (m.Mat != null)
                {
                    if (m.Mat.HasProperty(BaseColorId))
                    {
                        Color c = m.Mat.GetColor(BaseColorId);
                        c.a = k;
                        m.Mat.SetColor(BaseColorId, c);
                    }
                    else
                    {
                        Color c = m.Mat.color;
                        c.a = k;
                        m.Mat.color = c;
                    }
                }
            }
        }

        /// <summary>
        /// v180：**环形**区域标记（肥料前摇用）。
        /// 与被它替掉的 `AddMarker` 的区别只有一处：环而不是填充圆盘 ——
        /// 判定半径一模一样（`radius` 就是爆炸半径）⇒ v91 那条"看得到 = 打得到"不变 ✓
        /// </summary>
        private void AddRingMarker(Vector2 pos, float radius, float duration, Color color)
        {
            Material mat = VfxMat(color, color);
            GameObject go = MeshObj(FxRingMesh(5620, 36, 0.90f), mat, "RingMarker");
            go.transform.position = new Vector3(pos.x, 0.06f, pos.y);
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
            _markers.Add(new Marker { Go = go, Ttl = duration, Total = duration, Mat = mat });
        }

        private void AddMarker(Vector2 pos, float radius, float duration, Color color)
        {
            Material mat = Mat(color);
            GameObject go = Prim(PrimitiveType.Cylinder, "Marker",
                new Vector3(pos.x, 0.12f, pos.y), new Vector3(radius * 2f, 0.04f, radius * 2f), mat);
            _markers.Add(new Marker { Go = go, Ttl = duration, Total = duration, Mat = mat });
        }

        /// <summary>v20：敌人出生预警——原地先出现一个闪烁标记，2 秒后才真正生成敌人。</summary>
        private void OnSpawnWarning(SpawnWarningEvent e)
        {
            GameObject go = Prim(PrimitiveType.Cylinder, "SpawnWarning",
                new Vector3(e.Pos.x, 0.06f, e.Pos.y),
                new Vector3(1.5f, 0.03f, 1.5f),
                // v176：颜色恒定 ⇒ 走缓存（刷怪预警每波都在新建，原来每个都漏一份材质）
                MatCached(new Color(1f, 0.35f, 0.25f, 1f), true));
            _warnMarkers.Add(go);
            _warnTimers.Add(Mathf.Max(0.2f, e.Seconds));
            // ★ v177：敌人出生预警也照亮（需求③）★
            // 预警圈原本是贴地半透明贴图，夜里几乎看不见；加一盏红光之后
            // "哪里要出敌人"在暗环境里也读得到。
            NightLighting.Illuminate(new Vector3(e.Pos.x, 0.5f, e.Pos.y),
                new Color(1f, 0.32f, 0.22f), 3.2f, 1.8f, 0.45f);
        }

        private readonly List<GameObject> _warnMarkers = new List<GameObject>();
        private readonly List<float> _warnTimers = new List<float>();

        private void TickWarnMarkers(float dt)
        {
            for (int i = _warnMarkers.Count - 1; i >= 0; i--)
            {
                _warnTimers[i] -= dt;
                GameObject go = _warnMarkers[i];
                if (go != null)
                {
                    // 闪烁：越接近生成闪得越快
                    float blink = Mathf.PingPong(Time.time * 8f, 1f);
                    float s = Mathf.Lerp(0.6f, 1.6f, blink);
                    go.transform.localScale = new Vector3(s, 0.03f, s);
                }
                if (_warnTimers[i] > 0f) continue;
                if (go != null) Object.Destroy(go);
                _warnMarkers.RemoveAt(i);
                _warnTimers.RemoveAt(i);
            }
        }

        private void OnTelegraph(JumpTelegraphEvent e)
        {
            AddMarker(e.To, e.Radius, Mathf.Max(0.2f, e.Duration), new Color(1f, 0.25f, 0.2f, 1f));
        }

        private void OnField(EnemyAttackFieldEvent e)
        {
            AddMarker(e.Pos, e.Radius, 0.25f, new Color(1f, 0.6f, 0.1f, 1f));
        }

        /// <summary>
        /// ★ v180：锄头的**刃形**网格（重做"扇形判定框"）★
        ///
        /// ── 被替掉的是什么 ──
        /// 旧版是 `ArcMesh()`：内半径 0.55 / 外半径 1.0 / 张角 110° 的**扇形面片**，
        /// **零厚度**、法线朝上的一整片 —— 玩家反馈的"扇形判定框"就是它 ✓
        /// 一片零厚度的扇形在俯视图里就是"地上亮起一块扇形色块"，读起来像 UI 判定区，
        /// 不像"一把锄头劈过去"✗
        ///
        /// ── 现在是什么 ──
        /// **有厚度的刃**：沿弧线扫过去的一条刀身，横截面是"内厚外尖"的楔形
        ///   · 内缘（`inner = 0.62`）厚度 `±0.055` —— 这是刀背
        ///   · 外缘（`outer = 1.0`）厚度 `±0.004` —— 收成一**条刃口**
        ///   · 张角 ±48°（★ v205：改前是 ±55° —— 比判定半张角 50° 还宽 5°/侧，
        ///     于是贴在弧边缘的怪"看着被劈到却不掉血" ✗ 现在 = `MeleeFxArcDeg(arc)/2`，
        ///     即"判定张角 − 4°"，由 `RunSession.MeleeFxArcDeg` 一处定义 ✓）
        /// ⇒ 亮起来的是"一条有体积的刃"，不是"一块扇形面"✓
        /// ⇒ 而且厚度在 Y 上，`TickSwings` 只缩放 XZ ⇒ **刃的厚度不随射程变大** ✓
        ///
        /// ★ 双面绕序（每个三角正反各发一次）★
        /// 这份网格是 4 个四边形环（上面 / 下面 / 内缘 / 刃口），逐面验绕序容易把某一面判反，
        /// 判反的那面会被背面剔除 ⇒ 表现是"刃缺了一角"，而且只在某个角度看才缺 ✗
        /// 320 个三角的代价可以忽略，换来的是"任何角度都不会因为绕序被剔除"✓
        /// 与 `FxShardMesh` / `FxFrustumMesh` 一样是 **flat shading**（按面拆顶点 = 硬边）✓
        /// </summary>
        private static Mesh FxBladeMesh()
        {
            const int key = 5500;
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            const int segs = 20;
            const float inner = 0.62f, outer = 1.0f;
            const float thickIn = 0.055f, thickOut = 0.004f;
            const float halfDeg = 48f;      // ★ v205：55 → 48 = `MeleeFxArcDeg(100°)/2`（判定 50° 在外圈）★
            var tri = new List<Vector3>(segs * 48);

            // 每个采样角的 4 个截面点：内上 / 外上 / 外下 / 内下
            Vector3[] v = new Vector3[4];
            for (int i = 0; i < segs; i++)
            {
                float a0 = Mathf.Deg2Rad * Mathf.Lerp(-halfDeg, halfDeg, i / (float)segs);
                float a1 = Mathf.Deg2Rad * Mathf.Lerp(-halfDeg, halfDeg, (i + 1) / (float)segs);
                Vector3[] p0 = Section(a0, inner, outer, thickIn, thickOut);
                Vector3[] p1 = Section(a1, inner, outer, thickIn, thickOut);
                // 0=内上 1=外上 2=外下 3=内下
                Quad(tri, p0[0], p1[0], p1[1], p0[1]);     // 上面（刀背 → 刃口）
                Quad(tri, p0[3], p1[3], p1[2], p0[2]);     // 下面
                Quad(tri, p0[0], p1[0], p1[3], p0[3]);     // 内缘（刀背的立面）
                Quad(tri, p0[1], p1[1], p1[2], p0[2]);     // 刃口（薄边）
            }
            Mesh m = BuildFlat("FxBlade", tri);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>一条弧上的 4 点截面（内上 / 外上 / 外下 / 内下）。</summary>
        private static Vector3[] Section(float angleRad, float inner, float outer,
                                         float thickIn, float thickOut)
        {
            float s = Mathf.Sin(angleRad), c = Mathf.Cos(angleRad);
            return new[]
            {
                new Vector3(s * inner,  thickIn,  c * inner),
                new Vector3(s * outer,  thickOut, c * outer),
                new Vector3(s * outer, -thickOut, c * outer),
                new Vector3(s * inner, -thickIn,  c * inner),
            };
        }

        /// <summary>一个四边形 ⇒ 2 个三角 × **正反两遍**（双面，见 `FxBladeMesh`）。</summary>
        private static void Quad(List<Vector3> tri, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            AddTri(tri, a, b, c);
            AddTri(tri, a, c, d);
            AddTri(tri, a, c, b);        // 反向
            AddTri(tri, a, d, c);        // 反向
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v185：**弯月**剑气网格（`FxCrescentMesh`）★
        //
        // ── 选型：新网格，不是"把 `FxBladeMesh` 转 90°" ──
        // `FxBladeMesh`（v180）确实也是"一段圆弧扫出的厚片"，但它**径向厚度处处相同**
        // （`inner = 0.62`、`outer = 1.0` 常数）⇒ 俯视图里是一条**等宽的弧带**，
        // 两端是**平头**的，读起来像"环的一截"，不像月亮 ✗
        // 而"弯月"的定义性特征恰恰是**两端收尖、中段最厚** —— 那是厚度随角度变的形状，
        // 靠旋转一个等厚弧带**变不出来**（转到哪个角度都还是等宽平头）✓
        // 所以按需求给的第二个选项"一段圆弧扫掠出的厚片"重做一张：
        //   · 外缘 `outer = 1.0`（常数，弧面）
        //   · 内缘 `inner(θ)` 随角度**外扩**到与外缘相交 ⇒ 两端收成**尖角（刃尖）** ✓
        //   · 厚度 `thick(θ)` 同一条曲线收细 ⇒ 光影上也是"中间厚、两边薄" ✓
        // 张角仍然是 ±55°（与判定框 `MeleeBoxHalfWidth` 的基准一致，见 `SwordWaveHalfWidth` 那条链）✓
        //
        // ── 为什么是**归一化**尺寸（外径 1.0）──
        // 弯月的世界尺寸必须**等于判定框**（"看到的 = 打得到的"）。
        // 但它宽深比不是 1（框半宽 2.91m vs 前向半长 1.9m），等比缩放贴不住 ✗
        // ⇒ 网格按外径 1.0 造，生成时由 `MeshBit.SclAxis` 给出逐轴倍率（X 贴半宽、Z 贴半长）✓
        //
        // ── 与既有特效同一套语言 ──
        // 双面绕序（任一角度的背面剔除都不会让它"缺一块"）、flat shading（硬边）、
        // 走 `_fxMeshCache`（键 5800，与 5100+/5200/5400/5500/5600-5820 都不撞）⇒
        // **运行期 0 次 `new Mesh`** ✓ 材质走 `VfxMatCached` ⇒ **0 次 `new Material`** ✓
        // 离线同源副本：`Tools/mesh_gen/gen_crescent_mesh.py`（数字钉死，改一边必须改另一边）
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>弯月的采样段数（与 `gen_crescent_mesh.py` 的 `SEGS` 逐字对应）。</summary>
        private const int CrescentSegs = 24;
        /// <summary>弯月的半张角（度）。两端在这里收成刃尖。</summary>
        private const float CrescentHalfDeg = 55f;
        /// <summary>
        /// 中段最厚处的相对厚度（相对外径 1.0）：内缘缩到 `1 - thickness`。
        /// ★ 0.56（v185 实测后从 0.34 加厚）★
        /// 0.34 时中段内缘在 0.66 ⇒ 径向只有 0.34 宽的一条带，
        /// 俯视 5.8m 宽 × 0.65m 深 ⇒ 屏幕上是一条**细长叶片**，读不出"月牙"✗
        /// 加厚到 0.56（内缘 0.44）⇒ 中段深 ≈1.1m，才是一弯**月牙**的形状 ✓
        /// </summary>
        private const float CrescentThickness = 0.46f;
        /// <summary>中段最厚处的**半厚**（Y 方向，相对外径）。两端收成 0。0.035 让它在俯视时有体积感。</summary>
        private const float CrescentHalfThickY = 0.035f;

        /// <summary>
        /// 弯月截面上"厚度随角度"的曲线：`1` = 中段（θ=0）、`0` = 两端刃尖。
        ///
        /// ★ v185 实测后改了两次 ★
        ///   ① `cos(u·π/2)`      —— 中段就开始收，整条弧厚薄渐变 ⇒ 俯视是一条**两头尖的叶片**（两端太早收细）✗
        ///   ② `cos^0.45`        —— 中段**保持厚度**、只在最后 15~20% 的角度里快速收成刃尖
        ///      ⇒ 屏幕上剩下的是"一条等宽的弧 + 两个尖"，那才是**月牙**的形状 ✓
        /// 幂次写在这里而不是直接写 `sqrt`：`0.45` 是为了让"开始收细"的位置落在
        /// ±48° 附近（`cos(48/55·π/2)^0.45 ≈ 0.55`），与判定框的 ±50° 半张角几乎重合 ✓
        /// </summary>
        private static float CrescentTaper(float angleRad)
        {
            float u = Mathf.Clamp(Mathf.Abs(angleRad) / (CrescentHalfDeg * Mathf.Deg2Rad), 0f, 1f);
            // ★ v187 修复（用户报"剑气直接报错"）：**先判余弦，再取幂** ★
            //
            // 病灶（实测，不是推测）：`Mathf.Cos(1f * Mathf.PI * 0.5f)` **不是 0**，而是 **-4.371139e-08**
            //   —— `Mathf.PI * 0.5f` 这个 float 比真正的 π/2 略**大** 一点点（1.5707964f），
            //      于是余弦落到**负侧**。而 `Mathf.Pow(负数, 0.45f)` = **NaN**（.NET：负底数 + 非整数指数）✗
            //   `u = 1` 恰好是**两端刃尖**（`a0` 在 i=0、`a1` 在 i=23 各命中一次）⇒
            //   那两处截面拿到 `taper = NaN` ⇒ `inner` 与 `hy` 全变 NaN ⇒ 顶点 (NaN, NaN, NaN) ✓
            //
            // 症状（用户看到的就是这个）：
            //   `Mesh 'FxCrescent': abnormal mesh bounds ... min=(0.77, -nan(ind), 0.57)`
            //   ＋ 渲染器每帧 `Invalid worldAABB. Object is too large or too far away from the origin.`
            //   ＋ `Invalid AABB aabb/b` 一连串 —— 控制台一片红，"剑气会直接报错" ✓
            //
            // 修法：`!(c > 0f)` 一次挡掉 **NaN / 0 / 负数** 三种情况 ⇒ 返回 0（正是"刃尖"的语义：
            //   内缘贴住外缘、厚度收到 0），并且**永远不会**把 NaN 递给 `Mathf.Pow` ✓
            // `Mathf.Max` 之类**挡不住 NaN**（`0.0008f > NaN` 为假 ⇒ 返回 NaN），所以这一道必须在这里 ✓
            float c = Mathf.Cos(u * Mathf.PI * 0.5f);
            if (!(c > 0f)) return 0f;
            return Mathf.Pow(c, 0.45f);
        }

        /// <summary>v185：弯月剑气网格（归一化：外径 1.0，局部 +Z = 飞行朝向）。</summary>
        private static Mesh FxCrescentMesh()
        {
            const int key = 5800;
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var tri = new List<Vector3>(CrescentSegs * 48);
            // 每个采样角 4 个截面点：内上 / 外上 / 外下 / 内下
            for (int i = 0; i < CrescentSegs; i++)
            {
                float a0 = Mathf.Deg2Rad * Mathf.Lerp(-CrescentHalfDeg, CrescentHalfDeg, i / (float)CrescentSegs);
                float a1 = Mathf.Deg2Rad * Mathf.Lerp(-CrescentHalfDeg, CrescentHalfDeg, (i + 1) / (float)CrescentSegs);
                Vector3[] p0 = CrescentSection(a0);
                Vector3[] p1 = CrescentSection(a1);
                Quad(tri, p0[0], p1[0], p1[1], p0[1]);     // 上面（内缘 → 外缘）
                Quad(tri, p0[3], p1[3], p1[2], p0[2]);     // 下面
                Quad(tri, p0[0], p1[0], p1[3], p0[3]);     // 内缘（凹面）
                Quad(tri, p0[1], p1[1], p1[2], p0[2]);     // 外缘（凸的刃背）
            }
            Mesh m = BuildFlat("FxCrescent", tri);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>
        /// 弯月在一个角度上的 4 点截面（内上 / 外上 / 外下 / 内下），外径归一到 1.0。
        /// 两端 `taper → 0` ⇒ 内缘贴住外缘、厚度归零 ⇒ **收成刃尖** ✓
        /// </summary>
        private static Vector3[] CrescentSection(float angleRad)
        {
            float taper = CrescentTaper(angleRad);
            float outer = 1f;
            float inner = 1f - CrescentThickness * taper;
            float hy = Mathf.Max(0.0008f, CrescentHalfThickY * taper);
            float s = Mathf.Sin(angleRad), c = Mathf.Cos(angleRad);
            return new[]
            {
                new Vector3(s * inner,  hy, c * inner),
                new Vector3(s * outer,  hy * 0.30f, c * outer),
                new Vector3(s * outer, -hy * 0.30f, c * outer),
                new Vector3(s * inner, -hy, c * inner),
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // v179：三种攻击的**实体颗粒**网格（棱面 / 硬边 / 鲜明色块）
        //
        // ★ 为什么运行时构造写在**本文件**，而不是 `Tools/mesh_gen/` ★
        //   `Tools/` 在 `Assets/` **之外**。Unity 只编译 `Assets/` 与 `Packages/`，
        //   而 `Seedfall.Runtime.csproj` 用的是**显式** `<Compile Include="Assets\...">`
        //   ⇒ 放在 `Tools/` 里的 .cs **不会被任何一方编译**，`WorldView` 也就引用不到 ✗
        //   所以分工是：
        //     · 本文件            = 运行时构造（唯一能被编译的位置）
        //     · `Tools/mesh_gen/gen_fx_meshes.py`    = 离线生成**同一套拓扑**的 OBJ 预览 + 清单
        //     · `Tools/mesh_gen/verify_counts.py`    = 与下面这些常量**逐项比对**（顶点/三角数）
        //   两边的数字是钉死的（见各生成器上方的注释），改一边必须改另一边 ✓
        //
        // ★ 全部 flat shading —— 硬边的来源 ★
        //   每种网格都**按面拆顶点**（每个三角形独占 3 个顶点），再由 `RecalculateNormals`
        //   得到**逐面法线** ⇒ 棱面、硬边、没有平滑过渡 ✓
        //   这就是"三种特效看得出是同一种颗粒语言"的技术基础：
        //   它们不是三套美术，是同一套「低面数硬边多面体」的三个用法 ✓
        //
        // ★ 全部走 `VfxMatCached`（第⑤轮那个 ~60 份/秒 的材质泄漏）★
        //   共享材质的契约是「**永不逐实例改写**」⇒ 下面所有颗粒的"淡出"一律靠
        //   **缩放**（S0→S1）而不是改 alpha；需要"白闪"的地方换用**另一份已缓存**的材质 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>v179：程序化网格的缓存。键是调用方给的常量 ⇒ **只建一次**，之后全局共用。</summary>
        private static readonly Dictionary<int, Mesh> _fxMeshCache = new Dictionary<int, Mesh>();

        /// <summary>v179：把"每面三个顶点"的三角汤建成 mesh —— **逐面法线 = 硬边**。
        ///
        /// ★ v187：**NaN / ∞ 顶点不许进网格**（用户报"剑气直接报错"的那道闸）★
        /// 一个 NaN 顶点就够让 `Mesh.RecalculateBounds()` 得到 NaN bounds ⇒
        ///   ① Unity 报 `Mesh 'xxx': abnormal mesh bounds`
        ///   ② 渲染器**每一帧**再报 `Invalid worldAABB` / `Invalid AABB aabb`（红字刷屏，且没人知道来自哪张网格）
        ///   ⇒ 症状与"崩了"无异，但**没有任何托管异常**、栈里也看不到出事的那一行 ✗
        /// 所以这道闸开在**唯一建网格的地方**：进来先检查，坏了就**退化成原点**并点名，
        /// 让"哪张网格、几个顶点"直接出现在日志里，而不是留给渲染器去喊 AABB ✓
        /// （根因修复在 `CrescentTaper`；这里是"下一张网格写错时也能立刻定位"的保险）
        /// </summary>
        private static Mesh BuildFlat(string name, List<Vector3> triSoup)
        {
            var verts = triSoup.ToArray();
            int bad = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                    float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
                {
                    verts[i] = Vector3.zero;
                    bad++;
                }
            }
            if (bad > 0)
                Log.Warn($"程序化网格 {name}：{bad}/{verts.Length} 个顶点是 NaN/∞ ⇒ 已退化成原点（否则 Unity 会刷 Invalid worldAABB）");
            var tris = new int[verts.Length];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
            var m = new Mesh { name = name };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static void AddTri(List<Vector3> tri, Vector3 a, Vector3 b, Vector3 c)
        {
            tri.Add(a); tri.Add(b); tri.Add(c);
        }

        /// <summary>v179：确定性抖动 `[-0.5, 0.5]`（LCG）。网格要缓存 ⇒ 不能用 `Random`。</summary>
        private static float NextJitter(ref uint seed)
        {
            seed = seed * 1664525u + 1013904223u;
            return ((seed >> 9) & 1023u) / 1023f - 0.5f;
        }

        /// <summary>
        /// v179：一块**多面体碎片** —— 八面体 + 各轴拉伸 + 确定性抖动。
        /// 6 极点 / 8 面 ⇒ flat 后 **24 顶点 / 8 三角**。
        /// `variant` 只决定"拉长方向"与抖动种子 ⇒ 12 块碎片看不出是同一个模子 ✓
        /// </summary>
        private static Mesh FxShardMesh(int variant)
        {
            int key = 5100 + variant;
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            // 八面体的 6 个极点：0=+x 1=-x 2=+y 3=-y 4=+z 5=-z
            Vector3[] p =
            {
                new Vector3( 1f, 0f, 0f), new Vector3(-1f, 0f, 0f),
                new Vector3( 0f, 1f, 0f), new Vector3( 0f,-1f, 0f),
                new Vector3( 0f, 0f, 1f), new Vector3( 0f, 0f,-1f),
            };
            // 每个 variant 一根不同的"拉长轴"⇒ 碎片形状彼此不重复
            Vector3[] stretch =
            {
                new Vector3(1.00f, 1.60f, 0.50f),
                new Vector3(1.50f, 0.60f, 0.80f),
                new Vector3(0.70f, 1.05f, 1.60f),
                new Vector3(1.25f, 0.95f, 1.05f),
                new Vector3(0.55f, 1.40f, 0.90f),
                new Vector3(1.10f, 1.15f, 0.62f),
            };
            Vector3 st = stretch[variant % stretch.Length];

            uint seed = 0x9E3779B9u * (uint)(variant + 1) + 12345u;
            for (int i = 0; i < p.Length; i++)
            {
                float jx = NextJitter(ref seed), jy = NextJitter(ref seed), jz = NextJitter(ref seed);
                p[i] = new Vector3((p[i].x + jx * 0.26f) * st.x,
                                   (p[i].y + jy * 0.26f) * st.y,
                                   (p[i].z + jz * 0.26f) * st.z);
            }

            // 8 个面，绕序保证**外法线朝外**（`Tools/mesh_gen/gen_fx_meshes.py` 用同一份绕序）
            int[] f =
            {
                0, 2, 4,  4, 2, 1,  1, 2, 5,  5, 2, 0,
                4, 3, 0,  1, 3, 4,  5, 3, 1,  0, 3, 5,
            };
            var tri = new List<Vector3>(24);
            for (int i = 0; i < f.Length; i++) tri.Add(p[f[i]]);
            Mesh m = BuildFlat("FxShard" + variant, tri);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>
        /// v179：一段**六棱台**（水柱的一段）。`sides` 个侧面 + 两个端盖。
        /// 每段 `sides × 4` 个三角 ⇒ sides=6 时 **24 三角 / 72 顶点**（flat）。
        /// ★ 硬边 + 直棱线 ★ —— 这是"水柱由一段段固体水拼成"而不是"一条发光的面片"的读法 ✓
        /// </summary>
        private static Mesh FxFrustumMesh(int key, int sides, float rA, float rB)
        {
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var tri = new List<Vector3>(sides * 12);
            var a = new Vector3[sides];
            var b = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float t = (i / (float)sides) * Mathf.PI * 2f;
                float cs = Mathf.Cos(t), sn = Mathf.Sin(t);
                a[i] = new Vector3(cs * rA, sn * rA, -0.5f);   // 后端环（局部 -Z）
                b[i] = new Vector3(cs * rB, sn * rB, 0.5f);    // 前端环（局部 +Z）
            }
            var ca = new Vector3(0f, 0f, -0.5f);
            var cb = new Vector3(0f, 0f, 0.5f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                AddTri(tri, a[i], b[j], b[i]);      // 侧面（外法线朝外）
                AddTri(tri, a[i], a[j], b[j]);
                AddTri(tri, ca, a[j], a[i]);        // 后端盖（法线 -Z）
                AddTri(tri, cb, b[i], b[j]);        // 前端盖（法线 +Z）
            }
            Mesh m = BuildFlat("FxFrustum" + key, tri);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>
        /// v179：**二十面体一级细分的球壳**（肥料爆炸的那层"皮"）。
        /// 12 顶点 / 20 面 → 每面四分 ⇒ **80 三角 / 240 顶点**（flat）。
        /// 一级细分是规格给定的档位，也正好是"看得出棱、又不像低模球"的那一档 ✓
        /// </summary>
        private static Mesh FxIcoShellMesh(int subdiv) => FxIcoShellMesh(subdiv, 1f);

        /// <summary>
        /// v180：同一个球壳，但**Y 方向压扁**（`squashY < 1`）。
        ///
        /// 为什么要在**网格里**压扁，而不是给实例设一个非等比 `localScale` ✗：
        /// 这个网格的所有实例都走 `MeshBit`，而它每帧写的是 `Vector3.one * Lerp(S0,S1,k)`
        /// **等比**缩放 ⇒ 实例上设的压扁会被下一帧覆盖掉 ✗（"设了没生效"的典型形态）
        /// 压扁烘进网格之后，等比缩放照旧，形状仍然是"云帽"而不是"球"✓
        /// </summary>
        private static Mesh FxIcoShellMesh(int subdiv, float squashY)
        {
            int key = squashY >= 0.999f ? 5200 + subdiv : 5250 + subdiv;
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1f,  t, 0f), new Vector3( 1f,  t, 0f), new Vector3(-1f, -t, 0f), new Vector3( 1f, -t, 0f),
                new Vector3( 0f, -1f, t), new Vector3( 0f,  1f, t), new Vector3( 0f, -1f,-t), new Vector3( 0f,  1f,-t),
                new Vector3( t,  0f,-1f), new Vector3( t,  0f, 1f), new Vector3(-t,  0f,-1f), new Vector3(-t,  0f, 1f),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;

            var idx = new List<int>
            {
                 0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                 1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
                 3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                 4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            };

            var mids = new Dictionary<long, int>();
            for (int s = 0; s < Mathf.Max(1, subdiv); s++)
            {
                var next = new List<int>(idx.Count * 4);
                for (int i = 0; i < idx.Count; i += 3)
                {
                    int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                    int ab = IcoMidpoint(v, mids, a, b);
                    int bc = IcoMidpoint(v, mids, b, c);
                    int ca = IcoMidpoint(v, mids, c, a);
                    next.Add(a);  next.Add(ab); next.Add(ca);
                    next.Add(ab); next.Add(b);  next.Add(bc);
                    next.Add(ca); next.Add(bc); next.Add(c);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }
                idx = next;
            }

            var tri = new List<Vector3>(idx.Count);
            for (int i = 0; i < idx.Count; i++)
            {
                Vector3 p = v[idx[i]];
                // v180：压扁烘进顶点（等比实例缩放才不会把形状改回球）
                if (squashY < 0.999f) p.y *= squashY;
                tri.Add(p);
            }
            Mesh m = BuildFlat("FxIcoShell" + subdiv + (squashY < 0.999f ? "_s" : ""), tri);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>v179：细分用的边中点。`(a,b)` 归一化成同一个键 ⇒ 相邻面**共用**同一个中点。</summary>
        private static int IcoMidpoint(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int hit)) return hit;
            int id = v.Count;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = id;
            return id;
        }

        /// <summary>
        /// v179：**地面湿痕**贴花 —— 一张贴地圆盘（`segs` 段 ⇒ `segs` 三角 / `segs×3` 顶点）。
        /// ★ 它是一片、但不是 billboard ★ 它平平地躺在 y≈0.03 的地上，
        /// 和水柱共用同一套"固体颗粒"，只是被压平了 ✓
        /// ★ 共享材质不能 alpha 淡出 ⇒ 用"先铺开"表达溅开，结束时整片撤掉 ✓
        /// </summary>
        private static Mesh FxDiscMesh(int key, int segs)
        {
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var tri = new List<Vector3>(segs * 3);
            Vector3 c = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float t0 = (i / (float)segs) * Mathf.PI * 2f;
                float t1 = ((i + 1) / (float)segs) * Mathf.PI * 2f;
                // 绕序：从 +Y 往下看顺时针 ⇒ 法线朝上（贴地才看得见）
                AddTri(tri, c,
                    new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1)),
                    new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0)));
            }
            Mesh m = BuildFlat("FxDisc" + key, tri);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>
        /// v180：一圈**细环**（也是 "圆" 的替代品）。
        /// `innerRatio` 是内半径 / 外半径：0.86 = 环宽 14% —— 读作"边界"，不是"填充区"✓
        /// 单位尺度（外半径 1），调用方用 XZ 缩放对到实际半径 ⇒ 一份网格服务所有尺寸 ✓
        /// </summary>
        private static Mesh FxRingMesh(int key, int segs, float innerRatio)
        {
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var tri = new List<Vector3>(segs * 12);
            for (int i = 0; i < segs; i++)
            {
                float t0 = (i / (float)segs) * Mathf.PI * 2f;
                float t1 = ((i + 1) / (float)segs) * Mathf.PI * 2f;
                Vector3 o0 = new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0));
                Vector3 o1 = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
                Vector3 i0 = o0 * innerRatio;
                Vector3 i1 = o1 * innerRatio;
                // 绕序：从 +Y 往下看顺时针 ⇒ 法线朝上（贴地才看得见），双面各发一遍见 `Quad`
                Quad(tri, i0, o0, o1, i1);
            }
            Mesh m = BuildFlat("FxRing" + key, tri);
            _fxMeshCache[key] = m;
            return m;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v203：玩家**脚下的朝向指示** —— 网格 + 材质（★ 本段之外一行都没动 ★）
        //
        //  用户原话："为玩家面向位置增加一个指示，放在玩家脚下"
        //
        //  ── 为什么需要它 ──
        //  豆豆人的正面有眼睛 / 胸兜 / 靴尖（`PlayerBean.Build` 那几段），但它戴一顶
        //  **直径 0.68m 的帽檐**（`PlayerBean.HatBrimR = 0.340`），而游戏机位是 60° 俯角
        //  （`tuning.camera.pitch`）⇒ 俯视时帽檐正好压住眼睛那一圈，
        //  "朝哪边"在实战机位上读不出来 ✗ —— 所以朝向要在**地面**上再表达一次 ✓
        //
        //  ── 为什么网格/材质在这个文件、而物件挂在 `PlayerBean` 下 ──
        //  ① 网格走 `_fxMeshCache`（本工程所有程序化网格的唯一缓存，与 `FxRingMesh`/`FxDiscMesh`
        //     同一套写法）⇒ 键号必须在这里登记，才不会与 5400/5500/5600-5740 撞 ✗
        //  ② 物件挂在**模型根节点**下 ⇒ 它的朝向**就是**模型的最终朝向
        //     （`Go.transform.rotation = Euler(0,yaw,0) * BaseRotation`，见 `ModelView.SetFacing`）
        //     ⇒ "指示与模型同一帧到位"不是"同步代码写对了"，而是**结构上不可能不同步** ✓
        //  ③ 挂进模型还有第二个必要理由：`PlayerShowcase` 与验收探针建的也是
        //     `ModelView.SpawnPlayer` ⇒ 它们摆出来的豆豆人**同样带着指示**，
        //     于是"朝北 / 朝东 / 转身中 / 俯视"四张图能在**同一条生产路径**上拍出来 ✓
        //
        //  ── ★★ v204：**改成 2D 贴图**（用户原话："重做方向指标，用2d贴图方式展示，
        //     一个圆环包裹玩家，一个圆形代表方向"）★★ ──
        //
        //  ── 改前是什么、为什么用户要换 ──
        //  改前是一张**程序化扇形网格**（`FacingFan`：内半径 0.50m、前尖 1.50m、半张角 58°），
        //  用 URP Lit 不透明材质画。它把"前"写进形状里，方向确实读得出来 ✓
        //  但用户要的是另一种**读法**：一圈环把玩家**包起来**（"我在这个圈的中心"），
        //  环上再放一个**圆点**指方向（"它在这一侧"）✓ 这比一个箭头更像"仪表"而非"贴纸"✓
        //  ⇒ 形状、画法、材质三样一起换：
        //      旧：程序化三角网格（`BuildFlat` + `_fxMeshCache`）＋ `URP/Lit` 不透明
        //      新：**一张贴图 + 一个 quad 面片**（`Texture2D` 程序化生成，alpha 定形状）
        //          ＋ `Sprites/Default`（无光照、读 `_MainTex` 的 alpha ⇒ 圆环 / 圆点都是"画"出来的）
        //
        //  ── 为什么"贴图"而不是"网格"（用户点名要的那条）──
        //  圆环的**内外半径由贴图 alpha 决定**（不是网格顶点）：改粗细只改一张 96×96 的图，
        //  不用碰任何几何；而且边缘能做成**硬边像素**（见下）——网格做不出那种台阶 ✓
        //  代价是一次 `new Texture2D`，所以**全程只生成一次**、静态缓存（见 `FacingRingTex`），
        //  三个玩家（以及 `PlayerShowcase` / 探针摆的样板）**共用同一张图、同一份材质** ✓
        //
        //  ── 为什么是"硬边像素风"、与图标同一套 ──
        //  用户原话："与图标同一套硬边像素风格"。图标（`Tools/icon_gen/gen_item_png.py`）的规范是
        //  ① 调色板 ≤ 6 色 ② **无渐变、无抗锯齿，alpha 只有 0 或 255** ③ 2px 暖灰描边
        //  ⇒ 这里逐条照做：`alpha ∈ {0,255}` + 2px `#3A3A38` 描边 + `FilterMode.Point`（不插值）✓
        //  三个颜色直接取图标那套：描边 OUT `#3A3A38`、环体 HI `#F2EFE6`、方向点 ACC 绿 `#8CE891` ✓
        //
        //  ── 朝向读的是哪个角（★ 这一条是**对的**，v203 定的，别丢 ★）──
        //  读**模型最终角**：整个指示挂在模型根节点下，方向点就摆在"本地 +Z"上。
        //  `ModelView.SetFacing` 写的是根节点的 rotation ⇒ 方向点与模型**同一条 Transform**，
        //  "同一帧到位"是结构保证，不存在晚一帧、也不存在同步代码写错 ✓
        //
        //  ── 尺寸 / 抬高（★ 世界米 ⇄ 模型米，两个数都报 ★）──
        //  quad 的顶点是**单位正方形**（模型米），靠 `localScale` 缩到设计尺寸；
        //  模型米 × `CharScale × PlayerScale = 1.6302` = 场上米：
        //      环外半径 **1.15m** 场上（= 0.7054 模型米，quad 边长 1.4108）
        //      环内半径 **0.782m**（= 外半径 × 0.68，**由贴图 alpha 定**）
        //      方向点直径 **0.36m**、圆心距 **1.02m**（贴在环带的中外段，像"环上的珠子"）
        //      离地 **0.016m** ← 消 Z-fighting（与旧版同一个量级；`Sprites/Default` 是 ZWrite Off，
        //      抬起来仍然必要：不抬就会被地面的深度挡掉，或被判成"和地面同深度"而闪）
        //  ★ 为什么外半径取 **1.15**（区间 0.9~1.2 的上部）★
        //  60° 俯角下玩家的身体/帽檐在地面上的遮挡半径实测 ≈ 1.13m（`ProbeV203Facing` 量过），
        //  环再小就整圈藏在身体后面；1.15 让"背对相机"时环的外缘与方向点**仍露出一点** ✓
        //  ★ 不挡脚 ★ 内半径 0.782m ≫ 靴子最远点 0.346m（旧版是 0.50 vs 0.346，还叠了 2.6cm；
        //  现在**几何上完全不重叠**，而且 `Sprites/Default` 是 alpha 混合、不写深度 ⇒ 就算重叠也不糊脚）✓
        // ══════════════════════════════════════════════════════════════════════

        // ── 设计值（场上米 / 贴图像素）—— 探针直接读这几个常量做判据 ──
        /// <summary>环外半径（场上米）。区间 0.9~1.2 的上部，理由见上方"为什么外半径取 1.15"。</summary>
        public const float FacingRingOuterM = 1.15f;
        /// <summary>环内半径 / 外半径。<b>形状的真值在贴图 alpha 里</b>，这个数同时供贴图生成与探针复算。</summary>
        public const float FacingRingInnerRatio = 0.68f;
        /// <summary>离地抬高（场上米）：消 Z-fighting。</summary>
        public const float FacingLiftM = 0.016f;
        /// <summary>
        /// 方向点圆心到玩家的距离（场上米）。
        ///
        /// ★ 为什么是 **1.12**（≈ 环外半径 1.15）而不是环带中点 ★
        ///   `ProbeV203Facing.RunCam` 实测（8 个相机方位、60° 俯角、开/关差分）：
        ///     圆点放在**环带中点**（圆心距 1.02、直径 0.36）时，
        ///     正对相机 282 px、斜后 257 px，但**侧对只有 31 px、背对 0 px** ✗
        ///   原因是几何的：60° 俯角下，帽檐（场上半径 0.554m，高 1.71m）在地面上的投影
        ///   要伸到前方约 `0.554 + 1.71/tan60° ≈ 1.54m` ⇒ 直径 0.36 的点整颗都在阴影里 ✗
        ///   把点挪到**环的外沿**并放大到 0.44 ⇒ 它越过 1.2m、露出更多（读数见报告）✓
        ///   仍然"在环上"（内半圈压在环带上），同时"看得出朝哪" ✓
        /// </summary>
        public const float FacingDotCenterM = 1.12f;
        /// <summary>方向点直径（场上米）。0.36 → **0.44**：为了让它在"背对相机"时也能露出一点，见上一条 ✓</summary>
        public const float FacingDotDiamM = 0.44f;
        /// <summary>环贴图边长（像素）。96 ⇒ 1 texel ≈ 2.4cm 场上，硬边台阶肉眼可见但不会碎成渣。</summary>
        public const int FacingRingTexSize = 96;
        /// <summary>方向点贴图边长（像素）。</summary>
        public const int FacingDotTexSize = 24;
        /// <summary>描边宽度（贴图像素）—— 与图标同一规范：2px 暖灰。</summary>
        public const float FacingOutlinePx = 2f;

        // ── 调色板：**直接 import 自图标那一套**（`gen_item_png.py`），不另造颜色 ──
        /// <summary>描边 `#3A3A38`（图标 OUT）</summary>
        internal static readonly Color FacingPixelOutline = new Color(0.227f, 0.227f, 0.220f, 1f);
        /// <summary>环体 `#F2EFE6`（图标 HI）—— 比任何地面色都亮、且不受光照 ⇒ 夜里也看得见</summary>
        internal static readonly Color FacingPixelBody = new Color(0.949f, 0.937f, 0.902f, 1f);
        /// <summary>方向点 `#8CE891`（图标 ACC 绿）—— 环上唯一的彩色，方向一眼落到它身上</summary>
        internal static readonly Color FacingPixelAccent = new Color(0.549f, 0.910f, 0.569f, 1f);

        /// <summary>朝向指示的**四边形网格**建过几份（只给探针读 ⇒ 证明"全程只建一次"）。</summary>
        public static int FacingQuadMeshBuilds;
        /// <summary>朝向指示的两张贴图**生成过几次**（只给探针读 ⇒ 证明"没有 per-instance 建贴图"）。</summary>
        public static int FacingTexBuilds;

        /// <summary>
        /// 朝向指示用的**单位四边形**（1×1，躺在 XZ 平面、中心在原点、y = 0）。
        ///
        /// ★ 为什么网格这么小、形状却在贴图里 ★
        ///   圆环 / 圆点都是**贴图 alpha** 画出来的 ⇒ 网格只需要"能摆一块方片"这一个能力 ✓
        ///   顶点 4 个、三角 4 个（双面各 2）：双面的理由是**兜底材质**（万一 `Sprites/Default`
        ///   取不到、退成别的着色器）时低机位也看得见；`Sprites/Default` 本身是 `Cull Off` ✓
        /// ★ 法线写死 (0,1,0) ★ 双绕序会被 `RecalculateNormals` 相互抵消成零向量
        ///   （`BloodStainMesh` 的注释里记过同一个坑）；贴地片片的法线本来就只有"朝上"一种答案 ✓
        /// </summary>
        internal static Mesh FacingQuadMesh()
        {
            const int key = 5920;
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var v = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3( 0.5f, 0f,  0.5f), new Vector3( 0.5f, 0f, -0.5f),
            };
            var uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            };
            var n = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            // 正反各一遍（索引反过来即可 ⇒ 顶点/UV 只存一份）
            var tri = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };

            var m = new Mesh { name = "FacingQuad", hideFlags = HideFlags.DontSave };
            m.vertices = v;
            m.uv = uv;
            m.normals = n;
            m.triangles = tri;
            FacingQuadMeshBuilds++;
            _fxMeshCache[key] = m;
            return m;
        }

        // ── 两张贴图（各生成一次、之后只读）──
        private static Texture2D _facingRingTex;
        private static Texture2D _facingDotTex;

        /// <summary>
        /// **环贴图**：一圈**硬边像素**圆环（alpha 只有 0 / 255）。
        /// 内外半径都按 `FacingRingOuterM / FacingRingInnerRatio` 折算到像素，
        /// 两侧各 2px 描边 ⇒ 环的形状**只由这张图**决定 ✓
        /// </summary>
        internal static Texture2D FacingRingTex()
        {
            if (_facingRingTex != null) return _facingRingTex;
            const int S = FacingRingTexSize;
            float outer = S * 0.5f - 1f;                    // 47：留 1px 边，避免边界采样越界
            float inner = outer * FacingRingInnerRatio;     // 31.96
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                float dy = y + 0.5f - S * 0.5f;
                for (int x = 0; x < S; x++)
                {
                    float dx = x + 0.5f - S * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 c = new Color32(0, 0, 0, 0);                     // 环内 / 环外：全透明
                    if (d <= outer && d >= inner)
                    {
                        bool edge = d > outer - FacingOutlinePx || d < inner + FacingOutlinePx;
                        c = ToPixel(edge ? FacingPixelOutline : FacingPixelBody);
                    }
                    px[y * S + x] = c;
                }
            }
            _facingRingTex = MakeFacingTex("FacingRingTex", S, px);
            return _facingRingTex;
        }

        /// <summary>
        /// **方向点贴图**：一颗实心圆（硬边 + 2px 描边 + 左上一点高光），
        /// 颜色是图标那套的 ACC 绿 ⇒ 环是白的、点是绿的，"朝哪"一眼落到点上 ✓
        /// </summary>
        internal static Texture2D FacingDotTex()
        {
            if (_facingDotTex != null) return _facingDotTex;
            const int S = FacingDotTexSize;
            float radius = S * 0.5f - 1f;                   // 11
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                float dy = y + 0.5f - S * 0.5f;
                for (int x = 0; x < S; x++)
                {
                    float dx = x + 0.5f - S * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 c = new Color32(0, 0, 0, 0);
                    if (d <= radius)
                    {
                        if (d > radius - FacingOutlinePx) c = ToPixel(FacingPixelOutline);
                        else if (dx + dy < -3.5f) c = ToPixel(FacingPixelBody);   // 左上高光（图标式）
                        else c = ToPixel(FacingPixelAccent);
                    }
                    px[y * S + x] = c;
                }
            }
            _facingDotTex = MakeFacingTex("FacingDotTex", S, px);
            return _facingDotTex;
        }

        private static Color32 ToPixel(Color c)
            => new Color32((byte)Mathf.RoundToInt(c.r * 255f), (byte)Mathf.RoundToInt(c.g * 255f),
                           (byte)Mathf.RoundToInt(c.b * 255f), 255);

        /// <summary>
        /// 生成一张**硬边像素**贴图（贴地指示共用这一条出口）。
        /// `Point` 不插值（硬边是风格要求，不是省事）；`Clamp` 不重复采样；
        /// `HideFlags.DontSave`（不进资源库、不写盘、不参与打包，与 `BloodTex` 同一条）；
        /// `Apply(false, false)`：不生成 mip、**保持可读**（探针要 `GetPixels32` 量 alpha）✓
        /// </summary>
        private static Texture2D MakeFacingTex(string name, int size, Color32[] px)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            FacingTexBuilds++;
            return tex;
        }

        // ── 两份材质（静态缓存，全场共用；**永不逐实例改写**）──
        private static Material _facingRingMat;
        private static Material _facingDotMat;

        internal static Material FacingRingMaterial() => FacingMat(ref _facingRingMat, "M_FacingRing", FacingRingTex());
        internal static Material FacingDotMaterial()
            => FacingMat(ref _facingDotMat, "M_FacingDot", FacingDotTex(), 3001);

        /// <summary>
        /// 贴地指示的材质：`Sprites/Default` + **贴图**（颜色与形状全在贴图里，材质色保持白）。
        /// `queue` = 渲染队列（环 3000 / 方向点 3001，理由见方法体里的注释）✓
        ///
        /// ★ 为什么是 `Sprites/Default`（本工程第二次用内建着色器）★
        ///   它的片元就是 `tex2D(_MainTex, uv) * IN.color`：**贴图 × 顶点色**，
        ///   `Cull Off` / `ZWrite Off` / alpha 混合 ⇒ 正是一块"贴地的透明贴片"该有的全部性质 ✓
        ///   本工程已验证它在打包里不会被剥掉（`BloodStainMat` 的注释记着
        ///   `ProjectSettings/GraphicsSettings.asset → m_AlwaysIncludedShaders`，fileID 10753/10754）✓
        /// ★ 取不到怎么办 ★
        ///   退回 `Universal Render Pipeline/Unlit`，并**按"能不能设"去设** alpha 裁剪
        ///   （`_AlphaClip` / `_Cutoff` / `_BaseMap` 都先 `HasProperty` 再写，绝不臆造属性名）——
        ///   这样形状仍然来自贴图，只是从"混合"变成"裁剪"，硬边效果反而更接近像素风 ✓
        ///   探针会把**实际用到的着色器名**打出来 ⇒ "退回了没有"是可读的 ✓
        /// </summary>
        private static Material FacingMat(ref Material slot, string name, Texture2D tex, int queue = 3000)
        {
            if (slot != null) return slot;
            Shader sh = Shader.Find("Sprites/Default");
            bool sprite = sh != null;
            if (!sprite)
            {
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Standard");    // 与 `Build()` 的兜底同一条
            }
            var m = new Material(sh) { name = sprite ? name : name + "FALLBACK" };
            m.mainTexture = tex;                 // = `_MainTex`
            m.color = Color.white;               // 材质色保持白：颜色**只来自贴图**
            // ★ v204：`queue` 只对**方向点**用 3001 ★
            //   两片都是 `ZWrite Off` 的透明片 ⇒ 谁后画谁在上面，而透明队列按"离相机远近"排序：
            //   方向点在环带上、**方向点比环心更远时会被环带盖掉**（背对/斜后方位实测少一截）✗
            //   给它一个更大的队列号 ⇒ 珠子**永远压在环带上面**（"环上的一颗珠子"该有的层次）✓
            //   这不改深度关系：帽檐/身体照旧能挡住它（那是对的，见 `FacingDotCenterM` 的实测）✓
            m.renderQueue = queue;
            if (!sprite)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_AlphaClip"))
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.EnableKeyword("_ALPHATEST_ON");
                }
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            }
            slot = m;
            return m;
        }

        /// <summary>
        /// 建一整套脚下指示（**每个玩家一份物件，但网格 / 贴图 / 材质全场共享**）。
        /// 返回的父物件名字固定是 `"FacingRing"`（探针与 `PlayerBean` 都按这个名字找）✓
        ///
        /// 世界米 → 模型米在这里**一次换算完**（除 `CharScale × PlayerScale = 1.6302`）：
        /// 调用方（`PlayerBean`）只管把它挂到模型根节点上，不需要知道缩放链 ✓
        /// 方向点摆在**本地 +Z** ⇒ 它就是"模型正前方"（读的是模型最终角，见上方注释）✓
        /// </summary>
        internal static GameObject BuildFacingIndicator(Transform root)
        {
            const float toWorld = CharScale * PlayerScale;      // 1.6302：模型米 → 场上米

            var go = new GameObject("FacingRing");
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;   // ← 与模型根**同一个 yaw**

            // ① 整圈环：quad 边长 = 直径
            FacingQuadChild(go.transform, "FacingRingDisc",
                FacingRingOuterM * 2f / toWorld, FacingLiftM / toWorld, 0f, FacingRingMaterial());
            // ② 方向点：贴在环上、正前（+Z）。抬高多 2mm ⇒ 与环同深时**永远压在环上面**
            FacingQuadChild(go.transform, "FacingDot",
                FacingDotDiamM / toWorld, (FacingLiftM + 0.002f) / toWorld, FacingDotCenterM / toWorld,
                FacingDotMaterial());
            return go;
        }

        /// <summary>一块贴地贴片（单位 quad × 缩放）。位置/缩放都用**模型米** ⇒ 只在本文件里换算。</summary>
        private static GameObject FacingQuadChild(Transform parent, string name, float sizeM, float yM, float zM,
                                                  Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.identity;      // quad 顶点已躺在 XZ 平面
            go.transform.localPosition = new Vector3(0f, yM, zM);
            go.transform.localScale = new Vector3(sizeM, 1f, sizeM);   // y 不缩放（片是平的）
            go.AddComponent<MeshFilter>().sharedMesh = FacingQuadMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            // 贴着地的一片：不要投影（会在地上糊出第二片），也不要吃别人的阴影（会被压暗）
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// ★ v180：**地面边界圈** —— 替代原来的"填充圆盘"（`AddMarker` 的 Cylinder）★
        /// 职责没变：把**真实判定半径**画出来（"看得到 = 打得到"，v91 定的规矩）✓
        /// 变的只是画法：一圈细环而不是一片实心圆 ⇒ 边界清楚、又不遮住蘑菇云 ✓
        /// 材质**逐实例持有**（要淡出）⇒ 不进 `VfxMatCached`，结束时由 `TickBlasts` 销毁 ✓
        /// </summary>
        private void SpawnGroundRim(Vector2 centre, float radius, in FxSpec fx, float life)
        {
            const float d0 = 0.7f;
            Material mat = VfxMat(fx.Rim, fx.Core);
            GameObject go = MeshObj(FxRingMesh(5600, 36, 0.86f), mat, "GroundRim");
            go.transform.position = new Vector3(centre.x, 0.045f, centre.y);
            go.transform.rotation = Quaternion.identity;     // 环本来就躺在 XZ 平面
            go.transform.localScale = new Vector3(d0, 1f, d0);
            _blasts.Add(new BlastFx
            {
                Go = go, Mat = mat, Core = fx.Rim, Rim = fx.Core,
                T = 0f, Total = FxLife(Mathf.Max(0.10f, life)), S0 = d0, S1 = radius * 2f, Thick = 0.05f
            });
        }
        // ══════════════════════════════════════════════════════════════════════
        //  ★ v188：**持续效果的场上表现** —— 毒区地面 / 灼烧火星 / 减速箭头 ★
        //
        //  用户三条原话（逐条对应到下面三个方法）：
        //    ① "增加爆炸毒素插件的特效，在地面留下**实际可见**的效果"
        //       ⇒ `SyncPoisonZones`：贴地腐蚀圆盘 + 上升气泡 + 边界微光，
        //         **存在多久就显示多久，到期当帧撤掉**（不留残影）✓
        //    ② "灼烧敌人身上冒小火花"
        //       ⇒ `SyncBurnSparks`：跟着敌人、持续冒上升的小火星，**层数越多越密**✓
        //    ③ "减速的敌人身上出现很多小的、向下指的蓝色箭头"
        //       ⇒ `SyncSlowArrows`：尖端朝下的小箭头绕身下落，**层数越多越多（5 层封顶 15 支）**✓
        //
        //  ★ 数据来源只有一个：`PluginDotTable`（逻辑层的持续效果表）★
        //    表现层**不自己记状态**：每一帧都从表里的行重算"现在该显示什么"。
        //    这样"表里没了但画面上还在"（或反过来）在结构上不可能发生 ✓
        //    —— 用户要求的"到期当帧消失、不要残留"就是这条的直接结果 ✓
        //
        //  ★ 零分配 ★
        //    网格走 `_fxMeshCache`（键 5730/5731/5740 与既有 5100+/5400/5600-5620/5700/5800 不撞）
        //    材质走 `VfxMatCached` 的静态缓存；粒子走既有的 `_fxMeshes`（`MeshBit`）
        //    ⇒ 每帧只有"读表 + 写 transform"，**没有一次 `new Mesh` / `new Material`** ✓
        //    唯一会新建对象的地方是"层数变大"与"毒区新出现/气泡发射"——
        //    那是**事件频率**，不是帧频率 ✓
        // ══════════════════════════════════════════════════════════════════════

        // ── 毒区配色（绿 = 腐蚀；与中毒弹字的绿同族，见 `PopupStyle.PoisonGreen`）──
        //  ★ v188 调过一次（有图作证）：第一版用"亮绿 + alpha 0.30"，拍出来**和草地一个颜色**，
        //    一眼看过去只是"草亮了一块"，不是"被腐蚀了" ✗
        //    现在的判据是**明度要低于草地**（暗橄榄绿渍）+ 一圈**酸性亮绿**环当边界 ⇒ "渍 + 光"两层才读得出 ✓
        /// <summary>腐蚀圆盘的本体色（**比草地暗** ⇒ 压在地上读作"这块的草烂了"）</summary>
        private static readonly Color PoisonCore = new Color(0.16f, 0.34f, 0.05f, 0.66f);
        /// <summary>内层：酸性亮绿的**7 边形**（不是盘也不是环）—— 低透明度，会转 ⇒ 读作"毒在翻涌"</summary>
        private static readonly Color PoisonInner = new Color(0.58f, 1.00f, 0.16f, 0.34f);
        /// <summary>边界微光（最外一圈细环，最亮）</summary>
        private static readonly Color PoisonRim = new Color(0.70f, 1.00f, 0.24f, 0.96f);
        /// <summary>气泡本体</summary>
        private static readonly Color PoisonBubble = new Color(0.62f, 1.00f, 0.28f, 0.90f);

        // ── 灼烧火星配色（炭火橙红，与 `PopupStyle.BurnOrange` / 插件灼烧的橙同族）──
        /// <summary>火星本体</summary>
        private static readonly Color BurnSparkCore = new Color(1.00f, 0.58f, 0.14f, 0.95f);
        /// <summary>火星边缘（更红）</summary>
        private static readonly Color BurnSparkRim = new Color(1.00f, 0.24f, 0.06f, 1.00f);

        /// <summary>灼烧火星的总量上限 —— 防"三只满层敌人 + 慢放"这种情况把物件数顶上去。</summary>
        private const int BurnSparkBudget = 90;
        /// <summary>减速箭头的单只敌人上限（5 层 = 15 支）。</summary>
        public const int SlowArrowMax = 15;

        /// <summary>一块毒区的三个贴地件（圆盘 / 内层 / 边环）+ 它的气泡节拍。</summary>
        private struct ZoneFx
        {
            public GameObject Disc;
            public GameObject Inner;
            public GameObject Rim;
            public Vector2 Center;
            public float Radius;
            public float T;
            public float BubbleT;
            /// <summary>本帧是否仍被表里的一行认领（false ⇒ 当帧撤掉）</summary>
            public bool Seen;
        }

        private readonly List<ZoneFx> _zoneFx = new List<ZoneFx>();

        /// <summary>一支减速箭头。只有 `Slot` 是状态 —— 位置/相位全由它算出来（见 `SyncSlowArrows`）。</summary>
        private struct SlowArrowFx
        {
            public GameObject Go;
            public int EnemyId;
            public int Slot;
        }

        private readonly List<SlowArrowFx> _slowArrows = new List<SlowArrowFx>();

        /// <summary>逐敌人的灼烧火星发射累计（秒）。</summary>
        private readonly Dictionary<int, float> _burnAcc = new Dictionary<int, float>();
        /// <summary>本帧每只敌人"该有几支减速箭头"。</summary>
        private readonly Dictionary<int, int> _slowNeed = new Dictionary<int, int>();
        /// <summary>清 `_burnAcc` 时用的临时表（避免边遍历边删）。</summary>
        private readonly List<int> _dotScratch = new List<int>();
        /// <summary>灼烧火星的表现时间（与 `_fxMeshes` 共用同一套推进）。</summary>
        private float _dotTime;

        /// <summary>v188：一帧的持续效果表现总入口。放在 `SyncEnemies` 之后（要先有敌人的位置）。</summary>
        private void SyncDotFx(RunSession s, float dt)
        {
            if (s == null || s.World == null || _root == null) return;
            _dotTime += Mathf.Max(0f, dt);
            PluginDotTable table = s.Dots;
            SyncPoisonZones(table, dt);
            SyncBurnSparks(s.World, table, dt);
            SyncSlowArrows(s.World, table);
        }

        // ─────────────────────────── ① 毒区地面 ───────────────────────────

        /// <summary>
        /// v188：把 `PluginDotTable` 里的**毒区行**画成地上的东西。
        ///
        /// ★ "存在多久就显示多久"是怎么保证的 ★
        ///   本方法**不持有寿命**：每帧先全部标记 `Seen = false`，再拿表里的行去认领；
        ///   一帧下来没被认领的（到期 / 被清场 / 被换波）⇒ **当帧 `SetActive(false)` + 销毁** ✓
        ///   没有 TTL、没有淡出尾巴 ⇒ 不会出现"毒没了地上还留一块绿"✗
        ///
        /// ★ 认领的判据与 `PluginDotTable.AddPoisonZone` **同一个**（0.15m 内算同一块）★
        ///   否则"同一个位置再炸一次"会让表里合并、而画面上多出一块 ✗（两处口径必须一样）
        /// </summary>
        private void SyncPoisonZones(PluginDotTable table, float dt)
        {
            for (int i = 0; i < _zoneFx.Count; i++)
            {
                ZoneFx z = _zoneFx[i];
                z.Seen = false;
                _zoneFx[i] = z;
            }

            if (table != null)
            {
                IReadOnlyList<DotRow> rows = table.Rows;
                for (int r = 0; r < rows.Count; r++)
                {
                    DotRow row = rows[r];
                    if (row.Kind != DotKind.PoisonZone) continue;
                    int found = -1;
                    for (int i = 0; i < _zoneFx.Count; i++)
                    {
                        if (_zoneFx[i].Seen) continue;
                        if ((_zoneFx[i].Center - row.Center).sqrMagnitude > 0.0225f) continue;
                        found = i;
                        break;
                    }
                    if (found >= 0)
                    {
                        ZoneFx z = _zoneFx[found];
                        z.Seen = true;
                        z.Radius = Mathf.Max(0.20f, row.Radius);
                        _zoneFx[found] = z;
                    }
                    else
                    {
                        _zoneFx.Add(CreateZone(row));
                    }
                }
            }

            for (int i = _zoneFx.Count - 1; i >= 0; i--)
            {
                ZoneFx z = _zoneFx[i];
                if (!z.Seen)
                {
                    // ★ 到期 ⇒ **当帧消失**：先关掉（本帧就不再画），再销毁 ✓
                    DestroyZoneFx(z);
                    _zoneFx.RemoveAt(i);
                    continue;
                }

                z.T += dt;
                float d = z.Radius * 2f;
                // 呼吸：±3%（"边界微光"要动起来才不像一块贴纸）
                float breath = 0.97f + 0.03f * Mathf.Sin(z.T * 2.3f + z.Center.x);
                if (z.Disc != null) z.Disc.transform.localScale = new Vector3(d, 1f, d);
                if (z.Inner != null)
                {
                    // ★ v188 第二版（有图作证）：内层原来也是**亮环**，和边界环一起被读成"靶心" ✗
                    //   现在内层是**低透明度、7 边形的酸绿块**，缓慢自转 ⇒ 7 条边在转，盘面就"翻涌"起来了，
                    //   而"边界"只剩下最外那一圈 ⇒ 两个语义不再打架 ✓
                    float di = d * (0.74f + 0.06f * Mathf.Sin(z.T * 1.9f + z.Center.y));
                    z.Inner.transform.localScale = new Vector3(di, 1f, di);
                    z.Inner.transform.rotation = Quaternion.Euler(0f, -z.T * 26f, 0f);
                }
                if (z.Rim != null) z.Rim.transform.localScale = new Vector3(d * breath, 1f, d * breath);

                // 气泡：每 0.13 秒一颗，从盘面上升（`Gravity = 0` ⇒ 只上升、不下落）
                z.BubbleT += dt;
                int guard = 0;
                while (z.BubbleT >= 0.13f && guard++ < 8)
                {
                    z.BubbleT -= 0.13f;
                    SpawnPoisonBubble(z);
                }
                _zoneFx[i] = z;
            }
        }

        /// <summary>v188：建一块毒区的三个贴地件（建好即出现在当前帧）。</summary>
        private ZoneFx CreateZone(in DotRow row)
        {
            float r = Mathf.Max(0.20f, row.Radius);
            var at = new Vector3(row.Center.x, 0.030f, row.Center.y);
            // ① 腐蚀圆盘：**9 边形**（不是正圆）—— 圆弧读作"判定框"，有棱角才读作"这块草地被烂掉了"✓
            GameObject disc = MeshObj(FxDiscMesh(5730, 9),
                VfxMatCached(PoisonCore, PoisonRim, VfxCoreBoostFlat), "PoisonDisc");
            disc.transform.position = at;
            disc.transform.rotation = Quaternion.identity;
            disc.transform.localScale = new Vector3(r * 2f, 1f, r * 2f);

            // ② 内层：**低透明度的 7 边形酸绿块**，缓慢自转 ⇒ 棱边在转 = 盘面在翻涌
            //    （v188 第二版：原来这里是个亮环，和③一起被读成"靶心"，见 `SyncPoisonZones` 的说明）
            GameObject inner = MeshObj(FxDiscMesh(5732, 7),
                VfxMatCached(PoisonInner, PoisonRim, VfxCoreBoostFlat), "PoisonInner");
            inner.transform.position = at + new Vector3(0f, 0.004f, 0f);
            inner.transform.rotation = Quaternion.identity;
            inner.transform.localScale = new Vector3(r * 2f, 1f, r * 2f);

            // ③ 边界微光：一圈细环（与肥料爆炸的 `SpawnGroundRim` 同一套网格语言）
            GameObject rim = MeshObj(FxRingMesh(5740, 32, 0.90f),
                VfxMatCached(PoisonRim, PoisonCore, VfxCoreBoostFlat), "PoisonRim");
            rim.transform.position = at + new Vector3(0f, 0.006f, 0f);
            rim.transform.rotation = Quaternion.identity;
            rim.transform.localScale = new Vector3(r * 2f, 1f, r * 2f);

            return new ZoneFx
            {
                Disc = disc, Inner = inner, Rim = rim,
                Center = row.Center, Radius = r, T = 0f, BubbleT = 0.08f, Seen = true
            };
        }

        /// <summary>v188：撤掉一块毒区。**先把引用断掉再销毁**，避免同一帧里的第二次读撞 MissingReference。</summary>
        private static void DestroyZoneFx(in ZoneFx z)
        {
            if (z.Disc != null) { z.Disc.SetActive(false); Object.Destroy(z.Disc); }
            if (z.Inner != null) { z.Inner.SetActive(false); Object.Destroy(z.Inner); }
            if (z.Rim != null) { z.Rim.SetActive(false); Object.Destroy(z.Rim); }
        }

        /// <summary>v188：一颗上升的毒气泡（低面数球壳，与其它特效同一套颗粒语言）。</summary>
        private void SpawnPoisonBubble(in ZoneFx z)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float rr = z.Radius * Mathf.Sqrt(Random.Range(0.04f, 0.86f));   // 面积均匀
            var from = new Vector3(z.Center.x + Mathf.Cos(a) * rr, 0.06f, z.Center.y + Mathf.Sin(a) * rr);
            // v188：0.10~0.19 在俯视机位下只有几个像素（第一版拍不到）⇒ 放大到 0.15~0.27 ✓
            float size = Random.Range(0.15f, 0.27f);
            GameObject go = MeshObj(FxIcoShellMesh(0),
                VfxMatCached(PoisonBubble, PoisonRim, VfxCoreBoostFlat), "PoisonBubble");
            go.transform.position = from;
            go.transform.localScale = Vector3.one * size;
            _fxMeshes.Add(new MeshBit
            {
                Go = go,
                Vel = new Vector3(0f, Random.Range(0.55f, 1.05f), 0f),
                Spin = new Vector3(0f, Random.Range(-90f, 90f), 0f),
                Gravity = 0f,                                        // 只上升，不下落
                T = -Random.Range(0f, 0.06f),
                Total = FxLife(Random.Range(0.42f, 0.62f)),
                S0 = size,
                S1 = size * 0.35f                                    // 缩小 = 消掉（不碰 alpha）✓
            });
        }

        // ─────────────────────────── ② 灼烧火星 ───────────────────────────

        /// <summary>
        /// v188：**被灼烧的敌人身上冒小火花**。
        ///
        /// ★ "跟着敌人移动"是怎么成立的 ★
        ///   位置每帧从 `World.Enemies` 的**当前** `Pos` 取 ⇒ 敌人走，火星的发射口跟着走 ✓
        ///   已经飞出去的那些留在原地（那是"飘走的火星"，本该落后于身体）✓
        ///
        /// ★ "层数越多越密"★
        ///   发射率 = `14 × 层数`（1/2/3 层 ⇒ 14/28/42 颗/秒，见 `BurnSparkRate`）——
        ///   这个函数是**唯一的一处**定义，探针打的读数也读它，
        ///   所以"报告里写的密度"与"画面上真的有多密"不可能分家 ✓
        ///   （上限 `BurnSparkBudget` 兜底：慢放或极多敌人时不会无限堆物件）
        ///
        /// ★ 闪烁不靠 alpha ★
        ///   每颗只活 0.28~0.40 秒 ⇒ 一个亮点出现、缩小、消失，下一个在别处出现 ⇒ 读作**在闪** ✓
        ///   （共享材质不能逐实例淡出，见 `VfxMatCached` 的契约）
        ///
        /// ★ v188 第二版（有图作证）：第一版"看不见"✗ ★
        ///   0.045~0.085m 的方块在 8~10m 机位下只有 2~5 像素，全部淹没在草纹里。
        ///   现在 0.14~0.26m（≈ 10~18 像素）+ 寿命拉长 ⇒ 同时在场 6/12/21 颗，
        ///   疏/中/密终于在三张图上分得开 ✓
        /// </summary>
        public static float BurnSparkRate(int stacks)
            => 14f * Mathf.Clamp(stacks, 0, PluginDotTable.BurnMaxStacks);

        private void SyncBurnSparks(World w, PluginDotTable table, float dt)
        {
            int alive = 0;
            for (int i = 0; i < _fxMeshes.Count; i++)
                if (_fxMeshes[i].Go != null && _fxMeshes[i].Go.name == "BurnSpark") alive++;

            Material core = VfxMatCached(BurnSparkCore, BurnSparkRim);
            Material hot = VfxMatCached(BurnSparkRim, BurnSparkCore);

            _dotScratch.Clear();
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.IsDead) continue;
                int stacks = table != null ? table.BurnStacks(e.Id) : 0;
                if (stacks <= 0)
                {
                    if (_burnAcc.ContainsKey(e.Id)) _dotScratch.Add(e.Id);
                    continue;
                }
                float rate = BurnSparkRate(stacks);
                float acc = _burnAcc.TryGetValue(e.Id, out float v) ? v : 0f;
                acc += dt;
                float step = 1f / rate;
                int guard = 0;
                while (acc >= step && guard++ < 8)
                {
                    acc -= step;
                    if (alive >= BurnSparkBudget) continue;
                    alive++;
                    float ang = Random.Range(0f, Mathf.PI * 2f);
                    float rad = e.Radius * Random.Range(0.25f, 0.95f);
                    // ★ v188 第二版（有图作证）：第一版 0.045~0.085 的方块在 8~10m 机位下只有 3~5 像素，
                    //   拍出来"看不见"✗ 而且高度只在身体内 ⇒ 被身体轮廓吃掉。
                    //   现在：更大 + 允许冒到头顶之上（×2.2 倍身高），这才读得出"身上在冒火"✓
                    var from = new Vector3(
                        e.Pos.x + Mathf.Cos(ang) * rad,
                        Random.Range(0.30f, 1.55f) * Mathf.Max(0.6f, e.Radius * 2.2f),
                        e.Pos.y + Mathf.Sin(ang) * rad);
                    // ★ v188 第三版：真实玩法机位是**广角**（约 42 像素/米）。
                    //   0.14~0.26m 的方块在那个机位下只有 6~11 像素 ⇒ 图上还是"点在哪儿"✗
                    //   0.26~0.46m ≈ 11~19 像素，与敌人（1.5m ≈ 63 像素）比例仍然读作"小火星" ✓
                    float size = Random.Range(0.26f, 0.46f);
                    GameObject go = Prim(PrimitiveType.Cube, "BurnSpark", from,
                        Vector3.one * size, ((alive & 1) == 0) ? core : hot);
                    go.transform.rotation = Quaternion.Euler(
                        Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                    _fxMeshes.Add(new MeshBit
                    {
                        Go = go,
                        Vel = new Vector3(Random.Range(-0.25f, 0.25f),
                                          Random.Range(0.90f, 1.90f),
                                          Random.Range(-0.25f, 0.25f)),
                        Spin = new Vector3(Random.Range(-260f, 260f),
                                           Random.Range(-260f, 260f),
                                           Random.Range(-260f, 260f)),
                        Gravity = 0f,                                 // 火星是"往上飘"的，不是落体
                        T = 0f,
                        Total = FxLife(Random.Range(0.28f, 0.40f)),
                        S0 = size,
                        S1 = size * 0.25f                             // 缩到 1/4（不是 1/10，否则一瞬间就没了）
                    });
                }
                _burnAcc[e.Id] = acc;
            }
            for (int i = 0; i < _dotScratch.Count; i++) _burnAcc.Remove(_dotScratch[i]);
        }

        // ─────────────────────────── ③ 减速箭头 ───────────────────────────

        /// <summary>v188：`n` 层减速 ⇒ 该画几支箭头（1..5 层 → 3/6/9/12/15）。</summary>
        public static int SlowArrowCount(int stacks)
        {
            if (stacks <= 0) return 0;
            return Mathf.Min(SlowArrowMax, stacks * 3);
        }

        /// <summary>
        /// v188：**减速的敌人身上很多小的、向下指的蓝色箭头**。
        ///
        /// ★ 为什么姿态是"绕身 + 下落 + 明暗循环"★
        ///   静止的一圈箭头会被读成"装饰贴纸"；只有**在动**才是"这个单位被压住了"✓
        ///   下落：`y` 从 1.6m 线性落到 0.1m 然后回卷（`Mathf.Repeat`）；
        ///   明暗循环：**缩放**呼吸（共享材质不能改 alpha，见 `VfxMatCached` 的契约）✓
        /// ★ 几支 ★ 每层 3 支、5 层封顶 15 支（`SlowArrowCount`）——
        ///   用户要的"层数越多越多"与"给读数"都落在同一个函数上 ✓
        /// ★ 位置/相位全部由 `Slot` 推出来 ⇒ 每支箭头**没有跨帧状态** ★
        ///   所以"层数变小"时多余的箭头只是 `SetActive(false)`，再叠回来立刻原位复现 ✓
        /// </summary>
        private void SyncSlowArrows(World w, PluginDotTable table)
        {
            // ── ① 本帧每只敌人需要几支 ──
            _slowNeed.Clear();
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.IsDead) continue;
                int stacks = table != null ? table.SlowStacks(e.Id) : 0;
                int need = SlowArrowCount(stacks);
                if (need > 0) _slowNeed[e.Id] = need;
            }

            // ── ② 已有箭头：需要的显示 + 摆位；不需要的关掉（不销毁，池化）──
            Material mat = VfxMatCached(PopupStyle.ColorOf(PopupKind.DamageSlow),
                                        new Color(0.05f, 0.18f, 0.55f, 1f), VfxCoreBoostFlat);
            float t = _dotTime;
            for (int i = 0; i < _slowArrows.Count; i++)
            {
                SlowArrowFx a = _slowArrows[i];
                if (a.Go == null) continue;
                int need = _slowNeed.TryGetValue(a.EnemyId, out int n) ? n : 0;
                EnemyActor e = need > 0 ? FindEnemy(w, a.EnemyId) : null;
                if (e == null || a.Slot >= need)
                {
                    if (a.Go.activeSelf) a.Go.SetActive(false);
                    continue;
                }
                if (!a.Go.activeSelf) a.Go.SetActive(true);
                // 箭头只有旋转/位置/缩放三种赋值 —— 零分配，也不碰材质
                float ang = (a.Slot / (float)need) * Mathf.PI * 2f + a.Slot * 0.61f + t * 0.25f;
                float rad = e.Radius * 1.55f + 0.10f * (a.Slot % 3);
                float frac = Mathf.Repeat(t * 1.35f + a.Slot * 0.37f, 1f);   // 0 = 头顶，1 = 落地
                float y = Mathf.Lerp(1.55f, 0.10f, frac);
                // v188 第三版：0.62 时箭头只有 ~0.21m（广角机位下 9 像素）⇒ 读成"小点"
                // 0.95 ⇒ ~0.32m（13 像素），仍是"小的箭头"，但能数得出几支 ✓
                float s = 0.95f * (0.82f + 0.18f * Mathf.Sin(t * 5.2f + a.Slot * 1.7f));
                a.Go.transform.position = new Vector3(
                    e.Pos.x + Mathf.Cos(ang) * rad, y, e.Pos.y + Mathf.Sin(ang) * rad);
                a.Go.transform.localScale = new Vector3(s, s, s);
            }

            // ── ③ 还缺的箭头：建出来（只在"层数变大"时发生，不是每帧）──
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.IsDead) continue;
                int need = _slowNeed.TryGetValue(e.Id, out int n) ? n : 0;
                if (need <= 0) continue;
                int have = 0;
                for (int k = 0; k < _slowArrows.Count; k++)
                    if (_slowArrows[k].EnemyId == e.Id) have++;
                for (int slot = have; slot < need; slot++)
                {
                    GameObject go = MeshObj(StatusArrowMesh(), mat, "SlowArrow");
                    _slowArrows.Add(new SlowArrowFx { Go = go, EnemyId = e.Id, Slot = slot });
                }
            }
        }

        /// <summary>v188：按 id 找敌人（`SyncSlowArrows` 用，表里只有 id）。</summary>
        private static EnemyActor FindEnemy(World w, int id)
        {
            for (int i = 0; i < w.Enemies.Count; i++)
                if (w.Enemies[i] != null && !w.Enemies[i].IsDead && w.Enemies[i].Id == id) return w.Enemies[i];
            return null;
        }

        /// <summary>
        /// ★ v188：**持续效果的表现读数**（只读，给探针与验收用）★
        ///
        /// 为什么要一个专门的读数：用户三条要求里两条带"层数越多越……"，
        /// 而层数是**逻辑量**（`PluginDotTable`）、密度是**画面量**（这里）。
        /// 只报层数 = 没验证"画面上真的变密了"✗ 只报画面 = 不知道是几层 ✗
        /// ⇒ 两份一起打印，才有"3 层 → 20 颗/秒、在飞 5 颗"这种可核对的句子 ✓
        /// </summary>
        public string DotFxReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("DOTFX");
            // ── 毒区：三件套 + 气泡 ──
            sb.Append("｜毒区块=").Append(_zoneFx.Count);
            if (_zoneFx.Count > 0)
            {
                ZoneFx z0 = _zoneFx[0];
                sb.Append("(r=").Append(z0.Radius.ToString("0.00"))
                  .Append(" 件=").Append((z0.Disc != null ? 1 : 0) + (z0.Inner != null ? 1 : 0) + (z0.Rim != null ? 1 : 0))
                  .Append(" 活=").Append(z0.Disc != null && z0.Disc.activeSelf ? "是" : "否").Append(')');
            }
            int bubbles = 0, sparks = 0, other = 0;
            int stem = 0, cap = 0, curl = 0, domeOpen = 0, domeFade = 0;
            float cloudMaxLeft = 0f;
            for (int i = 0; i < _fxMeshes.Count; i++)
            {
                MeshBit b = _fxMeshes[i];
                if (b.Go == null) continue;
                float left = b.Total - b.T;
                switch (b.Go.name)
                {
                    case "PoisonBubble": bubbles++; break;
                    case "BurnSpark": sparks++; break;
                    case "BlastStem": stem++; if (left > cloudMaxLeft) cloudMaxLeft = left; break;
                    case "BlastCap": cap++; if (left > cloudMaxLeft) cloudMaxLeft = left; break;
                    case "BlastCurl": curl++; if (left > cloudMaxLeft) cloudMaxLeft = left; break;
                    case "BlastDomeOpen": domeOpen++; if (left > cloudMaxLeft) cloudMaxLeft = left; break;
                    case "BlastDomeFade": domeFade++; if (left > cloudMaxLeft) cloudMaxLeft = left; break;
                    default: other++; break;
                }
            }
            sb.Append("｜气泡=").Append(bubbles);
            sb.Append("｜灼烧火星=").Append(sparks);
            // ── 减速箭头：需要几支 / 池子里共几支 / 现在亮着几支 ──
            int slowOn = 0;
            for (int i = 0; i < _slowArrows.Count; i++)
                if (_slowArrows[i].Go != null && _slowArrows[i].Go.activeSelf) slowOn++;
            sb.Append("｜减速箭头 共=").Append(_slowArrows.Count).Append(" 亮=").Append(slowOn)
              .Append(" 需要=");
            // 逐敌人需求（敌人少，直接打出来）
            var needSb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, int> kv in _slowNeed)
            {
                if (needSb.Length > 0) needSb.Append(' ');
                needSb.Append('#').Append(kv.Key).Append('→').Append(kv.Value).Append("支");
            }
            sb.Append(needSb.Length == 0 ? "0" : needSb.ToString());
            // ── 蘑菇云：五组构件 + 最长剩余寿命 ──
            sb.Append("｜蘑菇云 茎=").Append(stem).Append(" 帽=").Append(cap)
              .Append(" 卷=").Append(curl).Append(" 壳开=").Append(domeOpen)
              .Append(" 壳散=").Append(domeFade)
              .Append(" 最长剩=").Append(cloudMaxLeft.ToString("0.00")).Append("s");
            sb.Append("｜在场特效件合计=").Append(_fxMeshes.Count + other);
            // ★ v188 诊断：火星"报有 5 颗但图上没有"时，缺的就是这行 ★
            //   （位置 / 缩放 / 渲染器开关 / 材质是否为空 —— 四样里任何一样不对都会"看不见"）
            int shown = 0;
            for (int i = 0; i < _fxMeshes.Count && shown < 3; i++)
            {
                MeshBit b = _fxMeshes[i];
                if (b.Go == null || b.Go.name != "BurnSpark") continue;
                MeshRenderer mr = b.Go.GetComponent<MeshRenderer>();
                Vector3 p = b.Go.transform.position;
                sb.Append("\n  火星[").Append(shown).Append("] pos=(")
                  .Append(p.x.ToString("0.00")).Append(',').Append(p.y.ToString("0.00")).Append(',')
                  .Append(p.z.ToString("0.00")).Append(") s=").Append(b.Go.transform.localScale.x.ToString("0.000"))
                  .Append(" 活=").Append(b.Go.activeInHierarchy ? "是" : "否")
                  .Append(" 渲染=").Append(mr != null && mr.enabled ? "开" : "关")
                  .Append(" 材质=").Append(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "空")
                  .Append(" 余=").Append((b.Total - b.T).ToString("0.00")).Append('s');
                shown++;
            }
            return sb.ToString();
        }

        /// <summary>v188：换局/清场时把这一套表现件全部收干净（含毒区与箭头）。</summary>
        private void ClearDotFx()
        {
            for (int i = 0; i < _zoneFx.Count; i++) DestroyZoneFx(_zoneFx[i]);
            _zoneFx.Clear();
            for (int i = 0; i < _slowArrows.Count; i++)
            {
                SlowArrowFx a = _slowArrows[i];
                if (a.Go != null) Object.Destroy(a.Go);
            }
            _slowArrows.Clear();
            _burnAcc.Clear();
            _slowNeed.Clear();
            _dotScratch.Clear();
            _dotTime = 0f;
        }

        /// <summary>v179：建一个**自带网格**的物件。`Prim` 只吃 Unity 内置图元，这里要自定义 mesh。</summary>
        private GameObject MeshObj(Mesh mesh, Material mat, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// v179：一颗**实体颗粒**（碎片 / 棱台段 / 球壳 / 湿痕）。
        ///
        /// ★ 与 `FxBit`（火花）的区别：这条要有自己的 mesh 与朝向（会自转），
        ///   且要能"错开起跳"（`T` 从负数开始）和"落体"（`Gravity`）★
        /// ★ 与 `BlastFx` 的区别：**它不持有材质** ★
        ///   全部共用 `VfxMatCached` 的那份缓存 ⇒ `TickFxMeshes` 里**绝不** `Destroy(Mat)` ✓
        ///   （`BlastFx` 必须逐实例持有材质是因为它会 `SetVfxColor` 淡出；
        ///     本结构只做**缩放**，所以可以整批共用一份材质 —— 这正是"不再泄漏"的原因）✓
        /// </summary>
        private struct MeshBit
        {
            public GameObject Go;
            public Vector3 Vel;
            public Vector3 Spin;      // 度/秒，逐轴自转
            public float Gravity;     // >0 = 抛物下落；0 = 悬停
            public float T;
            public float Total;
            public float S0, S1;
            /// <summary>
            /// ★ v185：**非均匀**缩放轴（0 = 用不到，走原来的各向同性那条路）★
            ///
            /// 为什么需要它：弯月剑气的**宽深比**不是 1（框半宽 2.91m vs 前向半长 1.9m），
            /// 而 `localScale = Vector3.one * s` 只能等比放大 ⇒ 一个圆形网格无论怎么放
            /// 都不可能既贴住框宽又贴住框深 ✗
            /// 而把比例**烘进网格**又不行：比例是 `2·sin(arc/2)`，`arc` 改一下网格就错了 ✓
            /// 所以把轴放进结构里：`(0,0,0)` = 各向同性（**所有既有调用点都不用改**），
            /// 非零时与 `Lerp(S0,S1,k)` 相乘 ⇒ 体积/淡出仍然只靠缩放、不碰 alpha ✓
            /// </summary>
            public Vector3 SclAxis;
        }

        private readonly List<MeshBit> _fxMeshes = new List<MeshBit>();

        private void TickFxMeshes(float dt)
        {
            for (int i = _fxMeshes.Count - 1; i >= 0; i--)
            {
                MeshBit b = _fxMeshes[i];
                if (b.Go == null) { _fxMeshes.RemoveAt(i); continue; }
                b.T += dt;
                if (b.T < 0f) { _fxMeshes[i] = b; continue; }     // 错开起跳：还没轮到它
                float k = Mathf.Clamp01(b.T / Mathf.Max(0.01f, b.Total));
                if (k >= 1f)
                {
                    Object.Destroy(b.Go);                          // 只销毁物件，**材质是共享的、不能销毁**
                    _fxMeshes.RemoveAt(i);
                    continue;
                }
                Transform tr = b.Go.transform;
                tr.position += b.Vel * dt;
                if (b.Gravity != 0f) b.Vel += new Vector3(0f, -b.Gravity * dt, 0f);
                if (b.Spin.sqrMagnitude > 0f) tr.Rotate(b.Spin * dt, Space.Self);
                float s = Mathf.Max(0.005f, Mathf.Lerp(b.S0, b.S1, k));
                // ★ v185：`SclAxis` 全零 ⇒ 完全走原来的各向同性路径（既有调用点行为不变）★
                tr.localScale = b.SclAxis.sqrMagnitude > 0f
                    ? new Vector3(s * b.SclAxis.x, s * b.SclAxis.y, s * b.SclAxis.z)
                    : Vector3.one * s;
                _fxMeshes[i] = b;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v191·fx3 起、**v196·fx4 重做两块**：本节三套表现 ★
        //
        //   ① 整点回血特效      ⇒ 本节第一部分（**v196 重做**：世界空间四段）
        //   ② 移动轨迹灰尘      ⇒ 本节第二部分（v191，未改动）
        //   ③ 敌人死亡痕迹      ⇒ 本节第三部分（**v196 重做**：血液溅射 + 血渍纹理）
        //
        //  ── v196 为什么两块都要重做（用户原话）──
        //   · "重新制敌人死亡痕迹，需要类似血液溅射效果，有颜色纹理（深浅分明）"
        //     ⇒ ③ 从"绿色纯色圆盘"改成"程序化血迹贴图 × 顶点色"（下面第三部分有完整推导）
        //   · "重新设计每分钟回血特效，不要全屏绿色，改为场景中落下一个绿色十字，然后炸开，
        //      绿色光圈扩散到全场，接着玩家回血"
        //     ⇒ ① 整块删掉重写：**没有一个面片贴在相机前**，四段全部发生在世界空间
        //
        // ── 三条纪律（沿用本文件既有约定，**不新开一套**）──
        //   · 网格：**运行期零 `new Mesh`**
        //       溅射片复用 `FxShardMesh(variant)`（键 5100+，攻击碎片本来就在用）；
        //       血渍用键 **6000..6039**（40 片各自的 UV 角度，建池时一次建好，
        //       起点见 `StainMeshKeyBase` —— 原来的 5920 与 `FacingQuadMesh` 撞键）；
        //       回血的十字与细环用 **5902 / 5928**
        //       （与既有 5100+/5200/5250/5400/5500/5600-5620/5700/5730/5740/5800/5910 都不撞）✓
        //       ★ 旧键 5900 / 5901 已随"全屏绿"一起删除（见本节末尾那段说明）★
        //   · 材质：**运行期零 `new Material`**
        //       灰尘 → `MatCached`；尸块 → `VfxMatCached`（**240 块共用 1 份**；
        //       每块的颜色与淡出走**逐渲染器 `MaterialPropertyBlock`** —— MPB 是按渲染器覆盖属性，
        //       既不复制材质也不改共享材质，所以"颜色跟敌人走 + 淡出"和"零 new Material"可以同时成立 ✓）✓
        //       血渍 → **共用 1 份** `Sprites/Default + 血迹贴图`（建池时建 1 份；"每片不同"与"淡出"
        //       全走**顶点色**，不碰这份材质）✓
        //       回血四段 → 4 份各自独占的材质（每段都要自己的 alpha 斜坡，共享会互相改读数）✓
        //   · 对象：**全部池化**，`Build` 时建好、之后只 `SetActive` / 改 transform
        //       灰尘 **24** 个｜尸块 **240** 个（用户点名的上限）｜血渍 **40** 个｜回血 **4** 个
        //       （② 炸开时那 9 块碎片走既有的 `_fxMeshes` 通道，一分钟一次、用完自毁）
        // ══════════════════════════════════════════════════════════════════════

        // ──────────────────────────────────────────────────────────────────────
        //  ① **整点回血特效：世界空间四段**（v196 重做）
        //
        //  用户原话："重新设计每分钟回血特效，**不要全屏绿色**，改为**场景中落下一个绿色十字**，
        //  然后**炸开**，绿色**光圈扩散到全场**，接着**玩家回血**"
        //  ⇒ 逐句落到**四段顺序**，时长比例写上面 `HealFxTotal` 的注释里 ✓
        //
        //  ── "不要全屏绿" = 不要**贴在相机前**的东西 ──
        //  旧版是三件套钉在 `cam.position + forward × d` 上，按视锥换算成"正好铺满整屏"。
        //  那个做法本身没错（HUD 照旧在最上层），但用户要的**读法**是"场景里发生了什么事"，
        //  而不是"屏幕上蒙了一层东西"⇒ 现在：`_healRoot` 钉在**触发时的玩家位置**上，
        //  十字从 7.2m 高处掉下来、光圈贴着地面（y≈0.06）扩出去、生命符从玩家身上升起来
        //  ⇒ 镜头怎么动、有没有 HUD，这四段都在**世界里**，一次也不读相机位置 ✓
        //  （唯一用到相机的是 ①/④ 的 billboard 朝向：俯视 60° 下 XY 平面的十字会缩成一条线）
        //
        //  ── 触发源：**订阅别人的事件**，而不是在表现层"猜" ──
        //  用户说的是"每分钟整点回血"。那个回血在计时制下**确实每分钟发生一次**：
        //  `Clock.WaveIndex > WaveIndex` 每 60 秒必然成立 ⇒ `StartWave` → `ResetPlayerForNewWave`。
        //  但表现层没有任何办法"看出"它 —— 玩家本来就满血时，`Hp` 前后**一模一样**，差值是 0 ✗
        //  ⇒ 由**发起点**广播：`Combat.cs` 的 `MinuteHealEvent`（发布点就是那个回血方法）。
        //    本文件**只订阅**（`OnMinuteHeal`），`RunSession.cs` 一行都不用改 ✓
        //    （事件体里带着 `HealHp / Hp / MaxHp / Pos`，满血时 `HealHp` 是 0 —— 特效照样要放：
        //      那是"整点机制在跑"的读法，而不是"回了多少"的读数）✓
        //
        //  ── "接着玩家回血"是**视觉顺序**，不是重算数值 ──
        //  回血本身在事件发布那一刻就结算完了（`RunSession.ResetPlayerForNewWave`）。
        //  所以第 ④ 段只做一件事：光圈到边之后，**生命符从玩家身上升起来**——让玩家看见
        //  "我是被治的那一个" ✓ 数值层一行都没动（那是 `RunSession` / `Stats` 的地盘）✓
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 回血特效的**总时长**（秒）。用户给的区间是 1.2~1.6 ⇒ 取 **1.50**。
        ///
        /// ★ 四段的时长比例（用户要求"分四段比例，自己定并说明"）★
        ///   ① 十字下落  0.00 → **0.42**（28%）从 7.2m 高处落到离地 1.35m（k² 缓入 = 被重力拉）
        ///   ② 炸开      0.42 → **0.72**（20%）十字放大淡出 + 碎片飞散 + 一圈冲击环
        ///   ③ 光圈扩散  0.72 → **1.16**（29%）贴地从 0.9m 扩到 **28.28m** = 场地半对角线
        ///   ④ 玩家回血  1.16 → **1.50**（23%）光圈淡出抬起，玩家身上起一枚向上的生命符
        ///   合计 0.42 + 0.30 + 0.44 + 0.34 = 1.50 ✓
        /// ⇒ 四段是**顺序**的（用户原话"落下一个绿色十字，然后炸开，绿色光圈扩散到全场，
        ///   接着玩家回血"就是顺序）⇒ 每一刻只有一段是主体，抓图才对得上号 ✓
        /// ⇒ 1.50s 播完 ⇒ 判据"1.8s 后全部消失"自然成立（留 0.3s 余量）✓
        /// </summary>
        public const float HealFxTotal = 1.50f;
        /// <summary>① 的结束时刻：十字落到最低点、同时炸开。</summary>
        private const float HealFallEnd = 0.42f;
        /// <summary>② 的结束时刻：碎片飞完、冲击环收掉**且**地面光圈开始扩散。</summary>
        private const float HealBurstEnd = 0.72f;
        /// <summary>③ 的结束时刻：光圈抵达场地边缘（半对角线）。之后进入 ④。</summary>
        private const float HealRingEnd = 1.16f;
        /// <summary>十字从多高落下（米）。7.2m 在俯视 60° 的相机里仍在画面内 ⇒ 看得见"从上面掉下来"✓</summary>
        private const float HealCrossDropFrom = 7.2f;
        /// <summary>十字落到的悬停高度（米）。玩家模型约 1.86m 高 ⇒ 1.35 在胸口上方，不糊在脸上 ✓</summary>
        private const float HealCrossDropTo = 1.35f;
        /// <summary>光圈起步半径（米）。0 起步会有一瞬间"是一条看不见的线"，从 0.9 起才一直有厚度 ✓</summary>
        private const float HealRingR0 = 0.9f;

        /// <summary>
        /// 光圈的最大半径（米）= 场地半对角线 = `arenaHalf × √2`。
        /// ★ `arenaHalf` 从 `tuning.farm` 读（**不写死 20**）：场地改大小时这条自动跟着走 ✓
        /// </summary>
        public static float HealRingRadiusMax =>
            Mathf.Max(1f, TableDatabase.Tuning.farm.arenaHalf) * 1.4142f;

        /// <summary>回血绿。与 HUD 的"治疗"弹字同一色系，不新开一套配色。</summary>
        private static readonly Color HealFxCore = new Color(0.42f, 1f, 0.55f, 1f);
        private static readonly Color HealFxRim = new Color(0.18f, 0.92f, 0.38f, 1f);

        /// <summary>
        /// 抓图用的**慢放倍率**（默认 1 = 正常速度）。
        /// 四段里最短的 ② 只有 0.30s，而桥接抓一张图要 1 帧 + 往返 ⇒ 正常速度下**抓不准**哪一段
        /// ⇒ 验收探针把它设成 6，四段各抓一张，抓完设回 1。**不参与任何判定**，也不改时长常量 ✓
        /// （与本文件既有的 `DebugSwingHold` / `DebugFxHold` 是同一条通道，不是新机制）
        /// </summary>
        public static float HealFxSlow = 1f;

        private GameObject _healRoot;
        private MeshRenderer _healCross, _healBurst, _healRing, _healPulse;
        private Material _healCrossMat, _healBurstMat, _healRingMat, _healPulseMat;
        /// <summary>已播时间；`&lt; 0` = 没在播（默认）⇒ 四段全灭。</summary>
        private float _healT = -1f;
        /// <summary>特效的世界落点（触发那一刻的玩家位置）。</summary>
        private Vector3 _healAt;
        /// <summary>② 的碎片只喷一次（进入 ② 的那一帧喷）—— 用布尔标记，不靠时间差比较。</summary>
        private bool _healBurstFired;

        /// <summary>区间归一化：x 在 [a,b] 里的进度（0..1）。四段全靠它切时间轴。</summary>
        private static float Ramp01(float a, float b, float x)
            => Mathf.Clamp01((x - a) / Mathf.Max(1e-4f, b - a));

        /// <summary>
        /// 已经播完（或从没播过）的四段必须真的不在场上。
        /// ★ 判据是"1.8s 后全部消失"—— 而且判据落在 `activeSelf` 上，不是落在 alpha≈0 上 ★
        ///   只把 alpha 调到 0 而物件还开着，"消失"就只是画面上看不见，
        ///   下一次 `HealFxVisible` 一读还是 true ⇒ 判据会被自己骗过去 ✗
        /// </summary>
        private void HideHealFx()
        {
            if (_healCross != null) _healCross.gameObject.SetActive(false);
            if (_healBurst != null) _healBurst.gameObject.SetActive(false);
            if (_healRing != null) _healRing.gameObject.SetActive(false);
            if (_healPulse != null) _healPulse.gameObject.SetActive(false);
            if (_healRoot != null) _healRoot.SetActive(false);
        }

        /// <summary>
        /// 建四段的渲染件（**只建一次**，之后只 `SetActive` + 改 transform）。
        /// 刻意放在 `Build` 里而不是"第一次回血时"：池化物件在运行期不分配，
        /// 抓图/自检的时间线才不会因为"第一次用了才建"而抖一帧 ✓
        ///
        /// ★ 网格只用了两个键：**5902**（生命十字，本来就有）与 **5928**（细环，本轮新增）★
        ///   冲击环（②）与地面光圈（③）**共用 5928 这一份网格**，只是各自缩放到不同半径 ——
        ///   同一根"环"在两层时间轴上跑，不需要第二份网格 ✓
        ///   十字（①）与回血符（④）也共用 5902 ✓ ⇒ 四段 = **2 份网格 + 4 份材质**（建池时一次建好）
        /// </summary>
        private void EnsureHealFx()
        {
            if (_healRoot != null) return;
            if (_vfxShader == null) _vfxShader = Shader.Find("Seedfall/Vfx");
            // 找不到 Vfx 着色器 ⇒ **不做这个特效**。宁可没有，也不要一块不透明板子把画面糊住 ✗
            if (_vfxShader == null) return;

            _healRoot = new GameObject("HealWorldFx");
            _healRoot.transform.SetParent(_root, false);

            // 四份材质**逐段独占**（只有 4 份、建池时建好）：每一段都要自己做 alpha 斜坡，
            // 共享就会被"后一段的斜坡"改掉前一段的读数 ✓（与 `BlastFx.Mat` 同一条理由）
            _healCrossMat = VfxMat(HealFxCore, HealFxRim, VfxCoreBoostFlat);
            _healBurstMat = VfxMat(HealFxCore, HealFxRim, VfxCoreBoostFlat);
            _healRingMat = VfxMat(HealFxCore, HealFxRim, VfxCoreBoostFlat);
            _healPulseMat = VfxMat(HealFxRim, HealFxCore, VfxCoreBoostFlat);

            _healCross = AttachFxChild(FxRuneCrossMesh(5902), _healCrossMat, "HealCross");
            _healBurst = AttachFxChild(FxRingMesh(5928, 40, 0.90f), _healBurstMat, "HealBurstRing");
            _healRing = AttachFxChild(FxRingMesh(5928, 40, 0.90f), _healRingMat, "HealGroundRing");
            _healPulse = AttachFxChild(FxRuneCrossMesh(5902), _healPulseMat, "HealPulse");
            HideHealFx();
        }

        /// <summary>建一个挂在 `_healRoot` 下的渲染件（`MeshObj` 只挂 `_root`，这里要挂到特效根上）。</summary>
        private MeshRenderer AttachFxChild(Mesh mesh, Material mat, string name)
        {
            GameObject go = MeshObj(mesh, mat, name);
            go.transform.SetParent(_healRoot.transform, false);
            return go.GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// ★ 回血特效的**唯一触发源**：`MinuteHealEvent`（定义在 `Combat.cs:536`，发布在
        /// `RunSession.ResetPlayerForNewWave()` 的回血那一行）★
        ///
        /// ★ 为什么表现层不能"读 HP 差值"自己判断 ★
        /// 玩家本来就是满血时 `Hp` 前后**一模一样**，差值是 0 ✗；而"血量涨了"还会把吸血、
        /// 红心拾取一起混进来 ✗ ⇒ 由发起点广播，表现层只订阅、只画 ✓
        /// 注意：`HealHp` 满血时是 0，**特效照样要放** —— 那是"整点机制在跑"的读法 ✓
        ///
        /// ★ v196：用事件里带的 `Pos` ★
        /// 四段都发生在**世界空间**，必须有"落在哪里"这个数 —— 而事件里本来就有
        /// （`Combat.cs` 的 `MinuteHealEvent.Pos`，注释写着"局部特效/飘字需要"）。
        /// 这样表现层不必自己去问会话玩家在哪，也不必担心"事件到达时玩家已经跑开了"✓
        /// </summary>
        private void OnMinuteHeal(MinuteHealEvent e)
        {
            StartHealFx(e.Pos);
        }

        /// <summary>
        /// 把回血特效**从 0 开始播**。`pos` = 触发那一刻的玩家位置（= 十字落点、光圈圆心）。
        ///
        /// ★ 四段一开始都是**关**的：`_healT = 0` 那一帧只有 ① 亮起来 ★
        ///   （"t=0 时四段都不播"指的是**局内 t=0**：第 1 波开局那次回血不发事件 ⇒
        ///     `_healT` 一直是 -1、四段一直 inactive。这条由 `RunSession` 保证，本层只订阅 ✓）
        /// </summary>
        private void StartHealFx(Vector2 pos)
        {
            if (_healRoot == null) return;
            _healT = 0f;
            _healAt = new Vector3(pos.x, 0f, pos.y);
            _healBurstFired = false;
            _healRoot.SetActive(true);
            _healRoot.transform.position = _healAt;
            _healRoot.transform.rotation = Quaternion.identity;
            _healRoot.transform.localScale = Vector3.one;
            // ① 立刻亮；②③④ 等各自的时刻再亮（`TickHealFx` 里按时间轴开）
            if (_healCross != null) _healCross.gameObject.SetActive(true);
            if (_healBurst != null) _healBurst.gameObject.SetActive(false);
            if (_healRing != null) _healRing.gameObject.SetActive(false);
            if (_healPulse != null) _healPulse.gameObject.SetActive(false);
        }

        /// <summary>
        /// 四段的推进。**全部在世界空间**：`_healRoot` 钉在触发时的玩家位置上，
        /// 十字从天上掉下来、光圈贴着地面扩出去 —— 画面里没有任何"贴在相机前的全屏面片" ✓
        ///
        /// ★ 为什么不再每帧跟着相机走 ★
        /// 旧版把三件套钉在 `cam.position + forward × d` 上铺满屏幕（"全屏绿"）。
        /// 用户明确不要那个观感 ⇒ 现在**一次也不读相机位置**，只在 ① 里借相机朝向做 billboard，
        /// 让十字"正对着镜头"（否则俯视下只能看到一条侧边）✓
        /// </summary>
        private void TickHealFx(float dt)
        {
            if (_healT < 0f) return;
            if (_healCross == null || _healBurst == null || _healRing == null || _healPulse == null)
            { _healT = -1f; return; }

            _healT += dt / Mathf.Max(0.05f, HealFxSlow);
            float t = _healT;
            if (t >= HealFxTotal) { _healT = -1f; HideHealFx(); return; }

            // 十字 / 回血符要"正对镜头"：俯视 60° 下 XY 平面的十字会缩成一条线 ⇒ 借相机朝向
            Quaternion face = Quaternion.identity;
            if (_cam == null) _cam = Camera.main;
            if (_cam != null) face = _cam.transform.rotation;

            // ───────────── ① 十字下落：0 → 0.42 ─────────────
            // 高度用 k² 缓入 ⇒ 读起来是"被重力拉下来"，不是"匀速平移"
            float k1 = Ramp01(0f, HealFallEnd, t);
            float dropY = Mathf.Lerp(HealCrossDropFrom, HealCrossDropTo, k1 * k1);
            float crossS = Mathf.Lerp(0.95f, 1.75f, k1);
            // 出现的头 22% 淡入（避免"凭空跳出来"），落地前保持满亮
            float crossA = Mathf.SmoothStep(0f, 1f, Ramp01(0f, 0.22f * HealFallEnd, t));

            // ───────────── ② 炸开：0.42 → 0.72 ─────────────
            // 十字在 ② 里**放大 + 淡出**（只靠缩放与颜色，不逐实例改 alpha）
            float k2 = Ramp01(HealFallEnd, HealBurstEnd, t);
            if (t >= HealFallEnd)
            {
                crossS = Mathf.Lerp(1.75f, 2.90f, k2);
                crossA *= 1f - Mathf.SmoothStep(0f, 1f, Ramp01(0f, 0.55f, k2));
                if (!_healBurstFired)
                {
                    _healBurstFired = true;
                    EmitHealBurst(_healAt + Vector3.up * HealCrossDropTo);
                }
            }
            _healCross.transform.localPosition = new Vector3(0f, dropY, 0f);
            _healCross.transform.localRotation = face;
            _healCross.transform.localScale = Vector3.one * crossS;
            SetVfxColor(_healCrossMat,
                new Color(HealFxCore.r, HealFxCore.g, HealFxCore.b, 0.92f * crossA),
                new Color(HealFxRim.r, HealFxRim.g, HealFxRim.b, 0.75f * crossA));

            // ② 的冲击环：在十字炸开的那个高度上扩一圈，0.25 → 2.8m
            bool burstOn = t >= HealFallEnd && t < HealBurstEnd;
            if (_healBurst.gameObject.activeSelf != burstOn) _healBurst.gameObject.SetActive(burstOn);
            if (burstOn)
            {
                float br = Mathf.Lerp(0.25f, 2.80f, Mathf.Sqrt(k2));      // 快起步、缓收尾
                float ba = 1f - Mathf.SmoothStep(0f, 1f, k2);
                _healBurst.transform.localPosition = new Vector3(0f, HealCrossDropTo, 0f);
                _healBurst.transform.localRotation = Quaternion.identity;
                _healBurst.transform.localScale = new Vector3(br, 1f, br);
                SetVfxColor(_healBurstMat,
                    new Color(HealFxCore.r, HealFxCore.g, HealFxCore.b, 0.95f * ba),
                    new Color(HealFxRim.r, HealFxRim.g, HealFxRim.b, 0.55f * ba));
            }

            // ───────────── ③ 光圈扩散：0.72 → 1.16 ─────────────
            // ★ 为什么从 `HealBurstEnd` 起、而不是与 ② 同时起 ★
            //   第一版是 0.42 起（与炸开同时）。实测抓图时发现：光圈在 0.62 的曲率下**跑得比 ② 快**，
            //   到 0.72（② 结束）时半径已经 16.6m ⇒ 画面上"炸开"那一段的主体其实已经是光圈，
            //   四段里的 ③ 只剩"从 16.6 涨到 28"这一小截 ✗ 用户要的顺序是
            //   "炸开 → 然后光圈扩散到全场"⇒ 让它**在 ② 结束之后才开始**才是那句话的形状 ✓
            // 半径从 0.9 扩到 `HealRingRadiusMax`（= arenaHalf × √2 = 场地半对角线）
            // ⇒ 判据"覆盖全场"是**算出来的**，不是"看起来挺大" ✓
            // 曲率 `k^0.62`：起步快、越往外越慢 ⇒ 读作"冲击波扩散"，不是"匀速拉大一个圆" ✓
            float k3 = Ramp01(HealBurstEnd, HealRingEnd, t);
            bool ringOn = t >= HealBurstEnd;
            if (_healRing.gameObject.activeSelf != ringOn) _healRing.gameObject.SetActive(ringOn);
            float ringR = HealRingR0;
            if (ringOn)
            {
                float maxR = HealRingRadiusMax;
                ringR = Mathf.Lerp(HealRingR0, maxR, Mathf.Pow(k3, 0.62f));
                // 亮度：起步 0.9，中段 1.0（最亮），④ 里从满亮淡到 0
                float ringA = Mathf.Lerp(0.55f, 1f, Ramp01(0f, 0.25f, k3));
                if (t >= HealRingEnd) ringA *= 1f - Mathf.SmoothStep(0f, 1f, Ramp01(HealRingEnd, HealFxTotal, t));
                // ④ 里光圈**整圈轻微抬起**（位移淡出，与颜色淡出同时用）
                float lift = 0.55f * Mathf.SmoothStep(0f, 1f, Ramp01(HealRingEnd, HealFxTotal, t));
                _healRing.transform.localPosition = new Vector3(0f, 0.06f + lift, 0f);
                _healRing.transform.localRotation = Quaternion.identity;
                _healRing.transform.localScale = new Vector3(ringR, 1f, ringR);
                SetVfxColor(_healRingMat,
                    new Color(HealFxCore.r, HealFxCore.g, HealFxCore.b, 0.90f * ringA),
                    new Color(HealFxRim.r, HealFxRim.g, HealFxRim.b, 0.80f * ringA));
            }

            // ───────────── ④ 玩家回血：1.16 → 1.50 ─────────────
            // "接着玩家回血"：光圈到边之后，**生命符从玩家身上升起来**，最后消失。
            // 数值在事件发布那一刻就结算完了（`RunSession.ResetPlayerForNewWave`），
            // 这一段只是"让玩家看见自己是被治的那一个" ✓
            //
            // ★ 抓图实测调过的两个数（第一版抓出来"看不见"）★
            //   ① 起跳高度 0.35 → **0.55**：0.35m 那一刻生命符有一半埋进玩家脚下的耕地床面
            //   ② 尺寸 1.05 → **1.35**：1.05×（= 1m 见方）在 1.86m 高的角色旁边只有几十像素，
            //      而这一段只有 0.34 秒 ⇒ 图上读不出来 ✗ 放大到 1.35 才在玩家身边"立得住" ✓
            float k4 = Ramp01(HealRingEnd, HealFxTotal, t);
            bool pulseOn = t >= HealRingEnd;
            if (_healPulse.gameObject.activeSelf != pulseOn) _healPulse.gameObject.SetActive(pulseOn);
            if (pulseOn)
            {
                // 上升 0.55 → 2.25m，同时右转 120°（自转是"升起来"的读法，不是在抖）
                _healPulse.transform.localPosition = new Vector3(0f, Mathf.Lerp(0.55f, 2.25f, k4), 0f);
                _healPulse.transform.localRotation = face * Quaternion.Euler(0f, 0f, -120f * k4);
                _healPulse.transform.localScale = Vector3.one * Mathf.Lerp(1.35f, 0.85f, k4);
                // 包络：前 25% 起亮、之后淡掉 ⇒ 是"升起并化开"，不是"到点硬灭"
                float pa = k4 < 0.25f
                    ? Mathf.SmoothStep(0f, 1f, k4 / 0.25f) * 0.95f
                    : Mathf.Lerp(0.95f, 0f, Mathf.SmoothStep(0f, 1f, (k4 - 0.25f) / 0.75f));
                SetVfxColor(_healPulseMat,
                    new Color(HealFxRim.r, HealFxRim.g, HealFxRim.b, pa),
                    new Color(HealFxCore.r, HealFxCore.g, HealFxCore.b, pa * 0.65f));
            }
        }

        /// <summary>
        /// ② 炸开时喷的那一把碎片：**走既有的 `_fxMeshes` 通道**（与挥击碎片 / 水球同一套），
        /// 网格取自 `FxShardMesh` 缓存、材质取自 `VfxMatCached` ⇒ 这一次爆发不新建任何网格/材质 ✓
        /// 淡出仍然只靠缩放（`S0 → S1`），共享材质**一个字节都不改** ✓
        /// 数量 9：一分钟一次、每次 9 个物件 ⇒ 对帧时与内存都是噪声级
        /// </summary>
        private void EmitHealBurst(Vector3 at)
        {
            Material hi = VfxMatCached(HealFxCore, HealFxRim, VfxCoreBoostFlat);
            Material lo = VfxMatCached(HealFxRim, HealFxCore, VfxCoreBoostFlat);
            for (int i = 0; i < 9; i++)
            {
                float a = (i / 9f) * Mathf.PI * 2f + 0.31f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                GameObject go = MeshObj(FxShardMesh(i % 6), (i & 1) == 0 ? hi : lo, "HealBurstShard");
                go.transform.position = at + dir * 0.18f;
                go.transform.rotation = Random.rotation;
                float s0 = Random.Range(0.13f, 0.22f);
                _fxMeshes.Add(new MeshBit
                {
                    Go = go,
                    Vel = dir * Random.Range(2.6f, 4.6f) + Vector3.up * Random.Range(1.4f, 2.8f),
                    Spin = new Vector3(Random.Range(-380f, 380f),
                                       Random.Range(-380f, 380f),
                                       Random.Range(-380f, 380f)),
                    Gravity = 9f,
                    T = -0.012f * i,          // 逐个起跳 ⇒ 是"炸开"不是"一整块平移"
                    Total = FxLife(0.30f),
                    S0 = s0,
                    S1 = s0 * 0.12f
                });
            }
            // 炸开的一记绿光（把"这一下真的发生了"也写进场上的光里）
            IlluminateRamp(at, Color.Lerp(HealFxCore, Color.white, 0.25f), 5.5f, 2.6f, 0.28f, 3);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  ② **玩家移动轨迹：灰色立方体灰尘块（显现 → 消散）**
        //
        //  ── 参数（用户说"自己定，但要灰尘感不是砖块感"）──
        //    间隔 **0.10s**｜寿命 **0.85s**｜大小 **0.15m**｜上浮 **0.26~0.46 m/s**
        //    ⇒ 同屏约 0.85/0.10 ≈ **8.5 块**在飘（池给到 24，近三倍余量：
        //      抓图慢放 `DebugFxHold` 与低帧率下 `while` 补发都不会打穿池）✓
        //
        //  ★ 三个数字都是**抓图实测标定**出来的，不是拍脑袋 ★
        //    ① 大小 0.15m：先按用户建议的 0.12m 做，抓图实测**只有 8 像素** ⇒ 草地上的白点，
        //       读不出轨迹 ✗
        //    ② 间隔 0.10s：0.12s 时同屏 5 块太稀 ✗
        //    ③ 寿命 0.85s：**这一条最关键** —— 0.55s 时"带子"只有
        //       `走速 2.5 m/s × 0.55s ≈ 1.4m` 长，在俯视相机下是一小团 ✗
        //       0.85s ⇒ 约 **2.1m**，才读得出"身后一条轨迹" ✓
        //       上界被"停下来 1 秒必须全没"卡死：0.85 < 1.0 ✓（这是用户的判据）
        //  ── "灰尘感"靠三件事，而不是靠贴图 ──
        //    ① 先小后大再缩没（`0.25 → 1 → 0`），不是"一个方块从开始到结束一样大"
        //    ② 每块随机偏航 + 缓慢自转 + 轻微侧倾 ⇒ 没有两块是同一个朝向
        //    ③ 上浮 + 峰值大小随块抖动 ⇒ 读作"扬起来又散掉"，不是"地上砖头"
        //  ── "站着不动不产生"是**硬判据** ──
        //    只有 `SyncPlayer` 判定的 `moving` 为真才走产生分支（`Moved(-1, …)`：
        //    这一帧位移 >5mm 且速度 >0.05 m/s）⇒ 罚站一帧都不产生 ✓
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>池容量。用户建议 24。</summary>
        public const int DustMax = 24;
        /// <summary>一块灰尘的寿命（秒）。</summary>
        public const float DustLife = 0.85f;
        /// <summary>两块之间的间隔（秒）。</summary>
        public const float DustEvery = 0.10f;
        /// <summary>峰值边长（米）。用户建议 0.12；实测 0.12 在俯视相机下只有 8 像素 ⇒ 定 0.15。</summary>
        public const float DustSize = 0.15f;

        private struct DustPuff
        {
            public GameObject Go;
            public Vector3 Base;
            public float T;
            public float Total;
            public float Peak;
            public float Rise;
            public float Yaw;
            public float Spin;
            public bool Live;
        }

        private DustPuff[] _dust;
        private Material _dustMat;
        private float _dustClock;
        /// <summary>本局已经生成过多少块（探针/自检读它来证明"动就出、不动就不出"）。</summary>
        public int DustEmitted;
        /// <summary>此刻还活着的块数。</summary>
        public int DustAlive
        {
            get
            {
                if (_dust == null) return 0;
                int n = 0;
                for (int i = 0; i < _dust.Length; i++) if (_dust[i].Live) n++;
                return n;
            }
        }

        private void EnsureDustPool()
        {
            if (_dust != null) return;
            // 灰色。走 `MatCached`（共享、颜色恒定、**永不逐实例改写**）⇒ 淡出只能靠缩放 ✓
            // ★ 带自发光（`emissive: true`）不是为了好看，是为了**夜里也在** ★
            //   灰尘块本身只有 0.12m，`NightLighting` 一压暗它就与草地同色 ⇒
            //   "移动轨迹"在画面上等于不存在 ✗ 自发光 0.6×灰让它任何时段都能被认出来 ✓
            _dustMat = MatCached(new Color(0.80f, 0.78f, 0.74f), true);
            _dust = new DustPuff[DustMax];
            for (int i = 0; i < DustMax; i++)
            {
                GameObject go = Prim(PrimitiveType.Cube, "DustPuff", Vector3.zero,
                                     Vector3.one * DustSize, _dustMat);
                go.SetActive(false);
                _dust[i] = new DustPuff { Go = go };
            }
        }

        /// <summary>取一个空槽；池满了就复用**最接近寿命终点**的那一个（"复用最老的"）。</summary>
        private int AllocDust()
        {
            int fallback = 0;
            float oldest = float.MaxValue;
            for (int i = 0; i < _dust.Length; i++)
            {
                if (!_dust[i].Live) return i;
                float left = _dust[i].Total - _dust[i].T;
                if (left < oldest) { oldest = left; fallback = i; }
            }
            return fallback;
        }

        private void EmitDust(Vector3 at)
        {
            if (_dust == null) return;
            int i = AllocDust();
            DustPuff p = _dust[i];
            // 落点在脚下、略偏后 + 抖开 ⇒ 不是一条笔直的"砖路"
            Vector3 jitter = new Vector3(Random.Range(-0.10f, 0.10f), 0f, Random.Range(-0.10f, 0.10f));
            // 略高于地面 0.10m：耕地床面本身是抬起来的（实测约 0.1m），
            // 贴着 y=0.02 生成的灰尘会被床沿埋掉一半 ⇒ 取 0.10 在空地上仍是"脚下"，在床面上也不会被吞 ✓
            p.Base = new Vector3(at.x, 0.10f, at.z) + jitter;
            p.T = 0f;
            p.Total = DustLife;
            p.Peak = DustSize * Random.Range(0.80f, 1.20f);
            p.Rise = Random.Range(0.26f, 0.46f);
            p.Yaw = Random.Range(0f, 360f);
            p.Spin = Random.Range(-90f, 90f);
            p.Live = true;
            if (p.Go != null)
            {
                p.Go.transform.position = p.Base;
                p.Go.transform.rotation = Quaternion.Euler(0f, p.Yaw, 0f);
                p.Go.transform.localScale = Vector3.one * (p.Peak * 0.25f);
                p.Go.SetActive(true);
            }
            _dust[i] = p;
            DustEmitted++;
        }

        private void TickDust(float dt, Vector3 playerPos, bool moving)
        {
            if (_dust == null) return;

            // ── ① 推进 / 收尸 ──
            for (int i = 0; i < _dust.Length; i++)
            {
                if (!_dust[i].Live) continue;
                DustPuff p = _dust[i];
                p.T += dt;
                if (p.T >= p.Total || p.Go == null)
                {
                    p.Live = false;
                    if (p.Go != null) p.Go.SetActive(false);
                    _dust[i] = p;
                    continue;
                }
                float k = p.T / p.Total;
                // 显现（前 30%：0.25 → 1）＋ 消散（后 70%：1 → 0.02，同时上浮）
                float s = k < 0.30f
                    ? Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, k / 0.30f))
                    : Mathf.Lerp(1f, 0.02f, Mathf.SmoothStep(0f, 1f, (k - 0.30f) / 0.70f));
                p.Go.transform.position = p.Base + Vector3.up * (p.Rise * p.T);
                p.Go.transform.localScale = Vector3.one * (p.Peak * s);
                p.Go.transform.rotation = Quaternion.Euler(9f * Mathf.Sin(p.Yaw), p.Yaw + p.Spin * p.T, 6f);
                _dust[i] = p;
            }

            // ── ② 产生：**只在真的在移动时** ──
            if (!moving)
            {
                // 站着不动：不产生，且把节拍预置满 ⇒ 一起步就立刻出第一块，不用等 0.12 秒
                _dustClock = DustEvery;
                return;
            }
            _dustClock += dt;
            int guard = 0;
            while (_dustClock >= DustEvery && guard++ < DustMax)
            {
                _dustClock -= DustEvery;
                EmitDust(playerPos);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  ③ **敌人死亡痕迹：血液溅射 + 地面血渍（3 秒缓慢消失），BOSS ×2.2**
        //
        //  用户原话："重新制敌人死亡痕迹，需要**类似血液溅射**效果，**有颜色纹理（深浅分明）**"
        //  ⇒ 上一版是"绿色爆汁"、纯色（`SplatGreen` 一份材质）。本轮整段重做：
        //
        //  ── ① 为什么"有颜色纹理（深浅分明）"不能只靠一个纯色 ──
        //    · **程序化 `Texture2D`**（64×64，生成一次进静态缓存）：给**形状**与**细颗粒深浅**
        //      —— 主血团 + 14 颗飞溅小点 + 5 道拖尾，每个像素的颜色在
        //        深 `#5A0C10` ↔ 亮 `#C0392B` 之间按"离中心的距离 + 噪声"取 ⇒ 一片血渍里
        //        同时有深红与亮红（这才是"深浅分明"）✓
        //    · **顶点色**：给**每片之间**的差异（每个槽的色调、亮度、UV 旋转都不一样）
        //      以及**径向渐层**（中心亮、边缘暗，5×5 网格 = 16 个格、逐顶点插值）✓
        //    ⇒ 两条都做：贴图管"一片里的纹理"，顶点色管"片与片的不同" ✓
        //
        //  ── ② 贴图从哪来（为什么不是 PNG）──
        //    与道具图标同一条纪律：**加一件东西不该多一份美术依赖**。
        //    64×64 的 `Color32[]` 在 C# 里算出来，`SetPixels32` + `Apply(false, false)`，
        //    `FilterMode.Bilinear` / `WrapMode.Clamp` / `HideFlags.DontSave`（不进资源、不写盘）✓
        //    用 `System.Random(固定种子)` ⇒ 每次运行长出**一模一样**的血迹（抓图可比、可复现）✓
        //
        //  ── ③ 材质：**只有一份**（8 片血渍共用）──
        //    ★ 这里有一个坑（实测过的方向）★
        //      `Seedfall/Vfx` 既不采样贴图、也不读顶点色（只有 `_BaseColor` / `_RimColor`）
        //      ⇒ 想"贴图 + 顶点色"就必须换一支**内建**的、两者都吃的着色器。
        //      `Sprites/Default` 正好是：`c = tex2D(_MainTex, uv) * IN.color`（顶点色乘进贴图），
        //      并且它在 `ProjectSettings/GraphicsSettings.asset` 的
        //      `m_AlwaysIncludedShaders` 里（fileID 10753 / 10754）⇒ 打包也不会被剥掉 ✓
        //    ⇒ 8 片血渍**共用 1 份材质**（`Sprite/Default` + 血渍贴图）。
        //      那"每片颜色不同"与"缓慢淡出"怎么办？**都在顶点色里** ——
        //      顶点色是**逐槽网格自己的**（8 份网格在建池时建好），改它不碰任何共享资源 ✓
        //      淡出 = 逐帧改这 8 份网格的顶点色 alpha（判据要的就是"缓慢消失"，
        //      而"不逐实例改 alpha"指的是**不许碰共享材质** —— 这里材质一个字节都没动）✓
        //
        //  ── ④ 尸块（另外一半）—— v211 升级 ──
        //    3D 碎块向外飞散 + 重力落体 + 落地衰减 + **缩小**消失；
        //    颜色与淡出走**逐渲染器 MPB**（240 块共用 1 份 `VfxMatCached`，既不复制材质也不改共享材质）。
        //    颜色 = **敌人自身主色**（`EnemyDef.Color`）向深血棕混 45% ⇒ "读得出是这只怪炸的，但仍然是血"；
        //    读不到（敌人已不在列表 / 颜色是默认近白）就退回深红棕 ✓
        //    深浅还多一层：`Seedfall/Vfx` 的 `lerp(_BaseColor, _RimColor, fres)`
        //    ⇒ 每块的**面是亮红、边是深红**，同一块上也能读出深浅 ✓
        //
        //  ── ⑤ 数值（v211：按用户口径重定）──
        //    尸块 **7 块**（BOSS **18 块** = round(7×2.5)）｜边长 **0.10~0.28m**｜寿命 **0.8~1.4s**
        //    重力 **18 m/s²**｜水平初速 **2~4**、竖直 **3~5**｜BOSS 尺寸 **×1.6**
        //    血渍 **每次 2~3 片**（随机偏移 + 随机朝向）｜半径 = 0.55 × **1.8~2.2** ｜峰值 alpha **0.55~0.75**
        //    血渍前 35% 保持满强度、之后 smoothstep 渐隐，同时铺开 10% ⇒ "缓慢消失"而不是"到点没了"✓
        //    BOSS 的**血渍**半径仍乘 **2.2**（`BossSplatScale`）⇒ 判据"普通 vs boss 半径比 ≈2.2"✓
        //    上限：尸块 **240** / 血渍 **40**，满了就复用"剩余寿命最短"的那一个 ⇒ 连发不超上限 ✓
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>尸块的池上限（用户点名"同屏碎块 ≤ 240"）。满了就复用**剩余寿命最短**的槽。</summary>
        public const int SplatShardMax = 240;
        /// <summary>地面血渍的池上限（用户点名"血渍 ≤ 40"）。</summary>
        public const int SplatStainMax = 40;
        /// <summary>地面血渍的寿命（秒）。用户点名的 3 秒。</summary>
        public const float SplatStainLife = 3.0f;
        /// <summary>BOSS 的**血渍**放大倍率（用户给的例子 2.2）。尸块另有自己的两个倍率，见 `WorldView.Gibs.cs`。</summary>
        public const float BossSplatScale = 2.2f;
        /// <summary>普通敌人 / BOSS 各多少块尸块（BOSS = round(7 × 2.5) = 18）。</summary>
        public const int SplatShardsNormal = 7;
        public const int SplatShardsBoss = 18;
        /// <summary>血渍的基准半径（米）。实际值 = 它 × `StainGrowMin`~`StainGrowMax` × BOSS 倍率。</summary>
        public const float SplatStainR = 0.55f;

        /// <summary>
        /// 血渍网格在 `_fxMeshCache` 里的**键位起点**（每槽一份，键 = 起点 + 槽号）。
        ///
        /// ★ 为什么从 5920 搬到了 6000（真 bug，不是洁癖）★
        ///   槽位从 8 提到 40 之后，键位就是 5920..5959 —— 而这两段号码**早就有人占了**：
        ///     · **5920** = `FacingQuadMesh()`（朝向指示的单位四边形）
        ///     · **5928** = 回血细环（`FxRingMesh(5928, 40, 0.90f)`，`_healBurst` / `_healRing` 共用）
        ///   `_fxMeshCache` 只按整数键存网格、**不校验形状** ⇒ 撞上之后先建的那个赢，
        ///   后者拿到**形状完全不对**的网格：血渍那两槽会变成四边形/圆环，
        ///   而且顶点数与 `SplatStain.Base`（25）对不上 ⇒ `ApplyStainFade` 的守卫会拒绝写色，
        ///   那两片血渍从此**永不淡出**（静默、不报错 —— 最难查的那一类）✗
        ///   ⇒ 6000 起是空的（工程里 6xxx 只被 `WorldView.Prog.cs` 从 **6100** 开始用）✓
        /// </summary>
        private const int StainMeshKeyBase = 6000;

        // ── 血色三档（#6B0F14 → #C0392B 之间取三档，血渍贴图与兜底材质都用它们）──
        //    `Seedfall/Vfx` 用 `_CoreBoost = 1.0`（`VfxCoreBoostFlat`）：
        //    血是"要读色相"的东西，1.35 会把深红烧成橙红 ⇒ 用 flat 版保住血色的红 ✓
        private static readonly Color BloodDeep = new Color(0.420f, 0.059f, 0.078f, 1f);   // #6B0F14
        private static readonly Color BloodDark = new Color(0.557f, 0.106f, 0.122f, 1f);   // #8E1B1F
        private static readonly Color BloodBright = new Color(0.753f, 0.224f, 0.169f, 1f); // #C0392B

        private struct SplatShard
        {
            public GameObject Go;
            /// <summary>它就是那块尸块的渲染器 —— 上色/淡出走它的 `MaterialPropertyBlock`（不新建材质）</summary>
            public MeshRenderer Renderer;
            public Vector3 Vel;
            public Vector3 Spin;
            public float Gravity;
            public float T;
            public float Total;
            public float S0, S1;
            /// <summary>这块尸块的最大边长（米）—— 落地静止高度按它算</summary>
            public float Meters;
            /// <summary>这声炸裂的颜色（敌人主色混血棕 / 兜底深红棕）</summary>
            public Color Tone;
            /// <summary>上一次写进 MPB 的淡出系数（差 &lt; 0.03 就不重复写）</summary>
            public float Fade;
            /// <summary>是否已经落过地（落地后速度衰减，不再用重力加速）</summary>
            public bool Landed;
            public bool Live;
        }

        private sealed class SplatStain
        {
            public GameObject Go;
            /// <summary>本槽**自己的**网格（5×5 网格、UV 已按槽旋转）—— 顶点色写它，不碰共享资源</summary>
            public Mesh Mesh;
            /// <summary>本槽的基准顶点色（建池时算好：槽色调 × 径向渐层）。淡出 = 只乘 alpha ✓</summary>
            public Color[] Base;
            public float T;
            public float Total;
            public float R;
            /// <summary>这一片的**峰值不透明度**（0.55~0.75）—— 一次死亡铺 2~3 片，压低才不会糊成一片</summary>
            public float Peak = 1f;
            /// <summary>上一次写进网格的淡出系数（差 &lt;0.01 就不重复写，省掉每帧的 `SetColors`）</summary>
            public float Fade;
            public bool Live;
        }

        private SplatShard[] _splatShards;
        private SplatStain[] _splatStains;
        /// <summary>写顶点色用的复用缓冲（不每帧 `new List`）</summary>
        private readonly List<Color> _stainColBuf = new List<Color>(256);
        /// <summary>本局一共喷过多少片（探针用）。</summary>
        public int SplatShardsEmitted;
        /// <summary>最后一次爆出来的血渍半径（米）—— "boss 明显更大"的可读数字。</summary>
        public float SplatLastRadius;
        /// <summary>最后一次爆出来的溅射片边长（米）—— 与半径一起证明"尺寸比 ≈ 2.2"。</summary>
        public float SplatLastShardSize;
        /// <summary>最后一次爆的是不是 boss。</summary>
        public bool SplatLastWasBoss;
        /// <summary>此刻还活着的溅射片数。</summary>
        public int SplatShardsAlive
        {
            get
            {
                if (_splatShards == null) return 0;
                int n = 0;
                for (int i = 0; i < _splatShards.Length; i++) if (_splatShards[i].Live) n++;
                return n;
            }
        }
        /// <summary>此刻还活着的血渍数。</summary>
        public int SplatStainsAlive
        {
            get
            {
                if (_splatStains == null) return 0;
                int n = 0;
                for (int i = 0; i < _splatStains.Length; i++) if (_splatStains[i].Live) n++;
                return n;
            }
        }

        private void EnsureSplatPools()
        {
            if (_splatShards != null) return;
            _splatShards = new SplatShard[SplatShardMax];
            // ★ 240 块尸块共用**一份**材质 ★
            //   颜色与淡出全部走逐渲染器 `MaterialPropertyBlock`（见 `WriteGibColor`）⇒
            //   既做到"颜色跟敌人走"，又没有"每块一份材质"的内存增长，也没碰共享材质 ✓
            Material gibMat = VfxMatCached(GibToneDeepBrown, GibRimBrown, VfxCoreBoostFlat);
            for (int i = 0; i < SplatShardMax; i++)
            {
                // 复用攻击碎片的网格（6 个 variant 轮转）⇒ 尸块**没有一份新网格**
                GameObject go = MeshObj(FxShardMesh(i % 6), gibMat, "SplatShard");
                go.SetActive(false);
                _splatShards[i] = new SplatShard
                {
                    Go = go,
                    Renderer = go.GetComponent<MeshRenderer>(),
                    Tone = GibToneDeepBrown,
                    Fade = 1f
                };
            }

            // ── 血渍：`SplatStainMax`(40) 个槽 = 每个槽**自己的**网格（键 6000..6039）+ **共用的 1 份材质** ──
            Material stainMat = BloodStainMat();
            _splatStains = new SplatStain[SplatStainMax];
            for (int i = 0; i < SplatStainMax; i++)
            {
                // 每槽的 UV 旋转角不同 ⇒ 同一张贴图长不出两片一样的血渍
                float uvYaw = i * (360f / SplatStainMax) + 17f;
                Mesh mesh = BloodStainMesh(StainMeshKeyBase + i, i, uvYaw);
                GameObject go = MeshObj(mesh, stainMat, "SplatStain");
                go.SetActive(false);
                _splatStains[i] = new SplatStain
                {
                    Go = go,
                    Mesh = mesh,
                    Base = BloodStainColors(i),
                    Fade = 1f
                };
            }
        }

        private void ClearSplatPools()
        {
            // 物件是 `_root` 的子物体（`Clear()` 里那句 `Destroy(_root)` 会带走它们）⇒ 这里只断引用 ✓
            // ★ 材质必须**显式销毁**：血渍那份材质不在任何缓存里（它是这一局的），没人替它收 ★
            //   （与 `BlastFx.Mat` 同一条纪律；8 份网格相反 —— 它们在 `_fxMeshCache` 里，
            //     静态缓存、下一局直接复用，**不能**销毁 ✓）
            if (_splatStainMat != null) Object.Destroy(_splatStainMat);
            _splatStainMat = null;
            _splatStains = null;
            _splatShards = null;
            SplatShardsEmitted = 0;
        }

        private void ClearHealFx()
        {
            if (_healCrossMat != null) Object.Destroy(_healCrossMat);
            if (_healBurstMat != null) Object.Destroy(_healBurstMat);
            if (_healRingMat != null) Object.Destroy(_healRingMat);
            if (_healPulseMat != null) Object.Destroy(_healPulseMat);
            _healCrossMat = null;
            _healBurstMat = null;
            _healRingMat = null;
            _healPulseMat = null;
            _healCross = null;
            _healBurst = null;
            _healRing = null;
            _healPulse = null;
            _healRoot = null;
            _healT = -1f;
            _healBurstFired = false;
        }

        private void EnsureFx3Pools()
        {
            EnsureDustPool();
            EnsureSplatPools();
            EnsureHealFx();
        }

        private void ClearFx3Pools()
        {
            _dust = null;
            _dustMat = null;
            _dustClock = 0f;
            DustEmitted = 0;
            ClearSplatPools();
            ClearHealFx();
        }

        private int AllocSplatShard()
        {
            int fallback = 0;
            float oldest = float.MaxValue;
            for (int i = 0; i < _splatShards.Length; i++)
            {
                if (!_splatShards[i].Live) return i;
                float left = _splatShards[i].Total - _splatShards[i].T;
                if (left < oldest) { oldest = left; fallback = i; }
            }
            return fallback;
        }

        private int AllocSplatStain()
        {
            int fallback = 0;
            float oldest = float.MaxValue;
            for (int i = 0; i < _splatStains.Length; i++)
            {
                if (!_splatStains[i].Live) return i;
                float left = _splatStains[i].Total - _splatStains[i].T;
                if (left < oldest) { oldest = left; fallback = i; }
            }
            return fallback;
        }

        /// <summary>
        /// 一次死亡痕迹的入口。`boss` 决定大小与数量，`enemyId` 用来取"敌人自身主色"。
        /// ★ 实现在 `WorldView.Gibs.cs`（尸块炸裂 + 血渍放大，纯增量放另一个 partial 文件）★
        /// </summary>
        private void EmitSplat(Vector3 at, bool boss, string enemyId)
        {
            EmitGore(at, boss, enemyId);
        }

        /// <summary>
        /// 把"槽基准顶点色 × 淡出系数"写进这一槽自己的网格。
        /// ★ 只改 alpha，rgb 不动 ⇒ 血色的深浅关系在整个 3 秒里保持不变，变的只是浓度 ✓
        /// ★ 复用 `_stainColBuf`（容量 256）⇒ 每帧零分配；
        ///   而调用点有"变化 &lt;0.01 就不写"的节流 ⇒ 满场景 40 片同时在时也只有 40 次 `SetColors` ✓
        /// </summary>
        private void ApplyStainFade(SplatStain st, float fade)
        {
            if (st == null || st.Mesh == null || st.Base == null) return;
            // ★ 这条守卫是**实测教训**写进来的 ★
            //   第一版网格是"64 个三角各发 3 个顶点"（192 个顶点），而基准色只有 25 份（按 5×5 网格），
            //   `Mesh.SetColors` 当场抛 `Mesh.colors is out of bounds. The supplied array needs to be
            //   the same size as the Mesh.vertices array.` —— 而且**抛在 `EmitSplat` 中途**，
            //   于是后半段（`_splatStains[si] = st` / `SplatLastRadius`）全没执行 ✗
            //   ⇒ 网格已改成"25 顶点 + 索引缓冲"，这里再加一道显式守卫：
            //     长度对不上就**不写**（宁可这次淡出没生效，也不要在事件回调里抛异常）✓
            if (st.Base.Length != st.Mesh.vertexCount) return;
            _stainColBuf.Clear();
            for (int i = 0; i < st.Base.Length; i++)
            {
                Color c = st.Base[i];
                // ★ 多乘一个 `Peak` ★ —— 一次死亡铺 2~3 片，峰值不透明降到 0.55~0.75，
                //   叠在一起才不会糊成一片红（用户原话"透明度略降，避免糊成一片"）✓
                _stainColBuf.Add(new Color(c.r, c.g, c.b, c.a * fade * st.Peak));
            }
            st.Mesh.SetColors(_stainColBuf);
        }

        /// <summary>
        /// 死亡痕迹的推进：尸块飞散落体/落地衰减/缩小淡出 + 血渍缓慢淡出。
        /// ★ 实现在 `WorldView.Gibs.cs` 的 `TickGore`（定长数组、零分配）★
        /// </summary>
        private void TickSplat(float dt)
        {
            TickGore(dt);
        }

        /// <summary>击杀事件：原地喷一记血。BOSS 由 id 判定（与 `OnKilledShake` 同一判据）。</summary>
        private void OnEnemyKilledSplat(EnemyKilledEvent e)
        {
            // `e.EnemyId` 同时用于两件事：判定 boss、以及取"这只敌人自己的主色"（见 `GoreTone`）
            EmitSplat(new Vector3(e.Pos.x, 0f, e.Pos.y), e.EnemyId == "boss", e.EnemyId);
        }

        // ── 只读读数（探针 / 报告用；不参与任何判定，也不改任何状态）──

        /// <summary>回血特效此刻在不在播。</summary>
        public bool HealFxPlaying => _healT >= 0f;
        /// <summary>回血特效已经播了多少秒（没在播时是 -1）。</summary>
        public float HealFxAge => _healT;
        /// <summary>
        /// 此刻在第几段：0 = 没在播，1 = 十字下落，2 = 炸开，3 = 光圈扩散，4 = 玩家回血。
        /// 判据"四段各一张图"就是靠这个数**对上号**的（而不是"看图像猜是哪一段"）✓
        /// </summary>
        public int HealFxStage
        {
            get
            {
                if (_healT < 0f) return 0;
                if (_healT < HealFallEnd) return 1;
                if (_healT < HealBurstEnd) return 2;
                if (_healT < HealRingEnd) return 3;
                return 4;
            }
        }
        /// <summary>四段此刻**实际**有没有被画出来（`SetActive` 之后才为真）。</summary>
        public bool HealFxVisible
            => (_healCross != null && _healCross.gameObject.activeSelf)
            || (_healBurst != null && _healBurst.gameObject.activeSelf)
            || (_healRing != null && _healRing.gameObject.activeSelf)
            || (_healPulse != null && _healPulse.gameObject.activeSelf);
        /// <summary>光圈此刻的半径（米）。"扩散到覆盖全场"是靠这个数与 `HealRingRadiusMax` 比的。</summary>
        public float HealRingRadius => _healRing != null && _healRing.gameObject.activeSelf
            ? _healRing.transform.localScale.x : 0f;
        /// <summary>十字此刻的高度（米，相对落点）。</summary>
        public float HealCrossY => _healCross != null ? _healCross.transform.localPosition.y : 0f;

        /// <summary>
        /// 三套表现的一行读数。判据都落在**数字**上：
        ///   灰尘 `alive/emitted`｜痕迹 `片活/片共/喷过`、`渍活`、`末次半径/片边长`｜
        ///   回血 `playing/age/stage`、`光圈半径`
        /// </summary>
        public string Fx3Report()
        {
            float stainMax = 0f;
            int stainAlive = 0;
            if (_splatStains != null)
                for (int i = 0; i < _splatStains.Length; i++)
                {
                    if (!_splatStains[i].Live) continue;
                    stainAlive++;
                    if (_splatStains[i].R > stainMax) stainMax = _splatStains[i].R;
                }
            return $"DUST alive={DustAlive}/{DustMax} emitted={DustEmitted}"
                 + $"｜SPLAT shard={SplatShardsAlive}/{SplatShardMax} emitted={SplatShardsEmitted}"
                 + $" stain={stainAlive}/{SplatStainMax}"
                 + $" last={(SplatLastWasBoss ? "boss" : "normal")}/R{SplatLastRadius:0.00}/S{SplatLastShardSize:0.000}"
                 + $"｜HEAL playing={HealFxPlaying} age={(HealFxPlaying ? HealFxAge : -1f):0.00}/{HealFxTotal:0.00}"
                 + $" stage={HealFxStage} ring={HealRingRadius:0.00}/{HealRingRadiusMax:0.00}"
                 + $" visible={HealFxVisible}";
        }

        /// <summary>
        /// 血渍"到底画没画出来"的逐项取证（探针用）。
        /// 顺序按**能让人排除嫌疑**的顺序排：活着没有 → 挂没挂 → 渲染器开没开 →
        /// **引擎认为它在不在视锥里** → 尺寸 → 网格/材质/着色器 → 顶点色 alpha（淡出到哪了）。
        /// </summary>
        public string StainDebug()
        {
            if (_splatStains == null) return "STAIN 池未建";
            var sb = new System.Text.StringBuilder("STAIN");
            int shown = 0;
            for (int i = 0; i < _splatStains.Length && shown < 2; i++)
            {
                SplatStain st = _splatStains[i];
                if (st == null || st.Go == null) continue;
                shown++;
                MeshRenderer mr = st.Go.GetComponent<MeshRenderer>();
                MeshFilter mf = st.Go.GetComponent<MeshFilter>();
                Material mat = mr != null ? mr.sharedMaterial : null;
                float va = -1f;
                if (st.Mesh != null)
                {
                    Color[] cs = st.Mesh.colors;
                    if (cs != null && cs.Length > 0) va = cs[cs.Length / 2].a;   // 取中间那个顶点
                }
                sb.Append($"\n  #{i} live={st.Live} active={st.Go.activeSelf}/{st.Go.activeInHierarchy}")
                  .Append($" pos=({st.Go.transform.position.x:0.00},{st.Go.transform.position.y:0.00},{st.Go.transform.position.z:0.00})")
                  .Append($" scale=({st.Go.transform.localScale.x:0.00},{st.Go.transform.localScale.y:0.00},{st.Go.transform.localScale.z:0.00})")
                  .Append($" T={st.T:0.00}/{st.Total:0.00} R={st.R:0.00} fade={st.Fade:0.00}")
                  .Append($"\n     renderer={(mr == null ? "null" : "ok")} enabled={(mr != null && mr.enabled)}")
                  .Append($" isVisible={(mr != null && mr.isVisible)}")
                  .Append($" mesh={(mf == null || mf.sharedMesh == null ? "null" : mf.sharedMesh.name)}")
                  .Append($" verts={(st.Mesh == null ? 0 : st.Mesh.vertexCount)}")
                  .Append($" mat={(mat == null ? "null" : mat.name)}")
                  .Append($" shader={(mat == null ? "null" : mat.shader.name)}")
                  .Append($" tex={(mat == null || mat.mainTexture == null ? "null" : mat.mainTexture.name)}")
                  .Append($" vcolA={va:0.00}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// ★ "有颜色纹理（深浅分明）"的**机器取证**（探针用，只读）★
        ///
        /// 判据不能是"我看着像血"，得是数字：
        ///   ① 贴图：尺寸 / 采样方式 / **亮度直方图的两端**（最暗、最亮、中间档位数）
        ///      —— 单一纯色的贴图这里只会有 1 档；一张"深浅分明"的血迹会有十几档 ✓
        ///   ② 顶点色：8 片血渍各自的**顶点色 RGB 有多少种不同值**
        ///      —— 一色到底的实现这里是 1 种；这里应当 ≥ 8（每槽一个色调）× 径向渐层 ✓
        /// </summary>
        public string BloodEvidence()
        {
            var sb = new System.Text.StringBuilder("BLOOD");
            Texture2D tex = _bloodTex;
            if (tex == null)
            {
                sb.Append("\n  贴图：**还没生成**（血渍一次都没画过；Texture2D 是懒生成的）");
            }
            else
            {
                Color32[] px = tex.GetPixels32();
                int min = 255, max = 0, opaque = 0;
                var buckets = new HashSet<int>();
                var colors = new HashSet<int>();
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 c = px[i];
                    if (c.a < 24) continue;                    // 只统计"有血"的像素
                    opaque++;
                    int luma = (c.r * 30 + c.g * 59 + c.b * 11) / 100;
                    if (luma < min) min = luma;
                    if (luma > max) max = luma;
                    buckets.Add(luma / 8);                     // 每 8 级算一档
                    colors.Add((c.r << 16) | (c.g << 8) | c.b);
                }
                sb.Append($"\n  贴图 {tex.width}×{tex.height} {tex.format} filter={tex.filterMode} wrap={tex.wrapMode}")
                  .Append($" hideFlags={tex.hideFlags}")
                  .Append($"\n    有血像素 {opaque}/{px.Length}｜亮度 min={min} max={max} 跨度={max - min}")
                  .Append($"｜亮度档位 {buckets.Count} 档（每 8 级一档）｜不同的 RGB {colors.Count} 种");
            }

            // 顶点色：8 个槽各有多少种不同颜色（含 alpha=1 的基准色）
            int slots = _splatStains == null ? 0 : _splatStains.Length;
            var allVertex = new HashSet<int>();
            sb.Append($"\n  顶点色：{slots} 片血渍");
            for (int i = 0; i < slots; i++)
            {
                SplatStain st = _splatStains[i];
                if (st == null || st.Base == null) continue;
                var set = new HashSet<int>();
                for (int v = 0; v < st.Base.Length; v++)
                {
                    Color c = st.Base[v];
                    int q = (Mathf.RoundToInt(c.r * 63f) << 12)
                          | (Mathf.RoundToInt(c.g * 63f) << 6)
                          | Mathf.RoundToInt(c.b * 63f);
                    set.Add(q);
                    allVertex.Add(q);
                }
                if (i < 4) sb.Append($"\n    #{i} 顶点色 {set.Count} 种（{st.Base.Length} 个顶点）");
            }
            sb.Append($"\n  8 片合计不同的顶点色 {allVertex.Count} 种 ⇒ 判据：>1 就证明每片都不一样，不是一色到底 ✓");
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ★ v196：**血迹贴图 / 血渍网格 / 生命符与细环的网格** ★
        //
        //  网格键位（与既有键 5100+ / 5200 / 5250 / 5400 / 5500 / 5600-5620 / 5700 /
        //  5730 / 5732 / 5740 / 5800 / 5900 / 5901 / **5902** / 5910 / **5920** / **5928** /
        //  以及 `WorldView.Prog.cs` 的 6100-6150 **都不撞**）：
        //      **6000..6039** —— 40 片血渍各自的网格（UV 逐槽转过角 ⇒ 形态各不相同）
        //      **5928**      —— 回血细环（② 的冲击环与 ③ 的地面光圈**共用这一份**）
        //      **5902**      —— 生命符十字（沿用上一轮那份；① 与 ④ 共用）
        //  ★ 血渍从 5920..5927 搬到 6000..6039 的原因见 `StainMeshKeyBase` 的注释 ★
        //    （5920 是 `FacingQuadMesh`、5928 是回血细环 —— 槽位一提到 40 就撞上了）
        //  ⇒ 本轮**新增网格 40 份**，全部建池时一次建好、之后只读
        //  ⇒ 运行期 `new Mesh` = 0，`new Material` = 0（血渍那份材质建池时建 1 份）✓
        //
        //  ★ 5900 / 5901 两份网格**删除** ★
        //    它们是旧"全屏回血"的贴脸四边形与边框（`FxScreenQuadMesh` / `FxScreenFrameMesh`），
        //    用户明确不要"全屏绿"⇒ 四段全改世界空间后它们没有任何调用点。
        //    留着就是两段**死代码 + 两个占用的键**（下一个人想用 5900 会以为有人占着）✗
        //    删掉它们不影响 `BuildFlatN` / `AddEdgeQuad` —— 那两个还被 `FxRuneCrossMesh` 用着 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>血迹贴图的边长（像素）。64 够画"主血团 + 飞溅点 + 拖尾"，也够小到生成一次就忘掉它。</summary>
        public const int BloodTexSize = 64;

        private static Texture2D _bloodTex;
        /// <summary>血渍共用**那一份**材质（`Sprites/Default` + 血迹贴图）。整局只建一次。</summary>
        private Material _splatStainMat;

        /// <summary>
        /// **血渍贴图**：程序化 `Texture2D`，只在第一次用到时生成一次、之后只读。
        ///
        /// ★ 形状（"类似血液溅射"）★
        ///   中心一团**不规则**主血团（半径按角度做三/七次谐波扰动，不是正圆）＋
        ///   14 颗**飞溅点**（远近大小都不同）＋ 5 道**径向拖尾**（越往外越细）
        ///   ⇒ 是"溅出来的一片"，不是"一个圆饼"✓
        /// ★ 深浅（用户点名的"深浅分明"）★
        ///   每个像素的颜色在 深 `#5A0C10` ↔ 亮 `#C0392B` 之间取值，取法有三层：
        ///     ① 离中心越远越深（径向）② 每一颗飞溅点自己又是一团小的径向渐变
        ///     ③ 一层固定的"斑驳"噪声 ⇒ 同一块血团里也有明有暗 ✓
        ///   ⇒ 这张图上不会出现"整片一个颜色"那种情况（探针在 `BloodEvidence()` 里数档位）✓
        /// ★ 采样设置 ★
        ///   `Bilinear`（血要糊，不要马赛克；道具图标才是 `Point`）｜`Clamp`（不重复采样）｜
        ///   `HideFlags.DontSave`（**不进资源库、不写盘、不参与打包**）｜`Apply(false, false)`（不生成 mip）✓
        /// ★ 可复现 ★
        ///   `System.Random(0x5EED)` 固定种子 ⇒ 每次运行、每台机器长出同一张血迹（抓图可比）✓
        /// </summary>
        private static Texture2D BloodTex()
        {
            if (_bloodTex != null) return _bloodTex;
            const int S = BloodTexSize;
            var rng = new System.Random(0x5EED);

            // ── ① 主血团：半径按角度扰动；再叠 3 个偏心"瓣"，让它更不像圆 ──
            const float mainR = 0.30f;
            float[] harmA = { 0.045f, 0.030f, 0.022f };
            float[] harmF = { 3f, 7f, 11f };
            float[] harmP = { 1.1f, 2.3f, 0.4f };
            // ── ② 飞溅点（18 颗）与拖尾（6 道）：先摇好，后面逐像素查 ──
            //  ★ 数量与长度是**抓图实测**调过的 ★
            //    第一版 14 颗 + 5 道**长**拖尾（0.34~0.64），抓出来像"星芒/蜘蛛"——
            //    拖尾又细又长，读不出"溅出来的血"✗ 现在：拖尾短而根部粗（0.22~0.42、根部 0.05），
            //    飞溅点提到 18 颗且大小更散 ⇒ 读作"一团血 + 四周的碎点"✓
            const int drops = 18;
            var dropA = new float[drops];
            var dropR = new float[drops];
            var dropS = new float[drops];
            for (int i = 0; i < drops; i++)
            {
                dropA[i] = (float)rng.NextDouble() * Mathf.PI * 2f;
                dropR[i] = 0.26f + (float)rng.NextDouble() * 0.22f;
                dropS[i] = 0.010f + (float)rng.NextDouble() * 0.042f;
            }
            const int trails = 6;
            var trailA = new float[trails];
            var trailL = new float[trails];
            for (int i = 0; i < trails; i++)
            {
                trailA[i] = (float)rng.NextDouble() * Mathf.PI * 2f;
                trailL[i] = 0.22f + (float)rng.NextDouble() * 0.20f;
            }
            // ── ③ 斑驳噪声（固定 23×23 格点 + 双线性插值 ⇒ 是"雾/絮"不是"白噪点"）──
            const int NG = 23;
            var noise = new float[NG * NG];
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)rng.NextDouble();

            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                float v = (y + 0.5f) / S;
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);

                    // 主血团：半径被扰动过
                    float mr = mainR;
                    for (int h = 0; h < 3; h++)
                        mr += harmA[h] * Mathf.Sin(harmF[h] * ang + harmP[h]);
                    float dens = mr > 0.001f ? Mathf.Clamp01(1f - d / mr) : 0f;
                    dens = dens * dens * (3f - 2f * dens);          // smoothstep 一下，边缘才软

                    // 主团内部：靠噪声压出"厚/薄"（dens 乘 0.55~1.0）
                    float nz = SampleNoise(noise, NG, u, v);
                    dens *= Mathf.Lerp(0.55f, 1f, nz);

                    // 飞溅点：各自一小团径向
                    for (int i = 0; i < drops; i++)
                    {
                        float cx = 0.5f + Mathf.Cos(dropA[i]) * dropR[i];
                        float cy = 0.5f + Mathf.Sin(dropA[i]) * dropR[i];
                        float ddx = u - cx, ddy = v - cy;
                        float dd = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                        if (dd < dropS[i])
                        {
                            float k = 1f - dd / dropS[i];
                            float dv = k * k * (3f - 2f * k);
                            if (dv > dens) dens = dv;
                        }
                    }
                    // 拖尾：沿一条射线，半径随距离线性收窄（根部粗、末端尖）
                    for (int i = 0; i < trails; i++)
                    {
                        float ca = Mathf.Cos(trailA[i]), sa = Mathf.Sin(trailA[i]);
                        float along = dx * ca + dy * sa;                 // 沿射线
                        float across = -dx * sa + dy * ca;               // 垂直射线
                        if (along <= 0f || along > trailL[i]) continue;
                        float w = (1f - along / trailL[i]) * 0.050f;
                        float ad = Mathf.Abs(across);
                        if (ad < w)
                        {
                            float k = 1f - ad / w;
                            float tv = k * k * 0.80f;
                            if (tv > dens) dens = tv;
                        }
                    }

                    if (dens <= 0.004f) { px[y * S + x] = new Color32(0, 0, 0, 0); continue; }

                    // ── 深浅：中心亮、外边深，再叠一层斑驳 ──
                    //   `*1.8` 与 `*0.65` 是抓图后调大的对比度：第一版 1.55/0.55 出来的血渍
                    //   整体偏"一片暗红"，图上读不出"深浅分明"✗
                    float tone = Mathf.Clamp01(0.98f - d * 1.80f + (nz - 0.5f) * 0.65f);
                    Color c = Color.Lerp(new Color(0.353f, 0.047f, 0.063f),   // #5A0C10 深
                                         new Color(0.753f, 0.224f, 0.169f),   // #C0392B 亮
                                         tone);
                    // 边缘比中心更淡一点点（alpha 也跟着 dens 走 ⇒ 形状就是 dens 的形状）
                    px[y * S + x] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(dens) * 255f));
                }
            }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false, false)
            {
                name = "BloodSplatTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _bloodTex = tex;
            return tex;
        }

        /// <summary>双线性采样一张 `NG×NG` 的噪声格点（格点在 [0,1] 上等距，首尾不回绕）。</summary>
        private static float SampleNoise(float[] grid, int ng, float u, float v)
        {
            float fx = u * (ng - 1), fy = v * (ng - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, ng - 2);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, ng - 2);
            float tx = fx - x0, ty = fy - y0;
            float a = grid[y0 * ng + x0], b = grid[y0 * ng + x0 + 1];
            float c = grid[(y0 + 1) * ng + x0], d = grid[(y0 + 1) * ng + x0 + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>
        /// 血渍**共用**的那一份材质：`Sprites/Default` + 血迹贴图。
        ///
        /// ★ 为什么是内建 `Sprites/Default`（本轮唯一一次用内建着色器）★
        ///   `Seedfall/Vfx` 只有两个颜色属性：不采样贴图、也不读顶点色 ⇒ 做不出"纹理 + 逐片顶点色"✗
        ///   而 `Sprites/Default` 的片元就是 `tex2D(_MainTex, uv) * IN.color`：
        ///   **贴图 × 顶点色**，正好是本轮要的两层 ✓ 它还在工程的
        ///   `ProjectSettings/GraphicsSettings.asset → m_AlwaysIncludedShaders`（fileID 10753/10754）里
        ///   ⇒ 打包时不会被剥掉 ✓（这一条是查工程文件确认的，不是记忆）
        /// ★ 找不到它怎么办 ★
        ///   退回 `Seedfall/Vfx` 的深红：形状会退化成一片圆（纹理丢失），但**绝不会看不见**。
        ///   探针的 `StainDebug()` 会把 `shader=` 打出来 ⇒ "退回了没有"是可读的 ✓
        /// </summary>
        private Material BloodStainMat()
        {
            if (_splatStainMat != null) return _splatStainMat;
            Shader sprite = Shader.Find("Sprites/Default");
            if (sprite != null)
            {
                _splatStainMat = new Material(sprite) { name = "SplatStainMat" };
                _splatStainMat.mainTexture = BloodTex();
            }
            else
            {
                _splatStainMat = VfxMat(BloodDark, BloodDeep, VfxCoreBoostFlat);
                _splatStainMat.name = "SplatStainMatFALLBACK";
            }
            return _splatStainMat;
        }

        /// <summary>
        /// 一片血渍的网格：**25 个顶点 / 64 个三角（16 格 × 2 三角 × 双面各一遍）**。
        ///
        /// ★ 为什么是"25 个顶点 + 索引缓冲"而不是"64 个三角各发 3 个顶点" ★
        ///   `Mesh.SetColors` 要求**颜色数组长度 = 顶点数**（实测报错原文：
        ///   `Mesh.colors is out of bounds. The supplied array needs to be the same size as the
        ///   Mesh.vertices array.`）⇒ 用重复顶点（192 个）就得存 192 份基准色，
        ///   而"逐帧改浓度"每次要算 192 个；用索引则顶点 25 个、基准色 25 份、逐帧写 25 个 ✓
        ///   双面也照样成立：**同一批顶点**被两套绕序各引用一遍（索引反过来即可）✓
        ///
        /// ★ UV 为什么逐槽转一个角 ★
        ///   8 片共用同一张贴图，如果不转，8 片血渍会是**同一个形状**（只是位置不同）✗
        ///   逐槽把 UV 绕 (0.5,0.5) 转 `uvYaw` 度 ⇒ 8 个不同的溅射方向 ✓
        /// ★ 顶点色为什么逐槽不同 ★
        ///   `BloodStainColors(slot)` 给每槽一个色调（深/中/亮不同）与一个径向渐层
        ///   （中心亮 → 边缘深）⇒ "每片都有差异"落在顶点色上，材质仍然只有一份 ✓
        /// ★ 法线写死朝上 ★
        ///   不调 `RecalculateNormals` —— 双绕序会把相邻三角形的法线**互相抵消**（得到零向量）✗
        ///   贴地血渍的法线本来就只有"朝上"一种正确答案 ✓
        /// </summary>
        private static Mesh BloodStainMesh(int key, int slot, float uvYaw)
        {
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            const int NC = 4;                       // 4×4 格 ⇒ 5×5 顶点
            Color[] cols = BloodStainColors(slot);
            float rad = uvYaw * Mathf.Deg2Rad;
            float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);

            var verts = new List<Vector3>(25);
            var uvs = new List<Vector2>(25);
            var normals = new List<Vector3>(25);
            var colors = new List<Color>(25);
            for (int gz = 0; gz <= NC; gz++)
            {
                for (int gx = 0; gx <= NC; gx++)
                {
                    int vi = gz * (NC + 1) + gx;
                    float u = gx / (float)NC, v = gz / (float)NC;
                    // 贴地：XZ 平面，边长 2（-1..1）⇒ 调用方用 localScale = 半径 就对上真实尺寸
                    verts.Add(new Vector3(u * 2f - 1f, 0f, v * 2f - 1f));
                    // UV：绕中心转 uvYaw
                    float uu = u - 0.5f, vv = v - 0.5f;
                    uvs.Add(new Vector2(0.5f + uu * cs - vv * sn, 0.5f + uu * sn + vv * cs));
                    normals.Add(Vector3.up);
                    colors.Add(cols[vi]);
                }
            }
            // 16 格 × 2 三角 × 双面 = 64 三角 = 192 个索引
            var tris = new List<int>(192);
            for (int gz = 0; gz < NC; gz++)
            {
                for (int gx = 0; gx < NC; gx++)
                {
                    int i00 = gz * (NC + 1) + gx;
                    int i10 = i00 + 1;
                    int i01 = i00 + (NC + 1);
                    int i11 = i01 + 1;
                    // 绕序 A
                    tris.Add(i00); tris.Add(i11); tris.Add(i10);
                    tris.Add(i00); tris.Add(i01); tris.Add(i11);
                    // 绕序 B（把顺序反过来 ⇒ 另一面）
                    tris.Add(i10); tris.Add(i11); tris.Add(i00);
                    tris.Add(i11); tris.Add(i01); tris.Add(i00);
                }
            }
            var m = new Mesh { name = "BloodStain" + key };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>
        /// 第 `slot` 片血渍的**基准顶点色**（5×5 = 25 个）。
        ///   ① 槽色调：在 4 档血色之间按槽号取一个 ⇒ 8 片里最深的那片与最亮的那片明显不同
        ///   ② 径向渐层：中心 1.0 → 边缘 0.55 ⇒ 一片血渍内部也是"中间亮、边缘深"（深浅分明）✓
        /// </summary>
        private static Color[] BloodStainColors(int slot)
        {
            const int NC = 4;
            // 8 个槽在"深 ↔ 亮"之间均匀取，再让相邻两槽不重复
            float t = (slot * 3 % 8) / 7f;                       // 0,3,6,1,4,7,2,5 /7 ⇒ 8 个不同值
            Color tint = Color.Lerp(BloodDeep, BloodBright, t);
            var cols = new Color[(NC + 1) * (NC + 1)];
            for (int gz = 0; gz <= NC; gz++)
            {
                for (int gx = 0; gx <= NC; gx++)
                {
                    float u = gx / (float)NC * 2f - 1f;
                    float v = gz / (float)NC * 2f - 1f;
                    float r = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
                    float lum = Mathf.Lerp(1f, 0.55f, r);         // 中心亮、边缘深
                    cols[gz * (NC + 1) + gx] = new Color(
                        Mathf.Clamp01(tint.r * lum),
                        Mathf.Clamp01(tint.g * lum),
                        Mathf.Clamp01(tint.b * lum),
                        1f);
                }
            }
            return cols;
        }

        /// <summary>一块四边形（两个三角），四个顶点共用同一个法线。</summary>
        private static void AddEdgeQuad(List<Vector3> v, List<Vector3> vn,
                                        Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, Vector3 n)
        {
            v.Add(a0); v.Add(a1); v.Add(b1);
            v.Add(a0); v.Add(b1); v.Add(b0);
            for (int i = 0; i < 6; i++) vn.Add(n);
        }

        /// <summary>生命符：一个十字（两条等宽矩形），法线朝 -Z ⇒ 平色、不参与菲涅尔。</summary>
        private static Mesh FxRuneCrossMesh(int key)
        {
            if (_fxMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            const float arm = 0.5f;      // 半长
            const float half = 0.155f;   // 半宽（竖条 / 横条各 0.31 宽）
            var v = new List<Vector3>(12);
            var vn = new List<Vector3>(12);
            Vector3 n = new Vector3(0f, 0f, -1f);
            // 竖条
            AddEdgeQuad(v, vn, new Vector3(-half, -arm, 0f), new Vector3(half, -arm, 0f),
                        new Vector3(-half, arm, 0f), new Vector3(half, arm, 0f), n);
            // 横条
            AddEdgeQuad(v, vn, new Vector3(-arm, -half, 0f), new Vector3(arm, -half, 0f),
                        new Vector3(-arm, half, 0f), new Vector3(arm, half, 0f), n);
            Mesh m = BuildFlatN("FxRuneCross" + key, v, vn);
            _fxMeshCache[key] = m;
            return m;
        }

        /// <summary>
        /// 与 `BuildFlat` 相同，但**法线由调用方给**（不 `RecalculateNormals`）。
        /// 只给"要刻意倾斜法线"的网格用；其余网格继续走 `BuildFlat`（那里的自动法线正是要的）✓
        /// </summary>
        private static Mesh BuildFlatN(string name, List<Vector3> verts, List<Vector3> normals)
        {
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetNormals(normals);
            var tris = new int[verts.Count];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ── v179：点光的**强度衰减** ──
        //
        // `NightLighting.Illuminate` 背后是 8 槽光池（Forward+），池里的灯**到期就平掉**
        // （`_poolUntil` 一过就 `intensity = 0`，`NightLighting.cs:344`），本身没有渐变 ✗
        // 而规格要"寿命 0.15 秒、强度随弧扫过**衰减**"⇒ 衰减只能由调用方给：
        // 把一个 ramp 拆成几次采样，每次用**更低的强度**、**很短的 `seconds`** 去覆盖池灯。
        // 取 `seconds ≈ Total/Steps + 0.02` ⇒ 上一盏在下一采样点前后到期、**复用同一个槽**，
        // 不会把 8 个槽全占掉（水壶 / 爆炸还要用它们）✓
        private struct LightRamp
        {
            public Vector3 Pos;
            public Color Color;
            public float Range;
            public float I0;
            public float T;
            public float Total;
            public int Steps;
            public int Fired;
        }

        private readonly List<LightRamp> _lightRamps = new List<LightRamp>();

        /// <summary>v179：请求一盏**会衰减**的点光（拆 `steps` 次采样，总寿命 `seconds`）。</summary>
        private void IlluminateRamp(Vector3 pos, Color color, float range, float intensity, float seconds, int steps)
        {
            _lightRamps.Add(new LightRamp
            {
                Pos = pos,
                Color = color,
                Range = range,
                I0 = intensity,
                T = 0f,
                Total = FxLife(Mathf.Max(0.02f, seconds)),
                Steps = Mathf.Max(1, steps),
                Fired = 0
            });
        }

        private void TickLightRamps(float dt)
        {
            for (int i = _lightRamps.Count - 1; i >= 0; i--)
            {
                LightRamp r = _lightRamps[i];
                r.T += dt;
                float k = Mathf.Clamp01(r.T / Mathf.Max(0.01f, r.Total));
                // 到现在为止该发几个采样点（k=0 就发第 1 个 ⇒ 起手立刻亮，不延迟一帧）
                int want = Mathf.Min(r.Steps, Mathf.FloorToInt(k * r.Steps) + 1);
                while (r.Fired < want)
                {
                    float ek = r.Steps <= 1 ? 0f : r.Fired / (float)(r.Steps - 1);
                    // 1.0 → 0.15 线性衰减：扫过去时最亮，收势时只剩余晖 ✓
                    NightLighting.Illuminate(r.Pos, r.Color, r.Range,
                        r.I0 * Mathf.Lerp(1f, 0.15f, ek), r.Total / r.Steps + 0.02f);
                    r.Fired++;
                }
                if (k >= 1f) { _lightRamps.RemoveAt(i); continue; }
                _lightRamps[i] = r;
            }
            // ★★ v307：**每道一份**（见 `_beamFxCd` 的复盘）★★
            for (int i = 0; i < BeamFxSlots; i++)
            {
                if (_wetCd[i] > 0f) _wetCd[i] -= dt;
                if (_beamFxCd[i] > 0f) _beamFxCd[i] -= dt;
            }
        }

        /// <summary>
        /// ★★ v307：水壶表现层的**几道并发**上限 —— 多柱最多 3 道（2 层 `can_multijet`），取 8 留余量。
        /// `BeamEvent.Slot` 对它取模（越界不出错，只是与第 0 道共用一格）✓
        /// </summary>
        private const int BeamFxSlots = 8;

        /// <summary>
        /// ★ v179：湿痕的节流计时。水壶每秒发 60 次，不过滤就是每秒 60 张贴花。
        /// ★★ v307：从**一个 float** 改成**每道一格** ★★
        ///   `FireBeam` 在同一帧连发 N 个 `BeamEvent`（`Time.unscaledTime` 完全相同），
        ///   而改前这里只有一个计时变量 ⇒ 第 0 道把它置成 0.18 之后，第 1..N−1 道
        ///   全部被 `if (_wetCd <= 0f)` 挡掉 ⇒ **只有第一根水柱在落点留下湿痕** ✗
        ///   （用户："多柱…只有第一个水柱有特效"）
        /// </summary>
        private readonly float[] _wetCd = new float[BeamFxSlots];

        /// <summary>
        /// ★ v180：水柱**起点沿瞄准方向外推**的距离（米）。理由见 `OnBeam` 里那段推导 ★
        /// 玩家模型世界高 1.8584m、肩宽 + 手里工具伸出量 ≈ 0.7~0.8m，柱体半宽上限 0.30m
        /// ⇒ 取 0.75 刚好把角色整个让开，又不至于吃掉短射程的一大截 ✓
        /// </summary>
        private const float JetStartOffset = 0.75f;

        private struct Swing
        {
            public GameObject Go;
            public float T;
            public float Total;
            public Vector2 Dir;
            public float Range;
            public float ArcDeg;
        }

        private readonly List<Swing> _swings = new List<Swing>();

        /// <summary>挥击的扫过动画：从 -半张角扫到 +半张角，同时放大到射程、下沉贴地。</summary>
        private void TickSwings(float dt)
        {
            for (int i = _swings.Count - 1; i >= 0; i--)
            {
                Swing s = _swings[i];
                s.T += dt;
                float k = Mathf.Clamp01(s.T / Mathf.Max(0.01f, s.Total));
                if (k >= 1f || s.Go == null)
                {
                    if (s.Go != null) Object.Destroy(s.Go);
                    _swings.RemoveAt(i);
                    continue;
                }
                float baseYaw = Mathf.Atan2(s.Dir.x, s.Dir.y) * Mathf.Rad2Deg;
                s.Go.transform.rotation = Quaternion.Euler(0f, baseYaw + Mathf.Lerp(-s.ArcDeg * 0.5f, s.ArcDeg * 0.5f, k), 0f);
                float grow = Mathf.Lerp(0.55f, 1f, Mathf.Min(1f, k * 2.6f));
                s.Go.transform.localScale = new Vector3(s.Range * grow, 1f, s.Range * grow);
                Vector3 p = s.Go.transform.position;
                s.Go.transform.position = new Vector3(p.x, Mathf.Lerp(0.50f, 0.12f, k), p.z);
                _swings[i] = s;
            }
        }

        /// <summary>
        /// 调试用：把挥击弧的持续时间拉长，便于截图验收。
        /// 正常 0.20 秒的动画在截图链路上根本抓不到（桥接往返就 1 秒以上），
        /// 这不是"效果没问题"的证据缺失，而是"没法验证"——所以给验收留一个可控的慢放通道。
        /// </summary>
        public static float DebugSwingHold;

        private void OnMeleeSwing(MeleeSwingEvent e)
        {
            // ★ v172 缺陷②：近战挥击 = 锄头的攻击动作 ★
            // 这个事件的投递方只有两处，**都是玩家**：`SwingHoe`（真挥击）与
            // `DebugSwing`（调试用的挥击特效）⇒ 不需要额外判据 ✓
            _pendingAttack = PlayerActor.WeaponHoe;
            _pendingAttackRepeat = false;   // v204：离散出手 ⇒ 消费处无条件重开动画（见 `_pendingAttackRepeat`）

            // v176：挥击弧从此吃**锄头这一路**的插件外观 ✓
            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Hoe);
            float range = Mathf.Max(0.8f, e.Range);
            float arcDeg = Mathf.Max(40f, e.ArcDeg);
            // ══════════════════════════════════════════════════════════════════
            // ★★ v304：**巨型 = 整圈**（用户需求①「特效也没有变成环形」）★★
            //
            // 为什么是"改一个张角"就够了 ✗ 不是"糊上去"，是因为这条链本来就是可参数的：
            //   · 主弧网格 `FxBladeMesh` 是一段**等厚环带**（内径 0.62R、外径 1.00R）
            //     ⇒ 张角开到 360° 时它本身就是**一圈完整的环带**（不是"更长的一条弧"）✓
            //   · `TickSwings` 把这条环带从 `−ArcDeg/2` 转到 `+ArcDeg/2` 并渐隐
            //     ⇒ 360° 时正好是**绕玩家一整圈、顺时针扫过**（与用户给的两个选项之一一致）✓
            //   · 碎片 `shardN` 沿 `±halfArc` 分布 ⇒ 360° 时撒满整圈，而不是全挤在正前方 ✓
            // ⇒ 三处几何全都只吃 `arcDeg` 这一个数，**没有第二份真相**（改一处三处一起对）✓
            //
            // ⚠️ 普通挥击一行都不碰：`e.FullRing` 为假（默认）时 `arcDeg` 仍是
            //    `MeleeFxArcDeg(hoe.arc)`（今天 96°）⇒ 半圆路径**逐位不变** ✓
            // ══════════════════════════════════════════════════════════════════
            if (e.FullRing) arcDeg = 360f;
            float hold = DebugSwingHold > 0.01f ? DebugSwingHold : 0.20f;

            // ★ `Scale` 不乘到 `range` 上 ★ ——`SwingHoe` 的 `range` 已经含 `RangePct`，
            //   这里再乘一次就会画出"比打得到的更远"的弧，玩家按弧判断距离会被骗 ✓
            //
            // 拖尾（插件 `Trail` 通道）= **延迟起跳的残影弧**：
            // 它们和主弧走同一个 `TickSwings`，只是 `T` 从负数开始
            // ⇒ `k` 被钳在 0 ⇒ 晚一档再扫过去，读起来就是"跟着一道残影" ✓
            int echoes = Mathf.RoundToInt(fx.Trail * 3f);
            for (int t = echoes; t >= 0; t--)
            {
                bool isMain = t == 0;
                Color core = fx.Core, rim = fx.Rim;
                if (!isMain)
                {
                    float fade = 1f - 0.30f * t;
                    core = new Color(core.r * fade, core.g * fade, core.b * fade, core.a * 0.50f);
                    rim = new Color(rim.r * fade, rim.g * fade, rim.b * fade, rim.a * 0.42f);
                }
                GameObject go = new GameObject(isMain ? "MeleeBlade" : "MeleeBladeEcho");
                go.transform.SetParent(_root, false);
                MeshFilter mf = go.AddComponent<MeshFilter>();
                // ★ v180：扇形面片 → **有厚度的刃**（`FxBladeMesh`，理由见那个函数）★
                mf.sharedMesh = FxBladeMesh();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                // v70：挥击弧也走 VFX 材质——置顶，才不会被近处草丛盖掉
                // v176：改走缓存版 —— 挥击每秒都在发生，原来每挥一次漏一份材质 ✓
                // 且用 `VfxCoreBoostFlat`（1.0）：弧是**一整片**，1.35 会把色相烧成黄白 ✓
                mr.sharedMaterial = VfxMatCached(core, rim, VfxCoreBoostFlat);
                go.transform.position = new Vector3(e.Pos.x, 0.50f, e.Pos.y);
                _swings.Add(new Swing
                {
                    Go = go,
                    T = isMain ? 0f : -0.035f * t,          // 负时间 = 错开起跳
                    Total = hold,
                    Dir = e.Dir,
                    Range = isMain ? range : range * (1f - 0.06f * t),
                    ArcDeg = arcDeg
                });
            }

            // ── v179：**多面体碎片**拼出弧形扫过（8~12 块）──
            //
            // 弧本身留着当"扫过的空气"（它负责"方向 + 范围"这两件事）；
            // 碎片是**实体颗粒**，沿**同一条弧**分布 ⇒ 读起来是"这一下劈碎了什么"，
            // 而不是"地上亮起一片光面" ✓
            // 数量 8~12，由插件通道微调（`Rings` 多一圈 ⇒ 多几块；`Sparks` 多 ⇒ 颗粒更碎）
            int shardN = Mathf.Clamp(9 + fx.Rings - fx.Sparks / 2, 8, 12);
            Material shardMat = VfxMatCached(fx.Core, fx.Rim, VfxCoreBoostFlat);
            // 第二份**已缓存**的材质（混白的那一半）。不是去改上一份的 alpha，是换另一份 ✓
            Material shardMatHi = VfxMatCached(Color.Lerp(fx.Core, Color.white, 0.45f), fx.Rim, VfxCoreBoostFlat);
            Vector2 sdn = e.Dir.sqrMagnitude > 0.0001f ? e.Dir.normalized : Vector2.up;
            float sweepYaw = Mathf.Atan2(sdn.x, sdn.y) * Mathf.Rad2Deg;
            float halfArc = arcDeg * 0.5f;
            for (int i = 0; i < shardN; i++)
            {
                float u = shardN <= 1 ? 0.5f : i / (float)(shardN - 1);
                float rad = (sweepYaw + Mathf.Lerp(-halfArc, halfArc, u)) * Mathf.Deg2Rad;
                var outw = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                float at = range * Mathf.Lerp(0.62f, 0.98f, u);
                GameObject sg = MeshObj(FxShardMesh(i % 6),
                    ((i & 1) == 0) ? shardMat : shardMatHi, "MeleeShard");
                sg.transform.position = new Vector3(
                    e.Pos.x + outw.x * at,
                    0.40f + Random.Range(-0.08f, 0.20f),
                    e.Pos.y + outw.z * at);
                // ★ **各自**随机朝向 + 自转 ⇒ 12 块碎片不会像一个刚体一起飞 ★
                sg.transform.rotation = Quaternion.Euler(
                    Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                float s0 = Random.Range(0.15f, 0.28f) * fx.Scale;
                _fxMeshes.Add(new MeshBit
                {
                    Go = sg,
                    Vel = outw * Random.Range(0.9f, 2.4f) + Vector3.up * Random.Range(0.4f, 1.3f),
                    Spin = new Vector3(Random.Range(-420f, 420f),
                                       Random.Range(-420f, 420f),
                                       Random.Range(-420f, 420f)),
                    Gravity = 7f,
                    T = -0.012f * i,              // 负时间 = 沿弧**逐个**起跳（这就是"扫过去"）
                    Total = FxLife(0.34f),
                    S0 = s0,
                    S1 = s0 * 0.10f
                });
            }

            // v74：挥击在近场掀一层暖光，让"挥"这个动作在夜里也读得到
            // v176：颜色 / 半径 / 强度吃插件（`Glow` 通道）
            // ★ v179：改成**会衰减**的 1 盏点光（0.15 秒）★
            //   旧版是一次性 `Illuminate(..., 0.14f)` —— 亮 0.14 秒然后**硬灭**，
            //   与"强度随弧扫过衰减"不是一回事。现在拆 3 个采样点、强度 1.0→0.15 ✓
            IlluminateRamp(new Vector3(e.Pos.x, 0.50f, e.Pos.y),
                Color.Lerp(fx.Core, fx.Rim, 0.40f),
                3.4f * fx.Scale, 2.2f * fx.Glow, 0.15f, 3);

            // ── 火花（插件 `Sparks` 通道）：从挥击出发点往后撒，像"刮过去的屑" ──
            if (fx.Sparks > 0)
                SpawnSparks(new Vector3(e.Pos.x, 0.55f, e.Pos.y), fx,
                    Mathf.Min(10, fx.Sparks), range * 0.55f, 4.0f);

            // ── 冲击环（插件 `Rings` 通道）：落在弧的前沿，不是脚下 ──
            // 放脚下会与"自己的站位"混淆；放前沿才读得出"这一下劈到了哪里" ✓
            if (fx.Rings > 0)
            {
                Vector2 dn = e.Dir.sqrMagnitude > 0.0001f ? e.Dir.normalized : Vector2.up;
                Vector3 apex = new Vector3(e.Pos.x + dn.x * range * 0.75f, 0.45f,
                                           e.Pos.y + dn.y * range * 0.75f);
                int rings = Mathf.Min(3, fx.Rings);
                for (int r = 0; r < rings; r++)
                    SpawnShockRing(apex, fx, 0.25f + 0.20f * r,
                        (1.0f + 0.55f * r) * fx.Scale, 0.30f + 0.07f * r, 0.45f);
            }

            // 震屏只对"真的有配重/炸裂"的插件抬起来 —— 基础挥击本来不震，
            // 无条件加震屏会改变手感，那不是"加特效"而是"改玩法" ✗
            if (fx.Shake > 1.01f) Shake(0.05f * fx.Shake, 0.10f);
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v201：新 BOSS 技能的特效（**只加表现，不参与任何判定**）★★
        //
        // 九条事件全部由 `Actors/Enemies.cs` 单向发出；这里只画，不回写任何世界状态 ✓
        // 复用的既有积木：`AddRingMarker`（贴地预警圈）/ `MeshObj` + `FxRingMesh`（扩散环）/
        //                  `VfxMat`（每次一份、随对象销毁 —— 与 `AddRingMarker` 同一取舍）/
        //                  `NightLighting.Illuminate`（自发光照亮）/ `Shake`（震屏）
        // 频率低（吼叫 5 环 / 4 秒、激光 1 次 / 4.5 秒）⇒ 每次一份材质不会成为泄漏源 ✓
        // ══════════════════════════════════════════════════════════════════════

        private struct BossRing { public GameObject Go; public Material Mat; public float T, Total, R0, R1; }
        private readonly List<BossRing> _bossRings = new List<BossRing>(32);

        private static readonly Color OldBossViolet = new Color(0.72f, 0.36f, 1f, 1f);
        private static readonly Color OldBossHot = new Color(1f, 0.42f, 0.18f, 1f);
        private static readonly Color MothRose = new Color(0.95f, 0.58f, 0.92f, 1f);
        private static readonly Color MothCold = new Color(0.62f, 0.86f, 1f, 1f);

        /// <summary>一个从 r0 扩到 r1 的**环形波**（boss 的吼叫 / 爆炸冲击都走它）。</summary>
        private void SpawnBossRing(Vector2 pos, float r0, float r1, float life, Color color, float y = 0.08f)
        {
            Material mat = VfxMat(color, color);
            GameObject go = MeshObj(FxRingMesh(5610, 30, 0.86f), mat, "BossRingWave");
            go.transform.position = new Vector3(pos.x, y, pos.y);
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = new Vector3(Mathf.Max(0.02f, r0) * 2f, 1f, Mathf.Max(0.02f, r0) * 2f);
            _bossRings.Add(new BossRing { Go = go, Mat = mat, T = 0f, Total = Mathf.Max(0.08f, life), R0 = r0, R1 = r1 });
        }

        private void TickBossSkillFx(float dt)
        {
            for (int i = _bossRings.Count - 1; i >= 0; i--)
            {
                BossRing r = _bossRings[i];
                r.T += dt;
                float u = Mathf.Clamp01(r.T / r.Total);
                if (r.Go != null)
                {
                    float rad = Mathf.Lerp(r.R0, r.R1, u * (2f - u));     // 先快后慢：波前在减速
                    r.Go.transform.localScale = new Vector3(rad * 2f, 1f, rad * 2f);
                }
                if (r.Mat != null)
                {
                    Color c = r.Mat.color;
                    c.a = 1f - u;
                    r.Mat.color = c;
                }
                if (r.T >= r.Total)
                {
                    if (r.Go != null) Object.Destroy(r.Go);
                    if (r.Mat != null) Object.Destroy(r.Mat);
                    _bossRings[i] = r;
                    _bossRings.RemoveAt(i);
                    continue;
                }
                _bossRings[i] = r;
            }
        }

        /// <summary>旧 BOSS「吼叫」：每一环一圈向外扩散 + 蓝紫光脉冲；起手（Ring = -1）只照亮。</summary>
        private void OnBossRoar(BossRoarEvent e)
        {
            Vector3 at = new Vector3(e.Pos.x, 1.0f, e.Pos.y);
            if (e.Ring < 0)
            {
                NightLighting.Illuminate(at, OldBossViolet, 8f, 4f, 0.45f);
                SpawnBossRing(e.Pos, 0.4f, 2.4f, 0.45f, OldBossViolet);
                return;
            }
            // 每一环：从 BOSS 体表扩到这个环的"波前"（逻辑给的 radius = 弹速 × 间隔）
            SpawnBossRing(e.Pos, 1.2f, Mathf.Max(2f, e.Radius), 0.92f, OldBossViolet, 0.10f + 0.01f * e.Ring);
            NightLighting.Illuminate(at, OldBossViolet, 6f, 2.6f, 0.30f);
            if (e.Ring == 0) Shake(0.05f, 0.12f);
        }

        /// <summary>旧 BOSS「蓄力爆炸」：红色预警圈（半径 = 爆炸半径，时长 = 蓄力时长）+ 一圈向内收的环。</summary>
        private void OnBossBlastCharge(BossBlastChargeEvent e)
        {
            AddRingMarker(e.Pos, e.Radius, e.Duration, OldBossHot);
            SpawnBossRing(e.Pos, e.Radius, e.Radius * 0.18f, e.Duration, OldBossHot, 0.07f);   // 向内收
            NightLighting.Illuminate(new Vector3(e.Pos.x, 1.0f, e.Pos.y), OldBossHot,
                                     e.Radius * 1.2f, 5f, e.Duration);
        }

        /// <summary>旧 BOSS「蓄力爆炸」炸开：一圈快速扩散 + 强光 + 震屏。</summary>
        private void OnBossBlast(BossBlastEvent e)
        {
            SpawnBossRing(e.Pos, 0.5f, e.Radius, 0.42f, new Color(1f, 0.85f, 0.45f, 1f), 0.12f);
            SpawnBossRing(e.Pos, 0.3f, e.Radius * 0.75f, 0.60f, OldBossHot, 0.09f);
            AddMarker(e.Pos, e.Radius, 0.30f, new Color(1f, 0.55f, 0.25f, 1f));
            NightLighting.Illuminate(new Vector3(e.Pos.x, 1.2f, e.Pos.y), OldBossHot, e.Radius * 2f, 9f, 0.35f);
            Shake(0.16f, 0.30f);
        }

        /// <summary>飞蛾弹幕：只给一次枪口闪光（每 2~3 秒一次，不能重）。</summary>
        private void OnMothBarrage(MothBarrageEvent e)
        {
            NightLighting.Illuminate(new Vector3(e.Pos.x, 1.0f, e.Pos.y), MothRose, 3.6f, 1.8f, 0.16f);
        }

        /// <summary>飞蛾冲刺：起冲（Impact=true）给一圈音爆环，预警段给一盏灯。</summary>
        private void OnMothDash(MothDashEvent e)
        {
            if (!e.Impact)
            {
                NightLighting.Illuminate(new Vector3(e.From.x, 0.8f, e.From.y), MothRose, 3f, 1.4f, 0.30f);
                return;
            }
            SpawnBossRing(e.From, 0.3f, 1.8f, 0.28f, MothRose, 0.10f);
            NightLighting.Illuminate(new Vector3(e.From.x, 0.9f, e.From.y), MothRose, 4f, 2.4f, 0.18f);
        }

        /// <summary>蛹的激光：预警 = 落点红圈；落下 = **从天上打下来的光柱** + 灼烧区。</summary>
        private void OnMothLaser(MothLaserEvent e)
        {
            if (e.Stage == 0)
            {
                AddRingMarker(e.Pos, e.Radius, 1.10f, new Color(1f, 0.35f, 0.30f, 1f));
                NightLighting.Illuminate(new Vector3(e.Pos.x, 0.8f, e.Pos.y),
                    new Color(1f, 0.40f, 0.30f), e.Radius * 2.2f, 2.2f, 0.55f);
                return;
            }
            // 落下：一根细长的柱（半径 = 判定半径的 1/3，视觉上更锐）+ 底部冲击环 + 灼烧地痕
            Material beam = VfxMat(MothCold, new Color(1f, 0.95f, 1f, 1f));
            GameObject go = Prim(PrimitiveType.Cylinder, "MothLaserBeam",
                new Vector3(e.Pos.x, 4.2f, e.Pos.y),
                new Vector3(e.Radius * 0.42f, 4.2f, e.Radius * 0.42f), beam);
            _bossRings.Add(new BossRing { Go = go, Mat = beam, T = 0f, Total = 0.20f, R0 = 0f, R1 = 0f });
            SpawnBossRing(e.Pos, e.Radius * 0.6f, e.Radius, 0.35f, new Color(1f, 0.92f, 0.75f, 1f), 0.11f);
            AddMarker(e.Pos, e.Radius, MothLaserBurnSecondsView, new Color(1f, 0.45f, 0.20f, 1f));
            NightLighting.Illuminate(new Vector3(e.Pos.x, 1.0f, e.Pos.y),
                new Color(1f, 0.70f, 0.45f), e.Radius * 2.6f, 8f, 0.40f);
            Shake(0.10f, 0.20f);
        }

        /// <summary>与 `BossEnemy.PupaLaserBurnSeconds` 同值（表现侧只读，不参与判定）。</summary>
        private const float MothLaserBurnSecondsView = 3.0f;

        /// <summary>飞蛾消失 / 现身：消失 = 一圈向内塌陷 + 光被抽走；现身 = 反过来的爆开。</summary>
        private void OnMothVanish(MothVanishEvent e)
        {
            if (e.Hidden)
            {
                SpawnBossRing(e.Pos, 2.4f, 0.25f, 0.40f, MothRose, 0.12f);
                NightLighting.Illuminate(new Vector3(e.Pos.x, 1.0f, e.Pos.y),
                    new Color(0.45f, 0.25f, 0.55f), 4f, 1.2f, 0.45f);
                return;
            }
            SpawnBossRing(e.Pos, 0.3f, 3.0f, 0.45f, MothCold, 0.12f);
            NightLighting.Illuminate(new Vector3(e.Pos.x, 1.0f, e.Pos.y), MothRose, 5f, 3.2f, 0.40f);
        }

        /// <summary>飞蛾跳砸：落点预警圈（时长 = 前摇）+ 落地冲击环。</summary>
        private void OnMothSlam(MothSlamEvent e)
        {
            AddRingMarker(e.To, e.Radius, e.Duration, new Color(1f, 0.45f, 0.75f, 1f));
            NightLighting.Illuminate(new Vector3(e.To.x, 0.8f, e.To.y),
                new Color(1f, 0.50f, 0.80f), e.Radius * 2f, 2.4f, 0.40f);
        }

        /// <summary>飞蛾形态切换：一大圈紫环 + 强光（"它变了"必须一眼看出来）。</summary>
        private void OnMothForm(MothFormEvent e)
        {
            SpawnBossRing(e.Pos, 0.5f, 5.0f, 0.70f, MothRose, 0.14f);
            SpawnBossRing(e.Pos, 0.3f, 3.2f, 0.55f, MothCold, 0.10f);
            NightLighting.Illuminate(new Vector3(e.Pos.x, 1.4f, e.Pos.y), MothRose, 8f, 6f, 0.55f);
            Shake(0.08f, 0.18f);
        }

        private void OnBoss(BossEvent e)
        {
            if (e.Kind == "clear_field") AddMarker(e.Pos, 10f, 0.4f, new Color(0.7f, 0.3f, 1f, 1f));
            else if (e.Kind == "summon") AddMarker(e.Pos, 3f, 0.3f, new Color(0.9f, 0.4f, 1f, 1f));
            // ★ v177：Boss 技能也照亮（需求③）★
            // clear_field 是全场清空的大招，给一盏**大范围紫光**；summon 是召唤，只给近场。
            bool big = e.Kind == "clear_field";
            NightLighting.Illuminate(new Vector3(e.Pos.x, 1.0f, e.Pos.y),
                new Color(0.72f, 0.36f, 1f),
                big ? 14f : 5f,
                big ? 7f : 3f,
                big ? 0.45f : 0.3f);
        }

        private void RemoveStale<T>(Dictionary<int, GameObject> map, List<T> live, System.Func<T, int> idOf)
        {
            var alive = new HashSet<int>();
            for (int i = 0; i < live.Count; i++) alive.Add(idOf(live[i]));
            _scratch.Clear();
            foreach (var kv in map)
                if (!alive.Contains(kv.Key)) _scratch.Add(kv.Key);
            for (int i = 0; i < _scratch.Count; i++)
            {
                int id = _scratch[i];
                if (map.TryGetValue(id, out GameObject go))
                {
                    if (go != null) Object.Destroy(go);
                    map.Remove(id);
                }
            }
        }
    }

    // ★ v191·fx3 的 `ScreenHealEvent` **不再需要**：`Combat.cs` 的 `MinuteHealEvent` 已经落地，
    //   本文件改成订阅它（`Bus.Subscribe<MinuteHealEvent>(OnMinuteHeal)`），
    //   于是 `RunSession.cs` **一行都不用改** ✓
}
