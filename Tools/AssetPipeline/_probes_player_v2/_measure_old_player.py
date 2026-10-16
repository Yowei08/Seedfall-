# -*- coding: utf-8 -*-
"""
基线测量：把**现有**的 player.fbx 回读，量出帽檐、身高、肩宽、以及俯视遮挡比。

为什么要先做这一步：需求是"帽子显著收窄"，不先量出旧值就没法证明"收窄了"。
结论要能写成数字（旧帽檐半径 vs 新帽檐半径），不是"看着小了"。
"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import bpy                          # noqa: E402
import mathutils                    # noqa: E402

PROJECT = r"D:\Unity\project\Seedfall守园者"
CHARS = os.path.join(PROJECT, "Assets", "Resources", "Art", "Characters")


def measure_static(path, tag):
    kit.reset()
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    objs = list(bpy.context.scene.objects)
    print(f"===== {tag}: {os.path.basename(path)} =====")
    for o in sorted(objs, key=lambda x: x.name):
        par = o.parent.name if o.parent else "-"
        print(f"  {o.type:<9} {o.name:<30} parent={par}")

    pts, tris, mats = [], 0, set()
    for o in objs:
        if o.type != 'MESH':
            continue
        pts += [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
        tris += kit.tri_count(o)
        mats |= {m.name for m in o.data.materials if m}
    if not pts:
        print("  ★没有网格★")
        return None
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    print(f"  总包围盒 dims(x宽,y深,z高) = {tuple(round(v,3) for v in dims)}")
    print(f"  三角面 = {tris}   材质 = {sorted(mats)}")
    print(f"  zmin = {min(zs):.4f}  zmax = {max(zs):.4f}")

    # 逐部件量（按 Z 分层找"帽子" —— 帽子是最上面那一段，且横向显著外扩）
    print("  逐网格件（按 zmax 降序）:")
    rows = []
    for o in objs:
        if o.type != 'MESH':
            continue
        ps = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
        ox = max(p.x for p in ps) - min(p.x for p in ps)
        oy = max(p.y for p in ps) - min(p.y for p in ps)
        oz = max(p.z for p in ps) - min(p.z for p in ps)
        rows.append((max(p.z for p in ps), o.name, ox, oy, oz,
                     min(p.z for p in ps), kit.tri_count(o)))
    for zmax, nm, ox, oy, oz, zmin_, t in sorted(rows, reverse=True):
        print(f"    {nm:<30} x={ox:.3f} y={oy:.3f} z={oz:.3f} "
              f"[{zmin_:.3f}..{zmax:.3f}] tris={t}")
    return dict(dims=dims, tris=tris, zmin=min(zs), zmax=max(zs), mats=sorted(mats))


def occlusion(dims, zmax):
    """按 60° 俯视算"帽檐在地面上的横向投影"。

    相机 60° 俯角：水平面上的位移 Δh 对应竖直遮挡 Δh / tan(60°)。
    帽檐半径 R、离地高 zc → 它对**地面**的遮挡半径 ≈ R + zc / tan(60°)。
    这个数越大，角色在地上越像一块圆盘。
    """
    t60 = math.tan(math.radians(60.0))
    R = dims[0] * 0.5
    return R, R + zmax / t60


def main():
    old = measure_static(os.path.join(CHARS, "player.fbx"), "旧模型")
    if old:
        R, proj = occlusion(old["dims"], old["zmax"])
        print(f"  ★旧模型横向半径 R ≈ {R:.3f} m")
        print(f"  ★旧模型在 60° 俯视下的地面遮挡半径 ≈ {proj:.3f} m")
        print(f"  ★旧模型身高 = {old['zmax']:.3f} m（需求说 3.0 —— 若这里是 3.0 则确认）")


if __name__ == "__main__":
    main()
