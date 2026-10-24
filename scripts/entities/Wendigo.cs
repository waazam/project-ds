using Godot;
using ProjectDS.Audio;
using ProjectDS.World;
using ProjectDS.World.LakeParts;

namespace ProjectDS.Entities;

/// <summary>
/// The wendigo (Act 22 on; the owner's "best monster yet", after their references and the lore). The winter
/// woods' apex predator: in Act 22 it only stalks. It never touches the player; it is glimpsed, for a moment,
/// when they turn round, and the instant they look anywhere near it, it is gone, up into the trees faster than
/// anything should move, the boughs dumping their snow behind it (<see cref="Leap"/>). It speaks in voices it has
/// copied from people, and they come out wrong. Its steps are almost nothing.
///
/// The look (Algonquian accounts of it, and the owner's pictures; the owner, 2026-09-30: taller, lanky, gaunt, ribs out
/// of its chest, far more detail): a giant and yet starved, nearly five metres to its antlers' tips, hunched; ash-grey
/// skin drawn tight over the bones, every rib, vertebra and knuckle standing out, the belly sunk to the backbone; arms
/// far too long, hanging past its knees to frostbitten black hands, the fingers long and knuckled and hooked with
/// black claws; legs that bend backward like a deer's, on long feet and three clawed toes; a matted black mantle off
/// its shoulders, frosted at the tips. A hole torn in its chest, the ribs showing through it as bone, and behind them,
/// where a heart should be, a shard of blue ice glowing faintly (the lore's heart of ice). Its head an elk's skull,
/// bleached and cracked, a man's jaw hung under it full of long uneven teeth and torn lips, deep sockets with a
/// pinprick of cold light in each, a rack of antlers, one snapped short, hung with dead velvet. Its head hangs to one
/// side, and twitches.
///
/// The model is Blender's (tools/Blender/wendigo.py: assets/models/wendigo/wendigo.glb): about 70,000 triangles,
/// 2048 px baked albedo and normal maps (pores, wrinkles, veins, cracks in the bone), skinned to a skeleton (spine,
/// neck, head, jaw; arms to every finger joint; digitigrade legs to the toes) with its animations: idle, crouch (to
/// spring), air (the legs folding under it), pounce (flung at its prey). The head twitches and the jaw speaks on top.
///
/// The node's origin is on the ground between its feet; it faces -Z.
/// </summary>
public partial class Wendigo : Node3D
{
	public const float Height = 4.7f;
	public int Leaps { get; private set; }
	public bool Leaping => _leapT >= 0f;
	/// <summary>Where its chest is (for the view tests).</summary>
	public Vector3 ChestWorld => _skel != null && _chest >= 0 && !_headOnly
		? _skel.GlobalTransform * _skel.GetBoneGlobalPose(_chest).Origin + GlobalBasis * new Vector3(0, 0, -0.25f)
		: ToGlobal(new Vector3(0, 3.3f, -0.3f));

	private Node3D _body, _model, _head, _headOnlyModel;
	private Skeleton3D _skel;
	private AnimationPlayer _anim;
	/// <summary>(2026-10-24, the animation overhaul) Its gaits from the retargeted library (tools/Blender/retarget.py: the
	/// reference library's captured motion layered on its own clips' posture). Off: the hand-keyed clips, as before.</summary>
	[Export] public bool RetargetedGaits = true;
	public const string AnimLibPath = "res://assets/animations/wendigo/wendigo_anims.glb";
	private readonly System.Collections.Generic.Dictionary<string, string> _remap = new();
	/// <summary>The clip actually played for a clip name (its retargeted version when there is one).</summary>
	public string Clip(string name) => _remap.TryGetValue(name, out var r) ? r : name;
	/// <summary>How far one cycle of a gait carries it at full size (metres): the retargeted clip's measured stride, else
	/// <paramref name="fallback"/> (the hand-keyed clip's).</summary>
	public float CycleMetres(string name, float fallback)
	{
		if (!_remap.ContainsKey(name)) return fallback;
		return Systems.AnimLibraries.Meta(AnimLibPath).TryGetValue(_remap[name]["ual/".Length..], out var m) && m.cycle > 0.01f ? m.cycle : fallback;
	}
	private int _headBone = -1, _jawBone = -1, _chest = -1;
	private bool _headOnly;
	private GpuParticles3D _breath;
	private readonly RandomNumberGenerator _rng = new() { Seed = 666 };
	private double _time, _twitchAt = 1.2, _breathAt = 2.0;
	// (2026-10-15) the owl's roll, the stop-motion jerks, the slack jaw, the lurk
	private double _rollAt = 3.0, _rollUntil = -1, _stutterAt = 2.0, _stutterUntil = -1;
	private float _roll, _rollGoal, _stutterAcc;
	private const float StutterStep = 0.085f;
	/// <summary>How far the jaw hangs open at rest (radians), and how far it hunches over its chest while standing.</summary>
	[Export] public float JawSlack = 0.22f, Hunch = 0.12f;
	private Vector3 _twitch, _twitchGoal;
	private Vector3 _headRest = new(0.1f, 0f, 0.25f);
	private float _leapT = -1f, _leapDur, _speakUntil = -1f;
	// (2026-10-10c, the owner: "this whole animation sequence needs to be worked on a ton, it is so janky and not fluid at
	// all ... making the wendigo move fluidly"): a real gather before a spring (it was 0.07 s: no crouch at all, a pop)
	private const float Crouch = 0.24f;
	/// <summary>The shortest blend between two of its clips: under it, pose to pose was a pop.</summary>
	private const double MinBlend = 0.22;
	// the visible body follows the creature's place and facing on a spring (below)
	private Vector3 _visPos, _visVel;
	private Quaternion _visRot = Quaternion.Identity;
	private bool _visSet;
	private float _size = 1f;
	private string _leapClip = "air", _landClip;
	private float _arc = 1.2f, _landSeconds = 1f;
	private bool _ballistic;
	private Vector3 _leapFrom, _leapTo;
	private System.Action _landed;
	private bool _stayOnLanding;
	/// <summary>Points on the skull in the head bone's space (the mouth, the skull's middle), from the model's rest pose.</summary>
	private Vector3 _mouthLocal, _skullLocal;

	// (Blender's coordinates, as the model was built: x, its front y, up z; the glTF turns them to x, z, -y)
	private static Vector3 FromBlender(float x, float y, float z) => new(x, z, -y);
	private static readonly Vector3 HeadJoint = FromBlender(0, 0.74f, 3.9f);
	private static readonly Vector3 Mouth = FromBlender(0, 0.74f + 0.4f, 3.9f - 0.24f);
	private static readonly Vector3 SkullMid = FromBlender(0, 0.74f + 0.22f, 3.9f + 0.02f);

	public override void _Ready()
	{
		CreatureRim.Apply(this);
		_body = new Node3D { Name = "Body" };
		AddChild(_body);
		_model = GD.Load<PackedScene>("res://assets/models/wendigo/wendigo.glb").Instantiate<Node3D>();
		_model.Name = "Model";
		_body.AddChild(_model);
		_skel = Find<Skeleton3D>(_model);
		_anim = Find<AnimationPlayer>(_model);
		if (_skel != null)
		{
			_headBone = _skel.FindBone("head");
			_jawBone = _skel.FindBone("jaw");
			_chest = _skel.FindBone("chest");
			var headRest = _skel.GetBoneGlobalRest(_headBone);
			_mouthLocal = headRest.AffineInverse() * Mouth;
			_skullLocal = headRest.AffineInverse() * SkullMid;
			_head = new BoneAttachment3D { Name = "Head", BoneName = "head" };
			_skel.AddChild(_head);
		}
		else _head = new Node3D { Name = "Head" };
		if (_anim != null)
		{
			_anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
			foreach (var loop in new[] { "idle", "walk", "run", "lurk", "approach", "crawl", "sniff" })
				if (_anim.GetAnimation(loop) is { } a) a.LoopMode = Animation.LoopModeEnum.Linear;
			if (RetargetedGaits && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-retarget") < 0 && Systems.AnimLibraries.Attach(_anim, "ual", AnimLibPath, "walk", "run", "idle", "stalk"))
				foreach (var (from, to) in new[] { ("walk", "ual/walk"), ("run", "ual/run"), ("idle", "ual/idle"), ("approach", "ual/stalk") })
					if (_anim.HasAnimation(to)) _remap[from] = to;
			_anim.Play(Clip("idle"));
		}
		Tune(_model);
		BuildBreath();
		PhotoSubject.Attach(_head, "wendigo", _skullLocal, 3f, 60f, 10f, false);
	}

	private static T Find<T>(Node n) where T : Node
	{
		foreach (var c in n.GetChildren())
		{
			if (c is T t) return t;
			var f = Find<T>(c);
			if (f != null) return f;
		}
		return null;
	}

	/// <summary>The imported materials, tuned for the game's dark: the bone and the skin a faint pallor of their own (so
	/// the skull, the rack and the gaunt body show in the murk, never a glare), the eyes' and the heart's cold glow.</summary>
	private static void Tune(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is MeshInstance3D mi && mi.Mesh != null)
			{
				RestSpace(mi);
				for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
				{
					if (mi.Mesh.SurfaceGetMaterial(s) is not StandardMaterial3D m) continue;
					string name = m.ResourceName ?? "";
					m.SetMeta("detail_kind", -1);
					if (name.StartsWith("w_bone"))
					{
						// (2026-10-10c) the skull and the rack: old bone, yellowed and stained, split, blood dried round the
						// jaw; a faint pallor of its own still, so it shows in the murk
						mi.SetSurfaceOverrideMaterial(s, BoneMaterial(m));
					}
					else if (name.StartsWith("w_skin"))
					{
						// its hide: hard light, hoarfrost, frostbite (wendigo_skin.gdshader; the owner: the bosses should look imposing)
						mi.SetSurfaceOverrideMaterial(s, SkinMaterial(m));
					}
					else if (name.StartsWith("w_eye"))
					{
						// (2026-10-15, "scarier": the stalker's eyeshine the reference) a cold pinprick deep in each socket
						// that reads from across the dark: unshaded and over-bright, so it glows in the fog and the bloom
						m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
						m.AlbedoColor = new Color(0.75f, 0.88f, 1f);
						m.AlbedoTexture = null;
						m.EmissionEnabled = true;
						m.Emission = new Color(0.7f, 0.85f, 1f);
						m.EmissionEnergyMultiplier = 3.5f;
						m.DisableFog = true;
					}
					else if (name.StartsWith("w_tongue"))
					{
						// wet: a hard sheen on it
						m.Roughness = 0.12f;
						m.MetallicSpecular = 0.8f;
					}
					else if (name.StartsWith("w_hide"))
					{
						// the strips of hide are single sheets: both faces drawn
						m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
					}
					else if (name.StartsWith("w_heart"))
					{
						m.EmissionEnabled = true;
						m.Emission = new Color(0.35f, 0.62f, 1f);
						m.EmissionEnergyMultiplier = 0.6f;
						_heart = m;
					}
				}
				mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
			}
			Tune(c);
		}
	}

	/// <summary>(2026-10-10c) The mesh again with its rest pose in its custom channels, so the hide's drawn detail
	/// (wendigo_skin.gdshader) rides the flesh as it moves, and doesn't swim across it; (2026-10-13i) with its creases
	/// and its tangents for the skin's detail maps too (<see cref="CreatureDetail"/>, shared with the other creatures).</summary>
	private static void RestSpace(MeshInstance3D mi) => CreatureDetail.Prepare(mi, "w_skin", "w_bone");

	private static ShaderMaterial _skinMat;
	private static StandardMaterial3D _heart;
	private static NoiseTexture2D _skinNoise;

	/// <summary>The hide's shader, over the baked maps of the imported skin material (one for every wendigo).</summary>
	private static ShaderMaterial SkinMaterial(StandardMaterial3D baked)
	{
		if (_skinMat != null) return _skinMat;
		_skinNoise ??= new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.02f, FractalOctaves = 4, Seed = 7337 } };
		_skinMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/wendigo_skin.gdshader"), ResourceName = "wendigo_skin" };
		_skinMat.SetShaderParameter("albedo_tex", baked.AlbedoTexture);
		_skinMat.SetShaderParameter("normal_tex", baked.NormalTexture);
		_skinMat.SetShaderParameter("noise_tex", _skinNoise);
		_skinMat.SetShaderParameter("roughness", baked.Roughness);
		// (2026-10-13i) the skin's detail maps: its wrinkles a centimetre or two apart at its size
		CreatureDetail.Hook(_skinMat);
		_skinMat.SetShaderParameter("cd_scale", 1.3f);
		return _skinMat;
	}

	private static ShaderMaterial _boneMat;

	private static ShaderMaterial BoneMaterial(StandardMaterial3D baked)
	{
		if (_boneMat != null) return _boneMat;
		_skinNoise ??= new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.02f, FractalOctaves = 4, Seed = 7337 } };
		_boneMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/wendigo_skin.gdshader"), ResourceName = "wendigo_bone" };
		_boneMat.SetShaderParameter("albedo_tex", baked.AlbedoTexture);
		_boneMat.SetShaderParameter("normal_tex", baked.NormalTexture);
		_boneMat.SetShaderParameter("noise_tex", _skinNoise);
		_boneMat.SetShaderParameter("roughness", 0.8f);
		_boneMat.SetShaderParameter("base_a", new Color(0.6f, 0.56f, 0.46f));
		_boneMat.SetShaderParameter("base_b", new Color(0.48f, 0.4f, 0.26f));
		_boneMat.SetShaderParameter("base_c", new Color(0.3f, 0.25f, 0.2f));
		_boneMat.SetShaderParameter("vein_amt", 0f);
		_boneMat.SetShaderParameter("crack_amt", 1.3f);
		_boneMat.SetShaderParameter("frost", 0.12f);
		_boneMat.SetShaderParameter("glint", 0.25f);
		_boneMat.SetShaderParameter("bite_low", -5f);
		_boneMat.SetShaderParameter("bite_high", -4f);
		_boneMat.SetShaderParameter("baked", 0.25f);
		_boneMat.SetShaderParameter("pallor", 0.06f);
		// (2026-10-13i) the bone's surface: pitted and crazed, not wrinkled (the detail shallower, its cracks kept)
		CreatureDetail.Hook(_boneMat);
		_boneMat.SetShaderParameter("cd_scale", 1.6f);
		_boneMat.SetShaderParameter("cd_strength", 0.4f);
		_boneMat.SetShaderParameter("cd_fold_strength", 0.35f);
		return _boneMat;
	}

	/// <summary>Burned (Act 24): its hide charred black by the fire, 0 .. 1 (the skin's char).</summary>
	public void Char(float amount) => _skinMat?.SetShaderParameter("char_amount", Mathf.Clamp(amount, 0f, 1f));

	/// <summary>The heart of ice beating, slow and faint: a long swell and a fade, once every three seconds or so.</summary>
	private void PulseHeart()
	{
		if (_heart == null) return;
		float t = (float)_time;
		float beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.Tau / 3.2f)), 3f);
		_heart.EmissionEnergyMultiplier = 0.45f + 0.35f * beat;
	}

	private void BuildBreath()
	{
		_breath = new GpuParticles3D
		{
			Name = "Breath", Amount = 18, Lifetime = 1.6f, OneShot = true, Emitting = false, Explosiveness = 0.7f, Position = _mouthLocal,
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = new Vector3(0, -0.2f, -1f), Spread = 20f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.5f, Gravity = new Vector3(0, 0.08f, 0),
				ScaleMin = 0.6f, ScaleMax = 1.4f, Color = new Color(0.7f, 0.74f, 0.8f, 0.1f),
			},
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(0.09f, 0.09f),
				Material = new StandardMaterial3D { AlbedoTexture = LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles },
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_head.AddChild(_breath);
	}

	/// <summary>Just the head (the dining hall's platter, Act 23): the skull alone (its own model, unrigged), at the node's
	/// origin. It still twitches.</summary>
	public void ShowOnlyHead()
	{
		_headOnly = true;
		_model.Visible = false;
		_headOnlyModel = GD.Load<PackedScene>("res://assets/models/wendigo/wendigo_head.glb").Instantiate<Node3D>();
		_headOnlyModel.Name = "HeadOnly";
		_body.AddChild(_headOnlyModel);
		Tune(_headOnlyModel);
		// the breath and the photograph go with it
		_breath.Reparent(_headOnlyModel, false);
		_mouthLocal = Mouth - HeadJoint;
		_skullLocal = SkullMid - HeadJoint;
		_breath.Position = _mouthLocal;
		_head = _headOnlyModel;
		_head.Rotation = _headRest;
		PhotoSubject.Attach(_head, "wendigo", _skullLocal, 3f, 60f, 10f, false);
		Visible = true;
	}

	/// <summary>The skull's mouth (for the blood).</summary>
	public Vector3 MouthWorld => _head.ToGlobal(_mouthLocal);

	/// <summary>For a while its jaw works, as if it speaks (the voice is the caller's).</summary>
	public void Speak(float seconds) => _speakUntil = (float)_time + seconds;

	// ------------------------------------------------------------------ life

	/// <summary>Stand at <paramref name="ground"/> facing <paramref name="look"/>, visible.</summary>
	/// <summary>Its size (1: its full 4.7 m). In Act 24's snow tunnels, cramped and stooping, it is drawn smaller.</summary>
	public void SetSize(float s) { _size = s; if (_body != null) _body.Scale = Vector3.One * s; }

	/// <summary>(2026-10-10c) The body seen follows where the creature is and which way it faces on a critically damped
	/// spring: whatever moves it (a tween's straight run, a turn, a snap to a mark), the body seen eases into it, carries
	/// its momentum through a change of direction, and settles, never jerks. Tight while it leaps (it must stay on its
	/// arc), looser on the ground. A jump of more than 8 m is a teleport, taken at once.</summary>
	private void FollowBody(float dt)
	{
		if (_body == null || _headOnly) return;
		var target = GlobalTransform;
		var tq = target.Basis.Orthonormalized().GetRotationQuaternion();
		if (!_visSet || _visPos.DistanceTo(target.Origin) > 8f)
		{
			_visPos = target.Origin; _visVel = Vector3.Zero; _visRot = tq; _visSet = true;
		}
		else
		{
			float w = _leapT >= 0f ? 26f : 11f;
			_visVel += ((target.Origin - _visPos) * w * w - _visVel * 2f * w) * dt;
			_visPos += _visVel * dt;
			_visRot = _visRot.Normalized().Slerp(tq.Normalized(), 1f - Mathf.Exp(-dt * (_leapT >= 0f ? 14f : 7f))).Normalized();
		}
		_body.GlobalTransform = new Transform3D(new Basis(_visRot).Scaled(Vector3.One * _size * target.Basis.Scale.X), _visPos);
	}

	/// <summary>The body seen snapped to where the creature is (a cut, a fresh placement).</summary>
	public void SnapBody() => _visSet = false;

	public void StandAt(Vector3 ground, Vector3 look)
	{
		_leapT = -1f;
		GlobalPosition = ground;
		var d = (look - ground) with { Y = 0 };
		if (d.LengthSquared() > 0.01f) GlobalBasis = Basis.LookingAt(d, Vector3.Up);
		_body.Position = Vector3.Zero;
		_visSet = false;
		if (_anim != null && !_headOnly)
		{
			_anim.Play(Clip("idle"), 0.0);
			_anim.Seek(_rng.RandfRange(0f, (float)_anim.GetAnimation(Clip("idle")).Length), true);
		}
		Visible = true;
	}

	/// <summary>Holds a pose: <paramref name="clip"/> stopped at <paramref name="at"/> (0..1) of its length (crouched low
	/// on a wall's top, clinging to a trunk mid-climb). The head's twitch goes on over it.</summary>
	public void Hold(string clip, float at, double blend = 0.0)
	{
		clip = Clip(clip);
		if (_anim == null || _headOnly || !_anim.HasAnimation(clip)) return;
		_leapT = -1f;
		_anim.Play(clip, Mathf.Max(blend, MinBlend), 0f);
		_anim.Seek(_anim.GetAnimation(clip).Length * Mathf.Clamp(at, 0f, 1f), true);
	}

	/// <summary>Up into the trees (or down at its prey): a crouch of a few hundredths of a second, the legs folding to
	/// spring; then gone along an arc to <paramref name="perch"/> in <paramref name="seconds"/>, the legs tucking under
	/// it and the arms reaching (or, leaping down, flung flat at it, arms and claws thrown forward). <paramref name="landed"/>
	/// runs when it's there (the snow falls, it vanishes). <paramref name="ballistic"/>: a true throw (steady across, a
	/// parabola of <paramref name="arc"/> metres over the straight line: off a height it falls, faster and faster), and
	/// staying, it lands in <paramref name="landClip"/> (else rising straight into its idle).</summary>
	public void Leap(Vector3 perch, float seconds, System.Action landed, bool stay = false, float arc = 1.2f, bool ballistic = false, string landClip = null, float landSeconds = 1f)
	{
		Leaps++;
		_stayOnLanding = stay;
		_arc = arc;
		_ballistic = ballistic;
		_landClip = landClip;
		_landSeconds = landSeconds;
		_leapFrom = GlobalPosition;
		_leapTo = perch;
		_leapDur = seconds;
		_leapT = 0f;
		_landed = landed;
		_leapClip = perch.Y < GlobalPosition.Y - 1f ? "pounce" : "air";
		PlayClip("crouch", Crouch, 0.18);
		AudioDirector.OneShot(this, "wendigo_leap", 3, GlobalPosition + Vector3.Up * 2f, -3f, "Events", 7f, 0.05f);
	}

	/// <summary>Plays an animation stretched to last <paramref name="seconds"/> (it stops on its last frame, but the idle).</summary>
	public void Play(string name, float seconds, double blend)
	{
		_leapT = -1f;
		PlayClip(name, seconds, blend);
	}

	/// <summary>A looping gait (walk, run, idle) at its natural rate times <paramref name="rate"/>: the hunt in Act 24 sets the
	/// rate from how fast it's really going, each frame, so its feet keep to the ground.</summary>
	public void Gait(string name, float rate, double blend)
	{
		name = Clip(name);
		if (_anim == null || _headOnly || !_anim.HasAnimation(name)) return;
		_leapT = -1f;
		if (_anim.CurrentAnimation != name) _anim.Play(name, Mathf.Max(blend, 0.3), 1f);
		_anim.SpeedScale = Mathf.Clamp(rate, 0.05f, 3f);
	}

	/// <summary>A clip's length in seconds (0 if it isn't there).</summary>
	public float ClipLength(string name) => _anim != null && _anim.HasAnimation(Clip(name)) ? (float)_anim.GetAnimation(Clip(name)).Length : 0f;

	private void PlayClip(string name, float seconds, double blend)
	{
		name = Clip(name);
		if (_anim == null || _headOnly || !_anim.HasAnimation(name)) return;
		_anim.SpeedScale = 1f;   // (a gait's rate off: this clip's own timing)
		float len = (float)_anim.GetAnimation(name).Length;
		_anim.Play(name, Mathf.Max(blend, Mathf.Min(MinBlend, seconds * 0.4)), Mathf.Max(len, 0.01f) / Mathf.Max(seconds, 0.01f));
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;
		float dt = (float)delta;
		_time += delta;
		if (_leapT >= 0f)
		{
			bool wasCrouching = _leapT < Crouch;
			_leapT += dt;
			if (_leapT < Crouch) { Animate(dt); return; }
			if (wasCrouching) PlayClip(_leapClip, _leapDur, 0.12);
			float u = Mathf.Clamp((_leapT - Crouch) / _leapDur, 0f, 1f);
			GlobalPosition = _ballistic
				? _leapFrom.Lerp(_leapTo, u) + Vector3.Up * (4f * u * (1f - u) * _arc)
				: _leapFrom.Lerp(_leapTo, 1f - Mathf.Pow(1f - u, 2.2f)) + Vector3.Up * Mathf.Sin(u * Mathf.Pi) * _arc;
			Animate(dt);
			if (u >= 1f)
			{
				_leapT = -1f;
				// (dropped down onto the road: it stays, landed in a crouch and rising out of it; up into the trees: gone)
				if (_stayOnLanding)
				{
					GlobalPosition = _leapTo;
					if (_landClip != null) PlayClip(_landClip, _landSeconds, 0.05);
					else if (_anim != null && !_headOnly) _anim.Play(Clip("idle"), 0.6);
				}
				else Visible = false;
				var cb = _landed; _landed = null;
				cb?.Invoke();
			}
			return;
		}
		PulseHeart();
		// (2026-10-15, the owner: "scarier"; the stalker's lessons) now and then the head rolls over too far, as an owl's
		// does, and holds there; and now and then the body moves in stop-motion, as if frames were missing
		if (_time >= _rollAt)
		{
			_rollAt = _time + _rng.RandfRange(4f, 9f);
			_rollUntil = _time + _rng.RandfRange(0.8f, 1.8f);
			_rollGoal = (_rng.Randf() < 0.5f ? -1f : 1f) * _rng.RandfRange(0.65f, 1.05f);
		}
		_roll = Mathf.Lerp(_roll, _time < _rollUntil ? _rollGoal : 0f, 1f - Mathf.Exp(-dt * (_time < _rollUntil ? 3.5f : 14f)));
		if (_time >= _stutterAt)
		{
			_stutterAt = _time + _rng.RandfRange(4f, 10f);
			_stutterUntil = _time + _rng.RandfRange(0.45f, 0.9f);
		}
		// standing: breathing (the idle), the head hanging to one side and now and then snapping to a new angle
		if (_time >= _twitchAt)
		{
			_twitchAt = _time + _rng.RandfRange(0.6f, 2.4f);
			_twitchGoal = new Vector3(_rng.RandfRange(-0.2f, 0.25f), _rng.RandfRange(-0.35f, 0.35f), _rng.RandfRange(-0.5f, 0.3f));
		}
		_twitch = _twitch.Lerp(_twitchGoal, 1f - Mathf.Exp(-dt * 30f));
		if (_time >= _breathAt)
		{
			_breathAt = _time + _rng.RandfRange(2.2f, 3.6f);
			_breath.Restart();
		}
		Animate(dt);
	}

	/// <summary>(2026-10-10c, the animation pass: "really add some flare and smooth all their animations out") Its head
	/// turns to follow them: eased, up to about 55 degrees off where the clip has it, the twitch still in it. 0 lets
	/// the clip have it; a hunter sets it only while it can see them (it doesn't know where they are otherwise).</summary>
	public float LookWeight = 1f;
	public Vector3? LookAtWorld;
	private Quaternion _look = Quaternion.Identity;
	private AnimKit.BodyDynamics _dyn;
	/// <summary>For tests: its weight over the clips (the lean, the hips lowered to the ground).</summary>
	public AnimKit.BodyDynamics Dynamics => _dyn;

	private void TrackHead(float dt)
	{
		if (_headBone < 0 || _skel == null) return;
		Quaternion want = Quaternion.Identity;
		var at = LookAtWorld ?? GetViewport()?.GetCamera3D()?.GlobalPosition;
		if (at is { } target && LookWeight > 0.001f && GlobalPosition.DistanceTo(target) < 40f)
		{
			var headG = _skel.GetBoneGlobalPose(_headBone);
			Vector3 cur = (headG.Basis * _mouthLocal).Normalized();
			Vector3 to = (_skel.GlobalTransform.AffineInverse() * target - headG.Origin).Normalized();
			float ang = Mathf.Min(cur.AngleTo(to), 0.95f) * LookWeight;
			var axis = cur.Cross(to);
			if (axis.LengthSquared() > 1e-6f && ang > 0.001f) want = new Quaternion(axis.Normalized(), ang);
		}
		_look = _look.Normalized().Slerp(want.Normalized(), 1f - Mathf.Exp(-dt * 3.2f)).Normalized();
		if (_look.IsEqualApprox(Quaternion.Identity)) return;
		int parent = _skel.GetBoneParent(_headBone);
		var g = _skel.GetBoneGlobalPose(_headBone);
		var pg = parent >= 0 ? _skel.GetBoneGlobalPose(parent) : Transform3D.Identity;
		var turned = new Basis(_look) * g.Basis;
		_skel.SetBonePoseRotation(_headBone, (pg.Basis.Inverse() * turned).Orthonormalized().GetRotationQuaternion());
	}

	/// <summary>The skeleton: the animation, then the head's twitch and the jaw's speech on top.</summary>
	private void Animate(float dt)
	{
		FollowBody(dt);
		if (_headOnly)
		{
			_head.Rotation = _headRest + _twitch * 0.6f;
			return;
		}
		if (_anim == null || _skel == null) return;
		// (2026-10-15) stop-motion: the clip advanced in jerks, about twelve a second, frames missing between them
		if (_time < _stutterUntil)
		{
			_stutterAcc += dt;
			if (_stutterAcc >= StutterStep) { _anim.Advance(_stutterAcc); _stutterAcc = 0f; }
		}
		else
		{
			if (_stutterAcc > 0f) { _anim.Advance(_stutterAcc); _stutterAcc = 0f; }
			_anim.Advance(dt);
		}
		// (2026-10-12, the animation overhaul) weight over the clip: leaning into its starts, stops and turns, the long arms
		// swinging behind, the hooves planted on the ground under them
		_dyn ??= new AnimKit.BodyDynamics(_skel, "hips", new[] { "spine", "chest" }, "head", new[] { "upper_R", "upper_L" },
			new[] { ("thigh_R", "shin_R", "foot_R", "toe_R"), ("thigh_L", "shin_L", "foot_L", "toe_L") }) { LeanPerAccel = 0.03f, MaxLean = 0.28f };
		_dyn.Weight = _leapT >= 0f ? 0f : 1f;
		_dyn.Apply(_body, dt, _leapT < 0f);
		// (2026-10-15) the lurk: hunched lower over the chest while it stands
		if (_chest >= 0 && _leapT < 0f && Hunch > 0f)
			_skel.SetBonePoseRotation(_chest, _skel.GetBonePoseRotation(_chest) * new Quaternion(Vector3.Right, Hunch));
		if (_headBone >= 0 && _leapT < 0f)
		{
			_skel.SetBonePoseRotation(_headBone, _skel.GetBonePoseRotation(_headBone) * Quaternion.FromEuler(_twitch * Mathf.Lerp(1f, 0.45f, LookWeight)));
			TrackHead(dt);
			// the owl's roll on top of everything (looking at you, the head turned over on its side)
			if (Mathf.Abs(_roll) > 0.01f)
				_skel.SetBonePoseRotation(_headBone, _skel.GetBonePoseRotation(_headBone) * new Quaternion(Vector3.Up, _roll));
		}
		if (_jawBone >= 0)
		{
			// (2026-10-15, the stalker's torn jaw the reference) the jaw never closes: it hangs slack, quivering; it gapes
			// on the lunge; and it works when it speaks
			float open = JawSlack + 0.03f * Mathf.Sin((float)_time * 23f) * Mathf.Sin((float)_time * 3.1f);
			if (_leapT >= 0f) open = Mathf.Max(open, 0.55f);
			if (_time < _speakUntil) open += 0.12f + 0.2f * Mathf.Abs(Mathf.Sin((float)_time * 7.3f) * Mathf.Sin((float)_time * 2.9f + 1f));
			_skel.SetBonePoseRotation(_jawBone, _skel.GetBonePoseRotation(_jawBone) * new Quaternion(Vector3.Right, open));
		}
	}
}
