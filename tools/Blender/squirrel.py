"""Act 1's grey squirrel, modelled, baked, rigged and animated in Blender (the owner, 2026-10-15: "from now on everything
should be made in blender"; the code-built one read "too circular and polygonal").

    blender -b --python tools/Blender/squirrel.py

Writes:
  assets/models/squirrel/squirrel.glb   one skin on its skeleton, with its clips: forage, sit_up, hop, run, climb,
                                        tail_flick (tail_flick keys only the tail: the game plays it over the others)
  build/blender/squirrel_*.png          previews

The body a pear with defined haunches, the neck blended into a head that's a short wedge (a flat top, full cheeks, a
blunt snout), almond eyes, the ears thin tapered leaves with tufts; forearms and paws, long flat hind feet; one skin
through a voxel remesh. The tail is a plume of many fanned fur cards round a slim core, one chain of four bones from
base to tip (so it bends in an S and has no seam), its hair long and pale at the tips. Fur cards along the back and
the belly too.

Game space (Squirrel.cs): the origin on the ground under the body, facing -z, y up; the game scales it by
ModelScale 1.35.
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, join, apply_mods, add_mod, obj_from_bmesh, ROOT
import wendigo as W
from animal_kit import P, G, key, bell, sup, catmull, loft, fuse, roughen, hair_image, hair_material, Cards, surface_points, bones_fk, pose, clip, bake_maps, smooth_weights

V = Vector
OUT = os.path.join(ROOT, "assets", "models", "squirrel")
os.makedirs(OUT, exist_ok=True)
rnd = random.Random(11)
TEXSIZE = 512

TAIL = [(0, 0.085, 0.1), (0, 0.14, 0.135), (0, 0.21, 0.15), (0, 0.28, 0.12), (0, 0.32, 0.065)]

BONES_G = [
    ("hips", (0, 0.08, 0.09), (0, 0.078, 0.0), "root", 0.045),
    ("chest", (0, 0.078, 0.0), (0, 0.074, -0.09), "hips", 0.04),
    ("neck", (0, 0.078, -0.09), (0, 0.1, -0.11), "chest", 0.022),
    ("head", (0, 0.108, -0.105), (0, 0.092, -0.18), "neck", 0.025),
    ("tail1", TAIL[0], TAIL[1], "hips", 0.016),
    ("tail2", TAIL[1], TAIL[2], "tail1", 0.016),
    ("tail3", TAIL[2], TAIL[3], "tail2", 0.016),
    ("tail4", TAIL[3], TAIL[4], "tail3", 0.014),
]
for s, sx in ((-1, "L"), (1, "R")):
    BONES_G += [
        (f"ear_{sx}", (s * 0.017, 0.128, -0.11), (s * 0.02, 0.152, -0.108), "head", 0.008),
        (f"arm_{sx}", (s * 0.02, 0.07, -0.072), (s * 0.022, 0.018, -0.094), "chest", 0.012),
        (f"paw_{sx}", (s * 0.022, 0.018, -0.094), (s * 0.021, 0.004, -0.114), f"arm_{sx}", 0.008),
        (f"thigh_{sx}", (s * 0.03, 0.08, 0.07), (s * 0.034, 0.012, 0.088), "hips", 0.022),
        (f"foot_{sx}", (s * 0.034, 0.012, 0.088), (s * 0.03, 0.004, 0.02), f"thigh_{sx}", 0.008),
    ]


def bone_list():
    B = [("root", V((0, 0, 0)), V((0, 0, 0.1)), None, 0.0)]
    for n, h, t, p, r in BONES_G:
        B.append((n, P(*h), P(*t), p, r))
    return B


# ------------------------------------------------------------------ the parts

def body_part():
    pts = [P(0, 0.07, 0.118), P(0, 0.082, 0.08), P(0, 0.086, 0.03), P(0, 0.081, -0.03), P(0, 0.076, -0.08), P(0, 0.072, -0.112)]

    def sec(u, th):
        f = 1 - u   # 0 the chest .. 1 the rump
        w = key(f, [(0, 0.012), (0.15, 0.027), (0.4, 0.034), (0.62, 0.044), (0.8, 0.047), (0.93, 0.034), (1, 0.012)])
        h = key(f, [(0, 0.016), (0.15, 0.035), (0.4, 0.04), (0.62, 0.045), (0.8, 0.047), (0.93, 0.035), (1, 0.014)])
        x, e = sup(th, w, h, 2.3)
        if e < 0:
            e *= 0.93   # (a deeper chest: less bare leg under it)
        haunch = key(f, [(0.6, 0), (0.78, 1), (0.95, 0)])
        out = 0.011 * haunch * (bell(th, -0.2, 0.55) + bell(th, math.pi + 0.2, 0.55))
        r = math.hypot(x, e) or 1e-6
        k = 1 + out / r + 0.03 * noise.noise(V((math.cos(th) * 2, math.sin(th) * 2, f * 6)))
        return x * k, e * k

    return loft("Body", pts, sec, rings=48, sides=32, close=(0.1, 0.1))


def neck_part():
    pts = [P(0, 0.078, -0.07), P(0, 0.092, -0.095), P(0, 0.108, -0.11)]
    return loft("Neck", pts, lambda u, th: sup(th, key(u, [(0, 0.026), (1, 0.02)]), key(u, [(0, 0.03), (1, 0.022)]), 2.2), rings=14, sides=24, close=(0, 0))


def head_part():
    pts = [P(0, 0.111, -0.093), P(0, 0.113, -0.118), P(0, 0.106, -0.148), P(0, 0.096, -0.172), P(0, 0.091, -0.184)]

    def sec(u, th):
        # (2026-10-15: the snout was a soft cone) a blunt, boxy muzzle: whisker pads either side, the upper lip over a
        # chin set back beneath it, the mouth's line along each side; the nose its own piece on the end
        w = key(u, [(0, 0.02), (0.22, 0.031), (0.45, 0.028), (0.72, 0.018), (0.9, 0.0135), (1, 0.0115)])
        h = key(u, [(0, 0.02), (0.22, 0.029), (0.45, 0.024), (0.72, 0.0155), (0.9, 0.0125), (1, 0.0105)])
        top = math.sin(th) > 0
        x, e = sup(th, w, h, 3.6 if top else 2.7)   # the flat top
        cheek = key(u, [(0.25, 0), (0.4, 1), (0.6, 0)])
        x += math.copysign(0.004, x) * cheek * bell(th, -0.45 if x > 0 else math.pi + 0.45, 0.5)
        pad = key(u, [(0.66, 0), (0.84, 1), (1, 0.5)])
        x += math.copysign(0.0026, x) * pad * bell(th, -0.3 if x > 0 else math.pi + 0.3, 0.45)
        if e < 0:
            e *= key(u, [(0.74, 1.0), (1, 0.62)])   # the chin behind the lip
        mouth = key(u, [(0.6, 0), (0.72, 1)])
        k = 1 - 0.16 * mouth * (bell(th, -0.8, 0.13) + bell(th, math.pi + 0.8, 0.13))
        return x * k, e * k

    return loft("Head", pts, sec, rings=34, sides=36, close=(0.1, 0.05))


def nose():
    """The nose: a dark, slightly glossy pad, a rounded triangle point-down, on the end of the muzzle."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=1.0)
    for v in bm.verts:
        z = v.co.z
        v.co = V((v.co.x * 0.0058 * (1 - 0.4 * max(0.0, -z)), v.co.y * 0.0032, z * 0.0042))
    ob = obj_from_bmesh(bm, "Nose")
    ob.location = P(0, 0.0935, -0.1868)
    return ob


def arm_part(sx):
    # (2026-10-15: long thin sticks) a thick upper arm out of the shoulder, a short forearm, a small flat paw on the ground
    s = -1 if sx == "L" else 1
    pts = [P(s * 0.019, 0.076, -0.066), P(s * 0.022, 0.046, -0.08), P(s * 0.022, 0.018, -0.094), P(s * 0.021, 0.006, -0.103), P(s * 0.021, 0.004, -0.114)]

    def sec(u, th):
        w = key(u, [(0, 0.016), (0.3, 0.0115), (0.62, 0.008), (0.8, 0.0072), (1, 0.0068)])
        d = key(u, [(0, 0.019), (0.3, 0.0135), (0.62, 0.0085), (0.8, 0.0062), (1, 0.0042)])
        return sup(th, w, d, 2.3)

    return loft(f"Arm{sx}", pts, sec, rings=26, sides=18, close=(0.0, 0.1))


def foot_part(sx):
    # (2026-10-15: long thin sticks) the hind leg folded as a squirrel's is: the haunch's mass down to the heel, the long
    # foot flat on the ground from the heel forward
    s = -1 if sx == "L" else 1
    pts = [P(s * 0.03, 0.086, 0.072), P(s * 0.034, 0.052, 0.066), P(s * 0.035, 0.024, 0.08), P(s * 0.034, 0.01, 0.088),
           P(s * 0.033, 0.005, 0.068), P(s * 0.031, 0.0042, 0.042), P(s * 0.03, 0.0042, 0.018)]

    def sec(u, th):
        w = key(u, [(0, 0.02), (0.18, 0.018), (0.36, 0.011), (0.5, 0.008), (0.62, 0.0085), (0.85, 0.008), (1, 0.0072)])
        d = key(u, [(0, 0.026), (0.18, 0.022), (0.36, 0.012), (0.5, 0.0085), (0.62, 0.0048), (0.85, 0.0042), (1, 0.0038)])
        return sup(th, w, d, 2.4)

    return loft(f"Foot{sx}", pts, sec, rings=36, sides=18, close=(0.0, 0.08))


def tail_core():
    pts = [P(*p) for p in TAIL]
    return loft("TailCore", pts, lambda u, th: sup(th, key(u, [(0, 0.012), (0.3, 0.009), (1, 0.004)]), key(u, [(0, 0.014), (0.3, 0.01), (1, 0.004)]), 2.2),
                rings=30, sides=16, close=(0.0, 0.08))


def ear_part(sx):
    s = -1 if sx == "L" else 1
    pts = [P(s * 0.017, 0.124, -0.11), P(s * 0.02, 0.142, -0.109), P(s * 0.023, 0.158, -0.107)]

    def sec(u, th):
        w = key(u, [(0, 0.009), (0.5, 0.012), (0.85, 0.009), (1, 0.003)])
        return w * math.cos(th), 0.0018 * math.sin(th) + 0.0035 * math.cos(th) ** 2 - 0.0015

    ob = loft(f"Ear{sx}", pts, sec, rings=14, sides=16, close=(0.0, 0.12))
    add_mod(ob, "SUBSURF", levels=1)
    apply_mods(ob)
    return ob


def eyes():
    out = []
    for s in (-1, 1):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=10, radius=1.0)
        for v in bm.verts:
            v.co = V((v.co.x * 0.0038, v.co.y * 0.0085, v.co.z * 0.0075))
        ob = obj_from_bmesh(bm, f"Eye{s}")
        ob.location = P(s * 0.0275, 0.114, -0.136)
        ob.rotation_euler = (0, 0, s * 0.25)
        out.append(ob)
    return out


# ------------------------------------------------------------------ the coat

GREY, RUST, PALE, DARK = (0.16, 0.13, 0.11), (0.21, 0.12, 0.06), (0.64, 0.6, 0.52), (0.006, 0.005, 0.005)


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def ss(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a))) if b != a else 0.0
    return t * t * (3 - 2 * t)


def coat(co, nrm):
    g, n = G(co), G(nrm)
    c = mix(GREY, RUST, ss(0.2, 0.8, abs(n.x)) * (1 - ss(0.3, 0.8, n.y)) * 0.8)   # rusty along the flanks
    c = mix(c, RUST, ss(0.03, 0.0, g.y) * 0.6)                                      # and the feet
    c = mix(c, PALE, ss(-0.2, -0.65, n.y) * ss(0.1, 0.07, g.y))                     # the pale belly
    if g.z < -0.09:
        for sx in (-1, 1):
            de = (g - V((sx * 0.0275, 0.114, -0.136))).length
            c = mix(c, PALE, ss(0.012, 0.007, de) * 0.85)                            # the eye ring
        c = mix(c, PALE, ss(0.1, 0.09, g.y) * ss(-0.15, -0.18, g.z) * 0.6)          # the chin
        # the mouth's line along the lower muzzle and the cleft up from it to the nose
        if g.z < -0.152:
            yl = 0.0868 + (g.z + 0.186) * (0.0055 / 0.03)
            c = mix(c, (0.035, 0.028, 0.025), ss(0.0009, 0.0003, abs(g.y - yl)) * ss(-0.158, -0.166, g.z) * 0.8)
            c = mix(c, (0.035, 0.028, 0.025), ss(0.0012, 0.0005, abs(g.x)) * ss(-0.6, -0.85, n.z) * ss(yl - 0.001, yl + 0.0005, g.y) * ss(0.092, 0.0905, g.y) * 0.85)
    d = 0.5 + 0.5 * noise.noise(V((g.x * 12, g.y * 12, g.z * 12)))   # (faint and broad: a fine strong one read as camouflage)
    return tuple(ch * (0.95 + 0.1 * d) for ch in c)


def flat(rgb):
    return lambda co, nrm: rgb


# ------------------------------------------------------------------ fur

def fur(skin):
    cards = Cards()
    # the plume: cards fanned in the tail's plane (its broad side to the sides), long in the middle, pale at their tips
    tail = [P(*p) for p in TAIL]
    X = V((1, 0, 0))
    # (2026-10-15: random angles and lengths gave a ragged, noisy edge) laid out in order: at each station along the core,
    # layers at set angles either side, each as long as reaches one smooth outline (the plume's half-width there), so
    # the edge is an even curve of soft tips; two more turned out sideways for its depth from behind or above; a few short
    # ones wrapped round the core
    n = 36
    for i in range(n):
        u = (i + 0.5) / n
        c = catmull(tail, u)
        t = (catmull(tail, min(1, u + 0.02)) - catmull(tail, max(0, u - 0.02))).normalized()
        R = 0.024 + 0.026 * math.sin(math.pi * min(1.0, 0.15 + u * 0.95))   # the outline's half-width
        up = t.cross(X).normalized()
        stag = 0.12 * (i % 2)
        for side in (-1, 1):
            for k in range(4):
                a = 0.32 + 0.24 * k + stag + rnd.uniform(-0.04, 0.04)
                d = Quaternion(X, side * a) @ t
                length = min(R / max(math.sin(a), 0.3), 2.4 * R) * rnd.uniform(0.95, 1.03)
                base = mix(GREY, RUST, 0.15 + 0.2 * k / 3)
                tip = mix(base, PALE, 0.42)
                out = (d - t * d.dot(t)).normalized()
                cards.card(c, d, X.cross(d), length, 0.032, base, tip, normal=out)
            d = Quaternion(up, side * 0.75) @ t
            cards.card(c, d, up, R * 1.2, 0.03, mix(GREY, RUST, 0.25), mix(GREY, PALE, 0.4), normal=(d - t * d.dot(t)).normalized())
        for k in range(3):
            roll = rnd.uniform(0, math.tau)
            perp = Quaternion(t, roll) @ X
            d = (t * 0.6 + perp * 0.8).normalized()
            base = mix(GREY, RUST, 0.2)
            cards.card(c, d, perp.cross(d), 0.018, 0.022, base, mix(base, PALE, 0.3), normal=perp)
    # and a tuft off its end
    end = tail[-1]
    tdir = (tail[-1] - tail[-2]).normalized()
    for k in range(7):
        d = Quaternion(X, -0.45 + 0.15 * k) @ tdir
        cards.card(end, d, X.cross(d), 0.03, 0.028, GREY, mix(GREY, PALE, 0.45), normal=d)
    # fringe along the back and the belly, and the ear tufts
    for co, nrm in surface_points(skin, lambda co, n: True, 0.007):
        g, gn = G(co), G(nrm)
        col = coat(co, nrm)
        tipc = tuple(ch * 1.04 for ch in col)
        if gn.y > 0.75 and -0.055 < g.z < 0.1 and g.y > 0.09:   # (the back only: not the tops of the feet and arms)
            cards.card(co - nrm * 0.001, P(*(gn * 0.4 + V((0, 0, 1))).normalized()), P(1, 0, 0), 0.012, 0.012, col, tipc, normal=nrm)
        elif gn.y < -0.7 and -0.07 < g.z < 0.06 and g.y > 0.03 and abs(g.x) < 0.02:
            cards.card(co - nrm * 0.001, P(0, -1, 0.4), P(1, 0, 0), 0.01, 0.012, mix(col, GREY, 0.4), mix(col, GREY, 0.3), normal=nrm)
    for s in (-1, 1):
        tipp = P(s * 0.023, 0.156, -0.107)
        for k in range(4):
            d = P(s * 0.2, 1, rnd.uniform(-0.2, 0.2)).normalized()
            cards.card(tipp, d, P(0, 0, 1), 0.007, 0.006, GREY, (0.08, 0.06, 0.05))
    print(f"[squirrel] fur cards: {len(cards.col) // 4}")
    return cards


# ------------------------------------------------------------------ the clips

def merge(*ds):
    out = {}
    for d in ds:
        for k, v in d.items():
            out.setdefault(k, []).extend(v)
    return out


def tail_wave(t, amp, base=0.0):
    return {f"tail{i}": [("X", base * (1 if i == 1 else 0.3) + amp * math.sin(math.tau * t - i * 0.9))] for i in range(1, 5)}


def animate(ao):
    bones_fk(ao)
    ao.animation_data_create()
    allb = ["root"] + [b[0] for b in BONES_G]
    tails = [f"tail{i}" for i in range(1, 5)]

    def forage(t):
        nib = math.sin(t * math.tau * 6) * 0.08
        pawk = max(0.0, math.sin(t * math.tau * 3))
        return merge({"chest": [("X", -0.12)], "neck": [("X", 0.35)], "head": [("X", -0.45 + nib)],
                      "arm_L": [("X", 0.25 * pawk)], "arm_R": [("X", 0.25 * max(0.0, math.sin(t * math.tau * 3 + 1.5)))]},
                     tail_wave(t, 0.06, 0.0)), V((0, 0, 0))

    def sit_up(t):
        nib = math.sin(t * math.tau * 5) * 0.1
        return merge({"hips": [("X", 0.95)], "chest": [("X", 0.35)], "neck": [("X", -0.55)], "head": [("X", -0.45 + nib * 0.4)],
                      "arm_L": [("X", 1.0 + nib)], "arm_R": [("X", 1.0 + nib)], "paw_L": [("X", 0.9)], "paw_R": [("X", 0.9)],
                      "thigh_L": [("X", -0.92)], "thigh_R": [("X", -0.92)], "foot_L": [("X", 0.0)], "foot_R": [("X", 0.0)]},
                     tail_wave(t, 0.05, -0.6)), V((0, 0, 0.02))

    def bounding(t, reach):
        s = math.sin(math.tau * t)
        fore = math.sin(math.tau * t + 1.0)
        hind = math.sin(math.tau * t - 1.4)
        return merge({"hips": [("X", -0.12 * s)], "chest": [("X", 0.2 * s)], "neck": [("X", -0.15 * s)], "head": [("X", 0.1 * s)],
                      "arm_L": [("X", reach * fore)], "arm_R": [("X", reach * math.sin(math.tau * t + 1.2))],
                      "paw_L": [("X", -0.6 * max(0.0, -fore))], "paw_R": [("X", -0.6 * max(0.0, -fore))],
                      "thigh_L": [("X", reach * 0.9 * hind)], "thigh_R": [("X", reach * 0.9 * math.sin(math.tau * t - 1.25))],
                      "foot_L": [("X", 0.5 * max(0.0, hind))], "foot_R": [("X", 0.5 * max(0.0, hind))]},
                     tail_wave(t, 0.25, -0.15)), V((0, 0, 0.022 * max(0.0, s)))

    def climb(t):
        a = math.sin(math.tau * t)
        b = math.sin(math.tau * t + math.pi)
        return merge({"arm_L": [("X", 0.7 + 0.5 * a)], "arm_R": [("X", 0.7 + 0.5 * b)], "thigh_L": [("X", 0.4 * b)], "thigh_R": [("X", 0.4 * a)],
                      "foot_L": [("X", 0.3 * max(0.0, b))], "foot_R": [("X", 0.3 * max(0.0, a))], "head": [("X", 0.2)]},
                     tail_wave(t, 0.08, 0.2)), V((0, 0, 0))

    def tail_flick(t):
        k = math.sin(t * math.pi)
        return {f"tail{i}": [("X", -0.35 * k * math.sin(t * math.pi * 2 + i * 0.5))] for i in range(1, 5)}, None

    clip(ao, "forage", 60, forage, keyed=allb)
    clip(ao, "sit_up", 60, sit_up, keyed=allb)
    clip(ao, "hop", 10, lambda t: bounding(t, 0.75), keyed=allb)
    clip(ao, "run", 8, lambda t: bounding(t, 1.0), keyed=allb)
    clip(ao, "climb", 10, climb, keyed=allb)
    clip(ao, "tail_flick", 12, tail_flick, keyed=tails, cyclic=False)


# ------------------------------------------------------------------ build

def main():
    clear()
    skin_mat = mat("SquirrelCoat", GREY, rough=0.95)
    parts = [body_part(), neck_part(), head_part(), tail_core()] + [arm_part(sx) for sx in ("L", "R")] + [foot_part(sx) for sx in ("L", "R")]
    skin = fuse(parts, "SquirrelSkin", voxel=0.0012, smooth=4, material=skin_mat)
    roughen(skin, 0.00015, 80.0)
    W.paint(skin, coat)
    W.detail_attr(skin, 0.3)
    W.tag(skin, "flesh")
    print(f"[squirrel] fused skin: {W.tri_count(skin)} triangles")
    hair = hair_material(hair_image("squirrel_hair"))
    cards = fur(skin).object("SquirrelFur", hair)
    rigid = []
    for sx in ("L", "R"):
        e = ear_part(sx)
        W.paint(e, coat)
        W.detail_attr(e, 0.1)
        W.tag(e, f"ear_{sx}")
        rigid.append(e)
    nz = nose()
    W.paint(nz, flat((0.045, 0.03, 0.03)))
    W.detail_attr(nz, 0.0)
    W.tag(nz, "head")
    rigid.append(nz)
    for e in eyes():
        W.paint(e, flat(DARK))
        W.detail_attr(e, 0.0)
        W.tag(e, "head")
        rigid.append(e)
    for r in rigid:
        r.data.materials.clear()
        r.data.materials.append(skin_mat)
    hcopies = []
    for o in [skin] + rigid:
        c = o.copy()
        c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        hcopies.append(c)
    high = join(hcopies, "High")
    ratio = min(1.0, 4200 / max(W.tri_count(skin), 1))
    add_mod(skin, "DECIMATE", ratio=ratio)
    apply_mods(skin)
    low = join([skin] + rigid, "SquirrelMesh")
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[squirrel] low {W.tri_count(low)} triangles, high {W.tri_count(high)}, fur {W.tri_count(cards)}")
    cards.hide_render = True   # (its cards out of the bake: their shadows baked into the coat read as marks)
    albedo, normal = bake_maps(low, high, "squirrel", TEXSIZE, extrusion=0.0012, dist=0.003, ao=0.15)
    cards.hide_render = False
    bpy.data.objects.remove(high, do_unlink=True)
    W.final_materials(low, albedo, normal)
    bones = bone_list()
    ao = W.make_armature("Squirrel", bones)
    W.skin_weights(low, ao, bones)
    W.skin_weights(cards, ao, bones)
    for ob in (low, cards):
        for vg in list(ob.vertex_groups):
            if vg.name.startswith("P_"):
                ob.vertex_groups.remove(vg)
    smooth_weights(low)
    animate(ao)
    W.export_glb([ao, low, cards], os.path.join(OUT, "squirrel.glb"))
    if "--no-preview" not in sys.argv:
        from kit import preview
        for act, frame, name, cam in (("forage", 0, "squirrel_side", ((0, 0, 0.12), 0.55, 0.05, 90)), ("sit_up", 10, "squirrel_sit", ((0, 0.05, 0.13), 0.5, 0.05, 60)),
                                      ("hop", 3, "squirrel_hop", ((0, 0, 0.12), 0.6, 0.05, 90)), ("forage", 0, "squirrel_head", ((0, 0.12, 0.1), 0.2, 0.01, 50)),
                                      ("forage", 0, "squirrel_tail", ((0, -0.13, 0.2), 0.45, 0.0, 110))):
            ao.animation_data.action = bpy.data.actions[act]
            bpy.context.scene.frame_set(frame)
            preview(name, *cam)


main()
