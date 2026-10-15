"""The winter woods' trees (Acts 21-22), modelled in Blender (the owner's rule, 2026-10-15: every model made in Blender).

Each tree is built on the skeleton the game's own builder lays out (WinterTreeKit.BareTreeCode / WinterFirCode), dumped by
`creature_preview -- --dump-winter-trees`... (`--dump-lake-winter`) to tools/Blender/data/winter_trees.json: the limbs'
centre lines and radii become ONE skinned mesh (Blender's skin modifier, smoothed), so every fork grows out of its parent
where the code had separate tubes crossing; the bark's UVs run round and along each limb as the code's did; the twig and
bough sprays are its cards, given a little bend; the firs' snow caps are its caps, subdivided and roughened. The same
silhouette and reach, so what the game hangs off the tiers (icicles) still hangs off a bough.

Each surface's material is named for the game's own (bark0, bark1, bark2, twigs, boughs, firsnow): the game paints it.

    blender -b --python tools/Blender/winter_trees.py [-- key ...]      (keys: bare_501 ... fir_611; default all)
Writes assets/models/winter_trees/<key>.glb and previews to build/blender/winter_trees/.
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Vector, kdtree, noise

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "assets", "models", "winter_trees")
PREVIEW = os.path.join(ROOT, "build", "blender", "winter_trees")
DATA = os.path.join(HERE, "data", "winter_trees.json")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREVIEW, exist_ok=True)


def B(p):
    """The game's (x, y, z) (Y up, -Z forward) into Blender's (Z up; +Y exports as the game's -Z)."""
    return Vector((p[0], -p[2], p[1]))


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


_mats = {}


def mat(name, color):
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1.0)
    _mats[name] = m
    return m


def select_only(ob):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)


def apply_mods(ob):
    select_only(ob)
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


# ------------------------------------------------------------------ the limbs: one skin


LIMB_TRIS = 9000


def build_limbs(rec, key):
    """The limbs as a graph: the trunk first, every later limb starting from a point split into the nearest edge already
    there (so it grows out of its parent), then skinned and smoothed into one mesh."""
    verts, radii, cols, mats = [], [], [], []
    edges = []
    bark = None
    for br in rec["branches"]:
        rings = br["rings"]
        if len(rings) < 2:
            continue
        col = br["col"]
        bark = bark or br["mat"]
        pts = [(B(r[:3]), max(r[3], 0.007)) for r in rings]
        if not verts:
            start = None
        else:
            # the nearest point on an edge already in the graph: split that edge there and grow from it
            p0 = pts[0][0]
            best, bi, bt = 1e9, -1, 0.0
            for ei, (a, b) in enumerate(edges):
                va, vb = verts[a], verts[b]
                ab = vb - va
                L2 = ab.length_squared
                t = 0.0 if L2 < 1e-12 else max(0.0, min(1.0, (p0 - va).dot(ab) / L2))
                d = (va + ab * t - p0).length
                if d < best:
                    best, bi, bt = d, ei, t
            a, b = edges[bi]
            if bt < 0.08:
                start = a
            elif bt > 0.92:
                start = b
            else:
                n = len(verts)
                verts.append(verts[a].lerp(verts[b], bt))
                radii.append(radii[a] + (radii[b] - radii[a]) * bt)
                cols.append(cols[a])
                edges[bi] = (a, n)
                edges.append((n, b))
                start = n
            # the limb's own first ring sits inside its parent: skip it, keep its radius for the next
            pts = pts[1:] if (pts[0][0] - verts[start]).length < radii[start] * 2.5 + 0.05 else pts
        prev = start
        for p, r in pts:
            n = len(verts)
            verts.append(p)
            radii.append(r)
            cols.append(col)
            if prev is not None:
                edges.append((prev, n))
            prev = n
    print("[wt] graph", len(verts), len(edges), flush=True)
    me = bpy.data.meshes.new(key + "_limbs")
    me.from_pydata([tuple(v) for v in verts], edges, [])
    ob = bpy.data.objects.new(key + "_limbs", me)
    bpy.context.scene.collection.objects.link(ob)
    sk = ob.modifiers.new("skin", "SKIN")
    sk.use_smooth_shade = True
    sk.branch_smoothing = 0.6
    for i, r in enumerate(radii):
        me.skin_vertices[0].data[i].radius = (r, r)
    me.skin_vertices[0].data[0].use_root = True
    print("[wt] radii set", flush=True)
    apply_mods(ob)
    # the skin is dense where rings crowd; collapse it to an instancing budget (the forks stay smooth)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if tris > LIMB_TRIS:
        dec = ob.modifiers.new("dec", "DECIMATE")
        dec.ratio = LIMB_TRIS / tris
        apply_mods(ob)
    ob.data.materials.append(mat(bark or "bark0", (0.4, 0.35, 0.3)))
    for p in ob.data.polygons:
        p.use_smooth = True
    print("[wt] skinned", len(ob.data.vertices), flush=True)
    limb_uvs_and_colours(ob, verts, edges, radii, cols, 2.0)
    return ob


def limb_uvs_and_colours(ob, verts, edges, radii, cols, u_round):
    """Bark UVs as the code laid them: v up each limb at 0.55 a metre (accumulated from the root), u round it; the vertex
    colour of the limb it belongs to (the code's shading of roots and tiers)."""
    # arc length from the root along the graph (a tree: BFS from vertex 0)
    adj = {}
    for a, b in edges:
        adj.setdefault(a, []).append(b)
        adj.setdefault(b, []).append(a)
    arc = {0: 0.0}
    q = [0]
    while q:
        a = q.pop()
        for b in adj.get(a, []):
            if b not in arc:
                arc[b] = arc[a] + (verts[b] - verts[a]).length
                q.append(b)
    # sample points along every edge, for the nearest-edge lookup
    samples = []
    for ei, (a, b) in enumerate(edges):
        L = (verts[b] - verts[a]).length
        n = max(1, int(L / 0.04))
        for s in range(n + 1):
            samples.append((verts[a].lerp(verts[b], s / n), ei, s / n))
    kd = kdtree.KDTree(len(samples))
    for i, (p, _, _) in enumerate(samples):
        kd.insert(p, i)
    kd.balance()
    me = ob.data
    me.uv_layers.new(name="UVMap")
    me.color_attributes.new(name="Col", type="BYTE_COLOR", domain="CORNER")
    uvl = me.uv_layers["UVMap"]   # (fetched again: adding a layer moves the others)
    ca = me.color_attributes["Col"]
    me.color_attributes.active_color = ca
    vuv = {}
    vcol = {}
    for v in me.vertices:
        _, idx, _ = kd.find(v.co)
        p, ei, t = samples[idx]
        a, b = edges[ei]
        axis = (verts[b] - verts[a])
        axis = axis.normalized() if axis.length > 1e-6 else Vector((0, 0, 1))
        ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
        x = ref.cross(axis).normalized()
        y = axis.cross(x)
        d = v.co - p
        ang = math.atan2(d.dot(y), d.dot(x))
        u = (ang / (2 * math.pi) + 0.5) * (u_round if a == 0 or radii[a] > 0.09 else 1.0)
        vv = (arc.get(a, 0.0) + (arc.get(b, 0.0) - arc.get(a, 0.0)) * t) * 0.55
        vuv[v.index] = (u, vv)
        c = cols[b] if t > 0.5 else cols[a]
        vcol[v.index] = (c[0], c[1], c[2], 1.0)
    for poly in me.polygons:
        us = [vuv[me.loops[li].vertex_index][0] for li in poly.loop_indices]
        hi = max(us)
        for li in poly.loop_indices:
            u, vv = vuv[me.loops[li].vertex_index]
            if hi - u > 0.5:
                u += 1.0 if hi < 1.5 else 2.0   # (the seam: the face straddling it keeps its own side)
            uvl.data[li].uv = (u, vv)
            ca.data[li].color = vcol[me.loops[li].vertex_index]


# ------------------------------------------------------------------ the sprays


def build_cards(rec, key):
    """Every spray card of the code's, each given a gentle bend along its length (three steps, sagging toward the tip) so
    a crown reads as boughs, not flat panes; its UVs as the code's (the base along the texture's root edge)."""
    groups = {}
    for c in rec["cards"]:
        groups.setdefault(c["mat"], []).append(c)
    obs = []
    for mname, cards in groups.items():
        bm = bmesh.new()
        col = bm.loops.layers.color.new("Col")
        uv = bm.loops.layers.uv.new("UVMap")
        for c in cards:
            b0, b1, t1, t0 = (B(p) for p in c["quad"])
            cc = (*c["col"], 1.0)
            steps = 3
            along = ((t0 - b0) + (t1 - b1)) * 0.5
            sag = along.length * (0.08 if mname == "boughs" else 0.05)
            rows = []
            for s in range(steps + 1):
                f = s / steps
                drop = Vector((0, 0, -sag * f * f))
                rows.append((b0.lerp(t0, f) + drop, b1.lerp(t1, f) + drop, f))
            for s in range(steps):
                (l0, r0, f0), (l1, r1, f1) = rows[s], rows[s + 1]
                vs = [bm.verts.new(l0), bm.verts.new(r0), bm.verts.new(r1), bm.verts.new(l1)]
                face = bm.faces.new(vs)
                # game v: 1 at the base, 0 at the tip; Blender's v is flipped on export
                for loop, (u, gv) in zip(face.loops, [(0, 1 - f0), (1, 1 - f0), (1, 1 - f1), (0, 1 - f1)]):
                    loop[uv].uv = (u, 1 - gv)
                    loop[col] = cc
        me = bpy.data.meshes.new(f"{key}_{mname}")
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(f"{key}_{mname}", me)
        bpy.context.scene.collection.objects.link(ob)
        ob.data.materials.append(mat(mname, (0.3, 0.33, 0.25)))
        me.color_attributes.active_color = me.color_attributes["Col"]
        for p in me.polygons:
            p.use_smooth = True
        obs.append(ob)
    return obs


# ------------------------------------------------------------------ the snow on the firs' tiers


def build_caps(rec, key, seed):
    if not rec["caps"]:
        return None
    bm = bmesh.new()
    for cap in rec["caps"]:
        o = [B(p) for p in cap["o"]]
        i0 = [B(p) for p in cap["in"]]
        lip = [B(p) for p in cap["lip"]]
        n = len(o)
        vi = [bm.verts.new(p) for p in i0]
        vo = [bm.verts.new(p) for p in o]
        vl = [bm.verts.new(p) for p in lip]
        for j in range(n):
            k = (j + 1) % n
            bm.faces.new([vi[j], vi[k], vo[k], vo[j]])
            bm.faces.new([vo[j], vo[k], vl[k], vl[j]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=1, use_grid_fill=True)
    for v in bm.verts:
        nz = noise.noise(v.co * 1.7 + Vector((seed, 0, 0)))
        v.co.z += nz * 0.07
    me = bpy.data.meshes.new(key + "_snow")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(key + "_snow", me)
    bpy.context.scene.collection.objects.link(ob)
    sub = ob.modifiers.new("sub", "SUBSURF")
    sub.levels = 1
    apply_mods(ob)
    ob.data.materials.append(mat("firsnow", (0.9, 0.92, 0.95)))
    for p in ob.data.polygons:
        p.use_smooth = True
    # box UVs for the snow texture
    select_only(ob)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=2.0)
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def build(key, rec):
    clear()
    _mats.clear()
    seed = int(key.split("_")[1])
    obs = [build_limbs(rec, key)] + build_cards(rec, key)
    snow = build_caps(rec, key, seed)
    if snow:
        obs.append(snow)
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs:
        o.select_set(True)
    path = os.path.join(OUT, key + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_materials="EXPORT", export_colors=True, export_normals=True, export_texcoords=True)
    tris = sum(len(p.vertices) - 2 for o in obs for p in o.data.polygons)
    print(f"[winter_trees] {key}: {tris} triangles -> {path}")
    preview(key, obs)


def preview(key, obs):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "VERTEX"
    sc.render.resolution_x, sc.render.resolution_y = 700, 900
    zmax = max((o.matrix_world @ Vector(c)).z for o in obs for c in o.bound_box)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    cam.location = Vector((0, -zmax * 1.6, zmax * 0.5))
    cam.rotation_euler = (Vector((0, 0, zmax * 0.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 40
    sc.camera = cam
    sc.render.filepath = os.path.join(PREVIEW, key + ".png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    recipes = json.load(open(DATA))
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sorted(recipes)
    for key in want:
        build(key, recipes[key])
