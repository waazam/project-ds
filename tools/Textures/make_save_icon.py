"""The save indicator's film canister, painted (2026-10-24, the owner: "make the saving icon ui in the top right look way
better, lets make the design look weathered, old and faded and sort of cracked inside the green", with reference photos
of old film canisters, tins and painted signs).

    python tools/Textures/make_save_icon.py

Writes assets/ui/save_canister.png (RGBA, 160 x 232): a 35mm canister seen side on, the light trap to its right (the
leader is drawn by SaveIndicator.cs, winding in). Brushed tin gone dull and tarnished, rust freckling its rims, black
caps worn back to metal at their edges, and the label: a film stock's green, faded paler toward the top where the sun
had it, its paint crazed all over with fine cracks (the tin showing pale through them) and flaked away in patches, its
print (a made-up stock: no maker's name) worn half off. Seeded: the same every run.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "ui", "save_canister.png")
FONT = os.path.join(ROOT, "assets", "fonts", "Anton-Regular.ttf")
W, H = 160, 232
rng = np.random.default_rng(2410)


def noise(h, w, scale, octaves=4, seed=0):
    """Smooth value noise in 0..1 (summed octaves of upscaled random grids)."""
    r = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        s = max(2, int(scale * 2 ** o))
        g = r.random((int(h / max(1, H) * s) + 2, s + 2))
        img = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        out += np.asarray(img) / 255.0 * amp
        total += amp
        amp *= 0.5
    return out / total


def cracks(h, w, cells, seed):
    """A craquelure: distance to the nearest Voronoi edge (small along the crack lines), per pixel."""
    r = np.random.default_rng(seed)
    pts = np.stack([r.random(cells) * w, r.random(cells) * h], 1)
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((xx[..., None] - pts[:, 0]) ** 2 + (yy[..., None] - pts[:, 1]) ** 2)
    d.sort(axis=2)
    return d[..., 1] - d[..., 0]


yy, xx = np.mgrid[0:H, 0:W].astype(float)
img = np.zeros((H, W, 4))

# the cylinder's frame: body x 14..122 (the light trap 122..134), lips top and bottom, the knob above
bx0, bx1 = 14, 122
u = np.clip((xx - bx0) / (bx1 - bx0), 0, 1)
shade = 0.42 + 0.58 * np.sin(u * np.pi) ** 0.8
hl = np.exp(-((u - 0.3) / 0.07) ** 2) * 0.45 + np.exp(-((u - 0.82) / 0.05) ** 2) * 0.12


def fill(mask, rgb, a=1.0):
    for k in range(3):
        img[..., k] = np.where(mask, rgb[..., k] if rgb.ndim == 3 else rgb[k], img[..., k])
    img[..., 3] = np.where(mask, a, img[..., 3])


# --- the tin under everything: dull, brushed, tarnished brown-grey in clouds, rust freckles
tarnish = noise(H, W, 5, 4, 11)
brush = noise(H, W, 40, 2, 12) * 0.12
metal = np.stack([0.62 - 0.22 * tarnish, 0.6 - 0.25 * tarnish, 0.56 - 0.3 * tarnish], -1) + brush[..., None]
metal = metal * shade[..., None] + hl[..., None]
freckle = (noise(H, W, 30, 3, 13) > 0.68) * (noise(H, W, 8, 2, 14) > 0.5)
rust = np.array([0.42, 0.2, 0.08])
metal = np.where(freckle[..., None], metal * 0.4 + rust * shade[..., None] * 0.8, metal)

body = (xx >= bx0) & (xx < bx1) & (yy >= 30) & (yy < 212)
fill(body, metal)

# --- the caps: black, round-shouldered, worn through to the tin along their rims, a dent
for (y0, y1) in ((22, 34), (208, 220)):
    cap = (xx >= bx0 - 4) & (xx < bx1 + 4) & (yy >= y0) & (yy < y1)
    cu = np.clip((xx - (bx0 - 4)) / (bx1 - bx0 + 8), 0, 1)
    cs = 0.35 + 0.65 * np.sin(cu * np.pi)
    black = np.stack([0.08 * cs + 0.05 * hl, 0.08 * cs + 0.05 * hl, 0.085 * cs + 0.05 * hl], -1)
    edge = np.minimum(np.abs(yy - y0), np.abs(yy - (y1 - 1)))
    worn = (edge < 2.2) & (noise(H, W, 18, 3, y0) > 0.45)
    capc = np.where(worn[..., None], metal * 0.9, black)
    fill(cap, capc)
# the knob and its spindle
for (x0, x1, y0, y1, g) in ((48, 84, 8, 22, 0.16), (56, 76, 2, 10, 0.2)):
    m = (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)
    ku = np.clip((xx - x0) / (x1 - x0), 0, 1)
    ks = 0.4 + 0.6 * np.sin(ku * np.pi)
    kc = np.stack([g * ks, g * ks, g * 1.05 * ks], -1) + (noise(H, W, 20, 2, x0) * 0.05)[..., None]
    fill(m, kc)

# --- the label: green, faded, crazed, flaked
ly0, ly1 = 38, 204
lab = (xx >= bx0) & (xx < bx1) & (yy >= ly0) & (yy < ly1)
lv = (yy - ly0) / (ly1 - ly0)
green = np.array([0.16, 0.46, 0.22])
faded = np.array([0.5, 0.62, 0.48])
mottle = noise(H, W, 6, 4, 21)
sun = np.clip(0.55 - lv * 0.6 + (mottle - 0.5) * 0.5, 0, 1)            # paler toward the top, in clouds
paint = green * (1 - sun[..., None]) + faded * sun[..., None]
grime = np.clip((lv - 0.65) * 1.6, 0, 1) * (0.5 + 0.5 * noise(H, W, 10, 3, 22))
paint = paint * (1 - 0.45 * grime[..., None]) + np.array([0.18, 0.14, 0.08]) * 0.45 * grime[..., None]
paint = paint * (0.86 + 0.28 * noise(H, W, 26, 2, 23))[..., None]
paint = paint * shade[..., None] + hl[..., None] * 0.6
# the craquelure: fine dark hairlines (dirt in the cracks), crazed in patches where the paint has dried out most, the tin
# pale in only the widest of them
cr = cracks(H, W, 420, 31)
crazed = noise(H, W, 5, 3, 32) > 0.42
crack_line = (cr < 0.7) & crazed
crack_edge = (cr >= 0.7) & (cr < 1.4) & crazed
under = np.array([0.78, 0.76, 0.66]) * shade[..., None] * 0.95
paint = np.where(crack_edge[..., None], paint * 0.78, paint)
paint = np.where(crack_line[..., None], paint * 0.35 + np.array([0.06, 0.05, 0.03]), paint)
open_crack = crack_line & (noise(H, W, 24, 2, 33) > 0.72)
paint = np.where(open_crack[..., None], under * 0.85, paint)
# flakes: whole patches of paint gone (bigger toward the edges and the bottom, where it was handled)
flake_n = noise(H, W, 9, 4, 41)
handled = np.clip(np.abs(u - 0.5) * 1.2 + lv * 0.35, 0, 1)
flake_n = flake_n * 0.7 + noise(H, W, 30, 2, 42) * 0.3
flaked = flake_n + handled * 0.22 > 0.8
flake_rim = (flake_n + handled * 0.22 > 0.775) & ~flaked
paint = np.where(flake_rim[..., None], paint * 0.55, paint)
paint = np.where(flaked[..., None], metal * 0.95, paint)
# scratches across it
scr = Image.new("L", (W, H), 0)
sd = ImageDraw.Draw(scr)
for _ in range(14):
    x = rng.uniform(bx0, bx1)
    y = rng.uniform(ly0, ly1)
    a = rng.uniform(-0.5, 0.5)
    L = rng.uniform(8, 34)
    sd.line([(x, y), (x + L * np.cos(a), y + L * np.sin(a))], fill=int(rng.uniform(90, 200)), width=1)
scr = np.asarray(scr) / 255.0
paint = paint * (1 - scr[..., None] * 0.6) + under * scr[..., None] * 0.6
fill(lab, paint)

# --- the print: a made-up stock's name up the label, cream on the green, worn half off with the paint
txt = Image.new("L", (ly1 - ly0, bx1 - bx0), 0)
td = ImageDraw.Draw(txt)
try:
    big = ImageFont.truetype(FONT, 40)
    small = ImageFont.truetype(FONT, 15)
except OSError:
    big = small = ImageFont.load_default()
td.text((8, 14), "SILVER 400", font=big, fill=255)
td.text((10, 66), "PANCHROMATIC 135", font=small, fill=215)
td.text((10, 84), "ASA 400 · 36 EXP", font=small, fill=190)
txt = np.asarray(txt.rotate(90, expand=True)) / 255.0          # (reads bottom to top, as the references do)
pm = np.zeros((H, W))
th, tw = txt.shape
pm[ly0:ly0 + th, bx0:bx0 + tw] = txt[:min(th, ly1 - ly0), :min(tw, bx1 - bx0)]
wear = noise(H, W, 14, 3, 51)
pm = pm * np.clip((wear - 0.18) * 2.6, 0, 1) * ~flaked * ~crack_line
cream = np.array([0.9, 0.82, 0.55]) * shade[..., None] + hl[..., None] * 0.4
for k in range(3):
    img[..., k] = img[..., k] * (1 - pm) + cream[..., k] * pm
# a thin red band at the label's foot (a stock's stripe), worn too
band = lab & (yy >= 186) & (yy < 192) & (noise(H, W, 20, 2, 61) > 0.35) & ~flaked
fill(band, np.array([0.55, 0.16, 0.1]) * shade[..., None] * (1 - 0.3 * grime[..., None]))

# --- the light trap down the right: black felt, frayed
trap = (xx >= bx1) & (xx < bx1 + 12) & (yy >= 36) & (yy < 206)
felt = 0.05 + 0.04 * noise(H, W, 30, 2, 71)
fill(trap, np.stack([felt, felt * 0.95, felt * 0.9], -1))

# soften the hard pixel edges a hair (it's shown small), and write
out = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
alpha = out.getchannel("A").filter(ImageFilter.GaussianBlur(0.6))
out.putalpha(alpha)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
out.save(OUT)
print(f"[save-icon] {OUT}")
