# -*- coding: utf-8 -*-
"""探针 5：矩阵式排查骨骼动画导不出去的配置组合。"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import bpy                          # noqa: E402

D = math.radians
OUT = os.path.join(HERE, "_probe_out")


def build():
    kit.reset()
    bpy.ops.object.armature_add(enter_editmode=False, location=(0, 0, 0))
    arm = bpy.context.active_object
    arm.name = "Armature"
    arm.data.name = "Armature"
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

    mesh = kit.box("Part_Torso", (0.0, 0.0, 1.25), (0.34, 0.20, 0.40), "Cloth")
    kit.apply_transforms(mesh)
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True); arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    arm.data.bones.active = arm.data.bones["Bone_Chest"]
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.parent_set(type='BONE', keep_transform=True)
    mesh.parent_bone = "Bone_Chest"
    return arm


def animate(arm, mode="euler"):
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, 8
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ' if mode == "euler" else 'QUATERNION'
    arm.animation_data_create()
    act = bpy.data.actions.new("probe_anim")
    arm.animation_data.action = act
    for f in range(1, 9):
        scene.frame_set(f)
        ang = D(20.0) * math.sin(2 * math.pi * (f - 1) / 8.0)
        pbC = arm.pose.bones["Bone_Chest"]
        if mode == "euler":
            pbC.rotation_euler = (ang, 0.0, 0.0)
            pbC.keyframe_insert(data_path="rotation_euler")
        else:
            import mathutils
            pbC.rotation_quaternion = mathutils.Euler((ang, 0, 0), 'XYZ').to_quaternion()
            pbC.keyframe_insert(data_path="rotation_quaternion")
    return act


def readback(path, label):
    kit.reset()
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    a = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if not a:
        print(f"[P5] {label}: ★无 ARMATURE★", flush=True)
        return
    a = a[0]
    sc = bpy.context.scene
    s = []
    for f in range(1, 9):
        sc.frame_set(f)
        pb = a.pose.bones["Bone_Chest"]
        if pb.rotation_mode == 'QUATERNION':
            e = pb.rotation_quaternion.to_euler('XYZ')
            s.append(round(math.degrees(e.x), 2))
        else:
            s.append(round(math.degrees(pb.rotation_euler.x), 2))
    span = max(s) - min(s)
    acts = [(ac.name, len(ac.layers)) for ac in bpy.data.actions]
    print(f"[P5] {label}: mode={a.pose.bones['Bone_Chest'].rotation_mode} "
          f"逐帧X={s} 摆幅={span:.2f}° actions={acts}", flush=True)


VARIANTS = [
    dict(label="V1 base euler NULL", mode="euler", nodetype='NULL', all_actions=True,
         use_sel=True, space=False),
    dict(label="V2 quat NULL", mode="quat", nodetype='NULL', all_actions=True,
         use_sel=True, space=False),
    dict(label="V3 euler ROOT", mode="euler", nodetype='ROOT', all_actions=True,
         use_sel=True, space=False),
    dict(label="V4 euler NULL all_actions=F", mode="euler", nodetype='NULL',
         all_actions=False, use_sel=True, space=False),
    dict(label="V5 euler NULL space=True", mode="euler", nodetype='NULL',
         all_actions=True, use_sel=True, space=True),
    dict(label="V6 euler ROOT space=True", mode="euler", nodetype='ROOT',
         all_actions=True, use_sel=True, space=True),
]


def main():
    for v in VARIANTS:
        arm = build()
        animate(arm, v["mode"])
        path = os.path.join(OUT, v["label"].split()[0] + ".fbx")
        kit.ensure_dir(path)
        kw = dict(
            filepath=path, use_selection=v["use_sel"], apply_unit_scale=True, global_scale=1.0,
            object_types={'MESH', 'ARMATURE', 'EMPTY'},
            mesh_smooth_type='FACE', path_mode='AUTO', embed_textures=False,
            bake_anim=True, bake_anim_use_all_bones=True,
            bake_anim_use_all_actions=v["all_actions"], bake_anim_use_nla_strips=False,
            bake_anim_force_startend_keying=True, bake_anim_step=1.0,
            bake_anim_simplify_factor=1.0, add_leaf_bones=False,
            armature_nodetype=v["nodetype"],
            axis_forward='-Z', axis_up='Y', use_space_transform=True,
        )
        if v["space"]:
            kw["bake_space_transform"] = True
        try:
            bpy.ops.object.select_all(action='SELECT')
            bpy.ops.export_scene.fbx(**kw)
        except Exception as e:                              # noqa: BLE001
            print(f"[P5] {v['label']}: 导出异常 {e}", flush=True)
            continue
        readback(path, v["label"])


if __name__ == "__main__":
    main()
