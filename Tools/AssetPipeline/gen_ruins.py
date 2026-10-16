# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 废墟砖墙 + 农村装饰模块生成器
================================================
本脚本**只新增模块**，不触碰已有的 18 个环境模块（gen_environment.py）与任何 C# 脚本。

产出（Assets/Resources/Art/Environment/<id>.fbx）：
  A. 边界废墟墙 3 个变体（砖红、墙顶高低不平、带塌陷缺口）
       wall_ruin / wall_ruin_b / wall_ruin_c
  B. 农村装饰要素 10 个
       haystack(草垛) well(水井) scarecrow(稻草人) wheelbarrow(独轮车)
       clay_jars(陶罐堆) crate_stack(木箱堆) chicken_coop(鸡舍)
       birdhouse(鸟屋) garden_gate(园门) laundry_line(晾衣绳)

尺寸纪律（与玩法对齐）：
  · 墙体单段 2.00m，沿 **局部 X** 铺开 —— 和 fence 段一致，
    因此 WorldView.FenceRun 那套「摆在段中点 + yaw」可以直接复用
  · 墙厚 0.42m（现有边界方块 t=0.6，本模型更薄，不会更占玩法区）
  · 墙顶高度落在 0.8~1.6m，塌陷缺口处降到 0.30~0.50m
  · 装饰按真实米制：草垛 1.2m / 水井 1.3m / 稻草人 1.8m / 鸟屋 2.0m

★ 两个已踩过的坑（照做即可）：
  ① 导出必须走 kit.export_fbx(obj, path)，**不要自己烘 Y-up** —— 轴向只在一处处理
  ② 导出后要删掉同名 .fbx.meta，否则 Unity 不重新导入（见脚本尾部提示）

★ 原点（pivot）约定 —— 本脚本所有模块的原点都在「**底面中心**」：
     z = 0（脚底）/ x,y = 足迹中心（墙体保持作者坐标 x=0，保证段与段能对接）
  已有的 18 个模块原点是 join() 继承自 parts[0] 的（偏的），所以这里的做法是**更严格**的，
  摆放代码把 transform.position 直接给到落地点即可，不需要额外补偿。

用法：
  & "...\\blender.exe" -b --factory-startup -P gen_ruins.py -- all
  & "...\\blender.exe" -b --factory-startup -P gen_ruins.py -- wall_ruin haystack
  & "...\\blender.exe" -b --factory-startup -P gen_ruins.py -- preview      # 只出预览图
"""

import os
import sys
import math
import json

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit   # noqa: E402

ENV_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\Environment"
CAPTURE_DIR = r"D:\Unity\project\Seedfall守园者\Docs\captures"
MANIFEST = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ruins_manifest.json")
D = math.radians

# ---------------------------------------------------------------- 调色板
# 纯新增，向后兼容：只在**本脚本运行时**更新 kit.PALETTE，不改 seedfall_kit.py 本体，
# 避免和别人对同一文件的编辑打架。Unity 侧按材质名取 _BaseColor（ToonLook.SourceColor），
# 所以这里新增的键名不需要改任何 C#。
kit.PALETTE.update({
    # —— 与 gen_environment.py 同值的基础环境色（本脚本要独立可跑，故自带一份）——
    "Turf":      "#6E9A54",
    "TurfDark":  "#4F7A3C",
    "SoilRich":  "#5E4029",
    "Plank":     "#B08A5A",
    "PlankDark": "#8A6A42",
    "Roof":      "#B85C4A",
    "Wall":      "#E8DCC0",
    "StoneLt":   "#A8A49A",
    "StoneDk":   "#7C7870",
    "Canopy":    "#5F9E4E",
    "CanopyDk":  "#43793A",
    "Pond":      "#5AA0CC",
    # —— 本模块新增：砖红 / 砂浆 / 稻草 / 陶土 / 绳 ——
    "Brick":     "#A8443A",   # 砖红主色
    "BrickDark": "#8A352D",   # 砖缝阴影 / 下层受潮砖
    "BrickLt":   "#C4604E",   # 少量新茬砖，破掉大面积同色
    "Mortar":    "#CDBE9E",   # 残存的白灰墙皮
    "Straw":     "#D9B45C",
    "StrawDark": "#B8933F",
    "StrawLt":   "#EFD48C",
    "Clay":      "#B5714A",
    "ClayDark":  "#8E5638",
    "Rope":      "#C2A26A",
})

# ================================================================ 参数
# 改这里就能整体重建。所有数值都是**米**。
WALL_LEN = 2.00          # 单段墙长（= 游戏边界段距）
WALL_TH = 0.42           # 墙厚
COURSE = 0.22            # 砖层高（全墙共用同一套水平灰缝网格 → 砖层才连得起来）
FOOT_H = 0.14            # 石砌墙脚高
BRICK_LEN = 0.40         # 目标砖长（决定竖缝密度；实际按段长均分）
BRICK_SHRINK = 0.012     # 每块砖四周内缩，露出背后的深色灰缝核心 → 远看就是砖缝
FOOT_W = WALL_TH + 0.08  # 墙脚比墙身宽一点

DECOR_GAP = 0.50         # 预览排布间距

# 墙体变体：piers = (x0, x1, 名义顶高, 顶层砖高变化)，stub = 塌陷缺口处的残根
# 顶高都取在「灰缝网格 + 0.10~0.14」上，避免出现 2cm 的薄片砖。
# plaster 里的砖改用 Mortar 色（= 残存白灰墙皮）——比贴一块平板干净，而且不花三角面。
WALL_VARIANTS = {
    "wall_ruin": dict(
        label="砖墙废墟·A",
        piers=[(-1.00, -0.02, 1.56, (0.04, -0.16)),
               (0.30, 1.00, 1.02, (0.08, -0.06))],
        stub=(-0.02, 0.30, 0.50),
        holes=[(0, 3, 1), (1, 1, 0)],
        plaster=[(0, 4, 1), (0, 2, 0), (1, 2, 1)],
        rubble=3,
    ),
    "wall_ruin_b": dict(
        label="砖墙废墟·B",
        piers=[(-1.00, -0.30, 1.12, (0.06, -0.10)),
               (0.10, 1.00, 1.56, (0.04, -0.16))],
        stub=(-0.30, 0.10, 0.36),
        holes=[(1, 4, 0), (0, 2, 0), (1, 1, 1)],
        plaster=[(1, 5, 1), (1, 2, 0), (0, 0, 1)],
        rubble=2,
    ),
    "wall_ruin_c": dict(
        label="砖墙废墟·C",
        piers=[(-1.00, 0.50, 0.92, (0.06, -0.12)),
               (0.74, 1.00, 1.54, (0.04, -0.10))],
        stub=(0.50, 0.74, 0.30),
        holes=[(0, 2, 1), (0, 1, 2), (1, 2, 0), (0, 0, 0)],
        plaster=[(0, 3, 2), (0, 1, 1)],
        rubble=2,
    ),
}

# 砖色循环：同色占多数，少量深浅砖破一破 → 低模平涂下靠"色块节奏"读出砖砌
BRICK_CYCLE = ("Brick", "Brick", "BrickDark", "Brick", "BrickLt", "Brick")


# ================================================================ 小工具
def _lean(tilt_deg, az_deg):
    """让一根竖着的细杆朝方位角 az 的**外侧**倾斜 tilt 度。"""
    a = D(az_deg)
    return (D(-tilt_deg * math.cos(a)), D(-tilt_deg * math.sin(a)), 0.0)


def _rotz(dx, dy, deg):
    """把局部偏移 (dx,dy) 绕 Z 转 deg 度（放在旋转过的箱体上用）。"""
    a = D(deg)
    return (dx * math.cos(a) - dy * math.sin(a), dx * math.sin(a) + dy * math.cos(a))


def _rows(z0, z1):
    """按全墙共用的 COURSE 网格，切出 [z0,z1) 内的各层 (底, 顶)。
    顶层薄于 0.09 时并入下一层，避免出现薄片砖。"""
    out = []
    k = 0
    while True:
        zb = z0 + k * COURSE
        if zb >= z1 - 1e-6:
            break
        zt = min(zb + COURSE, z1)
        out.append([zb, zt])
        k += 1
    if len(out) >= 2 and (out[-1][1] - out[-1][0]) < 0.09:
        out[-2][1] = out[-1][1]
        out.pop()
    if not out:
        out = [[z0, z1]]
    return [(a, b) for a, b in out]


def _brick_cuts(x0, x1, row, pi):
    """把一段墙长切成若干块砖，并让**层与层的竖缝错开**（错缝砌法）。"""
    n = max(1, int(round((x1 - x0) / BRICK_LEN)))
    step = (x1 - x0) / n
    # 起始砖长在 0.5~1.0 个 step 之间按层号取值 → 竖缝逐层错开
    first = step * (0.55 + 0.45 * (((row * 2 + pi) % 3) / 2.0))
    cuts = [x0] + [x0 + first + i * step for i in range(n - 1)] + [x1]
    return [(cuts[i], cuts[i + 1]) for i in range(n)]


# ================================================================ A. 废墟砖墙
def build_wall_ruin(spec):
    """
    一段 2m 废墟砖墙。

    构成（低模，不逐块建模）：
      ① 石砌墙脚 2 块（错缝）
      ② 深色灰缝核心：每个墙垛一整块内芯，比砖面薄 3cm —— 砖块四周内缩后露出的就是它
      ③ 墙身：每个墙垛按 COURSE 逐层砌，层内均分成砖，层间竖缝错开
      ④ 顶层砖高低不一（±0.06~0.18m）→ 墙顶参差不齐
      ⑤ 缺砖（洞）+ 掉在墙脚下的整砖
      ⑥ 缺口正下方的碎砖堆 + 少量残存白灰墙皮
    """
    parts = []
    piers = spec["piers"]
    sx0, sx1, stop = spec["stub"]
    holes = set(spec.get("holes", ()))
    plaster = set(spec.get("plaster", ()))
    hole_marks = []

    # ① 石砌墙脚（通长一块 + 压顶）
    parts.append(kit.box("Foot", (0, 0, FOOT_H * 0.5), (WALL_LEN, FOOT_W, FOOT_H), "StoneDk"))
    parts.append(kit.box("Foot_Cap", (0, 0, FOOT_H + 0.015), (WALL_LEN - 0.02, FOOT_W - 0.05, 0.03),
                         "StoneLt"))

    # ②③④ 墙身
    for pi, (x0, x1, top, var) in enumerate(piers):
        rows = _rows(FOOT_H, top)
        n_br = max(1, int(round((x1 - x0) / BRICK_LEN)))
        if n_br >= 2:      # 一层里只有一块砖时没有竖缝，背后就不需要灰缝核心
            parts.append(kit.box(f"Core_{pi}", ((x0 + x1) * 0.5, 0, (FOOT_H + top) * 0.5),
                                 (x1 - x0, WALL_TH - 0.06, top - FOOT_H), "BrickDark"))
        for row, (zb, zt) in enumerate(rows):
            last = (row == len(rows) - 1)
            for k, (bx0, bx1) in enumerate(_brick_cuts(x0, x1, row, pi)):
                if (pi, row, k) in holes:
                    hole_marks.append((bx0 + bx1) * 0.5)
                    continue
                zt_k = zt + (var[k % len(var)] if (last and var) else 0.0)
                th = WALL_TH + 0.02 * (((row + k) % 3) - 1)      # 轻微出挑 → 侧面有起伏
                # ⑦ 残存白灰墙皮：直接把砖**染成 Mortar 色**，不额外建几何
                col = "Mortar" if (pi, row, k) in plaster \
                    else BRICK_CYCLE[(row * 2 + k + pi) % len(BRICK_CYCLE)]
                parts.append(kit.box(f"Brick_{pi}_{row}_{k}",
                                     ((bx0 + bx1) * 0.5, 0, (zb + zt_k) * 0.5),
                                     (bx1 - bx0 - BRICK_SHRINK, th, max(0.06, zt_k - zb) - BRICK_SHRINK),
                                     col))

    # 缺口残根（本身就矮，且一层只有一块砖 → 不做灰缝核心，砖缝露出来更像塌方）
    for row, (zb, zt) in enumerate(_rows(FOOT_H, stop)):
        parts.append(kit.box(f"Stub_{row}", ((sx0 + sx1) * 0.5, 0, (zb + zt) * 0.5),
                             (sx1 - sx0 - BRICK_SHRINK, WALL_TH, zt - zb - BRICK_SHRINK),
                             "BrickDark" if row == 0 else "Brick"))

    # ⑥ 塌落的碎砖堆（缺口正下方）+ ⑤ 从洞里掉下来的整砖
    cx = (sx0 + sx1) * 0.5
    # 碎砖/落砖都**紧贴墙脚**：别让它们横向伸太远，否则摆在边界上会啃进玩法区
    for i in range(spec.get("rubble", 3)):
        a = 24.0 + i * 118.0
        parts.append(kit.box(f"Rubble_{i}",
                             (cx + math.cos(D(a)) * (0.12 + 0.04 * i), math.sin(D(a)) * 0.15,
                              0.05 + 0.02 * (i % 2)),
                             (0.18 + 0.05 * (i % 2), 0.14, 0.09 + 0.02 * (i % 2)),
                             ("BrickDark", "Brick", "StoneDk")[i % 3],
                             rot=(D(5 * (i % 3) - 5), D(7 * ((i + 1) % 3) - 7), D(a))))
    for i, hx in enumerate(hole_marks[:2]):
        # 夹在段内：整段必须严格落在 ±1.0m（2m 段距），横出来的一截会顶到隔壁那一段
        fx = max(-WALL_LEN * 0.5 + 0.30, min(WALL_LEN * 0.5 - 0.30, hx))
        parts.append(kit.box(f"Fallen_{i}", (fx, -WALL_TH * 0.5 - 0.12 - 0.04 * i, 0.055),
                             (0.44, 0.28, 0.11), ("Brick", "BrickLt")[i % 2],
                             rot=(0, 0, D(-14 + 22 * i))))
    return parts


# ================================================================ B. 农村装饰
def build_haystack():
    """草垛：圆锥顶 + 捆绳 + 支棱出来的麦秆，中间穿一根木杆。1.24m 高。"""
    parts = []
    r = 0.62
    parts.append(kit.cyl("Body", (0, 0, 0.35), r, 0.70, "Straw", verts=12))
    parts.append(kit.cone("Roof", (0, 0, 0.91), r * 1.02, 0.24, 0.42, "StrawDark", verts=12))
    parts.append(kit.cone("Tip", (0, 0, 1.18), 0.24, 0.04, 0.12, "StrawLt", verts=10))
    parts.append(kit.cyl("Band_Lo", (0, 0, 0.28), r + 0.015, 0.05, "Rope", verts=12, smooth=False))
    parts.append(kit.cyl("Band_Hi", (0, 0, 0.62), r * 0.99, 0.045, "Rope", verts=12, smooth=False))
    # 木杆必须**正好从地面起**：center=0.65/depth=1.30 → 0.00~1.30。
    # 之前写成 center=0.62/depth=1.45，杆子从 -0.105 起 —— 模型会浮在地面上方 10cm。
    parts.append(kit.cyl("Pole", (0, 0, 0.65), 0.035, 1.30, "PlankDark", verts=6))
    for i in range(6):
        a = i * 61.0
        parts.append(kit.cone(f"Tuft_{i}", (math.sin(D(a)) * r * 0.88, math.cos(D(a)) * r * 0.88,
                                            0.34 + 0.09 * (i % 3)),
                              0.035, 0.004, 0.24, "StrawLt", verts=4, rot=_lean(56.0, a)))
    return parts


def build_well():
    """水井：石圈 + 井口水面 + 立柱摇把 + 小坡顶 + 吊桶。1.34m 高。"""
    parts = []
    parts.append(kit.cyl("Ring", (0, 0, 0.29), 0.55, 0.58, "StoneDk", verts=12))
    parts.append(kit.cyl("Rim", (0, 0, 0.61), 0.60, 0.10, "StoneLt", verts=12, smooth=False))
    parts.append(kit.cyl("Water", (0, 0, 0.54), 0.46, 0.04, "Pond", verts=12, smooth=False))
    for s in (-1, 1):
        parts.append(kit.box(f"Post_{s:+d}", (s * 0.46, 0, 0.88), (0.09, 0.09, 0.46), "PlankDark"))
    parts.append(kit.cyl("Axle", (0, 0, 1.06), 0.045, 1.02, "PlankDark", verts=8, rot=(0, D(90), 0)))
    for s in (-1, 1):
        parts.append(kit.box(f"Roof_{s:+d}", (0, s * 0.20, 1.18), (1.16, 0.44, 0.045), "Roof",
                             rot=(D(s * 34), 0, 0)))
    parts.append(kit.box("Ridge", (0, 0, 1.30), (1.20, 0.07, 0.06), "Roof"))
    parts.append(kit.cyl("Crank", (0.55, 0, 1.06), 0.03, 0.20, "Metal", verts=8, rot=(0, D(90), 0)))
    parts.append(kit.box("Handle", (0.66, 0, 0.98), (0.05, 0.05, 0.20), "Plank"))
    parts.append(kit.box("Rope", (0, 0, 0.92), (0.035, 0.035, 0.26), "Rope"))
    parts.append(kit.cyl("Bucket", (0, 0, 0.72), 0.13, 0.20, "Plank", verts=10))
    parts.append(kit.cyl("BucketRim", (0, 0, 0.825), 0.14, 0.03, "PlankDark", verts=10, smooth=False))
    parts.append(kit.box("Moss_A", (-0.44, -0.30, 0.13), (0.30, 0.22, 0.10), "CanopyDk", rot=(0, 0, D(18))))
    parts.append(kit.box("Moss_B", (0.40, 0.32, 0.10), (0.26, 0.20, 0.08), "CanopyDk", rot=(0, 0, D(-12))))
    parts.append(kit.sphere("Stone", (0.66, 0.34, 0.09), 0.14, "StoneLt",
                            scale=(1.1, 1.0, 0.6), segs=8, rings=4))
    return parts


def build_scarecrow():
    """稻草人：木桩十字 + 稻草衣 + 布袋头 + 草帽。1.82m 高，立在田里。"""
    parts = []
    parts.append(kit.cyl("Post", (0, 0, 0.85), 0.055, 1.70, "PlankDark", verts=8))
    parts.append(kit.box("Arm", (0, 0, 1.30), (1.24, 0.07, 0.07), "PlankDark"))
    parts.append(kit.cone("Body", (0, 0, 1.02), 0.30, 0.16, 0.56, "Straw", verts=10))
    parts.append(kit.cone("Skirt", (0, 0, 0.74), 0.34, 0.22, 0.32, "StrawDark", verts=10))
    parts.append(kit.sphere("Head", (0, 0, 1.50), 0.19, "Straw",
                            scale=(1.0, 0.94, 1.06), segs=10, rings=6))
    parts.append(kit.box("Eye_L", (-0.075, -0.155, 1.54), (0.055, 0.05, 0.055), "Ink"))
    parts.append(kit.box("Eye_R", (0.075, -0.155, 1.54), (0.055, 0.05, 0.055), "Ink"))
    parts.append(kit.box("Mouth", (0, -0.16, 1.43), (0.14, 0.05, 0.028), "Ink", rot=(0, 0, D(6))))
    parts.append(kit.cyl("Brim", (0, 0, 1.66), 0.33, 0.035, "StrawLt", verts=10, smooth=False))
    parts.append(kit.cone("Hat", (0, 0, 1.74), 0.19, 0.02, 0.15, "StrawLt", verts=10))
    for s in (-1, 1):
        for k in range(3):
            parts.append(kit.cone(f"Tuft_{s}_{k}", (s * 0.60, 0, 1.28 + (k - 1) * 0.05),
                                  0.045, 0.004, 0.22, "StrawLt", verts=4,
                                  rot=(D(4 * (k - 1)), D(-s * 72), 0)))
    for i in range(4):
        a = i * 90.0 + 30.0
        parts.append(kit.cone(f"Hem_{i}", (math.sin(D(a)) * 0.20, math.cos(D(a)) * 0.20, 0.56),
                              0.045, 0.004, 0.24, "StrawDark", verts=4, rot=_lean(10.0, a)))
    return parts


def build_wheelbarrow():
    """独轮车：木斗 + 单轮 + 两条手柄 + 支脚。1.52×0.68m。"""
    parts = []
    parts.append(kit.box("Floor", (0, 0, 0.44), (0.86, 0.50, 0.05), "Plank"))
    for s in (-1, 1):
        parts.append(kit.box(f"Side_{s:+d}", (0, s * 0.25, 0.56), (0.86, 0.05, 0.24), "Plank",
                             rot=(D(-s * 16), 0, 0)))
    parts.append(kit.box("Back", (-0.44, 0, 0.56), (0.05, 0.52, 0.26), "PlankDark"))
    parts.append(kit.box("Front", (0.44, 0, 0.55), (0.05, 0.50, 0.24), "PlankDark", rot=(0, D(-14), 0)))
    parts.append(kit.cyl("Wheel", (0.34, 0, 0.19), 0.20, 0.09, "PlankDark", verts=12, rot=(D(90), 0, 0)))
    parts.append(kit.cyl("Hub", (0.34, 0, 0.19), 0.055, 0.13, "Plank", verts=8, rot=(D(90), 0, 0)))
    for s in (-1, 1):
        parts.append(kit.box(f"Fork_{s:+d}", (0.28, s * 0.14, 0.30), (0.06, 0.05, 0.34), "PlankDark",
                             rot=(0, D(24), 0)))
        parts.append(kit.box(f"Handle_{s:+d}", (-0.62, s * 0.20, 0.50), (0.72, 0.055, 0.055), "Plank",
                             rot=(0, D(-10), 0)))
        parts.append(kit.box(f"Leg_{s:+d}", (-0.52, s * 0.20, 0.22), (0.05, 0.05, 0.34), "PlankDark"))
    parts.append(kit.box("Brace", (-0.50, 0, 0.44), (0.06, 0.44, 0.05), "PlankDark"))
    # 斗里装满的青草（两团，比一团更像"装满了"）
    parts.append(kit.sphere("Load_A", (-0.10, 0.02, 0.62), 0.20, "Canopy",
                            scale=(1.35, 0.85, 0.60), segs=10, rings=6))
    parts.append(kit.sphere("Load_B", (0.16, -0.04, 0.64), 0.16, "CanopyDk",
                            scale=(1.25, 0.85, 0.62), segs=8, rings=6))
    return parts


def build_clay_jars():
    """陶罐堆：3 个大小不一的罐子摆在木托板上。0.5m 高。"""
    parts = []

    def pot(tag, x, y, s, col):
        return [
            kit.cyl(f"{tag}_Body", (x, y, 0.16 * s), 0.17 * s, 0.32 * s, col, verts=10),
            kit.cone(f"{tag}_Shoulder", (x, y, 0.36 * s), 0.17 * s, 0.10 * s, 0.08 * s, col, verts=10),
            kit.cyl(f"{tag}_Neck", (x, y, 0.44 * s), 0.10 * s, 0.08 * s, "ClayDark", verts=10),
            kit.cyl(f"{tag}_Mouth", (x, y, 0.49 * s), 0.12 * s, 0.025 * s, col, verts=10, smooth=False),
        ]

    # 三只罐子沿 X 摊开：正视图里才不会互相挡住
    parts += pot("Big", -0.24, 0.04, 1.00, "Clay")
    parts += pot("Mid", 0.02, -0.16, 0.78, "ClayDark")
    parts += pot("Sml", 0.32, 0.14, 0.60, "Clay")
    parts.append(kit.box("Tray", (0, 0, 0.03), (0.92, 0.74, 0.06), "PlankDark"))
    parts.append(kit.box("Tray_B", (-0.40, -0.26, 0.06), (0.20, 0.16, 0.05), "Plank", rot=(0, 0, D(14))))
    parts.append(kit.cone("Straw", (0.22, 0.24, 0.12), 0.08, 0.004, 0.16, "Straw", verts=4,
                          rot=(D(-70), 0, D(20))))
    return parts


def build_crate_stack():
    """木箱堆：5 只板条箱错落堆叠 + 一只麻袋。1.0m 高。"""
    parts = []
    layout = [
        (0.00, 0.00, 0.23, 0.46, 0.0, "Plank"),
        (0.50, 0.03, 0.23, 0.46, 9.0, "Plank"),
        (0.24, 0.02, 0.70, 0.44, -6.0, "PlankDark"),
        (-0.46, 0.18, 0.21, 0.42, 22.0, "Plank"),
        (0.26, -0.32, 0.16, 0.32, -18.0, "PlankDark"),
    ]
    for i, (x, y, z, s, r, col) in enumerate(layout):
        parts.append(kit.box(f"Crate_{i}", (x, y, z), (s, s, s * 0.92), col, rot=(0, 0, D(r))))
        # 朝外那一面上下两条板条 → 一眼看出是木箱而不是方块
        for j, oz in ((0, 0.34), (1, -0.34)):
            ox, oy = _rotz(0.0, -s * 0.5 - 0.012, r)
            parts.append(kit.box(f"Slat_{i}_{j}", (x + ox, y + oy, z + oz * s),
                                 (s * 1.02, 0.03, s * 0.15), "PlankDark", rot=(0, 0, D(r))))
        ox, oy = _rotz(-s * 0.5 - 0.012, 0.0, r)
        parts.append(kit.box(f"Slat_{i}_L", (x + ox, y + oy, z), (0.03, s * 0.7, s * 0.15),
                             "PlankDark", rot=(0, 0, D(r))))
    # 麻袋用麻布色（Bone 太白，正视图里像一个球）
    parts.append(kit.sphere("Sack", (-0.30, -0.34, 0.19), 0.17, "Plank",
                            scale=(1.0, 1.0, 1.15), segs=10, rings=6))
    parts.append(kit.box("Plank_Loose", (0.62, 0.34, 0.05), (0.90, 0.16, 0.05), "Plank",
                         rot=(0, 0, D(-24))))
    return parts


def build_chicken_coop():
    """鸡舍：架空板条箱 + 双坡顶 + 门洞 + 坡道 + 侧面产蛋箱（带草和蛋）。1.15m 高。"""
    parts = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(kit.box(f"Leg_{sx}_{sy}", (sx * 0.46, sy * 0.32, 0.13),
                                 (0.08, 0.08, 0.26), "PlankDark"))
    parts.append(kit.box("Body", (0, 0, 0.56), (1.06, 0.80, 0.60), "Plank"))
    for i in range(3):
        parts.append(kit.box(f"Strip_{i}", (-0.30 + i * 0.30, -0.42, 0.56),
                             (0.07, 0.04, 0.58), "PlankDark"))
    for s in (-1, 1):
        parts.append(kit.box(f"Roof_{s:+d}", (0, s * 0.24, 0.98), (1.20, 0.56, 0.05), "Roof",
                             rot=(D(s * 30), 0, 0)))
    parts.append(kit.box("Ridge", (0, 0, 1.11), (1.24, 0.08, 0.06), "Roof"))
    parts.append(kit.box("Door", (0.18, -0.415, 0.50), (0.24, 0.03, 0.26), "SoilRich"))
    parts.append(kit.box("Ramp", (0.18, -0.78, 0.24), (0.30, 0.70, 0.04), "Plank", rot=(D(-22), 0, 0)))
    for i in range(3):
        parts.append(kit.box(f"Cleat_{i}", (0.18, -0.60 - i * 0.16, 0.13 + i * 0.07),
                             (0.32, 0.05, 0.03), "PlankDark", rot=(D(-22), 0, 0)))
    parts.append(kit.box("Nest", (0.78, 0.02, 0.70), (0.44, 0.46, 0.28), "PlankDark"))
    parts.append(kit.box("Nest_Straw", (0.78, -0.04, 0.87), (0.38, 0.40, 0.06), "Straw"))
    parts.append(kit.sphere("Egg_A", (0.70, -0.06, 0.93), 0.055, "Bone",
                            scale=(1, 1, 1.25), segs=8, rings=4))
    parts.append(kit.sphere("Egg_B", (0.87, -0.11, 0.93), 0.05, "Bone",
                            scale=(1, 1, 1.25), segs=8, rings=4))
    for i in range(3):
        a = i * 120.0 + 40.0
        parts.append(kit.cone(f"Grass_{i}", (math.sin(D(a)) * 0.75, math.cos(D(a)) * 0.62, 0.09),
                              0.05, 0.004, 0.18, "TurfDark", verts=4, rot=_lean(12.0, a)))
    return parts


def build_birdhouse():
    """鸟屋：木杆 + 小房子 + 梯子 + 地面食盘。1.97m 高。"""
    parts = []
    parts.append(kit.box("Post", (0, 0, 0.70), (0.11, 0.11, 1.40), "PlankDark"))
    for s in (-1, 1):
        parts.append(kit.box(f"Brace_{s:+d}", (s * 0.13, 0, 0.20), (0.30, 0.07, 0.07), "PlankDark",
                             rot=(0, D(-s * 42), 0)))
    parts.append(kit.box("Base", (0, 0, 1.44), (0.42, 0.38, 0.05), "Plank"))
    parts.append(kit.box("House", (0, 0, 1.62), (0.32, 0.28, 0.32), "Wall"))
    for s in (-1, 1):
        parts.append(kit.box(f"Roof_{s:+d}", (0, s * 0.10, 1.84), (0.40, 0.26, 0.04), "Roof",
                             rot=(D(s * 38), 0, 0)))
    parts.append(kit.box("Hole", (0, -0.145, 1.66), (0.11, 0.03, 0.11), "SoilRich"))
    parts.append(kit.cyl("Perch", (0, -0.21, 1.55), 0.022, 0.14, "PlankDark", verts=6, rot=(D(90), 0, 0)))
    for s in (-1, 1):
        parts.append(kit.box(f"Ladder_{s:+d}", (s * 0.07, -0.20, 1.10), (0.045, 0.04, 0.58), "PlankDark"))
    for i in range(3):
        parts.append(kit.box(f"Rung_{i}", (0, -0.20, 0.88 + i * 0.18), (0.20, 0.035, 0.035), "Plank"))
    parts.append(kit.box("Tray", (0.30, -0.26, 0.05), (0.38, 0.30, 0.06), "Plank"))
    for s in (-1, 1):
        parts.append(kit.box(f"Tray_Rim_{s:+d}", (0.30, -0.26 + s * 0.13, 0.10), (0.38, 0.04, 0.06),
                             "PlankDark"))
    parts.append(kit.box("Seed", (0.30, -0.26, 0.09), (0.28, 0.18, 0.035), "Gold"))
    for i in range(3):
        a = i * 120.0 + 200.0
        parts.append(kit.cone(f"Grass_{i}", (math.sin(D(a)) * 0.34, math.cos(D(a)) * 0.30, 0.08),
                              0.045, 0.004, 0.16, "TurfDark", verts=4, rot=_lean(14.0, a)))
    return parts


def build_garden_gate():
    """园门：跨度正好 2.00m（顶掉一段栅栏就能装上），高 0.94m。"""
    parts = []
    for s in (-1, 1):
        parts.append(kit.box(f"GatePost_{s:+d}", (s * 0.94, 0, 0.43), (0.13, 0.13, 0.86), "PlankDark"))
        parts.append(kit.cone(f"GateCap_{s:+d}", (s * 0.94, 0, 0.90), 0.09, 0.02, 0.08, "PlankDark", verts=8))
    for z in (0.26, 0.68):
        parts.append(kit.box(f"Rail_{z:.2f}", (0, 0, z), (1.76, 0.06, 0.08), "Plank"))
    for i in range(9):
        x = -0.80 + i * 0.20
        h = 0.56 if i % 2 else 0.62
        parts.append(kit.box(f"Picket_{i}", (x, 0, 0.20 + h * 0.5), (0.075, 0.05, h), "Plank"))
    # 斜撑：两端落在上下横杆**内侧一点**，否则它的下端会插进地里
    parts.append(kit.box("Brace", (0, 0, 0.50), (0.06, 0.045, 1.07), "PlankDark", rot=(0, D(17.4), 0)))
    for z in (0.26, 0.68):
        parts.append(kit.box(f"Hinge_{z:.2f}", (-0.88, 0, z), (0.14, 0.10, 0.06), "Metal"))
    parts.append(kit.box("Sill", (0, 0, 0.03), (2.00, 0.34, 0.06), "StoneLt"))
    return parts


def build_laundry_line():
    """晾衣绳：两根木杆 + 下垂的绳 + 三件衣物 + 地上的洗衣篮。1.70m 高。"""
    parts = []
    post_h = 1.70
    for s in (-1, 1):
        parts.append(kit.box(f"Post_{s:+d}", (s * 1.00, 0, post_h * 0.5), (0.10, 0.10, post_h), "PlankDark"))
        parts.append(kit.box(f"Arm_{s:+d}", (s * 0.86, 0, post_h - 0.06), (0.40, 0.07, 0.07), "Plank"))
        parts.append(kit.sphere(f"Foot_{s:+d}", (s * 1.00, 0, 0.10), 0.16, "StoneLt",
                                scale=(1.2, 1.2, 0.6), segs=8, rings=4))
    sag = (1.60, 1.50, 1.45, 1.50, 1.60)
    for i in range(4):
        x0, x1 = -0.86 + i * 0.43, -0.43 + i * 0.43
        z0, z1 = sag[i], sag[i + 1]
        ang = math.degrees(math.atan2(z1 - z0, x1 - x0))
        parts.append(kit.box(f"Rope_{i}", ((x0 + x1) * 0.5, 0, (z0 + z1) * 0.5),
                             (math.hypot(x1 - x0, z1 - z0), 0.03, 0.03), "Rope", rot=(0, D(-ang), 0)))
    cloth = [(-0.62, 0.42, 0.46, "Cloth", 8.0),
             (-0.02, 0.50, 0.40, "PetalB", -5.0),
             (0.58, 0.38, 0.52, "Wall", 6.0)]
    for i, (x, w, h, col, r) in enumerate(cloth):
        parts.append(kit.box(f"Cloth_{i}", (x, 0, 1.48 - h * 0.5), (w, 0.022, h), col, rot=(0, 0, D(r))))
        for s in (-1, 1):
            parts.append(kit.box(f"Peg_{i}_{s:+d}", (x + s * w * 0.36, 0, 1.47), (0.022, 0.03, 0.06), "Plank"))
    parts.append(kit.cyl("Basket", (0.56, 0.56, 0.14), 0.20, 0.28, "Plank", verts=10))
    parts.append(kit.cyl("Basket_Rim", (0.56, 0.56, 0.28), 0.21, 0.03, "PlankDark", verts=10, smooth=False))
    parts.append(kit.box("Basket_Cloth", (0.56, 0.56, 0.31), (0.26, 0.26, 0.06), "Cloth", rot=(0, 0, D(12))))
    return parts


# ================================================================ 模块表
MODULES = {
    # A. 边界废墟墙（3 个变体，沿墙交替摆 → 一眼看不出重复）
    "wall_ruin":   ("砖墙废墟·A", lambda: build_wall_ruin(WALL_VARIANTS["wall_ruin"])),
    "wall_ruin_b": ("砖墙废墟·B", lambda: build_wall_ruin(WALL_VARIANTS["wall_ruin_b"])),
    "wall_ruin_c": ("砖墙废墟·C", lambda: build_wall_ruin(WALL_VARIANTS["wall_ruin_c"])),
    # B. 农村装饰
    "haystack":     ("草垛",   build_haystack),
    "well":         ("水井",   build_well),
    "scarecrow":    ("稻草人", build_scarecrow),
    "wheelbarrow":  ("独轮车", build_wheelbarrow),
    "clay_jars":    ("陶罐堆", build_clay_jars),
    "crate_stack":  ("木箱堆", build_crate_stack),
    "chicken_coop": ("鸡舍",   build_chicken_coop),
    "birdhouse":    ("鸟屋",   build_birdhouse),
    "garden_gate":  ("园门",   build_garden_gate),
    "laundry_line": ("晾衣绳", build_laundry_line),
}

TRI_BUDGET = (200, 700)   # 装饰的三角面预算；墙体另有 400 的上限
WALL_TRI_MAX = 400


# ================================================================ 组装 / 导出
def _set_pivot(obj, center_xy=True):
    """
    把原点移到**底面中心**：z 恒 = 0（作者坐标系里的地面），x/y = 足迹中心
    （墙体保持作者 x=0，保证段与段能对接）。

    z 一律取 0，**不取几何最低点**：一根支棱出来的草叶、一个半陷进土里的麻袋
    都会把"最低点"拉到地面以下，用它当原点整只模型就浮起来了（草垛的穿心木杆
    当初就是这么浮了 10.5cm）。低于地面的部分只报警、不参与定位。
    """
    import mathutils
    pts = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    cx = (min(xs) + max(xs)) * 0.5 if center_xy else 0.0
    cy = (min(ys) + max(ys)) * 0.5 if center_xy else 0.0
    obj["zmin"] = min(zs)
    kit.set_origin(obj, (cx, cy, 0.0))
    return obj


def _assemble(mid):
    """建部件 → 合并 → 烘变换 → 原点归到底面中心。**不 reset 场景、不导出**。"""
    label, fn = MODULES[mid]
    obj = kit.join(fn(), "EN_" + mid)
    kit.apply_transforms(obj)
    _set_pivot(obj, center_xy=not mid.startswith("wall_ruin"))
    return obj


def build(mid, do_export=True):
    kit.reset()
    obj = _assemble(mid)
    tris = kit.tri_count(obj)
    dims = tuple(round(v, 3) for v in obj.dimensions)
    if do_export:
        path = os.path.join(ENV_DIR, mid + ".fbx")
        kit.export_fbx(obj, path)
    flag = ""
    if mid.startswith("wall_ruin"):
        if tris > WALL_TRI_MAX:
            flag = f"  ★超预算(>{WALL_TRI_MAX})"
    elif not (TRI_BUDGET[0] <= tris <= TRI_BUDGET[1]):
        flag = f"  ★超预算{TRI_BUDGET}"
    zmin = obj.get("zmin", 0.0)
    if zmin < -0.03:
        flag += f"  ★有 {abs(zmin):.3f}m 几何沉在地面下"
    print(f"[SEEDFALL-RUINS] {mid:<13} {MODULES[mid][0]:<10} "
          f"dims(x,y,z)={dims} tris={tris} zmin={zmin:+.3f}{flag}")
    return obj, dims, tris


def build_all():
    rows = []
    for mid in MODULES:
        _, dims, tris = build(mid)
        rows.append(dict(id=mid, label=MODULES[mid][0], dims=dims, tris=tris))
    kit.ensure_dir(MANIFEST)
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(dict(generator="gen_ruins.py", blender=bpy_version(),
                       dir=ENV_DIR, modules=rows), f, ensure_ascii=False, indent=2)
    print(f"[SEEDFALL-RUINS] manifest -> {MANIFEST}")
    print("[SEEDFALL-RUINS] ★ 记得删掉 .fbx.meta，否则 Unity 不会重新导入：")
    print('   Get-ChildItem "%s\\*.fbx.meta" | Remove-Item -Force -ErrorAction SilentlyContinue' % ENV_DIR)


def bpy_version():
    import bpy
    return bpy.app.version_string


# ================================================================ 预览图
CJK_FONTS = (r"C:\Windows\Fonts\Deng.ttf", r"C:\Windows\Fonts\simhei.ttf",
             r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simsun.ttc")


def _hex_rgb(h):
    h = (h or "#CCCCCC").lstrip("#")
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    return (int(h[0:2], 16) / 255.0, int(h[2:4], 16) / 255.0, int(h[4:6], 16) / 255.0)


def _srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _linearize_materials():
    """
    ★ 预览专用的颜色校正（不导出，只影响渲染）★

    kit.mat() 把 #A8443A 这样的 sRGB 数值**直接写进 Blender 的 Base Color 插槽**，
    而那个插槽是线性的 —— 于是渲染出来比调色板亮一大截：砖红 #A8443A 会渲成肉粉色。
    Unity 那边不吃这个亏（材质颜色按 sRGB 解释，屏幕上是调色板原色），
    所以这里渲染前按 sRGB→线性 重设一遍，让**预览图看到的颜色 = Unity 里看到的颜色**。
    导出的 .fbx 不受影响（导出走的是另一趟进程，且颜色写的是原值）。
    """
    import bpy
    n = 0
    for m in bpy.data.materials:
        key = kit.PALETTE.get(m.name)
        if not key or m.node_tree is None:
            continue
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf is None:
            continue
        rgb = [_srgb_to_linear(c) for c in _hex_rgb(key)]
        sock = bsdf.inputs.get("Base Color")
        if sock is not None:
            sock.default_value = (*rgb, 1.0)
        # 发光材质（标签文字）也要一起转 —— 否则标签会渲成一片浅灰，看不清字
        es = bsdf.inputs.get("Emission Strength")
        ec = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
        if ec is not None and es is not None and es.default_value > 0.0:
            ec.default_value = (*rgb, 1.0)
        m.diffuse_color = (*rgb, 1.0)
        n += 1
    return n


def _font():
    import bpy
    for p in CJK_FONTS:
        if os.path.isfile(p):
            try:
                return bpy.data.fonts.load(p)
            except Exception:
                continue
    return None


def _label(text, x, z, size):
    """在模型下方放一行标签。发光材质 + 正则视图变换 → 颜色不受灯光影响。"""
    import bpy
    bpy.ops.object.text_add(location=(x, 0.0, z))
    o = bpy.context.active_object
    o.data.body = text
    o.data.size = size
    o.data.align_x = 'CENTER'
    o.data.align_y = 'CENTER'
    o.rotation_euler = (D(90), 0, 0)
    f = _font()
    if f is not None:
        o.data.font = f
    o.data.materials.clear()
    o.data.materials.append(kit.mat("Ink", emission=1.0))
    return o


def _frame_camera(blend, radius):
    """★ kit.render_preview 里正交相机距离 = radius*6，大到几十米时会被默认 clip_end=100 裁掉。
    这里先把 PreviewCam 建好（kit 会复用同名相机），把裁剪面放大。"""
    cam = blend.data.objects.get("PreviewCam")
    if cam is None:
        cam = blend.data.objects.new("PreviewCam", blend.data.cameras.new("PreviewCam"))
        blend.context.collection.objects.link(cam)
    cam.data.clip_start = 0.05
    cam.data.clip_end = max(200.0, radius * 30.0)
    return cam


def preview(ids, path, size=(3200, 560), rows=1, gap=DECOR_GAP, angle=0.0, elev=0.0,
            labels=True, margin=1.10, ambient=0.75, key_energy=2.6, grid=True):
    """
    把若干模块排成一排（或 rows 排）渲一张联络图。
    标签用 Blender 文字对象渲染（中文字体取系统字体），不依赖外部图像库。
    """
    import bpy
    import mathutils
    kit.reset()

    per = int(math.ceil(len(ids) / float(rows)))
    bands = [ids[i:i + per] for i in range(0, len(ids), per)]
    row_gap = 1.15 if labels else 0.55
    placed = []          # (mid, obj, row_z)
    bases = []           # 每排的基准高度（= 该排模型的脚底）
    row_z = 0.0
    max_w = 0.0
    for band in bands:
        base_z = row_z
        bases.append(base_z)
        built = []
        x = 0.0
        for mid in band:
            obj = _assemble(mid)
            w = obj.dimensions.x
            obj.location.x = x + w * 0.5
            x += w + gap
            built.append((mid, obj, w))
        total_w = x - gap
        h = max(o.dimensions.z for _, o, _ in built)
        for mid, obj, w in built:
            obj.location.x -= total_w * 0.5
            obj.location.z += base_z
        placed.extend([(mid, o, base_z) for mid, o, _ in built])
        max_w = max(max_w, total_w)
        row_z -= (h + row_gap)

    z_top = max(rz + o.dimensions.z for _, o, rz in placed)
    z_bot = bases[-1]
    if labels:
        for mid, obj, rz in placed:
            hh = obj.dimensions.z
            _label(f"{MODULES[mid][0]}", obj.location.x, rz - 0.30, 0.26)
            _label(f"{mid}  {hh:.2f}m  {kit.tri_count(obj)}tri", obj.location.x, rz - 0.62, 0.165)
        z_bot = bases[-1] - 0.86

    # 地面参考板（浅色，便于判断"有没有浮空/陷进地里"）
    if grid:
        bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0, 0, -0.002))
        gp = bpy.context.active_object
        gp.name = "PreviewGround"
        gp.scale = (max_w * 0.5 + 1.0, 6.0, 1.0)
        gp.data.materials.clear()
        gp.data.materials.append(kit.mat("Turf", emission=0.25))

    half_w = max_w * 0.5
    half_h = (z_top - z_bot) * 0.5
    cz = (z_top + z_bot) * 0.5
    radius = max(half_w, half_h, 0.2)
    W, H = size
    aspect = float(W) / float(H)
    want_w = 2.0 * half_w * margin
    want_h = 2.0 * half_h * margin
    if W >= H:
        need = max(want_w, want_h * aspect)
    else:
        need = max(want_h, want_w / aspect)
    zoom = need / radius

    _frame_camera(bpy, radius)
    _linearize_materials()
    vs = bpy.context.scene.view_settings
    try:
        vs.view_transform = 'Standard'      # 平涂色要按调色板原色看，不能用 AgX
        vs.look = 'None'
        vs.exposure = 0.0
    except Exception:
        pass
    kit.render_preview(path, center=(0.0, 0.0, cz), radius=radius, size=size,
                       angle=angle, elev=elev, ortho=True, zoom=zoom,
                       ambient=ambient, key_energy=key_energy)
    print(f"[SEEDFALL-RUINS] preview -> {path}  ({len(ids)} 模块, {W}x{H}, "
          f"ortho_scale={radius * zoom:.2f}m)")
    return path


def verify(ids=None):
    """
    把刚导出的 .fbx **重新导入**一遍，逐模块核对：轴向、尺寸、材质名、三角面数。

    为什么非做不可：预渲染图看不出轴向问题。轴向错了（自己多烘一次 Y-up）在 Blender 里
    一切正常，到 Unity 才整片躺倒 —— 只有"导出→回读→比尺寸"能在本地把这件事证死：
      · 回读后 高度 必须仍然落在 **Z（Blender）= Unity 的 Y**，且与源模型尺寸一致
      · 材质名必须是调色板键名（ToonLook 按 _BaseColor 取色，名字是两端的契约）
    """
    import bpy
    import mathutils
    with open(MANIFEST, encoding="utf-8") as f:
        want = {m["id"]: m for m in json.load(f)["modules"]}
    kit.reset()
    bad = 0
    for mid in (ids or list(MODULES)):
        path = os.path.join(ENV_DIR, mid + ".fbx")
        if not os.path.isfile(path):
            print(f"[VERIFY] {mid:<13} ★ 缺少 {path}")
            bad += 1
            continue
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=path)
        new = [o for o in bpy.data.objects if o not in before]
        meshes = [o for o in new if o.type == 'MESH']
        pts = []
        for o in meshes:
            pts += [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
        dims = tuple(round(max(p[i] for p in pts) - min(p[i] for p in pts), 3) for i in range(3))
        mats = []
        for o in meshes:
            for m in o.data.materials:
                if m and m.name not in mats:
                    mats.append(m.name)
        tris = sum(kit.tri_count(o) for o in meshes)
        exp = want[mid]["dims"]
        d_ok = all(abs(dims[i] - exp[i]) < 0.012 for i in range(3))
        m_ok = all(n in kit.PALETTE for n in mats) and len(mats) > 0
        z_ok = dims[2] > 0.05          # 高度没塌成 0 → 没有"躺倒"
        ok = d_ok and m_ok and z_ok
        bad += 0 if ok else 1
        print(f"[VERIFY] {'PASS' if ok else '★FAIL'} {mid:<13} 回读 dims={dims} (源 {tuple(exp)}) "
              f"高{z_ok and '√' or '×'} 尺寸{d_ok and '√' or '×'} 材质{m_ok and '√' or '×'} "
              f"tris={tris} mats={','.join(mats)}")
        for o in new:
            bpy.data.objects.remove(o, do_unlink=True)
        for blk in (bpy.data.meshes, bpy.data.materials):
            for it in list(blk):
                if it.users == 0:
                    blk.remove(it)
    print(f"[VERIFY] 合计 {len(ids or MODULES)} 个模块，失败 {bad} 个")
    return bad


def preview_all():
    wall_ids = ["wall_ruin", "wall_ruin_b", "wall_ruin_c"]
    decor_ids = [k for k in MODULES if not k.startswith("wall_ruin")]
    kit.ensure_dir(os.path.join(CAPTURE_DIR, "x.png"))
    # ① 交付要求的联络图：所有新模块排成一排
    preview(wall_ids + decor_ids, os.path.join(CAPTURE_DIR, "ruins_preview.png"),
            size=(3200, 560), rows=1)
    # ② 两排大图：每个模块看得更清
    preview(wall_ids + decor_ids, os.path.join(CAPTURE_DIR, "ruins_sheet.png"),
            size=(1920, 1080), rows=2, gap=0.55)
    # ③ 砖墙单体特写（3/4 视角，用来判砖色与错缝）
    preview(["wall_ruin"], os.path.join(CAPTURE_DIR, "ruins_wall.png"),
            size=(1500, 900), rows=1, angle=26.0, elev=15.0, labels=False, margin=1.18)
    # ④ 三个墙体变体一排（3/4 视角，看高低起伏的差异）
    preview(wall_ids, os.path.join(CAPTURE_DIR, "ruins_wall_row.png"),
            size=(1900, 700), rows=1, gap=0.30, angle=22.0, elev=13.0, labels=False, margin=1.15)


# ================================================================ 入口
if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("用法: blender -b --factory-startup -P gen_ruins.py -- <模块id...> | all | preview | verify")
        print("  模块:", ", ".join(MODULES))
        print("  all = 全部生成并导出；verify = 回读 fbx 核对；preview = 只出 Docs/captures 预览图")
    elif args[0] == "all":
        build_all()
    elif args[0] == "preview":
        preview_all()
    elif args[0] == "verify":
        verify()
    else:
        for mid in args:
            build(mid)
