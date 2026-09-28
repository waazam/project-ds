import subprocess, numpy as np, os, imageio_ffmpeg

FF = imageio_ffmpeg.get_ffmpeg_exe()
SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "build", "polyhaven")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets", "textures", "detail")
os.makedirs(OUT, exist_ok=True)
N = 256

# the game's kinds of surface, and the Poly Haven texture (CC0) each takes its micro-surface from
KINDS = {
    "wood": "fine_grained_wood", "plaster": "grey_plaster", "concrete": "concrete", "metal": "rusty_metal_02",
    "fabric": "denim_fabric", "leather": "brown_leather", "stone": "dark_rock", "marble": "marble_01",
    "bark": "bark_brown_02", "foliage": "leafy_grass", "ground": "forest_leaves_02", "grime": "dirty_concrete",
}
# how strong each one's grain is (the standard deviation of the albedo multiplier)
STRENGTH = {"wood": 0.07, "plaster": 0.05, "concrete": 0.06, "metal": 0.08, "fabric": 0.07, "leather": 0.06,
            "stone": 0.08, "marble": 0.04, "bark": 0.09, "foliage": 0.08, "ground": 0.1, "grime": 0.06}


def read(path, pix, ch):
    raw = subprocess.run([FF, "-v", "error", "-i", path, "-vf", f"scale={N}:{N}:flags=area", "-f", "rawvideo", "-pix_fmt", pix, "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(N, N, ch).astype(np.float32) / 255.0


def write(path, arr, pix):
    data = (np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8).tobytes()
    subprocess.run([FF, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", pix, "-s", f"{N}x{N}", "-i", "-", path], input=data, check=True)


def highpass(g, sigma):
    """Removes everything broader than about sigma pixels (an FFT Gaussian, so it wraps: the tile stays seamless)."""
    f = np.fft.fft2(g)
    fy = np.fft.fftfreq(g.shape[0])[:, None]
    fx = np.fft.fftfreq(g.shape[1])[None, :]
    low = np.exp(-2 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(f * (1 - low)))


for kind, src in KINDS.items():
    col = read(f"{SRC}/{src}_col.jpg", "rgb24", 3)
    g = col @ np.array([0.299, 0.587, 0.114], np.float32)
    hp = highpass(g, 20)
    hp = hp / (hp.std() + 1e-6) * STRENGTH[kind] * 0.75   # subtle: the PS2 look, not a modern one
    # a multiplier, bright on average (the material's colour is lifted to match), never above white
    mul = np.clip(0.95 + hp, 0.6, 1.0)
    write(f"{OUT}/{kind}_albedo.png", np.repeat(mul[:, :, None], 3, axis=2), "rgb24")
    nor = read(f"{SRC}/{src}_nor.jpg", "rgb24", 3)
    write(f"{OUT}/{kind}_normal.png", nor, "rgb24")
    print(kind, src, f"mean {mul.mean():.3f} min {mul.min():.2f}")
