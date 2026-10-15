using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// (2026-10-15, the owner: "from now on everything should be made in blender") The squirrel as the Blender model
/// (tools/Blender/squirrel.py -> assets/models/squirrel/squirrel.glb): one skin on a skeleton, its plume of fur cards on
/// the tail's chain, with its clips (forage, sit_up, hop, run, climb, tail_flick). The behaviour is unchanged: it moves the
/// squirrel (and turns it onto a trunk), and picks the clip; the clips are advanced by hand, the tail flick played over
/// them by a second player. The code-built squirrel (<see cref="BuildModel"/>) only when the model is missing.
/// </summary>
public partial class Squirrel
{
	public const string ModelPath = "res://assets/models/squirrel/squirrel.glb";

	private bool _rigged;
	private Node3D _rig;
	private AnimationPlayer _anim, _over;
	private string _clip = "";
	private float _flickIn = 2f;

	public bool Rigged => _rigged;

	private bool TryLoadRig()
	{
		if (!ResourceLoader.Exists(ModelPath)) return false;
		var scene = GD.Load<PackedScene>(ModelPath);
		if (scene == null) return false;
		_model = new Node3D { Name = "Model", Scale = Vector3.One * ModelScale };
		AddChild(_model);
		_rig = scene.Instantiate<Node3D>();
		_model.AddChild(_rig);
		foreach (var n in _rig.FindChildren("*", nameof(AnimationPlayer), true, false)) { _anim = n as AnimationPlayer; break; }
		if (_anim == null) { _model.QueueFree(); _model = null; return false; }
		Deer.FixMaterials(_rig);
		_anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		_over = new AnimationPlayer { Name = "Overlay", RootNode = _anim.RootNode };
		_anim.GetParent().AddChild(_over);
		_over.AddAnimationLibrary("", _anim.GetAnimationLibrary(""));
		_over.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		int tris = 0;
		foreach (var mi in _rig.FindChildren("*", nameof(MeshInstance3D), true, false))
			if (mi is MeshInstance3D m && m.Mesh != null) tris += m.Mesh.GetFaces().Length / 3;
		TriangleCount = tris;
		_rigged = true;
		Clip("forage", 0f);
		return true;
	}

	private void Clip(string name, float blend, float speed = 1f)
	{
		if (_anim == null || !_anim.HasAnimation(name)) return;
		if (_clip != name)
		{
			if (_anim.GetAnimation(name) is { } a) a.LoopMode = Animation.LoopModeEnum.Linear;
			_anim.Play(name, blend, speed);
			_clip = name;
		}
		_anim.SpeedScale = speed;
	}

	/// <summary>Each frame (rigged): the clip advanced, and now and then the tail flicked over it.</summary>
	private void RigAdvance(float dt)
	{
		_anim.Advance(dt);
		_flickIn -= dt;
		if (_flickIn <= 0f && !_over.IsPlaying() && _clip is "forage" or "sit_up")
		{
			_over.Play("tail_flick");
			_flickIn = _rng.RandfRange(1.5f, 4.5f);
		}
		if (_over.IsPlaying()) _over.Advance(dt);
	}
}
