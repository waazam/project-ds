"""Act 23's second half (room 201, the crawlspace, the frozen lodge, the front doors), made offline with numpy in
the same way as make_winter_audio.py and make_lodge_audio.py.

- wendigo_howl_01..04: its howl, four of them, each worse: a throat far too big pushing a note up and over, a man's
  voice somewhere in it, a rasp under it; 04 the longest and harshest (the fourth arm)
- wendigo_howl_far_01..03: the same far off in the walls (muffled, echoing)
- wendigo_forever: "Starving..... Freezing..... Forever...." in its copied voice (src/forever_*.wav, the Windows
  voice), dragged slow, a second throat an octave under it
- wall_burst_01..03: boards smashed through from behind (a thud, splinters, laths clattering)
- claw_scrape_01..02: long nails dragged across wood as it pulls back
- dust_trickle_01..03: grit and flakes pattering down inside a wall
- light_switch_01..02: an old toggle switch
- wardrobe_fall: a wardrobe going over onto its face (a lean, a creak, the slam, the rattle)
- door_strain_01..02: a heavy door pushed against snow (wood groaning, snow grinding)
- door_explode: the front doors blown out (a boom, splintering, debris, the wind in behind)
- body_dive: throwing yourself onto a wooden floor

    python tools/WinterAudio/make_crawl_audio.py
"""
import os
import numpy as np
import make_winter_audio as W

SR = W.SR
HERE = os.path.dirname(os.path.abspath(__file__))
rng = np.random.default_rng(2324)


def t_(sec):
    return np.arange(int(SR * sec)) / SR


def noise(n, lo=None, hi=None, r=None):
    r = r or rng
    return W.fft_filter(r.standard_normal(n).astype(np.float32), lo=lo, hi=hi)


def decay(n, tau):
    return np.exp(-np.arange(n) / (SR * tau))


def crack(n_total, at, strength, r, lo=900, hi=7000):
    """A splintering crack: a sharp broadband spike with a short ringing body."""
    out = np.zeros(n_total, np.float32)
    n = int(SR * r.uniform(0.04, 0.12))
    if at + n > n_total: return out
    x = noise(n, lo=lo, hi=hi, r=r) * decay(n, r.uniform(0.012, 0.035))
    ring = np.sin(2 * np.pi * r.uniform(300, 900) * np.arange(n) / SR) * decay(n, 0.02) * 0.4
    out[at:at + n] += (x + ring) * strength
    return out


def thud(sec, f0, tau, r):
    t = t_(sec)
    f = f0 * (1 + 0.6 * np.exp(-t / 0.03))
    ph = 2 * np.pi * np.cumsum(f) / SR
    return (np.sin(ph) * np.exp(-t / tau) + noise(len(t), hi=400, r=r) * np.exp(-t / (tau * 0.5)) * 0.5).astype(np.float32)


def formant(x, freqs, bw=120):
    """Shape a buzz into a vowel: sum of band-passes at the formants."""
    out = np.zeros_like(x)
    for f, g in freqs:
        out += W.fft_filter(x, lo=f - bw, hi=f + bw) * g
    return out


def howl(sec, f_lo, f_hi, harsh, seed):
    r = np.random.default_rng(seed)
    t = t_(sec)
    u = t / sec
    # the note: pushed up out of a groan, held, and dragged back down (with a wide, slow wobble)
    shape = np.where(u < 0.25, u / 0.25, np.where(u < 0.7, 1.0, 1 - (u - 0.7) / 0.3 * 0.85))
    f = f_lo + (f_hi - f_lo) * shape
    f = f * (1 + 0.035 * np.sin(2 * np.pi * 5.2 * t) + 0.02 * np.sin(2 * np.pi * 1.3 * t + 1))
    ph = 2 * np.pi * np.cumsum(f) / SR
    buzz = np.zeros_like(t)
    for k in range(1, 14):
        buzz += np.sin(ph * k) / k ** (1.1 - 0.25 * harsh)
    # a throat's vowel: "oh" into "aah"
    vow = formant(buzz, [(420, 1.0), (820, 0.7), (2400, 0.25 + 0.3 * harsh)], 140)
    # an octave under it, rougher: something far bigger than a man
    sub = np.sin(ph * 0.5) * 0.5 + np.sin(ph * 0.5 * 2.01) * 0.25
    x = vow + sub * (0.4 + 0.3 * harsh)
    # the rasp: breath torn through it
    x = x + noise(len(t), lo=600, hi=5000, r=r) * (0.25 + 0.35 * harsh) * np.abs(np.sin(np.pi * u))
    # its human voice copied into it: a faint, wrong "help" smeared along under the note
    v = W.trim(W.load(os.path.join(HERE, "src", "help_me.wav")))
    v = W.resample(v, 0.62)
    if len(v) < len(x): v = np.concatenate([v, np.zeros(len(x) - len(v))])
    x = x + v[:len(x)] * 0.35
    env = np.minimum(1, t / 0.12) * np.minimum(1, (sec - t) / 0.5)
    x = x * env
    if harsh > 0.5: x = np.tanh(x * (1 + 2.5 * harsh)) / np.tanh(1 + 2.5 * harsh)
    return x.astype(np.float32)


def gen_howls():
    specs = [(2.4, 150, 330, 0.1), (2.8, 140, 360, 0.35), (3.3, 130, 400, 0.65), (4.0, 120, 440, 0.95)]
    for i, (sec, lo, hi, harsh) in enumerate(specs):
        x = howl(sec, lo, hi, harsh, 500 + i)
        W.save(f"wendigo_howl_{i + 1:02}.wav", W.reverb(x, 0.28))
    for i in range(3):
        x = howl(2.6 + i * 0.4, 135 + i * 10, 340 + i * 30, 0.3, 520 + i)
        x = W.fft_filter(x, lo=90, hi=1100)             # through the walls
        x = W.reverb(W.reverb(x, 0.5), 0.35)             # the cavity carrying it round
        W.save(f"wendigo_howl_far_{i + 1:02}.wav", x * 0.7)


def gen_forever():
    parts = []
    for i, n in enumerate(["forever_starving", "forever_freezing", "forever_forever"]):
        x = W.trim(W.load(os.path.join(HERE, "src", n + ".wav")))
        x = W.resample(x, 0.78)
        cut = int(len(x) * 0.55)
        x = np.concatenate([x[:cut], W.resample(x[cut:], 0.62)])   # the end of each word dragged out
        under = W.resample(x, 0.5)[:len(x)] if len(W.resample(x, 0.5)) >= len(x) else np.pad(W.resample(x, 0.5), (0, len(x) - len(W.resample(x, 0.5))))
        y = x + under * 0.55
        y = y + noise(len(y), lo=900, hi=5000, r=np.random.default_rng(530 + i)) * W.env(y, 2048) * 0.14
        gap = [1.8, 1.9, 2.2][i]
        seg = np.zeros(int(SR * gap), np.float32)
        seg[:min(len(y), len(seg))] = y[:len(seg)]
        parts.append(seg)
    x = np.concatenate(parts)
    x = W.fft_filter(x, lo=70, hi=4200)
    W.save("wendigo_forever.wav", W.reverb(x, 0.4))


def gen_wall_burst():
    for i in range(3):
        r = np.random.default_rng(600 + i)
        n = int(SR * 1.3)
        x = np.zeros(n, np.float32)
        th = thud(0.5, 70 + 10 * i, 0.12, r)
        x[:len(th)] += th * 0.9
        for k in range(14):
            at = int(SR * (0.005 + abs(r.normal(0, 0.08))))
            x += crack(n, at, r.uniform(0.3, 0.9), r)
        # laths and splinters clattering down after
        for k in range(10):
            at = int(SR * r.uniform(0.25, 1.1))
            x += crack(n, at, r.uniform(0.05, 0.2), r, lo=1500, hi=8000)
        W.save(f"wall_burst_{i + 1:02}.wav", W.reverb(x, 0.15))


def gen_claw_scrape():
    for i in range(2):
        r = np.random.default_rng(620 + i)
        sec = 1.4
        t = t_(sec)
        x = np.zeros_like(t, dtype=np.float32)
        strokes = 3 + i
        for s in range(strokes):
            a, b = s / strokes * sec * 0.85, s / strokes * sec * 0.85 + 0.35
            m = (t >= a) & (t < b)
            u = (t[m] - a) / 0.35
            grit = noise(m.sum(), lo=1800, hi=6000, r=r) * (1 + 0.6 * np.sin(2 * np.pi * r.uniform(40, 90) * t[m]))
            squeal = np.sin(2 * np.pi * np.cumsum(np.full(m.sum(), r.uniform(900, 1400)) * (1 + 0.1 * u)) / SR) * 0.25
            x[m] += (grit + squeal) * np.sin(np.pi * u)
        W.save(f"claw_scrape_{i + 1:02}.wav", W.reverb(x, 0.2))


def gen_dust():
    for i in range(3):
        r = np.random.default_rng(640 + i)
        n = int(SR * 1.4)
        x = np.zeros(n, np.float32)
        for k in range(90):
            at = int(SR * r.uniform(0, 1.2) ** 1.6)
            g = int(SR * 0.003)
            if at + g < n: x[at:at + g] += noise(g, lo=2500, hi=9000, r=r) * r.uniform(0.1, 0.5)
        x += noise(n, lo=1500, hi=6000, r=r) * decay(n, 0.5) * 0.08
        W.save(f"dust_trickle_{i + 1:02}.wav", x)


def gen_switch():
    for i in range(2):
        r = np.random.default_rng(660 + i)
        n = int(SR * 0.25)
        x = np.zeros(n, np.float32)
        for at, s in ((0, 1.0), (int(SR * 0.012), 0.6)):
            g = int(SR * 0.02)
            x[at:at + g] += noise(g, lo=1500, hi=9000, r=r) * decay(g, 0.003) * s
            x[at:at + g] += np.sin(2 * np.pi * (2400 + 300 * i) * np.arange(g) / SR) * decay(g, 0.004) * 0.3 * s
        W.save(f"light_switch_{i + 1:02}.wav", x)


def gen_wardrobe():
    r = np.random.default_rng(680)
    n = int(SR * 2.4)
    x = np.zeros(n, np.float32)
    # the lean: a long creak
    t = t_(0.8)
    f = 180 + 120 * t / 0.8
    creak = np.sin(2 * np.pi * np.cumsum(f) / SR) * (0.5 + 0.5 * np.sin(2 * np.pi * 30 * t)) * 0.3
    creak = W.fft_filter(creak + noise(len(t), lo=300, hi=2000, r=r) * 0.2, lo=150, hi=2500)
    x[:len(t)] += creak * np.sin(np.pi * t / 0.8)
    # the slam
    at = int(SR * 0.8)
    th = thud(1.0, 55, 0.25, r)
    x[at:at + len(th)] += th * 1.2
    for k in range(12):
        x += crack(n, at + int(SR * abs(r.normal(0, 0.03))), r.uniform(0.3, 0.8), r, lo=500, hi=5000)
    # the rattle of what was in it, and the dust settling
    for k in range(16):
        x += crack(n, at + int(SR * r.uniform(0.1, 1.2)), r.uniform(0.04, 0.15), r, lo=1200, hi=7000)
    W.save("wardrobe_fall.wav", W.reverb(x, 0.3))


def gen_door_strain():
    for i in range(2):
        r = np.random.default_rng(700 + i)
        sec = 1.6
        t = t_(sec)
        f = 120 + 80 * np.sin(np.pi * t / sec) + 20 * r.standard_normal(len(t)).cumsum() / SR
        groan = np.sin(2 * np.pi * np.cumsum(f) / SR) * (0.6 + 0.4 * np.sin(2 * np.pi * 22 * t))
        groan = W.fft_filter(groan, lo=90, hi=1800)
        grind = noise(len(t), lo=200, hi=2500, r=r) * (0.5 + 0.5 * np.abs(np.sin(2 * np.pi * 3 * t)))
        x = (groan * 0.7 + grind * 0.5) * np.sin(np.pi * t / sec)
        W.save(f"door_strain_{i + 1:02}.wav", W.reverb(x, 0.3))


def gen_door_explode():
    r = np.random.default_rng(720)
    n = int(SR * 3.0)
    x = np.zeros(n, np.float32)
    th = thud(1.6, 40, 0.45, r)
    x[:len(th)] += th * 1.4
    for k in range(40):
        x += crack(n, int(SR * abs(r.normal(0, 0.06))), r.uniform(0.3, 1.0), r, lo=600, hi=8000)
    for k in range(40):
        x += crack(n, int(SR * r.uniform(0.2, 2.0)), r.uniform(0.05, 0.25), r, lo=1000, hi=8000)
    # the wind in behind it
    wind = noise(n, lo=150, hi=1500, r=r) * np.minimum(1, np.arange(n) / (SR * 0.4)) * decay(n, 1.6) * 0.5
    x += wind
    W.save("door_explode.wav", W.reverb(x, 0.35))


def gen_dive():
    r = np.random.default_rng(740)
    n = int(SR * 0.9)
    x = np.zeros(n, np.float32)
    t = t_(0.25)
    x[:len(t)] += noise(len(t), lo=300, hi=3000, r=r) * np.sin(np.pi * t / 0.25) * 0.4   # cloth rushing
    at = int(SR * 0.22)
    th = thud(0.5, 65, 0.09, r)
    x[at:at + len(th)] += th
    W.save("body_dive.wav", W.reverb(x, 0.2))


if __name__ == "__main__":
    gen_howls()
    gen_forever()
    gen_wall_burst()
    gen_claw_scrape()
    gen_dust()
    gen_switch()
    gen_wardrobe()
    gen_door_strain()
    gen_door_explode()
    gen_dive()
    print("ok")
