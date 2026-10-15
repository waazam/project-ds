"""The small shared kits and leftovers (the owner, 2026-10-15: every model is built in Blender): the janitor's closet
bottles and tins (JanitorShelf), the room clutter (ClutterKit's carton, frame, board, broom, bucket, bundle), the wall
crumbs (DebrisKit), the service doors (ServiceDoor: leaf, hardware, frame trim), the failing bulb's holder, cage and glass
(FailingBulb, the save lamps' glass too), the save lamp's cage (SaveLamps), the hurricane lamp (HollowProps.HangingLamp)
and the Hollow's woodpile logs (Woodpile). Exported like fixtures.py (furniture.export: role materials, the game's box
UVs, a baked cavity map) to assets/models/furniture/<name>.glb:

    blender -b --python tools/Blender/kits.py [-- name ...]

Each piece is drawn in its C# builder's own frame (game coordinates through P()). The parametric ones (a carton's size,
a door's width, a frame's length) are modelled at a reference size the code scales them from; the code keeps the lights,
the emission, the pivots, the cords and the placing, and its old builders as fallbacks when a .glb is missing.
The janitor's labels keep their own UVs (KEEP_UV "label": u across the front, v up the label band) for the game's atlas
row and offset (JanitorShelf.LabelMat).
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furniture as F
from kit import obj_from_bmesh, add_mod, apply_mods
from fixtures import P, role, tube, cyl, lathe, box, blob, rot, _done

V = Vector
F.KEEP_UV.add("label")
F.ROLE_COLOURS.update({
    "tin": (0.55, 0.55, 0.52), "label": (0.8, 0.75, 0.6), "plastic": (0.8, 0.78, 0.7), "cap": (0.15, 0.2, 0.45),
    "glass": (0.45, 0.32, 0.18), "nozzle": (0.4, 0.36, 0.3), "trigger": (0.12, 0.12, 0.12), "wire": (0.3, 0.3, 0.3),
    "grip": (0.1, 0.1, 0.1), "body": (0.55, 0.42, 0.28), "flap": (0.48, 0.36, 0.24), "wood": (0.42, 0.3, 0.2),
    "canvas": (0.35, 0.3, 0.25), "bristle": (0.6, 0.5, 0.3), "metal": (0.45, 0.45, 0.45), "trim": (0.35, 0.35, 0.35),
    "twine": (0.75, 0.68, 0.5), "crumb": (0.6, 0.58, 0.55), "leaf": (0.34, 0.38, 0.34), "edge": (0.3, 0.33, 0.3),
    "hw": (0.5, 0.48, 0.45), "frame": (0.24, 0.22, 0.2), "bark": (0.3, 0.25, 0.2), "end": (0.6, 0.52, 0.4),
    "flame": (1.0, 0.7, 0.3), "cord": (0.06, 0.06, 0.06),
})


# ------------------------------------------------------------------ the janitor's lathe (JanitorShelf.Lathe)

ROWS = 8


class Spec:
    def __init__(self, prof, sides=24, sz=1.0, sq=2.0, cap_y=99.0, label=True, l0=0.0, l1=0.0, lhalf=math.pi, label_rings=10 ** 6, dents=()):
        self.prof, self.sides, self.sz, self.sq, self.cap_y = prof, sides, sz, sq, cap_y
        self.label, self.l0, self.l1, self.lhalf, self.label_rings, self.dents = label, l0, l1, lhalf, label_rings, dents


def _smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def _angdiff(a, b):
    d = (b - a) % math.tau
    return d - math.tau if d > math.pi else d


def _pos(s, a, i):
    """JanitorShelf.Pos: a = 0 the front (game -Z), squashed front to back and squared off where it's wide, dented."""
    r0, y = s.prof[i]
    c, sn = math.cos(a), math.sin(a)
    t = _smooth(0.022, 0.05, r0)
    sf = 1.0
    if s.sq != 2.0:
        sf = 1 + ((abs(c) ** s.sq + abs(sn) ** s.sq) ** (-1.0 / s.sq) - 1) * t
    sz = 1 + (s.sz - 1) * t
    d = 1.0
    for (da, dy, depth, aw, yw) in s.dents:
        ax, yy = _angdiff(a, da) / aw, (y - dy) / yw
        d -= depth * math.exp(-(ax * ax + yy * yy))
    r = r0 * sf * d
    return V((sn * r, y, -c * r * sz))


def jlathe(name, s, body, cap=None, label=None):
    """The turned body: its cap rings above cap_y in `cap`, its label band (front, l0..l1 up) in `label` with its own UVs."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    n, m = len(s.prof), s.sides
    A = lambda j: -math.pi + math.tau * j / m
    vs = [[bm.verts.new(P(_pos(s, A(j), i))) for j in range(m)] for i in range(n)]
    mats = [body] + ([cap] if cap else []) + ([label] if label else [])
    for i in range(n - 1):
        for j in range(m):
            j1 = (j + 1) % m
            ym = (s.prof[i][1] + s.prof[i + 1][1]) * 0.5
            am = (A(j) + A(j + 1)) * 0.5
            lab = label is not None and s.label and i < s.label_rings and s.l0 < ym < s.l1 and abs(am) <= s.lhalf
            ring = [vs[i][j], vs[i + 1][j], vs[i + 1][j1], vs[i][j1]]
            # (the bottom's and the top's centre rings are one point: a triangle there)
            q = []
            for v in ring:
                if v not in q:
                    q.append(v)
            if s.prof[i][0] < 1e-6:
                q = [vs[i][j], vs[i + 1][j], vs[i + 1][j1]]
            elif s.prof[i + 1][0] < 1e-6:
                q = [vs[i][j], vs[i + 1][j], vs[i][j1]]
            f = bm.faces.new(q)
            f.material_index = mats.index(label) if lab else (mats.index(cap) if cap and ym > s.cap_y else 0)
            if lab:
                for lp in f.loops:
                    jj = [k for k in range(m) if vs[i][k] is lp.vert or vs[i + 1][k] is lp.vert][0]
                    ii = i if any(vs[i][k] is lp.vert for k in range(m)) else i + 1
                    a = A(jj) if jj != 0 or j != m - 1 else math.pi   # (the seam: the far end of the last face)
                    u = (s.lhalf - a) / (2 * s.lhalf)
                    t = min(1.0, max(0.0, (s.prof[ii][1] - s.l0) / (s.l1 - s.l0)))
                    lp[uv].uv = (u, t)
    # (the centre points: merge each ring that is a point)
    for i in (0, n - 1):
        if s.prof[i][0] < 1e-6:
            bmesh.ops.remove_doubles(bm, verts=vs[i], dist=1e-7)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for mt in mats:
        me.materials.append(mt)
    for p in me.polygons:
        p.use_smooth = True
    return ob


CAN = [(0.0, 0.002), (0.074, 0.002), (0.081, 0.0), (0.0855, 0.004), (0.086, 0.009), (0.083, 0.013),
       (0.083, 0.017), (0.083, 0.045), (0.083, 0.07), (0.083, 0.095), (0.083, 0.12), (0.083, 0.145), (0.083, 0.168),
       (0.083, 0.173), (0.0865, 0.179), (0.087, 0.186), (0.0845, 0.19), (0.0795, 0.189),
       (0.077, 0.184), (0.0715, 0.1825), (0.0695, 0.1865), (0.065, 0.1865), (0.062, 0.1845), (0.0, 0.1845)]
CAN_DENTS = [(), ((0.9, 0.09, 0.08, 0.3, 0.018),), ((-2.0, 0.06, 0.07, 0.35, 0.02), (2.4, 0.12, 0.09, 0.25, 0.015))]


def js_can(variant):
    """A gallon paint can (the quart and the pint are it scaled): the chimes, the body, the lid pressed in; dented."""
    return [jlathe("Can", Spec(CAN, 24, l0=0.017, l1=0.168, dents=CAN_DENTS[variant]), role("tin"), None, role("label"))]


def js_can_bail():
    """The gallon's bail at no lean, its origin at the ears (the game leans it about X): the ears, the wire, the grip."""
    wire, grip = role("wire"), role("grip")
    r = 0.0875
    o = [box(f"Ear{sx}", (sx * (r - 0.001), 0, 0), (0.004, 0.016, 0.012), wire, bevel=0.0012) for sx in (-1, 1)]
    pts = [V((r * math.cos(math.pi * i / 20), r * 1.05 * math.sin(math.pi * i / 20), 0)) for i in range(21)]
    o.append(tube("Bail", pts, 0.0016, wire, 6, caps=True))
    top = V((0, r * 1.05, 0))
    o.append(lathe("Grip", [(0.0, -0.025), (0.004, -0.025), (0.0055, -0.022), (0.0058, 0.0), (0.0055, 0.022), (0.004, 0.025), (0.0, 0.025)],
                   grip, at=top, segs=10, basis=rot((0, 0, 1), math.pi * 0.5)))
    return o


JUG = [(0.0, 0.004), (0.064, 0.002), (0.072, 0.006), (0.075, 0.016), (0.075, 0.03), (0.075, 0.055), (0.075, 0.08), (0.075, 0.105),
       (0.075, 0.13), (0.075, 0.15), (0.075, 0.172), (0.074, 0.198), (0.067, 0.222), (0.047, 0.245),
       (0.026, 0.259), (0.019, 0.266), (0.019, 0.276), (0.0225, 0.277), (0.0225, 0.29), (0.0225, 0.3), (0.019, 0.303), (0.0, 0.303)]


def js_jug(squeezed):
    """A bleach jug: squared off and flat front to back, a moulded handle at the back of the shoulder, a screw cap."""
    body = role("plastic")
    s = Spec(JUG, 28, sz=0.66, sq=3.6, cap_y=0.2765, l0=0.03, l1=0.172, lhalf=0.95,
             dents=((1.2, 0.1, 0.06, 0.5, 0.03),) if squeezed else ())
    o = [jlathe("Jug", s, body, role("cap"), role("label"))]
    pts = [V((0, 0.14 + (0.236 - 0.14) * t, 0.036 + 0.05 * math.sin(math.pi * t) - 0.006 * t)) for t in (i / 10 for i in range(11))]
    o.append(tube("Handle", pts, (0.0105, 0.0085), body, 10, caps=True))
    # the cap's grip ribs
    for k in range(16):
        a = math.tau * k / 16
        o.append(box(f"Rib{k}", (math.sin(a) * 0.0228, 0.2885, -math.cos(a) * 0.0228), (0.0018, 0.02, 0.0016), role("cap"), bevel=0.0, basis=rot((0, 1, 0), -a)))
    return o


SPRAY = [(0.0, 0.003), (0.031, 0.001), (0.036, 0.007), (0.036, 0.03), (0.036, 0.06), (0.036, 0.09), (0.036, 0.12), (0.036, 0.148),
         (0.035, 0.163), (0.026, 0.182), (0.0145, 0.193), (0.0145, 0.203), (0.0165, 0.2035), (0.0165, 0.221), (0.0, 0.221)]


def js_spray():
    """A trigger sprayer: the bottle, its cap and head (collar, body, shroud), the nozzle, the trigger."""
    capm, nozzle, trig = role("cap"), role("nozzle"), role("trigger")
    s = Spec(SPRAY, 20, sz=0.72, sq=3.0, cap_y=0.2032, l0=0.03, l1=0.148, lhalf=1.15)
    o = [jlathe("Spray", s, role("plastic"), capm, role("label"))]
    o.append(lathe("Collar", [(0.0, 0.2205), (0.0125, 0.2205), (0.0118, 0.224), (0.0108, 0.2275), (0.0, 0.2275)], capm, segs=16))
    prof = [(0.021, 0.2365, 0.004), (0.018, 0.2365, 0.0105), (0.011, 0.2362, 0.0138), (-0.006, 0.2355, 0.013),
            (-0.026, 0.2348, 0.0108), (-0.039, 0.2345, 0.0094), (-0.044, 0.2345, 0.0062)]
    o.append(tube("Head", [(0, y, z) for (z, y, r) in prof], [r for (z, y, r) in prof], capm, 14, caps=True))
    for i in range(3):
        a = -0.55 + i * 0.55
        o.append(box(f"Shroud{i}", (math.sin(a) * 0.0075, 0.2205, -0.031 - math.cos(a) * 0.0045), (0.0085, 0.024, 0.004), capm, bevel=0.001,
                     basis=rot((0, 1, 0), -a) @ rot((1, 0, 0), 0.18)))
    o.append(lathe("Nozzle", [(0.0068, 0.0), (0.0066, 0.006), (0.006, 0.0115), (0.0025, 0.0118), (0.0, 0.0115)], nozzle, at=(0, 0.2345, -0.043), segs=10,
                   basis=rot((1, 0, 0), -math.pi * 0.5)))
    tr = [(0, 0.2265, -0.036), (0, 0.214, -0.0425), (0, 0.202, -0.041), (0, 0.1955, -0.0345)]
    o.append(tube("Trigger", tr, [(0.0048, 0.0035), (0.0046, 0.0034), (0.0044, 0.0032), (0.0042, 0.003)], trig, 8, caps=True))
    return o


BOTTLE = [(0.0, 0.004), (0.034, 0.001), (0.0395, 0.006), (0.04, 0.03), (0.04, 0.055), (0.04, 0.08), (0.04, 0.105), (0.04, 0.13),
          (0.039, 0.145), (0.031, 0.164), (0.017, 0.179), (0.0128, 0.188), (0.0128, 0.197),
          (0.0145, 0.1975), (0.0145, 0.206), (0.0145, 0.214), (0.0, 0.214)]


def js_bottle():
    """A brown glass bottle (ammonia), a black screw cap."""
    return [jlathe("Bottle", Spec(BOTTLE, 20, cap_y=0.1972, l0=0.03, l1=0.13, lhalf=1.25), role("glass"), role("cap"), role("label"))]


def js_can_open():
    """JanitorShelf.Spilled's quart, standing (the game lays it down): no lid, its inside, crusted rim, a dent."""
    sr = 0.66
    prof = [(r * sr, y * sr) for (r, y) in CAN[:17]] + [(0.079 * sr, 0.17 * sr), (0.08 * sr, 0.02 * sr), (0.0, 0.02 * sr)]
    s = Spec(prof, 22, l0=0.017 * sr, l1=0.168 * sr, label_rings=16, dents=((0.6, 0.07 * sr, 0.08, 0.35, 0.02),))
    return [jlathe("Open", s, role("tin"), None, role("label"))]


# ------------------------------------------------------------------ ClutterKit's pieces

def cl_carton():
    """A carton at the reference size 0.4 x 0.3 x 0.32 (the game scales it to each): eased edges, the lid's two flaps
    parted down the middle, a strip of tape over the seam, a corner crushed a little."""
    body, flap = role("body"), role("flap")
    w, h, d = 0.4, 0.3, 0.32
    b = box("Box", (0, h * 0.5, 0), (w, h, d), body, bevel=0.012, segs=2)
    for v in b.data.vertices:
        g = V((v.co.x, v.co.z, v.co.y))
        if g.x > w * 0.3 and g.y > h * 0.8 and g.z < -d * 0.3:
            g.y -= 0.012 * (g.x - w * 0.3) / (w * 0.2)
        g.x += 0.004 * math.sin(g.y * 9.0) * (abs(g.x) / (w * 0.5))   # (the sides bowed a little)
        v.co = P(g)
    o = [b]
    for s in (-1, 1):
        o.append(box(f"Flap{s}", (s * w * 0.25, h + 0.003, 0), (w * 0.5 - 0.01, 0.006, d - 0.01), flap, bevel=0.002))
    o.append(box("Tape", (0, h + 0.0065, 0), (0.05, 0.0015, d + 0.004), flap, bevel=0.0006))
    for s in (-1, 1):
        o.append(box(f"TapeEnd{s}", (0, h - 0.03, s * (d * 0.5 + 0.001)), (0.05, 0.06, 0.0015), flap, bevel=0.0006))
    return o


def cl_frame():
    """A picture frame at 0.5 x 0.65 (scaled to each): a moulded frame (an outer bead, a cove, an inner lip) round the
    canvas, its back's stretcher bars."""
    wood, canvas = role("wood"), role("canvas")
    w, h, t, f = 0.5, 0.65, 0.03, 0.06
    o = [box("Bottom", (0, f * 0.5, 0), (w, f, t), wood, bevel=0.008, segs=3),
         box("Top", (0, h - f * 0.5, 0), (w, f, t), wood, bevel=0.008, segs=3),
         box("Left", (-w * 0.5 + f * 0.5, h * 0.5, 0), (f, h - f * 2, t), wood, bevel=0.008, segs=3),
         box("Right", (w * 0.5 - f * 0.5, h * 0.5, 0), (f, h - f * 2, t), wood, bevel=0.008, segs=3)]
    # the bead round the outside, both faces
    for z in (-t * 0.5, t * 0.5):
        o.append(tube(f"Bead{z}", [(-w * 0.5 + 0.008, 0.008, z), (w * 0.5 - 0.008, 0.008, z), (w * 0.5 - 0.008, h - 0.008, z),
                                    (-w * 0.5 + 0.008, h - 0.008, z), (-w * 0.5 + 0.008, 0.008, z)], 0.0045, wood, 6, caps=False))
        o.append(tube(f"Lip{z}", [(-w * 0.5 + f, f, z), (w * 0.5 - f, f, z), (w * 0.5 - f, h - f, z), (-w * 0.5 + f, h - f, z), (-w * 0.5 + f, f, z)],
                      0.0035, wood, 6, caps=False))
    o.append(box("Canvas", (0, h * 0.5, 0), (w - f * 1.6, h - f * 1.6, 0.008), canvas, bevel=0.002))
    o.append(box("Stretcher", (0, h * 0.5, 0.009), (0.025, h - f * 2, 0.012), wood, bevel=0.003))
    return o


def cl_board():
    """A plank at 1.6 x 0.18 x 0.025 (scaled to each): planed, its arrises eased, a slight cup, a knot."""
    wood = role("wood")
    b = box("Plank", (0, 0.8, 0), (0.18, 1.6, 0.025), wood, bevel=0.004, segs=2)
    for v in b.data.vertices:
        g = V((v.co.x, v.co.z, v.co.y))
        g.z += 0.004 * (g.x / 0.09) ** 2 + 0.003 * math.sin(g.y * 2.1)   # (cupped across, a little bow along)
        v.co = P(g)
    o = [b, blob("Knot", (0.03, 1.05, -0.0125), (0.012, 0.016, 0.002), wood, seed=4, amp=0.2, subdiv=2)]
    return o


def cl_broom():
    """ClutterKit.Broom: a round handle with a rounded end set into a domed stock, a wire band, the bristles splaying
    from it to the floor, their tips ragged."""
    wood, bristle = role("wood"), role("bristle")
    o = [lathe("Handle", [(0.0, 0.0), (0.0145, 0.0), (0.0145, 1.12), (0.011, 1.13), (0.0, 1.135)], wood, at=(0, 0.215, 0), segs=12)]
    st = box("Stock", (0, 0.2, 0), (0.28, 0.045, 0.06), wood, bevel=0.016, segs=3)
    for v in st.data.vertices:
        g = V((v.co.x, v.co.z, v.co.y))
        if g.y > 0.2:
            g.y += 0.012 * (1 - (g.x / 0.14) ** 2)   # (the domed top)
        v.co = P(g)
    o.append(st)
    o.append(lathe("Ferrule", [(0.0, 0.2), (0.019, 0.2), (0.019, 0.245), (0.0165, 0.25), (0.0, 0.25)], role("trim"), segs=14))
    # the bristles: a flaring block of tufts, each a little different, ragged at the floor
    rnd = random.Random(11)
    for i in range(14):
        x = -0.125 + 0.25 * (i + 0.5) / 14
        for zz in (-0.012, 0.012):
            xb = x * 1.1 + rnd.uniform(-0.004, 0.004)
            zb = zz * 1.35 + rnd.uniform(-0.003, 0.003)
            yb = rnd.uniform(0.0, 0.012)
            o.append(tube(f"Tuft{i}{zz}", [(x, 0.18, zz), ((x + xb) * 0.5, 0.09, (zz + zb) * 0.5), (xb, yb, zb)],
                          [(0.0095, 0.012), (0.0105, 0.0135), (0.011, 0.014)], bristle, 6, caps=True))
    return o


def cl_bucket():
    """ClutterKit.Bucket: a pressed pail, foot ring, two swaged hoops, rolled rim, its inside, ears and the bail down."""
    metal, trim = role("metal"), role("trim")
    prof = [(0.0, 0.006), (0.112, 0.006), (0.116, 0.0), (0.122, 0.0), (0.121, 0.012), (0.124, 0.012), (0.128, 0.07), (0.131, 0.074), (0.132, 0.08),
            (0.14, 0.2), (0.143, 0.204), (0.144, 0.21), (0.15, 0.272), (0.158, 0.276), (0.16, 0.284), (0.155, 0.29), (0.147, 0.284),
            (0.143, 0.27), (0.118, 0.025), (0.0, 0.025)]
    o = [lathe("Pail", prof, metal, segs=40)]
    for s in (-1, 1):
        o.append(lathe(f"Ear{s}", [(0.0, 0.0), (0.016, 0.0), (0.013, 0.008), (0.0, 0.009)], trim, at=(s * 0.149, 0.245, 0), segs=12,
                       basis=rot((0, 0, 1), -s * math.pi * 0.5)))
    pts = [(-math.cos(a) * 0.158, 0.245 - math.sin(a) * 0.04, -math.sin(a) * 0.162) for a in (math.pi * i / 16 for i in range(17))]
    o.append(tube("Bail", pts, 0.0035, trim, 6, caps=True))
    return o


def cl_bundle():
    """A tied bundle at 0.3 x 0.12 x 0.24 (scaled to each): a soft slab, its sheets' edges, the string round both ways."""
    body, twine = role("body"), role("twine")
    w, h, d = 0.3, 0.12, 0.24
    b = box("Slab", (0, h * 0.5, 0), (w, h, d), body, bevel=0.01, segs=3)
    for v in b.data.vertices:
        g = V((v.co.x, v.co.z, v.co.y))
        pinch = math.exp(-(g.x / 0.02) ** 2) + math.exp(-(g.z / 0.02) ** 2)
        if g.y > h * 0.6:
            g.y -= 0.006 * min(1.0, pinch)   # (the string pulls the top in)
        v.co = P(g)
    o = [b]
    o.append(box("TwineA", (0, h * 0.5, 0), (0.008, h + 0.004, d + 0.004), twine, bevel=0.002))
    o.append(box("TwineB", (0, h * 0.5, 0), (w + 0.004, h + 0.004, 0.008), twine, bevel=0.002))
    o.append(blob("Knot", (0, h + 0.004, 0), (0.012, 0.006, 0.01), twine, seed=3, amp=0.25, subdiv=2))
    return o


# ------------------------------------------------------------------ DebrisKit's crumbs

def db_crumb(seed):
    """A crumb of plaster or concrete about 1 x 0.6 x 1 round its centre (the game sizes each): a broken lump."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0)
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    cuts = [V((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))).normalized() for _ in range(3)]
    for v in bm.verts:
        n = v.co.normalized()
        r = 0.55 + 0.08 * math.sin(n.x * 4 + ph[0]) * math.sin(n.z * 3 + ph[1]) + 0.05 * math.sin(n.y * 5 + ph[2])
        for c in cuts:   # (flat broken faces)
            r = min(r, 0.42 / max(0.2, n.dot(c)) if n.dot(c) > 0.2 else r)
        g = V((n.x * r, n.y * r * 0.6, n.z * r))
        v.co = P(g)
    ob = _done(bm, "Crumb", role("crumb"), smooth=False)
    return [ob]


# ------------------------------------------------------------------ ServiceDoor

def svc_leaf():
    """The leaf at 1.0 x 2.1 x 0.05 (the game scales it to each door), hinge side at x 0: the pressed steel slab and
    its rolled edges a hair proud all round."""
    leaf, edge = role("leaf"), role("edge")
    w, h, t, e = 1.0, 2.1, 0.05, 0.022
    et = t + 0.006
    o = [box("Slab", (w * 0.5, h * 0.5, 0), (w - 0.03, h - 0.03, t), leaf, bevel=0.002)]
    o.append(box("EdgeL", (e * 0.5, h * 0.5, 0), (e, h, et), edge, bevel=0.005, segs=3))
    o.append(box("EdgeR", (w - e * 0.5, h * 0.5, 0), (e, h, et), edge, bevel=0.005, segs=3))
    o.append(box("EdgeT", (w * 0.5, h - e * 0.5, 0), (w - 2 * e, e, et), edge, bevel=0.005, segs=3))
    o.append(box("EdgeB", (w * 0.5, e * 0.5, 0), (w - 2 * e, e, et), edge, bevel=0.005, segs=3))
    # two shallow pressed ribs across each face (the steel's stiffeners)
    for z in (-t * 0.5, t * 0.5):
        for y in (0.72, 1.5):
            o.append(box(f"Rib{z}{y}", (w * 0.5, y, z), (w - 0.14, 0.03, 0.004), leaf, bevel=0.0018, segs=2))
    return o


def svc_lever():
    """One face's lever set, its origin on the leaf's face at the spindle, out along +Z (the game mirrors it for the
    other face): the rose, the spindle's neck, the lever curling toward the hinge, the escutcheon under it."""
    hw = role("hw")
    o = [lathe("Rose", [(0.0, 0.0), (0.032, 0.0), (0.032, 0.004), (0.03, 0.009), (0.024, 0.012), (0.0, 0.012)], hw, segs=24,
               basis=rot((1, 0, 0), math.pi * 0.5))]
    o.append(cyl("Neck", (0, 0, 0.012), (0, 0, 0.05), 0.011, 0.0105, hw, 12, caps=False))
    o.append(tube("Lever", [(0.006, 0, 0.055), (-0.03, -0.001, 0.0565), (-0.08, -0.003, 0.0585), (-0.112, -0.004, 0.06)],
                  [(0.012, 0.009), (0.0105, 0.0075), (0.0105, 0.0075), (0.01, 0.007)], hw, 12, caps=True))
    o.append(blob("LeverEnd", (-0.113, -0.004, 0.06), (0.011, 0.008, 0.008), hw, seed=2, amp=0.02, subdiv=2))
    o.append(lathe("Escutcheon", [(0.0, 0.0), (0.02, 0.0), (0.0195, 0.004), (0.017, 0.006), (0.0, 0.006)], hw, at=(0, -0.14, 0), segs=20,
                   basis=rot((1, 0, 0), math.pi * 0.5)))
    o.append(box("Keyhole", (0, -0.14, 0.0062), (0.004, 0.012, 0.001), role("grip"), bevel=0.0))
    return o


def svc_kick():
    """A kick plate 0.9 wide (the game scales it to the leaf), scuffed: its edges turned, screw heads at the corners."""
    hw = role("hw")
    o = [box("Plate", (0, 0, 0), (0.9, 0.25, 0.003), hw, bevel=0.0012)]
    for x in (-0.43, 0.43):
        for y in (-0.105, 0.105):
            o.append(lathe(f"Screw{x}{y}", [(0.0, 0.0), (0.004, 0.0), (0.0035, 0.0012), (0.0, 0.0016)], hw, at=(x, y, 0.0015), segs=8,
                           basis=rot((1, 0, 0), math.pi * 0.5)))
    return o


def svc_hinge():
    """A hinge's knuckle round its centre, a pin's finials at both ends."""
    hw = role("hw")
    return [lathe("Knuckle", [(0.0, -0.068), (0.006, -0.068), (0.008, -0.064), (0.011, -0.06), (0.011, -0.002), (0.0115, 0.0),
                              (0.011, 0.002), (0.011, 0.06), (0.008, 0.064), (0.006, 0.068), (0.0, 0.068)], hw, segs=14)]


def svc_casing():
    """A face trim bar, 1 m long up Y from its centre, 0.055 across X and 0.014 deep (scaled to each): a back band, a
    cove and an eased outer edge, out toward +Z."""
    frame = role("frame")
    o = [box("Band", (0, 0, 0), (0.055, 1.0, 0.014), frame, bevel=0.004, segs=3)]
    o.append(tube("Bead", [(-0.0215, -0.5, 0.007), (-0.0215, 0.5, 0.007)], 0.0035, frame, 8, caps=True))
    return o


def svc_strip():
    """A soffit or stop strip, 1 m long up Y from its centre, 0.02 x 0.016 (scaled to each), its arrises eased."""
    return [box("Strip", (0, 0, 0), (0.02, 1.0, 0.016), role("frame"), bevel=0.003, segs=2)]


# ------------------------------------------------------------------ FailingBulb, SaveLamps

R = 0.045


def fb_holder():
    """The holder over the bulb at a 0.045 glass (scaled to each): the bakelite cup, its ribs, the brass collar."""
    w = role("wire")
    o = [lathe("Cup", [(0.0, R * 0.9), (R * 0.42, R * 0.9), (R * 0.46, R * 1.05), (R * 0.47, R * 1.15), (R * 0.44, R * 1.25),
                       (R * 0.5, R * 1.35), (R * 0.5, R * 2.2), (R * 0.45, R * 2.35), (R * 0.2, R * 2.4), (R * 0.12, R * 2.6), (0.0, R * 2.6)], w, segs=16)]
    for k in range(8):
        a = math.tau * k / 8
        o.append(box(f"Rib{k}", (math.cos(a) * R * 0.5, R * 1.78, math.sin(a) * R * 0.5), (0.003, R * 0.8, 0.003), w, bevel=0.0008, basis=rot((0, 1, 0), -a)))
    return o


def fb_cage():
    """FailingBulb's wire cage at a 0.045 glass: four ribs and two hoops (the game scales it)."""
    w = role("wire")
    r = R * 1.55
    o = []
    for i in range(4):
        a = i * math.pi * 0.5 + 0.4
        d = V((math.cos(a), 0, math.sin(a)))
        pts = [V((0, R * 2.2, 0)) + d * R * 0.6, d * r + V((0, R * 0.6, 0)), d * r * 0.9 + V((0, -R * 0.9, 0)), V((0, -R * 1.5, 0))]
        o.append(tube(f"Rib{i}", pts, 0.0035, w, 6, caps=True))
    for (y, rr) in ((R * 0.6, r), (-R * 0.6, r * 0.94)):
        pts = [(math.cos(math.tau * k / 24) * rr, y, math.sin(math.tau * k / 24) * rr) for k in range(25)]
        o.append(tube(f"Hoop{y}", pts, 0.0035, w, 6, caps=False))
    o.append(lathe("Foot", [(0.0, -R * 1.62), (0.008, -R * 1.62), (0.009, -R * 1.5), (0.0, -R * 1.4)], w, segs=10))
    return o


def fb_glass():
    """A bulb's glass round its centre, R 0.045 across and 2.3 R tall (the game scales it): a pear, the neck up into the
    holder."""
    prof = [(0.0, -1.15), (0.35, -1.12), (0.68, -1.0), (0.9, -0.78), (1.0, -0.45), (0.98, -0.1), (0.86, 0.25), (0.62, 0.6), (0.46, 0.85),
            (0.42, 1.0), (0.42, 1.15), (0.0, 1.15)]
    return [lathe("Glass", [(r * R, y * R) for (r, y) in prof], role("glass"), segs=20)]


def sl_cage():
    """The save lamp's cage under its cord: the cap and six wires round the bulb, a ring where they meet below."""
    cord = role("cord")
    o = [lathe("Cap", [(0.0, 0.02), (0.03, 0.02), (0.031, 0.024), (0.03, 0.06), (0.028, 0.07), (0.012, 0.074), (0.007, 0.08), (0.0, 0.08)], cord, segs=16)]
    for i in range(6):
        a = i / 6 * math.tau
        d = V((math.cos(a), 0, math.sin(a)))
        o.append(tube(f"Wire{i}", [V((0, 0.02, 0)) + d * 0.03, V((0, -0.07, 0)) + d * 0.055, V((0, -0.15, 0)) + d * 0.01], 0.003, cord, 6, caps=True))
    o.append(lathe("Knob", [(0.0, -0.162), (0.009, -0.158), (0.012, -0.15), (0.009, -0.143), (0.0, -0.14)], cord, segs=10))
    return o


# ------------------------------------------------------------------ HollowProps.HangingLamp (its foot at the origin)

def hl_frame():
    """The hurricane lamp's tinwork: the fuel font, the vented cap, the air tubes, the guard, the bail, the wick knob."""
    tin = role("tin")
    o = [lathe("Font", [(0.0, 0.0), (0.066, 0.0), (0.072, 0.006), (0.076, 0.022), (0.075, 0.04), (0.066, 0.054), (0.046, 0.062),
                        (0.03, 0.066), (0.028, 0.074), (0.046, 0.078), (0.048, 0.086), (0.0, 0.086)], tin, segs=24),
         lathe("Cap", [(0.04, 0.228), (0.058, 0.236), (0.062, 0.244), (0.052, 0.258), (0.034, 0.266), (0.022, 0.272),
                       (0.03, 0.278), (0.03, 0.284), (0.012, 0.292), (0.0, 0.293)], tin, segs=24, cap_bottom=False)]
    # the cap's vents
    for k in range(10):
        a = math.tau * k / 10
        o.append(box(f"Vent{k}", (math.cos(a) * 0.054, 0.25, math.sin(a) * 0.054), (0.004, 0.006, 0.012), role("grip"), bevel=0.0008,
                     basis=rot((0, 1, 0), -a) @ rot((0, 0, 1), 0.7)))
    for s in (-1, 1):
        pts = [(s * 0.07, 0.03, 0), (s * 0.092, 0.05, 0), (s * 0.094, 0.12, 0), (s * 0.094, 0.2, 0), (s * 0.082, 0.236, 0), (s * 0.058, 0.248, 0)]
        o.append(tube(f"Tube{s}", pts, 0.0075, tin, 10, caps=True))
    for i in range(4):
        a = math.pi * 0.25 + math.pi * 0.5 * i
        d = V((math.cos(a), 0, math.sin(a)))
        o.append(tube(f"Guard{i}", [d * 0.05 + V((0, 0.088, 0)), d * 0.078 + V((0, 0.14, 0)), d * 0.056 + V((0, 0.228, 0))], 0.0018, tin, 6, caps=True))
    o.append(tube("Ring", [(math.cos(math.tau * k / 32) * 0.078, 0.14, math.sin(math.tau * k / 32) * 0.078) for k in range(33)], 0.0022, tin, 6, caps=False))
    o.append(tube("Bail", [(-math.cos(a) * 0.066, 0.25 + math.sin(a) * 0.11, 0) for a in (math.pi * i / 16 for i in range(17))], 0.0022, tin, 6, caps=True))
    o.append(tube("Eye", [(math.cos(math.tau * k / 12) * 0.009, 0.354 + math.sin(math.tau * k / 12) * 0.009, 0) for k in range(13)], 0.0022, tin, 6, caps=False))
    o.append(cyl("Wick", (0, 0.072, 0.03), (0, 0.072, 0.05), 0.0025, 0.0025, tin, 6))
    o.append(lathe("Knob", [(0.0, 0.0), (0.009, 0.0), (0.009, 0.006), (0.0, 0.006)], tin, at=(0, 0.072, 0.05), segs=12, basis=rot((1, 0, 0), math.pi * 0.5)))
    return o


def hl_globe():
    """The globe: a pear of glass from the burner's seat up into the cap (open both ends, as the code's)."""
    return [lathe("Globe", [(0.042, 0.084), (0.05, 0.091), (0.056, 0.1), (0.062, 0.111), (0.066, 0.124), (0.068, 0.148), (0.066, 0.162),
                            (0.062, 0.176), (0.056, 0.19), (0.05, 0.204), (0.042, 0.222), (0.04, 0.232)], role("glass"), segs=24,
                  cap_bottom=False, cap_top=False)]


def hl_flame():
    """The flame: a small blade of light on the wick, flat front to back."""
    return [lathe("Flame", [(0.006, 0.084), (0.0095, 0.09), (0.011, 0.098), (0.0105, 0.105), (0.009, 0.112), (0.005, 0.121), (0.0, 0.13)],
                  role("flame"), segs=12, sz=0.45, cap_top=False)]


# ------------------------------------------------------------------ Woodpile

LOG_R, LOG_L = 0.085, 0.55


def wp_log(seed):
    """A split log round its centre along Z, LOG_R across and LOG_L long (the game sizes each): ridged bark, the ends
    sawn and grey, checked, a little proud of the bark's edge."""
    rnd = random.Random(seed)
    bark, end = role("bark"), role("end")
    segs, rings = 14, 7
    bm = bmesh.new()
    ph = [rnd.uniform(0, 6.28) for _ in range(5)]
    vs = []
    for i in range(rings):
        z = -LOG_L * 0.5 + LOG_L * i / (rings - 1)
        ring = []
        for s in range(segs):
            a = math.tau * s / segs
            ridge = 0.06 * abs(math.sin(a * 7 + ph[0] + z * 3)) + 0.04 * math.sin(a * 3 + ph[1]) * math.sin(z * 9 + ph[2])
            knot = 0.12 * math.exp(-(((a - ph[3]) % math.tau - math.pi) ** 2) / 0.05 - ((z - (ph[4] / 6.28 - 0.5) * 0.3) ** 2) / 0.002)
            r = LOG_R * (0.95 + ridge + knot) * (1 - 0.04 * (i / (rings - 1)))
            ring.append(bm.verts.new(P(V((math.cos(a) * r, math.sin(a) * r, z)))))
        vs.append(ring)
    for i in range(rings - 1):
        for s in range(segs):
            bm.faces.new((vs[i][s], vs[i][(s + 1) % segs], vs[i + 1][(s + 1) % segs], vs[i + 1][s]))
    o = [_done(bm, "Bark", bark)]
    for zs in (-1, 1):
        z = zs * (LOG_L * 0.5 + 0.002)
        o.append(lathe(f"End{zs}", [(0.0, -0.003), (LOG_R * 0.86, -0.003), (LOG_R * 0.88, 0.0), (LOG_R * 0.84, 0.003), (0.0, 0.004)], end,
                       at=(0, 0, z), segs=segs, basis=rot((1, 0, 0), zs * math.pi * 0.5)))
        for c in range(2):   # (checks: dark radial cracks)
            a = rnd.uniform(0, math.tau)
            o.append(box(f"Check{zs}{c}", (math.cos(a) * LOG_R * 0.4, math.sin(a) * LOG_R * 0.4, z + zs * 0.0035), (LOG_R * 0.6, 0.0025, 0.002),
                         role("grip"), bevel=0.0, basis=rot((0, 0, 1), a)))
    return o


def wp_stake():
    """A stake 1 m long up Y from its point at the origin (the game sizes it): split, its point hewn, its top battered."""
    wood = role("wood")
    return [lathe("Stake", [(0.0, 0.0), (0.02, 0.07), (0.035, 0.14), (0.034, 0.6), (0.031, 0.97), (0.033, 0.985), (0.026, 1.0), (0.0, 1.0)],
                  wood, segs=7)]


def wp_chip():
    """A chip of split wood, 0.075 x 0.015 x 0.04 round its centre (the game sizes each)."""
    return [blob("Chip", (0, 0, 0), (0.0375, 0.0075, 0.02), role("end"), seed=5, amp=0.25, subdiv=2)]


PIECES = {
    "js_can_0": (lambda: js_can(0), (0, 0, 0.1), 0.6, 0.2, 30),
    "js_can_1": (lambda: js_can(1), (0, 0, 0.1), 0.6, 0.2, 30),
    "js_can_2": (lambda: js_can(2), (0, 0, 0.1), 0.6, 0.2, 30),
    "js_can_bail": (js_can_bail, (0, 0, 0.05), 0.5, 0.2, 30),
    "js_can_open": (js_can_open, (0, 0, 0.07), 0.5, 0.2, 30),
    "js_jug_0": (lambda: js_jug(False), (0, 0, 0.15), 0.8, 0.2, 30),
    "js_jug_1": (lambda: js_jug(True), (0, 0, 0.15), 0.8, 0.2, 30),
    "js_spray": (js_spray, (0, 0, 0.12), 0.6, 0.2, 30),
    "js_bottle": (js_bottle, (0, 0, 0.11), 0.6, 0.2, 30),
    "cl_carton": (cl_carton, (0, 0, 0.15), 1.4, 0.4, 30),
    "cl_frame": (cl_frame, (0, 0, 0.32), 1.6, 0.2, 30),
    "cl_board": (cl_board, (0, 0, 0.8), 2.6, 0.2, 30),
    "cl_broom": (cl_broom, (0, 0, 0.5), 2.2, 0.4, 30),
    "cl_bucket": (cl_bucket, (0, 0, 0.15), 1.0, 0.4, 30),
    "cl_bundle": (cl_bundle, (0, 0, 0.06), 1.0, 0.4, 30),
    "db_crumb_0": (lambda: db_crumb(1), (0, 0, 0), 3.5, 0.6, 30),
    "db_crumb_1": (lambda: db_crumb(2), (0, 0, 0), 3.5, 0.6, 30),
    "db_crumb_2": (lambda: db_crumb(3), (0, 0, 0), 3.5, 0.6, 30),
    "db_crumb_3": (lambda: db_crumb(4), (0, 0, 0), 3.5, 0.6, 30),
    "svc_leaf": (svc_leaf, (0.5, 0, 1.05), 4.0, 0.2, 30),
    "svc_lever": (svc_lever, (-0.05, 0, -0.05), 0.6, 0.2, 40),
    "svc_kick": (svc_kick, (0, 0, 0), 1.5, 0.2, 10),
    "svc_hinge": (svc_hinge, (0, 0, 0), 0.4, 0.1, 30),
    "svc_casing": (svc_casing, (0, 0, 0), 1.6, 0.2, 40),
    "svc_strip": (svc_strip, (0, 0, 0), 1.6, 0.2, 40),
    "fb_holder": (fb_holder, (0, 0, 0.07), 0.4, 0.1, 30),
    "fb_cage": (fb_cage, (0, 0, 0.02), 0.5, 0.1, 30),
    "fb_glass": (fb_glass, (0, 0, 0), 0.35, 0.1, 30),
    "sl_cage": (sl_cage, (0, 0, -0.04), 0.6, 0.1, 30),
    "hl_frame": (hl_frame, (0, 0, 0.18), 1.0, 0.2, 30),
    "hl_globe": (hl_globe, (0, 0, 0.16), 0.6, 0.2, 30),
    "hl_flame": (hl_flame, (0, 0, 0.1), 0.2, 0.0, 30),
    "wp_log_0": (lambda: wp_log(1), (0, 0, 0), 1.0, 0.3, 40),
    "wp_log_1": (lambda: wp_log(2), (0, 0, 0), 1.0, 0.3, 40),
    "wp_log_2": (lambda: wp_log(3), (0, 0, 0), 1.0, 0.3, 40),
    "wp_stake": (wp_stake, (0, 0, 0.5), 1.8, 0.2, 30),
    "wp_chip": (wp_chip, (0, 0, 0), 0.3, 0.3, 30),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        F.clear()
        fn, tgt, dist, h, yaw = PIECES[name]
        parts = fn()
        F.export(parts, name, tgt, dist, h, yaw)
