#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Seedfall 数值设计文档 · 自洽校验

做一件事：把《Docs/数值设计-v193.md》里"自洽检查"那张表的数字，
从 json / cs 原始文件里**重算一遍**并逐项比对。

用法：
    python Tools/docs/verify_numbers.py            # 校验默认文档
    python Tools/docs/verify_numbers.py --doc X.md # 校验指定文档（用来证明它会 FAIL）
    python Tools/docs/verify_numbers.py --dump     # 只打印重算值，不校验文档

判定：
    全部通过 -> exit 0
    任一项对不上 / 缺项 / 文档读不到 -> exit 1

纪律：
    * 只读 Assets/**，一个字节都不写
    * 公式从 Defs.cs / RunSession.cs 里**照着抄**（不是从注释里猜）：
        WaveGrowth(wave,a,b)   = 1 + a*t + b*t^2        t = max(0, wave-1)
        EarlyNerfFactor(wave)  t >= end ? 1 : 1 - nerf*(1 - t/end)^2
        HpForWave(wave,boss)   Boss: base*growth*(1+bossHpGrowth*(max(1,boss)-1))
                               其他: base*growth*EarlyNerfFactor(wave)
        DamageForWave(base,w)  Boss: base*growth
                               其他: base*growth*EarlyNerfFactor(wave)
        PlayerDamagePoints(x)  x <= 0 ? 0 : max(1, round(x * hpUnit))
        RerollMult(n)          = 2^n
        RerollPrice(base,w,n)  = max(1, round(base * (1+step)^(w-1)) * 2^n)
"""

import json
import math
import os
import re
import sys

# ── 输出编码（Windows 控制台默认 cp936，中文一多就抛 UnicodeEncodeError）──
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
DATA = os.path.join(ROOT, "Assets", "Resources", "Data")
SCRIPTS = os.path.join(ROOT, "Assets", "Scripts")
DOC_DEFAULT = os.path.join(ROOT, "Docs", "数值设计-v193.md")


# ══════════════════════════════════════════════════════════════════════════
# ① 读原始文件
# ══════════════════════════════════════════════════════════════════════════

def read_text(path):
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def read_json(name):
    return json.loads(read_text(os.path.join(DATA, name)))


def cs(rel):
    return read_text(os.path.join(SCRIPTS, rel))


def cs_float(src, pattern, label):
    m = re.search(pattern, src)
    if not m:
        raise SystemExit("读不到 %s（正则没命中）—— 校验脚本拒绝用默认值" % label)
    return float(m.group(1))


def cs_float_array(src, pattern, label):
    m = re.search(pattern, src)
    if not m:
        raise SystemExit("读不到 %s（正则没命中）" % label)
    return [float(x) for x in re.findall(r"[-+]?\d*\.?\d+", m.group(1))]


# ══════════════════════════════════════════════════════════════════════════
# ② 公式（照抄 Defs.cs / RunSession.cs）
# ══════════════════════════════════════════════════════════════════════════

def wave_growth(wave, a, b):
    t = max(0, wave - 1)
    return 1.0 + a * t + b * t * t


def early_nerf(wave, nerf, end):
    if nerf <= 0 or end <= 0:
        return 1.0
    t = max(0.0, wave - 1)
    if t >= end:
        return 1.0
    u = 1.0 - t / end
    return 1.0 - nerf * u * u


def hp_for_wave(base_hp, kind, wave, boss_index, G):
    if kind == "Boss":
        a, b = G["bossHpStepA"], max(0.0, G["bossHpStepB"])
        growth = wave_growth(wave, a, b)
        return base_hp * growth * (1.0 + G["bossHpGrowth"] * (max(1, boss_index) - 1))
    if kind == "Jumper" and G["eliteHpStepA"] > 0:
        a, b = G["eliteHpStepA"], max(0.0, G["eliteHpStepB"])
    else:
        a, b = (G["hpStepA"] if G["hpStepA"] > 0 else 0.12), (G["hpStepB"] if G["hpStepB"] > 0 else 0.006)
    return base_hp * wave_growth(wave, a, b) * early_nerf(wave, G["earlyNerf"], G["earlyNerfEnd"])


def dmg_for_wave(base_dmg, kind, wave, G):
    if kind == "Boss":
        a, b = G["bossDmgStepA"], max(0.0, G["bossDmgStepB"])
        return base_dmg * wave_growth(wave, a, b)
    a = G["dmgStepA"] if G["dmgStepA"] > 0 else 0.08
    b = G["dmgStepB"] if G["dmgStepB"] > 0 else 0.003
    return base_dmg * wave_growth(wave, a, b) * early_nerf(wave, G["earlyNerf"], G["earlyNerfEnd"])


def damage_points(curved, hp_unit):
    if curved <= 0:
        return 0
    return max(1, int(math.floor(curved * hp_unit + 0.5)))


def reroll_price(base, wave, count, step):
    basis = max(1, int(math.floor(base * math.pow(1.0 + step, max(0, wave - 1)) + 0.5)))
    return max(1, int(math.floor(basis * math.pow(2.0, max(0, count)) + 0.5)))


# ══════════════════════════════════════════════════════════════════════════
# ③ 重算全部关键数字
# ══════════════════════════════════════════════════════════════════════════

def compute():
    T = read_json("tuning.json")
    E = read_json("enemies.json")
    I = read_json("items.json")
    P = read_json("plugins.json")
    S = read_json("seeds.json")

    defs = cs(os.path.join("Data", "Defs.cs"))
    wsys = cs(os.path.join("Combat", "WeaponSystem.cs"))
    run = cs(os.path.join("Flow", "RunSession.cs"))
    table = cs(os.path.join("Data", "TableDatabase.cs"))

    # ── 成长系数（tuning.json 是唯一来源）──
    G = {
        "hpStepA": T["enemies"]["hpStepA"], "hpStepB": T["enemies"]["hpStepB"],
        "eliteHpStepA": T["enemies"]["eliteHpStepA"], "eliteHpStepB": T["enemies"]["eliteHpStepB"],
        "dmgStepA": T["enemies"]["dmgStepA"], "dmgStepB": T["enemies"]["dmgStepB"],
        "bossHpStepA": T["enemies"]["bossHpStepA"], "bossHpStepB": T["enemies"]["bossHpStepB"],
        "bossDmgStepA": T["enemies"]["bossDmgStepA"], "bossDmgStepB": T["enemies"]["bossDmgStepB"],
        "bossHpGrowth": T["enemies"]["bossHpGrowth"],
        "earlyNerf": T["enemies"]["earlyNerf"], "earlyNerfEnd": T["enemies"]["earlyNerfEnd"],
    }

    by_id = {e["id"]: e for e in E["enemies"]}
    grunt = by_id["grunt"]
    jumper = by_id["jumper"]
    boss = by_id["boss"]
    shooter = by_id["shooter"]

    hp_unit = T["player"]["hpUnit"]

    # ── 属性枚举（Defs.cs 是唯一来源）──
    m = re.search(r"public enum StatId\s*\{(.*?)\n\s*\}", defs, re.S)
    stat_members = re.findall(r"([A-Za-z_]\w*)\s*=\s*(\d+)", m.group(1)) if m else []

    # ── 两条等级表（Defs.cs）──
    heart_tbl = cs_float_array(defs, r"HeartDropRateByLevel\s*=\s*\{([^}]*)\}", "HeartDropRateByLevel")
    pig_tbl = cs_float_array(defs, r"PigRateByLevel\s*=\s*\{([^}]*)\}", "PigRateByLevel")

    # ── 常量（WeaponSystem.cs / RunSession.cs）──
    sub_dmg_ratio = T["formula"]["subBurstDamageRatio"]
    sword_ratio = cs_float(wsys, r"SwordWaveDamageRatio\s*=\s*([\d.]+)f", "SwordWaveDamageRatio")
    dbl_ratio = 2.0 / 3.0  # WeaponSystem.DoubleStrikeDamageRatioPerLayer = 2f / 3f
    wave_dist_mult = cs_float(run, r"SwordWaveDistMult\s*=\s*([\d.]+)f", "SwordWaveDistMult")
    wave_speed = cs_float(run, r"SwordWaveSpeed\s*=\s*([\d.]+)f", "SwordWaveSpeed")
    no_overlap = cs_float(run, r"BeamNoOverlapMargin\s*=\s*([\d.]+)f", "BeamNoOverlapMargin")

    # ── ItemIconMasks 条目数（TableDatabase.cs）──
    masks = re.findall(r'\{\s*"([a-z_0-9]+)",\s*new\[\]', table)

    # ── 价格倍率（Defs.cs 的 QualityDef.PriceMult 是 switch，写在文档里；这里核对 json 权重）──
    grade_w = sum(x["weight"] for x in T["shop"]["itemGradeWeights"])
    pattern_w = sum(x["weight"] for x in T["levelReward"]["patterns"])

    # ── 一分钟表的实算（第 1/5/10/15/20 分钟 = 波次 1/5/10/15/20）──
    mins = [1, 5, 10, 15, 20]
    rows = []
    for w in mins:
        rows.append({
            "min": w,
            "gruntHp": hp_for_wave(grunt["baseHp"], "Grunt", w, 1, G),
            "jumperHp": hp_for_wave(jumper["baseHp"], "Jumper", w, 1, G),
            "bossHp": hp_for_wave(boss["baseHp"], "Boss", w, 1, G),
            "gruntTouch": dmg_for_wave(grunt["touchDamage"], "Grunt", w, G),
            "shooterProj": dmg_for_wave(shooter["projectileDamage"], "Shooter", w, G),
            "jumperJump": dmg_for_wave(jumper["jumpDamage"], "Jumper", w, G),
            "bossTouch": dmg_for_wave(boss["touchDamage"], "Boss", w, G),
            "bossProj": dmg_for_wave(boss["projectileDamage"], "Boss", w, G),
            "bossJump": dmg_for_wave(boss["jumpDamage"], "Boss", w, G),
        })
    for r in rows:
        r["gruntTouchPt"] = damage_points(r["gruntTouch"], hp_unit)
        r["shooterProjPt"] = damage_points(r["shooterProj"], hp_unit)
        r["jumperJumpPt"] = damage_points(r["jumperJump"], hp_unit)
        r["bossTouchPt"] = damage_points(r["bossTouch"], hp_unit)
        r["bossProjPt"] = damage_points(r["bossProj"], hp_unit)
        r["bossJumpPt"] = damage_points(r["bossJump"], hp_unit)

    # BOSS 实际刷出的 bossIndex（RunSession.cs: max(1, wave/5)）
    boss_spawn_hp = {w: hp_for_wave(boss["baseHp"], "Boss", w, max(1, w // 5), G) for w in mins}

    # 刷新价序列（第 1 波 rerollBase=3，step=priceWaveStep）
    seq = [reroll_price(T["shop"]["rerollBase"], 1, n, T["shop"]["priceWaveStep"]) for n in range(12)]

    return {
        "T": T, "E": E, "I": I, "P": P, "S": S, "G": G, "by_id": by_id,
        "grunt": grunt, "jumper": jumper, "boss": boss, "shooter": shooter,
        "hp_unit": hp_unit, "stat_members": stat_members,
        "heart_tbl": heart_tbl, "pig_tbl": pig_tbl,
        "sub_dmg_ratio": sub_dmg_ratio, "sword_ratio": sword_ratio, "dbl_ratio": dbl_ratio,
        "wave_dist_mult": wave_dist_mult, "wave_speed": wave_speed, "no_overlap": no_overlap,
        "masks": masks, "grade_w": grade_w, "pattern_w": pattern_w,
        "rows": rows, "boss_spawn_hp": boss_spawn_hp, "reroll_seq": seq,
    }


# ══════════════════════════════════════════════════════════════════════════
# ④ 17 项核对：(编号, 名称, 重算值, 容差)
# ══════════════════════════════════════════════════════════════════════════

def build_checks(C):
    r20 = [r for r in C["rows"] if r["min"] == 20][0]
    r1 = [r for r in C["rows"] if r["min"] == 1][0]
    G = C["G"]
    return [
        ("V01", "敌人种类数（enemies.json）", float(len(C["E"]["enemies"])), 0.0),
        ("V02", "20 分钟 基础兵 HP", r20["gruntHp"], 0.005),
        ("V03", "20 分钟 跳跃者 HP（精英曲线）", r20["jumperHp"], 0.005),
        ("V04", "标准 HP 曲线 20 分钟倍率", wave_growth(20, G["hpStepA"], G["hpStepB"]), 5e-6),
        ("V05", "第 1 分钟 前段压低因子", early_nerf(1, G["earlyNerf"], G["earlyNerfEnd"]), 1e-9),
        ("V06", "20 分钟 基础兵碰撞伤害（整数血点）", float(r20["gruntTouchPt"]), 0.0),
        ("V07", "20 分钟 BOSS HP（bossIndex=1 曲线基准）", r20["bossHp"], 0.5),
        ("V08", "抽卡卡池项数（levelReward.stats）", float(len(C["T"]["levelReward"]["stats"])), 0.0),
        ("V09", "道具总件数（items.json）", float(len(C["I"]["items"])), 0.0),
        ("V10", "插件件数（plugins.json）", float(len(C["P"]["plugins"])), 0.0),
        ("V11", "第 1 波 第 12 次刷新价", float(C["reroll_seq"][11]), 0.0),
        ("V12", "第 10 波 首次刷新价", float(reroll_price(C["T"]["shop"]["rerollBase"], 10, 0, C["T"]["shop"]["priceWaveStep"])), 0.0),
        ("V13", "种子种类数（seeds.json）", float(len(C["S"]["seeds"])), 0.0),
        ("V14", "属性总数 StatIds.Count（Defs.cs）", float(len(C["stat_members"])), 0.0),
        ("V15", "ItemIconMasks 条目数（TableDatabase.cs）", float(len(C["masks"])), 0.0),
        ("V16", "道具档位权重之和（itemGradeWeights）", float(C["grade_w"]), 1e-9),
        ("V17", "爆炸收割比 farm.blastHarvestRatio", float(C["T"]["farm"]["blastHarvestRatio"]), 1e-9),
    ]


# ══════════════════════════════════════════════════════════════════════════
# ⑤ 从文档里读那张自洽检查表
# ══════════════════════════════════════════════════════════════════════════

ROW_RE = re.compile(r"^\|\s*(V\d{2})\s*\|\s*([^|]*?)\s*\|\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?\d+)?)\s*\|")


def parse_doc(path):
    if not os.path.isfile(path):
        raise SystemExit("文档不存在：%s" % path)
    found = {}
    for line in read_text(path).splitlines():
        m = ROW_RE.match(line)
        if m:
            found[m.group(1)] = (float(m.group(3)), m.group(2))
    return found


# ══════════════════════════════════════════════════════════════════════════
# ⑥ main
# ══════════════════════════════════════════════════════════════════════════

def main(argv):
    doc = DOC_DEFAULT
    dump = False
    for i, a in enumerate(argv[1:], 1):
        if a == "--dump":
            dump = True
        elif a == "--doc":
            if i + 1 >= len(argv):
                raise SystemExit("--doc 后面要跟一个路径")
            doc = argv[i + 1]
        elif a.startswith("--doc="):
            doc = a.split("=", 1)[1]

    C = compute()
    checks = build_checks(C)

    if dump:
        G = C["G"]
        print("── 重算值（全部来自 Assets 下的原始文件）──")
        for cid, name, val, tol in checks:
            print("  %s  %-42s %s" % (cid, name, ("%.6f" % val).rstrip("0").rstrip(".")))
        print("\n── 敌人一分钟表 ──")
        print("  分钟  基础兵HP  跳跃者HP  BOSS曲线HP  触/弹/跳(血点)")
        for r in C["rows"]:
            print("  %5d %9.4f %9.4f %9.2f   %d/%d/%d"
                  % (r["min"], r["gruntHp"], r["jumperHp"], r["bossHp"],
                     r["gruntTouchPt"], r["shooterProjPt"], r["jumperJumpPt"]))
        print("\n  前段压低(1,2,3,5,10min) = %s"
              % ", ".join("%.4f" % early_nerf(w, G["earlyNerf"], G["earlyNerfEnd"]) for w in (1, 2, 3, 5, 10)))
        print("  刷新价序列(第1波,1..12次) = %s" % C["reroll_seq"])
        print("  BOSS 实刷 HP（含 bossIndex）: %s" % C["boss_spawn_hp"])
        print("  属性数 = %d｜ItemIconMasks = %d｜卡池 = %d"
              % (len(C["stat_members"]), len(C["masks"]), len(C["T"]["levelReward"]["stats"])))
        return 0

    docvals = parse_doc(doc)
    ok = 0
    fail = 0
    missing = 0
    print("校验文档：%s" % doc)
    print("%-4s %-42s %14s %14s %s" % ("编号", "核对项", "文档值", "重算值", "结果"))
    print("-" * 96)
    for cid, name, val, tol in checks:
        if cid not in docvals:
            print("%-4s %-42s %14s %14s %s" % (cid, name, "(缺)", ("%.6f" % val), "FAIL 文档里没有这一行"))
            missing += 1
            continue
        got, _label = docvals[cid]
        good = abs(got - val) <= tol
        if good:
            ok += 1
        else:
            fail += 1
        print("%-4s %-42s %14s %14s %s"
              % (cid, name, ("%g" % got), (("%.6f" % val).rstrip("0").rstrip(".")),
                 "PASS" if good else "FAIL 差 %g（容差 %g）" % (got - val, tol)))
    print("-" * 96)
    total = len(checks)
    print("合计 %d 项：通过 %d，不符 %d，缺行 %d" % (total, ok, fail, missing))
    if ok == total:
        print("结论：PASS（exit 0）")
        return 0
    print("结论：FAIL（exit 1）")
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
