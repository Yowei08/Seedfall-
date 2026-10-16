# -*- coding: utf-8 -*-
"""
把玩家 v2 的 60° 俯视图 + 三把武器拼成交付对照图。
用 Harness 自带的 Python（有 Pillow）；Blender 自带的 Python 没有 PIL。

用法：python compose_player_preview.py
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

PROJECT = r"D:\Unity\project\Seedfall守园者"
PREVIEW = os.path.join(PROJECT, "Tools", "AssetPipeline", "preview")
CAPTURE = os.path.join(PROJECT, "Docs", "captures", "player_v2_preview.png")

BG = (246, 243, 233)
INK = (40, 34, 26)
DIM = (120, 110, 96)
TITLE = (30, 26, 20)

# ★ 必须显式加载中文字体 ★ PIL 的默认位图字体只有 ASCII，
#   中文会全部渲成方块（第一版交付图就是这样，5 个标题全是"口口口"）。
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


def label(dr, x, y, text, fill=INK, size=1):
    dr.text((x, y), text, fill=fill)


def main():
    # (路径, 标题, 副标题)
    top_row = [
        (os.path.join(PREVIEW, "player_v2_top60.png"), "玩家 v2 · 60° 俯视（游戏相机角度）",
         "验收：头/肩/躯干/手臂/腿全部可见，不再是一块帽檐"),
        (os.path.join(PREVIEW, "player_v2_front.png"), "玩家 v2 · 正面",
         "高 1.839m，脚底 zmin=0，1676 三角面"),
    ]
    bot_row = [
        (os.path.join(PREVIEW, "tool_hoe.png"), "tool_hoe 锄头", "长 1.26m · GRIP 在原点"),
        (os.path.join(PREVIEW, "tool_can.png"), "tool_can 水壶", "高 0.29m · GRIP 在提手"),
        (os.path.join(PREVIEW, "tool_fert.png"), "tool_fert 肥料袋", "高 0.50m · GRIP 在袋口"),
    ]

    cw, ch = 520, 600
    cols = 3
    W = cols * cw
    H = 2 * ch + 96
    sheet = Image.new("RGB", (W, H), BG)
    dr = ImageDraw.Draw(sheet)

    f_big = load_font(30)
    f_mid = load_font(22)
    f_sml = load_font(18)

    dr.text((24, 18), "Seedfall 守园者 · 玩家 v2 + 三把武器", fill=TITLE, font=f_big)
    dr.text((24, 54), "生成 gen_player_v2.py   验证 verify_player_v2.py（逐帧回读骨骼角度）",
            fill=DIM, font=f_sml)

    def put(px, py, path, title, sub):
        if not os.path.exists(path):
            dr.text((px + 20, py + 60), f"缺图: {os.path.basename(path)}",
                    fill=(180, 40, 40), font=f_mid)
            return
        im = Image.open(path).convert("RGB")
        im.thumbnail((cw - 30, ch - 120))
        sheet.paste(im, (px + (cw - im.width) // 2, py + 18 + (ch - 120 - im.height) // 2))
        dr.rectangle([px + 6, py + 6, px + cw - 6, py + ch - 6], outline=(214, 208, 194))
        dr.text((px + 20, py + ch - 84), title, fill=INK, font=f_mid)
        dr.text((px + 20, py + ch - 54), sub, fill=DIM, font=f_sml)

    for i, (p, t, s) in enumerate(top_row):
        put(i * cw, 96, p, t, s)
    for i, (p, t, s) in enumerate(bot_row):
        put(i * cw, 96 + ch, p, t, s)

    os.makedirs(os.path.dirname(CAPTURE), exist_ok=True)
    sheet.save(CAPTURE)
    print(f"[COMPOSE] -> {CAPTURE}  ({sheet.width}x{sheet.height})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
