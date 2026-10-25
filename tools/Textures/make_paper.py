"""The paper the notes are read on (2026-10-24, the owner: "retexture the paper for all the notes to make it look more
rustic and textured literally", with photos of a ruled notebook page, a diary, a sketchbook, an ink-splashed stained page).

    python tools/Textures/make_paper.py

Writes assets/ui/paper/<name>.png (RGBA), each drawn at the note card's own pixel scale (the UI is 640x360) and laid
on the card as a nine-patch (NoteOverlay.cs; its edges, EDGE px, kept, the middle stretched). The ruled lines, the
margin and the punched holes are drawn over it by the card itself, so they line up with the text.

    note    a page torn from a notebook: cream, foxed, a coffee ring, a fold across, the top edge ripped
    rough   a sheet that's been through it: tea-stained, a water mark, creased twice, the edges deckled and scorched
    typed   thin typing paper gone yellow: a fold across the middle, foxing, rust from a paper clip
    card    printed card stock: cleaner, a little yellowed at the edges, a thumb's grime at one corner

Seeded: the same every run.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "ui", "paper")
os.makedirs(OUT, exist_ok=True)
W, H = 360, 480
EDGE = 14


def noise(h, w, scale, octaves=4, seed=0):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        s = max(2, int(scale * 2 ** o))
        g = r.random((max(2, int(s * h / w)) + 2, s + 2))
        out += np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)) / 255.0 * amp
        total += amp
        amp *= 0.5
    return out / total


yy, xx = np.mgrid[0:H, 0:W].astype(float)


def base(colour, seed, mottle=0.12):
    """Paper: its colour, fibres (fine streaks both ways), a soft mottling, a grain you can see."""
    c = np.array(colour)
    fib = noise(H, W * 4, 60, 2, seed)[:, ::4] * 0.5 + noise(H * 4, W, 60, 2, seed + 1)[::4] * 0.5
    grain = np.random.default_rng(seed + 2).random((H, W))
    m = noise(H, W, 4, 4, seed + 3)
    img = c * (1 - mottle * (m - 0.5) * 2)[..., None]
    img = img * (0.94 + 0.08 * fib)[..., None] * (0.97 + 0.05 * grain)[..., None]
    return img


def relief(img, amount, seed, crumple=1.0):
    """The paper's tooth and its crumpling, lit from the top left: a height field (fine tooth, a few broad crumples, the
    sharp facets of having been balled up once) turned to shading."""
    tooth = np.random.default_rng(seed).random((H, W))
    tooth = np.asarray(Image.fromarray((tooth * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7))) / 255.0
    broad = noise(H, W, 3, 3, seed + 1)
    # facets: ridged noise (sharp creases where it was crushed)
    facets = 1 - np.abs(noise(H, W, 7, 3, seed + 2) - 0.5) * 2
    facets = facets ** 3
    hgt = tooth * 0.6 + broad * 18 * crumple + facets * 7 * crumple
    gy, gx = np.gradient(hgt)
    shade = -(gx * 0.7 + gy * 0.7)
    return img * (1 + amount * shade)[..., None]


def stain(img, amount, seed, colour=(0.55, 0.38, 0.18), scale=3):
    """Tea-brown blooms, darker at their tide lines."""
    n = noise(H, W, scale, 4, seed)
    a = np.clip((n - (1 - amount)) * 4, 0, 1)
    tide = np.clip(1 - np.abs(n - (1 - amount) - 0.03) * 60, 0, 1) * (a > 0)
    a = a * 0.35 + tide * 0.25
    return img * (1 - a[..., None]) + np.array(colour) * a[..., None] * img.mean(-1, keepdims=True) * 1.2


def foxing(img, n, seed):
    """Foxing: little rust-brown spots, in clusters."""
    r = np.random.default_rng(seed)
    out = img.copy()
    for _ in range(n):
        cx, cy = r.uniform(0, W), r.uniform(0, H)
        for _ in range(r.integers(1, 7)):
            x, y = cx + r.normal(0, 10), cy + r.normal(0, 10)
            rad = r.uniform(0.6, 2.6)
            d = np.sqrt((xx - x) ** 2 + (yy - y) ** 2)
            a = np.clip(1 - d / rad, 0, 1) ** 0.7 * r.uniform(0.25, 0.6)
            out = out * (1 - a[..., None]) + np.array([0.5, 0.32, 0.16]) * a[..., None]
    return out


def ring(img, cx, cy, rad, seed, strength=0.35):
    """A mug's ring: a broken brown circle, darker at its outer lip, a faint fill inside."""
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    brk = noise(H, W, 10, 2, seed) > 0.32
    lip = np.clip(1 - np.abs(d - rad) / 2.2, 0, 1) * brk
    fill = (d < rad) * 0.12 * noise(H, W, 8, 3, seed + 1)
    a = (lip * strength + fill * strength)
    return img * (1 - a[..., None]) + np.array([0.42, 0.26, 0.12]) * a[..., None]


def crease(img, horizontal, at, seed, depth=0.1):
    """A fold: a pale ridge, its shadow on one side, grime caught along it, wandering a little."""
    off = (noise(1, W if horizontal else H, 6, 2, seed)[0] - 0.5) * 6
    if horizontal:
        d = yy - (at + off[None, :].repeat(H, 0))
    else:
        d = xx - (at + off[:, None].repeat(W, 1))
    light = np.clip(1 - np.abs(d + 0.8) / 1.2, 0, 1) * 0.6
    dark = np.clip(1 - np.abs(d - 1.2) / 1.6, 0, 1)
    grime = np.clip(1 - np.abs(d) / 7, 0, 1) * noise(H, W, 20, 2, seed + 1) * 0.5
    img = img * (1 - depth * 2 * dark[..., None]) * (1 - 0.18 * grime[..., None])
    return img + light[..., None] * depth


def edges(seed, ragged=1.0, torn_top=False, burnt=False):
    """The sheet's outline as alpha, and how far in from it each pixel is (for the darkened, handled edge)."""
    jag = (noise(H, W, 30, 3, seed) - 0.5) * 4 * ragged + (noise(H, W, 6, 2, seed + 1) - 0.5) * 5 * ragged
    inset = np.minimum(np.minimum(xx, W - 1 - xx), np.minimum(yy, H - 1 - yy)) - 3 + jag
    if torn_top:
        # ripped from the pad: the top edge bitten deep, in little teeth
        teeth = (noise(1, W, 60, 3, seed + 2)[0] - 0.5) * 16 + (noise(1, W, 6, 2, seed + 3)[0] - 0.5) * 14
        inset = np.minimum(inset, yy - 14 - teeth[None, :].repeat(H, 0))
    alpha = np.clip(inset + 0.5, 0, 1)
    return alpha, inset


def handled(img, inset, amount=0.3, burnt=False):
    """Darker and browner toward the edge (fingers, light, age); scorched black-brown at the very edge if burnt."""
    e = np.clip(1 - inset / 26, 0, 1) ** 1.6
    img = img * (1 - amount * e[..., None]) + np.array([0.45, 0.3, 0.15]) * amount * 0.6 * e[..., None]
    if burnt:
        b = np.clip(1 - inset / 5, 0, 1) ** 1.2
        img = img * (1 - 0.85 * b[..., None]) + np.array([0.18, 0.08, 0.03]) * 0.85 * b[..., None]
    return img


def save(name, img, alpha):
    a = Image.fromarray((np.clip(alpha, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.4))
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
    im.putalpha(a)
    im.save(os.path.join(OUT, name + ".png"))
    print(f"[paper] {name}")


def note():
    img = base((0.86, 0.82, 0.7), 11)
    img = stain(img, 0.22, 12, scale=3)
    img = foxing(img, 18, 13)
    img = ring(img, W * 0.74, H * 0.8, 46, 14, 0.3)
    img = crease(img, True, H * 0.36, 15, 0.07)
    img = relief(img, 0.08, 17, 0.7)
    alpha, inset = edges(16, 0.6, torn_top=True)
    img = handled(img, inset, 0.28)
    save("note", img, alpha)


def rough():
    img = base((0.8, 0.72, 0.55), 21, mottle=0.2)
    img = stain(img, 0.38, 22, scale=2)
    img = stain(img, 0.2, 23, colour=(0.4, 0.3, 0.2), scale=5)
    img = foxing(img, 40, 24)
    img = ring(img, W * 0.24, H * 0.18, 38, 25, 0.25)
    img = crease(img, True, H * 0.5, 26, 0.09)
    img = crease(img, False, W * 0.52, 27, 0.08)
    img = relief(img, 0.11, 29, 1.2)
    alpha, inset = edges(28, 1.8, burnt=True)
    img = handled(img, inset, 0.4, burnt=True)
    save("rough", img, alpha)


def typed():
    img = base((0.86, 0.83, 0.68), 31, mottle=0.08)
    img = stain(img, 0.12, 32, colour=(0.62, 0.48, 0.22), scale=3)
    img = foxing(img, 10, 33)
    img = crease(img, True, H * 0.5, 34, 0.08)
    # a paper clip's rust, top left
    clip = Image.new("L", (W, H), 0)
    ImageDraw.Draw(clip).rounded_rectangle((26, 2, 40, 46), 6, outline=150, width=2)
    c = np.asarray(clip.filter(ImageFilter.GaussianBlur(1.6))) / 255.0 * noise(H, W, 20, 2, 35)
    img = img * (1 - c[..., None] * 0.8) + np.array([0.5, 0.26, 0.1]) * c[..., None] * 0.8
    img = relief(img, 0.06, 37, 0.5)
    alpha, inset = edges(36, 0.25)
    img = handled(img, inset, 0.22)
    save("typed", img, alpha)


def card():
    img = base((0.88, 0.87, 0.8), 41, mottle=0.06)
    img = stain(img, 0.08, 42, scale=3)
    img = foxing(img, 5, 43)
    thumb = np.clip(1 - np.sqrt((xx - W * 0.9) ** 2 + (yy - H * 0.94) ** 2) / 40, 0, 1) * noise(H, W, 14, 3, 44)
    img = img * (1 - 0.25 * thumb[..., None])
    img = relief(img, 0.05, 46, 0.3)
    alpha, inset = edges(45, 0.1)
    img = handled(img, inset, 0.16)
    save("card", img, alpha)


note()
rough()
typed()
card()
