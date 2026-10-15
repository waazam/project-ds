"""Act 1's park and trail props, the signs, the trailhead's dressing and the lake's signs, modelled in Blender (2026-10-15,
the owner's rule: every model is made in Blender). Run headless:

    blender -b --python tools/Blender/park_props.py [-- name ...]

Each goes to assets/models/park/<name>.glb (a preview in build/blender/park_<name>.png).

Coordinates: every shape here is written in the GAME's frame (Y up, the prop's front toward +Z, the same numbers as the
code that used to build it: ParkProp.cs, SignPost.cs, TrailheadDressing.cs, LakeStructures/LakeDressing), and turned into
Blender's Z-up frame at export (game (x, y, z) -> Blender (x, -z, y)), which the glTF export turns back. So the lettering,
colliders and readables the game still places by those numbers land on these models exactly.

Materials are named "g_<key>": the game paints each with its own material by that key (its textured woods, painted
boards, concrete, weathered block, metal, bark, rock...), so the props keep the game's look; the colours here are only for
the preview. Vertex colours carry the weather (grime toward the foot, streaks under edges, damp at the ground) and the
shading the code used to give each part; the game's materials multiply them in where they use vertex colour.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "park")
os.makedirs(kit.OUT, exist_ok=True)

# game frame -> Blender frame
C = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


# ------------------------------------------------------------------ game-frame helpers


def M(key, color, rough=0.85, metal=0.0):
    return mat("g_" + key, color, rough, metal)


def rot(x=0.0, y=0.0, z=0.0):
    """Godot's Basis.FromEuler (YXZ): Ry * Rx * Rz, in the game frame."""
    return (Matrix.Rotation(y, 4, "Y") @ Matrix.Rotation(x, 4, "X") @ Matrix.Rotation(z, 4, "Z"))


def place(ob, centre=(0, 0, 0), r=None):
    m = Matrix.Translation(Vector(centre))
    if r is not None:
        m = m @ r
    ob.data.transform(m)
    ob.data.update()
    return ob


_n = [0]


def nm(base):
    _n[0] += 1
    return f"{base}{_n[0]}"


def gbox(centre, size, material, bevel=0.008, r=None, segs=2):
    """A box in the game frame, its edges rounded by a real bevel (no razor edges)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    ob = obj_from_bmesh(bm, nm("b"), material, smooth=False)
    if bevel > 0:
        add_mod(ob, "BEVEL", width=min(bevel, min(size) * 0.45), segments=segs, limit_method="ANGLE", harden_normals=False)
        apply_mods(ob)
        for p in ob.data.polygons:
            p.use_smooth = True
    return place(ob, centre, r)


def gcyl(a, b, r0, r1=None, sides=12, material=None, caps=True):
    """A cylinder between two game-frame points."""
    return cylinder(nm("c"), a, b, r0, r1, sides, material, caps)


def gprism(outline, depth, material, centre=(0, 0, 0), r=None, bevel=0.004):
    """A flat shape (an outline in the x-y plane) given its thickness along z: boards, headers, plates."""
    bm = bmesh.new()
    vs = [bm.verts.new((p[0], p[1], -depth * 0.5)) for p in outline]
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in ext["geom"]:
        if isinstance(v, bmesh.types.BMVert):
            v.co.z += depth
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bmesh(bm, nm("p"), material, smooth=False)
    if bevel > 0:
        add_mod(ob, "BEVEL", width=bevel, segments=2, limit_method="ANGLE")
        apply_mods(ob)
        for p in ob.data.polygons:
            p.use_smooth = True
    return place(ob, centre, r)


def glump(centre, radii, material, seed, amp=0.08, freq=2.2, subdiv=3, flat=None):
    return lumpy_sphere(nm("l"), centre, radii, material, seed, amp, freq, subdiv, flat)


def shade(ob, fn):
    """Vertex colours in the game frame: fn(position, normal) -> (r, g, b)."""
    me = ob.data
    if not me.color_attributes:
        me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    ca = me.color_attributes[0]
    for poly in me.polygons:
        for li in poly.loop_indices:
            v = me.vertices[me.loops[li].vertex_index]
            c = fn(v.co, poly.normal)
            ca.data[li].color = (min(1, max(0, c[0])), min(1, max(0, c[1])), min(1, max(0, c[2])), 1.0)


def weather(ob, base=(0.9, 0.88, 0.85), foot=0.0, top=2.0, grime=0.25, seed=0):
    """Grime gathering toward the foot, streaks running down, a little mottling: the weather the code laid on as decals."""
    def fn(p, n):
        h = max(0.0, min(1.0, (p.y - foot) / max(top - foot, 0.01)))
        streak = 0.5 + 0.5 * fbm(p.x * 14 + seed, p.z * 3 + seed, 0, 2)
        mott = fbm(p.x * 5 + seed, p.y * 5, p.z * 5, 3)
        k = 1.0 - grime * (1.0 - h) ** 1.6 - 0.08 * streak * (1.0 - h * 0.5) + 0.06 * mott
        return (base[0] * k, base[1] * k * 0.99, base[2] * k * 0.96)
    shade(ob, fn)
    return ob


def flat_shade(ob, c):
    shade(ob, lambda p, n: c)
    return ob


def finish(objs, name, uv=1.0, prev=(0, 1, 0), dist=4.0, h=1.2, yaw=35):
    """UVs (box-projected, by the metre), into Blender's frame, joined by material (one mesh per game material, named for
    it), exported."""
    objs = [o for o in objs if o is not None]
    for o in objs:
        if not o.data.color_attributes:
            flat_shade(o, (1, 1, 1))
        o.data.transform(C)
        o.data.update()
        uv_box(o, uv)
    by = {}
    for o in objs:
        key = o.data.materials[0].name if o.data.materials else "g_none"
        by.setdefault(key, []).append(o)
    out = []
    for key, group in by.items():
        j = join(group, key) if len(group) > 1 else group[0]
        j.name = key
        out.append(j)
    export(out, name)
    pv = Vector(prev)
    preview("park_" + name, (pv.x, -pv.z, pv.y), dist, height=h, yaw=yaw)
    return out


def post_cap(p, height, w, material, cap=None):
    """SignKit.Post: a square post with a pyramid cap."""
    cap = w * 0.45 if cap is None else cap
    out = [gbox((p[0], p[1] + height * 0.5, p[2]), (w, height, w), material, 0.012)]
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=4, radius1=w * 0.5 * math.sqrt(2), radius2=0.002, depth=cap)
    ob = obj_from_bmesh(bm, nm("cap"), material, smooth=False)
    # the cone is Z-up in its own frame: stand it on the game's Y, its corners square to the post
    place(ob, (p[0], p[1] + height + cap * 0.5, p[2]), rot(x=-math.pi / 2) @ Matrix.Rotation(math.pi / 4, 4, "Z"))
    out.append(ob)
    return out


def flat_post(p, height, w, material):
    """SignKit.FlatPost: a square post, its top chamfered."""
    return [gbox((p[0], p[1] + height * 0.5, p[2]), (w, height, w), material, 0.012),
            gbox((p[0], p[1] + height + 0.01, p[2]), (w - 0.03, 0.02, w - 0.03), material, 0.006)]


def header(c, span, overhang, h, d, material):
    """SignKit.Header: a beam across the posts' tops, its tails cut away underneath in a curve."""
    inner = span * 0.5
    pts = []
    # the top edge, end to end, then back along the underside: full depth between the posts, curving up under each tail
    pts.append((-inner - overhang, h * 0.5))
    pts.append((inner + overhang, h * 0.5))
    for i in range(10, -1, -1):
        u = i / 10.0
        keep = 1.0 - 0.55 * (u ** 1.4)
        pts.append((inner + overhang * u, h * 0.5 - h * keep))
    for i in range(0, 11):
        u = i / 10.0
        keep = 1.0 - 0.55 * (u ** 1.4)
        pts.append((-inner - overhang * u, h * 0.5 - h * keep))
    pts = pts[:2] + pts[2:]
    # reorder to a simple loop: top-left, top-right, down the right tail, along the bottom, up the left tail
    right = [(inner + overhang * (i / 10.0), h * 0.5 - h * (1.0 - 0.55 * (i / 10.0) ** 1.4)) for i in range(10, -1, -1)]
    left = [(-inner - overhang * (i / 10.0), h * 0.5 - h * (1.0 - 0.55 * (i / 10.0) ** 1.4)) for i in range(0, 11)]
    loop = [(-inner - overhang, h * 0.5), (inner + overhang, h * 0.5)] + right + left
    # drop the duplicate points where the tails meet the straight top
    clean = []
    for p in loop:
        if not clean or (abs(clean[-1][0] - p[0]) > 1e-5 or abs(clean[-1][1] - p[1]) > 1e-5):
            clean.append(p)
    return gprism(clean, d, material, c)


def arrow_board(c, len_, h, t, dir_, material, r=None):
    """SignKit.ArrowBoard: a plank, square or pointed at one end."""
    hl, hh = len_ * 0.5, h * 0.5
    tip = 0 if dir_ == 0 else min(h * 0.55, len_ * 0.3)
    if dir_ > 0:
        o = [(-hl, -hh), (hl - tip, -hh), (hl, 0), (hl - tip, hh), (-hl, hh)]
    elif dir_ < 0:
        o = [(-hl, 0), (-hl + tip, -hh), (hl, -hh), (hl, hh), (-hl + tip, hh)]
    else:
        o = [(-hl, -hh), (hl, -hh), (hl, hh), (-hl, hh)]
    return gprism(o, t, material, c, r, bevel=0.006)


def routed_arrow(c, len_, h, dir_, material, depth=0.004, r=None):
    """SignKit.RoutedArrow, given a little depth: a shaft and an open chevron head, filled with paint."""
    if dir_ == 0:
        return []
    s = dir_
    head, shaft = h * 0.9, h * 0.2
    x0, xh, x1 = -len_ * 0.5, len_ * 0.5 - head * 0.8, len_ * 0.5
    parts = [gprism([(x0 * s, -shaft * 0.5), (xh * s, -shaft * 0.5), (xh * s, shaft * 0.5), (x0 * s, shaft * 0.5)][:: s], depth, material, bevel=0)]
    w = shaft * 0.9
    for side in (-1, 1):
        a0 = Vector((x1 * s, 0)); a1 = Vector((xh * s, side * head * 0.5))
        d = (a1 - a0).normalized(); perp = Vector((-d.y, d.x)) * w * 0.5
        quad = [a0 - perp, a1 - perp, a1 + perp, a0 + perp]
        area = sum(quad[i].x * quad[(i + 1) % 4].y - quad[(i + 1) % 4].x * quad[i].y for i in range(4))
        if area < 0:
            quad = quad[::-1]
        parts.append(gprism([(q.x, q.y) for q in quad], depth, material, bevel=0))
    m = Matrix.Translation(Vector(c))
    if r is not None:
        m = m @ r
    for p in parts:
        p.data.transform(m)
    return parts


# ------------------------------------------------------------------ the trailhead sign


def trailhead_sign():
    clear()
    post, brown, green = M("signpost", (0.25, 0.18, 0.12)), M("signbrown", (0.25, 0.18, 0.12)), M("signgreen", (0.08, 0.17, 0.12))
    out = []
    pw, ph_, bw, pt = 0.2, 2.35, 2.2, 0.06
    for x in (-1.02, 1.02):
        out += flat_post((x, -0.4, 0), ph_ + 0.4, pw, post)
    y = 2.1
    z = pw * 0.5 + pt * 0.5 + 0.002
    for i, h in enumerate((0.36, 0.3, 0.3)):
        cy = y - h * 0.5
        out.append(gbox((0, cy, z), (bw, h - 0.014, pt), green if i == 0 else brown, 0.006))
        y -= h
    out.append(header((0, ph_ + 0.09, 0.02), 2.04 + pw, 0.38, 0.18, pw + 0.06, post))
    for o in out:
        weather(o, foot=-0.4, top=2.6, grime=0.3, seed=3)
    finish(out, "trailhead_sign", prev=(0, 1.3, 0), dist=5)


# ------------------------------------------------------------------ the info kiosk


def info_kiosk():
    clear()
    post, plank = M("post", (0.42, 0.33, 0.24)), M("plank", (0.45, 0.36, 0.26))
    roof, mapm, paper = M("kiosk_roof", (0.1, 0.09, 0.08)), M("map", (0.7, 0.68, 0.55)), M("paper", (0.82, 0.8, 0.72))
    out = []
    for x in (-0.84, 0.84):
        out.append(gbox((x, 1.05, 0), (0.16, 2.9, 0.16), post, 0.012))
    for i in range(5):
        o = gbox((0, 1.06 + i * 0.22, 0), (1.52, 0.21, 0.04), plank, 0.006)
        s = 0.82 + 0.14 * abs(math.sin(i * 3.7 + 1))
        weather(o, (s, s * 0.98, s * 0.95), foot=0.9, top=2.2, grime=0.2, seed=i)
        out.append(o)
    out.append(gbox((0, 2.07, 0.035), (1.6, 0.07, 0.05), post, 0.008))
    out.append(gbox((0, 0.93, 0.035), (1.6, 0.07, 0.05), post, 0.008))
    out.append(gbox((0, 2.25, 0.06), (1.6, 0.24, 0.05), plank, 0.008))
    peak, eave = 2.8, 2.48
    tl = Vector((0, peak - eave, 0.55))
    ang = math.atan2(tl.y, tl.z)
    for s in (1, -1):
        slab = gbox((0, (peak + eave) * 0.5, 0.28 * s), (2.0, 0.05, tl.length + 0.05), roof, 0.01, rot(x=ang * s))
        out.append(slab)
    out.append(gbox((0, peak + 0.02, 0), (2.04, 0.06, 0.08), roof, 0.01))   # the ridge
    out.append(gbox((0, 2.44, 0), (1.84, 0.1, 0.12), post, 0.01))
    fz = 0.021
    out.append(gbox((-0.35, 1.5, fz), (0.66, 0.92, 0.002), mapm, 0))
    out.append(gbox((0.35, 1.785, fz), (0.5, 0.33, 0.002), paper, 0))
    for o in out:
        if not o.data.color_attributes:
            weather(o, foot=-0.5, top=2.8, grime=0.28, seed=5)
    finish(out, "info_kiosk", prev=(0, 1.4, 0), dist=4.5)


# ------------------------------------------------------------------ the bin


def trash_can():
    clear()
    metal = M("metal", (0.38, 0.38, 0.36), 0.6, 0.4)
    out = [gbox((0, 0.52, 0), (0.75, 0.96, 0.6), metal, 0.03, segs=3)]
    out.append(flat_shade(gbox((0, 0.03, 0), (0.7, 0.06, 0.55), metal, 0.01), (0.62, 0.62, 0.6)))
    out.append(flat_shade(gbox((0, 0.16, 0.301), (0.6, 0.18, 0.012), metal, 0.004), (0.62, 0.62, 0.6)))
    out.append(gbox((0, 1.03, 0.005), (0.8, 0.05, 0.66), metal, 0.018, rot(x=math.radians(4))))
    out.append(gbox((0, 0.99, 0.33), (0.8, 0.06, 0.02), metal, 0.006))
    out.append(flat_shade(gcyl((-0.36, 1.005, -0.33), (0.36, 1.005, -0.33), 0.014, 0.014, 10, metal), (0.4, 0.4, 0.4)))
    out.append(flat_shade(gbox((0, 0.945, 0.322), (0.4, 0.035, 0.035), metal, 0.008), (0.4, 0.4, 0.4)))
    out.append(flat_shade(gbox((0, 0.935, 0.345), (0.12, 0.02, 0.03), metal, 0.006), (0.4, 0.4, 0.4)))
    for o in out[:1] + out[3:5]:
        weather(o, foot=0, top=1.1, grime=0.35, seed=7)
    finish(out, "trash_can", prev=(0, 0.6, 0), dist=2.4)


# ------------------------------------------------------------------ the vault toilet


def vault_toilet():
    clear()
    hw, hd, eave, ridge, ov = 1.15, 1.2, 2.45, 3.05, 0.32
    pad, block, conc = M("pad", (0.44, 0.43, 0.4)), M("block", (0.4, 0.33, 0.26)), M("concrete", (0.34, 0.33, 0.3))
    shingle, brown, along = M("shingle", (0.25, 0.22, 0.18)), M("brown", (0.3, 0.2, 0.13)), M("brown_along", (0.3, 0.2, 0.13))
    steel, iron, pipe = M("steel", (0.5, 0.38, 0.3), 0.7, 0.3), M("iron", (0.3, 0.3, 0.3), 0.6, 0.5), M("vent_pipe", (0.08, 0.08, 0.08), 0.6)
    dark, paper = M("dark", (0.04, 0.04, 0.035)), M("paper", (0.9, 0.88, 0.8))
    out = []
    out.append(flat_shade(gbox((0, -0.26, 0.25), (hw * 2 + 0.5, 0.58, hd * 2 + 1.0), pad, 0.03), (0.44, 0.43, 0.4)))
    out.append(gbox((0, (eave + 0.03) * 0.5, 0), (hw * 2, eave - 0.03, hd * 2), block, 0.02))
    out.append(flat_shade(gbox((0, 0.12, 0), (hw * 2 + 0.04, 0.24, hd * 2 + 0.04), conc, 0.015), (0.34, 0.33, 0.3)))
    out.append(flat_shade(gbox((0, eave - 0.06, 0), (hw * 2 + 0.05, 0.12, hd * 2 + 0.05), conc, 0.015), (0.42, 0.4, 0.36)))
    # the roof: two shingled slopes with real thickness, fascia, rake boards, a ridge cap
    rx, rz = hw + ov, hd + ov + 0.05
    ex = eave - ov * (ridge - eave) / hw
    slope = math.hypot(rx, ridge - ex)
    ang = math.atan2(ridge - ex, rx)
    for s in (-1, 1):
        cx = s * rx * 0.5
        cy = (ridge + ex) * 0.5 + 0.05
        out.append(gbox((cx, cy, 0), (slope + 0.02, 0.09, rz * 2), shingle, 0.015, rot(z=-s * ang)))
        out.append(gbox((s * rx + s * 0.015, ex + 0.02, 0), (0.035, 0.2, rz * 2 + 0.03), along, 0.008, rot(z=-s * ang)))
        for g in (-1, 1):
            out.append(gbox((cx, cy + 0.02, g * (rz + 0.015)), (slope + 0.03, 0.2, 0.035), along, 0.008, rot(z=-s * ang)))
    out.append(gbox((0, ridge + 0.1, 0), (0.22, 0.12, rz * 2 + 0.04), shingle, 0.03))
    # the gables: upright brown boards in a triangle
    for g in (-1, 1):
        tri = [(-hw - 0.02, 0), (hw + 0.02, 0), (0, ridge - 0.03 - eave)]
        out.append(gprism(tri, 0.02, brown, (0, eave, g * (hd + 0.01))))
    # the door in its steel frame, the louvre, the kick plate, the lever, the hinges
    dx, dw, dh, fz = 0.15, 0.92, 2.04, hd
    for s in (-1, 1):
        out.append(gbox((dx + s * (dw * 0.5 + 0.03), dh * 0.5 + 0.02, fz + 0.015), (0.06, dh + 0.04, 0.05), steel, 0.006))
    out.append(gbox((dx, dh + 0.05, fz + 0.015), (dw + 0.12, 0.06, 0.05), steel, 0.006))
    leaf = gbox((dx, dh * 0.5 + 0.02, fz + 0.005), (dw, dh, 0.045), steel, 0.008)
    weather(leaf, (0.56, 0.4, 0.3), foot=0, top=2.1, grime=0.35, seed=11)
    out.append(leaf)
    out.append(gbox((dx, 0.42, fz + 0.029), (0.5, 0.3, 0.004), dark, 0))
    for i in range(6):
        out.append(gbox((dx, 0.3 + i * 0.048, fz + 0.04), (0.5, 0.04, 0.006), steel, 0.002, rot(x=-0.6)))
    out.append(flat_shade(gbox((dx, 0.13, fz + 0.03), (dw - 0.04, 0.2, 0.004), steel, 0.002), (0.62, 0.6, 0.58)))
    lx = dx - dw * 0.5 + 0.1
    out.append(gcyl((lx, 1.0, fz + 0.027), (lx, 1.0, fz + 0.04), 0.032, 0.032, 14, iron))
    out.append(gcyl((lx, 1.0, fz + 0.04), (lx, 1.0, fz + 0.08), 0.01, 0.01, 8, iron))
    out.append(gcyl((lx, 1.0, fz + 0.075), (lx + 0.12, 0.99, fz + 0.075), 0.011, 0.009, 8, iron))
    out.append(gcyl((lx, 1.2, fz + 0.027), (lx, 1.2, fz + 0.04), 0.026, 0.026, 14, iron))
    for y in (0.3, 1.0, 1.75):
        out.append(gcyl((dx + dw * 0.5 + 0.005, y - 0.06, fz + 0.035), (dx + dw * 0.5 + 0.005, y + 0.06, fz + 0.035), 0.012, 0.012, 10, iron))
    for s in (-1, 1):
        out.append(gbox((s * (hw + 0.01), 2.1, 0), (0.03, 0.26, 0.6), steel, 0.004))
        out.append(gbox((s * (hw + 0.027), 2.1, 0), (0.004, 0.18, 0.5), dark, 0))
    # the vault's vent stack, its cap and brackets
    vp = Vector((-0.7, 0, -hd - 0.12))
    out.append(gcyl(vp + Vector((0, 0.2, 0)), vp + Vector((0, 3.7, 0)), 0.1, 0.1, 16, pipe, caps=False))
    out.append(gcyl(vp + Vector((0, 3.7, 0)), vp + Vector((0, 3.8, 0)), 0.19, 0.04, 16, pipe))
    for y in (0.9, 2.0):
        out.append(gbox(tuple(vp + Vector((0, y, 0.06))), (0.24, 0.04, 0.08), iron, 0.006))
    # the taped paper on the door, the gable's sign board
    out.append(gbox((dx, 1.57, fz + 0.0285), (0.4, 0.29, 0.002), paper, 0, rot(z=0.025)))
    out.append(gbox((0, eave + 0.2, hd + 0.04), (0.9, 0.2, 0.03), along, 0.006))
    for o in out:
        if not o.data.color_attributes:
            weather(o, foot=0, top=3, grime=0.3, seed=13)
    finish(out, "vault_toilet", prev=(0, 1.5, 0), dist=7, h=1.5)


# ------------------------------------------------------------------ small things


def bumper():
    clear()
    conc = M("concrete", (0.45, 0.44, 0.41))
    sec = [(-0.1, -0.02), (0.1, -0.02), (0.1, 0.06), (0.05, 0.125), (-0.05, 0.125), (-0.1, 0.06)]
    body = gprism([(-s[0], s[1]) for s in sec][::-1], 1.72, conc, bevel=0.01)
    body.data.transform(rot(y=math.pi / 2))
    for s in (-1, 1):
        cut = [(p[0] * 0.8, p[1] * 0.92 - 0.0016) for p in sec]
        e = gprism([(-c[0], c[1]) for c in cut][::-1], 0.04, conc, (s * 0.88, 0, 0), rot(y=math.pi / 2), bevel=0.008)
    objs = [o for o in bpy.data.objects if o.type == "MESH"]
    for o in objs:
        weather(o, (0.9, 0.9, 0.88), foot=-0.02, top=0.13, grime=0.25, seed=17)
    finish(objs, "bumper", prev=(0, 0.05, 0), dist=2.2, h=0.6)


def trail_marker():
    clear()
    post, blaze, routed = M("post", (0.42, 0.33, 0.24)), M("blaze", (0.11, 0.14, 0.15)), M("routed", (0.85, 0.83, 0.78))
    w, h = 0.15, 1.15
    out = post_cap((0, -0.3, 0), h + 0.3, w, post)
    out.append(gbox((0, h - 0.1, 0), (w + 0.004, 0.06, w + 0.004), blaze, 0.002))
    for side in (1, -1):
        r = rot(y=0 if side > 0 else math.pi) @ rot(z=math.pi / 2)
        out += routed_arrow((0, 0.6, side * (w * 0.5 + 0.002)), 0.1, 0.05, 1, routed, r=r)
    for o in out:
        if not o.data.color_attributes:
            weather(o, foot=-0.3, top=1.2, grime=0.35, seed=19)
    finish(out, "trail_marker", prev=(0, 0.7, 0), dist=2.2)


def backpack():
    clear()
    nylon, dark, pad = M("pack_red", (0.36, 0.14, 0.1)), M("pack_dark", (0.12, 0.11, 0.1)), M("pack_pad", (0.3, 0.32, 0.2))
    r = rot(y=0.4, z=0.12)
    body = glump((0, 0.11, -0.01), (0.175, 0.115, 0.27), nylon, 3, amp=0.05, subdiv=4, flat=0.012)
    pocket = glump((0, 0.19, 0.08), (0.12, 0.05, 0.11), nylon, 4, amp=0.04, subdiv=3)
    lid = glump((0, 0.2, -0.22), (0.16, 0.04, 0.09), nylon, 5, amp=0.04, subdiv=3)
    parts = [body, pocket, lid]
    parts.append(gbox((-0.1, 0.232, -0.02), (0.04, 0.012, 0.5), dark, 0.003))
    parts.append(gbox((0.1, 0.232, -0.02), (0.04, 0.012, 0.5), dark, 0.003))
    parts.append(gbox((0.24, 0.03, 0.05), (0.06, 0.02, 0.4), dark, 0.004, rot(y=0.3)))
    parts.append(gcyl((-0.22, 0.08, 0.33), (0.22, 0.08, 0.33), 0.075, 0.075, 16, pad))
    for p in parts:
        p.data.transform(r)
        weather(p, foot=0, top=0.25, grime=0.3, seed=23)
    finish(parts, "backpack", prev=(0, 0.1, 0), dist=1.4, h=0.6)


def boot():
    clear()
    leather, sole = M("boot_leather", (0.24, 0.15, 0.09)), M("boot_sole", (0.07, 0.07, 0.07))
    collar, lace = M("boot_collar", (0.16, 0.1, 0.06)), M("boot_lace", (0.45, 0.4, 0.3))
    # upright (toe toward -Z), then laid on its side
    parts = [glump((0, 0.018, -0.005), (0.05, 0.02, 0.15), sole, 31, amp=0.02, subdiv=3)]
    parts.append(glump((0, 0.075, -0.03), (0.05, 0.05, 0.13), leather, 32, amp=0.03, subdiv=4, flat=0.02))
    parts.append(gcyl((0, 0.1, 0.06), (0, 0.2, 0.075), 0.05, 0.046, 16, leather, caps=False))
    t = bpy.data.objects.new(nm("t"), bpy.data.meshes.new(nm("tm")))
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=16, radius1=0.05, radius2=0.05, depth=0.025)
    col = obj_from_bmesh(bm, nm("col"), collar)
    place(col, (0, 0.2, 0.075), rot(x=-math.pi / 2 + 0.12))
    parts.append(col)
    for i in range(5):
        z = -0.06 + i * 0.035
        y = 0.104 + i * 0.018 + (0.02 if i > 2 else 0)
        parts.append(gcyl((-0.022, y, z), (0.022, y + 0.002, z + 0.012), 0.0035, 0.0035, 6, lace, caps=False))
    r = Matrix.Translation(Vector((0, 0.05, 0))) @ rot(y=0.9, z=1.35)
    for p in parts:
        p.data.transform(r)
        weather(p, foot=0, top=0.2, grime=0.25, seed=29)
    finish(parts, "boot", prev=(0, 0.06, 0), dist=0.9, h=0.4)


def walking_stick():
    clear()
    wood, pale = M("stick_wood", (0.5, 0.4, 0.28)), M("stick_break", (0.72, 0.64, 0.5))
    tip, strap = M("stick_tip", (0.06, 0.06, 0.06)), M("stick_strap", (0.18, 0.2, 0.26))
    a0, a1 = Vector((-0.7, 0.018, 0.05)), Vector((0.02, 0.02, -0.02))
    b0, b1 = Vector((0.1, 0.02, 0.06)), Vector((0.62, 0.018, 0.32))
    parts = [gcyl(a0, a1, 0.016, 0.016, 10, wood), gcyl(b0, b1, 0.016, 0.015, 10, wood)]
    parts.append(gcyl(a1, a1 + (a1 - a0).normalized() * 0.07, 0.014, 0.002, 7, pale))
    parts.append(gcyl(b0, b0 - (b1 - b0).normalized() * 0.06, 0.014, 0.002, 7, pale))
    parts.append(gcyl(b1, b1 + (b1 - b0).normalized() * 0.05, 0.018, 0.018, 10, tip))
    parts.append(gbox((-0.66, 0.01, 0.13), (0.03, 0.006, 0.16), strap, 0.002))
    finish(parts, "walking_stick", prev=(0, 0.02, 0), dist=1.8, h=0.6)


def log_mesh(a, b, r0, r1, material, seed, sides=14):
    """A rough log: bark furrowed, not a smooth tube."""
    ob = gcyl(a, b, r0, r1, sides, material, caps=False)
    add_mod(ob, "SUBSURF", levels=1)
    apply_mods(ob)
    me = ob.data
    ax = (Vector(b) - Vector(a)).normalized()
    for v in me.vertices:
        p = v.co
        n = fbm(p.x * 6 + seed, p.y * 6, p.z * 6, 3)
        rad = (p - Vector(a)) - ax * (p - Vector(a)).dot(ax)
        if rad.length > 1e-6:
            v.co = p + rad.normalized() * n * 0.03
    for p in me.polygons:
        p.use_smooth = True
    return ob


def cut_log():
    clear()
    bark, end, rock = M("bark", (0.32, 0.25, 0.18)), M("endgrain", (0.6, 0.5, 0.36)), M("rock", (0.4, 0.37, 0.33))
    r, y, gap, xl, xr = 0.36, 0.27, 1.3, -7.0, 5.2
    parts = [log_mesh((xl, y + 0.05, 0.3), (-gap, y, 0), r * 1.08, r, bark, 1)]
    parts.append(log_mesh((gap, y, 0), (xr, y - 0.03, -0.25), r, r * 0.85, bark, 2))
    dl = (Vector((-gap, y, 0)) - Vector((xl, y + 0.05, 0.3))).normalized()
    dr = (Vector((xr, y - 0.03, -0.25)) - Vector((gap, y, 0))).normalized()
    parts.append(gcyl(Vector((-gap, y, 0)) - dl * 0.01, (-gap, y, 0), r - 0.005, r - 0.005, 14, end))
    parts.append(gcyl((gap, y, 0), Vector((gap, y, 0)) + dr * 0.01, r - 0.005, r - 0.005, 14, end))
    root = glump((xl - 0.3, 0.55, 0.35), (0.35, 0.95, 1.0), rock, 3, amp=0.12, subdiv=3)
    flat_shade(root, (0.55, 0.45, 0.35))
    parts.append(root)
    parts.append(gcyl((xr, y - 0.03, -0.25), (xr + 0.5, y - 0.05, -0.3), r * 0.85, 0.05, 12, bark))
    parts.append(gcyl((-3.5, y + 0.2, 0.12), (-3.3, y + 0.9, 0.5), 0.06, 0.02, 8, bark))
    parts.append(gcyl((3.2, y + 0.1, -0.12), (3.6, y + 0.25, -0.9), 0.05, 0.015, 8, bark))
    for p in parts:
        if not p.data.color_attributes:
            weather(p, (0.85, 0.8, 0.75), foot=0, top=0.7, grime=0.3, seed=37)
    finish(parts, "cut_log", prev=(0, 0.3, 0), dist=8, h=2)


def boulder(v):
    clear()
    rock = M("rock", (0.42, 0.4, 0.37))
    radii = [(1.05, 0.8, 0.9), (1.15, 0.7, 0.85), (0.95, 0.85, 1.0)][v]
    ob = glump((0, 0, 0), radii, rock, 11 + v * 7, amp=0.16, freq=1.6, subdiv=4, flat=-0.45)
    add_mod(ob, "DECIMATE", ratio=0.45)
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    def fn(p, n):
        moss = max(0.0, n.y) * (0.5 + 0.5 * fbm(p.x * 3, p.y * 3, p.z * 3, 3))
        k = 0.9 + 0.12 * fbm(p.x * 4 + v, p.y * 4, p.z * 4, 3) - 0.25 * max(0.0, -p.y)
        return (k * (1 - 0.25 * moss), k * (1 - 0.05 * moss), k * (1 - 0.3 * moss) * 0.97)
    shade(ob, fn)
    finish([ob], f"boulder_{v}", prev=(0, 0.3, 0), dist=3.5)


def torn_map():
    clear()
    mapm = M("map", (0.75, 0.72, 0.6))
    bm = bmesh.new()
    nx, nz = 8, 10
    vs = {}
    for i in range(nx + 1):
        for j in range(nz + 1):
            x = -0.21 + 0.42 * i / nx
            z = -0.29 + 0.58 * j / nz
            curl = max(0.0, (-z - 0.09) / 0.2)
            y = 0.01 + 0.06 * curl ** 2 * (1 - (x + 0.21) / 0.42) + 0.006 * math.sin(i * 1.7 + j)
            if z < -0.24 and x > 0.1:      # the torn corner
                continue
            vs[(i, j)] = bm.verts.new((x, y, z))
    for i in range(nx):
        for j in range(nz):
            q = [vs.get((i, j)), vs.get((i + 1, j)), vs.get((i + 1, j + 1)), vs.get((i, j + 1))]
            if all(q):
                bm.faces.new(q)
    ob = obj_from_bmesh(bm, nm("map"), mapm)
    add_mod(ob, "SOLIDIFY", thickness=0.002)
    apply_mods(ob)
    ob.data.transform(rot(y=0.5))
    # its own UVs: the map texture laid flat over the sheet
    me = ob.data
    uvl = me.uv_layers.new(name="UV")
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl.data[li].uv = ((co.x + 0.3) / 0.6, (co.z + 0.3) / 0.6)
    weather(ob, (0.88, 0.86, 0.78), foot=0, top=0.08, grime=0.2, seed=41)
    ob.data.transform(C)
    export([ob], "torn_map")
    preview("park_torn_map", (0, 0, 0.02), 0.9, height=0.5)


def tent():
    clear()
    canvas, pole = M("canvas", (0.33, 0.35, 0.28)), M("tent_pole", (0.35, 0.33, 0.3))
    rope, stake, ground = M("tent_rope", (0.52, 0.5, 0.42)), M("tent_stake", (0.3, 0.28, 0.25)), M("ground_sheet", (0.18, 0.19, 0.16))
    L, hw, rh = 2.0, 0.72, 1.0
    x0, x1 = -L * 0.5, L * 0.5

    def ridge(u):
        return rh - 0.32 * (3 * u * u - 2 * u ** 3 if u > 0.55 else 0) * 1.0 - 0.05 * math.sin(math.pi * u)

    def cloth(side, u, v):
        x = x0 + (x1 - x0) * u
        z = side * hw * v * (1 - 0.08 * max(0, (u - 0.6) / 0.4))
        y = ridge(u) + (0.03 - ridge(u)) * v
        sag = (0.07 + 0.08 * u) * math.sin(math.pi * u) * math.sin(math.pi * min(1, v * 1.05))
        n = Vector((0, hw, side * (ridge(u) - 0.03))).normalized()
        wr = 0.012 * fbm(x * 6, z * 6, v * 3, 2)       # wrinkles
        return Vector((x, y, z)) - n * (sag + wr)

    parts = []
    for side in (-1, 1):
        bm = bmesh.new()
        nu, nv = 24, 10
        grid = [[bm.verts.new(cloth(side, i / nu, j / nv)) for j in range(nv + 1)] for i in range(nu + 1)]
        for i in range(nu):
            for j in range(nv):
                bm.faces.new([grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]])
        ob = obj_from_bmesh(bm, nm("panel"), canvas)
        add_mod(ob, "SOLIDIFY", thickness=0.006)
        apply_mods(ob)
        def fn(p, n, s=side):
            k = (1.0 if s > 0 else 0.82) * (1 - 0.25 * (1 - min(1, p.y / 1.0)))
            return (k, k, k * 0.97)
        shade(ob, fn)
        parts.append(ob)
    # the rear panel, the door flaps, the rolled flap
    bm = bmesh.new()
    rt, rl, rr = cloth(1, 1, 0) + Vector((0.05, 0, 0)), cloth(-1, 1, 1), cloth(1, 1, 1)
    bm.faces.new([bm.verts.new(rl), bm.verts.new(rr), bm.verts.new(rt)])
    ob = obj_from_bmesh(bm, nm("rear"), canvas)
    add_mod(ob, "SOLIDIFY", thickness=0.006); apply_mods(ob)
    flat_shade(ob, (0.72, 0.72, 0.7)); parts.append(ob)
    ft, fl, fr = cloth(1, 0, 0), cloth(-1, 0, 1), cloth(1, 0, 1)
    fm = Vector((x0 - 0.02, 0.03, -0.05))
    bm = bmesh.new()
    bm.faces.new([bm.verts.new(fl), bm.verts.new(fm), bm.verts.new(ft)])
    ob = obj_from_bmesh(bm, nm("flap"), canvas)
    add_mod(ob, "SOLIDIFY", thickness=0.006); apply_mods(ob)
    flat_shade(ob, (0.78, 0.78, 0.76)); parts.append(ob)
    roll = gcyl(ft + Vector((-0.01, -0.05, 0.05)), fr + Vector((-0.01, 0.08, -0.08)), 0.045, 0.06, 12, canvas)
    flat_shade(roll, (0.7, 0.7, 0.68)); parts.append(roll)
    parts.append(flat_shade(gbox((-0.05, 0.012, 0), (L + 0.15, 0.006, hw * 1.6), ground, 0.003), (0.35, 0.36, 0.3)))
    parts.append(gcyl((x0, 0, 0), (x0, ridge(0) + 0.06, 0), 0.016, 0.014, 8, pole))
    parts.append(gcyl((x1 - 0.1, 0, 0.02), (x1 + 0.05, ridge(1) + 0.04, 0.1), 0.016, 0.014, 8, pole))

    def guy(frm, to, slack):
        frm, s = Vector(frm), Vector((to[0], 0, to[2]))
        if slack:
            mid = (frm + s) * 0.5
            mid.y = max(s.y, (frm.y + s.y) * 0.5 - 0.35)
            parts.append(gcyl(frm, mid, 0.005, 0.005, 5, rope, caps=False))
            parts.append(gcyl(mid, s + Vector((0, 0.02, 0)), 0.005, 0.005, 5, rope, caps=False))
        else:
            parts.append(gcyl(frm, s + Vector((0, 0.08, 0)), 0.005, 0.005, 5, rope, caps=False))
        parts.append(gbox(tuple(s + Vector((0, 0.05, 0))), (0.025, 0.12, 0.025), stake, 0.004, rot(z=0.3)))

    guy((x0, ridge(0) + 0.04, 0), (x0 - 0.95, 0, 0.05), False)
    guy((x1 + 0.05, ridge(1) + 0.02, 0.1), (x1 + 0.9, 0, 0.2), True)
    guy(cloth(-1, 0.02, 0.95), (x0 - 0.35, 0, -hw - 0.55), False)
    guy(cloth(1, 0.02, 0.95), (x0 - 0.35, 0, hw + 0.55), False)
    guy(cloth(-1, 0.98, 0.95), (x1 + 0.35, 0, -hw - 0.55), False)
    guy(cloth(1, 0.98, 0.95), (x1 + 0.4, 0, hw + 0.5), True)
    finish(parts, "tent", prev=(0, 0.5, 0), dist=4.2, h=1.4)


def holed_stone():
    clear()
    rock = M("rock", (0.5, 0.49, 0.46))
    height, sink, holeY = 1.15, 0.25, 0.62
    # a thick worn slab with a hole through it: a voxel-remeshed boolean of a lumpy slab and a cylinder
    slab = glump((0, 0.5, 0), (0.42, 0.72, 0.14), rock, 51, amp=0.08, freq=2.0, subdiv=4)
    hole = gcyl((0, holeY, -0.4), (0, holeY, 0.4), 0.17, 0.17, 24, rock)
    hole.data.transform(Matrix.Translation((0, holeY, 0)) @ Matrix.Scale(1.0, 4) @ Matrix.Translation((0, -holeY, 0)))
    for v in hole.data.vertices:
        v.co.y = holeY + (v.co.y - holeY) * 1.3
    b = slab.modifiers.new("hole", "BOOLEAN")
    b.object = hole
    b.operation = "DIFFERENCE"
    apply_mods(slab)
    bpy.data.objects.remove(hole)
    add_mod(slab, "REMESH", mode="VOXEL", voxel_size=0.018)
    add_mod(slab, "SMOOTH", factor=0.6, iterations=4)
    apply_mods(slab)
    for p in slab.data.polygons:
        p.use_smooth = True
    for v in slab.data.vertices:
        if v.co.y < 0:
            v.co.y = v.co.y * 0.4 - 0.0
    slab.data.transform(Matrix.Translation((0, -sink * 0.5, 0)))
    slab.data.transform(rot(x=math.radians(-4), z=math.radians(8.5)))
    add_mod(slab, "DECIMATE", ratio=0.4)
    apply_mods(slab)
    def fn(p, n):
        g = 0.75 + 0.12 * max(0, min(1, p.y / height)) + 0.08 * fbm(p.x * 6, p.y * 6, p.z * 6, 3)
        lichen = max(0, fbm(p.x * 3 + 4, p.y * 3, p.z * 3, 3))
        return (g * (1 + 0.1 * lichen), g * (1 + 0.12 * lichen) * 0.99, g * 0.93)
    shade(slab, fn)
    finish([slab], "holed_stone", prev=(0, 0.6, 0), dist=2.6)


def pebble(v):
    clear()
    rock = M("rock", (0.45, 0.44, 0.41))
    ob = glump((0, 0, 0), (1.3, 0.6, 1.0), rock, 61 + v, amp=0.12, subdiv=3, flat=-0.3)
    flat_shade(ob, (0.8, 0.8, 0.77))
    finish([ob], f"pebble_{v}", prev=(0, 0, 0), dist=4)


def trail_register():
    clear()
    post, plank, pencil = M("post", (0.42, 0.33, 0.24)), M("plank", (0.45, 0.36, 0.26)), M("pencil", (0.55, 0.45, 0.15))
    out = post_cap((0, -0.35, 0), 1.33, 0.11, post, 0.02)
    tilt = rot(x=math.radians(18))
    c = Vector((0, 1.08, 0.02))
    bw, bd, bh = 0.42, 0.3, 0.1

    def T(p):
        return c + (tilt.to_3x3() @ Vector(p))
    out.append(gbox(tuple(T((0, -bh * 0.5 + 0.01, 0))), (bw, 0.02, bd), plank, 0.004, tilt))
    out.append(gbox(tuple(T((0, 0, bd * 0.5))), (bw, bh, 0.02), plank, 0.004, tilt))
    out.append(gbox(tuple(T((0, 0, -bd * 0.5))), (bw, bh, 0.02), plank, 0.004, tilt))
    out.append(gbox(tuple(T((-bw * 0.5, 0, 0))), (0.02, bh, bd), plank, 0.004, tilt))
    out.append(gbox(tuple(T((bw * 0.5, 0, 0))), (0.02, bh, bd), plank, 0.004, tilt))
    hinge = T((0, bh * 0.5, -bd * 0.5))
    lid = rot(x=math.radians(-100))
    out.append(gbox(tuple(hinge + lid.to_3x3() @ Vector((0, 0.012, bd * 0.5))), (bw + 0.03, 0.022, bd + 0.02), plank, 0.005, lid))
    out.append(gcyl(T((0.12, -bh * 0.5 + 0.04, 0.09)), T((0.02, -bh * 0.5 + 0.04, 0.12)), 0.005, 0.005, 6, pencil))
    for o in out:
        weather(o, foot=-0.35, top=1.3, grime=0.3, seed=43)
    finish(out, "trail_register", prev=(0, 0.9, 0), dist=2)


# ------------------------------------------------------------------ the signs (SignPost.cs), in parts


def sign_post_tall():
    clear()
    p = M("signpost", (0.25, 0.18, 0.12))
    out = flat_post((0, -0.4, 0), 2.25 + 0.4, 0.17, p)
    for o in out:
        weather(o, foot=-0.4, top=2.3, grime=0.3, seed=47)
    finish(out, "sign_post_tall", prev=(0, 1.2, 0), dist=3)


def sign_board(name, len_, h, t, dir_, crack=False):
    clear()
    board, paint, metal = M("signbrown", (0.25, 0.18, 0.12)), M("whitepaint", (0.9, 0.9, 0.88)), M("metal", (0.25, 0.24, 0.22), 0.6, 0.4)
    out = [arrow_board((0, 0, 0), len_, h, t, 0 if crack else dir_, board)]   # (the low boards are square; only their arrow points)
    weather(out[0], foot=-h * 0.5, top=h * 0.5, grime=0.25, seed=len(name))
    face = t * 0.5 + 0.002
    if dir_ != 0:
        alen = 0.22 if len_ > 1.4 else 0.2
        edge = 0.22 if len_ > 1.4 else 0.1
        out += routed_arrow((dir_ * (len_ * 0.5 - edge - alen * 0.5), 0, face), alen, h * (0.42 if len_ > 1.4 else 0.34), dir_, paint)
    if len_ > 1.4:   # the directional boards are bolted over the post
        x = dir_ * (len_ * 0.5 - 0.2)
        out.append(flat_shade(gbox((-x, 0, t * 0.5 + 0.005), (0.025, 0.025, 0.012), metal, 0.004), (0.25, 0.24, 0.22)))
    if crack:
        out.append(flat_shade(gbox((0.1, -h * 0.33, t * 0.5 + 0.001), (len_ * 0.7, 0.006, 0.002), M("crack", (0.03, 0.025, 0.02)), 0, rot(z=0.02)), (0.02, 0.02, 0.02)))
    finish(out, name, prev=(0, 0, 0), dist=2.2, h=0.3)


def sign_low_frame():
    clear()
    p = M("signpost", (0.25, 0.18, 0.12))
    out = []
    for px in (-0.48, 0.48):
        out += flat_post((px, -0.35, 0), 0.95 + 0.35, 0.11, p)
    out.append(header((0, 0.95 + 0.065, 0), 0.96 + 0.11, 0.2, 0.11, 0.14, p))
    for o in out:
        weather(o, foot=-0.35, top=1.05, grime=0.32, seed=53)
    finish(out, "sign_low_frame", prev=(0, 0.6, 0), dist=2.5)


# ------------------------------------------------------------------ the trailhead's dressing (TrailheadDressing.cs), in parts


def rail_post():
    clear()
    bark, end = M("bark", (0.32, 0.25, 0.18)), M("endgrain", (0.6, 0.5, 0.36))
    out = [log_mesh((0, -0.15, 0), (0, 0.42, 0), 0.11, 0.1, bark, 5, sides=12)]
    out.append(gcyl((0, 0.42, 0), (0, 0.426, 0), 0.1, 0.1, 12, end))
    weather(out[0], (0.78, 0.72, 0.64), foot=-0.15, top=0.45, grime=0.3, seed=59)
    finish(out, "rail_post", prev=(0, 0.2, 0), dist=1.5)


def rail_log(v):
    """A log of length 1 along +X (the game scales it to the gap), radius 0.14; three of them, each its own bark."""
    clear()
    bark, end = M("bark", (0.32, 0.25, 0.18)), M("endgrain", (0.6, 0.5, 0.36))
    out = [log_mesh((0, 0, 0), (1, 0, 0), 0.14, 0.13, bark, 7 + v * 3, sides=12)]
    out.append(gcyl((-0.01, 0, 0), (0, 0, 0), 0.136, 0.136, 12, end))
    out.append(gcyl((1, 0, 0), (1.01, 0, 0), 0.126, 0.126, 12, end))
    weather(out[0], (0.82, 0.76, 0.68), foot=-0.14, top=0.14, grime=0.25, seed=61 + v)
    finish(out, f"rail_log_{v}", prev=(0.5, 0, 0), dist=2)


def pipe_gate():
    """The forest road's gate, swung open: the hinge post at the origin, the arm lying back along the road (the game's -Z),
    the latch post its own model. Facing is the road's basis (X its right)."""
    clear()
    yellow, refl, metal = M("gate_yellow", (0.74, 0.57, 0.13), 0.75), M("reflector", (0.6, 0.14, 0.1), 0.5), M("metal", (0.32, 0.3, 0.28), 0.6, 0.4)
    out = [gbox((0, 0.45, 0), (0.14, 1.25, 0.14), yellow, 0.015), gbox((0, 1.08, 0), (0.2, 0.04, 0.2), yellow, 0.01)]
    tip = Vector((0.15, 0, -4.6))
    a0, a1 = Vector((0, 0.9, 0)), tip + Vector((0, 0.86, 0))
    out.append(gcyl(a0, a1, 0.05, 0.05, 14, yellow))
    b0, b1 = Vector((0, 0.45, 0)), tip + Vector((0, 0.5, 0.6))
    out.append(gcyl(b0, b1, 0.035, 0.035, 12, yellow))
    for i in range(1, 4):
        u = i / 4
        out.append(gcyl(a0.lerp(a1, u), b0.lerp(b1, u), 0.025, 0.025, 10, yellow))
    out.append(gbox(tuple(a0.lerp(a1, 0.55) + Vector((0.055, 0, 0))), (0.01, 0.12, 0.35), refl, 0.002))
    stake = tip + Vector((0.25, 0, 0))
    out.append(gcyl(stake + Vector((0, -0.1, 0)), stake + Vector((0, 0.55, 0)), 0.025, 0.02, 8, metal))
    link, to = a1 + Vector((0, 0, -0.02)), stake + Vector((0, 0.5, 0))
    for i in range(10):
        u0, u1 = i / 10, (i + 1) / 10
        c0 = link.lerp(to, u0) + Vector((0, -0.12 * math.sin(math.pi * u0), 0))
        c1 = link.lerp(to, u1) + Vector((0, -0.12 * math.sin(math.pi * u1), 0))
        out.append(gcyl(c0, c1, 0.008, 0.008, 5, metal))
    for o in out:
        if not o.data.color_attributes:
            weather(o, foot=0, top=1.2, grime=0.35, seed=67)
    finish(out, "pipe_gate", prev=(0, 0.6, -2.3), dist=6)


def gate_latch_post():
    clear()
    yellow = M("gate_yellow", (0.74, 0.57, 0.13), 0.75)
    ob = gbox((0, 0.4, 0), (0.12, 1.1, 0.12), yellow, 0.015)
    weather(ob, foot=-0.15, top=1, grime=0.35, seed=71)
    finish([ob], "gate_latch_post", prev=(0, 0.5, 0), dist=2)


def highway_sign():
    clear()
    board, post = M("signbrown", (0.25, 0.18, 0.12)), M("signpost", (0.25, 0.18, 0.12))
    w, h, by = 1.5, 0.75, 1.55
    out = []
    for px in (-w * 0.32, w * 0.32):
        out.append(gbox((px, (by + h * 0.5) * 0.5, 0), (0.09, by + h * 0.5 + 0.3, 0.09), post, 0.01))
    out.append(gbox((0, by, 0.02), (w, h, 0.04), board, 0.008))
    for o in out:
        weather(o, foot=-0.15, top=2, grime=0.3, seed=73)
    finish(out, "highway_sign", prev=(0, 1.2, 0), dist=3.5)


def curve_sign():
    clear()
    metal, yel, blk = M("metal", (0.45, 0.45, 0.45), 0.6, 0.4), M("sign_yellow", (0.62, 0.5, 0.1), 0.8), M("sign_black", (0.05, 0.045, 0.04), 0.9)
    out = [flat_shade(gbox((0, 1.05, 0), (0.07, 2.4, 0.04), metal, 0.006), (0.55, 0.55, 0.55))]
    tilt = rot(x=math.radians(-6))
    c = Vector((0, 2.05, 0.04))
    T3 = tilt.to_3x3()
    out.append(gbox(tuple(c), (0.58, 0.58, 0.015), yel, 0.01, tilt @ rot(z=math.pi * 0.25)))
    out.append(gbox(tuple(c + T3 @ Vector((0, -0.04, 0.009))), (0.05, 0.26, 0.004), blk, 0.001, tilt))
    out.append(gbox(tuple(c + T3 @ Vector((0.045, 0.12, 0.01))), (0.05, 0.16, 0.004), blk, 0.001, tilt @ rot(z=math.radians(-45))))
    for o in out[1:2]:
        weather(o, (1, 1, 1), foot=1.7, top=2.4, grime=0.2, seed=79)
    finish(out, "curve_sign", prev=(0, 1.4, 0), dist=3.5)


# ------------------------------------------------------------------ the lake's signs


def dock_sign():
    clear()
    post, plank = M("post", (0.42, 0.33, 0.24)), M("plank", (0.45, 0.36, 0.26))
    out = post_cap((0, 0, 0), 1.85, 0.12, post)
    b = rot(y=0.35) @ Matrix.Rotation(0.05, 4, Vector((0, 0, -1)))
    out.append(arrow_board((0.05, 1.55, 0.07), 1.3, 0.3, 0.05, 0, plank, b))
    for o in out:
        weather(o, (0.5, 0.45, 0.36), foot=0, top=1.9, grime=0.3, seed=83)
    finish(out, "dock_sign", prev=(0, 1.2, 0), dist=3)


def birdhouse():
    clear()
    boards, moss, hole = M("boards", (0.5, 0.43, 0.35)), M("moss_roof", (0.36, 0.4, 0.3)), M("hole", (0.02, 0.02, 0.02))
    out = [gbox((0, 0, 0), (0.24, 0.32, 0.22), boards, 0.01), gbox((0, -0.4, -0.03), (0.12, 0.5, 0.06), boards, 0.008)]
    out.append(gbox((-0.08, 0.2, 0), (0.2, 0.03, 0.28), moss, 0.006, rot(z=0.55)))
    out.append(gbox((0.08, 0.2, 0), (0.2, 0.03, 0.28), moss, 0.006, rot(z=-0.55)))
    out.append(gcyl((0, 0.05, 0.105), (0, 0.05, 0.114), 0.035, 0.035, 14, hole))
    for o in out:
        weather(o, (0.62, 0.55, 0.46), foot=-0.65, top=0.25, grime=0.3, seed=89)
    finish(out, "birdhouse", prev=(0, -0.1, 0), dist=1.4)


def pipeline_sign():
    clear()
    metal, face, rim = M("metal", (0.45, 0.45, 0.42), 0.6, 0.4), M("pipeline_sign", (0.78, 0.77, 0.72), 0.8), M("pipeline_rim", (0.55, 0.45, 0.1), 0.8)
    out = []
    for x in (-0.55, 0.55):
        out.append(flat_shade(gcyl((x, -0.6, 0), (x, 2.9, 0), 0.04, 0.04, 12, metal), (0.55, 0.55, 0.52)))
    out.append(gbox((0, 2.2, 0.06), (1.5, 1.0, 0.03), face, 0.004))
    out.append(gbox((0, 2.2, 0.045), (1.58, 1.08, 0.02), rim, 0.004))
    for o in out[2:]:
        weather(o, (1, 1, 1), foot=1.6, top=2.8, grime=0.25, seed=97)
    finish(out, "pipeline_sign", prev=(0, 1.6, 0), dist=4)


ALL = {
    "trailhead_sign": trailhead_sign, "info_kiosk": info_kiosk, "trash_can": trash_can, "vault_toilet": vault_toilet,
    "bumper": bumper, "trail_marker": trail_marker, "backpack": backpack, "boot": boot, "walking_stick": walking_stick,
    "cut_log": cut_log, "torn_map": torn_map, "tent": tent, "holed_stone": holed_stone, "trail_register": trail_register,
    "sign_post_tall": sign_post_tall, "sign_low_frame": sign_low_frame,
    "dir_board_r": lambda: sign_board("dir_board_r", 1.55, 0.27, 0.05, 1),
    "dir_board_l": lambda: sign_board("dir_board_l", 1.55, 0.27, 0.05, -1),
    "dir_board_0": lambda: sign_board("dir_board_0", 1.55, 0.27, 0.05, 0),
    "low_board_r": lambda: sign_board("low_board_r", 1.3, 0.28, 0.05, 1, True),
    "low_board_l": lambda: sign_board("low_board_l", 1.3, 0.28, 0.05, -1, True),
    "low_board_0": lambda: sign_board("low_board_0", 1.3, 0.28, 0.05, 0, True),
    "rail_post": rail_post, "pipe_gate": pipe_gate, "gate_latch_post": gate_latch_post,
    "highway_sign": highway_sign, "curve_sign": curve_sign,
    "dock_sign": dock_sign, "birdhouse": birdhouse, "pipeline_sign": pipeline_sign,
}
for i in range(3):
    ALL[f"boulder_{i}"] = (lambda v=i: boulder(v))
    ALL[f"rail_log_{i}"] = (lambda v=i: rail_log(v))
for i in range(4):
    ALL[f"pebble_{i}"] = (lambda v=i: pebble(v))

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(ALL)
    for n in names:
        print(f"[park] building {n}")
        ALL[n]()
