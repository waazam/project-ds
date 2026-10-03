"""The smoke's flipbook (the weather pass, 2026-10-03, for FireVfx): a puff of smoke over its life, 16 frames in a 4 x 4
sheet of 128 px cells. It billows out from a dense knot into a wide, thin, ragged wisp: each frame the same noise
field, advected outward and eroded more, so the frames run on from one another.

    python tools/Textures/make_smoke.py

Writes assets/textures/weather/smoke_sheet.png (white, the density in alpha).
"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "textures", "weather")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(66)
C = 128


def fbm(shape, octaves, seed):
    r = np.random.default_rng(seed)
    out = np.zeros(shape, np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        n = r.random(shape).astype(np.float32)
        out += ndimage.gaussian_filter(n, shape[0] / (2.5 * 2 ** o), mode="wrap") * amp
        tot += amp
        amp *= 0.55
    out /= tot
    return (out - out.min()) / (out.max() - out.min() + 1e-6)


base = fbm((C, C), 5, 1)
wisps = fbm((C, C), 4, 2)
yy, xx = np.mgrid[0:C, 0:C].astype(np.float32)
cx = cy = C / 2.0
sheet = np.zeros((C * 4, C * 4), np.float32)
for f in range(16):
    u = f / 15.0
    # the puff grows, and the noise is pushed outward from its middle as it does (it billows)
    r = np.hypot(xx - cx, yy - cy) / (C * 0.5)
    push = 1.0 + 0.6 * u
    sx = cx + (xx - cx) / push
    sy = cy + (yy - cy) / push
    n = ndimage.map_coordinates(base, [sy, sx], order=1, mode="wrap")
    w = ndimage.map_coordinates(wisps, [sy * 1.3, sx * 1.3], order=1, mode="wrap")
    radius = 0.62 + 0.33 * u
    # the outline itself lumpy: billows round its edge
    ang = np.arctan2(yy - cy, xx - cx)
    lump = 1.0 + 0.22 * (n - 0.5) * 2.0 + 0.08 * np.sin(ang * 5.0 + u * 2.0)
    shape = np.clip(1.0 - r / (radius * lump), 0.0, 1.0)
    # cauliflower lumps inside, a dense knot early, eroded to wisps late
    lumps = np.clip((n * 0.7 + w * 0.3 - (0.3 + 0.25 * u)) / 0.35, 0.0, 1.0)
    a = np.clip(shape ** (0.8 + 0.6 * u) * (0.45 + 0.75 * lumps), 0.0, 1.0) * (1.0 - 0.4 * u)
    a = ndimage.gaussian_filter(a, 1.0)
    y, x = (f // 4) * C, (f % 4) * C
    sheet[y:y + C, x:x + C] = a
rgba = np.dstack([np.ones_like(sheet)] * 3 + [sheet])
Image.fromarray((rgba * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "smoke_sheet.png"))
print("wrote smoke_sheet.png")
