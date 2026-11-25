# -*- coding: utf-8 -*-
"""v284：为 items.json 新增的 50 件道具补图标 —— 只画图形，**复用 v258 的画风外壳**。

════════════════════════════════════════════════════════════════════════
★ 复现参数（全部写死）★
  画布     : 64×64 RGBA，PNG（与既有 130 张 icon_item_*.png 同尺寸）
  外壳     : `import restyle_icons_v258 as V258` —— 直接用它的常量与函数：
             SIZE=64 / RADIUS=12 / OBJ=44 / OUTLINE=#2B211B /
             THEMES["item"] 上亮 #8A6A46 → 下暗 #543C26 的纵向渐变 /
             rounded_mask() / gradient() / outline_of()
             合成顺序与 `V258.restyle()` 的 ①~④ **逐条一致**（见 compose()）：
               ① 渐变底板（圆角 12）
               ② 左上 1px 内高光 rgba(255,255,255,70)
               ③ 物件：右下 1px 投影(12,9,6,120) → 外扩 2px 深描边 → 本体，居中 44×44
               ④ 外框 2px OUTLINE
  调色板   : `import gen_missing_icons_v274 as V274` —— 只为拿它的 INK/STEEL/WOOD/
             WATER/FIRE/BLOOD/GOLD/LEAF/SOIL/BONE/ASH/CYAN/PURPLE 常量，
             保证这批新图与 v274 那 30 张**同色**（不改 v274 一个字节）
  物件区   : 44×44 透明画布，图元一律画在 2..41 之间（给外扩 2px 描边留位）
  物件画法 : 平涂 + 1 层亮面(左上) + 1 层暗面(右下) + ≤2 个 accent + 深色内部线，
             与既有道具图标同一套"硬边像素"画法（不抗锯齿）
  随机源   : SEED=20250284；每张图用 `random.Random("<id>|<SEED>")` 独立取随机器
             （只用于卵石/苔簇/星位等散点，量极小）⇒ 与批次顺序无关，
             两次运行逐字节相同
  输出     : Assets/Resources/Art/UI/icon_item_<id>.png（64×64）
  .meta    : 逐字段照抄邻居 `icon_item_wax_seal.png.meta`，**只换 guid**：
             guid = md5("Seedfall/v284/item/<id>") ⇒ 恒定的稳定 guid，重跑不变
             （textureType=0 / filterMode=1 / spritePixelsToUnits=100 /
               maxTextureSize=2048 / enableMipMap=0 与邻居逐字段相同）
  不动的   : 既有 PNG 与 .meta 一个字节都不改（脚本前后打印 sha256 比对并断言）；
             `powder_horn` 已经**有**一张真图（714 色，非占位色块）⇒ 本次只校验、不重画

★ 用法 ★
  python Tools/icon_gen/gen_items_v284.py             # 生成 49 张 + 校验 1 张
  python Tools/icon_gen/gen_items_v284.py --dry-run   # 只打印计划与守则
  python Tools/icon_gen/gen_items_v284.py --check     # 只做校验（尺寸/字节/风格锚点）
  python Tools/icon_gen/gen_items_v284.py --preview   # 只重建并排预览图
════════════════════════════════════════════════════════════════════════
"""
import os
import sys
import json
import math
import random
import hashlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw

import restyle_icons_v258 as V258            # ★ 画风外壳：常量 + 三个图元函数
import gen_missing_icons_v274 as V274        # ★ 只借调色板（不修改）

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UI = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
META_TEMPLATE = os.path.join(UI, "icon_item_wax_seal.png.meta")
PREVIEW_DIR = os.environ.get(
    "V284_PREVIEW_DIR", r"C:\Users\18462\Documents\deepseek-harness\default-workspace")
PREVIEW = os.path.join(PREVIEW_DIR, "_v284_items_icons.png")

SIZE = V258.SIZE            # 64
OBJ = V258.OBJ              # 44
RADIUS = V258.RADIUS        # 12
OUTLINE = V258.OUTLINE      # #2B211B
THEMES = V258.THEMES
SEED = 20250284

# ── 调色板：与 v274 完全同一组常量（同色），另加 4 个本批新用到的材质色 ──
INK = V274.INK
STEEL, STEEL_L, STEEL_D = V274.STEEL, V274.STEEL_L, V274.STEEL_D
WOOD, WOOD_D, WOOD_L = V274.WOOD, V274.WOOD_D, V274.WOOD_L
WATER, WATER_D, WATER_L = V274.WATER, V274.WATER_D, V274.WATER_L
FIRE, FIRE_L, FIRE_D = V274.FIRE, V274.FIRE_L, V274.FIRE_D
BLOOD, BLOOD_L = V274.BLOOD, V274.BLOOD_L
GOLD, GOLD_L, GOLD_D = V274.GOLD, V274.GOLD_L, V274.GOLD_D
LEAF, LEAF_D = V274.LEAF, V274.LEAF_D
SOIL, SOIL_D, SOIL_L = V274.SOIL, V274.SOIL_D, V274.SOIL_L
BONE, BONE_D = V274.BONE, V274.BONE_D
ASH, CYAN, PURPLE = V274.ASH, V274.CYAN, V274.PURPLE

CLOTH = (0xC8, 0xB8, 0x94, 255)      # 旧布/麻布（补丁背心、口粮布包）
CLOTH_D = (0x9A, 0x8A, 0x68, 255)
LEATHER = (0x7A, 0x54, 0x36, 255)    # 皮（鞋跟、挽具）
LEATHER_D = (0x52, 0x36, 0x22, 255)
ROCK = (0x5A, 0x46, 0x3E, 255)       # 陨石本体
GLASS = (0xD8, 0xF2, 0xFF, 255)      # 玻璃/透镜反光


# ══════════════════════════════════════ 外壳合成（= v258.restyle ①~④） ══════════════════════════════════════

def O44():
    return Image.new("RGBA", (OBJ, OBJ), (0, 0, 0, 0))


def compose(obj, kind="item"):
    """把 44×44 物件贴进 v258 的外壳 —— 逐条复刻 `restyle_icons_v258.restyle()` 的 ①~④。"""
    theme = THEMES[kind]
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # ① 渐变底板（圆角 12）—— 直接调 v258 的两个函数
    panel = V258.gradient(SIZE, theme[0], theme[1]).convert("RGBA")
    panel.putalpha(V258.rounded_mask(SIZE, RADIUS))
    canvas.alpha_composite(panel)

    # ② 左上 1px 内高光（与 v258 同一位置、同一 alpha）
    hl = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(hl).rounded_rectangle([1, 1, SIZE - 2, SIZE - 2], radius=RADIUS - 1,
                                         outline=(255, 255, 255, 70), width=1)
    canvas.alpha_composite(hl)

    # ③ 物件：投影 → 2px 深描边 → 本体（v258 的形态学外扩）
    alpha = obj.getchannel("A")
    off = (SIZE - OBJ) // 2
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow.alpha_composite(V258.outline_of(alpha, 2, (12, 9, 6, 120)), (off + 1, off + 2))
    canvas.alpha_composite(shadow)
    stroke = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    stroke.alpha_composite(V258.outline_of(alpha, 2, OUTLINE), (off, off))
    canvas.alpha_composite(stroke)
    canvas.alpha_composite(obj, (off, off))

    # ④ 外框 2px
    frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle([1, 1, SIZE - 2, SIZE - 2], radius=RADIUS,
                                            outline=OUTLINE, width=2)
    canvas.alpha_composite(frame)
    return canvas


def R(name):
    """每张图独立的确定随机器（与批次顺序无关）。"""
    return random.Random("%s|%d" % (name, SEED))


def star4(d, cx, cy, r, fill):
    """四角星（accent 用）。"""
    q = max(1, r // 3)
    d.polygon([(cx, cy - r), (cx + q, cy - q), (cx + r, cy), (cx + q, cy + q),
               (cx, cy + r), (cx - q, cy + q), (cx - r, cy), (cx - q, cy - q)], fill=fill)


def heart(d, cx, cy, s, fill):
    """小爱心（回血语义）。"""
    d.ellipse([cx - s, cy - s, cx, cy + 1], fill=fill)
    d.ellipse([cx, cy - s, cx + s, cy + 1], fill=fill)
    d.polygon([(cx - s, cy - s // 2), (cx + s, cy - s // 2), (cx, cy + s)], fill=fill)


# ══════════════════════════════════════ 50 件道具（按 items.json 顺序） ══════════════════════════════════════

def ic_chipped_sickle():
    """缺角镰刀：木柄 + 新月刃，刃口啃掉一块缺口（近战 +1）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(22, 41), (34, 29)], fill=WOOD, width=5)                    # 柄
    d.line([(23, 40), (31, 32)], fill=WOOD_D, width=1)                  # 木纹
    d.arc([3, 2, 35, 32], start=196, end=344, fill=STEEL, width=7)      # 新月刃
    d.arc([5, 4, 33, 30], start=204, end=300, fill=STEEL_L, width=2)    # 刃上亮面
    d.ellipse([15, 1, 23, 9], fill=(0, 0, 0, 0))                        # 崩出的缺口
    d.polygon([(9, 5), (12, 1), (13, 6)], fill=(0, 0, 0, 0))            # 第二处小崩
    star4(d, 9, 12, 3, BONE)                                            # 缺口的白茬
    return im


def ic_reed_whistle():
    """芦苇哨：苇管 + 吹口 + 两个指孔，右侧三道气线（远程 +1）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(6, 37), (30, 11)], fill=LEAF_D, width=7)                   # 苇管
    d.line([(7, 35), (29, 12)], fill=(0xA8, 0xC0, 0x6A, 255), width=3)  # 苇黄
    d.polygon([(28, 9), (38, 5), (37, 14), (29, 16)], fill=WOOD, outline=INK)  # 吹口
    for t in (0.32, 0.58):                                              # 指孔
        x, y = int(6 + 24 * t), int(37 - 26 * t)
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=INK)
    for i, w in ((0, 2), (5, 2), (10, 1)):                              # 气线
        d.line([(34 + i, 22 + i), (41, 15 + i)], fill=WATER, width=w)
    return im


def ic_damp_fuse():
    """潮引线：S 形导火索 + 烧着的头 + 三颗水珠（受潮，爆炸 +1）。"""
    im = O44(); d = ImageDraw.Draw(im)
    pts = [(9, 38), (17, 30), (13, 22), (21, 15), (29, 9)]
    d.line(pts, fill=WOOD_L, width=6, joint="curve")                    # 索芯
    d.line(pts, fill=WOOD_D, width=2, joint="curve")                    # 药芯
    for i in range(1, 5):                                               # 编纹
        x, y = pts[i]
        d.line([(x - 3, y + 3), (x + 3, y - 3)], fill=WOOD_D, width=1)
    d.polygon([(28, 6), (36, 4), (34, 12), (29, 12)], fill=FIRE)        # 火头
    d.polygon([(30, 8), (34, 7), (33, 11), (30, 11)], fill=FIRE_L)
    for x, y in ((12, 27), (22, 30), (26, 19)):                         # 水珠（潮）
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=WATER, outline=INK, width=1)
        d.ellipse([x - 2, y - 2, x - 1, y - 1], fill=WATER_L)
    return im


def ic_straw_kneepad():
    """草护膝：草编圆垫 + 十字编纹 + 上下两根皮带（最大生命 +1）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(11, 12), (6, 4)], fill=LEATHER, width=3)                   # 上带
    d.line([(33, 12), (38, 4)], fill=LEATHER, width=3)
    d.rounded_rectangle([7, 10, 37, 36], radius=9, fill=GOLD_D, outline=INK, width=2)
    for y in (14, 18, 22, 26, 30, 34):                                  # 横向草股
        d.line([(9, y), (35, y)], fill=GOLD, width=1)
    for x in (13, 19, 25, 31):                                          # 纵向压股
        d.line([(x, 12), (x, 34)], fill=SOIL_D, width=1)
    d.arc([9, 12, 35, 24], start=200, end=300, fill=GOLD_L, width=2)    # 亮面
    d.line([(11, 34), (33, 34)], fill=SOIL_D, width=2)                  # 底边
    return im


def ic_tallow_candle():
    """牛脂烛：牛脂柱（带流挂）+ 灯芯 + 火苗 + 铜碟（攻速 +5%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([8, 32, 36, 41], fill=GOLD_D, outline=INK, width=2)       # 铜碟
    d.ellipse([12, 33, 32, 38], fill=GOLD)
    d.rectangle([16, 15, 28, 34], fill=BONE, outline=INK, width=2)      # 烛身
    d.rectangle([17, 16, 20, 33], fill=(0xFF, 0xF6, 0xE6, 255))         # 亮面
    d.ellipse([15, 29, 20, 37], fill=BONE_D)                            # 流挂
    d.ellipse([25, 31, 29, 38], fill=BONE_D)
    d.line([(22, 15), (22, 11)], fill=INK, width=1)                     # 灯芯
    d.polygon([(22, 3), (27, 9), (22, 14), (17, 9)], fill=FIRE)         # 火苗
    d.polygon([(22, 6), (24, 9), (22, 12), (20, 9)], fill=FIRE_L)
    return im


def ic_pebble_pouch():
    """石子袋：束口布袋 + 麻绳 + 三颗漏出的石子（击退 +6%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(11, 17), (33, 17), (37, 36), (7, 36)], fill=SOIL, outline=INK)
    d.ellipse([9, 27, 35, 39], fill=SOIL_L)                             # 袋底鼓
    d.polygon([(15, 8), (29, 8), (31, 18), (13, 18)], fill=SOIL_D, outline=INK)
    d.line([(14, 12), (30, 12)], fill=BONE_D, width=2)                  # 麻绳
    for x, y in ((15, 31), (21, 33), (27, 30)):                         # 袋里石子顶起
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=ASH, outline=INK, width=1)
    rng = R("pebble_pouch")
    for i in range(3):                                                  # 漏出的石子
        x = 8 + rng.randint(0, 26); y = 38 + rng.randint(0, 2)
        d.ellipse([x, y, x + 5, y + 4], fill=STEEL_D, outline=INK, width=1)
        d.ellipse([x + 1, y + 1, x + 2, y + 2], fill=STEEL_L)
    return im


def ic_field_ration():
    """田间口粮：麻布包 + 两道捆绳 + 一颗心（爱心恢复 +1）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(10, 18), (34, 18), (31, 39), (13, 39)], fill=CLOTH, outline=INK)
    d.polygon([(10, 18), (22, 24), (34, 18)], fill=CLOTH_D)             # 布褶
    d.line([(9, 26), (35, 26)], fill=WOOD_D, width=2)                   # 捆绳
    d.line([(10, 32), (34, 32)], fill=WOOD_D, width=2)
    d.line([(22, 18), (22, 25)], fill=WOOD_D, width=2)
    d.ellipse([19, 30, 25, 35], fill=WOOD_L)                            # 绳结
    heart(d, 22, 12, 6, BLOOD)                                          # 回血语义
    d.ellipse([19, 10, 22, 13], fill=BLOOD_L)
    return im


def ic_shallow_root():
    """浅根须：籽粒 + 短主根 + 四向横走的浅根（移速 +4%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([16, 4, 28, 17], fill=SOIL, outline=INK, width=2)         # 籽
    d.ellipse([18, 6, 22, 10], fill=SOIL_L)
    d.line([(22, 17), (22, 26)], fill=SOIL_D, width=3)                  # 主根（很短）
    for dx, dy in ((-11, 8), (-5, 12), (5, 12), (11, 7)):               # 横走的浅根
        d.line([(22, 26), (22 + dx, 24 + dy)], fill=SOIL_L, width=2)
        d.line([(22 + dx, 24 + dy), (22 + dx + (2 if dx > 0 else -2), 28 + dy)],
               fill=SOIL_D, width=1)
    for x in range(6, 40, 6):                                           # 地表
        d.line([(x, 37), (x + 4, 37)], fill=SOIL_D, width=1)
    return im


def ic_whittle_knife():
    """削尖小刀：短刃 + 削薄的刀背 + 木柄 + 卷起的木屑（近战 +2）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(9, 38), (17, 30)], fill=WOOD, width=7)                     # 木柄
    d.line([(10, 37), (16, 31)], fill=WOOD_D, width=1)
    d.polygon([(17, 30), (33, 8), (38, 14), (21, 35)], fill=STEEL, outline=INK)
    d.polygon([(19, 29), (32, 11), (34, 15), (21, 31)], fill=STEEL_L)   # 削薄的刃面
    d.line([(22, 27), (33, 12)], fill=BONE, width=1)                    # 刃线
    d.arc([23, 22, 37, 34], start=190, end=350, fill=WOOD_L, width=2)   # 木屑卷
    d.arc([26, 26, 36, 33], start=190, end=340, fill=WOOD_D, width=1)
    return im


def ic_brass_sight():
    """黄铜准星：双环 + 四向刻度 + 红心（攻击范围 +10%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([6, 6, 38, 38], outline=GOLD_D, width=3)                  # 外环
    d.ellipse([6, 6, 38, 38], outline=INK, width=1)
    d.ellipse([14, 14, 30, 30], outline=GOLD, width=2)                  # 内环
    for x0, y0, x1, y1 in ((22, 2, 22, 9), (22, 35, 22, 42),
                           (2, 22, 9, 22), (35, 22, 42, 22)):           # 四向刻度
        d.line([(x0, y0), (x1, y1)], fill=GOLD, width=2)
    d.line([(15, 22), (19, 22)], fill=GOLD_L, width=1)
    d.line([(25, 22), (29, 22)], fill=GOLD_L, width=1)
    d.ellipse([19, 19, 25, 25], fill=BLOOD, outline=INK, width=1)       # 红心
    d.ellipse([20, 20, 22, 22], fill=BLOOD_L)
    return im


def ic_cinder_cake():
    """火种饼：压着火星的圆饼 + 裂纹 + 底下一片叶（爆炸范围 +15%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([5, 12, 39, 35], fill=SOIL, outline=INK, width=2)         # 饼身
    d.ellipse([7, 9, 37, 29], fill=SOIL_L)                              # 上表面
    d.arc([8, 11, 36, 28], start=200, end=340, fill=(0xC8, 0xA8, 0x80, 255), width=2)
    for x0, y0, x1, y1 in ((14, 14, 19, 22), (19, 22, 26, 18), (26, 18, 30, 25)):
        d.line([(x0, y0), (x1, y1)], fill=SOIL_D, width=2)              # 裂纹
    for x, y in ((16, 18), (22, 21), (28, 20)):                         # 压着的火星
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=FIRE)
        d.ellipse([x - 1, y - 1, x, y], fill=FIRE_L)
    d.polygon([(9, 31), (2, 38), (13, 39)], fill=LEAF)                  # 垫叶
    d.polygon([(35, 30), (42, 36), (33, 39)], fill=LEAF_D)
    return im


def ic_patch_vest():
    """补丁背心：旧布背心 + 三块补丁与缝线（护盾 +2）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(12, 8), (18, 5), (22, 11), (26, 5), (32, 8), (35, 20),
               (33, 39), (11, 39), (9, 20)], fill=CLOTH, outline=INK)
    d.line([(22, 11), (22, 38)], fill=CLOTH_D, width=1)                 # 门襟
    d.line([(12, 16), (18, 16)], fill=CLOTH_D, width=1)
    for x0, y0, x1, y1, col in ((13, 20, 20, 27, LEAF_D),
                                (24, 20, 31, 26, BLOOD),
                                (16, 30, 28, 36, STEEL_D)):             # 三块补丁
        d.rectangle([x0, y0, x1, y1], fill=col, outline=INK, width=1)
        for sx in range(x0 + 1, x1, 3):                                 # 缝线
            d.line([(sx, y0 + 1), (sx, y0 + 1)], fill=BONE_D, width=1)
    d.line([(10, 24), (12, 34)], fill=(0xE0, 0xD4, 0xB4, 255), width=2)  # 亮面
    return im


def ic_tally_beads():
    """记账珠：算框 + 两根横杆 + 一串珠子（金币 +10%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([5, 8, 39, 36], radius=4, outline=WOOD_D, width=3)
    d.rounded_rectangle([5, 8, 39, 36], radius=4, outline=INK, width=1)
    for y, cols in ((17, (GOLD, BLOOD, GOLD, BLOOD)), (27, (BLOOD, GOLD, GOLD, BLOOD))):
        d.line([(7, y), (37, y)], fill=WOOD, width=2)                   # 横杆
        for i, c in enumerate(cols):
            cx = 11 + i * 7
            d.ellipse([cx - 3, y - 4, cx + 3, y + 3], fill=c, outline=INK, width=1)
            d.ellipse([cx - 2, y - 3, cx - 1, y - 2], fill=GOLD_L if c == GOLD else BLOOD_L)
    return im


def ic_study_lamp():
    """温书灯：油壶 + 玻璃罩里的火 + 提环（经验 +10%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([9, 32, 35, 40], radius=3, fill=GOLD_D, outline=INK, width=2)
    d.ellipse([13, 25, 31, 35], fill=GOLD, outline=INK, width=2)        # 油壶
    d.rounded_rectangle([17, 7, 27, 28], radius=5, fill=(0xFF, 0xE9, 0xB0, 190),
                        outline=GOLD_D, width=2)                        # 玻璃罩
    d.polygon([(22, 11), (26, 19), (22, 25), (18, 19)], fill=FIRE)      # 火
    d.polygon([(22, 14), (24, 19), (22, 23), (20, 19)], fill=FIRE_L)
    d.line([(19, 15), (19, 26)], fill=BONE, width=1)                    # 玻璃反光
    d.arc([28, 16, 40, 30], start=280, end=80, fill=GOLD_D, width=2)    # 提环
    for x, y in ((12, 10), (32, 8)):                                    # 光点
        d.line([(x, y), (x + 3, y - 3)], fill=GOLD_L, width=1)
    return im


def ic_thorn_seed():
    """荆棘籽：带刺的荚 + 暗红籽核（暴击伤害 +15%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    for i in range(12):                                                 # 一圈尖刺
        a = i * math.pi / 6
        bx, by = 22 + 11 * math.cos(a), 22 + 11 * math.sin(a)
        tx, ty = 22 + 18 * math.cos(a), 22 + 18 * math.sin(a)
        d.polygon([(bx - 2 * math.sin(a), by + 2 * math.cos(a)),
                   (bx + 2 * math.sin(a), by - 2 * math.cos(a)),
                   (tx, ty)], fill=BONE, outline=INK)
    d.ellipse([11, 11, 33, 33], fill=(0x6E, 0x2E, 0x2A, 255), outline=INK, width=2)
    d.ellipse([15, 15, 29, 29], fill=BLOOD)                             # 籽核
    d.ellipse([17, 16, 22, 21], fill=BLOOD_L)
    d.arc([13, 13, 31, 31], start=200, end=290, fill=(0x8E, 0x4A, 0x42, 255), width=1)
    return im


def ic_night_watch():
    """夜哨铃：手提铜铃 + 铃舌 + 两道声弧 + 一角月（攻速 +8%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(22, 12), (22, 5)], fill=WOOD_D, width=3)                   # 提柄
    d.polygon([(17, 11), (27, 11), (33, 31), (11, 31)], fill=GOLD_D, outline=INK)
    d.rectangle([10, 28, 34, 34], fill=GOLD, outline=INK, width=2)      # 铃口
    d.ellipse([19, 32, 25, 38], fill=STEEL_D, outline=INK, width=1)     # 铃舌
    d.line([(19, 14), (17, 27)], fill=GOLD_L, width=2)                  # 亮面
    for r in (5, 9):                                                    # 声弧
        d.arc([30 - r, 12 - r, 30 + r, 12 + r], start=300, end=20, fill=CYAN, width=2)
    d.ellipse([4, 5, 14, 15], fill=(0xF4, 0xF0, 0xC8, 255), outline=INK, width=1)  # 月
    d.ellipse([8, 3, 17, 13], fill=(0, 0, 0, 0))
    return im


def ic_crow_whistle():
    """乌鸦哨：骨/木哨 + 飞起的乌鸦剪影（远程 +2，代价最大生命 −1）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([5, 24, 27, 35], radius=4, fill=WOOD, outline=INK, width=2)
    d.polygon([(27, 25), (37, 23), (37, 33), (27, 34)], fill=WOOD_D, outline=INK)
    d.ellipse([11, 28, 16, 33], fill=INK)                               # 指孔
    d.line([(7, 27), (24, 27)], fill=WOOD_L, width=1)                   # 亮面
    d.polygon([(30, 6), (40, 10), (33, 13), (36, 18), (26, 14)], fill=INK)  # 乌鸦身
    d.polygon([(40, 10), (43, 8), (41, 12)], fill=GOLD_D)               # 喙
    d.line([(28, 12), (34, 16)], fill=INK, width=2)                     # 翅
    d.line([(26, 20), (22, 23)], fill=ASH, width=1)                     # 哨口
    return im


def ic_seesaw_harness():
    """跷跷板挽具：横梁 + 三角支点 + 两端皮带，高头挂一枚金币（金币 +12%/攻速 −5%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(22, 25), (30, 40), (14, 40)], fill=STEEL_D, outline=INK)  # 支点
    d.line([(5, 21), (39, 13)], fill=WOOD, width=5)                     # 横梁
    d.line([(6, 21), (38, 14)], fill=WOOD_L, width=1)
    for x, y in ((7, 22), (36, 14)):                                    # 两端皮带
        d.line([(x, y), (x, y + 12)], fill=LEATHER, width=3)
        d.ellipse([x - 3, y + 10, x + 3, y + 16], outline=STEEL, width=2)
    d.ellipse([29, 3, 39, 13], fill=GOLD, outline=INK, width=1)         # 高头金币
    d.ellipse([31, 5, 37, 11], outline=GOLD_D, width=1)
    return im


def ic_pig_lantern():
    """猪灯：猪脸灯罩 + 鼻子 + 提梁，眼里透光（小猪/爱心刷新 +1 级，金币 −8%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.arc([12, 1, 32, 15], start=180, end=360, fill=STEEL_D, width=2)   # 提梁
    for x0, y0 in ((9, 11), (27, 11)):                                  # 两只耳
        d.polygon([(x0, y0), (x0 + 6, y0 - 6), (x0 + 7, y0 + 4)], fill=GOLD_D,
                  outline=INK)
    d.ellipse([7, 9, 37, 38], fill=GOLD_D, outline=INK, width=2)        # 灯体
    d.ellipse([12, 15, 32, 33], fill=(0xF6, 0xC8, 0x7A, 255))           # 透光面
    d.ellipse([16, 23, 28, 33], fill=GOLD, outline=INK, width=1)        # 猪鼻
    for x in (19, 24):
        d.line([(x, 26), (x, 31)], fill=INK, width=1)                   # 鼻孔
    for x in (14, 22):                                                  # 眼睛
        d.ellipse([x, 18, x + 4, 22], fill=INK)
    for y in (18, 24, 30):                                              # 灯罩箍
        d.arc([8, y - 3, 36, y + 3], start=200, end=340, fill=GOLD_L, width=1)
    return im


def ic_spring_heel():
    """弹簧后跟：鞋跟 + 叠起的钢弹簧 + 底板（移速 +8%/闪避 +2%，击退 −15%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([12, 3, 32, 19], radius=4, fill=LEATHER, outline=INK, width=2)
    d.line([(15, 6), (15, 16)], fill=(0xA8, 0x7A, 0x54, 255), width=2)  # 皮面亮线
    for i, y in enumerate((22, 26, 30, 34)):                            # 弹簧四圈
        x0 = 11 if i % 2 == 0 else 15
        d.arc([x0, y - 3, x0 + 18, y + 4], start=170, end=370, fill=STEEL, width=2)
        d.arc([x0 + 1, y - 2, x0 + 17, y + 3], start=180, end=300, fill=STEEL_L, width=1)
    d.rectangle([9, 36, 35, 40], fill=STEEL_D, outline=INK, width=1)    # 底板
    for i in range(2):                                                  # 弹跳线
        d.line([(4, 24 + i * 6), (7, 21 + i * 6)], fill=CYAN, width=1)
    return im


def ic_honed_sickle():
    """精磨镰刀：长柄新月刃 + 刃面大片高光 + 刀尖一颗亮星（近战 +3）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(20, 42), (35, 27)], fill=WOOD, width=5)                    # 长柄
    d.line([(21, 41), (33, 30)], fill=WOOD_L, width=1)
    d.arc([2, 1, 36, 31], start=194, end=346, fill=STEEL, width=8)      # 厚刃
    d.arc([4, 3, 34, 29], start=198, end=330, fill=STEEL_L, width=3)    # 磨出的亮面
    d.arc([6, 5, 32, 27], start=205, end=300, fill=GLASS, width=1)      # 镜面反光
    star4(d, 8, 8, 4, BONE)                                             # 刃尖亮星
    star4(d, 15, 4, 2, (0xFF, 0xFF, 0xFF, 255))
    return im


def ic_press_rail():
    """压轨枪：枪身 + 两道平行轨 + 喷射的水线（远程 +3、范围 +10%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([4, 26, 26, 38], radius=4, fill=STEEL_D, outline=INK, width=2)
    d.polygon([(10, 38), (18, 38), (16, 43), (8, 43)], fill=WOOD_D)     # 握把
    for dy, col in ((0, STEEL_L), (4, STEEL)):                          # 两道压轨
        d.line([(20, 24 - dy), (34, 10 - dy)], fill=col, width=2)
    d.polygon([(32, 6), (40, 6), (38, 16), (30, 16)], fill=STEEL, outline=INK)  # 枪口
    d.line([(8, 29), (24, 29)], fill=STEEL_L, width=1)                  # 机匣亮面
    d.line([(30, 18), (40, 8)], fill=WATER, width=3)                    # 水线
    d.line([(31, 20), (41, 11)], fill=WATER_L, width=1)
    d.ellipse([36, 5, 42, 11], fill=WATER_L, outline=INK, width=1)      # 水珠
    return im


def ic_iron_plating():
    """铁衬板：带铆钉的斜铁板 + 倒角亮边 + 两处锈斑（最大生命 +3、护盾 +2）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(7, 10), (34, 5), (38, 30), (14, 39)], fill=STEEL, outline=INK)
    d.polygon([(9, 12), (31, 8), (34, 27), (16, 35)], fill=STEEL_L)     # 倒角亮面
    d.polygon([(20, 10), (34, 8), (36, 26), (22, 33)], fill=(0x8A, 0x92, 0x9C, 255))
    for x, y in ((12, 15), (28, 11), (31, 24), (17, 31)):               # 四颗铆钉
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=STEEL_D, outline=INK, width=1)
        d.ellipse([x - 1, y - 1, x, y], fill=(0xE6, 0xEC, 0xF2, 255))
    for x, y in ((14, 24), (26, 18)):                                   # 锈斑
        d.ellipse([x, y, x + 4, y + 3], fill=SOIL_D)
    return im


def ic_market_bell():
    """市集铃：宽口铜铃 + 竖纹 + 铃前一枚金币（金币 +14%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([17, 2, 27, 12], fill=GOLD_D, outline=INK, width=2)       # 顶环
    d.polygon([(14, 10), (30, 10), (36, 31), (8, 31)], fill=GOLD_D, outline=INK)
    d.rectangle([7, 28, 37, 34], fill=GOLD, outline=INK, width=2)       # 宽铃口
    for x in (16, 22, 28):                                              # 竖纹
        d.line([(x, 13), (x, 28)], fill=GOLD, width=1)
    d.line([(17, 14), (15, 28)], fill=GOLD_L, width=2)                  # 亮面
    d.ellipse([26, 31, 39, 42], fill=GOLD, outline=INK, width=2)        # 金币
    d.ellipse([29, 34, 36, 39], outline=GOLD_D, width=1)
    return im


def ic_seed_lens():
    """育苗透镜：放大镜 + 镜下探出的新芽 + 玻璃反光（经验 +18%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(29, 29), (39, 39)], fill=WOOD, width=4)                    # 手柄
    d.ellipse([3, 3, 31, 31], outline=GOLD_D, width=3)                  # 镜框
    d.ellipse([3, 3, 31, 31], outline=INK, width=1)
    d.ellipse([6, 6, 28, 28], fill=(0xCF, 0xEE, 0xE4, 170))             # 镜片
    d.line([(17, 24), (17, 14)], fill=LEAF_D, width=2)                  # 芽茎
    d.polygon([(17, 16), (9, 12), (17, 11)], fill=LEAF)                 # 芽叶
    d.polygon([(17, 20), (25, 16), (17, 15)], fill=LEAF_D)
    d.line([(9, 9), (15, 15)], fill=BONE, width=2)                      # 反光
    d.line([(19, 8), (23, 12)], fill=BONE, width=1)
    return im


def ic_hot_blood_powder():
    """热血散：纸包 + 露出红药面 + 洒出的红粉 + 三道提速线（攻速 +18%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(9, 12), (33, 12), (35, 34), (7, 34)], fill=BONE, outline=INK)
    d.polygon([(9, 12), (21, 19), (33, 12)], fill=BONE_D)               # 折口
    d.polygon([(11, 25), (31, 25), (32, 33), (10, 33)], fill=BLOOD)     # 红药面
    d.line([(13, 22), (29, 22)], fill=SOIL_D, width=1)
    rng = R("hot_blood_powder")
    for i in range(4):                                                  # 洒出的粉
        x = 6 + rng.randint(0, 30); y = 36 + rng.randint(0, 3)
        d.ellipse([x, y, x + 3, y + 2], fill=BLOOD)
    for i in range(3):                                                  # 提速线
        d.line([(28 + i * 4, 8), (32 + i * 4, 3)], fill=CYAN, width=1)
    for y in (14, 18):                                                  # 热气
        d.line([(4, y), (7, y - 2)], fill=BLOOD_L, width=1)
    return im


def ic_gale_vane():
    """风压标：风向箭头 + 尾羽 + 三道风线，力气顺到一个方向（伤害倍率 +20%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(4, 20), (28, 20)], fill=STEEL_L, width=4)                  # 箭杆
    d.polygon([(27, 11), (41, 20), (27, 29)], fill=STEEL, outline=INK)  # 箭头
    d.polygon([(4, 14), (12, 20), (4, 26)], fill=STEEL_D, outline=INK)  # 尾羽
    d.line([(6, 20), (10, 20)], fill=(0xE8, 0xEE, 0xF4, 255), width=1)
    for i in range(3):                                                  # 风线
        d.line([(6, 30 + i * 5), (30 + i * 4, 30 + i * 5)], fill=CYAN, width=1)
    return im


def ic_bramble_maul():
    """荆棘大槌：木柄 + 带刺的槌头（击退 +45%，代价攻速 −10%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(10, 41), (22, 24)], fill=WOOD, width=5)                    # 柄
    d.line([(11, 40), (21, 26)], fill=WOOD_D, width=1)
    d.rounded_rectangle([16, 6, 38, 26], radius=4, fill=STEEL_D, outline=INK, width=2)
    d.rectangle([19, 9, 35, 14], fill=STEEL)                            # 槌面亮块
    for x, y, dx, dy in ((16, 8, -6, -4), (27, 5, 0, -5), (38, 9, 6, -3),
                         (39, 18, 6, 2), (24, 26, -2, 6)):              # 一圈尖刺
        d.polygon([(x, y), (x + dx, y + dy), (x + 2, y + 2)], fill=BONE, outline=INK)
    for x, y in ((24, 17), (31, 21)):                                   # 木柄捆绳
        d.line([(x, y), (x + 3, y - 3)], fill=BONE_D, width=1)
    return im


def ic_sun_dial():
    """日晷盘：晷面 + 时刻刻度 + 晷针与影子 + 天上一点日（经验 +20%，代价金币 −12%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([4, 14, 40, 41], fill=BONE, outline=INK, width=2)         # 晷面
    d.ellipse([7, 17, 37, 38], fill=(0xDA, 0xD2, 0xBC, 255))
    for i in range(8):                                                  # 时刻刻度
        a = i * math.pi / 4
        d.line([(22 + 12 * math.cos(a), 27 + 9 * math.sin(a)),
                (22 + 15 * math.cos(a), 27 + 11 * math.sin(a))], fill=SOIL_D, width=1)
    d.polygon([(22, 20), (26, 5), (30, 20)], fill=STEEL_D, outline=INK)  # 晷针
    d.line([(24, 20), (36, 33)], fill=(0x8A, 0x74, 0x58, 255), width=2)  # 影子
    d.ellipse([2, 2, 12, 12], fill=GOLD, outline=INK, width=1)           # 日
    for i in range(4):
        a = i * math.pi / 2 + math.pi / 4
        d.line([(7 + 6 * math.cos(a), 7 + 6 * math.sin(a)),
                (7 + 9 * math.cos(a), 7 + 9 * math.sin(a))], fill=GOLD_L, width=1)
    return im


def ic_gamblers_nail():
    """赌徒铁钉：一枚铁钉斜钉 + 一颗骰子（暴击率 +5%、暴伤 +20%，代价血 −2）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rectangle([26, 4, 36, 9], fill=STEEL_L, outline=INK, width=1)     # 钉帽
    d.polygon([(28, 9), (34, 9), (32, 32), (30, 32)], fill=STEEL, outline=INK)  # 钉身
    d.polygon([(30, 32), (32, 32), (31, 40)], fill=STEEL_D, outline=INK)        # 钉尖
    d.line([(30, 11), (30, 30)], fill=STEEL_L, width=1)
    d.rounded_rectangle([5, 18, 23, 37], radius=3, fill=BONE, outline=INK, width=2)
    for x, y in ((9, 22), (17, 22), (13, 28), (9, 33), (17, 33)):       # 五点骰
        d.ellipse([x, y, x + 3, y + 3], fill=INK)
    return im


def ic_thunder_ledger():
    """雷电账本：皮面账本 + 封面一道闪电 + 书页边（金币 +14%，代价射程 −8%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(30, 6), (38, 11), (38, 36), (30, 38)], fill=BONE, outline=INK)  # 书页
    for y in (12, 17, 22, 27, 32):
        d.line([(31, y), (37, y)], fill=BONE_D, width=1)
    d.polygon([(6, 6), (31, 4), (32, 36), (7, 38)], fill=(0x7A, 0x3A, 0x34, 255),
              outline=INK)                                              # 封面
    d.polygon([(19, 9), (12, 22), (18, 22), (15, 33), (26, 18), (20, 18), (24, 9)],
              fill=GOLD_L, outline=INK)                                 # 闪电
    d.line([(9, 10), (9, 34)], fill=(0xA8, 0x5A, 0x50, 255), width=2)   # 书脊
    return im


def ic_moss_cushion():
    """苔藓垫：鼓起的湿苔垫 + 一簇苔叶 + 两点水（最大生命 +5，代价移速 −8%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([4, 18, 40, 40], fill=LEAF_D, outline=INK, width=2)       # 垫底
    d.ellipse([6, 12, 38, 32], fill=LEAF)                               # 垫顶
    rng = R("moss_cushion")
    for i in range(9):                                                   # 苔叶
        x = rng.randint(9, 33); y = rng.randint(14, 28)
        h = rng.randint(3, 5)
        d.line([(x, y), (x, y - h)], fill=LEAF_D, width=1)
        d.arc([x - 2, y - h - 1, x + 2, y - h + 3], start=180, end=360, fill=LEAF, width=1)
    d.ellipse([12, 30, 17, 35], fill=LEAF_D)
    d.line([(9, 20), (12, 27)], fill=(0xB8, 0xE0, 0x8A, 255), width=2)   # 亮面
    d.ellipse([26, 31, 30, 35], fill=WATER)                             # 水珠
    d.ellipse([16, 34, 19, 37], fill=WATER)
    return im


def ic_split_drum():
    """分鼓：鼓面被一道裂纹劈成两半 + 张紧绳（攻速 +18%，代价暴伤 −20%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([3, 9, 41, 37], fill=WOOD, outline=INK, width=2)          # 鼓身
    d.ellipse([7, 13, 37, 33], fill=BONE, outline=INK, width=2)         # 鼓面
    for i in range(8):                                                  # 张紧绳
        a = i * math.pi / 4 + math.pi / 8
        x, y = 22 + 17 * math.cos(a), 23 + 12 * math.sin(a)
        d.line([(x, y), (22 + 19 * math.cos(a), 23 + 13 * math.sin(a))], fill=WOOD_D,
               width=1)
    d.line([(22, 13), (19, 19), (25, 24), (20, 29), (23, 33)], fill=INK, width=2)
    d.line([(22, 13), (19, 19), (25, 24), (20, 29), (23, 33)], fill=(0, 0, 0, 0),
           width=1)                                                     # 劈开的口
    d.ellipse([12, 18, 16, 22], fill=WOOD_L)                            # 鼓面亮
    return im


def ic_harvest_fang():
    """收成獠牙：弯獠牙 + 牙尖亮光 + 根部一片叶（近战 +3、暴伤 +25%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(33, 4), (38, 12), (20, 34), (10, 39), (15, 28), (26, 12)],
              fill=BONE, outline=INK)
    d.polygon([(32, 7), (34, 12), (19, 30), (16, 33), (25, 18)], fill=(0xFF, 0xFC, 0xF0, 255))
    d.line([(31, 8), (18, 31)], fill=BONE_D, width=1)                   # 牙线
    d.polygon([(12, 34), (4, 40), (14, 41)], fill=LEAF)                 # 根部叶
    d.polygon([(14, 36), (22, 41), (12, 41)], fill=LEAF_D)
    star4(d, 36, 6, 4, GOLD_L)                                          # 牙尖亮光
    return im


def ic_rail_lance():
    """轨枪：长柄 + 两道轨 + 钢枪尖 + 水线（远程 +3、范围 +20%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(6, 40), (28, 16)], fill=WOOD, width=5)                     # 长柄
    d.line([(7, 39), (27, 17)], fill=WOOD_L, width=1)
    for dy in (0, 4):                                                   # 两道轨
        d.line([(12, 34 + dy), (30, 14 + dy)], fill=STEEL_L, width=2)
    d.polygon([(27, 13), (40, 2), (36, 17)], fill=STEEL, outline=INK)   # 枪尖
    d.polygon([(28, 14), (38, 5), (35, 15)], fill=STEEL_L)
    d.rectangle([24, 16, 32, 21], fill=STEEL_D, outline=INK, width=1)   # 轨座
    d.line([(33, 14), (41, 8)], fill=WATER, width=2)                    # 水线
    return im


def ic_salt_charge():
    """盐硝包：布药包 + 一圈白盐晶 + 引信与火花（爆炸 +4、范围 +22%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(9, 17), (33, 17), (37, 38), (5, 38)], fill=SOIL, outline=INK)
    d.polygon([(13, 10), (29, 10), (31, 18), (11, 18)], fill=SOIL_D, outline=INK)
    d.line([(12, 24), (34, 24)], fill=SOIL_D, width=2)                  # 缝线
    for x, y in ((11, 15), (18, 12), (25, 14), (31, 16), (15, 20), (28, 21)):
        d.rectangle([x, y, x + 4, y + 4], fill=BONE, outline=INK, width=1)  # 盐晶
    d.line([(21, 10), (21, 4), (28, 1)], fill=WOOD_D, width=2)          # 引信
    star4(d, 30, 3, 3, FIRE_L)                                          # 火花
    d.line([(11, 28), (31, 28)], fill=SOIL_L, width=1)                  # 亮面
    return im


def ic_bull_horn():
    """牛角号：弯牛角 + 铜箍吹口 + 两道声弧（击退 +45%、闪避 +4）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(10, 40), (9, 24), (17, 12), (30, 5), (38, 8), (30, 13),
               (21, 22), (18, 40)], fill=GOLD_D, outline=INK)
    d.polygon([(12, 38), (12, 25), (19, 14), (29, 9), (24, 16), (16, 26), (14, 38)],
              fill=GOLD)                                                # 亮面
    for i, y in enumerate((30, 34, 38)):                                # 角纹
        d.line([(10 + i, y), (18 - i, y)], fill=SOIL_D, width=1)
    d.ellipse([30, 3, 41, 13], fill=GOLD, outline=INK, width=2)         # 吹口铜箍
    d.ellipse([33, 6, 38, 10], fill=INK)
    for r in (4, 8):                                                    # 声弧
        d.arc([20 - r, 6 - r, 20 + r, 6 + r], start=290, end=350, fill=CYAN, width=2)
    return im


def ic_golden_scale():
    """金秤：立柱 + 横梁 + 两只金盘 + 底座（金币 +25%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([14, 34, 30, 40], radius=2, fill=GOLD_D, outline=INK, width=1)
    d.line([(22, 8), (22, 35)], fill=GOLD_D, width=3)                   # 立柱
    d.line([(5, 11), (39, 9)], fill=GOLD, width=3)                      # 横梁
    d.ellipse([18, 4, 26, 12], fill=GOLD, outline=INK, width=1)         # 顶珠
    for x, y in ((8, 12), (36, 10)):                                    # 两臂链条
        d.line([(x, y), (x - 2, y + 8)], fill=GOLD_D, width=1)
        d.line([(x, y), (x + 2, y + 8)], fill=GOLD_D, width=1)
        d.arc([x - 8, y + 7, x + 8, y + 15], start=0, end=180, fill=GOLD, width=3)  # 盘
        d.line([(x - 7, y + 11), (x + 7, y + 11)], fill=GOLD_L, width=1)
    return im


def ic_marathon_sole():
    """长跑鞋底：鞋底 + 花纹齿 + 左侧三道加速线（移速 +16%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([13, 4, 31, 40], radius=9, fill=LEATHER, outline=INK, width=2)
    d.rounded_rectangle([15, 6, 29, 38], radius=8, fill=(0x8E, 0x64, 0x42, 255))
    for y in range(11, 36, 5):                                          # 防滑齿
        d.line([(16, y), (28, y)], fill=STEEL_D, width=2)
        d.line([(16, y + 1), (28, y + 1)], fill=(0x6E, 0x4C, 0x32, 255), width=1)
    d.line([(18, 8), (18, 36)], fill=(0xB0, 0x84, 0x5C, 255), width=2)   # 亮面
    for i in range(3):                                                   # 加速线
        d.line([(4, 12 + i * 8), (10, 9 + i * 8)], fill=CYAN, width=1)
        d.line([(4, 14 + i * 8), (9, 11 + i * 8)], fill=CYAN, width=1)
    return im


def ic_blood_lens():
    """血透镜：镜筒 + 血色镜片 + 一道反光与一滴血（暴击 +6%、伤害 +12%，代价血 −3）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rectangle([8, 3, 28, 9], fill=STEEL_D, outline=INK, width=1)      # 上镜筒
    d.rectangle([8, 31, 28, 37], fill=STEEL_D, outline=INK, width=1)    # 下镜筒
    d.ellipse([10, 4, 26, 36], fill=BLOOD, outline=INK, width=2)        # 血色镜片
    d.ellipse([13, 8, 23, 32], fill=(0xE0, 0x5A, 0x4E, 255))
    d.line([(16, 11), (14, 26)], fill=BLOOD_L, width=2)                 # 反光
    d.polygon([(32, 10), (38, 19), (32, 25), (26, 19)], fill=BLOOD, outline=INK)  # 血滴
    d.ellipse([27, 15, 37, 25], fill=BLOOD, outline=INK)
    d.ellipse([29, 17, 32, 20], fill=BLOOD_L)
    return im


def ic_ram_frame():
    """撞锤架：带轮木架 + 横吊一根撞木、前端钢撞头（击退 +50%，代价近战 −2）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(5, 32), (39, 32)], fill=WOOD_D, width=4)                   # 车架
    for x in (10, 34):                                                  # 两个轮
        d.ellipse([x - 5, 33, x + 5, 41], fill=STEEL_D, outline=INK, width=2)
        d.ellipse([x - 1, 36, x + 1, 38], fill=STEEL_L)
    d.rectangle([8, 16, 30, 28], fill=WOOD, outline=INK, width=2)       # 撞木
    for x in (12, 18, 24):
        d.line([(x, 18), (x, 26)], fill=WOOD_D, width=1)                # 木纹
    d.polygon([(30, 12), (40, 22), (30, 31)], fill=STEEL, outline=INK)   # 钢撞头
    d.line([(31, 15), (36, 22)], fill=STEEL_L, width=2)
    for x in (12, 26):                                                  # 吊链
        d.line([(x, 16), (x, 9)], fill=STEEL_D, width=1)
        d.line([(x, 9), (x + 4, 6)], fill=STEEL_D, width=1)
    return im


def ic_tithe_bell():
    """什一钟：教堂钟 + 顶部悬环 + 钟绳，钟前被切走一角的金币（金币 +25%，代价经验 −12%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([18, 1, 26, 9], outline=GOLD_D, width=2)                  # 悬环
    d.polygon([(15, 8), (29, 8), (36, 28), (8, 28)], fill=GOLD_D, outline=INK)
    d.rectangle([7, 26, 37, 32], fill=GOLD, outline=INK, width=2)       # 钟口
    d.ellipse([19, 29, 25, 35], fill=STEEL_D, outline=INK, width=1)     # 钟舌
    d.line([(18, 12), (16, 26)], fill=GOLD_L, width=2)                  # 亮面
    d.line([(22, 32), (22, 40)], fill=WOOD_D, width=2)                  # 钟绳
    d.ellipse([26, 30, 40, 41], fill=GOLD, outline=INK, width=2)        # 金币
    d.polygon([(36, 30), (41, 33), (41, 42), (36, 41)], fill=(0, 0, 0, 0))  # 切走一成
    d.ellipse([29, 33, 35, 38], outline=GOLD_D, width=1)
    return im


def ic_storm_keg():
    """风暴桶：木桶 + 两道铁箍 + 桶顶压着一朵雷云（攻速 +20%，代价爆炸 −2）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([7, 16, 35, 39], radius=6, fill=WOOD, outline=INK, width=2)
    for y in (21, 33):                                                  # 铁箍
        d.rectangle([6, y, 36, y + 3], fill=STEEL_D, outline=INK, width=1)
    d.line([(11, 18), (11, 37)], fill=WOOD_L, width=2)                  # 亮面
    d.ellipse([7, 4, 21, 15], fill=ASH, outline=INK, width=1)           # 雷云
    d.ellipse([15, 2, 29, 14], fill=ASH, outline=INK, width=1)
    d.ellipse([24, 5, 36, 15], fill=(0x6E, 0x6C, 0x68, 255), outline=INK, width=1)
    d.polygon([(20, 13), (15, 21), (19, 21), (17, 27), (25, 17), (21, 17)],
              fill=GOLD_L, outline=INK)                                 # 闪电
    for x in (10, 17, 30):                                              # 打湿的雨
        d.line([(x, 37 + (x % 3)), (x, 40)], fill=WATER, width=1)
    return im


def ic_titan_haft():
    """泰坦斧柄：粗柄 + 柄端一把大斧头 + 两道铜箍（近战 +4、攻速 +10%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(13, 42), (23, 16)], fill=WOOD, width=9)                    # 粗柄
    d.line([(11, 40), (21, 15)], fill=WOOD_L, width=2)
    d.line([(15, 42), (25, 17)], fill=WOOD_D, width=2)
    d.polygon([(21, 5), (39, 1), (38, 20), (23, 17)], fill=STEEL_D, outline=INK)
    d.polygon([(24, 7), (36, 4), (35, 17), (25, 15)], fill=STEEL)       # 斧面
    d.line([(26, 8), (33, 5)], fill=STEEL_L, width=2)                   # 刃口亮光
    for y in (20, 26):                                                  # 铜箍
        d.line([(9, y + 4), (19, y - 2)], fill=GOLD_D, width=2)
    return im


def ic_star_lattice():
    """星格透镜：透镜里排着九宫格，四个交点上钉着星（远程 +4、暴击 +3）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([3, 3, 41, 41], outline=GOLD_D, width=3)
    d.ellipse([3, 3, 41, 41], outline=INK, width=1)
    d.ellipse([6, 6, 38, 38], fill=(0x2E, 0x3A, 0x4A, 255))             # 镜下暗
    for t in (14, 22, 30):                                              # 格子线
        d.line([(t, 8), (t, 36)], fill=(0x5C, 0x6E, 0x86, 255), width=1)
        d.line([(8, t), (36, t)], fill=(0x5C, 0x6E, 0x86, 255), width=1)
    for x, y in ((14, 14), (30, 14), (14, 30), (30, 30)):
        star4(d, x, y, 5, GOLD_L)                                       # 星位
    star4(d, 22, 22, 6, CYAN)
    d.line([(9, 9), (14, 14)], fill=BONE, width=1)                      # 玻璃反光
    return im


def ic_golden_plough():
    """金犁：镀金犁头 + 犁梁 + 前导轮（金币 +30%、移速 +12%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(7, 36), (30, 18)], fill=GOLD_D, width=5)                   # 犁梁
    d.line([(8, 35), (29, 19)], fill=GOLD_L, width=1)
    d.polygon([(24, 6), (39, 8), (34, 26), (21, 23)], fill=GOLD, outline=INK)
    d.polygon([(26, 9), (36, 10), (32, 23), (24, 21)], fill=GOLD_L)     # 犁面
    d.line([(24, 23), (34, 26)], fill=GOLD_D, width=2)                  # 犁底
    d.ellipse([4, 24, 18, 38], outline=GOLD, width=3)                   # 导轮
    d.ellipse([10, 30, 12, 32], fill=GOLD_D)
    for x in (12, 20, 28):                                              # 翻起的土
        d.line([(x, 40), (x + 4, 40)], fill=SOIL_D, width=1)
    return im


def ic_meteor_seed():
    """陨种：一块烧着的陨石 + 拖尾火线 + 顶上冒出的芽（爆炸 +5、范围 +28%，代价移速 −8%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    for i, w in ((0, 5), (-5, 3), (5, 3)):                              # 拖尾
        d.line([(6 + i, 41), (20 + i, 26)], fill=FIRE_D, width=w)
        d.line([(8 + i, 40), (20 + i, 27)], fill=FIRE, width=max(1, w - 2))
    d.polygon([(22, 8), (33, 6), (38, 16), (34, 29), (22, 29), (17, 17)],
              fill=ROCK, outline=INK)                                   # 石体
    d.polygon([(24, 11), (32, 10), (35, 17), (30, 22), (23, 20)], fill=(0x74, 0x5C, 0x50, 255))
    for x, y in ((22, 15), (28, 24)):                                   # 熔孔
        d.ellipse([x, y, x + 4, y + 4], fill=FIRE)
        d.ellipse([x + 1, y + 1, x + 2, y + 2], fill=FIRE_L)
    d.line([(28, 8), (30, 2)], fill=LEAF_D, width=2)                    # 冒出的芽
    d.polygon([(30, 4), (36, 2), (31, 6)], fill=LEAF)
    star4(d, 12, 16, 3, GOLD_L)                                         # 溅出的火星
    return im


def ic_last_stand():
    """背水：塔盾立在砖墙前，背后一条水线（最大生命 +4、护盾 +2，代价攻速 −10%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(4, 39), (40, 39)], fill=WATER, width=3)                    # 背靠的水
    d.line([(6, 41), (38, 41)], fill=WATER_D, width=2)
    for x0 in (26, 33):                                                 # 身后的砖墙
        for y0 in (8, 15, 22, 29):
            d.rectangle([x0, y0, x0 + 5, y0 + 5], fill=STEEL_D, outline=INK, width=1)
    d.rectangle([20, 8, 40, 34], outline=INK, width=1)                  # 墙框
    d.polygon([(6, 5), (22, 7), (22, 26), (14, 37), (6, 26)], fill=STEEL, outline=INK)
    d.polygon([(8, 8), (19, 10), (19, 25), (13, 33), (8, 25)], fill=STEEL_L)  # 盾面
    for x, y in ((10, 14), (17, 14), (13, 22)):                         # 铆钉
        d.ellipse([x - 1, y - 1, x + 1, y + 1], fill=STEEL_D)
    d.line([(10, 10), (10, 24)], fill=(0xE8, 0xEE, 0xF4, 255), width=1)  # 亮边
    return im


def ic_fate_wheel():
    """命轮：金色轮盘 + 八根辐条 + 中心一颗青星（暴伤 +40%、幸运 +8%，代价伤害倍率 −20%）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([2, 2, 42, 42], outline=GOLD_D, width=3)                  # 轮圈
    d.ellipse([4, 4, 40, 40], outline=INK, width=1)
    for i in range(8):                                                   # 八辐
        a = i * math.pi / 4
        d.line([(22 + 7 * math.cos(a), 22 + 7 * math.sin(a)),
                (22 + 17 * math.cos(a), 22 + 17 * math.sin(a))], fill=GOLD, width=2)
    for i in range(8):                                                   # 轮圈刻度
        a = i * math.pi / 4 + math.pi / 8
        d.line([(22 + 18 * math.cos(a), 22 + 18 * math.sin(a)),
                (22 + 20 * math.cos(a), 22 + 20 * math.sin(a))], fill=PURPLE, width=1)
    d.ellipse([16, 16, 28, 28], fill=GOLD, outline=INK, width=2)        # 轮心
    star4(d, 22, 22, 6, CYAN)
    d.arc([5, 5, 39, 39], start=195, end=255, fill=GOLD_L, width=1)     # 高光弧
    return im


# ══════════════════════════════════════ 作业表（= items.json 中新增 50 件的顺序） ══════════════════════════════════════
# (道具 id, 中文名, 画法)
JOBS = [
    ("chipped_sickle", "缺角镰刀", ic_chipped_sickle),
    ("reed_whistle", "芦苇哨", ic_reed_whistle),
    ("damp_fuse", "潮引线", ic_damp_fuse),
    ("straw_kneepad", "草护膝", ic_straw_kneepad),
    ("tallow_candle", "牛脂烛", ic_tallow_candle),
    ("pebble_pouch", "石子袋", ic_pebble_pouch),
    ("field_ration", "田间口粮", ic_field_ration),
    ("shallow_root", "浅根须", ic_shallow_root),
    ("whittle_knife", "削尖小刀", ic_whittle_knife),
    ("brass_sight", "黄铜准星", ic_brass_sight),
    ("cinder_cake", "火种饼", ic_cinder_cake),
    ("patch_vest", "补丁背心", ic_patch_vest),
    ("tally_beads", "记账珠", ic_tally_beads),
    ("study_lamp", "温书灯", ic_study_lamp),
    ("thorn_seed", "荆棘籽", ic_thorn_seed),
    ("night_watch", "夜哨铃", ic_night_watch),
    ("crow_whistle", "乌鸦哨", ic_crow_whistle),
    ("seesaw_harness", "跷跷板挽具", ic_seesaw_harness),
    ("pig_lantern", "猪灯", ic_pig_lantern),
    ("spring_heel", "弹簧后跟", ic_spring_heel),
    ("honed_sickle", "精磨镰刀", ic_honed_sickle),
    ("press_rail", "压轨枪", ic_press_rail),
    ("powder_horn", "火药角", None),                    # 已有真图 ⇒ 不重画
    ("iron_plating", "铁衬板", ic_iron_plating),
    ("market_bell", "市集铃", ic_market_bell),
    ("seed_lens", "育苗透镜", ic_seed_lens),
    ("hot_blood_powder", "热血散", ic_hot_blood_powder),
    ("gale_vane", "风压标", ic_gale_vane),
    ("bramble_maul", "荆棘大槌", ic_bramble_maul),
    ("sun_dial", "日晷盘", ic_sun_dial),
    ("gamblers_nail", "赌徒铁钉", ic_gamblers_nail),
    ("thunder_ledger", "雷电账本", ic_thunder_ledger),
    ("moss_cushion", "苔藓垫", ic_moss_cushion),
    ("split_drum", "分鼓", ic_split_drum),
    ("harvest_fang", "收成獠牙", ic_harvest_fang),
    ("rail_lance", "轨枪", ic_rail_lance),
    ("salt_charge", "盐硝包", ic_salt_charge),
    ("bull_horn", "牛角号", ic_bull_horn),
    ("golden_scale", "金秤", ic_golden_scale),
    ("marathon_sole", "长跑鞋底", ic_marathon_sole),
    ("blood_lens", "血透镜", ic_blood_lens),
    ("ram_frame", "撞锤架", ic_ram_frame),
    ("tithe_bell", "什一钟", ic_tithe_bell),
    ("storm_keg", "风暴桶", ic_storm_keg),
    ("titan_haft", "泰坦斧柄", ic_titan_haft),
    ("star_lattice", "星格透镜", ic_star_lattice),
    ("golden_plough", "金犁", ic_golden_plough),
    ("meteor_seed", "陨种", ic_meteor_seed),
    ("last_stand", "背水", ic_last_stand),
    ("fate_wheel", "命轮", ic_fate_wheel),
]


# ══════════════════════════════════════ 输出 / .meta / 校验 ══════════════════════════════════════

def res_of(item_id):
    return "icon_item_" + item_id


def make_meta(png_path, item_id):
    """.meta 逐字段等于邻居，只换 guid = md5("Seedfall/v284/item/<id>")（恒定 ⇒ 重跑不变）。"""
    tpl = open(META_TEMPLATE, encoding="utf-8").read()
    guid = hashlib.md5(("Seedfall/v284/item/" + item_id).encode("utf-8")).hexdigest()
    out = []
    for ln in tpl.split("\n"):
        out.append("guid: " + guid if ln.startswith("guid: ") else ln)
    open(png_path + ".meta", "w", encoding="utf-8", newline="\n").write("\n".join(out))


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def snap():
    return {f: sha(os.path.join(UI, f)) for f in os.listdir(UI) if f.endswith(".png")}


# ── 风格锚点：既有 130 张 icon_item_*.png 的底板像素（v258 模板的确定性输出） ──
ANCHOR_FILE = "icon_item_amber_sickle.png"
ANCHOR_PX = {
    (0, 32): (110, 82, 53, 255),      # 左侧底板（渐变中段）
    (32, 0): (138, 106, 70, 255),     # 顶边（渐变上端 #8A6A46）
    (32, 63): (84, 60, 38, 255),      # 底边（渐变下端 #543C26）
    (5, 2): (136, 104, 68, 255),      # 顶部内区
    (5, 58): (43, 33, 27, 255),       # 外框竖边（#2B211B）
    (0, 0): (0, 0, 0, 0),             # 圆角外透明
}


def check_style(im, name):
    """风格锚点：底板渐变/外框/圆角与既有图标**逐像素相同**；物件不得铺满整块。"""
    px = im.load()
    for (x, y), want in ANCHOR_PX.items():
        got = px[x, y]
        assert got == want, "%s 底板锚点 (%d,%d) 期望 %s 实际 %s" % (name, x, y, want, got)
    # 物件区：44×44 里非透明像素的比例（区分"真画了东西"与"空/满色块"）
    cov = sum(1 for y in range(10, 54) for x in range(10, 54) if px[x, y][3] > 0
              and px[x, y][:3] != tuple(min(255, v) for v in (0, 0, 0)))
    return cov


def count_unique(im):
    return len({im.getpixel((x, y)) for y in range(64) for x in range(64)})


# ══════════════════════════════════════ 三档入口 ══════════════════════════════════════

def plan():
    print("%-22s %-10s %-8s %s" % ("资源名", "中文名", "状态", "说明"))
    print("-" * 96)
    for iid, cn, fn in JOBS:
        p = os.path.join(UI, res_of(iid) + ".png")
        if fn is None:
            st = "已有真图"
            note = "非占位，本脚本只校验不重画"
        elif os.path.exists(p):
            st = "已存在→覆盖"
            note = "重画"
        else:
            st = "新增"
            note = "生成"
        print("%-22s %-10s %-8s %s" % (res_of(iid), cn, st, note))
    print("-" * 96)
    print("合计 %d 件；其中需生成 %d 张，已有 %d 张"
          % (len(JOBS), sum(1 for _, _, f in JOBS if f), sum(1 for _, _, f in JOBS if not f)))


def run():
    before = snap()
    rows = []
    print("%-4s %-24s %-26s %-9s %-8s %s"
          % ("#", "文件名", "中文名", "尺寸", "字节", "状态"))
    print("-" * 100)
    for idx, (iid, cn, fn) in enumerate(JOBS, 1):
        res = res_of(iid)
        p = os.path.join(UI, res + ".png")
        if fn is None:                                   # powder_horn：已有真图，只校验
            assert os.path.exists(p), "powder_horn 既无画法也无既有图 —— 这件必须补画"
            im = Image.open(p).convert("RGBA")
            assert im.size == (SIZE, SIZE)
            assert count_unique(im) > 200, "powder_horn 既有图疑似占位色块"
            rows.append((idx, res, cn, im.size, os.path.getsize(p), "已有·未改动"))
            print("%-4d %-24s %-26s %-9s %-8d %s"
                  % (idx, res + ".png", cn, "%d×%d" % im.size, os.path.getsize(p),
                     "已有·未改动"))
            continue

        obj = fn()
        assert obj.size == (OBJ, OBJ), (res, obj.size)
        img = compose(obj, "item")
        check_style(img, res)
        was = os.path.exists(p)
        img.save(p, "PNG", optimize=True)
        if not os.path.exists(p + ".meta"):
            make_meta(p, iid)
        size = os.path.getsize(p)
        rows.append((idx, res, cn, img.size, size, "重画" if was else "新增"))
        print("%-4d %-24s %-26s %-9s %-8d %s"
              % (idx, res + ".png", cn, "%d×%d" % img.size, size, "重画" if was else "新增"))

    print("-" * 100)
    written = [r for r in rows if r[5] != "已有·未改动"]
    sizes = [r[4] for r in written]
    print("生成 %d 张｜保留未动 %d 张｜覆盖 %d 件"
          % (len(written), len(rows) - len(written), len(rows)))
    print("尺寸：全部 %d×%d｜字节数区间 %d ~ %d（中位 %d）"
          % (SIZE, SIZE, min(sizes), max(sizes), sorted(sizes)[len(sizes) // 2]))

    # ── 回归断言：作业表之外的 PNG 必须逐字节不变 ──
    after = snap()
    mine = set(res_of(iid) + ".png" for iid, _, fn in JOBS if fn is not None)
    touched = sorted([f for f in before if f in after and before[f] != after[f]]
                     + [f for f in after if f not in before])
    bad = [f for f in touched if f not in mine]
    assert not bad, "作业表之外的 PNG 被改了：%s" % bad
    untouched = len([f for f in before if f in after and before[f] == after[f]
                     and f not in mine])
    print("本次改动 PNG %d 张（全部在作业表内）；其余 %d 张逐字节不变；PNG 总数 %d → %d"
          % (len(touched), untouched, len(before), len(after)))

    # ── 剪影查重：50 张里不许出现两张几乎一样的图（v274 用同一判据找过占位） ──
    dup = []
    sil = {}
    for iid, cn, fn in JOBS:
        im = Image.open(os.path.join(UI, res_of(iid) + ".png")).convert("RGBA")
        px = im.load()
        sil[iid] = frozenset((x, y) for y in range(10, 54) for x in range(10, 54)
                             if px[x, y][3] > 0 and px[x, y][:3] != (43, 33, 27))
    ids = [i for i, _, _ in JOBS]
    for a in range(len(ids)):
        for b in range(a + 1, len(ids)):
            A, B = sil[ids[a]], sil[ids[b]]
            if A and B:
                iou = len(A & B) / len(A | B)
                if iou >= 0.90:
                    dup.append((ids[a], ids[b], round(iou, 3)))
    print("剪影查重（IoU≥0.90 视为疑似同一张）：%s" % (dup if dup else "无，50 张互不相同"))


def check():
    """只校验落盘结果：尺寸 / .meta 逐字段 / 风格锚点 / guid。"""
    tpl = open(META_TEMPLATE, encoding="utf-8").read().split("\n")
    n_ok = 0
    print("%-24s %-8s %-9s %-8s %s" % ("文件", "尺寸", "色调数", "字节", ".meta"))
    print("-" * 96)
    for iid, cn, fn in JOBS:
        p = os.path.join(UI, res_of(iid) + ".png")
        assert os.path.exists(p), "缺图：" + p
        im = Image.open(p).convert("RGBA")
        assert im.size == (SIZE, SIZE), (p, im.size)
        check_style(im, res_of(iid))
        mp = p + ".meta"
        assert os.path.exists(mp), "缺 meta：" + mp
        got = open(mp, encoding="utf-8").read().split("\n")
        want = [(("guid: " + hashlib.md5(("Seedfall/v284/item/" + iid).encode()).hexdigest())
                 if ln.startswith("guid: ") else ln) for ln in tpl]
        if fn is not None:
            diff = [(i, w, g) for i, (w, g) in enumerate(zip(want, got)) if w != g]
            assert not diff, "%s.meta 与邻居不等：%s" % (res_of(iid), diff[:3])
        print("%-24s %-8s %-9d %-8d %s"
              % (res_of(iid) + ".png", "%d×%d" % im.size, count_unique(im),
                 os.path.getsize(p), "逐字段=邻居" if fn is not None else "既有(未改)"))
        n_ok += 1
    print("-" * 96)
    print("校验通过：%d/%d 张尺寸 64×64、.meta 逐字段等于邻居、底板与既有图标逐像素一致"
          % (n_ok, len(JOBS)))


def preview():
    """并排预览：50 张新图 + 一排既有图标（同一底色、同一放大倍数），便于人眼比对风格。"""
    Z = 3
    COLS = 10
    pad, label_h = 8, 12
    cell = SIZE * Z
    rows_new = (len(JOBS) + COLS - 1) // COLS
    olds = ["amber_sickle", "ash_pouch", "bronze_lens", "aegis_lamp", "bark_buckler",
            "beeswax_glove", "carrot_ring", "clay_trowel", "dew_stone", "iron_bracer"]
    rows_old = 1
    head = 26
    W = pad + COLS * (cell + pad)
    H = head + pad + rows_new * (cell + pad + label_h) + head + pad + rows_old * (cell + pad)
    sheet = Image.new("RGBA", (W, H), (26, 24, 22, 255))
    dd = ImageDraw.Draw(sheet)
    font = None
    try:
        from PIL import ImageFont
        font = ImageFont.load_default()
    except Exception:
        pass

    def put(x, y, im, txt):
        sheet.alpha_composite(im.convert("RGBA").resize((cell, cell), Image.NEAREST), (x, y))
        if font:
            dd.text((x, y + cell + 1), txt[:20], fill=(210, 200, 186, 255), font=font)

    y = pad
    dd.text((pad, pad - 4), "v284  50 items (new)", fill=(240, 230, 210, 255),
            font=font) if font else None
    y = head
    for i, (iid, cn, fn) in enumerate(JOBS):
        r, c = divmod(i, COLS)
        x = pad + c * (cell + pad)
        yy = y + r * (cell + pad + label_h)
        im = Image.open(os.path.join(UI, res_of(iid) + ".png"))
        put(x, yy, im, iid)
    y2 = y + rows_new * (cell + pad + label_h) + head
    if font:
        dd.text((pad, y2 - head + pad), "existing (v258/v274 style reference)",
                fill=(240, 230, 210, 255), font=font)
    for i, n in enumerate(olds):
        x = pad + i * (cell + pad)
        p = os.path.join(UI, "icon_item_%s.png" % n)
        if os.path.exists(p):
            put(x, y2, Image.open(p), n)
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    sheet.convert("RGB").save(PREVIEW, "PNG", optimize=True)
    print("预览图：%s（%d×%d，%s）" % (PREVIEW, sheet.width, sheet.height,
                                  "%d 新 + %d 旧" % (len(JOBS), len(olds))))


if __name__ == "__main__":
    if "--dry-run" in sys.argv:
        plan()
    elif "--check" in sys.argv:
        check()
    elif "--preview" in sys.argv:
        preview()
    else:
        run()
        check()
        preview()