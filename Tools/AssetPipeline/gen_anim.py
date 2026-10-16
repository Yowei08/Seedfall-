# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 动画生成器
================================
把角色搭成"可动木偶"再程序化打关键帧——不建骨骼、不做蒙皮。
理由：参考美术本身就是"刚性部件在动"的 chibi 风格，关节动画完全够用，
而且它可复现（纯函数：相位 → 姿态），比手 K 骨骼动画稳得多。

层级（每个部件的枢轴都在它的关节上，这样旋转才像肢体而不是绕场地中心画圈）：
    Root (Empty，脚底)
      ├─ Body           躯干 + 围巾 + 背包 + 护甲 + 接地阴影
      │    ├─ Head      头 + 眼 + 帽 + 角 + 呆毛
      │    ├─ Arm_R     右臂 + 武器
      │    └─ Arm_L     左臂
      ├─ Leg_R
      └─ Leg_L

产出：Assets/Resources/Art/Anim/<角色>_<动画>.fbx（每个动画一个文件，见 manifest 的取舍说明）
"""

import os
import sys
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit        # noqa: E402
import gen_characters as gc       # noqa: E402

ANIM_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\Anim"

# 动画表：名字 → (帧数, 是否循环)
ANIMS = {
    "idle":   (48, True),
    "walk":   (24, True),
    "attack": (18, False),
    "hit":    (12, False),
    "death":  (36, False),
}

# 部件名 → 身体分组（按名字前缀归类，和 build_chibi 的命名一一对应）
def group_of(name):
    if name.startswith(("Head", "Eye_", "Hat_", "Hood", "Sprout_", "Horn_")):
        return "Head"
    if name.startswith(("Arm_R", "Hoe_", "Club_", "Staff_", "BigClub_")):
        return "Arm_R"
    if name.startswith("Arm_L"):
        return "Arm_L"
    if name.startswith(("Leg_R", "Foot_R")):
        return "Leg_R"
    if name.startswith(("Leg_L", "Foot_L")):
        return "Leg_L"
    return "Body"      # 躯干 / 围巾 / 背包 / 护甲 / 翅膀 / 尾巴 / 影子


def set_parent(child, parent):
    """带保持世界变换的父子绑定（直接赋 .parent 会让子物体跳位）。"""
    bpy = kit.bpy
    bpy.ops.object.select_all(action='DESELECT')
    child.select_set(True)
    parent.select_set(True)
    bpy.context.view_layer.objects.active = parent
    bpy.ops.object.parent_set(type='OBJECT', keep_transform=True)


def build_puppet(char_id):
    """按分组装配木偶，返回 {组名: 对象} 与 cfg。"""
    bpy = kit.bpy
    cfg = dict(gc.CHARACTERS[char_id])
    kit.reset()
    parts, joints = gc.build_chibi(cfg, puppet=True)

    # 分组合并：每个组一个网格，枢轴落在该组的关节上
    buckets = {}
    for p in parts:
        buckets.setdefault(group_of(p.name), []).append(p)

    objs = {}
    for group, members in buckets.items():
        merged = kit.join(members, f"{group}_{char_id}")
        kit.apply_transforms(merged)
        if group in joints:
            kit.set_origin(merged, joints[group])   # ★ 枢轴 = 关节
        objs[group] = merged

    # Root 空物体（脚底），承载"整体倒地"这类全身动作
    bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
    root = bpy.context.active_object
    root.name = f"Root_{char_id}"

    # ★ 轴修正父节点 ★
    # Blender 是 Z-up、Unity 是 Y-up。修正在这里放**一层不参与动画的空物体**，
    # 而不是放在 Root 上：Root 每帧都被打关键帧，会把 -90° 的修正覆盖掉，
    # 结果就是动画版本躺平（静态版本没这问题，因为它把 Y-up 烘进了网格）。
    bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
    axis_fix = bpy.context.active_object
    axis_fix.name = f"AxisFix_{char_id}"
    axis_fix.rotation_euler = (math.radians(-90.0), 0.0, 0.0)
    set_parent(root, axis_fix)

    for g in ("Body", "Leg_R", "Leg_L"):
        if g in objs:
            set_parent(objs[g], root)
    for g in ("Head", "Arm_R", "Arm_L"):
        if g in objs and "Body" in objs:
            set_parent(objs[g], objs["Body"])

    return objs, root, cfg


# ---------------------------------------------------------------- 姿态函数
# 全部在**Blender 坐标系**里写：X = 右，Y = 前后（-Y 是角色正面），Z = 上
# 绕 X 旋转 = 前后俯仰；绕 Z 旋转 = 左右摆头/扭身；绕 Y 旋转 = 侧倾
D = math.radians


def pose(anim, t, objs, root, cfg):
    """相位 t∈[0,1) → 各部件姿态。纯函数，无随机。"""
    tau = 2.0 * math.pi

    for o in list(objs.values()) + [root]:
        o.rotation_euler = (0.0, 0.0, 0.0)
        o.location = (0.0, 0.0, 0.0)

    head = objs.get("Head")
    body = objs.get("Body")
    armR = objs.get("Arm_R")
    armL = objs.get("Arm_L")
    legR = objs.get("Leg_R")
    legL = objs.get("Leg_L")

    if anim == "idle":
        # 呼吸：身体轻微上下 + 头微俯仰 + 双臂微摆
        bob = 0.012 * math.sin(tau * t)
        if body:
            body.location = (0.0, 0.0, bob)
        if head:
            head.rotation_euler = (D(2.5) * math.sin(tau * t + 0.6), 0.0, D(3.0) * math.sin(tau * t * 0.5))
        if armR:
            armR.rotation_euler = (D(4.0) * math.sin(tau * t), 0.0, D(-3.0) * math.sin(tau * t))
        if armL:
            armL.rotation_euler = (D(-4.0) * math.sin(tau * t), 0.0, D(3.0) * math.sin(tau * t))

    elif anim == "walk":
        # 走：双腿反相摆动 + 双臂反向摆 + 身体上下两拍 + 轻微扭身
        swing = math.sin(tau * t)
        if legR:
            legR.rotation_euler = (D(26.0) * swing, 0.0, 0.0)
        if legL:
            legL.rotation_euler = (D(-26.0) * swing, 0.0, 0.0)
        if armR:
            armR.rotation_euler = (D(-20.0) * swing, 0.0, 0.0)
        if armL:
            armL.rotation_euler = (D(20.0) * swing, 0.0, 0.0)
        if body:
            body.location = (0.0, 0.0, 0.022 * abs(math.sin(tau * t)))
            body.rotation_euler = (D(4.0), 0.0, D(4.0) * swing)
        if head:
            head.rotation_euler = (D(-3.0), 0.0, D(-3.0) * swing)

    elif anim == "attack":
        # 挥击：抬臂蓄力 → 快速下劈 → 收势
        if t < 0.35:
            k = t / 0.35
            a = -D(72.0) * k                      # 抬起来
        elif t < 0.55:
            k = (t - 0.35) / 0.20
            a = -D(72.0) + D(120.0) * k           # 劈下去
        else:
            k = (t - 0.55) / 0.45
            a = D(48.0) * (1.0 - k)
        if armR:
            armR.rotation_euler = (a, 0.0, 0.0)
        lean = math.sin(math.pi * min(1.0, t / 0.55)) * D(12.0)
        if body:
            body.rotation_euler = (lean, 0.0, 0.0)
        if head:
            head.rotation_euler = (lean * 0.5, 0.0, 0.0)
        if armL:
            armL.rotation_euler = (-lean * 0.6, 0.0, 0.0)

    elif anim == "hit":
        # 受击：向后仰 + 下沉，再回弹
        k = math.sin(math.pi * min(1.0, t / 0.7)) if t < 0.7 else 0.0
        if body:
            body.rotation_euler = (D(-16.0) * k, 0.0, 0.0)
            body.location = (0.0, 0.0, -0.03 * k)
        if head:
            head.rotation_euler = (D(-22.0) * k, 0.0, 0.0)
        if armR:
            armR.rotation_euler = (D(24.0) * k, 0.0, 0.0)
        if armL:
            armL.rotation_euler = (D(24.0) * k, 0.0, 0.0)

    elif anim == "death":
        # 倒地：绕脚底向后翻倒 + 下沉，之后不动
        k = min(1.0, t / 0.45)
        ease = 1.0 - (1.0 - k) * (1.0 - k)        # 缓出
        if root:
            root.rotation_euler = (D(-82.0) * ease, 0.0, 0.0)
            root.location = (0.0, 0.0, -0.05 * ease)
        if head:
            head.rotation_euler = (D(-14.0) * ease, 0.0, 0.0)


# ---------------------------------------------------------------- 入口
def export_anim(char_id, anim, frames, loop):
    bpy = kit.bpy
    objs, root, cfg = build_puppet(char_id)
    scene = bpy.context.scene
    scene.frame_start = 1
    scene.frame_end = frames

    n = frames if loop else frames
    # ★ 让全部部件共用**同一条动作** ★
    # 每个对象各自一条 action 时，Blender 会为每条 action 写一个 take，
    # Unity 侧于是得到 7 个片段（每个部件一个）而不是 1 个完整动画。
    # 共用一条 action（Blender 4.4+ 用 slot 区分各对象）才会写出单个 take。
    shared = bpy.data.actions.new(f"{char_id}_{anim}")
    for o in list(objs.values()) + [root]:
        o.animation_data_create()
        o.animation_data.action = shared

    for f in range(1, n + 1):
        t = (f - 1) / float(frames) if loop else (f - 1) / float(max(1, frames - 1))
        pose(anim, t, objs, root, cfg)
        scene.frame_set(f)
        for o in list(objs.values()) + [root]:
            o.keyframe_insert(data_path="location")
            o.keyframe_insert(data_path="rotation_euler")

    # 整体 Z-up → Y-up 由 AxisFix 父节点承担（见 build_puppet），
    # 这里不再动 Root——Root 的旋转被逐帧关键帧覆盖，放修正会被抹掉。
    path = os.path.join(ANIM_DIR, f"{char_id}_{anim}.fbx")
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
        # 实测：本项为 False 时导出器**一条曲线都不写**（FBX 里 AnimCurveNode 出现 0 次），
        # 无论部件是否共用 action。所以必须为 True——它能导出动作，
        # 而"共用一条 action"保证了每个 FBX 里只有一条 take。
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=1.0,     # 0=不简化，会产出巨大文件
        axis_forward='-Z',
        axis_up='Y',
        use_space_transform=True,
    )
    print(f"[SEEDFALL-ANIM] {char_id}/{anim}: {frames} 帧 loop={loop} -> {path}")
    return path


def build(char_id, only=None):
    out = []
    for anim, (frames, loop) in ANIMS.items():
        if only and anim not in only:
            continue
        out.append(export_anim(char_id, anim, frames, loop))
    return out


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("用法: blender -b -P gen_anim.py -- <角色id> [动画名...]")
        print("  角色:", ", ".join(gc.CHARACTERS))
        print("  动画:", ", ".join(ANIMS))
    else:
        build(args[0], args[1:] or None)
