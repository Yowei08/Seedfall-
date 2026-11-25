# -*- coding: utf-8 -*-
"""v252：为这一轮新增的 8 件道具 + 4 件插件生成 64×64 图标（`Assets/Resources/Art/UI/`）。

约定（与既有 `gen_v212_icons.py` / `gen_plugin_icons.py` 一致）：
  · 64×64 RGBA，PNG，硬边（不抗锯齿之外的模糊一律不加）
  · 深色圆角底 + 单色/双色符号；色相按"这一件在游戏里干什么"选，不是随机
  · **确定性**：同一份脚本跑两次逐字节相同（没有任何随机源）⇒ 可复现 ✓
  · 幂等：目标文件已存在且尺寸正确时仍会重写（保证与脚本同源）

用法：`python Tools/icon_gen/gen_v252_icons.py`（写在工程里，可重跑）
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
S = 64

BG = (0x1A, 0x14, 0x10, 255)        # 与既有插件图标同一块深底
BORDER = (0x3A, 0x2E, 0x24, 255)


def new_panel():
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([2, 2, S - 3, S - 3], radius=10, fill=BG, outline=BORDER, width=2)
    return im, d


def save(im, name):
    p = os.path.join(OUT, name + ".png")
    im.save(p, "PNG", optimize=True)
    return p


# ─────────────────────────── 道具（8 件） ───────────────────────────

def icon_iron_bucket():
    im, d = new_panel()
    steel, light = (0x9A, 0xA3, 0xAD, 255), (0xD6, 0xDD, 0xE4, 255)
    # 提手
    d.arc([18, 12, 46, 40], start=200, end=340, fill=light, width=3)
    # 桶身（上宽下窄的梯形）
    d.polygon([(16, 26), (48, 26), (43, 52), (21, 52)], fill=steel)
    # 两道箍 + 高光
    d.line([(17, 33), (47, 33)], fill=light, width=2)
    d.line([(19, 43), (45, 43)], fill=light, width=2)
    d.line([(22, 28), (22, 50)], fill=light, width=1)
    return save(im, "icon_item_iron_bucket")


def icon_glass_lens_array():
    im, d = new_panel()
    glass, rim = (0x9C, 0xD8, 0xF0, 255), (0xE8, 0xF6, 0xFF, 255)
    for cx, cy, r in ((20, 34, 8), (32, 24, 9), (44, 34, 8)):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=rim, width=2, fill=(0x2E, 0x52, 0x66, 255))
    d.line([(26, 30), (30, 27)], fill=rim, width=2)
    d.line([(34, 27), (38, 30)], fill=rim, width=2)
    d.line([(24, 44), (40, 44)], fill=glass, width=2)
    d.line([(27, 48), (37, 48)], fill=glass, width=2)
    return save(im, "icon_item_glass_lens_array")


def icon_honey_jar():
    im, d = new_panel()
    honey, body = (0xE9, 0xC2, 0x5A, 255), (0xC8, 0x9A, 0x3C, 255)
    d.rounded_rectangle([18, 22, 46, 50], radius=6, fill=(0x8A, 0x6A, 0x2A, 255), outline=body, width=2)
    d.rectangle([17, 36, 47, 49], fill=honey)          # 蜜面
    d.rounded_rectangle([20, 16, 44, 23], radius=3, fill=(0xE8, 0xE2, 0xC8, 255))   # 盖子
    d.line([(22, 40), (22, 47)], fill=(0xFF, 0xF0, 0xB0, 255), width=2)             # 高光
    return save(im, "icon_item_honey_jar")


def icon_twin_lantern():
    im, d = new_panel()
    glow, frame = (0xFF, 0xD8, 0x6A, 255), (0x6E, 0x5A, 0x30, 255)
    for x0 in (14, 34):
        d.line([(x0 + 8, 10), (x0 + 8, 18)], fill=frame, width=2)
        d.polygon([(x0 + 2, 18), (x0 + 14, 18), (x0 + 12, 44), (x0 + 4, 44)], fill=glow)
        d.line([(x0 + 2, 26), (x0 + 14, 26)], fill=frame, width=2)
        d.line([(x0 + 2, 36), (x0 + 14, 36)], fill=frame, width=2)
        d.line([(x0 + 8, 44), (x0 + 8, 50)], fill=frame, width=2)
    return save(im, "icon_item_twin_lantern")


def icon_thorn_wall():
    im, d = new_panel()
    wood, thorn = (0x6B, 0x4A, 0x2A, 255), (0xC9, 0xE0, 0x9A, 255)
    d.rectangle([12, 40, 52, 46], fill=wood)
    for i, x in enumerate((16, 24, 32, 40, 48)):
        d.polygon([(x - 4, 40), (x, 14 + (i % 2) * 4), (x + 4, 40)], fill=thorn)
    d.line([(12, 50), (52, 50)], fill=(0x8A, 0x6A, 0x3A, 255), width=2)
    return save(im, "icon_item_thorn_wall")


def icon_moon_bracelet():
    im, d = new_panel()
    silver, moon = (0xC8, 0xD4, 0xE2, 255), (0xF4, 0xF0, 0xC8, 255)
    d.arc([14, 18, 50, 54], start=200, end=520, fill=silver, width=4)   # 环
    d.ellipse([24, 14, 42, 32], fill=moon)                              # 月
    d.ellipse([30, 12, 46, 28], fill=BG)                                 # 挖出月牙
    return save(im, "icon_item_moon_bracelet")


def icon_seed_drum():
    im, d = new_panel()
    skin, beat = (0xD9, 0x9A, 0x4A, 255), (0xF0, 0xE2, 0xC0, 255)
    d.rounded_rectangle([16, 20, 48, 48], radius=8, fill=(0x7A, 0x4E, 0x24, 255), outline=skin, width=2)
    d.ellipse([24, 24, 40, 44], outline=beat, width=2)
    d.line([(12, 18), (24, 30)], fill=beat, width=3)     # 两根鼓槌
    d.line([(52, 18), (40, 30)], fill=beat, width=3)
    return save(im, "icon_item_seed_drum")


def icon_storm_lens():
    im, d = new_panel()
    lens, bolt = (0x9C, 0xD8, 0xF0, 255), (0xFF, 0xE8, 0x5A, 255)
    d.ellipse([12, 12, 52, 52], outline=lens, width=3, fill=(0x22, 0x36, 0x4A, 255))
    d.polygon([(34, 18), (24, 34), (31, 34), (28, 46), (41, 29), (33, 29)], fill=bolt)
    return save(im, "icon_item_storm_lens")


# ─────────────────────────── 插件（4 件） ───────────────────────────

def icon_hoe_embertrail():
    im, d = new_panel()
    blade, ember = (0x9A, 0xA3, 0xAD, 255), (0xFF, 0x7A, 0x2E, 255)
    d.line([(14, 50), (38, 26)], fill=(0x8A, 0x6A, 0x3A, 255), width=5)     # 柄
    d.polygon([(34, 30), (50, 14), (54, 22), (40, 38)], fill=blade)         # 刃
    for i, (x, y) in enumerate(((46, 30), (50, 38), (40, 34))):
        r = 3 - i * 0.5
        d.ellipse([x - r, y - r, x + r, y + r], fill=ember)
    return save(im, "icon_plugin_hoe_embertrail")


def icon_can_widelance():
    im, d = new_panel()
    water = (0x5B, 0xC0, 0xEB, 255)
    d.rounded_rectangle([8, 30, 24, 50], radius=4, fill=(0x33, 0x72, 0x92, 255), outline=water, width=2)
    for dy, w in ((0, 22), (9, 16), (18, 10)):     # 三道由宽到窄的水墙
        d.polygon([(26, 34 + dy), (56, 30 + dy), (56, 36 + dy), (26, 40 + dy)], fill=water)
    return save(im, "icon_plugin_can_widelance")


def icon_fert_maw():
    im, d = new_panel()
    mouth, tooth = (0xB4, 0x3A, 0x2E, 255), (0xF4, 0xEC, 0xD8, 255)
    d.chord([12, 18, 52, 50], start=0, end=180, fill=mouth)     # 上颚
    d.polygon([(16, 34), (20, 42), (24, 34)], fill=tooth)
    d.polygon([(40, 34), (44, 42), (48, 34)], fill=tooth)
    d.ellipse([28, 36, 36, 46], fill=(0x40, 0x10, 0x0C, 255))
    return save(im, "icon_plugin_fert_maw")


def icon_hoe_carve():
    im, d = new_panel()
    blade, cut = (0x9A, 0xA3, 0xAD, 255), (0xFF, 0xF2, 0xC0, 255)
    d.line([(12, 50), (30, 32)], fill=(0x8A, 0x6A, 0x3A, 255), width=5)
    d.line([(22, 20), (54, 52)], fill=blade, width=4)     # 两把交叉的刃
    d.line([(42, 20), (14, 52)], fill=blade, width=4)
    d.line([(24, 24), (40, 40)], fill=cut, width=2)
    d.line([(40, 24), (24, 40)], fill=cut, width=2)
    return save(im, "icon_plugin_hoe_carve")


# ─────────────────────────── 植物（6 株，v252） ───────────────────────────

def icon_mint_sprout():
    im, d = new_panel()
    stem, leaf = (0x6F, 0xC1, 0x3C, 255), (0x9E, 0xE0, 0x6A, 255)
    d.line([(32, 52), (32, 26)], fill=stem, width=3)
    for cx, cy, w in ((22, 30, 12), (42, 30, 12), (32, 18, 14)):
        d.ellipse([cx - w // 2, cy - 7, cx + w // 2, cy + 7], fill=leaf)
    return save(im, "icon_plant_mint_sprout")


def icon_cinder_pepper():
    im, d = new_panel()
    body, stem = (0xE2, 0x56, 0x4D, 255), (0x4E, 0x6E, 0x3A, 255)
    d.polygon([(24, 18), (40, 18), (38, 44), (32, 54), (26, 44)], fill=body)   # 下尖的椒
    d.line([(28, 22), (30, 46)], fill=(0xFF, 0x9A, 0x8A, 255), width=2)        # 高光
    d.rectangle([28, 12, 36, 20], fill=stem)                                    # 蒂
    d.line([(36, 20), (50, 12)], fill=stem, width=2)
    return save(im, "icon_plant_cinder_pepper")


def icon_glass_melon():
    im, d = new_panel()
    rind, band = (0x6F, 0xC1, 0x8C, 255), (0xE6, 0xF2, 0xC0, 255)
    d.ellipse([12, 18, 52, 54], fill=rind)
    for x in (22, 32, 42):
        d.line([(x, 20), (x, 52)], fill=band, width=2)
    d.line([(32, 18), (32, 10)], fill=(0x4E, 0x6E, 0x3A, 255), width=3)
    return save(im, "icon_plant_glass_melon")


def icon_frost_reed():
    im, d = new_panel()
    reed, tip = (0xA8, 0xD8, 0xE8, 255), (0xF0, 0xFA, 0xFF, 255)
    d.line([(32, 56), (32, 16)], fill=reed, width=4)
    d.polygon([(28, 18), (32, 6), (36, 18)], fill=tip)
    d.polygon([(30, 40), (14, 30), (30, 34)], fill=reed)
    d.polygon([(34, 44), (50, 34), (34, 38)], fill=reed)
    return save(im, "icon_plant_frost_reed")


def icon_moon_reed():
    im, d = new_panel()
    reed, moon = (0x7A, 0x8E, 0x6A, 255), (0xF4, 0xF0, 0xC8, 255)
    d.line([(32, 56), (32, 30)], fill=reed, width=3)
    d.ellipse([20, 8, 44, 32], fill=moon)
    d.ellipse([26, 6, 48, 26], fill=BG)
    return save(im, "icon_plant_moon_reed")


def icon_amber_lily():
    im, d = new_panel()
    petal, core = (0xF0, 0xA8, 0x3C, 255), (0xFF, 0xE0, 0x80, 255)
    for a in range(5):
        cx = 32 + int(14 * __import__("math").cos(a * 1.2566))
        cy = 30 + int(14 * __import__("math").sin(a * 1.2566))
        d.ellipse([cx - 8, cy - 8, cx + 8, cy + 8], fill=petal)
    d.ellipse([27, 25, 37, 35], fill=core)
    d.line([(32, 38), (32, 54)], fill=(0x4E, 0x6E, 0x3A, 255), width=3)
    return save(im, "icon_plant_amber_lily")


JOBS = [
    icon_iron_bucket, icon_glass_lens_array, icon_honey_jar, icon_twin_lantern,
    icon_thorn_wall, icon_moon_bracelet, icon_seed_drum, icon_storm_lens,
    icon_hoe_embertrail, icon_can_widelance, icon_fert_maw, icon_hoe_carve,
    icon_mint_sprout, icon_cinder_pepper, icon_glass_melon,
    icon_frost_reed, icon_moon_reed, icon_amber_lily,
]

if __name__ == "__main__":
    made = [f() for f in JOBS]
    for p in made:
        im = Image.open(p)
        assert im.size == (S, S), (p, im.size)
        print("%-58s %s" % (os.path.basename(p), im.size))
    print("共 %d 张" % len(made))
