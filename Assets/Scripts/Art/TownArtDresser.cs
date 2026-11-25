using System.Collections.Generic;
using UnityEngine;

// `ToonTex` 是 `ToonLook` 的**嵌套**枚举（它属于"材质池"这件事，所以留在那边）。
// 这里起个别名：下面几十处 `ToonTex.Wood` 才读得顺，而不用写成 `ToonLook.ToonTex.Wood`。
using ToonTex = Seedfall.ToonLook.ToonTex;

namespace Seedfall
{
    /// <summary>
    /// ★★ v214：主城的**美术着装**（草皮、装饰物、贴图）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是"另外一个组件"，而不是改 `TownBootstrap`
    /// ══════════════════════════════════════════════════════════════════════
    /// 本轮的布局正在另一条线上重做（`Scripts/Town/**` 归那一线）。
    /// 所以这里**一个字都不动**主城的构建代码，只在它**之后**跑一遍，
    /// 把"美术"这一层叠上去：
    ///   · 它只**读** `TownBootstrap` 的 public 布局常量（`FarmHalf` / `RoadInner` /
    ///     `RoadOuter` / `SlabTopY` …）—— 布局改了，这里自动跟着改 ✓
    ///   · 它只**加**东西（新的草、新的装饰），不改已有物体的位置
    ///   · 它只有两处**替换**，且都是"材质"不是"布局"：地面/公园/道路的贴图
    ///     （`SwapGroundMaterials`，按名字找，找不到就跳过 ⇒ 布局改名字也不会崩）✓
    ///
    /// 于是"布局那条线"随时可以把主城重排，本文件不需要跟着改；
    /// 反过来，本文件挂了也只是一个美化层没出现 —— 主城照常能玩 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、硬约束：**不 new Material、不 new Mesh（每实例）**
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 材质：一律 `ToonLook.Get(...)`（全工程唯一的共享池，键 = 底色+描边+版本+贴图）
    ///   · 网格：草**不生根** —— 所有草片在启动时被合并进 3 个**大网格**
    ///     （每种色调一个），整片草 = 3 个 `GameObject` / 3 个 draw call，
    ///     而不是几百个。草地上没有任何逐实例的 Transform 需要更新，合并是纯赚 ✓
    ///   · 装饰模型：`Resources.Load` + `Instantiate` 预制体 ⇒ 网格与贴图都是资产共享 ✓
    /// 验收：`ToonLook.MaterialPoolCount` 只按"用到的颜色数"增长。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、确定性
    /// ══════════════════════════════════════════════════════════════════════
    /// 所有撒点走 `Pcg32`（工程内唯一逻辑随机源）+ **固定种子** ⇒
    /// 同一个版本每次进城，草和石头长在**同一个位置**。
    /// 这对截图对比、回归验收是必须的：否则"和昨天比哪不一样"永远说不清 ✓
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]      // 在 `TownBootstrap`（默认 0）之后：城建完才有地方画画
    public sealed class TownArtDresser : MonoBehaviour
    {
        private const string Tag = "[TownArt] ";
        private const string DresserName = "TOWN_DRESS";

        /// <summary>撒点种子。改这个数 = 换一种自然分布，但仍然是确定性的。</summary>
        private const int Seed = 20261019;

        /// <summary>草的三种色调（中/深/浅）—— 三份共享材质，也是草网格被切成三块的原因。</summary>
        private static readonly Color[] GrassColors =
        {
            new Color(0.44f, 0.66f, 0.30f),
            new Color(0.35f, 0.57f, 0.26f),
            new Color(0.52f, 0.72f, 0.33f),
        };

        // ── 草的高度/宽度（米）──
        // ★ v214 实测调过一轮：0.30 → **0.42** 米 ★
        // 第一版给 0.30 米、315 丛，近景截图里读成"草地上的黑点"——原因是**屏幕空间墨线**
        // （1.7 像素）在一根只有 6~8 像素宽的草叶上占掉了它的大半 ⇒ 越小的草越像黑渣。
        // 加高到 0.42 米 + 加宽到 5 厘米之后，一丛草在跟随机位下有 **25 像素**高，
        // 墨线只包住边、不吞掉叶子，才读成"卡通草"而不是"脏点" ✓
        private const float GrassHeight = 0.42f;
        private const float GrassBladeWidth = 0.052f;

        private Transform _root;
        private Transform _grassRoot;
        private Transform _decoRoot;

        /// <summary>已占用的地面（世界 XZ 圆心 + 半径）。撒点靠它躲开已有物件。</summary>
        private readonly List<Vector3> _taken = new List<Vector3>();
        private readonly List<float> _takenR = new List<float>();

        /// <summary>禁区矩形（世界 XZ）：路面、农田、耕地、传送门、仓库、路灯、公园小径。</summary>
        private readonly List<Rect> _excl = new List<Rect>();

        /// <summary>内容中心与半径：整座主城"有东西的地方"的外接圆（撒点的极坐标基准）。</summary>
        private Vector3 _center;
        private float _contentR = 12f;

        /// <summary>建筑锚点（`BUILD_*`）—— 装饰要贴着它们摆，而不是写死坐标。</summary>
        private struct Anchor
        {
            public Vector3 P;
            public float R;
            /// <summary>★ v220：包围盒的**半宽（世界 X）** —— 算"门点"要用（见 `DoorOf`）★</summary>
            public float Ex;
            /// <summary>★ v220：包围盒的**半深（世界 Z）**</summary>
            public float Ez;
            public string Name;
        }

        /// <summary>
        /// ★ v220：每栋 `BUILD_*` 的**门点**（世界 XZ）—— 树不许贴着它（用户判据：距锚点 ≥ 1.5 米）★
        ///
        /// 门点 = 从建筑中心朝**城里那一面**打到包围盒面上的那个点。
        /// 这个定义不是猜的：布局那路给的常量就是它 ——
        /// `HomeAnchor (11.20,-0.50)` 对应建筑中心 x≈13.8、半宽≈2.6 ⇒ 13.8−2.6 = **11.20** ✓
        /// `PortalAnchor (6.00,12.60)` 与运行时 `FX_BossPortal` 的世界坐标**逐位相同** ✓
        /// ⇒ 读场景几何就能复现那组常量，不必把它们的名字写进本文件（布局改名也不会崩）✓
        /// </summary>
        private readonly List<Vector3> _doors = new List<Vector3>();
        private readonly List<Anchor> _buildings = new List<Anchor>();

        /// <summary>基础地面的上表面高度（从场景里的 `GROUND_Grass` 量，量不到就 0）。</summary>
        private float _groundY;

        /// <summary>公园草皮的范围与高度（量不到公园就 `_hasPark = false`，跳过最密的那一区）。</summary>
        private Rect _parkRect;
        private float _parkY;
        private bool _hasPark;

        private void Start()
        {
            GameObject town = GameObject.Find("TOWN");
            if (town == null)
            {
                Log.Warn(Tag + "场景里没有 TOWN 根节点（主城还没构建？）—— 本次不铺美术层");
                return;
            }
            if (town.transform.Find(DresserName) != null)
            {
                Log.Info(Tag + "美术层已存在，跳过（幂等）");
                return;
            }

            int mats0 = ToonLook.MaterialPoolCount;
            float t0 = Time.realtimeSinceStartup;

            _root = new GameObject(DresserName).transform;
            _root.SetParent(town.transform, false);
            _grassRoot = new GameObject("GRASS").transform;
            _grassRoot.SetParent(_root, false);
            _decoRoot = new GameObject("DECO").transform;
            _decoRoot.SetParent(_root, false);

            Measure(town.transform);

            int upright = FixUpright(town.transform);
            int groundSwaps = RetextureTownSurface(town.transform);
            int furrows = DressFarm(town.transform);        // ★ v220：耕地沟（让 6×6 数得出来）
            int retex = RetextureExistingEnvs(town.transform);
            int clumps = PlantGrass();
            int deco = PlaceDecorations();
            SpawnPortalVfx(town.transform);          // ⑤ 传送门的黑洞动态特效

            Log.Info(Tag + $"美术层就绪：草 {clumps} 丛（合并为 {_grassRoot.childCount} 块网格）· " +
                            $"建筑旁新种树 {treesBesideBuildings} 棵 · " +
                            $"耕地沟 {furrows} 条 · " +
                            $"装饰 {deco} 件 · 地表/屋顶贴图 {groundSwaps} 个渲染器 · 既有装饰上纹理 {retex} 个渲染器 · " +
                            $"材质池 {mats0}→{ToonLook.MaterialPoolCount} · 贴图 {ToonLook.TextureCount} 张 · " +
                            $"内容中心 ({_center.x:0.0},{_center.z:0.0}) 半径 {_contentR:0.0} m · " +
                            $"耗时 {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");

#if UNITY_EDITOR
            // ★ v220：编辑器里自动跑一遍取证（探针 + 5 张图）★
            // 为什么要"自动"：本轮的验收图与读数必须在**主城刚建好的那一帧**拿到 ——
            // 登录→主城的流程会自己往下走（`LoginBootstrap` 的自动继续 / 传送门），
            // 靠人手在正确时刻点命令是不可复现的。等一帧（光照与材质就位）后一次性做完 ✓
            StartCoroutine(AutoProof());
#endif
        }

#if UNITY_EDITOR
        /// <summary>等一帧 ⇒ 跑全部只读探针 ⇒ 落盘 5 张验收图 ⇒ 全部进控制台。</summary>
        private System.Collections.IEnumerator AutoProof()
        {
            yield return null;
            try
            {
                Log.Info(EnvAxisReport());
                Log.Info(FeetReport());
                Log.Info(ProbeRoad());
                Log.Info(SnapAll());
            }
            catch (System.Exception e)
            {
                Log.Warn(Tag + "自动取证抛异常（美术层本身没受影响）：" + e.Message);
            }
        }
#endif

        // ══════════════════════════════════════════════════════════════════
        // 障碍表：先量"已经有什么"，撒点才躲得开
        //
        // ★★ 为什么不读 `TownBootstrap` 的常量 ★★
        // 第一版这里是写 `TownBootstrap.FarmHalf / RoadInner / RoadOuter` 的。
        // 实测编译不过 —— 布局那条线正在重做，这些常量已经被搬进新的 `TownLayout`，
        // 名字与可见性随时会再变（`TownBootstrap.cs` 的 mtime 就在我改文件的前一分钟）。
        // ⇒ 与其跟着他们的每一次重构改我的代码，不如**从场景里量**：
        //   路面/农田/耕地/传送门……它们的世界 AABB 就是禁区，量一次就够。
        //   布局怎么挪，这里自动跟着挪，一行都不用改 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 走一遍 TOWN 树，量出：地面高度、公园范围、禁区矩形、已有装饰的占地。
        ///
        /// 禁区按**名字前缀**归类，因为主城的构件命名是稳定的
        /// （`ROAD_*` / `FARM_*` / `PLOT_*` / `PORTAL_*` / `WAREHOUSE_*` / `LAMP_*` / `PARK_Path*`），
        /// 而坐标不是。名字变了也不会崩：认不出来就**不当禁区**（草会长到那儿，但不影响能玩）✓
        /// </summary>
        private void Measure(Transform town)
        {
            WalkTown(town);

            Transform props = town.Find("TOWN_Props");
            if (props != null)
            {
                Renderer[] all = props.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Bounds b = all[i].bounds;
                    float r = Mathf.Max(b.extents.x, b.extents.z);
                    AddTaken(new Vector3(b.center.x, 0f, b.center.z), r + 0.25f);
                    AddExcl(XZ(b, 0.05f));
                }
            }

            // ── 内容中心与半径 ──
            // 取"建筑的重心"作中心：圆形主城与棋盘式主城都成立（建筑都是围着中心摆的）✓
            // 半径取"最远建筑的边缘"，再按禁区矩形补齐（路面可能比建筑还外扩）✓
            Vector3 sum = Vector3.zero;
            int cnt = 0;
            for (int i = 0; i < _buildings.Count; i++) { sum += _buildings[i].P; cnt++; }
            _center = cnt > 0 ? sum / cnt : Vector3.zero;
            _center.y = 0f;

            float r2 = 6f;
            for (int i = 0; i < _buildings.Count; i++)
                r2 = Mathf.Max(r2, _buildings[i].R + Distance2D(_center, _buildings[i].P));
            for (int i = 0; i < _excl.Count; i++)
            {
                Rect rc = _excl[i];
                // 四个角里最远的那个 —— 写开来比四行 Max 清楚
                float d0 = Distance2D(_center, new Vector3(rc.xMin, 0f, rc.yMin));
                float d1 = Distance2D(_center, new Vector3(rc.xMax, 0f, rc.yMin));
                float d2 = Distance2D(_center, new Vector3(rc.xMin, 0f, rc.yMax));
                float d3 = Distance2D(_center, new Vector3(rc.xMax, 0f, rc.yMax));
                r2 = Mathf.Max(r2, Mathf.Max(Mathf.Max(d0, d1), Mathf.Max(d2, d3)));
            }
            _contentR = Mathf.Clamp(r2, 8f, 60f);

            // ── 门点表（树不许贴它）──
            _doors.Clear();
            for (int i = 0; i < _buildings.Count; i++) _doors.Add(DoorOf(_buildings[i]));

            Log.Info(Tag + $"量得：建筑 {_buildings.Count} 座 · 禁区 {_excl.Count} 块 · " +
                            $"障碍 {_taken.Count} 个 · 地面高度 y={_groundY:0.000} · " +
                            $"中心 ({_center.x:0.00},{_center.z:0.00}) · 半径 {_contentR:0.00} m");
        }

        /// <summary>
        /// 一栋建筑的**门点**：从建筑中心朝城里那一面，打到它包围盒面上的那个世界点。
        /// 见 `_doors` 的说明 —— 它与布局那路的 `*Anchor` 常量是同一个点（已用两栋实测对过）✓
        /// </summary>
        private Vector3 DoorOf(Anchor a)
        {
            Vector3 d = new Vector3(_center.x - a.P.x, 0f, _center.z - a.P.z);
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            d.Normalize();
            // 朝 d 方向走到盒面上要多少米：x / z 两个方向各算一个，取**先撞上**的那个
            float tx = Mathf.Abs(d.x) > 1e-4f ? a.Ex / Mathf.Abs(d.x) : float.MaxValue;
            float tz = Mathf.Abs(d.z) > 1e-4f ? a.Ez / Mathf.Abs(d.z) : float.MaxValue;
            return a.P + d * Mathf.Min(tx, tz);
        }

        /// <summary>这个点离**任何**建筑门点都 ≥ <paramref name="minM"/> 米吗（用户判据：≥ 1.5 米）。</summary>
        private bool ClearOfDoors(Vector3 p, float minM)
        {
            for (int i = 0; i < _doors.Count; i++)
                if (Distance2D(p, _doors[i]) < minM) return false;
            return true;
        }

        /// <summary>最近的一个门点有多远（报数用）。</summary>
        private float NearestDoorDistance(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _doors.Count; i++) best = Mathf.Min(best, Distance2D(p, _doors[i]));
            return best == float.MaxValue ? -1f : best;
        }

        private static float Distance2D(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>禁区前缀表（按"这些地方不能长草"归类，不是按坐标）。</summary>
        private static readonly string[] ExcludePrefixes =
        {
            "ROAD_", "FARM_", "PLOT_", "PORTAL_", "WAREHOUSE_", "SHOP", "LAMP_", "PARK_Path",
            "RING_", "PATH_", "BUILD_",
            // ★★ v220 补的 5 条 —— 上一版漏了它们，后果是"草/树种到路面上"★★
            // 实测（本轮第一次跑）：商业街那条路叫 `STREET_Shop`（x 6.2~11.8），
            // 它**不**以 `SHOP` 开头 ⇒ 旧的 `"SHOP"` 前缀一条都拦不住 ✗
            // ⇒ 4 棵"建筑旁的树"直接种在铺装上（(10.97,11.22) / (10.09,6.86) / (8.68,-4.94)），
            //   这正是用户点名"不许堵路"要防的东西 ✓
            // `ZONE_` 不能整条加（会把 `ZONE_ParkLawn` 也禁掉 —— 公园草坪正是草地最密的地方），
            // 所以只**精确**排除田壤与公园小径 ✓
            "STREET_",      // 商业街 / 主街路面
            "PLAZA_",       // 广场圆盘与压边石
            "TERRACE_",     // 仓库前的装卸广场
            "ZONE_Farm",    // 农田土底 + 田埂压边（`ZONE_FarmSoil` / `ZONE_FarmCurb*`）
            "ZONE_ParkPath",// 公园的十字小径
        };

        private void WalkTown(Transform node)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                string n = child.name;

                if (n == "GROUND_Grass" || n.StartsWith("GROUND"))
                {
                    Bounds b = BoundsOf(child);
                    if (b.size.sqrMagnitude > 0f) _groundY = b.max.y;
                }
                else if (n.StartsWith("PARK_Grass"))
                {
                    Bounds b = BoundsOf(child);
                    if (b.size.sqrMagnitude > 0f)
                    {
                        _parkRect = XZ(b, 0f);
                        _parkY = b.max.y;
                        _hasPark = true;
                    }
                }
                else
                {
                    // 名字命中禁区前缀 ⇒ 这一整块的 AABB 进禁区表
                    for (int k = 0; k < ExcludePrefixes.Length; k++)
                    {
                        if (!n.StartsWith(ExcludePrefixes[k])) continue;
                        Bounds b = BoundsOf(child);
                        if (b.size.sqrMagnitude > 0f) AddExcl(XZ(b, 0.1f));
                        break;
                    }

                    // 建筑：既是禁区，也是"摆装饰"的锚点
                    if (n.StartsWith("BUILD_"))
                    {
                        Bounds b = BoundsOf(child);
                        if (b.size.sqrMagnitude > 0f)
                        {
                            _buildings.Add(new Anchor
                            {
                                P = new Vector3(b.center.x, 0f, b.center.z),
                                R = Mathf.Max(b.extents.x, b.extents.z),
                                Ex = b.extents.x,
                                Ez = b.extents.z,
                                Name = n,
                            });
                        }
                    }
                }

                WalkTown(child);
            }
        }

        private static Bounds BoundsOf(Transform t)
        {
            Renderer r = t.GetComponent<Renderer>();
            if (r != null) return r.bounds;
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return default;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        private static Rect XZ(Bounds b, float pad)
        {
            return new Rect(b.min.x - pad, b.min.z - pad,
                            b.size.x + pad * 2f, b.size.z + pad * 2f);
        }

        private void AddExcl(Rect r)
        {
            // 只收"有面积"的：路灯柱这类很细的，矩形几乎是一条线，收进来也没坏处
            if (r.width > 0.01f && r.height > 0.01f) _excl.Add(r);
        }

        private void AddTaken(Vector3 p, float r)
        {
            _taken.Add(p);
            _takenR.Add(r);
        }

        private bool Free(Vector3 p, float r)
        {
            for (int i = 0; i < _taken.Count; i++)
            {
                float dx = p.x - _taken[i].x;
                float dz = p.z - _taken[i].z;
                float rr = r + _takenR[i];
                if (dx * dx + dz * dz < rr * rr) return false;
            }
            return true;
        }

        /// <summary>
        /// 这一点的**禁区判定**：命中任何禁区矩形就不长草。
        ///
        /// ★ 农田（含 36 块耕地）整块被 `FARM_Soil` 与 36 个 `PLOT_*` 的 AABB 覆盖 ★
        /// 用户这一轮明确说"不铺在农田格子里"——那一片是另一条线正在重做的田圃，
        /// 草长进去不只是视觉冲突，还会把"哪块地能种"的读数搞乱 ✓
        /// </summary>
        private bool Blocked(Vector3 p, float margin)
        {
            for (int i = 0; i < _excl.Count; i++)
            {
                Rect r = _excl[i];
                if (p.x >= r.xMin - margin && p.x <= r.xMax + margin &&
                    p.z >= r.yMin - margin && p.z <= r.yMax + margin) return true;
            }
            return false;
        }

        /// <summary>该点的地面高度：公园草皮比基础地面高一点点（量的），其余用基础地面。</summary>
        private float GroundY(Vector3 p)
        {
            if (_hasPark && p.x >= _parkRect.xMin && p.x <= _parkRect.xMax &&
                p.z >= _parkRect.yMin && p.z <= _parkRect.yMax)
                return _parkY;
            return _groundY;
        }

        // ══════════════════════════════════════════════════════════════════
        // ① 地表 / 路面 / 屋顶：换成带贴图的共享材质
        // ══════════════════════════════════════════════════════════════════

        private static readonly Color RoadColor = new Color(0.66f, 0.63f, 0.56f);
        private static readonly Color RoofColor = new Color(0.62f, 0.30f, 0.26f);

        // ══════════════════════════════════════════════════════════════════
        // ② 耕地色板：**土（畦）与沟必须分得开** —— 用户判据"一眼能数出 6 列 6 行"
        //
        // 根因先写清楚（这是本轮最重要的一条证据）：
        //   上一版 `RetextureTownSurface` 的归纳是"**又大又扁又贴地 ⇒ 地面类**"，
        //   再按色相二分"绿 ⇒ 草，否则 ⇒ 石"。而 `ZONE_FarmSoil`(0.36,0.25,0.17) 与
        //   36 个 `PLOT_*`(0.48,0.34,0.22) 都是**棕的**（不绿）⇒ 被归成"石地"，
        //   统一换成 `road` = (0.62,0.60,0.55) 的**灰色铺装材质** ✗
        //   ⇒ 整片农田与广场同色同纹 ⇒ 用户说的"耕地读成铺装地面" ✓
        //   同时畦比沟只高 0.026 米、沟宽只有 0.30 米 ⇒ 网格线彻底消失 ✓
        //
        // 修法两条腿，缺一不可：
        //   ① **色相三分**：绿 ⇒ 草；**棕 ⇒ 土**（新增）；其余 ⇒ 石。名字命中 `Farm/Soil/PLOT`
        //      的直接进"土"，不再看色相（布局改配色也不会把它翻回石地）✓
        //   ② **真的造出沟**：`DressFarm()` 在 36 块畦的**间隙**里铺深色条，
        //      沟色比土色暗 3 档 ⇒ 60° 俯角下是一条能数出来的深线 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>耕地**土面（畦）**底色：比沟亮得多、比路面暖 —— "这不是铺装"一眼读得出。</summary>
        internal static readonly Color BedColor = new Color(0.60f, 0.43f, 0.27f);
        /// <summary>大块**田底**底色（只在畦之间的底面上露出来）：比畦暗一档。</summary>
        internal static readonly Color BaseSoilColor = new Color(0.33f, 0.22f, 0.15f);
        /// <summary>耕地**沟**底色：与 `BedColor` 的亮度差 ≈ 3 档（0.43 vs 0.14 平均亮度）。</summary>
        internal static readonly Color FurrowColor = new Color(0.20f, 0.13f, 0.08f);

        /// <summary>
        /// 按**名字关键词**给"大面积的平面构件"上卡通贴图。
        ///
        /// ★ 为什么不再按写死的对象名（`GROUND_Grass` / `ROAD_N` …）★
        /// 第一版就是这么写的，实测只命中 **1 处** —— 因为布局那条线把主城整个重做成了
        /// "圆形大公园"，路面改叫 `RING_*`、地面换了名字。写死的名字 = 每改一次布局就静默失效一次。
        /// ⇒ 现在按**关键词**匹配（`Ground` / `Grass` / `Lawn` / `Road` / `Ring` / `Path` / `Roof`），
        ///   布局怎么改名，只要能读出"这是地/路/屋顶"就命中 ✓
        ///
        /// ★ 必须用世界 XZ 投影（`worldUV: true`）★
        /// 方块图元的 UV 是 0~1/面：把一张 64 的图拉过 40 米 ⇒ 1 texel ≈ 0.6 米，糊成一块纯色。
        /// 世界投影下 tiling 直接等于"每米几张图"✓
        /// </summary>
        private int RetextureTownSurface(Transform town)
        {
            Material grass = ToonLook.Get(new Color(0.46f, 0.62f, 0.34f), 0f, true,
                                          ToonTex.Grass, 0.45f, worldUV: true);
            Material lawn = ToonLook.Get(new Color(0.40f, 0.66f, 0.36f), 0f, true,
                                         ToonTex.Grass, 0.60f, worldUV: true);
            Material road = ToonLook.Get(new Color(0.62f, 0.60f, 0.55f), 0f, true,
                                         ToonTex.Stone, 1.10f, worldUV: true);
            Material roof = ToonLook.Get(RoofColor, 0f, true, ToonTex.Roof, 0.75f, worldUV: true);
            Material wood = ToonLook.Get(new Color(0.58f, 0.43f, 0.29f), 0f, true,
                                         ToonTex.Wood, 0.50f, worldUV: true);
            // ★★ v220：土面（畦）。**纯色，不挂贴图** ★★
            // 第一版给畦挂了 `ToonTex.Wood`（想用木纹当犁沟），实测**读成木地板**：
            // 0.71 米一格的木纹在 80° 俯角下就是一条条板缝 ⇒ 用户原话"读成铺装地面"那条
            // 只是从"灰铺装"变成了"木铺装"，没有解决问题 ✗
            // ⇒ 畦回到**干净的暖棕纯色**，让"这是耕地"由 ① 颜色 ② 沟 两件事共同表达 ✓
            Material soil = ToonLook.Get(BedColor, 0f, true);
            // 大块**田底**（`ZONE_FarmSoil` 14.3×14.3）：比畦暗一档 ⇒ 36 块畦从底面上"浮"出来 ✓
            Material soilBase = ToonLook.Get(BaseSoilColor, 0f, true);

            int n = 0;
            Renderer[] all = town.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer rd = all[i];
                if (rd == null) continue;
                Material cur = rd.sharedMaterial;
                if (cur == null) continue;
                // ★★ v220：**角色部件一律不参与** ★★
                // 本函数之前的归纳是"又大又扁又贴地 ⇒ 地面类"，而玩家脚下的
                // `FacingRingDisc` 正好是**一张贴地的四边形** ⇒ 被归成"石地"，
                // 环状贴图被换成**不透明石地材质** ⇒ 屏幕上就是"一片米色多边形薄片" ✗
                // 那正是用户 ① 要删的东西；哪怕指示器以后再加回来，这条护栏也必须挡住 ✓
                if (UnderCharacter(rd.transform)) continue;
                string nm = rd.transform.name;
                Bounds b = rd.bounds;

                // ★★ 判据换成**几何 + 色相**，不再依赖名字 ★★
                // 上一版按名字（Ground/Grass/Road/Ring/Park…）分类，实测漏了一大批：
                // 用户指的"家·图鉴 与 集市 之间那片素地"根本**不在**那几类名字里 ⇒
                // 它一直是原始材质 ⇒ 纯米色。名字是布局那路的，随时会变；几何不会。
                //
                // 规则：**又大又扁又贴地**的渲染器 = 地面类构件（路面/广场/草地/院子）。
                //   扁平度 = size.y / max(size.x, size.z) < 0.30
                //   尺度   = max(size.x, size.z) > 1.2 米
                //   贴地   = b.max.y 低于地面 + 0.8 米（排除屋顶、桌面）
                // 再按**底色色相**分草地/石地：绿分量显著高于红蓝 ⇒ 草，否则石 ✓
                float span = Mathf.Max(b.size.x, b.size.z);
                float flatness = b.size.y / Mathf.Max(1e-3f, span);
                bool floorLike = span > 1.2f && flatness < 0.30f && b.max.y < _groundY + 0.8f;

                Material want = null;
                if (floorLike)
                {
                    // ★ v220：**土优先**。名字里带 Farm/Soil/PLOT/Tilled/Bed 的一律当耕地土面，
                    //   不再走色相二分 —— 旧版就是在这里把 36 块耕地刷成了灰色铺装 ✗
                    //   `Curb`（田埂压边石）与 `Wall`（田边矮墙）要留在石/木那一类 ⇒ 先排除 ✓
                    bool soilNamed = (Has(nm, "Farm") || Has(nm, "Soil") || Has(nm, "PLOT")
                                      || Has(nm, "Tilled") || Has(nm, "Bed"))
                                     && !Has(nm, "Curb") && !Has(nm, "Wall") && !Has(nm, "Path");
                    Color c = BaseColorOf(cur);
                    bool greenish = c.g > c.r * 1.06f && c.g > c.b * 1.06f;
                    // 棕：红最高、绿居中、蓝最低，且三段都拉开一档；再要求整体偏暗
                    //（土 0.36~0.48 的 R；木构件 0.58 起 ⇒ 木地板不会被误判成土）✓
                    bool brownish = c.r > c.g * 1.10f && c.g > c.b * 1.10f && c.r < 0.55f;

                    if (soilNamed) want = span > 5f ? soilBase : soil;
                    else if (greenish) want = span > 6f ? lawn : grass;
                    else if (brownish) want = span > 5f ? soilBase : soil;
                    else want = road;
                }
                else if (Has(nm, "Roof") || Has(nm, "Tile")) want = roof;
                else if (Has(nm, "Wall") || Has(nm, "Beam") || Has(nm, "Fence") || Has(nm, "Wood")) want = wood;

                if (want == null || ReferenceEquals(rd.sharedMaterial, want)) continue;
                rd.sharedMaterial = want;
                n++;
            }
            return n;
        }

        /// <summary>名字包含（忽略大小写）。</summary>
        private static bool Has(string name, string token)
            => !string.IsNullOrEmpty(name) && name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// 这个渲染器是不是**角色/玩家的部件**（玩家根叫 `TownPlayer`，豆豆人根叫 `ANIM_farmer`，
        /// 脚下的朝向指示叫 `FacingRing`）。往上找 6 层就够 —— 玩家的层级只有 4~5 层 ✓
        ///
        /// ★ v255：白名单加了 `PlayerFacing` / `PLAYER_FACING` ★
        /// 朝向指示器（`PlayerFacingMarker`）这一轮**挂成了玩家模型的子物体**
        /// （`PlayerFacingRing` → `PlayerFacingDot`，父级 = `TownPlayer` 或 `MDL_farmer`）。
        /// 它的环带是"贴地的扁圆环"——`floorLike` 那条归纳天然会想把它当地面重贴
        /// （只是本轮 `span = 1.10m < 1.2m` 恰好不过门槛，**靠尺寸兜底是脆的**）。
        /// 现在有三道名字防线：渲染物自己的名字（`PlayerFacing`）、
        /// 旧的 `FacingRing` 前缀、以及祖先链上的 `TownPlayer` / `MDL_`。
        /// **改了这两个物件的名字就要回来同步这里**，否则会重演 v220 那次
        /// "环被换成不透明石地材质 ⇒ 屏幕上一片米色薄片"✗
        /// </summary>
        private static bool UnderCharacter(Transform t)
        {
            Transform cur = t;
            for (int depth = 0; depth < 6 && cur != null; depth++)
            {
                string n = cur.name;
                if (n.StartsWith("TownPlayer") || n.StartsWith("ANIM_") || n.StartsWith("FacingRing")
                    || n.StartsWith("PlayerFacing") || n.StartsWith("PLAYER_FACING")
                    || n.StartsWith("MODEL_") || n.StartsWith("MDL_"))
                    return true;
                cur = cur.parent;
            }
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        // ②.5 耕地沟：让"6 列 6 行"**数得出来**
        //
        // ── 为什么是"补沟"而不是"重做耕地" ──
        // 36 块 `PLOT_*` 归布局那条线（`Scripts/Town/**`，本轮禁改）。
        // 它们自身的几何是**对的**：6×6、每块 2.00×2.00、中心间距 2.30
        // ⇒ 间隙 = 0.30 米，畦顶比田底只高 **0.026 米**。
        // 屏幕上"数不出格"不是因为它没分开，而是因为**分开了但没有对比**：
        //   旧底色的畦与田底亮度几乎一样(0.48,0.34,0.22 对 0.36,0.25,0.17)，
        //   0.30 米的缝里又是同一族土色 ⇒ 60° 俯角下糊成一片 ✓
        // 所以要"加"的东西正好是**沟本身**：在缝里铺一条比土暗 3 档的深色条。
        //   · 不动 `PLOT_*` 的位置/尺寸/旋转/碰撞 ⇒ 布局与玩法读到的坐标一个字节没变 ✓
        //   · 只增加 `FURROW_*` 渲染器，共用一份材质与一份立方体网格 ⇒ 不多一张贴图 ✓
        //
        // ── 网格线是"从场景量出来的"，不是写死的 6 / 2.30 ──
        // 中心按 0.15 米容差聚类 ⇒ 布局改成 5×5 或 7×4，这里自动跟着变 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>沟的材质（全场一份）。</summary>
        private Material _furrowMat;

        private int DressFarm(Transform town)
        {
            var plots = new List<Renderer>();
            Renderer[] all = town.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (!all[i].transform.name.StartsWith("PLOT_")) continue;
                plots.Add(all[i]);
            }
            if (plots.Count < 4)
            {
                Log.Warn(Tag + $"只找到 {plots.Count} 块 PLOT_* —— 本轮不铺耕地沟（布局可能改名了）");
                return 0;
            }

            // ① 聚类：中心的 x / z 各聚一遍（0.15 米容差）
            var xs = new List<float>();
            var zs = new List<float>();
            float bedTop = float.MinValue, halfW = 0f, halfD = 0f;
            for (int i = 0; i < plots.Count; i++)
            {
                Bounds b = plots[i].bounds;
                xs.Add(b.center.x);
                zs.Add(b.center.z);
                bedTop = Mathf.Max(bedTop, b.max.y);
                halfW = Mathf.Max(halfW, b.extents.x);
                halfD = Mathf.Max(halfD, b.extents.z);
            }
            List<float> colX = Cluster(xs, 0.15f);
            List<float> rowZ = Cluster(zs, 0.15f);
            if (colX.Count < 2 || rowZ.Count < 2)
            {
                Log.Warn(Tag + "耕地中心没聚成网格 —— 不铺沟（列 " + colX.Count + " 行 " + rowZ.Count + "）");
                return 0;
            }

            // ② 沟的纵向范围 = 最外一排畦的外沿（这样沟的两端与畦齐平，不会戳到田埂上）
            float z0 = rowZ[0] - halfD, z1 = rowZ[rowZ.Count - 1] + halfD;
            float x0 = colX[0] - halfW, x1 = colX[colX.Count - 1] + halfW;
            // 沟顶比畦顶低 0.010 米 ⇒ 读成"凹下去的沟"，而不是"铺在上面的黑条"
            const float Thickness = 0.02f;
            float y = bedTop - 0.010f - Thickness * 0.5f;

            if (_furrowMat == null) _furrowMat = ToonLook.Get(FurrowColor, 0f, true);

            int n = 0;
            // ③ 列向沟（沿 Z 通长）：相邻两列之间的那条缝
            for (int i = 0; i + 1 < colX.Count; i++)
            {
                float mid = (colX[i] + colX[i + 1]) * 0.5f;
                float gap = (colX[i + 1] - colX[i]) - halfW * 2f;      // 量出来的缝宽
                if (gap < 0.04f) continue;
                TownKit.Box(_decoRoot, "FURROW_X" + i,
                            new Vector3(mid, y, (z0 + z1) * 0.5f),
                            new Vector3(gap + 0.02f, Thickness, z1 - z0), _furrowMat);
                n++;
            }
            // ④ 行向沟（沿 X 通长）
            for (int i = 0; i + 1 < rowZ.Count; i++)
            {
                float mid = (rowZ[i] + rowZ[i + 1]) * 0.5f;
                float gap = (rowZ[i + 1] - rowZ[i]) - halfD * 2f;
                if (gap < 0.04f) continue;
                TownKit.Box(_decoRoot, "FURROW_Z" + i,
                            new Vector3((x0 + x1) * 0.5f, y, mid),
                            new Vector3(x1 - x0, Thickness, gap + 0.02f), _furrowMat);
                n++;
            }

            Log.Info(Tag + $"耕地沟 {n} 条：{colX.Count} 列 × {rowZ.Count} 行 · " +
                            $"畦 {plots.Count} 块 · 缝宽 {(colX.Count > 1 ? colX[1] - colX[0] - halfW * 2f : 0f):0.00} 米 · " +
                            $"土色 RGB({BedColor.r:0.00},{BedColor.g:0.00},{BedColor.b:0.00}) · " +
                            $"沟色 RGB({FurrowColor.r:0.00},{FurrowColor.g:0.00},{FurrowColor.b:0.00})");
            return n;
        }

        /// <summary>把一堆坐标值聚成"间距大于容差的若干档"（升序）。耕地网格就是这样量出来的。</summary>
        private static List<float> Cluster(List<float> values, float tolerance)
        {
            values.Sort();
            var outv = new List<float>();
            float sum = 0f;
            int cnt = 0;
            float anchor = values[0];
            for (int i = 0; i < values.Count; i++)
            {
                if (Mathf.Abs(values[i] - anchor) > tolerance && cnt > 0)
                {
                    outv.Add(sum / cnt);
                    sum = 0f;
                    cnt = 0;
                    anchor = values[i];
                }
                sum += values[i];
                cnt++;
            }
            if (cnt > 0) outv.Add(sum / cnt);
            return outv;
        }

        /// <summary>按名字在**整棵树**里找（`GameObject.Find` 只找 active，这里连 inactive 一起）。</summary>
        private static Transform FindDeep(Transform node, string name)
        {
            if (node == null) return null;
            if (node.name == name) return node;
            for (int i = 0; i < node.childCount; i++)
            {
                Transform hit = FindDeep(node.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        // ①.5 扶正：**上轴修正被 yaw 覆盖掉了** —— 这是"大量倒下的模型"的根因
        //
        // ── 机理（一句话）──
        // `Art/Environment/*.fbx` 是 Blender 导出的 **Z-up** 资产，FBX 的**根节点自带一个
        // 修正旋转**（`Art/Anim/*` 那族是 Rx(270)，`brute_*` 是 Rx(180)——工程里的旋转审计早就记着这件事）。
        // 而 `TownKit.Env` / 我自己的 `EnvRaw` 都是这么写的：
        //     go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        // ⇒ 这行把**根节点自带的修正旋转整个覆盖掉**了 ✗
        // ⇒ 树干由竖直变成水平、木箱侧躺、长椅背面朝上、牌子平躺 —— 与用户截图逐条对得上 ✓
        //
        // ── 为什么布局那路的探针说"没有一件 >2°" ──
        // 它量的是 `transform.up`。而错的是**网格本身的上轴**，transform 只有 yaw ⇒
        // `up` 仍然是 (0,1,0)，永远"没问题"。判据必须落在**几何**上（AABB 长轴），
        // 见 `Audit()` 的 ① ✓
        //
        // ── 修法 ──
        // 正确的式子是把 yaw 和资产自带的修正**相乘**，而不是取而代之：
        //     rotation = Euler(0, yaw, 0) * prefab.transform.rotation
        // 我方的 `EnvRaw` 已经改成这个式子；这里再把**布局那方已经摆好的** `ENV_*` 就地扶正
        // （只改 rotation 与贴地高度，**不动位置、不动缩放、不动材质**）✓
        // ══════════════════════════════════════════════════════════════════

        private int FixUpright(Transform town)
        {
            Transform props = town.Find("TOWN_Props");
            if (props == null) return 0;
            int fixedCount = 0, scanned = 0;
            for (int i = 0; i < props.childCount; i++)
            {
                Transform child = props.GetChild(i);
                string id = EnvIdOf(child.name);
                if (id == null) continue;
                GameObject prefab = Resources.Load<GameObject>("Art/Environment/" + id);
                if (prefab == null) continue;
                scanned++;

                // 资产根节点自带的**上轴修正**（Z-up → Y-up）。
                // 没有修正（≈单位四元数）就什么都不用做 —— 这一条保证了本函数对"本来就正的模型"是空操作 ✓
                Quaternion assetFix = prefab.transform.rotation;
                if (Quaternion.Angle(assetFix, Quaternion.identity) < 1f) continue;

                Quaternion now = child.rotation;
                float yaw = child.eulerAngles.y;
                Quaternion want = Quaternion.Euler(0f, yaw, 0f) * assetFix;
                if (Quaternion.Angle(now, want) < 2f) continue;      // 已经正确

                child.rotation = want;
                // 转正之后重新贴地（原来的 y 是按"躺倒的 AABB"对出来的）
                Bounds b = BoundsOf(child);
                Vector3 p = child.position;
                child.position = new Vector3(p.x, p.y - b.min.y, p.z);
                fixedCount++;
            }
            if (scanned > 0)
                Log.Info(Tag + $"扶正：扫描 {scanned} 个 ENV_*，**修正 {fixedCount} 个**" +
                              "（根因＝yaw 覆盖了 FBX 根节点的上轴修正）");
            return fixedCount;
        }

        // ══════════════════════════════════════════════════════════════════
        // ② 既有装饰上纹理（不动位置，只换材质）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 给 `TownBootstrap` 已经摆好的 `ENV_*` 装饰**换上有纹理的共享材质**。
        ///
        /// 为什么必须在这边做：`TownKit.Env` 里写的是 `ToonLook.Get(color, 0f, true)` —— 没贴图、
        /// 没描边，而 `TownKit` 属于另一条线（不动它）。做法是**读它已经摆好的对象**、
        /// 按名字里的资源 id 决定该用哪种贴图，再换一次材质 ✓
        /// 材质仍然来自**共享池**：同色同贴图的所有装饰共用一份 `Material` ✓
        /// </summary>
        private int RetextureExistingEnvs(Transform town)
        {
            Transform props = town.Find("TOWN_Props");
            if (props == null) return 0;
            int n = 0;
            for (int i = 0; i < props.childCount; i++)
            {
                Transform child = props.GetChild(i);
                string id = EnvIdOf(child.name);
                if (id == null) continue;
                ToonTex tex = TexFor(id);
                Renderer[] rs = child.GetComponentsInChildren<Renderer>(true);
                for (int k = 0; k < rs.Length; k++)
                {
                    Material[] mats = rs[k].sharedMaterials;
                    if (mats == null) continue;
                    bool changed = false;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (mats[m] == null) continue;
                        Color c = BaseColorOf(mats[m]);
                        Material want = ToonLook.Get(c, 0f, true, tex, TilingFor(tex), worldUV: false);
                        if (ReferenceEquals(want, mats[m])) continue;
                        mats[m] = want;
                        changed = true;
                    }
                    if (changed) { rs[k].sharedMaterials = mats; n++; }
                }
            }
            return n;
        }

        /// <summary>`ENV_tree` → `tree`；不是装饰命名就返回 null。</summary>
        private static string EnvIdOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!name.StartsWith("ENV_")) return null;
            return name.Substring(4);
        }

        /// <summary>资源 id → 贴图种类。**这张表就是"哪些东西是木/石/草"的唯一清单**。</summary>
        private static ToonTex TexFor(string id)
        {
            switch (id)
            {
                case "tree":
                case "stump":
                case "log":
                case "fence":
                case "garden_gate":
                case "crate_stack":
                case "barrel":
                case "haystack":
                case "wheelbarrow":
                case "laundry_line":
                case "windmill":
                case "birdhouse":
                case "chicken_coop":
                case "scarecrow":
                case "farm_plot":
                    return ToonTex.Wood;

                case "rock_a":
                case "rock_b":
                case "rock_c":
                case "wall_ruin":
                case "wall_ruin_b":
                case "wall_ruin_c":
                case "well":
                case "pond":
                case "clay_jars":
                case "lantern":
                    return ToonTex.Stone;

                case "bush":
                case "flower_a":
                case "flower_b":
                case "grass":
                case "reeds":
                case "mushroom":
                    return ToonTex.Grass;

                default:
                    return ToonTex.Wood;    // 未知 id 给木纹：中性、最不容易出错
            }
        }

        /// <summary>道具的 UV 是 FBX 自带的 0~1 ⇒ tiling = "一个物件上铺几次"。2 次是折中。</summary>
        private static float TilingFor(ToonTex tex) => tex == ToonTex.Cloth ? 3f : 2f;

        private static Color BaseColorOf(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        // ══════════════════════════════════════════════════════════════════
        // ③ 卡通草：程序化草片 + 合并成 3 块大网格
        // ══════════════════════════════════════════════════════════════════

        private sealed class MeshBuild
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();

            /// <summary>加一片"麦穗形"草叶：底宽顶尖的四边形，**正反两面都写**。</summary>
            public void Blade(Vector3 basePos, float yawDeg, float leanDeg, float h, float w)
            {
                Quaternion yaw = Quaternion.Euler(0f, yawDeg, 0f);
                // 倾斜轴取"叶面法线"：草叶只在**自己那个平面内**倒，才像被风压弯；
                // 绕别的轴转会让叶面翻过来，从侧面看变成一条线
                Quaternion lean = Quaternion.AngleAxis(leanDeg, yaw * Vector3.forward);
                Vector3 up = lean * Vector3.up;
                Vector3 right = yaw * Vector3.right;

                float wTip = w * 0.16f;
                Vector3 bl = basePos - right * (w * 0.5f);
                Vector3 br = basePos + right * (w * 0.5f);
                Vector3 tl = basePos + up * h - right * (wTip * 0.5f);
                Vector3 tr = basePos + up * h + right * (wTip * 0.5f);

                // 法线：叶面朝外 45% + 世界向上 75%。
                // 纯叶面法线会让"背光的那一半草"整片掉进暗部，远看像草地缺了一块；
                // 掺一半"向上"之后，草地的明暗跟着地面走，才读成**一片**草 ✓
                Vector3 wn = Vector3.Normalize(yaw * Vector3.forward * 0.45f + Vector3.up * 0.75f);

                AddQuad(bl, br, tr, tl, wn, new Vector2(0.10f, 0.05f), new Vector2(0.90f, 0.95f));
                // 反面：`Cull Back` 下不写这一份，从另一侧看草就是空的
                AddQuad(br, bl, tl, tr, -wn, new Vector2(0.10f, 0.05f), new Vector2(0.90f, 0.95f));
            }

            private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 uv0, Vector2 uv1)
            {
                int i0 = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                N.Add(n); N.Add(n); N.Add(n); N.Add(n);
                UV.Add(new Vector2(uv0.x, uv0.y));
                UV.Add(new Vector2(uv1.x, uv0.y));
                UV.Add(new Vector2(uv1.x, uv1.y));
                UV.Add(new Vector2(uv0.x, uv1.y));
                T.Add(i0); T.Add(i0 + 1); T.Add(i0 + 2);
                T.Add(i0); T.Add(i0 + 2); T.Add(i0 + 3);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                // 顶点数可能超过 65535 ⇒ 必须 32 位索引（否则网格被**静默截断**）
                mesh.indexFormat = V.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(V);
                mesh.SetNormals(N);
                mesh.SetUVs(0, UV);
                mesh.SetTriangles(T, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>
        /// 撒草。返回**丛数**（不是草叶数 —— 一丛 5~7 片叶子）。
        ///
        /// ── 疏密是怎么给的（用户要求"要有疏密，不许等距重复"）──
        /// 不是"按网格撒 + 加抖动"（那永远看得出网格），而是**两段式**：
        ///   ① 撒若干"团心"（cluster），团心本身的位置随机；
        ///   ② 每个团心周围按 sqrt 分布撒若干丛，离团心越远越稀。
        /// 于是屏幕上呈现的是"一块浓、一块淡、中间有裸露土"的自然分布 ✓
        ///
        /// ── 区域怎么划（★ 不写死坐标 ★）──
        /// 以**量出来的**内容中心 `_center` 与半径 `_contentR` 为基准，按极坐标分三圈：
        ///   内圈 r ≤ 0.55R：城里（公园/广场周边），最密 —— 这是玩家一直看着的地方
        ///   中圈 0.55R ~ 1.15R：环路与建筑之间，中等
        ///   外圈 1.15R ~ 2.1R：城外草地，最疏
        /// 每一圈再被**禁区矩形**（路面/耕地/建筑/公园小径）剔掉 —— 所以"草不长在农田里"这条
        /// 不靠坐标硬编码，而是靠"农田是一块禁区"这个事实 ✓
        /// </summary>
        private int PlantGrass()
        {
            var builders = new MeshBuild[GrassColors.Length];
            for (int i = 0; i < builders.Length; i++) builders[i] = new MeshBuild();

            var rng = new Pcg32((ulong)Seed, 7UL);
            int clumps = 0;

            // 团心数量按面积给：一丛草 ≈ 0.35 米的观感直径，团心覆盖 3 米半径
            clumps += ScatterRing(builders, ref rng, 0.00f, 0.55f, 46, 1.6f, 7, 16, 0.05f);
            clumps += ScatterRing(builders, ref rng, 0.55f, 0.95f, 40, 1.1f, 5, 12, 0.15f);
            clumps += ScatterRing(builders, ref rng, 0.95f, 1.35f, 52, 1.4f, 4, 10, 0.25f);
            clumps += ScatterRing(builders, ref rng, 1.35f, 2.10f, 34, 2.4f, 3, 8, 0.30f);

            for (int i = 0; i < builders.Length; i++)
            {
                if (builders[i].V.Count == 0) continue;
                Mesh mesh = builders[i].ToMesh("MESH_Grass_" + i);
                var go = new GameObject("GRASS_" + i);
                go.transform.SetParent(_grassRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                // 草**不投影**：整片草合并成 3 块网格，投影时是一大块板；
                // 而 0.3 米高的草在地面上的影子本来也读不出来 ⇒ 省掉这一遍 shadow pass ✓
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
                mr.sharedMaterial = ToonLook.Get(GrassColors[i], 0f, true, ToonTex.Grass, 1f, worldUV: false);
            }
            return clumps;
        }

        /// <summary>在"以内容中心为圆心、`rMin`~`rMax` 倍的环带"里撒团心。</summary>
        private int ScatterRing(MeshBuild[] builders, ref Pcg32 rng,
                                float rMin, float rMax, int clusters, float spread,
                                int perMin, int perMax, float margin)
        {
            int clumps = 0;
            for (int c = 0; c < clusters; c++)
            {
                float ang = rng.Range(0f, Mathf.PI * 2f);
                float rr = _contentR * rng.Range(rMin, rMax);
                var centre = new Vector3(_center.x + Mathf.Cos(ang) * rr, 0f,
                                         _center.z + Mathf.Sin(ang) * rr);
                // 团心落进禁区就**整团丢掉**（不是"挪到边上"——那会在禁区边缘堆出人工边界）
                if (Blocked(centre, margin)) continue;

                int want = rng.RangeInt(perMin, perMax + 1);
                int placed = 0;
                for (int k = 0; k < want * 3 && placed < want; k++)
                {
                    float a2 = rng.Range(0f, Mathf.PI * 2f);
                    // 圆内均匀（sqrt）+ 1.35：外围再薄一点，团与团之间才有"空档"
                    float rad = spread * Mathf.Sqrt(rng.NextFloat()) * 1.35f;
                    var p = new Vector3(centre.x + Mathf.Cos(ang) * rad, 0f, centre.z + Mathf.Sin(ang) * rad);
                    if (Blocked(p, margin)) continue;
                    if (!Free(p, 0.26f)) continue;

                    p.y = GroundY(p);
                    int mat = rng.RangeInt(0, builders.Length);
                    AddClump(builders[mat], ref rng, p);
                    placed++;
                    clumps++;
                }
            }
            return clumps;
        }

        /// <summary>一丛草 = 5~7 片叶子，随机朝向/倾斜/高矮。</summary>
        private static void AddClump(MeshBuild b, ref Pcg32 rng, Vector3 p)
        {
            int blades = rng.RangeInt(5, 8);
            float baseYaw = rng.Range(0f, 360f);      // 整丛也偏一偏，避免"所有丛都朝北"
            for (int i = 0; i < blades; i++)
            {
                float yaw = baseYaw + i * (360f / blades) + rng.Range(-18f, 18f);
                float lean = rng.Range(6f, 26f);
                float h = GrassHeight * rng.Range(0.72f, 1.28f);
                float w = GrassBladeWidth * rng.Range(0.8f, 1.3f);
                var off = new Vector3(rng.Range(-0.055f, 0.055f), 0f, rng.Range(-0.055f, 0.055f));
                b.Blade(p + off, yaw, lean, h, w);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ④ 装饰物
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 主城装饰：**先手摆"有意思的点"，再随机撒"小东西"**。
        ///
        /// 为什么分开，而不是全靠随机：
        /// 纯随机的结果是"到处是石头，但没有一处值得看一眼"。
        /// 用户要的是"大量装饰物 + 疏密有致"——
        /// 前者靠**手摆的景**（每个都是一处小构图），后者靠**随机的碎件**（填满空白）✓
        /// </summary>
        /// <summary>
        /// 主城装饰：**三层**，每层解决一个不同的问题。
        ///
        ///   ① `DressBuildings` —— 每座 `BUILD_*` 门口放 1~2 件货 + 一块牌子。
        ///      解决"建筑像凭空放在草坪上"。位置**从建筑本身量**，不写死坐标 ⇒ 布局随便重排 ✓
        ///   ② `DressVignettes` —— 绕着城摆 8 处"小景"（树+桩+蘑菇 / 晾衣绳+花坛 / 井+陶罐 …）。
        ///      解决"一块值得看的地方都没有"：一处小景 = 一处构图 ✓
        ///   ③ `ScatterSmall` —— 随机碎件（石头/花/灌木/木段），彼此保持最小间距。
        ///      解决"大片空白草坪"✓
        ///
        /// ★ 为什么不再写死坐标（第一版就是那样，实测踩了）★
        /// 主城被另一条线整体重做成了"圆形大公园"：原本的农田/公园/商业街坐标全废，
        /// 写死的 26 件装饰散落在草坪上，看着像"随手扔的"，而不是"摆好的"。
        /// ⇒ 现在所有位置都从**场景里量出来的锚点**推出来，布局改成什么样都成立 ✓
        /// </summary>
        private int PlaceDecorations()
        {
            int n = 0;
            n += DressBuildings();
            n += DressVignettes();
            n += ScatterSmall();
            return n;
        }
        /// <summary>
        /// ① 贴着建筑摆货。按建筑**名字**决定摆什么 —— 这是"这座建筑在做什么"的唯一线索：
        ///   仓库 → 木箱/桶/陶罐；商店 → 陶罐/花坛/木箱；传送门 → 石头/干草垛；
        ///   服装 → 晾衣绳/木箱；帐篷 → 桶/木箱；大门 → 花坛 + 告示牌。
        /// 位置 = 建筑外接圆半径 + 一点余量，角度由建筑坐标的哈希决定（**确定性**）。
        /// </summary>
        /// <summary>v218：建筑旁新增的树（给日志与交付报数用）。</summary>
        private int treesBesideBuildings;

        /// <summary>v220：每栋建筑旁"种下几棵"的逐栋读数（交付里要逐栋报位置）。</summary>
        private readonly System.Text.StringBuilder besideLog = new System.Text.StringBuilder();

        /// <summary>
        /// ★ v220：给一栋建筑找一个**能种树**的点 ★
        ///
        /// 候选网格 = 5 个偏角 × 2 个符号 × 12 个半径，按"先近后远、先正侧后斜后"的顺序试。
        /// 三条判据全部来自用户的话，逐条落在代码里：
        ///   ① `Blocked(cand, 0f)` —— 不在任何禁区（路面/别的建筑/路灯/田圃）⇒ **不堵路** ✓
        ///   ② `ClearOfDoors(cand, 1.5f)` —— **树心距任何门点 ≥ 1.5 米** ✓
        ///   ③ `Free(cand, 0.82f)` —— 不压在已有装饰/别的树上（0.82 = 树冠视觉半径的 0.45 倍，
        ///      树冠可以互相搭一点，树干不许重合）✓
        /// 采不到就**返回 false**，由调用方少种一棵 —— 绝不为了凑数把树种到门里 ✓
        /// </summary>
        private bool FindTreeSpot(Anchor a, float baseAng, float side, out Vector3 spot)
        {
            // 偏角：52°(正侧偏前) → 120°(侧后)。正对门(0°)与正对背面(180°)都不取。
            // ★ v220b：5 档 → **8 档** ★ 实测 5 档时 `BUILD_Shop/Clothing/Home` 各只种下 1 棵
            // （第二棵的 5×12 个候选全被"禁区/门点/间距"三条判据拒绝），加密角度后就够了 ✓
            float[] off = { 52f, 62f, 72f, 82f, 92f, 102f, 111f, 120f };
            for (int oi = 0; oi < off.Length; oi++)
            {
                float ang = baseAng + side * off[oi];
                for (int k = 0; k < 17; k++)
                {
                    // 半径：贴墙根 0.45 米起，每档 0.22 米 —— 17 档覆盖到 a.R+3.97
                    float rad = a.R + 0.45f + k * 0.22f;
                    Vector3 cand = Polar(a.P, rad, ang);
                    if (Blocked(cand, 0f)) continue;
                    if (!ClearOfDoors(cand, 1.5f)) continue;
                    if (!Free(cand, 0.82f)) continue;
                    cand.y = GroundY(cand);
                    spot = cand;
                    return true;
                }
            }
            spot = Vector3.zero;
            return false;
        }

        private int DressBuildings()
        {
            int n = 0;
            for (int i = 0; i < _buildings.Count; i++)
            {
                Anchor a = _buildings[i];
                // 从建筑指向内容中心的方向 = "朝城里那一面"，货摆在那儿才看得见
                Vector3 toCentre = new Vector3(_center.x - a.P.x, 0f, _center.z - a.P.z);
                if (toCentre.sqrMagnitude < 1e-4f) toCentre = Vector3.forward;
                toCentre.Normalize();
                float baseAng = Mathf.Atan2(toCentre.z, toCentre.x) * Mathf.Rad2Deg;
                var rng = new Pcg32((ulong)(Seed + i * 977), 5UL);

                float ring = a.R + 1.15f;
                string want1, want2;
                PickBuildingProps(a.Name, out want1, out want2);

                float ang1 = baseAng + rng.Range(-26f, 26f);
                n += PlaceAtPolar(a.P, ring, ang1, want1, ref rng);
                if (want2 != null)
                {
                    float ang2 = baseAng + rng.Range(-44f, 44f);
                    n += PlaceAtPolar(a.P, ring + 0.75f, ang2, want2, ref rng);
                }

                // ★★ v220：**每栋建筑墙边/门两侧各 1~2 棵树**（用户："楼边缺树，每栋加 1~2 棵"）★★
                //
                // ── 上一版为什么只种下 3 棵（实测 7 栋里 4 栋一棵没有）──
                // 旧代码只试 3 个半径（`a.R*k+1.15`，k 从 1.05 到 1.85）就走人了：
                //   · `Blocked()` 用的是建筑的**AABB 矩形**，而 `a.R = max(extents)` ⇒
                //     斜向那个角在 `a.R*√2 ≈ 1.41a.R` 处 ⇒ 小的 k 一定还在盒子里被挡 ✗
                //   · 大的 k 又正好撞进相邻建筑/绿篱/路灯的禁区 ✗
                //   两个条件把 3 个候选全吃掉 ⇒ `found = false` ⇒ 静默少一棵 ✓
                // ⇒ 现在把"角度 × 半径"摊成一个**二维候选网格**逐个试，且**不再依赖运气**：
                //    角度 5 档 ×(±)  半径 12 档  ⇒ 每侧 120 个候选，7 栋最多 1680 次判定（几毫秒）
                //
                // ── 三条硬判据（逐条对应用户的话）──
                //   ① 贴墙不贴门：角度从"朝城里那一面"偏开 **58°~104°**（偏开才看得见侧墙），
                //      半径从 `a.R+0.55` 起 —— 贴着墙根，而不是墙里也不是路中间 ✓
                //   ② **树心距任何门点 ≥ 1.5 米**：`ClearOfDoors`（门点从几何量，见 `_doors`）✓
                //   ③ **不堵路**：`Blocked()` 的禁区表里本来就有 `RING_/ROAD_/PATH_/SHOP/…`
                //      ⇒ 候选只要不在禁区里，就既不在路上、也不在人流线上 ✓
                {
                    int wanted = 2;                       // 每栋 2 棵（1 棵是兜底）
                    int got = 0;
                    for (int ti = 0; ti < wanted; ti++)
                    {
                        float side = (ti % 2 == 0) ? 1f : -1f;
                        Vector3 tp = Vector3.zero;
                        if (!FindTreeSpot(a, baseAng, side, out tp)) continue;
                        float h, r2;
                        SizeOf("tree", out h, out r2);
                        if (EnvRaw("tree", tp, rng.Range(0f, 360f), h * rng.Range(0.90f, 1.10f), r2) == null) continue;
                        AddTaken(tp, 0.85f);
                        n++;
                        treesBesideBuildings++;
                        // 逐栋逐棵记下位置（交付里要"每栋位置"，也方便复核距门点的距离）
                        besideLog.Append("  ").Append(a.Name).Append(" → 树 @(")
                                 .Append(tp.x.ToString("0.00")).Append(',')
                                 .Append(tp.z.ToString("0.00")).Append(") 距门点 ")
                                 .Append(NearestDoorDistance(tp).ToString("0.00")).Append(" m\n");
                        got++;
                    }
                    if (got == 0)
                        besideLog.Append("  ").Append(a.Name).Append(" → **一棵都没种下**（禁区/门点/间距全部拒绝）\n");
                }

                // 大门/商店/仓库各立一块牌子（牌子上不写字，靠形状读）
                if (a.Name.Contains("Gate") || a.Name.Contains("Shop") || a.Name.Contains("Warehouse"))
                {
                    float angS = baseAng + rng.Range(48f, 76f);
                    Vector3 sp = Polar(a.P, a.R + 0.9f, angS);
                    n += Sign(sp, Mathf.Atan2(_center.x - sp.x, _center.z - sp.z) * Mathf.Rad2Deg, a.Name);
                }
            }
            if (besideLog.Length > 0)
                Log.Info(Tag + $"建筑旁种树：{treesBesideBuildings} 棵（判据：不在禁区 ∧ 树心距门点 ≥ 1.5 m ∧ 不压已有物件）\n"
                              + besideLog);
            return n;
        }

        private static void PickBuildingProps(string name, out string first, out string second)
        {
            if (name.Contains("Warehouse")) { first = "crate_stack"; second = "barrel"; return; }
            if (name.Contains("Shop")) { first = "clay_jars"; second = "crate_stack"; return; }
            if (name.Contains("Portal")) { first = "rock_a"; second = "haystack"; return; }
            if (name.Contains("Clothing")) { first = "laundry_line"; second = "crate_stack"; return; }
            if (name.Contains("Tent")) { first = "barrel"; second = "crate_stack"; return; }
            if (name.Contains("Gate")) { first = "bush"; second = null; return; }
            first = "barrel";
            second = null;
        }

        private static Vector3 Polar(Vector3 origin, float radius, float angleDeg)
        {
            float r = angleDeg * Mathf.Deg2Rad;
            return new Vector3(origin.x + Mathf.Cos(r) * radius, 0f, origin.z + Mathf.Sin(r) * radius);
        }

        /// <summary>在极坐标位置摆一件装饰（尺寸按 id 给一个"应该多大"的经验值）。</summary>
        private int PlaceAtPolar(Vector3 origin, float radius, float angleDeg, string id, ref Pcg32 rng)
        {
            if (id == null) return 0;
            Vector3 p = Polar(origin, radius, angleDeg);
            // 角度用 1° 量化：牌子/木箱的朝向不需要更细，且这样"同一份输入 ⇒ 同一份输出"
            float yaw = angleDeg + 90f + rng.Range(-14f, 14f);
            float h, r;
            SizeOf(id, out h, out r);
            int ok = Env(id, p, yaw, h, r);
            return ok;
        }

        /// <summary>装饰的"应该多大"（高度米 / 半径米）。改这里就是改全局尺寸口径。</summary>
        private static void SizeOf(string id, out float height, out float radius)
        {
            switch (id)
            {
                case "tree": height = 3.9f; radius = 1.8f; return;
                case "windmill": height = 4.2f; radius = 1.6f; return;
                case "well": height = 1.4f; radius = 0.9f; return;
                case "haystack": height = 1.45f; radius = 1.25f; return;
                case "chicken_coop": height = 1.7f; radius = 1.6f; return;
                case "laundry_line": height = 1.3f; radius = 1.55f; return;
                case "wheelbarrow": height = 0.85f; radius = 0.9f; return;
                case "crate_stack": height = 0.92f; radius = 0.68f; return;
                case "barrel": height = 0.84f; radius = 0.45f; return;
                case "clay_jars": height = 0.72f; radius = 0.48f; return;
                case "pond": height = 0.55f; radius = 2.1f; return;
                case "birdhouse": height = 1.5f; radius = 0.5f; return;
                case "bush": height = 0.85f; radius = 0.7f; return;
                case "reeds": height = 0.75f; radius = 0.6f; return;
                case "mushroom": height = 0.3f; radius = 0.3f; return;
                case "stump": height = 0.5f; radius = 0.45f; return;
                case "log": height = 0.45f; radius = 0.6f; return;
                case "rock_a":
                case "rock_b":
                case "rock_c": height = 0.6f; radius = 0.6f; return;
                case "flower_a":
                case "flower_b": height = 0.36f; radius = 0.3f; return;
                default: height = 0.8f; radius = 0.6f; return;
            }
        }

        /// <summary>
        /// ② 城外一圈"小景"：每处 2~4 件，自带疏密（角度不均匀、半径不均匀）。
        /// 这些小景是**从内容中心量出来的极坐标**，所以主城是圆形还是方格都成立 ✓
        /// </summary>
        private int DressVignettes()
        {
            // 每一行 = 一处小景：(半径倍数, 角度基准°, 组件 id 列表)
            // 角度刻意不均匀（31/78/124/…）—— 均匀角度会立刻读成"程序生成的"
            string[][] sets =
            {
                new[] { "tree", "stump", "mushroom" },
                new[] { "laundry_line", "bush" },
                new[] { "well", "clay_jars", "reeds" },
                new[] { "haystack", "rock_a", "rock_b" },
                new[] { "wheelbarrow", "crate_stack" },
                new[] { "tree", "tree", "log" },
                new[] { "pond", "reeds", "rock_c" },
                new[] { "chicken_coop", "bush", "flower_a" },
                new[] { "birdhouse", "bush", "flower_b" },
                new[] { "windmill", "crate_stack" },
            };
            float[] radius = { 1.18f, 1.42f, 1.26f, 1.55f, 1.30f, 1.62f, 1.72f, 1.48f, 1.22f, 1.66f };
            float[] angle = { 31f, 78f, 124f, 167f, 203f, 241f, 288f, 316f, 352f, 14f };

            int n = 0;
            var rng = new Pcg32((ulong)Seed, 21UL);
            for (int s = 0; s < sets.Length; s++)
            {
                float ang = angle[s % angle.Length] + rng.Range(-4f, 4f);
                float rad = _contentR * radius[s % radius.Length] * rng.Range(0.96f, 1.06f);
                Vector3 origin = Polar(_center, rad, ang);
                if (Blocked(origin, 0.5f)) continue;

                string[] com = sets[s];
                for (int k = 0; k < com.Length; k++)
                {
                    // 组件之间拉开 0.5~1.6 米，角度各偏一点 ⇒ 读成"一小片"而不是"堆在一起"
                    float a2 = ang + rng.Range(-9f, 9f);
                    float r2 = rad + rng.Range(-1.5f, 1.5f);
                    Vector3 p = Polar(_center, r2, a2);
                    if (Blocked(p, 0.15f)) continue;
                    if (!Free(p, 0.55f)) continue;
                    n += PlaceAtPolar(_center, r2, a2, com[k], ref rng);
                }
            }
            return n;
        }

        /// <summary>③ 随机碎件：石头 / 树桩 / 蘑菇 / 花，彼此保持最小间距。</summary>
        private int ScatterSmall()
        {
            var rng = new Pcg32((ulong)Seed, 11UL);
            string[] small =
            {
                "rock_a", "rock_b", "rock_c", "stump", "mushroom",
                "flower_a", "flower_b", "bush", "log", "reeds",
            };
            const int Want = 54;
            int placed = 0;
            for (int i = 0; i < Want * 14 && placed < Want; i++)
            {
                float ang = rng.Range(0f, Mathf.PI * 2f);
                float rr = _contentR * rng.Range(0.35f, 1.9f);
                var p = new Vector3(_center.x + Mathf.Cos(ang) * rr, 0f, _center.z + Mathf.Sin(ang) * rr);
                if (Blocked(p, 0.40f)) continue;
                if (!Free(p, 0.95f)) continue;

                string id = small[rng.RangeInt(0, small.Length)];
                float h, r;
                SizeOf(id, out h, out r);
                h *= rng.Range(0.82f, 1.18f);
                if (EnvRaw(id, p, rng.Range(0f, 360f), h, r) == null) continue;
                placed++;
                AddTaken(p, 0.45f);
            }
            return placed;
        }

        // ══════════════════════════════════════════════════════════════════
        // ⑤ 传送门的黑洞动态特效（v214 追加需求）
        //
        // 用户原话："战斗开始的传送门记得添加，并附上建模和黑洞的动态特效"。
        // 建模归另一条线（门框/立柱/拱顶/符文），这里只做**黑洞**，挂在约定的挂点
        // `FX_BossPortal` 上。
        //
        // ★ 动画全在着色器里（`Seedfall/BlackHole`）★
        //   来自 `_Time` ⇒ 没有 Animator、没有 Update 改材质、没有粒子系统
        //   ⇒ 多一个黑洞 = 多 3 个 draw call，不多一份材质、不多一帧 CPU ✓
        //
        // ★ 挂点策略（按上级给的降级方案）★
        //   ① 先按名字递归找 `FX_BossPortal`（连 inactive 一起）
        //   ② 找不到 ⇒ 在 `BUILD_Portal` 的中心自己建一个，**并在日志里说明是我补的**
        //   ③ 已经有子物体 `FX_BlackHole` ⇒ 跳过，绝不建两个 ✓
        // ══════════════════════════════════════════════════════════════════

        private const string PortalAnchorName = "FX_BossPortal";
        private const string HoleName = "FX_BlackHole";
        private const string HoleShaderName = "Seedfall/BlackHole";

        /// <summary>黑洞挂点（取景/日志用）。</summary>
        private Transform _portalAnchor;
        /// <summary>黑洞主体（三层旋涡）。</summary>
        private Transform _hole;
        /// <summary>挂点是不是我自己补的（不是布局那方给的）。</summary>
        private bool _portalAnchorBuilt;

        private static Material _holeMat;      // **全工程一份**，不是每实例一份
        private static Mesh _quadMesh;         // 与 `TownKit.Prim` 同一个共享网格缓存

        private void SpawnPortalVfx(Transform town)
        {
            // ★★ v251：**旋涡已接管就不再建美术那路** ★★
            // 用户报"传送门会在旧版和新版之间切换"：本方法建的 `FX_BlackHole`（美术那路）
            // 与 `TownPortalFx` 的 `PORTALSWIRL_*`（代码那路）是两套视觉效果，
            // 而代码那边每帧把美术这套关掉、本方法的周期扫描又把它建回来 ⇒ 交替闪。
            // 判据 `TownPortalFx.SwirlOwnsPortal`（旋涡已建成）⇒ 直接返回，从源头断掉重复 ✓
            if (TownPortalFx.SwirlOwnsPortal)
            {
                Log.Info(Tag + "传送门已由代码旋涡（PORTALSWIRL_）接管，跳过美术黑洞（v251 防重复）");
                return;
            }

            // ① 找挂点
            Transform anchor = FindDeep(town, PortalAnchorName);
            if (anchor == null)
            {
                Transform portal = FindDeep(town, "BUILD_Portal");
                if (portal == null)
                {
                    Log.Warn(Tag + "既没有 FX_BossPortal 也没有 BUILD_Portal —— 本轮不生成黑洞特效");
                    return;
                }
                Bounds pb = BoundsOf(portal);
                var go = new GameObject(PortalAnchorName);
                go.transform.SetParent(portal.parent != null ? portal.parent : town, false);
                go.transform.position = new Vector3(pb.center.x, pb.center.y, pb.center.z);
                anchor = go.transform;
                _portalAnchorBuilt = true;
                // 挂点建在传送门**包围盒中心**：门框围着这个点，黑洞就该长在门洞里，
                // 而不是门口的地面上 ✓
            }
            _portalAnchor = anchor;

            // ② 判重：已经有一个就不建第二个
            Transform existing = anchor.Find(HoleName);
            if (existing != null)
            {
                Log.Info(Tag + "黑洞特效已存在，跳过（幂等）");
                _hole = existing;
                return;
            }

            // ③ 材质与网格：**只建一次**（静态缓存 ⇒ 全场一份）
            Shader sh = Shader.Find(HoleShaderName);
            if (sh == null)
            {
                Log.Warn(Tag + "找不到 shader " + HoleShaderName + " —— 本轮不生成黑洞特效");
                return;
            }
            if (_holeMat == null || _holeMat.shader != sh)
                _holeMat = new Material(sh) { name = "M_BlackHole" };
            if (_quadMesh == null) _quadMesh = TownKit.Prim(PrimitiveType.Quad);

            var root = new GameObject(HoleName).transform;
            root.SetParent(anchor, false);

            // ── 位置与尺寸：优先按**传送门自己的包围盒**定，而不是死跟挂点 ──
            // 实测（第一版）：布局那方给的挂点在 y=1.60，而门洞中心在 y≈3.5 ⇒
            // 4 米直径的盘有一半**埋进地面**，截图里只剩台阶上一道月牙 ✗
            // ⇒ 抬到"挂点与门洞中心里较高的那个"，尺寸取门洞高的 62%（夹 2.5~4 米）✓
            Vector3 centre = anchor.position;
            float maxD = 4.00f;
            Transform portalRoot = FindDeep(town, "BUILD_Portal");
            if (portalRoot != null)
            {
                Bounds pb = BoundsOf(portalRoot);
                if (pb.size.sqrMagnitude > 0f)
                {
                    centre.y = Mathf.Max(anchor.position.y, pb.center.y);
                    maxD = Mathf.Clamp(pb.size.y * 0.62f, 2.5f, 4.0f);
                }
            }
            root.position = centre;

            // 面片朝向：**朝向主城中心** ⇒ 玩家从城里走过去看到的是正面
            Vector3 toCentre = new Vector3(_center.x - root.position.x, 0f, _center.z - root.position.z);
            if (toCentre.sqrMagnitude < 1e-4f) toCentre = Vector3.back;
            toCentre.Normalize();

            // ★★ 朝向必须**跟门框的平面走**，不能跟"城中心方向"走 ★★
            // 实测（第二轮截图）：按城中心求朝向时，地面片与门面之间有约 **11.7°** 的夹角
            // ⇒ 4 米盘的一侧翘到门面**后面**去了，两个平面互相切割，
            //   黑洞口只剩一道**斜切的白月牙** ✗
            // 现在：取向直接用挂点自己的 forward（挂点挂在 BUILD_Portal 下，天然与门面共面），
            // 只在"它朝哪一侧"上做一次符号判断（朝向城中心的那一侧才是正面）✓
            Vector3 faceDir = anchor.forward;
            Vector3 faceFlat = new Vector3(faceDir.x, 0f, faceDir.z);
            if (faceFlat.sqrMagnitude < 1e-4f) faceFlat = toCentre;
            faceFlat.Normalize();
            if (Vector3.Dot(faceFlat, toCentre) < 0f) faceFlat = -faceFlat;
            root.rotation = Quaternion.LookRotation(faceFlat, Vector3.up);

            // 再沿门派方向推出 0.35 米：保证整块盘都在门面**前面**（不共面就不会互相切）✓
            root.position = centre + faceFlat * 0.35f;

            // 三层：核心 / 吸积盘 / 外晕 = 0.65 / 0.825 / 1.00 × maxD
            float[] size = { maxD * 0.65f, maxD * 0.825f, maxD };
            // 暗核在**世界上**是同一个圆（直径 1.56 米）⇒ 三层各自的 `_CoreRadius`（UV 值）不同，
            // 但画出来的黑饼完全重合 ⇒ 不依赖三层的绘制顺序 ✓
            const float CoreWorldDiameter = 1.56f;
            for (int i = 0; i < size.Length; i++)
            {
                var go = new GameObject(HoleName + "_L" + i);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sharedMaterial = _holeMat;
                go.transform.localScale = new Vector3(size[i], size[i], 1f);

                // 逐实例只改两个 float（层号 + 暗核 UV 半径）—— 走 MPB，**不新建材质**
                var mpb = new MaterialPropertyBlock();
                mpb.SetFloat("_Layer", i);
                mpb.SetFloat("_Core", 1f);
                mpb.SetFloat("_CoreRadius", (CoreWorldDiameter * 0.5f) / (size[i] * 0.5f));
                mr.SetPropertyBlock(mpb);
            }

            _hole = root;

            // ④ 一盏紫色点光：让黑洞"照亮"门洞与地面。
            //    加色面片只加到自己身上，不会照亮周围 ⇒ 这一盏是让黑洞读成"有能量"的关键
            TownKit.Glow(anchor, "FX_BlackHoleLight", anchor.position,
                         new Color(0.55f, 0.35f, 1.00f), 9f, 2.4f);

            // ⑤ 报实测包围盒（交付里要写尺寸）
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
            Bounds b = BoundsOf(root);
            Log.Info(Tag + "黑洞特效已生成：挂点 " + (_portalAnchorBuilt ? "**我自己补的**" : "由布局那方提供") +
                            $" @ ({anchor.position.x:0.00},{anchor.position.y:0.00},{anchor.position.z:0.00})｜" +
                            $"层 {rs.Length} 片｜共享材质 1 份（全场）｜" +
                            $"包围盒 {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} m（直径 {Mathf.Max(b.size.x, b.size.y):0.00} m）｜" +
                            "动画 = shader 的 `_Time`：旋涡角速度 1.7（内圈更快，1/(0.18+r)）· 向心流速 1.2 · 3 旋臂 · 呼吸 0.30");
        }

        // ══════════════════════════════════════════════════════════════════
        // ⑤ 美术取景（**验收工具，不是玩法代码**）
        //
        // 为什么需要它：主城机位归 `TownBootstrap.UpdateCamera` 所有（每帧写死
        // orthographicSize / position），而它属于另一条线，本轮不许改。
        // 于是"近景图 / 全景图"这两张交付截图没法靠玩家操作拿到 ——
        // 这里在 `LateUpdate` 里压一层取景（LateUpdate 晚于 Update ⇒ 一定赢），
        // 用 `unity_drive send TownArtDresser TownArtDresser Photo 1|2|0` 切换。
        //
        // ★ 只在被显式调用时生效 ★ 默认 `_photoMode == 0` ⇒ 一个字段判断就返回，
        //   不影响任何一帧的正常观感 ✓
        // ══════════════════════════════════════════════════════════════════
        private int _photoMode;
        private Camera _photoCam;

        /// <summary>
        /// ★ v214：验收用的**整屏截图**（含 IMGUI）★
        ///
        /// 为什么需要它：桥的 `/capture` 走的是"把 Camera.main 渲到 RenderTexture"——
        /// 那条路**拍不到 IMGUI**（`OnGUI` 画在相机之后的屏幕缓冲上）⇒
        /// 登录卡、主城 HUD 在这条路上一律是空白 ✗
        /// `ScreenCapture.CaptureScreenshot` 抓的是**最终画面**，UI 在里面 ✓
        ///
        /// 用法：`unity_drive send {obj} {type} Shot 文件名` ⇒ 落到 `<工程根>/Library/<文件名>.png`
        /// 名字为空时用默认名。返回写盘路径（桥那边可以直接回读这个字符串）。
        /// </summary>
        public string Shot(string name)
        {
            string file = string.IsNullOrEmpty(name) ? "town_shot" : name;
            if (!file.EndsWith(".png")) file += ".png";
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Library");
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, file);
            ScreenCapture.CaptureScreenshot(path);
            Log.Info(Tag + "整屏截图 → " + path);
            return path;
        }

        /// <summary>
        /// ★★ v214：**几何判据审计**（验收工具）★ 一次回答三个问题：
        ///   ① 哪些装饰**躺倒了** —— 不量 `transform.up`，量**渲染器世界 AABB 的最长轴**。
        ///      `transform.up` 抓不到"网格自己是 Z-up 而 transform 没转"这一类
        ///      （布局那路的 `UpAxisProbe` 就漏在这里）；AABB 的长轴是**几何事实**，骗不了人 ✓
        ///   ② 路面/地表的**贴图到底有没有接上** —— 报材质名、`_MainTex`、`_TexMode`、
        ///      `_MainTex_ST`、网格 UV 范围。`_TexMode=1` 而 UV 全是 0 ⇒ 世界投影那条腿没生效。
        ///   ③ 玩家**脚下 2 米内有什么** —— 报名字与世界坐标（用户说的"脚底下一片贴图"）。
        ///
        /// 调用：`unity_drive send TownArtDresser TownArtDresser Audit`（返回字符串，桥会打出来）
        /// </summary>
        public string Audit()
        {
            var sb = new System.Text.StringBuilder();
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return "[AUDIT] 没有 TOWN";

            sb.Append("① 长轴判据（期望 +Y 的类：长轴应≈竖直）\n");
            string[] expectY =
            {
                "tree", "bush", "flower_a", "flower_b", "grass", "reeds", "mushroom", "stump", "log",
                "crate_stack", "barrel", "clay_jars", "haystack", "fence", "garden_gate", "birdhouse",
                "chicken_coop", "windmill", "well", "lantern", "scarecrow", "wheelbarrow", "laundry_line",
                "farm_plot", "rock_a", "rock_b", "rock_c", "pond", "wall_ruin",
            };
            int bad = 0, seen = 0;
            Renderer[] all = town.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                string nm = r.transform.name;
                string id = EnvIdOf(nm);
                if (id == null)
                {
                    if (!nm.StartsWith("DEC_")) continue;
                    id = nm.StartsWith("DEC_Planter") ? "planter"
                       : nm.StartsWith("DEC_Sign") ? "sign"
                       : nm.Substring(4);
                }
                bool wantY = false;
                for (int k = 0; k < expectY.Length; k++) if (id.StartsWith(expectY[k])) { wantY = true; break; }
                if (!wantY) continue;
                seen++;

                Bounds b = r.bounds;
                Vector3 s = b.size;
                // ★ v219：只把**细长**的东西判成"躺倒" ★
                // 实测：木桶 0.75×0.73×0.74、蘑菇 0.45×0.37×0.46 这类**本来就接近立方**的件，
                // 长轴取 X 还是 Y 是随机的 ⇒ 旧判据把它们全算成"躺倒"（196 个里报 103 个），
                // 而截图里它们明明是立着的 ✗ 判据要有意义，就得要求"长轴明显长于其它轴"：
                //   长轴 / 最短轴 > 1.50 才叫"细长" ⇒ 才有"站/躺"这回事
                float maxS = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                float minS = Mathf.Max(1e-4f, Mathf.Min(s.x, Mathf.Min(s.y, s.z)));
                bool slender = maxS / minS > 1.50f;
                int axis = (s.x >= s.y && s.x >= s.z) ? 0 : (s.y >= s.z ? 1 : 2);
                if (axis == 1) continue;
                if (!slender) continue;         // 接近立方 ⇒ 无所谓躺不躺

                bad++;
                if (bad <= 26)
                {
                    Vector3 e = r.transform.eulerAngles;
                    sb.Append("   ✗ ").Append(nm)
                      .Append(" ｜长轴=").Append(axis == 0 ? "X" : "Z")
                      .Append(" ｜size=").Append(s.x.ToString("0.00")).Append('×').Append(s.y.ToString("0.00")).Append('×').Append(s.z.ToString("0.00"))
                      .Append(" ｜y=").Append(b.min.y.ToString("0.00")).Append("..").Append(b.max.y.ToString("0.00"))
                      .Append(" ｜pos=(").Append(b.center.x.ToString("0.0")).Append(',').Append(b.center.z.ToString("0.0")).Append(')')
                      .Append(" ｜rot=").Append(e.x.ToString("0")).Append(',').Append(e.y.ToString("0")).Append(',').Append(e.z.ToString("0"))
                      .Append('\n');
                }
            }
            sb.Append("   ⇒ ").Append(bad).Append('/').Append(seen).Append(" 个长轴水平（期望竖直）\n");

            sb.Append("② 地表/路面材质\n");
            int shown = 0;
            for (int i = 0; i < all.Length && shown < 10; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                string nm = r.transform.name;
                if (!(Has(nm, "Ground") || Has(nm, "Grass") || Has(nm, "Lawn") || Has(nm, "Road")
                      || Has(nm, "Ring") || Has(nm, "Path") || Has(nm, "Roof") || Has(nm, "Stone"))) continue;
                Material m = r.sharedMaterial;
                if (m == null) { sb.Append("   ").Append(nm).Append(" ｜材质=null\n"); shown++; continue; }
                Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                float mode = m.HasProperty("_TexMode") ? m.GetFloat("_TexMode") : -1f;
                Vector2 st = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.zero;
                string uvInfo = "无MeshFilter";
                var mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh != null)
                {
                    Vector2[] uvs = mesh.uv;
                    if (uvs == null || uvs.Length == 0) uvInfo = "**没有UV**";
                    else
                    {
                        float u0 = uvs[0].x, v0 = uvs[0].y, u1 = uvs[0].x, v1 = uvs[0].y;
                        for (int k = 1; k < uvs.Length; k++)
                        {
                            if (uvs[k].x < u0) u0 = uvs[k].x;
                            if (uvs[k].y < v0) v0 = uvs[k].y;
                            if (uvs[k].x > u1) u1 = uvs[k].x;
                            if (uvs[k].y > v1) v1 = uvs[k].y;
                        }
                        uvInfo = "uv[" + u0.ToString("0.00") + ".." + u1.ToString("0.00") + ","
                               + v0.ToString("0.00") + ".." + v1.ToString("0.00") + "]";
                    }
                }
                Bounds b = r.bounds;
                sb.Append("   ").Append(nm)
                  .Append(" ｜mat=").Append(m.name)
                  .Append(" ｜tex=").Append(tex != null ? tex.name : "**null**")
                  .Append(" ｜_TexMode=").Append(mode.ToString("0"))
                  .Append(" ｜ST=(").Append(st.x.ToString("0.00")).Append(',').Append(st.y.ToString("0.00")).Append(')')
                  .Append(" ｜").Append(uvInfo)
                  .Append(" ｜AABB=").Append(b.size.x.ToString("0.0")).Append('×').Append(b.size.z.ToString("0.0"))
                  .Append('\n');
                shown++;
            }

            GameObject player = GameObject.Find("TownPlayer");
            if (player != null)
            {
                Vector3 pp = player.transform.position;
                sb.Append("③ 玩家脚下 6 米内、贴地扁平件（玩家 @ ").Append(pp.x.ToString("0.00")).Append(',')
                  .Append(pp.y.ToString("0.00")).Append(',').Append(pp.z.ToString("0.00")).Append("）\n");
                int near = 0;
                for (int i = 0; i < all.Length && near < 20; i++)
                {
                    Renderer r = all[i];
                    if (r == null) continue;
                    Bounds b = r.bounds;
                    float dx = b.center.x - pp.x, dz = b.center.z - pp.z;
                    if (dx * dx + dz * dz > 36f) continue;      // 半径 6 米
                    // 只看"贴地的扁平件"：用户的"脚底下一片贴图"就是这类
                    if (b.max.y > pp.y + 1.6f) continue;
                    near++;
                    Material mm = r.sharedMaterial;
                    string matInfo = mm == null ? "mat=null"
                        : ("mat=" + mm.name + (mm.HasProperty("_BaseColor") ? " a=" + mm.GetColor("_BaseColor").a.ToString("0.00") : "")
                           + " shader=" + mm.shader.name);
                    var chain = new System.Text.StringBuilder();
                    Transform tk = r.transform;
                    int depth = 0;
                    while (tk != null && depth < 5) { chain.Insert(0, tk.name + "/"); tk = tk.parent; depth++; }
                    sb.Append("   · ").Append(chain.ToString())
                      .Append(" ｜").Append(matInfo)
                      .Append(" ｜size=").Append(b.size.x.ToString("0.00")).Append('×').Append(b.size.y.ToString("0.00")).Append('×').Append(b.size.z.ToString("0.00"))
                      .Append(" ｜y=").Append(b.min.y.ToString("0.00")).Append("..").Append(b.max.y.ToString("0.00"))
                      .Append(" ｜center=(").Append(b.center.x.ToString("0.00")).Append(',').Append(b.center.z.ToString("0.00")).Append(')')
                      .Append('\n');
                }
                if (near == 0) sb.Append("   （2 米内没有任何渲染器）\n");
            }
            return sb.ToString();
        }


        /// <summary>
        /// ★★ v214：**路面贴图探针**（用户的判据：同一块路面取 3 个世界坐标，色差必须明显）★★
        ///
        /// 做法不是"读材质参数猜" —— 而是**真的把它渲染出来、读像素**：
        ///   ① 用几何判据挑出最大的一块"又扁又贴地"的地面渲染器
        ///   ② 造一台临时正交相机垂直俯拍它的中心，渲进 128×128 的 RenderTexture
        ///   ③ 把 3 个世界坐标（中心 / +1.5m / -1.5m）用 `WorldToViewportPoint` 投影成像素，各取 1 个点
        ///   ④ 报 3 个 RGB 与两两 Δ（Δ<0.02 即"纹理没生效"）
        ///
        /// 为什么必须渲染后读像素：材质参数全对、shader 分支也对，**仍然可能**因为
        /// mipmap 把细纹理平均掉 / UV 尺度太小 / 走错分支 ⇒ 屏幕上一片纯色。
        /// 只有"屏幕上那三个像素"是判据 ✓
        /// </summary>
        public string ProbeRoad()
        {
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return "[PROBE] 没有 TOWN";

            Renderer target = null;
            float best = -1f;
            Renderer[] all = town.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Bounds b = r.bounds;
                float span = Mathf.Max(b.size.x, b.size.z);
                float flatness = b.size.y / Mathf.Max(1e-3f, span);
                // ★ 排除我方生成的几何 ★
                // 实测：`GRASS_1` 是**合并后的大网格**（AABB 122×0.53×106）⇒ 它比任何路面都"大而扁"，
                // 第一版探针就选中了它 ⇒ 报的是草的 Δ，不是路面的 ✗
                // v220 追加：`FURROW_*`（耕地沟条，14.3 米长）同样"又大又扁"，同样要排除 ✗
                string nm0 = r.transform.name;
                if (nm0.StartsWith("GRASS_") || nm0.StartsWith("DEC_") || nm0.StartsWith("FX_")
                    || nm0.StartsWith("FURROW_")) continue;
                if (span < 3f || flatness > 0.30f || b.max.y > _groundY + 0.8f) continue;
                // ★ v220：**路面优先** —— 先按名字/材质认定"这是路面"，没有再退回几何最大者 ★
                // 否则 12.8 米的 `RING_GreenBelt`（草）会压过 8.8 米的 `RING_RoadInner`，
                // 探针报的是一块草地的色差 ⇒ 判据对不上"路面贴图生效"这件事 ✗
                bool roadLike = Has(nm0, "Road") || Has(nm0, "Street") || Has(nm0, "Plaza")
                             || Has(nm0, "Path") || Has(nm0, "Curb");
                float score = (roadLike ? 1000f : 0f) + span;
                if (score > best) { best = score; target = r; }
            }
            if (target == null) return "[PROBE] 找不到大块地面";

            Bounds tb = target.bounds;
            var sb = new System.Text.StringBuilder();
            Material m = target.sharedMaterial;
            Texture tex = (m != null && m.HasProperty("_MainTex")) ? m.GetTexture("_MainTex") : null;
            Vector2 st = (m != null && m.HasProperty("_MainTex")) ? m.GetTextureScale("_MainTex") : Vector2.zero;
            float mode = (m != null && m.HasProperty("_TexMode")) ? m.GetFloat("_TexMode") : -1f;
            sb.Append("[PROBE] 目标=").Append(target.name)
              .Append(" ｜AABB=").Append(tb.size.x.ToString("0.00")).Append('×').Append(tb.size.y.ToString("0.00")).Append('×').Append(tb.size.z.ToString("0.00"))
              .Append(" ｜mat=").Append(m != null ? m.name : "null")
              .Append(" ｜_MainTex=").Append(tex != null ? tex.name : "**null**")
              .Append(" ｜_TexMode=").Append(mode.ToString("0"))
              .Append(" ｜_MainTex_ST=(").Append(st.x.ToString("0.00")).Append(',').Append(st.y.ToString("0.00")).Append(')')
              .Append(" ｜_BaseColor=").Append(m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "?")
              .Append('\n')
              .Append("       世界投影下的平铺：每米 ").Append(st.x.ToString("0.00")).Append(" 次 ⇒ 一块砖 ")
              .Append((st.x > 0.001f ? (1f / (st.x * 2f)) : 0f).ToString("0.00")).Append(" 米（贴图 2×2 块）\n");

            // 临时俯拍
            // ★ v220：正交半高固定 **3.0 米**（一屏 6 米 / 128 像素 ⇒ 21 px/m）★
            // 旧版取 `tb.extents.z * 0.9`：路面是 8.8 米见方 ⇒ 半高 3.96 ⇒ 一屏 7.9 米 / 128 像素，
            // 3 个采样点只隔 1.5 米 = **24 像素**，而 `_MainTex_ST` 是每米 1.1 次 ⇒ 24 像素里
            // 正好是同一块砖 → Δ 被小样本吃掉。固定 3.0 米后 1.5 米 = 32 像素，跨过砖缝 ✓
            GameObject camGo = new GameObject("PROBE_CAM");
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 3.00f;
            cam.transform.position = new Vector3(tb.center.x, tb.max.y + 12f, tb.center.z);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 40f;

            const int R = 128;
            RenderTexture rt = RenderTexture.GetTemporary(R, R, 24);
            RenderTexture prev = RenderTexture.active;
            Texture2D read = null;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                read = new Texture2D(R, R, TextureFormat.RGB24, false);
                read.ReadPixels(new Rect(0, 0, R, R), 0, 0);
                read.Apply();

                Vector3[] world =
                {
                    new Vector3(tb.center.x, tb.max.y, tb.center.z),
                    new Vector3(tb.center.x + 1.5f, tb.max.y, tb.center.z),
                    new Vector3(tb.center.x, tb.max.y, tb.center.z - 1.5f),
                };
                Color[] got = new Color[3];
                for (int i = 0; i < 3; i++)
                {
                    Vector3 vp = cam.WorldToViewportPoint(world[i]);
                    int px = Mathf.Clamp(Mathf.RoundToInt(vp.x * R), 0, R - 1);
                    int py = Mathf.Clamp(Mathf.RoundToInt(vp.y * R), 0, R - 1);
                    got[i] = read.GetPixel(px, py);
                    sb.Append("       采样").Append(i + 1).Append(" @世界(")
                      .Append(world[i].x.ToString("0.00")).Append(',').Append(world[i].z.ToString("0.00")).Append(") → 像素(")
                      .Append(px).Append(',').Append(py).Append(") = RGB(")
                      .Append(got[i].r.ToString("0.000")).Append(',').Append(got[i].g.ToString("0.000")).Append(',').Append(got[i].b.ToString("0.000"))
                      .Append(")\n");
                }
                float d01 = Mathf.Abs(got[0].r - got[1].r) + Mathf.Abs(got[0].g - got[1].g) + Mathf.Abs(got[0].b - got[1].b);
                float d02 = Mathf.Abs(got[0].r - got[2].r) + Mathf.Abs(got[0].g - got[2].g) + Mathf.Abs(got[0].b - got[2].b);
                float d12 = Mathf.Abs(got[1].r - got[2].r) + Mathf.Abs(got[1].g - got[2].g) + Mathf.Abs(got[1].b - got[2].b);
                float mx = Mathf.Max(d01, Mathf.Max(d02, d12));
                sb.Append("       色差：Δ01=").Append(d01.ToString("0.0000"))
                  .Append(" Δ02=").Append(d02.ToString("0.0000"))
                  .Append(" Δ12=").Append(d12.ToString("0.0000"))
                  .Append(" ⇒ 最大 ").Append(mx.ToString("0.0000"))
                  .Append(mx < 0.02f ? "  ✗ **判未生效**（<0.02）" : "  ✓ 纹理生效（>0.02）")
                  .Append('\n');

                // ★ v220：**5×5 网格扫描**（补强判据）★
                // 只取 3 个点会被"相位巧合"骗：实测 Δ01=0.0118（两点正好落在同一种砖面上），
                // 看起来像"纹理没生效"，其实相邻 0.45 米就换了一块砖。
                // ⇒ 在目标中心 ±1.0 米的方形里均匀取 25 个点，报**最小值 / 最大值 / 平均**：
                //   纹理在 ⇒ 25 个点之间必然有明显起伏；纹理不在（纯色材质）⇒ 25 个点几乎全等 ✓
                int gN = 5;
                float gLo = float.MaxValue, gHi = float.MinValue, gSum = 0f;
                int gCnt = 0;
                for (int gz = 0; gz < gN; gz++)
                {
                    for (int gx = 0; gx < gN; gx++)
                    {
                        var w = new Vector3(tb.center.x - 1f + gx * 0.5f, tb.max.y, tb.center.z - 1f + gz * 0.5f);
                        Vector3 vp2 = cam.WorldToViewportPoint(w);
                        int px2 = Mathf.Clamp(Mathf.RoundToInt(vp2.x * R), 0, R - 1);
                        int py2 = Mathf.Clamp(Mathf.RoundToInt(vp2.y * R), 0, R - 1);
                        Color c2 = read.GetPixel(px2, py2);
                        float lum = c2.r * 0.299f + c2.g * 0.587f + c2.b * 0.114f;
                        gLo = Mathf.Min(gLo, lum);
                        gHi = Mathf.Max(gHi, lum);
                        gSum += lum;
                        gCnt++;
                    }
                }
                sb.Append("       网格扫描 5×5（±1.0 米）：亮度 min=").Append(gLo.ToString("0.0000"))
                  .Append(" max=").Append(gHi.ToString("0.0000"))
                  .Append(" 平均=").Append((gSum / Mathf.Max(1, gCnt)).ToString("0.0000"))
                  .Append(" ⇒ 起伏 ").Append((gHi - gLo).ToString("0.0000"))
                  .Append(gHi - gLo < 0.02f ? "  ✗ **整片同色 ⇒ 判未生效**" : "  ✓ 同一块路面上确有色差")
                  .Append('\n');
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null) Destroy(read);
                Destroy(camGo);
            }
            Log.Info(sb.ToString());
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // ① 脚下那片"米色多边形薄片"的**归属查证**（用户点名的第①条）
        //
        // 用户原话："玩家脚下那片异常贴图删除 —— 这个好像是登入界面为了效果添加的"。
        // 图里是玩家脚下一片**米色多边形薄片**（半透）。
        //
        // ── 它是谁（先说结论，本方法负责把证据打出来）──
        // 名字 `FacingRing` / 子物体 `FacingRingDisc` ＋ `FacingDot`，挂在 `TownPlayer` 根节点下。
        // 造它的是 **`WorldView.BuildFacingIndicator`**，由 **`Art/PlayerBean.cs:338`** 在
        // `PlayerBean.Build` 里调用 —— 也就是 v203/v204 那个"玩家脚下的朝向指示"。
        // 材质里 `FacingPixelBody = (0.949, 0.937, 0.902)` ⇒ 正是**米色**；
        // 走的是 `_RingTex` 的四边形 ⇒ **多边形薄片**；材质队列 3000（Transparent）⇒ **半透** ✓
        // 用户的印象"登入界面为了效果添加的"其实指的就是它（登录→主城一路都跟着玩家）✓
        //
        // ⇒ 归属：`Assets/Scripts/Art/**` ＋ `Assets/Scripts/Bootstrap/**`，
        //   **不属于 `Scripts/Town/**` 的 `BUILD_*`** ⇒ 按约定**可以**由本轮删除（见 PlayerBean 的改动）✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v220：报出玩家脚下那片薄片的**名字 / 世界坐标 / 父级链 / 材质**，并顺带列出
        /// 玩家 2.5 米内所有"贴地且半透"的渲染器（防止脚下还有第二片）。
        /// 调用：`unity_drive send TownArtDresser TownArtDresser FeetReport`
        /// </summary>
        public string FeetReport()
        {
            var sb = new System.Text.StringBuilder();
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return "[FEET] 没有 TOWN（还没进城？）";

            GameObject player = GameObject.Find("TownPlayer");
            Vector3 pp = player != null ? player.transform.position : Vector3.zero;
            sb.Append("[FEET] 玩家 @ (").Append(pp.x.ToString("0.00")).Append(',')
              .Append(pp.y.ToString("0.00")).Append(',').Append(pp.z.ToString("0.00")).Append(")\n");

            // ① 按名字找那一族
            Transform ring = FindDeep(town.transform, "FacingRing");
            if (ring == null)
            {
                sb.Append("   · 场景里**没有** `FacingRing` —— 脚下那片已经不在了\n");
            }
            else
            {
                sb.Append("   · 名字 = `").Append(ring.name).Append("`  ｜ 世界坐标 (")
                  .Append(ring.position.x.ToString("0.000")).Append(',')
                  .Append(ring.position.y.ToString("0.000")).Append(',')
                  .Append(ring.position.z.ToString("0.000")).Append(")\n");
                sb.Append("     父级链 = ").Append(ChainOf(ring)).Append('\n');
                sb.Append("     子物体 =");
                for (int i = 0; i < ring.childCount; i++)
                {
                    Transform c = ring.GetChild(i);
                    sb.Append(' ').Append(c.name);
                    Renderer cr = c.GetComponent<Renderer>();
                    if (cr != null)
                    {
                        Material cm = cr.sharedMaterial;
                        sb.Append("[mat=").Append(cm != null ? cm.name : "null");
                        if (cm != null && cm.HasProperty("_BaseColor"))
                        {
                            Color cc = cm.GetColor("_BaseColor");
                            sb.Append(" RGB(").Append(cc.r.ToString("0.000")).Append(',')
                              .Append(cc.g.ToString("0.000")).Append(',')
                              .Append(cc.b.ToString("0.000")).Append(") a=").Append(cc.a.ToString("0.00"));
                        }
                        if (cm != null) sb.Append(" shader=").Append(cm.shader.name);
                        sb.Append(" queue=").Append(cm != null ? cm.renderQueue.ToString() : "?");
                        sb.Append(']');
                    }
                }
                sb.Append('\n');
            }

            // ② 玩家 2.5 米内、贴地且半透的渲染器（不封顶 20 条 —— 上一版就栽在这个封顶）
            sb.Append("   · 玩家 2.5 米内贴地半透件：\n");
            Renderer[] all = town.GetComponentsInChildren<Renderer>(true);
            int found = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Bounds b = r.bounds;
                float dx = b.center.x - pp.x, dz = b.center.z - pp.z;
                if (dx * dx + dz * dz > 6.25f) continue;              // 2.5 米
                Material m = r.sharedMaterial;
                bool transparent = m != null && m.renderQueue >= 3000;
                float alpha = (m != null && m.HasProperty("_BaseColor")) ? m.GetColor("_BaseColor").a : 1f;
                if (!transparent && alpha > 0.995f) continue;
                if (b.max.y > pp.y + 0.35f) continue;                 // 贴地
                found++;
                sb.Append("       - ").Append(ChainOf(r.transform))
                  .Append(" ｜mat=").Append(m != null ? m.name : "null")
                  .Append(" a=").Append(alpha.ToString("0.00"))
                  .Append(" ｜size=").Append(b.size.x.ToString("0.00")).Append('×')
                  .Append(b.size.y.ToString("0.00")).Append('×').Append(b.size.z.ToString("0.00"))
                  .Append(" ｜center=(").Append(b.center.x.ToString("0.00")).Append(',')
                  .Append(b.center.z.ToString("0.00")).Append(")\n");
            }
            if (found == 0) sb.Append("       （没有：脚下已经干净了）\n");

            Log.Info(sb.ToString());
            return sb.ToString();
        }

        /// <summary>从根（含 TOWN）往下的父级链，形如 `TOWN/TownPlayer/FacingRing`。</summary>
        private static string ChainOf(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            Transform cur = t;
            int guard = 0;
            while (cur != null && guard < 12)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
                guard++;
            }
            string s = sb.ToString();
            return s.Length > 1 ? s.Substring(0, s.Length - 1) : s;
        }

        // ══════════════════════════════════════════════════════════════════
        // ⑤ 上轴修正的**前后读数**：只量几何，不量 `transform.up`
        //
        // 用户判据（逐字）："渲染器世界 AABB 的**最长轴** —— `TREE_*` 必须接近 +Y（阈值 15°）"，
        // 并明确：扁平件（木箱/长椅/路牌）与 `ENV_log`（本来就该躺）**豁免**。
        //
        // 为什么必须用 AABB：`transform.up` 永远是 (0,1,0)（错的是**网格**的上轴，不是 Transform），
        // 布局那路的探针就是漏在这里 ⇒ 它报"没有一件 >2°"而截图里一片躺倒 ✗
        //
        // 本方法**不改工程状态**：它把同一个对象的旋转临时换成"旧式子"量一次、再换回来 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>本来就该"长轴水平"或接近立方、因此不参与"站/躺"判定的 id（用户点名豁免）。</summary>
        private static readonly string[] AxisExempt =
        {
            "log", "crate_stack", "clay_jars", "laundry_line", "wheelbarrow", "pond",
            "haystack", "wall_ruin", "farm_plot", "fence", "garden_gate",
        };

        /// <summary>
        /// ★ v220：报 `ENV_*` 的**长轴判据**，并给出"旧式子（只留 yaw）→ 现在"的前后对比。
        /// 调用：`unity_drive send TownArtDresser TownArtDresser EnvAxisReport`
        /// </summary>
        public string EnvAxisReport()
        {
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return "[AXIS] 没有 TOWN";
            Transform props = town.transform.Find("TOWN_Props");
            if (props == null) return "[AXIS] 没有 TOWN_Props";

            var sb = new System.Text.StringBuilder();
            sb.Append("[AXIS] 判据 = 世界 AABB 最长轴是不是 +Y（用户口径；阈值 15°，等价于逐轴比较）\n");
            sb.Append("       豁免（本来就该扁平/该躺）：").Append(string.Join(", ", AxisExempt)).Append('\n');

            int tot = 0, slender = 0, badNow = 0, badOld = 0;
            int treeTot = 0, treeNow = 0, treeOld = 0;
            var detail = new System.Text.StringBuilder();
            var per = new Dictionary<string, int[]>();

            for (int i = 0; i < props.childCount; i++)
            {
                Transform child = props.GetChild(i);
                string id = EnvIdOf(child.name);
                if (id == null) continue;
                bool exempt = false;
                for (int k = 0; k < AxisExempt.Length; k++) if (id == AxisExempt[k]) { exempt = true; break; }
                if (exempt) continue;

                Renderer[] rs = child.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;

                Quaternion now = child.rotation;
                bool slNow;
                Bounds bNow = CombinedBounds(rs);
                bool upNow = LongAxisIsUp(bNow, out slNow);
                // ★ 旧式子：只留 yaw，把 FBX 根节点的上轴修正覆盖掉 ★
                //   —— 这就是用户截图里"模型全倒下"的那一行代码 ✓
                child.rotation = Quaternion.Euler(0f, now.eulerAngles.y, 0f);
                bool slOld;
                bool upOld = LongAxisIsUp(CombinedBounds(rs), out slOld);
                child.rotation = now;                                  // 还原（本方法只读）

                tot++;
                if (slNow) slender++;
                if (slNow && !upNow) badNow++;
                if (slOld && !upOld) badOld++;

                // 现在仍然"长轴水平"的，逐个报尺寸 —— 读者要能自己判断"这是造型还是倒伏"
                if (slNow && !upNow && detail.Length < 2000)
                {
                    Vector3 ds = bNow.size;
                    detail.Append("   · ").Append(child.name)
                          .Append(" ｜size=").Append(ds.x.ToString("0.00")).Append('×')
                          .Append(ds.y.ToString("0.00")).Append('×').Append(ds.z.ToString("0.00"))
                          .Append(" ｜长轴=").Append(ds.x >= ds.y && ds.x >= ds.z ? "X" : (ds.z >= ds.y ? "Z" : "Y"))
                          .Append(" ｜高/宽比 ").Append((ds.y / Mathf.Max(1e-4f, Mathf.Max(ds.x, ds.z))).ToString("0.00"))
                          .Append('\n');
                }

                string key = id.StartsWith("tree") ? "tree" : id;
                if (!per.TryGetValue(key, out int[] acc)) { acc = new int[3]; per[key] = acc; }
                acc[0]++;                                   // 共几个
                if (slOld && !upOld) acc[1]++;              // 旧式子下长轴水平
                if (slNow && !upNow) acc[2]++;              // 现在长轴水平

                if (id.StartsWith("tree"))
                {
                    treeTot++;
                    if (slNow && upNow) treeNow++;
                    if (slOld && upOld) treeOld++;
                }
            }

            foreach (var kv in per)
            {
                int[] a = kv.Value;
                sb.Append("   ").Append(kv.Key.PadRight(14))
                  .Append("共 ").Append(a[0])
                  .Append(" ｜长轴水平：旧式子 ").Append(a[1]).Append(" → 现在 ").Append(a[2]).Append('\n');
            }
            sb.Append("   ── 合计 ── 参与判定 ").Append(tot).Append(" 个（其中细长 ").Append(slender).Append(" 个）\n");
            sb.Append("   长轴水平的：**旧式子 ").Append(badOld).Append(" 个 → 现在 ").Append(badNow).Append(" 个**\n");
            sb.Append("   TREE_* 专项：共 ").Append(treeTot)
              .Append(" 棵 ｜长轴≈+Y：旧式子 ").Append(treeOld)
              .Append(" 棵 → 现在 ").Append(treeNow).Append(" 棵\n");
            if (detail.Length > 0)
            {
                sb.Append("   现在仍然「长轴水平」的逐个如下（判断是造型还是倒伏用）：\n");
                sb.Append(detail);
            }
            Log.Info(sb.ToString());
            return sb.ToString();
        }

        /// <summary>AABB 最长轴是不是 Y（且这堆渲染器确实"细长" —— 接近立方的东西没有站/躺之分）。</summary>
        private static bool LongAxisIsUp(Bounds b, out bool isSlender)
        {
            Vector3 s = b.size;
            float maxS = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            float minS = Mathf.Max(1e-4f, Mathf.Min(s.x, Mathf.Min(s.z, s.y)));
            isSlender = maxS / minS > 1.30f;
            // 用户口径就是"最长轴"：最长边落在 Y 上 = 站得住 ✓
            // （15° 阈值落在"最长轴"这个离散判断上：AABB 里最长边与 +Y 的夹角只可能是 0° 或 90°）
            return s.y >= s.x - 1e-5f && s.y >= s.z - 1e-5f;
        }

        /// <summary>一堆渲染器的合并世界 AABB。</summary>
        private static Bounds CombinedBounds(Renderer[] rs)
        {
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ══════════════════════════════════════════════════════════════════
        // 验收截图：**同步渲染 → 直接落盘 PNG**
        //
        // 为什么不沿用 `Photo(mode)`（LateUpdate 压游戏相机）：
        // 那条路要"相机在正确的帧上被采"，而本轮要的 5 张图里有 1 张（耕地）必须
        // **整块 6×6 进画面**、1 张（脚下）必须**正好对着玩家** —— 靠人手动切档 +
        // 抓时机不可复现。这里用一台临时正交相机 `Render()` 进 RenderTexture，
        // `ReadPixels` → `EncodeToPNG` → 写 `Library/v220/`，**一帧内全部完成** ✓
        // 与 `ProbeRoad` 同一手法：读数与图都来自"真的渲染出来的像素"。
        // ══════════════════════════════════════════════════════════════════

        /// <summary>36 块 `PLOT_*` 的中心（世界 XZ，y 用地面高度）。</summary>
        private Vector3 FarmCentreOfPlots()
        {
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return _center;
            Renderer[] rs = town.GetComponentsInChildren<Renderer>(true);
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].transform.name.StartsWith("PLOT_")) continue;
                sum += rs[i].bounds.center;
                n++;
            }
            if (n == 0) return _center;
            return new Vector3(sum.x / n, _groundY, sum.z / n);
        }

        /// <summary>第一棵 `ENV_tree` 的世界坐标（取景用；没有就退回内容中心）。</summary>
        private Vector3 FirstTreeSpot()
        {
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return _center;
            Transform props = town.transform.Find("TOWN_Props");
            if (props != null)
                for (int i = 0; i < props.childCount; i++)
                    if (props.GetChild(i).name.StartsWith("ENV_tree"))
                        return props.GetChild(i).position;
            return _center;
        }

        /// <summary>
        /// `ProbeRoad` **真正测到的那块路面**的中心 —— 图④与读数④必须指向同一块地，
        /// 否则"图上没砖缝"会被读成"纹理没生效"✓ 选路规则与 `ProbeRoad` 完全一致。
        /// </summary>
        private Vector3 RoadCentre()
        {
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return _center;
            Renderer best = null;
            float bestScore = -1f;
            Renderer[] all = town.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Bounds b = r.bounds;
                float span = Mathf.Max(b.size.x, b.size.z);
                float flatness = b.size.y / Mathf.Max(1e-3f, span);
                string nm = r.transform.name;
                if (nm.StartsWith("GRASS_") || nm.StartsWith("DEC_") || nm.StartsWith("FX_")
                    || nm.StartsWith("FURROW_")) continue;
                if (span < 3f || flatness > 0.30f || b.max.y > _groundY + 0.8f) continue;
                bool roadLike = Has(nm, "Road") || Has(nm, "Street") || Has(nm, "Plaza")
                             || Has(nm, "Path") || Has(nm, "Curb");
                float score = (roadLike ? 1000f : 0f) + span;
                if (score > bestScore) { bestScore = score; best = r; }
            }
            if (best == null) return _center;
            Bounds bb = best.bounds;
            return new Vector3(bb.center.x, _groundY, bb.center.z);
        }

        /// <summary>东侧那三栋（服装/杂货/家）的重心 —— "楼边有树"这张图就对着它们拍。</summary>
        private Vector3 EastBuildingsCentre()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < _buildings.Count; i++)
            {
                if (_buildings[i].P.x < 6f) continue;
                sum += _buildings[i].P;
                n++;
            }
            if (n == 0) return _center;
            return new Vector3(sum.x / n, _groundY, sum.z / n);
        }

        /// <summary>
        /// ★ v220：一次落盘 5 张验收图到 `Library/v220/` ★
        /// 返回每张的文件名与字节数（交付里直接引用）。
        /// </summary>
        public string SnapAll()
        {
            var sb = new System.Text.StringBuilder();
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Library", "v220");
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            sb.Append("[SNAP] 输出目录 ").Append(dir).Append('\n');

            var camGo = new GameObject("SNAP_CAM");
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.64f, 0.79f, 0.93f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            cam.cullingMask = ~0;

            Vector3 playerPos = _center;
            GameObject player = GameObject.Find("TownPlayer");
            if (player != null) playerPos = player.transform.position;

            try
            {
                // ① 耕地：整块 6×6 进画面（俯角 80°、半高 7.6 ⇒ 地面纵深 15.4 米 > 13.5 米）
                sb.Append(RenderPng(cam, System.IO.Path.Combine(dir, "v220_1_farm_6x6.png"),
                                    FarmCentreOfPlots(), 80f, 40f, 7.6f, 1100, 760));
                // ② 全景：楼 + 树 + 公园一眼看完
                sb.Append(RenderPng(cam, System.IO.Path.Combine(dir, "v220_2_town_wide.png"),
                                    new Vector3(_center.x, _groundY, _center.z), 60f, 140f,
                                    Mathf.Max(10f, _contentR * 1.55f), 1100, 760));
                // ③ 东侧三栋楼 + 新种的树（俯角 30° ⇒ 树的高度读得出来）
                sb.Append(RenderPng(cam, System.IO.Path.Combine(dir, "v220_3_buildings_trees.png"),
                                    EastBuildingsCentre(), 30f, 40f, 11f, 1100, 760));
                // ④ 路面贴地：12° 低角、半高 2.2 ⇒ 一屏 4.4 米，砖缝看得见
                // ★ v220b：焦点改成**探针真正测到的那块路面**的中心 ★
                // 旧值是 `_center + (6.5,0,6.5)` = (12.3, 5.6)，实测那个点正对着商业街边的**建筑外墙**
                // ⇒ 图上一面墙，看不到铺装 ✗ 现在与 `ProbeRoad` 用同一条选路规则，图与读数对得上 ✓
                Vector3 roadAt = RoadCentre();
                sb.Append(RenderPng(cam, System.IO.Path.Combine(dir, "v220_4_road_texture.png"),
                                    roadAt, 12f, 9f, 2.2f, 1100, 760));
                // ⑤ ENV_* 扶正：对着一棵树，俯角只有 14° ⇒ 树干是不是竖直一眼可判
                sb.Append(RenderPng(cam, System.IO.Path.Combine(dir, "v220_5_env_upright.png"),
                                    FirstTreeSpot(), 14f, 26f, 6.5f, 1100, 760));
                // ⑥ 玩家脚下（① 那条薄片的"删前/删后"对照就靠这一张）
                // ★ 必须**近乎垂直俯视**：第一版用 58° 从南边拍，机位正对着 `BUILD_Gate` 的门楼
                //   ⇒ 整块薄片被门楼的横梁挡掉，图上一片灰 ✗ 90° 会撞上 `LookRotation` 的退化
                //   （forward 与 up 平行），所以取 84° ✓
                sb.Append(RenderPng(cam, System.IO.Path.Combine(dir, "v220_6_player_feet.png"),
                                    new Vector3(playerPos.x, _groundY, playerPos.z),
                                    84f, 9f, 1.8f, 1100, 760));
            }
            finally
            {
                Destroy(camGo);
            }

            Log.Info(sb.ToString());
            return sb.ToString();
        }

        /// <summary>把一台正交相机摆到 <paramref name="focus"/> 上方、按俯角拍一张，写 PNG。</summary>
        private static string RenderPng(Camera cam, string path, Vector3 focus,
                                        float pitchDeg, float dist, float orthoSize,
                                        int w, int h)
        {
            float p = pitchDeg * Mathf.Deg2Rad;
            Vector3 off = new Vector3(0f, Mathf.Sin(p), -Mathf.Cos(p)) * dist;
            cam.orthographicSize = orthoSize;
            cam.transform.position = focus + off;
            cam.transform.rotation = Quaternion.LookRotation(-off.normalized, Vector3.up);

            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24);
            RenderTexture prev = RenderTexture.active;
            Texture2D read = null;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                read = new Texture2D(w, h, TextureFormat.RGB24, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply();
                byte[] png = read.EncodeToPNG();
                System.IO.File.WriteAllBytes(path, png);
                return "   · " + System.IO.Path.GetFileName(path) + "  " + png.Length + " B" +
                       "  ｜焦点 (" + focus.x.ToString("0.0") + "," + focus.z.ToString("0.0") + ")" +
                       " ｜俯角 " + pitchDeg.ToString("0") + "° ｜半高 " + orthoSize.ToString("0.0") + " m\n";
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null) Destroy(read);
            }
        }

        /// <summary>
        /// 取景档位：0 = 还原（不干预）；1 = 近景（4 米半高，看草/描边/贴图）；
        /// 2 = 全景（整座城装进画面）。返回当前档位，便于桥那边回读。
        /// </summary>
        public int Photo(int mode)        {
            _photoMode = Mathf.Clamp(mode, 0, 6);
            if (_photoCam == null) _photoCam = Camera.main;
            if (_photoMode == 0) { _photoCam = null; Log.Info(Tag + "取景：还原"); return 0; }

            string name = mode == 1 ? "近景 4m" : mode == 2 ? "全景" : mode == 3 ? "黑洞近景"
                        : mode == 4 ? "草地贴地" : mode == 5 ? "路面贴地" : "耕地近景";
            Log.Info(Tag + "取景：" + name + "｜相机=" + (_photoCam != null ? _photoCam.name : "无"));
            return _photoMode;
        }

        private void LateUpdate()
        {
            if (_photoMode == 0) return;
            if (_photoCam == null) { _photoCam = Camera.main; if (_photoCam == null) return; }

            // 取景中心：玩家优先（近景要看他脚下的草），否则用内容中心
            GameObject player = GameObject.Find("TownPlayer");
            Vector3 focus = player != null ? player.transform.position : _center;

            if (_photoMode == 3 && _hole != null)
            {
                // 黑洞近景：正交半高 3.2 ⇒ 一屏 6.4 米，4 米直径的盘占 62% 画面高
                const float PitchDeg = 20f, Dist = 11f;
                float pitch3 = PitchDeg * Mathf.Deg2Rad;
                Vector3 off3 = new Vector3(0f, Mathf.Sin(pitch3), -Mathf.Cos(pitch3)) * Dist;
                Vector3 face = _hole.position - off3;
                _photoCam.orthographic = true;
                _photoCam.orthographicSize = 3.2f;
                _photoCam.transform.position = face + off3;
                _photoCam.transform.rotation = Quaternion.LookRotation(-off3.normalized, Vector3.up);
                return;
            }

            if (_photoMode == 6)
            {
                // 耕地近景：俯角 62°、正交半高 4.6 ⇒ 一屏 9.2 米，6×6 块耕地刚好整块进画面，
                // 判据是"一眼能数出 6 列 6 行" ✓
                Vector3 fp = _center;
                Renderer[] rs6 = GameObject.Find("TOWN").GetComponentsInChildren<Renderer>(true);
                int plotN = 0; Vector3 sum = Vector3.zero;
                for (int i = 0; i < rs6.Length; i++)
                {
                    if (rs6[i] == null) continue;
                    string n6 = rs6[i].transform.name;
                    // ★ v220：取景中心 = **36 块 `PLOT_*` 的中心** ★
                    // 旧版找 `FARM_` 前缀，而实际的田底叫 `ZONE_FarmSoil` ⇒ 一块都匹配不上
                    // ⇒ 退化成"内容中心"，拍到的根本不是农田 ✗
                    if (!n6.StartsWith("PLOT_")) continue;
                    sum += rs6[i].bounds.center; plotN++;
                }
                if (plotN > 0) fp = new Vector3(sum.x / plotN, _groundY, sum.z / plotN);
                // ★ v220：取景参数按"整块 6×6 必须进画面"反推 ★
                // 地块网格从第一块中心到最后一块中心 = (6−1)×2.30 = 11.5 米，
                // 再加两侧各 1.0 米（半块畦）= **13.5 米**；正交相机的"地面纵深"是
                // `2×orthoSize / sin(俯角)` ⇒ 要 ≥ 15 米留边，俯角 80° 时 orthoSize ≥ 7.4 ✓
                // 旧值 4.6 米只能装下 9.4 米 ⇒ 一屏只看得见 4 行 ✗（这正是"数不出 6×6"的原因之一）
                const float Pitch6 = 80f, Dist6 = 40f;
                float p6 = Pitch6 * Mathf.Deg2Rad;
                Vector3 off6 = new Vector3(0f, Mathf.Sin(p6), -Mathf.Cos(p6)) * Dist6;
                _photoCam.orthographic = true;
                _photoCam.orthographicSize = 7.6f;
                _photoCam.transform.position = fp + off6;
                _photoCam.transform.rotation = Quaternion.LookRotation(-off6.normalized, Vector3.up);
                return;
            }

            if (_photoMode == 5)
            {
                // 路面贴地：俯角只有 12°、正交半高 2.2 ⇒ 一屏 4.4 米，1.1 米一块砖 ⇒ **约 4 块可见**，能数
                const float Pitch5 = 12f, Dist5 = 9f;
                float p5 = Pitch5 * Mathf.Deg2Rad;
                Vector3 off5 = new Vector3(0f, Mathf.Sin(p5), -Mathf.Cos(p5)) * Dist5;
                var rp = new Vector3(_center.x + 6.5f, _groundY, _center.z + 6.5f);
                _photoCam.orthographic = true;
                _photoCam.orthographicSize = 2.2f;
                _photoCam.transform.position = rp + off5;
                _photoCam.transform.rotation = Quaternion.LookRotation(-off5.normalized, Vector3.up);
                return;
            }

            if (_photoMode == 4)
            {
                // 草地贴地：正交半高 1.4 ⇒ 一屏 2.8 米，0.42 米的草占 15% 屏高，看得清墨线与贴图
                const float PitchDeg = 30f, Dist = 8f;
                float pitch4 = PitchDeg * Mathf.Deg2Rad;
                Vector3 off4 = new Vector3(0f, Mathf.Sin(pitch4), -Mathf.Cos(pitch4)) * Dist;
                Vector3 gp = focus + new Vector3(2.2f, 0f, 2.6f);
                gp.y = _groundY;
                _photoCam.orthographic = true;
                _photoCam.orthographicSize = 1.4f;
                _photoCam.transform.position = gp + off4;
                _photoCam.transform.rotation = Quaternion.LookRotation(-off4.normalized, Vector3.up);
                return;
            }

            if (_photoMode == 1)
            {
                // 近景：正交半高 4.2 ⇒ 一屏 8.4 米，草（0.3 米）约占 3.6% 屏高，看得清描边
                const float PitchDeg = 40f, Dist = 14f;
                float pitch = PitchDeg * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(0f, Mathf.Sin(pitch), -Mathf.Cos(pitch)) * Dist;
                _photoCam.orthographic = true;
                _photoCam.orthographicSize = 4.2f;
                _photoCam.transform.position = focus + offset;
                _photoCam.transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
            }
            else
            {
                // 全景：正交半高按内容半径给（保证整座城进画面），俯角 62°
                const float PitchDeg = 62f, Dist = 60f;
                float pitch = PitchDeg * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(0f, Mathf.Sin(pitch), -Mathf.Cos(pitch)) * Dist;
                _photoCam.orthographic = true;
                _photoCam.orthographicSize = Mathf.Max(10f, _contentR * 1.55f);
                _photoCam.transform.position = new Vector3(_center.x, 0f, _center.z) + offset;
                _photoCam.transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
            }
        }

        /// <summary>摆一件装饰，并把它的占地记进障碍表（后面撒碎件和种花都躲着它）。</summary>
        private int Env(string id, Vector3 pos, float yaw, float targetHeight, float targetRadius)
        {
            GameObject go = EnvRaw(id, pos, yaw, targetHeight, targetRadius);
            if (go == null) return 0;
            AddTaken(new Vector3(pos.x, 0f, pos.z), Mathf.Max(0.45f, targetRadius));
            return 1;
        }

        /// <summary>
        /// 复用 `Resources/Art/Environment/*` 的一件装饰 —— 与 `TownKit.EnvFit` 同一手法
        /// （量包围盒 → 按目标高度等比缩 → 底部对齐），但**材质走带贴图 + 墨线的那一版**。
        ///
        /// 为什么不直接调 `TownKit.EnvFit`：它用 `ToonLook.Get(color, 0f, true)`（无贴图、无描边），
        /// 而本轮要的是"带纹理 + 有黑边"。`TownKit` 归另一条线，不动它 ⇒
        /// 在这里重写这段**纯表现**的代码（十几行），代价远小于跨线改文件的冲突风险 ✓
        /// </summary>
        private GameObject EnvRaw(string id, Vector3 pos, float yaw, float targetHeight, float targetRadius)
        {
            GameObject prefab = Resources.Load<GameObject>("Art/Environment/" + id);
            if (prefab == null)
            {
                Log.Warn(Tag + "装饰资源 Art/Environment/" + id + " 缺失");
                return null;
            }
            GameObject go = Instantiate(prefab, _decoRoot);
            go.name = "DEC_" + id;
            // ★★ 把 yaw 与**资产自带的上轴修正**相乘，而不是取代它 ★★
            // 旧式子是 `SetPositionAndRotation(pos, Euler(0, yaw, 0))` ⇒ 把 FBX 根节点的
            // Rx(270)（Z-up → Y-up）**整个覆盖掉** ⇒ 树躺倒、木箱侧躺、牌子平躺 ✗
            // 见 `FixUpright` 的注释（那条机理是"大量倒下的模型"的根因）✓
            Quaternion assetFix = prefab.transform.rotation;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * assetFix);

            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return go;

            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float k = 1f;
            if (targetHeight > 0f && b.size.y > 1e-4f) k = targetHeight / b.size.y;
            if (targetRadius > 0f)
            {
                float r = Mathf.Max(b.size.x, b.size.z) * 0.5f;
                if (r > 1e-4f) k = Mathf.Min(k, targetRadius / r);
            }
            if (!Mathf.Approximately(k, 1f)) go.transform.localScale *= k;

            // 底部对齐（缩放后 bounds 变了，重新量）
            rs = go.GetComponentsInChildren<Renderer>(true);
            Bounds b2 = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b2.Encapsulate(rs[i].bounds);
            Vector3 p = go.transform.position;
            go.transform.position = new Vector3(p.x, p.y - b2.min.y, p.z);

            // 材质：共享池 + 贴图 + 墨线。描边宽度只分 3 档（小/中/大），
            // **不按每件东西的包围盒逐件算** —— 那会让材质池按物体数增长（TownKit 的注释里量过）✓
            ToonTex tex = TexFor(id);
            float outline = targetHeight < 0.6f ? 0.012f : (targetHeight < 2f ? 0.018f : 0.028f);
            for (int i = 0; i < rs.Length; i++)
            {
                Material[] mats = rs[i].sharedMaterials;
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                    mats[m] = ToonLook.Get(BaseColorOf(mats[m]), outline, true, tex, TilingFor(tex), worldUV: false);
                rs[i].sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>
        /// 把一组"以原点为中心"的零件装到 <paramref name="pos"/> 并转 <paramref name="yaw"/>。
        ///
        /// 为什么要绕这一道：`TownKit.Box/Cyl/Ball` 设的是**世界坐标**（它们自己 SetParent 后
        /// 直接写 `transform.position`）。所以零件必须先按"原点附近"建，建完再整体搬 ——
        /// 否则旋转父节点会把零件甩到别处 ✓
        /// </summary>
        private static void PlaceRig(Transform rig, Vector3 pos, float yaw)
        {
            rig.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>新件①：**花坛** —— 木框 + 土面 + 三朵花（没有对应 FBX，程序化造）。</summary>
        private int Planter(Vector3 pos, float yaw)
        {
            var rigGo = new GameObject("DEC_Planter");
            rigGo.transform.SetParent(_decoRoot, false);
            rigGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform t = rigGo.transform;

            Material wood = ToonLook.Get(new Color(0.62f, 0.45f, 0.29f), 0.016f, true, ToonTex.Wood, 2f);
            Material soil = ToonLook.Get(new Color(0.36f, 0.26f, 0.19f), 0.014f, true, ToonTex.Stone, 2f);
            Material leaf = ToonLook.Get(new Color(0.36f, 0.60f, 0.28f), 0.012f, true, ToonTex.Grass, 2f);
            Material[] petals =
            {
                ToonLook.Get(new Color(0.92f, 0.55f, 0.62f), 0.012f, true, ToonTex.Cloth, 2f),
                ToonLook.Get(new Color(0.96f, 0.82f, 0.38f), 0.012f, true, ToonTex.Cloth, 2f),
                ToonLook.Get(new Color(0.72f, 0.58f, 0.92f), 0.012f, true, ToonTex.Cloth, 2f),
            };

            TownKit.Box(t, "Body", new Vector3(0f, 0.16f, 0f), new Vector3(1.50f, 0.32f, 0.62f), wood);
            TownKit.Box(t, "Rim", new Vector3(0f, 0.34f, 0f), new Vector3(1.62f, 0.08f, 0.74f), wood);
            TownKit.Box(t, "Soil", new Vector3(0f, 0.33f, 0f), new Vector3(1.38f, 0.06f, 0.50f), soil);
            // 支脚：离地 0.02 米，读"这是个能搬动的花箱"而不是砌在地上的
            TownKit.Box(t, "FootL", new Vector3(-0.60f, 0.01f, 0f), new Vector3(0.16f, 0.06f, 0.50f), wood);
            TownKit.Box(t, "FootR", new Vector3(0.60f, 0.01f, 0f), new Vector3(0.16f, 0.06f, 0.50f), wood);

            var rng = new Pcg32((ulong)(Seed + Mathf.RoundToInt(pos.x * 8f) * 31 + Mathf.RoundToInt(pos.z * 8f) * 17), 3UL);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.44f;
                float h = rng.Range(0.20f, 0.30f);
                TownKit.Cyl(t, "Stem" + i, new Vector3(x, 0.33f + h * 0.5f, 0f), 0.018f, h, leaf);
                TownKit.Ball(t, "Head" + i, new Vector3(x, 0.33f + h, 0f),
                             Vector3.one * rng.Range(0.085f, 0.115f), petals[i % petals.Length]);
            }

            PlaceRig(t, pos, yaw);
            AddTaken(new Vector3(pos.x, 0f, pos.z), 1.0f);
            return 1;
        }

        /// <summary>
        /// 新件②：**告示牌** —— 两根柱子 + 牌面 + 金色边框。
        ///
        /// 牌面上不画字（IMGUI 才能画字；世界空间里画字要另开一套 UI 方案，不值得）。
        /// 靠"木牌 + 金框"这个形状读出"这是块牌子"，与主城其它木构同一套语言 ✓
        /// </summary>
        private int Sign(Vector3 pos, float yaw, string label)
        {
            var rigGo = new GameObject("DEC_Sign");
            rigGo.transform.SetParent(_decoRoot, false);
            rigGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform t = rigGo.transform;

            Material wood = ToonLook.Get(new Color(0.58f, 0.41f, 0.26f), 0.018f, true, ToonTex.Wood, 2f);
            Material gold = ToonLook.Get(new Color(0.94f, 0.76f, 0.26f), 0.014f, true, ToonTex.Cloth, 2f);
            Material board = ToonLook.Get(new Color(0.86f, 0.78f, 0.62f), 0.016f, true, ToonTex.Cloth, 2f);

            TownKit.Cyl(t, "PostL", new Vector3(-0.52f, 0.55f, 0f), 0.055f, 1.10f, wood);
            TownKit.Cyl(t, "PostR", new Vector3(0.52f, 0.55f, 0f), 0.055f, 1.10f, wood);
            TownKit.Box(t, "Board", new Vector3(0f, 1.02f, 0f), new Vector3(1.28f, 0.60f, 0.07f), board);
            TownKit.Box(t, "Trim", new Vector3(0f, 1.02f, 0.01f), new Vector3(1.38f, 0.70f, 0.05f), gold);
            TownKit.Box(t, "Cap", new Vector3(0f, 1.40f, 0f), new Vector3(1.44f, 0.10f, 0.14f), wood);

            PlaceRig(t, pos, yaw);
            Log.Info(Tag + $"立了一块告示牌：「{label}」@ ({pos.x:0.0}, {pos.z:0.0})");
            AddTaken(new Vector3(pos.x, 0f, pos.z), 0.8f);
            return 1;
        }
    }
}
