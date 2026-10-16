# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 把剪影渲染图拼成对照图（纯 Pillow，在 Blender 之外跑）
================================================================================

为什么单独一个脚本：**Blender 自带的 Python 里没有 Pillow**（实测 `No module named 'PIL'`），
而 Harness 的 Python 有。硬把两者混在一起的结果是"渲染全做完了、拼图那一步崩掉"，
前面几十秒的渲染白费。所以职责切开：

    Blender 侧（gen_plants_qstyle_verify.py）→ 出剪影 PNG + 算 IoU + 写 plan JSON
    本脚本（普通 Python）                      → 读 plan JSON 拼图 + 写中文标签

## 拼图的公平性（这条是"三阶段成长关系"能不能被看见的关键）

三格用**同一个像素/米刻度**（都按全库最高的模型定标），所以

    图里高度之比 == 真实高度之比

于是「幼苗 → 成长中 → 成熟」在图上是**量出来**的高，而不是把三张图各自拉到一样大
再假装它们长大了。后者看起来也一样高，根本读不出成长。

用法:
    python compose_qstyle_sheets.py <plan.json>
"""

import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

FONT_PATH = r"C:\Windows\Fonts\msyh.ttc"
STAGE_CN = {"young": "幼苗", "grown": "成长中", "mature": "成熟"}
STAGE_ORDER = ("young", "grown", "mature")


def load_font(size):
    try:
        return ImageFont.truetype(FONT_PATH, size)
    except Exception:                                      # noqa: BLE001
        return ImageFont.load_default()


def compose(plan_path):
    with open(plan_path, encoding="utf-8") as fh:
        plan = json.load(fh)

    ids = plan["ids"]
    heights = plan["heights"]          # {"id/stage": 米}
    pngs = plan["pngs"]                # {"id/stage": 路径}
    out_dir = plan["outDir"]
    label_w = int(plan.get("labelWidth", 200))
    cell_w = int(plan.get("cellWidth", 260))
    row_h = int(plan.get("rowHeight", 300))
    per_sheet = int(plan.get("perSheet", 4))

    os.makedirs(out_dir, exist_ok=True)
    font = load_font(26)
    font_s = load_font(19)

    hmax = max(heights.values())
    scale = (row_h - 46) * 0.92 / hmax        # 像素/米（全库统一）

    pad = 12
    sheets = []
    for s0 in range(0, len(ids), per_sheet):
        chunk = ids[s0:s0 + per_sheet]
        W = label_w + 3 * cell_w + pad * 2
        H = pad + len(chunk) * (row_h + pad)
        sheet = Image.new("RGB", (W, H), (255, 255, 255))
        d = ImageDraw.Draw(sheet)
        # 行首标出刻度，让"像素=米"这件事可被复核
        d.text((pad, 2), "同一像素刻度：1 m = %.1f px" % scale, fill=(150, 150, 150), font=font_s)
        for ri, pid in enumerate(chunk):
            y0 = pad + ri * (row_h + pad)
            cn = plan["names"][pid]
            d.text((pad, y0 + 8), cn, fill=(20, 20, 20), font=font)
            d.text((pad, y0 + 42), pid, fill=(120, 120, 120), font=font_s)
            d.text((pad, y0 + 66), plan["forms"][pid], fill=(160, 160, 160), font=font_s)
            for ci, st in enumerate(STAGE_ORDER):
                key = "%s/%s" % (pid, st)
                im = Image.open(pngs[key])
                if im.mode == "RGBA":
                    bgc = Image.new("RGBA", im.size, (255, 255, 255, 255))
                    im = Image.alpha_composite(bgc, im)
                im = im.convert("RGB")
                hpx = max(1, int(round(heights[key] * scale)))
                wpx = max(1, int(round(hpx * im.size[0] / float(im.size[1]))))
                im = im.resize((wpx, hpx), Image.LANCZOS)
                cx = label_w + ci * cell_w + (cell_w - wpx) // 2
                base_y = y0 + row_h - 22
                cy = base_y - hpx
                d.line([(label_w + ci * cell_w + 8, base_y),
                        (label_w + (ci + 1) * cell_w - 8, base_y)],
                       fill=(205, 205, 205), width=1)
                sheet.paste(im, (cx, cy))
                d.text((label_w + ci * cell_w + 10, base_y + 3),
                       "%s  %.2f m" % (STAGE_CN[st], heights[key]),
                       fill=(80, 80, 80), font=font_s)
        sheets.append(sheet)
        out = os.path.join(out_dir, "plants_qstyle_sheet%d.png" % (s0 // per_sheet + 1))
        sheet.save(out)
        print("[COMPOSE] 并排图 -> " + out)

    W = max(s.size[0] for s in sheets)
    H = sum(s.size[1] for s in sheets) + 62
    master = Image.new("RGB", (W, H), (255, 255, 255))
    d = ImageDraw.Draw(master)
    d.text((16, 10), "Seedfall 守园者 · 植物 Q 版三阶段（幼苗 / 成长中 / 成熟）",
           fill=(15, 15, 15), font=font)
    d.text((16, 38), "三格同一像素刻度，图内高度之比 = 真实高度之比",
           fill=(120, 120, 120), font=font_s)
    y = 62
    for s in sheets:
        master.paste(s, (0, y))
        y += s.size[1]
    out = os.path.join(out_dir, "plants_qstyle_preview.png")
    master.save(out)
    print("[COMPOSE] 总览对照图 -> %s  (%dx%d)" % (out, master.size[0], master.size[1]))
    return out


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        sys.exit(0)
    compose(args[0])
