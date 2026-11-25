# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 卡通贴图材质生成（v214 美术升级 · 交付项 ②）

产出一组 **64×64、可无缝平铺、带深色描边缝** 的卡通贴图，给 `Seedfall/Toon`
系列的 `_MainTex` 用。目标不是"写实材质"，而是漫画里的**网点/墨线**观感：

    木纹  toon_wood    木板 + 深色板缝 + 木节
    石纹  toon_stone   错缝砌块 + 深色灰浆
    瓦片  toon_roof    瓦垄 + 深色瓦缝
    布纹  toon_cloth   经纬交叉 + 深色织线
    草色  toon_grass   竖向草叶 + 深色根部

──────────────────────────────────────────────────────────────────────
为什么是 64×64
──────────────────────────────────────────────────────────────────────
贴图在 shader 里是**乘到 `_BaseColor` 上的明暗调制**，不是解耦的 albedo：
一张 64 的图铺 2 米，1 像素 ≈ 3 厘米，在正交主城机位（半高 9.5m / 1080p
⇒ 1 像素 ≈ 1.8 厘米）下每 texel 约 2 像素 —— 刚好能读出纹理又不产生摩尔纹。
512 的图在这个尺寸下是浪费显存（5 张 512 = 5MB，5 张 64 = 80KB），
而且会把"卡通平涂"拉成"写实噪点"，与描边风格打架。

──────────────────────────────────────────────────────────────────────
为什么每条缝都要**深到近黑**
──────────────────────────────────────────────────────────────────────
用户本轮的原话是"加入黑色描边"。反向外壳描边只能勾**物体外轮廓**，
物体内部的板缝/瓦缝勾不出来；这一层深色缝就是"内部的墨线"，
它在 draw call 上是零成本（同一次采样），观感上把"一整块棕色的箱子"
读成"几块拼起来的木板"。

──────────────────────────────────────────────────────────────────────
可平铺：环绕绘制
──────────────────────────────────────────────────────────────────────
与 `Tools/AssetPipeline/gen_textures.py` 同一手法：每个元素在 ±W / ±H
的 8 个方向各画一份，落在画布外的被自然裁掉，边界内容因此接得上。
随机数用**固定种子**，同一台机器上重复运行必须逐字节相同。

用法：
    python gen_toon_textures.py                # 生成全部
    python gen_toon_textures.py --only wood    # 只生成一张
    python gen_toon_textures.py --out <目录>    # 换输出目录（默认工程内的 Textures）
    python gen_toon_textures.py --help
"""

import argparse
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_OUT = os.path.join(PROJECT, "Assets", "Resources", "Art", "Textures")

SIZE = 64            # ★ 交付要求：64×64
SEED = 20261019      # 固定种子 ⇒ 可复现

# 墨线色：比反向外壳描边的 (0.05,0.05,0.05) 略亮一点。
# 原因：贴图是**乘算**调制，纯黑会把接触面压成死黑，反而丢掉体积；
# 0.10 在亮部仍读作"一条黑缝"，在暗部（_ShadeColor 已压到 0.55）还能看出结构。
INK = (26, 24, 22)


def _wrap(fn, x, y, *args, **kwargs):
    """把一次绘制复制到 ±SIZE 的 8 个方向，保证平铺无缝。"""
    for dx in (-SIZE, 0, SIZE):
        for dy in (-SIZE, 0, SIZE):
            fn(x + dx, y + dy, *args, **kwargs)


def _mul(color, k):
    return tuple(max(0, min(255, int(v * k))) for v in color)


def _blend(a, b, t):
    return tuple(int(a[i] * (1.0 - t) + b[i] * t) for i in range(3))


# ─────────────────────────────────────────────────────────────── 木纹
def make_wood():
    """
    木板：横向板条 + 深色板缝 + 木节（同心椭圆）+ 细纵向木理。

    板条只有 3 条（64 / 3 ≈ 21 像素 ≈ 每块板 66cm）—— 太多会碎成噪点，
    太少铺在 5 米的屋顶上会看出重复。3~4 条是"一眼是木板"和"不显周期"的交点。
    """
    rng = random.Random(SEED + 1)
    base = (150, 108, 68)
    img = Image.new("RGB", (SIZE, SIZE), base)
    d = ImageDraw.Draw(img)

    planks = 3
    ph = SIZE // planks
    for p in range(planks):
        y0 = p * ph
        # 每条板的底色各自有明暗差 —— 同一块木头不同板子不会完全一样
        k = rng.uniform(0.90, 1.10)
        col = _mul(base, k)
        d.rectangle([0, y0, SIZE, y0 + ph], fill=col)
        # 上沿高光 / 下沿板缝（深到近黑）
        d.line([0, y0 + 1, SIZE, y0 + 1], fill=_mul(col, 1.16), width=1)
        d.line([0, y0 + ph - 1, SIZE, y0 + ph - 1], fill=INK, width=2)
        # 板内纵向木理：短而细的竖线，不跨板
        for _ in range(5):
            x = rng.randrange(SIZE)
            ln = rng.randint(ph // 3, ph - 4)
            yy = y0 + rng.randint(2, max(3, ph - ln - 2))
            d.line([x, yy, x + rng.randint(-1, 1), yy + ln], fill=_mul(col, 0.86), width=1)

    # 木节：两个，同心环
    for (nx, ny, nr) in ((rng.randrange(8, SIZE - 8), rng.randrange(6, SIZE - 6), 3),
                         (rng.randrange(8, SIZE - 8), rng.randrange(6, SIZE - 6), 2)):
        _wrap(lambda x, y, r: d.ellipse([x - r, y - r, x + r, y + r], outline=INK), nx, ny, nr)
        _wrap(lambda x, y, r: d.ellipse([x - r // 2, y - r // 2, x + r // 2, y + r // 2],
                                       fill=_mul(base, 0.72)), nx, ny, nr)

    return img


# ─────────────────────────────────────────────────────────────── 石纹
def make_stone():
    """
    石纹：2 行错缝砌块 + **粗灰浆** + 每块砖独立的明暗 + 上沿高光 + 低频斑块。

    ★ v214 第二轮重做（用户第二次说"场景还是存在贴图问题"）★
    第一版的毛病：块与块之间只差 ±14%，灰浆只有 1 像素。
    在 26 米正交机位下（约 37 像素/米）一条 1 像素灰浆在 64 像素的图上只占
    屏幕的 ~0.6 像素 ⇒ **被 mipmap 直接平均掉** ⇒ 整面读成纯米色 ✗
    现在：块间明暗 **±30%**、灰浆 **2~3 像素且更黑**、再叠一层**低频斑块**
    （低频是唯一能在缩小时存活的信息）⇒ 远看是"一块块石头"，近看是石头 ✓
    """
    rng = random.Random(SEED + 2)
    mortar = (52, 50, 46)          # 灰浆：更黑（原来是 74,72,68）
    quilt = (176, 172, 162)
    img = Image.new("RGB", (SIZE, SIZE), mortar)
    d = ImageDraw.Draw(img)

    # ③ 低频斑块：先铺一层随位置缓慢变化的明暗（缩小时唯一能活下来的信息）
    for _ in range(18):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        r = rng.randint(10, 22)
        k = rng.uniform(0.86, 1.14)
        _wrap(lambda cx, cy, rr, cc: d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=cc),
              x, y, r, _mul(quilt, k))

    rows = 2
    bh = SIZE // rows
    cols = 2
    bw = SIZE // cols
    for r in range(rows):
        y0 = r * bh
        off = (bw // 2) if (r % 2) else 0
        for c in range(cols + 1):
            x0 = c * bw - off
            k = rng.uniform(0.70, 1.30)          # ±30%（原来 ±14%）
            col = _mul(quilt, k)
            pad = 2                                # 灰浆 2 像素（原来 1）
            pts = [(x0 + pad, y0 + pad), (x0 + bw - pad - 1, y0 + pad),
                   (x0 + bw - pad - 1, y0 + bh - pad - 1), (x0 + pad, y0 + bh - pad - 1)]
            for dx in (-SIZE, 0, SIZE):
                for dy in (-SIZE, 0, SIZE):
                    d.polygon([(px + dx, py + dy) for px, py in pts], fill=col)
            # 顶面高光：一眼读出"有厚度"
            for dx in (-SIZE, 0, SIZE):
                d.line([x0 + pad + dx, y0 + pad, x0 + bw - pad - 1 + dx, y0 + pad],
                       fill=_mul(col, 1.22), width=2)
                # 底面阴影：与高光配合才有"砌块凸起"的读数
                d.line([x0 + pad + dx, y0 + bh - pad - 2, x0 + bw - pad - 1 + dx, y0 + bh - pad - 2],
                       fill=_mul(col, 0.66), width=2)
            # 每块砖内部的一处凹坑
            if rng.random() < 0.8:
                hx = x0 + rng.randint(4, max(5, bw - 6))
                hy = y0 + rng.randint(4, max(5, bh - 6))
                _wrap(lambda x, y, cc: d.ellipse([x - 3, y - 2, x + 3, y + 2], fill=cc),
                      hx, hy, _mul(col, 0.80))

    return img.filter(ImageFilter.SMOOTH)


# ─────────────────────────────────────────────────────────────── 屋顶瓦
def make_roof():
    """
    屋顶瓦：4 行鱼鳞瓦 + 深色瓦缝 + 每片瓦的半圆上沿高光。

    瓦是**唯一的"竖向重复"贴图**：铺在屋顶上时 `_MainTex_ST` 的 y 会给得更大
    （2~3），所以单张里只需要 4 行就够密。
    """
    rng = random.Random(SEED + 3)
    base = (176, 82, 68)          # 陶土红
    img = Image.new("RGB", (SIZE, SIZE), _mul(base, 0.55))
    d = ImageDraw.Draw(img)

    rows = 4
    th = SIZE / rows
    tw = SIZE / 4.0
    for r in range(rows):
        y0 = r * th
        off = (tw * 0.5) if (r % 2) else 0.0
        for c in range(6):
            x0 = c * tw - off
            k = rng.uniform(0.88, 1.12)
            col = _mul(base, k)
            # 瓦片本体：下沿是半圆（鱼鳞感）
            for dx in (-SIZE, 0, SIZE):
                for dy in (-SIZE, 0, SIZE):
                    d.rectangle([x0 + 1 + dx, y0 + dy, x0 + tw - 1 + dx, y0 + th * 0.72 + dy], fill=col)
                    d.ellipse([x0 + 1 + dx, y0 + th * 0.30 + dy, x0 + tw - 1 + dx, y0 + th * 0.98 + dy],
                              fill=col)
                # 瓦缝（近黑）：瓦与瓦之间的竖线
                d.line([x0 + dx, y0, x0 + dx, y0 + th], fill=INK, width=1)
                # 每片瓦的上沿高光
                d.line([x0 + 2 + dx, y0 + 1, x0 + tw - 2 + dx, y0 + 1],
                       fill=_mul(col, 1.18), width=1)
        # 行缝
        d.line([0, y0 + th - 1, SIZE, y0 + th - 1], fill=INK, width=1)

    return img


# ─────────────────────────────────────────────────────────────── 布纹
def make_cloth():
    """
    布：经纬交叉的细密格 + 深色织线。

    布是最需要"贴图"的一类：纯色的帆布/旗帜/口袋在卡通里就是一块死色块，
    加一层织纹立刻读得出"这是布料"。对比度刻意做得比木/石低 ——
    布是背景件，纹理抢眼就喧宾夺主了。
    """
    rng = random.Random(SEED + 4)
    base = (196, 178, 150)
    img = Image.new("RGB", (SIZE, SIZE), base)
    d = ImageDraw.Draw(img)

    step = 4
    for i in range(0, SIZE, step):
        even = (i // step) % 2 == 0
        # 归一到 0.92 之后，原来的 0.90/0.96 压成了"看不出来"——
        # 乘算贴图的对比度会在归一化时被**同比压缩**，所以这里要按最终亮度重新给深线。
        d.line([i, 0, i, SIZE], fill=_mul(base, 0.74 if even else 0.88), width=1)
        d.line([0, i, SIZE, i], fill=_mul(base, 0.80 if even else 0.68), width=1)

    # 交叉点：每隔一格压一个深点，织物的"结节"
    for y in range(0, SIZE, step * 2):
        for x in range(0, SIZE, step * 2):
            d.point((x, y), fill=_mul(base, 0.60))

    # 几处磨损/污渍：低频，避免整片均匀
    for _ in range(6):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        r = rng.randint(4, 9)
        _wrap(lambda cx, cy, rr: d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr],
                                          fill=_mul(base, 0.94)), x, y, r)

    return img


# ─────────────────────────────────────────────────────────────── 草地
def make_grass():
    """
    草地：竖向草叶丛 + 深色根部。

    与 `Art/Textures/grass.png`（512，战斗场地的写实地表）**不是同一件事**：
    那张是给 URP/Lit 的地表，这张是给卡通草/公园草地的**平涂 + 草叶线**。
    它同时是"卡通草"这一项的兜底：即便程序化草片因为性能被关掉，
    草地本身也带着草叶纹理，不是一块纯绿。
    """
    rng = random.Random(SEED + 5)
    base = (108, 158, 84)
    img = Image.new("RGB", (SIZE, SIZE), base)
    d = ImageDraw.Draw(img)

    # 低频起伏：先画一层柔和的暗块（不模糊也能接受，因为草叶会盖住硬边）
    for _ in range(14):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        r = rng.randint(6, 14)
        _wrap(lambda cx, cy, rr: d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr],
                                          fill=_mul(base, rng.uniform(0.92, 1.06))), x, y, r)

    # 草叶：短竖线，深色根 → 亮色梢
    for _ in range(260):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        ln = rng.randint(3, 7)
        lean = rng.randint(-2, 2)
        col = _mul(base, rng.uniform(0.80, 1.22))
        _wrap(lambda cx, cy, cl, ein, l: d.line(
            [cx, cy, cx + ein, cy - l], fill=cl, width=1), x, y, col, lean, ln)

    # 深色根部：稀疏几条更长的暗线，把草"种进"地里
    for _ in range(40):
        x, y = rng.randrange(SIZE), rng.randrange(SIZE)
        _wrap(lambda cx, cy: d.line([cx, cy, cx, cy - 5], fill=_mul(base, 0.62), width=1), x, y)

    return img


def normalize_multiplier(img, target=0.92):
    """
    把整张图按"**亮度乘数**"归一化到 mean ≈ target，并允许高光被夹在 1.0。

    ── 为什么必须归一化 ──
    这批图在着色器里是**乘算**进 `_BaseColor` 的（albedo *= tex）。
    如果不归一化，各张图的平均亮度会落在 0.37~0.67 之间（生成时实测），
    等于"挂上贴图的木器整体暗 40%、布器暗 33%" —— 同一种颜色挂不同贴图
    会得到不同亮度，材质池的语义就崩了 ✗

    ── 为什么 target 取 0.92 而不是 1.0 ──
    留 8% 的平均压暗，读起来像"这块材料比纯色稍微实一点"；
    取 1.0 会把大部分像素顶到 255 被夹掉 ⇒ **深色缝也跟着变浅**，墨线就没了 ✓
    夹在 1.0 的只有最亮的少数像素（板面高光、砖面上沿），
    而深色缝（墨线）压到 0.2~0.3 依然是一条能数出来的黑线 ✓
    """
    px = list(img.convert("RGB").getdata())
    mean = sum(sum(p) / 3.0 for p in px) / (255.0 * len(px))
    if mean <= 1e-6:
        return img, mean
    k = target / mean
    out = Image.new("RGB", img.size)
    out.putdata([tuple(max(0, min(255, int(v * k))) for v in p) for p in px])
    return out, mean


TEXTURES = {
    "wood": ("toon_wood", make_wood),
    "stone": ("toon_stone", make_stone),
    "roof": ("toon_roof", make_roof),
    "cloth": ("toon_cloth", make_cloth),
    "grass": ("toon_grass", make_grass),
}


def main():
    global SIZE
    ap = argparse.ArgumentParser(
        description="Seedfall 卡通贴图生成（64×64 无缝平铺，带深色墨线缝）")
    ap.add_argument("--only", default=None,
                    help="只生成一张：wood / stone / roof / cloth / grass")
    ap.add_argument("--out", default=DEFAULT_OUT, help="输出目录（默认工程内 Art/Textures）")
    ap.add_argument("--size", type=int, default=SIZE, help=f"边长，默认 {SIZE}")
    args = ap.parse_args()

    SIZE = args.size

    keys = list(TEXTURES)
    if args.only:
        if args.only not in TEXTURES:
            ap.error(f"未知的贴图 '{args.only}'，可选：{', '.join(keys)}")
        keys = [args.only]

    os.makedirs(args.out, exist_ok=True)
    for key in keys:
        name, fn = TEXTURES[key]
        img, raw_mean = normalize_multiplier(fn())
        path = os.path.join(args.out, name + ".png")
        img.save(path, optimize=True)
        # ★ 归一化前后的平均亮度都打出来 ★
        # 归一化的目标值（0.92）是写死的：这批图是**乘算**到 `_BaseColor` 上的，
        # 只有五张图的平均亮度一致，"挂不同贴图的同色物体"才会是同一个亮度。
        # 改了画法之后重跑本脚本，只要这五行 mean_after 都在 0.92 附近就没问题 ✓
        px = list(img.convert("L").getdata())
        mean_after = sum(px) / (255.0 * len(px))
        print(f"[SEEDFALL-TOONTEX] {name:12s} {img.size[0]}x{img.size[1]} "
              f"mean_raw={raw_mean:.4f} mean_after={mean_after:.4f} -> {path}")

    print(f"[SEEDFALL-TOONTEX] 共 {len(keys)} 张，输出目录 {args.out}，种子 {SEED}")


if __name__ == "__main__":
    main()
