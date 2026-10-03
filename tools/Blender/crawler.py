"""The crawler (Act 14's stairwell, the church's crypt), modelled, baked and rigged in Blender (the owner, 2026-10-02: the
remaining monster brought up to the wendigo's and the stalker's standard, "as horrific as possible").

    blender -b --python tools/Blender/crawler.py

Writes:
  assets/models/crawler/crawler_rig.glb   one skinned figure on its skeleton (no clips: the game drives every bone,
                                          its limbs planted by Crawler.cs's own two-bone solver, as before)
  build/blender/crawler_*.png             previews

The design is the owner's (their reference): a pale, starved humanoid on all fours, its long limbs folded so the
elbows and knees ride up higher than its back, its head hanging low between its shoulders. Now one hide over its
bones: the spine a ridge of knuckles down its back, the shoulder blades and the hip bones standing up out of it, the
ribs, the belly sunk to nothing; long hands and feet with long knuckled fingers and toes, the nails black; long black
hair hanging from its scalp in wet strands; its jaw unhinged, hanging far too low, full of crowded teeth; its eyes
sunk to dark hollows.

It is built in the game's own body space (x its right, y up, z its back: it faces -z, its origin the body's middle,
BodyHeight 0.7 m above the ground at rest) and turned into Blender's (x, -z, y) as it goes, so the export lands back
in the game's space with nothing to undo.
"""
import bpy, bmesh, math, os, random, sys
import numpy as np
from mathutils import Vector, Matrix, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import clear, mat, skin_tree, lumpy_sphere, cylinder, join, apply_mods, add_mod, obj_from_bmesh, fbm, ROOT
import kit
import wendigo as W

OUT = os.path.join(ROOT, "assets", "models", "crawler")
PREV = os.path.join(ROOT, "build", "blender")
os.makedirs(OUT, exist_ok=True)
V = Vector
rnd = random.Random(4404)
TEXSIZE = 2048


def P(x, y, z):
    """A point from the game's body space to Blender's."""
    return V((x, -z, y))


def G(v):
    """Back from Blender's to the game's (for the colours, which are thought of in the game's space)."""
    return V((v.x, v.z, -v.y))


# the game's numbers (Crawler.cs)
FRONT = [(P(-0.2, 0.03, -0.32), P(-0.46, -0.7, -0.58), 0.62, 0.66), (P(0.2, 0.03, -0.32), P(0.46, -0.7, -0.58), 0.62, 0.66)]
BACK = [(P(-0.16, 0.0, 0.4), P(-0.44, -0.7, 0.64), 0.66, 0.72), (P(0.16, 0.0, 0.4), P(0.44, -0.7, 0.64), 0.66, 0.72)]
NECK0, NECK1 = P(0, 0.02, -0.42), P(0, -0.12, -0.58)
HEAD = P(0, -0.17, -0.63)


def knee_of(root, foot, l1, l2, side):
    """Crawler.Solve: the elbow or knee thrown up and out."""
    d = foot - root
    dist = min(max(d.length, 0.05), (l1 + l2) * 0.98)
    dirv = d.normalized()
    a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
    b = math.sqrt(max(0.0, l1 * l1 - a * a))
    outward = V((side, 0, 0))
    pole = (P(0, 1, 0) * 2.2 + outward).normalized()
    pole = (pole - dirv * pole.dot(dirv)).normalized()
    return root + dirv * a + pole * b, root + dirv * dist


def limbs():
    out = []
    for kind, defs in (("arm", FRONT), ("leg", BACK)):
        for root, rest, l1, l2 in defs:
            s = 1 if root.x > 0 else -1
            knee, foot = knee_of(root, rest, l1, l2, s)
            out.append(dict(kind=kind, s=s, sx="R" if s > 0 else "L", root=root, knee=knee, foot=foot))
    return out


def digits(L):
    """The long fingers (or toes) off a hand (or foot) lying flat, pointing ahead (the game's -z) and splayed."""
    front = L["kind"] == "arm"
    foot = L["foot"]
    palm = foot + P(0, 0.012, -0.05)
    out = []
    n = 4
    for i in range(n):
        a = (i - 1.5) * 0.33
        d = P(math.sin(a) * L["s"] * 0.6, 0, -math.cos(a)).normalized()
        k = palm + P(0, 0, -0.03) + d * 0.02
        pts, cur = [k], k
        lens = (0.075, 0.06, 0.05) if front else (0.055, 0.045, 0.035)
        for j, ln in enumerate(lens):
            dd = (d + P(0, -0.15 * (j + 1), 0)).normalized()
            cur = cur + dd * ln
            pts.append(cur)
        out.append(pts)
    return palm, out


def materials():
    return dict(skin=mat("c_skin", (0.6, 0.58, 0.54), 0.7), nail=mat("c_nail", (0.03, 0.025, 0.02), 0.4),
                hair=mat("c_hair", (0.02, 0.02, 0.02), 0.6), tooth=mat("c_tooth", (0.6, 0.55, 0.42), 0.4),
                dark=mat("c_dark", (0.02, 0.01, 0.01), 0.3))


def R(rx, ry, rz):
    """Radii given along the game's axes (x across, y up, z along) as lumpy_sphere wants them (Blender's)."""
    return (rx, rz, ry)


SPINE = [(0.48, 0.0, 0.13, 0.11), (0.38, 0.02, 0.19, 0.12), (0.24, 0.05, 0.15, 0.1), (0.1, 0.07, 0.12, 0.085),
         (-0.04, 0.08, 0.15, 0.1), (-0.18, 0.07, 0.2, 0.13), (-0.3, 0.05, 0.22, 0.14), (-0.4, 0.03, 0.17, 0.11), (-0.47, 0.0, 0.08, 0.07)]


def torso_at(z):
    """The torso's ring at z (the game's): its middle's height, half-width, half-height."""
    for (z0, y0, w0, h0), (z1, y1, w1, h1) in zip(SPINE, SPINE[1:]):
        if z1 <= z <= z0:
            u = (z0 - z) / (z0 - z1)
            return y0 + (y1 - y0) * u, w0 + (w1 - w0) * u, h0 + (h1 - h0) * u
    return SPINE[-1][1:]


def chain(name, pts, radii, subdiv=1):
    return skin_tree(name, pts, [(i - 1, i) for i in range(1, len(pts))], radii, None, subdiv=subdiv)


def remesh(ob, voxel, smooth=4):
    add_mod(ob, "REMESH", mode="VOXEL", voxel_size=voxel, adaptivity=0.0)
    apply_mods(ob)
    add_mod(ob, "SMOOTH", factor=0.6, iterations=smooth)
    apply_mods(ob)
    return ob


def loft_torso():
    """The torso along the spine (pelvis behind, chest ahead), its back arched up, its belly sunk."""
    bm = bmesh.new()
    spine = SPINE
    rings = []
    nr = 36
    for z, y, w, h in spine:
        ring = []
        for j in range(nr):
            a = j / nr * math.tau
            c, s = math.cos(a), math.sin(a)
            under = 0.6 if s < 0 and -0.2 < z < 0.3 else 1.0          # the belly sunk to nothing
            ring.append(bm.verts.new(P(c * w, y + s * h * under, z)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for j in range(nr):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % nr], rings[i + 1][(j + 1) % nr], rings[i + 1][j]))
    for ring, c in ((rings[0], P(0, 0, 0.52)), (rings[-1], P(0, 0, -0.5))):
        cv = bm.verts.new(c)
        for j in range(nr):
            bm.faces.new((ring[j], ring[(j + 1) % nr], cv))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(bm, "Torso")


def build_body(m):
    parts = [loft_torso()]
    # the spine's knuckles standing up out of the back, the shoulder blades, the hip bones, the ribs
    for k in range(24):
        z = 0.46 - k * 0.04
        y = 0.13 - abs(z - 0.04) * 0.08
        parts.append(lumpy_sphere(f"Vert{k}", P(0, y + 0.012, z), (0.022, 0.024, 0.02), None, 200 + k, amp=0.12, subdiv=2))
    for s in (-1, 1):
        parts.append(lumpy_sphere(f"Blade{s}", P(s * 0.11, 0.16, -0.26), R(0.07, 0.022, 0.09), None, 30 + s, amp=0.08, subdiv=3))
        parts.append(lumpy_sphere(f"HipBone{s}", P(s * 0.13, 0.08, 0.38), R(0.045, 0.025, 0.05), None, 32 + s, amp=0.08, subdiv=3))
        for i in range(8):
            z = -0.36 + i * 0.045
            y, w, h = torso_at(z)
            # round the side just under the skin, sloping back toward the hips
            pts = [P(s * w * 0.35, y + h * 0.92, z), P(s * w * 0.9, y + h * 0.45, z + 0.012), P(s * w * 1.0, y - h * 0.15, z + 0.025), P(s * w * 0.75, y - h * 0.6, z + 0.04)]
            parts.append(chain(f"Rib{s}{i}", pts, [0.008] * 4, subdiv=0))
    # the neck, hung down and forward, its cords standing out
    parts.append(chain("Neck", [NECK0 + P(0, 0.02, 0.04), NECK0, NECK0.lerp(NECK1, 0.5) + P(0, 0.01, 0), NECK1, HEAD + P(0, 0.03, 0.03)], [0.06, 0.05, 0.042, 0.04, 0.045]))
    for s in (-1, 1):
        parts.append(chain(f"Cord{s}", [NECK0 + P(s * 0.03, -0.02, 0.02), NECK1 + P(s * 0.025, -0.01, 0.0)], [0.012, 0.01]))
    # the limbs: long bones under skin, knobbed joints, the tendons
    for L in limbs():
        r0, kn, ft = L["root"], L["knee"], L["foot"]
        thick = 0.046 if L["kind"] == "arm" else 0.058
        parts.append(chain(f"Limb{L['kind']}{L['s']}", [r0, r0.lerp(kn, 0.25), r0.lerp(kn, 0.6), r0.lerp(kn, 0.9), kn, kn.lerp(ft, 0.2), kn.lerp(ft, 0.55), kn.lerp(ft, 0.9), ft + P(0, 0.02, 0)],
                           [thick * 1.15, thick, thick * 0.82, thick * 0.62, thick * 0.66, thick * 0.72, thick * 0.6, thick * 0.42, thick * 0.4]))
        # the joint's bone under the skin (not a ball: a knob on the outside of it)
        out = (kn - (r0 + ft) * 0.5).normalized()
        parts.append(lumpy_sphere(f"Joint{L['kind']}{L['s']}", kn + out * thick * 0.25, R(thick * 0.62, thick * 0.6, thick * 0.66), None, 40 + L["s"], amp=0.12, subdiv=3))
        # the root's mass: the shoulder (or the haunch) the limb comes out of
        parts.append(lumpy_sphere(f"Root{L['kind']}{L['s']}", r0.lerp(kn, 0.08) + P(0, 0.02, 0), R(thick * 1.5, thick * 1.3, thick * 1.5), None, 44 + L["s"], amp=0.08, subdiv=3))
        for k, off in enumerate((P(0.012, 0.01, 0), P(-0.012, 0.012, 0))):
            parts.append(chain(f"Tendon{L['kind']}{L['s']}{k}", [kn.lerp(ft, 0.12) + off, kn.lerp(ft, 0.8) + off * 0.6], [0.008, 0.006]))
    body = join(parts, "Body")
    remesh(body, 0.0055)
    body.data.materials.clear()
    body.data.materials.append(m["skin"])
    print(f"[crawler] body remeshed: {len(body.data.polygons)} faces")
    return body


def build_hands(m):
    obs = []
    for L in limbs():
        palm, fingers = digits(L)
        parts = [lumpy_sphere("Palm", palm + P(0, 0, -0.01), R(0.045, 0.018, 0.065), None, 50 + L["s"], amp=0.06, subdiv=3)]
        parts.append(chain("Wrist", [L["foot"] + P(0, 0.05, 0.02), L["foot"] + P(0, 0.02, -0.01), palm + P(0, 0.008, 0.01)], [0.026, 0.026, 0.024]))
        for k, pts in enumerate(fingers):
            parts.append(chain(f"Finger{k}", [palm.lerp(pts[0], 0.5)] + pts, [0.012, 0.012, 0.0105, 0.009, 0.007]))
            for q in pts[1:-1]:
                parts.append(lumpy_sphere("Knuckle", q, (0.011, 0.011, 0.011), None, 60 + k, amp=0.08, subdiv=2))
        hand = join(parts, f"Hand{L['kind']}{L['s']}")
        remesh(hand, 0.0028, 3)
        hand.data.materials.clear()
        hand.data.materials.append(m["skin"])
        nails = []
        for k, pts in enumerate(fingers):
            tip, prev = pts[-1], pts[-2]
            d = (tip - prev).normalized()
            n = chain(f"Nail{k}", [tip - d * 0.006, tip + d * 0.012, tip + d * 0.02 + P(0, -0.008, 0)], [0.006, 0.004, 0.0008])
            n.data.materials.append(m["nail"])
            nails.append(n)
        obs.append((L, hand, nails, fingers))
    return obs


def build_head(m):
    """The skull, long and hairless but for the strands; the sockets sunk to hollows; the jaw unhinged."""
    # the skull, in its own frame at HEAD: x across, the face forward-down (the game's -z, -y)
    face = P(0, -0.35, -1).normalized()
    up = P(0, 1, -0.2).normalized()
    side = face.cross(up).normalized()
    up = side.cross(face).normalized()

    def H(x, y, z):
        """A point in the head's frame: x to its side, y up its crown, z out of its face."""
        return HEAD + side * x + up * y + face * z
    skull = lumpy_sphere("Skull", (0, 0, 0), (0.09, 0.105, 0.12), None, 31, amp=0.04, subdiv=4)
    for v in skull.data.vertices:
        c = V(v.co)
        v.co = H(c.x, c.z, c.y)
    parts = [skull]
    parts.append(lumpy_sphere("Cheeks", H(0, -0.045, 0.06), R(0.065, 0.045, 0.05), None, 34, amp=0.05, subdiv=3))
    head = join(parts, "Head")
    remesh(head, 0.003, 3)
    # the sockets: pushed deep
    bm = bmesh.new()
    bm.from_mesh(head.data)
    for v in bm.verts:
        q = V(v.co) - HEAD
        lx, ly, lz = q.dot(side), q.dot(up), q.dot(face)
        if lz < 0.05:
            continue
        for sx in (-0.035, 0.035):
            d = ((lx - sx) / 0.025) ** 2 + ((ly - 0.012) / 0.021) ** 2
            if d < 1.0:
                v.co = V(v.co) - face * 0.03 * (1 - d) ** 0.5
    bm.to_mesh(head.data)
    bm.free()
    head.data.materials.clear()
    head.data.materials.append(m["skin"])
    # the jaw, hanging open far too low (dislocated): a bar of bone round under the face, and its teeth
    # (a mandible: a curved shelf of bone under skin, hinged far back, hanging open far too low)
    jparts = []
    for sx in (-1, 1):
        jparts.append(chain(f"Ramus{sx}", [H(sx * 0.062, -0.03, -0.01), H(sx * 0.06, -0.1, 0.02), H(sx * 0.05, -0.16, 0.06)], [0.016, 0.018, 0.02]))
    jparts.append(lumpy_sphere("Chin", H(0, -0.19, 0.07), R(0.05, 0.03, 0.04), None, 36, amp=0.06, subdiv=3))
    jaw = join(jparts, "Jaw")
    remesh(jaw, 0.003, 3)
    jaw.data.materials.clear()
    jaw.data.materials.append(m["skin"])
    mouth = lumpy_sphere("Mouth", H(0, -0.11, 0.04), R(0.042, 0.06, 0.03), m["dark"], 35, amp=0.1, subdiv=3)
    teeth = []
    for row, (ya, za, ln) in (("upper", (-0.045, 0.085, -1)), ("lower", (-0.185, 0.085, 1))):
        for i in range(11):
            x = -0.042 + 0.084 * i / 10 + rnd.uniform(-0.003, 0.003)
            curve = 0.02 * (x / 0.042) ** 2
            base = H(x, ya + (0.0 if row == "upper" else 0.0), za - curve)
            tip = base + up * ln * rnd.uniform(0.018, 0.035) + side * rnd.uniform(-0.004, 0.004)
            t = cylinder(f"Tooth{row}{i}", base, tip, rnd.uniform(0.003, 0.0045), 0.0004, 6, m["tooth"])
            teeth.append((t, "head" if row == "upper" else "jaw"))
    # its hair: long black strands off the scalp, hanging straight down past the face, wet and clumped
    bm = bmesh.new()
    for k in range(70):
        a = rnd.uniform(-1.3, 1.3)
        b = rnd.uniform(-0.2, 1.1)
        root = H(math.sin(a) * 0.085, 0.06 + 0.04 * math.cos(b), -0.04 + 0.08 * math.sin(b) * 0.5)
        ln = rnd.uniform(0.25, 0.55)
        pts = [root]
        cur = root
        wob = rnd.uniform(0, 6.28)
        for j in range(7):
            u = (j + 1) / 7
            cur = cur + P(math.sin(wob + u * 3) * 0.008, -ln / 7, math.cos(wob + u * 2) * 0.006) + side * (math.sin(a) * 0.01 * (1 - u))
            pts.append(cur)
        w = rnd.uniform(0.012, 0.022)
        out = (root - HEAD).normalized()
        W.ribbon(bm, pts, [w, w * 0.9, w * 0.8, w * 0.7, w * 0.55, w * 0.4, w * 0.25, 0.002], out)
    hair = obj_from_bmesh(bm, "Hair", m["hair"])
    m["hair"].use_backface_culling = False
    return head, jaw, mouth, teeth, hair


# ------------------------------------------------------------------ colours (linear)

def skin_colour(co, nrm):
    g = G(co)
    n1 = fbm(g.x * 6, g.y * 6, g.z * 6, 4)
    n2 = fbm(g.x * 22 + 3, g.y * 22, g.z * 22, 3)
    c = V((0.52, 0.5, 0.46)) * (0.85 + 0.25 * n1)
    # veins: thin dark blue-grey lines under the pale
    vein = abs(noise.noise(V((g.x * 14, g.y * 14, g.z * 14))))
    if vein < 0.035:
        c = c.lerp(V((0.2, 0.22, 0.3)), 0.55 * (1 - vein / 0.035))
    # bruised at the joints and the spine, grimed on the hands, the feet, the knees and the elbows
    low = min(max((-g.y - 0.45) / 0.25, 0.0), 1.0)
    c = c.lerp(V((0.12, 0.1, 0.08)), low * 0.7)
    bruise = max(0.0, fbm(g.x * 3 + 7, g.y * 3, g.z * 3, 3) - 0.15) * 1.4
    c = c.lerp(V((0.28, 0.22, 0.28)), min(bruise, 0.5))
    c *= 0.9 + 0.15 * n2
    if nrm.z < -0.5:
        c *= 0.8
    return c.x, c.y, c.z


def flat(rgb):
    return lambda co, nrm: rgb


def preview(name, target, dist, height, yaw, textured=False):
    sc = bpy.context.scene
    if textured:
        kit.preview(name, target, dist, height, yaw)
        return
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "SINGLE"
    sh.single_color = (0.6, 0.58, 0.55)
    sh.show_shadows = True
    sh.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 900, 700
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
    t = V(target)
    a = math.radians(yaw)
    cam.location = t + V((math.sin(a) * dist, math.cos(a) * dist, height))
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 40
    sc.camera = cam
    sc.render.filepath = os.path.join(PREV, name + ".png")
    bpy.ops.render.render(write_still=True)


def bone_list(hands):
    B = [("root", V((0, 0, 0)), P(0, 0, -0.2), None, 0.0)]
    B.append(("hips", P(0, 0.02, 0.44), P(0, 0.05, 0.12), "root", 0.15))
    B.append(("spine", P(0, 0.05, 0.12), P(0, 0.07, -0.18), "hips", 0.13))
    B.append(("chest", P(0, 0.07, -0.18), NECK0, "spine", 0.18))
    B.append(("neck", NECK0, NECK1, "chest", 0.05))
    B.append(("head", NECK1, HEAD + P(0, -0.06, -0.12), "neck", 0.1))
    B.append(("jaw", HEAD + P(0, -0.03, -0.02), HEAD + P(0, -0.2, -0.1), "head", 0.03))
    for L, hand, nails, fingers in hands:
        k, sx = L["kind"], L["sx"]
        parent = "chest" if k == "arm" else "hips"
        palm, _ = digits(L)
        B.append((f"{k}_upper_{sx}", L["root"], L["knee"], parent, 0.06))
        B.append((f"{k}_lower_{sx}", L["knee"], L["foot"], f"{k}_upper_{sx}", 0.05))
        B.append((f"{k}_hand_{sx}", L["foot"], palm + P(0, 0, -0.03), f"{k}_lower_{sx}", 0.03))
        for i, pts in enumerate(fingers):
            B.append((f"{k}_f{i}_{sx}", pts[0], pts[-1], f"{k}_hand_{sx}", 0.012))
    return B


def main():
    clear()
    m = materials()
    body = build_body(m)
    hands = build_hands(m)
    head, jaw, mouth, teeth, hair = build_head(m)
    W.paint(body, skin_colour); W.detail_attr(body, 1.0); W.tag(body, "flesh")
    W.paint(head, skin_colour); W.detail_attr(head, 0.8); W.tag(head, "head")
    W.paint(jaw, skin_colour); W.detail_attr(jaw, 0.6); W.tag(jaw, "jaw")
    W.paint(mouth, flat((0.015, 0.006, 0.006))); W.detail_attr(mouth, 0.0); W.tag(mouth, "head")
    W.paint(hair, flat((0.012, 0.011, 0.012))); W.detail_attr(hair, 0.2); W.tag(hair, "head")
    rigid = [head, jaw, mouth, hair]
    for t, bone in teeth:
        W.paint(t, flat((0.5, 0.45, 0.33))); W.detail_attr(t, 0.3); W.tag(t, bone)
        rigid.append(t)
    flesh_hands = []
    for L, hand, nails, fingers in hands:
        W.paint(hand, skin_colour); W.detail_attr(hand, 1.0); W.tag(hand, "flesh")
        flesh_hands.append(hand)
        for i, n in enumerate(nails):
            W.paint(n, flat((0.02, 0.016, 0.014))); W.detail_attr(n, 0.1); W.tag(n, f"{L['kind']}_f{i}_{L['sx']}")
            rigid.append(n)

    def copy(o):
        c = o.copy()
        c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        return c
    high = join([copy(o) for o in [body] + flesh_hands + rigid], "High")
    for o, target in [(body, 38000)] + [(h, 3500) for h in flesh_hands] + [(head, 7000)]:
        add_mod(o, "DECIMATE", ratio=min(1.0, target / max(W.tri_count(o), 1)))
        apply_mods(o)
    low = join([body] + flesh_hands + rigid, "CrawlerMesh")
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0015)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[crawler] low {W.tri_count(low)} triangles, high {W.tri_count(high)}")
    W.PREV = PREV
    albedo, normal = W.bake_maps(low, high, "crawler", TEXSIZE)
    bpy.data.objects.remove(high, do_unlink=True)
    W.final_materials(low, albedo, normal)
    bones = bone_list(hands)
    arm = W.make_armature("Crawler", bones)
    W.skin_weights(low, arm, bones)
    for vg in list(low.vertex_groups):
        if vg.name.startswith("P_"):
            low.vertex_groups.remove(vg)
    path = os.path.join(OUT, "crawler_rig.glb")
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=False,
                              export_animations=False, export_skins=True, export_image_format="AUTO", export_materials="EXPORT")
    print(f"[crawler] exported {path}: {W.tri_count(low)} triangles, {len(bones)} bones")
    preview("crawler_side", (0, 0, 0.0), 2.6, 0.5, 90)
    preview("crawler_front", (0, -0.2, 0.0), 2.2, 0.3, 180)
    preview("crawler_face", tuple(HEAD + P(0, -0.05, -0.05)), 0.7, -0.1, 180)
    preview("crawler_above", (0, 0, 0), 2.6, 1.8, 40)
    preview("crawler_hand", tuple(limbs()[0]["foot"]), 0.6, 0.3, 30)
    preview("crawler_face_tex", tuple(HEAD + P(0, -0.05, -0.05)), 0.7, -0.1, 180, textured=True)


if __name__ == "__main__":
    main()
