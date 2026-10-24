"""Contact sheets for the art pass (the art-pass skill's review gate).

    python tools/art/sheets.py grid <folder> [<folder> ...] -o <out.png>     every shot in each folder, one row per folder
    python tools/art/sheets.py pair <before_dir> <after_dir> -o <out.png>    before over after, shot by shot (by order)

Shots are scaled to 480 px wide; each row is labelled with its folder's name.
"""
import os, sys
from PIL import Image, ImageDraw

W = 480


def shots(folder):
    return [os.path.join(folder, f) for f in sorted(os.listdir(folder)) if f.lower().endswith(".png") and "_OFF" not in f]


def thumb(path):
    im = Image.open(path).convert("RGB")
    return im.resize((W, int(im.height * W / im.width)))


def grid(folders, out):
    rows = []
    for d in folders:
        ims = [thumb(p) for p in shots(d)]
        if not ims:
            continue
        h = max(i.height for i in ims)
        row = Image.new("RGB", (W * len(ims), h + 22), (18, 18, 20))
        for k, im in enumerate(ims):
            row.paste(im, (k * W, 22))
        ImageDraw.Draw(row).text((6, 4), os.path.basename(os.path.normpath(d)), fill=(230, 230, 230))
        rows.append(row)
    sheet = Image.new("RGB", (max(r.width for r in rows), sum(r.height for r in rows)), (18, 18, 20))
    y = 0
    for r in rows:
        sheet.paste(r, (0, y))
        y += r.height
    sheet.save(out)
    print(f"[sheets] {out}: {len(rows)} rows")


def pair(before, after, out):
    a, b = shots(before), shots(after)
    n = min(len(a), len(b))
    ims = [(thumb(a[i]), thumb(b[i])) for i in range(n)]
    h = max(x.height for p in ims for x in p)
    sheet = Image.new("RGB", (W * n, h * 2 + 44), (18, 18, 20))
    d = ImageDraw.Draw(sheet)
    d.text((6, 4), "before: " + before, fill=(230, 230, 230))
    d.text((6, h + 26), "after: " + after, fill=(230, 230, 230))
    for i, (x, y) in enumerate(ims):
        sheet.paste(x, (i * W, 22))
        sheet.paste(y, (i * W, h + 44))
    sheet.save(out)
    print(f"[sheets] {out}: {n} pairs")


if __name__ == "__main__":
    args = sys.argv[1:]
    out = args[args.index("-o") + 1]
    rest = [a for a in args[1:] if a not in ("-o", out)]
    (grid if args[0] == "grid" else lambda r, o: pair(r[0], r[1], o))(rest, out)
