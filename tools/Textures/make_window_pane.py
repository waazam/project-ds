"""The lodge's window panes, seen from inside (2026-10-10c, the owner: "we need large windows around the room that let more
light in ... the windows should be snowed in a little").

One 256 x 512 texture, two panes stacked: the top half a pane of grey-blue snow light, frost feathering in from its
corners and edges; the bottom half the same pane with snow drifted up against the glass outside, its top a soft uneven
line, bluer in its shadowed body, brighter at its crest where the light comes through it. The window builder
(SkiLodge.InnerWindow) gives every pane of the bottom row the drift and the rest the clear half. Kept dim (the PS2 look,
and a window is never a glare).

    python tools/textures/make_window_pane.py
"""
import os
import numpy as np
from PIL import Image

W, H = 256, 256
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "textures", "lodge")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(23)


def noise(w, h, f, seed):
    r = np.random.default_rng(seed)
    g = r.random((f + 2, f + 2))
    y, x = np.mgrid[0:h, 0:w]
    fx, fy = x / w * f, y / h * f
    i, j = fx.astype(int), fy.astype(int)
    tx, ty = fx - i, fy - j
    tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
    return (g[j, i] * (1 - tx) + g[j, i + 1] * tx) * (1 - ty) + (g[j + 1, i] * (1 - tx) + g[j + 1, i + 1] * tx) * ty


def fbm(w, h, seed, base=4):
    return sum(noise(w, h, base * 2 ** o, seed + o) * 0.5 ** o for o in range(5)) / 1.94


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


y, x = np.mgrid[0:H, 0:W] / np.array([H, W])[:, None, None]
u, v = x, y  # v down

# the light outside: an overcast, a little brighter toward the top of the pane, faint blowing snow in it
sky = np.dstack([0.4 + 0.08 * (1 - v), 0.45 + 0.08 * (1 - v), 0.53 + 0.08 * (1 - v)])
streak = noise(W, H, 6, 5)[..., None] * 0.04
sky = sky + streak

# frost: feathered crystals from the corners and in along the edges
edge = np.minimum.reduce([u, 1 - u, v, 1 - v])
corner = np.minimum(np.hypot(u, v), np.minimum(np.hypot(1 - u, v), np.minimum(np.hypot(u, 1 - v), np.hypot(1 - u, 1 - v))))
fn = fbm(W, H, 9, 6)
frost_amt = np.clip(smooth(0.26, 0.04, corner + (fn - 0.5) * 0.22) * 0.85 + smooth(0.05, 0.0, edge + (fn - 0.5) * 0.06) * 0.6, 0, 1)
# its fern-like grain
fern = np.abs(np.sin((u * 37 + v * 23 + fn * 6.0) * 3.0)) * np.abs(np.sin((u * 19 - v * 41 + fn * 5.0) * 2.0))
frost_col = np.array([0.66, 0.72, 0.78])


def with_frost(c, seed):
    f = (frost_amt * (0.75 + 0.25 * fern))[..., None]
    return c * (1 - f) + frost_col * f


clear = with_frost(sky, 1)

# the drift: snow banked up the glass outside to a soft, uneven crest a little under halfway
crest = 0.58 + 0.07 * (noise(W, 1, 3, 31)[0] - 0.5) * 2 + 0.03 * (noise(W, 1, 9, 32)[0] - 0.5) * 2
d = v - crest[None, :]
inside = smooth(-0.01, 0.015, d)
layers = 0.035 * np.sin((v + 0.08 * fbm(W, H, 6, 12)) * 70.0)
body = np.dstack([0.64 - 0.12 * v, 0.7 - 0.12 * v, 0.8 - 0.1 * v]) * (0.88 + 0.1 * fbm(W, H, 40, 8)[..., None] + layers[..., None])
glow = smooth(0.05, 0.0, d)[..., None] * 0.14  # the crest lit through
snow = body + glow * np.array([0.9, 0.95, 1.0])
drift = sky * (1 - inside[..., None]) + snow * inside[..., None]
drift = with_frost(drift, 2)

img = np.vstack([clear, drift])
Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "window_pane.png"))
print("wrote window_pane.png", img.mean(axis=(0, 1)).round(3))
