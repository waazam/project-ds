using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// The wendigo's arm through the wall (Act 23's crawlspace, the owner's document): it smashes out of the boards at the
/// height of a head and hangs across the cavity reaching and grabbing, and the only way on is to crouch under it. Four
/// times, and it's bloodier each time (the boards tear it: blood and frost left on the holes as it pulls back).
///
/// The same ash-grey skin as the wendigo itself (<see cref="Wendigo"/>), its fingers black and far too long. Two bones,
/// solved toward what it's grabbing for each frame; the fingers clench and open; it twitches. Its colliders are the
/// upper arm, the forearm and the hand at the height they are: standing, it's a wall; crouched (the body's 1.1 m), it
/// passes over. Stand within its reach and it grabs: a lunge at the head and a shove back down the cavity.
///
/// The node sits on the wall's face at the hole, +X out of the wall across the cavity, +Y up.
/// </summary>
public partial class WendigoArm : Node3D
{
	/// <summary>0 clean .. 1 torn to the bone (the fourth).</summary>
	public float Bloodiness = 0.2f;
	/// <summary>How far across the cavity the far wall is (its face).</summary>
	public float Across = 0.9f;
	/// <summary>The lowest its hand, forearm and elbow reach (relative to the node): above a crouched head.</summary>
	public float Floor = -0.2f;
	public enum State { Waiting, Burst, Reach, Withdraw, Gone }
	public State Phase { get; private set; } = State.Waiting;
	public int Grabs { get; private set; }
	public event Action<WendigoArm> Grabbed;
	/// <summary>Where it's reaching for (world): the player's head, while they're near.</summary>
	public Func<Vector3?> Prey;

	private const float L1 = 0.62f, L2 = 0.6f;
	private static readonly Vector3 Shoulder = new(-0.7f, 0.05f, 0f);
	private Node3D _upper, _fore, _hand;
	private readonly List<Node3D> _fingers = new();
	private StaticBody3D _body;
	private CollisionShape3D _cUpper, _cFore, _cHand;
	private float _t, _out;   // _out: 0 in the wall .. 1 at full reach
	private Vector3 _target, _aim;
	private double _clock, _twitchAt;
	private float _clench, _grabCool;
	private readonly RandomNumberGenerator _rng = new();
	private GpuParticles3D _drip;

	public override void _Ready()
	{
		_rng.Seed = (ulong)GetInstanceId();
		// corpse-grey, bluer than the body's (the cold in it), a wet sheen, a pale rim catching the lantern
		var skin = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = new Color(0.8f, 0.86f, 1f), Roughness = 0.5f, MetallicSpecular = 0.55f, AlbedoTexture = ProcTextures.Grime(), RimEnabled = true, Rim = 0.45f, RimTint = 0.2f };
		var blood = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.32f, 0.015f, 0.015f, Mathf.Clamp(0.15f + Bloodiness * 0.7f, 0f, 0.9f)), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Roughness = 0.2f, MetallicSpecular = 0.7f, AlbedoTexture = ProcTextures.Grime(), Grow = true, GrowAmount = 0.004f,
		};
		_upper = Bone("Upper", skin, blood, true);
		_fore = Bone("Fore", skin, blood, false);
		_hand = new Node3D { Name = "Hand" };
		AddChild(_hand);
		var hk = new MeshKit();
		hk.Mat(skin);
		// a big frostbitten hand: black-blue, the knuckles knobbed
		Wendigo.Loft(hk, new() { (new Vector3(-0.03f, 0, 0), 0.058f, 0.036f, Wendigo.Frostbite), (new Vector3(0.07f, 0, 0), 0.085f, 0.034f, Wendigo.Frostbite), (new Vector3(0.14f, 0, 0), 0.08f, 0.03f, new Color(0.06f, 0.06f, 0.08f)) }, 9);
		hk.CommitTo(_hand, "Palm", false).MaterialOverlay = blood;
		for (int f = 0; f < 5; f++)
		{
			bool thumb = f == 4;
			float spread = thumb ? -0.08f : (f - 1.5f) * 0.045f;
			var fn = new Node3D { Name = $"Finger{f}", Position = new Vector3(thumb ? 0.04f : 0.14f, thumb ? -0.03f : 0, spread), Rotation = new Vector3(0, thumb ? -0.8f : spread * 2.5f, 0) };
			_hand.AddChild(fn);
			var fk = new MeshKit();
			fk.Mat(skin);
			// far too long, knuckled twice, ending in a hooked black claw
			float len = thumb ? 0.16f : 0.36f + 0.06f * (1.5f - Mathf.Abs(f - 1.5f));
			var black = new Color(0.03f, 0.03f, 0.04f);
			Wendigo.Loft(fk, new()
			{
				(Vector3.Zero, 0.024f, 0.022f, Wendigo.Frostbite), (new Vector3(len * 0.3f, -0.004f, 0), 0.018f, 0.017f, Wendigo.Frostbite),
				(new Vector3(len * 0.36f, -0.006f, 0), 0.022f, 0.02f, Wendigo.Frostbite), (new Vector3(len * 0.64f, -0.014f, 0), 0.014f, 0.013f, Wendigo.Frostbite),
				(new Vector3(len * 0.7f, -0.018f, 0), 0.016f, 0.015f, Wendigo.Frostbite), (new Vector3(len * 0.9f, -0.03f, 0), 0.011f, 0.01f, black),
				(new Vector3(len + 0.04f, -0.06f, 0), 0.006f, 0.005f, black), (new Vector3(len + 0.09f, -0.1f, 0), 0.001f, 0.001f, black),
			}, 6);
			fk.CommitTo(fn, "Finger", false);
			_fingers.Add(fn);
		}
		// solid where it is: a standing body can't get past it, a crouched one goes under
		_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		_cUpper = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.12f, Height = L1 + 0.24f }, Disabled = true };
		_cFore = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.1f, Height = L2 + 0.2f }, Disabled = true };
		_cHand = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.15f }, Disabled = true };
		_body.AddChild(_cUpper); _body.AddChild(_cFore); _body.AddChild(_cHand);
		AddChild(_body);
		// blood running off it (more on each)
		_drip = new GpuParticles3D
		{
			Name = "Drip", Amount = 6 + (int)(Bloodiness * 18), Lifetime = 0.8f, LocalCoords = false, Emitting = false,
			ProcessMaterial = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.05f, Gravity = new Vector3(0, -7f, 0), ScaleMin = 0.6f, ScaleMax = 1.2f },
			DrawPass1 = new SphereMesh { Radius = 0.008f, Height = 0.016f, RadialSegments = 4, Rings = 2, Material = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.02f, 0.02f), Roughness = 0.1f, MetallicSpecular = 0.8f } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_fore.AddChild(_drip);
		// the storm's cold light in through the hole with it (the arm reads against the dark of the cavity: a cold rim, not a glare)
		AddChild(new OmniLight3D { Name = "HoleLight", Position = new Vector3(0.3f, 0.35f, 0), LightColor = new Color(0.55f, 0.66f, 0.86f), LightEnergy = 0.85f, OmniRange = 2.2f, OmniAttenuation = 1.4f, ShadowEnabled = false });
		Visible = false;
		_target = new Vector3(Across * 0.6f, 0.1f, 0);
		_aim = _target;
		Pose(0f);
	}

	private Node3D Bone(string name, Material skin, Material blood, bool upper)
	{
		var n = new Node3D { Name = name };
		AddChild(n);
		var k = new MeshKit();
		k.Mat(skin);
		float L = upper ? L1 : L2;
		// a giant's limb starved to rope and bone: thick at the shoulder, wasted between, a great knot of an elbow,
		// the forearm going bruised and then black toward the hand (+X along the bone)
		if (upper)
			Wendigo.Loft(k, new()
			{
				(new Vector3(-0.06f, 0, 0), 0.125f, 0.115f, Wendigo.Ash), (new Vector3(L * 0.12f, 0.01f, 0), 0.118f, 0.105f, Wendigo.Ash),
				(new Vector3(L * 0.38f, 0, 0), 0.092f, 0.085f, Wendigo.AshDark), (new Vector3(L * 0.62f, -0.005f, 0), 0.078f, 0.07f, Wendigo.Bruise),
				(new Vector3(L * 0.86f, 0, 0), 0.088f, 0.08f, Wendigo.AshDark), (new Vector3(L, 0.01f, 0), 0.112f, 0.1f, Wendigo.BoneCol),
			}, 10);
		else
		{
			Wendigo.Loft(k, new()
			{
				(new Vector3(-0.05f, 0, 0), 0.105f, 0.095f, Wendigo.BoneCol), (new Vector3(L * 0.14f, 0, 0), 0.088f, 0.08f, Wendigo.AshDark),
				(new Vector3(L * 0.45f, 0, 0), 0.074f, 0.064f, Wendigo.Bruise), (new Vector3(L * 0.75f, 0, 0), 0.058f, 0.05f, Wendigo.Frostbite),
				(new Vector3(L, 0, 0), 0.052f, 0.042f, Wendigo.Frostbite),
			}, 10);
			// tendons standing out along it, like cords under the skin
			for (int t = 0; t < 3; t++)
			{
				float a = t * 2.1f + 0.4f;
				Vector3 o(float r) => new(0, Mathf.Cos(a) * r, Mathf.Sin(a) * r);
				Wendigo.Loft(k, new() { (new Vector3(L * 0.1f, 0, 0) + o(0.075f), 0.008f, 0.008f, Wendigo.AshDark), (new Vector3(L * 0.5f, 0, 0) + o(0.068f), 0.013f, 0.012f, Wendigo.Bruise), (new Vector3(L * 0.92f, 0, 0) + o(0.048f), 0.006f, 0.006f, Wendigo.Frostbite) }, 5);
			}
		}
		k.CommitTo(n, "Bone", false).MaterialOverlay = blood;
		// frost crusted on it (it's been out in the storm)
		var f = new MeshKit();
		f.Mat(WinterWoods.SoftSnow);
		f.Color = Colors.White;
		var rng = new RandomNumberGenerator { Seed = (ulong)(upper ? 71 : 73) };
		for (int i = 0; i < 6; i++)
		{
			float x = rng.RandfRange(0.1f, 0.9f) * L, ang = rng.RandfRange(0f, Mathf.Tau), r = upper ? 0.09f : 0.065f;
			f.Blob(new Vector3(x, Mathf.Cos(ang) * r, Mathf.Sin(ang) * r), new Vector3(0.04f, 0.018f, 0.03f), 90 + i + (upper ? 0 : 20), 0.4f, false, 1f);
		}
		f.CommitTo(n, "Frost", false);
		return n;
	}

	/// <summary>Out of the wall: the boards burst (the caller does the hole and the sound), and it's reaching.</summary>
	public void Burst()
	{
		if (Phase != State.Waiting) return;
		Phase = State.Burst;
		Visible = true;
		_t = 0f;
		foreach (var c in new[] { _cUpper, _cFore, _cHand }) c.Disabled = false;
		_drip.Emitting = true;
	}

	/// <summary>Back into the wall (the prey gone past it).</summary>
	public void Withdraw()
	{
		if (Phase is State.Withdraw or State.Gone or State.Waiting) return;
		Phase = State.Withdraw;
		_t = 0f;
		AudioDirector.OneShot(this, "claw_scrape", 2, GlobalPosition, -2f, "Events", 3f, 0.08f);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Phase is State.Waiting or State.Gone) return;
		float dt = (float)delta;
		_clock += delta;
		_t += dt;
		_grabCool -= dt;
		switch (Phase)
		{
			case State.Burst:
				_out = Mathf.Clamp(_t / 0.18f, 0f, 1f);
				_out = 1f - (1f - _out) * (1f - _out);
				if (_t >= 0.18f) { Phase = State.Reach; _t = 0f; }
				break;
			case State.Withdraw:
				_out = 1f - Mathf.Clamp(_t / 0.4f, 0f, 1f);
				if (_t >= 0.4f)
				{
					Phase = State.Gone;
					Visible = false;
					foreach (var c in new[] { _cUpper, _cFore, _cHand }) c.Disabled = true;
					_drip.Emitting = false;
					return;
				}
				break;
		}
		// what it's reaching for: the prey's head if it's anywhere near, else feeling along the cavity
		Vector3 want;
		var prey = Prey?.Invoke();
		if (prey is { } head && head.DistanceTo(GlobalPosition) < 2.6f)
		{
			want = ToLocal(head);
			want.X = Mathf.Clamp(want.X, 0.3f, Across - 0.38f);   // (its fingers, 0.3 m past the hand, just scratch the far boards)
			want.Z = Mathf.Clamp(want.Z, -0.45f, 0.45f);
		}
		else want = new Vector3(Across * 0.65f, 0.15f * Mathf.Sin((float)_clock * 1.7f), 0.3f * Mathf.Sin((float)_clock * 0.9f));
		// jerky: a new twitch of the aim every so often, snapped to fast
		if (_clock >= _twitchAt)
		{
			_twitchAt = _clock + _rng.RandfRange(0.12f, 0.45f);
			_aim = want + new Vector3(_rng.RandfRange(-0.08f, 0.08f), _rng.RandfRange(-0.1f, 0.12f), _rng.RandfRange(-0.15f, 0.15f));
		}
		_target = _target.Lerp(_aim, 1f - Mathf.Exp(-dt * 14f));
		_target.Y = Mathf.Max(_target.Y, Floor);
		_clench = 0.5f + 0.5f * Mathf.Sin((float)_clock * 9f + Mathf.Sin((float)_clock * 3.1f) * 2f);
		Pose(_out);
		// the grab: someone standing within its reach
		if (Phase == State.Reach && _grabCool <= 0f && prey is { } h2)
		{
			var hand = _hand.GlobalPosition;
			if (new Vector2(hand.X - h2.X, hand.Z - h2.Z).Length() < 0.6f && Mathf.Abs(hand.Y - h2.Y) < 0.45f)
			{
				_grabCool = 2.2f;
				Grabs++;
				Grabbed?.Invoke(this);
			}
		}
	}

	/// <summary>The two bones and the hand for <paramref name="reach"/> (0 in the wall .. 1 out): the shoulder deep in the
	/// wall, the elbow bent up (the pole), the hand at the target; the fingers clenching.</summary>
	private void Pose(float reach)
	{
		Vector3 tgt = _target;
		Vector3 s = Shoulder + new Vector3(-(1f - reach) * 0.9f, 0, 0);
		tgt = s + (tgt - s) * Mathf.Lerp(0.5f, 1f, reach);
		Vector3 d = tgt - s;
		float dist = Mathf.Clamp(d.Length(), 0.2f, L1 + L2 - 0.02f);
		Vector3 dir = d.Normalized();
		tgt = s + dir * dist;
		float cosA = (L1 * L1 + dist * dist - L2 * L2) / (2f * L1 * dist);
		float a = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
		Vector3 pole = Vector3.Up;
		Vector3 side = dir.Cross(pole);
		if (side.LengthSquared() < 1e-4f) side = Vector3.Back;
		Vector3 bendDir = side.Normalized().Cross(dir).Normalized();
		if (bendDir.Dot(pole) < 0f) bendDir = -bendDir;
		Vector3 elbow = s + dir * (L1 * Mathf.Cos(a)) + bendDir * (L1 * Mathf.Sin(a));
		Place(_upper, s, elbow);
		Place(_fore, elbow, tgt);
		_hand.Transform = new Transform3D(Along(tgt - elbow), tgt);
		for (int i = 0; i < _fingers.Count; i++)
			_fingers[i].Rotation = _fingers[i].Rotation with { Z = -(0.2f + 1.1f * _clench) * (i == 4 ? 0.5f : 1f) };
		// its colliders along the bones
		if (_cUpper != null)
		{
			_cUpper.Transform = new Transform3D(Along(elbow - s) * new Basis(Vector3.Back, Mathf.Pi * 0.5f), (s + elbow) * 0.5f);
			_cFore.Transform = new Transform3D(Along(tgt - elbow) * new Basis(Vector3.Back, Mathf.Pi * 0.5f), (elbow + tgt) * 0.5f);
			_cHand.Position = tgt + (tgt - elbow).Normalized() * 0.15f;
		}
	}

	private static void Place(Node3D bone, Vector3 from, Vector3 to) => bone.Transform = new Transform3D(Along(to - from), from);

	/// <summary>A basis whose +X runs along <paramref name="v"/> (its +Y kept as near up as can be).</summary>
	private static Basis Along(Vector3 v)
	{
		Vector3 x = v.Normalized();
		Vector3 up = Mathf.Abs(x.Dot(Vector3.Up)) > 0.95f ? Vector3.Back : Vector3.Up;
		Vector3 z = x.Cross(up).Normalized();
		Vector3 y = z.Cross(x).Normalized();
		return new Basis(x, y, z);
	}

	/// <summary>Where its hand is now (tests).</summary>
	public Vector3 HandWorld => _hand.GlobalPosition;
	/// <summary>Its lowest point now (tests: a crouched head must pass under it).</summary>
	public float LowestWorldY => Mathf.Min(Mathf.Min(_hand.GlobalPosition.Y, _fore.GlobalPosition.Y), _upper.GlobalPosition.Y) - 0.07f;
}
