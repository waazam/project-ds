"""Scans the story test's screenshots for artifacting (docs: the polish pass):
- magenta: Godot's colour for a missing texture or a broken shader;
- blown out: a frame mostly pure white (a lighting/exposure fault);
- black: a frame all black inside the letterbox (a camera or lighting fault; some are meant, like fades);
- stripes: rows that are one flat colour right across (a render glitch).

    python tools/Textures/scan_shots.py [test-output/story]
"""
import os, sys, subprocess
import numpy as np
import imageio_ffmpeg

FF = imageio_ffmpeg.get_ffmpeg_exe()
root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "test-output", "story")
W, H = 320, 180


def load(path):
    raw = subprocess.run([FF, "-v", "error", "-i", path, "-vf", f"scale={W}:{H}:flags=area", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
                         capture_output=True).stdout
    if len(raw) != W * H * 3:
        return None
    return np.frombuffer(raw, np.uint8).reshape(H, W, 3).astype(np.float32) / 255.0


rows = []
for name in sorted(os.listdir(root)):
    if not name.endswith(".png"):
        continue
    img = load(os.path.join(root, name))
    if img is None:
        rows.append((name, "UNREADABLE", 0))
        continue
    inner = img[int(H * 0.12):int(H * 0.88)]           # inside the letterbox
    r, g, b = inner[..., 0], inner[..., 1], inner[..., 2]
    magenta = np.mean((r > 0.8) & (b > 0.8) & (g < 0.25))
    lum = inner @ np.array([0.299, 0.587, 0.114], np.float32)
    white = np.mean(lum > 0.97)
    black = np.mean(lum < 0.015)
    flat_rows = np.mean(np.std(inner, axis=1).max(axis=1) < 0.002)
    issues = []
    if magenta > 0.002: issues.append(f"magenta {magenta:.1%}")
    if white > 0.35: issues.append(f"blown out {white:.0%}")
    if black > 0.97: issues.append("all black")
    if flat_rows > 0.25 and black < 0.9: issues.append(f"flat rows {flat_rows:.0%}")
    if issues:
        rows.append((name, ", ".join(issues), float(lum.mean())))
print(f"{sum(1 for n in os.listdir(root) if n.endswith('.png'))} screenshots scanned in {root}")
for name, what, mean in rows:
    print(f"  {name:48s} {what}   (mean {mean:.2f})")
if not rows:
    print("  nothing found")
