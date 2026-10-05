using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World;
using ProjectDS.World.SnowMaze;

namespace ProjectDS.Entities;

/// <summary>
/// The wendigo in the snow maze (Act 24; the owner: "the wendigo will be a boss enemy that will hunt the player ...
/// reminiscent of alien isolation: the ai is a two brain system"):
/// <list type="bullet">
/// <item><b>The director</b> always knows where the player is, but never tells the wendigo. It keeps a menace gauge: the
/// nearer the thing is to them, the more it can see of them, the higher it climbs. Left low too long (they're too safe),
/// it nudges the wendigo toward their part of the maze, a junction or two off, never to them; kept high too long (they've
/// been under it too long), it calls the wendigo away into the far tunnels.</item>
/// <item><b>The hunter</b> only knows what it sees and hears. It hears the player by their noise: running carries far,
/// walking a little, creeping crouched hardly at all, standing still not at all (and the flamethrower's roar further than
/// anything); the sound travels along the tunnels, not through the snow. It sees in a cone ahead of it, out to where the
/// cave's dark swallows a shape, never through a mound or a wall, a crouched body less and a lit lantern more; behind it,
/// it is half deaf and blind. What it notices, it goes to; what it's sure of, it runs down; what it loses, it searches for,
/// slowly, and gives up on.</item>
/// <item>Once burned (<see cref="Burned"/>), the hunter is the prey: it hides, keeps out of their sight, comes back in
/// slowly and from behind, and runs at them when they're looking away; burned again, it runs. It can still kill them.</item>
/// </list>
/// It walks the maze's graph (<see cref="SnowMazeLayout"/>), in the cave's own space. Tuning is in the exports.
/// </summary>
public partial class WendigoHunter : Node3D
{
	public enum Mode { Patrol, Investigate, Search, Chase, Kill, Burning, Flee, Hide, Stalk, Ambush, Off }

	[Export] public float WalkSpeed = 1.7f, HuntSpeed = 3.0f, ChaseSpeed = 4.7f, FleeSpeed = 5.4f;
	[Export] public float HearRun = 20f, HearWalk = 8f, HearCrouch = 2.6f, HearFlame = 30f;
	[Export] public float SightRange = 17f, SightHalfAngle = 60f, KillReach = 1.9f;
	/// <summary>Hits of fire to finish it (the owner: "4-5 hits will engage act 25").</summary>
	[Export] public int HitsToKill = 4;
	/// <summary>Its size in the tunnels, and its eye's height off the floor there (hunched).</summary>
	public const float BodyScale = 0.66f, EyeHeight = 2.2f;

	public Mode State { get; private set; } = Mode.Patrol;
	public Wendigo Body { get; private set; }
	public float Menace { get; private set; }
	public float Awareness { get; private set; }
	public int Hits { get; private set; }
	public bool Burned => Hits > 0;
	public bool Dead { get; private set; }
	/// <summary>For tests: what it last heard, the director's last nudge and send-away, kills.</summary>
	public int Heard { get; private set; }
	public int Nudges { get; private set; }
	public int SendAways { get; private set; }
	public int Sightings { get; private set; }
	/// <summary>Tests: stop it hunting (it stands where it is, idle).</summary>
	public bool Paused;
	/// <summary>Tests: it senses and burns, but stays where it's put.</summary>
	public bool HoldStill;
	public event System.Action<int> Hit;
	public event System.Action Killed;
	public event System.Action CaughtPlayer;

	private SnowMazeCave _cave;
	private SnowMazeLayout L => _cave.Layout;
	private readonly List<Vector3> _route = new();
	private Vector3 _goal, _lastKnown;
	private float _modeT, _senseT, _lowMenaceT, _highMenaceT, _searchLeft, _exposure, _hitCool, _stepT, _breathT, _unseenT;
	private int _searchStops;
	private readonly RandomNumberGenerator _rng = new();
	private PlayerController _player;
	private bool _phase2;
	private FireVfx _burn;

	public void Setup(SnowMazeCave cave, Vector3 startLocal)
	{
		_cave = cave;
		_rng.Randomize();
		Body = new Wendigo { Name = "MazeWendigo" };
		cave.AddChild(Body);
		// (the tunnels' roofs are three metres up: at its full height it stood through them)
		Body.SetSize(BodyScale);
		Body.StandAt(cave.ToGlobal(startLocal), cave.ToGlobal(startLocal + Vector3.Back));
		Body.Play("walk", 1.4f, 0.2);
		Pick(Mode.Patrol);
	}

	private Vector3 Pos => _cave.ToLocal(Body.GlobalPosition);
	private Vector3 Fwd => (_cave.GlobalBasis.Inverse() * -Body.GlobalBasis.Z) with { Y = 0 };
	private Vector3 PlayerLocal => _cave.ToLocal(_player.GlobalPosition);

	public override void _Process(double delta)
	{
		if (_cave == null || Body == null || Dead) return;
		float dt = (float)delta;
		_player ??= StoryBeat.Player(this);
		if (_player == null || Paused || State == Mode.Off || State == Mode.Kill) return;
		_modeT += dt;
		_hitCool -= dt;
		Director(dt);
		Senses(dt);
		Burnt(dt);
		if (!HoldStill) Act(dt);
		Sound(dt);
	}

	// ------------------------------------------------------------------ the director

	private void Director(float dt)
	{
		var p = PlayerLocal;
		float d = Pos.DistanceTo(p);
		// menace: up when it's near them and the more so when it can see them; down when it's off in the maze
		float near = Mathf.Clamp(1f - d / 22f, 0f, 1f);
		float rise = near * 18f + (State is Mode.Chase or Mode.Ambush ? 25f : 0f) + Awareness * 10f;
		Menace = Mathf.Clamp(Menace + (rise - 6f) * dt, 0f, 100f);
		if (Menace < 15f) _lowMenaceT += dt; else _lowMenaceT = 0f;
		if (Menace > 70f && State is not (Mode.Chase or Mode.Kill or Mode.Ambush)) _highMenaceT += dt; else _highMenaceT = 0f;
		// safe too long: nudge it toward their part of the maze (a junction or two off them, never to them)
		if (_lowMenaceT > (_phase2 ? 30f : 22f) && State is Mode.Patrol or Mode.Hide)
		{
			_lowMenaceT = 0f;
			int pn = L.NearestConnected(p);
			var near2 = new List<int>();
			foreach (int a in L.Adj[pn]) foreach (int b in L.Adj[a]) if (b != pn && !near2.Contains(b)) near2.Add(b);
			if (near2.Count == 0) near2.AddRange(L.Adj[pn]);
			int target = near2[_rng.RandiRange(0, near2.Count - 1)];
			Nudges++;
			GD.Print($"[wendigo] director: too quiet - nudged toward node {target} (the player near {pn})");
			if (_phase2) { Pick(Mode.Stalk); RouteTo(L.Nodes[target]); }
			else { Pick(Mode.Patrol); RouteTo(L.Nodes[target]); }
		}
		// under it too long: call it away into the far tunnels
		if (_highMenaceT > 20f)
		{
			_highMenaceT = 0f;
			SendAway();
		}
	}

	private void SendAway()
	{
		var p = PlayerLocal;
		int best = 0;
		float bd = 0f;
		for (int i = 0; i < 12; i++)
		{
			int v = _rng.RandiRange(0, L.Nodes.Count - 1);
			float d = L.Nodes[v].DistanceTo(p);
			if (d > bd) { bd = d; best = v; }
		}
		SendAways++;
		GD.Print($"[wendigo] director: too much - sent away to node {best}");
		Pick(_phase2 ? Mode.Hide : Mode.Patrol);
		RouteTo(L.Nodes[best]);
	}

	// ------------------------------------------------------------------ the senses

	/// <summary>How far the player's noise carries now (metres), and from where.</summary>
	private float Noise()
	{
		if (Flamethrower.Instance is { Firing: true }) return HearFlame;
		float sp = _player.GroundSpeed;
		if (sp < 0.3f) return 0f;
		if (_player.Crouching) return HearCrouch;
		return _player.IsRunning && sp > 3.2f ? HearRun : HearWalk;
	}

	private bool Behind(Vector3 p)
	{
		var to = (p - Pos) with { Y = 0 };
		return to.LengthSquared() > 0.01f && Fwd.Normalized().Dot(to.Normalized()) < Mathf.Cos(Mathf.DegToRad(110f));
	}

	private void Senses(float dt)
	{
		var p = PlayerLocal;
		// sight: a cone ahead, out to the dark; nothing through a mound or a wall
		bool sees = CanSee(p);
		float range = SightRange * (_player.Crouching ? 0.7f : 1f) * (LanternLit() ? 1.3f : 1f);   // (the same reach CanSee used)
		float dist = Pos.DistanceTo(p);
		if (sees)
		{
			float closeness = Mathf.Clamp(1f - dist / range, 0f, 1f);
			Awareness = Mathf.Min(1.2f, Awareness + dt * (0.6f + 3.5f * closeness * closeness));
			_lastKnown = p;
		}
		else Awareness = Mathf.Max(0f, Awareness - dt * 0.25f);
		if (sees && Awareness >= 1f && State is not (Mode.Chase or Mode.Burning or Mode.Flee))
		{
			if (_phase2 && State != Mode.Ambush)
			{
				// the prey, seen: it breaks away (unless it's already coming at them)
				if (State is not Mode.Hide) { Pick(Mode.Flee); FleeFrom(p); }
			}
			else
			{
				Sightings++;
				GD.Print($"[wendigo] sees them ({dist:0.0} m) - the chase");
				Pick(Mode.Chase);
				Shriek();
			}
		}
		// hearing, four times a second: the noise's reach, along the tunnels; behind it, half of it
		_senseT -= dt;
		if (_senseT > 0f) return;
		_senseT = 0.25f;
		float reach = Noise();
		if (reach <= 0f || State is Mode.Chase or Mode.Burning or Mode.Flee) return;
		if (Behind(p)) reach *= 0.5f;
		float along = TunnelDistance(Pos, p);
		if (along > reach) return;
		Heard++;
		var guess = p + new Vector3(_rng.RandfRange(-1.8f, 1.8f), 0, _rng.RandfRange(-1.8f, 1.8f));
		if (_phase2 && State is Mode.Hide or Mode.Flee) return;   // (the prey keeps its head down)
		if (State != Mode.Investigate || guess.DistanceTo(_goal) > 3f)
		{
			GD.Print($"[wendigo] hears them ({along:0} m along the tunnels, their noise carries {reach:0} m) - going to look");
			Pick(_phase2 ? Mode.Stalk : Mode.Investigate);
			RouteTo(guess);
		}
	}

	/// <summary>The crate prised open, the flamethrower in their hands: it heard (the menace up; it comes to look where the
	/// noise was, from wherever it is, unless it is already on them).</summary>
	public bool PlayerArmed { get; private set; }
	public void ArmedPlayer()
	{
		PlayerArmed = true;
		Menace = Mathf.Max(Menace, 60f);
		_player ??= StoryBeat.Player(this);
		if (_player == null || Body == null || State is Mode.Chase or Mode.Kill or Mode.Burning) return;
		Heard++;
		Pick(_phase2 ? Mode.Stalk : Mode.Investigate);
		RouteTo(PlayerLocal + new Vector3(_rng.RandfRange(-3f, 3f), 0, _rng.RandfRange(-3f, 3f)));
	}

	private bool LanternLit() => _player.GetNodeOrNull<Lantern>("Lantern") is { } l && l.Shining;

	/// <summary>In its cone and its reach, and nothing in the way (a ray from its eyes to their chest).</summary>
	public bool CanSee(Vector3 p)
	{
		var eye = Pos + Vector3.Up * EyeHeight;   // (under the roof: an eye up in the snow saw nothing)
		var chest = p + Vector3.Up * (_player.Crouching ? 0.7f : 1.25f);
		float d = eye.DistanceTo(chest);
		float range = SightRange * (_player.Crouching ? 0.7f : 1f) * (LanternLit() ? 1.3f : 1f);
		if (d > range) return false;
		var to = (chest - eye) with { Y = 0 };
		if (to.LengthSquared() > 0.25f && Fwd.Normalized().Dot(to.Normalized()) < Mathf.Cos(Mathf.DegToRad(SightHalfAngle))) return false;
		var q = PhysicsRayQueryParameters3D.Create(_cave.ToGlobal(eye), _cave.ToGlobal(chest), 1);
		return GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
	}

	/// <summary>Distance between two points the way a sound goes: along the tunnels.</summary>
	public float TunnelDistance(Vector3 a, Vector3 b) => L.TunnelDistance(a, b);

	// ------------------------------------------------------------------ what it does

	private void Pick(Mode m)
	{
		if (State == m && _modeT < 0.2f) return;
		State = m;
		_modeT = 0f;
		float speed = m switch { Mode.Chase or Mode.Ambush => ChaseSpeed, Mode.Flee => FleeSpeed, Mode.Investigate or Mode.Search => HuntSpeed, _ => WalkSpeed };
		if (m is Mode.Burning or Mode.Kill) return;
		if (m is Mode.Hide) { Body.Hold("crouch", 0.7f, 0.4); return; }
		Body.Play(speed > 3.5f ? "run" : "walk", speed > 3.5f ? 0.75f : 1.5f * (1.7f / Mathf.Max(speed, 0.5f)), 0.25);
	}

	private void RouteTo(Vector3 target)
	{
		_route.Clear();
		_goal = target;
		int a = L.NearestConnected(Pos), b = L.NearestConnected(target);
		var path = L.Path(a, b);
		for (int i = 0; i < path.Count; i++)
		{
			// through a door of the cavern (not its middle) unless the cavern is where it's going
			if (path[i] == L.Cavern && i > 0 && i < path.Count - 1)
			{
				_route.Add(L.EndAt(path[i - 1], L.Cavern));
				_route.Add(L.EndAt(path[i + 1], L.Cavern));
				continue;
			}
			if (path[i] == L.Cavern && i > 0) { _route.Add(L.EndAt(path[i - 1], L.Cavern)); continue; }
			if (i == 0 && Pos.DistanceTo(L.Nodes[path[i]]) < 1f) continue;
			_route.Add(L.Nodes[path[i]]);
		}
		_route.Add(target with { Y = 0f });
	}

	private void Act(float dt)
	{
		var p = PlayerLocal;
		float dist = Pos.DistanceTo(p);
		switch (State)
		{
			case Mode.Patrol:
				if (_route.Count == 0) RouteTo(L.Nodes[RandomNodeNear(Pos, 4)]);
				Walk(WalkSpeed, dt);
				break;
			case Mode.Investigate:
				Walk(HuntSpeed, dt);
				if (_route.Count == 0) { Pick(Mode.Search); _searchLeft = _rng.RandfRange(10f, 18f); _searchStops = 0; }
				break;
			case Mode.Search:
				_searchLeft -= dt;
				if (_route.Count == 0)
				{
					if (_searchLeft <= 0f || _searchStops > 4) { GD.Print("[wendigo] gives up searching"); Pick(Mode.Patrol); break; }
					_searchStops++;
					// a look round, then on to somewhere close by (an alcove, the next junction)
					RouteTo(L.Nodes[RandomNodeNear(_lastKnown == Vector3.Zero ? Pos : _lastKnown, 1)] + new Vector3(_rng.RandfRange(-1.5f, 1.5f), 0, _rng.RandfRange(-1.5f, 1.5f)));
				}
				Walk(HuntSpeed * 0.8f, dt);
				break;
			case Mode.Chase:
			case Mode.Ambush:
				// straight at them where it can see them, else along the tunnels to where they were
				if (CanSee(p)) { _lastKnown = p; _unseenT = 0f; _route.Clear(); _route.Add(p); }
				else
				{
					_unseenT += dt;
					if (_route.Count == 0) RouteTo(_lastKnown);
					if (_unseenT > 4.5f) { GD.Print("[wendigo] lost them - searching"); Pick(Mode.Search); _searchLeft = 14f; _searchStops = 0; RouteTo(_lastKnown); break; }
				}
				Walk(ChaseSpeed * (State == Mode.Ambush ? 1.08f : 1f), dt);
				if (dist < KillReach && !PlayerDeath.Dying && (!_phase2 || State == Mode.Ambush || _rng.Randf() < 0.5f)) _ = Cutscene.Run(this, ct => Catch(ct), lockInput: true, freezeBody: true);
				break;
			case Mode.Flee:
				Walk(FleeSpeed, dt);
				if (_route.Count == 0) { Pick(Mode.Hide); _modeT = 0f; }
				break;
			case Mode.Hide:
				// keeps still a while, then creeps back in toward them
				if (_modeT > _rng.RandfRange(9f, 16f)) { Pick(Mode.Stalk); RouteTo(L.Nodes[RandomNodeNear(p, 2)]); }
				break;
			case Mode.Stalk:
				Walk(WalkSpeed * 1.1f, dt);
				// close, and they're looking away: it comes
				if (dist < 11f && !PlayerLooksAt() && CanSee(p)) { GD.Print("[wendigo] ambush - it comes at them from behind"); Pick(Mode.Ambush); Shriek(); }
				else if (dist < 15f && PlayerLooksAt() && CanSee(p)) { Pick(Mode.Flee); FleeFrom(p); }
				else if (_route.Count == 0) RouteTo(L.Nodes[RandomNodeNear(p, 1)]);
				break;
		}
	}

	private bool PlayerLooksAt()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return false;
		var to = (Body.GlobalPosition + Vector3.Up * 2.5f - cam.GlobalPosition).Normalized();
		return (-cam.GlobalBasis.Z).Dot(to) > 0.75f;
	}

	private int RandomNodeNear(Vector3 at, int hops)
	{
		int v = L.NearestConnected(at);
		for (int i = 0; i < hops; i++) { var adj = L.Adj[v]; v = adj[_rng.RandiRange(0, adj.Count - 1)]; }
		return v;
	}

	private void FleeFrom(Vector3 p)
	{
		int best = L.NearestConnected(Pos);
		float bd = 0f;
		for (int i = 0; i < 16; i++)
		{
			int v = _rng.RandiRange(0, L.Nodes.Count - 1);
			float d = L.Nodes[v].DistanceTo(p) - L.Nodes[v].DistanceTo(Pos) * 0.3f;
			if (d > bd) { bd = d; best = v; }
		}
		RouteTo(L.Nodes[best]);
	}

	/// <summary>Along its route at <paramref name="speed"/>, turning toward where it's going.</summary>
	private void Walk(float speed, float dt)
	{
		if (_route.Count == 0) return;
		var pos = Pos;
		var to = (_route[0] - pos) with { Y = 0 };
		float d = to.Length();
		if (d < 0.6f) { _route.RemoveAt(0); return; }
		var dir = to / d;
		var np = pos + dir * Mathf.Min(speed * dt, d);
		Body.GlobalPosition = _cave.ToGlobal(np with { Y = 0f });
		var want = Basis.LookingAt(_cave.GlobalBasis * dir, Vector3.Up);
		Body.GlobalBasis = new Basis(Body.GlobalBasis.GetRotationQuaternion().Slerp(want.GetRotationQuaternion(), 1f - Mathf.Exp(-dt * 5f)));
		_stepT -= dt * speed;
		if (_stepT <= 0f)
		{
			_stepT = 1.15f;
			AudioDirector.OneShot(this, "wendigo_step", 6, Body.GlobalPosition, speed > 3.5f ? -4f : -12f, "Events", 4f, 0.08f);
		}
	}

	private void Sound(float dt)
	{
		_breathT -= dt;
		if (_breathT > 0f) return;
		_breathT = _rng.RandfRange(4f, 9f);
		// its noises: a click in its throat, a stolen voice muttering, now and then a far howl down the tunnels
		float r = _rng.Randf();
		if (State is Mode.Hide) return;
		if (r < 0.45f) AudioDirector.OneShot(this, "wendigo_mumble", 4, Body.GlobalPosition + Vector3.Up * 3f, -8f, "Voice", 5f, 0.05f);
		else if (r < 0.75f) AudioDirector.OneShot(this, "wendigo_mimic", 14, Body.GlobalPosition + Vector3.Up * 3f, -10f, "Voice", 5f, 0f);
		else AudioDirector.OneShot(this, "wendigo_howl_far", 3, Body.GlobalPosition + Vector3.Up * 3f, -6f, "Unnatural", 12f, 0.03f);
	}

	private void Shriek() => AudioDirector.OneShot(this, "wendigo_howl_03", 1, Body.GlobalPosition + Vector3.Up * 3f, 4f, "Unnatural", 10f, 0.03f);

	// ------------------------------------------------------------------ the fire

	/// <summary>The flamethrower's fire on it: enough of it (a third of a second in the stream) is a hit; it burns, shrieks,
	/// and runs. The last hit finishes it.</summary>
	private void Burnt(float dt)
	{
		if (Flamethrower.Instance?.Stream is not { } s || _hitCool > 0f) { _exposure = 0f; return; }
		float heat = s.Heat(Body.GlobalPosition + Vector3.Up * 1.6f, 1.4f) + s.Heat(Body.GlobalPosition + Vector3.Up * 0.7f, 1.2f);
		_exposure = heat > 0.5f ? _exposure + dt : Mathf.Max(0f, _exposure - dt * 0.5f);
		if (_exposure < 0.32f) return;
		_exposure = 0f;
		_hitCool = 2.6f;
		Hits++;
		_phase2 = true;
		Hit?.Invoke(Hits);
		GD.Print($"[wendigo] burned: hit {Hits} of {HitsToKill}");
		Body.Char(Mathf.Clamp(Hits / (float)HitsToKill, 0f, 1f));
		AudioDirector.OneShot(this, "wendigo_howl_04", 1, Body.GlobalPosition + Vector3.Up * 3f, 7f, "Unnatural", 12f, 0.02f);
		if (_burn == null || !IsInstanceValid(_burn))
		{
			_burn = new FireVfx { Name = "Burning", Extent = new Vector3(0.6f, 1.2f, 0.5f), FlameScale = 0.7f, LightRange = 6f, LightEnergy = 1.6f, LightShadows = false, Haze = false, Position = new Vector3(0, 1.4f, 0) };
			Body.AddChild(_burn);
		}
		_burn.Intensity = 1f;
		if (Hits >= HitsToKill) { Dead = true; State = Mode.Off; Killed?.Invoke(); return; }
		Pick(Mode.Burning);
		var tw = CreateTween();
		tw.TweenInterval(1.6f);
		tw.TweenCallback(Callable.From(() => { if (State == Mode.Burning) { Pick(Mode.Flee); FleeFrom(PlayerLocal); } }));
		tw.TweenInterval(3f);
		tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(_burn)) _burn.Intensity = 0.25f; }));
	}

	// ------------------------------------------------------------------ the catch

	private async Task Catch(CancellationToken ct)
	{
		if (PlayerDeath.Dying) return;
		State = Mode.Kill;
		PlayerDeath.Begin();
		CaughtPlayer?.Invoke();
		GD.Print("[wendigo] it has them");
		var cam = _player.CameraRig;
		var head = Body.GlobalPosition + Vector3.Up * 2.3f;
		// the view snapped round onto it (fast, but eased), it lunges in over them, black
		AudioDirector.OneShot(this, "wendigo_howl_03", 1, head, 6f, "Unnatural", 8f, 0.02f);
		var from = Body.GlobalPosition;
		var to = _player.GlobalPosition + (from - _player.GlobalPosition).Normalized() * 0.9f;
		Body.Play("pounce", 0.5f, 0.05);
		double t = 0;
		while (t < 0.55)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			Body.GlobalPosition = from.Lerp(to, (float)Mathf.Min(1.0, t / 0.4));
			var d = Body.GlobalPosition + Vector3.Up * 2.6f - cam.Camera.GlobalPosition;
			_player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(cam.Yaw, Mathf.Atan2(-d.X, -d.Z)) * Mathf.Min(1f, dt * 10f), 0));
			cam.SetPitch(Mathf.Lerp(cam.Pitch, Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), Mathf.Min(1f, dt * 8f)));
		}
		StoryBeat.Fader(this)?.SetBlack(true);
		await PlayerDeath.Reload(this, "It found you.", ct);
	}
}
