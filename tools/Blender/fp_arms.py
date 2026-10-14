"""The gloved hands seen in first person.

    blender -b --python tools/Blender/fp_arms.py [-- handle stick camera relaxed wide small] [-- fast] [-- look]

Writes assets/models/player/fp_hand_L_<pose>.glb and fp_hand_R_<pose>.glb (the left mirrored, built from the same
skin; their baked normal map embedded), one pair per pose in POSES, and previews to build/blender/fp_arm_L_<pose>_*.png.
`look` only builds the shape and renders the previews (no bake, no export): for quick looks while shaping. Every pose is
modelled as the LEFT hand, its origin the pose's socket (the grip's centre):
  handle    a fist round a horizontal grip along X (the lantern's bail grip), the thumb over the fingers
  stick     a fist round an upright grip along Y (a flare, a tool's handle)
  camera    one end of a box held from the side, the grip along Y: fingers round its front, the thumb up its back
  relaxed   open, the fingers gently curled; the socket the middle of the palm
  wide      a hand round a thick upright neck (the newel cap's, 7 cm): fingers half round it, the thumb opposite
  small     a tight fist round a small upright thing (the lighter)

History: 2026-10-11 the first modelled arms; 2026-10-13d a fist round a real grip, a strong wrist, one hand for every
pose (a swept tube per finger, fused by a voxel remesh); 2026-10-14e one signed-distance field, sculpted, meshed by
surface nets. (2026-10-14f, the owner, again: "skeletor robot hands") The field's skin read as lumpy clay or a mitten,
its decimated bake as ragged patches. Rebuilt as hands are modelled for games: a low quad cage (box-modelled: a palm of
twenty-vertex sections, four eight-sided fingers grown out of its end with their webbing, a thumb grown out of its
heel, a fleshy ball of the thumb), posed joint by joint along the solved grip, and smoothed by subdivision, so the
whole hand is one continuous, smooth skin with real knuckles, finger pads and creases. The glove's folds, seams and
wrinkles are carved into a finer subdivision and baked onto the game's (two levels) through clean UVs; the leather's
grain is the game's shader's. The glove ends in a short cuff under the jacket's knitted storm cuff, the sleeve over that.

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
FAST = "fast" in ARGS          # (a coarser bake, for quick looks)
LOOK = "look" in ARGS          # (shape and previews only)
TEX = 512 if FAST else 1024
GLOVE_T = 0.0025               # (the glove's leather and lining over the bare hand)


def G(x, y, z):
    return V((x, -z, y))


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


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


# ------------------------------------------------------------------ the hand's skeleton

# A man's hand (hand units; the game draws it at 1.1): per finger its three bones (proximal, middle, distal: the distal
# to the tip's end), its knuckle across the palm (s) and along it from the wrist (f), and its radius at the knuckle.
FINGERS = [
    ("index", (0.043, 0.025, 0.020), 0.0285, 0.090, 0.0099),
    ("middle", (0.047, 0.029, 0.021), 0.0090, 0.094, 0.0102),
    ("ring", (0.044, 0.027, 0.020), -0.0105, 0.090, 0.0097),
    ("little", (0.035, 0.020, 0.018), -0.0280, 0.081, 0.0087),
]
TAPER = (1.0, 0.93, 0.87, 0.83)   # (the radius at each joint and the tip, of the knuckle's)


class Hand:
    """A hand in its own frame: origin the wrist joint; f toward the knuckles, b out of the back of the hand, s toward
    the thumb (a left hand)."""

    def __init__(self):
        self.w = V((0, 0, 0))
        self.f, self.b = V((1, 0, 0)), V((0, 0, 1))
        self.s = self.f.cross(self.b).normalized()
        self.fingers = {}    # name -> joint points (knuckle, two joints, tip)
        self.rads = {}       # name -> radii at those points
        self.sides = {}      # name -> the axis its joints bend about
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
    return pts, side


# ------------------------------------------------------------------ the poses

# Each pose's socket is where the held thing's grip sits, in the frame the game holds it in (x right, y up, z back).
#   grip    the grip's radius as drawn (metres; the hand is built at 1/1.1), None for none
#   axis    which way the grip runs through the socket (the thumb's side of the hand toward it)
#   back    which way the back of the hand faces
#   thumb   "over" (closed over the first two fingers), "along" (up the grip opposite the fingers), "relaxed"
#   toward  which way the forearm leaves the wrist (to the elbow)
#   curl    (optional) how much of the solved grip the fingers close (1 = round the grip)
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
    gT = GLOVE_T
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
            hand.fingers[name], hand.sides[name] = chain(hand, base, d, bones, curl)
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
            hand.fingers[name], hand.sides[name] = chain(hand, hand.P(f0, 0.0, s0), d, bones, RELAXED_CURL[name])
            hand.rads[name] = [r * t for t in TAPER]
        gdir = hand.s
        Tc = hand.P(0.05, -0.024, -0.002)
    # the thumb: from its root low on the palm's side (deep in its ball), its metacarpal, then its two bones
    tb = hand.P(0.020, -0.011, 0.019)
    if P_["thumb"] == "over":
        # out beside the first finger's first bone, then across the front of its middle bone, the tip on the middle finger
        via0 = (hand.fingers["index"][0] + hand.fingers["index"][1]) * 0.5 + hand.s * 0.024 - hand.b * 0.01
        via1 = outside("index", 1, 0.021) + hand.s * 0.008
        tip = outside("middle", 1, 0.020)
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
    via0 = tb + (via0 - tb).normalized() * 0.042
    via1 = via0 + (via1 - via0).normalized() * 0.031
    tip = via1 + (tip - via1).normalized() * 0.027
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
    hand.sides = {k: (R @ v).normalized() for k, v in hand.sides.items()}
    hand.thumb = [M @ p for p in hand.thumb]
    # (the owner: "less bendy at the wrist and more strong": the forearm nearly in line with the hand)
    hand.toward = (-hand.f).lerp(P_["toward"].normalized(), 0.4).normalized()
    return hand


# ------------------------------------------------------------------ the cage: a low quad skin, subdivided smooth

SH_F = 1.12    # (an eight-sided tube's subdivided surface lies about this much inside its cage)
SH_P = 1.05    # (the palm's twenty-sided sections, less)
THUMB_J0 = 8   # (the palm's section index the thumb's root starts at)


class Cage:
    def __init__(self):
        self.v, self.faces, self.mat, self.reg = [], [], [], []
        self.region = 0   # (which part a face is: 0 the palm, 1 a finger, 2 the thumb; for the look previews)

    def add(self, p):
        self.v.append(V(p))
        return len(self.v) - 1

    def ring(self, pts):
        return [self.add(p) for p in pts]

    def quad(self, a, b, c, d, m=0):
        self.faces.append((a, b, c, d))
        self.mat.append(m)
        self.reg.append(self.region)

    def bridge(self, A, B, m=0, skip=()):
        n = len(A)
        for j in range(n):
            if j in skip:
                continue
            self.quad(A[j], A[(j + 1) % n], B[(j + 1) % n], B[j], m)

    def align(self, A, B):
        """B's order turned and maybe reversed to best follow A's (by direction from each loop's centre)."""
        ca = sum((self.v[i] for i in A), V()) / len(A)
        cb = sum((self.v[i] for i in B), V()) / len(B)
        da = [(self.v[i] - ca).normalized() for i in A]
        db_ = [(self.v[i] - cb).normalized() for i in B]
        n = len(A)
        best, bo = -1e9, None
        for rev in (False, True):
            order = list(range(n))[::-1] if rev else list(range(n))
            for off in range(n):
                idx = [order[(j + off) % n] for j in range(n)]
                sc = sum(da[j].dot(db_[idx[j]]) for j in range(n))
                if sc > best:
                    best, bo = sc, idx
        return [B[k] for k in bo]

    def cap(self, R, apex):
        c = self.add(apex)
        for k in range(0, 8, 2):
            self.quad(R[k], R[(k + 1) % 8], R[(k + 2) % 8], c)

    def build(self, name):
        me = bpy.data.meshes.new(name)
        used = sorted({i for f in self.faces for i in f})
        remap = {o: n for n, o in enumerate(used)}
        me.from_pydata([self.v[i][:] for i in used], [], [tuple(remap[i] for i in f) for f in self.faces])
        me.validate()
        for p, m in zip(me.polygons, self.reg if LOOK else self.mat):
            p.material_index = m
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = True
        return ob


def section(c, d, up, hw, top, bot, e=0.8, n=8):
    """An eight-point section round c, square-ish: little-top, top, thumb-top, thumb side, thumb-bottom, bottom,
    little-bottom, little side (the same order as the palm's). `top` toward `up` (the back of the finger), `bot` the pad."""
    t = d.cross(up).normalized()
    up = t.cross(d).normalized()
    pts = []
    for j in range(n):
        a = math.radians(135 - 360 / n * j)
        x, y = math.cos(a), math.sin(a)
        px = hw * math.copysign(abs(x) ** e, x)
        py = (top if y > 0 else bot) * math.copysign(abs(y) ** e, y)
        pts.append(c + t * px + up * py)
    return pts


def build_cage(hand):
    """The gloved hand's cage (its measures the glove's outside, before subdivision shrinks them in)."""
    cg = Cage()
    f, b, s = hand.f, hand.b, hand.s
    T = hand.toward
    names = ["little", "ring", "middle", "index"]                  # (the palm's columns, little side to the thumb's)
    fing = {nm: (bones, s0, f0, r) for nm, bones, s0, f0, r in FINGERS}
    # the columns across the knuckle end: each finger's edges and middle
    xs, fe = [], []
    for i, nm in enumerate(names):
        _, s0, f0, r = fing[nm]
        if i == 0:
            xs.append(s0 - (r + GLOVE_T) * 1.02)
            fe.append(f0 - 0.004)
        xs.append(s0)
        fe.append(f0)
        if i < 3:
            n2 = names[i + 1]
            xs.append((s0 + fing[n2][1]) * 0.5)
            fe.append((f0 + fing[n2][2]) * 0.5)
        else:
            xs.append(s0 + (r + GLOVE_T) * 1.02)
            fe.append(f0 - 0.004)
    xs, fe = np.array(xs), np.array(fe)
    lo, hi = xs.min(), xs.max()
    xn = 2 * (xs - lo) / (hi - lo) - 1
    cs = (lo + hi) * 0.5
    hw_end = (hi - lo) * 0.5
    # the palm's sections, from inside the sleeve to the knuckles: (f along the hand (behind the wrist along the
    # forearm), half-width over the knuckles', back, front (the palm side), arch)
    G2 = GLOVE_T
    rows = [(-0.085, 0.70, 0.0165, 0.0185, 0.30),
            (-0.055, 0.71, 0.0165, 0.0185, 0.30),
            (-0.030, 0.73, 0.0160, 0.0180, 0.30),
            (-0.010, 0.71, 0.0145, 0.0165, 0.30),   # (the wrist)
            (0.012, 0.80, 0.0138, 0.0180, 0.30),   # (the heel of the hand)
            (0.032, 0.91, 0.0132, 0.0190, 0.26),
            (0.052, 0.98, 0.0128, 0.0185, 0.22),
            (0.068, 1.00, 0.0126, 0.0160, 0.16),
            (None, 1.00, 0.0120, 0.0122, 0.05)]    # (the knuckles' end, each column at its own knuckle)
    rings = []
    for k, (fk, sc, top, bot, arch) in enumerate(rows):
        top += 0 if k >= 4 else 0
        hw = hw_end * sc
        if fk is not None and fk < 0:
            c0 = hand.w + T * (-fk)
            d = -T
            up = (b - d * b.dot(d)).normalized()
        else:
            c0 = hand.w
            d, up = f, b
        t = d.cross(up).normalized()
        P = []
        top_row, bot_row = [], []
        for j in range(9):
            x = xn[j]
            fj = (fe[j] - 0.011) if fk is None else fk
            base = (c0 + d * fj) if (fk is None or fk >= 0) else c0
            ss = cs + x * hw
            corner = j in (0, 8)
            tb_ = top * (0.62 if corner else (1 - arch * x * x))
            bb = bot * (0.62 if corner else (1 - 0.2 * x * x))
            # the palm's flesh: the heel of the hand on the little finger's side, a hollow in the middle
            if 4 <= k <= 7:
                hyp = max(0.0, -x) * (1.0 if k in (5, 6) else 0.6)
                bb += 0.0045 * hyp
                bb -= 0.0025 * math.exp(-((x - 0.1) / 0.35) ** 2) * (1.0 if k in (5, 6, 7) else 0.4)
                # (the ball of the thumb swelling out of the palm toward the thumb's root)
                bb += 0.0045 * max(0.0, x) ** 1.5 * (1.0 if k in (4, 5) else 0.5 if k == 6 else 0.0)
            if corner:
                ss = cs + math.copysign(hw * 0.98, x)
            top_row.append(base + t * ss * SH_P + up * tb_ * SH_P)
            bot_row.append(base + t * ss * SH_P - up * bb * SH_P)
        for j in range(9):
            P.append(top_row[j])
        mid = lambda j, sgn: ((top_row[j] + bot_row[j]) * 0.5 + t * (sgn * hw * 0.11 * SH_P))
        P.append(mid(8, 1))
        for j in range(8, -1, -1):
            P.append(bot_row[j])
        P.append(mid(0, -1))
        rings.append(cg.ring(P))
    # the thumb's root: four faces taken out of the palm's thumb side over the heel (sections 4-6; from its upper corner
    # (8, so the back of the thumb flows out of the back of the hand) through its side (9) to its lower corner (10))
    KA, J0 = 4, THUMB_J0
    hole = {(KA, J0), (KA, J0 + 1), (KA + 1, J0), (KA + 1, J0 + 1)}
    for k in range(len(rings) - 1):
        cg.bridge(rings[k], rings[k + 1], 0, skip={j for (kk, j) in hole if kk == k})
    loop = [rings[KA][J0], rings[KA][J0 + 1], rings[KA][J0 + 2], rings[KA + 1][J0 + 2], rings[KA + 2][J0 + 2],
            rings[KA + 2][J0 + 1], rings[KA + 2][J0], rings[KA + 1][J0]]
    # the fingers out of the knuckles' end: each its octagon (the end's corners, and a vertex in each web)
    end = rings[-1]
    top_i = end[0:9]
    bot_i = end[10:19][::-1]       # (little side to the thumb's)
    m_i = {0: end[19], 8: end[9]}
    for j in (2, 4, 6):
        p = (cg.v[top_i[j]] + cg.v[bot_i[j]]) * 0.5 + f * 0.007 - b * 0.001
        m_i[j] = cg.add(p)
    for i, nm in enumerate(names):
        a = 2 * i
        root = [top_i[a], top_i[a + 1], top_i[a + 2], m_i[a + 2], bot_i[a + 2], bot_i[a + 1], bot_i[a], m_i[a]]
        cg.region = 1
        finger(cg, hand, nm, root)
    cg.region = 2
    thumb(cg, hand, loop)
    return cg


def _bend(a, b_):
    return math.acos(max(-1.0, min(1.0, a.normalized().dot(b_.normalized()))))


def finger(cg, hand, nm, root):
    pts = hand.fingers[nm]
    side = hand.sides[nm]
    r = [x + GLOVE_T for x in hand.rads[nm]]
    d = [(pts[i + 1] - pts[i]).normalized() for i in range(3)]
    L = [(pts[i + 1] - pts[i]).length for i in range(3)]
    palm_d = (hand.f - side * hand.f.dot(side)).normalized()
    secs = []

    def up_of(dd):
        return side.cross(dd).normalized()

    def joint(p, din, dout, rr, knuckle):
        th = _bend(din, dout)
        dd = (din + dout).normalized()
        mit = min(1.4, 1.0 / max(0.5, math.cos(th * 0.5)))
        top = rr * 0.86 * mit + knuckle * (0.4 + 0.6 * math.sin(min(th, 1.5)))
        bot = rr * 0.84 * mit ** 0.25
        return section(p, dd, up_of(dd), rr * 1.0 * SH_F, top * SH_F, bot * SH_F)

    # the knuckle (the head of its bone standing proud of the back of the fist as it closes)
    secs.append(joint(pts[0] + hand.f * 0.0, palm_d, d[0], r[0], 0.0028))
    secs.append(section(pts[0] + d[0] * L[0] * 0.5, d[0], up_of(d[0]), r[0] * 0.97 * SH_F, r[0] * 0.80 * SH_F, r[0] * 1.02 * SH_F))
    secs.append(joint(pts[1], d[0], d[1], r[1], 0.0016))
    secs.append(section(pts[1] + d[1] * L[1] * 0.5, d[1], up_of(d[1]), r[1] * 0.95 * SH_F, r[1] * 0.78 * SH_F, r[1] * 1.0 * SH_F))
    secs.append(joint(pts[2], d[1], d[2], r[2], 0.0010))
    secs.append(section(pts[2] + d[2] * L[2] * 0.42, d[2], up_of(d[2]), r[2] * 0.95 * SH_F, r[2] * 0.76 * SH_F, r[2] * 1.02 * SH_F))
    tipc = pts[3] - d[2] * r[3] * 0.55
    secs.append(section(tipc, d[2], up_of(d[2]), r[3] * 0.92 * SH_F, r[3] * 0.72 * SH_F, r[3] * 0.92 * SH_F))
    prev = root
    for P in secs:
        R = cg.ring(P)
        cg.bridge(prev, R)
        prev = R
    cg.cap(prev, pts[3] + d[2] * r[3] * 0.35 - up_of(d[2]) * r[3] * 0.12)


def thumb(cg, hand, loop):
    tp = hand.thumb
    r = [x + GLOVE_T for x in hand.thumb_r]
    d = [(tp[i + 1] - tp[i]).normalized() for i in range(3)]
    L = [(tp[i + 1] - tp[i]).length for i in range(3)]
    ax = d[0].cross(d[2])
    if ax.length < 0.12:
        ax = d[0].cross(hand.b * 0.4 - hand.s * 0.0 + hand.f * 0.0 - hand.b * 0.0 + (hand.b + hand.s) * 0.5)
    side = ax.normalized()

    def up_of(dd):
        return dd.cross(side).normalized()

    def joint(p, din, dout, rr, kn):
        th = _bend(din, dout)
        dd = (din + dout).normalized()
        mit = min(1.4, 1.0 / max(0.5, math.cos(th * 0.5)))
        return section(p, dd, up_of(dd), rr * SH_F, (rr * 0.86 * mit + kn) * SH_F, rr * 0.86 * mit ** 0.25 * SH_F)

    secs = []
    # its ball: the metacarpal deep in a broad, fleshy mound toward the palm
    m0 = tp[0] + d[0] * L[0] * 0.52
    secs.append(section(m0, d[0], up_of(d[0]), r[0] * 1.18 * SH_F, r[0] * 0.80 * SH_F, r[0] * 1.30 * SH_F))
    secs.append(joint(tp[1], d[0], d[1], r[1], 0.0016))
    secs.append(section(tp[1] + d[1] * L[1] * 0.5, d[1], up_of(d[1]), r[1] * 0.98 * SH_F, r[1] * 0.80 * SH_F, r[1] * 1.0 * SH_F))
    secs.append(joint(tp[2], d[1], d[2], r[2], 0.001))
    secs.append(section(tp[2] + d[2] * L[2] * 0.42, d[2], up_of(d[2]), r[2] * 0.98 * SH_F, r[2] * 0.74 * SH_F, r[2] * 1.02 * SH_F))
    tipc = tp[3] - d[2] * r[3] * 0.55
    secs.append(section(tipc, d[2], up_of(d[2]), r[3] * 0.94 * SH_F, r[3] * 0.72 * SH_F, r[3] * 0.92 * SH_F))
    prev = loop
    for k, P in enumerate(secs):
        R = cg.ring(P)
        if k == 0:
            R = cg.align(prev, R)
            # (a section between the palm's opening and the ball's: the ball of the thumb swells out of the palm, no ridge)
            mid = []
            for a_, b_ in zip(prev, R):
                p = cg.v[a_].lerp(cg.v[b_], 0.5)
                ax_p = tp[0] + d[0] * (p - tp[0]).dot(d[0])
                mid.append(cg.add(p + (p - ax_p).normalized() * 0.0022))
            cg.bridge(prev, mid)
            prev = mid
        cg.bridge(prev, R)
        prev = R
    # (the tip's cap, its apex a little toward the pad)
    cg.cap(prev, tp[3] + d[2] * r[3] * 0.35 - up_of(d[2]) * r[3] * 0.12)


def make_skin(hand, levels, name):
    ob = build_cage(hand).build(name)
    if "cage" in ARGS:
        return ob
    add_mod(ob, "SUBSURF", levels=levels, render_levels=levels, uv_smooth="PRESERVE_BOUNDARIES")
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


# ------------------------------------------------------------------ the glove's folds, seams and wear

def _np(v):
    return np.array((v[0], v[1], v[2]))


def carve(ob, hand):
    """Carves the glove's marks into the dense skin, along each vertex's normal; returns how deep in a fold or seam each
    vertex lies (0..1). Leather bunches in folds inside every bend and across the palm, lies in soft wrinkles over the
    back of the fingers, and is sewn: seams down the fingers' sides, round each fingertip, round the thumb, the cuff's
    hem."""
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
                disp += fold * 0.00042 * env
                dark = np.maximum(dark, np.clip(-fold, 0, 1) * env * 0.85)
                # over the back of the joint the leather is stretched smooth, wrinkled a little either side of it
                wb = np.clip((-inside - 0.2) / 0.5, 0, 1) * near
                wr = np.cos(along / (rads[i] * 0.33) * math.pi + ph) * g(np.abs(along) - rads[i] * 0.85, rads[i] * 0.35)
                disp += wr * 0.00018 * wb
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
    # slack: worn leather lies in fine irregular wrinkles across the fingers, and is crumpled a little everywhere
    for name, pts in allj:
        rads = [r + GLOVE_T for r in hand.rads.get(name, hand.thumb_r)]
        for i in range(1 if name == "thumb" else 0, 3):
            dseg, tt, dn, rl2, L = seg_frame(pts[i], pts[i + 1])
            m = (rl2 < rads[i] * 1.7) & (tt > 0) & (tt < L)
            if not m.any():
                continue
            wob = np.sin(np.arctan2(dn @ _np(hand.s), dn @ B) * rng.uniform(1.5, 3.0) + rng.uniform(0, math.tau))
            lam = rng.uniform(0.0028, 0.0038)
            rip = np.sin(tt / lam * math.tau + 2.2 * wob + rng.uniform(0, math.tau))
            env = m * np.sin(np.clip(tt / L, 0, 1) * math.pi) ** 0.5
            # (here and there, not in rings all the way down: even rings read as a robot's segments)
            env = env * np.clip(noise3(co.astype(np.float32), 260.0, 30 + i) * 1.6 + 0.2, 0, 1)
            disp += rip * 0.00005 * env
            dark = np.maximum(dark, np.clip(-rip, 0, 1) * env * 0.3)
    P32 = co.astype(np.float32)
    disp += 0.00010 * noise3(P32, 380.0, 21) + 0.00003 * noise3(P32, 1100.0, 22)
    # the thumb's seam round its root (the glove's thumb is a separate piece)
    tp = hand.thumb
    dseg, tt, dn, rl2, L = seg_frame(tp[0], tp[1])
    seam(tt - L * 0.7, (rl2 < 0.03) & (np.abs(tt - L * 0.7) < 0.003))
    # the palm: folds bunched across it where the fingers close (two broad creases)
    for fpos, k_ in ((0.068, 1.0), (0.05, 0.7)):
        rel = co - _np(hand.w)
        fwd = rel @ _np(hand.f) - fpos - (rel @ _np(hand.s)) * 0.18
        m = (nr @ B < -0.3) & (rel @ B < 0) & (rel @ _np(hand.f) > 0.02)
        env = g(fwd, 0.006) * m * k_
        disp -= env * 0.0004
        dark = np.maximum(dark, env * 0.7)
    # the cuff's hem: a stitched band where the glove ends (mostly under the jacket's cuff)
    t = hand.toward
    for at, dep in ((0.022, 0.00028), (0.012, 0.00022)):
        rel = co - _np(hand.w + t * at)
        along = rel @ _np(t)
        r2 = np.linalg.norm(rel - along[:, None] * _np(t), axis=1)
        seam(along, (r2 < 0.06) & (np.abs(along) < 0.004), 0.0005, dep)
    # the back of the hand: two stitched lines from between the knuckles toward the wrist (the panel's darts)
    for k in range(3):
        sa, sb = FINGERS[k][2], FINGERS[k + 1][2]
        sm = (sa + sb) * 0.5
        rel = co - _np(hand.w)
        fs = np.stack((rel @ _np(hand.f), rel @ _np(hand.s)), 1)
        a2 = np.array((0.074, sm * 1.02))
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


# (linear colours) worn, dull brown leather, greyed toward the early acts' cold; rubbed paler where it's stretched
GLOVE = np.array((0.050, 0.038, 0.028))
GLOVE_WORN = np.array((0.125, 0.104, 0.084))
CUFF = np.array((0.030, 0.031, 0.032))        # (the jacket's knitted storm cuff: charcoal wool)
SLEEVE = np.array((0.048, 0.056, 0.040))      # (the jacket: waxed olive canvas)


def paint_glove(ob, hand, ao=None):
    """Worn leather: a slow mottle, rubbed paler over the knuckles, the fingertips and wherever it's stretched, darker in
    its hollows (and by the baked occlusion)."""
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
    base = GLOVE[None, :] * (0.9 + 0.14 * mott)[:, None]
    wear = np.clip(curv - 0.35, 0, 1) ** 0.8 * 0.45
    joints = [(pts[i], 1.0 if i == 0 else 0.75) for pts in hand.fingers.values() for i in range(1, 4)]
    joints += [(hand.thumb[2], 0.7), (hand.thumb[3], 0.8)]
    for nm, pts in hand.fingers.items():
        joints.append((pts[0] + hand.b * 0.012, 1.0))   # (the knuckles)
    for p, kk in joints:
        d = np.linalg.norm(co - _np(p), axis=1)
        wear = np.maximum(wear, np.clip(1 - d / 0.015, 0, 1) ** 1.5 * kk * 0.75)
    # (the palm and the insides of the fingers worn smooth and dark with handling, a scuff here and there)
    palm = np.clip(-(nr @ _np(hand.b)), 0, 1)
    scuff = np.clip(noise3(P, 150.0, 9) * 1.6 - 0.7, 0, 1)
    wear = np.maximum(wear, scuff * 0.22)
    wear *= 0.75 + 0.5 * np.clip(noise3(P, 300.0, 5), -0.5, 0.5)
    col = base * (1 - 0.12 * palm)[:, None]
    col = col + (GLOVE_WORN[None, :] - col) * np.clip(wear, 0, 1)[:, None]
    col *= (1 - 0.4 * np.clip(-curv - 0.1, 0, 1))[:, None]
    if ao is not None:
        col *= (0.3 + 0.7 * ao)[:, None]
    set_colors(me, col)


def set_colors(me, col):
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.concatenate((col, np.ones((len(col), 1))), axis=1).ravel())
    me.color_attributes.active_color = attr


# ------------------------------------------------------------------ the jacket's cuff and sleeve

def loft(name, rings_, closed_end=False):
    """A tube through `rings_` (each a list of points, all the same count), open at the start; UVs u round, v along."""
    bm = bmesh.new()
    nr = len(rings_[0])
    R = [[bm.verts.new(p) for p in ring] for ring in rings_]
    uv = bm.loops.layers.uv.new("UVMap")
    nv = len(R) - 1
    for i in range(nv):
        for j in range(nr):
            f_ = bm.faces.new((R[i][j], R[i][(j + 1) % nr], R[i + 1][(j + 1) % nr], R[i + 1][j]))
            for lp, (uu, vv) in zip(f_.loops, ((j, i), (j + 1, i), (j + 1, i + 1), (j, i + 1))):
                lp[uv].uv = (uu / nr, vv / nv)
    if closed_end:
        f_ = bm.faces.new(R[-1])
        for lp in f_.loops:
            lp[uv].uv = (0.5, 1.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def ellipse_ring(c, d, up, rx, ry, nr, fn=None):
    t = d.cross(up).normalized()
    up = t.cross(d).normalized()
    out = []
    for j in range(nr):
        a = j / nr * math.tau
        k = fn(a) if fn else 1.0
        out.append(c + (t * math.cos(a) * rx + up * math.sin(a) * ry) * k)
    return out


def knit_cuff(hand):
    """The jacket's knitted storm cuff, snug round the glove's wrist: a rolled edge and a ribbed band back into the
    sleeve. Its ribs are the shader's (its UVs run round it)."""
    T, b = hand.toward, hand.b
    d = -T
    up = (b - d * b.dot(d)).normalized()
    c0 = hand.w + T * 0.010 - up * 0.0012
    nr = 48
    rings_ = []
    # (inside the edge, hugging the glove; round the roll; then out along the band)
    for along, rx, ry in ((0.020, 0.0315, 0.0215), (0.008, 0.0318, 0.0218), (0.0035, 0.0335, 0.0232),
                          (0.006, 0.0352, 0.0248), (0.016, 0.0358, 0.0254), (0.032, 0.0364, 0.0260),
                          (0.050, 0.0362, 0.0262), (0.080, 0.0335, 0.0245)):
        rings_.append(ellipse_ring(c0 + T * along, d, up, rx, ry, nr,
                                   lambda a: 1.0 + 0.008 * math.sin(a * 3 + 1.0)))
    ob = loft("Cuff", rings_)
    return ob


def sleeve(hand, length=0.42, seed=7):
    """The jacket's sleeve over the forearm: loose waxed canvas from just behind the wrist (the cuff showing out of it)
    back past the elbow, folds gathered toward its hem; a turned hem."""
    T, b = hand.toward, hand.b
    d = -T
    up = (b - d * b.dot(d)).normalized()
    rng = np.random.default_rng(seed)
    phase = rng.uniform(0, math.tau, 6)
    nr, n = 40, 22
    c0 = hand.w + T * 0.058 - up * 0.002
    rings_ = []
    # (the hem's turn-up: inside, round the edge, out)
    for along, k in ((0.008, 0.86), (0.0015, 0.93), (0.0, 1.0)):
        rings_.append(ellipse_ring(c0 + T * along, d, up, 0.0372 * k, 0.0292 * k, nr,
                                   lambda a: 1.0 + 0.03 * math.sin(a * 2 + phase[0])))
    for i in range(1, n + 1):
        t = i / n
        along = 0.004 + length * t
        base = 1.0 + 0.12 * min(1, t * 2.2)
        gather = math.exp(-t * 4)

        def fn(a, t=t, gather=gather):
            fold = 0.0
            for k in range(3):
                fold += math.sin(a * (2 + k) + phase[k] + t * (14 + 5 * k)) * (0.06 - 0.015 * k)
            fold += math.sin(t * 38 + phase[3] + a) * 0.05 * gather
            return 1.0 + fold * (0.5 + gather) * min(1.0, 0.3 + t * 6) + 0.03 * math.sin(a * 2 + phase[0])
        rings_.append(ellipse_ring(c0 + T * along, d, up, 0.0372 * base, 0.0292 * base, nr, fn))
    ob = loft("Sleeve", rings_, closed_end=True)
    add_mod(ob, "SUBSURF", levels=1)
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


# ------------------------------------------------------------------ the bake and the export

def select_only(*obs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[-1]


def unwrap(ob, angle=62):
    select_only(ob)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=0.012, area_weight=0.0)
    bpy.ops.object.mode_set(mode="OBJECT")


def flat_material(name, rough):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    vc = m.node_tree.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    m.node_tree.links.new(vc.outputs["Color"], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = rough
    return m


def bake(high, low, img_name):
    """The dense skin's folds and seams into the light one's normal map; the occlusion into its vertex colours."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    img = bpy.data.images.new(img_name, TEX, TEX, alpha=False, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    m = flat_material("fp_glove", 0.7)
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tex.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], nt.nodes["Principled BSDF"].inputs["Normal"])
    low.data.materials.clear()
    low.data.materials.append(m)
    nt.nodes.active = tex
    high.data.materials.clear()
    high.data.materials.append(bpy.data.materials.new("high"))
    select_only(high, low)
    sc.render.bake.use_selected_to_active = True
    sc.render.bake.cage_extrusion = 0.0007   # (the two skins lie within a fraction of a millimetre; fingers pressed
    sc.render.bake.max_ray_distance = 0.0015   # together are closer than a long ray would reach)
    sc.render.bake.margin = 12
    bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
    img.filepath_raw = os.path.join(PREV, img_name + ".png")
    img.file_format = "PNG"
    img.save()
    img.pack()
    # occlusion into the light mesh's vertex colours (the cuff and sleeve there to shade the glove's wrist)
    high.hide_render = True
    sc.render.bake.use_selected_to_active = False
    sc.cycles.samples = 16 if FAST else 64
    set_colors(low.data, np.ones((len(low.data.vertices), 3)))
    sc.render.bake.target = "VERTEX_COLORS"
    select_only(low)
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.light_settings.distance = 0.014   # (the occlusion of its own folds, between the fingers, under the cuff)
    bpy.ops.object.bake(type="AO")
    sc.render.bake.target = "IMAGE_TEXTURES"
    col = np.empty(len(low.data.vertices) * 4, dtype=np.float32)
    low.data.color_attributes["Col"].data.foreach_get("color", col)
    return col.reshape(-1, 4)[:, 0].astype(np.float64)


def export(ob, path):
    select_only(ob)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_materials="EXPORT", export_colors=True, export_tangents=True,
                              export_image_format="AUTO")


def make_hand(pose):
    hand = skeleton(pose)
    low = make_skin(hand, 2, "fp_hand_L_" + pose)
    cuff = knit_cuff(hand)
    sl = sleeve(hand)
    if LOOK:
        for nm_, c_ in (("palm", (0.35, 0.3, 0.26)), ("finger", (0.3, 0.32, 0.4)), ("thumb", (0.45, 0.25, 0.2))):
            mm = bpy.data.materials.new(nm_)
            mm.diffuse_color = (*c_, 1.0)
            low.data.materials.append(mm)
        bpy.context.scene.display.shading.color_type = "MATERIAL"
        set_colors(low.data, np.tile(GLOVE_WORN * 3, (len(low.data.vertices), 1)))
        set_colors(cuff.data, np.tile(CUFF * 4, (len(cuff.data.vertices), 1)))
        set_colors(sl.data, np.tile(SLEEVE * 4, (len(sl.data.vertices), 1)))
        print(f"[fp-arm] {pose}: {tri_count(low)} glove triangles (look only)")
        return low
    high = make_skin(hand, 4, "High_" + pose)
    dark = carve(high, hand)
    unwrap(low)
    ao = bake(high, low, "fp_hand_L_" + pose + "_normal")
    bpy.data.objects.remove(high)
    paint_glove(low, hand, ao)
    # the cuff and the sleeve: their own cloths, coloured with a little slow variation
    for ob_, colr, mname, rough in ((cuff, CUFF, "fp_cuff", 0.95), (sl, SLEEVE, "fp_sleeve", 0.9)):
        vv = np.array([v.co[:] for v in ob_.data.vertices], dtype=np.float32)
        set_colors(ob_.data, colr[None, :] * (0.92 + 0.14 * noise3(vv, 40.0, 2))[:, None])
        ob_.data.materials.append(flat_material(mname, rough))
    select_only(cuff, sl, low)
    bpy.ops.object.join()
    me = low.data
    for a_ in [a for a in me.attributes if not a.name.startswith(".") and a.name not in ("position", "sharp_face", "material_index", "Col", "UVMap")]:
        try:
            me.attributes.remove(a_)
        except Exception:
            pass
    me.color_attributes.active_color = me.color_attributes["Col"]
    me.validate(clean_customdata=False)
    me.update()
    path = os.path.join(OUT, "fp_hand_L_" + pose + ".glb")
    export(low, path)
    print(f"[fp-arm] {pose}: {tri_count(low)} faces -> {path}")
    # the right hand: the same skin mirrored across x (its own mesh, so its normal map's frame is its own)
    right = low.copy()
    right.data = low.data.copy()
    right.name = "fp_hand_R_" + pose
    bpy.context.scene.collection.objects.link(right)
    right.data.transform(Matrix.Scale(-1, 4, V((1, 0, 0))))
    right.data.flip_normals()
    right.data.update()
    export(right, os.path.join(OUT, "fp_hand_R_" + pose + ".glb"))
    bpy.data.objects.remove(right)
    return low


def preview(ob, name, eye, grip):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "MATERIAL" if LOOK else "VERTEX"
    sh.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 700, 700
    if grip is not None:
        A = grip[0]
        rot = (0, math.pi / 2, 0) if abs(A.x) > 0.5 else (0, 0, 0)
        bpy.ops.mesh.primitive_cylinder_add(radius=grip[1] / 1.1, depth=0.09 / 1.1, location=(0, 0, 0), rotation=rot)
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    for tag, at, look, lens in (("eye", eye, G(0, 0, 0), 35), ("eyeclose", eye, G(0, 0.005, 0.02), 90),
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
