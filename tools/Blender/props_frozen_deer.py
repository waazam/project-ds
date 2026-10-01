"""The frozen deer (see winter_props.py): a young buck frozen where it stood at the road's edge, mid-step, one foreleg
lifted; frost crusted along its back, icicles off its belly and chin, its eyes gone white. It doesn't bolt.
Its body along X (head +X), its side to the road (+Y), its hooves on z = 0."""
from kit import *
from props_antler_tree import antler


def fur_texture():
    """A deer's coat: warm grey-brown, darker along the back's line, flecked (64 px, on box-projected UVs)."""
    def fn(x, y):
        n = fbm(x * 18, y * 18, 0.5, 3)
        c = (0.42 + 0.06 * n, 0.32 + 0.05 * n, 0.23 + 0.03 * n)
        return c
    return make_texture("deer_fur", 64, fn)


def build():
    clear()
    rnd = random.Random(1090)
    fur = mat("deer_fur", (1, 1, 1), 0.9, tex=fur_texture())
    pale = mat("deer_pale", (0.78, 0.74, 0.68), 0.9)
    hoof = mat("hoof", (0.08, 0.07, 0.06), 0.5)
    dark = mat("deer_nose", (0.05, 0.045, 0.045), 0.3)
    eye = mat("frozen_eye", (0.82, 0.86, 0.9), 0.15)
    bone = mat("bone", (0.74, 0.69, 0.58), 0.6)
    ice = mat("ice", (0.62, 0.74, 0.88), 0.08)
    snow = snow_mat()
    verts, edges, radii = [], [], []

    def add(p, r, parent=None):
        verts.append(Vector(p))
        radii.append(r)
        i = len(verts) - 1
        if parent is not None:
            edges.append((parent, i))
        return i

    # the spine, the neck, the head
    hips = add((-0.42, 0, 0.98), 0.165)
    mid = add((-0.1, 0, 1.0), 0.19, hips)
    chest = add((0.22, 0, 0.97), 0.205, mid)
    withers = add((0.42, 0, 1.0), 0.165, chest)
    n1 = add((0.55, 0, 1.18), 0.105, withers)
    n2 = add((0.66, 0, 1.37), 0.083, n1)
    n3 = add((0.72, 0, 1.49), 0.074, n2)
    head = add((0.83, 0, 1.5), 0.078, n3)
    face = add((0.96, 0, 1.44), 0.054, head)
    muzzle = add((1.06, 0, 1.39), 0.038, face)
    t1 = add((-0.57, 0, 1.0), 0.05, hips)
    add((-0.64, 0, 0.9), 0.034, t1)

    def leg(parent, pts, rs):
        cur = parent
        for p, r in zip(pts, rs):
            cur = add(p, r, cur)
        return cur

    hooves = []
    # the forelegs: the far one planted, the near one (the road's side) lifted mid-step
    hooves.append(leg(withers, [(0.38, -0.12, 0.84), (0.36, -0.11, 0.62), (0.38, -0.1, 0.36), (0.39, -0.1, 0.11), (0.41, -0.1, 0.03)], [0.08, 0.05, 0.034, 0.027, 0.03]))
    hooves.append(leg(withers, [(0.4, 0.12, 0.84), (0.43, 0.11, 0.64), (0.56, 0.1, 0.5), (0.53, 0.1, 0.33), (0.51, 0.1, 0.27)], [0.08, 0.05, 0.034, 0.027, 0.03]))
    # the hind legs: the long thigh, the hock bent back, the cannon down to the hoof
    for y in (-0.13, 0.13):
        sgn = 1 if y > 0 else -1
        hooves.append(leg(hips, [(-0.38, y, 0.86), (-0.28, y * 0.95, 0.62), (-0.5, y * 0.8, 0.42), (-0.47, y * 0.78, 0.12), (-0.45, y * 0.78, 0.03)], [0.1, 0.065, 0.038, 0.027, 0.03]))
    body = skin_tree("Deer", verts, edges, radii, fur, subdiv=2)
    # (a deer's deep and narrow, not round)
    body.scale = (1, 0.82, 1)
    uv_box(body, 2.0)
    parts = [body]
    # the hooves, black; the pale rump, throat and tail's underside
    for hi in hooves:
        p = verts[hi]
        parts.append(lumpy_sphere(f"Hoof{hi}", (p.x + 0.015, p.y * 0.82, p.z), (0.035, 0.028, 0.03), hoof, hi, amp=0.05, subdiv=2))
    parts.append(lumpy_sphere("Rump", (-0.56, 0, 0.94), (0.06, 0.1, 0.11), pale, 3, amp=0.1, subdiv=3))
    parts.append(lumpy_sphere("Throat", (0.67, 0, 1.3), (0.05, 0.05, 0.06), pale, 4, amp=0.1, subdiv=3))
    parts.append(lumpy_sphere("Belly", (0.0, 0, 0.84), (0.32, 0.11, 0.06), pale, 5, amp=0.08, subdiv=3))
    # the face: the black nose, the eyes gone white, the ears up and out
    parts.append(lumpy_sphere("Nose", (1.09, 0, 1.38), (0.025, 0.03, 0.022), dark, 6, amp=0.05, subdiv=2))
    for s in (-1, 1):
        parts.append(lumpy_sphere(f"Eye{s}", (0.89, s * 0.058, 1.5), (0.018, 0.012, 0.016), eye, 7 + s, amp=0.02, subdiv=2))
        ear = lumpy_sphere(f"Ear{s}", (0, 0, 0), (0.028, 0.012, 0.075), fur, 9 + s, amp=0.05, subdiv=2)
        ear.location = (0.79, s * 0.075, 1.6)
        ear.rotation_euler = (s * math.radians(-40), math.radians(-15), 0)
        parts.append(ear)
        parts.append(antler(f"Antler{s}", Vector((0.82, s * 0.035, 1.57)), (-0.25, s * 0.3, 1), (0.1, s, 0.2), 0.62, rnd, bone))
    # frost crusted along its back and its head
    for o in list(parts):
        if o.name == "Deer":
            frost = snow_on_top(o, "Frost", snow, thick=0.022, min_up=0.55, seed=13)
            parts.append(frost)
    # icicles off its belly and its chin
    for i in range(16):
        x = rnd.uniform(-0.35, 0.35)
        top = Vector((x, rnd.uniform(-0.08, 0.08), 0.8 - 0.02 * abs(x)))
        parts.append(cylinder(f"Icicle{i}", top, top - Vector((0, 0, rnd.uniform(0.06, 0.24))), 0.013, 0.001, 5, ice))
    for i in range(4):
        top = Vector((1.0 + rnd.uniform(-0.04, 0.04), rnd.uniform(-0.02, 0.02), 1.35))
        parts.append(cylinder(f"ChinIcicle{i}", top, top - Vector((0, 0, rnd.uniform(0.04, 0.1))), 0.008, 0.001, 5, ice))
    for o in parts:
        if o.type == "MESH" and len(o.data.uv_layers) == 0:
            uv_box(o, 3.0)
    ob = join(parts, "FrozenDeer")
    export([ob], "frozen_deer")
    preview("frozen_deer", (0.3, 0, 0.9), 3.4, 0.4, 20)
    preview("frozen_deer_head", (0.9, 0, 1.45), 1.2, 0.1, 50)
