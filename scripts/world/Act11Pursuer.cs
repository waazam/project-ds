using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 11 (the owner's addition): the thing from the bunker's rooms has followed them out. After the
/// radio, on the long walk to the last staircase, it comes out of the bunker's door behind them and
/// follows: slowly at first, a gaunt shape keeping to their trail, only ever seen when they turn round.
/// Once they are within reach of the staircase it matches their pace. If it gets close it shoves them,
/// hard, spinning them round toward the stairs. It cannot climb: at the foot of the flight it stops,
/// and stays there, staring up after them.
///
/// Made and freed by <see cref="Act11Ending"/> (while the way up is open, until the cap is placed).
/// </summary>
public partial class Act11Pursuer : Node3D
{
	/// <summary>Its walking pace far from the stairs (m/s): slower than a walk.</summary>
	[Export] public float SlowSpeed = 1.25f;
	/// <summary>Within this of the staircase's foot the player is in its "radius": it keeps their pace.</summary>
	[Export] public float StairsRadius = 70f;
	/// <summary>Closer than this and it shoves them toward the stairs.</summary>
	[Export] public float ShoveRange = 1.5f;
	[Export] public float ShoveCooldown = 6f;

	public StaircaseBuilder Stairs { get; set; }
	public StalkerBody Body { get; private set; }
	/// <summary>For tests: how many times it has shoved them.</summary>
	public int Shoves { get; private set; }
	/// <summary>For tests: it has reached the foot of the stairs and stopped there (the player is climbing).</summary>
	public bool StuckAtFoot { get; private set; }
	public float DistanceToPlayer { get; private set; } = 999f;
	/// <summary>Where it waits at the bottom: just off the foot of the flight, to one side (never in the way up).</summary>
	public Vector3 FootWorld => Stairs != null ? Stairs.ToGlobal(new Vector3(Stairs.Width * 0.5f + 1.3f, 0, 1.4f)) : GlobalPosition;

	private readonly Queue<Vector3> _trail = new();
	private Vector3 _lastCrumb;
	private float _cool = 3f, _pause, _phase, _stepT;
	private bool _shoving;
	private ForestTerrain _terrain;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1111 };

	public override void _Ready()
	{
		_terrain = GroundSnap.FindTerrain(this);
		Body = new StalkerBody { Name = "Body", Seed = 2077, Size = 1.08f, Idle = true };
		AddChild(Body);
		Body.GlowEyes(new Color(1f, 0.16f, 0.05f), 6f);
		var p = StoryBeat.Player(this);
		if (p != null) _lastCrumb = p.GlobalPosition;
	}

	private float Ground(Vector3 at) => _terrain != null ? _terrain.HeightAt(at.X, at.Z) : at.Y;

	/// <summary>True while the player is on the flight itself (it can't follow there).</summary>
	private bool OnFlight(Vector3 world)
	{
		if (Stairs == null) return false;
		Vector3 l = Stairs.ToLocal(world);
		return l.Z <= 0.6f && l.Z >= Stairs.BackZ - 1.5f && Mathf.Abs(l.X) < Stairs.Width * 0.5f + 0.8f && l.Y > 0.3f;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		var player = StoryBeat.Player(this);
		if (player == null || Stairs == null) return;
		Vector3 pp = player.GlobalPosition;
		// the trail it keeps to: where they have walked (so it never goes through a tree they went round)
		if (pp.DistanceTo(_lastCrumb) > 0.8f && !OnFlight(pp)) { _trail.Enqueue(pp); _lastCrumb = pp; if (_trail.Count > 400) _trail.Dequeue(); }
		Vector3 flat = new(pp.X - GlobalPosition.X, 0, pp.Z - GlobalPosition.Z);
		DistanceToPlayer = flat.Length();
		_cool -= dt;
		Body.LookTarget = pp + Vector3.Up * 1.6f;
		if (_shoving) return;

		// up the stairs: it goes to the foot and no further, and stands there staring up at them
		bool climbing = OnFlight(pp);
		// far behind and out of sight: it is simply nearer, on their trail, the next time they look round
		if (!climbing && DistanceToPlayer > 35f && !Seen(player)) CatchUp(pp);
		Vector3 goal;
		if (climbing)
		{
			goal = FootWorld;
			_trail.Clear();
		}
		else if (_trail.Count > 0)
		{
			goal = _trail.Peek();
			if (new Vector2(goal.X - GlobalPosition.X, goal.Z - GlobalPosition.Z).Length() < 0.6f) { _trail.Dequeue(); goal = _trail.Count > 0 ? _trail.Peek() : pp; }
		}
		else goal = pp;
		// never onto the flight
		if (Stairs.ToLocal(goal).Z < 1.2f && Mathf.Abs(Stairs.ToLocal(goal).X) < Stairs.Width * 0.5f + 1.5f) goal = FootWorld;

		float nearStairs = new Vector2(pp.X - FootWorld.X, pp.Z - FootWorld.Z).Length();
		float playerSpeed = new Vector2(player.Velocity.X, player.Velocity.Z).Length();
		float speed = nearStairs < StairsRadius ? Mathf.Max(SlowSpeed, playerSpeed) : SlowSpeed;
		// it never falls far behind: out of sight back there, it closes the gap
		if (DistanceToPlayer > 16f) speed = Mathf.Max(speed, Mathf.Max(playerSpeed * 1.15f, 3.6f));
		if (_pause > 0f) { _pause -= dt; speed = 0f; }

		Vector3 to = new(goal.X - GlobalPosition.X, 0, goal.Z - GlobalPosition.Z);
		float dist = to.Length();
		StuckAtFoot = climbing && GlobalPosition.DistanceTo(FootWorld with { Y = GlobalPosition.Y }) < 0.8f;
		if (dist > 0.05f && speed > 0f && !(climbing && StuckAtFoot) && DistanceToPlayer > 1.1f)
		{
			Vector3 step = to / dist * Mathf.Min(dist, speed * dt);
			Vector3 np = GlobalPosition + step;
			np.Y = Ground(np);
			GlobalPosition = np;
			_phase += speed * dt / 1.3f * Mathf.Pi;
			Body.WalkPhase = _phase;
			Body.WalkAmount = Mathf.Clamp(speed / 2.2f, 0.4f, 1f);
			_stepT -= speed * dt;
			if (_stepT <= 0f) { _stepT = 1.3f; Sfx("step_dirt", 4, -14f); }
		}
		else Body.WalkPhase = -1f;
		// face where it's going, or them when it has stopped
		Vector3 face = climbing && StuckAtFoot || DistanceToPlayer < 3f ? flat : to;
		if (face.LengthSquared() > 0.001f) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(face.X, face.Z), Mathf.Min(1f, dt * 4f)), 0);

		if (!climbing && DistanceToPlayer < ShoveRange && _cool <= 0f && player.PlayerInput.Enabled)
		{
			_cool = ShoveCooldown;
			_ = Cutscene.Run(this, ct => Shove(player, ct), lockInput: true, freezeBody: true);
		}
	}

	private bool Seen(PlayerController player)
	{
		var cam = player.CameraRig?.Camera;
		if (cam == null) return true;
		Vector3 to = (GlobalPosition + Vector3.Up * 1.4f - cam.GlobalPosition).Normalized();
		return (-cam.GlobalBasis.Z).Dot(to) > 0.25f;
	}

	/// <summary>Onto the crumb of their trail about 18 m back from them (dropping the older ones).</summary>
	private void CatchUp(Vector3 pp)
	{
		var crumbs = _trail.ToArray();
		int pick = -1;
		for (int i = crumbs.Length - 1; i >= 0; i--)
			if (new Vector2(crumbs[i].X - pp.X, crumbs[i].Z - pp.Z).Length() >= 18f) { pick = i; break; }
		if (pick < 0) return;
		Vector3 at = crumbs[pick];
		at.Y = Ground(at);
		GlobalPosition = at;
		_trail.Clear();
		for (int i = pick + 1; i < crumbs.Length; i++) _trail.Enqueue(crumbs[i]);
	}

	/// <summary>It shoves them: a hard push that spins them round to face the stairs and throws them a few
	/// metres toward them. Then it stops a moment, and comes on again.</summary>
	private async Task Shove(PlayerController player, CancellationToken ct)
	{
		_shoving = true;
		Shoves++;
		GD.Print($"[story] Act 11: it caught them up - a shove toward the stairs ({Shoves})");
		Sfx("creature_snarl", 3, -2f);
		Sfx("body_thump", 2, -4f);
		Vector3 toStairs = FootWorld - player.GlobalPosition;
		toStairs.Y = 0;
		if (toStairs.LengthSquared() < 0.01f) toStairs = -GlobalBasis.Z;
		toStairs = toStairs.Normalized();
		float yawTo = Mathf.Atan2(-toStairs.X, -toStairs.Z);
		Vector3 from = player.GlobalPosition, dest = from + toStairs * 3.2f;
		var rig = player.CameraRig;
		double t = 0;
		const double dur = 0.5;
		while (t < dur)
		{
			await Cutscene.Frame(this, ct);
			float d = (float)GetProcessDeltaTime();
			t += d;
			float u = Mathf.Clamp((float)(t / dur), 0f, 1f);
			float e = 1f - (1f - u) * (1f - u);
			Vector3 p = from.Lerp(dest, e);
			p.Y = Ground(p) + 0.05f;
			player.GlobalPosition = p;
			// spun round toward the stairs, and the view jolted down a little
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, yawTo) * Mathf.Min(1f, d * 14f), 0));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, -0.12f, Mathf.Min(1f, d * 8f)));
		}
		player.Velocity = Vector3.Zero;
		_pause = 1.4f;
		_shoving = false;
	}

	private void Sfx(string name, int variants, float db)
	{
		string path = $"res://assets/audio/sfx/{name}_{_rng.RandiRange(1, variants):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}_01.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, UnitSize = 4f, MaxDistance = 40f };
		AddChild(s);
		s.Position = Vector3.Up * 1.4f;
		s.Finished += s.QueueFree;
		s.Play();
	}
}
