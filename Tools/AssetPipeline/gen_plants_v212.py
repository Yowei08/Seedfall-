#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Seedfall 守园者 —— ★ v212：八株**新高品质植物**的程序化模型（FBX）★

为什么必须真有模型
    `ModelView.Spawn("Plants", <id>)` 去 `Resources/Art/Plants/<id>.fbx` 找资产；
    取不到就**退回占位方块**（`EnsurePlotCropModels` 三档全空）。
    v197 那 6 株机制植物没有 fbx，靠 `WorldView.Prog.cs` 里六个程序化形状兜住；
    本轮新增的 8 株**不在那个形状表里**（那张表按 id 硬编码）⇒ 没有 fbx 就是 8 个一模一样的方块 ✗
    ⇒ 这个脚本给 8 株各生成一个成熟体 fbx ✓

规格（与既有 16 株成熟体同口径，见 `gen_plants_qstyle.py` 的文件头）
   · 原点在**脚底中心**（Blender zmin = 0）、高度沿 **Z**、正面朝 Blender **−Y**
   · 成熟体高 **1.68~1.78 m**、三角面 < 1500
   · 导出走 `seedfall_kit.export_fbx`（`axis_forward='-Z'`, `axis_up='Y'`，
     **不自己再烘 Y-up** —— 那个坑写在 `export_fbx` 的注释里）
   · 几何全部来自 Blender 内建图元 + 固定参数 ⇒ **确定性**（同参数每次同结果）

重跑命令（无头，不需要打开 Blender 界面）
   & "D:\\Steam\\steamapps\\common\\Blender\\blender.exe" -b -P ^
       "D:\\Unity\\project\\Seedfall守园者\\Tools\\AssetPipeline\\gen_plants_v212.py"

参数
   --only <id>   只重做一株
   --report     只打印已有 fbx 的尺寸/面数（不生成）
"""

import math
import os
import sys

import bpy
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import seedfall_kit as kit          # noqa: E402  （必须先把本目录塞进 sys.path）

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
PLANT_DIR = os.path.join(ROOT, "Assets", "Resources", "Art", "Plants")

# ══════════════════════════════════════════════════════════════════════════
# 一、调色板（与 seeds.json 里的名字/形态说明同源；数值是 sRGB 0..1）
# ══════════════════════════════════════════════════════════════════════════
C = {
    "berry":      (0.36, 0.25, 0.48),
    "berry_hi":   (0.64, 0.42, 0.84),
    "twig":       (0.29, 0.23, 0.18),
    "leaf_dark":  (0.31, 0.48, 0.27),
    "gourd":      (0.18, 0.50, 0.55),
    "gourd_hi":   (0.50, 0.83, 0.87),
    "stem":       (0.29, 0.23, 0.18),
    "vine":       (0.50, 0.70, 0.35),
    "vine_in":    (0.25, 0.42, 0.20),
    "cactus":     (0.56, 0.64, 0.29),
    "spine":      (0.85, 0.76, 0.48),
    "bell":       (0.85, 0.61, 0.17),
    "bell_hi":    (0.95, 0.78, 0.29),
    "stalk":      (0.25, 0.42, 0.20),
    "star":       (0.42, 0.29, 0.65),
    "star_hi":    (0.73, 0.55, 0.91),
    "trunk":      (0.35, 0.25, 0.19),
    "canopy":     (0.31, 0.48, 0.27),
    "reed":       (0.44, 0.64, 0.31),
    "reed_hi":    (0.66, 0.83, 0.42),
}


# ══════════════════════════════════════════════════════════════════════════
# 二、图元小工具（全部确定性：没有 random、没有时间）
# ══════════════════════════════════════════════════════════════════════════
def reset_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    # ★ 材质缓存必须一起清 ★ 否则 `_mat_cache` 里留着**已被删除的数据块引用**，
    #   下一株一碰就是 `ReferenceError: StructRNA of type Material has been removed`
    #   （实测：第 2 株 tide_gourd 在第 1 个球上炸）
    _mat_cache.clear()
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.objects):
        for item in list(block):
            if getattr(item, "users", 0) == 0:
                block.remove(item)
    bpy.context.view_layer.update()


_mat_cache = {}


def mat(name):
    """按名字取/建材质：Principled BSDF + 纯色（无贴图，导出后 Unity 侧统一重挂 Toon）。"""
    if name in _mat_cache:
        return _mat_cache[name]
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    rgb = C[name]
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = 0.65
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = 0.0
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
    _mat_cache[name] = m
    return m


def _finish(obj, color):
    obj.data.materials.clear()
    obj.data.materials.append(mat(color))
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def sphere(loc, scale, color, segments=16, rings=10, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1.0, location=loc)
    o = bpy.context.active_object
    o.scale = scale
    o.rotation_euler = rot
    return _finish(o, color)


def cone(loc, r1, r2, depth, color, vertices=12, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2, depth=depth, location=loc)
    o = bpy.context.active_object
    o.rotation_euler = rot
    return _finish(o, color)


def cylinder(loc, r, depth, color, vertices=14, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=r, depth=depth, location=loc)
    o = bpy.context.active_object
    o.rotation_euler = rot
    o.scale = scale
    return _finish(o, color)


def torus(loc, major_r, minor_r, color, rot=(0, 0, 0), scale=(1, 1, 1), major_seg=18, minor_seg=8):
    bpy.ops.mesh.primitive_torus_add(major_radius=major_r, minor_radius=minor_r,
                                     major_segments=major_seg, minor_segments=minor_seg,
                                     location=loc)
    o = bpy.context.active_object
    o.rotation_euler = rot
    o.scale = scale
    return _finish(o, color)


def star_pad(loc, r_out, r_in, points, depth, color, rot=(0, 0, 0)):
    """星形扁盘：先画 2N 边多边形再挤出（确定性顶点顺序）。"""
    verts, faces = [], []
    n = points * 2
    for i in range(n):
        ang = math.pi / 2.0 + i * math.pi / points
        rr = r_out if i % 2 == 0 else r_in
        verts.append((rr * math.cos(ang), rr * math.sin(ang), 0.0))
    faces.append(list(range(n)))
    faces.append(list(reversed(range(n))))
    me = bpy.data.meshes.new("pad")
    me.from_pydata(verts, [], faces)
    me.update()
    o = bpy.data.objects.new("Pad", me)
    bpy.context.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = rot
    o.scale = (1.0, 1.0, max(0.02, depth))
    sol = o.modifiers.new("sol", 'SOLIDIFY')
    sol.thickness = 0.05
    sol.offset = 0.0
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=sol.name)
    return _finish(o, color)


def join_all(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    kit.apply_transforms(o)
    return o


# ══════════════════════════════════════════════════════════════════════════
# 三、八株的造型（高度都在 1.68~1.78m，正面朝 −Y）
# ══════════════════════════════════════════════════════════════════════════
def build_dusk_berry():
    """暮莓：矮灌木 —— 一根主茎 + 6 根弯枝 + 6 颗浆果 + 3 片叶。

    ★ 面数预算 ★ 成熟体上限 1500 tris；第一版用 16×10 的球做了 8 颗浆果 ⇒ 实测 2860 ✗
      降到 8×6 的球 + 8 边的柱，整株 ≈ 850 ✓
    """
    parts = [cylinder((0, 0, 0.70), 0.10, 1.40, "twig", vertices=8)]
    for i in range(6):
        ang = i * math.pi / 3.0
        rad = 0.42 + 0.05 * (i % 2)
        h = 0.72 + 0.16 * (i % 3)
        parts.append(cylinder((rad * math.cos(ang) * 0.6, rad * math.sin(ang) * 0.6, h * 0.72),
                              0.045, 0.95,
                              "twig", vertices=8,
                              rot=(math.radians(26), 0, ang)))
    for i in range(6):
        ang = i * math.pi / 3.0 + 0.3
        rad = 0.46 + 0.07 * (i % 3)
        h = 0.92 + 0.30 * (i % 4) * 0.5
        parts.append(sphere((rad * math.cos(ang), rad * math.sin(ang), h),
                            (0.11, 0.11, 0.11), "berry" if i % 3 else "berry_hi",
                            segments=8, rings=6))
    for i in range(3):
        ang = i * 2.0 * math.pi / 3.0 + 0.9
        parts.append(sphere((0.40 * math.cos(ang), 0.40 * math.sin(ang), 0.52),
                            (0.20, 0.09, 0.12), "leaf_dark", segments=8, rings=4,
                            rot=(0, math.radians(28), ang)))
    return join_all(parts, "dusk_berry")


def build_tide_gourd():
    """潮瓜：双球葫芦 + 卷曲的茎 + 一道浅色高光带。（面数预算 < 1500：球降到 16×10）"""
    parts = [
        sphere((0, 0, 0.52), (0.52, 0.52, 0.46), "gourd", segments=16, rings=10),
        sphere((0, 0, 1.15), (0.36, 0.36, 0.34), "gourd", segments=14, rings=9),
        torus((0, 0, 0.86), 0.20, 0.05, "gourd_hi", rot=(0, 0, 0), scale=(1, 1, 1.1),
              major_seg=14, minor_seg=6),
        cylinder((0, 0, 1.46), 0.06, 0.20, "stem", vertices=10),
        torus((0.10, 0, 1.56), 0.13, 0.035, "stem", rot=(math.radians(72), 0, 0),
              major_seg=12, minor_seg=5),
        sphere((0.38, -0.30, 0.30), (0.26, 0.10, 0.08), "leaf_dark", segments=10, rings=6,
               rot=(0, 0, math.radians(-24))),
    ]
    return join_all(parts, "tide_gourd")


def build_hollow_vine():
    """空藤：拱起的空心管（三段圆环＋两端开口）+ 两片叶。"""
    parts = [
        cylinder((0, 0, 0.36), 0.13, 0.72, "vine"),
        torus((0, 0, 0.76), 0.52, 0.13, "vine", rot=(math.radians(90), 0, 0), scale=(1, 1, 1.7)),
        cylinder((0, 0, 1.28), 0.13, 0.34, "vine"),
        cylinder((0, 0, 1.40), 0.10, 0.28, "vine_in"),   # 口沿的暗面（读得出"中空"）
        sphere((-0.44, -0.16, 0.90), (0.26, 0.11, 0.07), "vine_in", rot=(0, math.radians(-18), 0)),
        sphere((0.46, -0.10, 1.06), (0.24, 0.10, 0.07), "vine_in", rot=(0, math.radians(16), 0)),
    ]
    return join_all(parts, "hollow_vine")


def build_dune_cactus():
    """沙掌：三片扁掌 + 短刺 + 一撮沙根。"""
    parts = [
        cylinder((0, 0, 0.22), 0.28, 0.44, "cactus"),
        sphere((0, 0, 0.92), (0.34, 0.20, 0.56), "cactus", segments=16, rings=10),
        sphere((-0.36, 0.02, 1.18), (0.24, 0.16, 0.34), "cactus", segments=14, rings=9,
               rot=(0, math.radians(-22), 0)),
        sphere((0.34, 0.00, 1.32), (0.22, 0.15, 0.30), "cactus", segments=14, rings=9,
               rot=(0, math.radians(20), 0)),
    ]
    for i in range(10):
        ang = i * math.pi / 5.0
        r = 0.30 + 0.04 * (i % 3)
        z = 0.70 + 0.10 * (i % 6)
        parts.append(cone((r * math.cos(ang), r * math.sin(ang) * 0.6, z), 0.026, 0.0, 0.16,
                          "spine", vertices=6, rot=(math.radians(78), 0, ang)))
    parts.append(sphere((0, 0, 0.06), (0.40, 0.40, 0.07), "spine", segments=14, rings=6))
    return join_all(parts, "dune_cactus")


def build_harvest_bell():
    """丰收铃：一根主茎 ＋ 三只下垂的铃形花 ＋ 金色花蕊。"""
    parts = [cylinder((0, 0, 0.84), 0.055, 1.68, "stalk")]
    for i in range(3):
        ang = i * 2.0 * math.pi / 3.0
        h = 1.16 + 0.16 * i
        dx, dy = 0.30 * math.cos(ang), 0.30 * math.sin(ang)
        parts.append(cylinder((dx * 0.5, dy * 0.5, h - 0.10), 0.028, 0.36, "stalk",
                              rot=(math.radians(52), 0, ang)))
        parts.append(cone((dx, dy, h - 0.30), 0.19, 0.055, 0.34, "bell", vertices=12,
                          rot=(math.radians(180), 0, 0)))
        parts.append(torus((dx, dy, h - 0.46), 0.19, 0.026, "bell_hi", rot=(0, 0, 0),
                           major_seg=12, minor_seg=6))
        parts.append(sphere((dx, dy, h - 0.52), (0.045, 0.045, 0.075), "bell_hi", segments=8, rings=6))
    parts.append(sphere((0.30, -0.18, 0.34), (0.26, 0.10, 0.07), "stalk", rot=(0, 0, math.radians(-26))))
    return join_all(parts, "harvest_bell")


def build_star_ladder():
    """星梯：四层星盘层层缩小叠成塔 ＋ 一根主轴。"""
    parts = [cylinder((0, 0, 0.84), 0.06, 1.68, "stalk")]
    layers = [(0.52, 0.60, 5), (0.66, 0.46, 5), (0.86, 0.36, 5), (1.08, 0.26, 5)]
    for i, (z, r, pts) in enumerate(layers):
        parts.append(star_pad((0, 0, z), r, r * 0.46, pts, 0.10, "star" if i % 2 == 0 else "star_hi"))
    parts.append(star_pad((0, 0, 1.44), 0.20, 0.09, 5, 0.12, "star_hi"))
    return join_all(parts, "star_ladder")


def build_elder_oak():
    """古木：粗矮树干（三节错层）＋ 两团树冠 ＋ 露出的根。"""
    parts = [
        cylinder((0, 0, 0.42), 0.34, 0.84, "trunk", vertices=16),
        cylinder((0.02, 0.01, 1.02), 0.29, 0.42, "trunk", vertices=16, rot=(0, math.radians(3), 0)),
        sphere((0, 0, 1.40), (0.62, 0.58, 0.40), "canopy", segments=18, rings=11),
        sphere((-0.34, 0.16, 1.24), (0.42, 0.38, 0.30), "canopy", segments=16, rings=9),
        sphere((0.36, -0.12, 1.20), (0.36, 0.34, 0.26), "canopy", segments=16, rings=9),
    ]
    for i in range(5):
        ang = i * 2.0 * math.pi / 5.0
        parts.append(cylinder((0.30 * math.cos(ang), 0.30 * math.sin(ang), 0.09),
                              0.075, 0.36, "trunk", rot=(math.radians(74), 0, ang)))
    return join_all(parts, "elder_oak")


def build_quick_reed():
    """疾苇：四根细高叶 ＋ 一撮根部 ＋ 亮色叶尖。"""
    parts = [sphere((0, 0, 0.10), (0.34, 0.30, 0.12), "stalk", segments=14, rings=8)]
    blades = [(0.0, 0.94, 5.0), (0.9, 1.02, 3.0), (2.0, 0.88, -4.0), (3.1, 1.06, 2.0), (4.4, 0.82, -3.0)]
    for i, (ang, hscale, tilt) in enumerate(blades):
        r = 0.16 + 0.04 * (i % 3)
        h = 1.30 * hscale
        parts.append(cone((r * math.cos(ang), r * math.sin(ang), 0.12 + h * 0.5),
                          0.075, 0.012, h, "reed", vertices=8,
                          rot=(math.radians(tilt * 1.6), 0, ang)))
        parts.append(cone((r * math.cos(ang) * 1.22, r * math.sin(ang) * 1.22, 0.14 + h * 0.86),
                          0.045, 0.008, h * 0.30, "reed_hi", vertices=8,
                          rot=(math.radians(tilt * 1.6), 0, ang)))
    return join_all(parts, "quick_reed")


BUILDERS = {
    "dusk_berry": build_dusk_berry,
    "tide_gourd": build_tide_gourd,
    "hollow_vine": build_hollow_vine,
    "dune_cactus": build_dune_cactus,
    "harvest_bell": build_harvest_bell,
    "star_ladder": build_star_ladder,
    "elder_oak": build_elder_oak,
    "quick_reed": build_quick_reed,
}

TARGET_HEIGHT = 1.72          # 目标成熟高（1.68~1.78 区间中点）


def normalise(o):
    """把造型缩放到目标高度，并把**原点钉在脚底中心**（与既有成熟体同口径）。

    ★ 为什么不是"直接写 o.location" ★
      `kit.apply_transforms` 只烘 rotation/scale，**不烘 location**（`location=False`），
      而 `join_all` 之后物体的原点停在"第一个部件"的位置（实测：暮莓原点在 z=0.70、丰收铃在 0.84）
      ⇒ 写 location 只是把整个物体平移，原点依旧不在脚底，导出到 Unity 后
      "把植物放到格子上"就会整体下沉或悬空 ✗（第一版实测 zmin = −0.70 ~ −0.84）
    ⇒ 正确做法是 `kit.set_origin(..., at=脚底中心)`：它把**顶点**按新原点重算 ⇒
      原点 = 脚底中心，再把 location 归零，几何就正好站在 z=0 上 ✓
    """
    kit.apply_transforms(o)
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    zs = [p.z for p in pts]
    h = max(1e-6, max(zs) - min(zs))
    s = TARGET_HEIGHT / h
    o.scale = (s, s, s)
    kit.apply_transforms(o)
    bpy.context.view_layer.update()

    pts = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zmin = min(p.z for p in pts)
    cx = (min(xs) + max(xs)) * 0.5
    cy = (min(ys) + max(ys)) * 0.5
    kit.set_origin(o, at=(cx, cy, zmin))
    o.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()
    return o


def build_one(pid):
    reset_scene()
    o = BUILDERS[pid]()
    normalise(o)
    path = os.path.join(PLANT_DIR, pid + ".fbx")
    kit.export_fbx(o, path)
    meta = path + ".meta"
    if os.path.isfile(meta):
        os.remove(meta)                 # 与既有管线一致：让 Unity 重新生成
    tris = kit.tri_count(o)
    pts = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    zs = [p.z for p in pts]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    print("[v212] %-14s tris=%-5d height=%.3f zmin=%.3f footprint=%.2fx%.2f  -> %s"
          % (pid, tris, max(zs) - min(zs), min(zs), max(xs) - min(xs), max(ys) - min(ys),
             os.path.basename(path)))
    return tris


def main(argv):
    only = None
    if "--only" in argv:
        only = argv[argv.index("--only") + 1]
    ids = [only] if only else list(BUILDERS.keys())
    if "--report" in argv:
        for pid in ids:
            p = os.path.join(PLANT_DIR, pid + ".fbx")
            print("[v212] report %-14s exists=%s size=%s"
                  % (pid, os.path.isfile(p), os.path.getsize(p) if os.path.isfile(p) else 0))
        return 0
    os.makedirs(PLANT_DIR, exist_ok=True)
    total = 0
    for pid in ids:
        total += build_one(pid)
    print("[v212] done: %d plants, %d tris total" % (len(ids), total))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
