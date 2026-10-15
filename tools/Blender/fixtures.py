"""Fixtures and props of the Hollow, the station and the library (the owner, 2026-10-15: "from now on everything should be
made in blender ... anything that isn't made in blender should probably be redone in blender"). They were built in C#
(HollowProps, Cabin.Interior, Shed.Tools, Library); each is modelled here in Blender 4.0, headless, and exported through
the furniture pipeline (furniture.export: role materials the game swaps its own onto, a metre-a-repeat box UV, a baked
cavity map) to assets/models/furniture/fx_<name>.glb:

    blender -b --python tools/Blender/fixtures.py [-- name ...]

Each piece is drawn in the GAME's own coordinates (Y up, the same local frame its C# builder used, so the game places
it with the same transform), through P(), which turns a game point into Blender's (the export's mirror and Y-up turn it
back). The C# builders stay as fallbacks when a .glb is missing.
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furniture as F
from kit import obj_from_bmesh, add_mod, apply_mods

V = Vector


def P(x, y=None, z=None):
    """A game point (x, y up, z) as Blender's (after the export's mirror and Y-up: game = (bx, bz, by))."""
    if y is None:
        x, y, z = x
    return V((x, z, y))


def role(name):
    return F.role(name)


def _done(bm, name, m, smooth=True):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name, m, smooth=smooth)
    return ob


# ------------------------------------------------------------------ primitives, in game coordinates

def _frame(d):
    d = d.normalized()
    up = V((0, 1, 0)) if abs(d.y) < 0.95 else V((1, 0, 0))
    u = d.cross(up).normalized()
    w = d.cross(u).normalized()
    return u, w


def tube(name, pts, radii, m, segs=16, caps=True, rz=None):
    """A tube through game points, each ring perpendicular to the path (parallel-transported), radius per point (or
    (rx, rz) pairs: an oval)."""
    pts = [V(p) for p in pts]
    bm = bmesh.new()
    rings = []
    u, w = _frame(pts[1] - pts[0])
    prev_d = (pts[1] - pts[0]).normalized()
    for i, p in enumerate(pts):
        if i == 0:
            d = pts[1] - pts[0]
        elif i == len(pts) - 1:
            d = pts[-1] - pts[-2]
        else:
            d = (pts[i + 1] - pts[i - 1])
        d = d.normalized()
        # parallel transport of the frame
        axis = prev_d.cross(d)
        if axis.length > 1e-6:
            ang = prev_d.angle(d)
            R = Matrix.Rotation(ang, 3, axis.normalized())
            u, w = (R @ u).normalized(), (R @ w).normalized()
        prev_d = d
        r = radii[i] if isinstance(radii, list) else radii   # (a list: per point; a tuple: one oval for all)
        ra, rb = (r if not isinstance(r, tuple) else r[0]), (r if not isinstance(r, tuple) else r[1])
        ring = []
        for s in range(segs):
            a = s / segs * math.tau
            q = p + u * math.cos(a) * ra + w * math.sin(a) * rb
            ring.append(bm.verts.new(P(q)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for s in range(segs):
            bm.faces.new((rings[k][s], rings[k][(s + 1) % segs], rings[k + 1][(s + 1) % segs], rings[k + 1][s]))
    if caps:
        bm.faces.new(rings[0])
        bm.faces.new(list(reversed(rings[-1])))
    return _done(bm, name, m)


def cyl(name, a, b, r0, r1, m, segs=16, caps=True):
    return tube(name, [a, b], [r0, r1], m, segs, caps)


def lathe(name, profile, m, at=(0, 0, 0), segs=32, sx=1.0, sz=1.0, cap_bottom=True, cap_top=True, basis=None):
    """A turned piece round the game's Y at `at`: profile [(radius, y), ...]; sx/sz squash it to an oval."""
    bm = bmesh.new()
    at = V(at)
    rings = []
    for (r, y) in profile:
        ring = []
        for s in range(segs):
            a = s / segs * math.tau
            q = V((math.cos(a) * r * sx, y, math.sin(a) * r * sz))
            if basis is not None:
                q = basis @ q
            ring.append(bm.verts.new(P(at + q)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for s in range(segs):
            bm.faces.new((rings[k][s], rings[k][(s + 1) % segs], rings[k + 1][(s + 1) % segs], rings[k + 1][s]))
    if cap_bottom and profile[0][0] > 1e-5:
        bm.faces.new(rings[0])
    if cap_top and profile[-1][0] > 1e-5:
        bm.faces.new(list(reversed(rings[-1])))
    return _done(bm, name, m)


def box(name, c, size, m, bevel=0.004, segs=2, basis=None):
    """A box at game centre c, game size, optionally turned by a game-space basis; its edges bevelled."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        q = V((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if basis is not None:
            q = basis @ q
        v.co = P(V(c) + q)
    ob = _done(bm, name, m, smooth=False)
    if bevel > 0:
        add_mod(ob, "BEVEL", width=min(bevel, min(size) * 0.45), segments=segs, limit_method="ANGLE")
        apply_mods(ob)
    return ob


def grid(name, pts, m, thick=0.0, closed_u=False):
    """A surface through a grid of game points pts[i][j]; given a thickness (solidify) so it's a closed shell."""
    bm = bmesh.new()
    vv = [[bm.verts.new(P(V(p))) for p in row] for row in pts]
    ni, nj = len(vv), len(vv[0])
    for i in range(ni - 1):
        for j in range(nj - 1 if not closed_u else nj):
            j1 = (j + 1) % nj
            bm.faces.new((vv[i][j], vv[i][j1], vv[i + 1][j1], vv[i + 1][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name, m, smooth=True)
    if thick > 0:
        add_mod(ob, "SOLIDIFY", thickness=thick, offset=0.0)
        apply_mods(ob)
    return ob


def blob(name, c, radii, m, seed=1, amp=0.12, subdiv=3):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 6.28) for _ in range(6)]
    for v in bm.verts:
        n = v.co.normalized()
        d = 1 + amp * (math.sin(n.x * 3.1 + ph[0]) * math.sin(n.y * 2.7 + ph[1]) + 0.5 * math.sin(n.z * 4.3 + ph[2]))
        g = V((n.x * radii[0] * d, n.y * radii[1] * d, n.z * radii[2] * d))
        v.co = P(V(c) + g)
    return _done(bm, name, m)


def rot(axis, a):
    """A game-space rotation (axis in game coordinates)."""
    return Matrix.Rotation(a, 3, V(axis))


# ------------------------------------------------------------------ the station desk's banker's lamp (HollowProps.BankersLamp)

_L, _RY, _RZ = 0.13, 0.068, 0.074
_XS = [-_L + d for d in (0, 0.002, 0.006, 0.012, 0.02, 0.03, 0.045, 0.065)] + [-0.04, -0.015, 0.015, 0.04] + [_L - d for d in (0.065, 0.045, 0.03, 0.02, 0.012, 0.006, 0.002, 0)]
_SHADE = Matrix.Translation(V((0, 0.296, 0))) @ rot((1, 0, 0), -0.24).to_4x4()


def _shell(grow, arc=28):
    def taper(x):
        d = max(0.0, abs(x) - (_L - 0.05)) / 0.05
        return math.sqrt(max(0.0, 1 - d * d)) * 0.82 + 0.18
    rows = []
    for x in _XS:
        t = taper(x)
        row = []
        for j in range(arc + 1):
            a = math.pi * j / arc
            q = V((x * (1 + grow * 0.6), math.sin(a) * (_RY * t + grow * _RY), math.cos(a) * (_RZ * t + grow * _RZ)))
            row.append(_SHADE @ q)
        rows.append(row)
    return rows


def banker_lamp():
    brass, out, inside = role("brass"), role("shade_out"), role("shade_in")
    o = [
        lathe("Foot", [(0.0, 0.0), (0.088, 0.0), (0.09, 0.004), (0.088, 0.009), (0.078, 0.013), (0.074, 0.019), (0.058, 0.024), (0.036, 0.029), (0.022, 0.036), (0.014, 0.042), (0.0, 0.043)], brass, segs=40, sz=0.74),
        lathe("Stem", [(0.0085, 0.04), (0.0085, 0.14), (0.013, 0.147), (0.016, 0.158), (0.013, 0.169), (0.0085, 0.175), (0.0085, 0.245), (0.013, 0.25), (0.013, 0.258), (0.0, 0.259)], brass, segs=20),
        cyl("Yoke", (-0.112, 0.256, 0), (0.112, 0.256, 0), 0.0055, 0.0055, brass, 12),
        lathe("Socket", [(0.0, 0.0), (0.012, 0.0), (0.014, 0.006), (0.014, 0.024), (0.011, 0.03), (0.0, 0.031)], brass, at=(0, 0.256, 0), segs=20),
    ]
    for s in (-1, 1):
        o.append(tube(f"Arm{s}", [(s * 0.112, 0.252, 0), (s * 0.112, 0.28, 0), (s * 0.112, 0.296, 0)], 0.005, brass, 10))
        o.append(cyl(f"Nut{s}", (s * 0.124, 0.296, 0), (s * 0.108, 0.296, 0), 0.008, 0.008, brass, 12))
    o.append(grid("Shade", _shell(0.0), out, thick=0.004))
    o.append(grid("ShadeInside", _shell(-0.06), inside, thick=0.002))
    # the lip round the open side and the ends (the cased glass's thickness): a thin rod along each open edge
    outer = _shell(0.0)
    for j in (0, len(outer[0]) - 1):
        o.append(tube(f"Lip{j}", [r[j] for r in outer], 0.0035, inside, 8))
    fin = _SHADE @ V((0, _RY - 0.004, 0))
    o.append(lathe("Finial", [(0.011, 0.0), (0.009, 0.006), (0.004, 0.01), (0.005, 0.014), (0.0, 0.019)], brass, at=fin, segs=16, cap_bottom=True, cap_top=False))
    a, b, c = _SHADE @ V((0.03, -0.012, 0.005)), _SHADE @ V((0.033, -0.085, 0.03)), _SHADE @ V((0.0335, -0.1, 0.033))
    o.append(cyl("Chain", a, b, 0.0013, 0.0013, brass, 6))
    o.append(cyl("Bead", b, c, 0.0045, 0.003, brass, 10))
    return o


def banker_lamp_glow():
    lit = role("lit")
    o = [grid("Glow", _shell(-0.075), lit, thick=0.001)]
    bulb = _SHADE @ V((0, -0.012, 0))
    o.append(lathe("Bulb", [(0.0, -0.002), (0.008, -0.002), (0.018, 0.004), (0.026, 0.016), (0.027, 0.03), (0.02, 0.045), (0.01, 0.05), (0.0, 0.051)], lit, at=bulb, segs=20))
    return o


# ------------------------------------------------------------------ the enamel kettle (HollowProps.Kettle): spout toward -X

def kettle():
    enamel, wood = role("enamel"), role("wood")
    o = [lathe("Body", [(0.0, 0.0), (0.082, 0.0), (0.094, 0.008), (0.098, 0.03), (0.094, 0.058), (0.078, 0.084), (0.05, 0.1), (0.044, 0.104), (0.046, 0.11), (0.03, 0.118), (0.012, 0.122), (0.009, 0.128), (0.013, 0.134), (0.0, 0.138)], enamel, segs=36)]
    path = [(-0.07, 0.035, 0), (-0.1, 0.055, 0), (-0.122, 0.082, 0), (-0.138, 0.112, 0), (-0.148, 0.128, 0)]
    o.append(tube("Spout", path, [0.022 + (0.011 - 0.022) * i / 4 for i in range(5)], enamel, 16))
    for s in (-1, 1):
        o.append(cyl(f"Lug{s}", (0, 0.092, s * 0.07), (0, 0.104, s * 0.078), 0.006, 0.006, enamel, 10))
    wire = [(0.0, 0.104 + math.sin(math.pi * i / 16) * 0.09, math.cos(math.pi * i / 16) * 0.078) for i in range(17)]
    o.append(tube("Bail", wire, 0.0025, enamel, 8, caps=True))
    o.append(cyl("Grip", (0, 0.194, -0.035), (0, 0.194, 0.035), 0.009, 0.009, wood, 14))
    return o


# ------------------------------------------------------------------ a plain kitchen chair (HollowProps.Chair): back toward -Z

def kitchen_chair():
    wood = role("wood")
    seat = 0.45
    o = [box("Seat", (0, seat - 0.016, 0.01), (0.42, 0.032, 0.4), wood, 0.01, 3),
         box("Rails", (0, seat - 0.06, 0.01), (0.36, 0.05, 0.34), wood, 0.005, 2)]
    leg = [(0.0, 0.0), (0.016, 0.0), (0.018, 0.02), (0.015, 0.14), (0.02, 0.17), (0.016, 0.2), (0.019, 0.34), (0.02, 0.4), (0.0, 0.401)]
    for x in (-0.17, 0.17):
        o.append(lathe(f"FrontLeg{x}", leg, wood, at=(x, 0, 0.165), segs=14))
        o.append(tube(f"BackPost{x}", [(x, 0, -0.17), (x, seat, -0.175), (x, 0.9, -0.22)], [0.018, 0.019, 0.016], wood, 14))
        o.append(cyl(f"SideStretcher{x}", (x, 0.17, 0.165), (x, 0.17, -0.17), 0.01, 0.01, wood, 10))
    o.append(cyl("Stretcher", (-0.17, 0.2, 0), (0.17, 0.2, 0), 0.01, 0.01, wood, 10))
    top = [(a * 0.17, 0.85, -0.215 - 0.025 * (1 - a * a)) for a in [i / 8 * 2 - 1 for i in range(9)]]
    mid = [(a * 0.17, 0.66, -0.198 - 0.02 * (1 - a * a)) for a in [i / 8 * 2 - 1 for i in range(9)]]
    o.append(tube("TopRail", top, (0.035, 0.011), wood, 12))
    o.append(tube("MidRail", mid, (0.02, 0.009), wood, 12))
    return o


# ------------------------------------------------------------------ a tin can (HollowProps.Can), unit size r 0.05, h 0.12

def tin_can(lidded):
    tin, inside = role("tin"), role("inside")
    r, h = 0.05, 0.12
    prof = [(0.0, 0.0), (r - 0.003, 0.0), (r + 0.0015, 0.003), (r, 0.008), (r, h * 0.33 - 0.004), (r - 0.002, h * 0.33), (r, h * 0.33 + 0.004),
            (r, h * 0.67 - 0.004), (r - 0.002, h * 0.67), (r, h * 0.67 + 0.004), (r, h - 0.006), (r + 0.0015, h - 0.002), (r - 0.002, h)]
    if lidded:
        prof.append((0.0, h - 0.001))
        return [lathe("Can", prof, tin, segs=28)]
    o = [lathe("Can", prof, tin, segs=28, cap_top=False)]
    o.append(lathe("Inside", [(0.0, h * 0.3), (r - 0.002, h * 0.3), (r - 0.002, h - 0.001)], inside, segs=28, cap_bottom=False, cap_top=False))
    return o


# ------------------------------------------------------------------ the cabin's box stove (Cabin.BuildStove): door to +X, no pipe

def box_stove():
    iron, rust = role("iron"), role("rust")
    o = [box("Firebox", (0, 0.47, 0), (0.5, 0.5, 0.62), iron, 0.025, 3),
         box("AshLip", (0, 0.215, 0), (0.56, 0.03, 0.68), iron, 0.01, 2),
         box("Top", (0, 0.735, 0), (0.58, 0.03, 0.7), rust, 0.008, 2)]
    for dz in (-0.15, 0.15):
        o.append(lathe(f"Lid{dz}", [(0.0, 0.75), (0.095, 0.75), (0.093, 0.754), (0.09, 0.757), (0.0, 0.757)], rust, at=(0.02, 0, dz), segs=32))
        o.append(box(f"Notch{dz}", (0.02, 0.759, dz), (0.012, 0.004, 0.06), rust, 0.001))
    for s in (-1, 1):
        for r in range(3):
            o.append(box(f"Rib{s}{r}", (0, 0.34 + r * 0.13, s * 0.315), (0.44, 0.022, 0.016), iron, 0.006, 2))
    fx = 0.252
    o.append(box("Door", (fx + 0.004, 0.48, 0), (0.016, 0.28, 0.32), iron, 0.004))
    for dz in (-0.14, 0.14):
        o.append(box(f"DoorStile{dz}", (fx + 0.016, 0.48, dz), (0.012, 0.26, 0.024), iron, 0.003))
    for dy in (-0.12, 0.12):
        o.append(box(f"DoorRail{dy}", (fx + 0.016, 0.48 + dy, 0), (0.012, 0.024, 0.3), iron, 0.003))
    for dy in (-0.09, 0.09):
        o.append(cyl(f"Hinge{dy}", (fx + 0.012, 0.48 + dy - 0.03, -0.165), (fx + 0.012, 0.48 + dy + 0.03, -0.165), 0.012, 0.012, iron, 12))
    o.append(cyl("HandleStem", (fx + 0.02, 0.5, 0.12), (fx + 0.06, 0.5, 0.12), 0.006, 0.006, iron, 8))
    coil = [(fx + 0.06 + 0.012 * math.cos(t * 0.5), 0.5 + 0.012 * math.sin(t * 0.5), 0.09 + t * 0.06 / (math.tau * 5)) for t in [i * 0.25 for i in range(int(math.tau * 5 / 0.25) + 1)]]
    o.append(tube("HandleCoil", coil, 0.0025, rust, 6))
    o.append(cyl("Draft", (fx, 0.29, 0), (fx + 0.018, 0.29, 0), 0.05, 0.05, iron, 28))
    o.append(box("DraftBar", (fx + 0.019, 0.29, 0), (0.006, 0.01, 0.07), iron, 0.002))
    for lx, lz in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        knee, ankle, foot = (lx * 0.2, 0.205, lz * 0.255), (lx * 0.235, 0.06, lz * 0.3), (lx * 0.25, 0.012, lz * 0.315)
        o.append(tube(f"Leg{lx}{lz}", [knee, ankle, foot], [0.034, 0.022, 0.03], iron, 12))
        o.append(lathe(f"Pad{lx}{lz}", [(0.0, 0.0), (0.034, 0.0), (0.034, 0.016), (0.0, 0.016)], iron, at=(foot[0], 0, foot[2]), segs=16))
    return o


# ------------------------------------------------------------------ the cabin's cot (Cabin.BuildCot), cw 0.36, 1.9 m, centred

def cot():
    wood, canvas, ticking, blanket, pillow = role("wood"), role("canvas"), role("ticking"), role("blanket"), role("pillow")
    cw, z0, z1 = 0.36, -0.95, 0.95
    ln = z1 - z0
    o = []
    for s in (-1, 1):
        o.append(box(f"Rail{s}", (s * cw, 0.36, 0), (0.045, 0.06, ln + 0.08), wood, 0.01, 3))
    for z in (z0 + 0.04, z1 - 0.04):
        o.append(box(f"Stretcher{z}", (0, 0.31, z), (cw * 2 - 0.05, 0.035, 0.035), wood, 0.006))
        for s in (-1, 1):
            a, b = V((-s * (cw - 0.03), 0.01, z)), V((s * (cw - 0.03), 0.34, z))
            o.append(tube(f"Leg{z}{s}", [a, (a + b) * 0.5, b], (0.0175, 0.015), wood, 10))
        o.append(cyl(f"Pin{z}", (0, 0.175, z - 0.03), (0, 0.175, z + 0.03), 0.012, 0.012, wood, 12))
    rows = []
    for i in range(13):
        z = z0 + 0.02 + (ln - 0.04) * i / 12
        rows.append([(x, 0.385 - 0.035 * (1 - (x / cw) ** 2), z) for x in [cw * (k / 6 * 2 - 1) for k in range(7)]])
    o.append(grid("Canvas", rows, canvas, thick=0.004))
    o.append(box("Pad", (0, 0.42, 0), (cw * 2 - 0.02, 0.07, ln - 0.06), ticking, 0.03, 4))
    # the blanket, rumpled back: a draped sheet over the pad, its sides hanging, a turned fold at its head
    rnd = random.Random(19)
    bz0, bz1, bxw, by = z1 - 1.33, z1 - 0.06, cw + 0.04, 0.46
    cols = []
    for i in range(17):
        z = bz0 + (bz1 - bz0) * i / 16
        lump = rnd.uniform(0, 0.02)
        row = []
        for k in range(13):
            u = k / 12 * 2 - 1
            if abs(u) <= 0.7:
                y = by + 0.025 + lump * (1 - abs(u))
                x = u * bxw
            else:
                t = (abs(u) - 0.7) / 0.3
                x = math.copysign(bxw * (0.7 + 0.3 * math.sin(t * math.pi / 2)) + 0.012 * t, u)
                y = by + 0.025 - 0.17 * t * t - lump * t
            row.append((x, y, z))
        cols.append(row)
    o.append(grid("Blanket", cols, blanket, thick=0.008))
    o.append(tube("Fold", [(-bxw - 0.01, by + 0.045, bz0 - 0.03), (0, by + 0.055, bz0 - 0.028), (bxw + 0.01, by + 0.05, bz0 - 0.02)], [0.045, 0.05, 0.05], blanket, 16))
    o.append(blob("Pillow", (0, 0.515, z0 + 0.22), (0.25, 0.05, 0.14), pillow, 7, amp=0.08))
    return o


# ------------------------------------------------------------------ the shed's tools (Shed.Tools), each in its own frame

def hand_saw():
    steel, wood, brass = role("steel"), role("wood"), role("brass")
    half, top, hHeel, hToe = 0.25, 0.04, 0.115, 0.07
    pts = []
    n = 24
    for i in range(n + 1):
        x = -half + 2 * half * i / n
        e = top - (hToe + (hHeel - hToe) * (x + half) / (2 * half))
        tooth = -0.006 if i % 2 else 0.0
        pts.append([(x, e + tooth, 0.0), (x, top, 0.0)])
    o = [grid("Blade", pts, steel, thick=0.0012)]
    gx = half - 0.02

    def g(x, y):
        return V((gx + x, top - 0.01 + y, 0))
    d = [g(-0.02, 0.01), g(0.1, 0.01), g(0.12, -0.06), g(0.09, -0.12), g(-0.01, -0.12)]
    o.append(tube("Grip", d + [d[0]], (0.014, 0.012), wood, 12))
    o.append(box("Cheek", (gx - 0.015, top - 0.065, 0), (0.035, 0.13, 0.024), wood, 0.008, 3))
    for sy in (-0.03, -0.08):
        o.append(cyl(f"Screw{sy}", (gx - 0.015, top - 0.01 + sy, -0.014), (gx - 0.015, top - 0.01 + sy, 0.014), 0.006, 0.006, brass, 12))
    return o


def wrench():
    steel = role("steel")
    return [box("Handle", (0, -0.01, 0), (0.024, 0.19, 0.009), steel, 0.004, 3),
            box("Head", (0, 0.1, 0), (0.055, 0.04, 0.012), steel, 0.004, 3),
            box("FixedJaw", (-0.018, 0.135, 0), (0.018, 0.035, 0.012), steel, 0.003, 2),
            box("MovingJaw", (0.016, 0.13, 0), (0.016, 0.026, 0.011), steel, 0.003, 2),
            cyl("Worm", (-0.012, 0.087, 0.007), (0.012, 0.087, 0.007), 0.006, 0.006, steel, 14)]


def pliers():
    steel, grip = role("steel"), role("grip")
    o = []
    for s in (-1, 1):
        o.append(tube(f"Jaw{s}", [(0, 0, 0), (s * 0.004, 0.03, 0), (s * 0.006, 0.055, 0)], [(0.006, 0.004), (0.005, 0.004), (0.003, 0.003)], steel, 10))
        o.append(tube(f"Handle{s}", [(0, 0, 0), (s * 0.02, -0.05, 0), (s * 0.04, -0.2, 0)], [(0.0055, 0.004), (0.005, 0.004), (0.005, 0.004)], steel, 10))
        o.append(tube(f"Grip{s}", [(s * 0.0147, -0.042, 0), (s * 0.03, -0.12, 0), (s * 0.04, -0.2, 0)], [0.008, 0.0085, 0.009], grip, 12))
    o.append(cyl("Pivot", (0, 0, -0.006), (0, 0, 0.007), 0.009, 0.009, steel, 14))
    return o


def screwdriver():
    wood, steel = role("wood"), role("steel")
    return [lathe("Handle", [(0.0, 0.0), (0.013, 0.0), (0.0145, 0.03), (0.015, 0.08), (0.0148, 0.11), (0.011, 0.12), (0.0, 0.121)], wood, segs=12),
            cyl("Ferrule", (0, -0.005, 0), (0, 0.005, 0), 0.011, 0.011, steel, 16),
            cyl("Shank", (0, -0.15, 0), (0, 0, 0), 0.003, 0.003, steel, 10),
            box("Tip", (0, -0.155, 0), (0.007, 0.012, 0.0015), steel, 0.0005)]


def rope_coil():
    rope, steel = role("rope"), role("steel")
    o = []
    for loop in range(3):
        r, lz, sag = 0.12 - loop * 0.008, 0.015 + loop * 0.02, 0.01 * loop
        pts = [(math.cos(a) * r, -sag + math.sin(a) * r * 1.08, lz) for a in [i * math.tau / 28 for i in range(29)]]
        o.append(tube(f"Loop{loop}", pts, 0.016, rope, 10))
    o.append(cyl("Hook", (0, 0.12, -0.002), (0, 0.12, 0.08), 0.005, 0.005, steel, 10))
    return o


def block_plane():
    wood, steel = role("wood"), role("steel")
    return [box("Body", (0, 0.03, 0), (0.05, 0.05, 0.16), wood, 0.006, 3),
            box("Iron", (0, 0.062, 0.01), (0.04, 0.012, 0.07), steel, 0.003, 2, rot((1, 0, 0), -0.5)),
            cyl("Knob", (0, 0.06, -0.05), (0, 0.075, -0.05), 0.012, 0.012, steel, 14)]


def long_tools():
    """The shovel and the rake leaning in the corner (Shed.LongTools), in the frame of c0."""
    wood, steel = role("wood"), role("steel")
    shTop, shFoot = V((-0.28, 1.25, -0.12)), V((0.0, 0.28, 0.0))
    o = [cyl("ShovelHandle", shFoot, shTop, 0.018, 0.016, wood, 12),
         cyl("ShovelKnob", shTop, shTop + (shTop - shFoot).normalized() * 0.04, 0.016, 0.03, wood, 12)]
    rkFoot, rkTop = V((-0.1, 0.02, -0.18)), V((-0.35, 1.4, -0.3))
    o.append(cyl("RakeHandle", rkFoot + V((0, 0.02, 0)), rkTop, 0.016, 0.015, wood, 12))
    bb = rot((1, 0, 0), 0.1) @ rot((0, 1, 0), 0.4) @ rot((0, 0, 1), 0.2)
    bc = V((0.02, 0.15, 0.01))
    rows = []
    for j in range(6):
        y = -0.14 + 0.27 * j / 5
        row = []
        for i in range(9):
            x = -0.11 + 0.22 * i / 8
            w = 1 - 0.15 * (y + 0.14) / 0.27
            dd = 0.022 * (1 - (x / 0.11) ** 2)
            row.append(bc + bb @ V((x * w, y, dd)))
        rows.append(row)
    o.append(grid("Blade", rows, steel, thick=0.002))
    o.append(cyl("Socket", bc + bb @ V((0, 0.12, 0.008)), shFoot, 0.022, 0.019, steel, 12))
    d = (rkTop - rkFoot).normalized()
    head = rkTop + d * 0.015
    hb = rot((0, 1, 0), 0.5)
    o.append(cyl("RakeHead", head + hb @ V((-0.18, 0, 0)), head + hb @ V((0.18, 0, 0)), 0.012, 0.012, steel, 10))
    for t in range(10):
        tp = head + hb @ V((-0.17 + t * 0.038, 0, 0))
        o.append(tube(f"Tine{t}", [tp, tp + hb @ V((0, 0.04, 0.008)), tp + hb @ V((0, 0.07, 0.03))], [0.004, 0.0035, 0.003], steel, 6))
    return o


def gas_can():
    paint, steel = role("paint"), role("steel")
    return [box("Body", (0, 0.15, 0), (0.2, 0.3, 0.3), paint, 0.025, 4),
            box("Emboss", (0, 0.15, 0), (0.205, 0.22, 0.22), paint, 0.01, 2),
            tube("Handle", [(0, 0.29, -0.09), (0, 0.335, -0.07), (0, 0.338, -0.02), (0, 0.335, 0.03), (0, 0.29, 0.05)], (0.012, 0.016), paint, 12),
            cyl("Spout", (0, 0.29, 0.1), (0, 0.36, 0.14), 0.022, 0.016, steel, 16),
            cyl("Cap", (0, 0.355, 0.137), (0, 0.375, 0.149), 0.019, 0.019, steel, 16)]


# ------------------------------------------------------------------ the library's reading table (Library.ReadingTable), 1.6 x 0.77 x 0.9

def reading_table():
    wood = role("wood")
    X, Y, Z, leg = 1.6, 0.77, 0.9, 0.07
    topT = min(max(Y * 0.055, 0.03), 0.045)
    o = [box("Top", (0, Y - topT * 0.5, 0), (X, topT, Z), wood, topT * 0.3, 3),
         box("Moulding", (0, Y - topT - 0.009, 0), (X - 0.03, 0.018, Z - 0.03), wood, 0.006, 2)]
    inset, ah = 0.05 + leg * 0.5, 0.1
    ay = Y - topT - 0.018 - ah * 0.5
    for s in (-1, 1):
        o.append(box(f"ApronL{s}", (0, ay, s * (Z * 0.5 - inset)), (X - inset * 2, ah, 0.022), wood, 0.005))
        o.append(box(f"ApronS{s}", (s * (X * 0.5 - inset), ay, 0), (0.022, ah, Z - inset * 2), wood, 0.005))
    legH = Y - topT - 0.018
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * (X * 0.5 - inset), sz * (Z * 0.5 - inset)
            o.append(box(f"LegTop{sx}{sz}", (x, legH - (ah + 0.02) * 0.5, z), (leg, ah + 0.02, leg), wood, 0.004))
            h = legH - (ah + 0.02)
            r = leg * 0.5
            o.append(lathe(f"Leg{sx}{sz}", [(0.0, 0.0), (r * 0.55, 0.0), (r * 0.6, 0.02), (r * 0.5, 0.05), (r * 0.62, h * 0.18), (r * 0.85, h * 0.32), (r * 0.72, h * 0.45),
                                             (r * 0.55, h * 0.7), (r * 0.7, h * 0.85), (r * 0.6, h * 0.94), (r * 0.8, h), (0.0, h + 0.001)], wood, at=(x, 0, z), segs=20))
    return o


# ------------------------------------------------------------------ LampKit: the library's lamps (bodies only; bulbs and lights stay in code)

def _pairs(*v):
    return [(v[i], v[i + 1]) for i in range(0, len(v), 2)]


def _curve(a, c, b, n=8):
    a, c, b = V(a), V(c), V(b)
    return [a * (1 - t) ** 2 + c * 2 * (1 - t) * t + b * t * t for t in (i / n for i in range(n + 1))]


def lk_banker():
    """LampKit.Banker at the identity: the brass, the chain, the cased-glass trough (its bulb and light stay in code)."""
    brass, out, inside = role("brass"), role("shade_out"), role("shade_in")
    o = [
        lathe("Foot", _pairs(0, 0, 0.105, 0, 0.105, 0.007, 0.099, 0.012, 0.09, 0.0135, 0.088, 0.019, 0.078, 0.026, 0.06, 0.033, 0.04, 0.039,
                             0.026, 0.044, 0.02, 0.05, 0.016, 0.053, 0, 0.054), brass, segs=40, sz=0.68),
        lathe("Stem", _pairs(0.013, 0.053, 0.011, 0.062, 0.0075, 0.075, 0.0075, 0.118, 0.012, 0.124, 0.0135, 0.13, 0.012, 0.136, 0.0075, 0.142,
                             0.0075, 0.2, 0.012, 0.205, 0.013, 0.215, 0.0075, 0.222, 0.0075, 0.262, 0, 0.262), brass, segs=20),
        lathe("Socket", _pairs(0.013, 0, 0.015, 0.006, 0.015, 0.05, 0.012, 0.058, 0, 0.058), brass, at=(0.04, 0.278, 0), segs=20,
              basis=rot((0, 0, 1), math.pi * 0.5)),
    ]
    for s in (-1, 1):
        o.append(tube(f"Harp{s}", _curve((0, 0.21, 0), (s * 0.1, 0.2, 0), (s * 0.112, 0.268, 0), 10), 0.0042, brass, 10))
        o.append(lathe(f"Finial{s}", _pairs(0.006, 0.002, 0.0075, 0.007, 0.005, 0.012, 0, 0.013), brass, at=(s * 0.112, 0.262, 0), segs=12))
    c0, c1 = V((-0.022, 0.276, -0.012)), V((-0.024, 0.205, -0.05))
    o.append(tube("Chain", _curve(c0, (-0.023, 0.24, -0.05), c1, 8), 0.0018, brass, 6))
    o.append(lathe("Drop", _pairs(0.0055, 0.006, 0.006, 0.012, 0.003, 0.022, 0.002, 0.025, 0, 0.025), brass, at=c1 + V((0, -0.024, 0)), segs=12))
    # the shade: a half-elliptic trough of green cased glass, white inside, tipped toward the reader
    M = Matrix.Translation(V((0, 0.282, 0.008))) @ rot((1, 0, 0), -0.2).to_4x4()
    L, D, H, T = 0.135, 0.072, 0.088, 0.004
    xs = [-L, -L + 0.006, -L + 0.016, -L + 0.03, L - 0.03, L - 0.016, L - 0.006, L]
    sc = [0.74, 0.9, 0.97, 1, 1, 0.97, 0.9, 0.74]

    def sec(t, shrink):
        flare = abs(math.cos(t)) ** 6 * 0.006
        return math.cos(t) * (D + flare - shrink), math.sin(t) * (H - shrink) - flare * 0.4

    def rows(shrink, k=1.0):
        rr = []
        for x, f in zip(xs, sc):
            row = []
            for j in range(29):
                z, y = sec(math.pi * j / 28, shrink)
                row.append(M @ V((x * k, y * f, z * f)))
            rr.append(row)
        return rr
    o.append(grid("Shade", rows(0.0), out, thick=0.003))
    o.append(grid("ShadeInside", rows(T, (L - T) / L), inside, thick=0.0015))
    # the ends: a half-ellipse plate closing each
    for e, (x, f) in enumerate(((xs[0], sc[0]), (xs[-1], sc[-1]))):
        bm = bmesh.new()
        vs = []
        for j in range(29):
            z, y = sec(math.pi * j / 28, 0.0)
            vs.append(bm.verts.new(P(M @ V((x, y * f, z * f)))))
        bm.faces.new(vs)
        add = obj_from_bmesh(bm, f"End{e}", out, smooth=False)
        add_mod(add, "SOLIDIFY", thickness=0.003, offset=0.0)
        apply_mods(add)
        o.append(add)
    for j in (0, 28):
        o.append(tube(f"Rim{j}", [r[j] for r in rows(T * 0.5)], 0.0024, inside, 8))
    return o


def lk_floor():
    """LampKit.Floor at the identity, height 1.7 (the library's): the brass foot and pole, the spider, the drum shade
    (role "shade", tinted the lamp's colour in code), its lining, its binding rings."""
    brass, cloth, lining, bind = role("brass"), role("shade"), role("lining"), role("binding")
    top = 1.7 - 0.3
    o = [lathe("Foot", _pairs(0, 0, 0.15, 0, 0.15, 0.012, 0.14, 0.022, 0.11, 0.034, 0.07, 0.042, 0.045, 0.05, 0.04, 0.07, 0.05, 0.085,
                              0.035, 0.1, 0.018, 0.11, 0, 0.11), brass, segs=40)]
    prof = [(0.014, 0.105)]
    for f in (0.36, 0.68):
        y = 0.11 + (top - 0.11) * f
        prof += [(0.013, y - 0.025), (0.022, y - 0.012), (0.024, y), (0.022, y + 0.012), (0.013, y + 0.025)]
    prof += [(0.012, top - 0.06), (0.024, top - 0.04), (0.026, top - 0.02), (0.016, top), (0.01, top + 0.06), (0, top + 0.06)]
    o.append(lathe("Pole", prof, brass, segs=20))
    for i in range(3):
        a = math.tau * i / 3
        o.append(cyl(f"Spider{i}", (0, top + 0.05, 0), (math.cos(a) * 0.12, top + 0.19, math.sin(a) * 0.12), 0.0025, 0.0025, brass, 6))
    y0, y1 = top - 0.06, top + 0.2
    o.append(grid("Shade", [[(math.cos(a) * r, y, math.sin(a) * r) for a in (math.tau * j / 48 for j in range(48))]
                            for (r, y) in ((0.215, y0), (0.212, y0 + 0.05), (0.19, y0 + 0.15), (0.135, y1))], cloth, thick=0.003, closed_u=True))
    o.append(grid("Lining", [[(math.cos(a) * r, y, math.sin(a) * r) for a in (math.tau * j / 48 for j in range(48))]
                             for (r, y) in ((0.209, y0 + 0.002), (0.206, y0 + 0.05), (0.184, y0 + 0.15), (0.129, y1 - 0.002))], lining, thick=0.001, closed_u=True))
    for k, (r, y) in enumerate(((0.215, y0), (0.135, y1))):
        o.append(lathe(f"Bind{k}", [(r - 0.006, y - 0.004), (r + 0.004, y - 0.004), (r + 0.006, y), (r + 0.004, y + 0.005), (r - 0.006, y + 0.005)], bind, segs=48))
    return o


def lk_sconce():
    """LampKit.Sconce with its shade's middle at the origin, the wall toward +Z, reach 0.5 (the library's): the rosette,
    the scrolled arm, the cup, the parchment shade and its lining."""
    brass, cloth, lining = role("brass"), role("shade"), role("lining")
    n, reach = V((0, 0, 1)), 0.5
    plate = n * reach + V((0, -0.14, 0))
    # the rosette, an oval standing on the wall (its axis along -n)
    o = [lathe("Rosette", _pairs(0, 0, 0.055, 0, 0.055, 0.006, 0.045, 0.014, 0.025, 0.02, 0.016, 0.03, 0, 0.032), brass, at=plate + n * 0.002,
               segs=32, sx=0.75, basis=rot((1, 0, 0), -math.pi * 0.5))]
    cup = V((0, -0.11, 0))
    o.append(tube("Arm", _curve(plate - n * 0.03, plate - n * reach * 0.6 + V((0, -0.09, 0)), cup + V((0, -0.035, 0)), 14), 0.008, brass, 10))
    o.append(lathe("Cup", _pairs(0, 0, 0.012, 0, 0.01, 0.012, 0.02, 0.022, 0.045, 0.035, 0.05, 0.04, 0.044, 0.042, 0.016, 0.04, 0, 0.04), brass,
                   at=cup + V((0, -0.04, 0)), segs=28))
    y0, y1 = -0.07, 0.07
    o.append(grid("Shade", [[(math.cos(a) * r, y, math.sin(a) * r) for a in (math.tau * j / 40 for j in range(40))]
                            for (r, y) in ((0.11, y0), (0.1, y0 + 0.05), (0.075, y1 - 0.02), (0.06, y1))], cloth, thick=0.0025, closed_u=True))
    o.append(grid("Lining", [[(math.cos(a) * r, y, math.sin(a) * r) for a in (math.tau * j / 40 for j in range(40))]
                             for (r, y) in ((0.106, y0 + 0.002), (0.096, y0 + 0.05), (0.071, y1 - 0.02), (0.056, y1 - 0.002))], lining, thick=0.001, closed_u=True))
    return o


def lk_candlestick():
    """LampKit.Candlestick's brass at h 0.22 (the game scales it (sqrt f, f, sqrt f) for another height)."""
    metal = role("metal")
    return [lathe("Stick", _pairs(0, 0, 0.05, 0, 0.05, 0.006, 0.044, 0.014, 0.032, 0.026, 0.016, 0.036, 0.011, 0.046, 0.015, 0.054,
                                  0.011, 0.062, 0.008, 0.09, 0.012, 0.13, 0.017, 0.15, 0.012, 0.172, 0.008, 0.186, 0.011, 0.194, 0.032, 0.2,
                                  0.034, 0.204, 0.016, 0.206, 0.015, 0.22, 0.012, 0.22, 0.012, 0.212, 0, 0.212), metal, segs=32)]


# ------------------------------------------------------------------ the station's cigar box and pump wheel, the janitor's bucket and mop

def cigar_box():
    """StationRoom1.BuildBox's box, in the CigarBox node's frame (its foot at the origin; the lid and tapes apart)."""
    wood, band = role("wood"), role("band")
    hw, hd, hh = 0.22, 0.14, 0.075
    o = [box("Body", (0, hh, 0), (hw * 2, hh * 2, hd * 2), wood, bevel=0.004)]
    # the walls' joints: a thin bead round the foot
    o.append(box("Plinth", (0, 0.006, 0), (hw * 2 + 0.008, 0.012, hd * 2 + 0.008), wood, bevel=0.003))
    o.append(box("Band", (0, hh * 1.2, hd + 0.0015), (hw * 1.2, hh * 0.5, 0.003), band, bevel=0.0008))
    return o


def cigar_lid():
    """The cigar box's lid, in the Lid node's frame (its hinge at the origin, the lid running out along +Z)."""
    wood = role("wood")
    hw, hd = 0.22, 0.14
    return [box("Lid", (0, 0, hd), (hw * 2 + 0.01, 0.015, hd * 2 + 0.01), wood, bevel=0.003),
            box("Lip", (0, -0.006, hd * 2 + 0.002), (0.05, 0.006, 0.006), wood, bevel=0.002)]


def pump_housing():
    """StationBasement.BuildWheel's pump housing and the wheel's hub boss, in the Wheel node's frame (the hub at the origin)."""
    plate, steel = role("plate"), role("steel")
    o = [box("Housing", (0, -0.2, -0.2), (1.0, 1.3, 0.3), plate, bevel=0.012)]
    o.append(box("Flange", (0, 0.45, -0.2), (1.04, 0.04, 0.34), plate, bevel=0.008))
    # bolts round the front
    for x in (-0.44, -0.15, 0.15, 0.44):
        for y in (-0.8, 0.38):
            o.append(lathe(f"Bolt{x}{y}", [(0.014, 0), (0.014, 0.006), (0.01, 0.01), (0, 0.011)], steel, at=(x, y, -0.05), segs=6,
                           basis=rot((1, 0, 0), math.pi * 0.5)))
    o.append(lathe("Hub", [(0.13, 0), (0.13, 0.04), (0.1, 0.06), (0.1, 0.16), (0.07, 0.2), (0, 0.2)], steel, at=(0, 0, -0.05), segs=24,
                   basis=rot((1, 0, 0), math.pi * 0.5)))
    return o


def valve_wheel():
    """The handwheel, in the Spokes node's frame (it turns about Z): six spokes, a rim, a boss."""
    paint = role("paint")
    R = 0.55
    o = [lathe("Boss", [(0.06, 0.08), (0.07, 0.1), (0.07, 0.15), (0.05, 0.17), (0, 0.17)], paint, segs=20, basis=rot((1, 0, 0), math.pi * 0.5))]
    for i in range(6):
        a = math.tau * i / 6
        o.append(tube(f"Spoke{i}", [(math.cos(a) * 0.06, math.sin(a) * 0.06, 0.12), (math.cos(a) * R * 0.5, math.sin(a) * R * 0.5, 0.12), (math.cos(a) * (R - 0.02), math.sin(a) * (R - 0.02), 0.12)],
                      [0.032, 0.026, 0.024], paint, 10))
    ring = [(math.cos(math.tau * j / 64) * R, math.sin(math.tau * j / 64) * R, 0.12) for j in range(65)]
    o.append(tube("Rim", ring, (0.05, 0.035), paint, 12, caps=False))
    return o


def mop_bucket():
    """JanitorShelf.Bucket at the identity: tapered plastic, a rolled lip, grey water, the bail laid back."""
    plastic, murk, wire, grip = role("plastic"), role("murk"), role("wire"), role("grip")
    prof = [(0.0, 0.004), (0.118, 0.002), (0.127, 0.008), (0.131, 0.03), (0.137, 0.12), (0.143, 0.22), (0.148, 0.285), (0.157, 0.292),
            (0.159, 0.302), (0.153, 0.306), (0.146, 0.298), (0.139, 0.22), (0.133, 0.12), (0.126, 0.03), (0.0, 0.03)]
    o = [lathe("Bucket", prof, plastic, segs=48)]
    o.append(lathe("Water", [(0.134, 0.1), (0.135, 0.104), (0, 0.104)], murk, segs=48))
    r, earY, lean = 0.15, 0.27, 1.25
    R = rot((1, 0, 0), lean)
    for sx in (-1, 1):
        o.append(box(f"Ear{sx}", (sx * (r + 0.004), earY, 0), (0.006, 0.02, 0.014), plastic, bevel=0.002))
    pts = [V((0, earY, 0)) + R @ V((r * math.cos(math.pi * i / 24), r * 1.05 * math.sin(math.pi * i / 24), 0)) for i in range(25)]
    o.append(tube("Bail", pts, 0.0024, wire, 6))
    top = V((0, earY, 0)) + R @ V((0, r * 1.05, 0))
    o.append(cyl("Grip", top + R @ V((-0.026, 0, 0)), top + R @ V((0.026, 0, 0)), 0.0065, 0.0065, grip, 10))
    return o


def mop_head():
    """JanitorShelf.MopHead with its head at the origin, lying toward -Z: the socket and a sodden splay of strands."""
    tin, cotton = role("tin"), role("cotton")
    rnd = random.Random(7)
    d0, side = V((0, 0, -1)), V((1, 0, 0))
    o = [lathe("Socket", [(0.02, 0), (0.026, 0.05), (0.022, 0.058), (0, 0.06)], tin, at=(0, 0.028, 0.05), segs=14, basis=rot((1, 0, 0), -math.pi * 0.5))]
    for i in range(70):
        spread = rnd.uniform(-0.4, 0.4) * (1.8 if i % 5 == 0 else 1)
        ln = rnd.uniform(0.14, 0.3)
        d = (d0 * math.cos(spread) + side * math.sin(spread)).normalized()
        if i % 9 == 0:
            d = (-d0 + side * rnd.uniform(-0.6, 0.6)).normalized()
        r = rnd.uniform(0.012, 0.018)
        pts = [V((rnd.uniform(-0.02, 0.02), rnd.uniform(0.025, 0.045), 0))]
        for s in range(1, 6):
            t = s / 5
            q = d * ln * t + side * rnd.uniform(-0.035, 0.035) * t + d0 * rnd.uniform(-0.02, 0.02)
            q.y = r + (1 - math.sqrt(t)) * 0.022 + rnd.uniform(0, 0.02) * (1 - t)
            pts.append(q)
        o.append(tube(f"Strand{i}", pts, [r * (1 - 0.15 * k / 5) for k in range(6)], cotton, 6))
    return o


# ------------------------------------------------------------------ the library: the fireplace, the card catalogue, the book cart

def lib_fireplace():
    """Library.BuildFireplace's stone, in a frame at (HalfW, 0, 6.5) (the room's east wall at x 0, the hearth out toward
    -X): jambs on plinths, the lintel, the moulded mantel shelf, the chimney breast to the ceiling (3.8), the hearth, the
    soot-black back."""
    stone, soot = role("stone"), role("soot")
    o = []
    for s in (-1, 1):
        o.append(box(f"Jamb{s}", (-0.3, 0.66, s * 0.9), (0.58, 1.0, 0.38), stone, bevel=0.025, segs=3))
        o.append(box(f"Plinth{s}", (-0.31, 0.09, s * 0.9), (0.66, 0.18, 0.46), stone, bevel=0.02, segs=3))
        o.append(box(f"Corbel{s}", (-0.34, 1.13, s * 0.86), (0.6, 0.14, 0.32), stone, bevel=0.03, segs=3))
    # the lintel: three voussoir-ish blocks, the keystone proud
    o.append(box("LintelL", (-0.3, 1.3, -0.62), (0.68, 0.2, 0.74), stone, bevel=0.02, segs=3))
    o.append(box("LintelR", (-0.3, 1.3, 0.62), (0.68, 0.2, 0.74), stone, bevel=0.02, segs=3))
    o.append(box("Key", (-0.32, 1.29, 0), (0.72, 0.24, 0.46), stone, bevel=0.02, segs=3))
    # the mantel shelf, a cyma under it
    o.append(box("Shelf", (-0.36, 1.43, 0), (0.82, 0.06, 2.36), stone, bevel=0.012, segs=3))
    o.append(grid("Cyma", [[(-0.36 - 0.36 * k, 1.4 - 0.022 * (1 - math.cos(math.pi * k)) - 0.03 * k, z) for k in (0, 0.25, 0.5, 0.75, 1.0)] for z in (-1.14, 1.14)],
                  stone, thick=0.006))
    o.append(box("Bed", (-0.33, 1.385, 0), (0.74, 0.03, 2.28), stone, bevel=0.008))
    o.append(box("Breast", (-0.2, 2.6, 0), (0.4, 2.4, 1.9), stone, bevel=0.02, segs=3))
    o.append(box("Hearth", (-0.5, 0.04, 0), (1.0, 0.08, 2.2), stone, bevel=0.012, segs=2))
    o.append(box("Back", (-0.05, 0.6, 0), (0.1, 1.2, 1.4), soot, bevel=0.01))
    o.append(box("Firebed", (-0.3, 0.083, 0), (0.48, 0.006, 1.38), soot, bevel=0.002))
    return o


def lib_firedogs():
    """The fire dogs and logs in the fireplace's frame: iron andirons with ball finials, two split logs and a charred one."""
    iron, log, char = role("iron"), role("log"), role("char")
    o = []
    for dz in (-0.32, 0.32):
        o.append(box(f"Bar{dz}", (-0.385, 0.1, dz), (0.53, 0.03, 0.03), iron, bevel=0.006))
        o.append(tube(f"Post{dz}", [(-0.65, 0.08, dz), (-0.65, 0.17, dz), (-0.65, 0.255, dz)], [0.02, 0.016, 0.014], iron, 10))
        o.append(lathe(f"Ball{dz}", [(0.0, 0.25), (0.022, 0.256), (0.03, 0.272), (0.026, 0.292), (0.012, 0.304), (0.0, 0.307)], iron, at=(-0.65, 0, dz), segs=16))
        for lx in (-0.6, -0.18):
            o.append(tube(f"Foot{dz}{lx}", [(lx - 0.04, 0.005, dz), (lx, 0.09, dz), (lx + 0.04, 0.005, dz)], 0.012, iron, 8))
    def logp(name, a, b, r0, r1, m, seed):
        rnd = random.Random(seed)
        a, b = V(a), V(b)
        pts = [a.lerp(b, t) for t in (0, 0.2, 0.4, 0.6, 0.8, 1.0)]
        rr = [r0 + (r1 - r0) * t * rnd.uniform(0.9, 1.1) for t in (0, 0.2, 0.4, 0.6, 0.8, 1.0)]
        return tube(name, pts, rr, m, 12)
    o.append(logp("Log0", (-0.42, 0.17, -0.42), (-0.38, 0.17, 0.4), 0.062, 0.055, log, 1))
    o.append(logp("Log1", (-0.24, 0.17, -0.38), (-0.27, 0.18, 0.42), 0.055, 0.06, log, 2))
    o.append(logp("Log2", (-0.34, 0.27, -0.3), (-0.3, 0.25, 0.28), 0.05, 0.045, char, 3))
    return o


_CC_PULLED, _CC_GONE, _CC_AJAR = (3, 2), (1, 0), (0, 4)


def card_catalogue():
    """Library.CardCatalogue at the identity: a 30-drawer cabinet on a stand, a drawer pulled out with its cards, one
    ajar, one gone (a dark slot) and lying upside down on the floor in front."""
    wood, dark, brass, cards = role("wood"), role("dark"), role("brass"), role("cards")
    o = []
    for sx in (-0.46, 0.46):
        for sz in (-0.19, 0.19):
            o.append(lathe(f"Leg{sx}{sz}", [(0.022, 0), (0.026, 0.02), (0.02, 0.06), (0.024, 0.2), (0.028, 0.3), (0.028, 0.34), (0, 0.341)], wood, at=(sx, 0, sz), segs=12))
    o.append(box("Stand", (0, 0.37, 0), (1.0, 0.06, 0.46), wood, bevel=0.01))
    o.append(box("Carcass", (0, 0.81, -0.01), (1.0, 0.84, 0.46), wood, bevel=0.012, segs=2))
    o.append(box("Top", (0, 1.245, 0), (1.08, 0.04, 0.53), wood, bevel=0.012, segs=2))
    o.append(box("TopCap", (0, 1.275, 0), (1.02, 0.025, 0.49), wood, bevel=0.008, segs=2))
    dw, dh = 0.18, 0.122
    for c in range(5):
        for r in range(6):
            x, y = -0.4 + c * 0.2, 0.47 + r * 0.135
            if (c, r) == _CC_GONE:
                o.append(box(f"Slot{c}{r}", (x, y, 0.2), (dw, dh, 0.02), dark, bevel=0.0))
                continue
            pull = 0.24 if (c, r) == _CC_PULLED else 0.06 if (c, r) == _CC_AJAR else 0.0
            z = 0.225 + pull
            o.append(box(f"Front{c}{r}", (x, y, z), (dw, dh, 0.02), wood, bevel=0.005))
            if pull > 0.04:
                for s in (-1, 1):
                    o.append(box(f"Side{c}{r}{s}", (x + s * dw * 0.45, y - 0.005, z - pull * 0.5 - 0.01), (0.01, dh * 0.75, pull), wood, bevel=0.002))
                for i in range(int(pull / 0.006)):
                    o.append(box(f"Card{c}{r}{i}", (x, y - 0.012 + (i % 3) * 0.002, z - 0.016 - i * 0.006), (dw - 0.03, dh * 0.62, 0.0015), cards, bevel=0.0))
            o.append(box(f"Holder{c}{r}", (x, y + 0.028, z + 0.012), (0.06, 0.028, 0.004), brass, bevel=0.001))
            o.append(box(f"Label{c}{r}", (x, y + 0.028, z + 0.0145), (0.05, 0.02, 0.002), cards, bevel=0.0))
            o.append(lathe(f"Pull{c}{r}", [(0.009, 0), (0.012, 0.012), (0.012, 0.016), (0.008, 0.02), (0, 0.021)], brass, at=(x, y - 0.025, z + 0.01), segs=10,
                           basis=rot((1, 0, 0), math.pi * 0.5)))
    # the drawer that came out, upside down on the floor in front
    B = rot((0, 1, 0), 0.7) @ rot((0, 0, 1), math.pi)
    o.append(box("Dropped", (0.12, 0.06, 0.78), (0.18, 0.11, 0.42), wood, bevel=0.005, basis=B))
    return o


def book_cart():
    """Library.BookCart's frame at the identity: end panels, two shelves, the divider, casters, the push bar."""
    wood, metal = role("wood"), role("metal")
    o = []
    for sx in (-0.4, 0.4):
        o.append(box(f"End{sx}", (sx, 0.46, 0), (0.03, 0.72, 0.42), wood, bevel=0.008, segs=2))
    o.append(box("Shelf0", (0, 0.2, 0), (0.78, 0.024, 0.42), wood, bevel=0.006))
    o.append(box("Shelf1", (0, 0.62, 0), (0.78, 0.024, 0.42), wood, bevel=0.006))
    o.append(box("Divider", (0, 0.72, 0), (0.78, 0.18, 0.02), wood, bevel=0.005))
    for sx in (-0.36, 0.36):
        for sz in (-0.17, 0.17):
            o.append(cyl(f"Stem{sx}{sz}", (sx, 0.11, sz), (sx, 0.07, sz), 0.012, 0.012, metal, 8))
            o.append(box(f"Fork{sx}{sz}", (sx, 0.065, sz), (0.04, 0.012, 0.03), metal, bevel=0.003))
            o.append(cyl(f"Caster{sx}{sz}", (sx - 0.012, 0.04, sz), (sx + 0.012, 0.04, sz), 0.038, 0.038, metal, 16))
    o.append(cyl("Bar", (0.43, 0.82, -0.17), (0.43, 0.82, 0.17), 0.014, 0.014, metal, 12))
    for sz in (-0.17, 0.17):
        o.append(cyl(f"Bracket{sz}", (0.41, 0.82, sz), (0.435, 0.82, sz), 0.01, 0.01, metal, 8))
    return o


# ------------------------------------------------------------------ the station lobby's photo frame and plant, the church's font and wardrobe

def photo_frame():
    """LobbyDecor.Frame, centred, its face toward +Z (the code turns +Z out of the wall): a moulded frame, a card mat,
    the print under glass (the figures stay in code, printed on it)."""
    frame, mat, prnt = role("frame"), role("mat"), role("print")
    W, H, T = 0.46, 0.36, 0.03
    o = []
    # the moulding: four mitred rails, each a profile swept along its side
    prof = [(0.0, 0.0), (0.0, 0.022), (0.008, 0.03), (0.02, 0.026), (0.03, 0.03), (0.04, 0.022), (0.04, 0.0)]   # (in from the edge, out from the wall)
    corners = [(-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2), (-W / 2, H / 2)]
    for e in range(4):
        (ax, ay), (bx, by) = corners[e], corners[(e + 1) % 4]

        def inward(x, y, d):   # (both ends stepped in on the diagonal: the rails meet in mitres)
            return x + (-d if x > 0 else d), y + (-d if y > 0 else d)
        rows = []
        for (x, y) in ((ax, ay), (bx, by)):
            row = []
            for (d, zz) in prof:
                ix, iy = inward(x, y, d)
                row.append((ix, iy, zz - T / 2))
            rows.append(row)
        o.append(grid(f"Rail{e}", rows, frame, thick=0.0))
    o.append(box("Back", (0, 0, -T / 2 + 0.004), (W, H, 0.008), frame, bevel=0.002))
    o.append(box("Mat", (0, 0, 0.012), (W - 0.06, H - 0.06, 0.004), mat, bevel=0.0))
    o.append(box("Print", (0, 0, 0.0145), (0.36, 0.26, 0.002), prnt, bevel=0.0))
    return o


def plant_pot():
    """LobbyDecor.PlantPot's terracotta pot at the origin: a tapered body, a rolled rim, soil."""
    pot, soil = role("pot"), role("soil")
    return [lathe("Pot", [(0.0, 0.0), (0.155, 0.0), (0.16, 0.01), (0.2, 0.37), (0.215, 0.372), (0.218, 0.4), (0.215, 0.43), (0.2, 0.432),
                          (0.192, 0.4), (0.18, 0.36), (0.0, 0.36)], pot, segs=40),
            lathe("Soil", [(0.185, 0.36), (0.18, 0.375), (0.1, 0.382), (0.0, 0.385)], soil, segs=40)]


def _leaf(name, base, tip, w, droop, m):
    """A strap leaf from base to tip: a midrib, the blade cupped and tapering, drooping toward its end."""
    base, tip = V(base), V(tip)
    d = tip - base
    side = d.cross(V((0, 1, 0)))
    side = side.normalized() if side.length > 1e-6 else V((1, 0, 0))
    up = side.cross(d).normalized()
    rows = []
    for i in range(9):
        t = i / 8
        c = base + d * t + V((0, -droop * t * t, 0))
        half = w * math.sin(math.pi * min(1.0, t * 1.15)) * (1 - 0.3 * t) + 0.001
        rows.append([c + side * half * s + up * (abs(s) * half * 0.35) for s in (-1, -0.5, 0, 0.5, 1)])
    return grid(name, rows, m, thick=0.002)


def plant(dead):
    """LobbyDecor.PlantPot's plant: nine stalks out of the soil, a strap leaf on each, standing up green or fallen
    over brown."""
    stalk, leaf = role("stalk"), role("leaf")
    rnd = random.Random(77)
    o = []
    for i in range(9):
        a = math.tau * i / 9
        lift = rnd.uniform(-0.25, 0.05) if dead else rnd.uniform(0.35, 0.7)
        mid = V((math.cos(a) * 0.2, 0.4 + max(lift, 0.02) * 0.6 + (0.06 if not dead else 0.0), math.sin(a) * 0.2))
        tip = V((math.cos(a) * 0.35, 0.4 + lift, math.sin(a) * 0.35))
        root = V((math.cos(a) * 0.02, 0.38, math.sin(a) * 0.02))
        pts = _curve(root, mid, tip, 6)
        o.append(tube(f"Stalk{i}", pts[:4], [0.012, 0.011, 0.01, 0.009], stalk, 6))
        out = V((math.cos(a + 0.15), 0, math.sin(a + 0.15)))
        end = tip + out * (0.2 if not dead else 0.14) + V((0, -0.02 if not dead else -0.08, 0))
        o.append(_leaf(f"Leaf{i}", pts[3], end, 0.07 if not dead else 0.05, 0.08 if not dead else 0.03, leaf))
    return o


def font():
    """Church.BuildFont's stone at FontLocal: an octagonal step, a plinth, a shaft ringed by eight colonnettes, the
    flared bowl, its rim and the dark inside down to the water (the lid, its chain and lock stay in code)."""
    stone, inner = role("stone"), role("inner")
    def octa(r):
        return r / math.cos(math.pi / 8)   # (the code's radii are to the corners; a lathe's are too: kept)
    def lath(name, prof, m, **kw):
        return lathe(name, prof, m, segs=8, basis=rot((0, 1, 0), -math.pi / 8), **kw)
    o = [lath("Step", [(0.95, 0.0), (0.95, 0.15), (0.93, 0.18), (0.0, 0.18)], stone),
         lath("Plinth", [(0.6, 0.18), (0.6, 0.21), (0.5, 0.27), (0.45, 0.3), (0.0, 0.3)], stone),
         lath("Shaft", [(0.32, 0.3), (0.32, 0.75)], stone, cap_bottom=False, cap_top=False),
         lath("Bowl", [(0.38, 0.75), (0.42, 0.78), (0.6, 0.86), (0.72, 0.95), (0.72, 1.18), (0.745, 1.19), (0.745, 1.22), (0.6, 1.22)], stone, cap_bottom=True, cap_top=False),
         lath("Inside", [(0.6, 1.22), (0.58, 1.12), (0.52, 0.98), (0.0, 0.98)], inner, cap_bottom=False)]
    for i in range(8):
        a = math.tau * i / 8 + math.pi / 8
        x, z = math.cos(a) * 0.36, math.sin(a) * 0.36
        o.append(lathe(f"Col{i}", [(0.065, 0.3), (0.065, 0.32), (0.05, 0.34), (0.05, 0.74), (0.065, 0.76), (0.065, 0.78), (0.0, 0.78)], stone, at=(x, 0, z), segs=10))
    return o


def wardrobe():
    """Church.Vestry's Wardrobe at the identity (w 2.2, h 2.45, d 0.66, its doors to -Z): a plinth, the carcass, a
    stepped cornice, two panelled doors, iron pulls."""
    oak, door, iron = role("oak"), role("door"), role("iron")
    w, h, d = 2.2, 2.45, 0.66
    o = [box("Plinth", (0, 0.06, 0.01), (w - 0.04, 0.12, d - 0.06), oak, bevel=0.01, segs=2),
         box("Carcass", (0, 0.12 + (h - 0.24) * 0.5, 0), (w, h - 0.24, d), oak, bevel=0.02, segs=2),
         box("Cornice0", (0, h - 0.09, -0.01), (w + 0.06, 0.06, d + 0.04), oak, bevel=0.012, segs=2),
         box("Cornice1", (0, h - 0.03, -0.02), (w + 0.12, 0.06, d + 0.08), oak, bevel=0.015, segs=2)]
    dw, dh = (w - 0.1) * 0.5, h - 0.24 - 0.12
    for s in (-1, 1):
        cx, cy, cz = s * (dw * 0.5 + 0.01), 0.18 + dh * 0.5, -d * 0.5 - 0.012
        o.append(box(f"Door{s}", (cx, cy, cz), (dw, dh, 0.03), door, bevel=0.006, segs=2))
        # two raised fielded panels on each, set in a frame of stiles and rails
        for py in (-0.25, 0.25):
            ph = dh * 0.42
            o.append(box(f"Panel{s}{py}", (cx, cy + py * dh, cz - 0.017), (dw - 0.16, ph, 0.008), door, bevel=0.012, segs=3))
        o.append(lathe(f"Pull{s}", [(0.018, 0.0), (0.018, 0.02), (0.024, 0.03), (0.0, 0.034)], iron, at=(s * 0.06, 1.2, -d * 0.5 - 0.027), segs=12,
                       basis=rot((1, 0, 0), -math.pi * 0.5)))
    return o


PIECES = {
    "fx_photo_frame": (photo_frame, (0, 0, 0), 0.9, 0.0, 180),
    "fx_plant_pot": (plant_pot, (0, 0, 0.2), 1.4, 0.4, 30),
    "fx_plant_live": (lambda: plant(False), (0, 0, 0.6), 2.0, 0.4, 30),
    "fx_plant_dead": (lambda: plant(True), (0, 0, 0.4), 2.0, 0.4, 30),
    "fx_font": (font, (0, 0, 0.6), 4.0, 0.6, 30),
    "fx_vestry_wardrobe": (wardrobe, (0, 0, 1.2), 5.5, 0.4, 30),
    "fx_lib_fireplace": (lib_fireplace, (0, -0.3, 1.2), 5.0, 0.6, 60),
    "fx_lib_firedogs": (lib_firedogs, (0, -0.35, 0.15), 1.8, 0.5, 60),
    "fx_card_catalogue": (card_catalogue, (0, 0.2, 0.7), 3.0, 0.6, 30),
    "fx_book_cart": (book_cart, (0, 0, 0.45), 2.4, 0.5, 30),
    "fx_cigar_box": (cigar_box, (0, 0, 0.08), 1.0, 0.4, 30),
    "fx_cigar_lid": (cigar_lid, (0, 0, 0.14), 1.0, 0.4, 30),
    "fx_pump_housing": (pump_housing, (0, 0, -0.2), 2.8, 0.4, 30),
    "fx_valve_wheel": (valve_wheel, (0, 0, 0.0), 2.0, 0.2, 20),
    "fx_mop_bucket": (mop_bucket, (0, 0, 0.15), 1.0, 0.4, 30),
    "fx_mop_head": (mop_head, (0, 0, -0.1), 1.0, 0.5, 30),
    "fx_lk_banker": (lk_banker, (0, 0, 0.2), 0.9, 0.3, 30),
    "fx_lk_floor": (lk_floor, (0, 0, 0.9), 3.6, 0.5, 30),
    "fx_lk_sconce": (lk_sconce, (0, 0, 0.0), 1.4, 0.2, 60),
    "fx_lk_candlestick": (lk_candlestick, (0, 0, 0.11), 0.6, 0.2, 30),
    "fx_banker_lamp": (banker_lamp, (0, 0, 0.2), 0.8, 0.3, 30),
    "fx_banker_lamp_glow": (banker_lamp_glow, (0, 0, 0.3), 0.8, 0.0, 30),
    "fx_kettle": (kettle, (0, 0, 0.1), 0.6, 0.2, 30),
    "fx_kitchen_chair": (kitchen_chair, (0, 0, 0.45), 2.0, 0.4, 30),
    "fx_tin_can": (lambda: tin_can(False), (0, 0, 0.06), 0.4, 0.2, 30),
    "fx_tin_can_lidded": (lambda: tin_can(True), (0, 0, 0.06), 0.4, 0.2, 30),
    "fx_box_stove": (box_stove, (0, 0, 0.4), 2.0, 0.4, 60),
    "fx_cot": (cot, (0, 0, 0.35), 3.2, 1.0, 40),
    "fx_hand_saw": (hand_saw, (0, 0, 0.0), 0.9, 0.0, 0),
    "fx_wrench": (wrench, (0, 0, 0.05), 0.5, 0.0, 0),
    "fx_pliers": (pliers, (0, 0, -0.05), 0.5, 0.0, 0),
    "fx_screwdriver": (screwdriver, (0, 0, 0.0), 0.5, 0.0, 0),
    "fx_rope_coil": (rope_coil, (0, 0, 0.0), 0.7, 0.0, 0),
    "fx_block_plane": (block_plane, (0, 0, 0.04), 0.5, 0.2, 30),
    "fx_long_tools": (long_tools, (-0.15, -0.15, 0.7), 2.4, 0.4, 40),
    "fx_gas_can": (gas_can, (0, 0, 0.18), 1.0, 0.3, 40),
    "fx_reading_table": (reading_table, (0, 0, 0.4), 2.8, 0.8, 30),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        F.clear() if hasattr(F, "clear") else None
        fn, tgt, dist, h, yaw = PIECES[name]
        parts = fn()
        F.export(parts, name, tgt, dist, h, yaw)
