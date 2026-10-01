"""The snowman (see winter_props.py)."""
from kit import *


# ------------------------------------------------------------------ the snowman


def build():
    """Three packed snowballs, lumpy and hand-rolled, settled on a skirt of trodden snow; stick arms up and out, one hand
    a twig claw with too many fingers; a red scarf frozen stiff round its neck (somebody's); its head turned back up the
    road the way the player came, seven coal eyes down one side of its face and a grin of real teeth."""
    clear()
    rnd = random.Random(150)
    snow = snow_mat()
    parts = []
    parts.append(lumpy_sphere("Base", (0, 0, 0.42), (0.62, 0.6, 0.5), snow, 1, amp=0.08, freq=3.0, subdiv=4, flat_bottom=0.02))
    parts.append(lumpy_sphere("Body", (0.02, 0, 1.13), (0.45, 0.43, 0.4), snow, 2, amp=0.08, freq=3.0, subdiv=4))
    parts.append(lumpy_sphere("Head", (0.01, 0.02, 1.71), (0.3, 0.29, 0.29), snow, 3, amp=0.06, freq=3.0, subdiv=4))
    # the skirt of snow packed round its foot, and the snow scooped off the ground to build it
    parts.append(lumpy_sphere("Skirt", (0, 0, 0.0), (0.95, 0.9, 0.18), snow, 4, amp=0.16, freq=3.5, subdiv=4, flat_bottom=-0.02))
    for c in range(5):
        a = c * 1.3 + 0.4
        parts.append(lumpy_sphere(f"Chunk{c}", (math.cos(a) * 0.85, math.sin(a) * 0.8, 0.08), (0.14, 0.12, 0.09), snow, 40 + c, amp=0.25, freq=3.0, subdiv=2))
    body = join(parts, "Snowman")
    uv_box(body, 1.2)
    # the face, turned: looking back up the road (its front +Y, the road; the head turned 150 degrees round toward -X)
    face = Matrix.Translation((0.01, 0.02, 1.71)) @ Matrix.Rotation(math.radians(140), 4, "Z")

    def F(x, y, z):
        return face @ Vector((x, y, z))

    coal = mat("coal", (0.03, 0.03, 0.035), 0.35)
    bits = []
    for i in range(7):
        p = F(-0.1 + (i % 2) * 0.05, 0.27 - abs(i - 3) * 0.006, 0.15 - i * 0.045)
        bits.append(lumpy_sphere(f"Eye{i}", p, (0.024, 0.02, 0.022), coal, 10 + i, amp=0.2, subdiv=2))
    bits.append(lumpy_sphere("EyeR", F(0.11, 0.27, 0.11), (0.027, 0.022, 0.025), coal, 30, amp=0.2, subdiv=2))
    tooth = mat("tooth", (0.78, 0.7, 0.52), 0.35)
    for i in range(12):
        u = (i - 5.5) / 5.5
        p = F(u * 0.15, 0.285 - u * u * 0.03, -0.09 + u * u * 0.035)
        t = box(f"Tooth{i}", p, (0.022, 0.016, 0.034 + (i % 3) * 0.008), tooth, bevel=0.005, segs=1, rot=(0, 0, math.radians(140)))
        bits.append(t)
    coals = join(bits, "Face")
    # stick arms (and a hand of twigs on one): skinned branches
    bark = mat("stick", (0.55, 0.47, 0.42), 0.9, tex=BARK)
    verts, edges, radii = [], [], []

    def stick(points, r0, r1):
        base = len(verts)
        for k, p in enumerate(points):
            verts.append(Vector(p))
            radii.append(r0 + (r1 - r0) * k / (len(points) - 1))
            if k:
                edges.append((base + k - 1, base + k))
        return base + len(points) - 1

    stick([(-0.34, 0.0, 1.22), (-0.6, 0.05, 1.45), (-0.85, 0.02, 1.78), (-0.95, -0.04, 1.98)], 0.028, 0.012)
    tip = stick([(0.34, 0.02, 1.2), (0.62, -0.02, 1.48), (0.86, -0.08, 1.86)], 0.03, 0.016)
    for f in range(6):
        a = -0.75 + f * 0.3
        d = Vector((math.sin(a) * 0.5 + 0.25, -0.1, math.cos(a)))
        p0 = verts[tip]
        mid = p0 + d * 0.11 + Vector((0, rnd.uniform(-0.03, 0.03), 0))
        end = mid + (d + Vector((rnd.uniform(-0.2, 0.2), 0, -0.25))).normalized() * 0.1
        base = len(verts)
        verts.extend([mid, end])
        radii.extend([0.008, 0.004])
        edges.extend([(tip, base), (base, base + 1)])
    arms = skin_tree("Arms", verts, edges, radii, bark, subdiv=1)
    uv_box(arms, 3.0)
    # the scarf: a thick wool band round the neck, two tails hanging stiff with frost, frayed ends
    wool = mat("scarf", (0.42, 0.06, 0.05), 0.95)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=20, radius1=0.33, radius2=0.29, depth=0.13)
    for v in bm.verts:
        v.co.z += 1.45 + 0.02 * math.sin(math.atan2(v.co.y, v.co.x) * 3)
    sc = obj_from_bmesh(bm, "ScarfRing", wool)
    add_mod(sc, "SOLIDIFY", thickness=0.035, offset=0.0)
    apply_mods(sc)
    tails = []
    for k, (x, ang) in enumerate([(0.12, 0.08), (0.24, -0.14)]):
        # hanging down its front over the body's curve, stiff with frost
        t = box(f"Tail{k}", (x, 0.43, 1.2), (0.11, 0.025, 0.5), wool, bevel=0.01, segs=1, rot=(0.35, ang, 0.1 * k))
        tails.append(t)
        for f in range(4):
            tails.append(cylinder(f"Fringe{k}{f}", (x - 0.04 + f * 0.027, 0.5, 0.975), (x - 0.045 + f * 0.027, 0.53, 0.89 - 0.02 * (f % 2)), 0.007, 0.005, 5, wool))
    scarf = join([sc] + tails, "Scarf")
    objs = [body, coals, arms, scarf]
    export(objs, "snowman")
    preview("snowman", (0, 0, 1.0), 3.4, 0.5, 20)
    preview("snowman_face", (0, 0, 1.6), 1.5, 0.15, -120)


