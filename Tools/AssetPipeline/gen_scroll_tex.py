# -*- coding: utf-8 -*-
"""
生成 UI 的"卷轴纸纹"贴图（v96，需求：为棕色框增加纹理模仿卷轴效果）。

关键设计：**输出灰度**，不是彩色。
  游戏侧是 `GUI.color = 棕色; GUI.DrawTexture(rect, tex);` —— color 与贴图**相乘**。
  所以贴图只需提供**明暗变化**（均值接近白），棕色调上去自然就是"有纸纹的棕"。
  如果这里直接画成棕色纸，乘完会变成脏黑。

另外必须**无缝**：它会被平铺在大小不一的框里（商品行、收纳格、菜单卡），
接缝一旦可见，"卷轴"就变成了"贴了几张纸"。
做法是**绘制时越界回绕**（wrap drawing）——和 gen_textures.py 同一套办法。

用法：
    python gen_scroll_tex.py
"""

import os
import random
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256
OUT = os.path.join(os.path.dirname(__file__), "..", "..",
                   "Assets", "Resources", "Art", "UI", "ui_scroll_paper.png")
OUT = os.path.abspath(OUT)

random.seed(20261015)   # 固定种子：贴图可复现


def wrap_line(d, x0, y0, x1, y1, fill, width=1):
    """跨边界回绕地画线，保证平铺无缝。"""
    for dx in (-SIZE, 0, SIZE):
        for dy in (-SIZE, 0, SIZE):
            d.line([(x0 + dx, y0 + dy), (x1 + dx, y1 + dy)], fill=fill, width=width)


def wrap_ellipse(d, x0, y0, x1, y1, fill):
    for dx in (-SIZE, 0, SIZE):
        for dy in (-SIZE, 0, SIZE):
            d.ellipse([x0 + dx, y0 + dy, x1 + dx, y1 + dy], fill=fill)


def main():
    # 底：接近白，但压到 0.93 —— 留出提亮与压暗的余地
    base = 238
    img = Image.new("L", (SIZE, SIZE), base)
    d = ImageDraw.Draw(img)

    # ① 纸纤维：细长的横竖交织短线，明暗交替。
    #    这是"纸"与"塑料"最大的区别——塑料是纯色，纸有方向性的纤维。
    for _ in range(700):
        x = random.uniform(0, SIZE)
        y = random.uniform(0, SIZE)
        if random.random() < 0.72:          # 横纹为主（卷轴的纤维是横向的）
            ln = random.uniform(10, 54)
            v = base + random.choice([-16, -11, -7, 7, 11, 16])
            wrap_line(d, x, y, x + ln, y + random.uniform(-1.2, 1.2), int(v), 1)
        else:
            ln = random.uniform(6, 22)
            v = base + random.choice([-9, -5, 5, 9])
            wrap_line(d, x, y, x + random.uniform(-1, 1), y + ln, int(v), 1)

    # ② 大块柔和明暗：让纸面有"被卷过、受压不均"的起伏。
    #    只画少量大斑点再模糊，避免变成噪点。
    blob = Image.new("L", (SIZE, SIZE), 128)
    bd = ImageDraw.Draw(blob)
    for _ in range(26):
        x = random.uniform(0, SIZE)
        y = random.uniform(0, SIZE)
        r = random.uniform(18, 52)
        v = 128 + random.choice([-26, -16, 16, 26])
        wrap_ellipse(bd, x - r, y - r * 0.6, x + r, y + r * 0.6, int(v))
    blob = blob.filter(ImageFilter.GaussianBlur(14))
    # 把起伏叠加到底上，幅度压小（±10 以内），否则会盖过框本身的颜色
    px = img.load()
    bp = blob.load()
    for y in range(SIZE):
        for x in range(SIZE):
            v = px[x, y] + int((bp[x, y] - 128) * 0.42)
            px[x, y] = max(196, min(255, v))

    # ③ 极轻的细颗粒：去掉"模糊块"的塑料感
    for _ in range(2600):
        x = random.randrange(SIZE)
        y = random.randrange(SIZE)
        px[x, y] = max(196, min(255, px[x, y] + random.choice([-5, -3, 3, 5])))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.save(OUT)

    lo, hi = img.getextrema()
    avg = sum(img.getdata()) / (SIZE * SIZE)
    print("已写出 %s" % OUT)
    print("  尺寸 %dx%d  灰度范围 %d~%d  均值 %.1f（越接近白，着色越准）"
          % (SIZE, SIZE, lo, hi, avg))


if __name__ == "__main__":
    main()
