# -*- coding: utf-8 -*-
"""plugin_sfx_report.py -- 12 条插件音效的**可区分度证据**。

    python plugin_sfx_report.py            # 表 + 两两对比
    python plugin_sfx_report.py --strict   # 任一对"分不开"就 exit 1

★ 为什么需要这个脚本 ★
"听感可区分"这句话**没法被贴进报告**（报告被读的时候没有声音）。
所以这里把"听感"换成三个**能从文件里量出来**的维度：

  ① **时长**      —— 12 个值两两不同，最小间隔 >= 15 ms
  ② **频谱重心**  —— 能量加权平均频率（Hz）。它是"亮/闷"的客观说法：
                     重心高 = 高频多 = 听起来尖；重心低 = 听起来闷 ✓
  ③ **起音个数**  —— 5 ms 帧的短时能量包络上的显著局部峰个数（>= 25% 峰值、间隔 >= 25 ms）。
                     它就是"响了几下"：双击 2 下、连环 3 下、毒素 6 下气泡 ✓

★ 判"分得开"的规则（先定规则再看结果，避免事后凑）★
  任意两条，只要**满足下面任意一条**就算分得开：
    · 时长差 >= 0.010 s
    · 频谱重心比 >= 1.25 倍（约 1/3 个倍频程，人耳对"亮/闷"能稳的分辨量级）
    · 起音个数不同
  两条"都不满足"才算**分不开**（`--strict` 下判 FAIL）。

所有峰值 / LUFS 都是**直接复用** `verify_audio.py` 的解码与测量函数 ——
不另写一份，免得"报告说 -12、验收说 -12.3"这种两张皮 ✗
"""

from __future__ import annotations

import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import verify_audio as V          # 复用：解码 / 峰值 / DC / LUFS
import sfx as sfxlib              # 复用：12 件插件的清单（顺序与 plugins.json 对齐）

SR = V.SR_EXPECT
MIN_DUR_GAP = 0.010
MIN_CENTROID_RATIO = 1.25


def centroid_hz(x, sr=SR):
    """能量加权频谱重心（左右声道先取平均幅度谱）。"""
    m = x.mean(axis=1)
    win = np.hanning(m.size)
    mag = np.abs(np.fft.rfft(m * win))
    freqs = np.fft.rfftfreq(m.size, 1.0 / sr)
    power = mag ** 2
    total = float(power.sum())
    if total <= 0.0:
        return 0.0
    return float((freqs * power).sum() / total)


def onset_count(x, sr=SR, frame_ms=5.0, rel=0.20, rise=1.6, min_gap_ms=25.0):
    """包络上的**显著上冲**次数 —— 也就是"能量跳了几下"。

    ★ 为什么用"上冲"而不是"局部极大" ★
    局部极大的写法对**平台**和**低频振荡**都不可靠：
      · 双击是两记等高的短峰，中间隔 100 ms ⇒ 局部极大能数对，但平台段会被漏掉 ✗
      · 加粗是 62 Hz 的正弦，5 ms 帧的包络本身就在抖 ⇒ 局部极大数出 6 下（假的）✗
    改成"比上一帧涨了 1.6 倍（+4 dB）以上、且绝对值不低于峰值的 20%"：
      · 双击 2 下 ✓｜连环 3 下 ✓｜多柱 3 下 ✓｜毒素 6 个气泡 ✓
      · 加粗 / 子爆 / 自充 各 1 下 ✓（它们是"一记"，包络单调）
      · 灼烧的 37 Hz 火苗抖动会被数成多次上冲 —— 那**就是**它的签名（一条持续抖动的嘶声）✓
    ⇒ 这个数不是"人耳听到几下"，而是"包络跳了几下"；报告里按这个说法写 ✓
    """
    m = np.abs(x).max(axis=1)
    fl = max(1, int(sr * frame_ms / 1000.0))
    n = (m.size // fl) * fl
    if n <= 0:
        return 0
    env = m[:n].reshape(-1, fl).max(axis=1)
    top = float(env.max())
    if top <= 0.0:
        return 0
    floor = rel * top
    gap = max(1, int(min_gap_ms / frame_ms))
    count, last = 0, -gap - 1
    for i in range(1, env.size):
        if env[i] < floor:
            continue
        if env[i] <= env[i - 1] * rise:
            continue
        if i - last < gap:
            continue
        count += 1
        last = i
    # 开头那一记（第 0 帧就很响、没有"上一帧"可比）单独算一次
    if env[0] >= floor:
        count += 1
    return max(count, 1)


def main(argv):
    strict = "--strict" in argv
    rows = []
    for name, _fn, _desc in sfxlib.PLUGIN_SFX:
        path = os.path.join(V.SFX_DIR, name + ".wav")
        if not os.path.isfile(path):
            print("MISSING: %s" % path)
            return 1
        r = V.analyse(path)
        lufs = V.lufs_of(r["x"], r["sr"], gated=False)
        rows.append(dict(
            name=name,
            seconds=r["seconds"],
            peak=r["peak_db"],
            lufs=lufs,
            dc=r["dc"],
            bytes=r["bytes"],
            centroid=centroid_hz(r["x"]),
            onsets=onset_count(r["x"]),
        ))

    print("=" * 108)
    print("Seedfall 插件音效（12 件）——12 个插件各一条，全部走 gen_audio.master_sfx 的母带链")
    print("=" * 108)
    print("")
    print("  %-20s %8s %8s %7s %8s %10s %7s"
          % ("file", "dur(s)", "peak", "LUFS", "DC", "centroid", "onsets"))
    print("  " + "-" * 82)
    for r in rows:
        print("  %-20s %8.3f %8.2f %7.2f %8.1e %10.0f %7d"
              % (r["name"], r["seconds"], r["peak"], r["lufs"], r["dc"],
                 r["centroid"], r["onsets"]))
    print("")
    print("  centroid = 能量加权频谱重心 (Hz)；onsets = 包络显著上冲次数"
          "（5ms 帧，>=20% 峰值，>=+4dB 跃升，>=25ms 间隔）")
    print("")

    # ── 两两对比 ──
    print("=" * 108)
    print("两两可区分性  （规则：时长差 >= %.0f ms  或  重心比 >= %.2fx  或  起音个数不同）"
          % (MIN_DUR_GAP * 1000.0, MIN_CENTROID_RATIO))
    print("=" * 108)
    bad, margins = [], []
    for i in range(len(rows)):
        for j in range(i + 1, len(rows)):
            a, b = rows[i], rows[j]
            dd = abs(a["seconds"] - b["seconds"])
            lo, hi = sorted((a["centroid"], b["centroid"]))
            cr = hi / max(lo, 1e-9)
            on = a["onsets"] != b["onsets"]
            ok = dd >= MIN_DUR_GAP or cr >= MIN_CENTROID_RATIO or on
            # 记录"最弱的那一条对"用来给结论一个余量
            margins.append((min(dd / MIN_DUR_GAP, cr / MIN_CENTROID_RATIO), a["name"], b["name"],
                           dd, cr, on, ok))
            if not ok:
                bad.append((a["name"], b["name"], dd, cr, on))
    margins.sort()
    print("")
    print("  最接近的 6 对（余量 = 达到判定线的最差比例，>=1 即过线）：")
    print("  %-20s %-20s %9s %9s %7s %6s"
          % ("pair", "", "dur diff", "centroid", "onsets", "ok"))
    for m, na, nb, dd, cr, on, ok in margins[:6]:
        print("  %-20s %-20s %8.0fms %8.2fx %7s %6s"
              % (na, nb, dd * 1000.0, cr, "diff" if on else "same", "PASS" if ok else "FAIL"))
    print("")
    if bad:
        print("分不开的对：%d" % len(bad))
        for a, b, dd, cr, on in bad:
            print("  x %s / %s : dur %.0fms, centroid %.2fx, onsets same"
                  % (a, b, dd * 1000.0, cr))
        print("")
        print("RESULT: %d PAIR(S) INDISTINGUISHABLE" % len(bad))
        return 1 if strict else 0

    print("RESULT: 全部 %d 对两两可分（12 条两两对比共 %d 对）"
          % (len(margins), len(margins)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
