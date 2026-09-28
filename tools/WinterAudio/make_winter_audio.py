"""Act 22's sounds (the winter woods, the wendigo, the lodge's back door), made offline with numpy.

The wendigo's voice (the owner: "mimicking human voices but sounding fake, like it's a copy"): plain human
lines (src/, spoken by the Windows voices, plus the game's own recorded "come and see" / "turn around"),
pulled wrong: pitched down, a grain of it caught and repeated like a skip, the end dragged slow, a dry rasp
breathing under it, a reversed ghost of the last word after it, and dulled by distance through the trees.

    python tools/WinterAudio/make_winter_audio.py
writes assets/audio/sfx/wendigo_mimic_XX.wav, wendigo_leap_XX.wav, snow_whump_XX.wav, ice_crack_XX.wav,
ice_tinkle_XX.wav, wendigo_step_XX.wav (44.1 kHz mono 16-bit).
"""
import os, subprocess, wave
import numpy as np
import imageio_ffmpeg

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
OUT = os.path.join(ROOT, "assets", "audio", "sfx")
SR = 44100
rng = np.random.default_rng(2222)
FF = imageio_ffmpeg.get_ffmpeg_exe()


def load(path):
    raw = subprocess.run([FF, "-v", "error", "-i", path, "-ac", "1", "-ar", str(SR), "-f", "s16le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.int16).astype(np.float32) / 32768.0


def save(name, x):
    x = np.asarray(x, np.float32)
    peak = np.max(np.abs(x)) + 1e-9
    x = x / peak * 0.89
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())


def fft_filter(x, lo=None, hi=None, tilt=0.0):
    n = len(x)
    f = np.fft.rfft(x)
    fr = np.fft.rfftfreq(n, 1 / SR)
    g = np.ones_like(fr)
    if hi: g *= 1 / np.sqrt(1 + (fr / hi) ** 4)
    if lo: g *= 1 / np.sqrt(1 + (lo / np.maximum(fr, 1)) ** 4)
    if tilt: g *= (np.maximum(fr, 50) / 1000) ** tilt
    return np.fft.irfft(f * g, n)


def resample(x, factor):
    """Plays x at `factor` speed (pitch and time together, like a tape)."""
    n = int(len(x) / factor)
    return np.interp(np.arange(n) * factor, np.arange(len(x)), x)


def env(x, win=512):
    e = np.sqrt(np.convolve(x * x, np.ones(win) / win, mode="same"))
    return e / (e.max() + 1e-9)


def reverb(x, wet=0.35, taps=((0.031, 0.5), (0.057, 0.38), (0.093, 0.3), (0.141, 0.22), (0.23, 0.14), (0.37, 0.08))):
    out = np.concatenate([x, np.zeros(int(SR * 0.9))])
    for t, g in taps:
        d = int(t * SR)
        out[d:d + len(x)] += x * g * wet * 2
    return out


def trim(x, thr=0.02):
    idx = np.where(np.abs(x) > thr * np.max(np.abs(x)))[0]
    return x[max(0, idx[0] - 800): idx[-1] + 2000] if len(idx) else x


def mimic(x, pitch, seed):
    r = np.random.default_rng(seed)
    x = trim(x)
    x = resample(x, pitch)                       # down: bigger than a person, slower
    # the skip: a grain of it caught and played again, two or three times (a copy with a flaw in it)
    e = env(x)
    voiced = np.where(e > 0.35)[0]
    if len(voiced) > SR // 4:
        at = int(voiced[int(len(voiced) * r.uniform(0.15, 0.45))])
        g = int(SR * r.uniform(0.07, 0.11))
        grain = x[at:at + g] * np.hanning(g)
        reps = int(r.integers(2, 4))
        x = np.concatenate([x[:at + g]] + [grain] * reps + [x[at + g:]])
    # the drag: the last third slowed to a drawl
    cut = int(len(x) * 0.68)
    x = np.concatenate([x[:cut], resample(x[cut:], 0.8)])
    # the rasp: dry breath under the words, following their loudness
    noise = fft_filter(r.standard_normal(len(x)).astype(np.float32), lo=900, hi=5000)
    x = x + noise * env(x, 2048) * 0.09
    # a faint ring on it: almost a person, not quite
    t = np.arange(len(x)) / SR
    x = x * (0.85 + 0.15 * np.sin(2 * np.pi * 38 * t))
    # the reversed ghost of the last word, after it
    tail = x[-int(SR * 0.45):][::-1] * np.linspace(0, 0.35, int(SR * 0.45))
    x = np.concatenate([x, np.zeros(int(SR * 0.12)), tail])
    # far off through the trees
    x = fft_filter(x, lo=140, hi=3200)
    return reverb(x)


def gen_mimics():
    names = ["hello", "is_someone_there", "help_me", "wait_for_me", "over_here", "im_so_cold", "come_back",
             "dont_leave_me", "i_can_see_you", "its_warm_in_here", "are_you_hungry", "turn_around"]
    srcs = [os.path.join(HERE, "src", f"{n}.wav") for n in names]
    # the game's own recorded voice, copied badly too
    srcs += [os.path.join(ROOT, "assets", "audio", "voice", f) for f in ("come_and_see_medium.mp3", "turn_around_distant.mp3")]
    i = 0
    for k, p in enumerate(srcs):
        x = load(p)
        pitch = 0.82 if "Zira" in p else 0.9
        if k in (2, 4, 6, 8, 10): pitch = 0.78          # the women's lines lower still: a man's throat doing a woman's voice
        save(f"wendigo_mimic_{k + 1:02}.wav", mimic(x, pitch, 100 + k))
        i += 1
    print(i, "mimic lines")


def gen_leap():
    # up into the trees faster than anything should move: a tearing whoosh rising, the bough taking the weight
    for v in range(3):
        n = int(SR * 1.6)
        t = np.arange(n) / SR
        w = rng.standard_normal(n).astype(np.float32)
        whoosh = fft_filter(w, lo=300, hi=2600) * np.exp(-((t - 0.12) / 0.07) ** 2)
        crack = np.zeros(n, np.float32)
        for c in range(int(rng.integers(5, 9))):
            at = int(SR * (0.17 + rng.uniform(0, 0.12)))
            L = int(SR * 0.012)
            crack[at:at + L] += rng.standard_normal(L) * np.exp(-np.arange(L) / (L / 4)) * rng.uniform(0.4, 1)
        creak_f = 190 + 60 * v
        creak = np.sin(2 * np.pi * (creak_f * t + 25 * np.sin(2 * np.pi * 3 * t))) * np.exp(-((t - 0.45) / 0.22) ** 2) * 0.35
        creak *= (1 + 0.5 * np.sign(np.sin(2 * np.pi * 31 * t)))
        sift = fft_filter(rng.standard_normal(n).astype(np.float32), lo=2500, hi=9000) * np.clip((t - 0.2) / 0.1, 0, 1) * np.exp(-(t - 0.2) / 0.5) * 0.5
        x = whoosh * 1.2 + fft_filter(crack, lo=500) * 0.8 + creak + sift
        save(f"wendigo_leap_{v + 1:02}.wav", reverb(x, 0.25))


def gen_whump():
    # a clump of snow sliding off a bough and landing
    for v in range(3):
        n = int(SR * 1.2)
        t = np.arange(n) / SR
        slide = fft_filter(rng.standard_normal(n).astype(np.float32), lo=1500, hi=7000) * np.exp(-((t - 0.15) / 0.12) ** 2) * 0.3
        land = fft_filter(rng.standard_normal(n).astype(np.float32), hi=260) * np.clip((t - 0.38) / 0.01, 0, 1) * np.exp(-np.maximum(t - 0.38, 0) / 0.09) * 2.5
        save(f"snow_whump_{v + 1:02}.wav", slide + land)


def gen_ice_crack():
    # ice giving way round a door: a deep split, splintering, shards ringing as they fall
    for v in range(3):
        n = int(SR * 2.2)
        t = np.arange(n) / SR
        x = np.zeros(n, np.float32)
        for c in range(int(rng.integers(14, 22))):
            at = int(SR * rng.uniform(0.0, 0.45) ** 1.5)
            L = int(SR * rng.uniform(0.004, 0.02))
            x[at:at + L] += rng.standard_normal(L) * np.exp(-np.arange(L) / (L / 3)) * rng.uniform(0.3, 1)
        x = fft_filter(x, lo=700)
        boom = np.sin(2 * np.pi * 55 * t) * np.exp(-t / 0.18) * 0.6
        ring = np.zeros(n, np.float32)
        for c in range(int(rng.integers(8, 14))):
            f = rng.uniform(2500, 6500)
            at = rng.uniform(0.3, 1.4)
            ring += np.sin(2 * np.pi * f * t) * np.exp(-np.maximum(t - at, 0) / 0.05) * (t > at) * rng.uniform(0.05, 0.15)
        save(f"ice_crack_{v + 1:02}.wav", reverb(x + boom + ring, 0.3))


def gen_tinkle():
    # icicles knocking together in the wind, near the lodge (soft)
    for v in range(3):
        n = int(SR * 3.0)
        t = np.arange(n) / SR
        x = np.zeros(n, np.float32)
        for c in range(int(rng.integers(5, 9))):
            f = rng.uniform(3000, 7000)
            at = rng.uniform(0, 2.4)
            x += np.sin(2 * np.pi * f * t) * np.exp(-np.maximum(t - at, 0) / 0.08) * (t > at) * rng.uniform(0.1, 0.3)
        save(f"ice_tinkle_{v + 1:02}.wav", reverb(x, 0.4))


def gen_steps():
    # its footfalls: barely there (the owner: quiet, unlike the other monsters): a soft press into snow
    for v in range(6):
        n = int(SR * 0.5)
        t = np.arange(n) / SR
        crunch = fft_filter(rng.standard_normal(n).astype(np.float32), lo=600, hi=3500)
        grains = (rng.random(n) < 0.02).astype(np.float32)
        x = crunch * np.exp(-((t - 0.1) / 0.06) ** 2) * 0.4 + fft_filter(grains, lo=1500) * np.exp(-((t - 0.12) / 0.07) ** 2)
        x += fft_filter(rng.standard_normal(n).astype(np.float32), hi=180) * np.exp(-((t - 0.08) / 0.03) ** 2) * 0.8
        save(f"wendigo_step_{v + 1:02}.wav", x)


if __name__ == "__main__":
    gen_mimics(); gen_leap(); gen_whump(); gen_ice_crack(); gen_tinkle(); gen_steps()
    print("done")
