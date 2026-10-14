"""The arms seen in first person (2026-10-11, the owner: "I can see pieces of the player arm not connected to each other",
then "we really need to focus on the arm looking way more realistic and fleshed out it looks like a cartoon character
right now ... getting the arms the best we can will really make the game stand out"; references: BioShock's and
Condemned's hands, a man carrying a hurricane lantern by its bail).

    blender -b --python tools/Blender/fp_arms.py [-- handle stick camera relaxed]

Writes assets/models/player/fp_hand_L_<pose>.glb, one per pose in POSES (and previews to build/blender/fp_arm_L_*.png).
(2026-10-13d) Every pose is the LEFT hand, its origin the pose's socket (the grip's centre); the game mirrors it for the
right (scripts/player/FirstPersonHand.cs):
  handle    a fist round a horizontal grip along X (the lantern's bail grip), the thumb over the fingers
  stick     a fist round an upright grip along Y (a flare, a tool's handle)
  camera    one end of a box held from the side, the grip along Y: fingers round its front, the thumb up its back
  relaxed   open, the fingers gently curled; the socket the middle of the palm

A gloved hand, built as a hand is: a palm lofted from the wrist to the knuckles (narrow at the wrist, domed, rounding
over into a row of knuckles; the heel of the hand, the thumb's base and the pad under the fingers' roots full under it),
four fingers of three bones each at their true lengths, each bent to wrap its grip (solve_curl) and swept as one smooth
skin (tube), the thumb from low on the palm's side; all of it fused into one skin (a fine voxel remesh). Then the
glove's marks carved in (detail: creases inside the bends, the fingers' side seams, the thumb's seam, the stitched lines
and the tendons down the back, the cuff seam and the wrist's shirring) and its leather painted by vertex (paint_glove:
worn paler where stretched, darker in its folds). The forearm leaves the wrist nearly in line with the hand; over it,
separate, the jacket's sleeve: loose, its folds gathered toward the cuff.

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


def tube(name, pts, rads, upref, flat=0.88, seed=0, per=10, nr=20):
    """(2026-10-13d) A finger as one smooth skin: swept along a Catmull-Rom curve through its joints (so the bends are
    rounded, never a corner between two capsules), its section carried along without twisting; tapering from joint to
    joint, a soft swell over the back of each knuckle, the pads fuller underneath, a slow unevenness along it; the root
    closed (it's buried in the palm), the tip rounded over."""
    rng = np.random.default_rng(1000 + seed)
    ph = rng.uniform(0, math.tau, 3)
    n = len(pts) - 1
    ext = [pts[0] * 2 - pts[1]] + list(pts) + [pts[-1] * 2 - pts[-2]]

    def cr(i, t):
        p0, p1, p2, p3 = ext[i], ext[i + 1], ext[i + 2], ext[i + 3]
        t2, t3 = t * t, t * t * t
        return 0.5 * ((p1 * 2) + (p2 - p0) * t + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * t2 + (p1 * 3 - p0 - p2 * 3 + p3) * t3)

    samples = []
    for i in range(n):
        for k in range(per):
            samples.append((cr(i, k / per), i + k / per))
    samples.append((pts[-1], float(n)))
    bm = bmesh.new()
    rings = []
    up = None
    tan_prev = None
    for j, (c, u) in enumerate(samples):
        a = samples[max(0, j - 1)][0]
        b = samples[min(len(samples) - 1, j + 1)][0]
        tan = (b - a).normalized()
        if up is None:
            up = (upref - tan * upref.dot(tan)).normalized()
        else:   # (the frame carried along without twist)
            rot = tan_prev.rotation_difference(tan)
            up = (rot @ up)
            up = (up - tan * up.dot(tan)).normalized()
        tan_prev = tan
        sd = tan.cross(up).normalized()
        i0 = min(n - 1, int(u))
        f = u - i0
        r = rads[i0] + (rads[i0 + 1] - rads[i0]) * f
        r *= 1 + 0.03 * math.sin(u * 2.1 + ph[0]) + 0.015 * math.sin(u * 5.3 + ph[1])
        # (the back of each joint a little proud; the middle of each bone a touch slimmer)
        knuckle = sum(math.exp(-((u - jn) / 0.2) ** 2) for jn in range(n)) * (0.1 if u < 0.5 else 0.08)
        r *= 1 - 0.035 * math.sin(math.pi * f)
        def ring(c, r, sd=sd, up=up, knuckle=knuckle):
            out = []
            for q in range(nr):
                ang = q / nr * math.tau
                ca, sa = math.cos(ang), math.sin(ang)
                k = 1 + (knuckle * sa if sa > 0 else -0.12 * sa)   # (proud knuckle above; full pads below)
                out.append(c + (sd * ca + up * sa * flat) * r * k)
            return out
        rings.append([bm.verts.new(p) for p in ring(c, r)])
        last = (c, r, tan, sd, up, knuckle)
    # the tip rounded over (a little flattened, the pad underneath)
    c, r, tan, sd, up, knuckle = last
    for k in range(1, 6):
        th = k / 6 * math.pi * 0.5
        cc = c + tan * r * 0.95 * math.sin(th) - up * r * 0.12 * math.sin(th)
        rr = r * math.cos(th)
        rings.append([bm.verts.new(p) for p in [cc + (sd * math.cos(q / nr * math.tau) + up * math.sin(q / nr * math.tau) * flat) * rr for q in range(nr)]])
    for i in range(len(rings) - 1):
        for q in range(nr):
            bm.faces.new((rings[i][q], rings[i][(q + 1) % nr], rings[i + 1][(q + 1) % nr], rings[i + 1][q]))
    bm.faces.new(rings[0])
    bm.faces.new(rings[-1])
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
        self.full = 1.0    # (the palm's depth: a gloved fist's back is fuller and rounder)
        self.feats = []    # (2026-10-13d) the glove's creases and seams, carved into the fused skin after the remesh

    def P(self, f, b, s):
        return self.w + self.f * f + self.b * b + self.s * s

    def palm(self, tendons=True):
        # the palm lofted from the wrist to the knuckles: wider and flatter toward them, the back arched across
        cs, secs = [], []
        for i in range(9):
            t = i / 8
            f = -0.012 + t * 0.1   # (from back inside the wrist: no cut end left showing at the bend)
            half_w = 0.029 + 0.013 * math.sin(t * math.pi * 0.6) + 0.002 * t
            back, front = (0.0115 - 0.002 * t) * self.full, (-0.0135 + 0.002 * t) * self.full
            c = self.P(f, (back + front) * 0.5, -0.002)
            hw, hb = half_w, (back - front) * 0.5
            def sec(a, hw=hw, hb=hb):
                ca, sa = math.cos(a), math.sin(a)
                x = ca * hw
                y = sa * hb
                if y > 0:
                    y += 0.003 * self.full ** 3 * (1 - (x / hw) ** 2)     # (the back of the hand arched across)
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
        for name, bones, s0, f0, r in (FINGERS if tendons else ()):
            self.parts.append(capsule(f"Tendon_{name}", self.P(0.016, 0.0098, s0 * 0.5), self.P(f0 - 0.014, 0.0103, s0), 0.0019, 0.0018))
        # the glove's cuff: a slightly fuller band round the wrist, its edge tucked under the sleeve
        self.parts.append(ellipsoid("GloveCuff", self.P(-0.022, -0.001, -0.001), (self.f, self.b, self.s), (0.018, 0.02, 0.031)))
        # the wrist: a rounded joint the palm and the forearm both run into, so the bend between them is one surface
        self.parts.append(ellipsoid("Wrist", self.P(-0.006, -0.001, -0.001), (self.f, self.b, self.s), (0.024, 0.0175, 0.029)))

    def palm_fist(self):
        """(2026-10-13d) The back of a gloved fist, not a slab: narrow at the wrist and widening to the knuckles, domed
        across and bowed along, rounding over at its front into the four knuckles standing proud in a row; the heel of the
        hand and the thumb's base full under it. Its seams: the three stitched lines down the back of the hand, the
        wrist's shirring, the seam where the cuff's panel meets the hand."""
        def sstep(a, b, x):
            t = min(1.0, max(0.0, (x - a) / (b - a)))
            return t * t * (3 - 2 * t)
        cs, secs = [], []
        N = 16
        for i in range(N + 1):
            t = i / N
            f = -0.018 + t * 0.098
            endk = 1.0 if t < 0.8 else math.sqrt(max(0.06, 1 - ((t - 0.8) / 0.21) ** 2))   # (rounding over at the knuckles)
            endk *= min(1.0, 0.88 + t * 2.0)   # (and tapered away inside the wrist at its start: no cut end standing out of it)
            hw = (0.0255 + 0.0155 * sstep(0.05, 0.85, t)) * (0.55 + 0.45 * endk)
            back = (0.0118 + 0.0026 * sstep(0.0, 0.7, t)) * endk
            front = (0.0125 + 0.004 * math.sin(math.pi * min(1.0, t * 1.08))) * endk
            arch = 0.0036 * sstep(0.1, 0.6, t) * endk
            c = self.P(f, 0.0016 * math.sin(math.pi * t), -0.001)
            def sec(a, hw=hw, back=back, front=front, arch=arch):
                ca, sa = math.cos(a), math.sin(a)
                x = ca * hw * (0.93 if ca < 0 else 1.0)   # (the little finger's side a touch narrower and rounder)
                if sa >= 0:
                    y = sa * back + arch * (1 - ca * ca) ** 1.5   # (domed across the back)
                else:
                    y = sa * front
                return self.s * x + self.b * y
            cs.append(c)
            secs.append(sec)
        self.parts.append(loft("Palm", cs, secs, nr=32))
        self.parts.append(ellipsoid("Thenar", self.P(0.034, -0.012, 0.016), (self.f, self.b, self.s), (0.034, 0.013, 0.02)))
        self.parts.append(ellipsoid("Hypothenar", self.P(0.042, -0.01, -0.018), (self.f, self.b, self.s), (0.036, 0.009, 0.017)))
        # the pad across the palm under the fingers' roots: they grow out of the palm, not plug into it
        self.parts.append(ellipsoid("PalmPad", self.P(0.074, -0.011, -0.002), (self.f, self.b, self.s), (0.015, 0.0085, 0.038)))
        self.parts.append(ellipsoid("UlnaHead", self.P(-0.01, 0.004, -0.021), (self.f, self.b, self.s), (0.008, 0.005, 0.0055)))
        self.parts.append(ellipsoid("Wrist", self.P(-0.012, -0.001, -0.001), (self.f, self.b, self.s), (0.018, 0.0105, 0.021)))
        # the knuckles: the heads of the four bones, a ridge of rounded bumps across the front of the fist
        for name, bones, s0, f0, r in FINGERS:
            self.parts.append(ellipsoid(f"Knuckle_{name}", self.P(f0 - 0.006, 0.003, s0), (self.f, self.b, self.s), (r * 0.95, r * 0.85, r * 1.0)))
        # (the owner: "more strong") the tendons drawn tight under the glove, from each knuckle back toward the wrist,
        # gathering in: soft raised ridges
        for name, bones, s0, f0, r in FINGERS:
            self.feats.append(("backline", [(f0 - 0.012, s0), (0.045, s0 * 0.72), (0.012, s0 * 0.4)], 0.0021, -0.00045))
        # the three stitched lines down the back of the hand, from between the knuckles toward the wrist (raised cords with
        # a fine groove each side), in the hand's plane: (f, s) points
        for k in range(3):
            sa, sb = FINGERS[k][2], FINGERS[k + 1][2]
            sm = (sa + sb) * 0.5
            self.feats.append(("backline", [(0.07, sm * 1.05), (0.045, sm * 0.92), (0.022, sm * 0.78)], 0.0009, -0.00014))
            for off in (-0.0019, 0.0019):
                self.feats.append(("backline", [(0.07, sm * 1.05 + off), (0.045, sm * 0.92 + off), (0.022, sm * 0.78 + off)], 0.0005, 0.00012))
        # the wrist: the seam where the cuff's panel meets the hand, and the shirring gathered across the back of the wrist
        self.feats.append(("ring", self.P(0.0, 0.0, -0.001), self.f, 0.04, 0.0008, 0.0008))
        self.feats.append(("pleat", self.P(-0.014, 0.0, -0.001), self.f, self.b, 0.01, 11, 0.0002))

    def _chain(self, base, d, lengths, radii, curl, prefix, flat=0.88, build=True):
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
        if not build:
            return pts
        for i in range(len(pts) - 1):
            seg = (pts[i + 1] - pts[i]).normalized()
            up = (self.b - seg * self.b.dot(seg)).normalized()
            self.parts.append(capsule(f"{prefix}{i}", pts[i], pts[i + 1], radii[i], radii[i + 1] * 0.97, up=up, flat=flat, waist=0.93))
        return pts

    def finger(self, name, bones, s0, f0, r, curl, spread=0.0, seed=0):
        """A finger from its knuckle, each joint bent by curl[i] radians toward the palm; spread turns it about b.
        (2026-10-13d, the owner: "more realistic and less robot") One smooth swept skin from the knuckle to the rounded
        tip, not a bead of capsules: tapering, a soft swell over the back of each joint, fuller pads underneath, a little
        uneven."""
        base = self.P(f0, 0.001, s0)
        d = (Matrix.Rotation(spread, 3, self.b) @ self.f).normalized()
        rad = [r, r * 0.9, r * 0.82, r * 0.74]
        pts = self._chain(base, d, bones, rad, curl, name, build=False)
        self.parts.append(tube(name, pts, rad, self.b, seed=seed))
        for i in range(len(pts) - 1):
            seg = (pts[i + 1] - pts[i]).normalized()
            up = (self.b - seg * self.b.dot(seg)).normalized()
            self.joints.append((pts[i] + up * rad[i] * 0.9, 1.0 if i == 0 else 0.7))
        last = (pts[-1] - pts[-2]).normalized()
        up = (self.b - last * self.b.dot(last)).normalized()
        self.nails.append((pts[-1] - last * rad[-1] * 0.9 + up * rad[-1] * 0.78, last, up, rad[-1]))
        self.joints.append((pts[-1], 0.9))
        # the glove's creases at every joint (inside the bend, and the bunched wrinkles either side of the knuckle), and the
        # seams down the finger's two sides
        side = d.cross(self.b).normalized()
        for i in range(3):
            d_in = d if i == 0 else (pts[i] - pts[i - 1]).normalized()
            d_out = (pts[i + 1] - pts[i]).normalized()
            self.feats.append(("joint", pts[i], d_in, d_out, rad[i], i))
            self.feats.append(("side", pts[i], pts[i + 1], side, rad[i]))
        return pts

    def thumb(self, tip, out=0.012, base=None, via=None, rad=None):
        """From inside its fleshy base to `tip` (where it rests): the metacarpal out from the palm's side, the two
        bones bowed a little outward (away from the palm) between."""
        base = base if base is not None else self.P(0.02, -0.006, 0.014)
        rad = rad or [0.0145, 0.0126, 0.0112, 0.0099]
        span = tip - base
        L = span.length
        dirv = span / L
        bow = (self.s - dirv * self.s.dot(dirv)).normalized()
        pts = [base, base + span * 0.42 + bow * out * 1.1, base + span * 0.74 + bow * out, tip]
        if via is not None:   # (2026-10-13d: laid across the front of the fist: out past the first finger, then over it)
            pts = [base, via[0], via[1], tip]
        # (2026-10-13d: one smooth skin, as the fingers)
        self.parts.append(tube("Thumb", pts, rad, self.b, flat=0.86, seed=5))
        last = (pts[-1] - pts[-2]).normalized()
        up = (self.b - last * self.b.dot(last)).normalized()
        self.nails.append((pts[-1] - last * rad[-1] * 0.9 + up * rad[-1] * 0.8, last, up, rad[-1] * 1.05))
        self.joints.append((pts[2], 0.7))
        self.joints.append((pts[-1], 0.8))
        # the thumb's own seam round its base (the glove's thumb is a separate piece sewn in), its joints creased
        s0 = (pts[1] - pts[0]).normalized()
        self.feats.append(("ring", pts[0].lerp(pts[1], 0.5), s0, rad[0] * 1.9, 0.00018, 0.001))
        for i in (1, 2):
            self.feats.append(("joint", pts[i], (pts[i] - pts[i - 1]).normalized(), (pts[i + 1] - pts[i]).normalized(), rad[i], 1))
        return pts

    def forearm(self, toward, length, swell=1.0):
        """From the wrist back along `toward` (unit), swelling toward the elbow, flattened (wide across the back)."""
        cs, secs = [], []
        n = 8
        # (2026-10-13d) the glove's cuff on the lantern's hand: a bent wrist, not a straight tube meeting the hand at an
        # angle (whose rim stood out of the back of the wrist): it leaves the hand along the hand's own line, the size of
        # the hand's heel there, and bends round into the forearm's line over its first few centimetres
        tuck = swell < 1
        if tuck:
            n = 14
            c, d0 = self.w + self.f * 0.004, -self.f
        for i in range(n + 1):
            t = i / n
            if tuck:
                bend = min(1.0, t / 0.35)
                tan = d0.lerp(toward, bend * bend * (3 - 2 * bend)).normalized()
                if i > 0:
                    c = c + tan * (length / n)
                g = min(1.0, t / 0.4)
                w = 0.0262 + 0.0018 * g
                h = 0.0148 + 0.004 * g
            else:
                tan = toward
                c = self.w + self.f * 0.006 + toward * (length * t)
                w = 0.028 + 0.012 * math.sin(min(1, t * 1.3) * math.pi * 0.5)
                h = 0.02 + 0.012 * math.sin(min(1, t * 1.2) * math.pi * 0.5)
            u = (self.s - tan * self.s.dot(tan)).normalized()
            v = tan.cross(u).normalized()
            if v.dot(self.b) < 0:
                v = -v
            secs.append(lambda a, w=w, h=h, u=u, v=v: u * math.cos(a) * w + v * math.sin(a) * h)
            cs.append(c)
        self.parts.append(loft("Forearm", cs, secs, cap0=True, cap1=True))
        # (where a straight sleeve along `toward` should start from to sit round it: the forearm's end, back along its line)
        self.arm_root = cs[-1] - toward * length


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


def sleeve(name, wrist, toward, b, length, seed, cuff=0.041, swell=0.015, start=0.022, oval=1.0):
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
        c = wrist + toward * (start + length * t)
        base = cuff + swell * min(1, t * 2.2) + (0.004 if t < 0.06 else 0.0)
        gather = math.exp(-t * 4)   # (folds bunched near the cuff, easing off up the arm)
        def sec(a, t=t, base=base, gather=gather):
            fold = 0.0
            for k in range(3):
                fold += math.sin(a * (2 + k) + phase[k] + t * (14 + 5 * k)) * (0.0045 - 0.001 * k)
            fold += math.sin(t * 38 + phase[3] + a) * 0.004 * gather
            ry = (1.0 + 0.12 * math.sin(a * 2 + phase[4])) * (oval + (1 - oval) * min(1.0, t * 3))   # (oval: flat round the wrist at the cuff)
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


# (2026-10-13: a shade up from near black, so its shape reads in the dark; 2026-10-13d: browner, the body's dark-brown
# gloves, rubbed to a lighter tan where the leather's stretched)
GLOVE = V((0.042, 0.027, 0.018))
GLOVE_WORN = V((0.105, 0.068, 0.043))


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


def build(hand, name, sleeve_ob, out_name, carve=True):
    # (2026-10-13d: a finer skin, 0.7 mm, to carry the glove's creases and seams, carved into it after the remesh)
    skin = remesh(join(hand.parts, name + "Skin"), 0.0007 if carve else 0.0009, 12 if carve else 6)   # (well smoothed: where the parts meet, one surface, no seam between them)
    # (2026-10-11, the owner: gloves, dark: no nails; the cuff of the glove a band at the wrist)
    # (coloured by vertex, not baked: a bake from the dense copy onto this one scattered streaks over it; the vertex
    # colours are smooth, and there are enough of them for the knuckles, the palm, the nails)
    if carve:
        paint_glove(skin, hand, detail(skin, hand))
    else:
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
    for o, target in ((skin, 36000 if carve else 12000), (sleeve_ob, 4000)):
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
    hand.nails = [(a + d, b, c, r) for a, b, c, r in hand.nails]
    orient(hand, Matrix.Translation(d))   # (the parts, the frame, the joints and the glove's marks together)


# ------------------------------------------------------------------ the two grips

# ------------------------------------------------------------------ (2026-10-13d) the fist round a real grip

GRIP_R = 0.0085 / 1.1   # the lantern bail's wooden grip (HeldLantern: 0.0085 m), in the hand's own units (it's drawn at 1.1)


def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


def solve_curl(bones, r, target, gr=GRIP_R):
    """The three joint angles that wrap a finger round the grip: `target` the grip's centre in the finger's own plane (x
    along the finger's line from its knuckle, y toward the back of the hand). The middle and last bones lie against the
    grip, the first clears it, the tip curls in toward the palm; the last joint follows the middle one, as fingers do."""
    T = V((target[0], target[1]))
    rad = [r, r * 0.9, r * 0.82, r * 0.74]
    best, bc = None, 1e9
    for i1 in range(31):
        a1 = 0.2 + i1 * 0.05
        for i2 in range(31):
            a2 = 0.6 + i2 * 0.05
            for i3 in range(15):
                a3 = 0.3 + i3 * 0.07
                th, p, pts = 0.0, V((0.0, 0.0)), [V((0.0, 0.0))]
                for L, a in zip(bones, (a1, a2, a3)):
                    th += a
                    p = p + V((math.cos(th), -math.sin(th))) * L
                    pts.append(p)
                d0 = _seg_dist(T, pts[0], pts[1]) - (gr + rad[0])
                d1 = _seg_dist(T, pts[1], pts[2]) - (gr + rad[1])
                d2 = _seg_dist(T, pts[2], pts[3]) - (gr + rad[2])
                c = d1 * d1 * 4 + d2 * d2 * 2 + (min(0.0, d0) ** 2) * 20 + (max(0.0, d0 - 0.004) ** 2)
                c += (min(0.0, d1) ** 2 + min(0.0, d2) ** 2) * 30   # (never through the grip)
                c += ((a3 - 0.72 * a2) ** 2) * 5e-5   # (the last joint follows the middle one)
                if c < bc:
                    bc, best = c, (a1, a2, a3)
    return best, bc


def orient(hand, M):
    """Moves the whole hand (its parts, its frame, its marks) by M."""
    R = M.to_3x3()
    for o in hand.parts:
        o.data.transform(M)
    hand.w = M @ hand.w
    hand.f, hand.b, hand.s = (R @ hand.f).normalized(), (R @ hand.b).normalized(), (R @ hand.s).normalized()
    hand.joints = [(M @ p, k) for p, k in hand.joints]
    out = []
    for ft in hand.feats:
        k = ft[0]
        if k == "joint":
            out.append((k, M @ ft[1], R @ ft[2], R @ ft[3], ft[4], ft[5]))
        elif k == "side":
            out.append((k, M @ ft[1], M @ ft[2], R @ ft[3], ft[4]))
        elif k == "ring":
            out.append((k, M @ ft[1], R @ ft[2], ft[3], ft[4], ft[5]))
        elif k == "pleat":
            out.append((k, M @ ft[1], R @ ft[2], R @ ft[3], ft[4], ft[5], ft[6]))
        else:
            out.append(ft)   # (backline: in the hand's own (f, s), read through the moved frame)
    hand.feats = out


def _np(v):
    return np.array((v[0], v[1], v[2]))


def detail(ob, hand):
    """Carves the glove's creases and seams into the fused skin (along each vertex's normal), and returns how deep in a
    crease or seam each vertex lies (0..1) for the colour: leather darker down in its folds."""
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    nr = np.empty(n * 3)
    me.vertex_normals.foreach_get("vector", nr)
    nr = nr.reshape(-1, 3)
    disp = np.zeros(n)
    dark = np.zeros(n)

    def g(x, w):
        return np.exp(-(x / w) ** 2)

    def ring_frame(c, axis):
        ax = _np(axis) / np.linalg.norm(_np(axis))
        rel = co - _np(c)
        along = rel @ ax
        radial = rel - along[:, None] * ax
        rl = np.linalg.norm(radial, axis=1) + 1e-9
        return ax, along, radial / rl[:, None], rl

    W, F, S, B = _np(hand.w), _np(hand.f), _np(hand.s), _np(hand.b)
    for ft in hand.feats:
        k = ft[0]
        if k == "joint":
            _, p, d_in, d_out, r, idx = ft
            ax, along, dirn, rl = ring_frame(p, d_in + d_out)
            m = (rl < r * 1.75) & (np.abs(along) < r * 1.3)
            if not m.any():
                continue
            inner = _np(d_out - d_in)
            bent = np.linalg.norm(inner)
            if bent > 0.08:
                inner = inner - ax * (inner @ ax)
                inner /= np.linalg.norm(inner) + 1e-9
                inside = dirn @ inner
                # one soft crease, only right inside the bend (rings of wrinkles all round each joint read as a robot's
                # segmented finger)
                w_in = np.clip((inside - 0.35) / 0.4, 0, 1) ** 1.5
                cr = g(along, 0.0012) * w_in
                disp -= np.where(m, cr * 0.0003, 0.0)
                dark = np.maximum(dark, np.where(m, np.clip(cr * 0.8, 0, 1), 0))
        elif k == "side":
            _, a, b_, side, r = ft
            A, Bp = _np(a), _np(b_)
            d = Bp - A
            L = np.linalg.norm(d)
            d /= L
            t = (co - A) @ d
            radial = co - A - t[:, None] * d
            rl = np.linalg.norm(radial, axis=1) + 1e-9
            sc = np.abs((radial / rl[:, None]) @ _np(side))
            m = (rl < r * 1.6) & (t > 0) & (t < L)
            w = g(1 - sc, 0.02) * m
            disp -= w * 0.00028
            dark = np.maximum(dark, w * 0.8)
        elif k == "ring":
            _, c, axis, rmax, depth, width = ft
            ax, along, dirn, rl = ring_frame(c, axis)
            w = g(along, width) * (rl < rmax)
            disp -= w * depth
            dark = np.maximum(dark, w * 0.9)
        elif k == "pleat":
            _, c, axis, up, half, count, amp = ft
            ax, along, dirn, rl = ring_frame(c, axis)
            upv = _np(up) - ax * (_np(up) @ ax)
            upv /= np.linalg.norm(upv)
            sd = np.cross(ax, upv)
            phi = np.arctan2(dirn @ sd, dirn @ upv)
            win = np.clip(1 - (along / half) ** 2, 0, 1) * np.clip((1.9 - np.abs(phi)) / 0.6, 0, 1) * (rl < 0.045)
            wave = np.cos(phi * count * 2)
            disp += wave * amp * win
            dark = np.maximum(dark, np.clip(-wave, 0, 1) * win * 0.7)
        elif k == "backline":
            _, pts2, width, depth = ft
            rel = co - W
            fs = np.stack((rel @ F, rel @ S), axis=1)
            best = np.full(n, 1e9)
            for (fa, sa), (fb, sb) in zip(pts2, pts2[1:]):
                a2, b2 = np.array((fa, sa)), np.array((fb, sb))
                ab = b2 - a2
                t = np.clip(((fs - a2) @ ab) / (ab @ ab), 0, 1)
                best = np.minimum(best, np.linalg.norm(fs - (a2 + t[:, None] * ab), axis=1))
            m = (nr @ B > 0.35) & (rel @ B > 0)
            w = g(best, width) * m
            disp -= w * depth
            if depth > 0:
                dark = np.maximum(dark, w * 0.8)
    co += nr * disp[:, None]
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    return dark


def paint_glove(ob, hand, dark):
    """Worn dark-brown leather: a little uneven, rubbed paler where it's stretched over the knuckles and the fingertips,
    darker down in its creases and seams and between the fingers."""
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    nr = np.empty(n * 3)
    me.vertex_normals.foreach_get("vector", nr)
    nr = nr.reshape(-1, 3)
    # how convex each vertex is (its neighbours below its normal: stretched; above: in a hollow)
    ev = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", ev)
    ev = ev.reshape(-1, 2)
    acc = np.zeros((n, 3))
    cnt = np.zeros(n)
    np.add.at(acc, ev[:, 0], co[ev[:, 1]])
    np.add.at(acc, ev[:, 1], co[ev[:, 0]])
    np.add.at(cnt, ev[:, 0], 1)
    np.add.at(cnt, ev[:, 1], 1)
    lap = acc / np.maximum(cnt, 1)[:, None] - co
    curv = -(lap * nr).sum(1)
    for _ in range(4):   # (smoothed over its neighbours: broad wear, not speckle)
        s = np.zeros(n)
        np.add.at(s, ev[:, 0], curv[ev[:, 1]])
        np.add.at(s, ev[:, 1], curv[ev[:, 0]])
        curv = 0.5 * curv + 0.5 * s / np.maximum(cnt, 1)
    sc = np.percentile(np.abs(curv), 95) + 1e-12
    curv = np.clip(curv / sc, -1, 1)
    # a slow mottle through the leather
    x, y, z = co[:, 0] * 160, co[:, 1] * 160, co[:, 2] * 160
    mott = (np.sin(x * 1.3 + np.sin(y * 0.7)) * np.sin(y * 1.1 + 1.7) * np.sin(z * 0.9 + np.sin(x * 0.5)) + 0.5 * np.sin(x * 3.1 + z * 2.3) * np.sin(y * 2.7)) * 0.5
    base = _np(GLOVE)[None, :] * (0.9 + 0.16 * mott)[:, None]
    wear = np.clip(curv - 0.15, 0, 1) ** 0.8
    for p, kk in hand.joints:   # (the knuckles and the fingertips rubbed most)
        d = np.linalg.norm(co - _np(p), axis=1)
        wear = np.maximum(wear, np.clip(1 - d / 0.013, 0, 1) * kk * 0.85)
    col = base + (_np(GLOVE_WORN)[None, :] - base) * (wear * 0.75)[:, None]
    hollow = np.clip(-curv - 0.1, 0, 1)
    col *= (1 - 0.45 * np.maximum(dark, hollow * 0.8))[:, None]
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.concatenate((col, np.ones((n, 1))), axis=1).ravel())
    me.color_attributes.active_color = attr


# ------------------------------------------------------------------ (2026-10-13d) the hand's poses

# Every pose is built as the LEFT hand (the game mirrors it for the right: FirstPersonHand.cs). Its origin is the pose's
# socket: where the held thing's grip sits, in the frame the game holds it in (x right, y up, z back toward the eye).
#   grip    the grip's radius (in the hand's own units; it's drawn at 1.1), None for no grip
#   axis    which way the grip runs through the socket (the thumb's side of the hand toward it)
#   back    which way the back of the hand faces
#   thumb   "over" (closed over the first two fingers), "along" (laid up the grip, opposite the fingers: a camera's side),
#           "relaxed"
#   toward  which way the forearm leaves the wrist (to the elbow)
POSES = {
    # the lantern's bail grip: a fist round a horizontal handle, the back of the hand up and a little toward the eye
    "handle": dict(grip=GRIP_R, axis=G(1, 0, 0), back=G(0.0, 1.0, 0.3), thumb="over", toward=G(-0.12, -0.68, 0.72),
                   eye=G(0.209, 0.064, 0.582)),
    # a stick held upright (a flare, a tool's handle): knuckles forward, the back of the hand out to the side
    "stick": dict(grip=0.016 / 1.1, axis=G(0, 1, 0), back=G(-0.8, 0.0, -0.45), thumb="over", toward=G(-0.3, -0.55, 0.78),
                  eye=G(0.2, 0.12, 0.5)),
    # one end of a camera (or any box) held from the side: the fingers round its front edge, the thumb up its back
    "camera": dict(grip=0.021 / 1.1, axis=G(0, 1, 0), back=G(-0.9, 0.0, -0.3), thumb="along", toward=G(-0.3, -0.55, 0.78),
                   eye=G(0.2, 0.08, 0.45)),
    # open and at ease: the fingers gently curled, each a little more than the one before, the thumb loose beside them
    "relaxed": dict(grip=None, axis=G(0.35, 0.9, 0.0), back=G(-1.0, 0.0, -0.4), thumb="relaxed", toward=G(-0.25, -0.6, 0.76),
                    eye=G(0.2, 0.12, 0.5)),
}
RELAXED_CURL = {"index": (0.28, 0.42, 0.22), "middle": (0.34, 0.5, 0.28), "ring": (0.42, 0.56, 0.32), "little": (0.52, 0.64, 0.38)}


def make_hand(pose):
    """(2026-10-13d, the owner: "it still looks a bit blocky", then "make the players hand look more realistic and less
    robot") A gloved left hand in one of POSES, built round its grip: each finger's three bones bent (solve_curl) so the
    middle and last bones lie against the grip and the tip curls in toward the palm; the grip running a little across the
    fingers, deeper toward the little finger (which curls tightest), the way a hand takes a handle. Every finger one smooth
    skin (tube), the back of the hand domed and narrowing to the wrist, the knuckles a row of soft bumps; the glove's
    creases, seams and wear carved and painted on after (detail, paint_glove). Built in the hand's own frame, then turned
    into the game's: the socket at the origin."""
    P_ = POSES[pose]
    # (the hand's own frame: f toward the knuckles, b out of the back of the hand, s across toward the thumb)
    hand = Hand(V((0, 0, 0)), V((1, 0, 0)), V((0, 0, 1)), right=False)
    hand.palm_fist()
    gr = P_["grip"]
    fingers = {}
    spreads = (0.03, 0.0, -0.035, -0.08)
    if gr is not None:
        gamma = 0.14   # (the grip across the fingers: further back toward the little finger)
        gdir = (hand.s * math.cos(gamma) + hand.f * math.sin(gamma)).normalized()
        T0 = hand.P(0.088, -0.0155 - gr, 0.0)
        for idx, ((name, bones, s0, f0, r), spread) in enumerate(zip(FINGERS, spreads)):
            d = (Matrix.Rotation(spread, 3, hand.b) @ hand.f).normalized()
            base = hand.P(f0, 0.001, s0)
            Ti = T0 + gdir * (s0 / math.cos(gamma))
            if name == "little":   # (the little finger curled tighter, in toward the heel of the hand)
                Ti = Ti - hand.f * 0.003 + hand.b * 0.002
            rel = Ti - base
            curl, cost = solve_curl(bones, r, (rel.dot(d), rel.dot(hand.b)), gr)
            print(f"[fp-arm] {pose} {name}: curl {tuple(round(a, 2) for a in curl)} cost {cost:.2e}")
            fingers[name] = hand.finger(name, bones, s0, f0, r, curl, spread, seed=idx)
        Tc = T0 + gdir * ((FINGERS[0][2] + FINGERS[3][2]) * 0.5)

        def outside(nm, k, gap):
            s0 = dict((n_, s_) for n_, _, s_, _, _ in FINGERS)[nm]
            Ti = T0 + gdir * (s0 / math.cos(gamma))
            p = (fingers[nm][k] + fingers[nm][k + 1]) * 0.5
            return p + (p - Ti - gdir * (p - Ti).dot(gdir)).normalized() * gap
    else:
        for idx, ((name, bones, s0, f0, r), spread) in enumerate(zip(FINGERS, (0.06, 0.0, -0.06, -0.13))):
            fingers[name] = hand.finger(name, bones, s0, f0, r, RELAXED_CURL[name], spread, seed=idx)
        gdir = hand.s
        Tc = hand.P(0.05, -0.022, -0.002)   # (the socket: the middle of the palm)
    # the thumb: its root low on the palm's side, at its true lengths (the metacarpal, then its two bones)
    tb = hand.P(0.026, -0.013, 0.02)
    if P_["thumb"] == "over":
        # out beside the first finger's first bone, then across the front of its middle bone, the tip on the middle finger's
        via0 = (fingers["index"][0] + fingers["index"][1]) * 0.5 + hand.s * 0.021 - hand.b * 0.008
        via1 = outside("index", 1, 0.02) + hand.s * 0.01
        tip = outside("middle", 1, 0.019) + hand.s * 0.002
    elif P_["thumb"] == "along":
        # up the grip on the palm's side, opposite the fingers, a little bent at its last joint
        cT = T0 + gdir * ((FINGERS[0][2] + 0.03) / math.cos(gamma))   # (the grip just past the first finger)
        via0 = tb + hand.s * 0.024 + hand.f * 0.024 - hand.b * 0.014
        tip = cT - hand.f * (gr + 0.009) + hand.b * 0.002
        via1 = via0.lerp(tip, 0.55) - hand.f * 0.006 - hand.b * 0.004
    else:
        via0 = tb + hand.f * 0.026 + hand.s * 0.022 - hand.b * 0.014
        via1 = via0 + hand.f * 0.024 + hand.s * 0.006 - hand.b * 0.01
        tip = via1 + hand.f * 0.018 - hand.s * 0.002 - hand.b * 0.009
    via0 = tb + (via0 - tb).normalized() * 0.036
    via1 = via0 + (via1 - via0).normalized() * 0.029
    tip = via1 + (tip - via1).normalized() * 0.023
    hand.thumb(tip, base=tb, via=(via0, via1), rad=[0.0132, 0.0117, 0.0107, 0.0097])
    # turned into the game's frame: the grip (or for the open hand, the thumb's side) along the pose's axis, the back of the
    # hand as near its way as that allows; the socket at the origin
    A = P_["axis"].normalized()
    back = P_["back"]
    back = (back - A * back.dot(A)).normalized()
    src = Matrix((gdir, hand.b, gdir.cross(hand.b))).transposed()
    dst = Matrix((A, back, A.cross(back))).transposed()
    M = (dst @ src.transposed()).to_4x4() @ Matrix.Translation(-Tc)
    orient(hand, M)
    gf = V((hand.f.x, hand.f.z, -hand.f.y))
    print(f"[fp-arm] {pose}: knuckles toward {tuple(round(x, 2) for x in gf)} (game)")
    # (the owner: "less bendy at the wrist and more strong": the forearm nearly in line with the hand, a firm small angle)
    toward = (-hand.f).lerp(P_["toward"].normalized(), 0.4).normalized()
    hand.forearm(toward, 0.12, swell=0.2)   # (the glove's cuff: under the sleeve from 4 cm on)
    sl = sleeve("Sleeve", hand.arm_root, toward, hand.b, 0.42, 7, cuff=0.034, swell=0.011, start=0.042, oval=0.74)
    return build(hand, "Hand_" + pose, sl, "fp_hand_L_" + pose, carve=True)


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
    for tag, at, look, lens in (("eye", eye, G(0, 0, 0), 35), ("eyeclose", eye, G(0, 0.005, 0.02), 130),
                                ("side", G(0.0, 0.12, 0.0) + V((0.32, 0.06, 0.06)), G(0, 0.02, 0.05), 35),
                                ("front", G(-0.12, 0.06, -0.28), G(0, 0.0, 0.03), 60),
                                ("close", G(0, 0.07, 0.0) + V((0.1, -0.14, 0.1)), G(0, 0.02, 0.02), 35)):
        cam.location = at
        cam.rotation_euler = (look - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        cam.data.clip_start = 0.005
        sc.render.filepath = os.path.join(PREV, f"fp_arm_{name}_{tag}.png")
        bpy.ops.render.render(write_still=True)


SHAPE = "--" in sys.argv and "shape" in sys.argv[sys.argv.index("--") + 1:]


def main():
    # (`-- handle stick` builds only those poses; all of them by default)
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for pose in [p for p in POSES if not [a for a in args if a in POSES] or p in args]:
        clear()
        ob = make_hand(pose)
        P_ = POSES[pose]
        if P_["grip"] is not None:   # (the grip in the previews only)
            rot = (0, math.pi / 2, 0) if abs(P_["axis"].x) > 0.5 else (0, 0, 0)
            bpy.ops.mesh.primitive_cylinder_add(radius=P_["grip"], depth=0.08 / 1.1, location=(0, 0, 0), rotation=rot)
        preview(ob, "L_" + pose, P_["eye"])   # (the eye, in the hand's own units)


main()
