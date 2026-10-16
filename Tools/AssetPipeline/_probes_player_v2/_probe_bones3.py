# -*- coding: utf-8 -*-
"""探针 3：Blender 5.2 的 slotted Action 结构 + 骨骼曲线导出到底缺什么。"""
import os
import sys
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit          # noqa: E402
import bpy                          # noqa: E402

D = math.radians
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_probe_out")
import _probe_bones as PB           # noqa: E402


def dump_action(act, tag):
    print(f"[P3] --- action {act.name} ({tag}) ---")
    print(f"[P3]   layers={len(act.layers)}")
    total = 0
    for li, lay in enumerate(act.layers):
        print(f"[P3]   layer[{li}] {lay.name} strips={len(lay.strips)}")
        for si, st in enumerate(lay.strips):
            bags = getattr(st, "channelbags", None)
            print(f"[P3]     strip[{si}] {st.type} channelbags={len(bags) if bags else 0}")
            if not bags:
                continue
            for bi, bag in enumerate(bags):
                nb = len(bag.fcurves)
                total += nb
                print(f"[P3]       bag[{bi}] fcurves={nb} slot_handle={getattr(bag,'slot_handle','-')}")
                for fc in list(bag.fcurves)[:3]:
                    vals = [round(math.degrees(fc.evaluate(f)), 2) for f in range(1, 9)]
                    print(f"[P3]         {fc.data_path}[{fc.array_index}] "
                          f"kps={len(fc.keyframe_points)} vals={vals}")
    print(f"[P3]   总 fcurves = {total}")
    return total


def main():
    arm, parts = PB.build()
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, 8

    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
    arm.animation_data_create()
    shared = bpy.data.actions.new("probe_anim")
    arm.animation_data.action = shared

    for f in range(1, 9):
        scene.frame_set(f)
        ang = D(20.0) * math.sin(2 * math.pi * (f - 1) / 8.0)
        arm.pose.bones["Bone_UpperArm_R"].rotation_euler = (0.0, 0.0, ang)
        arm.pose.bones["Bone_Chest"].rotation_euler = (ang * 0.5, 0.0, 0.0)
        for pb in arm.pose.bones:
            pb.keyframe_insert(data_path="rotation_euler")

    n = dump_action(shared, "新建后")

    ad = arm.animation_data
    print("[P3] ad.action =", ad.action.name if ad.action else None)
    print("[P3] ad.action_slot =", getattr(ad, "action_slot", "NO ATTR"))
    print("[P3] ad.action_slot_handle =", getattr(ad, "action_slot_handle", "NO ATTR"))
    try:
        print("[P3] action.slots =", [(s.identifier, s.handle) for s in shared.slots])
    except Exception as e:                                    # noqa: BLE001
        print("[P3] slots err:", e)

    # 姿态是否真的被驱动
    seq = []
    for f in range(1, 9):
        scene.frame_set(f)
        seq.append(round(math.degrees(arm.pose.bones["Bone_UpperArm_R"].rotation_euler.z), 2))
    print("[P3] 直接读 pose bone 逐帧 Z:", seq)

    # ── 导出（两种 all_bones + 是否 push NLA）──
    for tag, all_bones, use_nla in (("A allb=T nla=F", True, False),
                                    ("B allb=F nla=F", False, False),
                                    ("C allb=T nla=T", True, True)):
        if use_nla:
            tr = ad.nla_tracks.new()
            tr.name = "probe"
            tr.strips.new("probe", 1, shared)
        path = os.path.join(OUT, f"p3_{tag.split()[0]}.fbx")
        kit.ensure_dir(path)
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, apply_unit_scale=True, global_scale=1.0,
            object_types={'MESH', 'ARMATURE', 'EMPTY'},
            mesh_smooth_type='FACE', path_mode='AUTO', embed_textures=False,
            bake_anim=True, bake_anim_use_all_bones=all_bones,
            bake_anim_use_all_actions=True, bake_anim_use_nla_strips=use_nla,
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
            print(f"[P3] {tag}: ★回读无 ARMATURE★")
            continue
        a = a[0]
        sc = bpy.context.scene
        s = []
        for f in range(1, 9):
            sc.frame_set(f)
            s.append(round(math.degrees(a.pose.bones["Bone_UpperArm_R"].rotation_euler.z), 2))
        print(f"[P3] {tag}: 回读逐帧 Z={s} 摆幅={max(s)-min(s):.2f}°")
        print(f"[P3] {tag}: 回读 actions={[ac.name for ac in bpy.data.actions]}")


if __name__ == "__main__":
    main()
