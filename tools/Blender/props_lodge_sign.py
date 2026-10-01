"""The lodge's road sign (see winter_props.py): three routed planks with a raised border, hung between two log posts
on iron brackets, under a little shingled gable with the snow lying on it; icicles off its edges. Its lettering is the
game's own (SignKit, on the planks' face at y = 0.115). Its face to the road (+Y)."""
from kit import *

DARKWOOD = os.path.join(TEX, "surfaces", "darkwood_albedo.png")


def log(name, a, b, r0, r1, material, rnd):
    """A barked log, a little crooked."""
    a, b = Vector(a), Vector(b)
    pts = [a.lerp(b, t) + Vector((rnd.uniform(-0.02, 0.02), rnd.uniform(-0.02, 0.02), 0)) * (0 if t in (0, 1) else 1) for t in (0, 0.33, 0.66, 1)]
    rs = [r0 + (r1 - r0) * t for t in (0, 0.33, 0.66, 1)]
    ob = skin_tree(name, pts, [(0, 1), (1, 2), (2, 3)], rs, material, subdiv=1)
    uv_box(ob, 1.5)
    return ob


def build():
    clear()
    rnd = random.Random(1370)
    bark = mat("sign_bark", (0.66, 0.6, 0.56), 0.95, tex=BARK)
    wood = mat("sign_wood", (0.95, 0.85, 0.75), 0.85, tex=DARKWOOD)
    iron = mat("sign_iron", (0.17, 0.16, 0.15), 0.6, 0.6)
    shingle = mat("sign_shingle", (0.24, 0.2, 0.17), 0.95, tex=DARKWOOD)
    ice = mat("ice", (0.62, 0.74, 0.88), 0.08)
    snow = snow_mat()
    parts = []
    # the posts and the beam across their tops
    for x in (-1.25, 1.25):
        parts.append(log(f"Post{x}", (x, 0, -0.4), (x, 0, 2.42), 0.11, 0.09, bark, rnd))
    parts.append(log("Beam", (-1.48, 0, 2.3), (1.48, 0, 2.3), 0.085, 0.08, bark, rnd))
    # the gable over it: two shingled slopes, a ridge board, the snow lying thick on top
    roof = []
    for s in (-1, 1):
        for row in range(4):
            y = s * (0.08 + row * 0.11)
            z = 2.66 - row * 0.065
            roof.append(box(f"Shingles{s}{row}", (0, y, z), (3.1, 0.14, 0.025), shingle, bevel=0.006, segs=1, rot=(s * -0.53, 0, 0)))
    roof.append(box("Ridge", (0, 0, 2.7), (3.12, 0.06, 0.05), wood, bevel=0.01, segs=1))
    for x in (-1.25, 1.25):
        for s in (-1, 1):
            roof.append(box(f"Rafter{x}{s}", (x, s * 0.2, 2.52), (0.06, 0.05, 0.5), wood, rot=(s * -1.0, 0, 0)))
    roofob = join(roof, "Roof")
    uv_box(roofob, 1.0)
    parts.append(roofob)
    rs = snow_on_top(roofob, "RoofSnow", snow, thick=0.09, min_up=0.35, seed=21)
    uv_box(rs, 2.0)   # (its copied UVs were the roof boards' edges: collapsed, the snow smeared)
    parts.append(rs)
    # the planks: three boards, a hair apart, with a raised routed border round the whole face
    boards = []
    for k in range(3):
        z = 1.25 + k * 0.25
        boards.append(box(f"Board{k}", (0, 0.075, z), (2.3, 0.07, 0.235), wood, bevel=0.01, segs=1))
    for (c, sz) in [((0, 0.115, 1.74), (2.24, 0.012, 0.04)), ((0, 0.115, 1.26), (2.24, 0.012, 0.04)),
                    ((-1.1, 0.115, 1.5), (0.04, 0.012, 0.5)), ((1.1, 0.115, 1.5), (0.04, 0.012, 0.5))]:
        boards.append(box("Border", c, sz, wood, bevel=0.004, segs=1))
    # the battens behind, holding the boards together
    for x in (-0.7, 0.7):
        boards.append(box(f"Batten{x}", (x, 0.025, 1.5), (0.09, 0.03, 0.72), wood, bevel=0.005, segs=1))
    planks = join(boards, "Planks")
    uv_box(planks, 1.2)
    parts.append(planks)
    # the iron brackets: a strap from each post to the board's edge, top and bottom, their bolts
    for x in (-1.0, 1.0):
        for z in (1.32, 1.68):
            sx = 1 if x > 0 else -1
            parts.append(box(f"Strap{x}{z}", (x + sx * 0.08, 0.1, z), (0.28, 0.012, 0.05), iron))
            for bx in (x - sx * 0.03, x + sx * 0.18):
                parts.append(cylinder(f"Bolt{bx}{z}", (bx, 0.105, z), (bx, 0.125, z), 0.012, 0.01, 6, iron))
    # snow along the top board and on the posts' tops
    parts.append(lumpy_sphere("TopSnow", (0, 0.075, 1.4 + 0.25 + 0.135), (1.15, 0.05, 0.03), snow, 31, amp=0.18, freq=3.0))
    for x in (-1.25, 1.25):
        parts.append(lumpy_sphere(f"CapSnow{x}", (x, 0, 2.44), (0.12, 0.12, 0.05), snow, 32 + int(x), amp=0.2, subdiv=2))
    # icicles off the gable's eaves and the bottom board
    for i in range(22):
        x = rnd.uniform(-1.45, 1.45)
        y = rnd.choice((-0.47, 0.47))
        top = Vector((x, y, 2.43))
        parts.append(cylinder(f"EaveIcicle{i}", top, top - Vector((0, 0, rnd.uniform(0.05, 0.3))), 0.014, 0.001, 5, ice))
    for i in range(9):
        x = rnd.uniform(-1.05, 1.05)
        top = Vector((x, 0.08, 1.13))
        parts.append(cylinder(f"BoardIcicle{i}", top, top - Vector((0, 0, rnd.uniform(0.04, 0.16))), 0.01, 0.001, 5, ice))
    for o in parts:
        if o.type == "MESH" and len(o.data.uv_layers) == 0:
            uv_box(o, 2.0)
    ob = join(parts, "LodgeSign")
    export([ob], "lodge_sign")
    preview("lodge_sign", (0, 0, 1.4), 4.2, 0.5, 20)
