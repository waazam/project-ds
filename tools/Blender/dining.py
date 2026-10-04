"""The lodge's dining hall, Act 23's six sheeted tables and what's under them (the owner, 2026-10-04: "the models
underneath the sheets look pretty bad now compared to all the enhanced overhauls ... increase their texture resolutions
and polygon density so they really pop"). Built and exported like the furniture (furniture.py: roles, the game's
box-projected UVs, a baked cavity map), to assets/models/furniture/<name>.glb:

    blender -b --python tools/Blender/dining.py [-- name ...]

  dining_table        the long table (7.2 x 1.3 m, 0.76 high): a top of planks with breadboard ends and a moulded
                      edge, a beaded apron, four heavy turned legs, a long stretcher
  dining_top_half     one half of its top, broken off at the middle in a jagged split (the fifth sheet: it comes down)
  dining_leg          one of its legs (kicked out from under it)
  setting_meat        a dinner plate of something left for weeks: a rotted joint, its bone, mould; knife, fork, a glass
                      with the wine dried in it, a napkin dropped in a heap
  setting_soup        a soup plate on its dinner plate, a skin of something dried in it, the spoon; a glass on its
                      side; a crust gone green
  candelabrum         three branches in silver, the candles burnt down to stubs and their wax run down over it
  carcass_dish        a serving dish, and on it what's left of a roast bird: the keel and the ribs, picked clean, mould
  skeleton_headless   a whole skeleton laid out on its back, the skull gone, the neck snapped
  silver_platter      an oval platter, gadrooned rim and handles (the wendigo's head is served on it)
  cloche              its dome cover
  soup_tureen_bowl    a footed porcelain bowl (the snow, and 201's keycard standing in it)
And the pantry's shelves (the owner: "whatever items are on the shelves look horrible"):
  pantry_jars         a row of preserving jars, fruit gone dark and pickles in brine, behind thick glass
  pantry_tins         ribbed tins with paper labels, a couple stacked, one tipped over
  crock               a stoneware crock with its lid
  flour_sack          a burlap sack, half full and slumped, tied with twine

Blender's Z up, the base on z = 0, built with the front toward -Y (the export mirrors it to the game's -Z).
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix, Euler

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, apply_mods, add_mod, obj_from_bmesh, fbm, lumpy_sphere, skin_tree
import furniture as F
from furniture import role, lathe, block, piping, moulding, turned_leg, finish, join_objs, export

V = Vector
F.ROLE_COLOURS.update({
    "silver": (0.75, 0.75, 0.78), "ceramic": (0.88, 0.86, 0.8), "food": (0.25, 0.1, 0.06), "mould": (0.55, 0.6, 0.45),
    "bone": (0.82, 0.76, 0.62), "wax": (0.9, 0.86, 0.74), "linen": (0.82, 0.8, 0.74), "wine": (0.2, 0.02, 0.03),
    "glass": (0.2, 0.22, 0.2), "jarglass": (0.5, 0.58, 0.55), "zinc": (0.5, 0.5, 0.52), "label": (0.75, 0.68, 0.5), "cork": (0.5, 0.36, 0.22), "preserve": (0.35, 0.05, 0.08),
    "brine": (0.55, 0.55, 0.3), "pickle": (0.3, 0.35, 0.12), "stoneware": (0.7, 0.62, 0.5), "glaze": (0.2, 0.25, 0.4), "sack": (0.55, 0.45, 0.3),
    "twine": (0.5, 0.42, 0.3), "meat": (0.25, 0.08, 0.05), "sinew": (0.6, 0.52, 0.38),
})
F.BUDGET.update({"dining_table": 14000, "dining_top_half": 6000, "dining_leg": 900, "setting_meat": 3600, "setting_soup": 3200,
                 "candelabrum": 4000, "carcass_dish": 5000, "skeleton_headless": 22000, "silver_platter": 3000, "cloche": 1600,
                 "soup_tureen_bowl": 1400, "pantry_jars": 5000, "pantry_tins": 9000, "crock": 1500, "flour_sack": 3000, "pantry_bottles": 3500, "pantry_cans": 9500})

L, W, H = 7.2, 1.3, 0.76
TOP = 0.055


def lathe_mod(name, profile, material, segs, mod):
    """A turned piece whose rings are pushed in and out round the axis: mod(theta, ring index) -> radius scale (a
    gadrooned rim, a fluted cup)."""
    bm = bmesh.new()
    rings = []
    for k, (r, z) in enumerate(profile):
        ring = []
        for i in range(segs):
            a = i / segs * math.tau
            rr = r * mod(a, k)
            ring.append(bm.verts.new((math.cos(a) * rr, math.sin(a) * rr, z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segs):
            bm.faces.new((rings[k][i], rings[k][(i + 1) % segs], rings[k + 1][(i + 1) % segs], rings[k + 1][i]))
    for ring, (r, z) in ((rings[0], profile[0]), (rings[-1], profile[-1])):
        if r > 1e-4:
            c = bm.verts.new((0, 0, z))
            for i in range(segs):
                bm.faces.new((ring[i], ring[(i + 1) % segs], c))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name, material)


def scaled(ob, s, loc=(0, 0, 0), rot=(0, 0, 0)):
    ob.scale = s
    ob.rotation_euler = rot
    ob.location = loc
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def moved(ob, loc=(0, 0, 0), rot=(0, 0, 0)):
    return scaled(ob, (1, 1, 1), loc, rot)


def tube(name, pts, radii, material, subdiv=1):
    """A bone, a rib, a candle's branch: a chain of points skinned (radii per point: r, or (rx, ry))."""
    return skin_tree(name, [V(p) for p in pts], [(i, i + 1) for i in range(len(pts) - 1)], radii, material, subdiv)


# ------------------------------------------------------------------ the table

def plank_top(x0, x1, material, rnd, broken_end=None):
    """The top from x0 to x1: planks along it (their joints a hair apart, each a little uneven), breadboard ends across
    whichever ends are whole; broken_end: the end (x) it's split at, the planks ending raggedly there."""
    P = []
    n = 7
    pw = W / n
    for j in range(n):
        y = -W / 2 + pw * (j + 0.5)
        a, b = x0, x1
        if broken_end is not None:
            # (a jagged split: each plank broken off at its own length, splinters standing out of it)
            jag = rnd.uniform(-0.12, 0.12)
            if broken_end > x0:
                b = broken_end + jag
            else:
                a = broken_end + jag
        ends_whole = [e for e in (x0, x1) if broken_end is None or abs(e - broken_end) > 1e-3]
        inset = 0.1
        if x0 in ends_whole:
            a = max(a, x0 + inset)
        if x1 in ends_whole:
            b = min(b, x1 - inset)
        P.append(block(f"Plank{j}", ((a + b) / 2, y, H - TOP / 2 + rnd.uniform(-0.0015, 0.0015)), (b - a, pw - 0.004, TOP), material, bevel=0.004, segs=2))
        if broken_end is not None:
            e = b if broken_end > x0 else a
            sg = 1 if broken_end > x0 else -1
            for k in range(3):
                yy = y + rnd.uniform(-pw * 0.4, pw * 0.4)
                ln = rnd.uniform(0.04, 0.16)
                P.append(block(f"Splinter{j}{k}", (e + sg * ln / 2, yy, H - TOP * rnd.uniform(0.2, 0.8)), (ln, rnd.uniform(0.006, 0.02), rnd.uniform(0.006, 0.02)),
                               material, bevel=0.0, rot=(0, rnd.uniform(-0.2, 0.2), rnd.uniform(-0.15, 0.15))))
    for e in (x0, x1):
        if broken_end is not None and abs(e - broken_end) < 1e-3:
            continue
        sg = 1 if e == x0 else -1
        P.append(block(f"Breadboard{e}", (e + sg * 0.05, 0, H - TOP / 2), (0.1, W, TOP), material, bevel=0.006, segs=2))
        for j in range(3):
            P.append(lathe(f"Peg{e}{j}", [(0.0, H + 0.0005), (0.008, H + 0.0005), (0.008, H - 0.01), (0.0, H - 0.01)], material, 8, (e + sg * 0.05, -W / 3 + j * W / 3, 0)))
    # the moulded edge under the top, along its sides (and its whole ends)
    prof = [(0.0, 0.0), (0.01, -0.005), (0.014, -0.014), (0.006, -0.022), (-0.004, -0.024)]
    for sy in (-1, 1):
        pts = [(x, sy * (W / 2 - 0.004), H - TOP) for x in (x0 + 0.004, x1 - 0.004)]
        if sy > 0:
            pts = pts[::-1]
        P.append(moulding(f"Edge{sy}", pts, prof, material))
    return P


def apron(x0, x1, material):
    P = []
    ax, ay = L / 2 - 0.08, W / 2 - 0.08
    xa, xb = max(x0, -ax + 0.05), min(x1, ax - 0.05)
    for sy in (-1, 1):
        P.append(block(f"Apron{sy}", ((xa + xb) / 2, sy * ay, H - TOP - 0.07), (xb - xa, 0.03, 0.14), material, bevel=0.004, segs=1))
        P.append(piping(f"Bead{sy}", [(xa, sy * (ay + 0.017), H - TOP - 0.13), (xb, sy * (ay + 0.017), H - TOP - 0.13)], 0.006, material))
    for ex in (-ax, ax):
        if x0 - 0.01 <= ex <= x1 + 0.01:
            P.append(block(f"ApronEnd{ex}", (ex, 0, H - TOP - 0.07), (0.03, W - 0.2, 0.14), material, bevel=0.004, segs=1))
    return P


def table_leg(name, x, y, material):
    """A heavy turned leg, from the apron to the floor: the square block where the apron's tenoned in, a long vase,
    rings, a tapered foot on a little pad."""
    h = H - TOP
    r = 0.045
    prof = [(0.0, 0.0), (r * 0.7, 0.0), (r * 0.75, 0.012), (r * 0.6, 0.03), (r * 0.66, 0.07), (r * 0.95, 0.2), (r * 1.1, 0.27), (r * 1.0, 0.31),
            (r * 0.7, 0.35), (r * 0.62, 0.37), (r * 0.9, 0.39), (r * 0.62, 0.41), (r * 0.78, 0.44), (r * 0.62, 0.47), (r * 0.66, h - 0.17), (r * 0.66, h - 0.16)]
    ob = lathe(name, prof, material, 24, (x, y, 0))
    blk = block(name + "Blk", (x, y, h - 0.085), (r * 1.6, r * 1.6, 0.17), material, bevel=0.006, segs=2)
    return join_objs([ob, blk], name)


def dining_table():
    clear()
    wood = role("wood")
    rnd = random.Random(7)
    P = plank_top(-L / 2, L / 2, wood, rnd) + apron(-L / 2, L / 2, wood)
    ax, ay = L / 2 - 0.08, W / 2 - 0.08
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(table_leg(f"Leg{sx}{sy}", sx * ax, sy * ay, wood))
        # an H stretcher low between each end's legs, and the long one down the middle
        P.append(block(f"EndStretcher{sx}", (sx * ax, 0, 0.16), (0.05, W - 0.16, 0.06), wood, bevel=0.008, segs=2))
    P.append(block("LongStretcher", (0, 0, 0.16), (L - 0.16, 0.06, 0.05), wood, bevel=0.008, segs=2))
    return P


def dining_top_half():
    """The half from the split (x = 0) out to its end (+X), the apron's half with it."""
    clear()
    wood = role("wood")
    rnd = random.Random(11)
    P = plank_top(0.0, L / 2, wood, rnd, broken_end=0.0) + apron(0.02, L / 2, wood)
    # (in the half's own space: the top's surface at z = 0, as the game hangs it)
    return [moved(join_objs(P, "Half"), (0, 0, -H))]


def dining_leg():
    clear()
    return [table_leg("Leg", 0, 0, role("wood"))]


# ------------------------------------------------------------------ on the table

PLATE = [(0.0, 0.004), (0.075, 0.004), (0.078, 0.0), (0.084, 0.0), (0.088, 0.006), (0.13, 0.017), (0.136, 0.021), (0.134, 0.024),
         (0.126, 0.021), (0.087, 0.0105), (0.0, 0.0105)]


def plate(name, x=0, y=0, z=0, s=1.0):
    return lathe(name, [(r * s, h * s + z) for r, h in PLATE], role("ceramic"), 48, (x, y, 0))


def fork(name, x, y, z, yaw):
    m = role("silver")
    P = [tube(name + "H", [(0, -0.1, 0.004), (0, -0.02, 0.003), (0, 0.04, 0.006)], [(0.006, 0.002), (0.004, 0.0015), (0.005, 0.0015)], m)]
    for k in range(4):
        dx = (k - 1.5) * 0.0045
        P.append(tube(name + f"T{k}", [(dx * 0.6, 0.04, 0.006), (dx, 0.07, 0.008), (dx, 0.09, 0.009)], [0.0013, 0.0012, 0.0008], m, 0))
    return moved(join_objs(P, name), (x, y, z), (0, 0, yaw))


def knife(name, x, y, z, yaw):
    m = role("silver")
    bm = bmesh.new()
    pts = [(-0.008, 0.0), (0.008, 0.0), (0.009, 0.08), (0.004, 0.11), (-0.008, 0.115)]
    top = [bm.verts.new((px, py, 0.0025)) for px, py in pts]
    bot = [bm.verts.new((px, py, 0.0)) for px, py in pts]
    bm.faces.new(top)
    bm.faces.new(bot[::-1])
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    blade = obj_from_bmesh(bm, name + "B", m, smooth=False)
    handle = tube(name + "H", [(0, -0.1, 0.006), (0, -0.005, 0.005)], [(0.007, 0.004), (0.006, 0.004)], m)
    bol = lathe(name + "Bol", [(0.0, -0.004), (0.006, -0.004), (0.006, 0.004), (0.0, 0.004)], m, 10)
    bol = scaled(bol, (1, 1, 1), (0, 0, 0.005), (math.pi / 2, 0, 0))
    return moved(join_objs([blade, handle, bol], name), (x, y, z), (0, 0, yaw))


def spoon(name, x, y, z, yaw):
    m = role("silver")
    h = tube(name + "H", [(0, -0.1, 0.006), (0, -0.01, 0.004), (0, 0.02, 0.006)], [(0.005, 0.0015), (0.0035, 0.0015), (0.004, 0.0015)], m)
    bowl = lathe(name + "Bowl", [(0.0, 0.0), (0.012, 0.001), (0.02, 0.005), (0.022, 0.008), (0.018, 0.008), (0.01, 0.004), (0.0, 0.003)], m, 16)
    bowl = scaled(bowl, (0.75, 1.15, 1.0), (0, 0.045, 0.002))
    return moved(join_objs([h, bowl], name), (x, y, z), (0, 0, yaw))


def wine_glass(name, x, y, z=0.0, rot=(0, 0, 0), dregs=True):
    g = lathe(name, [(0, 0), (0.035, 0), (0.035, 0.003), (0.004, 0.006), (0.004, 0.09), (0.02, 0.1), (0.038, 0.14), (0.036, 0.17),
                     (0.034, 0.17), (0.035, 0.14), (0.018, 0.103), (0.0, 0.1)], role("glass"), 24)
    P = [g]
    if dregs:
        P.append(lathe(name + "Dregs", [(0.0, 0.103), (0.02, 0.105), (0.026, 0.115), (0.0, 0.112)], role("wine"), 20))
    return moved(join_objs(P, name), (x, y, z), rot)


def napkin(name, x, y, z, seed):
    """A napkin dropped in a heap: a cloth square crumpled and slumped."""
    bm = bmesh.new()
    n = 14
    s = 0.36
    rnd = random.Random(seed)
    grid = [[None] * (n + 1) for _ in range(n + 1)]
    for i in range(n + 1):
        for j in range(n + 1):
            u, v = i / n - 0.5, j / n - 0.5
            # gathered toward the middle, heaped up, folds through it
            d = math.hypot(u, v)
            px, py = u * s * (0.45 + 0.4 * d), v * s * (0.45 + 0.4 * d)
            pz = 0.05 * max(0.0, 1 - d * 1.6) + 0.018 * fbm(u * 4 + seed, v * 4, 0.3, 3) + 0.012 * math.sin(u * 19 + v * 7)
            grid[i][j] = bm.verts.new((px, py, max(pz, 0.002)))
    for i in range(n):
        for j in range(n):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    ob = obj_from_bmesh(bm, name, role("linen"))
    add_mod(ob, "SOLIDIFY", thickness=0.003)
    apply_mods(ob)
    return moved(ob, (x, y, z), (0, 0, rnd.uniform(0, 6.28)))


def rot_meat(name, x, y, z, seed):
    """A joint left on its plate for weeks: the meat shrunk and blackened round its bone, slumped into a skin of its own
    juices, mould furred over it."""
    P = [lumpy_sphere(name + "M", (x, y, z + 0.022), (0.065, 0.045, 0.028), role("food"), seed, amp=0.12, freq=3.5, subdiv=3, flat_bottom=z + 0.004)]
    P.append(tube(name + "Bone", [(x - 0.07, y + 0.01, z + 0.03), (x + 0.02, y, z + 0.035), (x + 0.085, y - 0.015, z + 0.03)], [0.009, 0.007, 0.011], role("bone")))
    P.append(lathe(name + "Juice", [(0.0, z + 0.0005), (0.085, z + 0.0005), (0.088, z + 0.0015), (0.0, z + 0.002)], role("food"), 24, (x, y, 0)))
    rnd = random.Random(seed)
    for k in range(9):
        a = rnd.uniform(0, 6.28)
        P.append(lumpy_sphere(name + f"Mould{k}", (x + math.cos(a) * rnd.uniform(0.0, 0.05), y + math.sin(a) * rnd.uniform(0.0, 0.035), z + 0.04 + rnd.uniform(-0.01, 0.005)),
                              (rnd.uniform(0.008, 0.02), rnd.uniform(0.008, 0.02), 0.006), role("mould"), seed * 10 + k, amp=0.2, freq=5, subdiv=2))
    return P


def setting_meat():
    clear()
    P = [plate("Plate"), *rot_meat("Joint", 0.0, 0.01, 0.0105, 3)]
    P.append(fork("Fork", -0.17, -0.02, 0.0, 0.05))
    P.append(knife("Knife", 0.17, -0.03, 0.0, -0.08))
    P.append(spoon("Spoon", 0.2, -0.02, 0.0, -0.04))
    P.append(wine_glass("Glass", 0.2, 0.17))
    P.append(napkin("Napkin", -0.25, 0.15, 0.0, 4))
    return P


def setting_soup():
    clear()
    P = [plate("Plate")]
    P.append(lathe("SoupPlate", [(0.0, 0.0105), (0.05, 0.0105), (0.052, 0.008), (0.056, 0.008), (0.06, 0.012), (0.085, 0.04), (0.11, 0.046), (0.113, 0.049),
                                 (0.108, 0.049), (0.082, 0.044), (0.058, 0.017), (0.0, 0.017)], role("ceramic"), 40))
    P.append(lathe("Skin", [(0.0, 0.026), (0.068, 0.026), (0.071, 0.028), (0.0, 0.028)], role("food"), 32))
    rnd = random.Random(21)
    for k in range(6):
        a = rnd.uniform(0, 6.28)
        P.append(lumpy_sphere(f"Fur{k}", (math.cos(a) * rnd.uniform(0.0, 0.05), math.sin(a) * rnd.uniform(0.0, 0.05), 0.028), (0.015, 0.012, 0.004), role("mould"), 40 + k, amp=0.25, freq=5, subdiv=2))
    P.append(spoon("Spoon", 0.15, 0.02, 0.0, 0.3))
    P.append(fork("Fork", -0.17, -0.01, 0.0, -0.06))
    P.append(knife("Knife", 0.19, -0.05, 0.0, 0.02))
    # a glass on its side, its dregs dried along it
    P.append(wine_glass("Glass", -0.16, 0.2, 0.038, (0, math.pi / 2, 0.7), dregs=False))
    P.append(lumpy_sphere("Spill", (-0.05, 0.25, 0.0005), (0.09, 0.05, 0.0015), role("wine"), 9, amp=0.25, freq=3, subdiv=3))
    # a crust of bread gone green
    P.append(lumpy_sphere("Crust", (0.05, 0.22, 0.02), (0.055, 0.03, 0.022), role("food"), 31, amp=0.12, freq=3, subdiv=3, flat_bottom=0.002))
    for k in range(5):
        P.append(lumpy_sphere(f"Green{k}", (0.03 + k * 0.012, 0.22 + rnd.uniform(-0.015, 0.015), 0.04), (0.012, 0.01, 0.005), role("mould"), 50 + k, amp=0.25, freq=5, subdiv=2))
    return P


def candelabrum():
    clear()
    s, wax = role("silver"), role("wax")
    P = [lathe("Base", [(0.0, 0.0), (0.075, 0.0), (0.078, 0.008), (0.07, 0.016), (0.05, 0.022), (0.03, 0.04), (0.02, 0.06), (0.026, 0.07), (0.016, 0.08),
                        (0.014, 0.2), (0.024, 0.21), (0.014, 0.22), (0.014, 0.26), (0.0, 0.26)], s, 32)]
    cups = [(0.0, 0.0, 0.33)]
    for sx in (-1, 1):
        P.append(tube(f"Arm{sx}", [(0, 0, 0.23), (sx * 0.06, 0, 0.225), (sx * 0.12, 0, 0.25), (sx * 0.14, 0, 0.29)], [0.008, 0.007, 0.007, 0.008], s))
        cups.append((sx * 0.14, 0, 0.29))
    P.append(tube("Stem", [(0, 0, 0.25), (0, 0, 0.33)], [0.01, 0.009], s))
    rnd = random.Random(3)
    for k, (x, y, z) in enumerate(cups):
        P.append(lathe(f"Cup{k}", [(0.0, z), (0.012, z), (0.03, z + 0.012), (0.034, z + 0.016), (0.022, z + 0.03), (0.0, z + 0.03)], s, 24, (x, y, 0)))
        ht = rnd.uniform(0.03, 0.08)
        P.append(lathe(f"Stub{k}", [(0.0, z + 0.02), (0.013, z + 0.02), (0.013, z + 0.02 + ht), (0.009, z + 0.03 + ht), (0.0, z + 0.028 + ht)], wax, 16, (x, y, 0)))
        for d in range(4):
            a = rnd.uniform(0, 6.28)
            ln = rnd.uniform(0.02, 0.06)
            top = z + 0.02 + ht * rnd.uniform(0.4, 1.0)
            P.append(tube(f"Drip{k}{d}", [(x + math.cos(a) * 0.014, y + math.sin(a) * 0.014, top), (x + math.cos(a) * 0.03, y + math.sin(a) * 0.03, z + 0.03),
                                          (x + math.cos(a) * 0.032, y + math.sin(a) * 0.032, z + 0.03 - ln * 0.5)], [0.0028, 0.0034, 0.0026], wax, 1))
    P.append(lumpy_sphere("Pool", (0.03, 0.04, 0.001), (0.06, 0.04, 0.003), wax, 7, amp=0.3, freq=3, subdiv=2))
    return P


def carcass_dish():
    clear()
    ce, bone = role("ceramic"), role("bone")
    dish = lathe("Dish", [(0.0, 0.004), (0.16, 0.004), (0.165, 0.0), (0.175, 0.0), (0.18, 0.008), (0.24, 0.03), (0.25, 0.036), (0.245, 0.04), (0.236, 0.036),
                          (0.178, 0.016), (0.0, 0.016)], ce, 48)
    P = [scaled(dish, (1.0, 0.68, 1.0))]
    z = 0.016
    # the keel, the breastbone's blade standing up the middle, and the ribs arching down off the spine either side
    P.append(tube("Keel", [(-0.1, 0, z + 0.04), (-0.02, 0, z + 0.075), (0.07, 0, z + 0.07), (0.11, 0, z + 0.04)], [(0.004, 0.012), (0.004, 0.018), (0.004, 0.016), (0.004, 0.008)], bone))
    P.append(tube("Spine", [(-0.12, 0, z + 0.015), (0.0, 0, z + 0.012), (0.12, 0, z + 0.015)], [0.012, 0.014, 0.01], bone))
    for k in range(7):
        x = -0.08 + k * 0.026
        for sy in (-1, 1):
            P.append(tube(f"Rib{k}{sy}", [(x, sy * 0.01, z + 0.014), (x + 0.005, sy * 0.06, z + 0.03), (x + 0.012, sy * 0.055, z + 0.065), (x + 0.015, sy * 0.008, z + 0.072)],
                          [0.0035, 0.0035, 0.003, 0.0025], bone, 1))
    P.append(tube("Drumstick", [(0.14, -0.05, z + 0.01), (0.2, -0.09, z + 0.015)], [0.012, 0.009], bone))
    P.append(lumpy_sphere("Knuckle", (0.205, -0.093, z + 0.016), (0.014, 0.012, 0.011), bone, 3, amp=0.1, freq=3, subdiv=2))
    rnd = random.Random(8)
    P.append(lumpy_sphere("Scraps", (-0.02, 0.0, z + 0.006), (0.15, 0.09, 0.008), role("food"), 4, amp=0.3, freq=4, subdiv=3, flat_bottom=z))
    for k in range(10):
        P.append(lumpy_sphere(f"Mould{k}", (rnd.uniform(-0.12, 0.12), rnd.uniform(-0.07, 0.07), z + rnd.uniform(0.01, 0.05)), (rnd.uniform(0.01, 0.025), rnd.uniform(0.01, 0.02), 0.006),
                              role("mould"), 60 + k, amp=0.25, freq=5, subdiv=2))
    return P


# ------------------------------------------------------------------ the skeleton

def vertebra(name, x, r, z, bone):
    """One vertebra lying on its back: the body, the arch's transverse processes out either side."""
    body = lathe(name, [(0.0, -0.011), (r * 0.9, -0.011), (r, -0.007), (r * 0.85, 0.0), (r, 0.007), (r * 0.9, 0.011), (0.0, 0.011)], bone, 12)
    body = scaled(body, (1, 1, 0.85), (x, 0, z + r * 0.85), (0, math.pi / 2, 0))
    tp = tube(name + "P", [(x, -r * 2.1, z + r * 0.5), (x, 0, z + r * 0.2), (x, r * 2.1, z + r * 0.5)], [0.004, 0.006, 0.004], bone, 1)
    return [body, tp]


def skeleton_headless():
    """On its back along +X, from the neck (x = 0, snapped) to the feet; z = 0 the table."""
    clear()
    bone = role("bone")
    P = []
    # the neck's last three, and a snapped one
    for k in range(3):
        P += vertebra(f"C{k}", 0.012 + k * 0.022, 0.012, 0.012, bone)
    P.append(lumpy_sphere("Snapped", (-0.004, 0.0, 0.018), (0.008, 0.011, 0.008), bone, 5, amp=0.35, freq=6, subdiv=2))
    # the spine: twelve thoracic, five lumbar
    xs = []
    x = 0.08
    for k in range(17):
        r = 0.013 + 0.012 * (k / 16)
        P += vertebra(f"V{k}", x, r, 0.0, bone)
        xs.append((x, r))
        x += 0.026 + 0.008 * (k / 16)
    lumbar_end = x
    # the ribs: from each thoracic vertebra out and up and round toward the breastbone (the cage half fallen in)
    rnd = random.Random(12)
    for k in range(12):
        vx, vr = xs[k]
        span = 0.08 + 0.06 * math.sin(min(k, 9) / 9 * math.pi * 0.85)
        reach = 0.12 if k < 7 else 0.12 - (k - 6) * 0.02
        sag = rnd.uniform(0.0, 0.02) + (0.03 if k in (3, 8) else 0.0)
        for sy in (-1, 1):
            pts = [(vx, sy * vr * 1.6, vr * 1.2), (vx + 0.02, sy * span, 0.03), (vx + 0.05, sy * (span + 0.01), 0.09 - sag), (vx + 0.08, sy * span * 0.7, 0.14 - sag)]
            if k < 7:
                pts.append((vx + 0.1, sy * 0.025, 0.155 - sag))
            if k == 9 and sy > 0:
                pts = pts[:3]   # (one broken short)
            P.append(tube(f"Rib{k}{sy}", pts, [(0.004, 0.0075)] * len(pts), bone, 1))
    # the breastbone
    P.append(tube("Sternum", [(0.1, 0, 0.15), (0.17, 0, 0.155), (0.26, 0, 0.145), (0.29, 0, 0.13)], [(0.014, 0.004), (0.016, 0.005), (0.013, 0.004), (0.006, 0.003)], bone))
    # the shoulders: the collarbones, the blades under them, and the arms down the sides
    for sy in (-1, 1):
        P.append(tube(f"Clavicle{sy}", [(0.1, sy * 0.025, 0.14), (0.085, sy * 0.09, 0.12), (0.095, sy * 0.15, 0.1), (0.09, sy * 0.19, 0.08)], [0.006, 0.007, 0.006, 0.007], bone))
        blade = lumpy_sphere(f"Scapula{sy}", (0.15, sy * 0.14, 0.012), (0.075, 0.055, 0.008), bone, 20 + sy, amp=0.08, freq=3, subdiv=3)
        P.append(blade)
        P.append(tube(f"Spine{sy}", [(0.11, sy * 0.1, 0.02), (0.1, sy * 0.18, 0.04)], [0.004, 0.006], bone))
        # the arm: humerus, then radius and ulna crossed, the hand splayed
        sh = V((0.09, sy * 0.21, 0.05))
        el = V((0.37, sy * 0.25, 0.03))
        wr = V((0.62, sy * 0.22, 0.03))
        P.append(lumpy_sphere(f"HumHead{sy}", tuple(sh), (0.022, 0.022, 0.02), bone, 30 + sy, amp=0.05, freq=2, subdiv=2))
        P.append(tube(f"Humerus{sy}", [tuple(sh + V((0.02, 0, 0))), tuple((sh + el) / 2), tuple(el - V((0.015, 0, 0)))], [0.011, 0.009, 0.012], bone))
        P.append(lumpy_sphere(f"Elbow{sy}", tuple(el), (0.018, 0.022, 0.014), bone, 33 + sy, amp=0.08, freq=3, subdiv=2))
        P.append(tube(f"Ulna{sy}", [tuple(el + V((0.01, -sy * 0.01, 0))), tuple(wr + V((0, -sy * 0.015, 0)))], [0.008, 0.005], bone))
        P.append(tube(f"Radius{sy}", [tuple(el + V((0.02, sy * 0.008, 0.004))), tuple(wr + V((0, sy * 0.012, 0.004)))], [0.006, 0.009], bone))
        P.append(lumpy_sphere(f"Carpals{sy}", tuple(wr + V((0.025, 0, 0))), (0.022, 0.025, 0.01), bone, 36 + sy, amp=0.2, freq=6, subdiv=2))
        for f in range(5):
            spread = (f - 2) * 0.016 * sy
            base = wr + V((0.04, spread, 0.0))
            curl = 0.008 * f
            pts = [tuple(base), tuple(base + V((0.045, spread * 0.4, 0.002))), tuple(base + V((0.07, spread * 0.6, 0.004 + curl))), tuple(base + V((0.088, spread * 0.7, 0.002 + curl * 1.5)))]
            if f == (0 if sy > 0 else 4):
                pts = [tuple(wr + V((0.03, sy * 0.03, 0.0))), tuple(wr + V((0.05, sy * 0.055, 0.004))), tuple(wr + V((0.07, sy * 0.065, 0.006)))]
            P.append(tube(f"Finger{sy}{f}", pts, [0.0045, 0.004, 0.0035, 0.003][:len(pts)], bone, 1))
    # the pelvis: the sacrum, the two wings of the hip, the arch of the pubis and its rings
    px = lumbar_end + 0.04
    P.append(lumpy_sphere("Sacrum", (px, 0, 0.02), (0.05, 0.04, 0.018), bone, 41, amp=0.08, freq=3, subdiv=3))
    for sy in (-1, 1):
        wing = lumpy_sphere(f"Ilium{sy}", (px - 0.005, sy * 0.085, 0.06), (0.055, 0.02, 0.07), bone, 44 + sy, amp=0.06, freq=2.5, subdiv=3)
        P.append(scaled(wing, (1, 1, 1), (0, 0, 0), (0, 0, 0)))
        P.append(tube(f"Crest{sy}", [(px - 0.05, sy * 0.06, 0.11), (px - 0.01, sy * 0.115, 0.12), (px + 0.04, sy * 0.1, 0.1)], [0.007, 0.008, 0.007], bone))
        P.append(tube(f"Pubis{sy}", [(px + 0.04, sy * 0.09, 0.06), (px + 0.08, sy * 0.06, 0.1), (px + 0.1, sy * 0.012, 0.11)], [0.009, 0.008, 0.01], bone))
        P.append(tube(f"Ischium{sy}", [(px + 0.04, sy * 0.09, 0.05), (px + 0.1, sy * 0.07, 0.04), (px + 0.11, sy * 0.03, 0.08), (px + 0.1, sy * 0.015, 0.105)], [0.009, 0.01, 0.008, 0.008], bone))
    # the legs: femur from the hip's socket, the kneecap, tibia and fibula, the foot fallen open
    for sy in (-1, 1):
        hip = V((px + 0.05, sy * 0.1, 0.06))
        knee = V((px + 0.5, sy * 0.12, 0.04))
        ank = V((px + 0.9, sy * 0.13, 0.04))
        P.append(lumpy_sphere(f"FemHead{sy}", tuple(hip + V((0, sy * 0.01, 0))), (0.023, 0.023, 0.022), bone, 50 + sy, amp=0.03, freq=2, subdiv=2))
        P.append(tube(f"Femur{sy}", [tuple(hip + V((0.02, sy * 0.02, 0.0))), tuple(hip + V((0.06, sy * 0.025, -0.005))), tuple((hip + knee) / 2), tuple(knee - V((0.03, 0, 0)))],
                      [0.012, 0.013, 0.011, 0.014], bone))
        P.append(lumpy_sphere(f"Trochanter{sy}", tuple(hip + V((0.04, sy * 0.035, 0.0))), (0.016, 0.014, 0.016), bone, 52 + sy, amp=0.1, freq=3, subdiv=2))
        for dy in (-0.012, 0.012):
            P.append(lumpy_sphere(f"Condyle{sy}{dy}", tuple(knee + V((-0.01, dy, 0.0))), (0.02, 0.014, 0.018), bone, 54 + sy, amp=0.05, freq=2, subdiv=2))
        P.append(lumpy_sphere(f"Patella{sy}", tuple(knee + V((0.0, 0, 0.03))), (0.016, 0.014, 0.007), bone, 56 + sy, amp=0.08, freq=3, subdiv=2))
        P.append(tube(f"Tibia{sy}", [tuple(knee + V((0.02, 0, 0))), tuple(knee + V((0.06, 0, 0.0))), tuple((knee + ank) / 2 + V((0, 0, 0.003))), tuple(ank)],
                      [0.017, 0.012, 0.01, 0.013], bone))
        P.append(tube(f"Fibula{sy}", [tuple(knee + V((0.03, sy * 0.025, -0.008))), tuple(ank + V((0.0, sy * 0.025, -0.005)))], [0.006, 0.007], bone))
        # the foot: heel and ankle bones, the long bones of the foot fanned out, fallen outward
        P.append(lumpy_sphere(f"Tarsals{sy}", tuple(ank + V((0.04, sy * 0.01, 0.0))), (0.045, 0.03, 0.022), bone, 58 + sy, amp=0.18, freq=5, subdiv=2))
        for t in range(5):
            spread = (t - 2) * 0.014
            base = ank + V((0.08, sy * (0.015 + spread * 0.5), 0.02))
            tip = base + V((0.08 - abs(t - 1) * 0.008, sy * (spread * 1.6 + 0.02), 0.03 - t * 0.004))
            P.append(tube(f"Toe{sy}{t}", [tuple(base), tuple((base + tip) / 2 + V((0, 0, 0.004))), tuple(tip)], [0.0055 if t == 0 else 0.0042, 0.004, 0.0035], bone, 1))
    # (the owner, 2026-10-04: "grimy and frozen over with some meat still decomposing off of his mainly skeletal remains")
    # what's left on him: the long back muscles shrunk to dark leather along the spine, rags of it hanging off the ribs, a
    # mass of it at the hips, half of one thigh still on, the neck's stump torn; sinew strung across the joints
    meat, sinew = role("meat"), role("sinew")
    rnd = random.Random(77)
    for sy in (-1, 1):
        pts = [(0.1 + k * 0.06, sy * 0.03, 0.012) for k in range(9)]
        P.append(tube(f"BackMuscle{sy}", pts, [(0.012 + 0.006 * math.sin(k), 0.008) for k in range(9)], meat, 1))
    for k in range(9):
        vx, vr = xs[rnd.randint(0, 9)]
        sy = rnd.choice((-1, 1))
        P.append(lumpy_sphere(f"RibRag{k}", (vx + 0.05, sy * rnd.uniform(0.08, 0.13), rnd.uniform(0.05, 0.11)), (rnd.uniform(0.015, 0.03), 0.012, rnd.uniform(0.01, 0.02)), meat, 300 + k, amp=0.3, freq=5, subdiv=2))
        # a strip of it hanging down off the rib
        top = V((vx + 0.05, sy * 0.12, 0.08))
        P.append(tube(f"RibStrip{k}", [tuple(top), tuple(top + V((0.005, sy * 0.02, -0.04))), tuple(top + V((0.01, sy * 0.025, -0.075)))], [0.006, 0.004, 0.002], meat, 1))
    P.append(lumpy_sphere("HipMass", (px + 0.02, 0, 0.045), (0.07, 0.1, 0.035), meat, 320, amp=0.25, freq=4, subdiv=3))
    hip = V((px + 0.05, 0.1, 0.06)); knee = V((px + 0.5, 0.12, 0.04))
    P.append(tube("ThighMeat", [tuple(hip + V((0.04, 0.0, 0.0))), tuple(hip.lerp(knee, 0.35) + V((0, 0.005, 0.004))), tuple(hip.lerp(knee, 0.6))], [(0.035, 0.028), (0.03, 0.024), (0.012, 0.01)], meat, 1))
    P.append(lumpy_sphere("NeckStump", (0.0, 0.0, 0.02), (0.03, 0.035, 0.025), meat, 330, amp=0.45, freq=6, subdiv=2))
    for k in range(5):
        a = k / 5 * math.tau
        P.append(tube(f"NeckShred{k}", [(0.0, math.cos(a) * 0.02, 0.02 + math.sin(a) * 0.015), (-0.03 - rnd.uniform(0, 0.02), math.cos(a) * 0.035, 0.012 + math.sin(a) * 0.02)], [0.004, 0.0015], meat, 1))
    # sinew across the knees, the elbows and the wrists
    for sy in (-1, 1):
        for (a0, a1) in (((px + 0.47, 0.12), (px + 0.56, 0.125)), ((0.35, 0.25), (0.42, 0.245)), ((0.6, 0.22), (0.66, 0.22))):
            P.append(tube(f"Sinew{sy}{a0[0]:.2f}", [(a0[0], sy * a0[1], 0.045), ((a0[0] + a1[0]) / 2, sy * (a0[1] + a1[1]) / 2, 0.05), (a1[0], sy * a1[1], 0.042)], [0.003, 0.0025, 0.003], sinew, 1))
    return P


# ------------------------------------------------------------------ silver and porcelain

def silver_platter():
    clear()
    s = role("silver")
    gad = lambda a, k: 1.0 + (0.02 * math.cos(a * 36) if k >= 5 else 0.0)
    plat = lathe_mod("Platter", [(0.0, 0.006), (0.36, 0.006), (0.37, 0.0), (0.4, 0.0), (0.41, 0.008), (0.45, 0.026), (0.48, 0.034), (0.49, 0.04), (0.485, 0.046),
                                 (0.47, 0.043), (0.44, 0.03), (0.405, 0.016), (0.0, 0.016)], s, 96, gad)
    P = [scaled(plat, (1.0, 0.68, 1.0))]
    for sx in (-1, 1):
        P.append(tube(f"Handle{sx}", [(sx * 0.47, -0.08, 0.04), (sx * 0.53, -0.07, 0.055), (sx * 0.555, 0.0, 0.06), (sx * 0.53, 0.07, 0.055), (sx * 0.47, 0.08, 0.04)],
                      [(0.008, 0.005)] * 5, s))
    # a chased border just inside the rim: a ring of leaves
    for k in range(40):
        a = k / 40 * math.tau
        x, y = math.cos(a) * 0.435, math.sin(a) * 0.435 * 0.68
        P.append(moved(lumpy_sphere(f"Leaf{k}", (0, 0, 0), (0.012, 0.005, 0.0018), s, k, amp=0.05, freq=2, subdiv=1), (x, y, 0.028), (0, 0, a + math.pi / 2)))
    return P


def cloche():
    clear()
    s = role("silver")
    prof = [(0.0, 0.0), (0.0, 0.0)]
    prof = [(0.3, 0.0), (0.305, 0.006), (0.3, 0.015), (0.295, 0.04), (0.28, 0.09), (0.245, 0.15), (0.19, 0.2), (0.12, 0.235), (0.05, 0.25), (0.0, 0.252),
            (0.0, 0.246), (0.05, 0.244), (0.115, 0.229), (0.185, 0.195), (0.24, 0.146), (0.274, 0.088), (0.29, 0.04), (0.293, 0.006)]
    dome = lathe("Dome", prof, s, 64)
    dome = scaled(dome, (1.0, 0.7, 0.9))
    knob = lathe("Knob", [(0.0, 0.22), (0.02, 0.22), (0.012, 0.24), (0.024, 0.255), (0.03, 0.275), (0.018, 0.29), (0.0, 0.295)], s, 20)
    return [dome, knob]


def soup_tureen_bowl():
    clear()
    ce = role("ceramic")
    prof = [(0.0, 0.0), (0.07, 0.0), (0.075, 0.006), (0.068, 0.014), (0.072, 0.02), (0.1, 0.035), (0.14, 0.07), (0.168, 0.1), (0.176, 0.112), (0.172, 0.118),
            (0.162, 0.112), (0.135, 0.078), (0.095, 0.045), (0.0, 0.032)]
    P = [lathe("Bowl", prof, ce, 56)]
    P.append(lathe("Band", [(0.1715, 0.098), (0.1725, 0.104)], role("silver"), 56))
    return P


# ------------------------------------------------------------------ the pantry's shelves (the owner, 2026-10-04: "whatever
# items are on the shelves look horrible")

def mason_jar(name, contents, seed):
    """A preserving jar: thick glass with shoulders, a screw band and lid, a paper label; inside, what was put up in it
    (fruit gone dark, pickles in their brine): the contents modelled, the glass round them."""
    rnd = random.Random(seed)
    P = [lathe(name + "Glass", [(0.0, 0.0), (0.04, 0.0), (0.043, 0.004), (0.044, 0.1), (0.04, 0.118), (0.032, 0.126), (0.032, 0.134), (0.0, 0.134)], role("jarglass"), 28)]
    P.append(lathe(name + "Band", [(0.0, 0.13), (0.0345, 0.13), (0.0355, 0.134), (0.0355, 0.148), (0.033, 0.151), (0.0, 0.151)], role("zinc"), 28))
    P.append(lathe(name + "Label", [(0.0445, 0.04), (0.0445, 0.085)], role("label"), 28))
    fill = rnd.uniform(0.07, 0.105)
    if contents == "fruit":
        P.append(lathe(name + "Syrup", [(0.0, 0.004), (0.04, 0.004), (0.041, fill), (0.0, fill)], role("preserve"), 20))
        for k in range(7):
            P.append(lumpy_sphere(name + f"Fruit{k}", (rnd.uniform(-0.022, 0.022), rnd.uniform(-0.022, 0.022), rnd.uniform(0.02, fill - 0.01)), (0.017, 0.017, 0.016),
                                  role("preserve"), seed * 7 + k, amp=0.08, freq=2, subdiv=2))
    else:
        P.append(lathe(name + "Brine", [(0.0, 0.004), (0.04, 0.004), (0.041, fill), (0.0, fill)], role("brine"), 20))
        for k in range(6):
            a = k / 6 * math.tau
            P.append(tube(name + f"Pickle{k}", [(math.cos(a) * 0.024, math.sin(a) * 0.024, 0.008), (math.cos(a) * 0.026, math.sin(a) * 0.026, fill * 0.95)], [0.011, 0.01], role("pickle"), 1))
    return P


def tin_ribbed(name):
    t, lab = role("zinc"), role("label")
    prof = [(0.0, 0.0), (0.038, 0.0), (0.0395, 0.004)]
    for k in range(6):
        z = 0.012 + k * 0.016
        prof += [(0.0395, z), (0.041, z + 0.004), (0.0395, z + 0.008)]
    prof += [(0.0395, 0.11), (0.038, 0.114), (0.0, 0.114)]
    return [lathe(name, prof, t, 24), lathe(name + "Label", [(0.0412, 0.025), (0.0412, 0.09)], lab, 24)]


def pantry_jars():
    clear()
    P = []
    for i, (x, kind) in enumerate([(-0.24, "fruit"), (-0.13, "pickle"), (-0.02, "fruit"), (0.1, "fruit"), (0.22, "pickle")]):
        P += [moved(o, (x, random.Random(i).uniform(-0.03, 0.03), 0)) for o in mason_jar(f"J{i}", kind, 5 + i)]
    return P


def pantry_tins():
    clear()
    P = []
    rnd = random.Random(9)
    x = -0.26
    for i in range(6):
        if i == 4:
            # one tipped over on its side, rolled to the shelf's back
            P += [moved(o, (x + 0.02, 0.03, 0.04), (math.pi / 2, 0, 0.3)) for o in tin_ribbed(f"T{i}")]
            x += 0.12
            continue
        P += [moved(o, (x, rnd.uniform(-0.04, 0.03), 0)) for o in tin_ribbed(f"T{i}")]
        if i in (1, 3):
            P += [moved(o, (x + 0.005, 0.0, 0.1145)) for o in tin_ribbed(f"T{i}b")]   # stacked
        x += 0.09
    return P


def crock():
    clear()
    ce = role("stoneware")
    P = [lathe("Crock", [(0.0, 0.0), (0.07, 0.0), (0.075, 0.006), (0.082, 0.05), (0.082, 0.16), (0.075, 0.19), (0.072, 0.2), (0.064, 0.2), (0.068, 0.19),
                         (0.072, 0.16), (0.072, 0.02), (0.0, 0.02)], ce, 32)]
    P.append(lathe("Lid", [(0.0, 0.205), (0.07, 0.198), (0.072, 0.204), (0.06, 0.212), (0.015, 0.218), (0.016, 0.235), (0.0, 0.237)], ce, 32))
    P.append(lathe("Glaze", [(0.0825, 0.12), (0.0825, 0.13)], role("glaze"), 32))
    return P


def flour_sack():
    """A burlap sack, half full and slumped against the wall, its neck tied with twine."""
    clear()
    ob = lumpy_sphere("Sack", (0, 0, 0.17), (0.17, 0.13, 0.19), role("sack"), 3, amp=0.06, freq=2.0, subdiv=4, flat_bottom=0.0)
    for v in ob.data.vertices:
        p = v.co
        # slumped: the top drawn in to the neck, the belly sagging out at the bottom
        t = max(0.0, (p.z - 0.17) / 0.19)
        p.x *= 1.0 - 0.75 * t * t
        p.y *= 1.0 - 0.75 * t * t
        if p.z < 0.12:
            p.x *= 1.08
            p.y *= 1.08
        p.z += 0.004 * math.sin(p.x * 120) * math.sin(p.z * 90)   # (the weave's folds)
    neck = lumpy_sphere("Neck", (0, 0, 0.38), (0.035, 0.03, 0.05), role("sack"), 4, amp=0.15, freq=4, subdiv=2)
    ears = lumpy_sphere("Ears", (0.0, 0.0, 0.43), (0.05, 0.03, 0.03), role("sack"), 5, amp=0.25, freq=4, subdiv=2)
    twine = lathe("Twine", [(0.03, 0.35), (0.033, 0.355), (0.03, 0.36)], role("twine"), 16)
    return [ob, neck, ears, twine]


def pantry_bottles():
    """A huddle of bottles: tall and short, a long-necked vinegar, a squat syrup, a milk bottle, a dark tonic, corks and
    caps and paper labels, one fallen on its side."""
    clear()
    rnd = random.Random(14)
    glass, label, cork = role("glass"), role("label"), role("cork")
    P = []
    kinds = [
        [(0, 0), (0.036, 0), (0.037, 0.005), (0.037, 0.2), (0.033, 0.225), (0.015, 0.255), (0.013, 0.3), (0.0, 0.3)],
        [(0, 0), (0.03, 0), (0.031, 0.005), (0.031, 0.17), (0.026, 0.19), (0.011, 0.215), (0.01, 0.27), (0.0, 0.27)],
        [(0, 0), (0.045, 0), (0.047, 0.01), (0.047, 0.1), (0.035, 0.13), (0.016, 0.15), (0.016, 0.175), (0.0, 0.175)],
        [(0, 0), (0.038, 0), (0.04, 0.008), (0.04, 0.14), (0.032, 0.17), (0.024, 0.19), (0.026, 0.205), (0.0, 0.205)],
    ]
    x = -0.26
    for i in range(6):
        prof = kinds[rnd.randint(0, len(kinds) - 1)]
        top = prof[-1][1]
        r = max(q[0] for q in prof)
        parts = [lathe(f"B{i}", prof, glass, 20),
                 lathe(f"L{i}", [(r + 0.0006, top * 0.25), (r + 0.0006, top * 0.25 + rnd.uniform(0.04, 0.07))], label, 20),
                 lathe(f"C{i}", [(0.0, top - 0.006), (0.012, top - 0.006), (0.013, top + 0.012), (0.0, top + 0.014)], cork, 12)]
        y = rnd.uniform(-0.04, 0.04)
        if i == 4:
            parts = [moved(o, (x, y, r), (math.pi / 2, 0, rnd.uniform(-0.5, 0.5))) for o in parts]   # (fallen on its side)
            x += 0.2
        else:
            parts = [moved(o, (x, y, 0)) for o in parts]
            x += r * 2 + rnd.uniform(0.012, 0.03)
        P += parts
    return P


def pantry_cans():
    """Cans in a stack: a row of five, three on them, one on top; ribbed, with paper labels."""
    clear()
    P = []
    for row, (n, z) in enumerate(((5, 0.0), (3, 0.1145), (1, 0.229))):
        for i in range(n):
            x = (i - (n - 1) / 2) * 0.086
            P += [moved(o, (x, (row % 2) * 0.01, z)) for o in tin_ribbed(f"S{row}{i}")]
    return P


PIECES = {
    "dining_table": (dining_table, (0, 0, 0.5), 7.5, 1.6, 25),
    "dining_top_half": (dining_top_half, (1.8, 0, 0.0), 4.2, 1.0, 30),
    "dining_leg": (dining_leg, (0, 0, 0.35), 1.6, 0.2, 30),
    "setting_meat": (setting_meat, (0, 0.05, 0.03), 0.9, 0.5, 20),
    "setting_soup": (setting_soup, (0, 0.05, 0.03), 0.9, 0.5, 20),
    "candelabrum": (candelabrum, (0, 0, 0.2), 1.0, 0.3, 30),
    "carcass_dish": (carcass_dish, (0, 0, 0.05), 0.9, 0.5, 25),
    "skeleton_headless": (skeleton_headless, (0.8, 0, 0.05), 2.4, 1.2, 15),
    "silver_platter": (silver_platter, (0, 0, 0.02), 1.4, 0.7, 25),
    "cloche": (cloche, (0, 0, 0.12), 1.0, 0.3, 30),
    "soup_tureen_bowl": (soup_tureen_bowl, (0, 0, 0.06), 0.7, 0.4, 30),
    "pantry_jars": (pantry_jars, (0, 0, 0.07), 0.9, 0.2, 20),
    "pantry_tins": (pantry_tins, (0, 0, 0.07), 0.9, 0.2, 20),
    "crock": (crock, (0, 0, 0.1), 0.8, 0.3, 30),
    "flour_sack": (flour_sack, (0, 0, 0.2), 1.1, 0.3, 30),
    "pantry_bottles": (pantry_bottles, (0, 0, 0.12), 0.9, 0.2, 20),
    "pantry_cans": (pantry_cans, (0, 0, 0.14), 0.9, 0.2, 20),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        fn, tgt, dist, h, yaw = PIECES[name]
        export(fn(), name, tgt, dist, h, yaw)
