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
	[Export] public float ModelScale = 1.35f;
	[Export] public int Seed = 1;

	private enum Mode { Forage, Dash, SitUp, ToTree, Climb, Cling, Descend, Flee }
	private Mode _mode = Mode.Forage;
	private float _timer, _phase, _speed;
	private Vector3 _home, _target, _trunkHit, _trunkNormal;
	private float _climbTop, _climbY;
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
		BuildModel();
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
	}

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
			if (_mode == Mode.ToTree) { _mode = Mode.Climb; _climbY = 0f; _climbTop = _rng.RandfRange(_fleeing ? 3.2f : 1.4f, _fleeing ? 5f : 3f); return; }
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
		float gait = _phase * _speed * 2.6f;
		float hop = Mathf.Max(0f, Mathf.Sin(gait)) * 0.06f * ModelScale;
		_model.Position = new Vector3(0, hop, 0);
		_model.Rotation = Vector3.Zero;
		_body.Rotation = new Vector3(Mathf.Sin(gait) * 0.25f, 0, 0);
		_tail.Rotation = new Vector3(-0.5f + Mathf.Sin(gait + 1f) * 0.15f, 0, 0);
		_head.Rotation = new Vector3(0.1f, 0, 0);
	}

	/// <summary>On the trunk at <paramref name="y"/> above the foot: head up climbing, head down clinging and coming down.</summary>
	private void OnTrunk(float y, bool up, float dt)
	{
		var foot = Ground(_trunkHit);
		// (trunks thin a little going up: it keeps to the bark)
		GlobalPosition = foot + new Vector3(0, y + 0.05f, 0) + _trunkNormal * (0.06f - 0.012f * y);
		Rotation = new Vector3(0, Yaw(-_trunkNormal), 0);
		_model.Position = Vector3.Zero;
		// belly to the bark either way: head up climbing, turned end over end (about the trunk's normal) head down
		var climb = new Basis(Vector3.Right, Mathf.Pi * 0.5f);
		_model.Basis = (up ? climb : new Basis(Vector3.Back, Mathf.Pi) * climb).Scaled(Vector3.One * ModelScale);
		float scurry = up ? Mathf.Sin(_phase * 18f) * 0.08f : 0f;
		_body.Rotation = new Vector3(scurry, 0, 0);
		_head.Rotation = new Vector3(up ? 0f : -0.3f + 0.2f * Mathf.Sin(_phase * 2.3f), up ? 0f : 0.5f * Mathf.Sin(_phase * 0.9f), 0);
		_tail.Rotation = new Vector3(-0.2f + 0.1f * Mathf.Sin(_phase * 4f), 0, 0);
	}

	private void Pose(float bodyPitch, float headPitch, float sitUp)
	{
		// sitting up: reared back on the haunches (the rump kept out of the ground)
		_model.Position = new Vector3(0, 0.05f * sitUp * ModelScale, 0);
		_model.Rotation = new Vector3(0.9f * sitUp, 0, 0);
		_body.Rotation = new Vector3(bodyPitch, 0, 0);
		// quick looks about, the way they do
		float look = Mathf.Sin(_phase * 1.7f + Seed) > 0.6f ? 0.6f * Mathf.Sign(Mathf.Sin(_phase * 0.7f + Seed)) : 0f;
		_head.Rotation = new Vector3(headPitch - 0.6f * sitUp, look, 0);   // (reared up, the head tips back down to look ahead)
		float flick = Mathf.Max(0f, Mathf.Sin(_phase * 3.1f + Seed * 2f) - 0.85f) * 3f;
		_tail.Rotation = new Vector3(-0.7f + flick * 0.4f, 0, 0);
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
			var hi = Ray(space, origin + Vector3.Up * 2.2f, dir, range);
			if (hi == null || Mathf.Abs(Flat(hi.Value.p - p).Length()) > 0.45f) continue;
			float d = Flat(p - origin).Length();
			if (d < best) { best = d; hit = new Vector3(p.X, 0, p.Z); normal = Flat(n).Normalized(); }
		}
		if (best == float.MaxValue) return false;
		hit = Ground(hit);
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

	private void BuildModel()
	{
		_fur ??= MakeFur();
		Color grey = new Color(0.46f, 0.43f, 0.4f).SrgbToLinear(), rust = new Color(0.5f, 0.36f, 0.24f).SrgbToLinear();
		Color belly = new Color(0.82f, 0.78f, 0.7f).SrgbToLinear(), dark = new Color(0.08f, 0.07f, 0.06f).SrgbToLinear();
		// some greyer, some browner
		Color coat = grey.Lerp(rust, _rng.RandfRange(0.1f, 0.6f));

		_model = new Node3D { Name = "Model", Scale = Vector3.One * ModelScale };
		AddChild(_model);
		// the body pivots at the hips (its back end), so the bound rocks it
		_body = new Node3D { Name = "Body", Position = new Vector3(0, 0.07f, 0.06f) };
		_model.AddChild(_body);
		var k = new MeshKit();
		k.Mat(_fur);
		k.Color = coat;
		k.Blob(new Vector3(0, 0.0f, -0.07f), new Vector3(0.05f, 0.05f, 0.1f), Seed * 3 + 1, 0.06f, false, 1f, 0f, 2);
		k.Color = belly;
		k.Blob(new Vector3(0, -0.02f, -0.08f), new Vector3(0.035f, 0.035f, 0.075f), Seed * 3 + 2, 0.05f, false, 1f, 0f, 1);
		// haunches and small forelegs
		k.Color = coat;
		foreach (float s in new[] { -1f, 1f })
		{
			k.Blob(new Vector3(0.035f * s, -0.015f, -0.0f), new Vector3(0.03f, 0.04f, 0.045f), Seed * 5 + (int)s, 0.06f, false, 1f, 0f, 1);
			k.Cylinder(new Vector3(0.025f * s, -0.02f, -0.12f), new Vector3(0.028f * s, -0.07f, -0.13f), 0.01f, 0.007f, 5, true, 2f);
			k.Cylinder(new Vector3(0.035f * s, -0.04f, 0.01f), new Vector3(0.035f * s, -0.07f, -0.02f), 0.012f, 0.008f, 5, true, 2f);
		}
		k.CommitTo(_body, "Mesh");

		_head = new Node3D { Name = "Head", Position = new Vector3(0, 0.03f, -0.17f) };
		_body.AddChild(_head);
		var hk = new MeshKit();
		hk.Mat(_fur);
		hk.Color = coat;
		hk.Blob(new Vector3(0, 0.0f, -0.025f), new Vector3(0.036f, 0.034f, 0.045f), Seed * 7 + 1, 0.05f, false, 1f, 0f, 2);
		hk.Color = belly;
		hk.Blob(new Vector3(0, -0.012f, -0.05f), new Vector3(0.02f, 0.016f, 0.02f), Seed * 7 + 2, 0.05f, false, 1f, 0f, 1);
		hk.Color = coat;
		foreach (float s in new[] { -1f, 1f })
		{
			// ears: small rounded points
			hk.Cylinder(new Vector3(0.018f * s, 0.025f, -0.01f), new Vector3(0.022f * s, 0.05f, 0.0f), 0.011f, 0.002f, 5, true, 2f);
			hk.Color = dark;
			hk.Blob(new Vector3(0.026f * s, 0.008f, -0.045f), new Vector3(0.007f, 0.008f, 0.006f), Seed * 7 + 4 + (int)s, 0.0f, false, 1f, 0f, 1);
			hk.Color = coat;
		}
		hk.Color = dark;
		hk.Blob(new Vector3(0, -0.002f, -0.07f), new Vector3(0.006f, 0.005f, 0.005f), Seed * 7 + 9, 0f, false, 1f, 0f, 1);
		hk.CommitTo(_head, "Mesh");

		// the tail: up over the back in an S, big and bushy, pale at its fringe
		_tail = new Node3D { Name = "Tail", Position = new Vector3(0, 0.02f, 0.03f) };
		_body.AddChild(_tail);
		var tk = new MeshKit();
		tk.Mat(_fur);
		Vector3[] spine = { new(0, 0.0f, 0.0f), new(0, 0.06f, 0.05f), new(0, 0.15f, 0.06f), new(0, 0.23f, 0.02f), new(0, 0.27f, -0.04f) };
		for (int i = 0; i < spine.Length; i++)
		{
			float r = Mathf.Lerp(0.03f, 0.055f, Mathf.Sin(Mathf.Pi * (i + 0.5f) / spine.Length));
			tk.Color = coat.Lerp(belly, 0.15f + 0.1f * i);
			tk.Blob(spine[i], new Vector3(r * 0.9f, r, r), Seed * 11 + i, 0.18f, false, 1f, 0f, 1);
		}
		tk.CommitTo(_tail, "Mesh");
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
