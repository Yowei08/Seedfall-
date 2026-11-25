using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Seedfall
{
    /// <summary>
    /// ★★ 训练场**实体仓库**（v305 重做）—— 独立建模的木石建筑 + 靠近时的悬浮互动提示 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// ★★ v307：位置 = **贴最左侧墙壁**（用户原话："再次移动仓库位置，贴着最左侧墙壁"）★★
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① "最左侧墙壁"是**实体墙，不是逻辑边界** —— 证据链：
    ///      · `WorldView.BuildArena`（`WorldView.cs:733`）→ `BuildRuinWall(half)`（`WorldView.cs:742`）
    ///        沿场地四边各铺一圈 `wall_ruin*` 烂砖墙段（`WorldView.cs:756-760`，西边 `pos.x = -half`）
    ///      · 墙段生成器 `Tools/AssetPipeline/gen_ruins.py`：`WALL_LEN = 2.00`、`FOOT_W = WALL_TH+0.08 = 0.50`
    ///        （`gen_ruins.py:83-89`），`_set_pivot(center_xy=False)` ⇒ **原点在墙厚中线上**（`gen_ruins.py:558`）
    ///      ⇒ `half = farm.arenaHalf = 20` ⇒ 墙厚中线 x = −20、**朝场内那一面 = −20 + 0.25 = −19.75**
    ///        （落砖在 −Y 侧、被转成朝场外，`WorldView.cs:732`：`-Y 朝场外` ⇒ 不侵入场地）
    ///      · 墙高 1.606（`ruins_manifest.json:7-14` 的 dims），所以 3.20m 的仓库会在墙**上方**露出来 ✓
    ///      · ⚠️ `WorldView.UpdateFence` 那道"围栏"只圈**耕地**（`WorldView.cs:1138-1161`），不是场地边界；
    ///        `arenaHalf` 只是 `World.ClampToArena` 的逻辑钳位（`Actors.cs:872`），没有可见墙 ⇒ 不能当"最左侧墙壁"
    ///
    ///   ② 新中心 **(−17.10, 0)**（旧 (−11.4, 0)），外轮廓半宽：西 **2.569**（西侧货架）/ 东 **2.516**（东屋面）
    ///      ⇒ 西沿 −19.669（离墙面 **0.081m**，肉眼即"贴着"）、东沿 −14.584（离商店车绕场左直边 x=−14.4 **0.184m**）✓
    ///      ⚠️ 这两条是**同一个预算的两头**：墙内面到车道线只有 5.35m，而本栋含屋檐总宽 5.085m
    ///        ⇒ 两侧余量之和恒为 0.265m，任何"贴墙"的位置都不可能再给车道更多余量 ✓
    ///
    ///   ③ 逐项间距（世界坐标；外轮廓 = x ∈ [−19.669, −14.584]、z ∈ [−3.62, +2.09]）：
    ///      | 已有物体                     | 它的范围                     | 最小间距 |
    ///      |------------------------------|------------------------------|----------|
    ///      | 西侧烂砖墙（实体墙）          | 墙面 x = −19.75、高 1.606    | **0.081m**（西沿到墙面）✓ |
    ///      | 场地逻辑边界 arenaHalf = 20   | \|x\| ≤ 20                   | **0.331m**（−19.669 到 −20）✓ |
    ///      | 耕地                         | x,z ∈ [−7.5, +7.5]           | **7.084m**（东沿到 −7.5）✓ |
    ///      | 9 个稻草人                   | x∈{−3,0,3}, z∈{7,10,13}      | **11.584m**（东沿到 x=−3）✓ |
    ///      | 巨人稻草人                   | (0, 16.5)                    | **14.584m**（沿 X）✓ |
    ///      | 玩家出生点                   | (0, −7)                      | **14.97m**（到东南木箱角）✓ |
    ///      | 商店车绕场左直边             | x = −14.4（`ShopTruck.Path` ±0.72×20）| **0.184m**（东沿在线的**西侧**，不压线）✓ |
    ///      | 商店车离场直线               | z = 0、x: 0→−28              | 0（穿场而过，见 ④） |
    ///      | 内圈树带（46 棵 r=17.4~19）   | 半径 ≥ 17.4                  | 落进轮廓的被 `ClearSceneryUnderBuilding` 隐藏 ✓ |
    ///      | 路灯圈（12 盏 r=19.4）        | 半径 19.4                    | (−19.4, 0) 那盏被隐藏（−19.4 > −20.3 在隐藏框内）✓ |
    ///
    ///   ④ 商店车：**绕场线不压**（0.184m 余量，上一轮 x=−11.4 时是车穿过房子）；
    ///      但 `Phase.Leaving` 的那条**离场直线**（`ShopTruck.SamplePoint`，z=0、x 0→−28）仍然穿场而过 ——
    ///      这条线**本来就穿过耕地**（x 0→−7.5 段），是"车开出地图"的设计路径，与本文的位置无关 ✓
    ///
    ///   ⑤ 可见性（交付报告 ③ 的表；相机 `C = (0, 19.0526, −18.0)`，fov62/pitch60/dist22）：
    ///      水平最紧点仍是**西檐外角** (−19.616, 2.558, −2.016)：|x_c|/z_c = **0.8806**
    ///      ⇒ 要求窗口宽高比 ≥ **1.4655**：16:9 (1.778) ✓、16:10 (1.600) ✓、4:3 (1.333) ✗（切掉西檐那个角）
    ///      旧位置 (−11.4,0) 是 0.6930 ⇒ 1.1533（4:3 也能过）—— 这一条是**贴墙的代价**，
    ///      要 4:3 也满幅就只能把 `CenterXZ.y` 往北挪（+5.00 ⇒ 要求 1.317），本轮按用户口径取 z = 0 ✓
    ///      垂直方向全部远在限内（最大 0.3945 < 0.60086）✓
    ///
    ///   ⚠️ 下面 v306 的那张表是**旧位置 (−11.4,0)** 的读数，保留作对照；数别再照抄。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// ★ v306 变更摘要（用户原话："仓库模型缩小调整位置，不要与其它模型重叠，
    ///                        增加碰撞禁止玩家穿过"）★
    /// ══════════════════════════════════════════════════════════════════════
    ///   本节是 v306 的**唯一入口**，四条落点（详细推导都在常量块与对应方法上）：
    ///     ① **缩小**：墙体 6.0×5.0 → **4.2×3.6**、屋脊 4.60 → **3.20**。
    ///        实现方式是根节点非等比缩放 `(0.700, 0.6957, 0.720)`，
    ///        本地网格**一个字都没改**（39 个盒子的绕序/法线沿用上一轮的验证结果）✓
    ///     ② **移动**：`CenterXZ = (−13, 0)` → **(−11.4, 0)**。
    ///        旧位置最硬的冲突是**商店车绕场的左直边 x = −14.4**（0.72×20）正好穿过房子；
    ///        新位置的逐物体间距表见常量块上方那段 ✓
    ///     ③ **不重叠**：静态物体由 ② 解决；**随机撒的地面装饰**（草/花/蘑菇/碎石）
    ///        由 `ClearSceneryUnderBuilding` 在训练场里让路（战斗侧的撒点逻辑不改）✓
    ///     ④ **碰撞**：本工程玩家走的是**逻辑坐标**（`Pos += MoveSpeed*dt`），
    ///        Unity 的 Collider 拦不住它 ⇒ 在 `PushPlayerOut` 里按矩形把 `Pos` 推出去 ✓
    ///        （所以下面 三 里"全部零件都没有 Collider"**依然成立且依然是对的**：
    ///          碰撞不在物理层，在逻辑层）
    ///   ⚠️ 下面 二 的那张视锥表是 **v305 的 (−13, 0) + 6×5/4.60** 的读数，保留作对照；
    ///      v306 位置 (−11.4, 0) 的同一算法结果是 `|x_c|/z_c = 0.6204`（1.04）；
    ///      **v307 现在的 (−17.10, 0) 是 0.8806 ⇒ 1.4655**（贴墙的代价，见本文件顶部 v307 节 ⑤）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、v305 用户原话（本文件最初的验收口径）
    /// ══════════════════════════════════════════════════════════════════════
    ///   「独立建模一个仓库，靠近后悬浮 ui 提示互动按键，类似商店车的；…
    ///     仓库位置移动到地图左侧」
    ///
    ///   拆成三条，各自在本文件里有一个唯一落点：
    ///     ① **独立建模** ⇒ `BuildModel` 用 `Mesh.SetVertices/SetTriangles` 手写顶点，
    ///        不是上一版那样"几个 `CreatePrimitive` 的 Cube 堆起来"（见 三）
    ///     ② **靠近悬浮提示** ⇒ `DrawHint`：世界坐标投影 + 正对镜头 + 按距离缩放 + `sin` 浮动，
    ///        手感照抄商店车那套（`ImGuiHud.DrawShopTruckKey`，`ImGuiHud.cs:3853`，见 四）
    ///     ③ **地图左侧** ⇒ `CenterXZ`（v305 是 (−13,0)，v306 起是 (−11.4,0)），
    ///        可见性算术见 二（**不是估的，是算的**）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、★ 位置与可见性算术（交付报告 ①）★
    /// ══════════════════════════════════════════════════════════════════════
    ///   镜头（`Assets/Resources/Data/tuning.json` 的 `camera` 段，**实际读表值**）：
    ///     `fov = 62`、`pitch = 60`、`distance = 22`、`orthographic = false`、`clampToArena = false`
    ///   机位算式（`GameBootstrap.UpdateCamera:585-590`）：机位 = 焦点 + (0, sin60, −cos60)×22
    ///     ⇒ 焦点 `F` 时相机在 `F + (0, 19.0526, −11.00)`（**南侧**、朝北俯视）✓
    ///   视野半高（`GameBootstrap.VisibleGroundHalfExtents:594-615`）：
    ///     `halfH = 22 × tan(31°) = 22 × 0.60086 = 13.2189`
    ///     `sinPitch = 0.86603`、`tanPitch = 1.73205`
    ///     `below  = halfH × (1/sin − 1/tan) = 13.2189 × 0.57735 = 7.6325`（焦点**以南**可见纵深）
    ///     `halfW  = halfH × aspect`（水平半宽，随窗口比例变化）
    ///
    ///   ★ 只用"焦点以南可见纵深"是不够的 —— 那是一条对**正交/对称**估算的近似。
    ///     本文件改成**直接做视锥判定**（对每一个轮廓顶点算相机空间坐标）：
    ///       `d = P − C`；`x_c = d.x`；`y_c = 0.50000·d.y + 0.86603·d.z`；`z_c = −0.86603·d.y + 0.50000·d.z`
    ///       （相机基：前 `f=(0,−0.86603,0.5)`、右 `r=(1,0,0)`、上 `u=(0,0.5,0.86603)`）
    ///       可见 ⇔ `|y_c|/z_c ≤ tan(31°) = 0.60086` 且 `|x_c|/z_c ≤ 0.60086 × aspect`
    ///
    ///   玩家出生点 `(0, −7)`（`RunSession.cs:2843`）、`clampToArena=false` ⇒ 首帧焦点就是它
    ///   ⇒ 相机 `C = (0, 19.0526, −18.0000)`。逐点读数（交付报告 ① 的表就是这张）：
    ///
    ///   | 轮廓点（世界）            | x_c     | y_c    | z_c     | |x_c|/z_c | |y_c|/z_c |
    ///   |---------------------------|---------|--------|---------|-----------|-----------|
    ///   | 门洞地面 (−13, 0.0, −2.5) | −13.00  | 8.2272 | 26.7500 | 0.4860    | 0.3076    |
    ///   | 屋脊     (−13, 4.6,  0.0) | −13.00  | 8.3622 | 21.5162 | 0.6042    | 0.3886    |
    ///   | 西檐外角 (−16.5,3.6, −2.8)| −16.50  | 9.9272 | 23.7800 | 0.6939    | 0.4174    |
    ///   | 招牌板   (−15.05,2.42,−3.87)| −15.05| 10.6228| 25.3392 | 0.5940    | 0.4193    |
    ///   | 货架顶   (−16.30,2.40, 0.0)| −16.30| 7.2622 | 23.4220 | 0.6960    | 0.3101    |
    ///
    ///   ⇒ 垂直方向**全部**远在 0.60086 之内（最大 0.4193）✓
    ///   ⇒ 水平方向的**最紧一点**是西檐外角 0.6939 ⇒ 需要 `aspect ≥ 0.6939 / 0.60086 = 1.1548`
    ///     （即窗口比 1.155:1 更宽）。16:9 = 1.7778 ✓、16:10 = 1.6000 ✓、4:3 = 1.3333 ✓、5:4 = 1.2500 ✓
    ///     —— 只有**正方形及更窄**的窗口会裁掉最西那条屋檐（而门、招牌、屋脊、提示气泡全都在画面内）
    ///   ⇒ **一进训练场就整栋在画面内**（16:9/4:3 实测口径）✓，且它落在画面**左侧**：
    ///     门洞在地面投影 `x/z = 0.486` 在左半区、招牌 0.594 更靠左 ✓ 正是"地图左侧"的观感
    ///
    ///   与耕地不重叠（这是第二件必须算的事）：
    ///     耕地 6×6 格、格 2.5m ⇒ 占 `x ∈ [−6.25, +6.25]`、`z ∈ [−6.25, +6.25]`（`FarmGrid.WorldOf`）
    ///     本建筑：墙体 `x ∈ [−16.0, −10.0]`、`z ∈ [−2.5, +2.5]`；含屋檐 `x ∈ [−16.5, −9.5]`、`z ∈ [−2.8, +2.8]`
    ///     ⇒ **通道宽** = −9.5 − (−6.25) = **3.25 米**（屋檐到耕地西沿）✓ 不压耕地、不挡种地
    ///     ⇒ 整栋最西 (−16.5, 含货架 −16.4) 仍在竞技场内（`farm.arenaHalf = 20`，`|x| ≤ 20`）✓
    ///
    ///   门朝 **−Z（南）**：相机在南侧朝北看 ⇒ 玩家一进场就**正面看到门廊、台阶、挂牌**，
    ///   而不是看到一面后墙 ✓（上一版把门朝北、正对出生点；本轮位置换到左侧之后，
    ///   朝北的门会被整栋建筑自己挡住 —— 所以门必须朝向来光的这一侧）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、★ 建模：手写顶点（交付报告 ②）★
    /// ══════════════════════════════════════════════════════════════════════
    ///   `AddBox` 把**一个盒子**写进 5 个材质组之一的顶点缓冲（每面 4 顶点 ⇒ 24 顶点 / 36 索引）。
    ///   顶点**不共享** ⇒ 每个面都是硬边（木板的棱角才立得住）✓
    ///   法线：显式写入朝外法线，再调 `Mesh.RecalculateNormals()`（要求里点名的那一步）——
    ///     因为顶点不共享，重算出来的结果与显式法线**逐字节等价**，不存在"被平均成圆角"的风险 ✓
    ///   绕序：每个面按"从外面看**顺时针**"写（Unity 的正面约定：顺时针 = 正面），
    ///     于是叉积 `Cross(c1−c0, c2−c0)` 与显式法线同向 ⇒ 背面剔除不会把外墙剔没 ✓
    ///   ★ 模板 Mesh 缓存 ★ `UnitBox()` 只建一次（24 顶点/36 索引的**单位立方体**），
    ///     之后每次 `AddBox` 都是把模板顶点 **乘尺寸 → 旋转 → 平移** 后追加 ⇒ 一次建模里
    ///     只走一次 `new Mesh`（每材质组一个），不逐个零件建 Mesh ✓
    ///
    ///   零件清单（39 个盒子，交付报告 ② 有逐组顶点/三角形计数）：
    ///     石基座 / 木地板 / 4 根角柱 / 后墙 / 左右侧墙 / 前墙两段 / 门楣 / 门洞内凹 / 门框两根
    ///     / 三级台阶 / 门廊两根柱子 / 门廊横梁 / 门廊小坡顶 / 双坡大屋顶（含出檐 0.5m）
    ///     / 屋脊梁 / 两道檐口枋 / 烟囱 / 招牌（挑臂 + 吊杆 + 框 + 板）
    ///     / 西侧货架（2 立柱 + 2 层板）/ 门前木箱 ×3（含一个叠放）
    ///   尺寸：墙体 6.0(x) × 5.0(z) × 3.6(檐口)，屋脊 4.60，含招牌总高 4.66
    ///        ⇒ 用户的"6 × 4 × 5 米左右"按 **占地 6×5、总高 4.6** 落地 ✓
    ///   配色沿用工程既有取色（木 0.42,0.30,0.17 / 深木 0.30,0.21,0.12 / 石 0.55,0.53,0.50 /
    ///   板 0.52,0.36,0.20 / 洞 0.10,0.09,0.08，与旧版 `TrainingWarehouse.Mat` 同源）
    ///   着色器兜底链照抄工程既有写法：URP/Lit → URP/Unlit → Standard → Sprites/Default。
    ///   ⚠️ **全部零件都没有 Collider**：创建的是 `MeshFilter + MeshRenderer`，**从来没加过**碰撞体
    ///      ⇒ 玩家/敌人的逻辑坐标移动不会卡在墙里，也不会干扰稻草人索敌 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、★ 悬浮提示（交付报告 ③）★
    /// ══════════════════════════════════════════════════════════════════════
    ///   商店车那套（`ImGuiHud.DrawShopTruckKey`）的四个特征，本文件逐条照搬：
    ///     ① **世界坐标投影**：`Camera.WorldToScreenPoint(锚点)`（不是屏幕中央 ——
    ///        "悬浮"的语义是"贴着那个东西"）
    ///     ② **正对镜头**：IMGUI 画的永远是屏幕对齐的 ⇒ 只要锚点跟着相机投影，
    ///        提示就自动正对镜头，不需要任何 LookAt ✓
    ///     ③ **按距离缩放**：`kz = clamp(24 / dist, 0.62, 1.25)`（22 米机距下 ≈ 0.91）
    ///     ④ **轻微浮动**：`y += sin(unscaledTime × 2.4) × 6 × kz`，外加 3.5% 的呼吸缩放
    ///   与商店车的差别只有一处（**必须写明**）：车是"停靠期 + 距离"两条件，
    ///   本建筑没有阶段机，判据是 `NearNow()`（玩家到门洞 XZ 距离 ≤ `InteractRadius = 4m`）——
    ///   而这个判据**同时**是 `TrainingWarehouse.EKeyTakenByWarehouse` 用的那一个
    ///   ⇒ 提示画的时机与"按 E 真的会开"**不可能不一致**（本会话踩过"两份距离阈值各自漂移"的坑）✓
    ///   面板开着时不画（`TrainingVault.IsOpen`），暂停（Esc 菜单）时也不画。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、输入：与战斗同一套（E 开/关，Esc 关）
    /// ══════════════════════════════════════════════════════════════════════
    ///   战斗唯一读法 `GameBootstrap.ReadInput:429` 是 `kb.eKey.wasPressedThisFrame`（新 Input System）。
    ///   本文件读**同一个设备、同一个键**，但取并集 `isPressed 上升沿 ∪ wasPressedThisFrame`
    ///   （写法照抄 `EscKey.Pressed`，`EscKey.cs:57-60`）——它是那一行的**超集**，
    ///   战斗能读到的 E，这里一定也能读到 ✓（`ProjectSettings` 里 `activeInputHandler = 1`
    ///   ⇒ `UnityEngine.Input.*` 在这台机器上不可用，一个都没用）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 六、为什么它对正常战斗**零影响**（结构性 + 一条纯插入守卫）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① 本组件**只由 `TrainingBootstrap.Awake` 创建**（`TrainingBootstrap.cs:193`）
    ///      ⇒ `Training.unity` 之外**根本没有这个对象**，`Update` / `OnGUI` 一行都不跑 ✓
    ///   ② `Update` / `OnGUI` 第一行 `if (!TrainingMode.Active) return;`（双保险）
    ///   ③ 战斗侧唯一读本文件的那一行是 `GameBootstrap.ReadInput:438`（**纯插入，未改任何既有语句**）：
    ///        `if (TrainingWarehouse.EKeyTakenByWarehouse) input.ShopOpen = false;`
    ///      战斗场景里 `TrainingMode.Active == false` ⇒ 属性第一行就返回 false ⇒ 等于 `if (false)` ⇒
    ///      `input.ShopOpen` 保持上一行赋给它的值 ⇒ **行为不变** ✓（逐条论证见交付报告 ⑦）
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class TrainingWarehouse : MonoBehaviour
    {
        private const string Tag = "[仓库建筑] ";

        // ══════════════════════════════════════════════════════════════════
        // 布置常量（交付报告 ①② 报的每一个数都在这一段）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 建筑中心（世界 XZ）。★ v307：(−11.4, 0) → **(−17.10, 0)**：贴着西侧烂砖墙的墙面（x=−19.75）★
        ///
        /// 数值来源（类注释 v307 节有完整推导）：
        ///   西沿（**西侧货架**，比屋檐还外 0.053m）= CenterX − 2.569 = **−19.669** ⇒ 离墙面 **0.081m**；
        ///   东沿（东屋面）= CenterX + 2.516 = **−14.584** ⇒ 离商店车绕场左直边 (−14.4) **0.184m**。
        ///   这两个余量之和恒等于 0.265m（= 墙内面 −19.75 到车道线 −14.4 的 5.35m 减去本栋总宽 5.085m）
        ///   ⇒ **贴墙**与**不压车道线**是本位置唯一能同时满足的解，别再单看一头调参 ✓
        /// ⚠️ 下面所有派生量（`DoorXZ` / `SignWorld` / 模型落点 / 逻辑碰撞矩形 / 隐藏装饰矩形 /
        ///   `DrawHint` 的锚点）**全部**从本字段算出来 ⇒ 改这一行即可，别处不用动 ✓
        /// </summary>
        public static readonly Vector2 CenterXZ = new Vector2(-17.10f, 0f);

        // ══════════════════════════════════════════════════════════════════
        // ★★ v306：尺寸缩小 + 位置重算 + 逻辑碰撞（交付报告 ④）★★
        // ══════════════════════════════════════════════════════════════════
        //
        // ── 用户的三个要求 ──
        //   ① "仓库模型缩小"        ⇒ 墙体 6.0×5.0 → **4.2×3.6**、屋脊 4.60 → **3.20**
        //   ② "调整位置，不要与其它模型重叠"
        //   ③ "增加碰撞禁止玩家穿过"（本工程的玩家移动是**逻辑坐标**，
        //      Unity 的 Collider 拦不住它 ⇒ 必须在逻辑层做，见 `PushPlayerOut`)
        //
        // ── 缩小的实现方式：**根节点等比缩放**，而不是改 39 个盒子的坐标 ──
        //   `BuildModel` 里每个盒子的坐标都是按"本地 6.0×5.0、屋脊 4.60"手写的
        //   （那是上一轮逐条验过绕序与法线的结果）。改成一格一格去乘系数，
        //   等于把 39 个数字重验一遍 —— 收益为零、风险最大 ✗
        //   所以：本地网格**一个字都不改**，只在根节点上挂一个非等比缩放
        //   `(0.700, 0.6957, 0.720)` ⇒ 世界尺寸正好是用户要的 4.2×3.6/3.20 ✓
        //   非等比是因为三个方向的缩放系数不同（0.70 / 0.6957 / 0.72），差值 < 3.5%
        //   ⇒ 肉眼看不出"压扁"，但三个目标尺寸都**精确**落在用户给的数上 ✓
        //
        // ── 位置：(-13, 0) → **(-11.4, 0)** ──
        //   逐项核对"不重叠"（含屋檐后的外轮廓 = 半宽 2.45 / 半深 2.02，见下）：
        //     | 已有物体                        | 它的范围                        | 与仓库外轮廓的最小间距 |
        //     |---------------------------------|---------------------------------|------------------|
        //     | 耕地（6 列 × 6 行 × 2.5m）      | x,z ∈ [−7.5, +7.5]（`FarmlandEdge`）| 8.95−7.5 = **1.45m** |
        //     | 9 个稻草人                      | x∈{−3,0,3}, z∈{7,10,13}          | 8.95−3 = **5.95m**（沿 X）|
        //     | 巨人稻草人                      | (0, 16.5)                        | 8.95−0 = **8.95m**（沿 X）|
        //     | 玩家出生点                      | (0, −7)                          | 7−2.016 = **4.98m**（沿 Z）|
        //     | 场地边界 arenaHalf=20           | \|x\|,\|z\| ≤ 20                 | 20−13.85 = **6.15m** |
        //     | 商店车绕场路线                  | x 边在 ±14.4、z 边在 ±14.4       | 14.4−13.85 = **0.55m** |
        //     | 内圈树带（46 棵，r=17.4~19）    | 半径 ≥ 17.4                      | **3.55m**（沿 −X） |
        //     | 路灯圈（12 盏，r=19.4）         | 半径 19.4                        | **5.55m** |
        //     | 门廊台阶/门前木箱               | 仓库自身南侧（z 到 −3.57）        | —— 属于本建筑 |
        //   ⇒ 与**所有静态物体**都不重叠 ✓
        //   ⇒ 商店车那 0.55m 是刻意留的：车在训练场里**根本不出现**（`TrainingMode.NoShopTruck`），
        //     但若哪天有人把它放回来，它也是"擦边开过"而不是"穿过房子" ✓
        //
        //   ★ 为什么从 −13 挪到 −11.4（而不是随便挪一点）★
        //     旧位置 −13：屋檐西沿 −16.5，而车绕场的左直边在 **x = −14.4**（0.72×20）
        //     ⇒ 车**每一圈都从房子里穿过去一次** —— 这正是用户看到的"与其它模型重叠"的主因之一。
        //     新的 −11.4 同时满足三件事：屋檐西沿 −13.85 让开车道 0.55m、
        //     屋檐东沿 −8.95 离耕地 1.45m（玩家仍能走这条缝）、
        //     并且**在画面里更靠中间**：旧的西檐外角在相机空间是 |x_c|/z_c = 0.6939
        //     ⇒ 需要窗口宽高比 ≥ 1.155 才不被裁；新位置是 **0.6204** ⇒ 只要 ≥ 1.04
        //     （16:9 / 16:10 / 4:3 / 5:4 全部 ✓，连 1:1 也几乎满幅）✓
        //
        // ── 还有一类"重叠"是随机撒的地面装饰（草/花/蘑菇/碎石/树桩/木桶）──
        //   它们在 `WorldView.BuildScenery` 里按固定种子撒满 r ∈ [8.75, 19.2] 的环带，
        //   **任何**场内位置都会被撒到（这正是"模型重叠"观感的另一半）。
        //   那是战斗表现层的代码，本文件**不改它** —— 改为在训练场里把落进轮廓的那几个
        //   `SCN_*` 装饰**藏起来**（`ClearSceneryUnderBuilding`，只在训练场存在）✓
        //   统计口径：装饰环带面积 π(19.2²−8.75²) ≈ 918m²，本建筑外轮廓 ≈ 21m²
        //   ⇒ 期望被覆盖的地面装饰 ≈ 4~6 个（实测由日志给出）✓

        /// <summary>墙体占地（米）：X 宽 × Z 深。★ v306：6.0×5.0 → **4.2×3.6**（用户要求"缩小"）★</summary>
        public const float FootprintX = 4.20f;
        public const float FootprintZ = 3.60f;

        /// <summary>本地网格的基准尺寸 —— `BuildModel` 里每个盒子的坐标都是按这一套手写的。</summary>
        private const float BaseFootprintX = 6.00f;
        private const float BaseFootprintZ = 5.00f;
        private const float BaseWallTopY = 3.60f;
        private const float BaseRidgeY = 4.60f;

        /// <summary>根节点缩放系数（由"目标尺寸 ÷ 本地基准尺寸"反推，见上面那段推导）。</summary>
        public const float ScaleX = FootprintX / BaseFootprintX;   // 0.7000
        public const float ScaleY = RidgeY / BaseRidgeY;           // 0.6957
        public const float ScaleZ = FootprintZ / BaseFootprintZ;   // 0.7200

        /// <summary>檐口高度（**世界**值，米）= 本地 3.60 × Y 缩放 = 2.504。</summary>
        public const float WallTopY = BaseWallTopY * ScaleY;

        /// <summary>屋脊高度（**世界**值，米）= 建筑总高。★ v306：4.60 → **3.20** ★</summary>
        public const float RidgeY = 3.20f;

        /// <summary>地面高度（竞技场是 y = 0 的平面）。</summary>
        public const float GroundY = 0f;

        /// <summary>门洞中心（世界 XZ）= 建筑**南面**（−Z，朝向相机与出生点一侧）。</summary>
        public static readonly Vector2 DoorXZ = new Vector2(CenterXZ.x, CenterXZ.y - 2.50f * ScaleZ);

        /// <summary>互动半径（米）。玩家 XZ 到 <see cref="DoorXZ"/> ≤ 它 ⇒ 显示悬浮提示 + E 可用。</summary>
        public const float InteractRadius = 4.0f;

        /// <summary>悬浮提示的锚点高度（世界 Y）—— 屋脊上方 1.55 米，不与建筑轮廓打架。</summary>
        public const float HintY = RidgeY + 1.55f;

        /// <summary>挂牌的世界坐标（IMGUI 把「仓库」两字投到这里，让 3D 牌子读起来是招牌）。</summary>
        public static readonly Vector3 SignWorld =
            new Vector3(CenterXZ.x - 2.05f * ScaleX, 2.42f * ScaleY, CenterXZ.y - 3.87f * ScaleZ);

        // ── 逻辑碰撞矩形（墙体 + 屋檐余量；玩家半径在用法处再加）──
        //
        //  墙体 4.2×3.6 ⇒ 半宽 2.10 / 半深 1.80
        //  屋檐出挑 0.5（本地）⇒ ×缩放 = 0.35(X) / 0.36(Z)
        //  再留 0.15 余量把"视觉上贴着墙擦过去"也挡掉
        /// <summary>碰撞矩形半宽（X，米）= 2.10 + 0.35 + 0.15 = **2.60**。</summary>
        public const float BlockHalfX = FootprintX * 0.5f + 0.50f;
        /// <summary>碰撞矩形半深（Z，米）= 1.80 + 0.36 + 0.01 = **2.17**。</summary>
        public const float BlockHalfZ = FootprintZ * 0.5f + 0.37f;

        /// <summary>基础配色（与工程既有取色同源，见类注释 三）。</summary>
        private static readonly Color StoneColor = new Color(0.55f, 0.53f, 0.50f);
        private static readonly Color PlankColor = new Color(0.52f, 0.36f, 0.20f);
        private static readonly Color WoodColor = new Color(0.42f, 0.30f, 0.17f);
        private static readonly Color WoodDarkColor = new Color(0.30f, 0.21f, 0.12f);
        private static readonly Color HoleColor = new Color(0.10f, 0.09f, 0.08f);

        // ══════════════════════════════════════════════════════════════════
        // 运行期
        // ══════════════════════════════════════════════════════════════════

        /// <summary>全进程唯一实例（由 `TrainingBootstrap.Awake` 挂上）。战斗场景里恒为 null。</summary>
        private static TrainingWarehouse _instance;

        private GameBootstrap _boot;
        private Transform _root;

        /// <summary>按颜色缓存的共享材质（**纯 C# 字典**，不含 Unity 对象 ⇒ 可以在字段初始化器里建）。</summary>
        private readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>(8);
        /// <summary>本实例建出来的网格（`OnDestroy` 一起销毁，防换场景泄漏）。</summary>
        private readonly List<Mesh> _meshes = new List<Mesh>(8);
        private Shader _shader;
        private bool _shaderTried;

        /// <summary>上一帧结束时 `Session.Paused` 的值（Esc 撤销用，见 `ReadKeys`）。</summary>
        private bool _pausedAtEndOfLastFrame;

        /// <summary>E 键的上升沿锁存（上一帧是否按住）。</summary>
        private bool _eHeld;

        // IMGUI 样式（只在 OnGUI 里第一次用时建 —— `GUI.skin` 只在 OnGUI 有效）
        private GUIStyle _signText, _hintText, _hintKey;
        private Font _font;
        private bool _stylesReady;

        /// <summary>1×1 白纹理（懒创建：**绝不在静态初始化器里 new Unity 对象**）。</summary>
        private static Texture2D _white;

        private static Texture2D White()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "TRAIN_WarehouseWhite",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            return _white;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ 战斗侧唯一要问的那一个属性（纯查询，无副作用）★
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 本帧的 **E 键是否归训练场仓库**（`GameBootstrap.ReadInput:438` 里纯插入的那一行读它）。
        ///
        /// 语义：面板开着 ⇒ E 是"关闭"，靠近仓库 ⇒ E 是"打开"；
        ///       两种情况下都**不应该**再同时请求商店车（`input.ShopOpen`）✓
        /// 为什么是属性而不是每帧写一个静态布尔：`GameBootstrap.Update`（order 0）**早于**
        ///   本组件的 `Update`（order 100）⇒ 写成布尔会晚一帧，等于没写 ✗
        ///   做成**按需计算**的属性，战斗来问的那一刻就是最新值，帧内顺序问题直接消失 ✓
        /// 战斗场景里的取值链：`TrainingMode.Active == false` ⇒ 第一行就返回 false ✓
        /// </summary>
        public static bool EKeyTakenByWarehouse
        {
            get
            {
                if (!TrainingMode.Active) return false;     // ← 战斗模式在这里就短路了
                TrainingWarehouse w = _instance;
                if (w == null) return false;                // ← 第二道：没有实体就没有这个功能
                if (TrainingVault.IsOpen) return true;      // 面板开着：E 用来关，别去开店
                return w.NearNow();                          // 靠近仓库：E 归仓库
            }
        }

        /// <summary>一行读数（日志/探针用）。</summary>
        public static string Describe()
            => $"中心 ({CenterXZ.x:0.0},{CenterXZ.y:0.0}) 墙体 {FootprintX:0.00}×{FootprintZ:0.00}"
             + $" 檐口 {WallTopY:0.00}m 屋脊 {RidgeY:0.00}m（缩放 {ScaleX:0.###}/{ScaleY:0.###}/{ScaleZ:0.###}）"
             + $" 门 ({DoorXZ.x:0.0},{DoorXZ.y:0.0}) 互动半径 {InteractRadius:0.0}m"
             + $"｜碰撞矩形 ±{BlockHalfX:0.00}×±{BlockHalfZ:0.00}（未含玩家半径）"
             + $"｜隐藏装饰 {_sceneryHidden} 个"
             + $"｜实例 {(_instance != null ? "有" : "无")}"
             + $"｜面板 {(TrainingVault.IsOpen ? "开" : "关")}";

        // ══════════════════════════════════════════════════════════════════
        // 距离
        // ══════════════════════════════════════════════════════════════════

        /// <summary>当前会话（没会话/没 Ready ⇒ null）。顺手懒取 `_boot`。</summary>
        private RunSession Session()
        {
            if (_boot == null) _boot = GetComponent<GameBootstrap>();
            if (_boot == null || !_boot.Ready) return null;
            return _boot.Session;
        }

        /// <summary>玩家到**门洞**的 XZ 距离；取不到玩家时返回 <see cref="float.MaxValue"/>。</summary>
        private float DistanceToDoor(RunSession s)
        {
            if (s == null || s.World == null || s.World.Player == null) return float.MaxValue;
            return Vector2.Distance(s.World.Player.Pos, DoorXZ);
        }

        /// <summary>玩家现在是否在互动半径内（**可被战斗侧安全调用**，见 `EKeyTakenByWarehouse`）。</summary>
        private bool NearNow() => DistanceToDoor(Session()) <= InteractRadius;

        // ══════════════════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _instance = this;
            _boot = GetComponent<GameBootstrap>();
            EnsureBuilt();
            Log.Info(Tag + "实体仓库（手写网格版）已建好：" + Describe());
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
            _meshes.Clear();
            foreach (KeyValuePair<Color, Material> kv in _mats)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _mats.Clear();
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        private void Update()
        {
            // ① 双保险：本组件只活在训练场（见类注释 六）
            if (!TrainingMode.Active) return;

            // ② 建筑被外部清掉（换会话 / 别人 Clear 了场景）⇒ 重建，保证"看得见"永远成立
            EnsureBuilt();

            RunSession s = Session();
            if (s == null)
            {
                _pausedAtEndOfLastFrame = false;
                return;
            }

            ReadKeys(s);

            // ④ ★ v306：**逻辑碰撞** —— 玩家移动之后把 `Pos` 推回建筑外（见 `PushPlayerOut`）
            PushPlayerOut(s);

            // ⑤ ★ v306：把落进建筑轮廓的地面装饰藏起来（每个会话一次，见 `ClearSceneryUnderBuilding`）
            ClearSceneryUnderBuilding(s);

            // ⑥ 记下"这一帧结束时是否暂停"，下一帧 Esc 撤销要用（见 ReadKeys）
            _pausedAtEndOfLastFrame = s.Paused;
        }

        // ══════════════════════════════════════════════════════════════════
        // 输入（E / Esc；面板自己的 B 由 `TrainingVault` 处理）
        // ══════════════════════════════════════════════════════════════════

        private void ReadKeys(RunSession s)
        {
            bool pausedBefore = _pausedAtEndOfLastFrame;

            // ── Esc：面板开着 ⇒ 关闭面板 ──
            //   ⚠️ 战斗自己的 Esc（`GameBootstrap.cs:464` → `Session.TogglePause`）**没有改**，
            //      铁律要求不许动它。所以顺序是：战斗先在 `Update`(order 0) 里把 Esc 收成"暂停"，
            //      本组件在 `Update`(order 100) 里看到「原本没暂停、现在暂停了」⇒ 那就是**同一帧
            //      战斗刚打开的那个菜单** ⇒ 再 `TogglePause()` 一次把它原样收回去（净效果 = 没开）
            //      ⇒ 观感正是用户要的「按 Esc 关面板」，而战斗文件一个字没动 ✓
            //      `TogglePause` 只动 `PhaseBeforePause` / `Paused` 两个字段，
            //      连调两次净零，不留残余状态 ✓
            if (EscKey.Pressed() && TrainingVault.IsOpen)
            {
                TrainingVault.SetOpen(false);
                if (s.Paused && !pausedBefore) s.TogglePause();
                Log.Info(Tag + "Esc 关闭仓库面板｜" + Describe());
            }

            // ── E：开着 ⇒ 关；靠近且没暂停 ⇒ 开 ──
            if (EKeyEdge())
            {
                if (TrainingVault.IsOpen)
                {
                    TrainingVault.SetOpen(false);
                    Log.Info(Tag + "E 关闭仓库面板｜" + Describe());
                }
                else if (!s.Paused && NearNow())
                {
                    TrainingVault.SetOpen(true);
                    Log.Info(Tag + "E 打开仓库面板（实体互动）｜距离 "
                                 + $"{DistanceToDoor(s):0.00}m ≤ {InteractRadius:0.0}m｜" + Describe());
                }
            }
        }

        /// <summary>
        /// E 键的上升沿（**超集**：`isPressed` 上升沿 ∪ `wasPressedThisFrame`）。
        /// 写法与 `EscKey.Pressed` 完全一致：只要键至少持续到某一次更新就不会丢，
        /// 同时保留当帧判定，极短点击也不漏 ⇒ 比战斗那行裸 `wasPressedThisFrame` 更稳，
        /// 且**包含**它 —— 战斗能读到的 E，这里一定也能读到 ✓
        /// </summary>
        private bool EKeyEdge()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) { _eHeld = false; return false; }
            var key = kb.eKey;
            if (key == null) { _eHeld = false; return false; }
            bool down = key.isPressed;
            bool edge = (down && !_eHeld) || key.wasPressedThisFrame;
            _eHeld = down;
            return edge;
#else
            return false;
#endif
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v306：逻辑碰撞（用户原话："增加碰撞禁止玩家穿过"）★★
        // ══════════════════════════════════════════════════════════════════
        //
        // ── 为什么不能加 Collider ──
        //   本工程的玩家移动是**逻辑坐标**：`Actor.Tick` 里 `Pos += dir * MoveSpeed * dt`
        //   （`Actors.cs:849`），再由 `World.ClampToArena` 钳到场地里。
        //   全程不经过 `CharacterController` / `Rigidbody` / 任何物理求解器
        //   ⇒ **Unity 的 Collider 一个都不参与移动**，加上去只会让人误以为挡住了 ✗
        //   （上一轮的类注释 三 也写着"全部零件都没有 Collider"，那时是刻意的，现在依然是 ✓）
        //
        // ── 所以碰撞必须落在**逻辑层**：移动之后把 `Pos` 推出去 ──
        //   矩形 = 墙体 + 屋檐余量（`BlockHalfX/Z`）+ **玩家半径**
        //   ⇒ 挡的是玩家的**身体**，不是中心点（否则半个身子会插进墙里）✓
        //   推力方向取"位移最小的那一条边" ⇒ 从哪边进来就从哪边出去，不会把人横着弹开 ✓
        //
        // ── 为什么放在 `Update`（order 100）而不是 `LateUpdate` ──
        //   `GameBootstrap.Update`（默认 order 0）里跑 `Session.FixedTick` ⇒ 玩家在这一帧已经动完；
        //   本组件 order 100 ⇒ 一定在它之后、且仍在本帧**渲染之前**
        //   ⇒ 修正后的 `Pos` 就是这一帧最终的逻辑位置 ✓
        //   （表现层的模型 transform 会晚一帧跟上来 —— 16ms，肉眼不可见；
        //     要消掉它就得改战斗侧的表现同步，违反"不改战斗"的铁律 ✗）
        //
        // ── 为什么是训练场专属 ──
        //   本方法只被本组件的 `Update` 调用，而本组件**只由 `TrainingBootstrap.Awake` 创建**
        //   ⇒ 战斗场景里根本不存在这段代码 ⇒ 战斗模式的移动与改前**逐字节相同** ✓
        //   （不是"靠开关自觉"，是"编译出来的对象根本不在那里"）

        private void PushPlayerOut(RunSession s)
        {
            if (s == null || s.World == null) return;
            PlayerActor p = s.World.Player;
            if (p == null || p.IsDead) return;

            float hx = BlockHalfX + p.Radius;      // 把玩家半径算进去（挡身体）
            float hz = BlockHalfZ + p.Radius;

            Vector2 d = p.Pos - CenterXZ;           // Vector2 的 .y 就是世界 Z
            if (Mathf.Abs(d.x) >= hx || Mathf.Abs(d.y) >= hz) return;   // 已在外面

            float outX = hx - Mathf.Abs(d.x);       // 推到 X 边外还差多少
            float outZ = hz - Mathf.Abs(d.y);       // 推到 Z 边外还差多少
            if (outX <= outZ)
            {
                float sx = d.x >= 0f ? 1f : -1f;
                p.Pos = new Vector2(CenterXZ.x + sx * hx, p.Pos.y);
            }
            else
            {
                float sz = d.y >= 0f ? 1f : -1f;
                p.Pos = new Vector2(p.Pos.x, CenterXZ.y + sz * hz);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v306：把落进建筑轮廓里的地面装饰藏起来 ★★
        // ══════════════════════════════════════════════════════════════════
        //
        // `WorldView.BuildScenery`（战斗表现层）按固定种子把草/花/蘑菇/碎石/树桩/木桶
        // 撒在半径 8.75~19.2 的环带上 ⇒ 场里**任何**位置都会被撒到，本建筑也不例外
        // （期望 4~6 个）。它们会从地板里探出来 —— 这就是"与其它模型重叠"的那一半观感。
        //
        // 战斗侧的撒点逻辑**一个字都不改**（那是"整个农场的装饰"，不是本建筑的私有逻辑）。
        // 训练场只做一件事：把落进轮廓的那几个 `SCN_*` 对象**藏起来** ✓
        //   · 每个会话跑一次（`Shift+R` 重开一局会重建整片装饰 ⇒ 必须重跑）
        //   · 只在训练场存在 ⇒ 战斗模式零影响 ✓
        //   · 判据用对象名前缀 `SCN_`（`WorldView.SpawnScenery` 里唯一命名处，
        //     `WorldView.cs:1100` `mv.Go.name = "SCN_" + id;`），不碰别的任何 GameObject ✓

        /// <summary>这一批装饰已经被本组件清过的是哪个会话（重开局就重来一次）。</summary>
        private RunSession _sceneryClearedFor;
        /// <summary>上一次隐藏了几个（日志/读数用；`Describe` 是静态的，所以这里也静态）。</summary>
        private static int _sceneryHidden;

        private void ClearSceneryUnderBuilding(RunSession s)
        {
            if (ReferenceEquals(_sceneryClearedFor, s)) return;
            _sceneryClearedFor = s;

            // 比碰撞矩形再宽 0.6m：屋檐外沿紧贴的那几棵也别从墙里探出来
            float hx = BlockHalfX + 0.60f;
            float hz = BlockHalfZ + 0.60f;
            _sceneryHidden = 0;

            // ★ 用不带 `FindObjectsSortMode` 的重载：项目用的 Unity 6 已把它标为废弃（会产生 CS0618 警告）
            Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null) continue;
                string n = t.name;
                if (n == null || n.Length < 5
                    || !n.StartsWith("SCN_", System.StringComparison.Ordinal)) continue;
                Vector3 p = t.position;
                if (Mathf.Abs(p.x - CenterXZ.x) >= hx) continue;
                if (Mathf.Abs(p.z - CenterXZ.y) >= hz) continue;
                t.gameObject.SetActive(false);
                _sceneryHidden++;
            }

            Log.Info(Tag + "已把落进建筑轮廓的地面装饰隐藏 " + _sceneryHidden
                         + " 个（`SCN_*`，装饰由战斗表现层随机撒，这里只让路）｜" + Describe());
        }

        // ══════════════════════════════════════════════════════════════════
        // ① 建模：手写顶点（交付报告 ②）
        // ══════════════════════════════════════════════════════════════════

        private void EnsureBuilt()
        {
            if (_root != null) return;
            BuildModel();
        }

        /// <summary>一个材质组的顶点缓冲（顶点不共享 ⇒ 每面硬边）。</summary>
        private sealed class MeshBuilder
        {
            public readonly List<Vector3> Verts;
            public readonly List<Vector3> Normals;
            public readonly List<int> Tris;
            /// <summary>本组里写了几个盒子（交付报告 ② 的计数来源）。</summary>
            public int Boxes;

            public MeshBuilder(int boxes)
            {
                Verts = new List<Vector3>(boxes * 24);
                Normals = new List<Vector3>(boxes * 24);
                Tris = new List<int>(boxes * 36);
            }

            /// <summary>把一个盒子写进缓冲（模板顶点 × 尺寸 → 旋转 → 平移）。</summary>
            public void AddBox(Vector3 center, Vector3 size, Quaternion rot)
            {
                TrainingWarehouse.UnitBox();          // 懒建模板（一次）
                Vector3[] bv = _boxVerts;
                Vector3[] bn = _boxNormals;
                int[] bt = _boxTris;
                int baseIdx = Verts.Count;
                for (int i = 0; i < bv.Length; i++)
                {
                    Vector3 p = bv[i];
                    p = new Vector3(p.x * size.x, p.y * size.y, p.z * size.z);
                    Verts.Add(center + rot * p);
                    Normals.Add(rot * bn[i]);
                }
                for (int i = 0; i < bt.Length; i++) Tris.Add(baseIdx + bt[i]);
                Boxes++;
            }

            /// <summary>落地成一个 `Mesh`（显式法线 + `RecalculateNormals`，见类注释 三）。</summary>
            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Verts);
                mesh.SetNormals(Normals);
                mesh.SetTriangles(Tris, 0);
                mesh.RecalculateNormals();      // 要求里点名的一步；顶点不共享 ⇒ 与显式法线等价
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ── 单位立方体模板（**只建一次**，之后所有盒子都从它复制）──

        private static Vector3[] _boxVerts;
        private static Vector3[] _boxNormals;
        private static int[] _boxTris;

        /// <summary>
        /// 懒建单位立方体模板：24 顶点 / 36 索引，法线朝外。
        ///
        /// ── 绕序（唯一一处，六个面逐条验过）──
        ///   每个面的四角按"`Cross(c1−c0, c2−c0)` 指向外法线"的顺序写死在表里；
        ///   这正是 Unity 的正面约定（官方 Mesh 文档的示例：`(0,0,0)→(0,1,0)→(1,1,0)`
        ///   得到的法线是 `−Vector3.forward`，与文档里手写的 normals 一致）✓
        ///   ⇒ 面朝外、背面剔除不会把外墙剔没；缩放到非等比尺寸 / 再旋转都不会翻转 ✓
        /// ⚠️ 不能写成静态字段初始化器里的 `new Mesh(...)`（见 `TrainingBootstrap.Mpb` 那段事故）。
        /// </summary>
        private static void UnitBox()
        {
            if (_boxVerts != null) return;

            // 六个面 × 4 角（每行 = 一个面；列 = 从外面看顺时针的四个角）
            // 顺序校验：+X Cross((0,2,0),(0,2,2)) = (4,0,0) ✓  −X = (−4,0,0) ✓
            //           +Y = (0,4,0) ✓  −Y = (0,−4,0) ✓  +Z = (0,0,4) ✓  −Z = (0,0,−4) ✓
            float[][] corners =
            {
                //        x    y    z     x    y    z      x    y    z     x    y    z
                new[]{  1f, -1f, -1f,   1f,  1f, -1f,    1f,  1f,  1f,    1f, -1f,  1f }, // +X
                new[]{ -1f, -1f, -1f,  -1f, -1f,  1f,   -1f,  1f,  1f,   -1f,  1f, -1f }, // −X
                new[]{ -1f,  1f, -1f,  -1f,  1f,  1f,    1f,  1f,  1f,    1f,  1f, -1f }, // +Y
                new[]{ -1f, -1f, -1f,   1f, -1f, -1f,    1f, -1f,  1f,   -1f, -1f,  1f }, // −Y
                new[]{ -1f, -1f,  1f,   1f, -1f,  1f,    1f,  1f,  1f,   -1f,  1f,  1f }, // +Z
                new[]{ -1f, -1f, -1f,  -1f,  1f, -1f,    1f,  1f, -1f,    1f, -1f, -1f }  // −Z
            };
            // 每个面的外法线（与上面那张表一一对应）
            float[][] normalsOfFace =
            {
                new[]{  1f, 0f, 0f }, new[]{ -1f, 0f, 0f }, new[]{ 0f,  1f, 0f },
                new[]{  0f,-1f, 0f }, new[]{  0f, 0f, 1f }, new[]{ 0f,  0f,-1f }
            };

            _boxVerts = new Vector3[24];
            _boxNormals = new Vector3[24];
            _boxTris = new int[36];

            for (int f = 0; f < 6; f++)
            {
                float[] c = corners[f];
                for (int k = 0; k < 4; k++)
                {
                    int vi = f * 4 + k;
                    _boxVerts[vi] = new Vector3(c[k * 3], c[k * 3 + 1], c[k * 3 + 2]);
                    _boxNormals[vi] = new Vector3(normalsOfFace[f][0], normalsOfFace[f][1], normalsOfFace[f][2]);
                }
                int b = f * 4;
                int t = f * 6;
                _boxTris[t + 0] = b + 0; _boxTris[t + 1] = b + 1; _boxTris[t + 2] = b + 2;
                _boxTris[t + 3] = b + 0; _boxTris[t + 4] = b + 2; _boxTris[t + 5] = b + 3;
            }
        }

        private void BuildModel()
        {
            var rootGo = new GameObject("TRAINING_Warehouse");
            _root = rootGo.transform;
            _root.position = new Vector3(CenterXZ.x, GroundY, CenterXZ.y);
            _root.rotation = Quaternion.identity;
            // ★ v306：整个模型在这里缩小到用户要的 4.2×3.6 / 屋脊 3.20 ★
            //   本地网格一个字都没改（见类注释 四 的推导）⇒ 绕序/法线/配色全部沿用上一轮的验证结果 ✓
            _root.localScale = new Vector3(ScaleX, ScaleY, ScaleZ);

            // ── 五个材质组（交付报告 ② 的"几个子网格"就是这五行）──
            var stone = new MeshBuilder(4);
            var plank = new MeshBuilder(10);
            var wood = new MeshBuilder(5);
            var dark = new MeshBuilder(19);
            var hole = new MeshBuilder(1);

            // 局部坐标：原点 = 建筑中心地面；**门朝 −Z（南）**；y = 0 是地面
            Quaternion none = Quaternion.identity;

            // ── 1. 石基座（x ±3.35 / z ±2.85 / 高 0.36）──
            stone.AddBox(new Vector3(0f, 0.18f, 0f), new Vector3(6.70f, 0.36f, 5.70f), none);

            // ── 2. 木地板（顶面 0.60 = 墙脚）──
            plank.AddBox(new Vector3(0f, 0.48f, 0f), new Vector3(6.00f, 0.24f, 5.00f), none);

            // ── 3. 四级骨架：4 根角柱（0.60 → 3.60）──
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    dark.AddBox(new Vector3(sx * 2.86f, 2.10f, sz * 2.36f),
                                new Vector3(0.30f, 3.00f, 0.30f), none);
                }
            }

            // ── 4. 后墙（z = +2.40 是"后背"，门在南）＋左右侧墙 ──
            wood.AddBox(new Vector3(0f, 2.10f, 2.40f), new Vector3(5.80f, 3.00f, 0.20f), none);
            wood.AddBox(new Vector3(-2.90f, 2.10f, 0f), new Vector3(0.20f, 3.00f, 4.80f), none);
            wood.AddBox(new Vector3(2.90f, 2.10f, 0f), new Vector3(0.20f, 3.00f, 4.80f), none);

            // ── 5. 前墙两段 + 门楣 + 门洞内凹 + 门框两根 ──
            //    门洞净宽 2.00（|x| ≤ 1.00）、门洞顶 2.90
            const float doorHalfW = 1.00f;
            const float doorTop = 2.90f;
            const float segW = 2.90f - doorHalfW;                 // 1.90
            wood.AddBox(new Vector3(-(doorHalfW + segW * 0.5f), 2.10f, -2.40f),
                        new Vector3(segW, 3.00f, 0.20f), none);
            wood.AddBox(new Vector3(doorHalfW + segW * 0.5f, 2.10f, -2.40f),
                        new Vector3(segW, 3.00f, 0.20f), none);
            dark.AddBox(new Vector3(0f, (doorTop + BaseWallTopY) * 0.5f, -2.40f),
                        new Vector3(2.30f, BaseWallTopY - doorTop, 0.24f), none);   // 门楣
            hole.AddBox(new Vector3(0f, (0.60f + doorTop) * 0.5f, -2.34f),
                        new Vector3(2.00f, doorTop - 0.60f, 0.10f), none);      // 门洞（深色）
            for (int sx = -1; sx <= 1; sx += 2)
            {
                dark.AddBox(new Vector3(sx * 1.06f, (0.60f + doorTop) * 0.5f, -2.44f),
                            new Vector3(0.14f, doorTop - 0.60f, 0.26f), none);  // 门框立柱
            }

            // ── 6. 三级台阶（门在南，台阶往 −Z 铺）──
            stone.AddBox(new Vector3(0f, 0.09f, -3.55f), new Vector3(3.60f, 0.18f, 1.30f), none);
            stone.AddBox(new Vector3(0f, 0.27f, -3.28f), new Vector3(3.40f, 0.18f, 0.90f), none);
            stone.AddBox(new Vector3(0f, 0.45f, -3.08f), new Vector3(3.20f, 0.18f, 0.55f), none);

            // ── 7. 门廊：两根柱子 + 横梁 + 小坡顶 ──
            for (int sx = -1; sx <= 1; sx += 2)
            {
                dark.AddBox(new Vector3(sx * 1.40f, 1.54f, -3.30f),
                            new Vector3(0.26f, 2.72f, 0.26f), none);
            }
            dark.AddBox(new Vector3(0f, 3.02f, -3.30f), new Vector3(3.10f, 0.26f, 0.30f), none);
            // 小坡顶：朝 −Z 方向压低 16°（绕 X 轴负角，见类注释 三 的绕序说明）
            // 中心 y = 3.28：下沿刚好压在门廊横梁顶（3.15）上，+Z 一端抬到 3.57 与主屋檐口（3.60）接住
            plank.AddBox(new Vector3(0f, 3.28f, -3.30f), new Vector3(3.40f, 0.16f, 2.10f),
                         Quaternion.Euler(-16f, 0f, 0f));

            // ── 8. 双坡大屋顶（檐口 3.60 → 屋脊 4.60，含 0.5m 出檐）──
            //    ⚠️ 这里一律用**本地基准**（`BaseWallTopY` / `BaseRidgeY`）：
            //       本节所有坐标都是 `_root` 的局部坐标，根节点的 `localScale` 会统一缩到世界尺寸 ✓
            const float halfSpan = 3.50f;                        // 含出檐
            const float rise = BaseRidgeY - BaseWallTopY;        // 1.00（本地）
            float slopeLen = Mathf.Sqrt(halfSpan * halfSpan + rise * rise) + 0.14f;   // 3.78
            float slopeDeg = Mathf.Atan2(rise, halfSpan) * Mathf.Rad2Deg;             // 15.945°
            for (int sx = -1; sx <= 1; sx += 2)
            {
                plank.AddBox(new Vector3(sx * halfSpan * 0.5f, (BaseWallTopY + BaseRidgeY) * 0.5f, 0f),
                             new Vector3(slopeLen, 0.20f, 5.60f),
                             Quaternion.Euler(0f, 0f, -sx * slopeDeg));
            }
            dark.AddBox(new Vector3(0f, BaseRidgeY + 0.12f, 0f), new Vector3(0.44f, 0.26f, 5.80f), none);
            // 两道檐口枋（让屋檐在暗光下也有厚度与轮廓）
            for (int sx = -1; sx <= 1; sx += 2)
            {
                dark.AddBox(new Vector3(sx * 3.42f, 3.56f, 0f), new Vector3(0.18f, 0.24f, 5.60f), none);
            }

            // ── 9. 烟囱（让天际线不是一个纯三角形）──
            dark.AddBox(new Vector3(2.20f, 4.30f, 1.50f), new Vector3(0.46f, 1.30f, 0.46f), none);

            // ── 10. 招牌：挑臂 + 吊杆 + 框 + 板（挂在门廊左前方）──
            dark.AddBox(new Vector3(-2.05f, 3.05f, -3.30f), new Vector3(0.14f, 0.14f, 1.50f), none);
            dark.AddBox(new Vector3(-2.05f, 2.88f, -3.85f), new Vector3(0.06f, 0.22f, 0.06f), none);
            dark.AddBox(new Vector3(-2.05f, 2.42f, -3.85f), new Vector3(1.36f, 0.72f, 0.10f), none);
            plank.AddBox(new Vector3(-2.05f, 2.42f, -3.87f), new Vector3(1.24f, 0.60f, 0.12f), none);

            // ── 11. 西侧货架（贴外墙，2 立柱 + 2 层板）──
            for (int sz = -1; sz <= 1; sz += 2)
            {
                dark.AddBox(new Vector3(-3.30f, 1.25f, sz * 1.10f),
                            new Vector3(0.14f, 2.30f, 0.14f), none);
            }
            plank.AddBox(new Vector3(-3.32f, 0.95f, 0f), new Vector3(0.70f, 0.10f, 2.40f), none);
            plank.AddBox(new Vector3(-3.32f, 1.75f, 0f), new Vector3(0.70f, 0.10f, 2.40f), none);

            // ── 12. 门前木箱 ×3（叠一个，让轮廓有层次；都落在地面上）──
            plank.AddBox(new Vector3(-2.60f, 0.35f, -4.60f), new Vector3(0.72f, 0.70f, 0.72f),
                         Quaternion.Euler(0f, 12f, 0f));
            plank.AddBox(new Vector3(-1.78f, 0.30f, -4.30f), new Vector3(0.62f, 0.60f, 0.62f),
                         Quaternion.Euler(0f, -9f, 0f));
            plank.AddBox(new Vector3(-2.58f, 0.90f, -4.58f), new Vector3(0.56f, 0.52f, 0.56f),
                         Quaternion.Euler(0f, 24f, 0f));

            // ── 落地：一组一个 GameObject（无 Collider），共 5 个 ──
            int totalVerts = 0, totalTris = 0, totalBoxes = 0;
            Attach(stone, "Stone", StoneColor, ref totalVerts, ref totalTris, ref totalBoxes);
            Attach(plank, "Plank", PlankColor, ref totalVerts, ref totalTris, ref totalBoxes);
            Attach(wood, "Wood", WoodColor, ref totalVerts, ref totalTris, ref totalBoxes);
            Attach(dark, "WoodDark", WoodDarkColor, ref totalVerts, ref totalTris, ref totalBoxes);
            Attach(hole, "DoorHole", HoleColor, ref totalVerts, ref totalTris, ref totalBoxes);

            Log.Info(Tag + $"手写网格已建：{totalBoxes} 个盒子 / 5 个材质组 / {totalVerts} 顶点 / "
                         + $"{totalTris / 3} 三角形；墙体 {FootprintX:0.0}×{FootprintZ:0.0}m、"
                         + $"檐口 {WallTopY:0.00}m、屋脊 {RidgeY:0.00}m、坡角 {slopeDeg:0.00}°；"
                         + $"落点 ({CenterXZ.x:0.0}, {GroundY:0.0}, {CenterXZ.y:0.0})");
        }

        /// <summary>把一个材质组落地成一个 GameObject（**只有 MeshFilter + MeshRenderer，没有 Collider**）。</summary>
        private void Attach(MeshBuilder b, string name, Color color,
                            ref int totalVerts, ref int totalTris, ref int totalBoxes)
        {
            if (b.Boxes == 0) return;
            Mesh mesh = b.ToMesh("TRAINING_Warehouse_" + name);
            _meshes.Add(mesh);

            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material m = Mat(color);
            if (m != null) mr.sharedMaterial = m;

            totalVerts += b.Verts.Count;
            totalTris += b.Tris.Count;
            totalBoxes += b.Boxes;
        }

        /// <summary>按颜色取共享材质（同色只建一次；着色器兜底链与工程既有写法一致）。</summary>
        private Material Mat(Color c)
        {
            if (_mats.TryGetValue(c, out Material m) && m != null) return m;
            if (!_shaderTried)
            {
                _shaderTried = true;
                _shader = Shader.Find("Universal Render Pipeline/Lit");
                if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (_shader == null) _shader = Shader.Find("Standard");
                if (_shader == null) _shader = Shader.Find("Sprites/Default");
                if (_shader == null) Log.Warn(Tag + "找不到任何可用着色器 ⇒ 仓库会是默认材质");
            }
            m = _shader != null ? new Material(_shader) : null;
            if (m == null) return null;
            m.name = "TRAIN_WarehouseMat";
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            _mats[c] = m;
            return m;
        }

        // ══════════════════════════════════════════════════════════════════
        // ② 招牌文字 + 悬浮互动提示（交付报告 ③）
        // ══════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (!TrainingMode.Active) return;
            if (!_stylesReady) BuildStyles();

            // depth 约定照抄本工程：**越小越靠上**。HUD = 0、仓库面板 = −5
            // ⇒ 提示与招牌取 −4：压在 HUD 之上，但**在仓库面板之下**（面板开着时提示让位）✓
            int prev = GUI.depth;
            GUI.depth = -4;

            DrawSign();
            DrawHint();

            GUI.depth = prev;
        }

        /// <summary>挂牌上的「仓库」：把 <see cref="SignWorld"/> 投到屏幕上（缩放走 `GUI.matrix`）。</summary>
        private void DrawSign()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(SignWorld);
            if (sp.z <= 0.05f) return;

            float sx = sp.x;
            float sy = Screen.height - sp.y;
            if (sx < -240f || sx > Screen.width + 240f) return;

            float dist = Vector3.Distance(cam.transform.position, SignWorld);
            float kz = Mathf.Clamp(26f / Mathf.Max(8f, dist), 0.55f, 1.10f);

            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(sx, sy, 0f), Quaternion.identity,
                                       new Vector3(kz, kz, 1f));
            GUI.Label(new Rect(-70f, -16f, 140f, 32f), "仓 库", _signText);
            GUI.matrix = prev;
        }

        /// <summary>
        /// ★ 悬浮互动提示：「E 打开仓库」（交付报告 ③）★
        ///
        /// 判据只有一条 `NearNow()` —— 与 `EKeyTakenByWarehouse` 内用的是**同一个**方法，
        /// 所以"提示什么时候出现"与"按 E 到底会不会开"不可能分裂 ✓
        /// </summary>
        private void DrawHint()
        {
            if (TrainingVault.IsOpen) return;         // 面板开着：提示让位
            Camera cam = Camera.main;
            if (cam == null) return;

            RunSession s = Session();
            if (s == null || s.Paused) return;        // Esc 菜单开着时不画（那时任何键都不进仓库）
            float d = DistanceToDoor(s);
            if (d > InteractRadius) return;

            var anchor = new Vector3(CenterXZ.x, HintY, CenterXZ.y);
            Vector3 sp = cam.WorldToScreenPoint(anchor);
            if (sp.z <= 0.05f) return;                // 在相机背后

            float sx = sp.x;
            float sy = Screen.height - sp.y;
            if (sx < -400f || sx > Screen.width + 400f) return;

            // ③ 按距离缩放：22 米机距下 ≈ 0.91，走近会略放大（0.62 … 1.25）
            float dist = Vector3.Distance(cam.transform.position, anchor);
            float kz = Mathf.Clamp(24f / Mathf.Max(8f, dist), 0.62f, 1.25f);
            // ④ 轻微上下浮动 + 呼吸缩放
            float bob = Mathf.Sin(Time.unscaledTime * 2.4f) * 6f;
            float pulse = 1f + 0.035f * Mathf.Sin(Time.unscaledTime * 3.1f);

            Matrix4x4 prev = GUI.matrix;
            // 以锚点的屏幕位置为原点缩放 ⇒ 气泡永远"贴着"仓库上方，随相机移动自动跟住
            GUI.matrix = Matrix4x4.TRS(new Vector3(sx, sy - bob * kz, 0f), Quaternion.identity,
                                       new Vector3(kz * pulse, kz * pulse, 1f));

            Texture2D white = White();
            Color prevColor = GUI.color;

            const float bw = 268f, bh = 64f;
            var box = new Rect(-bw * 0.5f, -bh, bw, bh);

            // 外发光（夜里也要看得见 —— 与商店车键帽同路子）
            GUI.color = new Color(1f, 0.86f, 0.40f, 0.16f);
            GUI.DrawTexture(new Rect(box.x - 12f, box.y - 12f, bw + 24f, bh + 24f), white);
            // 投影
            GUI.color = new Color(0f, 0f, 0f, 0.42f);
            GUI.DrawTexture(new Rect(box.x + 2f, box.y + 5f, bw, bh), white);
            // 本体（深色半透明板）
            GUI.color = new Color(0.09f, 0.08f, 0.06f, 0.93f);
            GUI.DrawTexture(box, white);
            // 顶部暖色高光条 + 一圈细边（"高级"的那两笔，不新增任何资源）
            GUI.color = new Color(1f, 0.84f, 0.42f, 0.85f);
            GUI.DrawTexture(new Rect(box.x, box.y, bw, 2f), white);
            GUI.color = new Color(1f, 0.88f, 0.60f, 0.35f);
            GUI.DrawTexture(new Rect(box.x, box.yMax - 1f, bw, 1f), white);
            GUI.DrawTexture(new Rect(box.x, box.y, 1f, bh), white);
            GUI.DrawTexture(new Rect(box.xMax - 1f, box.y, 1f, bh), white);

            // 键帽「E」
            var key = new Rect(box.x + 12f, box.y + (bh - 42f) * 0.5f, 42f, 42f);
            GUI.color = new Color(0.08f, 0.07f, 0.05f, 0.95f);
            GUI.DrawTexture(new Rect(key.x - 2f, key.y - 2f, key.width + 4f, key.height + 4f), white);
            GUI.color = new Color(0.99f, 0.94f, 0.74f, 0.99f);
            GUI.DrawTexture(key, white);

            // 文本（先还原 `GUI.color` 为白：`Label` 的最终色 = GUI.color × style 文字色）
            GUI.color = Color.white;
            GUI.Label(key, "E", _hintKey);
            GUI.Label(new Rect(key.xMax + 12f, box.y + 6f, bw - (key.width + 34f), bh - 12f),
                      "打开仓库", _hintText);

            // 指向下面那栋房子的下三角（逐行递减宽度的横条 —— IMGUI 只能画矩形）
            GUI.color = new Color(0.09f, 0.08f, 0.06f, 0.93f);
            for (int r = 0; r < 9; r++)
            {
                float w = Mathf.Lerp(22f, 3f, r / 8f);
                GUI.DrawTexture(new Rect(-w * 0.5f, box.yMax + r * 2.6f, w, 2.6f), white);
            }

            GUI.color = prevColor;
            GUI.matrix = prev;
        }

        private void BuildStyles()
        {
            _stylesReady = true;

            // 中文只能靠系统字体（工程里没有任何 .ttf，与 `ImGuiHud` 同一套做法）
            if (_font == null)
            {
                string[] cands = null;
                try { cands = L10n.Fonts; } catch { cands = null; }
                if (cands == null || cands.Length == 0) cands = new[] { "Microsoft YaHei", "SimHei", "Arial" };
                for (int i = 0; i < cands.Length; i++)
                {
                    Font f = Font.CreateDynamicFontFromOSFont(cands[i], 22);
                    if (f != null) { _font = f; break; }
                }
            }

            _signText = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _signText.normal.textColor = new Color(1f, 0.94f, 0.72f);

            _hintText = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _hintText.normal.textColor = new Color(1f, 0.97f, 0.88f);

            _hintKey = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _hintKey.normal.textColor = new Color(0.10f, 0.08f, 0.05f);

            if (_font != null)
            {
                _signText.font = _font;
                _hintText.font = _font;
                _hintKey.font = _font;
            }
        }
    }
}