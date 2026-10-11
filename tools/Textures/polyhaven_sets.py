"""Scanned surface sets from Poly Haven (https://polyhaven.com, CC0: free for any use, the Steam release included), for
SurfaceSets (scripts/world/SurfaceSets.cs): each becomes ph_<id>_albedo.jpg, ph_<id>_normal.jpg and ph_<id>_rough.jpg
in assets/textures/surfaces, at 1k (the source size; mipmapped and compressed on import).

The retexture pass (2026-10-10c, the owner: "go through https://polyhaven.com/textures and make sure EVERYTHING looks
good, we need a full retexture tweaking spree here, everything needs to look fresh and varied"). Credited in the
changelog and in assets/textures/surfaces/README.md.

    python tools/textures/polyhaven_sets.py            (fetches whatever isn't there yet)
"""
import json, os, urllib.request

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "textures", "surfaces")
UA = {"User-Agent": "DeadSilent-texture-fetch/1.0"}

SETS = {
    # the lodge's lobby (the owner: "i also hate this grey drab bland stone color of the room lets change that to feel
    # more modern"): warm stacked stone for the chimney breast and the stair's pier, wide warm planks underfoot
    "rustic_stone_wall": "the lobby's chimney breast, the stair's stone pier",
    "wood_floor": "the lobby's floor",
    # the crawlspace, deeper in: boards gone grey, split and black with age
    "weathered_brown_planks": "the crawlspace's far reaches",
    # the lodge's roof (the owner: "give it some color like a rustic royal blue thats been faded"): painted standing-seam
    # sheet, its red paint taken to a faded royal blue below (ph_lodge_roof_blue)
    "box_profile_metal_sheet": "the lodge's roofs (repainted blue)",
    # the retexture pass through the rest of the game: the forester station, the sewer, the pit, the stairwell, the bunker
    "red_brick_03": "the station's brick (its basement stair)",
    "marble_01": "the station's marble",
    "rusty_metal_02": "the station's rusted plate",
    "mossy_brick": "the sewer's brick",
    "rusty_metal_03": "the sewer's pipes",
    "metal_plate_02": "the pit's galvanized steel",
    "concrete_wall_004": "the stairwell's concrete",
    "concrete_block_wall_02": "the stairwell's block walls",
    "concrete_wall_006": "the bunker's walls",
    "concrete_floor_worn_001": "the bunker's floors",
}


def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60).read()


for pid in SETS:
    files = None
    for kind, out in (("Diffuse", "albedo"), ("nor_gl", "normal"), ("Rough", "rough")):
        dest = os.path.join(OUT, f"ph_{pid}_{out}.jpg")
        if os.path.exists(dest):
            continue
        files = files or json.loads(get(f"https://api.polyhaven.com/files/{pid}"))
        entry = files.get(kind) or files.get(kind.lower()) or (files.get("arm") if kind == "Rough" else None)
        if not entry:
            print(f"  {pid}: no {kind}")
            continue
        url = entry["1k"]["jpg"]["url"]
        open(dest, "wb").write(get(url))
        print(f"{pid:28s} {out:7s} <- {url}")


# ---- derived: the lodge's roof, the sheet's paint taken to a faded royal blue (its wear, its streaks and its rust kept)
def _roof_blue():
    import numpy as np
    from PIL import Image
    src = os.path.join(OUT, "ph_box_profile_metal_sheet_albedo.jpg")
    dst = os.path.join(OUT, "ph_lodge_roof_blue_albedo.jpg")
    if not os.path.exists(src):
        return
    a = np.asarray(Image.open(src).convert("RGB")).astype(np.float32) / 255
    lum = a @ np.array([0.3, 0.59, 0.11])
    red = np.clip((a[..., 0] - a[..., 1] - 0.04) * 6.0, 0, 1)   # where the paint still is (rust and bare metal aren't red)
    lum = lum / max(float(np.mean(lum)), 1e-3)                    # (its light and dark about 1: the wear, the seams)
    h, w = lum.shape
    yy, xx = np.mgrid[0:h, 0:w] / np.array([h, w])[:, None, None]
    rng = np.random.default_rng(7)
    def blur_noise(f):
        g = np.tile(rng.random((f, f)), (3, 3))   # (tiled, resized, the middle kept: it repeats seamlessly)
        from PIL import Image as _I
        big = np.asarray(_I.fromarray((g * 255).astype(np.uint8)).resize((w * 3, h * 3), _I.BICUBIC)).astype(np.float32) / 255
        return big[h:2 * h, w:2 * w]
    fade = np.clip(blur_noise(5) * 0.7 + blur_noise(13) * 0.3, 0, 1)   # where it's faded most (patchy, tiling not needed: a big tile)
    royal = np.array([0.2, 0.28, 0.5])
    faded = np.array([0.46, 0.51, 0.58])
    paint = royal[None, None] * (1 - fade[..., None] * 0.6) + faded[None, None] * (fade[..., None] * 0.6)
    paint = paint * np.clip(0.4 + 0.6 * lum[..., None], 0, 1.3)
    # chalky streaks straight down the sheet where the meltwater runs
    cols = rng.random(w) ** 4 * 0.16
    streak = np.repeat(cols[None, :], h, axis=0) * (0.6 + 0.4 * blur_noise(9))
    paint = paint * (1 - streak[..., None]) + streak[..., None] * np.array([0.62, 0.66, 0.7])
    out = paint * red[..., None] + a * 0.85 * (1 - red[..., None])
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(dst, quality=92)
    for m in ("normal", "rough"):
        s2 = os.path.join(OUT, f"ph_box_profile_metal_sheet_{m}.jpg")
        d2 = os.path.join(OUT, f"ph_lodge_roof_blue_{m}.jpg")
        if os.path.exists(s2) and not os.path.exists(d2):
            open(d2, "wb").write(open(s2, "rb").read())
    print("ph_lodge_roof_blue <- box_profile_metal_sheet, repainted")


_roof_blue()
