"""The player's own body, seen in first person (2026-10-11, the owner's list: "A first-person body: look down and see your
legs and boots; your shadow has arms" and "it needs to look really really good ... do a lot of modeling of the body before
we implement it"). Modelled, baked and rigged here; posed by the game every frame (PlayerBody.cs: the legs by IK onto the
ground under them, the arms swinging, the hips and the spine), so there are no baked clips.

    blender -b --python tools/Blender/player_body.py [-- shape]

("shape" stops after the shapes, with previews: quicker to look at while modelling.)

Writes:
  assets/models/player/player_body.glb   the hiker, skinned to a skeleton, in separate meshes the game treats apart:
                                         Torso (the jacket's body, the straps), Legs (jeans), Boots (seen: looked down
                                         at), and Arms, Hands, Head, Pack (in the shadow only: in first person the hands
                                         in view are the held things' own, and the head is where the eye is)
  build/blender/player_body_*.png        previews: front, three-quarter, back, and from the eye looking down

Who: a day hiker dressed for late October. A dark olive softshell jacket (a zip, two chest pockets, a collar, the hood
rolled at the back, cuffs), a small rust-brown day pack (two padded shoulder straps, a sternum strap and its buckle),
indigo jeans, worn at the thighs and knees, a little stacked over the boots, brown leather hiking boots (a lugged black
sole, a rubber toe cap, a padded collar, tan laces criss-crossed up the eyelets), dark brown gloves, a charcoal beanie.

How it's made (as the stalker and the wendigo):
  1. each garment lofted on rings along the body (the jacket on the spine, the jeans down each leg, the boot along the
     foot and up the ankle), joined to its sleeves or legs and fused by a voxel remesh, so it drapes as one;
  2. what isn't cloth (the zip, the pockets' flaps, the soles, the laces and eyelets, the buckle) kept crisp, not fused;
  3. a high-detail copy (each material's own weave as a procedural bump: the softshell's fine grain and its seams, the
     jeans' twill, the leather's grain and creases, the sole's lugs) baked onto the export copy: 2048 px albedo (the
     painted colours times their occlusion) and normal maps;
  4. the skeleton (hips, spine, chest, neck, head; clavicles, arms, hands, fingers, thumbs; thighs, shins, feet, toes;
     the pack on the chest), and the weights, each part only to the bones it can move with (the jacket's hem to the
     hips, never the thighs).

Blender is Z-up, the figure facing +Y, X its right; the glTF export makes that the game's -Z with Y up. The eye is at
1.62 m (the game's PlayerCameraRig.EyeHeight), 0.09 m in front of the spine.
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, lumpy_sphere, cylinder, join, apply_mods, add_mod, obj_from_bmesh, fbm, ROOT
import kit
import wendigo as W

OUT = os.path.join(ROOT, "assets", "models", "player")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREV, exist_ok=True)
V = Vector
TEXSIZE = 2048
rnd = random.Random(1162)
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

EYE = V((0, 0.09, 1.62))
CHEST_CUT = 1.0   # the jacket seen from the eye up to here; above, in the shadow only

# ------------------------------------------------------------------ the proportions


def spine_at(t):
    """The spine from the pelvis (0) to the base of the neck (1): upright, a slight S."""
    z = 0.95 + 0.53 * t
    y = -0.012 * math.sin(t * math.pi) + 0.008 * t
    return V((0, y, z))


def arm_joints(s):
    """s: 1 its right, -1 its left. Arms hanging, a little out from the body (the rest pose)."""
    return dict(clav=V((s * 0.03, 0.015, 1.455)), shoulder=V((s * 0.185, -0.01, 1.435)), elbow=V((s * 0.235, -0.035, 1.15)),
                wrist=V((s * 0.26, 0.0, 0.9)), palm=V((s * 0.27, 0.012, 0.83)), tip=V((s * 0.272, 0.02, 0.74)),
                thumb0=V((s * 0.255, 0.035, 0.87)), thumb=V((s * 0.248, 0.06, 0.8)))


def leg_joints(s):
    return dict(hip=V((s * 0.092, 0.0, 0.93)), knee=V((s * 0.098, 0.018, 0.505)), ankle=V((s * 0.1, -0.012, 0.092)),
                ball=V((s * 0.104, 0.13, 0.03)), toe=V((s * 0.106, 0.215, 0.025)))


HEAD = V((0, 0.012, 1.64))      # the skull's centre
NECK0 = V((0, 0.0, 1.47))
NECK1 = V((0, 0.012, 1.56))


# ------------------------------------------------------------------ materials (the colours are painted; these are the
# surfaces' kinds, for the bake's weave and the game's roughness)

def materials():
    return dict(
        jacket=mat("pb_jacket", (0.024, 0.03, 0.024), 0.85), sleeve=mat("pb_sleeve", (0.024, 0.03, 0.024), 0.85),
        jeans=mat("pb_jeans", (0.03, 0.04, 0.08), 0.95), leather=mat("pb_leather", (0.1, 0.05, 0.025), 0.6),
        sole=mat("pb_sole", (0.02, 0.018, 0.016), 0.9), lace=mat("pb_lace", (0.25, 0.17, 0.08), 0.9),
        metal=mat("pb_metal", (0.25, 0.24, 0.22), 0.35, 0.8), pack=mat("pb_pack", (0.15, 0.055, 0.03), 0.9),
        strap=mat("pb_strap", (0.02, 0.02, 0.02), 0.9), glove=mat("pb_glove", (0.012, 0.009, 0.007), 0.75),
        skin=mat("pb_skin", (0.45, 0.3, 0.22), 0.6), knit=mat("pb_knit", (0.03, 0.03, 0.03), 1.0),
        plastic=mat("pb_plastic", (0.01, 0.01, 0.01), 0.4), collar=mat("pb_collar", (0.024, 0.03, 0.024), 0.85),
        welt=mat("pb_welt", (0.09, 0.07, 0.05), 0.7), chest=mat("pb_chest", (0.038, 0.047, 0.034), 0.85))


def remesh(ob, voxel, smooth=4):
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel, adaptivity=0.0)
    apply_mods(ob)
    add_mod(ob, "SMOOTH", factor=0.6, iterations=smooth)
    apply_mods(ob)
    return ob


def chain(name, pts, radii, subdiv=1):
    return W.chain(name, pts, radii, subdiv)


def set_mat(ob, m):
    ob.data.materials.clear()
    ob.data.materials.append(m)
    for p in ob.data.polygons:
        p.material_index = 0
    return ob


def loft(name, rings_fn, nt, nr, cap0=True, cap1=True, subdiv=0):
    """A closed loft: rings_fn(i/nt) -> list of nr points."""
    bm = bmesh.new()
    rings = [[bm.verts.new(p) for p in rings_fn(i / nt)] for i in range(nt + 1)]
    for i in range(nt):
        for j in range(nr):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % nr], rings[i + 1][(j + 1) % nr], rings[i + 1][j]))
    for ring, do in ((rings[0], cap0), (rings[-1], cap1)):
        if do:
            c = sum((v.co for v in ring), V()) / len(ring)
            cv = bm.verts.new(c)
            for j in range(nr):
                bm.faces.new((ring[j], ring[(j + 1) % nr], cv))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name)
    if subdiv:
        add_mod(ob, "SUBSURF", levels=subdiv)
        apply_mods(ob)
    return ob


def superellipse(c, hx, hy, a, n=2.5, front=0.0):
    """A point round a rounded-box section: half-widths hx (X) and hy (Y), at angle a (0 its right, pi/2 its front);
    `front` pushes the front out (a chest)."""
    ca, sa = math.cos(a), math.sin(a)
    x = math.copysign(abs(ca) ** (2 / n), ca) * hx
    y = math.copysign(abs(sa) ** (2 / n), sa) * hy
    if y > 0:
        y *= 1 + front * (sa ** 2)
    return c + V((x, y, 0))


# ------------------------------------------------------------------ the jacket

JACKET = [   # (z, half-width, half-depth, the chest's push forward)
    (0.82, 0.2, 0.148, 0.0),
    (0.9, 0.195, 0.14, 0.0),
    (0.98, 0.186, 0.132, 0.0),
    (1.06, 0.18, 0.128, 0.02),
    (1.15, 0.184, 0.132, 0.06),
    (1.25, 0.193, 0.136, 0.1),
    (1.34, 0.2, 0.134, 0.1),
    (1.405, 0.204, 0.122, 0.06),
    (1.445, 0.175, 0.102, 0.02),
    (1.475, 0.115, 0.083, 0.0),
    (1.5, 0.078, 0.07, 0.0),
]


def jacket_section(z):
    for (z0, w0, d0, f0), (z1, w1, d1, f1) in zip(JACKET, JACKET[1:]):
        if z <= z1:
            u = (z - z0) / (z1 - z0)
            u = u * u * (3 - 2 * u)
            return w0 + (w1 - w0) * u, d0 + (d1 - d0) * u, f0 + (f1 - f0) * u
    return JACKET[-1][1:]


def build_jacket(m):
    z0, z1 = JACKET[0][0], JACKET[-1][0]
    nr = 44

    def ring(t):
        z = z0 + (z1 - z0) * t
        hx, hy, f = jacket_section(z)
        c = spine_at((z - 0.95) / 0.53) if z > 0.95 else V((0, 0, z))
        c = V((0, c.y, z))
        pts = []
        for j in range(nr):
            a = j / nr * math.tau
            p = superellipse(c, hx, hy, a, 2.6, f)
            # folds: soft vertical drape below the chest, a gather over the hem's band
            fold = 0.0035 * math.sin(a * 7 + 1.3) * max(0.0, 1.15 - z) / 0.3
            out = (p - c).normalized()
            pts.append(p + out * fold)
        return pts
    torso = loft("JacketTorso", ring, 40, nr)
    parts = [torso]
    for s in (1, -1):
        J = arm_joints(s)
        sh, el, wr = J["shoulder"], J["elbow"], J["wrist"]
        # the sleeve: a little loose, gathered at the elbow, a cuff
        parts.append(chain(f"Sleeve{s}", [J["clav"] + V((s * 0.08, -0.01, -0.01)), sh + V((s * 0.0, 0, 0.0)), sh.lerp(el, 0.35), sh.lerp(el, 0.75), el,
                                          el.lerp(wr, 0.4), el.lerp(wr, 0.85), wr + (wr - el).normalized() * 0.01],
                           [0.082, 0.082, 0.076, 0.068, 0.066, 0.062, 0.056, 0.054]))
        parts.append(lumpy_sphere(f"ShoulderCap{s}", sh + V((-s * 0.02, -0.005, 0.0)), (0.085, 0.08, 0.075), None, 31 + s, amp=0.015, subdiv=3))
        # creases at the inside of the elbow
        for k in range(3):
            a = el + (wr - el).normalized() * (-0.03 + k * 0.03) + V((0, 0.045, 0))
            parts.append(lumpy_sphere(f"ElbowFold{s}{k}", a, (0.04, 0.012, 0.009), None, 40 + k + s, amp=0.05, subdiv=2))
    # the collar: a stand-up band round the neck
    for k in range(3):
        z = 1.49 + k * 0.022
        parts.append(W.loft_path(f"Collar{k}", [V((0, 0.0, z - 0.012)), V((0, 0.0, z + 0.012))], [0.083 - k * 0.004] * 2, [0.078 - k * 0.004] * 2,
                                 up=V((0, 1, 0)), sides=28))
    # the hood, rolled up at the back of the neck
    parts.append(chain("Hood", [V((0.1, -0.04, 1.49)), V((0.08, -0.09, 1.51)), V((0.0, -0.105, 1.52)), V((-0.08, -0.09, 1.51)), V((-0.1, -0.04, 1.49))],
                       [0.03, 0.04, 0.045, 0.04, 0.03]))
    # the hem's band
    parts.append(W.loft_path("HemBand", [V((0, 0.0, 0.815)), V((0, 0.0, 0.85))], [0.204, 0.198], [0.152, 0.146], up=V((0, 1, 0)), sides=40))
    jacket = join(parts, "Jacket")
    remesh(jacket, 0.0055, 5)
    # the neck's opening: cut it (it closed in the remesh)
    bm = bmesh.new()
    bm.from_mesh(jacket.data)
    kill = [v for v in bm.verts if v.co.z > 1.535 and V((v.co.x, v.co.y)).length < 0.065]
    kill += [v for v in bm.verts if v.co.z < 0.83 and V((v.co.x, v.co.y - 0.0)).length < 0.182]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(jacket.data)
    bm.free()
    set_mat(jacket, m["jacket"])
    print(f"[body] jacket: {len(jacket.data.polygons)} faces")
    return jacket


def build_jacket_details(m):
    """The zip, the chest pockets' flaps and zips, the cuffs' tabs: crisp, laid on the jacket's surface."""
    out = []
    # the zip up the front: a raised placket and its teeth, a pull at the top
    pts = []
    for i in range(24):
        z = 0.825 + i * (1.535 - 0.825) / 23
        hx, hy, f = jacket_section(min(z, 1.5))
        c = spine_at((z - 0.95) / 0.53) if z > 0.95 else V((0, 0, z))
        y = c.y + hy * (1 + f) + 0.002
        if z > 1.49:
            y = 0.085 - (z - 1.49) * 0.1
        pts.append(V((0, y, z)))
    plack = W.loft_path("Placket", pts, [0.012] * len(pts), [0.0035] * len(pts), up=V((0, 1, 0)), sides=8)
    set_mat(plack, m["chest"])
    out.append(plack)
    teeth = W.loft_path("ZipTeeth", [p + V((0, 0.0035, 0)) for p in pts], [0.0035] * len(pts), [0.0016] * len(pts), up=V((0, 1, 0)), sides=6)
    set_mat(teeth, m["plastic"])
    out.append(teeth)
    top = pts[-3]
    pull = kit.box("ZipPull", top + V((0, 0.006, -0.018)), (0.007, 0.002, 0.026), None, bevel=0.0008)
    set_mat(pull, m["metal"])
    out.append(pull)
    # the chest pockets: a flap and a zip across each, a little proud
    for s in (1, -1):
        z = 1.3
        hx, hy, f = jacket_section(z)
        x0, x1 = s * 0.035, s * 0.14
        flap_pts = []
        for k in range(9):
            x = x0 + (x1 - x0) * k / 8
            a = math.atan2(1, 0.0001)
            # the surface's y at this x: the section's front edge
            u = abs(x) / hx
            y = hy * (1 + f) * max(0.0, 1 - u ** 2.6) ** (1 / 2.6) + 0.003
            flap_pts.append(V((x, y, z)))
        flap = W.loft_path(f"PocketFlap{s}", flap_pts, [0.03] * 9, [0.004] * 9, up=V((0, 1, 0)), sides=8)
        set_mat(flap, m["chest"])
        out.append(flap)
        zipl = W.loft_path(f"PocketZip{s}", [p + V((0, 0.003, 0.028)) for p in flap_pts], [0.003] * 9, [0.0015] * 9, up=V((0, 1, 0)), sides=6)
        set_mat(zipl, m["plastic"])
        out.append(zipl)
        p = flap_pts[1] + V((0, 0.006, 0.02))
        pp = kit.box(f"PocketPull{s}", p, (0.005, 0.002, 0.02), None, bevel=0.0007)
        set_mat(pp, m["metal"])
        out.append(pp)
    # the cuffs: a band and a tab
    for s in (1, -1):
        J = arm_joints(s)
        d = (J["wrist"] - J["elbow"]).normalized()
        c0 = J["wrist"] - d * 0.035
        cuff = W.loft_path(f"Cuff{s}", [c0, J["wrist"] + d * 0.012], [0.049, 0.048], [0.049, 0.048], up=V((1, 0, 0)), sides=20)
        set_mat(cuff, m["sleeve"])
        out.append(cuff)
    return out


# ------------------------------------------------------------------ the jeans

def build_jeans(m):
    parts = []

    def pelvis(t):
        z = 0.8 + 0.2 * t
        hx = 0.168 + 0.012 * math.sin(t * math.pi)
        hy = 0.112 + 0.006 * t
        return [superellipse(V((0, -0.005, z)), hx, hy, j / 32 * math.tau, 2.4) for j in range(32)]
    parts.append(loft("Pelvis", pelvis, 8, 32))
    for s in (1, -1):
        L = leg_joints(s)
        hp, kn, an = L["hip"], L["knee"], L["ankle"]
        hem = an + V((0, 0.012, 0.055))
        parts.append(chain(f"Leg{s}", [hp + V((s * 0.0, 0, 0.04)), hp.lerp(kn, 0.3), hp.lerp(kn, 0.7), kn, kn.lerp(hem, 0.35), kn.lerp(hem, 0.75), hem],
                           [0.112, 0.1, 0.084, 0.074, 0.072, 0.066, 0.068]))
        # the stack over the boot: a few soft rings, and the knee's give
        for k in range(2):
            c = hem + V((s * 0.003 * (k - 1), 0.004 * (1 - k), 0.04 + k * 0.045 + 0.006 * math.sin(k * 2.1 + s)))
            st = lumpy_sphere(f"Stack{s}{k}", c, (0.066, 0.066, 0.009), None, 50 + k * 3 + s, amp=0.12, freq=3.0, subdiv=2)
            st.rotation_euler = (0.18 * math.sin(k * 1.7 + s), 0.14 * math.cos(k * 2.3), 0)
            parts.append(st)
        parts.append(lumpy_sphere(f"KneeBag{s}", kn + V((0, 0.03, -0.01)), (0.06, 0.03, 0.045), None, 60 + s, amp=0.04, subdiv=3))
        # a fold behind the knee
        parts.append(lumpy_sphere(f"KneeBack{s}", kn + V((0, -0.045, 0.01)), (0.05, 0.018, 0.014), None, 64 + s, amp=0.06, subdiv=2))
    jeans = join(parts, "Jeans")
    remesh(jeans, 0.005, 4)
    # open the hems
    bm = bmesh.new()
    bm.from_mesh(jeans.data)
    # (and the waist, all under the jacket: unseen, it only shadowed the jacket in the bake)
    kill = [v for v in bm.verts if v.co.z < 0.135 or v.co.z > 0.9]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(jeans.data)
    bm.free()
    set_mat(jeans, m["jeans"])
    print(f"[body] jeans: {len(jeans.data.polygons)} faces")
    return jeans


# ------------------------------------------------------------------ the boots

def boot_profile(s):
    """The boot's path from the toe, along the foot and up the ankle, and its section at each point (half-width,
    height from the path down to the sole's top: an arch over the foot)."""
    L = leg_joints(s)
    x = L["ankle"].x
    pts = [V((x, 0.236, 0.042)), V((x, 0.228, 0.042)), V((x + s * 0.002, 0.2, 0.046)), V((x + s * 0.004, 0.16, 0.052)), V((x + s * 0.003, 0.12, 0.06)), V((x, 0.075, 0.074)),
           V((x - s * 0.002, 0.035, 0.095)), V((x - s * 0.003, 0.0, 0.12)), V((x - s * 0.002, -0.018, 0.15)), V((x, -0.02, 0.175))]
    hw = [0.012, 0.03, 0.046, 0.054, 0.055, 0.053, 0.05, 0.049, 0.053, 0.056]
    hh = [0.014, 0.03, 0.04, 0.046, 0.049, 0.054, 0.06, 0.064, 0.056, 0.054]
    return pts, hw, hh


def build_boot(m, s):
    L = leg_joints(s)
    pts, hw, hh = boot_profile(s)
    parts = []
    # the upper: along the foot then up, flat-bottomed (the sole's top at z 0.03)
    def squash(t, a):
        return 1.0
    nr = 28
    n = len(pts)

    def ring(t):
        i = min(int(t * (n - 1)), n - 2)
        u = t * (n - 1) - i
        c = pts[i].lerp(pts[i + 1], u)
        w = hw[i] + (hw[i + 1] - hw[i]) * u
        h = hh[i] + (hh[i + 1] - hh[i]) * u
        tan = (pts[min(i + 1, n - 1)] - pts[i]).normalized()
        side = V((s, 0, 0))
        up = side.cross(tan).normalized() * s
        if up.z < 0:
            up = -up
        out = []
        for j in range(nr):
            a = j / nr * math.tau
            ca, sa = math.cos(a), math.sin(a)
            # top half an arch; bottom half flattened onto the sole
            r_up = h * (abs(sa) ** 0.8) * (1 if sa >= 0 else 0.0)
            p = c + side * (math.copysign(abs(ca) ** 0.7, ca) * w) + up * r_up
            if sa < 0:
                p = V((p.x, p.y, max(0.03, min(p.z, c.z - 0.0))))
                p.z = 0.03 + (p.z - 0.03) * 0.2
            out.append(p)
        return out
    upper = loft(f"BootUpper{s}", ring, 32, nr)
    parts.append(upper)
    # the heel counter and the ankle's padded collar
    parts.append(lumpy_sphere(f"Heel{s}", V((L["ankle"].x, -0.05, 0.065)), (0.046, 0.04, 0.05), None, 70 + s, amp=0.02, subdiv=3, flat_bottom=0.03))
    parts.append(W.loft_path(f"Collar{s}", [V((L["ankle"].x, -0.022, 0.165)), V((L["ankle"].x, -0.024, 0.19))], [0.056, 0.054], [0.054, 0.052],
                             up=V((0, 1, 0)), sides=24))
    # the shaft up the ankle, and the ankle bones
    parts.append(chain(f"Shaft{s}", [V((L["ankle"].x, -0.015, 0.06)), V((L["ankle"].x, -0.02, 0.17))], [0.05, 0.05]))
    # the tongue, rising out of the lacing at the top
    parts.append(chain(f"Tongue{s}", [V((L["ankle"].x, 0.05, 0.11)), V((L["ankle"].x, 0.03, 0.17)), V((L["ankle"].x, 0.02, 0.2))], [0.022, 0.022, 0.018]))
    boot = join(parts, f"Boot{s}")
    remesh(boot, 0.0035, 3)
    bm = bmesh.new()
    bm.from_mesh(boot.data)
    kill = [v for v in bm.verts if v.co.z > 0.185 and (V((v.co.x, v.co.y)) - V((L["ankle"].x, -0.02))).length < 0.042]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(boot.data)
    bm.free()
    set_mat(boot, m["leather"])

    out = [boot]
    # the sole: a lugged slab under it all, a little wider, the heel block deeper
    def sole_ring(t):
        y = 0.24 - t * 0.335
        half = 0.03 + 0.033 * math.sin(min(1, max(0, (0.245 - y) / 0.12)) * math.pi * 0.5)
        if y < 0.06:
            half = 0.061 - 0.01 * max(0, (0.03 - y) / 0.07) - 0.012 * max(0, (-0.05 - y) / 0.04)
        if y < -0.08:
            half *= 0.85
        bottom = 0.0
        top = 0.036 if y > 0.05 else 0.044
        cx = L["ankle"].x + s * 0.004 * math.sin(t * math.pi)
        ring = []
        for j in range(16):
            a = j / 16 * math.tau
            ca, sa = math.cos(a), math.sin(a)
            x = cx + math.copysign(abs(ca) ** 0.35, ca) * half
            z = (top + bottom) / 2 + math.copysign(abs(sa) ** 0.35, sa) * (top - bottom) / 2
            ring.append(V((x, y, z)))
        return ring
    sole = loft(f"Sole{s}", sole_ring, 24, 16)
    # the toe's spring: the front lifted
    for v in sole.data.vertices:
        if v.co.y > 0.16:
            v.co.z += (v.co.y - 0.16) ** 2 * 3.0
    set_mat(sole, m["sole"])
    out.append(sole)
    lugs = []
    for k in range(26):
        t = (k + 0.5) / 26
        ring = sole_ring(t)
        for j, side in ((0, 1), (8, -1)):
            p = ring[j]
            q = V((L["ankle"].x, p.y, 0)).lerp(V((p.x, p.y, 0)), 0.93)
            lugs.append(kit.box(f"Lug{s}{k}{side}", V((q.x, q.y, 0.004)), (0.01, 0.008, 0.009), None, bevel=0.0015))
    for k in range(4):
        lugs.append(kit.box(f"HeelLug{s}{k}", V((L["ankle"].x + (k - 1.5) * 0.022, -0.07, 0.004)), (0.014, 0.008, 0.009), None, bevel=0.0015))
    # (the lugs as solids read as teeth from above: they're in the bake's relief instead)
    for o in lugs:
        bpy.data.objects.remove(o, do_unlink=True)
    def mid_ring(t):
        return [p + V((0, 0, 0)) for p in sole_ring(t)]
    welt = loft(f"Welt{s}", lambda t: [V((p.x + (p.x - L["ankle"].x) * 0.03, p.y, min(max(p.z, 0.03), 0.042))) for p in sole_ring(0.02 + t * 0.96)], 24, 16)
    set_mat(welt, m["welt"])
    out.append(welt)
    # the rubber toe cap
    cap_pts = [V((L["ankle"].x, 0.12, 0.05)), V((L["ankle"].x + s * 0.002, 0.18, 0.045)), V((L["ankle"].x, 0.222, 0.042))]
    cap = W.loft_path(f"ToeCap{s}", cap_pts, [0.054, 0.046, 0.02], [0.03, 0.026, 0.016], up=V((0, 0, 1)), sides=20)
    for v in cap.data.vertices:
        v.co.z = max(v.co.z, 0.03)
    set_mat(cap, m["sole"])
    out.append(cap)
    # the eyelets and the laces: five crossings up the instep, then two hooks each side, a bow at the top
    lace_path = []
    for k in range(7):
        u = k / 6
        i = 4 + u * 3.2
        i0 = int(i)
        f = i - i0
        c = pts[i0].lerp(pts[min(i0 + 1, n - 1)], f)
        h = hh[i0] + (hh[min(i0 + 1, n - 1)] - hh[i0]) * f
        c = c + V((0, 0, h)) + V((0, 0.006 * (1 - u), 0))
        lace_path.append(c)
    for k, c in enumerate(lace_path):
        for side in (1, -1):
            e = c + V((side * 0.022, 0, -0.004))
            ring = cylinder(f"Eyelet{s}{k}{side}", e - V((0, 0, 0.002)), e + V((0, 0, 0.004)), 0.0055, 0.0055, sides=8)
            set_mat(ring, m["metal"])
            out.append(ring)
    for k in range(len(lace_path) - 1):
        a, b = lace_path[k], lace_path[k + 1]
        for side in (1, -1):
            l = cylinder(f"Lace{s}{k}{side}", a + V((side * 0.021, 0, 0.004)), b + V((-side * 0.021, 0, 0.004)), 0.0036, 0.0036, sides=6)
            set_mat(l, m["lace"])
            out.append(l)
    top = lace_path[-1] + V((0, 0.006, 0.006))
    for side in (1, -1):
        loop = W.loft_path(f"Bow{s}{side}", [top, top + V((side * 0.025, 0.012, 0.004)), top + V((side * 0.012, 0.022, -0.002)), top + V((0, 0.004, 0))],
                           [0.003] * 4, [0.003] * 4, up=V((0, 0, 1)), sides=6)
        set_mat(loop, m["lace"])
        out.append(loop)
        tail = W.loft_path(f"LaceEnd{s}{side}", [top, top + V((side * 0.01, 0.03, -0.03)), top + V((side * 0.015, 0.035, -0.055))],
                           [0.0028] * 3, [0.0028] * 3, up=V((0, 1, 0)), sides=6)
        set_mat(tail, m["lace"])
        out.append(tail)
    print(f"[body] boot {s}: {sum(len(o.data.polygons) for o in out)} faces")
    return out


# ------------------------------------------------------------------ the hands, the head, the pack

def build_hand(m, s):
    J = arm_joints(s)
    wr, palm, tip = J["wrist"], J["palm"], J["tip"]
    parts = [chain(f"Wrist{s}", [wr + (wr - J["elbow"]).normalized() * -0.02, wr, wr.lerp(palm, 0.6)], [0.032, 0.03, 0.03])]
    parts.append(lumpy_sphere(f"Palm{s}", wr.lerp(palm, 0.75), (0.018, 0.042, 0.048), None, 80 + s, amp=0.03, subdiv=3))
    for k in range(4):
        off = V((0, (k - 1.5) * 0.019, 0))
        base = palm + off
        mid = base + V((s * 0.006, 0.006, -0.04))
        end = mid + V((-s * 0.004, 0.012, -0.03))
        parts.append(chain(f"Finger{s}{k}", [base, mid, end], [0.0105, 0.0095, 0.0085]))
    parts.append(chain(f"Thumb{s}", [J["thumb0"], J["thumb0"].lerp(J["thumb"], 0.5) + V((0, 0.005, 0)), J["thumb"]], [0.013, 0.011, 0.0095]))
    hand = join(parts, f"Hand{s}")
    remesh(hand, 0.003, 3)
    set_mat(hand, m["glove"])
    return hand


def build_head(m):
    parts = [chain("Neck", [NECK0 + V((0, 0, -0.02)), NECK1, HEAD + V((0, -0.01, -0.03))], [0.05, 0.048, 0.05])]
    parts.append(lumpy_sphere("Skull", HEAD + V((0, -0.008, 0.01)), (0.08, 0.098, 0.108), None, 90, amp=0.01, subdiv=4))
    parts.append(lumpy_sphere("Jaw", HEAD + V((0, 0.035, -0.06)), (0.062, 0.06, 0.05), None, 91, amp=0.01, subdiv=3))
    parts.append(lumpy_sphere("Chin", HEAD + V((0, 0.075, -0.088)), (0.025, 0.02, 0.022), None, 92, amp=0.01, subdiv=2))
    parts.append(lumpy_sphere("Nose", HEAD + V((0, 0.1, -0.015)), (0.013, 0.02, 0.025), None, 89, amp=0.02, subdiv=2))
    for s in (1, -1):
        parts.append(lumpy_sphere(f"Ear{s}", HEAD + V((s * 0.076, -0.005, -0.005)), (0.01, 0.018, 0.026), None, 93 + s, amp=0.02, subdiv=2))
        parts.append(lumpy_sphere(f"Brow{s}", HEAD + V((s * 0.033, 0.088, 0.03)), (0.026, 0.012, 0.01), None, 95 + s, amp=0.02, subdiv=2))
    head = join(parts, "Head")
    remesh(head, 0.004, 4)
    set_mat(head, m["skin"])
    # the beanie: a knit dome over the top and back, its turned-up band
    beanie = lumpy_sphere("BeanieDome", HEAD + V((0, -0.012, 0.03)), (0.088, 0.106, 0.105), None, 97, amp=0.015, subdiv=4)
    bm = bmesh.new()
    bm.from_mesh(beanie.data)
    cut = HEAD.z + 0.005
    kill = [v for v in bm.verts if v.co.z < cut - 0.03 * max(0.0, -v.co.y + HEAD.y) / 0.1]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(beanie.data)
    bm.free()
    band = W.loft_path("BeanieBand", [HEAD + V((0, -0.015, 0.0)), HEAD + V((0, -0.015, 0.035))], [0.092, 0.09], [0.11, 0.108], up=V((0, 1, 0)), sides=32)
    hat = join([beanie, band], "Beanie")
    set_mat(hat, m["knit"])
    return head, hat


STRAP_PATH = lambda s: [V((s * 0.075, -0.16, 1.37)), V((s * 0.1, -0.11, 1.455)), V((s * 0.115, -0.03, 1.488)), V((s * 0.118, 0.05, 1.468)),
                        V((s * 0.118, 0.118, 1.4)), V((s * 0.116, 0.142, 1.3)), V((s * 0.122, 0.142, 1.2)), V((s * 0.158, 0.11, 1.1)),
                        V((s * 0.19, 0.02, 1.05)), V((s * 0.17, -0.12, 1.04))]


def strap(name, pts, ws, hs, sides=12):
    """A strap along pts: a rounded flat section, its flat against the body (the normal out from the torso: forward on
    the chest, up over the shoulder, back behind), `ws` wide and `hs` thick."""
    bm = bmesh.new()
    rings = []
    n = len(pts)
    for i, c in enumerate(pts):
        tan = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        out = V((c.x * 0.25, c.y, max(0.0, c.z - 1.42) * 9.0))
        out = (out - tan * out.dot(tan)).normalized()
        side = tan.cross(out).normalized()
        ring = []
        for j in range(sides):
            a = j / sides * math.tau
            ca, sa = math.cos(a), math.sin(a)
            x = math.copysign(abs(ca) ** 0.4, ca) * ws[i] * 0.5
            y = math.copysign(abs(sa) ** 0.6, sa) * hs[i] * 0.5
            ring.append(bm.verts.new(c + side * x + out * (y + hs[i] * 0.5)))
        rings.append(ring)
    for i in range(n - 1):
        for j in range(sides):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % sides], rings[i + 1][(j + 1) % sides], rings[i + 1][j]))
    for ring in (rings[0], rings[-1]):
        bm.faces.new(ring)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name)
    add_mod(ob, "SUBSURF", levels=1)
    apply_mods(ob)
    return ob


def build_pack(m):
    out = []
    body = kit.box("PackBody", V((0, -0.235, 1.22)), (0.29, 0.17, 0.42), None, bevel=0.045, segs=4)
    for v in body.data.vertices:
        # a soft, slightly bulging bag: rounder at the back, narrower at the top
        u = (v.co.z - 0.0) / 0.21
        v.co.x *= 1 - 0.08 * max(0, u)
        if v.co.y < 0:
            v.co.y *= 1 + 0.15 * (1 - u * u)
    lid = kit.box("PackLid", V((0, -0.235, 1.445)), (0.27, 0.18, 0.06), None, bevel=0.025, segs=3)
    pocket = kit.box("PackPocket", V((0, -0.33, 1.14)), (0.2, 0.05, 0.2), None, bevel=0.02, segs=3)
    side = [kit.box(f"PackSide{s}", V((s * 0.155, -0.235, 1.12)), (0.03, 0.1, 0.16), None, bevel=0.012, segs=2) for s in (1, -1)]
    pack = join([body, lid, pocket] + side, "Pack")
    pack.location.z -= 0.035
    add_mod(pack, "SUBSURF", levels=1)
    apply_mods(pack)
    set_mat(pack, m["pack"])
    out.append(pack)
    # the straps: padded over the shoulders, webbing down the front
    for s in (1, -1):
        pts = STRAP_PATH(s)
        ws = [0.03, 0.034, 0.036, 0.034, 0.03, 0.026, 0.02, 0.014, 0.012, 0.012]
        hs = [0.008, 0.012, 0.013, 0.012, 0.01, 0.008, 0.004, 0.003, 0.003, 0.003]
        st = strap(f"Strap{s}", pts, ws, hs)
        set_mat(st, m["strap"])
        out.append(st)
    # the sternum strap and its buckle
    a, b = STRAP_PATH(1)[4].lerp(STRAP_PATH(1)[5], 0.4), STRAP_PATH(-1)[4].lerp(STRAP_PATH(-1)[5], 0.4)
    ster = strap("Sternum", [a + V((0, 0.006, 0)), (a + b) / 2 + V((0, 0.012, 0)), b + V((0, 0.006, 0))], [0.016] * 3, [0.003] * 3)
    set_mat(ster, m["strap"])
    out.append(ster)
    buckle = kit.box("Buckle", (a + b) / 2 + V((0, 0.016, 0)), (0.04, 0.008, 0.018), None, bevel=0.003)
    set_mat(buckle, m["plastic"])
    out.append(buckle)
    return out


# ------------------------------------------------------------------ colours

def jacket_colour(co, n):
    base = V((0.022, 0.03, 0.018))
    g = 0.92 + 0.16 * fbm(co.x * 14, co.y * 14, co.z * 14, 3)
    # the seams a touch darker: down the sides, across the yoke
    seam = 1.0
    if abs(abs(co.x) - 0.17) < 0.006 and co.z < 1.4:
        seam = 0.7
    if abs(co.z - 1.4) < 0.004 and co.y < 0:
        seam = 0.75
    return tuple(base * g * seam)


def jeans_colour(co, n):
    base = V((0.016, 0.024, 0.07))
    worn = V((0.04, 0.055, 0.11))
    # fades: the fronts of the thighs, the knees; the hems and the back of the knee darker
    f = 0.0
    if n.y > 0.2:
        f += 0.45 * max(0, 1 - abs(co.z - 0.72) / 0.14) + 0.4 * max(0, 1 - abs(co.z - 0.5) / 0.06)
    f *= 0.7 + 0.6 * fbm(co.x * 20, co.y * 20, co.z * 6, 3)
    c = base.lerp(worn, min(1.0, max(0.0, f)))
    if co.z < 0.2 or (n.y < -0.4 and abs(co.z - 0.5) < 0.05):
        c *= 0.8
    return tuple(c * (0.9 + 0.2 * fbm(co.x * 60, co.y * 60, co.z * 60, 2)))


def leather_colour(co, n):
    base = V((0.085, 0.045, 0.022))
    g = 0.8 + 0.4 * fbm(co.x * 30, co.y * 30, co.z * 30, 4)
    # scuffed lighter at the toe and the creases, darker by the sole (mud)
    scuff = 0.25 * max(0, (co.y - 0.14) / 0.08) if n.z > 0.2 else 0.0
    mud = max(0.0, 1 - (co.z - 0.03) / 0.03) * 0.5
    c = base * g * (1 + scuff) * (1 - mud) + V((0.03, 0.025, 0.018)) * mud
    return tuple(c)


def flat(rgb, var=0.15, freq=30):
    def fn(co, n):
        return tuple(V(rgb) * (1 - var / 2 + var * (0.5 + 0.5 * fbm(co.x * freq, co.y * freq, co.z * freq, 2))))
    return fn


def pack_colour(co, n):
    base = V((0.14, 0.05, 0.026))
    g = 0.85 + 0.3 * fbm(co.x * 18, co.y * 18, co.z * 18, 3)
    return tuple(base * g)


# ------------------------------------------------------------------ the bake's weaves

def weave_material(name, kind):
    """The high copy's material for the normal bake: each surface's own small relief."""
    mt = bpy.data.materials.new(name)
    mt.use_nodes = True
    nt = mt.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    height = None

    def node(t, **inp):
        nd = nt.nodes.new(t)
        for k, v in inp.items():
            nd.inputs[k].default_value = v
        return nd

    def math_(op, a, b):
        nd = nt.nodes.new("ShaderNodeMath")
        nd.operation = op
        for i, x in enumerate((a, b)):
            if isinstance(x, (int, float)):
                nd.inputs[i].default_value = x
            else:
                nt.links.new(x, nd.inputs[i])
        return nd.outputs[0]

    if kind == "twill":      # jeans: fine diagonal ribs and a slub noise
        w = node("ShaderNodeTexWave", Scale=420.0, Distortion=0.5, **{"Detail": 1.0})
        w.wave_type = "BANDS"
        w.bands_direction = "DIAGONAL"
        nt.links.new(tc.outputs["Object"], w.inputs["Vector"])
        nz = node("ShaderNodeTexNoise", Scale=60.0, Detail=4.0)
        nt.links.new(tc.outputs["Object"], nz.inputs["Vector"])
        height = math_("ADD", math_("MULTIPLY", w.outputs["Fac"], 0.6), math_("MULTIPLY", nz.outputs["Fac"], 0.4))
        strength = 0.35
    elif kind == "softshell":   # the jacket: a fine grain and soft creases
        nz = node("ShaderNodeTexNoise", Scale=300.0, Detail=2.0)
        nt.links.new(tc.outputs["Object"], nz.inputs["Vector"])
        cr = node("ShaderNodeTexNoise", Scale=18.0, Detail=3.0, Distortion=1.5)
        nt.links.new(tc.outputs["Object"], cr.inputs["Vector"])
        height = math_("ADD", math_("MULTIPLY", nz.outputs["Fac"], 0.3), math_("MULTIPLY", cr.outputs["Fac"], 0.7))
        strength = 0.3
    elif kind == "leather":
        vo = node("ShaderNodeTexVoronoi", Scale=260.0)
        nt.links.new(tc.outputs["Object"], vo.inputs["Vector"])
        cr = node("ShaderNodeTexNoise", Scale=40.0, Detail=6.0, Distortion=2.0)
        nt.links.new(tc.outputs["Object"], cr.inputs["Vector"])
        height = math_("ADD", math_("MULTIPLY", vo.outputs["Distance"], 0.5), math_("MULTIPLY", cr.outputs["Fac"], 0.5))
        strength = 0.4
    elif kind == "lugs":     # the sole: chunky lugs (a voronoi's cells, flat-topped)
        vo = node("ShaderNodeTexVoronoi", Scale=70.0)
        vo.feature = "DISTANCE_TO_EDGE"
        nt.links.new(tc.outputs["Object"], vo.inputs["Vector"])
        height = math_("MINIMUM", math_("MULTIPLY", vo.outputs["Distance"], 8.0), 1.0)
        strength = 0.8
    elif kind == "cordura":  # the pack: a basket weave
        a = node("ShaderNodeTexWave", Scale=260.0)
        a.bands_direction = "X"
        b = node("ShaderNodeTexWave", Scale=260.0)
        b.bands_direction = "Z"
        for x in (a, b):
            nt.links.new(tc.outputs["Object"], x.inputs["Vector"])
        height = math_("MULTIPLY", a.outputs["Fac"], b.outputs["Fac"])
        strength = 0.3
    elif kind == "knit":
        a = node("ShaderNodeTexWave", Scale=180.0)
        a.bands_direction = "Z"
        nt.links.new(tc.outputs["Object"], a.inputs["Vector"])
        height = a.outputs["Fac"]
        strength = 0.5
    else:
        nz = node("ShaderNodeTexNoise", Scale=120.0, Detail=2.0)
        nt.links.new(tc.outputs["Object"], nz.inputs["Vector"])
        height = math_("MULTIPLY", nz.outputs["Fac"], 0.3)
        strength = 0.15
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    bump.inputs["Distance"].default_value = 0.002
    nt.links.new(height, bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mt


WEAVE = dict(pb_chest="softshell", pb_jacket="softshell", pb_sleeve="softshell", pb_collar="softshell", pb_jeans="twill", pb_leather="leather", pb_sole="lugs", pb_pack="cordura",
             pb_strap="cordura", pb_knit="knit", pb_glove="leather")


def bake_maps(low, high, size):
    # the normal map: each of the high copy's materials swapped for its weave
    for slot in high.material_slots:
        if slot.material:
            slot.material = weave_material("w_" + slot.material.name, WEAVE.get(slot.material.name, "plain"))
    nimg = W.new_image("player_body_normal", size, True)
    W.bake(low, high, "NORMAL", nimg, extrusion=0.006, dist=0.015)
    W.set_all_materials(high, W.emit_vc_material("pb_vc"))
    cimg = W.new_image("player_body_albedo", size, False)
    W.bake(low, high, "EMIT", cimg, extrusion=0.006, dist=0.015)
    aimg = W.new_image("player_body_ao", size, True)
    sc = bpy.context.scene
    if sc.world is None:
        sc.world = bpy.data.worlds.new("w")
    sc.world.light_settings.distance = 0.05   # (the creases' own occlusion, not the layers under the cloth)
    W.bake(low, None, "AO", aimg, samples=64)
    c = np.array(cimg.pixels[:], np.float32).reshape(size, size, 4)
    a = np.array(aimg.pixels[:], np.float32).reshape(size, size, 4)[:, :, 0]
    for _ in range(3):
        a = (a + np.roll(a, 1, 0) + np.roll(a, -1, 0) + np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 5.0
    c[:, :, :3] *= (0.7 + 0.3 * a)[:, :, None]
    cimg.pixels = c.ravel()
    for img in (cimg, nimg):
        img.filepath_raw = os.path.join(PREV, img.name + ".png")
        img.file_format = "PNG"
        img.save()
    print(f"[body] baked albedo and normal at {size} px")
    return cimg, nimg


# ------------------------------------------------------------------ the skeleton and the weights

def bone_list():
    B = [("root", V((0, 0, 0)), V((0, 0.2, 0)), None, 0.0)]
    B.append(("hips", spine_at(0.0), spine_at(0.25), "root", 0.15))
    B.append(("spine", spine_at(0.25), spine_at(0.6), "hips", 0.15))
    B.append(("chest", spine_at(0.6), spine_at(1.0), "spine", 0.17))
    B.append(("neck", NECK0, NECK1, "chest", 0.05))
    B.append(("head", NECK1, NECK1 + V((0, 0.0, 0.2)), "neck", 0.09))
    B.append(("pack", V((0, -0.2, 1.05)), V((0, -0.2, 1.45)), "chest", 0.0))
    for s, sx in ((1, "R"), (-1, "L")):
        J = arm_joints(s)
        B.append((f"clav_{sx}", J["clav"], J["shoulder"], "chest", 0.06))
        B.append((f"upper_{sx}", J["shoulder"], J["elbow"], f"clav_{sx}", 0.06))
        B.append((f"fore_{sx}", J["elbow"], J["wrist"], f"upper_{sx}", 0.05))
        B.append((f"hand_{sx}", J["wrist"], J["palm"], f"fore_{sx}", 0.03))
        B.append((f"fingers_{sx}", J["palm"], J["tip"], f"hand_{sx}", 0.012))
        B.append((f"thumb_{sx}", J["thumb0"], J["thumb"], f"hand_{sx}", 0.012))
        L = leg_joints(s)
        B.append((f"thigh_{sx}", L["hip"], L["knee"], "hips", 0.09))
        B.append((f"shin_{sx}", L["knee"], L["ankle"], f"thigh_{sx}", 0.06))
        B.append((f"foot_{sx}", L["ankle"], L["ball"], f"shin_{sx}", 0.04))
        B.append((f"toe_{sx}", L["ball"], L["toe"], f"foot_{sx}", 0.02))
    return B


def weigh(ob, bones, allowed, only=None):
    """Each vertex (of `only`, if given) to its nearest allowed bone (by distance to the bone less its radius), blended
    across the joints."""
    me = ob.data
    P = np.array([v.co[:] for v in me.vertices], np.float32)
    info = {b[0]: b for b in bones}
    names = [n for n in allowed if n in info]
    children = {}
    for b in bones:
        if b[3] in names and b[0] in names:
            children.setdefault(b[3], []).append(b[0])
    Dm = np.zeros((len(P), len(names)), np.float32)
    T = np.zeros((len(P), len(names)), np.float32)
    for i, n in enumerate(names):
        _, h, t, _, r = info[n]
        d, tt = W.seg_dist(P, np.array(h[:], np.float32), np.array(t[:], np.float32))
        Dm[:, i] = d - r
        T[:, i] = tt
    near = np.argmin(Dm, axis=1)
    groups = {}
    for n in names:
        groups[n] = ob.vertex_groups.get(n) or ob.vertex_groups.new(name=n)
    for v in me.vertices:
        if only is not None and v.index not in only:
            continue
        i = near[v.index]
        n = names[i]
        t = T[v.index, i]
        w = {n: 1.0}
        par = info[n][3]
        if t < 0.25 and par in names:
            k = 0.5 * (1 - t / 0.25)
            w = {n: 1 - k, par: k}
        elif t > 0.75 and n in children:
            ch = min(children[n], key=lambda c: (info[c][1] - V(P[v.index])).length)
            k = 0.5 * (t - 0.75) / 0.25
            w = {n: 1 - k, ch: k}
        for bn, x in w.items():
            if x > 0:
                groups[bn].add([v.index], x, "REPLACE")


def split_arms(jacket, bones):
    """The jacket's sleeves onto their own material (the game draws them in the shadow only): each face by whether its
    vertices are nearer the arm's bones or the body's."""
    me = jacket.data
    info = {b[0]: b for b in bones}
    arm_bones = [f"{p}_{sx}" for p in ("upper", "fore") for sx in ("R", "L")]
    body_bones = ["hips", "spine", "chest", "neck", "clav_R", "clav_L"]
    P = np.array([v.co[:] for v in me.vertices], np.float32)

    def nearest(names):
        best = np.full(len(P), 1e9, np.float32)
        for n in names:
            _, h, t, _, r = info[n]
            d, _ = W.seg_dist(P, np.array(h[:], np.float32), np.array(t[:], np.float32))
            best = np.minimum(best, d - r)
        return best
    arm = nearest(arm_bones) < nearest(body_bones) - 0.004
    if len(jacket.material_slots) < 2:
        me.materials.append(bpy.data.materials["pb_sleeve"])
        me.materials.append(bpy.data.materials["pb_collar"])
    for p in me.polygons:
        hx = jacket_section(min(max(p.center.z, JACKET[0][0]), JACKET[-1][0]))[0]
        outside = abs(p.center.x) > hx * 0.97 or p.center.z > 1.32   # (beside the body's own side, or the shoulder)
        if outside and sum(arm[i] for i in p.vertices) * 2 > len(p.vertices):
            p.material_index = 1
        elif p.center.z > 1.462 or (p.center.z > 1.44 and p.center.y < -0.06 and abs(p.center.x) < 0.13):
            p.material_index = 2


# ------------------------------------------------------------------ previews

def preview(name, target, dist, height=1.0, yaw=35.0, lens=40, cam_at=None, textured=True):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE" if textured else "MATERIAL"
    sh.show_shadows = True
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sc.render.resolution_x, sc.render.resolution_y = 900, 900
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
    t = V(target)
    a = math.radians(yaw)
    cam.location = cam_at if cam_at is not None else t + V((math.sin(a) * dist, math.cos(a) * dist, height))
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    cam.data.clip_start = 0.02
    sc.camera = cam
    sc.render.filepath = os.path.join(PREV, name + ".png")
    bpy.ops.render.render(write_still=True)


def previews(textured):
    preview("player_body_front", (0, 0, 0.95), 3.2, 0.2, yaw=10, textured=textured)
    preview("player_body_three_quarter", (0, 0, 0.95), 3.2, 0.3, yaw=40, textured=textured)
    preview("player_body_back", (0, 0, 1.1), 3.0, 0.3, yaw=160, textured=textured)
    preview("player_body_boots", (0, 0.06, 0.1), 0.9, 0.35, yaw=30, textured=textured)
    preview("player_body_side", (0, 0.0, 1.25), 1.4, 0.2, yaw=90, textured=textured)
    # what the eye sees: from the eye, looking down and a little ahead (the game's 75 degree field ~ a 23 mm lens)
    hidden = [o for o in bpy.context.scene.objects if o.type == "MESH" and (o.name.startswith("Head") or o.name.startswith("Beanie") or o.name in ("Arms", "Hands") or o.name.startswith("Hand"))]
    for o in hidden:
        o.hide_render = True
    # (the eye when looking down is out in front of the neck: the head bows; PlayerBody.cs carries the body back to match)
    preview("player_body_eye_down", (0, 1.0, 0.0), 0, cam_at=V((0, 0.24, 1.6)), lens=23, textured=textured)
    preview("player_body_eye_feet", (0, 0.36, 0.0), 0, cam_at=V((0, 0.28, 1.58)), lens=23, textured=textured)
    for o in hidden:
        o.hide_render = False


# ------------------------------------------------------------------ the figure

def main():
    clear()
    m = materials()
    jacket = build_jacket(m)
    jdet = build_jacket_details(m)
    # the cap over the chest's cut (the eye sees down onto it: a dark top to the belly, not a hole into the jacket)
    hx, hy, f = jacket_section(CHEST_CUT)
    c = spine_at((CHEST_CUT - 0.95) / 0.53)
    bm = bmesh.new()
    c = V((0, 0, 0)) if CHEST_CUT <= 0.95 else c
    # domed (the belly under the jacket, its front fuller), not a flat lid
    rings = []
    for k in range(6):
        u = k / 6
        sc = math.cos(u * math.pi * 0.5)
        rings.append([bm.verts.new(superellipse(V((0, c.y + 0.012 * u, CHEST_CUT - 0.085 + 0.07 * math.sin(u * math.pi * 0.5))), hx * 0.985 * sc, hy * 0.985 * sc,
                                                j / 40 * math.tau, 2.6, f)) for j in range(40)])
    top = bm.verts.new(V((0, c.y + 0.012, CHEST_CUT - 0.085 + 0.07)))
    for k in range(5):
        for j in range(40):
            bm.faces.new((rings[k][j], rings[k][(j + 1) % 40], rings[k + 1][(j + 1) % 40], rings[k + 1][j]))
    for j in range(40):
        bm.faces.new((rings[5][j], rings[5][(j + 1) % 40], top))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    cap = obj_from_bmesh(bm, "ChestCap")
    set_mat(cap, m["jacket"])
    jdet.append(cap)
    jeans = build_jeans(m)
    boots = build_boot(m, 1) + build_boot(m, -1)
    hands = [build_hand(m, s) for s in (1, -1)]
    head, hat = build_head(m)
    pack = build_pack(m)

    # colours
    W.paint(jacket, jacket_colour)
    for o in jdet:
        mn = o.data.materials[0].name
        W.paint(o, jacket_colour if mn in ("pb_jacket", "pb_sleeve", "pb_chest") else flat((0.01, 0.01, 0.01)) if mn == "pb_plastic" else flat((0.25, 0.24, 0.22)))
    W.paint(jeans, jeans_colour)
    for o in boots:
        mn = o.data.materials[0].name
        W.paint(o, leather_colour if mn == "pb_leather" else flat((0.018, 0.016, 0.014)) if mn == "pb_sole" else flat((0.22, 0.15, 0.07)) if mn == "pb_lace" else flat((0.25, 0.24, 0.22)))
    for o in hands:
        W.paint(o, flat((0.012, 0.009, 0.007), 0.3))
    W.paint(head, flat((0.45, 0.3, 0.22), 0.1))
    W.paint(hat, flat((0.03, 0.03, 0.032), 0.3, 80))
    for o in pack:
        mn = o.data.materials[0].name
        W.paint(o, pack_colour if mn == "pb_pack" else flat((0.015, 0.015, 0.015)) if mn in ("pb_strap", "pb_plastic") else flat((0.25, 0.24, 0.22)))

    if "shape" in ARGS:
        previews(False)
        return

    bones = bone_list()
    allowed = {
        "jacket": ["hips", "spine", "chest", "neck", "clav_R", "clav_L", "upper_R", "upper_L", "fore_R", "fore_L", "hand_R", "hand_L"],
        "jeans": ["hips", "thigh_R", "thigh_L", "shin_R", "shin_L"],
        "boot": ["shin_R", "shin_L", "foot_R", "foot_L", "toe_R", "toe_L"],
        "hand": ["fore_R", "fore_L", "hand_R", "hand_L", "fingers_R", "fingers_L", "thumb_R", "thumb_L"],
        "head": ["chest", "neck", "head"],
        "pack": ["pack", "chest", "spine"],
    }

    def copy(o):
        c = o.copy()
        c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        return c
    everything = [jacket] + jdet + [jeans] + boots + hands + [head, hat] + pack
    high = join([copy(o) for o in everything], "High")
    # the export copy: the fused cloth decimated to its budget
    for o, target in ((jacket, 9000), (jeans, 6000), (head, 1500), (hat, 600)) + tuple((b, 1600) for b in boots if b.name.startswith("Boot")) + tuple((h, 700) for h in hands):
        ratio = min(1.0, target / max(W.tri_count(o), 1))
        if ratio < 1.0:
            add_mod(o, "DECIMATE", ratio=ratio)
            apply_mods(o)
    split_arms(jacket, bones)
    # the jacket's body only to the body's bones (weighted to a forearm hanging beside it, its side was dragged up with
    # the arm when the lantern's raised); the sleeves to the arm's
    body_v = {i for p in jacket.data.polygons if p.material_index != 1 for i in p.vertices}
    sleeve_v = {i for p in jacket.data.polygons if p.material_index == 1 for i in p.vertices} - body_v   # (shared: the body's)
    body_bones = ["hips", "spine", "chest", "neck", "clav_R", "clav_L"]
    weigh(jacket, bones, body_bones, only=set(range(len(jacket.data.vertices))) - sleeve_v)
    weigh(jacket, bones, allowed["jacket"], only=sleeve_v)
    for o in jdet:
        weigh(o, bones, body_bones if not o.name.startswith("Cuff") else allowed["jacket"])
    weigh(jeans, bones, allowed["jeans"])
    for o in boots:
        weigh(o, bones, allowed["boot"])
    for o in hands:
        weigh(o, bones, allowed["hand"])
    for o in (head, hat):
        weigh(o, bones, allowed["head"])
    for o in pack:
        weigh(o, bones, allowed["pack"] if o.name.startswith("Pack") or o.name.startswith("Buckle") or o.name.startswith("Sternum") else ["chest", "spine", "hips"])
    low = join(everything, "PlayerBody")
    # the chest (above the belly) in the shadow only: from the eye it hid the legs and the boots when looking down
    chest_i = len(low.data.materials)
    low.data.materials.append(m["chest"])
    jk = {i for i, sl in enumerate(low.material_slots) if sl.material and sl.material.name == "pb_jacket"}
    det = {i for i, sl in enumerate(low.material_slots) if sl.material and sl.material.name in ("pb_plastic", "pb_metal")}
    for pg in low.data.polygons:
        if (pg.material_index in jk and pg.center.z > CHEST_CUT) or (pg.material_index in det and pg.center.z > 0.5):
            pg.material_index = chest_i
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[body] low {W.tri_count(low)} triangles, high {W.tri_count(high)}")
    albedo, normal = bake_maps(low, high, TEXSIZE)
    bpy.data.objects.remove(high, do_unlink=True)
    me = low.data
    if "Col" in me.color_attributes:
        me.color_attributes.remove(me.color_attributes["Col"])
    W.final_materials(low, albedo, normal)
    arm_obj = W.make_armature("PlayerBody", bones)
    low.parent = arm_obj
    mod = low.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj
    # the parts the game treats apart: by material
    groups = dict(Torso=("pb_jacket", "pb_plastic", "pb_metal"), Arms=("pb_sleeve",), Legs=("pb_jeans",),
                  Boots=("pb_leather", "pb_sole", "pb_lace", "pb_welt"), Hands=("pb_glove",), Head=("pb_skin", "pb_knit", "pb_collar"),
                  Pack=("pb_pack", "pb_strap"), Chest=("pb_chest",))
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.mesh.separate(type="MATERIAL")
    parts = [o for o in bpy.context.scene.objects if o.type == "MESH" and o.parent == arm_obj]
    named = {}
    for o in parts:
        mn = o.data.materials[o.data.polygons[0].material_index].name if o.data.polygons else ""
        g = next((k for k, v in groups.items() if mn in v), "Torso")
        named.setdefault(g, []).append(o)
    final = []
    for g, obs in named.items():
        ob = obs[0] if len(obs) == 1 else join(obs, g)
        ob.name = g
        ob.data.name = g
        if ob.parent != arm_obj:
            ob.parent = arm_obj
        if not any(md.type == "ARMATURE" for md in ob.modifiers):
            md = ob.modifiers.new("Armature", "ARMATURE")
            md.object = arm_obj
        final.append(ob)
    # (the metal and the plastic of the straps' buckle and the zips are on the torso; the eyelets on the boots: by where)
    previews(True)
    path = os.path.join(OUT, "player_body.glb")
    bpy.ops.object.select_all(action="DESELECT")
    arm_obj.select_set(True)
    for o in final:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=False,
                              export_animations=False, export_skins=True, export_image_format="AUTO", export_materials="EXPORT")
    print(f"[body] exported {path}: {sum(W.tri_count(o) for o in final)} triangles, {len(bones)} bones; parts {sorted(named)}")


main()
