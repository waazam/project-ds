"""Act 13's story pieces: the forester station as Mitchell Webber's headquarters (STORY.md Rev 5, 2026-10-16). Built and
exported through the furniture pipeline (furniture.export: role materials the game swaps its own onto, a metre-a-repeat
box UV, a baked cavity map) to assets/models/furniture/ss_<name>.glb:

    blender -b --python tools/Blender/station_story.py [-- name ...]

The desk's photo (his copy of the one in the Act 1 cabin: a man and a boy, the man's face turned away, his corner rubbed
nearly smooth), the nameplate, the desk with a drawer (then two) pulled out, the key rack, the boarded windows, the
half-packed duffel, his logbook and the little table it lies open on, the cigar box's coat button, watch and ring, the
basement clock with its hands at the start of his shift, the chain on the locked room's door (whole, and cut), the bare
cot in that room, and the ranger staff photos in Room 3 with their faces scratched out.

Each piece is drawn in the GAME's own coordinates (fixtures.P: x right, y up, z toward the back; its front, the face the
player sees, toward -Z), so the game puts it in with the transform its code names. The desk variants and the clock are
the existing pieces (interiors.station_desk, clutter.grandfather_clock) changed, in Blender's own frame as they were.
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furniture as F
import fixtures as FX
from kit import clear, obj_from_bmesh, add_mod, apply_mods
from fixtures import P, box, cyl, tube, lathe, grid, blob, rot

V = Vector

F.ROLE_COLOURS.update({
    # the photo
    "frame": (0.3, 0.2, 0.12), "back": (0.35, 0.3, 0.22), "sky": (0.72, 0.64, 0.5), "lake": (0.6, 0.55, 0.45),
    "trees": (0.2, 0.17, 0.12), "ground": (0.4, 0.33, 0.24), "man": (0.14, 0.12, 0.1), "boy": (0.3, 0.24, 0.18),
    "skin": (0.7, 0.58, 0.45), "rubbed": (0.8, 0.74, 0.62),
    # the staff photo
    "mat": (0.62, 0.58, 0.48), "print": (0.55, 0.5, 0.4), "uniform": (0.22, 0.22, 0.16), "hat": (0.3, 0.24, 0.16),
    "face": (0.66, 0.58, 0.48), "scratch": (0.86, 0.84, 0.78),
    # the rest
    "wood": (0.3, 0.19, 0.1), "brass": (0.62, 0.48, 0.24), "paper": (0.8, 0.76, 0.66), "iron": (0.12, 0.12, 0.13),
    "steel": (0.55, 0.56, 0.55), "black": (0.02, 0.02, 0.02), "casing": (0.32, 0.24, 0.17), "glass": (0.04, 0.05, 0.06),
    "plank": (0.42, 0.32, 0.2), "canvas": (0.3, 0.32, 0.22), "strap": (0.12, 0.1, 0.08), "clothes": (0.45, 0.2, 0.15),
    "cover": (0.3, 0.1, 0.07), "page": (0.82, 0.78, 0.66), "edge": (0.75, 0.7, 0.58), "thread": (0.2, 0.22, 0.15),
    "leather": (0.25, 0.14, 0.08), "gold": (0.75, 0.58, 0.25), "blanket": (0.33, 0.3, 0.25), "lead": (0.1, 0.1, 0.1),
})
F.KEEP_UV.update({"page"})
F.BUDGET.update({"ss_desk_open1": 7000, "ss_desk_open2": 7500, "ss_station_clock": 9000})


# ------------------------------------------------------------------ helpers (game coordinates)

def slab(name, outline, xf, d0, d1, m):
    """A flat piece cut to an outline [(u, v), ...], from depth d0 to d1, put in the game by xf(u, v, d)."""
    bm = bmesh.new()
    a = [bm.verts.new(P(xf(u, v, d0))) for (u, v) in outline]
    b = [bm.verts.new(P(xf(u, v, d1))) for (u, v) in outline]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.split_edges(bm, edges=bm.edges[:])   # (flat: the export smooths every piece)
    return obj_from_bmesh(bm, name, m, smooth=False)


def ellipse(cu, cv, ru, rv, n=14, a0=0.0, a1=math.tau, wob=0.0, seed=1):
    rnd = random.Random(seed)
    pts = []
    for i in range(n):
        a = a0 + (a1 - a0) * i / (n if a1 - a0 >= math.tau - 1e-6 else n - 1)
        k = 1 + rnd.uniform(-wob, wob)
        pts.append((cu + math.cos(a) * ru * k, cv + math.sin(a) * rv * k))
    return pts


def torus(name, c, axis, R, r, m, segs=24, rsegs=8, sx=1.0):
    """A ring round `axis` at game centre c (sx stretches it along its own first axis: a chain link)."""
    axis = V(axis).normalized()
    u = axis.cross(V((0, 1, 0)) if abs(axis.y) < 0.9 else V((1, 0, 0))).normalized()
    w = axis.cross(u).normalized()
    bm = bmesh.new()
    rings = []
    for i in range(segs):
        a = i / segs * math.tau
        dirv = u * math.cos(a) + w * math.sin(a)
        centre = V(c) + u * math.cos(a) * R * sx + w * math.sin(a) * R
        ring = []
        for j in range(rsegs):
            b = j / rsegs * math.tau
            ring.append(bm.verts.new(P(centre + dirv * math.cos(b) * r + axis * math.sin(b) * r)))
        rings.append(ring)
    for i in range(segs):
        for j in range(rsegs):
            i1, j1 = (i + 1) % segs, (j + 1) % rsegs
            bm.faces.new((rings[i][j], rings[i1][j], rings[i1][j1], rings[i][j1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name, m)


def drop(objs, *names):
    """Takes the named parts out of a piece (and out of the scene)."""
    keep = []
    for o in objs:
        if o.name.split(".")[0] in names:
            bpy.data.objects.remove(o, do_unlink=True)
        else:
            keep.append(o)
    return keep


def find(objs, name):
    return next(o for o in objs if o.name.split(".")[0] == name)


def shift(ob, by):
    ob.data.transform(Matrix.Translation(V(by)))
    ob.data.update()


# ------------------------------------------------------------------ the desk's photo

PHOTO_LEAN = math.radians(12)
PW, PH = 0.13, 0.17          # the frame, outside
IW, IH = 0.094, 0.13         # the print


def _photo_xf(u, v, d):
    """Frame space (u to the viewer's right, which is the game's -X as the front faces -Z; v up from the frame's foot; d
    depth, negative toward the viewer) leaned back on its strut."""
    c, s = math.cos(PHOTO_LEAN), math.sin(PHOTO_LEAN)
    return V((-u, v * c - d * s + 0.0014, v * s + d * c))


def desk_photo():
    """His copy of the photo (STORY.md: the one in the Act 1 cabin, aged and handled soft). A plain wooden frame with
    its corners worn through to bare wood, a card back and its strut. The print: a lake shore and a treeline in a pale
    sky; on the left the man, tall, turned three-quarters away, his face toward the water; his hand on the shoulder of
    the boy beside him, who looks at the camera. The top left corner, where a thumb held it, rubbed nearly smooth."""
    frame, back, rubbed = F.role("frame"), F.role("back"), F.role("rubbed")
    o = []
    xf = _photo_xf
    hw, hh = PW / 2, PH
    bw = (PW - IW) / 2
    # the frame: four rails (the outside, less the window), eased by a chamfered outline
    outer = [(-hw, 0.0), (hw, 0.0), (hw, hh), (-hw, hh)]
    win = [(-IW / 2, bw), (IW / 2, bw), (IW / 2, bw + IH), (-IW / 2, bw + IH)]
    for i in range(4):
        a, b = outer[i], outer[(i + 1) % 4]
        c, d = win[(i + 1) % 4], win[i]
        o.append(slab(f"Rail{i}", [a, b, c, d], xf, -0.009, 0.0, frame))
    # the inner lip, a step in toward the print
    for i in range(4):
        a, b = win[i], win[(i + 1) % 4]
        cu, cv = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        mid = (0.0, bw + IH / 2)
        inn = lambda p: (p[0] + (mid[0] - p[0]) * 0.04, p[1] + (mid[1] - p[1]) * 0.03)
        o.append(slab(f"Lip{i}", [a, b, inn(b), inn(a)], xf, -0.006, -0.002, frame))
    # the card back and its strut
    o.append(slab("Back", [(-hw + 0.004, 0.003), (hw - 0.004, 0.003), (hw - 0.004, hh - 0.004), (-hw + 0.004, hh - 0.004)], xf, 0.0, 0.003, back))
    c, s = math.cos(PHOTO_LEAN), math.sin(PHOTO_LEAN)
    top = xf(0, 0.11, 0.003)
    foot = V((0, 0.0, top.z + top.y * math.tan(math.radians(30))))
    o.append(box("Strut", (top + foot) * 0.5, (0.03, (top - foot).length, 0.003), back, 0.0005,
                 basis=rot((1, 0, 0), -math.atan2(foot.z - top.z, top.y - foot.y))))
    # the print, in layers toward the viewer (each a hair proud of the last)
    def L(k):
        return -0.0021 - 0.00035 * k

    def layer(name, pts, k, role):
        o.append(slab(name, [(u, bw + v) for (u, v) in pts], xf, L(k + 1), L(k), F.role(role)))

    W2 = IW / 2
    layer("Sky", [(-W2, 0), (W2, 0), (W2, IH), (-W2, IH)], 0, "sky")
    layer("Lake", [(-W2, 0.052), (W2, 0.05), (W2, 0.074), (-W2, 0.076)], 1, "lake")
    # the far treeline: spruce tops along the shore
    rnd = random.Random(1988)
    tl = [(W2, 0.074)]
    x = W2
    while x > -W2:
        x2 = max(-W2, x - rnd.uniform(0.004, 0.008))
        tl.append(((x + x2) / 2, 0.081 + rnd.uniform(0.004, 0.016)))
        tl.append((x2, 0.079 + rnd.uniform(0.0, 0.003)))
        x = x2
    tl.append((-W2, 0.076))
    layer("Trees", tl, 2, "trees")
    layer("Shore", [(-W2, 0), (W2, 0), (W2, 0.05), (0.02, 0.054), (-W2, 0.052)], 2, "ground")
    # the man: tall, on the left, turned three-quarters away (the back of his head and his shoulder to us); a coat
    man = [(-0.034, 0.004), (-0.026, 0.004), (-0.024, 0.04), (-0.018, 0.04), (-0.016, 0.004), (-0.008, 0.004),
           (-0.009, 0.046), (-0.004, 0.088), (-0.008, 0.094), (-0.015, 0.098), (-0.03, 0.097), (-0.038, 0.09),
           (-0.04, 0.06), (-0.036, 0.046)]
    layer("Man", man, 3, "man")
    # his head, turned away: the back of it, his hair, no face; an ear's edge on the near side
    layer("Head", ellipse(-0.0215, 0.106, 0.0068, 0.0085, 16), 3, "man")
    layer("Ear", ellipse(-0.0158, 0.105, 0.0011, 0.0019, 8), 4, "skin")
    # his arm across the boy's shoulders
    layer("Arm", [(-0.006, 0.086), (0.014, 0.068), (0.018, 0.064), (0.016, 0.06), (0.012, 0.062), (-0.008, 0.078)], 4, "man")
    # the boy: smaller, on the right, facing the camera
    boy = [(0.006, 0.004), (0.012, 0.004), (0.013, 0.026), (0.016, 0.026), (0.017, 0.004), (0.023, 0.004),
           (0.024, 0.03), (0.028, 0.056), (0.024, 0.064), (0.008, 0.064), (0.004, 0.056), (0.007, 0.03)]
    layer("Boy", boy, 3, "boy")
    layer("BoyFace", ellipse(0.0158, 0.0722, 0.0054, 0.0066, 16), 4, "skin")
    layer("BoyHair", ellipse(0.0158, 0.0768, 0.0058, 0.0034, 12, 0.0, math.pi), 5, "boy")
    layer("BoyHand", ellipse(0.0255, 0.033, 0.0016, 0.002, 8), 5, "skin")
    # the corner his thumb wore: rubbed nearly smooth, the emulsion gone to the pale card in a soft-edged patch
    rub = []
    for (u, v) in ellipse(-W2 + 0.005, IH - 0.01, 0.034, 0.05, 24, wob=0.12, seed=3):
        q = (max(-W2, u), min(IH, v))
        if not rub or abs(q[0] - rub[-1][0]) + abs(q[1] - rub[-1][1]) > 1e-4:
            rub.append(q)
    layer("Rubbed", rub, 6, "rubbed")
    # the frame's own wear: the gilt and the stain rubbed through on the same corner
    o.append(slab("Worn", [(-hw, hh - 0.022), (-hw + 0.022, hh), (-hw, hh)], xf, -0.0095, -0.0089, rubbed))
    return o


# ------------------------------------------------------------------ the nameplate

def nameplate():
    """A wooden wedge on the desk's front edge with a brass plate on its slope (the game letters it: M. WEBBER, CHIEF
    RANGER). Its centre on the origin, its foot on y = 0, the plate toward -Z."""
    wood, brass = F.role("wood"), F.role("brass")
    o = []
    prof = [(-0.025, 0.0), (0.02, 0.0), (0.02, 0.045), (0.008, 0.045), (-0.025, 0.006)]   # (z, y)
    o.append(slab("Wedge", [(z, y) for (z, y) in prof], lambda u, v, d: V((d, v, u)), -0.12, 0.12, wood))
    a, b = V((0, 0.006, -0.025)), V((0, 0.045, 0.008))
    dirv = (b - a).normalized()
    n = V((0, dirv.z, -dirv.y))
    mid = (a + b) * 0.5
    basis = Matrix((V((1, 0, 0)), dirv, n)).transposed()
    o.append(box("Plate", mid + n * 0.00075, (0.21, (b - a).length * 0.7, 0.0015), brass, 0.0004, basis=basis))
    return o


# ------------------------------------------------------------------ the desk, its drawers pulled out (Blender frame)

def _pull_drawer(P_, key, cx, z, out=0.26):
    """Pulls one of station_desk's pedestal drawers out by `out` (its front, pull and label moved; the box behind its
    front, a dark slot where it was, papers in it)."""
    wood, black, paper = F.role("wood"), F.role("black"), F.role("paper")
    for nm in (f"Drawer{key}", f"Pull{key}", f"Label{key}"):
        shift(find(P_, nm), (0, -out, 0))
    y0 = -0.335 - out + 0.0125
    depth = out + 0.02
    P_.append(F.block(f"Slot{key}", (cx, -0.327, z), (0.44, 0.012, 0.16), black, bevel=0.0))
    for s in (-1, 1):
        P_.append(F.block(f"Side{key}{s}", (cx + s * 0.205, y0 + depth / 2, z - 0.012), (0.012, depth, 0.13), wood, bevel=0.002, segs=1))
    P_.append(F.block(f"Bottom{key}", (cx, y0 + depth / 2, z - 0.072), (0.41, depth, 0.008), wood, bevel=0.0))
    rnd = random.Random(sum(map(ord, key)))
    for k in range(5):
        P_.append(F.block(f"Paper{key}{k}", (cx + rnd.uniform(-0.06, 0.06), y0 + depth * 0.45 + rnd.uniform(-0.04, 0.04), z - 0.066 + k * 0.006),
                          (0.21, 0.28 if k % 2 else 0.2, 0.002), paper, bevel=0.0, rot=(rnd.uniform(-0.15, 0.15), 0, rnd.uniform(-0.4, 0.4))))
    return P_


def desk_open(n):
    import interiors
    P_ = interiors.station_desk()
    _pull_drawer(P_, "11", 0.62, 0.17 + 1 * 0.2)
    if n >= 2:
        _pull_drawer(P_, "-12", -0.62, 0.17 + 2 * 0.2, out=0.18)
    return P_


# ------------------------------------------------------------------ the basement clock, its hands at the start of his shift

def station_clock():
    """clutter.grandfather_clock, its hands at six (the start of his shift: the hour hand down, the minute hand up)."""
    import clutter
    P_ = drop(clutter.grandfather_clock(), "HourHand", "MinuteHand")
    black = F.role("black")
    P_.append(F.block("HourHand", (0, -0.181, 1.8 - 0.034), (0.009, 0.003, 0.068), black, bevel=0.0))
    P_.append(F.block("MinuteHand", (0, -0.183, 1.8 + 0.055), (0.006, 0.003, 0.11), black, bevel=0.0))
    P_.append(F.lathe("Boss", [(0.0, 0.0), (0.008, 0.0), (0.008, 0.004), (0.0, 0.005)], F.role("metal"), 12))
    P_[-1].data.transform(Matrix.Translation(V((0, -0.184, 1.8))) @ Matrix.Rotation(math.pi / 2, 4, "X"))
    return P_


# ------------------------------------------------------------------ the key rack by the desk

KEY_X = [-0.16, -0.08, 0.0, 0.08, 0.16]


def key_rack():
    """A board of five cup hooks, a paper label over each (lettered in the game, in his hand), keys on four of them; the
    middle hook, the clock's, empty. Its back on the wall (z = 0), its foot at y = 0."""
    wood, brass, iron, paper = F.role("wood"), F.role("brass"), F.role("iron"), F.role("paper")
    o = [box("Board", (0, 0.065, -0.009), (0.42, 0.13, 0.018), wood, 0.004),
         box("Cap", (0, 0.133, -0.012), (0.44, 0.012, 0.024), wood, 0.004)]
    for i, x in enumerate(KEY_X):
        o.append(tube(f"Hook{i}", [(x, 0.045, -0.018), (x, 0.045, -0.034), (x, 0.039, -0.042), (x, 0.031, -0.04), (x, 0.031, -0.034), (x, 0.035, -0.031)],
                      0.0022, brass, 8))
        o.append(box(f"Label{i}", (x, 0.098, -0.0185), (0.066, 0.017, 0.001), paper, 0.0))
        if i == 2:
            continue
        # the key: its bow on the hook, hanging straight down
        o.append(torus(f"Bow{i}", (x, 0.02, -0.038), (0, 0, 1), 0.009, 0.0022, iron if i % 2 else brass, 16, 6))
        o.append(cyl(f"Shank{i}", (x, 0.011, -0.038), (x, -0.04, -0.038), 0.0026, 0.0024, iron if i % 2 else brass, 8))
        o.append(box(f"Bit{i}", (x + 0.006, -0.034, -0.038), (0.01, 0.012, 0.003), iron if i % 2 else brass, 0.0005))
        if i in (0, 3):
            o.append(box(f"Tag{i}", (x - 0.004, -0.012, -0.036), (0.022, 0.034, 0.0008), paper, 0.0, basis=rot((0, 0, 1), 0.25)))
    return o


# ------------------------------------------------------------------ a window boarded from the inside

def boarded_window():
    """A sash window in its casing, the night black behind its four panes, and boards nailed across it from the inside:
    four planks, one on the slant, their nails. Its back on the wall (z = 0), the sill's foot at y = 0, centred on x."""
    casing, glass, plank, iron = F.role("casing"), F.role("glass"), F.role("plank"), F.role("iron")
    W, H = 0.9, 1.1
    o = [box("Sill", (0, 0.02, -0.035), (W + 0.12, 0.04, 0.07), casing, 0.006)]
    for s in (-1, 1):
        o.append(box(f"Jamb{s}", (s * (W / 2 - 0.04), 0.04 + H / 2, -0.012), (0.08, H, 0.024), casing, 0.005))
    o.append(box("Head", (0, 0.04 + H + 0.05, -0.014), (W + 0.06, 0.1, 0.028), casing, 0.005))
    o.append(box("Glass", (0, 0.04 + H / 2, -0.003), (W - 0.16, H - 0.02, 0.002), glass, 0.0))
    # the sash's rails and muntins
    for y in (0.07, 0.04 + H / 2, H + 0.02):
        o.append(box(f"Rail{y:.2f}", (0, y, -0.009), (W - 0.16, 0.035, 0.016), casing, 0.003))
    o.append(box("Muntin", (0, 0.04 + H / 2, -0.009), (0.025, H - 0.02, 0.014), casing, 0.003))
    rnd = random.Random(31)
    boards = [(0.24, 0.05, 0.14), (0.52, -0.04, 0.12), (0.8, 0.03, 0.15), (0.55, 0.62, 0.13)]
    for k, (y, tilt, w) in enumerate(boards):
        L = W + 0.14 if abs(tilt) < 0.3 else math.hypot(W, H) * 0.92
        b = rot((0, 0, 1), tilt)
        c = V((rnd.uniform(-0.02, 0.02), 0.04 + y * H, -0.036 - 0.023 * k))
        o.append(box(f"Board{k}", c, (L, w, 0.022), plank, 0.004, basis=b))
        for e in (-1, 1):
            for nn in (-1, 1):
                p = c + b @ V((e * (L / 2 - 0.05), nn * w * 0.25, -0.012))
                o.append(cyl(f"Nail{k}{e}{nn}", p, p + V((0, 0, -0.003)), 0.006, 0.006, iron, 8))
    return o


# ------------------------------------------------------------------ the half-packed duffel by the door

def duffel():
    """A canvas duffel lying by the door, half packed: its top unzipped and gaping, a shirt and a sleeve spilling out,
    its handles flopped over, a shoulder strap on the floor. Its foot on y = 0, its length along x."""
    canvas, strap, metal, clothes, black = F.role("canvas"), F.role("strap"), F.role("steel"), F.role("clothes"), F.role("black")
    o = []
    rows = []
    n_u, n_a = 18, 20
    L, R = 0.62, 0.15
    for i in range(n_u + 1):
        t = i / n_u
        x = (t - 0.5) * L
        end = math.sqrt(max(0.0, 1 - (abs(t - 0.5) * 2) ** 6))
        rr = R * (0.35 + 0.65 * end)
        row = []
        for j in range(n_a):
            a = j / n_a * math.tau
            y = 0.13 + math.sin(a) * rr * 0.85
            z = math.cos(a) * rr
            # half empty: the top sags in
            if math.sin(a) > 0.2:
                y -= 0.05 * end * (math.sin(a) - 0.2)
            row.append((x, max(0.004, y), z))
        rows.append(row)
    o.append(grid("Bag", rows, canvas, thick=0.004, closed_u=True))
    for s in (-1, 1):
        o.append(lathe(f"End{s}", [(0.0, 0.0), (R * 0.36, 0.0), (R * 0.36, 0.006), (0.0, 0.007)], strap, at=(s * L / 2, 0.13, 0), segs=20,
                       basis=rot((0, 0, 1), -s * math.pi / 2)))
    # the open zip along the top, the clothes coming out of it
    o.append(box("Gape", (0, 0.205, 0), (L * 0.62, 0.006, 0.05), black, 0.0))
    for s in (-1, 1):
        o.append(tube(f"Zip{s}", [(-L * 0.32, 0.205, s * 0.027), (0, 0.2, s * 0.03), (L * 0.32, 0.205, s * 0.027)], 0.004, strap, 6))
    o.append(blob("Shirt", (0.05, 0.22, -0.01), (0.12, 0.035, 0.05), clothes, 7, amp=0.25))
    o.append(tube("Sleeve", [(0.1, 0.22, -0.03), (0.16, 0.18, -0.13), (0.2, 0.06, -0.18), (0.22, 0.012, -0.24)], [0.026, 0.024, 0.022, 0.02], clothes, 10))
    for s in (-1, 1):
        o.append(tube(f"Handle{s}", [(-0.12, 0.2, s * 0.05), (-0.06, 0.24, s * 0.1), (0.06, 0.24, s * 0.1), (0.12, 0.2, s * 0.05)], (0.014, 0.003), strap, 8))
    o.append(tube("Strap", [(-0.29, 0.1, 0.12), (-0.25, 0.02, 0.22), (-0.05, 0.006, 0.3), (0.2, 0.006, 0.26), (0.3, 0.05, 0.13)], (0.02, 0.003), strap, 8))
    for x in (-0.29, 0.3):
        o.append(torus(f"Ring{x}", (x, 0.1, 0.12), (1, 0, 0), 0.012, 0.0025, metal, 12, 6))
    return o


# ------------------------------------------------------------------ his logbook, open, and the table it lies on

LOG_W, LOG_D = 0.21, 0.29


def logbook():
    """A ledger lying open, its two cloth-bound boards, the pages bowing up from the gutter (each page a 0..1 UV square,
    for the game's written page), a pencil in the gutter. Its foot on y = 0, its spine along z."""
    cover, edge, wood, lead = F.role("cover"), F.role("edge"), F.role("wood"), F.role("lead")
    page = F.role("page")
    o = []
    for s in (-1, 1):
        o.append(box(f"Board{s}", (s * (LOG_W / 2 + 0.006), 0.002, 0), (LOG_W + 0.012, 0.004, LOG_D + 0.012), cover, 0.0015))
        # the page block's top: a grid, its UVs laid on
        bm = bmesh.new()
        uv = bm.loops.layers.uv.new("UVMap")
        nx, nz = 12, 6
        vv = []
        for i in range(nx + 1):
            t = i / nx
            x = s * (0.004 + t * (LOG_W - 0.006))
            h = 0.004 + 0.012 * math.sin(min(1.0, t / 0.18) * math.pi / 2) * (1 - 0.55 * t) - 0.006 * (1 - min(1.0, t / 0.05))
            row = []
            for j in range(nz + 1):
                z = (j / nz - 0.5) * LOG_D
                row.append(bm.verts.new(P((x, h, z))))
            vv.append(row)
        for i in range(nx):
            for j in range(nz):
                q = (vv[i][j], vv[i + 1][j], vv[i + 1][j + 1], vv[i][j + 1]) if s > 0 else (vv[i][j], vv[i][j + 1], vv[i + 1][j + 1], vv[i + 1][j])
                f = bm.faces.new(q)
                for lp in f.loops:
                    ii = next(a for a in range(nx + 1) if lp.vert in vv[a])
                    jj = vv[ii].index(lp.vert)
                    u = ii / nx if s > 0 else 1 - ii / nx
                    lp[uv].uv = (u, 1 - jj / nz)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        top = obj_from_bmesh(bm, f"Page{s}", page)
        o.append(top)
        # the edges of the block (a box under the top, a little short of it)
        o.append(box(f"Block{s}", (s * (LOG_W / 2 + 0.001), 0.006, 0), (LOG_W - 0.008, 0.008, LOG_D - 0.006), edge, 0.001))
    o.append(cyl("Pencil", (0.03, 0.014, -0.1), (0.05, 0.014, 0.07), 0.0035, 0.0035, wood, 6))
    o.append(cyl("Tip", (0.05, 0.014, 0.07), (0.052, 0.014, 0.085), 0.0035, 0.0008, lead, 6))
    return o


def log_table():
    """A small plain table, its top at 0.74: an eased top, an apron, four tapered legs. Centred, its foot on y = 0."""
    wood = F.role("wood")
    W, D, H = 0.72, 0.48, 0.74
    o = [box("Top", (0, H - 0.015, 0), (W, 0.03, D), wood, 0.006)]
    for s in (-1, 1):
        o.append(box(f"ApronX{s}", (0, H - 0.07, s * (D / 2 - 0.04)), (W - 0.1, 0.08, 0.02), wood, 0.003))
        o.append(box(f"ApronZ{s}", (s * (W / 2 - 0.04), H - 0.07, 0), (0.02, 0.08, D - 0.1), wood, 0.003))
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * (W / 2 - 0.045), sz * (D / 2 - 0.045)
            bm = bmesh.new()
            bmesh.ops.create_cube(bm, size=1.0)
            for v in bm.verts:
                t = v.co.z + 0.5 if False else (v.co.y + 0.5)
                w = 0.04 if t > 0.5 else 0.026
                v.co = P(V((x + v.co.x * w, (H - 0.03) * t, z + v.co.z * w)))
            o.append(obj_from_bmesh(bm, f"Leg{sx}{sz}", wood, smooth=False))
    return o


# ------------------------------------------------------------------ the cigar box's things

def coat_button():
    """A ranger's coat button, brass, domed, a pine in relief on it, its shank and a tail of torn thread. Lying face up,
    centred, its foot on y = 0."""
    brass, thread = F.role("brass"), F.role("thread")
    o = [lathe("Dome", [(0.0, 0.002), (0.0145, 0.002), (0.0152, 0.004), (0.0148, 0.0055), (0.012, 0.0075), (0.007, 0.0088), (0.0, 0.0092)], brass, segs=32),
         lathe("Shank", [(0.0, 0.0), (0.003, 0.0), (0.003, 0.0025), (0.0, 0.0025)], brass, segs=10),
         torus("Rim", (0, 0.0042, 0), (0, 1, 0), 0.0138, 0.0009, brass, 32, 6)]
    # the pine: three stacked tiers and a trunk, standing proud of the dome
    for k, (r, y) in enumerate(((0.0062, 0.0089), (0.0048, 0.0093), (0.0032, 0.0097))):
        o.append(slab(f"Tier{k}", [(-r, -0.002 + k * 0.0032), (r, -0.002 + k * 0.0032), (0.0, 0.0018 + k * 0.0032)],
                      lambda u, v, d: V((u, d, v)), y, y + 0.0007, brass))
    o.append(tube("Thread", [(0.002, 0.001, 0.0), (0.009, 0.0008, 0.006), (0.016, 0.0006, 0.004), (0.022, 0.0005, 0.011)], 0.0006, thread, 5))
    return o


def watch():
    """A man's wristwatch, lying face up: its steel case and crown, a glass over a pale dial with its hands, the leather
    strap open and curled. Centred, its foot on y = 0."""
    steel, face, glass, leather, black, brass = F.role("steel"), F.role("face"), F.role("glass"), F.role("leather"), F.role("black"), F.role("brass")
    o = [lathe("Case", [(0.0, 0.004), (0.0165, 0.004), (0.018, 0.006), (0.018, 0.009), (0.0165, 0.0105), (0.0, 0.0105)], steel, segs=32),
         lathe("Dial", [(0.0, 0.0106), (0.0148, 0.0106), (0.0148, 0.0108), (0.0, 0.0108)], face, segs=32),
         lathe("Crystal", [(0.0, 0.0109), (0.0152, 0.0109), (0.012, 0.0122), (0.0, 0.0126)], glass, segs=32),
         cyl("Crown", (0.018, 0.0075, 0), (0.0215, 0.0075, 0), 0.0022, 0.0022, steel, 10),
         box("Hour", (0.0, 0.0112, -0.004), (0.0016, 0.0004, 0.008), black, 0.0),
         box("Minute", (0.004, 0.0113, 0.003), (0.0012, 0.0004, 0.012), black, 0.0, basis=rot((0, 1, 0), 0.9))]
    # the strap, both halves, curling up off the table and round toward each other
    for s in (-1, 1):
        pts = []
        for i in range(9):
            t = i / 8
            a = t * math.pi * (0.75 if s > 0 else 0.55)
            pts.append((0.0, 0.006 + (1 - math.cos(a)) * 0.022, s * (0.019 + math.sin(a) * 0.026 + t * 0.01)))
        o.append(tube(f"Strap{s}", pts, (0.0095, 0.0015), leather, 8))
    o.append(box("Buckle", (0, 0.03 + 0.012, 0.045), (0.022, 0.003, 0.003), brass, 0.0006, basis=rot((1, 0, 0), 0.5)))
    return o


def ring():
    """A plain gold band (a wedding ring), lying flat."""
    return [torus("Band", (0, 0.0021, 0), (0, 1, 0), 0.0098, 0.0021, F.role("gold"), 36, 10, 1.0)]


# ------------------------------------------------------------------ the locked room's door: its chain

def hasp():
    """The staple screwed to the door's leaf near its edge, the chain's anchor. Its plate on the leaf's face (z = 0),
    the plate toward -Z, centred."""
    iron = F.role("iron")
    return [box("Plate", (0, 0, -0.003), (0.035, 0.09, 0.006), iron, 0.0015),
            torus("Staple", (0, 0, -0.014), (0, 1, 0), 0.011, 0.0035, iron, 14, 6, 1.0)]


def _link(name, c, along, k, m):
    """A chain link at c, its long axis along `along`, every other one turned a quarter."""
    along = V(along).normalized()
    side = along.cross(V((0, 1, 0)) if abs(along.y) < 0.9 else V((1, 0, 0))).normalized()
    axis = side if k % 2 == 0 else along.cross(side).normalized()
    # torus round `axis`, its first axis stretched; the first axis torus() picks is axis x up/x: steer it to `along`
    ob = torus(name, (0, 0, 0), (0, 0, 1), 0.011, 0.0032, m, 16, 6, sx=1.7)
    # (built round game Z with its stretch along game X; turn it so X -> along, Z -> axis)
    b = Matrix((along, axis.cross(along).normalized(), axis)).transposed()
    M = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))   # game <-> Blender swap
    T = M @ b @ M
    ob.data.transform(Matrix.Translation(P(V(c))) @ T.to_4x4())
    ob.data.update()
    return ob


def _padlock(c, tilt, steel, brass):
    b = rot((0, 0, 1), tilt)
    o = [box("LockBody", V(c) + b @ V((0, -0.03, 0)), (0.045, 0.05, 0.02), brass, 0.004, basis=b)]
    pts = [V(c) + b @ V((math.cos(a) * 0.015, math.sin(a) * 0.016 - 0.004, 0)) for a in [math.pi * i / 8 for i in range(9)]]
    o.append(tube("Shackle", pts, 0.0035, steel, 8))
    return o


# the chain's two anchors, in the frame of the door's latch-side jamb (x = 0 at the gap's edge, z = 0 the wall's middle)
LEAF_EYE = V((-0.09, 1.04, -0.039))
CASING_EYE = V((0.012, 1.04, -0.139))


def door_chain():
    """The chain that holds the locked room shut, from outside: an eye screwed into the casing, a length of chain from
    the staple on the leaf sagging round to it, a padlock through two of its links."""
    iron, steel, brass = F.role("iron"), F.role("steel"), F.role("brass")
    o = [cyl("EyeScrew", CASING_EYE + V((0, 0, 0.015)), CASING_EYE + V((0, 0, -0.004)), 0.003, 0.003, iron, 8),
         torus("Eye", CASING_EYE + V((0, 0, -0.012)), (0, 1, 0), 0.011, 0.0035, iron, 14, 6)]
    # the run: out of the leaf's staple, down in a sag, the lock at its foot, up to the eye
    a, b = LEAF_EYE + V((0, -0.012, -0.002)), CASING_EYE + V((0, -0.012, -0.012))
    pts = []
    for i in range(15):
        t = i / 14
        p = a.lerp(b, t) + V((0, -0.13 * math.sin(math.pi * t), -0.03 * math.sin(math.pi * t)))
        pts.append(p)
    for i in range(len(pts) - 1):
        o.append(_link(f"Link{i}", (pts[i] + pts[i + 1]) * 0.5, pts[i + 1] - pts[i], i, iron))
    lock_at = pts[7] + V((0, -0.012, 0))
    o += _padlock(lock_at, 0.1, steel, brass)
    return o


def chain_cut():
    """The same chain, cut: hanging straight down from the casing's eye, its cut link open at the foot, and the rest on
    the floor under it, the padlock still shut on two links."""
    iron, steel, brass = F.role("iron"), F.role("steel"), F.role("brass")
    o = [cyl("EyeScrew", CASING_EYE + V((0, 0, 0.015)), CASING_EYE + V((0, 0, -0.004)), 0.003, 0.003, iron, 8),
         torus("Eye", CASING_EYE + V((0, 0, -0.012)), (0, 1, 0), 0.011, 0.0035, iron, 14, 6)]
    top = CASING_EYE + V((0, -0.016, -0.012))
    for i in range(7):
        c = top + V((0.002 * math.sin(i), -0.017 - i * 0.031, -0.004 * i))
        o.append(_link(f"Hang{i}", c, (0, -1, 0), i, iron))
    # on the floor below
    rnd = random.Random(23)
    p = V((0.05, 0.004, -0.3))
    d = V((1, 0, 0.3)).normalized()
    for i in range(6):
        o.append(_link(f"Floor{i}", p, d, i, iron))
        d = (rot((0, 1, 0), rnd.uniform(-0.6, 0.6)) @ d).normalized()
        p = p + d * 0.031
    lock = _padlock(p + V((0.02, 0.012, 0)), 0.0, steel, brass)
    for ob in lock:
        # (lying on its side on the floor)
        M = Matrix.Translation(P(p)) @ Matrix.Rotation(math.pi / 2, 4, "Y") @ Matrix.Translation(-P(p))
        ob.data.transform(M)
    return o + lock


# ------------------------------------------------------------------ the bare cot in the locked room

def bare_cot():
    """fixtures.cot's frame and canvas, bare (no pad, no pillow), a grey blanket bunched at its foot with a corner down
    to the floor. Its length along z, centred, its foot on y = 0."""
    o = drop(FX.cot(), "Pad", "Pillow", "Blanket", "Fold")
    blanket = F.role("blanket")
    rnd = random.Random(88)
    cols = []
    for i in range(14):
        t = i / 13
        z = 0.95 - 0.06 - 0.7 * t
        row = []
        for k in range(15):
            u = k / 14 * 2 - 1
            lump = 0.05 * math.sin(t * 9 + u * 4) * (1 - abs(u)) + rnd.uniform(0, 0.015)
            if abs(u) <= 0.75:
                x, y = u * 0.4, 0.4 + 0.03 + lump * (1 - t * 0.6)
            else:
                v = (abs(u) - 0.75) / 0.25
                drape = 0.38 if (u > 0 and t > 0.45) else 0.16
                x = math.copysign(0.4 + 0.02 * v, u)
                y = 0.42 - drape * v * v
            row.append((x, max(0.004, y), z))
        cols.append(row)
    o.append(grid("Blanket", cols, blanket, thick=0.008))
    return o


# ------------------------------------------------------------------ Room 3: the ranger staff photos, faces scratched out

def staff_photo(seed=1):
    """A framed staff photograph: the station's rangers in a row in their uniforms and campaign hats, the station's
    porch behind them; every face scratched out of the print. Its back on the wall (z = 0), front toward -Z, centred."""
    frame, mat, prnt, uniform, hat, face, scratch, brass = (F.role(r) for r in ("frame", "mat", "print", "uniform", "hat", "face", "scratch", "brass"))
    W, H, T = 0.62, 0.46, 0.028
    o = []
    xf = lambda u, v, d: V((u, v, d))
    outer = [(-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2), (-W / 2, H / 2)]
    iw, ih = W / 2 - 0.035, H / 2 - 0.035
    inner = [(-iw, -ih), (iw, -ih), (iw, ih), (-iw, ih)]
    for i in range(4):
        o.append(slab(f"Rail{i}", [outer[i], outer[(i + 1) % 4], inner[(i + 1) % 4], inner[i]], xf, -T, 0.0, frame))
    o.append(slab("Mat", inner, xf, -0.012, -0.006, mat))
    pw, ph = 0.24, 0.15
    o.append(slab("Print", [(-pw, -ph + 0.02), (pw, -ph + 0.02), (pw, ph), (-pw, ph)], xf, -0.0135, -0.012, prnt))
    o.append(slab("Plaque", [(-0.06, -0.19), (0.06, -0.19), (0.06, -0.165), (-0.06, -0.165)], xf, -0.0135, -0.012, brass))
    # the porch behind: its roof line and posts, a shade darker
    o.append(slab("Porch", [(-pw, 0.03), (pw, 0.03), (pw, 0.1), (0.0, 0.14), (-pw, 0.1)], xf, -0.0142, -0.0135, uniform))
    rnd = random.Random(seed)
    n = 4 if seed % 2 else 5
    for i in range(n):
        x = -pw + (2 * pw) * (i + 0.5) / n + rnd.uniform(-0.008, 0.008)
        tall = rnd.uniform(0.95, 1.06)
        fy = -ph + 0.03
        h = 0.2 * tall
        body = [(x - 0.022, fy), (x + 0.022, fy), (x + 0.026, fy + h * 0.62), (x + 0.03, fy + h * 0.75), (x + 0.02, fy + h * 0.8),
                (x - 0.02, fy + h * 0.8), (x - 0.03, fy + h * 0.75), (x - 0.026, fy + h * 0.62)]
        o.append(slab(f"Body{i}", body, xf, -0.0149, -0.0142, uniform))
        hy = fy + h * 0.86
        o.append(slab(f"Face{i}", ellipse(x, hy, 0.0115, 0.0145, 14), xf, -0.0149, -0.0142, face))
        o.append(slab(f"Brim{i}", ellipse(x, hy + 0.012, 0.024, 0.0045, 14), xf, -0.0156, -0.0149, hat))
        o.append(slab(f"Crown{i}", [(x - 0.011, hy + 0.012), (x + 0.011, hy + 0.012), (x + 0.007, hy + 0.028), (x, hy + 0.024), (x - 0.007, hy + 0.028)],
                      xf, -0.0156, -0.0149, hat))
        # the face scratched out: a ragged pale knot of scratches, a few strokes running wild of it
        knot = ellipse(x, hy - 0.001, 0.014, 0.017, 16, wob=0.3, seed=seed * 10 + i)
        o.append(slab(f"Scratch{i}", knot, xf, -0.0163, -0.0156, scratch))
        for k in range(3):
            a = rnd.uniform(0, math.pi)
            l = rnd.uniform(0.02, 0.04)
            cx, cy = x + rnd.uniform(-0.006, 0.006), hy + rnd.uniform(-0.006, 0.006)
            dx, dy = math.cos(a) * l, math.sin(a) * l
            nx, ny = -math.sin(a) * 0.0012, math.cos(a) * 0.0012
            o.append(slab(f"Stroke{i}{k}", [(cx - dx - nx, cy - dy - ny), (cx + dx - nx, cy + dy - ny), (cx + dx + nx, cy + dy + ny), (cx - dx + nx, cy - dy + ny)],
                          xf, -0.0164, -0.0157, scratch))
    return o


PIECES = {
    "ss_desk_photo": (desk_photo, (0, 0, 0.085), 0.42, 0.06, 25),
    "ss_nameplate": (nameplate, (0, 0, 0.02), 0.4, 0.15, 25),
    "ss_desk_open1": (lambda: desk_open(1), (0, 0, 0.4), 3.0, 0.6, 25),
    "ss_desk_open2": (lambda: desk_open(2), (0, 0, 0.4), 3.0, 0.6, 25),
    "ss_station_clock": (station_clock, (0, 0, 1.6), 1.6, 0.3, 25),
    "ss_key_rack": (key_rack, (0, 0, 0.05), 0.8, 0.1, 25),
    "ss_boarded_window": (boarded_window, (0, 0, 0.6), 2.6, 0.2, 25),
    "ss_duffel": (duffel, (0, 0, 0.12), 1.4, 0.5, 25),
    "ss_logbook": (logbook, (0, 0, 0.0), 0.8, 0.5, 25),
    "ss_log_table": (log_table, (0, 0, 0.4), 2.2, 0.6, 25),
    "ss_coat_button": (coat_button, (0, 0, 0.004), 0.09, 0.05, 25),
    "ss_watch": (watch, (0, 0, 0.01), 0.15, 0.08, 25),
    "ss_ring": (ring, (0, 0, 0.002), 0.06, 0.03, 25),
    "ss_hasp": (hasp, (0, 0, 0.0), 0.3, 0.0, 25),
    "ss_door_chain": (door_chain, (-0.04, 0.08, 0.95), 0.7, 0.0, 25),
    "ss_chain_cut": (chain_cut, (0.0, 0.2, 0.6), 1.4, 0.1, 25),
    "ss_bare_cot": (bare_cot, (0, 0, 0.3), 3.2, 1.0, 25),
    "ss_staff_photo_a": (lambda: staff_photo(1), (0, 0, 0.0), 1.0, 0.0, 25),
    "ss_staff_photo_b": (lambda: staff_photo(2), (0, 0, 0.0), 1.0, 0.0, 25),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        clear()
        fn, tgt, dist, h, yaw = PIECES[name]
        F.export(fn(), name, tgt, dist, h, yaw)
