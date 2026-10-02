"""The texture import policy (the optimization pass, 2026-10-02), applied to every texture's .import file:

    python tools/Textures/import_settings.py

  - mipmaps on, everywhere: without them a texture seen small or at a slant samples scattered texels (shimmer,
    sparkle on fine detail like webs and needles) and costs far more memory bandwidth;
  - compressed in video memory (S3TC/BPTC): an uncompressed 1024 px map is 4 MB on the card, compressed 0.5-1 MB.
    Normal maps are flagged as such (RG compression, their own mip filtering);
  - except the surface photos the game reads on the CPU to draw its own textures from (SurfaceKit): those stay
    lossless (they're sampled, not drawn);
  - one resolution per kind: the architecture's surface photos 256 px (the PS2 look), the furniture's baked cavity maps
    at most 512 px (soft occlusion: more is wasted), the hero textures (the wendigo's, the tablecloth's) as made.
Then run Godot's import (godot --headless --path . --import) to rebuild them.
"""
import os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
DIRS = ["assets/textures", "assets/models"]
CPU_READ = re.compile(r"assets/textures/surfaces/[^/]+_albedo\.png$")

changed = 0
for d in DIRS:
    for base, _, files in os.walk(os.path.join(ROOT, d)):
        for f in files:
            if not f.endswith(".import") or not re.search(r"\.(png|jpg|jpeg|webp)\.import$", f):
                continue
            p = os.path.join(base, f)
            rel = os.path.relpath(p, ROOT).replace("\\", "/")[: -len(".import")]
            s = open(p, encoding="utf-8").read()
            if 'importer="texture"' not in s:
                continue
            t = s
            t = re.sub(r"^mipmaps/generate=.*$", "mipmaps/generate=true", t, flags=re.M)
            if not CPU_READ.search(rel):
                t = re.sub(r"^compress/mode=.*$", "compress/mode=2", t, flags=re.M)
                t = re.sub(r"^detect_3d/compress_to=.*$", "detect_3d/compress_to=0", t, flags=re.M)
            # the resolution policy: the furniture's cavity maps (soft occlusion) at most 512 px; everything else as made
            limit = 512 if rel.endswith("_cavity.png") else 0
            t = re.sub(r"^process/size_limit=.*$", f"process/size_limit={limit}", t, flags=re.M)
            if re.search(r"(_normal|_nor_gl|_nor)\b", os.path.basename(rel)) or rel.endswith("_normal.png"):
                t = re.sub(r"^compress/normal_map=.*$", "compress/normal_map=1", t, flags=re.M)
            if t != s:
                open(p, "w", encoding="utf-8").write(t)
                changed += 1
print(f"{changed} imports updated")
