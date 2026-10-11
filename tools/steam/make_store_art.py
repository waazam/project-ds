"""Steam store art (2026-10-11, the owner's list: "Steam store polish: capsules, screenshots, trailer, achievements").
Made from the game's own frames (test-output/story, written by the story autotest at 2560x1440): the 2.2:1 picture is cut
out of its letterbox (the bars carry the HUD), graded a touch, and the title set in Anton (OFL, assets/fonts).

    python tools/steam/make_store_art.py

Writes build/steam/: header 920x430, small 462x174, main 1232x706, vertical 748x896, library capsule 600x900,
library hero 3840x1240 (no text, as Steam asks), library logo 1280x720 (transparent), and five store screenshots 1920x1080.
"""
import os
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
SHOTS = os.path.join(ROOT, "test-output", "story")
OUT = os.path.join(ROOT, "build", "steam")
FONT = os.path.join(ROOT, "assets", "fonts", "Anton-Regular.ttf")
TITLE = "DEAD SILENT"
INK = (236, 232, 224)

# the frames: the church in the snow for the wide ones, the lodge for the tall, the crawlspace's lights for the hero
WIDE = "232_act22_the_church_behind"
TALL = "249_act22_the_lodge"
HERO = "283_act23_crawl_lights"
SCREENS = ["232_act22_the_church_behind", "283_act23_crawl_lights", "311_act24_the_tunnels", "32_crt_room", "37_last_stairs"]


def picture(name):
    """The 2.2:1 picture out of a letterboxed frame, lifted a little (the game is dark; a thumbnail is small)."""
    im = Image.open(os.path.join(SHOTS, name + ".png")).convert("RGB")
    w, h = im.size
    top = int(round((h - w / 2.2) / 2))
    im = im.crop((0, top, w, h - top))
    im = ImageEnhance.Brightness(im).enhance(1.25)
    return ImageEnhance.Contrast(im).enhance(1.08)


def fill(im, size, focus=(0.5, 0.5)):
    """Scaled to cover `size`, cropped about `focus` (fractions of the picture)."""
    tw, th = size
    s = max(tw / im.width, th / im.height)
    im = im.resize((max(tw, round(im.width * s)), max(th, round(im.height * s))), Image.LANCZOS)
    x = min(max(0, round(im.width * focus[0] - tw / 2)), im.width - tw)
    y = min(max(0, round(im.height * focus[1] - th / 2)), im.height - th)
    return im.crop((x, y, x + tw, y + th))


def shade(im, bottom=0.55, edge=0.35):
    """Darkened toward the bottom (under the title) and at the edges: a soft vignette, no hard bands."""
    w, h = im.size
    m = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(m)
    for y in range(h):
        t = max(0.0, (y / h - 0.35) / 0.65)
        d.line([(0, y), (w, y)], fill=int(255 * bottom * t * t))
    v = Image.new("L", (w, h), 0)
    ImageDraw.Draw(v).ellipse((-w * 0.25, -h * 0.25, w * 1.25, h * 1.25), fill=255)
    v = v.filter(ImageFilter.GaussianBlur(min(w, h) * 0.18))
    edge_mask = Image.eval(v, lambda p: int((255 - p) * edge))
    black = Image.new("RGB", (w, h), (0, 0, 0))
    im = Image.composite(black, im, m)
    return Image.composite(black, im, edge_mask)


def title(im, height_frac, y_frac, spacing=0.06):
    """The title, centred, its cap height a fraction of the image's, with a soft shadow."""
    w, h = im.size
    size = int(h * height_frac / 0.72)   # (Anton's caps are about 0.72 of its size)
    f = ImageFont.truetype(FONT, size)
    widths = [f.getlength(c) for c in TITLE]
    track = size * spacing
    total = sum(widths) + track * (len(TITLE) - 1)
    if total > w * 0.88:   # too wide: smaller
        return title(im, height_frac * w * 0.88 / total, y_frac, spacing)
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    shadow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    x = (w - total) / 2
    y = h * y_frac + size * 0.36   # (the baseline: the caps centred on y_frac)
    for c, cw in zip(TITLE, widths):
        ImageDraw.Draw(shadow).text((x, y + size * 0.03), c, font=f, fill=(0, 0, 0, 200), anchor="ls")
        ImageDraw.Draw(layer).text((x, y), c, font=f, fill=INK + (255,), anchor="ls")
        x += cw + track
    shadow = shadow.filter(ImageFilter.GaussianBlur(size * 0.06))
    out = Image.alpha_composite(im.convert("RGBA"), shadow)
    return Image.alpha_composite(out, layer).convert("RGB")


def logo(size):
    """The title alone on transparency, for the library (Steam lays it over the hero)."""
    im = Image.new("RGBA", size, (0, 0, 0, 0))
    w, h = size
    f = ImageFont.truetype(FONT, int(h * 0.42))
    track = f.size * 0.06
    widths = [f.getlength(c) for c in TITLE]
    total = sum(widths) + track * (len(TITLE) - 1)
    if total > w * 0.94:
        f = ImageFont.truetype(FONT, int(f.size * w * 0.94 / total))
        track = f.size * 0.06
        widths = [f.getlength(c) for c in TITLE]
        total = sum(widths) + track * (len(TITLE) - 1)
    shadow = Image.new("RGBA", size, (0, 0, 0, 0))
    x = (w - total) / 2
    y = h * 0.5 + f.size * 0.36
    for c, cw in zip(TITLE, widths):
        ImageDraw.Draw(shadow).text((x, y), c, font=f, fill=(0, 0, 0, 230), anchor="ls")
        ImageDraw.Draw(im).text((x, y), c, font=f, fill=INK + (255,), anchor="ls")
        x += cw + track
    shadow = shadow.filter(ImageFilter.GaussianBlur(f.size * 0.05))
    return Image.alpha_composite(shadow, im)


def main():
    os.makedirs(OUT, exist_ok=True)
    wide, tall, hero = picture(WIDE), picture(TALL), picture(HERO)
    art = {
        "header_capsule_920x430.png": title(shade(fill(wide, (920, 430), (0.55, 0.45))), 0.15, 0.78),
        "small_capsule_462x174.png": title(shade(fill(wide, (462, 174), (0.55, 0.45)), 0.65), 0.3, 0.6),
        "main_capsule_1232x706.png": title(shade(fill(wide, (1232, 706), (0.55, 0.45))), 0.13, 0.8),
        "vertical_capsule_748x896.png": title(shade(fill(tall, (748, 896), (0.52, 0.5))), 0.08, 0.83),
        "library_capsule_600x900.png": title(shade(fill(tall, (600, 900), (0.52, 0.5))), 0.075, 0.83),
        "library_hero_3840x1240.png": shade(fill(hero, (3840, 1240), (0.5, 0.5)), 0.3, 0.3),
    }
    for name, im in art.items():
        im.save(os.path.join(OUT, name))
    logo((1280, 720)).save(os.path.join(OUT, "library_logo_1280x720.png"))
    for i, s in enumerate(SCREENS, 1):
        fill(picture(s), (1920, 1080)).save(os.path.join(OUT, f"screenshot_{i}_{s.split('_', 1)[1]}.png"))
    print("wrote", len(art) + 1 + len(SCREENS), "images to", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
