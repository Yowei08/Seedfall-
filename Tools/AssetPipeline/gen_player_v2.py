# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 玩家 v2 生成器（模型 + 动画 + 三把武器）
============================================================
需求原话：
  "重做玩家模型，**减少帽子大小，让视觉从上而下能够预览到更多角色身体部位**，
   同样重新配套角色**移动动画、受击攻击动画**；设计玩家的三个武器，制作**锄头、
   水壶、肥料模型，绑定在角色手部**、当切换武器时实时切换模型资源，并播放切换动画，
   为每个武器**制作专属的攻击动画（只影响上半身，不作用于下半身，下半身维持移动
   或者站立）**；强化角色**待机动画和移动动画表现，强烈的呼吸感起伏**"

────────────────────────────────────────────────────────────────
一、为什么用「真骨架 + 骨头父子绑定」而不是继续用空物体木偶
────────────────────────────────────────────────────────────────
既有动画（gen_anim / gen_pig / gen_plants_biped）都是**空物体木偶**：
每个身体部件是一个 Empty，部件网格挂成它的子节点。这套做法能跑，但无法满足本次
"用 Avatar Mask 把下半身交给 idle/walk、上半身交给 attack" 这条要求 —— Avatar
Mask 是按**骨骼名**过滤的，空物体没有骨骼名可过滤。

所以本文件改用 **Armature（Armature）+ 命名骨骼 + 骨头父子绑定**：
  · 骨骼名是人可读的 `Bone_*`（报告里给了完整清单，Unity 侧直接照着配 Mask）
  · 网格件用 `parent_set(type='BONE')` 绑到骨骼上 —— **不是蒙皮**
  · 绑定路径于是变成 `Armature/Bone_Chest/Part_Chest`，既是"骨骼"也是"节点路径"

★ 为什么不用 SkinnedMeshRenderer（蒙皮）★
  实测约束在 `Assets/Scripts/Art/ModelView.cs`：
    · `MeshRawHeight()` / `MeshLocalBounds()` 都是 `GetComponentsInChildren<MeshFilter>()`
      （第 208、260 行）。蒙皮网格是 `SkinnedMeshRenderer`，**没有 MeshFilter**，
      于是 RawHeight 会变成 0 → ScaleToHeight 直接 return、SetScaleFactor 算不出尺寸。
    · 骨头父子绑定导出的仍然是**普通 MeshFilter 网格件**，上面两个函数一个字都不用改。
  代价是没有关节处的平滑形变（低多边形玩具风本来也是硬切），换来的是"引擎侧零改动"。

────────────────────────────────────────────────────────────────
二、踩过的坑（都实测过，别再踩）
────────────────────────────────────────────────────────────────
  ① `parent_set(type='BONE')` 会报 `No active bone` —— 活动骨骼只能在 **POSE 模式**
     下设置（EDIT 模式重建骨骼、OBJECT 模式没有活动骨骼）。
  ② 读取回读后的骨骼角度要按**旋转模式**读：FBX 导出把骨骼烘成四元数，回读后
     `pose_bone.rotation_mode == 'QUATERNION'`，此时 `rotation_euler` 恒为 0 ——
     照着 euler 读会得到"动画没导出去"的**假故障**（本文件第一版探针就是这样，
     差点去改一套本来正确的导出参数）。必须先 `to_euler()` 再读。
  ③ 轴向只处理一次：`export_fbx` 的 `axis_forward='-Z' / axis_up='Y'` 已经做了
     Blender Z-up → Unity Y-up，**不要再自己烘 -90°**（见 seedfall_kit.export_fbx 注释）。
  ④ FBX 里 `hierarchy_type` 必须带 `'ARMATURE'`，否则骨架根本不在场景里，骨骼全丢。
  ⑤ 顺序不能换：**先 frame_set → 再摆姿态 → 再 keyframe_insert**
     （frame_set 触发依赖图求值会把刚摆好的姿态冲掉，见 gen_pig.export_anim）。

────────────────────────────────────────────────────────────────
三、上半身 / 下半身的骨骼分工（D 项的验收点）
────────────────────────────────────────────────────────────────
  下半身（**三条 attack 与 swap 里一律不带关键帧**，曲线恒为 rest）：
      Bone_Hips（仅旋转不动，见下）/ Bone_Thigh_L,R / Bone_LowerLeg_L,R / Bone_Foot_L,R
  上半身（attack / swap 只动这些）：
      Bone_Spine / Bone_Chest / Bone_Neck / Bone_Head
      Bone_Shoulder_L,R / Bone_UpperArm_L,R / Bone_LowerArm_L,R / Bone_Hand_L,R
  · `Bone_Hips` 是腿的父节点。为了让腿**绝对不动**，attack 里连 Hips 也保持 rest ——
    转 Hips 会连带转动整条腿，"下半身维持移动或站立"就失效了。
  · 腿骨仍然**打关键帧**（值恒为 0），因为需求明确要"曲线存在但为零"，
    这样 Unity 侧 Avatar Mask 才有一个"上半身有曲线、下半身曲线为零"的干净切分。

产出：
    Assets/Resources/Art/Characters/player.fbx                 静态模型（骨架 + 骨骼名）
    Assets/Resources/Art/Tools/tool_{hoe,can,fert}.fbx         三把武器（各带 GRIP 标记）
    Assets/Resources/Art/Anim/player_{idle,walk,hit,swap}.fbx  循环 / 一次性
    Assets/Resources/Art/Anim/player_attack_{hoe,can,fert}.fbx 三把武器各一套（只上半身）
    Docs/captures/player_v2_preview.png                        60° 俯视对照图

用法：
    blender -b --factory-startup -P gen_player_v2.py -- all
    blender -b --factory-startup -P gen_player_v2.py -- static
    blender -b --factory-startup -P gen_player_v2.py -- tools
    blender -b --factory-startup -P gen_player_v2.py -- anim [名字...]
    blender -b --factory-startup -P gen_player_v2.py -- verify
    blender -b --factory-startup -P gen_player_v2.py -- preview
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
CAPTURE = os.path.join(PROJECT, "Docs", "captures", "player_v2_preview.png")

# ---------------------------------------------------------------- 调色板
# 只**新增**键，不改既有键（改了会污染已验收的 4 角色 / 16 植物 / 环境模块）。
kit.PALETTE.update({
    "ToolHaft":  "#9A7148",   # 木柄（比 Wood 亮一档，锯开的桦木色）
    "ToolMetal": "#B9BCBE",   # 金属锄刃（比 Metal 暗一档，读得出是铁）
    "ToolDirt":  "#6E5236",   # 肥料/泥土
    "ToolCan":   "#7FA8B8",   # 水壶的搪瓷青
    "ToolBurlap": "#B98F5C",  # 麻袋
})

# ---------------------------------------------------------------- 规格
TARGET_H = 1.80          # 需求：高度约 1.8m
TRI_BUDGET = 2000        # 需求：面数 < 2000
FPS = 24.0

# ================================================================ 骨架
# 单位米，脚底 z=0，正面朝 -Y（沿用 gen_anim / gen_pig / gen_plants_biped 的约定）。
BONES = [
    # name,                head,                 tail,                parent
    ("Bone_Hips",        (0.000, 0.000, 0.920), (0.000, 0.000, 1.020), None),
    ("Bone_Spine",       (0.000, 0.000, 1.020), (0.000, 0.000, 1.180), "Bone_Hips"),
    ("Bone_Chest",       (0.000, 0.000, 1.180), (0.000, 0.000, 1.400), "Bone_Spine"),
    ("Bone_Neck",        (0.000, 0.000, 1.400), (0.000, 0.000, 1.480), "Bone_Chest"),
    ("Bone_Head",        (0.000, 0.000, 1.480), (0.000, 0.000, 1.800), "Bone_Neck"),
    ("Bone_Shoulder_L",  (0.040, 0.000, 1.400), (0.150, 0.000, 1.420), "Bone_Chest"),
    ("Bone_Shoulder_R",  (-0.040, 0.000, 1.400), (-0.150, 0.000, 1.420), "Bone_Chest"),
    ("Bone_UpperArm_L",  (0.150, 0.000, 1.420), (0.260, 0.000, 1.180), "Bone_Shoulder_L"),
    ("Bone_UpperArm_R",  (-0.150, 0.000, 1.420), (-0.260, 0.000, 1.180), "Bone_Shoulder_R"),
    ("Bone_LowerArm_L",  (0.260, 0.000, 1.180), (0.330, 0.000, 0.960), "Bone_UpperArm_L"),
    ("Bone_LowerArm_R",  (-0.260, 0.000, 1.180), (-0.330, 0.000, 0.960), "Bone_UpperArm_R"),
    ("Bone_Hand_L",      (0.330, 0.000, 0.960), (0.360, 0.000, 0.870), "Bone_LowerArm_L"),
    ("Bone_Hand_R",      (-0.330, 0.000, 0.960), (-0.360, 0.000, 0.870), "Bone_LowerArm_R"),
    ("Bone_Thigh_L",     (0.100, 0.000, 0.920), (0.105, 0.000, 0.500), "Bone_Hips"),
    ("Bone_Thigh_R",     (-0.100, 0.000, 0.920), (-0.105, 0.000, 0.500), "Bone_Hips"),
    ("Bone_LowerLeg_L",  (0.105, 0.000, 0.500), (0.108, 0.000, 0.100), "Bone_Thigh_L"),
    ("Bone_LowerLeg_R",  (-0.105, 0.000, 0.500), (-0.108, 0.000, 0.100), "Bone_Thigh_R"),
    ("Bone_Foot_L",      (0.108, 0.000, 0.100), (0.108, -0.120, 0.050), "Bone_LowerLeg_L"),
    ("Bone_Foot_R",      (-0.108, 0.000, 0.100), (-0.108, -0.120, 0.050), "Bone_LowerLeg_R"),
]

BONE_NAME = {b[0]: b for b in BONES}

#: **下半身骨骼**：attack / swap 里必须保持 rest（曲线存在但恒 0）
LOWER_BODY = ["Bone_Hips", "Bone_Thigh_L", "Bone_Thigh_R",
              "Bone_LowerLeg_L", "Bone_LowerLeg_R", "Bone_Foot_L", "Bone_Foot_R"]
#: **上半身骨骼**：attack / swap 允许打关键帧
UPPER_BODY = ["Bone_Spine", "Bone_Chest", "Bone_Neck", "Bone_Head",
              "Bone_Shoulder_L", "Bone_Shoulder_R",
              "Bone_UpperArm_L", "Bone_UpperArm_R",
              "Bone_LowerArm_L", "Bone_LowerArm_R",
              "Bone_Hand_L", "Bone_Hand_R"]

# 几何参数（米）。全部对着骨架写，改骨架这里要跟着改。
G = dict(
    # 躯干：髋 0.92 → 颈 1.40
    hip_c=(0.0, 0.0, 0.935), hip_r=0.128, hip_s=(1.10, 0.80, 0.52),
    waist_c=(0.0, 0.0, 1.075), waist_r=0.115, waist_s=(1.00, 0.70, 0.72),
    chest_c=(0.0, 0.0, 1.275), chest_r=0.150, chest_s=(1.04, 0.74, 1.06),
    # 颈 / 头 / 帽
    neck_r=0.058, neck_h=0.110,
    # 头心 1.620 + 半径 0.168 = 头顶 1.788（低于骨架的 1.80，草帽正好扣在上面）
    head_c=(0.0, 0.0, 1.620), head_r=0.168, head_s=(1.00, 0.98, 0.99),
    # 草帽三段：帽筒（比头大 5% 的球，扣在头上）+ 帽带 + 帽檐。
    # ★ 帽檐 0.180 ÷ 头半宽 0.168 = 1.07 ★ —— 需求建议 ≤1.2，这里留了余量。
    # 旧模型横向半径 0.360（帽子占掉了绝大部分），新值不到它一半。
    hat_crown_c=(0.0, 0.0, 1.626), hat_crown_r=0.1764, hat_crown_s=(1.00, 0.98, 1.01),
    hat_band_c=(0.0, 0.0, 1.646), hat_band_r=0.178, hat_band_s=(1.00, 0.98, 0.075),
    hat_brim_c=(0.0, 0.0, 1.652), hat_brim_r=0.180, hat_brim_s=(1.00, 1.00, 0.048),
    # 围巾
    scarf_c=(0.0, 0.0, 1.430), scarf_r=0.108, scarf_s=(1.00, 0.86, 0.30),
    # 肩 / 手
    shoulder_r=0.078, hand_r=0.058,
    # 腿
    thigh_r=0.072, calf_r=0.060, foot_s=(0.115, 0.185, 0.072),
)

#: 帽檐半径 ÷ 头半宽 —— 需求建议 ≤1.2。这是"帽子不许再盖住身体"的核心旋钮。
BRIM_RATIO_TARGET = 1.2

# ---------------------------------------------------------------- 动画表
# (总帧数, 是否循环, 说明)。帧数直接决定频率：24fps 下
#   idle 96 帧 = 4.00s = **0.25Hz**（呼吸周期，需求点名的那个数）
#   walk 96 帧 = 4.00s，里面正好装 **6 个步周期（16 帧/步 = 1.50Hz）** 和
#               **1 个呼吸周期（4.00s = 0.25Hz）**
#   两者 6:1，不可能同频 —— 这是需求"不要同频"的硬保证。
#
# ★ 为什么 walk 也是 96 帧，而不是一个 16 帧的短循环 ★
#   呼吸周期是 4s，而一个步周期只有 0.667s。如果 walk 只有 16 帧（一个步周期），
#   呼吸相位在里面只走了 1/6 圈，**循环接回去时呼吸会被硬切**（波形不闭合，
#   首尾差很大，Unity 侧 loopTime 会看到跳帧）。
#   让片段长度 = 呼吸周期 = 6 个步周期，两条曲线才同时闭合。
#   频率没有变（步频仍是 1.5Hz、呼吸仍是 0.25Hz），变的只是"片段装了几个周期"。
ANIMS = {
    "idle":        (96, True,  "强烈呼吸起伏，胸腔 0.25Hz"),
    "walk":        (96, True,  "步频 1.5Hz 腿反相 + 呼吸 0.25Hz 叠加（6:1）"),
    "hit":         (24, False, "受击后仰（只给姿态，不做缩放/染色）"),
    "swap":        (9, False, "收起当前 → 抽出下一把，0.375s，只上半身"),
    "attack_hoe":  (36, False, "抡起 → 劈下，大幅有重量"),
    "attack_can":  (36, False, "倾斜壶身倒下，小幅腕部为主"),
    "attack_fert": (36, False, "抓一把往前撒，手臂外摆"),
}

#: walk 的步周期帧数（一个左右完整的步 = 1 步周期）。呼吸周期 = 6 × 它。
WALK_STEP_FRAMES = 16

#: attack / swap 一律只动上半身；idle / walk / hit 动全身
UPPER_ONLY = ("swap", "attack_hoe", "attack_can", "attack_fert")

# 呼吸频率（Hz）与步频（Hz）—— 需求点名的两个数，验证时按这两个数去核
BREATH_HZ = 0.25
STEP_HZ = 1.50


# ================================================================ 通用
def drop_meta(path):
    """删掉 Unity 生成的 .fbx.meta —— 同名 FBX 内容变了但 meta 还在时 Unity 可能不重导。"""
    meta = path + ".meta"
    if os.path.exists(meta):
        os.remove(meta)
        print(f"[PLAYER2] 已删 {os.path.basename(meta)}（强制 Unity 重新导入）")


def limb_tube(name, p0, p1, r0, r1, color, verts=8):
    """在两点之间放一根锥形筒（肢体段）。返回对象。

    为什么要写这个而不是 kit.cyl：肢体的两端**不在同一条竖直线上**
    （A-pose 的手臂是斜的），kit.cyl 只能给固定 rot，斜向位置得手算欧拉角，
    改一次骨架就得重算一遍。这里用 (p1-p0) 直接对齐，骨架一改自动跟着走。
    """
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    length = max(1e-4, d.length)
    mid = (p0 + p1) * 0.5
    # 圆锥的默认轴是 +Z；把 +Z 转到 d 的方向
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
    """造出玩家的全部网格件。返回 [(obj, 绑定骨骼名), ...]。"""
    out = []
    g = G

    def sphere(name, loc, r, color, scale, segs=10, rings=6, bone=None):
        o = kit.sphere(name, loc, r, color, scale=scale, segs=segs, rings=rings)
        flatten(o)
        out.append((o, bone or "Bone_Chest"))
        return o

    def cyl(name, loc, r, h, color, verts=10, bone=None, rot=(0, 0, 0), scale=(1, 1, 1),
            smooth=True):
        o = kit.cyl(name, loc, r, h, color, verts=verts, rot=rot, scale=scale, smooth=smooth)
        flatten(o)
        out.append((o, bone or "Bone_Chest"))
        return o

    def cube(name, loc, size, color, bone, rot=(0, 0, 0), smooth=False):
        o = kit.box(name, loc, size, color, rot=rot, smooth=smooth)
        flatten(o)
        out.append((o, bone))
        return o

    def tube(name, p0, p1, r0, r1, color, bone, verts=8):
        o = limb_tube(name, p0, p1, r0, r1, color, verts=verts)
        flatten(o)
        out.append((o, bone))
        return o

    # ── 躯干：髋 / 腰 / 胸三段 ──
    # 为什么分三段而不是一个大胶囊：需求要的是"从上往下能读出躯干、腰、肩的起伏"。
    # 一个胶囊的俯视投影是一个圆，读不出腰；三段能给出"肩宽 → 腰收 → 髋"的读数。
    sphere("Part_Hips", g["hip_c"], g["hip_r"], "Cloth", g["hip_s"], 10, 6, "Bone_Hips")
    sphere("Part_Waist", g["waist_c"], g["waist_r"], "Scarf", g["waist_s"], 10, 6, "Bone_Spine")
    sphere("Part_Chest", g["chest_c"], g["chest_r"], "Cloth", g["chest_s"], 12, 7, "Bone_Chest")
    # 胸口的小披肩：俯视时在肩之间压出一条横向亮色，进一步把"肩"读出来
    sphere("Part_Mantle", (0.0, 0.0, 1.352), 0.138, "HatDark",
           (1.22, 0.92, 0.26), 10, 6, "Bone_Chest")

    # ── 颈 / 头 / 草帽 ──
    cyl("Part_Neck", (0.0, 0.0, 1.432), g["neck_r"], g["neck_h"], "Skin",
        verts=8, bone="Bone_Neck")
    sphere("Part_Head", g["head_c"], g["head_r"], "Skin", g["head_s"], 12, 8, "Bone_Head")
    # 帽 = 帽檐 + 帽筒 + 帽带。
    # ★ 帽檐半径 0.180 ÷ 头半宽 0.168 = 1.07，**满足需求建议的 ≤1.2** ★
    # 旧模型横向半径 0.360（帽子占了绝大部分）—— 新值不到旧值的一半。
    cyl("Part_HatBrim", g["hat_brim_c"], g["hat_brim_r"], 0.020, "Hat",
        verts=16, bone="Bone_Head", scale=g["hat_brim_s"], smooth=False)
    sphere("Part_HatCrown", g["hat_crown_c"], g["hat_crown_r"], "Hat",
           g["hat_crown_s"], 12, 7, "Bone_Head")
    cyl("Part_HatBand", g["hat_band_c"], g["hat_band_r"], 0.030, "HatDark",
        verts=16, bone="Bone_Head", scale=g["hat_band_s"], smooth=False)
    # 帽顶的小芽（保留"种落小灵"的辨识度，但不是帽子的一部分，不参与遮挡判据）
    sphere("Part_Sprout", (0.0, -0.052, 1.792), 0.030, "Sprout", (0.55, 0.55, 1.55),
           6, 4, "Bone_Head")

    # ── 围巾 ──
    sphere("Part_Scarf", g["scarf_c"], g["scarf_r"], "Scarf", g["scarf_s"], 10, 6, "Bone_Chest")

    # ── 肩 + 手臂（A-pose，与骨架一致）──
    for s, side in ((1.0, "L"), (-1.0, "R")):
        sb = BONE_NAME[f"Bone_Shoulder_{side}"]
        ub = BONE_NAME[f"Bone_UpperArm_{side}"]
        lb = BONE_NAME[f"Bone_LowerArm_{side}"]
        hb = BONE_NAME[f"Bone_Hand_{side}"]
        # 肩帽：一个球，和骨架的肩骨同高 —— 俯视时它就是"肩"这个读数
        sphere(f"Part_Shoulder_{side}",
               ((sb[1][0] + sb[2][0]) * 0.5, 0.0, (sb[1][2] + sb[2][2]) * 0.5),
               g["shoulder_r"], "Cloth", (1.00, 0.90, 0.86), 10, 6, f"Bone_Shoulder_{side}")
        tube(f"Part_UpperArm_{side}", ub[1], ub[2], 0.062, 0.052, "Skin", f"Bone_UpperArm_{side}")
        tube(f"Part_LowerArm_{side}", lb[1], lb[2], 0.052, 0.046, "Skin", f"Bone_LowerArm_{side}")
        hx = (hb[1][0] + hb[2][0]) * 0.5
        hz = (hb[1][2] + hb[2][2]) * 0.5
        sphere(f"Part_Hand_{side}", (hx, 0.0, hz), g["hand_r"], "Skin",
               (0.92, 0.86, 1.00), 8, 5, f"Bone_Hand_{side}")

    # ── 腿 + 脚 ──
    for s, side in ((1.0, "L"), (-1.0, "R")):
        tb = BONE_NAME[f"Bone_Thigh_{side}"]
        cb = BONE_NAME[f"Bone_LowerLeg_{side}"]
        fb = BONE_NAME[f"Bone_Foot_{side}"]
        tube(f"Part_Thigh_{side}", tb[1], tb[2], g["thigh_r"], g["calf_r"], "Cloth",
             f"Bone_Thigh_{side}")
        tube(f"Part_Calf_{side}", cb[1], cb[2], g["calf_r"], 0.050, "Cloth",
             f"Bone_LowerLeg_{side}")
        cube(f"Part_Foot_{side}", (fb[1][0], -0.050, 0.036), g["foot_s"], "Bag",
             f"Bone_Foot_{side}")

    # ── 接地阴影（并进同一个网格件，挂在 Hips 上，随角色走）──
    cyl("Part_Shadow", (0.0, 0.0, 0.006), 0.240, 0.011, "Ink", verts=16,
        bone="Bone_Hips", scale=(1.0, 1.0, 1.0), smooth=False)
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

    ★ 两个坑，都会让整个模型"长高/跑偏"，而且不报任何错 ★
      ① `parent_set(type='BONE')` 需要"当前活动骨骼"，否则抛 `No active bone`。
         活动骨骼只能在 **POSE 模式**下设置（EDIT 模式重建骨骼、OBJECT 模式没有活动骨骼）。
      ② **绑完之后绝对不要再写 `obj.parent_bone = bone_name`。**
         运算符内部的 keep_transform 会同时算出 `matrix_parent_inverse` 与
         `parent_bone`，而这个 inverse 是按"**骨骼尾部(tail)**"这一帧算的。
         事后重写 `parent_bone` 会让 inverse 与新的父帧不一致，表现就是**每件都沿骨骼
         长度方向整体平移一个骨长**（实测：肩从 z=1.41 漂到 2.70、影子 y 从 0 漂到 0.78、
         身高从 1.80 变成 2.89），缩放却仍是 1.0 —— 所以从"缩放正常"完全看不出问题。
         一次只绑一件时它**碰巧是对的**，所以单件测试也验证不出来，必须在整批里量。
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

    ★ 为什么不用 `obj.bound_box` ★
    骨头父子绑定过的物体，`bound_box`（对象空间包围盒）在依赖图求值时机不对时会给出
    过期值；顶点坐标是几何真值，任何时候都可信。旧模型是单网格没有这个问题，
    换成骨架之后这个区别是"量出来 2.89m"和"真的 1.80m"的区别。
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
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    return ((min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs)))


def dims_of(pts):
    (x0, x1), (y0, y1), (z0, z1) = extents(pts)
    return (x1 - x0, y1 - y0, z1 - z0)


def check_assembly(arm, meshes):
    """装配自检：每件网格的**实际**世界位置必须和它建模时写的位置一致。

    这是"骨头父子绑定绑错了"唯一的可靠探针 —— 绑错的表现是每件都沿骨骼长度方向
    整体平移一个骨长（身高 1.80 → 2.89），而**缩放仍然是 1.0**、不报任何错。
    光看缩放或包围盒尺寸都发现不了。
    """
    bad = []
    for o in meshes:
        if not o.parent_bone:
            bad.append(f"{o.name}: 没有绑定骨骼")
            continue
        bone = arm.data.bones[o.parent_bone]
        # 网格件的几何中心应当落在**从骨骼头到骨骼尾的延长线附近**是过强的假设，
        # 真正不变量是：绑定不得改变世界位置。这里用"离自身骨骼的距离"做粗检 +
        # 用整体高度/脚底做硬检（下面 build_static 里）。
        d = (o.matrix_world.translation - bone.head_local).length
        if d > 1.6:
            bad.append(f"{o.name}→{o.parent_bone} 离骨骼头 {d:.3f}m（疑似漂移）")
    return bad


# ================================================================ 静态模型导出
def build_static():
    arm, meshes = assemble_player()
    tris = sum(kit.tri_count(m) for m in meshes)
    pts = mesh_points(meshes)
    (x0, x1), (y0, y1), (z0, z1) = extents(pts)
    dims = (x1 - x0, y1 - y0, z1 - z0)
    zmin = z0

    ratio = G["hat_brim_r"] / (G["head_r"] * G["head_s"][0])
    print(f"[PLAYER2] 静态 dims(x宽,y深,z高) = {tuple(round(v, 3) for v in dims)}  "
          f"目标高 {TARGET_H}")
    print(f"[PLAYER2] 三角面 = {tris}  预算 = {TRI_BUDGET}")
    print(f"[PLAYER2] 帽檐半径 {G['hat_brim_r']:.3f} ÷ 头半宽 "
          f"{G['head_r'] * G['head_s'][0]:.3f} = {ratio:.2f}  (目标 ≤ {BRIM_RATIO_TARGET})")
    print(f"[PLAYER2] zmin = {zmin:.4f}  zmax = {z1:.4f}")

    problems = []
    if abs(zmin) > 0.005:
        problems.append(f"脚底不在 z=0（zmin={zmin:.4f}）")
    if abs(dims[2] - TARGET_H) > 0.05:
        problems.append(f"高度偏离 1.8：{dims[2]:.3f}")
    if tris >= TRI_BUDGET:
        problems.append(f"面数超预算：{tris} ≥ {TRI_BUDGET}")
    if ratio > BRIM_RATIO_TARGET:
        problems.append(f"帽檐比例超了：{ratio:.2f} > {BRIM_RATIO_TARGET}")
    problems += check_assembly(arm, meshes)
    for p in problems:
        print(f"[PLAYER2] ★静态自检未过：{p}★")
    if not problems:
        print("[PLAYER2] 静态自检全部通过")

    path = os.path.join(CHAR_DIR, "player.fbx")
    kit.ensure_dir(path)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        # ★ 必须带 ARMATURE，否则骨架不在场景里、骨骼全丢 ★
        object_types={'MESH', 'ARMATURE', 'EMPTY'},
        mesh_smooth_type='FACE',
        path_mode='AUTO',
        embed_textures=False,
        bake_anim=False,
        add_leaf_bones=False,
        armature_nodetype='NULL',
        axis_forward='-Z',          # 轴向只处理一次
        axis_up='Y',
        use_space_transform=True,
    )
    drop_meta(path)
    print(f"[PLAYER2] 静态 -> {path}")
    return dict(tris=tris, dims=dims, zmin=zmin, ratio=ratio, path=path,
                problems=problems)


# ================================================================ 武器
def grip_frame():
    """算出 GRIP 该有的朝向。

    握法约定：**手柄（+Z）是主握轴，手背法线（+X）是次参考轴**。
    为什么次参考轴不能用"手掌轴"：手掌轴 (-0.3160, 0, -0.9494) 与竖起来的手柄
    （+Z）**几乎平行**（夹角 71.6°），拿它当参考等于没参考。真正垂直于手柄、
    能把"手是正着握还是反着握"定下来的量是**手掌平面法线**：

        palm_normal = normalize(haft × 手掌轴)      # 与 +Z 垂直，指向手掌外侧

    在 GRIP 的局部坐标系里，这条法线正好落在 ±X 上（+X = -Y × +Z），
    所以 GRIP 的朝向就是"让 +X 对准 palm_normal、+Z 对准手柄"的那一组基。
    这样武器挂到 `Bone_Hand_R` 上时，刃口/壶嘴的朝向和手是一致的，不用在 Unity 侧试角度。
    （`arm_axes()` 与这里都从 BONES 表推导，改骨架角度会自动跟着走。）
    """
    hb = BONE_NAME["Bone_Hand_R"]
    palm = (V(hb[2]) - V(hb[1])).normalized()     # 手腕 → 掌心
    haft = V((0.0, 0.0, 1.0))                     # 手柄朝上
    yv = palm - haft * palm.dot(haft)
    if yv.length < 1e-6:
        yv = V((0.0, -1.0, 0.0))
    yv.normalize()
    xv = yv.cross(haft).normalized()              # +X 是次轴 → 基准
    zv = xv.cross(yv).normalized()
    m = mathutils.Matrix(((xv.x, yv.x, zv.x),
                          (xv.y, yv.y, zv.y),
                          (xv.z, yv.z, zv.z)))
    return m.to_quaternion(), math.degrees(m.to_quaternion().angle), palm


def palm_normal():
    """手掌平面法线（世界空间，已正交化到与竖直手柄垂直）。"""
    hb = BONE_NAME["Bone_Hand_R"]
    palm = (V(hb[2]) - V(hb[1])).normalized()
    n = V((0.0, 0.0, 1.0)).cross(palm)
    return n.normalized() if n.length > 1e-6 else V((1.0, 0.0, 0.0))


def grip_axis_error(quat):
    """量 GRIP 的 +X 与手掌平面法线差多少度。**理想是 0**。

    第一版这里量的是 +Y 与手掌轴的夹角，恒得 71.6° —— 那不是握错了，
    是量错了轴（手掌轴本来就与竖起来的手柄近乎平行）。指标写错会把真结果读成故障，
    比不量更危险，所以这条注释留在代码里。
    """
    gx = (quat @ V((1.0, 0.0, 0.0))).normalized()
    return math.degrees(math.acos(max(-1.0, min(1.0, abs(palm_normal().dot(gx))))))


def build_tool(kind):
    """造一把武器：GRIP 在原点，工具沿 +Z 伸出。返回 (grip, meshes)。"""
    kit.reset()
    g, g_ang, palm = grip_frame()
    parts = []

    if kind == "hoe":
        # 锄头：木柄 1.1~1.3m + 金属锄刃。握点在柄上，柄从 GRIP 往上 1.25、向下 0.12。
        parts.append(kit.cyl("Tool_Haft", (0, 0, 0.505), 0.026, 1.25, "ToolHaft", verts=10))
        parts.append(kit.cyl("Tool_Collar", (0, 0, 1.108), 0.033, 0.055, "ToolMetal",
                             verts=10, smooth=False))
        # ★ 锄刃的朝向是这一版改的第二遍 ★
        #   第一版把刃做成"从柄顶往前上方翘出去的方板"（绕 X 转 +34°），
        #   结果刃的凹面朝后、看起来像一块悬空的桨，正面渲出来甚至与柄断开。
        #   真锄头的刃是：**从柄顶的箍往下、往后翻**的一条扁钢，刃口在最低最远处。
        #   所以绕 X 转 **-92°**（把方板立起来、往 -Y 前方翻下去），
        #   刃口再单独往前下方补一小块。刃宽 0.16（不是 0.175）—— 太宽会读成桨。
        # ★ 刃的位置：**必须挂在柄的后方（+Y）**，不是前方 ★
        #   第二版把刃心放在 y=-0.072（前方），渲出来是"一块悬在柄前面的板"——
        #   锄头的刃是从箍往下、朝**使用者这一侧（后方）**翻的，刃口在最下最前。
        #   所以刃心 y 收回到 -0.030（几乎在柄轴上），刃口那一小块才探到 -0.110。
        parts.append(kit.cone("Tool_Neck", (0.0, -0.028, 1.078), 0.030, 0.048, 0.090,
                              "ToolMetal", verts=8, rot=(D(62), 0, 0), smooth=False))
        parts.append(kit.box("Tool_Blade", (0.0, -0.030, 0.985), (0.030, 0.160, 0.300),
                             "ToolMetal", rot=(D(-92), 0, 0), smooth=False))
        parts.append(kit.box("Tool_BladeTip", (0.0, -0.110, 0.828), (0.022, 0.140, 0.075),
                             "ToolMetal", rot=(D(-104), 0, 0), smooth=False))
        parts.append(kit.cyl("Tool_GripWrap", (0, 0, 0.055), 0.030, 0.190, "Bag", verts=10))

    elif kind == "can":
        # 水壶：壶身 + 壶嘴 + 提手。总高约 0.30m（需求 0.3）。
        # 握持：手从上方抓提手，GRIP 在提手顶 —— 手的中心正好落在提手上。
        #
        # ★ 这一版重做的两处（第一版渲染出来是一团看不懂的东西）★
        #   ① 提手：第一版是 5 个球按 0.088 的弧长摊开，球半径只有 0.015 ——
        #      相邻球完全不重叠，渲出来是"空中飘着几粒米"。改成 **9 个球、半径 0.021、
        #      弧长 0.062**，相邻球重叠 60% 以上才连成一根提手。
        #   ② 壶嘴：第一版用 cone(rot=118°) + 端面圆盘，渲出来是一块斜插的方板加一块
        #      飘在旁边的板。改成"沿壶身下缘斜伸出去的锥筒 + 末端花洒盘"，两件同轴。
        parts.append(kit.cyl("Tool_Body", (0, 0, -0.070), 0.090, 0.150, "ToolCan", verts=14))
        parts.append(kit.sphere("Tool_BodyTop", (0, 0, 0.005), 0.090, "ToolCan",
                                scale=(1.0, 1.0, 0.30), segs=12, rings=6))
        parts.append(kit.sphere("Tool_BodyBot", (0, 0, -0.145), 0.090, "ToolCan",
                                scale=(1.0, 1.0, 0.26), segs=12, rings=6))
        parts.append(kit.cyl("Tool_Lip", (0, 0, 0.024), 0.094, 0.016, "Metal",
                             verts=14, smooth=False))
        # ★ 壶嘴的朝向：**从壶身下缘往上翘**，不是往下扎 ★
        #   第二版用 rot=-52°，渲出来壶嘴是朝**前下方**插进地里的。
        #   轴的换算：绕 X 转 θ，锥体的 +Z 轴变成 (0, -sinθ, cosθ)。
        #   要指向"前上方"= (0, -0.78, +0.62) → sinθ=+0.78 → **θ=+51°**（不是 -52°）。
        #   长度也加长到 0.20、根部加粗到 0.044 —— 0.18 长、0.040 粗在预览里读不出是嘴。
        parts.append(kit.cone("Tool_Spout", (0.0, -0.128, -0.048), 0.044, 0.019, 0.200,
                              "ToolCan", verts=10, rot=(D(51), 0, 0), smooth=False))
        # 花洒盘：落在壶嘴的**末端**。锥筒中心 (0,-0.128,-0.048)，轴 (0,-0.783,0.622)，
        # 半长 0.10 → 末端 = 中心 + 0.10×轴 = (0, -0.206, 0.014)。盘就套在那里。
        # 第三版错写成 (0,-0.206,0.014) 却把盘半径放大到 0.033 又没对齐轴，
        # 渲出来是"扣在壶嘴上方的一片盖子"；这里让盘与锥筒**严格同轴同点**。
        parts.append(kit.cyl("Tool_SpoutRose", (0.0, -0.206, 0.014), 0.030, 0.018,
                             "Metal", verts=10, rot=(D(51), 0, 0), smooth=False))
        # 提手：跨过壶顶的拱（球要够密才连得起来，见上面的说明）
        for i in range(9):
            u = i / 8.0
            a = math.pi * u
            parts.append(kit.sphere(f"Tool_Handle_{i}",
                                    (0.0, 0.070 * math.cos(a), 0.030 + 0.072 * math.sin(a)),
                                    0.021, "Metal", segs=8, rings=5))

    elif kind == "fert":
        # 肥料袋：麻袋，扎口的颈在 GRIP（手抓着袋口），袋身挂在下方。
        # 袋身总高 0.40m（需求 0.4）。
        parts.append(kit.sphere("Tool_Bag", (0.0, 0.0, -0.238), 0.150, "ToolBurlap",
                                scale=(1.0, 0.92, 1.28), segs=12, rings=8))
        parts.append(kit.cone("Tool_Neck", (0.0, 0.0, -0.052), 0.052, 0.088, 0.105,
                              "ToolBurlap", verts=10, smooth=False))
        parts.append(kit.cyl("Tool_Tie", (0.0, 0.0, -0.020), 0.058, 0.030, "Bag",
                             verts=10, smooth=False))
        parts.append(kit.cone("Tool_Mouth", (0.0, 0.0, 0.022), 0.062, 0.046, 0.055,
                              "ToolBurlap", verts=10, smooth=False))
        # ★ 袋内露出的肥料 ★
        #   第一版用一圈 "Seam" 圆柱想在袋身上做出缝线，实测**完全看不见**——
        #   那个圆柱的半径 0.152 与袋身最大半径几乎相同，整根埋进了袋子里。
        #   改成"从袋口露出的一撮肥料"：既看得见，又直接说明"这是个装肥料、能撒的东西"。
        #   ★ 必须落在袋口翻边（z=0.049）**下面**，否则会从口沿上穿出去，
        #     渲出来像一颗卡在瓶口的怪球（第三版就是这个症状）。
        for i, (dx, dy, dz, r) in enumerate(((0.0, 0.0, 0.028, 0.046),
                                             (0.032, -0.014, 0.020, 0.028),
                                             (-0.028, 0.018, 0.022, 0.026),
                                             (0.004, 0.030, 0.016, 0.024),
                                             (-0.016, -0.030, 0.018, 0.024))):
            parts.append(kit.sphere(f"Tool_Content_{i}", (dx, dy, dz), r, "ToolDirt",
                                    segs=6, rings=4))
        # 袋口翻边（比袋口大一圈，俯视时能看出"是开口的"）
        parts.append(kit.cyl("Tool_MouthRim", (0.0, 0.0, 0.049), 0.064, 0.014, "Bag",
                             verts=10, smooth=False))
    else:
        raise ValueError(kind)

    merged = kit.join(parts, f"TOOL_{kind}")
    kit.apply_transforms(merged)

    # ★ GRIP 节点必须是"干净"的：位置在原点、**旋转单位**、缩放 1 ★
    #   需求原文是"放一个空物体或顶点组，名字统一为 GRIP"，Unity 侧要拿它对齐手部骨骼。
    #   如果把握持朝向塞进 GRIP 自己的旋转里，Unity 侧就得再补一次逆旋转，
    #   每把武器都要算一遍，很容易对不齐。
    #   所以：**把朝向烘进网格顶点**（网格下标 = 握持坐标系），GRIP 保持 identity。
    #
    #   做法（每一步都有原因，别简化）：
    #     ① 顶点左乘 R_grip⁻¹ —— 把"世界朝向"的几何搬进"握持坐标系"
    #     ② GRIP 空物体摆在原点、旋转 identity
    #     ③ 直接把 **matrix_parent_inverse 设成单位阵**，而不是用
    #        `parent_set(keep_transform=True)` —— 后者会为了让世界变换不变而写一个
    #        非单位的 parent_inverse，于是 GRIP 又把 R_grip 加了回来，
    #        节点旋转仍然不是 identity（实测就是这样，量出来是 90°）。
    #        父矩阵设成单位阵时，matrix_local 恰等于我们烘进顶点的那个旋转，
    #        世界变换不变、网格不会跑位。
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

    # 自检：GRIP 必须是 identity（朝向已经烘进网格），且节点在原点
    gq = grip.rotation_quaternion
    grip_identity = (all(abs(a - b) < 1e-6 for a, b in zip(gq, mathutils.Quaternion()))
                     and grip.location.length < 1e-6)
    print(f"[PLAYER2] tool_{kind}: GRIP 节点={'原点 + identity ✔' if grip_identity else '★异常★'}")

    err = grip_axis_error(g)
    print(f"[PLAYER2] tool_{kind}: 三角面={kit.tri_count(merged)} "
          f"GRIP 转角={g_ang:.1f}° 手背法线差 {err:.1f}°")

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
    print(f"[PLAYER2] tool_{kind} -> {path}  dims={tuple(round(v,3) for v in dims)}")
    return dict(kind=kind, path=path, tris=kit.tri_count(merged), dims=dims,
                grip_yaw=math.degrees(g.angle), grip_err=err)


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
    """算出某侧手臂"相对躯干"的抬起 / 前摆轴，返回 (抬轴, 前轴, 内收轴)。

    为什么不能直接写 rotation_euler.x —— 手臂骨是**斜的**（A-pose），
    绕骨的本地 X 转不等于"抬手臂"，实测会变成一种斜向的翻滚。
    这里用几何求出两个真正的轴：
        · 抬起轴 = 竖直方向 × 上臂方向   → 绕它转 = 手臂在竖直平面里升降
        · 前摆轴 = 上臂方向 × 抬起轴     → 绕它转 = 手臂前后摆
    姿态函数全部用"抬起角 / 前摆角"这类**语义量**表达，而不是欧拉分量，
    这样以后改 A-pose 的角度，动画不用重写。
    """
    ub = BONE_NAME[f"Bone_UpperArm_{side}"]
    d = (V(ub[2]) - V(ub[1])).normalized()
    up = V((0.0, 0.0, 1.0))
    lift = up.cross(d)
    if lift.length < 1e-6:
        lift = V((1.0, 0.0, 0.0))
    lift.normalize()
    # 用"竖直方向绕抬起轴转过 90°"得到前摆轴，保证与 d 正交
    swing = d.cross(lift).normalized()
    return lift, swing


def apply_arm(arm, side, lift_deg=0.0, swing_deg=0.0, out_deg=0.0,
              lower=0.0, hand=(0.0, 0.0, 0.0), shoulder=(0.0, 0.0, 0.0)):
    """按语义量摆一条手臂。

    lift_deg  : 抬起（正 = 手臂往上抬）
    swing_deg : 前摆（正 = 手往前甩）
    out_deg   : 外展（正 = 远离躯干）
    lower     : 小臂相对大臂的弯曲（正 = 屈肘收手）
    hand      : 手腕的 (x,y,z) 附加欧拉（度）
    shoulder  : 肩骨的附加坡度（跟随大臂做一点，动作才不像"只有一根骨头在转"）
    """
    lift, swing = arm_axes(arm, side)
    pb_u = arm.pose.bones[f"Bone_UpperArm_{side}"]
    pb_l = arm.pose.bones[f"Bone_LowerArm_{side}"]
    pb_h = arm.pose.bones[f"Bone_Hand_{side}"]
    if shoulder != (0.0, 0.0, 0.0):
        add_rot(arm, f"Bone_Shoulder_{side}", *shoulder)
    # 抬起轴符号：左手朝 +Z 抬起时轴指向 +Y 一侧，右手相反 → 统一乘上侧别
    sgn = 1.0 if side == "L" else -1.0
    q = mathutils.Quaternion(lift, D(-lift_deg * sgn)) @ \
        mathutils.Quaternion(swing, D(swing_deg)) @ \
        mathutils.Quaternion(V((0.0, 1.0, 0.0)), D(-out_deg * sgn))
    pb_u.rotation_euler = q.to_euler('XYZ')
    if lower:
        pb_l.rotation_euler = (D(lower), 0.0, 0.0)
    if hand != (0.0, 0.0, 0.0):
        pb_h.rotation_euler = (D(hand[0]), D(hand[1]), D(hand[2]))


def ease_out(k):
    return 1.0 - (1.0 - k) * (1.0 - k)


def bump(t, a, b):
    """在 [a,b] 区间内的 0→1→0 平滑包络（正弦）。"""
    if t <= a or t >= b:
        return 0.0
    return math.sin(math.pi * (t - a) / (b - a))


def ramp(t, a, b):
    """[a,b] 内的 0→1 线性推进（越界钳制）。"""
    if b <= a:
        return 1.0 if t >= b else 0.0
    return max(0.0, min(1.0, (t - a) / (b - a)))


# ---------------------------------------------------------------- 姿态函数
def pose_player(anim, t, arm):
    """相位 t（循环动画传 [0,1)，一次性动画传 [0,1]）→ 骨骼姿态。

    纯函数，无随机（同 t 必得同姿态）。
    约定（与 gen_pig / gen_plants_biped 一致）：
      绕 X 正 = 身体前倾 / 头低下；  绕 Z 正 = 左右转；  绕 Y 正 = 左右侧倾
    """
    rest_all(arm)
    tau = 2.0 * math.pi
    # 🔴 呼吸相位 —— 三个动画共用同一条呼吸曲线，但**周期不同**：
    #     idle 的 t 在 96 帧里走一圈 → 1 圈 = 1 次呼吸 = 4s = 0.25Hz
    #     walk 的 t 也在 96 帧里走一圈，所以**两条曲线的呼吸部分完全一样**（都 4s 一次）。
    #    ⚠ 第一版 walk 用 16 帧的 t，却把呼吸写成 `breathe = br = sin(2πt)`
    #      （与步相位 st 是同一个函数），于是 `-5.0*breathe + 3.2*sin(2πt)` 两项同频反相，
    #      合成后塌成 `-1.8*sin(2πt)` —— **呼吸被自己抵消掉了**，
    #      量出来"呼吸层 = 1.41Hz"。写成两个数不等于就是两个频率，相位必须真的错开。
    bp = math.sin(tau * t)
    br = bp
    st = math.sin(tau * t * (ANIMS["walk"][0] / float(WALK_STEP_FRAMES)))
    breathe = 0.5 + 0.5 * bp
    breathe = breathe ** 1.35

    if anim == "idle":
        # ── 强烈呼吸起伏（需求原话"强烈的呼吸感起伏"）──
        # 拆成三层，全部由**同一个** br 驱动、同一个 0.25Hz 周期：
        #   ① 胸腔抬升：躯干在竖直方向整体起伏（有位移，不只是转一点角度）
        #   ② 脊柱伸展：Chest 绕 X 的后仰角随呼吸变化（正面看得出胸口起伏）
        #   ③ 肩部抬落：双肩随呼吸上下，俯视时肩线在"张—收"之间变化
        # 这三个加起来才叫"一眼能看出角色在活着"；只做①会像在点头。
        arm.pose.bones["Bone_Chest"].location = (0.0, 0.0, 0.022 * br)
        arm.pose.bones["Bone_Spine"].location = (0.0, 0.0, 0.010 * br)
        add_rot(arm, "Bone_Chest", x=-4.6 * br)
        add_rot(arm, "Bone_Spine", x=-2.2 * br)
        add_rot(arm, "Bone_Hips", x=0.9 * br)          # 髋随呼吸极轻地前后倒
        add_rot(arm, "Bone_Neck", x=2.6 * br)
        # 头：呼吸节拍 + 一个更慢的左右看（0.5 倍频 = 0.125Hz，刻意与呼吸错开）
        add_rot(arm, "Bone_Head", x=-3.4 * br, z=5.5 * math.sin(tau * t * 0.5))
        # 肩：抬落 + 极轻外张
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Shoulder_{side}", x=-3.2 * br, z=(1.6 * br if side == "L"
                                                                 else -1.6 * br))
        # 手臂：贴身轻晃，**两侧同相**（反相看起来像在走路）
        for side in ("L", "R"):
            apply_arm(arm, side, lift_deg=4.2 * br, swing_deg=1.8 * br,
                      out_deg=2.4 * br, lower=3.0 * br)
        # 腿：站着不能踏步。只做 1.2° 的重心微调。
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Thigh_{side}", x=0.7 * br)
            add_rot(arm, f"Bone_LowerLeg_{side}", x=-1.2 * br)

    elif anim == "walk":
        # ── 两个不同频率叠加（需求：呼吸 ≈0.25Hz、步伐 ≈1.5Hz，不要同频）──
        # 步相位 st 每帧走 1/16 圈 = 1.5Hz；呼吸相位每帧走 1/96 圈 = 0.25Hz。
        # 因为 public 循环长度不同，走一遍 walk 正好有 6 步、只完成 1 次呼吸 → 6:1。
        breathe = br          # 0.25Hz（与 idle 同一条曲线，速率不同）
        # 腿：左右反相 ±27°
        add_rot(arm, "Bone_Thigh_R", x=27.0 * st)
        add_rot(arm, "Bone_Thigh_L", x=-27.0 * st)
        # 膝：后摆的那条腿屈膝（走路必须有膝，否则像钟摆）
        add_rot(arm, "Bone_LowerLeg_R", x=-20.0 * max(0.0, -st))
        add_rot(arm, "Bone_LowerLeg_L", x=-20.0 * max(0.0, st))
        # 脚踝：跟随小腿做一点回弹
        add_rot(arm, "Bone_Foot_R", x=9.0 * st)
        add_rot(arm, "Bone_Foot_L", x=-9.0 * st)
        # 髋：每步一次起伏 + 呼吸的慢抬升（0.25Hz）
        arm.pose.bones["Bone_Hips"].location = (0.0, 0.0, 0.026 * abs(st) + 0.010 * bp)
        add_rot(arm, "Bone_Hips", x=2.4 * math.sin(tau * t * 2.0),
                y=2.2 * st, z=-3.4 * st)
        # 躯干：步频的前后颠（1.5Hz） + **呼吸的慢后仰（0.25Hz）**
        # ★ 呼吸只放在 Spine.x ★ 第一版同时叠在 Spine.x 与 Chest.x 上，
        #   结果 Chest.x 的主频被 1.5Hz 的前后颠吃掉，量出来是"呼吸层 = 1.41Hz"。
        #   物理上也对：走路时跨步的颠簸让胸腔在 1.5Hz 抖，呼吸体现在**肩线的慢起落**上。
        #   Chest 只留慢速的竖直抬升（location），不参与高频旋转。
        arm.pose.bones["Bone_Chest"].location = (0.0, 0.0, 0.016 * bp)
        add_rot(arm, "Bone_Spine", x=-7.0 * bp + 3.2 * math.sin(tau * t),
                y=1.6 * st, z=2.6 * st)
        add_rot(arm, "Bone_Chest", x=1.0 * math.sin(tau * t * 2.0), y=1.2 * st, z=1.8 * st)
        add_rot(arm, "Bone_Neck", x=-1.8 * bp)
        add_rot(arm, "Bone_Head", x=-2.8 - 2.2 * math.sin(tau * t * 2.0) + 2.2 * bp,
                z=-4.2 * st)
        # 肩：呼吸抬落（走的时候肩也在呼吸）
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Shoulder_{side}", x=-3.0 * bp)
        # 手臂：与腿反相摆 + 呼吸的抬落
        apply_arm(arm, "R", lift_deg=19.0 * (-st) + 3.4 * bp,
                  swing_deg=19.0 * (-st), out_deg=3.0 * abs(st), lower=16.0 + 8.0 * abs(st))
        apply_arm(arm, "L", lift_deg=19.0 * st + 3.4 * bp,
                  swing_deg=19.0 * st, out_deg=3.0 * abs(st), lower=16.0 + 8.0 * abs(st))

    elif anim == "hit":
        # 受击后仰（24 帧 = 1s）。**只给姿态**：游戏侧已有"膨胀 + 闪白"，
        # 这里绝不做缩放也不做染色（需求明确点了这一条）。
        k = math.sin(math.pi * min(1.0, t / 0.55)) if t < 0.55 else 0.0
        rec = ease_out(ramp(t, 0.55, 1.0))        # 回正
        kk = k * (1.0 - rec)
        # 后仰 + 后退半步 + 手腕护住
        add_rot(arm, "Bone_Hips", x=-7.0 * kk)
        add_rot(arm, "Bone_Spine", x=-11.0 * kk)
        add_rot(arm, "Bone_Chest", x=-13.0 * kk, z=6.0 * kk)
        add_rot(arm, "Bone_Neck", x=-2.0 * kk)
        add_rot(arm, "Bone_Head", x=-19.0 * kk, z=9.0 * kk)
        apply_arm(arm, "R", lift_deg=-14.0 * kk, swing_deg=-16.0 * kk,
                  out_deg=13.0 * kk, lower=26.0 * kk)
        apply_arm(arm, "L", lift_deg=-10.0 * kk, swing_deg=-13.0 * kk,
                  out_deg=15.0 * kk, lower=30.0 * kk)
        # 腿：受击时前后撑一下（受力反馈），幅度小、不是踏步
        add_rot(arm, "Bone_Thigh_R", x=-5.0 * kk)
        add_rot(arm, "Bone_Thigh_L", x=6.0 * kk)
        add_rot(arm, "Bone_LowerLeg_R", x=-4.0 * kk)
        add_rot(arm, "Bone_LowerLeg_L", x=-6.0 * kk)

    elif anim == "swap":
        # 切换武器：收起当前 → 抽出下一把。9 帧 = 0.375s（需求 0.3~0.4s）。
        # **只上半身**（玩家可能在跑动中换武器）。
        # 手往腰侧一沉 → 换手 → 抬回：读起来就是"收一把、抽一把"。
        if t < 0.45:
            u = ramp(t, 0.0, 0.45)
            k = math.sin(math.pi * 0.5 * u)                      # 缓入
        else:
            u = ramp(t, 0.45, 1.0)
            k = 1.0 - ease_out(u)
        add_rot(arm, "Bone_Spine", x=4.0 * k)
        add_rot(arm, "Bone_Chest", x=7.0 * k, z=-9.0 * k)
        add_rot(arm, "Bone_Neck", x=-3.0 * k)
        add_rot(arm, "Bone_Head", x=5.0 * k, z=-11.0 * k)
        # 右手下沉到腰侧再抬回（k 是 0→1→0 的包络）
        apply_arm(arm, "R", lift_deg=-30.0 * k, swing_deg=-24.0 * k,
                  out_deg=-8.0 * k, lower=48.0 * k, hand=(0.0, 0.0, -18.0 * k))
        apply_arm(arm, "L", lift_deg=-9.0 * k, swing_deg=-14.0 * k,
                  out_deg=6.0 * k, lower=22.0 * k)
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Shoulder_{side}", x=5.0 * k)

    elif anim.startswith("attack_"):
        _pose_attack(anim, t, arm)

    else:
        raise ValueError(anim)


def _pose_attack(anim, t, arm):
    """三把武器各一套专属攻击。**只动上半身**（见 UPPER_ONLY 的说明）。

    三套必须彼此不同，所以各自的"发力源"刻意选在不同关节：
        hoe  → 肩 + 躯干大幅折叠（重量在躯干）
        can  → 手腕 + 小臂为主，躯干几乎不动（重量在手）
        fert → 大臂外摆横扫，躯干侧转（重量在肩的横向）
    这样即使把武器模型藏起来，光看上半身剪影也能分出是哪一把。
    """
    if anim == "attack_hoe":
        # 抡起 → 劈下（36 帧 = 1.5s）：大幅、有重量。
        if t < 0.42:                       # 前摇：抬到身后
            k = ramp(t, 0.0, 0.42)
            e = math.sin(math.pi * 0.5 * k)
            lift, swing, lean, twist, lower = -86.0 * e, -34.0 * e, -15.0 * e, 16.0 * e, 62.0 * e
        elif t < 0.60:                     # 劈：快速下砸
            k = (t - 0.42) / 0.18
            lift = -86.0 + 168.0 * k
            swing = -34.0 + 62.0 * k
            lean = -15.0 + 40.0 * k
            twist = 16.0 - 26.0 * k
            lower = 62.0 - 46.0 * k
        else:                              # 回收：缓出回到站姿
            k = ease_out(ramp(t, 0.60, 1.0))
            lift = 82.0 * (1.0 - k)
            swing = 28.0 * (1.0 - k)
            lean = 25.0 * (1.0 - k)
            twist = -10.0 * (1.0 - k)
            lower = 16.0 * (1.0 - k)
        add_rot(arm, "Bone_Spine", x=lean * 0.55, z=twist * 0.45)
        add_rot(arm, "Bone_Chest", x=lean * 0.75, z=twist * 0.65)
        add_rot(arm, "Bone_Neck", x=-lean * 0.20)
        add_rot(arm, "Bone_Head", x=lean * 0.50, z=twist * 0.30)
        apply_arm(arm, "R", lift_deg=lift / 1.35 + 6.0, swing_deg=swing * 0.85,
                  out_deg=10.0, lower=lower, hand=(14.0, 0.0, -12.0))
        apply_arm(arm, "L", lift_deg=lift * 0.26, swing_deg=swing * 0.30,
                  out_deg=18.0, lower=lower * 0.55)
        for side in ("L", "R"):
            add_rot(arm, f"Bone_Shoulder_{side}",
                    x=(-8.0 if side == "R" else -4.0) * (lift / 86.0),
                    z=(6.0 if side == "R" else -3.0) * (lift / 86.0))

    elif anim == "attack_can":
        # 倾斜壶身、倒下（小幅、**腕部为主**）。躯干几乎不动 —— 这是与 hoe 的分界。
        if t < 0.46:
            k = ramp(t, 0.0, 0.46)
            e = ease_out(k)
            tilt, lift, swing, lower = 72.0 * e, 30.0 * e, -22.0 * e, 44.0 * e
            lean = -4.0 * e
        else:
            k = ease_out(ramp(t, 0.46, 1.0))
            tilt = 72.0 * (1.0 - k)
            lift = 30.0 * (1.0 - k)
            swing = -22.0 * (1.0 - k)
            lower = 44.0 * (1.0 - k)
            lean = -4.0 * (1.0 - k)
        add_rot(arm, "Bone_Spine", x=lean * 0.5)
        add_rot(arm, "Bone_Chest", x=lean, z=-4.0 * (tilt / 72.0))
        add_rot(arm, "Bone_Head", x=-3.0 * (tilt / 72.0))
        # can 的辨识度全在手腕：腕绕 X 大角度翻转（就是"把壶倒过来"）
        apply_arm(arm, "R", lift_deg=lift, swing_deg=swing, out_deg=12.0,
                  lower=lower, hand=(tilt, 0.0, -6.0))
        apply_arm(arm, "L", lift_deg=6.0, swing_deg=-6.0, out_deg=8.0, lower=12.0)
        add_rot(arm, "Bone_Shoulder_R", x=-4.0 * (tilt / 72.0))

    elif anim == "attack_fert":
        # 抓一把往前撒（**手臂外摆**）。发力源在肩的横向外展 —— 与另外两套都不同。
        if t < 0.30:                       # 探身抓肥
            k = ramp(t, 0.0, 0.30)
            e = ease_out(k)
            out_d, lift, swing, lean, twist = -30.0 * e, -34.0 * e, 26.0 * e, 20.0 * e, -18.0 * e
        elif t < 0.56:                     # 外摆撒出
            k = (t - 0.30) / 0.26
            out_d = -30.0 + 96.0 * k
            lift = -34.0 + 78.0 * k
            swing = 26.0 - 78.0 * k
            lean = 20.0 - 32.0 * k
            twist = -18.0 + 52.0 * k
        else:
            k = ease_out(ramp(t, 0.56, 1.0))
            out_d = 66.0 * (1.0 - k)
            lift = 44.0 * (1.0 - k)
            swing = -52.0 * (1.0 - k)
            lean = -12.0 * (1.0 - k)
            twist = 34.0 * (1.0 - k)
        add_rot(arm, "Bone_Spine", x=lean * 0.5, z=twist * 0.5)
        add_rot(arm, "Bone_Chest", x=lean * 0.8, z=twist * 0.7)
        add_rot(arm, "Bone_Neck", x=-lean * 0.18)
        add_rot(arm, "Bone_Head", x=lean * 0.45, z=twist * 0.35)
        apply_arm(arm, "R", lift_deg=lift, swing_deg=swing, out_deg=out_d,
                  lower=22.0 + 14.0 * abs(math.sin(math.pi * t)),
                  hand=(-10.0, 0.0, 8.0))
        apply_arm(arm, "L", lift_deg=lift * 0.35, swing_deg=swing * 0.3,
                  out_deg=out_d * 0.30, lower=18.0)
        add_rot(arm, "Bone_Shoulder_R", x=-6.0 * (lift / 40.0), z=10.0 * (out_d / 66.0))


# ================================================================ 动画导出
def collect_bones(arm, anim):
    """本动画要打关键帧的骨骼清单。

    ★ 腿骨一定要在里面 ★ 需求要的是"腿部曲线存在但为零"，所以腿骨要打关键帧，
    只是值恒为 0（pose_player 对 attack 根本不碰它们，rest_all 已经把值压回 0）。
    """
    names = [b[0] for b in BONES]
    return [arm.pose.bones[n] for n in names]


def export_anim(anim, frames, loop):
    """导出一条动画。**每次重新搭一次角色**（照 gen_pig / gen_plants_biped 的做法）。

    ★ 不要复用同一个骨架 ★ Blender 5 的 action 是挂在"槽位"上的，换 action 之后
    槽位还停在上一条绑定上，实测后果是一个 FBX 里同时写进两条 take，Unity 侧互相覆盖。
    重建一次骨架只要零点几秒，换来的是"一个 FBX 一条 take"这个铁律。
    """
    arm, meshes = assemble_player()
    scene = bpy.context.scene
    total = frames + 1 if loop else frames
    scene.frame_start = 1
    scene.frame_end = total

    pbs = collect_bones(arm, anim)
    shared = bpy.data.actions.new(f"player_{anim}")
    arm.animation_data_create()
    arm.animation_data.action = shared

    for f in range(1, total + 1):
        if loop:
            t = (f - 1) / float(frames)
            if f == total:
                t = 0.0            # 收尾帧 = 首帧，loopTime 才无缝
        else:
            t = (f - 1) / float(max(1, total - 1))
        # ★ 先 frame_set，再摆姿态，最后打关键帧 —— 顺序不能换 ★
        scene.frame_set(f)
        pose_player(anim, t, arm)
        for pb in pbs:
            pb.keyframe_insert(data_path="rotation_euler")
        # 位移只给真正需要位移的骨骼打（躯干起伏 / 髋起伏），
        # 腿骨**不打 location** —— 省下的曲线是白拿的，而且腿本来就不该有位移。
        for pb in pbs:
            if pb.name in ("Bone_Hips", "Bone_Spine", "Bone_Chest"):
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
    print(f"[PLAYER2] {anim}: {total} 帧 loop={loop} {mode} -> {os.path.basename(path)}")
    return path


def build_anims(only=None):
    out = []
    for anim, (frames, loop, _note) in ANIMS.items():
        if only and anim not in only:
            continue
        out.append(export_anim(anim, frames, loop))
    return out


# ================================================================ 入口参数解析
def _args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


USAGE = """用法:
  blender -b --factory-startup -P gen_player_v2.py -- all
  blender -b --factory-startup -P gen_player_v2.py -- static
  blender -b --factory-startup -P gen_player_v2.py -- tools
  blender -b --factory-startup -P gen_player_v2.py -- anim [名字...]
  blender -b --factory-startup -P gen_player_v2.py -- verify
  blender -b --factory-startup -P gen_player_v2.py -- preview
动画名: """ + ", ".join(ANIMS)


def main():
    a = _args()
    if not a:
        print(USAGE)
        return
    cmd = a[0]
    if cmd == "static":
        build_static()
    elif cmd == "tools":
        build_tools()
    elif cmd == "anim":
        build_anims(a[1:] or None)
    elif cmd == "all":
        build_static()
        build_tools()
        build_anims()
    elif cmd == "verify":
        import verify_player_v2 as vp
        vp.verify()
    elif cmd == "preview":
        import verify_player_v2 as vp
        vp.render_previews()
    else:
        print(USAGE)


if __name__ == "__main__":
    main()
