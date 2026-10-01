"""The ski gear at the end of the ski tracks (see winter_props.py): two poles planted crossed in an X, and one ski
stuck upright in the snow beside them. Nobody. The origin is where the tracks stop; the tracks came from -Y (the road)."""
from kit import *


def pole(name, base, top, rnd):
    """A ski pole: the aluminium shaft, the hard tip, the basket near the bottom, the rubber grip and its wrist strap."""
    alu = mat("pole_alu", (0.55, 0.57, 0.6), 0.35, 0.8)
    rubber = mat("pole_grip", (0.08, 0.08, 0.09), 0.8)
    strap = mat("pole_strap", (0.4, 0.06, 0.05), 0.85)
    base, top = Vector(base), Vector(top)
    d = (top - base).normalized()
    parts = [cylinder(name + "Shaft", base, top - d * 0.16, 0.0095, 0.0095, 8, alu),
             cylinder(name + "Tip", base - d * 0.05, base, 0.004, 0.009, 8, alu),
             cylinder(name + "Grip", top - d * 0.17, top, 0.017, 0.019, 10, rubber)]
    for i in range(5):
        c = top - d * (0.04 + i * 0.03)
        parts.append(cylinder(name + f"Ridge{i}", c - d * 0.006, c + d * 0.006, 0.021, 0.021, 10, rubber))
    # the basket: a ring and its spokes, 9 cm up from the tip
    bc = base + d * 0.09
    side = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
    side2 = d.cross(side).normalized()
    ring = []
    for i in range(12):
        a0, a1 = i / 12 * math.tau, (i + 1) / 12 * math.tau
        p0 = bc + (side * math.cos(a0) + side2 * math.sin(a0)) * 0.05
        p1 = bc + (side * math.cos(a1) + side2 * math.sin(a1)) * 0.05
        ring.append(cylinder(name + f"Ring{i}", p0, p1, 0.004, 0.004, 5, rubber, caps=False))
    for i in range(4):
        a = i / 4 * math.tau
        ring.append(cylinder(name + f"Spoke{i}", bc, bc + (side * math.cos(a) + side2 * math.sin(a)) * 0.05, 0.003, 0.003, 4, rubber))
    parts += ring
    # the wrist strap: a loop hanging off the grip's top
    loop = []
    for i in range(10):
        a0, a1 = i / 10 * math.pi * 2, (i + 1) / 10 * math.pi * 2
        def L(a):
            return top - d * 0.01 + side * math.sin(a) * 0.04 + (-d * (1 - math.cos(a)) * 0.07)
        loop.append(cylinder(name + f"Strap{i}", L(a0), L(a1), 0.006, 0.006, 4, strap, caps=False))
    parts += loop
    return join(parts, name)


def build():
    clear()
    rnd = random.Random(700)
    snow = snow_mat()
    parts = []
    # the poles, planted crossed: their tips in the snow, their grips leaning apart
    parts.append(pole("PoleA", (-0.32, 0.05, -0.15), (0.42, -0.05, 1.12), rnd))
    parts.append(pole("PoleB", (0.34, -0.02, -0.15), (-0.4, 0.06, 1.1), rnd))
    # one ski stuck upright, its tip curled over, its binding open
    ski_mat = mat("ski", (0.12, 0.2, 0.4), 0.4)
    edge = mat("ski_edge", (0.5, 0.52, 0.55), 0.3, 0.9)
    bind = mat("binding", (0.08, 0.08, 0.09), 0.5)
    bm = bmesh.new()
    n = 24
    prof = []
    for i in range(n + 1):
        t = i / n
        z = -0.25 + t * 1.7
        y = 0.0
        if t > 0.86:   # the tip curling back
            u = (t - 0.86) / 0.14
            y = -math.sin(u * 1.4) * 0.12
            z = -0.25 + 0.86 * 1.7 + math.sin(u * 1.2) * 0.2
        w = 0.042 + 0.01 * math.cos(t * math.pi * 2) * (0.3 if t < 0.86 else 0.0)
        prof.append((z, y, w))
    rows = []
    for (z, y, w) in prof:
        rows.append([bm.verts.new((-w, y, z)), bm.verts.new((w, y, z))])
    for i in range(n):
        bm.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
    ski = obj_from_bmesh(bm, "Ski", ski_mat)
    add_mod(ski, "SOLIDIFY", thickness=0.016)
    apply_mods(ski)
    ski.location = (0.75, 0.25, 0)
    ski.rotation_euler = (math.radians(-8), math.radians(6), math.radians(20))
    parts.append(ski)
    parts.append(box("Binding", (0.75, 0.25, 0.75), (0.07, 0.05, 0.12), bind, bevel=0.01, segs=1, rot=(math.radians(-8), math.radians(6), math.radians(20))))
    parts.append(box("Heel", (0.76, 0.22, 0.42), (0.065, 0.045, 0.09), bind, bevel=0.01, segs=1, rot=(math.radians(-8), math.radians(6), math.radians(20))))
    # the snow kicked up round where they went in
    for k, (x, y) in enumerate([(-0.32, 0.05), (0.34, -0.02), (0.73, 0.28)]):
        parts.append(lumpy_sphere(f"Kick{k}", (x, y, 0.0), (0.16, 0.14, 0.06), snow, 90 + k, amp=0.25, subdiv=2, flat_bottom=-0.02))
    for o in parts:
        if o.type == "MESH" and len(o.data.uv_layers) == 0:
            uv_box(o, 2.0)
    ob = join(parts, "SkiGear")
    export([ob], "ski_gear")
    preview("ski_gear", (0.2, 0, 0.6), 2.6, 0.5, 25)
