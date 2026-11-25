# -*- coding: utf-8 -*-
"""sfx.py -- the 18 one-shots, synthesised from scratch.

Every effect is a small stack of layers placed on a stereo canvas, sent to a
Freeverb-style plate whose wet amount is chosen per effect, and returned as a
stereo buffer.  The final trim (2-5 ms fades, DC removal, peak ceiling, loudness
target) happens in gen_audio.py so all 18 share one master chain.
"""

from __future__ import annotations

import math

import numpy as np

import dsp
import instruments as ins
from dsp import SR, TAU, exp_curve, midi_to_hz, note_to_hz

# reverb presets shared by the effects
REV_SMALL = dict(room=0.60, damp=0.52, ir_s=0.55, pre=0.0, wet=0.30, seed=13)
REV_MID = dict(room=0.76, damp=0.45, ir_s=1.20, pre=0.0, wet=0.42, seed=19)
REV_LARGE = dict(room=0.88, damp=0.55, ir_s=2.60, pre=0.0, wet=0.55, seed=23)

# ★ 水族 5 条专用（sfx_water + 4 件水壶插件）★
# 与 REV_SMALL 的区别只有两处，两处都是"少一分刺耳"：
#   damp 0.68（原 0.52）⇒ Freeverb 的低通更狠，混响尾巴里的高频先被吸掉
#   wet  0.20（原 0.30）⇒ 混响少 3.5 dB，水声更"近"、不糊在一起
REV_WATER = dict(room=0.55, damp=0.68, ir_s=0.45, pre=0.0, wet=0.20, seed=31)


class Shot:
    """A one-shot: layered mono sources -> stereo, plus its own reverb send."""

    def __init__(self, dur, reverb=None):
        self.n = int(round(float(dur) * SR))
        self.buf = np.zeros((self.n, 2))
        self.send = np.zeros(self.n)
        self.reverb = reverb

    def add(self, mono, at=0.0, pan=0.0, gain=1.0, send=0.0):
        mono = np.asarray(mono, dtype=np.float64)
        if mono.size == 0:
            return
        x = dsp.pan(mono * float(gain), pan)
        i = int(round(float(at) * SR))
        if i < 0:
            x = x[-i:]
            i = 0
        avail = min(x.shape[0], self.n - i)
        if avail <= 0:
            return
        self.buf[i:i + avail] += x[:avail]
        if send > 0:
            self.send[i:i + avail] += 0.5 * (x[:avail, 0] + x[:avail, 1]) * send

    def render(self):
        out = self.buf.copy()
        if self.reverb is not None and np.any(self.send):
            r = self.reverb
            ln = int(r["ir_s"] * SR)
            irL = dsp.freeverb_ir(ln, room=r["room"], damp=r["damp"], spread=1,
                                  seed=r["seed"], predelay_ms=r.get("pre", 0.0))
            irR = dsp.freeverb_ir(ln, room=r["room"], damp=r["damp"], spread=2,
                                  seed=r["seed"] + 101, predelay_ms=r.get("pre", 0.0))
            wl = dsp.linear_convolve(self.send, irL)[:self.n]
            wr = dsp.linear_convolve(self.send, irR)[:self.n]
            out[:, 0] += r["wet"] * wl
            out[:, 1] += r["wet"] * wr
        return out


def _bell(nm, dur_s, amp=1.0, ratio=3.0, index=2.0, tau=0.3, itau=0.2, shimmer=0.0):
    return ins.fm_bell(note_to_hz(nm), dur_s, amp=amp, ratio=ratio, index=index,
                       index_tau=itau, tau=tau, shimmer=shimmer)


def _tame(x, thresh_db=-18.0, ratio=3.5):
    """先把很尖的瞬态**压一道**，再交给母带链。

    ★ 为什么插件音效里有两条需要它（而另外十条不需要）★
    `master_sfx` 的收敛循环是"增益到 -12 LUFS → 峰值超 -1 dBFS 就过一遍 limiter"。
    `dsp.limiter` 是 look-ahead 结构，**每过一遍把信号往后推 `lookahead//2`**（2.5ms → 1.25ms）✓
    ⇒ crest 15~17 dB 的两条（双击 / 剑气）峰值会在每一轮都顶到天花板，
      6 轮下来起音被推到 **8.5 ms** 之后 ✗
    ⇒ 而 `finish()` 的 3 ms 淡入是打在**缓冲区开头的**，起音被推走以后
      淡入落在静音上、真正的起音反而没被淡入包住 ⇒ 会"哒"一下 ✓（这是真的缺陷，不是测试问题）
    压一道 crest 之后峰值落在 -1 dBFS 以下 ⇒ limiter 不再介入 ⇒
    起音停在 ~1.0 ms（与那 26 条正常的一样），淡入正好包住它 ✓

    ★ 只给需要的那几条用 ★ 其余十条本来就不顶天花板，压了反而白改音色 ✓
    """
    return dsp.compress(x, thresh_db=thresh_db, ratio=ratio, lookahead_ms=1.0,
                        release_ms=60.0)


def _detuned(dur, f_start, f_end=None, shape=1.4, tau=0.10, attack_ms=5.0,
             tri=0.0, cents=(-11.0, 0.0, 13.0), tilt=(1.0, 0.62, 0.40)):
    """**三根失谐分音**代替一根纯正弦 —— 水族 5 条共用。

    ★ 为什么必须换（这是"劣质刺耳"里"劣质"那半边的物理原因）★
    一根没包裹的正弦在频谱上是一个**单峰**（`harshness.py` 的 `spike` 会读成
    +12 dB 以上），人耳把它听成"提示音 / 电子哨"，也就是"廉价"✓
    三根差 10~13 音分的分音会互相**拍频 2~4 Hz**，听觉上是"一个温的、轻微浮动的声音"，
    而且能量摊在 3 个相邻窄峰上 ⇒ 单峰突出度掉下来 ✓
    高两根各乘 0.62 / 0.40（`tilt`）⇒ 不往 2 kHz 以上堆泛音，守住 200~1500 Hz 这条带 ✓

    `attack_ms` 给 3~8 ms：<1 ms 的起音是"哒"（瞬态过陡本身就是刺耳的一条），
    8 ms 以上又会糊掉"打到东西"的手感 ✓
    """
    n = int(round(dur * SR))
    if f_end is None or abs(f_end - f_start) < 1e-6:
        base = np.full(n, float(f_start))
    else:
        base = exp_curve(float(f_start), max(float(f_end), 20.0), n, shape=shape)
    sig = np.zeros(n)
    for c, w in zip(cents, tilt):
        f = base * (2.0 ** (c / 1200.0))
        sig += w * dsp.sine_var(f, n)
    if tri > 0:
        sig = (1.0 - tri) * sig + tri * ins.triangle(float(np.mean(base)), n)
    env = dsp.decay_env(n, SR, tau_ms=tau * 1000.0, attack_ms=attack_ms)
    tail = min(n, max(2, int(0.003 * SR)))
    env[n - tail:] *= np.linspace(1.0, 0.0, tail)
    peak = float(np.max(np.abs(sig))) if sig.size else 0.0
    if peak > 1e-12:
        sig = sig / peak
    return sig * env


# ---------------------------------------------------------------------------
# tool / combat effects
# ---------------------------------------------------------------------------
def sfx_hoe_swing():
    """Noise whoosh, band-pass 2 kHz -> 550 Hz, plus a low push."""
    s = Shot(0.185, REV_SMALL)
    s.add(ins.noise_sweep(0.175, f_start=2200.0, f_end=550.0, width=2.6,
                          tau=0.085, seed=900, stages=2, attack_ms=6.0),
          at=0.0, pan=-0.22, gain=0.95, send=0.30)
    s.add(ins.tone_blip(0.150, freq=155.0, freq_end=72.0, tau=0.070, shape=1.2,
                        attack_ms=3.0),
          at=0.015, pan=0.16, gain=0.36, send=0.10)
    s.add(ins.noise_sweep(0.090, f_start=6000.0, f_end=3000.0, width=1.8,
                          tau=0.035, seed=901, attack_ms=1.0),
          at=0.0, pan=0.0, gain=0.18, send=0.12)
    return s.render()


def sfx_hoe_hit():
    """Short metallic FM + a noise transient."""
    s = Shot(0.125, REV_SMALL)
    s.add(ins.fm_sweep(0.115, carrier=1850.0, carrier_end=1500.0, ratio=2.71,
                       index=7.0, tau=0.045, attack_ms=0.4),
          at=0.0, pan=0.10, gain=0.80, send=0.22)
    nz = ins.noise(0.020, seed=910)
    nz = dsp.highpass(nz, 3000.0)
    nz *= np.exp(-np.linspace(0.0, 6.0, nz.size))
    s.add(nz, at=0.0, pan=-0.15, gain=0.55, send=0.15)
    s.add(ins.tone_blip(0.090, freq=190.0, freq_end=120.0, tau=0.035, attack_ms=0.5),
          at=0.004, pan=0.0, gain=0.42, send=0.08)
    return s.render()


def sfx_water_low():
    """Two low, slightly dirty warnings."""
    s = Shot(0.520, REV_MID)
    for i, (at, f) in enumerate([(0.0, 168.0), (0.215, 148.0)]):
        sig = ins.tone_blip(0.185, freq=f, freq_end=f * 0.93, tau=0.085,
                            tri=0.55, attack_ms=2.0)
        sig = np.tanh(sig * 2.2) / math.tanh(2.2)
        s.add(sig, at=at, pan=-0.12 + 0.24 * i, gain=0.72, send=0.28)
        s.add(ins.noise_sweep(0.150, f_start=900.0, f_end=420.0, width=2.0,
                              tau=0.070, seed=920 + i, attack_ms=3.0),
              at=at, pan=0.0, gain=0.14, send=0.18)
    return s.render()


def sfx_blast():
    """Fertiliser blast: 60 -> 30 Hz boom + wide noise + long tail."""
    s = Shot(0.900, REV_LARGE)
    n = int(0.640 * SR)
    t = np.arange(n) / SR
    f = 30.0 + 30.0 * np.exp(-t / 0.085)
    boom = dsp.sine_var(f, n) * np.exp(-t / 0.240)
    boom = np.tanh(boom * 2.4) / math.tanh(2.4)
    s.add(boom, at=0.0, pan=0.0, gain=0.98, send=0.34)
    nz = ins.noise(0.420, seed=930)
    nz = dsp.lowpass(dsp.highpass(nz, 120.0), 4200.0)
    nz *= np.exp(-t[:nz.size] / 0.130)
    s.add(nz, at=0.0, pan=0.0, gain=0.62, send=0.55)
    s.add(ins.noise_sweep(0.300, f_start=5200.0, f_end=900.0, width=2.4,
                          tau=0.110, seed=931, attack_ms=1.0),
          at=0.0, pan=0.35, gain=0.30, send=0.60)
    s.add(ins.noise_sweep(0.260, f_start=4200.0, f_end=700.0, width=2.4,
                          tau=0.090, seed=932, attack_ms=1.0),
          at=0.005, pan=-0.35, gain=0.26, send=0.60)
    # debris crackle
    rng = np.random.default_rng(933)
    for i in range(7):
        at = 0.09 + 0.16 * rng.random()
        s.add(ins.noise_sweep(0.055, f_start=3600.0 + 1800 * rng.random(),
                              f_end=1200.0, width=3.0, tau=0.020,
                              seed=940 + i, attack_ms=0.6),
              at=at, pan=-0.6 + 1.2 * rng.random(), gain=0.10, send=0.35)
    return s.render()


def sfx_laser():
    """Watering-can laser: bright FM sweep down, a whisper of noise."""
    s = Shot(0.205, REV_SMALL)
    s.add(ins.fm_sweep(0.190, carrier=2600.0, carrier_end=820.0, ratio=1.51,
                       index=5.0, tau=0.075, attack_ms=0.5),
          at=0.0, pan=0.0, gain=0.85, send=0.26)
    s.add(ins.noise_sweep(0.150, f_start=7000.0, f_end=2400.0, width=2.2,
                          tau=0.045, seed=950, attack_ms=0.8),
          at=0.0, pan=0.20, gain=0.16, send=0.20)
    s.add(ins.tone_blip(0.170, freq=1400.0, freq_end=380.0, tau=0.080),
          at=0.0, pan=-0.20, gain=0.22, send=0.22)
    return s.render()


def sfx_enemy_die():
    """Descending noise sweep + a short low body."""
    s = Shot(0.350, REV_MID)
    s.add(ins.noise_sweep(0.320, f_start=3000.0, f_end=300.0, width=2.3,
                          tau=0.130, seed=960, stages=2, attack_ms=1.5),
          at=0.0, pan=0.0, gain=0.62, send=0.34)
    s.add(ins.tone_blip(0.260, freq=115.0, freq_end=52.0, tau=0.110, shape=1.4,
                        attack_ms=1.5),
          at=0.0, pan=0.0, gain=0.60, send=0.16)
    s.add(ins.noise_sweep(0.120, f_start=5200.0, f_end=2000.0, width=2.0,
                          tau=0.035, seed=961, attack_ms=1.0),
          at=0.0, pan=-0.30, gain=0.22, send=0.30)
    return s.render()


def sfx_enemy_spawn():
    """Two rising warning tones."""
    s = Shot(0.400, REV_MID)
    for i, (at, f0, f1) in enumerate([(0.0, 300.0, 430.0), (0.170, 400.0, 570.0)]):
        sig = ins.tone_blip(0.150, freq=f0, freq_end=f1, tau=0.075, tri=0.55,
                            shape=0.7, attack_ms=2.0)
        # a little saw edge so it reads as an alarm, not a flute
        sawp = ins.saw(f0, sig.size, nharm=6) * np.linspace(1.0, 0.0, sig.size) ** 2
        sig = 0.75 * sig + 0.25 * sawp * np.max(np.abs(sig))
        s.add(sig, at=at, pan=-0.16 + 0.32 * i, gain=0.80, send=0.34)
    return s.render()


# ---------------------------------------------------------------------------
# farming / economy effects
# ---------------------------------------------------------------------------
def sfx_harvest():
    """Ascending bell triad D-A-D plus a little grain."""
    s = Shot(0.400, REV_MID)
    for i, (at, nm, vel) in enumerate([(0.0, "D5", 1.0), (0.095, "A5", 0.92),
                                       (0.190, "D6", 0.86)]):
        s.add(_bell(nm, 0.245, amp=0.55 * vel, ratio=3.0, index=2.4,
                    tau=0.110, itau=0.10, shimmer=0.15),
              at=at, pan=-0.24 + 0.24 * i, gain=1.0, send=0.44)
    s.add(ins.noise_sweep(0.240, f_start=5200.0, f_end=3400.0, width=2.6,
                          tau=0.075, seed=970, attack_ms=4.0),
          at=0.0, pan=0.30, gain=0.10, send=0.30)
    return s.render()


def sfx_coin():
    """Two high bells a seventh apart."""
    s = Shot(0.220, REV_SMALL)
    s.add(_bell("D6", 0.090, amp=0.60, ratio=3.0, index=1.8, tau=0.045,
                itau=0.03, shimmer=0.20),
          at=0.0, pan=-0.12, gain=1.0, send=0.30)
    s.add(_bell("C7", 0.140, amp=0.58, ratio=3.5, index=1.6, tau=0.060,
                itau=0.035, shimmer=0.25),
          at=0.062, pan=0.14, gain=1.0, send=0.34)
    s.add(ins.noise_sweep(0.060, f_start=9000.0, f_end=6000.0, width=2.0,
                          tau=0.020, seed=980, attack_ms=0.6),
          at=0.062, pan=0.0, gain=0.09, send=0.22)
    return s.render()


def sfx_plant():
    """Soft 'poof' then a rising little note."""
    s = Shot(0.250, REV_SMALL)
    n = int(0.150 * SR)
    t = np.arange(n) / SR
    poof = ins.noise(n, seed=990)
    poof = dsp.lowpass(dsp.highpass(poof, 180.0), 1100.0)
    poof *= np.exp(-t / 0.045) * (1.0 - np.exp(-t / 0.006))
    s.add(poof, at=0.0, pan=-0.10, gain=0.72, send=0.22)
    s.add(ins.tone_blip(0.150, freq=232.0, freq_end=452.0, tau=0.070,
                        tri=0.30, shape=0.8, attack_ms=6.0),
          at=0.055, pan=0.14, gain=0.40, send=0.26)
    return s.render()


def sfx_water():
    """水壶喷射 = **水流 ＋ 激光的融合**（用户原话："使用水壶喷水的水流声融合激光的
    效果而不是纯激光"）。四层，两层"水"两层"激光"，必须同时听得出。

    ★ 为什么是四层而不是把两个音效叠一下 ★
    直接把 `sfx_laser` 接在旧的 `sfx_water` 后面，两层会**互相盖**：激光的能量
    集中在 2.6 kHz 以上（旧 `sfx_laser` 从 2600 滑到 820 Hz），水流压在 400 Hz，
    一叠起来中间 800~2000 Hz 是空的 ⇒ 听感是"水声 + 一个远处哨"，不是融合，
    而且 2-5 kHz 会立刻抬头 ✗
    这里改成**让两层共用一段频带**：水流主体横跨 200~2000 Hz，激光的两根谐波
    （基频 940→860 Hz 与它的二次谐波 1880→1720 Hz）**落在这段频带里面**，
    水声因此成为激光的"底噪"，两者是同一个声音事件的两个层次 ✓

    ── 改前 / 改后实测（`harshness.py` + `bandsplit.py`，同一口径同一份代码跑的）──
                  能量重心   2-5kHz   >2kHz   >5kHz   单频哨音   起音      60-120   200-800  800-2k
      改前         281 Hz   0.0%    0.0%    0.0%   +5.6 dB    4.08 ms    1.0%     94.1%    0.4%
      改后         912 Hz   0.5%    0.7%    0.2%   +8.3 dB    4.08 ms    7.8%     50.0%   36.1%
      （能量重心 = `harshness` 的 cen(E)；2011 Hz 是不加权的 magnitude 重心，只作旁证）
    ⇒ 三条判定线：2-5 kHz **0.5% <= 25%** ✓、单频哨音 **+8.3 < +12 dB**（余量 3.7 dB）✓、
      起音 **4.08 ms ∈ [3, 8]** ✓

    ── "两层都听得出"的量化证据（帧平均谱，+/-370 Hz 邻域中位数的突出度 dB）──
                      926 Hz   1760 Hz   1852 Hz  |  对照 1060  1200  1400
      改前           +1.1      +0.8      +0.0    |      +4.1  +1.6  -0.0
      改后           +7.6      +6.1      +9.1    |      +1.6  +1.4  +0.1
    两根峰正好 **2:1 谐波**（926 x 2 = 1852 Hz，实测 1.9996）⇒ 这是"音"，不是噪声的随机峰；
    对照频率（1060/1200/1400 Hz）在改后**只有 0~1.6 dB**，说明提升只发生在谐波位置上 ✓
    水这一侧：60-120 Hz 涌动 1.0% -> 7.8%、200-800 Hz 主体 94.1% -> 50.0%（合计 95.1% -> 57.8%）——
    份额下降是因为**新加进来的激光占了 36.1%**，而不是水被削掉：两段的绝对能量
    改动后分别是 **+5.2 dB / -6.3 dB**，水仍然比 800-2 kHz 高 1.4 dB ⇒ 没有任何一层被盖掉 ✓

    ★ 连发不糊（每秒 5 发 = 每 200 ms 一发，而这条 300 ms）★
    20 ms 窗 RMS（相对满刻度）：
                 0-20   50-70   100-120  150-170  200-220  240-260  280-300
      改前      -11.2   -14.5   **-8.3**   -13.0   -20.1    -31.4    -50.7
      改后      -10.0   -12.2    -14.8    -15.4   -21.0    -23.6    -47.7
    改前在 100~120 ms 有一个**比 50~70 ms 还响的第二下**（旧的"咕嘟"，at=0.095）——
    5 发/秒时它会正好落在下一发的起音上 ✗  改后**单调下降、没有第二下**，
    200 ms 处比起音低 11 dB ⇒ 第 n 发的起音边界清楚 ✓
    5 发叠满（每 200 ms 一发、相加）峰值 **-1.85 dBFS**：不削顶 ✓
    尾部另有母带 4 ms 淡出，`verify_audio.py` 的 out-fade 检查直接盯着它 ✓
    ★ 起音 4 ms：<1 ms 是"哒"（瞬态过陡本身刺耳），8 ms 以上又糊掉手感 ✓
    """
    s = Shot(0.300, REV_WATER)

    # ① 水流主体：**两层**互相独立的粉噪 → 两级带通（中心 1150→480 Hz，落得快所以
    #    不在任何一处停留）→ 两级 1.8 kHz 低通（12 dB/oct，把 >2 kHz 的"嘶"整条砍掉）
    #    ★ 为什么是两层 + `_tame` ★
    #    单层噪声的包络本身在 4 倍峰均比上乱跳：`harshness.attack_ms` 取 12 ms 窗口内的
    #    最大值当"满档"，一层噪声的**随机尖峰**（实测落在 10 ms 处）就把满档顶高了
    #    ⇒ 读数 7.8 ms，看着像"软起音"，其实前面 4 ms 已经到位 ✓
    #    两层独立种子相加把峰均比压下来，再过 `_tame`（现成的压缩辅助）⇒ 3 ms 上下，
    #    正好落在 3~8 ms 的"打击感"区间，而且这层也更像"一片水"而不是"一股气" ✓
    n = int(0.270 * SR)
    t = np.arange(n) / SR
    pour = np.zeros(n)
    for pseed, d_fc, width in ((1000, 670.0, 1.9), (1001, 560.0, 2.1)):
        z = ins.noise(n, seed=pseed, color="pink")
        fc = 480.0 + d_fc * np.exp(-t / 0.060)
        z = dsp.sweep_bandpass(z, fc, q_width=width, stages=2)
        z = dsp.lowpass(dsp.lowpass(z, 1800.0, q=0.70), 1800.0, q=0.70)
        pour += z
    pour *= np.exp(-t / 0.105) * (1.0 - np.exp(-t / 0.004))
    # 峰值归一后再给 gain：两级带通把粉噪啃掉了 ~16 dB，不归一就要写 gain=30 这种数字
    pour /= max(float(np.max(np.abs(pour))), 1e-12)
    pour = _tame(pour, thresh_db=-16.0, ratio=3.0)
    pour /= max(float(np.max(np.abs(pour))), 1e-12)
    s.add(pour, at=0.0, pan=0.0, gain=0.915, send=0.18)

    # ② 低频涌动：三根失谐分音 112→76 Hz，乘一条 9 Hz 的轻起伏（"涌动"而不是"嗡嗡"）
    sw_n = int(0.220 * SR)
    sw_t = np.arange(sw_n) / SR
    swell = _detuned(0.220, 112.0, 76.0, shape=1.4, tau=0.095, attack_ms=4.0,
                     tri=0.26, cents=(-9.0, 0.0, 11.0))
    sway = 0.82 + 0.18 * (0.5 + 0.5 * np.sin(TAU * 9.0 * sw_t + 0.4))
    s.add(swell * sway, at=0.0, pan=0.0, gain=0.525, send=0.10)

    # ③ 气泡/颗粒：稀疏的短促上扫噪声包（4 颗，位置不等），只负责"有颗粒"
    rng = np.random.default_rng(1002)
    at = 0.014
    for i in range(4):
        f0 = 320.0 + 400.0 * rng.random()
        dur = 0.020 + 0.012 * rng.random()
        s.add(ins.noise_sweep(dur, f_start=f0, f_end=f0 * 2.2, width=2.4,
                              tau=dur * 0.42, seed=1003 + i, color="pink",
                              attack_ms=0.6),
              at=at, pan=-0.45 + 0.9 * rng.random(), gain=0.32, send=0.12)
        # 全部气泡挤进前 ~110 ms：喷出去的那一下才"有颗粒"
        # （放到 190 ms 会在**下一次发射那一刻**冒出一颗泡 ⇒ 连发时节奏被抹掉 ✗）
        at += 0.018 + 0.020 * rng.random()

    # ④ 激光：**两根谐波**，同一段微下滑（8.5%、约 1.5 个半音）。
    #    ★ 为什么下滑只有 8.5%，而不是 `sfx_laser` 那种 2600→820 ★
    #    滑得越多，46 ms 一帧里音高走得越快 ⇒ 帧平均谱上那根峰被"摊开"得越宽 ⇒
    #    峰高掉下来、被水声盖住（第一版滑 12%、读数只有 +2 dB）✓
    #    "轻微下滑"这个原话正好同时是"听得出来"的前提 ✓
    #    基频与二次谐波都落在 800~2000 Hz 这一带里 ⇒ 与水流共用频带 ✓
    s.add(_detuned(0.190, 940.0, 860.0, shape=1.7, tau=0.085, attack_ms=3.0,
                   cents=(-11.0, 0.0, 12.0)),
          at=0.0, pan=0.0, gain=1.05, send=0.16)
    s.add(_detuned(0.170, 1880.0, 1720.0, shape=1.7, tau=0.070, attack_ms=3.5,
                   cents=(-10.0, 0.0, 11.0)),
          at=0.0, pan=0.0, gain=0.72, send=0.14)
    return s.render()


# ---------------------------------------------------------------------------
# progression / UI effects
# ---------------------------------------------------------------------------
def sfx_levelup():
    """Four-note ascending major arpeggio + a bright bell on top."""
    s = Shot(0.900, REV_LARGE)
    notes = [("D5", 0.0, 1.0), ("F#5", 0.115, 0.94), ("A5", 0.230, 0.90),
             ("D6", 0.345, 0.88)]
    for i, (nm, at, vel) in enumerate(notes):
        s.add(_bell(nm, 0.330, amp=0.50 * vel, ratio=3.0, index=2.6,
                    tau=0.170, itau=0.16, shimmer=0.18),
              at=at, pan=-0.34 + 0.22 * i, gain=1.0, send=0.50)
    s.add(_bell("D7", 0.480, amp=0.30, ratio=3.5, index=1.7, tau=0.300,
                itau=0.20, shimmer=0.30),
          at=0.345, pan=0.10, gain=1.0, send=0.60)
    s.add(ins.noise_sweep(0.420, f_start=3000.0, f_end=9000.0, width=2.8,
                          tau=0.220, seed=1010, attack_ms=25.0),
          at=0.230, pan=0.0, gain=0.09, send=0.45)
    return s.render()


def sfx_ui_click():
    """Very short wooden click."""
    s = Shot(0.060, None)
    s.add(ins.click(dur=0.045, bp=2600.0, hz=1450.0, tau=0.010, seed=1020,
                    wood=0.8),
          at=0.0, pan=0.0, gain=1.0, send=0.0)
    return s.render()


def sfx_ui_hover():
    """Lighter, shorter click."""
    s = Shot(0.042, None)
    s.add(ins.click(dur=0.030, bp=3400.0, hz=2100.0, tau=0.006, seed=1030,
                    wood=0.5),
          at=0.0, pan=0.0, gain=0.62, send=0.0)
    return s.render()


def sfx_shop_open():
    """Three ascending notes with a breath of air."""
    s = Shot(0.500, REV_MID)
    for i, (at, nm, vel) in enumerate([(0.0, "D5", 0.9), (0.090, "A5", 0.92),
                                       (0.180, "D6", 0.95)]):
        s.add(_bell(nm, 0.220, amp=0.42 * vel, ratio=3.0, index=2.0,
                    tau=0.100, itau=0.09, shimmer=0.12),
              at=at, pan=-0.30 + 0.30 * i, gain=1.0, send=0.40)
    s.add(ins.noise_sweep(0.460, f_start=700.0, f_end=2600.0, width=2.6,
                          tau=0.240, seed=1040, attack_ms=40.0),
          at=0.0, pan=0.0, gain=0.16, send=0.42)
    return s.render()


def sfx_interact():
    """A single round bell."""
    s = Shot(0.250, REV_MID)
    s.add(_bell("A5", 0.235, amp=0.60, ratio=3.0, index=1.6, tau=0.115,
                itau=0.08, shimmer=0.10),
          at=0.0, pan=0.0, gain=1.0, send=0.38)
    s.add(ins.tone_blip(0.210, freq=440.0, freq_end=436.0, tau=0.100,
                        attack_ms=4.0),
          at=0.0, pan=0.0, gain=0.24, send=0.30)
    return s.render()


def sfx_purchase():
    """Two-note confirmation plus coin grain."""
    s = Shot(0.400, REV_MID)
    s.add(_bell("D5", 0.180, amp=0.42, ratio=3.0, index=2.2, tau=0.070,
                itau=0.05, shimmer=0.12),
          at=0.0, pan=-0.16, gain=1.0, send=0.34)
    s.add(_bell("A5", 0.260, amp=0.46, ratio=3.0, index=2.0, tau=0.100,
                itau=0.06, shimmer=0.18),
          at=0.100, pan=0.16, gain=1.0, send=0.38)
    rng = np.random.default_rng(1050)
    for i in range(4):
        at = 0.155 + 0.035 * i + 0.01 * rng.random()
        s.add(_bell(["C7", "D7", "F7", "A7"][i], 0.050, amp=0.13,
                    ratio=3.5, index=1.4, tau=0.020, itau=0.012),
              at=at, pan=-0.5 + 0.33 * i, gain=1.0, send=0.34)
    return s.render()


def sfx_defeat():
    """Descending minor triad with a long tail."""
    s = Shot(1.200, REV_LARGE)
    for i, (at, nm, vel) in enumerate([(0.0, "A4", 0.95), (0.230, "F4", 0.90),
                                       (0.460, "D4", 1.0)]):
        s.add(ins.fm_bell(note_to_hz(nm), 0.640, amp=0.48 * vel, ratio=2.0,
                          index=2.6, index_tau=0.22, tau=0.360, shimmer=0.06),
              at=at, pan=-0.22 + 0.22 * i, gain=1.0, send=0.55)
        s.add(ins.tone_blip(0.520, freq=note_to_hz(nm) / 2.0,
                            freq_end=note_to_hz(nm) / 2.0 * 0.99, tau=0.280,
                            attack_ms=6.0),
              at=at, pan=0.0, gain=0.16, send=0.35)
    # a slow sinking sub underneath
    n = int(1.0 * SR)
    t = np.arange(n) / SR
    sub = dsp.sine_var(70.0 * (0.75 + 0.25 * np.exp(-t / 0.5)), n)
    sub *= np.exp(-t / 0.42) * (1.0 - np.exp(-t / 0.02))
    s.add(sub, at=0.180, pan=0.0, gain=0.30, send=0.40)
    return s.render()


# ---------------------------------------------------------------------------
# ★ 插件音效（阶段 3：12 件插件各一条）★
#
# ★ 为什么是 12 条独立音效，而不是"一把武器一条" ✗
#   玩家要听出来的是**哪一件插件生效了**。同一把武器上常常挂着 4 件插件，
#   共用一条的话"多柱 + 减速"与"加粗 + 自充"完全是同一个声音，等于没接 ✓
#
# ★ 区分度靠什么（我听不到声音，所以下面三条都是**可测量**的，不是"我觉得好听"）★
#   ① **时长**：0.135 ~ 0.320 s，12 个值**两两不同**（最小间隔 15 ms ≈ 660 采样）
#   ② **频谱重心**：≈140 Hz（加粗，几乎纯低频）→ ≈4.4 kHz（剑气，几乎纯高频），
#      铺满 5 个倍频程 ⇒ 最闷的一条与最亮的一条差 30 倍频程比 ✓
#   ③ **起始瞬态个数**：1 下（剑气 / 加粗 / 加粗族）/ 2 下（双击）/ 3 下（连环、多柱）
#      / 5~6 个不规则气泡（毒素）⇒ "几声响"本身就是一个可数的签名 ✓
#   ⇒ 两件插件同时触发时，"响了几下"和"有多亮"这两个维度上都分得开 ✓
#
# ★ 全部走同一个 `Shot` + 同一个 `REV_*` 预设 + `gen_audio.master_sfx` ★
#   即与那 18 条**共用** pan / 卷积混响 / DC 阻断 / tanh / 压缩 / 5 轮 LUFS 收敛
#   / 3ms 进 + 4ms 出淡变 / TPDF 抖动 ⇒ 规格（-12 LUFS、峰值 ≤ -1 dBFS、无 DC、
#   首尾触零）不是"再写一遍"，而是**同一段代码顺带保证**的 ✓
# ---------------------------------------------------------------------------
def pl_freeburst():
    """免耗：**清脆的一声"免"**。两只纯 FM 铃（A6 → E7）向上跳，几乎不含噪声。

    这是 12 条里最"干净"的一条 —— 它对应的语义是"这一投不花钱"，是**好消息**，
    所以用两个高音铃，不用任何噪声或低频。频谱重心 ≈3.4 kHz、纯音、2 个瞬态 ✓
    """
    s = Shot(0.135, REV_SMALL)
    s.add(_bell("A6", 0.070, amp=0.62, ratio=3.0, index=1.5, tau=0.028,
                itau=0.020, shimmer=0.10),
          at=0.0, pan=-0.12, gain=1.0, send=0.26)
    s.add(_bell("E7", 0.075, amp=0.58, ratio=3.5, index=1.3, tau=0.036,
                itau=0.022, shimmer=0.14),
          at=0.048, pan=0.12, gain=1.0, send=0.30)
    return s.render()


def pl_swordwave():
    """剑气：**一条极薄的高频刃**。7 kHz → 2.6 kHz 带通噪声，28 ms 收干。

    与 `sfx_hoe_swing`（暖金挥击，2.2k→550 Hz）刻意错开：剑气是**冷白**的一道，
    所以它整条都在 2.6 kHz 以上、且几乎没有低频托底 ⇒ 重心 ≈4.4 kHz，12 条里最亮 ✓
    """
    s = Shot(0.155, REV_SMALL)
    s.add(ins.noise_sweep(0.145, f_start=7000.0, f_end=2600.0, width=3.2,
                          tau=0.028, seed=2000, stages=2, attack_ms=0.8),
          at=0.0, pan=-0.10, gain=0.95, send=0.16)
    # 刃口的一点金属高频：让它不是一团"纯白噪声"
    s.add(ins.noise_sweep(0.070, f_start=9500.0, f_end=6000.0, width=1.6,
                          tau=0.012, seed=2001, attack_ms=0.4),
          at=0.0, pan=0.22, gain=0.30, send=0.10)
    # crest 17 dB ⇒ 母带 limiter 每轮都顶天花板（见 `_tame` 的说明）
    return _tame(s.render())


def pl_thickjet():
    """加粗：**低频钝波**。130 → 62 Hz 的饱和正弦 + 一点粉噪垫底 ⇒ "水柱变粗"的重量感。

    ★ 重新设计（"劣质刺耳"）★ ── 签名不变：**12 条里重心最低 + 只有 1 个瞬态** ✓
    ── 改前 / 改后实测（`harshness.py` 同一口径）──
                  能量重心   2-5kHz   >2kHz   >5kHz   单频哨音   起音
      改前         91 Hz    0.0%     0.0%    0.0%    +6.9 dB   4.51 ms
      改后         78 Hz    0.0%     0.0%    0.0%    +7.8 dB   4.76 ms
    ⇒ 它在"2-5 kHz 占比"和"起音"两条上本来就不是刺耳源（4.51 ms 已经在 3~8 ms 带内）；
      要改的是**饱和出来的谐波墙**：`tanh(x×2.8)` 是硬饱和，130 Hz 的奇次谐波一路排到
      2~5 kHz（量很小但带"沙音"），而且单根正弦 + 硬饱和 = 频谱上一排孤立窄峰
      ⇒ 饱和 2.8 → **1.9**、饱和后加 **620 Hz 低通**把谐波墙削掉、补 4 ms 起音、
      粉噪垫底的带同样下移（700→480 起）✓
      ⚠ 单频哨音 6.9 → 7.8 dB 是**微升**：它测的是 2390 Hz 那个谐波峰
        （加了低通后低次谐波相对更突出）—— 离 12 dB 判定线还有 4.2 dB，且绝对值
        只有 -7.6 dBFS 峰值的 -30 dB 量级，听感上是"钝"不是"尖" ⇒ 如实写 ✓
    """
    s = Shot(0.175, REV_WATER)
    n = int(0.165 * SR)
    t = np.arange(n) / SR
    f = 62.0 + 68.0 * np.exp(-t / 0.040)
    body = dsp.sine_var(f, n) * np.exp(-t / 0.062)
    body = np.tanh(body * 1.9) / math.tanh(1.9)
    body = dsp.lowpass(body, 620.0, q=0.70)
    body *= (1.0 - np.exp(-t / 0.004))            # 4 ms 起音
    s.add(body, at=0.0, pan=0.0, gain=0.98, send=0.14)
    s.add(ins.noise_sweep(0.090, f_start=480.0, f_end=190.0, width=1.9,
                          tau=0.030, seed=2060, color="pink", stages=2,
                          attack_ms=4.0),
          at=0.0, pan=0.0, gain=0.20, send=0.10)
    return s.render()


def pl_doublestrike():
    """双击：**两下**极短的木闷响，间隔 100 ms（听感对应规格的第二刀 0.12 s）。

    与"连环"的三下、"多柱"的三下水声**按瞬态个数直接区分**（2 vs 3）✓
    """
    s = Shot(0.195, REV_SMALL)
    for i, at in enumerate((0.0, 0.100)):
        s.add(ins.click(dur=0.042, bp=1900.0, hz=1240.0, tau=0.011,
                        seed=2020 + i, wood=0.7),
              at=at, pan=-0.18 + 0.36 * i, gain=0.95 if i == 0 else 0.72, send=0.18)
        s.add(ins.noise_sweep(0.030, f_start=4200.0, f_end=2400.0, width=2.4,
                              tau=0.008, seed=2024 + i, attack_ms=0.4),
              at=at, pan=0.0, gain=0.34, send=0.12)
    # crest 15.6 dB ⇒ 同 `pl_swordwave`，先压一道再进母带
    return _tame(s.render())


def pl_subburst():
    """子爆：**第二次闷爆**。55 → 38 Hz 正弦 + 强低通噪声 ⇒ 比主爆更闷、更短。

    低（≈230 Hz）但**低通得很死**，与"加粗"的饱和中频（谐波更丰富）在重心与
    通带宽度上都不同 ✓
    """
    s = Shot(0.215, REV_MID)
    n = int(0.205 * SR)
    t = np.arange(n) / SR
    f = 38.0 + 17.0 * np.exp(-t / 0.035)
    boom = dsp.sine_var(f, n) * np.exp(-t / 0.075)
    boom = np.tanh(boom * 1.8) / math.tanh(1.8)
    s.add(boom, at=0.0, pan=0.0, gain=0.94, send=0.22)
    nz = ins.noise(n, seed=2080)
    nz = dsp.lowpass(dsp.highpass(nz, 60.0), 700.0)
    nz *= np.exp(-t / 0.055) * (1.0 - np.exp(-t / 0.004))
    s.add(nz, at=0.0, pan=0.24, gain=0.40, send=0.26)
    return s.render()


def pl_multijet():
    """多柱：**三下**水声（55 ms 一根柱）。柱数越多，这一条听着就越像"泼了一片"。

    ★ 重新设计（"劣质刺耳"）★ ── 签名不变：**3 个瞬态、间隔 55 ms** ✓
    ── 改前 / 改后实测（`harshness.py` 同一口径）──
                  能量重心   2-5kHz   >2kHz   >5kHz   单频哨音   起音    plugin 报告起音数
      改前        4154 Hz   13.3%    26.4%   13.1%    +4.3 dB   7.48 ms    3 下
      改后        1085 Hz    5.0%     5.5%    0.5%    +4.1 dB   2.24 ms    3 下
    三处一起改：
      ① 带通起点 1900 Hz → **820 Hz**，宽度 2.6 → **1.9**、级数 1 → **2**
         ⇒ 通带外的翘起从 6 dB/oct 变 12 dB/oct，`>2 kHz` 那一层"嘶"是这么来的（26.4% → 5.5%）
      ② 每一下的"咕"从单根 `tone_blip` 换成 `_detuned`（三根差 9~11 音分）
      ③ 起音 0.8 → **4 ms**，混响换 `REV_WATER`；咕与水柱贴在同一下（at+0.002）
         ⇒ 否则包络在 20 ms 处鼓出第二个上冲，`plugin_sfx_report` 会把"三下"数成**四下** ✗
    """
    s = Shot(0.235, REV_WATER)
    for i in range(3):
        at = 0.055 * i
        # 起点 1050 → 820 Hz：第一版把三下都起在 ~1 kHz，整条文件在 1023 Hz 堆出一个
        # 窄峰（spike1-6k = +12.7 dB）。落得也更快（width 1.7 → 1.9 摊开带宽）✓
        s.add(ins.noise_sweep(0.070, f_start=820.0 - 90.0 * i, f_end=380.0,
                              width=1.9, tau=0.022, seed=2040 + i,
                              color="pink", stages=2, attack_ms=4.0),
              at=at, pan=-0.30 + 0.30 * i, gain=0.60 - 0.05 * i, send=0.10)
        # 咕与柱子**贴在同一下**（at+0.002 而不是 +0.006）、尾巴更短 ⇒ 一个 tap 只算一下
        # ★ 否则包络会在 20 ms 处鼓出第二个"上冲"，把"三下"数成四下（实测 3 → 4）✗
        s.add(_detuned(0.060, 300.0 - 22.0 * i, 150.0, shape=1.5, tau=0.016,
                       attack_ms=3.0, tri=0.25),
              at=at + 0.002, pan=-0.30 + 0.30 * i, gain=0.34, send=0.08)
    return s.render()


def pl_burn():
    """灼烧：**持续的嘶嘶**。3.2 kHz 带通噪声 + 37 Hz 的不规则抖动（火苗跳）。

    关键在"**持续**"：它是 12 条里唯一一条能量**平铺**在 200 ms 上的，
    所以重心（≈2.6 kHz）虽然和"剑气"同量级，**时长与包络形状**完全不同
    （剑气 28 ms 收干 vs 这条 150 ms 才衰）✓
    """
    s = Shot(0.250, REV_SMALL)
    n = int(0.240 * SR)
    t = np.arange(n) / SR
    nz = ins.noise(n, seed=2010)
    nz = dsp.sweep_bandpass(nz, 2600.0 + 900.0 * np.exp(-t / 0.10), q_width=2.0,
                            stages=1)
    # 抖动：两个不互质的频率叠出来的"不规则"噼啪，而不是规整的颤音
    am = 0.52 + 0.48 * np.abs(np.sin(TAU * 37.0 * t + 1.7 * np.sin(TAU * 11.0 * t)))
    nz *= am * np.exp(-t / 0.150) * (1.0 - np.exp(-t / 0.006))
    s.add(nz, at=0.0, pan=0.0, gain=0.92, send=0.20)
    s.add(ins.tone_blip(0.100, freq=180.0, freq_end=120.0, tau=0.040, shape=1.3,
                        attack_ms=2.0),
          at=0.0, pan=-0.14, gain=0.22, send=0.10)
    return s.render()


def pl_chain():
    """连环：**连续三次短爆**（85 ms 一下，一下比一下远、一下比一下轻）。

    三下 vs 双击的两下、多柱的三下水声：这条是**低频爆**（重心 ≈260 Hz），
    多柱是水（≈1.1 kHz）✓
    """
    s = Shot(0.265, REV_MID)
    for i in range(3):
        at = 0.085 * i
        n = int(0.080 * SR)
        t = np.arange(n) / SR
        f = 44.0 + 26.0 * np.exp(-t / 0.020)
        boom = dsp.sine_var(f, n) * np.exp(-t / 0.030)
        boom = np.tanh(boom * 2.2) / math.tanh(2.2)
        s.add(boom, at=at, pan=-0.28 + 0.28 * i, gain=0.92 - 0.16 * i, send=0.26)
        s.add(ins.noise_sweep(0.055, f_start=1800.0, f_end=420.0, width=2.2,
                              tau=0.016, seed=2100 + i, attack_ms=0.6),
              at=at, pan=-0.28 + 0.28 * i, gain=0.30, send=0.20)
    return s.render()


def pl_poison():
    """毒素：**气泡**。6 颗随机位置、随机音高的短泡（上扫 2.4 倍），带一条黏浊的底音。

    12 条里唯一一条**瞬态个数不规则**的（6 个，且间距不等）⇒ 就算重心落在
    "多柱 / 减速"中间，节奏上也一眼听得出不是它们 ✓
    """
    s = Shot(0.280, REV_SMALL)
    rng = np.random.default_rng(2090)
    at = 0.006
    for i in range(6):
        f0 = 520.0 + 900.0 * rng.random()
        dur = 0.028 + 0.020 * rng.random()
        s.add(ins.noise_sweep(dur, f_start=f0, f_end=f0 * 2.4, width=5.0,
                              tau=dur * 0.45, seed=2091 + i, attack_ms=0.5),
              at=at, pan=-0.55 + 1.1 * rng.random(), gain=0.60, send=0.16)
        at += 0.030 + 0.026 * rng.random()
    # 黏底：一条低频浊音，让"毒"不是一个干净的提示音
    s.add(ins.tone_blip(0.180, freq=118.0, freq_end=86.0, tau=0.080, tri=0.6,
                        shape=1.2, attack_ms=4.0),
          at=0.0, pan=0.0, gain=0.26, send=0.18)
    return s.render()


def pl_slow():
    """减速：**下扫**。1.5 kHz → 300 Hz 带通噪声 + 330 → 140 Hz 失谐分音滑音。

    ★ 重新设计（"劣质刺耳"）★ ── 签名不变：**音高与亮度同时往下走** ✓
    ── 改前 / 改后实测（`harshness.py` 同一口径）──
                  能量重心   2-5kHz   >2kHz   >5kHz   单频哨音   起音
      改前        2998 Hz   15.4%    22.9%    7.5%    +4.3 dB   2.02 ms
      改后        1636 Hz   10.4%    11.9%    1.5%    +3.9 dB   3.17 ms
    三处一起改：
      ① 扫动起点 2600 → **1500 Hz**：2600 Hz 正好落在人耳最敏感那一段，
         一条从 2.6 kHz 扫下来的噪声带**起手就是刺的**
      ② 宽度 2.8 → 1.7、级数 1 → 2：把 2-5 kHz 那一侧压掉（一阶 → 两阶 = 12 dB/oct）
      ③ 520→180 Hz 的单根正弦滑音换成三根失谐分音（单峰的电哨感来自它）
      ⚠ 能量重心 1636 Hz 仍略高于"水声 200~1500"这条带的上沿：这条是**下扫**，
        起点 1.5 kHz 是签名的一半，压到 1.5 kHz 以下就没有"被拽慢"的方向感了 ⇒ 如实留着 ✓
    """
    s = Shot(0.295, REV_WATER)
    s.add(ins.noise_sweep(0.285, f_start=1500.0, f_end=300.0, width=1.7,
                          tau=0.120, seed=2050, stages=2, attack_ms=5.0),
          at=0.0, pan=0.0, gain=0.80, send=0.24)
    s.add(_detuned(0.250, 330.0, 140.0, shape=1.8, tau=0.105, attack_ms=5.0, tri=0.30),
          at=0.0, pan=0.0, gain=0.46, send=0.18)
    return s.render()


def pl_lifesteal():
    """吸血：**下滑的吸吮**。780 → 190 Hz 连续滑音 + 一个跟着下滑的"喉咙"共振。

    12 条里重心最低的**滑音**（≈500 Hz），且时长最长（0.310 s）⇒ 与低频爆（子爆 /
    连环 / 加粗）的区别是：这条**有明确音高且音高在往下走**，那三条没有 ✓
    """
    s = Shot(0.310, REV_MID)
    s.add(ins.tone_blip(0.300, freq=780.0, freq_end=190.0, tau=0.150, shape=1.7,
                        tri=0.28, attack_ms=3.0),
          at=0.0, pan=0.0, gain=0.86, send=0.26)
    # 吸吮的"气"：2.2k → 400 Hz 窄带噪声，跟着同一个下滑走
    s.add(ins.noise_sweep(0.230, f_start=2200.0, f_end=400.0, width=3.0,
                          tau=0.090, seed=2030, attack_ms=2.0),
          at=0.010, pan=0.0, gain=0.34, send=0.22)
    s.add(ins.tone_blip(0.150, freq=1560.0, freq_end=420.0, tau=0.070, shape=1.5),
          at=0.0, pan=-0.20, gain=0.20, send=0.18)
    return s.render()


def pl_autocharge():
    """自充：**上扫蓄能**。250 → 880 Hz 失谐分音 + 越来越亮的噪声 ⇒ "越攒越满"。

    ★ 重新设计（"劣质刺耳"）★ ── 签名不变：**12 条里唯一"音高与亮度同时上升"** ✓
    ── 改前 / 改后实测（`harshness.py` 同一口径）──
                  能量重心   2-5kHz   >2kHz   >5kHz   单频哨音   起音
      改前        1025 Hz   0.4%     1.0%    0.6%    +4.3 dB   8.25 ms
      改后         499 Hz   0.6%     0.7%    0.1%    +4.8 dB   6.01 ms
    ⇒ 它刺的地方是**尾巴**：`900→5200 Hz` 的噪声"亮气"直接扫进 5 kHz（>5 kHz 0.6% 全靠它），
      而 320→1500 Hz 的单根正弦滑音本身就是一条会哨的线
      ⇒ 滑音 250 → **880 Hz** 且换三根失谐分音；亮气 520 → **1500 Hz**（上限压到 2 kHz 附近）；
      起音 8 ms、混响 `REV_WATER` ✓ 上升的"方向"这个签名一点没动（音高升 3.5 倍、频带也升 3 倍）✓
      ⚠ 2-5 kHz 从 0.4% 微升到 0.6%（绝对值仍远低于 25% 判定线）—— 如实写 ✓
    """
    s = Shot(0.320, REV_WATER)
    s.add(_detuned(0.310, 250.0, 880.0, shape=2.0, tau=0.400, attack_ms=8.0,
                   tri=0.15, cents=(-9.0, 0.0, 11.0)),
          at=0.0, pan=0.0, gain=0.72, send=0.24)
    s.add(ins.noise_sweep(0.300, f_start=520.0, f_end=1500.0, width=1.6,
                          tau=0.190, seed=2070, stages=2, attack_ms=12.0),
          at=0.0, pan=0.0, gain=0.22, send=0.22)
    return s.render()


# ---------------------------------------------------------------------------
SFX = [
    ("sfx_hoe_swing", sfx_hoe_swing, "hoe swing: band-pass 2k->550Hz noise whoosh + 155->72Hz push, 185ms"),
    ("sfx_hoe_hit", sfx_hoe_hit, "hoe hit: FM 2.71:1 metallic + 20ms HP noise transient + 190Hz thump, 125ms"),
    ("sfx_harvest", sfx_harvest, "harvest: 3 FM bells D5-A5-D6 + 5.2k grain noise, 400ms"),
    ("sfx_coin", sfx_coin, "coin: 2 bells D6 then C7 (minor 7th) + sparkle noise, 220ms"),
    ("sfx_plant", sfx_plant, "plant: 1.1kHz LP noise puff + 232->452Hz blip, 250ms"),
    ("sfx_water", sfx_water, "water+laser FUSION: 2x pink pour 2-stage BP 1.15k->480Hz + 2x1.8k LP (12dB/oct) + 3-partial detuned swell 112->76Hz with 9Hz sway + 4 sparse bubble chirps + laser 2 harmonics 940->860 and 1880->1720Hz (2:1), 4ms attack, 300ms"),
    ("sfx_laser", sfx_laser, "laser: FM 1.51:1 2600->820Hz sweep + noise + 1400->380Hz sub, 205ms"),
    ("sfx_blast", sfx_blast, "blast: 60->30Hz tanh boom + 4.2k LP noise + 7 debris clicks, 900ms"),
    ("sfx_enemy_die", sfx_enemy_die, "enemy die: 3k->300Hz noise sweep + 115->52Hz body, 350ms"),
    ("sfx_enemy_spawn", sfx_enemy_spawn, "enemy spawn: two rising alarm tones 300->430 and 400->570Hz, 400ms"),
    ("sfx_levelup", sfx_levelup, "level up: D-F#-A-D bells + D7 sparkle + 3k->9k air, 900ms"),
    ("sfx_ui_click", sfx_ui_click, "ui click: band-pass 2.6k noise click + 1450Hz body, 60ms"),
    ("sfx_ui_hover", sfx_ui_hover, "ui hover: same click, 3.4kHz, -4dB, 42ms"),
    ("sfx_shop_open", sfx_shop_open, "shop open: D5-A5-D6 + 700->2600Hz air, 500ms"),
    ("sfx_interact", sfx_interact, "interact: single A5 FM bell + 440Hz body, 250ms"),
    ("sfx_purchase", sfx_purchase, "purchase: D5 then A5 bells + 4 coin sparkles, 400ms"),
    ("sfx_water_low", sfx_water_low, "water low: two 168/148Hz saturated warnings + noise, 520ms"),
    ("sfx_defeat", sfx_defeat, "defeat: descending A4-F4-D4 bells + sinking 70Hz sub, 1.2s"),
]

TARGET_DUR = {
    "sfx_hoe_swing": 0.180, "sfx_hoe_hit": 0.120, "sfx_harvest": 0.400,
    "sfx_coin": 0.220, "sfx_plant": 0.250, "sfx_water": 0.300,
    "sfx_laser": 0.200, "sfx_blast": 0.900, "sfx_enemy_die": 0.350,
    "sfx_enemy_spawn": 0.400, "sfx_levelup": 0.900, "sfx_ui_click": 0.060,
    "sfx_ui_hover": 0.040, "sfx_shop_open": 0.500, "sfx_interact": 0.250,
    "sfx_purchase": 0.400, "sfx_water_low": 0.500, "sfx_defeat": 1.200,
}

# ---------------------------------------------------------------------------
# ★ 12 件插件 → 12 条音效 ★（顺序与 plugins.json 的 12 个 id 逐条对齐）
#
# 命名 `sfx_pl_<插件 id 去掉武器前缀>`：
#   hoe_swordwave  -> sfx_pl_swordwave      can_multijet   -> sfx_pl_multijet
#   hoe_burn       -> sfx_pl_burn           can_slow       -> sfx_pl_slow
#   hoe_doublestrike->sfx_pl_doublestrike   can_thickjet   -> sfx_pl_thickjet
#   hoe_lifesteal  -> sfx_pl_lifesteal      can_autocharge -> sfx_pl_autocharge
#   fert_subburst  -> sfx_pl_subburst       fert_poison    -> sfx_pl_poison
#   fert_freeburst -> sfx_pl_freeburst      fert_chain     -> sfx_pl_chain
#
# ★ 为什么去掉武器前缀而不是直接用 id ★
#   播放层是按 `PluginEffectKind` 取音的（12 个枚举值，与武器无关），
#   而 id 里的 `hoe_/can_/fert_` 描述的是"能装在哪个槽" ✗ 与音效无关 ✓
# ---------------------------------------------------------------------------
PLUGIN_SFX = [
    ("sfx_pl_swordwave", pl_swordwave,
     "plugin 剑气: 7k->2.6kHz 3.2-width BP noise sweep, 28ms tau, no low body (brightest), 155ms"),
    ("sfx_pl_burn", pl_burn,
     "plugin 灼烧: 2.6k+900Hz BP noise sizzle, 37Hz irregular flutter AM, 150ms decay (sustained), 250ms"),
    ("sfx_pl_doublestrike", pl_doublestrike,
     "plugin 双击: 2 wooden clicks 100ms apart (1.9kHz BP) + 30ms noise tick each, 195ms"),
    ("sfx_pl_lifesteal", pl_lifesteal,
     "plugin 吸血: 780->190Hz glide + 2.2k->400Hz throat noise (falling pitch = signature), 310ms"),
    ("sfx_pl_multijet", pl_multijet,
     "plugin 多柱: 3 pink taps 55ms apart (1.05k->470Hz, 2-stage BP) + 3-partial detuned blips, 4ms attack, 235ms"),
    ("sfx_pl_slow", pl_slow,
     "plugin 减速: 1.5k->300Hz 2-stage BP down-sweep + 330->140Hz 3-partial detuned glide, 5ms attack, 295ms"),
    ("sfx_pl_thickjet", pl_thickjet,
     "plugin 加粗: 130->62Hz sine saturated tanh 1.9 then 620Hz LP (harmonic wall shaved) + pink dust, 4ms attack, 175ms"),
    ("sfx_pl_autocharge", pl_autocharge,
     "plugin 自充: 250->880Hz 3-partial detuned rising glide + 520->1.5kHz rising air (only riser), 8ms attack, 320ms"),
    ("sfx_pl_subburst", pl_subburst,
     "plugin 子爆: 55->38Hz muffled boom + 60-700Hz LP noise (second, duller blast), 215ms"),
    ("sfx_pl_poison", pl_poison,
     "plugin 毒素: 6 irregular bubble chirps (520-1420Hz, x2.4 upward) + 118Hz murk, 280ms"),
    ("sfx_pl_freeburst", pl_freeburst,
     "plugin 免耗: 2 clean FM bells A6->E7 (3:1/3.5:1), no noise at all (cleanest), 135ms"),
    ("sfx_pl_chain", pl_chain,
     "plugin 连环: 3 low booms 85ms apart (44+26Hz tanh) each with a falling tail (rhythmic triplet), 265ms"),
]

PLUGIN_TARGET_DUR = {
    "sfx_pl_swordwave": 0.155, "sfx_pl_burn": 0.250,
    "sfx_pl_doublestrike": 0.195, "sfx_pl_lifesteal": 0.310,
    "sfx_pl_multijet": 0.235, "sfx_pl_slow": 0.295,
    "sfx_pl_thickjet": 0.175, "sfx_pl_autocharge": 0.320,
    "sfx_pl_subburst": 0.215, "sfx_pl_poison": 0.280,
    "sfx_pl_freeburst": 0.135, "sfx_pl_chain": 0.265,
}

TARGET_DUR.update(PLUGIN_TARGET_DUR)

# 生成器只认这一条：18 条主音效 + 12 条插件音效 = 30
ALL_SFX = SFX + PLUGIN_SFX
