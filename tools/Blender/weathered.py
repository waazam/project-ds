"""The weathered props (2026-10-24, the owner: "populate the game with more weathered objects to make it feel more lived
in", with photos of old tins, coffee cans, film canisters, a copper kettle, crocks, a rusted watering can and a burn
barrel). Modelled here, wrapped in the painted labels and wear from tools/Textures/make_weathered.py, and exported
together to assets/models/items/weathered.glb, one named mesh each (the game places them with ItemModels.Add(...,
"weathered", "<Name>", ...); their materials are their own, kept as imported):

    python tools/Textures/make_weathered.py
    blender -b --python tools/Blender/weathered.py

Each sits on its base at the origin, its front (the label's first face) toward Blender's -Y (the game's +Z). A preview
of them all lined up goes to build/blender/weathered.png.
"""
import bpy, bmesh, math, os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
TEX = os.path.join(ROOT, "assets", "models", "items", "weathered_tex")
OUT = os.path.join(ROOT, "assets", "models", "items", "weathered.glb")
PREV = os.path.join(ROOT, "build", "blender", "weathered.png")

bpy.ops.wm.read_factory_settings(use_empty=True)
_mats = {}


def mat(name, tex=None, color=(0.5, 0.5, 0.5), rough=0.7, metal=0.0):
    key = (name, tex)
    if key in _mats:
        return _mats[key]
    m = bpy.data.materials.new("w_" + name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if tex:
        img = bpy.data.images.load(os.path.join(TEX, tex + ".png"))
        node = m.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = img
        m.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (*color, 1)
    _mats[key] = m
    return m


def obj(name, bm, materials):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in materials:
        me.materials.append(m)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    for p in me.polygons:
        p.use_smooth = True
    return o


def lathe(bm, uv, profile, segs, mi, u0=0.0, u1=1.0, v_map=None, start=-math.pi / 2, cap_top=False, cap_bottom=False, cap_uv=None):
    """A surface of revolution round Z: profile is [(r, z)] bottom to top. UV: u round (u0..u1 from the front), v by
    v_map(z) (default z over the height). Caps optional, their UVs planar into cap_uv (u0, v0, u1, v1)."""
    zs = [z for _, z in profile]
    zmin, zmax = min(zs), max(zs)
    vm = v_map or (lambda z: (z - zmin) / max(1e-6, zmax - zmin))
    rings = []
    for (r, z) in profile:
        ring = []
        for i in range(segs + 1):
            a = start + 2 * math.pi * i / segs
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segs):
            f = bm.faces.new((rings[k][i], rings[k][i + 1], rings[k + 1][i + 1], rings[k + 1][i]))
            f.material_index = mi
            for loop, (ii, kk) in zip(f.loops, ((i, k), (i + 1, k), (i + 1, k + 1), (i, k + 1))):
                loop[uv].uv = (u0 + (u1 - u0) * ii / segs, vm(profile[kk][1]))
    for (want, idx, flip) in ((cap_bottom, 0, True), (cap_top, -1, False)):
        if not want:
            continue
        ring = rings[idx][:-1]
        f = bm.faces.new(list(reversed(ring)) if flip else ring)
        f.material_index = mi
        r0 = profile[idx][0]
        cu = cap_uv or (0.0, 0.0, 0.02, 0.02)
        for loop in f.loops:
            x, y = loop.vert.co.x / max(1e-6, r0), loop.vert.co.y / max(1e-6, r0)
            loop[uv].uv = (cu[0] + (cu[2] - cu[0]) * (0.5 + 0.5 * x), cu[1] + (cu[3] - cu[1]) * (0.5 + 0.5 * y))
    bm.verts.ensure_lookup_table()
    return rings


def new_bm():
    bm = bmesh.new()
    return bm, bm.loops.layers.uv.new("UVMap")


def can(name, tex, r, h, rough=0.55, metal=0.3, bead=True, cap_uv=(0.0, 0.0, 0.04, 0.08)):
    """A tin can: the label round it, a rolled bead at top and bottom, a lid (the rim's tin)."""
    bm, uv = new_bm()
    prof = [(r, 0.0), (r * 1.03, 0.004), (r * 1.03, 0.012), (r, 0.016), (r, h - 0.016), (r * 1.035, h - 0.012), (r * 1.035, h - 0.003), (r * 0.99, h)] if bead else [(r, 0), (r, h)]
    lathe(bm, uv, prof, 32, 0, cap_top=True, cap_bottom=True, cap_uv=cap_uv)
    return obj(name, bm, [mat(name, tex, rough=rough, metal=metal)])


def box(name, tex, sx, sy, sz, rough=0.6, metal=0.2):
    """A rectangular tin or box: the image round its four sides (front, right, back, left), top and bottom from its rim."""
    bm, uv = new_bm()
    x, y = sx / 2, sy / 2
    pts = [(-x, y), (x, y), (x, -y), (-x, -y), (-x, y)]   # front (+Y) first
    per = [sx, sy, sx, sy]
    tot = sum(per)
    acc = 0.0
    for i in range(4):
        (ax, ay), (bx, by) = pts[i], pts[i + 1]
        vs = [bm.verts.new((ax, ay, 0)), bm.verts.new((bx, by, 0)), bm.verts.new((bx, by, sz)), bm.verts.new((ax, ay, sz))]
        f = bm.faces.new(vs)
        ua, ub = acc / tot, (acc + per[i]) / tot
        for loop, c in zip(f.loops, ((ua, 0), (ub, 0), (ub, 1), (ua, 1))):
            loop[uv].uv = c
        acc += per[i]
    for z, flip in ((sz, False), (0, True)):
        vs = [bm.verts.new((-x, -y, z)), bm.verts.new((x, -y, z)), bm.verts.new((x, y, z)), bm.verts.new((-x, y, z))]
        f = bm.faces.new(list(reversed(vs)) if flip else vs)
        for loop in f.loops:
            loop[uv].uv = (0.6 + 0.3 * (loop.vert.co.x / sx + 0.5), 0.94 + 0.05 * (loop.vert.co.y / sy + 0.5))
    bmesh.ops.bevel(bm, geom=[e for e in bm.edges], offset=min(sx, sy, sz) * 0.06, segments=2, affect="EDGES") if False else None
    return obj(name, bm, [mat(name, tex, rough=rough, metal=metal)])


def flat_tin(name, tex, r, h):
    """A round flat tin: a lid over a body, the lid's top the image's upper square, the sides its lower band."""
    bm, uv = new_bm()
    side = lambda z: 0.0 + 0.24 * (z / h)
    lathe(bm, uv, [(r * 0.97, 0.0), (r * 0.97, h * 0.6)], 40, 0, v_map=lambda z: 0.02 + 0.2 * z / h, cap_bottom=True, cap_uv=(0.02, 0.0, 0.06, 0.04))
    lathe(bm, uv, [(r, h * 0.55), (r, h), (r * 0.985, h + 0.002)], 40, 0, v_map=lambda z: 0.02 + 0.22 * (z - h * 0.55) / (h * 0.45 + 0.002), cap_top=True, cap_uv=(0.125, 0.25, 0.875, 1.0))
    return obj(name, bm, [mat(name, tex, rough=0.5, metal=0.35)])


def film(name, tex, r=0.0125, h=0.05):
    bm, uv = new_bm()
    lathe(bm, uv, [(r, 0.004), (r, h - 0.004)], 24, 0)
    lathe(bm, uv, [(r * 1.05, 0.0), (r * 1.05, 0.004)], 24, 1, cap_bottom=True)
    lathe(bm, uv, [(r * 1.05, h - 0.004), (r * 1.05, h)], 24, 1, cap_top=True)
    lathe(bm, uv, [(r * 0.35, h), (r * 0.35, h + 0.006)], 12, 1, cap_top=True)
    return obj(name, bm, [mat(name, tex, rough=0.5, metal=0.2), mat("cap_black", None, (0.03, 0.03, 0.03), 0.4, 0.3)])


def barrel(name):
    bm, uv = new_bm()
    R, H = 0.29, 0.88
    prof = [(R, 0.0)]
    for z in (0.02, 0.28, 0.29, 0.30, 0.31, 0.57, 0.58, 0.59, 0.60, 0.86, 0.88):
        bump = 0.008 if z in (0.29, 0.30, 0.58, 0.59) else 0.0
        prof.append((R + bump, z))
    lathe(bm, uv, prof, 40, 0, v_map=lambda z: 1.0 - z / H)
    # the open top: the inside wall, charred, and the ash a hand down from the rim
    lathe(bm, uv, [(R - 0.006, H), (R - 0.006, H - 0.25)], 40, 1, cap_bottom=True)
    lathe(bm, uv, [(R, H), (R - 0.006, H)], 40, 0, v_map=lambda z: 0.0)
    lathe(bm, uv, [(R, 0.0), (R * 0.98, 0.0)], 40, 0, cap_bottom=True, cap_uv=(0.0, 0.9, 0.1, 1.0))
    return obj(name, bm, [mat(name, "burn_barrel", rough=0.9, metal=0.25), mat("char", None, (0.03, 0.025, 0.02), 1.0)])


def kettle(name):
    bm, uv = new_bm()
    prof = [(0.0, 0.0), (0.085, 0.0), (0.105, 0.025), (0.11, 0.07), (0.1, 0.12), (0.07, 0.15), (0.045, 0.16), (0.045, 0.175), (0.0, 0.18)]
    lathe(bm, uv, prof, 32, 0, v_map=lambda z: 1 - z / 0.18)
    # the spout: a tapering tube up and out of the front
    path = [Vector((0, 0.09, 0.05)), Vector((0, 0.14, 0.08)), Vector((0, 0.18, 0.13)), Vector((0, 0.2, 0.16))]
    rads = [0.022, 0.017, 0.012, 0.009]
    tube(bm, uv, path, rads, 0, 10)
    # the bail handle: an arc over the top
    arc = [Vector((0.07 * math.cos(t), 0, 0.17 + 0.09 * math.sin(t))) for t in [math.pi * i / 10 for i in range(11)]]
    tube(bm, uv, arc, [0.005] * len(arc), 1, 6)
    return obj(name, bm, [mat(name, "kettle_copper", rough=0.45, metal=0.75), mat("iron_dark", None, (0.06, 0.055, 0.05), 0.6, 0.6)])


def tube(bm, uv, path, rads, mi, segs):
    prev = None
    for k, (p, r) in enumerate(zip(path, rads)):
        d = (path[min(k + 1, len(path) - 1)] - path[max(k - 1, 0)]).normalized()
        a = d.orthogonal().normalized()
        b = d.cross(a)
        ring = [bm.verts.new(p + (a * math.cos(2 * math.pi * i / segs) + b * math.sin(2 * math.pi * i / segs)) * r) for i in range(segs)]
        if prev:
            for i in range(segs):
                f = bm.faces.new((prev[i], prev[(i + 1) % segs], ring[(i + 1) % segs], ring[i]))
                f.material_index = mi
                for loop in f.loops:
                    loop[uv].uv = (0.5 + 0.2 * (i / segs), 0.5 + 0.2 * k / len(path))
        prev = ring


def crock(name):
    bm, uv = new_bm()
    prof = [(0.0, 0.0), (0.08, 0.0), (0.11, 0.04), (0.125, 0.12), (0.115, 0.2), (0.07, 0.26), (0.045, 0.29), (0.05, 0.31), (0.042, 0.31), (0.036, 0.28)]
    lathe(bm, uv, prof, 32, 0, v_map=lambda z: 1 - z / 0.31)
    loop_pts = [Vector((0, -0.04 - 0.07 * math.sin(t), 0.27 - 0.12 * (1 - math.cos(t)) * 0.9)) for t in [math.pi * i / 10 for i in range(11)]]
    tube(bm, uv, [Vector((0, -0.045, 0.27))] + [Vector((0, -0.05 - 0.06 * math.sin(t), 0.27 - 0.1 * t / math.pi)) for t in [math.pi * i / 8 for i in range(1, 9)]], [0.012] * 9, 0, 8)
    return obj(name, bm, [mat(name, "crock_terracotta", rough=0.75)])


def watering(name):
    bm, uv = new_bm()
    lathe(bm, uv, [(0.0, 0.0), (0.11, 0.0), (0.115, 0.01), (0.115, 0.2), (0.105, 0.22), (0.0, 0.225)], 32, 0, v_map=lambda z: 1 - z / 0.225)
    tube(bm, uv, [Vector((0, 0.1, 0.04)), Vector((0, 0.2, 0.14)), Vector((0, 0.28, 0.24))], [0.018, 0.013, 0.01], 0, 10)
    lathe_rose = [(0.0, 0.0), (0.025, 0.0), (0.03, 0.02), (0.0, 0.022)]
    tube(bm, uv, [Vector((0, -0.1, 0.22)), Vector((0, -0.05, 0.3)), Vector((0, 0.05, 0.3)), Vector((0, 0.1, 0.22))], [0.008] * 4, 0, 6)
    o = obj(name, bm, [mat(name, "watering_can", rough=0.85, metal=0.3)])
    return o


def slab(bm, uv, outline, t, mi, uv_rect, y0=0.0):
    """A flat board cut to <outline> (points in X/Z) and <t> thick, from y0 to y0 + t; the image reads from -Y (the
    side the cans' labels face), mirrored on the +Y side (uv_rect: x0, z0, x1, z1 of the image),
    the edges a sliver of it."""
    x0, z0, x1, z1 = uv_rect
    def puv(x, z, back=False):
        u = (x - x0) / (x1 - x0)
        return (1 - u if back else u, (z - z0) / (z1 - z0))
    fr = [bm.verts.new((x, y0 + t, z)) for (x, z) in outline]
    bk = [bm.verts.new((x, y0, z)) for (x, z) in outline]
    f = bm.faces.new(list(reversed(fr)))
    f.material_index = mi
    for l in f.loops:
        l[uv].uv = puv(l.vert.co.x, l.vert.co.z, True)
    f = bm.faces.new(bk)
    f.material_index = mi
    for l in f.loops:
        l[uv].uv = puv(l.vert.co.x, l.vert.co.z)
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        f = bm.faces.new((fr[i], fr[j], bk[j], bk[i]))
        f.material_index = mi
        for l in f.loops:
            l[uv].uv = (0.5 + 0.001 * i, 0.98)


def block(bm, uv, c, size, mi, scale=1.0):
    """An axis-aligned box centred at c, box-mapped (scale: image repeats per metre)."""
    bmesh_ret = bmesh.ops.create_cube(bm, size=1.0)
    vs = bmesh_ret["verts"]
    for v in vs:
        v.co = Vector((c[0] + v.co.x * size[0], c[1] + v.co.y * size[1], c[2] + v.co.z * size[2]))
    faces = {f for v in vs for f in v.link_faces}
    for f in faces:
        f.material_index = mi
        nrm = f.normal
        ax = max(range(3), key=lambda k: abs(nrm[k]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for l in f.loops:
            l[uv].uv = (l.vert.co[a] * scale, l.vert.co[b] * scale)


def sign_arrow(name):
    """A painted arrow board nailed to a grey post, pointing right (game +X), its face to the front."""
    bm, uv = new_bm()
    L, H, P = 0.9, 0.2, 0.13
    out = [(-L / 2, -H / 2), (L / 2 - P, -H / 2), (L / 2, 0.0), (L / 2 - P, H / 2), (-L / 2, H / 2)]
    out = [(x + 0.32, z + 1.18) for (x, z) in out]
    slab(bm, uv, out, 0.024, 0, (-L / 2 + 0.32, -H / 2 + 1.18, L / 2 + 0.32 - 0.07, H / 2 + 1.18), y0=-0.069)
    block(bm, uv, (0, 0, 0.66), (0.09, 0.09, 1.32), 1, 1.6)
    return obj(name, bm, [mat(name, "sign_arrow", rough=0.9), mat("post_grey", None, (0.3, 0.28, 0.25), 0.95)])


def sign_firewood(name):
    """A roadside board on two stakes."""
    bm, uv = new_bm()
    W, H = 0.84, 0.42
    slab(bm, uv, [(-W / 2, 0.5), (W / 2, 0.5), (W / 2, 0.5 + H), (-W / 2, 0.5 + H)], 0.022, 0, (-W / 2, 0.5, W / 2, 0.5 + H), y0=-0.052)
    for x in (-W / 2 + 0.1, W / 2 - 0.1):
        block(bm, uv, (x, 0, 0.47), (0.06, 0.05, 0.94), 1, 1.6)
    return obj(name, bm, [mat(name, "sign_firewood", rough=0.9), mat("post_grey", None, (0.3, 0.28, 0.25), 0.95)])


def sign_tin(name):
    """An enamelled tin sign, its back at the origin (it hangs on a wall, reading out along -Y), its middle at the
    origin's height."""
    bm, uv = new_bm()
    W, H = 0.5, 0.25
    slab(bm, uv, [(-W / 2, -H / 2), (W / 2, -H / 2), (W / 2, H / 2), (-W / 2, H / 2)], 0.004, 0, (-W / 2, -H / 2, W / 2, H / 2), y0=-0.004)
    for x in (-W / 2 + 0.02, W / 2 - 0.02):
        for z in (-H / 2 + 0.02, H / 2 - 0.02):
            block(bm, uv, (x, -0.006, z), (0.008, 0.004, 0.008), 1)
    return obj(name, bm, [mat(name, "sign_tin", rough=0.45, metal=0.2), mat("iron_dark", None, (0.06, 0.055, 0.05), 0.6, 0.6)])


def filing_cabinet(name):
    """A four-drawer steel filing cabinet in chipped green enamel, its back at the origin, its drawers out along -Y:
    drawer fronts proud of the carcass, a pull and a
    card holder on each, one drawer left an inch open."""
    bm, uv = new_bm()
    W, D, H = 0.46, 0.62, 1.32
    block(bm, uv, (0, D / 2, H / 2 + 0.02), (W, D, H - 0.04), 0)
    block(bm, uv, (0, D / 2, 0.02), (W - 0.03, D - 0.03, 0.04), 2)        # the plinth, dark
    dh = (H - 0.08) / 4
    for i in range(4):
        z = 0.06 + dh * (i + 0.5)
        y = -0.008 - (0.03 if i == 2 else 0.0)
        block(bm, uv, (0, y, z), (W - 0.03, 0.02, dh - 0.016), 0)
        block(bm, uv, (0, y - 0.022, z + 0.02), (0.14, 0.03, 0.018), 2)   # the pull
        block(bm, uv, (0, y - 0.012, z + 0.085), (0.08, 0.004, 0.036), 3)  # the card holder
    return obj(name, bm, [mat(name, "filing_green", rough=0.55, metal=0.3), mat("filing_trim", None, (0.2, 0.2, 0.19), 0.5, 0.6),
                          mat("iron_dark", None, (0.06, 0.055, 0.05), 0.6, 0.6), mat("card", None, (0.62, 0.58, 0.48), 0.9)])


def cowbell(name, tex):
    """A cowbell hung from its strap: its origin the strap's top (the nail), the bell's mouth 0.25 below."""
    bm, uv = new_bm()
    prof = [(0.072, 0.0), (0.07, 0.006), (0.06, 0.06), (0.05, 0.12), (0.032, 0.15), (0.0, 0.152)]
    rings = lathe(bm, uv, prof, 24, 0, v_map=lambda z: z / 0.152)
    for ring in rings:
        for v in ring:
            v.co.y *= 0.62
    # (the inside, dark: a second skin a hair in)
    inner = lathe(bm, uv, [(0.0, 0.14), (0.045, 0.12), (0.055, 0.06), (0.066, 0.006)], 24, 1)
    for ring in inner:
        for v in ring:
            v.co.y *= 0.62
    tube(bm, uv, [Vector((-0.02, 0, 0.145)), Vector((-0.02, 0, 0.175)), Vector((0.0, 0, 0.19)), Vector((0.02, 0, 0.175)), Vector((0.02, 0, 0.145))], [0.004] * 5, 0, 6)
    block(bm, uv, (0, 0, 0.25), (0.022, 0.006, 0.14), 2)
    for v in bm.verts:
        v.co.z -= 0.32
    return obj(name, bm, [mat(name, tex, rough=0.7, metal=0.5), mat("bell_in", None, (0.03, 0.025, 0.02), 0.9), mat("leather", None, (0.2, 0.12, 0.07), 0.8)])


props = [
    can("CoffeeCan_Red", "coffee_red", 0.064, 0.14),
    can("CoffeeCan_Blue", "coffee_blue", 0.064, 0.14),
    can("CoffeeCan_Yellow", "coffee_yellow", 0.064, 0.14),
    box("SpiceTin_Cinnamon", "spice_cinnamon", 0.06, 0.035, 0.11),
    flat_tin("Tin_Tobacco", "tobacco_green", 0.055, 0.035),
    flat_tin("Tin_Tea", "tea_teal", 0.07, 0.09),
    film("Film_Brown", "film_brown"), film("Film_Orange", "film_orange"), film("Film_Green", "film_green"),
    box("Matchbox_A", "match_a", 0.052, 0.036, 0.016, rough=0.9, metal=0.0),
    box("Matchbox_B", "match_b", 0.052, 0.036, 0.016, rough=0.9, metal=0.0),
    box("Matchbox_C", "match_c", 0.052, 0.036, 0.016, rough=0.9, metal=0.0),
    barrel("BurnBarrel"), kettle("Kettle_Copper"), crock("Crock_Jug"), watering("WateringCan"),
    sign_arrow("Sign_Arrow"), sign_firewood("Sign_Firewood"), sign_tin("Sign_Tin"), filing_cabinet("FilingCabinet"),
    cowbell("Cowbell_Rust", "cowbell_rust"), cowbell("Cowbell_Brass", "cowbell_brass"),
]

# the preview: all of them in a row, the small ones scaled up beside to be seen
x = 0.0
for o in props:
    w = max(o.dimensions.x, 0.06)
    o.location.x = x + w / 2
    x += w + 0.06
bpy.ops.object.select_all(action="SELECT")
os.makedirs(os.path.dirname(OUT), exist_ok=True)
for o in props:
    o.location = (0, 0, 0)
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                          export_materials="EXPORT", export_image_format="AUTO")
print(f"[weathered] exported {OUT}: {len(props)} props")
x = 0.0
for o in props:
    s = 2.5 if o.dimensions.z < 0.06 else 1.0
    o.scale = (s, s, s)
    w = max(o.dimensions.x, 0.06)
    o.location = (x + w / 2, 0, 0)
    x += w + 0.08
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
scene.render.resolution_x, scene.render.resolution_y = 1800, 560
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.08, 0.08, 0.09, 1)
scene.world = world
cam_d = bpy.data.cameras.new("cam")
cam_d.type = "ORTHO"
cam_d.ortho_scale = x + 0.1
cam = bpy.data.objects.new("cam", cam_d)
scene.collection.objects.link(cam)
cam.location = (x / 2, -3.0, 0.45)
cam.rotation_euler = (math.radians(84), 0, 0)
scene.camera = cam
for (loc, e) in (((x * 0.3, -2, 2.5), 900.0), ((x * 0.9, -1.5, 1.2), 300.0)):
    ld = bpy.data.lights.new("l", "AREA")
    ld.energy = e
    ld.size = 3
    lo = bpy.data.objects.new("l", ld)
    lo.location = loc
    lo.rotation_euler = ((Vector((x / 2, 0, 0.3)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler())
    scene.collection.objects.link(lo)
os.makedirs(os.path.dirname(PREV), exist_ok=True)
scene.render.filepath = PREV
bpy.ops.render.render(write_still=True)
print(f"[weathered] preview {PREV}")
