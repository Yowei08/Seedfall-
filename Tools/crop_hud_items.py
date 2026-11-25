"""把整屏截图裁成"每一项一张"的前后对比图。

为什么用裁切而不是每项单独截图：
  `DebugApi.Capture` 走 `ScreenCapture`，一次只能拍整屏。四类元素同时出现在一帧里，
  所以"每项的前后图"= 同一帧的**同一块矩形**在改前/改后各裁一次 —— 这样两块区域的底图
  （草/木/夜）也天然一致，比较是公平的。

坐标换算：HUD 整体套了 `GUI.matrix` 缩放 1.35（tuning.json → ui.scale），
所以屏幕像素 = 虚拟坐标 × 1.35。

用法：python crop_hud_items.py <before.png> <after.png> [<outdir>]
"""
import sys
import pathlib
from PIL import Image

SCALE = 1.35

# 每项：名字 → 虚拟坐标矩形 (x, y, w, h)，四周留一圈边距，让人看清"周围有没有底板"
#
# ⚠️ 这些 x 是**按 1920×1080 ÷ 1.35 算出来的**（virtualW=1422 / virtualH=800），
#    不是随手取的整数。用错一次就会裁到隔壁元素上（本脚本第一版就是）——
#    所以下面每一行都写着它对应的那处 `Panel(...)` 原句，改布局时照它复核。
ITEMS = {
    # DrawVitals —— 原 `Panel(new Rect(10, 10, 330, 122))`
    "1-hp-bar":        (0,     0,   360, 140),
    # DrawWave —— 原 `Panel(new Rect(x, 8, 640, 66))`，x = (virtualW-640)/2 = 391
    "2-countdown":     (385,   0,   652, 92),
    # DrawAmmoBar —— 原 `Panel(new Rect(virtualW/2-260, virtualH-90, 520, 78))`，x = 451
    "3-charge":        (443,   700, 536, 96),
    # DrawWeaponPanel —— 原 `Panel(new Rect(virtualW-394, 150, 380, 558))`，x = 1028
    "4-weapon-switch": (1020,  144, 396, 572),
    # DrawPlantPanel —— 原 `Panel(new Rect(10, 354, 380, 440))`
    "5-seed-bag":      (5,     348, 390, 450),
}


def px(v):
    return int(round(v * SCALE))


def crop(src: pathlib.Path, outdir: pathlib.Path, tag: str):
    img = Image.open(src).convert("RGB")
    W, H = img.size
    made = []
    for name, (vx, vy, vw, vh) in ITEMS.items():
        box = (max(0, px(vx)), max(0, px(vy)),
               min(W, px(vx + vw)), min(H, px(vy + vh)))
        if box[2] - box[0] < 8 or box[3] - box[1] < 8:
            print(f"  SKIP {name}: degenerate box {box}")
            continue
        out = outdir / f"{name}-{tag}.png"
        img.crop(box).save(out)
        made.append((name, box, out.stat().st_size))
    return made


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        print("usage: python crop_hud_items.py <before.png> <after.png> [<outdir>]")
        return 2
    before = pathlib.Path(sys.argv[1])
    after = pathlib.Path(sys.argv[2])
    outdir = pathlib.Path(sys.argv[3]) if len(sys.argv) > 3 else before.parent / "ui-compare"
    outdir.mkdir(parents=True, exist_ok=True)

    for src, tag in ((before, "before"), (after, "after")):
        print(f"[{tag}] {src.name}")
        for name, box, size in crop(src, outdir, tag):
            print(f"  {name:16s} box={box} -> {size} B")
    print(f"outdir = {outdir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
