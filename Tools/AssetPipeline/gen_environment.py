# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 场景环境模块生成器
======================================
产出可直接摆放的**模块化道具**（不是一整块地形）：
农田、栅栏、石头、树木、灌木、草丛、风车小屋、水塘。

尺寸纪律：
  · 农田 1 格 = 1.6m，和 FarmGrid 的格子尺寸对齐（摆放时不用再缩放）
  · 栅栏 1 段 = 2.0m，首尾能无缝接上
  · 角色高 0.7~1.5m，所以树 3~4m、小屋 2.6m，形成合理的高度层次

产出：Assets/Resources/Art/Environment/<id>.fbx
"""

import os
import sys
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit   # noqa: E402

ENV_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\Environment"
D = math.radians

kit.PALETTE.update({
    "Turf":    "#6E9A54",
    "TurfDark": "#4F7A3C",
    "SoilRich": "#5E4029",
    "Plank":   "#B08A5A",
    "PlankDark": "#8A6A42",
    "Roof":    "#B85C4A",
    "Wall":    "#E8DCC0",
    "StoneLt": "#A8A49A",
    "StoneDk": "#7C7870",
    "Canopy":  "#5F9E4E",
    "CanopyDk": "#43793A",
    "Pond":    "#5AA0CC",
})

PLOT = 1.6          # 一格农田的边长
FENCE = 2.0         # 一段栅栏的长度


# ---------------------------------------------------------------- 农田
def build_farm_plot():
    """一格耕地：木框 + 松土。和 FarmGrid 的格子等大，直接按格心摆放。"""
    parts = []
    half = PLOT * 0.5
    t = 0.09        # 木框厚度
    h = 0.16        # 木框高度
    # 四条边
    for (dx, dz, sx, sz) in ((0, half, PLOT, t), (0, -half, PLOT, t),
                             (half, 0, t, PLOT + t), (-half, 0, t, PLOT + t)):
        parts.append(kit.box(f"Frame_{dx:+.1f}_{dz:+.1f}", (dx, dz, h * 0.5),
                             (sx, sz, h), "Plank"))
    # 松土：略微下凹的方块 + 几条垄
    parts.append(kit.box("Soil", (0, 0, 0.045), (PLOT - t * 2, PLOT - t * 2, 0.09), "SoilRich"))
    for i in range(3):
        off = (i - 1) * (PLOT * 0.26)
        parts.append(kit.box(f"Furrow_{i}", (off, 0, 0.095), (PLOT * 0.14, PLOT - t * 2.4, 0.03), "Soil"))
    return parts


# ---------------------------------------------------------------- 栅栏
def build_fence():
    """一段 2m 木栅栏：两根立柱 + 两条横杆。首尾相接可无限延伸。"""
    parts = []
    half = FENCE * 0.5
    for side in (-1, 1):
        parts.append(kit.box(f"Post_{side:+d}", (half * side, 0, 0.36), (0.10, 0.10, 0.72), "PlankDark"))
        parts.append(kit.cone(f"Cap_{side:+d}", (half * side, 0, 0.76), 0.075, 0.02, 0.10, "Plank", verts=8))
    for z in (0.52, 0.28):
        parts.append(kit.box(f"Rail_{z:.2f}", (0, 0, z), (FENCE, 0.055, 0.075), "Plank"))
    return parts


# ---------------------------------------------------------------- 石头
def build_rock(size, squash, color):
    """一块石头：2~3 个交叠的球体压扁而成，比单个球更像石头。"""
    parts = []
    parts.append(kit.sphere("Boulder", (0, 0, size * squash * 0.52), size, color,
                            scale=(1.0, 0.82, squash), segs=14, rings=10, rot=(0, 0, D(12))))
    parts.append(kit.sphere("Chip", (size * 0.62, size * 0.30, size * squash * 0.30),
                            size * 0.46, color, scale=(1.0, 0.9, squash * 0.9), segs=12, rings=8))
    parts.append(kit.sphere("Moss", (-size * 0.35, -size * 0.30, size * squash * 0.80),
                            size * 0.34, "CanopyDk", scale=(1.2, 1.0, 0.30), segs=10, rings=6))
    return parts


# ---------------------------------------------------------------- 树木 / 灌木 / 草
def build_tree():
    """圆冠树：深色树干 + 3 团交叠的树冠（参考美术里树冠都是团块感）。"""
    parts = []
    parts.append(kit.cyl("Trunk", (0, 0, 1.05), 0.16, 2.10, "Bark", verts=12))
    for side in (-1, 1):
        parts.append(kit.cyl(f"Root_{side:+d}", (side * 0.22, 0, 0.16), 0.07, 0.40, "Bark",
                             verts=8, rot=(0, D(34 * side), 0)))
    for (dx, dy, dz, r) in ((-0.45, 0.10, 2.55, 0.98), (0.50, -0.15, 2.42, 0.86), (0.02, 0.22, 3.20, 0.80)):
        parts.append(kit.sphere(f"Canopy_{dx:+.1f}", (dx, dy, dz), r, "Canopy",
                                scale=(1.0, 0.92, 0.86), segs=16, rings=10))
    parts.append(kit.sphere("Canopy_Top", (0, 0, 3.72), 0.58, "CanopyDk",
                            scale=(1.0, 0.95, 0.80), segs=14, rings=8))
    return parts


def build_bush():
    parts = [kit.sphere("Bush", (0, 0, 0.36), 0.46, "Canopy", scale=(1.0, 0.9, 0.78), segs=14, rings=10)]
    parts.append(kit.sphere("Bush_B", (0.34, 0.16, 0.28), 0.30, "CanopyDk",
                            scale=(1.0, 0.9, 0.80), segs=12, rings=8))
    parts.append(kit.sphere("Berry", (-0.24, -0.28, 0.44), 0.075, "Petal", segs=8, rings=6))
    parts.append(kit.sphere("Berry2", (0.20, -0.30, 0.36), 0.065, "Petal", segs=8, rings=6))
    return parts


def build_grass():
    """一丛草：5 片斜插的叶片。用于铺地面细节。"""
    parts = []
    for i in range(5):
        a = i * 72.0
        tilt = 14.0 + (i % 3) * 9.0
        parts.append(kit.cone(f"Blade_{i}", (math.sin(D(a)) * 0.05, math.cos(D(a)) * 0.05, 0.11),
                              0.035, 0.004, 0.24, "TurfDark", verts=6,
                              rot=(D(tilt) * math.cos(D(a)), D(tilt) * math.sin(D(a)), 0)))
    return parts


# ---------------------------------------------------------------- 水塘
def build_pond():
    """小水塘：石头围一圈 + 水面。给场景一个视觉焦点。"""
    parts = []
    r = 1.25
    parts.append(kit.cyl("Pond", (0, 0, 0.035), r, 0.07, "Pond", verts=24, smooth=False))
    for i in range(9):
        a = i * 40.0
        parts.append(kit.sphere(f"Edge_{i}", (math.sin(D(a)) * r, math.cos(D(a)) * r, 0.10),
                                0.26, "StoneLt", scale=(1.15, 1.15, 0.55), segs=10, rings=8,
                                rot=(0, 0, D(a))))
    for i in range(3):
        a = i * 120.0 + 20.0
        parts.append(kit.sphere(f"Lily_{i}", (math.sin(D(a)) * r * 0.45, math.cos(D(a)) * r * 0.45, 0.075),
                                0.16, "Canopy", scale=(1.0, 1.0, 0.16), segs=10, rings=6))
    return parts


# ---------------------------------------------------------------- 风车小屋
def build_windmill():
    """风车小屋：石基 + 木墙 + 红屋顶 + 风车叶。作为场景地标。"""
    parts = []
    # 石基
    parts.append(kit.box("Base", (0, 0, 0.14), (2.30, 2.00, 0.28), "StoneDk"))
    # 木墙
    parts.append(kit.box("Wall", (0, 0, 1.05), (2.00, 1.70, 1.55), "Wall"))
    # 木梁（几根深色横带，木屋的辨识点）
    for z in (0.45, 1.05, 1.62):
        parts.append(kit.box(f"Beam_{z:.2f}", (0, 0, z), (2.06, 1.76, 0.09), "PlankDark"))
    # 屋顶：两层收进的四棱锥 + 屋檐
    parts.append(kit.box("Eave", (0, 0, 1.86), (2.30, 1.98, 0.10), "Roof"))
    parts.append(kit.cone("Roof", (0, 0, 2.28), 1.55, 0.10, 0.80, "Roof", verts=4, rot=(0, 0, D(45))))
    # 门与窗
    parts.append(kit.box("Door", (0, -0.88, 0.62), (0.52, 0.06, 0.86), "PlankDark"))
    for side in (-1, 1):
        parts.append(kit.box(f"Window_{side:+d}", (side * 0.62, -0.88, 1.20), (0.36, 0.06, 0.36), "Pond"))
    # 风车叶：4 片，装在正面偏上
    hub = (0.0, -0.95, 1.55)
    parts.append(kit.cyl("Hub", hub, 0.10, 0.16, "PlankDark", verts=10, rot=(D(90), 0, 0)))
    for i in range(4):
        a = i * 90.0
        parts.append(kit.box(f"Blade_{i}", (math.sin(D(a)) * 0.62, hub[1] - 0.05, hub[2] + math.cos(D(a)) * 0.62),
                             (0.20, 0.05, 1.05), "Plank", rot=(0, D(a), 0)))
    return parts


# ---------------------------------------------------------------- 新增装饰（v64）
def build_flower(color):
    """一株野花：细茎 + 花头 + 两片叶。比作物矮小，用来点缀地面。"""
    parts = []
    parts.append(kit.cyl("Stem", (0, 0, 0.22), 0.018, 0.44, "CanopyDk", verts=8))
    for i, ang in enumerate((-50, 0, 50)):
        parts.append(kit.sphere(f"Leaf_{i}", (math.sin(D(ang)) * 0.07, 0, 0.16 + i * 0.05),
                                0.075, "Leaf", scale=(1.5, 0.4, 0.7), rot=(0, D(ang * 0.5), 0),
                                segs=10, rings=6))
    for i in range(6):
        a = i * 60.0
        parts.append(kit.sphere(f"Petal_{i}", (math.sin(D(a)) * 0.055, math.cos(D(a)) * 0.055, 0.46),
                                0.055, color, scale=(0.9, 0.9, 0.4), segs=8, rings=6))
    parts.append(kit.sphere("Core", (0, 0, 0.47), 0.045, "Gold", scale=(1, 1, 0.6), segs=10, rings=6))
    return parts


def build_mushroom():
    """装饰蘑菇：矮、圆帽、几个白点。和毒蘑菇（作物）区分开——更小更钝。"""
    parts = []
    parts.append(kit.cyl("Stalk", (0, 0, 0.09), 0.045, 0.18, "Bone", verts=12))
    parts.append(kit.sphere("Cap", (0, 0, 0.20), 0.16, "Shroom", scale=(1.0, 1.0, 0.62), segs=16, rings=10))
    for i, (dx, dz) in enumerate(((0.06, 0.05), (-0.07, 0.03), (0.02, -0.08))):
        parts.append(kit.sphere(f"Spot_{i}", (dx, dz, 0.26), 0.035, "Bone", scale=(1, 1, 0.5),
                                segs=8, rings=6))
    return parts


def build_reeds():
    """芦苇丛：几根细高秆 + 穗。放在水塘边。"""
    parts = []
    for i in range(7):
        a = i * 51.0
        h = 0.55 + (i % 3) * 0.16
        r = 0.05 + (i % 4) * 0.035
        parts.append(kit.cyl(f"Reed_{i}", (math.sin(D(a)) * r, math.cos(D(a)) * r, h * 0.5),
                             0.014, h, "CanopyDk", verts=6,
                             rot=(D(6) * math.cos(D(a)), D(6) * math.sin(D(a)), 0)))
        parts.append(kit.sphere(f"Seed_{i}", (math.sin(D(a)) * r * 1.3, math.cos(D(a)) * r * 1.3, h + 0.03),
                                0.035, "Bark", scale=(0.8, 0.8, 1.8), segs=8, rings=6))
    return parts


def build_stump():
    """树桩：切面 + 年轮 + 侧面树皮纹。"""
    parts = []
    parts.append(kit.cyl("Stump", (0, 0, 0.22), 0.22, 0.44, "Bark", verts=14))
    parts.append(kit.cyl("Cut", (0, 0, 0.445), 0.20, 0.02, "Plank", verts=14, smooth=False))
    parts.append(kit.cyl("Ring", (0, 0, 0.455), 0.13, 0.01, "PlankDark", verts=14, smooth=False))
    for i, (dx, dz) in enumerate(((0.16, 0.10), (-0.14, 0.12))):
        parts.append(kit.sphere(f"Root_{i}", (dx, dz, 0.06), 0.10, "Bark",
                                scale=(1.4, 1.2, 0.5), segs=10, rings=6))
    return parts


def build_log():
    """倒木：横躺的原木 + 断面。"""
    parts = []
    parts.append(kit.cyl("Log", (0, 0, 0.18), 0.18, 1.5, "Bark", verts=12, rot=(0, D(90), 0)))
    for side in (-1, 1):
        parts.append(kit.cyl(f"Cut_{side}", (side * 0.75, 0, 0.18), 0.175, 0.02, "Plank",
                             verts=12, rot=(0, D(90), 0), smooth=False))
    parts.append(kit.sphere("Moss", (0.2, -0.10, 0.32), 0.16, "CanopyDk",
                            scale=(1.6, 0.7, 0.3), segs=10, rings=6))
    return parts


def build_barrel():
    """木桶：桶身 + 两道铁箍。给场景一点"人住过"的痕迹。"""
    parts = []
    parts.append(kit.cyl("Body", (0, 0, 0.30), 0.24, 0.60, "Plank", verts=14))
    for z in (0.14, 0.46):
        parts.append(kit.cyl(f"Hoop_{z:.2f}", (0, 0, z), 0.252, 0.05, "StoneDk", verts=14))
    parts.append(kit.cyl("Top", (0, 0, 0.605), 0.225, 0.02, "PlankDark", verts=14, smooth=False))
    return parts


def build_lantern():
    """路灯：木杆 + 灯罩 + 暖光。夜晚场景的视觉锚点。"""
    parts = []
    parts.append(kit.cyl("Post", (0, 0, 1.05), 0.06, 2.10, "PlankDark", verts=10))
    parts.append(kit.box("Arm", (0.16, 0, 2.05), (0.32, 0.06, 0.06), "PlankDark"))
    parts.append(kit.box("Housing", (0.30, 0, 1.92), (0.22, 0.22, 0.26), "StoneDk"))
    parts.append(kit.sphere("Glow", (0.30, 0, 1.86), 0.085, "Gold", emission=4.0, segs=12, rings=8))
    parts.append(kit.cone("Cap", (0.30, 0, 2.10), 0.17, 0.02, 0.10, "StoneDk", verts=8))
    return parts


# ---------------------------------------------------------------- 入口
MODULES = {
    "farm_plot":  ("农田", build_farm_plot),
    "fence":      ("栅栏段", build_fence),
    "rock_a":     ("石头·大", lambda: build_rock(0.62, 0.70, "StoneLt")),
    "rock_b":     ("石头·中", lambda: build_rock(0.40, 0.78, "StoneDk")),
    "rock_c":     ("石头·小", lambda: build_rock(0.24, 0.86, "StoneLt")),
    "tree":       ("圆冠树", build_tree),
    "bush":       ("灌木", build_bush),
    "grass":      ("草丛", build_grass),
    "pond":       ("水塘", build_pond),
    "windmill":   ("风车小屋", build_windmill),
    # v64 新增装饰
    "flower_a":   ("野花·粉", lambda: build_flower("Petal")),
    "flower_b":   ("野花·蓝", lambda: build_flower("PetalB")),
    "mushroom":   ("装饰蘑菇", build_mushroom),
    "reeds":      ("芦苇丛", build_reeds),
    "stump":      ("树桩", build_stump),
    "log":        ("倒木", build_log),
    "barrel":     ("木桶", build_barrel),
    "lantern":    ("路灯", build_lantern),
}


def build(mid):
    label, fn = MODULES[mid]
    kit.reset()
    parts = fn()
    obj = kit.join(parts, "EN_" + mid)
    kit.apply_transforms(obj)
    path = os.path.join(ENV_DIR, mid + ".fbx")
    kit.export_fbx(obj, path)
    dims = tuple(round(v, 3) for v in obj.dimensions)
    print(f"[SEEDFALL-ENV] {mid} ({label}) dims(x,y,z)={dims} tris={kit.tri_count(obj)} -> {path}")
    return obj


def build_all():
    for mid in MODULES:
        build(mid)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("用法: blender -b -P gen_environment.py -- <模块id...> | all")
        print("  可选:", ", ".join(MODULES))
    elif args[0] == "all":
        build_all()
    else:
        for mid in args:
            build(mid)
