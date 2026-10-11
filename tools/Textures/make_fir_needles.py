"""The lobby's Christmas tree's needles (2026-10-10c, the owner: "the christmas tree still has a lot of gray textures").
It wore the winter woods' bough photo, snow-laden: under the lodge's lamps it read grey. Its own: a fir spray drawn here,
a stem down the card and needles off it both ways in layers, dark fir greens with a lighter tip to each, no snow; the alpha
cut round the needles (the material's alpha scissor).

    python tools/textures/make_fir_needles.py
"""
import os
import numpy as np
from PIL import Image, ImageDraw

N = 512
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "textures", "lodge")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1225)
img = Image.new("RGBA", (N, N), (20, 40, 22, 0))
d = ImageDraw.Draw(img)


def green(light):
    base = np.array([42, 96, 50]) + light * np.array([44, 78, 30]) + rng.normal(0, 6, 3)
    return tuple(int(c) for c in np.clip(base, 0, 255)) + (255,)


# a few sprays per card, each a stem with needles off both sides, the back ones darker
for spray in range(9):
    x0 = rng.uniform(0.08, 0.92) * N
    y0 = N * 0.98
    ang = np.deg2rad(rng.uniform(-14, 14) - 90)
    length = rng.uniform(0.75, 0.95) * N
    depth = spray / 8.0
    pts = []
    for i in range(80):
        t = i / 79
        bend = np.deg2rad(18) * np.sin(t * 2.0 + spray)
        a = ang + bend * t
        pts.append((x0 + np.cos(a) * length * t, y0 + np.sin(a) * length * t))
    for i in range(len(pts) - 1):
        d.line([pts[i], pts[i + 1]], fill=(40, 30, 20, 255), width=max(1, int(4 * (1 - i / 80)) + 1))
    for i in range(4, len(pts) - 1):
        t = i / 79
        (px, py), (qx, qy) = pts[i], pts[i + 1]
        tx, ty = qx - px, qy - py
        tl = np.hypot(tx, ty) + 1e-6
        tx, ty = tx / tl, ty / tl
        for side in (-1, 1):
            nl = (1 - t * 0.5) * N * rng.uniform(0.07, 0.1)
            # swept forward along the stem, out to the side
            sx, sy = -ty * side, tx * side
            dx, dy = sx * 0.85 + tx * 0.5, sy * 0.85 + ty * 0.5
            light = rng.uniform(0.0, 0.6) * (0.5 + 0.5 * depth)
            ex, ey = px + dx * nl, py + dy * nl
            d.line([(px, py), (ex, ey)], fill=green(light), width=4)
            # its paler tip
            d.line([(px + dx * nl * 0.75, py + dy * nl * 0.75), (ex, ey)], fill=green(min(1.0, light + 0.45)), width=2)

a = np.array(img).astype(np.float32)
# a little shading: darker toward the card's foot (the inside of the tree)
yy = np.linspace(0, 1, N)[:, None]
a[..., :3] *= (0.75 + 0.25 * (1 - yy))[..., None] if False else (0.75 + 0.25 * (1 - yy))[:, :, None]
Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(os.path.join(OUT, "xmas_needles.png"))
print("wrote xmas_needles.png")
