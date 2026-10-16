# -*- coding: utf-8 -*-
"""诊断：骨头父子绑定后，网格件的真实世界尺寸（用顶点算，不用 bound_box）。"""
import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_player_v2 as gp          # noqa: E402
import bpy                          # noqa: E402
import mathutils                    # noqa: E402

V = mathutils.Vector


def true_pts(objs):
    pts = []
    for o in objs:
        if o.type != 'MESH':
            continue
        mw = o.matrix_world
        for v in o.data.vertices:
            pts.append(mw @ v.co)
    return pts


def main():
    arm, meshes = gp.assemble_player()
    bpy.context.view_layer.update()

    print("[DIAG] --- 逐件：物体矩阵的缩放 / 世界顶点包围盒 ---")
    for o in meshes:
        mw = o.matrix_world
        sc = mw.to_scale()
        pts = [mw @ v.co for v in o.data.vertices]
        xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
        print(f"[DIAG] {o.name:<22} parent={o.parent.name if o.parent else '-':<10} "
              f"bone={o.parent_bone:<18} "
              f"objscale=({sc.x:.3f},{sc.y:.3f},{sc.z:.3f}) "
              f"local_dim=({o.dimensions.x:.3f},{o.dimensions.y:.3f},{o.dimensions.z:.3f}) "
              f"world x[{min(xs):.3f},{max(xs):.3f}] "
              f"z[{min(zs):.3f},{max(zs):.3f}]")

    pts = true_pts(meshes)
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print(f"[DIAG] ★世界顶点并集 dims = ({max(xs)-min(xs):.3f}, {max(ys)-min(ys):.3f}, "
          f"{max(zs)-min(zs):.3f})  zmin={min(zs):.4f} zmax={max(zs):.4f}★")

    # 试一下：把 armature 的显示缩放/变换清干净会不会变
    print(f"[DIAG] armature matrix_world scale = {arm.matrix_world.to_scale()}")
    print(f"[DIAG] armature matrix_world = {arm.matrix_world}")


if __name__ == "__main__":
    main()
