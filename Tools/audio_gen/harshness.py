# -*- coding: utf-8 -*-
"""harshness.py -- measure *why* a one-shot sounds harsh, from the WAVs on disk.

"I can't hear it, so 'harsh' has to be a number."  Every cause below is
measurable and each has a threshold:

  1. too much energy where the ear is most sensitive
     `2-5k %` / `>2k %` = share of the **energy** (|X|^2) in that band.
     > 25 % in 2-5 kHz reads as bright/harsh on small speakers.
  2. a narrow-band component (a lone barely-detuned sine that whistles)
     `spike` = how far the loudest bin rises above the median of its
     +/-110 Hz neighbourhood.  >= 12 dB is a tone standing out of texture.
  3. too steep an onset (click-like)
     `attack` = 1 ms-envelope rise time from 1 % to 90 % of that envelope's
     own maximum, in ms.  < 1 ms is a click, 3-8 ms is a hit.

Also reported: magnitude-weighted and energy-weighted spectral centroid,
crest factor, and how many samples sit within 1 dB of the peak (a one-sample
click shows up as 1 sample).

Read-only: nothing is regenerated, every number is decoded from the file.

    python harshness.py                       # table for every sfx
    python harshness.py --only sfx_water ...  # just these stems
    python harshness.py --json out.json       # machine readable (with sha256)
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
SFX_DIR = os.path.join(PROJECT, "Assets", "Resources", "Audio", "Sfx")
MUSIC_DIR = os.path.join(PROJECT, "Assets", "Resources", "Audio", "Music")
I16 = 32768.0


def load(path):
    with wave.open(path, "rb") as wf:
        ch, sr = wf.getnchannels(), wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    x = np.frombuffer(raw, dtype="<i2").astype(np.float64) / I16
    return x.reshape(-1, ch), sr


def db(v):
    return 20.0 * math.log10(max(float(v), 1e-15))


def lufs_ungated(x, sr):
    """Same measure verify_audio.py uses for one-shots (imported to stay in sync)."""
    sys.path.insert(0, HERE)
    from verify_audio import lufs_of
    return lufs_of(x, sr, gated=False)


def spectrum(x):
    m = x.mean(axis=1)
    n = 1 << int(math.ceil(math.log2(max(m.size, 2))))
    X = np.fft.rfft(m * np.hanning(m.size), n)
    return np.fft.rfftfreq(n, 1.0 / 44100.0), np.abs(X)


def band_share(freqs, mag, lo, hi):
    p = mag * mag
    tot = float(np.sum(p[(freqs >= 20.0) & (freqs <= 20000.0)]))
    sel = (freqs >= lo) & (freqs < hi)
    return float(np.sum(p[sel])) / tot if tot > 0 else 0.0


def centroids(freqs, mag):
    sel = (freqs >= 20.0) & (freqs <= 20000.0)
    f, a = freqs[sel], mag[sel]
    s = float(np.sum(a))
    c_mag = float(np.sum(f * a) / s) if s > 0 else 0.0
    p = a * a
    sp = float(np.sum(p))
    c_pow = math.sqrt(float(np.sum(f * f * p) / sp)) if sp > 0 else 0.0
    return c_mag, c_pow


def tone(x, sr, frame=2048, lo=800.0, hi=8000.0, half_bins=8):
    """A **sustained** narrow peak, i.e. a real whistle -- measured on a frame-averaged
    spectrum.

    ★ 为什么不能用"整段文件的单次 FFT"判"单频哨音"（实测教训）★
    第一版就是那样做的，结果**水族 5 条全被标成 +12~+16 dB 的"尖峰"**，追到频率上
    发现连不同信号都停在同一个 bin、周围 bin 也在同一电平 ⇒ 那不是音，是**噪声的次序统计量**：
    一条近似平谱的噪声，±110 Hz 内 80 个 bin 的最大值本来就会比中位数高 11~13 dB。
    整段 FFT 无法把"随机的最大值"与"固定的音"分开 ✗

    ★ 能做开的是什么 ★
    把信号切成 46 ms 的帧（2048 点，Hann），**逐帧算幅度谱再平均**：
      · 随机噪声的峰在帧间**换位置**，平均之后被压平（峰/邻域比掉到 ~7~9 dB）
      · 真正的单频分量在每一帧都落在**同一个 bin**，平均之后峰**原样保留**
    ⇒ 平均谱上 >= 12 dB 的孤峰才真的能听成"哨"✓ 这是判"单频哨音"该用的口径 ✓
    """
    m = x.mean(axis=1)
    if m.size < frame:
        # ★ 既有缺陷（2026-10-17 跑全表时才暴露，不是本次改动引入的）★
        # 原来这里用 `ceil`：1852 样本的 `sfx_ui_hover`（0.042 s）→ ceil(log2)=11 → frame 仍是 2048，
        # 比文件还长 ⇒ `m[i:i+frame] * hanning(frame)` 广播失败 ⇒ 整条检查以 exit 1 收场 ✗
        # 改成 `floor` 才真的是"把窗口缩到塞得下"；判定口径（帧长、步长、半窗 bin 数）一个字没动 ✓
        frame = 1 << int(math.floor(math.log2(max(m.size, 2))))
    frame = max(2, min(frame, m.size))
    step = frame // 2
    acc = None
    n = 0
    for i in range(0, max(m.size - frame, 0) + 1, step):
        seg = m[i:i + frame] * np.hanning(frame)
        s = np.abs(np.fft.rfft(seg))
        acc = s if acc is None else acc + s
        n += 1
    if acc is None or n == 0:
        return 0.0, 0.0
    acc /= float(n)
    f = np.fft.rfftfreq(frame, 1.0 / sr)
    sel = (f >= lo) & (f <= hi)
    ff, pp = f[sel], acc[sel] / max(float(np.max(acc)), 1e-15)
    if pp.size < 4 * half_bins + 4:
        return 0.0, 0.0
    k = 2 * half_bins + 1
    win = np.lib.stride_tricks.sliding_window_view(np.pad(pp, half_bins, mode="edge"), k)
    med = np.median(win, axis=1)
    rel = 20.0 * np.log10(np.maximum(pp, 1e-12) / np.maximum(med, 1e-12))
    i = int(np.argmax(rel))
    return float(rel[i]), float(ff[i])


def spike(freqs, mag, half_hz=110.0, lo=150.0, hi=18000.0, floor_db=-45.0):
    """Loudest bin vs the median of its neighbourhood, whole-file FFT (report only).

    ★ 这个口径**分不开"音"与"噪声的最大值"**（实测见 `tone()` 的说明），
      所以它只作为旁证留在 JSON 里，判定用 `tone()`（帧平均谱）✓
    """
    sel = (freqs >= lo) & (freqs <= hi)
    f = freqs[sel]
    p = mag[sel] / max(float(np.max(mag)), 1e-15)
    if p.size < 16:
        return 0.0, 0.0
    df = float(f[1] - f[0])
    half = max(1, int(round(half_hz / df)))
    k = 2 * half + 1
    win = np.lib.stride_tricks.sliding_window_view(np.pad(p, half, mode="edge"), k)
    med = np.median(win, axis=1)
    rel = 20.0 * np.log10(np.maximum(p, 1e-12) / np.maximum(med, 1e-12))
    audible = p >= 10.0 ** (floor_db / 20.0)
    if not np.any(audible):
        return 0.0, 0.0
    rel = np.where(audible, rel, -1e9)
    i = int(np.argmax(rel))
    return float(rel[i]), float(f[i])


def attack_ms(x, sr, window_ms=12.0):
    """How long until the FIRST transient is at full level: 10 % -> 90 % of the
    envelope maximum inside the first `window_ms`, in ms.

    ★ 三个口径都试过，为什么留这一个（实测）★
      ① 1%->90% 的**全段**峰值：`sfx_water` 是"泼水 + 咕嘟"两下、第二下更响
         ⇒ 读数一路跨到 0.10 s，报 **102 ms** —— 那不是起音，是"第二声何时到" ✗
      ② 找"第一个局部峰"再量它自己的 10%->90%：会被包络抖动抓住，报 1.2 ms
         —— 比母带 3 ms 淡入还陡，物理上不可能 ⇒ 抓错了峰 ✗
      ③ 现在这个：**窗口 12 ms 内的包络峰值**当"满档"，量 10%->90%
         ⇒ 3 ms 线性淡入在它上面读出 ~2.4 ms；起音比淡入慢的那几条（自充 8 ms 蓄能、
            多柱的水膜/混响尾巴在 20 ms 处还有一个小鼓包）也只在**第一记**上量 ✓
    所以这个数同时是"起音"和"3 ms 淡入有没有被瞬态顶穿"的检查 ✓
    """
    env = np.abs(x).mean(axis=1)
    w = max(1, int(0.001 * sr))
    env = np.convolve(env, np.ones(w) / w, mode="same")
    n = min(env.size, int(window_ms * 0.001 * sr))
    if n < 8:
        return 0.0
    seg = env[:n]
    top = float(np.max(seg))
    if top <= 0.0:
        return 0.0
    i10 = int(np.argmax(seg >= 0.10 * top))
    i90 = int(np.argmax(seg >= 0.90 * top))
    if seg[i90] < 0.90 * top:
        return 0.0
    return (i90 - i10) / float(sr) * 1000.0


def analyse(path):
    x, sr = load(path)
    freqs, mag = spectrum(x)
    sha = hashlib.sha256(open(path, "rb").read()).hexdigest()
    c_mag, c_pow = centroids(freqs, mag)
    peak = float(np.max(np.abs(x)))
    rms = float(np.sqrt(np.mean(x ** 2)))
    near = int(np.sum(np.abs(x) >= peak * 10 ** (-1.0 / 20.0)))
    r = dict(
        name=os.path.basename(path)[:-4],
        path=path,
        bytes=os.path.getsize(path),
        sha256=sha,
        seconds=x.shape[0] / float(sr),
        peak_db=db(peak),
        lufs=lufs_ungated(x, sr),
        centroid_hz=c_mag,
        centroid_energy_hz=c_pow,
        band_2_5k=band_share(freqs, mag, 2000.0, 5000.0),
        band_above_2k=band_share(freqs, mag, 2000.0, 20000.0),
        band_200_1500=band_share(freqs, mag, 200.0, 1500.0),
        band_above_5k=band_share(freqs, mag, 5000.0, 20000.0),
        attack_ms=attack_ms(x, sr),
        crest_db=db(peak) - db(rms),
        peak_samples_1db=near,
    )
    r["spike_db"], r["spike_hz"] = spike(freqs, mag)
    # 帧平均谱上的孤峰 = 真正的单频哨音（见 tone()；整段单次 FFT 分不开这个）
    r["tone_db"], r["tone_hz"] = tone(x, sr)
    return r


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--json", default=None)
    ap.add_argument("--all", action="store_true", help="include the music beds")
    ap.add_argument("--dir", default=None,
                    help="analyse this folder instead (e.g. a backup of the previous "
                         "generation) so before/after use the identical code path")
    args = ap.parse_args(argv)

    dirs = [args.dir] if args.dir else ([SFX_DIR] + ([MUSIC_DIR] if args.all else []))
    rows = []
    for d in dirs:
        for nm in sorted(f for f in os.listdir(d) if f.lower().endswith(".wav")):
            if args.only and nm[:-4] not in args.only:
                continue
            rows.append(analyse(os.path.join(d, nm)))

    head = ("%-18s %7s %7s %8s %9s %8s %8s %8s %8s %9s %9s"
            % ("file", "dur(s)", "LUFS", "peak", "centroid", "cen(E)",
               "2-5k%", ">2k%", ">5k%", "tone", "attack"))
    print(head)
    print("-" * len(head))
    for r in rows:
        flag = ""
        if r["band_2_5k"] > 0.25:
            flag += " H2-5K"
        if r["tone_db"] >= 12.0:
            flag += " WHISTLE"
        if r["attack_ms"] < 1.0:
            flag += " CLICK"
        print("%-18s %7.3f %+7.2f %+8.2f %9.0f %8.0f %7.1f%% %7.1f%% %7.1f%% %+5.1f@%5.0f %6.2fms%s"
              % (r["name"], r["seconds"], r["lufs"], r["peak_db"], r["centroid_hz"],
                 r["centroid_energy_hz"], r["band_2_5k"] * 100.0,
                 r["band_above_2k"] * 100.0, r["band_above_5k"] * 100.0,
                 r["tone_db"], r["tone_hz"], r["attack_ms"], flag))
    print("")
    print("thresholds: 2-5k%% <= 25   tone < +12 dB   attack 3-8 ms   "
          "centroid(energy) 200-1500 Hz for water")
    print("tone = isolated peak on the 46 ms FRAME-AVERAGED spectrum, dB above the median")
    print("       of +/-370 Hz; a single whole-file FFT cannot tell a tone from the max")
    print("       of 80 noise bins (that reads +11..+13 dB on pure noise -- measured).")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(rows, f, indent=1)
        print("json -> %s" % args.json)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
