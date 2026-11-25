# -*- coding: utf-8 -*-
"""bandsplit.py -- WHERE the energy sits.  Read-only measuring instrument.

Why this file exists
--------------------
`sfx_water` is now a deliberate **fusion** of two sources: a water pour and a
laser tone.  A single spectral centroid cannot argue that both are present --
a loud pour happily hides a quiet tone inside one averaged number.  So this
script answers the only question that matters for that brief:

    are the two layers **both** measurable, and is either one missing?

It reports, per file:

  A. THREE-BAND ENERGY SPLIT  (share of total |X|^2 in 20 Hz - 20 kHz)
       60-120 Hz    the low-frequency swell ("低频涌动")
       200-800 Hz   the water body proper ("水流主体"), below the laser band
       800-2000 Hz  the laser band ("激光窄带") -- reported twice: all of it,
                    and just the part that sits inside the harmonic peaks,
                    so "narrowband tone" and "noise that happens to be there"
                    do not get added together.
  B. NARROW PEAKS in 700-2100 Hz, measured on the **frame-averaged** spectrum
     (same 2048-sample Hann window / 1024 hop as ``harshness.tone()``, so a
     prominence printed here is the same quantity that table calls `tone`).
     A sustained tone keeps its peak from frame to frame and survives the
     average; random noise peaks move and get flattened.  That is what makes
     this a measurement of a *layer* rather than of one lucky bin.
  C. The arithmetic behind A: total energy inside +-40 Hz of the detected
     harmonics vs between them, in dB, plus the peak/neighbour prominence.

Nothing here synthesises, filters or rewrites a file, and nothing here is
imported by `gen_audio.py`: the generator's sound is still made by dsp.py /
instruments.py only.  The band and window definitions are imported from
`harshness.py` so the two reports cannot drift apart.

    python bandsplit.py                       # every sfx, three-band split
    python bandsplit.py --dir <folder>        # analyse a backup instead
    python bandsplit.py --compare <before> <after> --name sfx_water
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from harshness import SFX_DIR, band_share, db, load, spectrum   # noqa: E402

# the three bands this brief is about
RUMBLE = (60.0, 120.0)
BODY = (200.0, 800.0)
LASER = (800.0, 2000.0)
LASER_LO = (700.0, 2100.0)      # detection window, slightly wider than the band

FRAME = 2048                    # same as harshness.tone()
HALF_HZ = 370.0                 # same neighbourhood as harshness.tone()
HARM_HZ = 40.0                  # +-40 Hz counts as "inside the harmonic"
BIAS = 2000.0                   # the +2000 dB bias trick: floor sits at -2000 dB


def frame_avg_spectrum(x, sr, frame=FRAME):
    """Frame-averaged magnitude spectrum -- identical windowing to harshness.tone().

    Returns (freqs, mag_avg) where ``mag_avg`` is the mean over frames of
    |rfft(Hann(frame))|.  Averaging is what separates a tone (always in the same
    bin) from noise (its maximum jumps around and averages down).
    """
    m = x.mean(axis=1)
    if m.size < frame:
        # same frame-sizing fix as harshness.tone(): floor, then clamp, so a file
        # shorter than one frame cannot broadcast-fail
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
        return None, None
    return np.fft.rfftfreq(frame, 1.0 / sr), acc / float(n)


def neighbourhood_median(f, mag, half_hz=HALF_HZ):
    """Median of the spectrum inside +-half_hz (zero-energy bins included).

    ``harshness.tone()`` takes the median over the bins it can see between
    ``lo`` and ``hi``; here the window is taken on the whole spectrum with
    silent bins counted as silence, which is the stricter reading.
    """
    df = float(f[1] - f[0])
    half = max(1, int(round(half_hz / df)))
    k = 2 * half + 1
    win = np.lib.stride_tricks.sliding_window_view(
        np.pad(mag, half, mode="edge"), k)
    return np.median(win, axis=1), half


def find_peaks(f, mag, lo=LASER_LO[0], hi=LASER_LO[1], max_n=6,
               min_prom_db=2.0):
    """Local maxima of the frame-averaged spectrum, **ranked by prominence**.

    Only bins that are local maxima over +-3 bins are kept, so one wide peak is
    counted once.  Each peak is reported with the median of its +-370 Hz
    neighbourhood -- the same quantity `harshness.tone()` maximises.

    ★ ranked by prominence, not by magnitude ★
    The question is "is there a *tone*", and a loud noise skirt is not a tone:
    a peak buried 1 dB above its neighbourhood can be the biggest thing in the
    band and still be all texture.  Ranking by prominence is what makes the
    table answer the question that was asked.
    """
    sel = np.where((f >= lo) & (f <= hi))[0]
    if sel.size < 8:
        return []
    med, _ = neighbourhood_median(f, mag)
    out = []
    for i in sel:
        i0, i1 = max(i - 3, sel[0]), min(i + 3, sel[-1])
        if mag[i] < np.max(mag[i0:i1 + 1]):
            continue
        if mag[i] <= 0.0:
            continue
        prom = 20.0 * math.log10(max(mag[i], 1e-300) / max(med[i], 1e-300))
        if not np.isfinite(prom) or prom < min_prom_db:
            continue
        out.append(dict(hz=float(f[i]), prom_db=float(prom),
                        mag=float(mag[i]), med=float(med[i])))
    out.sort(key=lambda p: -p["prom_db"])
    return out[:max_n]


def prominence_at(f, mag, hz, half_hz=60.0):
    """Best prominence within +-half_hz of a *predicted* frequency.

    Used as a control: if the laser is really at 940/1880 Hz, the prominence
    must be high exactly there and low at neighbouring control frequencies.
    """
    med, _ = neighbourhood_median(f, mag)
    sel = np.where((f >= hz - half_hz) & (f <= hz + half_hz))[0]
    if sel.size == 0:
        return 0.0, 0.0
    rel = 20.0 * np.log10(np.maximum(mag[sel], 1e-300) / np.maximum(med[sel], 1e-300))
    i = int(np.argmax(rel))
    return float(rel[i]), float(f[sel[i]])


def local_energy(f, x_complex_energy, lo, hi, peaks, harm_hz=HARM_HZ):
    """Energy inside +-harm_hz of the peaks vs the rest of [lo, hi)."""
    sel = (f >= lo) & (f < hi)
    e = x_complex_energy
    tot = float(np.sum(e[sel]))
    inside = 0.0
    for p in peaks:
        if lo <= p["hz"] < hi:
            inside += float(np.sum(e[(f >= p["hz"] - harm_hz) & (f <= p["hz"] + harm_hz)]))
    return tot, inside


def analyse(path, probe_hz=None):
    x, sr = load(path)
    freqs, mag = spectrum(x)                 # whole-file, Hann (harshness'口径)
    power = mag * mag
    sel = (freqs >= 20.0) & (freqs <= 20000.0)
    total = float(np.sum(power[sel]))

    ff, favg = frame_avg_spectrum(x, sr)
    peaks = find_peaks(ff, favg) if favg is not None else []

    # energy of the harmonic peaks, measured on the whole-file spectrum so it is
    # the same quantity as the other shares
    tot_laser, inside_laser = local_energy(freqs, power, LASER[0], LASER[1], peaks)

    r = dict(
        name=os.path.basename(path)[:-4],
        seconds=x.shape[0] / float(sr),
        total_energy_db=db(math.sqrt(total)) if total > 0 else -200.0,
        share_rumble=band_share(freqs, mag, *RUMBLE),
        share_body=band_share(freqs, mag, *BODY),
        share_laser=band_share(freqs, mag, *LASER),
        share_below_200=band_share(freqs, mag, 20.0, 200.0),
        share_above_2k=band_share(freqs, mag, 2000.0, 20000.0),
        laser_peak_share=(inside_laser / tot_laser) if tot_laser > 0 else 0.0,
        peaks=[dict(hz=round(p["hz"], 1), prom_db=round(p["prom_db"], 1))
               for p in peaks],
    )
    if probe_hz:
        r["probe"] = {("%.0f" % hz): round(prominence_at(ff, favg, hz)[0], 1)
                      for hz in probe_hz}
    # absolute levels of the three bands (energy dBFS^2, same reference as the file)
    for key, (lo, hi) in (("rumble", RUMBLE), ("body", BODY), ("laser", LASER)):
        s = (freqs >= lo) & (freqs < hi)
        r["level_%s_db" % key] = 10.0 * math.log10(max(float(np.sum(power[s])), 1e-30))
    return r


def fmt_peaks(peaks):
    if not peaks:
        return "-"
    return " ".join("%.0f/%+.1f" % (p["hz"], p["prom_db"]) for p in peaks[:6])


def print_table(rows):
    head = ("%-18s %6s %8s %8s %8s %8s %8s  %s"
            % ("file", "dur", "60-120", "200-800", "800-2k", ">2k", "in-peak",
               "narrow peaks 700-2100 Hz  Hz/dB above +-370 Hz median"))
    print(head)
    print("-" * len(head))
    for r in rows:
        print("%-18s %6.3f %7.1f%% %7.1f%% %7.1f%% %7.1f%% %7.1f%%  %s"
              % (r["name"], r["seconds"], r["share_rumble"] * 100.0,
                 r["share_body"] * 100.0, r["share_laser"] * 100.0,
                 r["share_above_2k"] * 100.0, r["laser_peak_share"] * 100.0,
                 fmt_peaks(r["peaks"])))
    print("")
    print("60-120   low-frequency swell        (0.3 s one-shot: needs a real layer)")
    print("200-800  water body below the laser band")
    print("800-2k   laser band, all of it      in-peak = the share of that band")
    print("         sitting inside +-40 Hz of a detected harmonic (a tone), vs the")
    print("         rest of it (noise that merely happens to live there)")


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=None, help="analyse this folder instead")
    ap.add_argument("--name", default=None, help="only this stem")
    ap.add_argument("--json", default=None)
    ap.add_argument("--probe", default=None,
                    help="comma separated Hz: also print the peak prominence "
                         "(dB above the +-370 Hz median) at those frequencies")
    ap.add_argument("--compare", nargs=2, default=None, metavar=("BEFORE", "AFTER"),
                    help="print a before/after diff for --name")
    args = ap.parse_args(argv)
    probe = [float(v) for v in args.probe.split(",")] if args.probe else None

    if args.compare:
        before_dir, after_dir = args.compare
        name = args.name or "sfx_water"
        b = analyse(os.path.join(before_dir, name + ".wav"), probe)
        a = analyse(os.path.join(after_dir, name + ".wav"), probe)
        rows = [b, a]
        print("=" * 96)
        print("%s : BEFORE  %s" % (name, before_dir))
        print("%s : AFTER   %s" % (name, after_dir))
        print("=" * 96)
        print_table(rows)
        print("")
        hdr = ("%-16s %10s %10s %10s"
               % ("band", "before", "after", "delta"))
        print(hdr)
        print("-" * len(hdr))
        for key, label in (("share_rumble", "60-120 Hz"),
                           ("share_body", "200-800 Hz"),
                           ("share_laser", "800-2000 Hz"),
                           ("laser_peak_share", "in-peak /800-2k"),
                           ("share_above_2k", ">2 kHz")):
            print("%-16s %9.1f%% %9.1f%% %+9.1fpp"
                  % (label, b[key] * 100.0, a[key] * 100.0,
                     (a[key] - b[key]) * 100.0))
        print("")
        print("absolute band energy (dB):")
        for key, label in (("level_rumble_db", "60-120 Hz"),
                           ("level_body_db", "200-800 Hz"),
                           ("level_laser_db", "800-2000 Hz")):
            print("  %-12s before %+8.2f   after %+8.2f   delta %+7.2f"
                  % (label, b[key], a[key], a[key] - b[key]))
        if probe:
            print("")
            print("peak prominence at fixed frequencies (dB above +-370 Hz median,")
            print("frame-averaged spectrum; >= ~6 dB reads as a tone, noise is ~0-3):")
            print("  %-10s %10s %10s" % ("Hz", "before", "after"))
            for hz in probe:
                k = "%.0f" % hz
                print("  %-10s %+10.1f %+10.1f" % (k, b["probe"][k], a["probe"][k]))
        print("")
        if args.json:
            with open(args.json, "w", encoding="utf-8") as f:
                json.dump(rows, f, indent=1)
            print("json -> %s" % args.json)
        return 0

    d = args.dir or SFX_DIR
    rows = []
    for nm in sorted(f for f in os.listdir(d) if f.lower().endswith(".wav")):
        if args.name and nm[:-4] != args.name:
            continue
        rows.append(analyse(os.path.join(d, nm), probe))
    print_table(rows)
    if probe:
        print("")
        print("peak prominence at fixed frequencies (dB above +-370 Hz median):")
        for hz in probe:
            k = "%.0f" % hz
            print("  %-7s " % k + "  ".join("%s %+.1f" % (r["name"], r["probe"][k])
                                            for r in rows))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(rows, f, indent=1)
        print("json -> %s" % args.json)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
