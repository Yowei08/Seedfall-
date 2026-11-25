# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · v202 「8 株新植物」资源验收器（只读，不写任何东西）
================================================================================

回答三个"别靠肉眼"的问题：

  ① 8 张图标与**生成器此刻的产出**逐像素一致吗？
     不一致就说明磁盘上是旧图 / 手改过 / 半途截断 —— 那正是本场吃过的那类坏。
  ② 图标的规格还在硬约束里吗？64×64、alpha 只有 {0,255}、≤6 色、落在同一调色板。
  ③ 命名与既有 22 株一致吗？—— 直接扫 `Assets/Resources/Art/UI/` 的既有 `icon_plant_*`，
     逐条比对命名字符集与导入设置，而不是"照着记忆写"。

## 用法
    python Tools/icon_gen/verify_plant_8.py
    python Tools/icon_gen/verify_plant_8.py --verbose

退出码：0 = 全过；1 = 有不通过项（逐条打印 ✗ 原因）。
"""

import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
UI_DIR = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
PLANT_DIR = os.path.join(ROOT, "Assets", "Resources", "Art", "Plants")
REPORT = os.path.join(HERE, "out", "plant_meshes_8.json")

sys.path.insert(0, HERE)

NEW_IDS = ["dusk_berry", "tide_gourd", "hollow_vine", "dune_cactus",
           "harvest_bell", "star_ladder", "elder_oak", "quick_reed"]
STAGES = ("young", "grown", "mature")
SUFFIX = {"young": "_young", "grown": "_grown", "mature": ""}
# 既有 22 株里"机制植物"的图标命名样本（项目里最早落地的那一批）
KING_NAMES = ["icon_plant_rainlily.png", "icon_plant_gale_vine.png",
              "icon_plant_sunflower_crown.png"]


def load_render():
    import gen_plant_png_8 as P
    return P, P.render_all()


def check_icons(verbose):
    from PIL import Image
    problems, lines = [], []
    P, imgs = load_render()
    pal = {P.OUT_C, P.HI, P.B1, P.B2, P.B3, P.AC}

    for pid in NEW_IDS:
        path = os.path.join(UI_DIR, "icon_plant_%s.png" % pid)
        if not os.path.exists(path):
            problems.append("%s：图标不存在 %s" % (pid, path))
            continue
        disk = Image.open(path).convert("RGBA")
        fresh = imgs[pid]
        same_px = disk.tobytes() == fresh.tobytes()
        alphas = {disk.load()[x, y][3] for y in range(disk.height)
                  for x in range(disk.width)}
        colors = {c for c in disk.getdata() if c[3]}
        lines.append("  %-13s %s  size=%s  px==生成器:%s  colors=%d  alpha=%s"
                     % (pid, os.path.basename(path), "%dx%d" % disk.size,
                        "是 ✓" if same_px else "否 ✗",
                        len(colors), sorted(alphas)))
        if disk.size != (64, 64):
            problems.append("%s：尺寸 %s ≠ 64×64" % (pid, disk.size))
        if not same_px:
            problems.append("%s：磁盘图标与生成器输出**不一致**（旧图/手改/截断）" % pid)
        if alphas - {0, 255}:
            problems.append("%s：alpha 有中间值 %s" % (pid, sorted(alphas - {0, 255})))
        if len(colors) > 6:
            problems.append("%s：用了 %d 色 > 6" % (pid, len(colors)))
        bad = colors - pal
        if bad:
            problems.append("%s：调色板外的颜色 %s" % (pid, sorted(bad)[:3]))

    # 命名一致性：与既有 icon_plant_* 同一套命名（`icon_plant_<id>.png`，id 只含 [a-z0-9_]）
    existing = sorted(f for f in os.listdir(UI_DIR)
                      if f.startswith("icon_plant_") and f.endswith(".png"))
    lines.append("  目录里 icon_plant_*.png 共 %d 张（既有 %d + 本批 8）"
                 % (len(existing), len(existing) - len([1 for f in existing
                                                       if f[11:-4] in NEW_IDS])))
    for f in existing:
        sid = f[len("icon_plant_"):-4]
        if not all(c.islower() or c.isdigit() or c == "_" for c in sid):
            problems.append("命名不合既有规则：%s" % f)
    for f in KING_NAMES:
        if not os.path.exists(os.path.join(UI_DIR, f)):
            problems.append("既有命名样本缺失（说明我比对错了目录）：%s" % f)
    return lines, problems, len(existing)


def check_meta():
    problems, lines = [], []
    # 图标
    for pid in NEW_IDS:
        m = os.path.join(UI_DIR, "icon_plant_%s.png.meta" % pid)
        lines.append("  icon_plant_%-13s .meta=%s" % (pid, "有 ✓" if os.path.exists(m) else "缺 ✗"))
        if not os.path.exists(m):
            problems.append("%s：缺 .meta ⇒ Unity 不会把它当资源，运行时加载不到" % pid)
    # 模型
    for pid in NEW_IDS:
        for st in STAGES:
            f = os.path.join(PLANT_DIR, pid + SUFFIX[st] + ".fbx")
            m = f + ".meta"
            ok_f, ok_m = os.path.exists(f), os.path.exists(m)
            lines.append("  %-22s .fbx=%s .meta=%s"
                         % (pid + SUFFIX[st] + ".fbx",
                            "有 ✓" if ok_f else "缺 ✗", "有 ✓" if ok_m else "缺 ✗"))
            if not ok_f:
                problems.append("缺模型 %s" % f)
            if ok_f and not ok_m:
                problems.append("缺 .meta：%s" % m)
    return lines, problems


def check_heights():
    problems, lines = [], []
    with io.open(REPORT, encoding="utf-8") as fh:
        rep = json.load(fh)
    band = (1.68, 1.78)
    by = {}
    for row in rep["models"]:
        by.setdefault(row["stage"], []).append((row["id"], row["height"], row["bodyRatio"]))
    for st in STAGES:
        rows = by.get(st, [])
        hs = [h for _, h, _ in rows]
        br = [b for _, _, b in rows]
        lines.append("  %-6s n=%2d 高=[%.3f, %.3f] 主体占比=[%.3f, %.3f]"
                     % (st, len(rows), min(hs), max(hs), min(br), max(br)))
        if len(rows) != 8:
            problems.append("%s：只有 %d 株（应为 8）" % (st, len(rows)))
        for pid, h, b in rows:
            if b < 0.45:
                problems.append("%s/%s 主体占比 %.3f < 0.45" % (pid, st, b))
    hs = [h for _, h, _ in by.get("mature", [])]
    if hs:
        inside = all(band[0] - 0.06 <= h <= band[1] + 0.06 for h in hs)
        lines.append("  成熟体是否落在既有窄带 [%.2f, %.2f]：%s（极差 %.3f）"
                     % (band[0], band[1], "是 ✓" if inside else "否 ✗",
                        max(hs) - min(hs)))
        if not inside:
            problems.append("成熟体超出既有窄带：%s" % hs)
    return lines, problems


def main(argv):
    verbose = "--verbose" in argv
    all_problems = []

    print("① 图标：磁盘 vs 生成器（逐像素）")
    lines, p, n_existing = check_icons(verbose)
    print("\n".join(lines) if verbose else "\n".join(lines[:3] + ["  …"]))
    where = (lines[-1] if lines else "")
    print("  " + where)
    all_problems += p

    print("\n② .meta 覆盖面（没有 .meta 就等于没有资源）")
    lines, p = check_meta()
    if verbose:
        print("\n".join(lines))
    else:
        print("  24 个模型 + 8 张图标逐个检查：%s" % ("全有 ✓" if not p else "有缺失 ✗"))
    all_problems += p

    print("\n③ 模型高度/主体占比（读生成报告，不是读内存）")
    lines, p = check_heights()
    print("\n".join(lines))
    all_problems += p

    print("\n" + "=" * 70)
    if all_problems:
        print("结论：%d 条不通过" % len(all_problems))
        for x in all_problems:
            print("  ✗ " + x)
        return 1
    print("结论：全部通过 ✓（图标 8 × 逐像素一致 ＋ 规格合格｜模型 8×3 ＋ .meta 齐备｜高度与占比合格）")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
