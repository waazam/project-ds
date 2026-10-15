"""The church's furnishings (Acts 20-21), the library's bookcases, books and journal (Act 19) and the burnt portraits'
frames (Acts 13-14), modelled in Blender (the owner, 2026-10-15: "from now on everything should be made in blender").
They were built in C# (Church.Gothic, Church.Interior, Library.Shelves, LibraryJournal, PortraitKit); each piece is
drawn here in its builder's own local frame, through fixtures.P (game coordinates, Y up), and exported through the
furniture pipeline to assets/models/furniture/<name>.glb:

    blender -b --python tools/Blender/church_props.py [-- name ...]

The C# builders stay as fallbacks when a .glb is missing. What's parametric in the game is cut so a fixed model fits it:
the bookcase's crown and shelves are a metre long (scaled along their run), the books a unit box (scaled per book, as
the MultiMesh always did), the portrait frames nine-sliced (a mitred corner scaled (rim, rim, depth) and a straight rail
scaled along its side), so no mitre or moulding is stretched out of true.
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furniture as F
from kit import obj_from_bmesh, add_mod, apply_mods
from fixtures import P, tube, cyl, lathe, box, grid, blob, rot, role, _pairs

V = Vector
UP = V((0, 1, 0))


# ------------------------------------------------------------------ helpers (game coordinates)

def beam(name, a, b, w, h, m, bevel=0.004):
    """A square bar from a to b, w across and h deep (MeshKit.Beam)."""
    a, b = V(a), V(b)
    d = b - a
    L = d.length
    d.normalize()
    side = d.cross(UP) if abs(d.y) < 0.95 else d.cross(V((1, 0, 0)))
    side.normalize()
    up = d.cross(side).normalized()
    basis = Matrix((side, up, d)).transposed()
    return box(name, (a + b) * 0.5, (w, h, L), m, bevel=bevel, segs=1, basis=basis)


def torus(name, c, basis, R, r, m, segs=24, rsegs=8):
    """A ring in the local XY plane of `basis` (its axis local Z) at c."""
    bm = bmesh.new()
    rings = []
    for i in range(segs):
        a = math.tau * i / segs
        ca, sa = math.cos(a), math.sin(a)
        ring = []
        for j in range(rsegs):
            b = math.tau * j / rsegs
            q = V(((R + r * math.cos(b)) * ca, (R + r * math.cos(b)) * sa, r * math.sin(b)))
            ring.append(bm.verts.new(P(V(c) + basis @ q)))
        rings.append(ring)
    for i in range(segs):
        for j in range(rsegs):
            i1, j1 = (i + 1) % segs, (j + 1) % rsegs
            bm.faces.new((rings[i][j], rings[i1][j], rings[i1][j1], rings[i][j1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name, m, smooth=True)


def octa(name, prof, m, at=(0, 0, 0), **kw):
    """An octagonal turned piece, its corners at pi/8 (MeshKit.Cylinder's 8 sides turned pi/8; radii to the corners)."""
    return lathe(name, prof, m, at=at, segs=8, basis=rot((0, 1, 0), -math.pi / 8), **kw)


def pointed(x, half, r):
    ax = min(abs(x), half)
    c = half - r
    return math.sqrt(max(0.0, r * r - (ax - c) * (ax - c)))


def sheet(name, quads_fn, m):
    """Faces given directly (wound for their front): quads_fn(bm, vert) adds them."""
    bm = bmesh.new()
    cache = {}

    def vert(p):
        key = tuple(round(c, 6) for c in p)
        if key not in cache:
            cache[key] = bm.verts.new(P(V(p)))
        return cache[key]
    quads_fn(bm, vert)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=bm.edges)
    return obj_from_bmesh(bm, name, m, smooth=True)


def face(bm, vs):
    vs2 = []
    for v in vs:
        if not vs2 or vs2[-1] is not v:
            vs2.append(v)
    if len(vs2) > 1 and vs2[0] is vs2[-1]:
        vs2.pop()
    if len(set(vs2)) >= 3:
        try:
            bm.faces.new(list(reversed(vs2)))   # (wound for the game's front; P() swaps two axes, so reversed in Blender's)
        except ValueError:
            pass


def extrude_profile(name, prof, m, x0=-0.5, x1=0.5, closed=True):
    """A moulding along game X: prof [(z, y), ...] in the YZ plane (z toward the room is -Z), swept from x0 to x1, its
    ends capped."""
    bm = bmesh.new()
    a = [bm.verts.new(P(V((x0, y, z)))) for (z, y) in prof]
    b = [bm.verts.new(P(V((x1, y, z)))) for (z, y) in prof]
    n = len(prof)
    for i in range(n if closed else n - 1):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    if closed:
        bm.faces.new(a)
        bm.faces.new(list(reversed(b)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name, m, smooth=False)


# ------------------------------------------------------------------ the candle alcoves' votive racks

def votive_rack():
    """Church.BuildCandleAlcoves' iron rack at (x, 0, z) for the east side (s = +1; the west's is turned pi): three
    stepped trays on four legs, the lowest nearest the aisle; the candles stay in code (each rack burns its own)."""
    iron = role("iron")
    o = []
    for t in range(3):
        y, xx = 0.7 + t * 0.22, t * 0.16
        o.append(box(f"Tray{t}", (xx, y, 0), (0.2, 0.025, 1.6), iron, bevel=0.004, segs=1))
        # a lip round the tray (the wax runs off it)
        for sx in (-1, 1):
            o.append(box(f"LipX{t}{sx}", (xx + sx * 0.098, y + 0.022, 0), (0.006, 0.02, 1.6), iron, bevel=0.002, segs=1))
        for sz in (-1, 1):
            o.append(box(f"LipZ{t}{sz}", (xx, y + 0.022, sz * 0.798), (0.2, 0.02, 0.006), iron, bevel=0.002, segs=1))
        # the bracket under it, to the legs
        for dz in (-0.78, 0.78):
            o.append(beam(f"Brk{t}{dz}", (0, y - 0.15, dz), (xx + 0.08, y - 0.015, dz), 0.016, 0.016, iron, bevel=0.002))
    for dz in (-0.78, 0.78):
        for dx in (0.0, 0.32):
            o.append(box(f"Leg{dx}{dz}", (dx, 0.6, dz), (0.025, 1.2, 0.025), iron, bevel=0.003, segs=1))
            o.append(lathe(f"Foot{dx}{dz}", [(0.0, 0.0), (0.03, 0.0), (0.03, 0.008), (0.018, 0.016), (0.0, 0.017)], iron, at=(dx, 0, dz), segs=10))
            o.append(lathe(f"Knop{dx}{dz}", [(0.0, 1.195), (0.018, 1.2), (0.024, 1.22), (0.016, 1.24), (0.006, 1.255), (0.0, 1.26)], iron, at=(dx, 0, dz), segs=10))
        # the stretcher between front and back legs, low down
        o.append(beam(f"StrX{dz}", (0, 0.14, dz), (0.32, 0.14, dz), 0.016, 0.016, iron, bevel=0.002))
    for dx in (0.0, 0.32):
        o.append(beam(f"StrZ{dx}", (dx, 0.14, -0.78), (dx, 0.14, 0.78), 0.016, 0.016, iron, bevel=0.002))
    # the back's cresting: a rail along the top and a row of iron scrolls on it
    o.append(beam("Crest", (0.32, 1.2, -0.78), (0.32, 1.2, 0.78), 0.02, 0.02, iron, bevel=0.003))
    for i in range(5):
        z = -0.6 + i * 0.3
        pts = []
        for k in range(14):
            a = k / 13 * math.pi * 1.6
            r = 0.07 * (1 - k / 18)
            pts.append((0.32, 1.2 + 0.07 - math.cos(a) * r, z + math.sin(a) * r * 0.9))
        o.append(tube(f"Scroll{i}", pts, 0.006, iron, segs=6))
    return o


# ------------------------------------------------------------------ the crossing's crown of candles

def crown():
    """Church.BuildCrossingDais' corona at cc (the long chain to the vault stays in code): two round hoops, a band of
    uprights, a spike between each pair of candles, twelve cups, the chains up to the boss."""
    iron = role("iron")
    ident = Matrix.Rotation(-math.pi / 2, 3, V((1, 0, 0)))   # local XY -> the game's XZ (the ring lies flat)
    o = [torus("Hoop0", (0, 0, 0), ident, 1.7, 0.03, iron, 72, 8),
         torus("Hoop1", (0, 0.35, 0), ident, 1.7, 0.03, iron, 72, 8),
         torus("Hoop2", (0, 0.175, 0), ident, 1.7, 0.012, iron, 72, 6)]
    for i in range(24):
        a = math.tau * (i + 0.5) / 24
        x, z = math.cos(a) * 1.7, math.sin(a) * 1.7
        o.append(cyl(f"Up{i}", (x, 0, z), (x, 0.35, z), 0.012, 0.012, iron, 6, False))
        # a little spike over the top hoop, and a drop under the lower
        o.append(lathe(f"Spike{i}", [(0.0, 0.36), (0.016, 0.37), (0.008, 0.4), (0.0, 0.47)], iron, at=(x, 0, z), segs=6))
        o.append(lathe(f"Drop{i}", [(0.0, -0.11), (0.012, -0.08), (0.016, -0.05), (0.008, -0.02), (0.0, 0.0)], iron, at=(x, 0, z), segs=6))
    # the boss the chains meet at, and its hanging eye
    o.append(lathe("Boss", [(0.0, 1.02), (0.03, 1.04), (0.06, 1.09), (0.07, 1.12), (0.05, 1.28), (0.035, 1.3), (0.0, 1.31)], iron, segs=12))
    o.append(torus("Eye", (0, 1.36, 0), Matrix.Identity(3), 0.045, 0.01, iron, 12, 5))
    cup = _pairs(0, 0, 0.025, 0, 0.03, 0.012, 0.06, 0.025, 0.065, 0.04, 0.055, 0.042, 0.035, 0.035, 0, 0.036)
    for i in range(12):
        a = math.tau * i / 12
        p = V((math.cos(a) * 1.7, 0.35, math.sin(a) * 1.7))
        o.append(cyl(f"Chain{i}", p + UP * 0.03, (0, 1.2, 0), 0.01, 0.01, iron, 6, False))
        o.append(lathe(f"Cup{i}", cup, iron, at=p, segs=12))
        o.append(lathe(f"Pricket{i}", [(0.0, 0.03), (0.004, 0.03), (0.0, 0.07)], iron, at=p, segs=5))
    return o


# ------------------------------------------------------------------ the transepts' altars

def trans_altar():
    """Church.BuildTranseptAltars' altar at `at` (the same each side; the cloth's tint and the blood are the game's):
    a stone footpace, the block with a moulded base and a mensa, the cloth over it, the iron cross on its stepped
    base. The candlesticks are LampKit's (fx_lk_candlestick)."""
    stone, cloth, iron = role("stone"), role("cloth"), role("iron")
    o = [box("Foot", (0, 0.1, 0), (3.0, 0.2, 1.8), stone, bevel=0.02, segs=2),
         box("Base", (0, 0.25, 0.2), (2.24, 0.1, 1.04), stone, bevel=0.025, segs=2),
         box("Block", (0, 0.72, 0.2), (2.1, 0.88, 0.9), stone, bevel=0.012, segs=1),
         box("Mensa", (0, 1.17, 0.2), (2.2, 0.06, 1.0), stone, bevel=0.012, segs=2)]
    # the block's front: three sunk panels (the cloth's frontal hides their heads)
    for i in range(3):
        o.append(box(f"Panel{i}", (-0.66 + i * 0.66, 0.6, -0.252), (0.5, 0.5, 0.012), stone, bevel=0.006, segs=1))
    # the cloth: over the top, a frontal hanging in shallow folds, and short drops at the ends
    o.append(box("ClothTop", (0, 1.21, 0.2), (2.24, 0.02, 1.04), cloth, bevel=0.006, segs=1))

    def frontal(z, x0, x1, y0, y1, n=28, folds=7, amp=0.012):
        rows = []
        for j in range(8):
            y = y1 + (y0 - y1) * j / 7
            row = []
            for i in range(n + 1):
                x = x0 + (x1 - x0) * i / n
                f = math.sin((x - x0) / (x1 - x0) * math.pi * folds) * amp * (j / 7)
                hem = 0.015 * math.sin(i * 1.7) * (1 if j == 7 else 0)
                row.append((x, y + hem, z - f))
            rows.append(row)
        return rows
    o.append(grid("Frontal", frontal(-0.33, -1.12, 1.12, 0.8, 1.22), cloth, thick=0.01))
    for sx in (-1, 1):
        rows = [[(sx * 1.13 + sx * 0.008 * (j / 6), 1.22 - 0.3 * j / 6, -0.32 + 1.04 * i / 6) for i in range(7)] for j in range(7)]
        o.append(grid(f"Drop{sx}", rows, cloth, thick=0.008))
    # the iron cross (flared terminals, its foot in a stepped base)
    o += [box("Stem", (0, 1.8, 0.5), (0.05, 1.1, 0.05), iron, bevel=0.01, segs=1),
          box("Arm", (0, 2.05, 0.5), (0.5, 0.05, 0.05), iron, bevel=0.01, segs=1),
          box("TermL", (-0.25, 2.05, 0.5), (0.03, 0.09, 0.08), iron, bevel=0.008, segs=1),
          box("TermR", (0.25, 2.05, 0.5), (0.03, 0.09, 0.08), iron, bevel=0.008, segs=1),
          box("TermT", (0, 2.35, 0.5), (0.09, 0.03, 0.08), iron, bevel=0.008, segs=1),
          box("Step0", (0, 1.25, 0.5), (0.2, 0.03, 0.15), iron, bevel=0.008, segs=1),
          box("Step1", (0, 1.28, 0.5), (0.14, 0.04, 0.1), iron, bevel=0.008, segs=1),
          torus("Nimbus", (0, 2.05, 0.5), Matrix.Identity(3), 0.14, 0.008, iron, 32, 5)]
    return o


# ------------------------------------------------------------------ the high altar and its canopy

def high_altar():
    """Church.BuildChancel's high altar at (0, ChancelY, az): two red marble steps, the white marble block on a moulded
    base, its mensa, the gilded frontal between colonnettes, the jacquard cloth, the gold cross and two candlesticks
    (their candles in the 'wax' role)."""
    red, marble, gold, cloth, wax = role("red"), role("marble"), role("gold"), role("cloth"), role("wax")
    ay = 0.36
    o = [box("Step0", (0, 0.09, 0), (5.2, 0.18, 3.6), red, bevel=0.02, segs=2),
         box("Step1", (0, 0.27, 0), (4.4, 0.18, 2.8), red, bevel=0.02, segs=2),
         box("Base", (0, ay + 0.05, 0), (3.34, 0.1, 1.32), marble, bevel=0.03, segs=2),
         box("Block", (0, ay + 0.55, 0), (3.2, 0.9, 1.2), marble, bevel=0.01, segs=1),
         box("Mensa", (0, ay + 1.03, 0), (3.4, 0.06, 1.35), marble, bevel=0.015, segs=2),
         box("Frontal", (0, ay + 0.5, -0.61), (2.8, 0.7, 0.03), gold, bevel=0.006, segs=1),
         box("FrontalRim", (0, ay + 0.5, -0.6), (2.9, 0.78, 0.02), gold, bevel=0.008, segs=1)]
    for i in range(5):
        x = -1.2 + i * 0.6
        o.append(lathe(f"Col{i}", [(0.0, 0.0), (0.06, 0.0), (0.06, 0.03), (0.045, 0.05), (0.04, 0.06), (0.04, 0.59), (0.05, 0.61), (0.06, 0.65), (0.0, 0.65)],
                       gold, at=(x, ay + 0.2, -0.63), segs=12))
    # quatrefoils between the colonnettes, on the frontal
    for i in range(4):
        x = -0.9 + i * 0.6
        for k in range(4):
            a = math.pi / 2 * k
            o.append(torus(f"Foil{i}{k}", (x + math.cos(a) * 0.045, ay + 0.5 + math.sin(a) * 0.045, -0.63), Matrix.Identity(3), 0.05, 0.007, gold, 14, 4))
    o.append(box("ClothTop", (0, ay + 1.065, 0), (3.5, 0.01, 1.45), cloth, bevel=0.003, segs=1))
    o.append(box("ClothFront", (0, ay + 0.85, -0.73), (3.5, 0.42, 0.01), cloth, bevel=0.003, segs=1))
    for sx in (-1, 1):
        o.append(box(f"ClothEnd{sx}", (sx * 1.755, ay + 0.85, 0), (0.01, 0.42, 1.45), cloth, bevel=0.003, segs=1))
    # the cross
    top = V((0, ay + 1.07, 0.2))
    o.append(lathe("CrossFoot", [(0.0, 0.0), (0.16, 0.0), (0.16, 0.02), (0.13, 0.04), (0.12, 0.07), (0.1, 0.1), (0.06, 0.12), (0.045, 0.14), (0.0, 0.14)], gold, at=top, segs=16))
    o.append(cyl("CrossShaft", top + UP * 0.12, top + UP * 1.35, 0.035, 0.035, gold, 10))
    o.append(cyl("CrossArm", top + V((-0.31, 1.05, 0)), top + V((0.31, 1.05, 0)), 0.035, 0.035, gold, 10))
    for (dx, dy) in ((-0.33, 1.05), (0.33, 1.05), (0, 1.37)):
        o.append(blob(f"Fleur{dx}{dy}", top + V((dx, dy, 0)), (0.05, 0.05, 0.03), gold, seed=7, amp=0.15, subdiv=2))
    o.append(lathe("CrossKnop", [(0.0, 0.0), (0.05, 0.02), (0.06, 0.05), (0.04, 0.08), (0.0, 0.09)], gold, at=top + UP * 0.3, segs=12))
    # the candlesticks and their candles
    for x in (-1.2, 1.2):
        b = V((x, ay + 1.07, 0.1))
        o.append(lathe(f"Stick{x}", [(0.0, 0.0), (0.1, 0.0), (0.1, 0.015), (0.08, 0.04), (0.06, 0.08), (0.03, 0.1), (0.025, 0.12), (0.025, 0.25),
                                     (0.04, 0.27), (0.045, 0.3), (0.03, 0.33), (0.025, 0.35), (0.025, 0.53), (0.06, 0.55), (0.065, 0.58), (0.06, 0.6), (0.0, 0.6)],
                       gold, at=b, segs=16))
        o.append(lathe(f"Candle{x}", [(0.0, 0.0), (0.03, 0.0), (0.03, 0.24), (0.026, 0.25), (0.0, 0.252)], wax, at=(x, ay + 1.67, 0.1), segs=12))
    return o


def baldachin():
    """Church.BuildChancel's gilded canopy over the altar (same origin): four columns on bases with capitals, a
    pinnacle on each, the cornice, a pointed arch under a gable on every face, the canopy's top and a crocketed spire."""
    gold = role("gold")
    cx, cz, ch = 2.3, 2.0, 5.2
    y0 = 0.36
    o = []
    for x in (-cx, cx):
        for z in (-cz, cz):
            o.append(octa(f"Base{x}{z}", [(0.0, y0), (0.28, y0), (0.28, y0 + 0.08), (0.24, y0 + 0.14), (0.22, y0 + 0.3)], gold, at=(x, 0, z), cap_top=False))
            o.append(lathe(f"Shaft{x}{z}", [(0.13, y0 + 0.3), (0.13, y0 + ch)], gold, at=(x, 0, z), segs=16, cap_bottom=False, cap_top=False))
            for k in range(4):   # the shaft's clustered shafts
                a = math.pi / 2 * k + math.pi / 4
                o.append(cyl(f"Shf{x}{z}{k}", (x + math.cos(a) * 0.13, y0 + 0.3, z + math.sin(a) * 0.13), (x + math.cos(a) * 0.13, y0 + ch, z + math.sin(a) * 0.13), 0.04, 0.04, gold, 8, False))
            o.append(octa(f"Cap{x}{z}", [(0.14, y0 + ch - 0.05), (0.2, y0 + ch), (0.24, y0 + ch + 0.15), (0.27, y0 + ch + 0.2), (0.27, y0 + ch + 0.25), (0.0, y0 + ch + 0.25)], gold, at=(x, 0, z), cap_bottom=False))
            p = V((x, y0 + ch + 0.25, z))
            o.append(box(f"Pin{x}{z}", p + UP * 0.5, (0.36, 1.0, 0.36), gold, bevel=0.015, segs=1))
            o.append(lathe(f"Spire{x}{z}", [(0.25, 1.0), (0.2, 1.06), (0.01, 2.6)], gold, at=p, segs=4, basis=rot((0, 1, 0), math.pi / 4)))
            o.append(blob(f"PinFin{x}{z}", p + UP * 2.62, (0.05, 0.08, 0.05), gold, seed=3, amp=0.1, subdiv=1))
    cy = y0 + ch + 0.25
    faces = [((-cx, cy, -cz), (cx, cy, -cz)), ((-cx, cy, cz), (cx, cy, cz)), ((-cx, cy, -cz), (-cx, cy, cz)), ((cx, cy, -cz), (cx, cy, cz))]
    for fi, (a, b) in enumerate(faces):
        a, b = V(a), V(b)
        o.append(beam(f"Cornice{fi}", a, b, 0.3, 0.35, gold, bevel=0.02))
        mid, d = (a + b) * 0.5, (b - a).normalized()
        half = (b - a).length * 0.5
        pts = []
        for i in range(0, 21):
            u = -half + 2 * half * i / 20
            pts.append(mid + d * u + UP * (pointed(u, half, half * 1.6) - 1.4 - pointed(0, half, half * 1.6) + 1.2))
        o.append(tube(f"Arch{fi}", pts, 0.08, gold, segs=8))
        # cusps along the arch's inside
        for i in (5, 8, 12, 15):
            q = pts[i] - UP * 0.1
            o.append(blob(f"Cusp{fi}{i}", q, (0.05, 0.06, 0.05), gold, seed=fi * 10 + i, amp=0.1, subdiv=1))
        apex = mid + UP * 2.2
        o.append(beam(f"GableA{fi}", a + UP * 0.15, apex, 0.16, 0.2, gold))
        o.append(beam(f"GableB{fi}", b + UP * 0.15, apex, 0.16, 0.2, gold))
        o.append(lathe(f"Finial{fi}", [(0.0, 0.0), (0.08, 0.02), (0.1, 0.12), (0.06, 0.25), (0.01, 0.5), (0.0, 0.5)], gold, at=apex, segs=8))
        # crockets up the gable's slopes
        for e in (a, b):
            for t in (0.25, 0.5, 0.75):
                q = (e + UP * 0.15).lerp(apex, t) + UP * 0.13
                o.append(blob(f"Crk{fi}{t}{e.x}{e.z}", q, (0.06, 0.05, 0.06), gold, seed=int(t * 40) + fi, amp=0.2, subdiv=1))
    o.append(box("Top", (0, cy + 0.2, 0), (cx * 2, 0.1, cz * 2), gold, bevel=0.02, segs=1))
    o.append(octa("Spire", [(0.75, cy), (0.75, cy + 0.15), (0.02, cy + 6.5)], gold, cap_top=False))
    # crockets up the spire's eight arrises
    for k in range(8):
        a = math.tau * k / 8 + math.pi / 8
        for j in range(1, 7):
            t = j / 7.5
            r = 0.75 + (0.02 - 0.75) * t + 0.05
            q = V((math.cos(a) * r, cy + 0.15 + (6.35 * t), math.sin(a) * r))
            o.append(blob(f"SpC{k}{j}", q, (0.07 * (1 - t * 0.6),) * 3, gold, seed=k * 7 + j, amp=0.2, subdiv=1))
    o.append(blob("SpireFinial", (0, cy + 6.55, 0), (0.1, 0.14, 0.1), gold, seed=99, amp=0.15, subdiv=2))
    return o


def choir_stall():
    """A run of Church.BuildChancel's choir stalls (6 m) at (x, y, its middle) for the east side (the west's turned pi):
    a raised platform, ten seats parted by shaped elbows, a panelled back with blind arcading and a moulded capping, end
    panels with poppyhead finials."""
    oak = role("oak")
    L = 6.0
    o = [box("Platform", (0, 0.13, 0), (0.9, 0.26, L), oak, bevel=0.015, segs=1),
         box("SeatFront", (-0.2, 0.355, 0), (0.04, 0.19, L), oak, bevel=0.006, segs=1),
         box("Seat", (0.02, 0.45, 0), (0.52, 0.05, L), oak, bevel=0.01, segs=2),
         box("Back", (0.28, 0.9, 0), (0.06, 0.9, L), oak, bevel=0.008, segs=1),
         box("Capping", (0.27, 1.38, 0), (0.12, 0.06, L + 0.04), oak, bevel=0.015, segs=2),
         box("CapBead", (0.25, 1.34, 0), (0.1, 0.025, L + 0.02), oak, bevel=0.008, segs=1)]
    n = 10
    for i in range(n + 1):
        z = -L / 2 + L * i / n
        if 0 < i < n:
            # an elbow between two seats
            o.append(box(f"Div{i}", (0.06, 0.6, z), (0.4, 0.3, 0.04), oak, bevel=0.01, segs=1))
            o.append(lathe(f"Elbow{i}", [(0.0, 0.0), (0.04, 0.0), (0.05, 0.03), (0.035, 0.06), (0.0, 0.07)], oak, at=(-0.12, 0.75, z), segs=10))
    # the back's blind arcade: a colonnette and a pointed head per seat
    for i in range(n):
        z0, z1 = -L / 2 + L * i / n + 0.06, -L / 2 + L * (i + 1) / n - 0.06
        half = (z1 - z0) * 0.5
        zc = (z0 + z1) * 0.5
        pts = []
        for k in range(13):
            u = -half + 2 * half * k / 12
            pts.append((0.245, 1.0 + pointed(u, half, half * 1.5), zc + u))
        o.append(tube(f"Arc{i}", [(0.245, 0.55, z0)] + pts + [(0.245, 0.55, z1)], 0.012, oak, segs=6))
    for sz in (-1, 1):
        z = sz * (L / 2 + 0.03)
        o.append(box(f"End{sz}", (0.04, 0.73, z), (0.5, 0.94, 0.06), oak, bevel=0.012, segs=2))
        o.append(blob(f"Poppy{sz}", (-0.16, 1.27, z), (0.07, 0.1, 0.05), oak, seed=12 + sz, amp=0.25, subdiv=2))
        o.append(lathe(f"PoppyNeck{sz}", [(0.03, 1.2), (0.04, 1.17), (0.0, 1.16)], oak, at=(-0.16, 0, z), segs=8, cap_top=False))
    return o


# ------------------------------------------------------------------ the font's lid

def font_lid():
    """Church.BuildFont's lid at the FontLid node (Church.Story lifts the whole of it): an octagon of oak, its cone, four
    iron straps and a band round its edge, the ring at its top, the chain down its side, the padlock."""
    oak, iron = role("oak"), role("iron")
    o = [octa("Board", [(0.0, 0.0), (0.78, 0.0), (0.78, 0.05), (0.74, 0.09), (0.5, 0.09)], oak, cap_top=False),
         octa("Cone", [(0.5, 0.09), (0.3, 0.17), (0.06, 0.3), (0.0, 0.3)], oak, cap_bottom=False),
         octa("Band", [(0.785, 0.015), (0.795, 0.02), (0.795, 0.055), (0.785, 0.06)], iron, cap_bottom=False, cap_top=False)]
    for i in range(4):
        a = math.tau * i / 4
        # (lying on the cone and the board, bent where they bend: the code's ran straight and stood off the cone)
        run = [(0.1, 0.274), (0.277, 0.17), (0.462, 0.09), (0.69, 0.09), (0.735, 0.05)]
        pts = [V((math.cos(a) * r, y + 0.0075, math.sin(a) * r)) for (r, y) in run]
        for j in range(len(pts) - 1):
            o.append(beam(f"Strap{i}_{j}", pts[j], pts[j + 1], 0.07, 0.015, iron, bevel=0.003))
        for j in (1, 2, 3):   # its nails
            o.append(blob(f"Nail{i}{j}", pts[j] + UP * 0.008, (0.012, 0.007, 0.012), iron, seed=i, amp=0.05, subdiv=1))
    o.append(lathe("Staple", [(0.0, 0.29), (0.035, 0.29), (0.03, 0.31), (0.0, 0.32)], iron, segs=8))
    o.append(torus("Ring", (0, 0.38, 0), Matrix.Identity(3), 0.07, 0.012, iron, 20, 6))
    d = V((0.1, -0.07, 0)).normalized()
    back = V((0, 0, 1))
    for i in range(7):
        c = V((0.12, 0.3, 0)) + d * (i * 0.105)
        ax = back if i % 2 == 0 else d.cross(back).normalized()
        b = Matrix((ax.cross(d).normalized(), d, ax)).transposed() @ Matrix.Diagonal(V((1, 1.5, 1)))
        o.append(torus(f"Link{i}", c, b, 0.03, 0.008, iron, 12, 5))
    o.append(box("Lock", (0.74, -0.15, 0), (0.1, 0.11, 0.045), iron, bevel=0.012, segs=2))
    o.append(box("Keyplate", (0.74, -0.16, -0.024), (0.03, 0.04, 0.004), iron, bevel=0.002, segs=1))
    pts = [(0.74 + math.cos(a) * 0.032, -0.085 + math.sin(a) * 0.032, 0) for a in [math.pi * k / 10 for k in range(11)]]
    o.append(tube("Shackle", [(0.772, -0.1, 0)] + pts + [(0.708, -0.1, 0)], 0.008, iron, segs=6))
    return o


# ------------------------------------------------------------------ the library: bookcases, books, the journal

SHELF_D = 0.34
CASE_H = 3.8


def lib_upright():
    """One of Library.Shelves' uprights at its foot (X along the run, its face to -Z, the room): the side board and its
    pilaster front, a plinth block and a capital ('rubbed': the face, paler where hands have rubbed it)."""
    wood, rubbed = role("wood"), role("rubbed")
    h = CASE_H - 0.2
    return [box("Side", (0, h * 0.5, 0), (0.045, h, SHELF_D), wood, bevel=0.004, segs=1),
            box("Face", (0, h * 0.5, -SHELF_D * 0.5 - 0.006), (0.055, h - 0.24, 0.012), rubbed, bevel=0.004, segs=2),
            box("Reed", (0, h * 0.5, -SHELF_D * 0.5 - 0.013), (0.018, h - 0.5, 0.004), rubbed, bevel=0.0015, segs=1),
            box("FootBlock", (0, 0.1, -SHELF_D * 0.5 - 0.01), (0.07, 0.2, 0.02), wood, bevel=0.005, segs=2),
            box("CapBlock", (0, h - 0.07, -SHELF_D * 0.5 - 0.01), (0.07, 0.14, 0.02), wood, bevel=0.005, segs=2)]


def lib_cornice():
    """Library.Shelves' crown and plinth, a metre of them (the game scales X to the run's length): the crown's ogee
    over its lip ('lip', a shade darker) and the plinth along the foot ('plinth', darker still)."""
    wood, lip, plinth = role("wood"), role("lip"), role("plinth")
    zb, zf = SHELF_D * 0.5 + 0.025, -(SHELF_D * 0.5 + 0.025)
    y0 = CASE_H - 0.26
    crown = [(zb, y0), (zf + 0.02, y0), (zf + 0.02, y0 + 0.03), (zf, y0 + 0.04), (zf - 0.015, y0 + 0.07), (zf - 0.02, y0 + 0.1),
             (zf - 0.045, y0 + 0.13), (zf - 0.05, y0 + 0.15), (zf - 0.05, y0 + 0.18), (zb, y0 + 0.18)]
    yl = CASE_H - 0.31
    lipp = [(-SHELF_D * 0.5, yl), (-SHELF_D * 0.5 - 0.012, yl + 0.004), (-SHELF_D * 0.5 - 0.03, yl + 0.03), (-SHELF_D * 0.5 - 0.03, yl + 0.05), (-SHELF_D * 0.5, yl + 0.05)]
    zp = -SHELF_D * 0.5
    plin = [(zp + 0.02, 0.0), (zp - 0.026, 0.0), (zp - 0.026, 0.07), (zp - 0.016, 0.08), (zp - 0.012, 0.095), (zp + 0.02, 0.095)]
    return [extrude_profile("Crown", crown, wood), extrude_profile("Lip", lipp, lip), extrude_profile("Plinth", plin, plinth)]


def lib_shelf():
    """A metre of Library.Shelves' shelf board (scaled X to its bay), centred as the code's box: a rounded nosing."""
    wood = role("wood")
    t, d = 0.024, SHELF_D
    prof = [(d * 0.5, -t * 0.5), (-d * 0.5 + 0.006, -t * 0.5), (-d * 0.5 - 0.002, -t * 0.3), (-d * 0.5 - 0.004, 0.0),
            (-d * 0.5 - 0.002, t * 0.3), (-d * 0.5 + 0.006, t * 0.5), (d * 0.5, t * 0.5)]
    return [extrude_profile("Board", prof, wood)]


def book(kind):
    """Library.BookMeshes' binding `kind` in a unit box (X thickness, Y height, Z depth; its spine to -Z): two boards
    standing proud of the page block, the spine rounded over it, the boards' edges worn round. 0: calf with four raised
    bands; 1: plain cloth, a flatter spine; 2: cloth with a paper label at its head ('pages': its paper)."""
    cloth, pages = role("cloth"), role("pages")
    o = [box("Block", (0.0, 0.0, 0.035), (0.8, 0.94, 0.92), pages, bevel=0.0, segs=1)]
    for sx in (-0.45, 0.45):
        o.append(box(f"Board{sx}", (sx, 0, 0.0), (0.1, 1.0, 1.0), cloth, bevel=0.03, segs=1))
    sq = 0.14 if kind == 0 else 0.09
    # the spine: a half oval over the block's back, from board to board, headed and tailed
    bm = bmesh.new()
    n, rows = 8, [-0.5, -0.47, 0.47, 0.5]
    vv = []
    for yi, y in enumerate(rows):
        ring = []
        for i in range(n + 1):
            a = math.pi * i / n
            r = 0.5 if yi in (1, 2) else 0.47
            ring.append(bm.verts.new(P(V((math.cos(a) * r, y, -0.48 - math.sin(a) * sq * (1.0 if yi in (1, 2) else 0.8))))))
        vv.append(ring)
    for j in range(len(rows) - 1):
        for i in range(n):
            bm.faces.new((vv[j][i], vv[j][i + 1], vv[j + 1][i + 1], vv[j + 1][i]))
    bm.faces.new(vv[0])
    bm.faces.new(list(reversed(vv[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o.append(obj_from_bmesh(bm, "Spine", cloth, smooth=True))
    if kind == 0:
        for y in (-0.3, -0.1, 0.1, 0.3):
            pts = [(math.cos(math.pi * i / 4) * 0.5, y, -0.48 - math.sin(math.pi * i / 4) * (sq + 0.015)) for i in range(5)]
            o.append(tube(f"Band{y}", pts, 0.022, cloth, segs=3))
    if kind == 2:
        o.append(box("Label", (0, 0.3, -0.48 - sq - 0.004), (0.52, 0.16, 0.02), pages, bevel=0.0, segs=1))
    return o


def journal():
    """LibraryJournal's journal at its Book node (lying flat, its spine to -X; the top board's face at y 0.014, where the
    blacklight's hand is): worn leather boards over a rounded spine, the page block, a strap round it with a buckle."""
    leather, pages, strap, brass = role("leather"), role("pages"), role("strap"), role("brass")
    o = [box("Pages", (0.006, 0.0, 0.0), (0.138, 0.021, 0.2), pages, bevel=0.002, segs=1)]
    for sy in (-1, 1):
        o.append(box(f"Board{sy}", (0.0, sy * 0.0115, 0.0), (0.15, 0.005, 0.21), leather, bevel=0.0022, segs=2))
    o.append(lathe("Spine", [(0.014, -0.105), (0.0145, -0.1), (0.0145, 0.1), (0.014, 0.105)], leather, at=(-0.067, 0, 0), segs=16, sx=0.7,
                   basis=rot((1, 0, 0), math.pi / 2)))
    # the strap: round the book a third of the way along, its buckle on the fore-edge
    z = 0.03
    pts = [(-0.06, 0.0152, z), (0.074, 0.0152, z), (0.0795, 0.008, z), (0.0795, -0.008, z), (0.074, -0.0152, z), (-0.06, -0.0152, z),
           (-0.084, -0.006, z), (-0.084, 0.006, z), (-0.06, 0.0152, z)]
    o.append(tube("Strap", pts, (0.009, 0.0012), strap, segs=6, caps=False))
    o.append(box("Buckle", (0.082, 0.0, z), (0.004, 0.022, 0.026), brass, bevel=0.0015, segs=1))
    o.append(box("Tongue", (0.0835, 0.0, z), (0.002, 0.016, 0.003), brass, bevel=0.0, segs=1))
    return o


def ledgers():
    """The two old ledgers under the journal (LibraryJournal's root), a little askew, their spines to the room (+X)."""
    calf, cloth, pages = role("calf"), role("cloth"), role("pages")
    o = []
    for (sz, y, off, yaw, m, nm) in (((0.24, 0.035, 0.32), 0.0175, (0, 0), 0.06, calf, "A"), ((0.22, 0.03, 0.3), 0.05, (0.005, 0.01), -0.08, cloth, "B")):
        bs = rot((0, 1, 0), yaw)
        c = V((off[0], y, off[1]))
        w, h, d = sz
        o.append(box(f"Pages{nm}", c + bs @ V((-0.006, 0, 0)), (w - 0.014, h - 0.008, d - 0.012), pages, bevel=0.002, segs=1, basis=bs))
        for sy in (-1, 1):
            o.append(box(f"Board{nm}{sy}", c + bs @ V((0, sy * (h * 0.5 - 0.002), 0)), (w, 0.004, d), m, bevel=0.0015, segs=1, basis=bs))
        o.append(lathe(f"Spine{nm}", [(h * 0.5, -d * 0.5), (h * 0.5, d * 0.5)], m, at=c + bs @ V((w * 0.5 - h * 0.35, 0, 0)), segs=12, sx=0.6,
                       basis=bs @ rot((1, 0, 0), math.pi / 2)))
    return o


# ------------------------------------------------------------------ the burnt portraits' frames

# PortraitKit's moulding profiles: (out across the rim 0..1, up off the wall 0..1)
PROFILES = {
    "a": [(0, 0.25), (0.04, 0.3), (0.1, 0.36), (0.16, 0.3), (0.2, 0.32), (0.3, 0.5), (0.45, 0.82), (0.58, 0.96), (0.7, 1), (0.8, 0.94), (0.88, 0.8), (0.94, 0.62), (0.98, 0.5), (1, 0)],
    "b": [(0, 0.25), (0.05, 0.3), (0.14, 0.28), (0.2, 0.26), (0.3, 0.5), (0.6, 0.7), (0.8, 0.72), (0.92, 0.62), (1, 0.55), (1, 0)],
    "c": [(0, 0.3), (0.08, 0.4), (0.2, 0.42), (0.35, 0.75), (0.55, 0.9), (0.8, 0.85), (1, 0.6), (1, 0)],
    "d": [(0, 0.55), (0.1, 0.6), (0.7, 0.85), (0.9, 1), (1, 0.8), (1, 0)],
}


def _refine(prof, k=3):
    """The profile with its swells eased: each run split into k, the inner points nudged along a Catmull-Rom curve."""
    out = []
    for i in range(len(prof) - 1):
        p0 = V(prof[max(i - 1, 0)]); p1 = V(prof[i]); p2 = V(prof[i + 1]); p3 = V(prof[min(i + 2, len(prof) - 1)])
        sharp = abs(p2.x - p1.x) < 1e-6 or abs(p1.x - p0.x) < 1e-6 or abs(p3.x - p2.x) < 1e-6
        for j in range(k):
            t = j / k
            if sharp or j == 0:
                q = p1.lerp(p2, t)
            else:
                q = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
            out.append((q.x, q.y, i))
    out.append((prof[-1][0], prof[-1][1], len(prof) - 2))
    return out


def _bands(prof):
    """The refined stations with the band each face takes (the code's: the first three runs the slip, then body and a
    shade of it by turns), the inner wall down to the canvas first."""
    st = _refine(prof)
    st = [(0.0, 0.0, 0)] + st
    return st


def _band_role(i):
    return "slip" if i < 3 else ("body" if i % 2 == 0 else "shade")


def frame_rail(style):
    """A straight run of moulding a unit long (X -0.5..0.5), the canvas's edge at y 0, out to y 1, up off the wall to
    z 1 (+Z, the room): scaled (side, rim, depth) and turned to each side of the picture."""
    st = _bands(PROFILES[style])
    mats = {r: role(r) for r in ("slip", "body", "shade")}
    out = []
    for rname in ("slip", "body", "shade"):
        def q(bm, vert, rname=rname):
            for k in range(len(st) - 1):
                (t0, z0, _), (t1, z1, band) = st[k], st[k + 1]
                if _band_role(band) != rname:
                    continue
                face(bm, (vert((-0.5, t0, z0)), vert((0.5, t0, z0)), vert((0.5, t1, z1)), vert((-0.5, t1, z1))))
        out.append(sheet(f"Rail_{rname}", q, mats[rname]))
    return out


def frame_corner(style):
    """The mitred corner beyond the canvas's top right corner: the unit square (x, y in 0..1), the moulding's height
    there its profile at max(x, y): the rails meeting on the diagonal."""
    st = _bands(PROFILES[style])
    mats = {r: role(r) for r in ("slip", "body", "shade")}
    out = []
    for rname in ("slip", "body", "shade"):
        def q(bm, vert, rname=rname):
            for k in range(len(st) - 1):
                (t0, z0, _), (t1, z1, band) = st[k], st[k + 1]
                if _band_role(band) != rname:
                    continue
                A0, B0, C0 = vert((t0, 0, z0)), vert((t0, t0, z0)), vert((0, t0, z0))
                A1, B1, C1 = vert((t1, 0, z1)), vert((t1, t1, z1)), vert((0, t1, z1))
                face(bm, (A0, A1, B1, B0))
                face(bm, (B0, B1, C1, C0))
        out.append(sheet(f"Corner_{rname}", q, mats[rname]))
    return out


def frame_rosette():
    """A carved boss for the gilt frames' corners and cartouche: eight lobed petals round a domed button, a unit across
    and a unit high, lying on +Z (the game scales it)."""
    body = role("body")
    bm = bmesh.new()
    na, nr = 48, 10
    centre = bm.verts.new(P(V((0, 0, 1.0))))
    rings = []
    for j in range(1, nr + 1):
        s = j / nr
        ring = []
        for i in range(na):
            a = math.tau * i / na
            lobe = 0.82 + 0.18 * abs(math.cos(a * 4))
            r = s * lobe
            # the button in the middle, a groove, then the petals' swell falling to the edge
            if s < 0.3:
                z = 1.0 - (s / 0.3) ** 2 * 0.35
            elif s < 0.38:
                z = 0.55
            else:
                u = (s - 0.38) / 0.62
                z = 0.75 * math.sin(math.pi * (0.15 + 0.85 * u)) * (1 - u * 0.3) * (0.8 + 0.2 * math.cos(a * 8))
            if j == nr:
                z = 0.0
            ring.append(bm.verts.new(P(V((math.cos(a) * r, math.sin(a) * r, z)))))
        rings.append(ring)
    for i in range(na):
        bm.faces.new((centre, rings[0][(i + 1) % na], rings[0][i]))   # (reversed: P() swaps two axes)
    for j in range(nr - 1):
        for i in range(na):
            i1 = (i + 1) % na
            bm.faces.new((rings[j][i1], rings[j + 1][i1], rings[j + 1][i], rings[j][i]))
    return [obj_from_bmesh(bm, "Rosette", body, smooth=True)]


PIECES = {
    "ch_votive_rack": (votive_rack, (0.16, 0, 0.8), 3.0, 0.5, 60),
    "ch_crown": (crown, (0, 0, 0.4), 6.0, 1.2, 30),
    "ch_trans_altar": (trans_altar, (0, -0.2, 1.2), 5.0, 0.6, 20),
    "ch_high_altar": (high_altar, (0, 0, 1.0), 6.0, 0.6, 20),
    "ch_baldachin": (baldachin, (0, 0, 5.0), 16.0, 0.4, 25),
    "ch_choir_stall": (choir_stall, (0, 0, 0.7), 5.0, 0.4, 60),
    "ch_font_lid": (font_lid, (0, 0, 0.1), 2.6, 0.6, 20),
    "lib_upright": (lib_upright, (0, 0, 1.8), 4.0, 0.2, 30),
    "lib_cornice": (lib_cornice, (0, 0, 2.0), 4.0, 0.2, 30),
    "lib_shelf": (lib_shelf, (0, 0, 0.0), 1.4, 0.3, 30),
    "lib_book_0": (lambda: book(0), (0, 0, 0), 3.0, 0.3, 140),
    "lib_book_1": (lambda: book(1), (0, 0, 0), 3.0, 0.3, 140),
    "lib_book_2": (lambda: book(2), (0, 0, 0), 3.0, 0.3, 140),
    "lib_journal": (journal, (0, 0, 0), 0.5, 0.25, 30),
    "lib_ledgers": (ledgers, (0, 0, 0.03), 0.8, 0.3, 30),
    "pf_rail_a": (lambda: frame_rail("a"), (0, 0.5, 0.5), 2.0, 0.6, 30),
    "pf_rail_b": (lambda: frame_rail("b"), (0, 0.5, 0.5), 2.0, 0.6, 30),
    "pf_rail_c": (lambda: frame_rail("c"), (0, 0.5, 0.5), 2.0, 0.6, 30),
    "pf_rail_d": (lambda: frame_rail("d"), (0, 0.5, 0.5), 2.0, 0.6, 30),
    "pf_corner_a": (lambda: frame_corner("a"), (0.5, 0.5, 0.5), 2.0, 0.6, 30),
    "pf_corner_b": (lambda: frame_corner("b"), (0.5, 0.5, 0.5), 2.0, 0.6, 30),
    "pf_corner_c": (lambda: frame_corner("c"), (0.5, 0.5, 0.5), 2.0, 0.6, 30),
    "pf_corner_d": (lambda: frame_corner("d"), (0.5, 0.5, 0.5), 2.0, 0.6, 30),
    "pf_rosette": (frame_rosette, (0, 0, 0.3), 2.5, 0.8, 30),
}


if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        F.clear() if hasattr(F, "clear") else None
        fn, tgt, dist, h, yaw = PIECES[name]
        parts = fn()
        F.export(parts, name, tgt, dist, h, yaw)
