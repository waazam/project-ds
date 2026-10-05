"""Act 24's pieces (the snow maze's trenches and the flamethrower), modelled in Blender and exported like the furniture
(furniture.py: roles, box-projected UVs, a baked cavity map), to assets/models/furniture/<name>.glb:

    blender -b --python tools/Blender/act24.py [-- name ...]

  flamethrower_wand   the WW2 portable flamethrower's gun (the owner's M2-2 references): two pistol grips, the long
                      barrel, the fuel valve, the igniter's cartridge drum, the nozzle sooted black, the hose off its
                      back end; olive drab, brass fittings (seen in the hands, in the player's view)
  flame_crate         the long wooden crate it came in, "FLAME THROWER M2-2" stencilled on it, its lid nailed down
  flame_crate_open    the same, its lid prised off and leaning, straw inside
  weapon_crate        a rifle crate, rope handles, stencils
  helmet_m1           a steel helmet (the round one), dented, its chinstrap hanging
  helmet_stahl        a coal-scuttle helmet (the other side's)
  rifle               a bolt-action rifle, the stock's wood grey with age
  soldier_remains     a soldier's remains slumped against a trench wall: his greatcoat gone stiff, the skull under his
                      helmet, bone hands in his lap, his boots
  crowbar             a long iron bar, its claw end bent over
  sandbags            a short wall of sandbags, slumped and frozen

Blender's Z up; built with the front toward -Y (the export mirrors it to the game's -Z).
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, apply_mods, add_mod, obj_from_bmesh, lumpy_sphere, skin_tree
import furniture as F
from furniture import role, lathe, block, piping, finish, join_objs, export
F.OUT = os.environ.get("ACT24_OUT", F.OUT)   # (where to write: the game's furniture folder, unless told elsewhere)

V = Vector
F.ROLE_COLOURS.update({
    "olive": (0.27, 0.29, 0.17), "steel": (0.22, 0.22, 0.22), "brass": (0.62, 0.45, 0.2), "rubber": (0.035, 0.035, 0.035),
    "grip": (0.08, 0.07, 0.06), "soot": (0.05, 0.045, 0.04), "crate": (0.32, 0.27, 0.18), "stencil": (0.75, 0.66, 0.2),
    "straw": (0.6, 0.5, 0.28), "coat": (0.22, 0.2, 0.15), "bone": (0.74, 0.68, 0.55), "leather": (0.2, 0.12, 0.07),
    "wood": (0.3, 0.22, 0.14), "rust": (0.3, 0.17, 0.1), "sack": (0.45, 0.4, 0.3), "rope": (0.5, 0.42, 0.3), "iron": (0.12, 0.12, 0.13),
})
F.BUDGET.update({"flamethrower_wand": 7000, "flame_crate": 3000, "flame_crate_open": 4500, "weapon_crate": 2500, "helmet_m1": 1400,
                 "helmet_stahl": 1400, "rifle": 2200, "soldier_remains": 9000, "crowbar": 500, "sandbags": 3500})


def moved(ob, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    ob.scale = scale
    ob.rotation_euler = rot
    ob.location = loc
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def tube(name, pts, radii, material, subdiv=1):
    return skin_tree(name, [V(p) for p in pts], [(i, i + 1) for i in range(len(pts) - 1)], radii, material, subdiv)


def along_y(name, profile, material, segs, x=0.0, z=0.0):
    """A turned piece lying along -Y (profile [(radius, distance along -Y)])."""
    ob = lathe(name, profile, material, segs)
    return moved(ob, (x, 0, z), (math.pi / 2, 0, 0))   # (its Z axis turned onto -Y)


def stencil(name, text, size, loc, rot, material):
    bpy.ops.object.text_add(location=(0, 0, 0))
    t = bpy.context.view_layer.objects.active
    t.data.body = text
    t.data.size = size
    t.data.align_x = "CENTER"
    t.data.align_y = "CENTER"
    t.data.extrude = 0.0006
    bpy.ops.object.convert(target="MESH")
    t = bpy.context.view_layer.objects.active
    t.name = name
    t.data.materials.clear()
    t.data.materials.append(material)
    # (mirrored here: the export mirrors every piece front to back, and the words came out backwards)
    return moved(t, loc, rot, (-1, 1, 1))


# ------------------------------------------------------------------ the flamethrower

def flamethrower_wand():
    """Along -Y from the rear grip (origin) to the nozzle's mouth 0.86 m out; the barrel's axis at z = 0.035."""
    clear()
    ol, st, br, rb, gr, so = role("olive"), role("steel"), role("brass"), role("rubber"), role("grip"), role("soot")
    P = []
    z = 0.035
    # the barrel, and the fuel valve's body at its back
    P.append(along_y("Barrel", [(0.0, -0.1), (0.017, -0.1), (0.017, 0.64), (0.0, 0.64)], ol, 14, 0, z))
    P.append(block("Valve", (0, 0.02, z + 0.005), (0.05, 0.13, 0.06), ol, bevel=0.008, segs=2))
    P.append(along_y("RearCap", [(0.0, -0.13), (0.024, -0.13), (0.026, -0.1), (0.0, -0.1)], br, 12, 0, z))
    # the rear pistol grip (raked back), its trigger and guard
    P.append(moved(block("RearGrip", (0, 0, -0.06), (0.03, 0.045, 0.12), gr, bevel=0.008, segs=2), (0, 0.035, z - 0.01), (-0.3, 0, 0)))
    P.append(tube("Guard", [(0, 0.0, z - 0.03), (0, -0.04, z - 0.07), (0, -0.075, z - 0.035)], [0.004, 0.004, 0.004], st))
    P.append(moved(block("Trigger", (0, 0, -0.02), (0.008, 0.01, 0.035), st, bevel=0.002, segs=1), (0, -0.035, z - 0.025), (0.3, 0, 0)))
    # the fuel valve's lever
    P.append(tube("Lever", [(0.026, 0.0, z + 0.03), (0.04, -0.03, z + 0.05), (0.045, -0.08, z + 0.05)], [0.005, 0.004, 0.005], st))
    # the front grip, under the barrel two thirds along
    P.append(moved(block("FrontGrip", (0, 0, -0.055), (0.028, 0.042, 0.11), gr, bevel=0.007, segs=2), (0, -0.4, z - 0.012), (-0.2, 0, 0)))
    P.append(along_y("Clamp", [(0.021, 0.375), (0.021, 0.43)], br, 12, 0, z))
    # the igniter: the cartridge drum, its holes round it, and its ring
    P.append(along_y("Drum", [(0.0, 0.62), (0.03, 0.62), (0.032, 0.64), (0.032, 0.72), (0.03, 0.74), (0.0, 0.74)], ol, 18, 0, z))
    for k in range(5):
        a = k / 5 * math.tau
        P.append(along_y(f"Cartridge{k}", [(0.0, 0.645), (0.007, 0.645), (0.007, 0.715), (0.0, 0.715)], br, 8, math.cos(a) * 0.033, z + math.sin(a) * 0.033))
    # the nozzle, sooted black at its mouth
    P.append(along_y("Nozzle", [(0.0, 0.74), (0.028, 0.74), (0.025, 0.82), (0.023, 0.86), (0.016, 0.86), (0.0, 0.84)], so, 16, 0, z))
    P.append(along_y("NozzleRing", [(0.029, 0.745), (0.029, 0.765)], br, 16, 0, z))
    # the hose: off the back, curving down and away over the shoulder to the tanks
    P.append(tube("Hose", [(0, 0.13, z), (0.0, 0.22, z - 0.03), (0.04, 0.3, z - 0.12), (0.1, 0.34, z - 0.26)], [0.016, 0.017, 0.017, 0.017], rb))
    P.append(along_y("HoseCollar", [(0.0, -0.17), (0.02, -0.17), (0.02, -0.13), (0.0, -0.13)], br, 12, 0, z))
    return P


# ------------------------------------------------------------------ the crates

def crate_box(L, W, H, wood, lid=True):
    """A plank crate: the box of boards, end battens, rope handles; the lid (or not)."""
    P = []
    for k in range(4):
        P.append(block(f"Side{k}", (0, -W / 2 + 0.01, H * (k + 0.5) / 4), (L, 0.02, H / 4 - 0.004), wood, bevel=0.003, segs=1))
        P.append(block(f"Back{k}", (0, W / 2 - 0.01, H * (k + 0.5) / 4), (L, 0.02, H / 4 - 0.004), wood, bevel=0.003, segs=1))
    for s in (-1, 1):
        P.append(block(f"End{s}", (s * (L / 2 - 0.01), 0, H / 2), (0.02, W - 0.04, H), wood, bevel=0.003, segs=1))
        P.append(block(f"Batten{s}", (s * (L / 2 + 0.008), 0, H / 2), (0.02, W - 0.02, 0.06), wood, bevel=0.004, segs=1))
        P.append(tube(f"Rope{s}", [(s * (L / 2 + 0.02), -0.08, H * 0.6), (s * (L / 2 + 0.05), 0, H * 0.52), (s * (L / 2 + 0.02), 0.08, H * 0.6)], [0.008, 0.009, 0.008], role("rope")))
    P.append(block("Floor", (0, 0, 0.01), (L - 0.04, W - 0.04, 0.02), wood, bevel=0.0))
    if lid:
        for k in range(3):
            P.append(block(f"Lid{k}", (0, -W / 2 + W * (k + 0.5) / 3, H + 0.01), (L + 0.02, W / 3 - 0.004, 0.02), wood, bevel=0.003, segs=1))
        for s in (-1, 1):
            P.append(block(f"LidBatten{s}", (s * (L / 2 - 0.08), 0, H + 0.03), (0.05, W, 0.02), wood, bevel=0.003, segs=1))
    return P


def flame_crate(open_lid=False):
    clear()
    wood, st = role("crate"), role("stencil")
    L, W, H = 1.2, 0.55, 0.45
    P = crate_box(L, W, H, wood, lid=not open_lid)
    P.append(stencil("Stencil1", "FLAME THROWER, PORTABLE", 0.045, (0, -W / 2 - 0.0015, H * 0.62), (math.pi / 2, 0, 0), st))
    P.append(stencil("Stencil2", "M2-2   SERIAL 322", 0.035, (0, -W / 2 - 0.0015, H * 0.42), (math.pi / 2, 0, 0), st))
    P.append(stencil("Stencil3", "CHEMICAL WARFARE SERVICE", 0.03, (0, -W / 2 - 0.0015, H * 0.25), (math.pi / 2, 0, 0), st))
    if open_lid:
        # the lid prised off, leaning against the crate's back, the straw inside
        lid = []
        for k in range(3):
            lid.append(block(f"OLid{k}", (0, -W / 2 + W * (k + 0.5) / 3, 0), (L + 0.02, W / 3 - 0.004, 0.02), wood, bevel=0.003, segs=1))
        lid = join_objs(lid, "OpenLid")
        P.append(moved(lid, (0, W / 2 + 0.2, H * 0.55), (1.15, 0, 0.05)))
        P.append(lumpy_sphere("Straw", (0, 0, H * 0.7), (L * 0.45, W * 0.4, 0.08), role("straw"), 8, amp=0.25, freq=6, subdiv=3))
    return P


def weapon_crate():
    clear()
    wood, st = role("crate"), role("stencil")
    L, W, H = 1.1, 0.45, 0.32
    P = crate_box(L, W, H, wood)
    P.append(stencil("Stencil", "RIFLES  CAL .30  M1", 0.04, (0, -W / 2 - 0.0015, H * 0.55), (math.pi / 2, 0, 0), st))
    return P


# ------------------------------------------------------------------ the war's leavings

def helmet(kind):
    clear()
    m = role("olive") if kind == "m1" else role("steel")
    P = []
    if kind == "m1":
        prof = [(0.0, 0.17), (0.06, 0.165), (0.11, 0.14), (0.14, 0.09), (0.152, 0.03), (0.165, 0.0), (0.17, -0.004), (0.163, -0.006), (0.146, 0.028),
                (0.138, 0.085), (0.108, 0.133), (0.058, 0.158), (0.0, 0.162)]
        P.append(lathe("Shell", prof, m, 32))
        P.append(tube("Strap", [(-0.15, 0, 0.02), (-0.12, -0.02, -0.08), (-0.05, -0.04, -0.13)], [0.006, 0.006, 0.005], role("leather")))
    else:
        prof = [(0.0, 0.16), (0.07, 0.15), (0.12, 0.12), (0.14, 0.07), (0.145, 0.02), (0.16, -0.03), (0.185, -0.06), (0.18, -0.065), (0.152, -0.035),
                (0.137, 0.018), (0.132, 0.068), (0.112, 0.112), (0.065, 0.142), (0.0, 0.152)]
        P.append(lathe("Shell", prof, m, 32))
    sh = P[0]
    # a dent or two, and a little rust
    rnd = random.Random(3 if kind == "m1" else 5)
    for v in sh.data.vertices:
        d = (v.co - V((0.08, -0.06, 0.12))).length
        if d < 0.06:
            v.co -= v.co.normalized() * (0.06 - d) * 0.3
    return [moved(join_objs(P, "Helmet"), (0, 0, 0.07))]


def rifle():
    clear()
    wood, st = role("wood"), role("steel")
    P = []
    # the stock: butt, wrist, fore-end (along -Y, muzzle forward)
    P.append(tube("Butt", [(0, 0.55, 0.0), (0, 0.4, 0.02), (0, 0.25, 0.04)], [(0.02, 0.06), (0.018, 0.05), (0.015, 0.03)], wood))
    P.append(tube("Fore", [(0, 0.25, 0.04), (0, 0.0, 0.05), (0, -0.4, 0.055)], [(0.016, 0.022), (0.016, 0.02), (0.014, 0.016)], wood))
    P.append(along_y("Barrel", [(0.0, -0.05), (0.01, -0.05), (0.009, 0.7), (0.0, 0.7)], st, 10, 0, 0.075))
    P.append(block("Receiver", (0, 0.12, 0.07), (0.028, 0.16, 0.035), st, bevel=0.004, segs=1))
    P.append(tube("Bolt", [(0.015, 0.1, 0.08), (0.045, 0.1, 0.08)], [0.006, 0.006], st))
    P.append(tube("Guard", [(0, 0.16, 0.03), (0, 0.12, 0.0), (0, 0.08, 0.03)], [0.004, 0.004, 0.004], st))
    for y in (-0.2, 0.05):
        P.append(along_y(f"Band{y}", [(0.021, y + 0.0), (0.021, y + 0.015)], st, 10, 0, 0.06))
    return [moved(join_objs(P, "Rifle"), (0, 0, 0.0))]


def fused(parts, name, voxel=0.014, smooth=6, folds=0.012, seed=1):
    """Parts melted into one surface (a voxel remesh), smoothed, and creased with a little noise (cloth gone stiff)."""
    ob = join_objs(parts, name)
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel)
    add_mod(ob, "SMOOTH", factor=0.6, iterations=smooth)
    apply_mods(ob)
    tex = bpy.data.textures.new(name + "Folds", "CLOUDS")
    tex.noise_scale = 0.06
    d = ob.modifiers.new("Folds", "DISPLACE")
    d.texture = tex
    d.strength = folds
    apply_mods(ob)
    for p_ in ob.data.polygons:
        p_.use_smooth = True
    return ob


def soldier_remains():
    """Slumped against a wall (behind him, +Y), legs out in front, chin on his chest."""
    clear()
    coat, bone, lea, ol = role("coat"), role("bone"), role("leather"), role("olive")
    P = []
    # the greatcoat, stiff with frost, one slumped mass: the body leaning back on the wall, the shoulders hunched, the arms
    # in its sleeves, the skirts spread over the legs
    body = [tube("Torso", [(0, 0.16, 0.12), (0, 0.13, 0.36), (0, 0.09, 0.58)], [(0.16, 0.12), (0.17, 0.12), (0.19, 0.12)], coat),
            tube("Shoulders", [(-0.21, 0.1, 0.6), (0, 0.07, 0.65), (0.21, 0.1, 0.6)], [0.07, 0.075, 0.07], coat),
            tube("Collar", [(0, 0.08, 0.62), (0, 0.04, 0.7)], [(0.075, 0.065), (0.065, 0.055)], coat),
            lumpy_sphere("Skirt", (0, -0.06, 0.09), (0.26, 0.26, 0.08), coat, 12, amp=0.15, freq=3, subdiv=3, flat_bottom=0.0)]
    for s in (-1, 1):
        body.append(tube(f"Sleeve{s}", [(s * 0.22, 0.1, 0.6), (s * 0.25, 0.02, 0.4), (s * 0.13, -0.11, 0.27)], [0.058, 0.052, 0.044], coat))
        body.append(tube(f"Leg{s}", [(s * 0.1, -0.02, 0.1), (s * 0.12, -0.35, 0.09), (s * 0.13, -0.64, 0.07)], [0.072, 0.06, 0.05], coat))
    P.append(fused(body, "Coat", seed=3))
    for s in (-1, 1):
        hand = V((s * 0.09, -0.17, 0.25))
        P.append(lumpy_sphere(f"Palm{s}", tuple(hand), (0.028, 0.032, 0.01), bone, 13 + s, amp=0.2, freq=4, subdiv=2))
        for f in range(4):
            P.append(tube(f"Finger{s}{f}", [tuple(hand + V((s * 0.012 * (f - 1.5), -0.02, 0))), tuple(hand + V((s * 0.014 * (f - 1.5), -0.06, -0.012 - 0.004 * f)))], [0.005, 0.0035], bone, 1))
        P.append(lumpy_sphere(f"Boot{s}", (s * 0.13, -0.74, 0.07), (0.056, 0.125, 0.07), lea, 15 + s, amp=0.05, freq=2, subdiv=2, flat_bottom=0.0))
    P.append(tube("Strap", [(-0.19, 0.07, 0.62), (0, -0.05, 0.45), (0.18, 0.08, 0.24)], [0.012, 0.012, 0.012], lea))
    P.append(lathe("Canteen", [(0.0, 0.0), (0.05, 0.0), (0.055, 0.05), (0.05, 0.12), (0.015, 0.14), (0.0, 0.14)], ol, 12, (0.25, 0.07, 0.12)))
    # the skull, down on the collar, chin on its chest: cranium and face fused, the sockets and the nose's hole cut deep
    # into it, the cheekbones, the jaw fallen open on its teeth
    sk = V((0, -0.02, 0.76))
    skull = fused([lumpy_sphere("Cranium", tuple(sk + V((0, 0.02, 0.03))), (0.068, 0.082, 0.072), bone, 17, amp=0.03, freq=2, subdiv=3),
                   lumpy_sphere("Face", tuple(sk + V((0, -0.045, -0.015))), (0.055, 0.042, 0.052), bone, 18, amp=0.05, freq=3, subdiv=3)]
                  + [lumpy_sphere(f"Cheek{s}", tuple(sk + V((s * 0.046, -0.05, -0.03))), (0.018, 0.022, 0.013), bone, 21 + s, amp=0.1, freq=3, subdiv=2) for s in (-1, 1)],
                  "Skull", voxel=0.006, smooth=3, folds=0.002)
    for name, at, r in (("SocketL", (-0.025, -0.085, -0.004), (0.02, 0.03, 0.017)), ("SocketR", (0.025, -0.085, -0.004), (0.02, 0.03, 0.017)), ("NoseHole", (0, -0.092, -0.032), (0.009, 0.02, 0.014))):
        cut = lumpy_sphere(name, tuple(sk + V(at)), r, bone, 30, amp=0.0, freq=1, subdiv=2)
        m = skull.modifiers.new(name, "BOOLEAN")
        m.operation = "DIFFERENCE"
        m.object = cut
        bpy.context.view_layer.objects.active = skull
        bpy.ops.object.modifier_apply(modifier=name)
        bpy.data.objects.remove(cut, do_unlink=True)
    P.append(skull)
    P.append(lumpy_sphere("Jaw", tuple(sk + V((0, -0.05, -0.088))), (0.045, 0.035, 0.016), bone, 24, amp=0.08, freq=3, subdiv=2))
    for k in range(8):
        P.append(block(f"Tooth{k}", tuple(sk + V(((k - 3.5) * 0.009, -0.078, -0.06))), (0.007, 0.006, 0.012), bone, bevel=0.0015, segs=1))
    hel = lathe("Helmet", [(0.0, 0.17), (0.06, 0.165), (0.11, 0.14), (0.14, 0.09), (0.152, 0.03), (0.165, 0.0), (0.17, -0.004), (0.163, -0.006), (0.146, 0.028),
                           (0.138, 0.085), (0.108, 0.133), (0.058, 0.158), (0.0, 0.162)], ol, 28)
    P.append(moved(hel, tuple(sk + V((0, 0.005, 0.0))), (-0.42, 0, 0.12)))
    return P


def crowbar():
    clear()
    ir = role("iron")
    P = [tube("Bar", [(0, 0.35, 0.012), (0, -0.25, 0.012), (0, -0.33, 0.03), (0, -0.36, 0.06)], [0.011, 0.011, 0.01, 0.007], ir, 1)]
    P.append(tube("Claw", [(0, 0.35, 0.012), (0, 0.42, 0.02), (0, 0.45, 0.05), (0, 0.43, 0.075)], [0.011, 0.01, 0.008, 0.005], ir, 1))
    return P


def sandbags():
    clear()
    sk = role("sack")
    rnd = random.Random(21)
    P = []
    for row in range(3):
        n = 4 - row
        for i in range(n):
            x = (i - (n - 1) / 2) * 0.5 + rnd.uniform(-0.03, 0.03)
            P.append(lumpy_sphere(f"Bag{row}{i}", (x, rnd.uniform(-0.03, 0.03), 0.08 + row * 0.14), (0.24, 0.15, 0.08), sk, 30 + row * 5 + i, amp=0.08, freq=2.5, subdiv=3, flat_bottom=row * 0.14))
    return P


PIECES = {
    "flamethrower_wand": (flamethrower_wand, (0, -0.4, 0.03), 1.2, 0.2, 30),
    "flame_crate": (lambda: flame_crate(False), (0, 0, 0.25), 2.0, 0.6, 25),
    "flame_crate_open": (lambda: flame_crate(True), (0, 0, 0.25), 2.0, 0.8, 25),
    "weapon_crate": (weapon_crate, (0, 0, 0.18), 1.8, 0.6, 25),
    "helmet_m1": (lambda: helmet("m1"), (0, 0, 0.12), 0.7, 0.3, 30),
    "helmet_stahl": (lambda: helmet("stahl"), (0, 0, 0.12), 0.7, 0.3, 30),
    "rifle": (rifle, (0, 0.1, 0.05), 1.5, 0.4, 30),
    "soldier_remains": (soldier_remains, (0, -0.2, 0.45), 2.2, 0.4, 30),
    "crowbar": (crowbar, (0, 0, 0.03), 1.0, 0.4, 30),
    "sandbags": (sandbags, (0, 0, 0.2), 1.8, 0.4, 25),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        fn, tgt, dist, h, yaw = PIECES[name]
        export(fn(), name, tgt, dist, h, yaw)
