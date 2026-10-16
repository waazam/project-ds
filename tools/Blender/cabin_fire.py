"""The cabin's fire (2026-10-16, STORY.md Revision 5, Act 7: part of the roof caves in over the door, burning beams across
the doorway, chopped through with the axe in three blows; Act 25: the cabin standing burned). Run headless:

    blender -b --python tools/Blender/cabin_fire.py

-> assets/models/items/cabin_fire.glb (ItemModels.Get("cabin_fire", <name>)):
- BeamA, BeamB, BeamC: the three burning beams fallen across the doorway, one mesh each (each blow of the axe takes one
  away). Modelled in the cabin's frame with the origin at the middle of the door's threshold on the floor (y = 0), +Z out
  of the door: they lie just inside it (z from -0.6 to -0.05), wedged across the opening (1.3 wide, 2.0 high) from the
  floor to the jambs. A is the low cross-beam, B and C the two rafters crossed over it.
- RoofDebris: what came down with them: broken roof boards, split shingles and charred splinters heaped on the floor just
  inside the door (z from -1.5 to 0), never higher than 0.22 (walked over).
- BeamStubs: the chopped ends left of them, kicked against the wall either side of the door (Act 25's room).
- AshSheet: the assignment sheet burned to ash where it lay: a curled grey leaf, broken at the edges, crumbs round it.
  Origin at the middle of its underside (on the folder, at y = 0); about 0.2 x 0.26.

The game's axes, as cabin_story.py: B()/S() turn a game point/size (Y up, -Z forward) into Blender's. Materials named for
the game to paint: "char" (charred wood, black and cracked), "ember" (the glowing cracks in it), "ash" (grey ash),
"char_paper" (the sheet's blackened edges).
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "items")
os.makedirs(kit.OUT, exist_ok=True)

PREVIEW_COL = {"char": (0.05, 0.04, 0.035), "ember": (0.95, 0.35, 0.08), "ash": (0.5, 0.49, 0.47), "char_paper": (0.12, 0.11, 0.1)}


def M(base, rough=0.9, metal=0.0):
    return mat(base, PREVIEW_COL.get(base, (0.6, 0.6, 0.6)), rough, metal)


def B(p):
    x, y, z = p
    return Vector((x, -z, y))


def apply(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def smooth(ob, angle=40):
    for p in ob.data.polygons:
        p.use_smooth = True
    if hasattr(ob.data, "use_auto_smooth"):
        ob.data.use_auto_smooth = True
        ob.data.auto_smooth_angle = math.radians(angle)
    return ob


def two_mats(ob, a, b, share, seed):
    """Every face is material a, but for a random share of them (b): the embers in the char."""
    ob.data.materials.clear()
    ob.data.materials.append(a)
    ob.data.materials.append(b)
    r = random.Random(seed)
    for p in ob.data.polygons:
        p.material_index = 1 if r.random() < share else 0


def rough_up(ob, amp, seed, keep_ends=False):
    """Knocks the verts about a little (charred wood is never true)."""
    r = random.Random(seed)
    for v in ob.data.vertices:
        v.co += Vector((r.uniform(-amp, amp), r.uniform(-amp, amp), r.uniform(-amp, amp)))


def beam(name, a, b, radius, seed, sides=10, segs=9, ember=0.22):
    """A charred log beam from game point a to b: its round cut into ridges of char, cracked, its ends split."""
    a, b = Vector(a), Vector(b)
    bm = bmesh.new()
    axis = b - a
    length = axis.length
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=sides, radius1=radius, radius2=radius * 0.9, depth=length)
    # cut along its length so it can crack and sag
    edges = [e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > length * 0.5]
    bmesh.ops.subdivide_edges(bm, edges=edges, cuts=segs - 1, use_grid_fill=True)
    r = random.Random(seed)
    for v in bm.verts:
        u = (v.co.z / length) + 0.5
        # the char: deep alligator cracks, a little lost off the round toward the ends (burned through there)
        n = fbm(v.co.x * 30 + seed, v.co.y * 30, v.co.z * 7, 3)
        k = 1.0 + 0.32 * n - 0.18 * max(0.0, abs(u - 0.5) * 2 - 0.6)
        v.co.x *= k
        v.co.y *= k
        v.co.z += r.uniform(-0.01, 0.01)
    # its ends split into splinters: the top cap's verts pulled out unevenly
    for v in bm.verts:
        if abs(v.co.z) > length * 0.49:
            v.co.z += math.copysign(r.uniform(0.0, 0.09), v.co.z)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    # into place: Blender's cone runs along its Z, centred
    ba, bb = B(a), B(b)
    q = Vector((0, 0, 1)).rotation_difference((bb - ba).normalized())
    ob.matrix_world = Matrix.Translation((ba + bb) * 0.5) @ q.to_matrix().to_4x4()
    apply(ob)
    two_mats(ob, M("char"), M("ember", 0.6), ember, seed)
    uv_box(ob, 6.0)
    return smooth(ob, 35)


def board(name, c, size, yaw, tilt, seed, m):
    """A broken roof board (or a split shingle): a thin plank, one end snapped off ragged."""
    x, y, z = c
    w, h, d = size
    ob = box(name, B(c), (w, d, h), m)
    apply(ob)
    me = ob.data
    r = random.Random(seed)
    # the snapped end: the +X end's verts pushed about
    for v in me.vertices:
        if v.co.x > x + w * 0.4:
            v.co.x += r.uniform(-w * 0.18, w * 0.05)
            v.co.y += r.uniform(-d * 0.1, d * 0.1)
    pv = B(c)
    me.transform(Matrix.Translation(pv) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(tilt, 4, "Y") @ Matrix.Translation(-pv))
    uv_box(ob, 6.0)
    return ob


# ------------------------------------------------------------------ the beams across the doorway

def beams():
    # A: the low cross-beam, jammed between the jambs a little above the knee, sagging to its right end
    a = beam("BeamA", (-0.92, 0.62, -0.22), (0.95, 0.48, -0.3), 0.085, 11)
    # B: a rafter down from the left jamb's head to the floor at the right, inside
    b = beam("BeamB", (-0.78, 1.78, -0.12), (0.88, 0.06, -0.55), 0.072, 23)
    # C: the other rafter, crossed over it, from the right jamb's head down to the floor at the left
    c = beam("BeamC", (0.8, 1.62, -0.08), (-0.86, 0.05, -0.48), 0.07, 37)
    for o, n in ((a, "BeamA"), (b, "BeamB"), (c, "BeamC")):
        o.name = n
    return [a, b, c]


def roof_debris():
    p = []
    r = random.Random(5)
    char, ember = M("char"), M("ember", 0.6)
    for i in range(9):
        x = r.uniform(-0.8, 0.8)
        z = r.uniform(-1.4, -0.1)
        w = r.uniform(0.45, 0.95)
        o = board(f"b{i}", (x, 0.015 + i * 0.008, z), (w, 0.022, r.uniform(0.11, 0.16)), r.uniform(-1.4, 1.4), r.uniform(-0.12, 0.12), 100 + i, char)
        p.append(o)
    for i in range(14):
        x = r.uniform(-0.9, 0.9)
        z = r.uniform(-1.5, 0.0)
        o = board(f"s{i}", (x, 0.01 + r.uniform(0, 0.04), z), (r.uniform(0.18, 0.32), 0.012, r.uniform(0.1, 0.16)), r.uniform(-3, 3), r.uniform(-0.3, 0.3), 200 + i, char)
        p.append(o)
    # splinters and a few glowing coals among them
    for i in range(10):
        x, z = r.uniform(-0.9, 0.9), r.uniform(-1.4, -0.05)
        a = (x, 0.02, z)
        bb = (x + r.uniform(-0.25, 0.25), 0.03 + r.uniform(0, 0.05), z + r.uniform(-0.25, 0.25))
        o = cylinder(f"sp{i}", B(a), B(bb), 0.012, 0.004, 5, char)
        p.append(o)
    for i in range(7):
        x, z = r.uniform(-0.7, 0.7), r.uniform(-1.2, -0.1)
        o = lumpy_sphere(f"coal{i}", B((x, 0.02, z)), (0.035, 0.03, 0.022), ember, 300 + i, amp=0.25, freq=3.0, subdiv=1)
        p.append(o)
    ob = join(p, "RoofDebris")
    return smooth(ob, 30)


def beam_stubs():
    p = [
        beam("st0", (-1.15, 0.08, -0.35), (-1.2, 0.1, -1.25), 0.08, 51, segs=3, ember=0.04),
        beam("st1", (-1.05, 0.07, -0.25), (-0.55, 0.32, -0.3), 0.07, 52, segs=2, ember=0.03),
        beam("st2", (1.12, 0.08, -0.4), (1.25, 0.09, -1.2), 0.072, 53, segs=3, ember=0.03),
    ]
    ob = join(p, "BeamStubs")
    return smooth(ob, 35)


def ash_sheet():
    """The sheet burned where it lay: a grey leaf of ash, curled up at its edges, broken, its edges blackened."""
    W, D = 0.2, 0.26
    bm = bmesh.new()
    nx, nz = 10, 13
    r = random.Random(77)
    verts = {}
    for i in range(nx + 1):
        for j in range(nz + 1):
            u, v = i / nx, j / nz
            x, z = (u - 0.5) * W, (v - 0.5) * D
            # curling: the edges lift and roll in
            e = max(abs(u - 0.5), abs(v - 0.5)) * 2
            y = 0.0015 + 0.022 * max(0.0, e - 0.55) ** 1.6 + 0.003 * fbm(x * 40, z * 40, 1, 2)
            x *= 1 - 0.12 * max(0.0, e - 0.7)
            z *= 1 - 0.12 * max(0.0, e - 0.7)
            verts[(i, j)] = bm.verts.new(B((x + r.uniform(-0.002, 0.002), y, z + r.uniform(-0.002, 0.002))))
    for i in range(nx):
        for j in range(nz):
            u, v = (i + 0.5) / nx, (j + 0.5) / nz
            e = max(abs(u - 0.5), abs(v - 0.5)) * 2
            # broken: bits of it gone, more at the edges
            if e > 0.75 and r.random() < 0.35 * (e - 0.6) * 3:
                continue
            if r.random() < 0.04:
                continue
            f = bm.faces.new((verts[(i, j)], verts[(i + 1, j)], verts[(i + 1, j + 1)], verts[(i, j + 1)]))
            f.material_index = 1 if e > 0.72 else 0
    ob = obj_from_bmesh(bm, "AshSheetLeaf", None)
    ob.data.materials.append(M("ash", 0.95))
    ob.data.materials.append(M("char_paper", 0.95))
    add_mod(ob, "SOLIDIFY", thickness=0.0015, offset=-1.0)
    apply_mods(ob)
    p = [ob]
    # crumbs of it round about
    for i in range(9):
        x, z = r.uniform(-0.16, 0.16), r.uniform(-0.19, 0.19)
        if abs(x) < 0.08 and abs(z) < 0.11:
            continue
        p.append(lumpy_sphere(f"crumb{i}", B((x, 0.002, z)), (0.012, 0.009, 0.0025), M("ash", 0.95), 400 + i, amp=0.3, freq=4.0, subdiv=1))
    out = join(p, "AshSheet")
    return smooth(out, 25)


def build():
    clear()
    obs = beams() + [roof_debris(), beam_stubs(), ash_sheet()]
    for o in obs:
        o.location = (0, 0, 0)
    export(obs, "cabin_fire")
    for o in obs:
        for q in obs:
            q.hide_render = q is not o
        d = max(o.dimensions)
        c = sum((o.matrix_world @ Vector(b) for b in o.bound_box), Vector()) / 8
        preview(f"cabin_fire_{o.name}", tuple(c), d * 1.4 + 0.05, height=d * 0.6, yaw=30, size=(600, 450))


if __name__ == "__main__":
    build()
