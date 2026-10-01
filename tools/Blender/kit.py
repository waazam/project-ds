"""The helpers for Act 22's photo subjects, modelled in Blender (the owner, 2026-09-30: the things to photograph on the road were far too
low in detail). Run headless; each is exported to assets/models/winter/<name>.glb, and a preview rendered to
build/blender/<name>.png:

    blender -b --python tools/Blender/winter_props.py [-- name ...]

Blender is Z-up; the glTF export turns it Y-up with Blender's +Y becoming the game's -Z. So each model's front (what
faces the road: the game's -Z) is modelled toward Blender's +Y, and its length along the road on X, its base at z = 0.

The look is the game's (PS2-era): solid modelled shapes, a few hundred to a few thousand faces each, small soft
textures (the game's own snow and bark, and small ones drawn here), no photographic detail.
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix, Euler, noise

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "assets", "models", "winter")
PREVIEW = os.path.join(ROOT, "build", "blender")
TEX = os.path.join(ROOT, "assets", "textures")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREVIEW, exist_ok=True)
os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)

# ------------------------------------------------------------------ the scene, materials, textures


_mats = {}


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()   # (the materials went with the scene)


def image(path):
    name = os.path.basename(path)
    img = bpy.data.images.get(name)
    if img is None:
        img = bpy.data.images.load(path)
    return img


def mat(name, color, rough=0.8, metal=0.0, tex=None, tex_scale=1.0, emit=None):
    """A Principled material: a base colour, or a texture (tinted by the colour)."""
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)   # (the preview's colour for an untextured material)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if tex:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = image(tex)
        if tex_scale != 1.0:
            mp = nt.nodes.new("ShaderNodeMapping")
            mp.inputs["Scale"].default_value = (tex_scale, tex_scale, tex_scale)
            tc = nt.nodes.new("ShaderNodeTexCoord")
            nt.links.new(tc.outputs["UV"], mp.inputs["Vector"])
            nt.links.new(mp.outputs["Vector"], t.inputs["Vector"])
        if color != (1, 1, 1):
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.inputs["Factor"].default_value = 1.0
            nt.links.new(t.outputs["Color"], mix.inputs[6])
            mix.inputs[7].default_value = (*color, 1.0)
            nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
        else:
            nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 1.0
    _mats[name] = m
    return m


def make_texture(name, size, fn):
    """A small texture drawn here: fn(x, y) -> (r, g, b) in 0..1 (x, y in 0..1). Saved beside the models."""
    import numpy as np
    path = os.path.join(OUT, "textures", name + ".png")
    img = bpy.data.images.new(name, size, size)
    px = np.zeros((size, size, 4), np.float32)
    for j in range(size):
        for i in range(size):
            r, g, b = fn(i / size, j / size)
            px[j, i] = (r, g, b, 1.0)
    img.pixels = px.ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return path


def fbm(x, y, z=0.0, oct=4):
    v, a, f = 0.0, 0.5, 1.0
    for _ in range(oct):
        v += a * noise.noise(Vector((x * f, y * f, z * f)))
        a *= 0.5
        f *= 2.0
    return v


# ------------------------------------------------------------------ mesh helpers


def obj_from_bmesh(bm, name, material=None, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if material:
        ob.data.materials.append(material)
    for p in ob.data.polygons:
        p.use_smooth = smooth
    return ob


def apply_mods(ob):
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def add_mod(ob, kind, **kw):
    m = ob.modifiers.new(kind.lower(), kind)
    for k, v in kw.items():
        setattr(m, k, v)
    return m


def uv_box(ob, scale=1.0):
    """Box-projected UVs (world-scaled): textures sit evenly on any shape."""
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.0 / max(scale, 1e-4), scale_to_bounds=False, correct_aspect=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def uv_smart(ob):
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


def lumpy_sphere(name, centre, radii, material, seed, amp=0.06, freq=2.2, subdiv=3, flat_bottom=None):
    """A sphere roughened with noise (snowballs, clumps), optionally flattened below a height."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    off = Vector((seed * 1.37, seed * 2.11, seed * 0.73))
    for v in bm.verts:
        d = v.co.normalized()
        n = fbm(d.x * freq + off.x, d.y * freq + off.y, d.z * freq + off.z, 4)
        r = 1.0 + n * amp * 2.0
        v.co = Vector((d.x * radii[0] * r, d.y * radii[1] * r, d.z * radii[2] * r)) + Vector(centre)
        if flat_bottom is not None and v.co.z < flat_bottom:
            v.co.z = flat_bottom + (v.co.z - flat_bottom) * 0.1
    return obj_from_bmesh(bm, name, material)


def skin_tree(name, verts, edges, radii, material, subdiv=1):
    """A branching shape (a tree, an antler, a deer's skeleton) skinned into one continuous mesh."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], edges, [])
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    sk = ob.modifiers.new("skin", "SKIN")
    sk.use_smooth_shade = True
    for i, r in enumerate(radii):
        rr = r if isinstance(r, (tuple, list)) else (r, r)
        me.skin_vertices[0].data[i].radius = rr
    me.skin_vertices[0].data[0].use_root = True
    if subdiv:
        add_mod(ob, "SUBSURF", levels=subdiv, render_levels=subdiv)
    apply_mods(ob)
    if material:
        ob.data.materials.append(material)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def cylinder(name, a, b, r0, r1=None, sides=12, material=None, caps=True):
    a, b = Vector(a), Vector(b)
    r1 = r0 if r1 is None else r1
    bm = bmesh.new()
    axis = b - a
    length = axis.length
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=sides, radius1=r0, radius2=r1, depth=length)
    rot = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) * 0.5) @ rot, verts=bm.verts)
    return obj_from_bmesh(bm, name, material)


def box(name, centre, size, material=None, bevel=0.0, segs=2, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    ob = obj_from_bmesh(bm, name, material, smooth=False)
    ob.location = Vector(centre)
    ob.rotation_euler = Euler(rot)
    if bevel > 0:
        add_mod(ob, "BEVEL", width=bevel, segments=segs, limit_method="ANGLE")
        apply_mods(ob)
        for p in ob.data.polygons:
            p.use_smooth = True
        ob.data.use_auto_smooth = True if hasattr(ob.data, "use_auto_smooth") else None
    return ob


def join(objs, name):
    objs = [o for o in objs if o is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    return ob


def snow_on_top(src, name, material, thick=0.05, min_up=0.55, seed=1):
    """Snow lying on whatever faces up on an object: its upward faces, copied, lifted and thickened."""
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.transform(src.matrix_world)
    bm.normal_update()
    kill = [f for f in bm.faces if f.normal.z < min_up]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    for f in bm.faces:
        f.material_index = 0   # (the copies kept the source's material slots: the plow's snow came out painted orange)
    for v in bm.verts:
        n = fbm(v.co.x * 3 + seed, v.co.y * 3, v.co.z * 3, 3)
        v.co.z += thick * (0.6 + 0.8 * (n + 0.5))
    ob = obj_from_bmesh(bm, name, material)
    add_mod(ob, "SOLIDIFY", thickness=thick * 0.8, offset=-1.0)
    apply_mods(ob)
    return ob


def export(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_image_format="AUTO", export_materials="EXPORT")
    tris = sum(len(p.vertices) - 2 for o in objs if o.type == "MESH" for p in o.data.polygons)
    print(f"[props] {name}: {tris} triangles -> {path}")


def preview(name, target, dist, height=1.0, yaw=35.0, size=(900, 700)):
    """A quick look (Workbench, the materials' colours and textures, a matcap-free studio light)."""
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.world = bpy.data.worlds.new("w") if sc.world is None else sc.world
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
    t = Vector(target)
    a = math.radians(yaw)
    cam.location = t + Vector((math.sin(a) * dist, math.cos(a) * dist, height))
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 40
    sc.camera = cam
    sc.render.filepath = os.path.join(PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)


# ------------------------------------------------------------------ the shared materials

SNOW = os.path.join(TEX, "snow", "snow_albedo.png")
BARK = os.path.join(TEX, "winter", "bark_albedo.png")
BARK_GREY = os.path.join(TEX, "winter", "bark_grey_albedo.png")


def snow_mat():
    return mat("snow", (0.9, 0.93, 1.0), 0.9, tex=SNOW)


