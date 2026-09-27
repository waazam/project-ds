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
	}

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
		BuildBody();
		BuildLimbs();
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
		k.CommitTo(_body, "Torso", false);
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
		h.CommitTo(_head, "Skull", false);
	}

	private MeshInstance3D Bone(float r0, float r1)
	{
		var mi = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = r1, BottomRadius = r0, Height = 1f, RadialSegments = 8, Rings = 1 },
			MaterialOverride = _limbSkin, CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		};
		AddChild(mi);
		mi.TopLevel = true;
		return mi;
	}

	private void BuildLimbs()
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
			l.Paw = pk.CommitTo(this, front ? "Hand" : "Foot", false);
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
					AudioDirector.OneShot(this, "crawler_step", 8, l.Plant, Mathf.Lerp(-4f, 0f, Hurry), "Events", 4f, 0.08f);
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
		_head.Rotation = new Vector3(0.5f, 0, 0) + _twitch;
		// the body, heaving a little with the breath
		_body.Position = new Vector3(0, Mathf.Sin((float)_time * 2.2f) * 0.012f, 0);
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
	private void Solve(Limb l)
	{
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
