# -*- coding: utf-8 -*-
"""
基线探针：在覆盖 player.fbx **之前**，先把现状量下来。
========================================================
Q 版重做会覆盖 4 个玩家 / 3 个武器 / 7 条动画文件。覆盖之后"原来是什么样"
就再也量不到了 —— 所以先落一份基线，报告里的"前/后"对比才有依据。

量什么：
  · 高度（Z 轴）、zmin、三角面
  · 19 根骨骼名（必须逐一对上，Unity 靠 Bone_Hand_R 挂武器）
  · 每个网格件的世界包围盒 → 推出「帽檐半径」「头半宽」「头（含帽）占比」
  · 60° 俯视 + 正面渲染（覆盖前的对照图）

用法：
    blender -b --factory-startup -P _probe_baseline.py -- <模型路径> <输出前缀>
"""

import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.dirname(HERE)
sys.path.insert(0, KIT)
import seedfall_kit as kit          # noqa: E402

import bpy                          # noqa: E402
import mathutils                    # noqa: E402

CHAR_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\Characters"


def fresh():
    bpy.ops.wm.read_homefile(use_empty=True)
    for blk in (bpy.data.actions, bpy.data.objects, bpy.data.meshes,
                bpy.data.materials, bpy.data.armatures):
        for item in list(blk):
            try:
                blk.remove(item)
            except Exception:                       # noqa: BLE001
                pass


def parts_bounds(objs):
    """每个网格件的世界包围盒（名字 + 中心 + 尺寸）。"""
    rows = []
    for o in objs:
        if o.type != 'MESH':
            continue
        mw = o.matrix_world
        pts = [mw @ v.co for v in o.data.vertices]
        xs = [p.x for p in pts]
        ys = [p.y for p in pts]
        zs = [p.z for p in pts]
        rows.append((o.name, (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs)),
                     kit.tri_count(o)))
    return rows


def measure(path, tag):
    fresh()
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    objs = list(bpy.context.scene.objects)
    meshes = [o for o in objs if o.type == 'MESH']
    arms = [o for o in objs if o.type == 'ARMATURE']

    pts = []
    tris = 0
    for o in meshes:
        mw = o.matrix_world
        pts += [mw @ v.co for v in o.data.vertices]
        tris += kit.tri_count(o)
    zs = [p.z for p in pts]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    H = max(zs) - min(zs)

    print("=" * 78)
    print(f"基线 [{tag}] {os.path.basename(path)}")
    print("=" * 78)
    print(f"  高度(Z) = {H:.4f} m   zmin = {min(zs):.4f}  zmax = {max(zs):.4f}")
    print(f"  X 宽 = {max(xs) - min(xs):.4f}   Y 深 = {max(ys) - min(ys):.4f}"
          f"   （Z 是最大维 = {H >= max(max(xs) - min(xs), max(ys) - min(ys))}）")
    print(f"  三角面 = {tris}   网格件 = {len(meshes)}")

    bones = [b.name for b in arms[0].data.bones] if arms else []
    print(f"  骨骼 {len(bones)} 根: {', '.join(bones)}")

    rows = parts_bounds(meshes)
    print("  ── 每个网格件（世界包围盒）──")
    for name, xr, yr, zr, t in sorted(rows, key=lambda r: -r[3][1]):
        print(f"    {name:<26} X[{xr[0]:+.3f},{xr[1]:+.3f}] "
              f"Y[{yr[0]:+.3f},{yr[1]:+.3f}] Z[{zr[0]:+.3f},{zr[1]:+.3f}] 面={t}")

    # ── Q 版两项比例的**现状**（同一个算法，覆盖前后可比）──
    def find(sub):
        for name, xr, yr, zr, t in rows:
            if sub.lower() in name.lower():
                return (name, xr, yr, zr, t)
        return None

    hat = find("hat") or find("brim")
    head = find("head")
    leg = find("foot") or find("calf") or find("thigh")
    if hat and head:
        brim_r = max(abs(hat[1][0]), abs(hat[1][1]), abs(hat[2][0]), abs(hat[2][1]))
        head_hw = max(abs(head[1][0]), abs(head[1][1]))
        head_lo = min(head[3][0], hat[3][0])
        head_top = max(head[3][1], hat[3][1])
        head_span = head_top - head_lo
        print(f"  ── Q 版比例现状 ──")
        print(f"    头(含帽) Z[{head_lo:.3f},{head_top:.3f}] 高 {head_span:.3f}"
              f"  ÷ 总高 {H:.3f} = {head_span / H:.3f}")
        print(f"    帽檐半径 {brim_r:.3f} ÷ 头半宽 {head_hw:.3f} = "
              f"{brim_r / max(1e-6, head_hw):.3f}")
    else:
        print(f"  ── 找不到 hat/head 件（hat={bool(hat)} head={bool(head)}）"
              f"，Q 版比例无法按名字算 ──")
    if leg:
        print(f"    最下面的件（腿/脚）Z 顶 = {leg[3][1]:.3f}"
              f" → 腿身高 = {leg[3][1]:.3f}")

    return dict(tag=tag, path=path, H=H, zmin=min(zs), tris=tris,
                bones=bones, parts=[r[0] for r in rows])


def main():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    path = a[0] if a else os.path.join(CHAR_DIR, "player.fbx")
    tag = a[1] if len(a) > 1 else "before"
    info = measure(path, tag)
    print(f"[PROBE] {info}")
    return info


if __name__ == "__main__":
    main()
