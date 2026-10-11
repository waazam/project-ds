"""The climb in the dark (2026-10-11): the sound under the black while the hollow is built after Act 1's stairs, so the load
is the climb going on, not a silence. Mixed here from the game's own sounds into one 24 s loop (it must play on by itself:
while a level builds, nothing else in the game can run): footsteps on wooden stairs slowing and labouring, breath in and out
with them, the stairs' hum under it, a heartbeat far down in it.

    python tools/audio/make_climb_loop.py
"""
import glob, os, wave
import numpy as np

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
SFX = os.path.join(ROOT, "assets", "audio", "sfx")
AMB = os.path.join(ROOT, "assets", "audio", "ambient")
SR = 44100
LEN = 24.0
rng = np.random.default_rng(93)


def read(path):
    with wave.open(path) as w:
        sr, ch, n, width = w.getframerate(), w.getnchannels(), w.getnframes(), w.getsampwidth()
        raw = w.readframes(n)
    a = np.frombuffer(raw, dtype={2: np.int16, 4: np.int32}[width]).astype(np.float32)
    a /= 32768.0 if width == 2 else 2147483648.0
    if ch > 1:
        a = a.reshape(-1, ch).mean(axis=1)
    if sr != SR:
        idx = np.arange(0, len(a), sr / SR)
        a = np.interp(idx, np.arange(len(a)), a)
    return a


out = np.zeros(int(SR * LEN), np.float32)


def add(sig, at, gain):
    i = int(at * SR) % len(out)
    n = len(sig)
    end = i + n
    if end <= len(out):
        out[i:end] += sig * gain
    else:   # wrap round the loop's seam
        k = len(out) - i
        out[i:] += sig[:k] * gain
        out[: n - k] += sig[k:] * gain


steps = [read(p) for p in sorted(glob.glob(os.path.join(SFX, "step_wood_*.wav")))]
bin_ = [read(p) for p in sorted(glob.glob(os.path.join(SFX, "breath_in_*.wav")))]
bout = [read(p) for p in sorted(glob.glob(os.path.join(SFX, "breath_out_*.wav")))]
hum = read(os.path.join(AMB, "stairs_hum_loop.wav"))
heart = read(os.path.join(AMB, "heartbeat_loop.wav"))

# the steps: one each 0.82 s, a little uneven, every fourth a scuff (quieter, later)
t = 0.3
k = 0
while t < LEN - 0.05:
    s = steps[rng.integers(len(steps))]
    add(s, t, 0.55 * rng.uniform(0.8, 1.05) * (0.6 if k % 4 == 3 else 1.0))
    t += 0.82 + rng.uniform(-0.05, 0.09)
    k += 1
# the breath: in and out, every 3 s, labouring
t = 0.0
while t < LEN - 0.1:
    add(bin_[rng.integers(len(bin_))], t, 0.42)
    add(bout[rng.integers(len(bout))], t + 1.35, 0.4)
    t += 3.0 + rng.uniform(-0.2, 0.2)
# the hum under it, and the heart far down (tiled to the loop)
def tile(sig):
    reps = int(np.ceil(len(out) / len(sig)))
    return np.tile(sig, reps)[: len(out)]
out += tile(hum) * 0.35 + tile(heart) * 0.22

# a soft seam: the loop's last 0.3 s faded into its first
x = int(0.3 * SR)
fade = np.linspace(0, 1, x, dtype=np.float32)
out[:x] = out[:x] * fade + out[-x:] * (1 - fade)
out = out[: len(out) - x]
peak = np.max(np.abs(out)) or 1.0
out = out / peak * 0.7
pcm = (np.clip(out, -1, 1) * 32767).astype(np.int16)
with wave.open(os.path.join(AMB, "climb_dark_loop.wav"), "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
print("wrote climb_dark_loop.wav", len(pcm) / SR, "s")
