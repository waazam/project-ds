"""Bakes Act 24's ice-maze texture sets (the owner: "Increase the texel density on the ice walls ... Upres the snow cavern
textures, bake way higher-res texture sets for the ice maze, we should make the tunnel more icy"). All tile seamlessly
(periodic noise from filtered spectra, Voronoi on a torus), 1024 x 1024:

  assets/textures/ice/ice_albedo.png   glacial ice: deep blue, banded where it froze in layers, paler cloudy patches,
                                       trapped bubbles in strings, white-ish fracture lines
  assets/textures/ice/ice_normal.png   the scallops (broad dimples), the fractures as fine grooves, a fine ripple
  assets/textures/ice/ice_rough.png    glassy where it's clear, frosted in the cloudy patches and along the cracks
  assets/textures/ice/snow_albedo.png  packed cave-floor snow: granular, bluish in its hollows, a crust here and there
  assets/textures/ice/snow_normal.png

    python tools/textures/ice_maze.py
"""
import os
import numpy as np
from PIL import Image

N = 1024
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "textures", "ice")
rng = np.random.default_rng(2424)


def spectral(beta, lo=1.0, hi=None, seed=None):
    """Periodic noise with a 1/f^beta spectrum, band-limited, normalised 0..1."""
    r = np.random.default_rng(seed)
    w = r.standard_normal((N, N))
    F = np.fft.fft2(w)
    fy = np.fft.fftfreq(N)[:, None] * N
    fx = np.fft.fftfreq(N)[None, :] * N
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    amp = 1.0 / f ** beta
    amp[f < lo] = 0.0
    if hi:
        amp *= np.exp(-(f / hi) ** 2)
    n = np.real(np.fft.ifft2(F * amp))
    n -= n.min()
    return n / n.max()


def voronoi(count, seed):
    """Distance to the nearest and second-nearest point on a torus (tiles): F1, F2."""
    r = np.random.default_rng(seed)
    pts = r.random((count, 2)) * N
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    f1 = np.full((N, N), 1e9, np.float32)
    f2 = np.full((N, N), 1e9, np.float32)
    for (px, py) in pts:
        dx = np.abs(xx - px); dx = np.minimum(dx, N - dx)
        dy = np.abs(yy - py); dy = np.minimum(dy, N - dy)
        d = np.sqrt(dx * dx + dy * dy)
        nf1 = np.minimum(f1, d)
        f2 = np.where(d < f1, f1, np.minimum(f2, d))
        f1 = nf1
    return f1, f2


def normal_from(h, strength):
    """A tangent-space normal map (OpenGL-style, green up) from a height field that tiles."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save(a, name):
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(a).save(os.path.join(OUT, name))
    print("baked", name, a.shape)


def lerp(a, b, t):
    return a + (b - a) * t[..., None]


def ice():
    # the scallops: broad dimples, each a cell (F1 squared: a bowl), about 30 cm across at the wall's mapping
    f1, f2 = voronoi(110, 1)
    cell = f1 / f1.max()
    bowl = 1.0 - cell ** 1.6
    # the fractures: thin lines along the cells' edges of a finer set, wandering (warped by noise)
    g1, g2 = voronoi(48, 2)
    edge = g2 - g1
    warp = spectral(1.6, lo=2, seed=3)
    crack = np.exp(-((edge + (warp - 0.5) * 14.0) / 1.3) ** 2) * (0.4 + 0.6 * spectral(1.4, lo=2, seed=8))
    crack *= spectral(1.2, lo=3, seed=4) > 0.45
    # bands where it froze in layers: soft stripes across, wavering
    yy = np.mgrid[0:N, 0:N][0] / N
    band = 0.5 + 0.5 * np.sin((yy * 6.0 + (spectral(1.8, lo=1, seed=5) - 0.5) * 0.9) * 2 * np.pi)
    band = band ** 3
    cloud = spectral(1.7, lo=1, seed=6)
    fine = spectral(0.9, lo=8, hi=300, seed=7)
    # bubbles: strings of small pale beads (dots along a few wandering lines), and a scatter
    bub = np.zeros((N, N), np.float32)
    yy2, xx2 = np.mgrid[0:N, 0:N]
    for k in range(900):
        cx, cy = rng.random() * N, rng.random() * N
        rad = rng.uniform(0.8, 2.6)
        x0, x1 = int(cx - 4), int(cx + 5)
        y0, y1 = int(cy - 4), int(cy + 5)
        for y in range(y0, y1):
            for x in range(x0, x1):
                d = np.hypot(x - cx, y - cy)
                if d < rad + 1:
                    v = max(0.0, 1.0 - d / (rad + 1))
                    bub[y % N, x % N] = max(bub[y % N, x % N], v)
    # colour: deep glacial blue, paler and greener where it's thin (the bowls' rims), cloudy-white patches, banded
    deep = np.array([0.04, 0.12, 0.24])
    mid = np.array([0.12, 0.28, 0.45])
    pale = np.array([0.42, 0.6, 0.72])
    col = lerp(np.broadcast_to(deep, (N, N, 3)), np.broadcast_to(mid, (N, N, 3)), np.clip(0.35 + (1 - bowl) * 0.5 + (fine - 0.5) * 0.3, 0, 1))
    cloudy = np.clip((cloud - 0.55) * 2.2, 0, 1) * 0.55 + band * 0.18
    col = lerp(col, np.broadcast_to(pale, (N, N, 3)), np.clip(cloudy, 0, 1))
    col = lerp(col, np.broadcast_to(np.array([0.7, 0.82, 0.9]), (N, N, 3)), np.clip(crack * 0.32 + bub * 0.4, 0, 1))
    save((np.clip(col, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8), "ice_albedo.png")
    h = bowl * 1.0 - crack * 0.2 + (fine - 0.5) * 0.12 + bub * 0.05
    save(normal_from(h * 40.0, 1.0), "ice_normal.png")
    rough = np.clip(0.12 + cloudy * 0.45 + crack * 0.35 + (fine - 0.5) * 0.1, 0.05, 0.9)
    save((rough * 255).astype(np.uint8), "ice_rough.png")


def snow():
    grain = spectral(0.6, lo=40, hi=420, seed=11)
    lumps = spectral(1.6, lo=2, seed=12)
    crust_c, _ = voronoi(70, 13)
    crust = np.clip(1.0 - crust_c / 28.0, 0, 1) * (spectral(1.5, lo=2, seed=14) > 0.55)
    base = np.array([0.66, 0.71, 0.79])
    hollow = np.array([0.4, 0.5, 0.64])
    col = lerp(np.broadcast_to(hollow, (N, N, 3)), np.broadcast_to(base, (N, N, 3)), np.clip(0.35 + lumps * 0.55 + (grain - 0.5) * 0.25, 0, 1))
    col = lerp(col, np.broadcast_to(np.array([0.74, 0.8, 0.86]), (N, N, 3)), crust * 0.5)
    save((np.clip(col, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8), "snow_albedo.png")
    h = lumps * 1.0 + (grain - 0.5) * 0.35 + crust * 0.2
    save(normal_from(h * 30.0, 1.0), "snow_normal.png")


if __name__ == "__main__":
    ice()
    snow()
