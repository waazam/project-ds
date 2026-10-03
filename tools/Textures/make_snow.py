"""The winter woods' snow (Act 22), from Poly Haven's snow textures (CC0; downloaded to build/polyhaven):
three ground textures at 256 px, in the game's PS2 look: soft, a little crunchy, and toned down to a cold
blue-grey so a field of snow never glares (the owner finds bright white scenes painful).

- snow:   fresh deep snow off the path          (snow_03)
- plowed: the snowplow's track, packed snow worn down to dark ground in the ruts   (asphalt_snow)
- packed: the frozen, glazed crust near the lodge  (snow_floor)

    python tools/Textures/make_snow.py

(2026-09-30: superseded for the snow, the packed crust and the plowed road by make_winter_forest.py, from the owner's
own snow sets; run that after this one if this is ever run again.)
"""
import subprocess, os
import numpy as np
import imageio_ffmpeg

FF = imageio_ffmpeg.get_ffmpeg_exe()
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "..", "build", "polyhaven")
OUT = os.path.join(HERE, "..", "..", "assets", "textures", "snow")
os.makedirs(OUT, exist_ok=True)
N = 256

KINDS = {
    # name: (source, tint (multiplied), brightness ceiling, contrast)
    "snow": ("snow_03", (0.80, 0.84, 0.92), 0.80, 0.7),
    "plowed": ("asphalt_snow", (0.74, 0.77, 0.84), 0.9, 0.8),
    "packed": ("snow_floor", (0.70, 0.78, 0.88), 0.74, 1.2),
}


def read(path, pix, ch):
    raw = subprocess.run([FF, "-v", "error", "-i", path, "-vf", f"scale={N}:{N}:flags=area", "-f", "rawvideo", "-pix_fmt", pix, "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(N, N, ch).astype(np.float32) / 255.0


def write(path, arr):
    data = (np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8).tobytes()
    subprocess.run([FF, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{N}x{N}", "-i", "-", path], input=data, check=True)


def seamless(a):
    """Blends the tile's edges into its opposite edges (Poly Haven's 1k tiles are seamless, but scaling nudges them)."""
    w = 12
    ramp = np.linspace(0, 1, w, dtype=np.float32)
    for i in range(w):
        t = 0.5 * (1 - ramp[i])
        a[i, :] = a[i, :] * (1 - t) + a[-1 - i, :] * t
        a[:, i] = a[:, i] * (1 - t) + a[:, -1 - i] * t
    return a


for name, (src, tint, ceil, contrast) in KINDS.items():
    col = read(f"{SRC}/{src}_col.jpg", "rgb24", 3)
    lum = col @ np.array([0.299, 0.587, 0.114], np.float32)
    m = lum.mean()
    lum = np.clip(m + (lum - m) * contrast, 0, 1)
    # keep the source's own colour a little (grit in the ruts), mostly the cold tint
    rgb = 0.25 * col + 0.75 * lum[:, :, None]
    rgb = rgb * np.array(tint, np.float32)[None, None, :]
    rgb = rgb / max(rgb.max(), 1e-6) * ceil
    write(f"{OUT}/{name}_albedo.png", seamless(rgb))
    nor = read(f"{SRC}/{src}_nor.jpg", "rgb24", 3)
    write(f"{OUT}/{name}_normal.png", nor)
    print(name, src, f"mean {rgb.mean():.3f} max {rgb.max():.2f}")
