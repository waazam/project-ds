"""Bakes the high-res surface sets that replaced the small procedural ones (2026-10-09, the owner: "The wood in the cabin
and the ski lodge looks pretty bad ... the texture for all the concrete needs to be drastically upscaled to look more
real"). Seamless, 1024 px (the log 1024 x 256: one course), each an albedo, a normal map and a roughness map:

  wood_log       the cabin's round logs, one course: bark-stripped, weathered grey-brown, checks along the grain,
                 the rounded shading, pale chinking top and bottom
  wood_boards    vertical boards, grey and weathered, a hand wide, dark seams, knots, nail holes, sun-bleached
  wood_floor     floor planks, worn paler down the middle, staggered butt joints, dark seams
  wood_dark      the lodge's stained dark panelling: close straight grain, figure, a varnish worn at the edges
  wood_pine      the lodge's split-log walls: golden-brown pine, wide grain, knots, rounded courses
  concrete_a     cast concrete: fine pores and pits, aggregate, a soft mottle, hairline cracks
  concrete_b     the same, older: stained, efflorescence, water tide-marks, more cracks
  concrete_clean poured smooth: board-form marks, faint

    python tools/textures/surfaces.py [names...]
"""
import os
import sys
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "textures", "surfaces")


def spectral(shape, beta, lo=1.0, hi=None, seed=0, aniso=(1.0, 1.0)):
    """Periodic noise with a 1/f^beta spectrum (stretched by aniso: grain), normalised 0..1."""
    h, w = shape
    r = np.random.default_rng(seed)
    F = np.fft.fft2(r.standard_normal((h, w)))
    # (aniso stretches the features: (8, 1) makes them 8x longer along x)
    fy = np.fft.fftfreq(h)[:, None] * h * aniso[1]
    fx = np.fft.fftfreq(w)[None, :] * w * aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    amp = 1.0 / f ** beta
    amp[f < lo] = 0.0
    if hi:
        amp *= np.exp(-(f / hi) ** 2)
    n = np.real(np.fft.ifft2(F * amp))
    n -= n.min()
    return n / max(n.max(), 1e-9)


def normal_from(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save(name, albedo, height, rough, nstrength):
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((np.clip(albedo, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8)).save(os.path.join(OUT, f"{name}_albedo.png"))
    Image.fromarray(normal_from(height, nstrength)).save(os.path.join(OUT, f"{name}_normal.png"))
    Image.fromarray((np.clip(rough, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, f"{name}_rough.png"))
    print("baked", name, albedo.shape[:2])


def lerp(a, b, t):
    a = np.asarray(a, np.float64); b = np.asarray(b, np.float64)
    return a + (b - a) * np.asarray(t)[..., None]


def knots(shape, count, seed, size=(8, 22)):
    """Knots: dark ellipses with a ring round them, and the grain's swirl (as a field 0..1 and a warp)."""
    h, w = shape
    r = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    k = np.zeros(shape); warp = np.zeros(shape)
    for _ in range(count):
        cx, cy = r.random() * w, r.random() * h
        rx = r.uniform(*size); ry = rx * r.uniform(0.5, 0.8)
        dx = np.abs(xx - cx); dx = np.minimum(dx, w - dx)
        dy = np.abs(yy - cy); dy = np.minimum(dy, h - dy)
        d = np.sqrt((dx / rx) ** 2 + (dy / ry) ** 2)
        k = np.maximum(k, np.clip(1.2 - d, 0, 1))
        warp += np.exp(-d * d * 0.35) * np.sign(xx - cx + 1e-3) * 4.0
    return k, warp


def grain_wood(shape, seed, along_x=True, rings=48.0, warp_amt=18.0):
    """Wood grain: long rings (sine of a warped coordinate across the grain), fibres along it."""
    h, w = shape
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    big = spectral(shape, 2.0, lo=1, seed=seed, aniso=(8, 1) if along_x else (1, 8))
    fib = spectral(shape, 0.8, lo=6, hi=w / 2, seed=seed + 1, aniso=(16, 1) if along_x else (1, 16))
    across = (yy / h if along_x else xx / w)
    phase = across * rings + (big - 0.5) * warp_amt / 6.0
    ring = 0.5 + 0.5 * np.sin(phase * 2 * np.pi)
    ring = ring ** 4 * 0.7 + ring * 0.3
    return ring, fib, big


def wood_log():
    h, w = 256, 1024
    ring, fib, big = grain_wood((h, w), 11, True, rings=10.0)
    v = (np.mgrid[0:h, 0:w][0] + 0.5) / h
    checks = (spectral((h, w), 1.2, lo=4, seed=13, aniso=(30, 1)) > 0.83).astype(float)
    blotch = spectral((h, w), 2.2, lo=1, seed=14)
    base = lerp([0.16, 0.115, 0.08], [0.44, 0.34, 0.24], np.clip(0.35 + fib * 0.45 + ring * 0.15 + (blotch - 0.5) * 0.4, 0, 1))
    lum = base.mean(-1, keepdims=True)
    base = base + (np.concatenate([lum * 1.04, lum, lum * 0.95], -1) - base) * (0.28 + (1 - v)[..., None] * 0.2)   # greyed, most on top
    round_ = np.sin(np.pi * np.clip((v - 0.06) / 0.88, 0, 1))
    shade = 0.5 + 0.55 * round_ - 0.1 * v
    col = base * shade[..., None] * (1 - checks * 0.55)[..., None]
    chink = (v < 0.06) | (v > 0.94)
    mortar = lerp([0.3, 0.29, 0.26], [0.42, 0.4, 0.36], spectral((h, w), 1, lo=8, seed=15))
    col[chink] = mortar[chink] * 0.85
    height = round_ * 1.0 + fib * 0.08 - checks * 0.15 - chink * 0.3
    rough = np.clip(0.78 + fib * 0.15 + checks * 0.05, 0, 1)
    save("wood_log", col, height, rough, 6.0)


def boards(name, dark, light, vertical=True, width_px=128, grey=0.35, seed=20):
    N = 1024
    shape = (N, N)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    pos = xx if vertical else yy
    along = yy if vertical else xx
    board = (pos // width_px).astype(int)
    local = (pos % width_px) / width_px
    ring, fib, big = grain_wood(shape, seed, along_x=not vertical, rings=30.0)
    kn, warp = knots(shape, 14, seed + 5)
    r = np.random.default_rng(seed + 7)
    tone = r.random(board.max() + 2)[board]
    t = np.clip(0.25 + fib * 0.45 + ring * 0.2 + (tone - 0.5) * 0.35, 0, 1)
    col = lerp(dark, light, t)
    lum = col.mean(-1, keepdims=True)
    col = col + (lum - col) * grey
    col = col * (1 - kn * 0.6)[..., None]
    seam = np.clip(1 - np.minimum(local, 1 - local) * width_px / 2.5, 0, 1)
    col = col * (1 - seam * 0.75)[..., None]
    # nail holes near the ends of each board, at intervals
    nails = np.zeros(shape)
    for b in range(N // width_px):
        for k in range(4):
            cy = (k + 0.5) * N / 4 + r.uniform(-20, 20)
            for side in (0.18, 0.82):
                cx = (b + side) * width_px
                if vertical:
                    d = np.hypot(xx - cx, yy - cy)
                else:
                    d = np.hypot(yy - cx, xx - cy)
                nails = np.maximum(nails, np.clip(1 - d / 2.5, 0, 1))
    col = col * (1 - nails * 0.7)[..., None]
    height = fib * 0.25 + ring * 0.1 - seam * 0.8 - kn * 0.1 - nails * 0.4
    rough = np.clip(0.72 + fib * 0.2 - kn * 0.1, 0, 1)
    save(name, col, height, rough, 5.0)


def wood_floor():
    N = 1024
    shape = (N, N)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    row = (yy // 128).astype(int)
    vy = (yy % 128) / 128
    ring, fib, big = grain_wood(shape, 31, along_x=True, rings=34.0)
    r = np.random.default_rng(33)
    tone = r.random(row.max() + 2)[row]
    col = lerp([0.11, 0.075, 0.05], [0.36, 0.25, 0.16], np.clip(0.25 + fib * 0.45 + ring * 0.2 + (tone - 0.5) * 0.4, 0, 1))
    wear = np.clip(1 - np.abs(vy - 0.5) * 2, 0, 1) * spectral(shape, 2, lo=1, seed=34)
    col = col + (np.array([0.33, 0.27, 0.2]) - col) * (wear * 0.35)[..., None]
    seam = np.clip(1 - np.minimum(vy, 1 - vy) * 128 / 2.5, 0, 1)
    joints = np.zeros(shape)
    for rr in range(N // 128):
        for j in range(2):
            jx = ((rr * 389 + j * 512 + 97) % N)
            d = np.minimum(np.abs(xx - jx), N - np.abs(xx - jx))
            joints = np.maximum(joints, (np.clip(1 - d / 1.8, 0, 1)) * (row == rr))
    col = col * (1 - np.maximum(seam, joints) * 0.75)[..., None]
    height = fib * 0.2 - seam * 0.8 - joints * 0.8
    rough = np.clip(0.6 + fib * 0.2 - wear * 0.25, 0, 1)
    save("wood_floor", col, height, rough, 5.0)


def wood_dark():
    N = 1024
    shape = (N, N)
    ring, fib, big = grain_wood(shape, 41, along_x=False, rings=70.0, warp_amt=30.0)
    figure = spectral(shape, 1.4, lo=2, seed=42, aniso=(1, 6))
    col = lerp([0.06, 0.03, 0.02], [0.26, 0.13, 0.07], np.clip(0.3 + ring * 0.35 + fib * 0.25 + (figure - 0.5) * 0.4, 0, 1))
    worn = spectral(shape, 2.2, lo=1, seed=43)
    col = col + (np.array([0.3, 0.18, 0.11]) - col) * (np.clip(worn - 0.7, 0, 1) * 0.8)[..., None]
    height = ring * 0.06 + fib * 0.1
    rough = np.clip(0.38 + fib * 0.1 + np.clip(worn - 0.7, 0, 1) * 0.6, 0, 1)
    save("wood_dark", col, height, rough, 3.0)


def wood_pine():
    h, w = 1024, 1024
    ring, fib, big = grain_wood((h, w), 51, True, rings=12.0, warp_amt=40.0)
    kn, _ = knots((h, w), 10, 52, size=(10, 26))
    v = (np.mgrid[0:h, 0:w][0] + 0.5) / h
    course = (v * 4) % 1.0
    col = lerp([0.2, 0.13, 0.075], [0.5, 0.37, 0.24], np.clip(0.3 + fib * 0.35 + ring * 0.3, 0, 1))
    col = col * (1 - kn * 0.65)[..., None]
    round_ = np.sin(np.pi * np.clip((course - 0.05) / 0.9, 0, 1))
    col = col * (0.55 + 0.5 * round_)[..., None]
    chink = (course < 0.05) | (course > 0.95)
    col[chink] *= 0.3
    height = round_ + fib * 0.08 - chink * 0.3
    rough = np.clip(0.66 + fib * 0.15, 0, 1)
    save("wood_pine", col, height, rough, 6.0)


def concrete(name, seed, age):
    N = 1024
    shape = (N, N)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    mott = spectral(shape, 1.8, lo=1, seed=seed)
    fine = spectral(shape, 0.7, lo=40, hi=480, seed=seed + 1)
    agg = spectral(shape, 0.3, lo=120, hi=500, seed=seed + 2)
    r = np.random.default_rng(seed + 3)
    # pores and pits: small dark holes
    pits = np.zeros(shape)
    for _ in range(2600):
        cx, cy, rad = r.random() * N, r.random() * N, r.uniform(0.8, 3.2)
        x0, x1, y0, y1 = int(cx - 4), int(cx + 5), int(cy - 4), int(cy + 5)
        ys = np.arange(y0, y1) % N; xs = np.arange(x0, x1) % N
        sub = np.hypot(np.arange(x0, x1)[None, :] - cx, np.arange(y0, y1)[:, None] - cy)
        pits[np.ix_(ys, xs)] = np.maximum(pits[np.ix_(ys, xs)], np.clip(1 - sub / rad, 0, 1))
    # hairline cracks: thin wandering ridges of a stretched noise
    crk = spectral(shape, 1.6, lo=2, seed=seed + 4)
    cracks = np.exp(-((crk - 0.5) / 0.006) ** 2) * (spectral(shape, 1.5, lo=1, seed=seed + 5) > 0.55 - age * 0.15)
    base = np.array([0.45, 0.44, 0.42])
    col = base * (0.78 + 0.3 * mott + 0.12 * (fine - 0.5) + 0.08 * (agg - 0.5))[..., None]
    col = lerp(col, [0.32, 0.31, 0.29], np.clip(mott - 0.62, 0, 1) * 1.6)   # darker blotches
    if age > 0:
        # water: tide-marks, streaks down, a pale bloom of salts
        streak = spectral(shape, 1.4, lo=2, seed=seed + 6, aniso=(1, 14))
        col = lerp(col, [0.27, 0.26, 0.23], np.clip(streak - 0.6, 0, 1) * 2.0 * age)
        eff = np.clip(spectral(shape, 2, lo=2, seed=seed + 7) - 0.72, 0, 1) * 3.0
        col = lerp(col, [0.62, 0.61, 0.58], eff * 0.5 * age)
        tide = np.exp(-((((yy / N) * 3 + (mott - 0.5) * 0.3) % 1.0 - 0.5) / 0.02) ** 2) * 0.35 * age
        col = lerp(col, [0.3, 0.29, 0.26], tide)
    col = col * (1 - pits * 0.6)[..., None] * (1 - cracks * 0.55)[..., None]
    height = fine * 0.3 + agg * 0.2 + mott * 0.2 - pits * 1.0 - cracks * 0.6
    rough = np.clip(0.82 + fine * 0.12 - (age * 0.1), 0, 1)
    save(name, col, height, rough, 7.0)


def concrete_clean():
    N = 1024
    shape = (N, N)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    mott = spectral(shape, 1.9, lo=1, seed=71)
    fine = spectral(shape, 0.7, lo=40, hi=480, seed=72)
    # board-form marks: horizontal planks' imprint, faint, a seam every 128 px
    plank = (yy // 128).astype(int)
    r = np.random.default_rng(73)
    ptone = r.random(plank.max() + 2)[plank]
    fgrain = spectral(shape, 1.0, lo=4, seed=74, aniso=(20, 1))
    seam = np.exp(-(((yy % 128) - 0) / 1.4) ** 2) + np.exp(-(((yy % 128) - 128) / 1.4) ** 2)
    col = np.array([0.5, 0.49, 0.47]) * (0.82 + 0.22 * mott + 0.06 * (fine - 0.5) + 0.05 * (ptone - 0.5) + 0.05 * (fgrain - 0.5))[..., None]
    col = col * (1 - seam * 0.35)[..., None]
    height = fine * 0.2 + fgrain * 0.25 + mott * 0.1 - seam * 0.5
    rough = np.clip(0.75 + fine * 0.1, 0, 1)
    save("concrete_clean", col, height, rough, 5.0)


def concrete_grime():
    """Grimy concrete: dark, blotched, oil soaked in, a web of cracks, a blue-black oily sheen here and there."""
    N = 1024
    shape = (N, N)
    mott = spectral(shape, 1.8, lo=1, seed=91)
    fine = spectral(shape, 0.7, lo=40, hi=480, seed=92)
    oil = spectral(shape, 2.0, lo=1, seed=93)
    sheen = spectral(shape, 1.8, lo=2, seed=94)
    crk = spectral(shape, 1.6, lo=2, seed=95)
    cracks = np.exp(-((crk - 0.5) / 0.007) ** 2) * (spectral(shape, 1.5, lo=1, seed=96) > 0.4)
    g = 0.2 + 0.28 * mott + 0.08 * (fine - 0.5)
    col = np.stack([g, g * 0.97, g * 0.92], -1)
    col = lerp(col, [0.05, 0.05, 0.065], np.clip((oil - 0.58) * 3.5, 0, 0.85))
    col = lerp(col, [0.11, 0.14, 0.26], np.clip((sheen - 0.72) * 2.5, 0, 0.6))
    col = col * (1 - cracks * 0.7)[..., None]
    height = fine * 0.3 + mott * 0.2 - cracks * 0.7
    rough = np.clip(0.75 - np.clip((oil - 0.58) * 2.0, 0, 0.5) + fine * 0.1, 0.15, 1)
    save("concrete_grime", col, height, rough, 6.0)


JOBS = {
    "concrete_grime": concrete_grime,
    "wood_log": wood_log,
    "wood_boards": lambda: boards("wood_boards", [0.13, 0.1, 0.075], [0.38, 0.32, 0.25], True, 128, 0.4, 20),
    "wood_floor": wood_floor,
    "wood_dark": wood_dark,
    "wood_pine": wood_pine,
    "concrete_a": lambda: concrete("concrete_a", 61, 0.4),
    "concrete_b": lambda: concrete("concrete_b", 81, 1.0),
    "concrete_clean": concrete_clean,
}

if __name__ == "__main__":
    names = sys.argv[1:] or list(JOBS)
    for n in names:
        JOBS[n]()
