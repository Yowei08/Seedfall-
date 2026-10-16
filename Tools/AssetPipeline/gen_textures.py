# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 地表与墙体贴图生成

产出可**无缝平铺**的 2D 贴图，供地面与墙体使用。

★ 可平铺的关键：环绕绘制 ★
直接在一张画布上随机撒点，左右/上下边缘会接不上，铺开就是明显的网格缝。
这里的做法是：每画一个元素，除了它本身的位置，再在 ±W / ±H 的 8 个方向各画一份。
只画"刚好跨过边界"的那些会落在画布内，其余被裁掉——但边界处的内容就自然接上了。

★ 编辑约定 ★
本文件含中文，**不要用 PowerShell 的 Get-Content/Set-Content 做文本替换**：
Windows PowerShell 5.1 按 ANSI 读、按 UTF-8 写，中文会变成乱码。
这个坑在本项目已经踩过三次（着色器两次、本文件一次）。改动请用编辑器工具直接写。

用法（不需要 Blender，只用 Pillow）：
    python gen_textures.py
"""

import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Textures")

SIZE = 512        # 256→512：地面铺得很广，256 的图重复感很明显
SEED = 20261015
K = SIZE / 256.0  # 尺寸倍率：所有绝对半径都要跟着缩放，
                  # 否则"提了尺寸、斑块相对变小"，重复感反而更强


def wrap_circle(draw, x, y, r, fill, size=SIZE):
    """在 (x,y) 画一个圆，并在 8 个环绕方向各画一份，保证平铺无缝。"""
    for dx in (-size, 0, size):
        for dy in (-size, 0, size):
            draw.ellipse([x + dx - r, y + dy - r, x + dx + r, y + dy + r], fill=fill)


def wrap_poly(draw, pts, fill, size=SIZE):
    for dx in (-size, 0, size):
        for dy in (-size, 0, size):
            draw.polygon([(px + dx, py + dy) for px, py in pts], fill=fill)


def wrap_line(draw, x0, y0, x1, y1, fill, width=1, size=SIZE):
    for dx in (-size, 0, size):
        for dy in (-size, 0, size):
            draw.line([x0 + dx, y0 + dy, x1 + dx, y1 + dy], fill=fill, width=width)


def make_grass():
    """
    草地：三层合成，比例很关键。
      ① 底色
      ② 低频起伏——**单独一层画完重度模糊再叠**。直接在成品上画圆会留下硬边，
         铺开就是一块块迷彩色斑（第一版就是这么翻车的）。
      ③ 高频草叶——短线段，占画面的主导纹理
    """
    rng = random.Random(SEED)

    # ② 低频层
    #    两块改动一起做才有用：
    #      ① 尺寸 256→512（K=2），所有半径跟着乘 K
    #      ② 斑块数量 40→90、模糊半径跟着放大
    #    只提尺寸不提数量，等于把同一张花纹放大，重复感不会减轻。
    low = Image.new("L", (SIZE, SIZE), 128)
    ld = ImageDraw.Draw(low)
    for _ in range(90):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        r = rng.randint(int(30 * K), int(78 * K))
        v = rng.randint(108, 148)
        for dx in (-SIZE, 0, SIZE):
            for dy in (-SIZE, 0, SIZE):
                ld.ellipse([x + dx - r, y + dy - r, x + dx + r, y + dy + r], fill=v)
    low = low.filter(ImageFilter.GaussianBlur(20 * K))

    img = Image.new("RGB", (SIZE, SIZE), (78, 108, 64))
    px = img.load()
    lp = low.load()
    for y in range(SIZE):
        for x in range(SIZE):
            k = lp[x, y] / 128.0                 # 渐变的明暗，不是硬边
            r0, g0, b0 = px[x, y]
            px[x, y] = (min(255, int(r0 * k)), min(255, int(g0 * k)), min(255, int(b0 * k)))

    d = ImageDraw.Draw(img)

    # ③ 高频草叶：数量与长度都按面积倍率走，512 上才不会显得稀疏
    blades = int(2200 * K * K)
    for _ in range(blades):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        ln = rng.uniform(2 * K, 5 * K)
        ang = rng.uniform(-0.35, 0.35) - math.pi / 2
        k = rng.uniform(0.82, 1.20)
        col = tuple(max(0, min(255, int(v * k))) for v in (104, 140, 78))
        wrap_line(d, x, y, x + math.cos(ang) * ln, y + math.sin(ang) * ln, col, 1)

    # ④ 少量枯草：细、短、低饱和（第一版画成了红果子，是饱和度给太高）
    for _ in range(int(40 * K * K)):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        ln = rng.uniform(2 * K, 4 * K)
        ang = rng.uniform(-0.4, 0.4) - math.pi / 2
        wrap_line(d, x, y, x + math.cos(ang) * ln, y + math.sin(ang) * ln, (108, 104, 74), 1)

    return img


def make_wall_stone():
    """
    石墙：错缝砌块 + 灰浆缝 + 每块砖的明暗差异。
    砖块按行错位——正缝会立刻暴露平铺周期。
    """
    rng = random.Random(SEED + 7)
    img = Image.new("RGB", (SIZE, SIZE), (58, 56, 52))
    d = ImageDraw.Draw(img)

    rows = int(6 * K)
    bh = SIZE / rows
    for r in range(rows):
        y0 = r * bh
        offset = (r % 2) * (SIZE / 8.0)      # 错缝
        cols = int(4 * K)
        bw = SIZE / cols
        for c in range(cols + 1):
            x0 = c * bw - offset
            k = rng.uniform(0.78, 1.16)
            col = tuple(max(0, min(255, int(v * k))) for v in (122, 118, 108))
            pad = 2.0 * K
            pts = [(x0 + pad, y0 + pad), (x0 + bw - pad, y0 + pad),
                   (x0 + bw - pad, y0 + bh - pad), (x0 + pad, y0 + bh - pad)]
            wrap_poly(d, pts, col)
            # 砖块上沿的高光，制造体积感
            wrap_poly(d, [(x0 + pad, y0 + pad), (x0 + bw - pad, y0 + pad),
                          (x0 + bw - pad, y0 + pad + 2 * K), (x0 + pad, y0 + pad + 2 * K)],
                      tuple(min(255, int(v * 1.18)) for v in col))

    # 噪点：让石面不那么平
    px = img.load()
    for _ in range(SIZE * SIZE // 12):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        v = rng.randint(-14, 14)
        r0, g0, b0 = px[x, y]
        px[x, y] = (max(0, min(255, r0 + v)), max(0, min(255, g0 + v)), max(0, min(255, b0 + v)))

    return img.filter(ImageFilter.SMOOTH)


def make_soil():
    """耕地的松土：深褐底 + 颗粒 + 犁沟。"""
    rng = random.Random(SEED + 13)
    img = Image.new("RGB", (SIZE, SIZE), (92, 66, 45))
    d = ImageDraw.Draw(img)
    for _ in range(int(500 * K * K)):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        k = rng.uniform(0.8, 1.2)
        wrap_circle(d, x, y, rng.randint(int(2 * K), int(6 * K)),
                    tuple(int(v * k) for v in (92, 66, 45)))
    # 犁沟：水平方向的暗带
    furrows = int(6 * K)
    for i in range(furrows):
        y = int((i + 0.5) * SIZE / furrows)
        for dx in (-SIZE, 0, SIZE):
            d.line([dx, y, dx + SIZE, y], fill=(70, 48, 32), width=int(3 * K))
    px = img.load()
    for _ in range(SIZE * SIZE // 8):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        v = rng.randint(-18, 18)
        r0, g0, b0 = px[x, y]
        px[x, y] = (max(0, min(255, r0 + v)), max(0, min(255, g0 + v)), max(0, min(255, b0 + v)))
    return img.filter(ImageFilter.SMOOTH)


TEXTURES = {
    "grass": make_grass,
    "wall_stone": make_wall_stone,
    "soil": make_soil,
}


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, fn in TEXTURES.items():
        img = fn()
        path = os.path.join(OUT_DIR, name + ".png")
        img.save(path)
        print(f"[SEEDFALL-TEX] {name} {img.size[0]}x{img.size[1]} -> {path}")
    print(f"[SEEDFALL-TEX] {len(TEXTURES)} 张，输出目录 {OUT_DIR}")


if __name__ == "__main__":
    main()
