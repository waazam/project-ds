"""R.H.'s key to the bunker, in two halves, and where he hid them (2026-10-15, the owner: the door code became two pieces of a
key found by following blacklight arrows on the trees). Run headless:

    blender -b --python tools/Blender/rh_key.py

-> assets/models/items/rh_key.glb:
- KeyHalfBow: the bow, the collar and the shank as far as a threaded stub.
- KeyHalfBit: the rest of the shank, the bit and its teeth, a knurled sleeve at its cut end that screws over the stub.
- RhKey: the two joined (the sleeve over the stub), as it turns in the door.
- TinBox, TinLid: a hinged tobacco tin, rusted, its lid hinged along the back edge (the lid modelled closed, its hinge at
  the origin's back edge: (0, 0.028, -0.037)).
- HollowStump: a broken, hollow snag about a metre tall, open at its jagged top, a floor of rot inside at 0.52 m (the tin's
  nailed in there: half 1).
- Cairn: a stack of stones, a flat one jutting over a gap at the foot of its front (the tin's tucked under it: half 2).
- Keyhole: the brass escutcheon on the vault door's face, its slot dark (facing the game's +Z).

The halves and the joined key share the house key's frame (ItemMeshes' Key: the bow at -X, the bit toward +X, flat on the
game's XZ), so the hand's key pinch (HeldUse.PinchOf) holds them the same way. The game's axes, as items_small.py: B()/S()
turn a game point/size (Y up, -Z forward) into Blender's, so the export's Y-up turn brings it back exactly. Materials are
named after the game's ("brass", "bark", "rock", "metal", "steel_dark"); ItemModels paints them, a "#rrggbb" suffix the tint.
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
    "brass": (0.78, 0.6, 0.28), "bark": (0.32, 0.24, 0.17), "endgrain": (0.55, 0.42, 0.3), "rock": (0.5, 0.5, 0.48),
    "metal": (0.5, 0.48, 0.44), "steel_dark": (0.22, 0.21, 0.2), "hole": (0.01, 0.01, 0.01), "rust": (0.42, 0.22, 0.12),
}


def M(base, tint=None, rough=0.6, metal=0.0):
    name = base if tint is None else base + "#%02x%02x%02x" % tuple(max(0, min(255, round(c * 255))) for c in tint)
    col = PREVIEW_COL.get(base, (0.6, 0.6, 0.6))
    if tint is not None:
        col = tuple(a * b for a, b in zip(col, tint)) if base != "brass" else tuple(tint)
    return mat(name, col, rough, metal)


def B(p):
    x, y, z = p
    return Vector((x, -z, y))


def S(s):
    x, y, z = s
    return (x, z, y)


def apply(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def rot(ob, axis_game, angle, pivot_game=(0, 0, 0)):
    apply(ob)
    pv = B(pivot_game)
    ob.data.transform(Matrix.Translation(pv) @ Matrix.Rotation(angle, 4, B(axis_game)) @ Matrix.Translation(-pv))
    return ob


def smooth(ob, angle=40):
    for p in ob.data.polygons:
        p.use_smooth = True
    if hasattr(ob.data, "use_auto_smooth"):
        ob.data.use_auto_smooth = True
        ob.data.auto_smooth_angle = math.radians(angle)
    return ob


def gbox(name, c, s, m, bevel=0.0, segs=2, uvs=20.0):
    ob = box(name, B(c), S(s), m, bevel, segs)
    apply(ob)
    uv_box(ob, uvs)
    return smooth(ob, 35)


def gcyl(name, a, b, r0, r1=None, sides=16, m=None, caps=True, uvs=20.0, bevel=0.0):
    ob = cylinder(name, B(a), B(b), r0, r1, sides, m, caps)
    if bevel > 0 and caps:
        add_mod(ob, "BEVEL", width=bevel, segments=2, limit_method="ANGLE")
        apply_mods(ob)
    uv_box(ob, uvs)
    return smooth(ob, 50)


def gtorus(name, c, R, r, m, axis_game=(0, 1, 0), major=28, minor=10, flatten=1.0, uvs=20.0):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=major, minor_segments=minor)
    ob = bpy.context.active_object
    ob.name = name
    if flatten != 1.0:
        ob.scale = (1, 1, flatten)
    apply(ob)
    a = B(axis_game).normalized()
    if (a - Vector((0, 0, 1))).length > 1e-4:
        ob.data.transform(Vector((0, 0, 1)).rotation_difference(a).to_matrix().to_4x4())
    ob.data.transform(Matrix.Translation(B(c)))
    if m:
        ob.data.materials.append(m)
    uv_box(ob, uvs)
    return smooth(ob, 60)


def finish(parts, name):
    ob = join(parts, name)
    return smooth(ob)


def copy(ob, name):
    c = ob.copy()
    c.data = ob.data.copy()
    c.name = name
    bpy.context.scene.collection.objects.link(c)
    return c


# ------------------------------------------------------------------ the key

def key_brass():
    # an old, dull brass, darker than the house key's (the station's key, kept in a tin in the woods)
    return M("brass", (0.78, 0.64, 0.4), 0.45, 0.8)


def half_bow():
    brass = key_brass()
    p = [gtorus("bow", (-0.058, 0, 0), 0.024, 0.0068, brass, flatten=0.58, major=32, minor=10),
         # a stamped number plate across the bow's neck: "3", the station's
         gbox("neck", (-0.03, 0, 0), (0.014, 0.0085, 0.017), brass, bevel=0.0016),
         gcyl("shank", (-0.024, 0, 0), (0.006, 0, 0), 0.0055, 0.0053, 16, brass, bevel=0.001),
         # the threaded stub the other half screws onto: a narrower shank ringed with thread
         gcyl("stub", (0.006, 0, 0), (0.026, 0, 0), 0.0036, 0.0034, 14, brass, bevel=0.0006)]
    for i in range(5):
        x = 0.009 + i * 0.0036
        p.append(gtorus(f"thread{i}", (x, 0, 0), 0.0036, 0.0009, brass, axis_game=(1, 0, 0), major=14, minor=5))
    # a bright, fresh-filed face where it was cut (the stub's end)
    p.append(gcyl("cut", (0.0258, 0, 0), (0.0266, 0, 0), 0.0031, 0.0029, 14, M("brass", (0.95, 0.85, 0.6), 0.25, 0.9)))
    return finish(p, "KeyHalfBow")


def half_bit():
    brass = key_brass()
    p = [gcyl("sleeve", (0.004, 0, 0), (0.03, 0, 0), 0.0072, 0.0068, 18, brass, bevel=0.0012)]
    for i in range(4):   # (knurled: a grip for the fingers to turn it on)
        p.append(gtorus(f"knurl{i}", (0.008 + i * 0.006, 0, 0), 0.0071, 0.0011, brass, axis_game=(1, 0, 0), major=18, minor=5))
    p.append(gcyl("shank", (0.03, 0, 0), (0.088, 0, 0), 0.0052, 0.0048, 16, brass, bevel=0.001))
    p.append(gbox("bit", (0.072, 0, 0.0145), (0.03, 0.0075, 0.023), brass, bevel=0.0012))
    for i, (x, h) in enumerate([(0.061, 0.011), (0.0725, 0.007), (0.083, 0.012)]):
        p.append(gbox(f"tooth{i}", (x, 0, 0.026 + h * 0.5), (0.0065, 0.0075, h), brass, bevel=0.001))
    p.append(gcyl("tip", (0.088, 0, 0), (0.092, 0, 0), 0.0048, 0.0028, 14, brass))
    return finish(p, "KeyHalfBit")


# ------------------------------------------------------------------ the tin

def tin():
    tinm = M("metal", (0.62, 0.6, 0.52), 0.55, 0.7)
    rust = M("rust", None, 0.9, 0.2)
    w, h, d = 0.11, 0.024, 0.07
    base = gbox("base", (0, h * 0.5, 0), (w, h, d), tinm, bevel=0.006, segs=3)
    # rust blooms on its sides
    blooms = []
    rnd = random.Random(7)
    for i in range(5):
        x = rnd.uniform(-w * 0.4, w * 0.4)
        side = rnd.choice([-1, 1])
        blooms.append(gbox(f"rust{i}", (x, rnd.uniform(0.006, 0.016), side * (d * 0.5 + 0.0004)), (rnd.uniform(0.01, 0.025), rnd.uniform(0.006, 0.014), 0.0012), rust, bevel=0.0005))
    # the nail through its back (it's nailed in)
    nail = gcyl("nail", (0, 0.012, -d * 0.5 - 0.03), (0, 0.012, -d * 0.5 + 0.004), 0.0018, 0.0018, 8, M("steel_dark"))
    head = gcyl("nailhead", (0, 0.012, -d * 0.5 + 0.004), (0, 0.012, -d * 0.5 + 0.0065), 0.0045, 0.0045, 10, M("steel_dark"))
    box_ = finish([base] + blooms + [nail, head], "TinBox")
    lid = gbox("lidtop", (0, h + 0.004, 0), (w + 0.003, 0.008, d + 0.003), tinm, bevel=0.004, segs=3)
    emb = gbox("emboss", (0, h + 0.0083, 0.002), (w * 0.62, 0.0012, d * 0.5), M("metal", (0.75, 0.68, 0.5), 0.45, 0.8), bevel=0.0006)
    lr = gbox("lidrust", (0.03, h + 0.0083, -0.018), (0.024, 0.0012, 0.016), rust, bevel=0.0005)
    hinge = gcyl("hinge", (-w * 0.45, h + 0.004, -d * 0.5 - 0.002), (w * 0.45, h + 0.004, -d * 0.5 - 0.002), 0.0028, 0.0028, 10, tinm)
    lid = finish([lid, emb, lr, hinge], "TinLid")
    return box_, lid


# ------------------------------------------------------------------ the hiding places

def ring(bm, r_fn, y, sides, centre=(0, 0)):
    return [bm.verts.new(B((centre[0] + math.cos(math.tau * i / sides) * r_fn(i), y(i) if callable(y) else y, centre[1] + math.sin(math.tau * i / sides) * r_fn(i)))) for i in range(sides)]


def hollow_stump():
    """A broken snag, hollow: the outer bark up to a jagged rim, the rim, the rotten inner wall down to a floor of rot."""
    bark = M("bark", (0.66, 0.6, 0.54))
    rot_ = M("endgrain", (0.5, 0.4, 0.3))
    rnd = random.Random(31)
    sides = 26
    lob = [rnd.uniform(0, math.tau) for _ in range(3)]

    def outer(i, y):
        a = math.tau * i / sides
        flare = 1.0 + 0.45 * max(0.0, 0.35 - y) / 0.45
        return 0.34 * flare * (1 + 0.07 * math.sin(3 * a + lob[0]) + 0.05 * math.sin(5 * a + lob[1]) + 0.03 * math.sin(9 * a + lob[2] + y * 4))
    # the jagged top: a broken edge, higher at the back (the game's -Z), torn low at the front where it's looked into
    top = []
    for i in range(sides):
        a = math.tau * i / sides
        back = 0.5 - 0.5 * math.sin(a)   # (sin(a) = the game's +Z, the front, low)
        top.append(0.84 + 0.3 * back + rnd.uniform(-0.05, 0.05) + (0.07 if i % 5 == 0 else 0.0))
    bm = bmesh.new()
    heights = [-0.12, 0.0, 0.14, 0.32, 0.55]
    rings = [ring(bm, lambda i, y=y: outer(i, y), y, sides) for y in heights]
    rings.append([bm.verts.new(B((math.cos(math.tau * i / sides) * outer(i, top[i]) * 0.97, top[i], math.sin(math.tau * i / sides) * outer(i, top[i]) * 0.97))) for i in range(sides)])
    # the rim (inward), the inner wall down to the floor
    wall = 0.06
    inner_top = [bm.verts.new(B((math.cos(math.tau * i / sides) * (outer(i, top[i]) * 0.97 - wall), top[i] - rnd.uniform(0.01, 0.05), math.sin(math.tau * i / sides) * (outer(i, top[i]) * 0.97 - wall)))) for i in range(sides)]
    floor_y = 0.52
    inner_low = [bm.verts.new(B((math.cos(math.tau * i / sides) * (outer(i, floor_y) - wall * 1.4), floor_y + rnd.uniform(-0.01, 0.02), math.sin(math.tau * i / sides) * (outer(i, floor_y) - wall * 1.4)))) for i in range(sides)]
    outer_faces = []
    for j in range(len(rings) - 1):
        for i in range(sides):
            k = (i + 1) % sides
            outer_faces.append(bm.faces.new((rings[j][i], rings[j][k], rings[j + 1][k], rings[j + 1][i])))
    rim_faces, inner_faces = [], []
    for i in range(sides):
        k = (i + 1) % sides
        rim_faces.append(bm.faces.new((rings[-1][i], rings[-1][k], inner_top[k], inner_top[i])))
        inner_faces.append(bm.faces.new((inner_top[i], inner_top[k], inner_low[k], inner_low[i])))
    c = bm.verts.new(B((0, floor_y - 0.02, 0)))
    floor_faces = [bm.faces.new((inner_low[i], inner_low[(i + 1) % sides], c)) for i in range(sides)]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # the inner wall and the floor face inward/up: recalc can't know a hollow from a solid, so set them by hand
    for f in outer_faces:
        if f.normal.dot(f.calc_center_median() * Vector((1, 1, 0))) < 0:
            f.normal_flip()
    for f in inner_faces:
        if f.normal.dot(f.calc_center_median() * Vector((1, 1, 0))) > 0:
            f.normal_flip()
    for f in floor_faces:
        if f.normal.z < 0:
            f.normal_flip()
    for f in rim_faces:
        if f.normal.z < 0:
            f.normal_flip()
    me_faces = set(inner_faces) | set(floor_faces) | set(rim_faces)
    for f in bm.faces:
        f.material_index = 1 if f in me_faces else 0
    ob = obj_from_bmesh(bm, "HollowStump", bark)
    ob.data.materials.append(rot_)
    uv_box(ob, 2.5)
    smooth(ob, 55)
    # three roots running into the ground
    roots = []
    for a in (0.6, 2.9, 4.4):
        d = Vector((math.cos(a), 0, math.sin(a)))
        roots.append(gcyl(f"root{a}", (d.x * 0.28, 0.06, d.z * 0.28), (d.x * 0.72, -0.14, d.z * 0.72), 0.14, 0.05, 10, bark, uvs=2.5))
    return finish([ob] + roots, "HollowStump")


def cairn():
    # (2026-10-29, the playtest: "the cairn should look older: greener mossy stones, the bottom stones sunk into the ground as
    # if buried") the woods' mossy stone ("moss_rock": the moss on every upward face), the whole pile settled 5 cm (20 at the
    # game's scale) so its foot is half under the ground, and three old stones round the foot almost buried
    rock = M("moss_rock", (0.8, 0.8, 0.76))
    rnd = random.Random(12)
    stones = []
    sink = 0.05
    # (centre, radii (x, y, z) in the game's frame) bottom up; the front (+Z) foot is left open under a flat stone
    spec = [((-0.2, 0.09, -0.05), (0.2, 0.11, 0.17)), ((0.19, 0.08, -0.08), (0.18, 0.1, 0.16)), ((0.0, 0.09, -0.24), (0.2, 0.1, 0.15)),
            ((-0.24, 0.07, 0.17), (0.12, 0.08, 0.11)), ((0.24, 0.07, 0.16), (0.12, 0.08, 0.12)),
            # the lintel over the gap
            ((0.0, 0.21, 0.13), (0.28, 0.045, 0.16)),
            ((-0.06, 0.3, -0.08), (0.2, 0.09, 0.17)), ((0.08, 0.43, -0.04), (0.15, 0.08, 0.13)), ((-0.02, 0.54, -0.06), (0.11, 0.07, 0.1)),
            ((0.02, 0.63, -0.05), (0.07, 0.05, 0.07))]
    spec = [((x, y - sink, z), r) for (x, y, z), r in spec]
    # tumbled off it long ago and gone into the ground
    spec += [((-0.38, -0.035, -0.22), (0.11, 0.06, 0.09)), ((0.36, -0.04, -0.3), (0.09, 0.055, 0.1)), ((0.12, -0.04, -0.42), (0.12, 0.06, 0.08))]
    for i, (c, r) in enumerate(spec):
        ob = lumpy_sphere(f"stone{i}", B(c), S(r), rock, 40 + i * 3, amp=0.07, freq=2.0, subdiv=3)
        stones.append(ob)
    ob = finish(stones, "Cairn")
    uv_box(ob, 3.0)
    return ob


def keyhole():
    brass = M("brass", (0.7, 0.58, 0.36), 0.45, 0.8)
    plate = gbox("plate", (0, 0, 0.004), (0.07, 0.11, 0.008), brass, bevel=0.003, segs=3)
    screws = [gcyl(f"screw{i}", (0, y, 0.008), (0, y, 0.011), 0.0045, 0.004, 10, M("steel_dark")) for i, y in enumerate((0.042, -0.042))]
    # the slot: a round head and a narrow throat, dark, a few millimetres proud of the plate's face (read as a hole)
    hole = M("hole", None, 0.95, 0.0)
    head = gcyl("slot_head", (0, 0.012, 0.0081), (0, 0.012, 0.0086), 0.0085, 0.0085, 16, hole)
    throat = gbox("slot_throat", (0, -0.006, 0.0083), (0.0062, 0.026, 0.0006), hole)
    # a hinged dust cover, swung aside
    cover = gbox("cover", (0.021, 0.022, 0.0105), (0.016, 0.028, 0.002), brass, bevel=0.0008)
    rot(cover, (0, 0, 1), 0.5, (0.021, 0.034, 0.0105))
    return finish([plate] + screws + [head, throat, cover], "Keyhole")


def build():
    clear()
    bow, bit = half_bow(), half_bit()
    joined = finish([copy(bow, "jb"), copy(bit, "jt")], "RhKey")
    box_, lid = tin()
    obs = [bow, bit, joined, box_, lid, hollow_stump(), cairn(), keyhole()]
    for o in obs:
        o.location = (0, 0, 0)
    export(obs, "rh_key")
    for o in obs:
        for q in obs:
            q.hide_render = q is not o
        d = max(o.dimensions)
        c = sum((o.matrix_world @ Vector(b) for b in o.bound_box), Vector()) / 8
        preview(f"rh_key_{o.name}", tuple(c), d * 1.4 + 0.03, height=d * 0.9, yaw=30, size=(600, 450))


if __name__ == "__main__":
    build()
