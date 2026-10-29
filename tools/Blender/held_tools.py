"""The hand tools and the newel post's cap, modelled in Blender (2026-10-15, the owner: every model is made in Blender).
One model per thing serves the hand (HeldModels), the pickup on the ground (ItemMeshes) and, for the cap, the stairs:

    blender -b --python tools/Blender/held_tools.py [-- axe hammer knife lighter walkie newel_cap]

Each is written to assets/models/tools/<name>.glb. The materials are only named slots ("hickory", "forged", "honed",
"black", "leather", "chrome", "brass", "radio", "rubber", "grille", "label", "led", "stone", "break"): the game paints
them with its own (ToolModels.cs). The wear is in the vertex colours (grime where the hand goes, scale and rust on the
steel, damp under the stone), multiplied into those materials.

Space: the game's grip space (HeldModels: the origin in the middle of the fist, +Y up the handle, -Z the working end;
the walkie's speaker +Z), except the cap, which is in the stairs' cap space (y = 0 on the pier's seat). Blender is Z-up
and the glTF export turns Blender's +Y into the game's -Z, so every point here goes through g(x, y, z) (game) -> Blender.
The cap's broken underside is the same twelve points as the stump's top (StaircaseBuilder.BreakY), so it seats exactly.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "assets", "models", "tools")
os.makedirs(OUT, exist_ok=True)


def g(x, y, z):
    """A game point (Y up, -Z ahead) as a Blender point."""
    return Vector((x, -z, y))


RIGHT, UP, BACK, FWD = Vector((1, 0, 0)), Vector((0, 0, 1)), Vector((0, -1, 0)), Vector((0, 1, 0))


def lerp(a, b, t):
    return a + (b - a) * t


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ building blocks


class Part:
    """One object being built: a bmesh, its material slots, a colour per vertex."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.slots = []
        self.col = {}

    def slot(self, m):
        if m not in self.slots:
            self.slots.append(m)
        return self.slots.index(m)

    def vert(self, p, c=(1, 1, 1)):
        v = self.bm.verts.new(p)
        self.col[v] = c
        return v

    def face(self, vs, m):
        f = self.bm.faces.new(vs)
        f.material_index = self.slot(m)
        f.smooth = True
        return f

    def loft(self, centers, halves, u, v, p, sides, m, cap0=True, cap1=True, color=None, mats=None, flat_caps=False):
        """Superellipse sections (|x/hx|^p + |y/hy|^p = 1) through `centers`, in the planes of u (x) and v (y).
        `mats[r]`, if given, is the material between ring r and r+1. `color(point, r)` gives a vertex colour."""
        e = 2.0 / p
        rings = []
        for r, (c, h) in enumerate(zip(centers, halves)):
            ring = []
            for i in range(sides):
                a = 2 * math.pi * i / sides + math.pi / sides
                ca, sa = math.cos(a), math.sin(a)
                x = math.copysign(abs(ca) ** e, ca) * max(h[0], 1e-5)
                y = math.copysign(abs(sa) ** e, sa) * max(h[1], 1e-5)
                q = c + u * x + v * y
                ring.append(self.vert(q, color(q, r) if color else (1, 1, 1)))
            rings.append(ring)
        for r in range(len(rings) - 1):
            mm = mats[r] if mats else m
            for i in range(sides):
                j = (i + 1) % sides
                self.face([rings[r][i], rings[r][j], rings[r + 1][j], rings[r + 1][i]], mm)
        if cap0:
            f = self.face(list(reversed(rings[0])), m)
            f.smooth = not flat_caps
        if cap1:
            f = self.face(rings[-1], mats[-1] if mats else m)
            f.smooth = not flat_caps
        return rings

    def lathe(self, base, axis, u, profile, sides, m, cap0=False, cap1=True, color=None):
        """A turned shape: profile is [(radius, distance along axis)]."""
        v = axis.cross(u).normalized()
        cs = [base + axis * d for _, d in profile]
        hs = [(r, r) for r, _ in profile]
        return self.loft(cs, hs, u, v, 2.0, sides, m, cap0, cap1, color)

    def box(self, centre, size, m, color=(1, 1, 1), bevel=0.0, segs=2):
        """A box (Blender axes), optionally with its edges bevelled into the mesh."""
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        vs = [self.vert(centre + Vector((sx * hx, sy * hy, sz * hz)), color) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        fs = [self.face([vs[i] for i in q], m) for q in idx]
        if bevel > 0:
            edges = list({e for f in fs for e in f.edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=segs, profile=0.5, affect="EDGES")
            for v in self.bm.verts:
                if v not in self.col:
                    self.col[v] = color
        return fs

    def finish(self, materials, subdiv=0, weighted=True, autosmooth=None, uv_cyl=False):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-6)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        if uv_cyl:
            # turned stone: wrapped round the upright axis (one seam, at the back) so the stone's texture runs on without the
            # box projection's lines across the ball
            uvl = self.bm.loops.layers.uv.new("UVMap")
            for f in self.bm.faces:
                us = []
                for lp in f.loops:
                    co = lp.vert.co
                    a = math.atan2(co.y, co.x)
                    us.append(a / (2 * math.pi))
                lo = min(us)
                for lp, u in zip(f.loops, us):
                    if max(us) - lo > 0.5 and u < 0:
                        u += 1.0
                    # (one repeat of the stone round and up the whole cap: the scan has panel joints in it, and smaller
                    # repeats laid them across the ball as lines)
                    lp[uvl].uv = (u * 0.85, lp.vert.co.z / 0.38)
        me = bpy.data.meshes.new(self.name)
        # the colours, by vertex (bmesh keeps the dict's keys through remove_doubles for the kept verts)
        cl = self.bm.loops.layers.color.new("Col")
        for f in self.bm.faces:
            for lp in f.loops:
                c = self.col.get(lp.vert, (1, 1, 1))
                lp[cl] = (c[0], c[1], c[2], 1.0)
        self.bm.to_mesh(me)
        self.bm.free()
        for s in self.slots:
            me.materials.append(materials[s])
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        bpy.context.view_layer.objects.active = ob
        ob.select_set(True)
        # UVs: a box projection in world units (the game's textures repeat about every 12 cm)
        if not uv_cyl:
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.cube_project(cube_size=0.12, correct_aspect=False, scale_to_bounds=False)
            bpy.ops.object.mode_set(mode="OBJECT")
        ob.select_set(False)
        if subdiv:
            sm = ob.modifiers.new("subd", "SUBSURF")
            sm.levels = subdiv
            sm.render_levels = subdiv
        if hasattr(me, "use_auto_smooth"):
            me.use_auto_smooth = True
            me.auto_smooth_angle = math.radians(50)
        elif hasattr(me, "set_sharp_from_angle"):   # (Blender 4.1+: the auto smooth is gone; the same edges marked sharp)
            me.set_sharp_from_angle(angle=math.radians(50))
        if weighted:
            wn = ob.modifiers.new("wn", "WEIGHTED_NORMAL")
            wn.keep_sharp = True
        me.attributes.active_color = me.attributes.get("Col")
        return ob


_mats = {}


def mat(name, color):
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1.0)
    _mats[name] = m
    return m


def materials():
    return {
        "hickory": mat("hickory", (0.55, 0.38, 0.22)), "forged": mat("forged", (0.25, 0.24, 0.24)),
        "honed": mat("honed", (0.7, 0.7, 0.72)), "black": mat("black", (0.04, 0.04, 0.04)),
        "leather": mat("leather", (0.35, 0.22, 0.12)), "chrome": mat("chrome", (0.7, 0.7, 0.7)),
        "brass": mat("brass", (0.7, 0.55, 0.25)), "radio": mat("radio", (0.12, 0.13, 0.11)),
        "rubber": mat("rubber", (0.03, 0.03, 0.03)), "grille": mat("grille", (0.08, 0.08, 0.08)),
        "label": mat("label", (0.8, 0.78, 0.6)), "led": mat("led", (0.3, 0.03, 0.02)),
        "stone": mat("stone", (0.8, 0.8, 0.76)), "break": mat("break", (0.9, 0.88, 0.83)),
    }


def wear(base, amount):
    """A colour darkened toward grime by amount (0..1)."""
    k = 1.0 - 0.55 * max(0.0, min(1.0, amount))
    return (base[0] * k, base[1] * k, base[2] * k)


def hsh(p, s=1.0):
    """A cheap smooth-ish variation from a point."""
    return 0.5 + 0.5 * math.sin(p.x * 91.0 * s + p.y * 57.0 * s + p.z * 133.0 * s) * math.cos(p.y * 41.0 * s - p.z * 77.0 * s)


# ------------------------------------------------------------------ the axe


def axe(M):
    p = Part("Axe")
    # the handle: oval (thin across X, deep toward the bit), a belly, a swelling knob at the butt, sweeping a touch to the bit
    sec = [(-0.115, 0.009, 0.013, 0.004), (-0.112, 0.0135, 0.02, 0.004), (-0.1, 0.0155, 0.024, 0.003), (-0.082, 0.0135, 0.019, 0.001),
           (-0.05, 0.0128, 0.0175, 0.0), (0.0, 0.0125, 0.0171, -0.001), (0.05, 0.0122, 0.0168, -0.002), (0.12, 0.0117, 0.0163, -0.003),
           (0.2, 0.0112, 0.0158, -0.004), (0.27, 0.0108, 0.0158, -0.0037), (0.33, 0.0106, 0.0162, -0.003), (0.40, 0.0108, 0.0175, -0.001),
           (0.43, 0.0112, 0.0188, 0.0), (0.505, 0.0108, 0.0182, 0.0)]
    cs = [g(0, y, z) for y, _, _, z in sec]
    hs = [(hx, hz) for _, hx, hz, _ in sec]

    def handle_col(q, r):
        y = q.z   # (Blender z = game y)
        grip = math.exp(-((y - 0.0) / 0.11) ** 2)   # oiled dark where the hand has been
        grain = 0.9 + 0.1 * math.sin(q.y * 900 + q.x * 300)
        return wear((grain, grain * 0.97, grain * 0.93), 0.55 * grip + 0.15 * hsh(q, 0.3))
    p.loft(cs, hs, RIGHT, BACK, 2.4, 20, "hickory", True, True, handle_col)
    # the wedge's kerf across the top of the handle, through the head's eye
    p.box(g(0, 0.5056, 0), (0.0035, 0.03, 0.0014), "black")
    # the head: sections from the poll (+Z) to the bit (-Z), rounded rectangles across X (thickness) and Y (height)
    hy = 0.468
    S = [(0.05, 0.0125, 0.028, 0), (0.047, 0.0148, 0.032, 0), (0.03, 0.016, 0.036, 0), (0.0, 0.0168, 0.0375, 0),
         (-0.025, 0.014, 0.037, -0.001), (-0.05, 0.0085, 0.041, -0.004), (-0.075, 0.0048, 0.05, -0.008), (-0.098, 0.0026, 0.058, -0.011),
         (-0.106, 0.0011, 0.0598, -0.0115), (-0.1115, 0.00025, 0.0575, -0.012)]
    hc = [g(0, hy + dy, z) for z, _, _, dy in S]
    hh = [(t, h) for _, t, h, _ in S]
    mats = ["forged"] * (len(S) - 3) + ["honed", "honed"]

    def head_col(q, r):
        z = -q.y   # game z
        scale = 0.35 + 0.4 * hsh(q, 0.8)          # forge scale, blotchy
        return wear((1, 1, 1), scale if z > -0.09 else 0.05)
    p.loft(hc, hh, RIGHT, UP, 5.0, 28, "forged", True, True, head_col, mats)
    return p.finish(M)


# ------------------------------------------------------------------ the hammer (a sledge)


def hammer(M):
    """(2026-10-29, the owner's friend: "the hammer looks like a mini hammer") A ten-pound sledge, weathered: a long hickory
    handle gone grey and grimy, the fist choked up under the head and the rest of it hanging below; a square-faced
    double head, the struck faces mushroomed at their lips, scale and rust blotched over it; a steel wedge in the eye."""
    p = Part("Hammer")
    # the handle: the fist at y 0 just under the head, its butt 0.6 m below (a knob), oval, a little fatter toward the butt
    sec = [(-0.615, 0.011, 0.015, 0.0), (-0.61, 0.0175, 0.023, 0.0), (-0.595, 0.0195, 0.026, 0.0), (-0.57, 0.0178, 0.0235, 0.0),
           (-0.45, 0.0168, 0.0225, 0.0), (-0.3, 0.0162, 0.0218, 0.0), (-0.12, 0.0156, 0.021, 0.0), (0.05, 0.0152, 0.0205, 0.0),
           (0.17, 0.0158, 0.0215, 0.0), (0.215, 0.0168, 0.023, 0.0), (0.297, 0.0168, 0.023, 0.0)]

    def handle_col(q, r):
        y = q.z
        # grey, weathered hickory: darker where both hands go (choked up under the head, and at the knob for the swing),
        # streaks of old grime along the grain, a pale split here and there
        grip = math.exp(-((y - 0.0) / 0.09) ** 2) + 0.8 * math.exp(-((y + 0.56) / 0.07) ** 2)
        grain = 0.88 + 0.12 * math.sin(q.y * 700 + q.x * 210 + y * 30)
        streak = 0.25 * max(0.0, math.sin(y * 47.0 + q.x * 400) - 0.6)
        base = (0.82 * grain, 0.8 * grain, 0.76 * grain)
        return wear(base, 0.55 * grip + 0.25 * hsh(q, 0.25) + streak)
    p.loft([g(0, y, z) for y, _, _, z in sec], [(hx, hz) for _, hx, hz, _ in sec], RIGHT, BACK, 2.6, 20, "hickory", True, True, handle_col)
    hy = 0.255

    def steel(q, r):
        # forge scale gone to rust in blotches, the edges worn brighter
        # (patches, not a coat: the steel has to read dark and hard against the wood)
        rust = smooth((hsh(q, 0.45) - 0.66) * 5.0) * 0.55
        k = 0.35 + 0.35 * hsh(q, 1.3)
        c = wear((0.9, 0.9, 0.92), k)
        return (lerp(c[0], 0.55, rust), lerp(c[1], 0.32, rust), lerp(c[2], 0.2, rust))
    # the head: a long rounded-square block across the handle's top, the faces at both ends (-Z ahead, +Z behind), each
    # face's lip mushroomed out by years of blows, a slight waist at the eye
    zs = [-0.106, -0.1, -0.09, -0.06, -0.025, 0.0, 0.025, 0.06, 0.09, 0.1, 0.106]
    ws = [(0.0335, 0.0335), (0.0405, 0.0405), (0.0382, 0.0382), (0.037, 0.037), (0.0378, 0.0385), (0.0395, 0.04),
          (0.0378, 0.0385), (0.037, 0.037), (0.0382, 0.0382), (0.0405, 0.0405), (0.0335, 0.0335)]
    rings = p.loft([g(0, hy, z) for z in zs], ws, RIGHT, UP, 6.0, 28, "forged", False, False, steel)
    # the struck faces, flat and honed bright where they meet the work (a fan to the middle, a little domed)
    for ring, z in ((rings[0], -0.106), (rings[-1], 0.106)):
        cv = p.vert(g(0, hy, z + (0.0025 if z > 0 else -0.0025)), (0.85, 0.82, 0.8))
        n = len(ring)
        for i in range(n):
            a, b = ring[i], ring[(i + 1) % n]
            p.face([a, b, cv] if z > 0 else [b, a, cv], "honed")
    # the wedge driven into the eye's top: a steel slip across the handle's end grain
    p.box(g(0, hy + 0.0405, 0), (0.007, 0.034, 0.003), "black")
    p.box(g(0, hy + 0.0405, 0), (0.026, 0.008, 0.0024), "forged", (0.6, 0.55, 0.5))
    return p.finish(M)


# ------------------------------------------------------------------ the knife


def knife(M):
    p = Part("Knife")
    # stacked leather washers between a butt cap and the brass guard: bands in the colour
    sec = [(-0.055, 0.0115, 0.0145), (-0.042, 0.012, 0.0152), (-0.03, 0.0122, 0.0158), (-0.015, 0.0121, 0.0156), (0.0, 0.0118, 0.0152),
           (0.018, 0.0115, 0.0149), (0.035, 0.0112, 0.0146), (0.048, 0.011, 0.0142), (0.058, 0.0108, 0.0138)]

    def washers(q, r):
        band = 0.82 + 0.18 * (1 if int((q.z + 0.1) / 0.0045) % 2 else 0)
        return wear((band, band * 0.96, band * 0.9), 0.25 * hsh(q, 0.5))
    # (ring every washer edge for the bands to read)
    ys = [-0.055 + i * 0.00225 for i in range(int(0.113 / 0.00225) + 1)]
    def interp(y):
        for a, b in zip(sec, sec[1:]):
            if a[0] <= y <= b[0]:
                t = (y - a[0]) / (b[0] - a[0])
                return lerp(a[1], b[1], t), lerp(a[2], b[2], t)
        return sec[-1][1], sec[-1][2]
    p.loft([g(0, y, 0) for y in ys], [interp(y) for y in ys], RIGHT, BACK, 2.4, 20, "leather", False, False, washers)
    # the butt cap
    p.loft([g(0, -0.069, 0), g(0, -0.067, 0), g(0, -0.062, 0), g(0, -0.055, 0)], [(0.006, 0.008), (0.0095, 0.012), (0.0115, 0.0145), (0.0118, 0.0148)],
           RIGHT, BACK, 2.6, 20, "chrome", True, False)
    # the guard: a brass oval plate, rounded
    p.loft([g(0, y, -0.001) for y in (0.058, 0.0585, 0.062, 0.066, 0.067)], [(0.0075, 0.019), (0.0085, 0.021), (0.0085, 0.021), (0.0085, 0.021), (0.0072, 0.019)],
           RIGHT, BACK, 4.0, 24, "brass", True, True)
    # the blade: a drop point, a 4 mm spine ground to the edge (spine +Z, edge -Z), 16 cm
    n = 20
    spine, edge, grind, th = [], [], [], []
    for i in range(n + 1):
        t = i / n
        y = 0.067 + t * 0.16
        sz = 0.0125 if t < 0.72 else lerp(0.0125, -0.001, ((t - 0.72) / 0.28) ** 1.3)
        ez = (-0.0175 + t * 0.005) if t < 0.65 else lerp(-0.01425, -0.001, ((t - 0.65) / 0.35) ** 0.7)
        if i == n:
            sz = ez = -0.001
        spine.append((y, sz)); edge.append((y, ez)); grind.append((y, lerp(ez, sz, 0.55)))
        th.append(0.0021 * (1 - t * t))
    vs = []
    for i in range(n + 1):
        y = spine[i][0]
        row = {
            "sL": p.vert(g(-th[i], y, spine[i][1])), "sR": p.vert(g(th[i], y, spine[i][1])),
            "gL": p.vert(g(-th[i], y, grind[i][1])), "gR": p.vert(g(th[i], y, grind[i][1])),
            "e": p.vert(g(0, y, edge[i][1])),
        }
        vs.append(row)
    for i in range(n):
        a, b = vs[i], vs[i + 1]
        p.face([a["sL"], b["sL"], b["gL"], a["gL"]], "forged")
        p.face([a["gR"], b["gR"], b["sR"], a["sR"]], "forged")
        p.face([a["gL"], b["gL"], b["e"], a["e"]], "honed")
        p.face([a["e"], b["e"], b["gR"], a["gR"]], "honed")
        p.face([a["sR"], b["sR"], b["sL"], a["sL"]], "forged")
    a = vs[0]
    p.face([a["sL"], a["gL"], a["e"], a["gR"], a["sR"]], "forged")
    return p.finish(M)


# ------------------------------------------------------------------ the lighter (its lid on its own node, hinged)


def lighter(M):
    case = Part("Lighter")
    brass = lambda q, r: wear((1, 1, 1), 0.3 * hsh(q, 0.7))
    # (Blender axes: X across, Y = game -Z, Z = game up) the case 38 x 13 x 40 mm, the hinge's barrel on the right
    case.box(g(0, 0.0, 0), (0.038, 0.013, 0.040), "brass", (0.96, 0.95, 0.92), bevel=0.0028, segs=3)
    case.box(g(0, 0.0206, 0), (0.0384, 0.0135, 0.0012), "black")
    case.lathe(g(0.0195, 0.012, 0), UP, RIGHT, [(0.0024, 0.0), (0.0024, 0.018)], 14, "brass", True, True)
    case_ob = case.finish(M)
    lid = Part("Lid")
    lid.box(Vector((0, 0, 0)), (0.038, 0.013, 0.0168), "brass", (0.9, 0.88, 0.84), bevel=0.0026, segs=3)
    # the lid's origin is the hinge (the case's right edge, at the seam), so turning it opens it
    hinge = g(0.0195, 0.021, 0)
    for v in lid.bm.verts:
        v.co += g(0, 0.0295, 0) - hinge
    lid_ob = lid.finish(M)
    lid_ob.location = hinge
    lid_ob.parent = case_ob
    return case_ob


# ------------------------------------------------------------------ the walkie-talkie (its LED its own node)


def walkie(M):
    W, D, H = 0.06, 0.034, 0.152
    y0 = -0.06
    y1 = y0 + H
    p = Part("Walkie")
    plastic = lambda c: (lambda q, r: wear(c, 0.2 * hsh(q, 0.6)))
    # the battery pack below, the case above it, a seam between
    p.box(g(0, y0 + 0.024, -0.001), (W - 0.002, D - 0.002, 0.048), "radio", (0.82, 0.86, 0.8), bevel=0.004, segs=3)
    p.box(g(0, y0 + 0.049 + (H - 0.049) * 0.5, 0), (W, D, H - 0.049), "radio", (1, 1, 1), bevel=0.005, segs=3)
    # the speaker grille set into a raised rim, on the face (+Z: back to the eye)
    gy = y0 + 0.085
    zf = D * 0.5 + 0.0006
    gv = [p.vert(g(x, y, zf)) for x, y in ((-0.022, gy - 0.028), (0.022, gy - 0.028), (0.022, gy + 0.028), (-0.022, gy + 0.028))]
    gf = p.face(gv, "grille")
    gf.smooth = False
    for s in (-1, 1):
        p.box(g(s * 0.024, gy, D * 0.5 + 0.001), (0.003, 0.002, 0.062), "radio", (0.7, 0.72, 0.68), bevel=0.0007, segs=1)
        p.box(g(0, gy + s * 0.0305, D * 0.5 + 0.001), (0.051, 0.002, 0.003), "radio", (0.7, 0.72, 0.68), bevel=0.0007, segs=1)
    # the channel sticker under the grille
    lv = [p.vert(g(x, y, D * 0.5 + 0.0009)) for x, y in ((-0.016, y0 + 0.033), (0.02, y0 + 0.033), (0.02, y0 + 0.049), (-0.016, y0 + 0.049))]
    lf = p.face(lv, "label")
    lf.smooth = False
    # the top: the antenna (ribbed rubber, tapering), the volume and channel knobs
    ant = [(0.0085, 0.0), (0.0085, 0.012), (0.0072, 0.016), (0.006, 0.05), (0.0048, 0.1), (0.0044, 0.118), (0.0032, 0.122), (0.0005, 0.123)]
    # (the ribs: a bump every 10.5 mm along the lower part)
    full = []
    for i in range(len(ant) - 1):
        r0, d0 = ant[i]; r1, d1 = ant[i + 1]
        steps = max(1, int((d1 - d0) / 0.0026))
        for k in range(steps):
            t = k / steps
            d = lerp(d0, d1, t)
            rib = 0.0007 if 0.02 < d < 0.105 and ((d - 0.02) % 0.0105) < 0.003 else 0.0
            full.append((lerp(r0, r1, t) + rib, d))
    full.append(ant[-1])
    p.lathe(g(0.017, y1, -0.002), UP, RIGHT, full, 16, "rubber", False, True)
    for x, r, h in ((-0.016, 0.0075, 0.013), (-0.0005, 0.0058, 0.009)):
        # knurled: the radius rippling round (a ring of fine ribs)
        prof = [(r, 0.0), (r, h), (r - 0.0015, h + 0.0012), (0.0003, h + 0.0012)]
        rings = p.lathe(g(x, y1, -0.002), UP, RIGHT, prof, 32, "rubber", False, True)
        for ring in rings[:2]:
            for i, v in enumerate(ring):
                if i % 2 == 0:
                    off = (v.co - g(x, v.co.z, -0.002))
                    off.z = 0
                    if off.length > 0:
                        v.co += off.normalized() * 0.0007
    # the push-to-talk bar on the right side, the belt clip on the back
    p.box(g(W * 0.5 + 0.002, y0 + 0.095, 0.0), (0.006, 0.04, 0.018), "rubber", bevel=0.002, segs=2)
    p.box(g(0, y0 + 0.085, -D * 0.5 - 0.0035), (0.03, 0.004, 0.075), "radio", (0.75, 0.78, 0.72), bevel=0.0015, segs=2)
    body = p.finish(M)
    led = Part("Led")
    led.box(g(0.006, y1 + 0.0012, 0.01), (0.004, 0.004, 0.003), "led", bevel=0.0008, segs=1)
    led_ob = led.finish(M, weighted=False)
    led_ob.parent = body
    return body


# ------------------------------------------------------------------ the newel post's cap (cap space)


SIDES = 12
SPIGOT_R, SPIGOT_TOP, BREAK_C = 0.07, 0.065, 0.04
PLINTH_W, PLINTH_TOP, STEP_W, STEP_TOP = 0.19, 0.1, 0.16, 0.109
BALL_R, BALL_Y = 0.1, 0.223
STONE, UNDER, LICHEN, FRESH = (0.93, 0.92, 0.88), (0.6, 0.6, 0.57), (0.7, 0.74, 0.58), (0.92, 0.9, 0.85)


def break_y(a):
    return 0.036 + 0.013 * math.sin(3 * a + 1.3) + 0.007 * math.sin(7 * a + 0.4) + 0.004 * math.sin(11 * a + 2.1)


def break_pt(i):
    a = 2 * math.pi * i / SIDES
    return math.cos(a) * SPIGOT_R, break_y(a), math.sin(a) * SPIGOT_R


def mix(a, b, t):
    return tuple(lerp(x, y, t) for x, y in zip(a, b))


def stone_col(q):
    """Pale above, damp and dark under, lichen on the top (the stairs' own cap colours)."""
    y = q.z   # game y
    up = max(-1.0, min(1.0, (y - BALL_Y) / BALL_R))
    c = mix(UNDER, STONE, smooth((up + 0.9) / 1.0))
    ang = math.atan2(-q.y, q.x)
    lichen = max(0.0, up - 0.35) * (0.6 + 0.4 * math.sin(ang * 3 + 0.7))
    c = mix(c, LICHEN, min(0.55, lichen))
    pit = 0.94 + 0.06 * hsh(q, 1.4)
    return (c[0] * pit, c[1] * pit, c[2] * pit)


def newel_cap(M):
    p = Part("NewelCap")
    # the spigot from the break up into the plinth: twelve sides, the break the stump's own twelve points
    lower = []
    upper = []
    for i in range(SIDES):
        x, y, z = break_pt(i)
        lower.append(p.vert(g(x, y, z), mix(UNDER, STONE, 0.1)))
        upper.append(p.vert(g(x, SPIGOT_TOP + 0.002, z), UNDER))
    for i in range(SIDES):
        j = (i + 1) % SIDES
        p.face([lower[i], lower[j], upper[j], upper[i]], "stone")
    # the break: pale fresh stone, a fan to the centre, facing down
    c = p.vert(g(0, BREAK_C, 0), FRESH)
    brk = []
    for i in range(SIDES):
        x, y, z = break_pt(i)
        brk.append(p.vert(g(x, y, z), FRESH))
    for i in range(SIDES):
        j = (i + 1) % SIDES
        f = p.face([c, brk[j], brk[i]], "break")
        f.smooth = False
    # the square plinth and the small step above it, edges worn round
    p.box(g(0, (SPIGOT_TOP + PLINTH_TOP) * 0.5, 0), (PLINTH_W, PLINTH_W, PLINTH_TOP - SPIGOT_TOP), "stone", mix(UNDER, STONE, 0.75), bevel=0.006, segs=3)
    p.box(g(0, (PLINTH_TOP + STEP_TOP) * 0.5 + 0.0005, 0), (STEP_W, STEP_W, STEP_TOP - PLINTH_TOP + 0.001), "stone", mix(UNDER, STONE, 0.85), bevel=0.003, segs=2)
    # the neck, a ring, and the ball: turned smooth (48 round)
    prof = [(0.052, STEP_TOP), (0.049, 0.112), (0.042, 0.118), (0.0375, 0.123), (0.036, 0.126), (0.0395, 0.1295), (0.047, 0.133), (0.0475, 0.1355), (0.047, 0.138), (0.044, 0.1395)]
    for deg in range(-62, 89, 6):
        t = math.radians(deg)
        prof.append((BALL_R * math.cos(t), BALL_Y + BALL_R * math.sin(t)))
    prof.append((0.0012, BALL_Y + BALL_R))
    rings = p.lathe(g(0, 0, 0), UP, RIGHT, [(r, d) for r, d in prof], 48, "stone", False, True, lambda q, r: stone_col(q))
    return p.finish(M, uv_cyl=True)


# ------------------------------------------------------------------ export


BUILD = {"axe": axe, "hammer": hammer, "knife": knife, "lighter": lighter, "walkie": walkie, "newel_cap": newel_cap}


def export(root, name):
    bpy.ops.object.select_all(action="DESELECT")
    stack = [root]
    while stack:
        o = stack.pop()
        o.select_set(True)
        stack.extend(o.children)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(OUT, name + ".glb")
    args = dict(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                export_texcoords=True, export_normals=True, export_materials="EXPORT", export_image_format="NONE")
    try:
        bpy.ops.export_scene.gltf(**args, export_colors=True)
    except TypeError:
        # (Blender 4.2+ renamed it: the active colour layer, the wear, by name)
        bpy.ops.export_scene.gltf(**args, export_vertex_color="ACTIVE")
    tris = 0
    for o in [root] + list(root.children_recursive):
        dg = bpy.context.evaluated_depsgraph_get()
        me = o.evaluated_get(dg).to_mesh()
        tris += sum(len(pl.vertices) - 2 for pl in me.polygons)
        o.evaluated_get(dg).to_mesh_clear()
    print(f"[held_tools] {name}: {tris} triangles -> {path}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(BUILD)
    for name in names:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        _mats.clear()
        M = materials()
        root = BUILD[name](M)
        export(root, name)


main()
