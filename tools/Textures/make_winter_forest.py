"""Act 22's winter forest textures (2026-09-30 overhaul), in the game's PS2 look (256 px, soft, toned down):

- the snow, from the owner's Poly Haven snow sets (snow_01, snow_02: CC0; the 4k zips in Downloads, the 1k normal
  maps fetched): snow_02 is the fresh, deep snow off the road; snow_01 (trodden, boot prints) the packed crust
  near the lodge, and the plowed road (with a little of make_snow.py's asphalt_snow grit in it).
- the bark: Poly Haven's bark_brown_02 and bark_willow_02 for the bare broadleaves (their moss and warmth
  pulled out to a cold winter grey-brown), pine_bark for the firs.
- twigs: a spray of fine bare twigs drawn here (RGBA, alpha-cut), a little snow along their tops, for the
  crossed cards at the ends of the bare trees' branches (the fine tracery the references are all about).

Sources go in build/polyhaven/winter (downloaded if missing; the snow diffuse maps from the owner's zips).

    python tools/Textures/make_winter_forest.py
"""
import os, subprocess, urllib.request, zipfile
import numpy as np
import imageio_ffmpeg

FF = imageio_ffmpeg.get_ffmpeg_exe()
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
SRC = os.path.join(ROOT, "build", "polyhaven", "winter")
SNOW = os.path.join(ROOT, "assets", "textures", "snow")
OUT = os.path.join(ROOT, "assets", "textures", "winter")
DOWNLOADS = os.path.join(os.path.expanduser("~"), "Downloads")
os.makedirs(SRC, exist_ok=True)
os.makedirs(OUT, exist_ok=True)
N = 256


def fetch(asset, kind):
    dest = os.path.join(SRC, f"{asset}_{kind}_1k.jpg")
    if os.path.exists(dest):
        return dest
    # the owner's snow zips first (the diffuse maps only; their normals are EXR)
    z = os.path.join(DOWNLOADS, f"{asset}_4k.blend.zip")
    if kind == "diff" and os.path.exists(z):
        with zipfile.ZipFile(z) as f:
            with open(dest, "wb") as o:
                o.write(f.read(f"textures/{asset}_diff_4k.jpg"))
        return dest
    url = f"https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/{asset}/{asset}_{kind}_1k.jpg"
    print("download", url)
    urllib.request.urlretrieve(url, dest)
    return dest


def read(path, n=N, pix="rgb24", ch=3):
    raw = subprocess.run([FF, "-v", "error", "-i", path, "-vf", f"scale={n}:{n}:flags=area", "-f", "rawvideo", "-pix_fmt", pix, "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(n, n, ch).astype(np.float32) / 255.0


def write(path, arr, pix="rgb24"):
    n = arr.shape[0]
    data = (np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8).tobytes()
    subprocess.run([FF, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", pix, "-s", f"{arr.shape[1]}x{n}", "-i", "-", path], input=data, check=True)


def seamless(a, w=12):
    ramp = np.linspace(0, 1, w, dtype=np.float32)
    for i in range(w):
        t = 0.5 * (1 - ramp[i])
        a[i, :] = a[i, :] * (1 - t) + a[-1 - i, :] * t
        a[:, i] = a[:, i] * (1 - t) + a[:, -1 - i] * t
    return a


def soft_normal(nor, keep):
    """Pulls a normal map toward flat (the PS2 look: shallow relief)."""
    v = nor * 2 - 1
    v[:, :, 0] *= keep
    v[:, :, 1] *= keep
    v /= np.linalg.norm(v, axis=2, keepdims=True) + 1e-6
    return v * 0.5 + 0.5


LUMA = np.array([0.299, 0.587, 0.114], np.float32)

# ---- the snow
for name, src, tint, ceil, contrast in [
    ("snow", "snow_02", (0.80, 0.84, 0.92), 0.80, 0.9),
    ("packed", "snow_01", (0.72, 0.79, 0.90), 0.74, 1.1),
]:
    col = read(fetch(src, "diff"))
    lum = col @ LUMA
    m = lum.mean()
    lum = np.clip(m + (lum - m) * contrast, 0, 1)
    rgb = lum[:, :, None] * np.array(tint, np.float32)[None, None, :]
    rgb = rgb / max(rgb.max(), 1e-6) * ceil
    write(os.path.join(SNOW, f"{name}_albedo.png"), seamless(rgb))
    write(os.path.join(SNOW, f"{name}_normal.png"), soft_normal(read(fetch(src, "nor_gl")), 0.8))
    print(name, src, f"mean {rgb.mean():.3f}")

# ---- the plowed road: packed, trodden snow (snow_01's boot prints) with a little of the road's grit in it (the
# snowplow's own asphalt_snow, make_snow.py's source): it had read as brown dirt in the lantern's light
trod = read(fetch("snow_01", "diff")) @ LUMA
grit_path = os.path.join(ROOT, "build", "polyhaven", "asphalt_snow_col.jpg")
grit = read(grit_path) @ LUMA if os.path.exists(grit_path) else trod
lum = 0.72 * trod + 0.28 * grit
m = lum.mean()
lum = np.clip(m + (lum - m) * 1.15, 0, 1)
rgb = lum[:, :, None] * np.array((0.74, 0.77, 0.84), np.float32)[None, None, :]
rgb = rgb / max(rgb.max(), 1e-6) * 0.68
write(os.path.join(SNOW, "plowed_albedo.png"), seamless(rgb))
write(os.path.join(SNOW, "plowed_normal.png"), soft_normal(read(fetch("snow_01", "nor_gl")), 0.9))
print("plowed", f"mean {rgb.mean():.3f}")

# ---- the bark: cold, grey-brown, the moss gone
for name, src, tint, ceil, contrast in [
    ("bark", "bark_brown_02", (0.74, 0.70, 0.66), 0.62, 1.15),
    ("bark_grey", "bark_willow_02", (0.72, 0.71, 0.70), 0.66, 1.1),
    ("pine_bark", "pine_bark", (0.66, 0.58, 0.52), 0.55, 1.0),
]:
    col = read(fetch(src, "diff"))
    lum = col @ LUMA
    m = lum.mean()
    lum = np.clip(m + (lum - m) * contrast, 0, 1)
    rgb = 0.2 * col + 0.8 * lum[:, :, None]           # most of the colour out (no green moss in winter)
    rgb = rgb * np.array(tint, np.float32)[None, None, :]
    rgb = rgb / max(rgb.max(), 1e-6) * ceil
    write(os.path.join(OUT, f"{name}_albedo.png"), seamless(rgb))
    write(os.path.join(OUT, f"{name}_normal.png"), soft_normal(read(fetch(src, "nor_gl")), 0.75))
    print(name, src, f"mean {rgb.mean():.3f}")

# ---- the twigs: a spray of fine bare twigs, drawn 4x and area-downscaled (anti-aliased), alpha-cut in game
S = N * 4
rng = np.random.default_rng(2222)
cov = np.zeros((S, S), np.float32)     # twig coverage
top = np.zeros((S, S), np.float32)     # snow along their tops
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)


def seg(a, b, r):
    x0, y0 = a
    x1, y1 = b
    lo_x, hi_x = int(max(min(x0, x1) - r - 2, 0)), int(min(max(x0, x1) + r + 3, S))
    lo_y, hi_y = int(max(min(y0, y1) - r - 2, 0)), int(min(max(y0, y1) + r + 3, S))
    if lo_x >= hi_x or lo_y >= hi_y:
        return
    px, py = xx[lo_y:hi_y, lo_x:hi_x], yy[lo_y:hi_y, lo_x:hi_x]
    dx, dy = x1 - x0, y1 - y0
    L2 = dx * dx + dy * dy + 1e-6
    t = np.clip(((px - x0) * dx + (py - y0) * dy) / L2, 0, 1)
    d = np.hypot(px - (x0 + t * dx), py - (y0 + t * dy))
    c = np.clip(r + 0.5 - d, 0, 1)
    cov[lo_y:hi_y, lo_x:hi_x] = np.maximum(cov[lo_y:hi_y, lo_x:hi_x], c)
    # snow: a thin rim just above the twig where it lies near level (up the image is -y)
    flat = 1 - min(abs(dy) / (np.sqrt(L2)), 1)
    if flat > 0.35:
        ds = np.hypot(px - (x0 + t * dx), py + r * 1.3 - (y0 + t * dy))
        s = np.clip(r * 0.9 + 0.5 - ds, 0, 1) * flat
        top[lo_y:hi_y, lo_x:hi_x] = np.maximum(top[lo_y:hi_y, lo_x:hi_x], s)


def twig(p, ang, length, r, depth):
    # a slightly crooked twig: two or three kinked steps
    steps = 3
    for _ in range(steps):
        ang += rng.normal(0, 0.18)
        q = (p[0] + np.cos(ang) * length / steps, p[1] - np.sin(ang) * length / steps)
        seg(p, q, r)
        if depth > 0 and rng.random() < 0.55:
            side = 1 if rng.random() < 0.5 else -1
            twig(q, ang + side * rng.uniform(0.35, 0.8), length * rng.uniform(0.45, 0.65), max(r * 0.7, 1.2), depth - 1)
        p = q
        r = max(r * 0.85, 1.1)
    if depth > 0:
        for side in (-1, 1):
            if rng.random() < 0.8:
                twig(p, ang + side * rng.uniform(0.3, 0.7), length * rng.uniform(0.5, 0.7), max(r * 0.75, 1.1), depth - 1)


# the spray grows up from the bottom middle (its card is hung at a branch's tip, the base toward the branch)
for i in range(5):
    base = (S * 0.5 + rng.uniform(-S * 0.08, S * 0.08), S * 0.99)
    twig(base, np.pi / 2 + rng.uniform(-0.55, 0.55), S * rng.uniform(0.34, 0.44), 7.0, 3)


def down(a):
    return a.reshape(N, 4, N, 4).mean(axis=(1, 3))


alpha = np.clip(down(cov) * 1.6, 0, 1)
snow = np.clip(down(top) * 1.5, 0, 1)
bark = np.array([0.2, 0.18, 0.17], np.float32)
white = np.array([0.72, 0.76, 0.84], np.float32)
rgb = bark[None, None, :] * (1 - snow[:, :, None]) + white[None, None, :] * snow[:, :, None]
alpha = np.maximum(alpha, snow * 0.9)
# (the colour bled out under transparent pixels, so mipmaps don't fringe white or black)
rgb = np.where(alpha[:, :, None] > 0.02, rgb, bark[None, None, :])
write(os.path.join(OUT, "twigs.png"), np.dstack([rgb, alpha]), pix="rgba")
print("twigs", f"cover {alpha.mean():.3f}")

# ---- a fir bough (the winter firs' tiers): a stem from the base (bottom middle) to the tip (top), its needles swept
# forward either side in a long spindle, side shoots off it; dark cold green, a brown stem. The snow on the boughs is
# the tree's own (the caps along each tier), not drawn here.
cov[:] = 0
top[:] = 0
stemc = np.zeros((S, S), np.float32)


def needles(p, ang, length, spread_len):
    """A shoot from p at ang: its stem, and needles every few pixels both sides, longest mid-way."""
    steps = int(length / 7)
    x, y = p
    for i in range(steps):
        f = i / max(steps - 1, 1)
        ang += rng.normal(0, 0.012)
        nx, ny = x + np.cos(ang) * 7, y - np.sin(ang) * 7
        seg((x, y), (nx, ny), 2.2)
        nl = spread_len * (np.sin(np.pi * min(f * 1.1, 1.0)) * 0.85 + 0.15)
        for side in (-1, 1):
            a = ang + side * rng.uniform(0.75, 1.05)
            l = nl * rng.uniform(0.8, 1.1)
            seg((nx, ny), (nx + np.cos(a) * l, ny - np.sin(a) * l), 1.6)
        x, y = nx, ny


# the stem (and its side shoots), the needles on all
seg((S * 0.5, S * 0.99), (S * 0.5, S * 0.07), 2.6)
stemc[:] = cov
needles((S * 0.5, S * 0.99), np.pi / 2, S * 0.92, S * 0.11)
for i in range(9):
    f = 0.1 + i * 0.09
    y = S * (0.99 - 0.92 * f)
    for side in (-1, 1):
        if rng.random() < 0.9:
            needles((S * 0.5, y), np.pi / 2 + side * rng.uniform(0.6, 0.9), S * 0.44 * (1 - f * 0.65), S * 0.07)
alpha = np.clip(down(cov) * 1.4, 0, 1)
green = np.array([0.1, 0.15, 0.12], np.float32)
brown = np.array([0.2, 0.16, 0.13], np.float32)
vary = rng.uniform(0.85, 1.15, (N, N)).astype(np.float32)
rgb = green[None, None, :] * vary[:, :, None]
rgb = np.where(down(stemc)[:, :, None] > 0.5, brown[None, None, :], rgb)
rgb = np.where(alpha[:, :, None] > 0.02, rgb, green[None, None, :])
write(os.path.join(OUT, "fir_bough.png"), np.dstack([rgb, alpha]), pix="rgba")
print("fir bough", f"cover {alpha.mean():.3f}")
