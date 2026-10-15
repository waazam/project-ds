"""Held and carried items, modelled in Blender (the owner, 2026-10-15: "from now on everything should be made in
blender"). Built and exported through the furniture pipeline (furniture.py: faces grouped by role, a baked cavity map
on the second UV set, the game's own materials put on each role by FurnitureKit), into assets/models/furniture/:

    blender -b --python tools/Blender/items.py [-- name ...]

The difference from furniture: these are hand-sized and their materials are the held items' small tiles (the camera's
leatherette is a 64 px grain at ~24 repeats a metre), so the first UV set is box-projected at UV_REPEAT a metre, not
one. Blender's Z up; a piece is built with its front toward -Y (the export mirrors it so the game's front is -Z).

  camera_slr    the 35 mm SLR (held for every photo from Act 1; the pickup on the car): its body centred on the origin,
                the lens on its front, the advance lever and shutter at its right end (+X), the rewind at its left.
                Roles: leatherette, chrome, black, rubber, glass, strap.
  camera_strap  the tan leather strap lying in a loose loop, for the pickup on the ground (role: strap).
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, apply_mods, add_mod, obj_from_bmesh, preview
import furniture as F

V = Vector
UV_REPEAT = 24.0

COLOURS = {"leatherette": (0.05, 0.05, 0.05), "chrome": (0.75, 0.75, 0.78), "black": (0.02, 0.02, 0.02),
           "rubber": (0.03, 0.03, 0.03), "glass": (0.1, 0.12, 0.2), "strap": (0.55, 0.38, 0.22)}


def role(name):
    return mat(name, COLOURS.get(name, (0.5, 0.5, 0.5)), 0.6, 1.0 if name == "chrome" else 0.0)


def rbox(name, centre, size, material, bevel=0.002, segs=3):
    return F.block(name, centre, size, material, bevel=bevel, segs=segs)


def turned(name, axis, centre, profile, material, segs=48):
    """A lathe [(radius, along)] turned round `axis` ('-y' toward the front, 'z' up) from `centre`."""
    ob = F.lathe(name, profile, material, segs)
    if axis == "-y":
        ob.rotation_euler = Euler((math.radians(90), 0, 0))   # local +Z -> world -Y
    ob.location = V(centre)
    bpy.context.view_layer.update()
    return ob


def knurl(name, axis, centre, r, length, z0, material, teeth=48, depth=0.0012):
    """A ring of fine ridges (a focus ring's or a dial's grip): radius alternating tooth by tooth."""
    bm = bmesh.new()
    rings = []
    for z in (z0, z0 + length):
        ring = []
        for i in range(teeth * 2):
            a = i / (teeth * 2) * math.tau
            rr = r + (depth if i % 2 == 0 else 0.0)
            ring.append(bm.verts.new((math.cos(a) * rr, math.sin(a) * rr, z)))
        rings.append(ring)
    n = teeth * 2
    for i in range(n):
        bm.faces.new((rings[0][i], rings[0][(i + 1) % n], rings[1][(i + 1) % n], rings[1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, name, material, smooth=False)
    if axis == "-y":
        ob.rotation_euler = Euler((math.radians(90), 0, 0))
    ob.location = V(centre)
    bpy.context.view_layer.update()
    return ob


def tapered(name, centre, bottom, top, height, material, bevel=0.0015):
    """A block whose top face is smaller than its bottom (the prism's hump): bottom/top are (x, y) sizes."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        sx, sy = (top if v.co.z > 0 else bottom)
        v.co = V((v.co.x * sx, v.co.y * sy, v.co.z * height))
    bmesh.ops.translate(bm, vec=V(centre), verts=bm.verts)
    ob = obj_from_bmesh(bm, name, material, smooth=False)
    return F.finish(ob, True, bevel, 3)


def camera_slr():
    W, H, D, TOP = 0.143, 0.062, 0.036, 0.016
    leather, chrome, black, rubber, glass, strap = (role(n) for n in ("leatherette", "chrome", "black", "rubber", "glass", "strap"))
    P = []
    zt = H * 0.5                      # the body's top, the top plate's underside
    zp = zt + TOP                     # the top plate's top
    # the body: the leatherette wrap between the chrome ends, a touch proud of the plates
    P.append(rbox("Body", (0, 0, 0), (W - 0.012, D, H), leather, 0.004, 3))
    for s in (-1, 1):
        P.append(rbox(f"End{s}", (s * (W * 0.5 - 0.0035), 0, 0), (0.007, D + 0.0008, H), chrome, 0.0025, 3))
    # the bottom plate and the top plate (chrome), the top one a little deeper toward the back
    P.append(rbox("Base", (-0.002, 0, -zt - 0.0025), (W - 0.004, D + 0.002, 0.005), chrome, 0.0018, 3))
    P.append(rbox("Top", (-0.001, 0.0015, zt + TOP * 0.5), (W - 0.002, D + 0.005, TOP), chrome, 0.003, 4))
    # the prism's hump over the lens: chrome sides, a black nameplate face raked back, the hot shoe on its crown
    lx = -0.004
    P.append(tapered("Prism", (lx, 0.0025, zp + 0.013), (0.058, 0.04), (0.03, 0.024), 0.028, chrome, 0.003))
    plate = rbox("Nameplate", (lx, -0.0165, zp + 0.011), (0.036, 0.002, 0.014), black, 0.0006, 2)
    plate.rotation_euler = Euler((math.radians(-24), 0, 0))
    P.append(plate)
    P.append(rbox("Shoe", (lx, 0.0025, zp + 0.0285), (0.02, 0.018, 0.003), chrome, 0.0006, 2))
    P.append(rbox("ShoeRails", (lx, 0.0025, zp + 0.0305), (0.022, 0.016, 0.0012), black, 0.0003, 1))
    # the eyepiece on the back, a rubber cup round its little window
    P.append(rbox("Eyecup", (lx, D * 0.5 + 0.006, zp + 0.01), (0.026, 0.01, 0.021), rubber, 0.003, 3))
    P.append(rbox("EyeGlass", (lx, D * 0.5 + 0.0112, zp + 0.01), (0.016, 0.0006, 0.012), glass, 0.0, 1))
    # the lens, turned toward the front: the chrome bayonet ring, the black barrel, the knurled focus ring, the aperture
    # ring's thin chrome line and its scale, the filter rim, the glass set back in the black throat
    lc = (lx, -D * 0.5, -0.001)
    P.append(turned("Bayonet", "-y", lc, [(0.0305, -0.001), (0.0305, 0.006), (0.0285, 0.007)], chrome, 64))
    P.append(turned("Barrel", "-y", lc, [(0.0285, 0.006), (0.029, 0.016), (0.0275, 0.017), (0.0275, 0.04), (0.0292, 0.041),
                                        (0.0292, 0.047), (0.0268, 0.048), (0.0242, 0.048)], black, 64))
    P.append(knurl("Focus", "-y", lc, 0.0276, 0.019, 0.0185, rubber, 56, 0.0015))
    P.append(turned("ApertureLine", "-y", lc, [(0.0294, 0.0102), (0.0294, 0.0110), (0.0291, 0.0110)], chrome, 64))
    P.append(knurl("Aperture", "-y", lc, 0.0289, 0.0045, 0.0113, black, 36, 0.0006))
    P.append(turned("Throat", "-y", lc, [(0.0242, 0.048), (0.021, 0.044), (0.019, 0.043)], black, 64))
    P.append(turned("Glass", "-y", lc, [(0.019, 0.043), (0.014, 0.0455), (0.006, 0.0466), (0.0, 0.0469)], glass, 64))
    # the right end of the top plate: the shutter speed dial (knurled), the shutter button, the advance lever
    rx = 0.042
    P.append(turned("Dial", "z", (rx, 0.002, zp), [(0.0122, 0.0), (0.0122, 0.0065), (0.0105, 0.0076), (0.0, 0.0076)], black, 48))
    P.append(knurl("DialGrip", "z", (rx, 0.002, zp), 0.0122, 0.0045, 0.0008, rubber, 32, 0.0007))
    P.append(turned("Shutter", "z", (rx, 0.002, zp + 0.0076), [(0.0042, 0.0), (0.0042, 0.0038), (0.0034, 0.0052), (0.0, 0.0055)], chrome, 32))
    lever = rbox("Lever", (0.051, 0.0115, zp + 0.0016), (0.042, 0.0055, 0.0024), chrome, 0.0009, 2)
    lever.rotation_euler = Euler((0, 0, math.radians(10)))
    P.append(lever)
    P.append(rbox("LeverTip", (0.0715, 0.0155, zp + 0.0022), (0.0075, 0.012, 0.0072), black, 0.0022, 3))
    P.append(turned("LeverHub", "z", (0.031, 0.008, zp), [(0.0055, 0.0), (0.0055, 0.0032), (0.0, 0.0034)], chrome, 24))
    # the left end: the rewind knob and its folded crank
    P.append(turned("Rewind", "z", (-0.05, 0.001, zp), [(0.0112, 0.0), (0.0112, 0.0062), (0.0096, 0.0076), (0.0, 0.0076)], chrome, 40))
    P.append(knurl("RewindGrip", "z", (-0.05, 0.001, zp), 0.0112, 0.004, 0.0008, chrome, 28, 0.0006))
    P.append(rbox("Crank", (-0.05, 0.0065, zp + 0.0086), (0.006, 0.017, 0.0018), chrome, 0.0006, 2))
    P.append(turned("CrankKnob", "z", (-0.05, 0.0145, zp + 0.0095), [(0.0024, 0.0), (0.0024, 0.0045), (0.0, 0.0047)], black, 16))
    # the frame counter's little window by the lever, and the self-timer lever on the front beside the lens
    P.append(rbox("Counter", (0.031, -0.004, zp + 0.0004), (0.007, 0.004, 0.0012), glass, 0.0, 1))
    P.append(rbox("Timer", (0.038, -D * 0.5 - 0.002, 0.012), (0.006, 0.004, 0.015), black, 0.0012, 2))
    P.append(turned("TimerPivot", "-y", (0.038, -D * 0.5, 0.019), [(0.003, 0.0), (0.003, 0.003), (0.0, 0.0032)], chrome, 16))
    # the strap lugs (chrome eyes) and a stub of the tan strap from each
    for s in (-1, 1):
        lug = turned(f"Lug{s}", "z", (s * (W * 0.5 + 0.0025), 0.0, zt + 0.002), [(0.0042, 0.0), (0.0042, 0.006), (0.0, 0.0062)], chrome, 20)
        P.append(lug)
        stub = rbox(f"Strap{s}", (s * (W * 0.5 + 0.009), 0.002, zt - 0.007), (0.0028, 0.015, 0.028), strap, 0.0009, 2)
        stub.rotation_euler = Euler((0, s * math.radians(-20), 0))
        P.append(stub)
    return P


def camera_strap():
    """The strap in a loose loop on the ground beside the camera (the pickup's: Act 1, on the car's hatch / the leaves)."""
    strap = role("strap")
    pts = [(-0.07, 0.0, 0.068), (-0.09, 0.01, 0.03), (-0.11, 0.04, 0.004), (-0.12, 0.1, 0.004), (-0.06, 0.17, 0.004),
           (0.03, 0.19, 0.004), (0.12, 0.14, 0.004), (0.13, 0.06, 0.004), (0.1, 0.01, 0.03), (0.07, 0.0, 0.068)]
    cu = bpy.data.curves.new("StrapCurve", "CURVE")
    cu.dimensions = "3D"
    sp = cu.splines.new("NURBS")
    sp.points.add(len(pts) - 1)
    for p, (x, y, z) in zip(sp.points, pts):
        p.co = (x, -y, z, 1.0)
    sp.use_endpoint_u = True
    sp.order_u = 4
    cu.resolution_u = 10
    cu.bevel_depth = 0.0
    cu.extrude = 0.009
    cu.twist_mode = "Z_UP"
    ob = bpy.data.objects.new("Strap", cu)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    ob = bpy.context.view_layer.objects.active
    ob.data.materials.append(strap)
    add_mod(ob, "SOLIDIFY", thickness=0.0022, offset=0)
    apply_mods(ob)
    return [ob]


def export(objs, name, tgt, dist, h, yaw):
    """furniture.export, with the first UV set at the held items' tile scale."""
    ob = F.join_objs(objs, name)
    ob.scale = (1, -1, 1)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")
    add_mod(ob, "WEIGHTED_NORMAL", keep_sharp=True)
    add_mod(ob, "TRIANGULATE")
    apply_mods(ob)
    F.uv_layers(ob)
    for d in ob.data.uv_layers[0].data:
        d.uv = d.uv * UV_REPEAT
    F.bake_cavity(ob, name, 512)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(F.OUT, name + ".glb"), export_format="GLB", use_selection=True, export_yup=True,
                              export_apply=True, export_texcoords=True, export_normals=True, export_tangents=True,
                              export_materials="EXPORT", export_image_format="NONE")
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[items] {name}: {tris} triangles")
    preview(name, tgt, dist, h, yaw)
    return ob


PIECES = {
    "camera_slr": (camera_slr, (0, 0, 0.0), 0.42, 0.12, 35),
    "camera_strap": (camera_strap, (0, 0.1, 0.02), 0.6, 0.35, 30),
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        clear()
        fn, tgt, dist, h, yaw = PIECES[name]
        export(fn(), name, tgt, dist, h, yaw)
