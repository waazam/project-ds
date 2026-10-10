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
/// The wendigo's own arm (<see cref="Wendigo"/>; tools/Blender/wendigo.py: assets/models/wendigo/wendigo_arm.glb): ash-grey
/// and sinewed, a knobbed wrist, a long frostbitten hand and fingers far too long, knuckled, hooked with black claws;
/// skinned to its bones. The upper arm and the forearm are solved toward what it's grabbing each frame and the skeleton
/// follows; every finger joint curls as it clenches and opens; it twitches. Its colliders are the
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
	/// <summary>(2026-10-10) Something clattered down further along the cavity (a thrown tin): it claws after the noise a
	/// few seconds, not at them, and doesn't grab.</summary>
	public void Distract(Vector3 world, float seconds)
	{
		_distractAt = world;
		_distractUntil = _clock + seconds;
		Distractions++;
	}
	public bool Distracted => _clock < _distractUntil;
	public int Distractions { get; private set; }
	private Vector3 _distractAt;
	private double _distractUntil = -1;

	private const float L1 = 0.78f, L2 = 0.74f;   // (the model's: tools/Blender/wendigo.py, ARM_L1, ARM_L2)
	private static readonly Vector3 Shoulder = new(-0.95f, 0.05f, 0f);
	private Skeleton3D _skel;
	private int _bUpper = -1, _bFore = -1, _bHand = -1;
	private readonly List<int[]> _fingerBones = new();
	private Transform3D[] _rest;
	private Node3D _upper, _fore, _hand;
	private StaticBody3D _body;
	private CollisionShape3D _cUpper, _cFore, _cHand;
	private float _t, _out;   // _out: 0 in the wall .. 1 at full reach
	private Vector3 _target, _aim;
	private double _clock, _twitchAt;
	private float _clench, _grabCool;
	// (frantic: starved for them) a lunge at the prey now and then: out at it, the hand splayed, then snapped shut and
	// dragged back
	private double _nextLunge, _lungeUntil = -1, _recoilUntil = -1;
	private readonly RandomNumberGenerator _rng = new();
	private GpuParticles3D _drip;

	public override void _Ready()
	{
		CreatureRim.Apply(this);
		_rng.Seed = (ulong)GetInstanceId();
		// corpse-grey, bluer than the body's (the cold in it), a wet sheen, a pale rim catching the lantern
		var skin = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, AlbedoColor = new Color(0.8f, 0.86f, 1f), Roughness = 0.5f, MetallicSpecular = 0.55f, AlbedoTexture = ProcTextures.Grime(), RimEnabled = true, Rim = 0.45f, RimTint = 0.2f };
		var blood = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.32f, 0.015f, 0.015f, Mathf.Clamp(0.15f + Bloodiness * 0.7f, 0f, 0.9f)), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Roughness = 0.2f, MetallicSpecular = 0.7f, AlbedoTexture = ProcTextures.Grime(), Grow = true, GrowAmount = 0.004f,
		};
		// the bones' markers (the drip hangs off the forearm's; the tests read the hand's)
		_upper = new Node3D { Name = "Upper" };
		_fore = new Node3D { Name = "Fore" };
		_hand = new Node3D { Name = "Hand" };
		AddChild(_upper); AddChild(_fore); AddChild(_hand);
		var model = GD.Load<PackedScene>("res://assets/models/wendigo/wendigo_arm.glb").Instantiate<Node3D>();
		model.Name = "Model";
		AddChild(model);
		_skel = FindSkeleton(model);
		if (_skel != null)
		{
			_bUpper = _skel.FindBone("upper");
			_bFore = _skel.FindBone("fore");
			_bHand = _skel.FindBone("hand");
			for (int f = 0; f < 5; f++)
				_fingerBones.Add(new[] { _skel.FindBone($"f{f}_1"), _skel.FindBone($"f{f}_2"), _skel.FindBone($"f{f}_3") });
			_rest = new Transform3D[_skel.GetBoneCount()];
			for (int b = 0; b < _rest.Length; b++) _rest[b] = _skel.GetBoneGlobalRest(b);
			foreach (var mi in Meshes(model))
			{
				mi.MaterialOverlay = blood;
				mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
				for (int si = 0; si < mi.Mesh.GetSurfaceCount(); si++)
					if (mi.Mesh.SurfaceGetMaterial(si) is StandardMaterial3D m)
					{
						m.SetMeta("detail_kind", -1);
						if ((m.ResourceName ?? "").StartsWith("w_skin")) { m.RimEnabled = true; m.Rim = 0.4f; m.RimTint = 0.25f; }
					}
			}
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

	private static Skeleton3D FindSkeleton(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is Skeleton3D sk) return sk;
			var f = FindSkeleton(c);
			if (f != null) return f;
		}
		return null;
	}

	private static IEnumerable<MeshInstance3D> Meshes(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is MeshInstance3D mi && mi.Mesh != null) yield return mi;
			foreach (var m in Meshes(c)) yield return m;
		}
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
		// what it's reaching for: the prey's head if it's anywhere near, else clawing along the cavity. Starved and meaning it
		// (the owner): its aim shifts to a new spot a few times a second, it trembles, it reaches out at them with the hand
		// splayed and snaps it shut, and drags it back to reach again
		Vector3 want;
		var prey = Distracted ? null : Prey?.Invoke();
		bool near = prey is { } head && head.DistanceTo(GlobalPosition) < 2.8f;
		if (Distracted)
		{
			// after the noise: the hand along the cavity toward it, scrabbling
			var d = ToLocal(_distractAt);
			want = new Vector3(Mathf.Clamp(d.X, 0.3f, Across - 0.38f), 0.1f * Mathf.Sin((float)_clock * 3.1f), Mathf.Clamp(d.Z, -0.5f, 0.5f));
		}
		else if (near)
		{
			want = ToLocal(prey.Value);
			want.X = Mathf.Clamp(want.X, 0.3f, Across - 0.38f);   // (its fingers, 0.3 m past the hand, just scratch the far boards)
			want.Z = Mathf.Clamp(want.Z, -0.5f, 0.5f);
		}
		else want = new Vector3(Across * 0.6f, 0.2f * Mathf.Sin((float)_clock * 2.3f), 0.4f * Mathf.Sin((float)_clock * 1.4f));
		bool lunging = _clock < _lungeUntil, recoiling = !lunging && _clock < _recoilUntil;
		if (Phase == State.Reach && near && !lunging && !recoiling && _clock >= _nextLunge)
		{
			// (deliberate, 2026-10-04, the owner: "it is a little too fast when his hand is grabbing around for the player and will
			// look scarier if he is intentionally trying to grab you and not just flailing as fast as he can": about half the
			// pace it had, each reach meant)
			_lungeUntil = _clock + _rng.RandfRange(0.26f, 0.36f);
			_recoilUntil = _lungeUntil + _rng.RandfRange(0.38f, 0.58f);
			_nextLunge = _recoilUntil + _rng.RandfRange(0.6f, 1.3f);
			lunging = true;
			if (_rng.Randf() < 0.6f) AudioDirector.OneShot(this, "claw_scrape", 2, GlobalPosition + GlobalBasis.X * 0.6f, -4f, "Events", 3f, 0.12f);
		}
		if (_clock >= _twitchAt)
		{
			_twitchAt = _clock + _rng.RandfRange(0.14f, 0.42f);
			float j = near ? 0.09f : 0.07f;
			_aim = want + new Vector3(_rng.RandfRange(-j, j) * 0.8f, _rng.RandfRange(-j, j * 1.4f), _rng.RandfRange(-j * 1.6f, j * 1.6f));
		}
		Vector3 aim = _aim;
		float rate = 9f;
		if (lunging) { aim = want; rate = 20f; }   // straight at them
		else if (recoiling) { aim = want with { X = want.X * 0.55f }; rate = 8f; }   // dragged back toward the wall
		_target = _target.Lerp(aim, 1f - Mathf.Exp(-dt * rate));
		// a tremble through it, always
		float tr = near ? 0.018f : 0.01f;
		_target += new Vector3(Mathf.Sin((float)_clock * 19f) * tr * 0.5f, Mathf.Sin((float)_clock * 17f + 1.3f) * tr, Mathf.Sin((float)_clock * 13f + 2.1f) * tr);
		_target.Y = Mathf.Max(_target.Y, Floor);
		// the hand: clawing open and shut fast; splayed wide on the lunge, snapped shut at its end
		float claw = 0.5f + 0.5f * Mathf.Sin((float)_clock * 8f + Mathf.Sin((float)_clock * 2.7f) * 2.5f);
		float wantClench = lunging ? -0.35f : recoiling ? 1f : claw;
		_clench = Mathf.MoveToward(_clench, wantClench, dt * (lunging || recoiling ? 9f : 5f));
		Pose(_out);
		// the grab: someone standing within its reach
		if (Phase == State.Reach && _grabCool <= 0f && prey is { } h2)
		{
			var hand = _hand.GlobalPosition;
			bool standing = h2.Y > GlobalPosition.Y + Floor - 0.15f;
			if (standing && new Vector2(hand.X - h2.X, hand.Z - h2.Z).Length() < 0.6f && Mathf.Abs(hand.Y - h2.Y) < 0.45f)
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
		PoseSkeleton(s, elbow, tgt);
		// its colliders along the bones
		if (_cUpper != null)
		{
			_cUpper.Transform = new Transform3D(Along(elbow - s) * new Basis(Vector3.Back, Mathf.Pi * 0.5f), (s + elbow) * 0.5f);
			_cFore.Transform = new Transform3D(Along(tgt - elbow) * new Basis(Vector3.Back, Mathf.Pi * 0.5f), (elbow + tgt) * 0.5f);
			_cHand.Position = tgt + (tgt - elbow).Normalized() * 0.15f;
		}
	}

	private static void Place(Node3D bone, Vector3 from, Vector3 to) => bone.Transform = new Transform3D(Along(to - from), from);

	/// <summary>The skeleton to the solved arm: the upper arm and the forearm turned from their rest onto their new lines,
	/// the hand carried with the forearm, and every finger joint curled toward the palm by how hard it's clenching.</summary>
	private void PoseSkeleton(Vector3 shoulder, Vector3 elbow, Vector3 wrist)
	{
		if (_skel == null || _bUpper < 0) return;
		var toSkel = _skel.GlobalTransform.AffineInverse() * GlobalTransform;
		Vector3 S = toSkel * shoulder, E = toSkel * elbow, W = toSkel * wrist;
		var g = new Transform3D[_rest.Length];
		// a bone turned from its rest line (to its child's head) onto a new one, its head at `at`
		Transform3D Turn(int b, Vector3 restTo, Vector3 newDir, Vector3 at)
		{
			var rd = (restTo - _rest[b].Origin).Normalized();
			var q = Arc(rd, newDir.Normalized());
			return new Transform3D(new Basis(q) * _rest[b].Basis, at);
		}
		g[_bUpper] = Turn(_bUpper, _rest[_bFore].Origin, E - S, S);
		g[_bFore] = Turn(_bFore, _rest[_bHand].Origin, W - E, E);
		var handTurn = Arc((_rest[_bHand].Origin - _rest[_bFore].Origin).Normalized(), (W - E).Normalized());
		g[_bHand] = new Transform3D(new Basis(handTurn) * _rest[_bHand].Basis, W);
		// the fingers: carried with the hand, then each joint curled about its own across-axis (the finger's line crossed
		// with the palm's normal: the palm is down, -Y, in the rest pose)
		var handB = new Basis(handTurn);
		var palmN = handB * Vector3.Down;
		for (int f = 0; f < _fingerBones.Count; f++)
		{
			var fb = _fingerBones[f];
			if (fb[0] < 0) continue;
			Vector3 prevRest = _rest[_bHand].Origin, prevNew = W;
			var cum = Basis.Identity;
			for (int j = 0; j < 3; j++)
			{
				int b = fb[j];
				Vector3 seg = handB * (_rest[b].Origin - prevRest);
				Vector3 at = prevNew + (j == 0 ? seg : cum * seg);
				// (the last joint's line: the one before it, carried on)
				Vector3 dir = handB * (j < 2 ? _rest[fb[j + 1]].Origin - _rest[b].Origin : _rest[b].Origin - _rest[fb[j - 1]].Origin);
				dir = (cum * dir).Normalized();
				var axis = dir.Cross(palmN).Normalized();
				float ang = Mathf.Max(0.15f + 0.85f * _clench, -0.2f) * (0.55f + 0.25f * j) * (f == 4 ? 0.6f : 1f);
				if (axis.LengthSquared() > 0.5f) cum = new Basis(axis, ang) * cum;
				g[b] = new Transform3D(cum * handB * _rest[b].Basis, at);
				prevRest = _rest[b].Origin;
				prevNew = at;
			}
		}
		// into the bones' local poses
		foreach (int b in AllPosed())
		{
			int parent = _skel.GetBoneParent(b);
			var local = parent >= 0 ? g[parent].AffineInverse() * g[b] : g[b];
			_skel.SetBonePosePosition(b, local.Origin);
			_skel.SetBonePoseRotation(b, local.Basis.Orthonormalized().GetRotationQuaternion());
		}
	}

	private IEnumerable<int> AllPosed()
	{
		yield return _bUpper;
		yield return _bFore;
		yield return _bHand;
		foreach (var fb in _fingerBones)
			foreach (int b in fb)
				if (b >= 0) yield return b;
	}

	/// <summary>The shortest turn from one direction onto another.</summary>
	private static Quaternion Arc(Vector3 from, Vector3 to)
	{
		float d = from.Dot(to);
		if (d > 0.9999f) return Quaternion.Identity;
		if (d < -0.9999f)
		{
			var ax = from.Cross(Vector3.Up);
			if (ax.LengthSquared() < 1e-4f) ax = from.Cross(Vector3.Right);
			return new Quaternion(ax.Normalized(), Mathf.Pi);
		}
		var c = from.Cross(to);
		return new Quaternion(c.X, c.Y, c.Z, 1f + d).Normalized();
	}

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
