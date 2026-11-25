#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
v202 · 8 株新植物的 **64×64 硬边像素图标**生成器
================================================================================

需求原话：「设计 8 个新高品质植物（带机制），包括模型和图标」。

## 这个脚本为什么存在（而不是去改 `gen_item_png.py`）
`gen_item_png.py` 是 v198 定下来的唯一出图器，它画的是 **62 件既有图标**
（46 件道具 + 16 张 v197 新图）。本轮只加 8 株植物，把 8 个新画法塞进那个文件
会让它的"62 张"口径与自检的 `ITEM-ICON-PNG`（62 张）分家 ✗
所以这里**不复制画法、只 import 它**：

    import gen_item_png as G          # 同一份调色板、同一组原语、同一套收尾
    G.ICONS["dusk_berry"] = i_dusk_berry   # 只追加 8 个画法，不改它的任何一行

⇒ 8 张图与既有 62 张**逐像素同一套风格**：同一 64×64 画布、同一 6 色调色板
（描边 `OUT_C` / 高光 `HI` / 主体 `B1 B2 B3` / 品类强调 `ACC_PLANT`）、
同一"先居中 → 再高光 → 再 2px 描边 → 再装饰"的收尾顺序、alpha 只有 0 与 255。

## 落盘（与既有 6 株机制植物**同一套命名**）
    Assets/Resources/Art/UI/icon_plant_<id>.png      ← 耕地格 / 种子卡 / 商店种子行读它
（既有的 6 株还多一张 `icon_item_<id>.png`，那是因为 `SelfCheck.ITEM-ICON-PNG`
  那份 62 件清单里逐件点名要 `icon_item_*`；本批 8 株不在那份清单里，
  运行时也只走 `icon_plant_*` ⇒ 只出这一张，不留没人读的副本 ✓）

## 用法（可重跑、可复现）
    python Tools/icon_gen/gen_plant_png_8.py                 # 打印本说明
    python Tools/icon_gen/gen_plant_png_8.py --write         # 生成 8 张 PNG 落盘
    python Tools/icon_gen/gen_plant_png_8.py --check         # 只校验，不写文件
    python Tools/icon_gen/gen_plant_png_8.py --write --sheet # 同时出对照图

    --sheet → Tools/icon_gen/out/plant_png_8_sheet.png（8 格，带 id 文字）
    --sheet → Tools/icon_gen/out/plant_png_8_sheet.txt（同一份清单的文字版）

## 校验口径（硬约束逐条机器判，不靠肉眼）
① 恰好 64×64；② alpha 只有 {0, 255}（硬边，不许半透明）；
③ 每张用色 ≤ 6 种且全部落在上面那份调色板里（含 `ACC_PLANT`）；
④ 8 张两两不同（像素哈希）；⑤ 与既有 62 张的像素哈希**不撞**；
⑥ 每张的非透明包围盒边长 ≥ 40px（"画得太小"也是一种失败：它在 64×64 的行里读不出来）。
"""

import hashlib
import io
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
UI_DIR = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
SEEDS = os.path.join(ROOT, "Assets", "Resources", "Data", "seeds.json")
OUT = os.path.join(HERE, "out")

sys.path.insert(0, HERE)
import gen_item_png as G          # noqa: E402  ← 唯一的出图实现，不复制

# 直接用它的调色板与原语（名字保持与 G 一致，读代码时不必来回对照）
Cv, rect, poly = G.Cv, G.rect, G.poly
disc, ring, ellipse = G.disc, G.ring, G.ellipse
line, taper, bezier = G.line, G.taper, G.bezier
spiral, drop = G.spiral, G.drop
B1, B2, B3, HI, OUT_C = G.B1, G.B2, G.B3, G.HI, G.OUT_C
AC = G.ACC_PLANT                  # 植物统一用"麦穗金"作强调色
W = H = 64

# 装饰分配：有"动作/速度"含义的给 `///`，静止的用右下角角标（与既有 62 张同一规则）
SLASH_IDS = {"quick_reed", "star_ladder", "harvest_bell"}


# ════════════════════════════════════════════════════════════════════════════
#  8 个新画法（每个函数只画"不透明主体 + 强调点"，描边/高光/装饰由 G.build 收尾）
# ════════════════════════════════════════════════════════════════════════════
def i_dusk_berry(cv, ac):
    """暮莓：三颗浆果一簇 + 果柄与叶 + 一颗将落的水珠（DewDrop 的可读化）。"""
    disc(cv, 24, 30, 13, B1)              # 上果
    disc(cv, 43, 34, 12, B2)              # 右果
    disc(cv, 32, 47, 11, B1)              # 下果
    rect(cv, 30, 6, 34, 22, B3)           # 果柄
    poly(cv, [(34, 12), (52, 8), (46, 20), (34, 18)], B3)     # 叶
    drop(cv, 52, 50, 6, ac)               # 将落的水珠
    disc(cv, 24, 30, 4, B3)               # 果脐
    disc(cv, 43, 34, 4, B3)


def i_tide_gourd(cv, ac):
    """潮瓜：横置葫芦（大小两球）+ 两道竖棱 + 顶部卷须 + 一滴水（WaterSave）。"""
    disc(cv, 24, 36, 15, B1)              # 大肚
    disc(cv, 46, 30, 11, B2)              # 小肚
    rect(cv, 33, 18, 37, 30, B3)          # 蒂
    spiral(cv, 40, 14, 2, 8, B3, 2, 1.2)  # 卷须
    rect(cv, 18, 24, 21, 48, B3)          # 竖棱 1
    rect(cv, 28, 22, 31, 50, B3)          # 竖棱 2
    drop(cv, 52, 48, 6, ac)               # 水珠


def i_hollow_vine(cv, ac):
    """空藤：一段中空的藤环（真的挖空）+ 一截断口 + 焦渴开出的花（ThirstBloom）。"""
    ring(cv, 30, 38, 20, 11, B1)          # 中空藤环（内部透明 = "空"）
    rect(cv, 26, 58, 34, 62, B3)          # 断口
    taper(cv, 30, 18, 30, 4, 7, 3, B2)    # 向上抽出的花梗
    disc(cv, 30, 8, 6, ac)                # 花心
    for k in range(4):                    # 四瓣
        a = math.pi * 2 * k / 4 + 0.4
        disc(cv, 30 + math.cos(a) * 9, 8 + math.sin(a) * 9, 5, B2)
    disc(cv, 52, 24, 5, B3)               # 侧芽


def i_dune_cactus(cv, ac):
    """沙掌：仙人掌掌片（两节）+ 侧掌 + 刺 + 顶花（ThirstTolerant：越旱越撑得住）。"""
    rect(cv, 22, 20, 42, 58, B1)          # 主掌（方肩 = 掌片）
    rect(cv, 14, 30, 22, 46, B2)          # 左掌
    rect(cv, 42, 26, 50, 42, B2)          # 右掌
    for y in range(26, 58, 8):            # 刺（横排短点）
        for x in (26, 34, 40):
            rect(cv, x, y, x + 1, y + 1, B3)
    rect(cv, 18, 46, 22, 50, B3)          # 掌趾
    rect(cv, 42, 38, 46, 42, B3)
    disc(cv, 32, 14, 7, ac)               # 顶花
    disc(cv, 32, 14, 3, B1)


def i_harvest_bell(cv, ac):
    """丰收铃：钟形铃身 + 铃舌 + 顶环 + 三枚麦粒（RichHarvest）。"""
    poly(cv, [(22, 14), (42, 14), (50, 46), (14, 46)], B1)    # 铃身（下宽）
    rect(cv, 12, 44, 52, 50, B2)          # 铃口
    disc(cv, 32, 56, 6, B3)               # 铃舌
    ring(cv, 32, 9, 6, 3, B3)             # 顶环
    rect(cv, 20, 20, 44, 22, B3)          # 铃身横箍
    for (x, y) in [(24, 62), (32, 62), (40, 62)]:
        rect(cv, x - 2, y - 2, x + 2, y + 2, ac)              # 三枚麦粒


def i_star_ladder(cv, ac):
    """星梯：两轨四横档 + 三颗星（ChainHarvest：一格接一格往上收）。"""
    rect(cv, 16, 8, 21, 56, B2)           # 左轨
    rect(cv, 43, 8, 48, 56, B2)           # 右轨
    for y in (20, 32, 44):                # 横档
        rect(cv, 21, y, 43, y + 4, B1)
    for (x, y) in [(32, 12), (14, 30), (50, 34)]:             # 星星
        poly(cv, [(x, y - 6), (x + 2, y - 1), (x + 7, y - 1), (x + 3, y + 2),
                  (x + 5, y + 7), (x, y + 4), (x - 5, y + 7), (x - 3, y + 2),
                  (x - 7, y - 1), (x - 2, y - 1)], ac)
    disc(cv, 32, 30, 5, ac)               # 中央星核


def i_elder_oak(cv, ac):
    """古木：粗干 + 三层冠层 + 外露树根 + 年轮点（ElderGrowth：越老越快）。"""
    taper(cv, 32, 16, 32, 60, 14, 8, B3)  # 树干（上细下粗）
    for (cx, cy, r, c) in [(32, 16, 15, B1), (20, 26, 10, B2), (44, 26, 10, B2)]:
        disc(cv, cx, cy, r, c)            # 冠层三团
    for (x0, y0, x1, y1) in [(20, 58, 8, 62), (44, 58, 56, 62), (26, 60, 22, 63),
                             (38, 60, 42, 63)]:
        taper(cv, x0, y0, x1, y1, 5, 3, B3)                   # 树根
    rect(cv, 29, 34, 35, 36, ac)          # 年轮记号
    rect(cv, 29, 44, 35, 46, ac)


def i_quick_reed(cv, ac):
    """疾苇：三根苇秆 + 穗 + 三道风（Haste：长得最快的那一档）。"""
    taper(cv, 20, 60, 24, 12, 8, 3, B1)   # 苇 1
    taper(cv, 34, 60, 32, 6, 8, 3, B2)    # 苇 2（最高）
    taper(cv, 48, 60, 46, 18, 8, 3, B1)   # 苇 3
    rect(cv, 30, 4, 34, 20, B3)           # 穗
    for k in range(3):                    # 风线（越上越长）
        y = 46 - k * 12
        rect(cv, 6 + k * 3, y, 18 + k * 4, y + 2, ac)
        rect(cv, 52 - k * 2, y + 5, 60 - k, y + 7, ac)


PLANT_ICONS = {
    "dusk_berry": i_dusk_berry,
    "tide_gourd": i_tide_gourd,
    "hollow_vine": i_hollow_vine,
    "dune_cactus": i_dune_cactus,
    "harvest_bell": i_harvest_bell,
    "star_ladder": i_star_ladder,
    "elder_oak": i_elder_oak,
    "quick_reed": i_quick_reed,
}

PLANT_NAMES = {
    "dusk_berry": "暮莓", "tide_gourd": "潮瓜", "hollow_vine": "空藤",
    "dune_cactus": "沙掌", "harvest_bell": "丰收铃", "star_ladder": "星梯",
    "elder_oak": "古木", "quick_reed": "疾苇",
}


def register():
    """把 8 个画法与 3 条装饰登记进 `gen_item_png` 的同一张表（只追加，不改它）。"""
    for k, fn in PLANT_ICONS.items():
        G.ICONS[k] = fn
    G.SLASH.update(SLASH_IDS)


# ════════════════════════════════════════════════════════════════════════════
#  画 + 校验 + 落盘
# ════════════════════════════════════════════════════════════════════════════
def render_all():
    register()
    return {k: G.to_rgba(G.build(k, AC)) for k in PLANT_ICONS}


def sig(img):
    return hashlib.sha256(img.tobytes()).hexdigest()[:16]


def bbox_of(img):
    px = img.load()
    x0, y0, x1, y1 = W, H, -1, -1
    for y in range(H):
        for x in range(W):
            if px[x, y][3]:
                x0 = min(x0, x); y0 = min(y0, y)
                x1 = max(x1, x); y1 = max(y1, y)
    return (x0, y0, x1, y1) if x1 >= 0 else None


def existing_hashes(exclude_ids):
    """既有 62 张（以及任何已存在的 icon_*）的像素哈希 —— 用来证明"新 8 张不是复制品"。"""
    from PIL import Image
    out = {}
    if not os.path.isdir(UI_DIR):
        return out
    for f in sorted(os.listdir(UI_DIR)):
        if not f.endswith(".png"):
            continue
        sid = f[:-4]
        if sid in ("icon_plant_" + i for i in exclude_ids):
            continue
        if sid.startswith("icon_plant_" + PLANT_ICONS.get("__none__", "\0")):
            continue
        try:
            im = Image.open(os.path.join(UI_DIR, f)).convert("RGBA")
        except Exception:
            continue
        if im.size != (W, H):
            continue
        out[sid] = sig(im)
    return out


def palette_ok(img):
    px = img.load()
    alphas, colors = set(), set()
    for y in range(H):
        for x in range(W):
            r, g, b, a = px[x, y]
            alphas.add(a)
            if a:
                colors.add((r, g, b, a))
    return alphas, colors


def check(images, verbose=True):
    """6 条硬约束逐条判。返回 (ok, lines)。"""
    lines, problems = [], []
    pal = {OUT_C, HI, B1, B2, B3, AC}
    lines.append("画布 %d×%d｜调色板 %d 色（描边/高光/主体×3/植物强调）" % (W, H, len(pal)))
    seen = {}
    olds = existing_hashes(set(PLANT_ICONS))
    old_sig = set(olds.values())
    for k in PLANT_ICONS:
        img = images[k]
        alphas, colors = palette_ok(img)
        bb = bbox_of(img)
        h = sig(img)
        px = sum(1 for c in range(W) for r in range(H) if img.load()[c, r][3])
        lines.append(
            "%-13s %s %2d×%-2d bbox=%s px=%4d colors=%d %s"
            % (k, PLANT_NAMES[k], img.size[0], img.size[1], "%s" % (bb,), px,
               len(colors), h))
        if img.size != (W, H):
            problems.append("%s 尺寸 %s ≠ 64×64" % (k, img.size))
        if alphas - {0, 255}:
            problems.append("%s alpha 有中间值 %r（必须硬边 0/255）"
                            % (k, sorted(alphas - {0, 255})[:4]))
        bad = colors - pal
        if bad:
            problems.append("%s 用了调色板外的颜色 %r" % (k, sorted(bad)[:3]))
        if len(colors) > 6:
            problems.append("%s 用了 %d 色 > 6" % (k, len(colors)))
        if bb is None or min(bb[2] - bb[0] + 1, bb[3] - bb[1] + 1) < 40:
            problems.append("%s 主体太小 %s（64×64 行里读不出来）" % (k, bb))
        if h in seen:
            problems.append("%s 与 %s 像素完全相同" % (k, seen[h]))
        seen[h] = k
        if h in old_sig:
            problems.append("%s 与既有图标撞图（%s）"
                            % (k, next(s for s, v in olds.items() if v == h)))
    lines.append("两两不同：%d/%d｜与既有 %d 张不撞：%s"
                 % (len(seen), len(PLANT_ICONS), len(olds),
                    "是" if not (set(seen) & old_sig) else "否"))
    return (not problems), lines + ([""] + ["✗ " + p for p in problems] if problems else [])


def write(images):
    from PIL import Image  # noqa: F401
    written = []
    for k, img in images.items():
        p = os.path.join(UI_DIR, "icon_plant_%s.png" % k)
        img.save(p, "PNG", optimize=True)
        written.append((p, os.path.getsize(p)))
    return written


def sheet(images):
    from PIL import Image, ImageDraw
    cell, pad, cols = 64, 6, 4
    rows = (len(images) + cols - 1) // cols
    lab = 10
    img = Image.new("RGBA", (cols * (cell + pad) + pad, rows * (cell + pad + lab) + pad),
                    (26, 26, 28, 255))
    dr = ImageDraw.Draw(img)
    for i, k in enumerate(PLANT_ICONS):
        cx = pad + (i % cols) * (cell + pad)
        cy = pad + (i // cols) * (cell + pad + lab)
        img.alpha_composite(images[k], (cx, cy))
        dr.text((cx, cy + cell + 1), k[:13], fill=(220, 220, 210, 255))
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, "plant_png_8_sheet.png")
    img.save(p, "PNG", optimize=True)
    t = os.path.join(OUT, "plant_png_8_sheet.txt")
    with io.open(t, "w", encoding="utf-8") as fh:
        for k in PLANT_ICONS:
            fh.write("%-13s %-6s %s\n" % (k, PLANT_NAMES[k],
                                          os.path.join(UI_DIR, "icon_plant_%s.png" % k)))
    return p, t


def table_rows():
    """顺手读一遍 seeds.json，把 8 株的档位/收益打出来（验收时一眼可对）。"""
    with io.open(SEEDS, encoding="utf-8") as fh:
        seeds = {s["id"]: s for s in json.load(fh)["seeds"]}
    rows = []
    for k in PLANT_ICONS:
        s = seeds.get(k)
        if not s:
            rows.append((k, "✗ 表里没有这一行"))
            continue
        rows.append((k, "%s %s t=%s 金%s/验%s" % (s["grade"], s["mechanic"],
                                                 s.get("growTime"),
                                                 s.get("goldYield"), s.get("expYield"))))
    return rows


USAGE = __doc__


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(USAGE)
        print("表内现状：")
        for k, v in table_rows():
            print("  %-13s %s" % (k, v))
        return 0

    do_write = "--write" in argv
    do_check = "--check" in argv or not do_write
    do_sheet = "--sheet" in argv
    if not (do_write or do_check or do_sheet):
        print(USAGE)
        return 0

    images = render_all()
    ok, lines = check(images)
    for ln in lines:
        print(ln)
    if do_write:
        for p, n in write(images):
            print("写入 %s（%d 字节）" % (os.path.relpath(p, ROOT).replace("\\", "/"), n))
    if do_sheet:
        p, t = sheet(images)
        print("对照图 %s" % os.path.relpath(p, ROOT).replace("\\", "/"))
        print("清  单 %s" % os.path.relpath(t, ROOT).replace("\\", "/"))
    print("结论：%s" % ("8/8 通过" if ok else "有 %d 条不通过（见上）" % len([l for l in lines if l.startswith("✗ ")])))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
