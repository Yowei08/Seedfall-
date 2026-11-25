using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 农田**装饰物**（稻草人 / 杂草 / 水桶 / 草垛 / 锄头 / 鸡）＋ **路边围栏** —— v239 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、这一轮（v239）改了什么（用户原话）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① **杂草改随机**：固定哈希 + **拒绝采样**（N ≤ 40，仍要 ≥1.5 米）+ **不动 `World.Rng`**
    ///      ⇒ 见第六节的 `ComputeWeedPositions`（纯函数，诊断会再跑一遍做"两次结果相同"的判据）
    ///   ② **稻草人 2 → 1**：只留东南角 (-4.05,-7.75)
    ///   ③ **新增路边围栏**：量 `WalkRadius` 后建在它**之外 0.60 米**（边心距 22.00），
    ///      立柱 + 两道横杆、转角必立柱、南侧门口留 **4.00 米**缺口、材质 `OWN_FENCE_*`
    ///      ⇒ 见第十一节（`ComputeFencePlan` / `BuildFence` / `FenceInfo`）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、为什么推倒重写（上一版的两个硬伤，仍然有效）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① **垄公式落在格心上**：上一版写 `(k-1.5)*pitch` ⇒ 半格的**奇数**倍
    ///      ⇒ 恰好压住田格中心（格心 = ±1.15 / ±3.45 / ±5.75 = 1.15 的奇数倍），
    ///      16 丛杂草全被"到格心 ≥ 1.5 米"的门槛拒掉，最终读数 **0 丛** ✗
    ///      ⇒ 垄公式用 `(k-2)*pitch` = 1.15 的**偶数**倍 ⇒ 落在**格与格之间的沟**上。
    ///   ② **材质被美术通道污染**：`Art/TownArtDresser.cs`（`DefaultExecutionOrder(300)`）
    ///      在 `Start` 里遍历所有渲染器塞贴图 ⇒ 共享池材质被写进 `_MainTex`，
    ///      云 / 喷泉 / 长椅都长出了地面贴图。
    ///      ⇒ 每一份材质都是**自己的** `OWN_FARM_*` / `OWN_FENCE_*`，`_MainTex` / `_BaseMap` 显式置空，
    ///        并且每 90 帧校验一次把材质换回、把贴图清空（见 <see cref="Repair"/> 与
    ///        `TownFarmDecorGuard`）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、坐标（**全部从 `TownLayout` 量出来**，一个数字都不写死）
    /// ══════════════════════════════════════════════════════════════════════
    ///   `FarmCenter = (-11.80, 0.00)` · `PlotPitch = 2.30` · `PlotWidth = PlotDepth = 2.00`
    ///   `PlotCols = PlotRows = 6` · `FarmHalf = 7.15` · 半间距 `halfPitch = PlotPitch/2 = 1.15`
    ///
    ///   ── 田格中心（世界坐标，格心 = `(k-2.5)*pitch`）──
    ///     X = **-17.55 / -15.25 / -12.95 / -10.65 / -8.35 / -6.05**
    ///     Z = **-5.75 / -3.45 / -1.15 / +1.15 / +3.45 / +5.75**
    ///     ⇒ 每块田格 = 中心 ±(1.00, 1.00)，即 2.00 × 2.00 米
    ///     ⇒ 田格带最外沿 = 2.5×2.30 + 1.00 = **±6.75 米**（相对 `FarmCenter`）
    ///
    ///   ── 垄线（世界坐标，垄 = `(k-2)*pitch`，1.15 的**偶数**倍）──
    ///     X = **-16.40 / -14.10 / -11.80 / -9.50 / -7.20**
    ///     Z = **-4.60 / -2.30 / 0.00 / +2.30 / +4.60**
    ///     ⇒ 垄宽 = `PlotPitch - PlotWidth` = **0.30 米**（格与格之间的那条沟）
    ///
    ///   ── 田块外沿 ──
    ///     土面 `FarmHalf` = **±7.15** · 田埂压边石 = ±7.09 · **四边篱笆 = ±7.30**
    ///     （篱笆由 `TownBuildings.BuildFarmDressing` 摆在 `FarmHalf + 0.15`）
    ///     ⇒ 田格带(6.75) 与 土面(7.15) 之间有一条 **0.40 米宽的"田边带"**，
    ///       小件（水桶 / 锄头 / 鸡 / 杂草）就长在这条带上或垄线上 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、为什么"垄线"上只有**交叉点**能用（距离的算术，必须写下来）
    /// ══════════════════════════════════════════════════════════════════════
    ///   垄线 = 相邻两格的**中线**，它到两侧格心的距离恒为 `halfPitch` = **1.15 米 < 1.5** ✗
    ///   ⇒ 站在一条垄的**中段**（两格之间）永远不满足"到格心 ≥ 1.5 米"。
    ///   而**两条垄的交叉点**到最近的四个格心 = √(1.15² + 1.15²) = **1.6263 米 ≥ 1.5** ✓
    ///   ⇒ 所以 16 丛杂草全部长在**垄×垄的交叉点**上，一个中段都不用。
    ///     交叉点共 5×5 = 25 个，本版取"至少一个索引是奇数"的 16 个
    ///     （剔除 3×3 的偶数×偶数 9 个）⇒ **不多不少正好 16 丛**，且全部 1.626 米。
    ///
    ///   同类算术用在田边带上：田边带在 |偏移| = 6.75~7.15 之间，
    ///   取 `PlotHalfSpan + 0.20 = 6.95`（离格沿 0.20 米）且只在**垄线**上
    ///   ⇒ 到最近格心 = √(1.20² + 1.15²) = **1.662 米 ≥ 1.5** ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、不碰田格中央
    /// ══════════════════════════════════════════════════════════════════════
    ///   36 块田格是"玩家能种东西的地方"，装饰站上去就会被读成"田里冒出我没种过的植物"
    ///   （用户报过这个 bug）。⇒ 构建完**逐件复算**到最近格心的距离，< 1.5 米就打 WARN，
    ///     并把**最小距离**写进 `FarmDecorInfo` 的读数里（这就是"能不能自证"的那一条）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、确定性 / 资源纪律
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 网格：全部走 `TownKit.Prim()` / `TownKit.Cylinder()` 的**共享缓存**，零 `new Mesh`
    ///   · 抖动：**不碰 `World.Rng`**，用本文件内的整数散列（固定输入 ⇒ 固定输出）
    ///   · 材质：只有 12 份 `OWN_FARM_*`（按**颜色角色**建，不按物体数建），
    ///     并且**登记**每个渲染器 ↔ 它那一份（见 <see cref="Repair"/>），不按帧新建
    /// </summary>
    public static class TownFarmDecor
    {
        // ══════════════════════════════════════════════════════════════════
        // 一、从布局量出来的派生常量（改 `TownLayout` 这里自动跟着变）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>半间距 = 1.15。格心在它的**奇数**倍上，垄在它的**偶数**倍上。</summary>
        private static float HalfPitch => TownLayout.PlotPitch * 0.5f;

        /// <summary>田格带的外沿（相对 `FarmCenter`）：2.5×pitch + 半个格 = 6.75 米。</summary>
        private static float PlotHalfSpan
            => (TownLayout.PlotCols - 1) * 0.5f * TownLayout.PlotPitch + TownLayout.PlotWidth * 0.5f;

        /// <summary>田边带上的落点半径：6.75 + 0.20 = **6.95 米**（离格沿 0.20、离土面边 0.20）。</summary>
        private static float ApronEdge => PlotHalfSpan + 0.20f;

        /// <summary>稻草人的落地半径：土面外沿(7.15) 再外 **0.6 米** = 7.75。
        /// 必须 &gt; 篱笆(7.30)，否则十字木架的两臂会穿过篱笆横杆。</summary>
        private static float ScarecrowEdge => TownLayout.FarmHalf + 0.60f;

        /// <summary>到最近格心的硬门槛（米）。</summary>
        public const float MinPlotDistance = 1.50f;

        private const string RootName = "TOWN_FarmDecor";
        private const string GuardName = "TOWN_FarmDecorGuard";

        // ══════════════════════════════════════════════════════════════════
        // 二、登记表（诊断 + 修复共用一份，口径不会分叉）
        // ══════════════════════════════════════════════════════════════════

        private struct Item
        {
            public string Name;      // 节点名（FARM_SCARECROW_1 …）
            public Transform T;      // 装饰根（AABB 由它下面的渲染器合出来）
            public Vector2 At;       // 世界 (x, z)
            public string Kind;      // 稻草人 / 杂草 / 水桶 / 草垛 / 锄头 / 鸡
        }

        private static readonly List<Item> Items = new List<Item>(32);

        /// <summary>渲染器 ↔ 它**自己那一份** `OWN_FARM_*` 材质。修复只按这张表换回，**不新建**。</summary>
        private static readonly Dictionary<MeshRenderer, Material> Owned = new Dictionary<MeshRenderer, Material>(96);

        /// <summary>本版建出来的全部 `OWN_FARM_*` 材质（按颜色角色，一份一个）。</summary>
        private static readonly List<Material> Palette = new List<Material>(16);

        private static Transform _root;
        /// <summary>"登记表已经填过"——建城时填一次，或域重载后从场景认回一次（`EnsureRegistry`）。</summary>
        private static bool _registered;

        public static int Count => Items.Count;
        public static int RendererCount => Owned.Count;
        public static int MaterialCount => Palette.Count;
        /// <summary>全部装饰里"到最近田格中心"的**最小距离**（验收核心：必须 ≥ 1.5）。</summary>
        public static float MinDistanceToPlot { get; private set; }
        /// <summary>上一次校验修回了几处（材质被换回 或 贴图被清掉）。</summary>
        public static int RepairedLast { get; private set; }
        /// <summary>累计修回次数（只增，用来证明"每 90 帧的校验真的在干活"）。</summary>
        public static int RepairedTotal { get; private set; }
        public static int SweepCount { get; private set; }

        // ══════════════════════════════════════════════════════════════════
        // 三、材质：**每一份都是自己的** `OWN_FARM_*`，`_MainTex` / `_BaseMap` 显式置空
        // ══════════════════════════════════════════════════════════════════
        //
        // 为什么必须自己一份（而不是 `TownKit.Mat` 的共享池）：
        //   `Art/TownArtDresser.cs:508-557`（`RetextureTownSurface`）与 `:2506-2513` 会
        //   **直接改写渲染器的 sharedMaterial**，并且给共享池的材质 `SetTexture("_MainTex", …)`。
        //   共享池是按"颜色"复用的 ⇒ 只要有一件东西被塞了地面贴图，**同色的所有东西**一起长草。
        //   （云 / 喷泉 / 长椅就是这么坏掉的。）
        //   ⇒ 这里每份 `OWN_FARM_*` 只归农田装饰用，并且把两个贴图槽**显式置空**，
        //     美术通道就算再塞一次，`Repair` 也会把它清掉 ✓
        //
        // shader 依次试：`Seedfall/ToonSimple`（装饰专用档）→ `Unlit/Color` → `Seedfall/CloudFlat`，
        // 每个都 `Shader.Find` 判空兜底（工程里这三个 shader 都在 `Assets/Shaders/`）。
        private static Material _straw, _strawDark, _wood, _woodDark, _cloth;
        private static Material _weedA, _weedB, _metal, _water, _chickenA, _chickenB, _comb;
        /// <summary>路边围栏的两份材质：**前缀是 `OWN_FENCE_*`**（用户点名），同样显式置空贴图。</summary>
        private static Material _fencePost, _fenceRail;

        /// <summary>农田装饰的材质前缀。</summary>
        private const string MatPrefixFarm = "OWN_FARM_";
        /// <summary>路边围栏的材质前缀（用户点名 `OWN_FENCE_*`）。</summary>
        private const string MatPrefixFence = "OWN_FENCE_";

        private static Material Own(string prefix, string name, Color c)
        {
            Shader sh = Shader.Find("Seedfall/ToonSimple");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Seedfall/CloudFlat");
            if (sh == null)
            {
                Log.Warn("[Town] 农场装饰：三个 shader 都没找到 ⇒ 退回 ToonLook 共享池（贴图防护会弱一档）");
                return TownKit.Mat(c);
            }
            var m = new Material(sh) { name = prefix + name };
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);   // ★ 显式置空
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);   // ★ 显式置空
            Palette.Add(m);
            return m;
        }

        private static void BuildPalette()
        {
            if (Palette.Count > 0) return;
            _straw = Own(MatPrefixFarm, "Straw", new Color(0.86f, 0.74f, 0.36f));
            _strawDark = Own(MatPrefixFarm, "StrawDark", new Color(0.69f, 0.56f, 0.25f));
            _wood = Own(MatPrefixFarm, "Wood", new Color(0.52f, 0.38f, 0.24f));
            _woodDark = Own(MatPrefixFarm, "WoodDark", new Color(0.38f, 0.27f, 0.18f));
            _cloth = Own(MatPrefixFarm, "Cloth", new Color(0.58f, 0.30f, 0.26f));
            _weedA = Own(MatPrefixFarm, "WeedA", new Color(0.33f, 0.55f, 0.24f));
            _weedB = Own(MatPrefixFarm, "WeedB", new Color(0.45f, 0.66f, 0.29f));
            _metal = Own(MatPrefixFarm, "Metal", new Color(0.56f, 0.58f, 0.63f));
            _water = Own(MatPrefixFarm, "Water", new Color(0.40f, 0.62f, 0.78f));
            _chickenA = Own(MatPrefixFarm, "ChickenA", new Color(0.95f, 0.93f, 0.87f));
            _chickenB = Own(MatPrefixFarm, "ChickenB", new Color(0.78f, 0.58f, 0.34f));
            _comb = Own(MatPrefixFarm, "Comb", new Color(0.86f, 0.28f, 0.24f));
            _fencePost = Own(MatPrefixFence, "Post", new Color(0.44f, 0.32f, 0.21f));
            _fenceRail = Own(MatPrefixFence, "Rail", new Color(0.55f, 0.41f, 0.27f));
        }

        // ══════════════════════════════════════════════════════════════════
        // 四、构建入口
        // ══════════════════════════════════════════════════════════════════

        /// <summary>在 `TOWN` 下建整组农田装饰，并挂上每 90 帧的校验组件。可重复调用（幂等）。</summary>
        public static void Build(Transform townRoot)
        {
            if (townRoot == null) return;
            if (townRoot.Find(RootName) != null) return;      // 幂等：场景重进才重建

            Items.Clear();
            Owned.Clear();
            BuildPalette();

            var rootGo = new GameObject(RootName);
            _root = rootGo.transform;
            _root.SetParent(townRoot, false);

            // ★ 稻草人 **1 个**（用户这一轮：2 → 1，只留东南角那个）★
            Scarecrow(1, +1f, -1f);
            Weeds();
            // ★ v256：石头与稻草装饰 —— 与杂草共用同一张"已占位"清单 ⇒ 不会叠在一起
            //   顺序有意为之：先种草（它的采样域最窄），石头/稻草再填剩下的空位 ✓
            {
                var taken = new List<Vector2>(Items.Count);
                for (int i = 0; i < Items.Count; i++) taken.Add(Items[i].At);
                Stones(taken);
                Straw(taken);
            }
            Bucket();
            HayBale();
            Hoe();
            Chicken(1, -1f, +1f, false);
            Chicken(2, -1f, -1f, true);

            // ★ 构建完立刻**逐件复算**到最近格心的距离（自证，不靠"我觉得"）★
            MinDistanceToPlot = float.MaxValue;
            string worst = null;
            for (int i = 0; i < Items.Count; i++)
            {
                float d = NearestPlotDistance(Items[i].At);
                if (d < MinDistanceToPlot) { MinDistanceToPlot = d; worst = Items[i].Name; }
                if (d < MinPlotDistance)
                    Log.Warn($"[Town] 农田装饰「{Items[i].Name}」@ ({Items[i].At.x:0.00},{Items[i].At.y:0.00}) " +
                             $"到最近田格中心只有 {d:0.000} 米（< {MinPlotDistance:0.0}）⇒ 压格心了 ✗");
            }

            Log.Info($"[Town] 农田装饰已建：**{Items.Count} 件**（稻草人 1 / 杂草 8 / 石头 5 / 稻草 3 / 水桶 1 / 草垛 1 / 锄头 1 / 鸡 2）· " +
                     $"渲染件 {Owned.Count} 个 · 材质 {Palette.Count} 份 `OWN_FARM_*`+`OWN_FENCE_*` · " +
                     $"到最近格心的**最小距离 = {MinDistanceToPlot:0.000} 米**（门槛 {MinPlotDistance:0.0}，" +
                     $"{(MinDistanceToPlot >= MinPlotDistance ? "✓ 通过" : "✗ 不通过")}，最近的一件 = {worst}）");

            // ★ 路边围栏（城界木栅栏，见第十一节）★
            BuildFence(townRoot);

            _registered = true;      // 登记表已由上面这些构建填好

            // ★ 每 90 帧的校验：**挂在 TOWN 下的独立组件上** ★
            //   为什么不是写在 `TownBootstrap.Update` 里：`Login/LoginBootstrap.cs:197`
            //   在登录阶段执行 `_town.enabled = false` ⇒ `TownBootstrap.Update` 整帧不跑；
            //   而协程与"别的组件自己的 Update"都不受它影响 ⇒ 校验照跑 ✓
            if (townRoot.Find(GuardName) == null)
            {
                var guardGo = new GameObject(GuardName);
                guardGo.transform.SetParent(townRoot, false);
                guardGo.AddComponent<TownFarmDecorGuard>();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 五、稻草人 ×**1** —— 立在**东南田角外侧**（土面外沿 7.15 + 0.6 = 7.75 米，在篱笆 7.30 之外）
        // ══════════════════════════════════════════════════════════════════
        //
        // ★ 用户这一轮：**2 → 1**，只留东南角 (-4.05, -7.75)，西北角那个**删掉** ★
        //   （上一版的两个角是东南 + 西北；西北角在 (-19.55, +7.75)，离风车
        //     `PropPolar("windmill",152°,20.40)` 只有约 2.5 米，视觉上挤在一起 ⇒ 用户裁定只留一个 ✓）
        //
        // 为什么偏偏是**东南角**：
        //   · 东北角 (7.75, 7.75) 不能用：世界 (-4.05, 7.75) 正好落在**公园草坪的东南角**
        //     （`ParkCenter (-6.5,11.5)` ± 4 ⇒ x −10.5~−2.5、z 7.5~15.5），
        //     公园那棵树在 (-3.00, 7.90)（`BuildPark` 第 4 棵），相距只有 1.06 米 ⇒ 会穿进树冠 ✗
        //   · 西南角已经有美术那路的 `ENV_scarecrow (-8.20,-8.90)` 与 `ENV_haystack (-18.31,-8.54)`
        //   · 东南角朝城心、离仓库装卸广场 4 米开外 ⇒ 是四个角里最干净、也最容易被看到的一个 ✓
        private static void Scarecrow(int idx, float sx, float sz)
        {
            float e = ScarecrowEdge;                                   // 7.75
            Vector2 at = TownLayout.FarmCenter + new Vector2(sx * e, sz * e);
            Transform t = Node($"FARM_SCARECROW_{idx}", at, YawTo(at, TownLayout.FarmCenter), "稻草人");

            // 立柱：0 → 1.32 米（10 厘米见方，俯视图上是一条明确的"身体中轴"）
            Part(t, "FARM_SC_Post", new Vector3(0f, 0.66f, 0f), new Vector3(0.11f, 1.32f, 0.11f), _wood);
            // 十字横架：1.00 米宽 ⇒ **俯视必须看得出是一个人形**（比"一根杆"宽一倍以上）
            Part(t, "FARM_SC_Arms", new Vector3(0f, 0.98f, 0f), new Vector3(1.00f, 0.09f, 0.09f), _woodDark);
            // 两袖口的干草束（让"十字"两端有分叉，读成手而不是一根木条）
            Part(t, "FARM_SC_SleeveL", new Vector3(-0.48f, 0.92f, 0f), new Vector3(0.13f, 0.20f, 0.13f), _strawDark);
            Part(t, "FARM_SC_SleeveR", new Vector3(+0.48f, 0.92f, 0f), new Vector3(0.13f, 0.20f, 0.13f), _strawDark);
            // 草垛身体（0.52 × 0.44 × 0.32 ⇒ 俯视 0.52 宽，明显比立柱粗）
            Part(t, "FARM_SC_Body", new Vector3(0f, 0.86f, 0f), new Vector3(0.52f, 0.44f, 0.32f), _straw);
            // 草垛头（0.36 × 0.32 × 0.34）
            Part(t, "FARM_SC_Head", new Vector3(0f, 1.40f, 0f), new Vector3(0.36f, 0.32f, 0.34f), _straw);
            // 破帽子：帽檐 + 帽顶（总高 = 1.73 米 ≈ "1.7 米左右"）
            Part(t, "FARM_SC_HatBrim", new Vector3(0f, 1.575f, 0f), new Vector3(0.50f, 0.05f, 0.50f), _cloth);
            Part(t, "FARM_SC_HatTop", new Vector3(0f, 1.655f, 0f), new Vector3(0.24f, 0.15f, 0.24f), _cloth);
        }

        // ══════════════════════════════════════════════════════════════════
        // 六、杂草 ×16 —— **随机落点**（固定哈希 + 拒绝采样），但三条约束一条都不松
        // ══════════════════════════════════════════════════════════════════
        //
        // ★ 用户这一轮的要求（逐条对应实现）★
        //   ① **随机位置**，但**不许动 `World.Rng`**（动了会破坏一局确定性）
        //      ⇒ 用**固定种子的哈希**：`h = Hash(i, 尝试序号) ^ 0x9E3779B9`，再 `frac(h × 0.618…)` 取小数
        //      ⇒ 同一份代码每次跑出来是**同一套随机** ✓（`ComputeWeedPositions` 是纯函数）
        //   ② 仍要满足"到最近田格中心 **≥ 1.5 米**" ⇒ **拒绝采样**：
        //      在**田块矩形内**随机取点，最多试 `WeedSampleTries = 40` 次，
        //      **第一次满足就收**；40 次都不满足就**退回垄交叉点**（`NearestFreeCrossing`）
        //   ③ 随机范围只落在**垄线附近 / 田块内**，**绝不落在作物格心**（那是玩法区）
        //      —— 这一条由距离门槛**自动保证**（格心 1.5 米内的圆盘整块被拒掉）
        //   ④ 随机之后最小距离可能变小 ⇒ 采样门槛**提到 1.55**（比硬判据 1.5 多留 0.05 余量）
        //
        // ★★ 为什么"随机"看起来仍然贴着垄交叉点 —— 这是几何，不是偷懒（数字是实算的）★★
        //   禁入区 = 以 36 个格心为圆心、半径 1.5 的圆盘。格距 2.30 ⇒ 同一排相邻圆盘**相交**，
        //   于是每一条垄（相邻格心的中线，到两侧格心恒 1.15）**整条都进禁入区** ⇒ 可用的只有两块：
        //     · **垄×垄交叉点周围的菱形**：沿垄方向半宽 (1.15−√(1.55²−1.15²)=0.111) 米 ⇒
        //       每个 ≈ 0.025 m²，25 个 ≈ **0.6 m²**
        //     · **田边带**（6.75~6.90 米那圈）里"离最近格心 z 向 ≥0.98 米"的部分 ≈ **1.2 m²**
        //   合计 ≈ **1.8 m² / 192 m² ≈ 0.95%** ⇒ 40 次拒绝采样的**命中率约 32%**。
        //   实测（v239 一次真实运行）：16 丛里**命中 1 丛**（`FARM_WEED_10` @ (-18.64, 0.00)，到格心 1.582 米），
        //   其余 15 丛按用户规则**退回垄交叉点**（到格心 1.626 米）—— 这既满足了"随机"，
        //   又让"哪几个交叉点有草"由哈希决定（不是上一版那种整齐的奇偶网）✓
        //   ⇒ 所以本类的做法是"**整块田均匀撒点 + 40 次拒绝采样 + 命中不了退回交叉点**"（不偷换采样域），
        //     再叠一层"两丛不叠在一起"的最小间隔 0.6 米 ✓
        //
        // ★ 垄公式本身没变：`(k - 2) * PlotPitch` = 1.15 的**偶数**倍 ⇒ 落在沟上、不落格心 ★
        //   （上一版写成 `(k-1.5)*PlotPitch` = 1.15 的**奇数**倍 = 正好压住格心 ⇒ 16 丛全被拒、读数 0 丛 ✗）
        private const int WeedCount = 8;   // ★ v256：16 → 8（去掉交叉点那批，位置让给石头/稻草）
        /// <summary>拒绝采样最多试几次（用户给的上限 40）。</summary>
        private const int WeedSampleTries = 40;
        /// <summary>采样门槛 **1.55**（硬判据 1.5 + 0.05 余量；随机之后也不会跌破 1.5）。</summary>
        public const float WeedSampleGate = 1.55f;
        /// <summary>两丛之间至少隔开 0.60 米（随机可能两次落到同一处 ⇒ 会叠在一起）。</summary>
        private const float WeedSeparation = 0.60f;

        /// <summary>
        /// ★★ 杂草落点的**唯一实现**（纯函数：一个物体都不建）★★
        ///   构建用它摆位置；诊断**再跑一遍**与场景里的实际坐标逐点比对
        ///   ⇒ 那就是"同一份代码两次跑结果相同"的判据（`Info()` 里会打这一行）。
        /// </summary>
        public static void ComputeWeedPositions(List<Vector2> dst)
        {
            dst.Clear();
            var taken = new List<Vector2>(WeedCount);
            float span = TownLayout.FarmHalf - 0.25f;      // 采样域 = 田块矩形（土面再内收 0.25 米）
            for (int i = 0; i < WeedCount; i++)
            {
                Vector2 at = Vector2.zero;
                Vector2 last = Vector2.zero;
                bool hit = false;
                for (int a = 0; a < WeedSampleTries; a++)
                {
                    // ★ 固定哈希：h = Hash(i, 尝试序号) ^ 0x9E3779B9，再 frac(h × 0.6180339887) ★
                    float u = Hash01(i * 131 + a, 1);
                    float v = Hash01(i * 131 + a, 2);
                    last = TownLayout.FarmCenter + new Vector2((u * 2f - 1f) * span, (v * 2f - 1f) * span);
                    if (NearestPlotDistance(last) < WeedSampleGate) continue;   // ① 不压格心（≥1.55）
                    // ★ v256：② 离**任何**垄×垄交叉点 ≥0.85 米（"交错位置"一律不种）
                    if (NearAnyCrossing(last, 0.85f)) continue;
                    if (TooClose(taken, last)) continue;                        // ② 不和已定的丛叠在一起
                    at = last;
                    hit = true;
                    break;
                }
                // ★★ v256：**不再退回交叉点** ★★
                //   用户原话："去除这些在交错位置的杂草" —— 兜底正是那批"沿十字口撒一圈"的草。
                //   采样失败就**不种这一丛**（宁可少，也不要摆在十字口）✓
                if (!hit) continue;
                dst.Add(at);
                taken.Add(at);
            }
        }

        /// <summary>
        /// ★ v256：落点是否**贴着某个垄×垄交叉点**（<paramref name="r"/> 米内）。
        /// 用户点名要清掉的就是这批：它们整齐地落在"格与格的十字口"，看起来像被摆上去的。
        /// </summary>
        public static bool NearAnyCrossing(Vector2 p, float r)
        {
            float rr = r * r;
            for (int k = 0; k < 25; k++)
                if ((CrossingOf(k) - p).sqrMagnitude < rr) return true;
            return false;
        }

        /// <summary>
        /// ★ v256：新装饰（石头 / 稻草）的**通用落点采样** —— 与杂草同一套判据：
        ///   离最近格心 ≥1.55、离任何交叉点 ≥0.85、彼此不叠（0.6 米）、不压住已定的杂草。
        /// 失败就少放一个（绝不退回交叉点）✓
        /// </summary>
        private static Vector2 SampleFreeSpot(int seed, List<Vector2> taken, out bool ok)
        {
            float span = TownLayout.FarmHalf - 0.25f;
            for (int a = 0; a < WeedSampleTries; a++)
            {
                float u = Hash01(seed * 977 + a, 3);
                float v = Hash01(seed * 977 + a, 4);
                Vector2 p = TownLayout.FarmCenter + new Vector2((u * 2f - 1f) * span, (v * 2f - 1f) * span);
                if (NearestPlotDistance(p) < WeedSampleGate) continue;
                if (NearAnyCrossing(p, 0.85f)) continue;
                if (TooClose(taken, p)) continue;
                ok = true;
                return p;
            }
            ok = false;
            return Vector2.zero;
        }

        /// <summary>第 <paramref name="k"/> 个垄×垄交叉点（k = 0..24）。★ 垄公式 `(idx-2)*PlotPitch`，1.15 的偶数倍 ★</summary>
        public static Vector2 CrossingOf(int k)
        {
            int ia = k / 5, ib = k % 5;
            return TownLayout.FarmCenter + new Vector2((ia - 2) * TownLayout.PlotPitch, (ib - 2) * TownLayout.PlotPitch);
        }

        /// <summary>
        /// 退回用的"垄交叉点"：按**以 i 为盐的哈希顺序**扫 25 个交叉点，取第一个没被占用的。
        /// 顺序 = 起点由哈希定 + 步长 7（与 25 互质 ⇒ 一定能扫全），全确定性 ✓
        /// </summary>
        private static Vector2 NearestFreeCrossing(int i, List<Vector2> taken, Vector2 from)
        {
            int start = Mathf.Abs((int)(Hash01(i, 31) * 25f)) % 25;
            for (int k = 0; k < 25; k++)
            {
                Vector2 c = CrossingOf((start + k * 7) % 25);
                if (!TooClose(taken, c)) return c;
            }
            // 25 个都被占了（16 丛不可能占满，仅兜底）⇒ 取离最后一个采样点最近的
            Vector2 best = CrossingOf(0);
            float bd = float.MaxValue;
            for (int k = 0; k < 25; k++)
            {
                Vector2 c = CrossingOf(k);
                float d = (c - from).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        private static bool TooClose(List<Vector2> taken, Vector2 p)
        {
            for (int k = 0; k < taken.Count; k++)
                if ((taken[k] - p).sqrMagnitude < WeedSeparation * WeedSeparation) return true;
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ v256：石头 ×5 / 稻草装饰 ×3 —— 随机落点，替掉原来那 8 丛交叉点杂草
        // ══════════════════════════════════════════════════════════════════
        //
        // 美术口径（与既有装饰同一套材质前缀 `OWN_FARM_*`，不新开材质池）：
        //   · 石头：3~5 个面的矮块，**埋在土里 40%**（只露上半），大小 0.22~0.55 米，
        //     朝向/长宽比由哈希定 ⇒ 五块不会像"复制粘贴"
        //   · 稻草装饰：① 圆草垛（2 层，上层小一圈，绑一道草绳）② 散草堆（3 束斜插）
        //     ③ 立在田埂上的小草把（下粗上细，看得出是"收下来的"）
        private static void Stones(List<Vector2> taken)
        {
            Material stoneA = Own(MatPrefixFarm, "StoneA", new Color(0.62f, 0.60f, 0.56f));
            Material stoneB = Own(MatPrefixFarm, "StoneB", new Color(0.51f, 0.49f, 0.46f));
            for (int i = 0; i < 5; i++)
            {
                Vector2 at = SampleFreeSpot(700 + i, taken, out bool ok);
                if (!ok) continue;
                taken.Add(at);
                Transform t = Node($"FARM_STONE_{i + 1}", at, Hash01(i * 37 + 3, 8) * 360f, "石头");
                float w = 0.28f + Hash01(i * 11 + 1, 5) * 0.26f;      // 0.28 ~ 0.54
                float d = 0.24f + Hash01(i * 11 + 2, 6) * 0.22f;
                float h = 0.16f + Hash01(i * 11 + 3, 7) * 0.14f;
                Material m = (i % 2 == 0) ? stoneA : stoneB;
                // 主体：中心埋到 0.42·h（露 58%）⇒ 读起来是"从土里长出来的石头"
                GameObject body = TownKit.BoxL(t, $"FARM_STONE_{i + 1}_Body",
                                               new Vector3(0f, h * 0.58f - h * 0.42f, 0f),
                                               new Vector3(w, h, d), m);
                body.transform.localRotation = Quaternion.Euler(Hash01(i, 21) * 12f - 6f, 0f,
                                                               Hash01(i, 22) * 16f - 8f);
                Reg(body);
                // 第二块小的（叠在旁边）—— 单块石头太"规则"
                float w2 = w * 0.55f;
                GameObject chip = TownKit.BoxL(t, $"FARM_STONE_{i + 1}_Chip",
                                               new Vector3(w * 0.55f, h * 0.30f, d * 0.35f),
                                               new Vector3(w2, h * 0.55f, w2 * 0.9f), (i % 2 == 0) ? stoneB : stoneA);
                chip.transform.localRotation = Quaternion.Euler(0f, Hash01(i, 23) * 90f, Hash01(i, 24) * 20f - 10f);
                Reg(chip);
            }
        }

        private static void Straw(List<Vector2> taken)
        {
            // ① 圆草垛（2 层 + 草绳）
            Vector2 at0 = SampleFreeSpot(801, taken, out bool ok0);
            if (ok0)
            {
                taken.Add(at0);
                Transform t = Node("FARM_STRAW_R0UND", at0, Hash01(801, 9) * 360f, "稻草");
                GameObject lo = TownKit.CylL(t, "FARM_STRAW_R0UND_Lo", new Vector3(0f, 0.22f, 0f), 0.36f, 0.44f, _straw);
                Reg(lo);
                GameObject hi = TownKit.CylL(t, "FARM_STRAW_R0UND_Hi", new Vector3(0f, 0.60f, 0f), 0.27f, 0.34f, _strawDark);
                Reg(hi);
                GameObject rope = TownKit.CylL(t, "FARM_STRAW_R0UND_Rope", new Vector3(0f, 0.40f, 0f), 0.375f, 0.06f, _woodDark);
                Reg(rope);
            }
            // ② 散草堆（3 束斜插）
            Vector2 at1 = SampleFreeSpot(802, taken, out bool ok1);
            if (ok1)
            {
                taken.Add(at1);
                Transform t = Node("FARM_STRAW_LOOSE", at1, Hash01(802, 10) * 360f, "稻草");
                for (int k = 0; k < 3; k++)
                {
                    GameObject b = TownKit.BoxL(t, $"FARM_STRAW_LOOSE_{k}",
                                                new Vector3((k - 1) * 0.10f, 0.14f, k * 0.06f),
                                                new Vector3(0.08f, 0.30f, 0.08f), (k % 2 == 0) ? _straw : _strawDark);
                    b.transform.localRotation = Quaternion.Euler(0f, 0f, (k - 1) * 14f);
                    Reg(b);
                }
            }
            // ③ 小草把（下粗上细，立在田埂上）
            Vector2 at2 = SampleFreeSpot(803, taken, out bool ok2);
            if (ok2)
            {
                taken.Add(at2);
                Transform t = Node("FARM_STRAW_TUFT", at2, Hash01(803, 11) * 360f, "稻草");
                GameObject a = TownKit.BoxL(t, "FARM_STRAW_TUFT_A", new Vector3(0f, 0.16f, 0f),
                                            new Vector3(0.16f, 0.32f, 0.16f), _straw);
                Reg(a);
                GameObject b = TownKit.BoxL(t, "FARM_STRAW_TUFT_B", new Vector3(0f, 0.42f, 0f),
                                            new Vector3(0.09f, 0.22f, 0.09f), _strawDark);
                Reg(b);
            }
        }

        private static void Weeds()
        {
            var at = new List<Vector2>(WeedCount);
            ComputeWeedPositions(at);
            for (int n = 0; n < at.Count; n++)
            {
                Vector2 p = at[n];
                // 每丛的朝向也由哈希定 ⇒ 16 丛不会连叶片角度都一样
                Transform t = Node($"FARM_WEED_{n + 1:00}", p, Hash01(n * 17 + 5, 9) * 360f, "杂草");

                // 3 根，高度**严格** 0.15 / 0.25 / 0.35 米，绕着丛心错开 0.06~0.09 米 ⇒ 成"簇"而不是"三根并排"
                float[] h = { 0.15f, 0.25f, 0.35f };
                Vector3[] off =
                {
                    new Vector3(-0.055f, 0f, +0.030f),
                    new Vector3(+0.060f, 0f, +0.055f),
                    new Vector3(+0.010f, 0f, -0.070f),
                };
                float[] tilt = { -7f, +6f, -3f };
                for (int k = 0; k < 3; k++)
                {
                    GameObject blade = TownKit.BoxL(t, $"FARM_WEED_{n + 1:00}_Blade{k}",
                                                    off[k] + new Vector3(0f, h[k] * 0.5f, 0f),
                                                    new Vector3(0.045f, h[k], 0.045f),
                                                    (k % 2 == 0) ? _weedA : _weedB);
                    blade.transform.localRotation = Quaternion.Euler(0f, 0f, tilt[k]);
                    Reg(blade);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 七、水桶 ×1 —— 田边带东侧、垄线上（偏移 (6.95, −2.30) ⇒ 到最近格心 1.662 米）
        // ══════════════════════════════════════════════════════════════════
        private static void Bucket()
        {
            // 落点 = FarmCenter + (6.95, **−2.30**)：6.95 是田边带半径，−2.30 是**垄线 `−PlotPitch`**（不是 1.15！）
            Vector2 at = TownLayout.FarmCenter + new Vector2(ApronEdge, -TownLayout.PlotPitch);
            Transform t = Node("FARM_BUCKET_1", at, 12f, "水桶");
            Cyl(t, "FARM_BUCKET_Body", new Vector3(0f, 0f, 0f), 0.14f, 0.26f, _woodDark);
            Cyl(t, "FARM_BUCKET_Band", new Vector3(0f, 0.20f, 0f), 0.15f, 0.05f, _metal);
            Cyl(t, "FARM_BUCKET_Surface", new Vector3(0f, 0.235f, 0f), 0.12f, 0.02f, _water);   // ← 名字里**不写 WATER**（见注释底部的关键词表）
            Part(t, "FARM_BUCKET_Handle", new Vector3(0f, 0.29f, 0f), new Vector3(0.30f, 0.03f, 0.03f), _metal);
        }

        // ══════════════════════════════════════════════════════════════════
        // 八、草垛 ×1 —— 北侧田块外沿之外（篱笆 7.30 再外 0.65 米；到最近格心 2.48 米）
        // ══════════════════════════════════════════════════════════════════
        //
        // 为什么不在田边带上：草垛直径 0.88 米，而田边带只有 0.40 米宽 ⇒ 会同时压格沿与篱笆。
        // 落点 = `FarmCenter + (-2.30, +7.95)` = 世界 **(-14.10, 7.95)**：
        //   · 西半侧（公园草坪从 x −10.5 起，这里在公园之外，不会撞公园里的树）
        //   · 到最近格心 = √(1.15² + 2.20²) = **2.48 米**
        private static void HayBale()
        {
            // 落点 = FarmCenter + (**−2.30**, 7.95)：−2.30 是垄线 `−PlotPitch`（`HalfPitch`=1.15 是**格心**，别用错）
            Vector2 at = TownLayout.FarmCenter + new Vector2(-TownLayout.PlotPitch, TownLayout.FarmHalf + 0.80f);
            Transform t = Node("FARM_HAY_1", at, 24f, "草垛");
            Cyl(t, "FARM_HAY_Bale", Vector3.zero, 0.44f, 0.50f, _straw);
            Cyl(t, "FARM_HAY_Top", new Vector3(0f, 0.50f, 0f), 0.32f, 0.20f, _strawDark);
            Cyl(t, "FARM_HAY_Band", new Vector3(0f, 0.26f, 0f), 0.455f, 0.05f, _woodDark);
        }

        // ══════════════════════════════════════════════════════════════════
        // 九、锄头 ×1 —— 田边带东侧、垄线上（偏移 (7.00, 0.00) ⇒ 到最近格心 1.699 米），**靠着篱笆**
        // ══════════════════════════════════════════════════════════════════
        //
        // 倾斜 12°：底端在 7.00，顶端 = 7.00 + 1.30×sin12° = **7.27** ⇒ 正好搭在东边篱笆(7.30)的横杆上 ✓
        private static void Hoe()
        {
            Vector2 at = TownLayout.FarmCenter + new Vector2(ApronEdge + 0.05f, 0f);
            Transform t = Node("FARM_HOE_1", at, 0f, "锄头");

            var lean = new GameObject("FARM_HOE_Lean");
            lean.transform.SetParent(t, false);
            lean.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);   // 顶端朝 +X 倒向篱笆
            Transform lt = lean.transform;

            Part(lt, "FARM_HOE_Shaft", new Vector3(0f, 0.65f, 0f), new Vector3(0.05f, 1.30f, 0.05f), _wood);
            Part(lt, "FARM_HOE_Blade", new Vector3(0.055f, 0.05f, 0f), new Vector3(0.20f, 0.10f, 0.03f), _metal);
            Part(lt, "FARM_HOE_Grip", new Vector3(0f, 1.24f, 0f), new Vector3(0.07f, 0.14f, 0.07f), _woodDark);
        }

        // ══════════════════════════════════════════════════════════════════
        // 十、鸡 ×2 —— 西侧田边带、垄线上（偏移 (−6.95, ±… ) ⇒ 到最近格心 1.662 米）
        // ══════════════════════════════════════════════════════════════════
        private static void Chicken(int idx, float sx, float szSign, bool brown)
        {
            // sx = −1（西边带）；szSign 决定它在哪条垄上：+1 → z=+2.30，−1 → z=−4.60
            // 两条垄：+1 → z = **+2.30**（`+PlotPitch`）；−1 → z = **−4.60**（`−2*PlotPitch`）
            // ⚠️ 不能用 `PlotPitch - HalfPitch`（= 1.15）：那是**格心**的偏移，落在格心上 ⇒ 距离 1.15 < 1.5 ✗
            float zOff = (szSign > 0f) ? TownLayout.PlotPitch : -2f * TownLayout.PlotPitch;
            Vector2 at = TownLayout.FarmCenter + new Vector2(sx * ApronEdge, zOff);
            Material body = brown ? _chickenB : _chickenA;
            Transform t = Node($"FARM_CHICKEN_{idx}", at, YawTo(at, TownLayout.FarmCenter), "鸡");

            Part(t, "FARM_CK_Body", new Vector3(0f, 0.17f, 0f), new Vector3(0.24f, 0.22f, 0.30f), body);
            Part(t, "FARM_CK_Head", new Vector3(0f, 0.31f, 0.13f), new Vector3(0.13f, 0.14f, 0.13f), body);
            Part(t, "FARM_CK_Comb", new Vector3(0f, 0.395f, 0.12f), new Vector3(0.04f, 0.06f, 0.08f), _comb);
            Part(t, "FARM_CK_Beak", new Vector3(0f, 0.30f, 0.21f), new Vector3(0.05f, 0.04f, 0.07f), _straw);
            Part(t, "FARM_CK_Tail", new Vector3(0f, 0.24f, -0.17f), new Vector3(0.09f, 0.14f, 0.08f), _strawDark);
            for (int i = -1; i <= 1; i += 2)
                Part(t, "FARM_CK_Leg" + (i < 0 ? "L" : "R"), new Vector3(i * 0.05f, 0.03f, 0.02f),
                     new Vector3(0.025f, 0.07f, 0.025f), _strawDark);
        }

        // ══════════════════════════════════════════════════════════════════
        // 十一、路边围栏（城界木栅栏）—— **不能挡路**
        // ══════════════════════════════════════════════════════════════════
        //
        // ★ 用户这一轮的 4 条要求（逐条对应）★
        //   ① **先量 `WalkRadius`**（`TownLayout.WalkRadius` = 21.40），围栏建在它**之外 0.4~0.6 米**
        //      ⇒ 本版取 **+0.60**：多边形围栏离原点最近的点在**各边的中点**（= 边心距 apothem），
        //        所以"到 WalkRadius 的最近距离" = 边心距 − WalkRadius = 22.00 − 21.40 = **0.60 米 > 0** ✓
        //        ⇒ 玩家（可达半径 21.40）永远**走不到栏杆里**，也就卡不住 ✓
        //      ⚠️ **不能用"外接圆半径 22"**：那样边中点会退到 22×cos15° = **21.25** < WalkRadius
        //         ⇒ 栏杆有一段跑到玩家的可达范围**里面**，玩家会直接穿过去 ✗（这是最容易被写错的一处）
        //   ② **形态**：**立柱 ＋ 两道横杆**；立柱高 **1.05 米**（要求 0.9~1.1 ✓）；
        //      横杆两道在 y = **0.42 / 0.88**；柱距按 `len / ceil(len / 1.95)` 落在 **1.6~2.0 米**；
        //      **每个多边形转角必放柱**（横杆接头处若没有柱子就是悬空）✓ 见 `PlanEdge` 的 k=0 与 k=n
        //   ③ **门口断开**：南侧那条边的**中点正好在 270°**（= 城门 `GatePos (0,-21)` 的方位），
        //      在 ±2.00 处断开 ⇒ 缺口 **4.00 米**（用户给的范围 3~4 米 ✓），缺口两端各留一根**门柱** ✓
        //   ④ 材质 **`OWN_FENCE_*`**（`_MainTex` / `_BaseMap` 显式置空），图元走 `TownKit.Prim(Cube)` 共享 ✓
        //
        // ⚠️ **已知的径向重叠（如实写在这里，也写进诊断）**：
        //    `TownBootstrap.BuildHedge` 的灌木环在 **21.30~22.00**（56 段，径向厚 0.70）。
        //    围栏边心距 22.00 ⇒ 横杆正好贴着灌木环的**外表面**、转角柱插到 22.62（露在灌木外）。
        //    这一版**不动既有美术**（改灌木半径会影响整城剪影）；要彻底分开有两条路：
        //      (a) 把 `BuildHedge` 的半径外推到 22.10~22.80；或 (b) 把围栏收到 21.30 以内（但那样就挡路了 ✗）
        public const int FenceSides = 12;
        /// <summary>门口缺口宽度（米）。取上限 4.00：这样两条半边的柱距正好落进 1.6~2.0。</summary>
        public const float FenceGateGap = 4.00f;
        /// <summary>立柱高度（要求 0.9~1.1 米）。</summary>
        public const float FencePostHeight = 1.05f;
        public const float FenceSpacingMin = 1.60f;
        public const float FenceSpacingMax = 2.00f;
        private const float FenceRailLow = 0.42f;
        private const float FenceRailHigh = 0.88f;
        private const string FenceRootName = "TOWN_RoadFence";
        /// <summary>围栏节点的完整前缀（**不用 `FENCE_`**：`TownBuildings` 有 `FARM_FENCE_*`，会撞）。</summary>
        private const string FencePrefix = "ROADFENCE_";

        /// <summary>边心距 = `WalkRadius + 0.60` = **22.00 米**。多边形离原点最近的点就在这个半径上。</summary>
        public static float FenceApothem => TownLayout.WalkRadius + 0.60f;

        /// <summary>围栏几何的读数（构建与诊断**共用同一份数学**）。</summary>
        public struct FencePlan
        {
            public int Sides, Posts, Rails;
            public float Apothem, Circumradius;
            public float SpacingMin, SpacingMax;
            public float GateWidth;
            /// <summary>到 `WalkRadius` 的最近距离（米）。**必须 &gt; 0**，否则玩家会穿/卡栏杆。</summary>
            public float NearestToWalk;
            public string GateCenter;
        }

        /// <summary>
        /// ★★ 围栏的**唯一几何实现**（纯函数：一个物体都不建）★★
        ///   构建按它摆；诊断按它量 ⇒ "离 WalkRadius 多远""缺口多宽"这两个判据与摆出来的东西
        ///   是**同一份数学**，域重载清空静态表之后也照样成立 ✓
        /// </summary>
        public static FencePlan ComputeFencePlan(List<Vector4> segs, List<Vector2> posts)
        {
            segs.Clear();
            posts.Clear();
            var plan = new FencePlan { Sides = FenceSides, Apothem = FenceApothem, SpacingMin = float.MaxValue };

            float ap = plan.Apothem;
            float circum = ap / Mathf.Cos(Mathf.PI / FenceSides);      // 22.776
            plan.Circumradius = circum;
            // 顶点从 285° 起步（= 270° + 半个圆心角）⇒ 南侧那条边的**中点**正对 270° = 城门方向
            float start = 270f + 180f / FenceSides;
            var v = new Vector2[FenceSides];
            for (int k = 0; k < FenceSides; k++)
            {
                float rad = (start + k * 360f / FenceSides) * Mathf.Deg2Rad;
                v[k] = new Vector2(Mathf.Cos(rad) * circum, Mathf.Sin(rad) * circum);
            }

            // 南侧那条边 = **中点离门口点最近**的那条
            // ⚠️ 不能用"两端 z 最小"：南侧有**两条**边的某个端点都在 z = −边心距（285° 与 255° 两个顶点），
            //    z 比较会因为"严格小于"取到错误的那一条（实测缺口中心跑到 (11.00,−19.05) = 300° 那条边上 ✗）
            Vector2 gatePoint = new Vector2(0f, -ap);            // 270° 方向、半径 = 边心距 = 城门正后方
            int gateEdge = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < FenceSides; k++)
            {
                Vector2 mid = (v[k] + v[(k + 1) % FenceSides]) * 0.5f;
                float d = (mid - gatePoint).sqrMagnitude;
                if (d < bestD) { bestD = d; gateEdge = k; }
            }

            for (int k = 0; k < FenceSides; k++)
            {
                Vector2 a = v[k], b = v[(k + 1) % FenceSides];
                if (k != gateEdge) { PlanEdge(a, b, segs, posts, ref plan); continue; }

                // ★ 门口：在这条边的中点两侧各让开 `FenceGateGap/2` ★
                Vector2 m = (a + b) * 0.5f;
                Vector2 dir = (b - a).normalized;
                Vector2 g0 = m - dir * (FenceGateGap * 0.5f);
                Vector2 g1 = m + dir * (FenceGateGap * 0.5f);
                plan.GateWidth = Vector2.Distance(g0, g1);
                plan.GateCenter = $"({m.x:0.00}, {m.y:0.00})";
                PlanEdge(a, g0, segs, posts, ref plan);      // 缺口 → 转角
                PlanEdge(g1, b, segs, posts, ref plan);      // 转角 → 缺口
            }

            plan.Posts = posts.Count;
            plan.Rails = segs.Count * 2;

            // 到 WalkRadius 的最近距离 = 到各线段的最短距离 − WalkRadius
            float best = float.MaxValue;
            for (int i = 0; i < segs.Count; i++)
            {
                float d = SegDistanceToOrigin(new Vector2(segs[i].x, segs[i].y), new Vector2(segs[i].z, segs[i].w));
                if (d < best) best = d;
            }
            plan.NearestToWalk = best - TownLayout.WalkRadius;
            return plan;
        }

        /// <summary>一条边：两端**必定**立柱（转角 / 门柱），中间按柱距 1.6~2.0 米补柱；横杆按整段算。</summary>
        private static void PlanEdge(Vector2 a, Vector2 b, List<Vector4> segs, List<Vector2> posts, ref FencePlan plan)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.30f) return;

            int n = Mathf.Max(1, Mathf.CeilToInt(len / 1.95f));
            float sp = len / n;
            if (sp < FenceSpacingMin && n > 1) { n--; sp = len / n; }
            if (sp < plan.SpacingMin) plan.SpacingMin = sp;
            if (sp > plan.SpacingMax) plan.SpacingMax = sp;

            for (int k = 0; k <= n; k++)
            {
                Vector2 p = a + d * (k / (float)n);
                bool dup = false;
                for (int j = 0; j < posts.Count; j++)
                    if ((posts[j] - p).sqrMagnitude < 1e-4f) { dup = true; break; }
                if (!dup) posts.Add(p);
            }
            segs.Add(new Vector4(a.x, a.y, b.x, b.y));
        }

        /// <summary>原点到线段的**最短距离**（不是到端点的距离 —— 边中点比端点更近）。</summary>
        private static float SegDistanceToOrigin(Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(-a, ab) / l2);
            return (a + ab * t).magnitude;
        }

        private static void BuildFence(Transform townRoot)
        {
            if (townRoot == null) return;
            if (townRoot.Find(FenceRootName) != null) return;      // 幂等

            var segs = new List<Vector4>(32);
            var posts = new List<Vector2>(96);
            FencePlan plan = ComputeFencePlan(segs, posts);

            var rootGo = new GameObject(FenceRootName);
            Transform root = rootGo.transform;
            root.SetParent(townRoot, false);

            // 立柱（转角、门柱一定在 —— 见 `PlanEdge` 的 k=0 / k=n）
            for (int i = 0; i < posts.Count; i++)
            {
                Vector2 p = posts[i];
                FenceBox(root, $"{FencePrefix}Post{i:000}",
                         new Vector3(p.x, TownAvatar.GroundY + FencePostHeight * 0.5f, p.y),
                         new Vector3(0.12f, FencePostHeight, 0.12f), 0f, _fencePost);
            }
            // 两道横杆
            for (int i = 0; i < segs.Count; i++)
            {
                Vector2 a = new Vector2(segs[i].x, segs[i].y);
                Vector2 b = new Vector2(segs[i].z, segs[i].w);
                Vector2 d = b - a;
                float len = d.magnitude;
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                Vector2 m = (a + b) * 0.5f;
                for (int r = 0; r < 2; r++)
                {
                    FenceBox(root, $"{FencePrefix}Rail{i:00}_{r}",
                             new Vector3(m.x, TownAvatar.GroundY + (r == 0 ? FenceRailLow : FenceRailHigh), m.y),
                             new Vector3(0.06f, 0.09f, len), yaw, _fenceRail);
                }
            }

            Log.Info($"[Town] 路边围栏已建：{plan.Sides} 边形 · 边心距 **{plan.Apothem:0.00} 米**" +
                     $"（= WalkRadius {TownLayout.WalkRadius:0.00} + 0.60）· " +
                     $"**到 WalkRadius 的最近距离 = {plan.NearestToWalk:0.000} 米**（必须 > 0）· " +
                     $"立柱 {plan.Posts} 根（高 {FencePostHeight:0.00} 米）· 横杆 {plan.Rails} 条（y {FenceRailLow:0.00}/{FenceRailHigh:0.00}）· " +
                     $"柱距 {plan.SpacingMin:0.00}~{plan.SpacingMax:0.00} 米（要求 {FenceSpacingMin:0.0}~{FenceSpacingMax:0.0}）· " +
                     $"门口缺口 **{plan.GateWidth:0.00} 米** @ {plan.GateCenter} · 材质 OWN_FENCE_Post / OWN_FENCE_Rail");
        }

        private static void FenceBox(Transform root, string name, Vector3 center, Vector3 size, float yaw, Material mat)
        {
            GameObject go = TownKit.MeshObject(root, name, TownKit.Prim(PrimitiveType.Cube), center);
            go.transform.localScale = size;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Reg(go);
        }

        /// <summary>
        /// 围栏的**只读诊断**：边心距 / **到 WalkRadius 的最近距离（必须 &gt; 0）** / **门口缺口宽度** /
        /// 柱距 / 柱高 / 材质 `OWN_FENCE_*` 与 `_MainTex` 状态。
        /// 几何来自 `ComputeFencePlan`（纯函数）⇒ 域重载清空静态表也不影响这三条判据 ✓
        /// </summary>
        public static string FenceInfo()
        {
            EnsureRegistry();
            var segs = new List<Vector4>(32);
            var posts = new List<Vector2>(96);
            FencePlan plan = ComputeFencePlan(segs, posts);

            // 场景里的实际渲染件（材质/贴图）
            int renderers = 0, ownCount = 0, texNull = 0;
            GameObject town = GameObject.Find("TOWN");
            Transform root = town != null ? town.transform.Find(FenceRootName) : null;
            if (root != null)
            {
                var rs = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    if (rs[i] == null) continue;
                    renderers++;
                    Material m = rs[i].sharedMaterial;
                    bool own = m != null && m.name != null
                               && m.name.StartsWith(MatPrefixFence, System.StringComparison.Ordinal);
                    if (own) ownCount++;
                    bool texNullOk = true;
                    if (m != null)
                    {
                        if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) texNullOk = false;
                        if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) texNullOk = false;
                    }
                    if (texNullOk) texNull++;
                }
            }

            bool spacingOk = plan.SpacingMin >= FenceSpacingMin - 0.001f && plan.SpacingMax <= FenceSpacingMax + 0.001f;
            var sb = new System.Text.StringBuilder(1200);
            sb.Append($"{plan.Sides} 边形 · 边心距 **{plan.Apothem:0.00} 米**（= WalkRadius {TownLayout.WalkRadius:0.00} + 0.60）" +
                      $" · 外接半径 {plan.Circumradius:0.00} 米");
            sb.Append($"\n【核心判据①】**到 WalkRadius 的最近距离 = {plan.NearestToWalk:0.000} 米**" +
                      $"（必须 > 0 ⇒ {(plan.NearestToWalk > 0f ? "✓ 通过：玩家走不到栏杆里" : "✗ 不通过：栏杆压进可达范围")}）" +
                      $"｜扣掉横杆半厚 0.03 米后仍有 {plan.NearestToWalk - 0.03f:0.000} 米");
            sb.Append($"\n【核心判据②】**门口缺口 = {plan.GateWidth:0.00} 米**（要求 3~4 米 ⇒ " +
                      $"{(plan.GateWidth >= 3f && plan.GateWidth <= 4f ? "✓ 通过" : "✗ 不通过")}）" +
                      $" · 缺口中心 {plan.GateCenter}（270° = 城门方向）· 缺口两端各 1 根门柱");
            sb.Append($"\n  立柱 {plan.Posts} 根 · 高 {FencePostHeight:0.00} 米（要求 0.9~1.1 ✓）· " +
                      $"横杆 {plan.Rails} 条 · y {FenceRailLow:0.00} / {FenceRailHigh:0.00}");
            sb.Append($"\n  柱距 {plan.SpacingMin:0.00}~{plan.SpacingMax:0.00} 米 ⇒ {(spacingOk ? "✓ 落在 1.6~2.0" : "✗ 越界")}" +
                      $" · 转角必立柱（每条边的 k=0 / k=n 都是柱）");
            sb.Append($"\n  场景渲染件 {renderers} 个 · 材质 `OWN_FENCE_*` = {ownCount}/{renderers}" +
                      $" · `_MainTex`/`_BaseMap` 全空 = {texNull}/{renderers}" +
                      $" ⇒ {(ownCount == renderers && texNull == renderers && renderers > 0 ? "✓" : "✗")}");
            sb.Append("\n  ⚠️ 已知：横杆贴着灌木环(21.30~22.00)的外表面 —— 见 `Fence()` 注释里的两条出路");

            string s = sb.ToString();
            Log.Info("[Town] ROADFENCE " + s);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 十二、每 90 帧的校验：**把材质换回 `OWN_*`、把 `_MainTex` / `_BaseMap` 清空**
        // ══════════════════════════════════════════════════════════════════
        //
        // 为什么"建完就完"一定漏（这是本工程踩过的坑）：
        //   `TownArtDresser` 是 `DefaultExecutionOrder(300)`，它在 `Start` 里才改材质，
        //   而本类在 `TownBootstrap.Awake` 里就建完了 ⇒ **先建后污染**。
        //   ⇒ 只建一次 / 只修一次，材质必然又被换掉（用户："一点都没变"就是这个）。
        //   修法：登记表 + 反复校验（协程那份由 `TownFarmDecorGuard` 提供）。

        /// <summary>农田装饰节点的完整前缀（★ 只用完整前缀，不用三字母短词 ★）。</summary>
        private const string ItemPrefix = "FARM_";

        /// <summary>一份材质是不是"我们自己的"（`OWN_FARM_*` 或 `OWN_FENCE_*`，都按完整前缀判）。</summary>
        private static bool IsOwnMaterial(string n)
            => n != null
               && (n.StartsWith(MatPrefixFarm, System.StringComparison.Ordinal)
                   || n.StartsWith(MatPrefixFence, System.StringComparison.Ordinal));

        /// <summary>
        /// ★ 登记表被清空也能认回来（**域重载之后的自我修复**）★
        ///   实测踩到过：在 play 里改脚本 ⇒ Unity 做一次**域重载** ⇒ 场景里的物体**还在**
        ///   （截图里稻草人、杂草一个不少），而 `Items` / `Owned` / `Palette` 这三张**静态**表被清空
        ///   ⇒ `FarmDecorInfo` 老老实实报"0 件 / 最小距离 0.000 / ✗ 不通过" —— 那是**诊断在说谎** ✗
        ///   ⇒ 每次读数 / 校验之前先 `EnsureRegistry()`：表空了就**按名字从场景认回来**，
        ///     材质也按 `OWN_FARM_*` / `OWN_FENCE_*` 前缀重新收齐 ⇒ 修复逻辑不依赖建城那一刻的静态快照 ✓
        ///   ⚠️ 只认**完整前缀**（`FARM_` / `ROADFENCE_` / `OWN_FARM_` / `OWN_FENCE_`）：
        ///     `RIM` 这种三字母词会命中 `HatBrim` ✗
        /// </summary>
        private static void EnsureRegistry()
        {
            if (_registered) return;

            GameObject town = GameObject.Find("TOWN");
            if (town == null) return;

            Palette.Clear();
            Owned.Clear();
            Items.Clear();
            MinDistanceToPlot = float.MaxValue;

            // ① 农田装饰：`TOWN_FarmDecor/` 下、`FARM_` 开头的直接子节点
            Transform root = town.transform.Find(RootName);     // 直接子节点；不做全深度模糊搜索
            if (root != null)
            {
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform t = root.GetChild(i);
                    if (t == null) continue;
                    if (!t.name.StartsWith(ItemPrefix, System.StringComparison.Ordinal)) continue;

                    Vector3 p = t.position;
                    Items.Add(new Item { Name = t.name, T = t, At = new Vector2(p.x, p.z), Kind = KindOf(t.name) });
                    AdoptRenderers(t);
                }

                MinDistanceToPlot = Items.Count > 0 ? float.MaxValue : 0f;
                for (int i = 0; i < Items.Count; i++)
                {
                    float d = NearestPlotDistance(Items[i].At);
                    if (d < MinDistanceToPlot) MinDistanceToPlot = d;
                }
            }

            // ② 路边围栏：`TOWN_RoadFence/` 下所有渲染件（围栏只报汇总读数，不逐件列）
            Transform fence = town.transform.Find(FenceRootName);
            if (fence != null) AdoptRenderers(fence);

            if (root == null && fence == null) return;          // 还没建过 ⇒ 下次再认
            _registered = true;

            Log.Info($"[Town] 农田装饰的登记表已从场景认回：{Items.Count} 件 · 渲染件 {Owned.Count} 个 · " +
                     $"材质 {Palette.Count} 份（域重载会清空静态表 —— 这一条让诊断与校验都不说谎）");

            // ★ 顺带把**每 90 帧的校验**重新接上 ★
            //   域重载会把协程一起干掉，而 `Start` 不会重跑 ⇒ 不重接的话校验会静默停摆
            //   （协程本身不受 `enabled=false` 影响，见 `TownFarmDecorGuard` 的注释）
            TownFarmDecorGuard guard = town.GetComponentInChildren<TownFarmDecorGuard>(true);
            if (guard != null) guard.Arm();
        }

        /// <summary>把一棵子树里**我们自己的**渲染件收进登记表（材质只收 `OWN_*` 前缀的）。</summary>
        private static void AdoptRenderers(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            for (int k = 0; k < rs.Length; k++)
            {
                MeshRenderer mr = rs[k] as MeshRenderer;
                if (mr == null) continue;
                Material m = mr.sharedMaterial;
                if (m == null || !IsOwnMaterial(m.name)) continue;
                Owned[mr] = m;
                if (!Palette.Contains(m)) Palette.Add(m);
            }
        }

        /// <summary>节点名 → 中文类别（**完整前缀**匹配，够长、不会误伤）。</summary>
        private static string KindOf(string name)
        {
            if (name.StartsWith("FARM_SCARECROW_", System.StringComparison.Ordinal)) return "稻草人";
            if (name.StartsWith("FARM_WEED_", System.StringComparison.Ordinal)) return "杂草";
            if (name.StartsWith("FARM_BUCKET_", System.StringComparison.Ordinal)) return "水桶";
            if (name.StartsWith("FARM_HAY_", System.StringComparison.Ordinal)) return "草垛";
            if (name.StartsWith("FARM_HOE_", System.StringComparison.Ordinal)) return "锄头";
            if (name.StartsWith("FARM_CHICKEN_", System.StringComparison.Ordinal)) return "鸡";
            return "装饰";
        }

        public static int Repair(bool verbose)
        {
            EnsureRegistry();
            RepairedLast = 0;
            SweepCount++;

            // ① 渲染器：材质被换掉就**换回登记的那一份**（不新建，避免每 90 帧漏一堆材质）
            foreach (KeyValuePair<MeshRenderer, Material> kv in Owned)
            {
                MeshRenderer mr = kv.Key;
                Material own = kv.Value;
                if (mr == null || own == null) continue;
                if (!ReferenceEquals(mr.sharedMaterial, own))
                {
                    mr.sharedMaterial = own;
                    RepairedLast++;
                }
            }

            // ② 材质：`_MainTex` / `_BaseMap` 被塞进贴图就清空（美术通道就是这么污染共享池的）
            for (int i = 0; i < Palette.Count; i++)
            {
                Material m = Palette[i];
                if (m == null) continue;
                if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)
                {
                    m.SetTexture("_MainTex", null);
                    RepairedLast++;
                }
                if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                {
                    m.SetTexture("_BaseMap", null);
                    RepairedLast++;
                }
            }

            RepairedTotal += RepairedLast;
            if (verbose)
                Log.Info($"[Town] 农田装饰校验：第 {SweepCount} 次｜本次修回 {RepairedLast} 处｜累计 {RepairedTotal} 处｜" +
                         $"在册渲染件 {Owned.Count} 个 / 材质 {Palette.Count} 份 OWN_FARM_*");
            return RepairedLast;
        }

        // ══════════════════════════════════════════════════════════════════
        // 十二、诊断（**只读**：不增不减、不修材质，如实报当前状态）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 逐件报告：名字 / 世界坐标 / AABB 尺寸 / **到最近田格中心的距离** / 材质名 / `_MainTex` 是否为空。
        /// 汇总里打"**最小距离 = X 米**"（必须 ≥ 1.5）、材质是否全部 `OWN_FARM_*`、贴图是否全空。
        /// 本方法**只读**：不改材质、不增删物体。要强制修一次用 <see cref="Repair"/>。
        /// </summary>
        public static string Info()
        {
            EnsureRegistry();
            var sb = new System.Text.StringBuilder(2600);
            sb.Append($"农田装饰：**{Items.Count} 件**（稻草人 1 / 杂草 16 / 水桶 1 / 草垛 1 / 锄头 1 / 鸡 2）· " +
                      $"渲染件 {Owned.Count} 个 · 材质 {Palette.Count} 份 · " +
                      $"校验已跑 {SweepCount} 次（累计修回 {RepairedTotal} 处，上一次 {RepairedLast} 处）");

            sb.Append($"\n【核心判据】到最近田格中心的**最小距离 = {MinDistanceToPlot:0.000} 米**" +
                      $"（硬门槛 {MinPlotDistance:0.0} ⇒ {(MinDistanceToPlot >= MinPlotDistance ? "✓ 通过" : "✗ 不通过")}" +
                      $"；采样门槛 {WeedSampleGate:0.00}）" +
                      $"\n  田格中心 X = {PlotAxisText(true)}" +
                      $"\n  田格中心 Z = {PlotAxisText(false)}" +
                      $"\n  垄线    X = {RidgeAxisText(true)}" +
                      $"\n  垄线    Z = {RidgeAxisText(false)}" +
                      $"\n  半间距 = {HalfPitch:0.00} 米（格心在它的奇数倍上，垄线在偶数倍上 ⇒ 垄×垄交叉点 = √2×1.15 = 1.626 米）" +
                      $"\n  田格带外沿 = ±{PlotHalfSpan:0.00} 米 · 土面 = ±{TownLayout.FarmHalf:0.00} 米 · " +
                      $"篱笆 = ±{TownLayout.FarmHalf + 0.15f:0.00} 米 · 田边带落点 = ±{ApronEdge:0.00} 米");

            // ★★ 确定性判据：把落点算法**再跑一遍**，与场景里的实际坐标逐点比对 ★★
            //   杂草位置是"固定哈希的拒绝采样"⇒ 同一份代码两次算出来必须**逐位相同**。
            //   第 1 遍 = 建城时摆出来的（场景坐标）；第 2 遍 = 现在重算 ⇒ 这就是"两次跑"的证据。
            var run2 = new List<Vector2>(WeedCount);
            ComputeWeedPositions(run2);
            var run1 = new List<Vector2>(WeedCount);
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Name.StartsWith("FARM_WEED_", System.StringComparison.Ordinal)) run1.Add(Items[i].At);

            int n = Mathf.Min(run1.Count, run2.Count);
            int mismatch = 0;
            float worstDelta = 0f;
            for (int i = 0; i < n; i++)
            {
                float d = Vector2.Distance(run1[i], run2[i]);
                if (d > worstDelta) worstDelta = d;
                if (d > 1e-4f) mismatch++;
            }
            Vector2 p0a = n > 0 ? run1[0] : Vector2.zero, p0b = run2.Count > 0 ? run2[0] : Vector2.zero;
            Vector2 p15a = n > 15 ? run1[15] : Vector2.zero, p15b = run2.Count > 15 ? run2[15] : Vector2.zero;
            sb.Append($"\n【确定性】杂草 {n} 丛**两次计算逐点比对** ⇒ " +
                      $"{(mismatch == 0 ? "**坐标完全一致 ✓**" : $"**{mismatch} 处不一致 ✗**")}" +
                      $"（最大偏差 {worstDelta:0.000000} 米；不碰 `World.Rng`，用 `Hash(i,尝试序号) ^ 0x9E3779B9` × 0.618…）" +
                      $"\n  第 01 丛：场景 ({p0a.x:0.0000}, {p0a.y:0.0000}) ｜ 重算 ({p0b.x:0.0000}, {p0b.y:0.0000})" +
                      $"\n  第 16 丛：场景 ({p15a.x:0.0000}, {p15a.y:0.0000}) ｜ 重算 ({p15b.x:0.0000}, {p15b.y:0.0000})");

            int ownOk = 0, texOk = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                if (it.T == null)
                {
                    sb.Append($"\n  {it.Name}：**已不存在**（被别的系统删了）");
                    continue;
                }
                Vector3 p = it.T.position;
                float d = NearestPlotDistance(it.At);

                // AABB：把这一件下面的所有渲染器合起来（全深度）
                Bounds b = new Bounds(p, Vector3.zero);
                var rs = it.T.GetComponentsInChildren<Renderer>(true);
                bool first = true;
                for (int k = 0; k < rs.Length; k++)
                {
                    if (rs[k] == null) continue;
                    if (first) { b = rs[k].bounds; first = false; }
                    else b.Encapsulate(rs[k].bounds);
                }

                // 材质名（去重）+ `_MainTex` 状态
                var names = new List<string>(6);
                bool allOwn = true, allTexNull = true;
                for (int k = 0; k < rs.Length; k++)
                {
                    if (rs[k] == null) continue;
                    Material m = rs[k].sharedMaterial;
                    string mn = m != null ? m.name : "(null)";
                    if (!names.Contains(mn)) names.Add(mn);
                    if (m == null || !mn.StartsWith("OWN_FARM_", System.StringComparison.Ordinal)) allOwn = false;
                    if (m != null)
                    {
                        if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) allTexNull = false;
                        if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) allTexNull = false;
                    }
                }
                if (allOwn) ownOk++;
                if (allTexNull) texOk++;

                sb.Append($"\n  {it.Name}（{it.Kind}）@ ({p.x:0.00}, {p.z:0.00})" +
                          $" · AABB {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00}" +
                          $" · 最近格心 **{d:0.000} m**" +
                          $" · 材质 {string.Join(" / ", names)} {(allOwn ? "✓OWN_FARM_" : "✗ 不是 OWN_FARM_")}" +
                          $" · _MainTex {(allTexNull ? "null ✓" : "**有贴图 ✗**")}");
            }

            sb.Append($"\n【汇总】材质全部 `OWN_FARM_*` 的件数 = {ownOk}/{Items.Count}" +
                      $" · `_MainTex`/`_BaseMap` 全空的件数 = {texOk}/{Items.Count}" +
                      (ownOk == Items.Count && texOk == Items.Count ? " ✓" : " ✗"));

            // 围栏汇总（几何来自纯函数 `ComputeFencePlan` ⇒ 静态表被清也不影响读数）
            var fsegs = new List<Vector4>(32);
            var fposts = new List<Vector2>(96);
            FencePlan fp = ComputeFencePlan(fsegs, fposts);
            sb.Append($"\n【路边围栏】{fp.Sides} 边形 · 边心距 {fp.Apothem:0.00} 米 · " +
                      $"**到 WalkRadius({TownLayout.WalkRadius:0.00}) 的最近距离 = {fp.NearestToWalk:0.000} 米**" +
                      $"（必须 > 0 ⇒ {(fp.NearestToWalk > 0f ? "✓" : "✗")}）· " +
                      $"**门口缺口 = {fp.GateWidth:0.00} 米** ⇒ 明细用 `FenceInfo()`");

            string s = sb.ToString();
            Log.Info("[Town] FARMDECOR " + s);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 十三、小工具
        // ══════════════════════════════════════════════════════════════════

        /// <summary>到**最近一块田格中心**的距离（36 块全算一遍；诊断与构建共用同一口径）。</summary>
        public static float NearestPlotDistance(Vector2 p)
        {
            float best = float.MaxValue;
            int n = TownLayout.PlotCols * TownLayout.PlotRows;
            for (int i = 0; i < n; i++)
            {
                float d = (TownLayout.PlotCenter(i) - p).sqrMagnitude;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>
        /// ★ 确定性哈希 → [0,1)★ 用户指定口径：`h = Hash(seed) ^ 0x9E3779B9`，再 `frac(h × 0.6180339887)`。
        /// **不碰 `World.Rng`**（动它就是动整个游戏的确定性）。同一个 (seed, salt) 永远同一个结果 ✓
        /// </summary>
        private static float Hash01(int seed, int salt)
        {
            unchecked
            {
                // Hash(seed)：两层混合 + 一个与 salt（第几次尝试）相关的偏移
                uint h = (uint)seed * 374761393u + (uint)salt * 668265263u;
                h = (h ^ (h >> 15)) * 2246822519u;
                h ^= 0x9E3779B9u;                       // ← 用户点名的黄金分割常量
                h = (h ^ (h >> 13)) * 3266489917u;
                h ^= h >> 16;
                float f = (h & 0xFFFFFFu) / 16777216f;   // [0,1)
                f *= 0.6180339887f;                      // ← 用户点名的取小数口径
                return f - Mathf.Floor(f);
            }
        }

        /// <summary>让**本地 +Z**指向 <paramref name="to"/> 的 Y 轴欧拉角。</summary>
        private static float YawTo(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        private static Transform Node(string name, Vector2 at, float yaw, string kind)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(at.x, TownAvatar.GroundY, at.y);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Items.Add(new Item { Name = name, T = go.transform, At = at, Kind = kind });
            return go.transform;
        }

        /// <summary>一个方块零件（走 `TownKit` 的共享 Cube 网格，**不 new Mesh**），并登记它的材质。</summary>
        private static void Part(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
            => Reg(TownKit.BoxL(parent, name, localPos, size, mat));

        /// <summary>一个圆柱零件（走 `TownKit.Cylinder()` 的**按尺寸缓存**网格）。</summary>
        private static void Cyl(Transform parent, string name, Vector3 localPos, float radius, float height, Material mat)
            => Reg(TownKit.CylL(parent, name, localPos, radius, height, mat));

        private static void Reg(GameObject go)
        {
            if (go == null) return;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            Material m = mr.sharedMaterial;
            if (m != null) Owned[mr] = m;
        }

        private static string PlotAxisText(bool xAxis)
        {
            var sb = new System.Text.StringBuilder(64);
            for (int k = 0; k < TownLayout.PlotCols; k++)
            {
                int idx = xAxis ? k : k * TownLayout.PlotCols;
                Vector2 c = TownLayout.PlotCenter(idx);
                if (k > 0) sb.Append(" / ");
                sb.Append((xAxis ? c.x : c.y).ToString("0.00"));
            }
            return sb.ToString();
        }

        private static string RidgeAxisText(bool xAxis)
        {
            var sb = new System.Text.StringBuilder(48);
            for (int k = 0; k < 5; k++)
            {
                // ★ 同一个垄公式：`(k - 2) * PlotPitch`（1.15 的偶数倍）★
                float c = (xAxis ? TownLayout.FarmCenter.x : TownLayout.FarmCenter.y) + (k - 2) * TownLayout.PlotPitch;
                if (k > 0) sb.Append(" / ");
                sb.Append(c.ToString("0.00"));
            }
            return sb.ToString();
        }

        // ── v239 收尾记录 ──
        //  · 杂草 = 固定哈希的**拒绝采样**（`ComputeWeedPositions` 纯函数）⇒ 诊断再跑一遍比对坐标
        //    门槛：采样 1.55 / 硬判据 1.5；两丛最小间隔 0.60 米；试不满 40 次就退回垄交叉点
        //  · 稻草人 2 → **1**（只留东南角）；路边围栏新增（边心距 = WalkRadius + 0.60 = 22.00）
        //  · 静态登记表会在**域重载**后被清空（play 里改脚本就会发生）⇒ `EnsureRegistry()` 兜住，
        //    并且会把围栏的渲染件一起收回来（`AdoptRenderers`）
        //  · 未做：装饰的碰撞体（本工程约定"装饰不加 Collider"，与长椅/公园道具一致）
    }
}
