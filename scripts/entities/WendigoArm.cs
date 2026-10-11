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

	/// <summary>(2026-10-10c, the owner: "shrink the size of the hand by 15%? It is just way too large") The whole arm
	/// drawn at this size; its bones' lengths with it, and its shoulder nearer the hole so it still reaches across.</summary>
	public const float Size = 0.85f;
	private const float L1 = 0.78f * Size, L2 = 0.74f * Size;   // (the model's: tools/Blender/wendigo.py, ARM_L1, ARM_L2)
	private static readonly Vector3 Shoulder = new(-0.72f, 0.05f, 0f);
	// (the search: what it's doing now, and until when; the hand's motion a damped spring toward its aim, never a snap)
	private enum Hunt { Feel, Creep, Listen, Snatch, Recover }
	private Hunt _hunt = Hunt.Feel;
	private double _huntUntil;
	private Vector3 _vel, _creepFrom;
	private float _spread;   // how far its fingers are working, feeling (0 still .. 1 each one moving on its own)
	private double _patAt;
	private float _pat;
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
		model.Scale = Vector3.One * Size;
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
		_cUpper = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.12f * Size, Height = L1 + 0.24f * Size }, Disabled = true };
		_cFore = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.1f * Size, Height = L2 + 0.2f * Size }, Disabled = true };
		_cHand = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.15f * Size }, Disabled = true };
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
		// (2026-10-10c, the owner: "its animation needs to be smoother and more like it is reaching to try and find you
		// instead of flailing. Make it feel like it has purpose") It hunts. Nothing near: it feels its way along the
		// cavity, slow, the open hand patting the far boards and the ceiling, its fingers working. Something near: it turns
		// its search that way and creeps toward them; stops dead to listen, the fingers curling one after another; then the
		// reach, deliberate, and at its end the hand snatches shut; it draws back a little, and starts feeling again from
		// there. Every move is a damped spring toward its aim: it slows into each stop, never jerks.
		var prey = Distracted ? null : Prey?.Invoke();
		bool near = prey is { } head && head.DistanceTo(GlobalPosition) < 2.8f;
		Vector3 Clamp(Vector3 v) => new(Mathf.Clamp(v.X, 0.3f, Across - 0.38f), Mathf.Max(v.Y, Floor), Mathf.Clamp(v.Z, -0.5f, 0.5f));
		Vector3 toward = Distracted ? Clamp(ToLocal(_distractAt)) : near ? Clamp(ToLocal(prey.Value)) : Vector3.Zero;
		bool hunting = near || Distracted;
		if (Phase == State.Reach && _clock >= _huntUntil)
		{
			switch (_hunt)
			{
				case Hunt.Feel or Hunt.Recover:
					if (hunting) { _hunt = Hunt.Creep; _creepFrom = _target; _huntUntil = _clock + _rng.RandfRange(0.7f, 1.3f); }
					else { _hunt = Hunt.Feel; _huntUntil = _clock + 1.0; }
					break;
				case Hunt.Creep:
					_hunt = hunting ? Hunt.Listen : Hunt.Feel;
					_huntUntil = _clock + (hunting ? _rng.RandfRange(0.45f, 0.85f) : 1.0f);
					break;
				case Hunt.Listen:
					if (near)
					{
						_hunt = Hunt.Snatch;
						_huntUntil = _clock + 0.32;
						if (_rng.Randf() < 0.6f) AudioDirector.OneShot(this, "claw_scrape", 2, GlobalPosition + GlobalBasis.X * 0.6f, -4f, "Events", 3f, 0.12f);
					}
					else { _hunt = hunting ? Hunt.Creep : Hunt.Feel; _huntUntil = _clock + 0.8; }
					break;
				case Hunt.Snatch:
					_hunt = Hunt.Recover;
					_huntUntil = _clock + _rng.RandfRange(0.7f, 1.0f);
					break;
			}
		}
		// where it means its hand to be, and how quickly it gets there (the spring's speed)
		Vector3 aim;
		float omega;
		float wantClench;
		float tc = (float)_clock;
		switch (_hunt)
		{
			case Hunt.Creep:
			{
				// toward them, a little at a time, its search swinging either side of the line as it comes
				float u = Mathf.Clamp((float)(1.0 - (_huntUntil - _clock) / 1.0), 0f, 1f);
				aim = _creepFrom.Lerp(toward with { X = toward.X * 0.85f }, 0.35f + 0.45f * u) + new Vector3(0, 0.05f * Mathf.Sin(tc * 2.1f), 0.12f * Mathf.Sin(tc * 1.7f));
				omega = 4f; wantClench = 0.15f; _spread = Mathf.MoveToward(_spread, 0.6f, dt * 2f);
				break;
			}
			case Hunt.Listen:
				// still: the hand held where it stopped, the fingers spread and curling slowly one after another
				aim = _target; omega = 7f; wantClench = 0.05f + 0.2f * Mathf.Clamp((float)(1.0 - (_huntUntil - _clock) / 0.8), 0f, 1f);
				_spread = Mathf.MoveToward(_spread, 1f, dt * 2f);
				break;
			case Hunt.Snatch:
				// the reach: straight to them, the hand open until the last, then shut
				aim = toward == Vector3.Zero ? _target : toward; omega = 16f;
				wantClench = _huntUntil - _clock < 0.1 ? 1f : -0.3f;
				_spread = 0f;
				break;
			case Hunt.Recover:
				// drawn back a third of the way to the wall, closed, then opening as it starts to feel again
				aim = (_target with { X = Mathf.Max(0.3f, _target.X * 0.66f) });
				omega = 4.5f; wantClench = Mathf.Lerp(1f, 0.25f, Mathf.Clamp((float)(1.0 - (_huntUntil - _clock) / 0.9), 0f, 1f));
				_spread = Mathf.MoveToward(_spread, 0.4f, dt);
				break;
			default:
			{
				// feeling its way: a slow sweep over the cavity, and now and then a pat of the open hand against the far boards
				aim = new Vector3(Across * (0.48f + 0.14f * Mathf.Sin(tc * 0.37f)), 0.12f + 0.22f * Mathf.Sin(tc * 0.53f + 1f), 0.42f * Mathf.Sin(tc * 0.29f));
				if (_clock >= _patAt) { _patAt = _clock + _rng.RandfRange(1.1f, 2.2f); _pat = 1f; }
				_pat = Mathf.MoveToward(_pat, 0f, dt * 3.5f);
				aim.X += 0.14f * Mathf.Sin(_pat * Mathf.Pi);
				omega = 3f; wantClench = 0.18f + 0.12f * Mathf.Sin(_pat * Mathf.Pi);
				_spread = Mathf.MoveToward(_spread, 0.7f, dt);
				break;
			}
		}
		aim = Clamp(aim);
		// the spring (critically damped): accelerates away, slows into its stop
		var acc = (aim - _target) * (omega * omega) - _vel * (2f * omega);
		_vel += acc * dt;
		_target += _vel * dt;
		// a faint, slow tremble (it's cold, and it's starving): never a shake
		_target += new Vector3(Mathf.Sin(tc * 5.1f), Mathf.Sin(tc * 4.3f + 1.3f), Mathf.Sin(tc * 3.7f + 2.1f)) * 0.004f;
		_target.Y = Mathf.Max(_target.Y, Floor);
		_clench = Mathf.MoveToward(_clench, wantClench, dt * (_hunt == Hunt.Snatch ? 12f : 2.5f));
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
				float work = _spread * 0.22f * Mathf.Sin((float)_clock * 1.9f + f * 1.1f + j * 0.4f);   // (each finger feeling on its own)
				float ang = Mathf.Max(0.15f + 0.85f * _clench + work, -0.2f) * (0.55f + 0.25f * j) * (f == 4 ? 0.6f : 1f);
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
