# -*- coding: utf-8 -*-
"""v274：补齐"没有匹配图标"的物品 —— 只画**缺失/占位**的那些，复用 v258 的画风外壳。

════════════════════════════════════════════════════════════════════════
★ 复现参数（全部写死，脚本内**没有任何随机源** ⇒ 两次运行逐字节相同）★
  画布     : 64×64 RGBA，PNG
  外壳     : 直接 import `restyle_icons_v258`，用它的常量与函数 ——
             SIZE=64 / RADIUS=12 / OBJ=44 / OUTLINE=#2B211B /
             THEMES{item,plant,plugin} 的上下渐变 / rounded_mask / gradient / outline_of
  合成顺序 : 与 `v258.restyle()` 的 ①~④ 完全一致：
             ① 渐变底板（圆角 12）
             ② 左上 1px 内高光 rgba(255,255,255,70)
             ③ 物件：外扩 2px 深描边 + 右下 1px 投影(12,9,6,120) + 居中 44×44
             ④ 外框 2px OUTLINE
  物件区   : 44×44 透明画布，图元一律画在 3..40 之间（给 2px 描边留位）
  物件画法 : 平涂 + 1 层亮面(左上) + 1 层暗面(右下) + ≤2 个 accent 点 + 深色内部线 ——
             与既有道具/植物图标同一套"硬边像素"画法（不抗锯齿）
  输出     : Assets/Resources/Art/UI/icon_<kind>_<id>.png
             既有 PNG 先备份到 Tools/icon_gen/backup_v274/（可 --restore 回滚）
             .meta：**已存在的沿用原文件**（guid 不变）；缺的按邻居模板生成
             （textureType=0 / filterMode=1 / spritePixelsToUnits=100 / maxTextureSize=2048
               / DefaultTexturePlatform maxTextureSize=256 —— 与 icon_item_*.png.meta 逐字段相同）

★ 用法 ★
  python Tools/icon_gen/gen_missing_icons_v274.py            # 生成
  python Tools/icon_gen/gen_missing_icons_v274.py --dry-run  # 只打印计划
  python Tools/icon_gen/gen_missing_icons_v274.py --restore  # 从 backup_v274 回滚

★ 这一版补的是谁（详见 --dry-run 输出）★
  A 类：**没有任何图标资源**（PNG 缺、`TableDatabase.ItemIconMasks` 也没有）
        ⇒ 界面只能画品质色块（纯绿/纯橙方块）。4 件道具。
  B 类：**图标是通用几何占位符号**（环/星/菱/条/X/点阵），与名称和功能毫无对应，
        色相还是与道具/植物那套不一致的高饱和霓虹。21 件插件。
  C 类：**同一张"菱形"占位剪影被复用**（剪影 IoU≥0.90 聚类得出），与名称不符：
        灰烬瓶、蜂蜜酒桶、青铜镜片、盐瓜、云兰。5 件（2 道具 + 3 植物）。
  合计 30 张。其余 202 张图标**一个字节都不动**（脚本前后会打印哈希比对）。
"""
import os
import sys
import json
import shutil
import hashlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw
import restyle_icons_v258 as V258          # ★ 画风复用：外壳常量与图元函数全在这里

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UI = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
BAK = os.path.join(ROOT, "Tools", "icon_gen", "backup_v274")
META_TEMPLATE = os.path.join(UI, "icon_item_wax_seal.png.meta")

SIZE = V258.SIZE          # 64
OBJ = V258.OBJ            # 44
RADIUS = V258.RADIUS      # 12
OUTLINE = V258.OUTLINE    # #2B211B
THEMES = V258.THEMES      # item/plant/plugin 的上下渐变

# ────────────────────────────── 调色板（与既有图标同一族） ──────────────────────────────
INK = (0x2B, 0x21, 0x1B, 255)
STEEL = (0x9A, 0xA3, 0xAD, 255)
STEEL_L = (0xCF, 0xD6, 0xDD, 255)
STEEL_D = (0x5E, 0x66, 0x70, 255)
WOOD = (0x8A, 0x6A, 0x3A, 255)
WOOD_D = (0x5E, 0x46, 0x26, 255)
WOOD_L = (0xB4, 0x92, 0x5E, 255)
WATER = (0x5B, 0xC0, 0xEB, 255)
WATER_D = (0x2E, 0x7C, 0xA8, 255)
WATER_L = (0xBF, 0xEC, 0xFF, 255)
FIRE = (0xEF, 0x7A, 0x2E, 255)
FIRE_L = (0xFF, 0xC4, 0x5E, 255)
FIRE_D = (0xA8, 0x40, 0x14, 255)
BLOOD = (0xB8, 0x38, 0x34, 255)
BLOOD_L = (0xE8, 0x6A, 0x5E, 255)
GOLD = (0xE8, 0xB6, 0x4A, 255)
GOLD_L = (0xFF, 0xE6, 0x9A, 255)
GOLD_D = (0xA8, 0x7A, 0x22, 255)
LEAF = (0x7F, 0xC0, 0x5A, 255)
LEAF_D = (0x4E, 0x7E, 0x36, 255)
SOIL = (0x8A, 0x66, 0x46, 255)
SOIL_D = (0x5E, 0x42, 0x28, 255)
SOIL_L = (0xB5, 0x92, 0x6A, 255)
BONE = (0xEC, 0xE6, 0xD6, 255)
BONE_D = (0xB8, 0xB0, 0x9C, 255)
ASH = (0x8E, 0x8C, 0x86, 255)
CYAN = (0x6F, 0xD4, 0xC0, 255)
PURPLE = (0x9A, 0x7C, 0xD8, 255)


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


# ══════════════════════════════════════ A 类：无任何图标资源（4 件道具） ══════════════════════════════════════

def ic_good_wateringcan():
    """铜洒水壶：壶身 + 长嘴(带莲蓬头) + 提手。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([7, 20, 29, 38], radius=4, fill=GOLD_D, outline=INK, width=2)   # 壶身
    d.rectangle([7, 26, 29, 29], fill=GOLD)                                             # 铜箍
    d.polygon([(26, 24), (39, 15), (41, 20), (29, 30)], fill=GOLD_D, outline=INK)       # 壶嘴
    d.polygon([(37, 11), (43, 13), (42, 21), (36, 19)], fill=STEEL, outline=INK)        # 莲蓬头
    d.arc([8, 10, 28, 28], start=185, end=355, fill=STEEL_L, width=3)                   # 提手
    d.line([(11, 23), (11, 35)], fill=GOLD_L, width=2)                                  # 高光
    for x, y in ((9, 33), (14, 34), (20, 33)):                                          # 水珠孔
        d.ellipse([x, y, x + 3, y + 3], fill=WATER)
    return im


def ic_good_fertilizer():
    """草木灰肥：粗布袋 + 扎口 + 灰堆与草屑。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(10, 17), (34, 17), (37, 38), (7, 38)], fill=SOIL, outline=INK)          # 袋身
    d.polygon([(16, 8), (28, 8), (30, 17), (14, 17)], fill=SOIL_D, outline=INK)         # 扎口
    d.line([(15, 9), (29, 9)], fill=BONE, width=2)                                      # 麻绳
    d.line([(11, 30), (33, 30)], fill=SOIL_D, width=2)                                  # 缝线
    d.line([(12, 35), (32, 35)], fill=SOIL_D, width=2)
    d.ellipse([13, 19, 31, 28], fill=ASH)                                               # 灰面
    for x, y in ((16, 22), (21, 20), (26, 23), (19, 25), (24, 26)):                     # 草屑
        d.line([(x, y), (x + 2, y - 3)], fill=LEAF_D, width=1)
    d.line([(14, 21), (18, 21)], fill=BONE_D, width=1)
    return im


def ic_good_beehive():
    """蜂箱：三层巢脾（椭圆自上而下变宽）+ 巢门 + 两只蜂。"""
    im = O44(); d = ImageDraw.Draw(im)
    for x0, y0, x1, y1 in ((5, 25, 39, 39), (8, 17, 36, 31), (12, 9, 32, 23)):           # 由下到上
        d.ellipse([x0, y0, x1, y1], fill=GOLD_D, outline=INK, width=2)
    d.arc([8, 17, 36, 31], start=200, end=340, fill=GOLD, width=1)                       # 层缝
    d.arc([12, 9, 32, 23], start=200, end=340, fill=GOLD, width=1)
    d.arc([5, 25, 39, 39], start=205, end=300, fill=GOLD_L, width=1)                     # 亮面
    d.chord([17, 29, 27, 40], start=0, end=180, fill=INK)                                # 巢门
    d.ellipse([12, 6, 16, 10], fill=INK)                                                 # 顶盖孔
    for cx, cy in ((36, 15), (7, 22)):                                                   # 两只蜂
        d.ellipse([cx - 3, cy - 2, cx + 3, cy + 3], fill=GOLD_L, outline=INK)
        d.ellipse([cx - 3, cy - 5, cx + 1, cy - 2], fill=BONE)                           # 翅
    return im


def ic_good_sprinkler():
    """环形喷灌：地上的环形水管 + 四道喷雾 + 水珠。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([5, 24, 39, 41], fill=STEEL_D, outline=INK, width=2)                      # 外环
    d.ellipse([14, 30, 30, 36], fill=(0, 0, 0, 0))                                      # 中空
    d.ellipse([14, 30, 30, 36], outline=INK, width=2)
    d.arc([6, 25, 38, 40], start=185, end=320, fill=STEEL_L, width=2)                   # 环上高光
    for x, h in ((13, 10), (20, 15), (27, 13), (33, 9)):                                # 喷雾
        d.line([(x, 30), (x, 30 - h)], fill=WATER, width=2)
    for x, y in ((11, 15), (18, 9), (25, 8), (32, 13), (36, 18)):                       # 水珠
        d.ellipse([x, y, x + 3, y + 3], fill=WATER_L)
    return im


# ══════════════════════════════════════ C 类：菱形占位剪影（2 道具 + 3 植物） ══════════════════════════════════════

def ic_ash_flask():
    """灰烬瓶：锥形瓶 + 瓶塞 + 瓶内灰烬。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(14, 38), (30, 38), (25, 24), (25, 16), (19, 16), (19, 24)], fill=BONE, outline=INK)
    d.polygon([(16, 34), (28, 34), (30, 38), (14, 38)], fill=ASH)                       # 底部灰层
    d.rectangle([17, 9, 27, 16], fill=WOOD, outline=INK, width=2)                       # 瓶塞
    d.line([(21, 18), (21, 34)], fill=BONE_D, width=2)                                  # 高光/内壁
    for x, y in ((18, 31), (24, 32), (21, 28)):                                         # 灰屑
        d.ellipse([x, y, x + 2, y + 2], fill=(0x5A, 0x58, 0x54, 255))
    return im


def ic_mead_vat():
    """蜂蜜酒桶：木桶 + 两道铁箍 + 龙头 + 一滴蜜。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([8, 12, 33, 38], radius=5, fill=WOOD, outline=INK, width=2)
    for y in (17, 27, 35):                                                              # 桶板缝
        d.line([(9, y), (32, y)], fill=WOOD_D, width=1)
    for y in (18, 31):                                                                  # 铁箍
        d.rectangle([7, y, 34, y + 3], fill=STEEL_D, outline=INK)
    d.line([(11, 14), (11, 36)], fill=WOOD_L, width=2)                                  # 亮面
    d.rectangle([32, 22, 39, 27], fill=STEEL, outline=INK)                              # 龙头
    d.ellipse([36, 27, 41, 33], fill=GOLD)                                              # 一滴蜜
    return im


def ic_bronze_lens():
    """青铜镜片：竖立的双凸透镜（亮玻璃 + 上下青铜镜筒夹持 + 反光条）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([14, 4, 30, 40], fill=WATER_L, outline=INK, width=2)                      # 镜体
    d.ellipse([17, 8, 27, 36], fill=(0xD8, 0xF2, 0xFF, 255))                            # 内亮
    d.line([(19, 12), (17, 26)], fill=BONE, width=2)                                    # 反光
    d.line([(26, 22), (27, 32)], fill=WATER_D, width=1)
    d.rectangle([11, 3, 33, 9], fill=GOLD_D, outline=INK)                               # 上镜筒
    d.rectangle([11, 35, 33, 41], fill=GOLD_D, outline=INK)                             # 下镜筒
    d.line([(13, 5), (31, 5)], fill=GOLD_L, width=1)                                    # 铜面高光
    d.line([(13, 37), (31, 37)], fill=GOLD_L, width=1)
    return im


def ic_salt_melon():
    """盐瓜：淡色瓜身 + 瓜棱 + 顶上盐粒 + 瓜蒂。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.ellipse([6, 17, 38, 39], fill=(0xDA, 0xE8, 0xC6, 255), outline=INK, width=2)
    for x in (14, 22, 30):                                                              # 瓜棱
        d.arc([x - 12, 18, x + 12, 38], start=250, end=290, fill=LEAF_D, width=2)
    d.line([(10, 24), (13, 33)], fill=(0xEF, 0xF7, 0xDF, 255), width=2)                 # 亮面
    d.rectangle([20, 10, 24, 18], fill=LEAF_D, outline=INK)                             # 瓜蒂
    d.polygon([(24, 12), (33, 8), (31, 13)], fill=LEAF)
    for x, y in ((15, 19), (22, 16), (28, 18), (19, 23)):                               # 盐粒
        d.rectangle([x, y, x + 2, y + 2], fill=BONE)
    return im


def ic_cloud_orchid():
    """云兰：五瓣云白花瓣围成一圈 + 金心 + 花茎与叶。"""
    import math
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(22, 26), (22, 41)], fill=LEAF_D, width=3)                                  # 花茎
    for i in range(5):                                                                   # 五瓣
        a = -math.pi / 2 + i * 2 * math.pi / 5
        cx, cy = 22 + 10.0 * math.cos(a), 19 + 8.5 * math.sin(a)
        d.ellipse([cx - 6, cy - 5, cx + 6, cy + 5], fill=(0xF2, 0xF6, 0xFF, 255),
                  outline=INK, width=2)
        d.ellipse([cx - 4, cy - 3, cx + 2, cy + 1], fill=(0xD2, 0xDE, 0xF2, 255))        # 瓣内阴影
    d.ellipse([19, 16, 25, 22], fill=GOLD, outline=INK)                                  # 花心
    d.polygon([(22, 32), (11, 38), (22, 41)], fill=LEAF)                                 # 叶
    d.polygon([(22, 34), (33, 38), (22, 41)], fill=LEAF_D)
    return im


# ══════════════════════════════════════ B 类：插件（21 件，符号各自对应功能） ══════════════════════════════════════

def _blade(im, d, x0=10, y0=38, x1=30, y1=14):
    """锄刃：柄 + 刃，插件图里反复用的主体（与 gen_v252_icons 的 hoe 画法同构）。"""
    d.line([(x0, y0), (x0 + 10, y0 - 12)], fill=WOOD, width=4)
    d.polygon([(x1 - 8, y1 + 8), (x1 + 6, y1 - 2), (x1 + 10, y1 + 6), (x1 - 2, y1 + 14)],
              fill=STEEL, outline=INK)
    d.line([(x1 - 4, y1 + 8), (x1 + 4, y1 + 3)], fill=STEEL_L, width=1)


def ic_hoe_echo():
    """回响：一记锄击（右），左侧三道回声弧。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(11, 39), (21, 29)], fill=WOOD, width=4)                                    # 柄
    d.polygon([(19, 31), (37, 12), (42, 18), (24, 37)], fill=STEEL, outline=INK)        # 刃
    d.line([(24, 31), (37, 17)], fill=STEEL_L, width=1)                                 # 刃上高光
    for r, a in ((7, 195), (11, 175), (15, 155)):                                       # 回声弧
        d.arc([16 - r, 20 - r, 16 + r, 20 + r], start=a, end=a + 80,
              fill=(0x7C, 0xE0, 0xFF, 200), width=2)
    d.ellipse([3, 17, 8, 22], fill=CYAN)                                                # 击点
    return im


def ic_hoe_warmth():
    """余温：刃口留着炭火，三道热浪。"""
    im = O44(); d = ImageDraw.Draw(im)
    _blade(im, d, 10, 38, 26, 16)
    for i in range(3):                                                                  # 热浪
        y = 10 + i * 7
        d.arc([18, y - 4, 38, y + 4], start=200, end=340, fill=FIRE_L, width=2)
    for x, y in ((30, 30), (34, 24), (27, 22)):                                         # 余烬
        d.ellipse([x, y, x + 3, y + 3], fill=FIRE)
    d.polygon([(33, 34), (36, 28), (39, 34)], fill=FIRE)
    return im


def ic_hoe_focus():
    """收束：三支箭头从三面收拢到中心一点。"""
    im = O44(); d = ImageDraw.Draw(im)
    for dx, dy in ((-15, -3), (0, -16), (15, -3)):
        ix, iy = 22 + dx * 0.45, 25 + dy * 0.45                                         # 箭头尖靠近中心
        d.line([(22 + dx, 25 + dy), (ix, iy)], fill=STEEL_L, width=3)                    # 箭杆
        d.polygon([(ix - 3, iy - 3), (ix + 3, iy - 3), (ix, iy + 3)], fill=GOLD)        # 箭头
    d.ellipse([18, 27, 26, 35], fill=GOLD, outline=INK)                                 # 焦点
    d.ellipse([20, 29, 24, 33], fill=GOLD_L)
    return im


def ic_can_ricochet():
    """弹射：水柱打到面上折返，落点三颗珠。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(6, 34), (16, 22), (30, 22), (37, 33)], fill=WATER, width=3)                # 折线
    d.polygon([(14, 20), (20, 20), (17, 26)], fill=STEEL, outline=INK)                  # 反弹面
    d.line([(8, 36), (14, 30)], fill=WATER_L, width=2)
    for x, y in ((33, 35), (30, 38), (37, 38)):                                         # 落点
        d.ellipse([x, y, x + 4, y + 4], fill=WATER_L, outline=INK)
    return im


def ic_can_ripen():
    """催熟：大水珠 + 作物被催熟变金 + 一点星光。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(16, 6), (25, 22), (16, 30), (7, 22)], fill=WATER, outline=INK)          # 水珠
    d.ellipse([7, 16, 25, 30], fill=WATER, outline=INK)
    d.ellipse([11, 19, 15, 23], fill=WATER_L)                                           # 珠内高光
    d.line([(21, 30), (21, 40)], fill=LEAF_D, width=3)                                  # 作物茎
    d.ellipse([14, 32, 28, 42], fill=GOLD, outline=INK)                                 # 熟果
    d.line([(17, 35), (18, 39)], fill=GOLD_L, width=2)
    d.line([(31, 8), (31, 14)], fill=GOLD_L, width=2)                                   # 星光
    d.line([(28, 11), (34, 11)], fill=GOLD_L, width=2)
    return im


def ic_can_tide():
    """潮汐：两道叠起的浪 + 月。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.arc([4, 16, 40, 40], start=200, end=340, fill=WATER_D, width=3)                   # 后浪
    d.arc([6, 22, 38, 42], start=200, end=340, fill=WATER, width=3)                     # 前浪
    d.arc([5, 20, 40, 38], start=205, end=290, fill=WATER_L, width=1)                   # 浪尖高光
    d.ellipse([26, 6, 36, 16], fill=(0xF4, 0xF0, 0xC8, 255), outline=INK)               # 月
    d.ellipse([30, 4, 39, 14], fill=(0, 0, 0, 0))
    d.ellipse([30, 4, 39, 14], outline=INK, width=0)
    return im


def ic_hoe_swordwave():
    """剑气：刃 + 一道飞出的大斩弧。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(8, 38), (20, 26)], fill=WOOD, width=4)                                     # 柄
    d.polygon([(18, 28), (36, 8), (40, 14), (22, 34)], fill=STEEL, outline=INK)         # 刃
    d.line([(22, 28), (35, 13)], fill=STEEL_L, width=1)
    for w, a in ((2, 245), (2, 205)):                                                   # 斩弧
        d.arc([12, 10, 44, 40], start=a, end=a + 45, fill=(0xD8, 0xF0, 0xFF, 220), width=w)
    return im


def ic_hoe_burn():
    """灼烧：刃口舔着火舌。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(8, 38), (18, 28)], fill=WOOD, width=4)
    d.polygon([(16, 30), (34, 10), (39, 17), (21, 36)], fill=STEEL, outline=INK)
    d.polygon([(30, 8), (36, 2), (34, 12), (40, 8), (36, 18), (30, 14)], fill=FIRE)    # 火舌
    d.polygon([(33, 11), (36, 8), (35, 15), (39, 12), (36, 17)], fill=FIRE_L)
    d.ellipse([24, 20, 30, 26], fill=FIRE)                                              # 刃上火星
    d.ellipse([20, 26, 24, 30], fill=GOLD_L)
    return im


def ic_hoe_doublestrike():
    """双击：两道平行的斜斩痕 + 中间迸开的火花。"""
    im = O44(); d = ImageDraw.Draw(im)
    for dx in (0, 9):                                                                   # 两道平行斩痕
        d.line([(7 + dx, 38), (28 + dx, 13)], fill=STEEL_D, width=5)
        d.line([(7 + dx, 38), (28 + dx, 13)], fill=STEEL, width=3)
        d.line([(10 + dx, 34), (25 + dx, 16)], fill=STEEL_L, width=1)
    d.line([(24, 30), (38, 30)], fill=GOLD_L, width=2)                                  # 迸发火花
    d.line([(31, 23), (31, 37)], fill=GOLD_L, width=2)
    d.ellipse([27, 26, 35, 34], fill=GOLD)
    d.ellipse([30, 29, 32, 31], fill=GOLD_L)
    return im


def ic_hoe_lifesteal():
    """吸血：刃 + 一颗被抽出的大血珠（带高光）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.line([(6, 39), (16, 29)], fill=WOOD, width=4)                                     # 柄
    d.polygon([(14, 31), (30, 13), (36, 19), (20, 37)], fill=STEEL, outline=INK)        # 刃
    d.line([(18, 31), (29, 18)], fill=STEEL_L, width=1)
    d.polygon([(32, 6), (39, 17), (32, 24), (25, 17)], fill=BLOOD, outline=INK)         # 血珠尖
    d.ellipse([25, 13, 39, 27], fill=BLOOD, outline=INK)
    d.ellipse([28, 16, 33, 21], fill=BLOOD_L)                                           # 珠内高光
    return im


def ic_can_multijet():
    """多柱：壶口喷出三根互不平行的水柱。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([6, 32, 20, 40], radius=3, fill=STEEL_D, outline=INK)           # 壶嘴
    for dx, w in ((-8, 2), (0, 3), (9, 2)):                                             # 三柱
        d.line([(19, 34), (30 + dx, 10 + abs(dx))], fill=WATER, width=w)
    for x, y in ((22, 8), (30, 6), (38, 9)):
        d.ellipse([x, y, x + 4, y + 4], fill=WATER_L, outline=INK)                      # 水珠
    d.line([(8, 34), (17, 34)], fill=STEEL_L, width=1)
    return im


def ic_can_slow():
    """减速：六向雪花 + 指向地面的箭头。"""
    import math
    im = O44(); d = ImageDraw.Draw(im)
    for a in (0, 60, 120):                                                              # 六向雪花
        dx, dy = math.cos(math.radians(a)) * 13, math.sin(math.radians(a)) * 13
        d.line([(22 - dx, 18 - dy), (22 + dx, 18 + dy)], fill=WATER_L, width=2)
    for i in range(6):                                                                  # 枝杈
        a = math.radians(i * 60)
        d.line([(22 + 8 * math.cos(a), 18 + 8 * math.sin(a)),
                (22 + 13 * math.cos(a) + 3, 18 + 13 * math.sin(a) + 3)], fill=WATER, width=1)
    d.ellipse([19, 15, 25, 21], fill=WATER)
    d.line([(22, 28), (22, 35)], fill=(0x8E, 0xA8, 0xD8, 255), width=3)                 # 箭头杆
    d.polygon([(22, 40), (15, 30), (29, 30)], fill=(0x8E, 0xA8, 0xD8, 255), outline=INK)
    return im


def ic_can_thickjet():
    """加粗：水柱往下越喷越宽（梯形）+ 两侧散流。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rectangle([12, 6, 32, 13], fill=STEEL_D, outline=INK)                             # 喷嘴
    d.polygon([(14, 13), (30, 13), (37, 38), (7, 38)], fill=WATER, outline=INK)         # 加粗水柱
    d.polygon([(18, 15), (22, 15), (20, 36), (15, 36)], fill=WATER_L)                   # 高光
    d.line([(4, 18), (8, 34)], fill=WATER_L, width=2)                                   # 侧散流
    d.line([(40, 18), (36, 34)], fill=WATER_L, width=2)
    return im


def ic_can_autocharge():
    """自充：一圈回充箭头 + 中心一滴水。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.arc([8, 8, 36, 36], start=40, end=320, fill=CYAN, width=3)                        # 回收弧
    d.polygon([(30, 4), (38, 12), (29, 14)], fill=CYAN)                                 # 箭头
    d.polygon([(22, 15), (28, 24), (22, 31), (16, 24)], fill=WATER, outline=INK)        # 水滴
    d.ellipse([18, 22, 26, 30], fill=WATER, outline=INK)
    d.ellipse([19, 23, 22, 26], fill=WATER_L)
    return im


def ic_fert_subburst():
    """子爆：主爆一圈，右侧再生两个小爆。"""
    im = O44(); d = ImageDraw.Draw(im)
    pts = []
    import math
    for i in range(16):                                                                 # 主爆星
        r = 15 if i % 2 == 0 else 8
        a = i * math.pi / 8
        pts.append((18 + r * math.cos(a), 20 + r * math.sin(a)))
    d.polygon(pts, fill=FIRE, outline=INK)
    d.ellipse([15, 17, 21, 23], fill=GOLD_L)
    for cx, cy, r in ((34, 32, 6), (31, 38, 4)):                                        # 子爆
        p2 = []
        for i in range(12):
            rr = r if i % 2 == 0 else r * 0.55
            a = i * math.pi / 6
            p2.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
        d.polygon(p2, fill=FIRE_L, outline=INK)
    return im


def ic_fert_poison():
    """毒素：细颈瓶 + 绿液 + 冒出的气泡。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(14, 38), (30, 38), (25, 22), (25, 12), (19, 12), (19, 22)], fill=(0x3E, 0x4A, 0x52, 255), outline=INK)
    d.polygon([(16, 30), (28, 30), (30, 38), (14, 38)], fill=(0x7F, 0xC0, 0x3C, 255))   # 毒液
    d.rectangle([17, 6, 27, 13], fill=WOOD, outline=INK)                                # 瓶塞
    for cx, cy, r in ((20, 26, 3), (26, 23, 2), (23, 19, 2)):                           # 气泡
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(0xB8, 0xE8, 0x6A, 255), outline=INK)
    d.line([(21, 15), (21, 28)], fill=(0x6A, 0x74, 0x7C, 255), width=1)
    return im


def ic_fert_freeburst():
    """免耗：爆开一圈 + 一道回退箭头（退回的那一份）。"""
    im = O44(); d = ImageDraw.Draw(im)
    import math
    pts = []
    for i in range(14):
        r = 13 if i % 2 == 0 else 7
        a = i * math.pi / 7
        pts.append((24 + r * math.cos(a), 24 + r * math.sin(a)))
    d.polygon(pts, fill=GOLD, outline=INK)
    d.ellipse([20, 20, 28, 28], fill=GOLD_L)
    d.arc([4, 6, 26, 28], start=200, end=20, fill=CYAN, width=3)                        # 回退弧
    d.polygon([(4, 12), (12, 8), (10, 18)], fill=CYAN)                                  # 箭头
    return im


def ic_fert_chain():
    """连环：三个相扣的环。"""
    im = O44(); d = ImageDraw.Draw(im)
    for cx, col in ((12, STEEL), (22, STEEL_L), (32, STEEL)):
        d.ellipse([cx - 6, 13, cx + 6, 30], outline=INK, width=5)                       # 深描边底
        d.ellipse([cx - 6, 13, cx + 6, 30], outline=col, width=3)                       # 环
    d.ellipse([18, 19, 26, 25], fill=(0, 0, 0, 0))
    for cx in (12, 32):
        d.arc([cx - 6, 13, cx + 6, 30], start=200, end=340, fill=GOLD_L, width=1)       # 环上反光
    return im


def ic_fert_mine():
    """伏雷：埋在地里的圆盘雷 + 引信 + 火花。"""
    im = O44(); d = ImageDraw.Draw(im)
    for x in range(6, 40, 5):                                                           # 地面
        d.line([(x, 39), (x + 3, 39)], fill=SOIL_L, width=1)
    d.ellipse([7, 20, 33, 40], fill=STEEL_D, outline=INK, width=2)                      # 雷体
    d.ellipse([12, 24, 28, 36], fill=FIRE_D)                                            # 压发盘
    d.ellipse([16, 27, 24, 33], fill=FIRE)
    for a in (0, 90, 180, 270):                                                         # 四根触角
        dx = [1, 0, -1, 0][a // 90] * 6
        dy = [0, 1, 0, -1][a // 90] * 6
        d.line([(20 + dx, 30 + dy), (20 + dx * 1.4, 30 + dy * 1.4)], fill=STEEL_L, width=2)
    d.line([(26, 22), (34, 12)], fill=WOOD_D, width=2)                                  # 引信
    d.polygon([(33, 8), (38, 4), (39, 11), (35, 14)], fill=GOLD_L)                      # 火花
    return im


def ic_fert_ward():
    """护壤：一面盾 + 盾面上的壤粒/裂纹（护罩把玩家罩住）。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.polygon([(22, 4), (38, 10), (36, 26), (22, 40), (8, 26), (6, 10)], fill=SOIL, outline=INK)  # 盾
    d.polygon([(22, 8), (34, 13), (32, 25), (22, 35)], fill=SOIL_L)                     # 左亮面
    d.line([(22, 8), (22, 35)], fill=SOIL_D, width=1)                                   # 中线
    for x, y in ((14, 16), (19, 21), (28, 18), (16, 25), (26, 27)):                     # 壤粒
        d.ellipse([x, y, x + 3, y + 3], fill=(0x5A, 0x3F, 0x26, 255))
    d.arc([10, 10, 34, 34], start=200, end=320, fill=CYAN, width=2)                     # 护罩弧光
    d.ellipse([20, 18, 24, 22], fill=CYAN)                                              # 罩心
    return im


def ic_fert_mark():
    """灼印：一枚烙铁印 + 印面烧红 + 烙痕线圈。"""
    im = O44(); d = ImageDraw.Draw(im)
    d.rounded_rectangle([10, 18, 34, 38], radius=4, fill=STEEL_D, outline=INK)          # 印座
    d.rounded_rectangle([14, 22, 30, 34], radius=3, fill=FIRE_D)                        # 烧红的印面
    d.line([(18, 28), (26, 28)], fill=FIRE_L, width=2)                                  # 印纹「＋」
    d.line([(22, 24), (22, 32)], fill=FIRE_L, width=2)
    d.line([(22, 18), (22, 10)], fill=WOOD_D, width=3)                                  # 柄
    for x, y in ((8, 12), (34, 8), (28, 14)):                                           # 火星
        d.ellipse([x, y, x + 3, y + 3], fill=FIRE)
    d.arc([6, 6, 26, 26], start=150, end=250, fill=FIRE_L, width=1)                     # 热晕
    return im


# ══════════════════════════════════════ 作业表 ══════════════════════════════════════
# (资源名, 主题, 画法, 中文名, 所属类别)
JOBS = [
    # ── A：没有任何图标资源（PNG 缺 + 掩码表也没有）⇒ 界面只能画品质色块 ──
    ("icon_item_good_wateringcan", "item", ic_good_wateringcan, "铜洒水壶", "A 无资源"),
    ("icon_item_good_fertilizer", "item", ic_good_fertilizer, "草木灰肥", "A 无资源"),
    ("icon_item_good_beehive", "item", ic_good_beehive, "蜂箱", "A 无资源"),
    ("icon_item_good_sprinkler", "item", ic_good_sprinkler, "环形喷灌", "A 无资源"),
    # ── B：插件图标是通用几何符号（环/星/菱/条/X/点阵），与功能无关 ──
    ("icon_plugin_hoe_echo", "plugin", ic_hoe_echo, "回响", "B 占位符号"),
    ("icon_plugin_hoe_warmth", "plugin", ic_hoe_warmth, "余温", "B 占位符号"),
    ("icon_plugin_hoe_focus", "plugin", ic_hoe_focus, "收束", "B 占位符号"),
    ("icon_plugin_can_ricochet", "plugin", ic_can_ricochet, "弹射", "B 占位符号"),
    ("icon_plugin_can_ripen", "plugin", ic_can_ripen, "催熟", "B 占位符号"),
    ("icon_plugin_can_tide", "plugin", ic_can_tide, "潮汐", "B 占位符号"),
    ("icon_plugin_hoe_swordwave", "plugin", ic_hoe_swordwave, "剑气", "B 占位符号"),
    ("icon_plugin_hoe_burn", "plugin", ic_hoe_burn, "灼烧", "B 占位符号"),
    ("icon_plugin_hoe_doublestrike", "plugin", ic_hoe_doublestrike, "双击", "B 占位符号"),
    ("icon_plugin_hoe_lifesteal", "plugin", ic_hoe_lifesteal, "吸血", "B 占位符号"),
    ("icon_plugin_can_multijet", "plugin", ic_can_multijet, "多柱", "B 占位符号"),
    ("icon_plugin_can_slow", "plugin", ic_can_slow, "减速", "B 占位符号"),
    ("icon_plugin_can_thickjet", "plugin", ic_can_thickjet, "加粗", "B 占位符号"),
    ("icon_plugin_can_autocharge", "plugin", ic_can_autocharge, "自充", "B 占位符号"),
    ("icon_plugin_fert_subburst", "plugin", ic_fert_subburst, "子爆", "B 占位符号"),
    ("icon_plugin_fert_poison", "plugin", ic_fert_poison, "毒素", "B 占位符号"),
    ("icon_plugin_fert_freeburst", "plugin", ic_fert_freeburst, "免耗", "B 占位符号"),
    ("icon_plugin_fert_chain", "plugin", ic_fert_chain, "连环", "B 占位符号"),
    ("icon_plugin_fert_mine", "plugin", ic_fert_mine, "伏雷", "B 占位符号"),
    ("icon_plugin_fert_ward", "plugin", ic_fert_ward, "护壤", "B 占位符号"),
    ("icon_plugin_fert_mark", "plugin", ic_fert_mark, "灼印", "B 占位符号"),
    # ── C：同一张"菱形"占位剪影被复用（IoU≥0.90 聚类），与名称不符 ──
    ("icon_item_ash_flask", "item", ic_ash_flask, "灰烬瓶", "C 菱形占位"),
    ("icon_item_mead_vat", "item", ic_mead_vat, "蜂蜜酒桶", "C 菱形占位"),
    ("icon_item_bronze_lens", "item", ic_bronze_lens, "青铜镜片", "C 菱形占位"),
    ("icon_plant_salt_melon", "plant", ic_salt_melon, "盐瓜", "C 菱形占位"),
    ("icon_plant_cloud_orchid", "plant", ic_cloud_orchid, "云兰", "C 菱形占位"),
]


def sha(path):
    return hashlib.md5(open(path, "rb").read()).hexdigest()


def make_meta(png_path, res_name):
    """按邻居模板写 .meta：只换 guid（同一资源名恒定 ⇒ 重跑不会换 guid）。"""
    tpl = open(META_TEMPLATE, encoding="utf-8").read()
    guid = hashlib.md5(("Seedfall/v274/" + res_name).encode("utf-8")).hexdigest()
    lines = []
    for ln in tpl.split("\n"):
        if ln.startswith("guid: "):
            lines.append("guid: " + guid)
        else:
            lines.append(ln)
    open(png_path + ".meta", "w", encoding="utf-8", newline="\n").write("\n".join(lines))


def plan():
    print("%-8s %-8s %-40s %s" % ("类别", "主题", "资源名", "状态"))
    for res, kind, fn, cn, cat in JOBS:
        p = os.path.join(UI, res + ".png")
        st = "已存在→替换(先备份)" if os.path.exists(p) else "新增"
        print("%-8s %-8s %-40s %s  %s" % (cat, kind, res, st, cn))


def run():
    os.makedirs(BAK, exist_ok=True)
    # ★ 清单：只在**第一次**生成时写死，之后每次重跑都读它 ★
    #   为什么要它：重跑时"我上次生成的那张"也存在 ⇒ 若按"存在就备份"处理，
    #   就会把我的产物当成"原件"备份进去，`--restore` 就回不到"改前没有这张图"的状态 ✗
    MANI = os.path.join(BAK, "_manifest.json")
    if os.path.exists(MANI):
        mani = json.load(open(MANI, encoding="utf-8"))
    else:
        mani = {"new": [], "replaced": []}
    before = {f: sha(os.path.join(UI, f)) for f in os.listdir(UI) if f.endswith(".png")}
    rows, replaced, added = [], 0, 0
    for res, kind, fn, cn, cat in JOBS:
        obj = fn()
        assert obj.size == (OBJ, OBJ), (res, obj.size)
        img = compose(obj, kind)
        p = os.path.join(UI, res + ".png")
        is_new = res in mani["new"]
        if os.path.exists(p) and not is_new:
            b = os.path.join(BAK, res + ".png")
            if not os.path.exists(b):
                shutil.copy2(p, b)                 # 备份原图（可 --restore 回滚）
            if res not in mani["replaced"]:
                mani["replaced"].append(res)
            replaced += 1
        elif is_new:
            added += 1
        else:
            added += 1
            if res not in mani["new"]:
                mani["new"].append(res)
        img.save(p, "PNG", optimize=True)
        if not os.path.exists(p + ".meta"):
            make_meta(p, res)                      # 已有 meta 不动 ⇒ guid 稳定
        px = Image.open(p)
        rows.append((cat, res, px.size, os.path.getsize(p), cn))
    if not os.path.exists(MANI):
        json.dump(mani, open(MANI, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    print("%-8s %-40s %-9s %-8s %s" % ("类别", "文件", "尺寸", "字节", "中文名"))
    print("-" * 96)
    for cat, res, size, nbytes, cn in rows:
        print("%-8s %-40s %-9s %-8d %s" % (cat, res, "%d×%d" % size, nbytes, cn))
        assert size == (SIZE, SIZE)
    print("-" * 96)
    print("新增 %d 张｜替换 %d 张（原件已备份到 Tools/icon_gen/backup_v274）｜合计 %d 张"
          % (added, replaced, len(rows)))

    after = {f: sha(os.path.join(UI, f)) for f in os.listdir(UI) if f.endswith(".png")}
    mine = set(res + ".png" for res, *_ in JOBS)
    touched = sorted([f for f in before if f in after and before[f] != after[f]]
                     + [f for f in after if f not in before])
    bad = [f for f in touched if f not in mine]                 # 动了不该动的
    same = [f for f in before if f in after and before[f] == after[f] and f not in mine]
    print("本次改动 PNG 共 %d 张：%s" % (len(touched), ", ".join(touched)))
    print("作业表之外的 PNG 保持逐字节不变：%d 张（总数 %d）"
          % (len(same), len([f for f in before if f not in mine])))
    assert not bad, "有不该动的图标被改了：%s" % bad
    print("PNG 总数：%d（改前 %d）" % (len(after), len(before)))


def restore():
    """回滚：被替换的从 backup_v274 拷回；本次**新增**的连 PNG+meta 一起删掉。"""
    MANI = os.path.join(BAK, "_manifest.json")
    mani = json.load(open(MANI, encoding="utf-8")) if os.path.exists(MANI) else {"new": [], "replaced": []}
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
    if "--restore" in sys.argv:
        restore()
    elif "--dry-run" in sys.argv:
        plan()
    else:
        run()
