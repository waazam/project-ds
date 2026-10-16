"""Acts 8 to 12's new pieces (STORY.md Revision 5, 2026-10-16), modelled in Blender. Run headless:

    blender -b --python tools/Blender/rh_trail.py

-> assets/models/items/rh_trail.glb:
- RhBoots: R.H.'s boots, the pair he took off just inside the bunker and walked on barefoot: worn leather hiking boots,
  their laces loosened and the tongues flopped open, one stood up straight and one slumped against it, and his initials,
  R.H., inked on the lining inside each top (on the back of the shaft, so a look down into them from the toe side reads
  them). Toes toward the game's -Z, heels at +Z, standing on y = 0, the pair across X.
- DoorRoots: roots and ivy grown through the bunker's vault door frame (Bunker-local: the ring's centre at
  (0, 1.25), its face at z = 0.3): roots out of the concrete over the top and down the right of the steel ring, ending
  in the gap at the door's edge, ivy leaves along them. Clear of the doorway (r < 1.14) and of the open leaf (the hinge
  side, low on the left).
- Crater: where he came down at the lake: a bowl of torn earth (its floor on y = 0), the rim thrown up round it and
  broken low on its -Z side where his body slid on out, clods and turf flung round it.
- Furrow: the long furrow his body dragged from the crater to where he lies: 10 m along -Z from the origin, a trench of
  dark turned earth between two low berms, deeper at the crater end, a heap of pushed earth at the far end. Rows every
  0.2 m, so the game can lay it to the ground's slope vertex by vertex.
- HurriedKnot: the rowboat's line knotted round the dock's cleat in a hurry: too many turns, tight and ugly, a loop
  half pulled through and the tail left hanging (the origin at the cleat's horn).

The game's axes, as rh_key.py: G() turns a game point (Y up, -Z forward) into Blender's, so the export's Y-up turn brings
it back exactly. Materials are named after the game's roles ("leather", "lining", "sole", "lace", "ink", "bark", "ivy",
"soil", "soil_dark", "turf", "rope"); the game paints them (BunkerHallway's, Bunker's, Lake's materials).
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix, noise
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "items")
os.makedirs(kit.OUT, exist_ok=True)

COL = {
    "leather": (0.26, 0.16, 0.09), "lining": (0.12, 0.1, 0.08), "sole": (0.06, 0.06, 0.06), "lace": (0.45, 0.4, 0.3),
    "ink": (0.02, 0.03, 0.08), "collar": (0.18, 0.11, 0.07), "bark": (0.32, 0.24, 0.17), "ivy": (0.12, 0.2, 0.08),
    "soil": (0.24, 0.18, 0.12), "soil_dark": (0.12, 0.09, 0.06), "turf": (0.22, 0.26, 0.12), "rope": (0.5, 0.44, 0.33),
}


def M(name, rough=0.8):
    return mat(name, COL[name], rough, 0.0)


def G(x, y, z):
    return Vector((x, -z, y))


def bm_obj(bm, name, material, smooth=True):
    return obj_from_bmesh(bm, name, material, smooth)


def tube(name, pts_game, radii, material, sides=8, cap=True):
    """A tube through game-frame points, its radius per point (a root, a lace, a rope)."""
    bm = bmesh.new()
    pts = [G(*p) for p in pts_game]
    rings = []
    prev_n = None
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        n = prev_n if prev_n is not None else (d.orthogonal().normalized())
        n = (n - d * n.dot(d)).normalized()
        prev_n = n
        b = d.cross(n).normalized()
        r = radii[i] if isinstance(radii, (list, tuple)) else radii
        ring = []
        for s in range(sides):
            a = 2 * math.pi * s / sides
            ring.append(bm.verts.new(p + (n * math.cos(a) + b * math.sin(a)) * r))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for s in range(sides):
            a, b_, c, d = rings[i][s], rings[i][(s + 1) % sides], rings[i + 1][(s + 1) % sides], rings[i + 1][s]
            bm.faces.new((a, b_, c, d))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm_obj(bm, name, material)


def face_up(ob):
    """Every face of a ground piece facing up (Blender's +Z): the winding a grid was built in is not to be trusted."""
    me = ob.data
    for p in me.polygons:
        if p.normal.z < 0:
            p.flip()
    me.update()
    return ob


# ------------------------------------------------------------------ R.H.'s boots


def text_mesh(s, size, name):
    bpy.ops.object.text_add()
    t = bpy.context.active_object
    t.data.body = s
    t.data.size = size
    t.data.extrude = 0.0004
    t.data.align_x = "CENTER"
    t.data.align_y = "CENTER"
    bpy.ops.object.convert(target="MESH")
    t = bpy.context.active_object
    t.name = name
    return t


def boot(k, ink, leather, lining, sole, lace, collar):
    """One boot in the game frame: heel at +Z, toe toward -Z, standing on y = 0, the shaft's axis at (0, z = 0.03)."""
    parts = []
    zc, r_out, r_in, y0, y1 = 0.03, 0.056, 0.049, 0.075, 0.2
    # the sole: a rounded slab, a heel block, lugs
    s = box(f"sole{k}", G(0, 0.016, -0.06), (0.104, 0.285, 0.032), sole, bevel=0.012, segs=2)
    parts.append(s)
    for j in range(6):
        parts.append(box(f"lug{k}{j}", G(0, 0.003, 0.05 - j * 0.045), (0.098, 0.014, 0.008), sole))
    # the foot: ankle down into the vamp and the toe
    foot = skin_tree(f"foot{k}", [G(0, 0.075, 0.03), G(0, 0.065, -0.05), G(0, 0.058, -0.13), G(0, 0.05, -0.18)],
                     [(0, 1), (1, 2), (2, 3)], [(0.045, 0.05), (0.047, 0.044), (0.045, 0.038), (0.034, 0.03)], leather, subdiv=2)
    parts.append(foot)
    # the shaft: an open tube, leather outside, the lining inside (its own faces, facing in), a padded collar round the top
    bm = bmesh.new()
    seg, rows = 20, 6
    outer, inner = [], []
    for r in range(rows + 1):
        y = y0 + (y1 - y0) * r / rows
        # the top dips at the front (the tongue's opening) and rises at the heel
        ro, ri = [], []
        for s_ in range(seg):
            a = 2 * math.pi * s_ / seg
            x, z = math.sin(a), math.cos(a)
            yy = y - (0.05 * max(0.0, -z) ** 1.5 if r == rows else 0.0)
            # oval (narrower across), leaning a little forward, nipped in at the ankle
            lean, nip = -0.018 * r / rows, 1.0 - 0.08 * math.sin(math.pi * r / rows)
            ro.append(bm.verts.new(G(x * r_out * 0.8 * nip, yy, zc + lean + z * r_out * nip)))
            ri.append(bm.verts.new(G(x * r_in * 0.8 * nip, yy, zc + lean + z * r_in * nip)))
        outer.append(ro)
        inner.append(ri)
    for r in range(rows):
        for s_ in range(seg):
            n = (s_ + 1) % seg
            f = bm.faces.new((outer[r][s_], outer[r][n], outer[r + 1][n], outer[r + 1][s_]))
            f.material_index = 0
            f = bm.faces.new((inner[r][n], inner[r][s_], inner[r + 1][s_], inner[r + 1][n]))
            f.material_index = 1
    for s_ in range(seg):
        n = (s_ + 1) % seg
        f = bm.faces.new((outer[rows][s_], outer[rows][n], inner[rows][n], inner[rows][s_]))
        f.material_index = 1
    sh = bm_obj(bm, f"shaft{k}", leather)
    sh.data.materials.append(lining)
    parts.append(sh)
    # the footbed, dark, a little down inside
    fb = cylinder(f"bed{k}", G(0, 0.08, zc), G(0, 0.082, zc), r_in * 0.76, r_in * 0.76, 20, lining)
    parts.append(fb)
    # the collar: a padded roll round the back of the top
    cpts = []
    for i in range(9):
        a = math.pi * (0.25 + 1.5 * i / 8)
        cpts.append((math.sin(a) * (r_out + 0.003) * 0.8, y1 - 0.004 + 0.006 * math.cos(a) - 0.05 * max(0.0, -math.cos(a)) ** 1.5, zc - 0.018 + math.cos(a) * (r_out + 0.003)))
    parts.append(tube(f"collar{k}", cpts, 0.009, collar, 8, True))
    # the tongue, flopped forward out of the opening: a padded strip, solidified
    bmt = bmesh.new()
    edge = []
    for i in range(7):
        u = i / 6
        y = 0.1 + 0.11 * u - 0.05 * u * u
        z = zc - r_out + 0.004 - 0.065 * u * u
        edge.append((bmt.verts.new(G(-0.026 + 0.004 * u, y, z)), bmt.verts.new(G(0.026 - 0.004 * u, y, z))))
    for i in range(6):
        bmt.faces.new((edge[i][0], edge[i][1], edge[i + 1][1], edge[i + 1][0]))
    tgo = bm_obj(bmt, f"tongue{k}", leather)
    add_mod(tgo, "SOLIDIFY", thickness=0.007, offset=0.0)
    apply_mods(tgo)
    parts.append(tgo)
    # the laces: loose crossings down the vamp, the ends hanging
    rng = random.Random(40 + k)
    for j in range(4):
        y = 0.08 + j * 0.028
        z = zc - r_out - 0.004 - j * 0.01
        parts.append(tube(f"lace{k}{j}", [(-0.03, y, z + 0.01), (0.0, y + 0.004, z - 0.002), (0.03, y + 0.01, z + 0.01)], 0.0028, lace, 5))
    for side in (-1, 1):
        parts.append(tube(f"end{k}{side}", [(side * 0.03, 0.17, zc - r_out + 0.004), (side * 0.045, 0.12, zc - r_out - 0.02),
                                              (side * 0.05 + rng.uniform(-0.01, 0.01), 0.03, zc - r_out - 0.03 + rng.uniform(-0.02, 0.0)),
                                              (side * 0.06, 0.004, zc - r_out - 0.05)], 0.0028, lace, 5))
    # his initials, inked on the lining at the back of the shaft just under the collar, wrapped round its curve
    t = text_mesh("R.H.", 0.045, f"ink{k}")
    t.data.materials.clear()
    t.data.materials.append(ink)
    yc = y1 - 0.04
    for v in t.data.vertices:
        tx, ty = v.co.x, v.co.y
        # on the wall as it is at that height (its nip and lean), a hair in front of it
        u = (yc + ty - y0) / (y1 - y0)
        nip, lean = 1.0 - 0.08 * math.sin(math.pi * u), -0.018 * u
        r_ink = r_in * nip - 0.0025
        th = -tx / r_ink   # read from inside, looking at the heel: its right is the game's -X
        v.co = G(math.sin(th) * r_ink * 0.8, yc + ty, zc + lean + math.cos(th) * r_ink)
    t.location = (0, 0, 0)
    t.rotation_euler = (0, 0, 0)
    parts.append(t)
    b = join(parts, f"Boot{k}")
    return b


def rh_boots():
    ink, leather, lining, sole, lace, collar = M("ink", 0.5), M("leather", 0.7), M("lining", 0.9), M("sole", 0.9), M("lace", 0.9), M("collar", 0.8)
    a = boot(0, ink, leather, lining, sole, lace, collar)
    b = boot(1, ink, leather, lining, sole, lace, collar)
    # the pair: one stood up straight, the other a hand's width over and slumped against it a little, turned out
    a.data.transform(Matrix.Translation(G(-0.065, 0, 0.0)))
    b.data.transform(Matrix.Translation(G(0.07, 0, 0.015)) @ Matrix.Rotation(-0.18, 4, "Z") @ Matrix.Rotation(-0.12, 4, "Y"))
    b.data.transform(Matrix.Translation((0, 0, 0.004)))
    return join([a, b], "RhBoots")


# ------------------------------------------------------------------ roots and ivy through the vault door's frame


def leaf(bm, at, normal, size, rng):
    """An ivy leaf: a five-pointed flat shape on a short stalk, facing out along normal (Blender frame)."""
    n = normal.normalized()
    t = n.orthogonal().normalized()
    t = (Matrix.Rotation(rng.uniform(0, 2 * math.pi), 3, n) @ t).normalized()
    b = n.cross(t)
    c = bm.verts.new(at + n * 0.004)
    ring = []
    for i in range(10):
        a = 2 * math.pi * i / 10
        rr = size * (1.0 if i % 2 == 0 else 0.55) * (0.75 if i in (4, 5, 6) else 1.0)
        p = at + (t * math.cos(a) + b * math.sin(a)) * rr + t * size * 0.35 + n * (0.008 * math.sin(a * 2))
        ring.append(bm.verts.new(p))
    for i in range(10):
        bm.faces.new((c, ring[i], ring[(i + 1) % 10]))


def door_roots():
    bark, ivy = M("bark", 0.95), M("ivy", 0.7)
    rng = random.Random(8812)
    cy, face = 1.25, 0.3
    roots = []
    # (angle on the ring in degrees, 0 = top, positive toward the game's +X: over the top and down the right side, a
    # couple high on the left clear of the hinge)
    angles = [-38, -20, -6, 8, 22, 37, 52, 68, 86, 104, 122, 140, -52]
    bm = bmesh.new()
    for i, deg in enumerate(angles):
        a0 = math.radians(deg + rng.uniform(-4, 4))
        drift = math.radians(rng.uniform(-14, 14))
        pts, radii = [], []
        steps = 9
        thick = rng.uniform(0.028, 0.055)
        for s in range(steps + 1):
            u = s / steps
            r = 1.9 - 0.78 * u + 0.05 * math.sin(u * 7 + i)
            a = a0 + drift * u + 0.05 * math.sin(u * 9 + i * 2)
            x, y = math.sin(a) * r, cy + math.cos(a) * r
            # out of the concrete, over the frame's face (it stands a few cm proud), and down into the gap at the door
            z = face - 0.03 + 0.1 * math.sin(min(1.0, u * 1.25) * math.pi) + 0.03 * u
            if u > 0.88:
                z -= (u - 0.88) * 0.9
            pts.append((x, y, z))
            radii.append(thick * (1.15 - 0.85 * u))
        roots.append(tube(f"root{i}", pts, radii, bark, 7, True))
        # a side root or two, thin, off the bigger ones
        if thick > 0.04:
            j = rng.randint(2, 5)
            base = pts[j]
            side = rng.choice([-1, 1])
            sp = [base]
            for s in range(1, 5):
                sp.append((base[0] + side * 0.07 * s * math.cos(a0), base[1] + side * 0.07 * s * -math.sin(a0) - 0.02 * s, face + 0.02 + 0.015 * math.sin(s)))
            roots.append(tube(f"side{i}", sp, [thick * 0.5, thick * 0.4, thick * 0.3, thick * 0.2, thick * 0.1], bark, 6, True))
        # leaves along it, more where it comes out of the concrete
        for s in range(steps):
            if rng.random() < 0.75:
                p = G(*pts[s]) + Vector((rng.uniform(-0.03, 0.03), 0, rng.uniform(-0.03, 0.03)))
                nrm = G(rng.uniform(-0.3, 0.3), rng.uniform(-0.2, 0.4), 1.0)
                leaf(bm, p + nrm.normalized() * radii[s], nrm, rng.uniform(0.035, 0.06), rng)
    # a curtain of ivy over the face above the ring, and a few leaves climbing its right side
    for i in range(60):
        a = math.radians(rng.uniform(-60, 140))
        r = rng.uniform(1.4, 2.3)
        x, y = math.sin(a) * r, cy + math.cos(a) * r
        if y < 0.4 or abs(x) > 2.5:
            continue
        p = G(x, y, face + 0.015)
        leaf(bm, p, G(rng.uniform(-0.2, 0.2), rng.uniform(0.0, 0.3), 1.0), rng.uniform(0.04, 0.065), rng)
    leaves = bm_obj(bm, "leaves", ivy, smooth=False)
    for o in roots:
        uv_box(o, 6.0)
    uv_box(leaves, 10.0)
    return join(roots + [leaves], "DoorRoots")


# ------------------------------------------------------------------ the crater and the furrow at the lake


def polar_heights(bm, rmax, rings, seg, h, mat_of):
    """A disc of ground (game frame) whose height is h(r, theta); faces take a material index from mat_of(r)."""
    grid = []
    centre = bm.verts.new(G(0, h(0.0, 0.0), 0))
    for i in range(1, rings + 1):
        r = rmax * i / rings
        row = []
        for s in range(seg):
            th = 2 * math.pi * s / seg
            row.append(bm.verts.new(G(math.sin(th) * r, h(r, th), math.cos(th) * r)))
        grid.append(row)
    for s in range(seg):
        f = bm.faces.new((centre, grid[0][s], grid[0][(s + 1) % seg]))
        f.material_index = mat_of(0.5 * rmax / rings)
    for i in range(rings - 1):
        r = rmax * (i + 1.5) / rings
        for s in range(seg):
            n = (s + 1) % seg
            f = bm.faces.new((grid[i][s], grid[i + 1][s], grid[i + 1][n], grid[i][n]))
            f.material_index = mat_of(r)


def clods(name, rng, count, area, size, material):
    obs = []
    for i in range(count):
        x, z = area(rng)
        s = rng.uniform(size * 0.4, size)
        obs.append(lumpy_sphere(f"{name}{i}", G(x, s * 0.25, z), (s, s * 0.8, s * 0.7), material, 50 + i, amp=0.18, freq=2.5, subdiv=1))
    return obs


def crater():
    soil, dark, turf = M("soil", 0.95), M("soil_dark", 0.95), M("turf", 0.95)
    rng = random.Random(1212)
    off = rng.uniform(0, 50)

    def h(r, th):
        n = noise.noise(Vector((math.sin(th) * r * 1.3 + off, math.cos(th) * r * 1.3, 0.5)))
        # the rim low on its -Z side (theta = pi: the game's -Z), where he slid on out of it
        gap = 1.0 - 0.75 * math.exp(-((th - math.pi) ** 2) / 0.18)
        rim = 0.34 * gap * math.exp(-((r - 1.45) ** 2) / 0.16) * (1.0 + 0.35 * n)
        bowl = 0.02 + 0.03 * n if r < 1.1 else 0.0
        edge = -0.05 * max(0.0, (r - 2.3) / 0.4)
        return max(bowl, rim) + edge + 0.01

    bm = bmesh.new()
    polar_heights(bm, 2.7, 18, 40, h, lambda r: 1 if r < 1.15 else 0)
    ob = face_up(bm_obj(bm, "bowl", soil))
    ob.data.materials.append(dark)
    parts = [ob]
    parts += clods("clod", rng, 22, lambda g: (lambda a, r: (math.sin(a) * r, math.cos(a) * r))(g.uniform(0, 2 * math.pi), g.uniform(1.9, 3.4)), 0.13, soil)
    # torn turf flung out over the rim, curled
    bmt = bmesh.new()
    for i in range(9):
        a = rng.uniform(0, 2 * math.pi)
        if abs(a - math.pi) < 0.5:
            continue
        r = rng.uniform(1.7, 2.6)
        c = G(math.sin(a) * r, 0.03, math.cos(a) * r)
        t = G(math.cos(a), 0, -math.sin(a))
        rad = G(math.sin(a), 0, math.cos(a))
        w, l = rng.uniform(0.15, 0.3), rng.uniform(0.2, 0.4)
        vs = []
        for j in range(4):
            u = j / 3
            lift = 0.06 * math.sin(u * math.pi) + 0.05 * u
            for sx in (-1, 1):
                vs.append(bmt.verts.new(c + rad * (l * (u - 0.5)) + t * (sx * w * 0.5) + Vector((0, 0, lift))))
        for j in range(3):
            bmt.faces.new((vs[j * 2], vs[j * 2 + 1], vs[j * 2 + 3], vs[j * 2 + 2]))
    tf = bm_obj(bmt, "turf", turf, smooth=False)
    add_mod(tf, "SOLIDIFY", thickness=0.03, offset=-1.0)
    apply_mods(tf)
    parts.append(tf)
    for o in parts:
        uv_box(o, 2.0)
    return join(parts, "Crater")


def furrow():
    soil, dark, turf = M("soil", 0.95), M("soil_dark", 0.95), M("turf", 0.95)
    rng = random.Random(1213)
    L, step = 10.0, 0.2
    xs = [-1.0, -0.82, -0.66, -0.5, -0.38, -0.27, -0.14, 0.0, 0.14, 0.27, 0.38, 0.5, 0.66, 0.82, 1.0]
    off = rng.uniform(0, 40)
    bm = bmesh.new()
    rows = []
    n_rows = int(L / step) + 1
    for i in range(n_rows):
        s = i * step
        u = s / L
        wig = 0.12 * math.sin(s * 0.7 + 1.0) + 0.05 * math.sin(s * 2.1)   # the body slewed a little as it went
        depth = 0.7 + 0.3 * (1.0 - u)
        row = []
        for x in xs:
            ax = abs(x)
            n = noise.noise(Vector((x * 3 + off, s * 1.7, 0.3)))
            if ax <= 0.27:
                y = 0.015 + 0.012 * n
            else:
                berm = 0.2 * depth * math.exp(-((ax - 0.48) ** 2) / 0.03) * (1.0 + 0.4 * n)
                y = berm + 0.01 - 0.045 * max(0.0, (ax - 0.85) / 0.15)
            # the end: earth pushed up in a heap where he stopped
            if u > 0.9:
                y += 0.22 * math.exp(-((s - (L - 0.35)) ** 2) / 0.08) * math.exp(-(x * x) / 0.35)
            row.append(bm.verts.new(G(x * (0.9 + 0.15 * depth) + wig, y, -s)))
        rows.append(row)
    for i in range(n_rows - 1):
        for j in range(len(xs) - 1):
            f = bm.faces.new((rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]))
            f.material_index = 1 if abs(xs[j] + xs[j + 1]) * 0.5 < 0.28 else 0
    ob = face_up(bm_obj(bm, "trench", soil))
    ob.data.materials.append(dark)
    parts = [ob]
    parts += clods("fclod", rng, 26, lambda g: (g.choice([-1, 1]) * g.uniform(0.65, 1.3), -g.uniform(0.2, L)), 0.09, soil)
    for o in parts:
        uv_box(o, 2.0)
    fu = join(parts, "Furrow")
    # (every face up: the game lays it to the slope by vertex height, and lights it from above)
    return fu


# ------------------------------------------------------------------ the hurried knot


def hurried_knot():
    rope = M("rope", 0.95)
    rng = random.Random(1214)
    parts = []
    # turns round the cleat's horn (along the game's Z), too many and crossed over each other, pulled tight
    for i in range(6):
        ang = rng.uniform(-0.6, 0.6)
        zc = -0.05 + i * 0.02 + rng.uniform(-0.006, 0.006)
        pts = []
        for s in range(15):
            a = 2 * math.pi * s / 14
            r = 0.022 + 0.004 * math.sin(a * 3 + i)
            x, y = math.cos(a) * r, 0.03 + math.sin(a) * r
            pts.append((x, y, zc + math.cos(a) * math.sin(ang) * 0.025))
        parts.append(tube(f"turn{i}", pts, 0.0072, rope, 6, False))
    # a figure-eight across the top, crooked
    pts = []
    for s in range(25):
        t = 2 * math.pi * s / 24
        pts.append((0.03 * math.sin(t), 0.058 + 0.008 * math.sin(2 * t), 0.06 * math.sin(t) * math.cos(t) * 1.6))
    parts.append(tube("eight", pts, 0.0072, rope, 6, False))
    # the loop half pulled through, and the tail left hanging down off the dock's edge
    parts.append(tube("loop", [(0.02, 0.06, 0.02), (0.05, 0.075, 0.035), (0.065, 0.05, 0.03), (0.045, 0.03, 0.01)], 0.007, rope, 6))
    parts.append(tube("tail", [(0.03, 0.04, -0.05), (0.08, 0.02, -0.08), (0.12, -0.05, -0.09), (0.13, -0.18, -0.07), (0.125, -0.3, -0.08)],
                      [0.0072, 0.0072, 0.007, 0.0068, 0.005], rope, 6))
    for o in parts:
        uv_box(o, 20.0)
    return join(parts, "HurriedKnot")


def build():
    clear()
    obs = [rh_boots(), door_roots(), crater(), furrow(), hurried_knot()]
    for o in obs:
        o.location = (0, 0, 0)
    export(obs, "rh_trail")
    for o in obs:
        for q in obs:
            q.hide_render = q is not o
        d = max(o.dimensions)
        c = sum((o.matrix_world @ Vector(b) for b in o.bound_box), Vector()) / 8
        preview(f"rh_trail_{o.name}", tuple(c), d * 1.2 + 0.1, height=d * 0.9, yaw=10, size=(600, 450))


if __name__ == "__main__":
    build()
