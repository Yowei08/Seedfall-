# -*- coding: utf-8 -*-
"""gen_audio.py -- one command regenerates every WAV Seedfall uses.

    python gen_audio.py                 # everything
    python gen_audio.py --only bgm      # just the three music beds
    python gen_audio.py --only sfx      # the 18 one-shots + the 12 plugin sounds
    python gen_audio.py --only plugin   # just the 12 plugin sounds
    python gen_audio.py --only bgm_night

Writes:
    Assets/Resources/Audio/Music/*.wav
    Assets/Resources/Audio/Sfx/*.wav
    Tools/audio_gen/report.txt

Master chains
-------------
Music (loop-safe, nothing in it may fade):
    stems -> constant-power pan + Haas -> mix + mono send
          -> Freeverb (circular convolution, tail wraps into bar 1)
          -> 18 Hz DC block (circular FFT filter)
          -> tanh glue
          -> [gain to target LUFS -> look-ahead limiter -> -1 dBFS] x N
          -> TPDF dither -> 16-bit

SFX:
    layers -> pan -> mix + mono send
          -> Freeverb (linear convolution)
          -> DC block -> tanh glue -> compressor
          -> [gain to target LUFS -> limiter -> -1 dBFS] x N
          -> DC block again -> 3 ms in / 4 ms out fades
          -> TPDF dither -> 16-bit
"""

from __future__ import annotations

import argparse
import os
import sys
import time
import zlib

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import dsp
import bgm as bgmlib
import sfx as sfxlib
from dsp import SR, db_to_lin, lin_to_db, lufs_full, lufs_integrated

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
MUSIC_DIR = os.path.join(PROJECT, "Assets", "Resources", "Audio", "Music")
SFX_DIR = os.path.join(PROJECT, "Assets", "Resources", "Audio", "Sfx")
REPORT = os.path.join(HERE, "report.txt")

BGM_TARGET_LUFS = -16.0
SFX_TARGET_LUFS = -12.0
CEILING_DB = -1.0

_log = []


def say(msg=""):
    print(msg)
    _log.append(msg)


# ---------------------------------------------------------------------------
# master chains
# ---------------------------------------------------------------------------
def master_music(dry, send, rev, target_lufs=BGM_TARGET_LUFS, iters=6,
                 pin_peak=False):
    """Loop-safe music master.  Returns (stereo, loudness history, wet/dry, t)."""
    n = dry.shape[0]
    ln = min(int(rev["ir_s"] * SR), n)
    irL = dsp.freeverb_ir(ln, room=rev["room"], damp=rev["damp"], spread=1,
                          seed=rev["seed"], predelay_ms=rev["predelay_ms"])
    irR = dsp.freeverb_ir(ln, room=rev["room"], damp=rev["damp"], spread=2,
                          seed=rev["seed"] + 101, predelay_ms=rev["predelay_ms"])
    t0 = time.time()
    wet_l = dsp.circular_convolve(send, irL)
    wet_r = dsp.circular_convolve(send, irR)
    wet = np.stack([wet_l, wet_r], axis=1) * rev["wet"]
    raw = dry + wet

    dry_rms = float(np.sqrt(np.mean(dry ** 2)))
    wet_rms = float(np.sqrt(np.mean(wet ** 2)))
    ratio = lin_to_db(wet_rms / max(dry_rms, 1e-12))

    # gain stage: give the compressor some clean headroom
    raw = dsp.normalize_peak(raw, CEILING_DB - 2.0)
    x = dsp.dc_block(raw, 18.0, circular=True)
    x = dsp.soft_clip(x, drive=1.06)
    # glue: pulls the crest factor down so the loudness target can be met
    # without the limiter shaving every transient
    x = dsp.compress(x, thresh_db=-15.0, ratio=1.75, lookahead_ms=4.0,
                     release_ms=200.0, sr=SR, circular=True)

    history = []
    for _ in range(iters):
        l = lufs_integrated(x)
        history.append(l)
        if abs(l - target_lufs) < 0.05:
            break
        x = x * db_to_lin(target_lufs - l)
        x = dsp.limiter(x, ceiling_db=CEILING_DB, lookahead_ms=3.0,
                        release_ms=140.0, circular=True)
    # A track can only be as quiet as its crest factor allows at a given peak:
    #   LUFS ~= peak - crest + K_offset,   so peak == -1 dBFS and a low LUFS
    # target need a BIG crest.  When pin_peak is on we force the peak to
    # exactly -1 EBFS and report whatever loudness that yields; when it is off
    # the loudness target wins and the peak is only bounded above by -1 dBFS.
    if pin_peak:
        x = dsp.normalize_peak(x, CEILING_DB)
    else:
        pk = lin_to_db(float(np.max(np.abs(x))))
        if pk > CEILING_DB:
            x = dsp.normalize_peak(x, CEILING_DB)
    return x, history, ratio, (time.time() - t0)


def master_sfx(x, target_lufs=SFX_TARGET_LUFS, iters=6):
    """One-shot master.

    Loudness is the primary normalisation (that is what keeps 18 effects
    balanced against each other); the -1 dBFS peak is enforced as a ceiling by
    the limiter.  For a dense one-shot the two cannot both be exact -- a 10 dB
    crest factor at -1 dBFS peak *is* about -7 LUFS -- so the peak ends up
    wherever -12 LUFS puts it, never above -1 dBFS.
    """
    # gain stage first, so the compressor threshold means the same thing in
    # every effect regardless of how hot the synthesis came out
    x = dsp.dc_block(x, 18.0)
    x = dsp.normalize_peak(x, CEILING_DB)
    x = dsp.soft_clip(x, drive=1.05)
    x = dsp.compress(x, thresh_db=-13.0, ratio=1.9, lookahead_ms=2.0,
                     release_ms=90.0)
    history = []
    for _ in range(iters):
        l = lufs_full(x)
        history.append(l)
        if abs(l - target_lufs) < 0.05:
            break
        x = x * db_to_lin(target_lufs - l)
        if lin_to_db(float(np.max(np.abs(x)))) > CEILING_DB:
            x = dsp.limiter(x, ceiling_db=CEILING_DB, lookahead_ms=2.5,
                            release_ms=90.0)
    return x, history


# ---------------------------------------------------------------------------
# per-file helpers
# ---------------------------------------------------------------------------
def finish(path, x, name, fade_in=0.0, fade_out=0.0, seed=0,
           normalize=True, lufs_fn=lufs_integrated):
    """Final sanitise + write, then read the file back for the report."""
    x = dsp.sanitize(x, name)
    # DC removal and the anti-click fades fight each other: subtracting the mean
    # after the fade lifts the silent edges off zero again (a low-frequency
    # one-shot carries a partial-cycle mean of ~10 LSB).  So fades first, then a
    # weighted DC correction that is zero at the edges -> both hold exactly.
    if fade_in > 0 or fade_out > 0:
        x = dsp.fade_edges(x, SR, fade_in, fade_out)
        x = dsp.dc_free_with_fades(x, SR, fade_in, fade_out)
    else:
        x = x - x.mean(axis=0, keepdims=True)
    dsp.write_wav(path, x, SR, ceiling_db=CEILING_DB, dither=True, seed=seed,
                  normalize=normalize)
    info = dsp.wav_info(path)
    info["bytes"] = os.path.getsize(path)
    info["name"] = name
    info["lufs"] = lufs_fn(info["x"])
    # how loud is the very last sample vs the peak (loop/click safety)
    tail = info["x"][-max(1, int(0.002 * SR)):]
    info["tail_dbfs"] = lin_to_db(float(np.max(np.abs(tail))))
    head = info["x"][:max(1, int(0.002 * SR))]
    info["head_dbfs"] = lin_to_db(float(np.max(np.abs(head))))
    return info


def bar(sec, beats_per_bar=4, bpm=None):
    if not bpm:
        return None
    return sec / (beats_per_bar * 60.0 / bpm)


# ---------------------------------------------------------------------------
# music
# ---------------------------------------------------------------------------
def gen_music(only=None):
    out = []
    for name, builder in bgmlib.BUILDERS.items():
        if only and name not in only:
            continue
        t0 = time.time()
        say("")
        say("[%s] composing ..." % name)
        mix, meta = builder()
        dry, send, info = mix.render(balance=meta.get("balance"))
        for k in sorted(info):
            v = info[k]
            say("[%s]   stem %-6s trim %+6.1f dB -> momentary %+6.1f dB,"
                " peak %+6.1f dB (was %+6.1f / rms %+6.1f)"
                % (name, k, v["trim_db"], v["level"], v["peak_db"],
                   v["level_before"], v["rms_db"]))
        raw_peak = lin_to_db(float(np.max(np.abs(dry))))
        send_rms = float(np.sqrt(np.mean(send ** 2)))
        say("[%s] dry peak %+.1f dBFS, send bus rms %+.1f dB"
            % (name, raw_peak, lin_to_db(send_rms)))
        target = float(meta.get("lufs_target", BGM_TARGET_LUFS))
        x, hist, wet_ratio, trev = master_music(dry, send, meta["reverb"],
                                                target_lufs=target,
                                                pin_peak=meta.get("pin_peak", False))
        say("[%s] reverb wet/dry = %+.1f dB, convolution %.1fs" % (name, wet_ratio, trev))
        say("[%s] loudness passes: %s" % (name, " -> ".join("%.2f" % h for h in hist)))
        path = os.path.join(MUSIC_DIR, name + ".wav")
        info2 = finish(path, x, name, fade_in=0.0, fade_out=0.0,
                       seed=zlib.crc32(name.encode()) & 0xFFFF,
                       normalize=False, lufs_fn=lufs_integrated)
        info2.update(meta)
        info2["render_s"] = time.time() - t0
        info2["seconds_expected"] = meta["bars"] * meta["bpb"] * 60.0 / meta["bpm"]
        info2["wet_ratio_db"] = wet_ratio
        info2["target_lufs"] = target
        info2["crest_db"] = info2["peak_dbfs"] - info2["rms_dbfs"]
        info2["loudness_hist"] = hist
        info2["stems"] = info
        out.append(info2)
        say("[%s] %.3fs  %d samples  peak %.2f dBFS  %.1f LUFS  (%.1fs)"
            % (name, info2["seconds"], info2["samples"], info2["peak_dbfs"],
               info2["lufs"], info2["render_s"]))
    return out


# ---------------------------------------------------------------------------
# sfx
# ---------------------------------------------------------------------------
def gen_sfx(only=None):
    out = []
    for name, fn, desc in sfxlib.ALL_SFX:
        if only and name not in only:
            continue
        t0 = time.time()
        raw = fn()
        x, hist = master_sfx(raw)
        path = os.path.join(SFX_DIR, name + ".wav")
        info = finish(path, x, name, fade_in=3.0, fade_out=4.0,
                      seed=(zlib.crc32(name.encode()) & 0x3FFF) + 1,
                      normalize=False, lufs_fn=lufs_full)
        info["desc"] = desc
        info["loudness_hist"] = hist
        info["render_s"] = time.time() - t0
        info["target_s"] = sfxlib.TARGET_DUR.get(name)
        out.append(info)
        say("[%s] %.3fs  peak %.2f dBFS  %.1f LUFS  %.0fms"
            % (name, info["seconds"], info["peak_dbfs"], info["lufs"],
               info["seconds"] * 1000))
    return out


# ---------------------------------------------------------------------------
# report
# ---------------------------------------------------------------------------
def write_report(music, sfx, elapsed):
    L = []
    ap = L.append
    ap("=" * 96)
    ap("Seedfall  audio generation report        44.1 kHz / 16-bit / stereo")
    ap("generated by Tools/audio_gen/gen_audio.py  in %.1f s" % elapsed)
    ap("=" * 96)
    ap("")
    ap("A. BACKGROUND MUSIC  (peak %.1f dBFS, seamless loop, per-scene loudness"
       % CEILING_DB)
    ap("   hierarchy: night < farm day < combat)")
    ap("")
    ap("  %-16s %8s %9s %8s %8s %8s %8s %6s %6s %10s"
       % ("file", "dur(s)", "samples", "peak", "rms", "LUFS", "target", "kHz", "ch",
          "bytes"))
    ap("  " + "-" * 100)
    for m in music:
        ap("  %-16s %8.3f %9d %8.2f %8.2f %8.2f %8.1f %6.1f %6d %10d"
           % (m["name"], m["seconds"], m["samples"], m["peak_dbfs"],
              m["rms_dbfs"], m["lufs"], m["target_lufs"], m["sr"] / 1000.0,
              m["channels"], m["bytes"]))
    ap("")
    ap("  musical spec")
    ap("  %-16s %6s %5s %6s %-9s %9s %9s %8s %8s"
       % ("file", "bpm", "bars", "beats", "key", "expect(s)", "actual(s)",
          "wet/dry", "crest"))
    ap("  " + "-" * 100)
    for m in music:
        ap("  %-16s %6d %5d %6d %-9s %9.3f %9.3f %+7.1f %7.1f"
           % (m["name"], m["bpm"], m["bars"], m["bars"] * m["bpb"], m["key"],
              m["seconds_expected"], m["seconds"], m["wet_ratio_db"],
              m["crest_db"]))
    ap("")
    ap("  progression (one chord per bar, wraps bar N -> bar 1)")
    for m in music:
        ap("  %-16s %s" % (m["name"], " ".join(m["prog"])))
    ap("")
    ap("  loop checks   (no fades anywhere; head/tail are the signal itself)")
    ap("  %-16s %10s %10s %11s %11s %10s"
       % ("file", "head dB", "tail dB", "int bars", "int beats", "nan/inf"))
    ap("  " + "-" * 92)
    for m in music:
        spb = SR * 60.0 / m["bpm"]
        beats_f = m["samples"] / spb
        ap("  %-16s %10.1f %10.1f %11d %11d %10d"
           % (m["name"], m["head_dbfs"], m["tail_dbfs"],
              int(round(beats_f / m["bpb"])) if abs(beats_f % m["bpb"]) < 0.02
              or abs(beats_f % m["bpb"] - m["bpb"]) < 0.02 else -1,
              int(round(beats_f)) if abs(beats_f - round(beats_f)) < 0.02 else -1,
              m["nan"]))
    ap("")
    ap("B. SOUND EFFECTS  (target %.1f LUFS, peak <= %.1f dBFS, 3ms in / 4ms out)"
       % (SFX_TARGET_LUFS, CEILING_DB))
    ap("")
    main_sfx = [s for s in sfx if not s["name"].startswith("sfx_pl_")]
    plug_sfx = [s for s in sfx if s["name"].startswith("sfx_pl_")]
    head = ("  %-18s %8s %8s %8s %8s %8s %8s %9s"
            % ("file", "dur(s)", "peak", "rms", "LUFS", "DC", "tail dB", "bytes"))
    rule = "  " + "-" * 92

    ap("  B1. main one-shots  (%d)" % len(main_sfx))
    ap(head)
    ap(rule)
    for s in main_sfx:
        ap("  %-18s %8.3f %8.2f %8.2f %8.2f %8.1e %8.1f %9d"
           % (s["name"], s["seconds"], s["peak_dbfs"], s["rms_dbfs"], s["lufs"],
              s["dc"], s["tail_dbfs"], s["bytes"]))
    ap("")
    ap("  B2. plugin effects -- one per plugin, 12 plugins  (%d)" % len(plug_sfx))
    ap("      same master chain as B1 => same -12 LUFS / <= -1 dBFS / zeroed edges.")
    ap("      The gameplay layer plays these 5 dB below B1 (see Audio/GameAudio.cs).")
    ap(head)
    ap(rule)
    for s in plug_sfx:
        ap("  %-18s %8.3f %8.2f %8.2f %8.2f %8.1e %8.1f %9d"
           % (s["name"], s["seconds"], s["peak_dbfs"], s["rms_dbfs"], s["lufs"],
              s["dc"], s["tail_dbfs"], s["bytes"]))
    ap("")
    ap("  synthesis path")
    for s in sfx:
        ap("  %-18s %s" % (s["name"], s["desc"]))
    ap("")
    tot_m = sum(m["bytes"] for m in music)
    tot_s = sum(s["bytes"] for s in sfx)
    tot_pl = sum(s["bytes"] for s in plug_sfx)
    ap("C. TOTALS")
    ap("  music  %2d files  %10d bytes  (%.2f MB)  %.1f s of audio"
       % (len(music), tot_m, tot_m / 1048576.0, sum(m["seconds"] for m in music)))
    ap("  sfx    %2d files  %10d bytes  (%.2f MB)"
       % (len(sfx), tot_s, tot_s / 1048576.0))
    ap("    of which plugin effects  %2d files  %8d bytes  (%.2f MB)"
       % (len(plug_sfx), tot_pl, tot_pl / 1048576.0))
    ap("  total  %2d files  %10d bytes  (%.2f MB)"
       % (len(music) + len(sfx), tot_m + tot_s, (tot_m + tot_s) / 1048576.0))
    ap("")
    return "\n".join(L)


def main(argv=None):
    ap = argparse.ArgumentParser(description="regenerate every Seedfall WAV")
    ap.add_argument("--only", nargs="*", default=None,
                    help="restrict to these names (bgm, sfx, or an exact file stem)")
    args = ap.parse_args(argv)

    os.makedirs(MUSIC_DIR, exist_ok=True)
    os.makedirs(SFX_DIR, exist_ok=True)

    only = None
    if args.only:
        only = set()
        for o in args.only:
            if o == "bgm":
                only |= set(bgmlib.BUILDERS)
            elif o == "sfx":
                only |= {n for n, _, _ in sfxlib.ALL_SFX}
            elif o == "plugin":
                only |= {n for n, _, _ in sfxlib.PLUGIN_SFX}
            else:
                only.add(o)

    t0 = time.time()
    say("Seedfall audio generation  ->  %s" % PROJECT)
    say("python %s" % sys.version.split()[0])

    if not dsp._selftest():
        say("DSP SELF-TEST FAILED -- refusing to generate")
        return 1
    say("")

    music = gen_music(only)
    sfx = gen_sfx(only)
    elapsed = time.time() - t0

    say("")
    report = write_report(music, sfx, elapsed)
    print(report)
    with open(REPORT, "w", encoding="utf-8") as f:
        f.write(report + "\n")
    print("\nreport written to %s" % REPORT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
