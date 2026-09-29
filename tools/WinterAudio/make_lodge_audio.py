"""Act 23's sounds (the ski lodge), made offline with numpy, in the same way as make_winter_audio.py.

- wendigo_room_XX:   its copied voice through a hotel door (room 202): "it's too cold", "I'm starving",
                     "always... hungry" - pulled wrong like its other lines, then muffled by the wood
- wendigo_mumble_XX: low, slurred, outside a window (room 203): "freezing", "too cold", "hungry", "starved"
- wendigo_laugh_XX:  01 a long low laugh from outside (the second table), 02 a short faint one (the third),
                     03 a big one for the fourth table (it's played moving round the dining hall)
- card_beep, card_error, reader_zap:  the rooms' keycard readers
- mop_clatter:  a mop handle falling on a tiled floor (the pantry)
- roach_skitter: a swarm of small legs on a tablecloth and wood
- table_collapse: a long table giving way: splintering, a heavy slam, the legs clattering
- snow_bury: snow sliding off a roof and burying a doorway (a long rumble, the whump, the settle)
- storm_wind_loop: the storm getting up round the lodge (a loop)

    python tools/WinterAudio/make_lodge_audio.py
"""
import os
import numpy as np
import make_winter_audio as W

rng = np.random.default_rng(2323)
SR = W.SR
HERE = os.path.dirname(os.path.abspath(__file__))


def src(name):
    return W.load(os.path.join(HERE, "src", name + ".wav"))


def muffle(x):
    """Through a closed wooden door: the top end gone, a boxy low resonance."""
    x = W.fft_filter(x, lo=90, hi=900)
    t = np.arange(len(x)) / SR
    return x * (0.9 + 0.1 * np.sin(2 * np.pi * 3 * t))


def gen_room():
    for i, n in enumerate(["room_too_cold", "room_starving", "room_always_hungry"]):
        x = W.mimic(src(n), 0.84 if "starving" in n else 0.9, 300 + i)
        W.save(f"wendigo_room_{i + 1:02}.wav", muffle(x))


def gen_mumble():
    for i, n in enumerate(["mumble_freezing", "mumble_too_cold", "mumble_hungry", "mumble_starved"]):
        x = W.trim(src(n))
        x = W.resample(x, 0.72)                      # low and slurred
        x = np.concatenate([x, W.resample(x[len(x) // 3:], 0.85) * 0.6])   # the word dragged again, fading
        x = x + W.fft_filter(rng.standard_normal(len(x)).astype(np.float32), lo=800, hi=4000) * W.env(x, 2048) * 0.12
        W.save(f"wendigo_mumble_{i + 1:02}.wav", W.reverb(W.fft_filter(x, lo=120, hi=2200), 0.45))


def laugh(x, pitch, seed, verb=0.4):
    r = np.random.default_rng(seed)
    x = W.trim(x)
    x = W.resample(x, pitch)
    # the laugh stutters: every syllable doubled a little late, like an echo that shouldn't be there
    d = int(SR * r.uniform(0.07, 0.11))
    y = np.concatenate([x, np.zeros(d)]) + np.concatenate([np.zeros(d), x]) * 0.45
    y = y + W.fft_filter(r.standard_normal(len(y)).astype(np.float32), lo=600, hi=3500) * W.env(y, 2048) * 0.15
    t = np.arange(len(y)) / SR
    y = y * (0.82 + 0.18 * np.sin(2 * np.pi * 27 * t))
    return W.reverb(W.fft_filter(y, lo=80, hi=3600), verb)


def gen_laughs():
    W.save("wendigo_laugh_01.wav", laugh(src("laugh_long"), 0.7, 400, 0.5))
    W.save("wendigo_laugh_02.wav", laugh(src("laugh_short"), 0.72, 401, 0.6))
    W.save("wendigo_laugh_03.wav", laugh(src("laugh_big"), 0.66, 402, 0.3))


def tone(f, dur, amp=0.5):
    t = np.arange(int(SR * dur)) / SR
    return np.sin(2 * np.pi * f * t) * amp * np.minimum(1, np.minimum(t / 0.004, (dur - t) / 0.01))


def gen_reader():
    W.save("card_beep.wav", np.concatenate([tone(1760, 0.07), np.zeros(int(SR * 0.04)), tone(2350, 0.09)]))
    W.save("card_error.wav", np.concatenate([tone(440, 0.14), np.zeros(int(SR * 0.06)), tone(330, 0.22)]))
    n = int(SR * 0.6)
    t = np.arange(n) / SR
    zap = rng.standard_normal(n) * (rng.random(n) < 0.3) * np.exp(-t / 0.12)
    zap = W.fft_filter(zap.astype(np.float32), lo=1500) + np.sin(2 * np.pi * 120 * t) * np.exp(-t / 0.2) * 0.3
    W.save("reader_zap.wav", zap)


def gen_mop():
    n = int(SR * 1.6)
    t = np.arange(n) / SR
    x = np.zeros(n, np.float32)
    for at, g in ((0.0, 0.25), (0.42, 1.0), (0.55, 0.6), (0.66, 0.45), (0.74, 0.3), (0.8, 0.2)):
        i = int(at * SR)
        L = int(SR * 0.12)
        hit = rng.standard_normal(L) * np.exp(-np.arange(L) / (SR * 0.02))
        body = np.sin(2 * np.pi * rng.uniform(180, 260) * np.arange(L) / SR) * np.exp(-np.arange(L) / (SR * 0.05))
        x[i:i + L] += (W.fft_filter(hit.astype(np.float32), lo=300, hi=5000) * 0.7 + body * 0.6) * g
    # the swish of it going over
    x += W.fft_filter(rng.standard_normal(n).astype(np.float32), lo=400, hi=2500) * np.exp(-((t - 0.25) / 0.12) ** 2) * 0.2
    W.save("mop_clatter.wav", W.reverb(x, 0.3))


def gen_roaches():
    n = int(SR * 2.6)
    t = np.arange(n) / SR
    clicks = (rng.random(n) < 0.012 * np.clip(1.2 - t / 2.2, 0, 1)).astype(np.float32)
    x = W.fft_filter(clicks, lo=2500, hi=9000) * 2.5
    x += W.fft_filter(rng.standard_normal(n).astype(np.float32), lo=3000, hi=8000) * 0.05 * np.clip(1 - t / 2.4, 0, 1)
    W.save("roach_skitter.wav", x)


def gen_table():
    n = int(SR * 2.8)
    t = np.arange(n) / SR
    x = np.zeros(n, np.float32)
    # the splintering, then the slam and the long wooden boom, then legs clattering down
    for c in range(30):
        at = int(SR * rng.uniform(0.0, 0.35) ** 1.3)
        L = int(SR * rng.uniform(0.005, 0.03))
        x[at:at + L] += rng.standard_normal(L) * np.exp(-np.arange(L) / (L / 3)) * rng.uniform(0.3, 0.8)
    x = W.fft_filter(x, lo=600)
    slam = W.fft_filter(rng.standard_normal(n).astype(np.float32), hi=300) * np.clip((t - 0.42) / 0.005, 0, 1) * np.exp(-np.maximum(t - 0.42, 0) / 0.25) * 3
    boom = np.sin(2 * np.pi * 58 * t) * np.exp(-np.maximum(t - 0.42, 0) / 0.4) * (t > 0.42) * 0.9
    legs = np.zeros(n, np.float32)
    for at in (0.8, 1.05, 1.2, 1.5):
        i = int(at * SR)
        L = int(SR * 0.1)
        legs[i:i + L] += np.sin(2 * np.pi * rng.uniform(140, 220) * np.arange(L) / SR) * np.exp(-np.arange(L) / (SR * 0.03)) * 0.5
    W.save("table_collapse.wav", W.reverb(x + slam + boom + legs, 0.35))


def gen_bury():
    n = int(SR * 4.5)
    t = np.arange(n) / SR
    rumble = W.fft_filter(rng.standard_normal(n).astype(np.float32), hi=160) * np.clip(t / 0.8, 0, 1) * np.exp(-np.maximum(t - 1.6, 0) / 0.6) * 2
    hiss = W.fft_filter(rng.standard_normal(n).astype(np.float32), lo=1200, hi=6000) * np.clip(t / 0.5, 0, 1) * np.exp(-np.maximum(t - 1.2, 0) / 0.5) * 0.3
    whump = W.fft_filter(rng.standard_normal(n).astype(np.float32), hi=220) * np.clip((t - 1.65) / 0.01, 0, 1) * np.exp(-np.maximum(t - 1.65, 0) / 0.2) * 3
    settle = W.fft_filter(rng.standard_normal(n).astype(np.float32), lo=1500, hi=7000) * np.exp(-((t - 2.4) / 0.6) ** 2) * 0.15
    W.save("snow_bury.wav", rumble + hiss + whump + settle)


def gen_storm():
    n = int(SR * 12)
    t = np.arange(n) / SR
    base = W.fft_filter(rng.standard_normal(n).astype(np.float32), lo=150, hi=1400)
    gust = 0.55 + 0.45 * np.sin(2 * np.pi * t / 12 * 3) * np.sin(2 * np.pi * t / 12 * 2 + 1)
    howl = np.sin(2 * np.pi * (330 + 60 * np.sin(2 * np.pi * t / 12 * 4)) * t) * 0.08 * np.clip(gust - 0.6, 0, 1) * 3
    x = base * gust + howl
    # a seamless loop: crossfade the end into the start
    f = int(SR * 1.0)
    x[:f] = x[:f] * np.linspace(0, 1, f) + x[-f:] * np.linspace(1, 0, f)
    W.save("storm_wind_loop.wav", x[:-f])


if __name__ == "__main__":
    W.OUT = os.path.join(HERE, "..", "..", "assets", "audio", "sfx")
    gen_room(); gen_mumble(); gen_laughs(); gen_reader(); gen_mop(); gen_roaches(); gen_table(); gen_bury()
    W.OUT = os.path.join(HERE, "..", "..", "assets", "audio", "ambient")
    gen_storm()
    print("done")
