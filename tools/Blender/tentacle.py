"""The tentacle creatures' flesh, modelled in Blender (2026-10-15, the owner: "from now on everything should be made in
blender"): the lake creature's limbs (Act 12), the window tentacle (Act 13), and the Leviathan (Act 18): its limbs and
the hide of its body.

    blender -b --python tools/Blender/tentacle.py

Writes assets/models/tentacle/tentacle.glb, one scene of unrigged meshes the game reads by name:

  Limb, LimbLong, LimbWhip   a limb straight up +Y, LENGTH long, unit radius (the game lays each limb's own radius over it:
                             TentacleKit.LimbMesh). The sucker cups are shaped into the one skin in two loose staggered
                             rows down the +Z side (the inner face), each its own size and place, now and then one
                             missing; slow swellings and pinches along it, soft lobes round it, fine creases. The three
                             are made for limbs of different build (length to thickness about 12, 20 and 70), so the cups
                             stay round once the game stretches them: lake and window / the Leviathan's thick limbs /
                             its skinny whips. Surfaces: TentFlesh, SuckerRim, SuckerHole.
  Maw                        the mouth at a limb's tip, size 1 (the game scales it): the limb's end swelling into it,
                             three curled lips of flesh round a dark throat, hooked teeth down their inner edges.
                             Neck radius 0.21 at y = 0. Surfaces: MawFlesh, Throat, Tooth.
  Eye_Ball, Eye_Iris, Eye_Pupil_Round/_Slit/_Bar, Eye_Lid
                             the eye in parts (radius 1, looking down -Z), put together by LakeCreature.BuildEye.
                             Surfaces: Sclera / Iris / Pupil / Lid.
  LeviathanHide              the body's hide, in the body's local space, sculpted on the game's own layout
                             (tools/Blender/data/leviathan_hide.json, written by `creature_preview -- --leviathan
                             --dump-leviathan`): its lobes and their fused swell, folds and wrinkles, warty sores, and a
                             socket with a swollen lid where each of its eyes sits. Surface: Hide.

Everything is built in the game's space (x right, y up, z toward the viewer) and turned into Blender's (x, -z, y) on the
way in, so the export (+Y up) lands back in the game's space.
"""
import bpy, bmesh, json, math, os, random, sys
import numpy as np
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, apply_mods, add_mod, ROOT

OUT = os.path.join(ROOT, "assets", "models", "tentacle")
DATA = os.path.join(ROOT, "tools", "Blender", "data", "leviathan_hide.json")
os.makedirs(OUT, exist_ok=True)
LENGTH = 12.0


def to_blender(v):
    """Game (x, y, z) rows to Blender's (x, -z, y)."""
    return np.stack([v[:, 0], -v[:, 2], v[:, 1]], axis=1)


def mesh_from(name, verts, faces, mats, face_mat=None):
    """An object from game-space vertices and quad/tri faces; face_mat picks each face's slot."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in to_blender(np.asarray(verts, np.float64))], [], [tuple(f) for f in faces])
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for m in mats:
        me.materials.append(m)
    if face_mat is not None:
        for p, mi in zip(me.polygons, face_mat):
            p.material_index = int(mi)
    for p in me.polygons:
        p.use_smooth = True
    # every face facing out
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return ob


def nz(x, y, z):
    return noise.noise(Vector((x, y, z)))


# ------------------------------------------------------------------ the limb


def profile(f):
    """The usual taper of a limb (unit radius at the root): only used to size the cups along it, so they stay round."""
    return 1.0 + (0.3 - 1.0) * f ** 1.6


def build_limb(name, aspect, seed, rings, sides, flesh, rim, hole):
    rnd = random.Random(seed)
    # the cups: two loose staggered rows round a = 0 (+Z), sized and spaced by the limb's taper
    cups = []
    for row in (-1, 1):
        y = 0.25 + (0.5 if row > 0 else 0.0) * 0.6
        while y < LENGTH * 0.985:
            f = y / LENGTH
            w = 0.27 * rnd.uniform(0.78, 1.2)                       # its half-width round the limb (radians, unit radius)
            h = w * profile(f) * LENGTH / aspect                    # and along it, so it's round once stretched
            if rnd.random() > 0.08:
                cups.append((y + rnd.uniform(-0.15, 0.15) * h, row * 0.45 * rnd.uniform(0.85, 1.12) + rnd.uniform(-0.05, 0.05), w, h,
                             rnd.uniform(0.85, 1.15)))
            y += h * 2.0 * rnd.uniform(1.05, 1.35)
    ys = np.linspace(0.0, LENGTH, rings)
    angs = np.linspace(0.0, 2 * math.pi, sides, endpoint=False)
    Y, A = np.meshgrid(ys, angs, indexing="ij")
    F = Y / LENGTH
    m = np.ones_like(Y)
    # the flesh's own shape: swellings and pinches along it, soft lobes round it, the sucker face a little flatter
    ph = rnd.uniform(0, 6.28)
    for i in range(rings):
        for j in range(sides):
            a, y = A[i, j], Y[i, j]
            m[i, j] += 0.06 * nz(y * 0.55 * aspect / 12.0, 3.1 + seed, 0.0) * (1.0 - 0.5 * F[i, j])
            m[i, j] += 0.035 * math.sin(3 * a + ph + y * 0.4) + 0.025 * math.sin(5 * a - ph - y * 0.6)
            m[i, j] += 0.018 * nz(math.cos(a) * 2.5 + y * 0.9 * aspect / 12.0, math.sin(a) * 2.5, 9.0 + seed)   # fine creases
    under = np.maximum(0.0, np.cos(A)) ** 2
    m *= 1.0 - 0.08 * under
    # the cups shaped into the skin: a soft round swelling, a raised rim, a wet pit sunk in its middle; and painted into
    # the vertex colour (red the rim, green the pit) for octopus_flesh to colour smoothly, round, not by faces
    rim_w = np.zeros_like(Y)
    pit_w = np.zeros_like(Y)
    for (cy, ca, w, h, depth) in cups:
        da = np.angle(np.exp(1j * (A - ca)))
        rho = np.sqrt(((Y - cy) / h) ** 2 + (da / w) ** 2)
        near = rho < 1.8
        if not near.any():
            continue
        disp = 0.12 * np.exp(-(rho / 1.0) ** 2) + 0.07 * np.exp(-((rho - 0.68) / 0.16) ** 2) - 0.15 * depth * np.exp(-(rho / 0.36) ** 2)
        m += np.where(near, disp, 0.0)
        rim_w = np.maximum(rim_w, np.clip(1.35 * np.exp(-((rho - 0.6) / 0.3) ** 2), 0.0, 1.0))
        pit_w = np.maximum(pit_w, np.exp(-(rho / 0.34) ** 4))
    verts = np.stack([np.sin(A) * m, Y, np.cos(A) * m], axis=-1).reshape(-1, 3)
    cols = [(float(r), float(g), 0.0, 1.0) for r, g in zip(rim_w.ravel(), pit_w.ravel())]
    faces, fmat = [], []
    for i in range(rings - 1):
        for j in range(sides):
            a, b = i * sides + j, i * sides + (j + 1) % sides
            c, d = a + sides, b + sides
            faces.append((a, b, d, c))
            fmat.append(0)
    # a dome over the end
    last = (rings - 1) * sides
    cap = []
    for k in range(1, 4):
        t = k / 4.0
        ring = []
        for j in range(sides):
            a = angs[j]
            r = math.cos(t * math.pi * 0.5) * m[-1, j]
            ring.append((math.sin(a) * r, LENGTH + math.sin(t * math.pi * 0.5) * 0.5, math.cos(a) * r))
        cap.append(ring)
    verts = verts.tolist()
    prev = list(range(last, last + sides))
    for ring in cap:
        start = len(verts)
        verts.extend(ring)
        cur = list(range(start, start + sides))
        for j in range(sides):
            faces.append((prev[j], prev[(j + 1) % sides], cur[(j + 1) % sides], cur[j]))
            fmat.append(0)
        prev = cur
    tip = len(verts)
    verts.append((0.0, LENGTH + 0.5, 0.0))
    for j in range(sides):
        faces.append((prev[j], prev[(j + 1) % sides], tip))
        fmat.append(0)
    # (the root's end is open: it's always buried in the water or the body)
    cols += [(0.0, 0.0, 0.0, 1.0)] * (len(verts) - len(cols))
    ob = mesh_from(name, verts, faces, [flesh], fmat)
    ca = ob.data.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for i, c in enumerate(cols):
        ca.data[i].color = c
    ob.data.color_attributes.active_color = ca
    ob.data.color_attributes.render_color_index = 0
    print(f"[tentacle] {name}: {len(ob.data.polygons)} faces, {len(cups)} cups")
    return ob


# ------------------------------------------------------------------ the mouth


def loft_rings(rings_pts):
    """Faces joining a list of equal-length closed rings of vertex indices."""
    faces = []
    for r0, r1 in zip(rings_pts, rings_pts[1:]):
        n = len(r0)
        for j in range(n):
            faces.append((r0[j], r0[(j + 1) % n], r1[(j + 1) % n], r1[j]))
    return faces


def build_maw(flesh_m, throat_m, tooth_m):
    rnd = random.Random(707)
    S = 28
    verts, faces = [], []

    def ring(y, r, squash=1.0, wob=0.0, ph=0.0):
        idx = []
        for j in range(S):
            a = j / S * 2 * math.pi
            rr = r * (1 + wob * math.sin(3 * a + ph))
            idx.append(len(verts))
            verts.append((math.sin(a) * rr, y, math.cos(a) * rr * squash))
        return idx
    # the swelling: the limb's own end widening into the mouth
    neck, head = 0.21, 0.31
    rs = [ring(-0.12, neck * 0.98), ring(0.05, neck * 1.04, wob=0.03), ring(0.16, (neck + head) / 2, wob=0.04, ph=1.0),
          ring(0.3, head, wob=0.05, ph=2.0), ring(0.44, head * 0.96, wob=0.04, ph=0.5), ring(0.52, head * 0.86)]
    faces += loft_rings(rs)
    swell = mesh_from("MawSwell", verts, faces, [flesh_m])
    # three lips: curled, thick at the root, thinning to a soft edge
    lips = []
    for jaw in range(3):
        a = jaw / 3 * 2 * math.pi + 0.4 + rnd.uniform(-0.15, 0.15)
        radial = Vector((math.cos(a), 0, math.sin(a)))
        side = Vector((-radial.z, 0, radial.x))
        ln, flare = rnd.uniform(0.85, 1.15), 0.38 * rnd.uniform(0.8, 1.25)
        root = Vector((0, 0.5, 0)) + radial * head * 0.62
        mid = root + (Vector((0, 0.8, 0)) + radial * flare).normalized() * 0.5 * ln
        tip = mid + (Vector((0, 0.35, 0)) + radial * (flare + 0.65)).normalized() * 0.42 * ln

        def B(u):
            return root * (1 - u) ** 2 + mid * 2 * (1 - u) * u + tip * u * u
        lv, lf = [], []
        rings_i = []
        N = 14
        for q in range(9):
            u = q / 8
            w = head * 1.15 * (1 - 0.82 * u ** 1.3) * 0.5
            th = 0.11 * (1 - u) + 0.025 * u
            c = B(u)
            rr = []
            for j in range(N):
                t = j / N * 2 * math.pi
                p = c + side * math.cos(t) * w + radial * math.sin(t) * th
                rr.append(len(lv))
                lv.append(tuple(p))
            rings_i.append(rr)
        lf += loft_rings(rings_i)
        tipi = len(lv)
        lv.append(tuple(B(1.0) + (B(1.0) - B(0.95)).normalized() * 0.03))
        for j in range(N):
            lf.append((rings_i[-1][j], rings_i[-1][(j + 1) % N], tipi))
        lip = mesh_from(f"Lip{jaw}", lv, [f[::-1] for f in lf], [flesh_m])
        lips.append((lip, B, radial, side, head))
    # fuse the swelling and the lips into one smooth flesh
    bpy.ops.object.select_all(action="DESELECT")
    for o in [swell] + [l[0] for l in lips]:
        o.select_set(True)
    bpy.context.view_layer.objects.active = swell
    bpy.ops.object.join()
    add_mod(swell, "SOLIDIFY", thickness=0.035, offset=-1.0)   # (open tubes: a voxel remesh of a bare surface comes out empty)
    apply_mods(swell)
    add_mod(swell, "REMESH", mode="VOXEL", voxel_size=0.01, adaptivity=0.0)
    apply_mods(swell)
    add_mod(swell, "SMOOTH", factor=0.6, iterations=6)
    apply_mods(swell)
    add_mod(swell, "DECIMATE", ratio=min(1.0, 6000 / max(len(swell.data.polygons), 1)))   # (one on every limb: ~6k faces)
    apply_mods(swell)
    swell.name = "MawFleshPart"
    for p in swell.data.polygons:
        p.use_smooth = True
    # the throat, dark and wet, down inside it
    tv, tf = [], []
    trs = []
    for (y, r) in [(0.3, head * 0.3), (0.42, head * 0.6), (0.5, head * 0.78), (0.56, head * 0.86)]:
        rr = []
        for j in range(20):
            a = j / 20 * 2 * math.pi
            rr.append(len(tv))
            tv.append((math.sin(a) * r, y, math.cos(a) * r))
        trs.append(rr)
    tf += [f[::-1] for f in loft_rings(trs)]
    c = len(tv)
    tv.append((0, 0.26, 0))
    for j in range(20):
        tf.append((trs[0][(j + 1) % 20], trs[0][j], c))
    throat = mesh_from("MawThroat", tv, tf, [throat_m])
    # teeth down both edges of each lip, hooked inward, each its own length
    dv, df = [], []
    for (lip, B, radial, side, hd) in lips:
        for t in range(7):
            u = (t + 0.5) / 7
            p = B(u * 0.92)
            ww = hd * 1.15 * (1 - 0.82 * u ** 1.3) * 0.42
            for s in (-1, 1):
                base = p + side * s * ww - radial * 0.03
                tl = rnd.uniform(0.06, 0.13) * (1.2 - u * 0.5)
                tipp = base - radial * tl + Vector((0, tl * 0.3, 0))
                axis = (tipp - base).normalized()
                ortho = axis.cross(Vector((0, 1, 0)) if abs(axis.y) < 0.9 else Vector((1, 0, 0))).normalized()
                ortho2 = axis.cross(ortho)
                i0 = len(dv)
                for k in range(6):
                    a = k / 6 * 2 * math.pi
                    dv.append(tuple(base + (ortho * math.cos(a) + ortho2 * math.sin(a)) * tl * 0.28))
                dv.append(tuple(tipp))
                for k in range(6):
                    df.append((i0 + k, i0 + (k + 1) % 6, i0 + 6))
    teeth = mesh_from("MawTeeth", dv, df, [tooth_m])
    bpy.ops.object.select_all(action="DESELECT")
    for o in (swell, throat, teeth):
        o.select_set(True)
    bpy.context.view_layer.objects.active = swell
    bpy.ops.object.join()
    swell.name = "Maw"
    print(f"[tentacle] Maw: {len(swell.data.polygons)} faces")
    return swell


# ------------------------------------------------------------------ the eyes


def build_eye_parts(sclera_m, iris_m, pupil_m, lid_m):
    """The eye in parts, put together by the game (LakeCreature.BuildEye): Eye_Ball (the white, radius 1, looking down -Z),
    Eye_Iris (a domed cap just proud of the front), Eye_Pupil_Round / _Slit / _Bar (a dark shape on it), Eye_Lid (a
    swollen, lumpy, wet lid of flesh round the front, for the eyes set in the hide)."""
    out = []
    verts, faces = [], []
    R, S = 18, 32
    for i in range(R + 1):
        th = i / R * math.pi
        for j in range(S):
            ph = j / S * 2 * math.pi
            x, y, z = math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), -math.cos(th)
            if z < -0.86:
                z = -0.86 + (z + 0.86) * 0.55   # flattened a little under the iris
            verts.append((x, y, z))
    for i in range(R):
        for j in range(S):
            a, b = i * S + j, i * S + (j + 1) % S
            faces.append((a, b, b + S, a + S))
    ball = mesh_from("Eye_Ball", verts, faces, [sclera_m])
    # the white's veins are a texture (LakeCreature.EyeMat), laid on as Godot's own sphere lays it: round the Y axis,
    # pole to pole down it
    uv = ball.data.uv_layers.new(name="UVMap")
    for poly in ball.data.polygons:
        us = []
        for li in poly.loop_indices:
            co = ball.data.vertices[ball.data.loops[li].vertex_index].co
            gx, gy, gz = co.x, co.z, -co.y
            us.append((math.atan2(gz, gx) / (2 * math.pi) + 0.5, 1.0 - math.acos(max(-1.0, min(1.0, gy))) / math.pi))
        # (no wrap across the seam within one face)
        if max(u for u, _ in us) - min(u for u, _ in us) > 0.5:
            us = [(u + 1.0 if u < 0.5 else u, v) for u, v in us]
        for li, (u, v) in zip(poly.loop_indices, us):
            uv.data[li].uv = (u, v)
    out.append(ball)
    iv, iff = [], []
    ir, rings, segs = 0.5, 7, 32
    for k in range(rings + 1):
        rr = ir * k / rings
        for j in range(segs):
            a = j / segs * 2 * math.pi
            iv.append((math.cos(a) * rr, math.sin(a) * rr, -0.88 - 0.05 * (1 - (rr / ir) ** 2)))
    for k in range(rings):
        for j in range(segs):
            a, b = k * segs + j, k * segs + (j + 1) % segs
            iff.append((a + segs, b + segs, b, a))
    out.append(mesh_from("Eye_Iris", iv, iff, [iris_m]))
    for name, (sx, sy) in (("Round", (0.18, 0.18)), ("Slit", (0.08, 0.3)), ("Bar", (0.3, 0.08))):
        pv, pf = [], []
        n = 28
        for j in range(n):
            a = j / n * 2 * math.pi
            pv.append((math.cos(a) * sx, math.sin(a) * sy, -0.94))
        pv.append((0, 0, -0.95))
        for j in range(n):
            pf.append(((j + 1) % n, j, n))
        out.append(mesh_from("Eye_Pupil_" + name, pv, pf, [pupil_m]))
    lv, lf = [], []
    RS, TS = 36, 12
    rnd = random.Random(5)
    ph = [rnd.uniform(0, 6.28) for _ in range(2)]
    for i in range(RS):
        a = i / RS * 2 * math.pi
        swell = 1 + 0.12 * math.sin(2 * a + ph[0]) + 0.06 * math.sin(5 * a + ph[1])
        for j in range(TS):
            b = j / TS * 2 * math.pi
            rr = 0.88 + 0.2 * swell * math.cos(b)
            lv.append((math.cos(a) * rr, math.sin(a) * rr * 1.08, -0.5 - 0.22 * swell * math.sin(b)))
    for i in range(RS):
        for j in range(TS):
            a, b = i * TS + j, i * TS + (j + 1) % TS
            c, d = ((i + 1) % RS) * TS + j, ((i + 1) % RS) * TS + (j + 1) % TS
            lf.append((a, b, d, c))
    out.append(mesh_from("Eye_Lid", lv, lf, [lid_m]))
    return out


# ------------------------------------------------------------------ the Leviathan's hide


def build_hide(hide_m):
    with open(DATA) as fh:
        d = json.load(fh)
    grid = np.array(d["radius"], np.float64)   # [theta 0..180 by 2][phi 0..360 by 2]
    eyes = d["eyes"]

    def radius(v):
        th = math.degrees(math.acos(max(-1.0, min(1.0, v[1])))) / 2.0
        ph = (math.degrees(math.atan2(v[2], v[0])) % 360.0) / 2.0
        t0, p0 = int(th), int(ph)
        t1, p1 = min(t0 + 1, grid.shape[0] - 1), min(p0 + 1, grid.shape[1] - 1)
        ft, fp = th - t0, ph - p0
        return (grid[t0, p0] * (1 - ft) * (1 - fp) + grid[t1, p0] * ft * (1 - fp) + grid[t0, p1] * (1 - ft) * fp + grid[t1, p1] * ft * fp)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=7, radius=1.0)
    eye_dirs = [(Vector(e[:3]).normalized(), Vector(e[:3]), e[3]) for e in eyes]
    rnd = random.Random(1818)
    warts = [(Vector((rnd.uniform(-1, 1), rnd.uniform(-0.2, 1), rnd.uniform(-1, 1))).normalized(), rnd.uniform(0.25, 0.7)) for _ in range(140)]
    for v in bm.verts:
        dvec = Vector((v.co.x, v.co.y, v.co.z)).normalized()   # (built in game space here; turned at the end)
        r = radius(dvec)
        p = dvec * r
        # folds: long soft ridges, wrinkles over them
        fold = noise.ridged_multi_fractal(p * 0.14, 1.0, 2.0, 4, 0.0, 1.0) * 0.35
        wr = noise.noise(p * 1.4) * 0.06 + noise.noise(p * 3.1) * 0.025
        sag = -0.25 * max(0.0, -dvec.y) ** 2   # it sags into the blood
        out = fold + wr + sag
        # sores and warts over the upper hide
        for wd, ws in warts:
            c = dvec.dot(wd)
            if c > 0.995:
                ang = math.acos(min(1.0, c)) * r
                out += ws * 0.5 * math.exp(-(ang / (ws * 0.9)) ** 2)
        # each eye's socket: sunk in, a swollen lid round it
        for ed, ep, es in eye_dirs:
            dist = (p - ep).length
            if dist < es * 2.6:
                out += -0.55 * es * math.exp(-(dist / (es * 0.95)) ** 2) + 0.22 * es * math.exp(-((dist - es * 1.15) / (es * 0.35)) ** 2)
        v.co = dvec * (r + out)
    verts = [(v.co.x, v.co.y, v.co.z) for v in bm.verts]
    faces = [tuple(vv.index for vv in f.verts) for f in bm.faces]
    bm.free()
    ob = mesh_from("LeviathanHide", verts, faces, [hide_m])
    add_mod(ob, "SMOOTH", factor=0.5, iterations=2)
    apply_mods(ob)
    print(f"[tentacle] LeviathanHide: {len(ob.data.polygons)} faces, {len(eyes)} sockets")
    return ob


def main():
    clear()
    flesh = mat("TentFlesh", (0.35, 0.12, 0.14), 0.4)
    rim = mat("SuckerRim", (0.5, 0.17, 0.19), 0.15)
    hole = mat("SuckerHole", (0.14, 0.01, 0.03), 0.08)
    maw_flesh = mat("MawFlesh", (0.35, 0.12, 0.14), 0.4)
    throat = mat("Throat", (0.22, 0.02, 0.04), 0.1)
    tooth = mat("Tooth", (0.78, 0.72, 0.5), 0.3)
    sclera = mat("Sclera", (0.62, 0.5, 0.42), 0.18)
    iris = mat("Iris", (0.3, 0.035, 0.03), 0.08)
    pupil = mat("Pupil", (0.01, 0.005, 0.005), 0.05)
    hide = mat("Hide", (0.3, 0.22, 0.28), 0.35)
    objs = [
        build_limb("Limb", 12.0, 11, 300, 72, flesh, rim, hole),
        build_limb("LimbLong", 20.0, 23, 420, 72, flesh, rim, hole),
        build_limb("LimbWhip", 70.0, 37, 420, 32, flesh, rim, hole),
        build_maw(maw_flesh, throat, tooth),
    ]
    objs += build_eye_parts(sclera, iris, pupil, mat("Lid", (0.3, 0.06, 0.08), 0.15))
    if os.path.exists(DATA):
        objs.append(build_hide(hide))
    else:
        print(f"[tentacle] no {DATA}: no hide (run creature_preview -- --leviathan --dump-leviathan)")
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    path = os.path.join(OUT, "tentacle.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_animations=False, export_materials="EXPORT", export_normals=True, export_colors=True)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)
    print(f"[tentacle] exported {path}: {len(objs)} meshes, {tris} triangles")


if __name__ == "__main__":
    main()
