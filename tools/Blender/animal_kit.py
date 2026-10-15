"""Shared pieces for Act 1's animals modelled in Blender (the owner, 2026-10-15: the doe and the squirrels "still too
circular and polygonal"; "from now on everything should be made in blender"): deer.py and squirrel.py.

The method, the stalker's and the wendigo's: every part lofted along its own curve from shaped cross-sections (ovals,
teardrops, flat bony fronts, ridges, a deeper throat, a wedge of a head: never a round tube or a ball), all fused by a
voxel remesh into one continuous skin so the limbs grow out of the body; painted, baked (albedo with its occlusion, and
a normal map of fine hair grain) from a dense copy onto a decimated one; a skeleton with nearest-bone weights blended at
the joints; clips keyed bone by bone. Fur: alpha-cut strand cards along the silhouettes (and the squirrel's plume made
of them), a separate surface with its own material ("Hair") that the game makes two-sided and alpha-cut.

Space: Blender Z-up, the animal facing +Y; P(x, y, z) takes the game's model space (y up, facing -z) into it, so the
export (Y-up, Blender +Y -> the game's -Z) lands back in the game's space.
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, Quaternion, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, join, apply_mods, add_mod, obj_from_bmesh, fbm, ROOT
import wendigo as W

V = Vector
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(PREV, exist_ok=True)


def P(x, y, z):
    """The game's model space (x right, y up, facing -z) to Blender's (x right, z up, facing +y)."""
    return V((x, -z, y))


def G(v):
    """Back from Blender's to the game's."""
    return V((v.x, v.z, -v.y))


def key(u, keys):
    """A value from (u, v) keys, eased between them."""
    if u <= keys[0][0]:
        return keys[0][1]
    for (a, va), (b, vb) in zip(keys, keys[1:]):
        if u <= b:
            t = (u - a) / max(1e-6, b - a)
            t = t * t * (3 - 2 * t)
            return va + (vb - va) * t
    return keys[-1][1]


def bell(th, at, width):
    d = math.atan2(math.sin(th - at), math.cos(th - at)) / width
    return math.exp(-d * d)


def sup(th, w, h, e):
    """A superellipse point: e 2 an ellipse, higher boxier, lower pointier."""
    c, s = math.cos(th), math.sin(th)
    return (w * math.copysign(abs(c) ** (2.0 / e), c), h * math.copysign(abs(s) ** (2.0 / e), s))


def catmull(pts, u):
    f = max(0.0, min(1.0, u)) * (len(pts) - 1)
    i = min(len(pts) - 2, int(f))
    t = f - i
    p0, p1, p2, p3 = pts[max(0, i - 1)], pts[i], pts[i + 1], pts[min(len(pts) - 1, i + 2)]
    return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)


def loft(name, pts, section, rings=40, sides=28, side_hint=V((1, 0, 0)), close=(0.05, 0.05), material=None):
    """A closed shape swept along the curve through pts (Blender space). The frame at each ring: X its side
    (from side_hint, carried along without twist), E = X x T its second axis. section(u, th) -> (x, e) offsets.
    The ends close: the section shrinks to a rounded point over `close` of the length at each end."""
    n = rings
    c = [catmull(pts, i / (n - 1)) for i in range(n)]
    tan = [(c[min(n - 1, i + 1)] - c[max(0, i - 1)]).normalized() for i in range(n)]
    hint = side_hint.copy()
    if abs(hint.dot(tan[0])) > 0.95:
        hint = V((0, 0, 1)) if abs(tan[0].z) < 0.9 else V((0, 1, 0))
    X = [(hint - tan[0] * hint.dot(tan[0])).normalized()]
    for i in range(1, n):
        v = X[-1] - tan[i] * X[-1].dot(tan[i])
        X.append(v.normalized() if v.length > 1e-8 else X[-1])
    bm = bmesh.new()
    rows = []
    for i in range(n):
        u = i / (n - 1)
        e2 = X[i].cross(tan[i]).normalized()
        a = min(1.0, u / close[0]) if close[0] > 0 else 1.0
        b = min(1.0, (1 - u) / close[1]) if close[1] > 0 else 1.0
        shrink = math.sqrt(max(0.0, 1 - (1 - a) ** 2)) * math.sqrt(max(0.0, 1 - (1 - b) ** 2))
        shrink = max(shrink, 0.04)
        row = []
        for j in range(sides):
            th = j / sides * math.tau
            x, e = section(u, th)
            row.append(bm.verts.new(c[i] + X[i] * x * shrink + e2 * e * shrink))
        rows.append(row)
    for i in range(n - 1):
        for j in range(sides):
            bm.faces.new((rows[i][j], rows[i][(j + 1) % sides], rows[i + 1][(j + 1) % sides], rows[i + 1][j]))
    for row, cc in ((rows[0], c[0] - tan[0] * 0.002), (rows[-1], c[-1] + tan[-1] * 0.002)):
        cv = bm.verts.new(cc)
        for j in range(sides):
            bm.faces.new((row[j], row[(j + 1) % sides], cv))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, name, material)


def fuse(objs, name, voxel, smooth=6, material=None):
    """One skin: the parts joined and voxel-remeshed together (the limbs grow out of the body, no seams), then relaxed."""
    ob = join(objs, name)
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel, use_smooth_shade=True)
    apply_mods(ob)
    if smooth:
        add_mod(ob, "SMOOTH", factor=0.6, iterations=smooth)
        apply_mods(ob)
    ob.data.materials.clear()
    if material:
        ob.data.materials.append(material)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def roughen(ob, amp, freq, seed=0.0):
    """A little irregularity over the whole skin (along each vertex normal): nothing perfectly symmetric or smooth."""
    me = ob.data
    for v in me.vertices:
        n = noise.noise(V((v.co.x * freq + seed, v.co.y * freq, v.co.z * freq)))
        v.co += v.normal * amp * n


# ------------------------------------------------------------------ fur

def hair_image(name, w=64, h=128, strands=60, seed=7):
    """White hair strands with alpha (tinted by the vertex colour in the game): strands of different lengths and leans,
    tapering to their tips, roots at the bottom of the image."""
    rng = random.Random(seed)
    a = np.zeros((h, w), np.float32)
    lum = np.zeros((h, w), np.float32)
    for _ in range(strands):
        x0, lean, ln, l = rng.uniform(1, w - 2), rng.uniform(-6, 6), rng.uniform(0.5, 1.0), rng.uniform(0.7, 1.0)
        for y in range(h):
            v = y / (h - 1)   # 0 at the root (row 0 is the bottom in Blender's pixels)
            if v > ln:
                break
            x = x0 + lean * v * v
            half = 1.6 * (1 - v / ln) + 0.4
            for xi in range(int(x - half - 1), int(x + half + 2)):
                if 0 <= xi < w:
                    al = max(0.0, min(1.0, half - abs(xi - x) + 0.5))
                    if al > a[y, xi]:
                        a[y, xi] = al
                        lum[y, xi] = l
    px = np.zeros((h, w, 4), np.float32)
    px[:, :, 0] = px[:, :, 1] = px[:, :, 2] = np.where(a > 0, lum, 1.0)
    px[:, :, 3] = a
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels = px.ravel()
    img.filepath_raw = os.path.join(PREV, name + ".png")
    img.file_format = "PNG"
    img.save()
    img.pack()
    return img


def hair_material(img):
    """The fur cards' material: the strand image's colour and alpha, alpha-clipped, two-sided."""
    mt = bpy.data.materials.new("Hair")
    mt.use_nodes = True
    nt = mt.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    ti = nt.nodes.new("ShaderNodeTexImage")
    ti.image = img
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(ti.outputs["Color"], mul.inputs[6])
    nt.links.new(vc.outputs["Color"], mul.inputs[7])
    nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
    nt.links.new(ti.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 1.0
    mt.blend_method = "CLIP"
    mt.alpha_threshold = 0.45
    mt.use_backface_culling = False
    return mt


class Cards:
    """Fur cards gathered into one mesh: each a quad from its root along a direction, strands rooted at its base."""

    def __init__(self):
        self.bm = bmesh.new()
        self.col = []

    def card(self, root, direction, across, length, width, root_col, tip_col):
        across = across.normalized() * width * 0.5
        tip = root + direction.normalized() * length
        vs = [self.bm.verts.new(p) for p in (root - across, root + across, tip + across * 0.75, tip - across * 0.75)]
        f = self.bm.faces.new(vs)
        uv = self.bm.loops.layers.uv.verify()
        for lp, t in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            lp[uv].uv = t
        self.col.extend([root_col, root_col, tip_col, tip_col])

    def object(self, name, material):
        ob = obj_from_bmesh(self.bm, name, material)
        me = ob.data
        attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
        cols = np.array([(*c, 1.0) for c in self.col], np.float32)
        attr.data.foreach_set("color", cols.ravel())
        return ob


def surface_points(ob, pick, every):
    """Points on a mesh's surface (with their normals) where pick(co, normal) holds, thinned to about one per `every` m."""
    out = []
    taken = []
    for v in ob.data.vertices:
        if not pick(v.co, v.normal):
            continue
        if any((v.co - t).length < every for t in taken[-60:]):
            continue
        taken.append(v.co.copy())
        out.append((v.co.copy(), v.normal.copy()))
    return out


# ------------------------------------------------------------------ the bake (sized to the animal)

def bake_maps(low, high, prefix, size, extrusion, dist):
    """W.bake_maps with the cage and the ray sized to the animal: the wendigo's 2 cm cage and 5 cm reach, on a squirrel
    five centimetres through, found the far side of the body and the tail, and the coat came out in patches."""
    keep = [s.material for s in high.material_slots]
    W.set_all_materials(high, W.bump_material(prefix + "_bump", 1.0))
    nimg = W.new_image(prefix + "_normal", size, True)
    W.bake(low, high, "NORMAL", nimg, extrusion=extrusion, dist=dist)
    W.set_all_materials(high, W.emit_vc_material(prefix + "_vc"))
    cimg = W.new_image(prefix + "_albedo", size, False)
    W.bake(low, high, "EMIT", cimg, extrusion=extrusion, dist=dist)
    aimg = W.new_image(prefix + "_ao", size, True)
    # (the dense copy lies exactly over the low one: left in, the occlusion rays hit it at once and the coat came out in
    # blotches and "glyphs"; out of the render for this pass)
    high.hide_render = True
    W.bake(low, None, "AO", aimg, samples=64)
    high.hide_render = False
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
    print(f"[animal] baked {prefix}: albedo and normal at {size} px (cage {extrusion}, reach {dist})")
    return cimg, nimg


# ------------------------------------------------------------------ the rig and the clips

def bones_fk(arm_obj):
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "QUATERNION"


def pose(arm_obj, rot, loc=None, frame=None, keyed=None):
    """Sets bones' rotations (name -> list of (axis 'X'/'Y'/'Z', radians) in the bone's own frame, composed in order)
    and the root's location; keys them at frame (every bone in `keyed`, so a clip holds its whole pose)."""
    names = keyed if keyed is not None else [pb.name for pb in arm_obj.pose.bones]
    for n in names:
        pb = arm_obj.pose.bones[n]
        q = Quaternion()
        for ax, ang in rot.get(n, []):
            q = q @ Quaternion(V({"X": (1, 0, 0), "Y": (0, 1, 0), "Z": (0, 0, 1)}[ax]), ang)
        pb.rotation_quaternion = q
        if n == "root":
            pb.location = loc if loc is not None else V((0, 0, 0))
        if frame is not None:
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            if n == "root":
                pb.keyframe_insert("location", frame=frame)


def clip(arm_obj, name, frames, fn, keyed=None, cyclic=True):
    """A clip of `frames` frames (30 fps): fn(t in 0..1) -> (rot, root_loc). Keyed every 2nd frame; cyclic ones end on
    their first pose."""
    act = bpy.data.actions.new(name)
    arm_obj.animation_data.action = act
    step = 2
    for f in list(range(0, frames, step)) + [frames]:
        t = (f / frames) if not (cyclic and f == frames) else 0.0
        rot, loc = fn(t if f < frames else (0.0 if cyclic else 1.0))
        pose(arm_obj, rot, loc, frame=f, keyed=keyed)
    arm_obj.animation_data.action = None
    tr = arm_obj.animation_data.nla_tracks.new()
    tr.name = name
    tr.strips.new(name, 0, act)
    return act
