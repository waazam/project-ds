"""The hurricane lantern, modelled in Blender (2026-10-15, the owner's rule: every model made in Blender). Run headless:

    blender -b --python tools/Blender/lantern.py [-- lantern lantern_held]

- lantern (the pickup, lying about): the bail folded half down over the cap.
- lantern_held (in the left hand, Act 3 on): the bail standing tall and squared off, a turned wooden grip across its top
  for the fist to close round (HeldLantern hangs the lantern from the middle of that grip).

Same size and layout as the code-built one it replaces (ItemMeshes.Lantern), so the hand's grip, the hang, the flame's
place and the light all line up: a painted steel font 0.158 m across, a brass burner and wick knob, the clear globe on
it with soot clouding its top, six wire guards and three hoops, the vented cap and chimney, the bail. In the game's
space (Y up) everything stands on y = 0; here Z is up, and the glTF export turns it.

The materials are named for the game (it paints each with its own material by name, see LanternModel.cs):
  lantern_paint (the red font and cap), lantern_brass, lantern_steel (guards, hoops, bail), lantern_glass (the globe:
  the lit amber glass in the hand), lantern_soot (the globe's clouded top), lantern_vent (the dark vent holes),
  lantern_wood (the bail's grip), lantern_flame (the flame).
Objects: "Body" (all of it but the bail and flame), "Bail" (and its "Grip"), "Flame".
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "lantern")
os.makedirs(kit.OUT, exist_ok=True)

SIDES = 48


def lathe(name, profile, material, sides=SIDES, cap_bottom=False, cap_top=False, uv_v=6.0):
    """A surface of revolution round Z from (radius, height) points, smooth, with UVs round and up it."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rings = []
    for (r, z) in profile:
        ring = []
        for i in range(sides):
            a = math.tau * i / sides
            ring.append(bm.verts.new((math.cos(a) * r, math.sin(a) * r, z)))
        rings.append(ring)
    for j in range(len(rings) - 1):
        for i in range(sides):
            i2 = (i + 1) % sides
            f = bm.faces.new((rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]))
            for k, l in enumerate(f.loops):
                ii = i if k in (0, 3) else i + 1
                jj = j if k in (0, 1) else j + 1
                l[uv].uv = (ii / sides, profile[jj][1] * uv_v)
    if cap_bottom:
        f = bm.faces.new(list(reversed(rings[0])))
    if cap_top:
        f = bm.faces.new(rings[-1])
    bm.normal_update()
    ob = obj_from_bmesh(bm, name, material)
    ob.data.use_auto_smooth = True
    ob.data.auto_smooth_angle = math.radians(42)
    return ob


def wire(name, pts, r, material, cyclic=False, res=4):
    """A round wire along points (a curve with a bevel, made a mesh)."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = r
    cu.bevel_resolution = res
    cu.use_fill_caps = True
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        sp.points[i].co = (p[0], p[1], p[2], 1.0)
    sp.use_cyclic_u = cyclic
    ob = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    ob = bpy.context.view_layer.objects.active
    ob.data.materials.append(material)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def smooth_path(ctrl, n):
    """Catmull-Rom through control points, n points per span."""
    out = []
    P = [ctrl[0]] + list(ctrl) + [ctrl[-1]]
    for s in range(1, len(P) - 2):
        p0, p1, p2, p3 = (Vector(P[s - 1]), Vector(P[s]), Vector(P[s + 1]), Vector(P[s + 2]))
        for t in range(n):
            u = t / n
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u * u * u))
    out.append(Vector(ctrl[-1]))
    return [tuple(v) for v in out]


def build(held):
    clear()
    M = {
        "paint": mat("lantern_paint", (0.42, 0.08, 0.05), 0.55),
        "brass": mat("lantern_brass", (0.9, 0.76, 0.46), 0.35, 1.0),
        "steel": mat("lantern_steel", (0.4, 0.4, 0.42), 0.4, 1.0),
        "glass": mat("lantern_glass", (1.0, 0.95, 0.85), 0.1),
        "soot": mat("lantern_soot", (0.35, 0.3, 0.26), 0.4),
        "vent": mat("lantern_vent", (0.05, 0.04, 0.035), 0.9),
        "wood": mat("lantern_wood", (0.36, 0.22, 0.13), 0.6),
        "flame": mat("lantern_flame", (1.0, 0.75, 0.35), 0.9, emit=(1.0, 0.6, 0.2)),
    }
    parts = []
    # the font: a squat drum with a rolled base, swelling a little, its shoulder sloping in to the burner
    parts.append(lathe("Font", [(0.0, 0.0), (0.068, 0.0), (0.0745, 0.0015), (0.0775, 0.0055), (0.0785, 0.012), (0.079, 0.022),
                                (0.0788, 0.036), (0.078, 0.046), (0.0745, 0.052), (0.068, 0.0565), (0.06, 0.0605), (0.046, 0.0638), (0.034, 0.066)],
                       M["paint"], cap_bottom=False))
    # its rolled seam (a brass band) and the filler cap on the shoulder
    parts.append(lathe("Seam", [(0.0793, 0.026), (0.0809, 0.0278), (0.0812, 0.03), (0.0809, 0.0322), (0.0793, 0.034)], M["brass"]))
    cap = lathe("Filler", [(0.0, 0.0), (0.0108, 0.0), (0.0112, 0.002), (0.0108, 0.011), (0.0094, 0.0125), (0.0, 0.013)], M["brass"], sides=20)
    cap.location = (0.046, -0.021, 0.058)
    cap.rotation_euler = (math.radians(-12), math.radians(14), 0)
    parts.append(cap)
    # the burner: a brass collar under the globe, its gallery, and the wick knob out of its side on a short spindle
    parts.append(lathe("Burner", [(0.034, 0.063), (0.0366, 0.066), (0.0368, 0.07), (0.0345, 0.0745), (0.032, 0.08), (0.0302, 0.0838), (0.024, 0.085)], M["brass"]))
    spindle = cylinder("Spindle", (0.034, 0, 0.072), (0.057, 0, 0.072), 0.0025, sides=10, material=M["brass"])
    parts.append(spindle)
    knob = lathe("Knob", [(0.0, 0.0), (0.0072, 0.0), (0.0082, 0.0012), (0.0082, 0.0038), (0.0072, 0.005), (0.0, 0.005)], M["brass"], sides=24)
    knob.location = (0.057, 0, 0.072)
    knob.rotation_euler = (0, math.radians(90), 0)
    parts.append(knob)
    # the wire guards: six bowed rods from the font's shoulder up to the cap, and three hoops round them
    for i in range(6):
        a = math.tau * (i + 0.5) / 6
        d = (math.cos(a), math.sin(a))
        ctrl = [(d[0] * 0.066, d[1] * 0.066, 0.05), (d[0] * 0.0735, d[1] * 0.0735, 0.095), (d[0] * 0.075, d[1] * 0.075, 0.14),
                (d[0] * 0.072, d[1] * 0.072, 0.185), (d[0] * 0.064, d[1] * 0.064, 0.222)]
        parts.append(wire(f"Guard{i}", smooth_path(ctrl, 6), 0.0028, M["steel"]))
    for (z, r) in [(0.09, 0.0705), (0.14, 0.0765), (0.19, 0.0715)]:
        pts = [(math.cos(math.tau * i / 40) * r, math.sin(math.tau * i / 40) * r, z) for i in range(40)]
        parts.append(wire(f"Hoop{z:.2f}", pts, 0.0022, M["steel"], cyclic=True))
    # the globe: clear glass, its waist and shoulders; the top clouded with soot (its own material)
    globe = [(0.028, 0.078), (0.036, 0.083), (0.042, 0.092), (0.047, 0.1), (0.051, 0.109), (0.0545, 0.12), (0.057, 0.132),
             (0.058, 0.145), (0.0575, 0.158), (0.055, 0.172), (0.0515, 0.185), (0.05, 0.19)]
    parts.append(lathe("Globe", globe, M["glass"], uv_v=4.0))
    parts.append(lathe("Soot", [(0.05, 0.19), (0.0465, 0.197), (0.042, 0.204), (0.036, 0.21), (0.03, 0.216)], M["soot"]))
    # the cap: a pressed dome over the globe with a rolled lip, rising to the chimney with its own little hat
    parts.append(lathe("Cap", [(0.06, 0.212), (0.0645, 0.2135), (0.068, 0.217), (0.0685, 0.2215), (0.066, 0.2255), (0.058, 0.232),
                               (0.051, 0.238), (0.045, 0.2455), (0.036, 0.2515), (0.028, 0.256), (0.0215, 0.259), (0.021, 0.27),
                               (0.0212, 0.276), (0.0255, 0.278), (0.0262, 0.281), (0.024, 0.2835), (0.0, 0.2845)], M["paint"], cap_bottom=False))
    # the vent holes round the cap's slope: dark slots pressed in
    for i in range(10):
        a = math.tau * i / 10
        slot = box(f"Vent{i}", (math.cos(a) * 0.0525, math.sin(a) * 0.0525, 0.2385), (0.0085, 0.0042, 0.0042), M["vent"], bevel=0.0012, segs=2,
                   rot=(0, math.radians(38), a))
        parts.append(slot)
    body = join(parts, "Body")
    for p in body.data.polygons:
        p.use_smooth = True
    body.data.use_auto_smooth = True
    body.data.auto_smooth_angle = math.radians(42)

    # the bail: a stiff wire from the cap's sides, over the top
    rise = 0.09 if held else 0.1
    pts = []
    n = 32 if held else 20
    for i in range(n + 1):
        a = math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        if held:   # squared off: straight across the top under the grip
            c = math.copysign(abs(c) ** 0.5, c)
            s = abs(s) ** 0.5
        pts.append((c * 0.07, 0.0, 0.232 + s * rise))
    # its ends turned in through the ears on the cap
    pts = [(0.062, 0.0, 0.226)] + pts + [(-0.062, 0.0, 0.226)]
    bail = wire("Bail", pts, 0.003, M["steel"])
    objs = [body, bail]
    if held:
        y = 0.232 + rise
        prof = [(0.0042, -0.0445), (0.0062, -0.0425), (0.0082, -0.039), (0.0089, -0.03), (0.0092, -0.018), (0.0093, 0.0),
                (0.0092, 0.018), (0.0089, 0.03), (0.0082, 0.039), (0.0062, 0.0425), (0.0042, 0.0445)]
        grip = lathe("Grip", [(r, z) for (r, z) in prof], M["wood"], sides=24, uv_v=8.0)
        grip.rotation_euler = (0, math.radians(90), 0)
        grip.location = (0, 0, y)
        objs.append(grip)
    # the flame: a soft teardrop on the wick
    fl = lathe("Flame", [(0.0, 0.086), (0.0045, 0.09), (0.0078, 0.098), (0.008, 0.104), (0.0065, 0.114), (0.0035, 0.124), (0.0, 0.133)],
               M["flame"], sides=16)
    objs.append(fl)
    name = "lantern_held" if held else "lantern"
    export(objs, name)
    preview(name, (0, 0, 0.15), 0.75, height=0.25, yaw=30)


BUILDERS = {"lantern": lambda: build(False), "lantern_held": lambda: build(True)}

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for n in (argv or list(BUILDERS)):
        BUILDERS[n]()
