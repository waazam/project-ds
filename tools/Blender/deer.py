"""Act 1's doe, modelled, baked, rigged and animated in Blender (the owner, 2026-10-15: "from now on everything should be
made in blender"; the code-built doe read "too circular and polygonal").

    blender -b --python tools/Blender/deer.py

Writes:
  assets/models/deer/deer.glb   one skin on its skeleton, with its clips: graze, head_up, walk, trot, bound, ear_twitch
                                (ear_twitch keys only the ears: the game plays it over the others)
  build/blender/deer_*.png      previews

Anatomy, from the sections out: the barrel deeper than wide, a ridge down the spine, the shoulder blades and the
haunches standing in it, the waist tucked; the neck tall and flattened side to side with a deeper throat; the head a
wedge (broad forehead, flat cheeks, narrow muzzle); the legs' own sections (the forearm and gaskin muscled teardrops,
the knee a flat bony front, the point of the hock, the cannon a narrow oval with its tendon ridge behind, the fetlock,
the pastern), all one skin through a voxel remesh; split hooves and dew claws, almond eyes, thin cupped leaf ears;
a flat tail, white beneath. Fur cards along the back, the belly, the throat, the backs of the thighs and the tail.

Game space (Deer.cs): the origin on the ground between the feet, facing -z, y up; the withers about 1.06 m (the game
scales it by ModelScale 1.05).
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, join, apply_mods, add_mod, obj_from_bmesh, ROOT
import wendigo as W
from animal_kit import P, G, key, bell, sup, catmull, loft, fuse, roughen, hair_image, hair_material, Cards, surface_points, bones_fk, pose, clip, bake_maps, smooth_weights

V = Vector
OUT = os.path.join(ROOT, "assets", "models", "deer")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
rnd = random.Random(5)
TEXSIZE = 1024

# ------------------------------------------------------------------ the skeleton (game space), the rest pose: standing,
# head up

LEGX = 0.1
BONES_G = [
    ("pelvis", (0, 0.9, 0.44), (0, 0.88, 0.14), "root", 0.15),
    ("spine", (0, 0.88, 0.14), (0, 0.88, -0.16), "pelvis", 0.15),
    ("chest", (0, 0.88, -0.16), (0, 0.9, -0.4), "spine", 0.16),
    ("neck", (0, 0.92, -0.42), (0, 1.12, -0.58), "chest", 0.06),
    ("neck2", (0, 1.12, -0.58), (0, 1.3, -0.73), "neck", 0.05),
    ("head", (0, 1.33, -0.72), (0, 1.22, -1.0), "neck2", 0.045),
    ("tail", (0, 0.94, 0.56), (0, 0.79, 0.65), "pelvis", 0.025),
]
for s, sx in ((-1, "L"), (1, "R")):
    x = s * LEGX
    BONES_G += [
        (f"ear_{sx}", (s * 0.04, 1.375, -0.72), (s * 0.115, 1.5, -0.75), "head", 0.015),
        (f"upperarm_{sx}", (x, 0.8, -0.37), (x, 0.6, -0.33), "chest", 0.045),
        (f"forearm_{sx}", (x, 0.6, -0.33), (x, 0.36, -0.355), f"upperarm_{sx}", 0.03),
        (f"fcannon_{sx}", (x, 0.36, -0.355), (x, 0.09, -0.352), f"forearm_{sx}", 0.018),
        (f"fpastern_{sx}", (x, 0.09, -0.352), (x, 0.0, -0.378), f"fcannon_{sx}", 0.016),
        (f"thigh_{sx}", (x, 0.84, 0.4), (x, 0.6, 0.33), "pelvis", 0.06),
        (f"gaskin_{sx}", (x, 0.6, 0.33), (x, 0.4, 0.51), f"thigh_{sx}", 0.035),
        (f"hcannon_{sx}", (x, 0.4, 0.51), (x, 0.09, 0.47), f"gaskin_{sx}", 0.018),
        (f"hpastern_{sx}", (x, 0.09, 0.47), (x, 0.0, 0.45), f"hcannon_{sx}", 0.016),
    ]


def bone_list():
    B = [("root", V((0, 0, 0)), V((0, 0, 0.3)), None, 0.0)]
    for n, h, t, p, r in BONES_G:
        B.append((n, P(*h), P(*t), p, r))
    return B


# ------------------------------------------------------------------ the sections

def body_part():
    # rump (u 0) to the brisket (u 1): along +Y, so the frame's second axis is up (th 90 the spine, 270 the belly)
    pts = [P(0, 0.85, 0.67), P(0, 0.875, 0.56), P(0, 0.885, 0.4), P(0, 0.875, 0.18), P(0, 0.865, -0.05), P(0, 0.86, -0.28), P(0, 0.84, -0.46), P(0, 0.8, -0.62)]

    def sec(u, th):
        f = 1 - u   # 0 the chest's front .. 1 the rump
        w = key(f, [(0, 0.03), (0.08, 0.1), (0.2, 0.14), (0.4, 0.15), (0.58, 0.142), (0.75, 0.153), (0.9, 0.12), (1, 0.03)])
        h = key(f, [(0, 0.04), (0.08, 0.15), (0.2, 0.205), (0.4, 0.19), (0.58, 0.165), (0.75, 0.172), (0.9, 0.14), (1, 0.04)])
        x, e = sup(th, w, h, 2.4)
        if e < 0:
            e *= key(f, [(0, 1.0), (0.3, 1.0), (0.55, 0.82), (0.75, 0.95), (1, 1)])   # the waist tucked up
        e += 0.012 * bell(th, math.pi / 2, 0.22)                                         # the spine's ridge
        blade = key(f, [(0.1, 0), (0.22, 1), (0.36, 0)])
        haunch = key(f, [(0.62, 0), (0.78, 1), (0.92, 0)])
        out = 0.016 * blade * (bell(th, math.pi / 2 - 0.62, 0.32) + bell(th, math.pi / 2 + 0.62, 0.32)) \
            + 0.02 * haunch * (bell(th, 0.35, 0.5) + bell(th, math.pi - 0.35, 0.5))
        r = math.hypot(x, e) or 1e-6
        k = 1 + out / r + 0.025 * noise.noise(V((math.cos(th) * 2, math.sin(th) * 2, f * 5)))
        return x * k, e * k

    return loft("Body", pts, sec, rings=64, sides=40, close=(0.08, 0.08))


def neck_part():
    pts = [P(0, 0.8, -0.34), P(0, 0.95, -0.46), P(0, 1.1, -0.58), P(0, 1.23, -0.68), P(0, 1.33, -0.74)]

    def sec(u, th):
        w = key(u, [(0, 0.1), (0.2, 0.078), (0.5, 0.056), (0.8, 0.047), (1, 0.048)])
        d = key(u, [(0, 0.13), (0.2, 0.11), (0.5, 0.085), (0.8, 0.07), (1, 0.065)])
        x, e = sup(th, w, d, 2.2)
        if e < 0:
            e *= 1.0 + 0.16 * key(u, [(0, 0.3), (0.5, 1), (1, 0.6)])   # the throat, deeper
        return x, e

    return loft("Neck", pts, sec, rings=36, sides=32, close=(0.0, 0.0))


def head_part():
    pts = [P(0, 1.36, -0.68), P(0, 1.34, -0.77), P(0, 1.3, -0.86), P(0, 1.262, -0.94), P(0, 1.232, -0.995), P(0, 1.212, -1.02)]

    def sec(u, th):
        w = key(u, [(0, 0.04), (0.12, 0.058), (0.35, 0.054), (0.6, 0.037), (0.85, 0.024), (1, 0.017)])
        h = key(u, [(0, 0.046), (0.12, 0.064), (0.35, 0.058), (0.6, 0.044), (0.85, 0.03), (1, 0.02)])
        x, e = sup(th, w, h, 3.0)
        x *= 0.72 + 0.33 * (math.sin(th) + 1) / 2   # the wedge: broad forehead, narrow jaw
        # the cheekbone below the eye, the brow over it
        x += math.copysign(0.004, x) * key(u, [(0.2, 0), (0.35, 1), (0.5, 0)]) * bell(th, 0.0, 0.5) * (1 if math.cos(th) > 0 else 1)
        return x, e

    return loft("Head", pts, sec, rings=36, sides=32, close=(0.12, 0.05))


def leg_part(sx, hind):
    s = -1 if sx == "L" else 1
    x = s * LEGX
    if hind:
        g = [(x, 0.98, 0.38), (x, 0.84, 0.4), (x, 0.6, 0.33), (x, 0.5, 0.43), (x, 0.4, 0.51), (x, 0.25, 0.49), (x, 0.09, 0.47), (x, 0.045, 0.455)]
        W_ = [(0.0, 0.014), (0.05, 0.016), (0.09, 0.02), (0.13, 0.016), (0.34, 0.016), (0.4, 0.024), (0.46, 0.03), (0.56, 0.044), (0.66, 0.058), (0.8, 0.05), (0.92, 0.03), (1.0, 0.02)]
        D_ = [(0.0, 0.016), (0.05, 0.018), (0.09, 0.026), (0.13, 0.021), (0.34, 0.022), (0.4, 0.034), (0.46, 0.045), (0.56, 0.06), (0.66, 0.095), (0.8, 0.09), (0.92, 0.06), (1.0, 0.03)]
    else:
        g = [(x, 0.95, -0.37), (x, 0.8, -0.37), (x, 0.6, -0.33), (x, 0.47, -0.345), (x, 0.36, -0.355), (x, 0.22, -0.355), (x, 0.09, -0.352), (x, 0.045, -0.37)]
        W_ = [(0.0, 0.014), (0.05, 0.016), (0.09, 0.02), (0.13, 0.016), (0.3, 0.016), (0.36, 0.026), (0.42, 0.025), (0.55, 0.033), (0.62, 0.046), (0.74, 0.045), (0.86, 0.03), (1.0, 0.018)]
        D_ = [(0.0, 0.016), (0.05, 0.018), (0.09, 0.026), (0.13, 0.02), (0.3, 0.021), (0.36, 0.031), (0.42, 0.03), (0.55, 0.042), (0.62, 0.065), (0.74, 0.07), (0.86, 0.05), (1.0, 0.025)]
    pts = [P(*p) for p in g]

    def sec(u, th):
        h = G(catmull(pts, u)).y
        w, d = key(h, W_), key(h, D_)
        front = math.sin(th) > 0   # (the frame's second axis is the leg's front here)
        flat = key(h, [(0.3, 0), (0.36, 1), (0.42, 0)]) if not hind else 0.0
        xx, e = sup(th, w, d, 2.0 + (2.2 * flat if front else 0.0))   # the knee: a flat bony front
        # the tendon down the back of the cannon
        tend = key(h, [(0.1, 0), (0.14, 1), (0.3, 1), (0.35, 0)])
        e -= 0.0045 * tend * bell(th, 1.5 * math.pi, 0.35)
        if hind:
            hock = key(h, [(0.35, 0), (0.4, 1), (0.45, 0)])
            e -= 0.013 * hock * bell(th, 1.5 * math.pi, 0.45)   # the point of the hock
            if not front:
                e *= 1 + 0.12 * key(h, [(0.44, 0), (0.52, 1), (0.6, 0)])   # the gaskin's muscle behind
        else:
            if front:
                e *= 1 + 0.1 * key(h, [(0.42, 0), (0.5, 1), (0.58, 0)])     # the forearm's muscle in front
        k = 1 + 0.03 * noise.noise(V((math.cos(th) * 3, math.sin(th) * 3, h * 9 + s)))
        return xx * k, e * k

    return loft(f"Leg{'H' if hind else 'F'}{sx}", pts, sec, rings=72, sides=24, close=(0.1, 0.05))


def tail_part():
    pts = [P(0, 0.955, 0.54), P(0, 0.91, 0.61), P(0, 0.84, 0.645), P(0, 0.785, 0.655)]

    def sec(u, th):
        w = key(u, [(0, 0.025), (0.3, 0.042), (0.7, 0.036), (1, 0.012)])
        return sup(th, w, 0.014, 2.5)

    return loft("Tail", pts, sec, rings=24, sides=20, close=(0.0, 0.12))


def ear_part(sx):
    s = -1 if sx == "L" else 1
    pts = [P(s * 0.04, 1.37, -0.72), P(s * 0.078, 1.45, -0.735), P(s * 0.12, 1.52, -0.755)]

    def sec(u, th):
        w = key(u, [(0, 0.016), (0.35, 0.045), (0.72, 0.038), (1, 0.005)])
        x = w * math.cos(th)
        e = 0.0045 * math.sin(th) + 0.009 * (math.cos(th) ** 2) - 0.004   # thin, and cupped: its edges back, a hollow in front
        return x, e

    return loft(f"Ear{sx}", pts, sec, rings=20, sides=18, close=(0.0, 0.05))


def hooves(sx, hind):
    """The cloven hoof: two claws, each a half-heart in plan (its outer wall rounded, its inner face flat against the
    other's, its toe a point), sloping forward from the coronet to a flat sole, a little apart at the toes; and the
    two dew claws behind the fetlock, small pointed horns angled down and back. Their own glossy material."""
    s = -1 if sx == "L" else 1
    x = s * LEGX
    z = 0.455 if hind else -0.37
    zl = 0.47 if hind else -0.352   # the leg's centre at the fetlock
    parts = []
    for k in (-1, 1):
        # (round 2: the first ones stood above the pastern as a wide cuff, a boot: now they rise no higher than its end,
        # narrow at the coronet where the leg goes in and flaring to the ground, the cleft between them open at the toe)
        pts = [P(x + k * 0.0072, 0.046, z + 0.006), P(x + k * 0.0078, 0.022, z - 0.004), P(x + k * 0.0084, 0.0, z - 0.013)]

        def sec(u, th, k=k):
            L = key(u, [(0, 0.012), (1, 0.025)])     # half its length, heel to toe
            W_ = key(u, [(0, 0.0062), (1, 0.009)])   # half its width
            c, sn = math.cos(th), math.sin(th)
            xx, e = W_ * c, L * sn
            if xx * k < 0:
                xx *= 0.22                            # the inner face, flat, the cleft between
            if e > 0:
                f = (e / L) ** 1.4
                xx *= 1 - 0.85 * f                    # the toe drawn to a point
            return xx, e

        parts.append(loft(f"Hoof{sx}{k}", pts, sec, rings=9, sides=20, close=(0.0, 0.0)))
        # the dew claw on the back of the fetlock, set into it, short
        dc = [P(x + k * 0.008, 0.104, zl + 0.012), P(x + k * 0.0095, 0.092, zl + 0.019), P(x + k * 0.0105, 0.083, zl + 0.025)]
        parts.append(loft(f"Dew{sx}{k}", dc, lambda u, th: sup(th, key(u, [(0, 0.0045), (1, 0.0014)]), key(u, [(0, 0.005), (1, 0.0016)]), 2.2),
                          rings=8, sides=12, close=(0.0, 0.25)))
    return parts


def eyes():
    out = []
    for s in (-1, 1):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=1.0)
        for v in bm.verts:
            v.co = V((v.co.x * 0.006, v.co.y * 0.016, v.co.z * 0.011))
        ob = obj_from_bmesh(bm, f"Eye{s}")
        ob.location = P(s * 0.05, 1.338, -0.81)
        ob.rotation_euler = (0, 0, s * 0.35)
        out.append(ob)
    return out


# ------------------------------------------------------------------ the coat

HIDE, BACK, PALE = (0.24, 0.14, 0.075), (0.14, 0.085, 0.045), (0.7, 0.64, 0.52)
DARK, MUZZLE, HOOF, INNER = (0.012, 0.01, 0.009), (0.1, 0.07, 0.05), (0.02, 0.018, 0.016), (0.5, 0.36, 0.3)


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def ss(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a))) if b != a else 0.0
    return t * t * (3 - 2 * t)


def coat(co, nrm):
    g, n = G(co), G(nrm)   # (the game's space: y up, facing -z)
    c = mix(HIDE, BACK, ss(0.45, 0.95, n.y) * 0.85)
    pale = 0.0
    if g.z > -0.5 and g.y > 0.6 and g.y < 1.0:
        pale = max(pale, ss(-0.25, -0.7, n.y) * ss(0.86, 0.74, g.y))          # the belly
    pale = max(pale, ss(0.5, 0.62, g.z) * ss(0.15, 0.6, n.z) * ss(0.72, 0.84, g.y))   # the rump patch
    if g.y > 0.95:
        pale = max(pale, ss(-0.1, -0.6, n.z) * ss(0.1, -0.5, n.y) * ss(1.28, 1.0, g.y) * ss(-0.36, -0.5, g.z) * ss(-0.74, -0.68, g.z))   # the throat
    if g.y < 0.72:
        pale = max(pale, ss(0.55, 0.95, abs(n.x)) * 0.4 * (1 if (n.x * g.x) < 0 else 0))   # inside the legs
    c = mix(c, PALE, pale)
    if g.y < 0.34:
        c = mix(c, (0.17, 0.12, 0.085), 0.4)   # the lower legs greyer
    if g.z < -0.68 and g.y > 1.15:
        # the face: the muzzle darker, the chin and an eye ring pale, the nose black
        c = mix(c, MUZZLE, ss(-0.9, -0.97, g.z) * 0.7)
        c = mix(c, PALE, ss(1.245, 1.215, g.y) * ss(-0.88, -0.96, g.z) * 0.7)
        for sx in (-1, 1):
            de = (g - V((sx * 0.05, 1.338, -0.81))).length
            c = mix(c, PALE, ss(0.03, 0.018, de) * 0.8)
        c = mix(c, DARK, ss(-0.995, -1.012, g.z) * ss(1.255, 1.225, g.y))
    if g.y > 0.92 and g.z > 0.5:
        c = mix(c, PALE, ss(0.1, -0.5, n.z))   # under the tail
    # (a faint, broad unevenness only: the strong fine dapple read as camouflage)
    d = 0.5 + 0.5 * noise.noise(V((g.x * 3.5, g.y * 3.5, g.z * 3.5)))
    return tuple(ch * (0.96 + 0.08 * d) for ch in c)


def flat(rgb):
    return lambda co, nrm: rgb


# ------------------------------------------------------------------ fur

def fur(skin):
    cards = Cards()
    pts = surface_points(skin, lambda co, n: True, 0.016)

    def put(root, d, across, length, width, col, n):
        # each card turned a little about its own hair and its length varied: cards all in one plane and one length read
        # as a comb of stripes (2026-10-15)
        d = d.normalized()
        across = Quaternion(d, rnd.uniform(-0.6, 0.6)) @ across
        tipc = tuple(ch * 1.04 for ch in col)
        cards.card(root, P(*(d + V((rnd.uniform(-0.15, 0.15), rnd.uniform(-0.1, 0.1), rnd.uniform(-0.15, 0.15)))).normalized()),
                   P(*across), length * rnd.uniform(0.75, 1.1), width, col, tipc, normal=n)

    for co, n in pts:
        g, gn = G(co), G(n)
        back = V((0, 0, 1))
        root = co - n * 0.002
        col = coat(co, n)
        # where the silhouette is: along the spine, under the belly, the throat, the backs of the thighs, the tail
        if gn.y > 0.8 and 0.55 < g.y < 1.1 and -0.5 < g.z < 0.62:
            put(root, gn * 0.35 + back, V((1, 0, 0)), 0.03, 0.03, col, n)
        elif gn.y < -0.75 and 0.6 < g.y < 0.8 and -0.45 < g.z < 0.35:
            # the belly: fewer, shorter, the coat's own colour toward the hide (its pale paint, brightened at the tips,
            # read as a bluish-white comb against the dark ground)
            if rnd.random() < 0.45:
                put(root, V((0, -1, 0.5)), V((1, 0, 0)), 0.024, 0.028, mix(col, HIDE, 0.55), n)
        elif g.y > 0.95 and g.y < 1.32 and gn.z < -0.55 and g.z < -0.38:
            put(root, gn * 0.5 + V((0, -1, 0)), V((1, 0, 0)), 0.026, 0.028, mix(col, HIDE, 0.25), n)
        elif 0.5 < g.y < 0.85 and g.z > 0.42 and gn.z > 0.55:
            put(root, gn + V((0, -0.8, 0)), V((0, 1, 0)), 0.024, 0.026, col, n)
        elif g.z > 0.58 and g.y > 0.78 and g.y < 0.95 and abs(gn.x) > 0.7:
            put(root, gn * 0.6 + V((0, -0.6, 0.2)), V((0, 1, 0)), 0.022, 0.024, col, n)
    print(f"[deer] fur cards: {len(cards.col) // 4}")
    return cards


# ------------------------------------------------------------------ the clips

def gait_rot(t, phases, swing, flex_f, flex_h, lift_back=0.0):
    """Every leg at its own phase of a cycle: the upper bone swinging, the lower folding as it comes through."""
    rot = {}
    for sx, legs in (("L", (0, 2)), ("R", (1, 3))):
        for i, (upper, mid, lower, past, fore) in enumerate(((f"upperarm_{sx}", f"forearm_{sx}", f"fcannon_{sx}", f"fpastern_{sx}", True),
                                                             (f"thigh_{sx}", f"gaskin_{sx}", f"hcannon_{sx}", f"hpastern_{sx}", False))):
            ph = phases[(sx, fore)]
            a = math.sin(math.tau * (t + ph))
            through = max(0.0, math.sin(math.tau * (t + ph) + 1.3))
            rot[upper] = [("X", swing * a)]
            if fore:
                rot[mid] = [("X", -0.15 * through)]
                rot[lower] = [("X", -flex_f * through)]
                rot[past] = [("X", -0.5 * through)]
            else:
                rot[mid] = [("X", 0.25 * through)]
                rot[lower] = [("X", -flex_h * through)]
                rot[past] = [("X", -0.5 * through)]
    return rot


def merge(*ds):
    out = {}
    for d in ds:
        for k, v in d.items():
            out.setdefault(k, []).extend(v)
    return out


def animate(ao):
    bones_fk(ao)
    ao.animation_data_create()
    body = ["root", "pelvis", "spine", "chest", "neck", "neck2", "head", "tail"] + [b[0] for b in BONES_G if b[0].startswith(("upperarm", "forearm", "fcannon", "fpastern", "thigh", "gaskin", "hcannon", "hpastern"))]
    allb = body + ["ear_L", "ear_R"]

    def graze(t):
        chew = math.sin(t * math.tau * 4) * 0.03
        sway = math.sin(t * math.tau) * 0.03
        # (the bend shared down the neck's length, the least at its root: all at the root folded the chest's front in)
        return merge({"neck": [("X", 0.7)], "neck2": [("X", 0.62 + sway)], "head": [("X", 0.22 + chew)], "chest": [("X", 0.08)],
                      "tail": [("X", 0.05 * math.sin(t * math.tau * 2))]}), V((0, 0, 0))

    def head_up(t):
        b = math.sin(t * math.tau)
        look = math.sin(t * math.tau + 1) * 0.12
        return {"neck": [("X", -0.05 + 0.02 * b)], "neck2": [("X", 0.02)], "head": [("Z", look), ("X", 0.04 * b)], "spine": [("X", 0.01 * b)]}, V((0, 0, 0.003 * b))

    walk_ph = {("L", False): 0.0, ("L", True): 0.25, ("R", False): 0.5, ("R", True): 0.75}
    trot_ph = {("L", False): 0.0, ("R", True): 0.0, ("R", False): 0.5, ("L", True): 0.5}
    bound_ph = {("L", True): 0.0, ("R", True): 0.06, ("L", False): 0.5, ("R", False): 0.56}

    def walk(t):
        nod = math.sin(t * math.tau * 2)
        return merge(gait_rot(t, walk_ph, 0.32, 0.9, 0.7), {"neck": [("X", 0.15 + 0.04 * nod)], "head": [("X", -0.05)]}), V((0, 0, 0.008 * nod))

    def trot(t):
        nod = math.sin(t * math.tau * 2)
        return merge(gait_rot(t, trot_ph, 0.45, 1.1, 0.9), {"neck": [("X", 0.05 + 0.05 * nod)], "head": [("X", 0.0)], "tail": [("X", -0.6)]}), V((0, 0, 0.02 * abs(nod)))

    def bound(t):
        s = math.sin(t * math.tau)
        flex = {"spine": [("X", 0.08 * s)], "chest": [("X", 0.06 * s)], "pelvis": [("X", -0.05 * s)], "neck": [("X", -0.15 + 0.15 * s)], "tail": [("X", -1.1 + 0.15 * s)]}
        return merge(gait_rot(t, bound_ph, 0.75, 1.3, 1.0), flex), V((0, 0, 0.04 * max(0.0, s)))

    def ear_twitch(t):
        k = math.sin(t * math.pi)
        return {"ear_L": [("Z", 0.6 * k)], "ear_R": [("Z", -0.25 * k)]}, None

    clip(ao, "graze", 120, graze, keyed=allb)
    clip(ao, "head_up", 90, head_up, keyed=allb)
    clip(ao, "walk", 36, walk, keyed=allb)
    clip(ao, "trot", 21, trot, keyed=allb)
    clip(ao, "bound", 13, bound, keyed=allb)
    clip(ao, "ear_twitch", 12, ear_twitch, keyed=["ear_L", "ear_R"], cyclic=False)


# ------------------------------------------------------------------ build

def main():
    clear()
    skin_mat = mat("DeerHide", (0.24, 0.14, 0.075), rough=0.95)
    parts = [body_part(), neck_part(), head_part(), tail_part()] + [leg_part(sx, h) for sx in ("L", "R") for h in (False, True)]
    skin = fuse(parts, "DeerSkin", voxel=0.0035, smooth=5, material=skin_mat)
    roughen(skin, 0.0005, 30.0)
    W.paint(skin, coat)
    W.detail_attr(skin, 0.35)
    W.tag(skin, "flesh")
    print(f"[deer] fused skin: {W.tri_count(skin)} triangles")
    hair = hair_material(hair_image("deer_hair", strands=110))
    cards = fur(skin).object("DeerFur", hair)
    # the rigid parts: ears, eyes (baked with the skin); the hooves their own mesh, dark glossy keratin
    rigid = []
    hoof_parts = []
    for sx in ("L", "R"):
        e = ear_part(sx)
        W.paint(e, lambda co, n: mix(coat(co, n), INNER, ss(-0.2, -0.7, G(n).z) * 0.8))
        W.detail_attr(e, 0.2)
        W.tag(e, f"ear_{sx}")
        rigid.append(e)
        for hind, past in ((False, f"fpastern_{sx}"), (True, f"hpastern_{sx}")):
            for h in hooves(sx, hind):
                W.tag(h, past)
                hoof_parts.append(h)
    hoof_mat = mat("DeerHoof", (0.022, 0.018, 0.016), rough=0.28)
    hoof_ob = join(hoof_parts, "DeerHooves")
    hoof_ob.data.materials.clear()
    hoof_ob.data.materials.append(hoof_mat)
    for p in hoof_ob.data.polygons:
        p.use_smooth = True
    for e in eyes():
        W.paint(e, flat((0.004, 0.003, 0.003)))
        W.detail_attr(e, 0.0)
        W.tag(e, "head")
        rigid.append(e)
    for r in rigid:
        r.data.materials.clear()
        r.data.materials.append(skin_mat)
    # high (dense, for the bake) and low (decimated, for the game)
    hcopies = []
    for o in [skin] + rigid:
        c = o.copy()
        c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        hcopies.append(c)
    high = join(hcopies, "High")
    ratio = min(1.0, 8500 / max(W.tri_count(skin), 1))
    add_mod(skin, "DECIMATE", ratio=ratio)
    apply_mods(skin)
    low = join([skin] + rigid, "DeerMesh")
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.002)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[deer] low {W.tri_count(low)} triangles, high {W.tri_count(high)}, fur {W.tri_count(cards)}")
    cards.hide_render = True   # (its cards out of the bake: their shadows baked into the coat read as camouflage)
    albedo, normal = bake_maps(low, high, "deer", TEXSIZE, extrusion=0.005, dist=0.012, ao=0.16)
    cards.hide_render = False
    bpy.data.objects.remove(high, do_unlink=True)
    W.final_materials(low, albedo, normal)
    bones = bone_list()
    ao = W.make_armature("Deer", bones)
    W.skin_weights(low, ao, bones)
    W.skin_weights(cards, ao, bones)
    W.skin_weights(hoof_ob, ao, bones)
    for ob in (low, cards, hoof_ob):
        for vg in list(ob.vertex_groups):
            if vg.name.startswith("P_"):
                ob.vertex_groups.remove(vg)
    smooth_weights(low)
    animate(ao)
    W.export_glb([ao, low, cards, hoof_ob], os.path.join(OUT, "deer.glb"))
    if "--no-preview" not in sys.argv:
        previews(ao)


def previews(ao):
    from kit import preview
    for act, frame, name, cam in (("head_up", 0, "deer_side", ((0, 0, 0.8), 3.2, 0.3, 90)), ("graze", 30, "deer_graze", ((0, 0.2, 0.7), 3.0, 0.4, 50)),
                                  ("walk", 9, "deer_walk", ((0, 0, 0.8), 3.2, 0.3, 90)), ("bound", 4, "deer_bound", ((0, 0, 0.8), 3.4, 0.2, 90)),
                                  ("head_up", 0, "deer_head", ((0, 0.86, 1.3), 0.9, 0.05, 55)), ("head_up", 0, "deer_legs", ((0.1, 0.0, 0.35), 1.1, 0.0, 80))):
        ao.animation_data.action = bpy.data.actions[act]
        bpy.context.scene.frame_set(frame)
        preview(name, *cam)


main()
