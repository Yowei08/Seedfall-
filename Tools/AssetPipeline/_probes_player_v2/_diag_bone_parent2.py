# -*- coding: utf-8 -*-
"""诊断 3：让 Blender 自己绑（parent_set keep_transform），把它写出的矩阵读出来，
    再照着重建一遍。这样能一次确定正确的 frame 约定。"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_player_v2 as gp          # noqa: E402
import bpy                          # noqa: E402
import mathutils                    # noqa: E402

M = mathutils.Matrix


def world_extent(o):
    mw = o.matrix_world
    pts = [mw @ v.co for v in o.data.vertices]
    xs = [p.x for p in pts]; zs = [p.z for p in pts]
    return (round(min(xs), 4), round(max(xs), 4), round(min(zs), 4), round(max(zs), 4))


def make_tests():
    kit.reset()
    arm = gp.make_armature()
    a = kit.box("T_Spine", (0.0, 0.0, 1.10), (0.2, 0.1, 0.1), "Cloth")
    kit.apply_transforms(a)
    b = kit.box("T_Arm", (-0.26, 0.0, 1.18), (0.1, 0.1, 0.1), "Skin")
    kit.apply_transforms(b)
    return arm, a, b


def bind_by_operator(arm, obj, bone):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    # ★ 进 POSE 模式必须先把 armature 设为活动对象 ★
    bpy.ops.object.mode_set(mode='POSE')
    arm.data.bones.active = arm.data.bones[bone]
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.parent_set(type='BONE', keep_transform=True)
    bpy.context.view_layer.update()
    return obj


def main():
    # ── 1) 用 Blender 自己的 parent_set ──
    arm, a, b = make_tests()
    want_a = world_extent(a)
    want_b = world_extent(b)
    print(f"[D3] 期望 T_Spine x[{want_a[0]},{want_a[1]}] z[{want_a[2]},{want_a[3]}]")
    print(f"[D3] 期望 T_Arm   x[{want_b[0]},{want_b[1]}] z[{want_b[2]},{want_b[3]}]")

    bind_by_operator(arm, a, "Bone_Spine")
    bind_by_operator(arm, b, "Bone_UpperArm_R")
    got_a, got_b = world_extent(a), world_extent(b)
    print(f"[D3] parent_set 后 T_Spine x[{got_a[0]},{got_a[1]}] z[{got_a[2]},{got_a[3]}] "
          f"{'OK' if got_a == want_a else '★漂移★'}")
    print(f"[D3] parent_set 后 T_Arm   x[{got_b[0]},{got_b[1]}] z[{got_b[2]},{got_b[3]}] "
          f"{'OK' if got_b == want_b else '★漂移★'}")

    for o, nm in ((a, "T_Spine"), (b, "T_Arm")):
        bone = o.parent_bone
        bb = arm.data.bones[bone]
        print(f"[D3] --- {nm} bone={bone}")
        print(f"[D3]   parent_type={o.parent_type}")
        print(f"[D3]   matrix_basis      T={tuple(round(v,4) for v in o.matrix_basis.translation)}")
        print(f"[D3]   matrix_local      T={tuple(round(v,4) for v in o.matrix_local.translation)}")
        print(f"[D3]   matrix_world      T={tuple(round(v,4) for v in o.matrix_world.translation)}")
        print(f"[D3]   mpi               T={tuple(round(v,4) for v in o.matrix_parent_inverse.translation)}")
        print(f"[D3]   bone.head_local = {tuple(round(v,4) for v in bb.head_local)}")
        print(f"[D3]   bone.tail_local = {tuple(round(v,4) for v in bb.tail_local)}")
        print(f"[D3]   bone.matrix_local T={tuple(round(v,4) for v in bb.matrix_local.translation)}")
        # 反解：Blender 实际用的父母世界矩阵 = world @ mpi^-1 @ basis^-1
        derived = o.matrix_world @ o.matrix_parent_inverse.inverted() @ o.matrix_basis.inverted()
        print(f"[D3]   反解父母矩阵    T={tuple(round(v,4) for v in derived.translation)}")
        print(f"[D3]   反解父母矩阵    3x3={[[round(v,4) for v in r] for r in derived.to_3x3()]}")

        # 候选 A：T(tail) @ matrix_local
        candA = M.Translation(bb.tail_local) @ bb.matrix_local.to_3x3().to_4x4()
        # 候选 B：T(head) @ matrix_local
        candB = M.Translation(bb.head_local) @ bb.matrix_local.to_3x3().to_4x4()
        for tag, c in (("T(tail)@rot", candA), ("T(head)@rot", candB)):
            err = max(abs(c[i][j] - derived[i][j]) for i in range(4) for j in range(4))
            print(f"[D3]   候选 {tag:<12} 与反解最大差 {err:.6f} "
                  f"{'← 就是它' if err < 1e-4 else ''}")


if __name__ == "__main__":
    main()
