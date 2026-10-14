"""The joints check (2026-10-13, the owner: "fix any objects that have disconnects or gaps between joints"): the loose
parts of a model that float. Parts that touch (a corner of one inside the other, or within 3 mm of it, either way
round) are gathered into groups; a group that has something else within 3 cm but touches nothing is reported (a part
meant to meet another that doesn't: a fang under the gum, toes off their foot with their nails on them, a handle off
its box). Measured corner to surface, inside or out, so a tooth sunk in its jaw counts as joined (unlike a box test).

    blender -b --python tools/Blender/gap_check.py -- assets/models/furniture/footlocker.glb [more.glb ...]

Prints one GAP line per floating group (its gap, its size in faces, its centre in Blender's frame: Z up, the model's
front -Y), then a total per model. The game-side audit (GapAudit.cs, gap_audit.tscn) does the posed and in-level half.
Skinned models are checked in their rest pose.
"""
import bpy, bmesh, sys
from mathutils.bvhtree import BVHTree
from mathutils import Vector

TOUCH, NEAR = 0.003, 0.03


def check(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    bm = bmesh.new()
    for o in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        m = o.data.copy()
        m.transform(o.matrix_world)
        bm.from_mesh(m)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    # the islands (faces joined by edges)
    comp, n = {}, 0
    for f in bm.faces:
        if f.index in comp:
            continue
        stack, comp[f.index] = [f], n
        while stack:
            a = stack.pop()
            for e in a.edges:
                for g in e.link_faces:
                    if g.index not in comp:
                        comp[g.index] = n
                        stack.append(g)
        n += 1
    vcomp = {}
    for f in bm.faces:
        for v in f.verts:
            vcomp[v.index] = comp[f.index]
    tree = BVHTree.FromBMesh(bm)
    # which islands touch which: from every corner, the faces of other islands near it
    parent = list(range(n))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    gap = {}   # (island, island) -> the nearest corner-to-surface distance between them, if they don't touch
    for v in bm.verts:
        c = vcomp.get(v.index)
        if c is None:
            continue
        for loc, nrm, idx, d in tree.find_nearest_range(v.co, NEAR):
            o = comp[idx]
            if o == c:
                continue
            if d <= TOUCH or (v.co - loc).dot(nrm) < 0:
                parent[find(c)] = find(o)
            else:
                k = (min(c, o), max(c, o))
                gap[k] = min(gap.get(k, NEAR), d)
    groups = {}
    for c in range(n):
        groups.setdefault(find(c), []).append(c)
    if len(groups) <= 1:
        print(f"CHECKED {path}: {n} parts, 1 group, 0 floating")
        return
    biggest = max(groups, key=lambda g: len(groups[g]))
    found = 0
    for g, members in groups.items():
        best = min((d for (a, b), d in gap.items() if (find(a) == g) != (find(b) == g)), default=None)
        if best is None:
            continue   # nothing near it: a separate thing, not a broken joint
        ms = set(members)
        verts = [i for i, c in vcomp.items() if c in ms]
        cen = sum((bm.verts[i].co for i in verts), Vector()) / len(verts)
        nf = sum(1 for f in bm.faces if comp[f.index] in ms)
        print(f"GAP {best * 1000:5.1f} mm  faces {nf:5d}  parts {len(members):3d}  centre {tuple(round(x, 3) for x in cen)}"
              f"{'  (the main body)' if g == biggest else ''}  {path}")
        found += 1
    print(f"CHECKED {path}: {n} parts, {len(groups)} groups, {found} floating")


if __name__ == "__main__":
    for p in sys.argv[sys.argv.index("--") + 1:]:
        check(p)
