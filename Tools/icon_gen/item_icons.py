#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
道具程序化图标的**离线出图 / 校验**工具（v191）。

★★ v198：**这个脚本已经不是"图标"的生产者了** ★★
  用户本轮要求"重新绘制所有图标，统一到磨刀石那种风格" ⇒ 62 张 **64×64 硬边像素 PNG**
  由同目录的 **`gen_item_png.py`** 生成（写到 `Assets/Resources/Art/UI/icon_item_<id>.png`
  与 6 张 `icon_plant_<id>.png`），运行时 `ImGuiHud.ItemIconOf` **PNG 优先** ⇒
  下面这套 11×11 掩码**不会被画出来**，它现在的身份是：
    ① `ITEM-ICON-MASK` / `ITEM-ICON-UNIQUE` 两条自检的**校验对象**（表结构仍然要合法）
    ② `ItemIconOf` 在**缺 PNG 时**的兜底（新加一件道具、忘了出图时界面不至于空）
    ③ `TableDatabase.ItemIconKeyAt` 给增益栏的**下标顺序**（`PlayerBuffs.IconIndex` 同源）
  ⇒ 本脚本继续保留，用来出**掩码对照图**（`out/items_icons_sheet.png`）；
     要看**这一版真正上屏的图标**，跑 `gen_item_png.py`，看 `out/item_png_sheet.png` ✓

★ 它读的是**唯一真相**，不另存一份数据 ★
  · 位掩码：`Assets/Scripts/Data/TableDatabase.cs` 的 `ItemIconMasks`
  · 道具表：`Assets/Resources/Data/items.json`（id / name / grade / category）
  · 配色：`TableDatabase.ItemIconPalette` 的两个分支（category 2 = 橙红，其余 = 绿）
⇒ 改形状只需改 C# 的那段字符画，本脚本重跑一次就得到新的对照图 ✓
  （反过来说：**不要**在这里手写第二份掩码 —— 那就是两份真相）

用法：
  python Tools/icon_gen/item_icons.py                # 校验 + 出对照图
  python Tools/icon_gen/item_icons.py --check        # 只校验，不出图
输出：
  Tools/icon_gen/out/items_icons_sheet.png           # **46 格**对照图（带中文字名/档位/正负色调）
  Tools/icon_gen/out/items_icons_sheet.txt           # 同一份清单的文字版（便于贴进报告）

★ v191·fx3：对照图从 22 格扩到 **39 格** ★
  新增的 17 件是**老道具补图**（用户原话"部分物品图标没有制作"）：
  water_pouch / glass_cannon / plow_weight / hardwood_haft / twin_blade / ferment_jar /
  overclock_core / thorn_collar / iron_bark / sniper_scope / barbed_tip / gambler_dice /
  wax_seal / blood_pact / greedy_sickle / hollow_fruit / heirloom
  ⇒ 图上一律标「补」，读图时一眼分得清"原本就有"与"这次补的"✓
  判据也从"22 张两两不同"提到"**39 张两两不同**"（同一份 `check()`，没删任何一条）✓

★ v196·fx4：对照图从 39 格扩到 **46 格 —— 表里 46 件道具一件不缺** ★
  又补 7 件（老道具里上一轮没轮到的那批）：
  iron_hoe / ash_pouch / scarecrow / snail_shell / sprinter_boots / lens / lucky_clover
  39 + 7 = 46 = `items.json` 的 `items` 总数 ⇒ 判据升到"**46 张两两不同**"，
  同一个 `check()` 再加一条"**表内每一件都必须有掩码**"（漏一件就红，不靠人眼数）✓
★ v197：对照图从 46 格扩到 **62 格（46 件道具 + 16 张 v197 新图）** ★
  用户本轮要求"6 植物 ＋ 10 道具 ＝ 16 张新掩码，总数 46 → 62"。
  ★ 6 株机制植物为什么也有掩码 ★
    `SeedDef` **没有图标字段**（查证过 `Defs.cs`）⇒ 要么它们共用某张道具掩码（只有 56 张，
    与"62"不符），要么把 `ItemIconMasks` 扩成"**11×11 位掩码表**"（道具 + 种子共用一套画法）。
    选后者 ⇒ 本脚本的 `check()` 同步放宽**一条**：键名必须在道具表**或种子表**里。
    其余判据（11×11、字符域 `#+.`、两两不同、表内全覆盖）**一个字没松** ✓
  ⇒ 图上：道具「新」= v197 的 10 件；种子「植」= v197 的 6 株；老道具「补」不变 ✓
"""

# ★ 24 件老道具（「补」标）。列在这里只为了在对照图上打「补」标，
#   **不是第二份掩码** —— 形状仍然只从 `TableDatabase.cs` 读 ✓
#   17（v191·fx3 补）+ 7（v196·fx4 补）= 24 = 老道具全部
BACKFILL = {
    "water_pouch", "glass_cannon", "plow_weight", "hardwood_haft", "twin_blade", "ferment_jar",
    "overclock_core", "thorn_collar", "iron_bark", "sniper_scope", "barbed_tip", "gambler_dice",
    "wax_seal", "blood_pact", "greedy_sickle", "hollow_fruit", "heirloom",
    "iron_hoe", "ash_pouch", "scarecrow", "snail_shell", "sprinter_boots", "lens", "lucky_clover",
}

# ★ v197：10 件机制道具（「新」标）与 6 株机制植物（「植」标）
V197_ITEMS = {
    "carrot_ring", "bunny_pet", "evade_helm", "big_can", "windup_gear",
    "charge_coil", "aegis_lamp", "gamble_glove", "sharp_spray", "tycoon_coin",
}
V197_SEEDS = {
    "warblade_bloom", "gale_vine", "sunflower_crown", "rainlily", "shelter_ivy", "lucky_monarch",
}
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CS = os.path.join(ROOT, "Assets", "Scripts", "Data", "TableDatabase.cs")
ITEMS = os.path.join(ROOT, "Assets", "Resources", "Data", "items.json")
SEEDS = os.path.join(ROOT, "Assets", "Resources", "Data", "seeds.json")
OUT = os.path.join(HERE, "out")

SIZE = 11  # = TableDatabase.ItemIconSize

# 与 C# 的 ItemIconPalette 一一对应（category 2 = 橙红；其余 = 绿）
PALETTE = {
    2: ((245, 128, 66), (255, 219, 117)),
    "default": ((140, 232, 145), (230, 255, 209)),
}


def parse_masks(cs_text):
    """从 TableDatabase.cs 里抽出 { id, new[] { ... } } 的字符画。"""
    start = cs_text.find("ItemIconMasks =")
    if start < 0:
        raise SystemExit("找不到 ItemIconMasks：TableDatabase.cs 结构变了，改这个正则前先看代码")
    block = cs_text[start:]
    # `new[]` 与数组体之间允许夹一行注释（表里每件都写了中文说明，那是给人看的，不能因为
    # 多一句注释就解析不到 —— 这里用 `[^{]*` 跳过它）
    pairs = re.findall(r'\{\s*"([a-z0-9_]+)"\s*,\s*new\[\][^{]*\{(.*?)\}\s*\}', block, re.S)
    masks = []
    for name, body in pairs:
        rows = re.findall(r'"([^"]*)"', body)
        masks.append((name, rows))
    return masks


def load_items():
    with io.open(ITEMS, encoding="utf-8") as f:
        return json.load(f)["items"]


def load_seeds():
    """v197：种子表。6 株机制植物的图标也登记在同一张掩码表里（理由见文件头）。"""
    with io.open(SEEDS, encoding="utf-8") as f:
        return json.load(f)["seeds"]


def check(masks, items, seeds):
    """返回问题清单（空 = 全过）。判据与 SelfCheck 的 ITEM-ICON-* 两条同源。"""
    problems = []
    by_id = {it["id"]: it for it in items}
    seed_ids = {s["id"] for s in seeds}
    if not masks:                       # 解析不出掩码 ≠ 没有问题（这是"一条都没做"）
        problems.append("一张掩码都没解析出来（TableDatabase.cs 的 ItemIconMasks 结构或正则不匹配）")
    if len(masks) != len({m[0] for m in masks}):
        problems.append("掩码 id 有重复")
    sigs = {}
    for name, rows in masks:
        # ★ v197：键名合法域 = 真实道具 **或** 真实种子（与 C# 的 `IconMaskKeyValid` 同一判据）
        if name not in by_id and name not in seed_ids:
            problems.append("掩码键既不是表里的道具也不是表里的种子：%s" % name)
        if len(rows) != SIZE:
            problems.append("%s：行数 %d ≠ %d" % (name, len(rows), SIZE))
        for i, r in enumerate(rows):
            if len(r) != SIZE:
                problems.append("%s：第 %d 行宽 %d ≠ %d（%r）" % (name, i, len(r), SIZE, r))
            extra = set(r) - set("#+.")
            if extra:
                problems.append("%s：第 %d 行有非法字符 %r" % (name, i, sorted(extra)))
        sig = tuple(rows)
        if sig in sigs:
            problems.append("%s 与 %s 的掩码完全相同（图标不许互相抄）" % (name, sigs[sig]))
        else:
            sigs[sig] = name
    # ★ v196·fx4：**表里每一件都要有掩码**（v197：56/56 件道具 + 6/6 株机制植物）★
    #   与 SelfCheck 的 `ITEM-ICON-MASK` 同一条判据。加这一条的理由：
    #   上面那个循环是"按掩码遍历"的 —— 少一件掩码它**根本看不到**（沉默漏图）✗
    #   所以再按**两张表**各遍历一遍，缺一件就点名 ✓
    have = {m[0] for m in masks}
    missing = [it["id"] for it in items if it["id"] not in have]
    if missing:
        problems.append("表里有 %d 件道具没有掩码：%s" % (len(missing), ", ".join(missing)))
    miss_seed = sorted(V197_SEEDS - have)
    if miss_seed:
        problems.append("v197 有 %d 株机制植物没有掩码：%s" % (len(miss_seed), ", ".join(miss_seed)))
    return problems


def render(masks, items, seeds, path):
    from PIL import Image, ImageDraw, ImageFont

    by_id = {it["id"]: it for it in items}
    by_seed = {s["id"]: s for s in seeds}
    font_path = r"C:\Windows\Fonts\msyh.ttc"   # 与游戏 HUD 同一个字体族（Microsoft YaHei）
    def font(px):
        try:
            return ImageFont.truetype(font_path, px)
        except Exception:
            return ImageFont.load_default()

    f_name, f_meta, f_title = font(19), font(13), font(22)
    scale = 8                     # 11×11 → 88×88，点采样（NEAREST）= 与游戏内一致
    icon = SIZE * scale
    pad, gap = 14, 10
    cols = 6
    rows = (len(masks) + cols - 1) // cols
    tw = icon + pad * 2
    th = icon + 80                                    # 80 = 三行文字（字名 / 档位 / 序号）
    W = pad + cols * (tw + gap)
    H = 64 + rows * (th + gap) + pad
    img = Image.new("RGB", (W, H), (28, 22, 17))       # 与 IconWellBg(0.32,0.24,0.16) 同色系
    d = ImageDraw.Draw(img)
    d.text((pad, 16), "Seedfall v197 图标 %d 张（%d 件道具 + %d 株机制植物；绿 = 纯增益/植物 / 橙红 = 有代价；补 = 老道具补图，新 = v197 道具，植 = v197 植物）"
           % (len(masks), len(items), len(V197_SEEDS)), font=f_title, fill=(240, 228, 200))

    for idx, (name, mask_rows) in enumerate(masks):
        it = by_id.get(name, {})
        sd = by_seed.get(name, {})
        cat = it.get("category", 1)
        main, accent = PALETTE.get(cat, PALETTE["default"])
        cx = pad + (idx % cols) * (tw + gap)
        cy = 64 + (idx // cols) * (th + gap)
        # 图标底衬（与游戏的 IconWellBg 一致）
        d.rectangle([cx, cy, cx + tw - 1, cy + th - 1], fill=(52, 39, 26))
        d.rectangle([cx, cy, cx + tw - 1, cy + 3], fill=main)   # 顶部色条
        # 掩码 → 像素（第 0 行是顶部）
        ox, oy = cx + pad, cy + pad
        for y, row in enumerate(mask_rows):
            for x, ch in enumerate(row):
                if ch == ".":
                    continue
                color = main if ch == "#" else accent
                d.rectangle([ox + x * scale, oy + y * scale,
                             ox + x * scale + scale - 1, oy + y * scale + scale - 1], fill=color)
        # v197：种子没有 name/grade 之外的字段，且没有 maxStack ⇒ 第三行改印"机制"
        if sd:
            d.text((cx + 6, oy + icon + 2), "%s" % sd.get("name", name), font=f_name, fill=(246, 240, 226))
            d.text((cx + 6, oy + icon + 24), "%s  %s  植物" % (sd.get("grade", "?"), name),
                   font=f_meta, fill=(196, 182, 158))
            d.text((cx + 6, oy + icon + 44), "#%02d  植 %s" % (idx + 1, sd.get("mechanic", "")),
                   font=f_meta, fill=(150, 220, 255))
        else:
            d.text((cx + 6, oy + icon + 2), "%s" % it.get("name", name), font=f_name, fill=(246, 240, 226))
            d.text((cx + 6, oy + icon + 24), "%s  %s  ×%s" % (it.get("grade", "?"), name, it.get("maxStack", "?")),
                   font=f_meta, fill=(196, 182, 158))
            # 序号 + 「补」/「新」标（第三行，**不压图标也不压档位**）
            tag = " 补" if name in BACKFILL else (" 新" if name in V197_ITEMS else "")
            d.text((cx + 6, oy + icon + 44), "#%02d%s" % (idx + 1, tag),
                   font=f_meta, fill=(255, 214, 120) if tag else (150, 150, 150))
    img.save(path)
    return path


def main():
    args = sys.argv[1:]
    cs_text = io.open(CS, encoding="utf-8").read()
    masks = parse_masks(cs_text)
    items = load_items()
    seeds = load_seeds()
    problems = check(masks, items, seeds)
    have = {n for n, _ in masks}
    new_ids = [it["id"] for it in items][24:]          # v191 起新增的道具（会随表变长）
    covered = [m for m in new_ids if m in have]
    print("掩码 %d 张｜表内道具 %d 件｜表内种子 %d 株（其中机制植物 %d 株）"
          % (len(masks), len(items), len(seeds), len(V197_SEEDS)))
    print("  新增道具 %d 件有掩码 %d/%d" % (len(new_ids), len(covered), len(new_ids)))
    back_have = sorted(BACKFILL & have)
    back_miss = sorted(BACKFILL - have)
    print("  老道具 %d 件有掩码 %d/%d%s"
          % (len(BACKFILL), len(back_have), len(BACKFILL),
             ("｜缺：" + ",".join(back_miss)) if back_miss else ""))
    v197_it = sorted(V197_ITEMS & have)
    v197_sd = sorted(V197_SEEDS & have)
    print("  v197 机制道具 %d/10 有掩码｜v197 机制植物 %d/6 有掩码 => 合计 %d 张新图"
          % (len(v197_it), len(v197_sd), len(v197_it) + len(v197_sd)))
    if problems:
        print("问题 %d 条：" % len(problems))
        for p in problems:
            print("  - " + p)
        return 1
    print("校验 PASS：每张 11×11、字符域合法、%d 张两两不同、键都在表里、表内 %d 件道具全覆盖"
          % (len(masks), len(items)))
    if "--check" in args:
        return 0
    os.makedirs(OUT, exist_ok=True)
    sheet = os.path.join(OUT, "items_icons_sheet.png")
    render(masks, items, seeds, sheet)
    txt = os.path.join(OUT, "items_icons_sheet.txt")
    with io.open(txt, "w", encoding="utf-8") as f:
        by_id = {it["id"]: it for it in items}
        by_seed = {s["id"]: s for s in seeds}
        f.write("# Seedfall v197 图标清单（%d 张 = 表内 %d 件道具 ＋ %d 株机制植物）\n"
                % (len(masks), len(items), len(V197_SEEDS)))
        for idx, (name, _) in enumerate(masks):
            it = by_id.get(name, {})
            sd = by_seed.get(name, {})
            if sd:
                f.write("#%02d %-16s %-6s 植物        %-14s %s\n" % (
                    idx + 1, name, sd.get("grade", "?"), sd.get("mechanic", ""), sd.get("name", "")))
            else:
                f.write("#%02d %-16s %-6s %-10s ×%-3s %s%s%s\n" % (
                    idx + 1, name, it.get("grade", "?"),
                    "纯增益" if it.get("category") != 2 else "有代价",
                    it.get("maxStack", "?"), it.get("name", ""),
                    "  [老道具补图]" if name in BACKFILL else "",
                    "  [v197 机制道具]" if name in V197_ITEMS else ""))
    print("出图：%s" % sheet)
    print("清单：%s" % txt)
    return 0


if __name__ == "__main__":
    sys.exit(main())
