using Godot;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// A grey squirrel about Act 1's woods (2026-10-15, the owner: "add squirrels that run around"). Life, not a subject:
/// it forages in short bursts round its home spot (a dash, a stop, a look about, sitting up), now and then runs to a
/// trunk and up it to cling a while, head down, then comes down again; and when the walker comes close it bolts for the
/// nearest trunk and goes up out of reach, scolding (if the chatter takes are there), coming down once they've gone on.
///
/// Built in code like the deer and the frog: a fur-shaded body, head, ears, a big bushy tail held up in an S, small legs.
/// About life size, a touch over (it has to read at a few metres in 640x360). No collision. The trunks it climbs are
/// found by ray: a vertical surface at knee and head height in the same place is a tree (not a rock or a sign).
/// </summary>
[GlobalClass]
public partial class Squirrel : Node3D
{
	[Export] public float RoamRadius = 9f;
	[Export] public float FleeDistance = 6.5f;
	[Export] public float ModelScale = 1.7f;   // (2026-10-23: was 1.35, too small to make out up a trunk)
	[Export] public int Seed = 1;

	private enum Mode { Forage, Dash, SitUp, ToTree, Climb, Cling, Descend, Flee }
	private Mode _mode = Mode.Forage;
	private float _timer, _phase, _speed;
	private Vector3 _home, _target, _trunkHit, _trunkNormal;
	private float _climbTop, _climbY;
	/// <summary>The trunk it's on as drawn (its centre and bark), when the forest has it; else the collider's face is used.</summary>
	private TreeTrunks.Trunk? _trunk;
	private bool _fleeing;
	private Node3D _model, _body, _head, _tail;
	private ForestTerrain _terrain;
	private Node3D _player;
	private readonly RandomNumberGenerator _rng = new();
	private Audio.RecentPicker _chatter;

	public override void _Ready()
	{
		_rng.Seed = (ulong)(Seed * 2654435761L + 17);
		_terrain = GroundSnap.FindTerrain(this);
		// (its TrailAnchor has set where it lives by now)
		_home = GlobalPosition;
		TopLevel = true;
		GlobalPosition = Ground(_home);
		if (!TryLoadRig()) BuildModel();   // (the Blender model; the code-built squirrel only if it's missing)
		// (2026-10-23, the owner: "we cant take pictures of them") on the album's list now: a small subject, so near, and
		// framed tight (its look points follow it up the trunk)
		// (on the model, so its look points turn with the body: up a trunk head down, they stay on its back and head)
		(_model ?? this).AddChild(new PhotoSubject
		{
			Name = "PhotoSubject", Id = "squirrel",
			LookPoints = new[] { new Vector3(0, 0.1f, 0), new Vector3(0, 0.1f, -0.12f) },
			MinDistance = 0.8f, MaxDistance = 18f, ConeDegrees = 12f, OwnerPath = "..",
		});
		_timer = _rng.RandfRange(0.5f, 3f);
		if (Systems.StoryManager.Instance is { } story && story.Current >= Systems.Checkpoint.Act2StairsClimbed) { Visible = false; SetProcess(false); }
	}

	private Vector3 Ground(Vector3 p) => _terrain == null ? p : new Vector3(p.X, _terrain.HeightAt(p.X, p.Z), p.Z);

	// ------------------------------------------------------------------ behaviour

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
		float toPlayer = _player == null ? 999f : Flat(_player.GlobalPosition - GlobalPosition).Length();
		if (toPlayer > 90f) return;   // (far off: it keeps its place, nothing to see)

		bool onGround = _mode is Mode.Forage or Mode.Dash or Mode.SitUp or Mode.ToTree or Mode.Flee;
		if (onGround && !_fleeing && toPlayer < FleeDistance) StartFlee();
		_timer -= dt;
		_phase += dt;

		switch (_mode)
		{
			case Mode.Forage:
				// nose down, nibbling; the tail flicks
				Pose(0f, -0.25f + 0.08f * Mathf.Sin(_phase * 11f), 0f);
				if (_timer <= 0f) Next();
				break;
			case Mode.SitUp:
				Pose(0f, 0.15f, 0.9f);
				if (_timer <= 0f) Next();
				break;
			case Mode.Dash:
			case Mode.ToTree:
			case Mode.Flee:
				Run(dt);
				break;
			case Mode.Climb:
				_climbY = Mathf.MoveToward(_climbY, _climbTop, dt * (_fleeing ? 2.6f : 1.7f));
				OnTrunk(_climbY, up: true, dt);
				if (Mathf.IsEqualApprox(_climbY, _climbTop)) { _mode = Mode.Cling; _timer = _fleeing ? 4f : _rng.RandfRange(2.5f, 6f); }
				break;
			case Mode.Cling:
				OnTrunk(_climbY, up: false, dt);
				// up there it waits for the walker to go on
				if (_fleeing && toPlayer < FleeDistance * 2f) _timer = Mathf.Max(_timer, 1.5f);
				if (_timer <= 0f) { _mode = Mode.Descend; _fleeing = false; }
				break;
			case Mode.Descend:
				_climbY = Mathf.MoveToward(_climbY, 0f, dt * 1.4f);
				OnTrunk(_climbY, up: false, dt);
				if (_climbY <= 0.001f)
				{
					GlobalPosition = Ground(_trunkHit + _trunkNormal * 0.25f);
					_mode = Mode.Forage; _timer = _rng.RandfRange(1f, 2.5f);
					Rotation = new Vector3(0, Yaw(_trunkNormal), 0);
					_model.Rotation = Vector3.Zero;
				}
				break;
		}
		if (_rigged) RigAdvance(dt);
	}

	/// <summary>For tests: sends it to the nearest trunk and up it now (false if there's none round it).</summary>
	public bool TestClimb()
	{
		if (!FindTrunk(GlobalPosition, 12f, out _trunkHit, out _trunkNormal)) return false;
		_fleeing = false; _mode = Mode.ToTree; _target = _trunkHit + _trunkNormal * 0.12f; _speed = 3.2f;
		return true;
	}
	/// <summary>For tests: up a trunk (climbing or clinging), and which way the bark faces it.</summary>
	public bool OnATrunk => _mode is Mode.Climb or Mode.Cling;
	public bool Clinging => _mode == Mode.Cling;
	public Vector3 TrunkNormal => _trunkNormal;

	private void Next()
	{
		float r = _rng.Randf();
		if (r < 0.18f && FindTrunk(GlobalPosition, 8f, out _trunkHit, out _trunkNormal))
		{
			_mode = Mode.ToTree; _target = _trunkHit + _trunkNormal * 0.12f; _speed = 3.2f;
			return;
		}
		if (r < 0.38f) { _mode = Mode.SitUp; _timer = _rng.RandfRange(0.8f, 2.2f); return; }
		// a dash somewhere else round its home: short, then stop
		for (int i = 0; i < 6; i++)
		{
			float a = _rng.RandfRange(0f, Mathf.Tau), d = _rng.RandfRange(1.5f, 4.5f);
			var t = GlobalPosition + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
			if (Flat(t - _home).Length() > RoamRadius) t = _home + (t - _home).Normalized() * RoamRadius * 0.7f;
			if (!Blocked(GlobalPosition, t)) { _target = Ground(t); break; }
		}
		_mode = Mode.Dash; _speed = _rng.RandfRange(2.4f, 3.6f);
	}

	private void StartFlee()
	{
		_fleeing = true;
		Chatter();
		// the nearest trunk away from them, else just away
		var away = Flat(GlobalPosition - _player.GlobalPosition).Normalized();
		if (FindTrunk(GlobalPosition + away * 2f, 9f, out _trunkHit, out _trunkNormal) && Flat(_trunkHit - _player.GlobalPosition).Length() > 3f)
		{ _mode = Mode.ToTree; _target = _trunkHit + _trunkNormal * 0.12f; }
		else { _mode = Mode.Flee; _target = Ground(GlobalPosition + away * 9f); }
		_speed = 5.2f;
	}

	/// <summary>Bounding along the ground to the target: hops, the body rocking, the tail streaming.</summary>
	private void Run(float dt)
	{
		var to = Flat(_target - GlobalPosition);
		float d = to.Length();
		if (d < 0.15f)
		{
			if (_mode == Mode.ToTree) { _mode = Mode.Climb; _climbY = 0f; _climbTop = _rng.RandfRange(_fleeing ? 2.2f : 1.2f, _fleeing ? 3.2f : 2.2f); return; }   // (2026-10-23: climbs lower, in sight and in reach of the camera; it went up to 5 m, a speck overhead)
			if (_mode == Mode.Flee) _fleeing = false;
			_mode = _rng.Randf() < 0.5f ? Mode.SitUp : Mode.Forage;
			_timer = _rng.RandfRange(0.7f, 2.5f);
			return;
		}
		var step = to / d * Mathf.Min(d, _speed * dt);
		var p = Ground(GlobalPosition + step);
		GlobalPosition = p;
		float yaw = Yaw(to);
		Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, yaw, 1f - Mathf.Exp(-18f * dt)), 0);
		if (_rigged)
		{
			// the clips: hop for a dash, run when it's fleeing; played at the pace it goes
			_model.Position = Vector3.Zero;
			_model.Basis = Basis.Identity.Scaled(Vector3.One * ModelScale);
			bool fast = _fleeing || _speed > 4f;
			Clip(fast ? "run" : "hop", 0.12f, _speed / (fast ? 5.2f : 3.0f));
			return;
		}
		float gait = _phase * _speed * 2.6f;
		float hop = Mathf.Max(0f, Mathf.Sin(gait)) * 0.06f * ModelScale;
		_model.Position = new Vector3(0, hop, 0);
		_model.Rotation = Vector3.Zero;
		_body.Rotation = new Vector3(Mathf.Sin(gait) * 0.25f, 0, 0);
		// (2026-10-15) the bound: stretched long as it reaches, gathered short as the hind feet come up past the fore; the
		// forearms reaching ahead to land; the plume rippling behind in an S, its tip a beat after its base
		_body.Scale = new Vector3(1f, 1f, 1f + 0.12f * Mathf.Sin(gait + 0.4f));
		_arms.Rotation = new Vector3(0.7f * Mathf.Sin(gait + 0.9f), 0, 0);
		_tail.Rotation = new Vector3(-0.5f + Mathf.Sin(gait + 1f) * 0.15f, 0, 0);
		_tailTip.Rotation = new Vector3(Mathf.Sin(gait + 2.4f) * 0.35f, 0, 0);
		_head.Rotation = new Vector3(0.1f + 0.08f * Mathf.Sin(gait + 1.5f), 0, 0);
	}

	/// <summary>On the trunk at <paramref name="y"/> above the foot: head up climbing, head down clinging and coming down.</summary>
	private void OnTrunk(float y, bool up, float dt)
	{
		var foot = Ground(_trunkHit);
		if (_trunk is { } t)
		{
			// (2026-10-23) on the bark as drawn at this height: out from the trunk's axis by its radius there
			var c = new Vector3(t.Centre.X, foot.Y, t.Centre.Y);
			GlobalPosition = c + new Vector3(0, y + 0.05f, 0) + _trunkNormal * (TreeTrunks.RadiusAt(t, y + 0.05f) + 0.035f);
		}
		else
			// (trunks thin a little going up: it keeps to the bark)
			GlobalPosition = foot + new Vector3(0, y + 0.05f, 0) + _trunkNormal * (0.06f - 0.012f * y);
		Rotation = new Vector3(0, Yaw(-_trunkNormal), 0);
		_model.Position = Vector3.Zero;
		// belly to the bark either way: head up climbing, turned end over end (about the trunk's normal) head down
		var climb = new Basis(Vector3.Right, Mathf.Pi * 0.5f);
		_model.Basis = (up ? climb : new Basis(Vector3.Back, Mathf.Pi) * climb).Scaled(Vector3.One * ModelScale);
		if (_rigged)
		{
			// scurrying up (or down), still while it clings
			Clip("climb", 0.15f, _mode == Mode.Cling ? 0f : (up ? 1.6f : 1.0f));
			return;
		}
		float scurry = up ? Mathf.Sin(_phase * 18f) * 0.08f : 0f;
		_body.Rotation = new Vector3(scurry, 0, 0);
		_head.Rotation = new Vector3(up ? 0f : -0.3f + 0.2f * Mathf.Sin(_phase * 2.3f), up ? 0f : 0.5f * Mathf.Sin(_phase * 0.9f), 0);
		_tail.Rotation = new Vector3(-0.2f + 0.1f * Mathf.Sin(_phase * 4f), 0, 0);
		_body.Scale = Vector3.One;
		// the forearms reaching up the bark as it climbs, splayed to hold on head down; the plume flat to the trunk
		_arms.Rotation = new Vector3(up ? 0.9f + 0.5f * Mathf.Sin(_phase * 18f) : 0.5f, 0, 0);
		_tailTip.Rotation = new Vector3(up ? 0.1f : -0.2f + 0.15f * Mathf.Sin(_phase * 3.1f), 0, 0);
	}

	/// <summary>For the creature preview: hold a pose at <paramref name="phase"/> seconds into it (0 foraging, 1 sat up,
	/// 2 mid-bound, 3 clinging head down), without moving it.</summary>
	public void PreviewPose(int which, float phase)
	{
		SetProcess(false);
		_phase = phase;
		if (_rigged)
		{
			string clip = which switch { 0 => "forage", 1 => "sit_up", 2 => "hop", 4 => "run", _ => "climb" };
			Clip(clip, 0f);
			_anim.Seek(phase % (float)_anim.GetAnimation(clip).Length, true);
			return;
		}
		switch (which)
		{
			case 0: Pose(0f, -0.25f, 0f); break;
			case 1: Pose(0f, 0.15f, 0.9f); break;
			case 2:
			{
				float gait = phase * 3.2f * 2.6f;
				_model.Position = new Vector3(0, Mathf.Max(0f, Mathf.Sin(gait)) * 0.06f * ModelScale, 0);
				_model.Rotation = Vector3.Zero;
				_body.Rotation = new Vector3(Mathf.Sin(gait) * 0.25f, 0, 0);
				_body.Scale = new Vector3(1f, 1f, 1f + 0.12f * Mathf.Sin(gait + 0.4f));
				_arms.Rotation = new Vector3(0.7f * Mathf.Sin(gait + 0.9f), 0, 0);
				_tail.Rotation = new Vector3(-0.5f + Mathf.Sin(gait + 1f) * 0.15f, 0, 0);
				_tailTip.Rotation = new Vector3(Mathf.Sin(gait + 2.4f) * 0.35f, 0, 0);
				_head.Rotation = new Vector3(0.1f, 0, 0);
				break;
			}
		}
	}

	private void Pose(float bodyPitch, float headPitch, float sitUp)
	{
		if (_rigged)
		{
			_model.Position = Vector3.Zero;
			_model.Basis = Basis.Identity.Scaled(Vector3.One * ModelScale);
			Clip(sitUp > 0.5f ? "sit_up" : "forage", 0.3f);
			return;
		}
		// sitting up: reared back on the haunches (the rump kept out of the ground)
		_model.Position = new Vector3(0, 0.05f * sitUp * ModelScale, 0);
		_model.Rotation = new Vector3(0.9f * sitUp, 0, 0);
		_body.Rotation = new Vector3(bodyPitch, 0, 0);
		// quick looks about, the way they do
		float look = Mathf.Sin(_phase * 1.7f + Seed) > 0.6f ? 0.6f * Mathf.Sign(Mathf.Sin(_phase * 0.7f + Seed)) : 0f;
		_head.Rotation = new Vector3(headPitch - 0.6f * sitUp, look, 0);   // (reared up, the head tips back down to look ahead)
		float flick = Mathf.Max(0f, Mathf.Sin(_phase * 3.1f + Seed * 2f) - 0.85f) * 3f;
		_tail.Rotation = new Vector3(-0.7f + flick * 0.4f, 0, 0);
		// the flick runs up the plume: the tip snaps after the base
		float tipFlick = Mathf.Max(0f, Mathf.Sin(_phase * 3.1f + Seed * 2f - 0.5f) - 0.85f) * 3f;
		_tailTip.Rotation = new Vector3(0.15f - tipFlick * 0.6f, 0, 0);
		_body.Scale = Vector3.One;
		// sitting up: the paws drawn up to the chest, nibbling at something held; foraging: the paws busy at the ground
		float nibble = Mathf.Sin(_phase * 13f) * 0.12f;
		_arms.Rotation = new Vector3(sitUp > 0.5f ? 1.15f * sitUp + nibble : 0.15f + 0.25f * Mathf.Max(0f, Mathf.Sin(_phase * 7f)), 0, 0);
	}

	// ------------------------------------------------------------------ the world

	/// <summary>The nearest tree trunk round <paramref name="from"/>: a vertical surface hit at knee and head height in
	/// the same place.</summary>
	private bool FindTrunk(Vector3 from, float range, out Vector3 hit, out Vector3 normal)
	{
		hit = normal = Vector3.Zero;
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return false;
		float best = float.MaxValue;
		var origin = Ground(from);
		for (int i = 0; i < 16; i++)
		{
			float a = Mathf.Tau * i / 16f + _rng.RandfRange(-0.1f, 0.1f);
			var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			var lo = Ray(space, origin + Vector3.Up * 0.4f, dir, range);
			if (lo == null) continue;
			var (p, n) = lo.Value;
			if (Mathf.Abs(n.Y) > 0.3f) continue;
			// (2026-10-23, the owner: squirrels "floating on the trees") a trunk is round: two rays a hand apart either side meet
			// its bark at different angles; a flat face (Act 1's invisible walls, a sign, a rock face) at the same one
			var sideV = new Vector3(-dir.Z, 0, dir.X) * 0.12f;
			var l = Ray(space, origin + Vector3.Up * 0.4f + sideV, dir, range);
			var r = Ray(space, origin + Vector3.Up * 0.4f - sideV, dir, range);
			if (l == null || r == null || Flat(l.Value.n).Normalized().AngleTo(Flat(r.Value.n).Normalized()) < Mathf.DegToRad(6f)) continue;
			if (Flat(l.Value.p - p).Length() > 0.4f || Flat(r.Value.p - p).Length() > 0.4f) continue;
			var hi = Ray(space, origin + Vector3.Up * 2.2f, dir, range);
			if (hi == null || Mathf.Abs(Flat(hi.Value.p - p).Length()) > 0.45f) continue;
			float d = Flat(p - origin).Length();
			if (d < best) { best = d; hit = new Vector3(p.X, 0, p.Z); normal = Flat(n).Normalized(); }
		}
		if (best == float.MaxValue) return false;
		hit = Ground(hit);
		// the bark as drawn: its centre, and the side of it toward where it came from
		_trunk = TreeTrunks.Nearest(this, new Vector2(hit.X, hit.Z), 0.7f);
		if (_trunk is { } t)
		{
			var c = new Vector3(t.Centre.X, hit.Y, t.Centre.Y);
			var outward = Flat(origin - c);
			normal = outward.LengthSquared() > 1e-4f ? outward.Normalized() : normal;
			hit = Ground(c + normal * TreeTrunks.RadiusAt(t, 0.3f));
		}
		return true;
	}

	private static (Vector3 p, Vector3 n)? Ray(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 dir, float range)
	{
		var q = PhysicsRayQueryParameters3D.Create(from, from + dir * range, 1u);
		var r = space.IntersectRay(q);
		if (r.Count == 0) return null;
		return ((Vector3)r["position"], (Vector3)r["normal"]);
	}

	private bool Blocked(Vector3 a, Vector3 b)
	{
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return false;
		var q = PhysicsRayQueryParameters3D.Create(Ground(a) + Vector3.Up * 0.15f, Ground(b) + Vector3.Up * 0.15f, 1u);
		return space.IntersectRay(q).Count > 0;
	}

	private void Chatter()
	{
		// (the scolding takes come from the trail's audio pass; without them it runs silent)
		int n = 0;
		while (n < 8 && ResourceLoader.Exists($"res://assets/audio/sfx/squirrel_{n + 1:00}.wav")) n++;
		if (n == 0) return;
		var p = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>($"res://assets/audio/sfx/squirrel_{_chatter.Next(_rng, n) + 1:00}.wav"),
			Bus = "Birds", UnitSize = 4f, MaxDistance = 40f, VolumeDb = -10f, PitchScale = _rng.RandfRange(0.95f, 1.08f),
		};
		AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
	private static float Yaw(Vector3 dir) => Mathf.Atan2(-dir.X, -dir.Z);

	// ------------------------------------------------------------------ the model

	private static StandardMaterial3D _fur;

	/// <summary>For the preview: the triangles in the model.</summary>
	public int TriangleCount { get; private set; }
	private Node3D _arms, _tailTip;

	private static float SS(float a, float b, float x) => Mathf.SmoothStep(a, b, x);
	private static int Tris(MeshInstance3D mi) => mi?.Mesh == null ? 0 : mi.Mesh.GetFaces().Length / 3;

	/// <summary>(2026-10-15, the owner: "the ... squirrels could use some graphic updates") A grey squirrel, not a few blobs:
	/// a pear of a body (the narrow chest, the big haunches), long hind feet, forearms with paws on their own pivot (held to
	/// the chest when it sits up), a round head with big dark eyes (a glint in each), a pale eye ring, rounded ears, a short
	/// snout; the tail a two-jointed plume, flattened like a real one, a pale fringe of loose hair down both edges, so it
	/// can ripple in an S. The coat painted per vertex: grey on the back, rusty along the flanks and feet, the belly pale.</summary>
	private void BuildModel()
	{
		_fur ??= MakeFur();
		Color grey = new Color(0.46f, 0.43f, 0.4f).SrgbToLinear(), rust = new Color(0.5f, 0.36f, 0.24f).SrgbToLinear();
		Color belly = new Color(0.84f, 0.8f, 0.72f).SrgbToLinear(), dark = new Color(0.05f, 0.045f, 0.04f).SrgbToLinear();
		Color white = new Color(0.95f, 0.95f, 0.92f).SrgbToLinear();
		// some greyer, some browner
		float warm = _rng.RandfRange(0.1f, 0.5f);
		Color coat = grey.Lerp(rust, warm), side = grey.Lerp(rust, Mathf.Min(1f, warm + 0.35f));
		int tris = 0;

		// the coat per vertex (eyes, nose and glints keep their own colour)
		void Coat(MeshKit k, bool tail = false) => k.Shade((Vector3 p, Vector3 n, ref Color c, ref Vector2 uv2) =>
		{
			if (c.R < 0.02f || c.R > 0.5f) return;   // (the eyes, the nose, the glints, the pale eye rings: as drawn)
			float d = 0.5f + 0.5f * Mathf.Sin(p.X * 140f + p.Z * 97f) * Mathf.Sin(p.Y * 123f - p.Z * 61f);
			if (tail) { c *= 0.9f + 0.16f * d; c.A = 1f; return; }   // (the plume and its pale fringe keep their colours)
			Color col = coat.Lerp(side, SS(0.2f, 0.8f, Mathf.Abs(n.X)) * (1f - SS(0.3f, 0.8f, n.Y)));
			col = col.Lerp(belly, SS(-0.2f, -0.65f, n.Y));
			c = col * (0.9f + 0.16f * d);
			c.A = 1f;
		});

		_model = new Node3D { Name = "Model", Scale = Vector3.One * ModelScale };
		AddChild(_model);
		// the body pivots at the hips (its back end), so the bound rocks it
		_body = new Node3D { Name = "Body", Position = new Vector3(0, 0.07f, 0.06f) };
		_model.AddChild(_body);
		var k = new MeshKit();
		k.Mat(_fur);
		k.Color = coat;
		// the pear: the chest narrow and low, the haunch round and high
		Loft.Tube(k, new[] { new Vector3(0, 0.0f, -0.165f), new Vector3(0, 0.005f, -0.12f), new Vector3(0, 0.012f, -0.06f), new Vector3(0, 0.015f, 0.0f), new Vector3(0, 0.01f, 0.045f) },
			new[] { new Vector2(0.024f, 0.028f), new Vector2(0.034f, 0.038f), new Vector2(0.042f, 0.044f), new Vector2(0.05f, 0.05f), new Vector2(0.03f, 0.032f) }, 12, 3);
		foreach (float s in new[] { -1f, 1f })
		{
			// the haunch, and the long hind foot flat on the ground (heel to toes)
			k.Blob(new Vector3(0.03f * s, -0.005f, -0.005f), new Vector3(0.026f, 0.042f, 0.048f), Seed * 5 + (int)s, 0.03f, false, 1f, 0f, 2);
			// (round-ended tubes through a rounded heel, not faceted cylinders meeting at a crease)
			Loft.Tube(k, new[] { new Vector3(0.034f * s, -0.04f, 0.012f), new Vector3(0.034f * s, -0.062f, 0.006f), new Vector3(0.033f * s, -0.07f, -0.02f), new Vector3(0.03f * s, -0.071f, -0.06f) },
				new[] { new Vector2(0.012f, 0.012f), new Vector2(0.01f, 0.01f), new Vector2(0.008f, 0.006f), new Vector2(0.006f, 0.004f) }, 10, 4);
		}
		Coat(k);
		tris += Tris(k.CommitTo(_body, "Mesh"));

		// the forearms and paws on their own pivot at the chest: down to the ground running, held to the chest sitting up
		_arms = new Node3D { Name = "Arms", Position = new Vector3(0, -0.005f, -0.14f) };
		_body.AddChild(_arms);
		var ak = new MeshKit();
		ak.Mat(_fur);
		ak.Color = coat;
		foreach (float s in new[] { -1f, 1f })
		{
			// one smooth limb through a rounded elbow (round-ended, enough sides to read round)
			Loft.Tube(ak, new[] { new Vector3(0.018f * s, 0.006f, 0.002f), new Vector3(0.02f * s, -0.035f, -0.012f), new Vector3(0.021f * s, -0.062f, -0.02f) },
				new[] { new Vector2(0.011f, 0.011f), new Vector2(0.008f, 0.009f), new Vector2(0.006f, 0.006f) }, 10, 4);
			ak.Color = dark * 4f;
			ak.Blob(new Vector3(0.021f * s, -0.064f, -0.026f), new Vector3(0.008f, 0.005f, 0.01f), Seed * 13 + (int)s, 0.02f, false, 1f, 0f, 1);   // the paw
			ak.Color = coat;
		}
		Coat(ak);
		tris += Tris(ak.CommitTo(_arms, "Mesh"));

		_head = new Node3D { Name = "Head", Position = new Vector3(0, 0.03f, -0.17f) };
		_body.AddChild(_head);
		var hk = new MeshKit();
		hk.Mat(_fur);
		hk.Color = coat;
		// a round skull narrowing to a short blunt snout
		Loft.Tube(hk, new[] { new Vector3(0, 0.0f, 0.015f), new Vector3(0, 0.004f, -0.015f), new Vector3(0, 0.0f, -0.045f), new Vector3(0, -0.006f, -0.068f) },
			new[] { new Vector2(0.028f, 0.03f), new Vector2(0.036f, 0.034f), new Vector2(0.026f, 0.024f), new Vector2(0.012f, 0.011f) }, 12, 3);
		foreach (float s in new[] { -1f, 1f })
		{
			// big dark eyes set high on the sides, a pale ring round each, a glint
			hk.Color = belly;
			hk.Blob(new Vector3(0.026f * s, 0.009f, -0.028f), new Vector3(0.006f, 0.012f, 0.013f), Seed * 7 + 20 + (int)s, 0.0f, false, 1f, 0f, 1);
			hk.Color = dark;
			hk.Blob(new Vector3(0.029f * s, 0.009f, -0.028f), new Vector3(0.006f, 0.01f, 0.011f), Seed * 7 + 4 + (int)s, 0.0f, false, 1f, 0f, 1);
			hk.Color = white;
			hk.Blob(new Vector3(0.0335f * s, 0.013f, -0.031f), new Vector3(0.0018f, 0.0022f, 0.0022f), Seed * 7 + 30 + (int)s, 0.0f, false, 1f, 0f, 1);
			// rounded ears up off the back of the skull, a little tuft at each tip
			hk.Color = coat;
			hk.Blob(new Vector3(0.018f * s, 0.036f, 0.002f), new Vector3(0.012f, 0.019f, 0.006f), Seed * 7 + 40 + (int)s, 0.0f, false, 1f, 0f, 2);
			Loft.Tube(hk, new[] { new Vector3(0.017f * s, 0.044f, 0.0f), new Vector3(0.018f * s, 0.055f, 0.002f) }, new[] { new Vector2(0.004f, 0.003f), new Vector2(0.0015f, 0.001f) }, 6, 2);
		}
		hk.Color = coat;
		hk.Blob(new Vector3(0, 0.002f, 0.008f), new Vector3(0.029f, 0.031f, 0.024f), Seed * 7 + 11, 0.0f, false, 1f, 0f, 2);   // the round back of the skull
		hk.Color = dark;
		hk.Blob(new Vector3(0, -0.004f, -0.071f), new Vector3(0.006f, 0.005f, 0.004f), Seed * 7 + 9, 0f, false, 1f, 0f, 1);   // the nose
		Coat(hk);
		tris += Tris(hk.CommitTo(_head, "Mesh"));

		// the tail in two pieces (the base, and the plume above it on its own joint) so it can ripple in an S: flattened
		// like a real one (wider than deep), its fringe of loose pale hair down both edges
		_tail = new Node3D { Name = "Tail", Position = new Vector3(0, 0.02f, 0.03f) };
		_body.AddChild(_tail);
		var tk = new MeshKit();
		tk.Mat(_fur);
		tk.Color = coat;
		Vector3[] lowSpine = { new(0, 0.0f, 0.0f), new(0, 0.05f, 0.045f), new(0, 0.12f, 0.06f) };
		Loft.Tube(tk, lowSpine, new[] { new Vector2(0.016f, 0.02f), new Vector2(0.03f, 0.042f), new Vector2(0.034f, 0.054f) }, 12, 3);
		Fringe(tk, lowSpine, 0.048f, belly, coat, Seed * 17);
		Coat(tk, tail: true);
		tris += Tris(tk.CommitTo(_tail, "Mesh"));
		_tailTip = new Node3D { Name = "TailTip", Position = new Vector3(0, 0.12f, 0.06f) };
		_tail.AddChild(_tailTip);
		tk = new MeshKit();
		tk.Mat(_fur);
		tk.Color = coat;
		Vector3[] topSpine = { new(0, 0.0f, 0.0f), new(0, 0.07f, -0.01f), new(0, 0.13f, -0.05f), new(0, 0.16f, -0.1f) };
		tk.Blob(Vector3.Zero, new Vector3(0.033f, 0.04f, 0.052f), Seed * 23, 0.0f, false, 1f, 0f, 2);   // (the joint: rounded, so bending it shows no seam)
		Loft.Tube(tk, topSpine, new[] { new Vector2(0.034f, 0.054f), new Vector2(0.036f, 0.06f), new Vector2(0.03f, 0.05f), new Vector2(0.012f, 0.018f) }, 12, 3);
		Fringe(tk, topSpine, 0.054f, belly, coat, Seed * 19);
		tk.Color = coat.Lerp(belly, 0.25f);
		tk.Blob(topSpine[^1] + new Vector3(0, 0.005f, -0.006f), new Vector3(0.02f, 0.026f, 0.028f), Seed * 29, 0.08f, false, 1f, 0f, 2);   // (the plume's tip: rounded, a little paler)
		Coat(tk, tail: true);
		tris += Tris(tk.CommitTo(_tailTip, "Mesh"));
		TriangleCount = tris;
	}

	/// <summary>Loose hair down both edges of the tail's plume: small pale tufts just outside its edge.</summary>
	private void Fringe(MeshKit k, Vector3[] spine, float width, Color pale, Color coat, int seed)
	{
		// (along the plume's broad edges, front and back, not round its thin sides: seen from the side the tail is its full
		// width, ragged at both edges; the tufts of mixed size and only a little paler, so it reads as loose hair, not beads)
		for (int i = 0; i < spine.Length - 1; i++)
		{
			var along = (spine[i + 1] - spine[i]).Normalized();
			var broad = Vector3.Right.Cross(along).Normalized();
			for (int j = 0; j < 4; j++)
			{
				float t = (j + _rng.RandfRange(0.1f, 0.9f)) / 4f;
				var p = spine[i].Lerp(spine[i + 1], t);
				foreach (float s in new[] { -1f, 1f })
				{
					k.Color = coat.Lerp(pale, _rng.RandfRange(0.15f, 0.4f));
					float r = _rng.RandfRange(0.011f, 0.02f);
					var at = p + broad * s * width * _rng.RandfRange(0.72f, 0.9f) + new Vector3(_rng.RandfRange(-0.01f, 0.01f), 0, 0);
					k.Blob(at, new Vector3(r * 1.1f, r * 1.4f, r), seed + i * 8 + j * 2 + (s > 0 ? 1 : 0), 0.05f, false, 1f, 0f, 1);
				}
			}
		}
	}

	private static StandardMaterial3D MakeFur()
	{
		var m = (StandardMaterial3D)PropTextures.FurMat.Duplicate();
		m.VertexColorUseAsAlbedo = true;
		m.Uv1Triplanar = true;
		m.Uv1WorldTriplanar = false;
		m.Uv1Scale = Vector3.One * 6f;
		m.Uv1TriplanarSharpness = 2f;
		return m;
	}
}
