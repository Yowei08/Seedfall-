# -*- coding: utf-8 -*-
"""
探针：真骨架（Armature + 带名骨骼）+ 骨头父子绑定的网格件 能否正确过 FBX？

想确认的四件事（不确认就动手 = 赌）：
  1) 骨骼名能不能原样留在 FBX 里（Unity 配 Avatar Mask 要靠名字）
  2) 骨骼父子链在回读后是否完整
  3) 骨头父子绑定的网格件，回读后绑定路径是不是 Bone_*/Part
  4) bake_anim=True 时骨骼动画曲线是否真的写进去了

用完即弃；结论写进 gen_player_v2.py 的注释。
"""
import os
import sys
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit          # noqa: E402
import bpy                          # noqa: E402
import mathutils                    # noqa: E402

D = math.radians
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_probe_out")


def make_armature():
    bpy.ops.object.armature_add(enter_editmode=False, location=(0, 0, 0))
    arm = bpy.context.active_object
    arm.name = "Armature"
    arm.data.name = "Armature"

    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for b in list(eb):
        eb.remove(b)

    def bone(name, head, tail, parent=None, connect=False):
        b = eb.new(name)
        b.head = head
        b.tail = tail
        if parent is not None:
            b.parent = eb[parent]
            b.use_connect = connect
        return b

    bone("Bone_Hips",   (0, 0, 0.90), (0, 0, 1.05))
    bone("Bone_Spine",  (0, 0, 1.05), (0, 0, 1.25), "Bone_Hips", True)
    bone("Bone_Chest",  (0, 0, 1.25), (0, 0, 1.42), "Bone_Spine", True)
    bone("Bone_Head",   (0, 0, 1.42), (0, 0, 1.72), "Bone_Chest", True)
    bone("Bone_UpperArm_R", (0.20, 0, 1.38), (0.45, 0, 1.20), "Bone_Chest")
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def bone_parent(obj, arm, bone_name):
    """把网格件绑到某根骨骼上（保持世界变换）。

    注意：parent_type 必须是 'BONE'，且 parent_bone 用**骨骼名**（不是索引）。
    keep_transform 之后 Blender 会写 matrix_parent_inverse，位置才不会跳。
    """
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    # ★ parent_set(type='BONE') 需要"当前活动骨骼"，否则报 No active bone ★
    # 活动骨骼只能在 POSE 模式下设置（EDIT 模式会重建骨骼、OBJECT 模式没有活动骨骼）。
    bpy.ops.object.mode_set(mode='POSE')
    arm.data.bones.active = arm.data.bones[bone_name]
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.parent_set(type='BONE', keep_transform=True)
    obj.parent_bone = bone_name
    bpy.context.view_layer.update()
    return obj


def build():
    kit.reset()
    arm = make_armature()

    torso = kit.box("Part_Torso", (0.0, 0.0, 1.25), (0.34, 0.20, 0.40), "Cloth")
    kit.apply_transforms(torso)
    bone_parent(torso, arm, "Bone_Chest")

    head = kit.sphere("Part_Head", (0.0, 0.0, 1.57), 0.16, "Skin", segs=10, rings=6)
    kit.apply_transforms(head)
    bone_parent(head, arm, "Bone_Head")

    hand = kit.box("Part_Hand_R", (0.48, 0.0, 1.18), (0.10, 0.10, 0.10), "Skin")
    kit.apply_transforms(hand)
    bone_parent(hand, arm, "Bone_UpperArm_R")
    return arm, [torso, head, hand]


def export(path, animated):
    kit.ensure_dir(path)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        # ★ 关键：必须把 ARMATURE 也导出，否则骨架自己不在场景里，骨骼全丢 ★
        object_types={'MESH', 'ARMATURE', 'EMPTY'},
        mesh_smooth_type='FACE',
        path_mode='AUTO',
        embed_textures=False,
        bake_anim=animated,
        bake_anim_use_all_bones=True if animated else False,
        bake_anim_use_all_actions=True if animated else False,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=animated,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=1.0,
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        armature_nodetype='NULL',
        axis_forward='-Z',
        axis_up='Y',
        use_space_transform=True,
    )


def report(tag):
    print(f"----- {tag} 节点清单 -----")
    for o in sorted(bpy.context.scene.objects, key=lambda x: x.name):
        par = o.parent.name if o.parent else "-"
        pb = getattr(o, "parent_bone", "") or ""
        print(f"   type={o.type:<9} name={o.name:<24} parent={par:<20} bone={pb}")


def main():
    # ── 1) 静止导出 + 回读 ──
    arm, parts = build()
    p1 = os.path.join(OUT, "probe_static.fbx")
    export(p1, animated=False)

    kit.reset()
    bpy.ops.import_scene.fbx(filepath=p1, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    report("回读 static")
    arm2 = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if arm2:
        a = arm2[0]
        print("[PROBE] 臂的骨骼名:", [b.name for b in a.data.bones])
        print("[PROBE] 骨骼链:")
        for b in a.data.bones:
            print(f"     {b.name:<22} parent={b.parent.name if b.parent else '-'}")
    else:
        print("[PROBE] ★回读后没有 ARMATURE★")

    # ── 2) 带动画导出 + 回读逐帧 ──
    arm, parts = build()
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, 8
    shared = bpy.data.actions.new("probe_anim")
    arm.animation_data_create()
    arm.animation_data.action = shared
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
    for f in range(1, 9):
        scene.frame_set(f)
        ang = D(20.0) * math.sin(2 * math.pi * (f - 1) / 8.0)
        arm.pose.bones["Bone_UpperArm_R"].rotation_euler = (0.0, 0.0, ang)
        arm.pose.bones["Bone_Chest"].rotation_euler = (ang * 0.5, 0.0, 0.0)
        for pb in arm.pose.bones:
            pb.keyframe_insert(data_path="rotation_euler")
    p2 = os.path.join(OUT, "probe_anim.fbx")
    export(p2, animated=True)

    kit.reset()
    bpy.ops.import_scene.fbx(filepath=p2, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    report("回读 anim")
    arm3 = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if arm3:
        a = arm3[0]
        print("[PROBE] anim 骨骼名:", [b.name for b in a.data.bones])
        sc = bpy.context.scene
        seq = []
        for f in range(1, 9):
            sc.frame_set(f)
            pb = a.pose.bones.get("Bone_UpperArm_R")
            seq.append(round(math.degrees(pb.rotation_euler.z), 2) if pb else None)
        print("[PROBE] UpperArm_R 逐帧 Z 角:", seq)
        span = (max(seq) - min(seq)) if seq and seq[0] is not None else 0
        print(f"[PROBE] ★骨骼动画摆幅 = {span:.2f}°（0 表示曲线没写进去）★")
    print("[PROBE] 输出目录:", OUT)


if __name__ == "__main__":
    main()
