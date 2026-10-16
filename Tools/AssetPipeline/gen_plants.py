# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 植物生成器
================================
16 个种子各一个模型。三条约定：

① **轮廓读得出玩法**——不看名字也能猜出它是干什么的：
     近战 → 有拳头/尖刺/厚盾；远程 → 有朝向的"炮口"；辅助 → 带光环或碗状器皿。
② **体量与华丽度跟稀有度走**——D 级小而简，S 级大而繁复（花瓣更多、配饰更足）。
③ **尺寸跟玩法半径对齐**——模型高 ≈ 攻击范围的一个视觉提示，不是随便定的。

产出：Assets/Resources/Art/Plants/<id>.fbx
"""

import os
import sys
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit   # noqa: E402

# 补几个植物专用色（并入 kit 的调色板，材质名即键名）
kit.PALETTE.update({
    "Icicle":  "#BFE3F2",
    "Ember":   "#E8603C",
    "Moon":    "#E8E4C8",
    "Moss":    "#6F9E5A",
    "Shroom":  "#C25B5B",
    "Pumpkin": "#E08A2E",
    "Stoner":  "#9A968C",
    "PetalC":  "#C9A0DC",
    "Bark":    "#6B4F32",
})

PLANT_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\Plants"

D = math.radians

# ── 参数表：从 seeds.json 的玩法属性推导，不是凭空写的 ──
# kind 决定轮廓；size 跟稀有度；face 表示"有个能读表情的正面"
PLANTS = {
    # ── D 级：启蒙兵种，小而简单 ──
    "radish_guard":    dict(kind="root",    size=0.55, body="Petal",  accent="Leaf",   face=True),
    "pea_shooter":     dict(kind="shooter", size=0.60, body="Leaf",   accent="LeafDark", face=True),
    "toxic_mushroom":  dict(kind="fungus",  size=0.58, body="Shroom", accent="Bone",   face=True, spots=5),
    "frost_ivy":       dict(kind="ivy",     size=0.45, body="Icicle", accent="LeafDark", face=False),
    # ── C 级：功能明确，轮廓更有辨识度 ──
    "thorn_pumpkin":   dict(kind="gourd",   size=0.62, body="Pumpkin", accent="LeafDark", face=True, spikes=10),
    "stone_fist":      dict(kind="rock",    size=0.72, body="Stoner", accent="Moss",   face=True, fists=2),
    "chili_mage":      dict(kind="shooter", size=0.66, body="Ember",  accent="LeafDark", face=True, hat="cone"),
    "starvine":        dict(kind="shooter", size=0.64, body="PetalB", accent="Moon",   face=True, hat="star"),
    # ── B 级：体量上一个台阶 ──
    "moontree":        dict(kind="flower",  size=0.86, body="Moon",   accent="Bark",   face=True, petals=10, trunk=True),
    "char_flower":     dict(kind="flower",  size=0.66, body="Ember",  accent="LeafDark", face=True, petals=8),
    "plague_mother":   dict(kind="fungus",  size=0.80, body="Purple", accent="Moss",   face=True, spots=8, big=True),
    "moss_shield":     dict(kind="moss",    size=0.56, body="Moss",   accent="Stoner", face=True, domes=3),
    # ── A 级：队伍核心，配饰最多 ──
    "healing_clover":  dict(kind="flower",  size=0.62, body="Leaf",   accent="Gold",   face=True, petals=4, halo=True),
    "life_spring":     dict(kind="spring",  size=0.70, body="Water",  accent="Stoner", face=True),
    "holy_tree_guard": dict(kind="tree",    size=0.95, body="Bark",   accent="Leaf",   face=True),
    # ── S 级：全场最大 ──
    "ironroot_colossus": dict(kind="rock",  size=1.10, body="Stoner", accent="Moss",   face=True, fists=2, crown=True),
}


# ---------------------------------------------------------------- 通用零件
def _face(parts, z, r, fwd=-1.0, eye="Ink", eyeR=0.045, sep=0.075):
    """两只眼睛：让植物有"能读的正面"。朝向 -Y（Blender 的角色正面）。"""
    for side in (-1, 1):
        parts.append(kit.sphere(f"Eye_{'R' if side > 0 else 'L'}",
                                (side * sep, fwd * r * 0.92, z), eyeR, eye,
                                scale=(1.0, 0.65, 1.15), segs=10, rings=8))


def _mound(parts, r, h, color):
    """底座土丘：几乎所有植物都有，把模型"钉"在地上。"""
    parts.append(kit.sphere("Soil", (0, 0, h * 0.35), r, "Soil", scale=(1.0, 1.0, h / r * 0.5),
                            segs=16, rings=10))
    parts.append(kit.cyl("Ground_Shadow", (0, 0, 0.005), r * 1.5, 0.010, "Ink", verts=20, smooth=False))


def _stem(parts, h, r, color="LeafDark"):
    parts.append(kit.cyl("Stem", (0, 0, h * 0.5), r, h, color, verts=12))


# ---------------------------------------------------------------- 各轮廓
def build_root(c):
    """萝卜卫士：倒水滴身体 + 叶冠 —— 近战挡刀，所以身体厚。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.42, s * 0.22, c["body"])
    parts.append(kit.sphere("Body", (0, 0, s * 0.46), s * 0.34, c["body"],
                            scale=(1.0, 1.0, 1.25), segs=20, rings=14))
    parts.append(kit.cone("Tip", (0, 0, s * 0.16), s * 0.20, 0.004, s * 0.30, c["body"], verts=14))
    for i, ang in enumerate((-40, 0, 40)):
        parts.append(kit.sphere(f"Top_{i}", (math.sin(D(ang)) * s * 0.30, 0, s * 0.80),
                                s * 0.17, c["accent"], scale=(1.5, 0.5, 0.75),
                                rot=(0, D(ang * 0.6), 0), segs=12, rings=8))
    _face(parts, s * 0.50, s * 0.34)
    return parts


def build_shooter(c):
    """远程兵：一个朝向正面的"炮口"，读得出它会发东西。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.38, s * 0.20, c["body"])
    _stem(parts, s * 0.42, s * 0.055)
    # 炮口：向 -Y 伸出的短筒（正面朝向玩家照看的方向）
    parts.append(kit.cyl("Barrel", (0, -s * 0.22, s * 0.58), s * 0.115, s * 0.34, c["body"],
                         verts=14, rot=(D(90), 0, 0)))
    parts.append(kit.cyl("Muzzle", (0, -s * 0.40, s * 0.58), s * 0.14, s * 0.05, c["accent"],
                         verts=14, rot=(D(90), 0, 0)))
    parts.append(kit.sphere("Head", (0, 0, s * 0.60), s * 0.20, c["body"], scale=(1.0, 0.95, 1.0),
                            segs=18, rings=12))
    _face(parts, s * 0.64, s * 0.20, fwd=-1.15, eyeR=s * 0.055)
    # 叶耳
    for side in (-1, 1):
        parts.append(kit.sphere(f"Ear_{'R' if side > 0 else 'L'}",
                                (side * s * 0.20, 0, s * 0.74), s * 0.11, c["accent"],
                                scale=(1.2, 0.45, 0.9), rot=(0, 0, D(20 * side)), segs=10, rings=6))
    if c.get("hat") == "cone":
        parts.append(kit.cone("Hat", (0, 0, s * 0.88), s * 0.20, s * 0.02, s * 0.26, c["accent"], verts=12))
    elif c.get("hat") == "star":
        for i in range(5):
            a = i * 72.0
            parts.append(kit.sphere(f"Star_{i}", (math.sin(D(a)) * s * 0.16, math.cos(D(a)) * s * 0.16, s * 0.86),
                                    s * 0.09, c["accent"], scale=(1.4, 1.4, 0.35), rot=(0, 0, D(a)),
                                    segs=10, rings=6))
    return parts


def build_fungus(c):
    """毒蘑菇 / 瘟疫之母：菌盖 + 斑点。斑点数量=毒性强度的视觉语言。"""
    s = c["size"]
    big = c.get("big", False)
    parts = []
    _mound(parts, s * 0.40, s * 0.20, c["body"])
    stalk_h = s * (0.42 if not big else 0.50)
    parts.append(kit.cyl("Stalk", (0, 0, stalk_h * 0.5 + s * 0.10), s * 0.085, stalk_h, "Bone", verts=14))
    cap_r = s * (0.36 if not big else 0.44)
    parts.append(kit.sphere("Cap", (0, 0, s * 0.10 + stalk_h), cap_r, c["body"],
                            scale=(1.0, 1.0, 0.62), segs=22, rings=12))
    spots = c.get("spots", 5)
    for i in range(spots):
        a = (i / float(spots)) * 360.0
        rr = cap_r * (0.45 + 0.32 * ((i % 3) / 2.0))
        parts.append(kit.sphere(f"Spot_{i}",
                                (math.sin(D(a)) * rr, math.cos(D(a)) * rr, s * 0.10 + stalk_h + cap_r * 0.32),
                                cap_r * 0.17, c["accent"], scale=(1.0, 1.0, 0.5), segs=10, rings=6))
    _face(parts, s * 0.10 + stalk_h * 0.45, s * 0.085, fwd=-1.4, eyeR=s * 0.030)
    return parts


def build_ivy(c):
    """寒霜藤：贴地的藤 + 冰晶叶。低矮、蔓延感。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.50, s * 0.16, c["body"])
    for i in range(7):
        a = i * 51.0
        rad = s * (0.22 + 0.30 * ((i % 3) / 2.0))
        x, y = math.sin(D(a)) * rad, math.cos(D(a)) * rad
        parts.append(kit.cyl(f"Vine_{i}", (x * 0.6, y * 0.6, s * 0.16), s * 0.022, s * 0.36,
                             c["accent"], verts=8, rot=(D(70) * math.cos(D(a)), D(70) * math.sin(D(a)), 0)))
        parts.append(kit.cone(f"Crystal_{i}", (x, y, s * 0.34), s * 0.075, 0.004, s * 0.22,
                              c["body"], verts=6, rot=(D(12) * math.cos(D(a)), D(12) * math.sin(D(a)), 0)))
    return parts


def build_gourd(c):
    """荆棘南瓜：圆形身体 + 尖刺 —— 反伤定位，读得出"别碰我"。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.42, s * 0.20, c["body"])
    r = s * 0.34
    parts.append(kit.sphere("Body", (0, 0, r + s * 0.12), r, c["body"], scale=(1.06, 1.06, 0.90),
                            segs=22, rings=14))
    parts.append(kit.cyl("Ridge", (0, 0, r + s * 0.12), r * 1.02, 0.02, c["accent"], verts=18, smooth=False))
    spikes = c.get("spikes", 10)
    for i in range(spikes):
        a = (i / float(spikes)) * 360.0
        el = D(-20 + 40 * (i % 2))
        parts.append(kit.cone(f"Spike_{i}",
                              (math.sin(D(a)) * r * 1.05, math.cos(D(a)) * r * 1.05, r + s * 0.12 + math.sin(el) * r),
                              s * 0.045, 0.003, s * 0.16, c["accent"], verts=6,
                              rot=(D(90) - el, 0, D(a))))
    parts.append(kit.cyl("Stalk", (0, 0, r * 2.05), s * 0.04, s * 0.14, "LeafDark", verts=10))
    _face(parts, r + s * 0.14, r, fwd=-1.0, eyeR=s * 0.050, sep=s * 0.085)
    return parts


def build_rock(c):
    """石拳 / 铁根巨像：石块身体 + 拳头 —— 近战坦克，体量最大。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.46, s * 0.20, c["body"])
    parts.append(kit.box("Chest", (0, 0, s * 0.44), (s * 0.54, s * 0.40, s * 0.46), c["body"]))
    parts.append(kit.box("Head", (0, 0, s * 0.78), (s * 0.34, s * 0.30, s * 0.26), c["body"]))
    _face(parts, s * 0.79, s * 0.28, fwd=-0.55, eyeR=s * 0.048, sep=s * 0.072)
    # 苔藓斑（石+苔的对比是这类角色的辨识点）
    for i, (dx, dz) in enumerate(((-0.16, 0.60), (0.18, 0.40), (0.05, 0.24))):
        parts.append(kit.sphere(f"Mossy_{i}", (dx * s, -s * 0.20, dz * s), s * 0.12, c["accent"],
                                scale=(1.2, 0.4, 0.8), segs=10, rings=6))
    for side in (-1, 1):
        parts.append(kit.box(f"Shoulder_{'R' if side > 0 else 'L'}",
                             (side * s * 0.40, 0, s * 0.60), (s * 0.22, s * 0.26, s * 0.20), c["accent"]))
        if c.get("fists", 0) >= 2:
            parts.append(kit.cyl(f"Arm_{'R' if side > 0 else 'L'}",
                                 (side * s * 0.44, 0, s * 0.38), s * 0.075, s * 0.26, c["body"], verts=10))
            parts.append(kit.box(f"Fist_{'R' if side > 0 else 'L'}",
                                 (side * s * 0.46, 0, s * 0.22), (s * 0.20, s * 0.22, s * 0.20), c["body"]))
    if c.get("crown"):
        for i, ang in enumerate((-30, 0, 30)):
            parts.append(kit.cone(f"Crown_{i}", (math.sin(D(ang)) * s * 0.18, 0, s * 0.94),
                                  s * 0.06, 0.004, s * 0.22, c["accent"], verts=6, rot=(0, D(ang * 0.5), 0)))
    return parts


def build_moss(c):
    """苔藓盾：低矮土丘 + 减速地带 —— 不攻击，所以没有武器，只有一堆鼓包。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.56, s * 0.22, c["body"])
    domes = c.get("domes", 3)
    for i in range(domes):
        a = (i / float(domes)) * 360.0
        rr = s * (0.20 + 0.10 * (i % 2))
        parts.append(kit.sphere(f"Dome_{i}", (math.sin(D(a)) * rr, math.cos(D(a)) * rr, s * 0.26),
                                s * 0.24, c["body"], scale=(1.0, 1.0, 0.7), segs=14, rings=8))
    parts.append(kit.sphere("Core", (0, 0, s * 0.34), s * 0.24, c["body"], scale=(1.0, 1.0, 0.8),
                            segs=16, rings=10))
    _face(parts, s * 0.36, s * 0.26, fwd=-1.05, eyeR=s * 0.048)
    for i in range(6):
        a = i * 60.0
        parts.append(kit.sphere(f"Pebble_{i}", (math.sin(D(a)) * s * 0.46, math.cos(D(a)) * s * 0.46, s * 0.10),
                                s * 0.075, c["accent"], scale=(1.1, 1.1, 0.6), segs=10, rings=6))
    return parts


def build_flower(c):
    """向日葵家族：花盘 + 花瓣。花瓣数跟稀有度走。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.38, s * 0.18, c["body"])
    trunk = c.get("trunk", False)
    stem_h = s * (0.52 if trunk else 0.40)
    if trunk:
        parts.append(kit.cyl("Trunk", (0, 0, stem_h * 0.5), s * 0.075, stem_h, c["accent"], verts=12))
    else:
        _stem(parts, stem_h, s * 0.048, c["accent"])
    head_z = stem_h + s * 0.06
    petals = c.get("petals", 8)
    petal_r = s * (0.30 if not trunk else 0.34)
    for i in range(petals):
        a = (i / float(petals)) * 360.0
        parts.append(kit.sphere(f"Petal_{i}",
                                (math.sin(D(a)) * petal_r * 0.92, 0, head_z + math.cos(D(a)) * petal_r * 0.92),
                                petal_r * 0.42, c["body"], scale=(0.85, 0.30, 1.25),
                                rot=(0, -D(a), 0), segs=12, rings=8))
    parts.append(kit.sphere("Disc", (0, 0, head_z), petal_r * 0.62, c["accent"],
                            scale=(1.0, 0.34, 1.0), segs=20, rings=10))
    _face(parts, head_z, petal_r * 0.60, fwd=-0.36, eyeR=s * 0.045, sep=s * 0.062)
    if c.get("halo"):
        for i in range(5):
            a = i * 72.0
            parts.append(kit.sphere(f"Halo_{i}", (math.sin(D(a)) * s * 0.30, math.sin(D(a)) * 0.0, head_z + s * 0.34),
                                    s * 0.055, "Gold", scale=(1.0, 1.0, 1.0), segs=8, rings=6))
    return parts


def build_spring(c):
    """生命之泉：碗 + 水滴 —— 辅助定位，所以"容器"是本体。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.44, s * 0.20, c["accent"])
    parts.append(kit.cyl("Basin", (0, 0, s * 0.26), s * 0.30, s * 0.20, c["accent"], verts=18))
    parts.append(kit.sphere("Water", (0, 0, s * 0.36), s * 0.27, c["body"], scale=(1.0, 1.0, 0.40),
                            segs=18, rings=10, emission=1.2))
    parts.append(kit.cyl("Spout", (0, 0, s * 0.52), s * 0.065, s * 0.24, c["accent"], verts=12))
    for i in range(5):
        a = i * 72.0
        parts.append(kit.sphere(f"Drop_{i}", (math.sin(D(a)) * s * 0.22, math.cos(D(a)) * s * 0.22, s * 0.62),
                                s * 0.055, c["body"], scale=(0.8, 0.8, 1.3), emission=1.5,
                                segs=10, rings=8))
    _face(parts, s * 0.28, s * 0.30, fwd=-1.02, eyeR=s * 0.042, sep=s * 0.070)
    return parts


def build_tree(c):
    """圣树守卫：树身 + 树冠 + 光环 —— 最高一档的辅助/近战。"""
    s = c["size"]
    parts = []
    _mound(parts, s * 0.46, s * 0.18, c["body"])
    parts.append(kit.cyl("Trunk", (0, 0, s * 0.34), s * 0.10, s * 0.68, c["body"], verts=14))
    for side in (-1, 1):
        parts.append(kit.cyl(f"Root_{'R' if side > 0 else 'L'}", (side * s * 0.10, 0, s * 0.10),
                             s * 0.04, s * 0.22, c["body"], verts=8, rot=(0, D(28 * side), 0)))
    for i, (dx, dz, rr) in enumerate(((-0.18, 0.74, 0.22), (0.18, 0.72, 0.20), (0.0, 0.86, 0.24))):
        parts.append(kit.sphere(f"Canopy_{i}", (dx * s, 0, dz * s), s * rr, c["accent"],
                                scale=(1.15, 0.95, 0.9), segs=16, rings=10))
    _face(parts, s * 0.46, s * 0.10, fwd=-1.0, eyeR=s * 0.040, sep=s * 0.058)
    for i in range(6):
        a = i * 60.0
        parts.append(kit.sphere(f"Halo_{i}", (math.sin(D(a)) * s * 0.30, math.cos(D(a)) * s * 0.30, s * 1.10),
                                s * 0.045, "Gold", emission=1.0, segs=8, rings=6))
    return parts


def build_young(c):
    """
    v68：成长中模型（种在耕地上、还没成熟时显示的形态）。
    不按 kind 分类——幼苗阶段本来就该"看不出是什么"，只保留两件事：
      · 配色跟成熟形态一致（让玩家能提前认出自己种的是什么）
      · 体量明显更小（约 40%），给"长大"留出可见的变化空间
    结构：短茎 + 一对子叶 + 顶端一个花苞。
    """
    s = c["size"] * 0.42
    parts = []
    _mound(parts, s * 0.55, s * 0.22, c["body"])
    parts.append(kit.cyl("Stem", (0, 0, s * 0.55), s * 0.052, s * 1.10, "LeafDark", verts=10))
    for side in (-1, 1):
        parts.append(kit.sphere(f"Cotyledon_{'R' if side > 0 else 'L'}",
                                (side * s * 0.34, 0, s * 0.86), s * 0.30, c["accent"],
                                scale=(1.25, 0.42, 0.72), rot=(0, 0, D(16 * side)),
                                segs=12, rings=8))
    parts.append(kit.sphere("Bud", (0, 0, s * 1.22), s * 0.26, c["body"],
                            scale=(1.0, 1.0, 1.15), segs=14, rings=10))
    return parts


BUILDERS = {
    "young": build_young,
    "root": build_root, "shooter": build_shooter, "fungus": build_fungus, "ivy": build_ivy,
    "gourd": build_gourd, "rock": build_rock, "moss": build_moss, "flower": build_flower,
    "spring": build_spring, "tree": build_tree,
}


def build(plant_id, young=False):
    c = dict(PLANTS[plant_id])
    kit.reset()
    parts = BUILDERS["young" if young else c["kind"]](c)
    suffix = "_young" if young else ""
    obj = kit.join(parts, "PL_" + plant_id + suffix)
    kit.apply_transforms(obj)
    path = os.path.join(PLANT_DIR, plant_id + suffix + ".fbx")
    kit.export_fbx(obj, path)
    dims = tuple(round(v, 3) for v in obj.dimensions)
    print(f"[SEEDFALL-PLANT] {plant_id}{suffix} dims={dims} tris={kit.tri_count(obj)} -> {path}")
    return obj


def build_all(with_young=True):
    for pid in PLANTS:
        build(pid, young=False)
        if with_young:
            build(pid, young=True)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("用法: blender -b -P gen_plants.py -- <植物id...> | all")
        print("  可选:", ", ".join(PLANTS))
    elif args[0] == "all":
        build_all()
    else:
        for pid in args:
            build(pid)
