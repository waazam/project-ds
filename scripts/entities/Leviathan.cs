using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;
using ProjectDS.World.LakeParts;

namespace ProjectDS.Entities;

/// <summary>
/// Act 18's boss: the thing from the lake (Act 12), at home. A vast mound of hide at the bottom of a
/// blood-filled pit, crusted with eyes, eight tentacles thick as trees rising out of the blood round
/// it. It cannot be fought. It can be drained, and it rots as the blood goes.
///
/// The tentacles are posed on curves from their roots to their tips, so a slam can be aimed: a limb
/// rears up over a spot, holds there (the telegraph), strikes down onto it, lies there a moment, and
/// draws back. <see cref="Impact"/> fires when it lands.
///
/// <see cref="Rot"/> (0..1) is how far gone it is. The hide goes from glossy purple-grey to mottled,
/// sore-covered, sloughing zombie flesh (<c>rot_skin.gdshader</c>), the eyes cloud over, and it
/// thrashes harder and faster: hurt, and rabid.
///
/// Local space: y=0 is the pit floor; the body sits in the middle.
/// </summary>
public partial class Leviathan : Node3D
{
	public const int Segments = 18;
	/// <summary>Limbs round the body (the owner: "multiply them tentacles").</summary>
	public const int LimbCount = 14;
	/// <summary>How long the strike itself takes, from the top of the rear to the catwalk.</summary>
	public const float StrikeSeconds = 0.3f;
	public const float Length = 30f;

	public sealed class Tentacle
	{
		public Node3D Root;
		public readonly List<Node3D> Segs = new();
		public readonly List<Node3D> Eyes = new();
		public float Angle, Phase, BaseR;
		public Vector3 RootLocal;
		public enum S { Idle, Raise, Hold, Strike, Down, Recover, Limp }
		public S State = S.Idle;
		public float T, RaiseTime, HoldTime;
		public Vector3 Target;        // local
		public Vector3 Tip, Ctrl;     // local, current
		public Vector3 FromTip, FromCtrl;
		public bool Hit;
	}

	/// <summary>Fires when a slam lands (world position on the catwalk).</summary>
	public event Action<Vector3, Tentacle> Impact;

	public float Rot { get => _rot; set { _rot = Mathf.Clamp(value, 0f, 1f); ApplyRot(); } }
	/// <summary>The blood's surface (local y): the limbs rise out of it.</summary>
	public float Level { get; set; } = 22.5f;
	public bool Dead { get; private set; }
	public int EyeCount => _bodyEyes.Count + _tentacles.Sum(t => t.Eyes.Count);
	public int EyesBurst { get; private set; }
	public IReadOnlyList<Tentacle> Tentacles => _tentacles;
	public bool AnySlamming => _tentacles.Exists(t => t.State is Tentacle.S.Raise or Tentacle.S.Hold or Tentacle.S.Strike);

	private readonly List<Tentacle> _tentacles = new();
	private readonly List<Node3D> _bodyEyes = new();
	private readonly List<float> _eyeSize = new();
	private Node3D _body;
	private ShaderMaterial _skin;
	private StandardMaterial3D _sclera, _iris;
	private float _rot, _time, _convulse;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1818 };
	private Camera3D _cam;

	public override void _Ready()
	{
		_skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/rot_skin.gdshader") };
		_skin.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		BuildBody();
		for (int i = 0; i < LimbCount; i++) BuildTentacle(i);
		ApplyRot();
	}

	// ------------------------------------------------------------------ building

	private void BuildBody()
	{
		_body = new Node3D { Name = "Body", Position = new Vector3(0, 5f, 0) };
		AddChild(_body);
		var k = new MeshKit();
		k.Mat(_skin);
		k.Color = Colors.White;
		k.Blob(Vector3.Zero, new Vector3(10.5f, 8f, 10.5f), 18, 0.14f, false, 1f);
		for (int i = 0; i < 9; i++)
		{
			float a = i / 9f * Mathf.Tau + 0.3f;
			k.Blob(new Vector3(Mathf.Cos(a) * 7.5f, _rng.RandfRange(-2f, 3f), Mathf.Sin(a) * 7.5f), Vector3.One * _rng.RandfRange(3.5f, 5.5f), 40 + i, 0.2f, false, 1f);
		}
		k.CommitTo(_body, "Hide", true);
		// eyes all over the upper half, big ones near the crown
		for (int e = 0; e < 46; e++)
		{
			float a = _rng.RandfRange(0, Mathf.Tau), up = Mathf.Lerp(0.15f, 1f, Mathf.Pow(_rng.Randf(), 0.7f));
			Vector3 dir = new Vector3(Mathf.Cos(a) * Mathf.Sqrt(1 - up * up), up, Mathf.Sin(a) * Mathf.Sqrt(1 - up * up)).Normalized();
			Vector3 at = new(dir.X * 10.2f, dir.Y * 7.8f, dir.Z * 10.2f);
			var socket = new Node3D { Name = $"Eye{e}", Position = at };
			_body.AddChild(socket);
			BuildEye(socket);
			_bodyEyes.Add(socket);
			_eyeSize.Add(Mathf.Lerp(0.6f, 2.3f, Mathf.Pow(up, 2f)) * _rng.RandfRange(0.7f, 1.1f));
		}
	}

	private void BuildEye(Node3D socket)
	{
		LakeCreature.BuildEye(socket);
		// this creature's own copies of the eye's materials, so they can cloud over as it rots
		var parts = socket.GetChildren();
		if (parts.Count >= 2 && parts[0] is MeshInstance3D white && parts[1] is MeshInstance3D iris)
		{
			_sclera ??= (StandardMaterial3D)((StandardMaterial3D)white.MaterialOverride).Duplicate();
			_iris ??= (StandardMaterial3D)((StandardMaterial3D)iris.MaterialOverride).Duplicate();
			white.MaterialOverride = _sclera;
			iris.MaterialOverride = _iris;
		}
	}

	private void BuildTentacle(int i)
	{
		var t = new Tentacle
		{
			Angle = i / (float)LimbCount * Mathf.Tau + 0.2f, Phase = _rng.RandfRange(0, Mathf.Tau), BaseR = _rng.RandfRange(1.0f, 1.5f),
		};
		float rootR = i % 2 == 0 ? 7.5f : 9f;
		t.RootLocal = new Vector3(Mathf.Cos(t.Angle) * rootR, i % 2 == 0 ? 7f : 3.5f, Mathf.Sin(t.Angle) * rootR);
		t.Root = new Node3D { Name = $"Tentacle{i}" };
		AddChild(t.Root);
		float segLen = Length / Segments;
		for (int s = 0; s < Segments; s++)
		{
			float f0 = (float)s / Segments, f1 = (float)(s + 1) / Segments;
			float r0 = Radius(t.BaseR, f0), r1 = Radius(t.BaseR, f1);
			var seg = new Node3D { Name = $"S{s}" };
			t.Root.AddChild(seg);
			var k = new MeshKit();
			k.Mat(_skin);
			k.Color = Colors.White;
			k.Cylinder(Vector3.Zero, Vector3.Up * segLen, r0, r1, 10, false, 1f);
			k.Blob(Vector3.Up * segLen, Vector3.One * r1 * 1.05f, s * 7 + i, 0.06f, false);
			k.CommitTo(seg, "Seg", true);
			t.Segs.Add(seg);
			if (s >= 5 && s <= 15 && s % 2 == 1)
			{
				float ang = _rng.RandfRange(0, Mathf.Tau), rr = Mathf.Lerp(r0, r1, 0.5f);
				var socket = new Node3D { Name = $"Eye{s}", Position = new Vector3(Mathf.Cos(ang) * rr, segLen * 0.5f, Mathf.Sin(ang) * rr) };
				seg.AddChild(socket);
				BuildEye(socket);
				t.Eyes.Add(socket);
			}
		}
		t.Tip = IdleTip(t);
		t.Ctrl = IdleCtrl(t, t.Tip);
		_tentacles.Add(t);
	}

	private static float Radius(float baseR, float f) => Mathf.Lerp(baseR, Mathf.Max(0.14f, baseR * 0.12f), Mathf.Pow(f, 1.4f));

	// ------------------------------------------------------------------ the fight's API

	/// <summary>How hurt and rabid it is, for its movement: 1 at no rot, up to about 2.5 at full.</summary>
	private float Rage => 1f + 1.5f * _rot;

	/// <summary>Rear a free limb (the one best placed) up over <paramref name="world"/> and bring it down
	/// after <paramref name="telegraph"/> seconds. Null if every limb is busy.</summary>
	public Tentacle Slam(Vector3 world, float telegraph)
	{
		Vector3 target = ToLocal(world);
		float want = Mathf.Atan2(target.Z, target.X);
		Tentacle best = null;
		float bestD = float.MaxValue;
		foreach (var t in _tentacles)
		{
			if (t.State != Tentacle.S.Idle) continue;
			float d = Mathf.Abs(Mathf.AngleDifference(t.Angle, want));
			if (d < bestD) { bestD = d; best = t; }
		}
		if (best == null || bestD > Mathf.Pi * 0.6f) return null;
		best.Target = target;
		best.State = Tentacle.S.Raise;
		best.T = 0f;
		best.RaiseTime = telegraph * 0.6f;
		best.HoldTime = telegraph * 0.4f;
		best.FromTip = best.Tip;
		best.FromCtrl = best.Ctrl;
		best.Hit = false;
		return best;
	}

	/// <summary>Distance from a world point to the lower, catwalk-height stretch of a slamming limb.</summary>
	public float LimbDistance(Tentacle t, Vector3 world)
	{
		Vector3 p = ToLocal(world);
		float best = float.MaxValue;
		for (int i = Segments * 2 / 3; i <= Segments; i++)
		{
			Vector3 c = Curve(t, i / (float)Segments);
			best = Mathf.Min(best, c.DistanceTo(p));
		}
		return best;
	}

	/// <summary>A jolt of pain (a valve turned): every limb thrashes for a moment.</summary>
	public void Convulse(float seconds) => _convulse = Mathf.Max(_convulse, seconds);

	// ------------------------------------------------------------------ posing

	private Vector3 IdleTip(Tentacle t)
	{
		float rage = Rage, tt = _time * 0.35f * rage + t.Phase;
		float r = 11.5f + 2.2f * Mathf.Sin(tt * 0.7f);
		float a = t.Angle + 0.25f * Mathf.Sin(tt * 0.5f);
		float y = Mathf.Max(Level, 0f) + 7f + 4f * Mathf.Sin(tt) + (Dead ? 0 : 0);
		return new Vector3(Mathf.Cos(a) * r, Mathf.Min(y, 30f), Mathf.Sin(a) * r);
	}

	private Vector3 IdleCtrl(Tentacle t, Vector3 tip) => (t.RootLocal).Lerp(tip, 0.55f) + Vector3.Up * 9f + new Vector3(Mathf.Cos(t.Angle), 0, Mathf.Sin(t.Angle)) * -2f;

	/// <summary>The limb's centre line at f (0 root .. 1 tip): a quadratic curve root -> ctrl -> tip, with
	/// a writhe along it.</summary>
	private Vector3 Curve(Tentacle t, float f)
	{
		Vector3 a = t.RootLocal, b = t.Ctrl, c = t.Tip;
		Vector3 p = (1 - f) * (1 - f) * a + 2 * (1 - f) * f * b + f * f * c;
		float amp = (0.5f + 0.9f * (Rage - 1f) + (_convulse > 0 ? 1.6f : 0f)) * f * (1 - f) * 4f;
		if (t.State is Tentacle.S.Strike or Tentacle.S.Down) amp *= 0.3f;
		if (t.State == Tentacle.S.Limp) amp *= 0.1f;
		float w = _time * (1.1f * Rage) + t.Phase - f * 5f;
		Vector3 side = new(-Mathf.Sin(t.Angle), 0, Mathf.Cos(t.Angle));
		return p + side * Mathf.Sin(w) * amp + Vector3.Up * Mathf.Cos(w * 0.8f) * amp * 0.4f;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_time += dt;
		_convulse = Mathf.Max(0f, _convulse - dt);
		_cam ??= GetViewport().GetCamera3D();
		foreach (var t in _tentacles) Step(t, dt);
		foreach (var t in _tentacles) Pose(t);
		LookAtCamera();
	}

	private void Step(Tentacle t, float dt)
	{
		t.T += dt;
		Vector3 above = t.Target + Vector3.Up * 10f + new Vector3(-t.Target.X, 0, -t.Target.Z).Normalized() * 2.5f;
		Vector3 aboveCtrl = (t.RootLocal).Lerp(above, 0.5f) + Vector3.Up * 14f;
		switch (t.State)
		{
			case Tentacle.S.Idle:
			{
				Vector3 tip = IdleTip(t);
				t.Tip = t.Tip.Lerp(tip, Mathf.Min(1f, dt * 1.5f));
				t.Ctrl = t.Ctrl.Lerp(IdleCtrl(t, t.Tip), Mathf.Min(1f, dt * 1.5f));
				break;
			}
			case Tentacle.S.Raise:
			{
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t.T / Mathf.Max(0.05f, t.RaiseTime)));
				t.Tip = t.FromTip.Lerp(above, u);
				t.Ctrl = t.FromCtrl.Lerp(aboveCtrl, u);
				if (t.T >= t.RaiseTime) { t.State = Tentacle.S.Hold; t.T = 0f; }
				break;
			}
			case Tentacle.S.Hold:
				// rearing back, trembling
				t.Tip = above + Vector3.Up * (0.6f * Mathf.SmoothStep(0f, 1f, t.T / Mathf.Max(0.05f, t.HoldTime))) + new Vector3(Mathf.Sin(_time * 30f), 0, Mathf.Cos(_time * 27f)) * 0.05f;
				t.Ctrl = aboveCtrl;
				if (t.T >= t.HoldTime) { t.State = Tentacle.S.Strike; t.T = 0f; t.FromTip = t.Tip; t.FromCtrl = t.Ctrl; }
				break;
			case Tentacle.S.Strike:
			{
				const float strike = StrikeSeconds;
				float u = Mathf.Min(1f, t.T / strike);
				u *= u;
				Vector3 downCtrl = (t.RootLocal).Lerp(t.Target, 0.55f) + Vector3.Up * 16f;
				t.Tip = t.FromTip.Lerp(t.Target + Vector3.Up * 0.4f, u);
				t.Ctrl = t.FromCtrl.Lerp(downCtrl, u);
				if (t.T >= strike && !t.Hit)
				{
					t.Hit = true;
					t.State = Tentacle.S.Down;
					t.T = 0f;
					Impact?.Invoke(ToGlobal(t.Target), t);
				}
				break;
			}
			case Tentacle.S.Down:
				if (t.T >= 1.0f / Mathf.Sqrt(Rage)) { t.State = Tentacle.S.Recover; t.T = 0f; t.FromTip = t.Tip; t.FromCtrl = t.Ctrl; }
				break;
			case Tentacle.S.Recover:
			{
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t.T / 1.2f));
				Vector3 tip = IdleTip(t);
				t.Tip = t.FromTip.Lerp(tip, u);
				t.Ctrl = t.FromCtrl.Lerp(IdleCtrl(t, tip), u);
				if (t.T >= 1.2f) t.State = Tentacle.S.Idle;
				break;
			}
			case Tentacle.S.Limp:
			{
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t.T / 3f));
				Vector3 floorTip = new(Mathf.Cos(t.Angle) * 16f, 0.8f, Mathf.Sin(t.Angle) * 16f);
				t.Tip = t.FromTip.Lerp(floorTip, u);
				t.Ctrl = t.FromCtrl.Lerp((t.RootLocal).Lerp(floorTip, 0.5f) + Vector3.Up * 3f, u);
				break;
			}
		}
	}

	private void Pose(Tentacle t)
	{
		float nominal = Length / Segments;
		for (int i = 0; i < Segments; i++)
		{
			Vector3 a = Curve(t, i / (float)Segments), b = Curve(t, (i + 1) / (float)Segments);
			Vector3 d = b - a;
			float len = d.Length();
			var seg = t.Segs[i];
			seg.Position = a;
			if (len > 0.001f)
			{
				Vector3 y = d / len;
				Vector3 x = y.Cross(Vector3.Forward);
				if (x.LengthSquared() < 0.01f) x = y.Cross(Vector3.Right);
				x = x.Normalized();
				Vector3 z = x.Cross(y);
				seg.Basis = new Basis(x, y * (len / nominal), z);
			}
		}
	}

	private void LookAtCamera()
	{
		if (_cam == null || !IsInstanceValid(_cam)) return;
		Vector3 target = _cam.GlobalPosition;
		for (int e = 0; e < _bodyEyes.Count; e++)
		{
			var eye = _bodyEyes[e];
			if (!eye.Visible) continue;
			float s = _eyeSize[e] * eye.GetMeta("swell", 1f).AsSingle();
			Vector3 at = eye.GlobalPosition;
			if (at.DistanceSquaredTo(target) > 0.01f) eye.GlobalBasis = Basis.LookingAt(target - at, Vector3.Up) * Basis.FromScale(Vector3.One * s);
		}
		foreach (var t in _tentacles)
			foreach (var eye in t.Eyes)
			{
				if (!eye.Visible) continue;
				Vector3 at = eye.GlobalPosition;
				float s = 0.55f * eye.GetMeta("swell", 1f).AsSingle();
				if (at.DistanceSquaredTo(target) > 0.01f) eye.GlobalBasis = Basis.LookingAt(target - at, Vector3.Up) * Basis.FromScale(Vector3.One * s);
			}
	}

	private void ApplyRot()
	{
		_skin?.SetShaderParameter("rot", _rot);
		if (_sclera != null)
		{
			// bloodshot white -> milky, clouded, yellow-grey
			_sclera.AlbedoColor = new Color(0.9f, 0.84f, 0.72f).Lerp(new Color(0.62f, 0.64f, 0.5f), _rot);
			_sclera.EmissionEnergyMultiplier = Mathf.Lerp(1f, 0.3f, _rot);
			_sclera.Roughness = Mathf.Lerp(0.12f, 0.5f, _rot);
		}
		if (_iris != null)
		{
			// the red glow burns hotter as it goes rabid, then clouds white over it
			_iris.EmissionEnergyMultiplier = Mathf.Lerp(1.1f, 2.4f, Mathf.Min(1f, _rot * 1.6f)) * Mathf.Lerp(1f, 0.5f, Mathf.SmoothStep(0.7f, 1f, _rot));
			_iris.AlbedoColor = new Color(0.45f, 0.03f, 0.02f).Lerp(new Color(0.55f, 0.5f, 0.45f), Mathf.SmoothStep(0.6f, 1f, _rot) * 0.6f);
		}
	}

	// ------------------------------------------------------------------ the end

	/// <summary>Every eye, in the order they go: from the crown of the body outward and down, then the limbs.</summary>
	public List<Node3D> EyesInBurstOrder()
	{
		var list = new List<Node3D>(_bodyEyes);
		list.Sort((a, b) => b.Position.Y.CompareTo(a.Position.Y));
		foreach (var t in _tentacles) list.AddRange(t.Eyes);
		return list;
	}

	/// <summary>One eye bursts: it swells, and goes, in a spray of blood.</summary>
	public void Burst(Node3D eye)
	{
		if (!eye.Visible) return;
		var tw = eye.CreateTween();
		tw.TweenMethod(Callable.From<float>(s => eye.SetMeta("swell", s)), 1f, 1.5f, 0.12f);
		tw.TweenCallback(Callable.From(() =>
		{
			eye.Visible = false;
			EyesBurst++;
			Spray(eye.GlobalPosition, (eye.GlobalPosition - GlobalPosition - Vector3.Up * 5f).Normalized());
		}));
	}

	private void Spray(Vector3 at, Vector3 dir)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = dir, Spread = 35f, InitialVelocityMin = 3f, InitialVelocityMax = 8f, Gravity = new Vector3(0, -9.8f, 0),
			ScaleMin = 0.8f, ScaleMax = 2f,
		};
		var p = new GpuParticles3D
		{
			Amount = 36, Lifetime = 1.6, OneShot = true, Explosiveness = 0.9f, ProcessMaterial = pm,
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.35f,
				Material = new StandardMaterial3D
				{
					AlbedoTexture = LakeFx.SoftDot(), AlbedoColor = new Color(0.45f, 0.02f, 0.02f, 0.95f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(new Vector3(-10, -15, -10), new Vector3(20, 25, 20)),
		};
		GetParent().AddChild(p);
		p.GlobalPosition = at;
		p.Emitting = true;
		GetTree().CreateTimer(2.5).Timeout += p.QueueFree;
	}

	/// <summary>Dead: every limb thrashes once more and then drops, and the body sags.</summary>
	public void Die()
	{
		Dead = true;
		_convulse = 1.6f;
		GetTree().CreateTimer(1.6).Timeout += () =>
		{
			foreach (var t in _tentacles) { t.State = Tentacle.S.Limp; t.T = 0f; t.FromTip = t.Tip; t.FromCtrl = t.Ctrl; }
			var tw = CreateTween();
			tw.TweenProperty(_body, "scale", new Vector3(1.05f, 0.78f, 1.05f), 3.5f).SetTrans(Tween.TransitionType.Sine);
		};
	}
}
