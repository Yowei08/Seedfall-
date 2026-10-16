using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 世界表现层：把逻辑状态脏值同步到 GameObject。
    /// 只读 RunSession，绝不回写逻辑（架构案 §数据流）。
    /// </summary>
    public sealed class WorldView
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private sealed class Marker
        {
            public GameObject Go;
            public float Ttl;
            public float Total;
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
        /// v92：alpha 0.85→1.00（需求"加强怪物红色霓虹光描边"）——半透明的描边在暗色夜里会被背景吃掉。</summary>
        private static readonly Color NeonEnemy = new Color(1f, 0.13f, 0.10f, 1.00f);

        /// <summary>
        /// 品质色 → 霓虹色。品质色本身偏暗（拿来做卡通底色刚好），
        /// 直接拿来发光会"亮不起来"，所以这里提亮 + 拉饱和。
        /// </summary>
        private static Color NeonOf(Color grade)
        {
            float max = Mathf.Max(grade.r, Mathf.Max(grade.g, grade.b));
            // v92：提亮上限 2.2→2.6、系数 1.35→1.60，alpha 0.75→1.00（需求"加强植物霓虹光描边"）
            float boost = max > 0.01f ? Mathf.Min(2.6f, 1.60f / max) : 1f;
            return new Color(Mathf.Clamp01(grade.r * boost), Mathf.Clamp01(grade.g * boost),
                             Mathf.Clamp01(grade.b * boost), 1.00f);
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
                moved = v2 > 0.0025f;
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

            Bus.Subscribe<JumpTelegraphEvent>(OnTelegraph);
            Bus.Subscribe<SpawnWarningEvent>(OnSpawnWarning);   // v20：敌人出生前的原地预警标记
            Bus.Subscribe<EnemyAttackFieldEvent>(OnField);
            Bus.Subscribe<BossEvent>(OnBoss);
            Bus.Subscribe<MeleeSwingEvent>(OnMeleeSwing);
            Bus.Subscribe<BeamEvent>(OnBeam);              // v38：水壶激光
            Bus.Subscribe<FertilizerBlastEvent>(OnFertBlast);   // v38：肥料爆炸
            Bus.Subscribe<FertilizerTelegraphEvent>(OnFertTelegraph);   // v91：爆炸前摇的区域标记
            Bus.Subscribe<TreeMatureEvent>(OnPlotMature);  // v38：成熟提示
            Bus.Subscribe<EnemyKilledEvent>(OnKilledShake);
        }

        public void Clear()
        {
            Bus.Unsubscribe<JumpTelegraphEvent>(OnTelegraph);
            Bus.Unsubscribe<SpawnWarningEvent>(OnSpawnWarning);
            Bus.Unsubscribe<EnemyAttackFieldEvent>(OnField);
            Bus.Unsubscribe<BossEvent>(OnBoss);
            Bus.Unsubscribe<MeleeSwingEvent>(OnMeleeSwing);
            Bus.Unsubscribe<BeamEvent>(OnBeam);
            Bus.Unsubscribe<FertilizerBlastEvent>(OnFertBlast);
            Bus.Unsubscribe<FertilizerTelegraphEvent>(OnFertTelegraph);
            Bus.Unsubscribe<TreeMatureEvent>(OnPlotMature);
            Bus.Unsubscribe<EnemyKilledEvent>(OnKilledShake);

            _enemies.Clear();
            _projectiles.Clear();
            _pickups.Clear();
            _markers.Clear();
            _plotTiles = null;
            _plotSoils = null;
            _plotTrees = null;
            _plotCrops = null;
            _plotYoungs = null;
            _plotGrowns = null;
            _plotGrowPhase = null;
            _plotSeedIds = null;
            _fenceSegs.Clear();
            _scenery.Clear();
            _sceneryStatic.Clear();
            _ruinWalls.Clear();
            _sceneryTime = 0f;
            for (int i = 0; i < _swings.Count; i++)
                if (_swings[i].Go != null) Object.Destroy(_swings[i].Go);
            _swings.Clear();
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
            if (_vfxShader == null) _vfxShader = Shader.Find("Seedfall/Vfx");
            if (_vfxShader == null) return Mat(baseColor, true);      // 兜底：找不到就用普通材质
            var m = new Material(_vfxShader);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, baseColor);
            else if (m.HasProperty(ColorId)) m.SetColor(ColorId, baseColor);
            m.SetColor(RimColorId, rim);
            m.SetFloat(RimPowerId, 2.2f);
            return m;
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
            Material ground = MatTextured("grass", new Color(1f, 1f, 1f), half * 0.6f);
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
            }
            _plotTiles = tiles;
            _plotSoils = soils;
            _plotTrees = trees;
            _plotCrops = crops;
            _plotYoungs = youngs;
            _plotGrowns = grown;
            _plotGrowPhase = phases;
            _plotSeedIds = seedIds;
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

            // ② 草丛：铺满全场（这是地面的"底料"，密度最高）
            //    注意：密度是**可玩性**问题，不是美术问题——铺太满，敌人和足迹就读不出来了。
            //    实测 320 丛时整屏都是草，压到 170。
            int tufts = 170;
            for (int i = 0; i < tufts; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = rng.Range(0f, half - 0.8f);
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                SpawnScenery("grass", p, rng.Range(0f, 360f), 1.0f, rng.Range(0f, 6.28f));
            }

            // ③ 碎石：**大幅减量**。石头在俯视图里是最大的地面对比物，
            //    铺多了像满地鹅卵石滩，把角色淹没掉。14 块点缀足够。
            for (int i = 0; i < 14; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = rng.Range(0f, half - 1.0f);
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                SpawnScenery(rng.Chance(0.5f) ? "rock_b" : "rock_c", p, rng.Range(0f, 360f), 0f, 0f);
            }

            // ④ 地标：风车小屋固定在西北角外，水塘在东南 —— 固定位置才有"这是个地方"的感觉
            SpawnScenery("windmill", new Vector3(-half + 3.2f, 0f, half - 3.4f), 135f, 0f, 0f);
            SpawnScenery("pond", new Vector3(half - 3.6f, 0f, -half + 3.8f), 0f, 0f, 0f);
            SpawnScenery("pond", new Vector3(half - 4.2f, 0f, half - 4.6f), 0f, 0f, 0f);

            // ⑤ v64 新增装饰：花 / 蘑菇 / 芦苇 / 树桩 / 倒木 / 木桶
            //    花草量大、单体小，负责把地面的"空"填掉；树桩木桶少量，负责讲"有人来过"的故事。
            for (int i = 0; i < 90; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = rng.Range(0f, half - 1.0f);
                SpawnScenery(rng.Chance(0.5f) ? "flower_a" : "flower_b",
                    new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                    rng.Range(0f, 360f), 1.0f, rng.Range(0f, 6.28f));
            }
            for (int i = 0; i < 24; i++)
            {
                float a = rng.Range(0f, Mathf.PI * 2f);
                float r = rng.Range(0f, half - 1.0f);
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
            const float Seg = 2.0f;
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
        /// v145：**把三把武器挂到右手**（武器接线第一步：只挂载，不切换）。
        ///
        /// ★ 为什么三把武器共用一个旋转值（+180° 绕 Y）★
        /// 建模时把**握持朝向烘进了网格顶点** ✓ 每把武器的 `GRIP` 节点都在原点、单位旋转 ✓
        /// 定义是"手柄沿 +Z、掌心法线沿 +X" ✓ 在手腕局部系里转成"手柄竖直、法线朝前"
        /// 正好是绕 Y 轴 180° —— 所以**不用逐把试角度** ✓
        ///
        /// ★ 为什么先只挂载 ★
        /// "切换 + 攻击动画两段采样"要动每帧更新那段（`WorldView.cs` 玩家 Tick），
        /// 而挂载是自包含的 ✓ 分开做，出问题时能确定是哪一半 ✗
        ///
        /// 三条空值保护都是必要的：骨骼名对不上、模型缺失、`Resources` 里没有，
        /// 任何一种都不该让玩家整个消失 ✗（`Find` 返回 null 而不是报错，最容易被忽略）
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
            for (int i = 0; i < ids.Length; i++)
            {
                ModelView mv = ModelView.Spawn("Tools", ids[i], null);
                if (mv == null || mv.Go == null) continue;
                GameObject go = mv.Go;
                go.transform.SetParent(hand, worldPositionStays: false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 三把同一个值
                _tools[i] = go;
                SetToolVisible(i, i == 0);                                     // 开局显示锄头
            }
            Debug.Log($"[Seedfall] 武器挂载完成：{(_tools[0] != null ? 1 : 0) + (_tools[1] != null ? 1 : 0) + (_tools[2] != null ? 1 : 0)}/3"
                      + $"（挂点 {(hand != null ? "OK" : "缺失")}）");
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
            sb.Append($"t={_playerModel.StateTime:0.000} speed={_playerModel.PlaybackSpeed:0.00} ");
            sb.Append($"atkClips=[{_playerModel.DescribeAttackClips()}]");
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
            _playerModel = PlayerModelEnabled ? ModelView.Spawn("Characters", "player", _root) : null;
            if (_playerModel != null)
            {
                _player = _playerModel.Go;
                _playerModel.SetScaleFactor(CharScale * PlayerScale);   // v85：玩家比普通怪再大一点
                AttachTools();                                          // v145：三把武器挂到手部

                // ★ v172 缺陷②（断点 A）：**攻击片段随武器** ★
                //
                // 资产是 `player_attack_hoe` / `_can` / `_fert`，**没有** `player_attack` ✗
                // 而 `ModelView` 是按"状态 → 一个片段名"加载的（`ClipNames[2] = "attack"`），
                // 于是玩家要的 `Art/AnimMerged/player_attack` 取不到 ⇒ `_clips[2] == null`
                // ⇒ 攻击静默回退待机 ✓
                // 这里把三段一次性交给 `ModelView`，由它按当前武器索引选 ✓
                //
                // 后缀表与 `AttachTools` 的 `ids` **同序**（hoe / can / fert），
                // 也与 `PlayerActor.WeaponHoe/Can/Fertilizer` = 0/1/2 同序 ✓
                _playerModel.SetAttackClips(new[]
                {
                    ModelView.LoadClip("player", "attack_hoe"),
                    ModelView.LoadClip("player", "attack_can"),
                    ModelView.LoadClip("player", "attack_fert"),
                });
                // 对齐"已经显示"的那一把：`AttachTools` 显示的是 0 号（锄头）✓
                // 不写这一句的话，第一帧会被误判成"换了武器"而白播一次切换动作 ✗
                _shownWeapon = 0;
                _pendingAttack = -1;
                _playerModel.SetAttackWeapon(0);
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
            SyncScenery(dt);          // v64：装饰随风摆动
            TickSwings(dt);           // v66：挥击弧的扫过动画
            SyncPlayer(w, dt);
            SyncShopTruck(s, dt);      // v157：商店车（只读会话状态，不持有状态机）
            // v74：火焰光跟随玩家（夜里唯一的稳定近场光源）
            if (NightLighting.Instance != null)
                NightLighting.Instance.FollowPlayer(new Vector3(w.Player.Pos.x, 0f, w.Player.Pos.y), dt);
            SyncBlastReticle(s);
            SyncEnemies(w, dt);
            TickBlasts(dt);
            TickWarnMarkers(dt);
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
            if (mature != null) { mature.SetNeon(neon); _plotCrops[i] = mature; }
            ModelView young = ModelView.Spawn("Plants", seedId + "_young", _root, false);
            if (young != null) { young.SetNeon(neon); _plotYoungs[i] = young; }
            // v103：第三档「成长中」。取不到就留 null —— 选择逻辑会退回用成体，
            // 也就是**旧行为**，不会因为缺这一个模型就整块地不显示。
            ModelView grownMv = ModelView.Spawn("Plants", seedId + "_grown", _root, false);
            if (grownMv != null) { grownMv.SetNeon(neon); _plotGrowns[i] = grownMv; }

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
                        bool young = gp < 0.55f;
                        bool mid = !young && _plotGrowns[i] != null;
                        ModelView show = young ? _plotYoungs[i] : (mid ? _plotGrowns[i] : _plotCrops[i]);
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
                            float baseK = young
                                ? Mathf.Lerp(0.45f, 0.75f, gp / 0.55f)
                                : Mathf.Lerp(0.78f, 1f, (gp - 0.55f) / 0.45f);
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
                            show.ScaleToHeight(tile * 0.72f * baseK * breath);
                            // v101：**成长中的作物也加品质色霓虹**（需求）。
                            // 旧版只有场上的参战帮手有霓虹，地里的作物没有——
                            // 于是玩家站在自己田边，看不出哪一株是高品质种子长出来的，
                            // 要等收割后才在帮手身上看到颜色。品质信息应该**在种下去的那一刻**就可读。
                            show.SetNeon(NeonOf(Projectile.GradeColor(SeedGradeOf(plot.CropSeedId))));
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
            }
        }

        private static void SetColor(GameObject go, Color c)
        {
            if (go == null) return;
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            Material m = r.sharedMaterial;
            if (m == null) return;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
            m.color = c;
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

            bool visible = t.Current != ShopTruck.Phase.Idle;
            if (_truckModel.Go.activeSelf != visible) _truckModel.Go.SetActive(visible);
            if (!visible) return;

            // 路径是归一化坐标（-1..1），乘场地半宽得到世界坐标 ✓
            // ★ 必须与 `RunSession.TickShopTruck` 用**同一个取位入口** ★ ——
            //   否则会出现"看得见的车在 A、能交互的车在 B" ⇒ 界面在撒谎 ✗
            //   停靠期 `SamplePoint` 返回 `DockPoint`（车真的停住），
            //   而 `PointAt(PhaseT)` 会让它又跑一整圈 ✗（见 `ShopTruck.SamplePoint`）
            float half = TableDatabase.Tuning.farm.arenaHalf;
            float prog = t.SampleT(s.Clock);
            Vector2 np = t.SamplePoint(s.Clock);
            _truckModel.Go.transform.position = new Vector3(np.x * half, 0f, np.y * half);

            // 车头朝 Unity +Z（与 Characters/ 一致），SetFacing 组合出朝向 ✓
            Vector2 d = ShopTruck.DirAt(prog);
            if (d.sqrMagnitude > 0.001f)
                _truckModel.SetFacing(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
        }

        private ModelView _truckModel;

        private void SyncPlayer(World w, float dt)
        {
            PlayerActor p = w.Player;
            if (p == null || _player == null) return;

            if (_playerModel != null)
            {
                Vector3 pos = new Vector3(p.Pos.x, 0f, p.Pos.y);
                bool moving = Moved(-1, pos, dt);
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
                // ══════════════════════════════════════════════════════════════════
                if (_pendingAttack >= 0)
                {
                    // ★ 只在"上一段攻击已经播完"时重开 ★
                    // 水壶是**按住持续**的：`TickBeam → FireBeam` 每个固定帧投递一次 `BeamEvent`，
                    // 无条件重开会把动画永远钉在第 0 帧（看起来就是"不动"）✗
                    // 播完之后下一个事件自然重开 ⇒ 持续激光就是**反复播**攻击动作，这正是要的效果 ✓
                    if (_playerModel.Current != ModelView.State.Attack)
                    {
                        _playerModel.SetAttackWeapon(_pendingAttack);
                        _playerModel.PlayForced(ModelView.State.Attack);
                    }
                    _pendingAttack = -1;
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
            Vector3 playerPos = w.Player != null ? new Vector3(w.Player.Pos.x, 0f, w.Player.Pos.y) : Vector3.zero;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
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
                        mv.SetScaleFactor(CharScale);
                        // v76：只有真正的敌人才套霓虹红描边。
                        // 小猪（Wander）是中立的肥料来源，给它红边会让玩家以为要打它。
                        if (e.Def.Kind != EnemyKind.Wander) mv.SetNeon(NeonEnemy);
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
                        ? VfxMat(new Color(0.45f, 0.80f, 1f, 0.95f), new Color(0.35f, 0.85f, 1f, 1f))
                        : Mat(tint, true);
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
                    go.transform.localScale = Vector3.one * (size * pulse);
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
                if (away.sqrMagnitude < 0.25f) _pendingAttack = PlayerActor.WeaponCan;
            }

            Vector3 from = new Vector3(e.From.x, 0.7f, e.From.y);
            Vector3 to = new Vector3(e.To.x, 0.7f, e.To.y);
            Vector3 mid = (from + to) * 0.5f;
            float len = Vector3.Distance(from, to);
            if (len < 0.2f) return;
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
            float visualW = Mathf.Clamp(w * 0.16f, 0.07f, 0.30f);

            GameObject go = Prim(PrimitiveType.Cube, "Laser", mid,
                new Vector3(visualW, visualW, len), Mat(new Color(0.4f, 0.95f, 1f), true));
            go.transform.LookAt(to);
            // v47：删掉旧版在激光正中点叠的那枚半径 0.6 的青色圆盘（AddMarker）——
            //      它既不在起点也不在终点，看起来像"凭空出现的圆形判定区"，是玩家反馈的"异常圆形"。
            Object.Destroy(go, 0.12f);
        }

        /// <summary>v38：肥料炸弹爆炸——橙色扩散圈。</summary>
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

            AddMarker(e.Pos, e.Radius, e.Duration, new Color(1f, 0.45f, 0.15f, 1f));
        }

        private void OnFertBlast(FertilizerBlastEvent e)
        {
            // v42：爆炸表现——橙色冲击环（从 0 扩散到半径，0.35 秒）+ 中心闪光 + 震屏
            AddMarker(e.Pos, e.Radius, 0.35f, new Color(1f, 0.55f, 0.15f, 1f));
            GameObject ring = Prim(PrimitiveType.Cylinder, "BlastRing",
                new Vector3(e.Pos.x, 0.12f, e.Pos.y), new Vector3(0.4f, 0.04f, 0.4f),
                VfxMat(new Color(1f, 0.62f, 0.2f, 0.9f), new Color(1f, 0.45f, 0.1f, 0.8f)));
            _blasts.Add(ring);
            _blastT.Add(0f);
            _blastR.Add(e.Radius);
            GameObject core = Prim(PrimitiveType.Sphere, "BlastCore",
                new Vector3(e.Pos.x, 0.7f, e.Pos.y), Vector3.one * (e.Radius * 0.55f),
                VfxMat(new Color(1f, 0.85f, 0.35f, 0.85f), new Color(1f, 0.6f, 0.15f, 0.9f)));
            _blasts.Add(core);
            _blastT.Add(0f);
            _blastR.Add(e.Radius);

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

            Shake(0.22f, 0.16f);

            // v74：爆炸是全场最亮的一瞬——大范围暖橙光，夜里这一下能照亮半个场地
            NightLighting.Illuminate(new Vector3(e.Pos.x, 0.8f, e.Pos.y),
                new Color(1f, 0.62f, 0.24f), e.Radius * 1.6f, 6.5f, 0.32f);
        }

        private readonly List<GameObject> _blasts = new List<GameObject>();
        private readonly List<float> _blastT = new List<float>();
        private readonly List<float> _blastR = new List<float>();

        /// <summary>v42：爆炸扩散动画（0.3 秒内从 0 扩到半径并淡出）。</summary>
        private void TickBlasts(float dt)
        {
            for (int i = _blasts.Count - 1; i >= 0; i--)
            {
                _blastT[i] += dt;
                float k = Mathf.Clamp01(_blastT[i] / 0.3f);
                GameObject go = _blasts[i];
                if (go != null)
                {
                    float s = Mathf.Lerp(0.4f, _blastR[i] * 2f, k);
                    go.transform.localScale = new Vector3(s, Mathf.Max(0.03f, 0.04f * (1f - k)), s);
                    var rend = go.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        Color c = rend.material.color;
                        c.a = 1f - k;
                        rend.material.color = c;
                    }
                }
                if (k < 1f) continue;
                if (go != null) Object.Destroy(go);
                _blasts.RemoveAt(i); _blastT.RemoveAt(i); _blastR.RemoveAt(i);
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
                    _markers.RemoveAt(i);
                    continue;
                }
                float k = Mathf.Clamp01(m.Ttl / Mathf.Max(0.01f, m.Total));
                Vector3 s = m.Go.transform.localScale;
                m.Go.transform.localScale = new Vector3(s.x, s.y, s.z);
                Color c = m.Go.GetComponent<Renderer>().sharedMaterial.color;
                c.a = k;
                SetColor(m.Go, c);
            }
        }

        private void AddMarker(Vector2 pos, float radius, float duration, Color color)
        {
            Material mat = Mat(color);
            GameObject go = Prim(PrimitiveType.Cylinder, "Marker",
                new Vector3(pos.x, 0.12f, pos.y), new Vector3(radius * 2f, 0.04f, radius * 2f), mat);
            _markers.Add(new Marker { Go = go, Ttl = duration, Total = duration });
        }

        /// <summary>v20：敌人出生预警——原地先出现一个闪烁标记，2 秒后才真正生成敌人。</summary>
        private void OnSpawnWarning(SpawnWarningEvent e)
        {
            GameObject go = Prim(PrimitiveType.Cylinder, "SpawnWarning",
                new Vector3(e.Pos.x, 0.06f, e.Pos.y),
                new Vector3(1.5f, 0.03f, 1.5f),
                Mat(new Color(1f, 0.35f, 0.25f, 1f), true));
            _warnMarkers.Add(go);
            _warnTimers.Add(Mathf.Max(0.2f, e.Seconds));
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

        /// <summary>近战判定框特效：沿挥击方向的一块扁平方形（临时表现，后续换成扇形网格）。</summary>
        /// <summary>
        /// v66：挥击弧的程序化网格——一个扇形环（内半径 0.55、外半径 1.0，张角 110°），
        /// 单位尺度，靠实例缩放对到武器射程。
        ///
        /// 旧版是一块静止的方板（Capsule 一样的 Cube）闪 0.14 秒：
        /// 既读不出"挥"的方向，也读不出范围。扇形 + 扫过动画才把这两件事说清楚。
        /// 网格只建一次、所有挥击共用。
        /// </summary>
        private static Mesh _arcMesh;

        private static Mesh ArcMesh()
        {
            if (_arcMesh != null) return _arcMesh;
            const int segs = 18;
            const float inner = 0.55f, outer = 1.0f;
            float half = 55f * Mathf.Deg2Rad;
            var verts = new Vector3[(segs + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segs * 6];
            for (int i = 0; i <= segs; i++)
            {
                float a = -half + (2f * half) * (i / (float)segs);
                float s = Mathf.Sin(a), c = Mathf.Cos(a);
                verts[i * 2] = new Vector3(s * inner, 0f, c * inner);
                verts[i * 2 + 1] = new Vector3(s * outer, 0f, c * outer);
                uvs[i * 2] = new Vector2(0f, i / (float)segs);
                uvs[i * 2 + 1] = new Vector2(1f, i / (float)segs);
            }
            for (int i = 0; i < segs; i++)
            {
                int b = i * 2;
                tris[i * 6 + 0] = b; tris[i * 6 + 1] = b + 2; tris[i * 6 + 2] = b + 1;
                tris[i * 6 + 3] = b + 1; tris[i * 6 + 4] = b + 2; tris[i * 6 + 5] = b + 3;
            }
            var m = new Mesh { name = "MeleeArcMesh" };
            m.vertices = verts;
            m.triangles = tris;
            m.uv = uvs;
            m.RecalculateNormals();
            m.RecalculateBounds();
            _arcMesh = m;
            return m;
        }

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

            GameObject go = new GameObject("MeleeArc");
            go.transform.SetParent(_root, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = ArcMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            // v70：挥击弧也走 VFX 材质——置顶，才不会被近处草丛盖掉
            mr.sharedMaterial = VfxMat(new Color(1f, 0.96f, 0.62f, 0.92f), new Color(1f, 0.85f, 0.35f, 0.7f));
            go.transform.position = new Vector3(e.Pos.x, 0.50f, e.Pos.y);
            // v74：挥击在近场掀一层暖光，让"挥"这个动作在夜里也读得到
            NightLighting.Illuminate(go.transform.position, new Color(1f, 0.88f, 0.5f), 3.4f, 2.2f, 0.14f);
            _swings.Add(new Swing
            {
                Go = go,
                T = 0f,
                Total = DebugSwingHold > 0.01f ? DebugSwingHold : 0.20f,
                Dir = e.Dir,
                Range = Mathf.Max(0.8f, e.Range),
                ArcDeg = Mathf.Max(40f, e.ArcDeg)
            });
        }

        private void OnBoss(BossEvent e)
        {
            if (e.Kind == "clear_field") AddMarker(e.Pos, 10f, 0.4f, new Color(0.7f, 0.3f, 1f, 1f));
            else if (e.Kind == "summon") AddMarker(e.Pos, 3f, 0.3f, new Color(0.9f, 0.4f, 1f, 1f));
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
}
