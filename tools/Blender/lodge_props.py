"""The ski lodge's props (Act 23), modelled in Blender (the owner, 2026-10-15: every model is built in Blender). They were
built in C# (LodgeProps, LodgeKit's lights, SkiLodge.Furnish/Overhaul/CrawlClutter/CrawlDressing); each is now modelled
here and exported, with its baked cavity map, to assets/models/furniture/lp_<name>.glb, where FurnitureKit finds it:

    blender -b --python tools/Blender/lodge_props.py [-- name ...]

Each piece is modelled in the *game's* local frame of the code that placed it (x right, y up, z as the code's z), so the
game puts it in with the very transform its builder used to push: the lamp at its foot, the sconce on the wall in its
(side, up, outward) frame, the case furniture at its xf. G(x, y, z) turns a game point into Blender's (x, z, y); the
export's mirror and Y-up turn it back exactly.

Faces are grouped by role (material slot names); the game puts its own material on each (the lodge's brass, iron, its
woods, porcelain, the switched glows of shades and bulbs).
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furniture as F
from kit import mat, apply_mods, add_mod, obj_from_bmesh, fbm, preview

V = Vector
TAU = math.tau

# preview colours for the roles (the game swaps its own materials in)
F.ROLE_COLOURS.update({
    "brass": (0.62, 0.48, 0.24), "crock": (0.34, 0.19, 0.1), "iron": (0.12, 0.12, 0.13), "glass": (0.12, 0.4, 0.2),
    "opal": (1.0, 0.88, 0.66), "shade": (0.9, 0.75, 0.5), "glow": (1.0, 0.6, 0.3), "bulb": (1.0, 0.8, 0.5), "linen": (0.8, 0.78, 0.72),
    "porcelain": (0.55, 0.74, 0.64), "seat": (0.2, 0.11, 0.06), "log": (0.35, 0.25, 0.17), "cut": (0.6, 0.48, 0.32),
    "darkwood": (0.15, 0.08, 0.05), "gold": (0.72, 0.54, 0.24), "mirror": (0.4, 0.42, 0.45), "rug": (0.5, 0.2, 0.15),
    "ski": (0.52, 0.1, 0.08), "strap": (0.18, 0.11, 0.07), "frame": (0.55, 0.4, 0.24), "lace": (0.62, 0.54, 0.4),
    "wrap": (0.62, 0.08, 0.07), "ribbon": (0.7, 0.06, 0.05), "fir": (0.06, 0.16, 0.07), "berry": (0.6, 0.03, 0.03),
    "holly": (0.05, 0.2, 0.07), "tinsel": (0.75, 0.75, 0.78), "paper": (0.78, 0.75, 0.66), "wire": (0.05, 0.1, 0.05),
    "socket": (0.08, 0.12, 0.08), "black": (0.035, 0.03, 0.03), "top": (0.84, 0.86, 0.82), "bowl": (0.55, 0.74, 0.64),
})
F.KEEP_UV.update({"rug", "wrap", "paper"})


def R(name):
    return F.role(name)


def G(p):
    """A game point (x, y up, z) as Blender's (x, z, y)."""
    return V((p[0], p[2], p[1]))


def _xf(basis=None, at=(0, 0, 0)):
    """A game-frame transform (a 3x3 basis given as its columns x, y, z, and an origin) as a function on game points."""
    if basis is None:
        return lambda p: V(p) + V(at)
    bx, by, bz = (V(c) for c in basis)
    return lambda p: bx * p[0] + by * p[1] + bz * p[2] + V(at)


def _finish(ob, smooth=True, thick=0.0, bevel=0.0, segs=2, subsurf=0):
    if thick > 0:
        add_mod(ob, "SOLIDIFY", thickness=thick, offset=0.0)
    if bevel > 0:
        add_mod(ob, "BEVEL", width=bevel, segments=segs, limit_method="ANGLE")
    if subsurf:
        add_mod(ob, "SUBSURF", levels=subsurf, render_levels=subsurf)
    if ob.modifiers:
        apply_mods(ob)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    for p in ob.data.polygons:
        p.use_smooth = smooth
    return ob


def bevel_box(name, c, size, role, r=0.006, basis=None, segs=2):
    """A game BevelBox: centred at c, size (x, y, z), its edges eased by r, turned by basis (columns, game frame)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    f = _xf(basis, (0, 0, 0))
    for v in bm.verts:
        lp = (v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])
        v.co = G(f(lp) + V(c))
    ob = obj_from_bmesh(bm, name, R(role), smooth=False)
    return _finish(ob, True, bevel=min(r, min(size) * 0.45) if r > 0 else 0.0, segs=segs)


def lathe(name, at, prof, role, segs=24, sx=1.0, sz=1.0, cap_bottom=False, cap_top=False, basis=None, thick=0.0):
    """A turned piece: a profile [(radius, height)] spun round the game's Y at `at` (an oval with sx, sz)."""
    bm = bmesh.new()
    f = _xf(basis, at)
    rings = []
    for (r, y) in prof:
        ring = []
        for i in range(segs):
            a = i / segs * TAU
            ring.append(bm.verts.new(G(f((math.cos(a) * r * sx, y, math.sin(a) * r * sz)))))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segs):
            j = (i + 1) % segs
            try:
                bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
            except ValueError:
                pass
    if cap_bottom and prof[0][0] > 1e-5:
        bm.faces.new(rings[0])
    if cap_top and prof[-1][0] > 1e-5:
        bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    ob = obj_from_bmesh(bm, name, R(role))
    return _finish(ob, True, thick=thick)


def tube(name, pts, r, role, res=3, cap=True, smooth_path=False, r_end=None, cyclic=False):
    """A round rod along game points (a wire, a chain's line, an arm): a curve with a bevel, made a mesh."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = r
    cu.bevel_resolution = res
    cu.resolution_u = 3 if smooth_path else 1
    cu.use_fill_caps = cap and not cyclic
    sp = cu.splines.new("NURBS" if smooth_path else "POLY")
    sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        q = G(p)
        sp.points[i].co = (q.x, q.y, q.z, 1.0)
        if r_end is not None:
            sp.points[i].radius = 1.0 + (r_end / r - 1.0) * (i / max(1, len(pts) - 1))
    if smooth_path:
        sp.order_u = min(4, len(pts))
        sp.use_endpoint_u = not cyclic
    sp.use_cyclic_u = cyclic
    ob = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(ob)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.convert(target="MESH")
    ob = bpy.context.view_layer.objects.active
    ob.data.materials.clear()
    ob.data.materials.append(R(role))
    return _finish(ob, True)


def ring_pts(c, normal, u, r, n=24, wobble=0.0, seed=0):
    """Points round a circle at c in the plane through u and normal x u (game frame)."""
    c, nrm, u = V(c), V(normal).normalized(), V(u).normalized()
    v = nrm.cross(u).normalized()
    out = []
    for i in range(n):
        a = i / n * TAU
        rr = r * (1.0 + wobble * math.sin(a * 3 + seed))
        out.append(c + (u * math.cos(a) + v * math.sin(a)) * rr)
    return out


def torus(name, c, normal, R0, r, role, n=20):
    nrm = V(normal).normalized()
    u = nrm.cross(V((0, 1, 0)) if abs(nrm.y) < 0.9 else V((1, 0, 0))).normalized()
    return tube(name, ring_pts(c, nrm, u, R0, n), r, role, res=2, cyclic=True, smooth_path=True)


def blob(name, c, radii, role, seed, amp=0.06, freq=2.2, subdiv=3):
    """A lumpy ellipsoid at a game point, radii in the game frame."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    off = V((seed * 1.37, seed * 2.11, seed * 0.73))
    for vv in bm.verts:
        d = vv.co.normalized()
        k = 1.0 + fbm(d.x * freq + off.x, d.y * freq + off.y, d.z * freq + off.z, 3) * amp * 2.0
        vv.co = G((d.x * radii[0] * k, d.z * radii[1] * k, d.y * radii[2] * k)) + G(c)
    ob = obj_from_bmesh(bm, name, R(role))
    return _finish(ob, True)


def grid_sheet(name, fn, nu, nv, role, thick=0.0):
    """A sheet from a function (u, v) -> game point over a grid (cloth, paper, a rug)."""
    bm = bmesh.new()
    rows = [[bm.verts.new(G(fn(i / nu, j / nv))) for i in range(nu + 1)] for j in range(nv + 1)]
    uvl = bm.loops.layers.uv.new()
    for j in range(nv):
        for i in range(nu):
            fq = bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
            for lp, (a, b) in zip(fq.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                lp[uvl].uv = (a / nu, b / nv)
    ob = obj_from_bmesh(bm, name, R(role))
    return _finish(ob, True, thick=thick)


# ------------------------------------------------------------------ export

BUDGET = {"lp_chandelier_hall": 30000, "lp_chandelier_room": 12000, "lp_wagon_wheel": 14000, "lp_pendant": 3000, "lp_fire_tools": 3000,
          "lp_dust_sheet_a": 9000, "lp_dust_sheet_b": 9000, "lp_dust_sheet_c": 9000, "lp_rug_oval": 4500, "lp_poles": 3000, "lp_snowshoes": 4000,
          "lp_gift": 3000, "lp_bow": 2500, "lp_tinsel": 2500, "lp_wreath_small": 5000, "lp_holly": 3000, "lp_sconce": 2500, "lp_bankers_lamp": 3000}


def export(objs, name, tgt=(0, 0, 0.3), dist=1.6, h=0.4, yaw=30, cavity=512):
    """As furniture.export (join, mirror, triangulate, UVs, cavity bake, glTF), with the bake's size per piece and weighted
    normals (the boxes' flats stay flat, their eased edges round)."""
    ob = F.join_objs([o for o in objs if o is not None], name)
    tris0 = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if name in BUDGET and tris0 > BUDGET[name]:
        add_mod(ob, "DECIMATE", ratio=BUDGET[name] / tris0, use_collapse_triangulate=True)
        apply_mods(ob)
    ob.scale = (1, -1, 1)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")
    add_mod(ob, "TRIANGULATE")
    apply_mods(ob)
    bpy.ops.object.shade_smooth()
    if hasattr(ob.data, "use_auto_smooth"):
        ob.data.use_auto_smooth = True
        ob.data.auto_smooth_angle = math.radians(60)
    wn = add_mod(ob, "WEIGHTED_NORMAL")
    wn.keep_sharp = True
    apply_mods(ob)
    F.uv_layers(ob)
    F.bake_cavity(ob, name, cavity)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(F.OUT, name + ".glb"), export_format="GLB", use_selection=True, export_yup=True,
                              export_apply=True, export_texcoords=True, export_normals=True, export_tangents=True,
                              export_materials="EXPORT", export_image_format="NONE")
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[lodge_props] {name}: {tris} triangles")
    TRIS[name] = tris
    if tgt is not None:
        preview(name, G(tgt) * V((1, -1, 1)), dist, h, yaw)
    return ob


TRIS = {}


# ================================================================== lamps and lights

def table_lamp():
    """LodgeProps.TableLamp at 62 cm (the game scales it): a crock like a ginger jar, a brass neck and socket, the harp's
    wires, a drum shade (both faces) with a brass trim at each rim, a finial."""
    P = []
    P.append(lathe("Foot", (0, 0, 0), [(0.062, 0), (0.064, 0.008), (0.058, 0.018)], "brass", 32, cap_bottom=True))
    P.append(lathe("Jar", (0, 0, 0), [(0.05, 0.016), (0.062, 0.03), (0.075, 0.06), (0.083, 0.1), (0.086, 0.14), (0.08, 0.18), (0.066, 0.215),
                                      (0.048, 0.24), (0.034, 0.258), (0.028, 0.27)], "crock", 40))
    P.append(lathe("Neck", (0, 0, 0), [(0.036, 0.266), (0.04, 0.272), (0.038, 0.282), (0.014, 0.29), (0.011, 0.3), (0.011, 0.355),
                                       (0.02, 0.362), (0.02, 0.405), (0.012, 0.412), (0.0, 0.414)], "brass", 24))
    for s in (-1, 1):
        P.append(tube(f"Harp{s}", [(s * 0.02, 0.36, 0), (s * 0.02, 0.45, 0), (s * 0.006, 0.6, 0)], 0.0025, "brass", 2, smooth_path=True))
    P.append(lathe("Shade", (0, 0, 0), [(0.19, 0.33), (0.187, 0.36), (0.168, 0.585), (0.165, 0.6)], "shade", 48, thick=0.003))
    P.append(lathe("TrimLow", (0, 0, 0), [(0.188, 0.326), (0.194, 0.329), (0.194, 0.337), (0.188, 0.34)], "brass", 48))
    P.append(lathe("TrimHigh", (0, 0, 0), [(0.163, 0.595), (0.169, 0.598), (0.169, 0.606), (0.163, 0.609)], "brass", 48))
    P.append(lathe("Finial", (0, 0, 0), [(0.0, 0.598), (0.012, 0.6), (0.014, 0.612), (0.009, 0.624), (0.004, 0.63), (0.0, 0.633)], "brass", 16))
    P.append(lathe("Bulb", (0, 0, 0), [(0.0, 0.4), (0.022, 0.42), (0.03, 0.45), (0.022, 0.48), (0.0, 0.495)], "bulb", 16))
    return P


def bankers_lamp():
    """LodgeProps.BankersLamp: an oval stepped brass base, the turned upright, the cradle, a half cylinder of cased green
    glass (the opal lining lit within), its rolled brass lips, the pull chain."""
    P = []
    P.append(lathe("Base", (0, 0, 0), [(0.13, 0), (0.132, 0.008), (0.126, 0.016), (0.112, 0.02), (0.108, 0.03), (0.09, 0.036), (0.05, 0.04),
                                       (0.0, 0.042)], "brass", 40, sz=0.62, cap_bottom=True))
    up = (0, 0, 0.045)
    P.append(lathe("Upright", up, [(0.018, 0.035), (0.012, 0.05), (0.008, 0.06), (0.008, 0.2), (0.014, 0.212), (0.008, 0.224), (0.008, 0.3),
                                   (0.012, 0.31), (0.0, 0.316)], "brass", 20))
    for s in (-1, 1):
        P.append(tube(f"Cradle{s}", [(0, 0.3, 0.045), (s * 0.06, 0.302, 0.03), (s * 0.085, 0.305, 0.02), (s * 0.095, 0.322, 0.005)], 0.0045,
                      "brass", 2, smooth_path=True))
    L, ry, rz = 0.125, 0.072, 0.09
    ax = V((0, 0.33, 0.0))
    t = -0.12
    ct, st = math.cos(t), math.sin(t)

    def S(x, a, grow):
        y, z = math.sin(a) * (ry + grow), math.cos(a) * (rz + grow)
        return ax + V((x, y * ct - z * st, y * st + z * ct))
    a0, a1 = -0.22, math.pi + 0.12

    def shell(name, grow, role):
        return grid_sheet(name, lambda u, v: S(-L + 2 * L * u, a0 + (a1 - a0) * v, grow), 16, 28, role)
    P.append(_finish(shell("Glass", 0.0035, "glass"), True, thick=0.0))
    P.append(shell("Opal", -0.0035, "opal"))
    for e in (-L, L):
        bm = bmesh.new()
        pts = [bm.verts.new(G(S(e, a0 + (a1 - a0) * i / 28, 0.0035))) for i in range(29)]
        mid = (S(e, a0, 0.0035) + S(e, a1, 0.0035)) * 0.5
        c = bm.verts.new(G(mid))
        for i in range(28):
            bm.faces.new((c, pts[i], pts[i + 1]))
        P.append(_finish(obj_from_bmesh(bm, f"End{e}", R("glass")), True, thick=0.003))
    for a in (a0, a1):
        P.append(tube(f"Lip{a}", [S(-L - 0.004, a, 0.0), S(L + 0.004, a, 0.0)], 0.0045, "brass", 2))
    lip = (S(0, a0, 0) + V((0.03, 0, 0)))
    P.append(tube("Chain", [lip, lip + V((0, -0.09, 0))], 0.0016, "brass", 1))
    P.append(lathe("Bead", lip + V((0, -0.1, 0)), [(0.0, 0.0), (0.006, 0.004), (0.006, 0.01), (0.0, 0.014)], "brass", 12))
    return P


def ceiling_dome():
    """LodgeProps.CeilingDome, hung from the ceiling point (the origin): a brass canopy, a frosted dome, a finial."""
    P = []
    P.append(lathe("Canopy", (0, 0, 0), [(0.15, 0.0), (0.16, -0.004), (0.185, -0.012), (0.186, -0.03), (0.172, -0.036), (0.0, -0.036)], "brass", 48))
    P.append(lathe("Dome", (0, 0, 0), [(0.17, -0.034), (0.168, -0.06), (0.15, -0.1), (0.11, -0.13), (0.05, -0.148), (0.0, -0.152)], "glass", 48, thick=0.004))
    P.append(lathe("Finial", (0, -0.15, 0), [(0.018, 0.0), (0.02, -0.012), (0.01, -0.03), (0.014, -0.036), (0.0, -0.045)], "brass", 16, cap_top=False))
    return P


def sconce():
    """LodgeProps.Sconce in its own frame (x along the wall, y up, z out of it): an oval turned pattress, a brass boss, the
    swan neck out and up, the cup and socket, a flared shade (both faces)."""
    P = []
    P.append(lathe("Plate", (0, 0, 0), [(0.055, 0.0), (0.06, 0.008), (0.056, 0.016), (0.04, 0.021), (0.0, 0.022)], "wood", 28, sz=1.6,
                   basis=((1, 0, 0), (0, 0, 1), (0, -1, 0))))
    P.append(lathe("Boss", (0, 0, 0.018), [(0.02, 0.0), (0.019, 0.006), (0.015, 0.012), (0.0, 0.013)], "brass", 16,
                   basis=((1, 0, 0), (0, 0, 1), (0, -1, 0))))
    neck = []
    for i in range(0, 13):
        u = i / 12
        neck.append((0, -0.01 + 0.08 * (1 - math.cos(u * math.pi * 0.5)), 0.02 + 0.14 * math.sin(u * math.pi * 0.5)))
    P.append(tube("Neck", neck, 0.0075, "brass", 3, smooth_path=True))
    cup = neck[-1]
    P.append(lathe("Cup", cup, [(0.008, 0.0), (0.03, 0.012), (0.032, 0.018), (0.014, 0.02), (0.014, 0.055), (0.0, 0.058)], "brass", 24))
    P.append(lathe("Shade", cup, [(0.078, 0.055), (0.07, 0.09), (0.055, 0.14), (0.046, 0.165)], "shade", 36, thick=0.0025))
    P.append(lathe("Bulb", cup, [(0.0, 0.05), (0.016, 0.07), (0.02, 0.09), (0.0, 0.11)], "bulb", 12))
    return P


def chandelier(chain, r, arms):
    """LodgeKit.Chandelier hung at the origin: the chain down to the hub, the hub, two rings of iron arms (the upper turned
    half a step), a cup under each candle, the candle sleeve, the flame bulb. The game keeps its bulbs' positions."""
    P = []
    hub = V((0, -chain, 0))
    # the chain: links, alternately turned (a chain, not a rod)
    # (a long drop: links for the last 2 m above the hub, a rod above them in the dark of the roof)
    links = min(28, max(4, int(chain / 0.07)))
    run = links * 0.07 if links == 28 else chain
    if run < chain:
        P.append(tube("Rod", [(0, 0, 0), (0, -(chain - run), 0)], 0.012, "iron", 2))
    for i in range(links):
        y = -(chain - run) - run * (i + 0.5) / links
        n = (1, 0, 0) if i % 2 else (0, 0, 1)
        P.append(torus(f"Link{i}", (0, y, 0), n, 0.024, 0.0045, "iron", 10))
    P.append(lathe("Hub", hub, [(0.0, 0.17), (0.03, 0.16), (0.07, 0.12), (0.075, 0.05), (0.06, -0.05), (0.05, -0.16), (0.03, -0.24),
                                (0.012, -0.27), (0.0, -0.28)], "iron", 24))
    for ring, rr, dy in ((0, r, 0.0), (1, r * 0.6, 0.35)):
        for i in range(arms):
            a = TAU * (i + ring * 0.5) / arms
            d = V((math.cos(a), 0, math.sin(a)))
            tip = hub + d * rr + V((0, dy, 0))
            path = [hub + V((0, dy * 0.5, 0)) + d * 0.05, hub + d * rr * 0.3 + V((0, dy - 0.08, 0)), hub + d * rr * 0.55 + V((0, dy - 0.13, 0)),
                    hub + d * rr * 0.8 + V((0, dy - 0.09, 0)), tip + V((0, -0.02, 0))]
            P.append(tube(f"Arm{ring}_{i}", path, 0.015, "iron", 3, smooth_path=True, r_end=0.011))
            P.append(lathe(f"Cup{ring}_{i}", tip + V((0, -0.02, 0)), [(0.012, 0.0), (0.03, 0.01), (0.04, 0.018), (0.043, 0.024), (0.02, 0.024)], "iron", 18))
            P.append(lathe(f"Candle{ring}_{i}", tip, [(0.018, 0.0), (0.018, 0.115), (0.012, 0.122), (0.0, 0.124)], "linen", 14, cap_bottom=True))
            P.append(lathe(f"Flame{ring}_{i}", tip + V((0, 0.12, 0)), [(0.0, 0.0), (0.02, 0.02), (0.025, 0.04), (0.016, 0.065), (0.0, 0.085)], "bulb", 12))
    return P


def wagon_wheel(drop, r, sockets):
    """LodgeKit.WagonWheel hung from the ceiling point (the origin): a hub, twelve turned spokes, a felloe of curved
    segments, an iron tyre, three chains to a hook, a ceiling plate, amber chimneys on the rim."""
    P = []
    hub = V((0, -drop, 0))
    P.append(lathe("Hub", hub, [(0.05, -0.12), (0.075, -0.1), (0.09, -0.07), (0.09, 0.07), (0.075, 0.1), (0.05, 0.12), (0.0, 0.12)], "darkwood", 24, cap_bottom=True))
    for i in range(12):
        a = TAU * i / 12
        d = V((math.cos(a), 0, math.sin(a)))
        P.append(tube(f"Spoke{i}", [hub + d * 0.08, hub + d * (r * 0.5), hub + d * (r - 0.04)], 0.022, "darkwood", 2, smooth_path=True, r_end=0.016))
    # the felloe: a ring of wood, square-ish in section; the tyre outside it
    def ring_mesh(name, rin, rout, h, role, n=72):
        bm = bmesh.new()
        prof = [(rin, -h / 2), (rout, -h / 2), (rout, h / 2), (rin, h / 2)]
        rings = [[bm.verts.new(G(hub + V((math.cos(i / n * TAU) * pr, py, math.sin(i / n * TAU) * pr)))) for i in range(n)] for (pr, py) in prof]
        for k in range(4):
            for i in range(n):
                bm.faces.new((rings[k][i], rings[k][(i + 1) % n], rings[(k + 1) % 4][(i + 1) % n], rings[(k + 1) % 4][i]))
        return _finish(obj_from_bmesh(bm, name, R(role)), True, bevel=0.006)
    P.append(ring_mesh("Felloe", r - 0.045, r + 0.04, 0.085, "darkwood"))
    P.append(ring_mesh("Tyre", r + 0.04, r + 0.054, 0.07, "iron"))
    hook = hub + V((0, drop * 0.55, 0))
    for i in range(3):
        a = TAU * i / 3 + 0.26
        foot = hub + V((math.cos(a), 0, math.sin(a))) * (r * 0.92) + V((0, 0.05, 0))
        n = max(6, int((hook - foot).length / 0.06))
        for k in range(n):
            p = foot.lerp(hook, (k + 0.5) / n)
            ax = (hook - foot).normalized()
            side = ax.cross(V((0, 1, 0)) if abs(ax.y) < 0.9 else V((1, 0, 0))).normalized()
            nrm = side if k % 2 else ax.cross(side)
            P.append(torus(f"Chain{i}_{k}", p, nrm, 0.018, 0.0035, "iron", 8))
    P.append(tube("Drop", [hook, V((0, -0.04, 0))], 0.01, "iron", 2))
    P.append(lathe("Plate", (0, 0, 0), [(0.0, -0.045), (0.09, -0.04), (0.11, -0.01), (0.11, 0.0)], "iron", 24))
    for i in range(sockets):
        a = TAU * (i + 0.5) / sockets
        p = hub + V((math.cos(a), 0, math.sin(a))) * r + V((0, 0.045, 0))
        P.append(lathe(f"Cup{i}", p, [(0.03, 0.0), (0.04, 0.012), (0.04, 0.035), (0.036, 0.04)], "iron", 16, cap_bottom=True))
        P.append(lathe(f"Chimney{i}", p + V((0, 0.035, 0)), [(0.034, 0.0), (0.038, 0.02), (0.04, 0.06), (0.035, 0.1), (0.03, 0.115)], "glow", 20, thick=0.003))
    return P


def pendant(drop):
    """LodgeKit.Pendant hung from the ceiling point: its canopy, the flex, the socket cap, an enamel cone shade with a rolled
    rim, the light's diffuser under it."""
    P = []
    top = V((0, -(drop - 0.2), 0))
    rim = V((0, -drop, 0))
    P.append(lathe("Canopy", (0, 0, 0), [(0.0, -0.03), (0.05, -0.03), (0.06, -0.006), (0.06, 0.0)], "black", 24))
    P.append(tube("Flex", [V((0, -0.03, 0)), top + V((0, 0.03, 0))], 0.0045, "black", 2))
    P.append(lathe("Cap", top, [(0.0, 0.035), (0.03, 0.033), (0.04, 0.0), (0.042, -0.02)], "shade", 24))
    P.append(lathe("Shade", rim, [(0.192, 0.0), (0.188, 0.012), (0.15, 0.06), (0.1, 0.12), (0.06, 0.18), (0.045, 0.2)], "shade", 48, thick=0.003))
    P.append(torus("Rim", rim, (0, 1, 0), 0.191, 0.004, "shade", 48))
    P.append(lathe("Diffuser", rim + V((0, 0.05, 0)), [(0.0, 0.0), (0.17, 0.0), (0.17, 0.002), (0.0, 0.004)], "glow", 40))
    return P


# ================================================================== the hearth, small things

def split_log():
    """LodgeProps.SplitLog in its frame (x along, y up, z out to the room), its split face the shelf at y = 0: 3.8 m,
    0.36 deep, 0.24 to its belly. Bark ridged along it, the split face checked, the sawn ends with rings."""
    L, d, h = 3.8, 0.36, 0.24
    random.seed(41)
    bm = bmesh.new()
    nx, na = 48, 20
    rows = []
    for i in range(nx + 1):
        x = -L / 2 + L * i / nx
        row = []
        for j in range(na + 1):
            a = math.pi + math.pi * j / na
            ridge = 0.006 * math.sin(j * 2.9 + x * 0.7) + 0.004 * fbm(x * 2.0, j * 0.6, 0.3, 3)
            u = math.cos(a) * (d * 0.5 + ridge)
            v = math.sin(a) * (h + ridge)
            row.append(bm.verts.new(G((x, v, u))))
        rows.append(row)
    for i in range(nx):
        for j in range(na):
            bm.faces.new((rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]))
    ob = _finish(obj_from_bmesh(bm, "Round", R("log")), True)
    # the split face: flat, a few checks in it; and the ends
    def face(name, fn, nu, nv):
        return grid_sheet(name, fn, nu, nv, "cut")
    top = face("Split", lambda u, v: V((-L / 2 + L * u, -0.002 * (abs(math.sin(u * 37 + v * 3)) > 0.97), -d / 2 + d * v)), 48, 4)
    ends = []
    for s in (-1, 1):
        bm = bmesh.new()
        c = bm.verts.new(G((s * L / 2, -h * 0.45, 0)))
        rim = [bm.verts.new(G((s * L / 2, math.sin(math.pi + math.pi * j / na) * h, math.cos(math.pi + math.pi * j / na) * d * 0.5))) for j in range(na + 1)]
        for j in range(na):
            bm.faces.new((c, rim[j], rim[j + 1]))
        bm.faces.new([c] + [rim[na], rim[0]])
        ends.append(_finish(obj_from_bmesh(bm, f"End{s}", R("cut")), True))
    return [ob, top] + ends


def fire_tools():
    """LodgeProps.FireTools: a dished iron foot, the stem, its ring handle, the bar with a poker, a shovel and a brush."""
    P = []
    P.append(lathe("Foot", (0, 0, 0), [(0.11, 0.0), (0.115, 0.01), (0.09, 0.025), (0.03, 0.04), (0.012, 0.05), (0.01, 0.06)], "iron", 32, cap_bottom=True))
    P.append(tube("Stem", [(0, 0.05, 0), (0, 0.72, 0)], 0.009, "iron", 2))
    P.append(lathe("Collar", (0, 0.72, 0), [(0.012, 0.0), (0.016, 0.01), (0.006, 0.02), (0.006, 0.03)], "iron", 16))
    P.append(torus("Ring", (0, 0.785, 0), (0, 0, 1), 0.035, 0.005, "iron", 24))
    P.append(tube("Bar", [(-0.09, 0.62, 0), (0.09, 0.62, 0)], 0.006, "iron", 2))
    for t in range(3):
        x = -0.07 + t * 0.07
        hook = V((x, 0.62, 0.012))
        top = hook + V((0, -0.06, 0.01))
        foot = V((x * 1.4, 0.07, 0.03))
        P.append(tube(f"Hook{t}", [hook, hook + V((0, 0.012, 0.01)), top], 0.0045, "iron", 2, smooth_path=True))
        P.append(tube(f"Shaft{t}", [top, foot + V((0, 0.1, 0))], 0.007, "iron", 2))
        P.append(lathe(f"Knob{t}", top + V((0, -0.005, 0)), [(0.0, 0.0), (0.012, 0.01), (0.012, 0.025), (0.0, 0.035)], "iron", 12))
        if t == 0:
            P.append(tube("PokerHook", [foot + V((0, 0.1, 0)), foot + V((0, 0.04, 0)), foot + V((0.03, 0.03, 0))], 0.006, "iron", 2, smooth_path=True, r_end=0.004))
        elif t == 1:
            P.append(bevel_box("Shovel", foot + V((0, 0.055, 0)), (0.09, 0.1, 0.006), "iron", 0.002,
                               basis=((1, 0, 0), (0, math.cos(-0.1), math.sin(-0.1)), (0, -math.sin(-0.1), math.cos(-0.1)))))
        else:
            P.append(lathe("Brush", foot + V((0, 0.02, 0)), [(0.0, 0.0), (0.03, 0.005), (0.036, 0.04), (0.03, 0.08), (0.012, 0.095), (0.0, 0.1)], "black", 20))
    return P


def table_candle():
    """LodgeProps.TableCandle: a net-glass globe glowing red, 10 cm, the dark of its mouth."""
    return [lathe("Globe", (0, 0, 0), [(0.026, 0.0), (0.04, 0.018), (0.048, 0.045), (0.046, 0.07), (0.036, 0.092), (0.03, 0.1), (0.026, 0.096)],
                  "glow", 32, cap_bottom=True),
            lathe("Mouth", (0, 0, 0), [(0.026, 0.096), (0.0, 0.07)], "black", 32)]


def knob():
    """LodgeProps.Knob: a small turned brass knob, sticking out along +Y."""
    return [lathe("Knob", (0, 0, 0), [(0.011, 0.0), (0.006, 0.006), (0.005, 0.014), (0.014, 0.02), (0.015, 0.026), (0.01, 0.032), (0.0, 0.034)],
                  "brass", 20, cap_bottom=True)]


def drawer_front(field):
    """LodgeProps.DrawerFront without its knobs (the game puts them on), 0.5 wide, 0.1 (flat) or 0.25 (a raised field) high,
    centred at the origin, its face toward +Z. The game scales it to the drawer."""
    h = 0.25 if field else 0.1
    P = [bevel_box("Board", (0, 0, 0), (0.5, h, 0.022), "wood", 0.005)]
    if field:
        P.append(bevel_box("Field", (0, 0, 0.011), (0.43, h - 0.06, 0.008), "wood", 0.004))
        P.append(bevel_box("Raise", (0, 0, 0.016), (0.39, h - 0.1, 0.006), "wood", 0.005))
    return P


# ================================================================== case furniture

def turned_leg(name, at, h, top_r=0.021):
    return lathe(name, at, [(0.012, 0.0), (0.015, 0.012), (0.013, h * 0.25), (0.017, h * 0.4), (0.019, h * 0.55), (0.0205, h * 0.75),
                            (top_r, h - 0.004), (top_r - 0.004, h)], "wood", 20, cap_bottom=True)


def carcass(w, h, d, openings, openW=0.0, legH=0.1, legs=True, cubby=False, topThick=0.026, topBack=0.0, drawTop=True, bays=1):
    """LodgeProps.Carcass, the same case to the millimetre (the drawers stand in its openings): the top, the sides, the
    back, the bottom; the front's rails between the openings, a raised panel where a rail is deep (doors in bays, a knob on
    each), or a cubby; short turned legs on a skirt, or a plinth."""
    P = []
    t = max(0.018, min(0.06, (w - openW - 0.01) * 0.5)) if openW > 0 else 0.022
    yTop = h - topThick
    if drawTop:
        P.append(bevel_box("Top", (0, h - topThick * 0.5, -0.015 * 0.5 + topBack * 0.5), (w + 0.03, topThick, d + 0.015 + topBack), "top", 0.008, segs=3))
    for s in (-1, 1):
        P.append(bevel_box(f"Side{s}", (s * (w * 0.5 - t * 0.5), (legH + yTop) * 0.5, 0), (t, yTop - legH, d), "wood", 0.004))
    P.append(bevel_box("Back", (0, (legH + yTop) * 0.5, d * 0.5 - 0.006), (w - t * 2, yTop - legH, 0.012), "wood", 0.002))
    P.append(bevel_box("Bottom", (0, legH + 0.01, 0.005), (w - t * 2, 0.02, d - 0.01), "wood", 0.003))
    spans = [(legH, yTop)]
    for (o0, o1) in openings:
        nxt = []
        for (s0, s1) in spans:
            if o1 + 0.004 <= s0 or o0 - 0.004 >= s1:
                nxt.append((s0, s1))
                continue
            if o0 - 0.004 > s0:
                nxt.append((s0, o0 - 0.004))
            if o1 + 0.004 < s1:
                nxt.append((o1 + 0.004, s1))
        spans = nxt
    zf = -d * 0.5 + 0.01
    for i, (s0, s1) in enumerate(spans):
        sh = s1 - s0
        if sh < 0.004:
            continue
        if cubby and i == 0 and sh > 0.12:
            P.append(bevel_box(f"Cubby{i}", (0, s0 + 0.012, zf), (w - t * 2, 0.024, 0.02), "wood", 0.004))
            continue
        P.append(bevel_box(f"Rail{i}", (0, (s0 + s1) * 0.5, zf), (w - t * 2, sh, 0.02), "wood", 0.004))
        if sh > 0.16:
            bw = (w - t * 2) / bays
            for b in range(bays):
                bx = -w * 0.5 + t + bw * (b + 0.5)
                P.append(bevel_box(f"Panel{i}_{b}", (bx, (s0 + s1) * 0.5, zf - 0.012), (bw - 0.09, sh - 0.09, 0.008), "wood", 0.006))
                P.append(bevel_box(f"Raise{i}_{b}", (bx, (s0 + s1) * 0.5, zf - 0.017), (bw - 0.15, sh - 0.15, 0.006), "wood", 0.006))
                if bays > 1:
                    P.append(bevel_box(f"Meet{i}_{b}", (bx + bw * 0.5, (s0 + s1) * 0.5, zf - 0.012), (0.012, sh, 0.01), "wood", 0.003))
                kx = bx + (1 if b % 2 == 0 else -1) * (bw * 0.5 - 0.07)
                for o in knob():
                    o.location = G((kx, s1 - 0.08, zf - 0.016))
                    o.rotation_euler = (math.radians(90), 0, 0)
                    bpy.context.view_layer.objects.active = o
                    bpy.ops.object.select_all(action="DESELECT")
                    o.select_set(True)
                    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
                    P.append(o)
    if legH > 0.01:
        if legs:
            P.append(bevel_box("Skirt", (0, legH - 0.022, -d * 0.5 + 0.012), (w - 0.02, 0.044, 0.024), "wood", 0.005))
            for s in (-1, 1):
                P.append(bevel_box(f"SkirtSide{s}", (s * (w * 0.5 - 0.012), legH - 0.022, 0), (0.024, 0.044, d - 0.02), "wood", 0.005))
            for sx in (-1, 1):
                for sz in (-1, 1):
                    P.append(turned_leg(f"Leg{sx}{sz}", (sx * (w * 0.5 - 0.028), 0, sz * (d * 0.5 - 0.028)), legH))
        else:
            P.append(bevel_box("Plinth", (0, legH * 0.5, 0.012), (w - 0.03, legH, d - 0.025), "wood", 0.006))
    return P


CASES = {
    # name: (w, h, d, openings, openW, legH, legs, cubby, topThick, drawTop, bays)
    "lp_case_backbar": (5.0, 1.0, 0.6, [], 0.0, 0.1, False, False, 0.04, True, 8),
    "lp_case_cabinet": (0.93, 0.94, 0.6, [(0.51, 0.81)], 0.8, 0.1, False, False, 0.04, False, 1),
    "lp_case_nightstand": (0.5, 0.6, 0.48, [(0.36, 0.52)], 0.42, 0.12, True, True, 0.026, True, 1),
    "lp_case_vanity": (0.9, 0.9, 0.56, [(0.51, 0.73), (0.21, 0.43)], 0.8, 0.1, False, False, 0.035, False, 1),
    "lp_case_dresser": (1.2, 1.1, 0.5, [(0.66, 0.98), (0.26, 0.58)], 1.1, 0.1, True, False, 0.026, True, 1),
}


def case_piece(name):
    w, h, d, op, ow, lh, legs, cubby, tt, dt, bays = CASES[name]
    return carcass(w, h, d, op, ow, lh, legs, cubby, tt, 0.0, dt, bays)


def cabinet_case(w, h, d):
    """LodgeProps.CabinetCase (the door is its own piece): panelled sides, a top board, a base rail and bottom, a moulded
    cornice of three steps, the back, the brass hanging rail."""
    t = 0.024
    P = []
    for s in (-1, 1):
        P.append(bevel_box(f"Side{s}", (s * (w * 0.5 - t * 0.5), h * 0.5, 0), (t, h, d), "wood", 0.005))
        P.append(bevel_box(f"SidePanel{s}", (s * (w * 0.5 + 0.002), h * 0.55, 0), (0.006, h * 0.68, d - 0.14), "wood", 0.003))
    P.append(bevel_box("TopBoard", (0, h - 0.012, 0), (w - t * 2, 0.024, d), "wood", 0.004))
    P.append(bevel_box("BaseRail", (0, 0.05, d * 0.5 - 0.012), (w - t * 2 + 0.002, 0.1, 0.026), "wood", 0.006))
    P.append(bevel_box("Bottom", (0, 0.1, 0), (w - t * 2, 0.02, d - 0.03), "wood", 0.003))
    P.append(bevel_box("Cornice1", (0, h + 0.012, 0.004), (w + 0.03, 0.024, d + 0.022), "wood", 0.006))
    P.append(bevel_box("Cornice2", (0, h + 0.04, 0.008), (w + 0.07, 0.034, d + 0.04), "wood", 0.012, segs=3))
    P.append(bevel_box("Cornice3", (0, h + 0.066, 0.006), (w + 0.05, 0.018, d + 0.032), "wood", 0.006))
    P.append(bevel_box("Back", (0, h * 0.5, -d * 0.5 + 0.006), (w - t * 2, h - 0.02, 0.012), "back", 0.002))
    P.append(tube("Rail", [(-w * 0.5 + t, h - 0.24, 0), (w * 0.5 - t, h - 0.24, 0)], 0.011, "brass", 3))
    return P


def cabinet_door(w, y0, y1, kn):
    """LodgeProps.CabinetDoor in its hinge frame (x from the hinge): the door, two raised panels, the knob, two hinges."""
    h, st, rl = y1 - y0, 0.085, 0.1
    P = [bevel_box("Door", (w * 0.5, (y0 + y1) * 0.5, 0.0125), (w - 0.004, h, 0.025), "wood", 0.005)]
    mid = y0 + h * 0.46
    for i, (p0, p1) in enumerate(((y0 + rl, mid - rl * 0.5), (mid + rl * 0.5, y1 - rl))):
        P.append(bevel_box(f"Panel{i}", (w * 0.5, (p0 + p1) * 0.5, 0.027), (w - st * 2, p1 - p0, 0.006), "wood", 0.004))
        P.append(bevel_box(f"Raise{i}", (w * 0.5, (p0 + p1) * 0.5, 0.031), (w - st * 2 - 0.06, p1 - p0 - 0.06, 0.006), "wood", 0.006))
    for o in knob():
        o.location = G(kn)
        o.rotation_euler = (math.radians(-90), 0, 0)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        P.append(o)
    for hy in (y0 + 0.2, y1 - 0.2):
        P.append(lathe(f"Hinge{hy}", (0.002, hy - 0.04, 0.0), [(0.007, 0.0), (0.008, 0.004), (0.008, 0.076), (0.007, 0.08), (0.0, 0.082)], "brass", 12, cap_bottom=True))
    return P


def round_slab(name, top, hx, hz, t, r, e, role):
    """LodgeProps.RoundSlab: a slab with rounded corners (r) and eased edges (e), its top at `top`."""
    P = bevel_box(name, (top[0], top[1] - t * 0.5, top[2]), (hx * 2, t, hz * 2), role, e, segs=3)
    return P


def writing_desk(w, d, h, dw, dy0, dy1, inset):
    """LodgeProps.WritingDesk: a top with rounded corners, the apron with the drawer's opening, square legs turning to a
    taper below the apron."""
    P = [round_slab("Top", (0, h, 0), w * 0.5, d * 0.5, 0.032, 0.02, 0.012, "wood")]
    ay1 = h - 0.032
    ay0 = min(dy0 - 0.012, ay1 - 0.12)
    lw = 0.05
    fz = -d * 0.5 + inset + 0.01
    lz0 = -d * 0.5 + inset + lw * 0.5
    lz1 = d * 0.5 - 0.04
    P.append(bevel_box("ApronBack", (0, (ay0 + ay1) * 0.5, lz1), (w - 0.08, ay1 - ay0, 0.02), "wood", 0.004))
    for s in (-1, 1):
        P.append(bevel_box(f"ApronSide{s}", (s * (w * 0.5 - 0.04), (ay0 + ay1) * 0.5, (lz0 + lz1) * 0.5), (0.02, ay1 - ay0, lz1 - lz0), "wood", 0.004))
        x0, x1 = dw * 0.5 + 0.004, w * 0.5 - 0.04 - lw * 0.5
        if x1 - x0 > 0.01:
            P.append(bevel_box(f"ApronFront{s}", (s * (x0 + x1) * 0.5, (ay0 + ay1) * 0.5, fz), (x1 - x0, ay1 - ay0, 0.02), "wood", 0.004))
    if dy0 - 0.004 > ay0:
        P.append(bevel_box("RailLow", (0, (ay0 + dy0 - 0.004) * 0.5, fz), (dw + 0.01, dy0 - 0.004 - ay0, 0.02), "wood", 0.003))
    if ay1 > dy1 + 0.004:
        P.append(bevel_box("RailHigh", (0, (ay1 + dy1 + 0.004) * 0.5, fz), (dw + 0.01, ay1 - dy1 - 0.004, 0.02), "wood", 0.003))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p = V((sx * (w * 0.5 - 0.04), 0, lz0 if sz < 0 else lz1))
            P.append(bevel_box(f"LegBlock{sx}{sz}", p + V((0, (ay0 + ay1) * 0.5, 0)), (lw, ay1 - ay0, lw), "wood", 0.005))
            P.append(lathe(f"Leg{sx}{sz}", p, [(0.014, 0.0), (0.018, 0.01), (0.015, 0.03), (0.017, 0.045), (0.021, ay0 * 0.6), (0.026, ay0 - 0.02),
                                              (0.03, ay0 + 0.002)], "wood", 20, cap_bottom=True))
    return P


def rrect(hx, hz, r, y, seg=6, x=0.0, z=0.0):
    pts = []
    for (cx, cz, a0) in ((hx - r, hz - r, 0.0), (-hx + r, hz - r, 0.5 * math.pi), (-hx + r, -hz + r, math.pi), (hx - r, -hz + r, 1.5 * math.pi)):
        for i in range(seg + 1):
            a = a0 + 0.5 * math.pi * i / seg
            pts.append(V((x + cx + math.cos(a) * r, y, z + cz + math.sin(a) * r)))
    return pts


def oval(rx, rz, y, n=40, x=0.0, z=0.0):
    return [V((x + math.cos(i / n * TAU) * rx, y, z + math.sin(i / n * TAU) * rz)) for i in range(n)]


def loft(name, rings, role, cap_last=False, cap_first=False):
    bm = bmesh.new()
    vs = [[bm.verts.new(G(p)) for p in ring] for ring in rings]
    n = len(rings[0])
    for k in range(len(vs) - 1):
        for i in range(n):
            j = (i + 1) % n
            try:
                bm.faces.new((vs[k][i], vs[k][j], vs[k + 1][j], vs[k + 1][i]))
            except ValueError:
                pass
    if cap_last:
        bm.faces.new(vs[-1])
    if cap_first:
        bm.faces.new(vs[0])
    return _finish(obj_from_bmesh(bm, name, R(role)), True)


def tub(hx, hz, h):
    """LodgeProps.Tub: a cast tub, its roll rim, the deep well inside it; brass drain and deck taps."""
    rings = [rrect(hx - 0.01, hz - 0.01, 0.05, 0.0), rrect(hx, hz, 0.055, 0.03), rrect(hx, hz, 0.055, h - 0.03),
             rrect(hx + 0.008, hz + 0.008, 0.06, h - 0.012), rrect(hx - 0.01, hz - 0.01, 0.05, h), rrect(hx - 0.07, hz - 0.07, 0.2, h),
             rrect(hx - 0.08, hz - 0.08, 0.2, h - 0.02), rrect(hx - 0.16, hz - 0.12, 0.17, 0.2), rrect(hx - 0.2, hz - 0.15, 0.12, 0.12)]
    P = [loft("Tub", rings, "porcelain", cap_last=True, cap_first=True)]
    P.append(lathe("Drain", (hx - 0.32, 0.12, 0), [(0.025, 0.001), (0.026, 0.004), (0.0, 0.005)], "brass", 20))
    deck = V((hx - 0.04, h, 0))
    P.append(lathe("Spout", deck, [(0.022, 0.0), (0.016, 0.012), (0.014, 0.06), (0.0, 0.064)], "brass", 20))
    P.append(tube("SpoutArm", [deck + V((0, 0.05, 0)), deck + V((-0.08, 0.065, 0)), deck + V((-0.13, 0.055, 0)), deck + V((-0.15, 0.03, 0))], 0.011,
                  "brass", 3, smooth_path=True, r_end=0.009))
    for s in (-1, 1):
        tp = deck + V((0, 0, s * 0.1))
        P.append(lathe(f"Tap{s}", tp, [(0.02, 0.0), (0.012, 0.012), (0.01, 0.05), (0.0, 0.056)], "brass", 16))
        for (sx, sz) in ((0.07, 0.012), (0.012, 0.07)):
            P.append(bevel_box(f"Cross{s}{sx}", tp + V((0, 0.05, 0)), (sx, 0.01, sz), "brass", 0.003))
    return P


def basin_top(w, d, h, bx, bz):
    """LodgeProps.BasinTop: the counter (rounded corners, an eased edge), the bowl let into it with its lip, a drain, the
    spout and two cross taps behind it."""
    t = 0.035
    rx, rz = min(0.22, w * 0.5 - 0.08), min(0.16, d * 0.5 - 0.07)
    # the counter: a slab with an oval hole (a boolean)
    slab = bevel_box("Counter", (0, h - t * 0.5, 0), (w, t, d), "top", 0.008, segs=3)
    cut = lathe("Cut", (bx, h - 0.1, bz), [(rx + 0.012, 0.0), (rx + 0.012, 0.2)], "top", 48, sz=(rz + 0.012) / (rx + 0.012), cap_bottom=True, cap_top=True)
    bm = slab.modifiers.new("hole", "BOOLEAN")
    bm.object = cut
    bm.operation = "DIFFERENCE"
    apply_mods(slab)
    bpy.data.objects.remove(cut)
    P = [slab]
    n = 40
    P.append(loft("Bowl", [oval(rx + 0.02, rz + 0.02, h + 0.002, n, bx, bz), oval(rx + 0.02, rz + 0.02, h + 0.006, n, bx, bz),
                           oval(rx, rz, h + 0.004, n, bx, bz), oval(rx * 0.98, rz * 0.97, h - 0.02, n, bx, bz), oval(rx * 0.92, rz * 0.9, h - 0.06, n, bx, bz),
                           oval(rx * 0.62, rz * 0.58, h - 0.13, n, bx, bz), oval(rx * 0.25, rz * 0.22, h - 0.15, n, bx, bz)], "bowl", cap_last=True))
    P.append(lathe("Drain", (bx, h - 0.149, bz), [(0.018, 0.0), (0.018, 0.002), (0.0, 0.003)], "brass", 20))
    back = V((bx, h, bz + rz + 0.06))
    P.append(lathe("Spout", back, [(0.02, 0.0), (0.014, 0.012), (0.012, 0.07), (0.0, 0.075)], "brass", 20))
    P.append(tube("SpoutArm", [back + V((0, 0.065, 0)), back + V((0, 0.075, -0.06)), back + V((0, 0.07, -0.1)), back + V((0, 0.04, -0.12))], 0.0095,
                  "brass", 3, smooth_path=True, r_end=0.008))
    for s in (-1, 1):
        tp = back + V((s * 0.11, 0, 0))
        P.append(lathe(f"Tap{s}", tp, [(0.018, 0.0), (0.01, 0.01), (0.009, 0.04), (0.0, 0.045)], "brass", 16))
        for (sx, sz) in ((0.06, 0.011), (0.011, 0.06)):
            P.append(bevel_box(f"Cross{s}{sx}", tp + V((0, 0.04, 0)), (sx, 0.009, sz), "brass", 0.003))
    return P


def lavatory():
    """LodgeProps.Lavatory: the bowl on its pedestal, a wooden seat and lid, the cistern on the wall behind, its lid, the
    brass lever."""
    n = 40

    def ring(rx, rz, y, zc):
        return oval(rx, rz, y, n, 0.0, zc)
    bowl = [ring(0.11, 0.15, 0.0, 0.0), ring(0.105, 0.14, 0.03, 0.0), ring(0.09, 0.12, 0.14, -0.01), ring(0.13, 0.19, 0.28, -0.04),
            ring(0.175, 0.23, 0.37, -0.06), ring(0.18, 0.235, 0.395, -0.06)]
    P = [loft("Bowl", bowl, "porcelain", cap_last=True, cap_first=True)]
    lid = [ring(0.178, 0.232, 0.398, -0.065), ring(0.186, 0.24, 0.41, -0.065), ring(0.184, 0.238, 0.43, -0.065), ring(0.17, 0.222, 0.438, -0.065)]
    P.append(loft("Lid", lid, "seat", cap_last=True, cap_first=True))
    P.append(bevel_box("Cistern", (0, 0.78 - 0.2, 0.13), (0.42, 0.4, 0.17), "porcelain", 0.03, segs=3))
    P.append(bevel_box("CisternLid", (0, 0.81 - 0.0175, 0.13), (0.45, 0.035, 0.19), "porcelain", 0.012, segs=3))
    P.append(lathe("LeverBoss", (-0.17, 0.7, 0.04), [(0.016, 0.0), (0.016, -0.015), (0.0, -0.016)], "brass", 16,
                   basis=((1, 0, 0), (0, 0, 1), (0, -1, 0))))
    P.append(tube("Lever", [(-0.17, 0.7, 0.025), (-0.13, 0.697, 0.022), (-0.1, 0.69, 0.02)], 0.0065, "brass", 2, smooth_path=True, r_end=0.006))
    return P


# ================================================================== the front desk and the bar

def front_desk():
    """SkiLodge.FrontDesk, its origin at (d0.x, 0, d0.z): the plinth, the body, six raised panels between pilasters, the
    ends, a thick top with its edges rounded, the brass rail under its lip. 3.2 m long, its top at 1.13."""
    # (heights as the code had them: the plinth, body, ends and top on the floor's 2 cm, the panels and rail absolute)
    L, D, y0 = 3.2, 0.6, 0.02
    top = y0 + 1.13
    P = [bevel_box("Plinth", (0, y0 + 0.06, 0.015), (L - 0.04, 0.12, D - 0.03), "wood", 0.01),
         bevel_box("Body", (0, y0 + 0.6, 0.01), (L - 0.06, 0.96, D - 0.04), "wood", 0.004)]
    panels = 6
    pw = (L - 0.1) / panels
    zf = -D * 0.5 + 0.02
    for i in range(panels):
        px = -(L - 0.1) * 0.5 + pw * (i + 0.5)
        P.append(bevel_box(f"Frame{i}", (px, 0.6, zf + 0.004), (pw - 0.05, 0.92, 0.02), "wood", 0.006))
        P.append(bevel_box(f"Panel{i}", (px, 0.6, zf - 0.006), (pw - 0.18, 0.66, 0.012), "wood", 0.006))
        P.append(bevel_box(f"Raise{i}", (px, 0.6, zf - 0.012), (pw - 0.28, 0.54, 0.012), "wood", 0.008))
    for i in range(panels + 1):
        P.append(bevel_box(f"Pilaster{i}", (-(L - 0.1) * 0.5 + pw * i, 0.6, zf - 0.01), (0.07, 0.96, 0.05), "wood", 0.012, segs=3))
        P.append(bevel_box(f"Cap{i}", (-(L - 0.1) * 0.5 + pw * i, 1.075, zf - 0.012), (0.085, 0.03, 0.06), "wood", 0.008))
    for s in (-1, 1):
        P.append(bevel_box(f"End{s}", (s * (L * 0.5 - 0.02), y0 + 0.6, 0.02), (0.04, 0.96, D - 0.04), "wood", 0.008))
    P.append(round_slab("Top", (0, top, -0.02), L * 0.5 + 0.05, D * 0.5 + 0.04, 0.05, 0.03, 0.018, "wood"))
    P.append(tube("Rail", [(-L * 0.5, 1.05, zf - 0.05), (L * 0.5, 1.05, zf - 0.05)], 0.012, "brass", 3))
    for x in (-L * 0.5 + 0.1, 0.0, L * 0.5 - 0.1):
        P.append(tube(f"RailPost{x}", [(x, 1.05, zf - 0.05), (x, 1.05, zf - 0.005)], 0.007, "brass", 2))
    return P


def bar_counter():
    """SkiLodge.BuildBar's counter, its origin at (BarCounterX, 0, middle of its run): the dark body, five raised panels on
    the customers' side (+X) between pilasters, the kick, the brass foot rail on its brackets, the bar top and its rounded
    nosing. 5 m."""
    z0, z1 = -2.5, 2.5
    P = [bevel_box("Body", (-0.35, 0.55, 0), (0.7, 1.1, z1 - z0), "darkwood", 0.004)]
    panels = 5
    pw = (z1 - z0) / panels
    for i in range(panels):
        pz = z0 + pw * (i + 0.5)
        P.append(bevel_box(f"Panel{i}", (0.004, 0.6, pz), (0.02, 0.62, pw - 0.22), "wood", 0.008))
        P.append(bevel_box(f"Raise{i}", (0.009, 0.6, pz), (0.02, 0.48, pw - 0.36), "wood", 0.008))
    for i in range(panels + 1):
        P.append(bevel_box(f"Pilaster{i}", (0.03, 0.55, z0 + pw * i), (0.06, 1.02, 0.09), "wood", 0.012, segs=3))
    P.append(bevel_box("Kick", (0.02, 0.06, 0), (0.06, 0.12, z1 - z0), "wood", 0.01))
    P.append(tube("FootRail", [(0.22, 0.16, z0 + 0.05), (0.22, 0.16, z1 - 0.05)], 0.025, "brass", 4))
    for z in (z0 + 0.3, 0.0, z1 - 0.3):
        P.append(tube(f"Bracket{z}", [(0.04, 0.16, z), (0.15, 0.17, z), (0.22, 0.16, z)], 0.012, "brass", 2, smooth_path=True))
        P.append(lathe(f"Rose{z}", (0.035, 0.16, z), [(0.0, 0.0), (0.03, 0.0), (0.028, 0.008), (0.0, 0.01)], "brass", 16,
                       basis=((0, 1, 0), (1, 0, 0), (0, 0, -1))))
    P.append(bevel_box("Top", (-0.3, 1.13, 0), (0.92, 0.05, z1 - z0 + 0.12), "wood", 0.02, segs=3))
    P.append(tube("Nosing", [(0.16, 1.12, z0 - 0.06), (0.16, 1.12, z1 + 0.06)], 0.035, "wood", 4))
    return P


def back_bar():
    """The back bar's dressing over its cupboards (the case is lp_case_backbar), its origin at (BarX0, 0, middle of its
    run): the mirror, its gilt frame and moulded cornice, three shelves with their lips on carved brackets."""
    z0, z1 = -2.5, 2.5
    P = [bevel_box("Mirror", (0.1, 2.0, 0), (0.02, 1.6, z1 - z0), "mirror", 0.001)]
    P.append(bevel_box("Frame", (0.12, 2.82, 0), (0.05, 0.06, z1 - z0 + 0.16), "gold", 0.012, segs=3))
    P.append(bevel_box("Cornice", (0.135, 2.87, 0), (0.08, 0.05, z1 - z0 + 0.24), "gold", 0.015, segs=3))
    P.append(bevel_box("CorniceTop", (0.14, 2.905, 0), (0.09, 0.02, z1 - z0 + 0.28), "gold", 0.008))
    for fz in (z0 - 0.04, z1 + 0.04):
        P.append(bevel_box(f"Stile{fz}", (0.12, 2.0, fz), (0.05, 1.66, 0.08), "gold", 0.012, segs=3))
    for y in (1.45, 1.95, 2.45):
        P.append(bevel_box(f"Shelf{y}", (0.25, y, 0), (0.3, 0.035, z1 - z0), "wood", 0.008))
        P.append(bevel_box(f"Lip{y}", (0.395, y + 0.02, 0), (0.012, 0.03, z1 - z0), "wood", 0.004))
        bz = z0 + 0.25
        while bz < z1:
            P.append(bevel_box(f"Cleat{y}{bz}", (0.12, y - 0.09, bz), (0.03, 0.15, 0.03), "wood", 0.006))
            P.append(tube(f"Brace{y}{bz}", [(0.12, y - 0.15, bz), (0.2, y - 0.1, bz), (0.33, y - 0.02, bz)], 0.012, "wood", 2, smooth_path=True))
            bz += 1.5
    return P


# ================================================================== cloth: the chairs' dust sheets

SHEETS = {1: (0.6, 0.15, 0.02), 2: (2.4, 0.2, -0.03), 3: (4.4, 0.13, 0.0)}   # (phase, hem height, slew) per sheet


def dust_sheet(seed):
    """A dust sheet thrown over the lodge's dining chair (in the chair model's frame), drawn as the game's own sheet was
    (SkiLodge.ChairDustSheet: the seat, the back rising behind it, a squircle rim round the frame, the fall flaring out to
    an uneven hem in folds that deepen toward it) but finely: 72 round, 28 from the middle to the hem, small creases over
    it, a cloth's thickness."""
    ph, hem0, slew = SHEETS[seed]
    na, n_in, n_out = 72, 10, 18
    seat_y, back_y, back_z, cz, ax, az = 0.515, 1.02, 0.2, 0.03, 0.285, 0.34

    def top(x, z):
        return max(seat_y - x * x * 0.3, back_y - max(0.0, back_z - z) * 3.2 - x * x * 0.7 - max(0.0, z - 0.3) * 1.5)

    def sgn(v):
        return 1.0 if v >= 0 else -1.0
    rows = []
    for i in range(na):
        a = TAU * i / na
        ca, sa = math.cos(a), math.sin(a)
        rim = V((ax * sgn(ca) * abs(ca) ** 0.5, cz + az * sgn(sa) * abs(sa) ** 0.5))
        rim_y = top(rim.x, rim.y)
        col = []
        for j in range(n_in + 1):
            s = j / n_in
            p = V((0, cz)).lerp(rim, s)
            col.append(V((p.x, top(p.x, p.y) + 0.004 * math.sin(a * 3 + ph) * s, p.y)))
        hem = max(0.06, hem0 + 0.08 * math.sin(a * 2 + ph) + 0.04 * math.sin(a * 5 + ph * 2) - 0.08 * max(0.0, sa))
        out = V((rim.x, rim.y - cz)).normalized()
        for j in range(1, n_out + 1):
            r = j / n_out
            fold = (0.028 * math.sin(a * 7 + ph + r * 1.3) + 0.012 * math.sin(a * 13 + ph * 3)) * r
            fold += 0.006 * math.sin(a * 29 + ph * 5 + r * 4) * r   # (the small creases between)
            p = rim + out * (0.08 * r * r + 0.015 * r + fold + 0.012 * math.sin(r * math.pi))
            y = rim_y + (hem - rim_y) * r
            col.append(V((p.x + slew * r, y, p.y)))
        rows.append(col)
    bm = bmesh.new()
    vs = [[bm.verts.new(G(p)) for p in col] for col in rows]
    nr = n_in + n_out + 1
    for i in range(na):
        i1 = (i + 1) % na
        for j in range(nr - 1):
            try:
                bm.faces.new((vs[i][j], vs[i1][j], vs[i1][j + 1], vs[i][j + 1]))
            except ValueError:
                pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    # a little crumple over it all (the cloth's own wrinkles, not the folds)
    off = seed * 3.1
    for v in bm.verts:
        g = v.co
        v.co.z += 0.0035 * fbm(g.x * 9 + off, g.y * 9, g.z * 9, 3)
    ob = obj_from_bmesh(bm, "Sheet", R("linen"))
    return [_finish(ob, True, thick=0.003)]




# ================================================================== rugs, the winter gear, bookshelves

def rug_oval():
    """SkiLodge.Overhaul.Rug as a braided oval rug, unit radius (the game scales it to rx, rz): its braids round it in
    rings, a little rise at each, the edge rolled down; its UVs radial (the rug's texture laid as the old disc's)."""
    nr, na = 22, 96
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new()
    rows = []
    for j in range(nr + 1):
        r = j / nr
        y = 0.008 + 0.0022 * math.sin(r * nr * math.pi) ** 2 - (0.006 if j == nr else 0.0)
        rows.append([(bm.verts.new(G((math.cos(i / na * TAU) * r, y, math.sin(i / na * TAU) * r))), r, i / na) for i in range(na)])
    centre = bm.verts.new(G((0, 0.01, 0)))
    for j in range(nr):
        for i in range(na):
            k = (i + 1) % na
            quad = (rows[j][i], rows[j][k], rows[j + 1][k], rows[j + 1][i])
            f = bm.faces.new([q[0] for q in quad])
            for lp, (v, r, a) in zip(f.loops, quad):
                ang = math.atan2(v.co.y, v.co.x)
                lp[uvl].uv = (0.5 + math.cos(ang) * 0.5 * r, 0.5 + math.sin(ang) * 0.5 * r)
    ob = obj_from_bmesh(bm, "Rug", R("rug"))
    return [_finish(ob, True, thick=0.006)]


def ski(name, length, role):
    """One ski (SkiLodge.Overhaul.Ski) along +Y, its face toward +Z: a long thin board, eased, the tip bent off the wall,
    a toe iron and a strap."""
    P = []
    n = 24
    def at(u):   # 0..1 up the board; the tip past 1
        y = -length * 0.5 + (length + 0.16) * u
        z = 0.0 if y < length * 0.5 else ((y - length * 0.5) / 0.16) ** 2 * 0.1
        w = 0.075 if y < length * 0.5 else 0.075 - (y - length * 0.5) / 0.16 * 0.03
        return y, z, w
    bm = bmesh.new()
    rows = []
    for i in range(n + 1):
        y, z, w = at(i / n)
        rows.append([bm.verts.new(G(p)) for p in ((-w / 2, y, z - 0.01), (w / 2, y, z - 0.01), (w / 2, y, z + 0.01), (-w / 2, y, z + 0.01))])
    for i in range(n):
        for k in range(4):
            bm.faces.new((rows[i][k], rows[i][(k + 1) % 4], rows[i + 1][(k + 1) % 4], rows[i + 1][k]))
    bm.faces.new(rows[0][::-1])
    bm.faces.new(rows[-1])
    P.append(_finish(obj_from_bmesh(bm, name, R(role)), True, bevel=0.004))
    P.append(bevel_box(name + "Iron", (0, -0.05, 0.02), (0.09, 0.05, 0.022), "brass", 0.004))
    P.append(bevel_box(name + "Strap", (0, -0.1, 0.025), (0.095, 0.035, 0.03), "strap", 0.006))
    return P


def _move(objs, basis, at):
    """Turn and place pieces (game frame)."""
    f = _xf(basis, at)
    for o in objs:
        for v in o.data.vertices:
            g = V((v.co.x, v.co.z, v.co.y))
            v.co = G(f(g))
    return objs


def skis_crossed():
    a = 0.42
    P = _move(ski("SkiA", 1.75, "ski"), ((math.cos(a), math.sin(a), 0), (-math.sin(a), math.cos(a), 0), (0, 0, 1)), (0, 0, 0))
    P += _move(ski("SkiB", 1.75, "ski"), ((math.cos(-a), math.sin(-a), 0), (-math.sin(-a), math.cos(-a), 0), (0, 0, 1)), (0, 0, 0.03))
    P.append(bevel_box("Peg", (0, 0, 0.06), (0.12, 0.12, 0.03), "strap", 0.01))
    return P


def skis_pair():
    P = _move(ski("SkiA", 1.8, "ski"), None, (-0.07, 0, 0)) + _move(ski("SkiB", 1.8, "ski"), None, (0.07, 0, 0))
    P.append(bevel_box("Strap", (0, 0.55, 0.025), (0.26, 0.05, 0.03), "strap", 0.008))
    return P


def poles():
    P = []
    for x in (-0.08, 0.08):
        P.append(tube(f"Shaft{x}", [(x, -0.6, 0), (x * 0.6, 0.65, 0)], 0.012, "wood", 2, r_end=0.014))
        P.append(lathe(f"Tip{x}", (x, -0.6, 0), [(0.0, -0.03), (0.008, -0.02), (0.012, 0.0)], "brass", 10))
        P.append(tube(f"Grip{x}", [(x * 0.62, 0.5, 0), (x * 0.6, 0.68, 0)], 0.02, "strap", 3))
        P.append(torus(f"Loop{x}", (x * 0.6, 0.62, 0.025), (1, 0, 0), 0.05, 0.005, "strap", 16))
        P.append(torus(f"Basket{x}", (x, -0.48, 0), (0, 1, 0), 0.06, 0.005, "strap", 20))
        for i in range(4):
            a = i / 4 * math.pi
            d = V((math.cos(a), 0, math.sin(a)))
            P.append(tube(f"Spoke{x}{i}", [V((x, -0.48, 0)) - d * 0.06, V((x, -0.48, 0)) + d * 0.06], 0.003, "strap", 1))
    return P


def snowshoes():
    P = []
    for x in (-0.17, 0.17):
        n = 40
        def Pt(i):
            a = i / n * TAU
            tail = max(0.0, -math.sin(a)) * 0.18
            return V((x + math.cos(a) * 0.15 * (1 - tail * 2.2), math.sin(a) * 0.28 - tail, 0))
        P.append(tube(f"Frame{x}", [Pt(i) for i in range(n)], 0.011, "frame", 3, cyclic=True, smooth_path=True))
        for j in range(-3, 4):
            P.append(tube(f"LaceA{x}{j}", [(x - 0.12, j * 0.06, 0), (x + 0.12, j * 0.06 + 0.05, 0)], 0.0028, "lace", 1))
            P.append(tube(f"LaceB{x}{j}", [(x - 0.12, j * 0.06 + 0.05, 0), (x + 0.12, j * 0.06, 0)], 0.0028, "lace", 1))
        P.append(tube(f"CrossBar{x}", [(x - 0.13, 0.05, 0), (x + 0.13, 0.05, 0)], 0.007, "frame", 2))
        P.append(bevel_box(f"Binding{x}", (x, 0.02, 0.015), (0.1, 0.06, 0.02), "strap", 0.006))
    return P


def bookshelf():
    """SkiLodge.Overhaul.Shelf in its frame (its back to the wall, its face toward +Z; the game's floor point the origin,
    the shelf 0.18 out from the wall): the back, the sides, five shelves, a plinth and a moulded cornice."""
    o = 0.18
    P = [bevel_box("Back", (0, 0.95, -0.15 + o), (1.3, 1.9, 0.03), "wood", 0.002)]
    for s in (-1, 1):
        P.append(bevel_box(f"Side{s}", (s * 0.635, 0.95, o), (0.03, 1.9, 0.32), "wood", 0.006))
    for k in range(5):
        P.append(bevel_box(f"Shelf{k}", (0, 0.06 + k * 0.45, o), (1.24, 0.03, 0.3), "wood", 0.006))
        P.append(bevel_box(f"ShelfEdge{k}", (0, 0.06 + k * 0.45, 0.148 + o), (1.24, 0.034, 0.012), "wood", 0.004))
    P.append(bevel_box("Plinth", (0, 0.04, 0.145 + o), (1.24, 0.08, 0.02), "wood", 0.005))
    P.append(bevel_box("Cornice1", (0, 1.92, 0.01 + o), (1.36, 0.04, 0.35), "wood", 0.01, segs=3))
    P.append(bevel_box("Cornice2", (0, 1.95, 0.005 + o), (1.32, 0.025, 0.33), "wood", 0.008))
    P.append(bevel_box("Cornice3", (0, 1.975, 0.0 + o), (1.38, 0.022, 0.36), "wood", 0.008))
    return P


# ================================================================== picture frames

def picture_frame():
    """LodgeProps.PictureFrame round a unit canvas (1 x 1) at a moulding 0.1 wide: an eased outer edge, a crest, a cove
    down to a lip at the canvas, mitred at the corners. The game stretches it to its canvas: what's inside +-0.5 stretches
    with the canvas, what's outside it with the moulding's width (each side keeps its profile)."""
    fw = 0.1
    def ring(grow, z):
        hx = hy = 0.5 + grow
        e = 0.0015
        return [V((hx, hy - e, z)), V((hx - e, hy, z)), V((-hx + e, hy, z)), V((-hx, hy - e, z)),
                V((-hx, -hy + e, z)), V((-hx + e, -hy, z)), V((hx - e, -hy, z)), V((hx, -hy + e, z))]
    prof = [(fw, -0.025), (fw, 0.012), (fw * 0.92, 0.02), (fw * 0.85, 0.024), (fw * 0.7, 0.03), (fw * 0.55, 0.032), (fw * 0.42, 0.03),
            (fw * 0.3, 0.026), (fw * 0.22, 0.019), (fw * 0.16, 0.014), (0.012, 0.013), (0.006, 0.012), (0.0, 0.003)]
    rings = [ring(g, z) for (g, z) in prof]
    # (rings in the frame's own XY plane: a game point (x, y, z) with z out of the wall)
    bm = bmesh.new()
    vs = [[bm.verts.new(G(p)) for p in r] for r in rings]
    for k in range(len(vs) - 1):
        for i in range(8):
            j = (i + 1) % 8
            bm.faces.new((vs[k][i], vs[k][j], vs[k + 1][j], vs[k + 1][i]))
    ob = _finish(obj_from_bmesh(bm, "Frame", R("wood")), True)
    back = bevel_box("Lip", (0, 0, -0.012), (1.01, 1.01, 0.02), "black", 0.0)
    return [ob, back]


# ================================================================== the crawlspace's Christmas

def gift():
    """CrawlClutter.Gift at s = 1 (the game scales it): a wrapped box with its paper folded in at the corners, a ribbon both
    ways, a bow of two loops and two tails on top."""
    s = 1.0
    size = (s, s * 0.7, s * 0.85)
    c = V((0, size[1] * 0.5, 0))
    P = [bevel_box("Box", c, size, "wrap", 0.012)]
    P.append(bevel_box("RibbonA", c, (size[0] * 1.012, size[1] * 1.012, s * 0.12), "ribbon", 0.004))
    P.append(bevel_box("RibbonB", c, (s * 0.12, size[1] * 1.012, size[2] * 1.012), "ribbon", 0.004))
    top = c + V((0, size[1] * 0.5 + 0.01, 0))
    for side in (-1, 1):
        loop = [top + V((side * (0.02 + 0.12 * (1 - math.cos(a))), 0.11 * math.sin(a), 0.03 * math.sin(2 * a))) for a in [i / 16 * math.pi for i in range(17)]]
        loop += [top + V((side * 0.02, 0.0, 0.0))]
        P.append(tube(f"Loop{side}", loop, 0.018, "ribbon", 2, smooth_path=True, cyclic=True))
        P.append(tube(f"Tail{side}", [top, top + V((side * 0.08, -0.005, 0.12)), top + V((side * 0.12, -0.03, 0.2))], 0.014, "ribbon", 2, smooth_path=True))
    P.append(blob("Knot", top + V((0, 0.02, 0)), (0.04, 0.035, 0.04), "ribbon", 3, 0.05))
    return P


def bow():
    """CrawlClutter.Bow at s = 1: two loops of ribbon, the knot, two tails, lying on the floor."""
    P = []
    for side in (-1, 1):
        loop = [V((side * (0.1 + 0.62 * math.sin(a * 0.5) ** 1.0 * 0.9), 0.12 + 0.45 * math.sin(a) * 0.5, 0.28 * math.sin(a) * side * 0.4))
                for a in [i / 20 * TAU for i in range(20)]]
        P.append(tube(f"Loop{side}", loop, 0.12, "ribbon", 2, smooth_path=True, cyclic=True))
        P.append(tube(f"Tail{side}", [V((side * 0.1, -0.05, 0.2)), V((side * 0.35, -0.2, 0.8)), V((side * 0.45, -0.25, 1.3))], 0.08, "ribbon", 2,
                      smooth_path=True))
    P.append(blob("Knot", (0, 0.05, 0), (0.28, 0.24, 0.28), "ribbon", 5, 0.05))
    return P


def tinsel_strand():
    """CrawlClutter.Tinsel: a run of tinsel 1 m along +X, snaking, its shreds standing out all round."""
    random.seed(7)
    pts = [V((-0.5 + i / 24, 0.01, math.sin(i * 0.9) * 0.05)) for i in range(25)]
    P = [tube("Core", pts, 0.006, "tinsel", 2, smooth_path=True)]
    for i in range(1, 24):
        for s in range(5):
            d = V((random.uniform(-1, 1), random.uniform(-0.3, 1), random.uniform(-1, 1))).normalized()
            P.append(tube(f"Shred{i}_{s}", [pts[i], pts[i] + d * random.uniform(0.018, 0.03)], 0.0018, "tinsel", 1, cap=False))
    return P


def paper_ball():
    """CrawlClutter.Crumple at r = 1: a balled-up sheet, creased hard."""
    return [blob("Ball", (0, 0.75, 0), (1.0, 0.8, 1.05), "paper", 17, 0.18, 3.6, 3)]


def paper_sheet():
    """CrawlClutter.Sheet at 1 x 1: a loose sheet, curling up at its edges and corners, creased across."""
    random.seed(3)
    def f(u, v):
        uu, vv = u - 0.5, v - 0.5
        edge = max(abs(uu), abs(vv)) * 2
        crease = 0.006 * (abs(math.sin(u * 7.0 + 0.4)) + abs(math.sin(v * 5.3 + 1.1)))
        return V((uu, 0.004 + 0.03 * edge ** 2 + crease, vv))
    return [grid_sheet("Sheet", f, 14, 14, "paper", thick=0.0015)]


def wreath_small():
    """CrawlClutter.Wreath at r = 1, flat to +Z (hung on a wall): a ring of fir clumps bound round, berries in it, a red bow at
    its foot."""
    P = []
    random.seed(19)
    for i in range(28):
        a = i / 28 * TAU
        p = V((math.cos(a), math.sin(a), 0.0))
        P.append(blob(f"Fir{i}", p * 1.0, (0.3, 0.3, 0.22), "fir", i * 7 + 1, 0.22, 3.0, 2))
        if i % 3 == 1:
            for b in range(3):
                q = p * (1.0 + random.uniform(-0.12, 0.12)) + V((random.uniform(-0.08, 0.08), random.uniform(-0.08, 0.08), 0.2))
                P.append(blob(f"Berry{i}_{b}", q, (0.06, 0.06, 0.06), "berry", i * 3 + b, 0.02, 2.0, 2))
    foot = V((0, -1.05, 0.15))
    for s in (-1, 1):
        P.append(torus(f"Bow{s}", foot + V((s * 0.18, 0, 0)), (0, 0, 1), 0.18, 0.055, "ribbon", 16))
        P.append(tube(f"Tail{s}", [foot, foot + V((s * 0.1, -0.25, 0.02)), foot + V((s * 0.14, -0.45, 0.0))], 0.04, "ribbon", 2, smooth_path=True))
    P.append(blob("Knot", foot, (0.09, 0.09, 0.06), "ribbon", 2, 0.03))
    return P


def holly_strand():
    """CrawlClutter.Holly: a strand 1 m along +X stapled at its ends, sagging 0.16 between, spiky leaves in pairs, berries
    every few knots (toward +Z: out from the wall)."""
    random.seed(23)
    P = [tube("Vine", [V((-0.5 + t / 20, -0.16 * 4 * (t / 20) * (1 - t / 20), 0.03)) for t in range(21)], 0.003, "holly", 1, smooth_path=True)]
    for i in range(15):
        t = i / 14
        p = V((-0.5 + t, -0.16 * 4 * t * (1 - t), 0.03))
        for l in range(2):
            d = V((random.uniform(-1, 1), random.uniform(-0.6, 0.6), random.uniform(0.2, 1))).normalized()
            P.append(holly_leaf(f"Leaf{i}_{l}", p + d * 0.02, d, i * 2 + l))
        if i % 3 == 1:
            for b in range(3):
                P.append(blob(f"Berry{i}_{b}", p + V((random.uniform(-0.012, 0.012), -0.01 - 0.008 * b, 0.02)), (0.009, 0.009, 0.009), "berry", i + b, 0.02, 2.0, 2))
    return P


def holly_leaf(name, at, d, seed):
    """One spiky holly leaf along d: a shallow V of a blade with points round its edge."""
    side = d.cross(V((0, 1, 0)) if abs(d.y) < 0.9 else V((1, 0, 0))).normalized()
    up = side.cross(d).normalized()
    pts = []
    L = 0.045
    for k in range(9):
        u = k / 8
        w = math.sin(u * math.pi) * 0.016 + (0.005 if k % 2 else 0.0)
        pts.append((u, w))
    bm = bmesh.new()
    mid = [bm.verts.new(G(at + d * (u * L) + up * 0.003 * math.sin(u * math.pi))) for (u, _) in pts]
    for s in (-1, 1):
        edge = [bm.verts.new(G(at + d * (u * L) + side * (w * s))) for (u, w) in pts]
        for k in range(8):
            try:
                bm.faces.new((mid[k], mid[k + 1], edge[k + 1], edge[k]) if s > 0 else (mid[k], edge[k], edge[k + 1], mid[k + 1]))
            except ValueError:
                pass
    return _finish(obj_from_bmesh(bm, name, R("holly")), True, thick=0.0012)


def xmas_bulb():
    """CrawlDressing.StringLights' bulb: an old C9 bulb, its socket cap on the wire (the origin) hanging down."""
    return [lathe("Socket", (0, -0.024, 0), [(0.0, 0.0), (0.009, 0.0), (0.0095, 0.006), (0.0095, 0.016), (0.008, 0.019), (0.0, 0.02)], "socket", 20),
            lathe("Bulb", (0, -0.024, 0), [(0.0, -0.046), (0.006, -0.042), (0.012, -0.03), (0.014, -0.016), (0.011, -0.004), (0.008, 0.0)], "bulb", 24)]


def wire_segment():
    """The lights' wire: a twisted pair 1 m along +X (the game stretches it between its points)."""
    P = []
    for s in (0, math.pi):
        P.append(tube(f"Lead{s}", [V((-0.5 + i / 12, 0.0028 * math.cos(i / 12 * TAU * 4 + s), 0.0028 * math.sin(i / 12 * TAU * 4 + s))) for i in range(13)],
                      0.0024, "wire", 0, cap=False))
    return P


# ================================================================== the list

PIECES = {
    "lp_table_lamp": (table_lamp, (0, 0.35, 0), 1.3),
    "lp_bankers_lamp": (bankers_lamp, (0, 0.25, 0), 1.0),
    "lp_ceiling_dome": (ceiling_dome, (0, -0.08, 0), 0.9),
    "lp_sconce": (sconce, (0, 0.06, 0.1), 0.7),
    "lp_chandelier_hall": (lambda: chandelier(8.2, 2.8, 14), (0, -8.2, 0), 9.0),
    "lp_chandelier_room": (lambda: chandelier(1.6, 1.2, 8), (0, -1.6, 0), 4.5),
    "lp_wagon_wheel": (lambda: wagon_wheel(1.25, 0.62, 8), (0, -1.0, 0), 3.2),
    "lp_pendant": (lambda: pendant(1.75), (0, -1.6, 0), 1.4),
    "lp_split_log": (split_log, (0, -0.1, 0), 3.0),
    "lp_fire_tools": (fire_tools, (0, 0.4, 0), 1.6),
    "lp_table_candle": (table_candle, (0, 0.05, 0), 0.4),
    "lp_knob": (knob, (0, 0.015, 0), 0.15),
    "lp_drawer_front": (lambda: drawer_front(False), (0, 0, 0), 0.8),
    "lp_drawer_front_field": (lambda: drawer_front(True), (0, 0, 0), 0.9),
    "lp_case_backbar": (lambda: case_piece("lp_case_backbar"), (0, 0.5, 0), 6.0),
    "lp_case_cabinet": (lambda: case_piece("lp_case_cabinet"), (0, 0.5, 0), 2.2),
    "lp_case_nightstand": (lambda: case_piece("lp_case_nightstand"), (0, 0.3, 0), 1.4),
    "lp_case_vanity": (lambda: case_piece("lp_case_vanity"), (0, 0.45, 0), 2.0),
    "lp_case_dresser": (lambda: case_piece("lp_case_dresser"), (0, 0.55, 0), 2.6),
    "lp_cabinet_case_110": (lambda: cabinet_case(1.1, 2.1, 0.6), (0, 1.05, 0), 3.8),
    "lp_cabinet_case_120": (lambda: cabinet_case(1.2, 2.3, 0.6), (0, 1.15, 0), 4.0),
    "lp_cabinet_door_110": (lambda: cabinet_door(1.1, 0.1, 2.1 - 0.008, (1.1 - 0.08, 1.1, 0.025)), (0.55, 1.05, 0), 3.8),
    "lp_cabinet_door_120": (lambda: cabinet_door(1.2, 0.1, 2.3 - 0.008, (1.2 - 0.08, 1.1, 0.025)), (0.6, 1.15, 0), 4.0),
    "lp_writing_desk": (lambda: writing_desk(1.2, 0.64, 0.765, 0.9, 0.58, 0.7, 0.031), (0, 0.4, 0), 2.4),
    "lp_tub": (lambda: tub(0.9, 0.4, 0.56), (0, 0.3, 0), 3.0),
    "lp_basin_top": (lambda: basin_top(0.94, 0.6, 0.9, 0.0, -0.04), (0, 0.85, 0), 1.8),
    "lp_lavatory": (lavatory, (0, 0.4, 0), 1.8),
    "lp_front_desk": (front_desk, (0, 0.6, 0), 4.5),
    "lp_bar_counter": (bar_counter, (0, 0.6, 0), 6.5),
    "lp_back_bar": (back_bar, (0.2, 2.0, 0), 6.5),
    "lp_dust_sheet_a": (lambda: dust_sheet(1), (0, 0.6, 0), 2.2),
    "lp_dust_sheet_b": (lambda: dust_sheet(2), (0, 0.6, 0), 2.2),
    "lp_dust_sheet_c": (lambda: dust_sheet(3), (0, 0.6, 0), 2.2),
    "lp_rug_oval": (rug_oval, (0, 0, 0), 3.0),
    "lp_skis_crossed": (skis_crossed, (0, 0, 0), 2.6),
    "lp_skis_pair": (skis_pair, (0, 0, 0), 2.6),
    "lp_poles": (poles, (0, 0, 0), 2.0),
    "lp_snowshoes": (snowshoes, (0, 0, 0), 1.4),
    "lp_bookshelf": (bookshelf, (0, 1.0, 0.18), 3.6),
    "lp_picture_frame": (picture_frame, (0, 0, 0), 2.2),
    "lp_gift": (gift, (0, 0.4, 0), 2.6),
    "lp_bow": (bow, (0, 0.1, 0), 3.0),
    "lp_tinsel": (tinsel_strand, (0, 0, 0), 1.2),
    "lp_paper_ball": (paper_ball, (0, 0.7, 0), 4.0),
    "lp_paper_sheet": (paper_sheet, (0, 0, 0), 1.6),
    "lp_wreath_small": (wreath_small, (0, 0, 0), 4.5),
    "lp_holly": (holly_strand, (0, -0.08, 0), 1.2),
    "lp_xmas_bulb": (xmas_bulb, (0, -0.04, 0), 0.15),
    "lp_wire": (wire_segment, (0, 0, 0), 0.2),
}

CAV = {"lp_knob": 128, "lp_xmas_bulb": 128, "lp_wire": 128, "lp_table_candle": 256, "lp_drawer_front": 256, "lp_drawer_front_field": 256,
       "lp_paper_ball": 256, "lp_paper_sheet": 256, "lp_holly": 256, "lp_tinsel": 256, "lp_bow": 256, "lp_gift": 512,
       "lp_chandelier_hall": 1024, "lp_case_backbar": 1024, "lp_bar_counter": 1024, "lp_back_bar": 1024, "lp_front_desk": 1024}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o)
        fn, tgt, dist = PIECES[name]
        try:
            export(fn(), name, tgt, dist, dist * 0.35, 30, CAV.get(name, 512))
        except Exception as e:
            import traceback
            traceback.print_exc()
            print(f"[lodge_props] FAILED {name}: {e}")
    print("[lodge_props] triangles:", TRIS)
