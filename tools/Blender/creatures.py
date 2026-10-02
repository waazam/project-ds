"""The stalker's and the crawler's bodies, remodelled (the owner, 2026-10-02: "give the stalker, the crawler and the
friend the same Blender modelling and rigging pass the wendigo had"). The friend is no longer a body in the game (what
he left on his table is all there is of him), so it's the stalker and the crawler.

Each creature's parts are the game's own (exported from it with `--export-bodies` to build/bodies, one PLY per part and
material, in the part's own pivot space with its tone colours): so the proportions, the pivots its idle and its walk
turn about, and the tones its shader reads (the colour channels: paleness, brightness, wetness; alpha: the eyes) all
stay exactly as they are. Here each part is rebuilt dense and sculpted:
  - the skin (the hide) is remeshed into one continuous surface at a few millimetres, relaxed, and given its surface:
    wrinkled, stringy, the skin pulled over bone, in two layers of noise along the normal;
  - what's too thin to remesh (the stalker's tattered strips, its bark shards, fingers) is subdivided smooth instead;
  - the tones are carried over from the original by the nearest point;
  - each part is brought down to its budget.
The crawler's limbs, plain cylinders until now, are modelled: a long starved bone under thin skin, a knob of joint
at each end, cords of tendon along it (unit length along +Y, scaled to each limb's length in the game).

    blender -b --python tools/Blender/creatures.py
writes assets/models/creatures/stalker.glb and crawler.glb (each part an object named as the game's part).
"""
import bpy, bmesh, math, os, random, sys, glob
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, apply_mods, add_mod, ROOT

SRC = os.path.join(ROOT, "build", "bodies")
OUT = os.path.join(ROOT, "assets", "models", "creatures")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREV, exist_ok=True)

STALKER_BUDGET = {"Lower": 16000, "Upper": 22000, "ArmL": 11000, "ArmR": 11000, "Head": 12000}
CRAWLER_BUDGET = {"Torso": 18000, "Skull": 9000, "Hand": 2500, "Foot": 2500}


def import_ply(path, name):
    before = set(bpy.data.objects)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.wm.ply_import(filepath=path)
    ob = next(o for o in bpy.data.objects if o not in before)
    ob.name = name
    if ob.data.users > 1:
        ob.data = ob.data.copy()
    return ob


def col_attr(ob):
    for a in ob.data.color_attributes:
        return a
    return None


def tone_mask(ob, hide_fn):
    """The faces whose (average) tone says they're skin (hide_fn on its RGBA)."""
    me = ob.data
    ca = col_attr(ob)
    out = []
    for p in me.polygons:
        if ca is None:
            out.append(True)
            continue
        if ca.domain == "POINT":
            cs = [ca.data[v].color for v in p.vertices]
        else:
            cs = [ca.data[l].color for l in p.loop_indices]
        c = [sum(x[i] for x in cs) / len(cs) for i in range(4)]
        out.append(hide_fn(c))
    return out


def split(ob, mask, name_a, name_b):
    """Splits ob into two objects by the face mask (True -> a)."""
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    for p, m in zip(ob.data.polygons, mask):
        p.select = not m
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    parts = [o for o in bpy.context.selected_objects]
    a = ob
    b = next((o for o in parts if o != ob), None)
    a.name = name_a
    if b is not None:
        b.name = name_b
    if len(a.data.polygons) == 0:
        bpy.data.objects.remove(a)
        a = None
    return a, b


def noise_tex(name, scale, kind="CLOUDS", depth=2):
    t = bpy.data.textures.get(name) or bpy.data.textures.new(name, kind)
    t.noise_scale = scale
    if hasattr(t, "noise_depth"):
        t.noise_depth = depth
    return t


def sculpt_skin(ob, voxel, wrinkle, wrinkle_scale, sinew, sinew_scale):
    """Remesh into one surface, relax it, and give it a surface: fine wrinkles and broader sinew along the normal."""
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel, adaptivity=0.0, use_smooth_shade=True)
    apply_mods(ob)
    add_mod(ob, "CORRECTIVE_SMOOTH", factor=0.6, iterations=6, smooth_type="SIMPLE", use_only_smooth=True)
    apply_mods(ob)
    m = ob.modifiers.new("sinew", "DISPLACE")
    m.texture = noise_tex("sinew_tex", sinew_scale, "CLOUDS", 1)
    m.texture_coords = "OBJECT"
    m.strength = sinew
    m.mid_level = 0.5
    apply_mods(ob)
    m = ob.modifiers.new("wrinkle", "DISPLACE")
    m.texture = noise_tex("wrinkle_tex", wrinkle_scale, "CLOUDS", 3)
    m.texture_coords = "OBJECT"
    m.strength = wrinkle
    m.mid_level = 0.5
    apply_mods(ob)


def smooth_thin(ob, levels=1):
    add_mod(ob, "SUBSURF", levels=levels, render_levels=levels)
    apply_mods(ob)


def refine(ob, levels=2, grain=0.0012, grain_scale=0.01):
    """Smoothed and given a fine skin grain, its own shape and tones kept exactly (for what a remesh would lose: open
    tubes like a neck or ribs, and a face whose tones are drawn face by face)."""
    # (its faces turned outward: the game's winding is the other way round from Blender's, and kept as it came, every
    # face was culled from the front)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    # (welded first: the game's meshes give each face its own corners, and smoothed apart they each shrank to an island)
    bpy.ops.mesh.remove_doubles(threshold=0.0008)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    smooth_thin(ob, levels)
    m = ob.modifiers.new("grain", "DISPLACE")
    m.texture = noise_tex("grain_tex", grain_scale, "CLOUDS", 2)
    m.texture_coords = "OBJECT"
    m.strength = grain
    m.mid_level = 0.5
    apply_mods(ob)


def transfer_tones(dst, src):
    """The original's tones onto the rebuilt surface, nearest point."""
    if col_attr(src) is None:
        return
    sa = col_attr(src)
    if col_attr(dst) is None:
        dst.data.color_attributes.new(name=sa.name, type="FLOAT_COLOR", domain="POINT")
    m = dst.modifiers.new("tones", "DATA_TRANSFER")
    m.object = src
    m.use_vert_data = True
    m.data_types_verts = {"COLOR_VERTEX"}
    m.vert_mapping = "POLYINTERP_NEAREST"
    m.layers_vcol_vert_select_src = "ALL"
    m.layers_vcol_vert_select_dst = "NAME"
    bpy.context.view_layer.objects.active = dst
    bpy.ops.object.modifier_apply(modifier=m.name)


def join(objs, name):
    objs = [o for o in objs if o is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    return ob


def budget(ob, tris):
    n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if n > tris:
        add_mod(ob, "DECIMATE", ratio=tris / n, use_collapse_triangulate=True)
        apply_mods(ob)
    add_mod(ob, "TRIANGULATE")
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def material(ob, name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    ob.data.materials.clear()
    ob.data.materials.append(m)


def export(objs, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    kw = dict(filepath=path, export_format="GLB", use_selection=True, export_yup=False, export_apply=True,
              export_normals=True, export_materials="PLACEHOLDER", export_texcoords=False)
    try:
        bpy.ops.export_scene.gltf(export_colors=True, **kw)
    except TypeError:
        bpy.ops.export_scene.gltf(**kw)


# ------------------------------------------------------------------ the stalker

def is_hide(c):
    # HideTone (r 0, g 1, b 1) and its shades; not the rags (g .85, b .25), not the eyes (alpha < 1)
    return c[2] > 0.6 and c[3] > 0.9


def stalker():
    clear()
    parts = []
    for name, tris in STALKER_BUDGET.items():
        files = sorted(glob.glob(os.path.join(SRC, f"stalker_{name}_s*.ply")))
        if not files:
            print(f"[creatures] no export for stalker {name}")
            continue
        src = import_ply(files[0], name + "_src")
        if name == "Head":
            refine(src, 2, 0.0015, 0.012)
            src.name = name
            n = budget(src, tris)
            material(src, "skin")
            parts.append(src)
            print(f"[creatures] stalker {name}: {n} triangles (refined)")
            continue
        work = src.copy()
        work.data = src.data.copy()
        bpy.context.scene.collection.objects.link(work)
        hide, thin = split(work, tone_mask(work, is_hide), name + "_hide", name + "_thin")
        if hide is not None:
            sculpt_skin(hide, voxel=0.007, wrinkle=0.002, wrinkle_scale=0.012, sinew=0.003, sinew_scale=0.06)
            transfer_tones(hide, src)
        if thin is not None:
            smooth_thin(thin, 1)
        ob = join([hide, thin], name)
        n = budget(ob, tris)
        material(ob, "skin")
        bpy.data.objects.remove(src)
        parts.append(ob)
        print(f"[creatures] stalker {name}: {n} triangles")
    if parts:
        export(parts, os.path.join(OUT, "stalker.glb"))


# ------------------------------------------------------------------ the crawler

def limb(name, r0, r1):
    """A starved limb's segment, unit length along +Y (y -0.5 at the root, +0.5 at the far joint): a thin bone under
    skin, a knob of joint at each end (the far one bigger: the elbow, the knee), cords of tendon along it."""
    bm = bmesh.new()
    rings = []
    n, sides = 28, 14
    for i in range(n + 1):
        u = i / n
        y = -0.5 + u
        r = r0 + (r1 - r0) * u
        r *= 0.78 + 0.35 * math.exp(-((u - 0.0) / 0.08) ** 2) + 0.5 * math.exp(-((u - 1.0) / 0.07) ** 2)   # the joints
        r *= 1.0 - 0.12 * math.sin(u * math.pi)                                                          # the wasted middle
        ring = []
        for k in range(sides):
            a = math.tau * k / sides
            # the bone's ridge down one side, tendons in two shallow cords
            rr = r * (1 + 0.12 * max(0, math.cos(a)) ** 6 + 0.08 * max(0, math.cos(a - 2.2)) ** 10 + 0.08 * max(0, math.cos(a + 2.2)) ** 10)
            ring.append(bm.verts.new((math.cos(a) * rr, y, math.sin(a) * rr)))
        rings.append(ring)
    for i in range(n):
        for k in range(sides):
            j = (k + 1) % sides
            bm.faces.new((rings[i][k], rings[i][j], rings[i + 1][j], rings[i + 1][k]))
    bmesh.ops.contextual_create(bm, geom=rings[0])
    bmesh.ops.contextual_create(bm, geom=rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    smooth_thin(ob, 1)
    m = ob.modifiers.new("skin_d", "DISPLACE")
    m.texture = noise_tex("limb_tex", 0.02, "CLOUDS", 3)
    m.texture_coords = "OBJECT"
    m.strength = 0.0025
    apply_mods(ob)
    budget(ob, 3000)
    material(ob, "limb")
    return ob


def crawler():
    clear()
    parts = []
    for name, tris in CRAWLER_BUDGET.items():
        files = sorted(glob.glob(os.path.join(SRC, f"crawler_{name}_s*.ply")))
        if not files:
            print(f"[creatures] no export for crawler {name}")
            continue
        pieces = []
        for f in files:
            dark = f.endswith("_s1.ply") and name == "Skull"
            src = import_ply(f, name + ("_dark" if dark else "_src"))
            if dark:
                smooth_thin(src, 1)
                material(src, "dark")
                pieces.append(src)
                continue
            refine(src, 2, 0.0012, 0.008)
            material(src, "skin")
            pieces.append(src)
        # (the skull's two materials kept as two surfaces of one object)
        ob = join(pieces, name)
        n = budget(ob, tris)
        parts.append(ob)
        print(f"[creatures] crawler {name}: {n} triangles")
    parts.append(limb("Upper", 0.045, 0.03))
    parts.append(limb("Lower", 0.03, 0.018))
    export(parts, os.path.join(OUT, "crawler.glb"))


if __name__ == "__main__":
    stalker()
    crawler()
    print("done")
