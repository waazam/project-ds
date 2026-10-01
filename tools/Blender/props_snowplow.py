"""The snowplow (see winter_props.py): front +X, the road (and the open door) +Y."""
from kit import *


def paint_texture():
    """Municipal orange: a little mottled, chipped to dark primer here and there, spotted with rust (128 px). (Long
    streaks read as wood grain.)"""
    def fn(x, y):
        n = fbm(x * 6, y * 6, 0.3, 3)
        chip = fbm(x * 40, y * 40, 1.7, 2)
        rust = max(0.0, fbm(x * 9 + 5, y * 9, 2.2, 4) - 0.28) * 3.0
        base = (0.68, 0.42, 0.06)
        c = [b * (0.93 + 0.12 * n) for b in base]
        if chip > 0.4:
            c = [0.16, 0.15, 0.14]
        rust = min(rust, 1.0)
        c = [c[0] * (1 - rust) + 0.32 * rust, c[1] * (1 - rust) + 0.14 * rust, c[2] * (1 - rust) + 0.05 * rust]
        return tuple(max(0.0, min(1.0, v)) for v in c)
    return make_texture("plow_paint", 128, fn)


def wheel(name, x, y, side, dual=False):
    """A truck wheel on the axle at (x, y): a treaded tyre, a steel rim, its lug nuts (side: +1 outward toward +Y)."""
    rubber = mat("rubber", (0.05, 0.05, 0.05), 0.9)
    steel = mat("rim", (0.32, 0.3, 0.28), 0.5, 0.6)
    parts = []
    w = 0.3
    ys = [y] if not dual else [y - side * 0.17, y + side * 0.17]
    for k, yy in enumerate(ys):
        a, b = Vector((x, yy - w / 2, 0.52)), Vector((x, yy + w / 2, 0.52))
        tire = cylinder(f"{name}Tyre{k}", a, b, 0.5, 0.5, 28, rubber)
        add_mod(tire, "BEVEL", width=0.06, segments=3, limit_method="ANGLE")
        apply_mods(tire)
        parts.append(tire)
        # the tread: blocks round it
        for t in range(28):
            ang = t / 28 * math.tau
            c = Vector((x + math.cos(ang) * 0.505, yy, 0.52 + math.sin(ang) * 0.505))
            parts.append(box(f"{name}Tread{k}_{t}", c, (0.06, w * 0.82, 0.035), rubber, rot=(0, -ang, 0)))
    # the rim on the outer face, its hub and lug nuts
    yo = ys[-1] + side * (w / 2 + 0.005)
    parts.append(cylinder(f"{name}Rim", (x, yo - side * 0.03, 0.52), (x, yo, 0.52), 0.31, 0.3, 20, steel))
    parts.append(cylinder(f"{name}Hub", (x, yo, 0.52), (x, yo + side * 0.06, 0.52), 0.12, 0.09, 12, steel))
    for t in range(8):
        ang = t / 8 * math.tau
        c = Vector((x + math.cos(ang) * 0.19, yo, 0.52 + math.sin(ang) * 0.19))
        parts.append(cylinder(f"{name}Lug{t}", c, c + Vector((0, side * 0.035, 0)), 0.022, 0.018, 6, steel))
    return parts


def build():
    """The snowplow that cut the road: a municipal plow truck pulled over against the wall it threw up, its blade angled
    into the snow, the cab's door hanging open on an empty seat (the key still in it), the headlights dead."""
    clear()
    paint = mat("plow_paint", (1, 1, 1), 0.55, 0.15, tex=paint_texture())
    black = mat("plow_black", (0.06, 0.06, 0.065), 0.7)
    steel = mat("plow_steel", (0.36, 0.36, 0.38), 0.45, 0.7)
    glass = mat("plow_glass", (0.03, 0.04, 0.05), 0.08, 0.2)
    chrome = mat("plow_chrome", (0.7, 0.7, 0.72), 0.2, 1.0)
    lamp = mat("plow_lamp", (0.75, 0.72, 0.6), 0.15)
    amber = mat("plow_amber", (0.55, 0.28, 0.02), 0.2)
    interior = mat("plow_interior", (0.08, 0.075, 0.07), 0.9)
    snow = snow_mat()
    P = []
    # ---- the frame and the wheels
    for y in (-0.48, 0.48):
        P.append(box("Rail", (-1.0, y, 0.78), (7.4, 0.12, 0.26), black, bevel=0.01, segs=1))
    P += wheel("FL", 1.35, -1.0, -1)
    P += wheel("FR", 1.35, 1.0, 1)
    for x in (-2.95, -4.2):
        P += wheel("RL", x, -0.95, -1, dual=True)
        P += wheel("RR", x, 0.95, 1, dual=True)
    for x in (1.35, -2.95, -4.2):
        P.append(cylinder("Axle", (x, -0.9, 0.52), (x, 0.9, 0.52), 0.07, 0.07, 8, black))
    # front fenders: arches over the front wheels
    for y in (-1.0, 1.0):
        bm = bmesh.new()
        segs = 14
        verts = []
        for i in range(segs + 1):
            a = math.pi * i / segs
            for dy in (-0.24, 0.24):
                verts.append(bm.verts.new((1.35 + math.cos(a) * 0.66, y + dy, 0.52 + math.sin(a) * 0.66)))
        for i in range(segs):
            bm.faces.new((verts[i * 2], verts[i * 2 + 1], verts[i * 2 + 3], verts[i * 2 + 2]))
        f = obj_from_bmesh(bm, "Fender", paint)
        add_mod(f, "SOLIDIFY", thickness=0.04)
        apply_mods(f)
        P.append(f)
    # ---- the hood, the grille, the lights, the bumper
    P.append(box("Hood", (1.12, 0, 1.38), (2.3, 1.9, 0.82), paint, bevel=0.09, segs=3))
    P.append(box("Grille", (2.29, 0, 1.3), (0.06, 1.5, 0.66), black, bevel=0.02, segs=1))
    for i in range(11):
        P.append(box("Slat", (2.33, -0.62 + i * 0.124, 1.3), (0.04, 0.035, 0.6), chrome))
    for y in (-0.82, 0.82):
        P.append(cylinder("Headlamp", (2.25, y, 1.2), (2.36, y, 1.2), 0.13, 0.12, 16, chrome))
        P.append(cylinder("Lens", (2.36, y, 1.2), (2.38, y, 1.2), 0.11, 0.1, 16, lamp))
    P.append(box("Bumper", (2.42, 0, 0.82), (0.18, 2.45, 0.28), steel, bevel=0.03, segs=2))
    # the plow lights on their stalks over the hood
    for y in (-0.95, 0.95):
        P.append(cylinder("Stalk", (2.1, y, 1.75), (2.1, y, 2.45), 0.03, 0.03, 8, black))
        P.append(box("PlowLight", (2.14, y, 2.5), (0.16, 0.3, 0.17), black, bevel=0.03, segs=2))
        P.append(box("PlowLens", (2.23, y, 2.5), (0.02, 0.24, 0.12), lamp))
    # ---- the cab
    P.append(box("Cab", (-0.95, 0, 1.92), (1.7, 2.3, 1.75), paint, bevel=0.045, segs=3))
    P.append(box("Windshield", (-0.09, 0, 2.3), (0.03, 1.95, 0.72), glass, bevel=0.02, segs=1))
    P.append(box("SideWindowL", (-0.95, -1.163, 2.3), (1.3, 0.03, 0.66), glass, bevel=0.02, segs=1))
    P.append(box("RearWindow", (-1.81, 0, 2.35), (0.03, 1.4, 0.45), glass))
    # the door opening on the road side: the cab's dark inside, the seat, the wheel, the key
    P.append(box("DoorHole", (-0.9, 1.13, 1.95), (1.1, 0.06, 1.45), interior))
    P.append(box("Seat", (-1.2, 0.55, 1.5), (0.55, 0.6, 0.18), interior, bevel=0.04, segs=2))
    P.append(box("SeatBack", (-1.47, 0.55, 1.85), (0.14, 0.6, 0.65), interior, bevel=0.04, segs=2, rot=(0, -0.15, 0)))
    ring = bmesh.new()
    vs = [ring.verts.new((math.cos(a / 16 * math.tau) * 0.2, math.sin(a / 16 * math.tau) * 0.2, 0)) for a in range(16)]
    for a in range(16):
        ring.edges.new((vs[a], vs[(a + 1) % 16]))
    me = bpy.data.meshes.new("Wheel")
    ring.to_mesh(me)
    ring.free()
    sw = bpy.data.objects.new("Wheel", me)
    bpy.context.scene.collection.objects.link(sw)
    sk = sw.modifiers.new("skin", "SKIN")
    for d in me.skin_vertices[0].data:
        d.radius = (0.022, 0.022)
    me.skin_vertices[0].data[0].use_root = True
    apply_mods(sw)
    sw.data.materials.append(black)
    sw.location = (-0.55, 0.55, 2.0)
    sw.rotation_euler = (0, math.radians(-55), 0)
    P.append(sw)
    P.append(cylinder("Column", (-0.55, 0.55, 2.0), (-0.3, 0.55, 1.6), 0.03, 0.04, 8, black))
    P.append(box("Key", (-0.42, 0.42, 1.78), (0.02, 0.015, 0.06), chrome))
    # the door itself, hinged at its front edge, swung open toward the road
    door = [box("DoorPanel", (0.55, 0, 0), (1.1, 0.07, 1.45), paint, bevel=0.03, segs=2),
            box("DoorWindow", (0.55, 0.04, 0.32), (0.85, 0.02, 0.55), glass),
            box("DoorHandle", (0.95, 0.06, 0.05), (0.15, 0.03, 0.04), chrome)]
    d = join(door, "Door")
    d.location = (-0.3, 1.17, 1.95)
    d.rotation_euler = (0, 0, math.radians(180 - 70))
    bpy.ops.object.select_all(action="DESELECT")
    d.select_set(True)
    bpy.context.view_layer.objects.active = d
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    P.append(d)
    # steps up to the cab, the mirrors on their arms, the beacon (dead), the stack, the tank
    for z in (0.75, 1.1):
        P.append(box("Step", (-0.9, 1.08, z), (0.55, 0.24, 0.05), steel))
    for y in (-1.0, 1.0):
        P.append(cylinder("MirrorArm", (-0.1, y * 1.12, 2.5), (0.15, y * 1.42, 2.4), 0.018, 0.018, 6, black))
        P.append(cylinder("MirrorArm2", (-0.1, y * 1.12, 1.95), (0.15, y * 1.42, 2.15), 0.018, 0.018, 6, black))
        P.append(box("Mirror", (0.15, y * 1.45, 2.27), (0.05, 0.2, 0.42), black, bevel=0.02, segs=1))
        P.append(box("MirrorGlass", (0.18, y * 1.45, 2.27), (0.01, 0.16, 0.36), chrome))
    P.append(cylinder("Beacon", (-0.95, 0, 2.8), (-0.95, 0, 2.95), 0.13, 0.12, 16, amber))
    P.append(cylinder("BeaconBase", (-0.95, 0, 2.78), (-0.95, 0, 2.82), 0.16, 0.16, 16, black))
    P.append(cylinder("Stack", (-1.95, -0.85, 1.0), (-1.95, -0.85, 3.25), 0.07, 0.07, 12, chrome))
    P.append(cylinder("StackShield", (-1.95, -0.85, 1.9), (-1.95, -0.85, 2.7), 0.1, 0.1, 12, black))
    P.append(box("StackCap", (-1.92, -0.85, 3.27), (0.14, 0.15, 0.03), black, rot=(0, -0.3, 0)))
    P.append(cylinder("Tank", (-0.3, -1.0, 0.78), (-1.6, -1.0, 0.78), 0.28, 0.28, 18, chrome))
    for x in (-0.45, -1.45):
        P.append(cylinder("Strap", (x, -1.0, 0.78), (x - 0.04, -1.0, 0.78), 0.3, 0.3, 18, black))
    # ---- the sander bed: an open box, ribbed, heaped with salt and snow; the spinner under its tail
    P.append(box("BedFloor", (-3.35, 0, 1.08), (2.7, 2.3, 0.1), steel))
    for y in (-1.12, 1.12):
        P.append(box("BedSide", (-3.35, y, 1.6), (2.7, 0.06, 1.0), paint, bevel=0.015, segs=1))
        for i in range(6):
            P.append(box("Rib", (-4.6 + i * 0.5, y * 1.03, 1.6), (0.06, 0.06, 1.0), paint))
        P.append(box("BedTop", (-3.35, y * 1.03, 2.11), (2.75, 0.1, 0.06), paint))
    P.append(box("BedFront", (-2.0, 0, 1.75), (0.08, 2.3, 1.3), paint, bevel=0.015, segs=1))
    P.append(box("Tailgate", (-4.7, 0, 1.6), (0.07, 2.3, 1.0), paint, bevel=0.015, segs=1))
    P.append(cylinder("Spinner", (-4.95, 0, 0.7), (-4.95, 0, 0.74), 0.32, 0.32, 18, black))
    for i in range(4):
        P.append(box("Vane", (-4.95, 0, 0.77), (0.6, 0.03, 0.05), black, rot=(0, 0, i * math.pi / 4)))
    P.append(box("Chute", (-4.85, 0, 0.92), (0.25, 0.4, 0.3), steel, rot=(0, 0.4, 0)))
    for y in (-1.0, 1.0):
        P.append(box("Mudflap", (-4.85, y, 0.55), (0.03, 0.42, 0.55), black))
    # ---- the plow: a curved moldboard angled into the snow, its cutting edge, ribs, the push frame and its rams
    yaw = math.radians(22)
    rotz = Matrix.Rotation(yaw, 4, "Z")
    origin = Vector((3.05, 0.2, 0.0))
    bm = bmesh.new()
    nu, nv = 16, 10
    grid = []
    for i in range(nu + 1):
        row = []
        y = -1.8 + 3.6 * i / nu
        for j in range(nv + 1):
            v = j / nv
            # the moldboard's curve: concave forward, rolling over at the top
            x = 0.32 * math.sin(v * math.pi * 0.75) - 0.12 * v
            row.append(bm.verts.new(origin + rotz @ Vector((x, y, 0.12 + v * 1.15))))
        grid.append(row)
    for i in range(nu):
        for j in range(nv):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    blade = obj_from_bmesh(bm, "Blade", paint)
    add_mod(blade, "SOLIDIFY", thickness=0.05, offset=-1.0)
    apply_mods(blade)
    P.append(blade)
    P.append(box("Edge", origin + rotz @ Vector((0.0, 0, 0.1)), (0.06, 3.6, 0.14), steel, rot=(0, 0, yaw)))
    for i in range(7):
        P.append(box("BladeRib", origin + rotz @ Vector((-0.18, -1.6 + i * 0.53, 0.62)), (0.06, 0.04, 1.0), paint, rot=(0, 0, yaw)))
    P.append(box("BladeBeam", origin + rotz @ Vector((-0.25, 0, 0.55)), (0.1, 3.4, 0.14), paint, rot=(0, 0, yaw)))
    for y in (-0.55, 0.55):
        P.append(cylinder("PushArm", origin + rotz @ Vector((-0.3, y, 0.55)), Vector((2.45, y * 0.8, 0.85)), 0.07, 0.07, 8, black))
    for y in (-0.35, 0.35):
        a = origin + rotz @ Vector((-0.3, y, 0.9))
        b = Vector((2.45, y, 1.05))
        P.append(cylinder("Ram", a, (a + b) * 0.5, 0.055, 0.055, 10, black))
        P.append(cylinder("RamRod", (a + b) * 0.5, b, 0.03, 0.03, 8, chrome))
    # ---- snow: on the roof, the hood, the bed's heap; banked against the blade
    P.append(lumpy_sphere("BedHeap", (-3.35, 0, 1.75), (1.3, 1.05, 0.45), snow, 63, amp=0.12, freq=2.5))
    P.append(lumpy_sphere("Banked", origin + rotz @ Vector((0.55, -0.3, 0.1)), (0.55, 1.7, 0.55), snow, 64, amp=0.18, freq=2.5, flat_bottom=0.0))
    truck = join(P, "Snowplow")
    uv_box(truck, 0.8)
    # the snow lying on it: on everything that faces up (the roof, the hood, the fenders, the steps, the blade's top)
    lying = snow_on_top(truck, "Lying", snow, thick=0.07, min_up=0.75, seed=7)
    uv_box(lying, 1.0)
    truck = join([truck, lying], "Snowplow")
    export([truck], "snowplow")
    preview("snowplow", (-0.6, 0, 1.3), 9.0, 2.0, 50)
    preview("snowplow_cab", (-0.8, 0.8, 1.8), 4.0, 0.4, 25)
