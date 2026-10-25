"""Painted, weathered surfaces for the weathered props (2026-10-24, the owner: "we should try and populate the game with more
weathered objects to make it feel more lived in", with photos of old coffee cans, spice and tobacco tins, film canisters,
painted signs, copper kettles, crocks, a rusted watering can and a burn barrel).

    python tools/Textures/make_weathered.py

Writes assets/models/items/weathered_tex/<name>.png, read by tools/Blender/weathered.py. Every brand is made up. The
wear is the references': the colour faded (most where the light fell), grime toward the foot, the print worn off the
high spots, the paint chipped back to the tin at the rims and the seam and anywhere it was knocked, rust blooming where
the tin's bare, scratches, and on the oldest the paint crazed. Seeded: the same every run.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "assets", "models", "items", "weathered_tex")
FONT = os.path.join(ROOT, "assets", "fonts", "Anton-Regular.ttf")
os.makedirs(OUT, exist_ok=True)


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


def font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except OSError:
        return ImageFont.load_default()


def text_mask(w, h, items):
    """Text drawn into a mask: items are (x, y, text, size, value, centre)."""
    im = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(im)
    for (x, y, t, size, val, centre) in items:
        f = font(size)
        if centre:
            bb = d.textbbox((0, 0), t, font=f)
            x = x - (bb[2] - bb[0]) / 2
        d.text((x, y), t, font=f, fill=int(val * 255))
    return np.asarray(im) / 255.0


def tin(h, w, seed):
    """Bare tinplate gone dull: grey with a warm tarnish in clouds."""
    t = noise(h, w, 4, 4, seed)
    b = noise(h, w, 60, 2, seed + 1) * 0.08
    return np.stack([0.6 - 0.2 * t + b, 0.58 - 0.23 * t + b, 0.53 - 0.27 * t + b], -1)


def rust(h, w, seed):
    """Rust: deep brown to orange, pitted."""
    n = noise(h, w, 12, 4, seed)
    p = noise(h, w, 50, 2, seed + 1)
    return np.stack([0.26 + 0.2 * n, 0.13 + 0.09 * n, 0.07 + 0.04 * n], -1) * (0.7 + 0.4 * p[..., None])


def weather(paint, seed, fade=0.35, grime=0.4, chips=0.3, rusty=0.5, craze=0.0, rim_rows=None, seam_col=None, sun_top=True):
    """The wear laid over a painted label: returns the weathered RGB."""
    h, w, _ = paint.shape
    yy, xx = np.mgrid[0:h, 0:w]
    v = yy / (h - 1)
    # faded: toward a chalky pale, in clouds, more at the top (where the light fell)
    pale = np.array([0.86, 0.83, 0.74])
    f = np.clip(fade * (0.6 + 0.8 * noise(h, w, 5, 3, seed)) * ((1.2 - v) if sun_top else 1.0), 0, 0.85)
    out = paint * (1 - f[..., None]) + pale * f[..., None]
    out = out * (0.86 + 0.24 * noise(h, w, 30, 2, seed + 2))[..., None]
    # grime toward the foot
    g = np.clip((v - 0.55) * 2.2, 0, 1) * grime * (0.5 + 0.6 * noise(h, w, 9, 3, seed + 3))
    out = out * (1 - 0.55 * g[..., None]) + np.array([0.16, 0.12, 0.07]) * 0.55 * g[..., None]
    # the bare tin: chipped at the rims and the seam and where knocked; rust in the bare places
    bare = noise(h, w, 24, 3, seed + 4) * 0.7 + noise(h, w, 70, 2, seed + 5) * 0.45
    edge = np.zeros((h, w))
    if rim_rows:
        for (r0, r1) in rim_rows:
            d = np.minimum(np.abs(yy - r0), np.abs(yy - r1)) / max(1, h * 0.035)
            edge = np.maximum(edge, np.clip(1 - d, 0, 1))
    if seam_col is not None:
        edge = np.maximum(edge, np.clip(1 - np.abs(xx - seam_col) / (w * 0.04), 0, 1))
    chipped = bare + edge * 0.5 > 1.02 - chips * 0.35
    chip_rim = (bare + edge * 0.5 > 0.99 - chips * 0.35) & ~chipped
    out = np.where(chip_rim[..., None], out * 0.6, out)
    metal = tin(h, w, seed + 6)
    rr = rust(h, w, seed + 7)
    rusted = noise(h, w, 16, 3, seed + 8) < rusty
    out = np.where(chipped[..., None], np.where(rusted[..., None], rr, metal), out)
    # rust bleeding out round the bare places (a brown halo)
    halo = chip_rim & rusted
    out = np.where(halo[..., None], out * 0.65 + rr * 0.35, out)
    # scratches
    sc = Image.new("L", (w, h), 0)
    sd = ImageDraw.Draw(sc)
    r = np.random.default_rng(seed + 9)
    for _ in range(int(w * h / 4000)):
        x, y = r.uniform(0, w), r.uniform(0, h)
        a, L = r.uniform(-0.6, 0.6) + (np.pi / 2 if r.random() < 0.3 else 0), r.uniform(6, w * 0.08)
        sd.line([(x, y), (x + L * np.cos(a), y + L * np.sin(a))], fill=int(r.uniform(60, 190)), width=1)
    s = np.asarray(sc)[..., None] / 255.0
    out = out * (1 - s * 0.5) + metal * s * 0.5
    if craze > 0:
        pts = np.stack([r.random(220) * w, r.random(220) * h], 1)
        d = np.sqrt((xx[..., None] - pts[:, 0]) ** 2 + (yy[..., None] - pts[:, 1]) ** 2)
        d.sort(axis=2)
        cr = (d[..., 1] - d[..., 0] < 0.9) & (noise(h, w, 4, 2, seed + 10) > 1 - craze)
        out = np.where(cr[..., None], out * 0.45, out)
    return np.clip(out, 0, 1)


def save(name, rgb):
    Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, name + ".png"))
    print(f"[weathered] {name}")


def bands(h, w, spec):
    """A label of horizontal bands: spec is [(v0, v1, rgb)], top to bottom."""
    img = np.zeros((h, w, 3))
    for (v0, v1, c) in spec:
        img[int(v0 * h):int(v1 * h)] = c
    return img


def ink(img, mask, rgb):
    return img * (1 - mask[..., None]) + np.array(rgb) * mask[..., None]


def coffee(name, base, band, letters, brand, sub, seed):
    h, w = 256, 768
    img = bands(h, w, [(0, 0.1, band), (0.1, 0.9, base), (0.9, 1.0, band)])
    # the brand twice round the can (one face each side), a roundel between
    items = []
    for cx in (w * 0.25, w * 0.75):
        items += [(cx, 52, brand, 64, 1, True), (cx, 140, sub, 34, 0.95, True), (cx, 190, "VACUUM PACKED  ·  1 LB NET", 16, 0.9, True)]
    m = text_mask(w, h, items)
    img = ink(img, m, letters)
    r = text_mask(w, h, [(w * 0.5, 96, "FINE GRIND", 22, 1, True), (0, 96, "DRIP", 22, 1, True)])
    img = ink(img, r, band)
    save(name, weather(img, seed, fade=0.35, grime=0.35, chips=0.35, rusty=0.6, rim_rows=[(0, 26), (230, 255)], seam_col=w - 4))


coffee("coffee_red", np.array([0.62, 0.09, 0.07]), np.array([0.85, 0.72, 0.28]), (0.95, 0.9, 0.78), "HILLCREST", "COFFEE", 101)
coffee("coffee_blue", np.array([0.17, 0.32, 0.6]), np.array([0.9, 0.86, 0.74]), (0.95, 0.93, 0.85), "BLUE RIDGE", "COFFEE", 102)
coffee("coffee_yellow", np.array([0.86, 0.68, 0.18]), np.array([0.1, 0.09, 0.08]), (0.12, 0.1, 0.08), "MORNING STAR", "COFFEE", 103)


# the spice tin: four faces round a rectangular tin, the front printed, a portrait in an oval
def spice():
    h, w = 256, 512
    img = bands(h, w, [(0, 1, np.array([0.8, 0.42, 0.24]))])
    img[:, :] = img * 0.9 + np.array([0.86, 0.78, 0.62]) * 0.1
    yy, xx = np.mgrid[0:h, 0:w]
    oval = ((xx - 64) / 32) ** 2 + ((yy - 70) / 42) ** 2 < 1
    img = np.where(oval[..., None], np.array([0.86, 0.78, 0.6]), img)
    m = text_mask(w, h, [(64, 120, "PURE", 18, 1, True), (64, 140, "GROUND", 18, 1, True), (64, 160, "CINNAMON", 30, 1, True),
                         (192, 120, "RAWLINS", 26, 1, True), (192, 160, "& SONS", 20, 1, True), (320, 40, "NET WT 2 OZ", 18, 0.9, True)])
    img = ink(img, m, (0.35, 0.08, 0.05))
    save("spice_cinnamon", weather(img, 201, fade=0.5, grime=0.5, chips=0.45, rusty=0.7, rim_rows=[(0, 18), (238, 255)], craze=0.3))


spice()


def flat(name, base, band, text, sub, seed, letters=(0.95, 0.9, 0.7)):
    """A round flat tin's lid (a disc) and its side band in one image: the lid top square, the band below."""
    h, w = 512, 512
    img = np.zeros((h, w, 3)) + base
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((xx - 256) ** 2 + (yy - 192) ** 2)
    ring = (d > 160) & (d < 176)
    img = np.where(ring[..., None], band, img)
    m = text_mask(w, h, [(256, 120, text, 60, 1, True), (256, 210, sub, 26, 0.9, True), (256, 420, text + "  " + sub, 30, 0.9, True)])
    img = ink(img, m, letters)
    img[384:] = img[384:] * 0.9 + band * 0.1
    save(name, weather(img, seed, fade=0.4, grime=0.2, chips=0.4, rusty=0.6, rim_rows=[(380, 392), (500, 511)], craze=0.2))


flat("tobacco_green", np.array([0.3, 0.42, 0.26]), np.array([0.76, 0.62, 0.3]), "OLD TRAIL", "FINE CUT", 301)
flat("tea_teal", np.array([0.45, 0.66, 0.62]), np.array([0.82, 0.68, 0.38]), "CEYLON", "BLENDED TEA", 302, letters=(0.95, 0.92, 0.82))


def canister(name, base, letters, brand, seed):
    h, w = 128, 256
    img = np.zeros((h, w, 3)) + base
    m = text_mask(w, h, [(w * 0.3, 18, brand, 46, 1, True), (w * 0.72, 30, "135-36", 22, 0.9, True)])
    img = ink(img, m, letters)
    save(name, weather(img, seed, fade=0.3, grime=0.25, chips=0.25, rusty=0.25, rim_rows=[(0, 6), (121, 127)]))


canister("film_brown", np.array([0.42, 0.2, 0.1]), (0.92, 0.72, 0.18), "PANATON", 401)
canister("film_orange", np.array([0.9, 0.42, 0.1]), (0.97, 0.94, 0.86), "ASA 50", 402)
canister("film_green", np.array([0.32, 0.5, 0.2]), (0.9, 0.78, 0.2), "SUPER-X", 403)


def matchbox(name, base, accent, word, seed):
    h, w = 128, 192
    img = np.zeros((h, w, 3)) + base
    img[10:118, 10:182] = accent
    img[18:110, 18:174] = base
    m = text_mask(w, h, [(96, 26, word, 40, 1, True), (96, 78, "SAFETY MATCHES", 14, 1, True)])
    img = ink(img, m, accent)
    save(name, weather(img, seed, fade=0.45, grime=0.3, chips=0.15, rusty=0.0, sun_top=False))


matchbox("match_a", np.array([0.85, 0.75, 0.45]), np.array([0.65, 0.12, 0.08]), "LION", 501)
matchbox("match_b", np.array([0.2, 0.35, 0.55]), np.array([0.92, 0.82, 0.4]), "NORTH", 502)
matchbox("match_c", np.array([0.85, 0.3, 0.12]), np.array([0.12, 0.1, 0.08]), "BEACON", 503)


# the burn barrel: rust all over, a ghost of old blue paint in patches, the ribs darker, soot from the top
def barrel():
    h, w = 512, 1024
    yy, xx = np.mgrid[0:h, 0:w]
    v = yy / (h - 1)
    rr = rust(h, w, 601)
    paint = np.array([0.22, 0.3, 0.42])
    left = noise(h, w, 6, 4, 602) > 0.7
    img = np.where(left[..., None], paint * (0.7 + 0.3 * noise(h, w, 30, 2, 603))[..., None], rr)
    for rib in (0.33, 0.66):
        d = np.abs(v - rib) < 0.02
        img = np.where(d[..., None], img * 0.55, img)
    soot = np.clip((0.35 - v) * 3, 0, 1) * (0.6 + 0.4 * noise(h, w, 10, 3, 604))
    img = img * (1 - 0.8 * soot[..., None])
    streak = (noise(h, w * 2, 40, 2, 605)[:, ::2] > 0.6) & (noise(h, w, 3, 2, 606) > 0.5)
    img = np.where(streak[..., None], img * 0.75, img)
    save("burn_barrel", img)


barrel()


# the copper kettle: copper, darkened, verdigris blooming in spots and where water ran
def kettle():
    h, w = 512, 512
    n = noise(h, w, 5, 4, 701)
    copper = np.stack([0.66 - 0.14 * n, 0.34 - 0.08 * n, 0.24 - 0.05 * n], -1)
    copper = copper * (0.88 + 0.2 * noise(h, w, 40, 2, 702))[..., None]
    verd = np.array([0.32, 0.55, 0.48])
    spots = noise(h, w, 30, 3, 703) > 0.72
    run = (noise(h, w * 3, 30, 2, 704)[:, ::3] > 0.62) & (np.mgrid[0:h, 0:w][0] > h * 0.45)
    img = np.where((spots | run)[..., None], copper * 0.45 + verd * 0.55, copper)
    soot = np.clip((np.mgrid[0:h, 0:w][0] / h - 0.75) * 4, 0, 1)
    img = img * (1 - 0.7 * soot[..., None])
    save("kettle_copper", img)


kettle()


# the crock: terracotta, the glaze worn off the high spots, a painted dark band and a squiggle
def crock():
    h, w = 512, 1024
    yy, xx = np.mgrid[0:h, 0:w]
    v = yy / (h - 1)
    n = noise(h, w, 6, 4, 801)
    terra = np.stack([0.68 - 0.12 * n, 0.4 - 0.08 * n, 0.24 - 0.05 * n], -1)
    glaze = noise(h, w, 10, 3, 802) > 0.45
    img = terra * (0.9 + 0.12 * noise(h, w, 10, 3, 802))[..., None]
    band = (np.abs(v - 0.42) < 0.012) | (np.abs(v - 0.46 - 0.04 * np.sin(xx / w * np.pi * 8)) < 0.01)
    img = np.where(band[..., None], np.array([0.16, 0.1, 0.07]), img)
    chip = noise(h, w, 60, 2, 803) > 0.84
    img = np.where(chip[..., None], np.array([0.78, 0.66, 0.52]), img)
    img = img * (1 - 0.35 * np.clip((v - 0.8) * 4, 0, 1))[..., None]
    save("crock_terracotta", img)


crock()


# the watering can: old galvanized tin gone dull, the rust come up through it, worst toward the foot where it stood
# wet (blended, not in hard blobs: it read as camouflage), a few last flecks of its pale green paint
def can():
    h, w = 512, 1024
    yy = np.mgrid[0:h, 0:w][0] / (h - 1)
    rr = rust(h, w, 901)
    zinc = tin(h, w, 904) * 0.9 + noise(h, w, 90, 2, 905)[..., None] * 0.06     # spangled, grey
    amt = noise(h, w, 10, 5, 903) * 0.6 + noise(h, w, 45, 3, 906) * 0.4 + (0.55 - yy) * 0.7     # (the model's v runs foot to top)
    m = np.clip((amt - 0.42) * 3.5, 0, 1)[..., None]
    img = zinc * (1 - m) + rr * m
    fleck = (noise(h, w, 60, 2, 902) > 0.74) & (m[..., 0] < 0.35)
    img = np.where(fleck[..., None], img * 0.5 + np.array([0.5, 0.62, 0.52]) * 0.5, img)
    save("watering_can", img)


can()


# ---- the second batch (2026-10-24, the owner: "painted signs, a filing cabinet and cowbells")
def wood(h, w, seed, base=(0.6, 0.56, 0.5)):
    """Old board gone silver-grey: grain along u, darker in the rings, checked along the grain."""
    yy, xx = np.mgrid[0:h, 0:w]
    grain = noise(h * 8, w, 6, 4, seed)[::8] * 0.6 + noise(h, w // 8, 40, 2, seed + 1).repeat(8, 1)[:, :w] * 0.4
    rings = 0.5 + 0.5 * np.sin(grain * 40)
    img = np.array(base) * (0.75 + 0.3 * rings)[..., None] * (0.85 + 0.2 * noise(h, w, 8, 3, seed + 2))[..., None]
    check = (np.abs(np.sin(grain * 23 + yy / h * 3)) < 0.03) & (noise(h, w, 6, 2, seed + 3) > 0.55)
    return np.where(check[..., None], img * 0.4, img)


def wood_weather(paint, bare, seed, peel=0.35, fade=0.45, grime=0.3):
    """Paint on a board left out: faded chalky, peeled back to the grey wood in long flakes along the grain, grime
    low down, the edges of what's left darker where the weather got under."""
    h, w, _ = paint.shape
    yy = np.mgrid[0:h, 0:w][0] / (h - 1)
    f = np.clip(fade * (0.6 + 0.8 * noise(h, w, 5, 3, seed)), 0, 0.85)
    out = paint * (1 - f[..., None]) + np.array([0.86, 0.84, 0.78]) * f[..., None]
    streak = noise(h * 6, w, 18, 3, seed + 1)[::6] * 0.55 + noise(h, w, 30, 3, seed + 2) * 0.45     # long along u
    xx = np.mgrid[0:h, 0:w][1] / (w - 1)
    edge = np.clip(1 - np.minimum(np.minimum(xx, 1 - xx) * 6, np.minimum(yy, 1 - yy) * 4), 0, 1)
    streak = streak + 0.35 * noise(h, w, 3, 2, seed + 4) + 0.18 * edge     # worse in patches and toward the edges
    cut = np.quantile(streak, 1 - peel)                      # (peel: the share of the board gone to wood)
    gone = streak > cut
    rim = (streak > cut - 0.025) & ~gone
    out = np.where(rim[..., None], out * 0.62, out)
    out = np.where(gone[..., None], bare, out)
    g = np.clip((yy - 0.6) * 2.5, 0, 1) * grime * (0.5 + 0.6 * noise(h, w, 9, 3, seed + 3))
    out = out * (1 - 0.5 * g[..., None]) + np.array([0.14, 0.12, 0.08]) * 0.5 * g[..., None]
    return np.clip(out, 0, 1)


def sign_arrow():
    """A hand-lettered arrow board (the refs' painted signpost): white gone chalky, red letters, an edge line."""
    h, w = 256, 1024
    img = np.zeros((h, w, 3)) + np.array([0.9, 0.88, 0.82])
    img[10:22, :] = img[234:246, :] = np.array([0.55, 0.12, 0.08])
    m = text_mask(w, h, [(430, 36, "LAKE TRAIL", 112, 1, True), (430, 172, "1/2 MILE", 40, 1, True)])
    img = ink(img, m, (0.6, 0.12, 0.08))
    save("sign_arrow", wood_weather(img, wood(h, w, 1101), 1102, peel=0.14))


def sign_firewood():
    """A roadside board, green with cream letters, the honour box's terms under."""
    h, w = 512, 1024
    img = np.zeros((h, w, 3)) + np.array([0.18, 0.36, 0.26])
    img[24:36, 24:1000] = img[476:488, 24:1000] = np.array([0.88, 0.82, 0.62])
    m = text_mask(w, h, [(512, 60, "FIREWOOD", 200, 1, True), (512, 300, "$2 A BUNDLE", 80, 1, True), (512, 400, "HONOR BOX  ·  THANK YOU", 44, 0.95, True)])
    img = ink(img, m, (0.9, 0.85, 0.66))
    save("sign_firewood", wood_weather(img, wood(h, w, 1201), 1202, peel=0.12, fade=0.45))


def sign_tin():
    """An enamelled tin sign: cream, a red border and red letters; chipped to rust at the nail holes and the edges,
    crazed."""
    h, w = 256, 512
    img = np.zeros((h, w, 3)) + np.array([0.9, 0.86, 0.72])
    red = np.array([0.66, 0.1, 0.07])
    img[8:22, 8:504] = img[234:248, 8:504] = red
    img[8:248, 8:22] = img[8:248, 490:504] = red
    m = text_mask(w, h, [(256, 34, "NO", 70, 1, True), (256, 104, "TRESPASSING", 76, 1, True), (256, 194, "VIOLATORS WILL BE PROSECUTED", 22, 1, True)])
    img = ink(img, m, red)
    save("sign_tin", weather(img, 1301, fade=0.3, grime=0.35, chips=0.5, rusty=0.85, rim_rows=[(0, 10), (245, 255)], craze=0.35))


def filing():
    """A steel filing cabinet's green enamel (the refs' green cabinet): scuffed, chipped at the edges, rust in the chips;
    the image tiles a metre, the box mapped onto it."""
    h, w = 512, 512
    img = np.zeros((h, w, 3)) + np.array([0.3, 0.42, 0.33])
    img = img * (0.9 + 0.15 * noise(h, w, 6, 3, 1401))[..., None]
    save("filing_green", weather(img, 1402, fade=0.25, grime=0.25, chips=0.45, rusty=0.8, sun_top=False))


def cowbell(name, metal, seed, green=False):
    """A bell's sheet iron gone to rust, the black paint hanging on in flecks; or brass, dark, verdigris in the dents."""
    h, w = 256, 512
    n = noise(h, w, 6, 4, seed)
    base = np.array(metal) * (0.75 + 0.4 * n)[..., None]
    if green:
        verd = np.clip((noise(h, w, 30, 3, seed + 1) - 0.6) * 6, 0, 1)[..., None]
        base = base * (1 - 0.6 * verd) + np.array([0.3, 0.5, 0.42]) * 0.6 * verd
    else:
        rr = rust(h, w, seed + 2)
        paint = np.clip((noise(h, w, 40, 3, seed + 3) - 0.55) * 5, 0, 1)[..., None]
        base = rr * (1 - paint) + np.array([0.1, 0.09, 0.08]) * paint
    save(name, base)


sign_arrow()
sign_firewood()
sign_tin()
filing()
cowbell("cowbell_rust", (0.4, 0.22, 0.1), 1501)
cowbell("cowbell_brass", (0.62, 0.48, 0.24), 1502, green=True)
