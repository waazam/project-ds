"""Act 1's photo birds and the creek frog, modelled, baked and rigged in Blender (2026-10-15, the owner: "from now on everything
should be made in blender" — they were MeshKit lofts, tubes and blobs).

    blender -b --python tools/Blender/birds.py [-- bird|frog]

Writes:
  assets/models/bird/bird.glb                  one songbird (a cardinal's build) on its skeleton, with clips: idle, look,
                                               peck, hop, takeoff, flap, glide, lunge. Its feathers wear the red albedo;
  assets/models/bird/bird_albedo_<c>.png       the other colours' albedo (blue, purple, black), swapped in by Bird.cs
  assets/models/frog/frog.glb                  the creek frog on its skeleton, with clips: idle, throat, hop
  build/blender/bird_*.png, frog_*.png         previews

Both are built in the game's own model space (x across, y up, z along; the bird's beak toward +z, the frog's snout toward
-z, as Bird.cs and Frog.cs had them; the origin at the feet) and turned into Blender's (x, -z, y) as they go, so the
export lands back in the game's space. Life size: the game scales them (the bird 2.1x, the frog 1.15x).

How each is made (the stalker's and the crawler's way, small): the soft parts lofted and joined, voxel-remeshed into one
skin and smoothed (no tube-and-ball joins); the hard parts (beak, eyes, feathers, toes) their own solids; colours painted
per vertex and baked with an occlusion pass into an albedo, fine detail (feather barbs, wet skin) baked into a normal map;
every rigid part tagged to its bone, the skin weighted to the nearest of its own bones.

The bird's wings come twice: folded along its flanks (bones wingfold_L/R) and spread for flight (wing_L/R). The rest pose
has both; Bird.cs (and the clips) scale whichever isn't in use down to nothing at its shoulder.
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, skin_tree, join, apply_mods, add_mod, obj_from_bmesh, fbm, ROOT
import wendigo as W

PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(PREV, exist_ok=True)
V = Vector


def P(x, y, z):
    """A point from the game's model space to Blender's."""
    return V((x, -z, y))


def G(v):
    """Back from Blender's to the game's."""
    return V((v.x, v.z, -v.y))


def srgb(c):
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


# ------------------------------------------------------------------ shared building

def smooth_rings(rings, k=5):
    """The rings resampled k times as densely along a Catmull-Rom curve (each of z, y, w, h), so the loft has no flat bands."""
    out = []
    n = len(rings)
    for i in range(n - 1):
        p0, p1, p2, p3 = rings[max(i - 1, 0)], rings[i], rings[i + 1], rings[min(i + 2, n - 1)]
        for s in range(k):
            t = s / k
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3)
                             for a, b, c, d in zip(p0, p1, p2, p3)))
    out.append(rings[-1])
    return out


def loft(name, rings, sides=40, squash_below=1.0):
    """A closed skin through rings (game space): (z, y, half-width, half-height); the lower half scaled by squash_below."""
    rings = smooth_rings(rings)
    bm = bmesh.new()
    rr = []
    for (z, y, w, h) in rings:
        ring = []
        for j in range(sides):
            a = j / sides * math.tau
            c, s = math.cos(a), math.sin(a)
            hh = h * (squash_below if s < 0 else 1.0)
            ring.append(bm.verts.new(P(c * w, y + s * hh, z)))
        rr.append(ring)
    for i in range(len(rr) - 1):
        for j in range(sides):
            bm.faces.new((rr[i][j], rr[i][(j + 1) % sides], rr[i + 1][(j + 1) % sides], rr[i + 1][j]))
    for ring, (z, y, _, _), step in ((rr[0], rings[0], -1), (rr[-1], rings[-1], 1)):
        c = bm.verts.new(P(0, y, z + step * 0.0005 * (1 if rings[-1][0] > rings[0][0] else -1)))
        for j in range(sides):
            bm.faces.new((ring[j], ring[(j + 1) % sides], c))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name)


def ellipsoid(name, centre, radii, seed=0, amp=0.0, subdiv=4):
    """An ellipsoid in game space (centre, radii along the game's x, y, z), roughened a little if amp."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    off = V((seed * 1.37, seed * 2.11, seed * 0.73))
    for v in bm.verts:
        d = v.co.normalized()
        r = 1.0 + (fbm(d.x * 2 + off.x, d.y * 2 + off.y, d.z * 2 + off.z, 3) * amp * 2 if amp else 0.0)
        v.co = P(centre[0] + d.x * radii[0] * r, centre[1] + d.y * radii[1] * r, centre[2] + d.z * radii[2] * r)
    return obj_from_bmesh(bm, name)


def cone(name, a, b, r0, r1, sides=16, squash=1.0, bend=0.0):
    """A cone from game point a to b (radius r0 to r1), its cross-section squashed in game y, bent down at the tip."""
    bm = bmesh.new()
    a, b = V(a), V(b)
    ax = (b - a)
    L = ax.length
    d = ax.normalized()
    side = d.cross(V((0, 1, 0)))
    if side.length < 1e-4:
        side = V((1, 0, 0))
    side.normalize()
    up = side.cross(d).normalized()
    n = 10
    rings = []
    for i in range(n + 1):
        t = i / n
        r = r0 + (r1 - r0) * t
        c = a + d * (L * t) - up * (bend * t * t)
        ring = []
        for j in range(sides):
            ang = j / sides * math.tau
            p = c + side * (math.cos(ang) * r) + up * (math.sin(ang) * r * squash)
            ring.append(bm.verts.new(P(*p)))
        rings.append(ring)
    for i in range(n):
        for j in range(sides):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % sides], rings[i + 1][(j + 1) % sides], rings[i + 1][j]))
    c0 = bm.verts.new(P(*a))
    for j in range(sides):
        bm.faces.new((rings[0][(j + 1) % sides], rings[0][j], c0))
    tip = bm.verts.new(P(*(b - up * bend)))
    for j in range(sides):
        bm.faces.new((rings[-1][j], rings[-1][(j + 1) % sides], tip))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name)


def plate(name, centre_fn, width_fn, normal_fn, nu=24, nv=7, thick=0.0016, tip_round=True):
    """A thin solid feather/wing plate: centre_fn(u) the spine (game space), width_fn(u) its half-width, normal_fn(u) the
    direction across it (game space, unit); solidified to thick."""
    bm = bmesh.new()
    grid = []
    for i in range(nu + 1):
        u = i / nu
        c, w, acr = V(centre_fn(u)), width_fn(u), V(normal_fn(u))
        row = []
        for j in range(nv + 1):
            v = j / nv * 2 - 1
            row.append(bm.verts.new(P(*(c + acr * (v * w)))))
        grid.append(row)
    for i in range(nu):
        for j in range(nv):
            bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
    ob = obj_from_bmesh(bm, name)
    add_mod(ob, "SOLIDIFY", thickness=thick, offset=0.0)
    add_mod(ob, "SUBSURF", levels=1, render_levels=1)
    apply_mods(ob)
    return ob


def drop_islands(ob, keep_ratio=0.002):
    """Deletes the remesh's stray specks: any loose piece under keep_ratio of the biggest."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    seen = set()
    islands = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, isl = [v], []
        seen.add(v.index)
        while stack:
            x = stack.pop()
            isl.append(x)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        islands.append(isl)
    if len(islands) > 1:
        print(f"[birds] {ob.name}: islands " + ", ".join(f"{len(i)}@{tuple(round(c, 3) for c in G(i[0].co))}" for i in sorted(islands, key=len, reverse=True)[:8]))
        big = max(len(i) for i in islands)
        dead = [v for isl in islands if len(isl) < min(60, big * keep_ratio) for v in isl]
        if dead:
            bmesh.ops.delete(bm, geom=dead, context="VERTS")
            print(f"[birds] {ob.name}: dropped {len(dead)} vertices of specks")
    bm.to_mesh(ob.data)
    bm.free()


def remesh(ob, voxel, smooth=4, factor=0.6):
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel, adaptivity=0.0)
    apply_mods(ob)
    drop_islands(ob)
    add_mod(ob, "SMOOTH", factor=factor, iterations=smooth)
    apply_mods(ob)
    return ob


def set_part(ob, pid):
    """The part each vertex belongs to (for the painter, which runs again per colour after the join)."""
    me = ob.data
    a = me.attributes.get("part") or me.attributes.new("part", "INT", "POINT")
    a.data.foreach_set("value", np.full(len(me.vertices), pid, np.int32))
    return ob


def paint_all(ob, fn):
    """Vertex colours from fn(game position, game normal, part)."""
    me = ob.data
    if "Col" not in me.color_attributes:
        me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    parts = np.zeros(len(me.vertices), np.int32)
    me.attributes["part"].data.foreach_get("value", parts)
    cols = np.zeros((len(me.vertices), 4), np.float32)
    for v in me.vertices:
        r, g, b = fn(G(v.co), G(v.normal), int(parts[v.index]))
        cols[v.index] = (r, g, b, 1.0)
    me.color_attributes["Col"].data.foreach_set("color", cols.ravel())


def feather_bump(name, barb_scale, scale_scale, strength):
    """The high copy's material for the normal bake: fine barbs along each feather and the overlapping feather tips,
    weighted by the 'detail' attribute (0 on the beak, eyes and legs)."""
    mt = bpy.data.materials.new(name)
    mt.use_nodes = True
    nt = mt.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.wave_type = "BANDS"
    wave.bands_direction = "Y"
    wave.inputs["Scale"].default_value = barb_scale
    wave.inputs["Distortion"].default_value = 6.0
    wave.inputs["Detail"].default_value = 3.0
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.feature = "F1"
    vor.inputs["Scale"].default_value = scale_scale
    for n in (wave, vor):
        nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
    mix = nt.nodes.new("ShaderNodeMath")
    mix.operation = "MULTIPLY_ADD"
    nt.links.new(wave.outputs["Fac"], mix.inputs[0])
    mix.inputs[1].default_value = 0.35
    nt.links.new(vor.outputs["Distance"], mix.inputs[2])
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "detail"
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    nt.links.new(mix.outputs["Value"], mul.inputs[0])
    nt.links.new(attr.outputs["Fac"], mul.inputs[1])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    bump.inputs["Distance"].default_value = 0.0004
    nt.links.new(mul.outputs["Value"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mt


def detail_by_part(ob, values):
    me = ob.data
    parts = np.zeros(len(me.vertices), np.int32)
    me.attributes["part"].data.foreach_get("value", parts)
    a = me.attributes.get("detail") or me.attributes.new("detail", "FLOAT", "POINT")
    a.data.foreach_set("value", np.array([values.get(int(p), 0.0) for p in parts], np.float32))


def unwrap(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_set(low, high, prefix, size, bump, variants, out_dir, ext=0.004, dist=0.008):
    from mathutils.kdtree import KDTree
    kd = KDTree(len(high.data.vertices))
    for v in high.data.vertices:
        kd.insert(v.co, v.index)
    kd.balance()
    far = [v for v in low.data.vertices if kd.find(v.co)[2] > dist * 0.5]
    print(f"[birds] {prefix}: {len(far)} of {len(low.data.vertices)} low vertices more than {dist * 0.5:.4f} from the high copy" + (f", e.g. at game {tuple(round(c, 4) for c in G(far[0].co))}" if far else ""))
    """The normal map once (from high's bump), the occlusion once, and an albedo per variant (high repainted each time:
    variants is [(tag, paint fn)]). Returns (albedo images by tag, normal image)."""
    keep = [s.material for s in high.material_slots]
    W.set_all_materials(high, bump)
    nimg = W.new_image(prefix + "_normal", size, True)
    W.bake(low, high, "NORMAL", nimg, extrusion=ext, dist=dist)
    aimg = W.new_image(prefix + "_ao", size, True)
    W.bake(low, high, "AO", aimg, samples=48, extrusion=ext, dist=dist)   # (the smooth copy's: the decimated one's triangles showed as lines)
    a = np.array(aimg.pixels[:], np.float32).reshape(size, size, 4)[:, :, 0]
    for _ in range(3):
        a = (a + np.roll(a, 1, 0) + np.roll(a, -1, 0) + np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 5.0
    albedos = {}
    W.set_all_materials(high, W.emit_vc_material(prefix + "_vc"))
    for tag, fn in variants:
        paint_all(high, fn)
        cimg = W.new_image(f"{prefix}_albedo_{tag}", size, False)
        W.bake(low, high, "EMIT", cimg, extrusion=ext, dist=dist)
        c = np.array(cimg.pixels[:], np.float32).reshape(size, size, 4)
        c[:, :, :3] *= (0.66 + 0.34 * a)[:, :, None]
        cimg.pixels = c.ravel()
        cimg.filepath_raw = os.path.join(out_dir, cimg.name + ".png")
        cimg.file_format = "PNG"
        cimg.save()
        albedos[tag] = cimg
    nimg.filepath_raw = os.path.join(out_dir, nimg.name + ".png")
    nimg.file_format = "PNG"
    nimg.save()
    return albedos, nimg


def final_material(low, slot_name, albedo, normal, rough):
    old = bpy.data.materials.get(slot_name)
    if old is not None:
        old.name = slot_name + "_baking"
    mt = bpy.data.materials.new(slot_name)
    mt.use_nodes = True
    nt = mt.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = rough
    ti = nt.nodes.new("ShaderNodeTexImage")
    ti.image = albedo
    nt.links.new(ti.outputs["Color"], bsdf.inputs["Base Color"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = normal
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return mt


def weights(ob, arm_obj, bones, flesh_bones, rigid):
    """Rigid parts (by part id) wholly to their bone; the skin smoothly over its own bones (inverse distance, top two)."""
    me = ob.data
    Pv = np.array([v.co[:] for v in me.vertices], np.float32)
    parts = np.zeros(len(me.vertices), np.int32)
    me.attributes["part"].data.foreach_get("value", parts)
    info = {b[0]: b for b in bones}
    groups = {b[0]: ob.vertex_groups.new(name=b[0]) for b in bones if b[0] != "root"}
    D = np.zeros((len(Pv), len(flesh_bones)), np.float32)
    for i, n in enumerate(flesh_bones):
        _, h, t, _, r = info[n]
        d, _ = W.seg_dist(Pv, np.array(h[:], np.float32), np.array(t[:], np.float32))
        D[:, i] = np.maximum(d - r, 1e-5)
    for vi in range(len(Pv)):
        p = int(parts[vi])
        if p in rigid and callable(rigid[p]):
            bone = rigid[p](G(V(Pv[vi])))
            groups[bone].add([vi], 1.0, "REPLACE")
            continue
        if p in rigid:
            groups[rigid[p]].add([vi], 1.0, "REPLACE")
            continue
        order = np.argsort(D[vi])
        a, b = order[0], order[1]
        wa, wb = 1.0 / D[vi, a] ** 3, 1.0 / D[vi, b] ** 3
        s = wa + wb
        groups[flesh_bones[a]].add([vi], wa / s, "REPLACE")
        groups[flesh_bones[b]].add([vi], wb / s, "REPLACE")
    ob.parent = arm_obj
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj


class Clips:
    """Actions keyed on pose bones (rotations as armature-space turns about a bone's head; scales; root moves)."""

    def __init__(self, arm_obj):
        self.ao = arm_obj
        if arm_obj.animation_data is None:
            arm_obj.animation_data_create()
        self.actions = {}

    def pose(self, frame, turns=None, scales=None, root=None):
        """turns: {bone: [(armature-space axis (game), degrees)...]}; scales: {bone: s}; root: game-space offset."""
        turns = turns or {}
        scales = scales or {}
        for pb in self.ao.pose.bones:
            b = pb.bone
            q = Quaternion()
            for ax, deg in turns.get(pb.name, []):
                axis_arm = P(*ax).normalized()
                local = (b.matrix_local.to_3x3().inverted() @ axis_arm).normalized()
                q = Quaternion(local, math.radians(deg)) @ q
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = q
            s = scales.get(pb.name, 1.0)
            pb.scale = (s, s, s)
            if pb.name == "root":
                off = P(*root) if root else V((0, 0, 0))
                pb.location = b.matrix_local.to_3x3().inverted() @ off
            else:
                pb.location = (0, 0, 0)
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("scale", frame=frame)
            pb.keyframe_insert("location", frame=frame)

    def action(self, name, keys):
        """keys: [(frame, kwargs for pose)]."""
        act = bpy.data.actions.new(name)
        self.ao.animation_data.action = act
        for f, kw in keys:
            self.pose(f, **kw)
        self.actions[name] = act
        self.ao.animation_data.action = None
        tr = self.ao.animation_data.nla_tracks.new()
        tr.name = name
        tr.strips.new(name, int(act.frame_range[0]), act)
        tr.mute = True
        return act


def make_armature(name, bones):
    return W.make_armature(name, [(n, h, t, p, r) for (n, h, t, p, r) in bones])


def export(objs, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=False,
                              export_animations=True, export_skins=True, export_animation_mode="ACTIONS",
                              export_force_sampling=True, export_image_format="AUTO", export_materials="EXPORT")
    print(f"[birds] exported {path}")


def look(name, target, dist, height, yaw, textured=True):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE" if textured else "SINGLE"
    sh.single_color = (0.6, 0.58, 0.55)
    sh.show_shadows = True
    sh.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 900, 700
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
    t = P(*target)
    a = math.radians(yaw)
    cam.location = t + V((math.sin(a) * dist, math.cos(a) * dist, height))
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    cam.data.clip_start = 0.005
    sc.camera = cam
    sc.render.filepath = os.path.join(PREV, name + ".png")
    bpy.ops.render.render(write_still=True)


# ------------------------------------------------------------------ the bird

BIRD_OUT = os.path.join(ROOT, "assets", "models", "bird")
# part ids
FLESH, BEAK, EYE, TAIL, FOLD_L, FOLD_R, WING_L, WING_R, PRIM_L, PRIM_R, LEG_L, LEG_R = range(12)

BODY_RINGS = [   # (z, y, half-width, half-height): tail root to neck
    (-0.064, 0.075, 0.007, 0.006), (-0.052, 0.074, 0.016, 0.014), (-0.036, 0.072, 0.024, 0.023), (-0.018, 0.072, 0.03, 0.029),
    (0.0, 0.075, 0.033, 0.033), (0.016, 0.083, 0.033, 0.034), (0.03, 0.095, 0.03, 0.031), (0.04, 0.106, 0.025, 0.025),
    (0.047, 0.115, 0.02, 0.02),
]
HEAD_C = (0.0, 0.124, 0.045)
BEAK0, BEAK1 = (0.0, 0.124, 0.064), (0.0, 0.119, 0.087)
EYE_C = (0.0168, 0.133, 0.055)
SHOULDER = (0.027, 0.103, 0.026)

PALETTES = {
    "red": dict(body=(0.55, 0.13, 0.11), belly=(0.62, 0.21, 0.17), wing=(0.42, 0.12, 0.1), prim=(0.3, 0.09, 0.08),
                tail=(0.38, 0.11, 0.1), mask=(0.05, 0.04, 0.04), beak=(0.78, 0.42, 0.22), legs=(0.4, 0.3, 0.27)),
    "blue": dict(body=(0.22, 0.32, 0.54), belly=(0.36, 0.43, 0.56), wing=(0.16, 0.24, 0.44), prim=(0.1, 0.15, 0.3),
                 tail=(0.15, 0.22, 0.4), mask=(0.05, 0.05, 0.06), beak=(0.72, 0.44, 0.26), legs=(0.38, 0.32, 0.31)),
    "purple": dict(body=(0.42, 0.2, 0.44), belly=(0.5, 0.3, 0.5), wing=(0.3, 0.15, 0.32), prim=(0.2, 0.1, 0.22),
                   tail=(0.28, 0.14, 0.3), mask=(0.05, 0.04, 0.05), beak=(0.74, 0.44, 0.27), legs=(0.37, 0.3, 0.3)),
    "black": dict(body=(0.05, 0.05, 0.056), belly=(0.065, 0.065, 0.07), wing=(0.035, 0.035, 0.042), prim=(0.025, 0.025, 0.03),
                  tail=(0.03, 0.03, 0.036), mask=(0.02, 0.02, 0.02), beak=(0.1, 0.09, 0.09), legs=(0.09, 0.09, 0.09)),
}


def bird_body_width(z):
    for (z0, y0, w0, h0), (z1, y1, w1, h1) in zip(BODY_RINGS, BODY_RINGS[1:]):
        if z0 <= z <= z1:
            u = (z - z0) / (z1 - z0)
            return y0 + (y1 - y0) * u, w0 + (w1 - w0) * u, h0 + (h1 - h0) * u
    return BODY_RINGS[0][1:] if z < BODY_RINGS[0][0] else BODY_RINGS[-1][1:]


def build_bird():
    parts = []
    # the soft body: the loft, the head, the cheeks and the crest, one skin
    body = loft("Body", BODY_RINGS, sides=40, squash_below=1.08)
    head = ellipsoid("Head", HEAD_C, (0.0195, 0.021, 0.0235), seed=3, amp=0.01)
    cheekL = ellipsoid("CheekL", (0.009, 0.122, 0.054), (0.009, 0.011, 0.011))
    cheekR = ellipsoid("CheekR", (-0.009, 0.122, 0.054), (0.009, 0.011, 0.011))
    throat = ellipsoid("Throat", (0.0, 0.112, 0.05), (0.014, 0.014, 0.013))
    # (a cardinal's crest: a short swept-back tuft, not a horn)
    crest = cone("Crest", (0.0, 0.134, 0.048), (0.0, 0.16, 0.022), 0.0135, 0.0025, sides=20, squash=0.62, bend=-0.006)
    flesh = join([body, head, cheekL, cheekR, throat, crest], "Flesh")
    remesh(flesh, 0.0011, smooth=6, factor=0.55)
    set_part(flesh, FLESH)
    parts.append(flesh)
    # the beak: a thick cone, its upper half curved over
    beak = cone("Beak", BEAK0, BEAK1, 0.0088, 0.0006, sides=20, squash=0.85, bend=0.0025)
    add_mod(beak, "SUBSURF", levels=1, render_levels=1)
    apply_mods(beak)
    parts.append(set_part(beak, BEAK))
    # eyes
    for s, nm in ((1, "EyeL"), (-1, "EyeR")):
        e = ellipsoid(nm, (s * EYE_C[0], EYE_C[1], EYE_C[2]), (0.0044, 0.0044, 0.0044), subdiv=3)
        parts.append(set_part(e, EYE))
    # the tail: seven feathers fanned a little, overlapping
    for i in range(7):
        f = (i - 3) / 3.0
        root = V((f * 0.004, 0.075, -0.052))
        tip = V((f * 0.017, 0.058 - abs(f) * 0.002, -0.132 + abs(f) * 0.008))
        lift = 0.0008 * (3 - abs(i - 3))
        tf = plate(f"Tail{i}", lambda u, r=root, t=tip, l=lift: r.lerp(t, u) + V((0, l + 0.002 * math.sin(u * math.pi), 0)),
                   lambda u: 0.0068 * min(1.0, u * 4 + 0.3) * (1.0 if u < 0.85 else math.sqrt(max(0.0, (1 - u) / 0.15))),
                   lambda u, f=f: V((1, 0.15 * f, 0)).normalized(), nu=18, nv=4, thick=0.0012)
        parts.append(set_part(tf, TAIL))
    # folded wings: a shell lying on each flank (the body's own surface, just off it), from the shoulder past the rump,
    # the primaries' tips crossing over the tail
    for s, pid in ((1, FOLD_L), (-1, FOLD_R)):
        bm = bmesh.new()
        nu, nv = 30, 10
        z0, zb, zt = 0.03, -0.054, -0.094
        rows = []
        last = None
        for i in range(nu + 1):
            u = i / nu
            z = z0 + (zt - z0) * u
            row = []
            if z >= zb:
                yc, w, h = bird_body_width(z)
                top = math.radians(78 - 16 * u)
                bot = math.radians(-26 + 46 * u)
                for j in range(nv + 1):
                    v = j / nv
                    a = top + (bot - top) * v
                    k = 1.07 + 0.012 * math.sin(v * math.pi)
                    edge = 0.0012 * math.sin(z * 520) if j == nv else 0.0     # the coverts' scalloped lower edge
                    row.append(P(s * (math.cos(a) * w * k + 0.0012), yc + math.sin(a) * h * k + edge, z))
                last = (yc, w, h, top, bot)
            else:
                # past the body: the primaries narrow to their tips over the tail
                t = (z - zb) / (zt - zb)
                yc, w, h, top, bot = last
                for j in range(nv + 1):
                    v = j / nv
                    a = top + (bot - top) * v
                    base = V((s * (math.cos(a) * w * 1.07 + 0.0012), yc + math.sin(a) * h * 1.07, z))
                    tip = V((s * 0.008 + s * 0.004 * (v - 0.5), 0.081 + 0.004 * (0.5 - v), z))
                    row.append(P(*base.lerp(tip, t ** 0.8)))
            rows.append([bm.verts.new(p) for p in row])
        for i in range(nu):
            for j in range(nv):
                bm.faces.new((rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]))
        fw = obj_from_bmesh(bm, f"Fold{s}")
        add_mod(fw, "SOLIDIFY", thickness=0.0018, offset=0.0)
        add_mod(fw, "SUBSURF", levels=1, render_levels=1)
        apply_mods(fw)
        parts.append(set_part(fw, pid))
    # spread wings (for flight): the arm out to the wrist, the primaries fanned past it
    for s, pid, ppid in ((1, WING_L, PRIM_L), (-1, WING_R, PRIM_R)):
        sh = V((s * SHOULDER[0], SHOULDER[1] - 0.003, SHOULDER[2] - 0.004))
        # the coverts: a rounded, tapering lobe over the arm (thin at its edges, a little arched), not a slab
        def c_fn(u, sh=sh, s=s):
            return sh + V((s * 0.072 * u, 0.004 * math.sin(u * math.pi) + 0.002 * u, 0.006 - 0.014 * u - 0.006 * math.sin(u * math.pi)))

        def w_fn(u):
            return 0.0035 + 0.014 * math.sin(math.pi * min(1.0, 0.12 + u * 0.92))

        arm = plate(f"Arm{s}", c_fn, w_fn, lambda u: V((0, 0, 1)), nu=22, nv=8, thick=0.0013)
        parts.append(set_part(arm, pid))
        # the secondaries: a row of long feathers behind the arm, laid back and overlapping
        for k in range(7):
            u = 0.08 + k * 0.13
            root = c_fn(u) + V((0, -0.0006, -w_fn(u) * 0.6))
            d = V((s * (0.15 + 0.06 * k), 0.0, -1.0)).normalized()
            ln = 0.036 + 0.003 * math.sin(k * 0.7)
            sf = plate(f"Sec{s}_{k}", lambda t, r=root, d=d, ln=ln: r + d * (ln * t) + V((0, -0.0008 * t, 0)),
                       lambda t: 0.0062 * (1.0 if t < 0.8 else math.sqrt(max(0.0, (1 - t) / 0.2))) * min(1.0, t * 5 + 0.5),
                       lambda t, d=d: V((-d.z, 0, d.x)).normalized(), nu=12, nv=3, thick=0.0011)
            parts.append(set_part(sf, pid))
        wrist = sh + V((s * 0.07, 0.003, -0.01))
        for k in range(7):
            ang = math.radians(-8 - k * 13)             # swept back, fanned
            d = V((s * math.cos(ang), 0.0, math.sin(ang)))
            ln = 0.07 - k * 0.004
            base = wrist + V((0, 0, -0.006 * k))
            pf = plate(f"Prim{s}_{k}", lambda u, b=base, d=d, ln=ln: b + d * (ln * u) + V((0, -0.0006 * k, 0)),
                       lambda u: 0.0055 * (1.0 if u < 0.75 else math.sqrt(max(0.0, (1 - u) / 0.25))) * min(1.0, u * 5 + 0.4),
                       lambda u, d=d: V((-d.z, 0, d.x)).normalized(), nu=14, nv=3, thick=0.0012)
            parts.append(set_part(pf, ppid))
    # legs and toes: thin scaly sticks, three toes ahead and one behind, gripping
    for s, pid in ((1, LEG_L), (-1, LEG_R)):
        hip = V((s * 0.011, 0.058, 0.004))
        ankle = V((s * 0.012, 0.004, 0.006))
        pts = [hip, ankle]
        radii = [0.0032, 0.0022]
        edges = [(0, 1)]
        for tx, tz, ln in ((0.008, 1, 0.022), (0.0, 1, 0.024), (-0.006, 1, 0.021), (0.002, -1, 0.014)):
            mid = ankle + V((s * tx * 0.5, -0.0018, tz * ln * 0.5))
            tip = ankle + V((s * tx, -0.0028, tz * ln))
            i0 = len(pts)
            pts += [mid, tip]
            radii += [0.0014, 0.0007]
            edges += [(1, i0), (i0, i0 + 1)]
        leg = skin_tree(f"Leg{s}", [P(*p) for p in pts], edges, radii, None, subdiv=2)
        parts.append(set_part(leg, pid))
    return parts


def bird_bones():
    def B(n, h, t, p, r=0.004):
        return (n, P(*h), P(*t), p, r)
    out = [B("root", (0, 0, 0), (0, 0.03, 0), None, 0.0),
           B("body", (0, 0.072, -0.03), (0, 0.1, 0.03), "root", 0.028),
           B("neck", (0, 0.1, 0.03), (0, 0.116, 0.041), "body", 0.016),
           B("head", (0, 0.116, 0.041), (0, 0.142, 0.06), "neck", 0.018),
           B("tail", (0, 0.075, -0.05), (0, 0.06, -0.125), "body", 0.006)]
    for s, sx in ((1, "L"), (-1, "R")):
        out.append(B(f"wingfold_{sx}", (s * SHOULDER[0], SHOULDER[1], SHOULDER[2]), (s * 0.014, 0.082, -0.088), "body"))
        out.append(B(f"wing_{sx}", (s * SHOULDER[0], SHOULDER[1] - 0.003, SHOULDER[2] - 0.004), (s * 0.135, 0.1, -0.03), "body"))
        out.append(B(f"leg_{sx}", (s * 0.011, 0.058, 0.004), (s * 0.012, 0.004, 0.006), "body", 0.003))
    return out


def bird_painter(pal):
    pal = {k: srgb(v) for k, v in pal.items()}
    bx, by, bz = BEAK0

    def fn(p, n, part):
        x, y, z = p.x, p.y, p.z
        grain = 0.86 + 0.14 * (0.5 + fbm(x * 260, y * 260, z * 260, 3))
        if part == BEAK:
            c = pal["beak"]
            k = 0.8 + 0.25 * min(1.0, (z - bz) / 0.02)
            return tuple(v * k for v in c)
        if part == EYE:
            return (0.01, 0.01, 0.01)
        if part in (LEG_L, LEG_R):
            k = 0.85 + 0.15 * (0.5 + 0.5 * math.sin(y * 900))      # the scales' rings
            return tuple(v * k for v in pal["legs"])
        if part == TAIL:
            k = 0.95 - 0.35 * max(0.0, (-z - 0.06) / 0.07)            # darker toward the tips
            return tuple(v * k * grain for v in pal["tail"])
        if part in (FOLD_L, FOLD_R):
            rear = max(0.0, (-z - 0.02) / 0.07)
            c = [a + (b - a) * min(1.0, rear) for a, b in zip(pal["wing"], pal["prim"])]
            edge = 1.0 + 0.12 * max(0.0, math.sin(z * 260 + x * 40))  # the coverts' pale edges
            return tuple(v * edge * grain for v in c)
        if part in (WING_L, WING_R):
            return tuple(v * grain for v in pal["wing"])
        if part in (PRIM_L, PRIM_R):
            return tuple(v * (0.9 + 0.1 * grain) for v in pal["prim"])
        # the skin: the black mask round the beak and down the throat; the belly paler; the crest a shade darker
        to_beak = (V((x, y, z)) - V((0, by - 0.001, bz - 0.004))).length
        mask = to_beak < 0.021 and y < 0.137 and z > 0.036
        if mask:
            return pal["mask"]
        belly = max(0.0, min(1.0, (-n.y + 0.15) * 1.6)) * (1.0 if z < 0.04 else 0.5)
        c = [a + (b - a) * belly for a, b in zip(pal["body"], pal["belly"])]
        if y > 0.15:
            c = [v * 0.85 for v in c]
        return tuple(v * grain for v in c)
    return fn


def bird_clips(arm):
    X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)            # the game's axes
    folded = {"wing_L": 0.001, "wing_R": 0.001}
    flying = {"wingfold_L": 0.001, "wingfold_R": 0.001}
    c = Clips(arm)
    c.action("idle", [(0, dict(scales=folded)),
                      (14, dict(scales=folded, turns={"head": [(Y, 12), (X, -6)], "tail": [(X, -10)]})),
                      (22, dict(scales=folded, turns={"head": [(Y, 12), (X, -6)], "tail": [(X, 6)]})),
                      (36, dict(scales=folded, turns={"head": [(Y, -18)]})),
                      (48, dict(scales=folded))])
    c.action("look", [(0, dict(scales=folded)), (3, dict(scales=folded, turns={"head": [(Y, 55), (Z, 10)]})),
                      (12, dict(scales=folded, turns={"head": [(Y, 55), (Z, 10)]})),
                      (15, dict(scales=folded, turns={"head": [(Y, -45), (X, 8)]})),
                      (24, dict(scales=folded, turns={"head": [(Y, -45), (X, 8)]})), (27, dict(scales=folded))])
    c.action("peck", [(0, dict(scales=folded)),
                      (3, dict(scales=folded, turns={"body": [(X, 22)], "neck": [(X, 25)], "head": [(X, 30)], "tail": [(X, -15)]})),
                      (5, dict(scales=folded, turns={"body": [(X, 25)], "neck": [(X, 30)], "head": [(X, 38)], "tail": [(X, -18)]})),
                      (9, dict(scales=folded))])
    c.action("hop", [(0, dict(scales=folded)), (2, dict(scales=folded, root=(0, -0.004, 0))),
                     (5, dict(scales=folded, root=(0, 0.025, 0.02), turns={"tail": [(X, -12)]})),
                     (8, dict(scales=folded, root=(0, 0.0, 0.04))), (10, dict(scales=folded, root=(0, 0.0, 0.04)))])

    def beat(up):
        return {"wing_L": [(Z, -up)], "wing_R": [(Z, up)]}
    c.action("takeoff", [(0, dict(scales=flying, turns={**beat(60), "body": [(X, -15)]})),
                         (2, dict(scales=flying, turns={**beat(-45), "body": [(X, -25)], "tail": [(X, 15)]})),
                         (4, dict(scales=flying, turns={**beat(70), "body": [(X, -25)]})),
                         (6, dict(scales=flying, turns={**beat(-45), "body": [(X, -20)]})),
                         (8, dict(scales=flying, turns={**beat(70), "body": [(X, -15)]})),
                         (10, dict(scales=flying, turns={**beat(-40), "body": [(X, -10)]})),
                         (12, dict(scales=flying, turns=beat(60)))])
    c.action("flap", [(0, dict(scales=flying, turns=beat(65))), (2, dict(scales=flying, turns=beat(10))),
                      (4, dict(scales=flying, turns=beat(-45))), (6, dict(scales=flying, turns=beat(15))),
                      (8, dict(scales=flying, turns=beat(65)))])
    c.action("glide", [(0, dict(scales=flying, turns=beat(8))), (8, dict(scales=flying, turns=beat(12))),
                       (16, dict(scales=flying, turns=beat(8)))])
    c.action("lunge", [(0, dict(scales=flying, turns={**beat(40), "head": [(X, -20)]})),
                       (3, dict(scales=flying, turns={**beat(80), "head": [(X, -35)], "tail": [(X, 20)], "body": [(X, -30)]})),
                       (6, dict(scales=flying, turns={**beat(-20), "head": [(X, -10)], "body": [(X, 10)]})),
                       (10, dict(scales=flying, turns={**beat(85), "head": [(X, -30)], "tail": [(X, 25)], "body": [(X, -25)]}))])
    arm.animation_data.action = c.actions["idle"]
    return c


def make_bird():
    clear()
    os.makedirs(BIRD_OUT, exist_ok=True)
    parts = build_bird()
    high = join(parts, "BirdHigh")
    if high.data.validate(verbose=False, clean_customdata=False):
        print("[birds] bird mesh repaired")
    low = high.copy()
    low.data = high.data.copy()
    bpy.context.scene.collection.objects.link(low)
    low.name = "BirdMesh"
    add_mod(low, "DECIMATE", ratio=min(1.0, 11000 / max(W.tri_count(low), 1)))
    apply_mods(low)
    # the eyes their own material (the omen's glow is set by the game on this surface)
    feather = bpy.data.materials.new("bird_feather")
    eye = bpy.data.materials.new("bird_eye")
    for ob in (low, high):
        ob.data.materials.clear()
        ob.data.materials.append(feather)
        ob.data.materials.append(eye)
        parts_a = np.zeros(len(ob.data.vertices), np.int32)
        ob.data.attributes["part"].data.foreach_get("value", parts_a)
        for poly in ob.data.polygons:
            poly.material_index = 1 if all(parts_a[v] == EYE for v in poly.vertices) else 0
    for mt in (feather, eye):
        mt.use_nodes = True
    unwrap(low)
    print(f"[birds] bird: low {W.tri_count(low)} triangles, high {W.tri_count(high)}")
    detail_by_part(high, {FLESH: 1.0, TAIL: 0.7, FOLD_L: 0.8, FOLD_R: 0.8, WING_L: 0.8, WING_R: 0.8, PRIM_L: 0.6, PRIM_R: 0.6})
    W.PREV = PREV
    bump = feather_bump("bird_bump", 1100.0, 260.0, 0.25)
    variants = [(tag, bird_painter(PALETTES[tag])) for tag in ("red", "blue", "purple", "black")]
    albedos, normal = bake_set(low, high, "bird", 1024, bump, variants, BIRD_OUT)
    bpy.data.objects.remove(high, do_unlink=True)
    low.data.materials[0] = final_material(low, "bird_feather", albedos["red"], normal, 0.75)
    if bpy.data.materials.get("bird_eye"):
        bpy.data.materials["bird_eye"].name = "bird_eye_baking"
    em = bpy.data.materials.new("bird_eye")
    em.use_nodes = True
    b = em.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.008, 0.008, 0.008, 1)
    b.inputs["Roughness"].default_value = 0.08
    low.data.materials[1] = em
    bones = bird_bones()
    arm = make_armature("Bird", bones)
    rigid = {BEAK: "head", EYE: "head", TAIL: "tail", FOLD_L: "wingfold_L", FOLD_R: "wingfold_R", WING_L: "wing_L",
             WING_R: "wing_R", PRIM_L: "wing_L", PRIM_R: "wing_R", LEG_L: "leg_L", LEG_R: "leg_R"}
    weights(low, arm, bones, ["body", "neck", "head", "tail"], rigid)
    clips = bird_clips(arm)
    export([arm, low], os.path.join(BIRD_OUT, "bird.glb"))
    # previews: the rest pose (wings both ways), and the idle and the flap
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.scale = (1, 1, 1)
        pb.location = (0, 0, 0)
    look("bird_rest_side", (0, 0.09, 0.0), 0.42, 0.04, 90)
    arm.animation_data.action = clips.actions["idle"]
    bpy.context.scene.frame_set(0)
    look("bird_idle_side", (0, 0.09, 0.0), 0.36, 0.03, 90)
    look("bird_idle_front", (0, 0.1, 0.02), 0.32, 0.03, 200)
    look("bird_idle_head", HEAD_C, 0.13, 0.01, 60)
    arm.animation_data.action = clips.actions["flap"]
    bpy.context.scene.frame_set(0)
    look("bird_flap_above", (0, 0.09, 0.0), 0.45, 0.32, 30)
    print(f"[birds] bird done: {W.tri_count(low)} triangles")


# ------------------------------------------------------------------ the frog

FROG_OUT = os.path.join(ROOT, "assets", "models", "frog")
F_FLESH, F_EYE, F_THROAT = 0, 1, 2

FROG_RINGS = [   # (z, y, half-width, half-height): rump (+z) to snout (-z)
    (0.037, 0.015, 0.006, 0.005), (0.031, 0.017, 0.015, 0.011), (0.02, 0.02, 0.021, 0.015), (0.004, 0.023, 0.024, 0.016),
    (-0.012, 0.023, 0.023, 0.014), (-0.026, 0.021, 0.021, 0.012), (-0.038, 0.019, 0.017, 0.0095), (-0.047, 0.017, 0.011, 0.0068),
    (-0.053, 0.016, 0.004, 0.003),
]
F_EYE_C = (0.0128, 0.031, -0.03)


def frog_body_at(z):
    """The frog body's ring at z: (centre height, half-width, half-height)."""
    rings = sorted(FROG_RINGS)
    for (z0, y0, w0, h0), (z1, y1, w1, h1) in zip(rings, rings[1:]):
        if z0 <= z <= z1:
            u = (z - z0) / (z1 - z0)
            return y0 + (y1 - y0) * u, w0 + (w1 - w0) * u, h0 + (h1 - h0) * u
    return (rings[0][1:] if z < rings[0][0] else rings[-1][1:])


def build_frog(voxel=0.00045):
    parts = []
    body = loft("FBody", FROG_RINGS, sides=40, squash_below=0.75)
    bulges = [ellipsoid(f"Bulge{s}", (s * 0.0122, 0.0292, -0.03), (0.0068, 0.0062, 0.0068)) for s in (1, -1)]
    limbs = []
    for s in (1, -1):
        # the hind leg folded tight: thigh forward along the flank, shin back, the long foot forward, its toes splayed
        hip = V((s * 0.016, 0.014, 0.026))
        knee = V((s * 0.03, 0.011, -0.002))
        ankle = V((s * 0.026, 0.006, 0.034))
        heel = V((s * 0.03, 0.003, 0.02))
        pts = [hip, knee, ankle, heel]
        radii = [0.0088, 0.0058, 0.0036, 0.0028]
        edges = [(0, 1), (1, 2), (2, 3)]
        for k, (ang, ln) in enumerate(((-30, 0.022), (-12, 0.026), (5, 0.024), (22, 0.019), (40, 0.014))):
            a = math.radians(ang)
            d = V((s * math.sin(a), 0, -math.cos(a)))
            mid = heel + d * (ln * 0.55) + V((0, -0.001, 0))
            tip = heel + d * ln + V((0, -0.0018, 0))
            i0 = len(pts)
            pts += [mid, tip]
            radii += [0.0013, 0.0012]
            edges += [(3, i0), (i0, i0 + 1)]
        limbs.append(skin_tree(f"Hind{s}", [P(*p) for p in pts], edges, radii, None, subdiv=1))
        # the front leg: down and in, the hand turned in, four slim fingers
        sh = V((s * 0.012, 0.014, -0.022))
        el = V((s * 0.02, 0.008, -0.03))
        wr = V((s * 0.018, 0.0025, -0.038))
        # (solid cones and joint balls: the skin modifier's front leg came out unclosed and the remesh lost it)
        limbs.append(cone(f"Upper{s}", sh, el, 0.0046, 0.0035, sides=16))
        limbs.append(cone(f"Fore{s}", el, wr, 0.0035, 0.0028, sides=16))
        limbs.append(ellipsoid(f"Elbow{s}", tuple(el), (0.0036, 0.0036, 0.0036), subdiv=3))
        limbs.append(ellipsoid(f"Hand{s}", tuple(wr + V((0, -0.0004, -0.002))), (0.0042, 0.0018, 0.0036), subdiv=3))
        for k, ang in enumerate((-40, -10, 15, 45)):
            a = math.radians(ang)
            d = V((s * math.sin(a), 0, -math.cos(a)))
            tip = wr + d * 0.0088 + V((0, -0.0016, 0))
            limbs.append(cone(f"Finger{s}_{k}", wr, tip, 0.0017, 0.0013, sides=10))
            limbs.append(ellipsoid(f"Pad{s}_{k}", tuple(tip), (0.0016, 0.0013, 0.0016), subdiv=2))
    flesh = join([body] + bulges + limbs, "FFlesh")
    remesh(flesh, voxel, smooth=5, factor=0.5)
    set_part(flesh, F_FLESH)
    parts.append(flesh)
    for s in (1, -1):
        e = ellipsoid(f"FEye{s}", (s * (F_EYE_C[0] + 0.0012), F_EYE_C[1] + 0.001, F_EYE_C[2] - 0.0005), (0.0056, 0.0056, 0.0056), subdiv=3)
        parts.append(set_part(e, F_EYE))
    th = ellipsoid("FThroat", (0.0, 0.0148, -0.034), (0.0108, 0.0048, 0.0095), subdiv=4)
    parts.append(set_part(th, F_THROAT))
    return parts


def frog_painter():
    skin, blotch, pale = srgb((0.5, 0.56, 0.24)), srgb((0.24, 0.29, 0.12)), srgb((0.8, 0.76, 0.54))
    ridge_c = srgb((0.62, 0.6, 0.32))
    gold, black = srgb((0.66, 0.52, 0.18)), (0.006, 0.006, 0.005)

    def fn(p, n, part):
        x, y, z = p.x, p.y, p.z
        if part == F_THROAT:
            return tuple(v * (0.92 + 0.08 * (0.5 + fbm(x * 600, y * 600, z * 600, 2))) for v in pale)
        if part == F_EYE:
            ex = abs(x) - F_EYE_C[0] - 0.0012
            ey = y - F_EYE_C[1] - 0.001
            out = ex > 0.0015                                   # the side of the eyeball facing out
            if out and abs(ey) < 0.0012 and abs(z - F_EYE_C[2]) < 0.0034:
                return black                                    # the pupil, a dark bar
            fleck = 0.85 + 0.25 * (0.5 + fbm(x * 2500, y * 2500, z * 2500, 2))
            return tuple(v * fleck for v in gold)
        # pale below: by height on the body (the remeshed normals are too grainy to decide it: they speckled the back)
        yb0, wb0, hb0 = frog_body_at(z)
        if abs(x) <= wb0 + 0.002:
            under = max(0.0, min(1.0, (yb0 - 0.25 * hb0 - y) / (0.45 * hb0)))
        else:
            under = max(0.0, min(1.0, (0.0045 - y) / 0.003))
        b = fbm(x * 140 + 3.1, y * 140, z * 140, 3)
        c = skin
        if b > 0.12:
            c = blotch
        elif b > 0.05:
            k = (b - 0.05) / 0.07
            c = [a + (bb - a) * k for a, bb in zip(skin, blotch)]
        # the pale line down the back's edges (the dorsolateral folds)
        if abs(abs(x) - 0.0145) < 0.0022 and -0.02 < z < 0.028 and n.y > 0.4:
            c = ridge_c
        # the legs barred darker (only out past the body's own flank)
        yb, wb, hb = frog_body_at(z)
        if abs(x) > wb + 0.0025 and n.y > -0.2:
            if math.sin((z * 0.7 + abs(x)) * 520) > 0.55:
                c = [v * 0.6 for v in c]
        c = [a + (bb - a) * under for a, bb in zip(c, pale)]
        g = 0.9 + 0.12 * (0.5 + fbm(x * 900, y * 900, z * 900, 2))
        return tuple(v * g for v in c)
    return fn


def frog_bones():
    def B(n, h, t, p, r=0.004):
        return (n, P(*h), P(*t), p, r)
    out = [B("root", (0, 0, 0), (0, 0.01, 0), None, 0.0),
           B("body", (0, 0.02, 0.03), (0, 0.022, -0.02), "root", 0.014),
           B("head", (0, 0.022, -0.02), (0, 0.017, -0.05), "body", 0.012),
           B("throat", (0, 0.012, -0.028), (0, 0.011, -0.04), "head", 0.003)]
    for s, sx in ((1, "L"), (-1, "R")):
        out.append(B(f"thigh_{sx}", (s * 0.016, 0.014, 0.026), (s * 0.03, 0.011, -0.002), "body", 0.006))
        out.append(B(f"shin_{sx}", (s * 0.03, 0.011, -0.002), (s * 0.026, 0.006, 0.034), f"thigh_{sx}", 0.004))
        out.append(B(f"foot_{sx}", (s * 0.03, 0.003, 0.02), (s * 0.03, 0.002, -0.004), f"shin_{sx}", 0.003))
        out.append(B(f"arm_{sx}", (s * 0.012, 0.014, -0.022), (s * 0.018, 0.0025, -0.038), "body", 0.003))
    return out


def frog_clips(arm):
    X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)
    c = Clips(arm)
    c.action("idle", [(0, dict()), (20, dict(turns={"body": [(X, -1.5)], "head": [(X, 2)]})), (40, dict())])
    c.action("throat", [(0, dict(scales={"throat": 1.0})), (4, dict(scales={"throat": 1.55})), (8, dict(scales={"throat": 1.0}))])
    c.action("hop", [(0, dict()),
                     (3, dict(root=(0, -0.002, 0.003), turns={"body": [(X, 8)]})),
                     (6, dict(root=(0, 0.03, -0.04), turns={"body": [(X, -25)], "thigh_L": [(Y, -40)], "thigh_R": [(Y, 40)],
                                                             "shin_L": [(Y, -60)], "shin_R": [(Y, 60)]})),
                     (10, dict(root=(0, 0.02, -0.08), turns={"body": [(X, 10)]})),
                     (14, dict(root=(0, 0.0, -0.1)))])
    arm.animation_data.action = c.actions["idle"]
    return c


def make_frog():
    clear()
    os.makedirs(FROG_OUT, exist_ok=True)
    # one build at a middle resolution (a finer remesh lost the thin front legs): the bake's copy is it subdivided, the
    # game's copy it decimated, so the two always lie on each other
    low = join(build_frog(0.0007), "FrogMesh")
    low.data.validate(verbose=False, clean_customdata=False)
    high = low.copy()
    high.data = low.data.copy()
    bpy.context.scene.collection.objects.link(high)
    high.name = "FrogHigh"
    add_mod(high, "SUBSURF", levels=1, render_levels=1)
    apply_mods(high)
    if os.environ.get("BIRDS_DEBUG"):
        look("frog_debug_high", (0, 0.015, -0.02), 0.13, 0.05, 220, textured=False)
    add_mod(low, "DECIMATE", ratio=min(1.0, 16000 / max(W.tri_count(low), 1)))
    apply_mods(low)
    skin = bpy.data.materials.new("frog_skin")
    skin.use_nodes = True
    for ob in (low, high):
        ob.data.materials.clear()
        ob.data.materials.append(skin)
    unwrap(low)
    print(f"[birds] frog: low {W.tri_count(low)} triangles, high {W.tri_count(high)}")
    detail_by_part(high, {F_FLESH: 1.0, F_THROAT: 0.4, F_EYE: 0.0})
    W.PREV = PREV
    bump = W.bump_material("frog_bump", 9.0)
    albedos, normal = bake_set(low, high, "frog", 1024, bump, [("green", frog_painter())], FROG_OUT, ext=0.0015, dist=0.004)
    bpy.data.objects.remove(high, do_unlink=True)
    low.data.materials[0] = final_material(low, "frog_skin", albedos["green"], normal, 0.28)
    bones = frog_bones()
    arm = make_armature("Frog", bones)
    weights(low, arm, bones, [b[0] for b in bones if b[0] not in ("root",)], {F_EYE: "head", F_THROAT: "throat"})
    clips = frog_clips(arm)
    export([arm, low], os.path.join(FROG_OUT, "frog.glb"))
    arm.animation_data.action = clips.actions["idle"]
    bpy.context.scene.frame_set(0)
    look("frog_side", (0, 0.015, -0.005), 0.16, 0.03, 90)
    look("frog_front", (0, 0.018, -0.03), 0.12, 0.03, 200)
    look("frog_above", (0, 0.015, 0.0), 0.15, 0.12, 30)
    print(f"[birds] frog done: {W.tri_count(low)} triangles")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["bird", "frog"]
    if "bird" in args:
        make_bird()
    if "frog" in args:
        make_frog()
