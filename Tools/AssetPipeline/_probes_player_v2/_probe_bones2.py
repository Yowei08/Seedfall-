# -*- coding: utf-8 -*-
"""探针 2：骨骼动画曲线为什么是 0？逐个假设验证。"""
import os
import sys
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit          # noqa: E402
import bpy                          # noqa: E402

D = math.radians
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_probe_out")

import _probe_bones as PB           # noqa: E402


def main():
    arm, parts = PB.build()
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, 8

    shared = bpy.data.actions.new("probe_anim")
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
    arm.animation_data_create()
    arm.animation_data.action = shared

    for f in range(1, 9):
        scene.frame_set(f)
        ang = D(20.0) * math.sin(2 * math.pi * (f - 1) / 8.0)
        arm.pose.bones["Bone_UpperArm_R"].rotation_euler = (0.0, 0.0, ang)
        arm.pose.bones["Bone_Chest"].rotation_euler = (ang * 0.5, 0.0, 0.0)
        for pb in arm.pose.bones:
            pb.keyframe_insert(data_path="rotation_euler")

    # ── 假设验证 1：关键帧到底有没有进 action？──
    print("[P2] action:", shared.name, "fcurves =", len(shared.fcurves))
    for fc in list(shared.fcurves)[:6]:
        vals = [round(math.degrees(fc.evaluate(f)), 2) for f in range(1, 9)]
        print(f"[P2]   curve {fc.data_path}[{fc.array_index}] slots={len(fc.keyframe_points)} "
              f"vals={vals}")

    # ── 假设验证 2：Blender 5 的 slot 机制 ──
    ad = arm.animation_data
    print("[P2] action_slot =", getattr(ad, "action_slot", None))
    try:
        print("[P2] action.slots =", [s.identifier for s in shared.slots])
    except Exception as e:                                   # noqa: BLE001
        print("[P2] 无 slots 属性:", e)
    try:
        print("[P2] ad.action_slot_handle =", getattr(ad, "action_slot_handle", None))
    except Exception as e:                                   # noqa: BLE001
        print("[P2] err", e)

    # ── 假设验证 3：pose bone 的实际值（依赖图有没有被驱动）──
    seq = []
    for f in range(1, 9):
        scene.frame_set(f)
        seq.append(round(math.degrees(arm.pose.bones["Bone_UpperArm_R"].rotation_euler.z), 2))
    print("[P2] 摆姿态后逐帧 Z:", seq)

    # ── 假设验证 4：要不要把 action push 到 NLA ──
    for opt in ("all_bones_False", "all_bones_True"):
        path = os.path.join(OUT, f"p2_{opt}.fbx")
        kit.ensure_dir(path)
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, apply_unit_scale=True, global_scale=1.0,
            object_types={'MESH', 'ARMATURE', 'EMPTY'},
            mesh_smooth_type='FACE', path_mode='AUTO', embed_textures=False,
            bake_anim=True,
            bake_anim_use_all_bones=(opt == "all_bones_True"),
            bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
            bake_anim_force_startend_keying=True, bake_anim_step=1.0,
            bake_anim_simplify_factor=1.0, add_leaf_bones=False,
            primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
            axis_forward='-Z', axis_up='Y', use_space_transform=True,
        )
        kit.reset()
        bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                                 ignore_leaf_bones=False)
        a = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
        if not a:
            print(f"[P2] {opt}: 回读无 ARMATURE")
            continue
        a = a[0]
        sc = bpy.context.scene
        s = []
        for f in range(1, 9):
            sc.frame_set(f)
            s.append(round(math.degrees(a.pose.bones["Bone_UpperArm_R"].rotation_euler.z), 2))
        print(f"[P2] {opt}: 回读逐帧 Z={s}  摆幅={max(s)-min(s):.2f}°")
        acts = [ac.name for ac in bpy.data.actions]
        print(f"[P2] {opt}: 回读 actions={acts}")


if __name__ == "__main__":
    main()
