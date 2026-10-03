using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// The crawler (the owner, after their reference: a pale, starved humanoid bent backwards into a crouch on
/// all fours, elbows and knees jutting up higher than its back, its head hanging low between its
/// shoulders). It first comes down the stairwell after the player in Act 14, and it is waiting in the
/// church's crypt later.
///
/// It is driven from outside: the owner sets where its body is and which way it's heading
/// (<see cref="Place"/>); it works out its own limbs. Each hand and foot is planted on whatever is below
/// it (a ray down onto the stairs) and stays there until the body has moved on past it, then snatches
/// forward to its next hold in one quick, jerking step. One limb at a time, in an uneven order, which is
/// what makes it move wrongly. Every plant rings on the steel tread (`crawler_step`). Its head twitches;
/// it breathes (`crawler_breath`, the owner's sample, pitched, warped and echoed into its own voice).
/// </summary>
public partial class Crawler : Node3D
{
	[Export] public float BodyHeight = 0.7f;
	[Export] public float StepReach = 0.55f;
	[Export] public float StepTime = 0.13f;

	/// <summary>Seconds of quiet between one breath and the next (shorter when it hurries).</summary>
	public float BreathEvery = 2.5f;
	/// <summary>0..1: how hard it is hurrying (quicker steps, quicker breath, more twitching).</summary>
	public float Hurry;
	/// <summary>For tests: plants so far.</summary>
	public int Steps { get; private set; }

	private sealed class Limb
	{
		public Vector3 Root;          // on the body (body space)
		public Vector3 Rest;          // where its hand/foot rests (body space, before the ground ray)
		public float L1, L2;
		public Vector3 Plant, From, To;
		public float T = 1f;
		public bool Front;
		public MeshInstance3D Upper, Lower, Knuckle, Paw;
		// the remodel's bones (-1 for the old pieces)
		public int BUpper = -1, BLower = -1, BHand = -1;
		public int[] BFingers;
	}

	// ------------------------------------------------------------------ the remodel (tools/Blender/crawler.py)

	public const string RigPath = "res://assets/models/crawler/crawler_rig.glb";
	/// <summary>True when it's the remodel (one skinned hide on a skeleton), false for the old pieces.</summary>
	public bool Rigged => _skel != null;
	private Skeleton3D _skel;
	private int _bHead = -1, _bJaw = -1, _bHips = -1, _bChest = -1, _bNeck = -1;
	private Dictionary<string, int> _boneIx;

	private bool LoadRig()
	{
		if (CreatureModels.Disabled || !ResourceLoader.Exists(RigPath) || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--old-crawler") >= 0) return false;
		var model = GD.Load<PackedScene>(RigPath).Instantiate<Node3D>();
		model.Name = "Model";
		var sk = model.FindChildren("*", "Skeleton3D", true, false);
		if (sk.Count == 0) { model.QueueFree(); return false; }
		AddChild(model);
		_skel = (Skeleton3D)sk[0];
		_boneIx = new();
		for (int i = 0; i < _skel.GetBoneCount(); i++) _boneIx[_skel.GetBoneName(i)] = i;
		_bHead = B("head"); _bJaw = B("jaw"); _bHips = B("hips"); _bChest = B("chest"); _bNeck = B("neck");
		foreach (var n in model.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)n;
			mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
			for (int s = 0; s < (mi.Mesh?.GetSurfaceCount() ?? 0); s++)
			{
				if (mi.Mesh.SurfaceGetMaterial(s) is not StandardMaterial3D m) continue;
				// its own baked colours, and the blacklight's glow over them; a cold rim so it reads against the dark
				var t = (StandardMaterial3D)m.Duplicate();
				string mn = m.ResourceName ?? "";
				t.Roughness = mn.StartsWith("c_tooth") ? 0.35f : 0.62f;
				t.RimEnabled = true; t.Rim = 0.22f; t.RimTint = 0.3f;
				t.NextPass = _uvGlow;
				if (mn.StartsWith("c_hair")) { t.CullMode = BaseMaterial3D.CullModeEnum.Disabled; t.Roughness = 0.3f; t.MetallicSpecular = 0.6f; }
				t.SetMeta("detail_kind", -1);
				mi.SetSurfaceOverrideMaterial(s, t);
			}
		}
		// the old code's body and head nodes (the voice rides the head bone)
		_body = new Node3D { Name = "Body" };
		AddChild(_body);
		var att = new BoneAttachment3D { Name = "Head", BoneName = "head" };
		_skel.AddChild(att);
		_head = att;
		return true;
	}

	private int B(string name) => _boneIx != null && _boneIx.TryGetValue(name, out int i) ? i : -1;

	private readonly List<Limb> _limbs = new();
	private readonly int[] _order = { 0, 3, 1, 2 };   // left hand, right foot, right hand, left foot
	private int _next;
	private Node3D _body, _head;
	private ShaderMaterial _skin;
	private StandardMaterial3D _dark;
	private readonly RandomNumberGenerator _rng = new() { Seed = 4404 };
	private double _time, _twitchAt, _breathAt;
	private Vector3 _twitch, _twitchGoal;
	private Vector3 _heading = Vector3.Forward;
	private bool _placed;
	private StandardMaterial3D _uvGlow, _limbSkin;
	private Vector3 _lastPos;
	private float _speed;
	private AudioStreamPlayer3D _voice;
	// its steps: a pool of three voices it rings round (a new player per plant stacked up to nine of one step at once)
	private const int StepVoices = 3;
	private readonly AudioStreamPlayer3D[] _steps = new AudioStreamPlayer3D[StepVoices];
	private int _stepVoice;
	private static AudioStream[] _stepStreams;

	public override void _Ready()
	{
		_skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/rot_skin.gdshader") };
		_skin.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		// pale, starved, faintly grey-green, bruised dark here and there (no red: it isn't bleeding)
		_skin.SetShaderParameter("rot", 0.2f);
		_skin.SetShaderParameter("scale", 2.2f);
		_skin.SetShaderParameter("healthy", new Color(0.6f, 0.58f, 0.54f));
		_skin.SetShaderParameter("rotten", new Color(0.46f, 0.48f, 0.42f));
		_skin.SetShaderParameter("sore", new Color(0.24f, 0.2f, 0.2f));
		_skin.SetShaderParameter("dead", new Color(0.3f, 0.28f, 0.26f));
		_dark = new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.02f, 0.02f), Roughness = 0.2f, MetallicSpecular = 0.7f };
		// its skin catches the lantern's blacklight (after the flame dies, the violet light is all there is)
		_uvGlow = new StandardMaterial3D
		{
			AlbedoColor = new Color(0f, 0f, 0f, 0f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			EmissionEnabled = false,
		};
		_skin.NextPass = _uvGlow;
		// the limbs: the same pale skin, plain (the body's mottling would stretch along the long bones into bands)
		_limbSkin = new StandardMaterial3D { AlbedoColor = new Color(0.52f, 0.5f, 0.46f), Roughness = 0.7f, RimEnabled = true, Rim = 0.3f, RimTint = 0.2f, NextPass = _uvGlow };
		_voice = new AudioStreamPlayer3D { Name = "Voice", Bus = "Events", UnitSize = 3.5f, MaxDistance = 30f };
		AddChild(_voice);
		bool rig = LoadRig();
		if (!rig) BuildBody();
		BuildLimbs(rig);
		_twitchAt = 1.0;
		_breathAt = 0.5;
	}

	// ------------------------------------------------------------------ building

	private void BuildBody()
	{
		_body = new Node3D { Name = "Body" };
		AddChild(_body);
		var k = new MeshKit();
		k.Mat(_skin);
		k.Color = Colors.White;
		// the torso: narrow, long, ribs showing, the spine a ridge (body space: forward is -Z)
		k.Blob(new Vector3(0, 0, 0.05f), new Vector3(0.17f, 0.12f, 0.42f), 11, 0.1f, false);
		k.Blob(new Vector3(0, 0.02f, -0.3f), new Vector3(0.22f, 0.14f, 0.2f), 12, 0.08f, false);   // the chest, the shoulders
		k.Blob(new Vector3(0, 0.0f, 0.38f), new Vector3(0.19f, 0.12f, 0.14f), 13, 0.08f, false);   // the pelvis
		for (int i = 0; i < 9; i++)
			k.Blob(new Vector3(0, 0.1f - Mathf.Abs(i - 4) * 0.004f, -0.3f + i * 0.08f), new Vector3(0.028f, 0.03f, 0.03f), 20 + i, 0.2f, false);
		for (int i = 0; i < 5; i++)
			foreach (float s in new[] { -1f, 1f })
				k.Cylinder(new Vector3(s * 0.02f, 0.07f, -0.28f + i * 0.07f), new Vector3(s * 0.19f, -0.04f, -0.24f + i * 0.07f), 0.012f, 0.01f, 5, false);
		// the neck, craning down and forward, and the head hanging from it
		k.Cylinder(new Vector3(0, 0.02f, -0.42f), new Vector3(0, -0.12f, -0.58f), 0.045f, 0.04f, 8, false);
		if (CreatureModels.Get("crawler", "Torso") is { } torso)
			_body.AddChild(new MeshInstance3D { Name = "Torso", Mesh = torso, MaterialOverride = _skin, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		else k.CommitTo(_body, "Torso", false);
		_head = new Node3D { Name = "Head", Position = new Vector3(0, -0.17f, -0.63f) };
		_body.AddChild(_head);
		var h = new MeshKit();
		h.Mat(_skin);
		h.Color = Colors.White;
		h.Blob(Vector3.Zero, new Vector3(0.095f, 0.11f, 0.12f), 31, 0.08f, false);                 // the skull, long
		h.Blob(new Vector3(0, -0.07f, -0.07f), new Vector3(0.06f, 0.035f, 0.06f), 32, 0.1f, false);   // the jaw, hanging open
		h.Mat(_dark);
		h.Color = Colors.White;
		foreach (float s in new[] { -1f, 1f }) h.Blob(new Vector3(s * 0.038f, 0.01f, -0.1f), new Vector3(0.026f, 0.022f, 0.02f), 33, 0.1f, false);   // sunken sockets
		h.Blob(new Vector3(0, -0.045f, -0.105f), new Vector3(0.035f, 0.03f, 0.02f), 34, 0.1f, false);   // the open mouth
		if (CreatureModels.Get("crawler", "Skull") is { } skull)
		{
			var mi = new MeshInstance3D { Name = "Skull", Mesh = skull, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			for (int i = 0; i < skull.GetSurfaceCount(); i++) mi.SetSurfaceOverrideMaterial(i, CreatureModels.SurfaceName(skull, i) == "dark" ? _dark : _skin);
			_head.AddChild(mi);
		}
		else h.CommitTo(_head, "Skull", false);
	}

	private MeshInstance3D Bone(float r0, float r1)
	{
		var mi = new MeshInstance3D
		{
			// (the remodelled limb: a starved bone under skin, its joints, its tendons; unit length along Y as before)
			Mesh = CreatureModels.Get("crawler", r0 > 0.04f ? "Upper" : "Lower") ?? new CylinderMesh { TopRadius = r1, BottomRadius = r0, Height = 1f, RadialSegments = 8, Rings = 1 },
			MaterialOverride = _limbSkin, CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		};
		AddChild(mi);
		mi.TopLevel = true;
		return mi;
	}

	private void BuildLimbs(bool rig = false)
	{
		// shoulders and hips in body space; the rests far out to the sides (the splayed, bridged crouch)
		(Vector3 root, Vector3 rest, float l1, float l2, bool front)[] defs =
		{
			// hands and feet in close under the body, so the long limbs fold and the elbows and knees ride high
			(new Vector3(-0.2f, 0.03f, -0.32f), new Vector3(-0.46f, 0, -0.58f), 0.62f, 0.66f, true),
			(new Vector3(0.2f, 0.03f, -0.32f), new Vector3(0.46f, 0, -0.58f), 0.62f, 0.66f, true),
			(new Vector3(-0.16f, 0.0f, 0.4f), new Vector3(-0.44f, 0, 0.64f), 0.66f, 0.72f, false),
			(new Vector3(0.16f, 0.0f, 0.4f), new Vector3(0.44f, 0, 0.64f), 0.66f, 0.72f, false),
		};
		foreach (var (root, rest, l1, l2, front) in defs)
		{
			var l = new Limb { Root = root, Rest = rest, L1 = l1, L2 = l2, Front = front };
			if (rig)
			{
				string k = front ? "arm" : "leg", sx = root.X > 0 ? "R" : "L";
				l.BUpper = B($"{k}_upper_{sx}"); l.BLower = B($"{k}_lower_{sx}"); l.BHand = B($"{k}_hand_{sx}");
				l.BFingers = new[] { B($"{k}_f0_{sx}"), B($"{k}_f1_{sx}"), B($"{k}_f2_{sx}"), B($"{k}_f3_{sx}") };
				_limbs.Add(l);
				continue;
			}
			l.Upper = Bone(0.045f, 0.03f);
			l.Lower = Bone(0.03f, 0.018f);
			l.Knuckle = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 8, Rings = 4 }, MaterialOverride = _limbSkin, TopLevel = true };
			AddChild(l.Knuckle);
			// a long hand (or foot), fingers splayed
			var pk = new MeshKit();
			pk.Mat(_skin);
			pk.Color = Colors.White;
			pk.Blob(new Vector3(0, 0.015f, -0.04f), new Vector3(0.045f, 0.02f, 0.07f), 40, 0.1f, false);
			for (int f = 0; f < 4; f++)
			{
				float a = (f - 1.5f) * 0.35f;
				Vector3 d = new(Mathf.Sin(a), 0, -Mathf.Cos(a));
				pk.Cylinder(new Vector3(0, 0.012f, -0.08f), new Vector3(0, 0.004f, -0.08f) + d * (front ? 0.13f : 0.09f), 0.011f, 0.005f, 5, false);
			}
			if (CreatureModels.Get("crawler", front ? "Hand" : "Foot") is { } paw)
			{
				l.Paw = new MeshInstance3D { Name = front ? "Hand" : "Foot", Mesh = paw, MaterialOverride = _skin };
				AddChild(l.Paw);
			}
			else l.Paw = pk.CommitTo(this, front ? "Hand" : "Foot", false);
			l.Paw.TopLevel = true;
			_limbs.Add(l);
		}
	}

	// ------------------------------------------------------------------ driving it

	/// <summary>Sets where the body is (world, on the stairs' surface: it stands up off it by <see cref="BodyHeight"/>)
	/// and which way it's heading (it pitches down or up the slope with it).</summary>
	public void Place(Vector3 ground, Vector3 heading)
	{
		if (heading.LengthSquared() > 1e-4f) _heading = _heading.Slerp(heading.Normalized(), 0.25f);
		GlobalPosition = ground + Vector3.Up * BodyHeight;
		Vector3 flat = _heading with { Y = 0 };
		if (flat.LengthSquared() < 1e-4f) flat = Vector3.Forward;
		float pitch = Mathf.Atan2(_heading.Y, flat.Length());
		GlobalBasis = Basis.LookingAt(flat.Normalized(), Vector3.Up) * new Basis(Vector3.Right, pitch * 0.8f);
		if (!_placed)
		{
			_placed = true;
			foreach (var l in _limbs) { l.Plant = Ground(GlobalTransform * l.Rest); l.T = 1f; }
		}
	}

	private Vector3 Ground(Vector3 p)
	{
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return p;
		var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 1.2f, p + Vector3.Down * 2.5f, 1u);
		var hit = space.IntersectRay(q);
		return hit.Count > 0 ? (Vector3)hit["position"] : p;
	}

	public override void _Process(double delta)
	{
		if (!_placed || !Visible) return;
		float dt = (float)delta;
		_time += dt;
		// its own speed, from where it's been put
		float speed = _lastPos == Vector3.Zero ? 0f : (GlobalPosition - _lastPos).Length() / Mathf.Max(dt, 1e-4f);
		_lastPos = GlobalPosition;
		_speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-6f * dt));
		// the gait: one limb at a time, in its uneven order, whenever the body has moved on past it; two at once
		// (a diagonal pair) when it hurries; and a limb left far behind snatched forward whatever else is moving
		int moving = 0;
		foreach (var l in _limbs) if (l.T < 1f) moving++;
		int allowed = Hurry > 0.3f || _speed > 1.2f ? 2 : 1;
		for (int n = 0; n < 4 && moving < 4; n++)
		{
			var l = _limbs[_order[(_next + n) % 4]];
			if (l.T < 1f) continue;
			Vector3 want = Ground(GlobalTransform * l.Rest);
			float far = l.Plant.DistanceTo(want);
			bool due = far > StepReach * (0.7f + 0.3f * (1f - Hurry)) && moving < allowed;
			if (due || far > StepReach * 2.2f)
			{
				l.From = l.Plant;
				// lead the body: land ahead by how far it will travel during the step
				Vector3 lead = (_heading with { Y = 0 }).Normalized() * Mathf.Min(_speed * StepTime, StepReach);
				l.To = Ground(GlobalTransform * l.Rest + lead);
				l.T = 0f;
				moving++;
				_next = (_next + n + 1) % 4;
			}
		}
		float stepTime = StepTime * Mathf.Lerp(1.15f, 0.7f, Mathf.Max(Hurry, Mathf.Clamp(_speed / 3f, 0f, 1f)));
		for (int i = 0; i < _limbs.Count; i++)
		{
			var l = _limbs[i];
			if (l.T < 1f)
			{
				l.T = Mathf.Min(1f, l.T + dt / stepTime);
				float e = l.T * l.T * (3f - 2f * l.T);
				l.Plant = l.From.Lerp(l.To, e) + Vector3.Up * Mathf.Sin(l.T * Mathf.Pi) * 0.22f;
				if (l.T >= 1f)
				{
					l.Plant = l.To;
					Steps++;
					PlayStep(l.Plant, Mathf.Lerp(-4f, 0f, Hurry));
				}
			}
			Solve(l);
		}
		// the head: hanging, and now and then snapping to a new angle and holding it (never a smooth turn)
		if (_time >= _twitchAt)
		{
			_twitchGoal = new Vector3(_rng.RandfRange(-0.35f, 0.25f), _rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(-0.7f, 0.7f));
			_twitchAt = _time + _rng.RandfRange(0.4f, 2.2f) * Mathf.Lerp(1f, 0.5f, Hurry);
		}
		_twitch = _twitch.Lerp(_twitchGoal, 1f - Mathf.Exp(-22f * dt));
		if (_skel != null) { PoseBody(); PoseLimbs(); }
		else
		{
			_head.Rotation = new Vector3(0.5f, 0, 0) + _twitch;
			// the body, heaving a little with the breath
			_body.Position = new Vector3(0, Mathf.Sin((float)_time * 2.2f) * 0.012f, 0);
		}
		// one voice: a breath, and the next only once it's done (a pause between, shorter when it hurries)
		_voice.GlobalPosition = _head.GlobalPosition;
		if (!_voice.Playing && _time >= _breathAt)
		{
			string path = $"res://assets/audio/sfx/crawler_breath_{_rng.RandiRange(1, 3):00}.wav";
			if (ResourceLoader.Exists(path))
			{
				_voice.Stream = GD.Load<AudioStream>(path);
				_voice.VolumeDb = Mathf.Lerp(-6f, -1f, Hurry);
				_voice.PitchScale = _rng.RandfRange(0.94f, 1.06f) * Mathf.Lerp(1f, 1.1f, Hurry);
				_voice.Play();
			}
			_breathAt = _time + BreathEvery * Mathf.Lerp(1f, 0.4f, Hurry) * _rng.RandfRange(0.4f, 1f);
		}
		// the blacklight on it: a cold violet-white glow where the lantern's cone falls
		float uv = ProjectDS.Player.Lantern.UvOn(GlobalPosition);
		_uvGlow.AlbedoColor = new Color(0.55f, 0.5f, 0.75f, 0.55f * uv);
	}

	/// <summary>Two bones from the shoulder/hip to the planted hand/foot, the elbow/knee thrown up and out.</summary>
	private void PlayStep(Vector3 at, float db)
	{
		if (_stepStreams == null)
		{
			var list = new List<AudioStream>();
			for (int i = 1; i <= 8; i++)
				if (ResourceLoader.Exists($"res://assets/audio/sfx/crawler_step_{i:00}.wav")) list.Add(GD.Load<AudioStream>($"res://assets/audio/sfx/crawler_step_{i:00}.wav"));
			_stepStreams = list.ToArray();
		}
		if (_stepStreams.Length == 0) return;
		var p = _steps[_stepVoice];
		if (p == null || !IsInstanceValid(p))
		{
			p = new AudioStreamPlayer3D { Name = $"Step{_stepVoice}", Bus = "Events", UnitSize = 4f, MaxDistance = 64f, TopLevel = true };
			AddChild(p);
			_steps[_stepVoice] = p;
		}
		_stepVoice = (_stepVoice + 1) % StepVoices;
		p.Stream = _stepStreams[_rng.RandiRange(0, _stepStreams.Length - 1)];
		p.VolumeDb = db;
		p.PitchScale = 1f + _rng.RandfRange(-0.08f, 0.08f);
		p.GlobalPosition = at;
		p.Play();
	}

	/// <summary>The remodel's body from its rest each frame: the spine heaving with the breath and rolling with its gait,
	/// the head hung and snapping to its twitches, the jaw hanging and working (wider as it hurries).</summary>
	private void PoseBody()
	{
		for (int i = 0; i < _skel.GetBoneCount(); i++)
		{
			var rest = _skel.GetBoneRest(i);
			_skel.SetBonePoseRotation(i, rest.Basis.GetRotationQuaternion());
			_skel.SetBonePosePosition(i, rest.Origin);
		}
		float t = (float)_time;
		float breath = Mathf.Sin(t * 2.2f);
		if (_bHips >= 0) _skel.SetBonePosePosition(_bHips, _skel.GetBoneRest(_bHips).Origin + new Vector3(0, breath * 0.012f, 0));
		BonePose.Turn(_skel, _bChest, Vector3.Right, breath * 0.04f);
		BonePose.Turn(_skel, _bChest, Vector3.Back, Mathf.Sin(t * 3.1f) * 0.05f * (0.3f + Hurry));
		// the head: hung low and twisted to its twitch
		if (_bHead >= 0) _skel.SetBonePoseRotation(_bHead, (_skel.GetBonePoseRotation(_bHead) * Quaternion.FromEuler(_twitch)).Normalized());
		BonePose.Turn(_skel, _bNeck, Vector3.Right, -0.15f + _twitch.X * 0.3f);
		// the jaw: hanging, working, gaping wider when it hurries
		float jaw = 0.12f + 0.08f * Mathf.Max(0f, Mathf.Sin(t * 1.7f)) + 0.25f * Hurry;
		BonePose.Turn(_skel, _bJaw, Vector3.Right, -jaw);
	}

	/// <summary>The remodel's limbs: the same two-bone solve as the old pieces (the elbow or knee thrown up and out), the
	/// bones turned to it; the hand or foot laid flat toward where it's going; the long fingers curling while it's lifted
	/// and splaying as it lands.</summary>
	private void PoseLimbs()
	{
		var inv = _skel.GlobalTransform.AffineInverse();
		var invB = _skel.GlobalBasis.Inverse();
		foreach (var l in _limbs)
		{
			if (l.BUpper < 0 || l.BLower < 0) continue;
			Vector3 S = _skel.GetBoneGlobalPose(l.BUpper).Origin;
			Vector3 E = _skel.GetBoneGlobalPose(l.BLower).Origin;
			Vector3 Wr = l.BHand >= 0 ? _skel.GetBoneGlobalPose(l.BHand).Origin : E;
			Vector3 T = inv * l.Plant;
			float l1 = S.DistanceTo(E), l2 = E.DistanceTo(Wr);
			Vector3 to = T - S;
			float d = Mathf.Clamp(to.Length(), 0.05f, (l1 + l2) * 0.98f);
			Vector3 dir = to.Normalized();
			float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
			float b = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
			Vector3 outward = invB * (GlobalBasis * new Vector3(Mathf.Sign(l.Root.X), 0, 0));
			Vector3 pole = ((invB * Vector3.Up).Normalized() * 2.2f + outward.Normalized()).Normalized();
			pole = (pole - dir * pole.Dot(dir)).Normalized();
			Vector3 K = S + dir * a + pole * b;
			BonePose.AimFrom(_skel, l.BUpper, E - S, K - S);
			Vector3 E2 = _skel.GetBoneGlobalPose(l.BLower).Origin;
			Vector3 W2 = l.BHand >= 0 ? _skel.GetBoneGlobalPose(l.BHand).Origin : E2;
			BonePose.AimFrom(_skel, l.BLower, W2 - E2, S + dir * d - E2);
			if (l.BHand >= 0)
			{
				// laid flat, toward where the body is heading (lifted: hanging, the fingers curling)
				Vector3 flatW = _heading with { Y = 0 };
				if (flatW.LengthSquared() < 1e-4f) flatW = -GlobalBasis.Z;
				float lifted = l.T < 1f ? Mathf.Sin(l.T * Mathf.Pi) : 0f;
				Vector3 want = invB * (flatW.Normalized() + Vector3.Down * (0.15f + 0.8f * lifted));
				BonePose.Aim(_skel, l.BHand, want);
				if (l.BFingers != null)
					foreach (int f in l.BFingers)
						if (f >= 0) BonePose.Turn(_skel, f, _skel.GetBoneGlobalPose(f).Basis.X.Normalized(), 0.5f * lifted - 0.15f);
			}
		}
	}

	private void Solve(Limb l)
	{
		if (l.Upper == null) return;   // (the remodel's: PoseLimbs)
		Vector3 root = GlobalTransform * l.Root;
		Vector3 to = l.Plant - root;
		float d = Mathf.Clamp(to.Length(), 0.05f, (l.L1 + l.L2) * 0.98f);
		Vector3 dir = to.Normalized();
		float a = (l.L1 * l.L1 - l.L2 * l.L2 + d * d) / (2f * d);
		float b = Mathf.Sqrt(Mathf.Max(0f, l.L1 * l.L1 - a * a));
		Vector3 outward = (GlobalBasis * new Vector3(Mathf.Sign(l.Root.X), 0, 0)).Normalized();
		Vector3 pole = (Vector3.Up * 2.2f + outward).Normalized();
		pole = (pole - dir * pole.Dot(dir)).Normalized();
		Vector3 knee = root + dir * a + pole * b;
		Vector3 foot = root + dir * d;
		Span(l.Upper, root, knee);
		Span(l.Lower, knee, foot);
		l.Knuckle.GlobalPosition = knee;
		Vector3 fwd = (GlobalBasis * -Vector3.Forward.Rotated(Vector3.Up, 0)).Normalized();
		Vector3 flat = ((foot - root) with { Y = 0 });
		if (flat.LengthSquared() < 1e-4f) flat = -fwd;
		l.Paw.GlobalTransform = new Transform3D(Basis.LookingAt(flat.Normalized(), Vector3.Up), foot);
	}

	private static void Span(MeshInstance3D mi, Vector3 a, Vector3 b)
	{
		Vector3 d = b - a;
		float len = d.Length();
		if (len < 1e-4f) return;
		Vector3 y = d / len;
		Vector3 x = Mathf.Abs(y.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up.Cross(y).Normalized();
		Vector3 z = x.Cross(y);
		mi.GlobalTransform = new Transform3D(new Basis(x, y * len, z), (a + b) * 0.5f);
	}
}
