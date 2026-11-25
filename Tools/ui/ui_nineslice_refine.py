"""九宫格贴图「观感细化」生成器（Seedfall 守园者 · UI 高级化）

做三件事，**尺寸/边框/配色一律不动**，只细化观感：
  ① 圆角抗锯齿：把硬台阶的 alpha 用 3x3 tent 卷积换成亚像素覆盖率（放大后不再是楼梯格）
  ② 外缘起光：上/左外缘提亮，下/右外缘压暗 —— 纸被上方光照到的"厚度感"
  ③ 金内衬双色：内衬那一圈上/左偏亮金，下/右偏暗金（单色内衬看起来像贴纸，双色才有金属反光）

判据全部来自像素本身的颜色分类（近黑 = 墨线，金 = R>150,G>110,B<110），
不假设任何几何常量 ⇒ 同一条脚本可以跑 panel/card/button/bar/scroll 全部 11 张。

用法（可复现）：
  python ui_nineslice_refine.py <目录> [--dry-run]
备份在 Library/ui9_backup/（脚本不负责备份）。
"""
import sys
import os
import shutil
from PIL import Image

NAMES = ["panel", "panel_dark", "card", "textbox", "button", "button_hover",
         "button_press", "bar_frame", "bar_white", "scroll_track", "scroll_thumb"]


def is_ink(p):
    return max(p[0], p[1], p[2]) < 70


def is_gold(p):
    return p[0] > 140 and p[1] > 100 and p[2] < 130


def refine(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    src = [[px[x, y] for x in range(w)] for y in range(h)]

    # ── ① 圆角抗锯齿：只在"边界像素"上重算 alpha ──
    def inside(x, y):
        if x < 0 or y < 0 or x >= w or y >= h:
            return 0.0
        return 1.0 if src[y][x][3] > 127 else 0.0

    out = [[list(src[y][x]) for x in range(w)] for y in range(h)]
    kernel = [[1, 2, 1], [2, 4, 2], [1, 2, 1]]
    ksum = 16.0
    for y in range(h):
        for x in range(w):
            a = src[y][x][3]
            if a == 0 or a == 255:
                # 只在"紧贴边界"的像素上算覆盖率（内部/外部一律不动）
                acc = 0.0
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        acc += kernel[dy + 1][dx + 1] * inside(x + dx, y + dy)
                cov = acc / ksum
                if 0.0 < cov < 1.0:
                    out[y][x][3] = int(round(255 * cov))
            else:
                out[y][x][3] = a

    # ── ② + ③ 外缘起光 / 金内衬双色 ──
    # 每条边向内扫最多 5 px：第 0 圈是外缘（半透明纸边），第 1~2 圈是墨线，再往里 1 圈是金内衬
    gold_hi = (224, 186, 106)
    gold_lo = (156, 118, 50)
    for side in range(4):
        for t in range(w if side < 2 else h):
            for d in range(0, 5):
                if side == 0:      # top
                    x, y = t, d
                elif side == 1:    # bottom
                    x, y = t, h - 1 - d
                elif side == 2:    # left
                    x, y = d, t
                else:              # right
                    x, y = w - 1 - d, t
                if out[y][x][3] == 0:
                    continue
                c = out[y][x]
                if d == 0:
                    # 外缘起光：上/左亮，下/右暗
                    f = 1.05 if side < 2 else 0.90
                    c[0] = max(0, min(255, int(c[0] * f)))
                    c[1] = max(0, min(255, int(c[1] * f)))
                    c[2] = max(0, min(255, int(c[2] * f)))
                elif is_gold((c[0], c[1], c[2])):
                    # 金内衬双色（上/左亮金，下/右暗金），50% 混过去：仍是原来那只金，只是有了反光
                    tgt = gold_hi if side < 2 or (side == 2 and t < h * 0.5) else gold_lo
                    for k in range(3):
                        c[k] = int(round(c[k] * 0.5 + tgt[k] * 0.5))

    res = Image.new("RGBA", (w, h))
    rp = res.load()
    for y in range(h):
        for x in range(w):
            rp[x, y] = tuple(out[y][x][:4])
    res.save(path)
    return (w, h)


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else "."
    dry = "--dry-run" in sys.argv
    for n in NAMES:
        p = os.path.join(folder, n + ".png")
        if not os.path.exists(p):
            print("skip (missing)", n)
            continue
        if dry:
            print("would refine", n)
        else:
            print("refined", n, refine(p))


if __name__ == "__main__":
    main()
