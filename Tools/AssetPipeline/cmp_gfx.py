# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 光影 A/B 对比图生成器
=====================================
把同一机位下「改前 / 改后」两张整帧 PNG 裁到同一块区域、放大、并排拼成一张对比图，
左 = 改前，右 = 改后，顶部写清是哪一条改动。

为什么要裁+放大：1920x1080 的整帧缩到预览尺寸后，SSAO 在墙根的那点压暗、
阴影贴地的那几像素、环境光三色在竖直面上的渐变，全部会被缩略图吃掉
⇒ 「有对比」和「看得出对比」是两件事。

用法：
  python cmp_gfx.py <before.png> <after.png> <out.png> "<标题>" [x0 y0 x1 y1] [zoom]
"""
import sys
from PIL import Image, ImageDraw, ImageFont

DEFAULT_BOX = (430, 40, 1180, 760)     # 默认裁「主城建筑群」那一块（屋顶/树/墙根都在里面）
DEFAULT_ZOOM = 1.15


def load_font(size):
    for name in ("msyh.ttc", "msyhbd.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            continue
    return ImageFont.load_default()


def main():
    if len(sys.argv) < 5:
        print(__doc__)
        return
    before, after, out, title = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
    box = DEFAULT_BOX
    zoom = DEFAULT_ZOOM
    if len(sys.argv) >= 9:
        box = tuple(int(v) for v in sys.argv[5:9])
        if len(sys.argv) >= 10:
            zoom = float(sys.argv[9])

    a = Image.open(before).convert("RGB").crop(box)
    b = Image.open(after).convert("RGB").crop(box)
    w, h = a.size
    w, h = int(w * zoom), int(h * zoom)
    a = a.resize((w, h), Image.LANCZOS)
    b = b.resize((w, h), Image.LANCZOS)

    pad, head = 10, 46
    sheet = Image.new("RGB", (w * 2 + pad * 3, h + head + pad * 2), (26, 26, 28))
    sheet.paste(a, (pad, head + pad // 2))
    sheet.paste(b, (pad * 2 + w, head + pad // 2))

    d = ImageDraw.Draw(sheet)
    f = load_font(22)
    fs = load_font(18)
    d.text((pad, 12), title, fill=(255, 236, 178), font=f)
    d.text((pad, head + pad // 2 + h + 2), "改前", fill=(220, 220, 220), font=fs)
    d.text((pad * 2 + w, head + pad // 2 + h + 2), "改后", fill=(220, 220, 220), font=fs)
    # 中线
    d.line([(pad + w + pad // 2, head), (pad + w + pad // 2, head + h + pad)], fill=(90, 90, 96), width=2)

    sheet.save(out)
    print(f"[CMP] {out}  {sheet.width}x{sheet.height}  (crop={box} zoom={zoom})")


if __name__ == "__main__":
    main()
