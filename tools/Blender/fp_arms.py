"""The arms seen in first person (2026-10-11, the owner: "I can see pieces of the player arm not connected to each other",
then "we really need to focus on the arm looking way more realistic and fleshed out it looks like a cartoon character
right now ... getting the arms the best we can will really make the game stand out"; references: BioShock's and
Condemned's hands, a man carrying a hurricane lantern by its bail).

    blender -b --python tools/Blender/fp_arms.py

Writes assets/models/player/ (and previews to build/blender/fp_arm_*.png):
  fp_arm_bail_L.glb   the left hand carrying the lantern by its bail (a wire along X through the origin): the hand over
                      the wire from behind, the four fingers hooked over it and curled back under, the thumb laid along
                      the first finger; the forearm and the jacket's sleeve back toward the eye, out of the lower left
  fp_arm_fist_R.glb   the right hand's grip round a handle standing up through the origin (a flare, a tool): fingers
                      wrapped round its front, the thumb over the first finger; the forearm out of the lower right

A real hand, built as a hand is: a palm lofted from the wrist to the knuckles (wider at the knuckles, the back arched,
the heel of the hand and the thumb's fleshy base swelling under it), four fingers of three bones each at their true
lengths with the knuckles standing out at every joint and nails on their ends, the thumb from low on the palm's side,
the tendons faint across the back of the hand, the wrist's bones, and the forearm swelling toward the elbow; all of it
fused into one skin (a fine voxel remesh). Over the forearm, separate, the jacket's sleeve: loose, its folds gathered
toward the wrist, a cuff band over the wrist bone. Skin coloured as skin (warmer and redder at the knuckles and the
fingertips, paler in the palm, the nails lighter), its pores and creases a baked normal map; the sleeve's weave too.

Coordinates: the game's camera space (x right, y up, z back toward the eye), converted to Blender's (x, -z, y).
"""
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, apply_mods, add_mod, fbm, ROOT
import wendigo as W

OUT = os.path.join(ROOT, "assets", "models", "player")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
V = Vector
TEX = 1024


def G(x, y, z):
    return V((x, -z, y))


# ------------------------------------------------------------------ primitives (each later fused into one skin)

def _obj(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def capsule(name, a, b, ra, rb, up=None, flat=1.0, waist=1.0):
    """A tapered bone from a (radius ra) to b (radius rb), rounded at both ends: swept in rings, flattened toward `up`
    by `flat` (a finger's bone is wider than it is deep) and slimmed in its middle by `waist`."""
    axis = b - a
    L = max(axis.length, 1e-5)
    d = axis / L
    if up is None:
        up = V((0, 0, 1)) if abs(d.z) < 0.9 else V((1, 0, 0))
    up = (up - d * up.dot(d)).normalized()
    sd = d.cross(up).normalized()
    bm = bmesh.new()
    nr, nl = 14, 9
    rings = []
    # (the rounded ends: rings stepping out from each end's tip)
    ts = [-0.9, -0.6, -0.3] + [i / (nl - 1) for i in range(nl)] + [1.3, 1.6, 1.9]
    for t in ts:
        if t < 0:
            k = math.sqrt(max(0.0, 1 - (t / 0.9 * 0.97) ** 2)) if t > -0.9 else 0.05
            c, r = a + d * (t / 0.9 * ra * 0.9), ra * k
        elif t > 1:
            u = (t - 1) / 0.9
            k = math.sqrt(max(0.0, 1 - (u * 0.97) ** 2)) if t < 1.9 else 0.05
            c, r = b + d * (u * rb * 0.9), rb * k
        else:
            c = a + d * (t * L)
            r = (ra + (rb - ra) * t) * (1 - (1 - waist) * math.sin(t * math.pi))
        rings.append([bm.verts.new(c + (sd * math.cos(j / nr * math.tau) + up * math.sin(j / nr * math.tau) * flat) * r) for j in range(nr)])
    for i in range(len(rings) - 1):
        for j in range(nr):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % nr], rings[i + 1][(j + 1) % nr], rings[i + 1][j]))
    for ring in (rings[0], rings[-1]):
        bm.faces.new(ring)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj(name, bm)


def ellipsoid(name, c, axes, radii):
    """An ellipsoid at c with its three axes (unit vectors) and radii."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=1.0)
    M = Matrix((axes[0] * radii[0], axes[1] * radii[1], axes[2] * radii[2])).transposed()
    for v in bm.verts:
        v.co = c + M @ v.co
    return _obj(name, bm)


def loft(name, centres, sections, cap0=True, cap1=True, nr=24):
    """A loft through ring sections: sections[i](angle) -> offset from centres[i]."""
    bm = bmesh.new()
    rings = [[bm.verts.new(c + sec(j / nr * math.tau)) for j in range(nr)] for c, sec in zip(centres, sections)]
    for i in range(len(rings) - 1):
        for j in range(nr):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % nr], rings[i + 1][(j + 1) % nr], rings[i + 1][j]))
    for ring, do in ((rings[0], cap0), (rings[-1], cap1)):
        if do:
            bm.faces.new(ring)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj(name, bm)


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    return ob


def remesh(ob, voxel, smooth):
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel, adaptivity=0.0)
    apply_mods(ob)
    add_mod(ob, "SMOOTH", factor=0.5, iterations=smooth)
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


# ------------------------------------------------------------------ the hand

# (length of each bone: proximal, middle, distal; the knuckle's place across the palm, across (s) and along (f); radius)
FINGERS = [
    ("index", (0.040, 0.024, 0.019), 0.024, 0.089, 0.0108),
    ("middle", (0.045, 0.028, 0.020), 0.005, 0.093, 0.0111),
    ("ring", (0.042, 0.026, 0.019), -0.013, 0.089, 0.0104),
    ("little", (0.033, 0.019, 0.017), -0.029, 0.080, 0.0092),
]


class Hand:
    """A hand in its own frame: origin the wrist joint; f toward the knuckles, b out of the back of the hand, s toward
    the thumb. Built for the right or the left hand (s flips)."""

    def __init__(self, wrist, f, b, right):
        self.w = wrist
        self.f = f.normalized()
        self.b = (b - self.f * b.dot(self.f)).normalized()
        self.s = (self.b.cross(self.f) if right else self.f.cross(self.b)).normalized()
        self.parts = []
        self.nails = []
        self.joints = []   # (point, how red: knuckles and tips)

    def P(self, f, b, s):
        return self.w + self.f * f + self.b * b + self.s * s

    def palm(self):
        # the palm lofted from the wrist to the knuckles: wider and flatter toward them, the back arched across
        cs, secs = [], []
        for i in range(9):
            t = i / 8
            f = -0.012 + t * 0.1   # (from back inside the wrist: no cut end left showing at the bend)
            half_w = 0.029 + 0.013 * math.sin(t * math.pi * 0.6) + 0.002 * t
            back, front = 0.0115 - 0.002 * t, -0.0135 + 0.002 * t
            c = self.P(f, (back + front) * 0.5, -0.002)
            hw, hb = half_w, (back - front) * 0.5
            def sec(a, hw=hw, hb=hb):
                ca, sa = math.cos(a), math.sin(a)
                x = ca * hw
                y = sa * hb
                if y > 0:
                    y += 0.003 * (1 - (x / hw) ** 2)     # (the back of the hand arched across)
                return self.s * x + self.b * y
            cs.append(c)
            secs.append(sec)
        self.parts.append(loft("Palm", cs, secs))
        # the thumb's fleshy base, and the heel of the hand on the little finger's side
        self.parts.append(ellipsoid("Thenar", self.P(0.033, -0.01, 0.019), (self.f, self.b, self.s), (0.034, 0.015, 0.019)))
        self.parts.append(ellipsoid("Hypothenar", self.P(0.04, -0.009, -0.024), (self.f, self.b, self.s), (0.034, 0.01, 0.011)))
        # the wrist's bones, standing out a little each side
        self.parts.append(ellipsoid("UlnaHead", self.P(-0.008, 0.003, -0.022), (self.f, self.b, self.s), (0.011, 0.008, 0.007)))
        # the tendons, faint across the back of the hand to each knuckle
        for name, bones, s0, f0, r in FINGERS:
            self.parts.append(capsule(f"Tendon_{name}", self.P(0.016, 0.0098, s0 * 0.5), self.P(f0 - 0.014, 0.0103, s0), 0.0019, 0.0018))
        # the glove's cuff: a slightly fuller band round the wrist, its edge tucked under the sleeve
        self.parts.append(ellipsoid("GloveCuff", self.P(-0.022, -0.001, -0.001), (self.f, self.b, self.s), (0.018, 0.02, 0.031)))
        # the wrist: a rounded joint the palm and the forearm both run into, so the bend between them is one surface
        self.parts.append(ellipsoid("Wrist", self.P(-0.006, -0.001, -0.001), (self.f, self.b, self.s), (0.024, 0.0175, 0.029)))

    def _chain(self, base, d, lengths, radii, curl, prefix, flat=0.88):
        """Bones from `base` along d, each joint bent by curl[i] radians toward the palm."""
        side = d.cross(self.b).normalized()
        # (which way round `side` bends a bone toward the palm: tried once, the right sign kept)
        test = (Matrix.Rotation(0.3, 3, side) @ d)
        sign = 1.0 if test.dot(self.b) < d.dot(self.b) else -1.0
        pts = [base]
        p = base
        bend = 0.0
        for k, (L, ang) in enumerate(zip(lengths, curl)):
            bend += ang
            dk = (Matrix.Rotation(sign * bend, 3, side) @ d).normalized()
            p = p + dk * L
            pts.append(p)
        for i in range(len(pts) - 1):
            seg = (pts[i + 1] - pts[i]).normalized()
            up = (self.b - seg * self.b.dot(seg)).normalized()
            self.parts.append(capsule(f"{prefix}{i}", pts[i], pts[i + 1], radii[i], radii[i + 1] * 0.97, up=up, flat=flat, waist=0.93))
        return pts

    def finger(self, name, bones, s0, f0, r, curl, spread=0.0):
        """A finger from its knuckle, each joint bent by curl[i] radians toward the palm; spread turns it about b."""
        base = self.P(f0, 0.001, s0)
        d = (Matrix.Rotation(spread, 3, self.b) @ self.f).normalized()
        rad = [r, r * 0.9, r * 0.82, r * 0.74]
        pts = self._chain(base, d, bones, rad, curl, name)
        # the knuckles a touch proud on the back of every joint
        for i in range(len(pts) - 1):
            seg = (pts[i + 1] - pts[i]).normalized()
            up = (self.b - seg * self.b.dot(seg)).normalized()
            self.parts.append(ellipsoid(f"{name}Knuckle{i}", pts[i] + up * rad[i] * 0.22, (seg, up, seg.cross(up)), (rad[i] * 0.85, rad[i] * 0.72, rad[i] * 0.92)))
            self.joints.append((pts[i] + up * rad[i] * 0.9, 1.0 if i == 0 else 0.7))
        # the pad of the fingertip, and its nail on the back of the last bone
        last = (pts[-1] - pts[-2]).normalized()
        up = (self.b - last * self.b.dot(last)).normalized()
        self.parts.append(ellipsoid(f"{name}Pad", pts[-1] - last * rad[-1] * 0.6 - up * rad[-1] * 0.25, (last, up, last.cross(up)), (rad[-1] * 1.25, rad[-1] * 0.8, rad[-1] * 1.0)))
        self.nails.append((pts[-1] - last * rad[-1] * 0.9 + up * rad[-1] * 0.78, last, up, rad[-1]))
        self.joints.append((pts[-1], 0.9))
        return pts

    def thumb(self, tip, out=0.012):
        """From inside its fleshy base to `tip` (where it rests): the metacarpal out from the palm's side, the two
        bones bowed a little outward (away from the palm) between."""
        base = self.P(0.02, -0.006, 0.014)
        rad = [0.0145, 0.0126, 0.0112, 0.0099]
        span = tip - base
        L = span.length
        dirv = span / L
        bow = (self.s - dirv * self.s.dot(dirv)).normalized()
        pts = [base, base + span * 0.42 + bow * out * 1.1, base + span * 0.74 + bow * out, tip]
        for i in range(3):
            seg = (pts[i + 1] - pts[i]).normalized()
            up = (self.b - seg * self.b.dot(seg)).normalized()
            self.parts.append(capsule(f"Thumb{i}", pts[i], pts[i + 1], rad[i], rad[i + 1] * 0.97, up=up, flat=0.9, waist=0.94))
        last = (pts[-1] - pts[-2]).normalized()
        up = (self.b - last * self.b.dot(last)).normalized()
        self.parts.append(ellipsoid("ThumbPad", pts[-1] - last * rad[-1] * 0.6 - up * rad[-1] * 0.25, (last, up, last.cross(up)), (rad[-1] * 1.25, rad[-1] * 0.8, rad[-1] * 1.0)))
        self.nails.append((pts[-1] - last * rad[-1] * 0.9 + up * rad[-1] * 0.8, last, up, rad[-1] * 1.05))
        self.joints.append((pts[2], 0.7))
        self.joints.append((pts[-1], 0.8))
        return pts

    def forearm(self, toward, length):
        """From the wrist back along `toward` (unit), swelling toward the elbow, flattened (wide across the back)."""
        cs, secs = [], []
        n = 8
        for i in range(n + 1):
            t = i / n
            c = self.w + self.f * 0.006 + toward * (length * t)
            w = 0.028 + 0.012 * math.sin(min(1, t * 1.3) * math.pi * 0.5)
            h = 0.02 + 0.012 * math.sin(min(1, t * 1.2) * math.pi * 0.5)
            u = (self.s - toward * self.s.dot(toward)).normalized()
            v = toward.cross(u).normalized()
            if v.dot(self.b) < 0:
                v = -v
            secs.append(lambda a, w=w, h=h, u=u, v=v: u * math.cos(a) * w + v * math.sin(a) * h)
            cs.append(c)
        self.parts.append(loft("Forearm", cs, secs, cap0=False, cap1=True))


def nail(name, at, along, up, r):
    """A nail: a thin curved plate on the back of the last bone."""
    side = along.cross(up).normalized()
    bm = bmesh.new()
    verts = []
    for i in range(5):
        for j in range(4):
            u = (i / 4 - 0.5) * 2
            v = j / 3
            p = at + side * u * r * 0.72 + along * (v - 0.4) * r * 1.5 - up * (u * u) * r * 0.25
            verts.append(bm.verts.new(p))
    for i in range(4):
        for j in range(3):
            bm.faces.new((verts[i * 4 + j], verts[(i + 1) * 4 + j], verts[(i + 1) * 4 + j + 1], verts[i * 4 + j + 1]))
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=r * 0.18)
    return _obj(name, bm)


def sleeve(name, wrist, toward, b, length, seed):
    """The jacket's sleeve over the forearm: a loose tube from just over the wrist bone back past the elbow, its folds
    gathered toward the cuff, a cuff band at its end. Open at the cuff."""
    cs, secs = [], []
    n = 22
    rng = np.random.default_rng(seed)
    phase = rng.uniform(0, math.tau, 6)
    u = (b - toward * b.dot(toward)).normalized().cross(toward).normalized()
    v = toward.cross(u).normalized()
    for i in range(n + 1):
        t = i / n
        c = wrist + toward * (0.022 + length * t)
        base = 0.041 + 0.015 * min(1, t * 2.2) + (0.004 if t < 0.06 else 0.0)
        gather = math.exp(-t * 4)   # (folds bunched near the cuff, easing off up the arm)
        def sec(a, t=t, base=base, gather=gather):
            fold = 0.0
            for k in range(3):
                fold += math.sin(a * (2 + k) + phase[k] + t * (14 + 5 * k)) * (0.0045 - 0.001 * k)
            fold += math.sin(t * 38 + phase[3] + a) * 0.004 * gather
            ry = 1.0 + 0.12 * math.sin(a * 2 + phase[4])
            return (u * math.cos(a) + v * math.sin(a) * ry) * (base + fold * (0.5 + gather))
        cs.append(c)
        secs.append(sec)
    ob = loft(name, cs, secs, cap0=False, cap1=True, nr=40)
    add_mod(ob, "SUBSURF", levels=2)
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


# ------------------------------------------------------------------ colour and the bake

SKIN = V((0.55, 0.3, 0.21))
SKIN_RED = V((0.6, 0.24, 0.17))
PALM = V((0.62, 0.36, 0.27))
NAIL = V((0.66, 0.45, 0.38))
SLEEVE = V((0.05, 0.06, 0.042))


def paint(ob, fn):
    me = ob.data
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    vals = []
    for v in me.vertices:
        c = fn(v.co, v.normal)
        vals.extend((c[0], c[1], c[2], 1.0))
    attr.data.foreach_set("color", vals)
    me.color_attributes.active_color = attr


GLOVE = V((0.018, 0.013, 0.01))
GLOVE_WORN = V((0.045, 0.034, 0.026))


def glove_colour(hand):
    """Dark leather: near black-brown, a little uneven, worn paler over the knuckles and the fingertips, the seams
    along the fingers' sides darker."""
    def fn(co, n):
        c = GLOVE * (0.85 + 0.3 * fbm(co.x * 120, co.y * 120, co.z * 120, 3))
        for p, k in hand.joints:
            d = (co - p).length
            if d < 0.012:
                c = c.lerp(GLOVE_WORN, k * (1 - d / 0.012) * 0.8)
        return tuple(c)
    return fn


def skin_colour(hand):
    def fn(co, n):
        c = SKIN * (0.96 + 0.06 * fbm(co.x * 90, co.y * 90, co.z * 90, 2))
        # paler in the palm (the side facing away from the back of the hand)
        if n.dot(hand.b) < -0.3:
            c = c.lerp(PALM, min(1, (-n.dot(hand.b) - 0.3) * 1.6))
        # warmer and redder at the knuckles and the fingertips
        for p, k in hand.joints:
            d = (co - p).length
            if d < 0.014:
                c = c.lerp(SKIN_RED, k * (1 - d / 0.014) * 0.7)
        # the faint blue of the veins over the back of the hand and the wrist
        vn = fbm(co.x * 60 + 3, co.y * 60, co.z * 60, 2)
        if n.dot(hand.b) > 0.3 and abs(vn) < 0.04:
            c = c.lerp(V((0.32, 0.27, 0.3)), 0.25)
        return tuple(c)
    return fn


def bump_material(name, kind):
    mt = bpy.data.materials.new(name)
    mt.use_nodes = True
    nt = mt.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    if kind == "skin":
        pores = nt.nodes.new("ShaderNodeTexVoronoi")
        pores.inputs["Scale"].default_value = 900.0
        lines = nt.nodes.new("ShaderNodeTexNoise")
        lines.inputs["Scale"].default_value = 260.0
        lines.inputs["Detail"].default_value = 2.0
        nt.links.new(tc.outputs["Object"], pores.inputs["Vector"])
        nt.links.new(tc.outputs["Object"], lines.inputs["Vector"])
        mix = nt.nodes.new("ShaderNodeMath")
        mix.operation = "MULTIPLY_ADD"
        nt.links.new(lines.outputs["Fac"], mix.inputs[0])
        mix.inputs[1].default_value = 0.25
        nt.links.new(pores.outputs["Distance"], mix.inputs[2])
        h, strength = mix.outputs["Value"], 0.08
    else:
        a = nt.nodes.new("ShaderNodeTexWave")
        a.inputs["Scale"].default_value = 420.0
        a.bands_direction = "DIAGONAL"
        nz = nt.nodes.new("ShaderNodeTexNoise")
        nz.inputs["Scale"].default_value = 70.0
        for x in (a, nz):
            nt.links.new(tc.outputs["Object"], x.inputs["Vector"])
        mix = nt.nodes.new("ShaderNodeMath")
        mix.operation = "MULTIPLY_ADD"
        nt.links.new(a.outputs["Fac"], mix.inputs[0])
        mix.inputs[1].default_value = 0.5
        nt.links.new(nz.outputs["Fac"], mix.inputs[2])
        h, strength = mix.outputs["Value"], 0.3
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    bump.inputs["Distance"].default_value = 0.0006
    nt.links.new(h, bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mt


def build(hand, name, sleeve_ob, out_name):
    skin = remesh(join(hand.parts, name + "Skin"), 0.0009, 6)
    # (2026-10-11, the owner: gloves, dark: no nails; the cuff of the glove a band at the wrist)
    # (coloured by vertex, not baked: a bake from the dense copy onto this one scattered streaks over it; the vertex
    # colours are smooth, and there are enough of them for the knuckles, the palm, the nails)
    paint(skin, glove_colour(hand))
    paint(sleeve_ob, lambda co, n: tuple(SLEEVE * (0.94 + 0.12 * fbm(co.x * 30, co.y * 30, co.z * 30, 2))))
    if SHAPE:
        return join([skin, sleeve_ob], out_name)
    for ob, mn, rough in ((skin, "fp_glove", 0.5), (sleeve_ob, "fp_sleeve", 0.95)):
        ob.data.materials.clear()
        m = bpy.data.materials.new(mn)
        m.use_nodes = True
        nt = m.node_tree
        vc = nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = "Col"
        nt.links.new(vc.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
        nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = rough
        ob.data.materials.append(m)
    for o, target in ((skin, 12000), (sleeve_ob, 4000)):
        ratio = min(1.0, target / max(1, W.tri_count(o)))
        if ratio < 1:
            add_mod(o, "DECIMATE", ratio=ratio)
            apply_mods(o)
    low = join([skin, sleeve_ob], out_name)
    me = low.data
    # (a clean mesh for the exporter: stale attributes from the remesh sent it out empty, "array length mismatch")
    for a_ in [a for a in me.attributes if not a.name.startswith(".") and a.name not in ("position", "sharp_face", "material_index", "Col")]:
        try:
            me.attributes.remove(a_)
        except Exception:
            pass
    me.color_attributes.active_color = me.color_attributes["Col"]
    add_mod(low, "TRIANGULATE")
    apply_mods(low)
    me.validate(clean_customdata=False)
    me.update()
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    path = os.path.join(OUT, out_name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_materials="EXPORT", export_colors=True)
    print(f"[fp-arm] {out_name}: {W.tri_count(low)} triangles -> {path}")
    return low


def place(hand, pts_list, target):
    """Moves every part so the grip (the mean of `pts_list`) lands on `target`."""
    m = sum(pts_list, V()) / len(pts_list)
    d = target - m
    for o in hand.parts:
        o.location += d
    hand.nails = [(a + d, b, c, r) for a, b, c, r in hand.nails]
    hand.joints = [(p + d, k) for p, k in hand.joints]
    hand.w = hand.w + d


# ------------------------------------------------------------------ the two grips

def bail_left():
    """The lantern carried by its bail (a wire along X): fingers forward over it and curled back under it."""
    # (2026-10-11, the owner: "too stiff and unnaturally bent ... the grip is still too loose": the forearm now drops away
    # down and back to the lower left, the hand carried a little up from it at the wrist, the way a lantern's carried)
    f = G(0.08, 0.1, -1.0)       # the knuckles' way: forward, a touch up, a little in toward the middle
    b = G(0.0, 1.0, 0.4)         # the back of the hand: up, and back toward the eye
    hand = Hand(G(-0.03, 0.06, 0.1), f, b, right=False)
    hand.palm()
    grip = []
    fingers = {}
    for (name, bones, s0, f0, r), spread in zip(FINGERS, (0.03, 0.0, -0.03, -0.07)):
        pts = hand.finger(name, bones, s0, f0, r, (0.3, 1.75, 1.15), spread)
        fingers[name] = pts
        # (the wire in the crease inside the middle knuckle, the finger closed tight round it: a finger's radius in from
        # the joint toward the middle of the curl, and the wire's own)
        # (pts: the knuckle, the middle joint, the last joint, the tip)
        c = (pts[0] + pts[1] + pts[2] + pts[3]) / 4
        grip.append(pts[1] + (c - pts[1]).normalized() * (r * 0.92 + 0.0016))
    # the thumb laid along the side of the first finger, resting on it
    ix = fingers["index"]
    hand.thumb(ix[1] + hand.s * 0.013 + hand.b * 0.003 - hand.f * 0.004)
    # the wire runs under the middle bones: the hook's inside (a finger's radius in from the bone line)
    place(hand, grip, G(0, 0, 0))
    toward = G(-0.3, -0.85, 0.55).normalized()
    hand.forearm(toward, 0.32)
    sl = sleeve("Sleeve", hand.w, toward, hand.b, 0.42, 7)
    return build(hand, "ArmBailL", sl, "fp_arm_bail_L")


def fist_right():
    """The right hand round a handle standing up through the origin."""
    f = G(-0.75, 0.05, -0.7)     # the knuckles' way: forward and in toward the middle
    b = G(0.75, 0.15, -0.55)     # the back of the hand: out to the right and forward
    hand = Hand(G(0.06, -0.02, 0.07), f, b, right=True)
    hand.palm()
    grip = []
    fingers = {}
    for (name, bones, s0, f0, r), spread in zip(FINGERS, (0.02, 0.0, -0.02, -0.05)):
        pts = hand.finger(name, bones, s0, f0, r, (1.45, 1.7, 1.1), spread)
        fingers[name] = pts
        grip.append((pts[1] + pts[2] + pts[3]) / 3)
    # the thumb across the front of the curled first and middle fingers
    tip = fingers["index"][2].lerp(fingers["middle"][2], 0.45) - hand.b * 0.004 + hand.s * 0.0
    hand.thumb(tip + (tip - hand.P(0.05, 0, 0)).normalized() * 0.011, out=0.008)
    place(hand, grip, G(0, 0, 0))
    toward = (-hand.f + G(0.35, -0.5, 0.3)).normalized()
    hand.forearm(toward, 0.32)
    sl = sleeve("Sleeve", hand.w, toward, hand.b, 0.42, 11)
    return build(hand, "ArmFistR", sl, "fp_arm_fist_R")


def preview(ob, name, eye):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE"
    sh.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 800, 800
    cam = bpy.data.objects.get("PreviewCam") or bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    sc.camera = cam
    for tag, at, look in (("eye", eye, G(0, 0, 0)), ("side", G(0.0, 0.12, 0.0) + V((0.32, 0.06, 0.06)), G(0, 0.02, 0.05)),
                          ("close", G(0, 0.07, 0.0) + V((0.1, -0.14, 0.1)), G(0, 0.02, 0.02))):
        cam.location = at
        cam.rotation_euler = (look - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = 35
        cam.data.clip_start = 0.005
        sc.render.filepath = os.path.join(PREV, f"fp_arm_{name}_{tag}.png")
        bpy.ops.render.render(write_still=True)


SHAPE = "--" in sys.argv and "shape" in sys.argv[sys.argv.index("--") + 1:]


def main():
    clear()
    a = bail_left()
    preview(a, "bail_L", G(0.27, 0.11, 0.62))
    clear()
    b = fist_right()
    preview(b, "fist_R", G(-0.22, 0.2, 0.55))


main()
