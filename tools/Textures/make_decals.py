"""Decals for the interiors (the fidelity pass, 2026-10-02): the stains and the rot of years of nobody, laid over the
walls and floors by DecalDresser.cs. Small (128 px), soft, PS2-era: they only ever darken.

    python tools/Textures/make_decals.py

Writes assets/textures/decals/<name>.png (RGBA: the colour, its alpha the coverage) and, for the wet ones, <name>_orm.png
(Godot's ORM: occlusion, roughness, metal; the puddles smooth, so the lantern glints in them):
  tide       a water stain risen from the floor, its tide lines ragged
  streak     drip streaks run down from above
  mould      a blotch of black mould, speckled at its edges
  floor      a dried stain on a floor, a darker ring where it dried
  wine       a spilt glass, dried dark red (the lodge's party)
  blood      old blood, brown-black: a smear and its drips (the bunker)
  puddle     standing water (its orm smooth)
  seep       a wet seep down a wall (its orm smooth)
  (2026-10-14g, for DecalDresser.Room's rules; the ghost is the one that lightens: a cleaner rectangle on a dirty wall)
  crack      a crack wandering down a wall, branching
  peel       old paint lifting off in flakes
  scuff      dark rubbed marks, low on walls and floors
  ghost      where a picture hung: the wall round it sooted, the rectangle cleaner, a nail hole
  ceiling    a water stain on a ceiling, its tide rings
  soot       a plume of black up a wall from a flame
"""
import os
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "textures", "decals")
os.makedirs(OUT, exist_ok=True)
N = 128
rng = np.random.default_rng(2026)


def noise(scale, octaves=4, seed=0):
    """Smooth value noise (tileable enough for decals), 0..1."""
    r = np.random.default_rng(seed)
    out = np.zeros((N, N), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        g = max(2, int(scale * 2 ** o))
        grid = r.random((g + 1, g + 1)).astype(np.float32)
        y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N * g
        x0, y0 = x.astype(int), y.astype(int)
        fx, fy = x - x0, y - y0
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        a = grid[y0, x0] * (1 - fx) + grid[y0, x0 + 1] * fx
        b = grid[y0 + 1, x0] * (1 - fx) + grid[y0 + 1, x0 + 1] * fx
        out += (a * (1 - fy) + b * fy) * amp
        tot += amp
        amp *= 0.5
    return out / tot


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


Y, X = np.mgrid[0:N, 0:N].astype(np.float32) / (N - 1)
R = np.sqrt((X - 0.5) ** 2 + (Y - 0.5) ** 2) * 2


def save(name, rgb, alpha, orm=None):
    img = np.dstack([np.clip(rgb, 0, 1), np.clip(alpha, 0, 1)[..., None]])
    Image.fromarray((img * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, name + ".png"))
    if orm is not None:
        Image.fromarray((np.clip(orm, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, name + "_orm.png"))
    print(name, f"coverage {alpha.mean():.2f}")


def col(r, g, b, shade):
    shade = np.broadcast_to(np.asarray(shade, np.float32), (N, N))
    return np.dstack([r * shade, g * shade, b * shade])


# the tide: risen from the bottom edge, its top a ragged line, darker bands where the water stood
n1, n2 = noise(3, 4, 1), noise(9, 3, 2)
top = 0.45 + 0.25 * n1 + 0.06 * (n2 - 0.5)
inside = smooth(top + 0.02, top - 0.05, 1 - Y)
bands = 0.0
for k, h in enumerate((0.0, 0.07, 0.15)):
    d = np.abs((1 - Y) - (top - h))
    bands = np.maximum(bands, (1 - smooth(0.0, 0.012 + 0.006 * k, d)) * (0.9 - 0.25 * k))
edge = smooth(0.0, 0.12, X) * smooth(1.0, 0.88, X)
a = (inside * (0.35 + 0.25 * n2) + bands * 0.45) * edge
save("tide", col(0.2, 0.15, 0.09, 0.8 + 0.4 * n2), a)

# streaks: drips run down from the top edge, each its own length and weight
a = np.zeros((N, N), np.float32)
for i in range(14):
    x0 = rng.uniform(0.08, 0.92)
    w = rng.uniform(0.006, 0.02)
    ln = rng.uniform(0.35, 1.0)
    wob = 0.01 * np.sin(Y * rng.uniform(8, 20) + rng.uniform(0, 6))
    line = 1 - smooth(w * 0.4, w, np.abs(X - x0 - wob))
    fade = smooth(ln, ln * 0.4, Y)
    a = np.maximum(a, line * fade * rng.uniform(0.3, 0.7))
a = np.maximum(a, 0.25 * smooth(0.25, 0.0, Y) * noise(6, 3, 3))
a *= smooth(0.0, 0.06, X) * smooth(1.0, 0.94, X)
save("streak", col(0.13, 0.11, 0.09, 0.9 + 0.2 * noise(10, 2, 4)), a)

# mould: a blotch, speckled outward
n1, n2 = noise(4, 5, 5), noise(16, 2, 6)
body = smooth(0.75, 0.35, R + 0.45 * (n1 - 0.5))
speck = (n2 > 0.62).astype(np.float32) * smooth(1.0, 0.5, R)
a = np.clip(body * (0.55 + 0.4 * n1) + speck * 0.5, 0, 0.9)
save("mould", col(0.04, 0.05, 0.035, 0.7 + 0.6 * n1), a)

# a floor stain: an irregular dried pool, its ring darker
n1 = noise(3, 4, 7)
rr = R + 0.35 * (n1 - 0.5)
pool = smooth(0.8, 0.6, rr)
ring = (1 - smooth(0.0, 0.05, np.abs(rr - 0.7))) * 0.6
a = pool * 0.35 + ring * 0.5
save("floor", col(0.16, 0.12, 0.08, 0.8 + 0.4 * noise(12, 2, 8)), a)

# wine: a spilt glass, dried
n1 = noise(4, 4, 9)
rr = np.sqrt(((X - 0.42) / 1.2) ** 2 + (Y - 0.5) ** 2) * 2 + 0.3 * (n1 - 0.5)
pool = smooth(0.62, 0.45, rr)
ring = (1 - smooth(0.0, 0.035, np.abs(rr - 0.55))) * 0.7
drops = np.zeros((N, N), np.float32)
for i in range(7):
    cx, cy, r = rng.uniform(0.6, 0.95), rng.uniform(0.2, 0.8), rng.uniform(0.012, 0.035)
    drops = np.maximum(drops, smooth(r, r * 0.5, np.sqrt((X - cx) ** 2 + (Y - cy) ** 2)))
a = pool * 0.55 + ring * 0.4 + drops * 0.6
save("wine", col(0.17, 0.025, 0.035, 0.8 + 0.3 * n1), a)

# old blood: a smear dragged sideways, its drips, brown-black
n1, n2 = noise(5, 4, 10), noise(14, 3, 11)
smear = smooth(0.32, 0.12, np.abs(Y - 0.42 - 0.08 * (n1 - 0.5)) + 0.25 * np.abs(X - 0.5) ** 2) * smooth(0.0, 0.15, X) * smooth(1.0, 0.8, X)
drip = np.zeros((N, N), np.float32)
for i in range(6):
    x0, ln = rng.uniform(0.2, 0.8), rng.uniform(0.55, 0.95)
    w = rng.uniform(0.006, 0.014)
    drip = np.maximum(drip, (1 - smooth(w * 0.5, w, np.abs(X - x0))) * smooth(ln, ln * 0.7, Y) * (Y > 0.42))
a = np.clip(smear * (0.6 + 0.4 * n2) + drip * 0.7, 0, 0.85)
save("blood", col(0.12, 0.03, 0.02, 0.7 + 0.5 * n2), a)

# a puddle: dark wet floor, smooth (its orm: roughness low)
n1 = noise(3, 4, 12)
rr = R + 0.4 * (n1 - 0.5)
a = smooth(0.85, 0.55, rr) * 0.75
orm = np.dstack([np.ones((N, N)), 0.06 + 0.5 * smooth(0.55, 0.85, rr), np.zeros((N, N))])
save("puddle", col(0.05, 0.05, 0.05, 1.0), a, orm)

# a seep: wet down a wall from a crack, smooth
n1 = noise(4, 4, 13)
w = 0.1 + 0.2 * Y + 0.08 * (n1 - 0.5)
a = smooth(w, w * 0.4, np.abs(X - 0.5 - 0.05 * np.sin(Y * 9))) * smooth(1.0, 0.75, Y) * 0.6
orm = np.dstack([np.ones((N, N)), 0.1 + 0.7 * (1 - a / 0.6), np.zeros((N, N))])
save("seep", col(0.06, 0.06, 0.055, 1.0), a, orm)

# ---- (2026-10-14g, the interiors kit: DecalDresser.Room's rules) ----

# a crack: a jagged line wandering down from the top edge, a branch or two, a soft halo of dirt along it
a = np.zeros((N, N), np.float32)


def crack(x0, y0, ang, length, width, depth=0):
    global a
    x, y = x0, y0
    steps = int(length * N * 2)
    for i in range(steps):
        if i % 4 == 0:
            ang += rng.normal(0, 0.45)
            ang = 0.85 * ang + 0.15 * (np.pi / 2)    # (drawn on downward, as cracks run)
        x += np.cos(ang) * 0.5 / N
        y += np.sin(ang) * 0.5 / N
        if not (0.02 < x < 0.98 and 0.0 <= y < 0.98):
            break
        w = width * (1 - i / steps * 0.7)
        d = np.sqrt((X - x) ** 2 + (Y - y) ** 2)
        a = np.maximum(a, 1 - smooth(w * 0.5, w, d))
        if depth < 2 and rng.random() < 0.007:
            crack(x, y, ang + rng.choice([-1, 1]) * rng.uniform(0.6, 1.1), length * 0.4, width * 0.6, depth + 1)


crack(0.5, 0.0, np.pi / 2, 1.0, 0.02)
blur = np.array(Image.fromarray((a * 255).astype(np.uint8)).resize((32, 32), Image.BILINEAR).resize((N, N), Image.BILINEAR), np.float32) / 255
save("crack", col(0.07, 0.06, 0.05, 1.0), np.clip(a * 0.95 + blur * 0.35, 0, 0.95))

# peeling paint: flakes of old pale paint lifting off, their edges dark, the bare plaster between a little darker
n1, n2 = noise(5, 4, 21), noise(18, 2, 22)
patch = smooth(0.78, 0.45, R + 0.5 * (n1 - 0.5))
flake = (n2 > 0.55).astype(np.float32)
edge = (np.abs(n2 - 0.55) < 0.025).astype(np.float32)
rgb = np.where(flake[..., None] > 0, col(0.47, 0.45, 0.4, 1.0), col(0.27, 0.24, 0.2, 1.0))
rgb = np.where(edge[..., None] > 0, col(0.08, 0.07, 0.06, 1.0), rgb)
save("peel", rgb, patch * np.clip(flake * 0.55 + (1 - flake) * 0.6 + edge * 0.4, 0, 0.9))

# scuffs: dark rubbed marks, long and low (a skirting, a doorway's foot, a chair's back against the wall)
a = np.zeros((N, N), np.float32)
for i in range(16):
    cx, cy = rng.uniform(0.1, 0.9), rng.uniform(0.25, 0.75)
    ln, th = rng.uniform(0.08, 0.35), rng.uniform(0.006, 0.02)
    ang = rng.normal(0, 0.18)
    dx, dy = X - cx, Y - cy
    u = dx * np.cos(ang) + dy * np.sin(ang)
    v = -dx * np.sin(ang) + dy * np.cos(ang)
    a = np.maximum(a, (1 - smooth(th * 0.4, th, np.abs(v))) * smooth(ln, ln * 0.6, np.abs(u)) * rng.uniform(0.3, 0.7))
a = np.maximum(a, 0.18 * smooth(0.5, 0.2, np.abs(Y - 0.5)) * noise(8, 3, 23))
a *= smooth(0.0, 0.08, X) * smooth(1.0, 0.92, X)
save("scuff", col(0.09, 0.08, 0.07, 0.9 + 0.2 * noise(12, 2, 24)), a)

# a ghost: where a picture hung for forty years: the wall round it sooted and yellowed, the rectangle cleaner (the
# frame's shadow line darkest), a nail hole above
n1 = noise(6, 3, 25)
inside = smooth(0.0, 0.025, X - 0.16) * smooth(0.0, 0.025, 0.84 - X) * smooth(0.0, 0.025, Y - 0.14) * smooth(0.0, 0.025, 0.86 - Y)
qx, qy = np.abs(X - 0.5) - 0.34, np.abs(Y - 0.5) - 0.36
box = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0)
rim = 1 - smooth(0.0, 0.03, np.abs(box))
outer = smooth(0.0, 0.14, np.minimum(np.minimum(X, 1 - X), np.minimum(Y, 1 - Y)))
nail = 1 - smooth(0.006, 0.012, np.sqrt((X - 0.5) ** 2 + (Y - 0.06) ** 2))
rgb = np.where(inside[..., None] > 0.5, col(0.44, 0.41, 0.35, 1.0), col(0.2, 0.16, 0.11, 0.8 + 0.4 * n1))
rgb = np.where(nail[..., None] > 0.5, col(0.03, 0.03, 0.03, 1.0), rgb)
save("ghost", rgb, np.clip(inside * 0.1 + (1 - inside) * outer * 0.28 * (0.6 + 0.4 * n1) + rim * 0.35 + nail * 0.9, 0, 0.9))

# a ceiling stain: a blotch where water stood above, its rim a brown tide ring, rings inside it
n1, n2 = noise(3, 4, 26), noise(10, 3, 27)
rr = R + 0.38 * (n1 - 0.5)
body = smooth(0.82, 0.55, rr)
rings = np.zeros((N, N), np.float32)
for k, h in enumerate((0.78, 0.62, 0.47)):
    rings = np.maximum(rings, (1 - smooth(0.0, 0.02 + 0.006 * k, np.abs(rr - h))) * (0.8 - 0.2 * k))
save("ceiling", col(0.24, 0.17, 0.09, 0.8 + 0.4 * n2), np.clip(body * 0.3 + rings * 0.6, 0, 0.85))

# soot: a plume of black up a wall from a flame below (narrow at its foot, the bottom edge; spreading up)
n1 = noise(5, 4, 28)
w = 0.12 + 0.36 * (1 - Y)
a = smooth(w, w * 0.3, np.abs(X - 0.5 - 0.06 * (n1 - 0.5))) * smooth(1.0, 0.7, Y) * smooth(0.0, 0.2, Y) * (0.55 + 0.4 * n1)
save("soot", col(0.03, 0.028, 0.025, 1.0), np.clip(a, 0, 0.85))
