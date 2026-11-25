#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Seedfall 守园者 —— ★ v212 八株新高品质植物的**档位预算算式与自检判据复算器** ★

它做什么
   把 `Assets/Scripts/Farm/PlantMechanics.cs` 里的算式与 `SelfCheck.SEED-TIER-RATE` /
   `SEED-ARCHETYPE` 的四条判据**逐条照抄成 Python**，然后：
     ① 读 `seeds.json`（22 株旧 + 8 株新 = 30 株）；
     ② 逐株复算 `(金+验)/秒` 与目标预算，打印偏差；
     ③ 决定新增那 8 株的 `goldYield / expYield / growTime` 是否**恰好落在整数格点上**；
     ④ 一键复算五档的跨档严格性、档内单调性、机制严格更低、周期轴、三元组重复。
   用途：改 `seeds.json` 之前先跑它 ⇒ 不必等 Unity 编译完才知道某株会红。

唯一事实来源（这里**不另立一套**，全部按代码逐字复写）
   预算      (金+验)/秒 = V档 × f(t) × (有机制 ? 1 − 6% : 1)
   f(t)       = 1 + 0.10 × log2(t / t档基准)          （t ≤ 基准 ⇒ f = 1）
   V档        = {D 0.50, C 0.62, B 0.80, A 0.90, S 1.06}
   t档基准    = {D 5, C 13.5, B 40, A 34, S 30}        （= SeedTierBaseGrow）
   机制补偿   = 6%（MechanicPayback）—— 公平值 = 每秒总和 × 1.06
   判据容差   = 0.5 + 1e-3（四舍五入的理论下界 ⇒ 不是放宽）

用法
   python Tools/icon_gen/seed_budget_v212.py            # 复算 + 逐株明细 + 判据全量
   python Tools/icon_gen/seed_budget_v212.py --quiet    # 只印结论与失败项（退出码 = 红了几条）
   python Tools/icon_gen/seed_budget_v212.py --search A 37 44 46 50
                                                       # 给某档与若干周期，列出**所有合法整数和**
退出码：0 = 全绿；1 = 有判据红；2 = 跑不起来（表读不到 / JSON 坏了）。
"""

import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SEEDS = os.path.join(ROOT, "Assets", "Resources", "Data", "seeds.json")

# ── 与 PlantMechanics.cs 逐字对应的常量 ────────────────────────────────
GRADES = ["D", "C", "B", "A", "S"]
V_TIER = [0.50, 0.62, 0.80, 0.90, 1.06]        # SeedTierBudget
BASE_GROW = [5.0, 13.5, 40.0, 34.0, 30.0]      # SeedTierBaseGrow
ESCALATION = 0.10                              # GrowTimeEscalation
PAYBACK = 0.06                                 # MechanicPayback
ROUND_TOL = 0.5 + 1e-3                         # SelfCheck pairTol

# SEED-ARCHETYPE 里那张**判据输入**表（注意它与 BASE_GROW 是两份事实，
# SelfCheck 用的是这一份 —— 所以这里也用它，否则复算与实际判据不是同一个数）
ARCH_BASE = [8.0, 27.0, 48.0, 42.5, 37.0]

# 用户的硬约束：成熟时间 ≤ 56 秒
MAX_GROW = 56.0


def gi(grade):
    return GRADES.index(grade)


def f_of(grade, t):
    """f(t) = 1 + 0.10 × log2(t / t档基准)；t ≤ 基准 ⇒ 1（与 GrowTimeFactor 同）"""
    base = BASE_GROW[gi(grade)]
    if t <= base or base <= 0:
        return 1.0
    return 1.0 + ESCALATION * math.log(t / base, 2)


def budget_rate(grade, t):
    """目标每秒总和（未含机制折扣）= V档 × f(t)"""
    return V_TIER[gi(grade)] * f_of(grade, t)


def expected_sum(grade, t, mech):
    """ExpectedSum：一格的整数和预算 = V档 × f(t) × t × (有机制 ? 0.94 : 1)"""
    s = budget_rate(grade, t) * t
    return s * (1.0 - PAYBACK) if mech else s


def legal_sums(grade, t, mech):
    """这一 (档, t, 机制状态) 下**所有**满足 |整数和 − 预算| ≤ 0.5 的正整数和。"""
    tgt = expected_sum(grade, t, mech)
    out = []
    for s in range(max(2, int(math.floor(tgt)) - 3), int(math.ceil(tgt)) + 4):
        if s >= 2 and abs(s - tgt) <= ROUND_TOL:
            out.append((s, s / t, s - tgt))
    return out, tgt


def run(quiet=False, search=None):
    try:
        with open(SEEDS, "r", encoding="utf-8-sig") as f:
            table = json.load(f)
    except Exception as ex:                     # 表读不到 / JSON 坏了
        print("读 seeds.json 失败：%s: %s" % (type(ex).__name__, ex))
        return 2

    seeds = table.get("seeds", [])

    if search:
        grade, times = search[0], [float(x) for x in search[1:]]
        out = ["── 档 %s：给这些周期，合法的整数和（机制植物 / 无机制植物）──" % grade]
        for t in times:
            sums, tgt = legal_sums(grade, t, True)
            plain, ptgt = legal_sums(grade, t, False)
            out.append("  t=%-5s 机制预算 %-7.3f => 合法和 %-30s | 无机制预算 %-7.3f => %s"
                       % (t, tgt, ", ".join("%d(%.4f,%+.2f)" % s for s in sums),
                          ptgt, ", ".join("%d(%.4f,%+.2f)" % s for s in plain)))
        write_report(out)
        print("seed_budget_v212 --search %s: report %s" % (grade, os.path.relpath(REPORT, ROOT)))
        return 0

    bad = []
    row_lines = []

    # ── 逐株明细 ──────────────────────────────────────────────────
    if not quiet:
        row_lines.append("══ 逐株：预算 vs 落表（|差| <= 0.501 才算过）══")
        row_lines.append("%-20s %-2s %-6s %6s %5s %5s %8s %9s %8s"
                         % ("id", "档", "机制", "grow", "金", "验", "和", "预算", "差"))
    by_grade = {}
    for d in seeds:
        g = d["grade"]
        t = float(d["growTime"])
        gold = int(d["goldYield"])
        exp = int(d["expYield"])
        mech = d.get("mechanic", "None") not in (None, "", "None")
        tgt = expected_sum(g, t, mech)
        diff = (gold + exp) - tgt
        by_grade.setdefault(g, []).append(d)
        if not quiet:
            row_lines.append("%-20s %-2s %-6s %6.1f %5d %5d %8d %9.3f %+8.3f"
                             % (d["id"], g, "机制" if mech else "-", t, gold, exp, gold + exp, tgt, diff))
        if abs(diff) > ROUND_TOL:
            bad.append("SEED-TIER-RATE① %s 整数和 %d 偏离预算 %.3f（差 %+.3f）"
                       % (d["id"], gold + exp, tgt, diff))
        if t > MAX_GROW + 1e-6:
            bad.append("硬约束 %s 成熟时间 %.1fs > %.0fs" % (d["id"], t, MAX_GROW))
        if gold < 1 or exp < 1:
            bad.append("VALIDATE %s 的金/验必须为正整数（现在 %d/%d）" % (d["id"], gold, exp))

    # ── 判据①（精确）：同档 同 t 同机制状态 ⇒ 整数和逐位相等 ──
    for g, of in by_grade.items():
        for a in range(len(of)):
            for b in range(a + 1, len(of)):
                da, db = of[a], of[b]
                if abs(float(da["growTime"]) - float(db["growTime"])) >= 1e-3:
                    continue
                ma = da.get("mechanic", "None") not in (None, "", "None")
                mb = db.get("mechanic", "None") not in (None, "", "None")
                if ma != mb:
                    continue
                if int(da["goldYield"]) + int(da["expYield"]) != int(db["goldYield"]) + int(db["expYield"]):
                    bad.append("SEED-TIER-RATE①(精确) %s 档 %s 与 %s 同期 %.1fs 却不同和"
                               % (g, da["id"], db["id"], float(da["growTime"])))

    # ── 判据②：同档（按机制状态分组）(金+验)/秒 随 t 单调不减 ──
    for g, of in by_grade.items():
        for a in of:
            for b in of:
                ma = a.get("mechanic", "None") not in (None, "", "None")
                mb = b.get("mechanic", "None") not in (None, "", "None")
                if ma != mb:
                    continue
                ta, tb = float(a["growTime"]), float(b["growTime"])
                ra = (int(a["goldYield"]) + int(a["expYield"])) / ta
                rb = (int(b["goldYield"]) + int(b["expYield"])) / tb
                if ta < tb - 1e-3 and rb < ra - 1e-6:
                    bad.append("SEED-TIER-RATE② %s 档 %s(%.1fs, %.4f) 比更短的 %s(%.1fs, %.4f) 还低"
                               % (g, b["id"], tb, rb, a["id"], ta, ra))

    # ── 判据③：机制植物严格低于同 t 无机制（没有对照则低于档基准）──
    for g, of in by_grade.items():
        for a in of:
            if a.get("mechanic", "None") in (None, "", "None"):
                continue
            ta = float(a["growTime"])
            ra = (int(a["goldYield"]) + int(a["expYield"])) / ta
            peer = None
            for b in of:
                if b.get("mechanic", "None") not in (None, "", "None"):
                    continue
                if abs(float(b["growTime"]) - ta) < 1e-3:
                    rb = (int(b["goldYield"]) + int(b["expYield"])) / float(b["growTime"])
                    peer = rb if peer is None else min(peer, rb)
            bound = peer if peer is not None else budget_rate(g, ta)
            if not (ra < bound - 1e-6):
                bad.append("SEED-TIER-RATE③ %s 档机制植物 %s %.4f 未严格低于%s %.4f"
                           % (g, a["id"], ra, "同 t 无机制" if peer is not None else "档基准", bound))

    # ── 判据④：跨档严格递增（低档最大值 < 高档最小值）──
    ladder = []
    for g in GRADES:
        of = by_grade.get(g, [])
        if not of:
            continue
        rates = [(int(d["goldYield"]) + int(d["expYield"])) / float(d["growTime"]) for d in of]
        ladder.append((g, min(rates), max(rates)))
        if len(ladder) >= 2:
            pg, plo, phi = ladder[-2]
            if not (phi < min(rates) - 1e-6):
                bad.append("SEED-TIER-RATE④ %s 档最大 %.4f 未严格低于 %s 档最小 %.4f"
                           % (pg, phi, g, min(rates)))

    # ── SEED-ARCHETYPE：象限数 + 三元组重复 ──
    arch = []
    for g in GRADES:
        of = by_grade.get(g, [])
        if not of:
            continue
        base = ARCH_BASE[gi(g)]
        is_gold = any(int(d["goldYield"]) > int(d["expYield"]) for d in of)
        is_exp = any(int(d["goldYield"]) < int(d["expYield"]) for d in of)
        is_heavy = any(float(d["growTime"]) >= base * 1.5 for d in of)
        is_fast = any(float(d["growTime"]) <= base * 0.7 for d in of)
        kinds = sum([bool(is_gold), bool(is_exp), bool(is_heavy), bool(is_fast)])
        tmin = min(float(d["growTime"]) for d in of)
        tmax = max(float(d["growTime"]) for d in of)
        cycle_axis = tmax >= tmin * 1.5
        need = min(4, len(of)) if cycle_axis else min(2, len(of))
        arch.append("%s %d/%d 种%s" % (g, kinds, need, "（周期有差异）" if cycle_axis else ""))
        if kinds < need:
            bad.append("SEED-ARCHETYPE %s 档只有 %d/%d 种（金 %s｜验 %s｜重 %s｜快 %s）"
                       % (g, kinds, need, is_gold, is_exp, is_heavy, is_fast))
        for a in range(len(of)):
            for b in range(a + 1, len(of)):
                da, db = of[a], of[b]
                if (int(da["goldYield"]) == int(db["goldYield"])
                        and int(da["expYield"]) == int(db["expYield"])
                        and abs(float(da["growTime"]) - float(db["growTime"])) < 1e-3):
                    bad.append("SEED-ARCHETYPE %s 档三元组重复：%s / %s" % (g, da["id"], db["id"]))

    # ── 汇总 ──────────────────────────────────────────────────────
    # ★ 控制台只印 ASCII（Windows 默认 GBK 代码页，✓ / ⇒ / 中文在这里会抛
    #   `UnicodeEncodeError`；中文明细一律写进 UTF-8 报告文件）★
    detail_lines = []
    detail_lines.append("══ 五档 (金+验)/秒 区间 ══")
    for item in ladder:
        detail_lines.append("  %s  %.4f ~ %.4f" % item)
    detail_lines.append("══ 象限 ══")
    for a in arch:
        detail_lines.append("  " + a)
    n_mech = sum(1 for d in seeds if d.get("mechanic", "None") not in (None, "", "None"))
    detail_lines.append("总株数 %d（机制 %d 株 / 无机制 %d 株）"
                        % (len(seeds), n_mech, len(seeds) - n_mech))
    if bad:
        detail_lines.append("FAIL 判据红 %d 条：" % len(bad))
        for b in bad:
            detail_lines.append("   - " + b)
    else:
        detail_lines.append("OK 全部判据通过（预算 ① / 单调 ② / 机制更低 ③ / 跨档 ④ / 象限 / 三元组 / 硬约束 ≤%.0fs）"
                            % MAX_GROW)
    if not quiet:
        detail_lines.extend(row_lines)
    write_report(detail_lines)

    print("seed_budget_v212: %d seeds (%d mechanic) -> report %s"
          % (len(seeds), n_mech, os.path.relpath(REPORT, ROOT)))
    if bad:
        for b in bad:
            print("  FAIL " + b.encode("ascii", "backslashreplace").decode("ascii"))
        return 1
    print("  OK all checks pass")
    return 0


REPORT = os.path.join(HERE, "out", "seed_budget_v212.txt")


def write_report(lines):
    """报告一律 UTF-8 落盘（控制台只留 ASCII 摘要）。"""
    try:
        d = os.path.dirname(REPORT)
        if d and not os.path.isdir(d):
            os.makedirs(d, exist_ok=True)
        with open(REPORT, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
    except Exception as ex:
        print("  (report write failed: %s)" % ex)


def main(argv):
    quiet = "--quiet" in argv
    search = None
    if "--search" in argv:
        i = argv.index("--search")
        search = argv[i + 1:]
        if not search or search[0] not in GRADES:
            print(__doc__)
            return 2
    tol_ok = run(quiet=quiet, search=search)
    return tol_ok


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
