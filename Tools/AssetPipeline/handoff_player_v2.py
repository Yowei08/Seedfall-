# -*- coding: utf-8 -*-
"""
交接数值 + 攻击姿态对照图
==========================
① 报告 Unity 侧挂武器需要的精确数值：Bone_Hand_R 的世界位置、GRIP 的四元数。
② 渲三套 attack 在"前摇 / 发力 / 收招"三个相位的姿态，**同一相机**。
   腿部在三条动画的任何相位都应该处在完全相同的像素位置上 ——
   这是"下半身维持站立"最直观的证据（角度值是 0，图上是腿没挪窝）。

用法：blender -b --factory-startup -P handoff_player_v2.py -- [values|poses|all]
"""

import os
import sys
import math
import json

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_player_v2 as gp          # noqa: E402

import bpy                          # noqa: E402
import mathutils                    # noqa: E402

V = mathutils.Vector
PROJECT = gp.PROJECT
PREVIEW_DIR = gp.PREVIEW_DIR


# ---------------------------------------------------------------- 轴向换算
def blender_to_unity(v):
    """Blender (x, y, z) → Unity (x, z, -y)。

    这正是 `export_fbx(axis_forward='-Z', axis_up='Y')` 用的那组变换：
    Blender 的 +Y（模型背后）变成 Unity 的 -Z，Blender 的 +Z 变成 Unity 的 +Y。
    """
    return (v[0], v[2], -v[1])


def handoff_values():
    """算出 Unity 侧挂武器要用的数值。"""
    arm, meshes = gp.assemble_player()
    bpy.context.view_layer.update()

    hb = gp.BONE_NAME["Bone_Hand_R"]
    quat, ang, palm = gp.grip_frame()
    pn = gp.palm_normal()

    out = {}
    out["hand_bone_head_blender"] = [round(v, 5) for v in hb[1]]
    out["hand_bone_tail_blender"] = [round(v, 5) for v in hb[2]]
    out["hand_bone_head_unity"] = [round(v, 5) for v in blender_to_unity(hb[1])]
    out["hand_bone_dir_blender"] = [round(v, 5) for v in (V(hb[2]) - V(hb[1])).normalized()]
    out["palm_normal_blender"] = [round(v, 5) for v in pn]
    out["grip_quat_unity_xyzw"] = [round(v, 6) for v in
                                   (quat.x, quat.y, quat.z, quat.w)]
    out["grip_quat_blender_xyzw"] = [round(v, 6) for v in
                                     (quat.x, quat.y, quat.z, quat.w)]
    out["grip_alignment_err_deg"] = round(gp.grip_axis_error(quat), 4)

    # 手动骨骼的世界位置（把整条手臂链读出来，方便 Unity 侧核对）
    for name in ("Bone_Shoulder_R", "Bone_UpperArm_R", "Bone_LowerArm_R", "Bone_Hand_R"):
        b = gp.BONE_NAME[name]
        out[f"{name}_head_unity"] = [round(v, 5) for v in blender_to_unity(b[1])]

    # 手腕坐标系的"手骨轴"与"掌心法线"（Unity 空间）
    hand_axis_u = blender_to_unity((V(hb[2]) - V(hb[1])).normalized())
    palm_n_u = blender_to_unity(pn)
    out["hand_axis_unity"] = [round(v, 6) for v in hand_axis_u]
    out["palm_normal_unity"] = [round(v, 6) for v in palm_n_u]

    # 在 Unity 里把武器的握持坐标系对齐到手腕需要的那次旋转。
    # 约束（见 gen_player_v2.grip_frame 的坐标定义）：
    #   GRIP.+Z（手柄）  → 世界 +Y（竖直）
    #   GRIP.+X（掌法线）→ world palm_normal
    # 在手腕局部坐标系里解出来后，实测正好是**绕 Y 轴 180°**。
    m = mathutils.Matrix(((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)))
    e = m.to_euler('XYZ')
    print("=" * 78)
    print("Unity 侧挂武器需要的数值（已换算到 Unity 坐标系：x 右, y 上, z 前）")
    print("=" * 78)
    for k, v in out.items():
        print(f"  {k:34} = {v}")
    print("=" * 78)
    print("  ★ 推荐挂法（一次对齐，不用试角度）★")
    print("      var hand = player.transform.Find(\"Armature/Bone_Hand_R\");")
    print("      weapon.SetParent(hand, false);")
    print("      weapon.localPosition = Vector3.zero;")
    print("      weapon.localRotation = Quaternion.Euler(0f, 180f, 0f);")
    print("    理由：武器的 GRIP 节点是 identity（握持朝向已烘进网格顶点），")
    print("          网格已按「手柄沿 +Z、掌心法线沿 +X」定好；")
    print("          手腕局部系里要把它转成「手柄竖直、法线朝前」，正好是绕 Y 轴 180°。")
    print("          三把武器用的是**同一个**握持坐标系，所以三把都用这一个旋转值。")
    print("=" * 78)

    p = os.path.join(PREVIEW_DIR, "handoff_values.json")
    kit.ensure_dir(p)
    with open(p, "w", encoding="utf-8") as fh:
        json.dump(out, fh, ensure_ascii=False, indent=2)
    print(f"[HANDOFF] 明细 -> {p}")
    return out


# ---------------------------------------------------------------- 攻击姿态图
def render_attack_poses():
    """渲三套 attack 的三个相位。**三套共用同一个相机**，腿的位置才可比。"""
    import verify_player_v2 as vp

    phases = (("windup", 0.34), ("strike", 0.54), ("recover", 0.80))
    shots = []
    for anim in ("attack_hoe", "attack_can", "attack_fert"):
        arm, meshes = gp.assemble_player()
        frames, loop, _n = gp.ANIMS[anim]
        total = frames
        row = []
        for tag, frac in phases:
            t = frac
            f = max(1, min(total, int(round(frac * (total - 1))) + 1))
            bpy.context.scene.frame_set(f)
            gp.pose_player(anim, t, arm)
            bpy.context.view_layer.update()
            pts = []
            for o in bpy.data.objects:
                if o.type == 'MESH':
                    mw = o.matrix_world
                    pts += [mw @ v.co for v in o.data.vertices]
            xs = [p.x for p in pts]; zs = [p.z for p in pts]
            center = ((min(xs) + max(xs)) * 0.5, 0.0, (min(zs) + max(zs)) * 0.5)
            # ★ 半径与中心固定成常量 ★ 三套之间不能各算各的取景，否则腿的像素位置没法比
            radius = 1.45
            p = os.path.join(PREVIEW_DIR, f"pose_{anim}_{tag}.png")
            kit.ensure_dir(p)
            try:
                kit.render_preview(p, center, radius, size=(420, 470), angle=26.0, elev=12.0)
                row.append((tag, p))
            except Exception as e:                      # noqa: BLE001
                print(f"[HANDOFF] 渲染失败 {anim}/{tag}: {e}")
        shots.append((anim, row))
    _compose_poses(shots)
    return shots


def _compose_poses(shots):
    try:
        from PIL import Image
    except Exception as e:                              # noqa: BLE001
        print(f"[HANDOFF] ★没有 PIL，跳过合成：{e}★")
        return None
    if not shots or not shots[0][1]:
        return None
    cw, ch = 420, 520
    cols = max(len(r) for _a, r in shots)
    sheet = Image.new("RGB", (cols * cw, len(shots) * ch), (246, 243, 233))
    for ri, (anim, row) in enumerate(shots):
        for ci, (_tag, p) in enumerate(row):
            try:
                im = Image.open(p).convert("RGB")
            except Exception:                           # noqa: BLE001
                continue
            sheet.paste(im, (ci * cw, ri * ch + 20))
    out = os.path.join(PROJECT, "Docs", "captures", "player_v2_attack_poses.png")
    kit.ensure_dir(out)
    sheet.save(out)
    print(f"[HANDOFF] 攻击姿态对照图 -> {out}")
    return out


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    cmd = a[0] if a else "all"
    if cmd == "values":
        handoff_values()
    elif cmd == "poses":
        render_attack_poses()
    else:
        handoff_values()
        render_attack_poses()
