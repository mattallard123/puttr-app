#!/usr/bin/env python3
"""dubstep.py [out.wav] [seconds] [bars]: the reel's dubstep track (15 seconds, bars ABCDE, unless told otherwise;
the 20-second cut is `dubstep.py dubstep-140-20s.wav 20 ABCDACDE`), synthesised here (numpy only), so it is ours to post.

140 BPM, F minor, 48 kHz stereo. The same beat grid the reel is cut on (edit-reel.json, "bpm": 140):
  beats  0-8   build: impact, rising chord, noise riser, snare roll that doubles every two beats, a half-beat gap
  beats  8-28  drop, half time: kick on 1, snare on 3, wobble and growl bass (BARS below is the pattern)
  beat  28     the last hit, sub drop and tail to 15 s
Change BARS and the video cuts in edit-reel.json together: every bass note starts on a cut."""
import sys, wave, pathlib
import numpy as np

SR, BPM = 48000, 140.0
SECONDS = float(sys.argv[2]) if len(sys.argv) > 2 else 15.0
BEAT = 60.0 / BPM
N = int(SR * SECONDS)
rng = np.random.default_rng(7)
F0 = 43.654  # F1

def secs(beats): return int(round(beats * BEAT * SR))
def buf(): return np.zeros(N)
def put(dst, src, at, gain=1.0):
    if at >= N: return
    n = min(len(src), N - at)
    dst[at:at + n] += src[:n] * gain

def fft_filter(x, lo=0.0, hi=1e9, slope=0.25):
    """Zero-phase band filter with soft edges (slope in octaves)."""
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR) + 1e-9
    g = np.ones_like(f)
    if lo > 0: g *= 1 / (1 + (lo / f) ** (2 / slope))
    if hi < 1e8: g *= 1 / (1 + (f / hi) ** (2 / slope))
    return np.fft.irfft(X * g, len(x))

def reverb(x, seconds=1.6, damp=3500, seed=3):
    r = np.random.default_rng(seed)
    n = int(SR * seconds); t = np.arange(n) / SR
    ir = r.standard_normal(n) * np.exp(-t * 6.9 / seconds)
    ir = fft_filter(ir, 250, damp); ir[: int(SR * 0.012)] = 0
    m = len(x) + n
    y = np.fft.irfft(np.fft.rfft(x, m) * np.fft.rfft(ir, m), m)[: len(x)]
    return y / (np.abs(y).max() + 1e-9) * np.abs(x).max()

# ---- drums ----
def kick():
    n = int(SR * 0.42); t = np.arange(n) / SR
    f = 46 + 190 * np.exp(-t * 38)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 7.5)
    click = fft_filter(rng.standard_normal(n), 1500, 9000) * np.exp(-t * 260) * 0.5
    return np.tanh((body + click) * 2.2)

def snare(big=True):
    n = int(SR * 0.36); t = np.arange(n) / SR
    tone = (np.sin(2 * np.pi * 196 * t) + 0.6 * np.sin(2 * np.pi * 287 * t)) * np.exp(-t * 26)
    noise = fft_filter(rng.standard_normal(n), 900, 11000) * np.exp(-t * (13 if big else 24))
    clap = fft_filter(rng.standard_normal(n), 1100, 4200) * (np.exp(-((t - 0.012) * 240) ** 2) + np.exp(-((t - 0.026) * 240) ** 2) + np.exp(-t * 20))
    return np.tanh((tone * 0.9 + noise * 0.9 + clap * 0.5) * 1.8)

def hat(open_=False):
    n = int(SR * (0.22 if open_ else 0.05)); t = np.arange(n) / SR
    return fft_filter(rng.standard_normal(n), 7000, 16000) * np.exp(-t * (16 if open_ else 90))

def impact():
    n = int(SR * 2.6); t = np.arange(n) / SR
    f = 34 + 70 * np.exp(-t * 5)
    sub = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 1.9)
    crash = fft_filter(rng.standard_normal(n), 300, 12000) * np.exp(-t * 3.2)
    return np.tanh(sub * 1.6) + crash * 0.5

# ---- bass: additive saw through a moving resonant low-pass (no aliasing, so it can be driven hard) ----
def bass(beats, kind, rate, semis=0.0, slide=0.0):
    """kind: wob (LFO opens the filter `rate` times a beat), growl (FM vowel), stab (short open bark), saw (held)."""
    n = secs(beats); t = np.arange(n) / SR; tb = t / BEAT
    f = F0 * 2 ** ((semis + slide * tb / max(beats, 1e-6)) / 12.0)
    ph = 2 * np.pi * np.cumsum(f) / SR
    lfo = 0.5 - 0.5 * np.cos(2 * np.pi * rate * tb)
    if kind == 'wob': fc = 110 + 3400 * lfo ** 1.7
    elif kind == 'stab': fc = 300 + 5200 * np.exp(-t * 9)
    elif kind == 'saw': fc = 500 + 2600 * (tb / max(beats, 1e-6))
    else: fc = 240 + 1500 * lfo
    Q = 3.2
    y = np.zeros(n)
    top = 9000.0
    for k in range(1, int(top / (F0 * 2)) + 1):
        fk = f * 2 * k   # the saw sits an octave above the sub
        r = fk / fc
        g = 1.0 / np.sqrt((1 - r * r) ** 2 + (r / Q) ** 2)
        y += np.sin(2 * k * ph + 0.3 * k) * g / k * (fk < top)
    if kind == 'growl':
        # an FM voice with two moving formants: the "yoi" over the saw
        idx = 2.5 + 7.0 * lfo
        fm = np.sin(2 * ph + idx * np.sin(1 * ph) + 0.8 * np.sin(3.01 * ph))
        f1, f2 = 350 + 550 * lfo, 900 + 1500 * (1 - lfo)
        fm = fm * (0.6 + 0.4 * np.sin(2 * np.pi * np.cumsum(f1) / SR)) + 0.5 * np.sin(2 * np.pi * np.cumsum(f2) / SR) * fm
        y = 0.45 * y + 1.1 * fm
    y = np.tanh(y * 1.7)
    y = np.tanh(y * 1.3 + 0.25 * y ** 2)
    sub = np.sin(ph) * (0.9 if kind != 'stab' else 0.6)
    env = np.minimum(1, t / 0.004) * np.minimum(1, (n / SR - t) / 0.012)
    if kind == 'wob': env = env * (0.55 + 0.45 * lfo)
    return y * env, sub * np.minimum(1, t / 0.004) * np.minimum(1, (n / SR - t) / 0.012)

# The drop, one row a bar: (start beat in the bar, length in beats, kind, rate, semitones, slide).
A = [(0, 1, 'wob', 2, 0, 0), (1, .5, 'stab', 0, 12, 0), (1.5, .5, 'stab', 0, 12, -5), (2, 1, 'wob', 4, 0, 0), (3, 1, 'wob', 3, 3, 0)]
B = [(0, 1, 'growl', 1, 0, -2), (1, 1, 'wob', 6, -2, 0), (2, 1, 'growl', 2, 0, 0), (3, .5, 'stab', 0, 15, 0), (3.5, .5, 'stab', 0, 10, -7)]
C = [(0, 1, 'wob', 2, 0, 0), (1, .5, 'growl', 4, 7, 0), (1.5, .5, 'growl', 4, 5, 0), (2, 1, 'wob', 8, 0, 0), (3, 1, 'growl', 1, 3, -3)]
D = [(0, 1, 'growl', 2, 0, 0), (1, 1, 'wob', 3, 0, 0), (2, .5, 'stab', 0, 12, 0), (2.5, .5, 'stab', 0, 12, 0), (3, 1, 'wob', 6, 5, 0)]
E = [(0, 1, 'wob', 4, 0, 0), (1, 1, 'growl', 3, 0, 5), (2, .5, 'stab', 0, 12, 0), (2.5, .5, 'stab', 0, 15, 0), (3, .25, 'stab', 0, 17, 0), (3.25, .25, 'stab', 0, 17, 0), (3.5, .25, 'stab', 0, 19, 0)]
BARS = [dict(A=A, B=B, C=C, D=D, E=E)[k] for k in (sys.argv[3] if len(sys.argv) > 3 else 'ABCDE')]
DROP = 8
END = DROP + 4 * len(BARS)   # the last hit

def build():
    mid, low, drums, wet, hats = buf(), buf(), buf(), buf(), buf()
    K, S, Ssmall, H, Ho, I = kick(), snare(), snare(False), hat(), hat(True), impact()
    duck_at = []

    # --- build, beats 0-8 ---
    put(wet, I, 0, 0.8)
    nb = secs(DROP); t = np.arange(nb) / SR; u = t / (DROP * BEAT)
    chord = np.zeros(nb)
    for semi in (0, 3, 7, 12, 15):            # F minor, climbing an octave by the drop
        for det in (-0.12, 0, 0.11):
            f = F0 * 4 * 2 ** ((semi + det + 12 * u ** 3) / 12.0)
            chord += np.sin(2 * np.pi * np.cumsum(f) / SR + rng.uniform(0, 6))
    gate = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * t / BEAT * np.where(u < .5, 2, 4)))   # trance gate, 8ths then 16ths
    chord = np.tanh(chord * 0.25) * u ** 1.5 * (0.35 + 0.65 * gate)
    noise = rng.standard_normal(nb)
    # riser: band-passed noise sweeping up (state variable filter, sample by sample)
    riser = np.zeros(nb); lp = bp = 0.0
    fcs = 300 * (30 ** u)
    for i in range(nb):
        fq = 2 * np.sin(np.pi * min(fcs[i], 11000) / SR)
        hp = noise[i] - lp - 0.35 * bp
        bp += fq * hp; lp += fq * bp
        riser[i] = bp
    riser *= u ** 2 * 0.5
    stop = secs(DROP - 0.5)                    # the half-beat of nothing before the drop
    cut = np.ones(nb); cut[stop:] = 0; cut[stop - 200:stop] = np.linspace(1, 0, 200)
    put(mid, chord * cut, 0, 0.55); put(wet, riser * cut, 0, 0.6)
    b = 2.0
    while b < DROP - 0.5 - 1e-6:               # snare roll: quarters, eighths, sixteenths, thirty-seconds
        step = 1 if b < 4 else .5 if b < 6 else .25 if b < 7 else .125
        put(drums, Ssmall, secs(b), 0.35 + 0.5 * (b / DROP)); b += step
    put(drums, K, secs(0), 1.0); duck_at.append(0.0)
    for kb in (2, 4, 5, 6, 6.5, 7): put(drums, K, secs(kb), 0.75)

    # --- drop, beats 8-28 ---
    for bar, rows in enumerate(BARS):
        b0 = DROP + bar * 4
        for kb in ((0, 1.5) if bar % 2 == 0 else (0, 2.5, 3.5)):
            if rows is E and kb > 2: continue
            put(drums, K, secs(b0 + kb), 1.0); duck_at.append(b0 + kb)
        put(drums, S, secs(b0 + 2), 1.0); put(wet, S, secs(b0 + 2), 0.22); duck_at.append(b0 + 2)
        for e in range(8):
            if rows is E and e >= 6: continue
            put(hats, Ho if e % 4 == 3 else H, secs(b0 + e * .5), 0.5 if e % 2 else 0.3)
        if rows is E:
            for q in (3, 3.25, 3.5, 3.75): put(drums, Ssmall, secs(b0 + q), 0.8)
        for (at, length, kind, rate, semis, slide) in rows:
            m, s = bass(length, kind, rate, semis, slide)
            put(mid, m, secs(b0 + at), 0.8); put(low, s, secs(b0 + at), 0.8)
    put(wet, I, secs(DROP), 0.7)

    # --- the last hit ---
    put(drums, K, secs(END), 1.0); put(wet, I, secs(END), 1.0); put(wet, S, secs(END), 0.5)
    n = N - secs(END); t = np.arange(n) / SR
    f = F0 * 2 * 2 ** (-t * 1.2)
    put(low, np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 1.6), secs(END), 0.9)

    # sidechain: the bass ducks under every kick and snare
    duck = np.ones(N); tt = np.arange(int(SR * 0.3)) / SR
    for db in duck_at:
        a = secs(db); m = min(len(tt), N - a)
        duck[a:a + m] = np.minimum(duck[a:a + m], 1 - 0.75 * np.exp(-tt[:m] / 0.07))
    mid *= duck; low *= duck

    wetv = reverb(wet, 1.8)
    # stereo: sub and drums in the middle, the mid bass and hats a touch wide (a few ms apart on each side)
    d = int(SR * 0.006)
    mid_hp = fft_filter(mid, 180)
    L = low * 0.85 + drums * 0.9 + mid * 0.62 + np.roll(mid_hp, d) * 0.22 + hats * 0.5 + wet * 0.55 + wetv * 0.5
    R = low * 0.85 + drums * 0.9 + mid * 0.62 - np.roll(mid_hp, d) * 0.22 + np.roll(hats, d // 2) * 0.5 + wet * 0.55 + np.roll(wetv, 3 * d) * 0.5
    out = np.stack([L, R], 1)
    out = np.tanh(out * 1.15 / np.percentile(np.abs(out), 99.7))   # soft clip as the limiter
    out *= 0.89 / np.abs(out).max()
    fade = int(SR * 0.25); out[-fade:] *= np.linspace(1, 0, fade)[:, None]
    return out

if __name__ == '__main__':
    path = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).with_name('dubstep-140.wav'))
    audio = build()
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((audio * 32767).astype('<i2').tobytes())
    print(path, f'{len(audio) / SR:.2f}s', 'peak', round(float(np.abs(audio).max()), 3), 'rms', round(float(np.sqrt((audio ** 2).mean())), 3))
