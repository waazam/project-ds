"""Act 12's rowboat and its oars, modelled in Blender (2026-10-15, the owner's rule: every model made in Blender). Run headless:

    blender -b --python tools/Blender/rowboat.py

Writes assets/models/boat/rowboat.glb and a preview in build/blender/rowboat.png.

The hull follows the game's own lines (Rowboat.cs: GunwaleHalf, SheerY, KeelY, Skin), so the seat, the oarlocks and the
collision box line up with it: a small clinker-built open boat, 3.9 m by 1.4 m, a pointed bow and a flat transom, five
real planks a side each lapping outside the one below, a stem post, a rubbing rail along each gunwale, sawn frames
(ribs) inside, the bottom boards with gaps between them, three thwarts with knees, bronze oarlocks on wooden pads, and a
skin of dirty bilge water slopping under the boards.

Coordinates: the game's boat-local space (origin at the waterline amidships, bow toward -Z, up +Y). Blender is Z-up and the
glTF export turns Blender +Y into the game's -Z, so the bow is modelled toward Blender +Y: game (x, y, z) -> Blender (x, -z, y).

Objects (the game paints each by its material's name with its own textures, keeping the PS2 look):
  Hull     paint (the planks outside, the transom), wood (the lining, the bottom boards, the thwarts), post (frames, stem,
           rails, pads, knees), bronze (the oarlock horns), bilge (the water)
  OarL/OarR   post (the loom and blade), leather (the collar at the lock), tip (the painted blade end); each along the
           game's X from its lock (OarR toward +X, OarL toward -X), the lock at its origin.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "boat")
os.makedirs(kit.OUT, exist_ok=True)

LENGTH, BEAM, DEPTH, DRAFT = 3.9, 1.4, 0.56, 0.2
HALF = LENGTH * 0.5
SEAT_Z = 0.28
OARLOCK_Z = SEAT_Z - 0.42
INBOARD, OUTBOARD = 0.55, 1.9


def G(x, y, z):
    """Game boat-local -> Blender."""
    return Vector((x, -z, y))


def gunwale_half(t):
    return BEAM * 0.5 * (math.sqrt(max(0.0, 1.0 - t * t * 0.98)) if t < 0 else 1.0 - 0.32 * t * t)


def sheer_y(t):
    return DEPTH - DRAFT + (0.2 if t < 0 else 0.08) * t * t


def keel_y(t):
    return -DRAFT + (0.16 if t < 0 else 0.07) * t * t


def skin(t, s, side):
    """A point on the hull skin, in game space: t along (-1 bow .. 1 stern), s keel (0) .. gunwale (1)."""
    gw = gunwale_half(t)
    bw = gw * 0.55
    x = bw + (gw - bw) * (max(s, 0.0) ** 0.7) + math.sin(max(s, 0.0) * math.pi) * 0.05 * gw / (BEAM * 0.5)
    # (2026-10-15, the owner: "a hole in the front of it") the bow closes: over its last ~0.25 m the planks pinch in to land
    # in the stem's rabbet, 1.5 cm off the centre line, instead of stopping 10-20 cm apart with nothing between
    f = min(max((-0.87 - t) / 0.13, 0.0), 1.0)
    f = f * f * (3 - 2 * f)
    x = x * (1 - f) + 0.015 * f
    y = keel_y(t) + (sheer_y(t) - keel_y(t)) * s
    return (side * x, y, t * HALF)


def oarlock(side):
    x, y, z = skin(OARLOCK_Z / HALF, 1.0, side)
    return (x, y + 0.07, z)


def grid(name, rows, mat_, smooth=True):
    """A mesh from a grid of game-space points rows[j][i]."""
    bm = bmesh.new()
    vs = [[bm.verts.new(G(*p)) for p in row] for row in rows]
    for j in range(len(rows) - 1):
        for i in range(len(rows[0]) - 1):
            bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name, mat_, smooth)


def solidify(ob, thick, offset=-1.0):
    add_mod(ob, "SOLIDIFY", thickness=thick, offset=offset, use_even_offset=True)
    apply_mods(ob)


def bevel(ob, width, segs=2):
    add_mod(ob, "BEVEL", width=width, segments=segs, limit_method="ANGLE")
    apply_mods(ob)


def tube(name, pts, radius, mat_, rect=None, res=6):
    """A swept bar along game-space points: round (radius) or a rounded rectangle (rect = (w, h))."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        sp.points[i].co = (*G(*p), 1.0)
    if rect:
        prof = bpy.data.curves.new(name + "_prof", "CURVE")
        ps = prof.splines.new("POLY")
        w, h = rect
        r = min(w, h) * 0.3
        corners = []
        for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
            for k in range(3):
                a = math.radians(a0 + k * 45)
                corners.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
        ps.points.add(len(corners) - 1)
        for i, (x, y) in enumerate(corners):
            ps.points[i].co = (x, y, 0, 1)
        ps.use_cyclic_u = True
        po = bpy.data.objects.new(name + "_prof", prof)
        bpy.context.scene.collection.objects.link(po)
        cu.bevel_mode = "OBJECT"
        cu.bevel_object = po
    else:
        cu.bevel_depth = radius
        cu.bevel_resolution = res // 2
    cu.use_fill_caps = True
    ob = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    ob = bpy.context.view_layer.objects.active
    ob.data.materials.clear()
    ob.data.materials.append(mat_)
    for p in ob.data.polygons:
        p.use_smooth = True
    if rect:
        bpy.data.objects.remove(bpy.data.objects[name + "_prof"])
    return ob


def rowboat():
    clear()
    paint = mat("paint", (0.46, 0.56, 0.5), 0.85)
    wood = mat("wood", (0.5, 0.46, 0.4), 0.8)
    post = mat("post", (0.44, 0.38, 0.3), 0.85)
    bronze = mat("bronze", (0.55, 0.42, 0.25), 0.4, 0.8)
    bilge = mat("bilge", (0.08, 0.07, 0.05), 0.08)
    parts = []
    T = [-1.0 + 2.0 * i / 40 for i in range(41)]

    # the planks outside: five a side, each lapping outside the one below (pushed out a plank's thickness per strake)
    for side in (-1, 1):
        for s in range(5):
            s0, s1 = s / 5 - 0.035, (s + 1) / 5
            rows = []
            for sv in (s0, (s0 + s1) / 2, s1):
                row = []
                for t in T:
                    x, y, z = skin(t, max(sv, 0.0) if s == 0 else sv, side)
                    row.append((x + side * 0.007 * s, y, z))
                rows.append(row)
            pl = grid(f"plank{side}{s}", rows, paint)
            solidify(pl, 0.012, -1.0 if side > 0 else 1.0)
            bevel(pl, 0.003, 1)
            parts.append(pl)
        # the lining inside: the same five strakes, bare wood, set in by the frames' depth
        for s in range(5):
            s0, s1 = s / 5 + 0.004, (s + 1) / 5 - 0.004
            rows = [[(side * max(abs(skin(t, sv, side)[0]) - 0.035, 0.03), skin(t, sv, side)[1], t * HALF) for t in T[1:-1]] for sv in (s0, s1)]
            ln = grid(f"lining{side}{s}", rows, wood)
            solidify(ln, 0.01, 1.0 if side > 0 else -1.0)
            parts.append(ln)

    # the bottom, outside
    rows = [[skin(t, 0.0, side) for t in T] for side in (-1, 1)]
    bot = grid("bottom", rows, paint)
    solidify(bot, 0.015)
    parts.append(bot)

    # the bottom boards inside: four boards with gaps, lying on the frames
    for b in range(4):
        u0, u1 = -1 + b * 0.5 + 0.02, -1 + (b + 1) * 0.5 - 0.02
        rows = []
        for t in [x for x in T if -0.82 <= x <= 0.86]:
            gw = gunwale_half(t) * 0.55 - 0.03
            row = []
            for u in (u0, u1):
                row.append((u * gw, keel_y(t) + 0.04 + abs(u) * 0.025, t * HALF))
            rows.append(row)
        bb = grid(f"board{b}", rows, wood)
        solidify(bb, 0.018, 1.0)
        bevel(bb, 0.004, 1)
        parts.append(bb)

    # the transom: a board closing the stern, its top following the sheer
    rows = [[skin(1.0, s / 6, side) for side in (-1, 1)] for s in range(7)]
    tr = grid("transom", rows, paint)
    solidify(tr, 0.03, 1.0)
    bevel(tr, 0.006, 2)
    parts.append(tr)

    # the stem post at the bow, the frames inside, and the rubbing rails along the gunwales
    stem = [(0.0, keel_y(-1) - 0.03 + 0.02 * k, -HALF - 0.02 - 0.03 * k * k) for k in range(0, 2)]
    stem = [(0.0, keel_y(-1.0) - 0.02, -HALF + 0.02), (0.0, (keel_y(-1) + sheer_y(-1)) * 0.5, -HALF - 0.03), (0.0, sheer_y(-1.0) + 0.1, -HALF - 0.06)]
    parts.append(tube("stem", stem, 0, post, rect=(0.07, 0.11)))
    # the apron inside the stem: the planks' ends land on it (the rabbet), closing the bow from inside
    apron = [(0.0, keel_y(-0.93) + 0.03, -HALF * 0.93), (0.0, (keel_y(-0.97) + sheer_y(-0.97)) * 0.5, -HALF * 0.97),
             (0.0, sheer_y(-0.97) - 0.02, -HALF * 0.975)]
    parts.append(tube("apron", apron, 0, post, rect=(0.09, 0.08)))
    # the breasthook: a knee of wood across the bow at the top, tying the two gunwales to the stem
    bh = [skin(-0.86, 1.0, -1), (0.0, sheer_y(-0.97) - 0.01, -HALF * 0.97), skin(-0.86, 1.0, 1)]
    bh = [(p[0] * 0.92, p[1] - 0.03, p[2]) for p in bh]
    parts.append(tube("breasthook", bh, 0, post, rect=(0.05, 0.11)))
    for side in (-1, 1):
        pts = [(skin(t, 1.0, side)[0] + side * 0.012, skin(t, 1.0, side)[1] + 0.012, t * HALF) for t in T]
        parts.append(tube(f"rail{side}", pts, 0, post, rect=(0.05, 0.045)))
        # the inwale: a lighter strip along the inside of the sheer
        pts = [(skin(t, 1.0, side)[0] - side * 0.045, skin(t, 1.0, side)[1] - 0.02, t * HALF) for t in T[3:-2]]
        parts.append(tube(f"inwale{side}", pts, 0, wood, rect=(0.03, 0.05)))
    t = -0.66
    while t < 0.92:
        pts = []
        for side in (-1, 1):
            seq = [k / 6 for k in range(7)]
            if side < 0:
                seq = list(reversed(seq))
            for sv in seq:
                x, y, z = skin(t, sv, side)
                pts.append((x - side * 0.03, y + 0.02, z))
        parts.append(tube(f"frame{t:.2f}", pts, 0, post, rect=(0.045, 0.032)))
        t += 0.24

    # the thwarts (bow, the rower's, stern), each with a knee to the side either end
    for (tt, hy) in ((-0.55, 0.62), (SEAT_Z / HALF, 0.58), (0.7, 0.66)):
        gw = gunwale_half(tt) - 0.05
        y = keel_y(tt) + (sheer_y(tt) - keel_y(tt)) * hy
        th = box(f"thwart{tt:.2f}", G(0, y, tt * HALF), (gw * 2.0, 0.24, 0.045), wood, bevel=0.008, segs=2)
        parts.append(th)
        for side in (-1, 1):
            x, ys, z = skin(tt, hy + 0.12, side)
            pts = [(side * (gw - 0.12), y - 0.02, tt * HALF), (x - side * 0.04, ys - 0.03, tt * HALF), (x - side * 0.035, ys + 0.12, tt * HALF)]
            parts.append(tube(f"knee{tt:.2f}{side}", pts, 0, post, rect=(0.04, 0.05)))

    # the oarlocks: a wooden pad on the rail and a bronze horn (a U in its socket)
    for side in (-1, 1):
        lx, ly, lz = oarlock(side)
        parts.append(box(f"pad{side}", G(lx, ly - 0.03, lz), (0.1, 0.3, 0.05), post, bevel=0.012, segs=2))
        horn = [(lx, ly - 0.03, lz - 0.05), (lx, ly + 0.05, lz - 0.05), (lx, ly + 0.085, lz - 0.025), (lx, ly + 0.085, lz + 0.025), (lx, ly + 0.05, lz + 0.05), (lx, ly - 0.03, lz + 0.05)]
        parts.append(tube(f"horn{side}", horn, 0.011, bronze, res=8))

    # bilge water under the boards, a little lapped up the sides
    rows = []
    for t in [x for x in T if -0.45 <= x <= 0.6]:
        y = -0.15
        s = (y - keel_y(t)) / (sheer_y(t) - keel_y(t))
        xs = skin(t, max(s, 0.0), 1)[0] - 0.045
        rows.append([(-xs, y, t * HALF), (0.0, y - 0.004, t * HALF), (xs, y, t * HALF)])
    parts.append(grid("bilge", rows, bilge))

    hull = join(parts, "Hull")
    uv_box(hull, 1.0)
    for p in hull.data.polygons:
        p.use_smooth = True
    add_mod(hull, "WEIGHTED_NORMAL", keep_sharp=True)
    apply_mods(hull)

    oars = [oar(side) for side in (-1, 1)]
    export([hull] + oars, "rowboat")
    for i, o in enumerate(oars):
        o.location = G(*oarlock(-1 if i == 0 else 1))
    preview("rowboat", (0, 0, 0.1), 5.2, height=2.6, yaw=-130)


def oar(side):
    """An oar from its lock at the origin, out along the game's +X (side +1) or -X (side -1): the grip and the loom inboard,
    a leather collar at the lock, the shaft tapering to a flat blade, its end painted."""
    post = mat("post", (0.44, 0.38, 0.3), 0.85)
    leather = mat("leather", (0.3, 0.22, 0.16), 0.7)
    tip = mat("tip", (0.52, 0.14, 0.1), 0.8)
    parts = []
    s = side
    # the loom and shaft, one lathed piece: radius along the length (x from -INBOARD to the blade's root)
    prof = [(-INBOARD, 0.0), (-INBOARD, 0.019), (-INBOARD + 0.01, 0.022), (-INBOARD + 0.15, 0.022), (-INBOARD + 0.17, 0.026),
            (-0.1, 0.031), (0.0, 0.032), (OUTBOARD * 0.6, 0.026), (OUTBOARD * 0.64, 0.02)]
    bm = bmesh.new()
    rings = []
    seg = 14
    for x, r in prof:
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            ring.append(bm.verts.new(G(s * x, math.sin(a) * r, math.cos(a) * r)))
        rings.append(ring)
    for j in range(len(rings) - 1):
        for k in range(seg):
            bm.faces.new((rings[j][k], rings[j][(k + 1) % seg], rings[j + 1][(k + 1) % seg], rings[j + 1][k]))
    bm.faces.new(rings[0])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    parts.append(obj_from_bmesh(bm, "loom", post))
    # the blade: flat, widening, a spine down its middle, thinning to the edges
    bm = bmesh.new()
    xs = [OUTBOARD * (0.6 + 0.4 * k / 10) for k in range(11)]
    rows = []
    for k, x in enumerate(xs):
        u = k / 10
        w = 0.024 + (0.085 - 0.024) * (u ** 0.6)
        row = []
        for c in range(7):
            v = c / 6 * 2 - 1
            thick = 0.012 * (1 - 0.75 * abs(v)) * (1 - 0.3 * u) + 0.002
            row.append((s * x, thick, v * w))
        rows.append(row)
    top = [[bm.verts.new(G(*p)) for p in row] for row in rows]
    bot = [[bm.verts.new(G(p[0], -p[1], p[2])) for p in row] for row in rows]
    for j in range(len(rows) - 1):
        for i in range(6):
            bm.faces.new((top[j][i], top[j][i + 1], top[j + 1][i + 1], top[j + 1][i]))
            bm.faces.new((bot[j][i], bot[j + 1][i], bot[j + 1][i + 1], bot[j][i + 1]))
    for j in range(len(rows) - 1):
        bm.faces.new((top[j][0], top[j + 1][0], bot[j + 1][0], bot[j][0]))
        bm.faces.new((top[j][6], bot[j][6], bot[j + 1][6], top[j + 1][6]))
    last = len(rows) - 1
    for i in range(6):
        bm.faces.new((top[last][i], bot[last][i], bot[last][i + 1], top[last][i + 1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    blade = obj_from_bmesh(bm, "blade", post)
    # the painted end: the last 7 % of the blade's faces get the tip paint
    blade.data.materials.append(tip)
    for p in blade.data.polygons:
        cx = sum(blade.data.vertices[v].co.x for v in p.vertices) / len(p.vertices)
        if abs(cx) > OUTBOARD * 0.93:
            p.material_index = 1
    parts.append(blade)
    # the leather collar at the lock
    parts.append(cylinder("collar", G(-s * 0.1, 0, 0), G(s * 0.1, 0, 0), 0.037, 0.037, 16, leather))
    o = join(parts, "OarR" if side > 0 else "OarL")
    uv_box(o, 1.0)
    add_mod(o, "WEIGHTED_NORMAL", keep_sharp=True)
    apply_mods(o)
    return o


if __name__ == "__main__":
    rowboat()
