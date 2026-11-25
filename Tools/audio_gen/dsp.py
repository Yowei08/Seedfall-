# -*- coding: utf-8 -*-
"""dsp.py -- numpy-only DSP toolkit for Seedfall 音频生成.

No scipy on this machine.  Everything below is implemented from scratch:

  * ADSR envelopes (exponential segments, 1 ms minimum attack)
  * one-pole and RBJ biquad filters, applied either
      - statically, through an exact FFT evaluation of H(z), or
      - time-variantly, through a block-wise closed-form recursion
  * filter *sweeps* (pluck brightness decay, whooshes, wind LFO)
  * a Freeverb-style reverb whose impulse response is generated here and
    applied by circular (loop-safe) or linear FFT convolution
  * constant-power panning and Haas widening
  * Gil-Werman sliding max/min -> look-ahead limiter and compressor
  * ITU-R BS.1770-4 K-weighting + gated integrated loudness (LUFS)
  * 16-bit stereo WAV writer with TPDF dither and a hard -1 dBFS ceiling

All arrays are float64.  Stereo is shape (n, 2); mono is shape (n,).
"""

from __future__ import annotations

import math
import wave

import numpy as np

SR = 44100
TAU = 2.0 * math.pi

# 16-bit full-scale reference used by Unity (samples are i16 / 32768).
I16 = 32768.0


# ---------------------------------------------------------------------------
# conversions
# ---------------------------------------------------------------------------
def db_to_lin(db: float) -> float:
    return 10.0 ** (db / 20.0)


def lin_to_db(v) -> float:
    v = abs(float(v))
    return 20.0 * math.log10(max(v, 1e-15))


_PC = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def note_to_midi(name: str) -> int:
    """'D4' -> 62, 'F#3' -> 54, 'Bb2' -> 46."""
    s = name.strip()
    pc = _PC[s[0].upper()]
    i = 1
    while i < len(s) and s[i] in "#b":
        pc += 1 if s[i] == "#" else -1
        i += 1
    return 12 * (int(s[i:]) + 1) + pc


def note_to_hz(name: str) -> float:
    return midi_to_hz(note_to_midi(name))


def midi_to_hz(m: float) -> float:
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


def cents_ratio(cents: float) -> float:
    return 2.0 ** (cents / 1200.0)


# ---------------------------------------------------------------------------
# basic oscillators (mono)
# ---------------------------------------------------------------------------
def sine(freq, n, sr=SR, phase=0.0):
    t = np.arange(n, dtype=np.float64) / sr
    return np.sin(TAU * freq * t + phase)


def sine_var(freq_curve, n, sr=SR):
    """Sine whose instantaneous frequency follows a per-sample curve."""
    ph = np.cumsum(np.asarray(freq_curve, dtype=np.float64)) / sr
    return np.sin(TAU * ph)


def triangle(freq, n, sr=SR):
    t = np.arange(n, dtype=np.float64) / sr
    return 2.0 / math.pi * np.arcsin(np.sin(TAU * freq * t))


_TABLE_CACHE = {}


def saw_table(nharm, size=2048):
    """Band-limited saw, one cycle, additive so it never aliases by itself."""
    key = (int(nharm), int(size))
    tab = _TABLE_CACHE.get(key)
    if tab is not None:
        return tab
    t = np.arange(size, dtype=np.float64) / size
    tab = np.zeros(size)
    for k in range(1, int(nharm) + 1):
        tab += np.sin(TAU * k * t) / k
    peak = np.max(np.abs(tab))
    if peak > 0:
        tab /= peak
    _TABLE_CACHE[key] = tab
    return tab


def nharm_for(freq, sr=SR, cap=48):
    """Harmonics that stay under Nyquist for this pitch."""
    return int(max(1, min(cap, math.floor(0.45 * sr / max(freq, 1.0)))))


def osc_table(freq, n, table, phase0=0.0):
    """Read a one-cycle table at a fractional rate -> band-limited saw."""
    size = table.size
    ph = (np.arange(n, dtype=np.float64) * (freq / SR) + phase0) % 1.0
    idx = ph * size
    i0 = idx.astype(np.int64) % size
    frac = idx - np.floor(idx)
    i1 = (i0 + 1) % size
    return table[i0] * (1.0 - frac) + table[i1] * frac


def saw(freq, n, nharm=None, phase0=0.0):
    if nharm is None:
        nharm = nharm_for(freq)
    return osc_table(freq, n, saw_table(nharm), phase0)


# ---------------------------------------------------------------------------
# envelopes
# ---------------------------------------------------------------------------
def adsr(n, sr=SR, a=0.005, d=0.08, s=0.7, r=0.20, gate=None):
    """Per-note ADSR.  Attack is exponential with a hard 1 ms floor."""
    n = int(n)
    if n <= 0:
        return np.zeros(0)
    a = max(int(1e-3 * sr), int(round(a * sr)))
    d = max(int(1e-3 * sr), int(round(d * sr)))
    r = max(int(2e-3 * sr), int(round(r * sr)))
    if gate is None:
        gate = n
    gate = int(min(max(gate, 1), n))

    env = np.zeros(n)
    # attack
    na = min(a, gate)
    if na > 0:
        u = np.arange(na, dtype=np.float64) / na
        env[:na] = (1.0 - np.exp(-4.6 * u)) / (1.0 - math.exp(-4.6))
    # decay to sustain
    ds = min(gate - na, d)
    if ds > 0:
        u = np.arange(ds, dtype=np.float64) / ds
        env[na:na + ds] = s + (1.0 - s) * np.exp(-4.6 * u)
    # sustain
    ss = max(0, gate - na - ds)
    if ss > 0:
        env[na + ds:na + ds + ss] = s
    # release
    rel_start = gate
    lvl = env[rel_start - 1] if rel_start > 0 else 0.0
    nr = n - rel_start
    if nr > 0:
        u = np.arange(nr, dtype=np.float64) / max(r, 1)
        env[rel_start:] = lvl * np.exp(-6.9 * np.minimum(u, 4.0))
    # tail guard: the last millisecond always lands on exactly zero
    tail = min(n, max(2, int(1e-3 * sr)))
    env[n - tail:] *= np.linspace(1.0, 0.0, tail, endpoint=True)
    return np.maximum(env, 0.0)


def ramp_env(n, sr=SR, attack_ms=1.0, release_ms=8.0, hold=1.0):
    """Flat body with a short anti-click attack and release (plucks, perc)."""
    n = int(n)
    env = np.full(n, float(hold), dtype=np.float64)
    na = int(round(attack_ms * 1e-3 * sr))
    nr = int(round(release_ms * 1e-3 * sr))
    na = min(na, n)
    nr = min(nr, n)
    if na > 0:
        env[:na] *= np.linspace(0.0, 1.0, na, endpoint=False)
    if nr > 0:
        env[n - nr:] *= np.linspace(1.0, 0.0, nr, endpoint=True)
    return env


def decay_env(n, sr=SR, tau_ms=100.0, attack_ms=0.5):
    """Exponential decay (percussion, plucks, blast tails)."""
    n = int(n)
    t = np.arange(n, dtype=np.float64) / sr
    env = np.exp(-t / max(tau_ms * 1e-3, 1e-6))
    na = min(int(round(attack_ms * 1e-3 * sr)), n)
    if na > 0:
        env[:na] *= np.linspace(0.0, 1.0, na, endpoint=False)
    return env


# ---------------------------------------------------------------------------
# filters -- static, exact, via the frequency response on the FFT grid
# ---------------------------------------------------------------------------
def _resp(b, a, zinv):
    """H(z) = sum b_k z^-k / sum a_k z^-k evaluated on the unit circle."""
    num = np.zeros_like(zinv)
    for k, c in enumerate(b):
        if c:
            num = num + c * zinv ** k
    den = np.zeros_like(zinv)
    for k, c in enumerate(a):
        if c:
            den = den + c * zinv ** k
    return num / den


def _next_pow2(n):
    return 1 << int(math.ceil(math.log2(max(int(n), 2))))


def iir(x, b, a, pad_ms=120.0, sr=SR, circular=False):
    """Apply an IIR filter by multiplying its exact response on the FFT grid.

    ``circular=True`` filters at exactly the signal length, so the impulse
    response wraps around instead of being cut -- which is what a seamless loop
    needs.  Otherwise the tail is zero-padded by ``pad_ms`` so the (finite)
    wrap-around error is well below -96 dB for the filters used here.
    """
    x = np.asarray(x, dtype=np.float64)
    if x.ndim == 2:
        return np.stack([iir(x[:, c], b, a, pad_ms, sr, circular)
                         for c in range(x.shape[1])], axis=1)
    n = x.size
    if n == 0:
        return x.copy()
    N = n if circular else _next_pow2(n + int(sr * pad_ms / 1000.0) + 8)
    X = np.fft.rfft(x, N)
    w = TAU * np.arange(N // 2 + 1) / N
    H = _resp(b, a, np.exp(-1j * w))
    return np.fft.irfft(X * H, N)[:n]


# --- RBJ cookbook coefficients ------------------------------------------------
def _rbj(b, a):
    b = np.asarray(b, dtype=np.float64)
    a = np.asarray(a, dtype=np.float64)
    return b / a[0], a / a[0]


def lp_coeffs(fc, q=0.70710678, sr=SR):
    w0 = TAU * min(max(fc, 1.0), 0.49 * sr) / sr
    cw, sw = math.cos(w0), math.sin(w0)
    al = sw / (2.0 * q)
    b = [(1 - cw) / 2, 1 - cw, (1 - cw) / 2]
    a = [1 + al, -2 * cw, 1 - al]
    return _rbj(b, a)


def hp_coeffs(fc, q=0.70710678, sr=SR):
    w0 = TAU * min(max(fc, 1.0), 0.49 * sr) / sr
    cw, sw = math.cos(w0), math.sin(w0)
    al = sw / (2.0 * q)
    b = [(1 + cw) / 2, -(1 + cw), (1 + cw) / 2]
    a = [1 + al, -2 * cw, 1 - al]
    return _rbj(b, a)


def bp_coeffs(fc, q=1.0, sr=SR):
    w0 = TAU * min(max(fc, 1.0), 0.49 * sr) / sr
    cw, sw = math.cos(w0), math.sin(w0)
    al = sw / (2.0 * q)
    b = [al, 0.0, -al]
    a = [1 + al, -2 * cw, 1 - al]
    return _rbj(b, a)


def peak_coeffs(fc, q=1.0, gain_db=0.0, sr=SR):
    A = 10.0 ** (gain_db / 40.0)
    w0 = TAU * min(max(fc, 1.0), 0.49 * sr) / sr
    cw, sw = math.cos(w0), math.sin(w0)
    al = sw / (2.0 * q)
    b = [1 + al * A, -2 * cw, 1 - al * A]
    a = [1 + al / A, -2 * cw, 1 - al / A]
    return _rbj(b, a)


def low_shelf(fc, gain_db, sr=SR, s=1.0):
    A = 10.0 ** (gain_db / 40.0)
    w0 = TAU * min(max(fc, 1.0), 0.49 * sr) / sr
    cw, sw = math.cos(w0), math.sin(w0)
    al = sw / 2.0 * math.sqrt((A + 1 / A) * (1 / s - 1) + 2)
    tsa = 2 * math.sqrt(A) * al
    b = [A * ((A + 1) - (A - 1) * cw + tsa),
         2 * A * ((A - 1) - (A + 1) * cw),
         A * ((A + 1) - (A - 1) * cw - tsa)]
    a = [(A + 1) + (A - 1) * cw + tsa,
         -2 * ((A - 1) + (A + 1) * cw),
         (A + 1) + (A - 1) * cw - tsa]
    return _rbj(b, a)


def lowpass(x, fc, q=0.70710678, sr=SR, circular=False):
    return iir(x, *lp_coeffs(fc, q, sr), sr=sr, circular=circular)


def highpass(x, fc, q=0.70710678, sr=SR, circular=False):
    return iir(x, *hp_coeffs(fc, q, sr), sr=sr, circular=circular)


def bandpass(x, fc, q=1.0, sr=SR, circular=False):
    return iir(x, *bp_coeffs(fc, q, sr), sr=sr, circular=circular)


def bell(x, fc, gain_db, q=1.0, sr=SR, circular=False):
    return iir(x, *peak_coeffs(fc, q, gain_db, sr), sr=sr, circular=circular)


def dc_block(x, fc=18.0, sr=SR, circular=False):
    """Static 18 Hz high pass -- kills DC and sub-sonic rumble."""
    return iir(x, *hp_coeffs(fc, 0.707, sr), sr=sr, circular=circular)


# ---------------------------------------------------------------------------
# filters -- time-varying (filter sweeps), block-wise closed form
# ---------------------------------------------------------------------------
def _sweep_onepole(x, fc, sr=SR, stages=1, block=128):
    """Cascade of one-pole low-passes with a per-sample cutoff curve.

    y[n] = a*y[n-1] + (1-a)*x[n] is solved in closed form inside each block
    (``a`` held constant per block), which keeps the whole thing vectorised:
    y[n] = a^(n+1)*y[-1] + conv((1-a)x, [a^0..a^(L-1)])[n]
    """
    x = np.asarray(x, dtype=np.float64)
    n = x.size
    if n == 0:
        return x.copy()
    fc = np.asarray(fc, dtype=np.float64)
    if fc.size == 1:
        fc = np.full(n, float(fc))
    fc = np.clip(fc, 20.0, 0.45 * sr)
    out = x.copy()
    for _ in range(max(1, stages)):
        y = np.empty(n)
        state = 0.0
        for i0 in range(0, n, block):
            i1 = min(i0 + block, n)
            L = i1 - i0
            a = math.exp(-TAU * float(fc[i0:i1].mean()) / sr)
            pows = a ** np.arange(L, dtype=np.float64)
            seg = np.convolve((1.0 - a) * out[i0:i1], pows)[:L]
            seg += (pows * a) * state
            y[i0:i1] = seg
            state = float(seg[-1])
        out = y
    return out


def sweep_lowpass(x, fc, sr=SR, stages=1, block=128):
    return _sweep_onepole(x, fc, sr, stages, block)


def sweep_highpass(x, fc, sr=SR, stages=1, block=128):
    x = np.asarray(x, dtype=np.float64)
    return x - _sweep_onepole(x, fc, sr, stages, block)


def sweep_bandpass(x, fc, q_width=2.2, sr=SR, stages=1, block=128):
    """High-pass then low-pass at related cutoffs -> moving band."""
    fc = np.asarray(fc, dtype=np.float64)
    lo = np.clip(fc / q_width, 20.0, 0.45 * sr)
    hi = np.clip(fc * q_width, 25.0, 0.45 * sr)
    return _sweep_onepole(sweep_highpass(x, lo, sr, stages, block), hi, sr, stages, block)


def exp_curve(start, end, n, shape=3.0):
    """Geometric glide from start to end over n samples (filter sweeps)."""
    u = np.linspace(0.0, 1.0, int(n))
    u = u ** shape
    return start * (end / start) ** u


# ---------------------------------------------------------------------------
# sliding statistics (Gil-Werman) -> limiter / compressor
# ---------------------------------------------------------------------------
def _slide(x, k, is_max=True):
    """Exact sliding window max/min, O(n), via the Gil-Werman block trick."""
    x = np.ascontiguousarray(x, dtype=np.float64)
    n = x.size
    k = int(k)
    if k <= 1 or n == 0:
        return x.copy()
    if k % 2 == 0:
        k += 1
    fill = -np.inf if is_max else np.inf
    left = k // 2
    right = k - 1 - left
    xp = np.concatenate((np.full(left, fill), x, np.full(right, fill)))
    m = int(math.ceil(xp.size / k))
    xp2 = np.concatenate((xp, np.full(m * k - xp.size, fill))).reshape(m, k)
    if is_max:
        fwd = np.maximum.accumulate(xp2, axis=1).ravel()
        bwd = np.maximum.accumulate(xp2[:, ::-1], axis=1)[:, ::-1].ravel()
    else:
        fwd = np.minimum.accumulate(xp2, axis=1).ravel()
        bwd = np.minimum.accumulate(xp2[:, ::-1], axis=1)[:, ::-1].ravel()
    return (bwd[:n], fwd)  # caller stitches; see slide_max/slide_min


def slide_max(x, k):
    n = np.asarray(x).size
    k = int(k) | 1
    bwd, fwd = _slide(x, k, True)
    shift = k - 1
    return np.maximum(bwd, fwd[shift:shift + n])


def slide_min(x, k):
    n = np.asarray(x).size
    k = int(k) | 1
    bwd, fwd = _slide(x, k, False)
    shift = k - 1
    return np.minimum(bwd, fwd[shift:shift + n])


def smooth_gain(g, ms, sr=SR, circular=False):
    """One-pole smoothing of a gain curve (uses the exact static one-pole).

    The non-circular branch warms the filter up on ``g[0]`` before the real
    signal, otherwise the one-pole starts from zero and ducks the first
    milliseconds of every one-shot by several dB.
    """
    g = np.asarray(g, dtype=np.float64)
    if g.size == 0:
        return g.copy()
    a = math.exp(-1.0 / (max(ms, 1e-3) * 1e-3 * sr))
    b = np.array([1.0 - a])
    den = np.array([1.0, -a])
    if circular:
        return iir(g, b, den, sr=sr, circular=True)
    warm = max(16, int(5.0 * max(ms, 1e-3) * 1e-3 * sr))
    gp = np.concatenate([np.full(warm, float(g[0])), g])
    y = iir(gp, b, den, pad_ms=200.0, sr=sr)
    return y[warm:warm + g.size]


def momentary_max_db(x, sr=SR, win_s=0.4):
    """Loudest 400 ms RMS window, in dB -- 'how loud is this when it plays'.

    Used to balance stems against each other: a sustained bass has a high RMS
    over the whole loop while a sparse pluck melody has a low one, so whole-loop
    RMS is the wrong trim reference.
    """
    x = np.asarray(x, dtype=np.float64)
    m = x if x.ndim == 1 else x.mean(axis=1)
    e = m * m
    w = max(1, int(round(win_s * sr)))
    cs = np.concatenate(([0.0], np.cumsum(e)))
    if e.size <= w:
        ms = float(cs[-1]) / e.size
    else:
        seg = (cs[w:] - cs[:-w]) / w
        ms = float(np.max(seg))
    return 10.0 * math.log10(max(ms, 1e-20))


def _delay(x, d, circular):
    d = int(d)
    if d == 0:
        return x.copy()
    if circular:
        return np.roll(x, d, axis=0)
    out = np.zeros_like(x)
    if d < x.shape[0]:
        out[d:] = x[:x.shape[0] - d]
    return out


# ---------------------------------------------------------------------------
# dynamics
# ---------------------------------------------------------------------------
def soft_clip(x, drive=1.0, bias=0.0):
    """tanh glue.  drive=1 is transparent-ish, drive>1 adds gentle harmonics."""
    if drive <= 0:
        return x
    peak = np.max(np.abs(x))
    if peak <= 0:
        return x
    return np.tanh(x / peak * drive) * peak / math.tanh(drive) * (1.0 - bias) + bias * peak


def limiter(x, ceiling_db=-1.0, lookahead_ms=3.0, release_ms=120.0, sr=SR, circular=False):
    """Look-ahead brick-wall limiter.

    gain_need = ceiling / |x|  ->  slide_min over the look-ahead window (so the
    gain is already down when the transient arrives) -> one-pole smoothing for
    the release -> min() with the unsmoothed curve so the attack never over-
    shoots.  The signal is delayed by the same amount to stay sample-aligned.
    """
    x = np.asarray(x, dtype=np.float64)
    mono = x.ndim > 1
    if not mono:
        x = x[:, None]
    n = x.shape[0]
    ceil = db_to_lin(ceiling_db)
    la = max(3, int(lookahead_ms * 1e-3 * sr)) | 1

    peak = np.max(np.abs(x), axis=1)
    env = slide_max(peak, la)
    need = np.where(env > ceil, ceil / np.maximum(env, 1e-12), 1.0)
    gmin = slide_min(need, int(la * 1.5) | 1)
    gsm = smooth_gain(gmin, release_ms, sr, circular)
    g = np.minimum(gsm, gmin)
    g = np.clip(g, 0.0, 1.0)

    xd = _delay(x, la // 2, circular)
    out = xd * g[:, None]
    return out[:, 0] if not mono else out


def compress(x, thresh_db=-18.0, ratio=3.0, lookahead_ms=2.0, release_ms=120.0,
             sr=SR, circular=False):
    """Feed-forward peak compressor with the same envelope machinery."""
    x = np.asarray(x, dtype=np.float64)
    mono = x.ndim == 1
    if mono:
        x = x[:, None]
    ceil = db_to_lin(thresh_db)
    la = max(3, int(lookahead_ms * 1e-3 * sr)) | 1
    peak = np.max(np.abs(x), axis=1)
    env = slide_max(peak, la)
    over = np.maximum(env, 1e-12) / ceil
    gain = np.where(over > 1.0, over ** (1.0 / ratio - 1.0), 1.0)
    gsm = smooth_gain(gain, release_ms, sr, circular)
    g = np.minimum(gsm, gain)
    g = np.clip(g, 0.0, 1.0)
    out = _delay(x, la // 2, circular) * g[:, None]
    return out[:, 0] if mono else out


# ---------------------------------------------------------------------------
# stereo
# ---------------------------------------------------------------------------
def pan(x, p):
    """Constant-power pan of a mono signal -> (n, 2)."""
    p = float(np.clip(p, -1.0, 1.0))
    th = (p + 1.0) * math.pi / 4.0
    x = np.asarray(x, dtype=np.float64)
    return np.stack([x * math.cos(th), x * math.sin(th)], axis=1)


def haas(x, ms=9.0, gain=1.0, side="right", sr=SR, circular=False):
    """Delay one channel slightly -> width without comb-filtering the centre."""
    y = np.array(x, dtype=np.float64, copy=True)
    if y.ndim == 1:
        y = np.stack([y, y], axis=1)
    d = int(round(ms * 1e-3 * sr))
    if side == "right":
        y[:, 1] = _delay(y[:, 1], d, circular) * gain
    elif side == "left":
        y[:, 0] = _delay(y[:, 0], d, circular) * gain
    else:  # opposite, symmetric
        y[:, 0] = _delay(y[:, 0], d, circular)
        y[:, 1] = _delay(y[:, 1], d, circular)
    return y


def to_stereo(x):
    x = np.asarray(x, dtype=np.float64)
    if x.ndim == 2:
        return x
    return np.stack([x, x], axis=1)


def to_mono(x):
    x = np.asarray(x, dtype=np.float64)
    return x if x.ndim == 1 else x.mean(axis=1)


# ---------------------------------------------------------------------------
# reverb -- Freeverb-style impulse response + convolution
# ---------------------------------------------------------------------------
_COMB_T = (1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617)
_AP_T = (556, 441, 341, 225)
_SPREAD = 23
_IR_CACHE = {}


def freeverb_ir(n, sr=SR, room=0.84, damp=0.35, spread=1, seed=7,
                predelay_ms=0.0, loops=1.0):
    """Build a Freeverb impulse response: 8 damped combs -> 4 Schroeder allpass.

    ``damp`` is the one-pole low-pass inside each comb feedback path (that is
    what makes the tail get darker as it decays), ``room`` is the comb
    feedback (decay time), ``spread`` shifts the delay lines to decorrelate
    the right channel from the left.
    """
    key = (int(n), sr, round(room, 4), round(damp, 4), spread, seed,
           round(predelay_ms, 3), loops)
    hit = _IR_CACHE.get(key)
    if hit is not None:
        return hit

    n = int(n)
    rng = np.random.default_rng(seed)
    exc_len = max(8, int(0.003 * sr))
    exc = rng.standard_normal(exc_len) * np.exp(-np.linspace(0.0, 5.0, exc_len))
    src = [0.0] * n
    pre = int(predelay_ms * 1e-3 * sr)
    m = min(exc_len, max(0, n - pre))
    if m > 0:
        src[pre:pre + m] = exc[:m].tolist()

    acc = [0.0] * n
    for t in _COMB_T:
        d = int(t * sr / 44100.0) + spread * _SPREAD
        if d < 2 or d >= n:
            continue
        buf = [0.0] * d
        filt = 0.0
        idx = 0
        g = room
        k = 1.0 - damp
        for i in range(n):
            y = buf[idx]
            filt = y * k + filt * damp
            buf[idx] = src[i] + filt * g
            acc[i] += y
            idx += 1
            if idx >= d:
                idx = 0
    scale = 1.0 / len(_COMB_T)
    sig = [v * scale for v in acc]
    del acc

    g = 0.5
    for t in _AP_T:
        d = int(t * sr / 44100.0) + (spread * (_SPREAD // 2))
        if d < 2 or d >= n:
            continue
        buf = [0.0] * d
        out = [0.0] * n
        idx = 0
        for i in range(n):
            vd = buf[idx]
            v = sig[i] + g * vd
            buf[idx] = v
            out[i] = vd - g * v
            idx += 1
            if idx >= d:
                idx = 0
        sig = out

    sig = np.array(sig, dtype=np.float64)
    energy = float(np.sum(sig * sig))
    if energy > 0:
        sig /= math.sqrt(energy)      # unit energy: convolution is energy-preserving
    _IR_CACHE[key] = sig
    return sig


def circular_convolve(x, h):
    """Convolution that wraps, so a loop stays a loop."""
    x = np.asarray(x, dtype=np.float64)
    N = x.size
    hh = np.asarray(h, dtype=np.float64)[:N]
    if hh.size < N:
        hh = np.concatenate([hh, np.zeros(N - hh.size)])
    return np.fft.irfft(np.fft.rfft(x, N) * np.fft.rfft(hh, N), N)


def linear_convolve(x, h):
    x = np.asarray(x, dtype=np.float64)
    h = np.asarray(h, dtype=np.float64)
    n = x.size + h.size - 1
    N = _next_pow2(n)
    return np.fft.irfft(np.fft.rfft(x, N) * np.fft.rfft(h, N), N)[:n]


# ---------------------------------------------------------------------------
# loudness -- ITU-R BS.1770-4
# ---------------------------------------------------------------------------
def _k_coeffs(sr=SR):
    """ITU-R BS.1770-4 K-weighting, re-derived for any sample rate.

    This is the bilinear design published alongside the standard (the same one
    libebur128/ffmpeg use).  Checked against the coefficient table printed in
    the standard for 48 kHz: identical to 1e-14 (see _selftest).
    """
    # stage 1: high shelf ("pre-filter")
    f0, G, Q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    K = math.tan(math.pi * f0 / sr)
    Vh = 10.0 ** (G / 20.0)
    Vb = Vh ** 0.4996667741545416
    a0 = 1.0 + K / Q + K * K
    b1 = np.array([(Vh + Vb * K / Q + K * K) / a0,
                   2.0 * (K * K - Vh) / a0,
                   (Vh - Vb * K / Q + K * K) / a0])
    a1 = np.array([1.0, 2.0 * (K * K - 1.0) / a0, (1.0 - K / Q + K * K) / a0])
    # stage 2: RLB high pass
    f0, Q = 38.13547087602444, 0.5003270373238773
    K = math.tan(math.pi * f0 / sr)
    a0 = 1.0 + K / Q + K * K
    b2 = np.array([1.0 / a0, -2.0 / a0, 1.0 / a0])
    a2 = np.array([1.0, 2.0 * (K * K - 1.0) / a0, (1.0 - K / Q + K * K) / a0])
    return (b1, a1), (b2, a2)


_K_COEFFS = _k_coeffs(SR)


def _k_weight_1d(x, sr):
    n = x.size
    N = _next_pow2(n)
    w = TAU * np.arange(N // 2 + 1) / N
    zinv = np.exp(-1j * w)
    (b1, a1), (b2, a2) = _K_COEFFS if sr == SR else _k_coeffs(sr)
    H = _resp(b1, a1, zinv) * _resp(b2, a2, zinv)
    return np.fft.irfft(np.fft.rfft(x, N) * H, N)[:n]


def k_weight(x, sr=SR):
    """K-weighting (both stages, both channels) in one FFT pass per channel."""
    x = np.asarray(x, dtype=np.float64)
    if x.ndim == 1:
        return _k_weight_1d(x, sr)
    return np.stack([_k_weight_1d(x[:, c], sr) for c in range(x.shape[1])], axis=1)


def _z_blocks(y, sr=SR, win_s=0.400, hop_s=0.100):
    """Per-block z = mean over samples of SUM over channels of y^2 (BS.1770)."""
    if y.ndim == 1:
        e = y ** 2
    else:
        e = np.sum(y ** 2, axis=1)
    n = e.shape[0]
    w = max(1, int(round(win_s * sr)))
    hop = max(1, int(round(hop_s * sr)))
    if n < w:
        w = n
        hop = n
    starts = np.arange(0, max(n - w, 0) + 1, hop, dtype=np.int64)
    if starts.size == 0:
        starts = np.array([0], dtype=np.int64)
    cs = np.concatenate(([0.0], np.cumsum(e)))
    return (cs[starts + w] - cs[starts]) / w


def lufs_integrated(x, sr=SR):
    """Gated integrated loudness (BS.1770-4) for a stereo (n,2) signal."""
    y = k_weight(to_stereo(x), sr)
    ms = _z_blocks(y, sr)
    if ms.size == 0 or not np.any(ms > 0):
        return -70.0
    with np.errstate(divide="ignore"):
        bl = -0.691 + 10.0 * np.log10(np.maximum(ms, 1e-20))
    keep = bl > -70.0
    if not np.any(keep):
        return -70.0
    rel = -0.691 + 10.0 * math.log10(float(np.mean(ms[keep]))) - 10.0
    keep2 = keep & (bl > rel)
    if not np.any(keep2):
        keep2 = keep
    return -0.691 + 10.0 * math.log10(max(float(np.mean(ms[keep2])), 1e-20))


def lufs_full(x, sr=SR):
    """Ungated K-weighted loudness of the whole file (used for one-shots)."""
    y = k_weight(to_stereo(x), sr)
    ms = float(np.mean(np.sum(y ** 2, axis=1)))
    return -0.691 + 10.0 * math.log10(max(ms, 1e-20))


# ---------------------------------------------------------------------------
# metering / normalisation
# ---------------------------------------------------------------------------
def peak_dbfs(x, ref=I16):
    peak = np.max(np.abs(to_stereo(x)))
    return 20.0 * math.log10(max(peak * ref / 32767.0 * 32767.0 / 32767.0, 1e-15))


def rms_dbfs(x):
    x = to_stereo(x)
    return 20.0 * math.log10(max(float(np.sqrt(np.mean(x ** 2))), 1e-15))


def normalize_peak(x, ceiling_db=-1.0):
    x = np.array(x, dtype=np.float64, copy=True)
    peak = np.max(np.abs(x))
    if peak <= 0:
        return x
    return x * (db_to_lin(ceiling_db) / peak)


def sanitize(x, name="signal"):
    """Last line of defence: no NaN, no Inf, no DC."""
    x = np.array(x, dtype=np.float64, copy=True)
    bad = ~np.isfinite(x)
    if np.any(bad):
        print("  [warn] %s: %d non-finite samples replaced with 0" % (name, int(bad.sum())))
        x[bad] = 0.0
    if x.ndim == 2:
        x -= x.mean(axis=0, keepdims=True)
    else:
        x -= x.mean()
    return x


def fade_edges(x, sr=SR, in_ms=3.0, out_ms=4.0):
    """Anti-click fades for one-shots (never used on looping music)."""
    y = np.array(x, dtype=np.float64, copy=True)
    n = y.shape[0]
    ni = min(int(round(in_ms * 1e-3 * sr)), n)
    no = min(int(round(out_ms * 1e-3 * sr)), n)
    if ni > 0:
        y[:ni] *= np.linspace(0.0, 1.0, ni, endpoint=False)[:, None] if y.ndim == 2 \
            else np.linspace(0.0, 1.0, ni, endpoint=False)
    if no > 0:
        r = np.linspace(1.0, 0.0, no, endpoint=True)
        y[n - no:] *= r[:, None] if y.ndim == 2 else r
    return y


def fade_mask(n, sr=SR, in_ms=3.0, out_ms=4.0):
    """The complement of the edge fades: 0 at the very ends, 1 in the body."""
    w = np.ones(n)
    ni = min(int(round(in_ms * 1e-3 * sr)), n)
    no = min(int(round(out_ms * 1e-3 * sr)), n)
    if ni > 0:
        w[:ni] = np.linspace(0.0, 1.0, ni, endpoint=False)
    if no > 0:
        w[n - no:] = np.linspace(1.0, 0.0, no, endpoint=True)
    return w


def dc_free_with_fades(x, sr=SR, in_ms=3.0, out_ms=4.0):
    """Make a one-shot exactly zero-mean WITHOUT lifting its faded ends off zero.

    Subtracting ``mean(x)`` after the fades pushes the silent edges up by that
    mean, and a low-frequency one-shot can carry a partial-cycle mean of ~10
    LSB.  Here the correction is weighted by ``1 - fade``, so it is zero at the
    edges and the solved amplitude makes the total mean exactly zero:

        mean(y - w*c) = 0   =>   c = mean(y) * n / sum(w)
    """
    y = np.array(x, dtype=np.float64, copy=True)
    n = y.shape[0]
    if n == 0:
        return y
    w = fade_mask(n, sr, in_ms, out_ms)
    sw = float(w.sum())
    if sw <= 0:
        return y
    for _ in range(2):
        m = y.mean(axis=0) if y.ndim == 2 else y.mean()
        if y.ndim == 2:
            y -= w[:, None] * (m * n / sw)[None, :]
        else:
            y -= w * (m * n / sw)
    return y


# ---------------------------------------------------------------------------
# WAV writing
# ---------------------------------------------------------------------------
def write_wav(path, x, sr=SR, ceiling_db=-1.0, dither=True, seed=12345,
              normalize=True):
    """Write 16-bit stereo PCM.

    ``normalize=True`` scales the signal so its peak lands exactly on
    ``ceiling_db`` (used for music).  ``normalize=False`` leaves the level alone
    and only guarantees the peak never exceeds ``ceiling_db`` (used for one-shots
    that were already loudness-normalised).

    The integer ceiling is floor(db_to_lin(ceiling_db) * 32767) so the value
    decoded by Unity (i16 / 32768) lands at or below the requested dBFS.  TPDF
    dither of +/-1 LSB is added first, then the samples are clipped to that same
    ceiling, so dither can never push the peak over.
    """
    x = np.array(x, dtype=np.float64, copy=True)
    if x.ndim == 1:
        x = np.stack([x, x], axis=1)
    lim = int(math.floor(db_to_lin(ceiling_db) * 32767.0))
    if normalize:
        peak = float(np.max(np.abs(x)))
        if peak > 0:
            x *= (lim / 32767.0) / peak
    if dither:
        rng = np.random.default_rng(int(seed))
        x += (rng.random(x.shape) - rng.random(x.shape)) / 32767.0
    np.clip(x, -lim / 32767.0, lim / 32767.0, out=x)
    i16 = np.round(x * 32767.0).astype(np.int16)
    np.clip(i16, -lim, lim, out=i16)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(2)
        wf.setsampwidth(2)
        wf.setframerate(int(sr))
        wf.writeframes(i16.astype("<i2").tobytes())
    return i16


def read_wav(path):
    """Read a WAV back as float64 (n, channels) normalised by 32768."""
    with wave.open(str(path), "rb") as wf:
        ch = wf.getnchannels()
        sw = wf.getsampwidth()
        sr = wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    if sw != 2:
        raise ValueError("expected 16-bit PCM, got %d bytes/sample" % sw)
    a = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    return a.reshape(-1, ch), sr


def wav_info(path):
    """Duration / peak / RMS / DC / fmt for the report."""
    x, sr = read_wav(path)
    peak = float(np.max(np.abs(x)))
    rms = float(np.sqrt(np.mean(x ** 2)))
    return {
        "samples": int(x.shape[0]),
        "channels": int(x.shape[1]),
        "sr": int(sr),
        "seconds": x.shape[0] / float(sr),
        "peak_dbfs": 20.0 * math.log10(max(peak, 1e-15)),
        "rms_dbfs": 20.0 * math.log10(max(rms, 1e-15)),
        "dc": float(np.mean(x)),
        "nan": int(np.sum(~np.isfinite(x))),
        "clipped": int(np.sum(np.abs(x) >= 32767.0 / 32768.0)),
        "x": x,
    }


# ---------------------------------------------------------------------------
# self test
# ---------------------------------------------------------------------------
def _selftest():
    ok = []

    def chk(name, cond, extra=""):
        ok.append((name, bool(cond), extra))

    rng = np.random.default_rng(0)

    # sliding max/min against a naive implementation
    v = rng.standard_normal(997)
    for k in (3, 7, 16, 31):
        naive = np.array([np.max(v[max(0, i - k // 2):i + k // 2 + 1]) for i in range(v.size)])
        got = slide_max(v, k)
        chk("slide_max k=%d" % k, np.allclose(naive, got, atol=1e-12),
            "maxdiff %.2e" % float(np.max(np.abs(naive - got))))
        naive = np.array([np.min(v[max(0, i - k // 2):i + k // 2 + 1]) for i in range(v.size)])
        got = slide_min(v, k)
        chk("slide_min k=%d" % k, np.allclose(naive, got, atol=1e-12))

    # static one-pole via FFT matches a direct recursion
    x = rng.standard_normal(2000)
    fc = 1200.0
    a = math.exp(-TAU * fc / SR)
    y_ref = np.zeros_like(x)
    acc = 0.0
    for i, xi in enumerate(x):
        acc = a * acc + (1 - a) * xi
        y_ref[i] = acc
    y_fft = iir(x, [1 - a], [1, -a])
    chk("iir matches direct one-pole", np.allclose(y_ref, y_fft, atol=1e-9),
        "maxdiff %.2e" % float(np.max(np.abs(y_ref - y_fft))))

    # block-wise sweep matches the same recursion when fc is constant
    ys = _sweep_onepole(x, np.full(x.size, fc), stages=1)
    chk("sweep_lp == one-pole (const fc)", np.allclose(y_ref, ys, atol=1e-9),
        "maxdiff %.2e" % float(np.max(np.abs(y_ref - ys))))

    # biquad lowpass: DC gain 1, Nyquist gain 0
    b, a2 = lp_coeffs(1000.0)
    dc = _resp(b, a2, np.array([1.0 + 0j]))[0]
    ny = _resp(b, a2, np.array([-1.0 + 0j]))[0]
    chk("biquad LP DC/nyquist", abs(dc - 1.0) < 1e-6 and abs(ny) < 1e-3,
        "dc=%.6f ny=%.2e" % (float(np.abs(dc)), float(np.abs(ny))))

    # high-pass removes DC
    dc_sig = np.full(4410, 0.3)
    h = highpass(dc_sig, 100.0)
    chk("biquad HP kills DC", abs(h[2000:].mean()) < 1e-6, "mean=%.2e" % float(h[2000:].mean()))

    # allpass is unity gain
    ap = freeverb_ir(int(0.5 * SR), room=0.7, damp=0.3)
    chk("freeverb IR finite", np.all(np.isfinite(ap)) and ap.size == int(0.5 * SR),
        "rms=%.4f" % float(np.sqrt(np.mean(ap ** 2))))

    # circular convolution wraps
    xx = np.zeros(1000)
    xx[0] = 1.0
    hh = np.zeros(1000)
    hh[0] = 1.0
    hh[10] = 0.5
    cc = circular_convolve(xx, hh)
    chk("circular conv wraps", abs(cc[10] - 0.5) < 1e-9 and abs(cc[0] - 1.0) < 1e-9)

    # loudness: cross-check the FFT path against a direct biquad recursion
    def _biquad_direct(sig, b, a):
        y = np.zeros_like(sig)
        x1 = x2 = y1 = y2 = 0.0
        for i in range(sig.size):
            x0 = sig[i]
            v = b[0] * x0 + b[1] * x1 + b[2] * x2 - a[1] * y1 - a[2] * y2
            x2, x1 = x1, x0
            y2, y1 = y1, v
            y[i] = v
        return y

    (b1, a1), (b2, a2) = _k_coeffs()
    t = np.arange(SR, dtype=np.float64) / SR
    s = 0.5 * np.sin(TAU * 997.0 * t)
    ref = _biquad_direct(_biquad_direct(s, b1, a1), b2, a2)
    z_direct = float(np.mean(2.0 * ref ** 2))          # two identical channels
    l_direct = -0.691 + 10.0 * math.log10(z_direct)
    st = np.stack([s, s], axis=1)
    l_fft = lufs_integrated(st)
    chk("LUFS fft path == direct recursion", abs(l_fft - l_direct) < 0.05,
        "fft=%.3f direct=%.3f" % (l_fft, l_direct))

    # hard ground truth: the coefficient table printed in BS.1770-4 for 48 kHz
    (B1, A1), (B2, A2) = _k_coeffs(48000)
    ref_b1 = [1.53512485958697, -2.69169618940638, 1.19839281085285]
    ref_a1 = [1.0, -1.69065929318241, 0.73248077421585]
    ref_a2 = [1.0, -1.99004745483398, 0.99007225036621]
    d1 = max(float(np.max(np.abs(B1 - ref_b1))), float(np.max(np.abs(A1 - ref_a1))),
             float(np.max(np.abs(A2 - ref_a2))))
    chk("K coefficients == BS.1770 48kHz table", d1 < 1e-13, "maxdiff %.2e" % d1)

    # calibration: a full-scale 997 Hz sine in both channels reads ~0 LUFS
    full = np.stack([np.sin(TAU * 997.0 * t), np.sin(TAU * 997.0 * t)], axis=1)
    l_full = lufs_integrated(full)
    chk("LUFS calibration: full-scale 997Hz stereo ~ 0 LUFS", abs(l_full) < 0.10,
        "%.3f LUFS" % l_full)

    # dither + ceiling
    import tempfile, os
    tmp = os.path.join(tempfile.gettempdir(), "_seedfall_selftest.wav")
    write_wav(tmp, st, SR, -1.0)
    info = wav_info(tmp)
    chk("wav peak <= -1 dBFS", info["peak_dbfs"] <= -1.0 + 1e-9, "%.4f dBFS" % info["peak_dbfs"])
    chk("wav is 16-bit stereo 44.1k", info["channels"] == 2 and info["sr"] == SR)
    os.remove(tmp)

    # ADSR sanity
    e = adsr(int(0.5 * SR), a=0.01, d=0.05, s=0.6, r=0.2, gate=int(0.3 * SR))
    chk("adsr finite, starts at 0, ends at 0",
        np.all(np.isfinite(e)) and e[0] < 1e-3 and e[-1] < 1e-3 and e[np.argmax(e)] > 0.9,
        "peak=%.3f" % float(e.max()))

    bad = [n for n, c, _ in ok if not c]
    for name, c, extra in ok:
        print("  [%s] %-42s %s" % ("OK " if c else "BAD", name, extra))
    print("  self-test: %d/%d passed" % (len(ok) - len(bad), len(ok)))
    return not bad


if __name__ == "__main__":
    import sys
    sys.exit(0 if _selftest() else 1)
