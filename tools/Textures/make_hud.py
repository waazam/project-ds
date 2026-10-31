"""The HUD's weathered pieces (2026-10-25, the owner: "reskinning the other ui elements ... we need to make our own custom
model that fits the theme of the game for the compass to make it more unique", with Oblivion's framed compass bar as the
example). Painted at 4x the size they're drawn at (the HUD draws at the window's resolution under the CRT setting, at
640 x 360 without it), the same wear as the save canister and the weathered props: tarnished brass, worn edges, old
leather, yellowed celluloid.

    python tools/Textures/make_hud.py

Writes assets/ui/hud/:
    compass_bar.png      the compass's band: a dark worn-leather strip in a tarnished brass rim, its ends drawn out to
                         points like a ship's compass card's lubber marks, a rivet near each end (260 x 22 drawn)
    compass_pointer.png  the lubber line: a small brass pointer hanging over the band's middle (16 x 12 drawn)
    keycap.png           an old typewriter key: a nickel ring round a dished, yellowed celluloid face; drawn in three
                         slices (its round ends kept, its middle stretched) so a long key name fits (24 x 24 drawn)
    item_strip.png       the held item's strip: worn leather, stitched along both edges, fading out to the left, a
                         brass rivet at its right end (120 x 28 drawn)
    stamina_groove.png   a recessed groove in dark brass for the stamina's tube to run in (100 x 7 drawn)

Seeded: the same every run.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "ui", "hud")
os.makedirs(OUT, exist_ok=True)
S = 4


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


def mask(w, h, draw):
    im = Image.new("L", (w, h), 0)
    draw(ImageDraw.Draw(im))
    return np.asarray(im) / 255.0


def brass(h, w, seed, light_top=True):
    """Tarnished brass: a warm gold gone brown, lighter along the top where the light catches it, brushed, tarnish in
    clouds (brown and a little green), fine scratches bright."""
    yy = np.mgrid[0:h, 0:w][0] / max(1, h - 1)
    base = np.array([0.62, 0.48, 0.24])
    shade = (1.15 - 0.55 * yy) if light_top else np.ones_like(yy)
    brush = noise(h, w * 8, 60, 2, seed)[:, ::8] * 0.15
    img = base * (shade + brush)[..., None]
    tarn = noise(h, w, 6, 4, seed + 1)
    img = img * (1 - 0.45 * np.clip(tarn - 0.4, 0, 1)[..., None] * 2) + np.array([0.22, 0.17, 0.1]) * 0.45 * np.clip(tarn - 0.4, 0, 1)[..., None] * 2
    verd = np.clip((noise(h, w, 14, 3, seed + 2) - 0.68) * 5, 0, 1)
    img = img * (1 - 0.5 * verd[..., None]) + np.array([0.28, 0.4, 0.3]) * 0.5 * verd[..., None]
    sc = Image.new("L", (w, h), 0)
    sd = ImageDraw.Draw(sc)
    r = np.random.default_rng(seed + 3)
    for _ in range(int(w * h / 900)):
        x, y = r.uniform(0, w), r.uniform(0, h)
        a, L = r.uniform(-0.3, 0.3), r.uniform(4, 30)
        sd.line([(x, y), (x + L * np.cos(a), y + L * np.sin(a))], fill=int(r.uniform(60, 160)), width=1)
    s = np.asarray(sc) / 255.0
    return np.clip(img + s[..., None] * 0.25, 0, 1)


def leather(h, w, seed, base=(0.11, 0.08, 0.06)):
    """Old dark leather: pebbled grain, rubbed paler in patches, cracked fine."""
    grain = noise(h, w, 90, 2, seed)
    rub = np.clip((noise(h, w, 5, 3, seed + 1) - 0.45) * 2.5, 0, 1)
    img = np.array(base) * (0.8 + 0.4 * grain)[..., None]
    img = img * (1 - 0.35 * rub[..., None]) + np.array([0.3, 0.24, 0.18]) * 0.35 * rub[..., None]
    pts = np.random.default_rng(seed + 2)
    yy, xx = np.mgrid[0:h, 0:w]
    crack = Image.new("L", (w, h), 0)
    cd = ImageDraw.Draw(crack)
    for _ in range(int(w * h / 1500)):
        x, y = pts.uniform(0, w), pts.uniform(0, h)
        pts_ = [(x, y)]
        for _ in range(4):
            x += pts.normal(0, 5); y += pts.normal(0, 2)
            pts_.append((x, y))
        cd.line(pts_, fill=int(pts.uniform(80, 180)), width=1)
    c = np.asarray(crack) / 255.0
    return np.clip(img * (1 - 0.5 * c[..., None]), 0, 1)


def save(name, rgb, alpha):
    a = Image.fromarray((np.clip(alpha, 0, 1) * 255).astype(np.uint8))
    im = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8))
    im.putalpha(a)
    im.save(os.path.join(OUT, name + ".png"))
    print(f"[hud] {name} {im.size}")


def soft(m, r):
    return np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r))) / 255.0


# --- the compass bar
def compass_bar():
    W, H = 260 * S, 22 * S
    cy = H / 2
    tip, shoulder = 6, 46                        # the points' tips and where they meet the band
    top, bot = 8, H - 8
    outline = [(tip, cy), (shoulder, top), (W - shoulder, top), (W - tip, cy), (W - shoulder, bot), (shoulder, bot)]
    inset = 10
    inner = [(tip + inset * 2.6, cy), (shoulder + inset * 0.4, top + inset), (W - shoulder - inset * 0.4, top + inset),
             (W - tip - inset * 2.6, cy), (W - shoulder - inset * 0.4, bot - inset), (shoulder + inset * 0.4, bot - inset)]
    outer_m = mask(W, H, lambda d: d.polygon(outline, fill=255))
    inner_m = mask(W, H, lambda d: d.polygon(inner, fill=255))
    rim = brass(H, W, 11)
    band = leather(H, W, 21)
    # the band: darker at its top edge (the rim's shadow on it), a faint warm sheen along its middle
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    v = (yy - (top + inset)) / max(1, (bot - inset) - (top + inset))
    band = band * (0.65 + 0.35 * np.clip(v * 3, 0, 1))[..., None] * (1 + 0.15 * np.exp(-((v - 0.55) / 0.2) ** 2))[..., None]
    img = rim * (1 - inner_m[..., None]) + band * inner_m[..., None]
    # the rim's edges: a bright worn line on its outer top, dark round its inner edge (bevel)
    edge_out = np.clip(outer_m - soft(outer_m, 2.0) * 1.0, 0, 1)
    img = img + edge_out[..., None] * np.array([0.25, 0.2, 0.1]) * (yy < cy)[..., None]
    inner_edge = np.clip(soft(inner_m, 2.5) - inner_m, 0, 1) + np.clip(inner_m - soft(inner_m, 2.5), 0, 1)
    img = img * (1 - 0.55 * inner_edge[..., None])
    # an engraved chevron in each point
    eng = mask(W, H, lambda d: (d.line([(tip + 22, cy), (shoulder + 2, top + 3)], fill=255, width=2),
                                d.line([(tip + 22, cy), (shoulder + 2, bot - 3)], fill=255, width=2),
                                d.line([(W - tip - 22, cy), (W - shoulder - 2, top + 3)], fill=255, width=2),
                                d.line([(W - tip - 22, cy), (W - shoulder - 2, bot - 3)], fill=255, width=2)))
    img = img * (1 - 0.6 * eng[..., None] * (1 - inner_m[..., None]))
    # rivets: domed, a highlight up-left
    for rx in (shoulder + 18, W - shoulder - 18):
        for ry in (top + 5, bot - 5):
            d = np.sqrt((xx - rx) ** 2 + (yy - ry) ** 2)
            dome = np.clip(1 - d / 6.5, 0, 1)
            hl = np.clip(1 - np.sqrt((xx - rx + 2) ** 2 + (yy - ry + 2) ** 2) / 2.6, 0, 1)
            img = img * (1 - dome[..., None] * 0.45) + np.array([0.5, 0.38, 0.18]) * dome[..., None] * 0.45 + hl[..., None] * 0.35
    # the band a touch see-through (the scene's dark behind it), the rim solid; a soft drop shadow under it all
    alpha = outer_m * (1 - inner_m * 0.12)
    shadow = soft(outer_m, 5) * 0.55
    sh = np.roll(shadow, 3, axis=0)
    final_a = np.maximum(alpha, sh * (1 - outer_m))
    img = img * outer_m[..., None]
    save("compass_bar", img, final_a)


def compass_pointer():
    W, H = 16 * S, 12 * S
    pts = [(W * 0.5, H - 3), (5, 6), (W - 5, 6)]
    m = mask(W, H, lambda d: d.polygon(pts, fill=255))
    m = soft(m, 0.6)
    img = brass(H, W, 31)
    edge = np.clip(m - soft(m, 1.5), 0, 1)
    img = img * (1 - 0.5 * edge[..., None])
    hl = mask(W, H, lambda d: d.line([(8, 8), (W * 0.5, H - 7)], fill=255, width=2))
    img = img + hl[..., None] * 0.25
    sh = np.roll(soft(m, 2.5), 2, axis=0) * 0.5
    save("compass_pointer", img, np.maximum(m, sh * (1 - m)))


def keycap():
    W = H = 24 * S
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    c = W / 2
    d = np.sqrt((xx - c) ** 2 + (yy - c) ** 2)
    R = W / 2 - 3
    ring = np.clip((R - d) + 0.5, 0, 1)
    face = np.clip((R - 9 - d) + 0.5, 0, 1)
    # the nickel ring: cool grey metal, lit from the top, pitted
    metal = np.array([0.62, 0.62, 0.6]) * (1.2 - 0.6 * (yy / H))[..., None] * (0.85 + 0.3 * noise(H, W, 20, 2, 41))[..., None]
    pit = noise(H, W, 40, 2, 42) > 0.78
    metal = np.where(pit[..., None], metal * 0.6 + np.array([0.3, 0.22, 0.12]) * 0.4, metal)
    # the face: yellowed celluloid, dished (darker to its rim, a soft highlight high up), grime in the dish's edge
    cel = np.array([0.86, 0.8, 0.62]) * (0.92 + 0.12 * noise(H, W, 8, 3, 43))[..., None]
    dish = np.clip(d / (R - 9), 0, 1)
    cel = cel * (1 - 0.25 * dish[..., None] ** 2)
    hl = np.clip(1 - np.sqrt((xx - c + 6) ** 2 + (yy - c + 10) ** 2) / 18, 0, 1) * 0.18
    cel = cel + hl[..., None]
    grime = np.clip((dish - 0.75) * 4, 0, 1) * face
    cel = cel * (1 - 0.3 * grime[..., None])
    img = metal * (1 - face[..., None]) + cel * face[..., None]
    sh = np.roll(soft(ring, 2.5), 3, axis=0) * 0.6
    save("keycap", img * ring[..., None], np.maximum(ring, sh * (1 - ring)))


def item_strip():
    W, H = 120 * S, 28 * S
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    shape = mask(W, H, lambda d: d.rounded_rectangle((0, 6, W - 6, H - 6), radius=10, fill=255))
    img = leather(H, W, 51, base=(0.12, 0.09, 0.065))
    # stitching along both edges
    st = Image.new("L", (W, H), 0)
    sd = ImageDraw.Draw(st)
    for y in (12, H - 12):
        for x in range(10, W - 16, 9):
            sd.line([(x, y), (x + 5, y)], fill=200, width=2)
    stitch = np.asarray(st) / 255.0
    img = img * (1 - stitch[..., None]) + np.array([0.55, 0.46, 0.32]) * stitch[..., None]
    # its right end: a brass rivet, the leather's edge darker
    d = np.sqrt((xx - (W - 18)) ** 2 + (yy - H / 2) ** 2)
    rv = np.clip(1 - d / 13, 0, 1)
    img = img * (1 - rv[..., None]) + brass(H, W, 52) * rv[..., None]
    edge = np.clip(shape - soft(shape, 2), 0, 1)
    img = img * (1 - 0.5 * edge[..., None])
    fade = np.clip(xx / (W * 0.55), 0, 1) ** 1.4               # fades out to the left
    save("item_strip", img, shape * fade * 0.92)


def stamina_groove():
    W, H = 100 * S, 7 * S
    shape = mask(W, H, lambda d: d.rounded_rectangle((2, 2, W - 2, H - 2), radius=12, fill=255))
    img = brass(H, W, 61) * 0.55
    inner = mask(W, H, lambda d: d.rounded_rectangle((7, 8, W - 7, H - 8), radius=6, fill=255))
    img = img * (1 - inner[..., None]) + np.array([0.04, 0.035, 0.03]) * inner[..., None]
    lip = np.clip(soft(inner, 1.5) - inner, 0, 1)
    img = img * (1 - 0.4 * lip[..., None])
    xx = np.mgrid[0:H, 0:W][1]
    fade = np.clip(np.minimum(xx, W - xx) / 30, 0, 1)
    save("stamina_groove", img, shape * (0.7 + 0.3 * fade) * 0.85)


compass_bar()
compass_pointer()
keycap()
item_strip()
stamina_groove()


# ---- the menus' controls (2026-10-25): a checkbox as a little brass-framed leather well, ticked with a red ink cross;
# the sliders' knob a brass slider-plate (the sliders' grooves are StyleBoxes, UiKit)
def check(name, on, alpha=1.0):
    W = H = 12 * S
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    outer = mask(W, H, lambda d: d.rounded_rectangle((3, 3, W - 4, H - 4), radius=6, fill=255))
    inner = mask(W, H, lambda d: d.rounded_rectangle((9, 9, W - 10, H - 10), radius=3, fill=255))
    img = brass(H, W, 71) * (1 - inner[..., None]) + leather(H, W, 72) * inner[..., None]
    img = img * (1 - 0.5 * np.clip(soft(inner, 1.5) - inner, 0, 1)[..., None])
    if on:
        ink = Image.new("L", (W, H), 0)
        d = ImageDraw.Draw(ink)
        d.line([(13, 14), (W - 13, H - 12)], fill=255, width=5)
        d.line([(W - 14, 12), (14, H - 14)], fill=255, width=5)
        m = np.asarray(ink.filter(ImageFilter.GaussianBlur(0.8))) / 255.0 * (0.7 + 0.3 * noise(H, W, 10, 2, 73))
        img = img * (1 - m[..., None]) + np.array([0.78, 0.16, 0.1]) * m[..., None]
    save(name, img, outer * alpha)


def knob(name, bright):
    W, H = 6 * S, 11 * S
    shape = mask(W, H, lambda d: d.rounded_rectangle((2, 2, W - 3, H - 3), radius=6, fill=255))
    img = brass(H, W, 81) * (1.25 if bright else 1.0)
    groove = mask(W, H, lambda d: [d.line([(6, y), (W - 7, y)], fill=255, width=1) for y in (16, 21, 26)])
    img = img * (1 - 0.5 * groove[..., None])
    edge = np.clip(shape - soft(shape, 1.5), 0, 1)
    img = img * (1 - 0.45 * edge[..., None])
    save(name, img, shape)


check("check_off", False)
check("check_on", True)
check("check_off_dim", False, 0.4)
check("check_on_dim", True, 0.4)
knob("slider_knob", False)
knob("slider_knob_hi", True)


# ---- (2026-10-31, the owner: "retexture the compass ... give it a tree bark texture ... with some of that mossy green
# texture on the bottom", with an oak's bark mossed over at its foot; "add a frosted effect to the compass tree bark
# texture" for the snow, with bark under a dusting of snow and lichen) the band as a length of oak branch: deep-fissured
# plates running along it, a darker smoothed channel down its middle where the marks are cut, moss thick along its
# lower edge and climbing into the cracks; and its frosted twin
def bark_field(H, W, seed):
    """Oak bark's height: long ridged plates (stretched along the branch), broken across by cracks."""
    ridges = noise(H * 4, W, 16, 4, seed)[::4]                          # long along u
    plates = noise(H, W, 9, 3, seed + 1)
    hgt = ridges * 0.7 + plates * 0.3
    fiss = np.abs(noise(H * 3, W, 40, 2, seed + 2)[::3] - 0.5) < 0.05  # fissures
    hgt = hgt - fiss * 0.35
    return hgt


def bark_rgb(hgt, H, W, seed):
    gy, gx = np.gradient(hgt * 60)
    shade = np.clip(1 + (-gx * 0.6 - gy * 0.9) * 0.8, 0.3, 1.7)
    base = np.array([0.42, 0.38, 0.33])
    tone = noise(H, W, 6, 3, seed + 3)
    img = base * (0.7 + 0.5 * tone)[..., None] * shade[..., None]
    deep = np.clip((0.42 - hgt) * 3, 0, 1)                               # the cracks: dark, a little red-brown
    img = img * (1 - 0.75 * deep[..., None]) + np.array([0.12, 0.06, 0.04]) * 0.75 * deep[..., None]
    lichen = (noise(H, W, 60, 2, seed + 4) > 0.78) & (hgt > 0.55)
    img = np.where(lichen[..., None], img * 0.5 + np.array([0.55, 0.6, 0.45]) * 0.5, img)
    return img, shade


PH = os.path.join(ROOT, "build", "polyhaven")


def photo_strip(name, W, H, scale, seed):
    """A strip W x H cut from a Poly Haven photo (its colour and its normal map), the photo tiled (mirrored at the seams)
    at scale px per photo-px."""
    col = Image.open(os.path.join(PH, name + "_col.jpg")).convert("RGB")
    nor = Image.open(os.path.join(PH, name + "_nor.jpg")).convert("RGB")
    size = int(col.width * scale)
    col, nor = col.resize((size, size), Image.LANCZOS), nor.resize((size, size), Image.LANCZOS)
    def tile(im):
        row = Image.new("RGB", (size * 2, size))
        row.paste(im, (0, 0))
        row.paste(im.transpose(Image.FLIP_LEFT_RIGHT), (size, 0))
        while row.width < W:
            wide = Image.new("RGB", (row.width * 2, size)); wide.paste(row, (0, 0)); wide.paste(row, (row.width, 0)); row = wide
        y0 = np.random.default_rng(seed).integers(0, size - H)
        return np.asarray(row.crop((0, y0, W, y0 + H))).astype(float) / 255.0
    return tile(col), tile(nor)


def stylize(img):
    """(2026-10-31, the owner: the bark "stylized with our games style to make it feel less real world and more of the
    game") A photo brought into the game's look: its fine noise smoothed into painted patches (a median), the colour
    stepped into a few bands (the PS2 era's posterized textures), the grade's lift and warmth, and the fissures inked
    darker."""
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(5))
    p = np.asarray(im).astype(float) / 255.0
    levels = 9.0
    p = np.floor(p * levels + 0.5) / levels
    luma = p.mean(-1, keepdims=True)
    p = p * 0.82 + luma * 0.18                                   # (a touch less colour)
    p = p * np.array([1.05, 1.0, 0.92])                          # (warmer)
    ink = luma[..., 0] < 0.16
    p = np.where(ink[..., None], p * 0.55, p)
    return np.clip(p, 0, 1)


def compass_bark(name, frost=False):
    """(2026-10-31, the owner, again: "make the texture look way more real like the reference materials", "less of the green
    moss along the bottom, kind of segment it", the inside "veiny ... just make it opaque so its not as cluttered"; the
    winter one "in a higher resolution ... remove the green moss") The band cut from a photographed bark (Poly Haven's,
    CC0: brown oak-like for the woods, a grey willow for the snow), lit by its own normal map so the fissures go deep; a
    plain, opaque channel of dark worn wood down its middle where the marks are cut; moss in a few separate clumps along its
    foot (the woods' only); in the snow, snow caught on every upward ledge of the bark and rime on it. 6x the size it's
    drawn at."""
    R = 6
    W, H = 260 * R, 17 * R
    cy = H / 2
    tip, shoulder = 9, 52
    top, bot = 7, H - 7
    outline = [(tip, cy), (shoulder, top), (W - shoulder, top), (W - tip, cy), (W - shoulder, bot), (shoulder, bot)]
    inset = 13
    inner = [(tip + inset * 2.6, cy), (shoulder + inset * 0.4, top + inset), (W - shoulder - inset * 0.4, top + inset),
             (W - tip - inset * 2.6, cy), (W - shoulder - inset * 0.4, bot - inset), (shoulder + inset * 0.4, bot - inset)]
    outer_m = mask(W, H, lambda d: d.polygon(outline, fill=255))
    inner_m = soft(mask(W, H, lambda d: d.polygon(inner, fill=255)), 1.5)
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    bark, nor = photo_strip("bark_willow_02" if frost else "bark_brown_02", W, H, 0.42, 7 if frost else 3)
    n = nor * 2 - 1
    light = np.clip(0.75 + 0.55 * (-n[..., 0] * 0.4 + n[..., 1] * 0.75), 0.35, 1.5)   # (lit from above, a touch left)
    img = np.clip(bark * light[..., None] * 1.05, 0, 1)
    img = (img - 0.5) * 1.15 + 0.5                                              # (a little more bite)
    # the channel: opaque, plain, dark worn wood (a faint grain, a shadow under its top lip)
    grain = noise(H * 4, W, 60, 2, 41)[::4]
    chan = np.array([0.075, 0.058, 0.045]) * (0.9 + 0.2 * grain)[..., None]
    lip = np.clip((yy - (top + inset)) / 6.0, 0, 1)
    chan = chan * (0.55 + 0.45 * lip)[..., None]
    img = img * (1 - inner_m[..., None]) + chan * inner_m[..., None]
    if not frost:
        # moss: a few separate clumps along the foot, fuzzy-edged, not a strip
        r = np.random.default_rng(11)
        moss_m = np.zeros((H, W))
        fuzz = noise(H, W, 220, 2, 12)
        for _ in range(5):
            cx = r.uniform(0.08, 0.92) * W
            wdt = r.uniform(70, 200)
            hgt = r.uniform(0.35, 0.6) * H
            d = ((xx - cx) / wdt) ** 2 + ((yy - H) / hgt) ** 2
            moss_m = np.maximum(moss_m, np.clip((1 - d) * 3 + (fuzz - 0.5) * 2.4, 0, 1))
        moss_m *= (1 - inner_m)
        mcol = np.array([0.24, 0.38, 0.09]) * (0.45 + 0.75 * fuzz)[..., None] * light[..., None]
        mcol = mcol + np.array([0.08, 0.11, 0.0]) * (fuzz > 0.72)[..., None]
        img = img * (1 - 0.85 * moss_m[..., None]) + mcol * 0.85 * moss_m[..., None]
    else:
        # snow on the ledges (the normal map's upward faces), heavier along the top edge, and rime on the rest
        up = np.clip(n[..., 1] * 1.6 - 0.2, 0, 1)
        sn = np.clip(up * 1.3 + np.clip((0.35 - yy / H) * 2.5, 0, 1) + (noise(H, W, 90, 3, 13) - 0.6) * 1.2, 0, 1) * (1 - inner_m)
        snow = np.array([0.9, 0.93, 0.97]) * (0.85 + 0.15 * noise(H, W, 120, 2, 14))[..., None]
        img = img * (1 - sn[..., None]) + snow * sn[..., None]
        rime = (np.random.default_rng(15).random((H, W)) > 0.94) * 0.5 * (1 - inner_m)
        img = img * (1 - rime[..., None]) + np.array([0.85, 0.9, 0.95]) * rime[..., None]
    img = stylize(img) * (1 - inner_m[..., None]) + img * inner_m[..., None]   # (the bark only: the channel stays plain)
    edge = np.clip(outer_m - soft(outer_m, 3.5), 0, 1)
    img = img * (1 - 0.55 * edge[..., None])
    shadow = np.roll(soft(outer_m, 7), 4, axis=0) * 0.55
    save(name, img * outer_m[..., None], np.maximum(outer_m, shadow * (1 - outer_m)))


def keycap_square():
    """A keyboard key cap: a square, sculpted top dished a little, a lighter lip all round, its skirt darker, worn
    shiny in the middle (the owner: "make them look like keyboard key caps")."""
    W = H = 24 * S
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    outer = mask(W, H, lambda d: d.rounded_rectangle((3, 4, W - 4, H - 2), radius=12, fill=255))
    top = mask(W, H, lambda d: d.rounded_rectangle((12, 9, W - 13, H - 15), radius=10, fill=255))
    skirt = np.array([0.16, 0.16, 0.17]) * (1.0 - 0.35 * (yy / H))[..., None]
    face = np.array([0.27, 0.27, 0.29]) * (0.9 + 0.2 * noise(H, W, 30, 2, 111))[..., None]
    cx, cyy = W / 2, H * 0.42
    dish = np.clip(np.sqrt(((xx - cx) / (W * 0.36)) ** 2 + ((yy - cyy) / (H * 0.32)) ** 2), 0, 1)
    face = face * (0.85 + 0.25 * dish)[..., None] + np.array([0.06, 0.06, 0.07]) * np.clip(1 - dish * 1.4, 0, 1)[..., None]
    lip = np.clip(soft(top, 1.2) - top * 0.0, 0, 1) - top
    img = skirt * (1 - top[..., None]) + face * top[..., None]
    img = img + np.clip(lip, 0, 1)[..., None] * 0.12
    shadow = np.roll(soft(outer, 3), 3, axis=0) * 0.6
    save("keycap_square", img * outer[..., None], np.maximum(outer, shadow * (1 - outer)))


compass_bark("compass_bark")
compass_bark("compass_bark_frost", frost=True)
keycap_square()
