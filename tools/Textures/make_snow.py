"""The snow's textures (the weather pass, 2026-10-03), cut from the owner's falling-snowflakes image ("Designed by
Kjpargeter / Freepik": credited in the game's credits and docs/CREDITS.md).

    python tools/Textures/make_snow.py [path/to/7225.jpg]

The source (a 5000 px JPG; default build/snow/falling_snowflakes_7225.jpg, kept out of the repository as its licence
asks) has its transparency baked in as a 60 x 60 checkerboard of two greys. That background is predictable, so each
pixel's coverage comes back as how far it stands above its own square's grey (the flakes are white).

Writes assets/textures/weather/:
  snow_atlas.png   512 px, 4 x 4 cells of 128 px (white, the coverage in alpha):
                     0-7    crystals, sharp (the near flakes: the six-armed ones in the image, each its own)
                     8-11   crystals defocused (between near and far)
                     12-13  soft round flakes (the image's dots, wet clumps)
                     14     a small soft speck   15  a tiny one (the far, dense snow; dust)
  snow_curtain.png 512 px, tileable: hundreds of the flakes shrunk to a few pixels, scattered (the far snow's
                   moving curtain)
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "build", "snow", "falling_snowflakes_7225.jpg")
OUT = os.path.join(ROOT, "assets", "textures", "weather")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7225)

img = np.asarray(Image.open(SRC).convert("L"), np.float32)
N = img.shape[0]
SQ = N / 60.0

# the checkerboard: each square's grey, measured from its own pixels (the darker of the two greys, or the lighter)
bg = np.zeros_like(img)
dark, light = 32.0, 89.0
for j in range(60):
    for i in range(60):
        y0, y1, x0, x1 = int(j * SQ), int((j + 1) * SQ), int(i * SQ), int((i + 1) * SQ)
        block = img[y0:y1, x0:x1]
        g = np.percentile(block, 20)
        bg[y0:y1, x0:x1] = dark if abs(g - dark) < abs(g - light) else light
# (along the squares' edges the JPEG rings: there the lighter grey is taken, so no edge reads as a flake)
edge = np.zeros_like(img, bool)
for k in range(61):
    c = int(round(k * SQ))
    edge[:, max(c - 3, 0):c + 3] = True
    edge[max(c - 3, 0):c + 3, :] = True
bg = np.where(edge, np.maximum(bg, light), bg)
alpha = np.clip((img - bg) / (255.0 - bg), 0, 1)
alpha = np.where(alpha < 0.05, 0, alpha)

print("coverage", alpha.mean())

# the flakes, one by one
lab, n = ndimage.label(alpha > 0.12)
objs = ndimage.find_objects(lab)
crystals, dots = [], []
for idx, sl in enumerate(objs):
    if sl is None:
        continue
    h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
    size = max(h, w)
    mask = lab[sl] == idx + 1
    fill = mask.sum() / float(h * w)
    if 70 <= size <= 260 and 0.08 < fill < 0.55 and 0.75 < h / w < 1.33:
        crystals.append((size, sl, fill))
    elif 14 <= size <= 50 and fill > 0.6 and 0.8 < h / w < 1.25:
        dots.append((size, sl))
crystals.sort(key=lambda c: -c[0])
dots.sort(key=lambda d: -d[0])
print(f"{len(crystals)} crystals, {len(dots)} dots")


def cut(sl, pad=0.12):
    y0, y1, x0, x1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
    s = max(y1 - y0, x1 - x0)
    cy, cx = (y0 + y1) // 2, (x0 + x1) // 2
    r = int(s * (0.5 + pad))
    a = np.zeros((2 * r, 2 * r), np.float32)
    ys, xs = max(cy - r, 0), max(cx - r, 0)
    ye, xe = min(cy + r, N), min(cx + r, N)
    a[ys - (cy - r):ye - (cy - r), xs - (cx - r):xe - (cx - r)] = alpha[ys:ye, xs:xe]
    # only this flake: the neighbours' bits cleared by keeping the component nearest the middle
    l2, n2 = ndimage.label(a > 0.08)
    if n2 > 1:
        keep = l2[r, r] if l2[r, r] else np.bincount(l2.ravel())[1:].argmax() + 1
        grown = ndimage.binary_dilation(l2 == keep, iterations=2)
        a = a * grown
    return a


def cell(a, size=128, blur=0.0):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).resize((size, size), Image.LANCZOS)
    if blur > 0:
        im = im.filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(im, np.float32) / 255.0


# pick eight crystals that differ: spread through the sizes, skipping near-duplicates by their fill
def symmetry(sl):
    """How six-fold it is (a snowflake is): the cut against itself turned 60 degrees. Dots, lines and two flakes
    overlapping score low."""
    a = cut(sl, pad=0.05)
    a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((96, 96), Image.LANCZOS), np.float32) / 255.0
    b = ndimage.rotate(a, 60, reshape=False, order=1)
    num = (a * b).sum()
    den = np.sqrt((a * a).sum() * (b * b).sum()) + 1e-6
    # (a disc is symmetric too: round a circle through a crystal's arms there are gaps between them)
    th = np.linspace(0, 2 * np.pi, 180, endpoint=False)
    ring = a[(48 + np.sin(th) * 30).astype(int), (48 + np.cos(th) * 30).astype(int)]
    if (ring > 0.2).mean() > 0.6:
        return 0.0
    # (nor anything with a solid lump in it, a dot a flake fell across: thin arms don't survive an erosion, a lump does)
    solid = ndimage.binary_erosion(a > 0.5, iterations=4).sum()
    if solid > 0.06 * max((a > 0.2).sum(), 1):
        return 0.0
    return num / den


scored = sorted(((symmetry(sl), size, sl, fill) for size, sl, fill in crystals), key=lambda t: -t[0])
print("symmetry", [round(t[0], 2) for t in scored[:12]])
picked = []
for sym, size, sl, fill in scored:
    if sym < 0.55:
        break
    if all(abs(fill - f) > 0.01 or abs(size - s) > 12 for s, _, f in picked):
        picked.append((size, sl, fill))
    if len(picked) == 8:
        break
while len(picked) < 8 and crystals:
    picked.append(crystals[len(picked) % len(crystals)])

cells = []
for k, (size, sl, fill) in enumerate(picked):
    a = cut(sl)
    # variation, so no two read as the same stamp: a mirror, a turn, a little thinning or thickening of the arms
    if k % 2:
        a = a[:, ::-1]
    a = np.rot90(a, k % 4)
    if k % 3 == 1:
        a = ndimage.grey_erosion(a, size=(2, 2))
    elif k % 3 == 2:
        a = ndimage.grey_dilation(a, size=(2, 2))
    cells.append(cell(a, 128, 0.4))
for k in range(4):
    a = cut(picked[(k * 3 + 1) % len(picked)][1])
    a = np.rot90(a[::-1, :], k)
    cells.append(cell(a, 128, 3.2) * 0.9)                 # defocused
for k in range(2):
    sl = dots[k * 3 % max(len(dots), 1)][1] if dots else None
    if sl is not None:
        a = cut(sl, pad=0.9)
    else:
        yy, xx = np.mgrid[-64:64, -64:64] / 64.0
        a = np.clip(1.2 - np.hypot(yy, xx) * 2.2, 0, 1)
    # a wet clump: soft, a little lumpy
    lump = ndimage.gaussian_filter(rng.random(a.shape).astype(np.float32), a.shape[0] / 18.0)
    lump = (lump - lump.min()) / (lump.max() - lump.min() + 1e-6)
    cells.append(cell(a * (0.75 + 0.5 * lump), 128, 2.5))
yy, xx = np.mgrid[-64:64, -64:64] / 64.0
cells.append(np.clip(1.0 - np.hypot(yy, xx) * 2.6, 0, 1) ** 1.5)          # a soft speck
cells.append(np.clip(1.0 - np.hypot(yy, xx) * 4.5, 0, 1) ** 1.3)          # a tiny one

atlas = np.zeros((512, 512), np.float32)
for i, c in enumerate(cells[:16]):
    y, x = (i // 4) * 128, (i % 4) * 128
    atlas[y:y + 128, x:x + 128] = c
rgba = np.dstack([np.ones_like(atlas), np.ones_like(atlas), np.ones_like(atlas), atlas])
Image.fromarray((rgba * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "snow_atlas.png"))

# the curtain: the flakes shrunk to a few pixels, scattered (tileable: each stamped wrapping round the edges)
C = 512
cur = np.zeros((C, C), np.float32)
small = [Image.fromarray((np.clip(c, 0, 1) * 255).astype(np.uint8)) for c in cells]
for k in range(900):
    src = small[rng.integers(0, 16)]
    s = int(rng.choice([3, 4, 5, 6, 8, 10, 13], p=[0.22, 0.2, 0.18, 0.15, 0.12, 0.08, 0.05]))
    st = np.asarray(src.resize((s, s), Image.LANCZOS), np.float32) / 255.0 * rng.uniform(0.45, 1.0)
    y, x = rng.integers(0, C), rng.integers(0, C)
    ys = (np.arange(s) + y) % C
    xs = (np.arange(s) + x) % C
    cur[np.ix_(ys, xs)] = np.maximum(cur[np.ix_(ys, xs)], st)
rgba = np.dstack([np.ones_like(cur), np.ones_like(cur), np.ones_like(cur), cur])
Image.fromarray((rgba * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "snow_curtain.png"))
print("wrote", OUT)
