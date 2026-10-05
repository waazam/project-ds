"""The wendigo, modelled, baked, rigged and animated in Blender (the owner, 2026-09-30: the game's main monster; taller,
lanky, ribs out of its chest, gaunt; far more detail, baked maps for close-ups; a real skeleton so its legs bend when it
leaps and lunges; real hands and clawed fingers to grab through the wall).

    blender -b --python tools/Blender/wendigo.py [-- body arm]

Writes:
  assets/models/wendigo/wendigo.glb       the whole creature, skinned to a skeleton, with its animations
                                          (idle, crouch, air, pounce, land, smash, walk, run)
  assets/models/wendigo/wendigo_head.glb  the head alone, unrigged (the dining hall's platter, Act 23)
  assets/models/wendigo/wendigo_arm.glb   the arm that comes through the crawlspace's wall (Act 23), skinned to its own
                                          bones: upper, fore, hand, and every joint of every finger
  build/blender/wendigo_*.png             previews

How it's made:
  1. the flesh: the torso lofted on elliptical rings along a hunched spine (the belly sunk to the backbone, the ribcage
     flaring), the limbs and neck as skinned tubes, and the bones under the skin (ribs, spine knobs, shoulder blades,
     collarbones, hip crests, knuckles, knees) as solids unioned in, all fused by a voxel remesh into one skin;
  2. a hole torn in the chest, the ribs showing through it as bone, a dark cavity behind them and the heart of ice;
  3. the parts that aren't skin: the elk's skull, the man's jaw and its teeth, the antlers (one snapped), the eyes,
     the claws, the matted mantle of hair, torn lips;
  4. the high-detail copy (the dense flesh, its vertex colours painted: ash, bruise, frostbite) and the export copy
     (decimated); the high's micro-detail (pores, wrinkles, veins, cracks in the bone) a procedural bump, baked with
     the colours and ambient occlusion onto the export copy: 2048 px albedo and normal maps;
  5. the skeleton (spine, neck, head, jaw; clavicles, arms, hands, every finger joint; digitigrade legs: thigh, shin,
     the long foot, toes) and the skin's weights (each vertex to its nearest bone, blended at the joints along each
     chain; the rigid parts to their own bone);
  6. the animations, posed joint by joint with small IK solvers for the legs and arms.

Blender is Z-up, the creature facing +Y; the glTF export makes that the game's -Z (its front) with Y up.
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, skin_tree, lumpy_sphere, cylinder, box, join, apply_mods, add_mod, obj_from_bmesh, preview, fbm, ROOT
from props_antler_tree import antler

OUT = os.path.join(ROOT, "assets", "models", "wendigo")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREV, exist_ok=True)
V = Vector
TEXSIZE = 2048

# ------------------------------------------------------------------ the creature's proportions (Blender: X its right,
# Y its front, Z up; metres). About 4.7 m to the antlers' tips, hunched.

H = V((0, 0.74, 3.9))                       # the head's joint (top of the neck)
SPINE = [V((0, -0.04, 2.32)), V((0, -0.04, 2.7)), V((0, 0.02, 3.05)), V((0, 0.12, 3.38)), V((0, 0.26, 3.62)), V((0, 0.36, 3.78))]
# torso rings along the spine: t, half-width, half-depth, how far the front is sunk (1 not at all)
TORSO = [(0.0, 0.14, 0.11, 1.0), (0.1, 0.165, 0.125, 0.95), (0.24, 0.12, 0.1, 0.55), (0.36, 0.15, 0.12, 0.7),
         (0.5, 0.25, 0.19, 1.0), (0.64, 0.28, 0.2, 1.0), (0.78, 0.29, 0.19, 1.0), (0.9, 0.25, 0.15, 1.0), (1.0, 0.13, 0.11, 1.0)]


def spine_at(t):
    """A point on the spine's curve, t 0 (the pelvis) .. 1 (the neck's base)."""
    n = len(SPINE) - 1
    f = min(max(t, 0.0), 1.0) * n
    i = min(int(f), n - 1)
    u = f - i
    p0, p1, p2, p3 = SPINE[max(i - 1, 0)], SPINE[i], SPINE[i + 1], SPINE[min(i + 2, n)]
    u2, u3 = u * u, u * u * u
    return 0.5 * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3)


def torso_dims(t):
    for k in range(len(TORSO) - 1):
        t0, a0, b0, f0 = TORSO[k]
        t1, a1, b1, f1 = TORSO[k + 1]
        if t0 <= t <= t1:
            u = (t - t0) / (t1 - t0)
            u = u * u * (3 - 2 * u)
            return a0 + (a1 - a0) * u, b0 + (b1 - b0) * u, f0 + (f1 - f0) * u
    return TORSO[-1][1:]


def torso_frame(t):
    c = spine_at(t)
    tan = (spine_at(min(t + 0.01, 1)) - spine_at(max(t - 0.01, 0))).normalized()
    ex = V((1, 0, 0))
    ey = tan.cross(ex).normalized()            # forward
    if ey.y < 0:
        ey = -ey
    return c, ex, ey


def torso_point(t, th, out=0.0):
    """The torso's surface at t, at angle th round it (0 its right side, pi/2 its front), pushed out by `out`."""
    c, ex, ey = torso_frame(t)
    a, b, f = torso_dims(t)
    s = math.sin(th)
    front = f if s > 0 else 1.0
    return c + ex * (a + out) * math.cos(th) + ey * (b * front + out) * s


def t_at_z(z):
    lo, hi = 0.0, 1.0
    for _ in range(30):
        m = (lo + hi) / 2
        if spine_at(m).z < z:
            lo = m
        else:
            hi = m
    return (lo + hi) / 2


# the limbs' joints, per side s (+1 its right)
def arm_joints(s):
    return dict(clav=V((s * 0.07, 0.26, 3.63)), shoulder=V((s * 0.46, 0.18, 3.66)), elbow=V((s * 0.6, 0.32, 2.55)),
                wrist=V((s * 0.66, 0.5, 1.5)), palm=V((s * 0.69, 0.55, 1.25)))


def leg_joints(s):
    return dict(hip=V((s * 0.17, -0.02, 2.38)), knee=V((s * 0.22, 0.36, 1.62)), hock=V((s * 0.2, -0.3, 0.94)),
                ball=V((s * 0.19, 0.02, 0.1)), toe=V((s * 0.19, 0.4, 0.02)))


def finger_chains(s, palm, wrist, scale=1.0):
    """Four fingers and a thumb off a hanging hand (palm facing in toward the body): their joints, knuckle to tip, and
    their radii. Long, knuckled, a little curled in."""
    fingers = []
    lens = [(0.2, 0.15, 0.11), (0.23, 0.17, 0.12), (0.24, 0.18, 0.13), (0.21, 0.16, 0.12)]
    for i in range(4):
        k = palm + V((s * 0.01, -0.07 + i * 0.047, -0.01)) * scale
        d = V((0, (i - 1.5) * 0.06, -1)).normalized()
        pts, cur = [k], k
        for j, L in enumerate(lens[i]):
            curl = 0.16 + j * 0.12                       # curling in toward the palm (toward the body: -s X)
            d = (d * math.cos(curl) + V((-s, 0, 0)) * math.sin(curl)).normalized()
            cur = cur + d * L * scale
            pts.append(cur)
        fingers.append(pts)
    t0 = wrist.lerp(palm, 0.45) + V((s * -0.02, 0.06, 0)) * scale
    d = V((0, 0.75, -0.66)).normalized()
    thumb = [t0]
    cur = t0
    for j, L in enumerate((0.11, 0.1, 0.08)):
        d = (d * math.cos(0.2) + V((-s, 0, -0.3)).normalized() * math.sin(0.2)).normalized()
        cur = cur + d * L * scale
        thumb.append(cur)
    fingers.append(thumb)
    return fingers


# ------------------------------------------------------------------ materials

def materials():
    return dict(
        skin=mat("w_skin", (0.47, 0.46, 0.45), 0.72),
        bone=mat("w_bone", (0.74, 0.71, 0.64), 0.6),
        claw=mat("w_claw", (0.03, 0.03, 0.04), 0.25),
        eye=mat("w_eye", (0.8, 0.9, 1.0), 0.2, emit=(0.6, 0.8, 1.0)),
        heart=mat("w_heart", (0.4, 0.65, 0.9), 0.05, emit=(0.3, 0.55, 0.95)),
        cavity=mat("w_cavity", (0.12, 0.03, 0.03), 0.4),
        hair=mat("w_hair", (0.07, 0.065, 0.065), 1.0),
        velvet=mat("w_velvet", (0.2, 0.15, 0.12), 0.95),
    )


def tag(ob, part):
    """Marks every vertex of a part: its bone (the rigid parts) or 'flesh'."""
    vg = ob.vertex_groups.new(name="P_" + part)
    vg.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
    return ob


def paint(ob, fn):
    """Vertex colours: fn(position, normal) -> (r, g, b) in linear."""
    me = ob.data
    if "Col" not in me.color_attributes:
        me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    attr = me.color_attributes["Col"]
    cols = np.zeros((len(me.vertices), 4), np.float32)
    for v in me.vertices:
        r, g, b = fn(v.co, v.normal)
        cols[v.index] = (r, g, b, 1.0)
    attr.data.foreach_set("color", cols.ravel())


def detail_attr(ob, value):
    """How much micro-detail (pores, wrinkles) the bake gives each vertex: a float attribute."""
    me = ob.data
    a = me.attributes.get("detail") or me.attributes.new("detail", "FLOAT", "POINT")
    vals = np.full(len(me.vertices), value, np.float32) if not callable(value) else np.array([value(v.co) for v in me.vertices], np.float32)
    a.data.foreach_set("value", vals)


# ------------------------------------------------------------------ the flesh

def loft_torso():
    bm = bmesh.new()
    nt, nr = 44, 36
    rings = []
    for i in range(nt + 1):
        t = i / nt
        ring = []
        for j in range(nr):
            th = j / nr * math.tau
            ring.append(bm.verts.new(torso_point(t, th)))
        rings.append(ring)
    for i in range(nt):
        for j in range(nr):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % nr], rings[i + 1][(j + 1) % nr], rings[i + 1][j]))
    for ring, c in ((rings[0], spine_at(0) + V((0, 0, -0.06))), (rings[-1], spine_at(1) + V((0, 0.02, 0.04)))):
        cv = bm.verts.new(c)
        for j in range(nr):
            bm.faces.new((ring[j], ring[(j + 1) % nr], cv))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, "Torso")


def loft_path(name, pts, widths, heights, up=V((0, 0, 1)), sides=24, material=None, squash=None):
    """A closed lofted shape along a path: at each point an ellipse `widths` across (its X) and `heights` high (toward
    `up`); squash(t, angle) -> a factor on the radius there (for flattening and dents)."""
    bm = bmesh.new()
    rings = []
    n = len(pts)
    for i, (c, w, h) in enumerate(zip(pts, widths, heights)):
        tan = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        side = tan.cross(up).normalized()
        upv = side.cross(tan).normalized()
        ring = []
        for j in range(sides):
            a = j / sides * math.tau
            f = squash(i / (n - 1), a) if squash else 1.0
            ring.append(bm.verts.new(c + side * math.cos(a) * w * f + upv * math.sin(a) * h * f))
        rings.append(ring)
    for i in range(n - 1):
        for j in range(sides):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % sides], rings[i + 1][(j + 1) % sides], rings[i + 1][j]))
    for ring, c in ((rings[0], pts[0] - (pts[1] - pts[0]).normalized() * 0.01), (rings[-1], pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.01)):
        cv = bm.verts.new(c)
        for j in range(sides):
            bm.faces.new((ring[j], ring[(j + 1) % sides], cv))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name, material)
    add_mod(ob, "SUBSURF", levels=1)
    apply_mods(ob)
    for pg in ob.data.polygons:
        pg.use_smooth = True
    return ob


def ribbon(bm, pts, widths, out):
    """A flat strand along pts, `widths` wide, facing `out` (hair: matted, one card, both sides drawn)."""
    prev = None
    for i, p in enumerate(pts):
        tan = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = tan.cross(out).normalized() * widths[i] * 0.5
        a, b = bm.verts.new(p - side), bm.verts.new(p + side)
        if prev:
            bm.faces.new((prev[0], prev[1], b, a))
        prev = (a, b)


def chain(name, pts, radii, subdiv=1):
    return skin_tree(name, pts, [(i - 1, i) for i in range(1, len(pts))], radii, None, subdiv=subdiv)


def ribs_curves():
    """Ten ribs a side: (side, index, the curve's points along the torso's surface)."""
    out = []
    for s in (1, -1):
        for i in range(10):
            z0 = 3.6 - i * 0.068
            end = 1.45 if i < 7 else 1.05 - (i - 7) * 0.2         # the floating ribs stop short of the front
            pts = []
            for k in range(17):
                u = k / 16
                th = -math.pi / 2 + u * (math.pi / 2 + end)     # from the spine round the side to the front
                z = z0 - 0.16 * u - 0.05 * u * u
                t = t_at_z(z)
                ang = th if s > 0 else math.pi - th
                pts.append(torso_point(t, ang, 0.0))
            out.append((s, i, pts))
    return out


def build_flesh(m):
    """The one skin: everything that's flesh, fused by a voxel remesh."""
    parts = [loft_torso()]
    # the neck, with its cords standing out
    parts.append(chain("Neck", [V((0, 0.28, 3.66)), V((0, 0.5, 3.84)), H + V((0, -0.02, 0.0))], [0.1, 0.078, 0.07]))
    for s in (1, -1):
        parts.append(chain(f"Cord{s}", [H + V((s * 0.055, 0.0, -0.02)), V((s * 0.05, 0.36, 3.67))], [0.02, 0.024]))
    for s in (1, -1):
        J = arm_joints(s)
        sh, el, wr = J["shoulder"], J["elbow"], J["wrist"]
        # the arm: the shoulder's slope off the collarbone, the wasted upper arm, the elbow's point, the forearm thick
        # below the elbow and wasting to a knobbed wrist
        parts.append(chain(f"Arm{s}", [J["clav"], J["clav"].lerp(sh, 0.6) + V((0, 0, 0.03)), sh, sh.lerp(el, 0.3), sh.lerp(el, 0.7), el,
                                       el.lerp(wr, 0.2), el.lerp(wr, 0.6), wr, J["palm"]],
                           [0.065, 0.07, 0.078, 0.06, 0.05, 0.055, 0.056, 0.04, 0.03, 0.045]))
        parts.append(lumpy_sphere(f"Delt{s}", sh + V((s * 0.015, 0.02, -0.04)), (0.065, 0.07, 0.11), None, 11 + s, amp=0.06, subdiv=3))
        parts.append(lumpy_sphere(f"Bicep{s}", sh.lerp(el, 0.45) + V((0, 0.035, 0)), (0.04, 0.035, 0.12), None, 15 + s, amp=0.08, subdiv=3))
        parts.append(lumpy_sphere(f"Elbow{s}", el + V((0, -0.045, 0)), (0.045, 0.055, 0.06), None, 12 + s, amp=0.06, subdiv=3))
        parts.append(lumpy_sphere(f"Wrist{s}", wr + V((s * 0.015, -0.01, 0)), (0.03, 0.03, 0.032), None, 13 + s, amp=0.06, subdiv=2))
        # sinews down the forearm, standing out of it
        for k, off in enumerate((V((s * 0.03, 0.03, 0)), V((-s * 0.02, 0.035, 0)), V((s * 0.01, -0.035, 0)))):
            parts.append(chain(f"Sinew{s}{k}", [el.lerp(wr, 0.12) + off * 1.3, el.lerp(wr, 0.55) + off * 1.05, wr.lerp(el, 0.06) + off * 0.6], [0.012, 0.011, 0.008]))
        # the hand: the palm's body, then the fingers and thumb with their knuckles
        parts.append(lumpy_sphere(f"Palm{s}", wr.lerp(J["palm"], 0.55), (0.028, 0.085, 0.115), None, 14 + s, amp=0.04, subdiv=3))
        for k, pts in enumerate(finger_chains(s, J["palm"], wr)):
            thumb = k == 4
            r = [0.024, 0.02, 0.017, 0.014] if not thumb else [0.028, 0.022, 0.018, 0.015]
            parts.append(chain(f"Finger{s}_{k}", [J["palm"].lerp(pts[0], 0.4) if not thumb else wr.lerp(J["palm"], 0.3)] + pts, [r[0]] + r))
            for jj, q in enumerate(pts[:-1]):
                parts.append(lumpy_sphere(f"Knuckle{s}_{k}_{jj}", q, (r[jj] * 1.12, r[jj] * 1.12, r[jj] * 1.2), None, 100 + k * 5 + jj, amp=0.08, subdiv=2))
        # the legs, a deer's: the knee forward, the hock back and up, the long foot to the ball, three toes
        L = leg_joints(s)
        hp, kn, hk, bl = L["hip"], L["knee"], L["hock"], L["ball"]
        parts.append(chain(f"Leg{s}", [hp, hp.lerp(kn, 0.3), hp.lerp(kn, 0.7), kn, kn.lerp(hk, 0.25), kn.lerp(hk, 0.7), hk, hk.lerp(bl, 0.5), bl],
                           [0.11, 0.092, 0.07, 0.06, 0.058, 0.045, 0.042, 0.032, 0.038]))
        parts.append(lumpy_sphere(f"Knee{s}", kn + V((0, 0.035, 0.01)), (0.05, 0.045, 0.055), None, 20 + s, amp=0.07, subdiv=3))
        parts.append(lumpy_sphere(f"Calf{s}", kn.lerp(hk, 0.3) + V((0, -0.03, 0)), (0.045, 0.04, 0.13), None, 24 + s, amp=0.08, subdiv=3))
        parts.append(lumpy_sphere(f"Heel{s}", hk + V((0, -0.045, 0.015)), (0.035, 0.055, 0.045), None, 21 + s, amp=0.07, subdiv=3))
        # the tendon down the back of the shin to the heel, the cords of the thigh
        parts.append(chain(f"Achilles{s}", [kn.lerp(hk, 0.4) + V((0, -0.045, 0)), hk + V((0, -0.06, 0.04))], [0.015, 0.013]))
        for k, off in enumerate((V((s * 0.05, 0.03, 0)), V((-s * 0.04, 0.04, 0)))):
            parts.append(chain(f"Thew{s}{k}", [hp.lerp(kn, 0.15) + off * 1.4, hp.lerp(kn, 0.85) + off * 0.9], [0.016, 0.012]))
        for k in (-1, 0, 1):
            tip = L["toe"] + V((k * 0.07, -abs(k) * 0.05, 0))
            parts.append(chain(f"Toe{s}_{k}", [bl, bl.lerp(tip, 0.5) + V((0, 0, 0.03)), tip], [0.032, 0.024, 0.018]))
        parts.append(lumpy_sphere(f"HipCrest{s}", V((s * 0.15, 0.02, 2.52)), (0.032, 0.05, 0.03), None, 22 + s, amp=0.08, subdiv=2))
        # the shoulder blade, standing out of the back; the collarbone
        parts.append(lumpy_sphere(f"Blade{s}", torso_point(0.78, -math.pi / 2 + s * 0.55, -0.02), (0.1, 0.03, 0.13), None, 23 + s, amp=0.06, subdiv=3))
        parts.append(chain(f"Collar{s}", [torso_point(0.95, math.pi / 2 + s * -0.3, -0.005), J["shoulder"] + V((-s * 0.06, 0.06, 0.04))], [0.02, 0.024]))
    # the spine's knuckles down the back, the sternum
    for k in range(20):
        t = 0.1 + k * 0.045
        parts.append(lumpy_sphere(f"Vert{k}", torso_point(t, -math.pi / 2, 0.005), (0.026, 0.024, 0.022), None, 200 + k, amp=0.1, subdiv=2))
    parts.append(chain("Sternum", [torso_point(0.95, math.pi / 2, -0.01), torso_point(0.6, math.pi / 2, -0.01)], [0.02, 0.018]))
    # the ribs under the skin
    for s, i, pts in ribs_curves():
        parts.append(chain(f"RibRidge{s}_{i}", pts, [0.018] * len(pts), subdiv=0))
    flesh = join(parts, "Flesh")
    add_mod(flesh, "REMESH", mode="VOXEL", voxel_size=0.0105, adaptivity=0.0)
    apply_mods(flesh)
    add_mod(flesh, "SMOOTH", factor=0.6, iterations=4)
    apply_mods(flesh)
    print(f"[wendigo] flesh remeshed: {len(flesh.data.polygons)} faces")
    flesh.data.materials.clear()
    flesh.data.materials.append(m["skin"])
    return flesh


HOLE_C = V((-0.11, 0.0, 3.3))


def hole_e(co):
    """Inside the tear in its chest when < 1 (its left front, below the breastbone), its edge ragged."""
    n = noise.noise(V((co.x * 11, co.z * 11, 3.3)))
    return ((co.x - HOLE_C.x) / 0.13) ** 2 + ((co.z - HOLE_C.z) / 0.16) ** 2 + 0.45 * n


def tear_chest(flesh):
    bm = bmesh.new()
    bm.from_mesh(flesh.data)
    kill = [f for f in bm.faces if all(v.co.y > 0.12 and hole_e(v.co) < 1.0 for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(flesh.data)
    bm.free()


# ------------------------------------------------------------------ the parts that aren't skin

def build_parts(m):
    rnd = random.Random(666)
    P = []

    def add(ob, part):
        P.append(tag(ob, part))
        return ob

    # ---- the exposed ribs in the tear, the cavity behind them, the heart of ice; the torn rim
    for s, i, pts in ribs_curves():
        inside = [p for p in pts if p.y > 0.08 and hole_e(p) < 1.7]
        if i in (3, 6):
            inside = inside[: max(2, len(inside) // 2)]   # (snapped)
        if len(inside) >= 2 and s < 0:
            jit = [V((0, 0, noise.noise(V((p.x * 20, i * 1.7, 0))) * 0.006)) for p in inside]
            ob = chain(f"Rib{i}", [p + (p - spine_at(t_at_z(p.z))).normalized() * -0.004 + d for p, d in zip(inside, jit)], [0.0145] * len(inside))
            ob.data.materials.append(m["bone"])
            add(ob, "chest")
    t = t_at_z(HOLE_C.z)
    c = spine_at(t)
    cav = lumpy_sphere("Cavity", c + V((-0.08, 0.02, 0)), (0.15, 0.13, 0.17), m["cavity"], 7, amp=0.08, subdiv=4)
    add(cav, "chest")
    heart = lumpy_sphere("Heart", torso_point(t, math.pi / 2 + 0.35, -0.08), (0.035, 0.03, 0.06), m["heart"], 8, amp=0.35, freq=3.0, subdiv=2)
    add(heart, "chest")
    # torn flaps of skin hanging into the tear from its upper edge
    for k in range(6):
        a = math.pi * (0.15 + k * 0.14)
        p = V((HOLE_C.x + math.cos(a) * 0.11, 0, HOLE_C.z + math.sin(a) * 0.13))
        tt = t_at_z(p.z)
        th = math.acos(max(-1, min(1, (p.x - spine_at(tt).x) / torso_dims(tt)[0])))
        q = torso_point(tt, th, -0.006) + V((0, 0, -0.025))
        ob = lumpy_sphere(f"Rim{k}", (0, 0, 0), (0.03, 0.006, 0.035), m["skin"], 300 + k, amp=0.3, subdiv=2)
        ob.rotation_euler = (0.3, 0, a - math.pi / 2)
        ob.location = q
        add(ob, "chest")
    # ---- the head: an elk's skull, bleached and cracked: long and narrow, the braincase behind, the orbits ringed with
    # bone on its sides, the long face tapering down to the nose; the eye sockets deep and dark; the nose's hollow
    sk_pts = [H + V((0, -0.03, 0.07)), H + V((0, 0.04, 0.09)), H + V((0, 0.12, 0.08)), H + V((0, 0.2, 0.05)), H + V((0, 0.3, -0.02)),
              H + V((0, 0.42, -0.1)), H + V((0, 0.53, -0.18)), H + V((0, 0.61, -0.24)), H + V((0, 0.655, -0.275))]
    sk_w = [0.06, 0.095, 0.1, 0.092, 0.066, 0.052, 0.044, 0.036, 0.018]
    sk_h = [0.06, 0.09, 0.085, 0.075, 0.062, 0.056, 0.048, 0.04, 0.02]

    def dents(t, a):
        # the sides pinched in behind the orbits (the temples), the face's underside flatter
        f = 1.0
        if 0.25 < t < 0.45 and abs(math.cos(a)) > 0.7:
            f *= 0.9
        if math.sin(a) < -0.3:
            f *= 0.88
        return f
    skull = [loft_path("Cranium", sk_pts, sk_w, sk_h, up=V((0, 0, 1)), material=m["bone"], squash=dents)]
    dark = mat("w_socket", (0.012, 0.01, 0.01), 1.0)
    for sd in (1, -1):
        oc = H + V((sd * 0.083, 0.2, 0.06))
        out = V((sd, 0.25, 0.15)).normalized()
        # the orbit's rim: a ring of bone round the socket
        ring = []
        up_ = out.cross(V((0, 0, 1))).normalized()
        up2 = out.cross(up_).normalized()
        for k in range(10):
            a = k / 10 * math.tau
            ring.append(oc + (up_ * math.cos(a) + up2 * math.sin(a)) * 0.034 + out * 0.008)
        rim = skin_tree(f"Orbit{sd}", ring, [(k, (k + 1) % 10) for k in range(10)], [0.009] * 10, m["bone"], subdiv=1)
        skull.append(rim)
        add(lumpy_sphere(f"Socket{sd}", oc - out * 0.004, (0.027, 0.027, 0.027), dark, 36 + sd, amp=0.12, subdiv=3), "head")
        add(lumpy_sphere(f"Eye{sd}", oc + out * 0.006, (0.007, 0.007, 0.007), m["eye"], 38 + sd, amp=0.02, subdiv=2), "head")
    sk = join(skull, "Skull")
    add(sk, "head")
    add(lumpy_sphere("Nose", H + V((0, 0.58, -0.205)), (0.016, 0.06, 0.014), dark, 40, amp=0.15, subdiv=2), "head")
    # the upper teeth: long and uneven, along the snout's underside
    for s in (1, -1):
        for k in range(8):
            u = k / 7
            base = H + V((s * (0.04 - u * 0.022), 0.24 + u * 0.37, -0.06 - u * 0.21))
            L = rnd.uniform(0.035, 0.08)
            tip = base + V((rnd.uniform(-0.01, 0.01), rnd.uniform(-0.005, 0.015), -L))
            add(cylinder(f"ToothU{s}{k}", base, tip, 0.008, 0.0015, 6, m["bone"]), "head")
    # a man's jaw hung under it, too big, its teeth long and uneven; torn lips between
    jaw = chain("Jaw", [H + V((-0.085, 0.08, -0.05)), H + V((-0.072, 0.2, -0.16)), H + V((-0.035, 0.29, -0.21)), H + V((0, 0.31, -0.225)),
                        H + V((0.035, 0.29, -0.21)), H + V((0.072, 0.2, -0.16)), H + V((0.085, 0.08, -0.05))],
                [0.02, 0.024, 0.026, 0.028, 0.026, 0.024, 0.02], subdiv=2)
    jaw.data.materials.append(m["bone"])
    add(jaw, "jaw")
    for s in (1, -1):
        for k in range(7):
            u = k / 6
            base = H + V((s * (0.072 - u * 0.05), 0.15 + u * 0.15, -0.13 - u * 0.08))
            L = rnd.uniform(0.035, 0.085)
            tip = base + V((rnd.uniform(-0.012, 0.012), rnd.uniform(-0.01, 0.01), L))
            add(cylinder(f"ToothL{s}{k}", base, tip, 0.009, 0.0015, 6, m["bone"]), "jaw")
    lip = mat("w_lip", (0.12, 0.035, 0.03), 0.4)
    for s in (1, -1):
        for k in range(2):
            a = H + V((s * 0.07, 0.2 + k * 0.1, -0.08 - k * 0.07))
            b = H + V((s * 0.068, 0.17 + k * 0.08, -0.15 - k * 0.06))
            mid = a.lerp(b, 0.5) + V((s * 0.012, 0.01, -0.025))
            add(chain(f"Lip{s}{k}", [a, mid, b], [0.0045, 0.003, 0.004]), "jaw").data.materials.append(lip)
    # the antlers: a great rack, the left snapped off short
    for sd in (1, -1):
        root = H + V((sd * 0.08, 0.06, 0.13))
        ob = antler(f"Antler{sd}", root, (sd * 0.35, -0.35, 1), (sd, -0.25, 0.2), 1.35 if sd > 0 else 0.62, rnd, m["bone"])
        add(ob, "head")
    # ---- the claws: long, hooked, black
    for s in (1, -1):
        J = arm_joints(s)
        for k, pts in enumerate(finger_chains(s, J["palm"], J["wrist"])):
            tip, prev = pts[-1], pts[-2]
            d = (tip - prev).normalized()
            hook = (d + V((-s, 0, 0)) * 0.6).normalized()
            L = 0.11 if k < 4 else 0.08
            a = tip - d * 0.01
            b = a + d * L * 0.5 + hook * L * 0.15
            c2 = b + hook * L * 0.6
            ob = chain(f"Claw{s}_{k}", [a, b, c2], [0.014, 0.009, 0.0015])
            ob.data.materials.append(m["claw"])
            add(ob, f"f{k}_3_{'R' if s > 0 else 'L'}")
        L = leg_joints(s)
        for k in (-1, 0, 1):
            tip = L["toe"] + V((k * 0.07, -abs(k) * 0.05, 0))
            ob = chain(f"ToeClaw{s}{k}", [tip - V((0, 0.02, -0.01)), tip + V((0, 0.06, -0.005)), tip + V((0, 0.1, -0.04))], [0.016, 0.01, 0.002])
            ob.data.materials.append(m["claw"])
            add(ob, f"toe_{'R' if s > 0 else 'L'}")
    # ---- the mantle of matted black hair off its shoulders and down its back: clumped ribbons of it, hanging and
    # swaying, frosted at the tips
    bm = bmesh.new()
    for c in range(70):
        t = rnd.uniform(0.74, 1.0)
        th = rnd.uniform(-math.pi * 1.05, 0.05) if rnd.random() < 0.85 else rnd.uniform(-0.3, 0.3) + (0 if rnd.random() < 0.5 else math.pi)
        L = rnd.uniform(0.35, 0.85) * (1.0 if th < 0 else 0.55)
        for k in range(rnd.randint(3, 6)):
            root = torso_point(t + rnd.uniform(-0.02, 0.02), th + rnd.uniform(-0.1, 0.1), -0.006)
            out = root - spine_at(t)
            out.z = 0
            out = out.normalized() if out.length > 1e-4 else V((0, -1, 0))
            ln = L * rnd.uniform(0.75, 1.1)
            pts = [root]
            cur = root
            ph = rnd.uniform(0, 6.28)
            for jj in range(6):
                u = (jj + 1) / 6
                sway = V((math.sin(ph + u * 4) * 0.03, math.cos(ph + u * 3) * 0.03, 0))
                cur = cur + out * (0.06 * (1 - u)) + sway + V((0, 0, -ln / 6))
                pts.append(cur)
            w0 = rnd.uniform(0.03, 0.055)
            ribbon(bm, pts, [w0, w0 * 0.95, w0 * 0.85, w0 * 0.7, w0 * 0.5, w0 * 0.3, 0.003], out)
    hj = obj_from_bmesh(bm, "Hair", m["hair"])
    m["hair"].use_backface_culling = False
    add(hj, "chest")
    return P


# ------------------------------------------------------------------ the colours (linear)

def frost_toward(z, z0, z1):
    return min(max((z0 - z) / (z0 - z1), 0.0), 1.0)


def skin_colour(co, nrm):
    n1 = fbm(co.x * 4, co.y * 4, co.z * 4, 4)
    n2 = fbm(co.x * 14 + 7, co.y * 14, co.z * 14, 3)
    base = V((0.29, 0.28, 0.27)) * (0.85 + 0.35 * n1)          # ash grey, mottled
    # bruised purples in patches, darker in the creases
    bruise = max(0.0, fbm(co.x * 3 + 3, co.y * 3, co.z * 3, 3) - 0.1) * 1.6
    base = base.lerp(V((0.11, 0.085, 0.12)), min(bruise, 0.7))
    # frostbite: the hands and the feet going blue-black, the fingers and toes black
    hands = frost_toward(co.z, 1.75, 0.95) if abs(co.x) > 0.45 else 0.0
    feet = frost_toward(co.z, 1.0, 0.1)
    fb = max(hands, feet)
    base = base.lerp(V((0.035, 0.04, 0.055)), fb ** 1.3 * 0.95)
    # sore red round the tear in its chest
    e = hole_e(co)
    if co.y > 0.05 and e < 1.8:
        base = base.lerp(V((0.16, 0.025, 0.02)), max(0.0, 1.0 - (e - 1.0) / 0.8) * 0.85)
    # grime, darker down its back
    base *= 0.92 + 0.12 * n2
    if nrm.y < -0.3:
        base *= 0.85
    return base.x, base.y, base.z


def bone_colour(co, nrm):
    n = fbm(co.x * 9, co.y * 9, co.z * 9, 4)
    crack = abs(noise.noise(V((co.x * 22, co.y * 22, co.z * 22))))
    c = V((0.5, 0.47, 0.4)) * (0.88 + 0.2 * n)
    if crack < 0.04:
        c *= 0.35                                               # hairline cracks
    stain = max(0.0, fbm(co.x * 5 + 2, co.y * 5, co.z * 5, 3)) * 0.6
    c = c.lerp(V((0.2, 0.13, 0.08)), stain * 0.5)
    return c.x, c.y, c.z


def flat_colour(rgb):
    def fn(co, nrm):
        return rgb
    return fn


def part_colour(name):
    if name.startswith(("Rib", "Skull", "Tooth", "Jaw", "Antler", "Cranium")):
        return bone_colour
    if name.startswith(("Claw", "ToeClaw")):
        return flat_colour((0.012, 0.012, 0.016))
    if name.startswith("Hair"):
        def hair(co, nrm):
            frost = max(0.0, min(1.0, (3.25 - co.z) / 0.35))
            c = V((0.02, 0.018, 0.018)).lerp(V((0.32, 0.34, 0.38)), frost * 0.7)
            return c.x, c.y, c.z
        return hair
    if name.startswith("Cavity"):
        return flat_colour((0.05, 0.008, 0.008))
    if name.startswith("Rim"):
        return flat_colour((0.12, 0.02, 0.018))
    if name.startswith("Heart"):
        return flat_colour((0.25, 0.5, 0.85))
    if name.startswith("Eye"):
        return flat_colour((0.7, 0.85, 1.0))
    if name.startswith(("Socket", "Nose")):
        return flat_colour((0.006, 0.005, 0.005))
    if name.startswith("Lip"):
        return flat_colour((0.09, 0.02, 0.018))
    if name.startswith("Velvet"):
        return flat_colour((0.07, 0.05, 0.04))
    return skin_colour


# ------------------------------------------------------------------ baking

def bump_material(name, detail_scale):
    """The high copy's material for the normal bake: micro-detail as a procedural bump (pores, fine wrinkles, raised
    veins, cracks), scaled by the 'detail' attribute."""
    mt = bpy.data.materials.new(name)
    mt.use_nodes = True
    nt = mt.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    pores = nt.nodes.new("ShaderNodeTexVoronoi")
    pores.feature = "F1"
    pores.inputs["Scale"].default_value = 140.0 * detail_scale
    wr = nt.nodes.new("ShaderNodeTexNoise")
    wr.inputs["Scale"].default_value = 35.0 * detail_scale
    wr.inputs["Detail"].default_value = 8.0
    wr.inputs["Roughness"].default_value = 0.65
    veins = nt.nodes.new("ShaderNodeTexVoronoi")
    veins.feature = "DISTANCE_TO_EDGE"
    veins.inputs["Scale"].default_value = 6.0 * detail_scale
    for n in (pores, wr, veins):
        nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
    # veins: thin raised lines (distance to the cells' edges, small = on a vein)
    vr = nt.nodes.new("ShaderNodeMapRange")
    vr.inputs["From Min"].default_value = 0.0
    vr.inputs["From Max"].default_value = 0.035
    vr.inputs["To Min"].default_value = 1.0
    vr.inputs["To Max"].default_value = 0.0
    nt.links.new(veins.outputs["Distance"], vr.inputs["Value"])
    add1 = nt.nodes.new("ShaderNodeMath")
    add1.operation = "MULTIPLY_ADD"
    nt.links.new(wr.outputs["Fac"], add1.inputs[0])
    add1.inputs[1].default_value = 0.6
    nt.links.new(pores.outputs["Distance"], add1.inputs[2])
    add2 = nt.nodes.new("ShaderNodeMath")
    add2.operation = "MULTIPLY_ADD"
    nt.links.new(vr.outputs["Result"], add2.inputs[0])
    add2.inputs[1].default_value = 0.5
    nt.links.new(add1.outputs["Value"], add2.inputs[2])
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "detail"
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    nt.links.new(add2.outputs["Value"], mul.inputs[0])
    nt.links.new(attr.outputs["Fac"], mul.inputs[1])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.6
    bump.inputs["Distance"].default_value = 0.0025
    nt.links.new(mul.outputs["Value"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mt


def emit_vc_material(name):
    mt = bpy.data.materials.new(name)
    mt.use_nodes = True
    nt = mt.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    nt.links.new(ca.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return mt


def set_all_materials(ob, mt):
    for i in range(len(ob.material_slots)):
        ob.material_slots[i].material = mt
    if len(ob.material_slots) == 0:
        ob.data.materials.append(mt)


def target_image(low, img):
    """Every material of the low object gets the bake target as its active image node."""
    for slot in low.material_slots:
        nt = slot.material.node_tree
        node = nt.nodes.get("BakeTarget") or nt.nodes.new("ShaderNodeTexImage")
        node.name = "BakeTarget"
        node.image = img
        nt.nodes.active = node


def bake(low, high, kind, img, samples=1, extrusion=0.02, dist=0.05):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    rb = sc.render.bake
    rb.use_selected_to_active = high is not None
    rb.cage_extrusion = extrusion
    rb.max_ray_distance = dist
    rb.margin = 6
    target_image(low, img)
    bpy.ops.object.select_all(action="DESELECT")
    if high is not None:
        high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    if kind == "NORMAL":
        bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
    elif kind == "EMIT":
        bpy.ops.object.bake(type="EMIT")
    elif kind == "AO":
        bpy.ops.object.bake(type="AO")


def new_image(name, size, data):
    img = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
    if data:
        img.colorspace_settings.name = "Non-Color"
    return img


def bake_maps(low, high, prefix, size):
    """Albedo (the high copy's colours times its occlusion) and the normal map (its micro-detail), onto the low copy."""
    # the normal map: the high copy with its bump
    keep = [s.material for s in high.material_slots]
    set_all_materials(high, bump_material(prefix + "_bump", 1.0))
    nimg = new_image(prefix + "_normal", size, True)
    bake(low, high, "NORMAL", nimg)
    # the colours
    set_all_materials(high, emit_vc_material(prefix + "_vc"))
    cimg = new_image(prefix + "_albedo", size, False)
    bake(low, high, "EMIT", cimg)
    # the occlusion (the low copy's own)
    aimg = new_image(prefix + "_ao", size, True)
    bake(low, None, "AO", aimg, samples=64)
    c = np.array(cimg.pixels[:], np.float32).reshape(size, size, 4)
    a = np.array(aimg.pixels[:], np.float32).reshape(size, size, 4)[:, :, 0]
    # (smoothed: at a few dozen samples it's grainy, and the grain read as camouflage)
    for _ in range(3):
        a = (a + np.roll(a, 1, 0) + np.roll(a, -1, 0) + np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 5.0
    c[:, :, :3] *= (0.62 + 0.38 * a)[:, :, None]
    cimg.pixels = c.ravel()
    for img in (cimg, nimg):
        img.filepath_raw = os.path.join(PREV, img.name + ".png")
        img.file_format = "PNG"
        img.save()
    print(f"[wendigo] baked {prefix}: albedo and normal at {size} px")
    return cimg, nimg


def final_materials(low, albedo, normal):
    """The low copy's materials for export: the baked albedo and normal map on each, roughness and glow kept."""
    for slot in low.material_slots:
        mt = slot.material
        nt = mt.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        bt = nt.nodes.get("BakeTarget")
        if bt:
            nt.nodes.remove(bt)
        for l in list(bsdf.inputs["Base Color"].links):
            nt.links.remove(l)
        ti = nt.nodes.new("ShaderNodeTexImage")
        ti.image = albedo
        nt.links.new(ti.outputs["Color"], bsdf.inputs["Base Color"])
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = normal
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])


# ------------------------------------------------------------------ the skeleton

def bone_list():
    """(name, head, tail, parent, radius): the rest pose."""
    B = []
    B.append(("root", V((0, 0, 0)), V((0, 0.3, 0)), None, 0.0))
    B.append(("hips", spine_at(0.0), spine_at(0.25), "root", 0.16))
    B.append(("spine", spine_at(0.25), spine_at(0.55), "hips", 0.15))
    B.append(("chest", spine_at(0.55), spine_at(0.97), "spine", 0.23))
    B.append(("neck", spine_at(0.97), V((0, 0.52, 3.85)), "chest", 0.08))
    B.append(("neck2", V((0, 0.52, 3.85)), H, "neck", 0.07))
    B.append(("head", H, H + V((0, 0.3, 0.05)), "neck2", 0.1))
    B.append(("jaw", H + V((0, 0.08, -0.06)), H + V((0, 0.42, -0.32)), "head", 0.03))
    for s, sx in ((1, "R"), (-1, "L")):
        J = arm_joints(s)
        B.append((f"clav_{sx}", J["clav"], J["shoulder"], "chest", 0.07))
        B.append((f"upper_{sx}", J["shoulder"], J["elbow"], f"clav_{sx}", 0.065))
        B.append((f"fore_{sx}", J["elbow"], J["wrist"], f"upper_{sx}", 0.05))
        B.append((f"hand_{sx}", J["wrist"], J["palm"], f"fore_{sx}", 0.05))
        for k, pts in enumerate(finger_chains(s, J["palm"], J["wrist"])):
            for j in range(3):
                B.append((f"f{k}_{j + 1}_{sx}", pts[j], pts[j + 1], f"hand_{sx}" if j == 0 else f"f{k}_{j}_{sx}", 0.02))
        L = leg_joints(s)
        B.append((f"thigh_{sx}", L["hip"], L["knee"], "hips", 0.1))
        B.append((f"shin_{sx}", L["knee"], L["hock"], f"thigh_{sx}", 0.065))
        B.append((f"foot_{sx}", L["hock"], L["ball"], f"shin_{sx}", 0.05))
        B.append((f"toe_{sx}", L["ball"], L["toe"], f"foot_{sx}", 0.03))
    return B


def make_armature(name, bones):
    ad = bpy.data.armatures.new(name + "Rig")
    ao = bpy.data.objects.new(name, ad)
    bpy.context.scene.collection.objects.link(ao)
    bpy.context.view_layer.objects.active = ao
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for (bn, h, t, p, r) in bones:
        e = ad.edit_bones.new(bn)
        e.head, e.tail = h, t
        # the roll: its Z toward the creature's front where it can be (so a bend is about its X)
        e.align_roll(V((0, 1, 0)) if abs((t - h).normalized().y) < 0.9 else V((0, 0, 1)))
        eb[bn] = e
    for (bn, h, t, p, r) in bones:
        if p:
            eb[bn].parent = eb[p]
            eb[bn].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return ao


def seg_dist(P, a, b):
    ab = b - a
    t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
    q = a + t[:, None] * ab
    return np.linalg.norm(P - q, axis=1), t


def skin_weights(ob, arm_obj, bones):
    """Each flesh vertex to its nearest bone (by distance to the bone less its flesh's radius), blended with the bone
    before it and after it along its chain near the joints; the rigid parts wholly to their bone."""
    me = ob.data
    P = np.array([v.co[:] for v in me.vertices], np.float32)
    names = [b[0] for b in bones if b[0] != "root"]
    info = {b[0]: b for b in bones}
    children = {}
    for b in bones:
        if b[3]:
            children.setdefault(b[3], []).append(b[0])
    D = np.zeros((len(P), len(names)), np.float32)
    T = np.zeros((len(P), len(names)), np.float32)
    for i, n in enumerate(names):
        _, h, t, _, r = info[n]
        d, tt = seg_dist(P, np.array(h[:], np.float32), np.array(t[:], np.float32))
        D[:, i] = d - r
        T[:, i] = tt
    near = np.argmin(D, axis=1)
    groups = {n: ob.vertex_groups.new(name=n) for n in names}
    forced = {}
    for vg in ob.vertex_groups:
        if vg.name.startswith("P_") and vg.name != "P_flesh":
            forced[vg.index] = vg.name[2:]
    W = {}
    for v in me.vertices:
        f = None
        for g in v.groups:
            if g.group in forced and g.weight > 0.5:
                f = forced[g.group]
        if f:
            W[v.index] = {f: 1.0}
            continue
        i = near[v.index]
        n = names[i]
        t = T[v.index, i]
        w = {n: 1.0}
        par = info[n][3]
        if t < 0.22 and par and par != "root":
            k = 0.5 * (1 - t / 0.22)
            w = {n: 1 - k, par: k}
        elif t > 0.78 and n in children:
            ch = min(children[n], key=lambda c: (info[c][1] - V(P[v.index])).length)
            k = 0.5 * (t - 0.78) / 0.22
            w = {n: 1 - k, ch: k}
        W[v.index] = w
    for vi, w in W.items():
        for n, x in w.items():
            groups[n].add([vi], x, "REPLACE")
    ob.parent = arm_obj
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj


# ------------------------------------------------------------------ posing

def two_bone(a, target, l1, l2, pole):
    d = target - a
    dist = min(max(d.length, 1e-3), l1 + l2 - 1e-3)
    dirv = d.normalized()
    cos_a = (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)
    ang = math.acos(max(-1, min(1, cos_a)))
    side = dirv.cross(pole)
    if side.length < 1e-5:
        side = V((1, 0, 0))
    bend = side.normalized().cross(dirv).normalized()
    if bend.dot(pole) < 0:
        bend = -bend
    mid = a + dirv * (l1 * math.cos(ang)) + bend * (l1 * math.sin(ang))
    return mid, a + dirv * dist


def rot_x(a):
    """Bowing forward by a (radians) about the creature's X axis."""
    return Matrix.Rotation(-a, 4, "X")


class Poser:
    """Poses the armature from joint positions and directions (armature space): every bone's new head is carried
    along by its parent; its direction set by the pose or inherited."""

    def __init__(self, arm_obj, bones):
        self.ao = arm_obj
        self.info = {b[0]: b for b in bones}
        self.order = [b[0] for b in bones]

    def solve(self, root_offset, dirs, spins):
        """Every bone's new head and rotation (armature space)."""
        new = {}
        rot = {}
        for n in self.order:
            _, h, t, p, _ = self.info[n]
            if p is None:
                head = h + root_offset
                R = Matrix.Identity(4)
            else:
                ph = self.info[p][1]
                head = new[p][0] + (rot[p] @ (h - ph).to_4d()).to_3d()
                R = rot[p]
            restd = (t - h).normalized()
            if n in dirs:
                R = restd.rotation_difference(dirs[n].normalized()).to_matrix().to_4x4()
            if n in spins:
                axis, ang = spins[n]
                axis_w = (R.to_3x3() @ axis).normalized()   # (its axis in the rest pose, carried round with it)
                R = Matrix.Rotation(ang, 4, axis_w) @ R
            new[n] = (head, R)
            rot[n] = R
        return new

    def apply(self, root_offset=V((0, 0, 0)), dirs=None, spins=None, frame=None):
        dirs = dirs or {}
        spins = spins or {}
        new = self.solve(root_offset, dirs, spins)
        # into each pose bone's basis
        for n in self.order:
            pb = self.ao.pose.bones[n]
            b = pb.bone
            head, R = new[n]
            desired = Matrix.Translation(head) @ R @ b.matrix_local.to_3x3().to_4x4()
            if b.parent:
                ph, pR = new[b.parent.name]
                pdes = Matrix.Translation(ph) @ pR @ b.parent.matrix_local.to_3x3().to_4x4()
                basis = b.matrix_local.inverted() @ b.parent.matrix_local @ pdes.inverted() @ desired
            else:
                basis = b.matrix_local.inverted() @ desired
            pb.matrix_basis = basis
            if frame is not None:
                pb.keyframe_insert("location", frame=frame)
                pb.keyframe_insert("rotation_quaternion", frame=frame)
        return new


def body_pose(poser, hip_off=V((0, 0, 0)), bow=0.0, neck_bow=0.0, head_pitch=0.0, leg=None, arm=None, curl=0.3, jaw=0.0, twist=0.0):
    """A pose from a few settings: the hips' offset and how far it bows; per side, where each foot's ball is and how its
    foot angles (leg), where each wrist is reaching (arm: "wrist", or "reach" from where the shoulder really is); how far
    the fingers curl; the chest's twist (positive: its right shoulder forward). The limbs are solved from where the
    bowed torso really puts the hips and the shoulders."""
    dirs, spins = {}, {}
    rest = poser.info
    if twist:
        spins["chest"] = ((rest["chest"][2] - rest["chest"][1]).normalized(), twist)
    for n, a in (("hips", bow * 0.5), ("spine", bow * 0.8), ("chest", bow)):
        h, t = rest[n][1], rest[n][2]
        dirs[n] = (rot_x(a) @ (t - h).to_4d()).to_3d()
    for n, a in (("neck", bow + neck_bow), ("neck2", bow + neck_bow * 1.3), ("head", bow + neck_bow + head_pitch)):
        h, t = rest[n][1], rest[n][2]
        dirs[n] = (rot_x(a) @ (t - h).to_4d()).to_3d()
    h, t = rest["jaw"][1], rest["jaw"][2]
    dirs["jaw"] = (rot_x(bow + neck_bow + head_pitch + jaw) @ (t - h).to_4d()).to_3d()
    torso = poser.solve(hip_off, dirs, dict(spins))
    for s, sx in ((1, "R"), (-1, "L")):
        L = leg_joints(s)
        lg = (leg or {}).get(sx, {})
        hip = torso[f"thigh_{sx}"][0]
        ball = lg.get("ball", L["ball"])
        foot_dir = lg.get("foot", (L["ball"] - L["hock"]).normalized())
        hock = ball - foot_dir.normalized() * (L["ball"] - L["hock"]).length
        l1, l2 = (L["knee"] - L["hip"]).length, (L["hock"] - L["knee"]).length
        knee, hock = two_bone(hip, hock, l1, l2, V((0, 1, 0.15)))
        dirs[f"thigh_{sx}"] = knee - hip
        dirs[f"shin_{sx}"] = hock - knee
        dirs[f"foot_{sx}"] = ball - hock
        dirs[f"toe_{sx}"] = lg.get("toe", L["toe"] - L["ball"])
    for s, sx in ((1, "R"), (-1, "L")):
        J = arm_joints(s)
        ag = (arm or {}).get(sx, {})
        sh = torso[f"upper_{sx}"][0]
        wrist = ag["wrist"] if "wrist" in ag else sh + ag["reach"] if "reach" in ag else J["wrist"] + hip_off
        l1, l2 = (J["elbow"] - J["shoulder"]).length, (J["wrist"] - J["elbow"]).length
        elbow, wrist = two_bone(sh, wrist, l1, l2, ag.get("pole", V((s * 0.4, -1, 0))))
        dirs[f"upper_{sx}"] = elbow - sh
        dirs[f"fore_{sx}"] = wrist - elbow
        dirs[f"hand_{sx}"] = ag.get("hand", wrist - elbow)
        c = ag.get("curl", curl)
        for k in range(5):
            for j in range(1, 4):
                fn = f"f{k}_{j}_{sx}"
                restd = (rest[fn][2] - rest[fn][1]).normalized()
                # curling in toward the palm (the hand's inside, toward the body in the rest pose)
                axis = restd.cross(V((-s, 0, 0))).normalized()
                spins[fn] = (axis, c * (0.35 + 0.25 * j) * (0.6 if k == 4 else 1.0))
    return dirs, spins


# ------------------------------------------------------------------ the animations

def animate(arm_obj, bones):
    sc = bpy.context.scene
    sc.render.fps = 30
    poser = Poser(arm_obj, bones)
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "QUATERNION"
    arm_obj.animation_data_create()
    actions = {}

    def action(name, keys, cyclic=False):
        act = bpy.data.actions.new(name)
        arm_obj.animation_data.action = act
        for frame, off, kw in keys:
            dirs, spins = body_pose(poser, hip_off=off, **kw)
            poser.apply(off, dirs, spins, frame=frame)
        actions[name] = act
        arm_obj.animation_data.action = None
        # (an NLA track each, so the glTF exporter writes every one)
        tr = arm_obj.animation_data.nla_tracks.new()
        tr.name = name
        tr.strips.new(name, int(keys[0][0]), act)
        return act

    Lr, Ll = leg_joints(1), leg_joints(-1)
    Ar, Al = arm_joints(1), arm_joints(-1)
    hang = 0.35

    # ---- idle: hunched, breathing slow and deep, its arms hanging and swaying a little, the fingers working
    idle = []
    for f, ph in ((0, 0), (30, 1), (60, 2), (90, 3), (120, 0)):
        b = math.sin(ph * math.pi / 2)
        sw = math.sin(ph * math.pi / 2 + 0.7)
        idle.append((f, V((0, 0, 0.02 * b)), dict(bow=hang + 0.03 * b, neck_bow=0.1, curl=0.32 + 0.12 * math.sin(ph * 2.1),
              arm=dict(R=dict(wrist=Ar["wrist"] + V((0.02 * sw, 0.06 + 0.05 * sw, 0.02))), L=dict(wrist=Al["wrist"] + V((-0.02 * sw, 0.06 - 0.05 * sw, 0.02)))))))
    action("idle", idle)

    # ---- crouch: down hard to spring, the hips dropped, the knees jutting, the hocks raised, the arms swept back
    def crouch_pose(u):
        drop = 0.78 * u
        foot = lambda L: (L["ball"] - L["hock"]).normalized().lerp(V((0, 0.55, -0.83)), u)
        return (V((0, -0.12 * u, -drop)), dict(bow=hang + 0.45 * u, neck_bow=0.1 - 0.35 * u, head_pitch=-0.25 * u, curl=0.3 + 0.4 * u,
                leg=dict(R=dict(foot=foot(Lr)), L=dict(foot=foot(Ll))),
                arm=dict(R=dict(wrist=Ar["wrist"] + V((0.1 * u, -0.9 * u, -0.7 * u + 0.3 * u))), L=dict(wrist=Al["wrist"] + V((-0.1 * u, -0.9 * u, -0.4 * u))))))
    action("crouch", [(0, *crouch_pose(0.0)), (4, *crouch_pose(0.7)), (8, *crouch_pose(1.0))])

    # ---- air: launched, the legs straight and the feet pointed; then the legs fold up under it, the arms reach up and
    # out with the claws spread; at the end the legs reach down
    def air_pose(ext, tuck, reach):
        dirs_leg = {}
        for sx, L in (("R", Lr), ("L", Ll)):
            # extended: the ball far below the hip; tucked: drawn up under the hips, the hock folded behind
            ext_ball = L["hip"] + V((0, -0.35, -2.2))
            tuck_ball = L["hip"] + V((0, 0.25, -1.0))
            ball = ext_ball.lerp(tuck_ball, tuck)
            foot = V((0, -0.3, -1)).normalized().lerp(V((0, 0.8, -0.6)).normalized(), tuck)
            dirs_leg[sx] = dict(ball=ball, foot=foot, toe=V((0, 0.2 + 0.5 * tuck, -1)))
        arms = {}
        for sx, A, s in (("R", Ar, 1), ("L", Al, -1)):
            up = A["shoulder"] + V((s * 0.5, 0.4, 1.4))
            fwd = A["shoulder"] + V((s * 0.7, 1.6, 0.5))
            arms[sx] = dict(wrist=up.lerp(fwd, reach), curl=0.05, pole=V((s, -0.5, 0)))
        return (V((0, 0, 0)), dict(bow=0.1 + 0.4 * tuck, neck_bow=-0.1, head_pitch=-0.2, leg=dirs_leg, arm=arms, jaw=0.35 * reach))
    action("air", [(0, *air_pose(1, 0, 0)), (6, *air_pose(1, 0.7, 0.5)), (12, *air_pose(1, 1, 0.9)), (18, *air_pose(1, 0.35, 1.0))])

    # ---- pounce: flung forward and down at its prey, flat out, the arms thrown wide and forward, the claws spread, the
    # jaw open, the legs trailing behind
    def pounce_pose(u):
        legs = {}
        for sx, L in (("R", Lr), ("L", Ll)):
            ball = L["hip"] + V((0, -1.6 - 0.3 * u, -0.9 + 0.3 * u))
            legs[sx] = dict(ball=ball, foot=V((0, -0.8, -0.4)).normalized(), toe=V((0, -0.6, -0.8)))
        arms = {}
        for sx, A, s in (("R", Ar, 1), ("L", Al, -1)):
            arms[sx] = dict(wrist=A["shoulder"] + V((s * (0.9 - 0.3 * u), 1.7, -0.2 - 0.4 * u)), curl=0.05 + 0.5 * u, pole=V((s, 0, 1)))
        return (V((0, 0, 0)), dict(bow=0.9, neck_bow=-0.35, head_pitch=-0.3, leg=legs, arm=arms, jaw=0.5))
    action("pounce", [(0, *pounce_pose(0)), (9, *pounce_pose(0.6)), (18, *pounce_pose(1.0))])

    # ---- land: down off a height onto a floor (Act 23's end, off the lodge's balcony): touching down with the legs
    # reaching and the arms out for the boards; then the weight taken, deep, the hands slammed flat in front of it and
    # the claws dug in, the head up and on its prey; holding there, breathing
    def land_pose(u, breathe=0.0):
        drop = 0.95 * u - 0.08 * breathe
        legs = {}
        for sx, L in (("R", Lr), ("L", Ll)):
            legs[sx] = dict(ball=L["ball"] + V((0, 0.05 * u, 0)), foot=(L["ball"] - L["hock"]).normalized().lerp(V((0, 0.5, -0.86)), u))
        arms = {}
        for sx, A, s in (("R", Ar, 1), ("L", Al, -1)):
            reach = A["shoulder"] + V((s * 0.5, 1.1, -0.6))
            floor = V((s * 0.8, 1.35, 0.2))
            arms[sx] = dict(wrist=reach.lerp(floor, u), curl=0.15 + 0.5 * u, pole=V((s * 0.6, -0.3, 0.6)))
        return (V((0, -0.08 * u, -drop)), dict(bow=hang + 0.6 * u, neck_bow=0.1 - 0.6 * u, head_pitch=-0.15 * u, curl=0.5,
                leg=legs, arm=arms, jaw=0.15 + 0.2 * u))
    action("land", [(0, *land_pose(0.0)), (4, *land_pose(1.0)), (10, *land_pose(0.92)), (20, *land_pose(0.95, 1.0)), (30, *land_pose(0.93))])

    # ---- smash: into a door, shoulder first (its right). Drawn back, the right shoulder turned away, the arms cocked;
    # driven forward, the shoulder and both hands into the wood, the claws spread on it; then the claws dragged down and
    # apart through the boards, tearing
    def smash_pose(phase):
        legs = {}
        if phase == 0:
            hip, bw, tw, jw, cl = V((0, -0.25, -0.35)), 0.4, -0.3, 0.2, 0.25
            reach = {"R": V((0.45, -0.25, 0.75)), "L": V((-0.35, 0.8, 0.35))}
            feet = {"R": V((0, -0.55, 0)), "L": V((0, 0.45, 0))}
        elif phase == 1:
            hip, bw, tw, jw, cl = V((0, 0.4, -0.25)), 0.75, 0.38, 0.55, 0.1
            reach = {"R": V((0.25, 1.45, -0.5)), "L": V((-0.35, 1.4, -0.25))}
            feet = {"R": V((0, -0.85, 0.05)), "L": V((0, 0.55, 0))}
        else:
            hip, bw, tw, jw, cl = V((0, 0.35, -0.4)), 0.85, 0.12, 0.35, 0.8
            reach = {"R": V((0.75, 1.0, -1.45)), "L": V((-0.75, 0.95, -1.35))}
            feet = {"R": V((0, -0.75, 0)), "L": V((0, 0.5, 0))}
        arms = {}
        for sx, L in (("R", Lr), ("L", Ll)):
            legs[sx] = dict(ball=L["ball"] + feet[sx])
        for sx, s in (("R", 1), ("L", -1)):
            arms[sx] = dict(reach=reach[sx], curl=cl, pole=V((s * 0.7, -0.2, -0.6)))
        return (hip, dict(bow=bw, neck_bow=-0.25, head_pitch=-0.25, curl=cl, leg=legs, arm=arms, jaw=jw, twist=tw))
    action("smash", [(0, *smash_pose(0)), (7, *smash_pose(1)), (11, *smash_pose(1)), (20, *smash_pose(2))])

    # ---- walk and run (Act 24, stalking the snow maze): the digitigrade legs stepping, each foot planted for three fifths
    # of the stride and swung through for the rest; the long arms swinging low against the legs; the body bobbing. The run
    # a long lope, hunched far over, the arms reaching
    def gait(phase, stride, lift, bow, arm_swing, bob, reach_down):
        legs = {}
        for sx, L, off in (("R", Lr, 0.0), ("L", Ll, 0.5)):
            p = (phase + off) % 1.0
            if p < 0.6:
                u = p / 0.6
                y, z = stride * (0.5 - u), 0.0
            else:
                u = (p - 0.6) / 0.4
                y, z = stride * (-0.5 + u), lift * math.sin(u * math.pi)
            foot = (L["ball"] - L["hock"]).normalized().lerp(V((0, -0.2, -1)).normalized(), 0.6 if p >= 0.6 else 0.0)
            legs[sx] = dict(ball=L["ball"] + V((0, y, z)), foot=foot)
        arms = {}
        for sx, s_, off in (("R", 1, 0.5), ("L", -1, 0.0)):
            p = (phase + off) % 1.0
            sw = math.sin(p * math.tau) * arm_swing
            arms[sx] = dict(reach=V((s_ * 0.2, 0.3 + sw, -reach_down + 0.1 * abs(sw))), curl=0.45, pole=V((s_ * 0.4, -1, 0)))
        hip = V((0, 0, -bob * (0.5 + 0.5 * math.cos(phase * 2 * math.tau))))
        return (hip, dict(bow=bow, neck_bow=0.05, head_pitch=-0.05, curl=0.45, leg=legs, arm=arms))
    action("walk", [(f, *gait(f / 32, 0.9, 0.22, hang + 0.08, 0.28, 0.06, 1.9)) for f in range(0, 33, 4)])
    action("run", [(f, *gait(f / 20, 1.6, 0.45, hang + 0.4, 0.65, 0.12, 1.5)) for f in range(0, 21, 2)])
    # the rest pose for export (and the previews): the idle's first frame
    arm_obj.animation_data.action = actions["idle"]
    sc.frame_set(0)
    return actions


# ------------------------------------------------------------------ export

def export_glb(objs, path, anim=True):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=False,
                              export_animations=anim, export_skins=True, export_animation_mode="ACTIONS" if anim else "ACTIVE_ACTIONS",
                              export_force_sampling=True, export_image_format="AUTO", export_materials="EXPORT")
    print(f"[wendigo] exported {path}")


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# ------------------------------------------------------------------ the body

def build_body():
    clear()
    m = materials()
    flesh = build_flesh(m)
    tear_chest(flesh)
    paint(flesh, skin_colour)
    detail_attr(flesh, lambda co: 1.0 if hole_e(co) > 1.3 else 0.3)
    parts = build_parts(m)
    for p in parts:
        paint(p, part_colour(p.name))
        detail_attr(p, 0.5 if p.name.startswith(("Skull", "Antler", "Rib", "Jaw")) else 0.15)
    tag(flesh, "flesh")
    # the high copy (for the bake) and the low (for the game)
    high_parts = []
    for p in parts:
        c = p.copy()
        c.data = p.data.copy()
        bpy.context.scene.collection.objects.link(c)
        high_parts.append(c)
    hf = flesh.copy()
    hf.data = flesh.data.copy()
    bpy.context.scene.collection.objects.link(hf)
    high = join([hf] + high_parts, "High")
    target = 85000
    ratio = min(1.0, target / max(tri_count(flesh), 1))
    add_mod(flesh, "DECIMATE", ratio=ratio)
    apply_mods(flesh)
    low = join([flesh] + parts, "WendigoMesh")
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0015)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002)   # (smart project left three quarters of the map empty)
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[wendigo] low {tri_count(low)} triangles, high {tri_count(high)}")
    albedo, normal = bake_maps(low, high, "wendigo", TEXSIZE)
    bpy.data.objects.remove(high, do_unlink=True)
    final_materials(low, albedo, normal)
    # the head alone (the platter): a copy of its parts, unrigged, the head's joint at the origin
    head = low.copy()
    head.data = low.data.copy()
    bpy.context.scene.collection.objects.link(head)
    bm = bmesh.new()
    bm.from_mesh(head.data)
    deform = bm.verts.layers.deform.active
    gi = {head.vertex_groups[n].index for n in ("P_head", "P_jaw") if n in head.vertex_groups}
    kill = [v for v in bm.verts if not any(g in gi for g in v[deform].keys())]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    for v in bm.verts:
        v.co -= H
    bm.to_mesh(head.data)
    bm.free()
    head.vertex_groups.clear()
    head.name = "WendigoHead"
    export_glb([head], os.path.join(OUT, "wendigo_head.glb"), anim=False)
    bpy.data.objects.remove(head, do_unlink=True)
    # the skeleton, the weights, the animations
    bones = bone_list()
    arm_obj = make_armature("Wendigo", bones)
    skin_weights(low, arm_obj, bones)
    for vg in list(low.vertex_groups):
        if vg.name.startswith("P_"):
            low.vertex_groups.remove(vg)
    animate(arm_obj, bones)
    export_glb([arm_obj, low], os.path.join(OUT, "wendigo.glb"))
    # previews: the rest pose and a frame of each action
    for name, frame, cam in (("wendigo_front", 0, ((0, 2.3, 2.4), 7.5, 0.3, 10)), ("wendigo_side", 0, ((0, 0.3, 2.3), 7.5, 0.2, 90)),
                             ("wendigo_head", 0, None), ("wendigo_chest", 0, ((-0.1, 0.4, 3.3), 1.6, 0.0, -15))):
        arm_obj.animation_data.action = bpy.data.actions["idle"]
        bpy.context.scene.frame_set(frame)
        if cam is None:
            hb = arm_obj.matrix_world @ arm_obj.pose.bones["head"].matrix
            hc = hb @ V((0, 0.2, 0))
            cam = (tuple(hc), 1.5, -0.1, 20)
        preview(name, *cam)
    for act, frame in (("crouch", 8), ("air", 12), ("pounce", 12), ("land", 10), ("smash", 0), ("smash", 9), ("smash", 20), ("walk", 8), ("run", 5)):
        arm_obj.animation_data.action = bpy.data.actions[act]
        bpy.context.scene.frame_set(frame)
        preview(f"wendigo_{act}" + (f"_{frame}" if act in ("smash", "walk", "run") else ""), (0, 0.3, 2.0), 8.0, 0.3, 70)


# ------------------------------------------------------------------ the arm through the wall

ARM_L1, ARM_L2 = 0.78, 0.74


def build_arm():
    """The arm alone (Act 23's crawlspace): along +X from its shoulder at the origin (deep in the wall), its palm down, the
    same hand as the body's; its own bones: upper, fore, hand, the fingers' joints."""
    clear()
    m = materials()
    sh = V((0, 0, 0))
    el = V((ARM_L1, 0, 0))
    wr = V((ARM_L1 + ARM_L2, 0, 0))
    palm = wr + V((0.26, 0, 0))
    # the hand: the body's right hand turned to lie along +X, palm down (the body's hangs along -Z, palm toward -X)
    J = arm_joints(1)
    # (a quarter turn about Y: the hanging hand's -Z becomes +X, its palm's -X becomes -Z: down)
    to_arm = Matrix.Translation(wr) @ Matrix.Rotation(-math.pi / 2, 4, "Y") @ Matrix.Translation(-J["wrist"])
    fing = [[(to_arm @ p.to_4d()).to_3d() for p in pts] for pts in finger_chains(1, J["palm"], J["wrist"])]
    palm = (to_arm @ J["palm"].to_4d()).to_3d()
    parts = [chain("Arm", [sh - V((0.1, 0, 0)), sh.lerp(el, 0.5), el, el.lerp(wr, 0.5), wr, palm], [0.09, 0.07, 0.072, 0.05, 0.035, 0.05])]
    parts.append(lumpy_sphere("Elbow", el + V((0, 0, 0.04)), (0.07, 0.07, 0.07), None, 2, amp=0.05, subdiv=3))
    parts.append(lumpy_sphere("Wrist", wr, (0.042, 0.045, 0.042), None, 3, amp=0.05, subdiv=2))
    pc = wr.lerp(palm, 0.55)
    p = lumpy_sphere("Palm", (0, 0, 0), (0.12, 0.09, 0.032), None, 4, amp=0.04, subdiv=3)
    p.location = pc
    parts.append(p)
    for k, pts in enumerate(fing):
        thumb = k == 4
        r = [0.026, 0.022, 0.019, 0.015] if not thumb else [0.03, 0.024, 0.02, 0.016]
        parts.append(chain(f"Finger{k}", [palm.lerp(pts[0], 0.4) if not thumb else wr.lerp(palm, 0.3)] + pts, [r[0]] + r))
        for j, q in enumerate(pts[:-1]):
            parts.append(lumpy_sphere(f"Knuckle{k}_{j}", q, (r[j] * 1.25,) * 3, None, 100 + k * 5 + j, amp=0.06, subdiv=2))
    flesh = join(parts, "ArmFlesh")
    add_mod(flesh, "REMESH", mode="VOXEL", voxel_size=0.008, adaptivity=0.0)
    apply_mods(flesh)
    add_mod(flesh, "SMOOTH", factor=0.6, iterations=4)
    apply_mods(flesh)
    flesh.data.materials.append(m["skin"])

    def arm_colour(co, nrm):
        # frostbite from the wrist out; ash up the arm
        x = co.x
        fb = min(max((x - (ARM_L1 + ARM_L2 + 0.05)) / 0.55, 0.0), 1.0)   # (from the knuckles to the fingertips)
        n1 = fbm(co.x * 4, co.y * 4, co.z * 4, 4)
        n2 = fbm(co.x * 14 + 7, co.y * 14, co.z * 14, 3)
        c = V((0.29, 0.28, 0.27)) * (0.85 + 0.35 * n1)
        bruise = max(0.0, fbm(co.x * 3 + 3, co.y * 3, co.z * 3, 3) - 0.1) * 1.6
        c = c.lerp(V((0.11, 0.085, 0.12)), min(bruise, 0.7)) * (0.92 + 0.12 * n2)
        c = c.lerp(V((0.035, 0.04, 0.055)), fb ** 1.2 * 0.95)
        return c.x, c.y, c.z

    paint(flesh, arm_colour)
    detail_attr(flesh, 1.0)
    tag(flesh, "flesh")
    claws = []
    for k, pts in enumerate(fing):
        tip, prev = pts[-1], pts[-2]
        d = (tip - prev).normalized()
        hook = (d + V((0, 0, -1)) * 0.6).normalized()
        L = 0.11 if k < 4 else 0.08
        a = tip - d * 0.01
        b = a + d * L * 0.5 + hook * L * 0.15
        c = chain(f"Claw{k}", [a, b, b + hook * L * 0.6], [0.014, 0.009, 0.0015])
        c.data.materials.append(m["claw"])
        paint(c, flat_colour((0.012, 0.012, 0.016)))
        detail_attr(c, 0.1)
        claws.append(tag(c, f"f{k}_3"))
    hc = [c.copy() for c in claws]
    for c, src in zip(hc, claws):
        c.data = src.data.copy()
        bpy.context.scene.collection.objects.link(c)
    hf = flesh.copy()
    hf.data = flesh.data.copy()
    bpy.context.scene.collection.objects.link(hf)
    high = join([hf] + hc, "High")
    add_mod(flesh, "DECIMATE", ratio=min(1.0, 16000 / max(tri_count(flesh), 1)))
    apply_mods(flesh)
    low = join([flesh] + claws, "WendigoArmMesh")
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")
    albedo, normal = bake_maps(low, high, "wendigo_arm", 1024)
    bpy.data.objects.remove(high, do_unlink=True)
    final_materials(low, albedo, normal)
    bones = [("upper", sh, el, None, 0.07), ("fore", el, wr, "upper", 0.05), ("hand", wr, palm, "fore", 0.05)]
    for k, pts in enumerate(fing):
        for j in range(3):
            bones.append((f"f{k}_{j + 1}", pts[j], pts[j + 1], "hand" if j == 0 else f"f{k}_{j}", 0.02))
    arm_obj = make_armature("WendigoArm", bones)
    skin_weights(low, arm_obj, bones)
    for vg in list(low.vertex_groups):
        if vg.name.startswith("P_"):
            low.vertex_groups.remove(vg)
    export_glb([arm_obj, low], os.path.join(OUT, "wendigo_arm.glb"), anim=False)
    print(f"[wendigo] arm: {tri_count(low)} triangles")
    preview("wendigo_arm", (1.2, 0, 0), 2.6, 0.8, 200)
    preview("wendigo_arm_hand", (1.75, 0, -0.05), 1.0, 0.5, 160)


if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["body", "arm"]
    if "body" in want:
        build_body()
    if "arm" in want:
        build_arm()
