# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · UI 九宫格贴图重制（v214 全局 UI 美观升级 · 交付项 ⑤）

重画 `Resources/Art/UI/` 下那批**九宫格**贴图。不改尺寸、不改 `UiSkin` 的 border，
所以 C# 一行都不用动 —— 换图 = 全局换皮（`ImGuiHud` 的每个面板/按钮/滑条都走这几张）。

──────────────────────────────────────────────────────────────────────
为什么这一轮要重画（改前 → 改后）
──────────────────────────────────────────────────────────────────────
改前：奶油底 + **浅棕**描边（`panel` 的上边实测 `#8C6239`）、圆角 6、无内衬、纯色填充。
     问题有两个，都是"读不出边界"：
       ① 描边是棕的不是黑的，压在暖光木色场景上**融进去了** —— 面板像"一块浅色污渍"，
          而不是一个浮在场景上的窗口；
       ② 面板内部是纯色，没有任何结构 ⇒ 大面积的奶油色在国内看着"空"。
改后：**近黑墨线**（`#14110E`，2 像素）+ 金色内衬（1 像素，inset 4）
      + 纸纹颗粒（低频噪点）+ 上亮下暗的竖向渐变 + 内阴影（顶部压暗 1 行）。
     ——"黑色描边"这条用户要求在这里同样成立：UI 的边框是它的一部分，
        而且这里是**唯一能 100% 控制的地方**（世界里的描边受材质/机位影响）。

──────────────────────────────────────────────────────────────────────
硬约束（照抄 `UiSkin.cs`，改这里必须同步改那边）
──────────────────────────────────────────────────────────────────────
  panel/panel_dark/card = 64×64，border 18/18/16
  textbox = 48×48，border 10
  button* = 48×48，border 14
  bar_frame/bar_white = 16×16，border 6
  scroll_track = 16×16，border 6；scroll_thumb = 16×16，border 7
九宫格要求 border 区域**不可拉伸**：所以圆角与外框必须完全落在 border 像素之内，
否则面板拉伸时圆角会被拉成椭圆。下面每个件的 `border` 都写死并与 `UiSkin` 对齐。

用法：
    python gen_ui_ninepatch.py            # 重画全部
    python gen_ui_ninepatch.py --help
    python gen_ui_ninepatch.py --out <目录>
"""

import argparse
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_OUT = os.path.join(PROJECT, "Assets", "Resources", "Art", "UI")

SEED = 20261019

# ── 调色板：与 `TownHud` 的公开调色板同源（Cream / Ink / Wood / Gold）──
INK = (20, 17, 14, 255)          # 墨线：近黑
INK_SOFT = (60, 48, 38, 255)     # 内阴影
CREAM = (244, 234, 212)          # 主底
CREAM_HI = (252, 246, 230)       # 渐变上端
CREAM_LO = (228, 214, 188)       # 渐变下端
GOLD = (196, 156, 74)            # 金内衬
GOLD_HI = (240, 208, 118)
WOOD = (150, 108, 68)
WOOD_HI = (176, 134, 92)
WOOD_LO = (110, 78, 48)
DARK_BG = (34, 30, 26)
DARK_HI = (50, 44, 38)


def rounded_mask(size, radius, border=0):
    """圆角矩形遮罩（L 模式）。border>0 时向内收 border 像素。"""
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([border, border, size - 1 - border, size - 1 - border],
                        radius=max(0, radius - border), fill=255)
    return m


def vertical_gradient(size, top, bottom):
    img = Image.new("RGB", (size, size))
    d = ImageDraw.Draw(img)
    for y in range(size):
        t = y / max(1, size - 1)
        col = tuple(int(top[i] * (1 - t) + bottom[i] * t) for i in range(3))
        d.line([0, y, size, y], fill=col)
    return img


def paper_grain(size, amount=5, seed=0):
    """
    纸纹：**低频**噪点（先画在 1/4 分辨率再放大）。
    直接在原分辨率撒白噪点会被压缩糊掉、而且会让文字边缘发毛；
    1/4 分辨率放大后是"纸的纤维感"，不是"电视雪花" ✓
    """
    rng = random.Random(SEED + seed)
    small = 8
    g = Image.new("L", (small, small))
    gp = g.load()
    for y in range(small):
        for x in range(small):
            gp[x, y] = 128 + rng.randint(-amount * 6, amount * 6)
    return g.resize((size, size), Image.BICUBIC).filter(ImageFilter.SMOOTH)


def apply_grain(img, amount=5, seed=0):
    grain = paper_grain(img.size[0], amount, seed)
    px = img.load()
    gp = grain.load()
    size = img.size[0]
    for y in range(size):
        for x in range(size):
            k = (gp[x, y] - 128) / 128.0
            r, g_, b = px[x, y][:3]
            px[x, y] = (max(0, min(255, int(r + k * 10 * amount / 5.0))),
                        max(0, min(255, int(g_ + k * 10 * amount / 5.0))),
                        max(0, min(255, int(b + k * 10 * amount / 5.0))))
    return img


def panel(size=64, radius=8, border=18, top=CREAM_HI, bottom=CREAM_LO, seed=1):
    """
    面板：圆角 + 近黑墨线（2px）+ 金内衬（1px, inset 4）+ 竖向渐变 + 纸纹。
    墨线落在 border 之外/之上都无所谓，只要**形状**不超出 border 区域即可。
    """
    img = vertical_gradient(size, top, bottom)
    img = apply_grain(img, 5, seed)

    # 内衬金线（画在墨线里面 1~2 像素）
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([3, 3, size - 4, size - 4], radius=radius - 2, outline=GOLD, width=1)
    # 顶部内阴影：一条柔和的暗线，制造"往里面凹"的读数
    d.line([border - 4, 2, size - border + 3, 2], fill=INK_SOFT, width=1)

    # 近黑墨线：2 像素
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=radius, outline=INK, width=2)

    # 圆角外裁切（四角必须透明，否则九宫格拉伸会出现直角块）
    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


def dark_panel(size=64, radius=8, border=18):
    """深色面板（HUD 上的浮层底）：墨线同样近黑，但内衬换成冷灰，免得暖色发腻。"""
    img = vertical_gradient(size, DARK_HI, DARK_BG)
    img = apply_grain(img, 4, 7)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([3, 3, size - 4, size - 4], radius=radius - 2,
                        outline=(96, 90, 78), width=1)
    d.line([border - 4, 2, size - border + 3, 2], fill=(10, 8, 6), width=1)
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=radius, outline=INK, width=2)
    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


def card(size=64, radius=6, border=16):
    """商品卡：比面板更亮、墨线更细（1px）——卡片是**次级**容器，不该和面板抢边界。"""
    img = vertical_gradient(size, (252, 246, 232), (238, 226, 202))
    img = apply_grain(img, 4, 11)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([2, 2, size - 3, size - 3], radius=radius - 1, outline=(150, 120, 80), width=1)
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=radius, outline=INK, width=1)
    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


def textbox(size=48, radius=5, border=10):
    """输入框：比面板更**暗**、内阴影更重 —— 一眼看出"这里能打字"。"""
    img = vertical_gradient(size, (206, 188, 158), (226, 210, 180))
    img = apply_grain(img, 3, 13)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([2, 2, size - 3, size - 3], radius=radius - 1, fill=None,
                        outline=(120, 92, 60), width=1)
    # 顶部的内阴影（凹进去）
    d.line([3, 2, size - 4, 2], fill=(70, 54, 38), width=2)
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=radius, outline=INK, width=1)
    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


def button(size=48, radius=7, border=14, mode="normal"):
    """
    按钮三态。**三态之间的差别必须一眼可读**，否则玩家不知道"我到底点没点上"：
      normal：暖木色，上亮下暗
      hover ：整体提亮 + 金边更亮（鼠标停在这里）
      press ：整体压暗 + 竖向渐变**反转**（凹进去），金边变暗
    """
    if mode == "normal":
        top, bot, edge, glow = WOOD_HI, WOOD_LO, GOLD, None
    elif mode == "hover":
        top, bot, edge, glow = (206, 162, 108), (146, 104, 62), GOLD_HI, True
    else:
        top, bot, edge, glow = (96, 68, 42), (128, 94, 58), (150, 116, 74), False

    img = vertical_gradient(size, top, bot)
    img = apply_grain(img, 4, 17)
    d = ImageDraw.Draw(img)

    # 顶部 1/3 的高光带（normal/hover）；press 时改画底部 → 读作"凹下去"
    if mode == "press":
        d.rectangle([2, size - 7, size - 3, size - 3], fill=tuple(min(255, c + 26) for c in bot))
    else:
        d.rectangle([2, 2, size - 3, 7], fill=tuple(min(255, c + 30) for c in top))

    # 金/暗内衬
    d.rounded_rectangle([3, 3, size - 4, size - 4], radius=radius - 2, outline=edge, width=1)
    if glow:
        d.rounded_rectangle([2, 2, size - 3, size - 3], radius=radius - 1, outline=(255, 236, 176), width=1)

    # 近黑墨线（2px）—— 与面板同一支笔
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=radius, outline=INK, width=2)

    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


def bar_frame(size=16, radius=3, border=6):
    """条框：深底 + 墨线。border=6 ⇒ 中间只剩 4 像素可拉伸，够用。"""
    img = Image.new("RGB", (size, size), (40, 34, 28))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=radius, outline=INK, width=1)
    d.rounded_rectangle([2, 2, size - 3, size - 3], radius=max(0, radius - 1),
                        outline=(90, 80, 68), width=1)
    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


def bar_white(size=16, radius=3, border=6):
    """填充条：纯白（**颜色由 IMGUI 的 GUI.color 上色**，所以这里必须是白的）。"""
    img = Image.new("RGB", (size, size), (255, 255, 255))
    d = ImageDraw.Draw(img)
    # 顶部一条极淡的高光，让条子有厚度；不能压暗太多，否则上色后颜色会脏
    d.line([1, 1, size - 2, 1], fill=(255, 255, 255), width=1)
    d.line([1, size - 2, size - 2, size - 2], fill=(226, 226, 226), width=1)
    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


def scroll_track(size=16, radius=4, border=6):
    img = Image.new("RGB", (size, size), (58, 50, 42))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=radius, outline=(24, 20, 16), width=1)
    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


def scroll_thumb(size=16, radius=4, border=7):
    img = vertical_gradient(size, WOOD_HI, WOOD_LO)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=radius, outline=INK, width=1)
    mask = rounded_mask(size, radius, 0)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img.convert("RGBA"), (0, 0), mask)
    return out


TARGETS = {
    "panel": lambda: panel(),
    "panel_dark": lambda: dark_panel(),
    "card": lambda: card(),
    "textbox": lambda: textbox(),
    "button": lambda: button(mode="normal"),
    "button_hover": lambda: button(mode="hover"),
    "button_press": lambda: button(mode="press"),
    "bar_frame": lambda: bar_frame(),
    "bar_white": lambda: bar_white(),
    "scroll_track": lambda: scroll_track(),
    "scroll_thumb": lambda: scroll_thumb(),
}


def main():
    ap = argparse.ArgumentParser(
        description="重画 UI 九宫格贴图（近黑墨线 + 金内衬 + 纸纹，尺寸与 border 与 UiSkin 对齐）")
    ap.add_argument("--out", default=DEFAULT_OUT, help="输出目录（默认工程内 Art/UI）")
    ap.add_argument("--only", default=None, help="只画一张（名字见脚本里的 TARGETS）")
    args = ap.parse_args()

    keys = list(TARGETS)
    if args.only:
        if args.only not in TARGETS:
            ap.error("未知名字 '%s'，可选：%s" % (args.only, ", ".join(keys)))
        keys = [args.only]

    os.makedirs(args.out, exist_ok=True)
    for k in keys:
        img = TARGETS[k]()
        path = os.path.join(args.out, k + ".png")
        img.save(path, optimize=True)
        print("[SEEDFALL-UI] %-14s %dx%d -> %s" % (k, img.size[0], img.size[1], path))
    print("[SEEDFALL-UI] 共 %d 张，输出目录 %s，种子 %d" % (len(keys), args.out, SEED))


if __name__ == "__main__":
    main()
