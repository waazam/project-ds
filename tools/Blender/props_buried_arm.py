"""Somebody inside the plowed wall (see winter_props.py; the owner, 2026-09-30: the man laid out on the road, put in
the wall instead with just his arm sticking out, and the arm real, not blocky). A parka's puffy sleeve breaks out of
the snow at chest height, bent at the elbow, reaching up and out toward the road; the bare hand at the end of it open
and clawed, frostbitten, the fingertips gone dark, the nails dark, frost crusted along the top of it all; the snow round
where it comes out broken.

The origin is the wall's foot on the road, the wall's face rising from there (into -Y), the road +Y."""
from kit import *


def puffy_sleeve(name, pts, r0, r1, material, puffs=6):
    """A down sleeve along points: quilted, swelling between its stitched rings."""
    dense = []
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        for k in range(8):
            dense.append(a.lerp(b, k / 8))
    dense.append(Vector(pts[-1]))
    n = len(dense)
    radii = []
    for i in range(n):
        t = i / (n - 1)
        radii.append((r0 + (r1 - r0) * t) * (0.86 + 0.14 * abs(math.sin(t * puffs * math.pi))))
    return skin_tree(name, dense, [(i - 1, i) for i in range(1, n)], radii, material, subdiv=2)


def orient(ob, x, y, z):
    """Turns an object (about its own origin) so its local axes lie along x, y, z."""
    ob.rotation_mode = "QUATERNION"
    ob.rotation_quaternion = Matrix((x, y, z)).transposed().to_quaternion()


def build():
    clear()
    rnd = random.Random(880)
    parka = mat("parka", (0.36, 0.07, 0.06), 0.85)
    cuff = mat("parka_cuff", (0.12, 0.11, 0.11), 0.9)
    skin = mat("dead_skin", (0.56, 0.53, 0.55), 0.6)
    bite = mat("frostbite", (0.3, 0.22, 0.32), 0.5)
    nail = mat("nail", (0.16, 0.14, 0.18), 0.3)
    snow = snow_mat()
    parts = []
    exit_z = 1.2
    # ---- the arm: from deep in the wall out to the elbow, then up and out to the wrist
    shoulder = Vector((0.05, -0.75, exit_z + 0.1))
    elbow = Vector((0.0, 0.26, exit_z - 0.06))
    wrist = Vector((-0.03, 0.5, exit_z + 0.4))
    upper = puffy_sleeve("Upper", [shoulder, Vector((0.03, -0.25, exit_z + 0.04)), elbow], 0.085, 0.078, parka, puffs=5)
    cuff_at = elbow.lerp(wrist, 0.74)
    fore = puffy_sleeve("Fore", [elbow, elbow.lerp(wrist, 0.4), cuff_at], 0.078, 0.066, parka, puffs=3)
    sleeve = join([upper, fore], "Sleeve")
    parts += [sleeve, snow_on_top(sleeve, "Frost", snow, thick=0.022, min_up=0.5, seed=11)]
    along = (wrist - elbow).normalized()
    parts.append(cylinder("Cuff", cuff_at - along * 0.01, cuff_at + along * 0.045, 0.052, 0.046, 16, cuff))
    # ---- the hand (a man's, a little large: it's what they're to look at), its frame: out along F, across W, its back D
    S = 1.25
    F = Vector((0.0, 0.62, 0.78)).normalized()
    W = Vector((1, 0, 0))
    D = W.cross(F).normalized()
    palm_dir = -D
    wr = cuff_at + along * 0.03 + F * 0.03 * S
    verts, edges, radii = [], [], []

    def add(p, r, parent=None):
        verts.append(Vector(p))
        radii.append(r)
        i = len(verts) - 1
        if parent is not None:
            edges.append((parent, i))
        return i

    # the wrist, coming out of the cuff
    root = add(cuff_at - along * 0.02, 0.031 * S)
    w = add(wr, 0.026 * S, root)
    distal = []
    # four fingers: (across, the bones' lengths (metacarpal to the knuckle, then three), spread, the curl at each joint)
    fingers = [(-0.027, (0.07, 0.044, 0.027, 0.022), -0.16, (0.3, 0.55, 0.4)),
               (-0.009, (0.072, 0.05, 0.031, 0.024), -0.05, (0.25, 0.5, 0.35)),
               (0.01, (0.069, 0.047, 0.029, 0.023), 0.07, (0.35, 0.6, 0.4)),
               (0.027, (0.063, 0.036, 0.022, 0.02), 0.2, (0.5, 0.7, 0.45))]
    for k, (off, bones, spread, curl) in enumerate(fingers):
        dirv = (F * math.cos(spread) + W * math.sin(spread)).normalized()
        knuckle = wr + (W * off * 1.1 + dirv * bones[0]) * S + D * 0.005 * S
        cur = add(knuckle, 0.0118 * S, w)
        p, d = knuckle, dirv
        for j in range(3):
            d = (d * math.cos(curl[j]) + palm_dir * math.sin(curl[j])).normalized()
            q = p + d * bones[j + 1] * S
            r = (0.0102, 0.0092, 0.0079)[j] * S * (0.88 if k == 3 else 1.0)
            if j < 2:
                # a little swelling at each joint, then the bone
                cur = add(p.lerp(q, 0.5), r * 0.92, cur)
                cur = add(q, r, cur)
            else:
                distal.append((p.copy(), q.copy(), d.copy(), r))
            p = q
    # the thumb: off the side of the palm by the wrist, reaching forward and across, clawed in
    tb = wr + (W * -0.03 + F * 0.015) * S + palm_dir * 0.01 * S
    t0 = add(tb, 0.015 * S, w)
    td = (F * 0.75 - W * 0.45 + palm_dir * 0.25).normalized()
    t1 = add(tb + td * 0.048 * S, 0.0125 * S, t0)
    td2 = (td * 0.7 + F * 0.3 + palm_dir * 0.35).normalized()
    t2 = add(verts[t1] + td2 * 0.034 * S, 0.0112 * S, t1)
    td3 = (td2 + palm_dir * 0.6).normalized()
    distal.append((verts[t2].copy(), verts[t2] + td3 * 0.03 * S, td3, 0.0098 * S))
    parts.append(skin_tree("Hand", verts, edges, radii, skin, subdiv=2))
    # the palm's body, over the metacarpals (the skin's spokes alone left it a fan)
    palm = lumpy_sphere("Palm", (0, 0, 0), (0.044 * S, 0.05 * S, 0.017 * S), skin, 31, amp=0.04, subdiv=3)
    palm.location = wr + F * 0.045 * S
    orient(palm, W, F, D)
    parts.append(palm)
    # the last bones, black-purple with frostbite, and their nails
    for k, (a, b, d, r) in enumerate(distal):
        # (begun back inside the finger: the skins' rounded ends had left a gap between them)
        a0 = a - d * (b - a).length * 0.45
        parts.append(skin_tree(f"Tip{k}", [a0, a, a.lerp(b, 0.55), b], [(0, 1), (1, 2), (2, 3)], [r * 0.98, r * 1.0, r * 0.95, r * 0.72], bite, subdiv=2))
        side = d.cross(D).normalized()
        up = side.cross(d).normalized()
        if up.dot(D) < 0:
            up = -up
        nl = box(f"Nail{k}", (0, 0, 0), (r * 1.5, (b - a).length * 0.5, 0.0035), nail, bevel=0.0015, segs=1)
        nl.location = a.lerp(b, 0.62) + up * r * 0.62
        orient(nl, side, d, up)
        parts.append(nl)
    # ---- the wall broken round where it comes out: a few flat chunks round the hole, and some fallen at the foot
    for i in range(7):
        a = i / 7 * math.tau + 0.3
        c = Vector((math.cos(a) * 0.15, -0.04, exit_z + math.sin(a) * 0.13))
        ch = lumpy_sphere(f"Broken{i}", (0, 0, 0), (rnd.uniform(0.045, 0.07), 0.03, rnd.uniform(0.035, 0.055)), snow, 50 + i, amp=0.3, subdiv=2)
        ch.location = c
        parts.append(ch)
    for i in range(5):
        c = Vector((rnd.uniform(-0.45, 0.45), rnd.uniform(0.05, 0.4), 0.0))
        parts.append(lumpy_sphere(f"Fallen{i}", c, (rnd.uniform(0.05, 0.1), rnd.uniform(0.04, 0.08), 0.045), snow, 60 + i, amp=0.3, subdiv=2, flat_bottom=0.0))
    for o in parts:
        if o.type == "MESH" and len(o.data.uv_layers) == 0:
            uv_box(o, 4.0)
    ob = join(parts, "BuriedArm")
    export([ob], "buried_arm")
    preview("buried_arm", (0, 0.2, 1.3), 1.9, 0.35, 35)
    preview("buried_arm_hand", wr + F * 0.08, 0.45, 0.12, 60)
