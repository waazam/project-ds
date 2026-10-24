"""Retargets motion from a reference library (Quaternius' Universal Animation Library, CC0: assets/animations/ual) onto the
game's own rigs, and writes it as an animation-only GLB that Godot imports as an AnimationLibrary (2026-10-24, the owner:
"overhaul all of the animations ... with those references").

    blender -b --python tools/Blender/retarget.py -- <job.json> [clip ...]

A job (tools/Blender/data/retarget/<character>.json) names the source GLB, the target rig GLB, the bone map, and the clips:
each a source action, a name in the game, and the character's style layers on top. Writes:

  assets/animations/<character>/<character>_anims.glb   the target's armature (named as in its model, so the tracks'
                                                        paths match the model's Skeleton3D), one skinned triangle to keep
                                                        it a skeleton in Godot, and one action per clip
  build/blender/anim_<character>_<clip>.png             a contact sheet per clip: the target model at four moments, from
                                                        the side and the front

How the motion moves across (the classic world-delta method): both rigs are brought into one frame (the source turned by
`source_yaw` degrees about Z: the library faces -Y, the game's rigs +Y), and each mapped target bone is given, frame by
frame, the same rotation away from its own rest pose (in armature space) that its source bone has away from the source's
rest. So a hunched creature stays hunched and walks the human's walk; a T-posed source and an A-posed target agree. The
hips' travel is scaled by the ratio of the hips' rest heights. Unmapped bones (rags, a jaw, a second neck bone) keep their
rest pose and follow their parents.

Two ways a bone takes its source's motion (style "mode", by bone prefix; "delta" unless set):
  delta  the same rotation away from its rest as the source bone's from its own (the spine: a hunch stays a hunch)
  dir    the bone points where its source points, swung from where its parent's pose leaves it (the limbs: rest poses
         that don't match, a T-posed library and an A-posed creature, don't matter)

Style layers (per clip, over a character's defaults; all optional):
  amplitude   {bone_prefix: 0..1.5}   how much of the motion that bone takes (1 all of it; 0 holds rest)
  offset      {bone: [x, y, z]}        an extra pose rotation, degrees, in the bone's local frame (a hunch, a head tilt)
  noise       {bone_prefix: degrees}   a slow coherent tremble (the stalker's tics), baked
  replace     {prefix: true}           with a base: these bones point where their source points (legs), blended from
                                       the base pose by amplitude, instead of adding the source's motion
  hips_scale  float                    the hips' travel scale on top of the height ratio
  frames      [start, end]             the slice of the source action
  step        int                      sample every n-th source frame (key density; the rate is the game's to set)
  loop        bool                     blend the last few frames into the first so it cycles cleanly
  base        clip name                additive: the character's own (old) clip, read at the same phase, is the pose;
                                       the source's motion, measured from its own mean pose over the clip, goes on top,
                                       scaled by amplitude (its posture never does). The unmapped bones keep the base's
                                       motion. hips_amplitude scales the hips' bob.
"""
import bpy, json, math, os, sys
from mathutils import Matrix, Quaternion, Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
PREVIEW = os.path.join(ROOT, "build", "blender")
os.makedirs(PREVIEW, exist_ok=True)


def rel(p):
    return p if os.path.isabs(p) else os.path.join(ROOT, p)


def load_job():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        raise SystemExit("usage: blender -b --python tools/Blender/retarget.py -- <job.json> [clip ...]")
    with open(rel(argv[0]), encoding="utf-8") as f:
        job = json.load(f)
    only = set(argv[1:])
    return job, only


def import_glb(path, tag):
    before = set(bpy.data.objects)
    actions_before = set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=rel(path))
    objs = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in objs if o.type == "ARMATURE")
    for o in objs:
        o["rt_tag"] = tag
    return arm, objs, [a for a in bpy.data.actions if a not in actions_before]


# ------------------------------------------------------------------ the transfer

def rest_matrices(arm):
    return {b.name: b.matrix_local.copy() for b in arm.data.bones}


def evaluated_pose(arm):
    """Armature-space pose matrices of every bone, as evaluated now."""
    return {pb.name: pb.matrix.copy() for pb in arm.pose.bones}


def qscale(q, k):
    """The rotation q taken k times as far (k may exceed 1: an exaggeration)."""
    if q.w < 0:
        q = -q
    axis, angle = q.to_axis_angle()
    return Quaternion(axis, angle * k)


def style_get(table, bone, default):
    """The value for the longest key that `bone` starts with (so 'f' covers every finger, 'upper' both arms)."""
    best, val = -1, default
    for k, v in (table or {}).items():
        if bone.startswith(k) and len(k) > best:
            best, val = len(k), v
    return val


def transfer_clip(src, tgt, bone_map, clip, defaults, yaw):
    """Bakes one clip onto the target as a new action. Returns the action."""
    style = dict(defaults)
    for k, v in clip.get("style", {}).items():
        style[k] = {**style.get(k, {}), **v} if isinstance(v, dict) and isinstance(style.get(k), dict) else v
    # (Blender 4 imports the library's clips as "<name>_Armature", Blender 5 as "<name>")
    src_action = bpy.data.actions.get(clip["source"]) or bpy.data.actions.get(clip["source"] + "_Armature")         or bpy.data.actions.get(clip["source"].removesuffix("_Armature"))
    if src_action is None:
        raise SystemExit(f"[retarget] no source action '{clip['source']}'")
    src.animation_data_create()
    src.animation_data.action = src_action
    if hasattr(src.animation_data, "action_slot") and src.animation_data.action_slot is None and len(src_action.slots):
        src.animation_data.action_slot = src_action.slots[0]
    f0, f1 = clip.get("frames", [int(src_action.frame_range[0]), int(src_action.frame_range[1])])
    step = max(1, int(clip.get("step", 1)))

    C = Matrix.Rotation(math.radians(yaw), 4, "Z")
    Cq = C.to_quaternion()
    src_rest = rest_matrices(src)
    tgt_rest = rest_matrices(tgt)
    # the hips' height ratio (the travel scales with it)
    s_hips, t_hips = bone_map.get("_hips_source", "pelvis"), bone_map.get("_hips_target", "hips")
    ratio = tgt_rest[t_hips].translation.z / max(0.01, src_rest[s_hips].translation.z)
    ratio *= style.get("hips_scale", 1.0)

    mapping = {t: s for s, t in bone_map.items() if not s.startswith("_") and t in tgt.data.bones and s in src.data.bones}
    order = [b for b in tgt.data.bones]   # (bones come parent first)
    act = bpy.data.actions.new(clip["name"])
    tgt.animation_data_create()
    tgt.animation_data.action = act
    for pb in tgt.pose.bones:
        pb.rotation_mode = "QUATERNION"

    frames = list(range(f0, f1 + 1, step))
    if frames[-1] != f1:
        frames.append(f1)
    scene = bpy.context.scene
    # the base: the character's own (old) clip, read at the same phase, in place of the rest pose
    base_poses, base_basis = None, None
    base_name = clip.get("base")
    if base_name:
        base_act = bpy.data.actions.get("_old_" + base_name)
        if base_act is None:
            raise SystemExit(f"[retarget] no base clip '{base_name}' on the target")
        tgt.animation_data.action = base_act
        if hasattr(tgt.animation_data, "action_slot") and len(base_act.slots):
            tgt.animation_data.action_slot = base_act.slots[0]
        b0, b1 = base_act.frame_range
        base_poses, base_basis = [], []
        for f in frames:
            ph = (f - f0) / max(1, f1 - f0)
            scene.frame_set(int(round(b0 + ph * (b1 - b0))))
            base_poses.append({pb.name: pb.matrix.copy() for pb in tgt.pose.bones})
            base_basis.append({pb.name: pb.matrix_basis.copy() for pb in tgt.pose.bones})
        tgt.animation_data.action = act
    baked = {b.name: [] for b in order}
    # additive (with a base): the source's motion is measured from its own mean pose over the clip, so only the motion
    # goes across (its posture is the base's)
    mean_q, mean_dir, mean_hips = {}, {}, None
    if base_poses:
        acc_q, acc_d, acc_h = {}, {}, Vector()
        for f in frames:
            scene.frame_set(f)
            for pb in src.pose.bones:
                q = pb.matrix.to_quaternion()
                if pb.name in acc_q and acc_q[pb.name].dot(q) < 0:
                    q = -q
                acc_q[pb.name] = acc_q.get(pb.name, Quaternion((0, 0, 0, 0))) + q if pb.name in acc_q else q.copy()
                acc_d[pb.name] = acc_d.get(pb.name, Vector()) + pb.matrix.to_3x3() @ Vector((0.0, 1.0, 0.0))
            acc_h += src.pose.bones[s_hips].matrix.translation
        mean_q = {k: Quaternion(v).normalized() for k, v in acc_q.items()}
        mean_dir = {k: v.normalized() for k, v in acc_d.items()}
        mean_hips = acc_h / len(frames)
    hips_loc = []
    for fi, f in enumerate(frames):
        scene.frame_set(f)
        sp = evaluated_pose(src)
        bp = base_poses[fi] if base_poses else None
        bb = base_basis[fi] if base_basis else None
        pose = {}
        for bone in order:
            name = bone.name
            rest = tgt_rest[name]
            if bone.parent:
                prest = tgt_rest[bone.parent.name]
                parent_pose = pose[bone.parent.name]
                rel_rest = prest.inverted() @ rest
                chain = parent_pose @ rel_rest   # where the parent puts this bone at its own rest rotation
            else:
                chain = rest.copy()
            rest_chain = chain   # (what the keyed basis is relative to: the parent's pose, this bone at rest)
            if bb is not None:
                # unmapped bones take the base clip's own motion (a jaw, the rags, a second neck bone)
                chain = chain @ bb[name]
            want_rot = chain.to_quaternion()
            if name in mapping:
                sname = mapping[name]
                amp = style_get(style.get("amplitude"), name, 1.0)
                if bp is not None and style_get(style.get("replace"), name, False):
                    # with a base, but this bone points where its source points (legs: the step pattern must be one
                    # clip's, or two out-of-phase swings cancel), blended from the base pose by amplitude
                    sd = (Cq @ (sp[sname].to_3x3() @ Vector((0.0, 1.0, 0.0)))).normalized()
                    cur = (chain.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
                    full = cur.rotation_difference(sd) @ chain.to_quaternion()
                    want_rot = chain.to_quaternion().slerp(full, min(1.0, amp))
                elif bp is not None:
                    # additive: the source's turn from its mean, scaled, on the base clip's pose
                    if style_get(style.get("mode"), name, "delta") == "dir":
                        sd = (Cq @ (sp[sname].to_3x3() @ Vector((0.0, 1.0, 0.0)))).normalized()
                        md = (Cq @ mean_dir[sname]).normalized()
                        r = qscale(md.rotation_difference(sd), amp)
                        bd = (bp[name].to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
                        d = (r @ bd).normalized()
                        cur = (chain.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
                        want_rot = cur.rotation_difference(d) @ chain.to_quaternion()
                    else:
                        dq = sp[sname].to_quaternion() @ mean_q[sname].inverted()
                        dq = Cq @ qscale(dq, amp) @ Cq.inverted()
                        want_rot = dq @ bp[name].to_quaternion()
                elif style_get(style.get("mode"), name, "delta") == "dir":
                    # the limb points where the source's points (a swing from where its parent's pose leaves it): rest
                    # poses that don't match (a T-posed library, an A-posed creature) don't matter
                    sd = (Cq @ (sp[sname].to_3x3() @ Vector((0.0, 1.0, 0.0)))).normalized()
                    cur = (chain.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
                    full = cur.rotation_difference(sd) @ chain.to_quaternion()
                    want_rot = chain.to_quaternion().slerp(full, amp) if amp != 1.0 else full
                else:
                    # the same turn away from its rest (or the base clip's pose) as the source's from its own rest: a
                    # hunch stays a hunch. With a base, the amount scales the source's turn, not a blend back to rest.
                    delta = Cq @ sp[sname].to_quaternion() @ src_rest[sname].to_quaternion().inverted() @ Cq.inverted()
                    if amp != 1.0:
                        delta = qscale(delta, amp)
                    anchor = bp[name].to_quaternion() if bp is not None else rest.to_quaternion()
                    want_rot = delta @ anchor
            # a slow tremble, coherent over time
            nz = style_get(style.get("noise"), name, 0.0)
            if nz:
                t = fi * step * 0.11
                seed = (hash(name) % 997) * 0.37
                e = Vector((noise.noise(Vector((t, seed, 0.0))), noise.noise(Vector((seed, t, 1.7))), noise.noise(Vector((2.3, seed, t)))))
                want_rot = want_rot @ Quaternion(e.normalized() if e.length > 1e-6 else Vector((1, 0, 0)), math.radians(nz) * min(1.0, e.length * 1.6))
            off = style.get("offset", {}).get(name)
            if off:
                want_rot = want_rot @ Matrix.Rotation(math.radians(off[0]), 4, "X").to_quaternion() \
                    @ Matrix.Rotation(math.radians(off[1]), 4, "Y").to_quaternion() @ Matrix.Rotation(math.radians(off[2]), 4, "Z").to_quaternion()
            pos = chain.translation.copy()
            if name == t_hips:
                d = C @ (sp[s_hips].translation - src_rest[s_hips].translation)
                if bp is not None:
                    d = C @ (sp[s_hips].translation - mean_hips)
                    pos = bp[name].translation + d * ratio * style.get("hips_amplitude", 1.0)
                else:
                    pos = rest.translation + d * ratio
            M = Matrix.Translation(pos) @ want_rot.to_matrix().to_4x4()
            pose[name] = M
            # basis = (parent_pose @ rel_rest)^-1 @ M
            basis = rest_chain.inverted() @ M
            baked[name].append(basis)
        hips_loc.append(None)

    # a clean cycle: ease the last frames toward the first
    if clip.get("loop"):
        n = max(2, len(frames) // 6)
        for name, mats in baked.items():
            first = mats[0]
            for i in range(n):
                k = (i + 1) / n
                j = len(mats) - n + i
                q = mats[j].to_quaternion().slerp(first.to_quaternion(), k * k * (3 - 2 * k))
                t = mats[j].translation.lerp(first.translation, k * k * (3 - 2 * k))
                mats[j] = Matrix.Translation(t) @ q.to_matrix().to_4x4()
            mats[-1] = first.copy()

    # keys, from frame 0
    for name, mats in baked.items():
        pb = tgt.pose.bones[name]
        prev = None
        for i, m in enumerate(mats):
            frame = (frames[i] - f0)
            loc, rot, _ = m.decompose()
            if prev is not None and prev.dot(rot) < 0:
                rot = -rot   # (the short way round: no flips between keys)
            prev = rot
            pb.rotation_quaternion = rot
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=name)
            if name == t_hips or loc.length > 1e-4:
                pb.location = loc
                pb.keyframe_insert("location", frame=frame, group=name)
    act.use_fake_user = True
    print(f"[retarget] {clip['source']} -> {clip['name']}: {len(frames)} keys over {f1 - f0} frames, {len(mapping)} bones mapped")
    return act, frames[-1] - f0


# ------------------------------------------------------------------ previews

def contact_sheet(tgt, meshes, act, length, out):
    """Four moments of the clip, side and front, the target's own model drawn by the workbench."""
    scene = bpy.context.scene
    tgt.animation_data.action = act
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x, scene.render.resolution_y = 360, 480
    scene.render.film_transparent = False
    scene.world = scene.world or bpy.data.worlds.new("w")
    cam_data = bpy.data.cameras.get("rt_cam") or bpy.data.cameras.new("rt_cam")
    cam = bpy.data.objects.get("rt_cam") or bpy.data.objects.new("rt_cam", cam_data)
    if cam.name not in scene.collection.objects:
        scene.collection.objects.link(cam)
    scene.camera = cam
    # frame the figure by its rest bounds
    pts = [tgt.matrix_world @ b.head_local for b in tgt.data.bones]
    top = max(p.z for p in pts)
    mid = Vector((0, 0, top * 0.5))
    tiles = []
    import numpy as np
    for view, yaw in (("side", 90.0), ("front", 0.0)):
        for k in range(4):
            f = int(round(length * k / 4))
            scene.frame_set(f)
            r = top * 1.45 + 1.0
            a = math.radians(yaw)
            cam.location = mid + Vector((math.sin(a) * r, math.cos(a) * r, top * 0.08))
            cam.rotation_euler = (mid - cam.location).to_track_quat("-Z", "Y").to_euler()
            cam_data.lens = 50
            path = os.path.join(PREVIEW, f"_tile_{view}_{k}.png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(path)
            px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
            tiles.append(px)
            bpy.data.images.remove(img)
            os.remove(path)
    h, w = tiles[0].shape[:2]
    sheet = np.zeros((h * 2, w * 4, 4), dtype=np.float32)
    for i, t in enumerate(tiles):
        r_, c_ = divmod(i, 4)
        sheet[(1 - r_) * h:(2 - r_) * h, c_ * w:(c_ + 1) * w] = t   # (Blender's pixel rows run bottom up)
    img = bpy.data.images.new("rt_sheet", w * 4, h * 2, alpha=True)
    img.pixels = sheet.ravel()
    img.filepath_raw = out
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print(f"[retarget] preview {out}")


# ------------------------------------------------------------------ export

def export_anims(tgt, actions, out):
    """The armature and one skinned triangle (so Godot builds a Skeleton3D and the tracks' paths match the model's)."""
    for o in list(bpy.data.objects):
        if o.get("rt_tag") != "target" or o is tgt:
            if o is not tgt:
                bpy.data.objects.remove(o, do_unlink=True)
    import bmesh
    me = bpy.data.meshes.new("AnimAnchor")
    bm = bmesh.new()
    for v in ((0, 0, 0), (0.001, 0, 0), (0, 0.001, 0)):
        bm.verts.new(v)
    bm.faces.new(bm.verts)
    bm.to_mesh(me)
    bm.free()
    anchor = bpy.data.objects.new("AnimAnchor", me)
    bpy.context.scene.collection.objects.link(anchor)
    root = tgt.data.bones[0].name
    vg = anchor.vertex_groups.new(name=root)
    vg.add([0, 1, 2], 1.0, "REPLACE")
    anchor.parent = tgt
    mod = anchor.modifiers.new("Armature", "ARMATURE")
    mod.object = tgt
    # only the new actions go out
    for a in list(bpy.data.actions):
        if a not in actions:
            bpy.data.actions.remove(a)
    tgt.animation_data.action = actions[0]
    for o in bpy.context.scene.objects:
        o.select_set(o in (tgt, anchor))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_yup=True, export_apply=False,
                              export_animations=True, export_skins=True, export_animation_mode="ACTIONS",
                              export_force_sampling=True, export_materials="NONE")
    print(f"[retarget] exported {out}: {len(actions)} clips")


def measure_cycles(job, clips, ratio_of):
    """Metres the library's root travels over each clip (from its root-motion twin), times the hips' height ratio: how
    far one cycle of the retargeted clip carries the target, so the game can set the playback rate from ground speed."""
    rm = job.get("root_motion_source")
    if not rm:
        return {}
    keep = set(bpy.data.objects)
    keep_actions = set(bpy.data.actions)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=rel(rm))
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == "ARMATURE")
    out = {}
    for clip in clips:
        base = clip["source"].removesuffix("_Armature")
        act = next((a for a in bpy.data.actions if a not in keep_actions and a.name.startswith(base)), None)
        if act is None:
            continue
        arm.animation_data_create()
        arm.animation_data.action = act
        if hasattr(arm.animation_data, "action_slot") and arm.animation_data.action_slot is None and len(act.slots):
            arm.animation_data.action_slot = act.slots[0]
        f0, f1 = clip.get("frames", [int(act.frame_range[0]), int(act.frame_range[1])])
        bpy.context.scene.frame_set(f0)
        a = arm.pose.bones[0].matrix.translation.copy()
        bpy.context.scene.frame_set(f1)
        b = arm.pose.bones[0].matrix.translation.copy()
        d = b - a
        out[clip["name"]] = round((Vector((d.x, d.y, 0.0))).length * ratio_of, 4)
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        if a not in keep_actions:
            bpy.data.actions.remove(a)
    return out


def preview_existing(job, only):
    """Contact sheets of the target's own (old) clips of the same names, for a fair before/after."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tgt, objs, acts = import_glb(job["target"], "target")
    meshes = [o for o in objs if o.type == "MESH"]
    for clip in job["clips"]:
        name = clip.get("compare", clip["name"])
        if only and clip["name"] not in only:
            continue
        act = bpy.data.actions.get(name)
        if act is None:
            continue
        tgt.animation_data_create()
        tgt.animation_data.action = act
        if hasattr(tgt.animation_data, "action_slot") and len(act.slots):
            tgt.animation_data.action_slot = act.slots[0]
        length = int(act.frame_range[1] - act.frame_range[0])
        contact_sheet(tgt, meshes, act, length, os.path.join(PREVIEW, f"anim_{job['character']}_{clip['name']}_old.png"))


def main():
    job, only = load_job()
    if "--old" in sys.argv:
        only.discard("--old")
        preview_existing(job, only)
        return
    bpy.ops.wm.read_factory_settings(use_empty=True)
    with open(rel(job["map"]), encoding="utf-8") as f:
        bone_map = json.load(f)
    src, src_objs, src_actions = import_glb(job["source"], "source")
    for o in src_objs:
        if o.type == "MESH":
            o.hide_render = True
    tgt, tgt_objs, tgt_actions = import_glb(job["target"], "target")
    for a in tgt_actions:
        a.name = "_old_" + a.name
    if tgt.animation_data:
        tgt.animation_data.action = None
    for pb in tgt.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    # the source off to the side (it is only read)
    src.location.x += 50.0
    bpy.context.view_layer.update()
    src.location.x -= 50.0
    meshes = [o for o in tgt_objs if o.type == "MESH"]
    defaults = job.get("style", {})
    made = []
    for clip in job["clips"]:
        if only and clip["name"] not in only:
            continue
        act, length = transfer_clip(src, tgt, bone_map, clip, defaults, job.get("source_yaw", 180.0))
        made.append(act)
        if not job.get("no_preview"):
            contact_sheet(tgt, meshes, act, length, os.path.join(PREVIEW, f"anim_{job['character']}_{clip['name']}.png"))
    if not made:
        raise SystemExit("[retarget] no clips")
    # the sidecar: each clip's length and, for the gaits, how far one cycle carries the target (metres at scale 1)
    src_rest, tgt_rest = rest_matrices(src), rest_matrices(tgt)
    ratio = tgt_rest[bone_map.get("_hips_target", "hips")].translation.z / max(0.01, src_rest[bone_map.get("_hips_source", "pelvis")].translation.z)
    clips = [c for c in job["clips"] if not only or c["name"] in only]
    cycles = measure_cycles(job, clips, ratio)
    fps = bpy.context.scene.render.fps
    meta = {a.name: {"seconds": round((a.frame_range[1] - a.frame_range[0]) / fps, 4), "cycle_m": cycles.get(a.name, 0.0),
                     "loop": bool(next((c.get("loop") for c in clips if c["name"] == a.name), False))} for a in made}
    export_anims(tgt, made, rel(job["out"]))
    side = os.path.splitext(rel(job["out"]))[0] + ".json"
    old = {}
    if only and os.path.exists(side):
        with open(side, encoding="utf-8") as f:
            old = json.load(f)
    old.update(meta)
    with open(side, "w", encoding="utf-8") as f:
        json.dump(old, f, indent=1)
    print(f"[retarget] sidecar {side}: {meta}")


main()
