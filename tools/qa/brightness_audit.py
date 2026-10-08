"""How dark is each act? Reads the autotest's screenshots (test-output/story) and its log, and lists every shot with its
act, its mean brightness (0..255, the HUD's bars cropped off) and the share of it that's all but black, darkest first;
then each act's median. A shot under the floor (mean < 18 and over 70% near-black) is flagged: too dark to see.

    python tools/qa/brightness_audit.py [path/to/auto.log]
"""
import os
import re
import sys
import glob
import statistics
from PIL import Image
import numpy as np

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
SHOTS = os.path.join(ROOT, "test-output", "story")
log = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ.get("TEMP", "/tmp"), "auto.log")

# the act each screenshot was taken in, from the log's "---- Act N" lines and "screenshot NAME" mentions
act_of = {}
act = "?"
if os.path.exists(log):
    for line in open(log, encoding="utf-8", errors="ignore"):
        m = re.search(r"\[storytest\] ---- (.+)$", line)
        if m:
            act = m.group(1).strip()
        for s in re.findall(r"screenshot ([A-Za-z0-9_]+)", line):
            act_of.setdefault(s, act)

rows = []
for f in sorted(glob.glob(os.path.join(SHOTS, "*.png"))):
    name = re.sub(r"^\d+_", "", os.path.splitext(os.path.basename(f))[0])
    a = np.asarray(Image.open(f).convert("L"), dtype=np.float32)
    h = a.shape[0]
    a = a[int(h * 0.1):int(h * 0.9)]   # (the cinema bars and the HUD's rows)
    mean = float(a.mean())
    black = float((a < 10).mean())
    rows.append((mean, black, act_of.get(name, "?"), name))

rows.sort()
print(f"{'mean':>6} {'black':>6}  act / shot")
for mean, black, a, n in rows:
    flag = "  <-- too dark" if mean < 18 and black > 0.7 else ""
    print(f"{mean:6.1f} {black*100:5.0f}%  {a} / {n}{flag}")
print()
by = {}
for mean, black, a, n in rows:
    by.setdefault(a, []).append(mean)
for a, ms in sorted(by.items(), key=lambda kv: statistics.median(kv[1])):
    print(f"{statistics.median(ms):6.1f}  {a}  ({len(ms)} shots)")
