using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seedfall
{
    /// <summary>
    /// ★ 玩家脚下的**方向指示器**（v255：**挂成玩家模型的子物体** + 主城/战斗**统一一套数值**）★
    ///
    /// 用户原话："将主城和战斗中的方向指示统一化，挂载到玩家模型下做子物体，确保实时统一"。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、结构：一个**不渲染的 DDOL 宿主** + 一组**挂在玩家模型根下的渲染物**
    /// ══════════════════════════════════════════════════════════════════════
    /// ```
    /// [DontDestroyOnLoad] PLAYER_FACING_MARKER   ← 只有本组件，没有 Renderer（跨场景活着）
    ///    └ (平时没有子物体：渲染物被 SetParent 到玩家根下)
    /// 玩家模型根 (TOWN/TownPlayer ｜ MDL_farmer ｜ 兜底胶囊 Player)
    ///    └ PlayerFacingRing   ← 环带 + 8 个刻度点（一份网格）
    ///         └ PlayerFacingDot ← 方向点（自己一份网格）
    /// ```
    /// 为什么宿主必须与渲染物**分开**（这是本轮唯一的结构性决定，理由只有一条）：
    ///   主城与战斗是**两个真实场景**（`Town.unity` / `SeedfallArena.unity`，切换走
    ///   `SceneManager.LoadScene`：`TownBootstrap.cs:560`、`GameBootstrap.cs:212`），
    ///   而本组件靠 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 自举 —— 它**只在启动时跑一次**。
    ///   ⇒ 若把组件本身挂到玩家模型下，换场景时它会随玩家一起被销毁，**再也没有人重建它** ✗
    ///   ⇒ 所以：组件留在 DDOL 宿主上（`Instances` 恒为 1），**渲染物挂到玩家下**（父子关系负责跟随）。
    ///   换场景 ⇒ 渲染物随旧玩家销毁 ⇒ 本文件在下一次 `Acquire` 里用**缓存的网格/材质**重建一份
    ///   （`MeshBuilds` / `MaterialBuilds` 不涨，见第四节的读数说明）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、"实时统一"是怎么获得的：**父子关系，不是低通滤波**
    /// ══════════════════════════════════════════════════════════════════════
    /// v254 的写法是自建一个世界空间的宿主，每帧读玩家位置/朝向、按 τ=0.045s 做一阶低通去"追"。
    /// 那条路有两个必然的代价：① 永远差 45ms（转向时方向点会"甩"一下）；② 每帧 1 次 position +
    /// 1 次 rotation 写入。现在 `SetParent` 一次，位置与朝向由 Transform 层级在**同一帧**给到
    /// ⇒ **每帧 0 次 position / 0 次 rotation 写入**，滞后为 0 ✓
    ///
    /// 两条附带结论（都是父子关系的直接结果，写清楚以免以后当成 bug 再改回去）：
    ///   · **环与刻度点现在随模型一起转**。v254 是"世界固定的表盘 + 方向点在环上移动"；
    ///     挂到模型下之后，父链已经带着模型 yaw ⇒ 环带（一个正圆）看起来完全一样，
    ///     8 个刻度点（每 45°）在静止时也与"世界固定"完全一致，只有**转身过程**中跟着转。
    ///     这是"每帧 0 次 rotation 写入"的代价，也是"挂在模型下"的题中之义 —— 用户要的正是这个
    ///     （"确保实时统一"）✓
    ///   · **方向点自身的 localRotation 恒为 identity**（挂接时写一次）：它的网格圆心本来就建在
    ///     本地 +Z 的环中线上 ⇒ 父链带着模型 yaw 时，它就在模型的**正前方**。
    ///     这正是 v254 里 `Euler(0,yaw,0)` 想达到的位置，只是现在由父链给出、一分不差 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、★ 防误伤：为什么这次不会被重贴成"一片米色石地"★
    /// ══════════════════════════════════════════════════════════════════════
    /// 旧版 `FacingRing`（`TOWN/TownPlayer/FacingRing`，见 `PlayerBean.cs:338-357`）被删的直接原因：
    /// `TownArtDresser.RetextureTownSurface` 的"**又大又扁又贴地 ⇒ 地面类**"归纳
    /// （`TownArtDresser.cs:529-531`：`span > 1.2m && flatness < 0.30 && b.max.y < _groundY+0.8`）
    /// 把它当成了路面，换成不透明石地材质 ⇒ 屏幕上就是"一片米色薄片"✗
    ///
    /// **今天那条流程已经有护栏了**（v220 加的，与旧版删除同一轮）：
    /// `TownArtDresser.UnderCharacter`（`TownArtDresser.cs:571-583`）在 `:515` 被调用，
    /// 判据是"**从渲染器往上走 6 层，任一层的名字以这些前缀开头就跳过**"：
    /// `TownPlayer` / `ANIM_` / `FacingRing` / `MODEL_` / `MDL_`。
    /// 本轮**三道**独立的防线（任何一道单独成立就够）：
    ///   ① **渲染物自己的名字** `PlayerFacingRing` / `PlayerFacingDot` ⇒ 命中新加进白名单的
    ///      `PlayerFacing` 前缀（`TownArtDresser.cs:577-579`，本文件同轮改动，只加了两个 `StartsWith`）；
    ///   ② **祖先链**：`PlayerFacingDot → PlayerFacingRing → MDL_farmer`（或 `TownPlayer`）
    ///      ⇒ 第 2~3 层就命中 `MDL_` / `TownPlayer`，远在 6 层之内 ✓
    ///   ③ 名字一旦被改到都不像，还有一道**几何上不会命中**的兜底：环带是 `0.55m` 外径、
    ///      只有 `341` 个顶点、`size.y = 0`（纯平面），但它的 `span` 是 **1.10m < 1.2m**
    ///      ⇒ `floorLike` 的第一项就不过。**注意这条不能当保证**：改大外径（>1.2m）就会失效 ✗
    /// 另外两个可能改材质的地方都**够不到**它（逐个查过）：
    ///   · `RetextureExistingEnvs`（`:792-822`）只遍历 `TOWN_Props` 下名字以 `ENV_` 开头的子物体；
    ///   · `EnvRaw`（`:2475-2524`）只处理它自己 `Instantiate` 出来的 `DEC_*`。
    /// ⇒ 这是**名字白名单**（两道名字 + 一道祖先链），不是组件/tag/layer 判据 ——
    ///   因为 dresser **只支持名字判据**（`UnderCharacter` 里没有任何组件/tag/layer 检查）。
    ///   **若以后把这两个物件改名且不再以 `PlayerFacing` 开头，防线 ①② 同时失效**，
    ///   只剩第三节末尾那条不牢靠的尺寸兜底 ⇒ 改名时请同步改 `UnderCharacter` 的白名单 ✗
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、造型（与 v254 完全一致：环形 + 方向点 + 刻度点，全部程序化三角形、无贴图）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 贴地高度 `LiftY = 0.025m`（相对玩家根；路面/耕地上表面在 y ≈ 0.02 ⇒ 留 5mm 不 z-fighting）
    ///   · **环**：内径 `0.42m`、外径 `0.55m`（线宽 0.13m）、`64` 段 ⇒ 256 顶点 / 384 索引
    ///   · **方向点**：半径 `0.075m` 的实心圆（`12` 段扇形），圆心落在环中线 `0.485m` 上、本地 +Z
    ///   · **刻度点**：每 `45°` 一个更小更暗的小圆（半径 `0.03m`、`8` 段），共 8 个
    ///   · 顶点总数 `341`，一次建好，**不随帧改几何**
    ///   · 材质 `Sprites/Default`（本工程已验证：血渍就是它 + 顶点色），`renderQueue = 3000`
    ///   · **无 Collider**（本文件从不 AddComponent 碰撞体 —— 挂在玩家下面尤其重要：
    ///     玩家的移动用 `TownAvatar.MoveWithCollision` 的胶囊扫掠，多一个碰撞体就会把自己挡住）
    ///   · `shadowCastingMode = Off`、`receiveShadows = false`（贴地的一片：不投影也不吃阴影）
    ///   · 环与方向点**共用一份材质**（差别全在顶点色）⇒ 可动态合批，`MaterialBuilds` 只 +1；
    ///     前后关系用 `Renderer.sortingOrder`（环 0 / 点 1）固定
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、颜色从玩家身上**推**出来（不写死 RGB；主城与战斗走**同一条路**）
    /// ══════════════════════════════════════════════════════════════════════
    /// 取玩家所有渲染器材质里**彩度最高**（max−min 最大）的那一支当"玩家主色"，再按 HSV 派生：
    ///   · `sat = clamp01(s × 1.35 + 0.12)`（提饱和，保证在地面读得出）
    ///   · 环   = `HSVToRGB(h, sat, 0.85)`，α `0.36`
    ///   · 方向点 = `HSVToRGB(h, sat, 1.00)`，α `0.92`  ← 同色相、v 更高、更不透明 = "更亮"
    ///   · 刻度点 = `HSVToRGB(h, sat, 0.70)`，α `0.30`  ← 同色相、更暗更淡 = "更退后"
    /// **主城档 / 战斗档已删除**（v254 是 `RingAlpha/BattleAlphaGain/IsTownChain` 两套）：
    /// 现在只有这一套数，两种状态逐位相同 ⇒ 不需要"我在哪个场景"这个判据 ✓
    /// 一支材质色都取不到（例如玩家还没建好）⇒ 落"无色相白"兜底，并在读数里标出来 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 六、性能与读数
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 宿主对象**一个**（`Instances` 恒为 1）；网格 2 份、材质 1 份，**建一次**（静态缓存）
    ///   · 每帧写入：`0` 次 position / `0` 次 rotation / `0` 次 localScale（详见 `LateUpdate` 末尾的清单）
    ///   · 颜色重算：只在挂接时 + 每 `ColorRefreshSeconds = 1.5s` 一次（换皮最迟 1.5 秒跟上），
    ///     读的是挂接时缓存好的 `Renderer[]`（不每帧 `GetComponentsInChildren`）
    ///   · 顶点色写回用 `_ringCols` / `_dotCols` 两个定长数组 ⇒ 稳态 0 次分配
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFacingMarker : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════
        // 几何（米）—— 改这几个数就能改大小 / 形状
        // ══════════════════════════════════════════════════════════════════
        /// <summary>贴地高度（米，相对玩家根）。路面/耕地上表面 y ≈ 0.02 ⇒ 留 5mm 防 z-fighting。</summary>
        public const float LiftY = 0.025f;

        /// <summary>环外径（米）。★ 别加到 1.2 以上：那是"地面类"归纳的尺度门槛（见类注释第三节）★</summary>
        public const float RingOuterR = 0.55f;
        /// <summary>环内径（米）。</summary>
        public const float RingInnerR = 0.42f;
        /// <summary>环线宽（米）= 外径 − 内径 = 0.13m。</summary>
        public const float RingWidthM = RingOuterR - RingInnerR;
        /// <summary>环的中线半径（米）= (外径 + 内径) / 2 = 0.485m ⇒ 方向点与刻度点都落在这条线上。</summary>
        public const float RingMidR = (RingOuterR + RingInnerR) * 0.5f;
        /// <summary>环的段数（需求 48~64）。64 段 ⇒ 每段圆心角 5.6°，肉眼看不出折线。</summary>
        public const int RingSegments = 64;

        /// <summary>方向点半径（米）。</summary>
        public const float DotRadiusM = 0.075f;
        /// <summary>方向点的扇形段数（需求给的 12 段）。</summary>
        public const int DotSegments = 12;
        /// <summary>方向点在环之上再抬的高度（米，局部）—— 配合 sortingOrder 保证"永远骑在环上"。</summary>
        public const float DotLiftM = 0.002f;

        /// <summary>刻度点个数（每 45° 一个 ⇒ 8 个）。</summary>
        public const int TickCount = 8;
        /// <summary>相邻刻度点的角度间隔（度）。</summary>
        public const float TickAngleDeg = 45f;
        /// <summary>刻度点半径（米）—— 比方向点更小。</summary>
        public const float TickRadiusM = 0.03f;
        /// <summary>刻度点的扇形段数（小圆，8 段足够圆）。</summary>
        public const int TickSegments = 8;

        // ── 顶点预算（全部是常数 ⇒ 可在读数里直接核对）──
        /// <summary>环带顶点数 = 64 段 × 4。</summary>
        public const int RingBandVertexCount = RingSegments * 4;
        /// <summary>刻度点顶点数 = 8 个 × (8 段 + 1 圆心)。</summary>
        public const int TickVertexCount = TickCount * (TickSegments + 1);
        /// <summary>「环」这一份网格的顶点数（环带 + 刻度点，共用一份网格）。</summary>
        public const int RingVertexCount = RingBandVertexCount + TickVertexCount;
        /// <summary>「方向点」这一份网格的顶点数 = 12 段 + 1 圆心。</summary>
        public const int DotVertexCount = DotSegments + 1;
        /// <summary>两份网格的顶点总数 = 341（需求上限 1500）。</summary>
        public const int TotalVertexCount = RingVertexCount + DotVertexCount;

        // ══════════════════════════════════════════════════════════════════
        // 视觉（★ 一套数值：主城与战斗逐位相同 ★）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>环的 α。取 v254 主城 0.30 与战斗 0.42 的中间值 —— 见 `PlayerFacingMarker` 类注释第五节。</summary>
        public const float RingAlpha = 0.36f;
        /// <summary>方向点的 α。取主城 0.85 与战斗 1.00 的中间偏上（它是唯一"必须一眼可读"的元素）。</summary>
        public const float DotAlpha = 0.92f;
        /// <summary>刻度点的 α。取主城 0.25 与战斗 0.35 的中间值。</summary>
        public const float TickAlpha = 0.30f;

        /// <summary>环的明度 v（同色相的基准亮度）。</summary>
        public const float RingValue = 0.85f;
        /// <summary>方向点的明度 v（&gt; 环的 v ⇒ "更亮"，这是造型要求里的"点比环亮"）。</summary>
        public const float DotValue = 1.00f;
        /// <summary>刻度点的明度 v（&lt; 环的 v ⇒ "更暗"）。</summary>
        public const float TickValue = 0.70f;

        /// <summary>透明队列：3000 = Unity 透明队列起点（不透明之后）。</summary>
        public const int TransparentQueue = 3000;

        // ══════════════════════════════════════════════════════════════════
        // 跟随 / 重建
        // ══════════════════════════════════════════════════════════════════
        /// <summary>颜色重算周期（秒）。换皮（`TownSkins`）之后最多 1.5 秒跟上来。</summary>
        public const float ColorRefreshSeconds = 1.5f;
        /// <summary>找不到玩家时的重试周期（秒）—— 避免每帧 `GameObject.Find`。</summary>
        public const float AcquireRetrySeconds = 0.5f;
        /// <summary>
        /// 玩家根**世界缩放**的相对变化容差。战斗里受击脉动会改玩家根的 `localScale`
        /// （`ModelView.ApplyScale`，`ModelView.cs:795`）⇒ 不补偿的话环会跟着涨落 ±几个百分点。
        /// 超过这个容差才补一次（稳态 0 次写入）。
        /// </summary>
        public const float ScaleDriftTolerance = 0.01f;

        /// <summary>
        /// 渲染物的名字。★ 必须以 `PlayerFacing` 开头 ★
        /// —— `TownArtDresser.UnderCharacter`（`TownArtDresser.cs:571-583`）按名字前缀跳过角色部件，
        ///    这两个名字是本轮加进它白名单的唯一凭据（类注释第三节）。
        /// </summary>
        public const string VisName = "PlayerFacingRing";
        /// <summary>方向点的名字（同样以 `PlayerFacing` 开头，理由同上）。</summary>
        public const string DotName = "PlayerFacingDot";

        /// <summary>按顺序尝试的玩家根名字（`TownAvatar.cs:96` / `ModelView.cs:115` / `WorldView.cs:1470`）。</summary>
        private static readonly string[] PlayerNames = { "TownPlayer", "MDL_farmer", "Player" };

        // ══════════════════════════════════════════════════════════════════
        // 读数（探针 / 交付报告用；与 `GardenCursor` 同一套风格）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>当前存活的宿主实例数。**必须恒为 1**（渲染物不是宿主，不计数）。</summary>
        public static int Instances;
        /// <summary>渲染物**被建起来**的次数（首次挂接 1 次；每换一个场景再 +1）。</summary>
        public static int MarkerBuilds;
        /// <summary>`new Mesh` 的次数（只可能是 2 —— 网格是静态缓存，换场景复用）。</summary>
        public static int MeshBuilds;
        /// <summary>`new Material` 的次数（只可能是 1 —— 环与点共用一份，差别全在顶点色）。</summary>
        public static int MaterialBuilds;
        /// <summary>顶点色写回的次数（每 1.5 秒 + 每次挂接时才涨）。</summary>
        public static int ColorWrites;
        /// <summary>上一次找到的玩家名（"(无)" = 还没找到）。</summary>
        public static string LastTarget = "(无)";
        /// <summary>上一次挂上去的父物件名（(=未挂) = 还没挂上）—— 证明"是子物体"这条读数。</summary>
        public static string LastParent = "(未挂)";
        /// <summary>上一次采到的朝向（度，来自玩家根的 forward，**只读**，不参与任何写入）。</summary>
        public static float LastYawDeg;
        /// <summary>上一次渲染物的世界坐标（y = 玩家根 y + `LiftY`）。</summary>
        public static Vector3 LastPos;
        /// <summary>颜色是从哪个渲染器的材质推出来的（读数，便于自证"派生而非写死"）。</summary>
        public static string LastColorSource = "(未取值)";
        /// <summary>实际用到的着色器名（带 FALLBACK 后缀 = 退回 URP 不透明，形状仍对、只是硬边）。</summary>
        public static string LastShader = "(未建)";

        // ══════════════════════════════════════════════════════════════════
        // 共享资源（**全场一份**：一个玩家、一个指示器）
        // ══════════════════════════════════════════════════════════════════
        private static PlayerFacingMarker _instance;
        private static Mesh _ringMesh;
        private static Mesh _dotMesh;
        private static Material _markerMat;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // ── 实例状态 ──
        private GameObject _vis;         // 环（渲染物根，**父级 = 玩家模型根**）
        private GameObject _dotGo;       // 方向点（`_vis` 的子物体）
        private Color[] _ringCols;       // RingVertexCount 个顶点（环带 + 刻度）
        private Color[] _dotCols;        // DotVertexCount 个顶点

        private Transform _target;             // 玩家模型根
        private Renderer[] _targetRenderers;   // 挂接时缓存一次，颜色重算复用它（**不含**本指示器的渲染器）
        private Vector3 _visScale;             // 上次补偿用的"玩家根世界缩放"
        private float _colorTimer;
        private float _retryTimer;

        // ══════════════════════════════════════════════════════════════════
        // 自举
        // ══════════════════════════════════════════════════════════════════

        /// <summary>进 play 模式时自己起一个**不渲染的**宿主（与 `GfxLever` / `AttackVfx` 同一套自举方式）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("PLAYER_FACING_MARKER");
            _instance = go.AddComponent<PlayerFacingMarker>();
        }

        private void Awake()
        {
            // 宿主必须跨场景活着（换场景时渲染物随旧玩家销毁，由本组件重建，见类注释第一节）
            DontDestroyOnLoad(gameObject);
            Instances++;
        }

        private void OnDestroy()
        {
            Instances--;
            if (_instance == this) _instance = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // 每帧：只读 + 低频写入（**唯一会写的 transform 属性是"缩放漂移补偿"**）
        // ══════════════════════════════════════════════════════════════════
        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            if (_target == null && !Acquire(dt))
            {
                SetVisible(false);
                return;
            }
            if (_target == null) { SetVisible(false); return; }

            // 兜底：玩家还活着但渲染物没了（被别处 Destroy / 场景切了一半）⇒ 重建 + 重新挂
            if (_vis == null) { BuildVisuals(); Attach(_target); }
            if (_vis == null) return;

            // 玩家被关掉（死亡 / 切场景的中间帧）⇒ 指示器跟着消失，不留一片"幽灵环"在地上
            if (!_target.gameObject.activeInHierarchy) { SetVisible(false); return; }
            SetVisible(true);

            // ① 缩放漂移补偿：只有玩家根的世界缩放变了（受击脉动 ±几个百分点）才写一次
            SyncScale();

            // ② 只读：朝向与位置进读数（**不写任何 transform** —— 跟随由父子关系负责）
            Vector3 f = _target.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-6f) LastYawDeg = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            LastPos = _vis.transform.position;

            // ③ 颜色：每 1.5 秒一次（换皮之后跟上来）
            _colorTimer -= dt;
            if (_colorTimer <= 0f)
            {
                _colorTimer = ColorRefreshSeconds;
                RefreshColors();
            }
        }

        /// <summary>
        /// 每帧的写入清单（交付时按这个核对）：
        ///   · `0` 次 position / `0` 次 rotation —— `SetParent` 一次之后由父子关系天然同步 ✓
        ///   · `SetActive` 0~1 次（只在显隐状态变化时才真的写）
        ///   · `localScale` / `localPosition` **0 次**（除非 `SyncScale` 检测到玩家根缩放漂移 &gt; 1%）
        ///   · 顶点色 0 次（除非 `_colorTimer` 到期，1/1.5 秒一次）
        /// </summary>
        private void SyncScale()
        {
            Vector3 s = _target.lossyScale;
            if (Mathf.Abs(s.x - _visScale.x) <= _visScale.x * ScaleDriftTolerance
                && Mathf.Abs(s.y - _visScale.y) <= _visScale.y * ScaleDriftTolerance
                && Mathf.Abs(s.z - _visScale.z) <= _visScale.z * ScaleDriftTolerance) return;
            ApplyScale(s);
        }

        /// <summary>找玩家根（按名字，顺序见 `PlayerNames`）。找不到就按 `AcquireRetrySeconds` 重试。</summary>
        private bool Acquire(float dt)
        {
            _retryTimer -= dt;
            if (_retryTimer > 0f) return false;
            _retryTimer = AcquireRetrySeconds;

            for (int i = 0; i < PlayerNames.Length; i++)
            {
                GameObject go = GameObject.Find(PlayerNames[i]);
                if (go == null) continue;
                // ★ 找到玩家 ⇒ `SetParent` 一次（在 Attach 里）。之后不再每帧追 ✓
                Attach(go.transform);
                LastTarget = go.name;
                return true;
            }
            LastTarget = "(无)";
            LastParent = "(未挂)";      // 没挂在任何东西上时读数别留着上一个场景的父名
            return false;
        }

        /// <summary>
        /// ★ 挂接：`SetParent` 一 次，然后只把**尺度/贴地高度/躺平角**算对 ★
        /// 之后位置与朝向全部由父子关系给出；玩家换模型 / 换场景时 `_vis` 会随旧玩家销毁，
        /// `LateUpdate` 的下一次 `Acquire` 会重建并再挂一次。
        /// </summary>
        private void Attach(Transform target)
        {
            _target = target;
            if (_vis == null) BuildVisuals();          // 换场景的第一次挂接：渲染物已随旧玩家销毁
            if (_vis == null) return;
            _targetRenderers = CollectPlayerRenderers(target);   // 不含本指示器的两个 Renderer
            _colorTimer = 0f;                          // 换目标 ⇒ 立刻按新目标重算颜色

            Transform vt = _vis.transform;
            vt.SetParent(target, false);               // ★★ 唯一的一次挂接 ★★
            vt.localRotation = FlatLocalRotation(target);
            ApplyScale(target.lossyScale);             // 网格按"米"建的 ⇒ 抵掉玩家根的缩放
            // 方向点：网格圆心就在本地 +Z 的环中线上 ⇒ 父链已带模型 yaw，localRotation 恒 identity
            _dotGo.transform.localRotation = Quaternion.identity;
            _dotGo.transform.localPosition = new Vector3(0f, DotLiftM, 0f);

            LastParent = target.name;
            RefreshColors();
        }

        /// <summary>
        /// 让环在世界里**躺平**：父链若带俯仰/翻滚（导入模型的 `BaseRotation` 就是这种），
        /// 这里一次性抵掉，只保留世界 yaw。本工程三个候选根（`TownPlayer` / `MDL_farmer` /
        /// 兜底胶囊）都是纯 yaw（`ModelView.cs:106` 明确写了 `BaseRotation = identity`）⇒ identity。
        /// </summary>
        private static Quaternion FlatLocalRotation(Transform parent)
        {
            Quaternion pr = parent.rotation;
            Vector3 f = pr * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) return Quaternion.identity;   // 根朝正上/正下：别算出 NaN
            return Quaternion.Inverse(pr) * Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        /// <summary>
        /// 抵消玩家根的世界缩放：网格按"米"建模，而战斗里玩家根是 `CharScale × PlayerScale = 1.6302`
        /// （`WorldView.cs:1444`）⇒ 不在这一层补偿，场上的环会是 0.90 米宽而不是 0.55 米 ✗
        /// 补偿之后 `_vis` 的**局部坐标 = 世界米**，所以方向点的 `localPosition.y` 可以直接写 `DotLiftM`。
        /// </summary>
        private void ApplyScale(Vector3 worldScale)
        {
            _visScale = new Vector3(
                Mathf.Max(1e-4f, worldScale.x),
                Mathf.Max(1e-4f, worldScale.y),
                Mathf.Max(1e-4f, worldScale.z));
            _vis.transform.localScale = new Vector3(1f / _visScale.x, 1f / _visScale.y, 1f / _visScale.z);
            _vis.transform.localPosition = new Vector3(0f, LiftY / _visScale.y, 0f);
        }

        /// <summary>
        /// 建渲染物（环 + 方向点）。**网格与材质复用静态缓存** ⇒ 换场景重建时 `MeshBuilds` / `MaterialBuilds` 不涨。
        /// 建完先按"无色相白"填一次：万一第一帧就取不到玩家色，也是**看得见的环 + 点**，而不是一片透明 ✓
        /// </summary>
        private void BuildVisuals()
        {
            if (_ringCols == null) _ringCols = new Color[RingVertexCount];
            if (_dotCols == null) _dotCols = new Color[DotVertexCount];

            Material mat = MarkerMaterial();

            // 环 + 刻度点：一份网格，不单独转（它跟着父链走）
            _vis = AddPiece(VisName, transform, RingMesh(), mat, 0f, 0);
            // 方向点：自己一份网格 + 自己的 localRotation（挂接时写一次 identity）
            _dotGo = AddPiece(DotName, _vis.transform, DotMesh(), mat, DotLiftM, 1);

            Color ring = Color.HSVToRGB(0.12f, 0f, RingValue);
            Color dot = Color.HSVToRGB(0.12f, 0f, DotValue);
            Color tick = Color.HSVToRGB(0.12f, 0f, TickValue);
            ring.a = RingAlpha;
            dot.a = DotAlpha;
            tick.a = TickAlpha;
            WriteColors(ring, dot, tick);

            _vis.SetActive(false);   // 还没挂到玩家身上 ⇒ 别在世界原点亮一下
            MarkerBuilds++;
        }

        /// <summary>
        /// 一片贴地部件：位置 y、共享网格、共享材质、**不投影不吃阴影**（无任何 Collider）。
        /// `sortingOrder` 决定透明队列里的前后（环 0 / 方向点 1 ⇒ 点永远压在环上）。
        /// </summary>
        private static GameObject AddPiece(string name, Transform parent, Mesh mesh, Material mat,
                                           float extraY, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.identity;   // 顶点已躺在 XZ 平面
            go.transform.localPosition = new Vector3(0f, extraY, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            // 贴地的一片：**不投影**（否则地上糊出第二片）、**不吃阴影**（否则被压暗）
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = sortingOrder;
            return go;
        }

        private void SetVisible(bool on)
        {
            if (_vis == null) return;
            if (_vis.activeSelf != on) _vis.SetActive(on);
        }

        // ══════════════════════════════════════════════════════════════════
        // 网格（各建一次；顶点色之后由 WriteColors 复用缓冲区改写）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>极坐标点 ⇒ 环上一点（角度以本地 +Z 为 0、朝 +X 增大，与 `atan2(x,z)` 的 yaw 约定一致）。</summary>
        private static Vector3 RingPoint(float angleRad, float radius)
        {
            return new Vector3(Mathf.Sin(angleRad) * radius, 0f, Mathf.Cos(angleRad) * radius);
        }

        /// <summary>
        /// 「环」网格 = 环带（64 段四边形带，每段 4 顶点 / 2 三角）+ 8 个刻度小圆（每 45°）。
        /// 两者在**同一份网格**里 ⇒ 只有 1 次 DrawCall。
        /// </summary>
        private static Mesh RingMesh()
        {
            if (_ringMesh != null) return _ringMesh;

            var v = new Vector3[RingVertexCount];
            var tris = new int[RingSegments * 6 + TickCount * TickSegments * 3];
            int n = 0;

            // ── 环带：每段 4 顶点（内0 / 外0 / 外1 / 内1）──
            for (int i = 0; i < RingSegments; i++)
            {
                float a0 = (float)i / RingSegments * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / RingSegments * Mathf.PI * 2f;
                int b = i * 4;
                v[b + 0] = RingPoint(a0, RingInnerR);
                v[b + 1] = RingPoint(a0, RingOuterR);
                v[b + 2] = RingPoint(a1, RingOuterR);
                v[b + 3] = RingPoint(a1, RingInnerR);
            }
            for (int i = 0; i < RingSegments; i++)
            {
                int b = i * 4;
                tris[n++] = b + 0; tris[n++] = b + 1; tris[n++] = b + 2;
                tris[n++] = b + 0; tris[n++] = b + 2; tris[n++] = b + 3;
            }

            // ── 刻度点：每 45° 一个实心小圆，圆心落在环的中线上 ──
            for (int t = 0; t < TickCount; t++)
            {
                float a = t * TickAngleDeg * Mathf.Deg2Rad;
                Vector3 c = RingPoint(a, RingMidR);
                int b = RingBandVertexCount + t * (TickSegments + 1);
                v[b] = c;                                     // 圆心
                for (int k = 0; k < TickSegments; k++)
                {
                    float ak = (float)k / TickSegments * Mathf.PI * 2f;
                    v[b + 1 + k] = c + new Vector3(Mathf.Sin(ak), 0f, Mathf.Cos(ak)) * TickRadiusM;
                }
                for (int k = 0; k < TickSegments; k++)
                {
                    tris[n++] = b;
                    tris[n++] = b + 1 + k;
                    tris[n++] = b + 1 + (k + 1) % TickSegments;
                }
            }

            var m = BuildFlatMesh("PlayerFacingRing", v, tris);
            _ringMesh = m;
            return m;
        }

        /// <summary>
        /// 「方向点」网格 = 半径 0.075 的实心圆（12 段扇形，13 顶点 / 12 三角），
        /// 圆心落在本地 +Z 方向的环中线半径上 ⇒ 网格自己就指着"正前方"。
        /// 挂到玩家模型下之后，父链带着模型 yaw ⇒ 这个圆点**永远在模型正前方**，无需旋转 ✓
        /// </summary>
        private static Mesh DotMesh()
        {
            if (_dotMesh != null) return _dotMesh;

            var v = new Vector3[DotVertexCount];
            var tris = new int[DotSegments * 3];
            Vector3 c = new Vector3(0f, 0f, RingMidR);
            v[0] = c;
            for (int k = 0; k < DotSegments; k++)
            {
                float ak = (float)k / DotSegments * Mathf.PI * 2f;
                v[1 + k] = c + new Vector3(Mathf.Sin(ak), 0f, Mathf.Cos(ak)) * DotRadiusM;
            }
            int n = 0;
            for (int k = 0; k < DotSegments; k++)
            {
                tris[n++] = 0;
                tris[n++] = 1 + k;
                tris[n++] = 1 + (k + 1) % DotSegments;
            }

            var m = BuildFlatMesh("PlayerFacingDot", v, tris);
            _dotMesh = m;
            return m;
        }

        /// <summary>
        /// 一片贴地的网格：法线**写死朝上**（不 `RecalculateNormals` —— Sprites/Default 用不到，
        /// 而双面绕序会把相邻三角的法线互相抵消成零向量，见 `BloodStainMesh` 的同一条注记）；
        /// UV 全部取 (0.5,0.5)（采样 1×1 白图，颜色只来自顶点色）。
        /// </summary>
        private static Mesh BuildFlatMesh(string name, Vector3[] verts, int[] tris)
        {
            var uv = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            var colors = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                uv[i] = new Vector2(0.5f, 0.5f);
                normals[i] = Vector3.up;
                colors[i] = Color.white;
            }

            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uv);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            MeshBuilds++;
            return m;
        }

        // ══════════════════════════════════════════════════════════════════
        // 材质（建一份，环与方向点共用；差别全在顶点色里）
        // ══════════════════════════════════════════════════════════════════
        private static Material MarkerMaterial()
        {
            if (_markerMat != null) return _markerMat;
            _markerMat = BuildMat("M_PlayerFacingRing");
            return _markerMat;
        }

        private static Material BuildMat(string name)
        {
            Shader sh = Shader.Find("Sprites/Default");
            bool sprite = sh != null;
            if (!sprite)
            {
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Standard");
            }
            var m = new Material(sh) { name = sprite ? name : name + "FALLBACK" };
            // 颜色**只来自顶点色** ⇒ 材质色保持白（与 `FacingMat` 的同一条约定）
            m.mainTexture = Texture2D.whiteTexture;   // 采样 1×1 白图，不依赖"没贴图时采样返回什么"
            m.color = Color.white;
            // `Sprites/Default` 的顶点阶段还有一项 `_RendererColor`（由 `SpriteRenderer` 用 MPB 送进来）。
            // 本工程用 `MeshRenderer` 画它 ⇒ 这一项没人写。这里显式给白：
            // 它是材质上的属性（不在 Properties 里，`HasProperty` 查不到），写它**不会报错**、
            // 但一旦它是黑的，整片会被乘成黑 —— 这是"看得见的一行防御"，不是猜测 ✓
            m.SetColor("_RendererColor", Color.white);
            // 透明队列（不透明之后）：`Cloth` 之外的最后一档之前都是不透明，3000 起才是混合
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
            LastShader = m.shader != null ? m.shader.name : "(null)";
            MaterialBuilds++;
            return m;
        }

        // ══════════════════════════════════════════════════════════════════
        // 颜色：从玩家材质**推**出来（见类注释第五节）
        // ══════════════════════════════════════════════════════════════════
        private void RefreshColors()
        {
            string src;
            Color main;
            if (!TryDeriveMainColor(_targetRenderers, out main, out src))
            {
                // 取不到任何材质色 ⇒ 落"无色相白"兜底（保底可见），并在读数里标出来
                main = Color.white;
                src = "(取不到材质色 → 白)";
            }
            LastColorSource = src;

            Color.RGBToHSV(main, out float h, out float s, out float _);
            if (s < 0.08f) s = 0.55f;                   // 纯灰/纯黑的玩家配色：给一个保底彩度，色相仍保留
            float sat = Mathf.Clamp01(s * 1.35f + 0.12f);

            // 同一色相 / 同一彩度，只有 v 与 α 分三档：环（基准）< 刻度（更暗）< 方向点（更亮）
            Color ring = Color.HSVToRGB(h, sat, RingValue);
            Color dot = Color.HSVToRGB(h, sat, DotValue);
            Color tick = Color.HSVToRGB(h, sat, TickValue);

            // ★ 一套 α：主城与战斗**逐位相同**（v254 的"战斗档 ×1.4"已删除）★
            ring.a = Mathf.Clamp01(RingAlpha);
            dot.a = Mathf.Clamp01(DotAlpha);
            tick.a = Mathf.Clamp01(TickAlpha);

            WriteColors(ring, dot, tick);
        }

        /// <summary>
        /// 把三档颜色写进两个定长缓冲区再 `SetColors`（复用之 ⇒ 稳态 0 次分配）：
        /// 环网格的前 256 个顶点是环带（环色），后 72 个是刻度点（刻度色）。
        /// </summary>
        private void WriteColors(Color ring, Color dot, Color tick)
        {
            for (int i = 0; i < RingBandVertexCount; i++) _ringCols[i] = ring;
            for (int i = RingBandVertexCount; i < RingVertexCount; i++) _ringCols[i] = tick;
            for (int i = 0; i < DotVertexCount; i++) _dotCols[i] = dot;

            if (_ringMesh != null) _ringMesh.SetColors(_ringCols);
            if (_dotMesh != null) _dotMesh.SetColors(_dotCols);
            ColorWrites++;
        }

        /// <summary>
        /// 取玩家身上的渲染器，**排掉指示器自己的两个**。
        ///
        /// ★ 为什么必须排掉（挂成子物体之后这是个真 bug，不是洁癖）★
        /// 指示器的材质色是白（颜色全在顶点色里）⇒ 彩度 = 0。而 `TryDeriveMainColor` 的
        /// `bestChroma` 从 −1 起 ⇒ 白材质也会被选成"主色"（0 &gt; −1）⇒ 色相归零、
        /// 再被 `s &lt; 0.08` 的保底抬成 0.55 ⇒ **环会变成红色**，与玩家配色无关 ✗
        /// </summary>
        private Renderer[] CollectPlayerRenderers(Transform root)
        {
            Renderer[] all = root.GetComponentsInChildren<Renderer>(true);   // 只在挂接时分配
            Transform own = _vis != null ? _vis.transform : null;
            var keep = new List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                if (own != null && (r.transform == own || r.transform.IsChildOf(own))) continue;
                keep.Add(r);
            }
            return keep.ToArray();
        }

        /// <summary>
        /// 在玩家的所有材质里挑**彩度最高**（max−min 最大）的一支作为主色。
        /// 用 `sharedMaterial`（**不改**玩家材质，只读）；跳过近黑（v &lt; 0.08，眼珠那类）与近透明。
        /// </summary>
        private static bool TryDeriveMainColor(Renderer[] renderers, out Color main, out string source)
        {
            main = Color.white;
            source = "(无渲染器)";
            if (renderers == null || renderers.Length == 0) return false;

            float bestChroma = -1f;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                Material[] mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    Material m = mats[k];
                    if (m == null) continue;
                    Color c = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                            : m.HasProperty(ColorId) ? m.GetColor(ColorId)
                            : Color.white;
                    if (c.a < 0.5f) continue;                       // 透明片不参与
                    float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    if (max < 0.08f) continue;                      // 眼珠/描边那类近黑不参与
                    float chroma = max - min;
                    if (chroma > bestChroma)
                    {
                        bestChroma = chroma;
                        main = new Color(c.r, c.g, c.b, 1f);
                        source = r.name + " / " + m.name;
                        found = true;
                    }
                }
            }
            if (!found) source = "(渲染器里没有可用材质色)";
            return found;
        }

        /// <summary>一行读数（探针 / 截图叠字用）。</summary>
        public static string Status()
        {
            return $"target={LastTarget} parent={LastParent} yaw={LastYawDeg:0.0}° pos=({LastPos.x:0.00},{LastPos.y:0.000},{LastPos.z:0.00})" +
                   $" 主色来源={LastColorSource} shader={LastShader}" +
                   $"｜实例={Instances} 搭建={MarkerBuilds} Mesh={MeshBuilds} Mat={MaterialBuilds} 颜色写入={ColorWrites}" +
                   $" α=({RingAlpha:0.00}/{DotAlpha:0.00}/{TickAlpha:0.00}) 顶点={TotalVertexCount}";
        }
    }
}
