# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 道具 2D 图标（v72）
=====================================
15 个道具各一张手绘风格图标，与植物图标（gen_icons2d.py）同一套画法：
平涂色块 + 统一深色描边。

约定：
  ① 轮廓读得出功能——不看名字也能猜出是干什么的（磨刀石=石头+刃光、放大镜=镜片+柄）
  ② 描边与植物图标同宽，两套图标并排不会一粗一细
  ③ 底色透明，卡片自己垫深底

产出：Assets/Resources/Art/UI/icon_item_<道具id>.png（256×256，RGBA）
"""

import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "UI")

SIZE = 256
SS = 4
C = SIZE * SS
INK = (38, 32, 28, 255)

# 与植物图标共用的一套基础色
STONE = (168, 166, 158, 255)
STONE_D = (126, 124, 118, 255)
WOOD = (150, 112, 70, 255)
WOOD_D = (112, 82, 50, 255)
IRON = (196, 200, 206, 255)
IRON_D = (140, 146, 154, 255)
GOLD = (240, 198, 74, 255)
GREEN = (110, 168, 96, 255)
GREEN_D = (78, 128, 72, 255)
RED = (216, 84, 78, 255)
BLUE = (96, 158, 224, 255)
PURPLE = (176, 128, 220, 255)
ASH = (92, 88, 84, 255)
BONE = (238, 230, 210, 255)


def ell(d, cx, cy, rx, ry, fill, w=6, outline=INK):
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=fill, outline=outline, width=w)


def poly(d, pts, fill, w=6, outline=INK):
    d.polygon([(x * C, y * C) for x, y in pts], fill=fill, outline=outline, width=w)


def line(d, pts, fill, w):
    d.line([(x * C, y * C) for x, y in pts], fill=fill, width=w, joint="curve")


def box(d, x0, y0, x1, y1, fill, w=6, outline=INK):
    d.rounded_rectangle([x0 * C, y0 * C, x1 * C, y1 * C], radius=int(.05 * C),
                        fill=fill, outline=outline, width=w)


# ---------------------------------------------------------------- 15 个造型
def i_whetstone(d):
    """磨刀石：石头 + 斜搁的刀刃 + 一点火花。"""
    box(d, .16, .56, .84, .80, STONE)
    poly(d, [(.20, .54), (.78, .40), (.80, .49), (.22, .63)], IRON)
    poly(d, [(.20, .54), (.78, .40), (.76, .35), (.18, .49)], IRON_D)
    for i in range(3):
        line(d, [(.30 + i * .10, .28), (.36 + i * .10, .16)], GOLD, 5)


def i_pouch(d):
    """火药袋：捆口布袋 + 冒出的火星。"""
    poly(d, [(.26, .74), (.30, .42), (.70, .42), (.74, .74), (.62, .86), (.38, .86)], ASH)
    box(d, .34, .34, .66, .44, WOOD_D)
    for i, (x, y) in enumerate(((.44, .20), (.54, .12), (.62, .22))):
        ell(d, x * C, y * C, .045 * C, .045 * C, GOLD, w=3)


def i_scarecrow(d):
    """稻草人：十字木架 + 头 + 草裙。"""
    line(d, [(.50, .22), (.50, .86)], WOOD_D, 14)
    line(d, [(.22, .42), (.78, .42)], WOOD_D, 14)
    poly(d, [(.30, .60), (.70, .60), (.80, .86), (.20, .86)], (214, 176, 96, 255))
    ell(d, .50 * C, .26 * C, .13 * C, .13 * C, (222, 198, 150, 255))
    ell(d, .45 * C, .25 * C, .022 * C, .022 * C, INK, w=0, outline=None)
    ell(d, .55 * C, .25 * C, .022 * C, .022 * C, INK, w=0, outline=None)


def i_snail_shell(d):
    """蜗牛壳：螺旋。"""
    ell(d, .50 * C, .52 * C, .32 * C, .32 * C, (206, 168, 128, 255))
    for r, col in ((.22, INK), (.15, INK), (.08, INK)):
        d.arc([(.50 - r) * C, (.52 - r) * C, (.50 + r) * C, (.52 + r) * C],
              20, 300, fill=col, width=6)
    poly(d, [(.16, .74), (.34, .78), (.30, .88), (.14, .84)], (186, 148, 110, 255))


def i_tonic(d):
    """补剂瓶：玻璃瓶 + 根须 + 液面。"""
    box(d, .38, .14, .62, .28, WOOD_D)
    poly(d, [(.34, .28), (.66, .28), (.72, .60), (.72, .82), (.28, .82), (.28, .60)], (150, 206, 176, 255))
    poly(d, [(.30, .56), (.70, .56), (.72, .82), (.28, .82)], GREEN_D, w=6)
    for i in range(4):
        line(d, [(.36 + i * .09, .82), (.34 + i * .09, .94)], WOOD_D, 5)


def i_fang_charm(d):
    """护符：尖牙 + 挂绳。"""
    line(d, [(.32, .16), (.50, .30), (.68, .16)], WOOD_D, 8)
    poly(d, [(.50, .28), (.68, .50), (.50, .88), (.32, .50)], BONE)
    poly(d, [(.50, .28), (.60, .44), (.50, .62), (.40, .44)], (208, 196, 172, 255), w=4)


def i_boots(d):
    """疾行靴：靴筒 + 翅膀。"""
    poly(d, [(.30, .18), (.58, .18), (.60, .58), (.86, .66), (.86, .84), (.26, .84), (.26, .58)], WOOD)
    box(d, .26, .66, .86, .86, WOOD_D, w=6)
    for i in range(3):
        line(d, [(.20 - i * .05, .40 + i * .09), (.02 - i * .02, .34 + i * .09)], BONE, 5)


def i_lens(d):
    """放大镜：镜圈 + 玻璃 + 柄。"""
    ell(d, .44 * C, .42 * C, .28 * C, .28 * C, (176, 216, 236, 190))
    ell(d, .44 * C, .42 * C, .28 * C, .28 * C, None, w=10, outline=WOOD)
    line(d, [(.62, .62), (.86, .88)], WOOD, 20)


def i_hourglass(d):
    """沙漏：上下框 + 沙。"""
    box(d, .22, .10, .78, .22, WOOD_D)
    box(d, .22, .78, .78, .90, WOOD_D)
    poly(d, [(.28, .22), (.72, .22), (.56, .50), (.72, .78), (.28, .78), (.44, .50)], (196, 226, 238, 170))
    poly(d, [(.32, .74), (.68, .74), (.58, .78), (.42, .78)], GOLD, w=4)


def i_clover(d):
    """幸运草：四片心形叶 + 柄。"""
    for a in (45, 135, 225, 315):
        r = math.radians(a)
        ell(d, (.50 + math.cos(r) * .17) * C, (.44 + math.sin(r) * .17) * C, .145 * C, .145 * C, GREEN)
    line(d, [(.50, .58), (.48, .90)], GREEN_D, 10)


def i_rune(d):
    """重击符文：石板 + 刻痕。"""
    poly(d, [(.26, .80), (.30, .26), (.70, .22), (.76, .78), (.50, .90)], STONE)
    line(d, [(.42, .38), (.58, .66)], INK, 8)
    line(d, [(.58, .38), (.42, .66)], INK, 8)


def i_seal(d):
    """商人印：印章 + 印面。"""
    box(d, .36, .14, .64, .34, RED)
    box(d, .28, .34, .72, .56, WOOD_D)
    ell(d, .50 * C, .74 * C, .26 * C, .16 * C, (196, 74, 68, 255))
    ell(d, .50 * C, .74 * C, .10 * C, .07 * C, GOLD, w=3)


def i_heart_pod(d):
    """爱心种荚：豆荚 + 里面的心。"""
    d.arc([.22 * C, .18 * C, .78 * C, .92 * C], 200, 340, fill=GREEN_D, width=16)
    poly(d, [(.50, .34), (.66, .48), (.50, .74), (.34, .48)], RED)
    line(d, [(.50, .12), (.50, .30)], GREEN, 8)


def i_impact_stone(d):
    """撞击石：石头 + 冲击线。"""
    poly(d, [(.28, .74), (.34, .40), (.62, .30), (.78, .58), (.66, .82)], STONE)
    for i, a in enumerate((-40, 10, 60)):
        r0, r1 = .34, .46
        line(d, [(.50 + math.cos(math.radians(a)) * r0, .50 - math.sin(math.radians(a)) * r0),
                 (.50 + math.cos(math.radians(a)) * r1, .50 - math.sin(math.radians(a)) * r1)], GOLD, 6)


def i_blast_core(d):
    """爆裂核心：核心球 + 放射。"""
    for a in range(0, 360, 45):
        r = math.radians(a)
        line(d, [(.50 + math.cos(r) * .22, .50 + math.sin(r) * .22),
                 (.50 + math.cos(r) * .42, .50 + math.sin(r) * .42)], GOLD, 7)
    ell(d, .50 * C, .50 * C, .24 * C, .24 * C, (248, 156, 72, 255))


ICONS = {
    "iron_hoe": i_whetstone, "ash_pouch": i_pouch, "scarecrow": i_scarecrow,
    "snail_shell": i_snail_shell, "root_tonic": i_tonic, "leech_vine": i_fang_charm,
    "sprinter_boots": i_boots, "lens": i_lens, "hourglass": i_hourglass,
    "lucky_clover": i_clover, "crit_charm": i_rune, "merchant_seal": i_seal,
    "heart_seed": i_heart_pod, "dew_stone": i_impact_stone, "blast_core": i_blast_core,
}


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for item_id, fn in ICONS.items():
        img = Image.new("RGBA", (C, C), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        fn(d)
        img = img.resize((SIZE, SIZE), Image.LANCZOS)
        img.save(os.path.join(OUT_DIR, "icon_item_" + item_id + ".png"))
    print(f"[SEEDFALL-ITEMICON] {len(ICONS)} 个 -> {OUT_DIR}")


if __name__ == "__main__":
    main()
