# -*- coding: utf-8 -*-
"""instruments.py -- every sound in Seedfall is synthesised here, no samples.

  pluck   Karplus-Strong: noise excitation, delay line, low-pass feedback
  pad     2-3 detuned band-limited saws + low-pass + slow attack/release
  bass    sine + a little triangle, mildly saturated
  bell    FM (3:1 and 7:2), fast index decay
  drums   kick (sine glide 120->45 Hz), snare (noise + tonal body), hat (HP noise)
  wind    filtered noise with slow LFO modulation of the cutoff

All functions return a mono float64 array.  Every generator takes an explicit
``seed`` so a rerun produces byte-identical files.
"""

from __future__ import annotations

import math

import numpy as np

from dsp import (
    SR, TAU, adsr, decay_env, exp_curve, lowpass, highpass, bandpass, bell as bell_eq,
    saw_table, nharm_for, osc_table, ramp_env, saw, sine, sine_var, sweep_lowpass,
    sweep_highpass, sweep_bandpass, triangle, db_to_lin,
)

__all__ = [
    "noise", "pluck", "pad_note", "bass_note", "fm_bell", "kick", "snare", "hat",
    "shaker", "wind", "tone_blip", "fm_sweep", "noise_sweep", "click",
]


# ---------------------------------------------------------------------------
def noise(n, seed=0, color="white"):
    """White / pink-ish noise.  Pink is a cheap 3-tap integrator cascade."""
    rng = np.random.default_rng(int(seed) & 0x7FFFFFFF)
    x = rng.standard_normal(int(n))
    if color == "pink":
        b, a = [0.049922035, -0.095993537, 0.050612699, -0.004408786], \
               [1.0, -2.494956002, 2.017265875, -0.522189400]
        # crude one-pole cascade, good enough and stable
        y = x.copy()
        for _ in range(3):
            y = 0.98 * np.concatenate(([0.0], y[:-1])) + 0.02 * y
        return y / max(np.max(np.abs(y)), 1e-12)
    return x


def _norm(x, target=1.0):
    p = float(np.max(np.abs(x))) if x.size else 0.0
    return x * (target / p) if p > 1e-12 else x


# ---------------------------------------------------------------------------
# Karplus-Strong pluck
# ---------------------------------------------------------------------------
def pluck(freq, dur, sr=SR, amp=1.0, t60=1.1, damp=0.55, pick=0.27, pick_coef=0.30,
          saw_mix=0.55, exc_lp_mult=5.0, seed=0, sweep_hi=9000.0, sweep_lo=2400.0,
          release_ms=45.0):
    """Karplus-Strong string.

    The delay line is one period long (minus the half-sample delay of the
    averaging filter, which is why P = round(fs/f - 0.5)); each pass writes the
    buffer and feeds back a damped copy of it.  Because one period is exactly
    one numpy vector, the whole string is synthesised ``n/P`` vector ops instead
    of a per-sample Python loop.

    The excitation is 4 periods of noise (folded -> lower variance) mixed with a
    periodic 6-harmonic ramp scaled by 1/k.  Pure noise leaves each harmonic at
    a random level, which buries the fundamental; the periodic component pins it
    so the note has definite pitch, while the noise keeps the pick transient.

    ``t60`` is the time for the string to fall 60 dB; ``damp`` is how much
    low-pass is applied per pass (0 = bright and buzzy, 1 = dull and short).
    Integer delay line -> tuning is quantised to roughly +-5 cents up to D5.
    """
    dur = max(float(dur), 0.02)
    n = int(round(dur * sr))
    P = int(round(sr / max(freq, 20.0) - 0.5))
    P = max(2, P)
    rng = np.random.default_rng(int(seed) & 0x7FFFFFFF)

    # --- excitation -------------------------------------------------------
    exc = np.zeros(P)
    for _ in range(4):
        exc += rng.standard_normal(P)
    exc /= 4.0
    lp = float(np.clip(exc_lp_mult * freq, 1500.0, 6000.0))
    exc = lowpass(np.concatenate([exc, exc, exc]), lp, sr=sr)[P:2 * P]
    exc /= max(np.max(np.abs(exc)), 1e-9)
    if saw_mix > 0:
        t = np.arange(P, dtype=np.float64) / sr
        per = np.zeros(P)
        for k in range(1, 7):
            per += np.sin(TAU * freq * k * t + rng.random() * TAU) / k
        per /= max(np.max(np.abs(per)), 1e-9)
        exc = (1.0 - saw_mix) * exc + saw_mix * per
    pickd = max(1, int(round(pick * P)))
    if pickd < P:
        exc = exc - pick_coef * np.roll(exc, pickd)
    exc *= np.exp(-np.linspace(0.0, 1.2, P))

    # --- feedback loop ----------------------------------------------------
    decay = 10.0 ** (-3.0 / (max(t60, 0.05) * max(freq, 20.0)))
    decay = float(np.clip(decay, 0.5, 0.99995))
    d = float(np.clip(damp, 0.0, 0.98))

    nb = int(math.ceil(n / P))
    out = np.empty(nb * P)
    buf = exc
    for i in range(nb):
        o = i * P
        out[o:o + P] = buf
        avg = 0.5 * (buf + np.roll(buf, 1))
        buf = decay * ((1.0 - d) * buf + d * avg)
    out = out[:n]

    # brightness decay: the string itself darkens, and we help it along
    if sweep_hi > sweep_lo:
        fc = exp_curve(sweep_hi, sweep_lo, n, shape=2.0)
        out = sweep_lowpass(out, fc, sr, stages=1)

    out *= ramp_env(n, sr, attack_ms=1.2, release_ms=release_ms)
    out = _norm(out, 1.0) * amp
    return out


# ---------------------------------------------------------------------------
# Pad: detuned band-limited saws
# ---------------------------------------------------------------------------
def pad_note(freq, hold, sr=SR, amp=1.0, detune=(-9.0, 0.0, 9.0), lp=2400.0, q=0.5,
             a=0.35, d=0.6, s=0.82, r=1.2, seed=0, open_from=None, phase_spread=True):
    """Warm bed.  Three saws detuned by a few cents each, low-passed, slow ADSR."""
    total = float(hold) + float(r)
    n = int(round(total * sr))
    gate = int(round(float(hold) * sr))
    acc = np.zeros(n)
    for i, c in enumerate(detune):
        f = freq * 2.0 ** (c / 1200.0)
        ph = (0.37 * i + 0.11 * (seed % 7)) if phase_spread else 0.0
        acc += osc_table(f, n, saw_table(nharm_for(f, sr)), phase0=ph % 1.0)
    acc /= max(len(detune), 1)
    acc = lowpass(acc, lp, q, sr)
    if open_from is not None and open_from < lp:
        fc = exp_curve(open_from, lp, n, shape=1.6)
        acc = sweep_lowpass(acc, fc, sr, stages=1)
    env = adsr(n, sr, a=a, d=d, s=s, r=r, gate=gate)
    return _norm(acc * env, 1.0) * amp


# ---------------------------------------------------------------------------
# Bass
# ---------------------------------------------------------------------------
def bass_note(freq, hold, sr=SR, amp=1.0, sub=0.9, tri=0.22, sat=1.35,
              a=0.008, d=0.18, s=0.72, r=0.16, lp=900.0, seed=0):
    """Sine fundamental + a little triangle (gives it a fingertip) + soft saturation."""
    total = float(hold) + float(r)
    n = int(round(total * sr))
    gate = int(round(float(hold) * sr))
    t = np.arange(n, dtype=np.float64) / sr
    sig = sub * np.sin(TAU * freq * t) + tri * triangle(freq, n, sr)
    sig = lowpass(sig, lp, 0.8, sr)
    if sat > 1.0:
        sig = np.tanh(sig * sat) / np.tanh(sat)
    env = adsr(n, sr, a=a, d=d, s=s, r=r, gate=gate)
    return _norm(sig * env, 1.0) * amp


# ---------------------------------------------------------------------------
# FM bell
# ---------------------------------------------------------------------------
def fm_bell(freq, hold, sr=SR, amp=1.0, ratio=3.0, index=2.6, index_tau=0.30,
            tau=0.55, shimmer=0.0, r=0.0):
    """2-operator FM.  3:1 = bell-ish, 7:2 = inharmonic glass."""
    n = int(round((float(hold) + max(r, 0.03)) * sr))
    t = np.arange(n, dtype=np.float64) / sr
    mod_env = index * np.exp(-t / max(index_tau, 1e-3))
    mod = np.sin(TAU * freq * ratio * t) * mod_env
    sig = np.sin(TAU * freq * t + mod)
    if shimmer > 0:
        sig = sig + shimmer * 0.5 * np.sin(TAU * freq * 2.0 * t + mod * 0.5)
    env = np.exp(-t / max(tau, 1e-3))
    na = max(1, int(0.0015 * sr))
    env[:na] *= np.linspace(0.0, 1.0, na, endpoint=False)
    tail = min(n, max(2, int(0.006 * sr)))
    env[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(sig * env, 1.0) * amp


# ---------------------------------------------------------------------------
# Percussion
# ---------------------------------------------------------------------------
def kick(dur=0.34, sr=SR, amp=1.0, f_hi=120.0, f_lo=45.0, sweep_tau=0.035,
         tau=0.16, click=0.22, seed=0, drive=1.6):
    """Sine with a fast pitch envelope 120 -> 45 Hz plus a beater click."""
    n = int(round(dur * sr))
    t = np.arange(n, dtype=np.float64) / sr
    f = f_lo + (f_hi - f_lo) * np.exp(-t / max(sweep_tau, 1e-4))
    body = sine_var(f, n, sr)
    env = np.exp(-t / max(tau, 1e-4))
    env[:max(1, int(0.001 * sr))] *= np.linspace(0.0, 1.0, max(1, int(0.001 * sr)), endpoint=False)
    sig = body * env
    if click > 0:
        cn = max(8, int(0.006 * sr))
        ck = noise(cn, seed) * np.exp(-np.linspace(0.0, 5.0, cn))
        sig[:cn] += click * ck
    if drive > 1.0:
        sig = np.tanh(sig * drive) / np.tanh(drive)
    tail = min(n, max(2, int(0.005 * sr)))
    sig[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(sig, 1.0) * amp


def snare(dur=0.22, sr=SR, amp=1.0, body_hz=185.0, tau=0.075, noise_tau=0.055,
          bp=1900.0, seed=0):
    """Noise through a band-pass + a short tonal body."""
    n = int(round(dur * sr))
    t = np.arange(n, dtype=np.float64) / sr
    nz = noise(n, seed)
    nz = bandpass(nz, bp, 0.8, sr)
    nen = np.exp(-t / max(noise_tau, 1e-3))
    body = np.sin(TAU * body_hz * t) * np.exp(-t / max(tau, 1e-3))
    sig = 0.75 * nz * nen + 0.6 * body
    tail = min(n, max(2, int(0.004 * sr)))
    sig[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(sig, 1.0) * amp


def hat(dur=0.06, sr=SR, amp=1.0, hp=7200.0, tau=0.03, seed=0, metallic=0.35):
    """High-passed noise, plus a touch of square-cluster metallic ring."""
    n = int(round(dur * sr))
    t = np.arange(n, dtype=np.float64) / sr
    nz = noise(n, seed)
    nz = highpass(nz, hp, 0.7, sr)
    if metallic > 0:
        f = 1.0 / max(tau, 1e-3)
        met = (np.sin(TAU * 3140.0 * t) + np.sin(TAU * 4730.0 * t)
               + np.sin(TAU * 6310.0 * t) + np.sin(TAU * 8110.0 * t))
        nz = nz + metallic * met / 4.0
    env = decay_env(n, sr, tau_ms=tau * 1000.0, attack_ms=0.3)
    tail = min(n, max(2, int(0.002 * sr)))
    env[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(nz * env, 1.0) * amp


def shaker(dur=0.09, sr=SR, amp=1.0, bp=5200.0, tau=0.035, seed=0):
    n = int(round(dur * sr))
    nz = noise(n, seed)
    nz = bandpass(highpass(nz, 2600.0, 0.7, sr), bp, 1.2, sr)
    env = decay_env(n, sr, tau_ms=tau * 1000.0, attack_ms=1.5)
    tail = min(n, max(2, int(0.002 * sr)))
    env[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(nz * env, 1.0) * amp


# ---------------------------------------------------------------------------
# Wind / ambience
# ---------------------------------------------------------------------------
def wind(n, sr=SR, amp=1.0, seed=0, base=520.0, depth=380.0, rate=0.071, stages=2):
    """Filtered noise whose cutoff is moved by two slow, incommensurate LFOs."""
    n = int(n)
    t = np.arange(n, dtype=np.float64) / sr
    lfo = (np.sin(TAU * rate * t) * 0.65
           + np.sin(TAU * rate * 1.618 * t + 1.3) * 0.35
           + np.sin(TAU * rate * 0.413 * t + 2.6) * 0.5)
    fc = np.clip(base + depth * lfo, 120.0, 2400.0)
    nz = noise(n, seed)
    nz = sweep_lowpass(nz, fc, sr, stages=stages)
    nz = sweep_highpass(nz, np.clip(fc * 0.22, 60.0, 600.0), sr, stages=1)
    # breath: very slow amplitude swell
    swell = 0.72 + 0.28 * (0.5 + 0.5 * np.sin(TAU * rate * 0.37 * t + 0.7))
    nz = nz * swell
    return _norm(nz, 1.0) * amp


# ---------------------------------------------------------------------------
# shared one-shot helpers used by the SFX recipes
# ---------------------------------------------------------------------------
def tone_blip(dur, sr=SR, amp=1.0, freq=880.0, freq_end=None, shape=1.0,
              tau=0.08, tri=0.0, attack_ms=1.5):
    """A short pitched blip, optionally gliding, optionally with triangle body."""
    n = int(round(dur * sr))
    if freq_end is None or abs(freq_end - freq) < 1e-6:
        cur = np.full(n, float(freq))
    else:
        cur = exp_curve(freq, max(freq_end, 20.0), n, shape=shape)
    sig = sine_var(cur, n, sr)
    if tri > 0:
        sig = (1.0 - tri) * sig + tri * triangle(float(np.mean(cur)), n, sr)
    env = decay_env(n, sr, tau_ms=tau * 1000.0, attack_ms=attack_ms)
    tail = min(n, max(2, int(0.003 * sr)))
    env[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(sig * env, 1.0) * amp


def fm_sweep(dur, sr=SR, amp=1.0, carrier=1200.0, carrier_end=None, ratio=2.0,
             index=3.0, tau=0.09, shape=1.0, attack_ms=0.6):
    """FM voice whose carrier and modulation index both move."""
    n = int(round(dur * sr))
    if carrier_end is None or abs(carrier_end - carrier) < 1e-6:
        f = np.full(n, float(carrier))
    else:
        f = exp_curve(carrier, max(carrier_end, 20.0), n, shape=shape)
    ph = np.cumsum(f) / sr
    idx = index * np.exp(-np.arange(n) / max(tau * sr, 1.0))
    sig = np.sin(TAU * ph + idx * np.sin(TAU * ph * ratio))
    env = decay_env(n, sr, tau_ms=tau * 1000.0, attack_ms=attack_ms)
    tail = min(n, max(2, int(0.003 * sr)))
    env[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(sig * env, 1.0) * amp


def noise_sweep(dur, sr=SR, amp=1.0, f_start=2000.0, f_end=600.0, width=2.2,
                tau=0.12, seed=0, stages=1, color="white", attack_ms=1.0):
    """The whoosh: noise pushed through a band-pass whose centre sweeps."""
    n = int(round(dur * sr))
    nz = noise(n, seed, color)
    fc = exp_curve(f_start, max(f_end, 40.0), n, shape=1.4)
    nz = sweep_bandpass(nz, fc, q_width=width, sr=sr, stages=stages)
    env = decay_env(n, sr, tau_ms=tau * 1000.0, attack_ms=attack_ms)
    tail = min(n, max(2, int(0.003 * sr)))
    env[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(nz * env, 1.0) * amp


def click(dur=0.04, sr=SR, amp=1.0, bp=2600.0, hz=1500.0, seed=0, wood=1.0,
          tau=0.012):
    """Wooden click: band-passed noise click + a very short sine body."""
    n = int(round(dur * sr))
    t = np.arange(n, dtype=np.float64) / sr
    nz = bandpass(noise(n, seed), bp, 1.4, sr)
    nz *= np.exp(-t / max(tau, 1e-4))
    body = np.sin(TAU * hz * t) * np.exp(-t / max(tau * 0.9, 1e-4)) * wood
    sig = nz + body
    tail = min(n, max(2, int(0.0015 * sr)))
    sig[n - tail:] *= np.linspace(1.0, 0.0, tail)
    return _norm(sig, 1.0) * amp
