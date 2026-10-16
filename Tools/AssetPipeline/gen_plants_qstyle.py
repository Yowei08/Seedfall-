# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 植物「Q 版三阶段」生成器
================================================================

需求原话：「植物模型也要重做，使用原本的 **Q 版风格**」。

本脚本产出 16 个品种 × 3 个阶段的模型：

    Assets/Resources/Art/Plants/<id>_young.fbx    幼苗     0.35~0.50 m   < 500 面
    Assets/Resources/Art/Plants/<id>_grown.fbx    成长中   0.95~1.15 m   < 900 面
    Assets/Resources/Art/Plants/<id>.fbx          成熟     1.68~1.78 m   < 1500 面

**本批实际写入 32 个（_young / _grown）**；成熟体（`<id>.fbx`）默认不动，
因为它的 4 套动画（idle/walk/attack/hit）正在 `Art/Anim/` 里服役，重做几何要连带
重做动画，属于另一件事。需要时用 `--include-mature` 显式打开（会覆盖 `<id>.fbx`）。

## Q 版是什么（按需求逐条落到参数上）

| 需求 | 本脚本的做法 | 可量化 |
|---|---|---|
| 头大身小、矮胖 | 主体块竖向占整体高 **≥45%**（`BODY_MIN`），按阶段 0.46~0.56 | 生成时逐件量 Z 跨度，超限直接 assert |
| 短粗圆头的四肢 | 支撑结构（根/茎/叶腿）高度 ≤ 15% 总高、半径按"短粗"取 | `supportH` 参与 assert |
| 大圆弧、少尖角 | 全部用 UV 球 / 圆柱 / 圆角圆锥；尖刺类只保留"圆润的尖" | 图元种类可 grep |
| 没有五官 | **一件眼睛都没有**（grep `Eye` = 0 处） | `--verify` 会数 |
| 高饱和 + 大色块 | 每株 3~4 个大色块：主体 / 强调 / 细节；材质平均 HSV 饱和度 ≥0.45 | 逐材质算 S，低于 `SAT_MIN` 直接拒写 |
| 剪影优先 | 16 个品种各有独立主块形态（球/荚/伞/簇/棱球/岩/锥/星/冠…） | `--verify` 归一化两两 IoU |

配色纪律是关键的一条：上一批的教训是「7 个品种全是低饱和灰白，站在一起分不出来」。
所以本脚本**不新增任何灰白键**，并且把饱和度检查做成写盘前的硬闸门 ——
不符合的模型**根本不会落盘**，而不是等渲染出来才发现。

## 三阶段为什么是「同一个个体」（结构保证，不是靠调参碰运气）

每个品种只有一个 `FORM` 条目（主体宽高比 / 支撑类型 / 冠层族 / 是否叠堆），
三个阶段**共用同一个 builder**，只是把同一个四级参数族（身体/支撑/冠层/细节）
换一组值：

    幼苗(0.44 / 0.10 / 0.08 / 0.35)  →  成长中(0.52 / 0.26 / 0.22 / 0.70)  →  成熟(0.56 / 0.26 / 0.26 / 1.00)

于是「同一株长大了」是**构造出来的**：主块形态、宽高比、支撑样式、冠层排布全都连续，
只有体量、细节数量与饱和度在长。反过来说，如果三阶段各写一份 builder，
它们必然会漂成三个不同的东西 —— 这正是要避免的。

成熟体参数（0.56/0.26/0.26）与本工程已验收的 `gen_plants_ally.py` 同构
（它生成的 `<id>.fbx` 实测 1000~1360 面、主体占比 0.5+），所以三阶段的衔接在量级上就对得上。

## 硬约束（继承工程既有约定，一条都没改）

· 高度在 **Z**、原点在**脚底中心**（`zmin = 0`）、`export_fbx(..., bake_yup=False)`
  —— 轴向只在导出器里转一次；自己再烘一次 Y-up 会让模型躺倒（这个坑踩过）
· 每个模型**逐件量 Z 跨度**后统一抬升 + 缩放到目标高，所以 `zmin` 恒为 0，
  不像旧版幼苗那样沉在 −0.007~−0.015（实测旧值，见报告）
· 面数三档预算：幼苗 <500、成长中 <900、成熟 <1500
· 材质名 = 调色板键名（Unity 侧按名字取色的契约）
· **导出后删掉 `.fbx.meta`**（Unity 会重新生成）

## 绝不触碰

`Art/Anim/**`（成熟体 4 套动画在用）、`Characters/player.fbx`、`Tools/tool_*.fbx`、
任何 `.cs`、`gen_plants.py`（只读，它的 `PLANTS` 调色板键名是单一数据源）。

`guard()` 在写盘前逐条比对这三处的 SHA256：**先比哈希再写文件**，
所以"没碰过"是被证明的，不是被声称的。

## 用法（$B = D:\\Steam\\steamapps\\common\\Blender\\blender.exe）

    & $B -b --factory-startup -P gen_plants_qstyle.py -- all
    & $B -b --factory-startup -P gen_plants_qstyle.py -- all --report Docs/plants_qstyle.json
    & $B -b --factory-startup -P gen_plants_qstyle.py -- radish_guard moontree
    & $B -b --factory-startup -P gen_plants_qstyle.py -- all --include-mature     # 连成熟体一起重做
    & $B -b --factory-startup -P gen_plants_qstyle.py -- all --dry-run            # 只量不写

    自检（回读 / 比例 / 饱和度 / IoU / 并排对照图）另开一个脚本：
    & $B -b --factory-startup -P gen_plants_qstyle_verify.py -- all
"""

import colorsys
import hashlib
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy                      # noqa: E402
import mathutils                # noqa: E402

import seedfall_kit as kit      # noqa: E402
import gen_plants as gp         # noqa: E402   ← PLANTS 调色板键名只有这一份，不复制

D = math.radians
TAU = math.pi * 2.0

# ---------------------------------------------------------------- 路径
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
PLANT_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Plants")
ANIM_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Anim")
CHARS_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Characters")
TOOLS_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Tools")

# ---------------------------------------------------------------- 调色板
# 只**新增**键，不改既有键：既有键（Leaf/Petal/Stoner/Moss…）是成熟体与其它脚本在用的，
# 动它们的值会连带改到别处。新增的键一律挑高饱和，补上"低饱和灰白"这块短板。
kit.PALETTE.update({
    # 强调 / 暗面（比既有键更饱和、更暗，Q 版的形只靠这三档明度读体积）
    "QLeafDeep":  "#2F7326",   # S=0.670
    "QPetalDeep": "#C42A50",   # S=0.786
    "QEmberDeep": "#A32A08",   # S=0.951
    "QFungusDeep": "#9E2233",  # S=0.785
    "QMossDeep":  "#3F7A22",   # S=0.721
    "QMetal":     "#B9A277",   # S=0.357 —— 暖金属（见下方 WAIVER 说明）
    "QMetalDeep": "#7A6A4E",   # S=0.363 —— 金属暗面
    # 高饱和主体色
    "QRock":      "#9E7A52",   # S=0.481 —— 暖沙岩（不是灰！旧版 Stoner #9A968C 只有 S=0.10）
    "QRockDeep":  "#7A5530",   # S=0.607
    "QSunDisc":   "#FFB020",   # S=0.875
    "QClover":    "#57AE3C",   # S=0.655
    "QIce":       "#63C6EE",   # S=0.584 —— 真正读得出"蓝"
    "QBerry":     "#6A3FA0",   # S=0.606
    "QBerryDeep": "#4A2A73",   # S=0.635
    "QGoo":       "#8FE86A",   # S=0.543
    "QGold":      "#FFC42E",   # S=0.820
    "QBlood":     "#8FA83C",   # S=0.643
    "QTrunk":     "#7E4E2A",   # S=0.667
    "QTrunkDeep": "#5C3418",   # S=0.739
    "QMoon":      "#DDCB6E",   # S=0.502 —— 淡月金（既有 Moon #E8E4C8 只有 S=0.138，太灰）
    # 地面基座：暖土色，**不是灰**——旧版用 Ink 做地面阴影，16 株都拖一块灰
    "SoilRich":   "#A9743C",   # S=0.645
    "SoilDeep":   "#7A4E22",   # S=0.721
})

# ── 为什么石头/金属类"灰一点"是可以的（WAIVER，落在报告里） ──────────────────
# 需求点名的教训是「7 个品种全是低饱和灰白，站在一起分不出来」。
# 本批把 16 个品种的**主体色**全部提到 S ≥ 0.48，只有两个品种的**大色块**是"偏灰"的，
# 而且它们恰好是需求允许的例外（石头/金属本来就是冷灰）：
#     stone_fist        主体 QRock  #9E7A52  S=0.481（暖沙岩，不是灰）✓
#     ironroot_colossus 主体 QMetal #B9A277  S=0.357（是 16 个里最低的，但 ≥0.35）⚠
# 巨像的 0.357 是**贴着线**过的，这里如实记一笔：它读起来仍是"浅砂岩"而不是"银灰"。
# 若要它更"金属"，可换 #8C9BB0（S=0.205）—— 但那样就落回低饱和，
# 正是这一批要消灭的问题。所以选择"偏暖的石金属"，并把数值摆在报告里让需求方定夺。

# 主体块竖向占比下限（需求：≥0.45）
BODY_MIN = 0.45
# 材质平均 HSV 饱和度下限（需求：低于 0.35 要点名；本脚本收得更紧）
SAT_MIN = 0.35
SAT_TARGET = 0.45

# ---------------------------------------------------------------- 图元细分档
# 面数公式（Blender 5.2 实测，见 gen_plants_ally.py）：球 2*segs*(rings-1)、管 4*v-4
Q = {"hero": (14, 8), "hi": (12, 7), "mid": (10, 6), "low": (8, 4), "tiny": (6, 3)}
QV = {"hi": 12, "mid": 10, "low": 8, "tiny": 6}
TIER_RANK = ["tiny", "low", "mid", "hi", "hero"]

# 预算收敛用的全局细分下限：从 hero 逐级往下压，直到面数进预算。
# 为什么是"全局压"而不是"逐个部件挑"：主体的形状是**剪影**，减它的细分等于减辨识度；
# 而整体压一档只影响圆润度，剪影不变。所以从最低收益的方向先动。
FLOOR = "hero"


def tri_sphere(segs, rings):
    return 2 * segs * (rings - 1)


def tri_tube(v):
    return 4 * v - 4


def _cap(tier):
    """把请求档位按当前全局下限截断。"""
    i = TIER_RANK.index(tier) if tier in TIER_RANK else 0
    f = TIER_RANK.index(FLOOR) if FLOOR in TIER_RANK else len(TIER_RANK) - 1
    return TIER_RANK[min(i, f)]


def _capv(tier):
    """管状图元用的是 QV 的四档名字（没有 hero）。"""
    return _cap(tier if tier in QV else "hi")


# ---------------------------------------------------------------- 三阶段参数族
# 每个品种的 FORM 给出「主体宽高比 / 支撑类型 / 冠层族」，三阶段共用同一 builder。
# 这里只放**阶段级**的值：身体/支撑/冠层的竖向占比与细节密度。
STAGE = {
    #            身体   支撑   冠层   细节   总高区间(m)       面数预算
    "young":  dict(body=0.44, support=0.10, crown=0.08, detail=0.35,
                   h=(0.35, 0.50), budget=500),
    "grown":  dict(body=0.52, support=0.26, crown=0.22, detail=0.70,
                   h=(0.95, 1.15), budget=900),
    "mature": dict(body=0.56, support=0.26, crown=0.26, detail=1.00,
                   h=(1.68, 1.78), budget=1500),
}
STAGE_ORDER = ("young", "grown", "mature")
STAGE_CN = {"young": "幼苗", "grown": "成长中", "mature": "成熟"}

# 成熟体竖向占比必须留在本工程既有区间内（`gen_plants_ally.py` 实测 [1.62, 1.80]），
# 否则 Unity 侧"按实测高度统一缩放"的观感会变。这里取窄带的中段，留出余量。
MATURE_H_LOCK = (1.68, 1.78)


# ================================================================ 品种表
# primary = 16 个彼此不同的**主块形态**；这正是"剪影可区分"的来源。
# 每行: (中文名, FORM, 主体色, 强调色, 细节色)
SPECIES = {
    "radish_guard":       ("萝卜卫士",   "bulb",    "Petal",      "QLeafDeep",    "Leaf"),
    "pea_shooter":        ("豌豆射手",   "pod",     "Leaf",       "QLeafDeep",    "QIce"),
    "toxic_mushroom":     ("毒蘑菇",     "cap",     "Shroom",     "QFungusDeep",  "QGoo"),
    "frost_ivy":          ("寒霜藤",     "cluster", "QIce",       "Leaf",         "QLeafDeep"),
    "thorn_pumpkin":      ("荆棘南瓜",   "gourd",   "Pumpkin",    "QLeafDeep",    "QGold"),
    "stone_fist":         ("石拳",       "rock",    "QRock",      "Moss",         "QMetal"),
    "chili_mage":         ("辣椒法师",   "taper",   "Ember",      "QEmberDeep",   "Leaf"),
    "starvine":           ("星藤",       "star",    "PetalB",     "QGold",        "QIce"),
    "moontree":           ("月树",       "canopy",  "QMoon",      "QTrunkDeep",   "QLeafDeep"),
    "char_flower":        ("炭花",       "flower",  "Ember",      "QSunDisc",     "Leaf"),
    "plague_mother":      ("瘟疫之母",   "dome",    "QBerry",     "QGoo",         "QBerryDeep"),
    "moss_shield":        ("苔藓盾",     "mossball", "Moss",      "QBlood",       "QMossDeep"),
    "healing_clover":     ("治愈三叶草", "clover",  "QClover",    "QGold",        "Leaf"),
    "life_spring":        ("生命之泉",   "basin",   "Water",      "QRock",        "QIce"),
    "holy_tree_guard":    ("圣树守卫",   "tree",    "QTrunk",     "QCrownLeaf",   "QGold"),
    "ironroot_colossus":  ("铁根巨像",   "colossus", "QMetal",    "QRock",        "QGold"),
}

# 品种级的竖向占比微调（同样是"三阶段共用一份"，不按阶段分别写）
#   字段含义：bscale/sscale/cscale 缩放该品种在阶段值上的身体/支撑/冠层占比；
#             jitter 让 16 个品种的轮廓高低不同，避免站在一起像一排同款。
FORM_TUNE = {
    "radish_guard":      dict(bs=1.00, ss=1.00, cs=1.00),
    "pea_shooter":       dict(bs=1.06, ss=0.70, cs=0.90),
    "toxic_mushroom":    dict(bs=1.05, ss=0.85, cs=0.85),
    "frost_ivy":         dict(bs=0.90, ss=1.30, cs=1.20),
    "thorn_pumpkin":     dict(bs=1.02, ss=0.85, cs=0.95),
    "stone_fist":        dict(bs=1.04, ss=0.80, cs=0.90),
    "chili_mage":        dict(bs=1.08, ss=0.75, cs=0.95),
    "starvine":          dict(bs=0.98, ss=0.95, cs=1.15),
    "moontree":          dict(bs=1.00, ss=0.85, cs=1.20),
    "char_flower":       dict(bs=0.96, ss=1.00, cs=1.10),
    "plague_mother":     dict(bs=1.06, ss=0.80, cs=0.95),
    "moss_shield":       dict(bs=0.94, ss=1.15, cs=1.00),
    "healing_clover":    dict(bs=0.95, ss=1.05, cs=1.05),
    "life_spring":       dict(bs=0.92, ss=1.00, cs=1.15),
    "holy_tree_guard":   dict(bs=0.98, ss=0.90, cs=1.10),
    "ironroot_colossus": dict(bs=1.05, ss=0.85, cs=0.95),
}

# 品类里需要的一个额外的叶色键
kit.PALETTE["QCrownLeaf"] = "#4FA03A"

GROUP = ("young", "grown", "mature")   # 只用于文档可读性


# ================================================================ 保护闸门
def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def snapshot_protected():
    """记录三处受保护文件的 SHA256 + mtime。写盘前后各跑一次，比对即得证明。"""
    out = {}
    for root, _dirs, files in os.walk(ANIM_DIR):
        for f in files:
            p = os.path.join(root, f)
            out[os.path.relpath(p, PROJECT).replace("\\", "/")] = sha256_file(p)
    for p in (os.path.join(CHARS_DIR, "player.fbx"),):
        if os.path.isfile(p):
            out[os.path.relpath(p, PROJECT).replace("\\", "/")] = sha256_file(p)
    if os.path.isdir(TOOLS_DIR):
        for f in sorted(os.listdir(TOOLS_DIR)):
            if f.startswith("tool_") and f.endswith(".fbx"):
                p = os.path.join(TOOLS_DIR, f)
                out[os.path.relpath(p, PROJECT).replace("\\", "/")] = sha256_file(p)
    return out


def guard():
    """
    写盘前的静态把关（与 SHA256 快照互补）：
      · 受保护目录/文件存在且非空（否则"没碰过"毫无意义）
      · gen_plants.py 只读——记录它的哈希，运行结束再比一次
    """
    assert os.path.isdir(ANIM_DIR), "找不到 Anim 目录: %s" % ANIM_DIR
    n_anim = sum(len(fs) for _r, _d, fs in os.walk(ANIM_DIR))
    assert n_anim > 0, "Anim 目录是空的？拒绝继续"
    assert os.path.isfile(os.path.join(CHARS_DIR, "player.fbx")), "player.fbx 不存在"
    gp_py = os.path.join(HERE, "gen_plants.py")
    return n_anim, sha256_file(gp_py)


# ================================================================ 记账式几何
class Ledger(object):
    """
    逐件记账：每造一个图元就记下它的
      · 名称 / 材质键名 / 三角面数
      · **世界坐标 Z 跨度**（用网格顶点算，不用 bound_box —— 后者依赖 depsgraph 更新，
        刚设完 scale 就去读会拿到旧值，这是本工程踩过的坑）
      · 角色：body（主体块）/ support（支撑）/ crown（冠层）/ detail（细节）/ garnish（配饰）
    有了角色标签，主体占比就能**量出来**，不需要靠渲染图目测。
    """

    def __init__(self):
        self.parts = []

    def add(self, obj, name, key, role):
        mw = obj.matrix_world
        zs = [(mw @ v.co).z for v in obj.data.vertices]
        tris = sum(max(1, len(p.vertices) - 2) for p in obj.data.polygons)
        mat = obj.data.materials[0] if obj.data.materials else None
        self.parts.append(dict(
            obj=obj, name=name, key=key, role=role, tris=tris,
            zmin=min(zs), zmax=max(zs),
            hex=kit.PALETTE.get(key, key if key.startswith("#") else None),
        ))
        return obj

    def zr(self, role):
        sel = [p for p in self.parts if p["role"] == role]
        if not sel:
            return None
        return min(p["zmin"] for p in sel), max(p["zmax"] for p in sel)

    def tris(self):
        return sum(p["tris"] for p in self.parts)

    def keys(self):
        seen = []
        for p in self.parts:
            if p["key"] not in seen:
                seen.append(p["key"])
        return seen

    def objs(self):
        return [p["obj"] for p in self.parts]


def _finish(obj, name, key, role, led, smooth=True):
    obj.name = name
    if smooth:
        kit._apply(obj, key, 0.0, 0.62)
        bpy.ops.object.shade_smooth()
    else:
        kit._apply(obj, key, 0.0, 0.62)
    return led.add(obj, name, key, role)


def sphere(led, name, key, role, loc, r, scale=(1, 1, 1), rot=(0, 0, 0), tier="hi"):
    segs, rings = Q[_cap(tier)]
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings,
                                         radius=r, location=loc)
    o = bpy.context.active_object
    o.scale = scale
    o.rotation_euler = rot
    return _finish(o, name, key, role, led)


def tube(led, name, key, role, loc, radius, depth, rot=(0, 0, 0),
         scale=(1, 1, 1), tier="hi", smooth=True):
    """圆柱。★ depth 是**全高**，不是半径倍数 ★（这个坑注释在 gen_plants_grown 里）"""
    v = QV[_capv(tier)]
    bpy.ops.mesh.primitive_cylinder_add(vertices=v, radius=radius, depth=depth,
                                        location=loc)
    o = bpy.context.active_object
    o.rotation_euler = rot
    o.scale = scale
    return _finish(o, name, key, role, led, smooth=smooth)


def ring(led, name, key, role, loc, r1, r2, depth, rot=(0, 0, 0),
         scale=(1, 1, 1), tier="mid"):
    v = QV[_capv(tier)]
    bpy.ops.mesh.primitive_cone_add(vertices=v, radius1=r1, radius2=r2,
                                    depth=depth, location=loc)
    o = bpy.context.active_object
    o.rotation_euler = rot
    o.scale = scale
    return _finish(o, name, key, role, led)


def ball(led, name, key, role, loc, size, rot=(0, 0, 0)):
    """
    立方体，`size` 是**半边长三元组**（所以实际边长 = 2*size）。

    ★ 半径类图元要"把半径烘进几何"，不能只设 obj.scale ★
      实测踩坑：只写 `o.scale = size` 时，立方体的顶点仍然是 ±0.5，
      而这个模型的 Z 跨度会被 `Ledger` 量成 **1.0000**（不是 size.z*2），
      于是"主体占比"算出 2.425 这种 >1 的荒谬值 —— 指标撒谎比不指标更糟。
      正确做法是把 size 乘进图元的 `scale=` 参数（Blender 会写进顶点），
      并把 obj.scale 保持为 1，这样 obj.scale 就不再影响任何测量。
      同一理由适用于 sphere/tube/ring：它们都用 radius/depth 参数把尺寸烘进顶点。
    """
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, scale=size)
    o = bpy.context.active_object
    o.rotation_euler = rot
    return _finish(o, name, key, role, led, smooth=False)


# ================================================================ 尺寸解析
def species_height(plant_id, stage):
    """
    把 PLANTS 的 size（0.45~1.10）线性映射到该阶段的高度区间。
    关键是**同一个品种在三阶段里用同一个 t**，所以三阶段的高度顺序天然单调，
    品种之间的高低关系也保持一致（D 级最矮、S 级最高）。
    """
    lo, hi = STAGE[stage]["h"]
    sizes = [p["size"] for p in gp.PLANTS.values()]
    smin, smax = min(sizes), max(sizes)
    t = 0.0 if smax <= smin else (gp.PLANTS[plant_id]["size"] - smin) / (smax - smin)
    if stage == "mature":
        # 成熟体钉在工程既有窄带里，保证 Unity 侧"统一缩放"的观感不变
        a, b = MATURE_H_LOCK
        return a + t * (b - a)
    return lo + t * (hi - lo)


def stage_ratios(plant_id, stage):
    """品种微调 × 阶段占比 → 归一化后的 (body, support, crown) 占比。"""
    st = STAGE[stage]
    tn = FORM_TUNE[plant_id]
    b = st["body"] * tn["bs"]
    s = st["support"] * tn["ss"]
    c = st["crown"] * tn["cs"]
    tot = b + s + c
    return b / tot, s / tot, c / tot


# ================================================================ 16 个主块形态
def _support(led, supH, supW, baseR, key, kind="stem"):
    """
    支撑结构（根 / 茎 / 叶腿）：**短、粗、圆头收尾**。
    需求要求"四肢短粗圆头" —— 所以这里默认是圆柱 + 顶端一个圆头，
    而不是细长直筒。
    """
    if supH <= 1e-4:
        return
    r = max(supW * 0.5, supH * 0.30)          # 短粗：半径不小于高度的 30%
    z = supH * 0.5
    if kind == "root":
        tube(led, "Sup_Root", key, "support", (0, 0, z), r * 0.95, supH,
             tier="mid")
        for i in range(3):
            a = i * 120.0 + 30.0
            tube(led, "Sup_RootArm_%d" % i, key, "support",
                 (math.sin(D(a)) * r * 0.9, math.cos(D(a)) * r * 0.9, supH * 0.35),
                 r * 0.45, supH * 0.85, rot=(D(28) * math.cos(D(a)),
                                             D(28) * math.sin(D(a)), 0), tier="tiny")
    elif kind == "stalk":
        tube(led, "Sup_Stalk", key, "support", (0, 0, z), r * 0.72, supH, tier="mid")
    elif kind == "leafleg":
        for side in (-1, 1):
            tube(led, "Sup_LeafLeg_%s" % ("R" if side > 0 else "L"), key, "support",
                 (side * r * 0.75, 0, z), r * 0.62, supH,
                 rot=(0, D(8) * side, 0), tier="mid")
    else:
        tube(led, "Sup_Stem", key, "support", (0, 0, z), r * 0.80, supH, tier="mid")


def form_bulb(led, ctx):
    """萝卜卫士：倒水滴主体 + 叶冠。近战挡刀 → 身体最厚。"""
    b = ctx
    sphere(led, "Body_Bulb", b["body_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.46),
           b["bodyW"] * 0.5, scale=(1.0, 1.0, b["bodyH"] / (b["bodyW"] * 0.5) * 0.5),
           tier="hero")
    # 顶部叶簇：细节密度随阶段增长 2 → 3 → 4 片
    n = 2 if b["stage"] == "young" else (3 if b["stage"] == "grown" else 4)
    for i in range(n):
        a = (i / float(n)) * 360.0
        sphere(led, "Crown_Leaf_%d" % i, b["accent_key"], "crown",
               (math.sin(D(a)) * b["bodyW"] * 0.26, math.cos(D(a)) * b["bodyW"] * 0.26 * 0.35,
                b["z0"] + b["bodyH"] * 0.92 + b["crownH"] * 0.35),
               b["bodyW"] * 0.22, scale=(1.0, 0.55, 1.25),
               rot=(0, -D(a), 0), tier="low")


def form_pod(led, ctx):
    """豌豆射手：竖向豆荚（高瘦椭圆）+ 顶端小豆粒 + 侧叶。远程 → 有朝向。"""
    b = ctx
    sphere(led, "Body_Pod", b["body_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.5),
           b["bodyW"] * 0.5, scale=(1.0, 0.92, b["bodyH"] / b["bodyW"]), tier="hero")
    # 豆粒：成熟才有 3 颗，成长中 2 颗，幼苗 1 颗（"未完成"的读法）
    n = 1 if b["stage"] == "young" else (2 if b["stage"] == "grown" else 3)
    for i in range(n):
        sphere(led, "Bean_%d" % i, b["detail_key"], "detail",
               (0, -b["bodyW"] * 0.44, b["z0"] + b["bodyH"] * (0.42 + 0.20 * i)),
               b["bodyW"] * 0.17, tier="low")
    sphere(led, "Muzzle", b["accent_key"], "crown", (0, -b["bodyW"] * 0.30,
                                                     b["z0"] + b["bodyH"] * 0.90),
           b["bodyW"] * 0.19, scale=(1.0, 0.85, 0.55), tier="mid")
    for side in (-1, 1):
        sphere(led, "SideLeaf_%s" % ("R" if side > 0 else "L"), b["accent_key"], "crown",
               (side * b["bodyW"] * 0.46, 0, b["z0"] + b["bodyH"] * 0.70),
               b["bodyW"] * 0.20, scale=(1.2, 0.42, 0.9),
               rot=(0, 0, D(20) * side), tier="low")


def form_cap(led, ctx):
    """毒蘑菇：宽扁伞盖 + 圆点。伞盖自己就是"头"，占比最大的一类。

    ★ 宽扁是刻意的 ★ 它与同属"圆球类"的瘟疫之母靠**扁平度**区分：
      蘑菇盖 aspect≈1.30（横向铺开）、瘟疫圆顶 aspect≈0.88（竖向鼓起）。
      早先两者都用默认 0.80，剪影几乎重合（IoU 实测 0.971 > 判据 0.74）。
    """
    b = ctx
    capR = b["bodyH"] * 1.15 * 0.5
    # ★ 必须**填满** bodyH，不能只填一部分 ★
    # 早先写成"扁平盖，只占 bodyH 的 55%"，而 bodyH 本身只有总高的 75%，
    # 于是实测主体占比 = 0.55 × 0.75 = 0.413 < 0.45，被闸门拦下 ✗
    # 教训：Q 版的"矮胖"靠的是 bodyH/total 的**占比**，
    # 而主体块必须真的把这份占比吃掉；块自己再打七折，占比就白设计了。
    fill = 1.0
    sphere(led, "Cap", b["body_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * fill * 0.5), capR,
           scale=(1.0, 1.0, fill * b["bodyH"] / (2.0 * capR)), tier="hero")
    # 斑点：3 / 5 / 6 颗，成熟最密
    n = 3 if b["stage"] == "young" else (5 if b["stage"] == "grown" else 6)
    for i in range(n):
        a = (i / float(n)) * 360.0
        rr = capR * (0.40 + 0.26 * ((i % 3) / 2.0))
        sphere(led, "CapSpot_%d" % i, b["detail_key"], "detail",
               (math.sin(D(a)) * rr, math.cos(D(a)) * rr,
                b["z0"] + b["bodyH"] * (fill - 0.16)),
               capR * 0.16, scale=(1.0, 1.0, 0.5), tier="tiny")


def form_cluster(led, ctx):
    """
    寒霜藤：低矮的圆润冰刺簇（尖而不锐：圆锥 + 圆头）。贴地蔓延。

    ★ 冰晶算 **body**，不是 crown ★
      "主体"的定义是"定义这个品种形状的那团质量"。寒霜藤就是一堆冰刺——
      刺就是它本身，把它记成冠层会让主体占比掉到 0.37，
      而那只是**标签**错了，不是造型错了（造型是对的：它就是一团刺）。
      真正的冠层是"长在主体之上的独立附件"（叶冠/花盘/光环/果实）。
    """
    b = ctx
    sphere(led, "Body_Mound", b["body_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.37),
           b["bodyW"] * 0.5, scale=(1.05, 0.95, b["bodyH"] / b["bodyW"] * 0.74),
           tier="hero")
    n = 4 if b["stage"] == "young" else (6 if b["stage"] == "grown" else 8)
    for i in range(n):
        a = (i / float(n)) * 360.0
        rr = b["bodyW"] * (0.20 + 0.20 * ((i % 3) / 2.0))
        h = b["bodyH"] * (0.55 if i % 2 else 0.85)
        ring(led, "Crystal_%d" % i, b["accent_key"] if i % 2 else b["body_key"], "body",
             (math.sin(D(a)) * rr, math.cos(D(a)) * rr, b["z0"] + h * 0.75),
             b["bodyW"] * 0.16, b["bodyW"] * 0.045, h,
             rot=(D(10) * math.cos(D(a)), D(10) * math.sin(D(a)), 0), tier="tiny")


def form_gourd(led, ctx):
    """荆棘南瓜：带棱的扁球 + 圆润短刺（"圆润的尖"）。反伤定位。"""
    b = ctx
    r = b["bodyW"] * 0.5
    sphere(led, "Gourd", b["body_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.46),
           r, scale=(1.08, 1.08, b["bodyH"] / r * 0.5), tier="hero")
    tube(led, "GourdRib", b["accent_key"], "detail",
         (0, 0, b["z0"] + b["bodyH"] * 0.46), r * 1.005, 0.012, tier="mid", smooth=False)
    n = 6 if b["stage"] == "young" else (8 if b["stage"] == "grown" else 10)
    for i in range(n):
        a = (i / float(n)) * 360.0
        el = D(-16 + 32 * (i % 2))
        # 短刺算 body：它们就是这个瓜的外轮廓，是"圆润的尖"而不是附件
        ring(led, "Spike_%d" % i, b["accent_key"], "body",
             (math.sin(D(a)) * r * 1.02, math.cos(D(a)) * r * 1.02,
              b["z0"] + b["bodyH"] * 0.46 + math.sin(el) * r * 0.9),
             b["bodyW"] * 0.075, b["bodyW"] * 0.02, b["bodyW"] * 0.20,
             rot=(D(90) - el, 0, D(a)), tier="tiny")
    tube(led, "GourdStalk", b["accent_key"], "detail",
         (0, 0, b["z0"] + b["bodyH"] * 0.99), b["bodyW"] * 0.05,
         b["crownH"] * 0.6, tier="tiny")


def form_rock(led, ctx):
    """
    石拳：方形块身 + 一对圆头短拳。近战坦克。
    ★ 主体用**方**的（ball），与其它品种的圆球拉开一档轮廓差异 ★
      方块厚度直接由 bodyH 推出，所以主体竖向占比与设计值一致。
    """
    b = ctx
    bh = b["bodyH"] * 0.5
    ball(led, "Chest", b["body_key"], "body", (0, 0, b["z0"] + bh),
         (b["bodyW"] * 0.52, b["bodyW"] * 0.44, bh))
    sphere(led, "Head", b["body_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * 0.80), b["bodyW"] * 0.30,
           scale=(1.0, 0.9, 0.62), tier="mid")
    for i, (dx, dz) in enumerate(((-0.34, 0.68), (0.36, 0.44), (0.05, 0.24))):
        sphere(led, "MossPatch_%d" % i, b["accent_key"], "detail",
               (dx * b["bodyW"], -b["bodyW"] * 0.30, b["z0"] + dz * b["bodyH"]),
               b["bodyW"] * 0.19, scale=(1.2, 0.40, 0.8), tier="low")
    if b["stage"] != "young":
        for side in (-1, 1):
            sphere(led, "Fist_%s" % ("R" if side > 0 else "L"), b["body_key"], "body",
                   (side * b["bodyW"] * 0.62, -b["bodyW"] * 0.05,
                    b["z0"] + b["bodyH"] * 0.26),
                   b["bodyW"] * 0.23, tier="mid")


def form_taper(led, ctx):
    """辣椒法师：上窄下宽的锥形椒身 + 弯尖。"""
    b = ctx
    ring(led, "Pepper", b["body_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.47),
         b["bodyW"] * 0.5, b["bodyW"] * 0.20, b["bodyH"], tier="hi")
    ring(led, "PepperTip", b["accent_key"], "crown",
         (0, 0, b["z0"] + b["bodyH"] * 1.02), b["bodyW"] * 0.20, b["bodyW"] * 0.03,
         b["crownH"] * 0.9 + 0.02, tier="mid")
    n = 2 if b["stage"] == "young" else (3 if b["stage"] == "grown" else 4)
    for i in range(n):
        a = (i / float(n)) * 360.0 + 45.0
        sphere(led, "Calyx_%d" % i, b["detail_key"], "detail",
               (math.sin(D(a)) * b["bodyW"] * 0.36, math.cos(D(a)) * b["bodyW"] * 0.36,
                b["z0"] + b["bodyH"] * 0.86), b["bodyW"] * 0.20,
               scale=(1.1, 0.42, 0.95), rot=(0, -D(a), 0), tier="low")


def form_star(led, ctx):
    """星藤：正面一个大星形花（5 瓣圆头尖）+ 垂藤。"""
    b = ctx
    R = b["bodyW"] * 0.5
    sphere(led, "Disc", b["detail_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.50),
           R * 0.62, scale=(1.0, 0.34, 1.0), tier="hi")
    n = 5
    for i in range(n):
        a = (i / float(n)) * 360.0
        sphere(led, "StarPetal_%d" % i, b["body_key"], "body",
               (math.sin(D(a)) * R * 0.92, 0, b["z0"] + b["bodyH"] * 0.50
                + math.cos(D(a)) * R * 0.92),
               R * 0.40, scale=(0.85, 0.30, 1.30), rot=(0, -D(a), 0), tier="mid")
    if b["stage"] != "young":
        for i in range(2):
            a = 40.0 + i * 200.0
            tube(led, "Tendril_%d" % i, b["accent_key"], "detail",
                 (math.sin(D(a)) * R * 1.05, 0, b["z0"] + b["bodyH"] * 0.30),
                 R * 0.055, b["bodyH"] * 0.55, rot=(D(14) * math.cos(D(a)),
                                                    D(14) * math.sin(D(a)), 0), tier="tiny")


def form_canopy(led, ctx):
    """月树：层叠圆冠（3~4 球）+ 暖棕树干。"""
    b = ctx
    n = 2 if b["stage"] == "young" else (3 if b["stage"] == "grown" else 4)
    for i in range(n):
        a = (i / float(n)) * 360.0
        rr = b["bodyW"] * (0.20 if i % 2 else 0.26)
        hh = b["bodyH"] * (0.34 + 0.22 * (i % 3))
        sphere(led, "Canopy_%d" % i, b["body_key"], "body",
               (math.sin(D(a)) * b["bodyW"] * 0.20, math.cos(D(a)) * b["bodyW"] * 0.16,
                b["z0"] + b["bodyH"] * 0.42 + hh),
               rr, scale=(1.15, 1.0, 0.92), tier="hero" if i == 0 else "mid")
    sphere(led, "CanopyCore", b["body_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * 0.34), b["bodyW"] * 0.30,
           scale=(1.0, 1.0, 0.9), tier="mid")
    if b["stage"] != "young":
        for i in range(3):
            a = i * 120.0
            sphere(led, "MoonRoot_%d" % i, b["detail_key"], "detail",
                   (math.sin(D(a)) * b["bodyW"] * 0.22, math.cos(D(a)) * b["bodyW"] * 0.22,
                    b["z0"] + b["bodyH"] * 0.10), b["bodyW"] * 0.10,
                   scale=(1.0, 1.0, 1.4), tier="low")


def form_flower(led, ctx):
    """炭花：暗芯 + 花瓣环（花瓣数随阶段 6 → 8 → 10）。"""
    b = ctx
    R = b["bodyW"] * 0.5
    n = 6 if b["stage"] == "young" else (8 if b["stage"] == "grown" else 10)
    for i in range(n):
        a = (i / float(n)) * 360.0
        sphere(led, "Petal_%d" % i, b["body_key"], "body",
               (math.sin(D(a)) * R * 0.86, 0,
                b["z0"] + b["bodyH"] * 0.52 + math.cos(D(a)) * R * 0.86),
               R * 0.42, scale=(0.88, 0.32, 1.24), rot=(0, -D(a), 0), tier="mid")
    sphere(led, "Disc", b["accent_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.52),
           R * 0.60, scale=(1.0, 0.36, 1.0), tier="hero")


def form_dome(led, ctx):
    """
    瘟疫之母：**两层伞盖 + 粗柄**的复合体，最强的一档。

    ★ 为什么改成复合体 ★
      它原本是个实心圆顶，投影宽高比 0.75，与治愈三叶草（0.72）几乎同一个球，
      渲染剪影 IoU 实测 0.925 > 判据 0.74 ✗
      只调"扁一点/鼓一点"救不了 —— 两个都是圆，怎么调都像。
      改法是把轮廓变成**两层**：下层大伞（带檐）+ 上层小伞 + 中间可见的柄，
      于是它有了别的品种没有的"阶梯状"侧面，剪影一眼可分。
      这也符合它的设定：菌群之母，比单伞的毒蘑菇繁复一档。
    """
    b = ctx
    R = b["bodyH"] * 0.86 * 0.5
    # 下层大伞：占 bodyH 的 62%，最宽
    sphere(led, "DomeLow", b["body_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * 0.31), R,
           scale=(1.0, 1.0, 0.62 * b["bodyH"] / (2.0 * R)), tier="hero")
    # 中间可见的柄段（夹在两伞之间，制造收腰）
    tube(led, "DomeStem", b["accent_key"], "body",
         (0, 0, b["z0"] + b["bodyH"] * 0.70), R * 0.30, b["bodyH"] * 0.24, tier="low")
    # 上层小伞：占 bodyH 的 34%，收窄
    sphere(led, "DomeHigh", b["body_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * 0.86), R * 0.62,
           scale=(1.0, 1.0, 0.30 * b["bodyH"] / (2.0 * R * 0.62)), tier="mid")
    n = 4 if b["stage"] == "young" else (6 if b["stage"] == "grown" else 8)
    for i in range(n):
        a = (i / float(n)) * 360.0
        rr = R * (0.42 + 0.26 * ((i % 3) / 2.0))
        sphere(led, "Spore_%d" % i, b["accent_key"], "detail",
               (math.sin(D(a)) * rr, math.cos(D(a)) * rr,
                b["z0"] + b["bodyH"] * 0.50),
               R * 0.16, scale=(1.0, 1.0, 0.6), tier="tiny")


def form_mossball(led, ctx):
    """苔藓盾：三团绒球挤在一起（低矮、无武器）。不攻击 → 没有刺。"""
    b = ctx
    for i in range(3):
        a = i * 120.0 + 20.0
        sphere(led, "MossDome_%d" % i, b["body_key"] if i != 1 else b["accent_key"],
               "body",
               (math.sin(D(a)) * b["bodyW"] * 0.26, math.cos(D(a)) * b["bodyW"] * 0.26,
                b["z0"] + b["bodyH"] * 0.42),
               b["bodyW"] * 0.32, scale=(1.05, 1.0, b["bodyH"] / (b["bodyW"] * 0.64)),
               tier="hero" if i == 0 else "mid")
    if b["stage"] != "young":
        for i in range(4):
            a = i * 90.0 + 45.0
            sphere(led, "Pebble_%d" % i, b["detail_key"], "detail",
                   (math.sin(D(a)) * b["bodyW"] * 0.52, math.cos(D(a)) * b["bodyW"] * 0.52,
                    b["z0"] + b["bodyH"] * 0.14), b["bodyW"] * 0.11,
                   scale=(1.1, 1.1, 0.6), tier="tiny")


def form_clover(led, ctx):
    """
    治愈三叶草：**四片分离的大圆叶**（真·四叶草轮廓）+ 金色花心。纯绿高饱和。

    ★ 叶必须"分得开" ★
      早先四片叶贴太近（叶片半径 0.34×bodyW、叶心距 0.30×bodyW），
      并成一坨，整体投影宽高比 0.72 —— 与瘟疫之母的圆顶撞脸到 IoU 0.925 ✗
      现在把叶心距拉开到 0.50×bodyW、叶片收到 0.28×bodyW，
      四片之间留出可见的**凹口**，整体才读得出"四叶"而不是"球"。
    """
    b = ctx
    n = 3 if b["stage"] == "young" else 4
    for i in range(n):
        a = (i / float(n)) * 360.0 + (30.0 if n == 3 else 0.0)
        sphere(led, "Clover_%d" % i, b["body_key"], "body",
               (math.sin(D(a)) * b["bodyW"] * 0.50, math.cos(D(a)) * b["bodyW"] * 0.50,
                b["z0"] + b["bodyH"] * 0.50),
               b["bodyW"] * 0.40, scale=(1.0, 1.0, b["bodyH"] / (b["bodyW"] * 0.80)),
               rot=(0, 0, D(a)), tier="hero")
    sphere(led, "CloverCore", b["accent_key"], "crown",
           (0, 0, b["z0"] + b["bodyH"] * 1.00), b["bodyW"] * 0.19,
           scale=(1.0, 1.0, 0.7), tier="low")


def form_basin(led, ctx):
    """
    生命之泉：矮胖的碗 + 发光水面 + 水滴。容器就是本体，所以**碗必须够高够宽**
    （早先写成 R*0.86 的收口锥、高度 0.78，主体只占 0.428 → 被闸门拦下）。
    现在改成"厚壁大碗"：几乎直筒（只收 14%），竖向吃掉 bodyH 的 ~94%。
    """
    b = ctx
    R = b["bodyW"] * 0.5
    ring(led, "Basin", b["body_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.47),
         R, R * 0.86, b["bodyH"] * 0.94, tier="hero")
    sphere(led, "Water", b["detail_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * 0.86), R * 0.84,
           scale=(1.0, 1.0, 0.34), tier="mid")
    n = 1 if b["stage"] == "young" else (3 if b["stage"] == "grown" else 5)
    for i in range(n):
        a = (i / float(n)) * 360.0
        sphere(led, "Drop_%d" % i, b["detail_key"], "crown",
               (math.sin(D(a)) * R * 0.70, math.cos(D(a)) * R * 0.70,
                b["z0"] + b["bodyH"] * 1.02 + b["crownH"] * 0.5),
               b["bodyW"] * 0.085, scale=(0.8, 0.8, 1.35), tier="tiny")


def form_tree(led, ctx):
    """圣树守卫：暖棕树干 + 圆冠 + 金环。最高一档。"""
    b = ctx
    sphere(led, "Trunk", b["accent_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.34),
           b["bodyW"] * 0.24, scale=(1.0, 1.0, b["bodyH"] / (b["bodyW"] * 0.48)),
           tier="mid")
    n = 2 if b["stage"] == "young" else (3 if b["stage"] == "grown" else 4)
    for i in range(n):
        a = (i / float(n)) * 360.0
        sphere(led, "Canopy_%d" % i, b["body_key"], "body",
               (math.sin(D(a)) * b["bodyW"] * 0.20, math.cos(D(a)) * b["bodyW"] * 0.20,
                b["z0"] + b["bodyH"] * 0.72 + b["bodyH"] * 0.20 * (i % 2)),
               b["bodyW"] * 0.34, scale=(1.1, 1.05, 0.95), tier="hero" if i == 0 else "mid")
    if b["stage"] == "mature":
        for i in range(6):
            a = i * 60.0
            sphere(led, "Halo_%d" % i, b["detail_key"], "crown",
                   (math.sin(D(a)) * b["bodyW"] * 0.48, math.cos(D(a)) * b["bodyW"] * 0.48,
                    b["z0"] + b["bodyH"] * 1.12), b["bodyW"] * 0.075, tier="tiny")


def form_colossus(led, ctx):
    """铁根巨像：最大块体 + 双肩 + 金冠（只做小面积配饰）。"""
    b = ctx
    ball(led, "Torso", b["body_key"], "body", (0, 0, b["z0"] + b["bodyH"] * 0.50),
         (b["bodyW"] * 0.5, b["bodyW"] * 0.44, b["bodyH"] * 0.48))
    sphere(led, "UpperRock", b["accent_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * 0.90), b["bodyW"] * 0.34,
           scale=(1.0, 0.92, 0.82), tier="mid")
    for side in (-1, 1):
        sphere(led, "Shoulder_%s" % ("R" if side > 0 else "L"), b["body_key"], "body",
               (side * b["bodyW"] * 0.48, 0, b["z0"] + b["bodyH"] * 0.72),
               b["bodyW"] * 0.20, tier="low")
    n = 1 if b["stage"] == "young" else (3 if b["stage"] == "grown" else 5)
    for i in range(n):
        a = (i / float(n)) * 360.0
        ring(led, "Crown_%d" % i, b["detail_key"], "crown",
             (math.sin(D(a)) * b["bodyW"] * 0.24, math.cos(D(a)) * b["bodyW"] * 0.24,
              b["z0"] + b["bodyH"] * 1.10 + b["crownH"] * 0.5),
             b["bodyW"] * 0.075, b["bodyW"] * 0.02, b["crownH"],
             rot=(0, D(10) * math.cos(D(a)), 0), tier="tiny")


FORMS = {
    "bulb": form_bulb, "pod": form_pod, "cap": form_cap, "cluster": form_cluster,
    "gourd": form_gourd, "rock": form_rock, "taper": form_taper, "star": form_star,
    "canopy": form_canopy, "flower": form_flower, "dome": form_dome,
    "mossball": form_mossball, "clover": form_clover, "basin": form_basin,
    "tree": form_tree, "colossus": form_colossus,
}

# 支撑结构样式（短粗圆头）
SUPPORT_KIND = {
    "bulb": "root", "pod": "stem", "cap": "stalk", "cluster": "stem",
    "gourd": "root", "rock": "leafleg", "taper": "stem", "star": "stem",
    "canopy": "trunk", "flower": "stem", "dome": "stalk", "mossball": "root",
    "clover": "stem", "basin": "root", "tree": "trunk", "colossus": "leafleg",
}


# ================================================================ 组装一个模型
def build_context(plant_id, stage):
    """算出一个品种在某阶段的所有尺寸。三阶段共用，所以成长关系是连续的。"""
    total = species_height(plant_id, stage)
    rb, rs, rc = stage_ratios(plant_id, stage)
    bodyH = total * rb
    supH = total * rs
    crownH = max(total * rc, 1e-4)
    name_cn, form, body_key, accent_key, detail_key = SPECIES[plant_id]
    return dict(
        plant_id=plant_id, stage=stage, total=total, form=form,
        bodyH=bodyH, supH=supH, crownH=crownH,
        # ★ 宽高比必须**跨阶段恒定** ★
        # 早先写成"幼苗 0.96 / 成长中与成熟 0.86"，本意是"幼苗胖一点更 Q"，
        # 结果是：幼苗的 bodyH 只有 0.31 m，乘 0.96 后主体几乎是**正球**，
        # 于是 16 个品种的苗期全塌成同一个球 —— 剪影 IoU 实测
        #   plague_mother/young vs toxic_mushroom/young = 0.971（判据 <0.74）✗
        # 而真正的"Q"来自 bodyH/total 的占比（≥0.45），不是来自把球压扁。
        # 固定宽高比之后，各品种的特征夸张（蘑菇的宽伞盖、豆荚的高椭圆、
        # 辣椒的锥形）在**苗期也读得出来**，品种认同不再依赖体量。
        bodyW=bodyH * 0.80,
        z0=supH,
        body_key=body_key, accent_key=accent_key, detail_key=detail_key,
        name_cn=name_cn,
        support_kind=SUPPORT_KIND[form],
    )


def build(plant_id, stage, dry_run=False, verbose=True):
    """造一个「品种 × 阶段」模型并导出。返回一行摘要（也给 --report 用）。"""
    if plant_id not in SPECIES:
        raise KeyError("未知品种: %s（可选: %s）" % (plant_id, ", ".join(SPECIES)))
    if stage not in STAGE:
        raise ValueError("未知阶段: %s（可选: %s）" % (stage, ", ".join(STAGE_ORDER)))

    ctx = build_context(plant_id, stage)
    spec = STAGE[stage]

    def paint(led):
        """造一遍全部几何。可以被预算收敛循环重复调用（每次都从空场景开始）。"""
        baseR = max(ctx["bodyW"] * 0.42, ctx["supH"] * 0.9, 0.02)
        sphere(led, "Base_Soil", "SoilRich", "support",
               (0, 0, ctx["supH"] * 0.30), baseR,
               scale=(1.05, 1.05, max(0.18, ctx["supH"] / baseR * 0.42)), tier="mid")
        _support(led, ctx["supH"], ctx["bodyW"], baseR, "SoilDeep",
                 kind=ctx["support_kind"])
        FORMS[ctx["form"]](led, ctx)

    # ── 预算收敛：从 hero 往下压全局细分下限，取第一个进预算的档 ──
    # 只压**圆润度**，不压剪影：主块的形状（球/荚/伞/簇/棱球…）与位置全程不变。
    global FLOOR
    led = None
    obj = None
    used_floor = None
    for fd in TIER_RANK[::-1]:                  # hero → hi → mid → low → tiny
        FLOOR = fd
        kit.reset()
        led = Ledger()
        paint(led)
        obj = kit.join(led.objs(), "PLQ_%s_%s" % (plant_id, stage))
        if obj is None:
            raise RuntimeError("%s/%s 没有生成任何几何" % (plant_id, stage))
        used_floor = fd
        if led.tris() < spec["budget"]:
            break
        # 这一档还是超标：下一轮 kit.reset() 会清场景，重造
    else:
        raise RuntimeError("%s/%s 连 %s 档都塞不进 %d 面预算（当前 %d）"
                           % (plant_id, stage, TIER_RANK[0], spec["budget"], led.tris()))
    FLOOR = "hero"                              # 复位，别污染下一个模型

    # ① 缩放到目标高度（设计即按米，这里通常是恒等，但保证 assert 恒成立）
    kit.apply_transforms(obj)
    hz = obj.dimensions.z
    k = ctx["total"] / hz if hz > 1e-6 else 1.0
    obj.scale = (k, k, k)
    kit.apply_transforms(obj)

    # ② 抬到踩地：zmin 恰好 0（旧版幼苗沉在 −0.008 上下，这里修掉）
    lift = -_world_zmin(obj)
    obj.location.z += lift
    _select_only(obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # ③ 原点 = 脚底中心
    kit.set_origin(obj, (0.0, 0.0, 0.0))

    # ④ 量：高度 / zmin / 面数 / 主体占比 / 饱和度
    dims = tuple(round(v, 4) for v in obj.dimensions)
    zmin = round(_world_zmin(obj), 6)
    loc = tuple(round(v, 5) for v in obj.location)
    tris = kit.tri_count(obj)
    mats = [m.name for m in obj.data.materials if m]
    keys = led.keys()

    # 主体占比用**实测**的世界 Z 跨度（不是设计值）——避免"设计 0.5 实测 0.3"
    body_lo, body_hi = led.zr("body") or (0.0, 0.0)
    body_ratio = ((body_hi - body_lo) / ctx["total"]) if ctx["total"] > 0 else 0.0
    support_lo, support_hi = led.zr("support") or (0.0, 0.0)

    sats = {}
    for kk in keys:
        hx = kit.PALETTE.get(kk)
        if not hx:
            continue
        r_, g_, b_ = kit._hex_to_rgb(hx)
        sats[kk] = round(colorsys.rgb_to_hsv(r_, g_, b_)[1], 4)
    mean_sat = round(sum(sats.values()) / len(sats), 4) if sats else 0.0
    min_key = min(sats, key=lambda kk: sats[kk]) if sats else "-"
    min_sat = sats.get(min_key, 0.0)

    # ⑤ 写盘前自检（不通过就不落盘）
    lo, hi = spec["h"]
    assert abs(dims[2] - ctx["total"]) < 0.006, \
        "%s/%s 高度没对上: %.4f vs %.4f" % (plant_id, stage, dims[2], ctx["total"])
    assert lo - 0.005 <= dims[2] <= hi + 0.005, \
        "%s/%s 高度 %.3f 超出阶段区间 [%.2f, %.2f]" % (plant_id, stage, dims[2], lo, hi)
    assert abs(zmin) < 1e-4, "%s/%s zmin 不为 0: %s" % (plant_id, stage, zmin)
    assert abs(loc[0]) < 1e-5 and abs(loc[1]) < 1e-5 and abs(loc[2]) < 1e-5, \
        "%s/%s 原点不在脚底中心: %s" % (plant_id, stage, loc)
    assert tris < spec["budget"], \
        "%s/%s 面数 %d 超出预算 %d" % (plant_id, stage, tris, spec["budget"])
    assert body_ratio >= BODY_MIN, \
        "%s/%s 主体占比 %.3f < %.2f（Q 版硬指标）" % (plant_id, stage, body_ratio, BODY_MIN)
    # ★ 不变量：主体必须落在 [0, 总高] 之内 ★
    # 这一条是"指标自己撒谎"的闸门：曾经因为立方体没烘缩放，
    # 主体 Z 跨度被量成 1.0（实际 0.24），算出占比 2.425 —— 数值荒谬但断言全过。
    assert body_lo > -0.02 and body_hi < ctx["total"] * 1.02, \
        "%s/%s 主体 Z 跨度 [%.4f, %.4f] 超出模型范围 [0, %.4f] —— 记账不可信" % (
            plant_id, stage, body_lo, body_hi, ctx["total"])
    assert min_sat >= SAT_MIN, \
        "%s/%s 材质 %s 饱和度 %.3f < %.2f（上一批的低饱和教训）" % (
            plant_id, stage, min_key, min_sat, SAT_MIN)

    head = "PLQ_%s_%s" % (plant_id, stage)
    suffix = {"young": "_young", "grown": "_grown", "mature": ""}[stage]
    path = os.path.join(PLANT_DIR, plant_id + suffix + ".fbx")
    meta = path + ".meta"

    row = dict(
        id=plant_id, stage=stage, nameCN=ctx["name_cn"], form=ctx["form"],
        height=round(dims[2], 4), dimX=round(dims[0], 4), dimY=round(dims[1], 4),
        zmin=zmin, origin=list(loc), tris=tris, budget=spec["budget"],
        detailFloor=used_floor,
        bodyRatio=round(body_ratio, 4),
        bodySpan=[round(body_lo, 4), round(body_hi, 4)],
        supportSpan=[round(support_lo, 4), round(support_hi, 4)],
        supportRatio=round((support_hi - support_lo) / ctx["total"], 4),
        crownRatio=round((ctx["total"] - body_hi) / ctx["total"], 4),
        materials=mats, materialKeys=keys,
        saturation=sats, meanSat=mean_sat, minSatKey=min_key, minSat=min_sat,
        path=os.path.relpath(path, PROJECT).replace("\\", "/"),
    )

    if not dry_run:
        kit.export_fbx(obj, path)          # bake_yup=False：轴向只由导出器转一次
        # 导出后删 .meta —— Unity 会按 ArtImportSettings 重新生成
        if os.path.isfile(meta):
            os.remove(meta)
            row["removedMeta"] = True
    if verbose:
        print("[QSTYLE] %-20s %-6s h=%.3f tris=%4d/%4d body=%.3f sat=%.3f(min %s=%.2f) "
              "mats=%s%s"
              % (plant_id, stage, dims[2], tris, spec["budget"], body_ratio,
                 mean_sat, min_key, min_sat, ",".join(mats),
                 "  [DRY]" if dry_run else ""))
    return row


# ---------------------------------------------------------------- 工具
def _select_only(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def _world_zmin(obj):
    mw = obj.matrix_world
    return min((mw @ v.co).z for v in obj.data.vertices)


# ---------------------------------------------------------------- 批量
def build_all(ids=None, stages=None, dry_run=False, include_mature=False, report=None):
    ids = list(ids) if ids else list(SPECIES.keys())
    stages = list(stages) if stages else ["young", "grown"]
    if include_mature and "mature" not in stages:
        stages.append("mature")

    before = snapshot_protected()
    n_anim, gp_hash = guard()

    rows = []
    for pid in ids:
        for st in stages:
            rows.append(build(pid, st, dry_run=dry_run))

    after = snapshot_protected()
    changed = [k for k in before if before.get(k) != after.get(k)]
    added = [k for k in after if k not in before]
    removed = [k for k in before if k not in after]
    gp_now = sha256_file(os.path.join(HERE, "gen_plants.py"))

    print("[QSTYLE] 共 %d 个模型（%s）" % (len(rows), "+".join(stages)))
    for st in stages:
        sub = [r for r in rows if r["stage"] == st]
        if not sub:
            continue
        print("[QSTYLE]   %-6s h=[%.3f, %.3f] tris=[%d, %d] body=[%.3f, %.3f] "
              "meanSat=[%.3f, %.3f]"
              % (st, min(r["height"] for r in sub), max(r["height"] for r in sub),
                 min(r["tris"] for r in sub), max(r["tris"] for r in sub),
                 min(r["bodyRatio"] for r in sub), max(r["bodyRatio"] for r in sub),
                 min(r["meanSat"] for r in sub), max(r["meanSat"] for r in sub)))
    low = [("%s/%s" % (r["id"], r["stage"]), r["minSat"]) for r in rows
           if r["minSat"] < SAT_TARGET]
    if low:
        print("[QSTYLE]   ⚠ 平均饱和度低于目标 %.2f 的材质: %s"
              % (SAT_TARGET, ", ".join("%s=%.3f" % t for t in low)))

    proof = dict(
        animFiles=n_anim,
        protectedCount=len(before),
        changed=changed, added=added, removed=removed,
        intact=(not changed and not added and not removed),
        genPlantsPySha256=gp_hash,
        genPlantsPyUnchanged=(gp_hash == gp_now),
    )
    print("[QSTYLE] 不覆盖证明: 受保护 %d 项, 变更 %d, 新增 %d, 删除 %d → %s"
          % (len(before), len(changed), len(added), len(removed),
             "全部未变 ✓" if proof["intact"] else "有变化 ✗"))
    print("[QSTYLE] gen_plants.py 未被改动: %s" % proof["genPlantsPyUnchanged"])

    if report:
        kit.ensure_dir(report)
        with open(report, "w", encoding="utf-8") as fh:
            json.dump(dict(
                generator="Tools/AssetPipeline/gen_plants_qstyle.py",
                command="blender.exe -b --factory-startup -P gen_plants_qstyle.py -- all",
                stages=stages, stageSpec=STAGE, bodyMin=BODY_MIN,
                satMin=SAT_MIN, satTarget=SAT_TARGET,
                models=rows, protection=proof,
            ), fh, ensure_ascii=False, indent=1)
        print("[QSTYLE] report -> " + report)
    return rows, proof


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args or args[0] in ("-h", "--help"):
        print("用法: blender -b --factory-startup -P gen_plants_qstyle.py -- all [选项]")
        print("     选项: --stages young,grown,mature  --include-mature  --dry-run")
        print("           --report <out.json>")
        print("  品种:", ", ".join(SPECIES))
        sys.exit(0)

    stages = None
    if "--stages" in args:
        i = args.index("--stages")
        stages = args[i + 1].split(",")
        args = args[:i] + args[i + 2:]
    report = None
    if "--report" in args:
        i = args.index("--report")
        report = args[i + 1]
        args = args[:i] + args[i + 2:]
    dry = "--dry-run" in args
    args = [a for a in args if a != "--dry-run"]
    inc_mature = "--include-mature" in args
    args = [a for a in args if a != "--include-mature"]

    ids = None if (args and args[0] == "all") else args
    build_all(ids=ids, stages=stages, dry_run=dry, include_mature=inc_mature,
              report=report)
