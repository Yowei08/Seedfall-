# -*- coding: utf-8 -*-
"""bgm.py -- the three looping beds: farm day, combat, night.

Everything here is generated into a buffer whose length is exactly
``bars * beats_per_bar * samples_per_beat`` samples:

  * every note tail is written with wrap-around, so the last note of bar 32
    rings straight into bar 1;
  * the reverb is applied by *circular* FFT convolution, so its tail wraps too;
  * the master chain (DC block, glue, limiter) is circular as well;
  * no fade in / fade out anywhere -- the loop point is musical, not faded.

The progressions close on the tonic so bar N -> bar 1 is a resolution, not a
cut.  See README.md for the per-track structure table.
"""

from __future__ import annotations

import math

import numpy as np

import dsp
import instruments as ins
from dsp import SR, TAU, exp_curve, midi_to_hz, note_to_hz

# ---------------------------------------------------------------------------
# chord tables (MIDI).  Pad voicings are deliberately open (root/fifth/tens).
# ---------------------------------------------------------------------------
CHORDS = {
    # D major family
    "D":      [50, 57, 62, 66],       # D3 A3 D4 F#4
    "Dadd9":  [50, 57, 64, 66],       # D3 A3 E4 F#4
    "A":      [45, 52, 57, 61],       # A2 E3 A3 C#4
    "A7":     [45, 52, 55, 61],       # A2 E3 G3 C#4
    "Bm":     [47, 54, 59, 62],       # B2 F#3 B3 D4
    "Bm7":    [47, 54, 59, 62],
    "G":      [43, 50, 55, 59],       # G2 D3 G3 B3
    "Gmaj7":  [43, 50, 54, 59],       # G2 D3 F#3 B3
    "F#m7":   [42, 49, 54, 57],       # F#2 C#3 F#3 A3
    "F#":     [42, 49, 54, 58],       # F#2 C#3 F#3 A#3
}

BASS_ROOT = {
    "D": 38, "Dadd9": 38, "A": 45, "A7": 45, "Bm": 35, "Bm7": 35,
    "G": 43, "Gmaj7": 43, "F#m7": 42, "F#": 30,
}

# arpeggio material: [root, third, fifth, octave, tenth] as MIDI
ARP = {
    "Bm":  [59, 62, 66, 71, 74],
    "Bm7": [59, 62, 66, 71, 74],
    "G":   [55, 59, 62, 67, 71],
    "Gmaj7": [55, 59, 62, 67, 71],
    "D":   [50, 54, 57, 62, 66],
    "Dadd9": [50, 54, 57, 62, 66],
    "A":   [45, 49, 52, 57, 61],
    "A7":  [45, 49, 52, 57, 61],
    "F#":  [42, 46, 49, 54, 58],
    "F#m7": [42, 45, 49, 54, 57],
}


# ---------------------------------------------------------------------------
# circular multi-track buffer
# ---------------------------------------------------------------------------
def circ_add(dst, src, start):
    """Add ``src`` into ``dst`` at ``start``, wrapping past the end."""
    n = dst.shape[0]
    m = src.shape[0]
    if m <= 0:
        return
    i = int(start) % n
    if i + m <= n:
        dst[i:i + m] += src
        return
    k = n - i
    dst[i:] += src[:k]
    rest = m - k
    p = k
    while rest > 0:
        c = min(rest, n)
        dst[:c] += src[p:p + c]
        rest -= c
        p += c


class Mix:
    """Collects stems, then renders them to a stereo mix plus a reverb send."""

    def __init__(self, n, seed=0):
        self.n = int(n)
        self.seed = seed
        self.stems = {}
        self.spec = {}      # name -> dict(gain, send, haas_ms, haas_gain, eq)

    def stem(self, name, gain=1.0, send=0.2, haas_ms=0.0, haas_gain=1.0, eq=None):
        if name not in self.stems:
            self.stems[name] = np.zeros((self.n, 2))
            self.spec[name] = dict(gain=gain, send=send, haas_ms=haas_ms,
                                   haas_gain=haas_gain, eq=eq)
        return self.stems[name]

    def add(self, name, mono, start, pan=0.0, gain=1.0):
        d = self.stems.get(name)
        if d is None:
            d = self.stem(name)
        circ_add(d, dsp.pan(np.asarray(mono) * gain, pan), start)

    def render(self, balance=None):
        """Renders to (dry, send, info).

        ``balance`` maps a stem name to the momentary-max level (dB) it should
        sit at.  When a stem is named there, the hand-set design gain is
        discarded and the trim alone places it, so the reported trim is the
        whole story and every stem lands where the mix spec says -- hand-
        guessing the gains was 20 dB off on the melody.

        An optional per-stem peak cap keeps one very dynamic stem from eating
        all the mix headroom (the 16th-note arpeggio was peaking past full
        scale after its +14 dB balance trim).
        """
        dry = np.zeros((self.n, 2))
        send = np.zeros(self.n)
        info = {}
        for name, st in self.stems.items():
            s = self.spec[name]
            x = st
            if s["eq"] is not None:
                x = s["eq"](x)
            if s["haas_ms"] > 0:
                x = dsp.haas(x, s["haas_ms"], s["haas_gain"], "right",
                             circular=True)
            g = s["gain"]
            trim_db = 0.0
            lvl_before = dsp.momentary_max_db(x)
            pk_before = dsp.lin_to_db(float(np.max(np.abs(x))))
            if balance is not None and name in balance and lvl_before > -80.0:
                spec = balance[name]
                if not isinstance(spec, dict):
                    spec = dict(m=spec)
                g = 1.0                       # the trim replaces the design gain
                trim_db = float(spec["m"]) - lvl_before
                trim_db = min(trim_db, float(spec.get("peak", 0.0)) - pk_before)
                trim_db = max(-36.0, min(36.0, trim_db))
                g *= dsp.db_to_lin(trim_db)
            x = x * g
            info[name] = dict(gain=g, trim_db=trim_db, level_before=lvl_before,
                              peak_db=dsp.lin_to_db(float(np.max(np.abs(x)))),
                              level=dsp.momentary_max_db(x),
                              rms_db=20.0 * np.log10(
                                  max(float(np.sqrt(np.mean(x ** 2))), 1e-12)))
            dry += x
            send += 0.5 * (x[:, 0] + x[:, 1]) * s["send"]
        return dry, send, info


# ---------------------------------------------------------------------------
# shared pad / bass helpers
# ---------------------------------------------------------------------------
def add_pad(mix, chord, beat, beats, spb, amp=0.16, lp=2400.0, a=0.35, r=1.0,
            seed=0, detune=(-9.0, 0.0, 9.0), voices=(0, 1, 2), pan_spread=0.42,
            open_from=None):
    """Place a chord as a pad.  Voices are split across the stereo field."""
    notes = CHORDS[chord]
    hold = beats * spb / SR
    for j, vi in enumerate(voices):
        if vi >= len(notes):
            continue
        f = midi_to_hz(notes[vi])
        p = 0.0
        if len(voices) > 1:
            p = -pan_spread + 2.0 * pan_spread * j / (len(voices) - 1)
        sig = ins.pad_note(f, hold, amp=amp, detune=detune, lp=lp, a=a, r=r,
                           seed=seed + j * 17 + int(beat) * 3, open_from=open_from)
        mix.add("pad", sig, int(round(beat * spb)), pan=p)


def add_bass(mix, chord, beat, beats, spb, amp=0.36, octave=0, pattern=None,
             lp=900.0, sat=1.35):
    root = BASS_ROOT[chord] + 12 * octave
    if pattern is None:
        pattern = [(0.0, beats * 0.96, 0)]
    for off, dur, oct_add in pattern:
        f = midi_to_hz(root + 12 * oct_add)
        sig = ins.bass_note(f, dur * spb / SR, amp=amp, lp=lp, sat=sat)
        mix.add("bass", sig, int(round((beat + off) * spb)))


# ---------------------------------------------------------------------------
# track 1 -- bgm_farm_day
# ---------------------------------------------------------------------------
FARM_BPM = 84
FARM_BARS = 32
FARM_PROG = [
    "D", "A", "Bm", "G",
    "D", "G", "A", "D",
    "Bm", "G", "D", "A",
    "G", "A", "D", "D",
]

# (beat, dur_beats, note, velocity)
FARM_MEL_A = [
    (0.0, 1.5, "D5", 0.95), (1.5, 0.5, "E5", 0.70), (2.0, 2.0, "F#5", 0.85),
    (4.0, 1.0, "A5", 0.95), (5.0, 1.0, "F#5", 0.75), (6.0, 2.0, "D5", 0.80),
    (8.0, 1.5, "B4", 0.85), (9.5, 0.5, "D5", 0.65), (10.0, 2.0, "F#5", 0.85),
    (12.0, 1.0, "E5", 0.80), (13.0, 1.0, "D5", 0.70), (14.0, 2.0, "B4", 0.75),
    (16.0, 1.5, "F#5", 0.85), (17.5, 0.5, "G5", 0.70), (18.0, 2.0, "A5", 0.90),
    (20.0, 1.0, "F#5", 0.80), (21.0, 1.0, "E5", 0.70), (22.0, 2.0, "D5", 0.85),
    (24.0, 1.5, "E5", 0.80), (25.5, 0.5, "F#5", 0.65), (26.0, 2.0, "E5", 0.75),
    (28.0, 3.5, "D5", 0.80),
]

FARM_MEL_B = [
    (32.0, 1.5, "D5", 0.90), (33.5, 0.5, "E5", 0.65), (34.0, 2.0, "F#5", 0.85),
    (36.0, 1.0, "B4", 0.80), (37.0, 1.0, "D5", 0.70), (38.0, 2.0, "F#5", 0.85),
    (40.0, 1.5, "A5", 0.95), (41.5, 0.5, "G5", 0.70), (42.0, 2.0, "F#5", 0.80),
    (44.0, 1.0, "E5", 0.80), (45.0, 1.0, "C#5", 0.70), (46.0, 2.0, "E5", 0.75),
    (48.0, 2.0, "G5", 0.85), (50.0, 1.0, "F#5", 0.75), (51.0, 1.0, "E5", 0.65),
    (52.0, 1.5, "D5", 0.85), (53.5, 0.5, "E5", 0.65), (54.0, 2.0, "C#5", 0.72),
    (56.0, 2.0, "D5", 0.85), (58.0, 1.0, "A4", 0.65),
    (59.0, 4.0, "D5", 0.78),
]

# sparse bell sparkle that comes in for the last 8 bars
FARM_SPARKLE = [
    (96.0, 2.5, "A5", 0.5), (100.0, 2.5, "F#5", 0.42), (104.0, 2.5, "D5", 0.40),
    (108.0, 2.5, "E5", 0.42), (112.0, 2.5, "B5", 0.45), (116.0, 2.5, "A5", 0.40),
    (120.0, 2.5, "F#5", 0.42), (124.0, 3.0, "D6", 0.38),
]


def build_farm_day():
    spb = SR * 60.0 / FARM_BPM
    beats = FARM_BARS * 4
    n = int(round(beats * spb))
    mix = Mix(n, seed=11)
    mix.stem("pluck", gain=0.90, send=0.30, haas_ms=6.0, haas_gain=0.75)
    mix.stem("pad", gain=0.85, send=0.26, haas_ms=9.0, haas_gain=0.80,
             eq=lambda x: dsp.highpass(x, 130.0))
    mix.stem("bass", gain=0.95, send=0.04, eq=lambda x: dsp.highpass(x, 34.0))
    mix.stem("perc", gain=1.00, send=0.13)
    mix.stem("bell", gain=0.55, send=0.42, haas_ms=11.0, haas_gain=0.7)

    prog32 = FARM_PROG * 2

    # --- pad + bass: one chord per bar -----------------------------------
    for bar in range(FARM_BARS):
        ch = prog32[bar]
        b = bar * 4.0
        add_pad(mix, ch, b, 4.0, spb, amp=0.150, lp=2100.0, a=0.55, r=1.05,
                seed=100 + bar, voices=(0, 2, 3), pan_spread=0.45,
                open_from=700.0)
        if bar % 4 == 0:
            pat = [(0.0, 1.85, 0), (2.0, 1.0, 0), (3.25, 0.7, 1)]
        elif bar % 4 == 1:
            pat = [(0.0, 1.85, 0), (2.5, 1.4, 0)]
        elif bar % 4 == 2:
            pat = [(0.0, 1.6, 0), (2.0, 1.0, 0), (3.0, 0.9, 0)]
        else:
            pat = [(0.0, 1.9, 0), (2.0, 1.5, 0)]
        add_bass(mix, ch, b, 4.0, spb, amp=0.34, pattern=pat)

    # --- pluck melody -----------------------------------------------------
    for i, (beat, dur, nm, vel) in enumerate(FARM_MEL_A + FARM_MEL_B):
        # second time round the phrase is played a touch softer/longer
        soft = 0.92 if beat < 64 else 0.86
        sig = ins.pluck(note_to_hz(nm), dur * spb / SR + 0.28,
                        amp=0.40 * vel * soft, t60=1.35, damp=0.52,
                        seed=700 + i, sweep_hi=8500.0, sweep_lo=1900.0,
                        release_ms=60.0)
        mix.add("pluck", sig, int(round(beat * spb)), pan=0.13)

    # --- bell sparkle in the last 8 bars ---------------------------------
    for i, (beat, dur, nm, vel) in enumerate(FARM_SPARKLE):
        sig = ins.fm_bell(note_to_hz(nm), dur * spb / SR, amp=0.16 * vel,
                          ratio=3.0, index=1.8, index_tau=0.20, tau=0.75,
                          shimmer=0.18)
        mix.add("bell", sig, int(round(beat * spb)), pan=-0.38)

    # --- light percussion -------------------------------------------------
    rng = np.random.default_rng(4242)
    for bar in range(FARM_BARS):
        b = bar * 4.0
        # kick: downbeat of every bar, pickup before some bar lines
        mix.add("perc", ins.kick(dur=0.26, amp=0.42, f_hi=110.0, f_lo=48.0,
                                 tau=0.13, click=0.12, seed=bar),
                int(round(b * spb)), pan=0.0)
        if bar % 4 == 3:
            mix.add("perc", ins.kick(dur=0.20, amp=0.26, f_hi=104.0, f_lo=50.0,
                                     tau=0.10, click=0.10, seed=bar + 900),
                    int(round((b + 3.5) * spb)), pan=0.0)
        # shaker on 8ths, alternating velocity
        for e in range(8):
            vel = 0.20 if e % 2 == 0 else 0.11
            if bar % 8 == 7 and e == 7:
                continue
            mix.add("perc", ins.shaker(amp=vel * (0.85 + 0.3 * rng.random()),
                                       seed=bar * 16 + e),
                    int(round((b + e * 0.5) * spb)), pan=0.34)
        # soft rim on beat 3 of the second half of each 8
        if 4 <= bar % 8 <= 7:
            mix.add("perc", ins.snare(dur=0.16, amp=0.13, tau=0.05,
                                      noise_tau=0.04, bp=2400.0,
                                      seed=bar + 77),
                    int(round((b + 2.0) * spb)), pan=-0.16)

    return mix, dict(name="bgm_farm_day", bpm=FARM_BPM, bars=FARM_BARS,
                     bpb=4, key="D major", prog=FARM_PROG,
                     lufs_target=-16.0,
                     balance=dict(pluck=dict(m=-18.0, peak=-4.0),
                                  pad=dict(m=-24.0, peak=-10.0),
                                  bass=dict(m=-21.0, peak=-7.0),
                                  perc=dict(m=-25.0, peak=-6.0),
                                  bell=dict(m=-30.0, peak=-14.0)),
                     reverb=dict(room=0.80, damp=0.42, predelay_ms=18.0,
                                 ir_s=3.0, wet=0.85, seed=31))


# ---------------------------------------------------------------------------
# track 2 -- bgm_combat
# ---------------------------------------------------------------------------
COMBAT_BPM = 132
COMBAT_BARS = 32
COMBAT_PROG = [
    "Bm", "Bm", "G", "G", "D", "D", "A", "A",
    "Bm", "G", "D", "A", "Bm", "G", "F#", "A",
    "Bm", "Bm", "G", "G", "D", "D", "A", "A",
    "Bm", "G", "D", "A", "G", "A", "F#", "Bm",
]

ARP_A = [0, 1, 2, 3, 2, 1, 0, 1, 0, 1, 2, 3, 2, 1, 0, 2]
ARP_B = [0, 2, 1, 3, 2, 0, 1, 2, 0, 2, 1, 3, 4, 3, 2, 1]
ARP_C = [0, 1, 2, 3, 4, 3, 2, 1, 0, 2, 1, 3, 2, 4, 3, 1]


def build_combat():
    spb = SR * 60.0 / COMBAT_BPM
    beats = COMBAT_BARS * 4
    n = int(round(beats * spb))
    mix = Mix(n, seed=22)
    mix.stem("arp", gain=0.92, send=0.17, haas_ms=5.0, haas_gain=0.7)
    mix.stem("pad", gain=0.55, send=0.14,
             eq=lambda x: dsp.highpass(x, 180.0))
    mix.stem("bass", gain=0.98, send=0.03, eq=lambda x: dsp.highpass(x, 33.0))
    mix.stem("drums", gain=1.00, send=0.10)

    rng = np.random.default_rng(909)

    # --- pad: two long chords per 4 bars, keeps it wide but out of the way
    for bar in range(0, COMBAT_BARS, 2):
        ch = COMBAT_PROG[bar]
        add_pad(mix, ch, bar * 4.0, 8.0, spb, amp=0.075, lp=1500.0,
                a=0.30, r=0.8, seed=300 + bar, voices=(0, 3), pan_spread=0.55,
                detune=(-11.0, 0.0, 11.0))

    # --- 16th-note arpeggio ----------------------------------------------
    idx = 0
    for bar in range(COMBAT_BARS):
        ch = COMBAT_PROG[bar]
        tones = ARP[ch]
        if bar < 8:
            pat, vel0, accent = ARP_A, 0.52, (0, 4, 8, 12)
        elif bar < 16:
            pat, vel0, accent = ARP_B, 0.56, (0, 3, 6, 8, 11, 14)
        elif bar < 24:
            pat, vel0, accent = ARP_A, 0.55, (0, 4, 8, 12)
        else:
            pat, vel0, accent = ARP_C, 0.60, (0, 2, 4, 8, 10, 12, 14)
        b0 = bar * 4.0
        for s in range(16):
            beat = b0 + s * 0.25
            tone = tones[pat[s] % len(tones)]
            vel = vel0 * (1.18 if s in accent else 1.0)
            vel *= 0.92 + 0.16 * rng.random()
            dur = spb * 0.25 / SR * 0.90 + 0.06
            sig = ins.pluck(midi_to_hz(tone), dur, amp=0.34 * vel,
                            t60=0.42, damp=0.42, seed=1000 + idx,
                            sweep_hi=7500.0, sweep_lo=2600.0, release_ms=22.0)
            pan = -0.30 + 0.60 * (s / 15.0)
            mix.add("arp", sig, int(round(beat * spb)), pan=pan)
            idx += 1

    # --- driving bass: 8ths, root / octave / fifth ------------------------
    for bar in range(COMBAT_BARS):
        ch = COMBAT_PROG[bar]
        root = BASS_ROOT[ch]
        b0 = bar * 4.0
        if ch == "F#" and bar >= 24:
            root = 30
        steps = [(0.0, 0, 0.95), (0.5, 0, 0.75), (1.0, 0, 0.85), (1.5, 0, 0.70),
                 (2.0, 0, 0.95), (2.5, 0, 0.75), (3.0, 7, 0.80), (3.5, 0, 0.72)]
        for off, add, vel in steps:
            f = midi_to_hz(root + add)
            sig = ins.bass_note(f, spb * 0.44 / SR, amp=0.30 * vel, lp=1100.0,
                                sat=1.9, a=0.004, d=0.10, s=0.55, r=0.08)
            mix.add("bass", sig, int(round((b0 + off) * spb)), pan=0.0)

    # --- drums ------------------------------------------------------------
    for bar in range(COMBAT_BARS):
        b0 = bar * 4.0
        half = bar >= 16
        # kick
        kicks = [(0.0, 0.85), (1.5, 0.55), (2.0, 0.80)]
        if bar % 4 == 3:
            kicks.append((3.5, 0.60))
        if half and bar % 2 == 1:
            kicks.append((2.75, 0.45))
        for off, vel in kicks:
            mix.add("drums", ins.kick(dur=0.22, amp=0.72 * vel, f_hi=130.0,
                                      f_lo=46.0, tau=0.11, click=0.20,
                                      drive=2.0, seed=bar * 8 + int(off * 4)),
                    int(round((b0 + off) * spb)), pan=0.0)
        # snare on 2 and 4
        for off, vel in [(1.0, 0.62), (3.0, 0.66)]:
            mix.add("drums", ins.snare(dur=0.19, amp=0.34 * vel, tau=0.06,
                                       noise_tau=0.055, bp=2000.0,
                                       seed=bar * 8 + int(off)),
                    int(round((b0 + off) * spb)), pan=-0.05)
        if bar % 8 == 7:
            mix.add("drums", ins.snare(dur=0.13, amp=0.16, tau=0.04,
                                       noise_tau=0.035, bp=2600.0,
                                       seed=bar + 500),
                    int(round((b0 + 3.75) * spb)), pan=0.12)
        # hats: 8ths, plus 16ths in the back half
        step = 0.25 if half else 0.5
        k = 0
        off = 0.0
        while off < 4.0 - 1e-9:
            acc = 1.0 if abs(off - round(off)) < 1e-6 else 0.62
            openh = (bar % 8 == 7 and abs(off - 3.5) < 1e-6)
            mix.add("drums", ins.hat(dur=0.20 if openh else 0.055,
                                     amp=(0.115 if openh else 0.072) * acc
                                         * (0.85 + 0.3 * rng.random()),
                                     tau=0.10 if openh else 0.022,
                                     seed=bar * 64 + k),
                    int(round((b0 + off) * spb)), pan=0.28)
            off += step
            k += 1

    return mix, dict(name="bgm_combat", bpm=COMBAT_BPM, bars=COMBAT_BARS,
                     bpb=4, key="B minor", prog=COMBAT_PROG,
                     lufs_target=-13.5,
                     balance=dict(arp=dict(m=-16.0, peak=-3.0),
                                  pad=dict(m=-27.0, peak=-14.0),
                                  bass=dict(m=-18.0, peak=-4.0),
                                  drums=dict(m=-17.0, peak=-3.0)),
                     reverb=dict(room=0.72, damp=0.30, predelay_ms=8.0,
                                 ir_s=1.8, wet=1.50, seed=37))


# ---------------------------------------------------------------------------
# track 3 -- bgm_night
# ---------------------------------------------------------------------------
NIGHT_BPM = 66
NIGHT_BARS = 24
NIGHT_PROG = [
    "Dadd9", "Bm7", "Gmaj7", "A",
    "Dadd9", "Gmaj7", "Bm7", "A",
    "Gmaj7", "A", "F#m7", "Bm7",
    "Gmaj7", "A", "Dadd9", "Dadd9",
    "Dadd9", "Bm7", "Gmaj7", "A",
    "Gmaj7", "A", "Dadd9", "Dadd9",
]

# (beat, hold_beats, note, velocity, ratio)
NIGHT_BELL = [
    (2.0, 3.0, "F#5", 0.55, 3.0),
    (5.0, 3.0, "D5", 0.48, 3.0),
    (8.5, 4.0, "A5", 0.52, 3.5),
    (12.0, 3.5, "F#5", 0.44, 3.0),
    (16.0, 4.0, "D6", 0.50, 3.5),
    (20.5, 3.0, "B5", 0.46, 3.0),
    (24.0, 3.5, "A5", 0.50, 3.5),
    (28.0, 4.0, "E5", 0.42, 3.0),
    (32.0, 4.5, "G5", 0.50, 3.5),
    (36.5, 4.0, "A5", 0.46, 3.0),
    (40.0, 4.0, "C#6", 0.44, 3.5),
    (44.5, 4.0, "B5", 0.42, 3.0),
    (48.0, 5.0, "D6", 0.52, 3.5),
    (53.0, 4.0, "A5", 0.44, 3.0),
    (56.0, 5.0, "F#5", 0.46, 3.5),
    (61.0, 4.5, "D5", 0.44, 3.0),
    (64.5, 5.0, "E5", 0.46, 3.5),
    (69.5, 5.0, "F#5", 0.48, 3.0),
    (74.0, 5.5, "A5", 0.50, 3.5),
    (79.0, 4.0, "G5", 0.44, 3.0),
    (82.5, 4.5, "F#5", 0.46, 3.5),
    (87.0, 6.0, "D5", 0.52, 3.0),
]


def build_night():
    spb = SR * 60.0 / NIGHT_BPM
    beats = NIGHT_BARS * 4
    n = int(round(beats * spb))
    mix = Mix(n, seed=33)
    mix.stem("bell", gain=0.90, send=0.52, haas_ms=12.0, haas_gain=0.75)
    mix.stem("pad", gain=0.80, send=0.34, haas_ms=14.0, haas_gain=0.85,
             eq=lambda x: dsp.highpass(x, 110.0))
    mix.stem("wind", gain=0.62, send=0.10)
    mix.stem("bass", gain=0.60, send=0.30,
             eq=lambda x: dsp.highpass(x, 30.0))

    # --- pad: one very slow chord per 2 bars ------------------------------
    for bar in range(0, NIGHT_BARS, 2):
        ch = NIGHT_PROG[bar]
        add_pad(mix, ch, bar * 4.0, 10.0, spb, amp=0.115, lp=1500.0,
                a=1.4, r=2.4, seed=600 + bar, voices=(0, 1, 2, 3),
                pan_spread=0.52, detune=(-8.0, 0.0, 8.0), open_from=520.0)

    # --- sub bass: one note per 2 bars, almost subliminal -----------------
    for bar in range(0, NIGHT_BARS, 2):
        ch = NIGHT_PROG[bar]
        add_bass(mix, ch, bar * 4.0, 7.5, spb, amp=0.17, octave=0,
                 pattern=[(0.0, 7.4, 0)], lp=420.0, sat=1.15)

    # --- bells ------------------------------------------------------------
    for i, (beat, hold, nm, vel, ratio) in enumerate(NIGHT_BELL):
        sig = ins.fm_bell(note_to_hz(nm), hold * spb / SR, amp=0.20 * vel,
                          ratio=ratio, index=2.2, index_tau=0.45, tau=1.05,
                          shimmer=0.20)
        pan = 0.34 if i % 2 == 0 else -0.34
        mix.add("bell", sig, int(round(beat * spb)), pan=pan)

    # --- wind: one continuous texture that is itself loop-length ----------
    w = ins.wind(n, amp=0.30, seed=808, base=460.0, depth=330.0, rate=0.058)
    # gentle slow swell that returns to its start value at the loop point
    t = np.arange(n, dtype=np.float64) / SR
    loop_s = n / SR
    k = max(1, int(round(loop_s / 19.0)))
    swell = 0.80 + 0.20 * np.sin(TAU * k * t / loop_s)
    circ_add(mix.stems["wind"], dsp.pan(w * swell, 0.0), 0)

    return mix, dict(name="bgm_night", bpm=NIGHT_BPM, bars=NIGHT_BARS,
                     bpb=4, key="D major", prog=NIGHT_PROG,
                     lufs_target=-21.0,
                     balance=dict(bell=dict(m=-17.0, peak=-3.0),
                                  pad=dict(m=-31.0, peak=-14.0),
                                  wind=dict(m=-38.0, peak=-24.0),
                                  bass=dict(m=-36.0, peak=-20.0)),
                     reverb=dict(room=0.88, damp=0.55, predelay_ms=28.0,
                                 ir_s=4.0, wet=0.90, seed=41))


BUILDERS = {
    "bgm_farm_day": build_farm_day,
    "bgm_combat": build_combat,
    "bgm_night": build_night,
}
