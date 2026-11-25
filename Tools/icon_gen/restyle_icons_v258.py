# -*- coding: utf-8 -*-
"""v258 ③：**图标风格统一** —— 把道具/植物/插件三批图标重绘到同一套模板。

── 先看清差异（`Docs/captures/v258-icons-before.png`）──
  · 道具：圆角深色底板 + 厚描边、物件填色居中 —— "瓷砖"感
  · 植物：米白/羊皮纸底板 + 深棕粗描边、饱和度很高
  · 插件：深棕底板 + **线稿**（细笔画）、粗细不一，几个还是纯色块
  ⇒ 三套底板、三套描边、三种笔画重量 ⇒ 摆在同一排里立刻"不一个游戏"✗

── 统一模板（三类共用同一套几何，只换底板的**色相**）──
  ① 外形：64×64 圆角方块，圆角半径 12，外描边 2px `#2B211B`
  ② 底板：纵向渐变（上亮下暗），三类只用**色相**区分：
        道具 = 暖褐 / 植物 = 叶绿 / 插件 = 石板蓝
  ③ 内高光：左上一条 1px 的亮线（同一位置、同一强度）⇒ 三类读作"同一个光源"
  ④ 物件：占内区 44×44（四周留 10px），统一 **2px 深描边** + 右下方 1px 投影
  ⑤ 物件本体：**保留原画**（不重画图形，只统一外壳与描边）——
     这一步很关键：222 张图标重画图形不可能一次做对，而"外壳/描边/光源"统一已经
     解决了 90% 的"不一个游戏"问题 ✓

── 做法（可复现）──
  读原 PNG → 裁掉旧底板（内缩 6px）→ 缩到 44×44 → 画新底板 → 叠上旧图形 →
  按 alpha 外扩 2px 生成统一描边 → 加投影/内高光 → 存回原路径
  原文件先备份到 `Tools/icon_gen/backup_v258/`（可回滚）✓
"""
import os, glob, shutil
from PIL import Image, ImageDraw, ImageFilter

UI = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\UI"
BAK = r"D:\Unity\project\Seedfall守园者\Tools\icon_gen\backup_v258"
SIZE = 64
RADIUS = 12
INSET = 6            # 旧底板内缩（物件都画在这 52×52 里）
OBJ = 44             # 新模板里物件的边长
OUTLINE = (0x2B, 0x21, 0x1B, 255)

# 三类底板：上亮 / 下暗（色相区分，亮度与对比度**同一条曲线**）
THEMES = {
    "item":   ((0x8A, 0x6A, 0x46), (0x54, 0x3C, 0x26)),
    "plant":  ((0x6E, 0x8A, 0x4A), (0x3C, 0x52, 0x2A)),
    "plugin": ((0x5C, 0x64, 0x78), (0x32, 0x38, 0x46)),
}


def rounded_mask(size, radius):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return m


def gradient(size, top, bottom):
    g = Image.new("RGB", (1, size))
    for y in range(size):
        t = y / max(1, size - 1)
        g.putpixel((0, y), tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return g.resize((size, size))


def outline_of(alpha, px, color):
    """按 alpha 外扩 px 生成一圈描边（用 MaxFilter 近似形态学膨胀）。"""
    m = alpha.filter(ImageFilter.MaxFilter(px * 2 + 1))
    layer = Image.new("RGBA", alpha.size, color)
    layer.putalpha(m)
    return layer


def restyle(path, kind):
    src = Image.open(path).convert("RGBA")
    if src.size != (SIZE, SIZE):
        src = src.resize((SIZE, SIZE), Image.LANCZOS)
    # ① 取旧物件层：内缩 INSET 后放大到 OBJ
    inner = src.crop((INSET, INSET, SIZE - INSET, SIZE - INSET)).resize((OBJ, OBJ), Image.LANCZOS)
    a = inner.getchannel("A")
    # 旧底板是不透明的 ⇒ 只在"明显不是底板"的地方保留：
    #   做法：把内区四角 12×12 的平均色当底板色，按色差阈值抠掉底
    px = inner.load()
    bg = [0, 0, 0, 0]
    cs = 0
    for cx, cy in ((0, 0), (OBJ - 6, 0), (0, OBJ - 6), (OBJ - 6, OBJ - 6)):
        for x in range(cx, cx + 6):
            for y in range(cy, cy + 6):
                r, g, b, al = px[x, y]
                bg[0] += r; bg[1] += g; bg[2] += b; bg[3] += al
                cs += 1
    bg = [v // cs for v in bg]
    cut = Image.new("L", (OBJ, OBJ), 0)
    cp = cut.load()
    for y in range(OBJ):
        for x in range(OBJ):
            r, g, b, al = px[x, y]
            if al < 24:
                continue
            d = abs(r - bg[0]) + abs(g - bg[1]) + abs(b - bg[2])
            if d > 26:            # 与底板色差足够大 ⇒ 是物件（阈值 26：细笔画不再被吃掉）
                cp[x, y] = al
    # 轻微收边，避免把旧描边残留带进来
    # （原来这里有一次 MinFilter(3) 收边 —— 它会把 1px 的线稿整条蚀掉，已删除）
    obj = inner.copy()
    obj.putalpha(cut)

    theme = THEMES[kind]
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    panel = gradient(SIZE, theme[0], theme[1]).convert("RGBA")
    panel.putalpha(rounded_mask(SIZE, RADIUS))
    canvas.alpha_composite(panel)

    # ② 内高光（左上 1px 亮线，三类同一位置）
    hl = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(hl).rounded_rectangle([1, 1, SIZE - 2, SIZE - 2], radius=RADIUS - 1,
                                         outline=(255, 255, 255, 70), width=1)
    canvas.alpha_composite(hl)

    # ③ 物件：统一描边 + 投影 + 居中
    off = (SIZE - OBJ) // 2
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow.alpha_composite(outline_of(cut, 2, (12, 9, 6, 120)).resize((OBJ, OBJ)),
                           (off + 1, off + 2))
    canvas.alpha_composite(shadow)
    stroke = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    stroke.alpha_composite(outline_of(cut, 2, OUTLINE), (off, off))
    canvas.alpha_composite(stroke)
    canvas.alpha_composite(obj, (off, off))

    # ④ 外框：统一 2px 深描边（压在底板边缘）
    frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle([1, 1, SIZE - 2, SIZE - 2], radius=RADIUS,
                                            outline=OUTLINE, width=2)
    canvas.alpha_composite(frame)
    return canvas


def main():
    os.makedirs(BAK, exist_ok=True)
    done = {}
    for kind in ("item", "plant", "plugin"):
        files = sorted(glob.glob(os.path.join(UI, "icon_%s_*.png" % kind)))
        n = 0
        for f in files:
            b = os.path.join(BAK, os.path.basename(f))
            if not os.path.exists(b):
                shutil.copy2(f, b)          # 备份原图（可回滚）
            try:
                out = restyle(f, kind)
            except Exception as ex:
                print("skip", os.path.basename(f), ex)
                continue
            out.save(f)
            n += 1
        done[kind] = n
    print("重绘完成：", done)


if __name__ == "__main__":
    main()
