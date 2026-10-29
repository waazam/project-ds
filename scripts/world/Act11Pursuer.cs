using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 11 (the owner's addition): the thing from the bunker's rooms has followed them out. After the radio it comes out
/// of the bunker's door behind them and runs them down to the last staircase.
///
/// The chase (the owner, 2026-10-29: "the stalker grabs the player from behind and kills them ... unless they get through
/// a new four-checkpoint system: four spotlights beaming straight down from above ... The stalker can't enter a
/// spotlight's beam: it slowly walks around them, shielding its eyes, to keep pursuing ... A player who just sprints flat
/// out ignoring the lights can still outrun it, but it stays right on their heels until it's locked out at the bottom of
/// the big staircase"):
/// - it keeps to their trail (so it never goes through a tree they went round) at <see cref="ChaseSpeed"/>: faster than
///   their walk, slower than their run (run, catch the breath walking, run: about 4.5 m/s), and further back than
///   <see cref="HeelGap"/> it closes at <see cref="HeelSpeed"/>: whoever runs keeps ahead, never far ahead;
/// - <see cref="Act11Lights"/>: it never sets foot in a lit pool; near one it slows to <see cref="CircleSpeed"/> and goes
///   round its rim, its forearms up over its eyes. Standing in one they are safe: it comes round to the side nearest them
///   and waits there, cringing. Leaving by the far side buys the time it takes to come round;
/// - in reach of them out of the light (<see cref="GrabRange"/>), it has them from behind: <see cref="Grab"/>, the death
///   and the reload (to the furthest light reached, or the radio spot);
/// - looked at, its head follows them and its mouth opens (it no longer stops dead: walking backwards watching it would
///   be a way through);
/// - it cannot climb: on the flight they are out of its reach; at the foot it stops, to one side, and stands staring up
///   after them.
///
/// Made and freed by <see cref="Act11Ending"/> (while the way up is open, until the cap is placed).
/// </summary>
public partial class Act11Pursuer : Node3D
{
	/// <summary>Its running pace on their trail (m/s): over a walk (2.7), under a run (5.4).</summary>
	[Export] public float ChaseSpeed = 4.1f;
	/// <summary>Further back than <see cref="HeelGap"/> it closes at this (out of sight or not): never left far behind.</summary>
	[Export] public float HeelSpeed = 5.0f;
	[Export] public float HeelGap = 7f, MaxHeelSpeed = 7.5f;
	/// <summary>Further back than this it closes harder still, the further the harder.</summary>
	[Export] public float FarGap = 10f;
	/// <summary>Its pace round a lit pool, shielding its eyes (m/s).</summary>
	[Export] public float CircleSpeed = 1.5f;
	/// <summary>How far outside a pool's lit edge it keeps (m), and how far beyond that the glare still makes it cringe.</summary>
	[Export] public float BeamMargin = 0.7f, ShieldRange = 3f;
	/// <summary>Closer than this, out of the light, and it has them.</summary>
	[Export] public float GrabRange = 1.15f;
	/// <summary>Out of the bunker's door: it stands there this long, rattling, before it comes (s).</summary>
	[Export] public float EmergeSeconds = 1.6f;
	public const string DeathLine = "It caught you.";

	public StaircaseBuilder Stairs { get; set; }
	public Act11Lights Lights { get; set; }
	/// <summary>It comes out of the bunker's door (the first time), not out of the dark behind them (a Continue).</summary>
	public bool OutOfTheDoor { get; set; }
	public StalkerBody Body { get; private set; }
	/// <summary>For tests: it has reached the foot of the stairs and stopped there (the player is climbing).</summary>
	public bool StuckAtFoot { get; private set; }
	public float DistanceToPlayer { get; private set; } = 999f;
	/// <summary>For tests: how many times it has had them; whether it has them now.</summary>
	public int Grabs { get; private set; }
	public bool Grabbing { get; private set; }
	/// <summary>For tests: seconds it has spent cringing from a light; the closest it has come to a lit pool's edge (m,
	/// negative: inside it).</summary>
	public float ShieldSeconds { get; private set; }
	public float ClosestToLight { get; private set; } = 999f;
	/// <summary>For tests: the shots of the grab (its face over theirs, its hands over their eyes).</summary>
	public bool HandsOverEyes { get; private set; }
	/// <summary>Where it waits at the bottom: just off the foot of the flight, to one side (never in the way up).</summary>
	public Vector3 FootWorld => Stairs != null ? Stairs.ToGlobal(new Vector3(Stairs.Width * 0.5f + 1.3f, 0, 1.4f)) : GlobalPosition;

	private readonly Queue<Vector3> _trail = new();
	private Vector3 _lastCrumb;
	private float _phase, _stepT, _emerge;
	private ForestTerrain _terrain;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1111 };

	public override void _Ready()
	{
		PhotoSubject.Attach(this, "bunker_creature", new Vector3(0, 1.5f, 0), 1f, 45f, 12f, true, new Vector3(0, 2.1f, 0));
		_terrain = GroundSnap.FindTerrain(this);
		Body = new StalkerBody { Name = "Body", Seed = 2077, Size = 1.08f, Idle = true };
		Body.AddToGroup(Audio.DreadDrone.Group);   // (2026-10-10: the dread)
		AddChild(Body);
		Body.GlowEyes(new Color(1f, 0.16f, 0.05f), 6f);
		var p = StoryBeat.Player(this);
		if (p != null) _lastCrumb = p.GlobalPosition;
		if (OutOfTheDoor)
		{
			// out of the dark of the doorway: it stands there a moment, rattling, and then it comes
			_emerge = EmergeSeconds;
			Sfx("bunker_door_grind", 1, 2f);
			Sfx("creature_rattle_stutter", 8, 0f);
		}
	}

	private float Ground(Vector3 at) => _terrain != null ? _terrain.HeightAt(at.X, at.Z) : at.Y;
	private float Rim => (Lights?.BeamRadius ?? 0f) + BeamMargin;

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
		if (player == null || Stairs == null || Grabbing) return;
		Vector3 pp = player.GlobalPosition;
		// the trail it keeps to: where they have walked (so it never goes through a tree they went round)
		if (pp.DistanceTo(_lastCrumb) > 0.8f && !OnFlight(pp)) { _trail.Enqueue(pp); _lastCrumb = pp; if (_trail.Count > 400) _trail.Dequeue(); }
		Vector3 flat = new(pp.X - GlobalPosition.X, 0, pp.Z - GlobalPosition.Z);
		DistanceToPlayer = flat.Length();
		Body.LookTarget = pp + Vector3.Up * 1.6f;
		if (Lights != null)
			for (int i = 0; i < Act11Lights.Count; i++)
				if (Lights.On) ClosestToLight = Mathf.Min(ClosestToLight, new Vector2(GlobalPosition.X - Lights.Centres[i].X, GlobalPosition.Z - Lights.Centres[i].Z).Length() - Lights.BeamRadius);
		if (PlayerDeath.Dying) { Body.WalkPhase = -1f; return; }
		if (_emerge > 0f)
		{
			_emerge -= dt;
			Body.WalkPhase = -1f;
			Body.TrackTarget = player.CameraRig?.Camera?.GlobalPosition;
			if (flat.LengthSquared() > 0.01f) Rotation = new Vector3(0, Mathf.Atan2(flat.X, flat.Z), 0);
			return;
		}

		bool climbing = OnFlight(pp);
		int safeIn = Lights?.BeamAt(pp) ?? -1;
		// far behind and out of sight: it is simply nearer, on their trail, the next time they look round
		if (!climbing && safeIn < 0 && DistanceToPlayer > 30f && !Seen(player)) CatchUp(pp);
		Vector3 goal;
		if (climbing)
		{
			goal = FootWorld;
			_trail.Clear();
		}
		else if (safeIn >= 0)
		{
			// they're in the light: round its rim to the point nearest them, and wait there
			Vector3 c = Lights.Centres[safeIn];
			// the way they came is behind them now: when they step out it goes from the rim, not back along the old trail
			_trail.Clear();
			Vector3 off = (pp - c) with { Y = 0 };
			if (off.Length() < 0.3f) off = (GlobalPosition - c) with { Y = 0 };
			goal = c + off.Normalized() * Rim;
		}
		else
		{
			// the next crumb of their trail that isn't in a light (it goes round those)
			while (_trail.Count > 0 && Lights != null && Lights.BeamAt(_trail.Peek(), BeamMargin + 0.2f) >= 0) _trail.Dequeue();
			goal = _trail.Count > 0 ? _trail.Peek() : pp;
			if (_trail.Count > 0 && new Vector2(goal.X - GlobalPosition.X, goal.Z - GlobalPosition.Z).Length() < 0.6f) { _trail.Dequeue(); goal = _trail.Count > 0 ? _trail.Peek() : pp; }
		}
		// never onto the flight
		if (Stairs.ToLocal(goal).Z < 1.2f && Mathf.Abs(Stairs.ToLocal(goal).X) < Stairs.Width * 0.5f + 1.5f) goal = FootWorld;

		// near a lit pool its forearms come up over its eyes; hugging the rim it slows to a crawl (2026-10-29, the first
		// sprint run: slowed all through the glare, it fell 20 m back on a player running past the lights)
		bool glare = !climbing && Lights != null && Lights.BeamAt(GlobalPosition, BeamMargin + ShieldRange) >= 0;
		bool hugging = glare && Lights.BeamAt(GlobalPosition, BeamMargin + 1.2f) >= 0;
		// further back than a few strides it closes, the harder the further (the second sprint run: out of the door 13 m back,
		// it never made up the start on a player running flat out)
		float speed = DistanceToPlayer > HeelGap ? Mathf.Clamp(HeelSpeed + (DistanceToPlayer - FarGap) * 0.3f, HeelSpeed, MaxHeelSpeed) : ChaseSpeed;
		if (glare) ShieldSeconds += dt;
		// round the rim: a crawl while they wait in that light; after someone running past it, a wary skirting pace (the third
		// sprint run: crawling round each light they ran through, it fell 20 m behind)
		if (hugging) speed = Lights.BeamAt(pp, BeamMargin + 1.2f) == Lights.BeamAt(GlobalPosition, BeamMargin + 1.2f) ? CircleSpeed : Mathf.Max(CircleSpeed, speed * 0.75f);
		Body.ShieldEyes = glare ? 1f : 0f;
		// looked at, its head follows them and its mouth opens (but it comes on: it doesn't stop dead any more)
		bool watched = !climbing && DistanceToPlayer < 26f && Seen(player);
		Body.Watched = watched;
		Body.TrackTarget = glare ? null : player.CameraRig?.Camera?.GlobalPosition;

		Vector3 to = new(goal.X - GlobalPosition.X, 0, goal.Z - GlobalPosition.Z);
		float dist = to.Length();
		StuckAtFoot = climbing && GlobalPosition.DistanceTo(FootWorld with { Y = GlobalPosition.Y }) < 0.8f;
		bool moving = dist > 0.05f && !(climbing && StuckAtFoot) && (safeIn >= 0 || DistanceToPlayer > 0.9f);
		if (moving)
		{
			Vector3 np = GlobalPosition + to / dist * Mathf.Min(dist, speed * dt);
			// never into a lit pool: round its rim instead, toward the side the goal is on
			if (Lights != null && Lights.BeamAt(np, BeamMargin) is int b && b >= 0) np = AroundRim(b, goal, speed * dt);
			np.Y = Ground(np);
			float moved = (np - GlobalPosition).Length();
			GlobalPosition = np;
			_phase += moved / (Body.Rigged ? Body.StepLength : 1.3f) * Mathf.Pi;
			Body.WalkPhase = _phase;
			Body.WalkAmount = Mathf.Clamp(speed / 2.2f, 0.4f, 1f);
			_stepT -= moved;
			if (_stepT <= 0f) { _stepT = Body.Rigged ? Body.StepLength : 1.3f; Sfx("stalker_step_dirt", 10, -12f); if (_rng.Randf() < 0.5f) Sfx("cloth", 4, -24f); }
			if (moved < 0.002f) Body.WalkPhase = -1f;
		}
		else
		{
			Body.WalkPhase = -1f;
			// at the foot of the flight, drawn up and staring after them
			if (climbing && StuckAtFoot && Body.Clip != "stare") Body.Play("stare", 0.5f);
			else if (!climbing && Body.Clip == "stare") Body.Rest();
		}
		// face where it's going, or them when it has stopped
		Vector3 face = !moving || climbing && StuckAtFoot || DistanceToPlayer < 3f ? flat : to;
		if (face.LengthSquared() > 0.001f) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(face.X, face.Z), Mathf.Min(1f, dt * 5f)), 0);

		if (!climbing && safeIn < 0 && DistanceToPlayer < GrabRange && player.PlayerInput.Enabled && !PlayerDeath.Dying)
			_ = Cutscene.Run(this, ct => Grab(player, ct), lockInput: true, freezeBody: true);
	}

	/// <summary>A step round pool <paramref name="b"/>'s rim, toward the side <paramref name="goal"/> is on.</summary>
	private Vector3 AroundRim(int b, Vector3 goal, float step)
	{
		Vector3 c = Lights.Centres[b];
		float r = Rim;
		Vector2 rel = new(GlobalPosition.X - c.X, GlobalPosition.Z - c.Z);
		if (rel.LengthSquared() < 1e-4f) rel = Vector2.Right;
		float a = Mathf.Atan2(rel.Y, rel.X);
		float ga = Mathf.Atan2(goal.Z - c.Z, goal.X - c.X);
		float na = a + Mathf.Clamp(Mathf.AngleDifference(a, ga), -step / r, step / r);
		return new Vector3(c.X + Mathf.Cos(na) * r, GlobalPosition.Y, c.Z + Mathf.Sin(na) * r);
	}

	private bool Seen(PlayerController player)
	{
		var cam = player.CameraRig?.Camera;
		if (cam == null) return true;
		Vector3 to = (GlobalPosition + Vector3.Up * 1.4f - cam.GlobalPosition).Normalized();
		return (-cam.GlobalBasis.Z).Dot(to) > 0.25f;
	}

	/// <summary>Onto the crumb of their trail about 18 m back from them (dropping the older ones; never one in a light).</summary>
	private void CatchUp(Vector3 pp)
	{
		var crumbs = _trail.ToArray();
		int pick = -1;
		for (int i = crumbs.Length - 1; i >= 0; i--)
			if (new Vector2(crumbs[i].X - pp.X, crumbs[i].Z - pp.Z).Length() >= 18f && (Lights == null || Lights.BeamAt(crumbs[i], BeamMargin + 0.5f) < 0)) { pick = i; break; }
		if (pick < 0) return;
		Vector3 at = crumbs[pick];
		at.Y = Ground(at);
		GlobalPosition = at;
		_trail.Clear();
		for (int i = pick + 1; i < crumbs.Length; i++) _trail.Enqueue(crumbs[i]);
	}

	/// <summary>Seconds the grab holds before the black.</summary>
	[Export] public float GrabSeconds = 1.8f;

	/// <summary>
	/// It has them, from behind: its rattle right at their ear, and the view yanked back, thrown up and over until they
	/// are looking up and back into its face, upside down, its lit eyes on theirs; its long hands come round over their
	/// face from either side; black, "It caught you.", and the last save. Control never comes back.
	/// </summary>
	private async Task Grab(PlayerController player, CancellationToken ct)
	{
		if (PlayerDeath.Dying) return;
		Grabbing = true;
		Grabs++;
		PlayerDeath.Begin();
		GD.Print("[story] Act 11: it caught them from behind");
		var rig = player.CameraRig;
		var cam = rig.Camera;
		Sfx("creature_rattle_stutter", 8, 4f);
		Sfx("body_thump", 2, -2f);
		Audio.AudioDirector.OneShot(this, "creature_jumpscare", 3, cam.GlobalPosition, -4f, "Unnatural", 6f, 0.03f);
		Audio.AudioDirector.OneShot(this, "pbreath_gasp", 16, null, -6f, "Player");
		Body.ShieldEyes = 0f;
		Body.WalkPhase = -1f;
		Body.Watched = true;
		Body.Play("loom", 0.08f);   // drawn up over them, the arms out
		float yaw = rig.Yaw;
		Vector3 fwd = new(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
		// right at their back, facing the way they face
		Rotation = new Vector3(0, Mathf.Atan2(fwd.X, fwd.Z), 0);
		Vector3 from = player.GlobalPosition, pulled = from - fwd * 0.45f;
		float pitch0 = rig.Pitch;
		rig.MaxPitchOverride = 88f;
		try
		{
			double t = 0;
			while (t < GrabSeconds)
			{
				await Cutscene.Frame(this, ct);
				float d = (float)GetProcessDeltaTime();
				t += d;
				float e = 1f - Mathf.Pow(1f - Mathf.Min(1f, (float)(t / 0.4)), 3f);
				// yanked back a step off their feet, the view thrown up and over, past straight up, into its face
				Vector3 p = from.Lerp(pulled, e);
				p.Y = Ground(p) + 0.05f;
				player.GlobalPosition = p;
				rig.SetPitch(Mathf.Lerp(pitch0, Mathf.DegToRad(88f), e));
				rig.PitchSwim = 0.42f * e + 0.02f * Mathf.Sin((float)t * 31f) * e;
				rig.RollSwim = 0.12f * e;
				Vector3 cp = cam.GlobalPosition;
				Basis cb = cam.GlobalBasis;
				Vector3 cf = -cb.Z, cr = cb.X, cu = cb.Y;
				// its face over theirs, a hand's breadth beyond the lens, looking down into their eyes
				Body.TrackTarget = cp;
				Vector3 wantEyes = cp + cf * 0.5f + cu * 0.06f;
				Vector3 shift = wantEyes - Body.EyesWorld;
				GlobalPosition += shift * Mathf.Min(1f, d * (t < 0.3 ? 8f : 14f));
				// its hands round from behind, over their eyes, the long fingers across
				if (t > 0.35)
				{
					Body.HandsTo(cp + cf * 0.3f + cr * 0.15f - cu * 0.03f, cp + cf * 0.32f - cr * 0.15f + cu * 0.02f, -cr, cr);
					HandsOverEyes = Body.HandWorld("R").DistanceTo(cp) < 0.6f;
				}
			}
			StoryBeat.Fader(this)?.SetBlack(true);
		}
		finally
		{
			if (IsInstanceValid(rig)) { rig.MaxPitchOverride = null; rig.PitchSwim = 0f; rig.RollSwim = 0f; }
		}
		await PlayerDeath.Reload(this, DeathLine, ct);
	}

	private void Sfx(string name, int variants, float db)
	{
		string path = Audio.AudioDirector.Take(name, variants);
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}_01.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, UnitSize = 4f, MaxDistance = 40f };
		AddChild(s);
		s.Position = Vector3.Up * 1.4f;
		s.Finished += s.QueueFree;
		s.Play();
	}
}
