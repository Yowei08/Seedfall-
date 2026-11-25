# -*- coding: utf-8 -*-
"""v252 第三批图标：10 道具 + 6 插件 + 5 植物 = 21 张 64×64 PNG。

做法与 `gen_v252_icons.py` 一致（深底 + 圆角 + 硬边符号 + **确定性**），
只是这批改用"**符号种类 + 两种颜色**"的表驱动画法：同类符号靠颜色/朝向/数量区分，
不靠手工逐个画 —— 21 张手画容易画歪，表驱动至少保证"同一 id 每次跑出来一模一样"。

用法：`python Tools/icon_gen/gen_v252_batch3_icons.py`
"""
import math
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
S = 64
BG = (0x1A, 0x14, 0x10, 255)
BORDER = (0x3A, 0x2E, 0x24, 255)


def panel():
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([2, 2, S - 3, S - 3], radius=10, fill=BG, outline=BORDER, width=2)
    return im, d


def save(im, name):
    p = os.path.join(OUT, name + ".png")
    im.save(p, "PNG", optimize=True)
    return p


# ── 符号种类 ──────────────────────────────────────────────────────────
def g_diamond(d, c):
    d.polygon([(32, 12), (52, 32), (32, 52), (12, 32)], fill=c)


def g_ring(d, c):
    d.ellipse([12, 12, 52, 52], outline=c, width=6)


def g_bars(d, c):
    for i, h in enumerate((18, 30, 42)):
        d.rectangle([16 + i * 12, 46 - h, 24 + i * 12, 46], fill=c)


def g_blade(d, c):
    d.polygon([(14, 50), (44, 14), (52, 22), (22, 56)], fill=c)


def g_drop(d, c):
    d.polygon([(32, 10), (46, 34), (32, 54), (18, 34)], fill=c)


def g_wave(d, c):
    for k in range(3):
        y = 22 + k * 10
        d.line([(10, y), (54, y - 6)], fill=c, width=4)


def g_leaf(d, c):
    d.chord([12, 12, 52, 52], start=200, end=340, fill=c)
    d.line([(32, 52), (32, 24)], fill=c, width=3)


def g_star(d, c):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        r = 20 if i % 2 == 0 else 9
        pts.append((32 + math.cos(a) * r, 32 + math.sin(a) * r))
    d.polygon(pts, fill=c)


GLYPH = dict(diamond=g_diamond, ring=g_ring, bars=g_bars, blade=g_blade,
             drop=g_drop, wave=g_wave, leaf=g_leaf, star=g_star)

# id → (前缀, 符号, 主色)
JOBS = {
    # 道具
    "iron_bracer":     ("icon_item_", "ring",    (0xC8, 0xD4, 0xE2, 255)),
    "bronze_lens":     ("icon_item_", "diamond", (0xE2, 0xA8, 0x4A, 255)),
    "ash_flask":       ("icon_item_", "drop",    (0xE0, 0x8A, 0x3C, 255)),
    "feather_fan":     ("icon_item_", "wave",    (0xF4, 0xEE, 0xC8, 255)),
    "hive_plate":      ("icon_item_", "bars",    (0xE9, 0xC2, 0x5A, 255)),
    "spyglass":        ("icon_item_", "ring",    (0x9C, 0xD8, 0xF0, 255)),
    "forge_heart":     ("icon_item_", "star",    (0xFF, 0x8A, 0x3C, 255)),
    "down_boots":      ("icon_item_", "leaf",    (0xBFE, 0xE8, 0x9A, 255)) if False else ("icon_item_", "leaf", (0xCF, 0xE8, 0x9A, 255)),
    "mead_vat":        ("icon_item_", "drop",    (0xE0, 0xB0, 0x4A, 255)),
    "star_atlas":      ("icon_item_", "star",    (0xC8, 0xC0, 0xF0, 255)),
    # 插件
    "hoe_thornbite":   ("icon_plugin_", "blade", (0xC9, 0xE0, 0x9A, 255)),
    "hoe_rend":        ("icon_plugin_", "blade", (0xF0, 0xB0, 0xA0, 255)),
    "can_frostwake":   ("icon_plugin_", "wave",  (0x9C, 0xD8, 0xF0, 255)),
    "can_torrent":     ("icon_plugin_", "wave",  (0x5B, 0xC0, 0xEB, 255)),
    "fert_ashfall":    ("icon_plugin_", "bars",  (0xD8, 0xD0, 0xC0, 255)),
    "fert_bramble":    ("icon_plugin_", "star",  (0xB4, 0xE0, 0x7A, 255)),
    # 植物
    "glow_moss":       ("icon_plant_", "leaf",   (0xA8, 0xE8, 0xC0, 255)),
    "salt_melon":      ("icon_plant_", "drop",   (0xE8, 0xF0, 0xC8, 255)),
    "cloud_orchid":    ("icon_plant_", "diamond",(0xD8, 0xE8, 0xF8, 255)),
    "mirror_bloom":    ("icon_plant_", "star",   (0xF0, 0xE0, 0xF8, 255)),
    "ember_reed":      ("icon_plant_", "wave",   (0xFF, 0xB0, 0x60, 255)),
}


def make(prefix, kind, color):
    im, d = panel()
    GLYPH[kind](d, color)
    d.rectangle([26, 54, 38, 57], fill=(0x60, 0x50, 0x40, 255))   # 底部一条共用"底座"，读起来成体系
    return save(im, prefix + kind and prefix)


if __name__ == "__main__":
    n = 0
    for sid, (prefix, kind, color) in JOBS.items():
        im, d = panel()
        GLYPH[kind](d, color)
        d.rectangle([26, 54, 38, 57], fill=(0x60, 0x50, 0x40, 255))
        p = save(im, prefix + sid)
        assert Image.open(p).size == (S, S)
        n += 1
        print("%-44s %s" % (os.path.basename(p), Image.open(p).size))
    print("共 %d 张" % n)
