"""Act 12's lake, modelled in Blender (2026-10-15, the owner's rule: every model made in Blender): the near shore's dock,
the far shore's forest rescue station (log hall, porch, roof, windows, sign, life rings), its radio mast, flagpole and
upturned boat; and the shore's growth: reeds, the bald cypresses, marsh grass, wrack, driftwood, a root wad, lily pads.

    blender -b --python tools/Blender/lake.py [-- name ...]

Each is exported to assets/models/lake/<name>.glb, a preview to build/blender/lake/<name>.png. The layout is the code's
(scripts/world/lakeparts/LakeStructures.cs, LakeDressing*.cs): the ground under the dock and the station, and the code
cypresses' crowns, come from tools/Blender/data/lake.json and cypress.json (`creature_preview -- --dump-lake-winter`).
Every surface is named for the game material it takes (LakeStructures.BlenderMat, the maps in LakeDressing), and carries
the code's vertex colours (the game's materials multiply by them). Geometry is written in the game's coordinates through
G() (Y up, -Z forward → Blender's Z up, +Y).
"""
import bpy, bmesh, json, math, os, random, sys
from mathutils import Vector, Matrix, noise

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import winter_trees as WT   # (the skinned limbs with their bark UVs: the cypress trunks, the driftwood)

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "assets", "models", "lake")
PREVIEW = os.path.join(ROOT, "build", "blender", "lake")
LAKE = json.load(open(os.path.join(HERE, "data", "lake.json")))
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREVIEW, exist_ok=True)

# the preview's colours (the game's own materials replace these by name)
SWATCH = {
    "deck": (0.5, 0.46, 0.4), "wetpost": (0.3, 0.27, 0.23), "post": (0.33, 0.29, 0.24), "rope": (0.5, 0.44, 0.33),
    "concrete": (0.5, 0.49, 0.46), "floor": (0.45, 0.4, 0.33), "log": (0.5, 0.47, 0.42), "logend": (0.62, 0.55, 0.42),
    "void": (0.01, 0.01, 0.012), "boards": (0.36, 0.34, 0.31), "shingle": (0.3, 0.29, 0.28), "sign": (0.46, 0.4, 0.32),
    "glass": (0.45, 0.5, 0.5), "ring_red": (0.55, 0.1, 0.07), "ring_white": (0.72, 0.7, 0.64), "metal": (0.45, 0.38, 0.32),
    "wire": (0.18, 0.17, 0.16), "flag": (0.4, 0.3, 0.25), "reed": (0.55, 0.58, 0.35), "cattail": (0.26, 0.17, 0.1),
    "marsh": (0.4, 0.55, 0.25), "straw": (0.6, 0.53, 0.4), "drift": (0.75, 0.72, 0.66), "lily": (0.2, 0.3, 0.14),
    "bark": (0.4, 0.35, 0.3), "branch": (0.15, 0.2, 0.12), "spray": (0.35, 0.45, 0.2), "moss": (0.4, 0.42, 0.3),
}
# box-projected UVs: the texture's repeats a metre (the code's BuildKit scales)
UVS = {"deck": 1.2, "wetpost": 1.5, "post": 1.0, "concrete": 0.5, "floor": 1 / 0.6, "boards": 1.2, "shingle": 1.0,
       "sign": 1.2, "metal": 1.0, "logend": 1.0}

PARTS = []


def G(x, y=None, z=None):
    """The game's (x, y, z) in Blender's axes."""
    if y is None:
        x, y, z = x
    return Vector((x, -z, y))


def R(axis, a):
    """A rotation about a game axis, as a Blender matrix."""
    return Matrix.Rotation(a, 3, G(axis).normalized())


def reset():
    WT.clear()
    WT._mats.clear()
    PARTS.clear()


def mat(name):
    return WT.mat(name, SWATCH.get(name, (0.6, 0.6, 0.6)))


# ------------------------------------------------------------------ parts: a bmesh made into an object


def emit(bm, mats, col=(1, 1, 1), uv="box", smooth=False, jitter=0.0, rng=None):
    """bm (Blender coordinates) as an object: its materials by name (face material_index into `mats`), box-projected UVs
    unless uv is None (the bmesh's own), every corner the vertex colour `col` (a little varied face to face)."""
    if isinstance(mats, str):
        mats = [mats]
    uvl = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    cl = bm.loops.layers.color.get("Col") or bm.loops.layers.color.new("Col")
    bm.normal_update()
    for f in bm.faces:
        mname = mats[min(f.material_index, len(mats) - 1)]
        if uv == "box":
            s = UVS.get(mname, 1.0)
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            a, b = {0: (1, 2), 1: (0, 2), 2: (0, 1)}[ax]
            for lp in f.loops:
                co = lp.vert.co
                lp[uvl].uv = (co[a] * s, co[b] * s)
        j = 1.0 + (rng.uniform(-jitter, jitter) if (jitter and rng) else 0.0)
        c = (min(col[0] * j, 1), min(col[1] * j, 1), min(col[2] * j, 1), 1.0)
        for lp in f.loops:
            lp[cl] = c
    me = bpy.data.meshes.new("part")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("part", me)
    bpy.context.scene.collection.objects.link(ob)
    for m in mats:
        me.materials.append(mat(m))
    for p in me.polygons:
        p.use_smooth = smooth
    PARTS.append(ob)
    return ob


def box(c, size, mname, col, rot=None, bevel=0.012, rng=None, jitter=0.0):
    """A box, centred on c (game), its size along the game's x, y, z, turned by rot (a Blender matrix), its edges bevelled."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((size[0], size[2], size[1])), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size) * 0.3), segments=1, affect="EDGES", profile=0.5)
    if rot is not None:
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=rot)
    bmesh.ops.translate(bm, verts=bm.verts, vec=G(c))
    return emit(bm, mname, col, rng=rng, jitter=jitter)


def frames(pts):
    """Parallel-transported frames along a polyline (Blender coords): (tangent, x, y) at each point."""
    out = []
    n = len(pts)
    t0 = (pts[1] - pts[0]).normalized()
    x = t0.cross(Vector((0, 0, 1)) if abs(t0.z) < 0.9 else Vector((1, 0, 0))).normalized()
    for i in range(n):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        x = (x - t * x.dot(t))
        x = x.normalized() if x.length > 1e-6 else t.cross(Vector((0, 0, 1))).normalized()
        out.append((t, x, t.cross(x).normalized()))
    return out


def tube(pts_game, radii, sides, mname, col, caps=None, u_len=1.0, v_rep=2.0, wobble=0.0, seed=0, smooth=True, cap_mat=None):
    """A tube along points (game) with a radius at each; UVs u along it (a metre is 1/u_len), v round it. caps: end faces
    (material `cap_mat` when given, with planar UVs)."""
    pts = [G(p) for p in pts_game]
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * len(pts)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    fr = frames(pts)
    rings = []
    arc = [0.0]
    for i in range(1, len(pts)):
        arc.append(arc[-1] + (pts[i] - pts[i - 1]).length)
    for i, (p, (t, x, y)) in enumerate(zip(pts, fr)):
        ring = []
        for s in range(sides):
            a = 2 * math.pi * s / sides
            r = radii[i] * (1 + wobble * noise.noise(Vector((math.cos(a) * 1.7 + seed, math.sin(a) * 1.7, arc[i] * 1.3))))
            ring.append(bm.verts.new(p + (x * math.cos(a) + y * math.sin(a)) * r))
        rings.append(ring)
    for i in range(len(pts) - 1):
        for s in range(sides):
            s1 = (s + 1) % sides
            f = bm.faces.new((rings[i][s], rings[i][s1], rings[i + 1][s1], rings[i + 1][s]))
            for lp, (si, ii) in zip(f.loops, [(s, i), (s + 1, i), (s + 1, i + 1), (s, i + 1)]):
                lp[uvl].uv = (arc[ii] / u_len, si / sides * v_rep)
    if caps:
        for end, ring in ((0, rings[0][::-1]), (1, rings[-1])):
            f = bm.faces.new(ring)
            f.material_index = 1 if cap_mat else 0
            f.smooth = False
            c = sum((v.co for v in ring), Vector()) / len(ring)
            t, x, y = fr[0 if end == 0 else -1]
            r = max(radii[0 if end == 0 else -1], 1e-3)
            for lp in f.loops:
                d = lp.vert.co - c
                lp[uvl].uv = (0.5 + d.dot(x) / (2 * r), 0.5 + d.dot(y) / (2 * r))
    ob = emit(bm, [mname] + ([cap_mat] if cap_mat else []), col, uv=None, smooth=smooth)
    for p in ob.data.polygons:
        if p.material_index == 1 or (caps and len(p.vertices) > 4):
            p.use_smooth = False
    return ob


def sheet(corner, nu, nv, P, mname, col, thick=0.03, uv_scale=1.0):
    """A board or panel: a grid P(u, v) (u, v in 0..1, game coords) given thickness. UVs from the grid scaled to its size."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    vs = [[bm.verts.new(G(P(i / nu, j / nv))) for j in range(nv + 1)] for i in range(nu + 1)]
    lu = (G(P(1, 0.5)) - G(P(0, 0.5))).length
    lv = (G(P(0.5, 1)) - G(P(0.5, 0))).length
    for i in range(nu):
        for j in range(nv):
            f = bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
            for lp, (a, b) in zip(f.loops, [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]):
                lp[uvl].uv = (a / nu * lu * uv_scale, b / nv * lv * uv_scale)
    bm.normal_update()
    if thick > 0:
        res = bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=thick)
    return emit(bm, mname, col, uv=None)


# ------------------------------------------------------------------ export, preview


def export(name, look=None):
    obs = list(PARTS)
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    if "Col" in ob.data.color_attributes:
        ob.data.color_attributes.active_color = ob.data.color_attributes["Col"]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_materials="EXPORT", export_colors=True, export_normals=True, export_texcoords=True)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[lake] {name}: {tris} triangles -> {path}", flush=True)
    preview(name, ob, look)


def preview(name, ob, look=None):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "MATERIAL"
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 900, 700
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
    hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
    mid, size = (lo + hi) * 0.5, (hi - lo).length
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    d = Vector(look or (0.55, -0.85, 0.45)).normalized()
    cam.location = mid + d * size * 1.05
    cam.rotation_euler = (mid - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 40
    sc.camera = cam
    sc.render.filepath = os.path.join(PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)


# ------------------------------------------------------------------ the dock


def dock():
    d = LAKE["dock"]
    rng = random.Random(77)
    z0, z1, hw, top = d["z0"], d["z1"], d["hw"], d["top"]
    # planks: each its own length, a hair crooked and cupped, a few split at the end; a gap near the end
    for z, ground in d["ground"]:
        if z1 + 1.3 < z < z1 + 1.6:
            continue
        sh = rng.uniform(0.42, 0.6)
        y = max(top, ground + 0.12) + rng.uniform(-0.012, 0.012)
        L = hw * 2 + rng.uniform(-0.06, 0.1)
        x0 = rng.uniform(-0.03, 0.03)
        cup = rng.uniform(0.004, 0.012)
        yaw = rng.uniform(-0.025, 0.025)
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=6, y_segments=2, size=0.5)
        bmesh.ops.scale(bm, vec=Vector((L, 0.22, 1)), verts=bm.verts)
        for v in bm.verts:
            v.co.z += cup * (v.co.y / 0.11) ** 2   # (cupped: the edges curl up a hair)
            v.co.z += 0.004 * noise.noise(Vector((v.co.x * 2 + z * 3, v.co.y, 0)))
        bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.05)
        if rng.random() < 0.25:   # a split running in from one end
            end = L * 0.5 * rng.choice((-1, 1))
            for v in bm.verts:
                if abs(v.co.x - end) < 0.3 and abs(v.co.y) < 0.03:
                    v.co.z -= 0.01
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(yaw, 3, "Z"))
        bmesh.ops.translate(bm, verts=bm.verts, vec=G(x0, y, z))
        emit(bm, "deck", (sh, sh * 0.93, sh * 0.85))
    # stringers under the planks, and the posts: round, weathered, slime-dark at the waterline; a brace across each pair
    for x in (-hw + 0.12, hw - 0.12):
        box((x, top - 0.13, (z0 + z1) * 0.5), (0.1, 0.14, z0 - z1), "wetpost", (0.36, 0.32, 0.27))
    z = z0 - 0.4
    while z > z1 - 0.1:
        for x in (-hw, hw):
            lean = rng.uniform(-0.04, 0.04)
            pts = [(x, -1.6, z), (x + lean * 0.5, (top - 1.6) * 0.5, z), (x + lean, top + 0.35, z)]
            tube(pts, [0.085, 0.08, 0.075], 9, "wetpost", (0.36, 0.32, 0.27), caps=True, u_len=0.66, wobble=0.06, seed=z)
            # the top worn round
            bm = bmesh.new()
            bmesh.ops.create_uvsphere(bm, u_segments=9, v_segments=4, radius=0.075)
            for v in bm.verts:
                v.co.z = max(v.co.z, 0) * 0.35
            bmesh.ops.translate(bm, verts=bm.verts, vec=G(x + lean, top + 0.35, z))
            emit(bm, "wetpost", (0.38, 0.34, 0.29), smooth=True)
        g = next((gy for gz, gy in d["ground"] if abs(gz - z) < 0.2), -1.0)
        tube([(-hw, max(g, -1.4) + 0.15, z), (hw, top - 0.22, z)], 0.045, 6, "wetpost", (0.32, 0.29, 0.25), caps=True, u_len=0.66)
        z -= 2.4
    # the cleat: a cast iron base and its two horns, bolted to the deck
    m = d["mooring"]
    cx, cy, cz = hw - 0.15, top + 0.05, m[1] - 1.3
    box((cx, cy - 0.015, cz), (0.08, 0.04, 0.2), "post", (0.3, 0.27, 0.23), bevel=0.01)
    tube([(cx, cy + 0.035, cz - 0.17), (cx, cy + 0.05, cz), (cx, cy + 0.035, cz + 0.17)], [0.018, 0.026, 0.018], 7, "post",
         (0.3, 0.27, 0.23), caps=True)
    for s in (-0.06, 0.06):
        tube([(cx, cy, cz + s), (cx, cy + 0.04, cz + s * 0.8)], 0.02, 6, "post", (0.3, 0.27, 0.23))
    export("dock", (0.8, -0.6, 0.35))


# ------------------------------------------------------------------ the rescue station


W, D, WALL, RIDGE = 15.0, 9.0, 3.7, 6.4
HW, HD = W / 2, D / 2
DOOR_W, DOOR_H = 2.2, 2.35


def log(a, b, r, rng, cut_a=True, cut_b=True, shade=0.68):
    """A round wall log from a to b (game), bowed a little, its bark rough, its sawn ends end grain."""
    a, b = Vector(a), Vector(b)
    n = 6
    side = Vector((0, 1, 0)).cross((b - a).normalized())
    bow = rng.uniform(-0.015, 0.015)
    pts = [tuple(a.lerp(b, i / n) + side * bow * math.sin(math.pi * i / n)) for i in range(n + 1)]
    rr = [r * (1 + rng.uniform(-0.04, 0.04)) * (1.04 - 0.08 * i / n) for i in range(n + 1)]
    return tube(pts, rr, 10, "log", (shade, shade * 0.97, shade * 0.93), caps=True, cap_mat="logend", u_len=1.14,
                v_rep=2.0, wobble=0.05, seed=a.x + a.z * 3)


def window(rng, c, n, boarded):
    """A window: its dark recess, a plank frame (sill, head, jambs), boarded over with three crooked boards or broken, a
    jagged shard of glass left in one corner."""
    c, n = Vector(c), Vector(n)
    right = Vector((0, 1, 0)).cross(n).normalized()
    basis = Matrix((G(right), G(-n), G(0, 1, 0))).transposed()   # (the box's own x, depth, up)
    bm = bmesh.new()
    vs = [bm.verts.new(G(c + right * sx * 0.55 + Vector((0, sy * 0.45, 0)) - n * 0.02)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bm.faces.new(vs)
    emit(bm, "void", (1, 1, 1))
    fc = (0.3, 0.27, 0.23)
    box(c + Vector((0, 0.5, 0)), (1.3, 0.1, 0.08), "post", fc, rot=basis)
    box(c - Vector((0, 0.5, 0)) + n * 0.02, (1.4, 0.08, 0.14), "post", fc, rot=basis)
    for s in (-1, 1):
        box(c + right * s * 0.6, (0.1, 1.1, 0.08), "post", fc, rot=basis)
    if boarded:
        for i in range(3):
            box(c + n * 0.06 + Vector((0, (i - 1) * 0.3, 0)), (1.45, 0.2, 0.03), "boards", (0.4, 0.37, 0.33),
                rot=R(n, rng.uniform(-0.15, 0.15)) @ basis, rng=rng, jitter=0.08)
            for s in (-1, 1):   # nail heads
                box(c + n * 0.08 + right * s * 0.6 + Vector((0, (i - 1) * 0.3, 0)), (0.02, 0.02, 0.01), "metal", (0.5, 0.45, 0.4), bevel=0)
    else:
        corner = c - right * 0.55 + Vector((0, 0.45, 0)) + n * 0.01
        bm = bmesh.new()
        pts = [corner, corner + right * 0.42, corner + right * 0.2 + Vector((0, -0.12, 0)), corner + right * 0.12 + Vector((0, -0.3, 0)),
               corner + Vector((0, -0.5, 0))]
        bm.faces.new([bm.verts.new(G(p)) for p in pts])
        bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.006)
        emit(bm, "glass", (1, 1, 1))


def life_ring(c, rng):
    """A red-and-white life ring hung flat against a post on a loop of rope over a nail."""
    c = Vector(c)
    tilt = rng.uniform(-0.25, 0.25)
    R0, r0, nu, nv = 0.3, 0.07, 32, 8
    bm = bmesh.new()
    vs = []
    for i in range(nu):
        a = 2 * math.pi * i / nu + tilt
        ring = []
        for j in range(nv):
            b = 2 * math.pi * j / nv
            rad = R0 + r0 * math.cos(b)
            ring.append(bm.verts.new(G(c + Vector((math.cos(a) * rad, math.sin(a) * rad, r0 * math.sin(b))))))
        vs.append(ring)
    for i in range(nu):
        for j in range(nv):
            f = bm.faces.new((vs[i][j], vs[(i + 1) % nu][j], vs[(i + 1) % nu][(j + 1) % nv], vs[i][(j + 1) % nv]))
            f.material_index = (i // 4) % 2   # (the code's eight bands, alternating)
    emit(bm, ["ring_red", "ring_white"], (1, 1, 1), smooth=True)
    nail = c + Vector((0, R0 + 0.12, -0.05))
    tube([nail + Vector((0, 0, -0.06)), nail + Vector((0, 0, 0.02))], 0.008, 5, "metal", (0.4, 0.38, 0.35))
    top = c + Vector((math.cos(math.pi / 2 + tilt) * R0, math.sin(math.pi / 2 + tilt) * R0, 0))
    for s in (-1, 1):
        tube([nail, (nail + top) * 0.5 + Vector((s * 0.05, 0, 0.02)), top + Vector((s * 0.06, -0.03, 0.02))], 0.012, 5, "rope", (1, 1, 1))


def station():
    st = LAKE["station"]
    rng = random.Random(7)
    # piers down to the ground, the floor
    for x, z, gy in st["piers"]:
        box((x, (gy - 0.4) * 0.5, z), (0.45, -gy + 0.4, 0.45), "concrete", (0.5, 0.48, 0.45), bevel=0.03, rng=rng, jitter=0.06)
    box((0, -0.05, 0), (W, 0.1, D), "floor", (0.5, 0.46, 0.4), bevel=0.01)

    # the log walls, course by course: round logs, the long ones running past the corners, the door cut through the front
    courses, logT, ext = 13, 0.26, 0.24
    logH = WALL / courses
    r = logH * 0.56
    for c in range(courses):
        y = c * logH + logH * 0.5
        y0, y1 = c * logH, (c + 1) * logH
        front_long = c % 2 == 0
        sh = rng.uniform(0.58, 0.78)
        xa = -HW - ext if front_long else -HW + logT * 0.5
        if y1 > 0.05 and y0 < DOOR_H - 0.05:
            log((xa, y, HD), (-DOOR_W / 2, y, HD), r, rng, shade=sh)
            log((DOOR_W / 2, y, HD), (-xa, y, HD), r, rng, shade=sh)
        else:
            log((xa, y, HD), (-xa, y, HD), r, rng, shade=sh)
        log((xa, y, -HD), (-xa, y, -HD), r, rng, shade=sh)
        za = -HD + logT * 0.5 if front_long else -HD - ext
        for s in (-1, 1):
            log((s * HW, y, za), (s * HW, y, -za), r, rng, shade=sh * rng.uniform(0.95, 1.03))
    # chinking between the courses (grey mortar, set back)
    for c in range(1, courses):
        y = c * logH
        for zz in (HD, -HD):
            if y < DOOR_H:
                for xa, xb in ((-HW, -DOOR_W / 2), (DOOR_W / 2, HW)):
                    box(((xa + xb) / 2, y, zz), (xb - xa, 0.07, 0.2), "concrete", (0.42, 0.4, 0.37), bevel=0)
            else:
                box((0, y, zz), (W, 0.07, 0.2), "concrete", (0.42, 0.4, 0.37), bevel=0)
        for xx in (-HW, HW):
            box((xx, y, 0), (0.2, 0.07, D), "concrete", (0.42, 0.4, 0.37), bevel=0)

    # darkness behind the doorway (faces inward), the door hanging open (planks on battens, a Z brace), its frame
    vx, back, front = DOOR_W / 2 + 0.3, HD - 1.6, HD - 0.14
    bm = bmesh.new()
    q = lambda *ps: bm.faces.new([bm.verts.new(G(p)) for p in ps])
    q((-vx, 0, back), (vx, 0, back), (vx, DOOR_H, back), (-vx, DOOR_H, back))
    q((-vx, 0, back), (-vx, 0, front), (-vx, DOOR_H, front), (-vx, DOOR_H, back))
    q((vx, 0, front), (vx, 0, back), (vx, DOOR_H, back), (vx, DOOR_H, front))
    q((-vx, DOOR_H, back), (vx, DOOR_H, back), (vx, DOOR_H, front), (-vx, DOOR_H, front))
    q((-vx, 0.01, front), (vx, 0.01, front), (vx, 0.01, back), (-vx, 0.01, back))
    emit(bm, "void", (1, 1, 1))
    hinge = Vector((-DOOR_W / 2, 0, HD - logT * 0.5))
    ang = 1.15
    rot = R((0, 1, 0), ang)
    def door_box(local, size, col=(0.36, 0.33, 0.3), roll=0.0):
        rr = rot @ R((0, 0, 1), roll) if roll else rot
        x, y, z = local   # (turned about the hinge, as the code's Basis(Up, 1.15))
        p = hinge + Vector((x * math.cos(ang) + z * math.sin(ang), y, -x * math.sin(ang) + z * math.cos(ang)))
        box(p, size, "boards", col, rot=rr, bevel=0.006, rng=rng, jitter=0.07)
    planks = 5
    for i in range(planks):
        door_box((0.06 + (i + 0.5) * DOOR_W * 0.92 / planks, DOOR_H * 0.5 - 0.02, -0.03), (DOOR_W * 0.92 / planks - 0.008, DOOR_H - 0.05, 0.045))
    for yy in (0.35, DOOR_H - 0.35):
        door_box((0.06 + DOOR_W * 0.46, yy, -0.075), (DOOR_W * 0.86, 0.14, 0.04), (0.32, 0.3, 0.27))
    diag = math.atan2(DOOR_H - 0.7, DOOR_W * 0.86)
    door_box((0.06 + DOOR_W * 0.46, DOOR_H * 0.5, -0.075), (math.hypot(DOOR_H - 0.7, DOOR_W * 0.86) - 0.1, 0.13, 0.04), (0.32, 0.3, 0.27), roll=diag)
    fc = (0.3, 0.27, 0.23)
    for s in (-1, 1):
        box((s * (DOOR_W / 2 + 0.05), DOOR_H / 2, HD + 0.1), (0.12, DOOR_H, 0.08), "post", fc)
    box((0, DOOR_H + 0.05, HD + 0.1), (DOOR_W + 0.3, 0.12, 0.08), "post", fc)
    box((0, 0.02, HD + 0.1), (DOOR_W + 0.1, 0.05, 0.3), "post", (0.34, 0.3, 0.25))   # the worn sill

    # windows: front left boarded over; front right broken (its lamp is the code's); the sides
    window(rng, (-4.4, 1.75, HD + 0.14), (0, 0, 1), True)
    window(rng, (4.4, 1.75, HD + 0.14), (0, 0, 1), False)
    for z in (-2.2, 2.2):
        window(rng, (-HW - 0.14, 1.75, z), (-1, 0, 0), True)
        window(rng, (HW + 0.14, 1.75, z), (1, 0, 0), rng.random() < 0.6)
    # boards knocked loose from the front, hanging askew on one nail
    for i in range(6):
        x = rng.uniform(-HW + 0.6, HW - 0.6)
        if abs(x) < DOOR_W / 2 + 0.4 or abs(abs(x) - 4.4) < 0.8:
            continue
        box((x, rng.uniform(0.5, WALL - 0.5), HD + 0.19), (0.2, 0.95, 0.03), "boards", (0.34, 0.33, 0.32),
            rot=R((0, 0, -1), rng.uniform(-0.5, 0.5)), rng=rng, jitter=0.08)

    # the roof, gable end to the lake: the left slope still shingled, course over course; the right stripped to
    # sagging boards, a hole torn through at the back with the rafters showing
    ridgeF, ridgeB = Vector((0, RIDGE, HD + 0.6)), Vector((0, RIDGE - 0.3, -HD - 0.6))
    ov = 0.5
    eL_f, eL_b = Vector((-HW - ov, WALL - 0.1, HD + ov + 0.1)), Vector((-HW - ov, WALL - 0.1, -HD - ov - 0.1))
    eR_f, eR_b = Vector((HW + ov, WALL - 0.1, HD + ov + 0.1)), Vector((HW + ov, WALL - 0.1, -HD - ov - 0.1))
    def slope(eF, eB, s, t, sag=0.0):
        """A point on a slope: s from the eave (0) to the ridge (1), t from the front (0) to the back (1)."""
        e = eF.lerp(eB, t)
        rdg = ridgeF.lerp(ridgeB, t)
        p = e.lerp(rdg, s)
        return p - Vector((0, sag, 0))
    # the underlay boards beneath the shingles
    sheet(None, 4, 8, lambda u, v: tuple(slope(eL_f, eL_b, u, v) - Vector((0, 0.05, 0))), "boards", (0.3, 0.29, 0.27), thick=0.03, uv_scale=1.2)
    rows = 26
    for i in range(rows):
        s0, s1 = i / rows, (i + 1.35) / rows
        t = 0.0
        while t < 1.0:
            dt = rng.uniform(0.035, 0.06)
            if rng.random() < 0.035:   # a shingle gone
                t += dt
                continue
            lift = rng.uniform(0.0, 0.02)
            def P(u, v, s0=s0, s1=s1, t=t, dt=dt, lift=lift):
                return tuple(slope(eL_f, eL_b, s0 + (s1 - s0) * v, min(t + dt * u, 1.0)) + Vector((0, 0.012 + lift * (1 - v), 0)))
            sheet(None, 1, 1, P, "shingle", tuple([rng.uniform(0.33, 0.43)] * 2 + [0.33]), thick=0.018)
            t += dt
    # the right slope's boards, down the slope, sagging in the middle; at the back a hole: boards broken off or gone
    nb = 44
    for i in range(nb):
        t0, t1 = i / nb + 0.002, (i + 1) / nb - 0.002
        tm = (t0 + t1) * 0.5
        z = ridgeF.z + (ridgeB.z - ridgeF.z) * tm
        hole = -HD * 0.72 < z < -HD * 0.08
        top_s = 1.0
        if hole:
            if rng.random() < 0.45:
                continue
            top_s = rng.uniform(0.15, 0.5)
        sag_t = math.sin(math.pi * min(max((tm - 0.1) / 0.8, 0), 1))
        def P(u, v, t0=t0, t1=t1, top_s=top_s, sag_t=sag_t):
            s = v * top_s
            return tuple(slope(eR_f, eR_b, s, t0 + (t1 - t0) * u, 0.6 * math.sin(math.pi * s) * sag_t))
        sh = rng.uniform(0.27, 0.34)
        sheet(None, 1, 6, P, "boards", (sh, sh * 0.97, sh * 0.9), thick=0.03, uv_scale=1.2)
    # rafters: square timbers from the ridge to the eave, seen through the hole; the ridge beam
    for t in [k / 14 for k in range(15)]:
        z = ridgeF.z + (ridgeB.z - ridgeF.z) * t
        for eF, eB in ((eR_f, eR_b), (eL_f, eL_b)):
            a = slope(eF, eB, 0.02, t) - Vector((0, 0.1, 0))
            b = slope(eF, eB, 0.98, t) - Vector((0, 0.1, 0))
            mid = (a + b) * 0.5
            L = (b - a).length
            ang = math.atan2(b.y - a.y, b.x - a.x)
            box(mid, (L, 0.15, 0.09), "post", (0.28, 0.26, 0.23), rot=R((0, 0, 1), ang), bevel=0.01)
    a, b = ridgeF - Vector((0, 0.12, 0)), ridgeB - Vector((0, 0.12, 0))
    box((a + b) * 0.5, (0.12, 0.2, (a - b).length), "post", (0.28, 0.26, 0.23), rot=R((1, 0, 0), -math.atan2(a.y - b.y, a.z - b.z)))
    # the gables: upright boards, their tops cut to the roof line (the front one carries the sign)
    for zz, rh in ((HD + 0.03, RIDGE), (-HD - 0.03, RIDGE - 0.3)):
        x = -HW
        while x < HW - 0.01:
            bw = min(0.3, HW - x)
            def roof_y(xx):
                return WALL + (rh - WALL) * (1 - abs(xx) / HW)
            def P(u, v, x=x, bw=bw):
                xx = x + bw * u
                return (xx, WALL - 0.05 + (roof_y(xx) - WALL + 0.05) * v, zz)
            sh = rng.uniform(0.3, 0.38)
            sheet(None, 1, 1, P, "boards", (sh, sh * 0.95, sh * 0.86), thick=0.04 if zz > 0 else -0.04, uv_scale=1.2)
            x += bw
    # the sign board across the front gable on two posts, its edges routed round
    sign_at = Vector((0, WALL + 0.72, HD + 0.16))
    box(sign_at, (7.2, 0.95, 0.07), "sign", (0.46, 0.4, 0.32), rot=R((0, 0, -1), 0.035), bevel=0.025)
    for s in (-1, 1):
        box((s * 3.1, WALL + 0.45, HD + 0.1), (0.1, 1.3, 0.08), "post", (0.3, 0.27, 0.23))

    # the porch: plank deck (a couple missing) on joists, steps down to the path, four posts (one snapped), the lean-to
    # roof slumping toward the missing one
    porchD, porchY = 2.4, -0.05
    z = HD + 0.15
    while z < HD + porchD:
        sh = rng.uniform(0.42, 0.58)
        if rng.random() >= 0.08:
            box((rng.uniform(-0.05, 0.05), porchY, z + 0.1), (W + 0.4, 0.05, 0.2), "deck", (sh, sh * 0.94, sh * 0.86),
                rot=R((0, 1, 0), rng.uniform(-0.004, 0.004)), bevel=0.008)
        z += 0.24
    for x in (-HW + 0.3, -HW * 0.33, HW * 0.33, HW - 0.3):
        box((x, porchY - 0.12, HD + porchD * 0.5 + 0.1), (0.1, 0.18, porchD), "post", (0.3, 0.27, 0.23))
    box((0, porchY - 0.13, HD + porchD + 0.05), (W + 0.4, 0.2, 0.1), "post", (0.3, 0.27, 0.23))
    gy = st["step_gy"]
    for i in range(3):
        t = (i + 1) / 4
        box((0, porchY + (gy - porchY) * t, HD + porchD + 0.15 + i * 0.3), (2.6, 0.06, 0.3), "deck", (0.46, 0.42, 0.36), bevel=0.008)
    for s in (-1, 1):
        a = Vector((s * 1.25, porchY - 0.05, HD + porchD))
        b = Vector((s * 1.25, gy - 0.05, HD + porchD + 0.9))
        box((a + b) * 0.5, (0.06, 0.2, (b - a).length), "post", (0.3, 0.27, 0.23), rot=R((1, 0, 0), math.atan2(a.y - b.y, b.z - a.z)))
    postZ = HD + porchD - 0.1
    posts = [-HW + 0.3, -DOOR_W / 2 - 0.7, DOOR_W / 2 + 0.7, HW - 0.3]
    pc = (0.3, 0.27, 0.23)
    for i, x in enumerate(posts):
        if i == 3:
            # snapped: a stump, its top splintered, and the rest lying on the deck
            bm = bmesh.new()
            bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.09, radius2=0.085, depth=0.75)
            for v in bm.verts:
                if v.co.z > 0.3:
                    v.co.z += rng.uniform(-0.12, 0.08)
            bmesh.ops.translate(bm, verts=bm.verts, vec=G(x, porchY + 0.375, postZ))
            emit(bm, "post", pc)
            tube([(x - 0.3, porchY + 0.1, postZ - 0.2), (x - 2.2, porchY + 0.12, postZ - 0.9)], 0.08, 8, "post", pc, caps=True, wobble=0.04)
            continue
        top = WALL - 0.45 - (0.25 if i == 2 else 0)
        tube([(x, porchY, postZ), (x + rng.uniform(-0.05, 0.05), top, postZ)], [0.09, 0.08], 8, "post", pc, caps=True, wobble=0.05, seed=i)
    pa, pb = Vector((-HW - 0.2, WALL - 0.2, HD + 0.1)), Vector((HW + 0.2, WALL - 0.2, HD + 0.1))
    pcn, pd = Vector((HW + 0.2, WALL - 1.35, HD + porchD + 0.3)), Vector((-HW - 0.2, WALL - 0.55, HD + porchD + 0.3))
    nb = 52
    for i in range(nb):
        u0, u1 = i / nb + 0.002, (i + 1) / nb - 0.002
        if rng.random() < 0.05:
            continue
        def P(u, v, u0=u0, u1=u1):
            uu = u0 + (u1 - u0) * u
            back, fr = pa.lerp(pb, uu), pd.lerp(pcn, uu)
            return tuple(back.lerp(fr, v) - Vector((0, 0.08 * math.sin(math.pi * v) * uu, 0)))
        sh = rng.uniform(0.29, 0.35)
        sheet(None, 1, 3, P, "boards", (sh, sh * 0.97, sh * 0.9), thick=0.03, uv_scale=1.2)
    # the beam along the front edge, resting on the posts
    a, b = pd + Vector((0, -0.12, -0.2)), pcn + Vector((0, -0.12, -0.2))
    box((a + b) * 0.5, ((b - a).length, 0.16, 0.12), "post", pc, rot=R((0, 0, 1), math.atan2(b.y - a.y, b.x - a.x)))
    for x in (posts[1], posts[2]):
        life_ring((x, 1.55, postZ + 0.12), rng)
    export("rescue_station", (0.6, -0.8, 0.45))


def radio_mast():
    st = LAKE["station"]
    mx, gy, mz = st["mast"]
    rng = random.Random(14)
    h, r = 14.0, 0.32
    base = Vector((mx, gy, mz))
    legs = [Vector((math.cos(2 * math.pi * i / 3) * r, 0, math.sin(2 * math.pi * i / 3) * r)) for i in range(3)]
    col = (0.45, 0.3, 0.22)
    box(base + Vector((0, -0.05, 0)), (1.0, 0.3, 1.0), "concrete", (0.5, 0.48, 0.45), bevel=0.04)
    def leg(i, y):
        return base + legs[i] * (1 - 0.5 * y / h) + Vector((0, y, 0))
    for i in range(3):
        tube([leg(i, 0), leg(i, h * 0.5), leg(i, h)], [0.035, 0.03, 0.025], 6, "metal", col, caps=True)
    y = 0.8
    while y < h - 0.5:
        for i in range(3):
            tube([leg(i, y), leg((i + 1) % 3, y + 1.1)], 0.012, 4, "metal", col)
            tube([leg(i, y), leg((i + 1) % 3, y)], 0.014, 4, "metal", col)
        y += 1.1
    # a small dish, pointing nowhere, on an arm off the mast
    top = base + Vector((0, h * 0.78, 0)) + Vector((0.3, 0, 0.2))
    aim = Vector((0.35, -0.1, 0.5)).normalized()
    bm = bmesh.new()
    nu, nv, rad, depth = 16, 4, 0.45, 0.14
    ctr = bm.verts.new(G(top))
    side = aim.cross(Vector((0, 1, 0))).normalized()
    up = side.cross(aim).normalized()
    grid = []
    for j in range(1, nv + 1):
        rr = rad * j / nv
        ring = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            p = top + (side * math.cos(a) + up * math.sin(a)) * rr + aim * depth * (rr / rad) ** 2
            ring.append(bm.verts.new(G(p)))
        grid.append(ring)
    for i in range(nu):
        bm.faces.new((ctr, grid[0][i], grid[0][(i + 1) % nu]))
    for j in range(nv - 1):
        for i in range(nu):
            bm.faces.new((grid[j][i], grid[j + 1][i], grid[j + 1][(i + 1) % nu], grid[j][(i + 1) % nu]))
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.015)
    emit(bm, "metal", (0.55, 0.52, 0.48), smooth=True)
    tube([top - aim * 0.02, top + aim * 0.3], 0.012, 4, "metal", (0.5, 0.48, 0.45))   # the feed
    tube([top, leg(0, h * 0.78)], 0.025, 5, "metal", col)
    # the guy wires, slack, to their anchors
    for an in st["anchors"]:
        an = Vector(an)
        frm = base + Vector((0, h * 0.7, 0))
        pts = [frm.lerp(an, s / 14) - Vector((0, math.sin(math.pi * s / 14) * 1.4, 0)) for s in range(15)]
        tube(pts, 0.01, 4, "wire", (1, 1, 1))
        box(an + Vector((0, 0.02, 0)), (0.35, 0.2, 0.35), "concrete", (0.48, 0.46, 0.43), bevel=0.03)
        tube([an + Vector((0, 0.1, 0)), an + Vector((0, 0.25, 0))], 0.02, 5, "metal", col)
    export("radio_mast", (0.6, -0.8, 0.3))


def flagpole():
    fx, gy, fz = LAKE["station"]["flag"]
    rng = random.Random(8)
    b = Vector((fx, gy, fz))
    col = (0.5, 0.48, 0.45)
    tube([b - Vector((0, 0.3, 0)), b + Vector((0, 4, 0)), b + Vector((0, 8, 0))], [0.06, 0.05, 0.04], 10, "metal", col, caps=True)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=0.07)
    bmesh.ops.translate(bm, verts=bm.verts, vec=G(b + Vector((0, 8.05, 0))))
    emit(bm, "metal", (0.55, 0.45, 0.3), smooth=True)
    box(b + Vector((0.07, 1.2, 0)), (0.04, 0.2, 0.05), "metal", col)   # the cleat
    box(b + Vector((0, -0.15, 0)), (0.5, 0.3, 0.5), "concrete", (0.5, 0.48, 0.45), bevel=0.03)
    tube([b + Vector((0.08, 1.25, 0.02)), b + Vector((0.07, 4.5, 0.03)), b + Vector((0.06, 7.95, 0.03))], 0.006, 4, "rope", (1, 1, 1))
    # the rag of a flag, hanging limp off the halyard, torn to a ragged fly
    top = b + Vector((0.06, 7.8, 0))
    nu, nv = 6, 10
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    vs = {}
    for i in range(nu + 1):
        for j in range(nv + 1):
            u, v = i / nu, j / nv
            reach = 0.45 * (1 - 0.35 * v) * (0.7 + 0.3 * noise.noise(Vector((v * 4, 0.5, 2))))   # the fly torn short below
            x = u * reach
            y = -v * 1.3 * (1 - 0.15 * u)
            z = 0.08 * math.sin(u * 2.4 + v * 1.5) * u + 0.05 * noise.noise(Vector((u * 3, v * 3, 1)))
            vs[i, j] = bm.verts.new(G(top + Vector((x + 0.03 * v, y, z))))
    for i in range(nu):
        for j in range(nv):
            if i == nu - 1 and rng.random() < 0.35:   # rags off the fly
                continue
            f = bm.faces.new((vs[i, j], vs[i + 1, j], vs[i + 1, j + 1], vs[i, j + 1]))
            for lp, (a, c) in zip(f.loops, [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]):
                lp[uvl].uv = (a / nu, c / nv)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.008)
    emit(bm, "flag", (1, 1, 1), uv=None, smooth=True)
    export("flagpole", (0.7, -0.7, 0.2))


def upturned_boat():
    bx, gy, bz = LAKE["station"]["boat"]
    rng = random.Random(9)
    b = Vector((bx, gy, bz))
    pc = (0.36, 0.32, 0.27)
    # two sawhorses: a beam on splayed legs, braced
    for z in (-1.2, 1.2):
        box(b + Vector((0, 0.77, z)), (1.1, 0.07, 0.09), "post", pc, bevel=0.008)
        for sx in (-1, 1):
            for sz in (-1, 1):
                tube([b + Vector((sx * 0.5, 0, z + sz * 0.16)), b + Vector((sx * 0.38, 0.74, z + sz * 0.03))], 0.03, 5, "post", pc, caps=True)
            box(b + Vector((sx * 0.45, 0.3, z)), (0.04, 0.06, 0.34), "post", pc)
    # the hull: an aluminium jon boat, keel up, its gunwales on the sawhorses; the bow (+z) raked; a hole stove in its bottom
    c = b + Vector((0, 1.15, 0))
    nz, ns = 28, 14
    L = 4.2
    bm = bmesh.new()
    grid = []
    for i in range(nz + 1):
        zt = i / nz
        z = -L / 2 + L * zt
        bow = max(0.0, (zt - 0.72) / 0.28)
        wg = 0.66 * (1 - 0.25 * bow ** 1.5)
        wb = 0.5 * (1 - 0.45 * bow ** 1.3)
        depth = 0.6 * (1 - 0.72 * bow ** 1.6)
        row = []
        for j in range(ns + 1):
            s = j / ns   # across: 0 gunwale (left), 0.5 the bottom's middle, 1 gunwale (right)
            side = -1 if s < 0.5 else 1
            q = abs(s - 0.5) * 2   # 0 middle, 1 gunwale
            if q < 0.6:   # the flat bottom (up, the boat being upturned), a slight vee
                x = side * wb * q / 0.6
                y = -0.3 + depth - 0.03 * (1 - q / 0.6)   # (the gunwale stays on the sawhorses; the bottom drops to it at the bow)
            else:   # the round chine, then the flared side down to the gunwale
                k = (q - 0.6) / 0.4
                x = side * (wb + (wg - wb) * k)
                y = -0.3 + depth * (1 - k ** 0.8)
            row.append(bm.verts.new(G(c + Vector((x, y, z)))))
        grid.append(row)
    hole = []
    for i in range(nz):
        for j in range(ns):
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            zc = -L / 2 + L * (i + 0.5) / nz
            xc = (j + 0.5) / ns
            if -0.8 < zc < -0.3 and 0.38 < xc < 0.58:
                hole.append(f)
    # the transom (the stern, -z): closing the section
    bm.faces.new(grid[0][::-1])
    # the hole: its faces pushed in, torn, dark
    for f in hole:
        f.material_index = 1
        for v in f.verts:
            v.co.z -= 0.03 + 0.03 * rng.random()
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.02)
    emit(bm, ["metal", "void"], (0.55, 0.53, 0.5), uv="box", smooth=True)
    # the gunwale rails and two seats' ends showing, the bow's lifting eye
    for side in (-1, 1):
        pts = []
        for i in range(nz + 1):
            zt = i / nz
            bow = max(0.0, (zt - 0.72) / 0.28)
            pts.append(c + Vector((side * 0.66 * (1 - 0.25 * bow ** 1.5), -0.31, -L / 2 + L * zt)))
        tube(pts, 0.025, 6, "metal", (0.5, 0.48, 0.45))
    for z in (-0.9, 0.5):
        box(c + Vector((0, -0.18, z)), (1.15, 0.03, 0.25), "metal", (0.5, 0.48, 0.45), bevel=0.005)
    export("upturned_boat", (0.8, -0.6, 0.4))


# ------------------------------------------------------------------ the shore's growth


def reeds(variant, tall):
    """A reed stand: curved blade cards round a root, each a slice of the reed texture (its tips at the top, the shader
    swaying them by height), bending and twisting as they rise; the tall stands with a few cattails on their stems."""
    rng = random.Random(1240 + variant * 17 + (500 if tall else 0))
    cards = rng.randint(6, 9)
    a = rng.uniform(0, math.pi)
    steps = 5
    for i in range(cards):
        a += math.pi / cards * rng.uniform(0.5, 1.5)
        half = (0.42 if tall else 0.28) * rng.uniform(0.65, 1.25)
        h = (2.6 if tall else 1.45) * rng.uniform(0.7, 1.15)
        off = Vector((rng.uniform(-0.12, 0.12), 0, rng.uniform(-0.12, 0.12))) * (1.6 if tall else 1.0)
        d = Vector((math.cos(a), 0, math.sin(a))) * half
        n = Vector((-d.z, 0, d.x)).normalized()
        lean = n * rng.uniform(-0.22, 0.22) * h
        twist = rng.uniform(-0.35, 0.35)
        uw = rng.uniform(0.45, 0.8)
        u0 = rng.uniform(0, 1 - uw)
        flip = rng.random() < 0.5
        ua, ub = (u0 + uw, u0) if flip else (u0, u0 + uw)
        l = rng.uniform(0.82, 1.12)
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        rows = []
        for s in range(steps + 1):
            f = s / steps
            dd = Matrix.Rotation(twist * f, 3, "Y") @ d * (1 + 0.25 * f)   # (game axes here; y up)
            centre = off + lean * f * f + Vector((0, h * f, 0))
            rows.append((bm.verts.new(G(centre - dd)), bm.verts.new(G(centre + dd)), f))
        for s in range(steps):
            (l0, r0, f0), (l1, r1, f1) = rows[s], rows[s + 1]
            face = bm.faces.new((l0, r0, r1, l1))
            for lp, uv in zip(face.loops, [(ua, f0), (ub, f0), (ub, f1), (ua, f1)]):
                lp[uvl].uv = uv   # (Blender v: the game's 1 - v; the root at the game's v = 1)
        emit(bm, "reed", (min(l, 1), min(l, 1), min(l * rng.uniform(0.92, 1.05), 1)), uv=None, smooth=True)
    if tall:
        for i in range(rng.randint(3, 5)):
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(0.05, 0.25)
            p0 = Vector((math.cos(a) * r, 0, math.sin(a) * r))
            hh = rng.uniform(1.5, 2.2)
            lean = Vector((rng.uniform(-0.2, 0.2), 0, rng.uniform(-0.2, 0.2)))
            pts = [p0 + lean * (f * f) + Vector((0, hh * f, 0)) for f in (0, 0.35, 0.7, 1.0)]
            tube(pts, [0.009, 0.008, 0.006, 0.003], 5, "cattail", (0.85, 1.0, 0.55), u_len=1.0)
            # the head: a brown velvet sausage two thirds of the way up
            hp0 = pts[0].lerp(pts[3], 0.72)
            hp1 = pts[0].lerp(pts[3], 0.86)
            mid = (hp0 + hp1) * 0.5
            tube([hp0, hp0.lerp(mid, 0.4), mid, hp1.lerp(mid, 0.4), hp1], [0.012, 0.024, 0.027, 0.024, 0.012], 7, "cattail",
                 (1, 1, 1), caps=True)
    export(f"reed_tall{variant}" if tall else f"reed{variant}", (0.7, -0.7, 0.25))


def marsh_mat():
    """Marsh grass lying out over the water: fourteen blade cards fanning from a root, each arching up and laid down so
    its tip trails on the surface."""
    rng = random.Random(4731)
    for i in range(14):
        a = 2 * math.pi * i / 14 + rng.uniform(-0.2, 0.2)
        o = Vector((math.cos(a), 0, math.sin(a)))
        side = Vector((-o.z, 0, o.x)) * 0.28
        L = rng.uniform(0.9, 1.6)
        rise = rng.uniform(0.26, 0.38)
        roll = rng.uniform(-0.3, 0.3)
        l = rng.uniform(0.85, 1.15)
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        rows = []
        steps = 6
        for s in range(steps + 1):
            f = s / steps
            # a quadratic arc: up to the bend, then down to the water
            p = o * L * f + Vector((0, rise * 4 * f * (1 - f) * (1 - 0.3 * f) + 0.02 * f, 0))
            w = side * (0.6 + 0.6 * f)
            w = w + Vector((0, roll * f * 0.2, 0))
            rows.append((bm.verts.new(G(p - w)), bm.verts.new(G(p + w)), f))
        for s in range(steps):
            (l0, r0, f0), (l1, r1, f1) = rows[s], rows[s + 1]
            face = bm.faces.new((l0, r0, r1, l1))
            for lp, uv in zip(face.loops, [(0, f0), (1, f0), (1, f1), (0, f1)]):
                lp[uvl].uv = uv
        emit(bm, "marsh", (min(l, 1), min(l, 1), min(l * 0.9, 1)), uv=None, smooth=True)
    export("marsh_mat", (0.6, -0.6, 0.6))


def wrack():
    """Washed-up straw: two crumpled mats, the top one turned and smaller, rucked up over each other."""
    for y, rot, sc, seed in ((0.0, 0.0, 1.0, 1.0), (0.025, 0.6, 0.8, 5.0)):
        nu, nv = 10, 6
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        vs = {}
        for i in range(nu + 1):
            for j in range(nv + 1):
                u, v = i / nu, j / nv
                x, z = (u - 0.5) * 2.2 * sc, (v - 0.5) * 1.2 * sc
                edge = min(u, 1 - u, v, 1 - v)
                lift = 0.035 * (0.5 + noise.noise(Vector((x * 2.2 + seed, z * 2.2, 0.3)))) * min(edge * 6, 1)
                p = Vector((x * math.cos(rot) + z * math.sin(rot), y + max(lift, 0) + 0.005, -x * math.sin(rot) + z * math.cos(rot)))
                vs[i, j] = bm.verts.new(G(p))
        for i in range(nu):
            for j in range(nv):
                f = bm.faces.new((vs[i, j], vs[i + 1, j], vs[i + 1, j + 1], vs[i, j + 1]))
                for lp, (a, b) in zip(f.loops, [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]):
                    lp[uvl].uv = (a / nu, b / nv)
        emit(bm, "straw", (1, 1, 1), uv=None, smooth=True)
    export("wrack", (0.4, -0.6, 0.8))


def limbs(branches, key, budget=9000):
    """Skinned limbs (winter_trees.build_limbs: one continuous mesh, its bark UVs and colours)."""
    keep = WT.LIMB_TRIS
    WT.LIMB_TRIS = budget
    ob = WT.build_limbs({"branches": branches}, key)
    WT.LIMB_TRIS = keep
    PARTS.append(ob)
    return ob


def drift(seed):
    """A bleached branch, washed up: forked, a broken stub or two, lying half on the sand."""
    rng = random.Random(seed * 977)
    col = [0.82, 0.78, 0.7]
    a = Vector((-1.3, 0.05, 0))
    b = Vector((1.3, 0.08, rng.uniform(-0.3, 0.3)))
    mid = (a + b) * 0.5 + Vector((0, 0.06, 0.05))
    main = [list(a.lerp(mid, f)) + [0.085 - 0.02 * f] for f in (0, 0.33, 0.66)] + \
           [list(mid.lerp(b, f)) + [0.065 - 0.035 * f] for f in (0, 0.5, 1.0)]
    br = [{"rings": main, "col": col, "mat": "drift"}]
    for i in range(3):
        f = rng.uniform(0.25, 0.85)
        p = a.lerp(b, f) + Vector((0, 0.02, 0))
        tip = p + Vector((rng.uniform(-0.4, 0.4), rng.uniform(0.05, 0.25), rng.uniform(0.2, 0.6) * (1 if i % 2 == 0 else -1)))
        br.append({"rings": [list(p) + [0.04], list(p.lerp(tip, 0.5)) + [0.028], list(tip) + [0.012]], "col": col, "mat": "drift"})
    limbs(br, "drift", 2500)
    export(f"drift_{seed}", (0.5, -0.8, 0.5))


def rootwad(seed):
    """A root wad: the torn-up foot of a tree, its roots sprawling out low and tangled, a few reaching up and over."""
    rng = random.Random(seed * 977)
    c = Vector((0, 0.18, 0))
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    for v in bm.verts:
        d = v.co.normalized()
        k = 1 + 0.3 * noise.noise(d * 2.2 + Vector((seed, 0, 0)))
        v.co = G(c + Vector((d.x * 0.38, d.z * 0.2, -d.y * 0.32)) * k)
        v.co.z = max(v.co.z, 0.01)
    emit(bm, "drift", (0.5, 0.46, 0.4), smooth=True)
    col = [0.56, 0.52, 0.46]
    br = [{"rings": [list(c - Vector((0.2, 0, 0))) + [0.14], list(c + Vector((0.2, 0, 0))) + [0.12]], "col": col, "mat": "drift"}]
    for i in range(14):
        a = rng.uniform(0, 2 * math.pi)
        el = rng.uniform(0.3, 0.7) if i < 4 else rng.uniform(-0.15, 0.25)
        o = Vector((math.cos(a) * math.cos(el), math.sin(el), math.sin(a) * math.cos(el)))
        side = Vector((-o.z, 0, o.x))
        L = rng.uniform(0.6, 1.4)
        p1 = c + o * L * 0.5 + side * rng.uniform(-0.2, 0.2)
        p2 = c + o * L + side * rng.uniform(-0.3, 0.3)
        if i >= 4:
            p1.y = max(0.04, p1.y)
            p2.y = rng.uniform(0.02, 0.12)
        p3 = p2 + (p2 - p1).normalized() * 0.3 + side * rng.uniform(-0.3, 0.3) + Vector((0, -0.25 if i < 4 else 0, 0))
        p3.y = max(p3.y, 0.02)
        r0 = rng.uniform(0.035, 0.07)
        br.append({"rings": [list(c + o * 0.2) + [r0], list(p1) + [r0 * 0.75], list(p2) + [r0 * 0.45], list(p3) + [0.012]],
                   "col": col, "mat": "drift"})
    limbs(br, "rootwad", 5000)
    export(f"rootwad_{seed}", (0.6, -0.7, 0.6))


def lily_pad():
    """A lily pad: a round leaf cupped a little, its notch, its rim curling up, radial veins in its colour."""
    segs, rings, notch, R0 = 16, 3, 0.5, 0.34   # (two hundred of them float on the lake: kept light)
    bm = bmesh.new()
    cl = None
    centre = bm.verts.new(G(0, 0.012, 0))
    grid = []
    for j in range(1, rings + 1):
        rr = R0 * j / rings
        row = []
        for i in range(segs + 1):
            a = notch * 0.5 + (2 * math.pi - notch) * i / segs
            wob = 1 + 0.03 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, 0.5)))
            y = 0.012 * (1 - (j / rings) ** 2) + 0.022 * max(0, (j / rings - 0.75) / 0.25) ** 2   # cupped; the rim curled up
            row.append(bm.verts.new(G(math.cos(a) * rr * wob, y, math.sin(a) * rr * wob)))
        grid.append(row)
    faces = []
    for i in range(segs):
        faces.append((bm.faces.new((centre, grid[0][i + 1], grid[0][i])), i))
    for j in range(rings - 1):
        for i in range(segs):
            faces.append((bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i])), i))
    cl = bm.loops.layers.color.new("Col")
    for f, i in faces:
        for lp in f.loops:
            # the veins: the code's alternating wedges, softened to a line down each wedge's middle
            lp[cl] = (0.9 + 0.1 * (i % 2), 1.0, 0.85, 1.0)
    emit(bm, "lily", (1, 1, 1), smooth=True)
    # (emit painted every corner one colour: put the wedges back)
    ob = PARTS[-1]
    me = ob.data
    ca = me.color_attributes["Col"]
    for p in me.polygons:
        c = p.center
        a = math.atan2(-c.y, c.x) % (2 * math.pi)
        i = int((a - notch * 0.5) / ((2 * math.pi - notch) / segs))
        col = (0.9 + 0.1 * (i % 2), 1.0, 0.85, 1.0)
        for li in p.loop_indices:
            ca.data[li].color = col
    export("lily_pad", (0.3, -0.5, 0.8))


# ------------------------------------------------------------------ the bald cypresses


CYPRESS = None


def cypress(seed):
    """A bald cypress: its trunk flaring wide at the foot, fluted with buttresses down the flare, its roots heaved up and
    twisting out across the ground and down into the water, knees poking up round it (all one skinned mesh, modelled
    here); the crown and the moss in it are the code's (the woods' fir crown, ForestScatter.FirCrown), carried over."""
    global CYPRESS
    if CYPRESS is None:
        CYPRESS = json.load(open(os.path.join(HERE, "data", "cypress.json")))
    rec = CYPRESS[f"cypress_{seed}"]
    h = rec["height"]
    lx, lz = rec["lean"]
    rng = random.Random(seed * 7717)
    def axis(y):
        f = (y / h) ** 2 * 2
        return Vector((lx * f, y, lz * f))
    bark = [0.55, 0.5, 0.45]
    trunk = [(Vector((0, -0.4, 0)), 1.3), (Vector((0, 0, 0)), 1.0), (axis(0.25), 0.74), (axis(0.5), 0.57), (axis(0.85), 0.47),
             (axis(1.3), 0.41), (axis(2.4), 0.34), (axis(h * 0.25), 0.3), (axis(h * 0.5), 0.26), (axis(h * 0.75), 0.17),
             (axis(h * 0.95), 0.06)]
    br = [{"rings": [list(p) + [r] for p, r in trunk], "col": [c * 0.95 for c in bark], "mat": "bark"}]
    # the flutes: buttresses down the flare
    for i in range(7):
        a = 2 * math.pi * i / 7 + rng.uniform(-0.2, 0.2)
        o = Vector((math.cos(a), 0, math.sin(a)))
        pts = []
        for y, r in ((-0.15, 1.05), (0.15, 0.8), (0.45, 0.58), (0.85, 0.45), (1.5, 0.37), (2.3, 0.31)):
            yy = max(y, 0)
            pts.append(list(o * r + axis(yy) - Vector((0, yy, 0)) + Vector((0, y, 0))) + [0.22 - 0.13 * (y + 0.15) / 2.45])
        br.append({"rings": pts, "col": [c * 0.95 for c in bark], "mat": "bark"})
    # the roots: heaved up, twisting out, down into the water; a smaller one off each, curling back
    for i in range(11):
        a = rng.uniform(0, 2 * math.pi)
        o = Vector((math.cos(a), 0, math.sin(a)))
        side = Vector((-o.z, 0, o.x))
        L = rng.uniform(1.4, 3.2)
        r0 = rng.uniform(0.09, 0.15)
        pts = [o * 0.75 + Vector((0, 0.25, 0))]
        for j in range(1, 6):
            u = j / 5
            pts.append(o * (0.75 + L * u) + side * rng.uniform(-0.35, 0.35) * u +
                       Vector((0, 0.3 * math.sin(u * math.pi) + rng.uniform(0, 0.12) - 0.3 * u * u, 0)))
        cc = [c * rng.uniform(0.7, 0.95) for c in bark]
        br.append({"rings": [list(p) + [r0 * (1 - 0.8 * j / 5) + 0.02] for j, p in enumerate(pts)], "col": cc, "mat": "bark"})
        mid = pts[2]
        br.append({"rings": [list(mid) + [r0 * 0.5], list(mid + side * 0.4 + Vector((0, 0.12, 0))) + [r0 * 0.35],
                             list(mid + side * 0.7 - o * 0.2 - Vector((0, 0.1, 0))) + [0.015]], "col": cc, "mat": "bark"})
    limbs(br, f"cypress_{seed}", 12000)
    # knees: knobbed stumps poking up round it
    for i in range(6):
        a, r = rng.uniform(0, 2 * math.pi), rng.uniform(1.6, 3.2)
        p = Vector((math.cos(a) * r, -0.2, math.sin(a) * r))
        hh = rng.uniform(0.3, 0.75)
        tip = p + Vector((rng.uniform(-0.05, 0.05), hh, 0))
        limbs([{"rings": [list(p) + [0.14], list(p.lerp(tip, 0.5)) + [0.1], list(tip) + [0.06]], "col": [c * 0.85 for c in bark],
                "mat": "bark"}], f"knee{i}", 400)
    # the crown (its wood, cores, sprays) and the moss: the code's, as it laid them
    surf = rec["surfaces"]
    def tri_ok_limbs(ps):
        c = (ps[0] + ps[1] + ps[2]) / 3
        if c.y < 0.3 * h - 0.4:
            return False
        ax = axis(c.y)
        rr = 0.26 + (0.34 - 0.26) * max(0.0, min(1.0, (h * 0.5 - c.y) / (h * 0.5 - 2.4))) if c.y < h * 0.5 else \
            0.26 + (0.06 - 0.26) * (c.y - h * 0.5) / (h * 0.45)
        return math.hypot(c.x - ax.x, c.z - ax.z) > rr + 0.01
    for key, mname in (("limbs", "bark"), ("branch", "branch"), ("spray", "spray"), ("moss", "moss")):
        s = surf.get(key)
        if not s:
            continue
        v, n, uv, col, idx = s["v"], s["n"], s["uv"], s["c"], s["i"]
        P = [Vector((v[3 * i], v[3 * i + 1], v[3 * i + 2])) for i in range(len(v) // 3)]
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        cl = bm.loops.layers.color.new("Col")
        bv = [bm.verts.new(G(p)) for p in P]
        normals = []
        for t in range(0, len(idx), 3):
            a, b, c = idx[t], idx[t + 1], idx[t + 2]
            if key == "limbs" and not tri_ok_limbs((P[a], P[b], P[c])):
                continue
            # (Godot winds clockwise seen from the front; Blender counter-clockwise)
            try:
                f = bm.faces.new((bv[a], bv[c], bv[b]))
            except ValueError:
                continue
            for lp, k in zip(f.loops, (a, c, b)):
                lp[uvl].uv = (uv[2 * k], 1 - uv[2 * k + 1])
                lp[cl] = (col[3 * k], col[3 * k + 1], col[3 * k + 2], 1.0) if col else (1, 1, 1, 1)
                normals.append(G(n[3 * k], n[3 * k + 1], n[3 * k + 2]))
        bmesh.ops.delete(bm, geom=[x for x in bm.verts if not x.link_faces], context="VERTS")
        me = bpy.data.meshes.new(key)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(key, me)
        bpy.context.scene.collection.objects.link(ob)
        me.materials.append(mat(mname))
        for p in me.polygons:
            p.use_smooth = True
        if hasattr(me, "use_auto_smooth"):
            me.use_auto_smooth = True
        try:
            me.normals_split_custom_set(normals)   # (the code's normals: the sprays lit as their shader expects)
        except Exception as e:
            print("[lake] custom normals:", e)
        me.color_attributes.active_color = me.color_attributes["Col"]
        PARTS.append(ob)
    export(f"cypress_{seed}", (0.6, -0.9, 0.35))


BUILDERS = {
    "dock": dock, "rescue_station": station, "radio_mast": radio_mast, "flagpole": flagpole, "upturned_boat": upturned_boat,
    "marsh_mat": marsh_mat, "wrack": wrack, "drift_5": lambda: drift(5), "rootwad_6": lambda: rootwad(6), "lily_pad": lily_pad,
}
for _v in range(3):
    BUILDERS[f"reed{_v}"] = (lambda v: lambda: reeds(v, False))(_v)
    BUILDERS[f"reed_tall{_v}"] = (lambda v: lambda: reeds(v, True))(_v)
for _s in (1, 2, 3):
    BUILDERS[f"cypress_{_s}"] = (lambda s: lambda: cypress(s))(_s)


if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(BUILDERS)
    for name in want:
        reset()
        BUILDERS[name]()
