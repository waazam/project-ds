"""The stalker, modelled, baked, rigged and animated in Blender (the owner, 2026-10-02: the first monster, on par with
the wendigo: a real model, a skeleton and animations so it moves rather than hinges, rags that hang and sway, a proper
face; up close it has to hold up).

    blender -b --python tools/Blender/stalker.py

Writes:
  assets/models/stalker/stalker.glb   the whole figure skinned to its skeleton, with its animations (idle, peek_R,
                                      peek_L, duck_R, duck_L, walk, shove, loom, stare, peek_low_R/L, cling); its tones in its vertex
                                      colours (the game's convention: r the face's paleness, g brightness, a 0 the eyes)
                                      and its baked albedo and normal maps on its material
  build/blender/stalker_*.png         previews

The design is the game's (StalkerBody.cs), in its units (Size 1 is about 2.2 m to the hump): gaunt and hunched, heavy
high shoulders crowned with gnarled root spikes, a small long head slung forward and low between them on a thin neck,
arms that hang past the knees to long many-jointed fingers, knees bent a little wrong, rags off the shoulders, arms and
hips, bark shards along the spine. Now as one hide drawn tight over the bones (ribs, the spine's knuckles, the shoulder
blades, the hip bones, knuckles, the tendons in the neck and wrists), fused by a voxel remesh; the face a small
stretched mask, too small for the head, with deep sockets and a lipless slit of a mouth, the eyes far back in the
sockets.

How it's made (as tools/Blender/wendigo.py):
  1. the flesh: the torso lofted on rings along a hunched spine, the limbs and neck as skinned tubes, the bones under
     the hide as solids unioned in, fused by a voxel remesh; the hands, the feet and the head remeshed finer on their own;
  2. the head's face carved: the sockets pushed deep, the mouth's slit, the nose's two slits, the brow over it all;
  3. the parts that aren't hide: the root crowns, the bark shards, the eyes, the nails, the rags (each a strip with a
     torn end, on its own chain of bones);
  4. a high-detail copy (the hide's micro-detail a procedural bump: cracks, wrinkles, a bark-like grain) baked onto the
     export copy: 2048 px albedo and normal maps;
  5. the skeleton (spine, neck, head, jaw; clavicles, arms, hands, every finger joint; legs, feet, toes; a chain of
     bones down every bunch of rags) and the weights;
  6. the animations, posed joint by joint with small IK solvers.

Blender is Z-up, the figure facing +Y; the glTF export makes that the game's -Z with Y up (StalkerBody turns it round).
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, skin_tree, lumpy_sphere, cylinder, join, apply_mods, add_mod, obj_from_bmesh, fbm, ROOT
import kit
import wendigo as W

OUT = os.path.join(ROOT, "assets", "models", "stalker")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREV, exist_ok=True)
V = Vector
TEXSIZE = 2048
rnd = random.Random(1931)

# ------------------------------------------------------------------ the proportions (Blender: X its right, Y its front,
# Z up; the game's design units). The game's design points (x, y up, z front) are (x, z, y) here.

def D(x, y, z):
    """A point from the game's design space (x, y up, z front)."""
    return V((x, z, y))


SPINE = [D(0, 1.04, -0.01), D(0, 1.2, 0.0), D(0, 1.36, 0.01), D(0, 1.52, 0.03), D(0, 1.7, 0.055), D(0, 1.86, 0.07), D(0, 1.98, 0.055), D(0, 2.05, 0.02)]
# rings along the spine: t, half-width, half-depth in front, half-depth behind, how far the front is sunk
TORSO = [(0.0, 0.13, 0.09, 0.1, 1.0), (0.1, 0.145, 0.1, 0.105, 1.0), (0.26, 0.098, 0.07, 0.085, 0.6), (0.4, 0.15, 0.11, 0.115, 0.85),
         (0.56, 0.205, 0.14, 0.15, 1.0), (0.7, 0.24, 0.15, 0.175, 1.0), (0.82, 0.26, 0.13, 0.2, 1.0), (0.9, 0.24, 0.1, 0.2, 1.0),
         (0.96, 0.17, 0.07, 0.16, 1.0), (1.0, 0.07, 0.04, 0.08, 1.0)]
NECK_BASE = D(0, 1.98, 0.14)
HC = D(0.02, 1.9, 0.44)                      # the head's centre
H = D(0.015, 1.935, 0.37)                    # the head's joint (the top of the neck, under the back of the skull)
# the head's pose (StalkerBody's: a roll of 16 degrees and the chin tucked 10)
HEAD_ROT = Matrix.Rotation(math.radians(-16), 4, "Y") @ Matrix.Rotation(math.radians(-10), 4, "X")


def spine_at(t):
    n = len(SPINE) - 1
    f = min(max(t, 0.0), 1.0) * n
    i = min(int(f), n - 1)
    u = f - i
    p0, p1, p2, p3 = SPINE[max(i - 1, 0)], SPINE[i], SPINE[i + 1], SPINE[min(i + 2, n)]
    u2, u3 = u * u, u * u * u
    return 0.5 * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3)


def torso_dims(t):
    for k in range(len(TORSO) - 1):
        t0, a0, f0, b0, s0 = TORSO[k]
        t1, a1, f1, b1, s1 = TORSO[k + 1]
        if t0 <= t <= t1:
            u = (t - t0) / (t1 - t0)
            u = u * u * (3 - 2 * u)
            return a0 + (a1 - a0) * u, f0 + (f1 - f0) * u, b0 + (b1 - b0) * u, s0 + (s1 - s0) * u
    return TORSO[-1][1:]


def torso_frame(t):
    c = spine_at(t)
    tan = (spine_at(min(t + 0.01, 1)) - spine_at(max(t - 0.01, 0))).normalized()
    ex = V((1, 0, 0))
    ey = tan.cross(ex).normalized()
    if ey.y < 0:
        ey = -ey
    return c, ex, ey


def torso_point(t, th, out=0.0):
    """The torso's surface at t, angle th round it (0 its right, pi/2 its front, -pi/2 its back), pushed out by out."""
    c, ex, ey = torso_frame(t)
    a, f, b, sunk = torso_dims(t)
    s = math.sin(th)
    depth = f * sunk if s > 0 else b
    return c + ex * (a + out) * math.cos(th) + ey * (depth + out) * s


def t_at_z(z):
    lo, hi = 0.0, 1.0
    for _ in range(30):
        m = (lo + hi) / 2
        if spine_at(m).z < z:
            lo = m
        else:
            hi = m
    return (lo + hi) / 2


def tp_design(y, deg, push=1.04):
    """StalkerBody's TorsoPoint: the torso's surface at design height y, angle deg (0 = +X, 90 its front, 270 its
    back), pushed out by a factor."""
    t = t_at_z(y)
    c, ex, ey = torso_frame(t)
    a, f, b, sunk = torso_dims(t)
    th = math.radians(deg)
    s = math.sin(th)
    depth = f * sunk if s > 0 else b
    return c + (ex * a * math.cos(th) + ey * depth * s) * push


def radial(deg):
    a = math.radians(deg)
    return V((math.cos(a), math.sin(a), 0))


def arm_joints(s):
    sh = D(0.395 * s, 1.985, 0.08)
    el = sh + D(0.055 * s, -0.52, -0.05)
    wr = el + D(-0.01 * s, -0.55, 0.1)
    palm = wr + D(0, -0.13, 0.03)
    return dict(clav=D(0.07 * s, 1.96, 0.12), shoulder=sh, elbow=el, wrist=wr, palm=palm)


def leg_joints(s):
    return dict(hip=D(0.11 * s, 1.12, -0.01), knee=D(0.155 * s, 0.625, 0.1), ankle=D(0.14 * s, 0.1, -0.015),
                ball=D(0.125 * s, 0.03, 0.17), toe=D(0.115 * s, 0.015, 0.29))


def finger_chains(s, J):
    """Four long fingers and a thumb off a hanging hand, the palm toward the thigh: their joints, knuckle to tip."""
    palm, wr = J["palm"], J["wrist"]
    out = []
    lens = [(0.1, 0.085, 0.07), (0.12, 0.1, 0.085), (0.12, 0.1, 0.085), (0.1, 0.085, 0.07)]
    for i in range(4):
        spread = (i - 1.5) * 0.028
        k = palm + V((0, spread, 0.01))
        d = V((0, spread * 1.2, -1)).normalized()
        pts, cur = [k], k
        for j, L in enumerate(lens[i]):
            curl = 0.1 + j * 0.1
            d = (d * math.cos(curl) + V((-s, 0, 0)) * math.sin(curl)).normalized()
            cur = cur + d * L
            pts.append(cur)
        out.append(pts)
    t0 = wr.lerp(palm, 0.4) + V((-s * 0.012, 0.04, 0))
    d = V((-s * 0.3, 0.55, -0.8)).normalized()
    thumb, cur = [t0], t0
    for j, L in enumerate((0.07, 0.06, 0.05)):
        d = (d * math.cos(0.18) + V((-s, 0, -0.3)).normalized() * math.sin(0.18)).normalized()
        cur = cur + d * L
        thumb.append(cur)
    out.append(thumb)
    return out


def toe_chains(s, L):
    out = []
    for k in range(4):
        sp = (k - 1.5) * 0.022
        a = L["ball"] + V((sp - s * 0.004, 0, 0))
        b = a + V((sp * 0.4 - s * 0.01, 0.065 - abs(k - 1.5) * 0.008, -0.012))
        c = b + V((sp * 0.2 - s * 0.006, 0.05 - abs(k - 1.5) * 0.008, -0.018))
        out.append([a, b, c])
    return out


# ------------------------------------------------------------------ materials (their names are what the game reads)

def materials():
    return dict(hide=mat("s_hide", (0.06, 0.055, 0.05), 0.9), face=mat("s_face", (0.3, 0.29, 0.27), 0.8),
                rag=mat("s_rag", (0.03, 0.028, 0.026), 1.0), bark=mat("s_bark", (0.07, 0.055, 0.045), 1.0),
                nail=mat("s_nail", (0.015, 0.014, 0.013), 0.4), eye=mat("s_eye", (0.9, 0.8, 0.45), 0.2),
                tooth=mat("s_tooth", (0.55, 0.5, 0.4), 0.4), mouth=mat("s_mouth", (0.02, 0.006, 0.006), 0.6))


# ------------------------------------------------------------------ the flesh

def loft_torso():
    bm = bmesh.new()
    nt, nr = 48, 40
    rings = []
    for i in range(nt + 1):
        t = i / nt
        ring = [bm.verts.new(torso_point(t, j / nr * math.tau)) for j in range(nr)]
        rings.append(ring)
    for i in range(nt):
        for j in range(nr):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % nr], rings[i + 1][(j + 1) % nr], rings[i + 1][j]))
    for ring, c in ((rings[0], spine_at(0) + V((0, 0, -0.05))), (rings[-1], spine_at(1) + V((0, -0.01, 0.05)))):
        cv = bm.verts.new(c)
        for j in range(nr):
            bm.faces.new((ring[j], ring[(j + 1) % nr], cv))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, "Torso")


def chain(name, pts, radii, subdiv=1):
    return skin_tree(name, pts, [(i - 1, i) for i in range(1, len(pts))], radii, None, subdiv=subdiv)


def ribs_curves():
    out = []
    for s in (1, -1):
        for i in range(8):
            z0 = D(0, 1.84 - i * 0.045 + 0.006 * math.sin(i * 2.3 + s), 0).z
            end = 1.3 if i < 5 else 0.95 - (i - 5) * 0.22
            pts = []
            for k in range(15):
                u = k / 14
                th = -math.pi / 2 + u * (math.pi / 2 + end)
                z = z0 - 0.13 * u - 0.07 * u * u
                ang = th if s > 0 else math.pi - th
                pts.append(torso_point(t_at_z(z), ang, 0.0))
            out.append((s, i, pts))
    return out


def remesh(ob, voxel, smooth=4):
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel, adaptivity=0.0)
    apply_mods(ob)
    add_mod(ob, "SMOOTH", factor=0.6, iterations=smooth)
    apply_mods(ob)
    return ob


def build_body(m):
    """The hide from the hips to the neck and down the limbs to the wrists and ankles, fused."""
    parts = [loft_torso()]
    parts.append(chain("Neck", [NECK_BASE + V((0, -0.05, 0.02)), D(0, 1.99, 0.21), D(0.01, 1.975, 0.29), D(0.02, 1.95, 0.36), H + V((0, -0.01, -0.02))],
                       [0.07, 0.05, 0.041, 0.038, 0.04]))
    for s in (1, -1):
        parts.append(chain(f"NeckCord{s}", [H + V((s * 0.022, -0.02, -0.03)), D(0.04 * s, 1.97, 0.18)], [0.011, 0.014]))
        J = arm_joints(s)
        sh, el, wr = J["shoulder"], J["elbow"], J["wrist"]
        parts.append(chain(f"Arm{s}", [J["clav"], J["clav"].lerp(sh, 0.6) + V((0, 0, 0.03)), sh, sh.lerp(el, 0.3), sh.lerp(el, 0.75), el,
                                       el.lerp(wr, 0.25), el.lerp(wr, 0.7), wr],
                           [0.05, 0.06, 0.066, 0.056, 0.042, 0.048, 0.05, 0.034, 0.028]))
        parts.append(lumpy_sphere(f"Delt{s}", sh + V((s * 0.02, 0.0, -0.03)), (0.06, 0.065, 0.085), None, 11 + s, amp=0.07, subdiv=3))
        parts.append(lumpy_sphere(f"Elbow{s}", el + V((0, -0.022, 0)), (0.032, 0.034, 0.042), None, 12 + s, amp=0.07, subdiv=3))
        parts.append(chain(f"Trap{s}", [NECK_BASE + V((s * 0.02, -0.08, 0.04)), D(0.2 * s, 2.02, 0.04), sh + V((-s * 0.04, -0.02, 0.03))], [0.05, 0.06, 0.055]))
        parts.append(lumpy_sphere(f"WristKnob{s}", wr + V((s * 0.014, -0.006, 0.01)), (0.022, 0.022, 0.024), None, 13 + s, amp=0.06, subdiv=2))
        for k, off in enumerate((V((s * 0.024, 0.02, 0)), V((-s * 0.016, 0.026, 0)), V((s * 0.008, -0.026, 0)))):
            parts.append(chain(f"Sinew{s}{k}", [el.lerp(wr, 0.1) + off * 1.3, el.lerp(wr, 0.55) + off, wr.lerp(el, 0.05) + off * 0.55], [0.009, 0.008, 0.006]))
        L = leg_joints(s)
        hp, kn, an = L["hip"], L["knee"], L["ankle"]
        parts.append(chain(f"Leg{s}", [hp + V((0, 0, 0.05)), hp.lerp(kn, 0.3), hp.lerp(kn, 0.8), kn, kn.lerp(an, 0.3) + V((0, -0.02, 0)), kn.lerp(an, 0.8), an],
                           [0.092, 0.08, 0.052, 0.056, 0.05, 0.034, 0.032]))
        parts.append(lumpy_sphere(f"Knee{s}", kn + V((0, 0.012, 0.005)), (0.04, 0.036, 0.046), None, 20 + s, amp=0.08, subdiv=3))
        parts.append(lumpy_sphere(f"Shin{s}", kn.lerp(an, 0.35) + V((0, -0.025, 0)), (0.04, 0.035, 0.11), None, 24 + s, amp=0.08, subdiv=3))
        parts.append(lumpy_sphere(f"HipBone{s}", D(0.125 * s, 1.21, 0.03), (0.022, 0.04, 0.026), None, 22 + s, amp=0.08, subdiv=2))
        parts.append(lumpy_sphere(f"Blade{s}", torso_point(0.8, -math.pi / 2 + s * 0.6, -0.015), (0.085, 0.028, 0.11), None, 23 + s, amp=0.07, subdiv=3))
        parts.append(chain(f"Collar{s}", [torso_point(0.97, math.pi / 2 + s * -0.25, -0.004), J["shoulder"] + V((-s * 0.05, 0.05, 0.03))], [0.016, 0.02]))
        parts.append(chain(f"Achilles{s}", [kn.lerp(an, 0.45) + V((0, -0.035, 0)), an + V((0, -0.04, 0.02))], [0.011, 0.01]))
    for k in range(22):
        t = 0.06 + k * 0.042
        parts.append(lumpy_sphere(f"Vert{k}", torso_point(t, -math.pi / 2, 0.004), (0.02, 0.019, 0.017), None, 200 + k, amp=0.1, subdiv=2))
    parts.append(chain("Sternum", [torso_point(0.95, math.pi / 2, -0.006), torso_point(0.6, math.pi / 2, -0.006)], [0.014, 0.013]))
    for s, i, pts in ribs_curves():
        parts.append(chain(f"RibRidge{s}_{i}", pts, [0.0085] * len(pts), subdiv=0))
    body = join(parts, "Body")
    remesh(body, 0.0062)
    body.data.materials.clear()
    body.data.materials.append(m["hide"])
    print(f"[stalker] body remeshed: {len(body.data.polygons)} faces")
    return body


def build_hand(m, s):
    J = arm_joints(s)
    wr, palm = J["wrist"], J["palm"]
    parts = [chain(f"Wrist{s}", [wr.lerp(J["elbow"], 0.08), wr, wr.lerp(palm, 0.5)], [0.027, 0.026, 0.022])]
    pb = lumpy_sphere(f"Palm{s}", wr.lerp(palm, 0.6), (0.014, 0.034, 0.055), None, 14 + s, amp=0.05, subdiv=3)
    parts.append(pb)
    for k, pts in enumerate(finger_chains(s, J)):
        thumb = k == 4
        r = [0.0115, 0.0102, 0.009, 0.0072] if not thumb else [0.013, 0.011, 0.0095, 0.0078]
        root = palm.lerp(pts[0], 0.3) if not thumb else wr.lerp(palm, 0.25)
        parts.append(chain(f"Finger{s}_{k}", [root] + pts, [r[0]] + r))
        for j, q in enumerate(pts[:-1]):
            parts.append(lumpy_sphere(f"Knuckle{s}_{k}_{j}", q, (r[j] * 1.25,) * 3, None, 100 + k * 5 + j, amp=0.08, subdiv=2))
    hand = join(parts, f"Hand{s}")
    remesh(hand, 0.0026, 3)
    hand.data.materials.clear()
    hand.data.materials.append(m["hide"])
    return hand


def build_foot(m, s):
    L = leg_joints(s)
    an, ball = L["ankle"], L["ball"]
    parts = [chain(f"Ankle{s}", [an + V((0, 0, 0.05)), an, an.lerp(ball, 0.55) + V((0, 0, 0.0)), ball], [0.03, 0.032, 0.03, 0.024])]
    parts.append(lumpy_sphere(f"Heel{s}", an + V((0, -0.022, -0.045)), (0.024, 0.032, 0.026), None, 30 + s, amp=0.06, subdiv=3))
    parts.append(lumpy_sphere(f"AnkleKnob{s}", an + V((s * 0.018, 0, 0)), (0.012, 0.015, 0.015), None, 31 + s, amp=0.06, subdiv=2))
    for k, pts in enumerate(toe_chains(s, L)):
        parts.append(chain(f"Toe{s}_{k}", pts, [0.012, 0.0095, 0.0075]))
    foot = join(parts, f"Foot{s}")
    remesh(foot, 0.0035, 3)
    foot.data.materials.clear()
    foot.data.materials.append(m["hide"])
    return foot


# ------------------------------------------------------------------ the head: a small, long skull; the face a mask

def head_local(p):
    """A point from the head's own frame (x across, y the face's way, z up the skull) to the figure's."""
    return (Matrix.Translation(HC) @ HEAD_ROT @ p.to_4d()).to_3d()


# the sockets (the face pass, 2026-10-03, the owner: "creepier eyes and facial features"): not a pair. Its left (-x)
# small, high and narrow; its right wide, deep and dropped lower, its eye bulging in it, too big for it
SOCKETS = [V((-0.029, 0.088, 0.013)), V((0.032, 0.088, 0.001))]
SOCKET_R = [(0.015, 0.02, 0.04), (0.021, 0.029, 0.052)]      # (across, up-down, depth)
# the jaw torn loose on its left: there the face below the slit is the jaw's alone (a clean tear), and a few sinews
# still span the tear; the right hinge holds (the game hangs it from there)
TORN_SIDE = -1
MOUTH_Z = -0.112


def build_head(m):
    rings = [((0, 0.045, -0.175), 0.02, 0.026), ((0, 0.03, -0.135), 0.044, 0.05), ((0, 0.005, -0.075), 0.047, 0.08),
             ((0, -0.005, -0.02), 0.076, 0.098), ((0, -0.002, 0.035), 0.08, 0.116), ((0, -0.03, 0.09), 0.072, 0.106),
             ((0, -0.065, 0.14), 0.064, 0.1), ((0, -0.095, 0.18), 0.048, 0.078), ((0, -0.11, 0.205), 0.02, 0.032)]
    pts = [V(c) for c, _, _ in rings]

    def squash(t, a):
        # the face a flat mask: its front pressed flat
        s = math.sin(a)
        # (down over the jaw too: a jaw left full stood out under the flat mask like a beak)
        bell = math.sin(math.pi * min(max((t + 0.18) / 0.98, 0.0), 1.0))
        return 1.0 - 0.24 * bell * max(0.0, s - 0.45) / 0.55
    skull = W.loft_path("Skull", pts, [w for _, w, _ in rings], [d for _, _, d in rings], up=V((0, 1, 0)), sides=28, squash=squash)
    parts = [skull]
    for sx in (-1, 1):
        # the cheekbones and the hinge of the jaw only as a shallow ridge under the mask's edge
        parts.append(chain(f"Cheek{sx}", [V((sx * 0.06, 0.035, -0.005)), V((sx * 0.052, 0.06, -0.03))], [0.012, 0.009]))
    head = join(parts, "Head")
    remesh(head, 0.0024, 4)
    # carve: the sockets deep, the mouth's slit (too wide, lipless), the nose's two slits
    bm = bmesh.new()
    bm.from_mesh(head.data)
    floor = [1.0, 1.0]
    for v in bm.verts:
        p = v.co
        if p.y < 0.03:
            continue
        push = 0.0
        for c, (rx, rz, depth) in zip(SOCKETS, SOCKET_R):
            d = ((p.x - c.x) / rx) ** 2 + ((p.z - c.z + 0.004) / rz) ** 2
            if d < 1.0:
                push = max(push, depth * (1 - d) ** 0.45)
            # creases radiating from the socket, the skin drawn into it
            dx, dz = p.x - c.x, p.z - c.z
            r = math.hypot(dx / rx, dz / rz)
            if 1.0 < r < 1.9:
                a = math.atan2(dz, dx) * 7.0 / (2 * math.pi) + (0.3 if c.x > 0 else 0.0)
                f = abs(a - round(a))
                if f < 0.07:
                    push = max(push, 0.0028 * (1 - f / 0.07) * (1 - (r - 1.0) / 0.9))
        # the cheeks hollow under the bone
        for sx in (-1, 1):
            d = ((p.x - sx * 0.042) / 0.02) ** 2 + ((p.z + 0.05) / 0.028) ** 2
            if d < 1.0:
                push = max(push, 0.008 * (1 - d) ** 1.5)
        # the nose gone: a cavity, narrowing to its foot
        nz = (p.z + 0.052) / 0.016
        if -1.0 < nz < 1.0:
            half = 0.009 * (0.55 + 0.45 * (nz + 1.0) / 2.0)
            if abs(p.x) < half:
                push = max(push, 0.016 * (1 - (p.x / half) ** 2) * (1 - nz * nz) ** 0.5)
        # the slit straight across, its corners dragged down (from below it would read as a smile)
        mz = (p.z - (MOUTH_Z - 0.014 * (p.x / 0.052) ** 2)) / 0.0042
        mx = abs(p.x) / 0.054
        if mx < 1.0 and abs(mz) < 1.0:
            push = max(push, 0.012 * (1 - mx ** 4) * (1 - mz * mz))
        for nx in (-0.008, 0.008):
            d = ((p.x - nx) / 0.0025) ** 2 + ((p.z + 0.058) / 0.007) ** 2
            if d < 1.0:
                push = max(push, 0.006 * (1 - d))
        if push > 0:
            v.co = p - V((0, push, 0))
        for i, c in enumerate(SOCKETS):
            # (every vertex here was on the face's front before the carve: the deep socket's floor is pushed far back)
            if ((p.x - c.x) / (SOCKET_R[i][0] * 0.35)) ** 2 + ((p.z - c.z) / (SOCKET_R[i][1] * 0.25)) ** 2 < 1.0:
                floor[i] = min(floor[i], v.co.y)
    bm.to_mesh(head.data)
    bm.free()
    add_mod(head, "SMOOTH", factor=0.4, iterations=1)
    apply_mods(head)
    # the mouth (the horror pass): behind the slit a dark throat, and in it needle teeth, two crowded rows of them,
    # their tips just showing through the closed slit; the lower row rides the jaw, so they bare as it opens
    co = [V(v.co) for v in head.data.vertices]

    def slit_z(x):
        return MOUTH_Z - 0.014 * (x / 0.052) ** 2

    def lip(x, above):
        best = None
        for q in co:
            if abs(q.x - x) > 0.004 or q.y < 0.02:
                continue
            dz = q.z - slit_z(x)
            if (0.004 < dz < 0.011) if above else (-0.011 < dz < -0.004):
                best = q.y if best is None else max(best, q.y)
        return best
    mouth = []
    teeth_rnd = random.Random(31)
    for row, above in (("upper", True), ("lower", False)):
        n = 15 if above else 13
        for i in range(n):
            x = -0.046 + 0.092 * (i + teeth_rnd.uniform(-0.25, 0.25)) / (n - 1)
            y = lip(x, above)
            if y is None:
                continue
            z0 = slit_z(x) + (0.0055 if above else -0.0055)
            ln = teeth_rnd.uniform(0.012, 0.02) * (1.0 - 0.35 * abs(x) / 0.05)
            root = V((x, y - 0.006, z0))
            tip = root + V((teeth_rnd.uniform(-0.0015, 0.0015), teeth_rnd.uniform(-0.002, 0.001), -ln if above else ln))
            t = cylinder(f"Tooth{row}{i}", root, tip, teeth_rnd.uniform(0.0015, 0.0022), 0.0002, 5, m["tooth"])
            for v in t.data.vertices:
                v.co = head_local(V(v.co))
            mouth.append((t, "head" if above else "jaw", "tooth"))
    yc = lip(0.0, True) or 0.06
    # (well inside: any nearer the slit and it bulged through it like lips)
    throat = lumpy_sphere("Throat", (0, 0, 0), (0.044, 0.016, 0.028), m["mouth"], 70, amp=0.05, subdiv=3)
    for v in throat.data.vertices:
        v.co = head_local(V(v.co) + V((0, yc - 0.042, slit_z(0) - 0.006)))
    mouth.append((throat, "head", "throat"))
    # into the figure's frame
    for v in head.data.vertices:
        v.co = head_local(V(v.co))
    head.data.materials.clear()
    head.data.materials.append(m["face"])
    eyes = []
    pupils = []
    for i, c in enumerate(SOCKETS):
        big = i == 1
        r = (0.0098, 0.0085, 0.0092) if big else (0.0062, 0.005, 0.0056)
        # the small one far back in its socket; the big one swollen half out of its own
        cy = floor[i] + (0.016 if big else 0.003)
        e = lumpy_sphere("Eye", head_local(V((c.x, cy, c.z))), r, m["eye"], 60, amp=0.0, subdiv=2)
        eyes.append(e)
        look = V((0.32, 1.0, 0.38)).normalized() if big else V((-0.05, 1.0, -0.05)).normalized()
        pc = V((c.x, cy, c.z)) + V((look.x * r[0], look.y * r[1], look.z * r[2])) * 1.02
        pu = lumpy_sphere("Pupil", (0, 0, 0), (0.0016 if big else 0.0013, 0.0005, 0.0016 if big else 0.0013), m["mouth"], 20, amp=0.0, subdiv=2)
        # flat against the eye, facing out along its look
        rotq = V((0, 1, 0)).rotation_difference(look)
        for v in pu.data.vertices:
            v.co = head_local(pc + rotq @ V(v.co))
        pupils.append(pu)
    # the sinews still spanning the tear on its left, from the face above the slit to the jaw below it
    sin_rnd = random.Random(47)
    for k in range(6):
        x = TORN_SIDE * (0.012 + 0.04 * (k + sin_rnd.uniform(-0.3, 0.3)) / 5)
        y = (lip(x, True) or 0.05) - 0.004
        top = V((x + sin_rnd.uniform(-0.002, 0.002), y, slit_z(x) + 0.006))
        bot = V((x + sin_rnd.uniform(-0.004, 0.004), y - sin_rnd.uniform(0.0, 0.004), slit_z(x) - 0.006))
        sn = cylinder(f"Sinew{k}", top, bot, sin_rnd.uniform(0.0008, 0.0014), sin_rnd.uniform(0.0006, 0.001), 5, m["mouth"])
        lower = [v.index for v in sn.data.vertices if v.co.z < slit_z(x)]
        for v in sn.data.vertices:
            v.co = head_local(V(v.co))
        mouth.append((sn, ("sinew", lower), "sinew"))
    return head, eyes + pupils, mouth


# ------------------------------------------------------------------ the parts that aren't hide

def build_crowns(m):
    """The shoulders' crowns of gnarled root spikes (the owner: not round and bubbly, spiked roots) and the bark shards
    along the spine and the elbows."""
    P = []
    for s in (1, -1):
        c = D(0.365 * s, 2.075, 0.05)
        knots = []
        for w in range(5):
            a = w / 5 * math.tau + rnd.uniform(-0.3, 0.3)
            fr = c + V((math.cos(a) * 0.09, math.sin(a) * 0.08, -0.1 + rnd.uniform(-0.03, 0.03)))
            to = c + V((math.cos(a + 1.3) * 0.07 * s, math.sin(a + 1.3) * 0.07, 0.08))
            knots.append(chain(f"Knot{s}{w}", [fr, fr.lerp(to, 0.5) + V((rnd.uniform(-0.01, 0.01), 0, 0)), to], [0.04, 0.034, 0.026]))
        spikes = [((0.55 * s, -0.25, 0.9), 0.34), ((0.9 * s, -0.1, 0.45), 0.3), ((0.35 * s, -0.75, 0.8), 0.36), ((0.8 * s, -0.55, 0.15), 0.27),
                  ((0.15 * s, 0.2, 1.0), 0.22), ((0.95 * s, 0.35, 0.6), 0.2), ((0.5 * s, -0.95, 0.3), 0.3)]
        for i, (d0, ln) in enumerate(spikes):
            d = V(d0).normalized()
            p0 = c + d * 0.06
            kink = V((rnd.uniform(-0.35, 0.35), rnd.uniform(-0.35, 0.35), rnd.uniform(-0.2, 0.3)))
            p1 = p0 + (d + kink * 0.4).normalized() * ln * 0.45
            p2 = p1 + (d - kink * 0.3).normalized() * ln * 0.35
            p3 = p2 + (d + kink * 0.2 + V((0, 0, 0.15))).normalized() * ln * 0.3
            knots.append(chain(f"Spike{s}{i}", [p0, p1, p2, p3], [0.036, 0.026, 0.014, 0.002]))
            if rnd.random() < 0.6:
                sh = (d + V((rnd.uniform(-0.8, 0.8), rnd.uniform(-0.8, 0.8), rnd.uniform(0.1, 0.6)))).normalized()
                knots.append(chain(f"Shoot{s}{i}", [p1, p1 + sh * ln * 0.16, p1 + sh * ln * 0.3], [0.013, 0.007, 0.0015]))
        crown = join(knots, f"Crown{s}")
        crown.data.materials.clear()
        crown.data.materials.append(m["bark"])
        P.append((crown, "clav_R" if s > 0 else "clav_L"))
    shards = [((-0.44, 2.14, -0.02), (-0.8, 0.55, -0.6), 0.19, "chest"), ((-0.3, 2.16, -0.06), (-0.2, 0.5, -1), 0.15, "chest"),
              ((0.45, 2.17, -0.01), (0.8, 0.6, -0.5), 0.21, "chest"), ((0.32, 2.18, -0.07), (0.3, 0.45, -1), 0.14, "chest"),
              ((0.0, 2.06, -0.13), (0.1, 0.6, -1), 0.17, "chest"), ((0.02, 1.94, -0.18), (-0.1, 0.4, -1), 0.13, "chest"),
              ((-0.03, 1.78, -0.18), (0.1, 0.2, -1), 0.1, "chest"), ((-0.48, 1.98, 0.0), (-1, 0.4, -0.3), 0.14, "chest"),
              ((0.03, 1.62, -0.17), (0.05, 0.1, -1), 0.08, "spine"), ((-0.02, 1.48, -0.13), (0, 0.05, -1), 0.07, "spine")]
    for i, (r, d, ln, bone) in enumerate(shards):
        root = D(*r)
        dd = D(*d).normalized()
        side = dd.cross(V((0, 0, 1))).normalized()
        tip = root + dd * ln
        sh = chain(f"Shard{i}", [root - dd * 0.02, root.lerp(tip, 0.45) + side * 0.008, tip], [(0.034, 0.016), (0.022, 0.01), (0.002, 0.002)], subdiv=1)
        sh.data.materials.append(m["bark"])
        P.append((sh, bone))
    for s in (1, -1):
        J = arm_joints(s)
        el = J["elbow"]
        sh = chain(f"ElbowShard{s}", [el + D(0.02 * s, 0, -0.04), el + D(0.045 * s, 0.02, -0.11), el + D(0.06 * s, 0.03, -0.17)], [0.024, 0.012, 0.002])
        sh.data.materials.append(m["bark"])
        P.append((sh, "upper_R" if s > 0 else "upper_L"))
    return P


def build_nails(m):
    P = []
    for s in (1, -1):
        sx = "R" if s > 0 else "L"
        J = arm_joints(s)
        for k, pts in enumerate(finger_chains(s, J)):
            tip, prev = pts[-1], pts[-2]
            d = (tip - prev).normalized()
            hook = (d + V((-s, 0, 0)) * 0.5).normalized()
            a = tip - d * 0.012
            b = a + d * 0.018 + hook * 0.004
            c = b + hook * 0.016
            n = chain(f"Nail{s}{k}", [a, b, c], [0.0075, 0.005, 0.0008])
            n.data.materials.append(m["nail"])
            P.append((n, f"f{k}_3_{sx}"))
        L = leg_joints(s)
        for k, pts in enumerate(toe_chains(s, L)):
            tip, prev = pts[-1], pts[-2]
            d = (tip - prev).normalized()
            n = chain(f"ToeNail{s}{k}", [tip - d * 0.008, tip + d * 0.01, tip + d * 0.018 + V((0, 0, -0.008))], [0.007, 0.004, 0.0008])
            n.data.materials.append(m["nail"])
            P.append((n, f"toe_{sx}"))
    return P


# ------------------------------------------------------------------ the rags: strips with torn ends, each bunch of them
# on its own chain of bones (RAG_CHAINS: name, parent bone, anchor, the chain's hang (down and out), its length)

RAG_CHAINS = []
RAG_SEGS = 6


def rag_strip(bm, layer_c, layer_u, chain_i, a, out_dir, ln, w, flare):
    """A hanging strip from a, down ln, drifting out along out_dir, ending in two torn tongues."""
    out_dir = V((out_dir.x, out_dir.y, 0)).normalized()
    across = V((0, 0, 1)).cross(out_dir).normalized()
    twist = rnd.uniform(-0.35, 0.35)
    drift = rnd.uniform(-0.04, 0.04)
    rows = []
    for i in range(RAG_SEGS + 1):
        t = i / RAG_SEGS
        c = a + V((0, 0, -ln * t)) + out_dir * (flare * t * t + 0.03 * math.sin(math.pi * t)) + across * drift * t
        if i > 0:
            c += V((rnd.uniform(-0.01, 0.01), rnd.uniform(-0.01, 0.01), 0))
        wi = w * (1 - 0.4 * t) * (rnd.uniform(0.75, 1.2) if i > 0 else 1.0)
        ac = (Matrix.Rotation(twist * t, 3, "Z") @ across)
        # a slight fold across the strip so it isn't a flat card
        mid = c + out_dir * 0.008 * math.sin(i * 1.7)
        rows.append((c - ac * wi * 0.5, mid, c + ac * wi * 0.5, t))
    vs = []
    for (l, mid, r, t) in rows:
        row = [bm.verts.new(l), bm.verts.new(mid), bm.verts.new(r)]
        for v in row:
            v[layer_c] = chain_i + 1
            v[layer_u] = t
        vs.append(row)
    for i in range(RAG_SEGS - 1):
        for j in range(2):
            bm.faces.new((vs[i][j], vs[i][j + 1], vs[i + 1][j + 1], vs[i + 1][j]))
    # the torn end: two ragged tongues either side of a notch
    e = RAG_SEGS - 1
    l, mid, r = vs[e]
    seg = ln / RAG_SEGS
    down = V((0, 0, -1))
    notch = bm.verts.new(mid.co + down * seg * rnd.uniform(0.1, 0.45))
    tl = bm.verts.new(l.co.lerp(mid.co, 0.3) + down * seg * rnd.uniform(0.8, 1.6))
    tr = bm.verts.new(r.co.lerp(mid.co, 0.3) + down * seg * rnd.uniform(0.6, 1.4))
    for v, u in ((notch, 1.0), (tl, 1.0), (tr, 1.0)):
        v[layer_c] = chain_i + 1
        v[layer_u] = u
    bm.faces.new((l, mid, notch))
    bm.faces.new((mid, r, notch))
    bm.faces.new((l, notch, tl))
    bm.faces.new((notch, r, tr))


def build_rags(m):
    bm = bmesh.new()
    lc = bm.verts.layers.int.new("rag_chain")
    lu = bm.verts.layers.float.new("rag_u")

    def new_chain(parent, anchor, hang, ln):
        RAG_CHAINS.append((f"rag{len(RAG_CHAINS)}", parent, anchor, hang.normalized(), ln))
        return len(RAG_CHAINS) - 1

    # the mantle off the shoulders, longest at the back: in pairs, a chain each
    angs = [-10 + i * (200 / 15) + 170 + rnd.uniform(-6, 6) for i in range(16)]
    for p in range(8):
        group = angs[p * 2:p * 2 + 2]
        mid_ang = sum(group) / 2
        backness = max(0.0, -math.sin(math.radians(mid_ang)))
        ln = (0.22 + 0.78 * backness * backness) * 1.1
        anchor = tp_design(2.03, mid_ang, 1.12)
        ci = new_chain("chest", anchor, radial(mid_ang) * 0.15 + V((0, 0, -1)), ln)
        for ang in group:
            b = max(0.0, -math.sin(math.radians(ang)))
            l = (0.22 + 0.78 * b * b) * rnd.uniform(0.75, 1.2)
            rag_strip(bm, lc, lu, ci, tp_design(2.02 + rnd.uniform(-0.04, 0.06), ang, 1.12), radial(ang), l, rnd.uniform(0.1, 0.17), rnd.uniform(0.04, 0.14))
    # a few short ones over the chest
    for p in range(2):
        a0 = 55 + p * 46
        ci = new_chain("chest", tp_design(1.97, a0 + 11, 1.08), radial(a0 + 11) * 0.1 + V((0, 0, -1)), 0.45)
        for q in range(2):
            ang = a0 + q * 23 + rnd.uniform(-5, 5)
            rag_strip(bm, lc, lu, ci, tp_design(1.97, ang, 1.08), radial(ang), rnd.uniform(0.3, 0.55), rnd.uniform(0.08, 0.13), 0.05)
    # the long strips down the back to the shins
    for p in range(3):
        a0 = 225 + p * 36
        ci = new_chain("chest", tp_design(1.95, a0 + 9, 1.1), radial(a0 + 9) * 0.12 + V((0, 0, -1)), 1.4)
        for q in range(2):
            ang = a0 + q * 18 + rnd.uniform(-5, 5)
            rag_strip(bm, lc, lu, ci, tp_design(1.9 + rnd.uniform(0, 0.12), ang, 1.1), radial(ang), rnd.uniform(1.25, 1.55), rnd.uniform(0.1, 0.16), rnd.uniform(0.1, 0.2))
    # the skirt at the hips (the front open so the legs show)
    hip_angs = [a for a in (150 + i * 24 + rnd.uniform(-7, 7) for i in range(11)) if math.sin(math.radians(a)) <= 0.6]
    for p in range(0, len(hip_angs), 2):
        group = hip_angs[p:p + 2]
        mid_ang = sum(group) / len(group)
        backness = max(0.0, -math.sin(math.radians(mid_ang)))
        ci = new_chain("hips", tp_design(1.24, mid_ang, 1.2), radial(mid_ang) * 0.1 + V((0, 0, -1)), 0.5 + 0.35 * backness)
        for ang in group:
            b = max(0.0, -math.sin(math.radians(ang)))
            rag_strip(bm, lc, lu, ci, tp_design(1.24 + rnd.uniform(-0.04, 0.04), ang, 1.2), radial(ang), (0.5 + 0.35 * b) * rnd.uniform(0.8, 1.15),
                      rnd.uniform(0.1, 0.16), rnd.uniform(0.05, 0.12))
    # the sleeves: off the back and outside of the upper arm and the forearm
    for s in (1, -1):
        sx = "R" if s > 0 else "L"
        J = arm_joints(s)
        sh, el, wr = J["shoulder"], J["elbow"], J["wrist"]
        outward = V((s, -0.6, 0)).normalized()
        for bone, a0, a1 in ((f"upper_{sx}", sh, el), (f"fore_{sx}", el, wr)):
            anchor = a0.lerp(a1, 0.3) + outward * 0.05
            ci = new_chain(bone, anchor, outward * 0.1 + V((0, 0, -1)), 0.5)
            for q in range(2):
                a = a0.lerp(a1, rnd.uniform(0.1, 0.5)) + outward * 0.05
                o = Matrix.Rotation(rnd.uniform(-0.5, 0.5), 3, "Z") @ outward
                rag_strip(bm, lc, lu, ci, a, o, rnd.uniform(0.35, 0.7), rnd.uniform(0.07, 0.12), rnd.uniform(0.02, 0.08))
    ob = obj_from_bmesh(bm, "Rags", m["rag"])
    print(f"[stalker] rags: {len(ob.data.polygons)} faces on {len(RAG_CHAINS)} chains")
    return ob


# ------------------------------------------------------------------ the colours (linear) for the bake

def hide_colour(co, nrm):
    n1 = fbm(co.x * 5, co.y * 5, co.z * 5, 4)
    n2 = fbm(co.x * 17 + 3, co.y * 17, co.z * 17, 3)
    c = V((0.055, 0.05, 0.046)) * (0.75 + 0.5 * n1)
    # mottled: grey-green rot in patches, darker in the creases and down the back
    rot = max(0.0, fbm(co.x * 3 + 5, co.y * 3, co.z * 3, 3) - 0.05) * 1.5
    c = c.lerp(V((0.045, 0.055, 0.04)), min(rot, 0.6))
    c *= 0.85 + 0.25 * n2
    if nrm.z < -0.4:
        c *= 0.75
    return c.x, c.y, c.z


def face_colour(co, nrm):
    # the mask: a dirty pallor on the face's front, the hide's dark behind, stains run down from the eyes and mouth
    hl = (HEAD_ROT.inverted() @ (Matrix.Translation(-HC) @ co.to_4d())).to_3d()
    hn = (HEAD_ROT.to_3x3().inverted() @ nrm)
    front = min(max((hn.y - 0.35) / 0.4, 0.0), 1.0)
    n = fbm(co.x * 30, co.y * 30, co.z * 30, 3)
    pale = V((0.3, 0.29, 0.265)) * (0.85 + 0.3 * n)
    run = 0.0
    for c in SOCKETS:
        if abs(hl.x - c.x) < 0.01 + 0.004 * n and hl.z < c.z and hl.y > 0.06:
            run = max(run, 0.7 * (1 - (c.z - hl.z) / 0.12))
    # the tear on its left: raw and dark along the slit's lower edge
    if TORN_SIDE * hl.x > 0.008 and -0.016 < hl.z - (MOUTH_Z - 0.014 * (hl.x / 0.052) ** 2) < 0.0:
        run = 1.0
    if abs(hl.z - (MOUTH_Z - 0.014 * (hl.x / 0.052) ** 2)) < 0.006 and abs(hl.x) < 0.054:
        run = 1.0
    base = V(hide_colour(co, nrm))
    col = base.lerp(pale, front).lerp(V((0.03, 0.025, 0.022)), max(0.0, run))
    return col.x, col.y, col.z


def bark_colour(co, nrm):
    n = fbm(co.x * 8, co.y * 8, co.z * 40, 4)
    c = V((0.075, 0.058, 0.045)) * (0.7 + 0.5 * n)
    return c.x, c.y, c.z


def flat(rgb):
    return lambda co, nrm: rgb


# the game's tones (vertex colour): r the face's paleness, g brightness, b (unused), a 0 marks the eyes
def tone_hide(co, nrm):
    return (0.0, 1.0, 1.0, 1.0)


def tone_face(co, nrm):
    hl = (HEAD_ROT.inverted() @ (Matrix.Translation(-HC) @ co.to_4d())).to_3d()
    hn = HEAD_ROT.to_3x3().inverted() @ nrm
    front = min(max((hn.y - 0.55) / 0.4, 0.0), 1.0)
    dark = 1.0
    for c, (rx, rz, _) in zip(SOCKETS, SOCKET_R):
        d = math.hypot((hl.x - c.x) / (rx * 1.2), (hl.z - c.z + 0.004) / (rz * 1.15))
        if d < 1.0 and hl.y > 0.03:
            dark = min(dark, 0.3 + 0.7 * d)
            front *= d
    if abs(hl.z - (MOUTH_Z - 0.014 * (hl.x / 0.052) ** 2)) < 0.005 and abs(hl.x) < 0.054:
        dark = 0.3
        front *= 0.3
    return (0.75 * front, dark, 0.6, 1.0)


def tone_rag(co, nrm):
    return (0.0, 0.85, 0.25, 1.0)


def tone_bark(co, nrm):
    return (0.0, 1.0, 0.1, 1.0)


def tone_eye(co, nrm):
    return (0.0, 0.3, 0.0, 0.0)


def paint4(ob, fn, attr="Tone"):
    me = ob.data
    if attr not in me.color_attributes:
        me.color_attributes.new(attr, "FLOAT_COLOR", "POINT")
    a = me.color_attributes[attr]
    cols = np.zeros((len(me.vertices), 4), np.float32)
    for v in me.vertices:
        cols[v.index] = fn(v.co, v.normal)
    a.data.foreach_set("color", cols.ravel())


# ------------------------------------------------------------------ the bake (the hide's micro-detail: a cracked,
# leathery, bark-like grain)

def bump_material(name):
    mt = bpy.data.materials.new(name)
    mt.use_nodes = True
    nt = mt.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    cells = nt.nodes.new("ShaderNodeTexVoronoi")
    cells.feature = "DISTANCE_TO_EDGE"
    cells.inputs["Scale"].default_value = 55.0
    grain = nt.nodes.new("ShaderNodeTexNoise")
    grain.inputs["Scale"].default_value = 90.0
    grain.inputs["Detail"].default_value = 10.0
    grain.inputs["Roughness"].default_value = 0.7
    wrink = nt.nodes.new("ShaderNodeTexWave")
    wrink.wave_type = "BANDS"
    wrink.bands_direction = "Z"
    wrink.inputs["Scale"].default_value = 70.0
    wrink.inputs["Distortion"].default_value = 9.0
    wrink.inputs["Detail"].default_value = 3.0
    for n in (cells, grain, wrink):
        nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
    cr = nt.nodes.new("ShaderNodeMapRange")
    cr.inputs["From Min"].default_value = 0.0
    cr.inputs["From Max"].default_value = 0.06
    cr.inputs["To Min"].default_value = 0.0
    cr.inputs["To Max"].default_value = 1.0
    nt.links.new(cells.outputs["Distance"], cr.inputs["Value"])
    a1 = nt.nodes.new("ShaderNodeMath")
    a1.operation = "MULTIPLY_ADD"
    nt.links.new(grain.outputs["Fac"], a1.inputs[0])
    a1.inputs[1].default_value = 0.5
    nt.links.new(cr.outputs["Result"], a1.inputs[2])
    a2 = nt.nodes.new("ShaderNodeMath")
    a2.operation = "MULTIPLY_ADD"
    nt.links.new(wrink.outputs["Fac"], a2.inputs[0])
    a2.inputs[1].default_value = 0.35
    nt.links.new(a1.outputs["Value"], a2.inputs[2])
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "detail"
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    nt.links.new(a2.outputs["Value"], mul.inputs[0])
    nt.links.new(attr.outputs["Fac"], mul.inputs[1])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.7
    bump.inputs["Distance"].default_value = 0.0015
    nt.links.new(mul.outputs["Value"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mt


def bake_maps(low, high, size):
    keep = [s.material for s in high.material_slots]
    W.set_all_materials(high, bump_material("stalker_bump"))
    nimg = W.new_image("stalker_normal", size, True)
    W.bake(low, high, "NORMAL", nimg, extrusion=0.01, dist=0.03)
    W.set_all_materials(high, W.emit_vc_material("stalker_vc"))
    cimg = W.new_image("stalker_albedo", size, False)
    W.bake(low, high, "EMIT", cimg, extrusion=0.01, dist=0.03)
    aimg = W.new_image("stalker_ao", size, True)
    W.bake(low, None, "AO", aimg, samples=48)
    c = np.array(cimg.pixels[:], np.float32).reshape(size, size, 4)
    a = np.array(aimg.pixels[:], np.float32).reshape(size, size, 4)[:, :, 0]
    for _ in range(3):
        a = (a + np.roll(a, 1, 0) + np.roll(a, -1, 0) + np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 5.0
    c[:, :, :3] *= (0.55 + 0.45 * a)[:, :, None]
    cimg.pixels = c.ravel()
    for img in (cimg, nimg):
        img.filepath_raw = os.path.join(PREV, img.name + ".png")
        img.file_format = "PNG"
        img.save()
    print(f"[stalker] baked albedo and normal at {size} px")
    return cimg, nimg


# ------------------------------------------------------------------ the skeleton

def bone_list():
    """(name, head, tail, parent, radius) in the rest pose."""
    B = [("root", V((0, 0, 0)), V((0, 0.3, 0)), None, 0.0)]
    B.append(("hips", spine_at(0.0), spine_at(0.22), "root", 0.15))
    B.append(("spine", spine_at(0.22), spine_at(0.55), "hips", 0.13))
    B.append(("chest", spine_at(0.55), spine_at(1.0), "spine", 0.22))
    B.append(("neck", NECK_BASE + V((0, -0.04, 0.0)), D(0.01, 1.975, 0.29), "chest", 0.045))
    B.append(("neck2", D(0.01, 1.975, 0.29), H, "neck", 0.04))
    B.append(("head", H, head_local(V((0, 0.12, 0.0))), "neck2", 0.06))
    B.append(("jaw", head_local(V((0, -0.01, -0.06))), head_local(V((0, 0.055, -0.17))), "head", 0.02))
    for s, sx in ((1, "R"), (-1, "L")):
        J = arm_joints(s)
        B.append((f"clav_{sx}", J["clav"], J["shoulder"], "chest", 0.06))
        B.append((f"upper_{sx}", J["shoulder"], J["elbow"], f"clav_{sx}", 0.05))
        B.append((f"fore_{sx}", J["elbow"], J["wrist"], f"upper_{sx}", 0.04))
        B.append((f"hand_{sx}", J["wrist"], J["palm"], f"fore_{sx}", 0.025))
        for k, pts in enumerate(finger_chains(s, J)):
            for j in range(3):
                B.append((f"f{k}_{j + 1}_{sx}", pts[j], pts[j + 1], f"hand_{sx}" if j == 0 else f"f{k}_{j}_{sx}", 0.01))
        L = leg_joints(s)
        B.append((f"thigh_{sx}", L["hip"], L["knee"], "hips", 0.08))
        B.append((f"shin_{sx}", L["knee"], L["ankle"], f"thigh_{sx}", 0.05))
        B.append((f"foot_{sx}", L["ankle"], L["ball"], f"shin_{sx}", 0.03))
        B.append((f"toe_{sx}", L["ball"], L["toe"], f"foot_{sx}", 0.012))
    for (name, parent, anchor, hang, ln) in RAG_CHAINS:
        prev = parent
        for j in range(3):
            a = anchor + hang * ln * j / 3
            b = anchor + hang * ln * (j + 1) / 3
            B.append((f"{name}_{j}", a, b, prev, 0.0))
            prev = f"{name}_{j}"
    return B


def skin_weights(ob, arm_obj, bones):
    """The rags along their chains (by where on the strip each vertex is); the rigid parts wholly to their bone; the
    hide to its nearest bone, blended across the joints (as the wendigo's)."""
    me = ob.data
    P = np.array([v.co[:] for v in me.vertices], np.float32)
    info = {b[0]: b for b in bones}
    body = [b[0] for b in bones if b[0] != "root" and not b[0].startswith("rag")]
    children = {}
    for b in bones:
        if b[3] and not b[0].startswith("rag"):
            children.setdefault(b[3], []).append(b[0])
    Dm = np.zeros((len(P), len(body)), np.float32)
    T = np.zeros((len(P), len(body)), np.float32)
    for i, n in enumerate(body):
        _, h, t, _, r = info[n]
        d, tt = W.seg_dist(P, np.array(h[:], np.float32), np.array(t[:], np.float32))
        Dm[:, i] = d - r
        T[:, i] = tt
    near = np.argmin(Dm, axis=1)
    groups = {b[0]: ob.vertex_groups.new(name=b[0]) for b in bones if b[0] != "root"}
    forced = {vg.index: vg.name[2:] for vg in ob.vertex_groups if vg.name.startswith("P_") and vg.name != "P_flesh"}
    rc = me.attributes.get("rag_chain")
    ru = me.attributes.get("rag_u")
    rcv = np.zeros(len(P), np.int32)
    ruv = np.zeros(len(P), np.float32)
    if rc is not None:
        rc.data.foreach_get("value", rcv)
        ru.data.foreach_get("value", ruv)
    head_g = None
    for v in me.vertices:
        w = None
        ci = int(rcv[v.index])
        if ci > 0:
            name = RAG_CHAINS[ci - 1][0]
            x = min(max(float(ruv[v.index]), 0.0), 1.0) * 3 - 0.5     # bone centres at 0, 1, 2
            if x <= 0:
                w = {f"{name}_0": 1.0}
            elif x >= 2:
                w = {f"{name}_2": 1.0}
            else:
                i0 = int(x)
                f = x - i0
                w = {f"{name}_{i0}": 1 - f, f"{name}_{i0 + 1}": f}
        else:
            for g in v.groups:
                if g.group in forced and g.weight > 0.5:
                    w = {forced[g.group]: 1.0}
            rigid = w is not None
            if w is None:
                i = near[v.index]
                n = body[i]
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
            if "head" in w and len(w) == 1 and not rigid:
                # the jaw: the face below the mouth's slit (its corners dragged down), forward of the hinge
                hl = (HEAD_ROT.inverted() @ (Matrix.Translation(-HC) @ V(P[v.index]).to_4d())).to_3d()
                sz = MOUTH_Z - 0.014 * (min(abs(hl.x), 0.06) / 0.052) ** 2
                torn = min(max((TORN_SIDE * hl.x - 0.006) / 0.012, 0.0), 1.0)   # 0 the held side .. 1 the torn
                if hl.z < sz - 0.002 + 0.0015 * torn and hl.y > -0.03:
                    k = min(1.0, (sz - 0.002 - hl.z) / 0.01)
                    k = k + (1.0 - k) * torn
                    w = {"head": 1 - k, "jaw": k}
        for n, x in w.items():
            if x > 0:
                groups[n].add([v.index], x, "REPLACE")
    ob.parent = arm_obj
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj


# ------------------------------------------------------------------ posing

def rot(axis, a):
    return Matrix.Rotation(a, 4, axis)


def pose(poser, hip=V((0, 0, 0)), bow=0.0, lean=0.0, twist=0.0, neck=0.0, head_pitch=0.0, head_roll=0.0, head_yaw=0.0,
         legs=None, arms=None, curl=0.3, jaw=0.0):
    """A pose from a few settings. bow: the torso bending forward (radians, from the rest's hunch); lean: toward +X;
    twist: turning left; legs[sx] = dict(ankle=, foot=, pole=); arms[sx] = dict(wrist=, hand=, pole=, curl=, spread=)."""
    dirs, spins = {}, {}
    rest = poser.info
    for n, k in (("hips", 0.3), ("spine", 0.7), ("chest", 1.0)):
        h, t = rest[n][1], rest[n][2]
        M = rot("Z", twist * k) @ rot("Y", lean * k) @ rot("X", -bow * k)
        dirs[n] = (M @ (t - h).to_4d()).to_3d()
    M = rot("Z", twist) @ rot("Y", lean) @ rot("X", -bow)
    for n, k in (("neck", 1.0), ("neck2", 1.3)):
        h, t = rest[n][1], rest[n][2]
        dirs[n] = (rot("Z", head_yaw * 0.3 * k) @ M @ rot("X", -neck * k) @ (t - h).to_4d()).to_3d()
    hM = rot("Z", head_yaw) @ M @ rot("X", -(neck * 1.3 + head_pitch))
    h, t = rest["head"][1], rest["head"][2]
    dirs["head"] = (hM @ (t - h).to_4d()).to_3d()
    if abs(head_roll) > 1e-4:
        spins["head"] = ((t - h).normalized(), head_roll)
    h, t = rest["jaw"][1], rest["jaw"][2]
    dirs["jaw"] = (hM @ rot("X", -jaw) @ (t - h).to_4d()).to_3d()
    torso = poser.solve(hip, dirs, spins)
    for s, sx in ((1, "R"), (-1, "L")):
        L = leg_joints(s)
        lg = (legs or {}).get(sx, {})
        hp = torso[f"thigh_{sx}"][0]
        ankle = lg.get("ankle", L["ankle"])
        l1, l2 = (L["knee"] - L["hip"]).length, (L["ankle"] - L["knee"]).length
        knee, ankle = W.two_bone(hp, ankle, l1, l2, lg.get("pole", V((s * 0.15, 1, 0))))
        dirs[f"thigh_{sx}"] = knee - hp
        dirs[f"shin_{sx}"] = ankle - knee
        dirs[f"foot_{sx}"] = lg.get("foot", L["ball"] - L["ankle"])
        dirs[f"toe_{sx}"] = lg.get("toe", L["toe"] - L["ball"])
    for s, sx in ((1, "R"), (-1, "L")):
        J = arm_joints(s)
        ag = (arms or {}).get(sx, {})
        sh = torso[f"upper_{sx}"][0]
        if "wrist" in ag:
            wrist = ag["wrist"]
        else:
            # hanging: where the bowed torso carries the rest's wrist
            wrist = sh + (J["wrist"] - J["shoulder"])
        l1, l2 = (J["elbow"] - J["shoulder"]).length, (J["wrist"] - J["elbow"]).length
        elbow, wrist = W.two_bone(sh, wrist, l1, l2, ag.get("pole", V((s * 0.4, -1, -0.2))))
        dirs[f"upper_{sx}"] = elbow - sh
        dirs[f"fore_{sx}"] = wrist - elbow
        dirs[f"hand_{sx}"] = ag.get("hand", wrist - elbow)
        c = ag.get("curl", curl)
        for k in range(5):
            for j in range(1, 4):
                fn = f"f{k}_{j}_{sx}"
                restd = (rest[fn][2] - rest[fn][1]).normalized()
                axis = restd.cross(V((-s, 0, 0))).normalized()
                spins[fn] = (axis, c * (0.3 + 0.25 * j) * (0.6 if k == 4 else 1.0))
    return dirs, spins


def animate(arm_obj, bones):
    sc = bpy.context.scene
    sc.render.fps = 30
    poser = W.Poser(arm_obj, bones)
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "QUATERNION"
    arm_obj.animation_data_create()

    def action(name, keys):
        act = bpy.data.actions.new(name)
        arm_obj.animation_data.action = act
        for frame, kw in keys:
            hip = kw.pop("hip", V((0, 0, 0)))
            dirs, spins = pose(poser, hip=hip, **kw)
            poser.apply(hip, dirs, spins, frame=frame)
        arm_obj.animation_data.action = None
        tr = arm_obj.animation_data.nla_tracks.new()
        tr.name = name
        tr.strips.new(name, int(keys[0][0]), act)
        return act

    Ar, Al = arm_joints(1), arm_joints(-1)
    Lr, Ll = leg_joints(1), leg_joints(-1)

    # ---- idle: a slow breath through the hunch, the weight shifting, the arms hanging and drifting, the fingers working
    keys = []
    for f in range(0, 181, 15):
        ph = f / 180 * math.tau
        b = math.sin(ph)
        sw = math.sin(ph * 2 + 0.7)
        keys.append((f, dict(hip=V((0.008 * math.sin(ph), 0, 0.01 * b)), bow=0.02 * b, lean=0.015 * math.sin(ph), neck=0.03 * math.sin(ph + 1),
                             curl=0.35 + 0.15 * math.sin(ph * 3),
                             legs=dict(R=dict(ankle=Lr["ankle"] + V((0.01, 0.03, 0))), L=dict(ankle=Ll["ankle"] + V((-0.01, -0.02, 0)))),
                             arms=dict(R=dict(wrist=Ar["wrist"] + V((0.01 * sw, 0.03 + 0.02 * sw, 0.01 * b)), curl=0.35 + 0.15 * math.sin(ph * 3 + 1)),
                                       L=dict(wrist=Al["wrist"] + V((-0.01 * sw, 0.03 - 0.02 * sw, 0.01 * b)), curl=0.35 + 0.15 * math.sin(ph * 3 + 2))))))
    action("idle", keys)

    # ---- peek: leaning out from behind a trunk on its own side s, the near hand up on the bark at the trunk's edge,
    # its long fingers curled round it; the head tipped out and toward you
    def peek(s, u):
        sx = "R" if s > 0 else "L"
        J = arm_joints(s)
        grip = D(0.3 * s, 1.68, 0.4)
        w = J["wrist"].lerp(grip, u)
        arms = {sx: dict(wrist=w, hand=(V((0, 1, 0)) * u + V((0, 0, -1)) * (1 - u) + V((-s * 0.25, 0, 0.15)) * u), curl=0.35 + 0.95 * u,
                         pole=V((s, -0.3, -0.6)))}
        return dict(hip=V((s * 0.02 * u, 0, -0.02 * u)), bow=0.05 * u, lean=s * 0.16 * u, twist=-s * 0.1 * u, neck=-0.06 * u,
                    head_roll=s * 0.35 * u, head_yaw=-s * 0.15 * u, arms=arms)
    for s, sx in ((1, "R"), (-1, "L")):
        action(f"peek_{sx}", [(0, peek(s, 0.0)), (12, peek(s, 0.85)), (20, peek(s, 1.0))])

    # ---- duck: from the peek, snatched back behind the trunk, low
    def duck(s, u):
        p = peek(s, 1.0 - u)
        p["hip"] = p["hip"] + V((-s * 0.12 * u, -0.04 * u, -0.12 * u))
        p["lean"] = p["lean"] - s * 0.22 * u
        p["bow"] = p["bow"] + 0.2 * u
        p["legs"] = dict(R=dict(ankle=Lr["ankle"]), L=dict(ankle=Ll["ankle"]))
        return p
    for s, sx in ((1, "R"), (-1, "L")):
        action(f"duck_{sx}", [(0, duck(s, 0.0)), (3, duck(s, 0.6)), (6, duck(s, 1.0))])

    # ---- walk: one cycle (two steps), stalking: low, long and deliberate, the arms swinging loose against the legs, the
    # head held level and forward. The right foot lands at 0, the left at half.
    stride = 0.3                      # each foot's travel either way of its rest: a step of 0.6
    keys = []
    N = 32
    for f in range(N + 1):
        t = f / N
        legs = {}
        for s, sx, L, off in ((1, "R", Lr, 0.0), (-1, "L", Ll, 0.5)):
            u = (t + off) % 1.0
            if u < 0.5:
                y = stride - 2 * stride * (u / 0.5)
                z = 0.0
                fd = L["ball"] - L["ankle"]
            else:
                v = (u - 0.5) / 0.5
                e = v * v * (3 - 2 * v)
                y = -stride + 2 * stride * e
                z = 0.11 * math.sin(math.pi * v)
                fd = (L["ball"] - L["ankle"]) + V((0, 0, -0.06 * math.sin(math.pi * v)))
            legs[sx] = dict(ankle=L["ankle"] + V((0, y, z)), foot=fd, pole=V((s * 0.1, 1, 0)))
        bob = abs(math.cos(t * math.tau))
        swing = math.sin(t * math.tau)
        arms = {"R": dict(wrist=Ar["wrist"] + V((0.0, -0.2 * swing + 0.06, 0.03 * abs(swing))), curl=0.4, pole=V((0.3, -1, -0.4))),
                "L": dict(wrist=Al["wrist"] + V((0.0, 0.2 * swing + 0.06, 0.03 * abs(swing))), curl=0.4, pole=V((-0.3, -1, -0.4)))}
        keys.append((f, dict(hip=V((0.025 * swing, 0.02, -0.07 - 0.03 * bob)), bow=0.14 + 0.02 * bob, lean=-0.03 * swing, twist=0.06 * swing,
                             neck=-0.1, legs=legs, arms=arms)))
    action("walk", keys)

    # ---- shove: a wind-up, both arms flung forward at chest height, the hands spread; the follow-through; back
    def shove(u_in, u_out):
        arms = {}
        for s, sx, J in ((1, "R", Ar), (-1, "L", Al)):
            back = J["wrist"] + V((0, -0.25, 0.25))
            hit = D(0.18 * s, 1.62, 0.78)
            w = back.lerp(hit, u_in).lerp(J["wrist"], u_out)
            arms[sx] = dict(wrist=w, hand=V((0, 1, 0.2)) * (u_in * (1 - u_out)) + V((0, 0, -1)) * (1 - u_in * (1 - u_out)), curl=0.4 - 0.5 * u_in * (1 - u_out),
                            pole=V((s, -0.5, -0.5)))
        k = u_in * (1 - u_out)
        return dict(hip=V((0, 0.12 * k - 0.04 * (1 - u_in), -0.06)), bow=0.18 * k - 0.05 * (1 - u_in) * (1 - u_out), neck=-0.15 * k, jaw=0.25 * k, arms=arms)
    action("shove", [(0, shove(0.0, 0.0)), (5, shove(0.0, 0.0)), (8, shove(1.0, 0.0)), (12, shove(1.0, 0.0)), (24, shove(1.0, 1.0))])

    # ---- loom (the bunker's hallway): drawing up out of its hunch, the arms lifting out wide, the fingers spread, the
    # mouth's slit opening; it holds there
    def loom(u):
        arms = {}
        for s, sx, J in ((1, "R", Ar), (-1, "L", Al)):
            w = J["wrist"].lerp(J["shoulder"] + D(0.38 * s, -0.12, 0.62), u)
            arms[sx] = dict(wrist=w, hand=V((s * 0.15, 0.8, 0.1)) * u + V((0, 0, -1)) * (1 - u), curl=0.35 - 0.5 * u, pole=V((s, -0.4, -0.6)))
        return dict(hip=V((0, 0.02 * u, 0.06 * u)), bow=-0.18 * u, neck=0.18 * u, head_pitch=-0.12 * u, jaw=0.85 * u, arms=arms)
    action("loom", [(0, loom(0.0)), (6, loom(0.75)), (12, loom(1.0))])

    # ---- peek low (the horror pass): folded down at the foot of the trunk, the knees up by its shoulders, the near hand
    # on the bark low, its head round the trunk at a child's height, tipped right over
    def peek_low(s, u):
        sx = "R" if s > 0 else "L"
        J = arm_joints(s)
        grip = D(0.3 * s, 0.85, 0.42)
        arms = {sx: dict(wrist=J["wrist"].lerp(grip, u), hand=V((-s * 0.25, 1, -0.1)).normalized(), curl=0.35 + 0.95 * u, pole=V((s, -0.2, 0.4)))}
        other = "L" if s > 0 else "R"
        Jo = arm_joints(-s)
        arms[other] = dict(wrist=D(-0.25 * s, 0.25, 0.25), hand=V((0, 0.4, -1)).normalized(), curl=0.9, pole=V((-s, -0.5, 0.2)))
        legs = {}
        for t, tx, L in ((1, "R", Lr), (-1, "L", Ll)):
            legs[tx] = dict(ankle=L["ankle"] + V((t * 0.08, -0.05, 0)), pole=V((t * 0.6, 1, 0.3)))
        return dict(hip=V((s * 0.03, -0.06, -0.62)) * u, bow=0.75 * u, lean=s * 0.25 * u, neck=-0.35 * u, head_roll=s * 0.9 * u,
                    head_yaw=-s * 0.2 * u, arms=arms, legs=legs)
    for s, sx in ((1, "R"), (-1, "L")):
        action(f"peek_low_{sx}", [(0, peek_low(s, 0.0)), (14, peek_low(s, 0.85)), (22, peek_low(s, 1.0))])

    # ---- cling (the horror pass): up a trunk, pressed against the far side of it, both arms round it, the hands on
    # either edge, the knees drawn up and the feet braced on the bark; the game lifts it, the hands go onto the bark
    def cling(u):
        arms = {}
        for t, tx, J in ((1, "R", Ar), (-1, "L", Al)):
            arms[tx] = dict(wrist=J["wrist"].lerp(D(0.34 * t, 1.92, 0.42), u), hand=V((-t * 0.4, 1, 0.2)).normalized() * u + V((0, 0, -1)) * (1 - u),
                            curl=0.35 + 0.9 * u, pole=V((t, -0.6, -0.3)))
        legs = {}
        for t, tx, L in ((1, "R", Lr), (-1, "L", Ll)):
            legs[tx] = dict(ankle=L["ankle"].lerp(D(0.24 * t, 0.62, 0.3), u), foot=V((0, 0.2, -1)).normalized() * u + (L["ball"] - L["ankle"]).normalized() * (1 - u),
                            pole=V((t * 0.7, 1, 0)))
        return dict(hip=V((0, -0.08, 0.05)) * u, bow=-0.12 * u, neck=0.1 * u, head_roll=0.5 * u, arms=arms, legs=legs)
    action("cling", [(0, cling(0.0)), (16, cling(1.0))])

    # ---- stare: drawn up straight at the foot of the stairs, dead still, the arms hanging; the head's look is the game's
    def stare(u):
        return dict(hip=V((0, 0, 0.02 * u)), bow=-0.1 * u, neck=0.05 * u, curl=0.25,
                    arms=dict(R=dict(wrist=Ar["wrist"] + V((0, 0.02, 0.02))), L=dict(wrist=Al["wrist"] + V((0, 0.02, 0.02)))))
    action("stare", [(0, stare(0.0)), (15, stare(1.0)), (30, stare(1.0))])

    arm_obj.animation_data.action = bpy.data.actions["idle"]
    sc.frame_set(0)


def preview(name, target, dist, height=1.0, yaw=35.0, textured=False):
    """kit.preview, the shape in a plain grey (the baked albedo is nearly black: the shape is what's being looked at)."""
    sc = bpy.context.scene
    kit.preview(name, target, dist, height, yaw) if textured else None
    if not textured:
        sc.render.engine = "BLENDER_WORKBENCH"
        sh = sc.display.shading
        sh.light = "STUDIO"
        sh.color_type = "SINGLE"
        sh.single_color = (0.55, 0.53, 0.5)
        sh.show_shadows = True
        sh.show_cavity = True
        sh.cavity_type = "BOTH"
        sc.render.resolution_x, sc.render.resolution_y = 900, 900
        cam = bpy.data.objects.get("PreviewCam")
        if cam is None:
            cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
            sc.collection.objects.link(cam)
        t = V(target)
        a = math.radians(yaw)
        cam.location = t + V((math.sin(a) * dist, math.cos(a) * dist, height))
        cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = 40
        sc.camera = cam
        sc.render.filepath = os.path.join(PREV, name + ".png")
        bpy.ops.render.render(write_still=True)


# ------------------------------------------------------------------ the figure

def main():
    clear()
    m = materials()
    body = build_body(m)
    hands = [build_hand(m, s) for s in (1, -1)]
    feet = [build_foot(m, s) for s in (1, -1)]
    head, eyes, mouth = build_head(m)
    crowns = build_crowns(m)
    nails = build_nails(m)
    rags = build_rags(m)

    # the bake's colours (Col) and detail, the game's tones (Tone); the rigid parts' bones
    for ob in [body] + hands + feet:
        W.paint(ob, hide_colour)
        W.detail_attr(ob, 1.0)
        paint4(ob, tone_hide)
        W.tag(ob, "flesh")
    W.paint(head, face_colour)
    W.detail_attr(head, 0.6)
    paint4(head, tone_face)
    W.tag(head, "flesh")
    for e in eyes:
        if e.name.startswith("Pupil"):
            W.paint(e, flat((0.01, 0.008, 0.006)))
            W.detail_attr(e, 0.0)
            paint4(e, lambda co, n: (0.0, 0.05, 0.0, 1.0))
        else:
            W.paint(e, flat((0.9, 0.8, 0.45)))
            W.detail_attr(e, 0.0)
            paint4(e, tone_eye)
        W.tag(e, "head")
    for ob, bone in crowns:
        W.paint(ob, bark_colour)
        W.detail_attr(ob, 0.8)
        paint4(ob, tone_bark)
        W.tag(ob, bone)
    for ob, bone in nails:
        W.paint(ob, flat((0.015, 0.014, 0.013)))
        W.detail_attr(ob, 0.1)
        paint4(ob, tone_bark)
        W.tag(ob, bone)
    for ob, bone, kind in mouth:
        W.paint(ob, flat((0.55, 0.5, 0.4) if kind == "tooth" else (0.09, 0.03, 0.025) if kind == "sinew" else (0.02, 0.006, 0.006)))
        W.detail_attr(ob, 0.2)
        paint4(ob, (lambda co, n: (0.55, 1.0, 0.3, 1.0)) if kind == "tooth" else (lambda co, n: (0.35, 0.45, 0.3, 1.0)) if kind == "sinew" else (lambda co, n: (0.0, 0.12, 0.0, 1.0)))
        if kind == "sinew":
            # stretched between the two: the top ring the face's, the bottom ring the jaw's
            W.tag(ob, "head")
            ob.vertex_groups["P_head"].remove(bone[1])
            ob.vertex_groups.new(name="P_jaw").add(bone[1], 1.0, "REPLACE")
        else:
            W.tag(ob, bone)
    W.paint(rags, lambda co, n: tuple(V((0.03, 0.027, 0.025)) * (0.7 + 0.6 * fbm(co.x * 9, co.y * 9, co.z * 9, 3))))
    W.detail_attr(rags, 0.5)
    paint4(rags, tone_rag)
    rigid = [o for o, _ in crowns] + [o for o, _ in nails] + eyes + [o for o, _, _ in mouth]

    # the high copy for the bake; the export copy decimated
    def copy(o):
        c = o.copy()
        c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        return c
    flesh = [body, head] + hands + feet
    high = join([copy(o) for o in flesh + rigid + [rags]], "High")
    budgets = {body.name: 42000, head.name: 9000}
    for o in flesh:
        target = budgets.get(o.name, 4500)
        ratio = min(1.0, target / max(W.tri_count(o), 1))
        add_mod(o, "DECIMATE", ratio=ratio)
        apply_mods(o)
    low = join(flesh + rigid + [rags], "StalkerMesh")
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0015)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[stalker] low {W.tri_count(low)} triangles, high {W.tri_count(high)}")
    albedo, normal = bake_maps(low, high, TEXSIZE)
    bpy.data.objects.remove(high, do_unlink=True)
    # the exported colour attribute is the game's tones
    me = low.data
    me.color_attributes.remove(me.color_attributes["Col"])
    me.color_attributes.active_color = me.color_attributes["Tone"]
    me.color_attributes.render_color_index = me.color_attributes.active_color_index
    for slot in low.material_slots:
        nt = slot.material.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        bt = nt.nodes.get("BakeTarget")
        if bt:
            nt.nodes.remove(bt)
    W.final_materials(low, albedo, normal)
    bones = bone_list()
    arm_obj = W.make_armature("Stalker", bones)
    skin_weights(low, arm_obj, bones)
    for vg in list(low.vertex_groups):
        if vg.name.startswith("P_"):
            low.vertex_groups.remove(vg)
    for a in ("rag_chain", "rag_u", "detail"):
        if a in me.attributes:
            me.attributes.remove(me.attributes[a])
    animate(arm_obj, bones)
    path = os.path.join(OUT, "stalker.glb")
    bpy.ops.object.select_all(action="DESELECT")
    arm_obj.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=False,
                              export_animations=True, export_skins=True, export_animation_mode="ACTIONS", export_force_sampling=True,
                              export_image_format="AUTO", export_materials="EXPORT", export_colors=True)
    print(f"[stalker] exported {path}: {W.tri_count(low)} triangles, {len(bones)} bones")
    # previews (the bake's colours are gone from the mesh now; the workbench shows the textures)
    for name, act, frame, cam in (("stalker_front", "idle", 0, ((0, 0, 1.3), 4.2, 0.2, 10)), ("stalker_side", "idle", 0, ((0, 0, 1.3), 4.2, 0.2, 90)),
                                  ("stalker_back", "idle", 0, ((0, 0, 1.3), 4.2, 0.4, 200)), ("stalker_face", "idle", 0, (tuple(HC), 0.75, 0.0, 15)),
                                  ("stalker_peek", "peek_R", 20, ((0, 0, 1.4), 3.6, 0.2, 20)), ("stalker_walk", "walk", 8, ((0, 0, 1.2), 4.2, 0.2, 80)),
                                  ("stalker_shove", "shove", 10, ((0, 0, 1.3), 4.2, 0.2, 50)), ("stalker_loom", "loom", 12, ((0, 0, 1.4), 4.0, 0.2, 15)),
                                  ("stalker_hand", "idle", 0, (tuple(arm_joints(1)["palm"]), 0.7, 0.0, 70)),
                                  ("stalker_low", "peek_low_R", 22, ((0, 0, 0.8), 3.4, 0.3, 20)), ("stalker_cling", "cling", 16, ((0, 0, 1.3), 3.6, 0.2, 200)),
                                  ("stalker_gape", "loom", 12, (tuple(HC), 0.7, -0.05, 10)),
                                  ("stalker_mouth", "idle", 0, (tuple(head_local(V((0, 0.06, MOUTH_Z)))), 0.3, 0.0, 0)),
                                  ("stalker_mouth_open", "loom", 12, (tuple(head_local(V((0, 0.06, MOUTH_Z)))), 0.35, -0.02, 0)),
                                  ("stalker_face_side", "idle", 0, (tuple(HC), 0.75, 0.0, 55))):
        arm_obj.animation_data.action = bpy.data.actions[act]
        bpy.context.scene.frame_set(frame)
        preview(name, *cam)
    arm_obj.animation_data.action = bpy.data.actions["idle"]
    bpy.context.scene.frame_set(0)
    preview("stalker_face_tex", tuple(HC), 0.75, 0.0, 15, textured=True)


if __name__ == "__main__":
    main()
