"""The pacing chart (2026-10-24, the game-loop pass): how a play-through breathes, act by act, from the beats PacingLog
writes (user://pacing_beats.csv: every story flag and checkpoint, with the play time).

    python tools/checks/pacing_chart.py [beats.csv] [out_dir]

Default input: the game's user folder (%APPDATA%/Godot/app_userdata/Project DS/pacing_beats.csv). Writes, in out_dir
(default test-output/pacing):

    pacing.png   one row per act: a tick for every beat, red bands over the dead air (over GAP s with nothing)
    pacing.md    per act: its length, its beats, beats per minute, the longest gap, and every gap over GAP s, with where

Only the last session in the file is charted (a session starts at a "session" line), unless --all is given.
"""
import csv
import os
import sys
from PIL import Image, ImageDraw, ImageFont

GAP = 90.0
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def load(path, everything):
    rows = []
    offset = 0.0   # (--all: each session's clock starts again; run them on one after another)
    with open(path, newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            if r["kind"] == "session":
                if not everything:
                    rows = []
                elif rows:
                    offset = rows[-1]["seconds"] + 5.0 - float(r["seconds"])
                continue
            r["seconds"] = float(r["seconds"]) + offset
            rows.append(r)
    return rows


def acts(rows):
    """Beats grouped into stretches by the checkpoint they were reached under, in the order played."""
    out = []
    for r in rows:
        if not out or out[-1]["name"] != r["checkpoint"]:
            out.append({"name": r["checkpoint"], "beats": []})
        out[-1]["beats"].append(r)
    for a in out:
        ts = [b["seconds"] for b in a["beats"]]
        a["t0"], a["t1"] = min(ts), max(ts)
        a["gaps"] = [(p["seconds"], q["seconds"], q) for p, q in zip(a["beats"], a["beats"][1:]) if q["seconds"] - p["seconds"] > GAP]
    # an act runs until the next one starts (its last beat to the next's first is still its time)
    for a, b in zip(out, out[1:]):
        if b["t0"] - a["t1"] > GAP:
            a["gaps"].append((a["t1"], b["t0"], b["beats"][0]))
        a["t1"] = max(a["t1"], b["t0"])
    return out


def chart(stretches, path):
    W, rowh, left = 1400, 26, 260
    H = 40 + rowh * len(stretches)
    im = Image.new("RGB", (W, H), (22, 22, 26))
    d = ImageDraw.Draw(im)
    try:
        font = ImageFont.truetype(os.path.join(ROOT, "assets", "fonts", "SpecialElite-Regular.ttf"), 13)
    except OSError:
        font = ImageFont.load_default()
    longest = max((a["t1"] - a["t0"] for a in stretches), default=1) or 1
    scale = (W - left - 20) / longest
    d.text((10, 10), f"beats per act (tick = a flag or checkpoint; red = over {GAP:.0f} s of nothing); widest act {longest / 60:.1f} min", fill=(200, 200, 190), font=font)
    for i, a in enumerate(stretches):
        y = 34 + i * rowh
        mins = (a["t1"] - a["t0"]) / 60
        d.text((10, y + 4), f"{a['name'][:26]}  {mins:4.1f}m", fill=(210, 205, 190), font=font)
        x0 = left
        d.rectangle((x0, y + 8, x0 + (a["t1"] - a["t0"]) * scale, y + 16), fill=(48, 50, 56))
        for (g0, g1, _) in a["gaps"]:
            d.rectangle((x0 + (g0 - a["t0"]) * scale, y + 4, x0 + (g1 - a["t0"]) * scale, y + 20), fill=(150, 40, 36))
        for b in a["beats"]:
            x = x0 + (b["seconds"] - a["t0"]) * scale
            c = (230, 200, 90) if b["kind"] == "checkpoint" else (180, 220, 200)
            d.line((x, y + 3, x, y + 21), fill=c, width=2 if b["kind"] == "checkpoint" else 1)
    im.save(path)


def report(stretches, path):
    lines = ["# Pacing", "", f"Dead air = over {GAP:.0f} s with no flag or checkpoint.", "",
             "| Act (checkpoint) | Minutes | Beats | Beats/min | Longest gap | Gaps |", "|---|---:|---:|---:|---:|---:|"]
    for a in stretches:
        mins = max(1 / 6, (a["t1"] - a["t0"]) / 60)   # (at least 10 s: a stretch of a few beats at once is not 1000s a minute)
        allg = [q["seconds"] - p["seconds"] for p, q in zip(a["beats"], a["beats"][1:])] + [g1 - g0 for g0, g1, _ in a["gaps"]]
        lines.append(f"| {a['name']} | {mins:.1f} | {len(a['beats'])} | {len(a['beats']) / mins:.1f} | {max(allg, default=0):.0f} s | {len(a['gaps'])} |")
    lines += ["", "## Dead air", ""]
    for a in stretches:
        for (g0, g1, nxt) in a["gaps"]:
            lines.append(f"- **{a['name']}**: {g1 - g0:.0f} s before `{nxt['what']}` (near {nxt['x']}, {nxt['y']}, {nxt['z']})")
    open(path, "w", encoding="utf-8").write("\n".join(lines) + "\n")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    src = args[0] if args else os.path.join(os.environ.get("APPDATA", ""), "Godot", "app_userdata", "Project DS", "pacing_beats.csv")
    out = args[1] if len(args) > 1 else os.path.join(ROOT, "test-output", "pacing")
    os.makedirs(out, exist_ok=True)
    rows = load(src, "--all" in sys.argv)
    if not rows:
        print(f"[pacing] no beats in {src}")
        return
    st = acts(rows)
    chart(st, os.path.join(out, "pacing.png"))
    report(st, os.path.join(out, "pacing.md"))
    print(f"[pacing] {len(rows)} beats, {len(st)} acts -> {out}")


if __name__ == "__main__":
    main()
