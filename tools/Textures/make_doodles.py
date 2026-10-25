"""Pen doodles for the notes (2026-10-24, the owner: "some drawings thrown in with the subject matter of the note dictating
if a drawing would be a fun addition", with photos of diaries and sketchbooks: scribbled heads with staring eyes, figures
in the margins, tally marks, hearts, boxes round words).

    python tools/Textures/make_doodles.py

Writes assets/ui/doodles/<name>.png: white ink on clear (the card tints it the note's own ink), drawn by a hand that
wobbles: every stroke a polyline nudged by smooth noise, some gone over twice, the pen pressing harder at the start.
Drawn at 4x and brought down, so they hold up at the card's scale. Which note gets which: NoteDoodles.cs.
"""
import math
import os
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "ui", "doodles")
os.makedirs(OUT, exist_ok=True)
S = 4                 # supersampling
W, H = 120, 90        # the doodle's size at the card's scale


class Pen:
    def __init__(self, seed, w=W, h=H, width=1.0):
        self.r = np.random.default_rng(seed)
        self.w, self.h = w, h
        self.im = Image.new("L", (w * S, h * S), 0)
        self.d = ImageDraw.Draw(self.im)
        self.width = width

    def wob(self, pts, amt=0.9, twice=0.0, width=None, step=2.0):
        """A wobbly stroke through pts (card px): resampled every <step> px, each point nudged by a smooth wander."""
        pts = [np.array(p, float) for p in pts]
        dense = []
        for a, b in zip(pts, pts[1:]):
            n = max(1, int(np.linalg.norm(b - a) / step))
            for i in range(n):
                dense.append(a + (b - a) * i / n)
        dense.append(pts[-1])
        passes = 2 if self.r.random() < twice else 1
        for p in range(passes):
            ph = self.r.uniform(0, 100, 2)
            fr = self.r.uniform(0.08, 0.16)
            out = []
            for i, q in enumerate(dense):
                off = np.array([math.sin(i * fr + ph[0]) + 0.5 * math.sin(i * fr * 2.7 + ph[1]),
                                math.cos(i * fr * 1.3 + ph[1]) + 0.5 * math.sin(i * fr * 2.1 + ph[0])]) * amt * 0.5
                out.append(tuple(((q + off + (p * self.r.normal(0, 0.5, 2))) * S).tolist()))
            wd = (width or self.width) * S
            self.d.line(out, fill=255, width=max(1, int(round(wd))), joint="curve")

    def curve(self, f, t0, t1, n=40, **kw):
        self.wob([f(t0 + (t1 - t0) * i / n) for i in range(n + 1)], **kw)

    def scribble(self, cx, cy, rx, ry, loops=60, amt=0.6, width=0.8):
        """A ball of scribble: a pen going round and round in small loops, drifting all over an ellipse (the refs'
        scratched-in head), denser to the middle."""
        pts = []
        x, y = cx, cy
        for i in range(loops):
            # the next place it drifts to, somewhere inside
            while True:
                tx, ty = self.r.uniform(-1, 1), self.r.uniform(-1, 1)
                if tx * tx + ty * ty < 1:
                    break
            tx, ty = cx + tx * rx * 0.85, cy + ty * ry * 0.85
            lr = self.r.uniform(3, 9)
            ph = self.r.uniform(0, 6.3)
            for k in range(14):
                t = k / 14
                px, py = x + (tx - x) * t, y + (ty - y) * t
                pts.append((px + lr * math.cos(ph + t * 6.3 * 1.6), py + lr * 0.8 * math.sin(ph + t * 6.3 * 1.6)))
            x, y = tx, ty
        self.wob(pts, amt=amt, width=width, step=1.5)

    def dot(self, x, y, r):
        self.d.ellipse(((x - r) * S, (y - r) * S, (x + r) * S, (y + r) * S), fill=255)

    def save(self, name):
        im = self.im.resize((self.w, self.h), Image.LANCZOS)
        a = np.asarray(im).astype(float) / 255.0
        a = np.clip(a * 1.25, 0, 1)
        rgba = np.zeros((self.h, self.w, 4), np.uint8)
        rgba[..., :3] = 255
        rgba[..., 3] = (a * 255).astype(np.uint8)
        Image.fromarray(rgba).save(os.path.join(OUT, name + ".png"))
        print(f"[doodle] {name}")


def ellipse(cx, cy, rx, ry, a0=0.0, turns=1.0):
    return lambda t: (cx + rx * math.cos(a0 + t * 2 * math.pi * turns), cy + ry * math.sin(a0 + t * 2 * math.pi * turns))


def scribble_head():
    """The refs' monster: a head scratched in, two wide staring eyes left white."""
    p = Pen(1, width=0.7)
    p.scribble(60, 48, 54, 42, loops=150)
    # the eyes: cleared, then ringed and pupilled
    for ex in (42, 76):
        p.d.ellipse(((ex - 9) * S, (44 - 7) * S, (ex + 9) * S, (44 + 7) * S), fill=0)
        p.curve(ellipse(ex, 44, 9, 7), 0, 1.05, width=1.2)
        p.curve(ellipse(ex, 44, 6.5, 5), 0.2, 1.1, width=0.8)
        p.dot(ex + 1, 44.5, 2.2)
    p.save("scribble_head")


def stairs():
    """Stairs standing in the trees, going up to nothing."""
    p = Pen(2)
    x0, y0 = 30, 84
    pts = [(x0, y0)]
    x, y = x0, y0
    for i in range(7):
        y -= 9
        pts.append((x, y))
        x += 9
        pts.append((x, y))
    p.wob(pts, twice=0.6, width=1.1)
    p.wob([(x0, y0), (x0 + 63, y0)], width=0.8)
    p.wob([(x, y), (x, y0)], width=0.8)
    for tx, th in ((10, 52), (104, 60), (114, 40)):
        tree(p, tx, 86, th)
    p.save("stairs")


def tree(p, x, base, h):
    p.wob([(x, base), (x, base - h)], width=0.9)
    for i in range(int(h / 6)):
        y = base - h + 4 + i * 6
        wd = 2 + i * 1.3
        p.wob([(x - wd, y + 3), (x, y), (x + wd, y + 3)], amt=0.5, width=0.7)


def pines():
    p = Pen(3)
    for tx, th in ((22, 60), (46, 76), (72, 54), (96, 70)):
        tree(p, tx, 86, th)
    p.wob([(4, 86), (116, 86)], width=0.7, amt=1.4)
    p.save("pines")


def eye():
    """One eye, wide, lashes scratched round it, the iris gone over and over."""
    p = Pen(4)
    p.curve(lambda t: (14 + 92 * t, 46 - 28 * math.sin(t * math.pi)), 0, 1, width=1.2, twice=1.0)
    p.curve(lambda t: (14 + 92 * t, 46 + 22 * math.sin(t * math.pi)), 0, 1, width=1.2, twice=1.0)
    for k in range(4):
        p.curve(ellipse(60, 46, 15 - k * 0.8, 15 - k * 0.8, k), 0, 1.02, width=0.7)
    p.dot(60, 46, 6)
    p.d.ellipse((63 * S, 41 * S, 66 * S, 44 * S), fill=0)
    for i in range(11):
        t = 0.08 + i * 0.084
        x, y = 14 + 92 * t, 46 - 28 * math.sin(t * math.pi)
        p.wob([(x, y), (x + (t - 0.5) * 10, y - 9)], amt=0.3, width=0.7)
    p.save("eye")


def antlers():
    """A deer's skull, its antlers branching up (the thing in the woods)."""
    p = Pen(5)
    p.wob([(48, 34), (44, 48), (52, 72), (60, 80), (68, 72), (76, 48), (72, 34), (60, 30), (48, 34)], twice=0.7, width=1.1)
    for ex in (52, 68):
        p.curve(ellipse(ex, 46, 5, 4), 0, 1.05, width=0.9)
        p.dot(ex, 46, 2.6)
    p.wob([(56, 66), (58, 70)], width=0.7)
    p.wob([(64, 66), (62, 70)], width=0.7)
    for sgn in (-1, 1):
        cx = 60 + sgn * 10
        main = [(cx, 32), (cx + sgn * 10, 22), (cx + sgn * 22, 14), (cx + sgn * 36, 4)]
        p.wob(main, twice=0.5, width=1.0)
        for (bx, by, ex2, ey2) in ((cx + sgn * 10, 22, cx + sgn * 6, 6), (cx + sgn * 22, 14, cx + sgn * 20, 2), (cx + sgn * 16, 18, cx + sgn * 32, 18)):
            p.wob([(bx, by), (ex2, ey2)], width=0.8)
    p.save("antlers")


def trail_map():
    """A path dotted through, a river, an X, an N with its arrow."""
    p = Pen(6)
    path = [(8, 80), (24, 70), (34, 74), (48, 58), (66, 60), (78, 42), (96, 30)]
    pts = []
    for a, b in zip(path, path[1:]):
        for i in range(5):
            t = i / 5
            pts.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    for q in pts:
        p.dot(q[0], q[1], 0.9)
    p.curve(lambda t: (4 + 112 * t, 20 + 10 * math.sin(t * 7)), 0, 1, width=0.8)
    p.curve(lambda t: (4 + 112 * t, 26 + 10 * math.sin(t * 7)), 0, 1, width=0.8)
    p.wob([(92, 24), (102, 36)], width=1.4)
    p.wob([(102, 24), (92, 36)], width=1.4)
    p.wob([(108, 88), (108, 66)], width=0.9)
    p.wob([(104, 70), (108, 64), (112, 70)], width=0.9)
    p.wob([(104, 86), (104, 76), (112, 86), (112, 76)], width=0.8)   # (an N, under the arrow)
    for tx in (20, 30, 58):
        tree(p, tx, 52 - (tx % 7), 14)
    p.save("trail_map")


def cabin():
    p = Pen(7)
    p.wob([(26, 84), (26, 54), (94, 54), (94, 84), (26, 84)], twice=0.5, width=1.0)
    p.wob([(20, 56), (60, 28), (100, 56)], twice=0.6, width=1.1)
    for y in range(60, 84, 6):
        p.wob([(26, y), (94, y)], amt=0.6, width=0.5)
    p.wob([(52, 84), (52, 66), (64, 66), (64, 84)], width=0.9)
    p.wob([(72, 62), (86, 62), (86, 74), (72, 74), (72, 62)], width=0.8)
    p.wob([(78, 38), (78, 26), (84, 26), (84, 42)], width=0.9)
    p.curve(lambda t: (81 + 6 * math.sin(t * 9), 24 - 22 * t), 0, 1, width=0.7)
    p.save("cabin")


def moon():
    p = Pen(8)
    p.curve(ellipse(50, 46, 30, 30), 1 / 6, 5 / 6, width=1.2, twice=1.0)
    p.curve(lambda t: ((1 - t) ** 2 * 65 + 2 * (1 - t) * t * 30 + t * t * 65, (1 - t) ** 2 * 20 + 2 * (1 - t) * t * 46 + t * t * 72), 0, 1, width=0.9)
    for (sx, sy, r) in ((96, 20, 6), (104, 56, 4), (84, 78, 5), (16, 14, 3)):
        for a in range(4):
            ang = a * math.pi / 4
            p.wob([(sx - r * math.cos(ang), sy - r * math.sin(ang)), (sx + r * math.cos(ang), sy + r * math.sin(ang))], amt=0.2, width=0.7)
    p.save("moon")


def tally():
    """Days counted: gates of five, the last one unfinished."""
    p = Pen(9, width=1.0)
    x = 6
    for g in range(4):
        n = 5 if g < 3 else 3
        for i in range(min(n, 4)):
            p.wob([(x + i * 6, 22 + p.r.normal(0, 1)), (x + i * 6 + p.r.normal(0, 1), 68)], amt=0.5)
        if n == 5:
            p.wob([(x - 4, 60), (x + 22, 30)], amt=0.5)
        x += 30
    p.save("tally")


def footprints():
    p = Pen(10)
    for i in range(5):
        x, y = 16 + i * 22, 78 - i * 14 + (6 if i % 2 else -6)
        p.curve(ellipse(x, y, 4.5, 8), 0, 1.04, width=0.9)
        for k in range(4):
            p.dot(x - 4 + k * 2.6, y - 12 - (1.5 if k in (1, 2) else 0), 1.2)
    p.save("footprints")


def flame():
    """A fire: an outer flame licking up to three tongues, an inner one, logs crossed under."""
    p = Pen(11)
    outer = [(36, 80), (30, 64), (36, 48), (40, 56), (44, 36), (52, 46), (58, 10), (66, 40), (72, 26), (78, 50), (84, 44), (88, 64), (82, 80)]
    p.wob(outer, width=1.1, twice=0.8, step=1.5)
    inner = [(48, 80), (46, 68), (52, 58), (56, 64), (60, 44), (66, 62), (70, 56), (72, 70), (68, 80)]
    p.wob(inner, width=0.8, step=1.5)
    p.wob([(24, 86), (96, 80)], width=1.4)
    p.wob([(26, 80), (94, 88)], width=1.4)
    p.save("flame")


def camera():
    p = Pen(12)
    p.wob([(18, 34), (102, 34), (102, 80), (18, 80), (18, 34)], twice=0.6, width=1.1)
    p.wob([(40, 34), (46, 24), (72, 24), (78, 34)], width=1.0)
    for r in (16, 10):
        p.curve(ellipse(60, 57, r, r), 0, 1.04, width=1.0)
    p.dot(60, 57, 3)
    p.wob([(88, 40), (96, 40), (96, 46), (88, 46), (88, 40)], width=0.7)
    p.save("camera")


def spiral():
    p = Pen(13)
    p.curve(lambda t: (60 + t * 36 * math.cos(t * 6 * math.pi), 46 + t * 36 * math.sin(t * 6 * math.pi) * 0.9), 0.02, 1, n=220, width=0.9)
    p.save("spiral")


def key():
    p = Pen(14)
    p.curve(ellipse(28, 46, 13, 13), 0, 1.04, width=1.1, twice=0.7)
    p.curve(ellipse(28, 46, 5, 5), 0, 1.04, width=0.8)
    p.wob([(41, 46), (104, 46)], width=1.2, twice=0.6)
    p.wob([(88, 46), (88, 58), (94, 58), (94, 46)], width=1.0)
    p.wob([(98, 46), (98, 54), (104, 54)], width=1.0)
    p.save("key")


def heart():
    p = Pen(15)
    f = lambda t: (60 + 2.2 * 16 * math.sin(t) ** 3, 44 - 2.2 * (13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)))
    p.curve(f, 0, 2 * math.pi, n=80, width=1.1, twice=1.0)
    p.wob([(58, 18), (64, 32), (54, 44), (62, 56), (58, 70)], width=0.9)   # (cracked down the middle)
    p.save("heart")


def figure():
    """Too tall and too thin, standing in the trees, no face."""
    p = Pen(16)
    p.curve(ellipse(60, 14, 5, 7), 0, 1.04, width=1.0)
    p.wob([(60, 21), (60, 60)], width=1.1, twice=0.6)
    p.wob([(60, 26), (50, 46), (46, 70)], width=0.9)
    p.wob([(60, 26), (70, 46), (74, 70)], width=0.9)
    p.wob([(60, 60), (54, 86)], width=1.0)
    p.wob([(60, 60), (66, 86)], width=1.0)
    for tx, th in ((16, 70), (32, 50), (94, 64), (108, 46)):
        tree(p, tx, 88, th)
    p.save("figure")


def boxes():
    """The refs' margin: a word boxed and crossed out, an arrow, a star."""
    p = Pen(17)
    p.wob([(8, 14), (64, 12), (66, 34), (10, 36), (8, 14)], twice=0.6, width=1.0)
    for y in range(18, 34, 4):
        p.wob([(12, y), (62, y - 2)], amt=0.3, width=0.8)
    p.curve(lambda t: (40 + 44 * t, 40 + 30 * math.sin(t * 2.4)), 0, 1, width=1.0)
    p.wob([(76, 70), (86, 70), (82, 60)], width=1.0)
    cx, cy = 100, 26
    star = [(cx + (14 if k % 2 == 0 else 6) * math.sin(k * math.pi / 5), cy - (14 if k % 2 == 0 else 6) * math.cos(k * math.pi / 5)) for k in range(11)]
    p.wob(star, width=0.9)
    p.save("boxes")


for f in (scribble_head, stairs, pines, eye, antlers, trail_map, cabin, moon, tally, footprints, flame, camera, spiral, key, heart, figure, boxes):
    f()
