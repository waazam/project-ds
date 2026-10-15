"""Act 1's small plants, modelled in Blender (2026-10-15, the owner: every model made in Blender): the wildflower patches'
flowers and the mushroom clumps. ParkProp still lays each patch and clump out by its seed (where every stem stands, how
tall, which way it leans, which colour) and drops one of these per plant. Run headless:

    blender -b --python tools/Blender/plants.py [-- name ...]

Each goes to assets/models/furniture/<name>.glb through the interactive props' pipeline (interactive.py: written in the
game's space, Y up, origin at the plant's foot; materials by role, the game swaps its own in, the "Col" shade multiplied
into the role's tint).

Sizes are canonical and the game scales them: a mushroom stands 0.1 m to the cap's underside with a 0.05 m cap; a flower
head is 0.05 m across its petals; the stem is 1 m (the game stretches it to each flower's height); the leaves are their
own piece at true size.

Roles: stem, cap, gill, fleck (mushrooms); stem, leaf, petal, eye (flowers).
"""
import math, os, sys
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import interactive as I

V = Vector
TAU = math.tau


def mushroom(name, cap_profile, stem_profile, gills=28, flecks=0, ring=False):
    """A mushroom standing on its foot at the origin: a lathed stem, a lathed cap (its top skin) with a recessed underside,
    radial gill fins between the stem and the rim, and (the amanita) warts on the cap and a skirt round the stem."""
    p = I.Piece(name)
    h = 0.1
    # the stem: a little bulb at the foot, tapering up
    p.role("stem").lathe((0, 0, 0), (0, 1, 0), [(r, y * h) for (r, y) in stem_profile], 16)
    if ring:
        # the skirt: a thin flared ring two-thirds up
        y0 = h * 0.72
        p.lathe((0, y0, 0), (0, -1, 0), [(0.0075, 0.0), (0.012, 0.004), (0.016, 0.012), (0.0165, 0.014), (0.0078, 0.003)], 20, close=False)
    # the cap's top skin: profile (radius fraction, height above the cap's underside, metres)
    R = 0.05
    top = [(R * rf, h + y) for (rf, y) in cap_profile]
    p.role("cap").lathe((0, 0, 0), (0, 1, 0), [(r, y) for (r, y) in top], 32, close=False)
    # the underside: a shallow dish from the rim to the stem, where the gills hang
    under = [(R * 0.97, h + cap_profile[0][1] - 0.001), (R * 0.6, h + 0.004), (0.009, h - 0.001)]
    p.role("gill").lathe((0, 0, 0), (0, -1, 0), [(r, -y) for (r, y) in under], 32, close=False)
    # the gill fins: thin wedges radiating from the stem to near the rim, short ones between the long
    for g in range(gills):
        a = g / gills * TAU
        d = V((math.cos(a), 0, math.sin(a)))
        side = V((-math.sin(a), 0, math.cos(a))) * 0.0006
        r0 = 0.009 if g % 2 == 0 else R * 0.45
        r1 = R * 0.93
        y0 = h + 0.002
        ya, yb = h - 0.003, h - 0.0005
        pts = [d * r0 + V((0, y0, 0)), d * r1 + V((0, h + cap_profile[0][1] - 0.002, 0)),
               d * r1 + V((0, yb, 0)), d * r0 + V((0, ya, 0))]
        a_ = [p.vert(q + side) for q in pts]
        b_ = [p.vert(q - side) for q in pts]
        p.face((a_[0], a_[1], a_[2], a_[3]))
        p.face((b_[3], b_[2], b_[1], b_[0]))
        p.face((a_[0], a_[3], b_[3], b_[0]))
        p.face((a_[1], b_[1], b_[2], a_[2]))
    # warts: little flattened domes on the cap's skin
    if flecks:
        import random
        rnd = random.Random(7)
        p.role("fleck")
        for _ in range(flecks):
            rf = math.sqrt(rnd.uniform(0.0, 0.8))
            a = rnd.uniform(0, TAU)
            # the cap's height at this radius (linear through the profile)
            y = top[0][1]
            for i in range(len(cap_profile) - 1):
                (r0, y0), (r1, y1) = cap_profile[i], cap_profile[i + 1]
                if min(r0, r1) <= rf <= max(r0, r1) and abs(r0 - r1) > 1e-6:
                    y = h + y0 + (y1 - y0) * (rf - r0) / (r1 - r0)
            c = V((math.cos(a) * rf * R, y + 0.0004, math.sin(a) * rf * R))
            s = rnd.uniform(0.0025, 0.005)
            p.lathe(c, (0, 1, 0), [(s, 0.0), (s * 0.8, s * 0.35), (s * 0.35, s * 0.6), (0.0, s * 0.65)], 8)
    return p.obj(bevel=0.0, smooth_angle=70)


# cap profiles: (radius fraction from the rim inward, height above the underside's rim, metres), rim first
CAP_DOME = [(1.0, 0.0), (1.0, 0.004), (0.92, 0.012), (0.75, 0.022), (0.5, 0.029), (0.25, 0.032), (0.0, 0.033)]
CAP_BELL = [(1.0, -0.004), (0.95, 0.006), (0.82, 0.022), (0.62, 0.036), (0.38, 0.046), (0.15, 0.052), (0.0, 0.053)]
CAP_FLAT = [(1.0, 0.0), (1.0, 0.003), (0.9, 0.01), (0.7, 0.016), (0.45, 0.02), (0.2, 0.022), (0.0, 0.0225)]
CAP_BUTTON = [(1.0, 0.0), (0.96, 0.008), (0.82, 0.02), (0.6, 0.03), (0.3, 0.036), (0.0, 0.037)]
# stem profiles: (radius, fraction of the 0.1 m height)
STEM_PLAIN = [(0.012, 0.0), (0.013, 0.05), (0.0105, 0.2), (0.0095, 0.7), (0.0092, 1.0), (0.0, 1.0)]
STEM_SLENDER = [(0.0075, 0.0), (0.008, 0.05), (0.006, 0.4), (0.0055, 1.0), (0.0, 1.0)]
STEM_BULB = [(0.0, 0.0), (0.016, 0.02), (0.018, 0.08), (0.013, 0.18), (0.0105, 0.5), (0.0095, 1.0), (0.0, 1.0)]


def petal_fan(p, n, length, width, cup, tip_round, centre_r, phase=0.0, layers=1, droop=0.0):
    """n petals round the head's centre, each a curved blade of quads (two-sided material), cupped up by `cup`, rounded at
    its tip by `tip_round`, drooping by `droop` at the tip."""
    for layer in range(layers):
        ph = phase + (layer * math.pi / n)
        for k in range(n):
            a = ph + k / n * TAU
            d = V((math.cos(a), 0, math.sin(a)))
            s = V((-math.sin(a), 0, math.cos(a)))
            segs = 5
            rows = []
            for i in range(segs + 1):
                t = i / segs
                r = centre_r + length * t
                # the blade's half-width along its length: narrow at the base, widest past the middle, rounded at the tip
                w = width * 0.5 * (math.sin(math.pi * min(1.0, t * (1.0 - 0.35 * tip_round) + 0.12)) ** 0.8)
                if i == segs:
                    w *= 1.0 - tip_round * 0.8
                y = cup * (t ** 2) * length - droop * (t ** 3) * length + layer * 0.0012
                c = d * r + V((0, y, 0))
                rows.append([p.vert(c - s * w), p.vert(c), p.vert(c + s * w)])
            for i in range(segs):
                r0, r1 = rows[i], rows[i + 1]
                p.face((r0[0], r1[0], r1[1], r0[1]))
                p.face((r0[1], r1[1], r1[2], r0[2]))


def flower_head(name, kind):
    """A flower head facing up (+Y), centred at the origin, 0.05 m across its petals."""
    p = I.Piece(name)
    R = 0.025
    if kind == "star":
        # phlox-like: five broad petals flaring flat from a short tube, a small yellow-green eye
        p.role("petal")
        p.lathe((0, -0.006, 0), (0, 1, 0), [(0.0015, 0.0), (0.0035, 0.004), (0.004, 0.006)], 10, close=False)
        petal_fan(p, 5, R - 0.004, 0.018, 0.12, 0.9, 0.004, droop=0.1)
        p.role("eye").lathe((0, 0.0005, 0), (0, 1, 0), [(0.0035, 0.0), (0.002, 0.0012), (0.0, 0.0016)], 10)
    elif kind == "buttercup":
        # five glossy rounded petals cupped into a bowl, a boss of stamens
        p.role("petal")
        petal_fan(p, 5, R - 0.003, 0.02, 0.9, 1.0, 0.003, droop=0.0)
        p.role("eye")
        p.lathe((0, 0.001, 0), (0, 1, 0), [(0.004, 0.0), (0.0042, 0.002), (0.0025, 0.004), (0.0, 0.0045)], 12)
    else:
        # daisy: a ring of narrow rays round a domed disc
        p.role("petal")
        petal_fan(p, 18, R - 0.0055, 0.0045, 0.04, 0.6, 0.0055, layers=1, droop=0.25)
        p.role("eye")
        p.lathe((0, 0.0, 0), (0, 1, 0), [(0.0062, 0.0), (0.006, 0.0015), (0.0045, 0.003), (0.002, 0.0038), (0.0, 0.004)], 16)
    return p.obj(bevel=0.0, smooth_angle=60)


def flower_stem():
    """A 1 m stem (the game stretches it to the flower's height), its sepal cup at the top."""
    p = I.Piece("plant_flower_stem").role("stem")
    # a gentle S along its length, 6 sides
    pts = [V((0.0, 0.0, 0.0)), V((0.003, 0.25, 0.0)), V((0.0, 0.5, 0.002)), V((-0.003, 0.75, 0.0)), V((0.0, 1.0, 0.0))]
    rings = []
    for i, c in enumerate(pts):
        r = 0.0045 - 0.0012 * i / (len(pts) - 1)
        rings.append(p.ring(c, (0, 1, 0), r, 6))
    for i in range(len(rings) - 1):
        p.bridge(rings[i], rings[i + 1])
    return p.obj(bevel=0.0, smooth_angle=80)


def flower_leaves():
    """A tuft of narrow basal leaves round the foot, arching out (true size)."""
    p = I.Piece("plant_flower_leaves").role("leaf")
    import random
    rnd = random.Random(3)
    for k in range(4):
        a = k / 4 * TAU + rnd.uniform(-0.4, 0.4)
        d = V((math.cos(a), 0, math.sin(a)))
        s = V((-math.sin(a), 0, math.cos(a)))
        L = rnd.uniform(0.08, 0.13)
        segs = 5
        rows = []
        for i in range(segs + 1):
            t = i / segs
            w = 0.006 * math.sin(math.pi * min(1.0, t * 0.92 + 0.08)) ** 0.7
            c = d * (L * t * 0.75) + V((0, L * (0.9 * t - 0.45 * t * t), 0))
            rows.append([p.vert(c - s * w), p.vert(c + V((0, 0.0012 * math.sin(math.pi * t), 0))), p.vert(c + s * w)])
        for i in range(segs):
            r0, r1 = rows[i], rows[i + 1]
            p.face((r0[0], r1[0], r1[1], r0[1]))
            p.face((r0[1], r1[1], r1[2], r0[2]))
    return p.obj(bevel=0.0, smooth_angle=70)


PIECES = {
    "plant_mush_dome": lambda: mushroom("plant_mush_dome", CAP_DOME, STEM_PLAIN, 28),
    "plant_mush_bell": lambda: mushroom("plant_mush_bell", CAP_BELL, STEM_SLENDER, 24),
    "plant_mush_amanita": lambda: mushroom("plant_mush_amanita", CAP_FLAT, STEM_BULB, 36, flecks=14, ring=True),
    "plant_mush_button": lambda: mushroom("plant_mush_button", CAP_BUTTON, STEM_PLAIN, 20),
    "plant_flower_star": lambda: flower_head("plant_flower_star", "star"),
    "plant_flower_buttercup": lambda: flower_head("plant_flower_buttercup", "buttercup"),
    "plant_flower_daisy": lambda: flower_head("plant_flower_daisy", "daisy"),
    "plant_flower_stem": flower_stem,
    "plant_flower_leaves": flower_leaves,
}

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PIECES)
    for name in want:
        I.fresh()
        ob = PIECES[name]()
        I.export(ob, name, 128)
        # no cavity map: with one, FurnitureKit dresses a piece in the indoor dust, and these grow in the open
        cav = os.path.join(I.F.OUT, name + "_cavity.png")
        if os.path.exists(cav):
            os.remove(cav)
