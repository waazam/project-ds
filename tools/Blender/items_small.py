"""The small pickups and key items, modelled in Blender (2026-10-15, the owner: every model is made in Blender). Run headless:

    blender -b --python tools/Blender/items_small.py [-- group ...]      (groups: items, station, friend)

- items   -> assets/models/items/small_items.glb: Key, FlatStone, ChoppingStump, Bookmark, Keycard201/202/203, PantryKey,
             DiningKey, FontKey, Chalice, Compass (ItemMeshes builds them from here; its code stays the fallback).
- station -> assets/models/items/station_items.glb: DeadEyeStump, DeadEyeFilm, PaleHand, StairTread (Act 13's pieces).
- friend  -> assets/models/items/friend.glb: FriendChair, FriendTable, FriendBandage, FriendFloorBandage (the Act 5 cabin).

Every item is modelled in the same local frame the old code used (before its own placing transform, which the game still
applies), in the game's axes: helpers B()/S() turn a game point/size (Y up, -Z forward) into Blender's (Z up), so the
export's Y-up turn brings it back exactly.

Materials are named after the game's own ("brass", "bark", "linen" ...); the game paints each surface with its material,
and a "#rrggbb" suffix is the tint the old code gave that part (its vertex colour, linear), applied as the material's
albedo. The colours here are only Blender's preview.
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "items")
os.makedirs(kit.OUT, exist_ok=True)

PREVIEW_COL = {
    "brass": (0.78, 0.6, 0.28), "chrome": (0.75, 0.76, 0.78), "paper": (0.86, 0.8, 0.66), "edge": (0.9, 0.9, 0.9),
    "dial": (0.92, 0.9, 0.82), "glass": (0.7, 0.8, 0.85), "needle_red": (0.7, 0.12, 0.1), "rock": (0.5, 0.5, 0.48),
    "bark": (0.32, 0.24, 0.17), "endgrain": (0.85, 0.72, 0.52), "porcelain": (0.88, 0.85, 0.78), "gold": (0.85, 0.66, 0.25),
    "velvet_violet": (0.35, 0.15, 0.4), "mustard": (0.75, 0.6, 0.2), "leather": (0.35, 0.2, 0.12), "linen": (0.85, 0.82, 0.74),
    "lodge_brass": (0.8, 0.62, 0.3), "steel_dark": (0.22, 0.21, 0.2), "meat": (0.5, 0.25, 0.25), "skin_pale": (0.8, 0.75, 0.68),
    "skin_cut": (0.45, 0.08, 0.07), "deck": (0.55, 0.52, 0.48), "carve": (0.18, 0.15, 0.12), "wood": (0.45, 0.33, 0.22),
    "gauze": (0.8, 0.78, 0.72), "film": (0.8, 0.8, 0.72),
}


def M(base, tint=None, rough=0.6, metal=0.0):
    """The game material `base`, tinted by `tint` (linear rgb, the old code's vertex colour)."""
    name = base if tint is None else base + "#%02x%02x%02x" % tuple(max(0, min(255, round(c * 255))) for c in tint)
    col = PREVIEW_COL.get(base, (0.6, 0.6, 0.6))
    if tint is not None:
        col = tuple(a * b for a, b in zip(col, tint)) if base not in ("brass", "lodge_brass") else tuple(tint)
    return mat(name, col, rough, metal)


def B(p):
    x, y, z = p
    return Vector((x, -z, y))


def S(s):
    x, y, z = s
    return (x, z, y)


def BA(axis):
    """A game axis as Blender's (the mapping is a proper rotation, so angles carry over unchanged)."""
    return B(axis)


def apply(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def rot(ob, axis_game, angle, pivot_game=(0, 0, 0)):
    apply(ob)
    pv = B(pivot_game)
    ob.data.transform(Matrix.Translation(pv) @ Matrix.Rotation(angle, 4, BA(axis_game)) @ Matrix.Translation(-pv))
    return ob


def smooth(ob, angle=40):
    for p in ob.data.polygons:
        p.use_smooth = True
    if hasattr(ob.data, "use_auto_smooth"):
        ob.data.use_auto_smooth = True
        ob.data.auto_smooth_angle = math.radians(angle)
    return ob


def uv(ob, scale):
    uv_box(ob, scale)
    return ob


def gbox(name, c, s, m, bevel=0.0, segs=2, uvs=20.0):
    ob = box(name, B(c), S(s), m, bevel, segs)
    apply(ob)
    return uv(smooth(ob, 35), uvs)


def gcyl(name, a, b, r0, r1=None, sides=16, m=None, caps=True, uvs=20.0, bevel=0.0):
    ob = cylinder(name, B(a), B(b), r0, r1, sides, m, caps)
    if bevel > 0 and caps:
        add_mod(ob, "BEVEL", width=bevel, segments=2, limit_method="ANGLE")
        apply_mods(ob)
    return uv(smooth(ob, 50), uvs)


def gtorus(name, c, R, r, m, axis_game=(0, 1, 0), major=28, minor=10, flatten=1.0, uvs=20.0):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=major, minor_segments=minor)
    ob = bpy.context.active_object
    ob.name = name
    if flatten != 1.0:
        ob.scale = (1, 1, flatten)
    apply(ob)
    a = BA(axis_game).normalized()
    if (a - Vector((0, 0, 1))).length > 1e-4:
        ob.data.transform(Vector((0, 0, 1)).rotation_difference(a).to_matrix().to_4x4())
    ob.data.transform(Matrix.Translation(B(c)))
    if m:
        ob.data.materials.append(m)
    return uv(smooth(ob, 60), uvs)


def glathe(name, prof, m, sides=32, cap_bottom=True, cap_top=False, centre=(0, 0, 0), uvs=20.0, noise=0.0, seed=0):
    """A turned shape round the game's up axis: prof = [(radius, height), ...] bottom to top."""
    bm = bmesh.new()
    rings = []
    rnd = random.Random(seed)
    lob = [rnd.uniform(0, math.tau) for _ in range(3)]
    for (r, h) in prof:
        ring = []
        for i in range(sides):
            a = math.tau * i / sides
            rr = r
            if noise:
                rr *= 1 + noise * (0.5 * math.sin(3 * a + lob[0]) + 0.3 * math.sin(5 * a + lob[1]) + 0.2 * math.sin(7 * a + lob[2] + h * 9))
            ring.append(bm.verts.new(B((centre[0] + math.cos(a) * rr, centre[1] + h, centre[2] + math.sin(a) * rr))))
        rings.append(ring)
    for j in range(len(rings) - 1):
        for i in range(sides):
            bm.faces.new((rings[j][i], rings[j][(i + 1) % sides], rings[j + 1][(i + 1) % sides], rings[j + 1][i]))
    if cap_bottom and prof[0][0] > 1e-5:
        bm.faces.new(list(reversed(rings[0])))
    if cap_top and prof[-1][0] > 1e-5:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name, m)
    return uv(smooth(ob, 50), uvs)


def gdisc(name, c, r, m, sides=40, thick=0.0005):
    """A flat round card facing up, its UVs the whole texture across it (a dial)."""
    bm = bmesh.new()
    centre = bm.verts.new(B(c))
    ring = [bm.verts.new(B((c[0] + math.cos(math.tau * i / sides) * r, c[1], c[2] + math.sin(math.tau * i / sides) * r))) for i in range(sides)]
    for i in range(sides):
        bm.faces.new((centre, ring[i], ring[(i + 1) % sides]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    lay = bm.loops.layers.uv.new()
    cb = B(c)
    for f in bm.faces:
        for lp in f.loops:
            d = lp.vert.co - cb
            lp[lay].uv = (0.5 + 0.5 * d.x / r, 0.5 + 0.5 * d.y / r)
    return obj_from_bmesh(bm, name, m)


def finish(parts, name):
    ob = join(parts, name)
    smooth(ob)
    return ob


# ------------------------------------------------------------------ the items (ItemMeshes)


def key():
    brass = M("brass", (0.95, 0.8, 0.5), 0.35, 0.8)
    p = []
    # the bow: a flat ring with a real hole, its edges rounded (a torus pressed flat)
    p.append(gtorus("bow", (-0.052, 0, 0), 0.021, 0.0062, brass, flatten=0.58, major=32, minor=10))
    p.append(gbox("collar", (-0.021, 0, 0), (0.01, 0.011, 0.013), brass, bevel=0.0015))
    p.append(gcyl("shaft", (-0.026, 0, 0), (0.078, 0, 0), 0.0048, 0.0045, 14, brass, bevel=0.001))
    p.append(gbox("bit", (0.062, 0, 0.013), (0.026, 0.007, 0.02), brass, bevel=0.0012))
    p.append(gbox("tooth1", (0.054, 0, 0.027), (0.007, 0.007, 0.01), brass, bevel=0.001))
    p.append(gbox("tooth2", (0.069, 0, 0.027), (0.007, 0.007, 0.01), brass, bevel=0.001))
    p.append(gtorus("ring", (-0.083, 0.001, 0.004), 0.02, 0.0022, M("chrome", (0.7, 0.72, 0.74), 0.2, 1.0), major=36, minor=8))
    tag = gbox("tag", (0, 0, 0), (0.07, 0.003, 0.04), M("paper", (0.86, 0.62, 0.34)), bevel=0.0008)
    rot(tag, (0, 1, 0), -0.35)
    tag.data.transform(Matrix.Translation(B((-0.135, -0.001, 0.02))))
    p.append(tag)
    # a punched hole in the tag (a dark eyelet) where the ring goes through
    p.append(gtorus("eyelet", (-0.106, 0.0018, 0.012), 0.004, 0.0012, M("brass", (0.6, 0.5, 0.35), 0.4, 0.8), major=16, minor=6))
    return finish(p, "Key")


def flat_stone():
    ob = lumpy_sphere("FlatStone", B((0, -0.012, 0)), (0.26, 0.2, 0.082), M("rock", (0.86, 0.86, 0.82)), 31, amp=0.05, freq=2.4, subdiv=4, flat_bottom=-0.05)
    return uv(smooth(ob, 70), 3.0)


def chopping_stump():
    bark = M("bark", (0.72, 0.66, 0.6))
    prof = [(0.29, -0.1), (0.27, -0.04), (0.25, 0.03), (0.225, 0.09), (0.213, 0.12), (0.208, 0.25), (0.205, 0.38), (0.203, 0.42)]
    trunk = glathe("trunk", prof, bark, sides=28, cap_bottom=False, cap_top=True, uvs=2.5, noise=0.06, seed=4)
    # the fresh-cut top: pale end grain on the trunk's own top face (so it meets the lobed bark with no seam)
    trunk.data.materials.append(M("endgrain", (0.95, 0.85, 0.68)))
    top_face = max(trunk.data.polygons, key=lambda f: f.center.z)
    top_face.material_index = 1
    uvl = trunk.data.uv_layers.active.data
    for li in top_face.loop_indices:
        v = trunk.data.vertices[trunk.data.loops[li].vertex_index].co
        uvl[li].uv = (0.5 + v.x / 0.42, 0.5 + v.y / 0.42)
    chip = gbox("chip", (0.26, 0.015, 0.12), (0.09, 0.025, 0.04), M("endgrain", (0.9, 0.78, 0.6)), bevel=0.004)
    rot(chip, (0, 1, 0), 0.6, (0.26, 0.015, 0.12))
    scar = gbox("scar", (0.19, 0.3, 0.06), (0.05, 0.08, 0.02), M("endgrain", (0.88, 0.76, 0.6)), bevel=0.004)
    rot(scar, (0, 1, 0), 0.32, (0.19, 0.3, 0.06))
    return finish([trunk, chip, scar], "ChoppingStump")


def bookmark():
    leather = M("brass", (0.55, 0.06, 0.08), 0.7, 0.1)
    gold = M("brass", (0.9, 0.72, 0.3), 0.35, 0.8)
    p = [gbox("strip", (0, 0, 0), (0.035, 0.003, 0.16), leather, bevel=0.001),
         gbox("inlay", (0, 0.0018, 0), (0.028, 0.0006, 0.15), gold, bevel=0.0002)]
    # its cut end: a swallowtail notch
    p.append(gcyl("cord", (0, 0.004, 0.08), (0, 0.004, 0.11), 0.004, 0.009, 12, gold))
    p.append(gtorus("knot", (0, 0.004, 0.08), 0.004, 0.002, gold, axis_game=(0, 0, 1), major=14, minor=6))
    return finish(p, "Bookmark")


def keycard(room):
    card = gbox("card", (0, 0, 0), (0.086, 0.0015, 0.054), M("porcelain", (0.86, 0.82, 0.72), 0.4), bevel=0.003, segs=3)
    crest = gbox("crest", (-0.02, 0.0009, -0.005), (0.03, 0.0004, 0.024), M("gold", None, 0.3, 0.9), bevel=0.0015)
    z = {"202": -0.018, "203": 0.0, "201": 0.018}[room]
    band = gbox("band", (0.025, 0.0009, z), (0.024, 0.0004, 0.012), M("velvet_violet" if room == "201" else "mustard"), bevel=0.0008)
    stripe = gbox("stripe", (0, -0.0009, 0.012), (0.086, 0.0004, 0.012), M("leather"))
    return finish([card, crest, band, stripe], "Keycard" + room)


def pantry_key():
    linen = M("linen", (0.86, 0.82, 0.72))
    env = gbox("envelope", (0, 0, 0), (0.1, 0.012, 0.06), linen, bevel=0.003)
    flap = gbox("flap", (0, 0, 0), (0.1, 0.003, 0.04), linen, bevel=0.001)
    rot(flap, (1, 0, 0), 0.12)
    flap.data.transform(Matrix.Translation(B((0, 0.007, -0.01))))
    brass = M("lodge_brass", None, 0.35, 0.8)
    bow = gtorus("bow", (-0.064, 0, 0.01), 0.011, 0.0035, brass, axis_game=(0, 1, 0), major=24, minor=8)
    shank = gcyl("shank", (-0.054, 0, 0.01), (-0.03, 0, 0.01), 0.0035, 0.0032, 12, brass)
    return finish([env, flap, bow, shank], "PantryKey")


def dining_key():
    brass = M("lodge_brass", None, 0.35, 0.8)
    p = [gtorus("ring", (0, 0, -0.1), 0.022, 0.0042, brass, major=36, minor=10),
         gcyl("shank", (0, 0, -0.078), (0, 0, 0.07), 0.006, 0.0055, 16, brass, bevel=0.0012),
         gcyl("collar", (0, 0, -0.074), (0, 0, -0.066), 0.0085, 0.0085, 16, brass, bevel=0.0015),
         gbox("bit", (0.012, 0, 0.06), (0.02, 0.006, 0.02), brass, bevel=0.0012),
         gbox("ward", (0.018, 0, 0.051), (0.008, 0.006, 0.004), brass, bevel=0.0008)]
    p.append(gbox("tag", (0.05, -0.002, -0.14), (0.05, 0.002, 0.03), M("linen"), bevel=0.0008))
    p.append(gcyl("string", (0.022, 0, -0.1), (0.03, -0.001, -0.135), 0.0008, 0.0008, 6, M("linen", (0.6, 0.55, 0.48)), caps=False))
    return finish(p, "DiningKey")


def font_key():
    iron = M("steel_dark", (0.22, 0.21, 0.2), 0.7, 0.6)
    p = []
    for i in range(3):
        a = math.tau * i / 3 + math.pi * 0.5
        p.append(gtorus(f"lobe{i}", (math.cos(a) * 0.017, 0, -0.1 + math.sin(a) * 0.017), 0.014, 0.0028, iron, major=28, minor=8))
    p.append(gcyl("shank", (0, 0, -0.078), (0, 0, 0.09), 0.0065, 0.0055, 16, iron, bevel=0.0012))
    p.append(gcyl("collar", (0, 0, -0.07), (0, 0, -0.062), 0.01, 0.01, 16, iron, bevel=0.0018))
    p.append(gbox("bit", (0.016, 0, 0.078), (0.026, 0.006, 0.022), iron, bevel=0.0012))
    p.append(gbox("tooth1", (0.024, 0, 0.07), (0.01, 0.006, 0.006), iron, bevel=0.0008))
    p.append(gbox("tooth2", (0.024, 0, 0.086), (0.01, 0.006, 0.006), iron, bevel=0.0008))
    return finish(p, "FontKey")


def chalice():
    silver = M("brass", (0.8, 0.82, 0.86), 0.25, 0.9)
    dark = M("brass", (0.3, 0.3, 0.32), 0.5, 0.6)
    outer = [(0.055, 0.0), (0.056, 0.004), (0.053, 0.011), (0.046, 0.014), (0.03, 0.022), (0.016, 0.034), (0.012, 0.045),
             (0.011, 0.058), (0.02, 0.066), (0.022, 0.071), (0.017, 0.078), (0.011, 0.086), (0.011, 0.097), (0.016, 0.106),
             (0.03, 0.118), (0.039, 0.132), (0.045, 0.155), (0.049, 0.178), (0.0505, 0.185)]
    shell = glathe("shell", outer, silver, sides=40, cap_bottom=True, cap_top=False, uvs=12)
    lip = gtorus("lip", (0, 0.185, 0), 0.0475, 0.0028, silver, major=40, minor=8)
    inner = [(0.046, 0.184), (0.044, 0.165), (0.038, 0.145), (0.028, 0.13), (0.012, 0.122), (0.0, 0.12)]
    inside = glathe("inside", inner, dark, sides=40, cap_bottom=False, cap_top=False, uvs=12)
    for f in inside.data.polygons:   # (its faces look into the bowl)
        f.flip()
    return finish([shell, lip, inside], "Chalice")


def compass():
    brass = M("brass", (0.95, 0.78, 0.48), 0.35, 0.8)
    case = glathe("case", [(0.044, 0.0), (0.049, 0.002), (0.05, 0.006), (0.05, 0.016), (0.048, 0.02), (0.044, 0.021), (0.041, 0.021),
                           (0.041, 0.012)], brass, sides=40, cap_bottom=True, cap_top=False, uvs=8)
    hinge = (0, 0.019, -0.05)
    lid = gcyl("lid", (0, 0, 0.05), (0, 0.006, 0.05), 0.05, 0.048, 40, brass, bevel=0.0012, uvs=8)
    face = gdisc("lidface", (0, -0.0008, 0.05), 0.04, M("edge"), sides=36)
    for f in face.data.polygons:
        f.flip()
    for o in (lid, face):
        o.data.transform(Matrix.Translation(B(hinge)))
        rot(o, (1, 0, 0), math.radians(-105), hinge)
    knuckle = gcyl("knuckle", (-0.012, 0.019, -0.05), (0.012, 0.019, -0.05), 0.004, 0.004, 14, M("brass", (0.9, 0.74, 0.45), 0.35, 0.8))
    thumb = gtorus("thumbring", (0, 0.012, 0.061), 0.011, 0.0022, M("brass", (0.9, 0.74, 0.45), 0.35, 0.8), major=24, minor=8)
    dial = gdisc("dial", (0, 0.0125, 0), 0.041, M("dial"), sides=40)
    # the needle: a slim diamond, red north half and white south, on a jewelled pin
    ny = 0.0145
    bm = bmesh.new()
    def needle(tipz, nm):
        bmn = bmesh.new()
        vs = [bmn.verts.new(B(v)) for v in ((-0.0035, ny, 0), (0.0035, ny, 0), (0, ny + 0.0012, 0), (0, ny, tipz))]
        bmn.faces.new((vs[0], vs[2], vs[3]))
        bmn.faces.new((vs[2], vs[1], vs[3]))
        ob = obj_from_bmesh(bmn, nm, None, smooth=False)
        return ob
    north = needle(-0.033, "north")
    north.data.materials.append(M("needle_red", (0.72, 0.14, 0.1)))
    south = needle(0.033, "south")
    south.data.materials.append(M("needle_red", (0.85, 0.85, 0.82)))
    pin = gcyl("pin", (0, 0.0125, 0), (0, 0.0162, 0), 0.0018, 0.0012, 10, brass)
    glass = gdisc("glass", (0, 0.0185, 0), 0.0415, M("glass"), sides=40)
    return finish([case, lid, face, knuckle, thumb, dial, north, south, pin, glass], "Compass")


# ------------------------------------------------------------------ Act 13's pieces (StationProps)


def dead_eye_stump(r=0.13):
    """The torn plug of tentacle behind the eye (modelled for a 0.13 m ball; the game scales it to its own)."""
    meat = M("meat", (0.55, 0.42, 0.44), 0.45)
    verts = [B((0, 0, r * 0.35)), B((0.008, -0.012, r * 1.1)), B((0.02, -0.03, r * 1.9)), B((0.03, -0.035, r * 2.3))]
    ob = skin_tree("DeadEyeStump", verts, [(0, 1), (1, 2), (2, 3)], [r * 0.78, r * 0.68, r * 0.58, r * 0.5], meat, subdiv=2)
    # torn: the far end ragged, a few strands
    displace(ob, 0.018, 0.04, 3)
    return uv(smooth(ob, 70), 6)


def dead_eye_film():
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=1.12)
    ob = bpy.context.active_object
    ob.name = "DeadEyeFilm"
    ob.data.materials.append(M("film"))
    return smooth(ob, 80)


def displace(ob, strength, size, seed=0):
    tex = bpy.data.textures.new(f"n{seed}", "CLOUDS")
    tex.noise_scale = size
    m = add_mod(ob, "DISPLACE", strength=strength, mid_level=0.5)
    m.texture = tex
    m.texture_coords = "GLOBAL"
    apply_mods(ob)


def pale_hand():
    """A severed hand, palm up, fingers half curled: one skin (skinned bones, remeshed), the cut a dark wet face."""
    skin = M("skin_pale")
    V, E, R = [], [], []

    def add(p, r, parent=None):
        V.append(B(p))
        R.append(r)
        i = len(V) - 1
        if parent is not None:
            E.append((parent, i))
        return i

    wrist = add((0, 0, 0.11), 0.03)
    w2 = add((0, 0.001, 0.07), 0.031, wrist)
    palm = add((0, 0.003, 0.02), 0.042, w2)
    knuckles = add((0, 0.006, -0.035), 0.04, palm)
    for f in range(4):
        x = -0.034 + f * 0.023
        bend = 0.006 * (f % 2)
        base = add((x, 0.006, -0.045), 0.0095, knuckles)
        mid = add((x * 1.05, 0.02, -0.08 + bend), 0.0085, base)
        tip = add((x * 1.08, 0.042, -0.098 + bend), 0.0065, mid)
        add((x * 1.08, 0.052, -0.094 + bend), 0.0055, tip)
    t0 = add((0.04, 0.0, 0.02), 0.012, palm)
    t1 = add((0.062, 0.012, -0.008), 0.0105, t0)
    t2 = add((0.075, 0.022, -0.032), 0.009, t1)
    add((0.078, 0.028, -0.045), 0.0075, t2)
    hand = skin_tree("hand", V, E, [(r * 1.0, r * 0.75) for r in R], skin, subdiv=2)
    # the palm flatter and wider than a tube
    for v in hand.data.vertices:
        if -0.05 < -v.co.y < 0.06:
            v.co.z = 0.004 + (v.co.z - 0.004) * 0.72
    uv(smooth(hand, 80), 10)
    cut = gcyl("cut", (0, 0, 0.108), (0, 0, 0.113), 0.03, 0.03, 24, M("skin_cut"), uvs=10)
    bone = gcyl("bone", (0.008, 0.002, 0.1), (0.008, 0.002, 0.1145), 0.006, 0.006, 12, M("skin_pale", (0.95, 0.92, 0.82)))
    return finish([hand, cut, bone], "PaleHand")


def stair_tread():
    deck = M("deck", (0.55, 0.52, 0.48))
    board = gbox("board", (0, 0, 0.012), (0.36, 0.045, 0.136), deck, bevel=0.004, uvs=6)
    nosing = gcyl("nosing", (-0.18, 0, -0.068), (0.18, 0, -0.068), 0.0225, 0.0225, 24, deck, uvs=6, bevel=0.003)
    carve = M("carve")
    # the carved eye: an almond groove, a pupil, sunk into the top face
    p = [board, nosing]
    pts = []
    for i in range(24):
        a = math.tau * i / 24
        pts.append((math.cos(a) * 0.06, 0.0232, math.sin(a) * 0.025 * (1 - 0.35 * abs(math.cos(a)))))
    for i in range(24):
        p.append(gcyl(f"g{i}", pts[i], pts[(i + 1) % 24], 0.0028, 0.0028, 6, carve, caps=False))
    p.append(gcyl("pupil", (0, 0.0222, 0), (0, 0.0236, 0), 0.012, 0.012, 20, carve))
    return finish(p, "StairTread")


# ------------------------------------------------------------------ the Act 5 cabin (FriendBody)

TABLE_TOP = 0.74


def friend_chair():
    wood = M("wood", (0.4, 0.31, 0.22))
    seat_y, seat_t = 0.45, 0.032
    p = [gbox("seat", (0, seat_y - seat_t * 0.5, 0.005), (0.43, seat_t, 0.41), wood, bevel=0.01, segs=3, uvs=1.5),
         gbox("rails", (0, seat_y - seat_t - 0.025, 0.005), (0.37, 0.05, 0.35), wood, bevel=0.005, uvs=1.5)]
    leg = [(0.017, 0.0), (0.019, 0.02), (0.016, 0.14), (0.021, 0.17), (0.021, 0.19), (0.017, 0.21), (0.02, 0.33), (0.022, 0.36)]
    for x in (-1, 1):
        top = Vector((x * 0.175, seat_y - seat_t - 0.05, 0.165))
        foot = Vector((x * 0.195, 0.0, 0.19))
        lg = glathe(f"leg{x}", leg, wood, sides=16, cap_bottom=True, cap_top=True, uvs=1.5)
        up = (top - foot).normalized()
        rm = Vector((0, 1, 0)).rotation_difference(up).to_matrix().to_4x4()
        lg.data.transform(Matrix.Translation(B(foot)) @ Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1))) @ rm @ Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1))))
        p.append(lg)
        p.append(gbox(f"block{x}", tuple(top + Vector((0, 0.025, 0))), (0.036, 0.05, 0.036), wood, bevel=0.004, uvs=1.5))
        pts = [(x * 0.185, 0.0, -0.2), (x * 0.18, seat_y - 0.03, -0.185), (x * 0.185, 0.7, -0.215), (x * 0.19, 0.93, -0.26)]
        post = skin_tree(f"post{x}", [B(q) for q in pts], [(0, 1), (1, 2), (2, 3)], [(0.019, 0.019), (0.02, 0.02), (0.02, 0.02), (0.016, 0.016)], wood, subdiv=1)
        p.append(uv(smooth(post, 60), 1.5))
        p.append(gcyl(f"finial{x}", pts[3], (pts[3][0], pts[3][1] + 0.025, pts[3][2] - 0.004), 0.016, 0.009, 12, wood, uvs=1.5))
    # the crest rail, curved back between the posts
    crest = [B((a * 0.185, 0.875 + 0.006 * (1 - a * a), -0.245 - 0.03 * (1 - a * a))) for a in [-1 + i / 4 for i in range(9)]]
    rail = skin_tree("crest", crest, [(i, i + 1) for i in range(8)], [(0.014, 0.037)] * 9, wood, subdiv=1)
    p.append(uv(smooth(rail, 50), 1.5))
    for i in (-1, 0, 1):
        p.append(gcyl(f"spindle{i}", (i * 0.075, seat_y, -0.175), (i * 0.078, 0.84, -0.266 + 0.025 * abs(i)), 0.011, 0.009, 10, wood, uvs=1.5))
    for x in (-1, 1):
        p.append(gcyl(f"stretch{x}", (x * 0.186, 0.17, 0.178), (x * 0.184, 0.17, -0.196), 0.011, 0.011, 10, wood, uvs=1.5))
    p.append(gcyl("stretchX", (-0.185, 0.2, -0.01), (0.185, 0.2, -0.01), 0.012, 0.012, 10, wood, uvs=1.5))
    return finish(p, "FriendChair")


def friend_table():
    top_t, apron = 0.03, 0.095
    p = []
    for s in (-1, 1):
        tint = (0.5, 0.39, 0.27) if s < 0 else (0.465, 0.363, 0.251)
        p.append(gbox(f"board{s}", (0, TABLE_TOP - top_t * 0.5, s * 0.1755), (1.1, top_t, 0.347), M("wood", tint), bevel=0.006, segs=3, uvs=1.2))
    dark = M("wood", (0.38, 0.29, 0.2))
    ay = TABLE_TOP - top_t - apron * 0.5
    p.append(gbox("apronF", (0, ay, 0.27), (0.88, apron, 0.022), dark, bevel=0.003, uvs=1.2))
    p.append(gbox("apronB", (0, ay, -0.27), (0.88, apron, 0.022), dark, bevel=0.003, uvs=1.2))
    p.append(gbox("apronR", (0.45, ay, 0), (0.022, apron, 0.5), dark, bevel=0.003, uvs=1.2))
    p.append(gbox("apronL", (-0.45, ay, 0), (0.022, apron, 0.5), dark, bevel=0.003, uvs=1.2))
    block_lo = TABLE_TOP - top_t - apron - 0.02
    turned = [(0.02, 0.0), (0.023, 0.012), (0.02, 0.03), (0.017, 0.05), (0.021, 0.35), (0.024, 0.47), (0.029, 0.5), (0.024, 0.53),
              (0.026, block_lo - 0.01), (0.03, block_lo)]
    for x in (-1, 1):
        for z in (-1, 1):
            p.append(glathe(f"leg{x}{z}", turned, dark, sides=16, cap_bottom=True, centre=(x * 0.47, 0, z * 0.29), uvs=1.2))
            h = TABLE_TOP - top_t - block_lo
            p.append(gbox(f"block{x}{z}", (x * 0.47, block_lo + h * 0.5, z * 0.29), (0.056, h, 0.056), dark, bevel=0.005, uvs=1.2))
    return finish(p, "FriendTable")


def ribbon(name, pts, width, m, lift=0.0):
    """A thin strip of cloth lying along points (game space), a little thickness so it catches the light."""
    bm = bmesh.new()
    left, right = [], []
    for i, q in enumerate(pts):
        q = Vector(q)
        d = (Vector(pts[min(i + 1, len(pts) - 1)]) - Vector(pts[max(i - 1, 0)]))
        d.y = 0
        d.normalize()
        side = Vector((-d.z, 0, d.x)) * width * 0.5
        left.append(bm.verts.new(B(q - side)))
        right.append(bm.verts.new(B(q + side)))
    for i in range(len(pts) - 1):
        bm.faces.new((left[i], right[i], right[i + 1], left[i + 1]))
    ob = obj_from_bmesh(bm, name, m)
    add_mod(ob, "SUBSURF", levels=2)
    add_mod(ob, "SOLIDIFY", thickness=0.0015, offset=1.0)
    apply_mods(ob)
    return uv(smooth(ob, 80), 6)


def friend_bandage():
    gauze = M("gauze", (0.78, 0.76, 0.7))
    roll = Vector((-0.36, TABLE_TOP + 0.034, 0.12))
    p = [gcyl("roll", tuple(roll + Vector((-0.026, 0, 0))), tuple(roll + Vector((0.026, 0, 0))), 0.034, 0.034, 28, gauze, uvs=6, bevel=0.003),
         gcyl("core", tuple(roll + Vector((-0.0265, 0, 0))), tuple(roll + Vector((0.0265, 0, 0))), 0.012, 0.012, 16, M("gauze", (0.55, 0.53, 0.48)), uvs=6)]
    p.append(ribbon("tail", [tuple(roll + Vector((0, -0.03, 0.03))), (-0.35, TABLE_TOP + 0.002, 0.2), (-0.3, TABLE_TOP + 0.002, 0.27), (-0.22, TABLE_TOP + 0.002, 0.29)], 0.05, gauze))
    p.append(ribbon("strip", [(0.12, TABLE_TOP + 0.002, -0.05), (0.19, TABLE_TOP + 0.003, 0.0), (0.25, TABLE_TOP + 0.003, -0.02), (0.31, TABLE_TOP + 0.002, 0.04)], 0.045, M("gauze", (0.62, 0.5, 0.45))))
    p.append(ribbon("bloody", [(0.3, TABLE_TOP + 0.003, -0.16), (0.36, TABLE_TOP + 0.003, -0.12), (0.39, TABLE_TOP + 0.002, -0.06)], 0.04, M("gauze", (0.17, 0.055, 0.045))))
    return finish(p, "FriendBandage")


def friend_floor_bandage():
    a = ribbon("floor", [(0.38, 0.006, -0.1), (0.45, 0.007, 0.02), (0.43, 0.008, 0.16), (0.5, 0.007, 0.27)], 0.045, M("gauze", (0.66, 0.63, 0.57)))
    b = ribbon("floorEnd", [(0.5, 0.0075, 0.27), (0.53, 0.0075, 0.305), (0.56, 0.007, 0.34)], 0.045, M("gauze", (0.17, 0.055, 0.045)))
    return finish([a, b], "FriendFloorBandage")


# ------------------------------------------------------------------ build

GROUPS = {
    "items": ("small_items", [key, flat_stone, chopping_stump, bookmark, lambda: keycard("201"), lambda: keycard("202"),
                              lambda: keycard("203"), pantry_key, dining_key, font_key, chalice, compass]),
    "station": ("station_items", [dead_eye_stump, dead_eye_film, pale_hand, stair_tread]),
    "friend": ("friend", [friend_chair, friend_table, friend_bandage, friend_floor_bandage]),
}


def build(group):
    clear()
    fname, makers = GROUPS[group]
    obs = [mk() for mk in makers]
    for o in obs:
        o.location = (0, 0, 0)
    export(obs, fname)
    # a preview of each, framed on its own
    for o in obs:
        for q in obs:
            q.hide_render = q is not o
        d = max(o.dimensions)
        c = sum((o.matrix_world @ Vector(b) for b in o.bound_box), Vector()) / 8
        preview(f"{fname}_{o.name}", tuple(c), d * 1.4 + 0.03, height=d * 0.9, yaw=30, size=(600, 450))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for g in (argv or list(GROUPS)):
        build(g)
