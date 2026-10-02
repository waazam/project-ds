"""Surface textures for the ski lodge (Act 23) and the church (Act 21), from Poly Haven (CC0; the 1k sources are
downloaded to build/polyhaven by this script if they aren't there already).

Each becomes a 256 px albedo and normal map in assets/textures/surfaces, in the game's PS2 look:
- area-downscaled to 256 px (soft, never crisp photo detail);
- the albedo's contrast pulled in a little and its colour kept (the factories tint or take the luminance);
- the normal map softened toward flat, so the relief stays shallow.

The texture factories (LodgeTextures, ChurchTextures) sample these through SurfaceKit: some take a photo
whole and tint it (the parquet, the dark wood, the fireplace's stone, the crypt's brick), others lay only
its grain under their own drawn pattern (the carpets' weave, the damask's jacquard, the log walls' grain).

    python tools/Textures/make_surfaces.py
"""
import os, subprocess, urllib.request
import numpy as np
import imageio_ffmpeg

FF = imageio_ffmpeg.get_ffmpeg_exe()
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "..", "build", "polyhaven")
OUT = os.path.join(HERE, "..", "..", "assets", "textures", "surfaces")
os.makedirs(SRC, exist_ok=True)
os.makedirs(OUT, exist_ok=True)
N = 256

# out name: (Poly Haven asset, albedo map name on Poly Haven, contrast, normal strength kept)
SURFACES = {
    # the lodge
    "parquet": ("herringbone_parquet", "diff", 0.9, 0.6),
    "darkwood": ("dark_wood", "diff", 0.85, 0.5),
    "fireplace_stone": ("rustic_stone_wall_02", "diff", 0.9, 0.8),
    "lobby_stone": ("stacked_stone_wall", "diff", 0.9, 0.8),
    "flagstone": ("monastery_stone_floor", "diff", 0.9, 0.7),
    "plaster": ("painted_plaster_wall", "diff", 0.8, 0.4),
    "leather": ("leather_red_02", "coll1", 0.9, 0.5),
    "velvet": ("velour_velvet", "diff", 0.8, 0.4),
    "linen": ("rough_linen", "diff", 0.8, 0.5),
    "carpet": ("dirty_carpet", "diff", 0.8, 0.5),
    "jacquard": ("quatrefoil_jacquard_fabric", "diff", 0.8, 0.5),
    "pine": ("wood_table_worn", "diff", 0.85, 0.5),
    # the church
    "limestone": ("marble_01", "diff", 0.85, 0.7),
    "crypt_brick": ("medieval_red_brick", "diff", 0.85, 0.7),
    "cobbles": ("cobblestone_floor_04", "diff", 0.85, 0.7),
    "vault_plaster": ("plaster_grey_04", "diff", 0.8, 0.4),
    "oak": ("wood_table_001", "diff", 0.85, 0.5),
    "old_planks": ("brown_planks_07", "diff", 0.85, 0.7),
}


def fetch(asset, kind, dest):
    if os.path.exists(dest):
        return
    url = f"https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/{asset}/{asset}_{kind}_1k.jpg"
    print("download", url)
    urllib.request.urlretrieve(url, dest)


def read(path):
    raw = subprocess.run([FF, "-v", "error", "-i", path, "-vf", f"scale={N}:{N}:flags=area", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(N, N, 3).astype(np.float32) / 255.0


def write(path, arr):
    data = (np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8).tobytes()
    subprocess.run([FF, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{N}x{N}", "-i", "-", path], input=data, check=True)


for name, (asset, diff, contrast, relief) in SURFACES.items():
    col_src = os.path.join(SRC, f"{asset}_col.jpg")
    nor_src = os.path.join(SRC, f"{asset}_nor.jpg")
    fetch(asset, diff, col_src)
    fetch(asset, "nor_gl", nor_src)
    col = read(col_src)
    m = col.mean(axis=(0, 1), keepdims=True)
    col = m + (col - m) * contrast
    write(os.path.join(OUT, f"{name}_albedo.png"), col)
    nor = read(nor_src)
    flat = np.array([0.5, 0.5, 1.0], np.float32)
    nor = flat + (nor - flat) * relief
    write(os.path.join(OUT, f"{name}_normal.png"), nor)
    # its roughness (the optimization and look pass, 2026-10-02): the photo's own, scaled so its mean is 1 and clipped,
    # so the material keeps the roughness it was tuned to and only gains the variation: the worn, handled places
    # smoother and catching the light, the rest as it was
    rough_src = os.path.join(SRC, f"{asset}_rough.jpg")
    try:
        fetch(asset, "rough", rough_src)
        rough = read(rough_src).mean(axis=2, keepdims=True)
        rough = np.clip(rough / max(rough.mean(), 1e-3), 0, 1)
        write(os.path.join(OUT, f"{name}_rough.png"), np.repeat(rough, 3, axis=2))
    except Exception as e:
        print(f"  (no roughness map for {asset}: {e})")
    print(f"{name:16s} <- {asset:28s} mean {col.mean():.3f}")
