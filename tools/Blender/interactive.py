"""The interactive close-ups, modelled in Blender (2026-10-15, the owner: every model made in Blender): the boss pit's valves,
the station's cryptex, the library's puzzle box, the round room's knife switches and caged bulbs, the bunker's caged lamps,
its CRT sets, the CRT room's shelving and console, and the bunker door's keypad. Run headless:

    blender -b --python tools/Blender/interactive.py [-- name ...]

Each piece goes to assets/models/furniture/<name>.glb with its cavity map, through the furniture library's pipeline
(furniture.py: materials by role, the game swaps its own in; UV 1 box-projected at a metre, UV 2 unique for the baked
cavity). Every moving part is its own piece with its origin on its pivot, so the game keeps turning the same nodes.

The pieces are written in the GAME's coordinates (Y up, the front toward -Z or as each builder had it) through G(), which
swaps Y and Z; the export mirrors Blender's Y (furniture.py), so what comes out in the game is exactly what's written
here. A colour attribute ("Col") carries each part's shade (a bezel's darker lip, a cast box's grime): the game multiplies
it into the role's material and its tint.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furniture as F
from kit import mat, add_mod, apply_mods, obj_from_bmesh

V = Vector
TAU = math.tau

ROLE_COLOURS = {
    "steel": (0.25, 0.27, 0.27), "yellow": (0.85, 0.68, 0.08), "red": (0.6, 0.06, 0.05), "brass": (0.62, 0.46, 0.2),
    "bronze": (0.46, 0.32, 0.16), "lip": (0.4, 0.3, 0.16), "bamboo": (0.75, 0.6, 0.35), "bakelite": (0.11, 0.085, 0.065),
    "copper": (0.72, 0.42, 0.25), "black": (0.05, 0.05, 0.055), "metal": (0.5, 0.5, 0.48), "enamel": (0.62, 0.6, 0.55),
    "glass": (1.0, 0.95, 0.8), "wire": (0.3, 0.3, 0.28), "plastic": (0.42, 0.4, 0.34), "rubber": (0.05, 0.05, 0.05),
    "paint": (0.72, 0.75, 0.73), "inset": (0.07, 0.075, 0.07), "keys": (0.4, 0.38, 0.33), "key": (0.7, 0.7, 0.68),
}


def role(name):
    return mat(name, ROLE_COLOURS.get(name, (0.5, 0.5, 0.5)), 0.6)


def G(p):
    """A point in the game's space -> the Blender space this file builds in (the export mirrors it back)."""
    return V((p[0], p[2], p[1]))


# ------------------------------------------------------------------ building in game space

class Piece:
    """A bmesh being filled with parts, each with a material slot and a shade (the "Col" attribute)."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.float_color.new("Col")
        self.slots = []
        self.cur = 0
        self.shade = (1.0, 1.0, 1.0)

    def role(self, r, shade=(1.0, 1.0, 1.0)):
        if r not in self.slots:
            self.slots.append(r)
        self.cur = self.slots.index(r)
        self.shade = shade
        return self

    def face(self, verts):
        f = self.bm.faces.new(verts)
        f.material_index = self.cur
        for l in f.loops:
            l[self.col] = (*self.shade, 1.0)
        f.smooth = True
        return f

    # (every point goes through G: written in game space)
    def vert(self, p):
        return self.bm.verts.new(G(p))

    def ring(self, centre, axis, r, segs, phase=0.0, rx=None):
        axis = V(axis).normalized()
        u = axis.orthogonal().normalized()
        w = axis.cross(u).normalized()
        out = []
        for i in range(segs):
            a = phase + i / segs * TAU
            rr = r if rx is None else rx
            out.append(self.vert(V(centre) + u * math.cos(a) * rr + w * math.sin(a) * r))
        return out

    def bridge(self, r0, r1):
        n = len(r0)
        for i in range(n):
            self.face((r0[i], r0[(i + 1) % n], r1[(i + 1) % n], r1[i]))

    def cap(self, ring, centre, flip=False):
        c = self.vert(centre)
        n = len(ring)
        for i in range(n):
            self.face((ring[(i + 1) % n], ring[i], c) if not flip else (ring[i], ring[(i + 1) % n], c))

    def cylinder(self, a, b, r0, r1=None, segs=24, caps=True, phase=0.0):
        r1 = r0 if r1 is None else r1
        a, b = V(a), V(b)
        ax = b - a
        ra = self.ring(a, ax, r0, segs, phase)
        rb = self.ring(b, ax, r1, segs, phase)
        self.bridge(ra, rb)
        if caps:
            self.cap(ra, a)
            self.cap(rb, b)

    def lathe(self, origin, axis, profile, segs=32, phase=0.0, close=True):
        """profile: [(radius, h), ...] along axis from origin."""
        origin, axis = V(origin), V(axis).normalized()
        rings = [self.ring(origin + axis * h, axis, max(r, 1e-5), segs, phase) for (r, h) in profile]
        for k in range(len(rings) - 1):
            self.bridge(rings[k], rings[k + 1])
        if close:
            if profile[0][0] > 1e-4:
                self.cap(rings[0], origin + axis * profile[0][1])
            if profile[-1][0] > 1e-4:
                self.cap(rings[-1], origin + axis * profile[-1][1])

    def torus(self, centre, axis, R, r, segs=48, tube=12):
        centre, axis = V(centre), V(axis).normalized()
        u = axis.orthogonal().normalized()
        w = axis.cross(u).normalized()
        rings = []
        for i in range(segs):
            a = i / segs * TAU
            d = u * math.cos(a) + w * math.sin(a)
            c = centre + d * R
            rings.append([self.vert(c + d * math.cos(b) * r + axis * math.sin(b) * r) for b in (j / tube * TAU for j in range(tube))])
        for i in range(segs):
            r0, r1 = rings[i], rings[(i + 1) % segs]
            for j in range(tube):
                self.face((r0[j], r1[j], r1[(j + 1) % tube], r0[(j + 1) % tube]))

    def box(self, c, s, basis=None, bevel=0.0):
        """An axis box (in basis, a 3x3 Matrix in game space) of size s at c; bevel rounds its edges (applied later)."""
        c = V(c)
        bm = Matrix.Identity(3) if basis is None else basis
        hx, hy, hz = s[0] * 0.5, s[1] * 0.5, s[2] * 0.5
        P = {}
        for ix in (-1, 1):
            for iy in (-1, 1):
                for iz in (-1, 1):
                    P[(ix, iy, iz)] = self.vert(c + bm @ V((ix * hx, iy * hy, iz * hz)))
        q = P
        for f in (((-1, -1, -1), (-1, 1, -1), (1, 1, -1), (1, -1, -1)), ((-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)),
                  ((-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)), ((-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1)),
                  ((-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)), ((1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1))):
            self.face(tuple(q[k] for k in f)).smooth = False

    def frustum(self, fc, fs, bc, bs):
        fc, bc = V(fc), V(bc)
        F_ = [self.vert(fc + V((sx * fs[0] * 0.5, sy * fs[1] * 0.5, 0))) for (sx, sy) in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        B_ = [self.vert(bc + V((sx * bs[0] * 0.5, sy * bs[1] * 0.5, 0))) for (sx, sy) in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        for i in range(4):
            self.face((F_[i], B_[i], B_[(i + 1) % 4], F_[(i + 1) % 4])).smooth = False
        self.face(tuple(reversed(B_))).smooth = False

    def obj(self, bevel=0.0, segs=2, smooth_angle=40.0, subsurf=0):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-6)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        mats = [role(r) for r in self.slots]
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        for m in mats:
            ob.data.materials.append(m)
        if bevel > 0:
            add_mod(ob, "BEVEL", width=bevel, segments=segs, limit_method="ANGLE", angle_limit=math.radians(50), harden_normals=False)
        if subsurf:
            add_mod(ob, "SUBSURF", levels=subsurf, render_levels=subsurf)
        apply_mods(ob)
        me.use_auto_smooth = True
        me.auto_smooth_angle = math.radians(smooth_angle)
        for p in me.polygons:
            p.use_smooth = True
        return ob


def export(ob, name, cavity=512, budget=None):
    """The furniture library's export (the mirror, the triangulation, the two UV sets, the cavity bake), keeping the
    piece's hard edges (auto smooth) and its shade attribute."""
    tris0 = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if budget and tris0 > budget:
        add_mod(ob, "DECIMATE", ratio=budget / tris0, use_collapse_triangulate=True)
        apply_mods(ob)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    ob.scale = (1, -1, 1)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")
    add_mod(ob, "TRIANGULATE")
    apply_mods(ob)
    F.uv_layers(ob)
    F.bake_cavity(ob, name, cavity)
    me = ob.data
    if "Col" in me.color_attributes:
        me.color_attributes.active_color = me.color_attributes["Col"]
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(F.OUT, name + ".glb"), export_format="GLB", use_selection=True, export_yup=True,
                              export_apply=True, export_texcoords=True, export_normals=True, export_tangents=True,
                              export_colors=True, export_materials="EXPORT", export_image_format="NONE")
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[interactive] {name}: {tris} triangles")
    return tris


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import kit
    kit._mats.clear()


# ------------------------------------------------------------------ Act 18: the valves (faces -Z)

def valve_body():
    p = Piece("valve_body").role("steel")
    p.cylinder((0, -1.25, 0), (0, 0.5, 0), 0.12, segs=32)
    for (y0, y1) in ((-0.25, -0.2), (0.2, 0.25)):
        p.lathe((0, y0, 0), (0, 1, 0), [(0.12, 0), (0.2, 0), (0.205, 0.006), (0.205, y1 - y0 - 0.006), (0.2, y1 - y0), (0.12, y1 - y0)], 40)
    # the cast globe between the flanges, a bonnet out toward the wheel, its hex packing nut
    p.lathe((0, 0, 0), (0, 1, 0), [(0.125, -0.2), (0.17, -0.16), (0.205, -0.1), (0.225, 0.0), (0.205, 0.1), (0.17, 0.16), (0.125, 0.2)], 40)
    p.lathe((0, 0, -0.17), (0, 0, -1), [(0.13, 0), (0.13, 0.03), (0.1, 0.04), (0.075, 0.1), (0.08, 0.11), (0.08, 0.14), (0.05, 0.15), (0.05, 0.19), (0.035, 0.2), (0.0, 0.2)], 36)
    p.lathe((0, 0, -0.32), (0, 0, -1), [(0.062, 0), (0.062, 0.035)], 6, phase=math.pi / 6)
    for b in range(6):
        a = b / 6 * TAU
        for y in (-0.23, 0.23):
            c = V((math.cos(a) * 0.17, y, math.sin(a) * 0.17))
            p.lathe(c - V((0, 0.03, 0)), (0, 1, 0), [(0.02, 0), (0.02, 0.06)], 6)
    p.cylinder((0, 0, -0.2), (0, 0, -0.44), 0.035, segs=20)
    return p.obj(bevel=0.004, segs=2)


def valve_wheel():
    """Its origin on the wheel's centre (0, 0, -0.42 on the valve), lying in the XY plane, facing -Z."""
    p = Piece("valve_wheel").role("yellow")
    R, tube = 0.36, 0.034
    p.torus((0, 0, 0), (0, 0, 1), R, tube, 72, 16)
    for s in range(5):
        a = s / 5 * TAU + 0.3
        d = V((math.cos(a), math.sin(a), 0))
        # a spoke swelling where it meets the rim and the hub, set a little back toward the valve
        p.lathe(d * 0.06, d, [(0.034, 0.0), (0.026, 0.04), (0.022, R * 0.5 - 0.03), (0.025, R - 0.08), (0.032, R - 0.06)], 16)
    p.lathe((0, 0, 0.05), (0, 0, -1), [(0.08, 0), (0.084, 0.01), (0.084, 0.09), (0.08, 0.1)], 40)
    p.role("red")
    p.lathe((0, 0, -0.05), (0, 0, -1), [(0.046, 0), (0.044, 0.025), (0.036, 0.04), (0.0, 0.042)], 32)
    return p.obj(bevel=0.002, segs=2)


def valve_gauge():
    p = Piece("valve_gauge").role("brass")
    p.cylinder((0, 0.5, 0), (0, 0.63, 0), 0.02, segs=16)
    p.lathe((0, 0.78, 0.03), (0, 0, -1), [(0.12, 0), (0.13, 0.01), (0.135, 0.05), (0.14, 0.068), (0.125, 0.078), (0.118, 0.078)], 40, close=False)
    p.cap(p.ring(V((0, 0.78, 0.03)), (0, 0, -1), 0.12, 40), V((0, 0.78, 0.03)), flip=True)
    return p.obj()


# ------------------------------------------------------------------ Act 13: the cryptex (long axis X)

RING_R, RING_W, GAP, RINGS = 0.065, 0.052, 0.006, 6


def cryptex_core():
    p = Piece("cryptex_core").role("bronze")
    half = RINGS * 0.5 * (RING_W + GAP)
    p.cylinder((-half - 0.01, 0, 0), (half + 0.01, 0, 0), RING_R * 0.8, segs=32)
    p.role("brass", (0.9, 0.75, 0.45))
    for s in (-1, 1):
        p.box((0, RING_R + 0.004, s * 0.018), (half * 2 + 0.03, 0.004, 0.004))
    return p.obj(bevel=0.0008)


def cryptex_ring():
    """One ring (centred on its own origin, the long axis X); the game spins it about X and sets the letters round it."""
    p = Piece("cryptex_ring").role("brass")
    w = RING_W * 0.5
    # a turned band: its faces slightly crowned, its edges rolled into raised lips, a groove between letters' rows
    p.lathe((-w, 0, 0), (1, 0, 0), [(RING_R * 0.82, 0), (RING_R + 0.003, 0.0), (RING_R + 0.0035, 0.002), (RING_R + 0.003, 0.0045),
                                     (RING_R, 0.006), (RING_R + 0.0004, w), (RING_R, RING_W - 0.006), (RING_R + 0.003, RING_W - 0.0045),
                                     (RING_R + 0.0035, RING_W - 0.002), (RING_R + 0.003, RING_W), (RING_R * 0.82, RING_W)], 52)
    p.role("lip", (0.72, 0.56, 0.32))
    for s in (-1, 1):
        p.torus((s * (w - 0.0015), 0, 0), (1, 0, 0), RING_R + 0.0028, 0.0012, 52, 6)
    return p.obj()


def cryptex_cap(side):
    """An end cap (its origin at the cap's centre: the game slides the right one off along X)."""
    p = Piece(f"cryptex_cap_{'r' if side > 0 else 'l'}").role("bronze")
    p.lathe((-side * 0.03, 0, 0), (side, 0, 0), [(RING_R * 0.8, 0), (RING_R + 0.012, 0.0), (RING_R + 0.013, 0.006), (RING_R + 0.01, 0.03),
                                                 (RING_R + 0.008, 0.054), (RING_R + 0.004, 0.06), (RING_R * 0.7, 0.06)], 48)
    p.role("brass", (0.85, 0.7, 0.42))
    # the finial: a turned knob with a bead and an acorn
    p.lathe((side * 0.03, 0, 0), (side, 0, 0), [(RING_R * 0.7, 0), (RING_R * 0.62, 0.006), (RING_R * 0.48, 0.01), (RING_R * 0.55, 0.014),
                                               (RING_R * 0.38, 0.022), (RING_R * 0.42, 0.03), (RING_R * 0.2, 0.042), (0.0, 0.046)], 36)
    p.torus((side * 0.012, 0, 0), (1, 0, 0), RING_R + 0.012, 0.005, 48, 8)
    # four rivets round the cap's face
    for i in range(4):
        a = i / 4 * TAU + 0.4
        c = V((side * 0.03, math.cos(a) * RING_R * 0.85, math.sin(a) * RING_R * 0.85))
        p.lathe(c, (side, 0, 0), [(0.004, 0), (0.0035, 0.002), (0.0, 0.003)], 10)
    return p.obj()


# ------------------------------------------------------------------ Act 19: the puzzle box

CELL, WALL, FLOOR, DEPTH, N = 0.07, 0.035, 0.012, 0.09, 4


def bamboo_box(name, size, bevel, segs=3):
    p = Piece(name).role("bamboo")
    p.box((0, 0, 0), size)
    ob = p.obj(bevel=bevel, segs=segs, smooth_angle=30)
    return ob


def puzzle_uv(ob, cube):
    """The bamboo's own texture over each face whole (the game's dissolve shader samples it 0..1), not a metre repeat."""
    F.KEEP_UV.add("bamboo")
    me = ob.data
    while len(me.uv_layers) < 1:
        me.uv_layers.new()
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=cube, scale_to_bounds=False, correct_aspect=False)
    bpy.ops.object.mode_set(mode="OBJECT")


# ------------------------------------------------------------------ Act 20: the knife switches (1:1, facing +Z on the wall)

def knife_base():
    p = Piece("knife_base").role("bakelite")
    p.box((0, 0, 0.02), (0.34, 0.56, 0.04))
    p.role("steel")
    for (x, y) in ((-0.14, 0.25), (0.14, 0.25), (-0.14, -0.25), (0.14, -0.25)):
        p.lathe((x, y, 0.04), (0, 0, 1), [(0.013, 0), (0.012, 0.004), (0.008, 0.007), (0.0, 0.008)], 16)
    for y in (0.19, -0.19):
        for x in (-0.1, 0.1):
            p.role("copper")
            for dx in (-0.014, 0.014):
                p.box((x + dx, y, 0.07), (0.008, 0.07, 0.06))
            p.role("black")
            p.box((x, y, 0.045), (0.04, 0.08, 0.012))
            p.role("steel")
            p.cylinder((x - 0.022, y, 0.045), (x + 0.022, y, 0.045), 0.004, segs=8)
    p.role("black")
    for x in (-0.1, 0.1):
        p.box((x, 0, 0.065), (0.04, 0.06, 0.05))
        p.role("steel")
        p.cylinder((x - 0.026, 0, 0.075), (x + 0.026, 0, 0.075), 0.006, segs=12)
        p.role("black")
    return p.obj(bevel=0.0025, segs=2)


def knife_lever():
    """The lever, its origin on the hinge (0, 0, 0.075 on the plate), lying along +Z (the game turns it about X)."""
    p = Piece("knife_lever").role("black")
    for x in (-0.1, 0.1):
        p.box((x, 0, 0.19), (0.022, 0.032, 0.38))
        p.role("copper")
        p.box((x, 0, 0.01), (0.006, 0.05, 0.07))
        p.role("black")
    p.box((0, 0, 0.2), (0.25, 0.018, 0.075))
    p.role("red")
    p.lathe((-0.098, 0, 0.37), (1, 0, 0), [(0.02, 0), (0.022, 0.01), (0.023, 0.06), (0.021, 0.1), (0.023, 0.14), (0.022, 0.186), (0.02, 0.196)], 24)
    p.role("black")
    for x in (-0.115, 0.115):
        p.lathe((x - 0.0175, 0, 0.37), (1, 0, 0), [(0.026, 0), (0.03, 0.004), (0.03, 0.031), (0.026, 0.035)], 24)
    return p.obj(bevel=0.002, segs=2)


# ------------------------------------------------------------------ the bunker's caged work lamp (hangs along -Y from its mount)

def caged_lamp():
    p = Piece("caged_lamp").role("metal", (0.55, 0.55, 0.52))
    p.lathe((0, 0, 0), (0, -1, 0), [(0.07, 0.0), (0.072, 0.008), (0.06, 0.02), (0.03, 0.028), (0.016, 0.03)], 32)
    p.cylinder((0, -0.028, 0), (0, -0.115, 0), 0.014, segs=16)
    p.lathe((0, 0, 0), (0, -1, 0), [(0.016, 0.105), (0.03, 0.112), (0.034, 0.13), (0.036, 0.152), (0.05, 0.158)], 32, close=False)
    p.role("metal", (0.4, 0.42, 0.4))
    p.lathe((0, 0, 0), (0, -1, 0), [(0.05, 0.158), (0.08, 0.17), (0.108, 0.186), (0.126, 0.2), (0.131, 0.206), (0.128, 0.208)], 40, close=False)
    p.role("enamel", (0.62, 0.6, 0.55))
    p.lathe((0, 0, 0), (0, -1, 0), [(0.125, 0.206), (0.105, 0.19), (0.078, 0.174), (0.048, 0.162)], 40, close=False)
    p.role("wire", (0.3, 0.3, 0.28))
    for i in range(4):
        a = TAU * i / 4 + 0.4
        d = V((math.cos(a), 0, math.sin(a)))
        pts = [d * 0.122 + V((0, -0.204, 0)), d * 0.112 + V((0, -0.26, 0)), d * 0.105 + V((0, -0.3, 0)), d * 0.088 + V((0, -0.34, 0)),
               d * 0.06 + V((0, -0.368, 0)), d * 0.012 + V((0, -0.385, 0))]
        for k in range(len(pts) - 1):
            p.cylinder(pts[k], pts[k + 1], 0.0035, segs=8)
    p.torus((0, -0.3, 0), (0, 1, 0), 0.105, 0.0035, 48, 8)
    p.torus((0, -0.385, 0), (0, 1, 0), 0.014, 0.004, 16, 8)
    return p.obj()


def lamp_bulb():
    p = Piece("lamp_bulb").role("glass")
    p.lathe((0, 0, 0), (0, -1, 0), [(0.019, 0.16), (0.02, 0.2), (0.03, 0.218), (0.044, 0.244), (0.05, 0.272), (0.048, 0.296),
                                     (0.036, 0.318), (0.018, 0.331), (0.0, 0.335)], 32)
    return p.obj()


def caged_bulb():
    """The round room's: a drop from the conduit ring, a socket, a bare bulb (its own glass) in a wire cage; the bulb's centre
    at (0, -0.35, 0) from the top."""
    p = Piece("caged_bulb").role("steel")
    p.cylinder((0, 0, 0), (0, -0.2, 0), 0.01, segs=12)
    p.lathe((0, -0.2, 0), (0, -1, 0), [(0.03, 0), (0.032, 0.01), (0.03, 0.06), (0.026, 0.09), (0.02, 0.095)], 24)
    for w in range(4):
        b = TAU * w / 4 + 0.4
        o = V((math.sin(b), 0, math.cos(b)))
        pts = [o * 0.026 + V((0, -0.28, 0)), o * 0.06 + V((0, -0.31, 0)), o * 0.07 + V((0, -0.36, 0)), o * 0.055 + V((0, -0.42, 0)), V((0, -0.45, 0))]
        for k in range(len(pts) - 1):
            p.cylinder(pts[k], pts[k + 1], 0.005, segs=8)
    p.torus((0, -0.36, 0), (0, 1, 0), 0.07, 0.005, 40, 8)
    return p.obj()


def round_bulb():
    p = Piece("round_bulb").role("glass")
    p.lathe((0, 0.055, 0), (0, -1, 0), [(0.012, 0.0), (0.014, 0.015), (0.03, 0.035), (0.045, 0.065), (0.044, 0.09), (0.03, 0.105), (0.0, 0.11)], 32)
    return p.obj()


# ------------------------------------------------------------------ the CRT sets (a reference size; the game scales each)

CRT_REF = (0.58, 0.5, 0.52)


def crt(knobs_right):
    W, H, D = CRT_REF
    fz = D * 0.5
    p = Piece("crt_tv" if knobs_right else "crt_mon").role("plastic")
    # the moulded bezel (rounded), the tapered housing behind
    p.box((0, H * 0.5, fz - 0.045), (W, H, 0.09))
    p.frustum((0, H * 0.5, fz - 0.08), (W * 0.95, H * 0.93), (0, H * 0.42, -fz), (W * 0.52, H * 0.55))
    if knobs_right:
        sw, sh, sc = W * 0.7, H * 0.74, (-W * 0.11, H * 0.53)
    else:
        sw, sh, sc = W * 0.8, H * 0.66, (0, H * 0.58)
    lip, lz = 0.022, fz + 0.008
    p.role("plastic", (0.8, 0.8, 0.8))
    # the tube's surround: a mitred frame with a soft inner chamfer
    p.box((sc[0], sc[1] + sh * 0.5 + lip * 0.5, lz), (sw + lip * 2, lip, 0.02))
    p.box((sc[0], sc[1] - sh * 0.5 - lip * 0.5, lz), (sw + lip * 2, lip, 0.02))
    p.box((sc[0] - sw * 0.5 - lip * 0.5, sc[1], lz), (lip, sh, 0.02))
    p.box((sc[0] + sw * 0.5 + lip * 0.5, sc[1], lz), (lip, sh, 0.02))
    p.role("black")
    if knobs_right:
        kx = W * 0.39
        p.lathe((kx, H * 0.72, fz), (0, 0, 1), [(0.028, 0), (0.029, 0.004), (0.026, 0.026), (0.024, 0.03), (0.0, 0.031)], 24)
        p.lathe((kx, H * 0.55, fz), (0, 0, 1), [(0.022, 0), (0.023, 0.004), (0.021, 0.022), (0.02, 0.025), (0.0, 0.026)], 24)
        for i in range(5):
            p.box((kx, H * 0.18 + i * 0.03, fz + 0.002), (W * 0.12, 0.01, 0.006))
    else:
        for i in range(3):
            kx = W * (0.1 + i * 0.1)
            p.lathe((kx, H * 0.12, fz), (0, 0, 1), [(0.016, 0), (0.017, 0.003), (0.015, 0.018), (0.014, 0.02), (0.0, 0.021)], 20)
        p.box((-W * 0.32, H * 0.12, fz + 0.004), (0.05, 0.025, 0.012))
    for fx in (-W * 0.35, W * 0.35):
        p.lathe((fx, 0.0, fz - 0.1), (0, 1, 0), [(0.03, 0), (0.026, 0.02), (0.0, 0.021)], 20)
    p.role("rubber")
    p.cylinder((W * 0.1, H * 0.3, -fz + 0.02), (W * 0.14, H * 0.26, -fz - 0.08), 0.012, segs=10)
    return p.obj(bevel=0.006, segs=2)


# ------------------------------------------------------------------ the CRT room's shelving and console

def shelf_post():
    """One slotted angle-iron upright, 2.84 m (the game places and mirrors four per unit): an L of 4 cm flanges, slotted,
    on its foot plate. Its corner on its origin, the flanges along +X and +Z."""
    p = Piece("shelf_post").role("paint")
    H = 2.84
    p.box((0.02, H * 0.5, 0.0015), (0.04, H, 0.003))
    p.box((0.0015, H * 0.5, 0.02), (0.003, H, 0.04))
    p.box((0.02, 0.002, 0.02), (0.05, 0.004, 0.05))
    return p.obj(bevel=0.0008)


def shelf_tray():
    """A pressed-steel shelf 1 m by 0.72 m (scaled along X to its unit), its edges turned down 3.4 cm; its top on y = 0."""
    p = Piece("shelf_tray").role("paint")
    uw, ud = 1.0, 0.72
    p.box((0, -0.00125, 0), (uw - 0.004, 0.0025, ud - 0.004))
    for iz in (-1, 1):
        p.box((0, -0.018, iz * (ud * 0.5 - 0.003)), (uw - 0.006, 0.034, 0.0025))
    for ix in (-1, 1):
        p.box((ix * (uw * 0.5 - 0.003), -0.018, 0), (0.0025, 0.034, ud - 0.006))
    return p.obj(bevel=0.0006)


def console_desk():
    """The CRT room's steel console desk, its top at y = 0.95 (CrtRoom.ConsoleTop), centred, its front toward +Z."""
    T = 0.95
    p = Piece("console_desk").role("paint", (0.75, 0.78, 0.76))
    p.box((0, T - 0.0175, 0), (1.6, 0.035, 0.8))
    for x in (-0.765, 0.765):
        p.box((x, (T - 0.035 + 0.06) * 0.5, 0), (0.035, T - 0.095, 0.76))
        p.box((x, 0.03, 0), (0.03, 0.06, 0.7))
    p.box((0, T * 0.5 + 0.06, -0.355), (1.5, T - 0.3, 0.02))
    p.box((0.48, (T - 0.035 + 0.07) * 0.5, 0.02), (0.52, T - 0.105, 0.7))
    for d in range(3):
        h = 0.34 if d == 2 else 0.2
        y0 = T - 0.06 - 0.1 if d == 0 else T - 0.06 - 0.31 if d == 1 else T - 0.06 - 0.6
        p.role("paint", (0.68, 0.71, 0.69))
        p.box((0.48, y0, 0.375), (0.48, h - 0.012, 0.012))
        p.role("steel", (0.55, 0.53, 0.48))
        p.cylinder((0.4, y0 + h * 0.25, 0.392), (0.56, y0 + h * 0.25, 0.392), 0.007, segs=12)
        for px in (0.4, 0.56):
            p.cylinder((px, y0 + h * 0.25, 0.38), (px, y0 + h * 0.25, 0.392), 0.006, segs=10, caps=False)
    p.role("inset")
    p.box((0, T + 0.0004, 0), (1.5, 0.001, 0.7))
    return p.obj(bevel=0.004, segs=2)


def keyboard():
    """A terminal keyboard: a sloped case and its keys in rows (its origin on the desk, under its middle)."""
    p = Piece("keyboard").role("plastic", (0.55, 0.52, 0.45))
    p.box((0, 0.014, 0), (0.46, 0.028, 0.17))
    p.role("keys", (0.4, 0.38, 0.33))
    for r in range(4):
        for c in range(14):
            x = -0.2 + c * 0.0275 + (r % 2) * 0.006
            if x > 0.165:
                continue
            p.box((x, 0.031 + r * 0.0025, 0.045 - r * 0.032), (0.023, 0.008, 0.024))
    for r in range(4):
        for c in range(2):
            p.box((0.18 + c * 0.028, 0.031, 0.045 - r * 0.032), (0.023, 0.008, 0.024))
    return p.obj(bevel=0.0018, segs=2)


# ------------------------------------------------------------------ the bunker door's keypad (faces +Z, on the wall at z = 0)

BOX_W, BOX_H, BOX_D = 0.15, 0.25, 0.045
PLATE_W, PLATE_H, PLATE_T = 0.124, 0.214, 0.004
FRONT = BOX_D + PLATE_T
KEY_SIZE, KEY_H = 0.027, 0.007


def keypad_body():
    p = Piece("keypad_body").role("steel", (0.34, 0.36, 0.33))
    p.box((0, 0, BOX_D * 0.5), (BOX_W, BOX_H, BOX_D))
    p.role("steel", (0.3, 0.3, 0.27))
    for sy in (-1, 1):
        p.box((0, sy * (BOX_H * 0.5 + 0.012), 0.004), (BOX_W * 0.7, 0.026, 0.008))
        for sx in (-0.035, 0.035):
            c = V((sx, sy * (BOX_H * 0.5 + 0.012), 0.008))
            p.lathe(c, (0, 0, 1), [(0.0065, 0), (0.006, 0.003), (0.0, 0.005)], 6)
    # the conduit, its gland nut, two straps, the junction box below
    p.role("steel", (0.36, 0.36, 0.33))
    c0, c1 = V((0.03, -BOX_H * 0.5, 0.024)), V((0.03, -0.42, 0.024))
    p.cylinder(c0 + V((0, 0.004, 0)), c1, 0.011, segs=20)
    p.lathe(c0 + V((0, -0.002, 0)), (0, -1, 0), [(0.0145, 0), (0.0145, 0.02)], 6)
    for y in (-0.2, -0.33):
        p.box((0.03, y, 0.024), (0.026, 0.012, 0.026))
        p.box((0.03, y, 0.003), (0.05, 0.012, 0.006))
    p.role("steel", (0.3, 0.3, 0.27))
    p.box((0.03, -0.46, 0.025), (0.08, 0.08, 0.05))
    p.box((0.03, -0.46, 0.051), (0.072, 0.072, 0.004))
    # the stainless faceplate, its tamper screws, the readout's bezel and the lamps' bezels
    p.role("steel", (0.62, 0.62, 0.6))
    p.box((0, -0.0095, BOX_D + PLATE_T * 0.5), (PLATE_W, PLATE_H, PLATE_T))
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = V((sx * (PLATE_W * 0.5 - 0.009), -0.0095 + sy * (PLATE_H * 0.5 - 0.009), FRONT))
            p.lathe(c, (0, 0, 1), [(0.0048, 0), (0.0046, 0.001), (0.0042, 0.0022)], 16)
    p.role("black")
    p.box((0, 0.074, FRONT + 0.0015), (0.086, 0.032, 0.003))
    p.role("steel", (0.45, 0.45, 0.43))
    for i in range(3):
        c = V(((i - 1) * 0.026, 0.041, FRONT))
        p.lathe(c, (0, 0, 1), [(0.0045, 0), (0.0045, 0.0018)], 16, close=False)
        p.torus(c + V((0, 0, 0.0016)), (0, 0, 1), 0.0039, 0.0006, 16, 6)
    return p.obj(bevel=0.0012, segs=2)


def keypad_hood():
    p = Piece("keypad_hood").role("steel", (0.32, 0.33, 0.3))
    a = math.radians(14)
    tilt = Matrix(((1, 0, 0), (0, math.cos(a), -math.sin(a)), (0, math.sin(a), math.cos(a))))
    p.box((0, BOX_H * 0.5 + 0.008, BOX_D + 0.012), (BOX_W + 0.012, 0.004, 0.075), tilt)
    for sx in (-1, 1):
        p.box((sx * (BOX_W * 0.5 + 0.004), BOX_H * 0.5 - 0.02, BOX_D + 0.012), (0.004, 0.05, 0.06))
    return p.obj(bevel=0.0008)


def keypad_key():
    """A key cap (its origin on the key's centre): a square stainless cap, its face dished a hair and rubbed brighter."""
    p = Piece("keypad_key").role("key")
    p.box((0, 0, -0.0004), (KEY_SIZE, KEY_SIZE, KEY_H - 0.0008))
    p.role("key", (1.12, 1.12, 1.1))
    p.box((0, 0, KEY_H * 0.5 - 0.0004), (KEY_SIZE - 0.0024, KEY_SIZE - 0.0024, 0.0008))
    return p.obj(bevel=0.0009, segs=3)


def keypad_seat():
    p = Piece("keypad_seat").role("rubber")
    p.box((0, 0, -KEY_H * 0.5 + 0.0008), (KEY_SIZE + 0.004, KEY_SIZE + 0.004, 0.0016))
    return p.obj(bevel=0.0006)


# ------------------------------------------------------------------ the list

def puzzle(name, size, cube):
    def build():
        ob = bamboo_box(name, size, bevel=min(size) * 0.12, segs=3)
        puzzle_uv(ob, cube)
        return ob
    return build


INNER = N * CELL
OUTER = INNER + 2 * WALL

PIECES = {
    "valve_body": (valve_body, None), "valve_wheel": (valve_wheel, None), "valve_gauge": (valve_gauge, None),
    "cryptex_core": (cryptex_core, None), "cryptex_ring": (cryptex_ring, None),
    "cryptex_cap_r": (lambda: cryptex_cap(1), None), "cryptex_cap_l": (lambda: cryptex_cap(-1), None),
    "puzzle_cell": (puzzle("puzzle_cell", (CELL - 0.003,) * 3, CELL), None),
    "puzzle_floor": (puzzle("puzzle_floor", (OUTER, FLOOR, OUTER), OUTER), None),
    "puzzle_wall_x": (puzzle("puzzle_wall_x", (OUTER, DEPTH, WALL), OUTER * 0.5), None),
    "puzzle_wall_z": (puzzle("puzzle_wall_z", (WALL, DEPTH, INNER), INNER * 0.5), None),
    "puzzle_inlay": (puzzle("puzzle_inlay", (OUTER / 4 - 0.002, 0.004, WALL - 0.004), OUTER / 4), None),
    "knife_base": (knife_base, None), "knife_lever": (knife_lever, None),
    "caged_lamp": (caged_lamp, 3000), "lamp_bulb": (lamp_bulb, None), "caged_bulb": (caged_bulb, None), "round_bulb": (round_bulb, None),
    "crt_tv": (lambda: crt(True), 1800), "crt_mon": (lambda: crt(False), 1800),
    "shelf_post": (shelf_post, None), "shelf_tray": (shelf_tray, None), "console_desk": (console_desk, None), "keyboard": (keyboard, None),
    "keypad_body": (keypad_body, None), "keypad_hood": (keypad_hood, None), "keypad_key": (keypad_key, None), "keypad_seat": (keypad_seat, None),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        fresh()
        fn, budget = PIECES[name]
        ob = fn()
        export(ob, name, 256 if name.startswith(("keypad_key", "keypad_seat", "puzzle", "round_bulb", "lamp_bulb")) else 512, budget)
