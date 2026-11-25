#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Seedfall 守园者 —— 插件图标程序化生成器（12 张 64x64 RGBA PNG）

重跑命令：
"C:\\Users\\18462\\.dsh\\dsh-runtimes\\dsh-primary-runtime\\dependencies\\python\\python.exe" "D:\\Unity\\project\\Seedfall守园者\\Tools\\icon_gen\\gen_plugin_icons.py"

规格：
  64x64 / RGBA / 完全透明底
  深色圆角底框 #1A1410：内缩 2px 圆角矩形 rounded_rectangle([2,2,61,61], radius=8)
  单色几何符号（无文字、无渐变）

输出：D:\\Unity\\project\\Seedfall守园者\\Assets\\Resources\\Art\\UI\\icon_plugin_<id>.png
  文件名沿用工程既有命名（icon_plugin_ 前缀），原地覆盖，保留 Unity 已生成的 .meta/GUID。

边界：只写本脚本与这 12 张 PNG；不改 .cs、不碰 .json、不开 Unity、不用桥。
"""

import math
import os
import sys

from PIL import Image, ImageDraw

# ---------------------------------------------------------------- 常量

OUT_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\UI"
NAME_FMT = "icon_plugin_%s.png"        # 工程既有命名，勿改，否则 Unity 会当成新资源

SIZE = 64
PANEL_FILL = (26, 20, 16, 255)         # #1A1410
PANEL_BOX = [2, 2, 61, 61]             # 内缩 2px
PANEL_RADIUS = 8


def hx(s):
    """#RRGGBB -> (r, g, b, 255)"""
    s = s.lstrip("#")
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), 255)


def base():
    """透明画布 + 深色圆角底框。"""
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(img).rounded_rectangle(
        PANEL_BOX, radius=PANEL_RADIUS, fill=PANEL_FILL
    )
    return img


def star_poly(cx, cy, r_out, r_in, points=8, rot=-math.pi / 2):
    """n 角星顶点（rot=-90° 时一个尖角朝上）。"""
    verts = []
    for i in range(points * 2):
        ang = rot + i * math.pi / points
        r = r_out if i % 2 == 0 else r_in
        verts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
    return verts


def droplet(d, cx, cy, r, c):
    """下坠液滴：圆 + 下方收尖三角。"""
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=c)
    d.polygon(
        [(cx, cy + r * 2.0),
         (cx - r * 0.72, cy + r * 0.25),
         (cx + r * 0.72, cy + r * 0.25)],
        fill=c,
    )


# ---------------------------------------------------------------- 12 个图标

def ico_hoe_swordwave():
    """三道斜线，左下 -> 右上，长度递减。"""
    c = hx("#E8F1FF")
    img = base()
    d = ImageDraw.Draw(img)
    d.line([(11, 53), (52, 12)], fill=c, width=5)      # 最长
    d.line([(17, 51), (45, 23)], fill=c, width=4)      # 中
    d.line([(25, 48), (41, 32)], fill=c, width=3)      # 最短
    return img, c


def ico_hoe_burn():
    """三瓣火苗：主椭圆 + 尖顶 + 左右两个小椭圆。"""
    c = hx("#FF8243")
    img = base()
    d = ImageDraw.Draw(img)
    d.ellipse([24, 20, 41, 48], fill=c)                # 主火焰
    d.polygon([(32, 9), (24, 30), (41, 30)], fill=c)   # 尖顶
    d.ellipse([14, 31, 26, 45], fill=c)                # 左瓣
    d.ellipse([40, 33, 51, 45], fill=c)                # 右瓣
    return img, c


def ico_hoe_doublestrike():
    """两道平行斜刃 + 一个小箭头。"""
    c = hx("#F2C14E")
    img = base()
    d = ImageDraw.Draw(img)
    d.line([(11, 43), (42, 12)], fill=c, width=5)      # 刃 1
    d.line([(18, 53), (49, 22)], fill=c, width=5)      # 刃 2（平行）
    d.line([(36, 47), (47, 36)], fill=c, width=3)      # 箭头杆
    d.polygon([(50, 33), (52, 46), (39, 44)], fill=c)  # 箭头尖
    return img, c


def ico_hoe_lifesteal():
    """一滴下坠的血：圆 + 上方三角收尖。"""
    c = hx("#B0102E")
    img = base()
    d = ImageDraw.Draw(img)
    d.ellipse([23, 31, 41, 49], fill=c)                # 血珠
    d.polygon([(32, 9), (23, 38), (41, 38)], fill=c)   # 上方收尖
    return img, c


def ico_can_multijet():
    """三道竖线，粗 4px。"""
    c = hx("#4FD1C5")
    img = base()
    d = ImageDraw.Draw(img)
    for x in (18, 32, 46):
        d.line([(x, 13), (x, 51)], fill=c, width=4)
    return img, c


def ico_can_slow():
    """六角雪花：3 条穿心线 + 6 个短叉。"""
    c = hx("#A4E3DF")
    img = base()
    d = ImageDraw.Draw(img)
    cx = cy = 32
    r_arm, r_fork, fork_ang = 19.0, 7.0, math.radians(62)
    for k in range(3):
        a = math.radians(k * 60)
        dx, dy = math.cos(a), math.sin(a)
        d.line([(cx - r_arm * dx, cy - r_arm * dy),
                (cx + r_arm * dx, cy + r_arm * dy)], fill=c, width=4)
        for sgn in (1, -1):                            # 每个端点一个短叉
            ex, ey = cx + sgn * r_arm * dx, cy + sgn * r_arm * dy
            for fs in (1, -1):
                fx, fy = math.cos(a + fs * fork_ang), math.sin(a + fs * fork_ang)
                d.line([(ex, ey),
                        (ex + sgn * r_fork * fx, ey + sgn * r_fork * fy)],
                       fill=c, width=3)
    d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=c)  # 花心
    return img, c


def ico_can_thickjet():
    """一根粗箭头 + 两侧各一根细线。"""
    c = hx("#6FD3A8")
    img = base()
    d = ImageDraw.Draw(img)
    d.line([(32, 12), (32, 36)], fill=c, width=11)     # 粗箭杆
    d.polygon([(19, 33), (45, 33), (32, 53)], fill=c)  # 箭头
    d.line([(15, 16), (15, 44)], fill=c, width=3)      # 左细线
    d.line([(49, 16), (49, 44)], fill=c, width=3)      # 右细线
    return img, c


def ico_can_autocharge():
    """环形箭头：300° 环 + 一个三角箭头。"""
    c = hx("#7EE0B8")
    img = base()
    d = ImageDraw.Draw(img)
    d.arc([15, 15, 49, 49], start=0, end=300, fill=c, width=5)
    r = 17.0
    end_a = math.radians(300)
    ex, ey = 32 + r * math.cos(end_a), 32 + r * math.sin(end_a)
    tx, ty = math.cos(end_a + math.pi / 2), math.sin(end_a + math.pi / 2)  # 切向
    px, py = -ty, tx                                                      # 法向
    d.polygon([(ex + 9 * tx, ey + 9 * ty),
               (ex - 6 * px, ey - 6 * py),
               (ex + 6 * px, ey + 6 * py)], fill=c)
    return img, c


def ico_fert_subburst():
    """中心圆 + 六个小圆按正六边形角分布（距中心 20px，小圆半径 5）。"""
    c = hx("#F59E0B")
    img = base()
    d = ImageDraw.Draw(img)
    cx = cy = 32
    d.ellipse([cx - 8, cy - 8, cx + 8, cy + 8], fill=c)      # 中心大圆
    for deg in (0, 60, 120, 180, 240, 300):
        a = math.radians(deg)
        qx, qy = cx + 20 * math.cos(a), cy + 20 * math.sin(a)
        d.ellipse([qx - 5, qy - 5, qx + 5, qy + 5], fill=c)  # 六个小圆
    return img, c


def ico_fert_poison():
    """三点液滴，上大下小。"""
    c = hx("#7FC241")
    img = base()
    d = ImageDraw.Draw(img)
    droplet(d, 32, 16, 8, c)     # 上：大
    droplet(d, 32, 34, 6, c)     # 中
    droplet(d, 32, 49, 4, c)     # 下：小
    return img, c


def ico_fert_freeburst():
    """一个罐子（矩形身 + 梯形口）+ 一道 45° 斜线划掉。"""
    c = hx("#F2C14E")
    img = base()
    d = ImageDraw.Draw(img)
    d.rectangle([21, 26, 43, 51], outline=c, width=4)                 # 罐身
    mouth = [(25, 13), (39, 13), (44, 26), (20, 26)]                  # 梯形口
    for i in range(len(mouth)):
        d.line([mouth[i], mouth[(i + 1) % len(mouth)]], fill=c, width=4)
    d.line([(12, 51), (52, 11)], fill=c, width=4)                     # 45° 划掉
    return img, c


def ico_fert_chain():
    """两个相邻的 8 角爆炸星，一大一小。"""
    c = hx("#FF6B35")
    img = base()
    d = ImageDraw.Draw(img)
    d.polygon(star_poly(25, 29, 16.0, 6.5), fill=c)   # 大星
    d.polygon(star_poly(46, 43, 10.0, 4.0), fill=c)   # 小星
    return img, c


ICONS = [
    ("hoe_swordwave", ico_hoe_swordwave),
    ("hoe_burn", ico_hoe_burn),
    ("hoe_doublestrike", ico_hoe_doublestrike),
    ("hoe_lifesteal", ico_hoe_lifesteal),
    ("can_multijet", ico_can_multijet),
    ("can_slow", ico_can_slow),
    ("can_thickjet", ico_can_thickjet),
    ("can_autocharge", ico_can_autocharge),
    ("fert_subburst", ico_fert_subburst),
    ("fert_poison", ico_fert_poison),
    ("fert_freeburst", ico_fert_freeburst),
    ("fert_chain", ico_fert_chain),
]


# ---------------------------------------------------------------- 生成 + 自查

def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR)

    ok, bad, rows = 0, [], []

    for icon_id, fn in ICONS:
        img, color = fn()
        path = os.path.join(OUT_DIR, NAME_FMT % icon_id)
        img.save(path, "PNG", optimize=True)

        # 读回自查：验磁盘上的文件，不信内存对象
        chk = Image.open(path)
        chk.load()
        rgba = chk.convert("RGBA")
        w, h = chk.size
        mode = chk.mode
        px = rgba.load()
        corners = [px[0, 0][3], px[w - 1, 0][3], px[0, h - 1][3], px[w - 1, h - 1][3]]

        main_px = 0
        for y in range(h):
            for x in range(w):
                if rgba.getpixel((x, y)) == color:
                    main_px += 1

        nbytes = os.path.getsize(path)

        fails = []
        if (w, h) != (SIZE, SIZE):
            fails.append("size=%dx%d" % (w, h))
        if mode != "RGBA":
            fails.append("mode=%s" % mode)
        if any(a != 0 for a in corners):
            fails.append("corner-alpha=%s" % corners)
        if main_px <= 50:
            fails.append("main-color-px=%d" % main_px)

        if fails:
            bad.append("%s (%s)" % (icon_id, "; ".join(fails)))
        else:
            ok += 1

        rows.append(
            "%-17s %2dx%-2d %-4s corners_a=%-15s main_px=%-5d bytes=%-6d %s"
            % (icon_id, w, h, mode,
               "[" + ",".join(str(a) for a in corners) + "]",
               main_px, nbytes,
               "OK" if not fails else "FAIL")
        )

    print("out_dir = %s" % OUT_DIR)
    print("-" * 104)
    for r in rows:
        print(r)
    print("-" * 104)

    if bad:
        print("icons = %d / %d OK  FAILED: %s"
              % (ok, len(ICONS), "; ".join(bad)))
        return 1
    print("icons = %d / %d OK" % (ok, len(ICONS)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
