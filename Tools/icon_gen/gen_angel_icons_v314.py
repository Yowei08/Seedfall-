# -*- coding: utf-8 -*-
"""v314：给两件新道具出图标 —— `angel_descent`（天使降临）/ `fallen_angel_feather`（陨落天使的羽毛）。

════════════════════════════════════════════════════════════════════════
★ 复现参数（全部写死，脚本内**没有任何随机源** ⇒ 两次运行逐字节相同）★
  画布     : 64×64 RGBA，PNG
  外壳     : `import restyle_icons_v258 as V258` —— 常量与图元函数全部复用
             SIZE=64 / RADIUS=12 / OBJ=44 / OUTLINE=#2B211B /
             THEMES["item"] 的上下渐变 / rounded_mask / gradient / outline_of
  合成顺序 : 与 `V258.restyle()` 的 ①~④ 逐条一致：
             ① 渐变底板（圆角 12）
             ② 左上 1px 内高光 rgba(255,255,255,70)
             ③ 物件：外扩 2px 深描边 + 右下 1px 投影(12,9,6,120) + 居中 44×44
             ④ 外框 2px OUTLINE
  物件区   : 44×44 透明画布，图元一律画在 3..40 之间（给 2px 描边留位）
  物件画法 : **硬边像素**（不抗锯齿）+ 平涂 + 1 层亮面(左上) + 1 层暗面(右下) + accent 点
             与 `gen_missing_icons_v274.py` / `gen_icons_v307.py` 同一套画风
  输出     : Assets/Resources/Art/UI/icon_item_angel_descent.png
             Assets/Resources/Art/UI/icon_item_fallen_angel_feather.png
             .meta 按邻居模板（`icon_item_wax_seal.png.meta`）生成，只换 guid
             —— guid = md5("Seedfall/v314/" + 资源名) ⇒ 重跑不会换 guid ✓
  备份     : 已存在的同名 PNG 先备份到 Tools/icon_gen/backup_v314/（`--restore` 可回滚）

★ 为什么出 PNG（而不是只补 11×11 位掩码）★
  `SelfCheck` 的 `ITEM-ICON-PNG` 断言盯着"这 62 件必须有 64×64 PNG"；
  而**新加**的两件不在那 62 件的点名清单里 —— 但它们同样需要一张图，
  否则 `ImGuiHud.ItemIconOf` 会退到"品质色块"（`ItemIconMasks` 里没有它们的掩码）。
  ⇒ 出 PNG 是**唯一**能让这两件在货架/道具栏里有图的办法 ✓
  ⇒ 并且**不能**顺手往 `TableDatabase.ItemIconMasks` 里加掩码：那张表有
     "张数恰好等于 62"的断言（`ITEM-ICON-UNIQUE`），加两张会让它变红 ✗

★ 用法 ★
  python Tools/icon_gen/gen_angel_icons_v314.py            # 生成
  python Tools/icon_gen/gen_angel_icons_v314.py --dry-run   # 只打印计划（不写盘）
  python Tools/icon_gen/gen_angel_icons_v314.py --restore   # 从 backup_v314 回滚
════════════════════════════════════════════════════════════════════════
"""
import os
import sys
import math
import shutil
import hashlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw
import restyle_icons_v258 as V258          # ★ 画风复用：外壳常量与图元函数全在这里

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UI = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
BAK = os.path.join(ROOT, "Tools", "icon_gen", "backup_v314")
META_TEMPLATE = os.path.join(UI, "icon_item_wax_seal.png.meta")

SIZE = V258.SIZE          # 64
OBJ = V258.OBJ            # 44
RADIUS = V258.RADIUS      # 12
OUTLINE = V258.OUTLINE    # #2B211B
THEMES = V258.THEMES

# ────────────────────────────── 调色板（与既有图标同一族） ──────────────────────────────
INK = (0x2B, 0x21, 0x1B, 255)
GOLD = (0xE8, 0xB6, 0x4A, 255)
GOLD_L = (0xFF, 0xE6, 0x9A, 255)
GOLD_D = (0xA8, 0x7A, 0x22, 255)
BONE = (0xF6, 0xF2, 0xE6, 255)          # 暖白（羽片主体）
BONE_L = (0xFF, 0xFD, 0xF6, 255)        # 高光（左上）
BONE_D = (0xD8, 0xCE, 0xB4, 255)        # 暗面（右下）
ASH = (0x6E, 0x68, 0x60, 255)           # 焦灰（堕落羽片主体）
ASH_L = (0x94, 0x8C, 0x80, 255)
ASH_D = (0x45, 0x40, 0x3A, 255)
EMBER = (0xE8, 0x5A, 0x28, 255)         # 余烬（堕落羽尖）
EMBER_L = (0xFF, 0xA8, 0x5E, 255)


def O44():
    return Image.new("RGBA", (OBJ, OBJ), (0, 0, 0, 0))


def compose(obj, kind="item"):
    """把 44×44 的物件贴进 v258 的外壳里 —— 顺序与 `restyle_icons_v258.restyle()` ①~④ 一致。"""
    theme = THEMES[kind]
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # ① 渐变底板（圆角）
    panel = V258.gradient(SIZE, theme[0], theme[1]).convert("RGBA")
    panel.putalpha(V258.rounded_mask(SIZE, RADIUS))
    canvas.alpha_composite(panel)

    # ② 左上 1px 内高光（与 v258 同一位置、同一 alpha）
    hl = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(hl).rounded_rectangle([1, 1, SIZE - 2, SIZE - 2], radius=RADIUS - 1,
                                         outline=(255, 255, 255, 70), width=1)
    canvas.alpha_composite(hl)

    # ③ 物件：投影 → 2px 深描边 → 本体（与 v258 同一套 outline_of 形态学外扩）
    alpha = obj.getchannel("A")
    off = (SIZE - OBJ) // 2
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow.alpha_composite(V258.outline_of(alpha, 2, (12, 9, 6, 120)), (off + 1, off + 2))
    canvas.alpha_composite(shadow)
    stroke = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    stroke.alpha_composite(V258.outline_of(alpha, 2, OUTLINE), (off, off))
    canvas.alpha_composite(stroke)
    canvas.alpha_composite(obj, (off, off))

    # ④ 外框：统一 2px 深描边（压在底板边缘）
    frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle([1, 1, SIZE - 2, SIZE - 2], radius=RADIUS,
                                            outline=OUTLINE, width=2)
    canvas.alpha_composite(frame)
    return canvas


# ══════════════════════════════════════════════════════════════════════
#  羽片：一行一行画（每行一条横线）⇒ 硬边轮廓，不做任何抗锯齿
# ══════════════════════════════════════════════════════════════════════

def draw_feather(d, cx, y0, y1, wmax, body, light, dark, quill, barbs=5):
    """在 `d`（O44 的 ImageDraw）上画一片**竖直**羽毛：尖头在上、羽柄在下。

    · 半宽 = `wmax · sin(π·u)^0.7`（u 从 0 到 1）⇒ 两头收尖、中段最宽
    · 亮面：每行左侧 1~2 px 画 `light`；暗面：右侧 1~2 px 画 `dark`
    · 羽柄：中心 1 px `quill` 通到底
    · 羽枝：`barbs` 组短斜线，向两侧分开（读作"这是一片羽毛"，不是一块牌子）
    """
    rows = {}
    for y in range(y0, y1 + 1):
        u = (y - y0) / max(1, (y1 - y0))
        half = int(round(wmax * (math.sin(math.pi * u) ** 0.7)))
        if half < 1:
            half = 1
        rows[y] = (cx - half, cx + half)
        d.line([(cx - half, y), (cx + half, y)], fill=body)
        # 亮面（左上光）：只画左边 1~2 px
        d.line([(cx - half, y), (cx - half + (1 if half > 4 else 0), y)], fill=light)
        # 暗面（右下影）：只画右边 1~2 px
        d.line([(cx + half - (1 if half > 4 else 0), y), (cx + half, y)], fill=dark)
    # 羽柄（中心线）
    d.line([(cx, y0 + 1), (cx, y1 - 1)], fill=quill)
    # 羽枝：在宽度最大的那一段，向两侧各画一条 2 px 的斜线
    span = y1 - y0
    for k in range(barbs):
        y = y0 + int(span * (0.22 + 0.58 * k / max(1, barbs - 1)))
        half = max(2, rows[y][1] - cx)
        d.line([(cx, y), (cx - half + 1, y + 2)], fill=quill)
        d.line([(cx, y), (cx + half - 1, y + 2)], fill=quill)
    return rows


def icon_angel_descent():
    """「天使降临」：**金色光环 + 一片暖白羽毛**（从上垂下来）+ 两点圣光。

    读法：光环 = 天使；羽毛 = 复活时的飘落；金色 = S 档品质。
    """
    obj = O44()
    d = ImageDraw.Draw(obj)

    # ① 光环（椭圆描边 2px，金色 + 顶部亮一档）—— 位置压在羽毛上方，不挡羽身
    d.ellipse([13, 4, 31, 12], outline=GOLD_D, width=2)
    d.ellipse([14, 5, 30, 11], outline=GOLD, width=1)
    d.line([(17, 4), (22, 4)], fill=GOLD_L)          # 顶部高光

    # ② 羽毛（暖白，竖直，尖朝上、柄朝下）
    draw_feather(d, 22, 14, 39, 8, BONE, BONE_L, BONE_D, GOLD_D, barbs=5)

    # ③ 两点圣光（四角星，左上 + 右下各一）—— accent，不抢主体
    for (sx, sy, r) in ((10, 20, 2), (33, 30, 2)):
        d.line([(sx - r, sy), (sx + r, sy)], fill=GOLD_L)
        d.line([(sx, sy - r), (sx, sy + r)], fill=GOLD_L)
        d.point((sx, sy), fill=(255, 255, 255, 255))
    return obj


def icon_fallen_angel_feather():
    """「陨落天使的羽毛」：**同一片羽毛，但焦灰、倾斜、羽尖烧红、没有光环**。

    读法：与「天使降临」同形不同色 ⇒ 一眼看得出是它的残渣；
          倾斜 + 焦边 + 余烬 = "堕落"；**没有光环** = 天使已经不在了。
    倾斜用 `NEAREST` 旋转 ⇒ 仍然是硬边像素（不引入抗锯齿）。
    """
    obj = O44()
    d = ImageDraw.Draw(obj)

    # ① 羽片本体（焦灰）+ 缺刻（右上咬掉一口 ⇒ 破损感）
    draw_feather(d, 22, 6, 34, 8, ASH, ASH_L, ASH_D, INK, barbs=5)
    d.polygon([(27, 10), (32, 12), (29, 16), (26, 13)], fill=(0, 0, 0, 0))

    # ② 羽柄末端：一段烧红的余烬（tip 朝下，被烧断）
    d.line([(22, 34), (22, 37)], fill=EMBER)
    d.line([(21, 36), (23, 36)], fill=EMBER_L)
    d.ellipse([20, 37, 24, 40], fill=EMBER)
    d.ellipse([21, 38, 23, 39], fill=EMBER_L)

    # ③ 倾斜（NEAREST = 硬边）—— 与「天使降临」的竖直羽毛形成形状差异
    obj = obj.rotate(-24, resample=Image.NEAREST, center=(22, 22))
    return obj


# ══════════════════════════════════════════════════════════════════════
#  写盘
# ══════════════════════════════════════════════════════════════════════

TARGETS = (
    ("angel_descent", icon_angel_descent),
    ("fallen_angel_feather", icon_fallen_angel_feather),
)


def res_name(item_id):
    return "icon_item_" + item_id


def make_meta(png_path, res):
    """按邻居模板写 .meta：只换 guid（同一资源名恒定 ⇒ 重跑不会换 guid）。"""
    tpl = open(META_TEMPLATE, encoding="utf-8").read()
    guid = hashlib.md5(("Seedfall/v314/" + res).encode("utf-8")).hexdigest()
    lines = []
    for ln in tpl.split("\n"):
        if ln.startswith("guid: "):
            lines.append("guid: " + guid)
        else:
            lines.append(ln)
    open(png_path + ".meta", "w", encoding="utf-8", newline="\n").write("\n".join(lines))


def main():
    dry = "--dry-run" in sys.argv
    restore = "--restore" in sys.argv
    os.makedirs(BAK, exist_ok=True)

    if restore:
        n = 0
        for f in sorted(os.listdir(BAK)):
            src = os.path.join(BAK, f)
            dst = os.path.join(UI, f)
            shutil.copy2(src, dst)
            n += 1
        print("已从 backup_v314 回滚 %d 个文件" % n)
        return

    for item_id, fn in TARGETS:
        res = res_name(item_id)
        png = os.path.join(UI, res + ".png")
        if os.path.exists(png):
            bak = os.path.join(BAK, res + ".png")
            if not os.path.exists(bak):
                shutil.copy2(png, bak)
        if dry:
            print("would write", png)
            continue
        img = compose(fn())
        assert img.size == (SIZE, SIZE), img.size
        img.save(png)
        make_meta(png, res)
        with open(png, "rb") as f:
            h = hashlib.sha256(f.read()).hexdigest()[:16]
        print("%-34s %d×%d  sha256=%s" % (res + ".png", img.size[0], img.size[1], h))


if __name__ == "__main__":
    main()