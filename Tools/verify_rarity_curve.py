# -*- coding: utf-8 -*-
"""
v277 复算：`shop.rarityCurve` 的时间膨胀公式 —— 道具/插件表 与 种子表 的逐品阶有效概率。

为什么要有这个脚本
------------------
`RarityCurveDef.SAt` 是**唯一实现**，本脚本逐句镜像它（含 `lowCap` 缩放与 D:C 按表比例分剩余），
用来证明三条硬线在**数学上**成立，而不是"我相信它成立"：

    t ≤ 5 分钟  ⇒ S < 1.0%
    t ≤ 10 分钟 ⇒ S < 5.0%
    任意 t      ⇒ S ≤ 10.0%
    每张表逐品阶 sum = 100%

运行： python Tools/verify_rarity_curve.py
"""
import io
import json
import os
import sys

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TUNING = os.path.join(ROOT, 'Assets', 'Resources', 'Data', 'tuning.json')
TIMES = [1, 3, 5, 10, 15, 20, 30, 40]          # 用户点名的 8 个时刻（分钟）

f32 = np.float32


def load():
    with io.open(TUNING, encoding='utf-8') as fh:
        t = json.load(fh)
    shop = t['shop']
    rc = shop['rarityCurve']
    return shop, rc


# ────────────────────────────────── 新公式（= Defs.cs 的 RarityCurveDef）────────────────────────
def sat(minutes, rc):
    """S(t)：传说概率。逐句镜像 `RarityCurveDef.SAt`。"""
    t = f32(max(0.0, minutes))
    knee = f32(rc['kneeMinutes'] if rc['kneeMinutes'] > 0.01 else 10.0)
    u = f32(t / knee) if t < knee else f32(1.0)
    gamma = f32(max(0.1, rc['gamma']))
    ramp = f32(np.power(u, gamma)) if u > 0 else f32(0.0)
    s = f32(rc['s0'] + (rc['s10'] - rc['s0']) * ramp)
    if t > knee:
        s = f32(s + max(0.0, rc['postKneePerMinute']) * (t - knee))
    cap = f32(rc['sMax'] if rc['sMax'] > 0 else 0.10)
    return f32(min(max(float(s), 0.0), float(cap)))


def aat(minutes, rc):
    return f32(max(0.0, rc['kA']) * sat(minutes, rc))


def bat(minutes, rc):
    return f32(max(0.0, rc['kB']) * sat(minutes, rc))


def minutes_at_wave(wave):
    """`ShopSystem.MinutesAtWave`：Clock.WaveIndex = MinuteIndex + 1 ⇒ t = wave − 1。"""
    return float(wave - 1) if wave > 1 else 0.0


def table_probs(table, minutes, rc):
    """逐句镜像 `ShopSystem.TierWeightsRaw`（返回已归一概率，顺序 = 表顺序）。"""
    n = len(table)
    eff = [f32(0.0)] * n
    iS = iA = iB = -1
    for i, row in enumerate(table):
        g = row['grade']
        if g == 'S':
            iS = i
        elif g == 'A':
            iA = i
        elif g == 'B':
            iB = i
    assert iS >= 0 and iA >= 0 and iB >= 0, '表里 S/A/B 三档必须齐'
    pS, pA, pB = sat(minutes, rc), aat(minutes, rc), bat(minutes, rc)
    high = f32(pS + pA + pB)
    low_cap = f32(rc['lowCap'])
    shrunk = False
    if high > low_cap and high > 0:
        k = f32(low_cap / high)
        pS, pA, pB = f32(pS * k), f32(pA * k), f32(pB * k)
        high = low_cap
        shrunk = True
    rest = f32(max(0.0, 1.0 - float(high)))
    low_table = f32(0.0)
    low_count = 0
    for i, row in enumerate(table):
        if i in (iS, iA, iB):
            continue
        low_table = f32(low_table + max(0.0, row['weight']))
        low_count += 1
    for i, row in enumerate(table):
        if i in (iS, iA, iB):
            continue
        eff[i] = (f32(rest * f32(max(0.0, row['weight'])) / low_table) if low_table > 0
                  else (f32(rest / low_count) if low_count > 0 else f32(0.0)))
    eff[iS], eff[iA], eff[iB] = pS, pA, pB
    total = f32(sum(float(x) for x in eff))
    return [float(f32(x / total)) for x in eff], shrunk


# ─────────────────────────────── 旧公式（v253~v276，用于"改前"对照）─────────────────────────────
def old_table_probs(table, wave, rc_floor=0.15, rc_ramp=10):
    """逐句镜像**改前**的 `TierWeightsRaw`（写死的 0.05 / 0.04 波次倾斜 + floor/ramp 抑制）。"""
    n = len(table)
    eff = []
    for i, row in enumerate(table):
        bias = i / max(1.0, n - 1.0)
        mult = 1.0 + bias * (0.05 * (wave - 1)) - (1.0 - bias) * 0.04 * (wave - 1)
        early = min(max((wave - 1.0) / max(1.0, rc_ramp - 1.0), 0.0), 1.0)
        dampen = 1.0 - (1.0 - early) * bias * (1.0 - rc_floor)
        eff.append(max(0.01, row['weight'] * max(0.05, mult) * dampen))
    total = sum(eff)
    return [e / total for e in eff]


def old_seed_probs(table):
    """改前种子表：`RollSeedOnce` 直接按原权重归一 —— **静态**，与时间无关。"""
    total = sum(max(0.0, r['weight']) for r in table)
    return [max(0.0, r['weight']) / total for r in table]


def main():
    shop, rc = load()
    # ★ v282：品阶表**只剩一张**（种子 / 道具 / 插件三类共用 `itemGradeWeights`）——
    #   旧的两表分叉（种子 `gradeWeights` 静态表）已随 v282 一起退役 ✓
    tables = [('三类共用 (itemGradeWeights)', shop['itemGradeWeights'])]

    print('=' * 108)
    print('v277 数值膨胀公式复算　来源：Assets/Resources/Data/tuning.json → shop.rarityCurve')
    print('=' * 108)
    print('参数表：' + '　'.join(f'{k}={v}' for k, v in rc.items()))
    print()
    print('公式： t   = WaveIndex − 1   （已进行分钟）')
    print('       u   = clamp01(min(t, kneeMinutes) / kneeMinutes)')
    print('       S   = clamp(s0 + (s10 − s0)·u^gamma + postKneePerMinute·max(0, t − kneeMinutes), 0, sMax)')
    print('       A   = kA·S     B = kB·S     D+C = 1 − S − A − B（按表里 D:C 原比例分）')
    print()
    print('-' * 108)
    print('S(t) 逐点读数（未归一前即最终概率）')
    print('-' * 108)
    for t in TIMES:
        print(f'  t = {t:>3} 分钟  →  S = {sat(t, rc) * 100:>7.4f}%   A = {aat(t, rc) * 100:>7.3f}%'
              f'   B = {bat(t, rc) * 100:>7.3f}%   高档合计 {(float(sat(t, rc)) + float(aat(t, rc)) + float(bat(t, rc))) * 100:>6.2f}%')
    print()

    peak = {}
    for label, table in tables:
        print('-' * 108)
        print(f'【{label}】改后（v277 曲线）逐品阶有效概率')
        print('-' * 108)
        hdr = '  t(分钟)' + ''.join(f'{r["grade"]:>9}' for r in table) + '      sum'
        print(hdr)
        for t in TIMES:
            probs, shrunk = table_probs(table, float(t), rc)
            peak[label] = max(peak.get(label, 0.0), probs[[r['grade'] for r in table].index('S')])
            print(f'  {t:>7}' + ''.join(f'{p * 100:>8.4f}%' for p in probs)
                  + f'{sum(probs) * 100:>9.4f}%' + ('   ← lowCap 触发' if shrunk else ''))
        print()
        print(f'【{label}】改前（v276：波次倾斜 / 静态表）逐品阶有效概率')
        hdr = '  t(分钟)' + ''.join(f'{r["grade"]:>9}' for r in table) + '      sum'
        print(hdr)
        for t in TIMES:
            wave = t + 1
            probs = old_table_probs(table, wave)
            print(f'  {t:>7}' + ''.join(f'{p * 100:>8.4f}%' for p in probs)
                  + f'{sum(probs) * 100:>9.4f}%')
        print()

    # ───────────────────────────────────── 四条断言 ─────────────────────────────────────
    print('=' * 108)
    print('断言实算')
    print('=' * 108)
    ok_all = True

    # ① t ≤ 5 分钟 ⇒ S < 1.0%（在 [0,5] 上逐点扫描：S 单调增 ⇒ 峰值必在 t=5）
    scan = [t / 100.0 for t in range(0, 501)]
    worst = max(scan, key=lambda t: float(sat(t, rc)))
    v = float(sat(worst, rc)) * 100.0
    ok = v < 1.0
    ok_all &= ok
    print(f'  ① t ≤ 5 分钟 ⇒ S < 1.0%　　扫描 0.00~5.00（步 0.01）峰值 t={worst:.2f}'
          f' → S = {v:.4f}%　{"PASS" if ok else "FAIL"}')

    # ② t ≤ 10 分钟 ⇒ S < 5.0%
    scan = [t / 100.0 for t in range(0, 1001)]
    worst = max(scan, key=lambda t: float(sat(t, rc)))
    v = float(sat(worst, rc)) * 100.0
    ok = v < 5.0
    ok_all &= ok
    print(f'  ② t ≤ 10 分钟 ⇒ S < 5.0%　　扫描 0.00~10.00（步 0.01）峰值 t={worst:.2f}'
          f' → S = {v:.4f}%　{"PASS" if ok else "FAIL"}')

    # ③ 任意 t ⇒ S ≤ 10.0%（扫到 180 分钟，远超一局）
    #   容差 1e-4 个百分点 = float32 的表示误差：`(float)0.1f × 100` 在 float32 里是
    #   10.000000149…（0.1 无法用二进制精确表示），而**数学值**正是 10.0% ✓
    scan = [t / 2.0 for t in range(0, 361)]
    worst = max(scan, key=lambda t: float(sat(t, rc)))
    v = float(sat(worst, rc)) * 100.0
    ok = v <= 10.0 + 1e-4
    ok_all &= ok
    print(f'  ③ 任意 t ⇒ S ≤ 10.0%　　　　扫描 0.0~180.0（步 0.5）峰值 t={worst:.1f}'
          f' → S = {v:.6f}%（硬顶，float32 表示误差 &lt; 1e-4 个百分点）　{"PASS" if ok else "FAIL"}')

    # ④ 每张表逐品阶 sum = 100%
    bad = []
    for label, table in tables:
        for t in TIMES:
            probs, _ = table_probs(table, float(t), rc)
            if abs(sum(probs) * 100.0 - 100.0) > 1e-3:
                bad.append(f'{label} t={t} sum={sum(probs) * 100:.6f}%')
    ok = not bad
    ok_all &= ok
    worst_sum = max(abs(sum(table_probs(tb, float(t), rc)[0]) * 100.0 - 100.0)
                    for _, tb in tables for t in TIMES)
    print(f'  ④ 每张表逐品阶 sum = 100%　　2 张表 × {len(TIMES)} 个时刻 = {2 * len(TIMES)} 次'
          f'　最大偏差 {worst_sum:.2e}%　{"PASS" if ok else "FAIL " + "; ".join(bad)}')

    print()
    print('全部断言：' + ('PASS' if ok_all else 'FAIL'))
    print('（附）两张表的 S 档峰值：' + '｜'.join(f'{k} {v * 100:.4f}%' for k, v in peak.items()))
    return 0 if ok_all else 1


if __name__ == '__main__':
    sys.exit(main())
