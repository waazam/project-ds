using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// (2026-10-15, the owner: "from now on everything should be made in blender") The doe as the Blender model
/// (tools/Blender/deer.py -> assets/models/deer/deer.glb): one skin on a skeleton, with its clips (graze, head_up, walk,
/// trot, bound, ear_twitch). The behaviour is the same as ever; the clips are chosen by it and advanced by hand each
/// frame, the ear twitch played over them by a second player, the head turned toward a noise on top. The code-built doe
/// (<see cref="BuildModel"/>) stays only for when the model is missing.
/// </summary>
public partial class Deer
{
	public const string ModelPath = "res://assets/models/deer/deer.glb";

	private bool _rigged;
	private Node3D _rig;
	private Skeleton3D _skel;
	private AnimationPlayer _anim, _over;
	private string _clip = "";
	private int _headBone = -1;
	private float _walkFor, _lookYaw;

	/// <summary>For tests and the preview: the model loaded (not the code fallback).</summary>
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
		_skel = FindIn<Skeleton3D>(_rig);
		_anim = FindIn<AnimationPlayer>(_rig);
		if (_skel == null || _anim == null) { _model.QueueFree(); _model = null; return false; }
		FixMaterials(_rig);
		_anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		// the overlay: the same clips, its own player, only ever the ear twitch (it keys the ears alone)
		_over = new AnimationPlayer { Name = "Overlay", RootNode = _anim.RootNode };
		_anim.GetParent().AddChild(_over);
		_over.AddAnimationLibrary("", _anim.GetAnimationLibrary(""));
		_over.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		_headBone = _skel.FindBone("head");
		Play("graze", 0f);
		int tris = 0;
		foreach (var mi in _rig.FindChildren("*", nameof(MeshInstance3D), true, false))
			if (mi is MeshInstance3D m && m.Mesh != null) tris += m.Mesh.GetFaces().Length / 3;
		TriangleCount = tris;
		_rigged = true;
		return true;
	}

	private static T FindIn<T>(Node root) where T : Node
	{
		foreach (var n in root.FindChildren("*", typeof(T).Name, true, false)) if (n is T t) return t;
		return null;
	}

	/// <summary>The fur cards' material ("Hair"): alpha-cut, two-sided, tinted by their vertex colours; the skin: its baked
	/// albedo alone (its painted vertex colours are what the bake was made from, not to be multiplied in again).</summary>
	internal static void FixMaterials(Node root)
	{
		foreach (var n in root.FindChildren("*", nameof(MeshInstance3D), true, false))
		{
			if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
			{
				if (mi.Mesh.SurfaceGetMaterial(s) is not StandardMaterial3D src) continue;
				var m = (StandardMaterial3D)src.Duplicate();
				bool hair = (src.ResourceName ?? "").StartsWith("Hair");
				m.VertexColorUseAsAlbedo = hair;
				if (hair)
				{
					// soft-edged (the solid middles of the strands write depth first, their fading edges blend over): a
					// hard alpha cut made a noisy, ragged edge. Each card is two faces, each lit by the skin's normal at
					// its root, so the back faces are culled rather than flipped dark.
					m.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
					m.CullMode = BaseMaterial3D.CullModeEnum.Back;
					m.Roughness = 1f;
					m.MetallicSpecular = 0.2f;
				}
				mi.SetSurfaceOverrideMaterial(s, m);
			}
		}
	}

	/// <summary>For the preview: held in one clip at time t, the head turned by yaw (as the behaviour turns it toward a
	/// noise: positive to the doe's left).</summary>
	public void PreviewClip(string clip, float t, float yaw = 0f)
	{
		if (!_rigged) return;
		SetProcess(false);
		Play(clip, 0f);
		_anim.Seek(t % (float)_anim.GetAnimation(clip).Length, true);
		_lookYaw = yaw;
		if (_headBone >= 0 && Mathf.Abs(_lookYaw) > 0.001f)
			_skel.SetBonePoseRotation(_headBone, _skel.GetBonePoseRotation(_headBone) * new Quaternion(Vector3.Back, _lookYaw));
	}

	/// <summary>The yaw the behaviour would turn the head by to look at a point (as in Alert).</summary>
	public float YawToward(Vector3 globalPoint)
	{
		Vector3 to = ToLocal(globalPoint);
		return Mathf.Clamp(Mathf.Atan2(-to.X, -to.Z), -0.9f, 0.9f);
	}

	private void Play(string clip, float blend, float speed = 1f)
	{
		if (_anim == null || !_anim.HasAnimation(clip)) return;
		if (_clip != clip)
		{
			_anim.Play(clip, blend, speed);
			if (_anim.GetAnimation(clip) is { } a && clip != "ear_twitch") a.LoopMode = Animation.LoopModeEnum.Linear;
			_clip = clip;
		}
		else _anim.SpeedScale = speed;
	}

	/// <summary>The rigged doe's frame: the clip the behaviour wants, advanced; the ears twitching over it; the head
	/// turned toward a noise.</summary>
	private void RigProcess(float dt)
	{
		switch (Current)
		{
			case State.Grazing:
				// now and then a few steps on along the grass, head still down (it moves itself a little: walking)
				if (_walkFor > 0f)
				{
					_walkFor -= dt;
					Play("walk", 0.4f);
					GlobalPosition += -GlobalBasis.Z * 0.45f * dt * ModelScale;
					if (_terrain != null) GlobalPosition = GlobalPosition with { Y = _terrain.HeightAt(GlobalPosition.X, GlobalPosition.Z) };
				}
				else
				{
					Play(_headDown ? "graze" : "head_up", 0.7f);
					_stepTimer -= dt;
					if (_headDown && _stepTimer <= 0f) { _walkFor = _rng.RandfRange(1.0f, 2.0f); _stepTimer = _rng.RandfRange(6f, 14f); }
				}
				_lookYaw = Mathf.Lerp(_lookYaw, _headDown ? 0f : _neckYaw, 1f - Mathf.Exp(-dt * 2f));
				break;
			case State.Alert:
				Play("head_up", 0.15f);
				_lookYaw = Mathf.Lerp(_lookYaw, _neckYaw, 1f - Mathf.Exp(-dt * 10f));
				break;
			case State.Fleeing:
				// the clip is one bound; played at the bounds the flight takes
				float len = (float)(_anim.GetAnimation("bound")?.Length ?? 0.43);
				Play("bound", 0.12f, BoundsPerSecond * len);
				_lookYaw = Mathf.Lerp(_lookYaw, 0f, 1f - Mathf.Exp(-dt * 6f));
				break;
		}
		_anim.Advance(dt);
		// the ears
		_earTimerL -= dt;
		if (_earTimerL <= 0f && !_over.IsPlaying())
		{
			_over.Play("ear_twitch");
			_earTimerL = _rng.RandfRange(1.5f, 5f);
		}
		if (_over.IsPlaying()) _over.Advance(dt);
		// the head turned toward what it heard (about the head's own up)
		if (_headBone >= 0 && Mathf.Abs(_lookYaw) > 0.001f)
			_skel.SetBonePoseRotation(_headBone, _skel.GetBonePoseRotation(_headBone) * new Quaternion(Vector3.Back, _lookYaw));
	}
}
