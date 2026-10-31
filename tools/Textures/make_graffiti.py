"""Graffiti for the underground (2026-10-31, the owner, with photos of graffitied tunnels: "implement a bunch of different
graffiti in the underground sections all over the walls and in the red light green light hallway. Make the graffiti
original, use words and phrases like: 19, Free Sex, Hush, Sssshhhh, Quiet, Sleep, East is Up, Thinny, Tip-Toe, 83', 87',
92', 81', Dreams, Speakeasy, Exclusion Zone, Red Light, Green Light ... make it look older and more faded like the paint
is chipping and flaking off").

    python tools/Textures/make_graffiti.py

Writes assets/textures/graffiti/<name>.png (RGBA: clear where there's no paint, and where it has flaked off), laid on the
walls as decals by scripts/world/GraffitiDresser.cs. All of it drawn here, nothing copied:
    piece_*    big letters: a fill fading top to bottom, a pattern in it, a hard black outline and a second colour round
               that, a 3D drop, white shines, a cloud behind, drips
    throw_*    quick bubble letters: one fill, one outline, the fill streaky where the can ran dry
    tag_*      hand styles in one colour, slanted, an underline swoosh, drips
    sten_*     stencils: sprayed through a cut card, the bridges left in the letters, the overspray soft
    char_*     characters: a face all eyes; a face with a finger to its lips; a sleeper and his Zs
Then the years on them: faded toward chalk, grime down from the top, the paint flaked off in patches (clear) with a
darker lip round each, pores, and here and there a grey buff where someone painted over it. Seeded: the same every run.
"""
import math
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "textures", "graffiti")
FONTS = os.path.join(ROOT, "assets", "fonts")
os.makedirs(OUT, exist_ok=True)


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


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


def arr(m):
    return np.asarray(m).astype(float) / 255.0


def img(a):
    return Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))


def grow(m, px):
    """A mask grown by px (round), as a 0..1 array."""
    im = img(m)
    for _ in range(max(1, px // 6)):
        im = im.filter(ImageFilter.MaxFilter(13 if px >= 6 else 2 * px + 1))
    return arr(im)


def letters(text, fnt, W, H, bounce=0.0, slant=0.0, seed=0, spacing=0):
    """The word as a mask, each letter nudged up and down and turned a little (a bounce), centred."""
    r = np.random.default_rng(seed)
    probe = ImageDraw.Draw(Image.new("L", (1, 1)))
    widths = [probe.textbbox((0, 0), ch, font=fnt)[2] for ch in text]
    total = sum(widths) + spacing * (len(text) - 1)
    m = Image.new("L", (W, H), 0)
    x = (W - total) / 2
    asc = fnt.getbbox("A")[3]
    for ch, w in zip(text, widths):
        if ch == " ":
            x += w + spacing
            continue
        cell = Image.new("L", (int(w * 1.6) + 40, int(asc * 1.8) + 40), 0)
        ImageDraw.Draw(cell).text((20, 20), ch, font=fnt, fill=255)
        cell = cell.rotate(r.uniform(-1, 1) * bounce * 14 - slant, resample=Image.BICUBIC, expand=False)
        dy = r.uniform(-1, 1) * bounce * asc * 0.12
        m.paste(cell, (int(x - 20), int((H - asc) / 2 - 20 + dy - asc * 0.08)), cell)
        x += w + spacing
    if slant:
        m = m.transform(m.size, Image.AFFINE, (1, math.tan(math.radians(slant)), -H * 0.5 * math.tan(math.radians(slant)), 0, 1, 0), resample=Image.BICUBIC)
    return arr(m)


def fit(text, name, W, frac, cap):
    """The biggest size (up to cap) at which text spans frac of W."""
    probe = ImageDraw.Draw(Image.new("L", (1, 1)))
    size = cap
    while size > 40 and probe.textbbox((0, 0), text, font=font(name, size))[2] > W * frac:
        size -= 10
    return size


def drips(m, n, seed, maxlen=60, width=4):
    """Runs down from the paint's lower edges."""
    r = np.random.default_rng(seed)
    H, W = m.shape
    d = Image.new("L", (W, H), 0)
    dd = ImageDraw.Draw(d)
    ys, xs = np.nonzero(m[:-2] > 0.5)
    if len(xs) == 0:
        return arr(d)
    for _ in range(n):
        i = r.integers(0, len(xs))
        x, y = xs[i], ys[i]
        while y + 1 < H and m[y + 1, x] > 0.5:
            y += 1
        L = r.uniform(0.25, 1.0) * maxlen
        w = r.uniform(0.6, 1.0) * width
        dd.line([(x, y), (x, y + L)], fill=255, width=int(w))
        dd.ellipse((x - w * 0.8, y + L - w * 0.8, x + w * 0.8, y + L + w * 0.8), fill=255)
    return arr(d)


def comp(layers, H, W):
    """Paint layers (rgb, mask) over each other, bottom first: the colour and the coverage."""
    rgb = np.zeros((H, W, 3))
    a = np.zeros((H, W))
    for col, m in layers:
        col = np.broadcast_to(np.asarray(col, float), (H, W, 3)) if np.asarray(col).ndim == 1 else col
        rgb = rgb * (1 - m[..., None]) + col * m[..., None]
        a = a + m * (1 - a)
    return rgb, a


def cloud(H, W, seed, cx=0.5, cy=0.5, rx=0.46, ry=0.4):
    r = np.random.default_rng(seed)
    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)
    for _ in range(26):
        a = r.uniform(0, math.tau)
        rr = r.uniform(0.55, 1.0)
        x = W * (cx + math.cos(a) * rx * rr * 0.75)
        y = H * (cy + math.sin(a) * ry * rr * 0.75)
        s = r.uniform(0.12, 0.24) * W
        d.ellipse((x - s, y - s * 0.8, x + s, y + s * 0.8), fill=255)
    return arr(m.filter(ImageFilter.GaussianBlur(6)).point(lambda v: 255 if v > 120 else 0))


def age(rgb, a, seed, fade=0.35, flake=0.22, buff=0.0, grime=0.35):
    """The years: faded toward chalk, grime from the top, flaked off in patches (clear, a darker lip round each),
    pores; a grey buff over part of it now and then."""
    H, W = a.shape
    yy = np.mgrid[0:H, 0:W][0] / H
    chalk = np.array([0.86, 0.84, 0.8])
    gray = rgb.mean(-1, keepdims=True)
    rgb = rgb * 0.7 + gray * 0.3                                   # (the sun and the damp take the colour first)
    f = np.clip(fade * (0.6 + 0.8 * noise(H, W, 4, 3, seed)), 0, 0.8)
    rgb = rgb * (1 - f[..., None]) + chalk * f[..., None]
    g = grime * np.clip(0.9 - yy * 1.4, 0, 1) * noise(H, W * 4, 40, 2, seed + 1)[:, ::4] * 0.8
    rgb = rgb * (1 - np.clip(g, 0, 0.6)[..., None])
    # the flaking: patches gone to the wall, worst at the bottom where it's damp and kicked
    n = noise(H, W, 22, 4, seed + 2) * 0.6 + noise(H, W, 70, 2, seed + 3) * 0.4
    gone = n + yy * 0.12 > np.quantile(n, 1 - flake) + 0.06
    lip = (n + yy * 0.12 > np.quantile(n, 1 - flake) + 0.03) & ~gone
    rgb = np.where(lip[..., None], rgb * 0.55, rgb)
    a = np.where(gone, 0.0, a)
    pores = np.random.default_rng(seed + 4).random((H, W)) < 0.05
    a = np.where(pores, a * 0.3, a)
    a = a * (0.72 + 0.28 * noise(H, W, 6, 3, seed + 5))             # (thin where the can was moving fast)
    if buff > 0:
        r = np.random.default_rng(seed + 6)
        x0, y0 = r.uniform(0.1, 0.5) * W, r.uniform(0.2, 0.5) * H
        bm = np.zeros((H, W))
        bm[int(y0):int(y0 + H * r.uniform(0.3, 0.6)), int(x0):int(x0 + W * buff)] = 1
        bm = arr(img(bm).filter(ImageFilter.GaussianBlur(2)))
        rgb = rgb * (1 - bm[..., None]) + np.array([0.42, 0.42, 0.41]) * bm[..., None]
        a = np.maximum(a, bm * 0.85)
    return rgb, a


def save(name, rgb, a, size):
    im = img(rgb)
    im.putalpha(img(a))
    im = im.resize(size, Image.LANCZOS)
    im.save(os.path.join(OUT, name + ".png"))
    print(f"[graffiti] {name}")


ANTON = "Anton-Regular.ttf"


def piece(name, word, cols, seed, size=(1024, 512), fs=300, bounce=0.7, pattern="dots", fade=0.3, flake=0.12, buff=0.0):
    W, H = 1600, 800
    fs = fit(word, ANTON, W, 0.8, 560)
    fill_a, fill_b, outline2, back, pat = [np.array(c) for c in cols]
    let = letters(word, font(ANTON, fs), W, H, bounce=bounce, seed=seed, spacing=-int(fs * 0.04))
    let = arr(img(let).filter(ImageFilter.GaussianBlur(2)).point(lambda v: 255 if v > 110 else 0))
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    ys = np.nonzero(let.max(1) > 0)[0]
    t = np.clip((yy - ys.min()) / max(1, ys.max() - ys.min()), 0, 1)
    fill = fill_a * (1 - t[..., None]) + fill_b * t[..., None]
    if pattern == "dots":
        pm = ((np.sin(xx / 22) * np.sin(yy / 22)) > 0.55).astype(float)
    elif pattern == "stripes":
        pm = (np.sin((xx + yy) / 14) > 0.6).astype(float)
    else:
        pm = (noise(H, W, 30, 2, seed) > 0.62).astype(float)
    fill = fill * (1 - pm[..., None] * 0.8) + pat * pm[..., None] * 0.8
    out1 = grow(let, 10)
    out2 = grow(let, 26)
    drop = np.roll(np.roll(out2, 18, 0), 18, 1)
    shine = np.clip(let - np.roll(np.roll(let, 9, 0), 9, 1), 0, 1) * (noise(H, W, 12, 2, seed + 1) > 0.5)
    cl = arr(img(grow(out2, 60)).filter(ImageFilter.GaussianBlur(14)).point(lambda v: 255 if v > 110 else 0))
    dr = drips(out2, 14, seed + 3, maxlen=90, width=7)
    layers = [(back, cl), ((0.04, 0.04, 0.05), np.maximum(drop, dr * 0.9)), (outline2, out2), ((0.03, 0.03, 0.035), out1),
              (fill, let), ((0.97, 0.97, 0.95), shine * 0.9)]
    rgb, a = comp(layers, H, W)
    rgb, a = age(rgb, a, seed + 9, fade=fade, flake=flake, buff=buff)
    save(name, rgb, a, size)


def throwup(name, word, fillc, linec, seed, size=(768, 384), fade=0.35, flake=0.12):
    W, H = 1200, 600
    let = letters(word, font(ANTON, fit(word, ANTON, W, 0.78, 420)), W, H, bounce=0.5, seed=seed, spacing=-12)
    bub = arr(img(let).filter(ImageFilter.MaxFilter(13)).filter(ImageFilter.GaussianBlur(9)).point(lambda v: 255 if v > 100 else 0))
    edge = grow(bub, 12)
    streak = noise(H, W * 6, 50, 2, seed)[:, ::6] > 0.38            # (the fill done fast: gaps where the can skipped)
    fill = bub * (0.65 + 0.35 * streak)
    inner = np.clip(grow(let, 2) - let, 0, 1) * 0.5                  # (a ghost of the letters' joins inside)
    dr = drips(edge, 10, seed + 1, maxlen=70, width=6)
    rgb, a = comp([(linec, np.maximum(edge, dr)), (fillc, fill), (linec, inner)], H, W)
    rgb, a = age(rgb, a, seed + 9, fade=fade, flake=flake)
    save(name, rgb, a, size)


def tag(name, word, col, seed, fnt="RockSalt-Regular.ttf", fs=150, size=(768, 384), slant=14, fade=0.25, flake=0.05):
    W, H = 1200, 600
    let = letters(word, font(fnt, min(fs, fit(word, fnt, W, 0.8, fs))), W, H, bounce=0.4, slant=slant, seed=seed)
    let = grow(let, 5)
    # the swoosh under it, a star or a crown
    sw = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(sw)
    ys, xs = np.nonzero(let > 0.5)
    if len(xs):
        x0, x1, yb = xs.min(), xs.max(), ys.max()
        pts = [(x0 + (x1 - x0) * t, yb + 24 - 30 * math.sin(t * math.pi) + 18 * t) for t in np.linspace(0, 1, 30)]
        d.line(pts, fill=255, width=9)
        d.line([pts[-1], (pts[-1][0] + 20, pts[-1][1] - 22)], fill=255, width=9)
    m = np.maximum(let, arr(sw))
    m = np.maximum(m, drips(m, 8, seed + 1, maxlen=55, width=4))
    rgb, a = comp([(col, m)], H, W)
    rgb, a = age(rgb, a, seed + 9, fade=fade, flake=flake, grime=0.2)
    save(name, rgb, a, size)


def stencil(name, word, col, seed, fs=230, size=(1024, 384), arrow=False, fade=0.3, flake=0.1):
    W, H = 1600, 600
    let = letters(word, font("SpecialElite-Regular.ttf", fit(word, "SpecialElite-Regular.ttf", W, 0.8 if arrow else 0.9, 320)), W, H, bounce=0.0, seed=seed, spacing=6)
    let = grow(let, 4)
    yy, xx = np.mgrid[0:H, 0:W]
    bridges = (np.abs(yy - H * 0.5) < 7) & (noise(H, W, 20, 1, seed) > 0.0)   # the card's bridges across the letters
    m = let * (~bridges)
    if arrow:
        ar = Image.new("L", (W, H), 0)
        d = ImageDraw.Draw(ar)
        ax = W * 0.92
        d.polygon([(ax, H * 0.12), (ax - 70, H * 0.4), (ax - 28, H * 0.4), (ax - 28, H * 0.88), (ax + 28, H * 0.88), (ax + 28, H * 0.4), (ax + 70, H * 0.4)], fill=255)
        m = np.maximum(m, arr(ar))
    over = arr(img(m).filter(ImageFilter.GaussianBlur(10))) * 0.35 * noise(H, W, 30, 2, seed + 1)
    rgb, a = comp([(col, over), (col, m)], H, W)
    rgb, a = age(rgb, a, seed + 9, fade=fade, flake=flake)
    save(name, rgb, a, size)


def char_eyes(name, seed, size=(512, 512)):
    """A face all eyes and teeth, sprayed in reds and black (the refs' wall faces), staring."""
    W = H = 900
    m = Image.new("RGB", (W, H), (0, 0, 0))
    al = Image.new("L", (W, H), 0)
    d, da = ImageDraw.Draw(m), ImageDraw.Draw(al)
    def blob(box, col):
        d.ellipse(box, fill=col); da.ellipse(box, fill=255)
    blob((150, 120, 750, 820), (196, 40, 32))
    blob((175, 150, 725, 790), (226, 120, 60))
    for ex in (330, 570):
        blob((ex - 110, 260, ex + 110, 480), (245, 240, 228))
        blob((ex - 38, 330, ex + 38, 410), (12, 10, 10))
        d.ellipse((ex - 14, 340, ex + 4, 358), fill=(250, 250, 250))
    d.rectangle((300, 600, 600, 680), fill=(20, 12, 10)); da.rectangle((300, 600, 600, 680), fill=255)
    for x in range(310, 600, 42):
        d.polygon([(x, 600), (x + 30, 600), (x + 15, 645)], fill=(240, 236, 220))
    d.line([(150, 470), (750, 470)], fill=(10, 8, 8), width=12)
    rgb, a = arr(m), arr(al)
    a = np.maximum(a, drips(a, 16, seed, maxlen=90, width=8) * 0.9)
    rgb, a = age(rgb, a, seed + 9, fade=0.3, flake=0.25)
    save(name, rgb, a, size)


def char_shush(name, seed, size=(512, 512)):
    """A pale face with a finger to its lips: Sssshhhh."""
    W = H = 900
    m = Image.new("RGB", (W, H), (0, 0, 0))
    al = Image.new("L", (W, H), 0)
    d, da = ImageDraw.Draw(m), ImageDraw.Draw(al)
    def poly(pts, col): d.polygon(pts, fill=col); da.polygon(pts, fill=255)
    def ell(box, col): d.ellipse(box, fill=col); da.ellipse(box, fill=255)
    ell((200, 100, 700, 720), (20, 20, 22))
    ell((220, 120, 680, 700), (214, 214, 206))
    for ex in (360, 540):
        ell((ex - 50, 330, ex + 50, 370), (20, 20, 22))
    d.line([(390, 560), (510, 560)], fill=(140, 20, 24), width=18)
    poly([(430, 760), (470, 760), (470, 470), (430, 470)], (214, 214, 206))
    ell((420, 440, 480, 500), (214, 214, 206))
    d.line([(430, 470), (430, 760)], fill=(20, 20, 22), width=6); d.line([(470, 470), (470, 760)], fill=(20, 20, 22), width=6)
    f = font("ReenieBeanie.ttf", 150)
    da.text((520, 600), "Sssshhhh", font=f, fill=255); d.text((520, 600), "Sssshhhh", font=f, fill=(150, 30, 40))
    rgb, a = arr(m), arr(al)
    rgb, a = age(rgb, a, seed + 9, fade=0.35, flake=0.2)
    save(name, rgb, a, size)


def char_sleep(name, seed, size=(512, 512)):
    """A closed eye, its lashes hanging, and Zs rising off it."""
    W = H = 900
    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)
    d.arc((120, 200, 680, 620), 15, 165, fill=255, width=30)
    for i in range(8):
        a = math.radians(25 + i * 18.5)
        x, y = 400 + 280 * math.cos(a), 410 + 210 * math.sin(a)
        d.line([(x, y), (x + math.cos(a) * 70, y + math.sin(a) * 70)], fill=255, width=16)
    for (x, y, s_) in ((560, 250, 1.0), (680, 130, 0.78), (780, 40, 0.58)):
        d.text((x, y), "Z", font=font(ANTON, int(170 * s_)), fill=255)
    a = arr(m)
    a = np.maximum(a, drips(a, 8, seed, maxlen=60, width=6))
    rgb = np.zeros((H, W, 3)) + np.array([0.3, 0.55, 0.85])
    rgb, a = age(rgb, a, seed + 9, fade=0.3, flake=0.06)
    save(name, rgb, a, size)


# the palettes (fill top, fill bottom, second outline, cloud, pattern): the refs' loud ones, sun-bleached later by age()
P = {
    "sun": ((0.98, 0.84, 0.12), (0.95, 0.42, 0.1), (0.15, 0.35, 0.85), (0.55, 0.15, 0.6), (0.98, 0.95, 0.6)),
    "pink": ((0.95, 0.3, 0.6), (0.6, 0.1, 0.45), (0.98, 0.85, 0.15), (0.15, 0.55, 0.75), (1.0, 0.7, 0.85)),
    "lime": ((0.6, 0.95, 0.3), (0.15, 0.6, 0.35), (0.9, 0.2, 0.25), (0.2, 0.2, 0.55), (0.9, 1.0, 0.6)),
    "ice": ((0.7, 0.92, 1.0), (0.2, 0.45, 0.95), (0.95, 0.95, 0.95), (0.9, 0.35, 0.2), (1.0, 1.0, 1.0)),
    "blood": ((0.9, 0.12, 0.1), (0.45, 0.02, 0.05), (0.95, 0.95, 0.9), (0.1, 0.1, 0.12), (1.0, 0.5, 0.4)),
    "green": ((0.3, 0.95, 0.45), (0.05, 0.45, 0.2), (0.05, 0.05, 0.05), (0.85, 0.85, 0.2), (0.7, 1.0, 0.7)),
    "violet": ((0.75, 0.55, 1.0), (0.35, 0.15, 0.7), (0.98, 0.6, 0.15), (0.2, 0.7, 0.6), (0.95, 0.85, 1.0)),
}

piece("piece_hush", "HUSH", P["pink"], 1, pattern="dots")
piece("piece_dreams", "DREAMS", P["violet"], 2, fs=250, pattern="stripes")
piece("piece_speakeasy", "SPEAKEASY", P["sun"], 3, fs=180, pattern="noise", buff=0.1)
piece("piece_thinny", "THINNY", P["ice"], 4, fs=250, pattern="stripes")
piece("piece_redlight", "RED LIGHT", P["blood"], 5, fs=190, pattern="dots", fade=0.25)
piece("piece_greenlight", "GREEN LIGHT", P["green"], 6, fs=170, pattern="noise", fade=0.25)
piece("piece_19", "19", P["lime"], 7, fs=420, pattern="dots", size=(768, 512))
piece("piece_freesex", "FREE SEX", P["pink"], 8, fs=200, pattern="stripes", flake=0.18)
piece("piece_sleep", "SLEEP", P["ice"], 9, fs=260, pattern="dots", fade=0.4)
throwup("throw_hush", "HUSH", (0.88, 0.88, 0.86), (0.05, 0.05, 0.06), 11)
throwup("throw_19", "19", (0.95, 0.85, 0.2), (0.6, 0.05, 0.1), 12)
throwup("throw_quiet", "QUIET", (0.3, 0.6, 0.95), (0.03, 0.03, 0.05), 13)
throwup("throw_dreams", "DREAMS", (0.95, 0.5, 0.75), (0.1, 0.05, 0.25), 14)
tag("tag_83", "83'", (0.9, 0.9, 0.88), 21, fnt=ANTON, fs=260, slant=10)
tag("tag_87", "87'", (0.95, 0.2, 0.25), 22, fnt=ANTON, fs=260, slant=-6)
tag("tag_92", "92'", (0.2, 0.8, 0.35), 23, fnt=ANTON, fs=260, slant=12)
tag("tag_81", "81'", (0.98, 0.8, 0.15), 24, fnt=ANTON, fs=260, slant=8)
tag("tag_tiptoe", "Tip-Toe", (0.1, 0.1, 0.12), 25, fnt="RockSalt-Regular.ttf", fs=120)
tag("tag_ssh", "Sssshhhh", (0.85, 0.2, 0.3), 26, fnt="ReenieBeanie.ttf", fs=240, slant=6)
tag("tag_hush", "hush", (0.2, 0.45, 0.95), 27, fnt="RockSalt-Regular.ttf", fs=150)
tag("tag_dreams", "dreams", (0.9, 0.9, 0.9), 28, fnt="ReenieBeanie.ttf", fs=260)
tag("tag_quiet", "QUIET", (0.95, 0.55, 0.1), 29, fnt="RockSalt-Regular.ttf", fs=130, slant=-8)
tag("tag_thinny", "thinny", (0.6, 0.3, 0.95), 30, fnt="RockSalt-Regular.ttf", fs=140)
stencil("sten_eastisup", "EAST IS UP", (0.08, 0.08, 0.09), 31, fs=190, arrow=True)
stencil("sten_exclusion", "EXCLUSION ZONE", (0.85, 0.12, 0.1), 32, fs=150)
stencil("sten_quiet", "QUIET", (0.9, 0.85, 0.2), 33, fs=300)
stencil("sten_speakeasy", "SPEAKEASY ->", (0.88, 0.88, 0.86), 34, fs=170)
char_eyes("char_eyes", 41)
char_shush("char_shush", 42)
char_sleep("char_sleep", 43)
