# -*- coding: utf-8 -*-
"""诊断 5：单件绑 OK、循环绑全漂 —— 找出漂移的触发条件。"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_player_v2 as gp          # noqa: E402
import bpy                          # noqa: E402


def ext(o):
    mw = o.matrix_world
    pts = [mw @ v.co for v in o.data.vertices]
    zs = [p.z for p in pts]
    return round(min(zs), 4), round(max(zs), 4)


def bind_variant(arm, obj, bone, variant):
    """按不同顺序绑，找出哪种能把世界位置保住。"""
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    if variant == "A":                      # gp.bone_parent 的顺序
        bpy.ops.object.mode_set(mode='POSE')
        arm.data.bones.active = arm.data.bones[bone]
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.parent_set(type='BONE', keep_transform=True)
        obj.parent_bone = bone
    elif variant == "B":                    # 先 POSE 设 active，绑完不再动 parent_bone
        bpy.ops.object.mode_set(mode='POSE')
        arm.data.bones.active = arm.data.bones[bone]
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.parent_set(type='BONE', keep_transform=True)
    elif variant == "C":                    # 设完 parent_bone 之后重新求值前，自己修 mpi
        bpy.ops.object.mode_set(mode='POSE')
        arm.data.bones.active = arm.data.bones[bone]
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.parent_set(type='BONE', keep_transform=True)
        obj.parent_bone = bone
        # 手工把 matrix_parent_inverse 重新算一遍：parent 世界矩阵的逆
        import mathutils
        bb = arm.data.bones[bone]
        par = mathutils.Matrix.Translation(bb.tail_local) @ bb.matrix_local.to_3x3().to_4x4()
        keep = obj.matrix_world.copy()
        obj.matrix_parent_inverse = par.inverted()
        obj.matrix_basis = par.inverted() @ keep
    elif variant == "D":                    # 绑完 update 一次
        bpy.ops.object.mode_set(mode='POSE')
        arm.data.bones.active = arm.data.bones[bone]
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.parent_set(type='BONE', keep_transform=True)
        obj.parent_bone = bone
        bpy.context.view_layer.update()
    bpy.context.view_layer.update()
    return obj


def main():
    for variant in ("A", "B", "C", "D"):
        # 连续绑两件，看第二件会不会被第一件影响
        kit.reset()
        arm = gp.make_armature()
        o1 = kit.box("P1", (0.0, 0.0, 1.10), (0.2, 0.1, 0.1), "Cloth")
        kit.apply_transforms(o1)
        o2 = kit.box("P2", (0.0, 0.0, 0.50), (0.2, 0.1, 0.1), "Cloth")
        kit.apply_transforms(o2)
        e1, e2 = ext(o1), ext(o2)
        bind_variant(arm, o1, "Bone_Spine", variant)
        bind_variant(arm, o2, "Bone_LowerLeg_L", variant)
        g1, g2 = ext(o1), ext(o2)
        ok1 = abs(g1[0] - e1[0]) < 0.002
        ok2 = abs(g2[0] - e2[0]) < 0.002
        print(f"[D5] variant {variant}: P1 期望{e1} 实得{g1} {'OK' if ok1 else '漂'}   "
              f"P2 期望{e2} 实得{g2} {'OK' if ok2 else '漂'}")


if __name__ == "__main__":
    main()
