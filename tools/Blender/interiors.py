"""The church's, the station's and the bunker's pieces (the owner, 2026-10-01: the fidelity pass on to the church, the
station and the bunker). Built and exported like the furniture (furniture.py: roles, the game's box-projected UVs, a
baked cavity map) to assets/models/furniture/<name>.glb:

    blender -b --python tools/Blender/interiors.py [-- name ...]

Each is built with its front toward -Y (the export mirrors it to the game's -Z), its base on z = 0.
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, apply_mods, add_mod, obj_from_bmesh, skin_tree
import furniture as F
from furniture import role, lathe, block, piping, cushion, finish, join_objs, export, turned_leg

V = Vector
F.ROLE_COLOURS.update({
    "cushion": (0.4, 0.06, 0.06), "plastic": (0.12, 0.12, 0.13), "steel": (0.62, 0.64, 0.6), "brass": (0.62, 0.48, 0.24),
    "leather": (0.3, 0.12, 0.08), "cloth": (0.25, 0.28, 0.2), "paper": (0.8, 0.76, 0.66), "rubber": (0.05, 0.05, 0.05),
    "iron": (0.12, 0.12, 0.13), "felt": (0.3, 0.26, 0.2),
})
F.BUDGET.update({"pew": 9000, "station_desk": 6000, "office_chair": 3500, "filing_cabinet": 2500, "filing_cabinet_open": 3500,
                 "coat_stand": 3500, "footlocker": 2000, "crate": 1200, "papers": 600, "clipboard": 400})


def place(ob, rot=(0, 0, 0), loc=(0, 0, 0)):
    ob.rotation_euler = rot
    ob.location = loc
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True)
    return ob


def outline_extrude(name, pts, thick, material, axis="x", offset=0.0):
    """A flat panel cut to an outline (pts: (u, v) in the panel's plane), extruded `thick` across `axis`."""
    bm = bmesh.new()
    vs = [bm.verts.new((offset, u, v) if axis == "x" else (u, offset, v)) for (u, v) in pts]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    shift = V((thick, 0, 0)) if axis == "x" else V((0, thick, 0))
    bmesh.ops.translate(bm, vec=shift, verts=[e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name, material, smooth=False)
    return finish(ob, True, 0.006, 2)


# ------------------------------------------------------------------ the church

def pew(length=5.3):
    """A pew: two carved ends (a shaped silhouette with a scroll at the arm and a poppyhead finial, a sunk panel with a
    quatrefoil), a seat with a rounded nosing, a raked back of boards with a moulded top rail, a shelf on its back with
    hymnals in it, a padded kneeler on its rail. Front (where you face, toward the altar) is -Y."""
    clear()
    wood, cushion_m, cloth, paper = role("wood"), role("cushion"), role("leather"), role("paper")
    P = []
    L = length
    # the ends: the silhouette (u: across the pew front -Y to back +Y; v: up)
    end = [(-0.33, 0.0), (0.33, 0.0), (0.33, 0.95), (0.3, 1.02), (0.22, 1.06), (0.12, 1.02), (0.04, 0.98), (-0.05, 0.9),
           (-0.15, 0.82), (-0.24, 0.8), (-0.3, 0.74), (-0.33, 0.66)]
    for s in (-1, 1):
        x = s * L / 2
        P.append(outline_extrude(f"End{s}", end, 0.07, wood, "x", x - 0.035))
        # the sunk panel and its quatrefoil, on the outer face
        P.append(block(f"Panel{s}", (x + s * 0.036, 0.04, 0.42), (0.01, 0.42, 0.5), wood, bevel=0.006, segs=2))
        for k in range(4):
            a = k * math.pi / 2
            P.append(lathe(f"Foil{s}{k}", [(0.0, 0.0), (0.045, 0.0), (0.045, 0.012), (0.0, 0.012)], wood, 16))
            place(P[-1], (0, s * math.pi / 2, 0), (x + s * 0.042, 0.04 + math.cos(a) * 0.055, 0.5 + math.sin(a) * 0.055))
        # the poppyhead: a turned neck and a carved knob
        P.append(lathe(f"Neck{s}", [(0.035, 0.0), (0.03, 0.05), (0.045, 0.07), (0.0, 0.075)], wood, 12, (x, 0.22, 1.05)))
        P.append(lathe(f"Poppy{s}", [(0.0, 0.0), (0.05, 0.02), (0.065, 0.06), (0.05, 0.1), (0.02, 0.125), (0.0, 0.13)], wood, 14, (x, 0.22, 1.12)))
    # the seat, its nosing rounded
    P.append(block("Seat", (0, 0.0, 0.45), (L - 0.07, 0.44, 0.05), wood, bevel=0.018, segs=3))
    P.append(block("SeatRail", (0, -0.2, 0.39), (L - 0.07, 0.03, 0.08), wood, bevel=0.005, segs=1))
    # the back: boards raked back, a top rail with a moulding
    rake = 0.14
    for b in range(4):
        z = 0.52 + b * 0.13
        y = 0.24 + (z - 0.5) * math.tan(rake)
        P.append(block(f"Board{b}", (0, y, z + 0.06), (L - 0.07, 0.035, 0.125), wood, bevel=0.006, segs=1, rot=(-rake, 0, 0)))
    P.append(block("TopRail", (0, 0.32, 1.0), (L - 0.07, 0.07, 0.05), wood, bevel=0.015, segs=3))
    P.append(piping("TopBead", [(-L / 2 + 0.04, 0.285, 0.98), (L / 2 - 0.04, 0.285, 0.98)], 0.01, wood))
    # the book shelf on the back, and hymnals in it
    P.append(block("Shelf", (0, 0.43, 0.82), (L - 0.07, 0.14, 0.02), wood, bevel=0.004, segs=1))
    P.append(block("ShelfLip", (0, 0.5, 0.86), (L - 0.07, 0.015, 0.07), wood, bevel=0.003, segs=1))
    rnd = random.Random(int(L * 100))
    x = -L / 2 + 0.25
    while x < L / 2 - 0.3:
        if rnd.random() < 0.55:
            t = rnd.uniform(0.03, 0.045)
            P.append(block(f"Hymnal{x:.2f}", (x, 0.43, 0.83 + 0.09), (t, 0.12, 0.17), cloth, bevel=0.004, segs=1, rot=(0, rnd.uniform(-0.12, 0.12), 0)))
            P.append(block(f"Pages{x:.2f}", (x, 0.428, 0.83 + 0.09), (t * 0.8, 0.118, 0.164), paper, bevel=0.0))
            x += t + 0.004
        else:
            x += rnd.uniform(0.2, 0.6)
    # the kneeler: a rail on two brackets and a padded cushion on it (for the pew behind: at the back, +Y)
    P.append(block("KneelRail", (0, 0.62, 0.1), (L - 0.2, 0.16, 0.05), wood, bevel=0.006, segs=1))
    P.append(cushion("Kneeler", (0, 0.62, 0.16), (L - 0.26, 0.17, 0.07), cushion_m, puff=0.25))
    return P


# ------------------------------------------------------------------ the station

def station_desk():
    """A ranger's pedestal desk: a moulded top with a leather writing inset, two pedestals of three drawers with brass
    cup pulls and label frames, a centre drawer, the modesty panel in two raised panels, a plinth."""
    clear()
    wood, leather, brass = role("wood"), role("leather"), role("brass")
    P = []
    W, D, H = 1.9, 0.8, 0.76
    P.append(block("Top", (0, 0, H - 0.02), (W, D, 0.04), wood, bevel=0.012, segs=3))
    P.append(block("Inset", (-0.05, 0.02, H + 0.0015), (1.2, 0.5, 0.004), leather, bevel=0.0))
    for s in (-1, 1):
        cx = s * 0.62
        P.append(block(f"Ped{s}", (cx, 0.02, (H - 0.04) / 2 + 0.04), (0.55, 0.7, H - 0.12), wood, bevel=0.006, segs=1))
        P.append(block(f"Plinth{s}", (cx, 0.02, 0.03), (0.58, 0.72, 0.06), wood, bevel=0.005, segs=1))
        for d in range(3):
            z = 0.17 + d * 0.2
            P.append(block(f"Drawer{s}{d}", (cx, -0.335, z), (0.46, 0.025, 0.17), wood, bevel=0.008, segs=2))
            P.append(lathe(f"Pull{s}{d}", [(0.0, 0.0), (0.03, 0.0), (0.032, 0.012), (0.0, 0.016)], brass, 12))
            place(P[-1], (math.pi / 2, 0, 0), (cx, -0.35, z - 0.02))
            P.append(block(f"Label{s}{d}", (cx, -0.349, z + 0.04), (0.07, 0.003, 0.03), brass, bevel=0.0))
    P.append(block("CentreDrawer", (0, -0.335, H - 0.1), (0.64, 0.025, 0.1), wood, bevel=0.006, segs=2))
    P.append(lathe("CentrePull", [(0.0, 0.0), (0.025, 0.0), (0.027, 0.01), (0.0, 0.013)], brass, 12))
    place(P[-1], (math.pi / 2, 0, 0), (0, -0.35, H - 0.1))
    # the back (the modesty panel faces the room behind the desk: +Y here)
    P.append(block("Modesty", (0, 0.33, 0.39), (0.7, 0.03, 0.62), wood, bevel=0.006, segs=1))
    for zz in (0.24, 0.53):
        P.append(block(f"Raised{zz}", (0, 0.35, zz), (0.56, 0.015, 0.22), wood, bevel=0.012, segs=3))
    return P


def coat_stand():
    """A bentwood coat stand: a turned pole on three splayed feet, six curled hooks at the top, a ranger's jacket and a
    campaign hat hung on it."""
    clear()
    wood, cloth, felt = role("wood"), role("cloth"), role("felt")
    P = [lathe("Pole", [(0.0, 0.0), (0.03, 0.0), (0.025, 0.2), (0.022, 1.7), (0.03, 1.74), (0.03, 1.78), (0.0, 1.82)], wood, 14)]
    for k in range(3):
        a = k / 3 * math.tau
        d = V((math.cos(a), math.sin(a), 0))
        P.append(skin_tree(f"Foot{k}", [V((0, 0, 0.25)), d * 0.18 + V((0, 0, 0.08)), d * 0.32 + V((0, 0, 0.01))], [(0, 1), (1, 2)], [0.02, 0.016, 0.014], wood, subdiv=1))
    for k in range(6):
        a = k / 6 * math.tau
        d = V((math.cos(a), math.sin(a), 0))
        P.append(skin_tree(f"Hook{k}", [V((0, 0, 1.7)), d * 0.1 + V((0, 0, 1.68)), d * 0.17 + V((0, 0, 1.73)), d * 0.15 + V((0, 0, 1.78))],
                           [(0, 1), (1, 2), (2, 3)], [0.01, 0.009, 0.008, 0.007], wood, subdiv=1))
    # the jacket, hung by its loop on a hook: shoulders, body in folds, sleeves
    bm = bmesh.new()
    nx, nz = 16, 14
    grid = []
    for j in range(nz + 1):
        t = j / nz
        w = 0.15 + 0.1 * min(1.0, t * 3)
        row = []
        for i in range(nx + 1):
            u = i / nx
            a = (u - 0.5) * math.pi * 1.1
            row.append(bm.verts.new((0.16 + math.sin(a) * w, -0.02 - math.cos(a) * 0.09 + 0.012 * math.sin(u * 20 + t * 4) * t, 1.68 - t * 0.78)))
        grid.append(row)
    for j in range(nz):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    jk = obj_from_bmesh(bm, "Jacket", cloth)
    add_mod(jk, "SOLIDIFY", thickness=0.02)
    P.append(finish(jk, True, 0, 1, subsurf=1))
    for s in (-1, 1):
        P.append(skin_tree(f"Sleeve{s}", [V((0.16 + s * 0.22, -0.04, 1.6)), V((0.16 + s * 0.25, -0.06, 1.3)), V((0.16 + s * 0.23, -0.05, 1.05))], [(0, 1), (1, 2)], [0.05, 0.045, 0.04], cloth, subdiv=1))
    for k in range(4):
        P.append(lathe(f"Button{k}", [(0.0, 0.0), (0.008, 0.0), (0.008, 0.004), (0.0, 0.005)], role("brass"), 8))
        place(P[-1], (math.pi / 2, 0, 0), (0.16, -0.115, 1.5 - k * 0.12))
    # the campaign hat on the opposite hook: a flat brim, a pinched crown
    P.append(lathe("Hat", [(0.0, 0.0), (0.19, 0.0), (0.19, 0.008), (0.075, 0.012), (0.08, 0.09), (0.05, 0.11), (0.0, 0.12)], felt, 28, (-0.15, 0.02, 1.7)))
    return P


# ------------------------------------------------------------------ the bunker

def office_chair():
    """A steel-framed office chair of the period: five legs on casters, the gas column, a padded seat and back on a steel
    spine, padded arm rests on steel arms."""
    clear()
    steel, plastic, rubber = role("steel"), role("plastic"), role("rubber")
    P = [lathe("Column", [(0.0, 0.07), (0.03, 0.07), (0.028, 0.38), (0.035, 0.4), (0.0, 0.41)], steel, 14),
         lathe("Hub", [(0.0, 0.05), (0.06, 0.05), (0.05, 0.1), (0.0, 0.1)], steel, 14)]
    for k in range(5):
        a = k / 5 * math.tau
        d = V((math.cos(a), math.sin(a), 0))
        P.append(skin_tree(f"Leg{k}", [V((0, 0, 0.08)), d * 0.3 + V((0, 0, 0.06))], [(0, 1)], [(0.022, 0.015), (0.016, 0.012)], steel, subdiv=1))
        c = d * 0.3 + V((0, 0, 0.03))
        P.append(lathe(f"Caster{k}", [(0.0, -0.025), (0.025, -0.025), (0.025, 0.025), (0.0, 0.025)], rubber, 12))
        place(P[-1], (0, math.pi / 2, a), tuple(c))
    P.append(cushion("Seat", (0, 0, 0.46), (0.46, 0.44, 0.07), plastic, puff=0.2))
    P.append(skin_tree("Spine", [V((0, 0.18, 0.42)), V((0, 0.26, 0.45)), V((0, 0.25, 0.7))], [(0, 1), (1, 2)], [(0.02, 0.02)] * 3, steel, subdiv=1))
    P.append(cushion("Back", (0, 0.25, 0.78), (0.4, 0.06, 0.34), plastic, puff=0.2))
    for s in (-1, 1):
        P.append(skin_tree(f"Arm{s}", [V((s * 0.2, 0.1, 0.43)), V((s * 0.25, 0.08, 0.5)), V((s * 0.25, 0.0, 0.62)), V((s * 0.25, -0.12, 0.62))], [(0, 1), (1, 2), (2, 3)], [0.012] * 4, steel, subdiv=1))
        P.append(cushion(f"Pad{s}", (s * 0.25, -0.05, 0.635), (0.05, 0.22, 0.03), plastic, puff=0.2))
    return P


def filing_cabinet(open_drawer):
    """A four-drawer steel filing cabinet: a pressed carcass with its edge return, four drawers with recessed handles and
    card-label frames; the open one pulled out 0.4 m on its runners, hanging files in it."""
    clear()
    steel, paper, metal = role("steel"), role("paper"), role("brass")
    P = [block("Carcass", (0, 0.02, 0.66), (0.48, 0.6, 1.32), steel, bevel=0.008, segs=2),
         block("Plinth", (0, 0.0, 0.02), (0.46, 0.58, 0.04), role("rubber"), bevel=0.003, segs=1)]
    for d in range(4):
        z = 0.18 + d * 0.32
        out = 0.4 if (open_drawer and d == 2) else 0.0
        y = -0.295 - out
        P.append(block(f"Front{d}", (0, y, z), (0.44, 0.02, 0.28), steel, bevel=0.01, segs=3))
        P.append(block(f"Handle{d}", (0, y - 0.016, z + 0.06), (0.14, 0.02, 0.025), metal, bevel=0.005, segs=2))
        P.append(block(f"LabelFrame{d}", (0, y - 0.011, z - 0.03), (0.08, 0.004, 0.04), metal, bevel=0.0))
        P.append(block(f"Label{d}", (0, y - 0.0125, z - 0.03), (0.07, 0.002, 0.03), paper, bevel=0.0))
        if out:
            # the tray pulled out: its sides, bottom and back, hanging files in it
            for s in (-1, 1):
                P.append(block(f"TraySide{s}", (s * 0.2, y + 0.22, z - 0.01), (0.012, 0.44, 0.24), steel, bevel=0.0))
            P.append(block("TrayBottom", (0, y + 0.22, z - 0.12), (0.42, 0.44, 0.01), steel, bevel=0.0))
            rnd = random.Random(3)
            for f in range(7):
                P.append(block(f"File{f}", (rnd.uniform(-0.01, 0.01), y + 0.05 + f * 0.055, z + 0.0), (0.38, 0.01, 0.22), paper, bevel=0.0, rot=(rnd.uniform(-0.1, 0.1), 0, 0)))
    return P


def footlocker():
    """A footlocker: a wooden chest bound in steel at its edges and corners, two side handles, a hasp and padlock."""
    clear()
    wood, steel, iron = role("wood"), role("steel"), role("iron")
    P = [block("Box", (0, 0, 0.18), (0.8, 0.42, 0.34), wood, bevel=0.006, segs=1),
         block("Lid", (0, 0, 0.37), (0.82, 0.44, 0.05), wood, bevel=0.01, segs=2)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(block(f"Corner{sx}{sy}", (sx * 0.4, sy * 0.21, 0.2), (0.025, 0.025, 0.4), steel, bevel=0.004, segs=1))
    for z in (0.04, 0.33):
        P.append(block(f"Band{z}", (0, -0.212, z), (0.8, 0.006, 0.03), steel, bevel=0.0))
        P.append(block(f"BandB{z}", (0, 0.212, z), (0.8, 0.006, 0.03), steel, bevel=0.0))
    for s in (-1, 1):
        P.append(piping(f"Handle{s}", [(s * 0.41, -0.06, 0.24), (s * 0.44, -0.04, 0.2), (s * 0.44, 0.04, 0.2), (s * 0.41, 0.06, 0.24)], 0.008, iron))
    P.append(block("Hasp", (0, -0.225, 0.32), (0.05, 0.01, 0.1), steel, bevel=0.002, segs=1))
    P.append(block("Lock", (0, -0.24, 0.26), (0.05, 0.02, 0.05), iron, bevel=0.006, segs=2))
    P.append(piping("Shackle", [(-0.015, -0.24, 0.285), (-0.015, -0.24, 0.31), (0.015, -0.24, 0.31), (0.015, -0.24, 0.285)], 0.004, iron))
    return P


def crate():
    """A slatted wooden crate: boards on a frame, the battens and corner posts, nail heads."""
    clear()
    wood, iron = role("wood"), role("iron")
    P = []
    S = 0.6
    for k in range(4):
        a = k * math.pi / 2
        n = V((math.cos(a), math.sin(a), 0))
        t = V((-n.y, n.x, 0))
        for b in range(4):
            z = 0.06 + b * 0.15
            c = n * (S / 2) + V((0, 0, z + 0.06))
            P.append(block(f"Slat{k}{b}", tuple(c), (abs(t.x) * (S - 0.04) + abs(n.x) * 0.02 + 0.001, abs(t.y) * (S - 0.04) + abs(n.y) * 0.02 + 0.001, 0.12), wood, bevel=0.004, segs=1))
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.append(block(f"Post{sx}{sy}", (sx * (S / 2 - 0.02), sy * (S / 2 - 0.02), S / 2 + 0.01), (0.05, 0.05, S + 0.02), wood, bevel=0.004, segs=1))
    P.append(block("Lid", (0, 0, S + 0.03), (S + 0.02, S + 0.02, 0.025), wood, bevel=0.004, segs=1))
    for k in range(3):
        P.append(block(f"LidBatten{k}", (0, -0.2 + k * 0.2, S + 0.05), (S, 0.06, 0.02), wood, bevel=0.003, segs=1))
    return P


def papers():
    """Loose papers, a few scattered and curled at the corners."""
    clear()
    paper = role("paper")
    rnd = random.Random(31)
    P = []
    for k in range(6):
        bm = bmesh.new()
        rows = []
        for i in range(4):
            u = i / 3
            curl = 0.012 * u * u
            rows.append([bm.verts.new((-0.105 + 0.21 * u, -0.15, curl)), bm.verts.new((-0.105 + 0.21 * u, 0.15, curl * 0.4))])
        for i in range(3):
            bm.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
        sh = obj_from_bmesh(bm, f"Sheet{k}", paper)
        add_mod(sh, "SOLIDIFY", thickness=0.0015)
        apply_mods(sh)
        P.append(place(sh, (0, 0, rnd.uniform(0, 6.28)), (rnd.uniform(-0.25, 0.25), rnd.uniform(-0.2, 0.2), 0.002 + k * 0.002)))
    return P


def clipboard():
    clear()
    wood, paper, steel = role("wood"), role("paper"), role("steel")
    return [block("Board", (0, 0, 0.003), (0.23, 0.32, 0.005), wood, bevel=0.002, segs=1),
            block("Sheets", (0, -0.01, 0.0075), (0.21, 0.28, 0.004), paper, bevel=0.0),
            block("Clip", (0, 0.135, 0.012), (0.09, 0.04, 0.012), steel, bevel=0.003, segs=2)]


PIECES = {
    "pew": (pew, (0, 0, 0.6), 4.2, 0.8, 35),
    "station_desk": (station_desk, (0, 0, 0.5), 3.0, 0.6, 30),
    "coat_stand": (coat_stand, (0, 0, 1.0), 2.8, 0.2, 30),
    "office_chair": (office_chair, (0, 0, 0.5), 1.8, 0.4, 30),
    "filing_cabinet": (lambda: filing_cabinet(False), (0, 0, 0.7), 2.4, 0.3, 30),
    "filing_cabinet_open": (lambda: filing_cabinet(True), (0, -0.2, 0.7), 2.6, 0.5, 40),
    "footlocker": (footlocker, (0, 0, 0.2), 1.6, 0.5, 30),
    "crate": (crate, (0, 0, 0.3), 1.8, 0.6, 30),
    "papers": (papers, (0, 0, 0.0), 1.0, 0.6, 30),
    "clipboard": (clipboard, (0, 0, 0.0), 0.6, 0.4, 30),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        fn, tgt, dist, h, yaw = PIECES[name]
        export(fn(), name, tgt, dist, h, yaw)
