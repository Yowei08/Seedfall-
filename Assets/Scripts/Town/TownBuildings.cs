using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城的配色（全部走 `ToonLook` 的**共享材质池**，见 `TownKit` 第一节）★★
    /// 集中放一处：Ground / 建筑 / 公园 / HUD 的色号都从这里取，改一个色不会只改半个城。
    /// </summary>
    public static class TownColors
    {
        public static readonly Color Grass = new Color(0.42f, 0.60f, 0.31f);
        public static readonly Color ParkGrass = new Color(0.35f, 0.64f, 0.35f);
        public static readonly Color Hedge = new Color(0.24f, 0.46f, 0.27f);
        // v217：田土与地块**加深**（放大之后 0.3 的沟很窄，浅色会让整块田读成"铺装地面"）
        public static readonly Color TilledSoil = new Color(0.36f, 0.25f, 0.17f);
        public static readonly Color PlotSoil = new Color(0.48f, 0.34f, 0.22f);
        public static readonly Color Road = new Color(0.68f, 0.65f, 0.58f);
        public static readonly Color RoadDark = new Color(0.58f, 0.55f, 0.49f);
        public static readonly Color Plaza = new Color(0.74f, 0.71f, 0.64f);
        public static readonly Color Stone = new Color(0.55f, 0.57f, 0.62f);
        public static readonly Color StoneDark = new Color(0.42f, 0.44f, 0.50f);
        public static readonly Color WallWood = new Color(0.58f, 0.43f, 0.29f);
        public static readonly Color WallWoodDark = new Color(0.47f, 0.34f, 0.23f);
        public static readonly Color RoofDark = new Color(0.34f, 0.25f, 0.20f);
        public static readonly Color RoofTile = new Color(0.60f, 0.28f, 0.24f);
        public static readonly Color HouseWarm = new Color(0.88f, 0.82f, 0.68f);
        public static readonly Color HouseRose = new Color(0.88f, 0.73f, 0.67f);
        public static readonly Color HouseBlue = new Color(0.72f, 0.79f, 0.88f);
        public static readonly Color DoorDark = new Color(0.33f, 0.22f, 0.16f);
        public static readonly Color SignGold = new Color(0.92f, 0.74f, 0.24f);
        public static readonly Color MetalDark = new Color(0.24f, 0.26f, 0.30f);
        public static readonly Color LampGlass = new Color(1.00f, 0.88f, 0.52f);
        public static readonly Color PortalGlow = new Color(0.40f, 0.86f, 1.00f);
        public static readonly Color Water = new Color(0.38f, 0.66f, 0.82f);
        public static readonly Color CanvasRed = new Color(0.78f, 0.34f, 0.30f);
        public static readonly Color CanvasCream = new Color(0.95f, 0.91f, 0.80f);
        public static readonly Color Bench = new Color(0.62f, 0.46f, 0.30f);
        public static readonly Color Gold = new Color(1.00f, 0.86f, 0.30f);
        public static readonly Color Statue = new Color(0.80f, 0.78f, 0.72f);
    }

    /// <summary>
    /// ★★ 主城里**所有立体的东西**：五栋互不相同的建筑 + 南门 + 中心喷泉 + 公园绿化 + 环上路灯 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么这些从 `TownBootstrap` 里搬出来（并且**必须一栋一函数**）
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户原话：「**所有建筑物单独建模，拒绝高度重复**」「**拆开商店和服装店为两个建筑**」
    /// 「禁止出现突兀的地块或者**高度重复的排列和模型**」。
    /// 上一版是 `for` 循环摆 6 栋 `HOUSE_Body{i}` —— 同一个盒子、同一个屋顶、只换墙色，
    /// 那正是要推翻的东西 ✗
    /// ⇒ 这一版每栋楼是**一个独立函数、一套独立尺寸、一种独立屋顶形状**：
    ///
    ///   | 建筑 | 方位 | 屋顶 | 体量 | 识别特征 |
    ///   |---|---|---|---|---|
    ///   | 大传送门 | 90°（正北） | 无顶·门楼 | 7.5×1.45×8.4 | 双柱＋发光门面＋火盆 |
    ///   | 服装店 | 25°（东北偏东） | **8 棱锥尖顶** | 3.3×3.1×9.8 | **三层塔楼**＋悬挂布幌＋模特 |
    ///   | 杂货商店 | 322°（东南偏东） | **半球圆顶** | 4.1 直径×5.6 | 圆柱身＋圆顶＋圆窗 |
    ///   | 种子仓库 | 205°（西南偏西） | **平顶** | 6.2×3.2×3.9 | 长条木仓＋装卸台＋滑门 |
    ///   | 集市帐篷 | 240°（南偏西） | **4 棱锥帆布** | 4.7×4.7×3.8 | 布篷＋柜台＋旗杆 |
    ///   | 南门 | 270°（正南） | 平梁 | 4.3×0.55×4.1 | 双石柱＋横幅＋灯笼 |
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、局部坐标（★ 每栋楼都在"自己的"坐标系里搭 ★）
    /// ══════════════════════════════════════════════════════════════════════
    /// <see cref="Slot"/> 造一个**已摆到方位角上、已转到正面朝圆心**的空物体；
    /// 楼里的零件全部用 `TownKit.BoxL / CylL / ConeL / DomeL` 按**局部坐标**给（+Z = 正面朝圆心，
    /// +X = 沿圆周的切线方向，y = 0 是地面）⇒ 改一栋楼不会牵动别栋，也不需要任何三角函数 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、碰撞体
    /// ══════════════════════════════════════════════════════════════════════
    /// 每个**实心零件**在造的时候传 `solid: true`（见 `TownKit` 第三节）：
    /// 墙体 / 屋顶 / 柱子 / 台阶这四类有 Collider，**门板、窗、招牌、布幌、发光门面没有** ——
    /// 它们贴在实体表面上，加了 Collider 只会让玩家在门前 5 厘米处被挡住（"隔空撞墙"那种手感）✗
    /// ⇒ 碰撞体是**跟着模型走的**：圆的是圆、尖的是尖，不是一个大方块 ✓
    /// </summary>
    public static class TownBuildings
    {
        // ══════════════════════════════════════════════════════════════════
        // 一、楼位（★ 唯一入口 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 在方位角 <paramref name="deg"/>、半径 <paramref name="radius"/> 上造一个空物体，
        /// 并把它**转到正面朝圆心**（局部 +Z 指向城心）。路灯 / 长椅这类"绕圈摆"的东西用它。
        /// </summary>
        public static Transform Slot(Transform root, string name, float deg, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = TownLayout.Polar3(deg, radius, 0f);
            go.transform.rotation = Quaternion.Euler(0f, TownLayout.FaceCenterYaw(deg), 0f);
            return go.transform;
        }

        /// <summary>
        /// ★ v215：**按世界坐标 + 绝对朝向**造一个楼位（西侧三区的建筑不是绕圈的，是沿街摆的）★
        /// <paramref name="yaw"/> = 这栋楼的正面（本地 +Z，即门那一面）朝哪：180=朝南、270=朝西、0=朝北。
        /// </summary>
        public static Transform Place(Transform root, string name, Vector2 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        /// <summary>把六栋建筑 + 南门 + 家 一次造完。返回值只用于日志。</summary>
        public static Transform[] BuildAll(Transform root, Transform propRoot)
        {
            Labels.Clear();
            var list = new[]
            {
                BuildPortal(root, propRoot),
                BuildClothing(root, propRoot),
                BuildShop(root, propRoot),
                BuildWarehouse(root, propRoot),
                BuildTent(root, propRoot),
                BuildHome(root, propRoot),
                BuildGate(root, propRoot),
            };

            // ★ v216 ⑤：**世界空间名字标签**（用户："在地图上标注功能性建筑的名字"）★
            //   为什么用 IMGUI 投影而不是 TextMesh：标签要永远正对镜头、永远压在最上层；
            //   而 TextMesh 会跟着世界的俯角变成斜的、还会被墙挡住 —— 投影标签每帧只花 7 次
            //   `WorldToScreenPoint`，换来的是一眼读得懂 ✓
            AddLabel("大传送门", TownLayout.PortalPos, 9.70f);
            AddLabel("杂货商店", TownLayout.ShopPos, 6.30f);
            AddLabel("服装店", TownLayout.ClothPos, 10.30f);
            AddLabel("种子仓库", TownLayout.WarePos, 5.30f);
            AddLabel("集 市", TownLayout.TentPos, 4.60f);
            AddLabel("家 · 图鉴", TownLayout.HomePos, 5.70f);
            // ★ v220：**「南 门」标签删掉**（用户："去除南门字样"）★
            //   门现在的逻辑：**纯装饰、不可交互**（无 ActKind、无锚点、无碰撞守卫），
            //   玩家从它下面走进城；真正的边界是圆形可达半径 r=21.4 ✓
            return list;
        }

        /// <summary>一条世界空间标签（名字 + 它挂在哪）。</summary>
        public struct Label
        {
            public string Text;
            public Vector3 World;
        }

        /// <summary>所有功能性建筑的名字标签（`TownBootstrap` 每帧投影到屏幕上画）。</summary>
        public static readonly System.Collections.Generic.List<Label> Labels =
            new System.Collections.Generic.List<Label>(8);

        /// <summary>加一条标签（世界坐标 = 楼位 + 楼高）。</summary>
        public static void AddLabel(string text, Vector2 pos, float height)
        {
            Labels.Add(new Label { Text = text, World = new Vector3(pos.x, height, pos.y) });
        }

        // ══════════════════════════════════════════════════════════════════
        // 二、大传送门（北向主街尽头 · 门楼）—— ★ 回战斗的出口，自带模型 ★
        //    用户原话：「战斗开始的传送门记得添加，并附上建模和黑洞的动态特效」
        //    ⇒ 模型：双柱（带凹槽）+ 拱顶门楣 + 阶梯 + 两块符文板 + 横贯的光带 + 火盆
        //    ⇒ 特效挂点 `FX_BossPortal`（黑洞/粒子由美术那路做，按名字找它，**别改名**）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// ★★ v261：**大传送门（真网格模型）** ★★
        ///
        /// ── 与上一版的区别（这是"拆掉重做"的实际内容）──
        ///   上一版是 30 多个 `BoxL` 方块拼的：柱子是长方体、门楣是长方体、拱顶靠"再压一层方块"假装；
        ///   门前还叠了 3 类**几乎共面的薄板**（台阶 7.2×1.2 / 月台 9.6×1.6 / 5 条 y=0.03 的光斑）
        ///   ⇒ 实机上就是用户报的"传送门前的路面异常"（一条条闪面）✗
        ///
        ///   这一版：
        ///     · 几何**全部是生成的网格**（`TownPortalModel`）：连续三级圆台 / 12 面锥台柱 /
        ///       真·拱带 / 楔形拱心石 / 门洞内壁 / 柱顶尖饰
        ///     · 门前**只有一层地面**（那块圆台）⇒ 闪面从根上消失 ✓
        ///     · 特效走真几何：三条对数螺旋**管**绕门洞旋转 + 门洞黑盘（`TownPortalFx` 照旧挂这里）
        ///     · 配色沿用 `TownColors`（Stone / StoneDark / SignGold / PortalGlow）⇒ 与主城同一套 ✓
        ///
        /// ⚠️ 门洞仍然是**实体屏障**（`PORTAL_Field`）：要回战斗请按 E（这是既有玩法，没动）✓
        /// </summary>
        private static Transform BuildPortal(Transform root, Transform propRoot)
        {
            Transform s = Place(root, "BUILD_Portal", TownLayout.PortalPos, TownLayout.PortalYaw);
            Material stone = TownKit.Mat(TownColors.Stone);
            Material stoneDark = TownKit.Mat(TownColors.StoneDark);
            Material metal = TownKit.Mat(TownColors.MetalDark);
            Material glow = TownKit.Mat(TownColors.PortalGlow);
            Material gold = TownKit.Mat(TownColors.SignGold);
            Material cavern = TownKit.Mat(new Color(0.13f, 0.14f, 0.18f));

            const float baseY = TownPortalModel.PlatformTop;      // 台基顶面 = 立柱起点
            const float colX = 3.05f;                             // 柱心到中线（留出 4.86 m 门洞）
            const float colTop = baseY + TownPortalModel.ColumnH; // 拱起点高度

            // ① 台基：**一整块**连续三级圆台（不再是互相压着的薄板）
            TownKit.Local(s, "PORTAL_Platform",
                          TownPortalModel.SteppedPlatform(new[] { 5.90f, 5.10f, 4.50f },
                                                          new[] { 0.08f, 0.05f, 0.05f }, 56),
                          Vector3.zero, stoneDark);

            // ② 立柱 ×2：12 面锥台 + 柱础（宽圆盘）+ 柱头（宽圆盘）+ 柱顶尖饰
            for (int i = -1; i <= 1; i += 2)
            {
                // ★ v268：**一个车削网格** = 柱础(0.86) → 柱脚收进 → 柱身(0.62→0.52) → 柱头(0.80) → 顶台(0.90)
                //   造型回来了，而且**没有零件交线**（描边通道不会多画线）✓
                var prof = new Vector2[]
                {
                    new Vector2(0.86f, 0.00f),   // 柱础底沿
                    new Vector2(0.86f, 0.26f),
                    new Vector2(0.70f, 0.34f),   // 收进
                    new Vector2(0.62f, 0.46f),   // 柱脚
                    new Vector2(0.52f, TownPortalModel.ColumnH - 0.42f),   // 柱身收分到顶
                    new Vector2(0.80f, TownPortalModel.ColumnH - 0.30f),   // 柱头外扩
                    new Vector2(0.80f, TownPortalModel.ColumnH - 0.10f),
                    new Vector2(0.90f, TownPortalModel.ColumnH),           // 顶台（承拱）
                };
                TownKit.Local(s, "PORTAL_Column", TownPortalModel.Lathe(prof, 14),
                              new Vector3(i * colX, baseY, 0f), stone, true);
                // ★ v266：**柱础 / 柱头 / 顶尖 / 三道金箍全部删除**
                //   原因：它们都比柱身粗、又套在逐渐收分的锥台柱上 ⇒ **三面互穿**，
                //   而本工程的描边通道会把每条交线画成黑线 ⇒ 门周围那些黑圈/黑弧 ✗
                //   柱身（12 面锥台，底 0.62 → 顶 0.52）本身已有收分，够读 ✓
            }

            // ③ 真·拱带（半圆）：内半径 = 门洞半宽，外半径 = 内 + 厚度，进深 1.30 m
            TownKit.Local(s, "PORTAL_Arch",
                          TownPortalModel.ArchBand(TownPortalModel.InnerRadius,
                                                   TownPortalModel.InnerRadius + TownPortalModel.ArchThick,
                                                   -TownPortalModel.ArchDepth * 0.5f,
                                                   TownPortalModel.ArchDepth * 0.5f, 30, 0f, 180f),
                          new Vector3(0f, colTop, 0f), stone);

            // ④ 拱心石（楔形，压在拱顶正中）
            TownKit.Local(s, "PORTAL_Keystone",
                          TownPortalModel.Wedge(0.86f, 1.24f, 1.05f, TownPortalModel.ArchDepth + 0.16f),
                          new Vector3(0f, colTop + TownPortalModel.InnerRadius - 0.18f, 0f), gold);

            // ★ v265：**门洞内壁（隧道）两件已删除**
            //   原因：`PORTAL_Tunnel`（3.4 m 深的拱形薄壳，厚 0.12）与 `PORTAL_TunnelWall`
            //   （两侧 0.14 m 直壁）都**穿进柱子与拱带里**，镜头里只露出细边 ⇒
            //   描边通道（`SeedfallScreenOutline`）把这些边画成**黑弧线/黑虚线**（用户报的
            //   「传送门周围有一些黑色条纹」就是它们）✗
            //   黑洞盘已经把门洞堵死，隧道内部永远看不到 ⇒ 删掉最干净 ✓

            // ★★ v271：**黄色装饰雕塑**（用户："传送门模型增加黄色装饰雕塑"）★★
            //   全部用基础几何（球/锥/盒），**不与主体互穿**：都摆在拱顶与柱顶的**外侧**，
            //   这样描边通道不会沿着交线再画黑边 ✓
            {
                // ① 拱顶徽记：球 + 锥 + 两片翼
                float apexY = colTop + TownPortalModel.InnerRadius + 0.55f;
                TownKit.BallL(s, "PORTAL_Crest", new Vector3(0f, apexY, 0.30f), 0.62f, gold);
                TownKit.ConeL(s, "PORTAL_CrestSpike", new Vector3(0f, apexY + 0.28f, 0.30f), 0.26f, 0.78f, gold, 8);
                for (int i = -1; i <= 1; i += 2)
                {
                    var wing = TownKit.BoxL(s, "PORTAL_CrestWing",
                                            new Vector3(i * 0.62f, apexY + 0.06f, 0.30f),
                                            new Vector3(0.62f, 0.16f, 0.10f), gold);
                    wing.transform.localRotation = Quaternion.Euler(0f, 0f, i * 22f);
                }
                // ② 两柱顶：金球 + 锥尖（柱头饰）
                for (int i = -1; i <= 1; i += 2)
                {
                    TownKit.BallL(s, "PORTAL_ColumnOrb", new Vector3(i * colX, colTop + 0.28f, 0f), 0.52f, gold);
                    TownKit.ConeL(s, "PORTAL_ColumnSpire", new Vector3(i * colX, colTop + 0.50f, 0f), 0.22f, 0.72f, gold, 8);
                }
                // ③ 柱身两道细金箍（0.635 半径 / 0.06 高：只比柱身粗 1.5 cm）
                for (int i = -1; i <= 1; i += 2)
                    for (int k = 0; k < 2; k++)
                        TownKit.CylL(s, "PORTAL_GoldBand",
                                     new Vector3(i * colX, baseY + 1.40f + k * 3.10f, 0f),
                                     0.635f, 0.06f, gold);
            }

            // ⑥ 门洞结界：★ v263 用户要求"移除中心的蓝色背景版" ★
            //   这块板**同时**是实体屏障（`solid: true` ⇒ 不许穿过去，要回战斗请按 E），
            //   所以**不能删**，只能把**渲染关掉**、碰撞留着 ✓
            //   （判据：`GetComponent<MeshRenderer>().enabled = false` 之后，
            //     玩家的碰撞体仍然撞得到它 —— 屏障在、蓝板不在）
            GameObject field = TownKit.BoxL(s, "PORTAL_Field",
                         new Vector3(0f, baseY + TownPortalModel.ColumnH * 0.5f + 0.6f, 0.02f),
                         new Vector3(TownPortalModel.InnerRadius * 2f - 0.10f, TownPortalModel.ColumnH + 0.4f, 0.16f),
                         glow, true);
            var fieldMr = field.GetComponent<MeshRenderer>();
            if (fieldMr != null) fieldMr.enabled = false;      // 蓝板消失，BoxCollider 照旧

            // ⑦b ★ v261c：**把那三条光柱删掉**
            //   它们是用管的网格做的体积光，实机截图（v261-portal-model2）里读成
            //   **三块斜插在广场上的木板**（管壁受光 + 投影 ⇒ 完全不透明的观感）✗
            //   结论：这种假体积光在本工程的光照下不成立。
            //   白色溢出改用**灯**（下面那盏 PORTAL_WhiteLight）+ 门洞黑盘边缘的光环表达 ✓
            TownKit.Glow(propRoot, "PORTAL_WhiteLight", s.TransformPoint(new Vector3(0f, 2.6f, 3.4f)),
                         new Color(0.94f, 0.97f, 1f), 18f, 2.0f);

            // ⑧ 门口两盏火盆（真圆柱 + 锥形火焰，位置挪到台基外侧，不压路面）
            for (int i = -1; i <= 1; i += 2)
            {
                TownKit.CylL(s, "PORTAL_Brazier", new Vector3(i * 4.60f, baseY, 1.10f), 0.28f, 0.95f, metal, true);
                TownKit.CylL(s, "PORTAL_Bowl", new Vector3(i * 4.60f, baseY + 0.95f, 1.10f), 0.42f, 0.20f, metal);
                TownKit.ConeL(s, "PORTAL_Flame", new Vector3(i * 4.60f, baseY + 1.15f, 1.10f), 0.30f, 0.62f, gold, 8);
                TownKit.Glow(propRoot, "PORTAL_Fire", s.TransformPoint(new Vector3(i * 4.60f, baseY + 1.40f, 1.10f)),
                             new Color(1f, 0.72f, 0.32f), 7f, 1.4f);
            }
            TownKit.Glow(propRoot, "PORTAL_FieldLight", s.TransformPoint(new Vector3(0f, colTop - 0.6f, 0.60f)),
                         TownColors.PortalGlow, 14f, 1.8f);

            // ★ 特效挂点：空物体、不渲染、不加碰撞 ⇒ `TownPortalFx`（黑盘 + 旋臂）挂这里
            var fx = new GameObject(TownLayout.BossPortalFxName);
            fx.transform.SetParent(s, false);
            fx.transform.localPosition = new Vector3(0f, baseY + TownPortalModel.ColumnH * 0.5f + 0.65f, 0.30f);
            fx.transform.localRotation = Quaternion.identity;
            return s;
        }

        /// <summary>
        /// ★ v261：**半透明白光材质**（门口散射光）。
        /// 为什么不用 `TownKit.Mat`：不透明白面在图上就是"白纸"；光必须叠在场景上 ⇒
        ///   ① 透明表面（URP `_Surface = 1`）② 关背面剔除（管壁从里外都看得见）
        ///   ③ α 很低（0.22）—— 多层叠起来才亮，单层不抢画面
        /// </summary>
        private static Material LightShaftMat(float alpha, string name)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return TownKit.Mat(new Color(0.97f, 0.98f, 1.00f));
            var m = new Material(sh) { name = name };
            var col = new Color(0.93f, 0.97f, 1.00f, alpha);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.renderQueue = 3100 + (int)(alpha * 100f);
            return m;
        }


        private static Transform BuildClothing(Transform root, Transform propRoot)
        {
            Transform s = Place(root, "BUILD_Clothing", TownLayout.ClothPos, TownLayout.ClothYaw);
            Material rose = TownKit.Mat(TownColors.HouseRose);
            Material warm = TownKit.Mat(TownColors.HouseWarm);
            Material blue = TownKit.Mat(TownColors.HouseBlue);
            Material roofDark = TownKit.Mat(TownColors.RoofDark);
            Material roofTile = TownKit.Mat(TownColors.RoofTile);
            Material door = TownKit.Mat(TownColors.DoorDark);
            Material glass = TownKit.Mat(TownColors.LampGlass);
            Material gold = TownKit.Mat(TownColors.SignGold);
            Material canvas = TownKit.Mat(TownColors.CanvasRed);
            Material cream = TownKit.Mat(TownColors.CanvasCream);

            // ── 第一层：方形石基（★ 实心 ★）──
            TownKit.BoxL(s, "CLOTH_Base", new Vector3(0f, 1.45f, 0f), new Vector3(3.30f, 2.90f, 3.10f), rose, true);
            TownKit.BoxL(s, "CLOTH_BaseTrim", new Vector3(0f, 2.94f, 0f), new Vector3(3.58f, 0.22f, 3.38f), roofDark);
            // ── 第二层：八角塔身（★ 实心 ★）──
            TownKit.CylL(s, "CLOTH_Mid", new Vector3(0f, 3.05f, 0f), 1.45f, 2.10f, warm, true);
            TownKit.CylL(s, "CLOTH_MidTrim", new Vector3(0f, 5.10f, 0f), 1.58f, 0.20f, roofDark);
            // ── 第三层：细塔身（★ 实心 ★）──
            TownKit.CylL(s, "CLOTH_Top", new Vector3(0f, 5.30f, 0f), 1.05f, 1.90f, blue, true);
            // ── 尖顶：8 棱锥（★ 实心 ★）──
            TownKit.ConeL(s, "CLOTH_Spire", new Vector3(0f, 7.20f, 0f), 1.34f, 2.60f, roofTile, 8, true);
            TownKit.BallL(s, "CLOTH_Finial", new Vector3(0f, 9.82f, 0f), 0.26f, gold);

            // ── 正面（+Z 朝圆心）：门 / 橱窗 / 布幌 / 招牌 ──
            TownKit.BoxL(s, "CLOTH_Door", new Vector3(0f, 1.08f, 1.56f), new Vector3(1.10f, 2.05f, 0.18f), door);
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(s, "CLOTH_Window", new Vector3(i * 1.05f, 2.35f, 1.56f),
                             new Vector3(0.72f, 0.64f, 0.12f), glass);
            TownKit.BoxL(s, "CLOTH_Awning", new Vector3(0f, 2.72f, 1.86f), new Vector3(2.10f, 0.10f, 0.85f), cream);
            TownKit.BoxL(s, "CLOTH_Sign", new Vector3(0f, 3.02f, 1.72f), new Vector3(1.55f, 0.46f, 0.12f), gold);
            // 悬挂的两幅布幌（"这是卖衣服的"最直观的读数）
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(s, "CLOTH_Banner", new Vector3(i * 1.28f, 2.00f, 1.62f),
                             new Vector3(0.46f, 1.45f, 0.06f), canvas);
            // 门口的小模特（= 一个迷你豆豆人，玩家一眼看出"衣服"）
            TownKit.CylL(s, "CLOTH_MannequinBody", new Vector3(0.05f, 0f, 2.10f), 0.17f, 0.52f, cream);
            TownKit.BallL(s, "CLOTH_MannequinHead", new Vector3(0.05f, 0.66f, 2.10f), 0.34f, warm);
            TownKit.BallL(s, "CLOTH_MannequinHat", new Vector3(0.05f, 0.86f, 2.10f), 0.30f, canvas);

            TownKit.Glow(propRoot, "CLOTH_Lamp", s.TransformPoint(new Vector3(0f, 2.55f, 2.10f)),
                         TownColors.LampGlass, 7f, 1.0f);
            // 店外的道具（复用工程里已有的装饰模型）
            Prop(propRoot, s, "crate_stack", new Vector3(-1.95f, 0f, 1.90f), 18f, 1.05f, 0.65f);
            Prop(propRoot, s, "clay_jars", new Vector3(1.90f, 0f, 1.95f), -12f, 0.70f, 0.55f);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 四、杂货商店（东南偏东 · 圆柱 + 半球圆顶）—— **不卖种子**
        // ══════════════════════════════════════════════════════════════════
        private static Transform BuildShop(Transform root, Transform propRoot)
        {
            Transform s = Place(root, "BUILD_Shop", TownLayout.ShopPos, TownLayout.ShopYaw);
            Material stone = TownKit.Mat(TownColors.Stone);
            Material stoneDark = TownKit.Mat(TownColors.StoneDark);
            Material warm = TownKit.Mat(TownColors.HouseWarm);
            Material roofTile = TownKit.Mat(TownColors.RoofTile);
            Material roofDark = TownKit.Mat(TownColors.RoofDark);
            Material door = TownKit.Mat(TownColors.DoorDark);
            Material glass = TownKit.Mat(TownColors.LampGlass);
            Material gold = TownKit.Mat(TownColors.SignGold);
            Material cream = TownKit.Mat(TownColors.CanvasCream);
            Material wood = TownKit.Mat(TownColors.WallWood);

            // 圆形台基（★ 实心：圆柱是圆的，碰撞体也是圆的 ★）
            // ⚠️ 台基半径 2.28 → 2.10：E 锚点在外环路 r=8.85 上，台基内沿必须在 8.85+身宽0.34 之外，
            //    否则玩家站到锚点的那一刻就被台基"包住"了（实测算过：2.28 时内沿 9.27 < 9.19，会挤）
            TownKit.CylL(s, "SHOP_Plinth", Vector3.zero, 2.10f, 0.26f, stone, true);
            // 圆柱塔身（★ 实心 ★）
            TownKit.CylL(s, "SHOP_Body", new Vector3(0f, 0.26f, 0f), 1.95f, 2.95f, warm, true);
            TownKit.CylL(s, "SHOP_Collar", new Vector3(0f, 3.16f, 0f), 2.12f, 0.24f, roofDark);
            // 半球圆顶（★ 实心：convex MeshCollider 逐顶点贴合 ★）
            TownKit.DomeL(s, "SHOP_Dome", new Vector3(0f, 3.40f, 0f), 2.08f, roofTile, true);
            TownKit.CylL(s, "SHOP_FinialPost", new Vector3(0f, 5.50f, 0f), 0.09f, 0.34f, gold);
            TownKit.BallL(s, "SHOP_Finial", new Vector3(0f, 5.86f, 0f), 0.30f, gold);

            // 正面：门 + 遮阳篷 + 招牌 + 两扇圆窗
            TownKit.BoxL(s, "SHOP_Door", new Vector3(0f, 1.14f, 1.90f), new Vector3(1.24f, 2.15f, 0.20f), door);
            TownKit.BoxL(s, "SHOP_Awning", new Vector3(0f, 2.70f, 2.34f), new Vector3(2.70f, 0.12f, 1.05f), cream);
            TownKit.BoxL(s, "SHOP_Sign", new Vector3(0f, 3.02f, 2.16f), new Vector3(1.95f, 0.52f, 0.14f), gold);
            for (int i = -1; i <= 1; i += 2)
                TownKit.BallL(s, "SHOP_Porthole", new Vector3(i * 1.26f, 2.30f, 1.60f), 0.62f, glass);
            // 台阶
            TownKit.BoxL(s, "SHOP_Step", new Vector3(0f, 0.07f, 2.24f), new Vector3(1.90f, 0.14f, 0.82f), stone);

            // 店外：货箱 + 木桶（"杂货"的读数）
            Prop(propRoot, s, "crate_stack", new Vector3(-1.90f, 0f, 2.05f), -22f, 1.15f, 0.70f);
            Prop(propRoot, s, "barrel", new Vector3(1.95f, 0f, 2.10f), 15f, 0.90f, 0.45f);
            Prop(propRoot, s, "barrel", new Vector3(2.35f, 0f, 1.35f), -40f, 0.80f, 0.42f);
            // 货架（贴着墙面，纯装饰）
            for (int k = 0; k < 2; k++)
                TownKit.BoxL(s, "SHOP_Shelf", new Vector3(-1.05f + k * 2.10f, 1.15f, 2.02f),
                             new Vector3(0.90f, 1.60f, 0.22f), wood);

            TownKit.Glow(propRoot, "SHOP_Lamp", s.TransformPoint(new Vector3(0f, 2.45f, 2.55f)),
                         TownColors.LampGlass, 8f, 1.15f);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 五、种子仓库（西南偏西 · 平顶长仓房）
        // ══════════════════════════════════════════════════════════════════
        private static Transform BuildWarehouse(Transform root, Transform propRoot)
        {
            Transform s = Place(root, "BUILD_Warehouse", TownLayout.WarePos, TownLayout.WareYaw);
            Material wood = TownKit.Mat(TownColors.WallWood);
            Material woodDark = TownKit.Mat(TownColors.WallWoodDark);
            Material roofDark = TownKit.Mat(TownColors.RoofDark);
            Material door = TownKit.Mat(TownColors.DoorDark);
            Material metal = TownKit.Mat(TownColors.MetalDark);
            Material gold = TownKit.Mat(TownColors.SignGold);
            Material stone = TownKit.Mat(TownColors.Stone);

            // 仓身（★ 实心 ★）
            TownKit.BoxL(s, "WARE_Body", new Vector3(0f, 1.70f, 0f), new Vector3(6.20f, 3.40f, 3.20f), wood, true);
            // 平顶（★ 实心：一层略外挑的薄板，没有屋脊 = 平顶 ★）
            TownKit.BoxL(s, "WARE_Roof", new Vector3(0f, 3.50f, 0f), new Vector3(6.55f, 0.26f, 3.55f), roofDark, true);
            // 女儿墙（平顶的矮墙，两段）
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(s, "WARE_Parapet", new Vector3(i * 3.10f, 3.76f, 0f),
                             new Vector3(0.20f, 0.30f, 3.50f), roofDark);
            // 木构的竖柱（读起来是"仓库"，不是民房）
            for (int k = -1; k <= 1; k++)
                TownKit.BoxL(s, "WARE_Stud", new Vector3(k * 2.05f, 1.70f, 1.62f),
                             new Vector3(0.22f, 3.30f, 0.10f), woodDark);
            // 屋顶的通风口（★ 实心 ★）
            TownKit.BoxL(s, "WARE_Vent", new Vector3(1.85f, 4.02f, -0.55f), new Vector3(0.75f, 0.80f, 0.75f), stone, true);

            // 装卸台（★ 实心 ★）+ 坡道（无碰撞：这是走上去的路）
            // ⚠️ 装卸台从 local z=1.95 收到 1.55：它的前沿原来伸到 r=9.00，而 E 锚点在 r=8.85（+身宽 0.34）
            //    ⇒ 玩家站到锚点上会被台子挤住；收到 1.55 之后前沿 r=9.40，离锚点 0.55 米 ✓
            TownKit.BoxL(s, "WARE_Dock", new Vector3(0f, 0.24f, 1.55f), new Vector3(5.30f, 0.48f, 1.10f), woodDark, true);
            TownKit.BoxL(s, "WARE_Ramp", new Vector3(0f, 0.12f, 2.36f), new Vector3(2.40f, 0.24f, 0.80f), woodDark);
            // 滑门（贴着仓身正面，无碰撞）
            TownKit.BoxL(s, "WARE_Door", new Vector3(0f, 1.28f, 1.62f), new Vector3(2.30f, 2.55f, 0.16f), door);
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(s, "WARE_DoorRail", new Vector3(i * 1.28f, 1.28f, 1.66f),
                             new Vector3(0.14f, 2.60f, 0.10f), metal);
            TownKit.BoxL(s, "WARE_Sign", new Vector3(0f, 3.10f, 1.66f), new Vector3(2.70f, 0.56f, 0.12f), gold);
            // 门楣横梁
            TownKit.BoxL(s, "WARE_Lintel", new Vector3(0f, 2.72f, 1.66f), new Vector3(2.90f, 0.24f, 0.20f), woodDark);

            // 仓库门口三件套（复用工程模型）
            Prop(propRoot, s, "crate_stack", new Vector3(-2.60f, 0f, 2.30f), 12f, 1.20f, 0.75f);
            Prop(propRoot, s, "haystack", new Vector3(2.65f, 0f, 2.30f), -8f, 1.35f, 0.95f);
            Prop(propRoot, s, "barrel", new Vector3(-3.05f, 0f, 1.35f), 0f, 0.90f, 0.45f);
            Prop(propRoot, s, "wheelbarrow", new Vector3(3.30f, 0f, 1.20f), 35f, 0.85f, 0.75f);

            TownKit.Glow(propRoot, "WARE_Lamp", s.TransformPoint(new Vector3(0f, 2.95f, 2.35f)),
                         TownColors.LampGlass, 9f, 1.2f);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 六、集市帐篷（南偏西 · 四棱锥帆布顶）
        // ══════════════════════════════════════════════════════════════════
        private static Transform BuildTent(Transform root, Transform propRoot)
        {
            Transform s = Place(root, "BUILD_Tent", TownLayout.TentPos, TownLayout.TentYaw);
            Material canvas = TownKit.Mat(TownColors.CanvasRed);
            Material cream = TownKit.Mat(TownColors.CanvasCream);
            Material wood = TownKit.Mat(TownColors.WallWood);
            Material metal = TownKit.Mat(TownColors.MetalDark);
            Material gold = TownKit.Mat(TownColors.SignGold);

            // 柜台（★ 实心 ★）
            TownKit.BoxL(s, "TENT_Counter", new Vector3(0f, 0.50f, 0.40f), new Vector3(3.00f, 1.00f, 0.90f), wood, true);
            TownKit.BoxL(s, "TENT_CounterTop", new Vector3(0f, 1.04f, 0.40f), new Vector3(3.20f, 0.10f, 1.02f), cream);
            // 四根支柱（★ 实心 ★）
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                    TownKit.CylL(s, "TENT_Post", new Vector3(ix * 1.55f, 0f, iz * 1.55f + 0.40f),
                                 0.09f, 2.45f, metal, true);
            // 帆布顶：四棱锥（★ 实心 ★）
            TownKit.ConeL(s, "TENT_Roof", new Vector3(0f, 2.45f, 0.40f), 2.55f, 1.45f, canvas, 4, true);
            // 檐口的波浪边（四段米色薄板，把"帆布"读出来）
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                Vector3 off = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 2.42f, 1.95f);
                GameObject eave = TownKit.BoxL(s, "TENT_Eave", off + new Vector3(0f, 0f, 0.40f),
                                               new Vector3(3.60f, 0.24f, 0.08f), cream);
                eave.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            }
            // 旗杆 + 小旗
            TownKit.CylL(s, "TENT_Pole", new Vector3(0f, 3.85f, 0.40f), 0.045f, 0.70f, metal);
            TownKit.BoxL(s, "TENT_Flag", new Vector3(0.26f, 4.32f, 0.40f), new Vector3(0.50f, 0.30f, 0.03f), gold);
            // 柜台上的货（三种主城种子的颜色 —— 但这里**不卖种子**，只是"集市卖东西"的视觉）
            for (int i = -1; i <= 1; i++)
            {
                Color c = TownData.SeedAt(i + 1) != null ? TownData.TintAt(i + 1) : TownColors.Gold;
                TownKit.BallL(s, "TENT_Goods", new Vector3(i * 0.85f, 1.22f, 0.40f), 0.32f, TownKit.Mat(c));
            }
            Prop(propRoot, s, "clay_jars", new Vector3(1.95f, 0f, 0.55f), 24f, 0.70f, 0.55f);
            Prop(propRoot, s, "barrel", new Vector3(-1.95f, 0f, 0.55f), -18f, 0.88f, 0.44f);
            TownKit.Glow(propRoot, "TENT_Lamp", s.TransformPoint(new Vector3(0f, 2.20f, 0.40f)),
                         TownColors.LampGlass, 7f, 1.1f);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 七、南门（正南 · 双石柱 + 横幅）
        // ══════════════════════════════════════════════════════════════════
        private static Transform BuildGate(Transform root, Transform propRoot)
        {
            Transform s = Place(root, "BUILD_Gate", TownLayout.GatePos, TownLayout.GateYaw);
            Material stone = TownKit.Mat(TownColors.Stone);
            Material stoneDark = TownKit.Mat(TownColors.StoneDark);
            Material metal = TownKit.Mat(TownColors.MetalDark);
            Material glass = TownKit.Mat(TownColors.LampGlass);
            Material canvas = TownKit.Mat(TownColors.CanvasRed);

            for (int i = -1; i <= 1; i += 2)
            {
                TownKit.BoxL(s, "GATE_Post", new Vector3(i * 2.05f, 1.85f, 0f), new Vector3(0.62f, 3.70f, 0.62f),
                             stone, true);
                TownKit.BoxL(s, "GATE_PostCap", new Vector3(i * 2.05f, 3.82f, 0f), new Vector3(0.86f, 0.26f, 0.86f),
                             stoneDark);
                TownKit.BallL(s, "GATE_Lantern", new Vector3(i * 2.05f, 3.10f, 0.42f), 0.30f, glass);
                TownKit.Glow(propRoot, "GATE_LanternLight", s.TransformPoint(new Vector3(i * 2.05f, 3.05f, 0.42f)),
                             TownColors.LampGlass, 6f, 0.9f);
            }
            TownKit.BoxL(s, "GATE_Arch", new Vector3(0f, 3.55f, 0f), new Vector3(4.30f, 0.52f, 0.52f), stone, true);
            TownKit.BoxL(s, "GATE_Banner", new Vector3(0f, 2.95f, 0.32f), new Vector3(2.20f, 0.62f, 0.08f), canvas);
            for (int i = -1; i <= 1; i += 2)
                TownKit.CylL(s, "GATE_Hook", new Vector3(i * 1.10f, 3.10f, 0.30f), 0.05f, 0.40f, metal);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 七之二、**家**（v216：功能性建筑 —— `E` 开图鉴）
        //   用户原话：「左侧增加功能性建筑（家），互动后展示图鉴（已经解锁的植物、道具、插件，
        //   全部详细介绍，分页展示，还有'成就'，先留空）」
        //   外形：三段式的"小屋" —— 石基 + 木墙 + 双坡屋顶（用两个斜置方块做坡）+ 门廊 + 烟囱 + 窗
        //   ⇒ 与另外五栋（拱门/塔楼/圆顶/平顶仓/帐篷）**又是一种形状** ✓
        // ══════════════════════════════════════════════════════════════════
        private static Transform BuildHome(Transform root, Transform propRoot)
        {
            Transform s = Place(root, "BUILD_Home", TownLayout.HomePos, TownLayout.HomeYaw);
            Material stone = TownKit.Mat(TownColors.Stone);
            Material wood = TownKit.Mat(TownColors.WallWood);
            Material woodDark = TownKit.Mat(TownColors.WallWoodDark);
            Material roof = TownKit.Mat(TownColors.RoofTile);
            Material roofDark = TownKit.Mat(TownColors.RoofDark);
            Material door = TownKit.Mat(TownColors.DoorDark);
            Material glass = TownKit.Mat(TownColors.LampGlass);
            Material gold = TownKit.Mat(TownColors.SignGold);
            Material cream = TownKit.Mat(TownColors.CanvasCream);

            // 石基（★ 实心 ★）
            TownKit.BoxL(s, "HOME_Plinth", new Vector3(0f, 0.14f, 0f), new Vector3(4.80f, 0.28f, 3.80f), stone, true);
            // 木墙（★ 实心 ★）
            TownKit.BoxL(s, "HOME_Body", new Vector3(0f, 1.72f, 0f), new Vector3(4.40f, 2.90f, 3.40f), wood, true);
            // 双坡屋顶：两块**斜的**方块 —— 这是本工程第一次用非 90° 的零件，
            //   碰撞体不存在于斜屋顶上（6 米高，玩家够不到）⇒ 不担心 BoxCollider 贴合问题
            for (int i = -1; i <= 1; i += 2)
            {
                GameObject slope = TownKit.BoxL(s, "HOME_RoofSlope", new Vector3(i * 1.22f, 3.86f, 0f),
                                                new Vector3(2.95f, 0.22f, 3.80f), roof);
                slope.transform.localRotation = Quaternion.Euler(0f, 0f, i * 26f);
            }
            TownKit.BoxL(s, "HOME_Ridge", new Vector3(0f, 4.52f, 0f), new Vector3(0.34f, 0.26f, 3.86f), roofDark);
            TownKit.BoxL(s, "HOME_GableN", new Vector3(0f, 3.62f, 1.72f), new Vector3(3.10f, 1.40f, 0.16f), cream);
            TownKit.BoxL(s, "HOME_GableS", new Vector3(0f, 3.62f, -1.72f), new Vector3(3.10f, 1.40f, 0.16f), cream);
            // 门廊（两柱 + 顶板）+ 门窗
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(s, "HOME_PorchPost", new Vector3(i * 1.35f, 1.05f, 2.05f),
                             new Vector3(0.20f, 2.10f, 0.20f), woodDark, true);
            TownKit.BoxL(s, "HOME_PorchRoof", new Vector3(0f, 2.22f, 2.00f), new Vector3(3.30f, 0.18f, 1.10f), roofDark);
            TownKit.BoxL(s, "HOME_Step", new Vector3(0f, 0.08f, 2.30f), new Vector3(2.40f, 0.16f, 0.90f), stone);
            TownKit.BoxL(s, "HOME_Door", new Vector3(0f, 1.10f, 1.72f), new Vector3(1.15f, 2.10f, 0.16f), door);
            TownKit.BoxL(s, "HOME_DoorFrame", new Vector3(0f, 1.16f, 1.80f), new Vector3(1.45f, 2.36f, 0.10f), woodDark);
            for (int i = -1; i <= 1; i += 2)
            {
                TownKit.BoxL(s, "HOME_Window", new Vector3(i * 1.40f, 1.95f, 1.74f),
                             new Vector3(0.85f, 0.85f, 0.12f), glass);
                TownKit.BoxL(s, "HOME_Shutter", new Vector3(i * 1.40f, 1.95f, 1.70f),
                             new Vector3(1.05f, 1.05f, 0.06f), woodDark);
            }
            // 烟囱 + 屋顶上的小旗（"家"的识别点）
            TownKit.CylL(s, "HOME_Chimney", new Vector3(1.35f, 4.20f, -0.90f), 0.26f, 1.20f, stone, true);
            TownKit.CylL(s, "HOME_FlagPole", new Vector3(-1.30f, 4.50f, 0.80f), 0.05f, 1.10f, woodDark);
            TownKit.BoxL(s, "HOME_Flag", new Vector3(-0.95f, 5.42f, 0.80f), new Vector3(0.70f, 0.42f, 0.03f), gold);
            // 招牌（"家 · 图鉴"）
            TownKit.BoxL(s, "HOME_Sign", new Vector3(0f, 2.62f, 1.86f), new Vector3(1.70f, 0.44f, 0.10f), gold);

            TownKit.Glow(propRoot, "HOME_Lamp", s.TransformPoint(new Vector3(0f, 2.40f, 2.30f)),
                         TownColors.LampGlass, 8f, 1.15f);
            Prop(propRoot, s, "barrel", new Vector3(-1.95f, 0f, 2.15f), 0f, 0.88f, 0.44f);
            Prop(propRoot, s, "crate_stack", new Vector3(1.95f, 0f, 2.20f), 18f, 1.05f, 0.65f);
            Prop(propRoot, s, "laundry_line", new Vector3(-2.60f, 0f, -1.20f), 90f, 1.80f, 1.35f);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 七之三、**右上角装饰性房屋 ×3** ＋ **右下角未开发地块**（v216）
        //   用户原话：「地图右侧太空，右上角部分增加装饰性房屋，右下角用护栏围着（告示牌写着未开发）」
        //   三栋小房**不可交互**（没有锚点、不进 `E` 判定），只为把空草坪填满；
        //   每栋一种屋顶：单坡棚 / 双坡小屋 / 圆顶灶房 —— 与可交互六栋都不重样 ✓
        // ══════════════════════════════════════════════════════════════════
        public static void BuildDecorHouses(Transform root, Transform propRoot)
        {
            Material wood = TownKit.Mat(TownColors.WallWood);
            Material warm = TownKit.Mat(TownColors.HouseWarm);
            Material rose = TownKit.Mat(TownColors.HouseRose);
            Material blue = TownKit.Mat(TownColors.HouseBlue);
            Material roofDark = TownKit.Mat(TownColors.RoofDark);
            Material roofTile = TownKit.Mat(TownColors.RoofTile);
            Material stone = TownKit.Mat(TownColors.Stone);
            Material door = TownKit.Mat(TownColors.DoorDark);
            Material glass = TownKit.Mat(TownColors.LampGlass);

            // ① 单坡棚屋（东偏北）—— 一大块斜屋顶
            Transform a = Place(root, "DECOR_Shed", TownLayout.DecorHouses[0], 205f);
            TownKit.BoxL(a, "DECOR_ShedBody", new Vector3(0f, 1.20f, 0f), new Vector3(3.60f, 2.40f, 3.00f), warm, true);
            GameObject shedRoof = TownKit.BoxL(a, "DECOR_ShedRoof", new Vector3(0f, 2.62f, 0f),
                                               new Vector3(4.10f, 0.20f, 3.50f), roofDark);
            shedRoof.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
            TownKit.BoxL(a, "DECOR_ShedDoor", new Vector3(0f, 0.95f, 1.54f), new Vector3(1.05f, 1.90f, 0.14f), door);
            TownKit.BoxL(a, "DECOR_ShedWin", new Vector3(1.10f, 1.70f, 1.54f), new Vector3(0.62f, 0.62f, 0.10f), glass);
            Prop(propRoot, a, "haystack", new Vector3(-2.30f, 0f, 1.60f), 12f, 1.15f, 0.85f);
            Prop(propRoot, a, "barrel", new Vector3(2.05f, 0f, 1.55f), 0f, 0.85f, 0.42f);

            // ② 双坡小屋（东）
            Transform b = Place(root, "DECOR_Cottage", TownLayout.DecorHouses[1], 230f);
            TownKit.BoxL(b, "DECOR_CottageBody", new Vector3(0f, 1.35f, 0f), new Vector3(3.20f, 2.70f, 2.90f), rose, true);
            for (int i = -1; i <= 1; i += 2)
            {
                GameObject g = TownKit.BoxL(b, "DECOR_CottageRoof", new Vector3(i * 0.86f, 3.00f, 0f),
                                            new Vector3(2.05f, 0.18f, 3.30f), roofTile);
                g.transform.localRotation = Quaternion.Euler(0f, 0f, i * 30f);
            }
            TownKit.BoxL(b, "DECOR_CottageDoor", new Vector3(0f, 0.95f, 1.50f), new Vector3(0.95f, 1.90f, 0.14f), door);
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(b, "DECOR_CottageWin", new Vector3(i * 1.05f, 1.75f, 1.50f),
                             new Vector3(0.58f, 0.58f, 0.10f), glass);
            TownKit.CylL(b, "DECOR_CottagePot", new Vector3(1.70f, 0f, 1.70f), 0.24f, 0.40f, stone, true);
            Prop(propRoot, b, "bush", new Vector3(-1.90f, 0f, 1.55f), 0f, 0.75f, 0.58f);

            // ③ 圆顶灶房（北偏东）
            Transform c = Place(root, "DECOR_Bakehouse", TownLayout.DecorHouses[2], 250f);
            TownKit.CylL(c, "DECOR_BakeBody", new Vector3(0f, 0f, 0f), 1.55f, 2.10f, blue, true);
            TownKit.DomeL(c, "DECOR_BakeDome", new Vector3(0f, 2.10f, 0f), 1.62f, roofTile, true);
            TownKit.BoxL(c, "DECOR_BakeDoor", new Vector3(0f, 0.85f, 1.40f), new Vector3(0.95f, 1.70f, 0.16f), door);
            TownKit.BoxL(c, "DECOR_BakeSign", new Vector3(0f, 1.80f, 1.52f), new Vector3(1.10f, 0.36f, 0.10f), wood);
            Prop(propRoot, c, "clay_jars", new Vector3(1.70f, 0f, 1.20f), 15f, 0.68f, 0.52f);
            Prop(propRoot, c, "wheelbarrow", new Vector3(-1.80f, 0f, 1.10f), 40f, 0.80f, 0.72f);
        }

        /// <summary>
        /// 右下角（东南）**用护栏围起来的未开发地块** ＋ 一块写着「未开发」的告示牌。
        /// 围栏是**可走过去**的（0.9 米高、没有碰撞）—— 它表达"这块地圈起来了"，不是一堵墙；
        /// 真正的边界仍然是城界的圆形边界 ✓
        /// </summary>
        public static void BuildUnbuiltLot(Transform root, Transform propRoot)
        {
            Material wood = TownKit.Mat(TownColors.WallWoodDark);
            Material post = TownKit.Mat(TownColors.WallWood);
            Material sign = TownKit.Mat(TownColors.SignGold);
            Material signBoard = TownKit.Mat(TownColors.CanvasCream);
            Rect lot = TownLayout.UnbuiltLot;

            // 四边围栏：横杆两根 + 立柱若干（全都没有碰撞）
            FenceLine(root, propRoot, new Vector3(lot.xMin, 0f, lot.yMin), new Vector3(lot.xMax, 0f, lot.yMin), wood, post, "FENCE_S");
            FenceLine(root, propRoot, new Vector3(lot.xMin, 0f, lot.yMax), new Vector3(lot.xMax, 0f, lot.yMax), wood, post, "FENCE_N");
            FenceLine(root, propRoot, new Vector3(lot.xMin, 0f, lot.yMin), new Vector3(lot.xMin, 0f, lot.yMax), wood, post, "FENCE_W");
            FenceLine(root, propRoot, new Vector3(lot.xMax, 0f, lot.yMin), new Vector3(lot.xMax, 0f, lot.yMax), wood, post, "FENCE_E");

            // 地块内部：夯土 + 两堆木料（"没开发"的样子）
            Vector2 mid = new Vector2(lot.center.x, lot.center.y);
            TownKit.Box(root, "LOT_UnbuiltSoil", new Vector3(mid.x, SlabY, mid.y),
                        new Vector3(lot.width - 0.4f, 0.04f, lot.height - 0.4f),
                        TownKit.Mat(TownColors.TilledSoil));
            PropAt(propRoot, "log", new Vector2(lot.xMin + 1.6f, lot.center.y), 20f, 0.55f, 0.75f);
            PropAt(propRoot, "rock_a", new Vector2(lot.xMax - 1.8f, lot.center.y - 1.4f), 60f, 0.62f, 0.58f);
            PropAt(propRoot, "stump", new Vector2(lot.center.x + 1.2f, lot.yMax - 1.6f), 0f, 0.55f, 0.48f);

            // 告示牌：两根柱子 + 一块斜钉的板 + 一根横梁（朝城内 = 朝北）
            Transform s = Place(root, "SIGN_Unbuilt", TownLayout.UnbuiltSignPos, 0f);
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(s, "SIGN_Post", new Vector3(i * 0.85f, 0.90f, 0f), new Vector3(0.16f, 1.80f, 0.16f), post);
            TownKit.BoxL(s, "SIGN_Board", new Vector3(0f, 1.62f, 0.06f), new Vector3(2.10f, 0.86f, 0.10f), signBoard);
            TownKit.BoxL(s, "SIGN_Frame", new Vector3(0f, 1.62f, 0.10f), new Vector3(2.26f, 1.02f, 0.05f), wood);
            TownKit.BoxL(s, "SIGN_Badge", new Vector3(0f, 2.14f, 0.12f), new Vector3(0.70f, 0.20f, 0.06f), sign);
        }

        /// <summary>一层薄板的地面高度（与 `TownBootstrap.SlabTopY` 同口径）。</summary>
        private const float SlabY = 0.014f;

        /// <summary>造一段围栏（两根横杆 + 每 1.6 米一根立柱）。</summary>
        private static void FenceLine(Transform root, Transform propRoot, Vector3 a, Vector3 b,
                                      Material rail, Material post, string name)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.2f) return;
            Vector3 dir = d / len;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            int posts = Mathf.Max(2, Mathf.RoundToInt(len / 1.6f) + 1);
            for (int i = 0; i < posts; i++)
            {
                Vector3 p = a + dir * (len * i / (posts - 1));
                GameObject go = TownKit.Box(root, name + "_Post" + i, new Vector3(p.x, 0.46f, p.z),
                                            new Vector3(0.12f, 0.92f, 0.12f), post);
            }
            for (int r = 0; r < 2; r++)
            {
                GameObject railGo = TownKit.Box(root, name + "_Rail" + r,
                                                new Vector3((a.x + b.x) * 0.5f, 0.34f + r * 0.42f, (a.z + b.z) * 0.5f),
                                                new Vector3(0.06f, 0.09f, len), rail);
                railGo.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 八、公园中央：喷泉 + 雕塑 + 大树（"中心为公园中央"）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>中心喷泉（三层水盘 + 顶球）。**加碰撞**：它是实体，玩家不能穿过去。</summary>
        public static void BuildFountain(Transform root, Transform propRoot)
        {
            Material stone = TownKit.Mat(TownColors.Stone);
            Material stoneDark = TownKit.Mat(TownColors.StoneDark);
            Material water = TownKit.Mat(TownColors.Water);
            Material gold = TownKit.Mat(TownColors.Gold);

            Transform f = new GameObject("PARK_Fountain").transform;
            f.SetParent(root, false);        // 不旋转：喷泉的零件用局部坐标 == 世界坐标，少一层换算
            // 池沿（★ 实心 ★）
            TownKit.CylL(f, "FOUNT_Basin", Vector3.zero, 1.28f, 0.52f, stoneDark, true);
            // 水面（略低于池沿，无碰撞 —— 玩家只会被池沿挡住）
            TownKit.Plate(f, "FOUNT_Water", TownKit.Disc(1.06f, 28), new Vector3(0f, 0.50f, 0f), water);
            // 中柱 + 两层小盘 + 顶球（★ 实心 ★）
            TownKit.CylL(f, "FOUNT_Column", new Vector3(0f, 0.52f, 0f), 0.20f, 1.55f, stone, true);
            TownKit.CylL(f, "FOUNT_BowlLow", new Vector3(0f, 1.30f, 0f), 0.62f, 0.16f, stone);
            TownKit.CylL(f, "FOUNT_Column2", new Vector3(0f, 1.46f, 0f), 0.12f, 0.95f, stone);
            TownKit.CylL(f, "FOUNT_BowlHigh", new Vector3(0f, 1.95f, 0f), 0.40f, 0.13f, stone);
            TownKit.BallL(f, "FOUNT_TopBall", new Vector3(0f, 2.62f, 0f), 0.30f, gold);
            TownKit.Glow(propRoot, "FOUNT_Glow", new Vector3(0f, 1.30f, 0f), new Color(0.62f, 0.86f, 1f), 7f, 0.9f);
        }

        /// <summary>
        /// ★ v215：**公园区**（西侧独立一块草坪，中心 = `TownLayout.ParkCenter`）＋ 中央广场的草坪环 ＋ 城外绿化环 ★
        ///
        /// 三层的分工：
        ///   · **公园区**（用户点名的"左侧容纳公园"）：11×11 的独立草坪，里面有池塘/水井/大树/
        ///     长椅/花圃/芦苇/鸟屋 —— 一眼就能认出"这是公园"，不是一个散落的装饰堆 ✓
        ///   · **中央广场环**（r 3.6~7.4）：喷泉外围的绿带，树与路灯把"中心"围出来 ✓
        ///   · **城外绿化环**（r 19~21.6）：建筑之间的空档才放，角度与六座楼错开 ✓
        /// 位置全部写死（不用极坐标等分）—— 用户点名不要"高度重复的排列"，等分摆一圈就是最典型的重复 ✗
        /// </summary>
        public static void BuildPark(Transform propRoot)
        {
            Vector2 c = TownLayout.ParkCenter;

            // ── 公园区：草坪上的六棵树（位置与高度都不同）──
            TreeAt(propRoot, "tree", new Vector2(c.x - 3.6f, c.y + 3.4f), 20f, 3.90f, 1.75f);
            TreeAt(propRoot, "tree", new Vector2(c.x + 3.9f, c.y + 3.2f), -35f, 3.35f, 1.60f);
            TreeAt(propRoot, "tree", new Vector2(c.x - 4.0f, c.y - 3.3f), 110f, 4.05f, 1.85f);
            TreeAt(propRoot, "tree", new Vector2(c.x + 3.5f, c.y - 3.6f), 250f, 3.50f, 1.65f);
            TreeAt(propRoot, "tree", new Vector2(c.x + 0.6f, c.y + 4.4f), 75f, 3.10f, 1.50f);
            TreeAt(propRoot, "tree", new Vector2(c.x - 1.2f, c.y - 4.6f), 200f, 2.90f, 1.45f);
            // ── 公园设施：池塘（西北）+ 水井（东）+ 长椅两张 + 花圃 + 芦苇 + 鸟屋 ──
            PropAt(propRoot, "pond", new Vector2(c.x - 2.6f, c.y + 1.2f), 20f, 0.45f, 1.70f);
            PropAt(propRoot, "well", new Vector2(c.x + 2.9f, c.y - 0.6f), 0f, 1.45f, 0.92f);
            PropAt(propRoot, "reeds", new Vector2(c.x - 3.4f, c.y + 0.2f), 0f, 0.85f, 0.60f);
            PropAt(propRoot, "rock_a", new Vector2(c.x - 1.4f, c.y + 2.4f), 30f, 0.62f, 0.58f);
            PropAt(propRoot, "rock_b", new Vector2(c.x + 1.8f, c.y + 2.9f), 70f, 0.52f, 0.48f);
            PropAt(propRoot, "bush", new Vector2(c.x + 2.4f, c.y + 1.4f), 0f, 0.82f, 0.62f);
            PropAt(propRoot, "bush", new Vector2(c.x - 2.2f, c.y - 2.4f), 60f, 0.72f, 0.56f);
            PropAt(propRoot, "flower_a", new Vector2(c.x + 0.4f, c.y + 2.2f), 0f, 0.44f, 0.36f);
            PropAt(propRoot, "flower_b", new Vector2(c.x - 0.8f, c.y + 3.0f), 15f, 0.44f, 0.36f);
            PropAt(propRoot, "flower_a", new Vector2(c.x + 1.2f, c.y - 2.0f), 35f, 0.40f, 0.33f);
            PropAt(propRoot, "grass", new Vector2(c.x - 3.0f, c.y - 1.2f), 0f, 0.36f, 0.40f);
            PropAt(propRoot, "grass", new Vector2(c.x + 3.2f, c.y + 1.8f), 40f, 0.36f, 0.40f);
            PropAt(propRoot, "stump", new Vector2(c.x + 2.0f, c.y + 3.6f), 0f, 0.50f, 0.45f);
            PropAt(propRoot, "birdhouse", new Vector2(c.x - 2.0f, c.y + 4.0f), 25f, 1.10f, 0.40f);
            // 公园里两张长椅（面朝池塘）
            BenchAt(propRoot, new Vector2(c.x - 1.0f, c.y - 1.6f), 12f);
            BenchAt(propRoot, new Vector2(c.x + 1.4f, c.y + 0.4f), 195f);
            // v217：公园边缘再补 4 棵（成簇，不挡小径）
            TreeAt(propRoot, "tree", new Vector2(c.x - 3.4f, c.y - 1.0f), 45f, 3.55f, 1.62f);
            TreeAt(propRoot, "tree", new Vector2(c.x + 3.2f, c.y - 2.9f), 160f, 3.15f, 1.48f);
            TreeAt(propRoot, "tree", new Vector2(c.x - 0.6f, c.y - 3.4f), 285f, 3.70f, 1.70f);
            TreeAt(propRoot, "tree", new Vector2(c.x + 2.6f, c.y + 3.2f), 340f, 3.25f, 1.52f);
            TownKit.Glow(propRoot, "PARK_Lamp", new Vector3(c.x + 0.4f, 2.4f, c.y), TownColors.LampGlass, 10f, 1.0f);

            // ── 中央广场外的绿带（r 4.4 ~ 6.4）：树、灌木、花圃，角度刻意不均匀 ──
            //   v217：树加量（用户："多放树…加量 2~3 倍，成簇围在草地/公园边缘"）
            for (int i = 0; i < BeltTreeAngles.Length; i++)
            {
                float deg = BeltTreeAngles[i];
                float r = 4.85f + (i % 3) * 0.45f;
                Tree(propRoot, deg, r, BeltTreeHeights[i % BeltTreeHeights.Length],
                     BeltTreeRadii[i % BeltTreeRadii.Length], BeltTreeAngles[(i + 4) % BeltTreeAngles.Length] * 1.7f);
            }
            PropPolar(propRoot, "bush", 165f, 5.95f, 0f, 0.80f, 0.60f);
            PropPolar(propRoot, "bush", 280f, 5.90f, 60f, 0.74f, 0.56f);
            PropPolar(propRoot, "flower_a", 42f, 6.05f, 0f, 0.42f, 0.35f);
            PropPolar(propRoot, "flower_b", 218f, 6.10f, 15f, 0.42f, 0.35f);
            // 广场上的四张长椅（朝圆心坐）
            for (int i = 0; i < 4; i++)
                Bench(propRoot, 45f + i * 90f + 12f, 2.35f);

            // ── 城外绿化环（r 18.4~20.8）：**成簇**的树，避开六座楼与四条锚点 ──
            for (int i = 0; i < OuterTreeAngles.Length; i++)
            {
                float deg = OuterTreeAngles[i];
                float r = 18.40f + (i % 4) * 0.62f;
                Tree(propRoot, deg, r, OuterTreeHeights[i % OuterTreeHeights.Length],
                     OuterTreeRadii[i % OuterTreeRadii.Length], OuterTreeAngles[(i + 5) % OuterTreeAngles.Length] * 1.3f);
            }
            PropPolar(propRoot, "windmill", 152f, 20.40f, 25f, 6.20f, 2.20f);   // 城西的风车（地标）
            // ★ 稻草人：v217 把农田改成 6×6 方格后，旧的极坐标 (176°, r=8.60) 正好落在第 22 号田格里 ✗
            //   实测（`TownBootstrap.FieldSquatters`）：(-8.58,0.60) 站在田格上 —— 田里"长"出一株
            //   跟任何种子都没关系的植物。⇒ 原意保留（农田南缘、面朝北看着田），位置挪到田块外 ✓
            PropAt(propRoot, "scarecrow", new Vector2(-8.20f, -8.90f), 0f, 1.40f, 0.58f);
            PropPolar(propRoot, "haystack", 205f, 20.20f, 15f, 1.20f, 0.85f);
            PropPolar(propRoot, "log", 232f, 20.30f, 70f, 0.55f, 0.75f);
            PropPolar(propRoot, "wall_ruin", 111f, 20.50f, 35f, 1.60f, 1.40f);
            PropPolar(propRoot, "wall_ruin_b", 118f, 20.60f, -20f, 1.30f, 1.10f);
            PropPolar(propRoot, "laundry_line", 42f, 20.50f, 10f, 1.85f, 1.40f);
            PropPolar(propRoot, "chicken_coop", 78f, 20.60f, -15f, 1.30f, 1.00f);
            PropPolar(propRoot, "mushroom", 246f, 20.50f, 0f, 0.40f, 0.35f);
            PropPolar(propRoot, "mushroom", 252f, 20.30f, 40f, 0.32f, 0.30f);
        }

        // 树的角度/高度/半径表：**刻意不均匀**（成簇、疏密不同），用户点名"不要高度重复的排列" ✓
        private static readonly float[] BeltTreeAngles = { 12f, 47f, 74f, 101f, 133f, 158f, 196f, 222f, 251f, 277f, 312f, 338f };
        private static readonly float[] BeltTreeHeights = { 3.60f, 3.15f, 3.85f, 3.30f, 3.50f };
        private static readonly float[] BeltTreeRadii = { 1.70f, 1.50f, 1.80f, 1.55f, 1.65f };
        private static readonly float[] OuterTreeAngles =
        {
            8f, 26f, 55f, 72f, 96f, 112f, 130f, 148f, 168f, 188f,
            205f, 225f, 242f, 262f, 288f, 306f, 326f, 344f,
        };
        private static readonly float[] OuterTreeHeights = { 3.55f, 3.20f, 3.80f, 3.35f, 3.65f, 3.05f };
        private static readonly float[] OuterTreeRadii = { 1.68f, 1.52f, 1.78f, 1.58f, 1.72f, 1.45f };

        /// <summary>路灯：环岛 8 盏 ＋ **右侧商业街 6 盏**（v217：路心只留路灯，装饰全挪边角）。</summary>
        public static void BuildLamps(Transform root, Transform propRoot)
        {
            Material metal = TownKit.Mat(TownColors.MetalDark);
            Material glass = TownKit.Mat(TownColors.LampGlass);
            var spots = new System.Collections.Generic.List<(float deg, float r)>();
            for (int i = 0; i < LampCount; i++) spots.Add((22.5f + i * 45f, 3.90f));   // 环岛
            // 商业街两侧交替（西侧 x=街西沿内 0.5，东侧 x=街东沿内 0.5）
            for (int i = 0; i < 6; i++)
            {
                float z = TownLayout.ShopStreetZMin + 1.6f + i * 5.4f;
                float x = (i % 2 == 0) ? TownLayout.ShopStreetXMin + 0.55f : TownLayout.ShopStreetXMax - 0.55f;
                string name = "LAMP_S" + (i + 1).ToString("00");
                BuildLampAt(root, propRoot, name, new Vector3(x, 0f, z), 0f, metal, glass);
            }
            for (int i = 0; i < spots.Count; i++)
            {
                Transform l = Slot(root, "LAMP_" + (i + 1).ToString("00"), spots[i].deg, spots[i].r);
                LampBody(propRoot, l, metal, glass);
            }
        }

        private static void BuildLampAt(Transform root, Transform propRoot, string name, Vector3 pos, float yaw,
                                        Material metal, Material glass)
        {
            Transform l = Place(root, name, new Vector2(pos.x, pos.z), yaw);
            LampBody(propRoot, l, metal, glass);
        }

        private static void LampBody(Transform propRoot, Transform l, Material metal, Material glass)
        {
            TownKit.CylL(l, "LAMP_Post", Vector3.zero, 0.10f, 3.80f, metal);
            TownKit.CylL(l, "LAMP_Base", Vector3.zero, 0.26f, 0.22f, metal);
            TownKit.BoxL(l, "LAMP_Arm", new Vector3(0f, 3.86f, 0.34f), new Vector3(0.12f, 0.12f, 0.90f), metal);
            Vector3 headLocal = new Vector3(0f, 3.92f, 0.72f);
            GameObject head = TownKit.EnvFit(propRoot, "lantern", l.TransformPoint(headLocal),
                                             l.eulerAngles.y, 0.55f, 0.30f);
            if (head == null)
                TownKit.BoxL(l, "LAMP_Head", headLocal + new Vector3(0f, 0.10f, 0f),
                             new Vector3(0.34f, 0.44f, 0.34f), glass);
            TownKit.Glow(propRoot, "LAMP_Light", l.TransformPoint(headLocal + new Vector3(0f, -0.02f, 0f)),
                         TownColors.LampGlass, 9f, 1.25f);
        }

        /// <summary>环岛上路灯的盏数（8 盏 = 45° 一盏）。</summary>
        public const int LampCount = 8;

        // ══════════════════════════════════════════════════════════════════
        // 九、小工具
        // ══════════════════════════════════════════════════════════════════

        /// <summary>在楼的局部坐标里摆一个环境装饰（自动换算世界坐标与朝向）。</summary>
        private static void Prop(Transform propRoot, Transform slot, string envId, Vector3 localPos,
                                 float extraYaw, float height, float radius)
        {
            Vector3 wp = slot.TransformPoint(localPos);
            wp.y = TownAvatar.GroundY;
            TownKit.EnvFit(propRoot, envId, wp, slot.eulerAngles.y + extraYaw, height, radius);
        }

        // ══════════════════════════════════════════════════════════════════
        // 八之二、装饰选点：**撞农田就换点**（不再一件一条黄色警告）
        //
        // 病根：外环树（r 18.4~20.3 的西弧）、内绿带的花草（r 4.9~6.1）与 6×6 农田块的
        // 矩形范围是**天然重叠**的（农田块 x −18.95~−4.65 / z −7.15~7.15），
        // 原实现撞上就 `Log.Warn` 跳过 ⇒ 每次 `BuildScene` 刷 6 条黄字，但这些装饰
        // 本来各有"差不多的邻居落点"。⇒ 现在的策略：
        //   ① 第 1 枪仍打原落点（绝大多数装饰压根不撞田，位置一个像素都不动）；
        //   ② 撞了就**换点重来**，最多 `DecorPlaceAttempts` 枪；推进量由 `envId` 的哈希与
        //      序号定死（**不用 Random**）⇒ 同一份代码每次进主城摆出来的东西完全一样；
        //      极坐标装饰沿**圆弧**左右摆、半径不动（圈层是设计好的，动半径会串到别的圈）；
        //   ③ 12 枪全压在田上才真的少摆一个，且**不逐条报**，只计数，由
        //      `TownBootstrap.BuildScene` 末尾调 `FlushDecorSkipReport` 打**一条**汇总。
        // ══════════════════════════════════════════════════════════════════

        /// <summary>装饰选点撞到耕地时的最大尝试次数（第 1 次 = 原落点，其后 11 次换点）。</summary>
        private const int DecorPlaceAttempts = 12;

        /// <summary>压田判定的外扩余量（米）≈ 一般散件的半宽 ⇒ 贴着格边的也算占位。</summary>
        private const float PlotMargin = 0.45f;

        /// <summary>极坐标装饰换点时每"步"沿圆弧摆开的角度（度）。为什么是 9° 见 <see cref="PolarCandidate"/>。</summary>
        private const float PolarRetryStepDeg = 9f;

        /// <summary>
        /// 换点"换到哪一枪算数"的门槛：落点离**整个田块矩形**至少这么远（米）才收工。
        /// 它**不是判据**（判据永远是 `TownLayout.IsOnPlot`），只用来在"已经不压田"的几枪里
        /// 挑最像空地的一枪，免得装饰贴着田埂根 / 篱笆根站。1.00 米 ≈ 一丛草、一块石头的尺度。
        /// </summary>
        private const float PlotClearanceWanted = 1.00f;

        /// <summary>本轮建城"换点也换不出来、只好少摆"的装饰数（只在汇总时用，不逐条报）。</summary>
        private static int _decorSkipped;

        /// <summary>
        /// 把本轮建城累计的"装饰因耕地换点失败而少摆"打**一条**汇总；一个都没有 ⇒ **什么都不打**。
        /// 由 `TownBootstrap.BuildScene` 在全部装饰摆完之后调一次（每次都清零，不留跨次残留）。
        /// </summary>
        public static void FlushDecorSkipReport()
        {
            if (_decorSkipped > 0)
                Log.Info($"[Town] 装饰跳过 {_decorSkipped} 个（落在耕地上，已重试 {DecorPlaceAttempts} 次）");
            _decorSkipped = 0;
        }

        /// <summary>
        /// 极坐标候选落点（**每枪的判定 = 只问一句 `TownLayout.IsOnPlot(x,z,0.45)`**）。
        ///
        /// 弧段预排除的**实际步长阶梯**（`PolarRetryStepDeg = 9°`，`EnvId` 的哈希只决定**先往左还是先往右**）：
        ///     枪 0：原方位（0°）
        ///     枪 1：−9° 或 +9°          枪 2：反方向 9°
        ///     枪 3：同向 18°            枪 4：反向 18°
        ///     枪 5：同向 27°            枪 6：反向 27°   … 枪 2k−1/2k：±9k°（k = 1…6）
        /// ⇒ 12 枪的实际搜索窗口是 ±9°、±18°、±27°、±36°、±45°、±54°，**左右交替逼近**（先占近处）。
        ///
        /// 为什么是 9°：西弧那两棵树（168° / 188°）在 r≈18.4~19.0 上，
        /// 农田矩形占掉的角段约 157°~203°（宽 ≈ 46°）⇒ 步长得让"半圈 23°"在几枪内跨出去。
        /// 实测（按 `TownLayout.PlotCenter` 逐格复算本工程全部装饰落点，压田的 6 件是
        /// flower_b@218°/6.10、bush@165°/5.95、tree@158°/5.75、tree@196°/4.85、tree@168°/18.40、tree@188°/19.02）：
        /// 9° 时它们分别在第 2 / 6 / 4 / 3 / 4 / 5 枪拿到 1.0 米余量，**最费枪 6 枪**，12 枪留了一倍余量。
        /// 步长 < 6° 时最费枪的那件要 10 枪以上（余量被吃掉）；> 12° 时近处能用的位置被跳过、装饰位移变大 ⇒ 取 9°。
        /// 半径不动：圈层（r 4.85~6.10 内绿带 / r 18.40~20.26 外环）是设计数据。
        /// </summary>
        private static Vector2 PolarCandidate(string envId, float deg, float radius, int attempt)
        {
            if (attempt == 0) return TownLayout.Polar(deg, radius);
            float dir = ((Hash(envId) >> 3) & 1) == 0 ? -1f : 1f;    // 先往哪边摆：由哈希定死（可复现）
            int mag = (attempt + 1) >> 1;                            // 1,1,2,2,3,3…（±9°、±18°、±27°…）
            float sign = (attempt & 1) == 1 ? dir : -dir;            // 左右交替
            return TownLayout.Polar(deg + sign * PolarRetryStepDeg * mag, radius);
        }

        /// <summary>
        /// 世界坐标候选落点：第 <paramref name="attempt"/> = 0 时就是原坐标；
        /// 之后按**黄金角**（137.5°）错开、逐级外扩 ⇒ 连续几枪不会落在同一条射线上，且完全确定性。
        /// </summary>
        private static Vector2 PointCandidate(string envId, Vector2 at, int attempt)
        {
            if (attempt == 0) return at;
            int h = Hash(envId);
            float ang = (h & 0x3FF) * (360f / 1024f) + attempt * 137.508f;
            float rad = 0.90f + 0.55f * attempt;
            float r = ang * Mathf.Deg2Rad;
            return at + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * rad;
        }

        /// <summary>字符串 → 非负整形哈希（FNV-1a）。换点要**可复现**：同一件装饰每次进主城落在同一处。</summary>
        private static int Hash(string s)
        {
            unchecked
            {
                int h = (int)2166136261u;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
                return h & 0x7FFFFFFF;
            }
        }

        /// <summary>
        /// 落点到"整个 6×6 田块矩形"的距离（米；落在矩形里 = 0）。
        /// 矩形**不手填**：由 `TownLayout.PlotCenter` 的西南 / 东北两个角格 + 格子尺寸推出来
        /// ⇒ 田格数量、间距、中心任何一处改动都自动跟上。只用于挑点（见 <see cref="SettlePolar"/>）。
        /// </summary>
        private static float PlotClearance(Vector2 p)
        {
            Vector2 sw = TownLayout.PlotCenter(0);
            Vector2 ne = TownLayout.PlotCenter(TownLayout.PlotCols * TownLayout.PlotRows - 1);
            float dx = Mathf.Max(sw.x - TownLayout.PlotWidth * 0.5f - p.x,
                                 p.x - (ne.x + TownLayout.PlotWidth * 0.5f));
            float dz = Mathf.Max(sw.y - TownLayout.PlotDepth * 0.5f - p.y,
                                 p.y - (ne.y + TownLayout.PlotDepth * 0.5f));
            if (dx < 0f) dx = 0f;
            if (dz < 0f) dz = 0f;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 极坐标装饰的**落点结算**：
        ///   ① 原落点不压田 ⇒ **一个像素都不动**（绝大多数装饰走这条，布局与改动前完全一致）；
        ///   ② 压田了才换点：沿 <see cref="PolarCandidate"/> 的角度阶梯逐枪试，压田的枪淘汰，
        ///      第一个"离田 ≥ <see cref="PlotClearanceWanted"/> 米"的枪收工，都不够就退回第一枪不压田的；
        ///   ③ 12 枪全压在田上 ⇒ 返回 false（调用方计一次跳过，汇总时只报一条）。
        /// </summary>
        private static bool SettlePolar(string envId, float deg, float radius, out Vector2 spot)
        {
            spot = PolarCandidate(envId, deg, radius, 0);
            if (!TownLayout.IsOnPlot(spot.x, spot.y, PlotMargin)) return true;

            bool any = false;
            for (int attempt = 1; attempt < DecorPlaceAttempts; attempt++)
            {
                Vector2 p = PolarCandidate(envId, deg, radius, attempt);
                if (TownLayout.IsOnPlot(p.x, p.y, PlotMargin)) continue;   // 判据不变：压田就淘汰这一枪
                if (PlotClearance(p) >= PlotClearanceWanted) { spot = p; return true; }
                if (!any) { spot = p; any = true; }
            }
            return any;
        }

        /// <summary>世界坐标装饰的落点结算（规则同 <see cref="SettlePolar"/>，候选点见 <see cref="PointCandidate"/>）。</summary>
        private static bool SettlePoint(string envId, Vector2 at, out Vector2 spot)
        {
            spot = at;
            if (!TownLayout.IsOnPlot(at.x, at.y, PlotMargin)) return true;

            bool any = false;
            for (int attempt = 1; attempt < DecorPlaceAttempts; attempt++)
            {
                Vector2 p = PointCandidate(envId, at, attempt);
                if (TownLayout.IsOnPlot(p.x, p.y, PlotMargin)) continue;
                if (PlotClearance(p) >= PlotClearanceWanted) { spot = p; return true; }
                if (!any) { spot = p; any = true; }
            }
            return any;
        }

        /// <summary>
        /// 按"半径 + 方位角"摆一个环境装饰。
        /// ★ 落在 36 块耕地上就**换点**（见 <see cref="PolarCandidate"/> / <see cref="SettlePolar"/>），换不出来才不摆 ★
        /// 田格是"玩家能种东西的地方"，别的东西站上去就会被读成"田里冒出我没种过的植物" ✗
        /// </summary>
        private static void PropPolar(Transform propRoot, string envId, float deg, float radius,
                                      float yaw, float height, float targetRadius)
        {
            if (!SettlePolar(envId, deg, radius, out Vector2 at)) { _decorSkipped++; return; }
            GameObject go = TownKit.EnvFit(propRoot, envId, new Vector3(at.x, TownAvatar.GroundY, at.y),
                                           yaw, height, targetRadius);
            if (go != null && (envId == "tree" || envId == "bush")) TownLife.RegisterSway(go.transform);
        }

        /// <summary>
        /// ★ v220：**田地的装饰**（用户："田地增加装饰（篱笆、小草、等）"）★
        ///   ① **四边篱笆**：立在土块外沿（±`FarmHalf`+0.15），**在田格之外** ⇒ 一格也不压 ✓
        ///   ② **小草 10 丛 + 石头 4 块**：全部摆在土块**外侧**的草地上，`PropAt` 里还有换点兜底 ✓
        ///   ③ 全部走 `TownKit.Prim` 共享网格 + `ToonLook` 材质池 ⇒ 没有 per-instance 材质/网格 ✓
        /// </summary>
        public static void BuildFarmDressing(Transform root, Transform propRoot)
        {
            Material rail = TownKit.Mat(TownColors.WallWoodDark);
            Material post = TownKit.Mat(TownColors.WallWood);
            Vector2 c = TownLayout.FarmCenter;
            float h = TownLayout.FarmHalf + 0.15f;          // 贴土块外沿，不碰田格（田格最外沿 = FarmHalf−0.4）

            FenceLine(root, propRoot, new Vector3(c.x - h, 0f, c.y - h), new Vector3(c.x + h, 0f, c.y - h), rail, post, "FARM_FENCE_S");
            FenceLine(root, propRoot, new Vector3(c.x - h, 0f, c.y + h), new Vector3(c.x + h, 0f, c.y + h), rail, post, "FARM_FENCE_N");
            FenceLine(root, propRoot, new Vector3(c.x - h, 0f, c.y - h), new Vector3(c.x - h, 0f, c.y + h), rail, post, "FARM_FENCE_W");
            FenceLine(root, propRoot, new Vector3(c.x + h, 0f, c.y - h), new Vector3(c.x + h, 0f, c.y + h), rail, post, "FARM_FENCE_E");

            // 外侧草地上的小草与石头（位置刻意不均匀；都在土块之外）
            float[] off = { -1.6f, -0.9f, -0.2f, 0.5f, 1.2f, 1.9f };
            for (int i = 0; i < 6; i++)
            {
                PropAt(propRoot, "grass", new Vector2(c.x - h - 0.75f, c.y + off[i] * 2.0f), i * 31f, 0.36f, 0.40f);
                PropAt(propRoot, "grass", new Vector2(c.x + h + 0.75f + (i % 2) * 0.4f, c.y + off[i] * 2.1f),
                       i * 47f, 0.34f, 0.38f);
            }
            for (int i = 0; i < 4; i++)
                PropAt(propRoot, "rock_a", new Vector2(c.x - h - 1.1f + i * 0.5f, c.y - 5.4f + i * 3.6f),
                       30f + i * 40f, 0.58f, 0.55f);
            PropAt(propRoot, "flower_a", new Vector2(c.x - h - 0.9f, c.y + 5.6f), 12f, 0.40f, 0.33f);
            PropAt(propRoot, "flower_b", new Vector2(c.x + h + 0.9f, c.y - 5.6f), 48f, 0.40f, 0.33f);
        }

        /// <summary>
        /// 按**世界 (x,z)** 摆一个环境装饰（西侧三区用；它们不是绕圈的）。
        /// 撞耕地同样**换点**（见 <see cref="PointCandidate"/> / <see cref="SettlePoint"/>），换不出来才不摆。
        /// </summary>
        private static void PropAt(Transform propRoot, string envId, Vector2 at, float yaw,
                                   float height, float targetRadius)
        {
            if (!SettlePoint(envId, at, out Vector2 p)) { _decorSkipped++; return; }
            GameObject go = TownKit.EnvFit(propRoot, envId, new Vector3(p.x, TownAvatar.GroundY, p.y),
                                           yaw, height, targetRadius);
            if (go != null && (envId == "tree" || envId == "bush")) TownLife.RegisterSway(go.transform);
        }

        /// <summary>按世界 (x,z) 摆一棵树。</summary>
        private static void TreeAt(Transform propRoot, string envId, Vector2 at, float yaw, float height, float radius)
            => PropAt(propRoot, envId, at, yaw, height, radius);

        /// <summary>按世界 (x,z) 摆一张长椅（<paramref name="yaw"/> = 座位正面朝哪）。</summary>
        private static void BenchAt(Transform propRoot, Vector2 at, float yaw)
        {
            Material wood = TownKit.Mat(TownColors.Bench);
            Material metal = TownKit.Mat(TownColors.MetalDark);
            Transform s = Place(propRoot, "PARK_Bench", at, yaw);
            TownKit.BoxL(s, "BENCH_Seat", new Vector3(0f, 0.46f, 0f), new Vector3(1.70f, 0.12f, 0.55f), wood);
            TownKit.BoxL(s, "BENCH_Back", new Vector3(0f, 0.74f, -0.24f), new Vector3(1.70f, 0.52f, 0.10f), wood);
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(s, "BENCH_Leg", new Vector3(i * 0.68f, 0.23f, 0f), new Vector3(0.10f, 0.46f, 0.50f), metal);
        }

        /// <summary>一棵树（带一点随机感的固定参数：高度、半径、朝向各不相同）。</summary>
        private static void Tree(Transform propRoot, float deg, float radius, float height, float targetRadius, float yaw)
        {
            PropPolar(propRoot, "tree", deg, radius, yaw, height, targetRadius);
        }

        /// <summary>一张长椅（程序化：座面 + 靠背 + 两条腿），朝圆心。</summary>
        private static void Bench(Transform propRoot, float deg, float radius)
        {
            Material wood = TownKit.Mat(TownColors.Bench);
            Material metal = TownKit.Mat(TownColors.MetalDark);
            Transform s = Slot(propRoot, "PARK_Bench", deg, radius);
            TownKit.BoxL(s, "BENCH_Seat", new Vector3(0f, 0.46f, 0f), new Vector3(1.70f, 0.12f, 0.55f), wood);
            TownKit.BoxL(s, "BENCH_Back", new Vector3(0f, 0.74f, -0.24f), new Vector3(1.70f, 0.52f, 0.10f), wood);
            for (int i = -1; i <= 1; i += 2)
                TownKit.BoxL(s, "BENCH_Leg", new Vector3(i * 0.68f, 0.23f, 0f), new Vector3(0.10f, 0.46f, 0.50f), metal);
        }
    }
}
