"""The player's car (the owner, 2026-10-15: "make the car look less blocky and more real but keep it PS2 style"), modelled
here and exported to assets/models/car/car.glb with its small textures beside it:

    blender -b --python tools/Blender/car.py [-- --preview]

A plain mid-90s Japanese estate in faded burgundy, after the second-generation Subaru Legacy wagon (1994-98: 4.68 x 1.70
x 1.49 m on a 2.63 m wheelbase, 195/65 R15 tyres, about 15 cm under the sills), kept to the game's 4.6 m so the opening,
the trunk and the endings line up as they did. Backed into its bay with the tailgate lifted.

The look is a PS2 car's (Silent Hill 2, San Andreas): about 5-6 k triangles; one smooth-shaded skin lofted through
cross-sections along the car, with hard creases only where a real car has them (the shoulder, the belt, the windscreen's
foot and head); small soft textures (lamps, grille, plate, gauges and stack in one atlas; tyre, hubcap, grain, cloth);
the paint, its sun-fade, the mud, the dust and an ambient occlusion baked into the vertex colours (the game's materials
multiply them in).

Built in the GAME's frame (x right, y up, z toward the tailgate; origin on the ground at the car's middle) and turned
into Blender's (x, -z, y) as the meshes are made, so the export (Y up, Blender's +Y the game's -Z) lands it back.
What the game relies on, unchanged: the load floor at y 0.6 to the tailgate's lip at z 2.3, the driver's eye at
(-0.38, 1.15, 0.25) behind the wheel, the roof under 1.45, the two collision boxes (ParkProp.Car).
Three objects: car_body, car_tailgate (lifted on its hinge), car_wheels. Material names are the game's roles.
"""
import bpy, math, os, sys
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kit import fbm, ROOT

OUT = os.path.join(ROOT, "assets", "models", "car")
PREV = os.path.join(ROOT, "build", "blender", "car")
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREV, exist_ok=True)

# ------------------------------------------------------------------ the dimensions (metres, the game's frame)

ZN, ZT = -2.3, 2.3                      # the nose, the tail (the tailgate's lip)
AXLE_Y, TYRE_R, ARCH_R = 0.335, 0.326, 0.39
ZFA, ZRA = -1.36, 1.27                  # the axles: a 2.63 m wheelbase
WHEELS = (ZFA, ZRA)
WHEEL_X = 0.734                         # the wheels' centre planes (a 1.47 m track)
ZC, ZH = -0.95, -0.06                   # the windscreen's foot (the cowl) and head (the header)
ZB0, ZB1 = 0.304, 0.40                  # the B pillar
ZC0, ZC1 = 1.076, 1.16                  # the C pillar
ZD0 = 1.98                              # the D pillar (the quarter glass ends)
FLOOR_Y = 0.6                           # the load floor (the camera lies on it)
SHUT = (-0.87, 0.30, 1.08)              # the doors' shut lines (front door's front, the B, the rear door's back)
SEAT_Z = 1.15                           # the rear seat's back; the load space behind it


def clamp(v, a=0.0, b=1.0):
    return a if v < a else b if v > b else v


def lerp(a, b, t):
    return a + (b - a) * t


def sstep(a, b, x):
    t = clamp((x - a) / (b - a))
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ the body's shape: plan, profile, section

def hw_max(z):
    """The body's half-width at the shoulder: the nose and the tail rounded in plan, the nose a little narrower."""
    hw = 0.865 - 0.02 * clamp((-1.0 - z) / 1.3)
    rn = 0.34
    if z < ZN + rn:
        d = (ZN + rn - z) / rn
        hw -= 0.16 * (1 - math.sqrt(max(0.0, 1 - d * d)))
    rt = 0.16
    if z > ZT - rt:
        d = (z - (ZT - rt)) / rt
        hw -= 0.05 * (1 - math.sqrt(max(0.0, 1 - d * d)))
    return hw


def in_arch(z):
    return any(abs(z - wz) <= ARCH_R + 1e-6 for wz in WHEELS)


def y_bottom(z):
    """The bottom edge of the side: round the wheel arches, else the sill; the overhangs lift toward the bumpers."""
    for wz in WHEELS:
        dz = z - wz
        if abs(dz) <= ARCH_R + 1e-6:
            return AXLE_Y + math.sqrt(max(0.0, ARCH_R * ARCH_R - dz * dz))
    y = 0.25
    if z < ZFA - ARCH_R:
        y += (ZFA - ARCH_R - z) * 0.11
    if z > ZRA + ARCH_R:
        y += (z - ZRA - ARCH_R) * 0.09
    return y


def y_bonnet(z):
    """The bonnet's height along its middle: low at the nose, rising and flattening to the scuttle."""
    t = clamp((z - ZN) / (ZC - 0.02 - ZN))
    return 0.715 + 0.185 * (1 - (1 - t) ** 1.7)


def y_belt(z):
    if z < ZC - 1e-4:
        return y_bonnet(z) - 0.03
    return 0.885 + 0.05 * clamp((z - ZC) / (ZD0 - ZC))


def y_shoulder(z):
    return 0.755 + 0.05 * sstep(ZN, -1.0, z) + 0.035 * clamp((z + 1.0) / 3.3)


def y_roof(z):
    return 1.385 + 0.012 * math.sin(math.pi * clamp((z - ZH) / (ZT - ZH))) - 0.022 * sstep(1.85, 2.3, z)


def lean(y):
    """The nose's face leans back toward its top."""
    return 0.075 * sstep(0.42, 0.72, y)


def rake(y):
    """The tail leans in toward the roof (the tailgate's glass is raked)."""
    return 0.10 * sstep(0.85, 1.42, y)


def z_shift(z, y):
    return lean(y) * clamp((ZN + 0.26 - z) / 0.26) - rake(y) * clamp((z - (ZT - 0.36)) / 0.36)


def rows_left(z):
    """The section at z, the left half from under the sill up to the roof's crown: 13 (half-width, height) rows.
      0 the sill's tuck under (or the arch's lip), 1 the bottom edge, 2-3 the flank (widest at 3), 4 the shoulder
      crease, 5 above it, 6 the belt (the door's top; at the front the wing's top edge), 7 the glass's foot, 8 its head,
      9 the frame over it, 10 the roof's edge, 11 half way in, 12 the crown. Ahead of the windscreen rows 7-12 lie
      across the bonnet; along the windscreen they climb its pillar and cross the glass."""
    hw = hw_max(z)
    yb = y_bottom(z)
    yfe = y_belt(z)
    ysh = min(y_shoulder(z), yfe - 0.065)
    hwb = hw - 0.05
    R = []
    if in_arch(z):
        R += [(hw - 0.065, yb + 0.012), (hw - 0.012, yb)]
    else:
        R += [(hw - 0.10, yb - 0.035), (hw - 0.03, yb)]
    R += [(hw - 0.006, lerp(yb, ysh, 0.33)), (hw, lerp(yb, ysh, 0.7)), (hw - 0.003, ysh), (hw - 0.022, lerp(ysh, yfe, 0.55)),
          (hwb, yfe)]
    if z < ZC - 1e-4:
        # the bonnet, crowned across
        def hb(x):
            return y_bonnet(z) - 0.035 * (x / hwb) ** 2
        R.append((hwb - 0.014, yfe + 0.008))
        for x in (hwb - 0.035, hwb - 0.05, hwb - 0.08, (hwb - 0.08) * 0.5, 0.0):
            R.append((x, hb(x)))
    elif z < ZH - 1e-4:
        # the windscreen: up its pillar, across the glass
        s = (z - ZC) / (ZH - ZC)
        yr = y_roof(ZH)
        hwc = hwb - 0.14
        x7 = hwb - 0.016
        yA = lerp(yfe + 0.015, yr - 0.075, s)
        x8 = lerp(x7, hwc, s)
        x10, y10 = x8 - 0.055, yA + 0.02 + 0.055 * s
        R += [(x7, yfe + 0.012), (x8, max(yA, yfe + 0.013)), (x8 - 0.014, yA + 0.012 + 0.025 * s), (x10, y10),
              (x10 * 0.5, y10 + 0.028 * 0.75), (0.0, y10 + 0.028)]
    else:
        # the glasshouse leaning in over the belt to the roof
        yr = y_roof(z)
        hwc = hwb - 0.14
        R += [(hwb - 0.016, yfe + 0.012), (hwc, yr - 0.075), (hwc - 0.014, yr - 0.038), (hwc - 0.055, yr),
              ((hwc - 0.055) * 0.5, yr + 0.036 * 0.75), (0.0, yr + 0.036)]
    return R


def stations():
    s = set()
    for k in range(6):
        s.add(ZN + 0.34 * (1 - math.cos(k / 5 * math.pi / 2)))
    for wz in WHEELS:
        for k in range(9):
            s.add(wz - ARCH_R * math.cos(math.pi * k / 8))
        s.add(wz - ARCH_R - 0.012)
        s.add(wz + ARCH_R + 0.012)
    for sz in SHUT:
        s.add(sz - 0.004)
        s.add(sz + 0.004)
    s |= {ZC - 0.02, ZC, ZC + 0.035, ZH - 0.035, ZH, ZB0, ZB1, ZC0, ZC1, ZD0}
    for k in range(5):
        s.add(ZT - 0.16 * (1 - math.cos(k / 4 * math.pi / 2)))
    zs = sorted(s)
    out = [zs[0]]
    for z in zs[1:]:
        gap = z - out[-1]
        if gap < 0.003:
            continue
        n = int(math.ceil(gap / 0.32))
        z0 = out[-1]
        for k in range(1, n):
            out.append(z0 + gap * k / n)
        out.append(z)
    return out


ZS = stations()
ROWS = [rows_left(z) for z in ZS]
NI = len(ZS)


def P(i, k):
    """Station i's point k on the full section (0-12 the left from the sill up to the crown, 13-24 the right back down)."""
    j = k if k <= 12 else 24 - k
    x, y = ROWS[i][j]
    if k < 12:
        x = -x
    z = ZS[i]
    return Vector((x, y, z + z_shift(z, y)))


def side_x(i, y):
    """The body's half-width at height y on station i (up its side, rows 1-9)."""
    L = ROWS[i]
    if y <= L[1][1]:
        return L[1][0]
    for j in range(1, 9):
        (x0, y0), (x1, y1) = L[j], L[j + 1]
        if y0 <= y <= y1 and y1 > y0 + 1e-6:
            return lerp(x0, x1, (y - y0) / (y1 - y0))
    return L[9][0]


def top_y(z, x):
    """The top's height at (x, z): the bonnet, the windscreen or the roof, between the stations."""
    x = abs(x)
    i = max(0, min(NI - 2, next((n for n in range(NI - 1) if ZS[n + 1] >= z), NI - 2)))
    t = clamp((z - ZS[i]) / max(ZS[i + 1] - ZS[i], 1e-6))

    def at(n):
        L = ROWS[n]
        for j in range(9, 12):
            (x0, y0), (x1, y1) = L[j], L[j + 1]
            if x1 <= x <= x0:
                return lerp(y0, y1, (x0 - x) / max(x0 - x1, 1e-6))
        return L[9][1] if x > L[9][0] else L[12][1]
    return lerp(at(i), at(i + 1), t)


def station_near(z):
    return min(range(NI), key=lambda n: abs(ZS[n] - z))


# ------------------------------------------------------------------ the mesh builder (game frame)

class Part:
    """Vertices and faces in the game's frame; each face a role (the material), its UVs if it has its own (else the
    box projection later), a tint (the vertex colour's base, where a role's own isn't wanted)."""

    def __init__(self, name):
        self.name = name
        self.V = []
        self.F = []

    def v(self, p):
        self.V.append(Vector(p))
        return len(self.V) - 1

    def face(self, idx, role, uvs=None, tint=None):
        # drop corners that sit on their neighbours (the degenerate edges of a collapsing grid)
        keep = []
        for n, a in enumerate(idx):
            b = idx[(n + 1) % len(idx)]
            if (self.V[a] - self.V[b]).length > 1e-6:
                keep.append(n)
        if len(keep) < 3:
            return
        idx = [idx[n] for n in keep]
        if uvs is not None:
            uvs = [uvs[n] for n in keep]
        self.F.append((tuple(idx), role, uvs, tint))

    def poly(self, pts, role, out=None, uvs=None, tint=None, share=False):
        pts = [Vector(p) for p in pts]
        if out is not None:
            n = Vector()
            for k in range(len(pts)):
                n += pts[k].cross(pts[(k + 1) % len(pts)])
            if n.dot(Vector(out)) < 0:
                pts.reverse()
                if uvs is not None:
                    uvs = list(reversed(uvs))
        self.face([self.v(p) for p in pts], role, uvs, tint)

    def fan(self, pts, role, out, tint=None):
        """A flat cap: a fan from its middle (shared vertices: one smooth piece)."""
        c = sum((Vector(p) for p in pts), Vector()) / len(pts)
        n = Vector()
        for k in range(len(pts)):
            n += Vector(pts[k]).cross(Vector(pts[(k + 1) % len(pts)]))
        if n.dot(Vector(out)) < 0:
            pts = list(reversed(pts))
        ci = self.v(c)
        idx = [self.v(p) for p in pts]
        for k in range(len(idx)):
            self.face([ci, idx[k], idx[(k + 1) % len(idx)]], role, tint=tint)

    def grid(self, G, role, out, uv=None, tint=None, closed_u=False, closed_v=False):
        """G[i][j] a grid of points: quads between them, one smooth piece, turned to face <out> (a vector, or a function
        of the quad's middle). role: a name, or a function (i, j) -> name or None (no face). uv: (i, j) -> (u, v)."""
        nu, nv = len(G), len(G[0])
        I = [[self.v(G[i][j]) for j in range(nv)] for i in range(nu)]
        iu = nu if closed_u else nu - 1
        jv = nv if closed_v else nv - 1
        score = 0.0
        quads = []
        for i in range(iu):
            for j in range(jv):
                i1, j1 = (i + 1) % nu, (j + 1) % nv
                a, b, c, d = G[i][j], G[i1][j], G[i1][j1], G[i][j1]
                n = (Vector(b) - Vector(a)).cross(Vector(d) - Vector(a)) + (Vector(d) - Vector(c)).cross(Vector(b) - Vector(c))
                mid = (Vector(a) + Vector(b) + Vector(c) + Vector(d)) / 4
                o = out(mid) if callable(out) else Vector(out)
                score += n.dot(o)
                quads.append((i, j, i1, j1))
        flip = score < 0
        for i, j, i1, j1 in quads:
            r = role(i, j) if callable(role) else role
            if r is None:
                continue
            # (unwrapped indices for the UVs: a closed grid's last column takes the next repeat, not the first's)
            corners = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            if flip:
                corners.reverse()
            uvs = [uv(a, b) for a, b in corners] if uv is not None else None
            self.face([I[a % nu][b % nv] for a, b in corners], r, uvs, tint)

    def lathe(self, centre, axis, profile, segs, role, uv=None, tint=None, closed_profile=False, out_sign=1.0, out=None):
        """Turned round <axis> through <centre>: profile [(a along the axis, r out from it)]. uv: (seg/segs, k) -> (u, v)."""
        ax = Vector(axis).normalized()
        u = ax.orthogonal().normalized()
        w = ax.cross(u)
        c = Vector(centre)
        G = []
        for s in range(segs):
            t = s / segs * math.tau
            d = u * math.cos(t) + w * math.sin(t)
            G.append([c + ax * a + d * r for a, r in profile])
        uvf = None
        if uv is not None:
            def uvf(i, j):
                return uv(i / segs, j)
        # outward: away from the axis (or toward it, out_sign -1), unless told
        def radial_out(m):
            rel = m - c
            radial = rel - ax * rel.dot(ax)
            return radial * out_sign if radial.length > 1e-6 else ax * out_sign
        self.grid(G, role, out if out is not None else radial_out, uvf, tint, closed_u=True, closed_v=closed_profile)

    def rbox(self, centre, size, role, r=0.0, rot=None, tint=None, out_hint=None):
        """A box, its edges rounded by r (one segment: smooth corners, PS2 cheap); r = 0: a plain box, flat."""
        from mathutils import Matrix
        M = rot if rot is not None else Matrix.Identity(3)
        h = Vector(size) / 2
        c = Vector(centre)
        if r <= 0:
            for ax in range(3):
                for sgn in (-1, 1):
                    n = Vector((0, 0, 0))
                    n[ax] = sgn
                    a1, a2 = (ax + 1) % 3, (ax + 2) % 3
                    pts = []
                    for s1, s2 in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                        p = Vector((0, 0, 0))
                        p[ax] = sgn * h[ax]
                        p[a1] = s1 * h[a1]
                        p[a2] = s2 * h[a2]
                        pts.append(c + M @ p)
                    self.poly(pts, role, out=M @ n, tint=tint)
            return
        hi = Vector([max(h[k] - r, 1e-4) for k in range(3)])
        # a chamfered box, smooth-shaded (44 triangles): each corner split into three points, one on each face
        V, L = {}, {}
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    s = (sx, sy, sz)
                    for a in range(3):
                        p = Vector([s[k] * (h[k] if k == a else hi[k]) for k in range(3)])
                        L[(s, a)] = p
                        V[(s, a)] = self.v(c + M @ p)

        def put(keys):
            pts = [L[k] for k in keys]
            n = Vector()
            for k in range(len(pts)):
                n += pts[k].cross(pts[(k + 1) % len(pts)])
            if n.dot(sum(pts, Vector()) / len(pts)) < 0:
                keys = list(reversed(keys))
            self.face([V[k] for k in keys], role, tint=tint)
        for ax in range(3):
            a1, a2 = (ax + 1) % 3, (ax + 2) % 3
            for sgn in (-1, 1):
                ring = []
                for s1, s2 in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    s = [0, 0, 0]
                    s[ax], s[a1], s[a2] = sgn, s1, s2
                    ring.append((tuple(s), ax))
                put(ring)
        for cax in range(3):
            a, b = (cax + 1) % 3, (cax + 2) % 3
            for sa in (-1, 1):
                for sb in (-1, 1):
                    def cs(sc):
                        s = [0, 0, 0]
                        s[cax], s[a], s[b] = sc, sa, sb
                        return tuple(s)
                    put([(cs(-1), a), (cs(1), a), (cs(1), b), (cs(-1), b)])
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    put([((sx, sy, sz), 0), ((sx, sy, sz), 1), ((sx, sy, sz), 2)])

    def beam(self, a, b, w, h, role, up=(0, 1, 0), tint=None):
        """A thin bar from a to b, w across and h deep (its height along <up>)."""
        a, b = Vector(a), Vector(b)
        d = (b - a).normalized()
        upv = Vector(up)
        side = d.cross(upv).normalized() * (w / 2)
        upv = side.cross(d).normalized() * (h / 2)
        corners = [side + upv, -side + upv, -side - upv, side - upv]
        G = [[a + c for c in corners], [b + c for c in corners]]
        mid = (a + b) / 2
        self.grid(G, role, lambda m: m - (mid + d * (m - mid).dot(d)), tint=tint, closed_v=True)
        self.poly([a + c for c in corners], role, out=-d, tint=tint)
        self.poly([b + c for c in corners], role, out=d, tint=tint)


# ------------------------------------------------------------------ the atlas: where each thing's picture is

def rect(u0, v0, u1, v1, px=256, inset=2):
    e = inset / px
    return (u0 + e, v0 + e, u1 - e, v1 - e)


HEAD = rect(0, 0.5, 0.5, 1.0)
TAIL = rect(0.5, 0.5, 1.0, 1.0)
GRILLE = rect(0, 0.25, 0.5, 0.5)
PLATE = rect(0.5, 0.25, 1.0, 0.5)
GAUGES = rect(0, 0, 0.5, 0.125)
RADIO = rect(0.5, 0, 0.75, 0.25)
VENT = rect(0.75, 0, 1.0, 0.125)
AMBER = rect(0, 0.125, 0.125, 0.25)


def in_rect(R, s, t):
    return (lerp(R[0], R[2], s), lerp(R[1], R[3], t))


# ------------------------------------------------------------------ the body

def role_top(i, band):
    """What the top's quad between stations i, i+1 and rows band, band+1 is: paint, black trim, glass or a shut line."""
    if True:
        zm = (ZS[i] + ZS[i + 1]) / 2
        if zm < ZC - 0.021:
            return "car_gap" if band == 6 else "car_paint"
        if zm < ZC:
            return "car_trim"
        if zm < ZH:
            if band == 6:
                return "car_trim"
            if band == 7:
                return "car_trim" if zm < ZC + 0.16 else "car_glass"
            if band in (8, 9):
                return "car_paint"
            return "car_trim" if (zm < ZC + 0.035 or zm > ZH - 0.035) else "car_glass"
        if band == 6:
            return "car_trim"
        if band == 7:
            if ZB0 < zm < ZB1 or ZC0 < zm < ZC1:
                return "car_trim"
            return "car_paint" if zm > ZD0 else "car_glass"
        if band == 8:
            return "car_paint" if zm > ZD0 else "car_trim"
        return "car_paint"


def body(B):
    out_shell = lambda m: m - Vector((0, 0.55, clamp(m.z, -1.7, 1.7)))

    def role_side(i, band):
        zm = (ZS[i] + ZS[i + 1]) / 2
        if band >= 1 and any(abs(zm - sz) < 0.0045 for sz in SHUT):
            return "car_gap"
        return "car_paint"

    # the sides: below the shoulder's crease, above it to the belt (left, then the right)
    for k0, k1 in ((0, 4), (4, 6), (18, 20), (20, 24)):
        G = [[P(i, k) for k in range(k0, k1 + 1)] for i in range(NI)]

        def rl(i, j, k0=k0):
            k = k0 + j
            band = k if k < 12 else 23 - k
            return role_side(i, band)
        B.grid(G, rl, out_shell)
    # the top, across the car from belt to belt, in its pieces (creased at the scuttle and the windscreen's head)
    cuts = [0, station_near(ZC - 0.02), station_near(ZC), station_near(ZH), NI - 1]
    for a, b in zip(cuts, cuts[1:]):
        G = [[P(i, k) for k in range(6, 19)] for i in range(a, b + 1)]

        def rt(i, j, a=a):
            k = 6 + j
            band = k if k < 12 else 23 - k
            return role_top(a + i, band)
        B.grid(G, rt, out_shell)

    # the nose: the section's end closed across (the grille, the lamps and the bumper go over it)
    nose = [P(0, k) for k in range(25)]
    B.fan(nose, "car_paint", (0, 0, -1))

    # the tail: closed below the load floor's lip; above it, the frame round the tailgate's opening and its jamb
    last = NI - 1
    O = [P(last, k) for k in range(25)]

    def cross_at(seq, y):
        for n in range(len(seq) - 1):
            a, b = seq[n], seq[n + 1]
            if (a.y - y) * (b.y - y) <= 0 and abs(b.y - a.y) > 1e-6:
                return n, a.lerp(b, (y - a.y) / (b.y - a.y))
        return None, None
    nl, pl = cross_at(O[:13], FLOOR_Y)
    nr_rev, pr = cross_at(list(reversed(O[12:])), FLOOR_Y)
    nr = 24 - nr_rev   # index on O of the point just below the crossing on the right
    lower = [pl] + [O[k] for k in range(nl, -1, -1)] + [O[k] for k in range(24, nr - 1, -1)] + [pr]
    B.fan(lower, "car_paint", (0, 0, 1))
    U = [pl] + [O[k] for k in range(nl + 1, nr)] + [pr]
    # the inner outline: each point in from the outer by the frame's width (wide beside the lamps, narrow at the top)
    I = []
    for n, p in enumerate(U):
        a = U[max(n - 1, 0)]
        b = U[min(n + 1, len(U) - 1)]
        t = Vector((b.x - a.x, b.y - a.y, 0)).normalized()
        nin = Vector((-t.y, t.x, 0))
        if nin.dot(Vector((-p.x, 1.0 - p.y, 0))) < 0:
            nin = -nin
        if n == 0 or n == len(U) - 1:
            nin = Vector((-math.copysign(1, p.x), 0, 0))
        w = lerp(0.125, 0.065, sstep(0.95, 1.08, p.y))
        q = p + nin * w
        I.append(Vector((q.x, q.y, ZT + z_shift(ZT, q.y))))
    B.grid([U, I], "car_paint", (0, 0, 1))
    J = [q + Vector((0, 0, -0.08)) for q in I]
    B.grid([I, J], "car_paint", lambda m: Vector((0, 1.0, m.z)) - m)
    global APERTURE
    APERTURE = I

    # the wheel arches' liners and the tubs' inner walls, the underside, the sills turned under to it
    for wz in WHEELS:
        for side in (-1, 1):
            n = 14
            ring = []
            for s in range(n + 1):
                a = math.pi * s / n
                z = wz + ARCH_R * math.cos(a)
                y = AXLE_Y + ARCH_R * math.sin(a)
                ring.append((z, y))
            ring = [(wz + ARCH_R, 0.24)] + ring + [(wz - ARCH_R, 0.24)]
            xo = side * (hw_max(wz) - 0.06)
            xi = side * 0.58
            G = [[Vector((xo, y, z)), Vector((xi, y, z))] for z, y in ring]
            B.grid(G, "car_gap", lambda m, wz=wz: Vector((0, AXLE_Y, wz)) - Vector((0, m.y, m.z)))
            B.poly([Vector((xi, y, z)) for z, y in ring], "car_gap", out=(side, 0, 0))
    B.poly([(-0.6, 0.205, -2.12), (0.6, 0.205, -2.12), (0.6, 0.205, 2.18), (-0.6, 0.205, 2.18)], "car_gap", out=(0, -1, 0))
    runs, cur = [], []
    for i in range(NI):
        if not in_arch(ZS[i]):
            cur.append(i)
        elif cur:
            runs.append(cur)
            cur = []
    if cur:
        runs.append(cur)
    for run in runs:
        if len(run) < 2:
            continue
        for side in (-1, 1):
            k = 0 if side < 0 else 24
            G = [[P(i, k), Vector((side * 0.6, 0.205, P(i, k).z))] for i in run]
            B.grid(G, "car_gap", (0, -1, 0))


# ------------------------------------------------------------------ strips laid over the body (lamps, bumpers, mouldings)

def nose_path(y, z_from, x_to=None, both=False, n_cap=6):
    """At height y: up the left side from z_from to the nose, across the nose's face to x_to (or round to the right
    side and back to z_from). Points (x, z) with their outward normals in plan."""
    idx = [i for i in range(NI) if ZS[i] <= z_from + 1e-6]
    idx.sort(reverse=True)
    pts = [(-side_x(i, y), ZS[i] + z_shift(ZS[i], y)) for i in idx]
    x0 = side_x(0, y)
    zc = ZS[0] + z_shift(ZS[0], y)
    xe = x_to if x_to is not None else x0
    for k in range(1, n_cap + (0 if both else 1)):
        pts.append((lerp(-x0, xe, k / n_cap), zc))
    if both:
        pts += [(side_x(i, y), ZS[i] + z_shift(ZS[i], y)) for i in reversed(idx)]
    return pts


def tail_path(y, z_from, x_to=None, both=False, n_cap=6):
    idx = [i for i in range(NI) if ZS[i] >= z_from - 1e-6]
    pts = [(-side_x(i, y), ZS[i] + z_shift(ZS[i], y)) for i in idx]
    xl = side_x(NI - 1, y)
    zc = ZT + z_shift(ZT, y)
    xe = x_to if x_to is not None else xl
    for k in range(1, n_cap + (0 if both else 1)):
        pts.append((lerp(-xl, xe, k / n_cap), zc))
    if both:
        pts += [(side_x(i, y), ZS[i] + z_shift(ZS[i], y)) for i in reversed(idx)]
    return pts


def plan_normals(pts, front):
    ns = []
    for n in range(len(pts)):
        a = pts[max(n - 1, 0)]
        b = pts[min(n + 1, len(pts) - 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]
        l = math.hypot(tx, tz) or 1.0
        nx, nz = (tz / l, -tx / l) if front else (-tz / l, tx / l)
        ns.append((nx, nz))
    return ns


def strip(B, path_fn, levels, role, front, uvr=None, flip_u=False, caps=False, tint=None):
    """levels [(out, y)]: at each height the plan path, pushed out along its normals; one grid up through them."""
    G = []
    for o, y in levels:
        pts = path_fn(y)
        ns = plan_normals(pts, front)
        G.append([Vector((x + nx * o, y, z + nz * o)) for (x, z), (nx, nz) in zip(pts, ns)])
    nv = len(G)
    # u along the path (its length), v up the levels
    L = [0.0]
    row = G[nv // 2]
    for n in range(1, len(row)):
        L.append(L[-1] + (row[n] - row[n - 1]).length)
    tot = L[-1] or 1.0
    uv = None
    if uvr is not None:
        def uv(j, i):
            s = L[i] / tot
            return in_rect(uvr, 1 - s if flip_u else s, j / (nv - 1))
    T = [[G[j][i] for i in range(len(G[0]))] for j in range(nv)]   # T[j][i]: j up the levels, i along the path
    centre = Vector((0, 0.45, 0))
    B.grid(T, role, lambda m: Vector((m.x, 0, m.z)) - Vector((0, 0, clamp(m.z, -1.5, 1.5))), uv, tint)
    if caps:
        for i in (0, len(G[0]) - 1):
            ring = [G[j][i] for j in range(nv)]
            d = G[0][i] - G[0][1 if i == 0 else i - 1]
            B.poly(ring, role, out=d, tint=tint)


def details(B):
    zfront = ZFA - ARCH_R - 0.012
    zrear = ZRA + ARCH_R + 0.012
    # ---- the bumpers, body-coloured, wrapped round to the arches; a black rubbing strip along each; plates
    fsec = [(-0.035, 0.235), (0.012, 0.25), (0.04, 0.29), (0.05, 0.345), (0.05, 0.40), (0.035, 0.455), (0.0, 0.478)]
    strip(B, lambda y: nose_path(y, zfront, both=True), fsec, "car_paint", True, caps=True)
    strip(B, lambda y: nose_path(y, zfront, both=True), [(0.058, 0.352), (0.064, 0.372), (0.058, 0.392)], "car_trim", True, caps=True)
    rsec = [(-0.03, 0.25), (0.015, 0.265), (0.045, 0.31), (0.055, 0.40), (0.055, 0.50), (0.042, 0.565), (0.0, 0.597)]
    strip(B, lambda y: tail_path(y, zrear, both=True), rsec, "car_paint", False, caps=True)
    strip(B, lambda y: tail_path(y, zrear, both=True), [(0.063, 0.42), (0.069, 0.44), (0.063, 0.46)], "car_trim", False, caps=True)
    # the plates (US: 30 x 15 cm), on the bumpers
    zf = ZN - 0.066
    B.poly([(0.152, 0.29, zf), (-0.152, 0.29, zf), (-0.152, 0.442, zf), (0.152, 0.442, zf)], "car_atlas", out=(0, 0, -1),
           uvs=[in_rect(PLATE, 0, 0), in_rect(PLATE, 1, 0), in_rect(PLATE, 1, 1), in_rect(PLATE, 0, 1)])
    zr = ZT + 0.074
    B.poly([(-0.152, 0.36, zr), (0.152, 0.36, zr), (0.152, 0.512, zr), (-0.152, 0.512, zr)], "car_atlas", out=(0, 0, 1),
           uvs=[in_rect(PLATE, 0, 0), in_rect(PLATE, 1, 0), in_rect(PLATE, 1, 1), in_rect(PLATE, 0, 1)])
    # ---- the headlamps, wrapped round the corners (the indicator's amber at their outer ends), the grille between
    hl = [(0.007, y) for y in (0.545, 0.585, 0.625, 0.665)]
    strip(B, lambda y: nose_path(y, -2.1, x_to=-0.37), hl, "car_atlas", True, HEAD)
    strip(B, lambda y: [(-x, z) for x, z in reversed(nose_path(y, -2.1, x_to=-0.37))], hl, "car_atlas", True, HEAD, flip_u=True)
    strip(B, lambda y: [(lerp(-0.35, 0.35, k / 6), ZS[0] + z_shift(ZS[0], y)) for k in range(7)], [(0.008, 0.55), (0.008, 0.66)], "car_atlas", True, GRILLE)
    # ---- the tail lamps: tall, in the corners beside the opening, wrapped round onto the sides
    tl = [(0.008, y) for y in (0.64, 0.75, 0.86, 0.975)]

    def tail_lamp(y):
        xl = side_x(NI - 1, y)
        return tail_path(y, 2.12, x_to=-(xl - 0.118), n_cap=3)
    strip(B, tail_lamp, tl, "car_atlas", False, TAIL)
    strip(B, lambda y: [(-x, z) for x, z in reversed(tail_lamp(y))], tl, "car_atlas", False, TAIL, flip_u=True)
    # the side mouldings along the doors
    for side in (-1, 1):
        idx = [i for i in range(NI) if -0.84 <= ZS[i] <= 0.86]
        G = []
        for y, o in ((0.47, 0.004), (0.48, 0.014), (0.51, 0.014), (0.52, 0.004)):
            G.append([Vector((side * (side_x(i, y) + o), y, ZS[i])) for i in idx])
        B.grid(G, "car_trim", (side, 0, 0))
        for i in (idx[0], idx[-1]):
            B.poly([Vector((side * (side_x(i, y) + o), y, ZS[i])) for y, o in ((0.47, 0.004), (0.48, 0.014), (0.51, 0.014), (0.52, 0.004))],
                   "car_trim", out=(0, 0, 1 if i == idx[-1] else -1))
    # ---- the mirrors, the door pulls, a side repeater
    for side in (-1, 1):
        i = station_near(ZC + 0.13)
        yb = ROWS[i][6][1]
        xb = ROWS[i][6][0]
        zc = ZC + 0.13
        B.rbox((side * (xb + 0.13), yb + 0.085, zc), (0.19, 0.115, 0.085), "car_trim", r=0.03)
        B.beam((side * (xb - 0.01), yb + 0.03, zc - 0.02), (side * (xb + 0.06), yb + 0.07, zc), 0.05, 0.03, "car_trim")
        B.poly([(side * (xb + 0.05), yb + 0.04, zc + 0.044), (side * (xb + 0.21), yb + 0.04, zc + 0.044),
                (side * (xb + 0.21), yb + 0.13, zc + 0.044), (side * (xb + 0.05), yb + 0.13, zc + 0.044)], "car_mirror", out=(0, 0, 1))
        for hz in (0.13, 0.95):
            i = station_near(hz)
            y = ROWS[i][6][1] - 0.075
            x = side_x(i, y)
            B.rbox((side * (x + 0.006), y, hz), (0.02, 0.03, 0.135), "car_trim")
            B.rbox((side * (x + 0.001), y - 0.002, hz - 0.01), (0.006, 0.042, 0.09), "car_gap")
        i = station_near(-1.62)
        y = 0.64
        B.poly([(side * (side_x(i, y) + 0.004), y - 0.016, -1.66), (side * (side_x(i, y) + 0.004), y - 0.016, -1.58),
                (side * (side_x(i, y) + 0.004), y + 0.016, -1.58), (side * (side_x(i, y) + 0.004), y + 0.016, -1.66)], "car_atlas",
               out=(side, 0, 0), uvs=[in_rect(AMBER, 0, 0), in_rect(AMBER, 1, 0), in_rect(AMBER, 1, 1), in_rect(AMBER, 0, 1)])
    # ---- the roof rails: a bar each side on three feet
    for side in (-1, 1):
        x = side * 0.56
        zs = [0.06 + (1.94 - 0.06) * k / 5 for k in range(6)]
        sec = [(-0.016, 0.05), (-0.018, 0.068), (-0.008, 0.078), (0.008, 0.078), (0.018, 0.068), (0.016, 0.05)]
        G = [[Vector((x + side * dx, top_y(z, x) + dy, z)) for dx, dy in sec] for z in zs]
        B.grid(G, "car_trim", lambda m, x=x: Vector((m.x - x, 1.0, 0)))
        for z in (zs[0], zs[-1]):
            B.poly([Vector((x + side * dx, top_y(z, x) + dy, z)) for dx, dy in sec], "car_trim", out=(0, 0, 1 if z > 1 else -1))
        for z in (0.12, 1.0, 1.88):
            B.rbox((x, top_y(z, x) + 0.03, z), (0.045, 0.06, 0.1), "car_trim")
    # ---- the scuttle's wipers, parked; the aerial; the exhaust
    for x0 in (-0.64, 0.0):
        z0 = ZC + 0.07
        a = Vector((x0, top_y(z0, x0) + 0.015, z0))
        b = Vector((x0 + 0.55, top_y(z0 + 0.03, x0 + 0.55) + 0.015, z0 + 0.03))
        B.beam(a, b, 0.012, 0.012, "car_trim")
        B.beam(a + Vector((0.05, 0.012, 0.004)), b + Vector((0.0, 0.012, 0.0)), 0.016, 0.008, "car_trim")
        B.beam((x0 + 0.02, top_y(ZC - 0.01, x0) + 0.0, ZC - 0.01), a, 0.02, 0.02, "car_trim")
    i = station_near(-1.12)
    B.lathe((0.71, ROWS[i][6][1] - 0.01, -1.12), (0, 1, 0.22), [(0.0, 0.006), (0.62, 0.003)], 4, "car_mirror")
    B.lathe((0.42, 0.225, ZT + 0.0), (0, 0, 1), [(-0.05, 0.028), (0.07, 0.03), (0.075, 0.024)], 8, "car_trim")
    B.poly([Vector((0.42 + 0.024 * math.cos(t), 0.225 + 0.024 * math.sin(t), ZT + 0.07)) for t in [k / 8 * math.tau for k in range(8)]],
           "car_gap", out=(0, 0, 1))


# ------------------------------------------------------------------ the cabin (seen through the glass, and from the seat)

def cabin(B):
    from mathutils import Matrix
    hwi = 0.745   # the door cards' inner faces
    # floor, tunnel, firewall
    B.poly([(-hwi, 0.30, ZC + 0.03), (hwi, 0.30, ZC + 0.03), (hwi, 0.30, SEAT_Z - 0.02), (-hwi, 0.30, SEAT_Z - 0.02)], "car_carpet", out=(0, 1, 0))
    B.rbox((0, 0.37, -0.25), (0.26, 0.16, 1.3), "car_carpet", r=0.05)
    B.poly([(-hwi, 0.30, ZC + 0.03), (hwi, 0.30, ZC + 0.03), (hwi, 0.62, ZC + 0.03), (-hwi, 0.62, ZC + 0.03)], "car_inner", out=(0, 0, 1))
    # the dash: a moulded top under the windscreen, its lip, its face down to the footwell
    prof = [(ZC + 0.03, 0.905), (ZC + 0.25, 0.925), (-0.53, 0.93), (-0.495, 0.915), (-0.485, 0.80), (-0.50, 0.68), (-0.60, 0.55), (ZC + 0.03, 0.50)]
    xs = [-hwi, -0.4, 0.0, 0.4, hwi]
    G = [[Vector((x, y + 0.012 * (1 - (x / hwi) ** 2), z)) for z, y in prof] for x in xs]
    B.grid(G, "car_inner", lambda m: Vector((0, m.y - 0.72, m.z + 0.62)), closed_v=True)
    for x in (-hwi, hwi):
        B.poly([Vector((x, y, z)) for z, y in prof], "car_inner", out=(x, 0, 0))
    # the binnacle's hood over the gauges
    hood = [(-0.62, 0.935), (-0.56, 0.998), (-0.47, 0.99), (-0.445, 0.955), (-0.47, 0.905)]
    G = [[Vector((x, y, z)) for z, y in hood] for x in (-0.6, -0.39, -0.17)]
    B.grid(G, "car_inner", lambda m: Vector((0, m.y - 0.9, m.z + 0.55)), closed_v=True)
    for x in (-0.6, -0.17):
        B.poly([Vector((x, y, z)) for z, y in hood], "car_inner", out=(x + 0.38, 0, 0))
    q = lambda R, x0, x1, y0, y1, z: B.poly([(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], "car_atlas", out=(0, 0, 1),
                                            uvs=[in_rect(R, 0, 0), in_rect(R, 1, 0), in_rect(R, 1, 1), in_rect(R, 0, 1)])
    q(GAUGES, -0.57, -0.2, 0.86, 0.952, -0.47)
    q(RADIO, -0.12, 0.12, 0.6, 0.84, -0.479)
    q(VENT, 0.36, 0.6, 0.83, 0.875, -0.478)
    q(VENT, -0.72, -0.6, 0.83, 0.875, -0.478)
    # the steering wheel on its column, three spokes
    # (low enough that the seat's eye sees the road over its rim)
    wc = Vector((-0.38, 0.86, -0.22))
    wax = Vector((0, 0.55, 1)).normalized()
    tor = [(0.017 * math.sin(t), 0.185 + 0.017 * math.cos(t)) for t in [k / 5 * math.tau for k in range(5)]]
    B.lathe(wc, wax, tor, 14, "car_trim", closed_profile=True)
    B.lathe(wc, wax, [(-0.02, 0.06), (0.025, 0.05), (0.04, 0.0)], 8, "car_trim")
    right = Vector((1, 0, 0))
    up = wax.cross(right).normalized()
    for d in (right, -right, -up):
        B.beam(wc + d * 0.05, wc + d * 0.175, 0.045, 0.012, "car_trim", up=wax)
    B.lathe(wc - wax * 0.36, wax, [(0.0, 0.04), (0.33, 0.032)], 8, "car_trim")
    # the gear lever
    B.lathe((0, 0.45, -0.22), (0, 1, -0.15), [(0.0, 0.04), (0.05, 0.008), (0.2, 0.007), (0.22, 0.022), (0.27, 0.0)], 5, "car_trim")
    # the seats: two buckets, the rear bench
    def rotx(a):
        return Matrix.Rotation(a, 3, "X")
    for sx in (-0.38, 0.38):
        B.rbox((sx, 0.355, 0.15), (0.44, 0.1, 0.44), "car_inner")
        B.rbox((sx, 0.47, 0.13), (0.5, 0.14, 0.52), "car_seat", r=0.05)
        B.rbox((sx, 0.86, 0.475), (0.5, 0.66, 0.13), "car_seat", r=0.05, rot=rotx(0.17))
        B.rbox((sx, 1.27, 0.545), (0.27, 0.16, 0.09), "car_seat", r=0.035, rot=rotx(0.17))
        B.beam((sx - 0.07, 1.17, 0.53), (sx - 0.07, 1.2, 0.535), 0.012, 0.012, "car_mirror")
        B.beam((sx + 0.07, 1.17, 0.53), (sx + 0.07, 1.2, 0.535), 0.012, 0.012, "car_mirror")
    B.rbox((0, 0.47, 0.86), (1.38, 0.14, 0.5), "car_seat", r=0.05)
    B.rbox((0, 0.82, 1.08), (1.38, 0.52, 0.12), "car_seat", r=0.05, rot=rotx(0.2))
    for sx in (-0.44, 0.44):
        B.rbox((sx, 1.12, 1.15), (0.24, 0.12, 0.08), "car_seat", r=0.03, rot=rotx(0.2))
    B.poly([(-hwi, 0.30, SEAT_Z - 0.02), (hwi, 0.30, SEAT_Z - 0.02), (hwi, FLOOR_Y, SEAT_Z - 0.02), (-hwi, FLOOR_Y, SEAT_Z - 0.02)], "car_inner", out=(0, 0, 1))
    # the door cards and their armrests, the window sills; the load space's trims and the arches in it
    for side in (-1, 1):
        x = side * hwi
        for z0, z1, top in ((ZC + 0.03, SEAT_Z, 0.875), (SEAT_Z, ZT - 0.07, 0.92)):
            y0 = 0.3 if z0 < 1 else FLOOR_Y
            B.poly([(x, y0, z0), (x, y0, z1), (x, top, z1), (x, top, z0)], "car_inner", out=(-side, 0, 0))
            B.poly([(x, top, z0), (x, top, z1), (side * (hwi + 0.06), top + 0.012, z1), (side * (hwi + 0.06), top + 0.012, z0)], "car_inner", out=(0, 1, 0))
        B.rbox((side * (hwi - 0.035), 0.66, -0.25), (0.07, 0.05, 0.5), "car_inner")
        B.rbox((side * (hwi - 0.035), 0.66, 0.75), (0.07, 0.05, 0.35), "car_inner")
        B.rbox((side * (hwi - 0.09), FLOOR_Y + 0.1, ZRA), (0.18, 0.2, 0.74), "car_inner", r=0.04)
    # the pillars and the window frames lined inside (from the seat the paint's back faces glared red), the glass left clear
    i0, i1 = station_near(ZC), station_near(SEAT_Z + 0.1)
    for ks, sgn in ((range(5, 11), 1), (range(14, 20), -1)):
        ks = list(ks)
        G = [[P(i, k) + Vector((sgn * 0.012, -0.004 if k in (10, 14) else 0.0, 0)) for k in ks] for i in range(i0, i1 + 1)]

        def rl(i, j, ks=ks):
            k = ks[j]
            band = k if k < 12 else 23 - k
            if band < 6:
                return "car_inner"
            return None if role_top(i0 + i, band) == "car_glass" else "car_inner"
        B.grid(G, rl, (sgn, 0, 0))
    # the load floor (needle-felt)
    B.poly([(-hwi, FLOOR_Y, SEAT_Z), (hwi, FLOOR_Y, SEAT_Z), (hwi, FLOOR_Y, ZT - 0.005), (-hwi, FLOOR_Y, ZT - 0.005)], "car_carpet", out=(0, 1, 0))
    # the headliner under the roof, the visors, the mirror
    G = []
    hs = list(range(station_near(ZH), NI))
    hs = hs[::3] + ([hs[-1]] if (len(hs) - 1) % 3 else [])
    for i in hs:
        G.append([P(i, k) + Vector((0, -0.03, 0)) for k in (9, 10, 12, 14, 15)])
    B.grid(G, "car_headliner", (0, -1, 0))
    for sx in (-0.38, 0.38):
        B.rbox((sx, 1.33, ZH + 0.13), (0.36, 0.022, 0.17), "car_headliner", rot=rotx(-0.35))
    B.rbox((0, 1.28, ZH + 0.07), (0.22, 0.06, 0.03), "car_trim", r=0.012)
    B.beam((0, 1.31, ZH + 0.07), (0, 1.39, ZH + 0.02), 0.015, 0.015, "car_trim")
    # in the back: a duffel
    prof = [(-0.2, 0.0), (-0.19, 0.08), (-0.15, 0.13), (-0.05, 0.15), (0.08, 0.15), (0.16, 0.13), (0.2, 0.08), (0.21, 0.0)]
    from mathutils import Matrix as M3
    c = Vector((-0.33, FLOOR_Y + 0.125, 1.5))
    G = []
    for s in range(10):
        t = s / 10 * math.tau
        G.append([c + Vector((a, r * math.sin(t) * 0.82, r * math.cos(t))) for a, r in prof])
    B.grid(G, "car_seat", lambda m: m - c, tint=(0.17, 0.21, 0.3), closed_u=True)
    for hx in (-0.4, -0.24):
        B.lathe(Vector((hx, FLOOR_Y + 0.25, 1.5)), (1, 0, 0), [(a, r) for a, r in [(0.007 * math.sin(t), 0.05 + 0.007 * math.cos(t)) for t in [k / 4 * math.tau for k in range(4)]]],
                6, "car_trim", closed_profile=True)


# ------------------------------------------------------------------ the tailgate, lifted

def tailgate(T):
    I = APERTURE
    top = max(p.y for p in I)

    def half(y):
        # the opening's half-width at y (its left side going up)
        left = [p for p in I if p.x < 0]
        left.sort(key=lambda p: p.y)
        if y >= left[-1].y:
            return -left[-1].x
        for a, b in zip(left, left[1:]):
            if a.y <= y <= b.y and b.y > a.y:
                return -lerp(a.x, b.x, (y - a.y) / (b.y - a.y))
        return -left[0].x
    ys = [FLOOR_Y + 0.005, 0.68, 0.78, 0.88, 0.95, 0.975, 1.12, 1.27, 1.30, top - 0.012, top + 0.01]
    cols = 9
    G = []
    for y in ys:
        w = half(min(y, top - 0.012)) + 0.012
        if y > top:
            w -= 0.03
        z = ZT + z_shift(ZT, min(y, top)) + 0.006
        G.append([Vector((w * (2 * c / (cols - 1) - 1), y, z + 0.03 * (1 - (2 * c / (cols - 1) - 1) ** 2))) for c in range(cols)])

    def role(j, c):
        y = (ys[j] + ys[j + 1]) / 2
        if 0.975 < y < 1.27 and 0 < c < cols - 2:
            return "car_glass"
        if 0.95 < y < 1.30:
            return "car_trim"
        return "car_paint"
    T.grid(G, role, (0, 0, 1))
    # its inside: the trim panel over the lower half and the top rail (the glass is the outer's, seen through)
    Gi = [[p + Vector((0, 0, -0.055)) for p in row] for row in G]

    def role_in(j, c):
        y = (ys[j] + ys[j + 1]) / 2
        if 0.975 < y < 1.27 and 0 < c < cols - 2:
            return None
        return "car_inner"
    T.grid(Gi, role_in, (0, 0, -1))
    # its edges
    for c in (0, cols - 1):
        T.grid([[G[j][c], Gi[j][c]] for j in range(len(ys))], "car_paint", (1 if c else -1, 0, 0))
    T.grid([G[0], Gi[0]], "car_paint", (0, -1, 0))
    T.grid([G[-1], Gi[-1]], "car_paint", (0, 1, 0))
    # the handle under the glass, the rear wiper, the struts' brackets
    zc = ZT + 0.04
    T.rbox((0, 0.905, zc), (0.22, 0.03, 0.025), "car_trim", r=0.01)
    T.beam((0.0, 0.99, ZT + z_shift(ZT, 0.99) + 0.045), (-0.33, 1.13, ZT + z_shift(ZT, 1.13) + 0.04), 0.012, 0.012, "car_trim")
    # lifted: about its hinge at the roof's back edge, to some 28 degrees over level
    hinge = Vector((0, top - 0.005, ZT + z_shift(ZT, top) - 0.03))
    d = Vector((0, (ys[0] + ys[1]) / 2, ZT)) - hinge
    phi_c = math.atan2(d.y, d.z)
    a = phi_c - math.radians(28)
    ca, sa = math.cos(a), math.sin(a)

    def rot(p):
        r = p - hinge
        return hinge + Vector((r.x, r.y * ca - r.z * sa, r.y * sa + r.z * ca))
    T.V = [rot(p) for p in T.V]
    global TG_ROT
    TG_ROT = rot
    return hinge


def struts(B, rot):
    """The gas struts from the opening's sides up to the lifted tailgate."""
    for side in (-1, 1):
        a = Vector((side * 0.66, 0.97, ZT - 0.05))
        b = rot(Vector((side * 0.62, 1.2, ZT + z_shift(ZT, 1.2) - 0.03)))
        m = a.lerp(b, 0.55)
        B.lathe(a, b - a, [(0.0, 0.011), ((m - a).length, 0.011)], 6, "car_trim")
        B.lathe(m, b - a, [(0.0, 0.0075), ((b - m).length, 0.0075)], 6, "car_mirror")


# ------------------------------------------------------------------ the wheels: 195/65 R15 on steel wheels under plain trims

def wheels(W):
    tyre = [(-0.088, 0.196), (-0.099, 0.255), (-0.088, 0.306), (-0.065, 0.324), (0.0, 0.326), (0.065, 0.324), (0.088, 0.306),
            (0.099, 0.255), (0.088, 0.196)]
    L = [0.0]
    for n in range(1, len(tyre)):
        L.append(L[-1] + math.dist(tyre[n], tyre[n - 1]))
    for side in (-1, 1):
        for wz in WHEELS:
            c = Vector((side * WHEEL_X, AXLE_Y, wz))
            # (inner bead to outer: v 0 to 1 on either side)
            prof = [(side * a, r) for a, r in tyre]

            def uv_t(s, k):
                return (s * 3.0, L[k] / L[-1])
            W.lathe(c, (1, 0, 0), prof, 14, "car_tyre", uv=uv_t)
            # the trim: a shallow dish over the steel wheel, its rim, the dark inside of the wheel behind it
            trim = [(0.084, 0.19), (0.094, 0.165), (0.1, 0.09), (0.103, 0.0)]
            tr = [(side * a, r) for a, r in trim]

            def uv_h(s, k, trim=trim):
                rr = trim[k][1] / 0.196
                t = s * math.tau
                return (0.5 + 0.5 * rr * math.cos(t), 0.5 + 0.5 * rr * math.sin(t))
            W.lathe(c, (1, 0, 0), tr, 14, "car_hubcap", uv=uv_h, out=(side, 0, 0))
            W.poly([c + Vector((-side * 0.07, 0.2 * math.sin(t), 0.2 * math.cos(t))) for t in [k / 10 * math.tau for k in range(10)]],
                   "car_gap", out=(-side, 0, 0))


# ------------------------------------------------------------------ textures (sRGB, drawn here, row 0 at the bottom)

def save_png(name, arr):
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.dstack([arr, np.ones((h, w))])
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels = np.clip(arr, 0, 1).astype(np.float32).ravel()
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img


def pnoise(h, w, seed, octaves=((3, 0.5), (6, 0.3), (12, 0.2), (24, 0.12))):
    """Tileable noise in 0..1 (sums of whole-period waves)."""
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    u, v = xx / w, yy / h
    out = np.zeros((h, w))
    for f, a in octaves:
        for _ in range(4):
            fx, fy = rng.integers(-f, f + 1, 2)
            out += a * np.sin(2 * np.pi * (fx * u + fy * v) + rng.uniform(0, 6.283))
    return (out - out.min()) / (out.max() - out.min())


def tex_grain():
    """The paint's (and the plastics'): a faint mottle, a few chips and fine scratches; near white (the vertex colours
    carry the colour)."""
    s = 128
    g = 0.9 + 0.1 * pnoise(s, s, 3)
    rng = np.random.default_rng(5)
    yy, xx = np.mgrid[0:s, 0:s]
    for _ in range(30):
        cx, cy, r = rng.uniform(0, s), rng.uniform(0, s), rng.uniform(0.5, 1.4)
        d = np.hypot(((xx - cx + s / 2) % s) - s / 2, ((yy - cy + s / 2) % s) - s / 2)
        g = np.where(d < r, 0.62, g)
    for _ in range(7):
        x0, y0, ang, ln = rng.uniform(0, s), rng.uniform(0, s), rng.uniform(0, math.pi), rng.uniform(8, 26)
        for t in np.linspace(0, ln, int(ln * 2)):
            x, y = int(x0 + math.cos(ang) * t) % s, int(y0 + math.sin(ang) * t) % s
            g[y, x] = 1.0
    return save_png("car_grain", np.dstack([g, g, g]))


def tex_fabric():
    """Seat cloth (and the felt, the headliner): fine ribs and a fleck."""
    s = 64
    yy, xx = np.mgrid[0:s, 0:s]
    g = 0.86 + 0.06 * np.sin(2 * np.pi * xx / 4) + 0.08 * (pnoise(s, s, 11, ((8, 0.5), (16, 0.3), (32, 0.3))) - 0.5)
    rng = np.random.default_rng(12)
    g += (rng.random((s, s)) > 0.94) * 0.08
    return save_png("car_fabric", np.dstack([g, g, g * 0.98]))


def tex_tyre():
    """u round the tyre (a third of it), v across from the inner bead to the outer: the sidewalls, the tread's blocks."""
    w, h = 256, 64
    yy, xx = np.mgrid[0:h, 0:w]
    v = (yy + 0.5) / h
    base = np.full((h, w), 0.17)
    tread = (v > 0.31) & (v < 0.69)
    base = np.where(tread, 0.15, base)
    for gv in (0.4, 0.5, 0.6):
        base = np.where(np.abs(v - gv) < 0.018, 0.05, base)
    rib = np.floor((v - 0.31) / 0.095)
    lat = ((xx + rib * 5) % 16) < 2
    base = np.where(tread & lat, 0.06, base)
    sh = ((v > 0.31) & (v < 0.37)) | ((v > 0.63) & (v < 0.69))
    base = np.where(sh & (((xx + 8) % 16) < 3), 0.05, base)
    for rv in (0.12, 0.88):
        base = np.where(np.abs(v - rv) < 0.012, 0.22, base)
    # the maker's lettering, raised, on the outer wall
    rng = np.random.default_rng(4)
    letters = np.zeros(w, bool)
    x = 20
    while x < w - 30:
        n = rng.integers(3, 7)
        for _ in range(n):
            letters[x:x + 4] = True
            x += 6
        x += rng.integers(20, 50)
    band = (v > 0.75) & (v < 0.81)
    base = np.where(band & letters[None, :] & (((yy * 3 + xx) % 5) > 0), 0.24, base)
    base *= 0.92 + 0.08 * pnoise(h, w, 9)
    return save_png("car_tyre", np.dstack([base, base, base * 0.97]))


def tex_hubcap():
    s = 128
    yy, xx = np.mgrid[0:s, 0:s]
    dx, dy = (xx + 0.5 - s / 2) / (s / 2), (yy + 0.5 - s / 2) / (s / 2)
    r = np.hypot(dx, dy)
    a = np.degrees(np.arctan2(dy, dx)) % 360
    c = np.full((s, s), 0.66)
    c = np.where(r < 0.14, 0.78, c)
    c = np.where((r >= 0.14) & (r < 0.17), 0.3, c)
    slot = (r > 0.32) & (r < 0.66) & (np.abs(((a + 22.5) % 45) - 22.5) < 9 + 6 * (r - 0.32))
    c = np.where(slot, 0.05, c)
    edge = (r > 0.30) & (r < 0.68) & (np.abs(((a + 22.5) % 45) - 22.5) < 11 + 6 * (r - 0.32)) & ~slot
    c = np.where(edge, 0.82, c)
    c = np.where((r > 0.68) & (r < 0.75), 0.8, c)
    notch = (r > 0.8) & (r < 0.9) & ((a % 15) < 3)
    c = np.where(notch, 0.25, c)
    c = np.where(r > 0.93, 0.2, c)
    c *= 0.88 + 0.12 * pnoise(s, s, 21)
    return save_png("car_hubcap", np.dstack([c, c * 1.01, c * 1.03]))


GLYPHS = {
    "2": ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "W": ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    " ": ["00000"] * 7,
}


def tex_atlas():
    S = 256
    A = np.zeros((S, S, 3))
    yy, xx = np.mgrid[0:S, 0:S]

    def region(R):
        x0, y0, x1, y1 = [int(round(t * S)) for t in R]
        return x0, y0, x1, y1

    def fill(R, fn):
        x0, y0, x1, y1 = region(R)
        h, w = y1 - y0, x1 - x0
        ty, tx = np.mgrid[0:h, 0:w]
        s = (tx + 0.5) / w
        t = (ty + 0.5) / h
        A[y0:y1, x0:x1] = fn(s, t, w, h)

    def border(s, t, w, h, px):
        return (s * w < px) | (s * w > w - px) | (t * h < px) | (t * h > h - px)

    def head(s, t, w, h):
        flute = 0.05 * np.sin(2 * np.pi * s * 26)
        c = np.dstack([0.58 + flute, 0.61 + flute, 0.64 + flute])
        e = ((s - 0.62) / 0.24) ** 2 + ((t - 0.5) / 0.36) ** 2
        c = np.where((e < 1)[..., None], np.dstack([0.78 + 0.15 * (1 - e)] * 3) + flute[..., None] * 0, c)
        c = np.where((e < 0.08)[..., None], 0.95, c)
        amb = s < 0.22
        c = np.where(amb[..., None], np.dstack([0.8 + flute, 0.47 + flute, 0.1 + 0 * flute]), c)
        c = np.where((np.abs(s - 0.22) < 0.012)[..., None], 0.08, c)
        return np.where(border(s, t, w, h, 3)[..., None], 0.06, c)

    def tail(s, t, w, h):
        rib = 0.07 * np.sin(2 * np.pi * t * 28)
        red = np.dstack([0.56 + rib, 0.05 + 0 * rib, 0.05 + 0 * rib])
        amb = np.dstack([0.74 + rib, 0.4 + rib, 0.05 + 0 * rib])
        wht = np.dstack([0.78 + rib, 0.78 + rib, 0.75 + rib])
        c = np.where((t < 0.2)[..., None], wht, np.where((t < 0.4)[..., None], amb, red))
        c = np.where((np.abs(t - 0.2) < 0.01)[..., None] | (np.abs(t - 0.4) < 0.01)[..., None], 0.06, c)
        return np.where(border(s, t, w, h, 3)[..., None], 0.05, c)

    def grille(s, t, w, h):
        c = np.full((h, w, 3), 0.035)
        for k in range(4):
            y = 0.2 + k * 0.2
            c = np.where((np.abs(t - y) < 0.045)[..., None], 0.13, c)
            c = np.where((np.abs(t - (y + 0.04)) < 0.012)[..., None], 0.2, c)
        e = ((s - 0.5) / 0.08) ** 2 + ((t - 0.5) / 0.22) ** 2
        c = np.where((e < 1)[..., None], np.dstack([0.55 + 0.2 * (1 - e)] * 3), c)
        return np.where(border(s, t, w, h, 4)[..., None], 0.09, c)

    def plate(s, t, w, h):
        c = np.dstack([0.86 - 0.06 * t, 0.84 - 0.06 * t, 0.76 - 0.05 * t])
        ink = np.array([0.1, 0.14, 0.32])
        m = np.zeros((h, w), bool)
        text = "2HW 417"
        gx, gy = 2, 3
        cw = 6 * gx + 1
        x0 = (w - len(text) * cw) // 2
        y0 = int(h * 0.22)
        for n, ch in enumerate(text):
            g = GLYPHS[ch]
            for r, row in enumerate(g):
                for col, bit in enumerate(row):
                    if bit == "1":
                        px, py = x0 + n * cw + col * gx, y0 + (6 - r) * gy
                        m[py:py + gy, px:px + gx] = True
        ty, tx = np.mgrid[0:h, 0:w]
        rng = np.random.default_rng(8)
        top = (ty > h * 0.8) & (ty < h * 0.9) & (tx > w * 0.25) & (tx < w * 0.75) & ((tx % 5) < 3)
        bot = (ty > h * 0.08) & (ty < h * 0.14) & (tx > w * 0.35) & (tx < w * 0.65) & ((tx % 4) < 2)
        c = np.where((m | bot)[..., None], ink, c)
        c = np.where(top[..., None], np.array([0.5, 0.1, 0.1]), c)
        for bx in (0.18, 0.82):
            d = np.hypot(tx - bx * w, ty - 0.86 * h)
            c = np.where((d < 2.2)[..., None], 0.3, c)
        c *= (0.9 + 0.1 * rng.random((h, w)))[..., None]
        return np.where(border(s, t, w, h, 2)[..., None], ink * 0.8, c)

    def gauges(s, t, w, h):
        c = np.full((h, w, 3), 0.03)
        ty, tx = np.mgrid[0:h, 0:w]
        for cx in (0.27, 0.73):
            d = np.hypot(tx - cx * w, ty - 0.5 * h)
            ang = np.degrees(np.arctan2(ty - 0.5 * h, tx - cx * w)) % 360
            ring = (d > h * 0.36) & (d < h * 0.44) & ((ang % 24) < 6) & ((ang < 200) | (ang > 340))
            c = np.where(ring[..., None], 0.8, c)
            c = np.where((d < h * 0.47)[..., None] & (d > h * 0.45)[..., None], 0.25, c)
        for cx, ang0 in ((0.27, 140), (0.73, 160)):
            for k in range(40):
                rr = k / 40 * h * 0.38
                px = int(cx * w + math.cos(math.radians(ang0)) * rr)
                py = int(0.5 * h + math.sin(math.radians(ang0)) * rr)
                if 0 <= px < w and 0 <= py < h:
                    c[py, px] = (0.9, 0.4, 0.1)
        for cx in (0.47, 0.53):
            c = np.where(((np.abs(tx - cx * w) < 3) & (np.abs(ty - 0.35 * h) < 2))[..., None], 0.35, c)
        return c

    def radio(s, t, w, h):
        c = np.full((h, w, 3), 0.1)
        vent = (t > 0.72) & (t < 0.95) & (((t * h).astype(int) % 4) < 2) & (s > 0.08) & (s < 0.92)
        c = np.where(vent[..., None], 0.03, c)
        face = (t > 0.38) & (t < 0.66) & (s > 0.06) & (s < 0.94)
        c = np.where(face[..., None], 0.15, c)
        disp = (t > 0.5) & (t < 0.6) & (s > 0.3) & (s < 0.7)
        c = np.where(disp[..., None], np.array([0.1, 0.2, 0.13]), c)
        btn = (t > 0.41) & (t < 0.46) & ((((s - 0.1) * 10) % 1.6) < 0.8) & (s > 0.1) & (s < 0.9)
        c = np.where(btn[..., None], 0.4, c)
        ty, tx = np.mgrid[0:h, 0:w]
        for cx in (0.22, 0.5, 0.78):
            d = np.hypot(tx - cx * w, ty - 0.18 * h)
            c = np.where((d < h * 0.09)[..., None], 0.3, c)
            c = np.where((d < h * 0.03)[..., None], 0.6, c)
        return c

    def vent(s, t, w, h):
        c = np.full((h, w, 3), 0.04)
        c = np.where(((((s * w).astype(int)) % 5) < 2)[..., None], 0.16, c)
        return np.where(border(s, t, w, h, 2)[..., None], 0.12, c)

    def amber(s, t, w, h):
        rib = 0.06 * np.sin(2 * np.pi * s * 8)
        c = np.dstack([0.8 + rib, 0.45 + rib, 0.08 + 0 * rib])
        return np.where(border(s, t, w, h, 2)[..., None], 0.08, c)
    A[:] = 0.5
    for R, fn in ((HEAD, head), (TAIL, tail), (GRILLE, grille), (PLATE, plate), (GAUGES, gauges), (RADIO, radio), (VENT, vent), (AMBER, amber)):
        # (each picture fills its whole cell; the UVs sit 2 px inside it)
        x0, y0, x1, y1 = R
        e = 2 / S
        fill((x0 - e, y0 - e, x1 + e, y1 + e), fn)
    return save_png("car_atlas", A)


def tex_shadow():
    """The ground's darkening under the car (the game lays it as a decal): soft-edged, darkest under the middle."""
    w, h = 64, 128
    yy, xx = np.mgrid[0:h, 0:w]
    dx = np.abs((xx + 0.5) / w - 0.5) * 2
    dy = np.abs((yy + 0.5) / h - 0.5) * 2
    d = np.maximum(((dx - 0.55) / 0.45).clip(0), ((dy - 0.72) / 0.28).clip(0))
    a = (1 - np.clip(np.hypot(((dx - 0.55) / 0.45).clip(0), ((dy - 0.72) / 0.28).clip(0)), 0, 1)) ** 1.6
    a *= 0.85 - 0.25 * dx
    out = np.dstack([np.zeros((h, w))] * 3 + [a])
    return save_png("car_shadow", out)


# ------------------------------------------------------------------ the Blender objects, the vertex colours

TILED = {"car_paint": 1.5, "car_trim": 1.5, "car_gap": 1.5, "car_inner": 2.0, "car_mirror": 1.0, "car_glass": 1.0,
         "car_seat": 5.0, "car_carpet": 5.0, "car_headliner": 4.0}
PREVIEW_COL = {"car_paint": (0.33, 0.085, 0.085), "car_trim": (0.07, 0.07, 0.075), "car_gap": (0.03, 0.03, 0.03),
               "car_glass": (0.05, 0.06, 0.065), "car_atlas": (1, 1, 1), "car_tyre": (1, 1, 1), "car_hubcap": (1, 1, 1),
               "car_inner": (0.22, 0.215, 0.205), "car_seat": (0.3, 0.28, 0.25), "car_carpet": (0.25, 0.24, 0.22),
               "car_headliner": (0.6, 0.58, 0.53), "car_mirror": (0.7, 0.72, 0.75)}
TEXFILE = {"car_paint": "car_grain", "car_trim": "car_grain", "car_gap": "car_grain", "car_inner": "car_grain",
           "car_mirror": "car_grain", "car_seat": "car_fabric", "car_carpet": "car_fabric", "car_headliner": "car_fabric",
           "car_atlas": "car_atlas", "car_tyre": "car_tyre", "car_hubcap": "car_hubcap"}
_mats = {}


def role_mat(role):
    """A material named for the role (the game puts its own on). For the preview: the texture times the vertex colours."""
    if role in _mats:
        return _mats[role]
    m = bpy.data.materials.new(role)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.5
    if role == "car_glass":
        bsdf.inputs["Base Color"].default_value = (0.02, 0.025, 0.03, 1)
        bsdf.inputs["Alpha"].default_value = 0.5
        bsdf.inputs["Roughness"].default_value = 0.05
        m.blend_method = "BLEND"
        m.use_backface_culling = False
    else:
        att = nt.nodes.new("ShaderNodeVertexColor")
        att.layer_name = "Col"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(att.outputs["Color"], mix.inputs[6])
        if role in TEXFILE:
            t = nt.nodes.new("ShaderNodeTexImage")
            t.image = bpy.data.images.get(TEXFILE[role])
            nt.links.new(t.outputs["Color"], mix.inputs[7])
        else:
            mix.inputs[7].default_value = (1, 1, 1, 1)
        nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
        if role == "car_mirror":
            bsdf.inputs["Metallic"].default_value = 1.0
            bsdf.inputs["Roughness"].default_value = 0.15
    m.diffuse_color = (*PREVIEW_COL.get(role, (0.5, 0.5, 0.5)), 1)
    _mats[role] = m
    return m


def to_object(part):
    me = bpy.data.meshes.new(part.name)
    me.from_pydata([(p.x, -p.z, p.y) for p in part.V], [], [f[0] for f in part.F])
    roles = sorted(set(f[1] for f in part.F))
    for r in roles:
        me.materials.append(role_mat(r))
    me.uv_layers.new(name="UVMap")
    # (fetched after the layer exists: a new layer moves the others' data, and an old reference writes into freed memory;
    # the tints stay here, not in a face attribute: Blender 4.0 corrupts its heap with one beside the materials)
    uvl = me.uv_layers["UVMap"]
    TINTS[part.name] = [f[3] for f in part.F]
    for pi, f in enumerate(part.F):
        poly = me.polygons[pi]
        poly.material_index = roles.index(f[1])
        poly.use_smooth = True
        if f[2] is not None:
            for k, li in enumerate(poly.loop_indices):
                uvl.data[li].uv = f[2][k]

    me.update()
    ob = bpy.data.objects.new(part.name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def box_uvs(ob):
    me = ob.data
    uvl = me.uv_layers[0]
    for p in me.polygons:
        role = me.materials[p.material_index].name.split(".")[0]
        if role not in TILED:
            continue
        sc = TILED[role]
        n = p.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if ax == 0:
                uv = (co.y, co.z)
            elif ax == 1:
                uv = (co.x, co.z)
            else:
                uv = (co.x, co.y)
            uvl.data[li].uv = (uv[0] * sc, uv[1] * sc)


def srgb_to_lin(c):
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def mix3(a, b, t):
    return tuple(lerp(a[k], b[k], t) for k in range(3))


def mud_amount(g, n):
    """How much mud and road dust at a point (the game's frame): up the sills, round and behind the wheels (it was driven
    nose first down a wet track), the dust a wagon's tail pulls up."""
    x, y, z = g
    m = clamp(1 - (y - 0.2) / 0.3) * 0.8
    for wz in WHEELS:
        dz = z - wz
        rr = math.hypot(dz, y - AXLE_Y)
        if abs(x) > 0.55 and rr < ARCH_R + 0.14 and y > AXLE_Y - 0.1:
            m = max(m, (1 - clamp((rr - ARCH_R) / 0.14)) * 0.75)
        if 0 < dz < 1.1:
            m = max(m, clamp(1 - (y - 0.28) / 0.42) * (1 - dz / 1.1) * 0.95)
    if z > 2.0:
        m = max(m, 0.5 * clamp(1 - (y - 0.45) / 0.7))
    m *= clamp(0.45 + 1.2 * (fbm(x * 3.1 + 7, y * 6.3, z * 3.3, 3) + 0.5))
    return clamp(m)


DEEP = (0.245, 0.078, 0.072)       # the old burgundy where the sun hasn't had it
FADED = (0.37, 0.225, 0.205)       # chalked and pinked on the roof and the bonnet
MUD_DRY = (0.36, 0.30, 0.22)
MUD_WET = (0.2, 0.155, 0.11)


def colour(role, g, n, ao, tint):
    x, y, z = g
    up = n[1]
    mud = mud_amount(g, n) if role not in ("car_inner", "car_seat", "car_carpet", "car_headliner", "car_glass") else 0.0
    if g[1] > 0.95 and role != "car_paint":
        mud = 0.0
    mudc = mix3(MUD_DRY, MUD_WET, clamp(1 - (y - 0.18) / 0.25))
    lit = 0.28 + 0.72 * ao
    if role == "car_paint":
        f = clamp((up - 0.25) / 0.6) * clamp(0.65 + 0.7 * (fbm(x * 1.7, y * 1.7, z * 1.7, 3) + 0.5))
        f = max(f, 0.18 * sstep(0.7, 0.95, y))
        c = mix3(DEEP, FADED, clamp(f))
        c = mix3(c, mudc, mud)
    elif role == "car_trim":
        c = mix3((0.07, 0.07, 0.075), (0.16, 0.16, 0.16), clamp((up - 0.3) / 0.6) * 0.8)
        c = mix3(c, mudc, mud * 0.7)
    elif role == "car_gap":
        c = mix3((0.03, 0.03, 0.03), (0.13, 0.1, 0.07), clamp(mud * 1.2))
        lit = 0.5 + 0.5 * ao
    elif role == "car_tyre":
        c = mix3((0.55, 0.55, 0.55), (1.0, 0.88, 0.72), clamp((0.32 - y) / 0.25) * 0.7)
        lit = 0.45 + 0.55 * ao
    elif role == "car_hubcap":
        c = mix3((1, 1, 1), (0.82, 0.7, 0.55), clamp((0.3 - y) / 0.15) * 0.6)
    elif role == "car_atlas":
        c = (1, 1, 1)
        c = mix3(c, (0.8, 0.68, 0.52), mud * 0.6)
        lit = 0.6 + 0.4 * ao
    elif role in ("car_inner", "car_seat", "car_carpet", "car_headliner"):
        c = PREVIEW_COL[role]
        lit = 0.58 + 0.42 * ao
    else:
        c = PREVIEW_COL.get(role, (1, 1, 1))
    if tint is not None:
        c = tint
    return tuple(clamp(v * lit) for v in c)


def bake_colours(obs):
    """Ambient occlusion (rays against the car itself and the ground, the glass letting light through), then each
    role's colour, its fade and its mud, into the vertex colours."""
    verts, polys = [], []
    for ob in obs:
        me = ob.data
        base = len(verts)
        verts += [v.co.copy() for v in me.vertices]
        for p in me.polygons:
            if me.materials[p.material_index].name.startswith("car_glass"):
                continue
            polys.append([base + i for i in p.vertices])
    g0 = len(verts)
    verts += [Vector((-30, -30, 0)), Vector((30, -30, 0)), Vector((30, 30, 0)), Vector((-30, 30, 0))]
    polys.append([g0, g0 + 1, g0 + 2, g0 + 3])
    bvh = BVHTree.FromPolygons(verts, polys)
    N = 48
    dirs = []
    for k in range(N):
        zz = 1 - (k + 0.5) / N * 2
        r = math.sqrt(1 - zz * zz)
        th = k * 2.39996
        dirs.append(Vector((r * math.cos(th), r * math.sin(th), zz)))
    for ob in obs:
        me = ob.data
        ao = []
        for v in me.vertices:
            n = v.normal
            occ = tot = 0.0
            o = v.co + n * 0.004
            for d in dirs:
                c = d.dot(n)
                if c <= 0.05:
                    continue
                hit, _, _, dist = bvh.ray_cast(o, d, 1.1)
                tot += c
                if hit is not None:
                    occ += c * (1 - dist / 1.1) ** 0.5
            ao.append(clamp(1 - 1.25 * occ / max(tot, 1e-6)))
        me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
        col = me.color_attributes["Col"]
        tints = TINTS[ob.name]
        for p in me.polygons:
            role = me.materials[p.material_index].name.split(".")[0]
            t = tints[p.index]

            for li in p.loop_indices:
                vi = me.loops[li].vertex_index
                v = me.vertices[vi]
                g = (v.co.x, v.co.z, -v.co.y)
                n = (v.normal.x, v.normal.z, -v.normal.y)
                c = colour(role, g, n, ao[vi], t)
                col.data[li].color = (*srgb_to_lin(c), 1.0)
        me.color_attributes.active_color = col
        try:
            me.color_attributes.render_color_index = me.color_attributes.find("Col")
        except Exception:
            pass



def export(obs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    path = os.path.join(OUT, "car.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
                              export_texcoords=True, export_normals=True, export_colors=True, export_materials="EXPORT",
                              export_image_format="NONE")
    tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in obs}
    print(f"[car] {sum(tris.values())} triangles {tris} -> {path}")


# ------------------------------------------------------------------ the preview (EEVEE: the textures times the colours)

def preview_shots():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.eevee.taa_render_samples = 16
    sc.render.resolution_x, sc.render.resolution_y = 960, 600
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.42, 0.45, 0.5, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
    sc.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 2.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    sc.collection.objects.link(sun)
    gm = bpy.data.materials.new("ground")
    gm.diffuse_color = (0.3, 0.29, 0.27, 1)
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.24, 0.22, 1)
    bpy.ops.mesh.primitive_plane_add(size=40)
    bpy.context.active_object.data.materials.append(gm)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    shots = {
        "front34": ((-4.3, 1.55, -4.6), (0, 0.7, 0), 50),
        "side": ((-6.2, 1.1, 0.0), (0, 0.65, 0), 50),
        "rear_open": ((1.6, 1.62, 5.2), (0, 0.9, 1.8), 50),
        "rear34": ((3.8, 1.5, 4.6), (0, 0.7, 0.3), 50),
        "far": ((-11, 1.6, -9), (0, 0.6, 0), 50),
        "wheel": ((-1.9, 0.75, -2.35), (-0.78, 0.36, -1.36), 50),
        "interior": ((-1.55, 1.45, 0.55), (0.1, 0.85, -0.2), 40),
        "seat": ((-0.38, 1.15, 0.25), (-0.38, 1.06, -3.0), 75),
        "trunk": ((0.0, 1.62, 3.2), (0.05, 0.6, 2.0), 60),
        "top": ((-3.5, 4.5, -2.5), (0, 0.7, 0), 50),
    }
    paths = []
    for name, (eye, at, fov) in shots.items():
        e = Vector((eye[0], -eye[2], eye[1]))
        t = Vector((at[0], -at[2], at[1]))
        cam.location = e
        cam.rotation_euler = (t - e).to_track_quat("-Z", "Y").to_euler()
        cam.data.angle = math.radians(fov)
        cam.data.clip_start = 0.02
        sc.render.filepath = os.path.join(PREV, name + ".png")
        bpy.ops.render.render(write_still=True)
        paths.append(sc.render.filepath)
    # one sheet of them
    ims = []
    for p in paths:
        im = bpy.data.images.load(p)
        a = np.array(im.pixels[:], np.float32).reshape(im.size[1], im.size[0], 4)
        ims.append(a)
    h, w = ims[0].shape[:2]
    cols = 2
    rows = (len(ims) + cols - 1) // cols
    sheet = np.ones((rows * h, cols * w, 4), np.float32)
    for n, a in enumerate(ims):
        r, c = n // cols, n % cols
        sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = a
    img = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
    img.pixels = sheet.ravel()
    img.filepath_raw = os.path.join(PREV, "sheet.png")
    img.file_format = "PNG"
    img.save()
    print("[car] preview ->", img.filepath_raw)


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()
    for f in (tex_grain, tex_fabric, tex_tyre, tex_hubcap, tex_atlas, tex_shadow):
        f()
    B = Part("car_body")
    body(B)
    details(B)
    cabin(B)
    T = Part("car_tailgate")
    tailgate(T)
    struts(B, TG_ROT)
    W = Part("car_wheels")
    wheels(W)
    obs = [to_object(p) for p in (B, T, W)]
    for ob in obs:
        box_uvs(ob)
    bake_colours(obs)
    export(obs)
    if "--preview" in sys.argv:
        preview_shots()


APERTURE = []
TINTS = {}
TG_ROT = None

if __name__ == "__main__":
    build()
