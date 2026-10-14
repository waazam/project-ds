"""The creatures' skin detail (2026-10-13i, the owner: "the monster models look too smooth, add more detail").

Two tiling 1024 px maps, made from nothing but noise (our own; no third-party source), laid over every creature's hide
by assets/shaders/creature_detail.gdshaderinc, projected three ways in each body's rest pose so they ride the flesh:

  assets/textures/creatures/flesh_detail_normal.png   OpenGL normal map (+Y up): the hide's relief
  assets/textures/creatures/flesh_detail_mask.png     R: occlusion (1 open .. 0 deep in a crease)
                                                      G: veins (raised, dark under the skin)
                                                      B: wetness (pores and slick patches: low roughness)

What's in it, from coarse to fine: loose folds and sag, crossing wrinkles in patches (each patch its own direction),
the skin's cells cracked like old leather in its dry patches, raised branching veins, a few long welted scars, and
pores. Every field is made in the frequency domain (white noise through a band filter), so it tiles exactly.

    <python with numpy> tools/Textures/make_creature_detail.py
(Blender's own Python has numpy: ~/tools/blender-4.0.2-windows-x64/4.0/python/bin/python.exe)
"""
import os, struct, zlib
import numpy as np

N = 1024
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(ROOT, "assets", "textures", "creatures")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1313)

fy = np.fft.fftfreq(N)[:, None] * N
fx = np.fft.fftfreq(N)[None, :] * N
FR = np.sqrt(fx * fx + fy * fy)


def norm(a):
    a = a - a.min()
    return a / max(a.max(), 1e-9)


def band(lo, hi, aniso=1.0, angle=0.0):
    """Tileable noise with its frequencies between lo and hi cycles per tile; aniso > 1 stretches it across angle."""
    w = rng.standard_normal((N, N))
    c, s = np.cos(angle), np.sin(angle)
    u = fx * c + fy * s
    v = -fx * s + fy * c
    r = np.sqrt(u * u * aniso + v * v / aniso)
    f = np.exp(-((np.log(np.maximum(r, 1e-6)) - np.log(np.sqrt(lo * hi))) ** 2) / (2 * (np.log(hi / lo) / 2.5) ** 2))
    f[0, 0] = 0
    a = np.real(np.fft.ifft2(np.fft.fft2(w) * f))
    return a / (a.std() + 1e-9)


def blur(a, sigma):
    """Gaussian blur of a (sigma in pixels), wrapping."""
    return np.real(np.fft.ifft2(np.fft.fft2(a) * np.exp(-2 * (np.pi * sigma) ** 2 * ((fx / N) ** 2 + (fy / N) ** 2))))


def warp(a, dx, dy):
    """Samples a at (x + dx, y + dy), wrapping (nearest pixel: the fields are smooth at this size)."""
    yy, xx = np.mgrid[0:N, 0:N]
    return a[(yy + dy).astype(int) % N, (xx + dx).astype(int) % N]


def cells(count, jitter_seed):
    """Tileable Worley: F2 - F1 (0 on the cracks between cells)."""
    pts = rng.random((count, 2)) * N
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    f1 = np.full((N, N), 1e9, np.float32)
    f2 = np.full((N, N), 1e9, np.float32)
    for px, py in pts:
        dx = np.abs(xx - px)
        dx = np.minimum(dx, N - dx)
        dy = np.abs(yy - py)
        dy = np.minimum(dy, N - dy)
        d = np.sqrt(dx * dx + dy * dy)
        nf2 = np.where(d < f1, f1, np.minimum(f2, d))
        f1 = np.minimum(f1, d)
        f2 = nf2
    return f2 - f1


print("[creature-detail] fields")
# loose sag and folds (the coarsest: the skin hanging off the bone)
sag = band(2, 6) * 0.6 + band(5, 12, aniso=3.0, angle=0.3) * 0.4
# wrinkles: thin creases, crossing in patches, each patch its own direction
patch = norm(blur(band(1.5, 4), 8))
wr = np.zeros((N, N))
for k, ang in enumerate((0.15, 1.2, 2.3)):
    w = band(28, 60, aniso=9.0, angle=ang)
    wx, wy = band(3, 8) * 9, band(3, 8) * 9
    w = warp(w, wx, wy)
    crease = np.exp(-(w ** 2) * 6.0)      # the line where the field crosses zero: a fold's crease
    weight = np.clip(1.0 - np.abs(patch - (k / 2.0)) * 2.2, 0, 1)
    wr += crease * weight
wr = np.clip(wr, 0, 1)
# the cracked, dry patches: cells of hide with cracks between
print("[creature-detail] cells")
cr = cells(900, 1)
crack = np.exp(-cr / 2.2)
dry = np.clip((norm(blur(band(1.5, 5), 6)) - 0.45) * 3.0, 0, 1)
fine = cells(2600, 2)
fcrack = np.exp(-fine / 1.4) * 0.5
# veins: ridged noise, thin, branching (warped), raised
vn = band(6, 16)
vn = warp(vn, band(4, 10) * 14, band(4, 10) * 14)
veins = np.exp(-(vn ** 2) * 18.0) * np.clip(norm(blur(band(1.5, 4), 4)) * 1.6 - 0.3, 0, 1)
veins = np.clip(veins + 0.6 * np.exp(-(warp(band(14, 30), band(5, 10) * 6, band(5, 10) * 6) ** 2) * 30.0) * veins.mean() * 3, 0, 1)
# scars: a few long welts, ragged
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
scar = np.zeros((N, N))
for _ in range(5):
    cx, cy = rng.random(2) * N
    ang = rng.random() * np.pi
    ln, wd = rng.uniform(90, 260), rng.uniform(3.0, 6.0)
    dx = xx - cx
    dx = (dx + N / 2) % N - N / 2
    dy = yy - cy
    dy = (dy + N / 2) % N - N / 2
    u = dx * np.cos(ang) + dy * np.sin(ang)
    v = -dx * np.sin(ang) + dy * np.cos(ang) + band(8, 20) * 1.5
    s = np.exp(-(v / wd) ** 2) * np.clip(1 - (u / ln) ** 2, 0, 1)
    scar = np.maximum(scar, s)
# pores: small dimples scattered
pores = np.zeros((N, N))
idx = rng.integers(0, N, (9000, 2))
pores[idx[:, 0], idx[:, 1]] = 1.0
pores = blur(pores, 1.1)
pores = pores / pores.max()
grain = band(90, 200) * 0.5

print("[creature-detail] height")
H = (sag * 0.55
     - wr * 0.9
     - crack * dry * 1.1 - fcrack * (0.4 + 0.6 * dry)
     + veins * 0.75
     + scar * 0.9 - np.exp(-((scar - 0.55) ** 2) * 40) * 0.25
     - pores * 0.8
     + grain * 0.12)
# the normal: the height's slope (wrapping), strong enough to read at a glance under a lantern
gx = (np.roll(H, -1, 1) - np.roll(H, 1, 1)) * 0.5
gy = (np.roll(H, -1, 0) - np.roll(H, 1, 0)) * 0.5
k = 2.2
nx, ny, nz = -gx * k, gy * k, np.ones_like(H)   # +Y up (OpenGL): image rows run down
ln = np.sqrt(nx * nx + ny * ny + nz * nz)
normal = np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5

# occlusion: how far below its surroundings a point lies (creases, cracks, pores), at two reaches
occ = 1.0 - np.clip((blur(H, 3) - H) * 0.9 + (blur(H, 12) - H) * 0.35, 0, 1)
occ = np.clip(occ, 0.15, 1.0)
wet = np.clip(norm(blur(band(2, 6), 3)) * 1.4 - 0.45, 0, 1) * 0.7 + pores * 0.3
mask = np.stack([occ, np.clip(veins, 0, 1), np.clip(wet, 0, 1)], -1)


def write_png(path, rgb):
    data = (np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8)
    h, w, _ = data.shape
    raw = b"".join(b"\x00" + data[y].tobytes() for y in range(h))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


write_png(os.path.join(OUT, "flesh_detail_normal.png"), normal)
write_png(os.path.join(OUT, "flesh_detail_mask.png"), mask)
print(f"[creature-detail] wrote {OUT}")
