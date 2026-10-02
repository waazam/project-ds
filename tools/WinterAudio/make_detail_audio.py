"""The detail pass's sounds (2026-10-01), made offline with numpy, in the same way as make_winter_audio.py.

- step_snow_deep_XX:  a step into deep snow off the plowed road: muffled, a low compressed crunch and the drag
                      of the leg pulled through (no bright grains)
- wind_wall_loop:     wind whistling over the plowed walls' tops (a seamless loop: a breathy hiss with a thin,
                      wandering whistle in it, swelling and falling)
- clock_longcase_loop: a grandfather clock's slow tick-tock (one beat a second, a wooden case's knock in it)
- glass_clink_XX:     glasses and bottles touching (the bar's shelves, a bottle picked up)
- ceramic_set_XX:     a mug or cup set down / picked up (a dull ceramic tap)
- book_handle_XX:     a hardback picked up and its pages riffled

    python tools/WinterAudio/make_detail_audio.py
"""
import numpy as np
import make_winter_audio as W

rng = np.random.default_rng(1001)
SR = W.SR


def noise(n):
    return rng.standard_normal(n).astype(np.float32)


def gen_deep_steps():
    for v in range(6):
        n = int(SR * 0.7)
        t = np.arange(n) / SR
        # the press: a low, packed crunch (no top end: the snow round the leg swallows it)
        press = W.fft_filter(noise(n), lo=120, hi=900) * np.exp(-((t - 0.09) / 0.07) ** 2)
        thud = W.fft_filter(noise(n), hi=140) * np.exp(-((t - 0.07) / 0.035) ** 2) * 1.4
        # the leg dragged up through it: a long soft hiss, rising and falling
        drag = W.fft_filter(noise(n), lo=300, hi=1600) * np.exp(-((t - 0.36 - v * 0.015) / 0.13) ** 2) * 0.35
        # a few packed squeaks, low
        sq = np.zeros(n, np.float32)
        for _ in range(rng.integers(2, 5)):
            a = int(SR * rng.uniform(0.05, 0.16)); L = int(SR * 0.012)
            f0 = rng.uniform(500, 800)
            sq[a:a + L] += np.sin(2 * np.pi * f0 * np.arange(L) / SR) * np.hanning(L) * 0.25
        W.save(f"step_snow_deep_{v + 1:02}.wav", press + thud + drag + sq)


def gen_wind_loop():
    L = int(SR * 12.0)
    t = np.arange(L) / SR
    # (periodic over the loop: every modulation a whole number of cycles in 12 s, and the noise filtered circularly)
    def circ(lo, hi, q=None):
        f = np.fft.rfft(noise(L)); fr = np.fft.rfftfreq(L, 1 / SR)
        g = 1 / np.sqrt(1 + (fr / hi) ** 4) * (1 / np.sqrt(1 + (lo / np.maximum(fr, 1)) ** 4))
        return np.fft.irfft(f * g, L)
    hiss = circ(250, 2400)
    swell = 0.55 + 0.25 * np.sin(2 * np.pi * t / 12 * 1) + 0.15 * np.sin(2 * np.pi * t / 12 * 3 + 1.2) + 0.05 * np.sin(2 * np.pi * t / 12 * 7)
    # the whistle: a narrow band of noise round a pitch that wanders (over the walls' edge), loud only in the swells
    pitch = 900 + 160 * np.sin(2 * np.pi * t / 12 * 2 + 0.4) + 60 * np.sin(2 * np.pi * t / 12 * 5)
    phase = 2 * np.pi * np.cumsum(pitch) / SR
    phase *= (2 * np.pi * round(phase[-1] / (2 * np.pi))) / phase[-1]   # (a whole number of turns: no click at the loop)
    band = circ(30, 120)
    band /= np.max(np.abs(band)) + 1e-9
    whistle = np.sin(phase) * (0.6 + 0.4 * band) * np.clip(swell - 0.45, 0, 1) ** 1.5 * 0.5
    low = circ(20, 160) * 0.8
    x = hiss * swell + whistle * np.max(np.abs(hiss)) + low * swell
    W.save("wind_wall_loop.wav", x)


def gen_longcase():
    L = SR * 4
    x = np.zeros(L, np.float32)
    for k in range(4):
        a = k * SR + int(SR * 0.02)
        n = int(SR * 0.25)
        tt = np.arange(n) / SR
        # the escapement's click (a little brighter on the tick, duller on the tock) and the case's wooden knock
        f = 2600 if k % 2 == 0 else 2100
        click = np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.006) + W.fft_filter(noise(n), lo=1500, hi=6000) * np.exp(-tt / 0.004) * 0.6
        knock = np.sin(2 * np.pi * (190 if k % 2 == 0 else 170) * tt) * np.exp(-tt / 0.05) * 0.5
        x[a:a + n] += (click + knock).astype(np.float32)
    # a faint room round it (its tail wrapped round to the loop's start, so it loops seamlessly)
    x = W.reverb(x, 0.2)
    tail = x[L:]
    x = x[:L].copy(); x[:len(tail)] += tail[:L]
    W.save("clock_longcase_loop.wav", x)


def ring(n, freqs, decay):
    tt = np.arange(n) / SR
    out = np.zeros(n)
    for f, a, d in zip(freqs, [1, 0.6, 0.4, 0.25], decay):
        out += a * np.sin(2 * np.pi * f * tt + rng.uniform(0, 6)) * np.exp(-tt / d)
    return out


def gen_glass():
    for v in range(5):
        n = int(SR * 1.2)
        base = rng.uniform(2200, 3600)
        x = ring(n, [base, base * 2.32, base * 3.9, base * 5.1], [0.35, 0.18, 0.09, 0.05])
        tt = np.arange(n) / SR
        x += W.fft_filter(noise(n), lo=3000) * np.exp(-tt / 0.002) * 0.6
        if v % 2 == 1:   # a second touch, softer (it rocks against the next)
            d = int(SR * rng.uniform(0.09, 0.16))
            x[d:] += ring(n - d, [base * 1.07, base * 2.5, base * 4.1, base * 5.5], [0.25, 0.12, 0.06, 0.04]) * 0.45
        W.save(f"glass_clink_{v + 1:02}.wav", W.reverb(x, 0.18))


def gen_ceramic():
    for v in range(3):
        n = int(SR * 0.6)
        tt = np.arange(n) / SR
        base = rng.uniform(900, 1300)
        x = ring(n, [base, base * 2.7, base * 4.4, base * 6.0], [0.05, 0.03, 0.02, 0.01])
        x += W.fft_filter(noise(n), lo=200, hi=2500) * np.exp(-tt / 0.008) * 0.8
        x += W.fft_filter(noise(n), hi=200) * np.exp(-tt / 0.02) * 0.6
        W.save(f"ceramic_set_{v + 1:02}.wav", W.reverb(x, 0.15))


def gen_book():
    for v in range(3):
        n = int(SR * 1.1)
        tt = np.arange(n) / SR
        # lifted: the board's soft knock; then the pages riffled under a thumb (a run of tiny flaps)
        x = W.fft_filter(noise(n), lo=80, hi=700) * np.exp(-((tt - 0.03) / 0.02) ** 2) * 0.9
        start = 0.25 + v * 0.03
        for k in range(rng.integers(14, 22)):
            a = int(SR * (start + k * rng.uniform(0.018, 0.03)))
            L = int(SR * 0.02)
            if a + L >= n: break
            x[a:a + L] += W.fft_filter(noise(L), lo=1200, hi=7000) * np.hanning(L) * (0.5 - k * 0.015)
        W.save(f"book_handle_{v + 1:02}.wav", x)


if __name__ == "__main__":
    gen_deep_steps(); gen_wind_loop(); gen_longcase(); gen_glass(); gen_ceramic(); gen_book()
    print("done")
