# -*- coding: utf-8 -*-
"""诊断 4：把 gp.bone_parent 与 parent_set 直连对比，并逐件追出漂移发生在第几件。"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_player_v2 as gp          # noqa: E402
import bpy                          # noqa: E402
import mathutils                    # noqa: E402


def world_extent(o):
    mw = o.matrix_world
    pts = [mw @ v.co for v in o.data.vertices]
    xs = [p.x for p in pts]; zs = [p.z for p in pts]
    return (round(min(xs), 4), round(max(xs), 4), round(min(zs), 4), round(max(zs), 4))


def main():
    # ── A) gp.bone_parent 单件测试 ──
    kit.reset()
    arm = gp.make_armature()
    a = kit.box("T_Spine", (0.0, 0.0, 1.10), (0.2, 0.1, 0.1), "Cloth")
    kit.apply_transforms(a)
    want = world_extent(a)
    gp.bone_parent(a, arm, "Bone_Spine")
    got = world_extent(a)
    print(f"[D4] gp.bone_parent T_Spine 期望={want} 实得={got} "
          f"{'OK' if want == got else '★漂移★'}")

    # ── B) 完整 assemble_player，逐件对比"绑定时"与"全部绑完后"的世界范围 ──
    kit.reset()
    arm = gp.make_armature()
    parts = gp.build_player_parts()
    snap_before = {}
    for o, bone in parts:
        snap_before[o.name] = world_extent(o)
    snap_after = {}
    for i, (o, bone) in enumerate(parts):
        gp.bone_parent(o, arm, bone)
        snap_after[o.name] = world_extent(o)

    print("[D4] --- 逐件：绑定前 → 绑定后（绑完即测）→ 全部绑完后再测 ---")
    bpy.context.view_layer.update()
    bad = 0
    for o, bone in parts:
        now = world_extent(o)
        b0 = snap_before[o.name]
        a0 = snap_after[o.name]
        ok = (abs(now[0] - b0[0]) < 0.002 and abs(now[2] - b0[2]) < 0.002)
        if not ok:
            bad += 1
        flag = "" if ok else "  ★漂移★"
        print(f"[D4] {o.name:<22} bone={bone:<18} 期望 z[{b0[2]},{b0[3]}] "
              f"绑后 z[{a0[2]},{a0[3]}] 终态 x[{now[0]},{now[1]}] z[{now[2]},{now[3]}]{flag}")
    print(f"[D4] ★漂移件数 = {bad} / {len(parts)}★")


if __name__ == "__main__":
    main()
