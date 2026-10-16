# -*- coding: utf-8 -*-
"""
把流动商店车的 4 张视图拼成交付对照图。
用 Harness 自带的 Python（有 Pillow）；Blender 自带的 Python 没有 PIL。

用法：python compose_truck_preview.py
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

PROJECT = r"D:\Unity\project\Seedfall守园者"
PREVIEW = os.path.join(PROJECT, "Tools", "AssetPipeline", "preview")
CAPTURE = os.path.join(PROJECT, "Docs", "captures", "shop_truck_preview.png")

BG = (246, 243, 233)
INK = (40, 34, 26)
DIM = (120, 110, 96)
TITLE = (30, 26, 20)
OK = (46, 110, 60)

# ★ 必须显式加载中文字体 ★ PIL 的默认位图字体只有 ASCII，
#   中文会全部渲成方块（compose_player_preview 踩过这个坑）。
FONT_CANDIDATES = [
    r"C:\Windows\Fonts\msyh.ttc",      # 微软雅黑
    r"C:\Windows\Fonts\msyhbd.ttc",
    r"C:\Windows\Fonts\simhei.ttf",    # 黑体
    r"C:\Windows\Fonts\simsun.ttc",    # 宋体
]


def load_font(size):
    for p in FONT_CANDIDATES:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:                          # noqa: BLE001
                continue
    print("[COMPOSE] ★没有找到中文字体，标题会用方块★")
    return ImageFont.load_default()


def main():
    # (文件名, 标题, 副标题)
    cells = [
        ("shop_truck_top60.png",
         "① 60° 俯视 —— 实机相机同源（车尾侧, pitch=60°）",
         "验收点：货斗里的水果是主角；招牌牌匾在驾驶室上方，不挡货斗"),
        ("shop_truck_rear34.png",
         "② 3/4 后视 —— 最贴近玩家实际看到的角度",
         "能同时读到：货斗开口、满斗彩色水果、驾驶室、招牌"),
        ("shop_truck_side.png",
         "③ 侧面 —— 农村卡车剪影 + 货斗比例",
         "驾驶室 + 敞开货斗；斗沿 1.40m，果堆顶 ≈1.44m（装到满出来）"),
        ("shop_truck_front.png",
         "④ 正面 —— 车头朝向确认（-Y / Unity +Z）",
         "圆大灯 + 横栅格栅 + 挡风玻璃；车头即 -Y 侧"),
    ]

    cw, ch = 520, 590
    cols = 2
    rows = 2
    W = cols * cw
    H = rows * ch + 132
    sheet = Image.new("RGB", (W, H), BG)
    dr = ImageDraw.Draw(sheet)

    f_big = load_font(30)
    f_mid = load_font(20)
    f_sml = load_font(16)

    dr.text((24, 18), "Seedfall 守园者 · 流动商店车（农村卡车 + 满斗水果）", fill=TITLE, font=f_big)
    facts = ("生成 gen_shop_truck.py   验证 verify_shop_truck.py（逐帧回读轮子转角）"
             "    尺寸 2.18×4.06×2.43 m    2340 三角面    17 个调色板材质")
    dr.text((24, 58), facts, fill=DIM, font=f_sml)
    dr.text((24, 84),
            "车头 = Blender −Y / Unity +Z     车底 zmin = 0.0000（原点在车底中心）"
            "     材质名全部为调色板键（TruckBody / Fruit_Red / …）",
            fill=OK, font=f_sml)

    def put(px, py, fname, title, sub):
        path = os.path.join(PREVIEW, fname)
        if not os.path.exists(path):
            dr.text((px + 20, py + 60), f"缺图: {fname}", fill=(180, 40, 40), font=f_mid)
            return
        im = Image.open(path).convert("RGB")
        box_w, box_h = cw - 30, ch - 130
        im.thumbnail((box_w, box_h))
        sheet.paste(im, (px + (cw - im.width) // 2, py + 16 + (box_h - im.height) // 2))
        dr.rectangle([px + 6, py + 6, px + cw - 6, py + ch - 6], outline=(214, 208, 194))
        dr.text((px + 20, py + ch - 96), title, fill=INK, font=f_mid)
        dr.text((px + 20, py + ch - 62), sub, fill=DIM, font=f_sml)

    for i, (f, t, s) in enumerate(cells):
        put((i % cols) * cw, 120 + (i // cols) * ch, f, t, s)

    os.makedirs(os.path.dirname(CAPTURE), exist_ok=True)
    sheet.save(CAPTURE)
    print(f"[COMPOSE] -> {CAPTURE}  ({sheet.width}x{sheet.height})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
