using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// `-- --autotest --measure-walks --story-from=N --story-to=N`: instead of act N's story steps, times its long walks
/// (the stairs and the halls), once at a walk and once holding run the whole way (stamina as in a release build: it
/// drains, runs out, and comes back), with the same scripted input a player's keys would give. Each walk follows the
/// way's own centre line, turning at its corners like a player who knows the way. Each time is reported as a check
/// ("walk: ... s; sprint: ... s"), with the slowest speed seen while pushing on and any jump in place (a loop-back
/// teleport) on the way, so "running never progresses" shows up as a failed check: running must always get there
/// at least as soon as walking.
///
/// Acts with a probe: 11 (the last climb), 14 (the stairwell), 15 (the corridor and the red-light hall), 17 (the
/// sewer pipe), 21 (the long stair). Other acts' steps are skipped in this mode.
/// </summary>
public partial class StoryTest
{
	private static bool MeasureWalks => OS.GetCmdlineUserArgs().Contains("--measure-walks");

	/// <summary>The probe that stands in for <paramref name="step"/> in --measure-walks, or a no-op.</summary>
	private Func<CancellationToken, Task> MeasureFor(StepDef step) => ActOf(step) switch
	{
		11 when step.Act.StartsWith("Act 11") => MeasureAct11,
		14 => MeasureAct14,
		15 => MeasureAct15,
		17 => MeasureAct17,
		21 => MeasureAct21,
		24 => async ct =>
		{
			// the maze is built by now: the winter world's clearance again, with it
			await WaitUntil(() => _input.Enabled, 20, ct);
			await Seconds(2.0, ct);
			CheckWinterClear(StationInterior.Instance?.Boss?.Library?.Round?.Stair);
		},
		_ => _ => Task.CompletedTask,
	};

	private ulong _walkPhys;

	private sealed record WalkResult(double Seconds, bool Arrived, float MinPushSpeed, int Jumps, float MinStamina, float Metres);

	/// <summary>Holds forward along <paramref name="path"/> (turning to each point in turn, flat), running or not, until the
	/// last point is reached or <paramref name="done"/>; standing still while <paramref name="hold"/>. Game seconds.</summary>
	private async Task<WalkResult> Traverse(IList<Vector3> path, bool run, CancellationToken ct, Func<bool> done = null, Func<bool> hold = null,
		float reach = 0.5f, double giveUp = 900, Func<Vector3, Vector3> avoid = null)
	{
		double t = 0, pushT = 0;
		int i = 0, jumps = 0;
		float minPush = 99f, minStam = 1f, metres = 0f;
		Vector3 last = _player.GlobalPosition;
		bool arrived = false;
		ulong realStart = Time.GetTicksMsec();
		_walkPhys = Engine.GetPhysicsFrames();
		try
		{
			while (t < giveUp)
			{
				ct.ThrowIfCancellationRequested();
				while (i < path.Count && Flat(path[i] - _player.GlobalPosition).Length() < reach) i++;
				if (i >= path.Count || (done != null && done())) { arrived = true; break; }
				bool stand = !_input.Enabled || (hold != null && hold());
				if (!stand)
				{
					Vector3 target = avoid != null ? avoid(path[i]) : path[i];
					var d = Flat(target - _player.GlobalPosition);
					if (d.LengthSquared() > 0.0001f) _player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
					_input.ScriptedMove = new Vector2(0, 1);
					_input.ScriptedRun = run;
				}
				else { _input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false; }
				await Frames(1, ct);
				// game time is the physics clock's (the body moves by it): on a slow frame the process delta is clamped
				// and undercounts it (seen at half speed on the test machine)
				ulong pf = Engine.GetPhysicsFrames();
				double dt = (pf - _walkPhys) / (double)Engine.PhysicsTicksPerSecond * Engine.TimeScale;
				_walkPhys = pf;
				t += dt;
				Vector3 now = _player.GlobalPosition;
				float step = now.DistanceTo(last);
				if (step > 2.5f) jumps++; else metres += Flat(now - last).Length();
				last = now;
				// the slowest it went while pushing on (after the first second's run-up, and not mid-cutscene)
				if (!stand && _input.Enabled) { pushT += dt; if (pushT > 1.0) minPush = Mathf.Min(minPush, _player.GroundSpeed); }
				else pushT = 0;
				if (_player.Stamina != null) minStam = Mathf.Min(minStam, _player.Stamina.Value);
				if ((int)(t / 5.0) != (int)((t - dt) / 5.0) || (t < 6.0 && (int)t != (int)(t - dt)))
					GD.Print($"[walks]   t {t:0.0} s (real {(Time.GetTicksMsec() - realStart) / 1000.0:0.0}, scale {Engine.TimeScale:0.0}), {metres:0.0} m, point {i}/{path.Count}, speed {_player.GroundSpeed:0.00}, run {_player.IsRunning}, stamina {_player.Stamina?.Value:0.00}, wade {_player.WadeScale:0.00}, at {_player.GlobalPosition}");
			}
		}
		finally { _input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false; }
		return new WalkResult(t, arrived, minPush, jumps, minStam, metres);
	}

	/// <summary>Stands still until the stamina is full again (a sprint starts fresh).</summary>
	private async Task Rest(CancellationToken ct)
	{
		_input.ScriptedMove = Vector2.Zero;
		await Seconds(0.5, ct);
		await WaitUntil(() => _player.Stamina == null || _player.Stamina.Value > 0.999f, 12, ct);
	}

	/// <summary>Reports a walk and a sprint of the same way as one check: both arrived, the sprint no slower than the walk.</summary>
	private void ReportWalk(string name, WalkResult walk, WalkResult sprint)
	{
		string W(WalkResult r) => $"{r.Seconds:0.0} s ({r.Seconds / 60.0:0.00} min) over {r.Metres:0} m{(r.Arrived ? "" : " NOT ARRIVED")}, slowest {(r.MinPushSpeed > 50 ? 0 : r.MinPushSpeed):0.0} m/s pushing on, {r.Jumps} jumps, stamina low {r.MinStamina:0.00}";
		bool ok = walk.Arrived && sprint.Arrived && sprint.Seconds <= walk.Seconds + 0.5 && sprint.Jumps == 0 && walk.Jumps == 0;
		Check($"[walks] {name}", ok, $"walk: {W(walk)} | sprint: {W(sprint)}");
		GD.Print($"[walks] {name}: walk {walk.Seconds:0.0} s, sprint {sprint.Seconds:0.0} s");
	}

	private void Place(Vector3 at, Vector3 faceToward)
	{
		var d = Flat(faceToward - at);
		_player.Teleport(at + Vector3.Up * 0.1f, d.LengthSquared() > 0.01f ? Mathf.Atan2(-d.X, -d.Z) : _player.CameraRig.Yaw);
		_lastPos = _player.GlobalPosition;
	}

	// ------------------------------------------------------------------ Act 11: the last climb

	private async Task MeasureAct11(CancellationToken ct)
	{
		Engine.TimeScale = 3.0;
		await WaitUntil(() => StoryManager.Instance.Act11DialogueDone, 40, ct);
		Engine.TimeScale = 1.0;
		var act11 = AllOf<Act11Ending>().FirstOrDefault();
		var stairs = act11?.OriginalStairs;
		if (stairs == null || !act11.StairsTall) { Check("[walks] Act 11: the tall staircase", false, "not built"); return; }
		Vector3 foot = stairs.ToGlobal(new Vector3(0, 0.05f, 2.0f));
		Vector3 top = stairs.ToGlobal(new Vector3(0, stairs.TotalHeight + 0.05f, (stairs.TopFrontZ + stairs.BackZ) * 0.5f));
		var path = new List<Vector3> { stairs.ToGlobal(new Vector3(0, 0, 0.6f)), stairs.ToGlobal(new Vector3(0, 0, -1f)), top };
		// (the thing from the bunker waits at the foot and shoves anyone near it toward the stairs: timing the climb, it's
		// held still, so a shove doesn't spin the walk off the side of the flight)
		if (act11.Pursuer != null) { act11.Pursuer.ProcessMode = ProcessModeEnum.Disabled; act11.Pursuer.GlobalPosition += new Vector3(0, -300f, 0); }
		var results = new WalkResult[2];
		for (int pass = 0; pass < 2; pass++)
		{
			// a fresh flight each time: the one-way wall from the last pass stands at the top
			stairs.GetNodeOrNull<OneWayFlight>("OneWay")?.Detach();
			await Frames(2, ct);
			OneWayFlight.Attach(stairs);
			Place(foot, top);
			await Rest(ct);
			// (a shove from it, if it came: let it finish, and start again at the foot)
			await WaitUntil(() => _input.Enabled, 10, ct);
			Place(foot, top);
			await Frames(3, ct);
			GD.Print($"[walks] Act 11 pass {pass} start: local {stairs.ToLocal(_player.GlobalPosition)}, input {_input.Enabled}, pursuer {act11.Pursuer?.GlobalPosition}");
			results[pass] = await Traverse(path, pass == 1, ct, () => act11.Progress >= 0.995f, reach: 0.8f, giveUp: 300);
			GD.Print($"[walks] Act 11 pass {pass}: progress {act11.Progress:0.00}, {_player.GlobalPosition}");
		}
		ReportWalk($"Act 11: the last staircase ({stairs.Steps} steps, {stairs.Steps * stairs.Run:0} m run, {stairs.TotalHeight:0} m up)", results[0], results[1]);
	}

	// ------------------------------------------------------------------ Act 14: the stairwell

	private async Task MeasureAct14(CancellationToken ct)
	{
		var sw = StationInterior.Instance?.Room3?.Stairs;
		if (sw == null) { Check("[walks] Act 14: the stairwell", false, "not built"); return; }
		await WaitUntil(() => _input.Enabled, 10, ct);
		await Frames(5, ct);
		await WalkTo(sw.TrenchTopWorld, 0.6f, ct, giveUp: 12f);
		var path = new List<Vector3>();
		for (int k = 0; k <= sw.GapCorner; k++) path.Add(sw.CornerWorld(k));
		var results = new WalkResult[2];
		var musicAt = new float[2];
		for (int pass = 0; pass < 2; pass++)
		{
			// (the question at the edge from the last pass: stepped back from)
			if (ChoicePrompt.Instance is { IsOpen: true }) ChoicePrompt.Instance.TestChoose(-1);
			await WaitUntil(() => !sw.Choosing && _input.Enabled, 8, ct);
			await Seconds(0.5, ct);
			Place(sw.TrenchTopWorld, sw.CornerWorld(0));
			await Frames(5, ct);
			GD.Print($"[walks] Act 14 pass {pass} start at {_player.GlobalPosition}, choosing {sw.Choosing}, input {_input.Enabled}");
			sw.ResetDescentMusicForTests();   // (its music again on each way down: where it is at the edge, walking and running)
			await Rest(ct);
			// from the top of the trench to the last landing above the fallen flight
			results[pass] = await Traverse(path, pass == 1, ct, reach: 0.45f, giveUp: 900);
			GD.Print($"[walks] Act 14 pass {pass}: deepest turn {sw.DeepestRev}, crawler caught {sw.CrawlerCatches}, music {sw.MusicPlaying}, faded at the edge {sw.MusicEndedAt:0.0} s into the track");
			musicAt[pass] = sw.MusicEndedAt;
		}
		ReportWalk($"Act 14: the stairwell ({sw.Revolutions} turns, {sw.Flights} flights, {-Stairwell.CornerY(sw.GapCorner):0} m down)", results[0], results[1]);
		if (ResourceLoader.Exists("res://assets/audio/music/stairwell_descent.mp3"))
		{
			Check("[walks] Act 14: the descent's music", true, $"{GD.Load<AudioStream>("res://assets/audio/music/stairwell_descent.mp3").GetLength():0.0} s long, started {sw.DescentMusicOffset:0.0} s in");
			// (Dan, 2026-10-13: its build lands at the bottom; the edge fades it wherever it is. The build is 64-95 s in)
			Check("[walks] Act 14: walking, the music is at the end of its build at the edge", musicAt[0] is > 86f and < 100f, $"{musicAt[0]:0.0} s into the track");
			Check("[walks] Act 14: running, the edge fades it too (in the build)", musicAt[1] is > 55f and < 100f, $"{musicAt[1]:0.0} s into the track");
		}
	}

	// ------------------------------------------------------------------ Act 15: the corridor and the hall

	private async Task MeasureAct15(CancellationToken ct)
	{
		var sw = StationInterior.Instance?.Room3?.Stairs;
		await WaitUntil(() => sw?.Hallway?.Shadow != null, 10, ct);
		var hw = sw?.Hallway;
		if (hw == null) { Check("[walks] Act 15: the hallway", false, "not built"); return; }
		await WaitUntil(() => _input.Enabled, 10, ct);
		await WalkTo(sw.ChamberExitWorld, 0.6f, ct, giveUp: 10f);
		await WalkTo(hw.AlongWorld(1f), 0.6f, ct, giveUp: 10f);
		// the narrow corridor and its taper, up to just short of the shadow man (the game hasn't started)
		float preShadow = Act15Hallway.ShadowZ - 3f;
		var corridor = new WalkResult[2];
		for (int pass = 0; pass < 2; pass++)
		{
			Place(hw.AlongWorld(1f), hw.AlongWorld(10f));
			await Rest(ct);
			corridor[pass] = await Traverse(new[] { hw.AlongWorld(preShadow) }, pass == 1, ct, () => hw.PlayerZ >= preShadow, reach: 0.4f);
		}
		ReportWalk($"Act 15: the narrow corridor and the taper ({preShadow - 1f:0} m)", corridor[0], corridor[1]);
		// the red-light hall, past him to the door, stopping in the flicker and the red
		Vector3 Round(Vector3 target)
		{
			Vector3 sl = hw.ToLocal(hw.Shadow.GlobalPosition);
			if (sl.Z > hw.PlayerZ - 1f && sl.Z < hw.PlayerZ + 6f && Mathf.Abs(sl.X) < 1.2f)
				return hw.ToGlobal(new Vector3(sl.X > 0 ? -1.2f : 1.2f, 0.05f, hw.PlayerZ + 3f));
			return target;
		}
		var hall = new WalkResult[2];
		int reds0 = hw.Reds;
		var reds = new int[2];
		for (int pass = 0; pass < 2; pass++)
		{
			await WaitUntil(() => hw.State is Act15Hallway.Phase.Waiting or Act15Hallway.Phase.Green, 15, ct);
			Place(hw.AlongWorld(preShadow), hw.AlongWorld(preShadow + 10f));
			await Rest(ct);
			int r0 = hw.Reds;
			var pts = new List<Vector3>();
			for (float z = preShadow + 6f; z < Act15Hallway.End - 1.5f; z += 6f) pts.Add(hw.AlongWorld(z));
			pts.Add(hw.AlongWorld(Act15Hallway.End - 1.5f));
			hall[pass] = await Traverse(pts, pass == 1, ct, () => hw.PlayerZ >= Act15Hallway.End - 1.6f,
				() => hw.State is Act15Hallway.Phase.Flicker or Act15Hallway.Phase.Red, reach: 1.0f, giveUp: 900, avoid: Round);
			reds[pass] = hw.Reds - r0;
		}
		ReportWalk($"Act 15: the red-light hall ({Act15Hallway.End - 1.5f - preShadow:0} m; reds walking {reds[0]}, running {reds[1]}; taken {hw.Reds - reds0 - reds[0] - reds[1]})", hall[0], hall[1]);
	}

	// ------------------------------------------------------------------ Act 17: the sewer pipe

	private async Task MeasureAct17(CancellationToken ct)
	{
		var sewer = StationInterior.Instance?.Sewer;
		await WaitUntil(() => sewer?.HoleUse != null, 10, ct);
		if (sewer == null) { Check("[walks] Act 17: the sewer", false, "not built"); return; }
		await WaitUntil(() => _input.Enabled, 10, ct);
		var results = new WalkResult[2];
		for (int pass = 0; pass < 2; pass++)
		{
			Place(sewer.EntranceWorld, sewer.AlongWorld(5f));
			await Rest(ct);
			results[pass] = await Traverse(new[] { sewer.AlongWorld(3f), sewer.TunnelEndWorld, sewer.PlatformEdgeWorld }, pass == 1, ct, reach: 0.8f);
		}
		ReportWalk($"Act 17: the sewer pipe and the cistern to the hole ({Sewer.TunnelLen:0} m pipe, wading)", results[0], results[1]);
	}

	// ------------------------------------------------------------------ Act 21: the long stair

	private async Task MeasureAct21(CancellationToken ct)
	{
		var rr = StationInterior.Instance?.Boss?.Library?.Round;
		await WaitUntil(() => rr?.Stair?.Church?.FontKeyPickup != null, 15, ct);
		var stair = rr?.Stair;
		if (stair == null) { Check("[walks] Act 21: the long stair", false, "not built"); return; }
		await WaitUntil(() => _input.Enabled, 10, ct);
		var results = new WalkResult[2];
		for (int pass = 0; pass < 2; pass++)
		{
			Place(stair.BottomWorld, stair.PointAt(0.05f));
			await Rest(ct);
			results[pass] = await Traverse(new[] { stair.PointAt(1f), stair.TopLandingWorld }, pass == 1, ct, () => stair.Progress(_player.GlobalPosition) >= 0.999f, reach: 0.6f);
		}
		ReportWalk($"Act 21: the long stair ({LongStair.Flights} flights, {LongStair.TopY:0} m up, {LongStair.TopZ0 - LongStair.StartZ:0} m along)", results[0], results[1]);
		CheckWinterClear(stair);
	}

	/// <summary>The winter world (the church, its woods, the lodge and the maze under it) hangs off the top of the long stair,
	/// so the stair's length moves all of it. Checks it stays clear of everything else built under the station: the winter
	/// ground well above every room, hall and pipe beneath it, and the maze inside none of them.</summary>
	private void CheckWinterClear(LongStair stair)
	{
		var station = StationInterior.Instance;
		var church = stair?.Church;
		if (station == null || church == null) return;
		bool Under(Node n, Node root) { for (var p = n; p != null; p = p.GetParent()) if (p == root) return true; return false; }
		bool Covered(float x, float z) => new Vector2(x - WinterGlade.Centre.X, z - WinterGlade.Centre.Y).Length() < WinterGlade.Radius
			|| SkiLodge.NearLodge(x, z, 90f) || WinterWoods.Nearest(x, z, out _, out _) < WinterWoods.RegionHalf;
		var others = new List<(Aabb box, string name)>();
		var maze = new List<Aabb>();
		var stack = new Stack<Node>();
		stack.Push(station);
		while (stack.Count > 0)
		{
			var n = stack.Pop();
			foreach (var c in n.GetChildren()) stack.Push(c);
			if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
			var box = mi.GlobalTransform * mi.GetAabb();
			if (box.Size.Length() > 3000f) continue;
			if (Under(mi, church)) { if (mi.GetPath().ToString().Contains("/SnowMaze/")) maze.Add(box); continue; }
			if (Under(mi, stair)) continue;   // the stair itself goes up under the crypt
			// what's built underground: the stairwell and the hallway (Room 3's), the sewer, the pit, the library and the round
			// room (the save lamps and R.H.'s pages are placed from the station but stand out in the winter world)
			if (!Under(mi, station.Room3) && !Under(mi, station.Sewer) && !Under(mi, station.Boss)) continue;
			others.Add((box, mi.GetPath().ToString().Replace(station.GetPath().ToString(), "")));
		}
		float worst = 9999f; string worstName = "-"; int samples = 0;
		foreach (var (box, name) in others)
		{
			float top = box.End.Y;
			for (float x = box.Position.X; x <= box.End.X + 0.01f; x += Mathf.Max(4f, box.Size.X / 12f))
			for (float z = box.Position.Z; z <= box.End.Z + 0.01f; z += Mathf.Max(4f, box.Size.Z / 12f))
			{
				Vector3 cl = church.ToLocal(new Vector3(x, top, z));
				if (!Covered(cl.X, cl.Z)) continue;
				samples++;
				float ground = church.ToGlobal(new Vector3(cl.X, WinterWoods.Height(cl.X, cl.Z), cl.Z)).Y;
				if (ground - top < worst) { worst = ground - top; worstName = name; }
			}
		}
		int overlaps = 0; string firstOverlap = "";
		var mazeBox = maze.Count > 0 ? maze.Aggregate((a, b) => a.Merge(b)) : new Aabb();
		if (maze.Count > 0)
			foreach (var (box, name) in others)
				if (mazeBox.Intersects(box)) { overlaps++; if (firstOverlap.Length == 0) firstOverlap = name; }
		Check("[walks] Act 21: the winter world stays clear of everything under the station", worst > 6f && overlaps == 0,
			$"church at {church.GlobalPosition.Round()}; the winter ground at least {worst:0.0} m above what's under it ({worstName}, {samples} samples); maze {(maze.Count > 0 ? $"{mazeBox.Position.Round()}..{mazeBox.End.Round()}" : "not built yet")}, overlapping {overlaps} ({firstOverlap})");
	}
}
