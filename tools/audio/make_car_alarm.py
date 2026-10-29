"""The van's alarm (2026-10-29, the owner's friend's playtest: at the fallen tree past the bridge "they hear their car alarm
going off in the distance"). No recording in the game's sounds, so it is made here: a mid-90s six-tone siren as the
aftermarket alarms of the time did it, through one small horn speaker under the bonnet, cycling its tones a few seconds
each: the slow whoop (a rising sweep), the fast warble, the two-tone hi-lo, and the machine-gun chirp. One 16 s loop,
mono, its ends meeting (it is looped by Act1Walks while the alarm goes).

    python tools/audio/make_car_alarm.py
"""
import os, wave
import numpy as np

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(ROOT, "assets", "audio", "sfx", "car_alarm_loop.wav")
SR = 44100
rng = np.random.default_rng(217)


def tone(freq):
    """A buzzy square-ish wave following the frequency track (the cheap piezo-driven horn)."""
    phase = np.cumsum(2 * np.pi * freq / SR)
    sq = np.tanh(4.0 * np.sin(phase))
    return sq * 0.75 + 0.25 * np.sin(2 * phase)


def section(kind, secs):
    n = int(secs * SR)
    t = np.arange(n) / SR
    if kind == "whoop":     # a rising sweep, 0.55 s, again and again
        u = (t % 0.55) / 0.55
        f = 520 + 1250 * u ** 1.4
        env = np.ones(n)
    elif kind == "warble":  # a fast wobble between two pitches
        f = 1050 + 330 * np.sin(2 * np.pi * 9.0 * t)
        env = np.ones(n)
    elif kind == "hilo":    # two tones, a quarter second each
        f = np.where((t % 0.5) < 0.25, 1400.0, 900.0)
        env = np.ones(n)
    else:                   # chirps: short sharp beeps
        f = np.full(n, 1250.0)
        env = ((t % 0.11) < 0.06).astype(float)
    k = int(0.004 * SR)     # (no clicks at the gates)
    env = np.convolve(env, np.ones(k) / k, mode="same")
    return tone(f) * env


def horn(x):
    """The little horn: a band of 600-4000 Hz with a nasal resonance, a little rattle."""
    spec = np.fft.rfft(x)
    fr = np.fft.rfftfreq(len(x), 1 / SR)
    band = 1 / (1 + (600 / np.maximum(fr, 1)) ** 4) / (1 + (fr / 4000) ** 4)
    reso = 1 + 1.4 * np.exp(-((fr - 1900) / 380) ** 2)
    y = np.fft.irfft(spec * band * reso, len(x))
    y += 0.02 * rng.standard_normal(len(x)) * np.abs(y)
    return y


parts = [section("whoop", 4.4), section("warble", 3.6), section("hilo", 4.0), section("chirp", 4.0)]
x = horn(np.concatenate(parts))
# the loop's seam: the last 30 ms faded into the first
f = int(0.03 * SR)
x[:f] = x[:f] * np.linspace(0, 1, f) + x[-f:] * np.linspace(1, 0, f)
x = x[:-f]
x = x / np.max(np.abs(x)) * 0.89
with wave.open(OUT, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((x * 32767).astype(np.int16).tobytes())
print(f"[alarm] {len(x) / SR:.1f} s -> {OUT}")
