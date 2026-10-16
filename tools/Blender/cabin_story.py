"""The story's things in the one cabin (2026-10-16, STORY.md Revision 5: the cabin is the Act 1 hub by the lot, boarded in
the Hollow in Act 5, burning in Act 7 and burned in Act 25; the same things in it every time). Run headless:

    blender -b --python tools/Blender/cabin_story.py

-> assets/models/items/cabin_story.glb (ItemModels.Get("cabin_story", <name>)):
- PhotoBoard: the backlit photo board, a light box of dark stained wood. Its origin is the centre of its back, on the wall;
  its face is toward +Z. Outer 1.04 x 0.74, 6 cm deep, the frame's face at z = 0.06. A little rocker switch on its right
  side, two brass hanging plates at its top.
- PhotoBoardPanel: the frosted panel inside the frame (material "glow"), 0.94 x 0.64, its face at z = 0.042. The prints are
  pinned to it.
- BoardPin: a pushpin. Its point is at the origin, the head toward +Z (about 1.2 cm out).
- PictureFrame: a 5 x 7 standing frame (portrait), tilted back 12 degrees on its easel. Origin at the middle of its bottom
  front edge, on the table. The photo (drawn by the game) sits in its opening: upright, the opening's centre would be at
  (0, 0.109, 0.004) facing +Z, 0.127 x 0.178; the whole frame is turned back by -12 degrees about X round (0, 0, 0.008).
- AxePegs: two pegs for the axe over the hearth. Modelled against a wall at z = 0 (+Z out of the wall), 0.4 m apart along
  X, the handle resting on them at about y = 0.03, z = 0.06.
- AssignmentFolder: a manila folder lying shut on the table, a paper clip at its back edge (the sheet itself is the game's
  readable paper, laid on it at y = 0.0045). Origin at the middle of its bottom face; 0.235 wide (X), 0.305 deep (Z).
- Pencil: a yellow pencil, sharpened, its eraser end to -X; centred on the origin, lying along X (its flats down).

The game's axes, as rh_key.py: B()/S() turn a game point/size (Y up, -Z forward) into Blender's, and the export's Y-up turn
brings it back exactly. Materials are named for the game to paint ("board_wood", "glow", "pin_red", "steel", "brass",
"frame_wood", "peg_wood", "manila", "pencil_paint", "graphite", "endgrain", "eraser"): PhotoBoard.cs paints them.
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix
import kit
from kit import *

kit.OUT = os.path.join(ROOT, "assets", "models", "items")
os.makedirs(kit.OUT, exist_ok=True)

PREVIEW_COL = {
    "board_wood": (0.2, 0.14, 0.1), "glow": (0.95, 0.92, 0.82), "pin_red": (0.7, 0.1, 0.08), "steel": (0.6, 0.6, 0.6),
    "brass": (0.78, 0.6, 0.28), "frame_wood": (0.32, 0.2, 0.12), "peg_wood": (0.45, 0.32, 0.2), "manila": (0.8, 0.68, 0.45),
    "pencil_paint": (0.85, 0.65, 0.12), "graphite": (0.12, 0.12, 0.12), "endgrain": (0.75, 0.6, 0.42), "eraser": (0.8, 0.45, 0.42),
    "backing": (0.35, 0.3, 0.24),
}


def M(base, rough=0.6, metal=0.0):
    return mat(base, PREVIEW_COL.get(base, (0.6, 0.6, 0.6)), rough, metal)


def B(p):
    x, y, z = p
    return Vector((x, -z, y))


def S(s):
    x, y, z = s
    return (x, z, y)


def apply(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def rot(ob, axis_game, angle, pivot_game=(0, 0, 0)):
    apply(ob)
    pv = B(pivot_game)
    ob.data.transform(Matrix.Translation(pv) @ Matrix.Rotation(angle, 4, B(axis_game)) @ Matrix.Translation(-pv))
    return ob


def smooth(ob, angle=40):
    for p in ob.data.polygons:
        p.use_smooth = True
    if hasattr(ob.data, "use_auto_smooth"):
        ob.data.use_auto_smooth = True
        ob.data.auto_smooth_angle = math.radians(angle)
    return ob


def gbox(name, c, s, m, bevel=0.0, segs=2, uvs=8.0):
    ob = box(name, B(c), S(s), m, bevel, segs)
    apply(ob)
    uv_box(ob, uvs)
    return smooth(ob, 35)


def gcyl(name, a, b, r0, r1=None, sides=16, m=None, caps=True, uvs=8.0, bevel=0.0):
    ob = cylinder(name, B(a), B(b), r0, r1, sides, m, caps)
    if bevel > 0 and caps:
        add_mod(ob, "BEVEL", width=bevel, segments=2, limit_method="ANGLE")
        apply_mods(ob)
    uv_box(ob, uvs)
    return smooth(ob, 50)


def finish(parts, name):
    ob = join(parts, name)
    return smooth(ob)


# ------------------------------------------------------------------ the photo board

def photo_board():
    wood = M("board_wood", 0.7)
    back = M("backing", 0.9)
    W, H, D, F = 1.04, 0.74, 0.06, 0.05   # outer width, height, depth, the moulding's width
    p = []
    # the light box behind the panel (where the tubes are)
    p.append(gbox("box", (0, 0, 0.02), (W - 0.02, H - 0.02, 0.04), back, bevel=0.004))
    # the frame: four rails, eased, a little proud at the front
    p.append(gbox("top", (0, H * 0.5 - F * 0.5, D * 0.5), (W, F, D), wood, bevel=0.006, segs=2))
    p.append(gbox("bottom", (0, -H * 0.5 + F * 0.5, D * 0.5), (W, F, D), wood, bevel=0.006, segs=2))
    p.append(gbox("left", (-W * 0.5 + F * 0.5, 0, D * 0.5), (F, H - 2 * F + 0.002, D), wood, bevel=0.006, segs=2))
    p.append(gbox("right", (W * 0.5 - F * 0.5, 0, D * 0.5), (F, H - 2 * F + 0.002, D), wood, bevel=0.006, segs=2))
    # a bead run round the opening, just inside the face
    t = 0.008
    oi_w, oi_h = W - 2 * F, H - 2 * F
    p.append(gbox("bead_t", (0, oi_h * 0.5 - t * 0.5, D - 0.012), (oi_w, t, 0.01), wood, bevel=0.002))
    p.append(gbox("bead_b", (0, -oi_h * 0.5 + t * 0.5, D - 0.012), (oi_w, t, 0.01), wood, bevel=0.002))
    p.append(gbox("bead_l", (-oi_w * 0.5 + t * 0.5, 0, D - 0.012), (t, oi_h, 0.01), wood, bevel=0.002))
    p.append(gbox("bead_r", (oi_w * 0.5 - t * 0.5, 0, D - 0.012), (t, oi_h, 0.01), wood, bevel=0.002))
    # the rocker switch on its right side, low down, in a little black housing
    sw = M("graphite", 0.5)
    p.append(gbox("sw_box", (W * 0.5 + 0.008, -H * 0.5 + 0.11, 0.03), (0.016, 0.05, 0.03), sw, bevel=0.002))
    rk = gbox("sw_rocker", (W * 0.5 + 0.018, -H * 0.5 + 0.11, 0.03), (0.006, 0.026, 0.014), sw, bevel=0.0015)
    rot(rk, (0, 0, 1), 0.2, (W * 0.5 + 0.016, -H * 0.5 + 0.11, 0.03))
    p.append(rk)
    # the two brass hanging plates screwed to its top, the wall's nails through them
    brass = M("brass", 0.4, 0.8)
    steel = M("steel", 0.4, 0.8)
    for x in (-0.36, 0.36):
        p.append(gbox(f"hang{x}", (x, H * 0.5 + 0.012, 0.004), (0.03, 0.04, 0.003), brass, bevel=0.001))
        p.append(gcyl(f"nail{x}", (x, H * 0.5 + 0.022, 0.0), (x, H * 0.5 + 0.022, 0.009), 0.0035, 0.0035, 10, steel))
    ob = finish(p, "PhotoBoard")
    # the panel, its own mesh (the game lights it)
    panel = gbox("PhotoBoardPanel", (0, 0, 0.04), (oi_w - 2 * t + 0.002, oi_h - 2 * t + 0.002, 0.004), M("glow", 0.4), uvs=1.0)
    panel.name = "PhotoBoardPanel"
    return [ob, panel]


def board_pin():
    red = M("pin_red", 0.35)
    steel = M("steel", 0.3, 0.9)
    p = [gcyl("needle", (0, 0, -0.0005), (0, 0, 0.003), 0.0006, 0.0007, 8, steel),
         gcyl("collar", (0, 0, 0.002), (0, 0, 0.0045), 0.0045, 0.0032, 14, red, bevel=0.0006),
         gcyl("grip", (0, 0, 0.0045), (0, 0, 0.01), 0.0026, 0.0034, 12, red),
         gcyl("head", (0, 0, 0.01), (0, 0, 0.0125), 0.0052, 0.0052, 16, red, bevel=0.0009)]
    return finish(p, "BoardPin")


# ------------------------------------------------------------------ the framed photo

def picture_frame():
    wood = M("frame_wood", 0.5)
    back = M("backing", 0.9)
    pw, ph, F, D = 0.127, 0.178, 0.02, 0.016
    ow, oh = pw + 2 * F, ph + 2 * F
    cy = oh * 0.5
    p = []
    # the moulding: four rails with a stepped profile (a flat and a raised round on the outside)
    for name, c, s in (("top", (0, oh - F * 0.5, 0), (ow, F, D)), ("bot", (0, F * 0.5, 0), (ow, F, D)),
                       ("l", (-ow * 0.5 + F * 0.5, cy, 0), (F, ph + 0.001, D)), ("r", (ow * 0.5 - F * 0.5, cy, 0), (F, ph + 0.001, D))):
        p.append(gbox(name, c, s, wood, bevel=0.003, segs=2))
    for name, c, s in (("rt", (0, oh - F * 0.35, D * 0.5 + 0.002), (ow - 0.01, F * 0.3, 0.004)), ("rb", (0, F * 0.35, D * 0.5 + 0.002), (ow - 0.01, F * 0.3, 0.004)),
                       ("rl", (-ow * 0.5 + F * 0.35, cy, D * 0.5 + 0.002), (F * 0.3, oh - 0.01, 0.004)), ("rr", (ow * 0.5 - F * 0.35, cy, D * 0.5 + 0.002), (F * 0.3, oh - 0.01, 0.004))):
        p.append(gbox(name, c, s, wood, bevel=0.0015))
    # the backing board behind the photo, and the easel strut hinged to it
    p.append(gbox("back", (0, cy, -D * 0.5 + 0.002), (ow - 0.01, oh - 0.01, 0.004), back))
    ob = finish(p, "PictureFrame")
    # tilted back on its easel, round the bottom front edge
    tilt = -math.radians(12)
    rot(ob, (1, 0, 0), tilt, (0, 0, D * 0.5))
    # the strut: from high on the back down to the table behind it (after the tilt, so its foot lands at y = 0)
    top = Vector((0, oh * 0.62, -D * 0.5 - 0.002))
    c, s = math.cos(tilt), math.sin(tilt)
    pv = Vector((0, 0, D * 0.5))
    rel = top - pv
    top_t = Vector((0, rel.y * c - rel.z * s, rel.y * s + rel.z * c)) + pv
    foot = Vector((0, 0.004, top_t.z - 0.09))
    strut = gbox("strut", (0, 0, 0), (0.03, 1, 0.004), back)
    # lay the unit-tall strut from the foot up to the top
    apply(strut)
    length = (top_t - foot).length
    strut.data.transform(Matrix.Diagonal((1, 1, length, 1)))
    ang = math.atan2(top_t.z - foot.z, top_t.y - foot.y)   # its lean back, from the vertical
    mid = (top_t + foot) * 0.5
    strut.data.transform(Matrix.Translation(B(tuple(mid))) @ Matrix.Rotation(ang, 4, B((1, 0, 0))))
    hinge = gbox("strut_hinge", tuple(top_t + Vector((0, 0, -0.002))), (0.022, 0.012, 0.004), M("brass", 0.4, 0.8))
    ob = finish([ob, strut, hinge], "PictureFrame")
    return ob


# ------------------------------------------------------------------ the pegs over the hearth

def axe_pegs():
    peg = M("peg_wood", 0.75)
    p = []
    for x in (-0.2, 0.2):
        # a split block nailed to the log, and a dowel in it angled up a little, a knob on its end
        p.append(gbox(f"block{x}", (x, -0.01, 0.012), (0.05, 0.07, 0.024), peg, bevel=0.004))
        p.append(gcyl(f"dowel{x}", (x, 0.0, 0.02), (x, 0.022, 0.095), 0.011, 0.01, 12, peg))
        p.append(gcyl(f"knob{x}", (x, 0.02, 0.09), (x, 0.032, 0.102), 0.014, 0.012, 12, peg, bevel=0.003))
        p.append(gcyl(f"nail{x}", (x, -0.03, 0.022), (x, -0.03, 0.026), 0.004, 0.004, 8, M("steel", 0.5, 0.8)))
    return finish(p, "AxePegs")


# ------------------------------------------------------------------ the assignment's folder, and a pencil

def assignment_folder():
    manila = M("manila", 0.85)
    w, d = 0.235, 0.305
    p = [gbox("lower", (0, 0.001, 0), (w, 0.002, d), manila, bevel=0.0006),
         gbox("upper", (0.002, 0.0032, 0.002), (w - 0.004, 0.0016, d - 0.006), manila, bevel=0.0005),
         # the tab along its back edge, to the left
         gbox("tab", (-w * 0.22, 0.001, -d * 0.5 - 0.009), (0.08, 0.002, 0.018), manila, bevel=0.0006)]
    ob = finish(p, "AssignmentFolder")
    # the paper clip over the back edge (where the sheet will be), a bent wire loop above and below
    steel = M("steel", 0.3, 0.9)
    wires = []
    x0, z0 = 0.05, -d * 0.5 + 0.006
    for y, ln, wd in ((0.0058, 0.032, 0.0085), (0.0, 0.026, 0.006)):
        # two long sides and the round ends, as thin cylinders
        for sx in (-1, 1):
            wires.append(gcyl(f"clip{y}{sx}", (x0 + sx * wd * 0.5, y + 0.0005, z0), (x0 + sx * wd * 0.5, y + 0.0005, z0 + ln), 0.0006, 0.0006, 6, steel, caps=False))
        wires.append(gcyl(f"clipend{y}", (x0 - wd * 0.5, y + 0.0005, z0 + ln), (x0 + wd * 0.5, y + 0.0005, z0 + ln), 0.0006, 0.0006, 6, steel, caps=False))
    wires.append(gcyl("clipbend", (x0, -0.0005, z0 - 0.001), (x0, 0.0063, z0 - 0.001), 0.0006, 0.0006, 6, steel, caps=False))
    return finish([ob] + wires, "AssignmentFolder")


def pencil():
    paint = M("pencil_paint", 0.45)
    p = []
    L, r = 0.15, 0.0037
    body = gcyl("body", (-L * 0.5, 0, 0), (L * 0.5 - 0.018, 0, 0), r, r, 6, paint, caps=True)
    # flats down: a hexagon's flat (not its corner) on the table
    rot(body, (1, 0, 0), math.radians(30), (0, 0, 0))
    p.append(body)
    p.append(gcyl("wood", (L * 0.5 - 0.018, 0, 0), (L * 0.5 - 0.003, 0, 0), r * 0.98, 0.0011, 12, M("endgrain", 0.8)))
    p.append(gcyl("lead", (L * 0.5 - 0.003, 0, 0), (L * 0.5 + 0.001, 0, 0), 0.0011, 0.0002, 8, M("graphite", 0.4)))
    p.append(gcyl("ferrule", (-L * 0.5 - 0.009, 0, 0), (-L * 0.5 + 0.001, 0, 0), r * 1.03, r * 1.03, 14, M("brass", 0.35, 0.8)))
    p.append(gcyl("eraser", (-L * 0.5 - 0.016, 0, 0), (-L * 0.5 - 0.009, 0, 0), r * 0.95, r * 0.98, 14, M("eraser", 0.9), bevel=0.0012))
    ob = finish(p, "Pencil")
    # resting on its flats: lift it so it sits on y = 0
    ob.data.transform(Matrix.Translation(B((0, r * math.cos(math.radians(30)), 0))))
    return ob


def build():
    clear()
    obs = photo_board() + [board_pin(), picture_frame(), axe_pegs(), assignment_folder(), pencil()]
    for o in obs:
        o.location = (0, 0, 0)
    export(obs, "cabin_story")
    for o in obs:
        for q in obs:
            q.hide_render = q is not o
        d = max(o.dimensions)
        c = sum((o.matrix_world @ Vector(b) for b in o.bound_box), Vector()) / 8
        preview(f"cabin_story_{o.name}", tuple(c), d * 1.4 + 0.03, height=d * 0.6, yaw=30, size=(600, 450))


if __name__ == "__main__":
    build()
