# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · UI 资源生成器
==================================
produce 一套可九宫格拉伸的界面素材 + 图标。

美术纪律（与 3D 侧同一套语言）：
  · **每块面板都有墨线描边**——和角色/植物的 Seedfall/Toon 描边呼应，
    界面与场景才像同一个世界，而不是"引擎默认 UI 盖在游戏上"
  · 暖色低饱和：奶油底 + 木色边 + 墨线，取自参考美术的配色
  · 九宫格：所有可拉伸元素都留出 border，任意尺寸不变形

产出：Assets/Resources/Art/UI/*.png
用法：python gen_ui.py            （无参数打印用法）
      python gen_ui.py all
      python gen_ui.py panel button icon
"""

import os
import sys
import math

from PIL import Image, ImageDraw, ImageFilter

OUT_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\UI"

# ── 调色板（与 3D 侧一致）──
CREAM = (242, 232, 210, 255)
CREAM_D = (222, 208, 180, 255)
WOOD = (140, 98, 57, 255)
WOOD_D = (108, 74, 42, 255)
INK = (42, 36, 28, 255)
GOLD = (232, 194, 74, 255)
SLATE = (46, 42, 36, 245)
SLATE_L = (62, 57, 49, 255)
HP = (210, 84, 74, 255)
SHIELD = (111, 168, 220, 255)
WATER = (102, 168, 216, 255)
FERT = (217, 138, 60, 255)
EXP = (150, 200, 110, 255)
WHITE = (255, 255, 255, 255)

# ── v220：面板外投影的参数（九宫格 + 阴影共用一份，改这里就改全局观感）──
#   SHADOW_INSET = 本体离画布边的距离（= 阴影最外圈的厚度）
#   画布 = 本体 + 2×INSET ⇒ 80 = 64 + 16
SHADOW_INSET = 6
SHADOW_ALPHA = 52       # 阴影最深处的不透明度（0..255）；52 ≈ 20%
                        #   第一版用 78（30%）+ inset 8：实测在 1920 宽下成了一条硬邦邦的暗边 ✗
                        #   九宫格会把"边"那条 1px 拉伸成整条边 ⇒ 影子必须够淡，否则就是描了个黑框
SHADOW_BLUR = 2.4       # 柔化半径（像素）—— 需求写的是「1~2px 柔化」
SHADOW_DY = 1           # 光源在左上 ⇒ 阴影整体下沉 1px（2px 时下沿明显偏重）


def ensure_dir():
    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR, exist_ok=True)


def save(img, name):
    path = os.path.join(OUT_DIR, name + ".png")
    img.save(path)
    print(f"[SEEDFALL-UI] {name}.png  {img.width}x{img.height}")
    return path


# ---------------------------------------------------------------- 九宫格面板
def ninepatch(name, size, border, fill, edge, ink=INK, radius=None, top_hl=True):
    """
    一块可九宫格拉伸的面板：圆角 + 木色边 + 墨线。
    border 必须 < size/2，否则中间那格就没有可拉伸区域。
    """
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = radius if radius is not None else border

    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=r, fill=fill,
                        outline=edge, width=max(2, border // 5))
    # 墨线：再往里收一圈画深色，形成"手绘勾边"的双线感
    inset = max(2, border // 4)
    d.rounded_rectangle([inset, inset, size - 1 - inset, size - 1 - inset],
                        radius=max(1, r - inset), outline=ink, width=1)
    if top_hl:
        # 顶部一道浅高光，让面板有厚度
        d.line([(r, inset + 2), (size - r, inset + 2)], fill=(255, 255, 255, 70), width=2)
    return save(img, name)


# ---------------------------------------------------------------- 面板外投影（v220）
def ninepatch_shadow(name, body_size, radius, fill, edge, ink=INK,
                     inset=SHADOW_INSET, alpha=SHADOW_ALPHA, blur=SHADOW_BLUR, dy=SHADOW_DY):
    """
    ★「面板外投影」★ —— 用户需求："UI 高级化处理"，面板要有**外投影**、1~2px 柔化。

    为什么要改贴图、而不是在画的时候先画一个影子框：
      面板是用 `GUI.Box(rect, none, UiSkin.PanelStyle)` 画的（九宫格），
      调用点在 `LoginHud`/`ImGuiHud` 里 —— 那些目录本轮**禁改**。
      九宫格样式唯一的抓手就是**贴图本身**，所以把影子**烘进贴图**：
        · 画布放大到 `本体 + 2×inset`（80 = 64 + 16）
        · 本体仍然画在画布正中、仍是 64px、圆角仍是 18px ⇒ **本体看着和改前一模一样**
        · 外圈 8px 是一条高斯柔化的黑影（下沉 2px，光源在左上）
        · `UiSkin` 的 border 相应从 18 改成 26（= 8 + 18），九宫格拉伸区不变形

    这样做的结果是**观感变、布局不变**：
      绘制用的 `Rect` 一个都没动，本体在 Rect 里内缩 8px（四周各 8px 被影子占掉），
      而文字的绘制矩形由调用方画在别处（`GUI.Box` 传的是 `GUIContent.none`）⇒ 文字位置不变 ✓
    """
    size = body_size + inset * 2

    # ① 影子：把本体形状画成白块 → 高斯模糊 → 当黑色蒙版的 alpha
    mask = Image.new("L", (size, size), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([inset, inset + dy, size - 1 - inset, size - 1 - inset + dy],
                         radius=radius, fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(blur))
    mask = mask.point(lambda v: int(v * alpha / 255))

    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow = Image.new("RGBA", (size, size), (16, 12, 8, 255))
    shadow.putalpha(mask)
    img = Image.alpha_composite(img, shadow)

    # ② 本体：与 `ninepatch` 同一套画法，只是整块搬到 inset 里面
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = inset, inset, size - 1 - inset, size - 1 - inset
    d.rounded_rectangle([x0, y0, x1, y1], radius=radius, fill=fill,
                        outline=edge, width=3)
    ring = max(3, radius // 5)
    d.rounded_rectangle([x0 + ring, y0 + ring, x1 - ring, y1 - ring],
                        radius=max(1, radius - ring), outline=ink, width=1)
    # 顶部高光（凸起感）+ 底部内阴影（厚度）：一升一降，面板才不像一张纸
    d.line([(x0 + radius, y0 + 3), (x1 - radius, y0 + 3)], fill=(255, 255, 255, 88), width=2)
    d.line([(x0 + radius, y1 - 3), (x1 - radius, y1 - 3)], fill=(0, 0, 0, 46), width=2)
    # 外缘一道极薄的亮线：把面板从背景里"拎"出来（高级感的来源）
    d.rounded_rectangle([x0 - 1, y0 - 1, x1 + 1, y1 + 1], radius=radius + 1,
                        outline=(255, 255, 255, 30), width=1)
    return save(img, name)


# ---------------------------------------------------------------- 条
def bar_frame(name, size=16, border=6):
    """血条/蓄水条的边框（九宫格，可拉成任意长度）。"""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=border, fill=(30, 26, 22, 235),
                        outline=INK, width=1)
    d.rounded_rectangle([1, 1, size - 2, size - 2], radius=border - 1,
                        outline=(255, 255, 255, 40), width=1)
    return save(img, name)


def bar_fill(name, color, size=16):
    """条的填充色块（九宫格，左右两端圆角、中间拉伸）。"""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=size // 2 - 1, fill=color)
    # 上半段提亮，做出圆柱感
    d.rounded_rectangle([1, 1, size - 2, size // 2], radius=size // 3,
                        fill=(min(255, color[0] + 45), min(255, color[1] + 45), min(255, color[2] + 45), 255))
    return save(img, name)


# ---------------------------------------------------------------- 文本框与滑动条（v71）
def textbox(name, size=48, border=10):
    """
    卡面上的正文框：**内凹**的底。
    和 panel 的区别是"凸/凹"——panel 是木色边 + 顶高光（凸起），
    正文框要反过来：内凹的阴影 + 无高光。

    v74：底色从深灰 (44,40,36) 换成**奶油木色**。
    深色底在一整套奶油色卡片里像块补丁——凹感应该靠**阴影**做，不是靠把它涂黑。
    现在：比 panel 略深一点的暖米色 + 上/左内侧暗影 + 下/右内侧微亮，读起来是"凹进去的纸"。
    """
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    fill = (222, 205, 172, 255)          # 比 panel 的 CREAM 略深一档
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=border // 2,
                        fill=fill, outline=(168, 138, 96, 255), width=2)
    # 内侧上方/左侧压暗：光源在左上，凹槽的经典画法
    d.arc([2, 2, size - 3, border * 2], 180, 360, fill=(140, 112, 74, 190), width=3)
    d.line([(2, border // 2), (2, size - border // 2)], fill=(140, 112, 74, 150), width=2)
    # 内侧下方/右侧提亮：凹槽的受光边
    d.line([(border, size - 3), (size - border, size - 3)], fill=(255, 250, 235, 110), width=1)
    d.line([(size - 3, border), (size - 3, size - border)], fill=(255, 250, 235, 90), width=1)
    return save(img, name)


def scroll_track(name, size=16):
    """滑动条轨道：细长凹槽。九宫格里只有中间那格会被拉伸。"""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = size // 2
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=r // 2,
                        fill=(38, 34, 30, 220), outline=INK, width=1)
    return save(img, name)


def scroll_thumb(name, size=16):
    """
    滑动条滑块：**两端圆头**的浅色条 + 中间两道横纹。
    圆头很关键——方块头的滑块看起来像没做完的占位图。
    """
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = size // 2 - 1
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=r,
                        fill=(214, 192, 156, 255), outline=INK, width=2)
    d.line([(2, size // 2), (size - 3, size // 2)], fill=(150, 128, 96, 220), width=2)
    d.line([(3, size // 2 - 3), (size - 4, size // 2 - 3)], fill=(255, 255, 255, 90), width=1)
    return save(img, name)


# ---------------------------------------------------------------- 图标
def _icon_canvas(size=64):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def _outline(d, pts, fill, ink=INK, width=3):
    d.polygon(pts, fill=fill, outline=ink, width=width)


def icon(name, kind):
    S = 64
    img, d = _icon_canvas(S)

    if kind == "hoe":
        # 锄头：斜柄 + 横刃
        d.line([(14, 54), (46, 16)], fill=WOOD, width=7)
        d.line([(14, 54), (46, 16)], fill=INK, width=1)
        _outline(d, [(38, 12), (58, 20), (54, 28), (34, 20)], (201, 203, 203, 255))

    elif kind == "can":
        # 水壶：壶身 + 壶嘴 + 提手
        d.rounded_rectangle([16, 26, 50, 54], radius=7, fill=(122, 150, 176, 255), outline=INK, width=3)
        _outline(d, [(46, 30), (60, 18), (64, 24), (50, 38)], (122, 150, 176, 255))
        d.arc([24, 12, 44, 30], start=200, end=340, fill=INK, width=3)
        d.ellipse([28, 34, 40, 46], fill=(160, 200, 232, 255))

    elif kind == "fertilizer":
        # 肥料：扎口布袋
        _outline(d, [(20, 24), (44, 24), (52, 56), (12, 56)], (150, 110, 70, 255))
        d.rectangle([26, 16, 38, 24], fill=(120, 88, 56, 255), outline=INK, width=2)
        d.line([(20, 24), (44, 24)], fill=INK, width=3)
        for i, y in enumerate((34, 42, 50)):
            d.line([(20, y), (44, y)], fill=(120, 88, 56, 255), width=2)

    elif kind == "coin":
        d.ellipse([8, 8, 56, 56], fill=GOLD, outline=INK, width=3)
        d.ellipse([16, 16, 48, 48], outline=(255, 240, 190, 200), width=2)
        d.ellipse([22, 20, 34, 30], fill=(255, 246, 210, 220))

    elif kind == "seed":
        _outline(d, [(32, 10), (48, 32), (32, 54), (16, 32)], (168, 200, 120, 255))
        d.line([(32, 16), (32, 46)], fill=(110, 150, 80, 255), width=2)

    elif kind == "heart":
        d.ellipse([12, 14, 34, 36], fill=HP)
        d.ellipse([30, 14, 52, 36], fill=HP)
        _outline(d, [(12, 28), (32, 56), (52, 28)], HP)

    elif kind == "shield":
        _outline(d, [(32, 8), (54, 18), (50, 42), (32, 56), (14, 42), (10, 18)], SHIELD)
        d.line([(32, 12), (32, 52)], fill=(200, 226, 246, 180), width=2)

    elif kind == "clock":
        d.ellipse([10, 10, 54, 54], fill=CREAM, outline=INK, width=3)
        d.line([(32, 32), (32, 18)], fill=INK, width=4)
        d.line([(32, 32), (43, 38)], fill=INK, width=4)

    elif kind == "wave":
        # 波次
        for i, x in enumerate((12, 28, 44)):
            h = (18, 30, 24)[i]
            d.rounded_rectangle([x - 5, 52 - h, x + 5, 52], radius=3, fill=GOLD, outline=INK, width=2)

    elif kind == "star":
        pts = []
        for i in range(10):
            a = math.radians(-90 + i * 36)
            r = 26 if i % 2 == 0 else 11
            pts.append((32 + math.cos(a) * r, 32 + math.sin(a) * r))
        _outline(d, pts, GOLD)

    else:
        d.ellipse([10, 10, 54, 54], fill=CREAM_D, outline=INK, width=3)

    return save(img, "icon_" + kind)


# ---------------------------------------------------------------- 入口
def build_all():
    ensure_dir()
    # ── 面板：v220 起带「外投影」──
    #   本体 64px / 圆角 18px 不变，画布 80px，外圈 8px 是柔化黑影
    #   ⇒ UiSkin 的 border 必须同步改成 8+18 = 26（见 Scripts/Art/UiSkin.cs）
    ninepatch_shadow("panel", 64, 18, CREAM, WOOD)
    ninepatch_shadow("panel_dark", 64, 18, SLATE, SLATE_L, ink=(20, 18, 15, 255))
    ninepatch_shadow("card", 64, 16, (250, 244, 228, 255), (200, 170, 120, 255))

    # 按钮三态：**不加影子**（按钮的标签是 GUI.Button 连着样式一起画的，
    # 加内缩会把文字推走 = 改布局 ✗）。只加"底边压暗 + 顶边提亮"的厚度感。
    ninepatch("button", 48, 14, (228, 210, 176, 255), WOOD)
    ninepatch("button_hover", 48, 14, (246, 226, 160, 255), (176, 128, 62, 255))
    ninepatch("button_press", 48, 14, (198, 178, 142, 255), WOOD_D)

    # 条
    bar_frame("bar_frame")
    # 纯白填充：HUD 里各处用 GUI.color 染色复用同一张，避免为每种颜色各出一张图
    bar_fill("bar_white", WHITE)
    bar_fill("bar_hp", HP)
    bar_fill("bar_shield", SHIELD)
    bar_fill("bar_water", WATER)
    bar_fill("bar_fert", FERT)
    bar_fill("bar_exp", EXP)

    # 图标
    for k in ("hoe", "can", "fertilizer", "coin", "seed", "heart", "shield", "clock", "wave", "star"):
        icon(k, k)

    # v71：文本框与滑动条
    textbox("textbox")
    scroll_track("scroll_track")
    scroll_thumb("scroll_thumb")


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print("用法: python gen_ui.py all | panel | button | bar | icon")
        print("  产物目录:", OUT_DIR)
    elif args[0] == "all":
        build_all()
    else:
        ensure_dir()
        for a in args:
            if a == "panel":
                ninepatch_shadow("panel", 64, 18, CREAM, WOOD)
                ninepatch_shadow("panel_dark", 64, 18, SLATE, SLATE_L, ink=(20, 18, 15, 255))
                ninepatch_shadow("card", 64, 16, (250, 244, 228, 255), (200, 170, 120, 255))
            elif a == "button":
                ninepatch("button", 48, 14, (228, 210, 176, 255), WOOD)
                ninepatch("button_hover", 48, 14, (246, 226, 160, 255), (176, 128, 62, 255))
                ninepatch("button_press", 48, 14, (198, 178, 142, 255), WOOD_D)
            elif a == "bar":
                bar_frame("bar_frame")
                for c, n in ((WHITE, "bar_white"), (HP, "bar_hp"), (SHIELD, "bar_shield"),
                             (WATER, "bar_water"), (FERT, "bar_fert"), (EXP, "bar_exp")):
                    bar_fill(n, c)
            elif a == "icon":
                for k in ("hoe", "can", "fertilizer", "coin", "seed", "heart", "shield", "clock", "wave", "star"):
                    icon(k, k)
