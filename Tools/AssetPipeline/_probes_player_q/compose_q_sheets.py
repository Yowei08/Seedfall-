# -*- coding: utf-8 -*-
"""
Q 版玩家 · 交付对照图合成
================================================================================
把 `verify_player_q.py` 渲染出来的散图拼成两张带标注的交付图：
  Docs/captures/player_qstyle_preview.png        旧版 / 新版 Q 版 同角度对照 + 三把武器
  Docs/captures/player_qstyle_attack_poses.png   三套攻击的 起手/发力/回收 姿态稿

为什么用**合图**：验收是"一眼看出改了什么"。散图要点开六次并且记不住上一张，
同条件的并排图能直接看出头身比、帽子、配色的差别。
两张图都是**同一套相机/灯光/色调映射**渲的（见 verify_player_q.render 的注释：
Blender 默认 AgX 会把高饱和色压成灰白，高饱和是 Q 版的卖点，必须换 Standard）。

用法（用 Harness 自带 Python，已含 Pillow）：
    python compose_q_sheets.py
"""

import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
AP = os.path.dirname(HERE)                       # Tools/AssetPipeline
PROJECT = r"D:\Unity\project\Seedfall守园者"
PREVIEW = os.path.join(AP, "preview")
CAPTURES = os.path.join(PROJECT, "Docs", "captures")

BG = (246, 243, 233)
INK = (38, 32, 26)
SUB = (110, 100, 88)
ACCENT = (196, 92, 40)

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\msyhl.ttc",
    r"C:\Windows\Fonts\simhei.ttf",
    r"C:\Windows\Fonts\simsun.ttc",
]


def font(size):
    for p in FONT_CANDIDATES:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:                                # noqa: BLE001
                pass
    return ImageFont.load_default()


def load(path, box):
    """读图并按 box 等比缩放；文件不存在返回 None（缺图不该让整张合图失败）。"""
    if not os.path.exists(path):
        print(f"[COMPOSE] 缺图，跳过：{path}")
        return None
    im = Image.open(path).convert("RGB")
    im.thumbnail(box, Image.LANCZOS)
    return im


def paste_cell(sheet, dr, path, box_xywh, label, sub=None, size=(0, 0)):
    x, y, w, h = box_xywh
    im = load(path, (w - 24, h - 56))
    if im is None:
        return
    sheet.paste(im, (x + (w - im.width) // 2, y + 14 + (h - 56 - im.height) // 2))
    dr.text((x + 16, y + h - 44), label, fill=INK, font=font(20))
    if sub:
        dr.text((x + 16, y + h - 22), sub, fill=SUB, font=font(15))


def main():
    os.makedirs(CAPTURES, exist_ok=True)

    # ── 图一：旧版 / 新版对照 + 姿态 + 三把武器（3×3）──
    CW, CH = 460, 520
    cols, rows = 3, 3
    sheet = Image.new("RGB", (cols * CW, rows * CH + 76), BG)
    dr = ImageDraw.Draw(sheet)
    dr.text((20, 18), "Seedfall 守园者 · 玩家 Q 版重做（同一相机 / 同一灯光 / 同一色调映射）",
            fill=INK, font=font(24))
    dr.text((20, 48), "旧版 = 正常人体比例（头含帽占 18.1%）  新版 = Q 版（头含帽占 41.3%，帽檐 = 头半宽 × 1.042）",
            fill=SUB, font=font(16))

    cells = [
        (os.path.join(PREVIEW, "q_before_top60.png"), "旧版 · 60° 俯视",
         "头只占 18%，身体细长、配色发灰"),
        (os.path.join(PREVIEW, "q_after_top60.png"), "新版 Q 版 · 60° 俯视（主验收）",
         "自上而下：头 → 肩 → 手臂 → 躯干 → 腿"),
        (os.path.join(PREVIEW, "q_after_front.png"), "新版 Q 版 · 正面",
         "草帽黄 / 围巾红 / 衣服蓝 / 靴棕"),
        (os.path.join(PREVIEW, "q_after_side.png"), "新版 Q 版 · 侧面",
         "短粗四肢 + 圆头收尾，无尖角"),
        (os.path.join(PREVIEW, "q_after_walk60.png"), "新版 Q 版 · 走动画中途 @60°",
         "动起来之后身体依然读得出来"),
        (os.path.join(PREVIEW, "q_after_attack60.png"), "新版 Q 版 · 锄头发力帧 @60°",
         "attack 只动上半身，腿保持站姿"),
        (os.path.join(PREVIEW, "q_tool_hoe.png"), "武器 · 锄头",
         "GRIP 在原点、单位旋转（手柄 +Z）"),
        (os.path.join(PREVIEW, "q_tool_can.png"), "武器 · 水壶", "握把加粗以配 Q 版的大手"),
        (os.path.join(PREVIEW, "q_tool_fert.png"), "武器 · 肥料袋", "三把各自独立 fbx"),
    ]
    for i, (p, lab, sub) in enumerate(cells):
        cx = (i % cols) * CW
        cy = 76 + (i // cols) * CH
        paste_cell(sheet, dr, p, (cx, cy, CW, CH), lab, sub)

    out1 = os.path.join(CAPTURES, "player_qstyle_preview.png")
    sheet.save(out1)
    print(f"[COMPOSE] {out1}  ({sheet.width}x{sheet.height})")

    # ── 图二：三套攻击姿态稿 ──
    CW2, CH2 = 330, 400
    cols2, rows2 = 3, 3
    sheet2 = Image.new("RGB", (cols2 * CW2, rows2 * CH2 + 66), BG)
    dr2 = ImageDraw.Draw(sheet2)
    dr2.text((18, 14), "三套攻击动画各自动作（腿骨一律恒 0° —— 下半身交给移动动画）",
             fill=INK, font=font(22))
    dr2.text((18, 42), "横向 = 锄头 / 水壶 / 肥料袋；纵向 = 起手 / 发力 / 回收",
             fill=SUB, font=font(15))
    names = ["attack_hoe", "attack_can", "attack_fert"]
    cn = {"attack_hoe": "锄头", "attack_can": "水壶", "attack_fert": "肥料"}
    phases = [("windup", "起手"), ("strike", "发力"), ("recover", "回收")]
    for r, anim in enumerate(names):
        for c, (ph, phcn) in enumerate(phases):
            p = os.path.join(PREVIEW, f"q_pose_{anim}_{ph}.png")
            paste_cell(sheet2, dr2, p, (c * CW2, 66 + r * CH2, CW2, CH2),
                       f"{cn[anim]} · {phcn}", anim if c == 0 else None)
    out2 = os.path.join(CAPTURES, "player_qstyle_attack_poses.png")
    sheet2.save(out2)
    print(f"[COMPOSE] {out2}  ({sheet2.width}x{sheet2.height})")

    # ── 主验收图也统一到 640 方图（verifier 已经拷过一份，这里确保存在）──
    top = os.path.join(CAPTURES, "player_qstyle_top60.png")
    print(f"[COMPOSE] 主验收图 {top} 存在={os.path.exists(top)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
