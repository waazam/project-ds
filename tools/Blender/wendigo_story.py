"""What the wendigo wears of Mitchell Webber (STORY.md Rev 5, 2026-10-16: the stalker in many masks; the wendigo is the
stalker wearing Mitchell, his badge, his name plate, his voice). Run headless:

    blender -b --python tools/Blender/wendigo_story.py

-> assets/models/wendigo/wendigo_story.glb:
- Badge: Mitchell's ranger badge, a brass shield (about 9 cm tall) with a raised rim, an enamelled field, a seal in the
  middle and the bar across its top, "CHIEF RANGER" on the bar and "OVERLOOK PARK" round the seal's foot; its pin on the
  back. Its own frame: the face toward the game's +Z, up +Y, the centre at the origin.
- NamePlate: a desk name plate like the one on his father's desk, torn off its wooden wedge: a brass plate with a black
  face and the brass letters "MITCHELL WEBBER", "CHIEF RANGER" small under it, a strip of the walnut still on its back
  and two bent nails through it (how it hangs on the hide). Its own frame, as the badge's.
- ChestBadge, ChestPlate: the same two where they hang on the wendigo's chest, in the wendigo model's own space at rest
  (wendigo.glb's skeleton space: the game attaches them to the "chest" bone with these transforms). The badge on its
  left breast, the plate on its right, over the tear in its chest.
- AshPile: where the mask fell and burned (Act 25): a low drift of grey-black ash about 1.3 m across, charred scraps in
  it, a few cinders glowing a steady dull orange (no flicker). Its base at y = 0.

The chest's surface is wendigo.py's own torso (its rings along its spine), pushed out past the ribs.
build/blender/wendigo_story_*.png are the previews (the two on the creature's chest with wendigo.glb imported).
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix
import kit
from kit import clear, mat, box, cylinder, join, export, preview, lumpy_sphere, add_mod, apply_mods, ROOT
import wendigo as W

kit.OUT = os.path.join(ROOT, "assets", "models", "wendigo")
os.makedirs(kit.OUT, exist_ok=True)
V = Vector


def mats():
    return dict(
        brass=mat("ws_brass", (0.72, 0.55, 0.27), 0.38, 0.85),
        brass_dark=mat("ws_brass_dark", (0.42, 0.31, 0.15), 0.55, 0.8),
        enamel=mat("ws_enamel", (0.05, 0.075, 0.06), 0.35, 0.0),
        black=mat("ws_plate_black", (0.03, 0.03, 0.03), 0.4, 0.2),
        walnut=mat("ws_walnut", (0.2, 0.11, 0.055), 0.7, 0.0),
        iron=mat("ws_iron", (0.16, 0.15, 0.14), 0.6, 0.7),
        ash=mat("ws_ash", (0.16, 0.155, 0.15), 1.0, 0.0),
        char=mat("ws_char", (0.03, 0.028, 0.026), 0.95, 0.0),
        cinder=mat("ws_cinder", (0.25, 0.08, 0.02), 0.9, 0.0, emit=(0.9, 0.28, 0.05)),
    )


def apply(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def smooth(ob, angle=35):
    for p in ob.data.polygons:
        p.use_smooth = True
    if hasattr(ob.data, "use_auto_smooth"):
        ob.data.use_auto_smooth = True
        ob.data.auto_smooth_angle = math.radians(angle)
    return ob


def prism(name, outline, y0, y1, material, bevel=0.0):
    """A flat shape (outline: points (x, z) in Blender, its face toward -Y) extruded from y0 (the face) to y1."""
    bm = bmesh.new()
    front = [bm.verts.new(V((x, y0, z))) for x, z in outline]
    back = [bm.verts.new(V((x, y1, z))) for x, z in outline]
    bm.faces.new(front[::-1])
    bm.faces.new(back)
    n = len(outline)
    for i in range(n):
        k = (i + 1) % n
        bm.faces.new((front[i], front[k], back[k], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = kit.obj_from_bmesh(bm, name, material, smooth=False)
    if bevel > 0:
        add_mod(ob, "BEVEL", width=bevel, segments=2, limit_method="ANGLE")
        apply_mods(ob)
    kit.uv_box(ob, 30.0)
    return smooth(ob, 40)


def outline(right):
    """The whole shield from its right half (from the top's middle round to the point): mirrored for the left."""
    return right + [(-x, z) for x, z in reversed(right[1:-1])]


def text(name, body, size, centre, material, depth=0.0006, bold=False):
    """Raised lettering on the face (toward -Y): a text object turned to stand in the XZ plane, made a mesh."""
    bpy.ops.object.text_add(location=(0, 0, 0))
    ob = bpy.context.active_object
    ob.name = name
    ob.data.body = body
    ob.data.size = size
    ob.data.extrude = depth
    ob.data.align_x = "CENTER"
    ob.data.align_y = "CENTER"
    ob.data.space_character = 1.05
    if bold:
        ob.data.offset = size * 0.012
    bpy.ops.object.convert(target="MESH")
    ob = bpy.context.active_object
    ob.rotation_euler = (math.radians(90), 0, 0)
    ob.location = V(centre)
    apply(ob)
    ob.data.materials.clear()
    ob.data.materials.append(material)
    return ob


def arc_text(prefix, body, size, radius, centre_z, y, material, span=2.2):
    """Letters round the foot of a circle (a seal's legend), each turned to the curve."""
    obs = []
    n = len(body)
    for i, ch in enumerate(body):
        if ch == " ":
            continue
        sa = (i / max(n - 1, 1) - 0.5) * span
        a = -math.pi * 0.5 + sa
        x, z = math.cos(a) * radius, centre_z + math.sin(a) * radius
        ob = text(f"{prefix}{i}", ch, size, (0, 0, 0), material)
        ob.data.transform(Matrix.Rotation(-sa, 4, "Y"))
        ob.data.transform(Matrix.Translation(V((x, y, z))))
        obs.append(ob)
    return obs


# ------------------------------------------------------------------ the badge

def badge(m):
    w, h = 0.074, 0.09
    out = outline(shield_pts(w, h))
    inner = outline(shield_pts(w * 0.84, h * 0.84))
    p = [prism("plate", out, -0.0035, 0.0015, m["brass"], bevel=0.0009)]
    field = prism("field", inner, -0.0041, -0.0030, m["enamel"])
    field.data.transform(Matrix.Translation(V((0, 0, -h * 0.03))))
    p.append(field)
    # the bar across the top, and its lettering
    p.append(box("bar", V((0, -0.0046, h * 0.335)), V((w * 0.78, 0.0018, 0.013)), m["brass"], bevel=0.0005))
    p.append(text("bar_txt", "CHIEF RANGER", 0.0072, (0, -0.0058, h * 0.335 - 0.0026), m["enamel"], depth=0.0003, bold=True))
    # the seal: a brass disc, a ring round it, a pine in the middle (three tiers and a trunk)
    sz = -h * 0.06
    p.append(cylinder("seal", V((0, -0.0042, sz)), V((0, -0.0058, sz)), 0.019, 0.0185, 32, m["brass"]))
    bpy.ops.mesh.primitive_torus_add(major_radius=0.0185, minor_radius=0.0012, major_segments=40, minor_segments=6, location=(0, -0.0058, sz), rotation=(math.radians(90), 0, 0))
    ring = bpy.context.active_object
    ring.name = "ring"
    ring.data.materials.append(m["brass_dark"])
    apply(ring)
    p.append(ring)
    for k, (zt, wt) in enumerate(((0.009, 0.006), (0.003, 0.009), (-0.004, 0.012))):
        tri = [(0, sz + zt + 0.007), (-wt, sz + zt - 0.003), (wt, sz + zt - 0.003)]
        p.append(prism(f"pine{k}", tri, -0.0066, -0.0056, m["enamel"]))
    p.append(box("trunk", V((0, -0.0061, sz - 0.0105)), V((0.003, 0.001, 0.004)), m["enamel"]))
    # the park's name round the seal's foot
    p += arc_text("legend", "OVERLOOK PARK", 0.0042, 0.0245, sz, -0.0046, m["brass_dark"], span=2.4)
    # the pin on the back, and its catch
    p.append(cylinder("pin", V((-0.026, 0.0028, h * 0.12)), V((0.026, 0.0028, h * 0.12)), 0.0007, 0.0007, 8, m["iron"]))
    p.append(box("catch", V((0.024, 0.0026, h * 0.12)), V((0.006, 0.003, 0.005)), m["iron"]))
    ob = join(p, "Badge")
    # (into its own frame: the face toward Blender -Y is the game's +Z)
    return smooth(ob, 40)


def shield_pts(w, h, k=36):
    pts = []
    for i in range(k + 1):
        u = i / k
        if u < 0.2:
            x, z = w * 0.5 * (u / 0.2) * 0.8, h * 0.5
        elif u < 0.28:
            v = (u - 0.2) / 0.08
            x, z = w * 0.5 * (0.8 + 0.2 * v), h * 0.5 - h * 0.09 * v
        else:
            v = (u - 0.28) / 0.72
            x = w * 0.5 * max(math.cos(v * math.pi * 0.5), 0.0) ** 0.7 * (1 + 0.1 * math.sin(v * math.pi))
            z = h * 0.41 - h * 0.91 * v
        pts.append((x, z))
    pts[-1] = (0.0, pts[-1][1])
    return pts


# ------------------------------------------------------------------ the name plate

def name_plate(m):
    pw, ph = 0.2, 0.05
    p = [box("walnut", V((0, 0.006, -0.002)), V((pw + 0.014, 0.008, ph + 0.012)), m["walnut"], bevel=0.0015)]
    # (the wedge's broken edge along the bottom: a few splinters)
    rnd = random.Random(4)
    for i in range(6):
        x = rnd.uniform(-pw * 0.48, pw * 0.48)
        p.append(box(f"splinter{i}", V((x, 0.006, -ph * 0.5 - 0.009 - rnd.uniform(0, 0.004))), V((rnd.uniform(0.006, 0.02), 0.006, rnd.uniform(0.004, 0.01))), m["walnut"]))
    p.append(box("brass", V((0, 0.0005, 0)), V((pw, 0.003, ph)), m["brass"], bevel=0.0008))
    p.append(box("face", V((0, -0.0012, 0)), V((pw - 0.008, 0.0008, ph - 0.008)), m["black"]))
    p.append(text("name", "MITCHELL WEBBER", 0.0165, (0, -0.0017, 0.0045), m["brass"], depth=0.0004, bold=True))
    p.append(text("title", "CHIEF RANGER", 0.0075, (0, -0.0017, -0.0135), m["brass"], depth=0.0003))
    # two bent nails through it into whatever it hangs on
    for sx in (-1, 1):
        x = sx * (pw * 0.5 - 0.009)
        p.append(cylinder(f"nail{sx}", V((x, -0.0025, 0)), V((x, 0.02, 0)), 0.0012, 0.0012, 8, m["iron"]))
        p.append(cylinder(f"nailhead{sx}", V((x, -0.0032, 0)), V((x, -0.0022, 0)), 0.0032, 0.0032, 10, m["iron"]))
    ob = join(p, "NamePlate")
    return smooth(ob, 40)


# ------------------------------------------------------------------ on its chest

def surface_frame(z, th, out, creature):
    """Where on the creature's hide (its rest pose) a flat thing hangs: aimed at wendigo.py's torso at height z, angle th
    round it (pi/2 its front), a ray from in front finds the skin itself (through its hanging hair and strips), and four
    more round it give the hide's slope there. Returns the frame: its face (the local -Y) along the hide's outward
    normal, its up along the spine, `out` off the skin."""
    t = W.t_at_z(z)
    c0 = W.torso_point(t, th, 0.0)
    s0 = W.spine_at(t)
    n0 = c0 - s0
    n0.z = 0.0
    n0.normalize()
    up0 = (W.spine_at(min(t + 0.02, 1)) - W.spine_at(max(t - 0.02, 0))).normalized()
    side0 = up0.cross(n0).normalized()

    def hit(p):
        o = p + n0 * 1.5
        d = -n0
        dg = bpy.context.evaluated_depsgraph_get()
        for _ in range(40):
            ok, loc, nrm, idx, ob, _m = bpy.context.scene.ray_cast(dg, o, d)
            if not ok:
                return None
            if ob in creature:
                me = ob.evaluated_get(dg).data
                mi = me.polygons[idx].material_index if idx < len(me.polygons) else 0
                mt = ob.material_slots[mi].material if mi < len(ob.material_slots) else None
                if mt is not None and mt.name.startswith("w_skin"):
                    return loc
            o = loc + d * 0.002
        return None

    c = hit(c0)
    n = n0
    if c is None:
        print(f"[wendigo_story] no hide found at z {z}, th {th:.2f}: on the loft")
        c = W.torso_point(t, th, 0.05)
    else:
        e = 0.035
        a, b = hit(c0 + side0 * e), hit(c0 - side0 * e)
        u, dn = hit(c0 + up0 * e), hit(c0 - up0 * e)
        if None not in (a, b, u, dn):
            n = (a - b).cross(u - dn).normalized()
            if n.dot(n0) < 0:
                n = -n
            # (the hide's ribs and cracks: halfway to the torso's own way out, so it hangs flat, not on one rib's slope)
            n = (n + n0).normalized()
    up = (up0 - n * up0.dot(n)).normalized()
    y = -n                  # (the local face is toward -Y)
    x = y.cross(up).normalized()
    up = x.cross(y).normalized()
    M = Matrix((x, y, up)).transposed().to_4x4()
    M.translation = c + n * out
    print(f"[wendigo_story] at z {z}, th {th:.2f}: {tuple(round(v, 3) for v in M.translation)}")
    return M


def on_chest(src, name, M, tilt, scale=1.0):
    ob = src.copy()
    ob.data = src.data.copy()
    ob.name = name
    bpy.context.scene.collection.objects.link(ob)
    ob.data.transform(Matrix.Rotation(tilt, 4, "Y") @ Matrix.Scale(scale, 4))   # (hung a little crooked; at its size, a man's badge is a fleck)
    ob.data.transform(M)
    return ob


# ------------------------------------------------------------------ the ash

def ash_pile(m):
    rnd = random.Random(25)
    p = [lumpy_sphere("ash", V((0, 0, 0.0)), (0.66, 0.58, 0.09), m["ash"], 25, amp=0.25, freq=2.6, subdiv=4, flat_bottom=0.0)]
    # a darker scorched ring round it, and the charred scraps in it (the mask's: hide and bone gone black)
    p.append(lumpy_sphere("scorch", V((0, 0, -0.01)), (0.9, 0.8, 0.03), m["char"], 26, amp=0.3, freq=2.0, subdiv=3, flat_bottom=0.0))
    for i in range(14):
        a, r = rnd.uniform(0, math.tau), rnd.uniform(0.05, 0.55)
        c = V((math.cos(a) * r, math.sin(a) * r * 0.88, 0.04))
        if rnd.random() < 0.5:
            p.append(lumpy_sphere(f"scrap{i}", c, (rnd.uniform(0.03, 0.08), rnd.uniform(0.02, 0.05), rnd.uniform(0.012, 0.03)), m["char"], 60 + i, amp=0.3, subdiv=2))
        else:
            # a charred stick of bone
            d = V((math.cos(a + 1.3), math.sin(a + 1.3), 0)) * rnd.uniform(0.06, 0.16)
            p.append(cylinder(f"bone{i}", c - d, c + d + V((0, 0, 0.012)), rnd.uniform(0.008, 0.014), None, 7, m["char"]))
    for i in range(9):
        a, r = rnd.uniform(0, math.tau), rnd.uniform(0.04, 0.45)
        p.append(lumpy_sphere(f"cinder{i}", V((math.cos(a) * r, math.sin(a) * r, 0.055)), (rnd.uniform(0.008, 0.02),) * 2 + (0.006,), m["cinder"], 90 + i, amp=0.3, subdiv=1))
    ob = join(p, "AshPile")
    kit.uv_box(ob, 3.0)
    return ob


def build():
    clear()
    m = mats()
    b = badge(m)
    plate = name_plate(m)
    ash = ash_pile(m)
    # the creature itself, at rest, to find its hide
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(kit.OUT, "wendigo.glb"))
    creature = [o for o in bpy.data.objects if o not in before]
    for o in creature:
        if o.type == "ARMATURE":
            o.data.pose_position = "REST"
    bpy.context.view_layer.update()
    # on its chest: the badge high on its left breast (its left is Blender -X), the plate across its right, both above
    # the tear (its left front, z 3.14 .. 3.46)
    cb = on_chest(b, "ChestBadge", surface_frame(3.62, math.pi * 0.5 + 0.5, 0.016, creature), 0.12, 1.6)
    cp = on_chest(plate, "ChestPlate", surface_frame(3.56, math.pi * 0.5 - 0.42, 0.02, creature), -0.1, 1.35)
    obs = [b, plate, cb, cp, ash]
    for o in creature:
        o.hide_render = True
    export(obs, "wendigo_story")
    # previews: each alone, then the two on the creature
    for o in (b, plate, ash):
        for q in obs:
            q.hide_render = q is not o
        d = max(o.dimensions)
        c = sum((o.matrix_world @ V(bb) for bb in o.bound_box), V()) / 8
        if o is ash:
            preview(f"wendigo_story_{o.name}", tuple(c), d * 1.3, height=d * 0.8, yaw=20, size=(700, 500))
        else:
            preview(f"wendigo_story_{o.name}", tuple(c), d * 1.6, height=0.0, yaw=180, size=(700, 500))
    for q in obs:
        q.hide_render = q not in (cb, cp)
    for o in creature:
        o.hide_render = False
    preview("wendigo_story_chest", (0, 0.3, 3.5), 1.6, height=0.1, yaw=0, size=(800, 800))
    preview("wendigo_story_chest_far", (0, 0.3, 3.0), 4.0, height=0.5, yaw=25, size=(800, 800))


# ------------------------------------------------------------------ the figure in the prints

PRINTS = os.path.join(ROOT, "assets", "textures", "stalker_prints")
# (the clip, how far into it (0 .. 1), the view's turn round it in degrees): a few ways it stands, so no two prints are
# the same
PRINT_POSES = [("idle", 0.0, 0.0), ("stare", 0.0, 14.0), ("idle", 0.35, -20.0), ("idle", 0.7, 30.0), ("stare", 0.2, -34.0)]


def _render(path, w, h):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path, check_existing=False)
    import numpy as np
    px = np.array(img.pixels[:], np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    return px


def stalker_prints():
    """The black figure the prints develop with after the reveal (STORY.md Act 25, "the photos after"): the real stalker
    (stalker.glb) rendered from the front in a few of its poses, flat and orthographic, into small RGBA cut-outs:
    alpha its silhouette, red how pale its face is there (its vertex colours' r). The game darkens a print through the
    alpha and lifts the face a little through the red (PhotoLog.StalkerInPrint)."""
    import numpy as np
    clear()
    os.makedirs(PRINTS, exist_ok=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(ROOT, "assets", "models", "stalker", "stalker.glb"))
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    print("[wendigo_story] clips:", [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.view_settings.view_transform = "Standard"
    sh = sc.display.shading
    sh.light = "FLAT"
    sh.show_shadows = False
    sh.show_cavity = False
    sh.show_object_outline = False
    sh.show_specular_highlight = False
    cam = bpy.data.objects.new("PrintCam", bpy.data.cameras.new("PrintCam"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.9
    sc.camera = cam
    W_, H_ = 112, 224
    tmp = os.path.join(ROOT, "build", "blender")
    os.makedirs(tmp, exist_ok=True)
    for k, (clip, at, yaw) in enumerate(PRINT_POSES):
        if arm is not None:
            act = next((a for a in bpy.data.actions if a.name == clip or a.name.endswith("|" + clip) or a.name.startswith(clip)), None)
            if arm.animation_data is None:
                arm.animation_data_create()
            if act is not None:
                arm.animation_data.action = act
                f0, f1 = act.frame_range
                sc.frame_set(int(round(f0 + (f1 - f0) * at)))
            else:
                print(f"[wendigo_story] no clip '{clip}': its rest pose")
        bpy.context.view_layer.update()
        a = math.radians(yaw)
        # (it faces Blender's +Y: the view from in front of it, turned round it by yaw)
        cam.location = V((math.sin(a) * 10, math.cos(a) * 10, 1.25))
        cam.rotation_euler = (V((0, 0, 1.25)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        sh.color_type = "SINGLE"
        sh.single_color = (0, 0, 0)
        sil = _render(os.path.join(tmp, f"_print_sil_{k}.png"), W_, H_)
        sh.color_type = "VERTEX"
        col = _render(os.path.join(tmp, f"_print_col_{k}.png"), W_, H_)
        alpha = sil[:, :, 3]
        face = np.clip(col[:, :, 0], 0, 1) * alpha
        out = np.zeros((H_, W_, 4), np.float32)
        out[:, :, 0] = face
        out[:, :, 3] = alpha
        img = bpy.data.images.new(f"stalker_print_{k}", W_, H_, alpha=True)
        img.pixels = out.ravel()
        img.filepath_raw = os.path.join(PRINTS, f"stalker_print_{k + 1:02}.png")
        img.file_format = "PNG"
        img.save()
        print(f"[wendigo_story] stalker print {k + 1}: {clip} {at} {yaw} deg, cover {alpha.mean():.3f}, face {face.max():.2f}")


if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["story", "prints"]
    if "story" in want:
        build()
    if "prints" in want:
        stalker_prints()
