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
grain is the game's shader's. The glove ends in a short gauntlet, all of it inside the jacket's sleeve (2026-10-15: the
knitted storm cuff that showed between them gone, the sleeve's hem down to the wrist; the glove charcoal-black, the
sleeve's canvas a very dark olive-brown).

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
                   eye=G(0.2, 0.115, 0.5)),
    "stick": dict(grip=0.016, axis=G(0, 1, 0), back=G(-0.8, 0.0, -0.45), thumb="over", toward=G(-0.3, -0.55, 0.78),
                  eye=G(0.2, 0.12, 0.5)),
    "camera": dict(grip=0.021, axis=G(0, 1, 0), back=G(-0.9, 0.0, -0.3), thumb="back", toward=G(-0.3, -0.55, 0.78),
                   eye=G(0.2, 0.08, 0.45)),
    "relaxed": dict(grip=None, axis=G(0.35, 0.9, 0.0), back=G(-1.0, 0.0, -0.4), thumb="relaxed", toward=G(-0.25, -0.6, 0.76),
                    eye=G(0.2, 0.12, 0.5)),
    # (2026-10-14g, "the cap hand shows more wrist than fingers": in the held things' convention, round the neck from
    # behind it, the back of the hand ahead and the palm to the eye, so the fingers close round the neck's near side;
    # the forearm off to the lower right past it)
    "wide": dict(grip=0.036, axis=G(0, 1, 0), back=G(0.0, 0.0, -1.0), thumb="along", forearm=G(-0.55, -0.5, 0.67), lean=0.0,
                 eye=G(0.2, 0.12, 0.5)),
    "small": dict(grip=0.0095, axis=G(0, 1, 0), back=G(-0.8, 0.0, -0.45), thumb="over", toward=G(-0.3, -0.55, 0.78),
                  eye=G(0.2, 0.12, 0.5)),
}

# (2026-10-14g, the owner: "the items in hands still look funky and like they're facing the wrong way") ONE convention for
# the hammer grip, shared by the hand and every item held in it, so a tool drops in with no turn of its own:
#   the socket is the grip's centre; the grip runs up +Y; the knuckles (and so the tool's working end: the bit, the face,
#   the claw, the edge) point -Z, ahead, away from the eye; the back of the (right) hand faces +X, out; the thumb closes
#   over the first two fingers on top.
# (The Stick pose's knuckles point forward and to the left and the game turned it yaw -66 besides, so its tools' working
# ends were some 70 degrees off its knuckles.) The game then carries the fist and its tool leaning forward FIST_LEAN
# degrees about the view's sideways axis (HeldModels' Lean); the forearm here is built turned back by as much about that
# axis, so in the game it comes in from the lower right as `forearm` says while only the wrist cocks. (The axis is x,
# which the right hand's mirroring leaves as it is.)
FIST_LEAN = 30.0
POSES["fist"] = dict(grip=0.016, axis=G(0, 1, 0), back=G(-1.0, 0.0, 0.0), thumb="over", forearm=G(-0.15, -0.5, 0.85), lean=FIST_LEAN,
                     eye=G(0.2, 0.2, 0.45))
# (2026-10-14g: the cap's stone is too broad to see a hand round its neck under the ball: it's carried on the open hand
# under its plinth, palm up, the fingers ahead and cupped up round its edge. The same frame: palm up +Y, fingers -Z, the
# right thumb out to +X (relaxed poses have no grip: their "axis" is the thumb's side), the socket the middle of the palm)
POSES["cradle"] = dict(grip=None, axis=G(-1.0, 0.0, 0.0), back=G(0.0, -1.0, 0.1), thumb="relaxed", forearm=G(-0.2, -0.45, 0.87),
                       lean=0.0, eye=G(0.2, 0.25, 0.45))
# (the small fist round the lighter: the same frame, upright)
POSES["small"] = dict(grip=0.0095, axis=G(0, 1, 0), back=G(-1.0, 0.0, 0.0), thumb="over", forearm=G(-0.15, -0.55, 0.82), lean=0.0,
                      eye=G(0.2, 0.12, 0.5))
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
    elif P_["thumb"] == "back":
        # (2026-10-14g, the owner: the camera's thumb "lies over the top of the camera body; should go up the back") the
        # ball of the thumb along the palm's side to the box's back face, the thumb then flat on that face, up and in
        # toward the middle of the box, its tip a centimetre under the top plate
        via0 = tb + hand.f * 0.031 - hand.b * 0.016 + hand.s * 0.002
        via1 = via0 - hand.b * 0.025 + hand.s * 0.011 + hand.f * 0.009
        tip = via1 - hand.b * 0.022 + hand.s * 0.008 + hand.f * 0.004
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
    if "toward" in P_:
        hand.toward = (-hand.f).lerp(P_["toward"].normalized(), 0.4).normalized()
    if "forearm" in P_:
        # (the hammer-grip convention: the forearm as the game will show it, turned back against the lean it gives the fist)
        hand.toward = (Matrix.Rotation(math.radians(P_["lean"]), 3, G(1, 0, 0)) @ P_["forearm"].normalized()).normalized()
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
    # (2026-10-14g: "the fingertips of an empty fist look round and toe-like": the last section a little narrower and
    # flatter over the nail, the cap's apex drawn further out and toward the pad, so the tip tapers to a fingertip)
    secs.append(section(tipc, d[2], up_of(d[2]), r[3] * 0.87 * SH_F, r[3] * 0.66 * SH_F, r[3] * 0.88 * SH_F))
    prev = root
    for P in secs:
        R = cg.ring(P)
        cg.bridge(prev, R)
        prev = R
    cg.cap(prev, pts[3] + d[2] * r[3] * 0.45 - up_of(d[2]) * r[3] * 0.2)


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

    stitch = np.zeros(n)

    def seam(dist, m, width=0.00055, depth=0.00028, along=None):
        """A sewn seam: a groove, a raised welt either side, a little darker; and (2026-10-14g) its thread: short pale
        stitches across the groove every few millimetres along it (`along`, metres)."""
        nonlocal disp, dark, stitch
        w = m * (g(dist, width) * -depth + (g(dist - width * 2.2, width) + g(dist + width * 2.2, width)) * depth * 0.45)
        disp += w
        dark = np.maximum(dark, m * g(dist, width * 1.4) * 0.7)
        if along is not None:
            dash = np.clip(np.sin(along / 0.0032 * math.tau) * 2.0 - 0.3, 0, 1)
            # (the thread lies over the welt, a little proud: a stitch catches the light)
            stitch = np.maximum(stitch, m * g(dist, width * 1.7) * dash)
            disp += m * g(dist, width * 1.5) * dash * depth * 0.6

    def round_len(dn, d):
        """Where round an axis `d` each vertex lies (radians times a typical radius: metres for a stitch's spacing)."""
        a1 = np.cross(d, np.array((0.31, 0.77, 0.55)))
        a1 /= np.linalg.norm(a1)
        a2 = np.cross(d, a1)
        return np.arctan2(dn @ a2, dn @ a1)

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
                # (2026-10-14g) and the creases across the back of the knuckle, where the leather folds each time the
                # fist opens: two or three, dark with the grime worked into them
                kc = np.cos(along / (rads[i] * 0.42) * math.pi + ph * 0.5) * g(along, rads[i] * 0.75)
                kc = kc * np.clip(1.0 + 0.6 * np.sin(np.arctan2(dirn @ _np(ax.cross(inner)), -inside) * 3 + ph), 0, 1.4)
                disp -= np.clip(kc - 0.55, 0, 1) * 0.00045 * wb
                dark = np.maximum(dark, np.clip(kc - 0.5, 0, 1) * 0.9 * wb * min(1.0, bent * 1.5))
            # the fingers' side seams (the fourchettes), from this joint to the next
            if name != "thumb":
                dseg, tt, dn, rl2, L = seg_frame(pts[i], pts[i + 1])
                side = _np((pts[i + 1] - pts[i]).normalized().cross(hand.b).normalized())
                m = (rl2 < rads[i] * 1.8) & (tt > -0.002) & (tt < L + 0.002)
                for sgn in (-1, 1):
                    ang = np.arccos(np.clip(dn @ (side * sgn), -1, 1))
                    seam(ang * rads[i], m & (ang < 0.6), along=tt)
        # the fingertip's cap: a seam round the end of the last bone
        tip, prev = pts[-1], pts[-2]
        dseg, tt, dn, rl2, L = seg_frame(prev, tip)
        m = (rl2 < rads[-1] * 1.6)
        seam(tt - L * 0.55, m & (tt > 0) & (tt < L * 1.2), 0.0005, 0.00022)   # (no thread drawn round the tip: a dotted ring there read as a toe)
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
    seam(tt - L * 0.7, (rl2 < 0.03) & (np.abs(tt - L * 0.7) < 0.003), along=round_len(dn, dseg) * 0.013)
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
        radn = (rel - along[:, None] * _np(t)) / (r2[:, None] + 1e-9)
        seam(along, (r2 < 0.06) & (np.abs(along) < 0.004), 0.0005, dep, along=round_len(radn, _np(t)) * 0.03)
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
        seam(dist, m, 0.0004, 0.00016, along=tt * np.linalg.norm(ab))
    co += nr * disp[:, None]
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    return dark, stitch


# (linear colours; 2026-10-15, the owner: "make the gloves darker grey or black") a winter work glove: charcoal-black
# leather over the back, rubbed to a worn grey over the knuckles and fingertips, a darker synthetic grip palm, grey thread
GLOVE = np.array((0.021, 0.020, 0.019))         # (the leather: a dyed charcoal-black)
GLOVE_RED = np.array((0.026, 0.023, 0.020))     # (here and there a little warmer, where the dye's faded)
GLOVE_WORN = np.array((0.060, 0.057, 0.052))    # (rubbed grey where it's stretched: the knuckles, the fingertips)
GLOVE_DIRT = np.array((0.012, 0.011, 0.010))    # (grime worked into the palm, the fingertips, the creases)
GLOVE_SCUFF = np.array((0.046, 0.044, 0.041))   # (scuffed: the grain roughed up, paler and duller)
GLOVE_SCRATCH = np.array((0.095, 0.090, 0.082))  # (a scratch through the dye to the grey leather under it)
GLOVE_PALM = np.array((0.011, 0.011, 0.011))    # (the palm's synthetic grip panel: blacker than the leather)
THREAD = np.array((0.070, 0.068, 0.064))        # (the grey nylon thread of the seams)
CUFF = np.array((0.036, 0.032, 0.026))          # (the jacket's knitted storm cuff: a warm charcoal wool)
CUFF_DIRT = np.array((0.030, 0.025, 0.018))
# (2026-10-14g, the owner: "in some shots the sleeve reads dark navy-black") the jacket's cotton duck, sun-faded paler on
# top, darker and dirtier underneath. (2026-10-15, the owner: darker colours) a very dark olive-brown now, the body's
# jacket's own dark olive (player_body.py) gone a little browner with wear: dark against the woods, but still brown-olive
# (not black: the charcoal glove has to read against it) under the lantern
SLEEVE = np.array((0.034, 0.030, 0.016))
SLEEVE_FADED = np.array((0.050, 0.045, 0.027))
SLEEVE_UNDER = np.array((0.020, 0.018, 0.010))
SLEEVE_DIRT = np.array((0.024, 0.020, 0.013))   # (mud, dried and rubbed in)
SLEEVE_GREASE = np.array((0.016, 0.015, 0.011))


def mesh_arrays(me):
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    nr = np.empty(n * 3)
    me.vertex_normals.foreach_get("vector", nr)
    return co.reshape(-1, 3), nr.reshape(-1, 3)


def curvature(me, co, nr, smooth=3):
    """Per vertex: + over a ridge (a knuckle, a fold's crest), - in a hollow; about -1..1."""
    n = len(co)
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
    for _ in range(smooth):
        s_ = np.zeros(n)
        np.add.at(s_, ev[:, 0], curv[ev[:, 1]])
        np.add.at(s_, ev[:, 1], curv[ev[:, 0]])
        curv = 0.5 * curv + 0.5 * s_ / np.maximum(cnt, 1)
    return np.clip(curv / (np.percentile(np.abs(curv), 95) + 1e-12), -1, 1)


def _lerp(col, to, k):
    return col + (np.asarray(to)[None, :] - col) * np.clip(k, 0, 1)[:, None]


def glove_colour(ob, hand, dark=None, stitch=None, smooth=3):
    """The worn work glove's colour: an oiled brown with a slow mottle, rubbed paler and warmer over the knuckles, the
    fingertips and wherever it's stretched; grime worked into the palm, the fingertips and every crease; scuffed patches;
    on the dense skin (`dark`, `stitch` from `carve`) its folds' dark crevices, fine scratches through the finish, and the
    seams' pale thread. Returns the colours and how worn (smooth, a little polished) each vertex is."""
    me = ob.data
    co, nr = mesh_arrays(me)
    n = len(co)
    curv = curvature(me, co, nr, smooth)
    P = co.astype(np.float32)
    B = _np(hand.b)
    mott = noise3(P, 140.0, 3) * 0.5 + noise3(P, 420.0, 4) * 0.5
    col = GLOVE[None, :] * (0.84 + 0.22 * mott)[:, None]
    col = _lerp(col, GLOVE_RED, np.clip(noise3(P, 55.0, 6) * 1.5, 0, 1) * 0.5)
    # worn: the ridges, the joints, the knuckles
    wear = np.clip(curv - 0.3, 0, 1) ** 0.8 * 0.55
    joints = [(pts[i], 1.0 if i == 0 else 0.8) for pts in hand.fingers.values() for i in range(1, 4)]
    joints += [(hand.thumb[2], 0.7), (hand.thumb[3], 0.9)]
    for nm, pts in hand.fingers.items():
        joints.append((pts[0] + hand.b * 0.012, 1.0))   # (the knuckles)
    for p, kk in joints:
        d = np.linalg.norm(co - _np(p), axis=1)
        wear = np.maximum(wear, np.clip(1 - d / 0.014, 0, 1) ** 1.4 * kk * 0.8)
    wear *= 0.7 + 0.6 * np.clip(noise3(P, 300.0, 5), -0.5, 0.5)
    wear = np.clip(wear, 0, 1)
    palm = np.clip(-(nr @ B) * 1.4, 0, 1)
    col = _lerp(col, GLOVE_PALM, palm * 0.8)
    col = _lerp(col, GLOVE_WORN, wear * (1 - 0.6 * palm))
    # scuffs: the grain roughed up in patches, paler and duller (not on the palm: that's polished with handling)
    sc = np.clip(noise3(P, 190.0, 9) * 2.4 - 1.15, 0, 1) * np.clip(noise3(P, 600.0, 10) + 0.8, 0, 1) * (1 - 0.7 * palm)
    col = _lerp(col, GLOVE_SCUFF, sc * 0.75)
    # grime: worked into the palm and the fingertips, broken up
    tipd = np.full(n, 1e9)
    for pts in list(hand.fingers.values()) + [hand.thumb]:
        tipd = np.minimum(tipd, np.linalg.norm(co - _np(pts[-1]), axis=1))
        tipd = np.minimum(tipd, np.linalg.norm(co - _np(pts[-2]), axis=1) + 0.006)
    tips = np.clip(1 - tipd / 0.02, 0, 1)
    brk = np.clip(0.45 + 1.1 * noise3(P, 95.0, 12) + 0.4 * noise3(P, 330.0, 13), 0, 1)
    grime = np.clip(palm * 0.75 + tips * 0.9, 0, 1) * brk
    col = _lerp(col, GLOVE_DIRT, grime * 0.7)
    # the hollows dark (and the grime there)
    hollow = np.clip(-curv - 0.05, 0, 1)
    col *= (1 - 0.45 * hollow)[:, None]
    if dark is not None:
        col = _lerp(col, GLOVE_DIRT * 0.5, dark * 0.8)
        # scratches: fine pale lines through the finish over the back and sides
        rng = np.random.default_rng(41)
        cand = np.nonzero((nr @ B > -0.35))[0]
        scr = np.zeros(n)
        for _ in range(90):
            i = cand[rng.integers(len(cand))]
            p0, n0 = co[i], nr[i]
            t = np.cross(n0, rng.normal(size=3))
            t /= np.linalg.norm(t) + 1e-12
            L = rng.uniform(0.003, 0.013)
            rel = co - p0
            near = (np.abs(rel @ n0) < 0.0025) & (nr @ n0 > 0.6) & (np.abs(rel @ t) < L)
            if not near.any():
                continue
            idx = np.nonzero(near)[0]
            r_ = rel[idx]
            tt = np.clip(r_ @ t, 0, L)
            # (a scratch tapers off at its ends and wanders a little)
            d = np.linalg.norm(r_ - tt[:, None] * t - (r_ @ n0)[:, None] * n0, axis=1)
            w = np.exp(-(d / rng.uniform(0.00018, 0.0003)) ** 2) * np.sin(np.clip(tt / L, 0, 1) * math.pi) ** 0.5
            scr[idx] = np.maximum(scr[idx], w * rng.uniform(0.5, 1.0))
        col = _lerp(col, GLOVE_SCRATCH, scr * 0.85)
        wear = np.maximum(wear, scr * 0.5)
    if stitch is not None:
        col = _lerp(col, THREAD, stitch * 0.9)
    return col, wear, palm


def cloth_colour(ob, hand, kind):
    """The jacket's cloths: `kind` 1 the knitted cuff, 2 the canvas sleeve. The sleeve sun-faded paler on top (the side
    the back of the hand faces), darker underneath; mud splashed and rubbed in, worst toward the hem and underneath; a
    grease mark; the folds' hollows dark."""
    me = ob.data
    co, nr = mesh_arrays(me)
    P = co.astype(np.float32)
    T = hand.toward
    up = hand.b - T * hand.b.dot(T)
    up.normalize()
    along = (co - _np(hand.w)) @ _np(T)
    top = nr @ _np(up)
    if kind == 1:
        col = CUFF[None, :] * (0.88 + 0.2 * noise3(P, 60.0, 2))[:, None]
        col = _lerp(col, CUFF_DIRT, np.clip(noise3(P, 120.0, 3) + 0.2, 0, 1) * 0.6)
        return col
    curv = curvature(me, co, nr, 2)
    col = SLEEVE[None, :] * (0.9 + 0.16 * noise3(P, 22.0, 2) + 0.06 * noise3(P, 70.0, 4))[:, None]
    col = _lerp(col, SLEEVE_FADED, np.clip(top, 0, 1) ** 1.3 * (0.65 + 0.25 * noise3(P, 30.0, 5)))
    col = _lerp(col, SLEEVE_UNDER, np.clip(-top, 0, 1) * 0.7)
    # mud: splashed on, worst near the hem and underneath, rubbed in
    hem = np.exp(-np.clip(along - HEM_AT, 0, None) / 0.05)
    splash = np.clip(noise3(P, 34.0, 15) * 2.0 - 0.55 + 0.5 * hem + 0.35 * np.clip(-top, 0, 1), 0, 1)
    splash *= np.clip(0.4 + noise3(P, 140.0, 16) * 1.2, 0, 1)
    col = _lerp(col, SLEEVE_DIRT, splash * 0.75)
    # a grease mark where the forearm rests on things (a dark, soft-edged patch on the outside)
    side = np.cross(_np(T), _np(up))
    gp = _np(hand.w + T * 0.16) + side * 0.03
    gd = np.linalg.norm(co - gp, axis=1)
    col = _lerp(col, SLEEVE_GREASE, np.clip(1 - gd / 0.035, 0, 1) ** 0.7 * np.clip(0.6 + noise3(P, 90.0, 17), 0, 1) * 0.7)
    # the folds: crests rubbed paler, hollows dark
    col = _lerp(col, SLEEVE_FADED * 1.1, np.clip(curv - 0.35, 0, 1) * 0.5)
    col *= (1 - 0.5 * np.clip(-curv - 0.1, 0, 1))[:, None]
    return col


def set_colors(me, col):
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.concatenate((col, np.ones((len(col), 1))), axis=1).ravel())
    me.color_attributes.active_color = attr


# ------------------------------------------------------------------ the jacket's cuff and sleeve

def loft(name, rings_, closed_end=False, vs=None):
    """A tube through `rings_` (each a list of points, all the same count), open at the start; UVs u round (0..1), v
    along (0..1, or `vs`: each ring's own v, e.g. metres along the arm, for the shader's seams and weave)."""
    bm = bmesh.new()
    nr = len(rings_[0])
    R = [[bm.verts.new(p) for p in ring] for ring in rings_]
    uv = bm.loops.layers.uv.new("UVMap")
    nv = len(R) - 1
    V_ = vs if vs is not None else [i / nv for i in range(nv + 1)]
    for i in range(nv):
        for j in range(nr):
            f_ = bm.faces.new((R[i][j], R[i][(j + 1) % nr], R[i + 1][(j + 1) % nr], R[i + 1][j]))
            for lp, (uu, vv) in zip(f_.loops, ((j, i), (j + 1, i), (j + 1, i + 1), (j, i + 1))):
                lp[uv].uv = (uu / nr, V_[vv])
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


# (2026-10-15, the owner: "make the player's coat ... cover the sleeve of the glove") The jacket's knitted storm cuff
# (knit_cuff, v5) is gone: the sleeve's hem comes down to the wrist over the glove's whole gauntlet instead.
HEM_AT = 0.004   # (how far behind the wrist joint the sleeve's hem lies; v5's was 0.058, its knitted cuff out of it)


def sleeve(hand, length=0.304, seed=7):
    """The jacket's sleeve over the forearm: loose canvas from the wrist (over the glove's gauntlet) back past the elbow,
    folds gathered toward its hem; a turned hem."""
    T, b = hand.toward, hand.b
    d = -T
    up = (b - d * b.dot(d)).normalized()
    rng = np.random.default_rng(seed)
    phase = rng.uniform(0, math.tau, 6)
    nr = 56
    # (2026-10-15, the owner: "so that it covers the sleeve of the glove") the hem down to the wrist, over the glove's
    # whole gauntlet: no knitted cuff, no wrist, the glove going straight into the sleeve. Its far end stays where it was.
    c0 = hand.w + T * HEM_AT - up * 0.002
    rings_, vs = [], []
    # (the hem's turn-up: inside, round the edge, out; the edge a little ragged, worn through in places)
    rag = rng.uniform(-1, 1, nr)
    for along, k, v_ in ((0.008, 0.86, -0.0095), (0.0015, 0.93, -0.0015), (0.0, 1.0, 0.0)):
        rings_.append(ellipse_ring(c0 + T * along, d, up, 0.0372 * k, 0.0292 * k, nr,
                                   lambda a: 1.0 + 0.03 * math.sin(a * 2 + phase[0]) + (0.012 * rag[int(round(a / math.tau * nr)) % nr] if k < 1 else 0)))
        vs.append(v_)
    # (2026-10-14g) the canvas bunched above the cuff: a few soft ridges stacked up the forearm, each only part of the way
    # round and wandering, as a sleeve pushed back over a glove gathers; creases at the elbow on its inside
    ridges = [(0.013, 0.075), (0.029, 0.07), (0.047, 0.06), (0.068, 0.045), (0.093, 0.03)]
    rph = rng.uniform(0, math.tau, (len(ridges), 3))
    alongs = [0.004 + 0.005 * i for i in range(20)] + [0.104 + (length - 0.104) * i / 15 for i in range(1, 16)]
    for along in alongs:
        t = along / length
        base = 1.0 + 0.12 * min(1, t * 2.2)
        gather = math.exp(-t * 4)

        def fn(a, t=t, gather=gather, along=along):
            fold = 0.0
            for k in range(3):
                fold += math.sin(a * (2 + k) + phase[k] + t * (14 + 5 * k)) * (0.06 - 0.015 * k)
            fold += math.sin(t * 38 + phase[3] + a) * 0.05 * gather
            r = 1.0 + fold * (0.5 + gather) * min(1.0, 0.3 + t * 6) + 0.03 * math.sin(a * 2 + phase[0])
            for (rc, amp), (p1, p2, p3) in zip(ridges, rph):
                c_ = rc + 0.006 * math.sin(a + p1) + 0.003 * math.sin(a * 3 + p2)
                part = max(0.0, 0.35 + 0.65 * math.sin(a + p3))
                r += amp * part * math.exp(-((along - c_) / 0.0062) ** 2) * min(1.0, along / 0.01)
            # (the elbow's creases, on the inside of the arm: underneath here)
            inside = max(0.0, -math.sin(a - 0.3))
            for ec in (0.244, 0.262, 0.28, 0.296):   # (2026-10-15: where they were, the hem moved 5.4 cm toward the hand)
                r += 0.03 * inside * math.exp(-((along - ec - 0.008 * math.sin(a * 2 + phase[4])) / 0.006) ** 2)
            return r
        rings_.append(ellipse_ring(c0 + T * along, d, up, 0.0372 * base, 0.0292 * base, nr, fn))
        vs.append(along)
    ob = loft("Sleeve", rings_, closed_end=True, vs=vs)
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
    # (2026-10-14g) the dense skin's colours (its wear, grime, scuffs, scratches, the seams' thread: finer than the light
    # mesh's vertices could hold) into the glove's own colour map
    alb_name = img_name.replace("_normal", "_albedo")
    alb = bpy.data.images.new(alb_name, TEX, TEX, alpha=False, float_buffer=False)
    alb.colorspace_settings.name = "sRGB"
    atex = nt.nodes.new("ShaderNodeTexImage")
    atex.image = alb
    hm = high.data.materials[0]
    hm.use_nodes = True
    hn = hm.node_tree
    for nd in list(hn.nodes):
        if nd.type != "OUTPUT_MATERIAL":
            hn.nodes.remove(nd)
    at_ = hn.nodes.new("ShaderNodeAttribute")
    at_.attribute_name = "Col"
    em = hn.nodes.new("ShaderNodeEmission")
    hn.links.new(at_.outputs["Color"], em.inputs["Color"])
    hn.links.new(em.outputs["Emission"], hn.nodes["Material Output"].inputs["Surface"])
    nt.nodes.active = atex
    select_only(high, low)
    bpy.ops.object.bake(type="EMIT")
    alb.filepath_raw = os.path.join(PREV, alb_name + ".png")
    alb.file_format = "PNG"
    alb.save()
    alb.pack()
    bsdf = nt.nodes["Principled BSDF"]
    for l_ in list(bsdf.inputs["Base Color"].links):
        nt.links.remove(l_)
    nt.links.new(atex.outputs["Color"], bsdf.inputs["Base Color"])
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
    sl = sleeve(hand)
    if LOOK:
        for nm_, c_ in (("palm", (0.35, 0.3, 0.26)), ("finger", (0.3, 0.32, 0.4)), ("thumb", (0.45, 0.25, 0.2))):
            mm = bpy.data.materials.new(nm_)
            mm.diffuse_color = (*c_, 1.0)
            low.data.materials.append(mm)
        bpy.context.scene.display.shading.color_type = "MATERIAL"
        set_colors(low.data, np.tile(GLOVE_WORN * 3, (len(low.data.vertices), 1)))
        set_colors(sl.data, np.tile(SLEEVE * 4, (len(sl.data.vertices), 1)))
        print(f"[fp-arm] {pose}: {tri_count(low)} glove triangles (look only)")
        return low
    high = make_skin(hand, 4, "High_" + pose)
    dark, stitch = carve(high, hand)
    hcol, _, _ = glove_colour(high, hand, dark, stitch, smooth=6)
    set_colors(high.data, hcol)
    unwrap(low)
    ao = bake(high, low, "fp_hand_L_" + pose + "_normal")
    bpy.data.objects.remove(high)
    # (2026-10-14g) the glove's colour is its baked map now; its vertices carry what the shader needs besides: the
    # occlusion (r), how worn and polished it is (g: a sheen on the knuckles and fingertips), the palm (b)
    _, wear, palm = glove_colour(low, hand)
    set_colors(low.data, np.stack((ao, wear, palm), 1))
    # the cuff and the sleeve: their own cloths, coloured in their vertices (the weave, seams and stitching the shader's)
    for ob_, kind, mname, rough in ((sl, 2, "fp_sleeve", 0.9),):
        set_colors(ob_.data, cloth_colour(ob_, hand, kind))
        ob_.data.materials.append(flat_material(mname, rough))
    select_only(sl, low)
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
    sh.color_type = "MATERIAL" if LOOK else "TEXTURE"
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
