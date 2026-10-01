"""The antler tree (see winter_props.py): a dead tree by the road, hung with antlers and jawbones on twine; a deer's
skull nailed to its trunk. Its base at the origin."""
from kit import *


def bone_mat():
    return mat("bone", (0.74, 0.69, 0.58), 0.6)


def antler(name, root, up, out, scale, rnd, material):
    """A deer's antler: a burr at the root, a main beam sweeping up, out and forward, its tines off the top of it."""
    up, out = Vector(up).normalized(), Vector(out).normalized()
    fwd = up.cross(out).normalized()
    verts, edges, radii = [Vector(root)], [], [0.032 * scale]
    prev = 0
    beam = []
    for i in range(1, 7):
        t = i / 6
        p = Vector(root) + (up * (t * 0.55) + out * (math.sin(t * 1.6) * 0.32) + fwd * (math.sin(t * 2.4) * 0.12 - t * t * 0.05)) * scale
        verts.append(p)
        radii.append((0.03 - t * 0.02) * scale)
        edges.append((prev, len(verts) - 1))
        prev = len(verts) - 1
        beam.append(prev)
    # the tines: off the top of the beam, pointing up and a little forward
    for k, bi in enumerate(beam[1:5]):
        base = verts[bi]
        tip = base + (up * rnd.uniform(0.16, 0.26) + fwd * rnd.uniform(0.02, 0.1) - out * 0.03) * scale
        mid = base.lerp(tip, 0.5) + fwd * 0.02 * scale
        verts += [mid, tip]
        radii += [0.012 * scale, 0.004 * scale]
        edges += [(bi, len(verts) - 2), (len(verts) - 2, len(verts) - 1)]
    # the brow tine, low and forward
    b = verts[1]
    verts += [b + (fwd * 0.12 + up * 0.04) * scale, b + (fwd * 0.2 + up * 0.1) * scale]
    radii += [0.012 * scale, 0.004 * scale]
    edges += [(1, len(verts) - 2), (len(verts) - 2, len(verts) - 1)]
    ob = skin_tree(name, verts, edges, radii, material, subdiv=1)
    # the burr: a rough ring at its root
    burr = lumpy_sphere(name + "Burr", root, (0.045 * scale, 0.045 * scale, 0.02 * scale), material, rnd.randint(0, 999), amp=0.25, subdiv=2)
    return join([ob, burr], name)


def jawbone(name, at, yaw, material, tooth):
    """A deer's lower jaw: a long thin curve of bone, the row of grinding teeth along it."""
    rot = Matrix.Rotation(yaw, 4, "Z")
    verts, edges, radii = [], [], []
    for i in range(7):
        t = i / 6
        verts.append(Vector(at) + rot @ Vector((t * 0.24, 0, -math.sin(t * 2.6) * 0.03 - t * 0.05)))
        radii.append((0.012, 0.022 - t * 0.008))
        if i:
            edges.append((i - 1, i))
    # the back of the jaw rising
    verts.append(Vector(at) + rot @ Vector((-0.02, 0, 0.07)))
    radii.append((0.01, 0.025))
    edges.append((0, len(verts) - 1))
    ob = skin_tree(name, verts, edges, radii, material, subdiv=1)
    parts = [ob]
    for i in range(5):
        p = Vector(at) + rot @ Vector((0.05 + i * 0.03, 0, 0.02 - i * 0.008))
        parts.append(box(f"{name}T{i}", p, (0.022, 0.016, 0.018), tooth, bevel=0.003, segs=1, rot=(0, 0, yaw)))
    return join(parts, name)


def build():
    clear()
    rnd = random.Random(481)
    bark = mat("antler_tree_bark", (0.62, 0.58, 0.56), 0.95, tex=BARK_GREY)
    bone = bone_mat()
    tooth = mat("bone_tooth", (0.62, 0.55, 0.4), 0.4)
    twine = mat("twine", (0.22, 0.17, 0.12), 1.0)
    snow = snow_mat()
    # ---- the tree: a dead trunk, leaning a little, its limbs broken short, its twigs gone
    verts, edges, radii = [], [], []
    hangs = []

    def add(p, r, parent=None):
        verts.append(Vector(p))
        radii.append(r)
        i = len(verts) - 1
        if parent is not None:
            edges.append((parent, i))
        return i

    lean = Vector((0.06, 0.03, 1)).normalized()
    prev = add((0, 0, -0.3), 0.36)
    trunk = []
    for k in range(1, 9):
        z = k * 0.95
        p = lean * z + Vector((math.sin(k * 1.3) * 0.08, math.cos(k * 0.9) * 0.07, 0))
        prev = add(p, max(0.3 - k * 0.032, 0.05), prev)
        trunk.append(prev)

    def limb(parent, d, length, r, depth):
        p0 = verts[parent]
        cur = parent
        steps = 3 if depth >= 2 else 2
        for s in range(1, steps + 1):
            d = (d + Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), rnd.uniform(-0.15, 0.1)))).normalized()
            cur = add(verts[cur] + d * length / steps, r * (1 - 0.45 * s / steps), cur)
            # somewhere to hang a thing from: a near-level stretch of limb, at a height to be seen
            if depth <= 2 and abs(d.z) < 0.6 and 3.0 < verts[cur].z < 7.5:
                hangs.append(cur)
        if depth > 0:
            for _ in range(2):
                a = rnd.uniform(0, math.tau)
                nd = (d + Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.5, 0.9) + Vector((0, 0, rnd.uniform(0.0, 0.35)))).normalized()
                limb(cur, nd, length * rnd.uniform(0.5, 0.7), r * 0.55, depth - 1)

    for k, ti in enumerate(trunk[2:]):
        a = k * 2.4 + 0.3
        d = Vector((math.cos(a), math.sin(a), rnd.uniform(0.25, 0.7))).normalized()
        limb(ti, d, rnd.uniform(1.8, 3.0) * (1 - k * 0.08), radii[ti] * 0.7, 3 if k < 4 else 2)
    tree = skin_tree("Tree", verts, edges, radii, bark, subdiv=1)
    add_mod(tree, "DECIMATE", ratio=0.4)   # (113k triangles whole: most of them in the skin's smooth limbs)
    apply_mods(tree)
    uv_box(tree, 1.4)
    lying = snow_on_top(tree, "TreeSnow", snow, thick=0.035, min_up=0.6, seed=3)
    uv_box(lying, 1.0)
    parts = [tree, lying]
    # ---- the skull nailed to the trunk, facing the road (+Y), antlers still on it
    sk_at = lean * 2.6 + Vector((0, 0.3, 0))
    # a deer's skull: the braincase, the long snout tapering forward and down to the nose, the eye sockets set wide
    # on its sides, the nose's dark hollow; nailed through the forehead, facing the road
    skull = lumpy_sphere("Skull", sk_at, (0.072, 0.085, 0.068), bone, 77, amp=0.05, subdiv=3)
    snout = skin_tree("Snout", [sk_at + Vector((0, 0.05, -0.01)), sk_at + Vector((0, 0.15, -0.07)), sk_at + Vector((0, 0.25, -0.15)), sk_at + Vector((0, 0.3, -0.19))],
                      [(0, 1), (1, 2), (2, 3)], [0.052, 0.04, 0.027, 0.018], bone, subdiv=2)
    dark = mat("socket", (0.02, 0.018, 0.016), 0.9)
    eyes = [lumpy_sphere(f"Socket{s}", sk_at + Vector((s * 0.058, 0.07, -0.005)), (0.022, 0.026, 0.024), dark, 79 + s, amp=0.1, subdiv=2) for s in (-1, 1)]
    nose = lumpy_sphere("Nose", sk_at + Vector((0, 0.31, -0.185)), (0.016, 0.01, 0.012), dark, 82, amp=0.1, subdiv=2)
    jaw_line = skin_tree("UpperJaw", [sk_at + Vector((s, 0.09, -0.06)) for s in (-0.035,)] + [sk_at + Vector((-0.02, 0.24, -0.17))], [(0, 1)], [0.012, 0.008], bone, subdiv=1)
    nail = cylinder("Nail", sk_at + Vector((0, 0.06, 0.04)), sk_at + Vector((0, -0.25, 0.04)), 0.008, 0.008, 6, mat("iron", (0.2, 0.18, 0.16), 0.6, 0.6))
    parts += [skull, snout, nose, jaw_line, nail] + eyes
    for s in (-1, 1):
        parts.append(antler(f"SkullAntler{s}", sk_at + Vector((s * 0.06, 0.02, 0.06)), (s * 0.3, -0.1, 1), (s, 0.1, 0.1), 1.3, rnd, bone))
    # ---- hung from the limbs on twine, turning: antlers, jawbones
    rnd.shuffle(hangs)
    used = []
    for hi in hangs:
        top = verts[hi] - Vector((0, 0, radii[hi] * 0.8))
        if any((top - u).length < 0.7 for u in used) or len(used) >= 18:
            continue
        used.append(top)
        drop = rnd.uniform(0.35, 1.0)
        end = top - Vector((0, 0, drop))
        parts.append(cylinder(f"Twine{len(used)}", top, end, 0.006, 0.006, 4, twine, caps=False))
        if len(used) % 3 == 0:
            parts.append(jawbone(f"Jaw{len(used)}", end - Vector((0.1, 0, 0.03)), rnd.uniform(0, math.tau), bone, tooth))
        else:
            a = rnd.uniform(0, math.tau)
            out = Vector((math.cos(a), math.sin(a), 0))
            parts.append(antler(f"Antler{len(used)}", end, (0, 0, -1), out, rnd.uniform(0.75, 1.0), rnd, bone))
    for o in parts:
        if o.type == "MESH" and len(o.data.uv_layers) == 0:
            uv_box(o, 2.0)
    ob = join(parts, "AntlerTree")
    add_mod(ob, "DECIMATE", ratio=0.45)   # (the antlers' skins are fine-meshed for what they are at this distance)
    apply_mods(ob)
    export([ob], "antler_tree")
    preview("antler_tree", (0, 0, 4.0), 11.0, 0.5, 30)
    preview("antler_tree_close", sk_at + Vector((0, 0.3, 0.5)), 2.6, 0.2, 10)
