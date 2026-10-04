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
	private int _headBone = -1, _jawBone = -1, _chest = -1;
	private bool _headOnly;
	private GpuParticles3D _breath;
	private readonly RandomNumberGenerator _rng = new() { Seed = 666 };
	private double _time, _twitchAt = 1.2, _breathAt = 2.0;
	private Vector3 _twitch, _twitchGoal;
	private Vector3 _headRest = new(0.1f, 0f, 0.25f);
	private float _leapT = -1f, _leapDur, _speakUntil = -1f;
	private const float Crouch = 0.07f;
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
			var idle = _anim.GetAnimation("idle");
			if (idle != null) idle.LoopMode = Animation.LoopModeEnum.Linear;
			_anim.Play("idle");
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
				for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
				{
					if (mi.Mesh.SurfaceGetMaterial(s) is not StandardMaterial3D m) continue;
					string name = m.ResourceName ?? "";
					m.SetMeta("detail_kind", -1);
					if (name.StartsWith("w_bone"))
					{
						m.EmissionEnabled = true;
						m.Emission = new Color(0.55f, 0.58f, 0.62f);
						m.EmissionEnergyMultiplier = 0.12f;
					}
					else if (name.StartsWith("w_skin"))
					{
						// its hide: hard light, hoarfrost, frostbite (wendigo_skin.gdshader; the owner: the bosses should look imposing)
						mi.SetSurfaceOverrideMaterial(s, SkinMaterial(m));
					}
					else if (name.StartsWith("w_eye"))
					{
						m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
						m.AlbedoColor = new Color(0.75f, 0.88f, 1f);
						m.AlbedoTexture = null;
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
		return _skinMat;
	}

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
	public void StandAt(Vector3 ground, Vector3 look)
	{
		_leapT = -1f;
		GlobalPosition = ground;
		var d = (look - ground) with { Y = 0 };
		if (d.LengthSquared() > 0.01f) GlobalBasis = Basis.LookingAt(d, Vector3.Up);
		_body.Position = Vector3.Zero;
		if (_anim != null && !_headOnly)
		{
			_anim.Play("idle", 0.0);
			_anim.Seek(_rng.RandfRange(0f, 3.5f), true);
		}
		Visible = true;
	}

	/// <summary>Holds a pose: <paramref name="clip"/> stopped at <paramref name="at"/> (0..1) of its length (crouched low
	/// on a wall's top, clinging to a trunk mid-climb). The head's twitch goes on over it.</summary>
	public void Hold(string clip, float at, double blend = 0.0)
	{
		if (_anim == null || _headOnly || !_anim.HasAnimation(clip)) return;
		_leapT = -1f;
		_anim.Play(clip, blend, 0f);
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
		PlayClip("crouch", Crouch, 0.03);
		AudioDirector.OneShot(this, "wendigo_leap", 3, GlobalPosition + Vector3.Up * 2f, -3f, "Events", 7f, 0.05f);
	}

	/// <summary>Plays an animation stretched to last <paramref name="seconds"/> (it stops on its last frame, but the idle).</summary>
	public void Play(string name, float seconds, double blend)
	{
		_leapT = -1f;
		PlayClip(name, seconds, blend);
	}

	private void PlayClip(string name, float seconds, double blend)
	{
		if (_anim == null || _headOnly || !_anim.HasAnimation(name)) return;
		float len = (float)_anim.GetAnimation(name).Length;
		_anim.Play(name, blend, Mathf.Max(len, 0.01f) / Mathf.Max(seconds, 0.01f));
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
			if (wasCrouching) PlayClip(_leapClip, _leapDur, 0.03);
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
					else if (_anim != null && !_headOnly) _anim.Play("idle", 0.6);
				}
				else Visible = false;
				var cb = _landed; _landed = null;
				cb?.Invoke();
			}
			return;
		}
		PulseHeart();
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

	/// <summary>The skeleton: the animation, then the head's twitch and the jaw's speech on top.</summary>
	private void Animate(float dt)
	{
		if (_headOnly)
		{
			_head.Rotation = _headRest + _twitch * 0.6f;
			return;
		}
		if (_anim == null || _skel == null) return;
		_anim.Advance(dt);
		if (_headBone >= 0 && _leapT < 0f)
			_skel.SetBonePoseRotation(_headBone, _skel.GetBonePoseRotation(_headBone) * Quaternion.FromEuler(_twitch));
		if (_jawBone >= 0 && _time < _speakUntil)
		{
			float open = 0.12f + 0.2f * Mathf.Abs(Mathf.Sin((float)_time * 7.3f) * Mathf.Sin((float)_time * 2.9f + 1f));
			_skel.SetBonePoseRotation(_jawBone, _skel.GetBonePoseRotation(_jawBone) * new Quaternion(Vector3.Right, open));
		}
	}
}
