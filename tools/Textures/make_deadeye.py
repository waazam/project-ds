"""Act 13's dead eye, painted onto one ball (2026-10-24, the owner: "make the eyeball look way better, you can see it has
like a ring around it ... the ring around it looks bad"). It was the lake thing's stock eye: a white ball with an iris
and a pupil stood on its front, and a swollen torus lid round it, which read as a band cut round the ball. Now the whole
eye is one texture on one sphere, so nothing stands proud of it and nothing rings it:

    python tools/Textures/make_deadeye.py

Writes assets/models/items/deadeye/deadeye_albedo.png (equirectangular, 1024 x 512, for Godot's SphereMesh: u round the
Y axis from +Z, so the front, -Z, is the middle of the image; v from the top) and deadeye_rough.png (wet where it's
glossy, dull where it's dried). The eye: a yellowed white going pink and raw toward the back where it was torn out,
veins branching forward from there and thinning out before the iris; the iris a dark amber-red, fibrous, its edge soft
(no hard limbal line), bloodshot round it; a long slit pupil gone a little ragged; a milky cataract clouding half of it;
a few dried flecks. Seeded: the same every run.
"""
import math
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "models", "items", "deadeye")
os.makedirs(OUT, exist_ok=True)
W, H = 1024, 512
rng = np.random.default_rng(1313)


def noise(h, w, scale, octaves=4, seed=0):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        s = max(2, int(scale * 2 ** o))
        g = r.random((max(2, int(s * h / w)) + 2, s + 2))
        g[:, -1] = g[:, 0]   # (wraps round)
        out += np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)) / 255.0 * amp
        total += amp
        amp *= 0.5
    return out / total


# every pixel's direction on the ball, and its angle from the front (-Z)
yy, xx = np.mgrid[0:H, 0:W].astype(float)
u = (xx + 0.5) / W
v = (yy + 0.5) / H
lat = (0.5 - v) * math.pi                     # +up
lon = u * 2 * math.pi
dx = np.cos(lat) * np.sin(lon)
dy = np.sin(lat)
dz = np.cos(lat) * np.cos(lon)
front = -dz                                   # 1 at the front, -1 at the back
ang = np.arccos(np.clip(front, -1, 1))        # radians from the front
# the front's own frame (looking at it): x right, y up, for the iris and the pupil
fx, fy = -dx, dy                              # (seen from the front, -X is on the viewer's right)
phi = np.arctan2(fy, fx)

# --- the white: yellowed, mottled, pink and raw toward the back
n1 = noise(H, W, 6, 4, 1)
white = np.array([0.86, 0.8, 0.68])
img = white * (0.9 + 0.12 * n1)[..., None]
back = np.clip((ang - 1.6) / 1.2, 0, 1) ** 1.3
raw = np.array([0.55, 0.2, 0.18]) * (0.8 + 0.3 * noise(H, W, 14, 3, 2))[..., None]
img = img * (1 - back[..., None]) + raw * back[..., None]
yellow = np.clip((ang - 0.6) / 1.4, 0, 1) * 0.25
img = img * (1 - yellow[..., None]) + np.array([0.78, 0.64, 0.42]) * yellow[..., None]

# --- veins: branching from the back toward the front, thinning; drawn in angle space (projected to the image)
veins = Image.new("L", (W, H), 0)
vd = ImageDraw.Draw(veins)


def to_px(a, p):
    """A point a radians from the front at angle p round it, to image pixels (and its 3D for wrap checks)."""
    x = math.sin(a) * math.cos(p)
    y = math.sin(a) * math.sin(p)
    z = -math.cos(a)
    # back to the image: fx = -dx, fy = dy
    ddx, ddy, ddz = -x, y, z
    la = math.asin(max(-1, min(1, ddy)))
    lo = math.atan2(ddx, ddz) % (2 * math.pi)
    return lo / (2 * math.pi) * W, (0.5 - la / math.pi) * H


def vein(a, p, width, depth):
    pts = [to_px(a, p)]
    da = -rng.uniform(0.02, 0.03)
    dp = 0.0
    stop = 0.75 + rng.uniform(0, 0.3)
    while a > stop and width > 0.5:
        a += da
        dp += rng.normal(0, 0.006)
        dp *= 0.85
        p += dp
        nx, ny = to_px(a, p)
        if abs(nx - pts[-1][0]) > W / 2:      # (crossed the seam: start a fresh line)
            vd.line(pts, fill=int(min(255, 90 + width * 50)), width=max(1, int(width)))
            pts = []
        pts.append((nx, ny))
        width *= 0.975
        if depth < 2 and rng.random() < 0.04:
            vein(a, p + rng.choice([-1, 1]) * rng.uniform(0.08, 0.2), width * 0.65, depth + 1)
    if len(pts) > 1:
        vd.line(pts, fill=int(min(255, 90 + width * 50)), width=max(1, int(width)))


for k in range(16):
    vein(rng.uniform(2.0, 2.5), (k + rng.uniform(-0.3, 0.3)) / 16 * 2 * math.pi, rng.uniform(2.0, 3.2), 0)
vm = np.asarray(veins.filter(ImageFilter.GaussianBlur(0.8))) / 255.0
vm = np.clip(vm * 1.6, 0, 1)
img = img * (1 - 0.85 * vm[..., None]) + np.array([0.55, 0.05, 0.04]) * 0.85 * vm[..., None]

# --- bloodshot: a red flush round the iris, in blotches
flush = np.clip(1 - np.abs(ang - 0.62) / 0.3, 0, 1) * np.clip((noise(H, W, 20, 3, 3) - 0.35) * 2, 0, 1)
img = img * (1 - 0.45 * flush[..., None]) + np.array([0.6, 0.12, 0.1]) * 0.45 * flush[..., None]

# --- the iris: dark amber-red, fibres running out from the pupil, its edge soft
IR = 0.5                                      # its angular radius
fib = noise(H, W, 6, 2, 4)                    # (low-frequency base)
spokes = 0.5 + 0.5 * np.sin(phi * 70 + 3 * noise(H, W, 30, 2, 5) * 6)
spokes2 = 0.5 + 0.5 * np.sin(phi * 23 + 2.0 * noise(H, W, 12, 2, 6) * 6)
rad = np.clip(ang / IR, 0, 1.5)
iris = np.array([0.42, 0.16, 0.06]) * (0.6 + 0.35 * spokes[..., None] * 0.6 + 0.25 * spokes2[..., None]) * (0.8 + 0.3 * fib[..., None])
iris = iris * (1.15 - 0.45 * rad[..., None])                  # (darker toward its rim, lighter round the pupil)
iris = iris + np.array([0.3, 0.2, 0.05]) * np.clip(1 - rad / 0.45, 0, 1)[..., None] * 0.5   # (a gold collar round the pupil)
edge = np.clip((IR - ang) / 0.06, 0, 1)                         # (soft: no hard ring)
img = img * (1 - edge[..., None]) + iris * edge[..., None]
# a faint darkening just inside the edge (the iris's own rim, blurred, not a line)
rim = np.clip(1 - np.abs(ang - IR * 0.92) / 0.08, 0, 1) * edge * 0.25
img = img * (1 - rim[..., None])

# --- the pupil: a long slit, a little ragged
sx = fx / np.maximum(np.sin(ang), 1e-6) * ang       # (the front's plane, in angle)
sy = fy / np.maximum(np.sin(ang), 1e-6) * ang
wob = (noise(H, W, 40, 2, 7) - 0.5) * 0.02
slit = ((sx + wob) / 0.075) ** 2 + (sy / 0.33) ** 2
pupil = np.clip((1 - slit) / 0.25, 0, 1) * (ang < 0.6)
img = img * (1 - pupil[..., None]) + np.array([0.02, 0.01, 0.01]) * pupil[..., None]

# --- the cataract: a milky cloud over part of the front, thickest off-centre
cx, cy = 0.12, 0.08
cd = np.sqrt((sx - cx) ** 2 + (sy - cy) ** 2) * (ang < 1.2)
cloud = np.clip(1 - cd / 0.42, 0, 1) ** 1.2 * (0.55 + 0.45 * noise(H, W, 18, 3, 8)) * (ang < 1.2)
milk = np.array([0.72, 0.74, 0.72])
cloud = cloud / max(cloud.max(), 1e-6) * 0.7
img = img * (1 - cloud[..., None]) + milk * cloud[..., None]

# --- dried flecks, a smear of grime
fleck = (noise(H, W, 90, 2, 9) > 0.86) & (ang > 2.0)
img = np.where(fleck[..., None], img * 0.75 + np.array([0.35, 0.08, 0.06]) * 0.25, img)

Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "deadeye_albedo.png"))
# roughness: glossy and wet over the front, duller where it's dried and toward the raw back
rough = 0.18 + 0.25 * np.clip((ang - 0.8) / 1.6, 0, 1) + 0.2 * fleck + 0.1 * noise(H, W, 20, 2, 10)
Image.fromarray((np.clip(rough, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "deadeye_rough.png"))
print(f"[deadeye] {OUT}  (cloud max {cloud.max():.2f}, at the front {cloud[H // 2, W // 2]:.2f})")
