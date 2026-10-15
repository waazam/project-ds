"""The title screen's set, modelled in Blender (2026-10-15, the owner's rule: every model made in Blender). Run headless:

    blender -b --python tools/Blender/title_tv.py

Writes assets/models/title/title_tv.glb and a preview in build/blender/title_tv.png.

An old wood-grain tube TV of the late eighties, knobs on the right: a moulded cabinet with softly rounded edges, a bezel
round a bulging tube, a dark mask with rounded inner corners, a channel knob and a volume knob (knurled), a slotted speaker
grille, rubber feet, rabbit-ear antennas on top, a cable out of the back; and two cables snaking off across the floor.

The TV is built at the size the game asks for (BunkerKit.RandomCrt size class 2 with jitter 1: 0.72 x 0.6 x 0.6 m; the game
scales it to its own spec), standing on its origin with the screen facing the game's +Z (Blender -Y). The tube is its own
object ("TvScreen") with UVs 0..1 over the picture (u left to right, v top to bottom in the game), so the title's tape
shader still plays on it. Everything else under the "TV" empty; the floor cables ("CableA", "CableB") in room space.

Materials, painted by the game with its own: cabinet, trim, knob, grille, rubber, metal, screen.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "title")
os.makedirs(kit.OUT, exist_ok=True)

W, H, D = 0.72, 0.6, 0.6
FZ = D * 0.5
SW, SH = W * 0.7, H * 0.74
SCX, SCY = -W * 0.11, H * 0.53


def G(x, y, z):
    return Vector((x, -z, y))


def rrect(w, h, r, n=4):
    """A rounded rectangle's outline (x, y), counter-clockwise."""
    pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def loft(name, sections, material, cap_front=True, cap_back=True):
    """Lofts closed outlines: sections = [(z, cx, cy, outline)], all outlines with the same point count."""
    bm = bmesh.new()
    rings = [[bm.verts.new(G(cx + x, cy + y, z)) for x, y in ol] for z, cx, cy, ol in sections]
    n = len(rings[0])
    for j in range(len(rings) - 1):
        for k in range(n):
            bm.faces.new((rings[j][k], rings[j][(k + 1) % n], rings[j + 1][(k + 1) % n], rings[j + 1][k]))
    if cap_front:
        bm.faces.new(rings[0])
    if cap_back:
        bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name, material)


def title_tv():
    clear()
    cabinet = mat("cabinet", (0.2, 0.13, 0.08), 0.6)
    trim = mat("trim", (0.06, 0.055, 0.05), 0.5)
    knob = mat("knob", (0.08, 0.08, 0.08), 0.45)
    grille = mat("grille", (0.05, 0.05, 0.05), 0.8)
    rubber = mat("rubber", (0.04, 0.04, 0.04), 0.9)
    metal = mat("metal", (0.6, 0.6, 0.62), 0.35, 1.0)
    screen = mat("screen", (0.2, 0.22, 0.24), 0.2)
    parts = []

    # the cabinet: a softly rounded front slab, its sides drawing back into the tapered tube housing
    front = rrect(W, H, 0.035, 5)
    housing_front = rrect(W * 0.95, H * 0.93, 0.05, 5)
    housing_back = rrect(W * 0.52, H * 0.55, 0.06, 5)
    slab = loft("slab", [(FZ, 0, H * 0.5, rrect(W - 0.012, H - 0.012, 0.029, 5)), (FZ - 0.006, 0, H * 0.5, front),
                         (FZ - 0.09, 0, H * 0.5, front)], cabinet)
    parts.append(slab)
    parts.append(loft("housing", [(FZ - 0.08, 0, H * 0.5, housing_front), (FZ - 0.3, 0, H * 0.47, rrect(W * 0.78, H * 0.78, 0.06, 5)),
                                  (-FZ, 0, H * 0.42, housing_back)], cabinet))

    # the mask round the tube: a dark frame with rounded inner corners, its outer edge a raised lip
    lip = 0.022
    outer = rrect(SW + lip * 2, SH + lip * 2, 0.03, 5)
    inner = rrect(SW - 0.006, SH - 0.006, 0.045, 5)
    bm = bmesh.new()
    zf, zb = FZ + 0.012, FZ - 0.004
    def ring(ol, z):
        return [bm.verts.new(G(SCX + x, SCY + y, z)) for x, y in ol]
    of, ob_, inf, inb = ring(outer, zf), ring(outer, zb), ring(inner, zf - 0.006), ring(inner, zb)
    n = len(outer)
    for k in range(n):
        k1 = (k + 1) % n
        bm.faces.new((of[k], of[k1], inf[k1], inf[k]))
        bm.faces.new((ob_[k], of[k], of[k1], ob_[k1])[::-1])
        bm.faces.new((inf[k], inf[k1], inb[k1], inb[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    parts.append(obj_from_bmesh(bm, "mask", trim))

    # the knobs: channel and volume, knurled, and a slotted speaker grille under them
    kx = W * 0.39
    for (ky, r0, r1, depth) in ((H * 0.72, 0.028, 0.024, 0.03), (H * 0.55, 0.022, 0.02, 0.025)):
        bm = bmesh.new()
        seg = 32
        rings = []
        for z, rr in ((FZ, r0 + 0.003), (FZ + 0.004, r0), (FZ + depth * 0.8, r1), (FZ + depth, r1 * 0.8)):
            ring_ = []
            for k in range(seg):
                a = 2 * math.pi * k / seg
                knurl = 0.0015 if (k % 2 == 0 and z > FZ) else 0.0
                ring_.append(bm.verts.new(G(kx + math.cos(a) * (rr + knurl), ky + math.sin(a) * (rr + knurl), z)))
            rings.append(ring_)
        for j in range(len(rings) - 1):
            for k in range(seg):
                bm.faces.new((rings[j][k], rings[j][(k + 1) % seg], rings[j + 1][(k + 1) % seg], rings[j + 1][k]))
        bm.faces.new(list(reversed(rings[-1])))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        kb = obj_from_bmesh(bm, f"knob{ky:.2f}", knob)
        parts.append(kb)
        # the pointer line on the knob's face
        parts.append(box(f"pointer{ky:.2f}", G(kx, ky + r1 * 0.45, FZ + depth + 0.001), (0.004, 0.003, r1 * 0.8), trim))
    for i in range(6):
        parts.append(box(f"slot{i}", G(kx, H * 0.16 + i * 0.026, FZ + 0.002), (W * 0.13, 0.008, 0.008), grille, bevel=0.002, segs=1))

    # rubber feet, the cable out of the back
    for fx in (-W * 0.35, W * 0.35):
        parts.append(cylinder(f"foot{fx:.2f}", G(fx, 0.0, FZ - 0.1), G(fx, 0.02, FZ - 0.1), 0.03, 0.026, 16, rubber))
    parts.append(cable("backcable", [(W * 0.1, H * 0.3, -FZ + 0.02), (W * 0.14, H * 0.26, -FZ - 0.08), (W * 0.11, 0.02, -FZ - 0.16)], 0.012, rubber))

    # rabbit ears on top: a weighted base and two telescoping rods in a V
    top = H * 0.5 + (H * 0.47 - H * 0.5) * 0.3 + H * 0.39
    bx, bz = W * 0.08, -0.06
    parts.append(cylinder("earbase", G(bx, H * 0.86 - 0.01, bz), G(bx, H * 0.86 + 0.025, bz), 0.05, 0.042, 24, trim))
    for sgn in (-1, 1):
        a = math.radians(32) * sgn
        tilt = math.radians(-12)
        d = Vector((math.sin(a), math.cos(a) * math.cos(tilt), math.cos(a) * math.sin(tilt)))
        p0 = Vector((bx + sgn * 0.012, H * 0.86 + 0.02, bz))
        segs = [(0.0, 0.16, 0.0045), (0.16, 0.3, 0.0035), (0.3, 0.44, 0.0026)]
        for (u0, u1, r) in segs:
            a0, a1 = p0 + d * u0, p0 + d * u1
            parts.append(cylinder(f"ear{sgn}{u0}", G(*a0), G(*a1), r, r, 10, metal))
        tip = p0 + d * 0.44
        parts.append(kit.lumpy_sphere(f"eartip{sgn}", G(*tip), (0.006, 0.006, 0.006), metal, 1, amp=0.0, subdiv=2))

    tv = join(parts, "TvBody")
    uv_box(tv, 4.0)
    add_mod(tv, "WEIGHTED_NORMAL", keep_sharp=True)
    apply_mods(tv)

    # the tube: a bulging glass face, UVs 0..1 over the picture (Blender v up; the export flips it to the game's top-down)
    bm = bmesh.new()
    nx, ny = 20, 16
    bulge = 0.022
    uv = bm.loops.layers.uv.new("UVMap")
    vs = []
    for j in range(ny + 1):
        row = []
        for i in range(nx + 1):
            u, v = i / nx * 2 - 1, j / ny * 2 - 1
            z = FZ + 0.002 + bulge * (1 - 0.55 * u * u - 0.55 * v * v)
            row.append(bm.verts.new(G(SCX + u * SW * 0.5, SCY + v * SH * 0.5, z)))
        vs.append(row)
    for j in range(ny):
        for i in range(nx):
            f = bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
            for loop, (ii, jj) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                loop[uv].uv = (ii / nx, jj / ny)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    scr = obj_from_bmesh(bm, "TvScreen", screen)
    for f in scr.data.polygons:
        if f.normal.y > 0:   # face the viewer (Blender -Y)
            pass

    tv_empty = bpy.data.objects.new("TV", None)
    bpy.context.scene.collection.objects.link(tv_empty)
    tv.parent = tv_empty
    scr.parent = tv_empty

    # the two cables across the floor (room space, as the game laid them)
    ca = cable("CableA", [(0.08, 0.17, -0.36), (0.3, 0.012, -0.6), (1.1, 0.012, -0.4), (2.2, 0.012, 0.3)], 0.012, rubber)
    cb = cable("CableB", [(-0.1, 0.15, -0.36), (-0.5, 0.01, -0.7), (-1.6, 0.01, -0.5), (-2.6, 0.01, 0.4)], 0.01, rubber)

    export([tv_empty, tv, scr, ca, cb], "title_tv")
    preview("title_tv", (0, 0, 0.32), 1.6, height=0.25, yaw=205)


def cable(name, pts, radius, material):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    sp = cu.splines.new("BEZIER")
    sp.bezier_points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        bp = sp.bezier_points[i]
        bp.co = G(*p)
        bp.handle_left_type = bp.handle_right_type = "AUTO"
    cu.bevel_depth = radius
    cu.bevel_resolution = 3
    cu.resolution_u = 10
    cu.use_fill_caps = True
    ob = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    ob = bpy.context.view_layer.objects.active
    ob.data.materials.clear()
    ob.data.materials.append(material)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


if __name__ == "__main__":
    title_tv()
