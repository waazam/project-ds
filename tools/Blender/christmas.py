"""The ski lodge's Christmas, years after the party (the owner, 2026-10-01: the lobby filled out, and Christmas: "a tree
and decorations too, like it looks like a christmas party happened years ago and the remnants are still around").
Built and exported like the furniture (furniture.py: roles, the game's box-projected UVs, a baked cavity map) to
assets/models/furniture/<name>.glb:

    blender -b --python tools/Blender/christmas.py [-- name ...]

Roles the game fills: needles (the tree's dried needles), bark, bauble_red / bauble_gold / bauble_green / bauble_blue /
bauble_silver (old glass ornaments, their silvering gone dull), bulb_dead / bulb_lit (the tree's string of lights: most
dead), tinsel, star, paper_red / paper_green / paper_gold / paper_white (wrapping), ribbon, felt (the tree skirt,
stockings), fur (the stockings' cuffs), wood, cloth, glass, metal, card (party hats), cake, plate.
Each is built with its front toward -Y (the export mirrors it to the game's -Z), its base on z = 0.
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, apply_mods, add_mod, obj_from_bmesh, skin_tree, lumpy_sphere
import furniture as F
from furniture import role, lathe, block, piping, cushion, finish, join_objs, export

V = Vector
F.ROLE_COLOURS.update({
    "needles": (0.12, 0.16, 0.08), "needles_core": (0.05, 0.07, 0.04), "bark": (0.22, 0.15, 0.1), "bauble_red": (0.45, 0.05, 0.05), "bauble_gold": (0.6, 0.45, 0.15),
    "bauble_green": (0.08, 0.25, 0.12), "bauble_blue": (0.08, 0.12, 0.35), "bauble_silver": (0.55, 0.55, 0.55),
    "bulb_dead": (0.15, 0.14, 0.12), "bulb_lit": (1.0, 0.75, 0.4), "tinsel": (0.55, 0.45, 0.2), "star": (0.65, 0.5, 0.2),
    "paper_red": (0.45, 0.06, 0.05), "paper_green": (0.08, 0.22, 0.1), "paper_gold": (0.55, 0.42, 0.15), "paper_white": (0.75, 0.72, 0.65),
    "ribbon": (0.6, 0.08, 0.06), "felt": (0.4, 0.06, 0.05), "fur": (0.8, 0.78, 0.72), "wood": (0.2, 0.11, 0.06), "cloth": (0.75, 0.72, 0.65),
    "glass": (0.25, 0.3, 0.25), "metal": (0.6, 0.45, 0.2), "card": (0.5, 0.1, 0.35), "cake": (0.55, 0.5, 0.42), "plate": (0.8, 0.78, 0.72),
    "leather": (0.3, 0.12, 0.08), "top": (0.04, 0.035, 0.035),
})
F.BUDGET.update({"xmas_tree": 90000, "presents_a": 4000, "presents_b": 4000, "present_open": 2500, "wreath": 6000,
                 "garland": 5000, "stocking": 1200, "party_hat": 300, "party_table": 9000, "club_chair": 7000,
                 "chess_table": 5000, "ottoman": 6000, "bauble_floor": 400, "tinsel_floor": 800})

rnd = random.Random(1225)
F.KEEP_UV.add("needles")


def place(ob, rot=(0, 0, 0), loc=(0, 0, 0)):
    ob.rotation_euler = rot
    ob.location = loc
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True)
    return ob


def sphere(name, c, r, material, subdiv=2):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    bmesh.ops.translate(bm, vec=V(c), verts=bm.verts)
    return obj_from_bmesh(bm, name, material)


def needle_card(bm, base, tip, width, droop):
    """A bough: a long narrow V of needles from base to tip (two quads, a shallow fold), sagging at its end."""
    along = (tip - base)
    side = along.cross(V((0, 0, 1)))
    if side.length < 1e-4:
        side = V((1, 0, 0))
    side = side.normalized() * width * 0.5
    mid = base + along * 0.55 + V((0, 0, -droop * 0.4))
    end = tip + V((0, 0, -droop))
    up = V((0, 0, width * 0.18))
    vs = [bm.verts.new(p) for p in (base - side * 0.4, base + side * 0.4, mid + side + up * 0.5, mid - side + up * 0.5, end + side * 0.35, end - side * 0.35, mid + up)]
    # its UVs: the spray photo (fir_bough.png) whole across it, the stem at the base
    uv = bm.loops.layers.uv.verify()
    uvs = [(0.42, 1.0), (0.58, 1.0), (1.0, 0.45), (0.0, 0.45), (0.68, 0.02), (0.32, 0.02), (0.5, 0.45)]
    for f in ((0, 6, 3), (0, 1, 6), (1, 2, 6), (3, 6, 5), (6, 4, 5), (6, 2, 4)):
        face = bm.faces.new([vs[i] for i in f])
        for loop, i in zip(face.loops, f):
            loop[uv].uv = uvs[i]


# ------------------------------------------------------------------ the tree

def xmas_tree(height=6.2, radius=1.75):
    """A big fir, full to the floor, years dead where it stands: its boughs in tiers of drooping needle sprays (the
    needles dried a dull olive and thinning, a carpet of them shed round the stand), a garland of tinsel spiralling
    down it, old glass baubles hung all over, a string of lights round it (most bulbs long dead), the star knocked
    askew at the top, a felt skirt round its foot in an iron stand."""
    clear()
    P = []
    needles, bark = role("needles"), role("bark")
    # the trunk
    P.append(lathe("Trunk", [(0.0, 0.0), (0.09, 0.0), (0.085, height * 0.5), (0.03, height * 0.97), (0.0, height)], bark, segs=10))
    # the boughs: tiers from the floor up, many sprays each, each with side sprays
    bm = bmesh.new()
    tiers = 22
    surface = []   # points on the crown's outside, for the decorations
    for t in range(tiers):
        f = t / (tiers - 1)
        z = 0.35 + (height - 0.6) * f ** 0.92
        R = radius * (1 - f) ** 0.95 + 0.12
        n = max(6, int(22 * (1 - f) + 6))
        rot = rnd.uniform(0, math.tau)
        for b in range(n):
            a = rot + math.tau * b / n + rnd.uniform(-0.12, 0.12)
            d = V((math.cos(a), math.sin(a), 0))
            L = R * rnd.uniform(0.85, 1.05)
            base = V((0, 0, z)) + d * 0.05
            tip = V((0, 0, z + L * 0.25)) + d * L
            droop = L * rnd.uniform(0.25, 0.4)
            needle_card(bm, base, tip, 0.28 + L * 0.22, droop)
            # side sprays off the bough, two each side
            for s in (-1, 1):
                for q in (0.3, 0.62):
                    sb = base + (tip - base) * (q + rnd.uniform(-0.06, 0.06))
                    sd = (d + d.cross(V((0, 0, 1))) * s * 0.8).normalized()
                    st = sb + sd * L * 0.42 + V((0, 0, L * 0.04))
                    needle_card(bm, sb, st, 0.22 + L * 0.14, L * 0.15)
            surface.append((tip + V((0, 0, -droop * 0.85)), d, z))
    ob = obj_from_bmesh(bm, "Boughs", needles, smooth=False)
    P.append(ob)
    # its dense heart: a dark cone of foliage inside the sprays (without it the tree read as see-through, the trunk
    # plain through the gaps)
    prof = [(0.0, 0.25)]
    for k in range(9):
        f = k / 8
        prof.append((radius * 0.66 * (1 - f) ** 0.95 + 0.05, 0.25 + (height - 0.5) * f))
    prof.append((0.0, height - 0.2))
    P.append(lathe("Heart", prof, role("needles_core"), segs=16))
    # the baubles: old glass, all over the outside of the crown (a few gaps where they've fallen)
    kinds = ["bauble_red", "bauble_gold", "bauble_green", "bauble_blue", "bauble_silver"]
    for i, (p, d, z) in enumerate(surface):
        if rnd.random() > 0.5:
            continue
        # hung from the bough's tip, below it and just outside it (inside the foliage they were lost)
        r = rnd.uniform(0.05, 0.085)
        c = p + d * 0.04 + V((0, 0, -0.04))
        m = role(kinds[rnd.randrange(len(kinds))])
        P.append(sphere(f"Bauble{i}", c - V((0, 0, r + 0.02)), r, m, 1))
        P.append(block(f"Cap{i}", c - V((0, 0, 0.012)), (0.018, 0.018, 0.022), role("metal"), bevel=0.0))
    # the tinsel garland: a rope spiralling down round the crown, sagging between the boughs
    tinsel = role("tinsel")
    pts = []
    turns = 4.5
    for k in range(240):
        u = k / 239
        z = height * 0.9 - (height * 0.85) * u
        R = radius * (1 - (z - 0.35) / (height - 0.6)) ** 0.95 * 0.92 + 0.15
        a = turns * math.tau * u
        sag = 0.08 * abs(math.sin(a * 4.5))
        pts.append(V((math.cos(a) * R, math.sin(a) * R, z - sag)))
    P.append(piping("Tinsel", pts, 0.022, tinsel))
    # the lights: a string round it the other way, bulbs every 22 cm (one in six still lit)
    wire = []
    for k in range(200):
        u = k / 199
        z = height * 0.85 - (height * 0.8) * u
        R = radius * (1 - (z - 0.35) / (height - 0.6)) ** 0.95 * 0.88 + 0.1
        a = -3.5 * math.tau * u + 1.0
        wire.append(V((math.cos(a) * R, math.sin(a) * R, z)))
    P.append(piping("Wire", wire, 0.006, role("top")))
    acc = 0.0
    lit, dead = role("bulb_lit"), role("bulb_dead")
    for k in range(1, len(wire)):
        acc += (wire[k] - wire[k - 1]).length
        if acc < 0.22:
            continue
        acc = 0.0
        c = wire[k]
        on = rnd.random() < 0.17
        bm2 = bmesh.new()
        bmesh.ops.create_cone(bm2, cap_ends=True, segments=6, radius1=0.016, radius2=0.004, depth=0.045)
        bmesh.ops.translate(bm2, vec=c + V((0, 0, -0.03)), verts=bm2.verts)
        P.append(obj_from_bmesh(bm2, f"Bulb{k}", lit if on else dead))
    # the star at the top, knocked askew
    star = role("star")
    pts2 = []
    for k in range(10):
        a = math.tau * k / 10 + math.pi / 2
        r = 0.26 if k % 2 == 0 else 0.11
        pts2.append((math.cos(a) * r, math.sin(a) * r))
    bm3 = bmesh.new()
    vs = [bm3.verts.new((x, 0, y)) for x, y in pts2]
    f = bm3.faces.new(vs)
    ex = bmesh.ops.extrude_face_region(bm3, geom=[f])
    bmesh.ops.translate(bm3, vec=V((0, 0.05, 0)), verts=[e for e in ex["geom"] if isinstance(e, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm3, faces=bm3.faces)
    st = obj_from_bmesh(bm3, "Star", star, smooth=False)
    P.append(place(st, (0.0, 0.35, 0.3), (0.03, -0.02, height + 0.18)))
    # the stand (iron, three feet) and the skirt: a felt ring, dusty, rucked
    P.append(lathe("Stand", [(0.0, 0.0), (0.32, 0.0), (0.3, 0.18), (0.12, 0.22), (0.1, 0.4), (0.0, 0.4)], role("top"), segs=16))
    bm4 = bmesh.new()
    rings = []
    for j in range(3):
        rr = 0.35 + j * 0.45
        ring = []
        for k in range(48):
            a = math.tau * k / 48
            h = 0.02 + 0.04 * (math.sin(a * 7) * 0.5 + 0.5) * (j / 2) + (0.03 if j == 0 else 0)
            ring.append(bm4.verts.new((math.cos(a) * rr, math.sin(a) * rr, h)))
        rings.append(ring)
    for j in range(2):
        for k in range(48):
            n = (k + 1) % 48
            bm4.faces.new((rings[j][k], rings[j + 1][k], rings[j + 1][n], rings[j][n]))
    P.append(obj_from_bmesh(bm4, "Skirt", role("felt")))
    # needles shed round it: a ragged carpet on the floor (flat cards)
    bm5 = bmesh.new()
    for k in range(70):
        a = rnd.uniform(0, math.tau)
        r = rnd.uniform(0.4, radius * 1.15)
        c = V((math.cos(a) * r, math.sin(a) * r, 0.004 + rnd.uniform(0, 0.003)))
        s = rnd.uniform(0.12, 0.3)
        rot = rnd.uniform(0, math.tau)
        dx, dy = V((math.cos(rot), math.sin(rot), 0)) * s, V((-math.sin(rot), math.cos(rot), 0)) * s * 0.6
        vs = [bm5.verts.new(c + o) for o in (-dx - dy, dx - dy, dx + dy, -dx + dy)]
        bm5.faces.new(vs)
    P.append(obj_from_bmesh(bm5, "Shed", needles, smooth=False))
    return P


# ------------------------------------------------------------------ under the tree

def present(name, c, size, paper, rot=0.0, lid_off=False, bow=True):
    P = []
    w, d, h = size
    body = block(name, (0, 0, h / 2), (w, d, h), role(paper), bevel=0.004, segs=1)
    P.append(body)
    rib = role("ribbon")
    if not lid_off:
        P.append(block(name + "R1", (0, 0, h / 2), (w + 0.006, 0.03, h + 0.006), rib, bevel=0.0))
        P.append(block(name + "R2", (0, 0, h / 2), (0.03, d + 0.006, h + 0.006), rib, bevel=0.0))
        if bow:
            for s in (-1, 1):
                b = lumpy_sphere(name + f"Bow{s}", (s * 0.04, 0, h + 0.025), (0.045, 0.02, 0.025), rib, rnd.randint(1, 99), 0.1, 2.0, 2)
                P.append(b)
    else:
        # opened: the lid off beside it, the paper torn back, the box empty
        P.append(block(name + "Lid", (w * 0.9, d * 0.3, 0.03), (w + 0.01, d + 0.01, 0.06), role(paper), bevel=0.003, segs=1, rot=(0.0, 0.25, 0.4)))
        torn = lumpy_sphere(name + "Torn", (-w * 0.7, -d * 0.4, 0.04), (0.14, 0.1, 0.05), role(paper), rnd.randint(1, 99), 0.25, 3.0, 2)
        P.append(torn)
    for o in P:
        place(o, (0, 0, rot), c)
    return P


def presents(seed):
    clear()
    rnd.seed(seed)
    P = []
    papers = ["paper_red", "paper_green", "paper_gold", "paper_white"]
    spots = [(0, 0), (0.42, 0.1), (-0.38, 0.18), (0.1, 0.45), (-0.15, -0.35), (0.45, -0.3)]
    for i, (x, y) in enumerate(spots):
        s = (rnd.uniform(0.2, 0.42), rnd.uniform(0.18, 0.35), rnd.uniform(0.12, 0.3))
        P += present(f"P{i}", (x, y, 0), s, papers[rnd.randrange(4)], rnd.uniform(-0.6, 0.6))
    # one stacked on another
    P += present("Top", (0.02, 0.0, P[0].dimensions.z if False else 0.25), (0.18, 0.16, 0.12), papers[rnd.randrange(4)], 0.3)
    return P


def present_open():
    clear()
    return present("Open", (0, 0, 0), (0.36, 0.28, 0.2), "paper_red", 0.2, lid_off=True)


# ------------------------------------------------------------------ the decorations

def wreath():
    """A wreath: a ring of dried fir sprays bound round, red berries, a big red bow at its foot. Facing -Y (hung on a
    wall: its back at y = 0)."""
    clear()
    P = []
    bm = bmesh.new()
    R = 0.42
    for k in range(70):
        a = math.tau * k / 70
        c = V((math.cos(a) * R, -0.06, math.sin(a) * R + 0.55))
        t = V((-math.sin(a), 0, math.cos(a)))
        out = V((math.cos(a), 0, math.sin(a)))
        jitter = out * rnd.uniform(-0.1, 0.1) + V((0, -rnd.uniform(0, 0.05), 0))
        needle_card(bm, c - t * 0.1 + jitter, c + t * 0.16 + out * rnd.uniform(-0.08, 0.12) + jitter, 0.18, 0.0)
    P.append(obj_from_bmesh(bm, "Ring", role("needles"), smooth=False))
    for k in range(18):
        a = rnd.uniform(0, math.tau)
        r = R + rnd.uniform(-0.1, 0.1)
        P.append(sphere(f"Berry{k}", (math.cos(a) * r, -0.13, math.sin(a) * r + 0.55), 0.018, role("bauble_red"), 1))
    rib = role("ribbon")
    for s in (-1, 1):
        P.append(lumpy_sphere(f"Loop{s}", (s * 0.1, -0.12, 0.17), (0.1, 0.03, 0.06), rib, 5 + s, 0.08, 2.0, 2))
        P.append(place(block(f"Tail{s}", (0, 0, 0), (0.05, 0.01, 0.28), rib, bevel=0.0), (0, s * 0.25, 0), (s * 0.06, -0.11, 0.0)))
    P.append(sphere("Knot", (0, -0.13, 0.17), 0.035, rib, 1))
    return P


def garland(length=2.4, sag=0.35):
    """One swag of fir garland between two points 2.4 m apart (its ends at x = +-1.2, z = 0), sagging, a red bow at
    each end, a few bulbs (all dead) and a bauble at its lowest."""
    clear()
    P = []
    bm = bmesh.new()
    n = 48
    pts = []
    for k in range(n + 1):
        u = k / n
        x = -length / 2 + length * u
        z = -sag * 4 * u * (1 - u)
        pts.append(V((x, 0, z)))
    for k in range(n):
        c = pts[k]
        t = (pts[k + 1] - pts[k]).normalized()
        for j in range(3):
            a = rnd.uniform(0, math.tau)
            o = V((0, math.cos(a), math.sin(a))) * 0.07
            needle_card(bm, c - t * 0.06 + o * 0.3, c + t * 0.12 + o, 0.14, 0.02)
    P.append(obj_from_bmesh(bm, "Rope", role("needles"), smooth=False))
    P.append(piping("Core", pts, 0.025, role("needles")))
    rib = role("ribbon")
    for x in (-length / 2, length / 2):
        for s in (-1, 1):
            P.append(lumpy_sphere(f"Bow{x}{s}", (x + s * 0.08, -0.04, 0.0), (0.08, 0.025, 0.05), rib, 3, 0.08, 2.0, 2))
        P.append(place(block(f"Tail{x}", (0, 0, 0), (0.04, 0.01, 0.3), rib, bevel=0.0), (0, 0.15, 0), (x, -0.05, -0.17)))
    for k in range(6, n, 8):
        c = pts[k] + V((0, -0.06, -0.04))
        bm2 = bmesh.new()
        bmesh.ops.create_cone(bm2, cap_ends=True, segments=6, radius1=0.016, radius2=0.004, depth=0.045)
        bmesh.ops.translate(bm2, vec=c, verts=bm2.verts)
        P.append(obj_from_bmesh(bm2, f"Bulb{k}", role("bulb_dead")))
    P.append(sphere("Bauble", pts[n // 2] + V((0, -0.05, -0.12)), 0.06, role("bauble_gold"), 2))
    return P


def stocking():
    """A felt stocking hung by its loop, a white fur cuff, sagging empty."""
    clear()
    P = []
    pts = [V((0, 0, 0)), V((0, 0, -0.32)), V((0.02, 0, -0.42)), V((0.12, 0, -0.47)), V((0.2, 0, -0.46))]
    P.append(skin_tree("Leg", pts, [(i, i + 1) for i in range(len(pts) - 1)], [(0.08, 0.035), (0.075, 0.035), (0.07, 0.04), (0.06, 0.045), (0.04, 0.035)], role("felt"), 2))
    P.append(cushion("Cuff", (0, 0, 0.02), (0.19, 0.09, 0.09), role("fur"), puff=0.3, segs=(8, 6, 6)))
    P.append(piping("Loop", [V((-0.05, 0, 0.07)), V((-0.06, 0, 0.13)), V((-0.03, 0, 0.15)), V((-0.02, 0, 0.08))], 0.006, role("ribbon")))
    return P


def party_hat():
    """A paper party hat, crushed a little, lying on its side; a frayed tassel."""
    clear()
    P = [lathe("Hat", [(0.0, 0.0), (0.075, 0.0), (0.0, 0.17)], role("card"), segs=12)]
    P.append(lumpy_sphere("Tassel", (0, 0, 0.18), (0.025, 0.025, 0.03), role("tinsel"), 7, 0.3, 3.0, 1))
    for o in P:
        place(o, (math.pi / 2 - 0.25, 0, 0.4), (0, 0, 0.075))
    return P


def bauble_floor():
    """A bauble that fell and broke: half of it whole, shards of glass round it."""
    clear()
    P = [sphere("Half", (0, 0, 0.05), 0.06, role("bauble_red"), 2)]
    P.append(block("Cap", (0.0, 0.0, 0.112), (0.02, 0.02, 0.02), role("metal"), bevel=0.0))
    for k in range(9):
        a = rnd.uniform(0, math.tau)
        r = rnd.uniform(0.08, 0.25)
        P.append(place(block(f"Shard{k}", (0, 0, 0), (rnd.uniform(0.01, 0.03), rnd.uniform(0.01, 0.025), 0.003), role("bauble_red"), bevel=0.0),
                       (0, 0, rnd.uniform(0, 6.28)), (math.cos(a) * r, math.sin(a) * r, 0.002)))
    return P


def tinsel_floor():
    """A strand of tinsel pulled off and dropped in loose loops on the floor."""
    clear()
    pts = []
    for k in range(60):
        u = k / 59
        pts.append(V((math.sin(u * 9) * 0.25 + u * 0.6 - 0.3, math.cos(u * 7) * 0.2, 0.012 + 0.008 * math.sin(u * 23))))
    return [piping("Strand", pts, 0.018, role("tinsel"))]


def party_table():
    """The buffet left as it was: a long table under a cloth, its ends hanging; a punch bowl gone dry with a ladle in
    it; a cake, half eaten and grey with age on a stand; stacked plates, a few left out; empty champagne bottles (one
    on its side), glasses (some knocked over); a candelabrum burnt down; napkins crumpled. Length along X, 2.6 m."""
    clear()
    P = []
    L, W, H = 2.6, 0.85, 0.76
    wood, cloth = role("wood"), role("cloth")
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(block(f"Leg{sx}{sy}", (sx * (L / 2 - 0.08), sy * (W / 2 - 0.08), H / 2), (0.06, 0.06, H), wood, bevel=0.005))
    P.append(block("Top", (0, 0, H - 0.02), (L, W, 0.04), wood, bevel=0.005))
    # the cloth: over the top and down the sides and ends, a hand's width from the floor... in soft folds
    bm = bmesh.new()
    nx, ny = 52, 20
    grid = []
    for i in range(nx + 1):
        row = []
        for j in range(ny + 1):
            u, v = i / nx, j / ny
            x = (u - 0.5) * (L + 0.7)
            y = (v - 0.5) * (W + 0.7)
            ox, oy = max(abs(x) - L / 2, 0), max(abs(y) - W / 2, 0)
            over = max(ox, oy)
            z = H + 0.004 - over * 1.6 + 0.012 * math.sin(x * 11 + y * 3) * min(over * 6, 1)
            x2 = x - math.copysign(min(ox, 0.35) * 0.85, x) if ox > 0 else x
            y2 = y - math.copysign(min(oy, 0.35) * 0.85, y) if oy > 0 else y
            row.append(bm.verts.new((x2, y2, max(z, 0.25))))
        grid.append(row)
    for i in range(nx):
        for j in range(ny):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    P.append(obj_from_bmesh(bm, "Cloth", cloth))
    top = H + 0.006
    glass, metal, plate = role("glass"), role("metal"), role("plate")
    # the punch bowl and its ladle
    P.append(place(lathe("Bowl", [(0.0, 0.0), (0.08, 0.0), (0.09, 0.03), (0.22, 0.12), (0.24, 0.16), (0.225, 0.16), (0.2, 0.12), (0.0, 0.06)], glass, segs=24), (0, 0, 0), (-0.8, 0.05, top)))
    P.append(cylinder_between("Ladle", V((-0.8, 0.05, top + 0.08)), V((-0.65, 0.18, top + 0.32)), 0.008, metal))
    # the cake on its stand, a wedge cut out, grey
    P.append(place(lathe("Stand", [(0.0, 0.0), (0.08, 0.0), (0.03, 0.02), (0.025, 0.12), (0.17, 0.13), (0.17, 0.14), (0.0, 0.14)], plate, segs=20), (0, 0, 0), (0.35, 0.05, top)))
    cake = lathe("Cake", [(0.0, 0.0), (0.14, 0.0), (0.14, 0.11), (0.0, 0.115)], role("cake"), segs=20)
    P.append(place(cake, (0, 0, 0), (0.35, 0.05, top + 0.14)))
    P.append(place(block("Wedge", (0, 0, 0), (0.1, 0.03, 0.1), role("cake"), bevel=0.004), (0, 0.4, 0.6), (0.7, -0.15, top + 0.02)))
    # plates: a stack, and a few left about
    for k in range(6):
        P.append(place(lathe(f"Plate{k}", [(0.0, 0.0), (0.1, 0.0), (0.12, 0.012), (0.0, 0.006)], plate, segs=18), (0, 0, 0), (0.95, 0.2, top + k * 0.012)))
    for k, (x, y) in enumerate([(-0.3, -0.22), (0.95, -0.2), (-1.05, 0.25)]):
        P.append(place(lathe(f"Left{k}", [(0.0, 0.0), (0.1, 0.0), (0.12, 0.012), (0.0, 0.006)], plate, segs=18), (0.03, 0, 0), (x, y, top)))
    # bottles: two standing, one on its side
    for k, (x, y, down) in enumerate([(-0.25, 0.25, False), (-0.1, 0.28, False), (0.15, -0.25, True)]):
        b = lathe(f"Bottle{k}", [(0.0, 0.0), (0.045, 0.0), (0.045, 0.2), (0.02, 0.26), (0.014, 0.32), (0.0, 0.32)], glass, segs=14)
        P.append(place(b, (math.pi / 2, 0, 0.7) if down else (0, 0, 0), (x, y, top + (0.045 if down else 0))))
    # glasses: coupes, two knocked over
    for k, (x, y, down) in enumerate([(-0.45, -0.2, False), (-0.55, -0.1, True), (0.6, 0.3, False), (1.15, -0.05, True), (-1.1, -0.2, False)]):
        g = lathe(f"Coupe{k}", [(0.0, 0.0), (0.035, 0.0), (0.005, 0.01), (0.004, 0.09), (0.05, 0.1), (0.055, 0.12), (0.0, 0.1)], glass, segs=12)
        P.append(place(g, (math.pi / 2, 0, rnd.uniform(0, 6)) if down else (0, 0, 0), (x, y, top + (0.05 if down else 0))))
    # the candelabrum, burnt down
    P.append(place(lathe("Candelabrum", [(0.0, 0.0), (0.07, 0.0), (0.02, 0.03), (0.015, 0.3), (0.0, 0.3)], metal, segs=12), (0, 0, 0), (0.0, 0.0, top)))
    for s in (-1, 1):
        P.append(cylinder_between(f"Arm{s}", V((0, 0, top + 0.26)), V((s * 0.13, 0, top + 0.3)), 0.008, metal))
        P.append(place(lathe(f"Stub{s}", [(0.0, 0.0), (0.012, 0.0), (0.012, 0.03), (0.0, 0.035)], role("plate"), segs=8), (0, 0, 0), (s * 0.13, 0, top + 0.3)))
    # crumpled napkins
    for k in range(4):
        P.append(lumpy_sphere(f"Napkin{k}", (rnd.uniform(-1.1, 1.1), rnd.uniform(-0.3, 0.3), top + 0.02), (0.06, 0.05, 0.025), cloth, k + 3, 0.3, 3.0, 2))
    return P


def cylinder_between(name, a, b, r, material):
    from kit import cylinder
    return cylinder(name, a, b, r, r, 8, material)


# ------------------------------------------------------------------ more furniture for the lobby

def club_chair():
    """A leather club chair: low and deep, rolled arms flush with the back, a loose seat cushion, short turned feet,
    brass nails along the front of the arms."""
    clear()
    up, wood, metal = role("upholstery"), role("wood"), role("metal")
    P = []
    W, D = 0.9, 0.9
    P.append(cushion("Base", (0, 0, 0.27), (W, D, 0.3), up, puff=0.12))
    P.append(cushion("Back", (0, 0.36, 0.55), (W, 0.2, 0.62), up, puff=0.18))
    for s in (-1, 1):
        P.append(cushion(f"Arm{s}", (s * (W / 2 - 0.09), -0.02, 0.5), (0.2, D - 0.05, 0.4), up, puff=0.2))
        for k in range(9):
            P.append(lumpy_sphere(f"Nail{s}{k}", (s * (W / 2 - 0.09), -D / 2 + 0.03, 0.34 + k * 0.035), (0.007, 0.004, 0.007), metal, k, 0.0, 1.0, 1))
    P.append(cushion("Seat", (0, -0.06, 0.47), (W - 0.36, D - 0.25, 0.13), up, puff=0.25))
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(lathe(f"Foot{sx}{sy}", [(0.0, 0.0), (0.03, 0.0), (0.035, 0.04), (0.03, 0.11), (0.0, 0.12)], wood, segs=10, axis_pos=(sx * (W / 2 - 0.08), sy * (D / 2 - 0.08), 0)))
    return P


def chess_table():
    """A games table: a square top inlaid as a chessboard (its squares in two woods), a turned pedestal on three feet;
    a game left half played: pieces standing, a few knocked over."""
    clear()
    wood, top = role("wood"), role("top")
    P = []
    P.append(block("Top", (0, 0, 0.72), (0.75, 0.75, 0.04), wood, bevel=0.006))
    light, dark = role("paper_white"), role("top")
    for i in range(8):
        for j in range(8):
            P.append(block(f"Sq{i}{j}", (-0.28 + i * 0.08, -0.28 + j * 0.08, 0.7415), (0.08, 0.08, 0.003), light if (i + j) % 2 == 0 else dark, bevel=0.0))
    P.append(lathe("Pedestal", [(0.0, 0.0), (0.06, 0.0), (0.05, 0.2), (0.07, 0.3), (0.04, 0.5), (0.06, 0.66), (0.0, 0.7)], wood, segs=16))
    for k in range(3):
        a = math.tau * k / 3
        P.append(place(block(f"Foot{k}", (0, 0, 0), (0.36, 0.06, 0.06), wood, bevel=0.01), (0, 0, a), (math.cos(a) * 0.18, math.sin(a) * 0.18, 0.04)))
    for k in range(14):
        i, j = rnd.randrange(8), rnd.randrange(8)
        c = (-0.28 + i * 0.08, -0.28 + j * 0.08, 0.743)
        m = light if k % 2 == 0 else dark
        p = lathe(f"Piece{k}", [(0.0, 0.0), (0.016, 0.0), (0.012, 0.01), (0.007, 0.03), (0.011, 0.045), (0.0, 0.05)], m, segs=8)
        if k % 5 == 4:
            P.append(place(p, (math.pi / 2, 0, rnd.uniform(0, 6)), (c[0], c[1], c[2] + 0.012)))
        else:
            P.append(place(p, (0, 0, 0), c))
    return P


def ottoman():
    """A long tufted ottoman (the first reference: before the fire), deep-buttoned top, piped, on bun feet."""
    clear()
    up, wood = role("upholstery"), role("wood")
    P = [cushion("Body", (0, 0, 0.27), (1.5, 0.75, 0.32), up, puff=0.15, tufts=("z", F.diamond_grid(-0.65, 0.65, -0.3, 0.3, 6, 3)), tuft_depth=0.03)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(lathe(f"Foot{sx}{sy}", [(0.0, 0.0), (0.035, 0.0), (0.045, 0.05), (0.03, 0.1), (0.0, 0.11)], wood, segs=10, axis_pos=(sx * 0.65, sy * 0.3, 0)))
    return P


PIECES = {
    "xmas_tree": (xmas_tree, (0, 0, 3.0), 9.0, 2.0, 30),
    "presents_a": (lambda: presents(11), (0, 0, 0.2), 2.0, 0.8, 30),
    "presents_b": (lambda: presents(23), (0, 0, 0.2), 2.0, 0.8, 30),
    "present_open": (present_open, (0, 0, 0.1), 1.5, 0.7, 30),
    "wreath": (wreath, (0, 0, 0.55), 2.2, 0.6, 20),
    "garland": (garland, (0, 0, -0.2), 3.2, 0.3, 20),
    "stocking": (stocking, (0, 0, -0.2), 1.2, 0.2, 30),
    "party_hat": (party_hat, (0, 0, 0.05), 0.7, 0.4, 30),
    "bauble_floor": (bauble_floor, (0, 0, 0.03), 0.8, 0.5, 30),
    "tinsel_floor": (tinsel_floor, (0, 0, 0.02), 1.2, 0.8, 30),
    "party_table": (party_table, (0, 0, 0.8), 3.8, 1.2, 25),
    "club_chair": (club_chair, (0, 0, 0.45), 2.4, 0.6, 30),
    "chess_table": (chess_table, (0, 0, 0.6), 2.0, 0.9, 30),
    "ottoman": (ottoman, (0, 0, 0.3), 2.6, 0.7, 30),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        fn, tgt, dist, h, yaw = PIECES[name]
        export(fn(), name, tgt, dist, h, yaw)
