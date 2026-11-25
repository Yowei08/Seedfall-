# -*- coding: utf-8 -*-
"""镜像 RunSession.RollOne/RollPlugin/RollGoodsTier/PickPluginOfTier 的抽样逻辑，
统计 hoe_swordwave（剑气）在商店刷新里的出现率（问题 A）。

只读数据文件，不改工程。可复现：
  python -X utf8 Tools/mirror_swordwave_roll.py
"""
import json, random, math, sys, os
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Assets", "Resources", "Data")

plugins = json.load(open(os.path.join(DATA, "plugins.json"), encoding="utf-8"))["plugins"]
tuning = json.load(open(os.path.join(DATA, "tuning.json"), encoding="utf-8"))
shop = tuning["shop"]

GRADE_IDX = {"D": 0, "C": 1, "B": 2, "A": 3, "S": 4}   # GoodsQuality
SLOTS = shop["slots"]
SEED_W, PLUG_W, ITEM_W = shop["seedWeight"], shop["pluginWeight"], shop["itemWeight"]
IGW = shop["itemGradeWeights"]
CURVE = shop["rarityCurve"]


def s_at(minutes):
    """RarityCurveDef.SAt —— 逐字镜像 Defs.cs:892-902"""
    t = minutes if minutes > 0 else 0.0
    knee = CURVE["kneeMinutes"] if CURVE["kneeMinutes"] > 0.01 else 10.0
    u = t / knee if t < knee else 1.0
    ramp = u ** max(0.1, CURVE["gamma"]) if u > 0 else 0.0
    s = CURVE["s0"] + (CURVE["s10"] - CURVE["s0"]) * ramp
    if t > knee:
        s += max(0.0, CURVE["postKneePerMinute"]) * (t - knee)
    cap = CURVE["sMax"] if CURVE["sMax"] > 0 else 0.10
    return min(max(s, 0.0), cap)


def minutes_at_wave(wave):
    return float(wave - 1) if wave > 1 else 0.0


def tier_weights_raw(wave):
    """RunSession.TierWeightsRaw(itemGradeWeights, wave) —— 镜像 RunSession.cs:1909-1961"""
    n = len(IGW)
    eff = [0.0] * n
    iS = iA = iB = -1
    for i, w in enumerate(IGW):
        g = w["grade"]
        if g == "S": iS = i
        elif g == "A": iA = i
        elif g == "B": iB = i
    if iS < 0 or iA < 0 or iB < 0:
        return [max(0.01, w["weight"]) for w in IGW]
    pS, pA, pB = s_at(minutes_at_wave(wave)), CURVE["kA"] * s_at(minutes_at_wave(wave)), CURVE["kB"] * s_at(minutes_at_wave(wave))
    high = pS + pA + pB
    if high > CURVE["lowCap"] and high > 0:
        shr = CURVE["lowCap"] / high
        pS, pA, pB = pS * shr, pA * shr, pB * shr
        high = CURVE["lowCap"]
    rest = max(0.0, 1.0 - high)
    low_table = sum(max(0.0, IGW[i]["weight"]) for i in range(n) if i not in (iS, iA, iB))
    low_count = sum(1 for i in range(n) if i not in (iS, iA, iB))
    for i in range(n):
        if i in (iS, iA, iB): continue
        eff[i] = rest * max(0.0, IGW[i]["weight"]) / low_table if low_table > 0 else (rest / low_count if low_count else 0.0)
    eff[iS], eff[iA], eff[iB] = pS, pA, pB
    return eff


def roll_goods_tier(rng, wave):
    """RunSession.RollGoodsTier —— 镜像 RunSession.cs:1986-..."""
    eff = tier_weights_raw(wave)
    total = sum(eff)
    r = rng.uniform(0.0, total)
    acc = 0.0
    for i, w in enumerate(eff):
        acc += max(0.0, w)
        if r <= acc:
            return i
    return len(eff) - 1


def pick_plugin_of_tier(rng, tier):
    """RunSession.PickPluginOfTier —— 镜像 RunSession.cs:1836-1863
    注意：该档位没有资产时**退到全部**（D 档没有任何插件 ⇒ 每次 D 都是 21 选 1）"""
    pool = [p for p in plugins if GRADE_IDX[p["quality"]] == tier]
    if pool:
        return rng.choice(pool)["id"], "该档 %d 件" % len(pool)
    return rng.choice(plugins)["id"], "退到全部 21 件"


def simulate(refreshes, wave, seed=12345):
    rng = random.Random(seed)
    total = max(1, SEED_W) + max(1, PLUG_W) + max(1, ITEM_W)
    hits = Counter()          # 每次刷新里出现过几次
    plugin_offers = 0
    tiers = Counter()
    for _ in range(refreshes):
        for _slot in range(SLOTS):
            r = rng.randrange(0, total)
            if r < SEED_W:      continue           # 种子货位
            if r >= SEED_W + PLUG_W: continue      # 道具货位
            plugin_offers += 1
            tier = roll_goods_tier(rng, wave)
            tiers[tier] += 1
            pid, _why = pick_plugin_of_tier(rng, tier)
            hits[pid] += 1
    return hits, plugin_offers, tiers


def report(wave, refreshes=100000):
    hits, offers, tiers = simulate(refreshes, wave)
    sq = hits.get("hoe_swordwave", 0)
    names = {p["id"]: p["quality"] for p in plugins}
    print("── 第 %d 波（t = %g 分钟）｜%d 次刷新 × %d 货位 ──" % (wave, minutes_at_wave(wave), refreshes, SLOTS))
    print("   插件货位 %d 个 ⇒ 每次刷新 %.2f 个插件货位" % (offers, offers / refreshes))
    tw = tier_weights_raw(wave)
    tot = sum(tw)
    print("   档位概率： " + " ".join("%s=%.2f%%" % (IGW[i]["grade"], tw[i] / tot * 100) for i in range(len(tw))))
    print("   剑气(hoe_swordwave)：%d 次 ⇒ **每次刷新 %.4f%%**（每 100 次刷新 %.4f 次）｜每个插件货位 %.2f%%"
          % (sq, sq * 100.0 / refreshes, sq * 100.0 / refreshes, sq * 100.0 / max(1, offers)))
    print("   同档（B）对照：")
    for pid in [p["id"] for p in plugins if p["quality"] == "B"]:
        n = hits.get(pid, 0)
        print("     %-16s %6d 次  每次刷新 %.4f%%" % (pid, n, n * 100.0 / refreshes))
    print("   B 档外对照：")
    for pid in ["hoe_focus", "hoe_lifesteal", "hoe_warmth", "can_ripen"]:
        n = hits.get(pid, 0)
        print("     %-16s %-2s %6d 次  每次刷新 %.4f%%" % (pid, names[pid], n, n * 100.0 / refreshes))
    print()


if __name__ == "__main__":
    print("plugins.json 共 %d 件；品质分布 %s" % (
        len(plugins), dict(Counter(p["quality"] for p in plugins))))
    print("shop: slots=%d seed=%d plugin=%d item=%d ⇒ 每个货位是插件的概率 %.2f%%"
          % (SLOTS, SEED_W, PLUG_W, ITEM_W, PLUG_W * 100.0 / (SEED_W + PLUG_W + ITEM_W)))
    print()
    for w in (1, 5, 10, 21, 31):
        report(w)
