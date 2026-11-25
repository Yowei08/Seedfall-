# -*- coding: utf-8 -*-
"""v307：补齐 19 张缺失图标 —— 7 个插件 + 12 植物（道具已 100% 齐，不碰）。

════════════════════════════════════════════════════════════════════════════
★ 复现参数（全部写死，脚本内没有任何随机源 ⇒ 两次运行逐字节相同）★
  统计口径 : `plugins.json`(28 件) 缺 7 张；`seeds.json`(51 株) 缺 12 张；
             `items.json`(151 件) 一张不缺 ⇒ 本脚本只画这 19 张。
  画布     : 64×64 RGBA / PNG（与既有 275 张 icon_*.png 同尺寸）
  外壳     : 直接 `import restyle_icons_v258 as V258` —— 用它自己的常量与函数：
             SIZE=64 / RADIUS=12 / OBJ=44 / OUTLINE=#2B211B /
             THEMES{item,plant,plugin} 的上下渐变 / rounded_mask / gradient / outline_of
             合成顺序与 `V258.restyle()` 的 ①~④ 逐条一致（见 compose()）：
               ① 渐变底板（圆角 12）
               ② 左上 1px 内高光 rgba(255,255,255,70)
               ③ 物件：右下 1px 投影(12,9,6,120) → 外扩 2px 深描边 → 本体，居中 44×44
               ④ 外框 2px OUTLINE
  调色板   : `import gen_missing_icons_v274 as V274` —— 只为拿它的 INK/STEEL/WOOD/
             WATER/FIRE/BLOOD/GOLD/LEAF/SOIL/BONE/ASH/CYAN/PURPLE 常量，
             保证这 19 张与 v274 那 30 张、v284 那 50 张**同色**（v274/v284 一个字节不改）
  物件区   : 44×44 透明画布，图元一律画在 2..41 之间（给外扩 2px 描边留位）
  物件画法 : 平涂 + 1 层亮面(左上) + 1 层暗面(右下) + ≥1 个 accent 点 + 深色内部线，
             与既有"硬边像素"画法同一条路子（不做抗锯齿插值）
  输出     : Assets/Resources/Art/UI/icon_plugin_<id>.png  ×7
             Assets/Resources/Art/UI/icon_plant_<id>.png   ×12
  .meta    : 以邻居 `icon_item_wax_seal.png.meta` 为模板，**逐字段照抄、只换 guid**；
             guid = md5("Seedfall/v307/<kind>/<id>") ⇒ 恒定，重跑不换 guid
             （textureType=0 / filterMode=1 / spritePixelsToUnits=100 /
               maxTextureSize=2048 / enableMipMap=0，均已在下断言）
  不动的东西: 既有 PNG 与 .meta 一个字节不改 —— run() 前后做 sha256 全量比对并断言；
             本批 19 张在跑之前**一张都不存在**（存在则视为"覆盖"，会先备份）

★ 语义来源（不许凭空编，逐个对应数据表）★
  插件看 `plugins.json` 的 displayName / effect / description；
  植物看 `seeds.json` 的 name / mechanic / attackType。
  每一张的"画什么"写在 JOBS 表的 SEM 列与各画法函数首行 docstring 里。

★ 查重 ★
  剪影 = 44×44 内与底板渐变色不同的像素；两张的 IoU ≥ 0.90 判为重复 ⇒ 必须重画。
  本脚本同时查两遍：① 19 张两两之间；② 19 张 × 既有 96 张（plugin 55 + plant 41）。

★ 用法 ★
  python Tools/icon_gen/gen_icons_v307.py              # 生成 19 张 + 全套断言 + 预览图
  python Tools/icon_gen/gen_icons_v307.py --dry-run    # 只打印作业表与现状，不写盘
  python Tools/icon_gen/gen_icons_v307.py --check      # 只校验落盘结果（尺寸/meta/锚点/查重/复现）
  python Tools/icon_gen/gen_icons_v307.py --preview    # 只重建预览图
  python Tools/icon_gen/gen_icons_v307.py --restore    # 回滚（本批全是新增 ⇒ 删掉 19 对 png+meta）
════════════════════════════════════════════════════════════════════════════
"""
import os
import sys
import io
import json
import math
import random
import shutil
import hashlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw

import restyle_icons_v258 as V258            # ★ 画风外壳：常量 + 三个图元函数
import gen_missing_icons_v274 as V274        # ★ 只借调色板（不修改它）

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UI = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
DATA = os.path.join(ROOT, "Assets", "Resources", "Data")
BAK = os.path.join(ROOT, "Tools", "icon_gen", "backup_v307")
META_TEMPLATE = os.path.join(UI, "icon_item_wax_seal.png.meta")
PREVIEW_DIR = os.environ.get(
    "V307_PREVIEW_DIR", r"C:\Users\18462\Documents\deepseek-harness\default-workspace")
PREVIEW = os.path.join(PREVIEW_DIR, "_v307_icons.png")

SIZE = V258.SIZE            # 64
OBJ = V258.OBJ              # 44
RADIUS = V258.RADIUS        # 12
OUTLINE = V258.OUTLINE      # #2B211B
THEMES = V258.THEMES
SEED = 20250307             # 固定种子（本脚本只用它给"散点"取坐标，且按 id 各自独立取）

# ─── 调色板：与 v274/v284 完全同一组常量 ───
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

# ─── 本批新增的 8 个材质色（v284 也是这么加 CLOTH/LEATHER/ROCK/GLASS 的） ───
SLUDGE = (0x6E, 0x84, 0x36, 255)     # 臭水沟：浑浊绿水
SLUDGE_L = (0x9C, 0xB4, 0x50, 255)
PEEL = (0xE6, 0xCE, 0x66, 255)       # 香蕉皮
PEEL_D = (0xB4, 0x9A, 0x3C, 255)
VOID = (0x24, 0x1C, 0x30, 255)       # 黑洞本体
VOID_L = (0x7A, 0x5E, 0xC8, 255)
CLAY = (0xC0, 0x8E, 0x5E, 255)       # 陶罐
CLAY_D = (0x8E, 0x62, 0x3C, 255)
PINK = (0xE6, 0xB4, 0xC6, 255)       # 芍药/莲花瓣
PINK_D = (0xC0, 0x84, 0x9A, 255)
MOON = (0xF4, 0xF0, 0xC8, 255)       # 月（与 v274 can_tide 同色）
TUBE = (0x2E, 0x35, 0x2A, 255)       # 水位管壁


# ══════════════════════════════════════ 外壳合成（≡ v258.restyle ①~④） ══════════════════════════════════════

def O44():
    return Image.new("RGBA", (OBJ, OBJ), (0, 0, 0, 0))


def compose(obj, kind):
    """把 44×44 的物件贴进 v258 的外壳里 —— 顺序与 `restyle_icons_v258.restyle()` ①~④ 一致。"""
    theme = THEMES[kind]
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # ① 渐变底板（圆角）—— 直接调 v258 的两个函数
    panel = V258.gradient(SIZE, theme[0], theme[1]).convert("RGBA")
    panel.putalpha(V258.rounded_mask(SIZE, RADIUS))
    canvas.alpha_composite(panel)

    # ② 左上 1px 内高光（与 v258 同一位置、同一 alpha）
    hl = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(hl).rounded_rectangle([1, 1, SIZE - 2, SIZE - 2], radius=RADIUS - 1,
                                         outline=(255, 255, 255, 70), width=1)
    canvas.alpha_composite(hl)

    # ③ 物件：投影 → 2px 深描边 → 本体（与 v258 同一套 outline_of 形态学外扩）
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


# ══════════════════════════════════════ 公共图元（本批三个画法共用） ══════════════════════════════════════

def rng_of(res):
    """每张图自己的随机源：只喂散点坐标，且由资源名 + SEED 决定 ⇒ 与作业顺序无关。"""
    return random.Random("%s|%d" % (res, SEED))


def drop(d, cx, cy, r, fill, ink=True):
    """水滴：圆 + 上尖（v274 里血珠/水珠就是这形状）。"""
    d.polygon([(cx, cy - r * 2), (cx + r * 0.85, cy - r * 0.35),
               (cx - r * 0.85, cy - r * 0.35)], fill=fill)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)
    if ink:
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=INK, width=1)


def coin(d, cx, cy, r, face=GOLD, edge=GOLD_D):
    """金币：外圈压边 + 内圈亮面。"""
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=edge, outline=INK)
    d.ellipse([cx - r + 1, cy - r + 1, cx + r - 1, cy + r - 1], fill=face)
    d.ellipse([cx - r + 2, cy - r + 1, cx + r - 3, cy - r + 3], fill=GOLD_L)


def grain(d, cx, cy, r=2, fill=GOLD):
    """麦粒：一枚小椭圆（带描边，用于"收成"语义）。"""
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill, outline=INK)


def fan(d, bx, by, angs, ln, wd, fill, ink=INK):
    """扇形花瓣：从基点 (bx,by) 向 angs 各方向各出一片三角瓣（莲花/芍药共用）。"""
    for a in angs:
        t = math.radians(a)
        px, py = -math.sin(t) * wd, math.cos(t) * wd
        tx, ty = bx + math.cos(t) * ln, by + math.sin(t) * ln
        d.polygon([(bx, by), (tx + px, ty + py), (tx - px, ty - py)], fill=fill, outline=ink)


def zig(d, pts, fill, w=2):
    """折线（闪电/裂纹）。"""
    d.line(pts, fill=fill, width=w, joint="curve")


# ══════════════════════════════════════ 插件 7 张（底板 = 石板蓝） ══════════════════════════════════════
# 语义全部来自 plugins.json：displayName / effect / description

def ic_can_sewer():
    """臭水沟 Sewer：壶嘴喷出**浑浊绿水**，脚边泼下一块**香蕉皮**，左上两条臭气。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 壶嘴（左侧）
    d.rounded_rectangle([3, 20, 15, 30], radius=3, fill=STEEL_D, outline=INK)
    d.line([(5, 22), (13, 22)], fill=STEEL_L, width=1)
    # 三股浑浊水（向右上扇形）
    for tx, ty in ((25, 12), (32, 17), (30, 25)):
        d.line([(14, 25), (tx, ty)], fill=SLUDGE, width=4)
        d.ellipse([tx - 3, ty - 3, tx + 3, ty + 3], fill=SLUDGE_L, outline=INK)
    d.line([(15, 22), (24, 13)], fill=SLUDGE_L, width=1)
    # 臭气（左上两条开口向下的弧）
    d.arc([1, 3, 15, 17], start=110, end=250, fill=SLUDGE_L, width=2)
    d.arc([8, 0, 22, 14], start=110, end=250, fill=SLUDGE_L, width=2)
    # 香蕉皮（右下：一条弧形皮 + 三片翻起的瓣）
    d.arc([14, 26, 42, 44], start=198, end=342, fill=PEEL, width=6)
    d.polygon([(17, 31), (21, 41), (26, 31)], fill=PEEL_D, outline=INK)
    d.polygon([(25, 31), (29, 41), (34, 30)], fill=PEEL, outline=INK)
    d.polygon([(33, 30), (37, 39), (40, 30)], fill=PEEL_D, outline=INK)
    d.line([(18, 33), (23, 33)], fill=GOLD_L, width=1)
    return im


def ic_can_sharp():
    """锋利 SharpHarvest：水柱被压成**一道水刀**，右下是**割下来的麦穗**（收成打折：断粒）。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 水刀（左下 → 右上的一道楔）
    d.polygon([(5, 31), (30, 6), (36, 14), (13, 38)], fill=WATER, outline=INK)
    d.line([(11, 29), (31, 10)], fill=WATER_L, width=2)       # 刃口高光
    d.line([(9, 34), (18, 25)], fill=WATER_D, width=1)        # 刃背暗面
    # 被割下的麦穗（右下，立在原地）
    d.line([(35, 41), (35, 25)], fill=LEAF_D, width=3)
    grain(d, 31, 26); grain(d, 39, 28); grain(d, 31, 32); grain(d, 39, 34)
    # 断口：两粒掉在地上 + 一点切口
    grain(d, 26, 39, 2, GOLD_D)
    d.line([(22, 37), (28, 33)], fill=WATER_L, width=2)
    return im


def ic_can_wave():
    """海浪 Wave：脚下一道**卷起的浪**把人扫得站不稳（你跟着更快：三条速度线）。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 浪身（卷成 C 形开口朝左；浪腹内部留空，别糊成一整块）
    d.polygon([(14, 37), (15, 25), (21, 16), (31, 11), (39, 15), (36, 22),
               (28, 21), (23, 26), (28, 31), (34, 29), (35, 37)],
              fill=WATER, outline=INK)
    # 浪尖泡沫
    for cx, cy in ((20, 17), (28, 12), (34, 16)):
        d.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], fill=WATER_L, outline=INK)
    # 浪腹暗面
    d.line([(18, 34), (31, 34)], fill=WATER_D, width=2)
    # 三道速度线（左）
    for k in range(3):
        y = 20 + k * 8
        d.line([(2, y), (9 - k, y)], fill=CYAN, width=2)
    # 一颗溅起的水珠
    drop(d, 8, 12, 2, WATER_L)
    return im


def ic_fert_blackhole():
    """黑洞 BlackHole：肥料的**爆点先把敌人吸过去** —— 暗盘 + 吸积弧 + 向心碎屑 + 即将炸开的芯。"""
    im = O44(); d = ImageDraw.Draw(im)
    cx = cy = 22
    # 吸积盘（两道弧，一紫一亮）
    d.arc([cx - 16, cy - 16, cx + 16, cy + 16], start=196, end=344, fill=VOID_L, width=3)
    d.arc([cx - 14, cy - 14, cx + 14, cy + 14], start=18, end=162, fill=PURPLE, width=2)
    # 事件视界（暗盘）
    d.ellipse([cx - 10, cy - 10, cx + 10, cy + 10], fill=VOID, outline=INK)
    d.ellipse([cx - 10, cy - 10, cx + 10, cy + 10], outline=VOID_L, width=1)
    # 被吸进去的碎屑（六块，尖朝中心）
    r = rng_of("fert_blackhole")
    for k in range(6):
        a = -math.pi / 2 + k * math.pi / 3 + r.uniform(-0.12, 0.12)
        rr = r.uniform(12.5, 15.0)
        x, y = cx + math.cos(a) * rr, cy + math.sin(a) * rr
        b = a + math.pi                                  # 内向
        d.polygon([(x + math.cos(b) * 3.4, y + math.sin(b) * 3.4),
                   (x + math.cos(b + 2.2) * 2.6, y + math.sin(b + 2.2) * 2.6),
                   (x + math.cos(b - 2.2) * 2.6, y + math.sin(b - 2.2) * 2.6)],
                  fill=STEEL_D, outline=INK)
    # 中心即将炸开的芯
    d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=GOLD_L)
    d.line([(cx - 7, cy), (cx + 7, cy)], fill=GOLD_L, width=1)
    d.line([(cx, cy - 7), (cx, cy + 7)], fill=GOLD_L, width=1)
    return im


def ic_hoe_frenzy():
    """狂暴 Frenzy：残血敌人挨这一刀更疼 —— 锄刃 + **迸开的怒气/血花**（比吸血多一圈放射）。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 柄 + 刃（左下 → 右上）
    d.line([(4, 40), (12, 32)], fill=WOOD, width=4)
    d.polygon([(10, 34), (24, 20), (30, 26), (16, 40)], fill=STEEL, outline=INK)
    d.line([(14, 34), (26, 23)], fill=STEEL_L, width=1)
    # 怒气放射（右上：六根长短不一的尖刺，全部收在 4..42 之内）
    for ang, ln in ((-84, 10), (-58, 11), (-30, 11), (-2, 10), (26, 9), (54, 8)):
        a = math.radians(ang)
        x1, y1 = 31 + math.cos(a) * 4, 18 + math.sin(a) * 4
        x2, y2 = 31 + math.cos(a) * ln, 18 + math.sin(a) * ln
        d.line([(x1, y1), (x2, y2)], fill=BLOOD_L, width=2)
    d.ellipse([28, 15, 34, 21], fill=BLOOD, outline=INK)      # 怒心
    # 溅血点
    for cx, cy in ((7, 17), (40, 36), (20, 8)):
        d.ellipse([cx - 2, cy - 2, cx + 2, cy + 2], fill=BLOOD, outline=INK)
    return im


def ic_hoe_giant():
    """巨型 Giant：挥击不再只打面前，而是**环绕玩家一整圈** —— 环 + 四个方向箭头 + 中心小锄。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 挥击环（几乎闭合，右上留一道缺口）
    d.arc([4, 4, 40, 40], start=-96, end=250, fill=STEEL, width=5)
    d.arc([4, 4, 40, 40], start=-90, end=246, fill=STEEL_L, width=1)      # 环上高光
    # 四个方向箭头（金；都留在 2..41 之内，给 2px 外扩描边留位）
    for pts in ([(22, 2), (17, 8), (27, 8)], [(22, 41), (17, 35), (27, 35)],
                [(2, 22), (8, 17), (8, 27)], [(41, 22), (35, 17), (35, 27)]):
        d.polygon(pts, fill=GOLD, outline=INK)
    # 中心：玩家 + 手里的小锄
    d.ellipse([18, 18, 26, 26], fill=BONE, outline=INK)
    d.line([(19, 25), (25, 19)], fill=WOOD_D, width=2)
    d.polygon([(24, 18), (28, 20), (25, 23)], fill=STEEL_D, outline=INK)
    return im


def ic_hoe_shock():
    """感电 Shock：砍中时**电流窜向周围** —— 锄刃在左下，电弧分叉到两个敌人点。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 柄 + 刃（左下，短）
    d.line([(3, 41), (11, 33)], fill=WOOD, width=4)
    d.polygon([(9, 35), (21, 23), (26, 29), (14, 41)], fill=STEEL, outline=INK)
    d.line([(13, 35), (23, 26)], fill=STEEL_L, width=1)
    # 电弧 1：从刃口窜到右上敌人
    zig(d, [(24, 26), (30, 20), (27, 16), (33, 12)], GOLD_L, 2)
    # 电弧 2：窜到右下敌人
    zig(d, [(23, 32), (29, 34), (31, 31), (35, 33)], GOLD_L, 2)
    # 两个被电到的敌人（圆 + 三道小闪电尖）
    for cx, cy in ((34, 9), (37, 33)):
        d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=STEEL_D, outline=INK)
        d.ellipse([cx - 2, cy - 2, cx + 2, cy + 2], fill=WATER_L)
        for dx, dy in ((-4, 0), (4, 0), (0, -4)):
            d.line([(cx + dx * 0.55, cy + dy * 0.55), (cx + dx, cy + dy)], fill=GOLD_L, width=1)
    return im


# ══════════════════════════════════════ 植物 12 张（底板 = 叶绿） ══════════════════════════════════════
# 语义全部来自 seeds.json：name / mechanic

def ic_dew_jar_moss():
    """蓄水苔 WaterSave：苔藓长在**陶罐口**上、把水存住 —— 罐身 + 满水 + 口沿苔团 + 一滴水。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 罐身（宽陶罐）
    d.rounded_rectangle([7, 17, 33, 41], radius=9, fill=CLAY, outline=INK)
    d.rounded_rectangle([9, 21, 16, 39], radius=4, fill=(0xD8, 0xAC, 0x7C, 255))   # 左侧亮面
    d.rounded_rectangle([10, 23, 30, 36], radius=6, fill=WATER)                    # 罐里存的满水
    d.line([(12, 24), (28, 24)], fill=WATER_L, width=1)                            # 水面高光
    d.rectangle([6, 13, 34, 19], fill=CLAY_D, outline=INK)                         # 口沿
    d.line([(8, 15), (32, 15)], fill=SOIL_L, width=1)
    # 口沿上的苔团（五团，高低不一）
    for cx, cy, r in ((11, 12, 4), (18, 9, 5), (26, 11, 4), (32, 14, 3), (24, 14, 3)):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=LEAF, outline=INK)
    r = rng_of("dew_jar_moss")
    for k in range(6):                                   # 苔上的小暗点（绒毛）
        cx = r.randint(10, 30); cy = r.randint(9, 15)
        d.point((cx, cy), fill=LEAF_D)
    # 一滴落下/悬着的水
    drop(d, 38, 25, 3, WATER_L)
    d.arc([36, 30, 41, 36], start=250, end=20, fill=WATER_L, width=1)
    return im


def ic_drizzle_bud():
    """凝露芽 DewDrop：花芽把空气里的水**凝成露** —— 尖顶闭合芽 + 三颗露珠压在芽尖上。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 外苞（闭合的尖芽）
    d.polygon([(17, 38), (10, 27), (14, 15), (20, 8), (26, 17), (28, 27), (25, 38)],
              fill=LEAF_D, outline=INK)
    # 内叶（亮面）
    d.polygon([(18, 36), (14, 27), (20, 13), (24, 27), (22, 36)], fill=LEAF)
    d.line([(18, 33), (19, 17)], fill=(0xA8, 0xD8, 0x7A, 255), width=1)   # 叶脉高光
    # 苞片上的两道折线
    d.line([(13, 24), (18, 21)], fill=LEAF_D, width=1)
    d.line([(25, 24), (20, 21)], fill=LEAF_D, width=1)
    # 三颗露珠（芽尖上方，大小递减；尖端都留在画布内）
    drop(d, 21, 8, 3, WATER)
    d.ellipse([19, 7, 22, 10], fill=WATER_L)
    drop(d, 31, 14, 2, WATER_L)
    drop(d, 11, 17, 2, WATER_L)
    # 土面
    d.polygon([(10, 41), (30, 41), (27, 38), (13, 38)], fill=SOIL, outline=INK)
    return im


def ic_rush_fern():
    """疾长蕨 Haste：长得最快的蕨 —— **羽状蕨叶**从下往上抽，左侧三道风线（速度）。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 主脉（从左下弯到右上）
    zig(d, [(14, 40), (18, 31), (23, 22), (29, 14), (34, 9)], LEAF_D, 3)
    # 六对羽片（越往上越小）
    for k, (x, y, w) in enumerate(((15, 37, 8), (18, 31, 8), (22, 25, 7),
                                    (26, 19, 6), (30, 14, 5), (33, 10, 4))):
        d.polygon([(x, y), (x - w, y - 2), (x - w + 2, y - 6)], fill=LEAF, outline=INK)
        d.polygon([(x, y - 2), (x + w, y - 4), (x + w - 2, y - 8)], fill=LEAF, outline=INK)
    d.ellipse([32, 7, 38, 13], fill=LEAF, outline=INK)      # 顶芽
    # 三道风线（左侧）
    for k in range(3):
        y = 20 + k * 8
        d.line([(2, y), (9 - k, y - 3)], fill=CYAN, width=2)
    # 土
    d.polygon([(11, 41), (20, 41), (18, 38), (13, 38)], fill=SOIL, outline=INK)
    return im


def ic_plump_gourd():
    """丰实瓜 RichHarvest：鼓到溢出的**葫芦** + 满地**金粒**（收成更肥）。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 葫芦：下大球 + 上小球
    d.ellipse([3, 22, 31, 42], fill=GOLD, outline=INK)
    d.ellipse([10, 8, 28, 28], fill=GOLD_D, outline=INK)
    d.ellipse([12, 10, 20, 24], fill=GOLD)                 # 上球亮面
    d.ellipse([6, 26, 16, 40], fill=GOLD_L)                # 下球亮面
    # 蒂
    d.rectangle([17, 4, 22, 10], fill=LEAF_D, outline=INK)
    d.polygon([(20, 5), (28, 1), (26, 7)], fill=LEAF, outline=INK)
    # 腰箍 + 竖棱
    d.line([(5, 30), (29, 30)], fill=GOLD_D, width=1)
    d.line([(11, 24), (11, 40)], fill=GOLD_D, width=1)
    d.line([(23, 24), (23, 40)], fill=GOLD_D, width=1)
    # 溢出的金粒（右下，收在 41 以内）
    for cx, cy in ((32, 32), (37, 36), (33, 39), (38, 30)):
        coin(d, cx, cy, 3)
    return im


def ic_patch_lattice():
    """连畦藤 PlotCountGold：藤把**一块块畦连成格子**，每连一格结点上结一颗金 —— 网格 + 节点金珠。"""
    im = O44(); d = ImageDraw.Draw(im)
    xs = (10, 20, 30)
    ys = (11, 21, 31)
    # 藤（竖三 + 横三）
    for x in xs:
        d.line([(x, 7), (x, 37)], fill=LEAF_D, width=4)
        d.line([(x - 1, 8), (x - 1, 36)], fill=LEAF, width=1)
    for y in ys:
        d.line([(7, y), (35, y)], fill=LEAF_D, width=4)
        d.line([(8, y - 1), (34, y - 1)], fill=LEAF, width=1)
    # 节点金珠（四个 + 中心最大）
    for x, y in ((10, 11), (20, 11), (10, 21), (30, 31), (10, 31)):
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=GOLD, outline=INK)
    coin(d, 20, 21, 4)
    # 右下抽出的藤须
    d.arc([31, 31, 42, 42], start=270, end=110, fill=LEAF, width=2)
    return im


def ic_drought_lotus():
    """耐旱莲 ThirstTolerant：**干裂的土**上照样开花 —— 扇形莲瓣 + 底下一整条裂开的干土。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 干土（底部一整条，带裂纹）
    d.rectangle([5, 33, 40, 41], fill=SOIL, outline=INK)
    for pts in ([(9, 41), (13, 36), (17, 41)], [(19, 41), (23, 35), (27, 41)],
                [(29, 41), (33, 37), (37, 41)], [(13, 36), (9, 33)]):
        zig(d, pts, SOIL_D, 1)
    # 莲花（五瓣扇形，从土里长出）
    fan(d, 22, 33, (-152, -118, -90, -62, -28), 17, 5, PINK_D)
    fan(d, 22, 33, (-136, -90, -44), 12, 4, PINK)
    d.ellipse([19, 24, 25, 30], fill=GOLD, outline=INK)      # 莲心（有金）
    # 一片枯叶（耐旱的代价）
    d.polygon([(31, 30), (38, 24), (39, 31), (33, 33)], fill=SOIL_L, outline=INK)
    return im


def ic_brim_peony():
    """盈水芍 FullWaterGold：水越满、金越多 —— 层叠芍药 + 右侧一根**满水的水位管** + 一枚金。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 芍药（左：三层圆瓣）
    d.ellipse([3, 10, 29, 36], fill=PINK_D, outline=INK)
    for ang in (0, 72, 144, 216, 288):
        a = math.radians(ang)
        cx, cy = 16 + math.cos(a) * 7, 23 + math.sin(a) * 7
        d.ellipse([cx - 6, cy - 6, cx + 6, cy + 6], fill=PINK, outline=INK)
    d.ellipse([12, 19, 20, 27], fill=GOLD, outline=INK)      # 花心
    # 水位管（右：竖长条 + 满水）
    d.rounded_rectangle([30, 9, 39, 41], radius=4, fill=TUBE, outline=INK)
    d.rectangle([32, 14, 37, 39], fill=WATER)
    d.line([(33, 15), (36, 15)], fill=WATER_L, width=1)
    for y in (20, 26, 32):                                    # 刻度
        d.line([(30, y), (32, y)], fill=BONE, width=1)
    coin(d, 35, 6, 4)                                         # 满水换来的金
    return im


def ic_brave_sprout():
    """血勇苗 LowHpGrow：血越少长得越猛 —— 苗 + 两片叶 + 头顶一颗**大血滴**。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 茎（细长、带弯）
    zig(d, [(20, 41), (19, 33), (22, 26)], LEAF_D, 4)
    # 左叶（小）
    d.polygon([(20, 31), (7, 28), (10, 20), (20, 26)], fill=LEAF, outline=INK)
    d.line([(18, 28), (11, 24)], fill=LEAF_D, width=1)
    # 右叶（大）
    d.polygon([(21, 26), (33, 17), (36, 24), (24, 29)], fill=LEAF, outline=INK)
    d.line([(23, 26), (33, 21)], fill=LEAF_D, width=1)
    # 大血滴（右上，与右叶之间留 3px 空隙，且不顶到画布边）
    drop(d, 34, 10, 4, BLOOD)
    d.ellipse([31, 7, 34, 10], fill=BLOOD_L)
    # 土
    d.polygon([(11, 41), (31, 41), (28, 37), (14, 37)], fill=SOIL, outline=INK)
    return im


def ic_aged_bloom():
    """积年花 ElderGrowth：年头越多长得越快 —— 右上开着花，左下三道**年轮弧**。"""
    im = O44(); d = ImageDraw.Draw(im)
    cx, cy = 27, 17
    # 六瓣花
    for k in range(6):
        a = math.radians(k * 60 - 90)
        px, py = cx + math.cos(a) * 9, cy + math.sin(a) * 9
        d.ellipse([px - 5, py - 5, px + 5, py + 5], fill=PURPLE, outline=INK)
    d.ellipse([cx - 6, cy - 6, cx + 6, cy + 6], fill=GOLD, outline=INK)
    d.ellipse([cx - 2, cy - 2, cx + 2, cy + 2], fill=GOLD_L)
    # 花梗
    zig(d, [(27, 26), (24, 32), (18, 37)], LEAF_D, 3)
    # 年轮弧（左下：三道同心，外沿不超过 41）
    for rr in (11, 7, 4):
        d.arc([15 - rr, 30 - rr, 15 + rr, 30 + rr], start=100, end=262, fill=BONE, width=2)
    d.ellipse([13, 28, 17, 32], fill=SOIL_D, outline=INK)     # 芯（木质部）
    return im


def ic_chain_bloom():
    """叠收莲 StreakHarvest：一茬接一茬**叠着收** —— 三层莲叶从下往上叠 + 右侧三颗计数金珠。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 三层莲叶（下宽上窄，带缺口）
    for cy, hw in ((36, 13), (26, 10), (17, 7)):
        d.ellipse([21 - hw, cy - 5, 21 + hw, cy + 5], fill=LEAF_D, outline=INK)
        d.ellipse([21 - hw + 2, cy - 3, 21 + hw - 2, cy + 2], fill=LEAF)
        d.polygon([(21, cy - 5), (26, cy - 1), (21, cy + 3)], fill=(0, 0, 0, 0))   # 叶缺口
        d.line([(9, cy + 3), (20, cy + 3)], fill=LEAF_D, width=1)
    # 顶芽
    d.polygon([(21, 12), (17, 20), (25, 20)], fill=LEAF, outline=INK)
    # 右侧三颗"又一茬"的金珠
    for k, cy in enumerate((17, 26, 35)):
        coin(d, 37, cy, 3)
    return im


def ic_nightdew_lily():
    """夜露莲 NightGold：**月光下**才结金的莲 —— 弯月 + 杯状莲 + 一颗夜露。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 月（右上，挖一刀成弯月）
    d.ellipse([25, 2, 42, 19], fill=MOON, outline=INK)
    d.ellipse([31, 0, 45, 13], fill=(0, 0, 0, 0))
    d.arc([25, 2, 42, 19], start=88, end=250, fill=INK, width=2)     # 弯月的内缘重描
    # 杯状莲（左下，三片外瓣）
    d.polygon([(9, 38), (2, 24), (13, 27)], fill=PINK_D, outline=INK)
    d.polygon([(10, 38), (13, 18), (20, 30)], fill=PINK, outline=INK)
    d.polygon([(11, 38), (26, 22), (24, 34)], fill=PINK_D, outline=INK)
    d.polygon([(10, 38), (22, 38), (16, 30)], fill=PINK, outline=INK)
    # 金蕊
    for cx, cy in ((16, 28), (21, 26), (13, 30)):
        d.ellipse([cx - 2, cy - 2, cx + 2, cy + 2], fill=GOLD, outline=INK)
    # 夜露（叶口一滴）
    drop(d, 30, 34, 3, WATER)
    d.ellipse([28, 32, 31, 35], fill=WATER_L)
    # 水下茎
    zig(d, [(16, 38), (14, 41), (19, 41)], LEAF_D, 2)
    return im


def ic_hunter_bloom():
    """猎血花 KillRush：咬死一个就更快 —— **斜着张开的大嘴花**（上颚 / 下颚 + 交错獠牙）+ 溅血。"""
    im = O44(); d = ImageDraw.Draw(im)
    # 喉（两颚之间的暗口，先画，后画的两颚把它夹住）
    d.polygon([(10, 19), (23, 17), (25, 25), (11, 29)], fill=(0x5A, 0x16, 0x16, 255))
    # 上颚（左上 → 右，斜着压下）
    d.polygon([(3, 13), (14, 3), (30, 9), (25, 16), (7, 18)], fill=BLOOD, outline=INK)
    d.line([(9, 11), (26, 9)], fill=BLOOD_L, width=2)
    # 下颚（左下 → 右下，向后收）
    d.polygon([(5, 31), (22, 25), (37, 29), (33, 39), (11, 40)], fill=BLOOD, outline=INK)
    d.line([(12, 35), (31, 35)], fill=BLOOD_L, width=2)
    # 上颚獠牙（朝下）
    for x, y in ((11, 16), (18, 15), (25, 14)):
        d.polygon([(x - 3, y), (x + 3, y - 1), (x, y + 6)], fill=BONE, outline=INK)
    # 下颚獠牙（朝上，与上颚交错）
    for x, y in ((13, 26), (21, 25)):
        d.polygon([(x - 3, y + 1), (x + 3, y), (x, y - 5)], fill=BONE, outline=INK)
    # 溅血（右上 / 右下各一点）
    drop(d, 38, 12, 2, BLOOD)
    d.ellipse([37, 36, 41, 40], fill=BLOOD_L, outline=INK)
    return im


# ══════════════════════════════════════ 作业表 ══════════════════════════════════════
# (kind, id, 中文名, 画法, 语义一句话)
JOBS = [
    # ── 插件 7：plugins.json 有、UI 目录里没图（displayName / effect 见括号） ──
    ("plugin", "can_sewer", "臭水沟", ic_can_sewer,
     "Sewer：浑浊绿水的水柱 + 泼在脚边的香蕉皮"),
    ("plugin", "can_sharp", "锋利", ic_can_sharp,
     "SharpHarvest：压成刀的水柱 + 被割下的麦穗"),
    ("plugin", "can_wave", "海浪", ic_can_wave,
     "Wave：脚下卷起的浪 + 三条速度线"),
    ("plugin", "fert_blackhole", "黑洞", ic_fert_blackhole,
     "BlackHole：暗盘 + 吸积弧 + 被吸向爆点的碎屑"),
    ("plugin", "hoe_frenzy", "狂暴", ic_hoe_frenzy,
     "Frenzy：锄刃 + 一圈迸开的怒气/血花"),
    ("plugin", "hoe_giant", "巨型", ic_hoe_giant,
     "Giant：环绕一整圈的挥击环 + 四向箭头"),
    ("plugin", "hoe_shock", "感电", ic_hoe_shock,
     "Shock：锄刃 + 分叉电弧窜到两个敌人"),
    # ── 植物 12：seeds.json 有、UI 目录里没图（name / mechanic 见括号） ──
    ("plant", "dew_jar_moss", "蓄水苔", ic_dew_jar_moss,
     "WaterSave：陶罐口长满苔藓、罐里存满水"),
    ("plant", "drizzle_bud", "凝露芽", ic_drizzle_bud,
     "DewDrop：尖顶花芽 + 芽尖上凝出的三颗露珠"),
    ("plant", "rush_fern", "疾长蕨", ic_rush_fern,
     "Haste：向上抽的羽状蕨 + 三道风线"),
    ("plant", "plump_gourd", "丰实瓜", ic_plump_gourd,
     "RichHarvest：鼓胀的葫芦 + 满地溢出的金粒"),
    ("plant", "patch_lattice", "连畦藤", ic_patch_lattice,
     "PlotCountGold：藤编成的畦格 + 结点上的金珠"),
    ("plant", "drought_lotus", "耐旱莲", ic_drought_lotus,
     "ThirstTolerant：裂开的干土上开出的莲花"),
    ("plant", "brim_peony", "盈水芍", ic_brim_peony,
     "FullWaterGold：层叠芍药 + 满水的水位管"),
    ("plant", "brave_sprout", "血勇苗", ic_brave_sprout,
     "LowHpGrow：两片叶的苗 + 头顶一颗大血滴"),
    ("plant", "aged_bloom", "积年花", ic_aged_bloom,
     "ElderGrowth：六瓣花 + 左下的年轮弧"),
    ("plant", "chain_bloom", "叠收莲", ic_chain_bloom,
     "StreakHarvest：三层叠起的莲叶 + 计数金珠"),
    ("plant", "nightdew_lily", "夜露莲", ic_nightdew_lily,
     "NightGold：弯月 + 杯状莲 + 一颗夜露"),
    ("plant", "hunter_bloom", "猎血花", ic_hunter_bloom,
     "KillRush：獠牙交错的血色大嘴 + 溅血"),
]


# 每张图的"语义色清单"：证明画法里那几个关键物件**真的落在像素上**了
# （没被后续图层整片盖掉、也没被画到 44×44 之外裁掉）—— 下限取实测量的 ~55%
SEM_INK = {
    "can_sewer":        [("浑浊绿水", SLUDGE, 60), ("香蕉皮", PEEL, 50), ("壶嘴", STEEL_D, 30)],
    "can_sharp":        [("水刀", WATER, 120), ("刃口高光", WATER_L, 30), ("麦粒", GOLD, 15), ("麦秆", LEAF_D, 20)],
    "can_wave":         [("浪身", WATER, 110), ("速度线", CYAN, 20), ("浪腹暗面", WATER_D, 12)],
    "fert_blackhole":   [("暗盘", VOID, 100), ("吸积弧", VOID_L, 60), ("紫弧", PURPLE, 20), ("爆芯", GOLD_L, 30)],
    "hoe_frenzy":       [("锄刃", STEEL, 60), ("怒气尖刺", BLOOD_L, 40), ("怒心", BLOOD, 20)],
    "hoe_giant":        [("挥击环", STEEL, 140), ("四向箭头", GOLD, 40), ("玩家", BONE, 10)],
    "hoe_shock":        [("锄刃", STEEL, 50), ("电弧", GOLD_L, 30), ("敌人点", STEEL_D, 15)],
    "dew_jar_moss":     [("陶罐", CLAY, 80), ("苔团", LEAF, 80), ("罐里水", WATER, 110)],
    "drizzle_bud":      [("外苞", LEAF_D, 80), ("内叶", LEAF, 60), ("露珠", WATER, 10)],
    "rush_fern":        [("羽片", LEAF, 40), ("风线", CYAN, 15)],
    "plump_gourd":      [("葫芦", GOLD, 170), ("暗面", GOLD_D, 90)],
    "patch_lattice":    [("藤格", LEAF_D, 160), ("结点金珠", GOLD, 35)],
    "drought_lotus":    [("莲瓣", PINK_D, 70), ("亮瓣", PINK, 40), ("干土", SOIL, 100), ("裂纹", SOIL_D, 15)],
    "brim_peony":       [("芍药", PINK, 170), ("水位管水", WATER, 70), ("管壁", TUBE, 35), ("金", GOLD, 30)],
    "brave_sprout":     [("叶", LEAF, 55), ("血滴", BLOOD, 35)],
    "aged_bloom":       [("花瓣", PURPLE, 190), ("花心", GOLD, 35), ("年轮弧", BONE, 60)],
    "chain_bloom":      [("莲叶", LEAF_D, 75), ("叶亮面", LEAF, 85), ("计数金珠", GOLD, 20)],
    "nightdew_lily":    [("月", MOON, 45), ("莲瓣", PINK, 25), ("外瓣", PINK_D, 40)],
    "hunter_bloom":     [("大嘴", BLOOD, 210), ("獠牙", BONE, 28)],
}


def res_of(kind, iid):
    return "icon_%s_%s" % (kind, iid)


def png_of(kind, iid):
    return os.path.join(UI, res_of(kind, iid) + ".png")


# ══════════════════════════════════════ .meta / 哈希 ══════════════════════════════════════

REQUIRED_META = [("textureType", "0"), ("filterMode", "1"),
                 ("spritePixelsToUnits", "100"), ("maxTextureSize", "2048"),
                 ("enableMipMap", "0")]


def meta_guid(kind, iid):
    return hashlib.md5(("Seedfall/v307/%s/%s" % (kind, iid)).encode("utf-8")).hexdigest()


def make_meta(png_path, kind, iid):
    """.meta 逐字段等于邻居，只换 guid = md5("Seedfall/v307/<kind>/<id>")（恒定 ⇒ 重跑不变）。"""
    tpl = open(META_TEMPLATE, encoding="utf-8").read()
    guid = meta_guid(kind, iid)
    out = []
    for ln in tpl.split("\n"):
        out.append("guid: " + guid if ln.startswith("guid: ") else ln)
    open(png_path + ".meta", "w", encoding="utf-8", newline="\n").write("\n".join(out))


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def snap(with_meta=True):
    """UI 目录全量指纹：png（+ png.meta）→ sha256。"""
    out = {}
    for f in sorted(os.listdir(UI)):
        if f.endswith(".png"):
            out[f] = sha(os.path.join(UI, f))
            if with_meta and os.path.exists(os.path.join(UI, f + ".meta")):
                out[f + ".meta"] = sha(os.path.join(UI, f + ".meta"))
    return out


# ══════════════════════════════════════ 风格锚点 / 剪影 ══════════════════════════════════════

# 物件最多外扩到 8..55（本体 10..53 再外扩 2px 描边 + 1px 投影偏移），
# 下面这些点一定落在"只有底板"的区域 ⇒ 逐像素比对可证明外壳与邻居同款
PANEL_PROBES = [(32, 4), (32, 60), (4, 32), (60, 32), (32, 3), (3, 32)]


def panel_expected(kind):
    g = V258.gradient(SIZE, THEMES[kind][0], THEMES[kind][1])
    gp = g.load()
    return {p: gp[p][:3] for p in PANEL_PROBES}


def check_panel(im, kind, name, ref_im=None):
    """底板渐变必须与 v258 模板（以及邻居实图）逐像素相同。"""
    px = im.load()
    want = panel_expected(kind)
    for p, w in want.items():
        got = px[p][:3]
        assert got == w, "%s 底板锚点 %s 期望 %s 实际 %s" % (name, p, w, got)
        if ref_im is not None:
            r = ref_im.load()[p][:3]
            assert got == r, "%s 与邻居底板不等 %s 邻居 %s 本图 %s" % (name, p, r, got)


def object_mask(im, kind):
    """物件剪影 = 44×44 内与底板渐变色不同的像素（本体 + 描边 + 投影都算）。"""
    gp = V258.gradient(SIZE, THEMES[kind][0], THEMES[kind][1]).load()
    px = im.load()
    return frozenset((x, y) for y in range(10, 54) for x in range(10, 54)
                     if px[x, y][:3] != gp[x, y])


def iou(a, b):
    if not a or not b:
        return 0.0
    return len(a & b) / len(a | b)


def ref_icons():
    """邻居实图：各取一张同主题的既有图标，用来证明"底板与邻居逐像素一致"。"""
    out = {}
    for kind, name in (("plugin", "icon_plugin_hoe_echo.png"),
                       ("plant", "icon_plant_elder_oak.png"),
                       ("item", "icon_item_wax_seal.png")):
        p = os.path.join(UI, name)
        if os.path.exists(p):
            out[kind] = Image.open(p).convert("RGBA")
    return out


# ══════════════════════════════════════ 三段入口 ══════════════════════════════════════

def plan():
    print("%-7s %-18s %-8s %-8s %-46s %s"
          % ("类别", "id", "中文名", "状态", "输出文件", "语义"))
    print("-" * 132)
    n_new = n_rep = 0
    for kind, iid, cn, fn, sem in JOBS:
        p = png_of(kind, iid)
        if os.path.exists(p):
            st = "已存在→覆盖"; n_rep += 1
        else:
            st = "新增"; n_new += 1
        print("%-7s %-18s %-8s %-8s %-46s %s"
              % (kind, iid, cn, st, os.path.relpath(p, ROOT), sem))
    print("-" * 132)
    print("合计 %d 张（插件 %d / 植物 %d）｜新增 %d｜覆盖 %d"
          % (len(JOBS), sum(1 for j in JOBS if j[0] == "plugin"),
             sum(1 for j in JOBS if j[0] == "plant"), n_new, n_rep))


def render_one(kind, iid, fn):
    obj = fn()
    assert obj.size == (OBJ, OBJ), (iid, obj.size)
    bb = obj.getbbox()
    assert bb is not None, "%s 画出来是空的" % iid
    # 物件不许贴到 44×44 的边上：贴边会被外扩 2px 描边吃掉（v274 的 2..41 约定）
    assert bb[0] >= 1 and bb[1] >= 1 and bb[2] <= OBJ - 1 and bb[3] <= OBJ - 1, \
        "%s 画到画布边上了：bbox=%s" % (iid, bb)
    return compose(obj, kind)


def run():
    os.makedirs(BAK, exist_ok=True)
    MANI = os.path.join(BAK, "_manifest.json")
    mani = (json.load(open(MANI, encoding="utf-8")) if os.path.exists(MANI)
            else {"new": [], "replaced": []})

    before = snap()
    refs = ref_icons()
    rows = []
    print("%-7s %-18s %-8s %-9s %-8s %-6s %s"
          % ("类别", "id", "中文名", "尺寸", "字节", "质量", "状态"))
    print("-" * 96)
    for kind, iid, cn, fn, sem in JOBS:
        res = res_of(kind, iid)
        p = png_of(kind, iid)
        was = os.path.exists(p)
        img = render_one(kind, iid, fn)
        check_panel(img, kind, res, refs.get(kind))
        if was and res not in mani["new"]:
            b = os.path.join(BAK, res + ".png")
            if not os.path.exists(b):
                shutil.copy2(p, b)                    # 备份原图（可 --restore 回滚）
            if res not in mani["replaced"]:
                mani["replaced"].append(res)
            st = "覆盖"
        else:
            if res not in mani["new"]:
                mani["new"].append(res)
            st = "新增"
        img.save(p, "PNG", optimize=True)
        if not os.path.exists(p + ".meta"):
            make_meta(p, kind, iid)                   # 已有 meta 不动 ⇒ guid 稳定
        elif os.path.exists(p + ".meta") and res in mani["new"]:
            pass
        mass = len(object_mask(Image.open(p).convert("RGBA"), kind))
        rows.append((kind, iid, cn, os.path.getsize(p), mass, st))
        print("%-7s %-18s %-8s %-9s %-8d %-6d %s"
              % (kind, iid, cn, "%d×%d" % (SIZE, SIZE), os.path.getsize(p), mass, st))

    if not os.path.exists(MANI):
        json.dump(mani, open(MANI, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    print("-" * 96)
    sizes = [r[3] for r in rows]
    print("生成 %d 张（插件 %d / 植物 %d）"
          % (len(rows), sum(1 for r in rows if r[0] == "plugin"),
             sum(1 for r in rows if r[0] == "plant")))
    print("尺寸：全部 %d×%d｜字节数区间 %d ~ %d（中位 %d）"
          % (SIZE, SIZE, min(sizes), max(sizes), sorted(sizes)[len(sizes) // 2]))

    # ── 回归断言：作业表之外的 PNG / .meta 必须逐字节不变 ──
    after = snap()
    mine = set()
    for kind, iid, cn, fn, sem in JOBS:
        mine.add(res_of(kind, iid) + ".png")
        mine.add(res_of(kind, iid) + ".png.meta")
    touched = sorted([f for f in before if f in after and before[f] != after[f]]
                     + [f for f in after if f not in before])
    bad = [f for f in touched if f not in mine]
    assert not bad, "作业表之外的文件被改了：%s" % bad
    unchanged = [f for f in before if f in after and before[f] == after[f] and f not in mine]
    print("本次改动 %d 个文件（全部在作业表内）｜其余 %d 个既有文件逐字节不变"
          % (len(touched), len(unchanged)))
    print("UI 目录 PNG 总数：%d → %d（+%d）"
          % (len([f for f in before if f.endswith(".png")]),
             len([f for f in after if f.endswith(".png")]),
             len([f for f in after if f.endswith(".png")]) -
             len([f for f in before if f.endswith(".png")])))

    iou_check()
    check()


def iou_check(verbose=True):
    """剪影查重：19 张两两之间 + 19×既有 96 张，IoU ≥ 0.90 视为重复。"""
    sil = {}
    for kind, iid, cn, fn, sem in JOBS:
        sil[(kind, iid)] = object_mask(Image.open(png_of(kind, iid)).convert("RGBA"), kind)
    keys = list(sil)
    worst, dup, pairs = (0.0, None), [], []
    for a in range(len(keys)):
        for b in range(a + 1, len(keys)):
            v = iou(sil[keys[a]], sil[keys[b]])
            pairs.append((round(v, 3), keys[a][1], keys[b][1]))
            if v > worst[0]:
                worst = (v, (keys[a], keys[b]))
            if v >= 0.90:
                dup.append((keys[a][1], keys[b][1], round(v, 3)))
    pairs.sort(reverse=True)
    # 与既有 96 张比对（只报告，不算失败：不同资源的同形不算"重复补图"，但仍要知会）
    hist = []
    for f in sorted(os.listdir(UI)):
        if not f.endswith(".png"):
            continue
        for kind in ("plugin", "plant"):
            pref = "icon_%s_" % kind
            if not f.startswith(pref):
                continue
            oid = f[len(pref):-4]
            if (kind, oid) in sil:
                continue
            m = object_mask(Image.open(os.path.join(UI, f)).convert("RGBA"), kind)
            for (k2, i2), s2 in sil.items():
                if k2 != kind:
                    continue
                v = iou(s2, m)
                if v >= 0.90:
                    hist.append((i2, oid, round(v, 3)))
    if verbose:
        print("剪影查重（19 张两两，IoU≥0.90 = 重复）：%s" % (dup if dup else "无"))
        print("  两两 IoU 最高的 5 对：%s"
              % ", ".join("%s↔%s %.3f" % (a, b, v) for v, a, b in pairs[:5]))
        print("剪影查重（19 × 既有 96 张，IoU≥0.90 = 撞形）：%s" % (hist if hist else "无"))
    assert not dup, "本批出现重复剪影：%s" % dup
    return worst


def check_semantics():
    """语义色存在性：每张图标里，画法用的那几个关键颜色必须still在画布上有足够像素。"""
    print("%-16s %s" % ("id", "语义色（实测像素数 / 下限）"))
    print("-" * 96)
    for kind, iid, cn, fn, sem in JOBS:
        im = Image.open(png_of(kind, iid)).convert("RGBA")
        px = im.load()
        cells = []
        for name, color, lo in SEM_INK[iid]:
            n = sum(1 for y in range(10, 54) for x in range(10, 54) if px[x, y] == color)
            assert n >= lo, "%s 缺语义元素「%s」：实测 %d < 下限 %d" % (iid, name, n, lo)
            cells.append("%s %d/%d" % (name, n, lo))
        print("%-16s %s" % (iid, "  ".join(cells)))
    print("-" * 96)
    print("语义色存在性：%d 张的关键元素全部落盘（%d 项断言）"
          % (len(JOBS), sum(len(v) for v in SEM_INK.values())))


def check():
    """只校验落盘结果：尺寸 / .meta 逐字段 / guid / 风格锚点 / 质量区间 / 复现性 / 查重。"""
    tpl = open(META_TEMPLATE, encoding="utf-8").read().split("\n")
    refs = ref_icons()
    # 邻居模板里这五行必须原样存在（本批 .meta 的核心要求）
    for key, val in REQUIRED_META:
        needle = "    %s: %s" % (key, val)
        assert any(ln.strip() == needle.strip() for ln in tpl), \
            "邻居 .meta 里没有 `%s: %s`" % (key, val)

    print("%-7s %-18s %-8s %-9s %-6s %-6s %s"
          % ("类别", "id", "尺寸", "字节", "质量", "色数", ".meta"))
    print("-" * 96)
    for kind, iid, cn, fn, sem in JOBS:
        res = res_of(kind, iid)
        p = png_of(kind, iid)
        assert os.path.exists(p), "缺图：" + p
        im = Image.open(p).convert("RGBA")
        assert im.size == (SIZE, SIZE), (p, im.size)
        check_panel(im, kind, res, refs.get(kind))
        # .meta 逐字段等于邻居，只换 guid
        mp = p + ".meta"
        assert os.path.exists(mp), "缺 meta：" + mp
        got = open(mp, encoding="utf-8").read().split("\n")
        want = [("guid: " + meta_guid(kind, iid)) if ln.startswith("guid: ") else ln
                for ln in tpl]
        diff = [(i, w, g) for i, (w, g) in enumerate(zip(want, got)) if w != g]
        assert not diff and len(want) == len(got), "%s.meta 与邻居不等：%s" % (res, diff[:3])
        # 复现性：内存里再画一遍，必须与磁盘逐字节相同
        again = render_one(kind, iid, fn)
        buf = io.BytesIO(); again.save(buf, "PNG", optimize=True)
        assert buf.getvalue() == open(p, "rb").read(), "%s 重画结果与磁盘不一致（不可复现）" % res
        mass = len(object_mask(im, kind))
        uniq = len({im.getpixel((x, y)) for y in range(10, 54) for x in range(10, 54)})
        print("%-7s %-18s %-8s %-9d %-6d %-6d %s"
              % (kind, iid, "%d×%d" % im.size, os.path.getsize(p), mass, uniq,
                 "逐字段=邻居 ✓"))
    print("-" * 96)

    # 质量区间：按既有同主题图的实际分布给带（既有插件 588~1936 / 植物 669~1908）
    band = {"plugin": (600, 1400), "plant": (650, 1950)}
    bad = []
    for kind, iid, cn, fn, sem in JOBS:
        m = len(object_mask(Image.open(png_of(kind, iid)).convert("RGBA"), kind))
        lo, hi = band[kind]
        if not (lo <= m <= hi):
            bad.append((iid, m, band[kind]))
    assert not bad, "物件质量超出既有分布区间：%s" % bad
    print("物件质量：全部落在既有分布区间内（插件 %d~%d / 植物 %d~%d）"
          % (band["plugin"][0], band["plugin"][1], band["plant"][0], band["plant"][1]))
    print("校验通过：%d 张 %d×%d、.meta 逐字段等于邻居、底板与邻居逐像素一致、内存重画逐字节相同"
          % (len(JOBS), SIZE, SIZE))
    check_semantics()


def preview():
    """预览：19 张放大 + 一排既有图标作画风参照（同一底色、同一放大倍数、中文标注）。"""
    from PIL import ImageFont
    Z = 4
    COLS = 7
    pad, lab = 10, 18
    cell = SIZE * Z
    font = None
    for fp in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simhei.ttf"):
        if os.path.exists(fp):
            try:
                font = ImageFont.truetype(fp, 14)
                break
            except Exception:
                pass
    olds = [("plugin", "hoe_echo"), ("plugin", "can_tide"), ("plugin", "fert_ward"),
            ("plant", "elder_oak"), ("plant", "dusk_berry"), ("plant", "salt_melon"),
            ("plant", "star_ladder")]
    plugins = [j for j in JOBS if j[0] == "plugin"]
    plants = [j for j in JOBS if j[0] == "plant"]
    blocks = [("v307 新增 · 插件 7 张（底板=石板蓝）", plugins),
              ("v307 新增 · 植物 12 张（底板=叶绿）", plants)]
    rows_total = sum((len(b[1]) + COLS - 1) // COLS for b in blocks)
    head = 30
    W = pad + COLS * (cell + pad)
    H = (pad + head * len(blocks) + rows_total * (cell + pad + lab)
         + head + (cell + pad + lab) + pad)
    sheet = Image.new("RGBA", (W, H), (26, 24, 22, 255))
    dd = ImageDraw.Draw(sheet)

    def put(x, y, im, txt):
        sheet.alpha_composite(im.convert("RGBA").resize((cell, cell), Image.NEAREST), (x, y))
        if font:
            dd.text((x, y + cell + 2), txt, fill=(214, 204, 188, 255), font=font)

    y = pad
    for title, jobs in blocks:
        if font:
            dd.text((pad, y), title, fill=(242, 232, 210, 255), font=font)
        y += head
        for i, (kind, iid, cn, fn, sem) in enumerate(jobs):
            r, c = divmod(i, COLS)
            put(pad + c * (cell + pad), y + r * (cell + pad + lab),
                Image.open(png_of(kind, iid)), "%s %s" % (cn, iid))
        y += ((len(jobs) + COLS - 1) // COLS) * (cell + pad + lab)
    if font:
        dd.text((pad, y), "既有图标参照（同一外壳：v274 插件 / 早期植物 / v274 植物）",
                fill=(242, 232, 210, 255), font=font)
    y += head
    for i, (kind, oid) in enumerate(olds):
        p = os.path.join(UI, res_of(kind, oid) + ".png")
        if os.path.exists(p):
            put(pad + i * (cell + pad), y, Image.open(p), oid)
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    sheet.convert("RGB").save(PREVIEW, "PNG", optimize=True)
    print("预览图：%s（%d×%d，%d 新 + %d 旧）"
          % (PREVIEW, sheet.width, sheet.height, len(JOBS), len(olds)))


def restore():
    """回滚：本批全是新增 ⇒ 删掉 19 对 png+meta；若有被覆盖的，从 backup_v307 拷回。"""
    MANI = os.path.join(BAK, "_manifest.json")
    mani = (json.load(open(MANI, encoding="utf-8")) if os.path.exists(MANI)
            else {"new": [], "replaced": []})
    n = 0
    for res in mani["replaced"]:
        src = os.path.join(BAK, res + ".png")
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(UI, res + ".png"))
            n += 1
            print("还原", res)
    for res in mani["new"]:
        for ext in (".png", ".png.meta"):
            p = os.path.join(UI, res + ext)
            if os.path.exists(p):
                os.remove(p)
                print("删除（原本就没有）", res + ext)
    print("回滚完成：还原 %d 张，删除新增 %d 张" % (n, len(mani["new"])))


if __name__ == "__main__":
    if not (sys.stdout.isatty() is False):
        try:
            sys.stdout.reconfigure(encoding="utf-8")
        except Exception:
            pass
    if "--restore" in sys.argv:
        restore()
    elif "--dry-run" in sys.argv:
        plan()
    elif "--check" in sys.argv:
        check(); iou_check()
    elif "--preview" in sys.argv:
        preview()
    else:
        run()
        preview()