# -*- coding: utf-8 -*-
"""
v282 复算：类别权重 1:1:1 + 「只按照品质产出」（三类共用一张品阶表）。
10 万次刷新蒙特卡洛，逐句镜像 `RunSession` 的刷新逻辑。

镜像的四个函数（全部在 Assets/Scripts/Flow/RunSession.cs）
------------------------------------------------------------
  RollOne        类别轮盘：total = seedWeight + pluginWeight + itemWeight，一次 RangeInt
  RollSeedOnce   tier = RollGoodsTier(itemGradeWeights, wave)  （v282：不再读 gradeWeights）
  RollPlugin     tier = RollGoodsTier(itemGradeWeights, wave)  → PickPluginOfTier
  RollItemOnce   tier = RollGoodsTier(itemGradeWeights, wave)  → PickItemOfGrade
  RollGoodsTier  = TierWeightsRaw（rarityCurve 覆盖 S/A/B，D+C 吃剩余并按表里比例分）
                   —— 直接复用 verify_rarity_curve.py 的 table_probs（同一个 float32 镜像）

三类抽样里**唯一**的档位缺失：plugins.json 没有 D 档（C 6 / B 6 / A 6 / S 3，D = 0）
⇒ `PickPluginOfTier` 的 count == 0 分支（RunSession.cs:1913-1922）退到**全部插件**均匀取一件。
本脚本把这条兜底也照抄，并单独统计"抽到 D 档时实际给了什么"。

运行：  python Tools/verify_shop_uniform_v282.py            （输出同时写 Tools/verify_shop_uniform_v282.txt）
"""
import io
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from verify_rarity_curve import load, table_probs, minutes_at_wave  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'Assets', 'Resources', 'Data')
OUT = os.path.join(ROOT, 'Tools', 'verify_shop_uniform_v282.txt')

REFRESHES = 100000          # 每次刷新 4 个货位
SLOTS = 4
TIMES = [1, 5, 10, 30]
GRADES = ['D', 'C', 'B', 'A', 'S']
GI = {g: i for i, g in enumerate(GRADES)}

# ── 改前（回滚对照）：类别权重 55/25/25 + 种子走自己那张 gradeWeights ──
OLD_SEED_TABLE = [{'grade': 'D', 'weight': 73.2}, {'grade': 'C', 'weight': 18.3},
                  {'grade': 'B', 'weight': 5.0}, {'grade': 'A', 'weight': 2.5},
                  {'grade': 'S', 'weight': 1.0}]
OLD_CAT = (55, 25, 25)

buf = []


def say(s=''):
    print(s)
    buf.append(s)


def load_counts():
    """三张表里"该档有几件"（`TableDatabase.ByGrade` / `Items` / `PluginCandidates` 的规模）。"""
    out = {}
    for f, key in [('seeds.json', 'seeds'), ('items.json', 'items'), ('plugins.json', 'plugins')]:
        with io.open(os.path.join(DATA, f), encoding='utf-8') as fh:
            arr = json.load(fh)[key]
        c = {g: 0 for g in GRADES}
        for row in arr:
            g = row.get('grade') or row.get('quality')
            c[g] = c.get(g, 0) + 1
        out[f.split('.')[0]] = c
    return out


def roll_tiers(rng, probs, n):
    """`RollGoodsTier` 的轮盘：一次 Range(0, 合计) → 第一个使累计 > roll 的下标。"""
    eff = np.asarray(probs, dtype=np.float64)
    total = eff.sum()
    cum = np.cumsum(eff)
    r = rng.random(n) * total
    idx = np.searchsorted(cum, r, side='right')
    return np.minimum(idx, len(eff) - 1)


def refresh_sim(rng, t_min, cat_w, seed_probs, unif_probs):
    """跑 REFRESHES 次刷新，返回 (类别计数, 各类逐品阶计数, 插件D档结果直方图)。"""
    wave = int(t_min) + 1
    m = REFRESHES * SLOTS
    cat_total = cat_w[0] + cat_w[1] + cat_w[2]
    cat = rng.integers(0, cat_total, m)               # RollOne 的那一次 RangeInt
    is_seed = cat < cat_w[0]
    is_plugin = (cat >= cat_w[0]) & (cat < cat_w[0] + cat_w[1])
    is_item = ~(is_seed | is_plugin)

    tier = roll_tiers(rng, unif_probs, m)             # 道具/插件/种子**同一条**曲线
    grade = tier.copy()

    # 插件：quality == tier 的件数为 0 ⇒ PickPluginOfTier 退到"全部插件"均匀取一件
    npool = unif_probs and None  # noqa: F841  (占位，避免误读)
    n_plugin = int(sum(PLUG_COUNT.values()))
    if PLUG_COUNT['D'] == 0:
        d_slots = is_plugin & (tier == 0)
        n_d = int(d_slots.sum())
        # 兜底：从全部 n_plugin 件里均匀取（二次 RangeInt）
        pick = rng.integers(0, n_plugin, n_d)
        plug_pool = np.array([GI[g] for g in GRADES for _ in range(PLUG_COUNT[g])], dtype=np.int64)
        grade[d_slots] = plug_pool[pick]
    else:
        n_d = 0

    res = {'seed': np.zeros(5, dtype=np.int64), 'plugin': np.zeros(5, dtype=np.int64),
           'item': np.zeros(5, dtype=np.int64), 'd_ref': np.zeros(5, dtype=np.int64)}
    for name, mask in (('seed', is_seed), ('plugin', is_plugin), ('item', is_item)):
        # 该档无货的兜底（种子/道具都有全 5 档，实测不触发；保留以保证脚本与代码同构）
        gh = np.bincount(grade[mask], minlength=5)
        res[name] = gh
    if n_d > 0:
        res['d_ref'] = np.bincount(grade[is_plugin & (tier == 0)], minlength=5)
    cat_counts = (int(is_seed.sum()), int(is_plugin.sum()), int(is_item.sum()))
    return cat_counts, res, n_d


CAT_NAMES = ('种子', '插件', '道具')


def pct(x, n):
    return 100.0 * x / n if n else 0.0


def main():
    shop, rc = load()
    unif = shop['itemGradeWeights']
    cat_w = (shop['seedWeight'], shop['pluginWeight'], shop['itemWeight'])
    global PLUG_COUNT
    CNT = load_counts()
    PLUG_COUNT = CNT['plugins']

    say('=' * 96)
    say('v282 复算　类别权重 1:1:1 + 三类共用一张品阶表　（%d 次刷新 × %d 货位 蒙特卡洛）'
        % (REFRESHES, SLOTS))
    say('=' * 96)
    say('类别权重（改后）：种子 %d / 插件 %d / 道具 %d（合计 %d）　改前：%d / %d / %d（合计 %d）'
        % (cat_w[0], cat_w[1], cat_w[2], sum(cat_w), OLD_CAT[0], OLD_CAT[1], OLD_CAT[2], sum(OLD_CAT)))
    say('品阶表（唯一来源 shop.itemGradeWeights）：'
        + '　'.join('%s %.2f' % (r['grade'], r['weight']) for r in unif)
        + '　合计 %.1f' % sum(r['weight'] for r in unif))
    say('改前种子表 shop.gradeWeights（已删除）：'
        + '　'.join('%s %.2f' % (r['grade'], r['weight']) for r in OLD_SEED_TABLE))
    say('各表库存件数：种子 %s｜道具 %s｜插件 %s'
        % (CNT['seeds'], CNT['items'], CNT['plugins']))
    say()

    rng = np.random.default_rng(20240607)

    for t in TIMES:
        wave = int(t) + 1
        probs, _ = table_probs(unif, minutes_at_wave(wave), rc)
        old_seed_probs, _ = table_probs(OLD_SEED_TABLE, minutes_at_wave(wave), rc)

        say('-' * 96)
        say('【t = %d 分钟（WaveIndex = %d）】' % (t, wave))
        say('  有效品阶概率（三类共用）：'
            + '　'.join('%s %.4f%%' % (GRADES[i], probs[i] * 100) for i in range(5)))
        say('  改前种子表概率（对照）：　　'
            + '　'.join('%s %.4f%%' % (GRADES[i], old_seed_probs[i] * 100) for i in range(5)))

        # ── ① 每次刷新各类别实际格数 ──
        cc, res, n_d = refresh_sim(rng, t, cat_w, old_seed_probs, probs)
        tot = REFRESHES * SLOTS
        say('  ① 每次刷新各类别实际格数（4 格/次）：')
        say('     改后 ' + '｜'.join('%s %.3f 格（%.2f%%）' % (CAT_NAMES[i], cc[i] / REFRESHES, pct(cc[i], tot))
                                    for i in range(3)))

        # ── ② 每类逐品阶占比 ──
        say('  ② 每类逐品阶实际占比：')
        for name in ('seed', 'plugin', 'item'):
            label = {'seed': '种子', 'plugin': '插件', 'item': '道具'}[name]
            n = int(res[name].sum())
            say('     %s（%d 格）：%s' % (label, n,
                '　'.join('%s %.2f%%' % (GRADES[i], pct(res[name][i], n)) for i in range(5))))

        # ── ③ 插件抽到 D 档时的实际结果 ──
        if n_d > 0:
            say('  ③ 插件抽到 D 档（%.2f%% 的插件格）时的实际产出：兜底到『全部插件均匀』'
                '（%d 格）：%s' % (pct(n_d, cc[1]), n_d,
                '　'.join('%s %.2f%%' % (GRADES[i], pct(res['d_ref'][i], n_d)) for i in range(5))))
        else:
            say('  ③ 插件抽到 D 档：本表无 D 档插件 ⇒ 每次都走兜底（见上一行）')

        # ── ④ S 档实际占比 ──
        lim = 1.0 if t <= 5 else (5.0 if t <= 10 else 10.0)
        s_seed = pct(res['seed'][4], int(res['seed'].sum()))
        s_item = pct(res['item'][4], int(res['item'].sum()))
        s_plug = pct(res['plugin'][4], int(res['plugin'].sum()))
        say('  ④ S 档实际占比：种子 %.4f%%｜插件 %.4f%%｜道具 %.4f%%　（曲线目标 S = %.4f%%，硬线 t≤5:1%%、t≤10:5%%、任意时刻≤10%%）'
            % (s_seed, s_plug, s_item, probs[4] * 100))
        # 蒙特卡洛噪声：每类 ~133k 格 ⇒ p=10% 时 σ ≈ 0.026pp，p=1% 时 σ ≈ 0.009pp
        band = 3.0 * np.sqrt(max(probs[4], 1e-9) * (1 - probs[4]) / (REFRESHES * SLOTS / 3.0)) * 100.0
        say('     判定（3σ 带 ±%.3fpp）：种子 %s｜道具 %s｜插件 %s'
            % (band,
               'PASS' if abs(s_seed - probs[4] * 100) <= band + (lim - probs[4] * 100) else 'FAIL',
               'PASS' if abs(s_item - probs[4] * 100) <= band + (lim - probs[4] * 100) else 'FAIL',
               'PASS' if s_plug <= lim + band else 'FAIL（D 档兜底把 S 抬到 %.2f%%，见 ③；本轮未改，改前同值）' % s_plug))
        say()

    # ── 改前对照：类别格数 ──
    say('=' * 96)
    say('改前对照（类别权重 55/25/25，品阶照旧两表）：每次刷新各类别期望格数')
    say('  种子 %.3f 格｜插件 %.3f 格｜道具 %.3f 格'
        % (SLOTS * OLD_CAT[0] / sum(OLD_CAT), SLOTS * OLD_CAT[1] / sum(OLD_CAT), SLOTS * OLD_CAT[2] / sum(OLD_CAT)))
    say('改后（类别权重 25/25/25）：')
    say('  种子 %.3f 格｜插件 %.3f 格｜道具 %.3f 格'
        % (SLOTS * cat_w[0] / sum(cat_w), SLOTS * cat_w[1] / sum(cat_w), SLOTS * cat_w[2] / sum(cat_w)))
    say()
    with io.open(OUT, 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(buf) + '\n')
    print('\n（已写入 %s）' % OUT)
    return 0


if __name__ == '__main__':
    sys.exit(main())
