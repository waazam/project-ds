"""The clutter library (the owner, 2026-09-30: more clutter and environmental detail): bottles, jars, glasses, books,
clocks, stools, logs, boots, skis, coats, magazines. Built and exported like the furniture (furniture.py: roles, the
game's box-projected UVs, a baked cavity map), to assets/models/furniture/<name>.glb:

    blender -b --python tools/Blender/clutter.py [-- name ...]
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, apply_mods, add_mod, obj_from_bmesh
import furniture as F
from furniture import role, lathe, block, piping, cushion, finish, join_objs, export

V = Vector
F.ROLE_COLOURS.update({
    "label": (0.75, 0.68, 0.5), "cork": (0.5, 0.36, 0.22), "ceramic": (0.85, 0.83, 0.78), "leather": (0.3, 0.12, 0.08),
    "cloth": (0.3, 0.24, 0.2), "iron": (0.12, 0.12, 0.13), "bark": (0.3, 0.26, 0.22), "wax": (0.9, 0.86, 0.74),
    "face": (0.88, 0.85, 0.76), "rubber": (0.05, 0.05, 0.05), "ski": (0.25, 0.15, 0.08),
})


def bottle(kind):
    """A bottle: wine (tall, shouldered), whiskey (flat-sided, a short neck), liqueur (round, squat); a label, a foil, a
    cap or a cork."""
    clear()
    glass, label, cork = role("glass"), role("label"), role("cork")
    P = []
    if kind == "wine":
        P.append(lathe("Glass", [(0, 0), (0.036, 0), (0.037, 0.005), (0.037, 0.2), (0.034, 0.225), (0.02, 0.255), (0.013, 0.27), (0.013, 0.31), (0.015, 0.315), (0.0, 0.315)], glass, 24))
        P.append(lathe("Label", [(0.0375, 0.07), (0.0375, 0.15)], label, 24))
        P.append(lathe("Foil", [(0.0, 0.29), (0.0145, 0.29), (0.0145, 0.322), (0.0, 0.322)], cork, 16))
    elif kind == "whiskey":
        P.append(block("Glass", (0, 0, 0.11), (0.08, 0.05, 0.22), glass, bevel=0.012, segs=3))
        P.append(lathe("Neck", [(0.02, 0.21), (0.016, 0.24), (0.016, 0.27), (0.0, 0.27)], glass, 16))
        P.append(block("Label", (0, -0.0255, 0.1), (0.06, 0.002, 0.08), label, bevel=0.0))
        P.append(lathe("Cap", [(0.0, 0.26), (0.018, 0.26), (0.018, 0.285), (0.0, 0.285)], cork, 16))
    else:
        P.append(lathe("Glass", [(0, 0), (0.045, 0), (0.05, 0.03), (0.05, 0.09), (0.03, 0.13), (0.012, 0.16), (0.012, 0.19), (0.0, 0.19)], glass, 24))
        P.append(lathe("Label", [(0.0505, 0.035), (0.0505, 0.08)], label, 24))
        P.append(lathe("Cork", [(0.0, 0.18), (0.011, 0.18), (0.013, 0.21), (0.0, 0.21)], cork, 12))
    return P


def jar():
    clear()
    return [lathe("Jar", [(0, 0), (0.042, 0), (0.045, 0.01), (0.045, 0.12), (0.038, 0.135), (0.0, 0.135)], role("glass"), 20),
            lathe("Lid", [(0.0, 0.135), (0.04, 0.135), (0.042, 0.145), (0.0, 0.15)], role("metal"), 20)]


def tin():
    clear()
    return [lathe("Tin", [(0, 0), (0.04, 0), (0.041, 0.005), (0.041, 0.11), (0.04, 0.115), (0.0, 0.115)], role("metal"), 20),
            lathe("Label", [(0.0415, 0.02), (0.0415, 0.09)], role("label"), 20)]


def tumbler():
    clear()
    return [lathe("Glass", [(0, 0), (0.038, 0), (0.04, 0.01), (0.042, 0.09), (0.039, 0.09), (0.037, 0.012), (0.0, 0.012)], role("glass"), 20)]


def wine_glass():
    clear()
    return [lathe("Glass", [(0, 0), (0.035, 0), (0.035, 0.003), (0.004, 0.006), (0.004, 0.09), (0.02, 0.1), (0.038, 0.14), (0.036, 0.17), (0.034, 0.17), (0.035, 0.14), (0.0, 0.105)], role("glass"), 20)]


def mug():
    clear()
    ce = role("ceramic")
    return [lathe("Mug", [(0, 0), (0.04, 0), (0.042, 0.01), (0.042, 0.095), (0.038, 0.095), (0.036, 0.012), (0.0, 0.012)], ce, 20),
            piping("Handle", [(0.042, 0, 0.08), (0.065, 0, 0.075), (0.07, 0, 0.05), (0.062, 0, 0.025), (0.042, 0, 0.02)], 0.007, ce)]


def ashtray():
    clear()
    glass = role("glass")
    P = [lathe("Tray", [(0, 0), (0.07, 0), (0.075, 0.025), (0.06, 0.028), (0.05, 0.012), (0.0, 0.012)], glass, 24)]
    for k in range(3):
        a = k / 3 * math.tau
        P.append(block(f"Rest{k}", (math.cos(a) * 0.066, math.sin(a) * 0.066, 0.026), (0.016, 0.016, 0.006), glass, bevel=0.0, rot=(0, 0, a)))
    P.append(piping("Butt", [(0.0, 0.0, 0.015), (0.045, 0.01, 0.022)], 0.004, role("paper")))
    return P


def book(w, h, d, seed=1):
    """A book standing up (its spine along -X): the text block of pages, the cover's boards proud of it, a rounded spine
    with raised bands."""
    cloth, paper = role("leather"), role("paper")
    P = [block("Pages", (0.004, 0, h / 2), (w - 0.012, d - 0.006, h - 0.01), paper, bevel=0.002, segs=1)]
    for s in (-1, 1):
        P.append(block(f"Board{s}", (0, s * (d / 2 - 0.0015), h / 2), (w, 0.003, h), cloth, bevel=0.0012, segs=1))
    P.append(lathe("Spine", [(0.0, 0.0), (d / 2, 0.0), (d / 2, h), (0.0, h)], cloth, 12, (-w / 2, 0, 0)))
    for k in range(4):
        z = h * (0.15 + k * 0.23)
        P.append(lathe(f"Band{k}", [(d / 2 + 0.002, z - 0.003), (d / 2 + 0.002, z + 0.003)], cloth, 12, (-w / 2, 0, 0)))
    return join_objs(P, "Book")


def place(ob, rot, loc):
    ob.rotation_euler = rot
    ob.location = loc
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True)
    return ob


def books_row(n, seed):
    """A row of books standing on a shelf (along +X, spines toward -Y), of different heights and thicknesses, the last
    leaning."""
    clear()
    rnd = random.Random(seed)
    P = []
    x = 0.0
    for i in range(n):
        h, d, w = rnd.uniform(0.18, 0.28), rnd.uniform(0.025, 0.06), rnd.uniform(0.13, 0.19)
        b = book(w, h, d, seed * 31 + i)
        lean = 0.2 if i == n - 1 else 0.0
        # (spine to -Y: turn it a quarter round; then lean the last one over)
        place(b, (0, -lean, math.pi / 2), (x + d / 2 + (h * math.sin(lean) * 0.5 if lean else 0), 0, 0))
        P.append(b)
        x += d + 0.002
    return P


def books_stack(n, seed):
    """A stack of books lying flat, each turned a little."""
    clear()
    rnd = random.Random(seed)
    P = []
    z = 0.0
    for i in range(n):
        d = rnd.uniform(0.025, 0.05)
        h = rnd.uniform(0.2, 0.28)
        b = book(rnd.uniform(0.15, 0.22), h, d, seed * 17 + i)
        # lying down: its height along Y, its thickness up
        place(b, (math.pi / 2, 0, rnd.uniform(-0.25, 0.25)), (0, -h / 2 * 0, z + d / 2))
        b.location = (0, h / 2, 0)
        bpy.ops.object.transform_apply(location=True)
        P.append(b)
        z += d
    return P


def candlestick():
    clear()
    metal, wax = role("metal"), role("wax")
    return [lathe("Stick", [(0, 0), (0.06, 0), (0.06, 0.008), (0.04, 0.015), (0.015, 0.03), (0.012, 0.12), (0.022, 0.14), (0.012, 0.16), (0.01, 0.22), (0.026, 0.235), (0.026, 0.245), (0.0, 0.245)], metal, 24),
            lathe("Candle", [(0, 0.245), (0.012, 0.245), (0.012, 0.36), (0.009, 0.368), (0.0, 0.37)], wax, 16),
            piping("Drip", [(0.012, 0, 0.36), (0.0135, 0, 0.33), (0.013, 0.002, 0.31)], 0.003, wax)]


def dial(name, r, at, material, ring=None):
    ob = lathe(name, [(0.0, 0.0), (r, 0.0), (r, 0.004), (0.0, 0.004)], material, 40)
    return place(ob, (math.pi / 2, 0, 0), at)


def mantel_clock():
    """A bracket clock: a domed case on a plinth, a white dial in a brass bezel, its hands, brass feet."""
    clear()
    wood, face, metal, black = role("wood"), role("face"), role("metal"), role("black")
    P = [block("Plinth", (0, 0, 0.02), (0.3, 0.14, 0.04), wood, bevel=0.006, segs=2),
         block("Case", (0, 0, 0.17), (0.26, 0.12, 0.26), wood, bevel=0.008, segs=2)]
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=0.13, radius2=0.13, depth=0.12)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
    bmesh.ops.translate(bm, vec=V((0, 0, 0.3)), verts=bm.verts)
    P.append(finish(obj_from_bmesh(bm, "Dome", wood), True, 0.006, 2))
    P.append(dial("Dial", 0.085, (0, -0.061, 0.19), face))
    P.append(dial("Bezel", 0.092, (0, -0.06, 0.19), metal))
    P.append(block("HandH", (0.0, -0.068, 0.21), (0.006, 0.002, 0.045), black, bevel=0.0))
    P.append(block("HandM", (0.022, -0.068, 0.19), (0.06, 0.002, 0.005), black, bevel=0.0, rot=(0, 0.3, 0)))
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(lathe(f"Foot{sx}{sy}", [(0, 0), (0.015, 0), (0.012, 0.012), (0, 0.015)], metal, 12, (sx * 0.13, sy * 0.055, -0.012)))
    return P


def grandfather_clock():
    """A longcase clock: a plinth, a long trunk with its door and glazed lenticle, the hood with its columns, a broken-arch
    pediment and finials; the dial, its numerals' ring, the hands."""
    clear()
    wood, face, metal, black, glass = role("wood"), role("face"), role("metal"), role("black"), role("glass")
    P = [block("Plinth", (0, 0, 0.2), (0.55, 0.4, 0.4), wood, bevel=0.012, segs=2),
         block("PlinthCap", (0, 0, 0.41), (0.58, 0.43, 0.03), wood, bevel=0.006, segs=2),
         block("Trunk", (0, 0, 0.98), (0.44, 0.3, 1.12), wood, bevel=0.01, segs=2),
         block("TrunkDoor", (0, -0.155, 1.0), (0.32, 0.02, 0.9), wood, bevel=0.012, segs=3),
         block("TrunkCap", (0, 0, 1.56), (0.5, 0.36, 0.04), wood, bevel=0.006, segs=2),
         block("Hood", (0, 0, 1.82), (0.48, 0.34, 0.48), wood, bevel=0.01, segs=2)]
    P.append(dial("Lenticle", 0.05, (0, -0.166, 1.1), glass))
    for sx in (-1, 1):
        P.append(lathe(f"Column{sx}", [(0.018, 0), (0.016, 0.42), (0.022, 0.44), (0.0, 0.45)], metal, 12, (sx * 0.21, -0.15, 1.6)))
    # the pediment: a board over the hood, its top a broken arch (two scrolls rising to a gap at the middle)
    P.append(block("Pediment", (0, -0.1, 2.1), (0.5, 0.06, 0.1), wood, bevel=0.008, segs=2))
    for sx in (-1, 1):
        P.append(piping(f"Scroll{sx}", [(sx * (0.24 - 0.2 * i / 10), -0.13, 2.15 + 0.09 * math.sin(math.pi * 0.5 * i / 10)) for i in range(11)], 0.03, wood))
    for x, z in ((-0.22, 2.07), (0.0, 2.15), (0.22, 2.07)):
        P.append(lathe(f"Finial{x}", [(0, 0), (0.025, 0), (0.03, 0.03), (0.012, 0.06), (0.018, 0.08), (0, 0.1)], metal, 12, (x, -0.1, z)))
    P.append(dial("Dial", 0.16, (0, -0.172, 1.8), face))
    P.append(dial("Ring", 0.145, (0, -0.175, 1.8), black))
    P.append(dial("RingIn", 0.13, (0, -0.177, 1.8), face))
    P.append(block("HourHand", (0.03, -0.181, 1.83), (0.07, 0.003, 0.008), black, bevel=0.0, rot=(0, 0.7, 0)))
    P.append(block("MinuteHand", (-0.01, -0.182, 1.86), (0.008, 0.003, 0.12), black, bevel=0.0, rot=(0, -0.15, 0)))
    return P


def bar_stool():
    """A bar stool: a piped leather seat on a turned brass column, a foot ring on spokes, a domed base."""
    clear()
    metal, leather = role("metal"), role("upholstery")
    P = [lathe("Base", [(0, 0), (0.2, 0), (0.19, 0.02), (0.12, 0.045), (0.04, 0.06), (0, 0.065)], metal, 32),
         lathe("Column", [(0, 0.06), (0.03, 0.06), (0.025, 0.66), (0.06, 0.7), (0.0, 0.71)], metal, 16),
         lathe("Ring", [(0.17, 0.3), (0.19, 0.3), (0.19, 0.32), (0.17, 0.32)], metal, 32)]
    for k in range(4):
        a = k / 4 * math.tau + 0.4
        P.append(piping(f"Spoke{k}", [(0.0, 0.0, 0.31), (math.cos(a) * 0.18, math.sin(a) * 0.18, 0.31)], 0.008, metal))
    P.append(lathe("Seat", [(0, 0.7), (0.19, 0.7), (0.205, 0.73), (0.2, 0.78), (0.15, 0.8), (0.0, 0.805)], leather, 32))
    P.append(lathe("Pipe", [(0.2, 0.774), (0.209, 0.778), (0.2, 0.782)], leather, 32))
    return P


def log_pile():
    """Split logs stacked in a cradle of iron hoops by the hearth."""
    clear()
    bark, wood, iron = role("bark"), role("wood"), role("iron")
    rnd = random.Random(77)
    P = []
    for row in range(3):
        for i in range(4 - row):
            x, z = -0.27 + i * 0.18 + row * 0.09, 0.1 + row * 0.15
            r = rnd.uniform(0.065, 0.085)
            P.append(place(lathe(f"Log{row}{i}", [(0, -0.28), (r, -0.28), (r * 1.02, 0.0), (r * 0.98, 0.28), (0, 0.28)], bark, 10), (math.pi / 2, rnd.uniform(-0.2, 0.2), 0), (x, 0, z)))
            for e in (-1, 1):
                P.append(place(lathe(f"End{row}{i}{e}", [(0, 0), (r * 0.95, 0), (r * 0.95, 0.004), (0, 0.004)], wood, 10), (math.pi / 2, 0, 0), (x, e * 0.283, z)))
    for y in (-0.22, 0.22):
        P.append(piping(f"Hoop{y}", [(-0.4 + 0.8 * i / 16, y, 0.02 + 0.45 * math.sin(math.pi * i / 16) ** 0.6) for i in range(17)], 0.012, iron))
    for x in (-0.4, 0.4):
        P.append(piping(f"Rail{x}", [(x, -0.25, 0.02), (x, 0.25, 0.02)], 0.012, iron))
    return P


def boots():
    """A pair of snow boots: the shaft and the foot moulded round (rubber below, laced leather above), a thick lugged
    sole, a felt cuff turned over; one stood, one tipped over on its side."""
    clear()
    rubber, leather, cloth = role("rubber"), role("leather"), role("cloth")
    from kit import skin_tree
    P = []
    for k, (x, tip) in enumerate(((-0.08, 0.0), (0.14, 1.4))):
        parts = []
        # the boot in one piece: a curve from the top of the shaft down through the ankle and forward along the foot to the
        # toe (an L), leather above the ankle, rubber below
        bp = [V((0, -0.04, 0.33)), V((0, -0.04, 0.22)), V((0, -0.035, 0.12)), V((0, -0.02, 0.07)), V((0, 0.05, 0.055)), V((0, 0.13, 0.05)), V((0, 0.19, 0.045))]
        br = [(0.058, 0.064), (0.052, 0.058), (0.056, 0.062), (0.06, 0.06), (0.058, 0.05), (0.052, 0.044), (0.04, 0.034)]
        boot = skin_tree(f"Boot{k}", bp, [(i, i + 1) for i in range(len(bp) - 1)], br, leather, subdiv=2)
        boot.data.materials.append(rubber)
        for pg in boot.data.polygons:
            c = sum((boot.data.vertices[v].co for v in pg.vertices), V()) / len(pg.vertices)
            if c.z < 0.1:
                pg.material_index = 1
        parts.append(boot)
        parts.append(block(f"Sole{k}", (0, 0.06, 0.012), (0.11, 0.3, 0.024), rubber, bevel=0.01, segs=2))
        for j in range(6):
            parts.append(block(f"Lug{k}{j}", (0, -0.07 + j * 0.05, 0.002), (0.1, 0.018, 0.008), rubber, bevel=0.0))
        parts.append(lathe(f"Cuff{k}", [(0.06, 0.3), (0.075, 0.305), (0.078, 0.33), (0.07, 0.345), (0.058, 0.345)], cloth, 20, (0, -0.04, 0)))
        for j in range(5):
            z = 0.09 + j * 0.045
            parts.append(piping(f"Lace{k}{j}", [(-0.028, 0.02, z), (0.028, 0.022, z + 0.012)], 0.003, cloth))
        b = join_objs(parts, f"Boot{k}")
        P.append(place(b, (0, tip, 0.25 * k), (x, 0, 0.06 if tip else 0)))
    return P


def skis():
    """A pair of old wooden skis with their bindings and two bamboo poles, leant together against a wall (toward +Y)."""
    clear()
    ski, metal, leather, bark = role("ski"), role("metal"), role("leather"), role("bark")
    P = []
    for k, x in enumerate((-0.05, 0.05)):
        bm = bmesh.new()
        rows = []
        for i in range(31):
            t = i / 30
            y = 0.0 if t < 0.9 else -0.08 * ((t - 0.9) / 0.1) ** 2
            w = 0.035 if t < 0.92 else 0.035 * (1 - (t - 0.92) / 0.08 * 0.6)
            rows.append([bm.verts.new((x - w, y, t * 1.9)), bm.verts.new((x + w, y, t * 1.9))])
        for i in range(30):
            bm.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
        sk = obj_from_bmesh(bm, f"Ski{k}", ski)
        add_mod(sk, "SOLIDIFY", thickness=0.018)
        P.append(finish(sk, True, 0.003, 1))
        P.append(block(f"Binding{k}", (x, -0.02, 0.85), (0.07, 0.03, 0.12), metal, bevel=0.004, segs=1))
        P.append(block(f"Strap{k}", (x, -0.025, 0.95), (0.08, 0.02, 0.03), leather, bevel=0.004, segs=1))
    for k, x in enumerate((0.16, 0.22)):
        P.append(lathe(f"Pole{k}", [(0, 0), (0.008, 0.02), (0.01, 1.3), (0.014, 1.33), (0.014, 1.45), (0, 1.47)], bark, 10, (x, -0.05, 0)))
        P.append(lathe(f"Basket{k}", [(0.0, 0.1), (0.06, 0.1), (0.06, 0.11), (0.0, 0.11)], leather, 16, (x, -0.05, 0)))
    for o in P:
        place(o, (-0.12, 0, 0), (0, 0, 0))
    return P


def coat_on_hook():
    """A heavy wool coat hung by its collar on an iron hook: the hook at the origin (on the wall, the coat hanging toward
    -Y off it), the body in folds, a sleeve, the collar."""
    clear()
    cloth, iron = role("cloth"), role("iron")
    P = [piping("Hook", [(0, 0.0, 0.0), (0, -0.05, 0.0), (0, -0.07, 0.03)], 0.006, iron)]
    bm = bmesh.new()
    nx, nz = 18, 16
    grid = []
    for j in range(nz + 1):
        t = j / nz
        width = 0.18 + 0.12 * min(1.0, t * 3)
        row = []
        for i in range(nx + 1):
            u = i / nx
            a = (u - 0.5) * math.pi * 0.9
            row.append(bm.verts.new((math.sin(a) * width, -0.06 - math.cos(a) * 0.1 + 0.015 * math.sin(u * 22 + t * 3) * t, -0.02 - t * 0.95)))
        grid.append(row)
    for j in range(nz):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    coat = obj_from_bmesh(bm, "Coat", cloth)
    add_mod(coat, "SOLIDIFY", thickness=0.02)
    P.append(finish(coat, True, 0, 1, subsurf=1))
    P.append(piping("Sleeve", [(0.24, -0.08, -0.12), (0.27, -0.1, -0.4), (0.25, -0.09, -0.62)], 0.045, cloth))
    P.append(cushion("Collar", (0, -0.07, -0.03), (0.22, 0.12, 0.06), cloth, puff=0.3))
    return P


def magazines():
    """A few magazines and a newspaper, fanned on a table."""
    clear()
    paper = role("paper")
    rnd = random.Random(5)
    P = []
    for k in range(4):
        w, d = (0.21, 0.28) if k < 3 else (0.3, 0.42)
        bm = bmesh.new()
        rows = [[bm.verts.new((-w / 2 + w * i / 4, -d / 2, 0.0)), bm.verts.new((-w / 2 + w * i / 4, d / 2, 0.0))] for i in range(5)]
        for i in range(4):
            bm.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
        m_ = obj_from_bmesh(bm, f"Mag{k}", paper)
        add_mod(m_, "SOLIDIFY", thickness=0.004 if k < 3 else 0.008)
        apply_mods(m_)
        P.append(place(m_, (0, 0, rnd.uniform(-0.6, 0.6)), (rnd.uniform(-0.12, 0.12), rnd.uniform(-0.08, 0.08), 0.005 + k * 0.006)))
    return P


CLUTTER = {
    "bottle_wine": (lambda: bottle("wine"), (0, 0, 0.15), 0.8, 0.1, 30),
    "bottle_whiskey": (lambda: bottle("whiskey"), (0, 0, 0.13), 0.7, 0.1, 30),
    "bottle_liqueur": (lambda: bottle("liqueur"), (0, 0, 0.1), 0.6, 0.1, 30),
    "jar": (jar, (0, 0, 0.07), 0.5, 0.1, 30),
    "tin": (tin, (0, 0, 0.06), 0.5, 0.1, 30),
    "tumbler": (tumbler, (0, 0, 0.05), 0.4, 0.1, 30),
    "wine_glass": (wine_glass, (0, 0, 0.09), 0.5, 0.1, 30),
    "mug": (mug, (0, 0, 0.05), 0.4, 0.1, 30),
    "ashtray": (ashtray, (0, 0, 0.02), 0.4, 0.2, 30),
    "books_row_a": (lambda: books_row(9, 3), (0.2, 0, 0.12), 0.9, 0.1, 20),
    "books_row_b": (lambda: books_row(6, 8), (0.15, 0, 0.12), 0.8, 0.1, 20),
    "books_stack": (lambda: books_stack(4, 11), (0, 0, 0.08), 0.7, 0.3, 30),
    "candlestick": (candlestick, (0, 0, 0.18), 0.7, 0.1, 30),
    "mantel_clock": (mantel_clock, (0, 0, 0.2), 0.9, 0.1, 20),
    "grandfather_clock": (grandfather_clock, (0, 0, 1.1), 3.2, 0.3, 25),
    "bar_stool": (bar_stool, (0, 0, 0.4), 1.8, 0.4, 30),
    "log_pile": (log_pile, (0, 0, 0.25), 1.6, 0.6, 30),
    "boots": (boots, (0, 0, 0.15), 1.0, 0.3, 30),
    "skis": (skis, (0, 0, 0.9), 2.8, 0.2, 30),
    "coat_on_hook": (coat_on_hook, (0, -0.1, -0.5), 1.8, 0.0, 30),
    "magazines": (magazines, (0, 0, 0.02), 0.9, 0.5, 30),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(CLUTTER)
    for name in want:
        fn, tgt, dist, h, yaw = CLUTTER[name]
        export(fn(), name, tgt, dist, h, yaw)
