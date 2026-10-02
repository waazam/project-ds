"""The furniture library (the owner, 2026-09-30: a full fidelity pass on the game's furniture and interiors; far more
geometry, baked maps, micro-surface detail). Each piece is modelled here, headless in Blender 4.0, and exported to
assets/models/furniture/<name>.glb with its baked cavity map, <name>_cavity.png:

    blender -b --python tools/Blender/furniture.py [-- name ...]

How a piece is made to sit with the game's own materials (the lodge's leather, velvet and wood are the game's, drawn
from Poly Haven photos with their own normal maps):
  - its faces are grouped by role (material slot names: upholstery, wood, metal, linen, pillow, cover, top, shade...);
    the game puts its own material on each role (FurnitureKit);
  - its first UV set is box-projected at a metre a repeat, as the game's own builder's, so those materials tile on
    it exactly as on everything else;
  - its second UV set is a unique layout, onto which the piece's cavity map is baked: ambient occlusion (the dark in
    the tufts, the creases, under the arms, where a leg meets the floor) with its sharp creases darkened further.
    The game multiplies that in (a detail map on the second UV set) and uses it as the occlusion too.
The geometry carries the rest: tufting, rolled arms, piping, turned legs, mouldings, carving.

Blender's Z up, its base on z = 0, centred on x = 0. The builders below make each piece with its front toward -Y; the
export mirrors it so its front is +Y (the game's -Z, as the game's own furniture faces).
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, apply_mods, add_mod, obj_from_bmesh, fbm, ROOT, preview

OUT = os.path.join(ROOT, "assets", "models", "furniture")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREV, exist_ok=True)
V = Vector
CAVITY = 1024

# preview colours per role (the export's materials only carry the role names; the game swaps its own in)
ROLE_COLOURS = {
    "upholstery": (0.35, 0.08, 0.06), "wood": (0.2, 0.11, 0.06), "metal": (0.6, 0.45, 0.2), "linen": (0.8, 0.78, 0.72),
    "pillow": (0.82, 0.8, 0.75), "cover": (0.3, 0.12, 0.35), "top": (0.04, 0.035, 0.035), "shade": (0.85, 0.7, 0.45),
    "glass": (0.1, 0.12, 0.1), "paper": (0.8, 0.76, 0.66), "black": (0.03, 0.03, 0.03),
}


def role(name):
    return mat(name, ROLE_COLOURS.get(name, (0.5, 0.5, 0.5)), 0.7)


# ------------------------------------------------------------------ geometry helpers

def finish(ob, smooth=True, bevel=0.0, segs=2, subsurf=0):
    if bevel > 0:
        add_mod(ob, "BEVEL", width=bevel, segments=segs, limit_method="ANGLE")
    if subsurf:
        add_mod(ob, "SUBSURF", levels=subsurf, render_levels=subsurf)
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = smooth
    return ob


def block(name, centre, size, material, bevel=0.01, segs=2, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=V(size), verts=bm.verts)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(V(centre)) @ Matrix.LocRotScale(None, __import__("mathutils").Euler(rot), None), verts=bm.verts)
    ob = obj_from_bmesh(bm, name, material, smooth=False)
    return finish(ob, True, bevel, segs) if bevel > 0 else ob


def lathe(name, profile, material, segs=24, axis_pos=(0, 0, 0)):
    """A turned piece: profile [(radius, z), ...] spun round the Z axis at axis_pos."""
    bm = bmesh.new()
    rings = []
    for (r, z) in profile:
        ring = []
        for i in range(segs):
            a = i / segs * math.tau
            ring.append(bm.verts.new((axis_pos[0] + math.cos(a) * r, axis_pos[1] + math.sin(a) * r, axis_pos[2] + z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segs):
            bm.faces.new((rings[k][i], rings[k][(i + 1) % segs], rings[k + 1][(i + 1) % segs], rings[k + 1][i]))
    for ring, z in ((rings[0], profile[0][1]), (rings[-1], profile[-1][1])):
        if (ring[0].co - V((axis_pos[0], axis_pos[1], axis_pos[2] + z))).length > 1e-4:
            c = bm.verts.new((axis_pos[0], axis_pos[1], axis_pos[2] + z))
            for i in range(segs):
                bm.faces.new((ring[i], ring[(i + 1) % segs], c))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name, material)
    return ob


def turned_leg(name, x, y, h, r, material, top=0.0, style="baluster"):
    """A turned leg from z=top down to the floor: a square block at the top (where the apron joins), a vase, rings, a
    tapered foot."""
    if style == "bun":
        prof = [(0.0, 0.0), (r * 0.9, 0.0), (r * 1.25, h * 0.35), (r * 1.1, h * 0.75), (r * 0.7, h * 0.95), (r * 0.7, h)]
        return lathe(name, prof, material, 20, (x, y, top - h))
    prof = [(0.0, 0.0), (r * 0.62, 0.0), (r * 0.7, 0.02), (r * 0.55, 0.05), (r * 0.62, h * 0.18), (r * 0.95, h * 0.36),
            (r * 1.05, h * 0.44), (r * 0.8, h * 0.52), (r * 0.62, h * 0.58), (r * 0.9, h * 0.63), (r * 0.62, h * 0.67),
            (r * 0.72, h * 0.74), (r * 0.72, h * 0.76)]
    ob = lathe(name, prof, material, 20, (x, y, top - h))
    blk = block(name + "Blk", (x, y, top - h * 0.12), (r * 1.6, r * 1.6, h * 0.24), material, bevel=r * 0.15, segs=2)
    return join_objs([ob, blk], name)


def join_objs(objs, name):
    objs = [o for o in objs if o is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(objs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    return ob


def cushion(name, centre, size, material, puff=0.25, segs=(12, 10, 6), tufts=None, tuft_depth=0.025, bulge_top=True):
    """An upholstered block: rounded and puffed (a subdivided box pushed outward), optionally tufted: buttons pulled in
    at the points of a diamond grid on its +Y face (or the face given), the cloth gathered toward them."""
    nx, ny, nz = segs
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=1)
    bm.free()
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.scale = V(size) * 0.5 * 2
    bpy.ops.object.transform_apply(scale=True)
    add_mod(ob, "SUBSURF", levels=3, render_levels=3, subdivision_type="SIMPLE")
    apply_mods(ob)
    me = ob.data
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    for v in me.vertices:
        p = v.co
        # round the corners: blend each vertex toward a superellipsoid
        u = V((p.x / hx, p.y / hy, p.z / hz))
        m = max(abs(u.x), abs(u.y), abs(u.z), 1e-6)
        n = u / m
        q = V((math.copysign(abs(n.x) ** 1.0, n.x), math.copysign(abs(n.y) ** 1.0, n.y), math.copysign(abs(n.z) ** 1.0, n.z)))
        r = q.normalized() * min(1.0, q.length)
        bulge = 1.0 + puff * (1 - abs(u.x) ** 4) * (1 - abs(u.y) ** 4) * (1 - abs(u.z) ** 4)
        p.x = hx * (u.x * 0.75 + r.x * 0.25) * (1.0 if abs(u.x) > 0.98 else bulge ** 0.3)
        p.y = hy * (u.y * 0.75 + r.y * 0.25) * (bulge if abs(u.y) > 0.6 else 1.0)
        p.z = hz * (u.z * 0.75 + r.z * 0.25) * (bulge if (bulge_top and u.z > 0.6) else 1.0)
    if tufts:
        face_axis, pts = tufts
        for v in me.vertices:
            p = v.co
            if face_axis in ("y", "-y"):
                sg = 1.0 if face_axis == "y" else -1.0
                if p.y * sg > hy * 0.5:
                    d = min(((p.x - a) ** 2 + (p.z - b) ** 2) for a, b in pts) ** 0.5
                    p.y -= sg * (tuft_depth * math.exp(-(d / 0.035) ** 2) + tuft_depth * 0.35 * math.exp(-(d / 0.09) ** 2))
            if face_axis == "z" and p.z > hz * 0.5:
                d = min(((p.x - a) ** 2 + (p.y - b) ** 2) for a, b in pts) ** 0.5
                p.z -= tuft_depth * math.exp(-(d / 0.035) ** 2) + tuft_depth * 0.35 * math.exp(-(d / 0.09) ** 2)
    ob.location = V(centre)
    bpy.ops.object.transform_apply(location=True)
    ob.data.materials.clear()
    ob.data.materials.append(material)
    for pg in ob.data.polygons:
        pg.use_smooth = True
    return ob


def diamond_grid(x0, x1, z0, z1, nx, nz):
    pts = []
    for j in range(nz):
        z = z0 + (z1 - z0) * (j + 0.5) / nz
        off = 0.5 if j % 2 else 0.0
        for i in range(nx):
            x = x0 + (x1 - x0) * (i + 0.5 + off) / (nx + (0.5 if j % 2 else 0))
            pts.append((x, z))
    return pts


def buttons(name, pts, plane_y, material, r=0.012, axis="y"):
    objs = []
    for k, (a, b) in enumerate(pts):
        c = (a, plane_y, b) if axis == "y" else (a, b, plane_y)
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=5, radius=r)
        bmesh.ops.scale(bm, vec=V((1, 0.5, 1)) if axis == "y" else V((1, 1, 0.5)), verts=bm.verts)
        bmesh.ops.translate(bm, vec=V(c), verts=bm.verts)
        objs.append(obj_from_bmesh(bm, f"{name}{k}", material))
    return join_objs(objs, name) if objs else None


def piping(name, pts, r, material, closed=False):
    """A cord along a path (the piping on a seam, a moulding)."""
    from kit import skin_tree
    edges = [(i, i + 1) for i in range(len(pts) - 1)]
    if closed:
        edges.append((len(pts) - 1, 0))
    return skin_tree(name, [V(p) for p in pts], edges, [r] * len(pts), material, subdiv=1)


def moulding(name, pts, profile, material):
    """A moulded edge along an open path: the profile [(out, up), ...] swept along it (out to the path's left)."""
    bm = bmesh.new()
    rows = []
    n = len(pts)
    for i, p in enumerate(pts):
        p = V(p)
        t = (V(pts[min(i + 1, n - 1)]) - V(pts[max(i - 1, 0)])).normalized()
        left = V((0, 0, 1)).cross(t).normalized()
        rows.append([bm.verts.new(p + left * o + V((0, 0, u))) for (o, u) in profile])
    for i in range(n - 1):
        for j in range(len(profile) - 1):
            bm.faces.new((rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name, material)


# ------------------------------------------------------------------ baking and export

KEEP_UV = set()


def uv_layers(ob):
    """UV 1: box projection at a metre a repeat (the game's own materials); UV 2: a unique layout for the cavity map."""
    me = ob.data
    while len(me.uv_layers) < 2:
        me.uv_layers.new()
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    me.uv_layers.active_index = 0
    # (faces of a KEEP_UV role keep the UVs they were built with: a card of needles maps its spray photo whole)
    keep = {i for i, m in enumerate(me.materials) if m is not None and m.name.split(".")[0] in KEEP_UV}
    for p in me.polygons:
        p.select = p.material_index not in keep
    bpy.ops.object.mode_set(mode="EDIT")
    if not keep:
        bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.0, scale_to_bounds=False, correct_aspect=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    me.uv_layers.active_index = 1
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.004)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    me.uv_layers.active_index = 0
    me.uv_layers[0].active_render = True


def bake_cavity(ob, name, size=CAVITY):
    """Ambient occlusion on the second UV set, its creases darkened, saved as <name>_cavity.png (grey: 1 is untouched)."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 48
    rb = sc.render.bake
    rb.use_selected_to_active = False
    rb.margin = 8
    img = bpy.data.images.new(name + "_ao", size, size, alpha=False)
    img.colorspace_settings.name = "Non-Color"
    me = ob.data
    me.uv_layers.active_index = 1
    for slot in ob.material_slots:
        nt = slot.material.node_tree
        node = nt.nodes.get("BakeTarget") or nt.nodes.new("ShaderNodeTexImage")
        node.name = "BakeTarget"
        node.image = img
        uvn = nt.nodes.get("BakeUV") or nt.nodes.new("ShaderNodeUVMap")
        uvn.name = "BakeUV"
        uvn.uv_map = me.uv_layers[1].name
        nt.links.new(uvn.outputs["UV"], node.inputs["Vector"])
        nt.nodes.active = node
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.bake(type="AO")
    a = np.array(img.pixels[:], np.float32).reshape(size, size, 4)[:, :, 0]
    for _ in range(2):
        a = (a + np.roll(a, 1, 0) + np.roll(a, -1, 0) + np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 5.0
    # the grey that multiplies in: deep creases near black, open surfaces untouched; a faint grime mottle over all
    g = np.clip(0.18 + 0.82 * a ** 0.8, 0, 1)
    yy, xx = np.mgrid[0:size, 0:size] / size
    mott = 0.96 + 0.04 * np.sin(xx * 61.0 + np.sin(yy * 37.0) * 2.0) * np.sin(yy * 53.0 + 1.3)
    g = np.clip(g * mott, 0, 1)
    out = np.dstack([g, g, g, np.ones_like(g)]).astype(np.float32)
    cav = bpy.data.images.new(name + "_cavity", size, size, alpha=False)
    cav.pixels = out.ravel()
    cav.filepath_raw = os.path.join(OUT, name + "_cavity.png")
    cav.file_format = "PNG"
    cav.save()
    me.uv_layers.active_index = 0
    for slot in ob.material_slots:
        nt = slot.material.node_tree
        for nn in ("BakeTarget", "BakeUV"):
            if nt.nodes.get(nn):
                nt.nodes.remove(nt.nodes[nn])


# the most triangles a piece may have in the game (they're used by the dozen: 72 dining chairs in the lodge's hall)
BUDGET = {"dining_chair": 2600, "bed_17": 16000, "lamp_floor": 5000, "lamp_table": 2500, "chesterfield_21": 9000, "chesterfield_24": 9500,
          "wing_chair": 7000, "coffee_table": 4000, "bar_stool": 1600, "boots": 3000, "log_pile": 4000, "coat_on_hook": 3000,
          "books_row_a": 2000, "books_row_b": 1400, "books_stack": 1000, "bottle_wine": 300, "bottle_whiskey": 260, "bottle_liqueur": 260}


def export(objs, name, preview_target=None, dist=3.0, height=0.6, yaw=30):
    ob = join_objs(objs, name)
    tris0 = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if name in BUDGET and tris0 > BUDGET[name]:
        add_mod(ob, "DECIMATE", ratio=BUDGET[name] / tris0, use_collapse_triangulate=True)
        apply_mods(ob)
    # (the pieces are built with their fronts toward -Y; the game's front is Blender's +Y: mirror them round)
    ob.scale = (1, -1, 1)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")
    add_mod(ob, "TRIANGULATE")
    apply_mods(ob)
    bpy.ops.object.shade_smooth()
    uv_layers(ob)
    bake_cavity(ob, name)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), export_format="GLB", use_selection=True, export_yup=True,
                              export_apply=True, export_texcoords=True, export_normals=True, export_tangents=True,
                              export_materials="EXPORT", export_image_format="NONE")
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[furniture] {name}: {tris} triangles")
    if preview_target is not None:
        preview(name, preview_target, dist, height, yaw)
    return ob


# ------------------------------------------------------------------ the pieces

def chesterfield(w):
    """A chesterfield, deep-buttoned: the back and the rolled arms one height, tufted in diamonds on their inside, the
    arms rolled over and pleated at their fronts; two seat cushions with piping; a plinth; turned bun feet."""
    clear()
    up, wood = role("upholstery"), role("wood")
    P = []
    d, seat_h, back_h = 0.9, 0.44, 0.78
    # the plinth (the base under the seat)
    P.append(cushion("Base", (0, 0, 0.24), (w, d, 0.24), up, puff=0.04))
    # the back: tufted on its front face
    bt = diamond_grid(-w / 2 + 0.22, w / 2 - 0.22, 0.5, back_h - 0.05, max(4, int(w / 0.28)), 3)
    P.append(cushion("Back", (0, d / 2 - 0.11, (back_h + 0.36) / 2), (w, 0.22, back_h - 0.36), up, puff=0.08, tufts=("-y", [(a, b - (back_h + 0.36) / 2) for a, b in bt]), tuft_depth=0.03))
    P.append(buttons("BackButtons", [(a, b) for a, b in bt], d / 2 - 0.11 - 0.11 + 0.028, up, 0.013))
    # the arms: rolled scrolls, as high as the back, tufted inside
    for s in (-1, 1):
        x = s * (w / 2 - 0.11)
        P.append(cushion(f"Arm{s}", (x, 0, (back_h + 0.36) / 2), (0.22, d, back_h - 0.36), up, puff=0.08))
        # the scroll rolled over its top
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=0.135, radius2=0.135, depth=d)
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
        bmesh.ops.translate(bm, vec=V((x + s * 0.02, 0, back_h - 0.02)), verts=bm.verts)
        roll = obj_from_bmesh(bm, f"Roll{s}", up)
        finish(roll, True, 0.03, 3)
        P.append(roll)
        # the pleated front of the scroll: a fan of folds round a button
        fc = V((x + s * 0.02, -d / 2 - 0.005, back_h - 0.02))
        folds = []
        for k in range(10):
            a = k / 10 * math.tau
            folds.append(piping(f"Pleat{s}{k}", [fc, fc + V((math.cos(a) * 0.12, -0.004, math.sin(a) * 0.12))], 0.008, up))
        P.append(join_objs(folds, f"Pleats{s}"))
        P.append(buttons(f"RollButton{s}", [(fc.x, fc.z)], fc.y - 0.004, up, 0.016))
    # the seat cushions, piped round their tops
    n = 2 if w < 2.3 else 3
    cw = (w - 0.44) / n
    for i in range(n):
        cx = -w / 2 + 0.22 + cw * (i + 0.5)
        P.append(cushion(f"Seat{i}", (cx, -0.06, seat_h), (cw - 0.01, d - 0.24, 0.13), up, puff=0.18))
        y0, y1 = -0.06 - (d - 0.24) / 2, -0.06 + (d - 0.24) / 2
        P.append(piping(f"Pipe{i}", [(cx - cw / 2 + 0.03, y0 + 0.01, seat_h + 0.06), (cx + cw / 2 - 0.03, y0 + 0.01, seat_h + 0.06)], 0.008, up))
    # bun feet
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(turned_leg(f"Foot{sx}{sy}", sx * (w / 2 - 0.1), sy * (d / 2 - 0.1), 0.12, 0.045, wood, top=0.12, style="bun"))
    return P


def wing_chair():
    """A wing armchair: a tall back tufted in diamonds, the wings swept forward, rolled arms, a loose seat cushion
    piped round, a seat rail; cabriole front legs and splayed back legs."""
    clear()
    up, wood = role("upholstery"), role("wood")
    P = []
    P.append(cushion("Base", (0, 0, 0.3), (0.82, 0.78, 0.22), up, puff=0.05))
    bt = diamond_grid(-0.28, 0.28, 0.55, 1.08, 3, 3)
    P.append(cushion("Back", (0, 0.31, 0.78), (0.74, 0.16, 0.86), up, puff=0.1, tufts=("-y", [(a, b - 0.78) for a, b in bt]), tuft_depth=0.026))
    P.append(buttons("BackButtons", bt, 0.31 - 0.08 + 0.024, up, 0.012))
    for s in (-1, 1):
        # the wing: a curved panel standing out from the back, swept forward
        bm = bmesh.new()
        grid = []
        for i in range(9):
            row = []
            t = i / 8
            for j in range(7):
                u = j / 6
                y = 0.32 - u * 0.36 * (0.4 + 0.6 * t)
                z = 0.62 + t * 0.5 - 0.06 * (u ** 2)
                x = s * (0.37 + 0.03 * math.sin(u * math.pi))
                row.append(bm.verts.new((x, y, z)))
            grid.append(row)
        for i in range(8):
            for j in range(6):
                bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
        wing = obj_from_bmesh(bm, f"Wing{s}", up)
        add_mod(wing, "SOLIDIFY", thickness=0.07, offset=0)
        finish(wing, True, 0.02, 2, subsurf=1)
        P.append(wing)
        # the arm and its roll
        P.append(cushion(f"Arm{s}", (s * 0.35, -0.04, 0.52), (0.14, 0.7, 0.26), up, puff=0.1))
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=18, radius1=0.075, radius2=0.075, depth=0.7)
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
        bmesh.ops.translate(bm, vec=V((s * 0.37, -0.04, 0.66)), verts=bm.verts)
        r = obj_from_bmesh(bm, f"Roll{s}", up)
        P.append(finish(r, True, 0.025, 3))
    P.append(cushion("Seat", (0, -0.05, 0.46), (0.58, 0.62, 0.12), up, puff=0.2))
    P.append(piping("SeatPipe", [(-0.27, -0.36, 0.5), (0.27, -0.36, 0.5)], 0.007, up))
    # the legs: front cabriole (a curved, tapering leg with a knee and a pad foot), back legs splayed
    for s in (-1, 1):
        pts, rs = [], []
        for k in range(9):
            t = k / 8
            pts.append(V((s * (0.33 + 0.02 * math.sin(t * math.pi)), -0.31 - 0.05 * math.sin(t * math.pi * 1.2), 0.2 - t * 0.2)))
            rs.append(0.03 - 0.012 * math.sin(t * math.pi * 0.9))
        from kit import skin_tree
        P.append(skin_tree(f"LegF{s}", pts, [(i, i + 1) for i in range(8)], rs, wood, subdiv=1))
        P.append(lathe(f"Pad{s}", [(0, 0), (0.035, 0), (0.04, 0.012), (0.028, 0.025)], wood, 16, (s * 0.33, -0.31, 0)))
        P.append(skin_tree(f"LegB{s}", [V((s * 0.33, 0.31, 0.2)), V((s * 0.35, 0.36, 0.0))], [(0, 1)], [0.03, 0.022], wood, subdiv=1))
    return P


def coffee_table(L, W, H):
    """A low table: a thick top with a moulded edge, a deep apron with a carved bead, four turned legs, a lower shelf."""
    clear()
    wood = role("wood")
    P = []
    P.append(block("Top", (0, 0, H - 0.025), (L, W, 0.05), wood, bevel=0.012, segs=3))
    # the moulded edge under the top
    edge = [(-L / 2 + 0.01, -W / 2 + 0.01), (L / 2 - 0.01, -W / 2 + 0.01), (L / 2 - 0.01, W / 2 - 0.01), (-L / 2 + 0.01, W / 2 - 0.01), (-L / 2 + 0.01, -W / 2 + 0.01)]
    P.append(moulding("Ovolo", [(x, y, H - 0.05) for x, y in edge], [(-0.0, 0.0), (0.008, -0.004), (0.01, -0.012), (0.004, -0.018), (-0.004, -0.02)], wood))
    ax, ay = L / 2 - 0.09, W / 2 - 0.09
    for (cx, cy, sx, sy) in ((0, ay, L - 0.2, 0.025), (0, -ay, L - 0.2, 0.025), (ax, 0, 0.025, W - 0.2), (-ax, 0, 0.025, W - 0.2)):
        P.append(block("Apron", (cx, cy, H - 0.1), (sx, sy, 0.1), wood, bevel=0.004, segs=1))
    P.append(piping("Bead", [(-L / 2 + 0.1, -ay - 0.013, H - 0.145), (L / 2 - 0.1, -ay - 0.013, H - 0.145)], 0.005, wood))
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(turned_leg(f"Leg{sx}{sy}", sx * ax, sy * ay, H - 0.05, 0.03, wood, top=H - 0.05))
    P.append(block("Shelf", (0, 0, 0.1), (L - 0.22, W - 0.22, 0.022), wood, bevel=0.005, segs=1))
    return P


def bar_table():
    """A round café table: a black top with a brass rim, a turned brass column, a weighted three-footed base."""
    clear()
    top, metal = role("top"), role("metal")
    P = []
    P.append(lathe("Top", [(0, 0.71), (0.4, 0.71), (0.402, 0.715), (0.402, 0.735), (0.398, 0.74), (0, 0.74)], top, 40))
    P.append(lathe("Rim", [(0.398, 0.712), (0.41, 0.716), (0.412, 0.738), (0.398, 0.742)], metal, 40))
    P.append(lathe("Column", [(0.0, 0.05), (0.06, 0.05), (0.035, 0.1), (0.028, 0.35), (0.045, 0.4), (0.028, 0.45), (0.024, 0.66), (0.06, 0.7), (0.0, 0.71)], metal, 18))
    from kit import skin_tree
    for k in range(3):
        a = k / 3 * math.tau
        d = V((math.cos(a), math.sin(a), 0))
        P.append(skin_tree(f"Foot{k}", [V((0, 0, 0.07)), d * 0.18 + V((0, 0, 0.05)), d * 0.3 + V((0, 0, 0.015))], [(0, 1), (1, 2)], [0.03, 0.022, 0.018], metal, subdiv=1))
    return P


def dining_chair():
    """A dining chair: turned front legs, square back legs rising into the back's stiles, a carved top rail, an
    upholstered drop-in seat with piping, an upholstered back panel with buttons, stretchers."""
    clear()
    wood, up = role("wood"), role("upholstery")
    P = []
    for s in (-1, 1):
        P.append(turned_leg(f"FrontLeg{s}", s * 0.2, -0.2, 0.44, 0.022, wood, top=0.44))
        # back leg and stile, raked back
        from kit import skin_tree
        P.append(skin_tree(f"Stile{s}", [V((s * 0.2, 0.2, 0.0)), V((s * 0.2, 0.2, 0.44)), V((s * 0.19, 0.25, 0.75)), V((s * 0.185, 0.27, 0.98))], [(0, 1), (1, 2), (2, 3)],
                           [(0.018, 0.018), (0.02, 0.02), (0.018, 0.018), (0.017, 0.017)], wood, subdiv=1))
        P.append(block(f"SideStretcher{s}", (s * 0.2, 0, 0.12), (0.02, 0.38, 0.025), wood, bevel=0.004, segs=1))
    P.append(block("SeatRail", (0, 0, 0.4), (0.44, 0.44, 0.06), wood, bevel=0.006, segs=1))
    P.append(cushion("Seat", (0, -0.01, 0.46), (0.46, 0.46, 0.07), up, puff=0.22))
    P.append(piping("SeatPipe", [(-0.22, -0.24, 0.48), (0.22, -0.24, 0.48)], 0.006, up))
    P.append(cushion("BackPanel", (0, 0.24, 0.73), (0.34, 0.04, 0.34), up, puff=0.15))
    P.append(buttons("BackButtons", [(-0.08, 0.76), (0.08, 0.76), (0.0, 0.68)], 0.24 - 0.02 + 0.004, up, 0.009))
    # the top rail: a carved, curved crest
    bm = bmesh.new()
    rows = []
    for i in range(17):
        t = i / 16
        x = -0.2 + 0.4 * t
        z = 0.93 + 0.04 * math.sin(t * math.pi) + 0.012 * math.sin(t * math.pi * 6)
        y = 0.265 + 0.03 * math.sin(t * math.pi)
        rows.append([bm.verts.new((x, y - 0.015, z - 0.07)), bm.verts.new((x, y - 0.015, z)), bm.verts.new((x, y + 0.015, z)), bm.verts.new((x, y + 0.015, z - 0.07))])
    for i in range(16):
        for j in range(4):
            bm.faces.new((rows[i][j], rows[i][(j + 1) % 4], rows[i + 1][(j + 1) % 4], rows[i + 1][j]))
    rail = obj_from_bmesh(bm, "TopRail", wood)
    P.append(finish(rail, True, 0.006, 2))
    P.append(block("FrontStretcher", (0, -0.2, 0.16), (0.38, 0.02, 0.025), wood, bevel=0.004, segs=1))
    return P


def bed(w):
    """A bed: a carved wooden frame with a footboard, a tall headboard upholstered in deep buttons inside a moulded
    wooden surround with a carved crest; a mattress with piping; a fitted sheet; two plump pillows; the counterpane
    turned down over a sheet, its edges hanging in soft folds."""
    clear()
    wood, up, linen, pillow, cover = role("wood"), role("upholstery"), role("linen"), role("pillow"), role("cover")
    P = []
    L = 2.1
    # the side rails and their mouldings
    for s in (-1, 1):
        P.append(block(f"Rail{s}", (s * (w / 2 - 0.02), 0, 0.25), (0.05, L - 0.1, 0.18), wood, bevel=0.008, segs=2))
    # the footboard: a low panelled board between turned posts
    for s in (-1, 1):
        P.append(turned_leg(f"FootPost{s}", s * (w / 2), -L / 2, 0.62, 0.04, wood, top=0.62))
        P.append(lathe(f"FootFinial{s}", [(0, 0.62), (0.045, 0.62), (0.05, 0.65), (0.03, 0.68), (0.04, 0.7), (0, 0.73)], wood, 16, (s * w / 2, -L / 2, 0)))
    P.append(block("FootBoard", (0, -L / 2, 0.42), (w - 0.06, 0.05, 0.36), wood, bevel=0.01, segs=2))
    P.append(block("FootPanel", (0, -L / 2 - 0.03, 0.42), (w - 0.3, 0.02, 0.24), wood, bevel=0.012, segs=3))
    # the headboard: posts, a surround, the tufted panel, a carved crest
    H = 1.45
    for s in (-1, 1):
        P.append(turned_leg(f"HeadPost{s}", s * (w / 2 + 0.03), L / 2, H, 0.05, wood, top=H))
        P.append(lathe(f"HeadFinial{s}", [(0, H), (0.055, H), (0.06, H + 0.04), (0.035, H + 0.08), (0.05, H + 0.11), (0, H + 0.16)], wood, 16, (s * (w / 2 + 0.03), L / 2, 0)))
    P.append(block("HeadFrame", (0, L / 2 + 0.02, 0.95), (w, 0.07, 0.95), wood, bevel=0.015, segs=3))
    ht = diamond_grid(-w / 2 + 0.2, w / 2 - 0.2, 0.65, 1.3, max(4, int(w / 0.25)), 3)
    P.append(cushion("HeadPanel", (0, L / 2 - 0.03, 0.97), (w - 0.18, 0.08, 0.8), up, puff=0.08, tufts=("-y", [(a, b - 0.97) for a, b in ht]), tuft_depth=0.026))
    P.append(buttons("HeadButtons", ht, L / 2 - 0.03 - 0.04 + 0.016, up, 0.012))
    crest = []
    for i in range(25):
        t = i / 24
        crest.append((-w / 2 + w * t, L / 2 + 0.02, H - 0.02 + 0.09 * math.sin(t * math.pi) ** 1.5))
    P.append(piping("Crest", crest, 0.03, wood))
    # the mattress, piped; the sheet over it
    P.append(cushion("Mattress", (0, -0.03, 0.43), (w - 0.08, L - 0.14, 0.22), linen, puff=0.06))
    for zz in (0.32, 0.54):
        P.append(piping(f"MPipe{zz}", [(-w / 2 + 0.05, -L / 2 + 0.05, zz), (w / 2 - 0.05, -L / 2 + 0.05, zz), (w / 2 - 0.05, L / 2 - 0.1, zz)], 0.006, linen))
    # pillows: plump, their corners pulled out
    for s in (-1, 1):
        pw = (w - 0.2) / 2
        P.append(cushion(f"Pillow{s}", (s * (pw / 2 + 0.03), L / 2 - 0.3, 0.62), (pw, 0.42, 0.14), pillow, puff=0.45))
    # the counterpane: draped over the bed's lower part, turned down at its top, hanging over the sides in folds
    bm = bmesh.new()
    nx, ny = 28, 26
    grid = []
    hang = 0.36
    for j in range(ny + 1):
        row = []
        yv = -L / 2 - 0.06 + (L * 0.72 + 0.06) * j / ny
        for i in range(nx + 1):
            u = i / nx
            # across: over the top, then down the sides (hanging)
            xw = -w / 2 - hang + (w + hang * 2) * u
            ax_ = abs(xw)
            if ax_ <= w / 2:
                z = 0.56 + 0.012 * math.sin(xw * 9 + yv * 3)
                x = xw
            else:
                over = ax_ - w / 2
                z = 0.56 - over
                x = math.copysign(w / 2 + 0.02 + 0.03 * math.sin(over * 8) * (over / hang) + over * 0.06, xw)
                # the folds as it hangs
                x += math.copysign(0.025 * math.sin(yv * 14) * (over / hang), xw)
            row.append(bm.verts.new((x, yv, z)))
        grid.append(row)
    for j in range(ny):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    cp = obj_from_bmesh(bm, "Counterpane", cover)
    add_mod(cp, "SOLIDIFY", thickness=0.025, offset=0)
    finish(cp, True, 0, 1, subsurf=1)
    P.append(cp)
    # the turned-down band at its top (the sheet showing)
    tb = L * 0.72 - L / 2 - 0.06
    P.append(cushion("Turndown", (0, tb - 0.08, 0.585), (w + 0.04, 0.2, 0.04), linen, puff=0.2))
    # the foot of the counterpane hanging over the end
    P.append(cushion("FootDrape", (0, -L / 2 - 0.07, 0.42), (w + 0.04, 0.03, 0.3), cover, puff=0.05))
    return P


def lamp(floor):
    """A lamp: a stepped, turned brass base, a reeded column with a knop, the fitting, and a pleated drum shade."""
    clear()
    metal, shade = role("metal"), role("shade")
    P = []
    H = 1.6 if floor else 0.5
    rb = 0.16 if floor else 0.09
    P.append(lathe("Base", [(0, 0), (rb, 0), (rb, 0.012), (rb * 0.85, 0.02), (rb * 0.85, 0.03), (rb * 0.5, 0.05), (rb * 0.3, 0.07), (0, 0.075)], metal, 32))
    colr = 0.018 if floor else 0.03
    P.append(lathe("Column", [(0, 0.07), (colr, 0.07), (colr, H * 0.45), (colr * 2.2, H * 0.48), (colr * 2.4, H * 0.5), (colr * 2.2, H * 0.52), (colr, H * 0.55), (colr, H - 0.2), (colr * 1.6, H - 0.18), (0, H - 0.17)], metal, 20))
    # reeds up the column
    for k in range(8):
        a = k / 8 * math.tau
        P.append(piping(f"Reed{k}", [(math.cos(a) * colr, math.sin(a) * colr, 0.1), (math.cos(a) * colr, math.sin(a) * colr, H * 0.44)], colr * 0.25, metal))
    # the shade: a pleated drum
    r0, r1 = (0.24, 0.14) if floor else (0.18, 0.1)
    bm = bmesh.new()
    seg = 64
    rings = []
    for (r, z) in ((r0, H - 0.24), (r1, H + 0.02)):
        ring = []
        for i in range(seg):
            a = i / seg * math.tau
            rr = r * (1.0 + 0.035 * (1 if i % 2 else -1))
            ring.append(bm.verts.new((math.cos(a) * rr, math.sin(a) * rr, z)))
        rings.append(ring)
    for i in range(seg):
        bm.faces.new((rings[0][i], rings[0][(i + 1) % seg], rings[1][(i + 1) % seg], rings[1][i]))
    sh = obj_from_bmesh(bm, "Shade", shade, smooth=False)
    add_mod(sh, "SOLIDIFY", thickness=0.004, offset=0)
    apply_mods(sh)
    P.append(sh)
    for (r, z) in ((r0 * 1.02, H - 0.24), (r1 * 1.02, H + 0.02)):
        P.append(lathe(f"Trim{z}", [(r - 0.004, z - 0.006), (r + 0.004, z - 0.006), (r + 0.004, z + 0.006), (r - 0.004, z + 0.006)], metal, 48))
    return P


PIECES = {
    "chesterfield_21": (lambda: chesterfield(2.1), (0, 0, 0.5), 3.4, 0.6, 30),
    "chesterfield_24": (lambda: chesterfield(2.4), (0, 0, 0.5), 3.6, 0.6, 30),
    "wing_chair": (wing_chair, (0, 0, 0.6), 2.4, 0.4, 30),
    "coffee_table": (lambda: coffee_table(1.4, 0.8, 0.42), (0, 0, 0.25), 2.4, 0.6, 30),
    "bar_table": (bar_table, (0, 0, 0.4), 2.0, 0.6, 30),
    "dining_chair": (dining_chair, (0, 0, 0.5), 1.8, 0.4, 35),
    "bed_17": (lambda: bed(1.7), (0, 0, 0.6), 4.2, 1.0, 30),
    "lamp_floor": (lambda: lamp(True), (0, 0, 0.9), 2.6, 0.3, 30),
    "lamp_table": (lambda: lamp(False), (0, 0, 0.3), 1.2, 0.2, 30),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        fn, tgt, dist, h, yaw = PIECES[name]
        parts = fn()
        export(parts, name, tgt, dist, h, yaw)
