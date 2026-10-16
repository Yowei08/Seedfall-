# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 玩家 **Q 版**重做生成器（模型 + 7 条动画 + 三把武器）
================================================================================
需求原话：
  "玩家模型也重做，包括玩家动画资源，使用原本的 **Q 版风格**"
  Q 版 = 头大身小、矮胖、圆润、高饱和：
    头（含帽）占整体高度 40~50%、四肢短粗圆头收尾、大圆弧无尖角、
    高饱和大色块（草帽的黄 / 围巾的红 / 衣服的蓝）、剪影简洁。

────────────────────────────────────────────────────────────────
一、这一版与 gen_player_v2 的关系
────────────────────────────────────────────────────────────────
`gen_player_v2.py` 是上一版（正常人体比例：头只有总高的 18.1%）。
**它一个字都没改** —— 里面的踩坑注释（骨头父子绑定会静默平移一个骨长、
四元数回读、轴向只处理一次…）依然是本机最可靠的参考，本文件沿用同一套做法，
只把「比例表 + 几何 + 姿态幅度」换成 Q 版，并新增两条此前没有的验收：
  · **Q 版比例**：头（含帽）÷ 总高 ≥ 0.40
  · **帽檐比**：帽檐半径 ÷ 头半宽 ≤ 1.1（60° 俯视下不许糊住身体）

────────────────────────────────────────────────────────────────
二、为什么 Q 版比正常比例**更容易**在俯视下糊成一坨（本版的核心矛盾）
────────────────────────────────────────────────────────────────
游戏相机（`GameBootstrap.UpdateCamera`）：
    offset = (0, sin(pitch), -cos(pitch)) × dist，**pitch = 60°**（Defs.CameraDef 默认值）。
也就是"从角色正前方、抬高 60° 往下看"。这种视角下角色的屏幕纵轴是
    screen_up = 0.866·y + 0.5·z      （Blender 系；人物朝 -Y，相机在 -Y 一侧）
一个球心在 (0,0,zc)、半轴 (rx,ry,rz) 的头，其**屏幕投影底边**是
    bottom = 0.5·zc − (0.866·ry + 0.5·rz)
底边越低，被它挡住的躯干就越多。所以 Q 版大头的正确做法不是"把头做成大球"，
而是：**纵向拉高（rz 大，撑住 40% 的比例）、横向与前后压扁（rx/ry 小，少挡身体）**：
    rz = 0.360  → 头(含帽) 0.7585 ÷ 1.8385 = **41.3%** ✓
    rx = 0.3312 → 比肩（±0.355）和手（±0.474）都窄 ⇒ 肩/手臂从头顶两侧露出来 ✓
    ry = 0.3096 → 屏幕底边不深，躯干在"下巴"以下仍有一条可见带 ✓
帽檐再收到 head_rx×1.0417 = 0.345（≤1.1 的硬要求），于是帽檐在**任何高度上都不比头宽**，
"帽檐投影盖住全身"这条老故障在本版由**几何**排除，不是靠调参碰运气。

★ 这条判据不靠肉眼：`verify_player_q.py` 会用**相机射线**逐件量"该件有多少比例的
  采样点是被别的件挡住的"，并报出 头/肩/手臂/躯干/腿 在屏幕上的纵向次序。

────────────────────────────────────────────────────────────────
三、Unity 侧的硬契约（一个都不能变）
────────────────────────────────────────────────────────────────
  · 19 根骨骼**名字完全一致**（`WorldView.AttachTools` 按名字找 `Bone_Hand_R` 挂武器）
  · 身高 1.6~1.9m，且**与旧的 1.8385m 基本一致**（游戏侧 `SetScaleFactor` 是固定倍率
    CharScale 2.6 × PlayerScale 0.627 = 1.6302，改了身高就改了世界尺寸）
  · `zmin = 0`（脚底贴地）、高度在 Z 轴、`export_fbx(..., bake_yup=False)` 的轴向约定
  · 面数 < 2000
  · **不做蒙皮**：网格件用 `parent_set(type='BONE')` 挂在骨骼上（与旧模型一致，
    `ModelView.MeshRawHeight` 走的是 MeshFilter）。
  · **attack / swap 的腿骨（含 Hips）曲线恒为 0** —— 游戏侧用两段采样做"边跑边攻击"：
        moveClip.SampleAnimation(...)   然后   attackClip.SampleAnimation(...)
    腿骨为 0 是"下半身不被覆盖"的机械保证。**Hips 也算腿**（它是腿的父节点）。

★ 本版比 v2 多做的一件事：attack/swap **不写 Hips 的 location 曲线**。
  v2 写了（恒 0），于是两段采样时 attack 会把 move 的髋部起伏**按 0 覆盖掉**——
  角色跑动中攻击时髋会"瞬移回静止"。不写这条曲线，move 的起伏就能保留下来。
  角度照写（恒 0），"腿骨角度 = 0"这条硬判据不受影响。

────────────────────────────────────────────────────────────────
四、产出
────────────────────────────────────────────────────────────────
  Assets/Resources/Art/Characters/player.fbx                  Q 版模型（19 骨骼 + 骨骼父子绑定）
  Assets/Resources/Art/Tools/tool_{hoe,can,fert}.fbx         三把武器（GRIP 在原点、单位旋转）
  Assets/Resources/Art/Anim/player_{idle,walk,hit,swap}.fbx  循环 / 一次性
  Assets/Resources/Art/Anim/player_attack_{hoe,can,fert}.fbx 三把各一套（只上半身）
  Docs/captures/player_qstyle_top60.png                       **主验收图**：60° 俯视
  Docs/captures/player_qstyle_*.png                          正面 / 对照图

用法：
    blender -b --factory-startup -P gen_player_q.py -- all
    blender -b --factory-startup -P gen_player_q.py -- static
    blender -b --factory-startup -P gen_player_q.py -- tools
    blender -b --factory-startup -P gen_player_q.py -- anim [名字...]
    blender -b --factory-startup -P gen_player_q.py -- verify
    blender -b --factory-startup -P gen_player_q.py -- preview

★ 上一版踩过、这里原样保留的坑（别再踩）★
  ① `primitive_cylinder_add(depth=)` 收**全高**，不是半径倍数。
  ② `parent_set(type='BONE')` 之后**绝不能**再写 `obj.parent_bone`：
     运算符的 keep_transform 按"骨骼尾部"算 matrix_parent_inverse，事后改名字会让
     每件沿骨长整体平移一个骨长，而缩放仍是 1.0、不报错（旧模型身高 1.80 → 2.89）。
     正确做法是让运算符写，然后**断言**它写对了。
  ③ 回读骨骼角度必须**先看 rotation_mode**：FBX 导出后是四元数，
     `rotation_euler` 恒为 0，照着 euler 读会得到"动画没导出去"的假故障。
  ④ `mode_set(mode='POSE')` 需要 armature 是**活动对象**。
  ⑤ 顺序不能换：先 `frame_set` → 再摆姿态 → 再 `keyframe_insert`。
  ⑥ 一个 FBX 一条 take：**每条动画重新搭一次角色**（Blender 5 的 action 挂在槽位上）。
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
CHAR_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Characters")
TOOL_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Tools")
ANIM_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Anim")
PREVIEW_DIR = os.path.join(HERE, "preview")
CAPTURE_DIR = os.path.join(PROJECT, "Docs", "captures")

# ---------------------------------------------------------------- 调色板
# ★ 只**新增**键，绝不改既有键 ★
#   既有键（Skin/Cloth/Hat/Scarf/…）被 4 个角色 + 16 株植物 + 环境共用，
#   改一个值会连带改掉已经验收过的东西。Q 版要"高饱和大色块"，
#   所以另开一组 Q* 键：草帽的黄、围巾的红、衣服的蓝都按需求点名来。
kit.PALETTE.update({
    "QSkin":    "#FBDDB0",   # 暖肤色（比 Skin 更饱和，Q 版要气色）
    "QCloth":   "#3E86E0",   # **衣服主色：蓝**
    "QClothDk": "#2A5CA8",   # 衣服暗部（袖口 / 裤）
    "QScarf":   "#E8453A",   # **围巾：红**
    "QHat":     "#F5C63C",   # **草帽：黄**
    "QHatDk":   "#D9A124",   # 帽带（暗一档的黄）
    "QBoot":    "#7A4E2B",   # 靴子棕
    "QInk":     "#2A231C",   # 眼/嘴
    "QSprout":  "#63C63F",   # 帽顶小芽绿
    # 武器（比 Tools 那组更饱和，握把加粗以配 Q 版的大手）
    "QHaft":    "#B07C3E",
    "QMetal":   "#C8CDD2",
    "QCan":     "#4FA8D8",
    "QBurlap":  "#C99A5E",
    "QToolDirt": "#7A5A38",
})

# ---------------------------------------------------------------- 规格
TARGET_H = 1.8385        # = 旧模型的 1.8385m，**刻意一模一样**（世界尺寸不变）
TRI_BUDGET = 2000        # 需求：面数 < 2000
FPS = 24.0

# Q 版两项比例（验收用）
HEAD_RATIO_MIN = 0.40    # 头（含帽）÷ 总高
BRIM_RATIO_MAX = 1.10    # 帽檐半径 ÷ 头半宽

# ================================================================ 骨架
# 单位米，脚底 z=0，正面朝 -Y（沿用全工程的约定）。
# ★ 19 根，名字与旧版**逐字相同** ★ 只有坐标是 Q 版的（躯干压短、头位抬高）。
#   旧版：Hips 0.92 / Chest 1.18 / Head 1.48→1.80；本版：Hips 0.62 / Chest 0.86 / Head 1.08→1.80
BONES = [
    # name,                head,                 tail,                parent
    ("Bone_Hips",        (0.000, 0.000, 0.620), (0.000, 0.000, 0.720), None),
    ("Bone_Spine",       (0.000, 0.000, 0.720), (0.000, 0.000, 0.860), "Bone_Hips"),
    ("Bone_Chest",       (0.000, 0.000, 0.860), (0.000, 0.000, 1.020), "Bone_Spine"),
    ("Bone_Neck",        (0.000, 0.000, 1.020), (0.000, 0.000, 1.080), "Bone_Chest"),
    # ★ 头骨从 1.08 一直到 1.80 —— 它撑起"头占 40%"这条要求 ★
    ("Bone_Head",        (0.000, 0.000, 1.080), (0.000, 0.000, 1.800), "Bone_Neck"),
    ("Bone_Shoulder_L",  (0.085, 0.000, 1.000), (0.285, 0.000, 1.005), "Bone_Chest"),
    ("Bone_Shoulder_R",  (-0.085, 0.000, 1.000), (-0.285, 0.000, 1.005), "Bone_Chest"),
    ("Bone_UpperArm_L",  (0.285, 0.000, 1.005), (0.335, 0.000, 0.815), "Bone_Shoulder_L"),
    ("Bone_UpperArm_R",  (-0.285, 0.000, 1.005), (-0.335, 0.000, 0.815), "Bone_Shoulder_R"),
    ("Bone_LowerArm_L",  (0.335, 0.000, 0.815), (0.385, 0.000, 0.645), "Bone_UpperArm_L"),
    ("Bone_LowerArm_R",  (-0.335, 0.000, 0.815), (-0.385, 0.000, 0.645), "Bone_UpperArm_R"),
    # ★ 手骨的方向刻意**与上一版逐分量相同**（+0.03, 0, −0.09 → 掌心轴 (0.316,0,−0.949)）★
    #   Unity 侧 `AttachTools` 对三把武器用同一个 `Quaternion.Euler(0,180,0)`，
    #   那个值是按上一版的掌心轴向试出来的。Q 版手更矮更外（位置变了没关系），
    #   只要**方向**不变，那个 180° 就依然成立，武器不用在引擎里重试角度。
    ("Bone_Hand_L",      (0.385, 0.000, 0.645), (0.415, 0.000, 0.555), "Bone_LowerArm_L"),
    ("Bone_Hand_R",      (-0.385, 0.000, 0.645), (-0.415, 0.000, 0.555), "Bone_LowerArm_R"),
    ("Bone_Thigh_L",     (0.130, 0.000, 0.620), (0.136, 0.000, 0.350), "Bone_Hips"),
    ("Bone_Thigh_R",     (-0.130, 0.000, 0.620), (-0.136, 0.000, 0.350), "Bone_Hips"),
    ("Bone_LowerLeg_L",  (0.136, 0.000, 0.350), (0.140, 0.000, 0.140), "Bone_Thigh_L"),
    ("Bone_LowerLeg_R",  (-0.136, 0.000, 0.350), (-0.140, 0.000, 0.140), "Bone_Thigh_R"),
    ("Bone_Foot_L",      (0.140, 0.000, 0.140), (0.140, -0.150, 0.058), "Bone_LowerLeg_L"),
    ("Bone_Foot_R",      (-0.140, 0.000, 0.140), (-0.140, -0.150, 0.058), "Bone_LowerLeg_R"),
]

BONE_NAME = {b[0]: b for b in BONES}

#: 需求点名的 19 根骨架名（Unity 侧按名字找 Bone_Hand_R 挂武器）
REQUIRED_BONES = ["Bone_Hips", "Bone_Spine", "Bone_Chest", "Bone_Neck", "Bone_Head",
                  "Bone_Shoulder_L", "Bone_Shoulder_R",
                  "Bone_UpperArm_L", "Bone_UpperArm_R",
                  "Bone_LowerArm_L", "Bone_LowerArm_R",
                  "Bone_Hand_L", "Bone_Hand_R",
                  "Bone_Thigh_L", "Bone_Thigh_R",
                  "Bone_LowerLeg_L", "Bone_LowerLeg_R",
                  "Bone_Foot_L", "Bone_Foot_R"]

#: **下半身骨骼**：attack / swap 里必须恒 0（含 Hips —— 它是腿的父节点）
LOWER_BODY = ["Bone_Hips", "Bone_Thigh_L", "Bone_Thigh_R",
              "Bone_LowerLeg_L", "Bone_LowerLeg_R", "Bone_Foot_L", "Bone_Foot_R"]
#: **上半身骨骼**：attack / swap 允许打关键帧
UPPER_BODY = ["Bone_Spine", "Bone_Chest", "Bone_Neck", "Bone_Head",
              "Bone_Shoulder_L", "Bone_Shoulder_R",
              "Bone_UpperArm_L", "Bone_UpperArm_R",
              "Bone_LowerArm_L", "Bone_LowerArm_R",
              "Bone_Hand_L", "Bone_Hand_R"]

# ---------------------------------------------------------------- 几何（米）
# 全部对着上面的骨架写。Q 版的三个要点都在这张表里：
#   · 头：rz 0.360（撑 40% 比例）/ rx 0.3312、ry 0.3096（少挡身体）
#   · 帽檐：0.345 = 头半宽 × 1.0417（≤1.1 的硬要求）
#   · 四肢：短粗 + 圆头收尾（手/脚都是球，没有尖角）
G = dict(
    # 躯干：蛋形（髋宽 → 腰略收 → 胸略宽 → 肩披最宽）
    hips_c=(0.0, 0.0, 0.630), hips_r=0.215, hips_s=(1.10, 0.86, 0.64),
    belly_c=(0.0, 0.0, 0.780), belly_r=0.205, belly_s=(1.05, 0.84, 0.62),
    chest_c=(0.0, 0.0, 0.920), chest_r=0.230, chest_s=(1.06, 0.80, 0.72),
    mantle_c=(0.0, 0.0, 1.005), mantle_r=0.210, mantle_s=(1.42, 0.95, 0.30),
    # 颈 / 红围巾。
    # ★ 围巾从 z1.048 抬到 1.085、半径 0.140 → 0.155 ★
    #   第一版围巾被**肩披**（z0.943~1.068）从两侧包住，渲染出来只剩脖子前一条红线 ——
    #   而需求点名"围巾的红"要是**大色块**。抬高到肩披之上、加粗一圈之后，
    #   它变成一道套在颈根的红领子，正面/侧面都能读出来。
    #   半径 0.164 仍远小于头半宽 0.3312 与肩 0.405 ⇒ 不会给 60° 俯视增加任何遮挡。
    neck_c=(0.0, 0.0, 1.062), neck_r=0.078, neck_h=0.130,
    scarf_c=(0.0, 0.0, 1.085), scarf_r=0.155, scarf_s=(1.06, 0.94, 0.38),
    # 头：椭球（高 > 宽 > 深）。
    # ★ 深度 ry 故意压扁（0.74 × 0.36 = 0.266）★
    #   60° 俯视下，头的屏幕投影是一个椭圆，纵向半轴 = √((0.866·ry)² + (0.5·rz)²)。
    #   rz 被"头占 40%"钉死（0.36），能自由收的只有 ry —— ry 从 0.31 收到 0.266，
    #   投影底边就从 0.402 抬到 0.427，**下面那条"看得见身体"的带子宽了 11%**。
    head_c=(0.0, 0.0, 1.440), head_r=0.360, head_s=(0.92, 0.74, 1.00),
    # 草帽两段：帽筒（**压扁的圆顶**，不是球）+ 帽带 + 帽檐，**整体后仰 15°**。
    # ★ 帽筒这一版改了两处，都是"从上面能不能看见身体"逼出来的 ★
    #   ① 心从 1.487 抬到 1.600、z 缩放从 0.99 压到 0.72：
    #      原先是**整颗球**扣在头上，球底一直垂到 z=1.155 —— 正好盖住肩（z≈1.0），
    #      射线审计量出来"肩只有 7.7% 露在外面"。压扁抬高之后球底到 1.375，
    #      肩整个露出来 ✓（现在 59.6%）
    #   ② 半径做到 0.3276（≈ 头半宽 0.3312）：于是**帽檐（0.345）才是横向最宽的那一圈**，
    #      "草帽"的读数才对（帽檐半径 ÷ 头半宽 = 1.0417 ≤ 1.1）。
    #   ③ ★ 后仰 15°（rot x = −15°）★ 这一条是"脸还能不能看见"的关键：
    #      60° 俯视下，一片水平的圆盘会在它正下方投出一条 0.87×半径 宽的死区 ——
    #      水平的帽檐正好把**整张脸**盖住（实测眼睛可见度 0/34，渲染图上一张脸都没有）。
    #      把帽子往后一推（前檐抬起、后檐压下）：帽檐前沿的屏幕高度抬了 0.055，
    #      死区从脸上移开，眼睛重新露出来 ✓ 而且"帽子往后戴"本来就是 Q 版常见画法。
    crown_c=(0.0, 0.0, 1.600), crown_r=0.312, crown_s=(1.05, 1.00, 0.72),
    crown_rot=(D(-15.0), 0.0, 0.0),
    band_c=(0.0, 0.0, 1.512), band_r=0.336, band_h=0.040,
    brim_c=(0.0, 0.0, 1.545), brim_r=0.345, brim_h=0.028,
    brim_rot=(D(-15.0), 0.0, 0.0),
    # 帽顶小芽（**它才是全模型最高点**，见 build_static 的高度断言）。
    # 帽子后仰之后帽顶往后挪了 ~0.058，芽也跟着挪，否则会看起来"飘在帽子前面"。
    sprout_c=(0.0, 0.006, 1.792), sprout_r=0.030, sprout_s=(0.55, 0.55, 1.55),
    # 眼 / 嘴（Q 版要靠脸读"是个小人"）。y 跟着压扁后的头面重算过：
    #   头面在 (x=0.115, z=1.45) 处 y=−0.2417，眼心放 −0.235 才会"略微凸出"而不是陷进去。
    #   z 从 1.478 降到 1.450：屏幕高度每降 0.028 就多 0.014 的余量，
    #   而帽檐死区就在上面 —— 眼睛要往**下**放，不是往大放。
    eye_c=(0.115, -0.235, 1.450), eye_r=0.062, eye_s=(1.00, 0.42, 1.10),
    mouth_c=(0.0, -0.250, 1.335), mouth_r=0.030, mouth_s=(1.50, 0.45, 0.62),
    # 肩 / 手
    shoulder_r=0.120, hand_r=0.092,
    # 腿 / 圆头靴。★ 靴的前伸从 1.30 收到 1.10 ★
    #   靴子越长，"到大腿下缘的那条视线"越容易被它自己挡掉 —— 收短之后
    #   大腿下缘在审计里从 2/16 变成可见（靴口与腿之间空出一条缝）。
    thigh_r=0.115, calf_r=0.100,
    foot_c=(0.140, -0.060, 0.0782), foot_r=0.115, foot_s=(0.92, 1.10, 0.68),
)

#: 帽檐半径 ÷ 头半宽 —— 需求建议 ≤1.1，这是"帽子不许再盖住身体"的核心旋钮
BRIM_RATIO = G["brim_r"] / (G["head_r"] * G["head_s"][0])

# ---------------------------------------------------------------- 动画表
# (总帧数, 是否循环, 说明)。帧数直接决定频率：24fps 下
#   idle 96 帧 = 4.00s = **0.25Hz**（呼吸周期，需求点名的那个数）
#   walk 96 帧 = 4.00s，里面正好装 **6 个步周期（16 帧/步 = 1.50Hz）** 与
#               **1 个呼吸周期（4.00s = 0.25Hz）** —— 6:1，不可能同频。
#   ★ walk 必须是 96 帧而不是 16 帧 ★ 呼吸周期 4s，片段若只有 16 帧（一个步周期），
#     呼吸相位在里面只走 1/6 圈，**循环接回去时呼吸被硬切**（波形不闭合）。
#     让片段长度 = 呼吸周期 = 6 个步周期，两条曲线才同时闭合。
ANIMS = {
    "idle":        (96, True,  "强烈呼吸起伏，胸腔 0.25Hz"),
    "walk":        (96, True,  "步频 1.5Hz 腿反相 + 呼吸 0.25Hz 叠加（6:1）"),
    "hit":         (24, False, "受击后仰（只给姿态，不做缩放/染色）"),
    "swap":        (9,  False, "收起当前 → 抽出下一把，0.375s，只上半身"),
    "attack_hoe":  (36, False, "抡起 → 劈下，大幅有重量"),
    "attack_can":  (36, False, "倾斜壶身倒下，小幅腕部为主"),
    "attack_fert": (36, False, "抓一把往前撒，手臂外摆"),
}

WALK_STEP_FRAMES = 16       # 一个步周期；呼吸周期 = 6 × 它
UPPER_ONLY = ("swap", "attack_hoe", "attack_can", "attack_fert")

BREATH_HZ = 0.25
STEP_HZ = 1.50


# ================================================================ 通用
def drop_meta(path):
    """删掉 Unity 生成的 .fbx.meta —— 同名 FBX 内容变了但 meta 还在时 Unity 可能不重导。"""
    meta = path + ".meta"
    if os.path.exists(meta):
        os.remove(meta)
        print(f"[PLAYERQ] 已删 {os.path.basename(meta)}（强制 Unity 重新导入）")


def limb_tube(name, p0, p1, r0, r1, color, verts=8):
    """在两点之间放一根锥形筒（肢体段）。返回对象。

    为什么不用 kit.cyl：肢体两端**不在同一条竖直线上**，kit.cyl 只能给固定 rot，
    斜向位置得手算欧拉角，改一次骨架就得重算一遍。这里用 (p1-p0) 直接对齐。
    """
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    length = max(1e-4, d.length)
    mid = (p0 + p1) * 0.5
    quat = V((0.0, 0.0, 1.0)).rotation_difference(d.normalized())
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1,
                                    depth=length, location=tuple(mid))
    o = bpy.context.active_object
    o.name = name
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = quat
    o.rotation_mode = 'XYZ'
    kit._apply(o, color, 0.0, 0.62)
    bpy.ops.object.shade_smooth()
    return o


def flatten(obj):
    """把旋转/缩放烘进网格（位置不烘）。"""
    kit.apply_transforms(obj)
    return obj


# ================================================================ 玩家模型零件
def build_player_parts():
    """造出玩家的全部网格件。返回 [(obj, 绑定骨骼名), ...]。

    ★ Q 版的几何纪律 ★
      · 只用球 / 圆柱 / 锥筒 —— 没有 box，没有尖角（需求："造型用大圆弧"）
      · 所有"收尾"都是球（手、脚、肩、膝），不是平口
      · 色块大：头=肤色、帽=黄、围巾=红、衣=蓝、腿=蓝暗、靴=棕
    """
    out = []
    g = G

    def sphere(name, loc, r, color, scale, segs=10, rings=6, bone=None, rot=(0, 0, 0)):
        o = kit.sphere(name, loc, r, color, scale=scale, segs=segs, rings=rings, rot=rot)
        flatten(o)
        out.append((o, bone or "Bone_Chest"))
        return o

    def cyl(name, loc, r, h, color, verts=10, bone=None, rot=(0, 0, 0), scale=(1, 1, 1),
            smooth=True):
        o = kit.cyl(name, loc, r, h, color, verts=verts, rot=rot, scale=scale, smooth=smooth)
        flatten(o)
        out.append((o, bone or "Bone_Chest"))
        return o

    def tube(name, p0, p1, r0, r1, color, bone, verts=8):
        o = limb_tube(name, p0, p1, r0, r1, color, verts=verts)
        flatten(o)
        out.append((o, bone))
        return o

    # ── 躯干：髋 / 腹 / 胸 三段 + 肩披 ──
    # 为什么分三段：需求要"从上往下能读出躯干"。一个胶囊的俯视投影是一个圆，
    # 读不出腰；三段能给出"肩披最宽 → 腰收 → 髋宽"的读数。
    # ★ 分段数按"能不能看见"分配 ★ 头/草帽/胸/肩/靴用 10~12 段（剪影要看它们），
    #   髋/腹/肩披/围巾被上一层盖住大半，用 8 段就够 —— 面数预算只有 2000。
    sphere("Part_Hips", g["hips_c"], g["hips_r"], "QClothDk", g["hips_s"], 8, 5, "Bone_Hips")
    sphere("Part_Belly", g["belly_c"], g["belly_r"], "QCloth", g["belly_s"], 8, 5, "Bone_Spine")
    sphere("Part_Chest", g["chest_c"], g["chest_r"], "QCloth", g["chest_s"], 12, 7, "Bone_Chest")
    # 肩披：俯视时在肩与肩之间压出一条横向亮色，把"肩"这个读数做出来
    sphere("Part_Mantle", g["mantle_c"], g["mantle_r"], "QClothDk", g["mantle_s"],
           8, 5, "Bone_Chest")

    # ── 红围巾 + 颈 ──
    cyl("Part_Neck", g["neck_c"], g["neck_r"], g["neck_h"], "QSkin",
        verts=8, bone="Bone_Neck")
    sphere("Part_Scarf", g["scarf_c"], g["scarf_r"], "QScarf", g["scarf_s"],
           8, 5, "Bone_Chest")

    # ── 头 + 脸 + 草帽 ──
    sphere("Part_Head", g["head_c"], g["head_r"], "QSkin", g["head_s"], 12, 8, "Bone_Head")
    for side, sx in (("L", 1.0), ("R", -1.0)):
        sphere(f"Part_Eye_{side}", (sx * g["eye_c"][0], g["eye_c"][1], g["eye_c"][2]),
               g["eye_r"], "QInk", g["eye_s"], 8, 5, "Bone_Head")
    sphere("Part_Mouth", g["mouth_c"], g["mouth_r"], "QInk", g["mouth_s"], 8, 4, "Bone_Head")
    # 帽 = 帽筒 + 帽带 + 帽檐。★ 帽檐 0.345 = 头半宽 0.3312 × 1.0417（≤1.1）★
    sphere("Part_HatCrown", g["crown_c"], g["crown_r"], "QHat", g["crown_s"],
           12, 7, "Bone_Head", rot=g["crown_rot"])
    cyl("Part_HatBand", g["band_c"], g["band_r"], g["band_h"], "QHatDk",
        verts=16, bone="Bone_Head", smooth=False, rot=g["crown_rot"])
    cyl("Part_HatBrim", g["brim_c"], g["brim_r"], g["brim_h"], "QHat",
        verts=16, bone="Bone_Head", smooth=False, rot=g["brim_rot"])
    sphere("Part_Sprout", g["sprout_c"], g["sprout_r"], "QSprout", g["sprout_s"],
           6, 4, "Bone_Head")

    # ── 肩 + 手臂（A-pose，与骨架一致）──
    for s, side in ((1.0, "L"), (-1.0, "R")):
        sb = BONE_NAME[f"Bone_Shoulder_{side}"]
        ub = BONE_NAME[f"Bone_UpperArm_{side}"]
        lb = BONE_NAME[f"Bone_LowerArm_{side}"]
        hb = BONE_NAME[f"Bone_Hand_{side}"]
        # 肩球：俯视时它就是"肩"这个读数。
        # ★ 放在**肩骨的尾端（关节）**，不是骨的中段 ★
        #   放中段时球心在 x=0.165、外沿只有 0.275 —— 比头半宽 0.3312 还小，
        #   整颗肩球都被头挡住（射线审计：肩 7.7% 可见）。放到关节（x=0.285）后
        #   外沿到 0.405，比头宽出 0.074，从头顶两侧明确露出来 ✓
        sphere(f"Part_Shoulder_{side}", (sb[2][0], 0.0, sb[2][2]),
               g["shoulder_r"], "QCloth", (1.00, 0.92, 0.92), 10, 6,
               f"Bone_Shoulder_{side}")
        tube(f"Part_UpperArm_{side}", ub[1], ub[2], 0.085, 0.078, "QCloth",
             f"Bone_UpperArm_{side}")
        tube(f"Part_LowerArm_{side}", lb[1], lb[2], 0.078, 0.072, "QClothDk",
             f"Bone_LowerArm_{side}")
        hx = (hb[1][0] + hb[2][0]) * 0.5
        hz = (hb[1][2] + hb[2][2]) * 0.5
        # 手：圆球（Q 版的手就是一颗球，没有手指）
        sphere(f"Part_Hand_{side}", (hx, 0.0, hz), g["hand_r"], "QSkin",
               (1.00, 0.92, 1.02), 8, 5, f"Bone_Hand_{side}")

    # ── 腿 + 圆头靴 ──
    for s, side in ((1.0, "L"), (-1.0, "R")):
        tb = BONE_NAME[f"Bone_Thigh_{side}"]
        cb = BONE_NAME[f"Bone_LowerLeg_{side}"]
        tube(f"Part_Thigh_{side}", tb[1], tb[2], g["thigh_r"], g["calf_r"], "QClothDk",
             f"Bone_Thigh_{side}")
        tube(f"Part_Calf_{side}", cb[1], cb[2], g["calf_r"], 0.096, "QClothDk",
             f"Bone_LowerLeg_{side}")
        # 靴：一颗压扁的球，**底极点严格落在 z=0**（球心 z = r × sz，见 G 表）
        sphere(f"Part_Foot_{side}",
               (s * g["foot_c"][0], g["foot_c"][1], g["foot_c"][2]),
               g["foot_r"], "QBoot", g["foot_s"], 10, 6, f"Bone_Foot_{side}")

    # ── 接地阴影（挂在 Hips 上，随角色走）──
    cyl("Part_Shadow", (0.0, 0.0, 0.006), 0.300, 0.011, "QInk", verts=12,
        bone="Bone_Hips", smooth=False)
    return out


# ================================================================ 骨架搭建
def make_armature():
    """按 BONES 表建骨架。返回 armature 对象。"""
    bpy.ops.object.armature_add(enter_editmode=False, location=(0, 0, 0))
    arm = bpy.context.active_object
    arm.name = "Armature"
    arm.data.name = "Armature"
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for b in list(eb):
        eb.remove(b)
    for name, head, tail, parent in BONES:
        b = eb.new(name)
        b.head = head
        b.tail = tail
        b.use_deform = True
    for name, head, tail, parent in BONES:
        if parent:
            eb[name].parent = eb[parent]
            eb[name].use_connect = False      # 不连接：肩/腿都是"悬"在父骨骼上的
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def bone_parent(obj, arm, bone_name):
    """把网格件绑到某根骨骼上（保持世界变换）。

    ★ 两个坑（gen_player_v2 实测过，这里原样保留断言）★
      ① `parent_set(type='BONE')` 需要"当前活动骨骼"，否则抛 `No active bone`；
         活动骨骼只能在 **POSE 模式**下设置。
      ② **绑完之后绝对不要再写 `obj.parent_bone = bone_name`**：运算符内部的
         keep_transform 会同时算出 `matrix_parent_inverse` 与 `parent_bone`，
         而这个 inverse 是按"**骨骼尾部(tail)**"这一帧算的。事后重写会让 inverse
         与新的父帧不一致，表现就是**每件都沿骨骼长度方向整体平移一个骨长**
         （旧模型身高 1.80 → 2.89、肩 z 从 1.41 漂到 2.70），而缩放仍是 1.0、不报错。
         正确做法：让运算符自己写，然后**断言**它写对了。
    """
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    arm.data.bones.active = arm.data.bones[bone_name]
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.parent_set(type='BONE', keep_transform=True)
    bpy.context.view_layer.update()
    if obj.parent_bone != bone_name:
        raise RuntimeError(f"{obj.name}: 期望绑到 {bone_name}，实际绑到 {obj.parent_bone!r}")
    return obj


def assemble_player():
    """搭出玩家（骨架 + 骨骼名 + 骨骼父子绑定）。返回 (arm, meshes)。"""
    kit.reset()
    arm = make_armature()
    parts = build_player_parts()
    for obj, bone in parts:
        bone_parent(obj, arm, bone)
    meshes = [o for o, _ in parts]
    return arm, meshes


# ================================================================ 尺寸度量
def mesh_points(objs):
    """所有网格件的**世界坐标顶点**。

    ★ 不用 `obj.bound_box` ★ 骨头父子绑定过的物体，bound_box 在依赖图求值时机
    不对时会给出过期值；顶点坐标是几何真值，任何时候都可信。
    """
    pts = []
    for o in objs:
        if o.type != 'MESH':
            continue
        mw = o.matrix_world
        for v in o.data.vertices:
            pts.append(mw @ v.co)
    return pts


def extents(pts):
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    return ((min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs)))


def check_assembly(arm, meshes):
    """装配自检：绑定不得改变世界位置。

    绑错的表现是每件沿骨骼长度方向整体平移一个骨长，而缩放仍然是 1.0、不报错。
    这里用"件中心离自身骨骼头的距离"做粗检（硬检在 build_static 里用整体高度做）。
    """
    bad = []
    for o in meshes:
        if not o.parent_bone:
            bad.append(f"{o.name}: 没有绑定骨骼")
            continue
        bone = arm.data.bones[o.parent_bone]
        d = (o.matrix_world.translation - bone.head_local).length
        if d > 1.6:
            bad.append(f"{o.name}→{o.parent_bone} 离骨骼头 {d:.3f}m（疑似漂移）")
    return bad


def head_span(parts_by_bone):
    """「头（含帽）」的 Z 跨度 —— 挂在 Bone_Head 上的全部件。

    为什么按骨骼归属而不是按名字找"Part_Head"：帽檐/帽筒/小芽都在骨头上，
    只量脸球会把帽子漏掉，"头含帽"这个口径就不成立。
    """
    zs = []
    for p in parts_by_bone.get("Bone_Head", []):
        zs.append(p)
    if not zs:
        return None
    return min(z[0] for z in zs), max(z[1] for z in zs)


# ================================================================ 静态模型导出
def build_static():
    arm, meshes = assemble_player()
    tris = sum(kit.tri_count(m) for m in meshes)
    pts = mesh_points(meshes)
    (x0, x1), (y0, y1), (z0, z1) = extents(pts)
    dims = (x1 - x0, y1 - y0, z1 - z0)

    # 每根骨骼上的件的 Z 区间（用来算"头含帽"）
    by_bone = {}
    for o in meshes:
        bb = [o.matrix_world @ V(c) for c in o.bound_box]
        by_bone.setdefault(o.parent_bone, []).append((min(p.z for p in bb),
                                                      max(p.z for p in bb)))
    hs = head_span(by_bone)
    head_h = (hs[1] - hs[0]) if hs else 0.0
    head_ratio = head_h / dims[2] if dims[2] > 0 else 0.0

    print(f"[PLAYERQ] 静态 dims(x宽,y深,z高) = {tuple(round(v, 3) for v in dims)}  "
          f"目标高 {TARGET_H}")
    print(f"[PLAYERQ] 三角面 = {tris}  预算 = {TRI_BUDGET}")
    print(f"[PLAYERQ] Q 版比例：头(含帽) Z[{hs[0]:.3f},{hs[1]:.3f}] 高 {head_h:.3f} "
          f"÷ 总高 {dims[2]:.3f} = {head_ratio:.4f}（目标 ≥ {HEAD_RATIO_MIN}）")
    print(f"[PLAYERQ] 帽檐半径 {G['brim_r']:.3f} ÷ 头半宽 "
          f"{G['head_r'] * G['head_s'][0]:.3f} = {BRIM_RATIO:.4f}"
          f"（目标 ≤ {BRIM_RATIO_MAX}）")
    print(f"[PLAYERQ] zmin = {z0:.4f}  zmax = {z1:.4f}")

    problems = []
    if abs(z0) > 0.005:
        problems.append(f"脚底不在 z=0（zmin={z0:.4f}）")
    if not (1.6 <= dims[2] <= 1.9):
        problems.append(f"高度 {dims[2]:.3f} 不在需求的 1.6~1.9m")
    if abs(dims[2] - TARGET_H) > 0.02:
        problems.append(f"高度 {dims[2]:.3f} 偏离旧版 {TARGET_H}（世界尺寸会变）")
    if tris >= TRI_BUDGET:
        problems.append(f"面数超预算：{tris} ≥ {TRI_BUDGET}")
    if head_ratio < HEAD_RATIO_MIN:
        problems.append(f"Q 版头身比不足：{head_ratio:.3f} < {HEAD_RATIO_MIN}")
    if BRIM_RATIO > BRIM_RATIO_MAX:
        problems.append(f"帽檐比例超了：{BRIM_RATIO:.3f} > {BRIM_RATIO_MAX}")
    problems += check_assembly(arm, meshes)
    for p in problems:
        print(f"[PLAYERQ] ★静态自检未过：{p}★")
    if not problems:
        print("[PLAYERQ] ✔ 静态自检全部通过")

    path = os.path.join(CHAR_DIR, "player.fbx")
    kit.ensure_dir(path)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        # ★ 必须带 ARMATURE，否则骨架根本不在场景里、骨骼全丢 ★
        object_types={'MESH', 'ARMATURE', 'EMPTY'},
        mesh_smooth_type='FACE',
        path_mode='AUTO',
        embed_textures=False,
        bake_anim=False,
        add_leaf_bones=False,
        armature_nodetype='NULL',
        axis_forward='-Z',          # 轴向只处理一次（见 seedfall_kit.export_fbx 注释）
        axis_up='Y',
        use_space_transform=True,
    )
    drop_meta(path)
    print(f"[PLAYERQ] 静态 -> {path}")
    return dict(tris=tris, dims=dims, zmin=z0, zmax=z1, head_h=head_h,
                head_ratio=head_ratio, brim_ratio=BRIM_RATIO, path=path,
                problems=problems)


# ================================================================ 武器
def palm_normal():
    """手掌平面法线（世界空间，已正交化到与竖直手柄垂直）。"""
    hb = BONE_NAME["Bone_Hand_R"]
    palm = (V(hb[2]) - V(hb[1])).normalized()
    n = V((0.0, 0.0, 1.0)).cross(palm)
    return n.normalized() if n.length > 1e-6 else V((1.0, 0.0, 0.0))


def grip_frame():
    """算出 GRIP 该有的朝向。

    握法约定：**手柄（+Z）是主握轴，掌心平面法线（±X）是次参考轴**。
    为什么次参考轴不能用"手掌轴"：手掌轴与竖起来的手柄**几乎平行**（夹角 ~70°），
    拿它当参考等于没参考。真正能把"正握/反握"定下来的是手掌平面法线。

    ★ 这里用的是 gen_player_v2 **逐字相同**的那套基（不是等价改写）★
      GRIP 的朝向会被**烘进网格顶点**（见 build_tool），所以"等价但差 180°"的基
      会让导出的工具在握把轴上翻半圈 —— 锄刃朝后、壶嘴朝后。
      Unity 侧 `AttachTools` 对三把武器用的是**同一个** `Quaternion.Euler(0,180,0)`，
      那个值只能在 v2 的基下成立；换基就等于要求引擎侧也跟着改（而按约定不许动 C#）。
      验证器会量这个差角：`grip_delta_vs_v2()` 报 **0.00°** 才算过。
    """
    hb = BONE_NAME["Bone_Hand_R"]
    palm = (V(hb[2]) - V(hb[1])).normalized()     # 手腕 → 掌心
    haft = V((0.0, 0.0, 1.0))                     # 手柄朝上
    yv = palm - haft * palm.dot(haft)             # 手掌轴正交化（垂直于手柄）
    if yv.length < 1e-6:
        yv = V((0.0, -1.0, 0.0))
    yv.normalize()
    xv = yv.cross(haft).normalized()
    zv = xv.cross(yv).normalized()
    m = mathutils.Matrix(((xv.x, yv.x, zv.x),
                          (xv.y, yv.y, zv.y),
                          (xv.z, yv.z, zv.z)))
    q = m.to_quaternion()
    return q, math.degrees(q.angle)


def grip_axis_error(quat):
    """量 GRIP 的 +X 与手掌平面法线差多少度。**理想是 0**。

    ★ 指标写错会把真结果读成故障，比不量更危险 ★
      gen_player_v2 的第一版这里量的是 +Y 与手掌轴的夹角，恒得 71.6° ——
      那不是握错了，是量错了轴（手掌轴本来就与竖起来的手柄近乎平行）。
    """
    gx = (quat @ V((1.0, 0.0, 0.0))).normalized()
    return math.degrees(math.acos(max(-1.0, min(1.0, abs(palm_normal().dot(gx))))))


def build_tool(kind):
    """造一把武器：GRIP 在原点，工具沿 +Z 伸出。返回 (grip, meshes)。

    Q 版配套调整（需求允许"配套调整武器比例"）：
      · 长度**不动**（锄 1.30m / 壶 0.30m / 袋 0.44m）—— 改了会改世界尺寸
      · 握把**加粗** + 色块**提饱和** —— 配得上 Q 版的大圆手
      · 形状沿用上一版已经调过三遍的那套（刃的朝向、壶嘴角度、提手球距都有注释）
    """
    kit.reset()
    g, g_ang = grip_frame()
    parts = []

    if kind == "hoe":
        parts.append(kit.cyl("Tool_Haft", (0, 0, 0.505), 0.032, 1.25, "QHaft", verts=10))
        parts.append(kit.cyl("Tool_Collar", (0, 0, 1.108), 0.040, 0.058, "QMetal",
                             verts=10, smooth=False))
        # 刃：从柄顶的箍往下、往后翻的一条扁钢，刃口在最下最前。
        # （第一版做成"前上翘的方板"读成桨，第二版刃心放到前方读成"悬空的板"，
        #   第三版才是现在这个：刃心几乎在柄轴 y=-0.030，只让刃口探到 -0.110。）
        parts.append(kit.cone("Tool_Neck", (0.0, -0.028, 1.078), 0.034, 0.052, 0.090,
                              "QMetal", verts=8, rot=(D(62), 0, 0), smooth=False))
        parts.append(kit.box("Tool_Blade", (0.0, -0.030, 0.985), (0.034, 0.160, 0.300),
                             "QMetal", rot=(D(-92), 0, 0), smooth=False))
        parts.append(kit.box("Tool_BladeTip", (0.0, -0.110, 0.828), (0.026, 0.140, 0.075),
                             "QMetal", rot=(D(-104), 0, 0), smooth=False))
        parts.append(kit.cyl("Tool_GripWrap", (0, 0, 0.055), 0.038, 0.200, "QBoot", verts=10))

    elif kind == "can":
        # 水壶：壶身 + 壶嘴 + 提手。总高约 0.30m。
        parts.append(kit.cyl("Tool_Body", (0, 0, -0.070), 0.090, 0.150, "QCan", verts=14))
        parts.append(kit.sphere("Tool_BodyTop", (0, 0, 0.005), 0.090, "QCan",
                                scale=(1.0, 1.0, 0.30), segs=12, rings=6))
        parts.append(kit.sphere("Tool_BodyBot", (0, 0, -0.145), 0.090, "QCan",
                                scale=(1.0, 1.0, 0.26), segs=12, rings=6))
        parts.append(kit.cyl("Tool_Lip", (0, 0, 0.024), 0.094, 0.016, "QMetal",
                             verts=14, smooth=False))
        # 壶嘴：**从壶身下缘往上翘**。轴的换算：绕 X 转 θ，锥的 +Z 轴变成
        # (0, -sinθ, cosθ)；要指向"前上方" (0,-0.78,+0.62) → θ=+51°（不是 -52°）。
        parts.append(kit.cone("Tool_Spout", (0.0, -0.128, -0.048), 0.044, 0.019, 0.200,
                              "QCan", verts=10, rot=(D(51), 0, 0), smooth=False))
        # 花洒盘：与锥筒**严格同轴同点**（锥心 (0,-0.128,-0.048)，半长 0.10 → 末端
        # = 中心 + 0.10×轴 = (0,-0.206,0.014)）。错开就会读成"扣在壶嘴上方的盖子"。
        parts.append(kit.cyl("Tool_SpoutRose", (0.0, -0.206, 0.014), 0.030, 0.018,
                             "QMetal", verts=10, rot=(D(51), 0, 0), smooth=False))
        # 提手：跨过壶顶的拱。★ 球要够密才连得起来 ★
        #   第一版 5 个球、半径 0.015、弧长 0.088 → 渲染出来是"空中飘着几粒米"。
        for i in range(9):
            u = i / 8.0
            a = math.pi * u
            parts.append(kit.sphere(f"Tool_Handle_{i}",
                                    (0.0, 0.070 * math.cos(a), 0.030 + 0.072 * math.sin(a)),
                                    0.021, "QMetal", segs=8, rings=5))

    elif kind == "fert":
        # 肥料袋：麻袋，扎口的颈在 GRIP，袋身挂在下方。袋身总高 0.40m。
        parts.append(kit.sphere("Tool_Bag", (0.0, 0.0, -0.238), 0.150, "QBurlap",
                                scale=(1.0, 0.92, 1.28), segs=12, rings=8))
        parts.append(kit.cone("Tool_Neck", (0.0, 0.0, -0.052), 0.052, 0.088, 0.105,
                              "QBurlap", verts=10, smooth=False))
        parts.append(kit.cyl("Tool_Tie", (0.0, 0.0, -0.020), 0.058, 0.030, "QBoot",
                             verts=10, smooth=False))
        parts.append(kit.cone("Tool_Mouth", (0.0, 0.0, 0.022), 0.062, 0.046, 0.055,
                              "QBurlap", verts=10, smooth=False))
        # 袋内露出的肥料（既看得见，又直接说明"这是装肥料、能撒的东西"）。
        # ★ 必须落在袋口翻边（z=0.049）**下面**，否则会从口沿穿出去，
        #   渲出来像一颗卡在瓶口的怪球。
        for i, (dx, dy, dz, r) in enumerate(((0.0, 0.0, 0.028, 0.046),
                                             (0.032, -0.014, 0.020, 0.028),
                                             (-0.028, 0.018, 0.022, 0.026),
                                             (0.004, 0.030, 0.016, 0.024),
                                             (-0.016, -0.030, 0.018, 0.024))):
            parts.append(kit.sphere(f"Tool_Content_{i}", (dx, dy, dz), r, "QToolDirt",
                                    segs=6, rings=4))
        parts.append(kit.cyl("Tool_MouthRim", (0.0, 0.0, 0.049), 0.064, 0.014, "QBoot",
                             verts=10, smooth=False))
    else:
        raise ValueError(kind)

    merged = kit.join(parts, f"TOOL_{kind}")
    kit.apply_transforms(merged)

    # ★ GRIP 节点必须是"干净"的：位置在原点、**旋转单位**、缩放 1 ★
    #   Unity 侧拿它对齐手部骨骼（局部旋转 0,180,0）。
    #   朝向**烘进网格顶点**，GRIP 自己保持 identity：
    #     ① 顶点左乘 R_grip⁻¹ —— 把"世界朝向"的几何搬进"握持坐标系"
    #     ② GRIP 空物体摆在原点、旋转 identity
    #     ③ `matrix_parent_inverse` 直接设单位阵，**不要**用
    #        `parent_set(keep_transform=True)` —— 后者会写一个非单位的 inverse，
    #        于是 GRIP 又把 R_grip 加了回来，节点旋转不再是 identity（实测 90°）。
    off = g.inverted()
    for v in merged.data.vertices:
        v.co = off @ v.co
    merged.data.update()

    bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
    grip = bpy.context.active_object
    grip.name = "GRIP"

    merged.parent = grip
    merged.matrix_parent_inverse = mathutils.Matrix.Identity(4)
    bpy.context.view_layer.update()

    gq = grip.rotation_quaternion
    grip_identity = (all(abs(a - b) < 1e-6 for a, b in zip(gq, mathutils.Quaternion()))
                     and grip.location.length < 1e-6)
    print(f"[PLAYERQ] tool_{kind}: GRIP 节点={'原点 + identity ✔' if grip_identity else '★异常★'}")

    err = grip_axis_error(g)
    print(f"[PLAYERQ] tool_{kind}: 三角面={kit.tri_count(merged)} "
          f"GRIP 转角={g_ang:.1f}° 掌心法线差 {err:.1f}°")

    path = os.path.join(TOOL_DIR, f"tool_{kind}.fbx")
    kit.ensure_dir(path)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, global_scale=1.0,
        object_types={'MESH', 'EMPTY'},    # 武器没有骨架，但 GRIP 是 EMPTY，必须带上
        mesh_smooth_type='FACE', path_mode='AUTO', embed_textures=False,
        bake_anim=False, add_leaf_bones=False, armature_nodetype='NULL',
        axis_forward='-Z', axis_up='Y', use_space_transform=True,
    )
    drop_meta(path)

    pts = [merged.matrix_world @ V(c) for c in merged.bound_box]
    dims = (max(p.x for p in pts) - min(p.x for p in pts),
            max(p.y for p in pts) - min(p.y for p in pts),
            max(p.z for p in pts) - min(p.z for p in pts))
    print(f"[PLAYERQ] tool_{kind} -> {path}  dims={tuple(round(v, 3) for v in dims)}")
    return dict(kind=kind, path=path, tris=kit.tri_count(merged), dims=dims,
                grip_yaw=g_ang, grip_err=err, grip_identity=grip_identity)


def build_tools():
    return [build_tool(k) for k in ("hoe", "can", "fert")]


# ================================================================ 姿态
def rest_all(arm):
    """把所有骨骼复位到 rest。**每帧先复位，再叠加本帧姿态。**"""
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.location = (0.0, 0.0, 0.0)
        pb.scale = (1.0, 1.0, 1.0)


def add_rot(arm, name, x=0.0, y=0.0, z=0.0, degrees=True):
    """在**当前**姿态上叠加一段欧拉旋转（度）。骨骼旋转是相对 rest 的，累加即叠加。"""
    pb = arm.pose.bones.get(name)
    if pb is None:
        return
    k = D(1.0) if degrees else 1.0
    pb.rotation_euler = (pb.rotation_euler.x + x * k,
                         pb.rotation_euler.y + y * k,
                         pb.rotation_euler.z + z * k)


def arm_axes(arm, side):
    """返回某侧手臂的语义轴（**世界系**）：(抬起轴, 前摆轴)。

    ★ 这一版把"语义轴"的定义写死了，不再从骨骼方向现推 ★
      `gen_player_v2` 的写法是 `lift = up × d`、`swing = d × lift`，方向随骨骼
      朝向走 —— 而手臂骨在 A-pose 下是**斜的**，于是左右两侧的 swing 轴**符号相反**，
      再叠上代码里那份"左手 +st / 右手 −st"的符号，两条手臂最终**同相摆动**：
      实测右手与右腿的前后摆相关 = **+0.986**（同手同脚），左手与右手也同相。
      走路时腿的判据（±30° 反相）照样全过，所以这个缺陷**只能从手/脚的世界坐标上看出来**。

      现在两侧都用同一组世界轴，符号由调用方明确给出：
        · 抬起轴 = (0, −sgn, 0)：正角 = 手臂向外抬起（远离躯干）
        · 前摆轴 = (−1, 0, 0)  ：正角 = 手向前甩（−Y 是角色正面）
      两条手臂共用同一个前摆轴 ⇒ "左手正角、右手负角"就是自然的前后交替。
    """
    sgn = 1.0 if side == "L" else -1.0
    lift = V((0.0, -sgn, 0.0))
    swing = V((-1.0, 0.0, 0.0))
    return lift, swing


def world_rot_local(arm, bone_name, q_world):
    """把**世界系**的一段旋转换算成该骨骼的**局部**旋转。

    ★ 为什么必须做这一步 ★
      `pose_bone.rotation_euler` 是**骨骼局部系**里的量，而骨骼的局部 Y 轴就是
      *沿着骨骼自己*的那条轴。于是"用世界轴构造的四元数直接写进 rotation_euler"
      会把"抬起手臂"变成"绕手臂轴自转（扭麻花）"—— 角度读数一切正常，
      模型却只是原地拧了一下。这条既看不出来又量不出来，只能靠换算出正确的局部量。
      换算就是把世界旋转按骨骼的**世界静止旋转**做一次共轭：
          R_local = R_rest⁻¹ · Q_world · R_rest
      （父骨骼若也在动，效果自然是"相对于躯干再叠加"，这正是层级该有的行为。）
    """
    R = (arm.matrix_world @ arm.data.bones[bone_name].matrix_local).to_quaternion()
    return R.inverted() @ q_world @ R


def apply_arm(arm, side, lift_deg=0.0, swing_deg=0.0, out_deg=0.0,
              lower=0.0, hand=(0.0, 0.0, 0.0), shoulder=(0.0, 0.0, 0.0)):
    """按语义量摆一条手臂（角度都是**相对静止姿态**的世界系语义）。

    lift_deg : 抬起（正 = 手臂向外/向上抬，远离躯干）
    swing_deg: 前摆（正 = 手向前甩，两侧同一约定）
    out_deg  : 外展（正 = 再向外张开一点；与抬起同轴，是给"张开"留的细调量）
    lower    : 小臂相对大臂的弯曲（正 = 屈肘收手）
    hand     : 手腕的 (x,y,z) 附加欧拉（度，骨骼局部系）
    shoulder : 肩骨的附加坡度（跟随大臂做一点，动作才不像"只有一根骨头在转"）
    """
    lift, swing = arm_axes(arm, side)
    pb_u = arm.pose.bones[f"Bone_UpperArm_{side}"]
    pb_l = arm.pose.bones[f"Bone_LowerArm_{side}"]
    pb_h = arm.pose.bones[f"Bone_Hand_{side}"]
    if shoulder != (0.0, 0.0, 0.0):
        add_rot(arm, f"Bone_Shoulder_{side}", *shoulder)
    q = (mathutils.Quaternion(lift, D(lift_deg + out_deg))
         @ mathutils.Quaternion(swing, D(swing_deg)))
    pb_u.rotation_euler = world_rot_local(arm, f"Bone_UpperArm_{side}",
                                          q).to_euler('XYZ')
    if lower:
        pb_l.rotation_euler = (D(lower), 0.0, 0.0)
    if hand != (0.0, 0.0, 0.0):
        pb_h.rotation_euler = (D(hand[0]), D(hand[1]), D(hand[2]))


def ease_out(k):
    return 1.0 - (1.0 - k) * (1.0 - k)


def ramp(t, a, b):
    """[a,b] 内的 0→1 线性推进（越界钳制）。"""
    if b <= a:
        return 1.0 if t >= b else 0.0
    return max(0.0, min(1.0, (t - a) / (b - a)))


# ---------------------------------------------------------------- 姿态函数
def pose_player(anim, t, arm):
    """相位 t（循环动画传 [0,1)，一次性动画传 [0,1]）→ 骨骼姿态。

    纯函数，无随机（同 t 必得同姿态）。
    约定（与全工程一致）：绕 X 正 = 身体前倾 / 头低下；绕 Z 正 = 左右转；绕 Y 正 = 左右侧倾。
    """
    rest_all(arm)
    tau = 2.0 * math.pi
    bp = math.sin(tau * t)                                  # 呼吸相位（0.25Hz）
    # 步相位：96 帧里走 6 圈 = 1.5Hz。**与呼吸 6:1**，不可能同频。
    st = math.sin(tau * t * (ANIMS["walk"][0] / float(WALK_STEP_FRAMES)))

    if anim == "idle":
        # ── 强烈呼吸起伏 ──
        # 拆三层，全部由**同一个** bp 驱动、同一个 0.25Hz：
        #   ① 胸腔抬升（有**位移**，不只是转一点角度）
        #   ② 脊柱伸展（正面看得出胸口起伏）
        #   ③ 肩部抬落（俯视时肩线在"张—收"之间变化）
        # 只做①会像在点头；三层加起来才叫"一眼能看出角色在活着"。
        arm.pose.bones["Bone_Chest"].location = (0.0, 0.0, 0.030 * bp)
        arm.pose.bones["Bone_Spine"].location = (0.0, 0.0, 0.014 * bp)
        add_rot(arm, "Bone_Chest", x=-6.2 * bp)
        add_rot(arm, "Bone_Spine", x=-3.0 * bp)
        add_rot(arm, "Bone_Hips", x=1.1 * bp)              # 髋随呼吸极轻地前后倒
        add_rot(arm, "Bone_Neck", x=3.0 * bp)
        # 头：呼吸节拍 + 一个更慢的左右看（0.5 倍频 = 0.125Hz，刻意与呼吸错开）
        add_rot(arm, "Bone_Head", x=-4.0 * bp, z=5.5 * math.sin(tau * t * 0.5))
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Shoulder_{side}", x=-4.0 * bp,
                    z=(2.0 * bp if side == "L" else -2.0 * bp))
        # 手臂：贴身轻晃，**两侧同相**（反相看起来像在走路）
        for side in ("L", "R"):
            apply_arm(arm, side, lift_deg=5.0 * bp, swing_deg=2.0 * bp,
                      out_deg=3.0 * bp, lower=4.0 * bp)
        # 腿：站着不能踏步。只做 1.2° 的重心微调。
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Thigh_{side}", x=0.7 * bp)
            add_rot(arm, f"Bone_LowerLeg_{side}", x=-1.2 * bp)

    elif anim == "walk":
        # ── 两个**不同频率**叠加（需求：呼吸 ≈0.25Hz、步伐 ≈1.5Hz，不要同频）──
        # 步相位 st 每帧走 1/16 圈 = 1.5Hz；呼吸 bp 每帧走 1/96 圈 = 0.25Hz。
        # 片段长 96 帧 = 6 步 = 1 呼吸 → 6:1。
        #
        # ⚠ 上一版踩过的坑：walk 第一版把呼吸写成 `sin(2πt)`（与步相位同一个函数），
        #   于是 `-5.0*breathe + 3.2*sin(2πt)` 两项**同频反相**，合成塌成 -1.8*sin()，
        #   呼吸被自己抵消掉，量出来"呼吸层 = 1.41Hz"。
        #   **写成两个数不等于就是两个频率，相位必须真的错开。**
        # 腿：左右反相 ±30°（Q 版步子大一点，读得出"在跑"）
        add_rot(arm, "Bone_Thigh_R", x=30.0 * st)
        add_rot(arm, "Bone_Thigh_L", x=-30.0 * st)
        # 膝：后摆的那条腿屈膝（走路必须有膝，否则像钟摆）
        add_rot(arm, "Bone_LowerLeg_R", x=-24.0 * max(0.0, -st))
        add_rot(arm, "Bone_LowerLeg_L", x=-24.0 * max(0.0, st))
        add_rot(arm, "Bone_Foot_R", x=10.0 * st)
        add_rot(arm, "Bone_Foot_L", x=-10.0 * st)
        # 髋：每步一次起伏（1.5Hz）+ 呼吸的慢抬升（0.25Hz）
        arm.pose.bones["Bone_Hips"].location = (0.0, 0.0, 0.030 * abs(st) + 0.012 * bp)
        add_rot(arm, "Bone_Hips", x=2.6 * math.sin(tau * t * 2.0), y=2.4 * st, z=-3.6 * st)
        # 躯干：步频的前后颠（1.5Hz）+ **呼吸的慢后仰（0.25Hz）**
        # ★ 呼吸只放在 Spine.x ★ 同时叠在 Chest.x 上时，Chest.x 的主频会被 1.5Hz
        #   的颠簸吃掉，量出来就是"呼吸=1.41Hz"。物理上也对：走路时跨步的颠簸让胸腔
        #   在 1.5Hz 抖，呼吸体现在**肩线的慢起落**上。
        arm.pose.bones["Bone_Chest"].location = (0.0, 0.0, 0.020 * bp)
        add_rot(arm, "Bone_Spine", x=-8.0 * bp + 3.4 * math.sin(tau * t),
                y=1.8 * st, z=2.8 * st)
        add_rot(arm, "Bone_Chest", x=1.0 * math.sin(tau * t * 2.0), y=1.2 * st, z=1.8 * st)
        add_rot(arm, "Bone_Neck", x=-2.0 * bp)
        add_rot(arm, "Bone_Head", x=-3.0 - 2.4 * math.sin(tau * t * 2.0) + 2.6 * bp,
                z=-4.6 * st)
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Shoulder_{side}", x=-3.4 * bp)
        # 手臂：与腿反相摆 + 呼吸的抬落。
        # ★ 符号就是"走路"本身 ★ 右腿在 st>0 时后摆（Thigh_R x=+30st ⇒ 脚向 +Y 即身后），
        #   所以**右手必须向前**（swing 正 = 前），左手向后。摆错就成同手同脚 ——
        #   实测过 +0.986 的相关，而腿自己的 ±30° 反相判据照样全过。
        apply_arm(arm, "R", lift_deg=4.0 * bp + 2.0 * abs(st),
                  swing_deg=21.0 * st, out_deg=3.4 * abs(st), lower=18.0 + 9.0 * abs(st))
        apply_arm(arm, "L", lift_deg=4.0 * bp + 2.0 * abs(st),
                  swing_deg=-21.0 * st, out_deg=3.4 * abs(st), lower=18.0 + 9.0 * abs(st))

    elif anim == "hit":
        # 受击后仰（24 帧 = 1s）。**只给姿态**：游戏侧已有"膨胀 + 闪白"，
        # 这里绝不做缩放也不做染色（需求明确点了这一条）。
        k = math.sin(math.pi * min(1.0, t / 0.55)) if t < 0.55 else 0.0
        rec = ease_out(ramp(t, 0.55, 1.0))
        kk = k * (1.0 - rec)
        add_rot(arm, "Bone_Hips", x=-7.0 * kk)
        add_rot(arm, "Bone_Spine", x=-12.0 * kk)
        add_rot(arm, "Bone_Chest", x=-14.0 * kk, z=6.0 * kk)
        add_rot(arm, "Bone_Neck", x=-2.0 * kk)
        add_rot(arm, "Bone_Head", x=-20.0 * kk, z=10.0 * kk)
        apply_arm(arm, "R", lift_deg=-2.0 * kk, swing_deg=-18.0 * kk,
                  out_deg=16.0 * kk, lower=28.0 * kk)
        apply_arm(arm, "L", lift_deg=-2.0 * kk, swing_deg=-18.0 * kk,
                  out_deg=16.0 * kk, lower=32.0 * kk)
        # 腿：受击时前后撑一下（受力反馈），幅度小、不是踏步
        add_rot(arm, "Bone_Thigh_R", x=-5.0 * kk)
        add_rot(arm, "Bone_Thigh_L", x=6.0 * kk)
        add_rot(arm, "Bone_LowerLeg_R", x=-4.0 * kk)
        add_rot(arm, "Bone_LowerLeg_L", x=-6.0 * kk)

    elif anim == "swap":
        # 切换武器：收起当前 → 抽出下一把。9 帧 = 0.375s（需求 0.3~0.4s）。
        # **只上半身**（玩家可能在跑动中换武器）。
        if t < 0.45:
            k = math.sin(math.pi * 0.5 * ramp(t, 0.0, 0.45))          # 缓入
        else:
            k = 1.0 - ease_out(ramp(t, 0.45, 1.0))
        add_rot(arm, "Bone_Spine", x=4.0 * k)
        add_rot(arm, "Bone_Chest", x=7.0 * k, z=-9.0 * k)
        add_rot(arm, "Bone_Neck", x=-3.0 * k)
        add_rot(arm, "Bone_Head", x=5.0 * k, z=-12.0 * k)
        apply_arm(arm, "R", lift_deg=-32.0 * k, swing_deg=-25.0 * k,
                  out_deg=-8.0 * k, lower=50.0 * k, hand=(0.0, 0.0, -18.0 * k))
        apply_arm(arm, "L", lift_deg=-10.0 * k, swing_deg=-15.0 * k,
                  out_deg=6.0 * k, lower=24.0 * k)
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Shoulder_{side}", x=5.0 * k)

    elif anim.startswith("attack_"):
        _pose_attack(anim, t, arm)

    else:
        raise ValueError(anim)


def _pose_attack(anim, t, arm):
    """三把武器各一套专属攻击。**只动上半身**（腿骨一律不碰 → 曲线恒 0）。

    三套必须彼此不同，所以各自的"发力源"刻意选在不同关节：
        hoe  → 肩 + 躯干大幅折叠（重量在躯干）
        can  → 手腕 + 小臂为主，躯干几乎不动（重量在手）
        fert → 大臂外摆横扫，躯干侧转（重量在肩的横向）
    即使把武器藏起来，光看上半身剪影也能分出是哪一把。
    """
    if anim == "attack_hoe":
        # 抡起 → 劈下（36 帧 = 1.5s）：大幅、有重量。
        # ★ 手臂的语义量直接写"抬起 / 前摆"，不再借道 lift 的半成品符号 ★
        #   起手 = 手臂抬到身后上方（抬起 +58、前摆 −42、屈肘 62）
        #   发力 = 一斧劈到身前下方（抬起 −12、前摆 +58、肘放直 14）
        if t < 0.42:                       # 前摇：抬到身后
            e = math.sin(math.pi * 0.5 * ramp(t, 0.0, 0.42))
            a_lift, a_swing, lower = 58.0 * e, -42.0 * e, 62.0 * e
            lean, twist = -16.0 * e, 17.0 * e
        elif t < 0.60:                     # 劈：快速下砸
            k = (t - 0.42) / 0.18
            a_lift = 58.0 - 70.0 * k
            a_swing = -42.0 + 100.0 * k
            lower = 62.0 - 48.0 * k
            lean = -16.0 + 42.0 * k
            twist = 17.0 - 28.0 * k
        else:                              # 回收：缓出回到站姿
            k = ease_out(ramp(t, 0.60, 1.0))
            a_lift = -12.0 * (1.0 - k)
            a_swing = 58.0 * (1.0 - k)
            lower = 14.0 * (1.0 - k)
            lean = 26.0 * (1.0 - k)
            twist = -11.0 * (1.0 - k)
        add_rot(arm, "Bone_Spine", x=lean * 0.55, z=twist * 0.45)
        add_rot(arm, "Bone_Chest", x=lean * 0.75, z=twist * 0.65)
        add_rot(arm, "Bone_Neck", x=-lean * 0.20)
        add_rot(arm, "Bone_Head", x=lean * 0.50, z=twist * 0.30)
        apply_arm(arm, "R", lift_deg=a_lift, swing_deg=a_swing,
                  out_deg=8.0, lower=lower, hand=(14.0, 0.0, -12.0))
        apply_arm(arm, "L", lift_deg=a_lift * 0.30, swing_deg=a_swing * 0.30,
                  out_deg=10.0, lower=lower * 0.50)
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Shoulder_{side}",
                    x=(-8.0 if side == "R" else -4.0) * (a_lift / 58.0),
                    z=(6.0 if side == "R" else -3.0) * (a_lift / 58.0))

    elif anim == "attack_can":
        # 倾斜壶身、倒下（小幅、**腕部为主**）。躯干几乎不动 —— 这是与 hoe 的分界。
        # 手臂：抬起 18 + 前摆 22（往身前倒水）—— 前摆为正才是"倒向前方"。
        if t < 0.46:
            e = ease_out(ramp(t, 0.0, 0.46))
            tilt, lift, swing, lower, lean = 78.0 * e, 18.0 * e, 22.0 * e, 46.0 * e, -4.0 * e
        else:
            k = ease_out(ramp(t, 0.46, 1.0))
            tilt = 78.0 * (1.0 - k)
            lift = 18.0 * (1.0 - k)
            swing = 22.0 * (1.0 - k)
            lower = 46.0 * (1.0 - k)
            lean = -4.0 * (1.0 - k)
        add_rot(arm, "Bone_Spine", x=lean * 0.5)
        add_rot(arm, "Bone_Chest", x=lean, z=-4.0 * (tilt / 78.0))
        add_rot(arm, "Bone_Head", x=-3.0 * (tilt / 78.0))
        # can 的辨识度全在手腕：腕绕 X 大角度翻转（就是"把壶倒过来"）
        apply_arm(arm, "R", lift_deg=lift, swing_deg=swing, out_deg=12.0,
                  lower=lower, hand=(tilt, 0.0, -6.0))
        apply_arm(arm, "L", lift_deg=6.0, swing_deg=-6.0, out_deg=8.0, lower=12.0)
        add_rot(arm, "Bone_Shoulder_R", x=-4.0 * (tilt / 78.0))

    elif anim == "attack_fert":
        # 抓一把往前撒（**手臂外摆**）。发力源在肩的横向外展 —— 与另外两套都不同。
        # 起手 = 手往身侧下方探（抬起 −36、前摆 −28），发力 = 大幅外摆到身前（抬起 48、前摆 +56）
        if t < 0.30:                       # 探身抓肥
            e = ease_out(ramp(t, 0.0, 0.30))
            out_d, lift, swing = -32.0 * e, -36.0 * e, -28.0 * e
            lean, twist = 20.0 * e, -19.0 * e
        elif t < 0.56:                     # 外摆撒出
            k = (t - 0.30) / 0.26
            out_d = -32.0 + 104.0 * k
            lift = -36.0 + 84.0 * k
            swing = -28.0 + 84.0 * k
            lean = 20.0 - 34.0 * k
            twist = -19.0 + 56.0 * k
        else:
            k = ease_out(ramp(t, 0.56, 1.0))
            out_d = 72.0 * (1.0 - k)
            lift = 48.0 * (1.0 - k)
            swing = 56.0 * (1.0 - k)
            lean = -14.0 * (1.0 - k)
            twist = 37.0 * (1.0 - k)
        add_rot(arm, "Bone_Spine", x=lean * 0.5, z=twist * 0.5)
        add_rot(arm, "Bone_Chest", x=lean * 0.8, z=twist * 0.7)
        add_rot(arm, "Bone_Neck", x=-lean * 0.18)
        add_rot(arm, "Bone_Head", x=lean * 0.45, z=twist * 0.35)
        apply_arm(arm, "R", lift_deg=lift, swing_deg=swing, out_deg=out_d,
                  lower=24.0 + 15.0 * abs(math.sin(math.pi * t)),
                  hand=(-10.0, 0.0, 8.0))
        apply_arm(arm, "L", lift_deg=lift * 0.35, swing_deg=swing * 0.30,
                  out_deg=out_d * 0.30, lower=18.0)
        add_rot(arm, "Bone_Shoulder_R", x=-6.0 * (lift / 40.0), z=10.0 * (out_d / 72.0))


# ================================================================ 动画导出
def keyed_bones(arm):
    """本动画要打关键帧的骨骼清单（19 根全打）。

    ★ 腿骨一定要在里面 ★ 需求要的是"腿部曲线存在但为零"，所以腿骨要打关键帧，
    只是值恒为 0（pose_player 对 attack/swap 根本不碰它们，rest_all 已把值压回 0）。
    """
    return [arm.pose.bones[b[0]] for b in BONES]


def export_anim(anim, frames, loop):
    """导出一条动画。**每次重新搭一次角色**。

    ★ 不要复用同一个骨架 ★ Blender 5 的 action 挂在"槽位"上，换 action 之后
    槽位还停在上一条绑定上，实测后果是一个 FBX 里同时写进两条 take，Unity 侧互相覆盖。
    重建一次骨架只要零点几秒，换来的是一条铁律：**一个 FBX 一条 take**。
    """
    arm, meshes = assemble_player()
    scene = bpy.context.scene
    total = frames + 1 if loop else frames
    scene.frame_start = 1
    scene.frame_end = total

    pbs = keyed_bones(arm)
    shared = bpy.data.actions.new(f"player_{anim}")
    arm.animation_data_create()
    arm.animation_data.action = shared

    # ★ 只上半身的动画**不写 Hips 的 location 曲线** ★
    #   写了（哪怕恒 0）会在两段采样时把 move 片段的髋部起伏按 0 覆盖掉，
    #   表现是"跑动中攻击，髋瞬移回静止"。角度照写（恒 0），硬判据不受影响。
    loc_bones = (("Bone_Spine", "Bone_Chest") if anim in UPPER_ONLY
                 else ("Bone_Hips", "Bone_Spine", "Bone_Chest"))

    for f in range(1, total + 1):
        if loop:
            t = (f - 1) / float(frames)
            if f == total:
                t = 0.0            # 收尾帧 = 首帧，loopTime 才无缝
        else:
            t = (f - 1) / float(max(1, total - 1))
        # ★ 先 frame_set → 再摆姿态 → 再 keyframe_insert（顺序不能换：
        #   frame_set 触发依赖图求值，会把刚摆好的姿态冲掉）★
        scene.frame_set(f)
        pose_player(anim, t, arm)
        for pb in pbs:
            pb.keyframe_insert(data_path="rotation_euler")
        for pb in pbs:
            if pb.name in loc_bones:
                pb.keyframe_insert(data_path="location")

    path = os.path.join(ANIM_DIR, f"player_{anim}.fbx")
    kit.ensure_dir(path)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, global_scale=1.0,
        object_types={'MESH', 'ARMATURE', 'EMPTY'},
        mesh_smooth_type='FACE', path_mode='AUTO', embed_textures=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=1.0,
        add_leaf_bones=False,
        armature_nodetype='NULL',
        axis_forward='-Z', axis_up='Y', use_space_transform=True,
    )
    drop_meta(path)
    mode = "只上半身" if anim in UPPER_ONLY else "全身"
    print(f"[PLAYERQ] {anim}: {total} 帧 loop={loop} {mode} "
          f"location 曲线 {loc_bones} -> {os.path.basename(path)}")
    return path


def build_anims(only=None):
    out = []
    for anim, (frames, loop, _note) in ANIMS.items():
        if only and anim not in only:
            continue
        out.append(export_anim(anim, frames, loop))
    return out


# ================================================================ 入口
def _args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def _run(cmd, rest):
    if cmd == "static":
        build_static()
    elif cmd == "tools":
        build_tools()
    elif cmd == "anim":
        build_anims(rest or None)
    elif cmd == "all":
        build_static()
        build_tools()
        build_anims()
    elif cmd == "verify":
        import verify_player_q as vq
        vq.verify()
    elif cmd == "preview":
        import verify_player_q as vq
        vq.render_previews()
    else:
        print(USAGE)


USAGE_BODY = """用法:
  blender -b --factory-startup -P gen_player_q.py -- all
  blender -b --factory-startup -P gen_player_q.py -- static
  blender -b --factory-startup -P gen_player_q.py -- tools
  blender -b --factory-startup -P gen_player_q.py -- anim [名字...]
  blender -b --factory-startup -P gen_player_q.py -- verify
  blender -b --factory-startup -P gen_player_q.py -- preview
动画名: """ + ", ".join(ANIMS)
USAGE = USAGE_BODY


def main():
    a = _args()
    if not a:
        print(USAGE)
        return
    _run(a[0], a[1:])


if __name__ == "__main__":
    main()
