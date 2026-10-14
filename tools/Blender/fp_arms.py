"""The gloved hands seen in first person.

    blender -b --python tools/Blender/fp_arms.py [-- handle stick camera relaxed wide small] [-- fast]

Writes assets/models/player/fp_hand_L_<pose>.glb (and its baked normal map, embedded), one per pose in POSES, and
previews to build/blender/fp_arm_L_<pose>_*.png. Every pose is the LEFT hand, its origin the pose's socket (the grip's
centre); the game mirrors it for the right (scripts/player/FirstPersonHand.cs):
  handle    a fist round a horizontal grip along X (the lantern's bail grip), the thumb over the fingers
  stick     a fist round an upright grip along Y (a flare, a tool's handle)
  camera    one end of a box held from the side, the grip along Y: fingers round its front, the thumb up its back
  relaxed   open, the fingers gently curled; the socket the middle of the palm
  wide      a hand cradling a thick upright neck (the newel cap's, 7 cm): fingers half round it, the thumb opposite
  small     a tight fist round a small upright thing (the lighter)

History: 2026-10-11 the first modelled arms; 2026-10-13d a fist round a real grip, a strong wrist, one hand for every
pose (a swept tube per finger, fused by a voxel remesh). (2026-10-14e, the owner: "hands still look like skeletor robot
hands") Rebuilt as a sculpt is, not a kit of tubes: the hand is one signed-distance field. Each bone is a rounded cone at
a real man's proportions (a palm as wide as it is long, short thick fingers, a heavy thumb), the flesh of the palm (the
thumb's ball, the heel of the hand, the pad under the fingers) ellipsoids blended in with wide smooth unions, the fingers
pressed side by side with no gaps, the knuckles soft bumps; then the whole hand inflated by the glove's thickness (3 mm of
worn leather, a little baggy), and the glove's gauntlet cuff flaring back over the sleeve, part of the same skin. The
field is meshed (surface nets), the glove's folds (bunched inside every bend and across the palm), wrinkles over the
knuckles, seams and fingertip caps are carved into that dense mesh, and its detail (and the leather's pebbled grain) is
baked into a normal map on a ~16k-triangle copy, with the ambient occlusion baked into its vertex colours.

Coordinates: the game's camera space (x right, y up, z back toward the eye), converted to Blender's (x, -z, y).
"""
import bpy, bmesh, math, os, sys
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, apply_mods, add_mod, ROOT

OUT = os.path.join(ROOT, "assets", "models", "player")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREV, exist_ok=True)
V = Vector
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FAST = "fast" in ARGS          # (coarser field and bake, for quick looks)
VOX = 0.0009 if FAST else 0.0006   # the field's lattice (hand units; drawn at 1.1); the mesh is subdivided once after
TEX = 512 if FAST else 1024
LOW_TRIS = 16000
GLOVE_T = 0.0029               # the glove's leather and lining over the bare hand (3.2 mm as drawn)


def G(x, y, z):
    return V((x, -z, y))


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# ------------------------------------------------------------------ the field

def _a(v):
    return np.array((v[0], v[1], v[2]), dtype=np.float32)


def smin(a, b, k):
    """Smooth union (polynomial): the two meet in a fillet about k wide."""
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(k - np.abs(a - b), 0, None) / k
    return np.minimum(a, b) - h * h * k * 0.25


def smax(a, b, k):
    return -smin(-a, -b, k)


def sd_round_cone(P, a, b, r1, r2):
    """Exact distance to a cone from a (radius r1) to b (radius r2), rounded at both ends (Quilez)."""
    a, b = _a(a), _a(b)
    ba = b - a
    l2 = float(ba @ ba)
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    pa = P - a
    y = pa @ ba
    z = y - l2
    xv = pa * l2 - y[..., None] * ba
    x2 = np.einsum("...i,...i->...", xv, xv)
    y2 = y * y * l2
    z2 = z * z * l2
    k = math.copysign(1.0, rr) * rr * rr * x2
    d_mid = (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - r1
    d_b = np.sqrt(x2 + z2) * il2 - r2
    d_a = np.sqrt(x2 + y2) * il2 - r1
    return np.where(np.sign(z) * a2 * z2 > k, d_b, np.where(np.sign(y) * a2 * y2 < k, d_a, d_mid))


def sd_oval_cone(P, a, b, r1, r2, up, squash):
    """A rounded cone whose sections are ovals: `squash` (<1) of its width toward `up`."""
    a3, b3 = V(a), V(b)
    d = (b3 - a3).normalized()
    u = (up - d * up.dot(d)).normalized()
    s = 1.0 / squash
    pa = P - _a(a3)
    along = pa @ _a(d)
    upc = pa @ _a(u)
    Q = pa + (upc * (s - 1.0))[..., None] * _a(u)   # (the up component stretched: the oval made round)
    return sd_round_cone(Q + _a(a3), a3, b3, r1, r2) * squash


def sd_ellipsoid(P, c, axes, radii):
    """Near-exact distance to an ellipsoid at c, its three (unit) axes and radii."""
    rel = P - _a(c)
    q = np.stack([rel @ _a(ax) for ax in axes], axis=-1)
    r = np.array(radii, dtype=np.float32)
    k0 = np.linalg.norm(q / r, axis=-1)
    k1 = np.linalg.norm(q / (r * r), axis=-1)
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)


def noise3(P, freq, seed):
    """A cheap smooth 3D noise (summed rotated sines), about -1..1."""
    rng = np.random.default_rng(seed)
    out = np.zeros(P.shape[:-1], dtype=np.float32)
    for i in range(5):
        d = rng.normal(size=3)
        d /= np.linalg.norm(d)
        f = freq * (1.0 + 0.37 * i)
        out += np.sin((P @ d.astype(np.float32)) * f + rng.uniform(0, math.tau)) / (1.0 + 0.5 * i)
    return out / 2.2


class Field:
    """A signed-distance field on a lattice: primitives are blended in only over their own bounds (+ the blend)."""

    def __init__(self, lo, hi, h):
        self.h = h
        self.o = np.floor(np.array(lo) / h) * h
        n = np.ceil((np.array(hi) - self.o) / h).astype(int) + 1
        self.F = np.full(tuple(n), 1.0, dtype=np.float32)
        print(f"[fp-arm] field {tuple(n)} ({self.F.size / 1e6:.1f}M points)")

    def _sub(self, lo, hi):
        i0 = np.clip(np.floor((np.array(lo) - self.o) / self.h).astype(int), 0, None)
        i1 = np.minimum(np.ceil((np.array(hi) - self.o) / self.h).astype(int) + 1, self.F.shape)
        if np.any(i1 <= i0):
            return None, None
        sl = tuple(slice(a, b) for a, b in zip(i0, i1))
        ax = [self.o[d] + np.arange(i0[d], i1[d], dtype=np.float32) * self.h for d in range(3)]
        X, Y, Z = np.meshgrid(*ax, indexing="ij")
        return sl, np.stack((X, Y, Z), axis=-1).astype(np.float32)

    def add(self, fn, lo, hi, k, op="union"):
        lo = np.array(lo) - k - self.h * 2
        hi = np.array(hi) + k + self.h * 2
        sl, P = self._sub(lo, hi)
        if sl is None:
            return
        d = fn(P)
        if op == "union":
            self.F[sl] = smin(self.F[sl], d, k)
        else:
            self.F[sl] = smax(self.F[sl], -d, k)

    def merge(self, other, k):
        """Blends another field (on the same lattice) into this one."""
        off = np.round((other.o - self.o) / self.h).astype(int)
        a0 = np.maximum(off, 0)
        a1 = np.minimum(off + np.array(other.F.shape), self.F.shape)
        if np.any(a1 <= a0):
            return
        sl = tuple(slice(a, b) for a, b in zip(a0, a1))
        so = tuple(slice(a - o, b - o) for a, b, o in zip(a0, a1, off))
        self.F[sl] = smin(self.F[sl], other.F[so], k)

    def mesh(self, name):
        """Surface nets: a vertex in every cell the surface crosses (the mean of its edges' crossings), a quad across every
        lattice edge it crosses. A clean, even mesh of the zero set."""
        F = self.F
        s = F < 0
        nx, ny, nz = F.shape
        anyin = np.zeros((nx - 1, ny - 1, nz - 1), bool)
        allin = np.ones((nx - 1, ny - 1, nz - 1), bool)
        corners = [(i, j, k) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
        for (i, j, k) in corners:
            c = s[i:nx - 1 + i, j:ny - 1 + j, k:nz - 1 + k]
            anyin |= c
            allin &= c
        act = anyin & ~allin
        ci = np.argwhere(act)
        vid = np.full(act.shape, -1, np.int64)
        vid[act] = np.arange(len(ci))
        acc = np.zeros((len(ci), 3), np.float64)
        cnt = np.zeros(len(ci), np.float64)
        cv = {c: F[ci[:, 0] + c[0], ci[:, 1] + c[1], ci[:, 2] + c[2]] for c in corners}
        for a in corners:
            for ax in range(3):
                if a[ax] == 1:
                    continue
                b = list(a)
                b[ax] = 1
                b = tuple(b)
                fa, fb = cv[a], cv[b]
                m = (fa < 0) != (fb < 0)
                t = fa[m] / (fa[m] - fb[m])
                p = np.array(a, np.float64)[None, :].repeat(m.sum(), 0)
                p[:, ax] += t
                acc[m] += p
                cnt[m] += 1
        verts = (ci + acc / cnt[:, None]) * self.h + self.o
        quads = []
        for ax in range(3):
            o1, o2 = [d for d in range(3) if d != ax]
            sh = [nx, ny, nz]
            sh[ax] -= 1
            sa = s[tuple(slice(0, sh[d]) for d in range(3))]
            sb = s[tuple(slice(1, None) if d == ax else slice(0, sh[d]) for d in range(3))]
            e = np.argwhere(sa != sb)
            e = e[(e[:, o1] >= 1) & (e[:, o2] >= 1) & (e[:, o1] <= F.shape[o1] - 2) & (e[:, o2] <= F.shape[o2] - 2)]
            flip = sa[tuple(e.T)]
            cells = []
            for d1, d2 in ((1, 1), (0, 1), (0, 0), (1, 0)):
                c = e.copy()
                c[:, o1] -= d1
                c[:, o2] -= d2
                cells.append(vid[tuple(c.T)])
            q = np.stack(cells, 1)
            q[flip] = q[flip][:, ::-1]
            quads.append(q)
        quads = np.concatenate(quads)
        quads = quads[(quads >= 0).all(1)]
        me = bpy.data.meshes.new(name)
        me.from_pydata(verts.tolist(), [], quads.tolist())
        me.validate()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = True
        print(f"[fp-arm] {name}: {len(verts)} vertices, {len(quads)} quads")
        return ob


# ------------------------------------------------------------------ the hand's skeleton

# A man's hand (hand units; the game draws it at 1.1): per finger its three bones (proximal, middle, distal: the distal
# to the tip's end), its knuckle across the palm (s) and along it from the wrist (f), and its radius at the knuckle.
FINGERS = [
    ("index", (0.043, 0.025, 0.020), 0.0285, 0.090, 0.0099),
    ("middle", (0.047, 0.029, 0.021), 0.0090, 0.094, 0.0102),
    ("ring", (0.044, 0.027, 0.020), -0.0105, 0.090, 0.0097),
    ("little", (0.035, 0.020, 0.018), -0.0280, 0.081, 0.0087),
]
TAPER = (1.0, 0.93, 0.87, 0.83)   # (the radius at each joint and the tip, of the knuckle's: thick to the end, a glove's)


class Hand:
    """A hand in its own frame: origin the wrist joint; f toward the knuckles, b out of the back of the hand, s toward
    the thumb (a left hand)."""

    def __init__(self):
        self.w = V((0, 0, 0))
        self.f, self.b = V((1, 0, 0)), V((0, 0, 1))
        self.s = self.f.cross(self.b).normalized()
        self.fingers = {}    # name -> joint points (knuckle, two joints, tip)
        self.rads = {}       # name -> radii at those points
        self.thumb = None

    def P(self, f, b, s):
        return self.w + self.f * f + self.b * b + self.s * s


def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


def solve_curl(bones, r, target, gr):
    """The three joint angles that wrap a (gloved) finger round the grip: `target` the grip's centre in the finger's own
    plane (x along the finger's line from its knuckle, y toward the back of the hand). The middle and last bones lie
    against the grip, the first clears it; the last joint follows the middle one, as fingers do."""
    T = V((target[0], target[1]))
    rad = [r * t for t in TAPER]
    best, bc = None, 1e9
    for i1 in range(31):
        a1 = 0.1 + i1 * 0.05
        for i2 in range(31):
            a2 = 0.5 + i2 * 0.05
            for i3 in range(15):
                a3 = 0.2 + i3 * 0.07
                th, p, pts = 0.0, V((0.0, 0.0)), [V((0.0, 0.0))]
                for L, a in zip(bones, (a1, a2, a3)):
                    th += a
                    p = p + V((math.cos(th), -math.sin(th))) * L
                    pts.append(p)
                d0 = _seg_dist(T, pts[0], pts[1]) - (gr + rad[0])
                d1 = _seg_dist(T, pts[1], pts[2]) - (gr + rad[1])
                d2 = _seg_dist(T, pts[2], pts[3]) - (gr + rad[2])
                c = d1 * d1 * 4 + d2 * d2 * 2 + (min(0.0, d0) ** 2) * 20 + (max(0.0, d0 - 0.003) ** 2)
                c += (min(0.0, d1) ** 2 + min(0.0, d2) ** 2) * 30   # (never through the grip)
                c += ((a3 - 0.72 * a2) ** 2) * 5e-5
                if c < bc:
                    bc, best = c, (a1, a2, a3)
    return best, bc


def chain(hand, base, d, lengths, curl):
    side = d.cross(hand.b).normalized()
    test = Matrix.Rotation(0.3, 3, side) @ d
    sign = 1.0 if test.dot(hand.b) < d.dot(hand.b) else -1.0
    pts, p, bend = [base], base, 0.0
    for L, ang in zip(lengths, curl):
        bend += ang
        p = p + (Matrix.Rotation(sign * bend, 3, side) @ d).normalized() * L
        pts.append(p)
    return pts


# ------------------------------------------------------------------ the poses

# Each pose's socket is where the held thing's grip sits, in the frame the game holds it in (x right, y up, z back).
#   grip    the grip's radius as drawn (metres; the hand is built at 1/1.1), None for none
#   axis    which way the grip runs through the socket (the thumb's side of the hand toward it)
#   back    which way the back of the hand faces
#   thumb   "over" (closed over the first two fingers), "along" (up the grip opposite the fingers), "relaxed"
#   toward  which way the forearm leaves the wrist (to the elbow)
POSES = {
    "handle": dict(grip=0.0085, axis=G(1, 0, 0), back=G(0.0, 1.0, 0.3), thumb="over", toward=G(-0.12, -0.68, 0.72),
                   eye=G(0.209, 0.064, 0.582)),
    "stick": dict(grip=0.016, axis=G(0, 1, 0), back=G(-0.8, 0.0, -0.45), thumb="over", toward=G(-0.3, -0.55, 0.78),
                  eye=G(0.2, 0.12, 0.5)),
    "camera": dict(grip=0.021, axis=G(0, 1, 0), back=G(-0.9, 0.0, -0.3), thumb="along", toward=G(-0.3, -0.55, 0.78),
                   eye=G(0.2, 0.08, 0.45)),
    "relaxed": dict(grip=None, axis=G(0.35, 0.9, 0.0), back=G(-1.0, 0.0, -0.4), thumb="relaxed", toward=G(-0.25, -0.6, 0.76),
                    eye=G(0.2, 0.12, 0.5)),
    "wide": dict(grip=0.036, axis=G(0, 1, 0), back=G(-0.8, 0.0, -0.45), thumb="along", toward=G(-0.3, -0.55, 0.78),
                 eye=G(0.2, 0.12, 0.5)),
    "small": dict(grip=0.0095, axis=G(0, 1, 0), back=G(-0.8, 0.0, -0.45), thumb="over", toward=G(-0.3, -0.55, 0.78),
                  eye=G(0.2, 0.12, 0.5)),
}
RELAXED_CURL = {"index": (0.30, 0.45, 0.25), "middle": (0.36, 0.52, 0.3), "ring": (0.44, 0.58, 0.34), "little": (0.54, 0.66, 0.4)}


def skeleton(pose):
    """The pose's joints in the hand's own frame, and the transform into the game's (socket at the origin)."""
    P_ = POSES[pose]
    hand = Hand()
    gr = None if P_["grip"] is None else P_["grip"] / 1.1
    gT = GLOVE_T * 0.8   # (the glove's leather between finger and grip: a gloved finger is that much thicker)
    if gr is not None:
        gamma = 0.14   # (the grip runs a little across the fingers, further back toward the little finger)
        gdir = (hand.s * math.cos(gamma) + hand.f * math.sin(gamma)).normalized()
        T0 = hand.P(0.088, -0.0165 - gr - gT * 0.6, 0.0)
        for (name, bones, s0, f0, r), spread in zip(FINGERS, (0.02, 0.0, -0.025, -0.06)):
            d = (Matrix.Rotation(spread, 3, hand.b) @ hand.f).normalized()
            base = hand.P(f0, 0.0, s0)
            Ti = T0 + gdir * (s0 / math.cos(gamma))
            if name == "little":
                Ti = Ti - hand.f * 0.003 + hand.b * 0.002
            rel = Ti - base
            curl, cost = solve_curl(bones, r + gT, (rel.dot(d), rel.dot(hand.b)), gr)
            print(f"[fp-arm] {pose} {name}: curl {tuple(round(a, 2) for a in curl)} cost {cost:.2e}")
            hand.fingers[name] = chain(hand, base, d, bones, curl)
            hand.rads[name] = [r * t for t in TAPER]
        Tc = T0 + gdir * ((FINGERS[0][2] + FINGERS[3][2]) * 0.5)

        def outside(nm, k, gap):
            s0 = dict((n_, s_) for n_, _, s_, _, _ in FINGERS)[nm]
            Ti = T0 + gdir * (s0 / math.cos(gamma))
            p = (hand.fingers[nm][k] + hand.fingers[nm][k + 1]) * 0.5
            return p + (p - Ti - gdir * (p - Ti).dot(gdir)).normalized() * gap
    else:
        for (name, bones, s0, f0, r), spread in zip(FINGERS, (0.05, 0.0, -0.045, -0.1)):
            d = (Matrix.Rotation(spread, 3, hand.b) @ hand.f).normalized()
            hand.fingers[name] = chain(hand, hand.P(f0, 0.0, s0), d, bones, RELAXED_CURL[name])
            hand.rads[name] = [r * t for t in TAPER]
        gdir = hand.s
        Tc = hand.P(0.05, -0.024, -0.002)
    # the thumb: from its root low on the palm's side (deep in its ball), its metacarpal, then its two bones
    tb = hand.P(0.022, -0.012, 0.021)
    if P_["thumb"] == "over":
        # out beside the first finger's first bone, then across the front of its middle bone, the tip on the middle finger
        via0 = (hand.fingers["index"][0] + hand.fingers["index"][1]) * 0.5 + hand.s * 0.024 - hand.b * 0.01
        via1 = outside("index", 1, 0.022) + hand.s * 0.008
        tip = outside("middle", 1, 0.021)
    elif P_["thumb"] == "along":
        # up the grip on the palm's side, opposite the fingers
        cT = T0 + gdir * ((FINGERS[0][2] + 0.012) / math.cos(gamma))
        via0 = tb + hand.s * 0.026 + hand.f * 0.024 - hand.b * 0.016
        tip = cT - hand.f * (gr + 0.0115) + hand.b * 0.002
        via1 = via0.lerp(tip, 0.55) - hand.f * 0.006 - hand.b * 0.004
    else:
        via0 = tb + hand.f * 0.026 + hand.s * 0.024 - hand.b * 0.014
        via1 = via0 + hand.f * 0.024 + hand.s * 0.006 - hand.b * 0.01
        tip = via1 + hand.f * 0.018 - hand.s * 0.002 - hand.b * 0.009
    via0 = tb + (via0 - tb).normalized() * 0.040
    via1 = via0 + (via1 - via0).normalized() * 0.031
    tip = via1 + (tip - via1).normalized() * 0.026
    hand.thumb = [tb, via0, via1, tip]
    hand.thumb_r = [0.0135, 0.0118, 0.0109, 0.0098]
    # into the game's frame: the grip (or the thumb's side, open) along the pose's axis, the back of the hand its way
    A = P_["axis"].normalized()
    back = (P_["back"] - A * P_["back"].dot(A)).normalized()
    src = Matrix((gdir, hand.b, gdir.cross(hand.b))).transposed()
    dst = Matrix((A, back, A.cross(back))).transposed()
    M = (dst @ src.transposed()).to_4x4() @ Matrix.Translation(-Tc)
    R = M.to_3x3()
    hand.w = M @ hand.w
    hand.f, hand.b, hand.s = (R @ hand.f).normalized(), (R @ hand.b).normalized(), (R @ hand.s).normalized()
    hand.fingers = {k: [M @ p for p in v] for k, v in hand.fingers.items()}
    hand.thumb = [M @ p for p in hand.thumb]
    # (the owner: "less bendy at the wrist and more strong": the forearm nearly in line with the hand)
    hand.toward = (-hand.f).lerp(P_["toward"].normalized(), 0.4).normalized()
    return hand


# ------------------------------------------------------------------ the gloved hand as a field

def build_field(hand):
    """The bare hand's bones and flesh, smooth-blended; inflated by the glove; its gauntlet cuff."""
    f, b, s = hand.f, hand.b, hand.s
    axes = (f, b, s)
    pts = [p for v in hand.fingers.values() for p in v] + hand.thumb + [hand.w]
    lo = np.min([[p.x, p.y, p.z] for p in pts], 0) - 0.03
    hi = np.max([[p.x, p.y, p.z] for p in pts], 0) + 0.03
    # (the gauntlet reaches back along the forearm)
    cuff_end = hand.w + hand.toward * 0.046
    for c in (cuff_end,):
        lo = np.minimum(lo, np.array(c[:]) - 0.055)
        hi = np.maximum(hi, np.array(c[:]) + 0.055)
    field = Field(lo, hi, VOX)

    def box(*ps, r=0.0):
        a = np.array([p[:] for p in ps])
        return a.min(0) - r, a.max(0) + r

    def ell(c, radii, ax=axes):
        def fn(P):
            return sd_ellipsoid(P, c, ax, radii)
        m = max(radii)
        return fn, np.array(c[:]) - m, np.array(c[:]) + m

    def cone(a, b_, r1, r2):
        lo_, hi_ = box(a, b_, r=max(r1, r2))
        return (lambda P: sd_round_cone(P, a, b_, r1, r2)), lo_, hi_

    P = hand.P
    # the palm: the four metacarpals, fused into one broad, flat, thick body
    palm = Field(*box(P(-0.03, 0, 0), P(0.11, 0, 0), P(0.05, 0, 0.06), P(0.05, 0, -0.06), r=0.03), VOX)
    for name, bones, s0, f0, r in FINGERS:
        fn, lo_, hi_ = cone(P(0.008, -0.001, s0 * 0.45), P(f0 - 0.006, 0.0, s0), 0.0118, r * 1.04)
        palm.add(fn, lo_, hi_, 0.012)   # (a wide blend swells: the palm stays firm and flat-backed)
    # its flesh: the ball of the thumb, the heel of the hand, the pad under the fingers, the back of the hand's arch
    for c, radii, k in ((P(0.036, -0.011, 0.021), (0.031, 0.0145, 0.0185), 0.012),
                        (P(0.044, -0.0095, -0.026), (0.036, 0.0118, 0.0135), 0.012),
                        (P(0.079, -0.0095, 0.0), (0.0135, 0.0105, 0.039), 0.01),
                        (P(0.042, 0.0, 0.0), (0.04, 0.0108, 0.032), 0.012),
                        (P(0.0, -0.001, -0.001), (0.022, 0.0172, 0.0285), 0.014)):   # (the wrist)
        palm.add(*ell(c, radii), k)
    # the forearm, a firm oval leaving the wrist nearly in line with the hand, under the gauntlet
    t = hand.toward
    fa, fb = hand.w + t * 0.004, hand.w + t * 0.06
    lo_, hi_ = box(fa, fb, r=0.04)
    palm.add(lambda Q: sd_oval_cone(Q, fa, fb, 0.0285, 0.031, b, 0.64), lo_, hi_, 0.02)
    # the knuckles: the heads of the bones, soft bumps across the back of the fist
    for name, bones, s0, f0, r in FINGERS:
        palm.add(*ell(P(f0 - 0.005, 0.0072, s0), (r * 0.8, r * 0.72, r * 0.85)), 0.006)
    field.merge(palm, 0.0)
    # the fingers: each one shape (its bones blended), pressed side by side; a fleshy pad under each bone
    allf = [p for v in hand.fingers.values() for p in v]
    fingers = Field(*box(*allf, r=0.02), VOX)
    for name, bones, s0, f0, r in FINGERS:
        pts_, rad = hand.fingers[name], hand.rads[name]
        fg = fingers
        for i in range(3):
            fg.add(*cone(pts_[i], pts_[i + 1], rad[i], rad[i + 1]), 0.003)
            seg = (pts_[i + 1] - pts_[i]).normalized()
            up = (b - seg * b.dot(seg)).normalized()
            sd = seg.cross(up).normalized()
            mid = pts_[i].lerp(pts_[i + 1], 0.55) - up * rad[i + 1] * 0.28
            fg.add(*ell(mid, ((pts_[i + 1] - pts_[i]).length * 0.42, rad[i + 1] * 0.64, rad[i + 1] * 0.8), (seg, up, sd)), 0.006)
    # (the fingers' roots grow out of the palm: blended broadly into it; side by side they only touch)
    field.merge(fingers, 0.011)
    # the thumb: heavy, its metacarpal buried in the ball of the thumb
    tp, tr = hand.thumb, hand.thumb_r
    th = Field(*box(*tp, r=0.025), VOX)
    for i in range(3):
        th.add(*cone(tp[i], tp[i + 1], tr[i], tr[i + 1]), 0.004)
        seg = (tp[i + 1] - tp[i]).normalized()
        up = (b - seg * b.dot(seg)).normalized()
        if i > 0:
            mid = tp[i].lerp(tp[i + 1], 0.5) - up * tr[i + 1] * 0.3
            th.add(*ell(mid, ((tp[i + 1] - tp[i]).length * 0.42, tr[i + 1] * 0.75, tr[i + 1] * 0.88), (seg, up, seg.cross(up).normalized())), 0.006)
    field.merge(th, 0.014)
    # the glove: the hand inflated by its leather, a little baggy (slow lumps, slack where it's loose)
    G_ = field.F
    G_ -= GLOVE_T
    for x0 in range(0, G_.shape[0], 32):   # (in slabs: the whole lattice's points at once is a lot of memory)
        x1 = min(G_.shape[0], x0 + 32)
        ax = [field.o[0] + np.arange(x0, x1, dtype=np.float32) * VOX] + [field.o[d] + np.arange(G_.shape[d], dtype=np.float32) * VOX for d in (1, 2)]
        Ps = np.stack(np.meshgrid(*ax, indexing="ij"), axis=-1).astype(np.float32)
        G_[x0:x1] +=0.0003 * noise3(Ps, 220.0, 7) + 0.00012 * noise3(Ps, 600.0, 8)
    # the gauntlet: a stiff flared cuff of the same leather from the wrist back over the sleeve, open at its end
    ca, cb = hand.w - t * 0.006, cuff_end
    d = (cb - ca).normalized()
    up = (b - d * b.dot(d)).normalized()
    L = (cb - ca).length

    def gauntlet(Q):
        rel = Q - _a(ca)
        along = rel @ _a(d)
        u = np.clip(along / L, 0, 1)
        flare = u ** 1.6
        w = 0.028 + GLOVE_T + 0.005 * flare      # (out of the glove at the wrist, a little fuller over the sleeve)
        hgt = 0.0182 + GLOVE_T + 0.0045 * flare
        x = rel @ _a(d.cross(up).normalized())
        y = rel @ _a(up)
        rr = np.sqrt((x / w) ** 2 + (y / hgt) ** 2)
        dist = (rr - 1.0) * np.minimum(w, hgt)
        shell = np.abs(dist) - 0.0012                       # (its leather 2.4 mm thick)
        cap = np.maximum(-along, along - L)                  # (between the wrist and its open end)
        return np.maximum(shell, cap)
    lo_, hi_ = box(ca, cb, r=0.052)
    field.add(gauntlet, lo_, hi_, 0.006)
    # (the inside of the gauntlet hollow back to the glove's own wrist: nothing to fill)
    return field


# ------------------------------------------------------------------ the glove's folds, seams and wear

def _np(v):
    return np.array((v[0], v[1], v[2]))


def carve(ob, hand):
    """Carves the glove's marks into the dense skin, along each vertex's normal; returns how deep in a fold or seam each
    vertex lies (0..1). Leather bunches in folds inside every bend and across the palm, lies in soft wrinkles over the
    back of the fingers, and is sewn: seams down the fingers' sides, round each fingertip's cap, round the thumb, the
    gauntlet's welt."""
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
    B = _np(hand.b)

    def g(x, w):
        return np.exp(-(x / w) ** 2)

    def seg_frame(a, b_):
        A, Bp = _np(a), _np(b_)
        d = Bp - A
        L = np.linalg.norm(d)
        d /= L
        tt = (co - A) @ d
        radial = co - A - tt[:, None] * d
        rl = np.linalg.norm(radial, axis=1) + 1e-9
        return d, tt, radial / rl[:, None], rl, L

    def seam(dist, m, width=0.00055, depth=0.00028):
        """A sewn seam: a groove, a raised welt either side, a little darker."""
        nonlocal disp, dark
        w = m * (g(dist, width) * -depth + (g(dist - width * 2.2, width) + g(dist + width * 2.2, width)) * depth * 0.45)
        disp += w
        dark = np.maximum(dark, m * g(dist, width * 1.4) * 0.7)

    rng = np.random.default_rng(11)
    allj = list(hand.fingers.items()) + [("thumb", hand.thumb)]
    for name, pts in allj:
        rads = hand.rads.get(name, hand.thumb_r)
        rads = [r + GLOVE_T for r in rads]
        for i in range(1 if name == "thumb" else 0, 3):
            p = pts[i]
            d_in = (pts[i] - pts[i - 1]).normalized() if i > 0 else (hand.f if name != "thumb" else (pts[1] - pts[0]).normalized())
            d_out = (pts[i + 1] - pts[i]).normalized()
            ax = (d_in + d_out).normalized()
            inner = d_out - d_in
            bent = inner.length
            rel = co - _np(p)
            along = rel @ _np(ax)
            radial = rel - along[:, None] * _np(ax)
            rl = np.linalg.norm(radial, axis=1) + 1e-9
            dirn = radial / rl[:, None]
            near = (rl < rads[i] * 1.9) & (np.abs(along) < rads[i] * 1.6)
            if bent > 0.12:
                inner = inner - ax * inner.dot(ax)
                inner.normalize()
                inside = dirn @ _np(inner)
                # bunched folds inside the bend: two or three, irregular, deeper with the bend
                wi = np.clip((inside - 0.1) / 0.5, 0, 1) * near
                ph = rng.uniform(0, math.tau)
                lam = rads[i] * 0.55
                fold = np.cos(along / lam * math.pi + ph + 0.6 * np.sin(np.arctan2(dirn @ _np(ax.cross(inner)), inside) * 2))
                env = g(along, rads[i] * 0.9) * wi * min(1.0, bent * 1.3)
                disp += fold * 0.00045 * env
                dark = np.maximum(dark, np.clip(-fold, 0, 1) * env * 0.85)
                # over the back of the joint the leather is stretched: smooth, rubbed paler (paint does that), wrinkled
                # a little either side of it
                wb = np.clip((-inside - 0.2) / 0.5, 0, 1) * near
                wr = np.cos(along / (rads[i] * 0.33) * math.pi + ph) * g(np.abs(along) - rads[i] * 0.85, rads[i] * 0.35)
                disp += wr * 0.0002 * wb
            # the fingers' side seams (the fourchettes), from this joint to the next
            if name != "thumb":
                dseg, tt, dn, rl2, L = seg_frame(pts[i], pts[i + 1])
                side = _np((pts[i + 1] - pts[i]).normalized().cross(hand.b).normalized())
                m = (rl2 < rads[i] * 1.8) & (tt > -0.002) & (tt < L + 0.002)
                for sgn in (-1, 1):
                    ang = np.arccos(np.clip(dn @ (side * sgn), -1, 1))
                    seam(ang * rads[i], m & (ang < 0.6))
        # the fingertip's cap: a seam round the end of the last bone
        tip, prev = pts[-1], pts[-2]
        dseg, tt, dn, rl2, L = seg_frame(prev, tip)
        m = (rl2 < rads[-1] * 1.6)
        seam(tt - L * 0.55, m & (tt > 0) & (tt < L * 1.2), 0.0005, 0.00022)
    # slack: worn leather lies in fine irregular wrinkles across the fingers (ripples a few millimetres apart, wandering),
    # and is crumpled a little everywhere
    for name, pts in allj:
        rads = [r + GLOVE_T for r in hand.rads.get(name, hand.thumb_r)]
        for i in range(1 if name == "thumb" else 0, 3):
            dseg, tt, dn, rl2, L = seg_frame(pts[i], pts[i + 1])
            m = (rl2 < rads[i] * 1.7) & (tt > 0) & (tt < L)
            if not m.any():
                continue
            wob = np.sin(np.arctan2(dn @ _np(hand.s), dn @ B) * rng.uniform(1.5, 3.0) + rng.uniform(0, math.tau))
            lam = rng.uniform(0.0026, 0.0036)
            rip = np.sin(tt / lam * math.tau + 2.2 * wob + rng.uniform(0, math.tau))
            env = m * np.sin(np.clip(tt / L, 0, 1) * math.pi) ** 0.5
            disp += rip * 0.00013 * env
            dark = np.maximum(dark, np.clip(-rip, 0, 1) * env * 0.35)
    P32 = co.astype(np.float32)
    disp += 0.00009 * noise3(P32, 1700.0, 21) + 0.00005 * noise3(P32, 3400.0, 22)
    # the thumb's seam round its root (the glove's thumb is a separate piece)
    tp = hand.thumb
    dseg, tt, dn, rl2, L = seg_frame(tp[0], tp[1])
    seam(tt - L * 0.62, (rl2 < 0.03) & (np.abs(tt - L * 0.62) < 0.003))
    # the palm: folds bunched across it where the fingers close (two broad creases), and across the root of the thumb
    for fpos, k_ in ((0.068, 1.0), (0.05, 0.7)):
        rel = co - _np(hand.w)
        fwd = rel @ _np(hand.f) - fpos - (rel @ _np(hand.s)) * 0.18
        m = (nr @ B < -0.3) & (rel @ B < 0)
        env = g(fwd, 0.006) * m * k_
        disp -= env * 0.0004
        dark = np.maximum(dark, env * 0.7)
    # the gauntlet's welt (a stitched band near its open end) and the seam where it's sewn to the glove at the wrist
    t = hand.toward
    for at, dep in ((0.04, 0.0003), (0.012, 0.00025)):
        rel = co - _np(hand.w + t * at)
        along = rel @ _np(t)
        r2 = np.linalg.norm(rel - along[:, None] * _np(t), axis=1)
        seam(along, (r2 < 0.06) & (np.abs(along) < 0.004), 0.0005, dep)
    # the back of the hand: two stitched lines from between the knuckles toward the wrist (the panel's darts)
    for k in range(3):
        sa, sb = FINGERS[k][2], FINGERS[k + 1][2]
        sm = (sa + sb) * 0.5
        a3 = hand.P(0.076, 0, sm * 1.02)
        b3 = hand.P(0.03, 0, sm * 0.78)
        rel = co - _np(hand.w)
        fs = np.stack((rel @ _np(hand.f), rel @ _np(hand.s)), 1)
        a2 = np.array((0.076, sm * 1.02))
        b2 = np.array((0.03, sm * 0.78))
        ab = b2 - a2
        tt = np.clip(((fs - a2) @ ab) / (ab @ ab), 0, 1)
        dist = np.linalg.norm(fs - (a2 + tt[:, None] * ab), axis=1)
        m = (nr @ B > 0.4) & (rel @ B > 0) & (tt > 0.001) & (tt < 0.999)
        seam(dist, m, 0.0004, 0.00016)
    co += nr * disp[:, None]
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    return dark


GLOVE = np.array((0.060, 0.038, 0.024))       # (worn brown leather, a work glove's)
GLOVE_WORN = np.array((0.15, 0.10, 0.064))     # (rubbed paler where it's stretched over the knuckles and fingertips)
SLEEVE = np.array((0.05, 0.06, 0.042))


def paint_glove(ob, hand, ao=None):
    """Worn brown leather: a slow mottle, rubbed paler over the knuckles, the fingertips and wherever it's stretched,
    darker in its hollows (and by the baked occlusion)."""
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    nr = np.empty(n * 3)
    me.vertex_normals.foreach_get("vector", nr)
    nr = nr.reshape(-1, 3)
    ev = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", ev)
    ev = ev.reshape(-1, 2)
    acc = np.zeros((n, 3))
    cnt = np.zeros(n)
    np.add.at(acc, ev[:, 0], co[ev[:, 1]])
    np.add.at(acc, ev[:, 1], co[ev[:, 0]])
    np.add.at(cnt, ev[:, 0], 1)
    np.add.at(cnt, ev[:, 1], 1)
    curv = -((acc / np.maximum(cnt, 1)[:, None] - co) * nr).sum(1)
    for _ in range(3):
        s_ = np.zeros(n)
        np.add.at(s_, ev[:, 0], curv[ev[:, 1]])
        np.add.at(s_, ev[:, 1], curv[ev[:, 0]])
        curv = 0.5 * curv + 0.5 * s_ / np.maximum(cnt, 1)
    curv = np.clip(curv / (np.percentile(np.abs(curv), 95) + 1e-12), -1, 1)
    P = co.astype(np.float32)
    mott = noise3(P, 140.0, 3) * 0.5 + noise3(P, 420.0, 4) * 0.5
    base = GLOVE[None, :] * (0.92 + 0.12 * mott)[:, None]
    wear = np.clip(curv - 0.4, 0, 1) ** 0.8 * 0.5
    joints = [(pts[i], 1.0 if i == 0 else 0.75) for pts in hand.fingers.values() for i in range(1, 4)]
    joints += [(hand.thumb[2], 0.7), (hand.thumb[3], 0.8)]
    for nm, pts in hand.fingers.items():
        joints.append((pts[0] + hand.b * 0.012, 1.0))   # (the knuckles)
    for p, kk in joints:
        d = np.linalg.norm(co - _np(p), axis=1)
        wear = np.maximum(wear, np.clip(1 - d / 0.016, 0, 1) ** 1.5 * kk * 0.8)
    wear *= 0.75 + 0.5 * np.clip(noise3(P, 300.0, 5), -0.5, 0.5)
    col = base + (GLOVE_WORN[None, :] - base) * np.clip(wear, 0, 1)[:, None]
    col *= (1 - 0.35 * np.clip(-curv - 0.1, 0, 1))[:, None]
    if ao is not None:
        col *= (0.35 + 0.65 * ao)[:, None]
    set_colors(me, col)


def set_colors(me, col):
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.concatenate((col, np.ones((len(col), 1))), axis=1).ravel())
    me.color_attributes.active_color = attr


# ------------------------------------------------------------------ the sleeve

def loft(name, centres, sections, cap0=True, cap1=True, nr=24):
    bm = bmesh.new()
    rings = [[bm.verts.new(c + sec(j / nr * math.tau)) for j in range(nr)] for c, sec in zip(centres, sections)]
    for i in range(len(rings) - 1):
        for j in range(nr):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % nr], rings[i + 1][(j + 1) % nr], rings[i + 1][j]))
    for ring, do in ((rings[0], cap0), (rings[-1], cap1)):
        if do:
            bm.faces.new(ring)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def sleeve(name, wrist, toward, b, length, seed, cuff=0.041, swell=0.015, start=0.022, oval=1.0):
    """The jacket's sleeve over the forearm: a loose tube from inside the glove's gauntlet back past the elbow, its folds
    gathered toward the cuff. Open at the cuff."""
    cs, secs = [], []
    n = 22
    rng = np.random.default_rng(seed)
    phase = rng.uniform(0, math.tau, 6)
    u = (b - toward * b.dot(toward)).normalized().cross(toward).normalized()
    v = toward.cross(u).normalized()
    for i in range(n + 1):
        t = i / n
        c = wrist + toward * (start + length * t)
        base = cuff + swell * min(1, t * 2.2)
        gather = math.exp(-t * 4)

        def sec(a, t=t, base=base, gather=gather):
            fold = 0.0
            for k in range(3):
                fold += math.sin(a * (2 + k) + phase[k] + t * (14 + 5 * k)) * (0.0045 - 0.001 * k)
            fold += math.sin(t * 38 + phase[3] + a) * 0.004 * gather
            ry = (1.0 + 0.12 * math.sin(a * 2 + phase[4])) * (oval + (1 - oval) * min(1.0, t * 3))
            tuck = min(1.0, 0.25 + t * 5)   # (tucked into the glove's cuff: its folds pressed flat there)
            return (u * math.cos(a) + v * math.sin(a) * ry) * (base + fold * (0.5 + gather) * tuck)
        cs.append(c)
        secs.append(sec)
    ob = loft(name, cs, secs, cap0=False, cap1=True, nr=40)
    add_mod(ob, "SUBSURF", levels=2)
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


# ------------------------------------------------------------------ the bake and the export

def grain_material(name):
    """The dense skin's material for the bake: the leather's pebbled grain as a bump (it goes into the normal map)."""
    mt = bpy.data.materials.new(name)
    mt.use_nodes = True
    nt = mt.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    vo = nt.nodes.new("ShaderNodeTexVoronoi")
    vo.feature = "DISTANCE_TO_EDGE"
    vo.inputs["Scale"].default_value = 1100.0
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.inputs["Scale"].default_value = 160.0
    nz.inputs["Detail"].default_value = 3.0
    for x in (vo, nz):
        nt.links.new(tc.outputs["Object"], x.inputs["Vector"])
    mix = nt.nodes.new("ShaderNodeMath")
    mix.operation = "MULTIPLY_ADD"
    nt.links.new(nz.outputs["Fac"], mix.inputs[0])
    mix.inputs[1].default_value = 0.35
    nt.links.new(vo.outputs["Distance"], mix.inputs[2])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.35
    bump.inputs["Distance"].default_value = 0.00025
    nt.links.new(mix.outputs["Value"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mt


def select_only(*obs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[-1]


def bake(high, low, sleeve_ob, img_name):
    """The dense skin's shape (and grain) into the light one's normal map; the occlusion into its vertex colours."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    select_only(low)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01, area_weight=0.0)
    bpy.ops.object.mode_set(mode="OBJECT")
    img = bpy.data.images.new(img_name, TEX, TEX, alpha=False, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    m = bpy.data.materials.new("fp_glove")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.78
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tex.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    low.data.materials.clear()
    low.data.materials.append(m)
    nt.nodes.active = tex
    high.data.materials.clear()
    high.data.materials.append(grain_material("grain"))
    select_only(high, low)
    sc.render.bake.use_selected_to_active = True
    sc.render.bake.cage_extrusion = 0.0015
    sc.render.bake.max_ray_distance = 0.004
    sc.render.bake.margin = 16
    bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
    # the margin's own grain, then the image packed into the model
    img.filepath_raw = os.path.join(PREV, img_name + ".png")
    img.file_format = "PNG"
    img.save()
    img.pack()
    # occlusion into the light mesh's vertex colours (the sleeve there to shade the gauntlet's inside)
    hide = high.hide_render
    high.hide_render = True
    sc.render.bake.use_selected_to_active = False
    sc.cycles.samples = 16 if FAST else 64
    set_colors(low.data, np.ones((len(low.data.vertices), 3)))
    sc.render.bake.target = "VERTEX_COLORS"
    select_only(low)
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.light_settings.distance = 0.012   # (the occlusion of its own folds, between the fingers, under the cuff)
    bpy.ops.object.bake(type="AO")
    sc.render.bake.target = "IMAGE_TEXTURES"
    high.hide_render = hide
    col = np.empty(len(low.data.vertices) * 4, dtype=np.float32)
    low.data.color_attributes["Col"].data.foreach_get("color", col)
    return col.reshape(-1, 4)[:, 0].astype(np.float64)


def make_hand(pose):
    hand = skeleton(pose)
    field = build_field(hand)
    high = field.mesh("High_" + pose)
    del field
    add_mod(high, "LAPLACIANSMOOTH", lambda_factor=0.6, iterations=3, use_volume_preserve=True)
    apply_mods(high)
    if not FAST:   # (fine enough to carry the seams' welts)
        add_mod(high, "SUBSURF", levels=1, subdivision_type="SIMPLE")
        apply_mods(high)
    dark = carve(high, hand)
    low = high.copy()
    low.data = high.data.copy()
    low.name = "fp_hand_L_" + pose
    bpy.context.scene.collection.objects.link(low)
    add_mod(low, "TRIANGULATE")
    apply_mods(low)
    add_mod(low, "DECIMATE", ratio=min(1.0, LOW_TRIS / tri_count(low)))
    apply_mods(low)
    for p in low.data.polygons:
        p.use_smooth = True
    sl = sleeve("Sleeve", hand.w, hand.toward, hand.b, 0.42, 7, cuff=0.024, swell=0.017, start=0.03, oval=0.74)
    ao = bake(high, low, sl, "fp_hand_L_" + pose + "_normal")
    paint_glove(low, hand, ao)
    rng = np.random.default_rng(3)
    sv = np.array([v.co[:] for v in sl.data.vertices], dtype=np.float32)
    set_colors(sl.data, SLEEVE[None, :] * (0.94 + 0.12 * noise3(sv, 30.0, 2))[:, None])
    m = bpy.data.materials.new("fp_sleeve")
    m.use_nodes = True
    vc = m.node_tree.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    m.node_tree.links.new(vc.outputs["Color"], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.95
    sl.data.materials.append(m)
    add_mod(sl, "DECIMATE", ratio=min(1.0, 4000 / tri_count(sl)))
    apply_mods(sl)
    bpy.data.objects.remove(high)
    # the sleeve joined in (its own material; no UVs it needs, but the exporter wants one map on every part)
    select_only(sl)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(island_margin=0.0)
    bpy.ops.object.mode_set(mode="OBJECT")
    select_only(sl, low)
    bpy.ops.object.join()
    me = low.data
    for a_ in [a for a in me.attributes if not a.name.startswith(".") and a.name not in ("position", "sharp_face", "material_index", "Col", "UVMap")]:
        try:
            me.attributes.remove(a_)
        except Exception:
            pass
    me.color_attributes.active_color = me.color_attributes["Col"]
    add_mod(low, "TRIANGULATE")
    apply_mods(low)
    me.validate(clean_customdata=False)
    me.update()
    select_only(low)
    path = os.path.join(OUT, "fp_hand_L_" + pose + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_materials="EXPORT", export_colors=True, export_image_format="AUTO")
    glove_tris = sum(1 for p in me.polygons if me.materials[p.material_index].name.startswith("fp_glove"))
    print(f"[fp-arm] {pose}: {tri_count(low)} triangles ({glove_tris} the glove) -> {path}")
    return low


def preview(ob, name, eye, grip):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "VERTEX"
    sh.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 700, 700
    if grip is not None:
        A = grip[0]
        rot = (0, math.pi / 2, 0) if abs(A.x) > 0.5 else (0, 0, 0)
        bpy.ops.mesh.primitive_cylinder_add(radius=grip[1] / 1.1, depth=0.09 / 1.1, location=(0, 0, 0), rotation=rot)
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    for tag, at, look, lens in (("eye", eye, G(0, 0, 0), 35), ("eyeclose", eye, G(0, 0.005, 0.02), 110),
                                ("side", G(0.0, 0.12, 0.0) + V((0.32, 0.06, 0.06)), G(0, 0.02, 0.05), 35),
                                ("front", G(-0.12, 0.06, -0.28), G(0, 0.0, 0.03), 60),
                                ("palm", G(0.0, -0.25, -0.05), G(0, 0.0, 0.03), 50)):
        cam.location = at
        cam.rotation_euler = (look - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        cam.data.clip_start = 0.005
        sc.render.filepath = os.path.join(PREV, f"fp_arm_{name}_{tag}.png")
        bpy.ops.render.render(write_still=True)


def main():
    for pose in [p for p in POSES if not [a for a in ARGS if a in POSES] or p in ARGS]:
        clear()
        ob = make_hand(pose)
        P_ = POSES[pose]
        preview(ob, "L_" + pose, P_["eye"], None if P_["grip"] is None else (P_["axis"], P_["grip"]))


main()
