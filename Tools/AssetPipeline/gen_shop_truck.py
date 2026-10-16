# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 流动商店车（农村卡车 + 满斗水果）生成器
============================================================
需求原话：
  "blender制作一个商店车建模（**农村卡车风格，载满各种水果**）"
  车 1 分钟刷新、存在 10 秒；从地图右上角进入、绕图一圈、回到顶上中间停 10 秒；
  入场/离场要有动画。车沿路径移动**由游戏代码用 Transform 沿路点做**。

────────────────────────────────────────────────────────────────
一、朝向约定：和 Assets/Resources/Art/Characters/ 保持一致
────────────────────────────────────────────────────────────────
游戏代码里写死的那句话（Assets/Scripts/Bootstrap/WorldView.cs:999）：

    "模型正面是 Unity +Z（Blender -Y 经导出烘焙而来），SetFacing 组合出朝向"

所以本文件的约定是：
    Blender 里 **-Y = 车头**（+Y = 货斗/车尾），X = 右，Z = 上
    导出后 Unity 里 **+Z = 车头**，Y = 上
  → Unity 里让车朝 dir 开：SetFacing(atan2(dir.x, dir.z) * Rad2Deg)
  → 想让车头指向 +X 就转 yaw = 90°

这和 gen_anim / gen_pig / gen_plants_biped / gen_player_v2 的"正面 -Y"完全一致。

────────────────────────────────────────────────────────────────
二、轴向只处理一次（踩过的坑）
────────────────────────────────────────────────────────────────
`bake_yup=False` —— 用 axis_forward='-Z' / axis_up='Y' 让**导出器**做
Blender Z-up → Unity Y-up 的换算。**不要再自己烘 -90°**：
两遍转换会让车躺在场地里（seedfall_kit.export_fbx 的 docstring 记着这个坑）。

高度在 **Z 轴**、原点在**车底中心**（zmin = 0）——
游戏把单位摆在 pos(y=0) 上，原点一偏就整台车浮空或陷地。

────────────────────────────────────────────────────────────────
三、为什么是"空物体木偶"而不是骨架，而且 Root 必须不动
────────────────────────────────────────────────────────────────
★ 与 gen_player_v2 的关键区别：这台车**不需要 Avatar Mask**（没有上半身/下半身
  分工），所以不需要真骨架，沿用 gen_pig / gen_anim 的**空物体木偶**即可。

★ 层级里 Root 与 Bob 是**两个**空物体，这是本文件最重要的一处设计：

        Root (Blender 世界原点, **没有动画数据，一动不动**)
          ├ Bob          ← 车身颠簸 / 俯仰 / 侧倾 打在这里
          │   ├ Body      整车外壳
          │   ├ BedTrunk  货斗本体
          │   ├ BedWalls  斗壁
          │   ├ Fruits    水果堆（单独一份，用来做"果堆轻晃"）
          │   └ Awning    车尾遮阳棚顶
          ├ Wheel_FL / FR / RL / RR   轮子（枢轴 = 轮心，绕自身 X 转）
          └ AwningPosts_L / R         棚柱（2 根粗柱，立在货斗外侧）

  理由：游戏侧是**用 Transform 沿路点移动**的。如果颠簸打在 Root 上，clip 里就会
  带一条 Root 的位移曲线；Unity 的旧版 Animation 组件把 clip 的 root 变换曲线
  **应用到播放对象自身**（== 整台车的位置）——那就变成"车自己往前跑 + 代码再推
  一把"，正是需求里警告的"车会飞出去"。所以：
    · Root：静止锚点，**一个关键帧都不打**（验证器会断言它恒为 (0,0,0)）
    · Bob ：所有颠簸/俯仰/侧倾都打在它身上，且始终只是**相对 Root 的小偏移**
  gen_pig 里 AxisFix / Root 的分工是同一个思路的另一个用法。

★ 动画里**不含任何路径位移**。truck_enter / truck_exit 只是"相对停车位的最后
  3.6m 加减速 + 俯仰"，不是整条路线 —— 路线由游戏代码给。

────────────────────────────────────────────────────────────────
四、踩过的坑（都实测过，别再踩）
────────────────────────────────────────────────────────────────
  ① `primitive_cylinder_add(depth=)` 收的是**全高**，不是半径倍数。
     轮子直径 0.72 → depth=0.72；写成 0.72*0.5 就得到一堆薄饼。
  ② `parent_set(type='OBJECT', keep_transform=True)` 之后**再写 child.location**
     等于在"保持世界变换算出来的本地位置"上**覆盖**，几何会静默平移。
     所以父级链一建好就不再动 .location —— 复位一律用"记下来的 rest 值"。
  ③ 回读角度要先看 `rotation_mode`：FBX 回读后 `rotation_euler` 恒为 0，
     必须先 `to_euler()`。本文件把轮子**逐帧**打 Euler 关键帧（不是只在首末两点
     插值），所以回读曲线就是真值，且不会出现 0/2π 的环绕跳变。
  ④ 顺序不能换：**先 frame_set → 再摆姿态 → 再 keyframe_insert**
     （frame_set 触发依赖图求值会把刚摆好的姿态冲掉，见 gen_pig.export_anim）。
  ⑤ 每个 FBX 重搭一次场景（照 gen_player_v2 的做法），保证"一个 FBX 一条 take"。
  ⑥ 遮阳棚别做太大：60° 俯视下，z=2.30 的棚顶在水平方向会投影出
     (2.30-1.3)/tan(60°) ≈ 0.55 的遮挡带；第一版做成整斗覆盖，果子全被吃掉，
     俯视只剩一块布。最终缩到**只盖车尾 0.80m**（斗长的 40%），
     果子主体的中前段完全露出来 —— 这是对着预览图调了两遍的旋钮。

────────────────────────────────────────────────────────────────
五、产出
────────────────────────────────────────────────────────────────
    Assets/Resources/Art/Props/shop_truck.fbx        静态模型（可被代码沿路点移动）
    Assets/Resources/Art/Anim/truck_drive.fbx        行驶循环 36 帧 / 1.50s / LOOP
    Assets/Resources/Art/Anim/truck_enter.fbx        入场 60 帧 / 2.50s / ONCE
    Assets/Resources/Art/Anim/truck_exit.fbx         离场 60 帧 / 2.50s / ONCE
    Docs/captures/shop_truck_preview.png             60° 俯视 + 侧 + 正 对照图

用法：
    blender -b --factory-startup -P gen_shop_truck.py -- all
    blender -b --factory-startup -P gen_shop_truck.py -- static
    blender -b --factory-startup -P gen_shop_truck.py -- anim [名字...]
    blender -b --factory-startup -P gen_shop_truck.py -- verify
    blender -b --factory-startup -P gen_shop_truck.py -- preview
"""

import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402

import bpy                          # noqa: E402
import mathutils                    # noqa: E402

D = math.radians
V = mathutils.Vector

# ---------------------------------------------------------------- 路径
PROJECT = r"D:\Unity\project\Seedfall守园者"
PROP_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Props")
ANIM_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Anim")
PREVIEW_DIR = os.path.join(HERE, "preview")
CAPTURE = os.path.join(PROJECT, "Docs", "captures", "shop_truck_preview.png")

ID = "shop_truck"

# ---------------------------------------------------------------- 调色板
# 只**新增**键，不改既有键（改了会污染已验收的 4 角色 / 16 植物 / 环境 / 3 武器）。
kit.PALETTE.update({
    # 车身：老农场皮卡的晒褪砖红，不是鲜红（鲜红会读成消防车）
    "TruckBody":   "#B4553F",
    "TruckCab":    "#A04834",
    "TruckTrim":   "#D9C6A6",
    "TruckWood":   "#A67C52",
    "TruckWoodDk": "#8A6640",
    "TruckMetal":  "#9BA0A2",
    "TruckDark":   "#4A4034",
    "TruckGlass":  "#8FB8C4",
    # 篷布（麻布米黄 —— 刻意低饱和，和水果的高饱和色拉开）
    "TruckCanvas": "#D8C6A0",
    "TruckTire":   "#3A3A3A",
    # 水果：**六种，色相各占约 60°，没有两个是邻近色**
    # （需求："颜色区分大，这样远处一眼能看出是商店车来了"）
    "Fruit_Red":    "#C8382F",   # 红苹果
    "Fruit_Orange": "#E8871E",   # 橙
    "Fruit_Yellow": "#F2D22E",   # 黄柠檬
    "Fruit_Green":  "#2F7D3A",   # 绿西瓜
    "Fruit_Purple": "#5B3A87",   # 紫葡萄
    "Fruit_Pink":   "#E87A9A",   # 粉桃
    # 小件
    "Fruit_Stem":   "#6B8F3A",   # 果柄
    "Fruit_Pumpkin": "#E07A2A",  # 南瓜（和橙靠明度拉开）
})

# ---------------------------------------------------------------- 规格（需求硬指标）
TARGET_LEN = (3.5, 4.5)      # 长 m
TARGET_WID = (1.8, 2.2)      # 宽 m
TARGET_HGT = (2.0, 2.6)      # 高 m
TRI_BUDGET = 2500            # 面数 < 2500
FPS = 24.0

#: 本车用到的调色板键（材质名合法性由验证器核）
#: TruckCanvas 曾用于遮阳棚，第三版把棚换成牌匾后就没用上了 —— 这里同步删掉，
#: 免得验证器把"声明了但没用到"当成正常（声明与实物不一致比少声明更坏）。
TRUCK_KEYS = ["TruckBody", "TruckCab", "TruckTrim", "TruckWood", "TruckWoodDk",
              "TruckMetal", "TruckDark", "TruckGlass", "TruckTire",
              "Fruit_Red", "Fruit_Orange", "Fruit_Yellow", "Fruit_Green",
              "Fruit_Purple", "Fruit_Pink", "Fruit_Stem", "Fruit_Pumpkin"]

# ================================================================ 几何参数（米）
# 全部在 **Blender 坐标系** 里写：X = 右，Y = 前后（**-Y 是车头**），Z = 上
#
# 从前到后（y 递减 = 越靠前）的整体分工 —— 改这里前先对一遍这条线：
#   y = -2.04  前保险杠最前缘
#   y = -1.92  引擎盖前端 / 车头面板
#   y = -1.93 .. -0.83   引擎盖（hood）
#   y = -1.20 .. +0.10   驾驶室（cab），后壁 +0.10
#   y = -1.14 .. +1.04  货斗（bed），前壁（headboard）在 -1.14，尾门在 +1.04
#   y = +2.02  后保险杠最后缘
#   y = +0.15 / +0.95  前轴 / 后轴
GEO = dict(
    # --- 轮 ---
    wheel_r=0.36, wheel_w=0.26, wheel_x=0.84,
    wheel_fy=-0.95, wheel_ry=0.85,         # 前轴 / 后轴
    hub_r=0.132,
    # --- 底盘（车架纵梁）---
    chassis_y0=-1.86, chassis_y1=1.84,     # 车架前后端
    chassis_z=0.46, chassis_h=0.18, chassis_w=1.58,
    # --- 前后保险杠 ---
    bumper_fy=-1.98, bumper_ry=1.96, bumper_z=0.475, bumper_w=1.80,
    # --- 踏板 ---
    step_z=0.34,
    # --- 挡泥板（平板式，覆盖轮顶）---
    fender_z=0.71, fender_w=0.38, fender_t=0.06,
    # --- 引擎盖 / 车头面板 ---
    hood_y=-1.38, hood_len=1.10, hood_z=0.76, hood_h=0.38, hood_w=1.70,
    grille_y=-1.815, nose_y=-1.90, nose_z=0.48, nose_h=0.26,
    # --- 驾驶室 ---
    cab_y=-0.55, cab_depth=1.30, cab_w=1.90, cab_z=0.92, cab_h=0.64,
    cabroof_z=1.540, cabroof_h=0.10,
    win_z=1.26, win_h=0.50,
    cab_front=-1.20, cab_back=0.10,
    # --- 货斗 ---
    # ★★ 第三版修正的**比例**问题，值得记下来 ★★
    #   第一/二版：斗底上表面 0.92、斗沿 1.22 → 壁只有 0.30 高，而果堆顶到 1.76。
    #   侧视看就是"几颗球飘在车厢顶上"，不是"装了一车货"。
    #   实车比例：斗底上表面离地 ≈0.90、斗壁高 ≈0.50 → 斗沿 ≈1.40。
    #   这次把斗沿抬到 1.40（壁 0.50），同时把水果半径整体缩约 25%，
    #   让堆顶(≈1.44) 只比斗沿高一点点 —— 这才是"装满一车水果"。
    #   两个数必须一起改：只抬斗沿果子露不出来，只缩果子又显得斗空。
    #
    #   箱子算术（改任一个都要重算这一串）：
    #     BedFloor 盒子：中心 z = bed_z，高 bed_h
    #     → 上表面 floor_top = bed_z + bed_h/2 = 0.76 + 0.28/2 = **0.90** ✓
    #     BedWalls 从 floor_top 长到 wall_top → 壁高 = 1.40 - 0.90 = 0.50 ✓
    bed_z=0.76, bed_h=0.28, bed_w=2.00,
    bed_y=-0.05, bed_depth=2.00,           # 货斗中心 y / 进深
    bed_y0=-1.05, bed_y1=0.95,             # 斗内地面范围
    bedwall_t=0.10, wall_top=1.40,
    # --- 车头方向的**招牌牌匾**（不是遮阳棚，见 build_body_parts 的说明）---
    # ★ 牌匾放在驾驶室上方，**不在货斗上方** ★
    #   放货斗上方 = 60° 俯视时把"满斗水果"整个盖掉（第一、二版就是这么废的）。
    sign_y=-0.90, sign_depth=0.62, sign_z=2.34, sign_h=0.13, sign_w=1.70,
    sign_pitch=-5.0,                       # 负角 = 车头侧抬高（见 build_body_parts）
    signpost_y=-1.25, signpost_x=0.74, signpost_z=1.95, signpost_h=0.86,
    signpost_r=0.050,
)

#: 车头那一侧（-Y）的所有部件都应该 ≤ 这个值 —— 用于自检朝向没写反
NOSE_Y = -2.05


def geo(k):
    return GEO[k]


# ================================================================ 通用
def drop_meta(path):
    """删掉 Unity 生成的 .fbx.meta —— 同名 FBX 内容变了但 meta 还在时 Unity 可能不重导。"""
    meta = path + ".meta"
    if os.path.exists(meta):
        os.remove(meta)
        print(f"[TRUCK] 已删 {os.path.basename(meta)}（强制 Unity 重新导入）")


def set_parent(child, parent):
    """带保持世界变换的父子绑定（直接赋 .parent 会让子物体跳位）。

    ★ 绑完之后**不要再写 child.location** ★
    绑定算出来的本地位置就是正确值；再覆盖一次等于把几何静默平移
    （gen_pig.pose 用 rest 复位就是为了绕开这一点）。
    """
    bpy.ops.object.select_all(action='DESELECT')
    child.select_set(True)
    parent.select_set(True)
    bpy.context.view_layer.objects.active = parent
    bpy.ops.object.parent_set(type='OBJECT', keep_transform=True)


def empty(name, loc=(0.0, 0.0, 0.0)):
    bpy.ops.object.empty_add(type='PLAIN_AXES', location=loc)
    e = bpy.context.active_object
    e.name = name
    return e


# ================================================================ 轮子
def make_wheel(name, cx, cy):
    """一只轮子：轮胎 + 轮毂 + 3 根轮辐，合成一个网格，**枢轴放到轮心**。

    ★ 枢轴必须在轮心 ★
    木偶是"整块肉挂在关节上"的模型，枢轴 = 旋转中心。
    mesh 造在世界坐标 (cx, cy, R)，于是 set_origin 直接把原点钉到轮心；
    之后绕自身 X 转就是原地转，不会绕整车轴心画圆。
    （验证器 check_hierarchy 要断言的就是这件事：轮心与枢轴偏差 < 5mm。）

    ★ 3 根轮辐而不是光板 ★
    纯黑圆柱转起来**看不出来在转**（没有视觉参考）。加 3 根深色辐条后，
    侧面一眼能看出轮子转了多少。每根 12 面，3 根 = 36 面/轮。
    """
    # ★ 10 边不行 ★ 圆柱的顶点按 2π/n 均布，n 不是 4 的倍数时**没有一个顶点落在
    #   最下方**，轮子的最低点变成一段弦，实测 zmin 从 0 涨到 0.0176（车浮起来了）。
    #   12 边（4 的倍数）在 270° 处有顶点 → 切点正好在最低点，zmin 精确为 0。
    r, w = geo("wheel_r"), geo("wheel_w")
    c = (cx, cy, r)
    tire = kit.cyl(f"{name}_Tire", c, r, w, "TruckTire", verts=12,
                   rot=(0.0, D(90.0), 0.0))
    hub = kit.cyl(f"{name}_Hub", c, geo("hub_r"), w * 1.12,
                  "TruckTrim", verts=8, rot=(0.0, D(90.0), 0.0), smooth=False)
    parts = [tire, hub]
    for i in range(3):
        # 绕 X 转 = 在 YZ 平面里铺开辐条（轮子的转动平面就是 YZ）
        parts.append(kit.box(f"{name}_Spoke{i}", c, (0.055, 0.20, 0.20), "TruckDark",
                             rot=(D(30.0 + i * 60.0), 0.0, 0.0), smooth=False))
    obj = kit.join(parts, name)
    kit.apply_transforms(obj)
    kit.set_origin(obj, c)
    return obj


# ================================================================ 水果
def fruit_sphere(name, loc, r, color, scale=(1.0, 1.0, 1.0), segs=8, rings=6, rot=(0, 0, 0)):
    """一颗水果：低面数球。

    ★ 8×6 的 UV 球实测 **65 三角面/颗**，不是 48 ★
    （公式"2·segs·(rings-1)"忽略了极点三角扇的排布）。
    第一版按 48 估，25 颗估出 960、实际 1640，整台车冲到 2728 面。
    18 颗 × 65 ≈ 1170 是这套配色的面数底价；
    想再省只能减颗数，不能减段数（8×5 的球在俯视下能看出棱）。
    """
    return kit.sphere(name, loc, r, color, scale=scale, segs=segs, rings=rings, rot=rot)


def build_fruit_pile():
    """货斗里的一堆水果：6 种主色 + 南瓜 + 葡萄。

    配色原则：红 / 橙 / 黄 / 绿 / 紫 / 粉 —— 六个色相各占约 60°，没有邻近色。
    形体原则：大果（西瓜 / 南瓜）当体积，小果（苹果 / 橙 / 柠檬 / 桃）当碎块，
    上面再压一层 —— 俯视看是"一斗满满的果子"，不是"摆整齐的一排球"。
    """
    b0, b1 = geo("bed_y0"), geo("bed_y1")            # -1.05 .. +0.95（斗内）
    floor = geo("bed_z") + geo("bed_h") * 0.5         # 斗内地面 z = 0.90

    # ★ 货斗内地面在 y ∈ [-1.05, +0.95]，**中心是 -0.05 而不是 0** ★
    #   第三版把所有水果坐标整体前移过多，实测水果材质的 y 质心跑到 -0.221 ——
    #   落在车头那一半了（验证器正是用质心 y 来判朝向，于是报"朝向反了"）。
    #   修正：把整堆果子沿 +Y 平移 +0.18，让质心回到货斗中心附近。
    #   这台车是"车头 = -Y"，果堆偏前会被读成"货斗在车头前面"。
    FSHIFT = 0.18

    def col(i):
        """5 列，落在斗内地面范围内，两端留边距。"""
        return b0 + (b1 - b0) * (0.12 + 0.19 * i)

    def fy(y):
        """货斗内 y 的最终值（统一加 FSHIFT）。"""
        return y + FSHIFT

    parts = []

    # --- 底层：大果，直接坐在斗底 ---
    # 大果用 8×6（和小的同一档）：10×7 的西瓜一颗 112 面，两颗就吃掉一个挡泥板的预算。
    # 第一版 3088 面、超预算 588 —— 降段数是这次唯一有效的省法。
    #
    # ★ 半径在第三版整体缩过约 25% ★
    #   第一版西瓜 r=0.216，三层堆到斗底之上 0.84m，而斗壁只有 0.50m ——
    #   侧视就是"球飘在车厢顶上"。缩到 0.185 后堆顶 ≈0.44m，
    #   只比斗沿高一点点，才读得出"装满"。
    #   面数只跟**段数**有关、与半径无关，所以这一改是零成本的。
    parts.append(fruit_sphere("Fruit_Watermelon_A", (col(1), fy(-0.30), floor + 0.170), 0.170,
                              "Fruit_Green", scale=(1.0, 1.22, 0.98), segs=8, rings=6))
    parts.append(fruit_sphere("Fruit_Watermelon_B", (col(3), fy(0.02), floor + 0.170), 0.170,
                              "Fruit_Green", scale=(1.0, 1.22, 0.98), segs=8, rings=6))
    parts.append(fruit_sphere("Fruit_Pumpkin_A", (col(0), fy(0.22), floor + 0.146), 0.160,
                              "Fruit_Pumpkin", scale=(1.0, 1.0, 0.80), segs=8, rings=6))
    parts.append(fruit_sphere("Fruit_Pumpkin_B", (col(4), fy(-0.10), floor + 0.146), 0.160,
                              "Fruit_Pumpkin", scale=(1.0, 1.0, 0.80), segs=8, rings=6))

    # --- 中层：红苹果 + 橙 —— 六个主色都要在俯视里露脸，这是配色要求的下限 ---
    mid = floor + 0.240
    for nm, x, y, z, r, c in (
        ("Fruit_Apple_A",  col(2), -0.62, mid,        0.122, "Fruit_Red"),
        ("Fruit_Apple_B",  col(4),  0.60, mid,        0.122, "Fruit_Red"),
        ("Fruit_Orange_A", col(0),  0.72, mid - 0.02, 0.128, "Fruit_Orange"),
        ("Fruit_Orange_B", col(3), -0.66, mid + 0.03, 0.128, "Fruit_Orange"),
    ):
        parts.append(fruit_sphere(nm, (x, fy(y), z), r, c, segs=8, rings=6))

    # --- 上层：黄柠檬 + 粉桃（压在正中，形成堆顶）---
    top = floor + 0.395
    for nm, x, y, z, r, c in (
        ("Fruit_Lemon_A",  col(2),  0.18, top,        0.112, "Fruit_Yellow"),
        ("Fruit_Lemon_B",  col(1), -0.06, top - 0.02, 0.108, "Fruit_Yellow"),
        ("Fruit_Peach_A",  col(3),  0.34, top + 0.02, 0.114, "Fruit_Pink"),
        ("Fruit_Peach_B",  col(2), -0.40, top + 0.01, 0.114, "Fruit_Pink"),
    ):
        parts.append(fruit_sphere(nm, (x, fy(y), z), r, c, segs=8, rings=6))

    # --- 葡萄：两处小串（紫），压在两前角 —— 紫色是六个色相里最容易被挤掉的 ---
    for nm, x, y, z in (("Fruit_Grapes_A", col(1), -0.88, floor + 0.150),
                        ("Fruit_Grapes_B", col(3), -0.92, floor + 0.150)):
        for k, (dx, dy, dz, rr) in enumerate(((0.0, 0.0, 0.0, 0.086),
                                              (0.074, 0.044, -0.048, 0.068))):
            parts.append(fruit_sphere(f"{nm}_{k}", (x + dx, fy(y + dy), z + dz), rr,
                                      "Fruit_Purple", segs=8, rings=6))

    # --- 果柄：给两颗大果加一点点绿，读起来像"鲜果"而不是"皮球" ---
    for nm, x, y, z in (("Fruit_Watermelon_A", col(1), -0.30, floor + 0.332),
                        ("Fruit_Watermelon_B", col(3), 0.02, floor + 0.332)):
        parts.append(kit.cyl(f"{nm}_Stem", (x, fy(y), z), 0.026, 0.070,
                             "Fruit_Stem", verts=6))
    return parts


# ================================================================ 整车外壳
def build_body_parts():
    """整车外壳（不含轮子 / 不含水果）。

    返回 (body, bed, walls, awning, awning_posts) —— 分组是为了让 Unity 侧能按
    分组换色/隐藏（比如以后想做"斗里空着"的状态），也方便验证器逐组报尺寸。
    """
    g = GEO
    body, bed, walls, awning, awn_posts = [], [], [], [], []

    # ---------------------------------------------------------- 车架 + 前后保险杠
    body.append(kit.box("Chassis", (0.0, (g["chassis_y0"] + g["chassis_y1"]) * 0.5,
                                    g["chassis_z"]),
                        (g["chassis_w"], g["chassis_y1"] - g["chassis_y0"], g["chassis_h"]),
                        "TruckDark"))
    body.append(kit.box("Bumper_Front", (0.0, g["bumper_fy"], g["bumper_z"]),
                        (g["bumper_w"], 0.12, 0.15), "TruckMetal", smooth=False))
    body.append(kit.box("Bumper_Rear", (0.0, g["bumper_ry"], g["bumper_z"]),
                        (g["bumper_w"], 0.12, 0.15), "TruckMetal", smooth=False))

    # ---------------------------------------------------------- 踏板（前后轮之间）
    for side in (-1, 1):
        body.append(kit.box(f"Step_{'R' if side > 0 else 'L'}",
                            (side * 0.94, 0.02, g["step_z"]), (0.30, 1.16, 0.08),
                            "TruckDark", smooth=False))

    # ---------------------------------------------------------- 挡泥板（平板式，盖过轮顶）
    # ★ 为什么用平板不用半圆拱 ★
    #   第一版用"圆柱压扁"做拱形，绕 X 转 90° 时 Blender 的缩放是**绕世界轴**、
    #   旋转又是先应用的，结果拱被压成一片扁板贴在轮子侧面（preview 里读不出是啥）。
    #   低多边形农场卡车本来就是直翼子板，用一块方板更稳、更好读，还只有 12 面/块。
    wf, wr = g["wheel_fy"], g["wheel_ry"]
    for tag, ax in (("F", wf), ("R", wr)):
        for side in (-1, 1):
            body.append(kit.box(f"Fender_{tag}{'R' if side > 0 else 'L'}",
                                (side * g["wheel_x"], ax, g["fender_z"]),
                                (g["fender_w"], 1.06, g["fender_t"]),
                                "TruckBody", smooth=False))

    # ---------------------------------------------------------- 引擎盖 + 车头面板
    body.append(kit.box("Hood", (0.0, g["hood_y"], g["hood_z"]),
                        (g["hood_w"], g["hood_len"], g["hood_h"]), "TruckBody", smooth=False))
    body.append(kit.box("Grille", (0.0, g["grille_y"], 0.70), (1.30, 0.09, 0.42),
                        "TruckMetal", smooth=False))
    for i in range(2):                       # 2 条横栅 —— 老卡车的辨识符（面数预算内）
        body.append(kit.box(f"GrilleBar_{i}", (0.0, g["grille_y"] - 0.05, 0.60 + i * 0.20),
                            (1.22, 0.05, 0.055), "TruckDark", smooth=False))
    for side in (-1, 1):                     # 圆形大灯，贴在车头面板上、栅格外侧
        body.append(kit.cyl(f"Headlight_{'R' if side > 0 else 'L'}",
                            (side * 0.62, g["grille_y"] - 0.02, 0.94), 0.104, 0.09,
                            "TruckTrim", verts=12, rot=(D(90.0), 0.0, 0.0)))

    # ---------------------------------------------------------- 驾驶室
    body.append(kit.box("Cab", (0.0, g["cab_y"], g["cab_z"]),
                        (g["cab_w"], g["cab_depth"], g["cab_h"]), "TruckCab", smooth=False))
    body.append(kit.box("CabRoof", (0.0, g["cab_y"], g["cabroof_z"]),
                        (g["cab_w"] * 1.03, g["cab_depth"] * 1.03, g["cabroof_h"]),
                        "TruckTrim", smooth=False))
    # 挡风 / 后窗：**贴在驾驶室前后立面上**（稍微凸出 0.02，否则会被 cab 面遮住）
    body.append(kit.box("Windshield", (0.0, g["cab_front"] - 0.02, g["win_z"]),
                        (1.58, 0.05, g["win_h"]), "TruckGlass", smooth=False))
    body.append(kit.box("RearWindow", (0.0, g["cab_back"] + 0.02, g["win_z"]),
                        (1.58, 0.05, g["win_h"]), "TruckGlass", smooth=False))
    for side in (-1, 1):
        # 侧窗：前段长（门）、后段短（B 柱之后），中间留一点门框
        body.append(kit.box(f"SideWin_{'R' if side > 0 else 'L'}",
                            (side * (g["cab_w"] * 0.5 + 0.005), -0.68, g["win_z"]),
                            (0.03, 0.70, g["win_h"]), "TruckGlass", smooth=False))
        body.append(kit.box(f"SideWinRear_{'R' if side > 0 else 'L'}",
                            (side * (g["cab_w"] * 0.5 + 0.005), -0.08, g["win_z"]),
                            (0.03, 0.30, g["win_h"] * 0.92), "TruckGlass", smooth=False))
        # 后视镜：细杆 + 镜片，朝车头方向探出去。
        # ★ 镜片不能探太远 ★ 第一版放到 x=±1.13，整车宽被它顶到 2.29m，
        #   直接冲破"宽 ≤ 2.2m"的硬指标（镜子是车上最外侧的东西，要按它算总宽）。
        body.append(kit.box(f"MirrorArm_{'R' if side > 0 else 'L'}",
                            (side * 1.00, g["cab_front"] + 0.06, 1.40),
                            (0.12, 0.035, 0.035), "TruckDark", smooth=False))
        body.append(kit.box(f"Mirror_{'R' if side > 0 else 'L'}",
                            (side * 1.06, g["cab_front"] + 0.06, 1.40),
                            (0.025, 0.09, 0.16), "TruckTrim", smooth=False))

    # ---------------------------------------------------------- 货斗
    bed.append(kit.box("BedFloor", (0.0, g["bed_y"], g["bed_z"]),
                       (g["bed_w"], g["bed_depth"], g["bed_h"]), "TruckWood", smooth=False))
    bed.append(kit.box("BedLiner", (0.0, g["bed_y"], g["bed_z"] + g["bed_h"] * 0.5 - 0.02),
                       (g["bed_w"] - 0.20, g["bed_depth"] - 0.20, 0.05),
                       "TruckWoodDk", smooth=False))
    wt, wtop = g["bedwall_t"], g["wall_top"]
    wall_z0 = g["bed_z"] + g["bed_h"] * 0.5           # 斗沿以下 = 0.84
    wall_h = wtop - wall_z0
    wall_cz = (wall_z0 + wtop) * 0.5
    for side in (-1, 1):
        walls.append(kit.box(f"Wall_{'R' if side > 0 else 'L'}",
                             (side * (g["bed_w"] * 0.5 - wt * 0.5), g["bed_y"], wall_cz),
                             (wt, g["bed_depth"], wall_h), "TruckBody", smooth=False))
        # 斗沿压条 ★ 第三版从 TruckTrim(米白) 改成 TruckBody(红) ★
        #   压条是**俯视/后视里最容易看到的那条边**，用米白会让整车读成"白车"。
        #   米白只留给真正需要对比的地方（驾驶室顶、大灯）。
        walls.append(kit.box(f"Rail_{'R' if side > 0 else 'L'}",
                             (side * (g["bed_w"] * 0.5 - wt * 0.5), g["bed_y"], wtop + 0.025),
                             (wt + 0.05, g["bed_depth"], 0.05), "TruckBody", smooth=False))
    walls.append(kit.box("Tailgate", (0.0, g["bed_y1"] + wt * 0.5, wall_cz),
                         (g["bed_w"], wt, wall_h), "TruckWood", smooth=False))
    walls.append(kit.box("Rail_Tail", (0.0, g["bed_y1"] + wt * 0.5, wtop + 0.025),
                         (g["bed_w"], wt + 0.05, 0.05), "TruckBody", smooth=False))
    walls.append(kit.box("Headboard", (0.0, g["bed_y0"] - wt * 0.5, wall_cz),
                         (g["bed_w"], wt, wall_h), "TruckWoodDk", smooth=False))

    # ---------------------------------------------------------- 招牌牌匾（车头方向）
    # ★★ 三版才做对的一处，教训留在这里 ★★
    #   需求："远处一眼能看出是商店车来了"。我的第一反应是给货斗加个遮阳棚 ——
    #   结果 **60° 俯视下整个货斗被棚顶盖住**，果子一颗都看不见，
    #   而"载满各种水果"恰恰是这台车唯一的视觉卖点（需求原话）。
    #   第二版把棚缩到只盖斗尾 0.80m，仍然从上方压掉一大片果子。
    #   换算法看：俯视 60° 时，高度 h 的一块板会在水平方向投影出 h/tan60° 的遮挡带，
    #   棚顶在 z=2.30、果子在 z≈1.3，遮挡带宽 (2.30-1.30)/1.732 ≈ 0.58m，
    #   而货斗只有 2.0m 长 —— 一块 0.8m 的棚就吃掉近 30% 的可见面积。
    #   最终把"商店"的标识从**货斗上方**移到**驾驶室上方**：一块立式牌匾 + 两根柱子。
    #   它在游戏相机角度下一点货斗都不挡（驾驶室本来就比货斗高），
    #   但侧视/正视里依然明确是"一块招牌"，商店的语义保住了。
    awning.append(kit.box("SignBoard", (0.0, g["sign_y"], g["sign_z"]),
                          (g["sign_w"], g["sign_depth"], g["sign_h"]), "TruckBody",
                          rot=(D(g["sign_pitch"]), 0.0, 0.0), smooth=False))
    # 牌匾下沿的深色压条：让"这是一块牌子"有厚度感，不是一片白纸
    awning.append(kit.box("SignRail", (0.0, g["sign_y"], g["sign_z"] - 0.10),
                          (g["sign_w"] * 0.98, g["sign_depth"] * 0.90, 0.05),
                          "TruckWoodDk", smooth=False))
    # 两根柱：从驾驶室顶（1.59）升到牌匾。x=±0.82 落在车宽内(±1.09)，
    # y=-1.25 在挡风玻璃(-1.22)之前，所以不会插进玻璃里。
    for side in (-1, 1):
        awn_posts.append(kit.cyl(f"SignPost_{'R' if side > 0 else 'L'}",
                                 (side * g["signpost_x"], g["signpost_y"],
                                  g["signpost_z"]),
                                 g["signpost_r"], g["signpost_h"], "TruckWoodDk", verts=8))
    # 牌匾横梁（把两根柱连起来）
    awn_posts.append(kit.box("SignBeam", (0.0, g["signpost_y"], 2.36),
                             (1.80, 0.07, 0.07), "TruckWoodDk", smooth=False))
    return body, bed, walls, awning, awn_posts


# ================================================================ 组装木偶
def build_puppet():
    """搭出木偶层级。返回 (objs, rest)。

    objs 的键 = 分组名，值 = 该分组的网格对象（特殊键：_root / _bob / Wheels）。
    rest      = 每个对象的**静止本地变换**（复位必须用 rest，不能写 0，
                见 gen_pig.pose 的说明）。
    """
    kit.reset()
    body, bed, walls, awning, awn_posts = build_body_parts()
    fruits = build_fruit_pile()

    root = empty(f"Root_{ID}", (0.0, 0.0, 0.0))
    bob = empty(f"Bob_{ID}", (0.0, 0.0, 0.0))
    set_parent(bob, root)

    objs = {}
    for name, parts in (("Body", body), ("BedTrunk", bed), ("BedWalls", walls),
                        ("Fruits", fruits), ("Awning", awning),
                        ("AwningPosts", awn_posts)):
        merged = kit.join(parts, f"{name}_{ID}")
        kit.apply_transforms(merged)
        objs[name] = merged

    wheels = {}
    for tag, (cx, cy) in (("FL", (-geo("wheel_x"), geo("wheel_fy"))),
                          ("FR", (geo("wheel_x"), geo("wheel_fy"))),
                          ("RL", (-geo("wheel_x"), geo("wheel_ry"))),
                          ("RR", (geo("wheel_x"), geo("wheel_ry")))):
        wheels[tag] = make_wheel(f"Wheel_{tag}_{ID}", cx, cy)
    objs["Wheels"] = wheels

    # 挂关系
    for k in ("Body", "BedTrunk", "BedWalls", "Fruits", "Awning"):
        set_parent(objs[k], bob)
    for w in wheels.values():
        # ★ 轮子挂 Root，不挂 Bob ★ 车身颠的时候轮子不该跟着上下跳
        #   （轮子是贴地的，跳了就像悬空）。这是木偶分工的第二个用意。
        set_parent(w, root)
    set_parent(objs["AwningPosts"], root)

    rest = {"_root": tuple(root.location), "_bob": tuple(bob.location)}
    for k, v in objs.items():
        if k == "Wheels":
            for tag, w in v.items():
                rest["Wheel_" + tag] = (tuple(w.location), tuple(w.rotation_euler))
        else:
            rest[k] = (tuple(v.location), tuple(v.rotation_euler))
    objs["_root"] = root
    objs["_bob"] = bob
    return objs, rest


def mesh_objects(objs):
    """全部网格对象（跳过 _root / _bob 两个空物体）。"""
    out = []
    for k, v in objs.items():
        if k in ("_root", "_bob"):
            continue
        if isinstance(v, dict):
            out += list(v.values())
        else:
            out.append(v)
    return out


# ================================================================ 静止模型
def build_static():
    objs, _rest = build_puppet()
    bpy.context.scene.frame_set(1)

    pts, tris, mats = [], 0, set()
    # 逐组报面数 —— 超预算时只有这张表能告诉你该砍谁（第一版 3088 超了 588，
    # 光看总数不知道该动水果还是动轮子；表一出来就看出是水果段数和轮子辐条）。
    per_group = {}
    for o in mesh_objects(objs):
        mw = o.matrix_world
        pts += [mw @ v.co for v in o.data.vertices]
        t = kit.tri_count(o)
        tris += t
        mats |= {m.name for m in o.data.materials if m}
        grp = o.name.split("_")[0] if not o.name.startswith("Wheel_") else "Wheel"
        per_group[grp] = per_group.get(grp, 0) + t
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    zmin = min(zs)

    print(f"[TRUCK] dims(x宽, y长, z高) = {tuple(round(v, 3) for v in dims)}")
    print(f"[TRUCK] zmin = {zmin:.4f}  zmax = {max(zs):.4f}  三角面 = {tris} "
          f"(预算 {TRI_BUDGET})")
    print(f"[TRUCK] 材质 {len(mats)}: {sorted(mats)}")
    print("[TRUCK] 分组面数: " + "  ".join(
        f"{k}={v}" for k, v in sorted(per_group.items(), key=lambda kv: -kv[1])))

    problems = []
    if not (TARGET_LEN[0] <= dims[1] <= TARGET_LEN[1]):
        problems.append(f"长 {dims[1]:.3f} 不在 {TARGET_LEN}")
    if not (TARGET_WID[0] <= dims[0] <= TARGET_WID[1]):
        problems.append(f"宽 {dims[0]:.3f} 不在 {TARGET_WID}")
    if not (TARGET_HGT[0] <= dims[2] <= TARGET_HGT[1]):
        problems.append(f"高 {dims[2]:.3f} 不在 {TARGET_HGT}")
    if abs(zmin) > 0.005:
        problems.append(f"车底不在 z=0（zmin={zmin:.4f}）")
    if tris >= TRI_BUDGET:
        problems.append(f"面数超预算：{tris} ≥ {TRI_BUDGET}")
    bad = [m for m in mats if m not in kit.PALETTE]
    if bad:
        problems.append(f"非调色板材质名：{bad}")
    # 朝向自检：车头（-Y）那一头的部件不能在 +Y 侧
    for o in mesh_objects(objs):
        if o.name.startswith(("Hood", "Grille", "Bumper_Front", "Headlight", "Nose")):
            ymin = min((o.matrix_world @ v.co).y for v in o.data.vertices)
            if ymin > NOSE_Y + 0.3:
                problems.append(f"{o.name} 偏到车尾侧了（ymin={ymin:.2f}）→ 朝向写反")
    for p in problems:
        print(f"[TRUCK] ★静态自检未过：{p}★")
    if not problems:
        print("[TRUCK] 静态自检全部通过")

    path = os.path.join(PROP_DIR, ID + ".fbx")
    kit.ensure_dir(path)
    # ★ bake_yup 保持 False：轴向只在导出器里处理一次 —— 不要再自己烘 Y-up ★
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        object_types={'MESH', 'EMPTY'},
        mesh_smooth_type='FACE',
        path_mode='AUTO',
        embed_textures=False,
        bake_anim=False,
        add_leaf_bones=False,
        axis_forward='-Z',          # 车头 -Y → Unity +Z（WorldView.cs:999 的约定）
        axis_up='Y',                # Blender Z-up → Unity Y-up
        use_space_transform=True,
    )
    drop_meta(path)
    print(f"[TRUCK] 静态 -> {path}")
    return dict(tris=tris, dims=tuple(round(v, 4) for v in dims),
                zmin=zmin, zmax=max(zs), mats=sorted(mats), path=path,
                problems=problems)


# ================================================================ 动画
# 帧数直接决定频率。24fps：
#   drive 36 帧 = 1.50s，里面正好装 **3 圈**轮子（= 0.75 转/s = 2.00 Hz 角频率）
#                + **1 个**车身起伏周期（0.667 Hz）+ **2 个**果堆晃动周期（1.33 Hz）
#   → 3 个频率互不相同、且 3:1 / 2:1 都是整数比，所以：
#       · 循环接回去时角度连续（不会倒跳）
#       · 轮子和车身不会同频（同频会看起来像"轮子原地抖"）
DRIVE_FRAMES = 36
DRIVE_REVS = 3.0
ENTER_FRAMES = 60
EXIT_FRAMES = 60
ENTER_REVS = 6.0
EXIT_REVS = 7.0

ANIMS = {
    # name:    (帧数, 是否循环, 轮子转数, 说明)
    "drive": (DRIVE_FRAMES, True,  DRIVE_REVS, "行驶循环：轮子 3 圈 + 车身颠簸 1 周期 + 果堆轻晃 2 周期"),
    "enter": (ENTER_FRAMES, False, ENTER_REVS, "入场：从车头前方 3.6m 冲进停车位，前倾 → 刹停点头"),
    "exit":  (EXIT_FRAMES,  False, EXIT_REVS,  "离场：起步加速驶离 3.6m，抬头 → 轻微侧倾"),
}


def smooth(k):
    """3k²-2k³ 缓入缓出。纯函数、无随机。"""
    k = max(0.0, min(1.0, k))
    return k * k * (3.0 - 2.0 * k)


def ease_out_cubic(k):
    k = max(0.0, min(1.0, k))
    return 1.0 - (1.0 - k) ** 3


def pose_truck(anim, t, objs, rest):
    """相位 t∈[0,1) → 各部件姿态。纯函数，无随机（同 t 必得同姿态）。

    ★ 复位用 rest，不能用 0 ★
    set_parent(keep_transform=True) 之后本地位置是**算出来的非零值**，
    写 0 等于把每块肉搬到父节点原点上（gen_pig 踩过：躯干整体下沉一个腿高）。
    """
    tau = 2.0 * math.pi

    # ---------- 复位 ----------
    # ★ 解包前必须先跳过下划线键 ★
    #   rest 里 "_root" / "_bob" 存的是**纯 location 三元组**（两个锚点都没有旋转），
    #   其余键存的是 (location, rotation_euler) 二元组 —— 两种形状**不一样**。
    #   第一版把 `if k.startswith("_")` 写在解包**之后**，于是 "_root" 的三个分量
    #   被当成 (loc, rot) 去解，报 "too many values to unpack (expected 2)"。
    #   第二版改了守卫位置，却把取用端写成 rest["_bob"][0] —— 那取到的是一个 float，
    #   于是 assignment 报 TypeError。两端都必须是"整个三元组"。
    for k, v in rest.items():
        if k.startswith("_"):
            continue
        loc, rot = v
        o = objs["Wheels"][k[6:]] if k.startswith("Wheel_") else objs.get(k)
        if o is None:
            continue
        o.location = loc
        o.rotation_euler = rot
    objs["_bob"].location = rest["_bob"]                 # ← 整个三元组
    objs["_bob"].rotation_euler = (0.0, 0.0, 0.0)
    # ★ Root 干脆不碰 ★ —— 它必须恒为 rest（= 世界原点），验证器会断言这一点
    objs["_root"].location = rest["_root"]               # ← 整个三元组

    bl = rest["_bob"]
    fl = rest["Fruits"][0]

    def spin_wheels(revs):
        """轮子绕自身 X 轴转 revs 圈。

        逐帧直接写**连续弧度量**（不取模），所以回读出来是一条单调递增的直线：
          · 不用操心 Euler 的 0/2π 环绕跳变
          · 角度 = revs·360·t 是直线，"在转"一眼可证（验证器量的是这条线的斜率）
        """
        ang = tau * revs * t
        for w in objs["Wheels"].values():
            w.rotation_euler = (ang, 0.0, 0.0)

    if anim == "drive":
        bob = objs["_bob"]
        spin_wheels(DRIVE_REVS)
        # 车身颠簸：**1 个周期**（比轮子慢 3 倍）
        bob.location = (bl[0], bl[1], bl[2] + 0.021 * math.sin(tau * t))
        bob.rotation_euler = (D(1.35) * math.sin(tau * t + 0.7),
                              D(1.05) * math.sin(tau * t * 2.0),
                              0.0)
        # 果堆轻晃：**2 个周期**，相位再错开一点 —— 三个频率互不相同
        fr = objs["Fruits"]
        fr.location = (fl[0] + 0.010 * math.sin(tau * t * 2.0 + 1.1),
                       fl[1],
                       fl[2] + 0.014 * math.sin(tau * t * 2.0 + 1.9))
        fr.rotation_euler = (0.0, 0.0, D(1.4) * math.sin(tau * t * 2.0))

    elif anim == "enter":
        bob = objs["_bob"]
        # 从停车位**前方** 3.6m 开进来：+Y 位移 = 相对停车位靠后 3.6m 处起步
        k = smooth(t)
        spin_wheels(ENTER_REVS * t)
        bob.location = (bl[0], bl[1] + 3.60 * (1.0 - k),
                        bl[2] + 0.024 * math.sin(tau * 4.0 * t))
        bob.rotation_euler = (D(4.2) * (1.0 - k) + D(2.6) * math.sin(tau * 3.0 * t) * (1.0 - k),
                              0.0, 0.0)
        # 加速度把果子往后压 → 末段回弹
        fr = objs["Fruits"]
        fr.location = (fl[0], fl[1] - 0.035 * math.sin(math.pi * min(1.0, t * 1.35)),
                       fl[2] + 0.010 * math.sin(tau * 5.0 * t))

    elif anim == "exit":
        bob = objs["_bob"]
        # 起步驶离：位移走 ease_out_cubic（先慢后快 = 加速）
        k = ease_out_cubic(t)
        spin_wheels(EXIT_REVS * t)
        bob.location = (bl[0], bl[1] - 3.60 * k,
                        bl[2] + 0.020 * math.sin(tau * 4.0 * t) * k)
        bob.rotation_euler = (D(-3.6) * k + D(1.8) * math.sin(tau * 3.0 * t) * k,
                              0.0,
                              D(1.6) * math.sin(tau * 2.0 * t) * k)
        fr = objs["Fruits"]
        fr.location = (fl[0], fl[1] + 0.045 * math.sin(math.pi * min(1.0, t * 1.35)),
                       fl[2] + 0.012 * math.sin(tau * 5.0 * t))

    else:
        raise ValueError(anim)


def export_anim(anim):
    """导出一条动画。**每次重新搭一次木偶**（照 gen_pig / gen_player_v2 的做法）。

    ★ 不要复用同一个场景 ★ Blender 5 的 action 是挂在"槽位"上的，换 action 之后
    槽位还停在上一条绑定上，实测后果是一个 FBX 里同时写进两条 take。
    """
    frames, loop, _revs, _note = ANIMS[anim]
    objs, rest = build_puppet()
    scene = bpy.context.scene
    total = frames + 1 if loop else frames
    scene.frame_start = 1
    scene.frame_end = total

    animated = [v for k, v in objs.items()
                if k not in ("_root",) and not isinstance(v, dict)]
    animated += list(objs["Wheels"].values())
    animated.append(objs["_bob"])

    shared = bpy.data.actions.new(f"{ID}_{anim}")
    # 全部对象**共用同一条 action**（含 Root）—— 否则导出器会为每个对象写一个 take，
    # Unity 侧于是得到 7~10 条片段而不是 1 条完整动画（gen_pig 记的坑）。
    for o in animated + [objs["_root"]]:
        o.animation_data_create()
        o.animation_data.action = shared

    for f in range(1, total + 1):
        if loop:
            t = (f - 1) / float(frames)
            if f == total:
                t = 0.0            # 收尾帧 = 首帧，loopTime 才无缝
        else:
            t = (f - 1) / float(max(1, total - 1))
        # ★ 先 frame_set，再摆姿态，最后打关键帧 —— 顺序不能换 ★
        scene.frame_set(f)
        pose_truck(anim, t, objs, rest)
        for o in animated:
            o.keyframe_insert(data_path="location")
            o.keyframe_insert(data_path="rotation_euler")
        # ★ Root 一个关键帧都不打 ★
        #   打了就会在 Unity 里被当成 root transform 曲线应用到**整台车的位置**上，
        #   和代码的路点位移叠加 —— 正是需求里警告的"车会飞出去"。

    path = os.path.join(ANIM_DIR, f"{ID}_{anim}.fbx")
    kit.ensure_dir(path)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        object_types={'MESH', 'EMPTY'},
        mesh_smooth_type='FACE',
        path_mode='AUTO',
        embed_textures=False,
        bake_anim=True,
        bake_anim_use_all_bones=False,
        # 实测：本项为 False 时导出器**一条曲线都不写**（gen_pig 记的坑）；
        # "全部对象共用一条 action" 保证每个 FBX 里只有一条 take。
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=1.0,     # 0 = 不简化，会产出巨大文件
        add_leaf_bones=False,
        axis_forward='-Z',                 # 轴向只在这里处理一次
        axis_up='Y',
        use_space_transform=True,
    )
    drop_meta(path)
    kind = "LOOP" if loop else "ONCE"
    print(f"[TRUCK] {anim}: {total} 帧 ({total / FPS:.2f}s) {kind} -> {path}")
    return path


def build_anims(only=None):
    out = []
    for anim in ANIMS:
        if only and anim not in only:
            continue
        out.append(export_anim(anim))
    return out


# ================================================================ 入口
def _args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


USAGE = """用法:
  blender -b --factory-startup -P gen_shop_truck.py -- all
  blender -b --factory-startup -P gen_shop_truck.py -- static
  blender -b --factory-startup -P gen_shop_truck.py -- anim [名字...]
  blender -b --factory-startup -P gen_shop_truck.py -- verify
  blender -b --factory-startup -P gen_shop_truck.py -- preview
动画名: """ + ", ".join(ANIMS)


def main():
    a = _args()
    if not a:
        print(USAGE)
        return
    cmd = a[0]
    if cmd == "static":
        build_static()
    elif cmd == "anim":
        build_anims(a[1:] or None)
    elif cmd == "all":
        build_static()
        build_anims()
    elif cmd in ("verify", "preview"):
        import verify_shop_truck as vt
        if cmd == "verify":
            vt.verify()
        else:
            vt.render_previews()
    else:
        print(USAGE)


if __name__ == "__main__":
    main()
