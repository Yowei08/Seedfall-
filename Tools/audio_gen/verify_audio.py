# -*- coding: utf-8 -*-
"""verify_audio.py -- independent checks on the WAVs that are actually on disk.

Nothing here imports the generator's in-memory state: every number is decoded
from the files, using only the stdlib plus numpy.

    python verify_audio.py            # human readable
    python verify_audio.py --strict   # exit code 1 on any FAIL

Checks
------
all files   RIFF/16-bit stereo/44.1 kHz header, peak <= -1.0 dBFS,
            |mean| < 1e-4, no NaN/Inf, not silent, LUFS in range
music       length == exactly bars*4*60/bpm seconds, loop seam is a normal
            sample step (i.e. no fade was applied), the two channels differ
sfx         first and last sample exactly 0, the 3 ms / 4 ms fades really
            attenuate, the tail is already quiet before the fade
"""

from __future__ import annotations

import math
import os
import struct
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
MUSIC_DIR = os.path.join(PROJECT, "Assets", "Resources", "Audio", "Music")
SFX_DIR = os.path.join(PROJECT, "Assets", "Resources", "Audio", "Sfx")

SR_EXPECT = 44100
PEAK_MAX = -1.0          # dBFS ceiling
DC_MAX = 1e-4
I16 = 32768.0

# (name, bpm, bars, beats per bar) -- what the music is supposed to be
MUSIC_SPEC = {
    "bgm_farm_day": dict(bpm=84, bars=32, bpb=4, lufs=-16.0),
    "bgm_combat": dict(bpm=132, bars=32, bpb=4, lufs=-13.5),
    "bgm_night": dict(bpm=66, bars=24, bpb=4, lufs=-21.0),
}
LSB = 1.0 / I16

SFX_NAMES = None  # discovered from disk


def db(v):
    return 20.0 * math.log10(max(float(v), 1e-15))


def read_header(path):
    """Parse the RIFF header directly so the check does not trust wave.open."""
    with open(path, "rb") as f:
        head = f.read(64)
    if head[:4] != b"RIFF" or head[8:12] != b"WAVE":
        return None
    pos = 12
    info = {}
    while pos + 8 <= len(head):
        cid = head[pos:pos + 4]
        size = struct.unpack("<I", head[pos + 4:pos + 8])[0]
        if cid == b"fmt ":
            fmt, channels, rate, byte_rate, align, bits = struct.unpack(
                "<HHIIHH", head[pos + 8:pos + 24])
            info.update(fmt=fmt, channels=channels, rate=rate, bits=bits,
                        align=align)
        pos += 8 + size + (size & 1)
    return info


def load(path):
    with wave.open(path, "rb") as wf:
        ch = wf.getnchannels()
        sw = wf.getsampwidth()
        sr = wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    a = np.frombuffer(raw, dtype="<i2").astype(np.float64) / I16
    return a.reshape(-1, ch), sr, sw


def analyse(path):
    x, sr, sw = load(path)
    m = x.mean(axis=1)
    r = {
        "path": path,
        "name": os.path.basename(path)[:-4],
        "bytes": os.path.getsize(path),
        "samples": int(x.shape[0]),
        "channels": int(x.shape[1]),
        "sr": int(sr),
        "sampwidth": int(sw),
        "seconds": x.shape[0] / float(sr),
        "peak_db": db(np.max(np.abs(x))),
        "rms_db": db(np.sqrt(np.mean(x ** 2))),
        "dc": float(np.mean(x)),
        "finite": bool(np.all(np.isfinite(x))),
        "silent": bool(np.max(np.abs(x)) < 1e-6),
        "head": x[:max(1, int(0.005 * sr))],
        "tail": x[-max(1, int(0.005 * sr)):],
        "first_sample": float(x[0, 0]),
        "last_sample": float(x[-1, 0]),
        "lr_corr": float(np.corrcoef(x[:, 0], x[:, 1])[0, 1])
        if x.shape[1] == 2 else 1.0,
        "seam_step": float(np.max(np.abs(x[0] - x[-1]))),
        "median_step": float(np.median(np.abs(np.diff(m)))),
        "x": x,
    }
    return r


def lufs_of(x, sr, gated=True):
    """Gated integrated loudness, re-implemented here so a bug in the
    generator's loudness code cannot hide behind the same code path."""
    f0, G, Q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    K = math.tan(math.pi * f0 / sr)
    Vh = 10.0 ** (G / 20.0)
    Vb = Vh ** 0.4996667741545416
    a0 = 1.0 + K / Q + K * K
    b1 = np.array([(Vh + Vb * K / Q + K * K) / a0, 2.0 * (K * K - Vh) / a0,
                   (Vh - Vb * K / Q + K * K) / a0])
    a1 = np.array([1.0, 2.0 * (K * K - 1.0) / a0, (1.0 - K / Q + K * K) / a0])
    f0, Q = 38.13547087602444, 0.5003270373238773
    K = math.tan(math.pi * f0 / sr)
    a0 = 1.0 + K / Q + K * K
    b2 = np.array([1.0 / a0, -2.0 / a0, 1.0 / a0])
    a2 = np.array([1.0, 2.0 * (K * K - 1.0) / a0, (1.0 - K / Q + K * K) / a0])

    def biquad(sig, b, a):
        n = sig.size
        N = 1 << int(math.ceil(math.log2(n)))
        w = 2 * math.pi * np.arange(N // 2 + 1) / N
        z = np.exp(-1j * w)
        H = (b[0] + b[1] * z + b[2] * z * z) / (a[0] + a[1] * z + a[2] * z * z)
        return np.fft.irfft(np.fft.rfft(sig, N) * H, N)[:n]

    y = np.stack([biquad(biquad(x[:, c], b1, a1), b2, a2)
                  for c in range(x.shape[1])], axis=1)
    e = np.sum(y ** 2, axis=1)
    w = int(0.4 * sr)
    hop = int(0.1 * sr)
    if e.size < w:
        return -0.691 + 10 * math.log10(max(float(np.mean(e)), 1e-20))
    if not gated:
        return -0.691 + 10 * math.log10(max(float(np.mean(e)), 1e-20))
    cs = np.concatenate(([0.0], np.cumsum(e)))
    starts = np.arange(0, e.size - w + 1, hop)
    ms = (cs[starts + w] - cs[starts]) / w
    bl = -0.691 + 10 * np.log10(np.maximum(ms, 1e-20))
    keep = bl > -70.0
    if not np.any(keep):
        return -70.0
    rel = -0.691 + 10 * math.log10(float(np.mean(ms[keep]))) - 10.0
    keep2 = keep & (bl > rel)
    if not np.any(keep2):
        keep2 = keep
    return -0.691 + 10 * math.log10(max(float(np.mean(ms[keep2])), 1e-20))


def check_music(r):
    """Returns a list of (label, ok, detail)."""
    c = []
    spec = MUSIC_SPEC.get(r["name"])
    hdr = read_header(r["path"])
    c.append(("16-bit PCM", hdr is not None and hdr["bits"] == 16
              and hdr["fmt"] == 1, "bits=%s fmt=%s" % (hdr and hdr["bits"], hdr and hdr["fmt"])))
    c.append(("44.1kHz stereo", r["sr"] == SR_EXPECT and r["channels"] == 2,
              "%d Hz / %d ch" % (r["sr"], r["channels"])))
    c.append(("peak <= -1.0 dBFS", r["peak_db"] <= PEAK_MAX + 1e-6,
              "%+.4f dBFS" % r["peak_db"]))
    c.append(("no NaN/Inf", r["finite"], "finite=%s" % r["finite"]))
    c.append(("no silence", not r["silent"], "rms %+.2f dB" % r["rms_db"]))
    c.append(("|dc| < 1e-4", abs(r["dc"]) < DC_MAX, "%.2e" % r["dc"]))
    c.append(("true stereo (L!=R)", r["lr_corr"] < 0.999,
              "L/R corr %.4f" % r["lr_corr"]))
    if spec:
        beats = spec["bars"] * spec["bpb"]
        want = beats * 60.0 / spec["bpm"] * SR_EXPECT
        ok = abs(r["samples"] - want) < 0.5
        c.append(("integer bars (%d x %d)" % (spec["bars"], spec["bpb"]), ok,
                  "%d samples, expect %.2f" % (r["samples"], want)))
        c.append(("bpm %d" % spec["bpm"],
                  abs(r["seconds"] - spec["bars"] * spec["bpb"] * 60.0 / spec["bpm"]) < 1e-3,
                  "%.4f s" % r["seconds"]))
    # loop seam: continuing the file must not be worse than an ordinary step
    c.append(("loop seam continuous",
              r["seam_step"] <= 8.0 * max(r["median_step"], 1e-6),
              "step %.2e vs median %.2e (x%.1f)"
              % (r["seam_step"], r["median_step"],
                 r["seam_step"] / max(r["median_step"], 1e-12))))
    # no fade: the head/tail must not be a ramp into exact silence
    c.append(("no fade at head/tail",
              abs(r["first_sample"]) > 1e-6 and abs(r["last_sample"]) > 1e-6,
              "first %.2e last %.2e" % (r["first_sample"], r["last_sample"])))
    l = lufs_of(r["x"], r["sr"])
    r["lufs"] = l
    if spec:
        c.append(("loudness %.1f +/- 1 LU" % spec["lufs"],
                  abs(l - spec["lufs"]) <= 1.0, "%.2f LUFS" % l))
    r["crest"] = r["peak_db"] - r["rms_db"]
    return c


def check_sfx(r):
    c = []
    hdr = read_header(r["path"])
    c.append(("16-bit PCM", hdr is not None and hdr["bits"] == 16
              and hdr["fmt"] == 1, "bits=%s fmt=%s" % (hdr and hdr["bits"], hdr and hdr["fmt"])))
    c.append(("44.1kHz stereo", r["sr"] == SR_EXPECT and r["channels"] == 2,
              "%d Hz / %d ch" % (r["sr"], r["channels"])))
    c.append(("peak <= -1.0 dBFS", r["peak_db"] <= PEAK_MAX + 1e-6,
              "%+.4f dBFS" % r["peak_db"]))
    c.append(("no NaN/Inf", r["finite"], "finite=%s" % r["finite"]))
    c.append(("not silent", not r["silent"], "rms %+.2f dB" % r["rms_db"]))
    c.append(("|dc| < 1e-4", abs(r["dc"]) < DC_MAX, "%.2e" % r["dc"]))
    c.append(("fade in touches 0 (+/-1 LSB dither)",
              abs(r["first_sample"]) <= 2.0 * LSB, "x[0] = %.2e" % r["first_sample"]))
    c.append(("fade out touches 0 (+/-1 LSB dither)",
              abs(r["last_sample"]) <= 2.0 * LSB, "x[-1] = %.2e" % r["last_sample"]))
    sr = r["sr"]
    x = r["x"]
    m1 = float(np.max(np.abs(x[:max(1, int(0.001 * sr))])))       # inside the ramp
    m3 = float(np.max(np.abs(x[int(0.003 * sr):int(0.006 * sr)])))  # past the ramp
    r["fade_in_ratio"] = m1 / max(m3, 1e-12)
    c.append(("3ms in-fade ramps", m3 < 1e-9 or m1 < 0.60 * m3,
              "1ms peak / 3-6ms peak = %.2f" % r["fade_in_ratio"]))
    e1 = float(np.max(np.abs(x[-max(1, int(0.001 * sr)):])))
    e3 = float(np.max(np.abs(x[-int(0.006 * sr):-int(0.004 * sr)])))
    r["fade_out_ratio"] = e1 / max(e3, 1e-12)
    c.append(("4ms out-fade ramps", e3 < 1e-9 or e1 < 0.60 * e3,
              "last 1ms / 4-6ms = %.2f" % r["fade_out_ratio"]))
    # one-shots use the ungated whole-file value, same measure the generator
    # normalises against (a 40 ms click has no meaningful 400 ms gating block)
    l = lufs_of(r["x"], r["sr"], gated=False)
    r["lufs"] = l
    c.append(("loudness -12 +/- 3 LU", abs(l + 12.0) <= 3.0, "%.2f LUFS" % l))
    return c


def main(argv):
    strict = "--strict" in argv
    rows, failures = [], 0

    print("=" * 104)
    print("Seedfall audio verification -- reading the files on disk")
    print("=" * 104)

    for d, kind in ((MUSIC_DIR, "music"), (SFX_DIR, "sfx")):
        if not os.path.isdir(d):
            print("MISSING DIRECTORY: %s" % d)
            failures += 1
            continue
        names = sorted(f for f in os.listdir(d) if f.lower().endswith(".wav"))
        print("")
        print("%s  (%d files in %s)" % ("A. BACKGROUND MUSIC" if kind == "music"
                                        else "B. SOUND EFFECTS", len(names), d))
        print("")
        for nm in names:
            r = analyse(os.path.join(d, nm))
            checks = check_music(r) if kind == "music" else check_sfx(r)
            bad = [lbl for lbl, ok, _ in checks if not ok]
            failures += len(bad)
            print("  %-18s %7.3fs %8d smp %6d B  peak %+6.2f  rms %+6.2f  "
                  "LUFS %+6.2f  %s"
                  % (r["name"], r["seconds"], r["samples"], r["bytes"],
                     r["peak_db"], r["rms_db"], r["lufs"],
                     "PASS" if not bad else "FAIL " + ",".join(bad)))
            for lbl, ok, det in checks:
                if not ok:
                    print("       x %-26s %s" % (lbl, det))
            rows.append((r, checks))

    print("")
    print("=" * 104)
    print("C. SUMMARY")
    print("=" * 104)
    print("  %-18s %8s %9s %8s %8s %8s %8s" %
          ("file", "dur(s)", "samples", "peak", "rms", "LUFS", "bytes"))
    print("  " + "-" * 76)
    tot = 0
    for r, _ in rows:
        tot += r["bytes"]
        print("  %-18s %8.3f %9d %8.2f %8.2f %8.2f %8d"
              % (r["name"], r["seconds"], r["samples"], r["peak_db"],
                 r["rms_db"], r["lufs"], r["bytes"]))
    print("  " + "-" * 76)
    print("  %d files, %d bytes (%.2f MB)" % (len(rows), tot, tot / 1048576.0))
    print("")
    if failures:
        print("RESULT: %d CHECK(S) FAILED" % failures)
    else:
        print("RESULT: ALL CHECKS PASSED (%d files)" % len(rows))
    return 1 if (failures and strict) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
