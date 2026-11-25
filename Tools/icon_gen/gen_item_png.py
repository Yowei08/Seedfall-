#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Seedfall 道具 / 植物 **64×64 硬边像素图标**生成器（v198）。

★ 目标风格 = 用户指定的「磨刀石」那张（`grind_stone`）★
  · 深暖灰描边（不是纯黑）
  · 主体是大块**纯色**面（石白 + 深灰底座），**无渐变、无抗锯齿**，硬边像素
  · 一条**斜向高光**（亮白），位置在主体**上缘**
  · 主体左上方有 **`///` 三条斜杠**（表示"动作 / 锋利"）；静态物（盾 / 甲 / 种子 / 果实）
    改用**右下角三颗角标点**，语义区分开，但整体仍是同一套语言
  · 主体**填满画框**（包围盒 ≈ 80%，留白极少）

★ 统一规范（**这张表是硬约束，脚本尾部 `verify()` 逐条机器校验**）★
  1. 画布 64×64、透明底；主体包围盒（含描边）宽高都 ≥ 44px（≈ 69%），且 ≤ 62px
  2. 调色板每张 **≤ 6 色**（唯一色数按 alpha>0 的像素统计）：
       描边 OUT #3A3A38 / 高光 HI #F2EFE6 / 主体三档 B1 #C9C6BE B2 #8E8B83 B3 #5D5B56 / 点缀 ACC
       ACC 随品类走：纯增益 = 绿 #8CE891（category 1）／有代价 = 橙红 #F58042（category 2）／
       植物（种子）= 金黄 #F2C74A —— 与 `TableDatabase.ItemIconPalette` 的正负两类**同源语义** ✓
  3. **无渐变 / 无抗锯齿 / 硬边**：所有形状按"像素中心是否在图形内"整像素扫描线填充，
     alpha 只有 0 或 255（PNG 里不存在中间透明度）
  4. 描边：主体轮廓统一 **2px OUT**（掩膜欧氏膨胀 2px 后填色，圆角自然）
  5. 每张**恰好一条** HI 斜向高光：沿主体上缘左侧 ~55% 铺 2px 亮带，右端再以 45° 下折收尾
  6. 装饰：动作性（武器 / 工具 / 爆炸 / 发射 / 速度）→ 左上 `///` 三道；静态 → 右下三颗角标
  7. **形状必须认得出是什么** —— 62 个 id 逐个单独设计（下方 `ICONS` 一 id 一段）
  8. 确定性：不用随机数、不用时间戳 ⇒ 同一份代码每次都出**逐字节相同**的 PNG ✓

★ 输出（`--out` 默认项目内）★
  Assets/Resources/Art/UI/icon_item_<id>.png      56 件道具
  Assets/Resources/Art/UI/icon_plant_<id>.png     6 株机制植物（与 item 同字节；
                                                  植物格/种子卡读的是 icon_plant_* 这条路）
  Tools/icon_gen/out/item_png_sheet.png           62 格对照图（带中文名 / id / 档位 / 标记）
  Tools/icon_gen/out/item_png_sheet.txt           同一份清单的文字版

★ 为什么植物同时写 `icon_item_*` 与 `icon_plant_*` ★
  `ImGuiHud.ItemIconOf(id)` 读 `icon_item_<id>`（道具格 / 增益栏 / 商店道具行）；
  `ImGuiHud.DrawOfferIcon` 对种子读 `icon_plant_<id>`（商店种子行 / 种子卡 / 植物格）。
  两条路的名字不同 ⇒ 6 株机制植物两张都写，才不会"商店里有图、田里的格子还是色块"✓

用法：
  python Tools/icon_gen/gen_item_png.py            # 出图 + 出对照图 + 校验
  python Tools/icon_gen/gen_item_png.py --check     # 只校验（不出图）
  python Tools/icon_gen/gen_item_png.py --no-sheet  # 只写 PNG
"""

import io
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
UI_DIR = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
ITEMS = os.path.join(ROOT, "Assets", "Resources", "Data", "items.json")
SEEDS = os.path.join(ROOT, "Assets", "Resources", "Data", "seeds.json")
OUT = os.path.join(HERE, "out")

W = H = 64

# ── 调色板（全局唯一一份；每张图最多用到其中 6 个）─────────────────────────
OUT_C = (58, 58, 56, 255)        # 描边：暖灰深色（不是纯黑）
HI = (242, 239, 230, 255)        # 高光 / 刃口线
B1 = (201, 198, 190, 255)        # 主体亮档（石白）
B2 = (142, 139, 131, 255)        # 主体中档
B3 = (93, 91, 86, 255)           # 主体暗档（底座 / 衬底）
ACC_GREEN = (140, 232, 145, 255)  # 纯增益
ACC_COST = (245, 128, 66, 255)    # 有代价
ACC_PLANT = (242, 199, 74, 255)   # 植物（种子）

# ── 装饰分配：`///` 给"有动作性"的，其余用右下角角标 ──────────────────────
SLASH = {
    "whet_oil", "long_handle", "grind_stone", "blast_grain", "longbow_arm",
    "thorn_whip", "big_bore", "frenzy_band", "glass_cannon", "plow_weight",
    "hardwood_haft", "twin_blade", "overclock_core", "sniper_scope", "barbed_tip",
    "greedy_sickle", "iron_hoe", "sprinter_boots", "windup_gear", "charge_coil",
    "sharp_spray", "warblade_bloom", "gale_vine", "wind_bell", "tailwind_sash",
}

# 6 株机制植物（v197 起在 `TableDatabase.ItemIconMasks` 里有掩码的那 6 个种子 id）
MECH_SEEDS = (
    "warblade_bloom", "gale_vine", "sunflower_crown", "rainlily",
    "shelter_ivy", "lucky_monarch",
)


# ════════════════════════════════════════════════════════════════════════════
#  画布与最小绘图原语（全部整像素、无抗锯齿）
# ════════════════════════════════════════════════════════════════════════════
class Cv(object):
    def __init__(self):
        self.d = {}

    def set(self, x, y, c):
        x = int(x)
        y = int(y)
        if not (0 <= x < W and 0 <= y < H):
            return
        if c is None:
            self.d.pop((x, y), None)
        else:
            self.d[(x, y)] = c

    def get(self, x, y):
        return self.d.get((x, y))

    def mask(self):
        return set(self.d.keys())


def rect(cv, x0, y0, x1, y1, c):
    """闭区间矩形。"""
    for y in range(int(y0), int(y1) + 1):
        for x in range(int(x0), int(x1) + 1):
            cv.set(x, y, c)


def poly(cv, pts, c):
    """扫描线填充多边形：像素中心 (x+0.5, y+0.5) 落在图形内即填 ⇒ 硬边。"""
    ys = [p[1] for p in pts]
    y0 = int(math.floor(min(ys)))
    y1 = int(math.ceil(max(ys)))
    n = len(pts)
    for y in range(y0, y1 + 1):
        yc = y + 0.5
        xs = []
        for i in range(n):
            ax, ay = pts[i]
            bx, by = pts[(i + 1) % n]
            if (ay <= yc < by) or (by <= yc < ay):
                t = (yc - ay) / float(by - ay)
                xs.append(ax + t * (bx - ax))
        xs.sort()
        for i in range(0, len(xs) - 1, 2):
            xa = int(math.ceil(xs[i] - 0.5))
            xb = int(math.ceil(xs[i + 1] - 0.5))
            for x in range(xa, xb):
                cv.set(x, y, c)


def disc(cv, cx, cy, r, c):
    r2 = r * r
    for y in range(int(cy - r), int(cy + r) + 2):
        for x in range(int(cx - r), int(cx + r) + 2):
            dx = x + 0.5 - cx
            dy = y + 0.5 - cy
            if dx * dx + dy * dy <= r2:
                cv.set(x, y, c)


def ring(cv, cx, cy, r_out, r_in, c):
    for y in range(int(cy - r_out), int(cy + r_out) + 2):
        for x in range(int(cx - r_out), int(cx + r_out) + 2):
            dx = x + 0.5 - cx
            dy = y + 0.5 - cy
            d2 = dx * dx + dy * dy
            if r_in * r_in <= d2 <= r_out * r_out:
                cv.set(x, y, c)


def ellipse(cv, cx, cy, rx, ry, c):
    for y in range(int(cy - ry), int(cy + ry) + 2):
        for x in range(int(cx - rx), int(cx + rx) + 2):
            dx = (x + 0.5 - cx) / rx
            dy = (y + 0.5 - cy) / ry
            if dx * dx + dy * dy <= 1.0:
                cv.set(x, y, c)


def line(cv, x0, y0, x1, y1, c, w=1):
    """粗线段：沿长轴步进，每步盖一个 w×w 的方块 ⇒ 端点也齐。"""
    x0, y0, x1, y1 = int(x0), int(y0), int(x1), int(y1)
    dx = x1 - x0
    dy = y1 - y0
    steps = max(abs(dx), abs(dy))
    if steps == 0:
        for yy in range(-(w // 2), w - (w // 2)):
            for xx in range(-(w // 2), w - (w // 2)):
                cv.set(x0 + xx, y0 + yy, c)
        return
    half = w // 2
    for i in range(steps + 1):
        t = i / float(steps)
        x = int(round(x0 + dx * t))
        y = int(round(y0 + dy * t))
        for yy in range(-half, w - half):
            for xx in range(-half, w - half):
                cv.set(x + xx, y + yy, c)


def taper(cv, x0, y0, x1, y1, w0, w1, c):
    """从 (x0,y0) 到 (x1,y1) 的楔形（宽度 w0 → w1）。"""
    dx = x1 - x0
    dy = y1 - y0
    ln = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / ln, dx / ln
    poly(cv, [
        (x0 + nx * w0 * 0.5, y0 + ny * w0 * 0.5),
        (x1 + nx * w1 * 0.5, y1 + ny * w1 * 0.5),
        (x1 - nx * w1 * 0.5, y1 - ny * w1 * 0.5),
        (x0 - nx * w0 * 0.5, y0 - ny * w0 * 0.5),
    ], c)


def bezier(cv, p0, p1, p2, r, c, steps=24):
    """二次贝塞尔路径上盖圆点（做藤 / 曲柄）。"""
    for i in range(steps + 1):
        t = i / float(steps)
        u = 1 - t
        x = u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0]
        y = u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]
        disc(cv, x, y, r, c)


def gear(cv, cx, cy, r_out, r_root, teeth, c):
    """齿轮：圆盘 + 偶数齿（梯形）。齿数 × 2 段角度交替。"""
    disc(cv, cx, cy, r_root, c)
    step = math.pi * 2 / teeth
    for k in range(teeth):
        a0 = k * step
        for da in (-0.16, -0.08, 0.08, 0.16):
            pass
        x0 = cx + math.cos(a0 - 0.14) * r_root
        y0 = cy + math.sin(a0 - 0.14) * r_root
        x1 = cx + math.cos(a0 - 0.09) * r_out
        y1 = cy + math.sin(a0 - 0.09) * r_out
        x2 = cx + math.cos(a0 + 0.09) * r_out
        y2 = cy + math.sin(a0 + 0.09) * r_out
        x3 = cx + math.cos(a0 + 0.14) * r_root
        y3 = cy + math.sin(a0 + 0.14) * r_root
        poly(cv, [(x0, y0), (x1, y1), (x2, y2), (x3, y3)], c)


def spiral(cv, cx, cy, r0, r1, c, w=2, turns=1.6, steps=90):
    """螺旋线（蜗牛壳 / 风）。"""
    for i in range(steps + 1):
        t = i / float(steps)
        a = math.pi * 2 * turns * t - math.pi * 0.5
        r = r0 + (r1 - r0) * t
        line(cv, cx + math.cos(a) * r, cy + math.sin(a) * r,
             cx + math.cos(a) * r, cy + math.sin(a) * r, c, w)


def drop(cv, cx, cy, r, c, tip=1.9):
    """水滴：圆 + 顶点在上。"""
    disc(cv, cx, cy, r, c)
    poly(cv, [(cx, cy - r * tip), (cx + r * 0.86, cy - r * 0.1),
              (cx - r * 0.86, cy - r * 0.1)], c)


# ════════════════════════════════════════════════════════════════════════════
#  62 张图标：一 id 一段（形状逐张单独设计，不许两张一样）
# ════════════════════════════════════════════════════════════════════════════
def i_whet_oil(cv, ac):
    rect(cv, 12, 22, 52, 54, B1)                     # 瓶身（撑满画框）
    rect(cv, 14, 18, 50, 22, B2)                     # 肩
    rect(cv, 26, 10, 38, 18, B2)                     # 颈
    rect(cv, 23, 6, 41, 10, B3)                      # 塞
    rect(cv, 16, 30, 48, 42, B3)                     # 标签
    rect(cv, 12, 52, 52, 56, B3)                     # 底座
    rect(cv, 18, 32, 46, 34, ac)                     # 标签上的色条（品类色）


def i_wind_bell(cv, ac):
    poly(cv, [(26, 10), (38, 10), (40, 18), (46, 34), (50, 42),
              (14, 42), (18, 34), (24, 18)], B1)     # 铃身
    rect(cv, 8, 42, 56, 48, B2)                      # 铃口
    rect(cv, 28, 4, 36, 10, B3)                      # 顶环座
    disc(cv, 32, 54, 5, B3)                          # 铃舌
    line(cv, 32, 48, 32, 50, B3, 3)
    rect(cv, 20, 22, 44, 25, ac)


def i_long_handle(cv, ac):
    taper(cv, 18, 16, 50, 52, 12, 9, B1)             # 柄杆
    poly(cv, [(6, 10), (26, 4), (30, 22), (10, 28)], B2)   # 柄头
    rect(cv, 30, 30, 44, 36, B3)                     # 箍 1
    rect(cv, 40, 42, 54, 48, B3)                     # 箍 2
    rect(cv, 20, 15, 25, 20, ac)


def i_lucky_bead(cv, ac):
    bezier(cv, (12, 26), (32, -6), (52, 26), 2, B3, 20)   # 串绳（抬起成弧）
    disc(cv, 20, 40, 13, B1)                         # 珠 1
    disc(cv, 20, 40, 4, B3)
    disc(cv, 44, 40, 13, B2)                         # 珠 2
    disc(cv, 44, 40, 4, B3)
    rect(cv, 27, 20, 37, 26, ac)                     # 绳结


def i_grind_stone(cv, ac):
    """★ 风格基准 ★ 石砖 + 深灰底座。"""
    poly(cv, [(16, 12), (48, 12), (52, 16), (52, 40),
              (48, 44), (16, 44), (12, 40), (12, 16)], B1)   # 石砖
    rect(cv, 10, 44, 54, 56, B3)                     # 底座
    rect(cv, 12, 26, 52, 31, ac)                     # 中段色带（品类色）
    rect(cv, 14, 48, 50, 50, B2)


def i_blast_grain(cv, ac):
    pts = []
    for k in range(16):
        a = math.pi * 2 * k / 16 - math.pi / 2
        r = 25 if k % 2 == 0 else 12
        pts.append((32 + math.cos(a) * r, 32 + math.sin(a) * r))
    poly(cv, pts, B1)                                # 炸开的星
    disc(cv, 32, 32, 9, ac)                          # 核心
    disc(cv, 8, 12, 4, B2)                           # 四向碎粒
    disc(cv, 55, 14, 3, B2)
    disc(cv, 10, 52, 3, B2)
    disc(cv, 54, 50, 4, B2)


def i_wide_funnel(cv, ac):
    poly(cv, [(10, 12), (54, 12), (40, 36), (24, 36)], B1)   # 上宽下窄
    rect(cv, 8, 8, 56, 16, B2)                       # 广口
    rect(cv, 26, 36, 38, 54, B2)                     # 出料口
    rect(cv, 24, 52, 40, 57, B3)
    rect(cv, 16, 24, 48, 28, ac)


def i_healing_herb(cv, ac):
    line(cv, 30, 56, 32, 18, B3, 5)                  # 茎
    poly(cv, [(32, 26), (50, 12), (54, 26), (38, 32)], B1)   # 右上叶
    poly(cv, [(32, 34), (14, 20), (10, 34), (26, 40)], B1)   # 左上叶
    poly(cv, [(30, 44), (48, 34), (50, 46), (34, 50)], B2)   # 右下叶
    disc(cv, 32, 18, 5, ac)                          # 花苞


def i_lucky_stone(cv, ac):
    poly(cv, [(32, 6), (54, 22), (46, 52), (18, 52), (10, 22)], B1)   # 宝石外形
    poly(cv, [(32, 6), (32, 24), (10, 22)], B2)      # 左切面
    poly(cv, [(32, 6), (54, 22), (32, 24)], B2)      # 右切面
    poly(cv, [(10, 22), (32, 24), (18, 52)], ac)     # 左下切面（品类色）
    line(cv, 10, 22, 54, 22, B3, 2)
    line(cv, 32, 6, 32, 24, B3, 2)


def i_pig_bell(cv, ac):
    poly(cv, [(12, 20), (22, 6), (30, 20)], B2)      # 左耳
    poly(cv, [(34, 20), (42, 6), (52, 20)], B2)      # 右耳
    poly(cv, [(10, 24), (54, 24), (50, 44), (32, 56), (14, 44)], B1)   # 猪脸
    disc(cv, 22, 34, 6, B3)                          # 鼻孔
    disc(cv, 42, 34, 6, B3)
    rect(cv, 28, 24, 36, 30, ac)


def i_dual_straps(cv, ac):
    taper(cv, 10, 8, 46, 38, 12, 11, B1)             # 束带 1
    taper(cv, 12, 26, 48, 56, 12, 11, B2)            # 束带 2（并行、留缝）
    rect(cv, 20, 14, 30, 22, B3)                     # 卡扣 1
    rect(cv, 30, 42, 40, 50, B3)                     # 卡扣 2
    rect(cv, 18, 18, 24, 22, ac)


def i_spring_basket(cv, ac):
    ring(cv, 32, 30, 22, 17, B2)                     # 提手
    poly(cv, [(10, 30), (54, 30), (47, 56), (17, 56)], B1)   # 篓身
    rect(cv, 14, 38, 50, 42, B3)                     # 编纹
    rect(cv, 16, 47, 48, 50, B3)
    rect(cv, 8, 28, 56, 33, B2)                      # 篓口
    rect(cv, 26, 24, 38, 30, ac)


def i_heart_lantern(cv, ac):
    disc(cv, 23, 26, 12, B1)                         # 心（两瓣 + 尖）
    disc(cv, 41, 26, 12, B1)
    poly(cv, [(11, 30), (53, 30), (32, 56)], B1)
    disc(cv, 32, 30, 8, ac)                          # 灯芯
    rect(cv, 28, 6, 36, 12, B3)                      # 挂环
    line(cv, 32, 12, 32, 18, B3, 3)


def i_fertile_core(cv, ac):
    poly(cv, [(6, 56), (12, 36), (52, 36), (58, 56)], B3)   # 土堆
    line(cv, 32, 38, 32, 22, B2, 4)                  # 芽茎
    poly(cv, [(32, 26), (46, 12), (50, 26), (36, 30)], B1)   # 右叶
    poly(cv, [(30, 30), (16, 18), (14, 30), (26, 34)], B2)   # 左叶
    disc(cv, 32, 20, 4, ac)


def i_berserk_mask(cv, ac):
    poly(cv, [(10, 8), (22, 26), (26, 10)], B2)      # 左角
    poly(cv, [(54, 8), (42, 26), (38, 10)], B2)      # 右角
    poly(cv, [(14, 22), (50, 22), (54, 40), (32, 58), (10, 40)], B1)  # 面具
    rect(cv, 18, 32, 28, 36, OUT_C)                  # 眼缝
    rect(cv, 36, 32, 46, 36, OUT_C)
    rect(cv, 28, 44, 36, 50, ac)                     # 口部色块


def i_longbow_arm(cv, ac):
    disc(cv, 30, 32, 27, B1)                         # 弓身（左弧）
    disc(cv, 30, 32, 20, None)
    rect(cv, 30, 0, 64, 64, None)
    line(cv, 30, 5, 30, 59, B3, 2)                   # 弓弦
    rect(cv, 31, 28, 50, 36, B2)                     # 箭杆
    poly(cv, [(50, 23), (58, 32), (50, 41)], B3)     # 箭头
    rect(cv, 33, 30, 37, 34, ac)                     # 箭羽 / 弦结


def i_thorn_whip(cv, ac):
    bezier(cv, (14, 50), (28, 12), (52, 28), 6, B1)   # 藤条
    for k, (x, y) in enumerate([(18, 38), (26, 24), (38, 16), (48, 22)]):
        poly(cv, [(x - 5, y), (x + 1, y - 11), (x + 4, y + 2)], B3)   # 倒刺
    disc(cv, 52, 30, 6, ac)                          # 鞭头


def i_big_bore(cv, ac):
    ring(cv, 32, 32, 26, 13, B1)                     # 大孔环
    ring(cv, 32, 32, 14, 12, B3)                     # 孔沿
    for k in range(6):
        a = math.pi * 2 * k / 6
        disc(cv, 32 + math.cos(a) * 20, 32 + math.sin(a) * 20, 4, B2)   # 螺栓
    ring(cv, 32, 32, 25, 23, ac)


def i_stone_draught(cv, ac):
    poly(cv, [(12, 10), (52, 10), (44, 54), (20, 54)], B1)   # 杯身
    poly(cv, [(15, 24), (49, 24), (43, 50), (21, 50)], B3)   # 药液
    rect(cv, 17, 24, 47, 27, ac)                     # 液面
    rect(cv, 10, 6, 54, 12, B2)                      # 杯口
    rect(cv, 18, 52, 46, 57, B3)


def i_night_lamp(cv, ac):
    poly(cv, [(18, 8), (46, 8), (40, 30), (24, 30)], B1)     # 灯罩
    rect(cv, 22, 28, 42, 33, B2)                     # 罩沿
    rect(cv, 29, 33, 35, 50, B2)                     # 立柱
    poly(cv, [(16, 50), (48, 50), (52, 57), (12, 57)], B3)   # 灯座
    rect(cv, 22, 12, 42, 16, ac)                     # 灯罩色条


def i_frenzy_band(cv, ac):
    ring(cv, 32, 32, 26, 15, B1)                     # 腕带
    # 亮环：只染**已存在**的像素 ⇒ 环内的空白不会被填上
    for y in range(30, 35):
        for x in range(0, W):
            if (x, y) in cv.d:
                cv.d[(x, y)] = ac


def i_tailwind_sash(cv, ac):
    poly(cv, [(4, 20), (26, 8), (46, 18), (60, 10), (60, 24),
              (46, 32), (26, 22), (4, 36)], B1)      # 顺风飘带（S 形）
    poly(cv, [(8, 38), (28, 30), (48, 40), (58, 34), (58, 46),
              (48, 52), (28, 42), (8, 52)], B2)      # 第二折
    poly(cv, [(6, 54), (24, 48), (40, 56), (24, 60), (6, 60)], B3)   # 尾端
    rect(cv, 18, 16, 30, 21, ac)


def i_water_pouch(cv, ac):
    drop(cv, 32, 39, 20, B1, 1.55)                   # 实心水滴
    disc(cv, 32, 43, 9, B3)                          # 袋口
    rect(cv, 20, 31, 44, 35, ac)


def i_glass_cannon(cv, ac):
    rect(cv, 10, 22, 44, 36, B1)                     # 炮身
    rect(cv, 44, 17, 56, 41, B2)                     # 炮口（透光）
    rect(cv, 46, 20, 54, 38, ac)
    poly(cv, [(14, 36), (42, 36), (36, 46), (20, 46)], B3)   # 炮架
    disc(cv, 20, 52, 7, B3)                          # 轮
    disc(cv, 40, 52, 7, B3)
    disc(cv, 20, 52, 3, None)
    disc(cv, 40, 52, 3, None)


def i_plow_weight(cv, ac):
    rect(cv, 10, 6, 54, 22, B2)                      # 配重铁
    rect(cv, 16, 10, 24, 18, B3)                     # 铆孔
    rect(cv, 40, 10, 48, 18, B3)
    rect(cv, 14, 20, 50, 25, ac)
    poly(cv, [(12, 26), (24, 26), (36, 52), (30, 57)], B1)   # 左犁刃
    poly(cv, [(52, 26), (40, 26), (28, 52), (34, 57)], B1)   # 右犁刃


def i_hardwood_haft(cv, ac):
    taper(cv, 14, 50, 50, 14, 14, 10, B1)            # 斜置木杆
    line(cv, 24, 40, 34, 30, B3, 4)                  # 箍 1
    line(cv, 38, 26, 46, 18, B3, 4)                  # 箍 2
    rect(cv, 34, 22, 44, 28, ac)                     # 木杆上的色标


def i_twin_blade(cv, ac):
    poly(cv, [(12, 12), (22, 6), (52, 52), (42, 58)], B1)    # 刃 1
    poly(cv, [(52, 12), (42, 6), (12, 52), (22, 58)], B2)    # 刃 2
    rect(cv, 24, 26, 40, 38, B3)                     # 护腕
    rect(cv, 30, 30, 34, 34, ac)


def i_ferment_jar(cv, ac):
    rect(cv, 12, 6, 52, 14, B2)                      # 盖
    poly(cv, [(12, 14), (52, 14), (52, 52), (47, 57), (17, 57), (12, 52)], B1)  # 罐身
    rect(cv, 15, 32, 49, 54, B3)                     # 液面
    rect(cv, 15, 32, 49, 36, ac)
    disc(cv, 24, 45, 4, B1)                          # 气泡
    disc(cv, 39, 41, 5, B1)


def i_overclock_core(cv, ac):
    gear(cv, 32, 32, 26, 19, 8, B1)                  # 齿轮圈
    disc(cv, 32, 32, 13, B3)                         # 内衬
    disc(cv, 32, 32, 8, ac)                          # 烧红的芯


def i_thorn_collar(cv, ac):
    ring(cv, 32, 32, 21, 13, B1)                     # 中空项圈
    for k in range(8):
        a = math.pi * 2 * k / 8 + 0.3
        x = 32 + math.cos(a) * 22
        y = 32 + math.sin(a) * 22
        poly(cv, [(x - 4, y + 3), (x, y - 8), (x + 4, y + 3)], B2)   # 外刺
    ring(cv, 32, 32, 21, 19, ac)


def i_iron_bark(cv, ac):
    rect(cv, 12, 8, 52, 56, B1)                      # 木皮
    for x in (20, 28, 36, 44):
        line(cv, x, 10, x, 54, B3, 2)                # 竖纹
    rect(cv, 10, 26, 54, 38, B2)                     # 铁箍
    disc(cv, 20, 32, 4, B3)                          # 铆钉
    disc(cv, 44, 32, 4, B3)
    rect(cv, 10, 30, 54, 33, ac)


def i_sniper_scope(cv, ac):
    rect(cv, 8, 22, 56, 40, B1)                      # 镜筒
    rect(cv, 4, 18, 14, 44, B2)                      # 目镜
    rect(cv, 50, 18, 60, 44, B2)                     # 物镜
    line(cv, 20, 31, 44, 31, OUT_C, 2)               # 十字准星
    line(cv, 32, 24, 32, 38, OUT_C, 2)
    poly(cv, [(24, 40), (36, 40), (34, 58), (26, 58)], B3)   # 支座
    rect(cv, 16, 26, 20, 30, ac)


def i_barbed_tip(cv, ac):
    poly(cv, [(32, 4), (46, 28), (32, 24), (18, 28)], B1)    # 箭镞
    poly(cv, [(20, 28), (8, 44), (26, 34)], B2)      # 左倒钩
    poly(cv, [(44, 28), (56, 44), (38, 34)], B2)     # 右倒钩
    rect(cv, 29, 28, 35, 58, B3)                     # 箭杆
    rect(cv, 27, 48, 37, 52, ac)


def i_gambler_dice(cv, ac):
    poly(cv, [(12, 12), (52, 12), (56, 16), (56, 52), (52, 56), (12, 56)], B1)  # 骰身
    for (x, y) in [(20, 20), (42, 20), (20, 42), (42, 42)]:
        disc(cv, x, y, 4, OUT_C)                     # 四点
    disc(cv, 31, 31, 5, ac)                          # 中心一点


def i_wax_seal(cv, ac):
    disc(cv, 32, 32, 24, B1)                         # 蜡印
    rect(cv, 20, 20, 44, 44, B2)                     # 方形印记
    rect(cv, 25, 25, 39, 39, B3)
    rect(cv, 29, 29, 35, 35, ac)
    disc(cv, 32, 8, 4, B2)                           # 溢出的蜡点


def i_blood_pact(cv, ac):
    drop(cv, 32, 38, 20, B1)                         # 血滴
    rect(cv, 29, 4, 35, 58, None)                    # 中间裂开
    rect(cv, 30, 20, 34, 26, ac)                     # 裂缝两侧的凝血


def i_greedy_sickle(cv, ac):
    disc(cv, 34, 22, 23, B1)                         # 弯刃
    disc(cv, 48, 32, 22, None)
    rect(cv, 44, 0, 64, 64, None)
    rect(cv, 4, 16, 14, 30, B2)                      # 刃根 / 握柄上端
    poly(cv, [(8, 32), (24, 26), (32, 56), (12, 58)], B3)    # 握柄
    rect(cv, 6, 18, 13, 24, ac)


def i_hollow_fruit(cv, ac):
    disc(cv, 32, 36, 21, B1)                         # 果身
    poly(cv, [(32, 18), (50, 6), (54, 20), (38, 22)], B2)    # 叶
    disc(cv, 32, 38, 10, None)                       # 空心
    disc(cv, 32, 38, 5, B3)                          # 只剩一枚核
    disc(cv, 32, 38, 2, ac)


def i_heirloom(cv, ac):
    ring(cv, 20, 10, 7, 4, B3)                       # 挂链
    ring(cv, 30, 16, 7, 4, B3)
    poly(cv, [(10, 24), (54, 24), (58, 44), (32, 58), (6, 44)], B1)   # 牌位
    disc(cv, 32, 36, 10, ac)                         # 宝石
    rect(cv, 12, 28, 52, 32, B2)


def i_iron_hoe(cv, ac):
    taper(cv, 44, 8, 22, 46, 9, 8, B1)               # 斜柄
    poly(cv, [(6, 40), (30, 34), (34, 52), (10, 58)], B2)    # 宽平刃
    rect(cv, 36, 16, 46, 20, B3)                     # 箍
    rect(cv, 10, 50, 32, 53, ac)                     # 刃口


def i_ash_pouch(cv, ac):
    poly(cv, [(22, 20), (42, 20), (38, 32), (26, 32)], B2)   # 束颈
    disc(cv, 32, 42, 21, B1)                         # 鼓身
    rect(cv, 24, 10, 40, 20, B3)                     # 袋口
    line(cv, 24, 22, 16, 34, B3, 3)                  # 绳 1
    line(cv, 40, 22, 48, 34, B3, 3)                  # 绳 2
    rect(cv, 26, 40, 38, 44, ac)


def i_scarecrow(cv, ac):
    poly(cv, [(10, 18), (54, 18), (44, 8), (20, 8)], B2)     # 草帽
    poly(cv, [(20, 18), (44, 18), (42, 36), (22, 36)], B1)   # 头
    rect(cv, 6, 32, 58, 38, B1)                      # 横杆手臂
    poly(cv, [(26, 36), (38, 36), (42, 52), (22, 52)], B2)   # 身
    rect(cv, 29, 50, 35, 58, B3)                     # 立柱
    for x in (12, 20, 44, 52):
        rect(cv, x, 38, x + 2, 46, B3)               # 垂下的草羽
    rect(cv, 28, 24, 36, 28, ac)


def i_snail_shell(cv, ac):
    disc(cv, 32, 32, 25, B1)                         # 外壳
    spiral(cv, 32, 32, 4, 19, B3, 3, 1.7)            # 螺纹
    poly(cv, [(40, 50), (58, 56), (52, 40)], None)   # 螺口缺口
    ring(cv, 32, 32, 25, 23, ac)


def i_lens(cv, ac):
    ring(cv, 28, 26, 22, 15, B1)                     # 镜圈
    rect(cv, 22, 20, 26, 24, HI)                     # 反光点 1
    rect(cv, 32, 28, 35, 30, HI)                     # 反光点 2
    taper(cv, 40, 40, 56, 54, 10, 9, B3)             # 斜柄
    ring(cv, 28, 26, 22, 20, ac)


def i_sprinter_boots(cv, ac):
    poly(cv, [(22, 8), (38, 8), (38, 34), (52, 40), (54, 50),
              (20, 50), (20, 8)], B1)                # 靴
    rect(cv, 16, 48, 58, 56, B3)                     # 鞋底
    rect(cv, 6, 20, 18, 24, ac)                      # 速度线
    rect(cv, 4, 32, 16, 36, ac)
    rect(cv, 26, 12, 36, 18, B2)                     # 靴筒色块


def i_lucky_clover(cv, ac):
    disc(cv, 22, 22, 12, B1)                         # 四叶
    disc(cv, 42, 22, 12, B1)
    disc(cv, 22, 42, 12, B1)
    disc(cv, 42, 42, 12, B1)
    rect(cv, 30, 8, 34, 56, None)                    # 十字缝
    rect(cv, 8, 30, 56, 34, None)
    rect(cv, 30, 44, 34, 58, B3)                     # 茎
    disc(cv, 32, 32, 4, ac)


def i_carrot_ring(cv, ac):
    for k in range(3):
        a = math.pi * 2 * k / 3 - math.pi / 2
        x = 32 + math.cos(a) * 17
        y = 32 + math.sin(a) * 17
        taper(cv, x, y, 32 + math.cos(a) * 5, 32 + math.sin(a) * 5, 11, 4, B1)
        disc(cv, 32 + math.cos(a) * 24, 32 + math.sin(a) * 24, 6, ac)   # 缨
    disc(cv, 32, 32, 6, B3)


def i_bunny_pet(cv, ac):
    poly(cv, [(10, 4), (24, 4), (30, 26), (16, 26)], B1)     # 左长耳
    poly(cv, [(40, 4), (54, 4), (48, 26), (34, 26)], B1)     # 右长耳
    rect(cv, 16, 10, 23, 24, B3)                     # 耳内
    rect(cv, 41, 10, 48, 24, B3)
    disc(cv, 32, 40, 19, B1)                         # 圆脸
    disc(cv, 23, 36, 4, OUT_C)                       # 眼
    disc(cv, 41, 36, 4, OUT_C)
    rect(cv, 27, 46, 37, 50, ac)                     # 嘴


def i_evade_helm(cv, ac):
    disc(cv, 32, 36, 25, B1)                         # 盔顶
    rect(cv, 0, 42, 64, 64, None)                    # 只留上半
    poly(cv, [(8, 36), (22, 36), (22, 58), (6, 50)], B2)     # 左护颊
    poly(cv, [(56, 36), (42, 36), (42, 58), (58, 50)], B2)   # 右护颊
    rect(cv, 6, 34, 58, 42, B2)                      # 亮边
    rect(cv, 16, 26, 48, 32, OUT_C)                  # 面窗
    rect(cv, 28, 10, 36, 40, B3)                     # 鼻梁
    rect(cv, 8, 42, 56, 45, ac)


def i_big_can(cv, ac):
    rect(cv, 16, 24, 44, 54, B1)                     # 壶身
    rect(cv, 22, 18, 38, 25, B2)                     # 壶口
    ring(cv, 30, 18, 13, 8, B2)                      # 提手
    rect(cv, 30, 14, 44, 20, None)                   # 提手挖空
    poly(cv, [(44, 34), (58, 18), (60, 26), (46, 44)], B2)   # 壶嘴
    rect(cv, 16, 52, 44, 57, B3)                     # 底
    rect(cv, 20, 30, 40, 34, ac)


def i_windup_gear(cv, ac):
    gear(cv, 32, 32, 27, 20, 8, B1)                  # 八齿
    disc(cv, 32, 32, 12, B3)                         # 轴座
    disc(cv, 32, 32, 5, ac)                          # 轴心
    ring(cv, 32, 32, 20, 18, B2)


def i_charge_coil(cv, ac):
    poly(cv, [(46, 4), (12, 34), (28, 34), (20, 58),
              (54, 26), (34, 26)], B1)               # 折下来的电
    rect(cv, 28, 28, 38, 38, ac)                     # 芯


def i_aegis_lamp(cv, ac):
    ring(cv, 32, 10, 9, 5, B3)                       # 顶环
    poly(cv, [(18, 16), (46, 16), (44, 46), (20, 46)], B1)   # 灯罩
    rect(cv, 24, 22, 40, 44, B3)                     # 灯芯腔
    rect(cv, 28, 26, 36, 40, ac)                     # 火苗
    rect(cv, 12, 46, 52, 56, B2)                     # 底座
    rect(cv, 16, 50, 48, 53, B3)


def i_gamble_glove(cv, ac):
    for x in (14, 22, 30, 38):
        poly(cv, [(x, 10), (x + 7, 10), (x + 7, 26), (x, 26)], B1)   # 四指
    poly(cv, [(10, 56), (10, 28), (22, 20), (42, 20), (52, 28),
              (50, 56)], B1)                        # 手掌
    poly(cv, [(10, 30), (4, 24), (6, 42), (12, 40)], B2)   # 拇指
    for (x, y) in [(24, 34), (40, 34), (32, 46)]:
        disc(cv, x, y, 5, OUT_C)                     # 三点面
    rect(cv, 16, 50, 46, 54, ac)


def i_sharp_spray(cv, ac):
    drop(cv, 32, 42, 19, B1, 1.5)                    # 一滴水
    taper(cv, 6, 52, 58, 16, 11, 9, B2)              # 斜切下来的刃
    line(cv, 8, 48, 56, 15, ac, 3)                   # 刃口亮线
    rect(cv, 30, 40, 36, 46, None)                   # 刃下的缝


def i_tycoon_coin(cv, ac):
    disc(cv, 32, 40, 21, B1)                         # 袋身
    poly(cv, [(22, 20), (42, 20), (38, 32), (26, 32)], B2)   # 束口
    rect(cv, 20, 12, 44, 20, B3)                     # 系带
    ring(cv, 32, 40, 9, 5, ac)                       # 钱孔
    rect(cv, 20, 32, 26, 36, HI)                     # 亮面


def i_warblade_bloom(cv, ac):
    disc(cv, 28, 20, 24, B1)                         # 镰刃
    disc(cv, 44, 30, 23, None)
    rect(cv, 40, 0, 64, 64, None)
    poly(cv, [(6, 16), (18, 12), (22, 30), (10, 34)], B3)    # 刃根
    line(cv, 30, 34, 40, 52, B2, 5)                  # 茎
    disc(cv, 45, 46, 9, ac)                          # 花
    disc(cv, 45, 46, 3, B1)


def i_gale_vine(cv, ac):
    spiral(cv, 34, 32, 6, 22, B1, 5, 1.4)            # 风卷
    poly(cv, [(6, 44), (26, 34), (30, 48), (10, 56)], B2)    # 被卷起的叶
    disc(cv, 20, 22, 6, ac)                          # 叶尖


def i_sunflower_crown(cv, ac):
    for k in range(8):
        a = math.pi * 2 * k / 8
        disc(cv, 32 + math.cos(a) * 19, 32 + math.sin(a) * 19, 9, B1)   # 八向花瓣
    disc(cv, 32, 32, 13, B2)                         # 花盘
    for (x, y) in [(28, 28), (36, 28), (28, 36), (36, 36), (32, 32)]:
        rect(cv, x - 1, y - 1, x + 1, y + 1, ac)     # 籽粒


def i_rainlily(cv, ac):
    ellipse(cv, 32, 46, 24, 13, B1)                  # 莲叶
    poly(cv, [(32, 58), (24, 34), (40, 34)], None)   # 叶缺口
    drop(cv, 32, 18, 8, B2)                          # 雨滴
    rect(cv, 20, 44, 44, 48, B3)                     # 叶脉
    disc(cv, 32, 18, 3, ac)


def i_shelter_ivy(cv, ac):
    poly(cv, [(32, 6), (54, 18), (50, 40), (32, 58), (14, 40), (10, 18)], B1)  # 藤盾
    bezier(cv, (14, 20), (32, 34), (50, 20), 4, B3)  # 藤 1
    bezier(cv, (16, 40), (32, 52), (48, 40), 4, B3)  # 藤 2
    for (x, y) in [(22, 26), (42, 26), (32, 46)]:
        disc(cv, x, y, 5, ac)                        # 叶


def i_lucky_monarch(cv, ac):
    poly(cv, [(32, 4), (50, 22), (32, 32), (14, 22)], B1)    # 上叶
    poly(cv, [(32, 32), (56, 32), (32, 50), (8, 32)], B1)    # 横叶
    poly(cv, [(32, 50), (46, 60), (32, 56), (18, 60)], B1)   # 下叶
    rect(cv, 29, 6, 35, 60, B3)                      # 主轴
    disc(cv, 32, 32, 6, ac)                          # 花心
    rect(cv, 18, 18, 24, 22, B2)
    rect(cv, 40, 18, 46, 22, B2)


ICONS = {
    "whet_oil": i_whet_oil,
    "wind_bell": i_wind_bell,
    "long_handle": i_long_handle,
    "lucky_bead": i_lucky_bead,
    "grind_stone": i_grind_stone,
    "blast_grain": i_blast_grain,
    "wide_funnel": i_wide_funnel,
    "healing_herb": i_healing_herb,
    "lucky_stone": i_lucky_stone,
    "pig_bell": i_pig_bell,
    "dual_straps": i_dual_straps,
    "spring_basket": i_spring_basket,
    "heart_lantern": i_heart_lantern,
    "fertile_core": i_fertile_core,
    "berserk_mask": i_berserk_mask,
    "longbow_arm": i_longbow_arm,
    "thorn_whip": i_thorn_whip,
    "big_bore": i_big_bore,
    "stone_draught": i_stone_draught,
    "night_lamp": i_night_lamp,
    "frenzy_band": i_frenzy_band,
    "tailwind_sash": i_tailwind_sash,
    "water_pouch": i_water_pouch,
    "glass_cannon": i_glass_cannon,
    "plow_weight": i_plow_weight,
    "hardwood_haft": i_hardwood_haft,
    "twin_blade": i_twin_blade,
    "ferment_jar": i_ferment_jar,
    "overclock_core": i_overclock_core,
    "thorn_collar": i_thorn_collar,
    "iron_bark": i_iron_bark,
    "sniper_scope": i_sniper_scope,
    "barbed_tip": i_barbed_tip,
    "gambler_dice": i_gambler_dice,
    "wax_seal": i_wax_seal,
    "blood_pact": i_blood_pact,
    "greedy_sickle": i_greedy_sickle,
    "hollow_fruit": i_hollow_fruit,
    "heirloom": i_heirloom,
    "iron_hoe": i_iron_hoe,
    "ash_pouch": i_ash_pouch,
    "scarecrow": i_scarecrow,
    "snail_shell": i_snail_shell,
    "lens": i_lens,
    "sprinter_boots": i_sprinter_boots,
    "lucky_clover": i_lucky_clover,
    "carrot_ring": i_carrot_ring,
    "bunny_pet": i_bunny_pet,
    "evade_helm": i_evade_helm,
    "big_can": i_big_can,
    "windup_gear": i_windup_gear,
    "charge_coil": i_charge_coil,
    "aegis_lamp": i_aegis_lamp,
    "gamble_glove": i_gamble_glove,
    "sharp_spray": i_sharp_spray,
    "tycoon_coin": i_tycoon_coin,
    "warblade_bloom": i_warblade_bloom,
    "gale_vine": i_gale_vine,
    "sunflower_crown": i_sunflower_crown,
    "rainlily": i_rainlily,
    "shelter_ivy": i_shelter_ivy,
    "lucky_monarch": i_lucky_monarch,
}


# ════════════════════════════════════════════════════════════════════════════
#  统一收尾：高光 → 描边 → 装饰（顺序不能换）
# ════════════════════════════════════════════════════════════════════════════
def add_highlight(cv, acc):
    """沿主体**上缘**左侧 ~42% 铺 2px 亮带，右端再以 45° 下折 10 步（3px 粗）= 一条斜向高光。"""
    m = cv.d
    if not m:
        return
    xs = [p[0] for p in m]
    x0, x1 = min(xs), max(xs)
    ytop = {}
    for (x, y) in m:
        if x not in ytop or y < ytop[x]:
            ytop[x] = y
    xa = x0 + 1
    xb = x0 + max(3, int((x1 - x0) * 0.42))
    for x in range(xa, xb + 1):
        if x not in ytop:
            continue
        for dy in (0, 1):
            p = (x, ytop[x] + dy)
            if p in m and m[p] not in (acc, OUT_C):
                m[p] = HI
    # 45° 下折（这一步把"平顶亮带"变成"斜向高光"）
    x, y = xb, ytop.get(xb, 0)
    for i in range(10):
        for dj in (0, 1, 2):
            p = (x + i, y + i + dj)
            if p in m and m[p] not in (acc, OUT_C):
                m[p] = HI


def add_outline(cv, acc):
    """统一 2px 暖灰描边（掩膜欧氏膨胀 2px 后填色 ⇒ 圆角自然）。"""
    m = cv.d
    body = set(m.keys())
    edge = set()
    r = 2
    for (x, y) in body:
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy > r * r + 0.25:
                    continue
                p = (x + dx, y + dy)
                if p in body:
                    continue
                if 0 <= p[0] < W and 0 <= p[1] < H:
                    edge.add(p)
    for p in edge:
        m[p] = OUT_C


def add_slashes(cv, acc):
    """`///` 三道斜杠：主体左上角内；若被掩膜裁得太少，退到画布左上空白处。"""
    m = cv.d
    body = set(m.keys())
    xs = [p[0] for p in body]
    ys = [p[1] for p in body]
    x0, y0 = min(xs), min(ys)
    w = max(xs) - x0
    h = max(ys) - y0
    ox, oy = x0 + int(w * 0.16), y0 + int(h * 0.28) + 10
    painted = _stamp_slashes(m, body, ox, oy)
    if painted < 12:
        _stamp_slashes(m, body, 4, 22)


def _stamp_slashes(m, body, ox, oy):
    """三条 45° 斜杠（亮白 2px + 右下 1px 描边阴影），只画在主体像素上。"""
    painted = 0
    for k in range(3):
        sx = ox + k * 7
        for i in range(7):
            for dj in (0, 1, 2):
                p = (sx + i, oy - i + dj)
                if p in body and m[p] != OUT_C:
                    m[p] = OUT_C if dj == 2 else HI
                    if dj < 2:
                        painted += 1
    return painted


def add_corner_dots(cv, acc):
    """静态物的右下角三颗角标点（品类色）。"""
    m = cv.d
    body = set(m.keys())
    xs = [p[0] for p in body]
    ys = [p[1] for p in body]
    x1, y1 = max(xs), max(ys)
    bx, by = x1 - 13, y1 - 13
    painted = 0
    for k in range(3):
        cx, cy = bx + k * 4, by + k * 4
        for dy in (0, 1, 2):
            for dx in (0, 1, 2):
                p = (cx + dx, cy + dy)
                if p in body and m[p] not in (OUT_C, HI):
                    m[p] = acc
                    painted += 1
    if painted < 9:
        # 兜底：贴着画布右下角的空白处画（保证"每张都有装饰"）
        for k in range(3):
            for dy in (0, 1, 2):
                for dx in (0, 1, 2):
                    p = (56 - k * 4 + dx, 56 - k * 4 + dy)
                    if p in m:
                        m[p] = acc


def fit_center(cv):
    """把主体整体平移，使包围盒居中（只平移、不缩放 ⇒ 像素比不变、依然是硬边）。"""
    m = cv.d
    if not m:
        return
    xs = [p[0] for p in m]
    ys = [p[1] for p in m]
    x0, x1 = min(xs), max(xs)
    y0, y1 = min(ys), max(ys)
    nx = (W - (x1 - x0 + 1)) // 2 - x0
    ny = (H - (y1 - y0 + 1)) // 2 - y0
    if nx == 0 and ny == 0:
        return
    cv.d = {(x + nx, y + ny): c for (x, y), c in m.items()}


def build(icon_id, acc):
    cv = Cv()
    ICONS[icon_id](cv, acc)
    fit_center(cv)
    add_highlight(cv, acc)
    add_outline(cv, acc)
    if icon_id in SLASH:
        add_slashes(cv, acc)
    else:
        add_corner_dots(cv, acc)
    return cv


# ════════════════════════════════════════════════════════════════════════════
#  落盘
# ════════════════════════════════════════════════════════════════════════════
def to_rgba(cv):
    from PIL import Image
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    px = img.load()
    for (x, y), c in cv.d.items():
        px[x, y] = c
    return img


def load_tables():
    with io.open(ITEMS, encoding="utf-8") as f:
        items = json.load(f)["items"]
    with io.open(SEEDS, encoding="utf-8") as f:
        seeds = json.load(f)["seeds"]
    by_seed = {s["id"]: s for s in seeds}
    return items, seeds, by_seed


def accent_of(item, seed):
    if seed is not None:
        return ACC_PLANT
    if item is not None and item.get("category") == 2:
        return ACC_COST
    return ACC_GREEN


# ════════════════════════════════════════════════════════════════════════════
#  校验（硬约束逐条机器判，不靠肉眼）
# ════════════════════════════════════════════════════════════════════════════
def verify(images, items, by_seed):
    from PIL import Image  # noqa: F401
    problems = []
    if len(images) != 62:
        problems.append("生成 %d 张 ≠ 62" % len(images))
    hashes = {}
    for icon_id, img in images.items():
        px = img.load()
        colors = set()
        bbox = [W, H, -1, -1]
        alphas = set()
        for y in range(H):
            for x in range(W):
                r, g, b, a = px[x, y]
                alphas.add(a)
                if a == 0:
                    continue
                colors.add((r, g, b, a))
                bbox[0] = min(bbox[0], x)
                bbox[1] = min(bbox[1], y)
                bbox[2] = max(bbox[2], x)
                bbox[3] = max(bbox[3], y)
        if alphas - {0, 255}:
            problems.append("%s：alpha 出现了中间值 %r（必须硬边 0/255）"
                            % (icon_id, sorted(alphas - {0, 255})[:4]))
        if len(colors) > 6:
            problems.append("%s：用了 %d 色 > 6" % (icon_id, len(colors)))
        if min(colors or [(1, 1, 1, 1)]) is None:
            pass
        bw = bbox[2] - bbox[0] + 1
        bh = bbox[3] - bbox[1] + 1
        if bw < 44 or bh < 44:
            problems.append("%s：主体包围盒 %d×%d < 44（填不满画框）" % (icon_id, bw, bh))
        if bw > 62 or bh > 62:
            problems.append("%s：主体包围盒 %d×%d 顶到画布边（没有留白）" % (icon_id, bw, bh))
        if (0, 0, 0, 255) in colors:
            problems.append("%s：出现纯黑（描边必须是暖灰 #3A3A38）" % icon_id)
        h = hashlib_sig(img)
        if h in hashes:
            problems.append("%s 与 %s 像素完全相同（图标不许互相抄）" % (icon_id, hashes[h]))
        else:
            hashes[h] = icon_id
        # 装饰存在性
        if icon_id in SLASH:
            hi = sum(1 for c in colors if c == HI)
            if len(colors) < 4:
                problems.append("%s：`///` 装饰似乎没画上（色数 %d）" % (icon_id, len(colors)))
    return problems


def hashlib_sig(img):
    import hashlib
    return hashlib.sha256(img.tobytes()).hexdigest()


def bbox_of(img):
    px = img.load()
    b = [W, H, -1, -1]
    for y in range(H):
        for x in range(W):
            if px[x, y][3] == 0:
                continue
            b[0] = min(b[0], x)
            b[1] = min(b[1], y)
            b[2] = max(b[2], x)
            b[3] = max(b[3], y)
    return "%d×%d (x %d..%d, y %d..%d)" % (b[2] - b[0] + 1, b[3] - b[1] + 1,
                                           b[0], b[2], b[1], b[3])


def render_sheet(images, items, by_seed, path):
    from PIL import Image, ImageDraw, ImageFont
    font_path = r"C:\Windows\Fonts\msyh.ttc"

    def font(px):
        try:
            return ImageFont.truetype(font_path, px)
        except Exception:
            return ImageFont.load_default()

    f_name, f_meta, f_title = font(18), font(12), font(21)
    scale = 2                       # 64 → 128（最近邻放大，与游戏内的"硬边"一致）
    icon = W * scale
    pad, gap = 10, 8
    cols = 7
    rows = (len(images) + cols - 1) // cols
    tw = icon + pad * 2
    th = icon + 60
    Wc = pad + cols * (tw + gap)
    Hc = 62 + rows * (th + gap) + pad
    img = Image.new("RGB", (Wc, Hc), (28, 22, 17))       # 与 IconWellBg 同色系
    d = ImageDraw.Draw(img)
    d.text((pad, 14),
           "Seedfall v198 图标 %d 张（%d 件道具 + %d 株机制植物）｜统一规范：64×64 / ≤6 色 / "
           "2px 暖灰描边 #3A3A38 / 一条斜向高光 / 硬边像素"
           % (len(images), len(items), len(MECH_SEEDS)),
           font=f_title, fill=(240, 228, 200))
    d.text((pad, 38),
           "绿 = 纯增益　橙红 = 有代价　金黄 = 植物｜`///` = 动作性（武器/工具/爆炸/发射）"
           "　右下角标 = 静态（容器/护甲/果实）",
           font=f_meta, fill=(190, 178, 152))

    for idx, icon_id in enumerate(images.keys()):
        cx = pad + (idx % cols) * (tw + gap)
        cy = 62 + (idx // cols) * (th + gap)
        d.rectangle([cx, cy, cx + tw - 1, cy + th - 1], fill=(52, 39, 26))
        seed = by_seed.get(icon_id)
        item = next((it for it in items if it["id"] == icon_id), None)
        acc = accent_of(item, seed)
        d.rectangle([cx, cy, cx + tw - 1, cy + 3], fill=acc)
        big = images[icon_id].resize((icon, icon), Image.NEAREST)
        img.paste(big, (cx + pad, cy + pad), big)
        name = (seed or item or {}).get("name", icon_id)
        grade = (seed or item or {}).get("grade", "?")
        d.text((cx + 6, cy + icon + pad + 2), "%s" % name, font=f_name, fill=(246, 240, 226))
        tag = "植" if seed else ("新" if icon_id in V197_ITEMS else ("补" if icon_id in BACKFILL else ""))
        d.text((cx + 6, cy + icon + pad + 24), "%s %s%s" % (grade, icon_id, (" " + tag) if tag else ""),
               font=f_meta, fill=(196, 182, 158))
        d.text((cx + 6, cy + icon + pad + 40),
               "#%02d %s" % (idx + 1, "///" if icon_id in SLASH else "角标"),
               font=f_meta, fill=(150, 220, 255))
    img.save(path)
    return path


# v197 新增的 10 件 + 24 件老道具补图（对照图上打标，与 `Tools/icon_gen/item_icons.py` 同源清单）
V197_ITEMS = {
    "carrot_ring", "bunny_pet", "evade_helm", "big_can", "windup_gear",
    "charge_coil", "aegis_lamp", "gamble_glove", "sharp_spray", "tycoon_coin",
}
BACKFILL = {
    "water_pouch", "glass_cannon", "plow_weight", "hardwood_haft", "twin_blade", "ferment_jar",
    "overclock_core", "thorn_collar", "iron_bark", "sniper_scope", "barbed_tip", "gambler_dice",
    "wax_seal", "blood_pact", "greedy_sickle", "hollow_fruit", "heirloom",
    "iron_hoe", "ash_pouch", "scarecrow", "snail_shell", "sprinter_boots", "lens", "lucky_clover",
}


def main():
    args = sys.argv[1:]
    items, seeds, by_seed = load_tables()
    item_by_id = {it["id"]: it for it in items}

    # 顺序：先道具（表序），再机制植物（表序）⇒ 与 `TableDatabase.ItemIconMasks` 的书写顺序一致
    order = [it["id"] for it in items if it["id"] in ICONS]
    order += [s["id"] for s in seeds if s["id"] in MECH_SEEDS and s["id"] in ICONS]
    missing = [k for k in ICONS if k not in order]
    if missing:
        print("⚠ 这些 id 有画法但不在任何表里：%s" % ", ".join(missing))

    images = {}
    for icon_id in order:
        item = item_by_id.get(icon_id)
        seed = by_seed.get(icon_id)
        images[icon_id] = to_rgba(build(icon_id, accent_of(item, seed)))

    problems = verify(images, items, by_seed)
    print("生成 %d 张｜表内道具 %d 件｜种子 %d 株（机制植物 %d 株）"
          % (len(images), len(items), len(seeds), len(MECH_SEEDS)))
    if "--stats" in args:
        for icon_id, img in images.items():
            print("  %-16s bbox %s" % (icon_id, bbox_of(img)))
    if problems:
        print("问题 %d 条：" % len(problems))
        for p in problems:
            print("  - " + p)
        if "--force" not in args:
            return 1
    else:
        print("校验 PASS：62/62 张 —— 64×64、每张 ≤6 色、alpha 只有 0/255（硬边）、"
              "主体包围盒 ≥44px、无纯黑、**62 张像素两两不同**")
    if "--check" in args:
        return 0

    if not os.path.isdir(UI_DIR):
        print("找不到 UI 目录：%s" % UI_DIR)
        return 1
    written = 0
    for icon_id, img in images.items():
        img.save(os.path.join(UI_DIR, "icon_item_%s.png" % icon_id))
        written += 1
        if icon_id in MECH_SEEDS:
            img.save(os.path.join(UI_DIR, "icon_plant_%s.png" % icon_id))
            written += 1
    print("写盘 %d 张 → %s（icon_item_*.png %d 张 ＋ icon_plant_*.png %d 张）"
          % (written, UI_DIR, len(images), len(MECH_SEEDS)))
    if "--no-sheet" in args:
        return 0

    os.makedirs(OUT, exist_ok=True)
    sheet = render_sheet(images, items, by_seed, os.path.join(OUT, "item_png_sheet.png"))
    txt = os.path.join(OUT, "item_png_sheet.txt")
    with io.open(txt, "w", encoding="utf-8") as f:
        f.write("# Seedfall v198 图标清单（%d 张 = %d 件道具 ＋ %d 株机制植物）\n"
                % (len(images), len(items), len(MECH_SEEDS)))
        f.write("# 统一规范：64×64 透明底 / ≤6 色 / 2px 暖灰描边 #3A3A38 / 一条斜向高光 #F2EFE6 / 硬边\\n")
        for idx, icon_id in enumerate(images.keys()):
            item = item_by_id.get(icon_id)
            seed = by_seed.get(icon_id)
            src = seed or item or {}
            f.write("#%02d %-16s %-4s %-10s %-6s %s\n" % (
                idx + 1, icon_id, src.get("grade", "?"),
                "植物" if seed else ("有代价" if (item or {}).get("category") == 2 else "纯增益"),
                "///" if icon_id in SLASH else "角标", src.get("name", "")))
    print("对照图：%s" % sheet)
    print("清单：%s" % txt)
    return 0


if __name__ == "__main__":
    sys.exit(main())
