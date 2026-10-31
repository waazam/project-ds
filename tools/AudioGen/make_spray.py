"""A spray can, somewhere behind them (2026-10-31: the graffiti that changes when they look away is heard being done):
a few shakes of the can (the mixing ball knocking), then a short hiss of paint going on, its pressure falling away.

    python tools/AudioGen/make_spray.py

Writes assets/audio/sfx/spray_hiss_01..03.wav (mono, 22050 Hz, 16-bit). Seeded: the same every run.
"""
import os
import wave
import numpy as np

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "audio", "sfx")
SR = 22050


def lowpass(x, a):
    y = np.zeros_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc += a * (v - acc)
        y[i] = acc
    return y


def make(seed):
    r = np.random.default_rng(seed)
    parts = []
    # the shakes: the ball knocking the can's walls, a hollow tick each, in a quick run
    shakes = r.integers(3, 6)
    for k in range(shakes):
        n = int(SR * r.uniform(0.09, 0.14))
        t = np.arange(n) / SR
        f = r.uniform(2400, 3200)
        tick = np.sin(2 * np.pi * f * t) * np.exp(-t * 90) + 0.5 * np.sin(2 * np.pi * f * 1.6 * t) * np.exp(-t * 140)
        tick += r.normal(0, 0.15, n) * np.exp(-t * 200)
        parts.append(tick * r.uniform(0.35, 0.55))
    parts.append(np.zeros(int(SR * r.uniform(0.15, 0.3))))
    # the hiss: bright noise, a breathy onset, a slow sag in pressure, the valve's click off
    n = int(SR * r.uniform(0.9, 1.6))
    t = np.arange(n) / SR
    noise = r.normal(0, 1, n)
    hiss = noise - lowpass(noise, 0.18)                           # (high-passed: aerosol, not wind)
    env = np.minimum(t / 0.04, 1.0) * (1.0 - 0.45 * t / t[-1]) * np.minimum((t[-1] - t) / 0.03, 1.0)
    flutter = 1.0 + 0.12 * np.sin(2 * np.pi * r.uniform(5, 9) * t)
    parts.append(hiss * env * flutter * 0.3)
    x = np.concatenate(parts)
    x = x / max(1e-6, np.abs(x).max()) * 0.7
    return (x * 32767).astype(np.int16)


for i in range(3):
    path = os.path.join(OUT, f"spray_hiss_{i + 1:02d}.wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(make(300 + i).tobytes())
    print(f"[spray] {path}")
