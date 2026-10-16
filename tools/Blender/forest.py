"""The woods' scatter, modelled in Blender (the owner, 2026-10-15: "from now on everything should be made in blender"). Every
mesh ForestScatter instances by the thousand (firs, broadleaves, snags, saplings, stumps, logs, branches, rocks, boulders,
ferns, grass, leaf litter, vine mounds, palmettos, the board pile), plus the photo trip's wildflower and mushroom clusters
and Act 6's giant firs, each exported to assets/models/forest/<key>.glb under the same key the scatter uses:

    blender -b --python tools/Blender/forest.py [-- key ...]

They keep the conventions the game's materials expect, so the scatter, its colliders, its instance colours and its shaders
all go on working unchanged:
- material slots are named for the game's materials (tree_bark, bark, fir_branch, fir_spray, endgrain, tree_endgrain,
  moss_rock, tree_leaf, ivy, vine_core, moss, fern, fern_leaf, grass, litter, wood, metal, palmetto); the game swaps its own in by name
  (scripts/world/ScatterModels.cs);
- UVs as the game laid them (bark: u twice round, v half a unit per metre; foliage cards: v 1 at the root, 0 at the tip, which
  the foliage shader's sway reads); vertex colours as the game's MeshKit gave them (FLOAT_COLOR, written unconverted);
- the same sizes and footprints (the scatter's colliders are fitted to them).

What Blender does better than the old C# kit: trunks lofted through interpolated rings with more sides, a bark relief from
noise and smooth normals across the whole tube; rocks, boulders and leaf clumps as noise-displaced spheres instead of
randomly jittered icospheres; logs and stumps as rough lofts with real ends.

Blender is Z-up; the glTF export turns it Y-up with Blender's +Y becoming the game's -Z. Points are given in the game's
frame here and converted as each vertex is written (game x, y, z -> Blender x, -z, y).
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "assets", "models", "forest")
os.makedirs(OUT, exist_ok=True)

TAU = math.tau
UP = Vector((0, 1, 0))
DOWN = Vector((0, -1, 0))
RIGHT = Vector((1, 0, 0))

# preview colours only (the game swaps its own materials in by name)
PREVIEW = {
    "tree_bark": (0.32, 0.27, 0.22), "bark": (0.35, 0.3, 0.24), "fir_branch": (0.08, 0.13, 0.08), "fir_spray": (0.1, 0.18, 0.09),
    "endgrain": (0.55, 0.45, 0.32), "tree_endgrain": (0.55, 0.45, 0.32), "moss_rock": (0.4, 0.42, 0.36), "tree_leaf": (0.2, 0.3, 0.12),
    "ivy": (0.12, 0.2, 0.08), "vine_core": (0.05, 0.08, 0.035), "moss": (0.34, 0.37, 0.3), "fern": (0.2, 0.32, 0.12), "fern_leaf": (0.15, 0.2, 0.08),
    "grass": (0.3, 0.38, 0.15), "litter": (0.3, 0.24, 0.16), "wood": (0.4, 0.34, 0.26), "metal": (0.42, 0.26, 0.16),
    "palmetto": (0.3, 0.38, 0.2), "stem": (0.25, 0.35, 0.15), "petal": (0.8, 0.8, 0.8), "cap": (0.5, 0.32, 0.18),
    "gill": (0.7, 0.62, 0.5), "fleck": (0.82, 0.78, 0.66), "giant_trunk": (0.32, 0.26, 0.2), "giant_needle": (0.06, 0.1, 0.07),
}


def V(x, y, z):
    return Vector((x, y, z))


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a))) if b != a else 0.0
    return t * t * (3 - 2 * t)


class Kit:
    """MeshKit's model, in Python: triangles with a normal, a UV and a colour at each corner, per material."""

    def __init__(self):
        self.surf = {}
        self.cur = None
        self.color = (1.0, 1.0, 1.0, 1.0)

    def mat(self, name):
        self.cur = self.surf.setdefault(name, [])
        return self

    def col(self, r, g=None, b=None):
        if g is None:
            g = b = r
        self.color = (r, g, b, 1.0)
        return self

    def tri(self, a, b, c, na, nb, nc, ua, ub, uc, ca=None, cb=None, cc=None):
        face = (b - a).cross(c - a)
        if face.length_squared < 4e-14:
            return
        ca = ca or self.color
        cb = cb or self.color
        cc = cc or self.color
        # the game's front faces wind the other way from glTF's: here the face's normal must agree with the given ones
        if face.dot(na + nb + nc) < 0:
            b, c, nb, nc, ub, uc, cb, cc = c, b, nc, nb, uc, ub, cc, cb
        self.cur.append(((a, na.normalized(), ua, ca), (b, nb.normalized(), ub, cb), (c, nc.normalized(), uc, cc)))

    def quad(self, a, b, c, d, n, ua=(0, 1), ub=(1, 1), uc=(1, 0), ud=(0, 0)):
        self.tri(a, b, c, n, n, n, ua, ub, uc)
        self.tri(a, c, d, n, n, n, ua, uc, ud)

    def cylinder(self, p0, p1, r0, r1, sides, caps=False, uvs=1.0, rough=0.0, seed=0):
        axis = p1 - p0
        ln = axis.length
        if ln < 1e-6:
            return
        z = axis / ln
        refv = RIGHT if abs(z.y) > 0.9 else UP
        x = refv.cross(z).normalized()
        y = z.cross(x)
        circ = TAU * max(r0, r1)
        slope = (r0 - r1) / ln
        rings = max(1, int(ln / 0.35)) if rough > 0 else 1
        def P(i, j):
            f = j / rings
            a = TAU * i / sides
            d = x * math.cos(a) + y * math.sin(a)
            r = lerp(r0, r1, f)
            if rough > 0 and r > 1e-4:
                q = p0 + axis * f + d * r
                r *= 1 + rough * noise.noise(q * 6.0 + Vector((seed, seed * 0.3, 0)))
            return p0 + axis * f + d * r, (d + z * slope).normalized()
        for j in range(rings):
            for i in range(sides):
                a, na = P(i, j)
                b, nb = P(i + 1, j)
                c, nc = P(i + 1, j + 1)
                d, nd = P(i, j + 1)
                u0, u1 = i / sides * circ * uvs, (i + 1) / sides * circ * uvs
                v0, v1 = (1 - j / rings) * ln * uvs, (1 - (j + 1) / rings) * ln * uvs
                last = j == rings - 1
                self.tri(a, b, c, na, nb, nc, (u0, v0), (u1, v0), (u1, v1))
                if not (last and r1 <= 1e-4):
                    self.tri(a, c, d, na, nc, nd, (u0, v0), (u1, v1), (u0, v1))
        if caps:
            for i in range(sides):
                a0, a1 = TAU * i / sides, TAU * (i + 1) / sides
                d0 = x * math.cos(a0) + y * math.sin(a0)
                d1 = x * math.cos(a1) + y * math.sin(a1)
                cu = lambda dd, r: (0.5 + dd.dot(x) * r * uvs, 0.5 + dd.dot(y) * r * uvs)
                self.tri(p0, p0 + d0 * r0, p0 + d1 * r0, -z, -z, -z, (0.5, 0.5), cu(d0, r0), cu(d1, r0))
                if r1 > 1e-4:
                    self.tri(p1, p1 + d0 * r1, p1 + d1 * r1, z, z, z, (0.5, 0.5), cu(d0, r1), cu(d1, r1))

    def loft(self, rings, sides, uvs=1.0, lobes=0.0, seed=0, relief=0.0, sub=2, flare=None):
        """A trunk as one smooth tube through rings [(centre, radius, colour)] (bottom to top), interpolated (Catmull-Rom)
        to `sub` times the rings, with low lobes round it (strongest at the foot) and a bark relief from noise; normals the
        surface's own; bark UVs running on unbroken (u twice round, v half a unit per metre). `flare` (root_flare's) swells
        the foot out into root buttresses running down into the ground, part of the same tube: the roots were separate
        lofts stuck into the trunk, and where they went in the owner saw a dark line round the trunk (2026-10-15)."""
        if len(rings) < 2:
            return
        def cr(p0, p1, p2, p3, t):
            return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)
        R = []
        n0 = len(rings)
        for i in range(n0 - 1):
            for s in range(sub):
                t = s / sub
                g = lambda k: rings[max(0, min(n0 - 1, k))]
                c = cr(g(i - 1)[0], g(i)[0], g(i + 1)[0], g(i + 2)[0], t)
                r = lerp(g(i)[1], g(i + 1)[1], t)
                col = tuple(lerp(g(i)[2][k], g(i + 1)[2][k], t) for k in range(3)) + (1.0,)
                R.append((c, r, col))
        R.append((rings[-1][0], rings[-1][1], tuple(rings[-1][2][:3]) + (1.0,)))
        n = len(R)
        tan = [(R[min(i + 1, n - 1)][0] - R[max(i - 1, 0)][0]).normalized() for i in range(n)]
        bx = tan[0].cross(RIGHT if abs(tan[0].y) > 0.9 else UP).normalized()
        dirs = []
        for i in range(n):
            if i > 0:
                xx = bx - tan[i] * bx.dot(tan[i])
                bx = xx.normalized() if xx.length_squared > 1e-8 else tan[i].cross(RIGHT if abs(tan[i].y) > 0.9 else UP).normalized()
            bz = bx.cross(tan[i]).normalized()
            dirs.append([bx * math.cos(TAU * s / sides) + bz * math.sin(TAU * s / sides) for s in range(sides)])
        rnd = random.Random(seed * 977 + 13)
        ph = [rnd.uniform(0, TAU) for _ in range(3)]
        off = Vector((rnd.uniform(0, 50), rnd.uniform(0, 50), rnd.uniform(0, 50)))
        pts = []
        for i in range(n):
            up = i / (n - 1)
            amp = lobes * (0.3 + 0.7 * (1 - up) ** 2)
            row = []
            for s in range(sides):
                a = TAU * s / sides
                lobe = amp * (0.5 * math.sin(3 * a + ph[0]) + 0.3 * math.sin(5 * a + ph[1]) + 0.35 * math.sin(2 * a + ph[2])) if lobes > 0 else 0.0
                if flare:
                    # the buttresses: by the axis's height (not the ring's index), so the interpolated rings swell smoothly
                    t = max(0.0, min(1.1, (flare["top"] - R[i][0].y) / flare["top"]))  # below ground the roots dive, not skim it
                    if t > 0:
                        w = max(flare["w"], 0.75 * TAU / sides)
                        bump = 0.0
                        for (ra, reach) in flare["roots"]:
                            da = (a - ra + math.pi) % TAU - math.pi
                            bump += reach * math.exp(-(da / w) ** 2)
                        lobe += t * t * bump
                p = R[i][0] + dirs[i][s] * R[i][1] * (1 + lobe)
                if relief > 0 and R[i][1] > 0.02:
                    # the bark's ridges run along the trunk: stretched noise
                    q = Vector((p.x * 9, p.y * 1.6, p.z * 9)) + off
                    p = p + dirs[i][s] * R[i][1] * relief * noise.noise(q)
                row.append(p)
            pts.append(row)
        nrm = []
        for i in range(n):
            row = []
            for s in range(sides):
                rnd_ = pts[i][(s + 1) % sides] - pts[i][(s - 1) % sides]
                along = pts[min(i + 1, n - 1)][s] - pts[max(i - 1, 0)][s]
                nn = along.cross(rnd_)
                if nn.length_squared < 1e-12:
                    nn = dirs[i][s].copy()
                nn.normalize()
                if nn.dot(dirs[i][s]) < 0:
                    nn = -nn
                row.append((nn * 0.85 + dirs[i][s] * 0.15).normalized())
            nrm.append(row)
        v0 = 0.0
        for i in range(n - 1):
            v1 = v0 + (R[i][0] - R[i + 1][0]).length * uvs * 0.5
            c0, c1 = R[i][2], R[i + 1][2]
            for s in range(sides):
                s1 = (s + 1) % sides
                u0, u1 = s / sides * 2, (s + 1) / sides * 2
                p00, p01, p10, p11 = pts[i][s], pts[i][s1], pts[i + 1][s], pts[i + 1][s1]
                n00, n01, n10, n11 = nrm[i][s], nrm[i][s1], nrm[i + 1][s], nrm[i + 1][s1]
                self.tri(p00, p10, p11, n00, n10, n11, (u0, v0), (u0, v1), (u1, v1), c0, c1, c1)
                self.tri(p00, p11, p01, n00, n11, n01, (u0, v0), (u1, v1), (u1, v0), c0, c1, c0)
            v0 = v1
        self.color = R[-2][2]

    def blob(self, c, radii, seed, rough=0.22, flat_bottom=False, uvs=1.0, shade_bottom=0.0, subdiv=3, scale=1.4):
        """A noise-displaced sphere (a rock, a boulder, a leaf clump), smooth-shaded."""
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
        off = Vector((seed * 1.37 % 97, seed * 0.71 % 53, seed * 2.13 % 31))
        P = {}
        for v in bm.verts:
            d = v.co.normalized()
            k = 1 + rough * (noise.noise(d * scale + off) + 0.5 * noise.noise(d * scale * 2.3 + off * 1.7))
            p = Vector((d.x * radii.x, d.y * radii.y, d.z * radii.z)) * k
            if flat_bottom and p.y < -radii.y * 0.55:
                p.y = -radii.y * 0.55 + (p.y + radii.y * 0.55) * 0.15
            P[v.index] = c + p
        # smooth normals from the displaced surface
        N = {v.index: Vector((0, 0, 0)) for v in bm.verts}
        for f in bm.faces:
            a, b, d = (P[v.index] for v in f.verts)
            fn = (b - a).cross(d - a)
            for v in f.verts:
                N[v.index] += fn
        base = self.color
        for f in bm.faces:
            ids = [v.index for v in f.verts]
            a, b, d = (P[i] for i in ids)
            ns = []
            for i in ids:
                nn = N[i].normalized() if N[i].length_squared > 1e-12 else (P[i] - c).normalized()
                if nn.dot(P[i] - c) < 0:
                    nn = -nn
                ns.append(nn)
            def U(p):
                return ((p.x + p.z * 0.7) * uvs, (p.y + p.z * 0.3) * uvs)
            if shade_bottom > 0:
                hy = sum(((P[i] - c).y / max(radii.y, 1e-4)) for i in ids) / 3 * 0.5 + 0.5
                k = 1 - shade_bottom * (1 - max(0.0, min(1.0, hy)))
                self.color = (base[0] * k, base[1] * k, base[2] * k, 1.0)
            self.tri(a, b, d, ns[0], ns[1], ns[2], U(a), U(b), U(d))
        self.color = base
        bm.free()

    def box(self, c, s, rot, uvs=1.0):
        """A box centred c, full size s, rotated by the 3x3 rot (columns = local axes)."""
        h = s * 0.5
        ax, ay, az = rot
        def P(x, y, z):
            return c + ax * (x * h.x) + ay * (y * h.y) + az * (z * h.z)
        u = uvs
        self.quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), ay, (0, 0), (s.x * u, 0), (s.x * u, s.z * u), (0, s.z * u))
        self.quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -ay, (0, 0), (s.x * u, 0), (s.x * u, s.z * u), (0, s.z * u))
        self.quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), az, (0, s.y * u), (s.x * u, s.y * u), (s.x * u, 0), (0, 0))
        self.quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -az, (0, s.y * u), (s.x * u, s.y * u), (s.x * u, 0), (0, 0))
        self.quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), ax, (0, s.y * u), (s.z * u, s.y * u), (s.z * u, 0), (0, 0))
        self.quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -ax, (0, s.y * u), (s.z * u, s.y * u), (s.z * u, 0), (0, 0))

    # ---------------------------------------------------------------- to Blender, and out

    def build(self, name):
        def G(p):
            return Vector((p.x, -p.z, p.y))
        me = bpy.data.meshes.new(name)
        verts, faces, loops_n, loops_uv, loops_col, fmat = [], [], [], [], [], []
        names = [k for k, v in self.surf.items() if v]
        for mi, mname in enumerate(names):
            for t in self.surf[mname]:
                base = len(verts)
                for (p, n, uv, c) in t:
                    verts.append(G(p))
                    loops_n.append(G(n))
                    loops_uv.append((uv[0], 1.0 - uv[1]))
                    loops_col.append(c)
                faces.append((base, base + 1, base + 2))
                fmat.append(mi)
        me.from_pydata([tuple(v) for v in verts], [], faces)
        for mname in names:
            m = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
            m.diffuse_color = (*PREVIEW.get(mname, (0.5, 0.5, 0.5)), 1.0)
            me.materials.append(m)
        for i, p in enumerate(me.polygons):
            p.material_index = fmat[i]
            p.use_smooth = True
        uv = me.uv_layers.new(name="UVMap")
        for i, l in enumerate(me.loops):
            uv.data[i].uv = loops_uv[l.vertex_index]
        ca = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
        for i, l in enumerate(me.loops):
            ca.data[i].color = loops_col[l.vertex_index]
        me.color_attributes.active_color = ca
        try:
            me.color_attributes.render_color_index = me.color_attributes.find("Col")
        except Exception:
            pass
        if hasattr(me, "use_auto_smooth"):
            me.use_auto_smooth = True
        me.normals_split_custom_set([tuple(loops_n[l.vertex_index]) for l in me.loops])
        me.validate(clean_customdata=False)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def export(ob, key):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    path = os.path.join(OUT, key + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_texcoords=True, export_normals=True, export_colors=True, export_materials="EXPORT",
                              export_tangents=False)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[forest] {key}: {tris} tris -> {os.path.relpath(path, ROOT)}")


# =================================================================== the trees


def spray(k, at, d, ln, w, roll, sag, lum, crossed=False):
    d = d.normalized()
    s = d.cross(UP)
    s = RIGHT.copy() if s.length_squared < 1e-4 else s.normalized()
    up = s.cross(d).normalized()
    tip = at + d * ln + DOWN * ln * (0.15 + 0.3 * sag)
    k.col(lum)
    def card(r, wk):
        sr = s * math.cos(r) + up * math.sin(r)
        n = (tip - at).cross(sr).normalized()
        if n.y < 0:
            n = -n
        wb, wt = w * wk * 0.8, w * wk * 1.2
        k.quad(at - sr * wb * 0.5, at + sr * wb * 0.5, tip + sr * wt * 0.5, tip - sr * wt * 0.5, n, (0, 1), (1, 1), (1, 0), (0, 0))
    card(roll, 1.0)
    if crossed:
        card(roll + math.pi * 0.5, 0.7)


def root_flare(seed, top=1.0):
    """4-6 root buttresses round the foot, for loft(flare=): each an angle and how far it swells (times the radius at
    ground level), fading out by `top` metres up and running on down below the ground."""
    rnd = random.Random(seed * 6007 + 17)
    n = rnd.randint(4, 6)
    a0 = rnd.uniform(0, TAU)
    return {"top": top, "w": 0.3, "roots": [(a0 + TAU * i / n + rnd.uniform(-0.3, 0.3), rnd.uniform(0.9, 1.5)) for i in range(n)]}


def moss_card(k, at, ln, width, rnd):
    keep_cur, keep_col = k.cur, k.color
    k.mat("moss")
    s = rnd.uniform(0.85, 1.1)
    k.col(s, s, s * 0.95)
    a = rnd.uniform(0, math.pi)
    for i in range(2):
        ai = a + i * math.pi * 0.5
        side = V(math.cos(ai), 0, math.sin(ai)) * width * 0.5
        n = V(-side.z, 0, side.x).normalized()
        bottom = at + DOWN * ln + V(rnd.uniform(-0.08, 0.08), 0, rnd.uniform(-0.08, 0.08))
        k.quad(bottom - side * 0.8, bottom + side * 0.8, at + side, at - side, n, (0, 0), (1, 0), (1, 1), (0, 1))
    k.cur, k.color = keep_cur, keep_col


def fir_crown(k, rnd, axis, height, trunkR, crownStart, tiers, maxR, ragged, spray_mat="fir_spray"):
    y0 = crownStart * height
    for t in range(tiers):
        f = t / (tiers - 1)
        y = lerp(y0, height * 0.9, f ** 0.92) + rnd.uniform(-0.25, 0.25)
        profile = lerp(1.0, 0.16, f ** 1.1) * lerp(0.82, 1.0, smoothstep(0, 0.25, f)) * rnd.uniform(0.82, 1.15)
        R = maxR * profile
        n = max(4, round(lerp(11, 5, f)))
        droop = lerp(0.7, 0.35, f)
        rot = rnd.uniform(0, TAU)
        ao = lerp(0.55, 1.0, f)
        c = axis(y)
        if y > 3.4 and R > 1.4:
            k.mat("fir_branch")
            cr = R * 0.42
            drop = cr * max(math.tan(droop * 0.7), 0.8)
            cs = 9
            k.col(ao * 0.8)
            apex = c + UP * 0.3
            H = lambda s, salt: abs(math.sin((t * 12.9898 + s * 78.233 + salt) * 43.758) * 137.5) % 1.0
            rim = []
            for s in range(cs):
                a0 = rot + TAU * s / cs
                rr = cr * (0.68 + 0.32 * H(s, y))
                rim.append(c + V(math.cos(a0) * rr, -drop * (0.75 + 0.4 * H(s, 3.1 + y)), math.sin(a0) * rr))
            for s in range(cs):
                if H(s, 7.7 + y) < 0.18:
                    continue
                p0, p1 = rim[s], rim[(s + 1) % cs]
                nn = ((p0 + p1 - 2 * c).normalized() + UP).normalized()
                k.tri(apex, p0, p1, nn, nn, nn, (0.5, 0.99), (0.15, 0.55), (0.85, 0.55))
        for i in range(n):
            if rnd.random() < ragged * 0.5 and t < tiers - 2:
                continue
            a = rot + TAU * i / n + rnd.uniform(-0.25, 0.25) * 1.7
            L = R * rnd.uniform(0.78, 1.12)
            d = V(math.cos(a), 0, math.sin(a))
            side = V(-d.z, 0, d.x)
            dr = droop * rnd.uniform(0.75, 1.3)
            pitch = rnd.uniform(-0.12, 0.1)
            root = c + UP * rnd.uniform(-0.35, 0.45)
            sag = math.tan(min(dr * 0.7 + pitch, 0.9))
            P = lambda u: root + d * (trunkR * 0.3 + L * u) + DOWN * (L * u * u * sag * 0.75)
            Dir = lambda u: (P(u + 0.05) - P(u)).normalized()
            lum = ao * rnd.uniform(0.85, 1.1)
            if f < 0.65 and L >= 1.0:
                # the limb: one smooth tube along the bough's own curve, ending inside its needles
                k.mat("tree_bark")
                lc = tuple(x * lerp(0.8, 1.0, ao) for x in (0.5, 0.48, 0.46)) + (1.0,)
                lr = max(0.025, min(trunkR * 0.16, 0.09))
                k.loft([(root - d * trunkR * 0.2, lr, lc), (P(0.2), lr * 0.75, lc), (P(0.35), lr * 0.5, lc), (P(0.48), 0.012, lc)], 4, 2.0, sub=1)
            k.mat(spray_mat)
            w = max(0.5, L * 0.42)
            spray(k, P(0.42), Dir(0.5), L * 0.58, w * 1.1, rnd.uniform(-0.3, 0.3), sag, lum, f < 0.3 and L > 1.6)
            if L < 1.1:
                continue
            lum2 = lum * 0.85
            d0 = Dir(0.3)
            spray(k, P(0.22), (d0 * 0.6 + side * 0.8).normalized(), L * 0.5, w * 0.85, rnd.uniform(0.15, 0.45), sag, lum2)
            spray(k, P(0.22), (d0 * 0.6 - side * 0.8).normalized(), L * 0.5, w * 0.85, -rnd.uniform(0.15, 0.45), sag, lum2)


def fir(seed, height, trunkR, crownStart, tiers, maxR, ragged, moss=0.0):
    rnd = random.Random(seed * 7919)
    k = Kit()
    def axis(y):
        f = y / height
        return V(math.sin(f * 2.1 + seed) * 0.18 * f, y, math.cos(f * 1.7 + seed * 2) * 0.14 * f)
    topY = height * 0.93
    k.mat("tree_bark")
    rings = [(V(0, -0.5, 0), trunkR * 1.55, (0.56, 0.54, 0.52)), (V(0, -0.12, 0), trunkR * 1.42, (0.58, 0.56, 0.54)),
             (V(0, 0.22, 0), trunkR * 1.2, (0.6, 0.58, 0.56)), (axis(0.6), trunkR * 1.06, (0.62, 0.6, 0.58)), (axis(1.2), trunkR, (0.62, 0.6, 0.58))]
    for fy in (0.3, 0.46, 0.62, 0.78, 1.0):
        y = topY * fy
        rings.append((axis(y), trunkR * lerp(1.02, 0.08, y / topY), tuple(lerp(a, b, fy) for a, b in zip((0.62, 0.6, 0.58), (0.68, 0.66, 0.63)))))
    k.loft(rings, max(12, min(22, round(12 + trunkR * 18))), 1.0, 0.07, seed, relief=0.05, sub=2, flare=root_flare(seed, max(0.8, trunkR * 2.4)))
    mrnd = random.Random(seed * 977 + 13)
    stubs = round(crownStart * height * 0.55)
    k.col(0.5, 0.48, 0.46)
    for _ in range(stubs):
        y = rnd.uniform(min(2.4, crownStart * height * 0.5), crownStart * height)
        a = rnd.uniform(0, TAU)
        dd = V(math.cos(a), rnd.uniform(-0.5, 0.05), math.sin(a)).normalized()
        ln = rnd.uniform(0.35, 1.1) * max(0.5, min(trunkR * 2.5, 1.3))
        fr = axis(y)
        k.mat("tree_bark").cylinder(fr, fr + dd * ln, 0.045, 0.008, 4, False, 2.0)
        if moss > 0 and mrnd.random() < moss:
            moss_card(k, fr + dd * ln * mrnd.uniform(0.5, 0.95), mrnd.uniform(0.5, 1.3), mrnd.uniform(0.25, 0.45), mrnd)
    fir_crown(k, rnd, axis, height, trunkR, crownStart, tiers, maxR, ragged)
    k.mat("fir_spray")
    lh = max(1.4, height * 0.09)
    b = axis(height * 0.86)
    for i in range(3):
        a = i * TAU / 3 + seed
        lean = V(math.cos(a), 0, math.sin(a)) * (0.12 if i == 0 else 0.4)
        lum = 0.9 if i == 0 else 0.8
        spray(k, b + UP * (0.1 if i == 0 else 0.0), UP + lean, lh * (1.0 if i == 0 else 0.7), lh * 0.42, a, 0.0, lum, i == 0)
    return k


def deciduous(seed):
    rnd = random.Random(seed * 104729)
    k = Kit()
    base = (0.6, 0.58, 0.55)
    k.col(*base)
    lean = rnd.uniform(-0.25, 0.25)
    top = V(lean, 4.2, rnd.uniform(-0.2, 0.2))
    k.mat("tree_bark")
    k.loft([(V(0, -0.4, 0), 0.34, base), (V(0, 0, 0), 0.29, base), (V(0, 0.35, 0), 0.245, base), (top * 0.35 + V(0, 0.1, 0), 0.2, base), (top, 0.13, base),
            (top + V(0, 0.8, 0), 0.09, base), (top + V(0, 1.6, 0), 0.05, base)],
           18, 1.0, 0.06, seed, relief=0.05, flare=root_flare(seed, 0.8))
    k.col(*base)
    for _ in range(3):
        a = rnd.uniform(0, TAU)
        fr = top * rnd.uniform(0.55, 0.9)
        to = fr + V(math.cos(a) * 1.6, rnd.uniform(1.0, 1.8), math.sin(a) * 1.6)
        k.mat("tree_bark").loft([(fr, 0.09, base), ((fr + to) * 0.5 + V(0, 0.15, 0), 0.065, base), (to, 0.04, base)], 7, 1.0, sub=3)
    k.mat("tree_leaf")
    for i in range(9):
        a = TAU * i / 9 * 2.3 + rnd.uniform(-0.4, 0.4)
        d = rnd.uniform(0, 0.4) if i < 2 else rnd.uniform(0.8, 1.9)
        c = top + V(math.cos(a) * d, 1.2 + i * 1.3 if i < 2 else rnd.uniform(-0.2, 2.4), math.sin(a) * d)
        s = rnd.uniform(0.7, 1.0)
        k.col(s, s, s * 0.95)
        r = rnd.uniform(0.7, 1.15)
        k.blob(c, V(r, r * rnd.uniform(0.9, 1.25), r * rnd.uniform(0.8, 1.1)), seed * 13 + i, 0.2, False, 0.6, 0.6, 2, 1.8)
    return k


def snag(seed, h, moss=0.0):
    rnd = random.Random(seed * 31337)
    k = Kit()
    r = 0.2 + h * 0.012
    top = V(0.02 * h, h, 0.01 * h)
    k.mat("tree_bark")
    k.loft([(V(0, -0.4, 0), r * 1.5, (0.56, 0.55, 0.54)), (V(0, 0, 0), r * 1.3, (0.58, 0.57, 0.56)), (V(0, 0.4, 0), r * 1.1, (0.58, 0.57, 0.56)),
            (top * 0.5, r * 0.7, (0.62, 0.61, 0.59)), (top, r * 0.25, (0.66, 0.65, 0.62))], 18, 1.0, 0.06, seed, relief=0.08, flare=root_flare(seed, 0.9))
    k.col(0.62, 0.6, 0.56)
    k.mat("tree_endgrain").cylinder(top, top + V(0.05, 0.08, 0), r * 0.25, 0.02, 7, False, 1.0)
    mrnd = random.Random(seed * 977 + 29)
    nb = round(h * 0.7)
    bc = (0.6, 0.58, 0.55)
    for _ in range(nb):
        y = rnd.uniform(h * 0.3, h * 0.92)
        a = rnd.uniform(0, TAU)
        ln = rnd.uniform(0.5, 1.8) * (1.25 - y / h)
        fr = top * (y / h)
        end = fr + V(math.cos(a) * ln, rnd.uniform(-0.5, 0.2) * ln, math.sin(a) * ln)
        k.mat("tree_bark").loft([(fr, 0.055, bc), ((fr + end) * 0.5 + V(0, 0.03, 0), 0.035, bc), (end, 0.01, bc)], 5, 1.0, sub=2)
        if moss > 0 and mrnd.random() < moss:
            moss_card(k, fr.lerp(end, mrnd.uniform(0.4, 0.9)), mrnd.uniform(0.6, 1.6), mrnd.uniform(0.3, 0.5), mrnd)
    return k


def sapling(seed, height):
    rnd = random.Random(seed * 6007)
    k = Kit()
    r = rnd.uniform(0.05, 0.075)
    lean = V(rnd.uniform(-0.4, 0.4), 0, rnd.uniform(-0.4, 0.4))
    def axis(f):
        return V(0, f * height, 0) + lean * f * f + V(math.sin(f * 5 + seed) * 0.12, 0, math.cos(f * 4 + seed) * 0.1) * f
    k.mat("tree_bark")
    rings = []
    for i in range(8):
        f = i / 7
        lich = rnd.uniform(1.15, 1.5) if rnd.random() < 0.55 else rnd.uniform(0.62, 0.8)
        rings.append((axis(f) + (DOWN * 0.3 if i == 0 else V(0, 0, 0)), r * lerp(1.15, 0.25, f), (0.62 * lich, 0.66 * lich, 0.58 * lich)))
    k.loft(rings, 10, 1.0, sub=2)
    tips = []
    for _ in range(7):
        f = rnd.uniform(0.45, 0.95)
        a = rnd.uniform(0, TAU)
        fr = axis(f)
        d = V(math.cos(a), rnd.uniform(0.3, 0.9), math.sin(a)).normalized()
        to = fr + d * rnd.uniform(0.5, 1.2)
        k.mat("tree_bark").col(0.55, 0.56, 0.5)
        k.loft([(fr, r * 0.35, (0.55, 0.56, 0.5)), ((fr + to) * 0.5 + V(0, 0.04, 0), r * 0.22, (0.55, 0.56, 0.5)), (to, 0.006, (0.55, 0.56, 0.5))], 5, 1.0, sub=2)
        tips.append((fr.lerp(to, 0.6), d))
        tips.append((to, d))
    tips.append((axis(1.0), UP))
    k.mat("ivy")
    leaves(k, rnd, tips, 9, (0.1, 0.17))
    return k


def leaves(k, rnd, tips, per, size):
    for at, d in tips:
        for _ in range(per):
            p = at + V(rnd.uniform(-0.3, 0.3), rnd.uniform(-0.2, 0.25), rnd.uniform(-0.3, 0.3))
            nrm = (UP * 0.6 + V(rnd.uniform(-1, 1), rnd.uniform(-0.2, 0.6), rnd.uniform(-1, 1))).normalized()
            t = (d + V(rnd.uniform(-0.6, 0.6), -0.4, rnd.uniform(-0.6, 0.6))).normalized()
            t = (t - nrm * t.dot(nrm))
            if t.length_squared < 0.25:
                continue
            t.normalize()
            side = nrm.cross(t).normalized()
            sz = rnd.uniform(*size)
            hs = sz * 0.5
            yel = rnd.uniform(0.4, 0.9) if rnd.random() < 0.3 else 0.0
            l = rnd.uniform(0.85, 1.15)
            base = (l, l, l * 0.9)
            k.col(*(lerp(base[i], (l * 2.2, l * 1.6, l * 0.4)[i], yel) for i in range(3)))
            cell = 3 if rnd.random() < 0.6 else (1 if rnd.random() < 0.5 else 0)
            u0, v0 = (cell % 2) * 0.5, (cell // 2) * 0.5
            k.quad(p - side * hs, p + side * hs, p + t * sz + side * hs, p + t * sz - side * hs, nrm,
                   (u0, v0 + 0.5), (u0 + 0.5, v0 + 0.5), (u0 + 0.5, v0), (u0, v0))


def vine_mound(seed, size):
    rnd = random.Random(seed * 9973)
    k = Kit()
    k.mat("vine_core").col(1.0)
    lumps = []
    leanA = rnd.uniform(0, TAU)
    lean = V(math.cos(leanA), 0, math.sin(leanA))
    for i in range(6):
        a = 0.0 if i == 0 else leanA + rnd.uniform(-1.4, 1.4) + (math.pi if i % 3 == 0 else 0.0)
        d = 0.0 if i == 0 else rnd.uniform(0.3, 0.65)
        hy = rnd.uniform(0.5, 0.7) if i == 1 else rnd.uniform(0.18, 0.38) * (1.2 if i == 0 else 0.85)
        c = V(math.cos(a) * size.x * d, size.y * hy, math.sin(a) * size.z * d) - lean * size.x * 0.12
        rr = V(size.x, size.y * (0.75 if i == 1 else 0.62), size.z) * rnd.uniform(0.38, 0.58) * (1.1 if i == 0 else 0.85)
        lumps.append((c, rr))
        k.blob(c, rr * 0.9, seed * 7 + i, 0.3, False, 0.6, 0.6, 2, 1.6)
    k.mat("ivy")
    n = int(260 * size.x * size.z) + 300
    for _ in range(n):
        c, r = lumps[rnd.randint(0, len(lumps) - 1)]
        th = rnd.uniform(0, TAU)
        ph = math.acos(rnd.uniform(-0.35, 1.0))
        dd = V(math.sin(ph) * math.cos(th), math.cos(ph), math.sin(ph) * math.sin(th))
        at = c + V(dd.x * r.x, dd.y * r.y, dd.z * r.z)
        if at.y < 0.05:
            continue
        nrm = V(dd.x / r.x, dd.y / r.y, dd.z / r.z).normalized()
        nrm = (nrm + V(rnd.uniform(-0.4, 0.4), rnd.uniform(-0.2, 0.4), rnd.uniform(-0.4, 0.4))).normalized()
        down = DOWN - nrm * DOWN.dot(nrm)
        t = down.normalized() if down.length_squared > 0.01 else RIGHT.copy()
        ang = rnd.uniform(-0.8, 0.8)
        t = (t * math.cos(ang) + nrm.cross(t) * math.sin(ang)).normalized()
        side = nrm.cross(t).normalized()
        sz = rnd.uniform(0.26, 0.42)
        hs = sz * 0.5
        b0 = at - t * sz * 0.2
        top = b0 + t * sz
        cell = 0 if rnd.random() < 0.5 else (1 if rnd.random() < 0.6 else (3 if rnd.random() < 0.7 else 2))
        u0, v0 = (cell % 2) * 0.5, (cell // 2) * 0.5
        l = rnd.uniform(0.55, 0.92)
        if at.y < size.y * 0.25 and rnd.random() < 0.45:
            continue
        k.col(l, l * rnd.uniform(0.95, 1.08), l * 0.9)
        k.quad(b0 - side * hs, b0 + side * hs, top + side * hs + nrm * 0.03, top - side * hs + nrm * 0.03, nrm,
               (u0, v0 + 0.5), (u0 + 0.5, v0 + 0.5), (u0 + 0.5, v0), (u0, v0))
    k.mat("bark").col(0.42, 0.38, 0.33)
    for _ in range(4):
        c, r = lumps[rnd.randint(0, len(lumps) - 1)]
        b = c + UP * r.y * 0.6
        d = V(rnd.uniform(-0.6, 0.6), 1, rnd.uniform(-0.6, 0.6)).normalized()
        k.cylinder(b, b + d * rnd.uniform(0.5, 1.0) * size.y * 0.6, 0.02, 0.004, 4, False, 2.0)
    return k


def palmetto(seed):
    rnd = random.Random(seed * 4421)
    k = Kit()
    for _ in range(rnd.randint(6, 10)):
        a = rnd.uniform(0, TAU)
        tilt = rnd.uniform(0.25, 0.95)
        o = V(math.cos(a), 0, math.sin(a))
        dead = rnd.random() < 0.2
        stem = rnd.uniform(0.5, 1.0) * (0.8 if dead else 1.0)
        top = o * math.sin(tilt) * stem + UP * math.cos(tilt) * stem * (0.6 if dead else 1.0)
        k.mat("bark").col(0.5, 0.45, 0.35)
        k.cylinder(V(0, 0, 0), top, 0.02, 0.012, 5, False)
        n = (o * 0.6 + UP * (-0.2 if dead else 0.8)).normalized()
        u = n.cross(UP)
        u = RIGHT.copy() if u.length_squared < 0.01 else u.normalized()
        v = n.cross(u).normalized()
        k.mat("palmetto")
        col = (1.5, 1.05, 0.6) if dead else (rnd.uniform(0.85, 1.2),) * 3
        r = rnd.uniform(0.45, 0.65)
        for i in range(15):
            t = lerp(-1.45, 1.45, i / 14)
            dir_ = (u * math.sin(t) + v * math.cos(t)).normalized()
            side = n.cross(dir_).normalized() * 0.022
            tip = top + dir_ * r - n * r * 0.18 * abs(math.sin(t)) - UP * (r * 0.4 if dead else 0.0)
            m = 0.85 + 0.3 * ((i * 7) % 5) / 4
            k.col(col[0] * m, col[1] * m, col[2] * m)
            k.quad(top - side * 0.4, top + side * 0.4, tip + side * 0.3, tip - side * 0.3, n)
    return k


# =================================================================== the ground's dead wood, its rocks, its small growth


def stump():
    k = Kit()
    k.mat("bark").col(0.8, 0.78, 0.74)
    k.loft([(V(0, -0.3, 0), 0.36, (0.8, 0.78, 0.74)), (V(0, 0.05, 0), 0.33, (0.8, 0.78, 0.74)), (V(0, 0.45, 0), 0.28, (0.82, 0.8, 0.76))], 14, 1.0, 0.08, 3, relief=0.08)
    k.mat("endgrain").col(0.7, 0.66, 0.6)
    k.cylinder(V(0, 0.44, 0), V(0, 0.45, 0), 0.285, 0.285, 14, True, 1.7)
    return k


def stump_broken():
    k = Kit()
    top = V(0.04, 0.66, 0.02)
    k.mat("bark")
    k.loft([(V(0, -0.3, 0), 0.38, (0.72, 0.7, 0.66)), (V(0.02, 0.2, 0.01), 0.33, (0.72, 0.7, 0.66)), (top, 0.27, (0.74, 0.72, 0.68))], 14, 1.0, 0.1, 5, relief=0.1)
    k.mat("endgrain").col(0.62, 0.56, 0.48)
    k.cylinder(top - V(0, 0.02, 0), top + V(0, 0.005, 0), 0.268, 0.25, 14, True, 1.7)
    k.mat("bark").col(0.6, 0.55, 0.48)
    for ang, ln in zip((0.3, 1.4, 2.2, 3.9, 5.1), (0.38, 0.22, 0.3, 0.12, 0.17)):
        d = V(math.cos(ang), 0, math.sin(ang))
        b = top + d * 0.19
        k.cylinder(b, b + UP * ln + d * 0.03, 0.075, 0.008, 5, False, 2.0)
    return k


def log():
    k = Kit()
    c = (0.62, 0.6, 0.55)
    k.mat("bark")
    k.loft([(V(-2.6, 0, 0), 0.27, c), (V(-0.9, 0.01, 0.03), 0.25, c), (V(0.9, 0.0, 0.07), 0.22, c), (V(2.6, 0, 0.1), 0.2, c)], 14, 1.0, 0.05, 9, relief=0.08)
    k.mat("endgrain").col(*c)
    k.cylinder(V(-2.62, 0, 0), V(-2.6, 0, 0), 0.265, 0.265, 14, True, 1.8)
    k.cylinder(V(2.6, 0, 0.1), V(2.62, 0, 0.1), 0.2, 0.2, 14, True, 2.3)
    k.mat("bark").col(*c)
    k.cylinder(V(0.5, 0.1, 0.05), V(0.9, 0.3, 0.9), 0.06, 0.02, 5, False)
    return k


def log_broken():
    k = Kit()
    c = (0.58, 0.55, 0.5)
    k.mat("bark")
    k.loft([(V(-2.6, 0, 0.05), 0.25, c), (V(-0.8, 0.02, 0.0), 0.24, c), (V(1.0, 0.0, -0.05), 0.23, c), (V(2.45, 0.02, -0.08), 0.22, c)], 14, 1.0, 0.06, 11, relief=0.1)
    k.mat("endgrain").col(*c)
    k.cylinder(V(-2.62, 0, 0.05), V(-2.6, 0, 0.05), 0.245, 0.245, 14, True, 1.8)
    k.mat("bark").col(0.55, 0.5, 0.44)
    for a, ln in ((0.4, 0.35), (1.5, 0.22), (2.6, 0.3), (4.1, 0.18), (5.3, 0.26)):
        d = V(0, math.sin(a), math.cos(a)) * 0.15
        b = V(2.4, 0.02, -0.08) + d
        k.cylinder(b, b + V(ln, d.y * 0.4, d.z * 0.4), 0.06, 0.008, 5, False, 2.0)
    k.cylinder(V(-0.9, 0.12, 0.1), V(-1.1, 0.5, 0.45), 0.05, 0.015, 5, False)
    k.cylinder(V(1.2, 0.15, -0.12), V(1.45, 0.3, -0.55), 0.04, 0.012, 5, False)
    return k


def branch():
    k = Kit()
    c = (0.55, 0.52, 0.48)
    k.mat("bark")
    k.loft([(V(-0.9, 0.04, 0), 0.045, c), (V(0, 0.045, 0.05), 0.033, c), (V(0.9, 0.04, 0.1), 0.02, c)], 6, 2.0, sub=3)
    k.loft([(V(0.1, 0.04, 0.04), 0.025, c), (V(0.4, 0.06, 0.27), 0.018, c), (V(0.7, 0.08, 0.5), 0.01, c)], 5, 2.0, sub=2)
    k.loft([(V(-0.4, 0.04, 0.01), 0.02, c), (V(-0.6, 0.045, -0.17), 0.014, c), (V(-0.8, 0.05, -0.35), 0.008, c)], 5, 2.0, sub=2)
    return k


def branch_fork():
    k = Kit()
    c = (0.5, 0.47, 0.43)
    k.mat("bark")
    k.loft([(V(-0.8, 0.04, 0.05), 0.05, c), (V(-0.35, 0.045, 0.03), 0.042, c), (V(0.05, 0.045, 0), 0.035, c)], 6, 2.0, sub=2)
    k.loft([(V(0.05, 0.045, 0), 0.035, c), (V(0.45, 0.04, 0.16), 0.024, c), (V(0.85, 0.035, 0.32), 0.012, c)], 6, 2.0, sub=2)
    k.loft([(V(0.05, 0.045, 0), 0.03, c), (V(0.4, 0.04, -0.18), 0.02, c), (V(0.75, 0.03, -0.36), 0.01, c)], 6, 2.0, sub=2)
    k.loft([(V(0.45, 0.04, 0.16), 0.015, c), (V(0.6, 0.06, 0.5), 0.006, c)], 5, 2.0, sub=2)
    k.loft([(V(-0.35, 0.045, 0.03), 0.018, c), (V(-0.5, 0.05, -0.25), 0.007, c)], 5, 2.0, sub=2)
    return k


def rock(seed):
    k = Kit()
    k.mat("moss_rock").col(0.85, 0.85, 0.82)
    k.blob(V(0, 0, 0), V(1.0, 0.62, 0.85), seed, 0.28, True, 0.7, 0.0, 3, 1.3)
    return k


def boulder(seed):
    k = Kit()
    k.mat("moss_rock").col(0.8, 0.8, 0.78)
    k.blob(V(0, 0, 0), V(1.25, 0.8, 1.0), seed, 0.26, True, 0.6, 0.0, 3, 1.2)
    k.blob(V(0.75, -0.1, 0.35), V(0.7, 0.55, 0.65), seed + 100, 0.24, True, 0.6, 0.0, 3, 1.4)
    return k


def fern():
    """(2026-10-15, the owner: "some of the ferns in act 1 look like bright green cobwebs") Real fronds, not alpha cards: the
    old two cards per frond carried a 32 px leaflet texture that fell apart into thin bright strands once filtered, scissored
    and thinned edge-on. Now each frond is an arching stalk with its leaflets (pinnae) as solid little blades in pairs,
    longest at the middle of the frond, swept toward the tip and a little folded down the midrib; one opaque material
    ("fern_leaf") lit like the rest of the floor. v runs 1 at the root to 0 at the tip, as the foliage shader's sway wants."""
    k = Kit()
    k.mat("fern_leaf")
    rnd = random.Random(31)
    fronds = 9
    for i in range(fronds):
        a = TAU * i / fronds + rnd.uniform(-0.25, 0.25)
        d = V(math.cos(a), 0, math.sin(a))
        side = V(-d.z, 0, d.x)
        ln = rnd.uniform(0.72, 0.95)
        rise = rnd.uniform(0.42, 0.6)
        root = d * 0.04 + V(0, 0.02, 0)
        ctrl = d * ln * 0.38 + V(0, rise, 0)
        tip = d * ln + V(0, rnd.uniform(0.05, 0.16), 0)
        twist = rnd.uniform(-0.25, 0.25)       # the frond's blade rolls a little about its stalk

        def P(t):
            return root * (1 - t) ** 2 + ctrl * 2 * t * (1 - t) + tip * t * t

        def T(t):
            return ((ctrl - root) * 2 * (1 - t) + (tip - ctrl) * 2 * t).normalized()

        def frame(t):
            tt = T(t)
            s = (side * math.cos(twist) + UP * math.sin(twist))
            s = (s - tt * s.dot(tt)).normalized()
            n = s.cross(tt).normalized()
            if n.y < 0:
                n = -n
            return tt, s, n

        segs = 6
        lum = rnd.uniform(0.82, 1.0)
        # the stalk: a thin strip, darker
        sw = 0.006
        for j in range(segs):
            t0, t1 = j / segs, (j + 1) / segs
            _, s0, n0 = frame(t0)
            _, s1, n1 = frame(t1)
            p0, p1 = P(t0), P(t1)
            w0, w1 = sw * (1 - 0.6 * t0), sw * (1 - 0.6 * t1)
            c = (0.55 * lum, 0.5 * lum, 0.42 * lum, 1.0)
            k.tri(p0 - s0 * w0, p0 + s0 * w0, p1 + s1 * w1, n0, n0, n1, (0.45, 1 - t0), (0.55, 1 - t0), (0.55, 1 - t1), c, c, c)
            k.tri(p0 - s0 * w0, p1 + s1 * w1, p1 - s1 * w1, n0, n1, n1, (0.45, 1 - t0), (0.55, 1 - t1), (0.45, 1 - t1), c, c, c)
        # the leaflets, in pairs from a short bare stipe to the tip
        pairs = 15
        for j in range(pairs):
            t = 0.16 + 0.8 * j / (pairs - 1)
            tt, s, n = frame(t)
            base = P(t)
            L = 0.19 * math.sin(math.pi * min(1.0, (t - 0.1) / 0.92)) ** 0.7 * (1.05 - 0.35 * t) + 0.012
            for sg in (-1, 1):
                dd = (s * sg * 0.9 + tt * 0.38 - n * 0.16).normalized()
                ww = dd.cross(n).normalized()
                wid = L * 0.46
                pt = base + dd * L
                m1 = base + dd * (L * 0.32) + ww * wid * 0.5
                m2 = base + dd * (L * 0.32) - ww * wid * 0.5
                m1 -= n * (L * 0.07)   # the halves dip either side of the midrib: a shallow fold
                m2 -= n * (L * 0.07)
                g = lum * rnd.uniform(0.85, 1.05)
                cb = (0.8 * g, 0.82 * g, 0.78 * g, 1.0)        # in by the stalk, shaded
                ct = (1.0 * g, 1.0 * g, 0.92 * g, 1.0)         # out at the leaflet's point
                v = 1 - t
                k.tri(base, m1, pt, n, n, n, (0.5, v), (0.0, v), (0.5, v), cb, cb, ct)
                k.tri(base, pt, m2, n, n, n, (0.5, v), (0.5, v), (1.0, v), cb, ct, cb)
    return k


def litter():
    k = Kit()
    k.mat("litter").col(1.0)
    N, S = 4, 1.6
    def P(i, j):
        u, v = i / N * 2 - 1, j / N * 2 - 1
        return V(u * S * 0.5, 0.05 * (1 - 0.5 * (u * u + v * v)), v * S * 0.5)
    for i in range(N):
        for j in range(N):
            k.quad(P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1), UP, (i / N, j / N), ((i + 1) / N, j / N), ((i + 1) / N, (j + 1) / N), (i / N, (j + 1) / N))
    return k


def grass():
    k = Kit()
    k.mat("grass").col(1.0)
    for i in range(3):
        a = math.pi * i / 3
        d = V(math.cos(a), 0, math.sin(a)) * 0.38
        n = V(-d.z, 0.6, d.x).normalized()
        k.quad(-d, d, d + V(0, 0.6, 0), -d + V(0, 0.6, 0), n, (0, 1), (1, 1), (1, 0), (0, 0))
    return k


def board_pile(seed):
    rnd = random.Random(seed * 3331)
    k = Kit()
    y = 0.03
    for i in range(9):
        tin = i % 4 == 3
        k.mat("metal" if tin else "wood")
        if tin:
            k.col(0.42, 0.26, 0.16)
        else:
            s = rnd.uniform(0.7, 1.05)
            k.col(0.62 * s, 0.58 * s, 0.52 * s)
        size = V(1.6, 0.01, 0.7) if tin else V(rnd.uniform(1.6, 2.6), 0.04, rnd.uniform(0.14, 0.25))
        rx, ry, rz = rnd.uniform(-0.12, 0.12), rnd.uniform(-0.5, 0.5), rnd.uniform(-0.1, 0.1)
        from mathutils import Euler
        m = Euler((rx, ry, rz), "YXZ").to_matrix()
        axes = (m.col[0].copy(), m.col[1].copy(), m.col[2].copy())
        k.box(V(rnd.uniform(-0.3, 0.3), y, rnd.uniform(-0.3, 0.3)), size, axes, 1.0)
        y += 0.02 if tin else rnd.uniform(0.03, 0.06)
    return k


def giant_fir():
    """Act 6's giant firs (DeepZoneDressing): a 48 m tree (each is scaled to its own 38-58 m), a dark trunk and tiers of
    dense needle mass, now a fir proper: a flared trunk and drooping tiers of boughs (scaled down they're the same tree)."""
    k = fir(41, 48.0, 1.35, 0.16, 18, 7.5, 0.06)
    return k


SPECS = {
    "fir_giant": lambda: fir(11, 33.0, 0.52, 0.42, 14, 4.3, 0.10),
    "fir_tall": lambda: fir(12, 26.0, 0.42, 0.36, 13, 3.7, 0.12),
    "fir_mid": lambda: fir(13, 18.0, 0.31, 0.26, 11, 3.0, 0.15),
    "fir_spire": lambda: fir(14, 22.0, 0.34, 0.30, 15, 2.4, 0.08),
    "fir_young": lambda: fir(15, 9.0, 0.17, 0.10, 8, 2.0, 0.05),
    "fir_giant_moss": lambda: fir(11, 33.0, 0.52, 0.42, 14, 4.3, 0.10, 0.55),
    "fir_tall_moss": lambda: fir(12, 26.0, 0.42, 0.36, 13, 3.7, 0.12, 0.55),
    "decid_a": lambda: deciduous(4),
    "decid_b": lambda: deciduous(5),
    "snag": lambda: snag(6, 9.5),
    "snag_tall": lambda: snag(16, 19.0),
    "snag_moss": lambda: snag(6, 9.5, 0.6),
    "snag_tall_moss": lambda: snag(16, 19.0, 0.6),
    "stump": stump,
    "stump_b": stump_broken,
    "rock_a": lambda: rock(7),
    "rock_b": lambda: rock(8),
    "boulder_a": lambda: boulder(21),
    "boulder_b": lambda: boulder(22),
    "log": log,
    "log_b": log_broken,
    "branch": branch,
    "branch_b": branch_fork,
    "fern": fern,
    "grass": grass,
    "litter": litter,
    "sapling_a": lambda: sapling(1, 5.5),
    "sapling_b": lambda: sapling(2, 7.0),
    "sapling_c": lambda: sapling(3, 4.2),
    "vinemound_a": lambda: vine_mound(1, V(1.6, 1.5, 1.4)),
    "vinemound_b": lambda: vine_mound(2, V(2.2, 2.1, 1.8)),
    "vinemound_c": lambda: vine_mound(3, V(2.6, 1.3, 1.7)),
    "palmetto_a": lambda: palmetto(1),
    "palmetto_b": lambda: palmetto(2),
    "boardpile": lambda: board_pile(1),
    "giant_fir": giant_fir,
    # (2026-10-15) a second, so the nine round the deep wood's heart aren't one tree turned about
    "giant_fir_b": lambda: fir(43, 48.0, 1.35, 0.18, 17, 7.2, 0.08),
}


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    keys = args or list(SPECS)
    for key in keys:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        k = SPECS[key]()
        ob = k.build(key)
        export(ob, key)


if __name__ == "__main__":
    main()
