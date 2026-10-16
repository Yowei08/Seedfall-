# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 2D 图标绘制（v70）
====================================
用 Pillow 直接**画**图标，取代原来的 3D 渲染（gen_icons.py）。

为什么换成 2D：
  渲染出来的图标带透视、明暗和高光，缩到 64px 就糊成一团色块；而且 16 个图标
  各有各的受光角度，排在一排里像 16 张不同的照片。2D 平涂 + 统一描边才*
  像"一套"图标。

三条绘制约定（与场上模型共用同一套语义）：
  ① 轮廓读得出玩法：近战有拳头/尖刺、远程有朝前的炮口、辅助有碗或光环
  ② 体量与华丽度跟稀有度走：用 size 决定占画布比例、用稀有度决定花瓣/斑点数量
  ③ 描边统一：深色粗描边 + 平涂色块，和 SeedfallToon 的墨线是同一套语言

★ 调色板不在这里重新抄一遍 ★
  颜色从 seedfall_kit.py / gen_plants.py 的源码里解析出来，
  避免"生成器里一套色、模型里另一套色"的漂移。
"""

import math
import os
import re
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "UI")

SIZE = 256                 # 画大一点再缩，边缘才干净
SS = 4                     # 超采样倍率
CANVAS = SIZE * SS
INK = (38, 32, 28, 255)    # 描边色，与 SeedfallToon 的 _OutlineColor 同源


# ---------------------------------------------------------------- 调色板解析
def load_palette():
    """
    从源码里解析出全部颜色名 → RGB。
    解析而不是复制：三处（kit 的 PALETTE、植物的 update、种子表的 body/accent）
    只要有一处改了，图标会自动跟上。
    """
    pal = {}
    for fn in ("seedfall_kit.py", "gen_plants.py"):
        path = os.path.join(HERE, fn)
        if not os.path.exists(path):
            continue
        src = open(path, encoding="utf-8").read()
        for name, hexv in re.findall(r'"(\w+)"\s*:\s*"(#[0-9A-Fa-f]{6})"', src):
            p = hexv.lstrip("#")
            pal[name] = (int(p[0:2], 16), int(p[2:4], 16), int(p[4:6], 16), 255)
    return pal


def load_plants():
    """解析 PLANTS 表：id → (kind, size, body, accent)。"""
    src = open(os.path.join(HERE, "gen_plants.py"), encoding="utf-8").read()
    out = {}
    for m in re.finditer(
        r'"(\w+)"\s*:\s*dict\(\s*kind="(\w+)"\s*,\s*size=([\d.]+)\s*,\s*'
        r'body="(\w+)"\s*,\s*accent="(\w+)"', src):
        out[m.group(1)] = dict(kind=m.group(2), size=float(m.group(3)),
                               body=m.group(4), accent=m.group(5))
    return out


# ---------------------------------------------------------------- 绘制基元
def ellipse(d, cx, cy, rx, ry, fill, outline=INK, w=6):
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=fill, outline=outline, width=w)


def poly(d, pts, fill, outline=INK, w=6):
    d.polygon(pts, fill=fill, outline=outline, width=w)


def leaf(d, cx, cy, rx, ry, ang_deg, fill, outline=INK, w=6):
    """一片叶子：旋转的椭圆。用旋转贴图实现，Pillow 没有旋转椭圆。"""
    layer = Image.new("RGBA", (int(rx * 2 + w * 2), int(ry * 2 + w * 2)), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.ellipse([w, w, rx * 2 + w, ry * 2 + w], fill=fill, outline=outline, width=w)
    layer = layer.rotate(ang_deg, resample=Image.BICUBIC, expand=True)
    d._image.paste(layer, (int(cx - layer.width / 2), int(cy - layer.height / 2)), layer)


# ---------------------------------------------------------------- 各类型的 2D 造型
def draw_root(d, body, accent, s, rare):
    """块根：土里的圆球 + 顶上散开的叶子。萝卜卫士。"""
    ellipse(d, .50 * s, .64 * s, .26 * s, .29 * s, body)
    for i, a in enumerate((-58, -22, 18, 56)):
        leaf(d, .50 * s + math.sin(math.radians(a)) * .17 * s,
             .34 * s - abs(a) * .0016 * s, .075 * s, .17 * s, a, accent)


def draw_shooter(d, body, accent, s, rare):
    """射手：茎 + 朝前的炮口 + 一对叶。豌豆射手。"""
    poly(d, [(.50 * s, .82 * s), (.58 * s, .46 * s), (.42 * s, .46 * s)], body)
    ellipse(d, .50 * s, .40 * s, .20 * s, .19 * s, body)
    ellipse(d, .50 * s, .40 * s, .11 * s, .10 * s, INK, outline=INK, w=3)   # 炮口
    leaf(d, .30 * s, .62 * s, .07 * s, .16 * s, -34, accent)
    leaf(d, .70 * s, .62 * s, .07 * s, .16 * s, 34, accent)
    ellipse(d, .58 * s, .35 * s, .035 * s, .035 * s, (255, 255, 255, 255), outline=None, w=0)


def draw_fungus(d, body, accent, s, rare):
    """菌类：伞盖 + 菌柄 + 斑点。毒蘑菇。"""
    poly(d, [(.42 * s, .84 * s), (.46 * s, .50 * s), (.54 * s, .50 * s), (.58 * s, .84 * s)], accent)
    d.pieslice([.16 * s, .18 * s, .84 * s, .70 * s], 180, 360, fill=body, outline=INK, width=6)
    d.line([.16 * s, .44 * s, .84 * s, .44 * s], fill=INK, width=6)
    for i in range(min(5, 2 + rare)):
        a = -160 + i * (140.0 / max(1, min(4, 1 + rare)))
        cx = .50 * s + math.cos(math.radians(a)) * .21 * s
        cy = .44 * s + math.sin(math.radians(a)) * .15 * s
        ellipse(d, cx, cy, .052 * s, .038 * s, accent, outline=None, w=0)


def draw_ivy(d, body, accent, s, rare):
    """藤蔓：一根弯曲主蔓 + 成对的刺叶。寒冰藤。"""
    pts = [(.50 * s + math.sin(i * .5) * .16 * s, .86 * s - i * .105 * s) for i in range(8)]
    d.line(pts, fill=accent, width=int(.055 * s), joint="curve")
    for i in range(1, 7):
        x, y = pts[i]
        side = -1 if i % 2 else 1
        leaf(d, x + side * .13 * s, y, .08 * s, .045 * s, side * 24, body)


def draw_gourd(d, body, accent, s, rare):
    """瓜：带棱的圆瓜 + 顶上的梗。荆棘南瓜。"""
    ellipse(d, .50 * s, .60 * s, .30 * s, .27 * s, body)
    for i in (-1, 0, 1):
        d.arc([.50 * s - .30 * s + i * .12 * s, .33 * s, .50 * s + .30 * s + i * .12 * s, .87 * s],
              250, 290, fill=INK, width=5)
    poly(d, [(.46 * s, .35 * s), (.50 * s, .20 * s), (.54 * s, .35 * s)], accent)


def draw_rock(d, body, accent, s, rare):
    """石拳：多面体石块 + 突出的拳面。石拳守卫。"""
    poly(d, [(.24 * s, .74 * s), (.30 * s, .40 * s), (.50 * s, .28 * s),
             (.72 * s, .42 * s), (.76 * s, .72 * s), (.50 * s, .84 * s)], body)
    for i in range(3):
        poly(d, [(.34 * s + i * .14 * s, .50 * s), (.42 * s + i * .14 * s, .44 * s),
                 (.46 * s + i * .14 * s, .56 * s), (.36 * s + i * .14 * s, .60 * s)], accent)


def draw_moss(d, body, accent, s, rare):
    """苔藓：一坨多瓣的绒球 + 几点亮斑。回春苔。"""
    for i in range(7):
        a = i * 51.4
        ellipse(d, .50 * s + math.cos(math.radians(a)) * .16 * s,
                .58 * s + math.sin(math.radians(a)) * .13 * s, .13 * s, .12 * s, body)
    ellipse(d, .50 * s, .58 * s, .18 * s, .17 * s, body)
    for i in range(3):
        ellipse(d, .40 * s + i * .12 * s, .50 * s, .035 * s, .035 * s, accent, outline=None, w=0)


def draw_flower(d, body, accent, s, rare):
    """花：环状花瓣 + 花心。数量随稀有度。"""
    n = 5 + min(3, rare)
    for i in range(n):
        a = i * (360.0 / n)
        leaf(d, .50 * s + math.sin(math.radians(a)) * .20 * s,
             .48 * s - math.cos(math.radians(a)) * .20 * s,
             .095 * s, .145 * s, -a, body)
    ellipse(d, .50 * s, .48 * s, .11 * s, .11 * s, accent)
    poly(d, [(.47 * s, .86 * s), (.50 * s, .58 * s), (.53 * s, .86 * s)], accent)


def draw_spring(d, body, accent, s, rare):
    """弹簧草：盘起来的圈 + 顶上的芽。跳跳草。"""
    for i in range(3):
        y = .78 * s - i * .17 * s
        d.arc([.28 * s, y - .10 * s, .72 * s, y + .10 * s], 0, 360, fill=body, width=int(.055 * s))
    ellipse(d, .50 * s, .22 * s, .085 * s, .075 * s, accent)
    ellipse(d, .50 * s, .78 * s, .26 * s, .075 * s, body)


def draw_tree(d, body, accent, s, rare):
    """月树：树干 + 层叠树冠。月桂树。"""
    poly(d, [(.44 * s, .84 * s), (.46 * s, .46 * s), (.54 * s, .46 * s), (.56 * s, .84 * s)], accent)
    for i, (cx, cy, r) in enumerate(((.50, .40, .21), (.32, .46, .15), (.68, .46, .15), (.50, .27, .15))):
        ellipse(d, cx * s, cy * s, r * s, r * .92 * s, body if i == 0 else accent)


DRAWERS = {
    "root": draw_root, "shooter": draw_shooter, "fungus": draw_fungus, "ivy": draw_ivy,
    "gourd": draw_gourd, "rock": draw_rock, "moss": draw_moss, "flower": draw_flower,
    "spring": draw_spring, "tree": draw_tree,
}


def build(plant_id, cfg, pal):
    kind = cfg["kind"]
    body = pal.get(cfg["body"], (150, 150, 150, 255))
    accent = pal.get(cfg["accent"], (110, 110, 110, 255))

    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d._image = img                       # 给 leaf() 用来贴旋转图层

    # size 决定占画布比例：大株占得满，小株留白多——一眼能分出品阶
    s = CANVAS * (0.72 + cfg["size"] * 0.42)
    off = (CANVAS - s) * 0.5
    d_shift = ImageDraw.Draw(img)

    # 用一个临时画布画主体，再居中贴回去
    body_img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body_img)
    bd._image = body_img
    rare = {"D": 0, "C": 1, "B": 2, "A": 3, "S": 4}.get("B", 1)
    DRAWERS.get(kind, draw_root)(bd, body, accent, s, rare)

    img.alpha_composite(body_img, (int(off), int(off * 0.35)))
    img = img.resize((SIZE, SIZE), Image.LANCZOS)

    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, "icon_plant_" + plant_id + ".png")
    img.save(path)
    return path


def main():
    pal = load_palette()
    plants = load_plants()
    if not plants:
        print("[SEEDFALL-ICON2D] 未解析到 PLANTS 表，检查 gen_plants.py 的格式")
        return
    n = 0
    for pid, cfg in plants.items():
        build(pid, cfg, pal)
        n += 1
    print(f"[SEEDFALL-ICON2D] 调色板 {len(pal)} 色 / 植物 {n} 个 -> {OUT_DIR}")


if __name__ == "__main__":
    main()
