# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · v202 「8 株新植物」三阶段模型生成器
================================================================================

需求原话：「设计 8 个新高品质植物（带机制），包括模型和图标」。

## 为什么是**新脚本**，而不是改 `Tools/AssetPipeline/gen_plants_qstyle.py`

那个脚本是 v175 定下来的 16 品种生成器，它有三处**硬保护**：
  · `SPECIES` 里只登记 16 个 id（`build()` 对未知 id 直接 `KeyError`）
  · `guard()` + `snapshot_protected()` 记录 `Art/Anim/**`、`player.fbx`、`Tools/tool_*.fbx`
    的 SHA256，并断言 `gen_plants.py` 一字未改
  · 头注释写明"绝不触碰"清单
改它 = 动一件已经验收过的资产管线的**唯一真相** ✗

因此本脚本**只 import 它**（读、不写）：

    sys.path.insert(0, "Tools/AssetPipeline")
    import gen_plants_qstyle as qs      # 同一套 Q 版原语 / 同一张调色板 / 同一组断言
    import seedfall_kit as kit

然后做三件事，**全部是追加**：
  ① 往 `gp.PLANTS` 里登记 8 个品种的 `size`（`species_height` 读它做归一）
  ② 往 `qs.SPECIES / qs.FORM_TUNE / qs.FORMS / qs.SUPPORT_KIND` 里登记 8 个新形态
  ③ 逐个调 `qs.build(id, stage)` —— 导出的还是那条既有出口：
     `kit.export_fbx(..., bake_yup=False)` → `Assets/Resources/Art/Plants/<id>[_young|_grown].fbx`

⇒ 产物与既有 22 株**同一套风格、同一套命名、同一套轴向/单位约定**：
   高度在 **Z**、原点在**脚底中心**（`zmin = 0`）、Z-up、导出器只转一次轴；
   Unity 侧 `ArtImportSettings` 对 `Art/Plants/` 用 `globalScale = 0.01` 把 FBX 单位压回去，
   `ModelView.Spawn` 按**实测网格高度**统一缩放 ⇒ 不需要任何代码改动 ✓

## 8 株（形态 / 主色 / 强调 / 细节 / 支撑）

| id | 中文 | 形态 | 主体 | 强调 | 细节 | 支撑 | 剪影一句话 |
|---|---|---|---|---|---|---|---|
| dusk_berry   | 暮莓   | berry  | QBerry      | QBerryDeep | QGold     | stem   | 三颗浆果簇，下大上两小 |
| tide_gourd   | 潮瓜   | melon  | Pumpkin     | QLeafDeep  | QIce      | root   | 横置扁瓜 + 两道竖棱 |
| hollow_vine  | 空藤   | hoop   | QClover     | QLeafDeep  | QSunDisc  | stem   | 中空藤环（真的镂空）+ 顶花 |
| dune_cactus  | 沙掌   | paddle | QMossDeep   | Moss       | QGold     | root   | 方肩掌片 + 两侧小掌 + 刺列 |
| harvest_bell | 丰收铃 | bell   | QGold       | QSunDisc   | QEmberDeep| stem   | 倒锥铃身 + 铃口 + 铃舌 |
| star_ladder  | 星梯   | ladder | QIce        | QMoon      | QGold     | stem   | 双轨四横档 + 星尖 |
| elder_oak    | 古木   | oaktree| QTrunk      | QCrownLeaf | QSunDisc  | trunk  | 粗干 + 三团冠 + 外露根 |
| quick_reed   | 疾苇   | reed   | QLeafDeep   | Water      | QGold     | stem   | 三根高秆 + 穗头 + 叶刃 |

## 用法（Blender 5.2 实测）

    B="D:\\Steam\\steamapps\\common\\Blender\\blender.exe"
    & $B -b --factory-startup -P Tools/icon_gen/gen_plant_meshes_8.py -- all
    & $B -b --factory-startup -P Tools/icon_gen/gen_plant_meshes_8.py -- all --dry-run
    & $B -b --factory-startup -P Tools/icon_gen/gen_plant_meshes_8.py -- dusk_berry elder_oak
    & $B -b --factory-startup -P Tools/icon_gen/gen_plant_meshes_8.py -- all --stages young,grown
    & $B -b --factory-startup -P Tools/icon_gen/gen_plant_meshes_8.py -- all --report Docs/v202_plants.json

默认三阶段全做（young / grown / mature）。**成熟体也做**：这 8 株在 `Art/Anim/` 下
没有 idle/walk 动画（那套只属于 16 个老品种），所以不存在"重做几何要把动画连锅端"的问题；
反而缺 `<id>.fbx` 时 `ModelView.Spawn` 会返回 null ⇒ 田里只画占位方块 ✗

## 落盘前的闸门（全部沿用 `qs.build` 里那套，一条没放宽）

`高度对得上` / `高度落在阶段区间` / `zmin = 0` / `原点在脚底中心` / `面数进预算`
（幼苗 <500、成长中 <900、成熟 <1500）/ `主体占比 ≥ 0.45` / `主体 Z 跨度不越界` /
`每个材质的饱和度 ≥ 0.35`。任何一条不过 ⇒ 该模型**根本不落盘** ✓

另加一道**本脚本自己的**前置闸门：8 个品种用到的 12 个调色板键必须真的在
`kit.PALETTE` 里（否则会静默落成 `#CCCCCC` 灰模 —— 那是本工程吃过的"不吭声的坏"）。
"""

import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
KIT_DIR = os.path.join(PROJECT, "Tools", "AssetPipeline")
sys.path.insert(0, KIT_DIR)

import bpy                      # noqa: E402  (只能跑在 Blender 里)
import seedfall_kit as kit      # noqa: E402
import gen_plants as gp         # noqa: E402
import gen_plants_qstyle as qs  # noqa: E402

D = qs.D
sphere, tube, ring, ball = qs.sphere, qs.tube, qs.ring, qs.ball

# ════════════════════════════════════════════════════════════════════════════
#  8 个品种登记（三阶段共用一份；阶段差异由 `qs.STAGE` 的四级参数族给出）
# ════════════════════════════════════════════════════════════════════════════
#            (中文名,   FORM,     主体,       强调,        细节)
NEW_SPECIES = {
    "dusk_berry":     ("暮莓",   "berry",   "QBerry",    "QBerryDeep", "QGold"),
    "tide_gourd":     ("潮瓜",   "melon",   "Pumpkin",   "QLeafDeep",  "QIce"),
    "hollow_vine":    ("空藤",   "hoop",    "QClover",   "QLeafDeep",  "QSunDisc"),
    "dune_cactus":    ("沙掌",   "paddle",  "QMossDeep", "Moss",       "QGold"),
    "harvest_bell":   ("丰收铃", "bell",    "QGold",     "QSunDisc",   "QEmberDeep"),
    "star_ladder":    ("星梯",   "ladder",  "QIce",      "QMoon",      "QGold"),
    "elder_oak":      ("古木",   "oaktree", "QTrunk",    "QCrownLeaf", "QSunDisc"),
    "quick_reed":     ("疾苇",   "reed",    "QLeafDeep", "Water",      "QGold"),
}

# 品种级竖向占比微调（与现有 16 株同一组字段：bs=身体 ss=支撑 cs=冠层）
NEW_TUNE = {
    "dusk_berry":   dict(bs=1.02, ss=0.85, cs=1.05),
    "tide_gourd":   dict(bs=1.05, ss=0.80, cs=0.95),
    "hollow_vine":  dict(bs=0.98, ss=0.95, cs=1.10),
    "dune_cactus":  dict(bs=1.04, ss=0.80, cs=0.95),
    "harvest_bell": dict(bs=1.02, ss=0.85, cs=1.10),
    "star_ladder":  dict(bs=1.00, ss=0.95, cs=1.12),
    "elder_oak":    dict(bs=0.96, ss=0.95, cs=1.25),
    "quick_reed":   dict(bs=1.06, ss=1.05, cs=0.90),
}

# 支撑结构样式（沿用 qs 的六种：root / stem / stalk / leafleg / trunk）
NEW_SUPPORT = {
    "berry": "stem", "melon": "root", "hoop": "stem", "paddle": "root",
    "bell": "stem", "ladder": "stem", "oaktree": "trunk", "reed": "stem",
}

# ⚠️ size 必须留在**既有 16 株的 [0.45, 1.10] 区间内**：
#    `qs.species_height` 用 `gp.PLANTS` 的全局 min/max 做归一（`t = (size-min)/(max-min)`），
#    一旦引入区间外的 size，**既有 16 株的 t 会整体偏一点点** ⇒ 它们的 `species_height`
#    也跟着变 ✗ 本轮明确"不动别的植物" ⇒ 8 个 size 全部取在区间内部 ✓
#    （min 仍是 frost_ivy 0.45、max 仍是 ironroot_colossus 1.10，逐位不变）
NEW_SIZE = {
    "dusk_berry": 0.72, "tide_gourd": 0.74, "hollow_vine": 0.70, "dune_cactus": 0.78,
    "harvest_bell": 0.88, "star_ladder": 0.90, "elder_oak": 1.02, "quick_reed": 0.92,
}

NEW_KIND = {
    "dusk_berry": "berry", "tide_gourd": "gourd", "hollow_vine": "vine",
    "dune_cactus": "cactus", "harvest_bell": "bell", "star_ladder": "ladder",
    "elder_oak": "tree", "quick_reed": "reed",
}


# ════════════════════════════════════════════════════════════════════════════
#  8 个主块形态
#  ★ 硬指标：每个形态都要让"主体块"的 Z 跨度 ≥ 0.9 × bodyH ★
#    因为 `qs.build` 用**实测**的主块 Z 跨度算 `bodyRatio` 并断言 ≥ 0.45，
#    而 bodyH 只占 total 的一半左右 ⇒ 主体不满 bodyH 就会红。
# ════════════════════════════════════════════════════════════════════════════
def form_berry(led, ctx):
    """暮莓：一颗大果居中 + 两颗小果分列上方 + 果柄与叶。"""
    b = ctx
    r = b["bodyW"] * 0.52
    sphere(led, "Body_Berry_C", b["body_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * 0.46), r,
           scale=(1.0, 1.0, b["bodyH"] / (2.0 * r) * 1.02), tier="hero")
    n = 2 if b["stage"] == "young" else 2
    for i in range(n):
        side = -1 if i == 0 else 1
        sphere(led, "Body_Berry_%s" % ("L" if side < 0 else "R"), b["body_key"], "body",
               (side * b["bodyW"] * 0.34, 0, b["z0"] + b["bodyH"] * 0.80),
               b["bodyW"] * 0.24, tier="hi")
    tube(led, "Peduncle", b["accent_key"], "support",
         (0, 0, b["z0"] + b["bodyH"] * 0.94), b["bodyW"] * 0.045,
         b["bodyH"] * 0.26, tier="tiny")
    sphere(led, "Leaf_Blade", b["accent_key"], "crown",
           (b["bodyW"] * 0.20, 0, b["z0"] + b["bodyH"] + b["crownH"] * 0.55),
           b["bodyW"] * 0.20, scale=(1.55, 0.42, 0.34), rot=(0, 0, D(28)), tier="low")
    for i, side in enumerate((-1, 1)):
        sphere(led, "Pip_%d" % i, b["detail_key"], "detail",
               (side * b["bodyW"] * 0.34, -b["bodyW"] * 0.20,
                b["z0"] + b["bodyH"] * 0.80), b["bodyW"] * 0.052, tier="tiny")


def form_melon(led, ctx):
    """潮瓜：横置扁瓜（宽 > 高）+ 一道腰箍 + 顶部卷须。"""
    b = ctx
    r = b["bodyW"] * 0.52
    sphere(led, "Body_Melon", b["body_key"], "body",
           (0, 0, b["z0"] + b["bodyH"] * 0.48), r,
           scale=(1.34, 1.18, b["bodyH"] / (2.0 * r)), tier="hero")
    tube(led, "Belt", b["accent_key"], "detail",
         (0, 0, b["z0"] + b["bodyH"] * 0.48), 0.012, b["bodyW"] * 0.92,
         rot=(0, D(90), 0), tier="tiny")
    k = 3 if b["stage"] == "young" else 5
    for i in range(k):
        a = (i / float(k)) * 180.0 - 90.0
        tube(led, "Ridge_%d" % i, b["accent_key"], "detail",
             (0, 0, b["z0"] + b["bodyH"] * 0.48), 0.010, b["bodyW"] * 0.30,
             rot=(0, 0, D(a)), scale=(1.0, 1.0, 1.0), tier="tiny")
    tube(led, "Tendril", b["accent_key"], "support",
         (b["bodyW"] * 0.10, 0, b["z0"] + b["bodyH"] * 0.94), b["bodyW"] * 0.040,
         b["bodyH"] * 0.20, rot=(D(22), 0, 0), tier="tiny")
    sphere(led, "Blossom", b["detail_key"], "crown",
           (b["bodyW"] * 0.16, 0, b["z0"] + b["bodyH"] + b["crownH"] * 0.45),
           b["bodyW"] * 0.15, scale=(1.0, 1.0, 0.7), tier="low")


def form_hoop(led, ctx):
    """空藤：8 颗球围成的**中空**藤环（中心什么都不放 = "空"）+ 顶端一朵花。"""
    b = ctx
    R = b["bodyH"] * 0.36
    rr = b["bodyH"] * 0.145
    n = 8
    for i in range(n):
        a = math.pi * 2 * i / n + D(12)
        sphere(led, "Hoop_%d" % i, b["body_key"], "body",
               (math.sin(a) * R, math.cos(a) * R * 0.30,
                b["z0"] + b["bodyH"] * 0.48 + math.cos(a) * R),
               rr, tier="mid")
    for i in range(2):                                    # 断口（强调色的一小段）
        a = math.pi * 2 * (i + 2) / n + D(12)
        sphere(led, "Break_%d" % i, b["accent_key"], "detail",
               (math.sin(a) * R * 1.02, math.cos(a) * R * 0.30,
                b["z0"] + b["bodyH"] * 0.48 + math.cos(a) * R * 1.02),
               rr * 0.52, tier="low")
    k = 3 if b["stage"] == "young" else 5
    for i in range(k):                                    # 花：一圈花瓣
        a = (i / float(k)) * math.pi * 2
        sphere(led, "Petal_%d" % i, b["accent_key"], "crown",
               (math.sin(a) * b["bodyW"] * 0.20, math.cos(a) * b["bodyW"] * 0.10,
                b["z0"] + b["bodyH"] + b["crownH"] * 0.45),
               b["bodyW"] * 0.13, scale=(1.0, 0.8, 0.7), tier="low")
    sphere(led, "Petal_Core", b["detail_key"], "crown",
           (0, 0, b["z0"] + b["bodyH"] + b["crownH"] * 0.50),
           b["bodyW"] * 0.11, tier="low")


def form_paddle(led, ctx):
    """沙掌：**两节**方肩掌片叠起来（下节宽、上节窄）+ 两侧小掌 + 刺列 + 顶花。

    ★ 为什么是两节掌片，而不是一块 ★
      主块竖向占比是被闸门钉死的：`body_ratio >= 0.45`，而且用**实测** Z 跨度除以总高。
      而 `ball()` 的 `size` 实为**全边长**不是半边长 —— `primitive_cube_add(size=1.0,
      scale=size)` 的顶点落在 ±size/2，所以 `bodyH*0.48` 那块只贡献 0.48×bodyH。
      本品种 `stage_ratios` 归一化后的 body 占比是 幼苗 0.746 / 成长中 0.565 / 成熟 0.561，
      于是单块掌片的实测占比 = 0.48 × 0.746 = **0.358** < 0.45 ⇒ 幼苗期直接判红
      （这就是上一轮只落盘了 `mature`、`_young`/`_grown` 缺失的原因）。

      叠成两节：下节 0.01~0.47、上节 0.47~0.89（× bodyH）⇒ 实测跨度 0.88×bodyH：
        幼苗 0.656｜成长中 0.497｜成熟 0.494  —— 三档全部过线 ✓
      而且"掌上再生一掌"本来就是仙人掌该有的剪影，比单块更像沙掌，不是为了过闸门凑数 ✓
    """
    b = ctx
    ball(led, "Body_Paddle_Lo", b["body_key"], "body",
         (0, 0, b["z0"] + b["bodyH"] * 0.24),
         (b["bodyW"] * 0.62, b["bodyW"] * 0.46, b["bodyH"] * 0.46))
    ball(led, "Body_Paddle_Hi", b["body_key"], "body",
         (0, 0, b["z0"] + b["bodyH"] * 0.68),
         (b["bodyW"] * 0.44, b["bodyW"] * 0.34, b["bodyH"] * 0.42))
    for side in (-1, 1):                                  # 侧掌长在下节上
        ball(led, "Arm_%s" % ("R" if side > 0 else "L"), b["body_key"], "body",
             (side * b["bodyW"] * 0.40, 0, b["z0"] + b["bodyH"] * 0.28),
             (b["bodyW"] * 0.18, b["bodyW"] * 0.14, b["bodyH"] * 0.16))
    rows = 2 if b["stage"] == "young" else 4
    for i in range(rows):                                 # 刺列：下节与上节各一半
        z = b["z0"] + b["bodyH"] * (0.10 + i * 0.20)
        half = b["bodyW"] * (0.31 if z < b["z0"] + b["bodyH"] * 0.47 else 0.22)
        for side in (-1, 1):
            tube(led, "Spine_%d_%s" % (i, "R" if side > 0 else "L"),
                 b["detail_key"], "detail",
                 (side * half, 0, z), 0.008, b["bodyW"] * 0.20,
                 rot=(0, D(90), 0), tier="tiny")
    sphere(led, "Bloom", b["accent_key"], "crown",
           (0, 0, b["z0"] + b["bodyH"] + b["crownH"] * 0.40),
           b["bodyW"] * 0.16, scale=(1.0, 1.0, 0.8), tier="low")
    sphere(led, "Bloom_Core", b["detail_key"], "crown",
           (0, 0, b["z0"] + b["bodyH"] + b["crownH"] * 0.46),
           b["bodyW"] * 0.07, tier="tiny")


def form_bell(led, ctx):
    """丰收铃：倒锥铃身（上窄下宽）+ 铃口 + 铃舌 + 顶环。"""
    b = ctx
    ring(led, "Body_Bell", b["body_key"], "body",
         (0, 0, b["z0"] + b["bodyH"] * 0.50), b["bodyW"] * 0.52, b["bodyW"] * 0.26,
         b["bodyH"] * 0.96, tier="hi")
    tube(led, "Rim", b["accent_key"], "body",
         (0, 0, b["z0"] + b["bodyH"] * 0.03), b["bodyW"] * 0.54, b["bodyH"] * 0.075,
         tier="mid")
    ring(led, "Loop", b["accent_key"], "crown",
         (0, 0, b["z0"] + b["bodyH"] + b["crownH"] * 0.40),
         b["bodyW"] * 0.13, b["bodyW"] * 0.06, b["crownH"] * 0.55, tier="low")
    sphere(led, "Clapper", b["detail_key"], "detail",
           (0, 0, b["z0"] + b["bodyH"] * 0.10), b["bodyW"] * 0.13, tier="low")
    for i, z in enumerate((0.30, 0.58)):                  # 铃身横箍（读得出"钟"）
        tube(led, "Band_%d" % i, b["accent_key"], "detail",
             (0, 0, b["z0"] + b["bodyH"] * z), b["bodyW"] * (0.44 - i * 0.05),
             b["bodyH"] * 0.045, tier="tiny")


def form_ladder(led, ctx):
    """星梯：双轨 + 三横档（真的像梯子）+ 星尖。"""
    b = ctx
    for side in (-1, 1):
        tube(led, "Rail_%s" % ("R" if side > 0 else "L"), b["body_key"], "body",
             (side * b["bodyW"] * 0.36, 0, b["z0"] + b["bodyH"] * 0.50),
             b["bodyW"] * 0.095, b["bodyH"] * 0.98, tier="mid")
    rungs = 2 if b["stage"] == "young" else 3
    for i in range(rungs):
        z = b["z0"] + b["bodyH"] * (0.18 + i * (0.62 / max(1, rungs - 1)))
        tube(led, "Rung_%d" % i, b["accent_key"], "body",
             (0, 0, z), b["bodyW"] * 0.075, b["bodyW"] * 0.74,
             rot=(0, D(90), 0), tier="mid")
    for i in range(3):                                    # 星尖（细节色）
        a = D(-90 + i * 120)
        sphere(led, "Star_%d" % i, b["detail_key"], "crown",
               (math.sin(a) * b["bodyW"] * 0.30, math.cos(a) * b["bodyW"] * 0.16,
                b["z0"] + b["bodyH"] + b["crownH"] * 0.42),
               b["bodyW"] * 0.10, scale=(1.0, 0.8, 1.2), tier="low")


def form_oaktree(led, ctx):
    """古木：粗干（上细下粗）+ 三团冠 + 外露树根。"""
    b = ctx
    ring(led, "Body_Trunk", b["body_key"], "body",
         (0, 0, b["z0"] + b["bodyH"] * 0.50), b["bodyW"] * 0.30, b["bodyW"] * 0.20,
         b["bodyH"] * 0.98, tier="hi")
    for i, (dx, dz, r) in enumerate((
            (0.00, 0.86, 0.42), (-0.34, 0.72, 0.30), (0.34, 0.70, 0.28))):
        sphere(led, "Canopy_%d" % i, b["accent_key"], "crown",
               (b["bodyW"] * dx, 0,
                b["z0"] + b["bodyH"] + b["crownH"] * dz),
               b["bodyW"] * r, scale=(1.0, 0.88, 0.80), tier="mid")
    for i in range(3):
        a = D(200 + i * 55)
        tube(led, "Root_%d" % i, b["body_key"], "support",
             (math.sin(a) * b["bodyW"] * 0.24, math.cos(a) * b["bodyW"] * 0.16,
              b["z0"] - b["bodyH"] * 0.02), b["bodyW"] * 0.055,
             b["bodyH"] * 0.22, rot=(D(64) * math.cos(a), D(64) * math.sin(a), 0),
             tier="tiny")
    for i in range(2):                                    # 年轮（细节色）
        tube(led, "Ring_%d" % i, b["detail_key"], "detail",
             (0, 0, b["z0"] + b["bodyH"] * (0.34 + i * 0.22)), b["bodyW"] * 0.19,
             b["bodyH"] * 0.045, tier="tiny")


def form_reed(led, ctx):
    """疾苇：三根高秆（高低错落）+ 穗头 + 两片叶刃。"""
    b = ctx
    heights = (0.98, 0.72, 0.86)
    for i, hz in enumerate(heights):
        side = (-1, 0, 1)[i]
        tube(led, "Body_Reed_%d" % i, b["body_key"], "body",
             (side * b["bodyW"] * 0.26, 0, b["z0"] + b["bodyH"] * hz * 0.50),
             b["bodyW"] * 0.088, b["bodyH"] * hz, tier="mid")
    for i, hz in enumerate(heights):                      # 穗
        side = (-1, 0, 1)[i]
        sphere(led, "Ear_%d" % i, b["accent_key"], "body",
               (side * b["bodyW"] * 0.26, 0, b["z0"] + b["bodyH"] * (hz + 0.04)),
               b["bodyW"] * 0.075, scale=(0.8, 0.8, 2.0), tier="low")
    for i, side in enumerate((-1, 1)):                    # 叶刃
        sphere(led, "Blade_%d" % i, b["detail_key"], "crown",
               (side * b["bodyW"] * 0.40, 0, b["z0"] + b["bodyH"] * 0.55),
               b["bodyW"] * 0.16, scale=(1.9, 0.30, 0.55),
               rot=(0, D(52) * side, 0), tier="low")


NEW_FORMS = {
    "berry": form_berry, "melon": form_melon, "hoop": form_hoop,
    "paddle": form_paddle, "bell": form_bell, "ladder": form_ladder,
    "oaktree": form_oaktree, "reed": form_reed,
}


# ════════════════════════════════════════════════════════════════════════════
#  登记 + 闸门 + 批量
# ════════════════════════════════════════════════════════════════════════════
def check_palette():
    """8 个品种用到的调色板键必须真的存在 —— 否则会静默落成灰模（#CCCCCC）。"""
    keys = set()
    for (_cn, _form, body, accent, detail) in NEW_SPECIES.values():
        keys |= {body, accent, detail}
    keys |= {"SoilRich", "SoilDeep"}          # `qs.build` 的底座与支撑固定用这两个
    missing = sorted(k for k in keys if k not in kit.PALETTE)
    if missing:
        raise SystemExit("[V202] 调色板缺键：%s ⇒ 拒绝生成（否则会落成灰模）" % missing)
    print("[V202] 调色板 12 键齐全：%s" % ", ".join(sorted(keys)))


def register():
    for pid, spec in NEW_SPECIES.items():
        qs.SPECIES[pid] = spec
        qs.FORM_TUNE[pid] = NEW_TUNE[pid]
        assert spec[1] in NEW_FORMS, "形态 %s 没实现" % spec[1]
        qs.FORMS[spec[1]] = NEW_FORMS[spec[1]]
        qs.SUPPORT_KIND[spec[1]] = NEW_SUPPORT[spec[1]]
        gp.PLANTS[pid] = dict(kind=NEW_KIND[pid], size=NEW_SIZE[pid],
                              body=spec[2], accent=spec[3], face=False)


def baseline_sizes():
    """记录既有 16 株的 size 区间 —— 用来证明"没有把它们的归一化基准改掉"。"""
    old = {k: v["size"] for k, v in gp.PLANTS.items() if k not in NEW_SPECIES}
    return min(old.values()), max(old.values()), old


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        print("可选品种：%s" % ", ".join(NEW_SPECIES))
        return 0

    stages = ["young", "grown", "mature"]
    if "--stages" in argv:
        i = argv.index("--stages")
        stages = argv[i + 1].split(",")
        argv = argv[:i] + argv[i + 2:]
    report = None
    if "--report" in argv:
        i = argv.index("--report")
        report = argv[i + 1]
        argv = argv[:i] + argv[i + 2:]
    dry = "--dry-run" in argv
    argv = [a for a in argv if a != "--dry-run"]
    ids = list(NEW_SPECIES) if (not argv or argv[0] == "all") else argv
    for pid in ids:
        if pid not in NEW_SPECIES:
            raise SystemExit("[V202] 未知品种 %s（可选：%s）" % (pid, ", ".join(NEW_SPECIES)))

    check_palette()
    before = qs.snapshot_protected()
    n_anim, gp_hash = qs.guard()
    smin, smax, old_sizes = baseline_sizes()
    register()

    print("[V202] 既有 16 株 size 区间 [%.2f, %.2f] 未被改动：%s"
          % (smin, smax,
             all(abs(gp.PLANTS[k]["size"] - v) < 1e-9 for k, v in old_sizes.items())))

    rows = []
    for pid in ids:
        for st in stages:
            rows.append(qs.build(pid, st, dry_run=dry))

    after = qs.snapshot_protected()
    changed = [k for k in before if before.get(k) != after.get(k)]
    added = [k for k in after if k not in before]
    removed = [k for k in before if k not in after]
    gp_now = qs.sha256_file(os.path.join(KIT_DIR, "gen_plants.py"))

    print("[V202] 共 %d 个模型（%s）" % (len(rows), "+".join(stages)))
    for st in stages:
        sub = [r for r in rows if r["stage"] == st]
        if not sub:
            continue
        print("[V202]   %-6s h=[%.3f, %.3f] tris=[%d, %d] body=[%.3f, %.3f] "
              "meanSat=[%.3f, %.3f]"
              % (st, min(r["height"] for r in sub), max(r["height"] for r in sub),
                 min(r["tris"] for r in sub), max(r["tris"] for r in sub),
                 min(r["bodyRatio"] for r in sub), max(r["bodyRatio"] for r in sub),
                 min(r["meanSat"] for r in sub), max(r["meanSat"] for r in sub)))
    print("[V202] 不覆盖证明：受保护 %d 项，变更 %d，新增 %d，删除 %d → %s"
          % (len(before), len(changed), len(added), len(removed),
             "全部未变 ✓" if not (changed or added or removed) else "有变化 ✗"))
    print("[V202] gen_plants.py 未被改动：%s" % (gp_hash == gp_now))

    if report:
        path = os.path.join(PROJECT, report)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(dict(
                generator="Tools/icon_gen/gen_plant_meshes_8.py",
                command="blender.exe -b --factory-startup -P "
                        "Tools/icon_gen/gen_plant_meshes_8.py -- all",
                stages=stages, species={k: list(v) for k, v in NEW_SPECIES.items()},
                sizes=NEW_SIZE, models=rows,
                sizeRangeUntouched=[smin, smax],
                protected=dict(count=len(before), changed=changed,
                               added=added, removed=removed),
            ), fh, ensure_ascii=False, indent=1)
        print("[V202] report -> %s" % report)
    return 0


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    sys.exit(main(args))
