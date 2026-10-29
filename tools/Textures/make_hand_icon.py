"""The HUD's empty hand (2026-10-29, the playtest: the "EMPTY HAND" icon "looks horrible", it should look like a real
hand). An open right hand, palm out, fingers a little spread, the thumb out to the side: drawn big as shapes (the palm,
four fingers and the thumb, each finger its own capsule so the gaps between them read), then traced as a neon tube
round its silhouette, with the palm's two creases fainter inside, and the whole hand filled very
faintly so it reads as a solid thing at a glance. White on clear: NeonKit tints it the HUD's colour.

    python tools/Textures/make_hand_icon.py

Writes assets/ui/hud/:
    hand.png       the tube and its faint fill (64 x 64: 4x the 16 px it's drawn at)
    hand_glow.png  the tube's soft glow, drawn under it in the HUD colour's glow

Seeded: nothing random; the same every run.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "ui", "hud")
os.makedirs(OUT, exist_ok=True)

SIZE = 64          # 4x the drawn 16 px
BIG = SIZE * 8     # drawn at 8x again and brought down, for clean curves


def P(x, y):
    """-0.5..0.5 across the icon to the big canvas."""
    return ((x + 0.5) * BIG, (y + 0.5) * BIG)


def capsule(d, a, b, r):
    ax, ay = P(*a)
    bx, by = P(*b)
    rr = r * BIG
    d.line([(ax, ay), (bx, by)], fill=255, width=int(rr * 2))
    for x, y in ((ax, ay), (bx, by)):
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=255)


def hand_mask():
    im = Image.new("L", (BIG, BIG), 0)
    d = ImageDraw.Draw(im)
    # the palm: wider at the knuckles, narrowing to the wrist, its heel rounded
    palm = [(-0.17, -0.02), (0.19, -0.04), (0.2, 0.12), (0.15, 0.27), (0.1, 0.4), (-0.1, 0.4), (-0.15, 0.3), (-0.2, 0.14)]
    d.polygon([P(*p) for p in palm], fill=255)
    d.ellipse([*P(-0.2, -0.08), *P(0.2, 0.14)], fill=255)
    # four fingers, fanned a little: (knuckle, tip, half-width); the middle the longest, the little one short
    fingers = [
        ((-0.135, -0.02), (-0.2, -0.3), 0.043),
        ((-0.045, -0.05), (-0.055, -0.42), 0.046),
        ((0.05, -0.05), (0.08, -0.39), 0.045),
        ((0.14, -0.01), (0.2, -0.25), 0.04),
    ]
    for a, b, r in fingers:
        capsule(d, a, b, r)
    # the thumb: out from the heel of the palm, bent at its joint, to the left
    capsule(d, (-0.13, 0.22), (-0.27, 0.07), 0.055)
    capsule(d, (-0.27, 0.07), (-0.36, -0.06), 0.046)
    return im, fingers


def main():
    mask, fingers = hand_mask()
    m = np.asarray(mask) / 255.0
    # the tube: a band round the silhouette's edge (grown out a little, eaten in a little)
    w = BIG * 0.038
    grow = np.asarray(mask.filter(ImageFilter.MaxFilter(int(w) | 1))) / 255.0
    shrink = np.asarray(mask.filter(ImageFilter.MinFilter(int(w) | 1))) / 255.0
    tube = np.clip(grow - shrink, 0, 1)

    # inside: two palm creases (fainter, thinner)
    det = Image.new("L", (BIG, BIG), 0)
    dd = ImageDraw.Draw(det)
    lw = int(BIG * 0.022)
    dd.arc([*P(-0.1, 0.02), *P(0.24, 0.24)], 200, 320, fill=150, width=lw)       # the heart line
    dd.arc([*P(-0.22, 0.06), *P(0.06, 0.44)], 255, 340, fill=130, width=lw)      # the life line, round the thumb
    detail = (np.asarray(det.filter(ImageFilter.GaussianBlur(BIG * 0.004))) / 255.0) * m

    alpha = np.clip(np.maximum(tube, detail * 0.55) + m * 0.08, 0, 1)
    rgba = np.zeros((BIG, BIG, 4), dtype=np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = (alpha * 255).astype(np.uint8)
    img = Image.fromarray(rgba).resize((SIZE, SIZE), Image.LANCZOS)
    img.save(os.path.join(OUT, "hand.png"))

    glow = Image.fromarray((tube * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(BIG * 0.03))
    g = np.asarray(glow) / 255.0
    g = np.clip(g / max(g.max(), 1e-6) * 0.9, 0, 1)
    rgba = np.zeros((BIG, BIG, 4), dtype=np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = (g * 255).astype(np.uint8)
    Image.fromarray(rgba).resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(OUT, "hand_glow.png"))
    print("wrote", os.path.join(OUT, "hand.png"), "and hand_glow.png")


if __name__ == "__main__":
    main()
