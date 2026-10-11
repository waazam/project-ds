"""The lodge's round and oval rugs (2026-10-10c, the owner: "Make sure all the new rugs in the lodge have more design details
on them and make them more christmas themed"). Drawn here (no licence to carry), 1024 px, a disc each; the game stretches
a disc into an oval where it's laid one.

- holly:      bottle green, a red border; a wreath of holly leaves and berries round it, a poinsettia in the middle,
              snowflakes scattered between.
- snowflake:  powder blue, navy border; a ring of eight big six-armed snowflakes, a great one in the middle, a dotted band.
- pines:      oxblood, a gold and green border; a ring of little pine trees pointing out from the middle, stars between,
              an eight-pointed star in the middle.
- candycane:  cream, red and green twisted candy-cane bands; a ring of baubles on their caps, a star in the middle.

Every one has a braided rim, a woven grain, and the wear of years: pale where it's walked on, a little faded.

    python tools/textures/make_rugs.py
"""
import math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

N = 1024
SS = 2
S = N * SS
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "textures", "lodge")
os.makedirs(OUT, exist_ok=True)
C = S / 2


def P(r, a):
    """A point at radius r (0..1 of the disc) and angle a (radians)."""
    return (C + math.cos(a) * r * C, C + math.sin(a) * r * C)


def disc(d, r, fill):
    d.ellipse([C - r * C, C - r * C, C + r * C, C + r * C], fill=fill)


def ring(d, r0, r1, fill):
    disc(d, r1, fill)


def leaf(d, r, a, size, col, vein):
    # a holly leaf: a pointed oval with spiked edges, along the tangent
    pts = []
    for i in range(24):
        t = i / 23
        along = (t - 0.5) * size * 2.2
        w = math.sin(t * math.pi) * size * 0.55 * (1 + 0.25 * math.cos(t * math.pi * 6))
        pts.append((along, w))
    for i in range(23, -1, -1):
        t = i / 23
        along = (t - 0.5) * size * 2.2
        w = math.sin(t * math.pi) * size * 0.55 * (1 + 0.25 * math.cos(t * math.pi * 6 + math.pi))
        pts.append((along, -w))
    cx, cy = P(r, a)
    tang = a + math.pi / 2 + 0.5
    ca, sa = math.cos(tang), math.sin(tang)
    poly = [(cx + x * ca * C - y * sa * C, cy + x * sa * C + y * ca * C) for x, y in pts]
    d.polygon(poly, fill=col)
    d.line([(cx - size * 1.0 * ca * C, cy - size * 1.0 * sa * C), (cx + size * 1.0 * ca * C, cy + size * 1.0 * sa * C)], fill=vein, width=max(2, int(size * C * 0.06)))


def berries(d, r, a, size, col, hi):
    for k, (dr, da) in enumerate([(0, 0), (size * 0.9, 0.04), (-size * 0.6, 0.05)]):
        cx, cy = P(r + dr * 0.3, a + da)
        rr = size * C * 0.45
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=col)
        d.ellipse([cx - rr * 0.45, cy - rr * 0.6, cx - rr * 0.05, cy - rr * 0.2], fill=hi)


def snowflake(d, cx, cy, size, col, w):
    for k in range(6):
        a = k * math.pi / 3
        ex, ey = cx + math.cos(a) * size, cy + math.sin(a) * size
        d.line([(cx, cy), (ex, ey)], fill=col, width=w)
        for f in (0.45, 0.72):
            bx, by = cx + math.cos(a) * size * f, cy + math.sin(a) * size * f
            for s in (-1, 1):
                b = a + s * 0.75
                l = size * (0.32 if f < 0.6 else 0.22)
                d.line([(bx, by), (bx + math.cos(b) * l, by + math.sin(b) * l)], fill=col, width=max(1, w - 1))
    d.ellipse([cx - w * 1.5, cy - w * 1.5, cx + w * 1.5, cy + w * 1.5], fill=col)


def star(d, cx, cy, r_out, r_in, n, col, rot=0.0):
    pts = []
    for k in range(n * 2):
        rr = r_out if k % 2 == 0 else r_in
        a = rot + k * math.pi / n - math.pi / 2
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    d.polygon(pts, fill=col)


def poinsettia(d, cx, cy, size, red, dark, gold):
    for layer, (sc, col, off) in enumerate([(1.0, dark, 0.0), (0.92, red, 0.0), (0.62, red, math.pi / 6)]):
        for k in range(6):
            a = off + k * math.pi / 3
            L = size * sc
            tip = (cx + math.cos(a) * L, cy + math.sin(a) * L)
            side1 = (cx + math.cos(a + 0.32) * L * 0.42, cy + math.sin(a + 0.32) * L * 0.42)
            side2 = (cx + math.cos(a - 0.32) * L * 0.42, cy + math.sin(a - 0.32) * L * 0.42)
            d.polygon([(cx, cy), side1, tip, side2], fill=col)
            d.line([(cx, cy), tip], fill=dark, width=max(2, int(size * 0.025)))
    for k in range(7):
        a = k * math.tau / 7
        bx, by = cx + math.cos(a) * size * 0.1, cy + math.sin(a) * size * 0.1
        rr = size * 0.045
        d.ellipse([bx - rr, by - rr, bx + rr, by + rr], fill=gold)


def pine(d, cx, cy, size, a, col, trunk):
    # a little tree pointing out along a (its top outward)
    ca, sa = math.cos(a), math.sin(a)
    def T(x, y):  # x along the tree (0 base .. 1 top), y across
        return (cx + (x * ca - y * sa) * size, cy + (x * sa + y * ca) * size)
    d.polygon([T(-0.15, -0.08), T(0.05, -0.08), T(0.05, 0.08), T(-0.15, 0.08)], fill=trunk)
    for k, (b, w) in enumerate([(0.05, 0.42), (0.35, 0.32), (0.62, 0.22)]):
        d.polygon([T(b, -w), T(b + 0.42, 0), T(b, w)], fill=col)


def bauble(d, cx, cy, size, col, cap, hi):
    d.ellipse([cx - size, cy - size, cx + size, cy + size], fill=col)
    d.rectangle([cx - size * 0.3, cy - size * 1.3, cx + size * 0.3, cy - size * 0.9], fill=cap)
    d.ellipse([cx - size * 0.55, cy - size * 0.6, cx - size * 0.15, cy - size * 0.2], fill=hi)
    d.line([(cx - size * 0.9, cy), (cx + size * 0.9, cy)], fill=cap, width=max(2, int(size * 0.12)))


def braid(d, r, col_a, col_b, width):
    n = 180
    for k in range(n):
        a0 = k / n * math.tau
        a1 = (k + 0.9) / n * math.tau
        rr = r + (width * 0.25 if k % 2 else -width * 0.25)
        d.line([P(rr, a0), P(r - (width * 0.25 if k % 2 else -width * 0.25), a1)], fill=col_a if k % 2 else col_b, width=int(width * C * 0.9))


def candy_band(d, r0, r1, red, white, green, n=72):
    for k in range(n):
        a0 = k / n * math.tau
        a1 = (k + 1) / n * math.tau
        col = (red, white, green, white)[k % 4]
        # a twisted stripe: a parallelogram leaning along the band
        d.polygon([P(r0, a0), P(r0, a1), P(r1, a1 + 0.05), P(r1, a0 + 0.05)], fill=col)


def finish(img, seed, fade=0.08):
    a = np.asarray(img.convert("RGB")).astype(np.float32) / 255
    rng = np.random.default_rng(seed)
    h, w = a.shape[:2]
    # the weave: fine rows and columns of knots
    yy, xx = np.mgrid[0:h, 0:w]
    weave = 0.94 + 0.06 * ((np.sin(xx * 1.1) * np.sin(yy * 1.1)) > 0)
    # wear: pale where it's walked on (a soft blotch), and patches of it
    g = rng.random((12, 12))
    big = np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255
    wear = np.clip((big - 0.62) * 1.6, 0, 1) * 0.6
    a = a * weave[..., None]
    a = a * (1 - wear[..., None] * 0.25) + wear[..., None] * 0.25 * np.array([0.72, 0.68, 0.6])
    # faded overall a little toward warm grey
    a = a * (1 - fade) + fade * np.array([0.5, 0.46, 0.4])
    # alpha: the disc
    r = np.sqrt((xx - w / 2) ** 2 + (yy - h / 2) ** 2) / (w / 2)
    alpha = (r < 0.995).astype(np.float32)
    out = np.dstack([np.clip(a, 0, 1), alpha])
    return Image.fromarray((out * 255).astype(np.uint8), "RGBA")


def holly():
    img = Image.new("RGB", (S, S), (24, 62, 38))
    d = ImageDraw.Draw(img)
    green, dgreen, red, cream, gold = (24, 62, 38), (14, 40, 24), (132, 22, 20), (214, 200, 168), (190, 150, 70)
    disc(d, 1.0, red)
    disc(d, 0.9, cream)
    disc(d, 0.885, red)
    disc(d, 0.86, green)
    # a band of little cream diamonds inside the border
    for k in range(64):
        a = k / 64 * math.tau
        cx, cy = P(0.82, a)
        sz = 0.012 * C
        d.polygon([(cx, cy - sz), (cx + sz, cy), (cx, cy + sz), (cx - sz, cy)], fill=cream)
    # the wreath
    for k in range(28):
        a = k / 28 * math.tau
        leaf(d, 0.6 + 0.03 * math.sin(k * 1.7), a, 0.07, (34, 92, 46) if k % 2 else (22, 72, 36), dgreen)
        leaf(d, 0.56, a + 0.11, 0.06, (28, 84, 42), dgreen)
        if k % 2 == 0:
            berries(d, 0.585, a + 0.06, 0.022, (176, 26, 24), (240, 170, 160))
    # snowflakes scattered in the field
    for k in range(12):
        a = k / 12 * math.tau + 0.13
        cx, cy = P(0.73, a)
        snowflake(d, cx, cy, 0.035 * C, cream, 5)
        cx, cy = P(0.36, a + 0.26)
        snowflake(d, cx, cy, 0.03 * C, cream, 4)
    disc(d, 0.24, red)
    disc(d, 0.225, cream)
    disc(d, 0.21, green)
    poinsettia(d, C, C, 0.2 * C, (176, 28, 26), (110, 16, 16), gold)
    braid(d, 0.975, (120, 20, 18), (70, 12, 10), 0.03)
    return finish(img, 11)


def snowflakes():
    img = Image.new("RGB", (S, S), (118, 150, 178))
    d = ImageDraw.Draw(img)
    navy, blue, cream, pale = (30, 44, 82), (118, 150, 178), (226, 230, 232), (168, 192, 210)
    disc(d, 1.0, navy)
    disc(d, 0.9, cream)
    disc(d, 0.885, navy)
    disc(d, 0.86, blue)
    for k in range(90):
        a = k / 90 * math.tau
        cx, cy = P(0.82, a)
        rr = 0.007 * C
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=cream)
    for k in range(8):
        a = k / 8 * math.tau
        cx, cy = P(0.6, a)
        snowflake(d, cx, cy, 0.13 * C, cream, 9)
        cx, cy = P(0.6, a + math.pi / 8)
        snowflake(d, cx, cy, 0.04 * C, pale, 5)
    for k in range(16):
        a = k / 16 * math.tau
        cx, cy = P(0.38, a)
        star(d, cx, cy, 0.018 * C, 0.007 * C, 4, cream, a)
    disc(d, 0.27, navy)
    disc(d, 0.255, blue)
    snowflake(d, C, C, 0.23 * C, cream, 12)
    braid(d, 0.975, (36, 52, 92), (200, 206, 214), 0.03)
    return finish(img, 21)


def pines():
    img = Image.new("RGB", (S, S), (98, 24, 22))
    d = ImageDraw.Draw(img)
    ox, gold, green, dgreen, cream, brown = (98, 24, 22), (196, 154, 72), (30, 78, 44), (18, 50, 28), (220, 204, 170), (80, 50, 30)
    disc(d, 1.0, green)
    disc(d, 0.93, gold)
    disc(d, 0.91, dgreen)
    disc(d, 0.88, gold)
    disc(d, 0.865, ox)
    # a running zig-zag band
    pts = []
    for k in range(97):
        a = k / 96 * math.tau
        pts.append(P(0.82 if k % 2 else 0.78, a))
    d.line(pts, fill=gold, width=int(0.012 * C))
    for k in range(16):
        a = k / 16 * math.tau
        cx, cy = P(0.5, a)
        pine(d, cx, cy, 0.22 * C, a, green if k % 2 else (40, 96, 54), brown)
        cx, cy = P(0.66, a + math.pi / 16)
        star(d, cx, cy, 0.03 * C, 0.012 * C, 5, gold, a)
        cx, cy = P(0.42, a + math.pi / 16)
        rr = 0.012 * C
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=cream)
    disc(d, 0.25, gold)
    disc(d, 0.235, ox)
    star(d, C, C, 0.21 * C, 0.09 * C, 8, gold)
    star(d, C, C, 0.12 * C, 0.05 * C, 8, cream, math.pi / 8)
    braid(d, 0.975, (30, 70, 40), (170, 130, 60), 0.03)
    return finish(img, 31)


def candycane():
    img = Image.new("RGB", (S, S), (222, 210, 186))
    d = ImageDraw.Draw(img)
    cream, red, green, gold, dred = (222, 210, 186), (160, 26, 24), (32, 90, 50), (196, 154, 72), (110, 18, 16)
    disc(d, 1.0, red)
    candy_band(d, 0.95, 0.82, red, cream, green)
    disc(d, 0.82, gold)
    disc(d, 0.805, cream)
    cols = [red, green, gold, (40, 60, 120)]
    for k in range(14):
        a = k / 14 * math.tau
        cx, cy = P(0.62, a)
        d.line([P(0.76, a), (cx, cy - 0.05 * C)], fill=gold, width=4)
        bauble(d, cx, cy, 0.055 * C, cols[k % 4], gold, (255, 240, 230))
        cx, cy = P(0.44, a + math.pi / 14)
        star(d, cx, cy, 0.03 * C, 0.012 * C, 5, gold, a)
    candy_band(d, 0.31, 0.24, red, cream, green, 40)
    disc(d, 0.24, dred)
    star(d, C, C, 0.2 * C, 0.085 * C, 5, gold)
    star(d, C, C, 0.1 * C, 0.04 * C, 5, cream, math.pi / 5)
    braid(d, 0.975, (150, 24, 22), (220, 206, 180), 0.03)
    return finish(img, 41)


for name, fn in (("holly", holly), ("snowflake", snowflakes), ("pines", pines), ("candycane", candycane)):
    im = fn().resize((N, N), Image.LANCZOS)
    im.save(os.path.join(OUT, f"rug_{name}.png"))
    print("wrote", name)
