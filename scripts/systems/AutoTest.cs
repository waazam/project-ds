using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;

namespace ProjectDS.Systems;

/// <summary>
/// Scripted playthrough for `-- --autotest`. It drives PlayerInput (never the
/// body directly) along the trail to the top of the stairs. On the way it
/// checks movement, running, the camera orbit, collision, footsteps, and the
/// silence curve. It writes a report, CSV telemetry, and screenshots to
/// test-output/, then quits with exit code 1 if any check failed.
///
/// Route: the Path3D in group "trail" (terrain height ignored), then the
/// "stairs_top_trigger". An optional "autotest_obstacle" node gets walked into.
/// </summary>
public partial class AutoTest : Node
{
	private const string OutDir = "res://test-output";
	private PlayerController _player;
	private PlayerInput _input;
	private GameFlow _flow;
	private ForestAmbienceManager _amb;
	private readonly List<(string name, bool ok, string detail)> _checks = new();
	private readonly StringBuilder _csv = new("t,x,y,z,speed,running,silence,birds_db,insects_db,wind_db,distant_db,bird_calls,steps,surface,fps\n");
	private double _t;
	private double _nextSample;
	private int _shot;
	private float _fpsSum; private int _fpsCount; private float _fpsMin = 999;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutDir));
		// Keep the editor from importing screenshots as project assets.
		using (FileAccess.Open($"{OutDir}/.gdignore", FileAccess.ModeFlags.Write)) { }
		_ = Run();
	}

	public override void _Process(double delta)
	{
		_t += delta;
		if (_player == null || _t < _nextSample) return;
		_nextSample = _t + 0.5;
		float fps = (float)Engine.GetFramesPerSecond();
		if (_t > 5) { _fpsSum += fps; _fpsCount++; _fpsMin = Mathf.Min(_fpsMin, fps); }
		var p = _player.GlobalPosition;
		var steps = _player.GetNode<PlayerFootsteps>("Footsteps");
		var birds = GetTree().Root.FindChild("BirdCalls", true, false) as BirdCallEmitter;
		_csv.Append($"{_t:0.0},{p.X:0.00},{p.Y:0.00},{p.Z:0.00},{_player.GroundSpeed:0.00},{_player.IsRunning},{_amb?.Silence ?? 0:0.000}," +
			$"{Db("Birds"):0.0},{Db("Insects"):0.0},{Db("Wind"):0.0},{Db("Distant"):0.0},{birds?.CallsPlayed ?? 0},{steps.StepsPlayed},{steps.LastSurface},{fps}\n");
	}

	private static float Db(string bus) => AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(bus));

	private void Check(string name, bool ok, string detail = "")
	{
		_checks.Add((name, ok, detail));
		GD.Print($"[autotest] {(ok ? "PASS" : "FAIL")} {name} {detail}");
	}

	private async Task Wait(double seconds) =>
		await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);

	private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

	private void Screenshot(string label)
	{
		var img = GetViewport().GetTexture().GetImage();
		img.SavePng(ProjectSettings.GlobalizePath($"{OutDir}/{_shot++:00}_{label}.png"));
	}

	private async Task Run()
	{
		await Frame();
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_flow = GetTree().Root.FindChild("GameFlow", true, false) as GameFlow;
		_amb = ForestAmbienceManager.Instance;
		if (_player == null || _flow == null) { Check("scene has player + GameFlow", false); Finish(); return; }
		_input = _player.PlayerInput;
		_input.Scripted = true;

		while (!_flow.Started) await Frame();
		await Wait(1.5);
		Check("player spawned on ground", _player.IsOnFloor(), $"pos {_player.GlobalPosition}");
		Screenshot("spawn");

		// Audio sanity at the start: living forest.
		await Wait(2.0);
		Check("forest alive at start", (_amb?.Silence ?? 1) < 0.05f && Db("Birds") > -1f && Db("Insects") > -1f,
			$"silence {_amb?.Silence:0.00} birds {Db("Birds"):0.0}dB");

		// Camera orbit: a full turn of yaw.
		float yaw0 = _player.CameraRig.Yaw;
		for (int i = 0; i < 60; i++) { _input.AddScriptedLook(new Vector2(Mathf.Tau / 120f, 0)); await Frame(); }
		Screenshot("orbit_half");
		for (int i = 0; i < 60; i++) { _input.AddScriptedLook(new Vector2(Mathf.Tau / 120f, 0)); await Frame(); }
		float yawErr = Mathf.Abs(Mathf.AngleDifference(yaw0, _player.CameraRig.Yaw));
		Check("camera orbits 360 and returns", yawErr < 0.05f, $"err {yawErr:0.000}");
		_input.AddScriptedLook(new Vector2(0, -0.25f)); await Frame();
		_input.AddScriptedLook(new Vector2(0, 0.25f));

		// Walk and run speeds (camera-relative: forward = camera forward).
		var start = _player.GlobalPosition;
		await Drive(new Vector2(0, 1), false, 2.5);
		float walk = _player.GroundSpeed;
		await Drive(new Vector2(0, 1), true, 2.5);
		float run = _player.GroundSpeed;
		Check("walks", walk > 1.5f && walk < 2.3f, $"{walk:0.00} m/s");
		Check("runs", run > 4.0f && run < 5.2f, $"{run:0.00} m/s");
		Check("moved from spawn", start.DistanceTo(_player.GlobalPosition) > 8f, $"{start.DistanceTo(_player.GlobalPosition):0.0} m");
		await Drive(new Vector2(-1, 0), false, 1.0);   // strafe
		await Drive(new Vector2(0, -1), false, 1.0);   // back toward camera
		await Drive(Vector2.Zero, false, 1.0);
		Check("decelerates to stop", _player.GroundSpeed < 0.05f, $"{_player.GroundSpeed:0.00}");

		await ObstacleTest();

		// Follow the trail.
		var route = BuildRoute();
		Check("trail route found", route.Count > 3, $"{route.Count} points");
		float travelled = 0; var last = _player.GlobalPosition;
		float nextShotAt = 30f;
		int birdsAtStart = (GetTree().Root.FindChild("BirdCalls", true, false) as BirdCallEmitter)?.CallsPlayed ?? 0;
		for (int i = 0; i < route.Count; i++)
		{
			bool ok = await GoTo(route[i], i == route.Count - 1 ? 0.35f : 2.0f);
			if (!ok) { Check($"reached waypoint {i}", false, $"stuck at {_player.GlobalPosition} going to {route[i]}"); break; }
			travelled += last.DistanceTo(_player.GlobalPosition); last = _player.GlobalPosition;
			if (travelled >= nextShotAt) { Screenshot($"trail_{(int)travelled}m"); nextShotAt += 45f; }
		}
		Check("trail walked", travelled > 50f, $"{travelled:0} m");
		int birdCalls = ((GetTree().Root.FindChild("BirdCalls", true, false) as BirdCallEmitter)?.CallsPlayed ?? 0) - birdsAtStart;
		Check("bird calls happened on the way", birdCalls > 5, $"{birdCalls} calls");

		// On the stairs: the silence should be near total.
		Screenshot("stairs_top");
		await Drive(Vector2.Zero, false, 0.5);
		var steps = _player.GetNode<PlayerFootsteps>("Footsteps");
		Check("footsteps played", steps.StepsPlayed > 50, $"{steps.StepsPlayed}");
		Check("wood footsteps on stairs", steps.LastSurface == "wood", steps.LastSurface);
		Check("near-total silence at stairs", (_amb?.Silence ?? 0) > 0.9f, $"silence {_amb?.Silence:0.00}");
		Check("birds + insects gone", Db("Birds") < -60f && Db("Insects") < -60f, $"{Db("Birds"):0} / {Db("Insects"):0} dB");
		Check("wind almost gone", Db("Wind") < -25f, $"{Db("Wind"):0.0} dB");

		// The slice should end after standing on top.
		double wait = 0;
		while (!_flow.EndReached && wait < 8) { await Wait(0.25); wait += 0.25; }
		Check("slice ends on the top step", _flow.EndReached, $"after {wait:0.0}s");
		await Wait(6.0);
		Screenshot("end_card");

		if (_fpsCount > 0) Check("performance", _fpsSum / _fpsCount > 55f, $"avg {_fpsSum / _fpsCount:0} min {_fpsMin:0} fps");
		Finish();
	}

	private async Task Drive(Vector2 move, bool run, double seconds)
	{
		_input.ScriptedMove = move; _input.ScriptedRun = run;
		await Wait(seconds);
		_input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false;
	}

	private async Task ObstacleTest()
	{
		if (GetTree().GetFirstNodeInGroup("autotest_obstacle") is not Node3D obstacle) return;
		// Face the obstacle and push into it for 3 s. The body must stop at its surface.
		await FaceTarget(obstacle.GlobalPosition);
		float before = Flat(_player.GlobalPosition).DistanceTo(Flat(obstacle.GlobalPosition));
		_input.ScriptedMove = new Vector2(0, 1);
		float closest = before;
		for (double t = 0; t < before / 1.9 + 3; t += 1.0 / 60)
		{
			SteerCamera(obstacle.GlobalPosition);
			await Frame();
			closest = Mathf.Min(closest, Flat(_player.GlobalPosition).DistanceTo(Flat(obstacle.GlobalPosition)));
		}
		_input.ScriptedMove = Vector2.Zero;
		Screenshot("collision");
		Check("collides with obstacle", closest > 0.2f && _player.GroundSpeed < 0.6f, $"closest {closest:0.00} m to centre");
		await Wait(0.5);
	}

	private async Task FaceTarget(Vector3 target)
	{
		for (int i = 0; i < 30; i++) { SteerCamera(target); await Frame(); }
	}

	private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

	/// <summary>Turns the camera toward a point; forward input then walks there, like a player would.</summary>
	private void SteerCamera(Vector3 target)
	{
		var to = target - _player.GlobalPosition;
		float want = Mathf.Atan2(-to.X, -to.Z);
		float diff = Mathf.AngleDifference(_player.CameraRig.Yaw, want);
		_input.AddScriptedLook(new Vector2(Mathf.Clamp(diff, -0.08f, 0.08f), 0));
	}

	private async Task<bool> GoTo(Vector3 target, float radius)
	{
		double stuckTimer = 0; float bestDist = float.MaxValue;
		while (true)
		{
			float d = Flat(_player.GlobalPosition).DistanceTo(Flat(target));
			if (d < radius) return true;
			if (d < bestDist - 0.3f) { bestDist = d; stuckTimer = 0; }
			stuckTimer += 1.0 / 60;
			if (stuckTimer > 4) return false;
			SteerCamera(target);
			// Run until the forest starts to hush, then walk like a nervous person.
			float s = _amb?.Silence ?? 0;
			_input.ScriptedRun = s < 0.15f;
			_input.ScriptedMove = new Vector2(0, 1);
			await Frame();
		}
	}

	private List<Vector3> BuildRoute()
	{
		var route = new List<Vector3>();
		if (GetTree().GetFirstNodeInGroup("trail") is Path3D trail && trail.Curve != null)
		{
			var pts = trail.Curve.GetBakedPoints();
			float acc = 0; Vector3 prev = pts.Length > 0 ? pts[0] : Vector3.Zero;
			// Waypoints every ~6 m, starting from the nearest point ahead of the player.
			var all = new List<Vector3>();
			foreach (var p in pts) { acc += p.DistanceTo(prev); prev = p; if (acc >= 6f || all.Count == 0) { all.Add(trail.GlobalTransform * p); acc = 0; } }
			all.Add(trail.GlobalTransform * pts[^1]);
			int nearest = 0; float best = float.MaxValue;
			for (int i = 0; i < all.Count; i++)
			{
				float d = Flat(all[i]).DistanceTo(Flat(_player.GlobalPosition));
				if (d < best) { best = d; nearest = i; }
			}
			route.AddRange(all.Skip(nearest));
		}
		if (GetTree().GetFirstNodeInGroup("stairs_top_trigger") is Node3D top)
		{
			// Approach the stairs from the front (the base is on the trigger's +Z side), then climb.
			var stairsBase = top.GetParentOrNull<Node3D>()?.FindChild("AutotestApproach", true, false) as Node3D;
			if (stairsBase != null) route.Add(stairsBase.GlobalPosition);
			route.Add(top.GlobalPosition);
		}
		return route;
	}

	private void Finish()
	{
		int failed = _checks.Count(c => !c.ok);
		var sb = new StringBuilder($"Project DS autotest  {Time.GetDatetimeStringFromSystem()}\n{_checks.Count - failed}/{_checks.Count} passed\n\n");
		foreach (var c in _checks) sb.Append($"{(c.ok ? "PASS" : "FAIL")}  {c.name}  {c.detail}\n");
		using (var f = FileAccess.Open($"{OutDir}/autotest_report.txt", FileAccess.ModeFlags.Write)) f.StoreString(sb.ToString());
		using (var f = FileAccess.Open($"{OutDir}/autotest_telemetry.csv", FileAccess.ModeFlags.Write)) f.StoreString(_csv.ToString());
		GD.Print(sb.ToString());
		GetTree().Quit(failed == 0 ? 0 : 1);
	}
}
