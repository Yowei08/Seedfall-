# -*- coding: utf-8 -*-
"""诊断 2：骨头父子绑定的 matrix_parent_inverse 到底该取哪个 —— parent_bone.tail 还是 .head？

现象（_diag_player_scale.py）：绑到骨骼之后网格件**位移漂移**（肩从 z=1.41 跑到 2.70，
手臂从 x=0.27 跑到 0.92），但**缩放是 1.0**。说明 matrix_parent_inverse 没框对。

假设：Blender 的骨头父子绑定是"挂在**骨骼尾部**"的，所以父母矩阵必须用 tail 构造。
这里对同一件网格分别用三种 inverse 试，看哪种能把世界位置还原。
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_player_v2 as gp          # noqa: E402
import bpy                          # noqa: E402
import mathutils                    # noqa: E402

V = mathutils.Vector
M = mathutils.Matrix


def make_bone_space(arm, bone, at_tail):
    """构造"骨骼空间"的世界矩阵：平移放到骨骼头（或尾），朝向用骨骼矩阵。"""
    b = arm.data.bones[bone]
    off = (b.tail_local if at_tail else b.head_local)
    return M.Translation(off) @ arm.matrix_world @ b.matrix_local.to_3x3().to_4x4()


def world_pts(o):
    mw = o.matrix_world
    return [mw @ v.co for v in o.data.vertices]


def extent(pts):
    xs = [p.x for p in pts]; zs = [p.z for p in pts]
    return (round(min(xs), 3), round(max(xs), 3), round(min(zs), 3), round(max(zs), 3))


def main():
    kit.reset()
    arm = gp.make_armature()

    # 造两件试验网格：一件绑 Spine（竖直骨）、一件绑 UpperArm_R（斜骨）
    a = kit.box("T_Spine", (0.0, 0.0, 1.10), (0.2, 0.1, 0.1), "Cloth")
    kit.apply_transforms(a)
    b = kit.box("T_Arm", (-0.26, 0.0, 1.18), (0.1, 0.1, 0.1), "Skin")
    kit.apply_transforms(b)

    targets = [(a, "Bone_Spine"), (b, "Bone_UpperArm_R")]
    before = {}
    for o, bone in targets:
        before[o.name] = extent(world_pts(o))
    for o, bone in targets:
        print(f"[D2] 绑定前 {o.name:<10} x[{before[o.name][0]},{before[o.name][1]}] "
              f"z[{before[o.name][2]},{before[o.name][3]}]")

    for at_tail in (True, False):
        # 复位
        for o, bone in targets:
            o.parent = None
            o.matrix_world = M.Translation((0, 0, 0))
        kit.reset()
        arm = gp.make_armature()
        a = kit.box("T_Spine", (0.0, 0.0, 1.10), (0.2, 0.1, 0.1), "Cloth")
        kit.apply_transforms(a)
        b = kit.box("T_Arm", (-0.26, 0.0, 1.18), (0.1, 0.1, 0.1), "Skin")
        kit.apply_transforms(b)
        targets = [(a, "Bone_Spine"), (b, "Bone_UpperArm_R")]

        for o, bone in targets:
            keep = o.matrix_world.copy()
            o.parent = arm
            o.parent_type = 'BONE'
            o.parent_bone = bone
            par = make_bone_space(arm, bone, at_tail)
            o.matrix_parent_inverse = par.inverted()
            o.matrix_basis = par.inverted() @ keep
        bpy.context.view_layer.update()
        tag = "tail" if at_tail else "head"
        for o, bone in targets:
            e = extent(world_pts(o))
            ok = (abs(e[0] - before[o.name][0]) < 0.002 and abs(e[2] - before[o.name][2]) < 0.002)
            print(f"[D2] inverse@{tag} {o.name:<10} x[{e[0]},{e[1]}] z[{e[2]},{e[3]}] "
                  f"{'OK ✔' if ok else 'MISMATCH ✘'}")

    # 再试第四种：直接信任 parent_set(keep_transform=True)，但 bone 先设好
    kit.reset()
    arm = gp.make_armature()
    a = kit.box("T_Spine", (0.0, 0.0, 1.10), (0.2, 0.1, 0.1), "Cloth")
    kit.apply_transforms(a)
    bpy.ops.object.mode_set(mode='POSE')
    arm.data.bones.active = arm.data.bones["Bone_Spine"]
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    a.select_set(True); arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='BONE', keep_transform=True)
    bpy.context.view_layer.update()
    e = extent(world_pts(a))
    print(f"[D2] parent_set      T_Spine   x[{e[0]},{e[1]}] z[{e[2]},{e[3]}]  "
          f"(期望 x[-0.100,0.100] z[1.050,1.150])")
    print(f"[D2]   parent_bone={a.parent_bone!r}  basis_loc={tuple(round(v,3) for v in a.matrix_basis.translation)}")
    print(f"[D2]   mpi translation={tuple(round(v,3) for v in a.matrix_parent_inverse.translation)}")


if __name__ == "__main__":
    main()
