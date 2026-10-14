using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// Act 1's approach to the stairs, three ways (2026-10-13): `-- --autotest --act1-approach=normal|wrong|side
/// --test-out=act1/normal`. A frame every half second (and labelled ones at the beats) and a report go to the
/// --test-out folder. Not part of the full run: it checks that the falls are seen, that the stop to watch them happens
/// and lets go, that a player who goes the wrong way is waited for and turned back, and that the title over the stairs
/// waits until the stairs are close and in view.
///   normal: down the trail to the dead fir, it comes down as they look, they stop and watch, then on to the stairs.
///   wrong:  off the trail into the woods before the fir, then stood near it looking away (it waits), then looking (it
///           comes down); on round its crown, then off the way into the woods (a tree comes down across their way),
///           back to it, and on to the stairs.
///   side:   in from the woods to the side of the way, the fir never seen; on to the stairs, then beside them with
///           their back to them (no title), turning round to them (the title).
/// </summary>
public partial class StoryTest
{
	private static string Act1ApproachMode()
	{
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--act1-approach=")) return a["--act1-approach=".Length..];
		return null;
	}

	private bool _rec;
	private string _recLabel = "";
	private int _recN;

	private async Task Recorder(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _recN < 240)
		{
			await ToSignal(GetTree().CreateTimer(_recLabel == "watch" ? 0.25 : 0.5, true, false, true), SceneTreeTimer.SignalName.Timeout);
			if (!_rec || _s == null) continue;
			try { GetViewport()?.GetTexture()?.GetImage()?.SavePng(ProjectSettings.GlobalizePath($"{OutDir}/f{_recN++:000}_{_recLabel}.png")); }
			catch (Exception) { }
		}
	}

	private async Task Act1Approach(string mode, CancellationToken ct)
	{
		await WaitUntil(() => _input.Enabled, 30, ct);
		StoryManager.Instance?.SetFlag(ChapterCard.Flag(1));   // (the first chapter's card belongs to the trailhead, not to these frames)
		var terrain = GroundSnap.FindTerrain(this);
		var fir = AllOf<FallenTree>().FirstOrDefault();
		var falls = AllOf<Act1TreeFalls>().FirstOrDefault();
		var line = AllOf<FriendTrail>().FirstOrDefault();
		var climb = AllOf<FirstClimbEvent>().FirstOrDefault();
		var stairs = climb?.GetParent() as StaircaseBuilder;
		Check("the pieces are there", terrain != null && fir != null && falls != null && line != null && stairs != null);
		if (terrain == null || fir == null || falls == null || line == null || stairs == null) return;
		Vector3 G(Vector2 p) => new(p.X, terrain.HeightAt(p.X, p.Y), p.Y);
		Vector3 L(float s, float off = 0f) { var p = line.At(s, out var d); return G(p + new Vector2(d.Y, -d.X) * off); }
		Vector3 firFoot = fir.Foot;
		bool Cracked() => fir.Fallen || (fir.Falling && !fir.Held);
		string flag = ChapterCard.Flag(2);
		float revealDist = -1f, flagDist = -1f;
		bool titleEarly = false;
		// watch the title the whole time: when it's set, how far the stairs were
		async Task WatchTitle()
		{
			while (!ct.IsCancellationRequested)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (_s == null) return;
				float d = _player.CameraRig.Camera.GlobalPosition.DistanceTo(stairs.GlobalPosition + Vector3.Up * 2f);
				if (revealDist < 0 && climb.Revealed) revealDist = climb.RevealedAt;
				if (flagDist < 0 && StoryManager.Instance.HasFlag(flag)) { flagDist = d; titleEarly = !climb.Revealed; }
			}
		}
		_ = WatchTitle();
		_ = Recorder(ct);
		GD.Print($"[act1] fir foot {firFoot}, lie {fir.LieSegment.a} -> {fir.LieSegment.b}; line {line.Length:0} m from {L(0)} to {L(line.Length)}; stairs {stairs.GlobalPosition}; trail end {terrain.TrailPoint(terrain.TrailLength, out _)}");
		float trailLen = terrain.TrailLength;
		var start = terrain.TrailPoint(trailLen - 30f, out var tangent);
		var trailEnd = terrain.TrailPoint(trailLen, out _);

		if (mode == "normal")
		{
			await Teleport(start, start + tangent * 10f, ct);
			await Aim(firFoot + Vector3.Up * 6f, ct);
			_rec = true; _recLabel = "approach";
			await WalkTo(trailEnd, 1.5f, ct, stopWhen: Cracked, giveUp: 20f);
			await WatchTheFall(fir, falls, ct);
		}
		else if (mode == "wrong")
		{
			// off the trail into the woods, before the fir: it creaks (they're near), and waits
			await Teleport(start, start + tangent * 10f, ct);
			_rec = true; _recLabel = "offtrail";
			var side = new Vector3(-tangent.Z, 0, tangent.X);
			var off = terrain.TrailPoint(trailLen - 16f, out _) + side * 20f;
			await WalkTo(off, 2f, ct, giveUp: 12f);
			await Aim(off + side * 10f + Vector3.Up * 1.5f, ct);   // looking further off, away from the fir
			await Seconds(5, ct);
			Check("off the trail, near the fir but not looking at it: it hasn't come down", !Cracked(), $"fir {(fir.Standing ? "standing" : fir.Held ? "creaking, held" : "falling")}, {Flat(_player.GlobalPosition).DistanceTo(Flat(firFoot)):0} m off");
			// stood still near it, looking back up the trail: it creaks and waits
			_recLabel = "stand_away";
			// (they come back to the trail's end from the woods with their back to the fir: put there, looking away)
			var stand = trailEnd - tangent * 9f;
			await Teleport(stand, stand - tangent * 20f, ct);
			await Aim(stand - tangent * 20f + Vector3.Up * 1.5f, ct);
			float d0 = Flat(_player.GlobalPosition).DistanceTo(Flat(firFoot));
			await Seconds(4, ct);
			Check("stood near it looking away: it creaks, and waits for them to look", fir.Held && !Cracked(), $"held {fir.Held}, {d0:0} m off, creaking {fir.CreakTime:0.0} s");
			// turning round to it (a person's turn, not a snap)
			_recLabel = "turn_to_fir";
			await TurnToward(fir.TrunkPoint(0.5f), 1.6f, ct, stopWhen: Cracked);
			await WaitUntil(Cracked, 2, ct);
			Check("looking at it, it comes down", Cracked() && falls.FirSeen);
			await WatchTheFall(fir, falls, ct);
			// on round its crown, then off the way into the woods: a tree out there comes down across their way, in view
			_recLabel = "way_in";
			foreach (var p in RouteFrom(_player.GlobalPosition))
			{
				if (falls.Progress >= 20f) break;
				if (!await WalkTo(p, 2.2f, ct)) break;
			}
			// off the way to the right, out into the woods ahead of the walls
			_recLabel = "stray";
			int unseen = falls.UnseenFalls;
			await WalkTo(L(27f, -26f), 2f, ct, stopWhen: () => falls.WardDown > 0, giveUp: 14f);
			_input.ScriptedMove = Vector2.Zero;
			await WaitUntil(() => falls.WardDown > 0, 20, ct);
			Check("strayed off the way: a tree out in the woods came down across their way", falls.WardDown > 0, $"wards {falls.WardCount}, started {falls.WardStarted}, down {falls.WardDown}, {falls.OffLine:0.0} m off the line at {falls.Progress:0} m");
			var ward = falls.GetChildren().OfType<FallingTree>().FirstOrDefault(t => t.Name.ToString().StartsWith("Ward") && t.Down);
			float clear = ward == null ? -1f : Act1DistToSeg(Flat(_player.GlobalPosition), Flat(ward.LieSegment.a), Flat(ward.LieSegment.b));
			Check("and it came down in view, never near them", ward != null && falls.UnseenFalls == unseen && clear > 3.5f, $"{clear:0.0} m from where it lies; unseen falls {falls.UnseenFalls - unseen}");
			Check("no wall came down with them outside it", falls.SideDown < falls.SideCount, $"{falls.SideDown} of {falls.SideCount} down");
			Screenshot("stray_tree_down");
			// back to the way
			_recLabel = "back";
			await WalkTo(L(24f), 2f, ct, giveUp: 16f);
			Check("back on the way", Mathf.Abs(falls.OffLine) < falls.OnTheWay, $"{falls.OffLine:0.0} m off at {falls.Progress:0} m along");
		}
		else if (mode == "side")
		{
			// in from the woods to the right of the way, well past the fir's corner
			var from = L(30f, -22f);
			await Teleport(from, L(30f), ct);
			_rec = true; _recLabel = "from_side";
			await WalkTo(L(28f), 2f, ct, giveUp: 16f);
			Check("in from the side: the walls begin all the same", falls.SideStarted > 0, $"{falls.SideStarted} started, fir {(fir.Standing ? "standing" : "begun")}");
		}

		// on to the stairs along the way (seen falls, the title only close and in view)
		_recLabel = "way";
		bool titleBefore = StoryManager.Instance.HasFlag(flag);
		Check("no title over the stairs while they're far off", !titleBefore && !climb.Revealed);
		var route = RouteFrom(_player.GlobalPosition);
		float sideStop = mode == "side" ? 22f : 4f;
		foreach (var p in route)
		{
			if (Flat(p).DistanceTo(Flat(climb.GlobalPosition)) < sideStop) break;
			if (!await WalkTo(p, 2.2f, ct)) break;
		}
		if (mode == "side")
		{
			// beside the stairs, their back to them: no title; turning round to them: the title
			var foot = stairs.GlobalPosition;
			var across = stairs.GlobalBasis.X.Normalized();
			var beside = foot + across * 9f + stairs.GlobalBasis.Z.Normalized() * -3f;
			beside.Y = terrain.HeightAt(beside.X, beside.Z);
			_recLabel = "beside_back_turned";
			await Teleport(beside, beside + across * 10f, ct);
			await Seconds(2.5, ct);
			Check("beside the stairs, their back to them: no title yet", !climb.Revealed && !StoryManager.Instance.HasFlag(flag),
				$"{_player.GlobalPosition.DistanceTo(foot):0.0} m from the stairs");
			Screenshot("beside_back_turned");
			_recLabel = "turn_to_stairs";
			await TurnToward(foot + Vector3.Up * 2.5f - stairs.GlobalBasis.Z * 2f, 1.2f, ct);
			await WaitUntil(() => climb.Revealed, 2, ct);
		}
		await WaitUntil(() => climb.Revealed, 4, ct);
		Check("the stairs revealed only close, and in view", climb.Revealed && climb.RevealedAt <= climb.RevealRange + 0.5f, $"revealed at {climb.RevealedAt:0.0} m");
		await WaitUntil(() => ChapterCard.Shown.Contains(0), 6, ct);
		await Seconds(1.6, ct);
		Screenshot("title");
		Check("the title came up after that, never before", ChapterCard.Shown.Contains(0) && !titleEarly && flagDist <= climb.RevealRange + 3f,
			$"flag set at {flagDist:0.0} m, early {titleEarly}");
		Check("the walls came down beside the way", falls.SideStarted >= (mode == "side" ? 4 : 5), $"{falls.SideStarted} of {falls.SideCount} started, {falls.SideDown} down");
		Check("the falls ahead were seen", falls.SeenFalls >= 4, $"seen {falls.SeenFalls}, unseen {falls.UnseenFalls}");
		Check("and behind them, heard", falls.BehindStarted >= (mode == "side" ? 1 : 2), $"{falls.BehindStarted}");
		_rec = false;
		GD.Print($"[act1] {mode}: stops {falls.WatchCount}, last {falls.LastWatchSeconds:0.0} s, {falls.LastWatchFalls} fell in it; seen {falls.SeenFalls}, unseen {falls.UnseenFalls}; wards {falls.WardStarted}/{falls.WardCount}; reveal at {climb.RevealedAt:0.0} m");
	}

	private static float Act1DistToSeg(Vector3 q, Vector3 a, Vector3 b)
	{
		var ab = b - a;
		float t = ab.LengthSquared() > 1e-6f ? Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
		return q.DistanceTo(a + ab * t);
	}

	/// <summary>The dead fir's crack: they stand (still pressing forward, not touching the mouse) and watch it and the
	/// walls' first trees come down; their pace goes and comes back.</summary>
	private async Task WatchTheFall(FallenTree fir, Act1TreeFalls falls, CancellationToken ct)
	{
		bool cracked = fir.Fallen || (fir.Falling && !fir.Held);
		float d = Flat(_player.GlobalPosition).DistanceTo(Flat(fir.Foot));
		Check("the dead fir comes down as they near it, in view", cracked && falls.FirSeen, $"{d:0} m from its foot");
		Check("they stop to watch", falls.Watching);
		_recLabel = "watch";
		float slowest = 99f; ulong t0 = Time.GetTicksMsec(); double t = 0;
		while (falls.Watching && t < 45)
		{
			_input.ScriptedMove = new Vector2(0, 1);
			await Frames(1, ct);
			t = (Time.GetTicksMsec() - t0) / 1000.0;
			if (t > 1.2) slowest = Mathf.Min(slowest, _player.GroundSpeed);
		}
		_input.ScriptedMove = Vector2.Zero;
		Check("still pressing on, their pace dies away while the trees come down", slowest < 0.3f, $"slowest {slowest:0.00} m/s");
		Check("the walls' first trees came down while they watched, in view", falls.LastWatchFalls >= 2, $"{falls.LastWatchFalls} came down (the fir too) in {falls.LastWatchSeconds:0.0} s");
		Check("the stop was brief", falls.LastWatchSeconds > 2f && falls.LastWatchSeconds < falls.WatchMax, $"{falls.LastWatchSeconds:0.0} s");
		Check("the dead fir lies across the trail", fir.Fallen);
		_input.ScriptedMove = new Vector2(0, 1);
		await WaitUntil(() => _player.WadeScale > 0.98f, 6, ct);
		_input.ScriptedMove = Vector2.Zero;
		Check("and their pace comes back", _player.WadeScale > 0.98f, $"pace {_player.WadeScale:0.00}");
	}

	/// <summary>Turns the view toward a point at a person's pace (rad/s), not a snap.</summary>
	private async Task TurnToward(Vector3 point, float rate, CancellationToken ct, Func<bool> stopWhen = null)
	{
		for (double t = 0; t < 4; t += GetProcessDeltaTime())
		{
			if (stopWhen != null && stopWhen()) return;
			var eye = _player.CameraRig.Camera.GlobalPosition;
			var d = point - eye;
			float dy = Mathf.AngleDifference(_player.CameraRig.Yaw, Mathf.Atan2(-d.X, -d.Z));
			float dp = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()) - _player.CameraRig.Pitch;
			if (Mathf.Abs(dy) < 0.02f && Mathf.Abs(dp) < 0.02f) return;
			float step = rate * (float)GetProcessDeltaTime();
			_input.AddScriptedLook(new Vector2(Mathf.Clamp(dy, -step, step), Mathf.Clamp(dp, -step, step)));
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
}
