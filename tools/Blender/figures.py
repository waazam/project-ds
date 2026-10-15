"""The minor figures, modelled in Blender (2026-10-15, the owner: "from now on everything should be made in blender ... anything
that isn't made in blender should probably be redone in blender"). Run headless:

    blender -b --python tools/Blender/figures.py [-- name ...]      (names: rail_figure, sheet_figure, moth)

Each goes to assets/models/figures/<name>.glb with a preview in build/blender/<name>.png. Blender is Z-up; the glTF export
turns it Y-up with Blender's +Y becoming the game's -Z, so every figure faces Blender's +Y (the game's -Z, what LookAt aims).

- rail_figure (Act 14, the stairwell): the tall thin thing at the rail below, in a long coat, its head tipped back to look up
  the well at whoever's coming down, arms hanging past its knees, long fingers. One continuous skinned body (no stacked
  cylinders). The game paints it its own near-black; the two eyes are their own mesh ("Eyes") for a faint glint.
- sheet_figure (Act 23, the dining hall): a dust sheet thrown over someone standing, simulated as cloth over a body so it
  falls in real folds; the head, the shoulders and the faint relief of a face pressed into it. Two poses in one file:
  "SheetA" standing square, "SheetB" its head dropped to one side and one arm a little forward under the cloth. UVs in metres
  (the game's linen tiles by the metre).
- moth (the summer woods, at the lantern): a small dusty moth, two centimetres across: a furry body and two wings on their
  own pivots ("Body", "WingL", "WingR") so the game flaps them; a small drawn texture of its wing pattern.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "figures")
os.makedirs(kit.OUT, exist_ok=True)
os.makedirs(os.path.join(kit.OUT, "textures"), exist_ok=True)


def remesh_smooth(ob, voxel=0.012, smooth=4):
    """Fuse a skinned body into one even skin (no seams where branches meet), then relax it."""
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel)
    add_mod(ob, "SMOOTH", factor=0.6, iterations=smooth)
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True


def displace(ob, strength, size, seed=0):
    tex = bpy.data.textures.new(f"n{seed}", "CLOUDS")
    tex.noise_scale = size
    tex.noise_depth = 2
    m = add_mod(ob, "DISPLACE", strength=strength, mid_level=0.5)
    m.texture = tex
    m.texture_coords = "GLOBAL"
    apply_mods(ob)


def decimate(ob, ratio):
    add_mod(ob, "DECIMATE", ratio=ratio)
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True


# ------------------------------------------------------------------ the figure at the rail


def rail_figure():
    clear()
    V, E, R = [], [], []

    def add(p, r, parent=None):
        V.append(Vector(p))
        R.append(r)
        i = len(V) - 1
        if parent is not None:
            E.append((parent, i))
        return i

    # the coat: a tall narrow column flaring to the hem, deeper at the chest than the waist (x wide, y deep)
    hem = add((0, 0.0, 0.04), (0.29, 0.22))
    c1 = add((0, 0.0, 0.45), (0.24, 0.18), hem)
    c2 = add((0, 0.01, 0.9), (0.18, 0.14), c1)
    c3 = add((0, 0.02, 1.25), (0.17, 0.13), c2)
    sh = add((0, 0.02, 1.52), (0.23, 0.13), c3)
    # the neck, long, and the head tipped back: looking up the well
    n1 = add((0, 0.02, 1.64), (0.055, 0.055), sh)
    n2 = add((0, -0.01, 1.73), (0.05, 0.05), n1)
    hd = add((0, -0.05, 1.83), (0.095, 0.11), n2)
    add((0, -0.08, 1.93), (0.075, 0.085), hd)
    # the jaw hanging a little open under the tipped head
    add((0, 0.03, 1.8), (0.05, 0.04), hd)
    # the arms: from narrow shoulders down past the knees, a hand of long fingers at the end of each
    for s in (-1, 1):
        a0 = add((s * 0.23, 0.02, 1.5), (0.055, 0.055), sh)
        a1 = add((s * 0.3, 0.06, 1.08), (0.04, 0.04), a0)
        a2 = add((s * 0.32, 0.08, 0.62), (0.032, 0.032), a1)
        hand = add((s * 0.32, 0.09, 0.46), (0.03, 0.022), a2)
        for f in range(4):
            sp = (f - 1.5) * 0.012
            f1 = add((s * (0.32 + sp * 0.3), 0.09 + sp, 0.38), (0.009, 0.009), hand)
            f2 = add((s * (0.322 + sp * 0.5), 0.095 + sp * 1.4, 0.27 - abs(sp) * 2.0), (0.007, 0.007), f1)
            add((s * (0.325 + sp * 0.5), 0.105 + sp * 1.6, 0.18 - abs(sp) * 3.0), (0.005, 0.005), f2)
    body = skin_tree("Figure", V, E, R, None, subdiv=1)
    remesh_smooth(body, voxel=0.01, smooth=3)
    # the coat's hem ragged and the cloth creased a little
    bm = bmesh.new()
    bm.from_mesh(body.data)
    for v in bm.verts:
        if v.co.z < 0.25:
            n = fbm(v.co.x * 9, v.co.y * 9, 3.0, 3)
            v.co.z = max(0.0, v.co.z + n * 0.05 * (1 - v.co.z / 0.25))
    bm.to_mesh(body.data)
    bm.free()
    displace(body, 0.012, 0.06, 1)
    decimate(body, 0.35)
    uv_box(body, 1.0)
    body.data.materials.append(mat("figure", (0.03, 0.03, 0.03), 0.95))
    # the eyes: two pin-points in the tipped-back face (the game makes them glint)
    eyes = []
    for s in (-1, 1):
        e = lumpy_sphere(f"Eye{s}", (s * 0.033, 0.03, 1.86), (0.009, 0.006, 0.009), mat("eyes", (0.7, 0.75, 0.8), 0.3, emit=(0.4, 0.45, 0.5)), 3, amp=0.0, subdiv=2)
        eyes.append(e)
    eye = join(eyes, "Eyes")
    export([body, eye], "rail_figure")
    preview("rail_figure", (0, 0, 1.0), 3.2, height=0.4, yaw=20)


# ------------------------------------------------------------------ the standing sheet


def person(pose):
    """The body under the sheet (a collision form; never exported)."""
    V, E, R = [], [], []

    def add(p, r, parent=None):
        V.append(Vector(p))
        R.append(r)
        i = len(V) - 1
        if parent is not None:
            E.append((parent, i))
        return i

    tilt = 0.12 if pose == "B" else 0.0
    pelvis = add((0, 0, 0.95), (0.17, 0.12))
    for s in (-1, 1):
        k = add((s * 0.1, 0.01, 0.5), (0.07, 0.07), pelvis)
        add((s * 0.11, 0.0, 0.06), (0.06, 0.08), k)
    chest = add((0, 0.01, 1.3), (0.19, 0.12), pelvis)
    sh = add((0, 0.0, 1.47), (0.22, 0.11), chest)
    neck = add((tilt * 0.3, 0.01, 1.58), (0.05, 0.05), sh)
    head = add((tilt, 0.015, 1.71), (0.09, 0.105), neck)
    add((tilt * 1.2, 0.0, 1.8), (0.075, 0.085), head)
    # the face's relief: a brow, a nose, a chin (what the sheet will press against)
    add((tilt, 0.1, 1.73), (0.04, 0.02), head)
    add((tilt, 0.115, 1.69), (0.016, 0.022), head)
    add((tilt, 0.09, 1.63), (0.035, 0.02), head)
    for s in (-1, 1):
        fwd = 0.18 if (pose == "B" and s > 0) else 0.0
        e = add((s * 0.24, 0.02 + fwd * 0.5, 1.15), (0.045, 0.045), sh)
        add((s * 0.24, 0.05 + fwd, 0.85 + fwd * 0.6), (0.04, 0.04), e)
    ob = skin_tree(f"Person{pose}", V, E, R, None, subdiv=1)
    return ob


def drape(form, name, seed):
    """A square sheet dropped onto the form and the floor, simulated as cloth, the result kept as a mesh."""
    sc = bpy.context.scene
    floor = box("Floor", (0, 0, -0.05), (6, 6, 0.1))
    for o in (form, floor):
        o.modifiers.new("collision", "COLLISION")
        o.collision.thickness_outer = 0.012
        o.collision.cloth_friction = 6.0
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=90, y_subdivisions=90, size=2.7, location=(0, 0, 1.95))
    sheet = bpy.context.active_object
    sheet.name = name
    # a little rumple to start from, so it doesn't fall as a perfect square
    rnd = random.Random(seed)
    sheet.rotation_euler = (0, 0, rnd.uniform(-0.4, 0.4))
    bpy.ops.object.transform_apply(rotation=True)
    for v in sheet.data.vertices:
        v.co.z += fbm(v.co.x * 2 + seed, v.co.y * 2, 0.5, 3) * 0.04
    cl = sheet.modifiers.new("cloth", "CLOTH")
    s = cl.settings
    s.quality = 10
    s.mass = 0.25
    s.tension_stiffness = 12
    s.compression_stiffness = 12
    s.shear_stiffness = 6
    s.bending_stiffness = 0.4
    s.air_damping = 1.5
    cl.collision_settings.use_self_collision = True
    cl.collision_settings.self_distance_min = 0.008
    cl.collision_settings.distance_min = 0.008
    cl.point_cache.frame_start = 1
    cl.point_cache.frame_end = 90
    sc.frame_start, sc.frame_end = 1, 90
    for f in range(1, 91):
        sc.frame_set(f)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(sheet.evaluated_get(dg))
    out = bpy.data.objects.new(name + "_baked", me)
    sc.collection.objects.link(out)
    for o in (sheet, floor):
        bpy.data.objects.remove(o, do_unlink=True)
    out.name = name
    # tidy: drop what lies flat on the floor far out, keep the pooled skirt
    bm = bmesh.new()
    bm.from_mesh(out.data)
    far = [v for v in bm.verts if v.co.z < 0.02 and Vector((v.co.x, v.co.y)).length > 0.75]
    bmesh.ops.delete(bm, geom=far, context="VERTS")
    for v in bm.verts:
        v.co.z = max(v.co.z, 0.004)
    bm.to_mesh(out.data)
    bm.free()
    for p in out.data.polygons:
        p.use_smooth = True
    add_mod(out, "SMOOTH", factor=0.3, iterations=2)
    apply_mods(out)
    decimate(out, 0.45)
    uv_box(out, 1.0)
    out.data.materials.append(mat("linen", (0.82, 0.8, 0.74), 0.9))
    return out


def sheet_figure():
    clear()
    sheets = []
    for i, pose in enumerate(("A", "B")):
        form = person(pose)
        sh = drape(form, f"Sheet{pose}", 11 + i * 7)
        bpy.data.objects.remove(form, do_unlink=True)
        sheets.append(sh)
    export(sheets, "sheet_figure")
    sheets[1].location.x += 1.2   # (side by side for the preview only, after the export)
    preview("sheet_figure", (0.6, 0, 0.9), 3.6, height=0.3, yaw=15)


# ------------------------------------------------------------------ the moth


def moth():
    clear()
    S = 0.001   # millimetres
    tex = make_texture("moth_wing", 64, lambda u, v: wing_pattern(u, v))
    wing_m = mat("moth_wing", (1, 1, 1), 1.0, tex=tex)
    body_m = mat("moth_body", (0.42, 0.36, 0.28), 1.0)
    # the body: a furry thorax and a tapering abdomen, a small head, two feathered antennae
    V, E, R = [], [], []

    def add(p, r, parent=None):
        V.append(Vector(p) * S)
        R.append((r[0] * S, r[1] * S))
        i = len(V) - 1
        if parent is not None:
            E.append((parent, i))
        return i

    tail = add((0, -9, 0), (0.6, 0.6))
    ab = add((0, -5, 0.2), (1.3, 1.2), tail)
    th = add((0, -1, 0.5), (1.6, 1.5), ab)
    hd = add((0, 1.8, 0.4), (1.0, 1.0), th)
    for s in (-1, 1):
        a1 = add((s * 1.5, 5.5, 1.5), (0.2, 0.2), hd)
        add((s * 3.0, 8.0, 2.0), (0.12, 0.12), a1)
    body = skin_tree("Body", V, E, R, body_m, subdiv=1)
    # the wings: forewing and hindwing in one flat shape each side, pivoting at the body's side (their origin)
    wings = []
    for s, name in ((-1, "WingL"), (1, "WingR")):
        outline = []
        n = 28
        for k in range(n + 1):
            t = k / n
            a = math.pi * t
            # a broad forewing sweeping back to a rounded hindwing
            rx = 11.0 * math.sin(a) ** 0.8 * (1.0 - 0.25 * math.sin(a * 2.0) ** 2)
            ry = 6.0 * math.cos(a) + 1.5 - 4.0 * t
            outline.append(Vector((s * rx, ry, 0)) * S)
        bm = bmesh.new()
        vs = [bm.verts.new(p) for p in outline]
        bm.faces.new(vs)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=2, use_grid_fill=True)
        for v in bm.verts:
            v.co.z += 0.0003 * math.sin(v.co.x / S * 0.6)   # a faint ripple, so it isn't a card
        for v in bm.verts:
            v.co.x += s * 0.2 * S   # tucked under the thorax's side; the pivot (the origin) stays at the body
        w = obj_from_bmesh(bm, name, wing_m)
        # planar UVs: the drawn pattern laid over each wing (mirrored for the left)
        uvl = w.data.uv_layers.new()
        for poly in w.data.polygons:
            for li in poly.loop_indices:
                co = w.data.vertices[w.data.loops[li].vertex_index].co / S
                uvl.data[li].uv = (abs(co.x) / 13.0, (co.y + 9.0) / 18.0)
        w.location = (s * 0.8 * S, -1.0 * S, 1.0 * S)
        wings.append(w)
    export([body] + wings, "moth")
    for o in [body] + wings:   # (the preview only, after the export: twenty times life, the camera clips at 10 cm)
        o.scale = (20, 20, 20)
        o.location = o.location * 20
    preview("moth", (0, 0, 0), 1.0, height=0.7, yaw=20)


def wing_pattern(u, v):
    """A dusty grey-brown wing: a darker band across it, a pale crescent, scales mottled, the edge darker."""
    base = 0.55 + 0.12 * fbm(u * 18, v * 18, 1.0, 3)
    band = math.exp(-((v - 0.55 - 0.15 * u) / 0.07) ** 2) * 0.25
    spot = math.exp(-(((u - 0.55) / 0.08) ** 2 + ((v - 0.68) / 0.06) ** 2)) * 0.2
    edge = max(0.0, u - 0.82) * 1.2
    l = max(0.08, base - band - edge + spot)
    return (l * 1.0, l * 0.9, l * 0.74)


# ------------------------------------------------------------------ the crypt's hooded figure


def crypt_figure():
    """Act 21: a hooded figure in stone on a low plinth, head bowed under the hood, looking down at its cupped hands; the
    robe falling straight in heavy folds, worn, its edges softened by age. The game paints it its own damp ashlar."""
    clear()
    V, E, R = [], [], []

    def add(p, r, parent=None):
        V.append(Vector(p))
        R.append(r)
        i = len(V) - 1
        if parent is not None:
            E.append((parent, i))
        return i

    base = add((0, 0, 0.32), (0.34, 0.3))
    k1 = add((0, 0.01, 0.75), (0.29, 0.25), base)
    k2 = add((0, 0.02, 1.15), (0.24, 0.2), k1)
    sh = add((0, 0.02, 1.42), (0.25, 0.17), k2)
    # the hood: a deep cowl over the bowed head, falling forward
    nk = add((0, 0.05, 1.55), (0.12, 0.12), sh)
    hd = add((0, 0.12, 1.66), (0.15, 0.16), nk)
    add((0, 0.06, 1.78), (0.13, 0.13), hd)
    add((0, 0.2, 1.62), (0.11, 0.06), hd)   # the hood's lip, forward over the face
    # the arms in their sleeves, bent, the hands cupped together before the chest
    for s in (-1, 1):
        a0 = add((s * 0.22, 0.03, 1.38), (0.075, 0.075), sh)
        a1 = add((s * 0.24, 0.08, 1.08), (0.07, 0.07), a0)
        cuff = add((s * 0.1, 0.24, 1.14), (0.075, 0.065), a1)
        add((s * 0.035, 0.29, 1.17), (0.045, 0.035), cuff)
    body = skin_tree("Figure", V, E, R, None, subdiv=1)
    remesh_smooth(body, voxel=0.012, smooth=3)
    # the robe's folds: vertical ridges below the waist, worn by water and age
    bm = bmesh.new()
    bm.from_mesh(body.data)
    for v in bm.verts:
        if v.co.z < 1.2:
            a = math.atan2(v.co.y, v.co.x)
            fold = math.sin(a * 9 + v.co.z * 1.5) * 0.018 * (1.2 - v.co.z) / 0.9
            d = Vector((v.co.x, v.co.y, 0))
            if d.length > 1e-4:
                v.co += d.normalized() * fold
        # the hood's mouth: pushed in, a shadowed hollow where the face would be
        if v.co.z > 1.52 and v.co.z < 1.72 and v.co.y > 0.18 and abs(v.co.x) < 0.09:
            v.co.y -= 0.05 * (1 - abs(v.co.x) / 0.09)
    bm.to_mesh(body.data)
    bm.free()
    displace(body, 0.008, 0.08, 3)
    # the plinth: a worn block
    plinth = box("Plinth", (0, 0, 0.15), (0.84, 0.84, 0.3), bevel=0.03, segs=2)
    fig = join([body, plinth], "CryptFigure")
    decimate(fig, 0.4)
    uv_box(fig, 1.0)
    fig.data.materials.append(mat("stone", (0.55, 0.53, 0.5), 0.95))
    export([fig], "crypt_figure")
    preview("crypt_figure", (0, 0, 0.9), 3.2, height=0.6, yaw=25)


# ------------------------------------------------------------------ the station's growths


def flesh_growth():
    """Act 13, the lobby's rot: what grows out through the plaster, three of them. Fused lumps of flesh, not balls: a
    bulging mass with creases and folds, smaller nodules budding off it, a few cord-like veins over it, its root spread flat
    like a stain where it comes out of the wall. Each sits on its root at z = 0 (the wall's face), growing up +Z (out of
    the wall) and about a metre across; the game scales, turns and paints it."""
    clear()
    out = []
    for vi in range(3):
        rnd = random.Random(500 + vi * 13)
        # the mass: overlapping lobes and the buds off them, fused into one skin by the remesh (no seams, no tearing)
        parts, lobes = [], []
        for j in range(rnd.randint(4, 6)):
            a = rnd.uniform(0, math.tau)
            d = rnd.uniform(0.05, 0.26)
            c = Vector((math.cos(a) * d, math.sin(a) * d, rnd.uniform(0.06, 0.2)))
            r = rnd.uniform(0.13, 0.22)
            lobes.append((c, r))
            parts.append(lumpy_sphere(f"L{vi}{j}", c, (r, r * rnd.uniform(0.8, 1.0), r * rnd.uniform(0.7, 0.95)), None, vi * 20 + j, amp=0.12, freq=2.6, subdiv=3))
        for j in range(rnd.randint(5, 8)):   # the buds
            c, r = rnd.choice(lobes)
            off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.3, 1))).normalized() * r * rnd.uniform(0.85, 1.05)
            rb = rnd.uniform(0.035, 0.075)
            parts.append(lumpy_sphere(f"B{vi}{j}", c + off, (rb, rb, rb), None, vi * 40 + j, amp=0.1, freq=3.0, subdiv=2))
        ob = join(parts, f"Mass{vi}")
        remesh_smooth(ob, voxel=0.012, smooth=3)
        # creases and folds over the mass, the root flattened like a stain on the wall
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        for v in bm.verts:
            c = v.co
            fold = fbm(c.x * 7 + vi, c.y * 7, c.z * 7, 3)
            n = Vector((c.x, c.y, max(c.z, 0.05))).normalized()
            c += n * (fold * 0.035 - abs(math.sin(c.x * 22 + c.y * 9)) * 0.008)
            c.z = max(0.0, c.z)
        bm.to_mesh(ob.data)
        bm.free()
        # the root: a low flat spread where it comes out of the wall, like a stain with a lip
        stain = lumpy_sphere(f"Root{vi}", (0, 0, 0.0), (0.44, 0.38, 0.06), None, 70 + vi, amp=0.14, freq=3.5, subdiv=4, flat_bottom=0.0)
        # veins: thin cords pressed onto it (shrink-wrapped to its surface, not sticking out)
        veins = []
        for j in range(3):
            a = rnd.uniform(0, math.tau)
            pts = [Vector((math.cos(a) * 0.42, math.sin(a) * 0.42, 0.05))]
            for t in range(5):
                pts.append(pts[-1].lerp(Vector((rnd.uniform(-0.08, 0.08), rnd.uniform(-0.08, 0.08), 0.3)), 0.28))
            ee = [(i, i + 1) for i in range(len(pts) - 1)]
            rr = [max(0.004, 0.011 - i * 0.0014) for i in range(len(pts))]
            vein = skin_tree(f"Vein{vi}{j}", pts, ee, rr, None, subdiv=1)
            sw = add_mod(vein, "SHRINKWRAP", target=ob, wrap_method="NEAREST_SURFACEPOINT", offset=0.006)
            apply_mods(vein)
            veins.append(vein)
        g = join([ob, stain] + veins, f"Growth{'ABC'[vi]}")
        remesh_smooth(g, voxel=0.012, smooth=1)
        decimate(g, 0.5)
        uv_box(g, 2.0)
        g.data.materials.append(mat("meat", (0.45, 0.22, 0.2), 0.6))
        g.location.x = vi * 1.3   # (side by side; the export applies it, the game re-centres each by its own origin)
        out.append(g)
    for g in out:
        g.location.x = 0
    export(out, "flesh_growth")
    for i, g in enumerate(out):
        g.location.x = i * 1.2
    preview("flesh_growth", (1.2, 0, 0.2), 3.0, height=1.6, yaw=10)


BUILDERS = {"rail_figure": rail_figure, "sheet_figure": sheet_figure, "moth": moth, "crypt_figure": crypt_figure, "flesh_growth": flesh_growth}

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for n in (argv or list(BUILDERS)):
        BUILDERS[n]()
