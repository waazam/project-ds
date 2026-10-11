"""The ski lodge's wallpapers (2026-10-10c, the owner: "I also think that wallpaper of the hotel needs a big tweak").

Drawn here, not taken from anywhere (no licence to carry for the Steam release): four old-hotel papers, each a 1 m tile
(the lodge's walls are UV'd in metres), 512 px, with a flocked relief for its normal map:

- damask:  the corridor and the dining hall. Tone-on-tone baroque medallions in a half-drop, deep olive on old gold.
- floral:  rooms 201 and 202. Trailing sprigs of roses and leaves on a dusty rose ground.
- trellis: room 203. A diamond trellis with a small fleur in each diamond, on powder blue.
- stripe:  room 204. Broad sage and cream stripes, a fine dark rule and a climbing vine between.

Every one is aged: yellowed toward the ceiling, a seam where the rolls meet every half metre, a faint water stain, and
it never goes bright (the PS2 look, and no glare round the lamps).

    python tools/textures/make_wallpapers.py
"""
import os
import numpy as np
from PIL import Image

N = 512
SS = 2
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "textures", "surfaces")
rng = np.random.default_rng(1408)


def grid(n=N * SS):
    t = (np.arange(n) + 0.5) / n
    return np.meshgrid(t, t)  # u across, v down (v = 0 at the top of the tile)


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def value_noise(u, v, f, seed):
    r = np.random.default_rng(seed)
    g = r.random((f + 1, f + 1))
    g[-1, :] = g[0, :]
    g[:, -1] = g[:, 0]
    x, y = u * f, v * f
    i, j = np.floor(x).astype(int) % f, np.floor(y).astype(int) % f
    fx, fy = x - np.floor(x), y - np.floor(y)
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b, c, d = g[j, i], g[j, i + 1], g[j + 1, i], g[j + 1, i + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(u, v, seed, base=4, oct=4):
    s, a, tot = 0, 0.5, 0
    for o in range(oct):
        s += a * value_noise(u, v, base * 2 ** o, seed + o)
        tot += a
        a *= 0.5
    return s / tot


def cell(u, v, cu, cv, halfdrop=True):
    """(x, y) in -0.5..0.5 within a repeat cu x cv of the tile, the columns dropped by half."""
    x = u * cu
    col = np.floor(x)
    y = v * cv + (0.5 * (col % 2) if halfdrop else 0)
    return x - col - 0.5, y - np.floor(y) - 0.5


def ring(d, r, w):
    return smooth(w, w * 0.4, np.abs(d - r))


def mirrored_noise(x, y, seed, f=6):
    """Noise mirrored about the cell's upright (x = 0): thresholded, it grows into symmetric foliage, as a damask's is."""
    r = np.random.default_rng(seed)
    g = r.random((64, 64))
    def vn(px, py):
        i, j = np.floor(px).astype(int), np.floor(py).astype(int)
        fx, fy = px - i, py - j
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        i, j = i % 63, j % 63
        return (g[j, i] * (1 - fx) + g[j, i + 1] * fx) * (1 - fy) + (g[j + 1, i] * (1 - fx) + g[j + 1, i + 1] * fx) * fy
    ax = np.abs(x)
    s, a, tot = 0, 0.5, 0
    for o in range(4):
        k = f * 2 ** o
        s += a * vn(ax * k + 3.1, (y + 0.5) * k + 5.7)
        tot += a
        a *= 0.5
    return s / tot


def damask_mask(x, y):
    ax = np.abs(x)
    t = np.clip((y + 0.47) / 0.94, 0, 1)
    # the ogee: an onion-domed lozenge, pointed above and below
    width = 0.42 * np.sin(np.pi * t) ** 0.85 * (1 + 0.3 * np.sin(np.pi * t * 2 + 0.9))
    width = np.maximum(width, 1e-3)
    e = ax - width
    inside = smooth(0.0, -0.015, e)
    outline = smooth(0.012, 0.004, np.abs(e + 0.03)) + smooth(0.008, 0.003, np.abs(e + 0.065)) * 0.8
    # its foliage: mirrored noise, a filigree, densest toward its middle, a clear margin inside the outline
    n = mirrored_noise(x, y, 77, 5)
    fol = smooth(0.44, 0.5, n) * smooth(-0.07, -0.11, e)
    # a spine up its middle, a bud at its top, a bloom low
    spine = smooth(0.016, 0.008, ax) * smooth(-0.3, -0.26, y) * smooth(0.3, 0.26, y)
    bud = smooth(0.06, 0.05, np.sqrt((ax * 1.4) ** 2 + (y + 0.3) ** 2))
    bloom_d = np.sqrt(ax ** 2 + (y - 0.12) ** 2)
    bloom = smooth(0.11, 0.1, bloom_d) * (0.4 + 0.6 * smooth(0.3, 0.6, np.cos(np.arctan2(y - 0.12, ax) * 8) * 0.5 + 0.5))
    m = np.clip(np.maximum.reduce([fol, outline * 0.9, spine, bud, bloom]) * smooth(0.0, -0.01, e + 0.0) + outline * (1 - inside), 0, 1)
    # between the medallions, a small mirrored sprig of the same foliage
    sx, sy = ax - 0.5, y
    sprig = smooth(0.52, 0.58, mirrored_noise(sx * 2, sy * 2, 91, 4)) * smooth(0.11, 0.07, np.sqrt(sx ** 2 + sy ** 2 * 0.5))
    return np.clip(m + sprig, 0, 1)


def floral_mask(u, v):
    x, y = cell(u, v, 4, 4)
    d = np.sqrt(x ** 2 + y ** 2)
    ang = np.arctan2(y, x)
    # a cabbage rose: a spiral of petals, darker at its heart
    spiral = np.cos(ang * 2.0 + d * 40.0)
    rose = smooth(0.2, 0.17, d * (1 + 0.12 * np.sin(ang * 6))) * (0.55 + 0.45 * smooth(-0.3, 0.6, spiral))
    heart = smooth(0.05, 0.02, d)
    # leaves and tendrils round it: mirrored noise in a ring, turned a little each cell so it isn't stiff
    lx, ly = x * np.cos(0.5) - y * np.sin(0.5), x * np.sin(0.5) + y * np.cos(0.5)
    n = mirrored_noise(lx, ly, 55, 6)
    leaves = smooth(0.54, 0.6, n) * smooth(0.16, 0.2, d) * smooth(0.48, 0.36, d)
    buds = smooth(0.035, 0.025, np.sqrt((np.abs(x) - 0.3) ** 2 + (y + 0.28) ** 2))
    return np.clip(rose - heart * 0.2, 0, 1) + buds * 0.8, np.clip(leaves, 0, 1)


def trellis_mask(u, v):
    k = 5.0
    a = (u + v) * k
    b = (u - v) * k
    lines = np.maximum(smooth(0.06, 0.03, np.abs(a - np.round(a))), smooth(0.06, 0.03, np.abs(b - np.round(b))))
    fa, fb = a - np.floor(a) - 0.5, b - np.floor(b) - 0.5
    # the fleur at each diamond's middle
    dx, dy = (fa - fb) * 0.5, (fa + fb) * 0.5
    d = np.sqrt(dx ** 2 + dy ** 2)
    ang = np.arctan2(dy, dx)
    fleur = smooth(0.11, 0.08, d / (0.55 + 0.45 * np.abs(np.cos(ang * 2)))) * smooth(0.0, 0.02, d)
    knot = smooth(0.1, 0.06, np.sqrt((np.abs(fa) - 0.5) ** 2 + (np.abs(fb) - 0.5) ** 2) * 3)
    return np.clip(lines + knot, 0, 1), fleur


def stripe_mask(u, v):
    s = (u * 4) % 1.0
    broad = smooth(0.0, 0.01, s) * smooth(0.52, 0.51, s)
    rule = smooth(0.012, 0.006, np.abs(s - 0.58)) + smooth(0.012, 0.006, np.abs(s - 0.94))
    # the vine climbing the cream between the rules
    vx = 0.76 + 0.06 * np.sin(v * np.pi * 2 * 5 + (np.floor(u * 4)) * 1.3)
    vine = smooth(0.012, 0.006, np.abs(s - vx))
    lp = (v * 10) % 1.0 - 0.5
    leaf = smooth(0.0, -0.05, ((s - vx - 0.035 * np.sign(lp)) / 0.03) ** 2 + (lp / 0.18) ** 2 - 1)
    return broad, np.clip(rule, 0, 1), np.clip(vine + leaf, 0, 1)


def age(col, u, v, seed):
    n = fbm(u, v, seed, 3, 4)
    col = col * (0.88 + 0.16 * n[..., None])
    # (the tile repeats up the wall, so nothing here may change with v alone: no gradient, the stain from tiling noise)
    # the rolls' seams every half metre, a hair darker, one a little lifted
    for sx in (0.0, 0.5):
        d = np.minimum(np.abs(u - sx), np.abs(u - sx - 1))
        col = col * (1 - 0.18 * smooth(0.004, 0.0, d)[..., None])
    # a faint water stain
    st = fbm(u, v, seed + 50, 2, 3)
    ring_ = smooth(0.015, 0.0, np.abs(st - 0.62))
    col = col * (1 - 0.1 * smooth(0.62, 0.7, st)[..., None]) - 0.02 * ring_[..., None]
    return np.clip(col, 0, 1)


def mix(a, b, t):
    return a * (1 - t[..., None]) + np.array(b) * t[..., None]


def save(name, col, height, relief=0.6):
    col = col.reshape(N, SS, N, SS, 3).mean(axis=(1, 3))
    h = height.reshape(N, SS, N, SS).mean(axis=(1, 3))
    Image.fromarray((np.clip(col, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, f"wp_{name}_albedo.png"))
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * relief
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * relief
    nrm = np.dstack([-gx, gy, np.ones_like(h)])
    nrm /= np.linalg.norm(nrm, axis=2, keepdims=True)
    Image.fromarray(((nrm * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, f"wp_{name}_normal.png"))
    print("wrote", name, col.mean(axis=(0, 1)).round(3))


u, v = grid()
paper = fbm(u, v, 7, 32, 3)   # the paper's own grain

# damask: deep olive flock on old gold
x, y = cell(u, v, 2, 2)
m = damask_mask(x, y)
col = mix(np.full(u.shape + (3,), (0.47, 0.38, 0.2)), (0.2, 0.2, 0.1), m * 0.85)
col = mix(col, (0.55, 0.42, 0.18), smooth(0.4, 0.5, m) * smooth(0.6, 0.5, m) * 0.6)  # a gold edge to the flock
col = col * (0.95 + 0.08 * paper[..., None])
save("damask", age(col, u, v, 11), m + paper * 0.15, 3.0)

# floral: dusty rose ground, roses in oxblood, leaves in moss
rose, leaves = floral_mask(u, v)
col = np.full(u.shape + (3,), (0.5, 0.37, 0.35))
col = mix(col, (0.24, 0.29, 0.17), leaves * 0.9)
col = mix(col, (0.36, 0.08, 0.09), np.clip(rose, 0, 1))
col = col * (0.95 + 0.08 * paper[..., None])
save("floral", age(col, u, v, 21), rose * 0.8 + leaves * 0.5 + paper * 0.15, 2.0)

# trellis: powder blue, cream trellis, slate fleurs
lines, fleur = trellis_mask(u, v)
col = np.full(u.shape + (3,), (0.36, 0.44, 0.5))
col = mix(col, (0.62, 0.58, 0.48), lines * 0.9)
col = mix(col, (0.2, 0.25, 0.32), fleur)
col = col * (0.95 + 0.08 * paper[..., None])
save("trellis", age(col, u, v, 31), lines * 0.7 + fleur * 0.5 + paper * 0.15, 2.0)

# stripe: sage and cream, dark rules, a vine
broad, rule, vine = stripe_mask(u, v)
col = np.full(u.shape + (3,), (0.56, 0.52, 0.4))
col = mix(col, (0.33, 0.4, 0.3), broad)
col = mix(col, (0.15, 0.13, 0.1), rule)
col = mix(col, (0.24, 0.32, 0.2), vine)
col = col * (0.95 + 0.08 * paper[..., None])
save("stripe", age(col, u, v, 41), broad * 0.3 + vine * 0.6 + rule * 0.3 + paper * 0.15, 2.0)
