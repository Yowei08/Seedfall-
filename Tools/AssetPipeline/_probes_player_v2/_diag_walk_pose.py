# -*- coding: utf-8 -*-
"""诊断：直接在 Blender 里逐帧求 walk 姿态，看 Spine.x 到底是多少（不经过导出/回读）。"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gen_player_v2 as gp          # noqa: E402
import bpy                          # noqa: E402


def main():
    arm, meshes = gp.assemble_player()
    frames, loop, _ = gp.ANIMS["walk"]
    total = frames + 1
    print(f"[DW] walk frames={frames} total={total} loop={loop}")
    rows = []
    for f in range(1, total + 1):
        t = (f - 1) / float(frames)
        if f == total:
            t = 0.0
        bpy.context.scene.frame_set(f)
        gp.pose_player("walk", t, arm)
        sx = math.degrees(arm.pose.bones["Bone_Spine"].rotation_euler.x)
        cx = math.degrees(arm.pose.bones["Bone_Chest"].rotation_euler.x)
        br = math.sin(2 * math.pi * t)
        rows.append((f, t, sx, cx, br))
    print("[DW]  帧    t     Spine.x   Chest.x   breathing")
    for f, t, sx, cx, br in rows:
        print(f"[DW]  {f:>3}  {t:.3f}  {sx:>8.2f}  {cx:>8.2f}  {br:>8.3f}")
    sxs = [r[2] for r in rows]
    print(f"[DW] Spine.x 区间 [{min(sxs):.2f}, {max(sxs):.2f}] 摆幅 {max(sxs)-min(sxs):.2f}°")


if __name__ == "__main__":
    main()
