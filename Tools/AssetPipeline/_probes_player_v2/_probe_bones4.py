# -*- coding: utf-8 -*-
"""探针 4：最小化定位崩溃点 + 读 slotted Action 结构。"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import bpy                          # noqa: E402

D = math.radians
OUT = os.path.join(HERE, "_probe_out")


def stage(n, msg):
    print(f"[P4] STAGE {n}: {msg}", flush=True)


def main():
    stage(1, "reset")
    kit.reset()

    stage(2, "armature_add")
    bpy.ops.object.armature_add(enter_editmode=False, location=(0, 0, 0))
    arm = bpy.context.active_object
    arm.name = "Armature"
    arm.data.name = "Armature"

    stage(3, "edit bones")
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for b in list(eb):
        eb.remove(b)
    b1 = eb.new("Bone_Hips"); b1.head = (0, 0, 0.9); b1.tail = (0, 0, 1.05)
    b2 = eb.new("Bone_Chest"); b2.head = (0, 0, 1.05); b2.tail = (0, 0, 1.4)
    b2.parent = b1; b2.use_connect = True
    b3 = eb.new("Bone_UpperArm_R"); b3.head = (0.2, 0, 1.35); b3.tail = (0.5, 0, 1.2)
    b3.parent = b2
    bpy.ops.object.mode_set(mode='OBJECT')

    stage(4, "setup action")
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, 8
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
    arm.animation_data_create()
    shared = bpy.data.actions.new("probe_anim")
    arm.animation_data.action = shared
    stage(4.1, "action assigned")
    print("[P4] ad.action_slot =", getattr(arm.animation_data, "action_slot", "NO ATTR"), flush=True)
    print("[P4] action.slots =", [(s.identifier, s.handle) for s in shared.slots], flush=True)

    stage(5, "keyframe loop")
    for f in range(1, 9):
        scene.frame_set(f)
        ang = D(20.0) * math.sin(2 * math.pi * (f - 1) / 8.0)
        arm.pose.bones["Bone_UpperArm_R"].rotation_euler = (0.0, 0.0, ang)
        arm.pose.bones["Bone_Chest"].rotation_euler = (ang * 0.5, 0.0, 0.0)
        for pb in arm.pose.bones:
            pb.keyframe_insert(data_path="rotation_euler")
    stage(6, "keyframes done")

    print("[P4] action.layers =", len(shared.layers), flush=True)
    tot = 0
    for li, lay in enumerate(shared.layers):
        print(f"[P4]  layer[{li}] {lay.name} strips={len(lay.strips)}", flush=True)
        for si, st in enumerate(lay.strips):
            bags = list(getattr(st, "channelbags", []) or [])
            print(f"[P4]    strip[{si}] type={st.type} bags={len(bags)}", flush=True)
            for bi, bag in enumerate(bags):
                tot += len(bag.fcurves)
                print(f"[P4]      bag[{bi}] fcurves={len(bag.fcurves)} "
                      f"slot_handle={getattr(bag, 'slot_handle', '-')}", flush=True)
                for fc in list(bag.fcurves)[:4]:
                    vals = [round(math.degrees(fc.evaluate(f)), 2) for f in range(1, 9)]
                    print(f"[P4]        {fc.data_path}[{fc.array_index}] "
                          f"kps={len(fc.keyframe_points)} vals={vals}", flush=True)
    print(f"[P4] 总 fcurves={tot}", flush=True)
    print("[P4] action.slots =", [(s.identifier, s.handle) for s in shared.slots], flush=True)

    stage(7, "read pose back")
    seq = []
    for f in range(1, 9):
        scene.frame_set(f)
        seq.append(round(math.degrees(arm.pose.bones["Bone_UpperArm_R"].rotation_euler.z), 2))
    print("[P4] 逐帧 Z:", seq, flush=True)

    stage(8, "export all_bones=True nla=False")
    path = os.path.join(OUT, "p4_a.fbx")
    kit.ensure_dir(path)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, global_scale=1.0,
        object_types={'MESH', 'ARMATURE', 'EMPTY'},
        bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=1.0, add_leaf_bones=False,
        axis_forward='-Z', axis_up='Y', use_space_transform=True,
    )
    stage(9, "exported; reimport")
    kit.reset()
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    a = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    print("[P4] 回读 ARMATURE:", [o.name for o in a], flush=True)
    if a:
        a = a[0]
        sc = bpy.context.scene
        s = []
        for f in range(1, 9):
            sc.frame_set(f)
            s.append(round(math.degrees(a.pose.bones["Bone_UpperArm_R"].rotation_euler.z), 2))
        print(f"[P4] 回读逐帧 Z={s} 摆幅={max(s)-min(s):.2f}°", flush=True)
        print("[P4] 回读 actions:", [ac.name for ac in bpy.data.actions], flush=True)
        for ac in bpy.data.actions:
            print(f"[P4]   {ac.name} layers={len(ac.layers)}", flush=True)
    stage(10, "done")


if __name__ == "__main__":
    main()
