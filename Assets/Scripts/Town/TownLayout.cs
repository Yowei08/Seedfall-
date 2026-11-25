using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城布局：**圆形轮廓 + 西侧三区（农田 / 商业街 / 公园）＋ 放射状主街** ★★
    /// 所有半径 / 方位 / 中心点**唯一数值来源**。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、v215 的改动（用户原话）
    /// ══════════════════════════════════════════════════════════════════════
    /// 「**主城扩大半径**，在主城**左侧容纳 6×6 农田区域**，容纳**商业街**，容纳**公园**」
    /// ⇒ 上一版（v214）整城半径 **13.6**，西侧是"2 圈 ×18 的花瓣形田圃"，装不下三个独立功能区。
    ///   这一版：
    ///     · 整城半径 **13.6 → 22.0**（+62%）✓
    ///     · **西侧（−X 半圆）= 三个区**：6×6 **方格**农田（用户点名要方格）＋ 商业街（一条 10 米宽的步行街，
    ///       街上有两栋店）＋ 公园（一块独立草坪区，有池塘/树/长椅/花圃）✓
    ///     · 东侧与南北：大传送门（正北）、种子仓库（正东）、南门（正南）—— 四栋都在放射主街的端头 ✓
    ///     · 圆的部分留着：**圆形城界（灌木环 r=22）＋ 中央广场（公园中央，喷泉）＋ 内环路（r 4.2~5.6）**，
    ///       外加**四条放射主街**（西=商业街 / 北=去传送门 / 东=去仓库 / 南=去南门）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、为什么"方格农田 + 圆形城"不冲突
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户上一轮要"圆形结构 / 整体是一个大公园"，这一轮又点名"**6×6 农田区域**"。
    /// 两者不矛盾的分法：**城的轮廓与主路是圆的，区块内部是方的** —— 现实里的城也是这么长的
    /// （环城路 + 街区）。⇒ 田圃回到 **6×6 方格**（间距 1.80 米、地块 1.60×1.55 米，整体 10.6×10.6），
    ///   它安安稳稳坐在西侧、被商业街与公园夹住，和圆形城界一点不打架 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、坐标
    /// ══════════════════════════════════════════════════════════════════════
    ///   世界 +X = 屏幕右、+Z = 屏幕上（与战斗场景同向）。
    ///   `Polar(deg, r)` 的 `deg` 从 +X 起、朝 +Z 为正：0°=东 / 90°=北 / 180°=西 / 270°=南。
    ///   西侧三区的中心点与建筑位置一律写成**世界 (x, z)**（它们是矩形区，用极坐标反而绕）✓
    /// </summary>
    public static class TownLayout
    {
        // ══════════════════════════════════════════════════════════════════
        // 一、城的轮廓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>中央广场（铺石圆盘）半径。</summary>
        public const float PlazaRadius = 3.00f;

        /// <summary>内环路内沿 / 外沿（围绕广场的一圈环路）。v217：收窄成 1 米宽的小环岛。</summary>
        public const float InnerRoadIn = 3.40f;
        public const float InnerRoadOut = 4.40f;

        /// <summary>绿带外沿（内环路以外的一圈草地，树/灌木/路灯都在这圈上）。</summary>
        public const float GreenBeltOuter = 6.40f;

        /// <summary>主街半宽：南北主街 2.4｜右侧商业街 2.8。v217：西侧那条 10.8 米宽的街**取消**。</summary>
        public const float StreetHalf = 2.40f;
        public const float ShopStreetHalf = 2.80f;

        /// <summary>南北主街的南北端。</summary>
        public const float StreetEndNorth = 6.40f;
        public const float StreetEndSouth = -18.60f;

        /// <summary>右侧商业街（南北向）：x ∈ [6.2, 11.8]，z 从 −15.5 到 +16.5。互动建筑全在这条街两边。</summary>
        public const float ShopStreetXMin = 6.20f;
        public const float ShopStreetXMax = 11.80f;
        public const float ShopStreetZMin = -15.50f;
        public const float ShopStreetZMax = 16.50f;

        /// <summary>全城轮廓半径（灌木环在这里）。</summary>
        public const float TownRadius = 22.00f;

        /// <summary>玩家可达半径（比灌木环内收 0.6 米）。</summary>
        public const float WalkRadius = 21.40f;

        // ══════════════════════════════════════════════════════════════════
        // 二、西侧：**放大的农田** ＋ 紧贴田圃的仓库 ＋ 公园（v217）
        //   用户原话：「放大耕地的区域面积凸显内容」「种子仓库移到左侧区域接着耕地」
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 6×6 农田区的中心。
        /// ★ v217：**放大**（用户："放大耕地的区域面积凸显内容"）★
        /// 地块 1.30×1.28 → **2.00×2.00**；间距 1.45 → **2.30**；外廓 8.55 → **13.5 米**（面积 ≈ 2.5 倍）；
        /// 中心 (−9.6,−10.0) → **(−11.8, 0.0)**（正西，整块吃掉左半边）。
        /// 外角 r = √(18.95² + 7.15²) = **20.25** ⇒ 距城界 22 还有 **1.75 米** ✓
        /// </summary>
        public static readonly Vector2 FarmCenter = new Vector2(-11.80f, 0f);

        /// <summary>农田土块半边长（13.5 的地块 + 0.8 的土边 = 14.3 ⇒ 半 7.15）。</summary>
        public const float FarmHalf = 7.15f;

        /// <summary>农田是 **6×6 方格**。</summary>
        public const int PlotCols = 6;
        public const int PlotRows = 6;

        /// <summary>地块中心间距（米）。v217：1.45 → 2.30。</summary>
        public const float PlotPitch = 2.30f;

        /// <summary>地块横向宽（米）。v217：1.30 → 2.00。</summary>
        public const float PlotWidth = 2.00f;

        /// <summary>地块纵向深（米）。v217：1.28 → 2.00。</summary>
        public const float PlotDepth = 2.00f;

        /// <summary>公园区中心（广场西北、农田北侧）。</summary>
        public static readonly Vector2 ParkCenter = new Vector2(-6.50f, 11.50f);

        /// <summary>公园草坪半边长（8×8 的一块独立草坪）。</summary>
        public const float ParkHalf = 4.00f;

        /// <summary>西侧那条 10.8 米宽的街已经取消（改成农田 + 草地）—— 这两个常量只留作色板口径。</summary>
        public const float MarketStreetZMin = -5.40f;
        public const float MarketStreetZMax = 5.40f;

        // ══════════════════════════════════════════════════════════════════
        // 三、六座建筑的位置与朝向（★ 每栋都在"自己的"位置与朝向上 ★）
        // ══════════════════════════════════════════════════════════════════
        //
        // `Yaw` = 让这栋楼的**正面（本地 +Z）朝向哪**：180° = 朝 −Z（朝南，面对商业街/广场），
        // 270° = 朝 −X（朝西，面对东向主街）。楼的正面 = 门那一面 ✓

        /// <summary>大传送门：**右侧商业街的北端**，正面朝南（沿街走上来正对它）。</summary>
        public static readonly Vector2 PortalPos = new Vector2(6.00f, 16.00f);
        public const float PortalYaw = 180f;

        /// <summary>杂货商店（圆顶）：商业街**东侧**，正面朝西（门对着街）。</summary>
        public static readonly Vector2 ShopPos = new Vector2(13.80f, 5.50f);
        public const float ShopYaw = 270f;

        /// <summary>服装店（尖顶塔楼）：商业街**东侧**、商店以北一段，正面朝西。</summary>
        public static readonly Vector2 ClothPos = new Vector2(13.80f, 11.00f);
        public const float ClothYaw = 270f;

        /// <summary>种子仓库（平顶仓房）：**紧贴农田南侧**，正面朝北（出门就是田）。</summary>
        public static readonly Vector2 WarePos = new Vector2(-11.80f, -10.60f);
        public const float WareYaw = 0f;

        /// <summary>集市帐篷：商业街**西侧**，正面朝东（对着街）。</summary>
        public static readonly Vector2 TentPos = new Vector2(5.60f, -6.50f);
        public const float TentYaw = 90f;

        /// <summary>家（功能性建筑，`E` 开图鉴）：商业街**东侧**、商店以南，正面朝西。</summary>
        public static readonly Vector2 HomePos = new Vector2(13.80f, -0.50f);
        public const float HomeYaw = 270f;

        /// <summary>南门：南向主街尽头（玩家从这里进城）。</summary>
        public static readonly Vector2 GatePos = new Vector2(0f, -21.00f);
        public const float GateYaw = 0f;

        // ══════════════════════════════════════════════════════════════════
        // 三之二、装饰性房屋 ×3 ＋「未开发」地块（v216 加、v217 跟着新布局挪）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>三栋**装饰性**小房（不可交互）：东北两栋 + 正北一栋，把外圈填满。</summary>
        public static readonly Vector2[] DecorHouses =
        {
            new Vector2(17.00f, 6.00f),     // r = 18.0
            new Vector2(16.00f, -9.00f),    // r = 18.4
            new Vector2(-1.00f, 17.50f),    // r = 17.5
        };

        /// <summary>右下角围起来的**未开发地块**（x/z 世界矩形）。</summary>
        public static readonly Rect UnbuiltLot = new Rect(12.00f, -12.00f, 4.00f, 4.50f);   // x 12~16, z −12~−7.5

        /// <summary>「未开发」告示牌的位置（立在围栏北侧、朝城内）。</summary>
        public static readonly Vector2 UnbuiltSignPos = new Vector2(14.00f, -7.00f);

        // ══════════════════════════════════════════════════════════════════
        // 四、锚点（`E` 交互点：站在主街上、各建筑门前，离建筑正面 ≥ 0.4 米）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v257：**互动判定点 = 大门本身**（用户原话：「实际的互动判定位置修改与大传送门重合」）。
        /// 改前 (6, 12.60) 在门**前 3.4 米**的空地上 —— 玩家站在空地上就弹难度面板，
        /// 与"门"没有任何关系 ✗ 现在挪到台阶上、门洞正下方 (6, **14.40**)：
        /// 必须走到门口才触发，读起来就是"进门 → 选难度 → 开打" ✓
        /// </summary>
        public static readonly Vector2 PortalAnchor = new Vector2(6.00f, 14.40f);
        public static readonly Vector2 ShopAnchor = new Vector2(11.20f, 5.50f);
        public static readonly Vector2 ClothingAnchor = new Vector2(11.20f, 11.00f);
        public static readonly Vector2 WarehouseAnchor = new Vector2(-11.80f, -7.70f);

        /// <summary>家的 E 锚点（站在商业街上、家门口）。</summary>
        public static readonly Vector2 HomeAnchor = new Vector2(11.20f, -0.50f);

        /// <summary>玩家出生点：南门里侧的南向主街上，面朝北（正对广场与传送门）。</summary>
        public static readonly Vector3 SpawnPos = new Vector3(0f, TownAvatar.GroundY, -18.60f);

        /// <summary>
        /// ★ 战斗传送门的**特效挂点**（黑洞/粒子由美术那路做，按这个名字找）★
        /// 位置 = 传送门正前方、玩家走得到的地方；朝向 = 背面朝门、正面朝玩家来路（本地 +Z 朝南）。
        /// **名字不要改**：美术那路按字符串找它 ✓
        /// </summary>
        public const string BossPortalFxName = "FX_BossPortal";

        /// <summary>特效挂点相对传送门局部的偏移（+Z = 门正面朝外 3.4 米、抬到 1.6 米高）。</summary>
        /// <summary>
        /// 特效挂点相对传送门局部的偏移。
        ///
        /// ★ v252：**+Z 从 3.40 米收到 0.45 米** ★
        ///   用户原话：「对主城中的传送门特效修改…让特效与门结合」。
        ///   3.40 m 的那个挂点来自"特效要浮在门前面"的旧做法 —— 实机截图
        ///   （`Docs/captures/v252-portal-zh`）里读到的是**一片悬在门前地上的黑盘**，
        ///   和石门是两件东西；而门洞（`PORTAL_Field`，局部 z=0.05、尺寸 4.95×6.90）
        ///   才是玩家认知里的"门"。0.45 m 让它落在**门洞内侧、门框前面**
        ///   （门框线脚在 0.20、光带在 0.16）⇒ 黑盘正好嵌进门洞 ✓
        ///   抬到 1.60 m 的那一维**没动**：`TownPortalFx` 建盘时用的是自己的 `CenterY = 3.85`
        ///   （= 门洞中心），挂点的 y 只影响世界坐标的参考，不影响盘的竖直位置 ✓
        /// </summary>
        public static readonly Vector3 BossPortalFxLocal = new Vector3(0f, 1.60f, 0.45f);

        /// <summary>小地图取景（世界 x/z）：整城半径 22 ⇒ ±23，四周各留 1 米。</summary>
        public static readonly Rect MapWorldRect = new Rect(-23f, -23f, 46f, 46f);

        // ══════════════════════════════════════════════════════════════════
        // 五、极坐标 / 朝向工具
        // ══════════════════════════════════════════════════════════════════

        /// <summary>方位角（度）+ 半径 → 世界 (x, z)。</summary>
        public static Vector2 Polar(float deg, float radius)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
        }

        /// <summary>同上，但返回带高度的世界坐标。</summary>
        public static Vector3 Polar3(float deg, float radius, float y = 0f)
        {
            Vector2 p = Polar(deg, radius);
            return new Vector3(p.x, y, p.y);
        }

        /// <summary>世界 (x, z) 的方位角（度，0~360）。</summary>
        public static float DegOf(Vector2 world)
        {
            float d = Mathf.Atan2(world.y, world.x) * Mathf.Rad2Deg;
            if (d < 0f) d += 360f;
            return d;
        }

        /// <summary>站在方位 <paramref name="deg"/> 上、要**面朝圆心**时的 Y 轴欧拉角。</summary>
        public static float FaceCenterYaw(float deg)
        {
            Vector2 d = -Polar(deg, 1f);
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        // ══════════════════════════════════════════════════════════════════
        // 六、36 块耕地（★ 6×6 方格，与 `TownState.PlotSeed[36]` 一一对应 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>第 <paramref name="index"/> 块地的列（0~5）。</summary>
        public static int PlotCol(int index) => index % PlotCols;

        /// <summary>第 <paramref name="index"/> 块地的行（0~5，行 0 在南）。</summary>
        public static int PlotRow(int index) => index / PlotCols;

        /// <summary>第 <paramref name="index"/> 块地的世界中心 (x/z)。**布局的唯一实现**。</summary>
        public static Vector2 PlotCenter(int index)
        {
            int col = PlotCol(index);
            int row = PlotRow(index);
            return new Vector2(
                FarmCenter.x + (col - (PlotCols - 1) * 0.5f) * PlotPitch,
                FarmCenter.y + (row - (PlotRows - 1) * 0.5f) * PlotPitch);
        }

        /// <summary>方格田不需要旋转（上一版的"切向贴圆周"随花瓣布局一起作废）。</summary>
        public static float PlotYaw(int index) => 0f;

        /// <summary>
        /// ★★ 这个世界点 (x, z) 是不是**落在某一块耕地上**（36 格之一，按格子的世界矩形判）★★
        ///
        /// 为什么要有它：田格是"玩家能种东西的地方"，**任何别的东西站上去都是错的** ——
        /// 用户报过「**耕地上的植物与实际拥有的种子不匹配**」：田里长出一株你根本没种过、
        /// 仓库里也没有对应种子的东西（实测抓到稻草人/灌木/树），读起来就是"田里的植物对不上账" ✗
        ///
        /// ⇒ 判定收在这**一个函数**里：主城自己摆散件（`TownBuildings`）与外部美术层
        /// （`TownArtDresser` 的禁区表）都以它为准，不再各写一份矩形比较。
        /// `margin` 是外扩的余量（米）—— 落地物有半径，贴着格边也算占位。
        /// </summary>
        public static bool IsOnPlot(float x, float z, float margin = 0f)
        {
            float halfW = PlotWidth * 0.5f + margin;
            float halfD = PlotDepth * 0.5f + margin;
            for (int i = 0; i < 36; i++)
            {
                Vector2 c = PlotCenter(i);
                if (Mathf.Abs(x - c.x) <= halfW && Mathf.Abs(z - c.y) <= halfD) return true;
            }
            return false;
        }

        /// <summary>整个农田方块的半边长（含田埂）。散景/散件要整体避开田时用它一次判完。</summary>
        public static float FarmHalfEdge => FarmHalf;
    }
}
