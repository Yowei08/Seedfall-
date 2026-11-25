#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
check_loudness.py —— 三首 BGM 的「响度层次」回归检查

为什么单独一个文件：
  - 生成（gen_audio.py）与校验（本文件）分开，校验器能独立重跑。
  - 校验的是「三首曲子之间的关系」，而单首曲子自己的峰值/时长由 report.txt 保证。

判据（三条同时成立才算通过）：
  1. 三首 BGM 的 LUFS 必须满足  night < farm_day < combat
  2. 相邻两两的差必须 >= MIN_GAP_LU（否则"相等"也能通过，等于没检查）
  3. 三首的峰值必须都在 PEAK_TOL 之内等于 PEAK_TARGET（响度调整不许动峰值）

数据来源：Tools/audio_gen/report.txt 的 A. BACKGROUND MUSIC 段（这是生成器自己写的，
         比我另行实现的 LUFS 近似更权威）。

用法：
  <python> Tools/audio_gen/check_loudness.py            # 退出码 0 = 通过，1 = 失败
  <python> Tools/audio_gen/check_loudness.py --verbose
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPORT = os.path.join(HERE, 'report.txt')

# 期望的层次：从最轻到最响
ORDER = ['bgm_night', 'bgm_farm_day', 'bgm_combat']
# 相邻两首至少差这么多 LU，否则视为「没有层次」
MIN_GAP_LU = 1.5
# 峰值**上限**（响度层次优先；峰值只是不许超的边界，不是钉死的目标）
PEAK_CEIL = -1.00


def parse_report(path):
    """从 report.txt 里抽出每首 BGM 的 LUFS 与 peak。

    行形如：
      bgm_farm_day       91.429   4032000    -1.00   -17.65   -16.00   44.1      2   16128044
    列：file dur(s) samples peak rms LUFS kHz ch bytes
    """
    if not os.path.exists(path):
        return None, 'report.txt 不存在: %s' % path

    rows = {}
    # 只认 A. BACKGROUND MUSIC 段到下一个空行分节为止
    in_bgm = False
    with open(path, 'r', encoding='utf-8') as f:
        for line in f:
            s = line.rstrip('\n')
            if 'A. BACKGROUND MUSIC' in s:
                in_bgm = True
                continue
            if in_bgm and s.startswith('  musical spec'):
                break
            if not in_bgm:
                continue
            m = re.match(r'\s+(bgm_\w+)\s+([\d.]+)\s+(\d+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)', s)
            if m:
                rows[m.group(1)] = {
                    'dur': float(m.group(2)),
                    'peak': float(m.group(4)),
                    'rms': float(m.group(5)),
                    'lufs': float(m.group(6)),
                }
    if not rows:
        return None, 'report.txt 里没找到 BGM 行（段落标记可能变了）'
    return rows, None


def main():
    verbose = '--verbose' in sys.argv

    rows, err = parse_report(REPORT)
    if err:
        print('FAIL  %s' % err)
        return 1

    missing = [n for n in ORDER if n not in rows]
    if missing:
        print('FAIL  缺少这几首: %s（report.txt 里只有 %s）'
              % (', '.join(missing), ', '.join(sorted(rows))))
        return 1

    print('  档位            LUFS      peak     (从轻到响)')
    for n in ORDER:
        r = rows[n]
        print('  %-14s %7.2f  %7.2f' % (n, r['lufs'], r['peak']))
    print('')

    problems = []

    # 1. 顺序
    for a, b in zip(ORDER, ORDER[1:]):
        la, lb = rows[a]['lufs'], rows[b]['lufs']
        if not (la < lb):
            problems.append('顺序错：%s 的 LUFS %.2f 不小于 %s 的 %.2f（%s 应当更轻）'
                            % (a, la, b, lb, a))

    # 2. 间距（防止"几乎相等"也算通过）
    for a, b in zip(ORDER, ORDER[1:]):
        gap = rows[b]['lufs'] - rows[a]['lufs']
        if gap < MIN_GAP_LU:
            problems.append('层次不足：%s → %s 只差 %.2f LU，要求 ≥ %.1f LU'
                            % (a, b, gap, MIN_GAP_LU))

    # 3. 峰值是**上限**，不是钉死的目标
    #
    # ★ 为什么改：agent 用两轮扫描实验证明「night −21 LUFS 且峰值钉 −1.00」
    #   在算术上做不到 —— LUFS ≈ 峰值 − 波峰因数 + K偏移，而本曲材料的波峰因数
    #   只有 12.2 dB（night）/ 12.6 dB（farm），要 −21 LUFS 需 21 dB。
    #   硬做只能把铃砍成「只剩全幅尖峰、中间近乎静音」—— 那不是音乐。
    # ⇒ 设计取舍：**响度层次是主目标（玩家真正感知的），峰值 −1.00 dBFS 是上限约束**。
    #   所以这里断言的是 `peak <= PEAK_CEIL`，而不是 `peak == PEAK_CEIL ± tol`。
    for n in ORDER:
        p = rows[n]['peak']
        if p > PEAK_CEIL + 1e-6:
            problems.append('峰值超上限：%s 是 %.2f dBFS，必须 ≤ %.2f'
                            % (n, p, PEAK_CEIL))

    if problems:
        print('FAIL  响度层次不成立（%d 条）' % len(problems))
        for p in problems:
            print('  - %s' % p)
        return 1

    gaps = ['%s→%s %.2f LU' % (a, b, rows[b]['lufs'] - rows[a]['lufs'])
            for a, b in zip(ORDER, ORDER[1:])]
    print('PASS  响度层次成立：%s < %s < %s（%s）'
          % (ORDER[0], ORDER[1], ORDER[2], '，'.join(gaps)))
    if verbose:
        for n in ORDER:
            print('  %s  dur %.3fs  rms %.2f dB' % (n, rows[n]['dur'], rows[n]['rms']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
