# Loads a retargeted AnimationLibrary and a model, and checks every track of every clip finds its bone on the model.
#   godot --headless --path . --script tools/checks/anim_lib_check.gd -- <library.glb> <model.glb>
extends SceneTree

func _init():
	var args = OS.get_cmdline_user_args()
	var lib: AnimationLibrary = load(args[0])
	var model: Node = (load(args[1]) as PackedScene).instantiate()
	var player: AnimationPlayer = model.find_children("*", "AnimationPlayer", true, false)[0] if model.find_children("*", "AnimationPlayer", true, false).size() > 0 else null
	var root: Node = player.get_node(player.root_node) if player else model
	print("CHECK library clips: ", lib.get_animation_list())
	var bad := 0
	for name in lib.get_animation_list():
		var a: Animation = lib.get_animation(name)
		var miss := 0
		for t in a.get_track_count():
			var p: NodePath = a.track_get_path(t)
			var node = root.get_node_or_null(NodePath(p.get_concatenated_names()))
			if node == null: miss += 1; continue
			if node is Skeleton3D and p.get_subname_count() > 0 and node.find_bone(p.get_subname(0)) < 0: miss += 1
		print("CHECK ", name, ": ", a.get_track_count(), " tracks, ", miss, " unmatched, length ", a.length, " path0 ", a.track_get_path(0))
		bad += miss
	print("CHECK done, unmatched ", bad)
	quit()
