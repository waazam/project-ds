using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// Scripted playthrough for `-- --autotest`. It drives PlayerInput (never the
/// body directly) along the trail to the top of the stairs. On the way it
/// checks movement, running, the camera orbit, collision, footsteps, and the
/// silence curve. It writes a report, CSV telemetry, and screenshots to
/// test-output/, then quits with exit code 1 if any check failed.
///
/// Route: the Path3D in group "autotest_route" (or "trail" if there is none;
/// terrain height ignored), then the stairs' AutotestApproach and
/// "stairs_top_trigger". An optional "autotest_obstacle" node gets walked into.
/// </summary>
public partial class AutoTest : Node
{
	/// <summary>test-output/, or test-output/NAME with `--test-out=NAME` on the command line.</summary>
	private static readonly string OutDir = OutputDir();

	internal static string OutputDir()
	{
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--test-out=")) return "res://test-output/" + a["--test-out=".Length..];
		return "res://test-output";
	}
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
		Cutscene.Run(this, async _ =>
		{
			try { await Run(); }
			catch (System.Exception e) when (e is not System.OperationCanceledException)
			{
				// A crash in the walkthrough is a failure, never a silent hang.
				Check("walkthrough ran without an exception", false, e.ToString());
				Finish();
			}
		});
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
		var birds = GetTree().Root.FindChild("BirdCalls", true, false) as OneShotEmitter;
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
		// A screenshot is diagnostic sugar, never a test assertion: an occasional null frame from the
		// headless/dummy renderer (seen right after an expensive synchronous rebuild, e.g. Act 11's
		// stairs) must not take the whole scripted run down with it.
		try
		{
			var img = GetViewport()?.GetTexture()?.GetImage();
			img?.SavePng(ProjectSettings.GlobalizePath($"{OutDir}/{_shot++:00}_{label}.png"));
		}
		catch (System.Exception e)
		{
			GD.PushWarning($"[autotest] screenshot '{label}' failed: {e.Message}");
		}
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

		if (GameSettings.Instance.AutoTestSkipToAct5)
		{
			await SkipToAct5();
			if (_fpsCount > 0) Check("performance", _fpsSum / _fpsCount > 55f, $"avg {_fpsSum / _fpsCount:0} min {_fpsMin:0} fps");
			Finish();
			return;
		}

		if (GameSettings.Instance.AutoTestSkipToAct7)
		{
			await SkipToAct7();
			if (_fpsCount > 0) Check("performance", _fpsSum / _fpsCount > 55f, $"avg {_fpsSum / _fpsCount:0} min {_fpsMin:0} fps");
			Finish();
			return;
		}

		if (GameSettings.Instance.AutoTestSkipToAct11)
		{
			await SkipToAct11();
			if (_fpsCount > 0) Check("performance", _fpsSum / _fpsCount > 55f, $"avg {_fpsSum / _fpsCount:0} min {_fpsMin:0} fps");
			Finish();
			return;
		}

		Check("player spawned on ground", _player.IsOnFloor(), $"pos {_player.GlobalPosition}");
		Screenshot("spawn");
		if (GameSettings.Instance.Camera == CameraMode.FirstPerson)
		{
			float eye = _player.CameraRig.Camera.GlobalPosition.Y - _player.GlobalPosition.Y;
			Check("first-person camera at eye height", Mathf.Abs(eye - _player.CameraRig.EyeHeight) < 0.1f, $"{eye:0.00} m");
		}

		// Audio sanity at the start: living forest.
		await Wait(2.0);
		Check("forest alive at start", (_amb?.Silence ?? 1) < 0.05f && Db("Birds") > -1f && Db("Insects") > -1f,
			$"silence {_amb?.Silence:0.00} birds {Db("Birds"):0.0}dB");

		// The looping beds must actually be playing (bus volumes alone proved nothing once).
		var silentBeds = new List<string>();
		foreach (var bed in new[] { "Insects", "Undertone", "Pad" })
			if (GetTree().Root.FindChild(bed, true, false) is AudioStreamPlayer p && !p.Playing) silentBeds.Add(bed);
		Check("ambience and score loops are playing", silentBeds.Count == 0, silentBeds.Count == 0 ? "all playing" : $"not playing: {string.Join(", ", silentBeds)}");

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
		Check("walks", Mathf.Abs(walk - _player.WalkSpeed) < 0.4f, $"{walk:0.00} m/s");
		Check("runs", Mathf.Abs(run - _player.RunSpeed) < 0.6f, $"{run:0.00} m/s");
		if (_player.GetNodeOrNull<PlayerStamina>("Stamina") is { } stamina)
			Check("running drains stamina (scripted input too)", stamina.Value < 0.9f, $"stamina {stamina.Value:0.00}");
		Check("moved from spawn", start.DistanceTo(_player.GlobalPosition) > 8f, $"{start.DistanceTo(_player.GlobalPosition):0.0} m");
		await Drive(new Vector2(-1, 0), false, 1.0);   // strafe
		await Drive(new Vector2(0, -1), false, 1.0);   // back toward camera
		await Drive(Vector2.Zero, false, 1.0);
		Check("decelerates to stop", _player.GroundSpeed < 0.05f, $"{_player.GroundSpeed:0.00}");

		await ObstacleTest();
		await StillnessTest();

		// Follow the trail.
		var route = BuildRoute();
		Check("trail route found", route.Count > 3, $"{route.Count} points");
		var steps = _player.GetNode<PlayerFootsteps>("Footsteps");
		float travelled = 0; var last = _player.GlobalPosition;
		float nextShotAt = 30f;
		bool lookedBack = false;
		int birdsAtStart = (GetTree().Root.FindChild("BirdCalls", true, false) as OneShotEmitter)?.CallsPlayed ?? 0;
		int stepsBeforeClimb = 0;
		for (int i = 0; i < route.Count; i++)
		{
			// Line up tightly at the foot of the stairs so the climb starts square to the flight.
			float radius = i == route.Count - 1 ? 0.35f : i == route.Count - 2 ? 0.25f : 2.0f;
			bool ok = await GoTo(route[i], radius);
			if (!ok) { Check($"reached waypoint {i}", false, $"stuck at {_player.GlobalPosition} going to {route[i]}"); break; }
			if (i == route.Count - 2) { await StillInSilenceTest(); stepsBeforeClimb = steps.StepsPlayed; }
			travelled += last.DistanceTo(_player.GlobalPosition); last = _player.GlobalPosition;
			if (travelled >= nextShotAt) { Screenshot($"trail_{(int)travelled}m"); nextShotAt += 45f; }
			if (!lookedBack && travelled > 160f) { lookedBack = true; await LookBackTest(); }
		}
		Check("trail walked", travelled > 50f, $"{travelled:0} m");
		if (GetTree().Root.FindChild("Stalker", true, false) is Stalker st)
			Check("stalker active on the walk", st.PeekCount > 0 || st.SoundCount > 0, $"peeks {st.PeekCount}, seen {st.SeenCount}, sounds {st.SoundCount}");
		int birdCalls = ((GetTree().Root.FindChild("BirdCalls", true, false) as OneShotEmitter)?.CallsPlayed ?? 0) - birdsAtStart;
		Check("bird calls happened on the way", birdCalls > 5, $"{birdCalls} calls");

		// Act 2: the first staircase should have taken control away and carried the player to the top.
		// The climb tween eases out at the very end, so GoTo can call the position "arrived" well
		// before the tween actually fires Finished (and the checkpoint) — poll instead of a fixed wait.
		// The tween's own tail accounts for a couple of seconds here, plus the forced look-down
		// off the top step (pan + hold, ~10 s) that now runs before the checkpoint fires.
		double climbFinishWait = 0;
		while (StoryManager.Instance.Current < Checkpoint.Act2StairsClimbed && climbFinishWait < 16) { await Wait(0.25); climbFinishWait += 0.25; }
		Screenshot("stairs_top");
		var topNode = GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Node3D;
		Check("forced climb reached the top landing", topNode != null && _player.GlobalPosition.DistanceTo(topNode.GlobalPosition) < 1.5f,
			$"{_player.GlobalPosition}");
		Check("no footsteps during the forced climb", steps.StepsPlayed - stepsBeforeClimb <= 2, $"{steps.StepsPlayed - stepsBeforeClimb} steps");
		Check("near-total silence at stairs", (_amb?.Silence ?? 0) > 0.9f, $"silence {_amb?.Silence:0.00}");
		Check("birds + insects gone", Db("Birds") < -60f && Db("Insects") < -60f, $"{Db("Birds"):0} / {Db("Insects"):0} dB");
		Check("wind almost gone", Db("Wind") < -25f, $"{Db("Wind"):0.0} dB");
		Check("checkpoint 2 (stairs climbed) reached", StoryManager.Instance.Current >= Checkpoint.Act2StairsClimbed && StoryManager.Instance.StairsClimbed,
			$"after {climbFinishWait:0.0}s, {StoryManager.Instance.Current}");
		var save2 = SaveSystem.Load();
		Check("checkpoint 2 saved to disk", save2 != null && save2.Checkpoint >= Checkpoint.Act2StairsClimbed, $"{save2?.Checkpoint}");
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		Check("cabin door boarded on the climb", cabin != null && cabin.DoorBoarded, $"cabin found: {cabin != null}");

		// Control should be back: a scripted nudge should actually move the player now.
		var beforeNudge = _player.GlobalPosition;
		await Drive(new Vector2(0, -1), false, 0.6);
		Check("player control restored after the climb", _player.GlobalPosition.DistanceTo(beforeNudge) > 0.2f,
			$"{_player.GlobalPosition.DistanceTo(beforeNudge):0.00} m");

		// Act 3: walk all the way back to the cabin and find the door boarded.
		var back = BuildReturnRoute();
		Check("return route found", back.Count > 3, $"{back.Count} points");
		float returned = 0; var lastBack = _player.GlobalPosition;
		for (int i = 0; i < back.Count; i++)
		{
			float radius = i == back.Count - 1 ? 1.2f : 2.0f;
			bool ok = await GoTo(back[i], radius);
			if (!ok) { Check($"return waypoint {i}", false, $"stuck at {_player.GlobalPosition} going to {back[i]}"); break; }
			returned += lastBack.DistanceTo(_player.GlobalPosition); lastBack = _player.GlobalPosition;
		}
		Check("walked back toward the cabin", returned > 50f, $"{returned:0} m");
		double doorWait = 0;
		while (StoryManager.Instance.Current < Checkpoint.Act3DoorBoarded && doorWait < 10) { await Wait(0.25); doorWait += 0.25; }
		_input.ScriptedMove = Vector2.Zero;
		await GoTo(cabin.ApproachPoint, 1.0f);
		_input.ScriptedMove = Vector2.Zero;
		await SnapLookAt(cabin.DoorCenter);
		Screenshot("cabin_boarded");
		Check("checkpoint 3 (door boarded) reached", StoryManager.Instance.Current >= Checkpoint.Act3DoorBoarded, $"after {doorWait:0.0}s");
		var save3 = SaveSystem.Load();
		Check("checkpoint 3 saved to disk", save3 != null && save3.Checkpoint >= Checkpoint.Act3DoorBoarded, $"{save3?.Checkpoint}");

		var inv = _player.GetNode<PlayerInventory>("Inventory");
		var compassUi = GetTree().Root.FindChild("Compass", true, false) as Compass;

		// Gear up on the porch: lantern, then compass. They sit close together, so the
		// generous pickup radius (needed on this sloped ground) can sweep up both from one
		// tap; a pickup already gone by its turn just means that happened, not a failure.
		var lanternNode = cabin?.GetNodeOrNull<Node3D>("LanternPickup");
		Check("LanternPickup present", lanternNode != null, $"{lanternNode?.GlobalPosition}");
		foreach (var pickup in new[] { "LanternPickup", "CompassPickup" })
		{
			var node = cabin?.GetNodeOrNull<Node3D>(pickup);
			if (node == null) continue;
			await GoTo(node.GlobalPosition, 1.2f);
			await InteractAt(AimPoint(node));
		}
		Check("lantern equipped", inv.HasLantern, "");
		Check("compass equipped", inv.HasCompass, "");
		await Wait(0.5);
		Check("compass HUD showing", compassUi != null && compassUi.ShowingCompass, "");

		// A wide shot with both buildings in frame, to catch the cabin and shed ever overlapping again.
		if (GetTree().Root.FindChild("Shed", true, false) is Node3D shed)
		{
			_input.ScriptedMove = Vector2.Zero;
			await SnapLookAt(shed.GlobalPosition + Vector3.Up * 1.2f);
			Screenshot("cabin_and_shed");
		}

		// Step outside the safe zone: the storm should begin.
		Check("storm not yet active near the cabin", StormController.Instance is { Active: false }, "");
		await GoTo(cabin.GlobalPosition + new Vector3(0, 0, -30f), 2f);
		double stormWait = 0;
		while (StormController.Instance is not { Active: true } && stormWait < 8) { await Wait(0.25); stormWait += 0.25; }
		Screenshot("storm_active");
		Check("storm activated on leaving the safe zone", StormController.Instance is { Active: true }, $"after {stormWait:0.0}s");
		Check("forest forced silent by the storm", (_amb?.Silence ?? 0) > 0.9f, $"silence {_amb?.Silence:0.00}");

		// Act 4: wait for the giant (autotest uses a short fuse), then the compass repoints home.
		double giantWait = 0;
		while (StoryManager.Instance is { GiantEventDone: false } && giantWait < 20) { await Wait(0.5); giantWait += 0.5; }
		Check("giant set-piece fired", StoryManager.Instance.GiantEventDone, $"after {giantWait:0.1}s");
		var objective = StoryManager.Instance.ObjectivePosition;
		Check("compass now points at the cabin", objective != null && objective.Value.DistanceTo(cabin.GlobalPosition) < 3f, $"{objective}");

		// Act 5: find the axe and chop the door open.
		var axe = GetTree().Root.FindChild("AxePickup", true, false) as Node3D;
		Check("axe placed in the world", axe != null, $"{axe?.GlobalPosition}");
		if (axe != null)
		{
			await GoTo(axe.GlobalPosition, 1.2f);
			await InteractAt(AimPoint(axe));
		}
		Check("axe picked up", inv.HasTool(ToolKind.Axe), inv.Serialize());

		await GoTo(cabin.WideApproachPoint, 2.0f);
		await GoTo(cabin.ApproachPoint, 1.0f);
		// Stand a pace out from the door and look at the boards: the break-in is an Interactable
		// under the crosshair now (a press with the axe; a hammer would need the full hold).
		await GoTo(cabin.DoorCenter, 1.6f);
		_input.ScriptedMove = Vector2.Zero;
		await InteractAt(cabin.DoorCenter, 0.2);
		double openWait = 0;
		while (!cabin.IsOpen && openWait < 8) { await Wait(0.25); openWait += 0.25; }
		Check("door chopped open with the axe", cabin.IsOpen, $"after {openWait:0.1}s");
		Check("axe consumed", !inv.HasTool(ToolKind.Axe), inv.Serialize());
		_input.ScriptedMove = Vector2.Zero;
		await GoTo(cabin.ApproachPoint, 1.0f);
		_input.ScriptedMove = Vector2.Zero;
		await SnapLookAt(cabin.DoorCenter);
		Screenshot("cabin_door_open");

		// Step inside and find the friend. The doorway is a tight, precise target for this bot's
		// straight-line steering (no real pathfinding/obstacle-avoidance) even though it's an easy
		// walk for an actual player; the door mechanic itself is already proven by the chop above.
		// Try it properly first, and only if that genuinely can't thread the gap, place the player
		// just inside (as a scripted entry would) so the reveal logic itself still gets exercised.
		Vector3 insideTarget = cabin.ToGlobal(new Vector3(0.3f, 0f, -0.8f));
		bool atDoor = await GoTo(cabin.DoorCenter, 0.6f);
		bool walkedIn = atDoor && await GoTo(insideTarget, 1.0f);
		Check("walked in through the doorway", walkedIn, walkedIn ? "" : $"stuck at {_player.GlobalPosition}; placing inside");
		if (!walkedIn) { insideTarget.Y = _player.GlobalPosition.Y; _player.GlobalPosition = insideTarget; await Frame(); }
		double revealWait = 0;
		while (StoryManager.Instance.Current < Checkpoint.Act5CabinEntered && revealWait < 24) { await Wait(0.25); revealWait += 0.25; }
		Screenshot("friend_found");
		Check("checkpoint 4 (found the friend) reached", StoryManager.Instance.Current >= Checkpoint.Act5CabinEntered, $"after {revealWait:0.1}s");
		var save4 = SaveSystem.Load();
		Check("checkpoint 4 saved to disk", save4 != null && save4.Checkpoint >= Checkpoint.Act5CabinEntered, $"{save4?.Checkpoint}");

		await Act6And7(inv, cabin);
		await Act8Through10();
		await Act11();

		if (_fpsCount > 0) Check("performance", _fpsSum / _fpsCount > 55f, $"avg {_fpsSum / _fpsCount:0} min {_fpsMin:0} fps");
		Finish();
	}

	/// <summary>
	/// Dev-only fast path (--skip-to-act5): fakes Acts 1-5 as already complete — door open,
	/// lantern/compass in hand, player just inside the cabin — and jumps straight to the Act 5 → 7
	/// handoff, for fast iteration on that stretch without replaying the whole game first.
	/// </summary>
	private async Task SkipToAct5()
	{
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		Check("cabin found for skip-to-act5", cabin != null, "");
		if (cabin == null) return;
		var inv = _player.GetNode<PlayerInventory>("Inventory");
		cabin.OpenDoor();
		inv.TryPickup(ToolKind.Lantern);
		inv.TryPickup(ToolKind.Compass);
		_player.GlobalPosition = cabin.ToGlobal(new Vector3(0.3f, 0f, -0.8f));
		await Frame();
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act5CabinEntered, _player.GlobalPosition, _player.CameraRig.Yaw);
		// Let checkpoint-gated Pickups (the newel post) notice the new checkpoint and reveal
		// themselves before we go looking for them — their own _Process only runs on real frames.
		await Wait(0.3);
		await Act6And7(inv, cabin);
		await Act8Through10();
	}

	/// <summary>Dev-only fast path (--skip-to-act7): fakes Acts 1-7 as already complete — cabin open,
	/// lantern/compass/newel post handled, the cabin already seen burning — and places the player
	/// just outside the bunker's open radius, for fast iteration on Acts 8-10 (the bunker interior).</summary>
	private async Task SkipToAct7()
	{
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		Check("cabin found for skip-to-act7", cabin != null, "");
		if (cabin == null) return;
		var inv = _player.GetNode<PlayerInventory>("Inventory");
		cabin.OpenDoor();
		inv.TryPickup(ToolKind.Lantern);
		inv.TryPickup(ToolKind.Compass);
		StoryManager.Instance.MarkGiantEventDone();
		StoryManager.Instance.MarkNewelPostTaken();
		StoryManager.Instance.MarkClearingVoiceHeard();
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act6BridgeCrossed, cabin.GlobalPosition, 0f);

		var bunkerNode = GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		Check("bunker found for skip-to-act7", bunkerNode != null, $"{bunkerNode?.GlobalPosition}");
		if (bunkerNode == null) return;
		// The bunker's own Y doesn't carry over to a point 18 m away on uneven ground — sample the
		// terrain there directly (as any real placement in this game does) rather than teleporting
		// blind and hoping gravity sorts it out.
		Vector3 spot = bunkerNode.GlobalTransform * new Vector3(0, 0, 18f);
		if (GetTree().GetFirstNodeInGroup("terrain") is ForestTerrain terrain)
			spot.Y = terrain.HeightAt(spot.X, spot.Z) + 0.2f;
		_player.GlobalPosition = spot;
		await Frame();
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act7CabinBurning, _player.GlobalPosition, _player.CameraRig.Yaw);
		await Wait(0.3);
		await Act8Through10();
	}

	/// <summary>Dev-only fast path (--skip-to-act11): fakes Acts 1-10 as already complete — cabin,
	/// newel post, clearing voice, and the whole bunker (hallway/CRT room/maze) all handled — and
	/// places the player just outside the bunker with the walkie-talkie already found, for fast
	/// iteration on Act 11 (the radio, the tall stairs, the giant) without replaying the bunker first.</summary>
	private async Task SkipToAct11()
	{
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		Check("cabin found for skip-to-act11", cabin != null, "");
		if (cabin == null) return;
		var inv = _player.GetNode<PlayerInventory>("Inventory");
		cabin.OpenDoor();
		inv.TryPickup(ToolKind.Lantern);
		inv.TryPickup(ToolKind.Compass);
		StoryManager.Instance.MarkGiantEventDone();
		StoryManager.Instance.MarkNewelPostTaken();
		StoryManager.Instance.MarkClearingVoiceHeard();
		StoryManager.Instance.MarkCrtPuzzleDone();

		var bunkerNode = GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		Check("bunker found for skip-to-act11", bunkerNode != null, $"{bunkerNode?.GlobalPosition}");
		if (bunkerNode == null) return;
		Vector3 spot = bunkerNode.GlobalTransform * new Vector3(0, 0, 16f);
		if (GetTree().GetFirstNodeInGroup("terrain") is ForestTerrain terrain)
			spot.Y = terrain.HeightAt(spot.X, spot.Z) + 0.2f;
		_player.GlobalPosition = spot;
		await Frame();
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act10WalkieFound, _player.GlobalPosition, _player.CameraRig.Yaw);
		inv.TryPickup(ToolKind.Radio);
		await Wait(0.3);
		await Act11();
	}

	/// <summary>Act 7's handoff into the bunker (Act 8's hallway, Act 9's CRT room, Act 10's maze escape).</summary>
	private async Task Act8Through10()
	{
		var bunkerNode = GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		Check("bunker exists in the world", bunkerNode != null, $"{bunkerNode?.GlobalPosition}");
		var objAfterFire = StoryManager.Instance.ObjectivePosition;
		Check("compass now points at the bunker", bunkerNode != null && objAfterFire != null && objAfterFire.Value.DistanceTo(bunkerNode.GlobalPosition) < 3f, $"{objAfterFire}");

		bool BunkerEntered() => StoryManager.Instance.Current >= Checkpoint.Act8BunkerEntered;
		if (bunkerNode != null)
		{
			// Wherever the fire checkpoint left the player standing near the cabin (a normal walk-up,
			// or a recovery from the accidental extended climb above), the same cabin/shed tightness
			// that required a sidestep-and-snap for the bridge leg applies here too — a blind
			// BuildTrailRouteTo from an arbitrary point right next to the building can box the bot in.
			var cabinNode = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
			if (cabinNode != null)
			{
				await GoTo(cabinNode.WideApproachPoint, 2.0f);
				await ReachTrailNear(cabinNode);
			}
			var toBunker = BuildTrailRouteTo(bunkerNode.GlobalPosition);
			for (int i = 0; i < toBunker.Count - 1 && !BunkerEntered(); i++)
				await GoTo(toBunker[i], 3.0f, BunkerEntered);
			// In through the door: the apron in front of it, then the doorway trigger just inside the hatch.
			if (bunkerNode is Bunker bunker)
			{
				if (!BunkerEntered()) await GoTo(bunker.ApproachPointWorld, 0.8f, BunkerEntered);
				if (!BunkerEntered()) await GoTo(bunker.EntryPointWorld, 0.3f, BunkerEntered);
			}
		}
		Screenshot("bunker_found");
		double bunkerWait = 0;
		while (StoryManager.Instance.Current < Checkpoint.Act8BunkerEntered && bunkerWait < 12) { await Wait(0.25); bunkerWait += 0.25; }
		Check("checkpoint 7 (entered the bunker) reached", StoryManager.Instance.Current >= Checkpoint.Act8BunkerEntered, $"after {bunkerWait:0.1}s");
		var save7 = SaveSystem.Load();
		Check("checkpoint 7 saved to disk", save7 != null && save7.Checkpoint >= Checkpoint.Act8BunkerEntered, $"{save7?.Checkpoint}");

		var bunkerInt = BunkerInterior.Instance;
		Check("bunker interior wired up", bunkerInt != null, "");
		if (bunkerInt == null) return;

		// The checkpoint fires the instant the trigger is crossed, but AdmitPlayer's own fade/teleport
		// sequence takes a couple of seconds to actually land the player — give it room to finish.
		double admitWait = 0;
		while (_player.GlobalPosition.DistanceTo(bunkerInt.GlobalPosition) > 500f && admitWait < 5) { await Wait(0.25); admitWait += 0.25; }
		Check("teleported into the bunker interior", _player.GlobalPosition.DistanceTo(bunkerInt.GlobalPosition) < 500f,
			$"player {_player.GlobalPosition}, interior {bunkerInt.GlobalPosition}, after {admitWait:0.0}s");

		// Act 8: the hallway. Walking its length should trip the light-reddening mechanic.
		bool reachedDoor = await GoTo(bunkerInt.VineDoorApproachWorld + new Vector3(0, 0, -4f), 0.6f);
		Screenshot("hallway_red");
		Check("walked the length of the hallway", reachedDoor, $"{_player.GlobalPosition}");
		Check("hallway lights commit to red by the far end", bunkerInt.RedTriggered, "");
		Check("player standing in the vine door's trigger zone", bunkerInt.PlayerAtVineDoor, $"{_player.GlobalPosition}");
		await InteractAt(bunkerInt.VineDoorInteractWorld);
		Check("vine door pushed open", bunkerInt.VineDoorOpenState, "");
		bool reachedCrtRoom = await GoTo(bunkerInt.CrtRoomInteriorWorld, 1.2f);
		Check("walked into the CRT room", reachedCrtRoom, $"{_player.GlobalPosition}");

		// Act 9: the CRT room.
		bool reachedCrtTarget = await GoTo(bunkerInt.CrtTargetApproachWorld, 1.0f);
		Check("reached the target screen", reachedCrtTarget, $"{_player.GlobalPosition} vs {bunkerInt.CrtTargetApproachWorld}");
		Check("in range to interact with the target screen", bunkerInt.PlayerAtCrtTarget, "");
		await InteractAt(bunkerInt.CrtSwitchWorld);
		await Wait(0.3);
		Check("all the screens go dark", bunkerInt.ScreensOff, "");
		double crtWait = 0;
		while (StoryManager.Instance is { CrtPuzzleDone: false } && crtWait < 8) { await Wait(0.25); crtWait += 0.25; }
		Screenshot("crt_room");
		Check("the screens turn back on showing the stairs", StoryManager.Instance.CrtPuzzleDone, $"after {crtWait:0.1}s");

		// Act 10: leaving back through the door turns the hallway into a maze mid-walk — a plain
		// GoTo would keep chasing the old (now irrelevant) target through the teleport that follows,
		// so steer by hand and stop the moment the maze actually appears.
		var vineApproach = bunkerInt.VineDoorApproachWorld;
		double backTimer = 0;
		while (!bunkerInt.MazeActive && backTimer < 15)
		{
			SteerCamera(vineApproach);
			_input.ScriptedMove = new Vector2(0, 1);
			_input.ScriptedRun = true;
			await Frame();
			backTimer += 1.0 / 60;
		}
		Check("leaving back through the door turns the hallway into a maze", bunkerInt.MazeActive, $"after {backTimer:0.0}s");

		if (bunkerInt.MazeSolutionWaypointsWorld is { Count: > 0 } maze)
		{
			for (int i = 0; i < maze.Count; i++)
				await GoTo(maze[i], i == maze.Count - 1 ? 1.2f : 1.5f);
		}
		Screenshot("maze_exit");
		var walkie = GetTree().Root.FindChild("WalkiePickup", true, false) as Node3D;
		Check("walkie-talkie dropped at the maze exit", walkie != null, $"{walkie?.GlobalPosition}");
		if (walkie != null)
		{
			await GoTo(walkie.GlobalPosition, 1.2f);
			await InteractAt(AimPoint(walkie));
		}
		double walkieWait = 0;
		while (StoryManager.Instance.Current < Checkpoint.Act10WalkieFound && walkieWait < 5) { await Wait(0.25); walkieWait += 0.25; }
		Check("checkpoint 8 (walkie-talkie found) reached", StoryManager.Instance.Current >= Checkpoint.Act10WalkieFound, $"after {walkieWait:0.1}s");
		var save8 = SaveSystem.Load();
		Check("checkpoint 8 saved to disk", save8 != null && save8.Checkpoint >= Checkpoint.Act10WalkieFound, $"{save8?.Checkpoint}");
	}

	/// <summary>Act 11, "The Third Man": the radio's one exchange, the walk back to the now-impossibly-tall
	/// original stairs, the climb, and the giant's touch.</summary>
	private async Task Act11()
	{
		// The radio wakes up: a fade-teleport carries the player back outside near the bunker, then the
		// exchange plays as a subtitle (never a full black screen — this is meant to happen mid-stride).
		double dialogueWait = 0;
		while (StoryManager.Instance is { Act11DialogueDone: false } && dialogueWait < 20) { await Wait(0.25); dialogueWait += 0.25; }
		Screenshot("radio_outside");
		Check("radio exchange finished, compass ready to point at the stairs", StoryManager.Instance.Act11DialogueDone, $"after {dialogueWait:0.1}s");
		var bunkerNode = GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		Check("player carried back outside near the bunker", bunkerNode != null && _player.GlobalPosition.DistanceTo(bunkerNode.GlobalPosition) < 60f,
			$"{_player.GlobalPosition}");

		var stairsMarker = GetTree().GetFirstNodeInGroup("stairs_clearing_marker") as Node3D;
		var objToStairs = StoryManager.Instance.ObjectivePosition;
		Check("compass now points at the main stairs", stairsMarker != null && objToStairs != null && objToStairs.Value.DistanceTo(stairsMarker.GlobalPosition) < 3f,
			$"{objToStairs}");

		var act11 = GetTree().Root.FindChild("Act11Ending", true, false) as Act11Ending;
		Check("Act11Ending wired up", act11 != null, "");
		if (act11 == null || stairsMarker == null) return;

		var toStairs = BuildTrailRouteTo(stairsMarker.GlobalPosition);
		for (int i = 0; i < toStairs.Count; i++) await GoTo(toStairs[i], i == toStairs.Count - 1 ? 3.0f : 3.0f);
		Screenshot("stairs_reveal");

		var approach = act11.ApproachWorld;
		if (approach != null) await GoTo(approach.Value, 1.0f);
		var trigger = act11.ClimbTriggerWorld;
		Check("climb trigger exists at the base of the (now huge) stairs", trigger != null, $"{trigger}");
		if (trigger != null) await GoTo(trigger.Value, 0.8f);

		double climbWait = 0;
		while (StoryManager.Instance.Current < Checkpoint.Act11GiantEncounter && climbWait < 90) { await Wait(0.5); climbWait += 0.5; }
		Screenshot("giant_touch");
		Check("checkpoint 9 (the giant's touch) reached", StoryManager.Instance.Current >= Checkpoint.Act11GiantEncounter, $"after {climbWait:0.1}s");
		var save9 = SaveSystem.Load();
		Check("checkpoint 9 saved to disk", save9 != null && save9.Checkpoint >= Checkpoint.Act11GiantEncounter, $"{save9?.Checkpoint}");
	}

	/// <summary>Act 5's handoff into Act 6 (newel post, the bridge, the clearing) and Act 7 (the cabin on fire).</summary>
	private async Task Act6And7(PlayerInventory inv, Cabin cabin)
	{
		var newelNode = GetTree().Root.FindChild("NewelPostPickup", true, false) as Node3D;
		Check("newel post placed on the table", newelNode != null, $"{newelNode?.GlobalPosition}");
		if (newelNode != null)
		{
			await GoTo(newelNode.GlobalPosition, 1.2f);
			await InteractAt(AimPoint(newelNode));
		}
		Check("newel post taken", inv.HasNewelPost, "");
		var bridgeMarker = GetTree().GetFirstNodeInGroup("bridge_marker") as Node3D;
		var objAfterNewel = StoryManager.Instance.ObjectivePosition;
		Check("compass now points at the bridge", objAfterNewel != null && bridgeMarker != null && objAfterNewel.Value.DistanceTo(bridgeMarker.GlobalPosition) < 3f,
			$"{objAfterNewel}");

		// Step back outside: the storm should break and dawn should come up. The shed sits
		// almost dead ahead along the cabin's own local +Z (its front wall is barely 0.4 m
		// past the shed's near wall — the two structures are placed very close together), so
		// any straight walk further out along that axis (ApproachPoint, WideApproachPoint)
		// drives straight into it. Sidestep well clear laterally instead of following that line.
		await GoTo(cabin.DoorCenter, 0.6f);
		await GoTo(cabin.GlobalTransform * new Vector3(-6f, 0f, 3f), 1.0f);
		double dawnWait = 0;
		while (StormController.Instance is { Active: true } && dawnWait < 8) { await Wait(0.25); dawnWait += 0.25; }
		Check("storm breaks once the newel post is carried outside", StormController.Instance is { Active: false }, $"after {dawnWait:0.1}s");
		Screenshot("dawn");

		// Act 6: cross the bridge — some 130 m of open forest away. The cabin and shed sit
		// tight enough against each other that the bot's blind steering can get boxed in
		// trying to path around both from right behind the cabin (a real player just looks
		// and walks around them); the storm/dawn trigger above already proves the actual exit
		// mechanic works, so get onto the open trail itself before following it to the bridge.
		await ReachTrailNear(cabin);
		Check("bridge placed in the world", bridgeMarker != null, $"{bridgeMarker?.GlobalPosition}");
		if (bridgeMarker != null)
		{
			var toBridge = BuildTrailRouteTo(bridgeMarker.GlobalPosition);
			for (int i = 0; i < toBridge.Count; i++)
				await GoTo(toBridge[i], i == toBridge.Count - 1 ? 1.0f : 3.0f);
		}
		Screenshot("bridge_area");
		double bridgeWait = 0;
		while (StoryManager.Instance.Current < Checkpoint.Act6BridgeCrossed && bridgeWait < 10) { await Wait(0.25); bridgeWait += 0.25; }
		Check("checkpoint 5 (bridge crossed) reached", StoryManager.Instance.Current >= Checkpoint.Act6BridgeCrossed, $"after {bridgeWait:0.1}s");
		var save5 = SaveSystem.Load();
		Check("checkpoint 5 saved to disk", save5 != null && save5.Checkpoint >= Checkpoint.Act6BridgeCrossed, $"{save5?.Checkpoint}");

		var clearingMarker = GetTree().GetFirstNodeInGroup("stairs_clearing_marker") as Node3D;
		var objAfterBridge = StoryManager.Instance.ObjectivePosition;
		Check("compass now points at the clearing", objAfterBridge != null && clearingMarker != null && objAfterBridge.Value.DistanceTo(clearingMarker.GlobalPosition) < 3f,
			$"{objAfterBridge}");

		// Walk to the clearing (the same one from Act 2, ~450 m further on) — reuse the exact
		// trail-following route the first climb used, which already proves out this whole
		// corridor, rather than trusting a long blind hop across the forest again.
		// The now-larger mini staircases can be close enough to this path that the bot brushes one
		// by accident and fires the (entirely optional, player-only) extended climb — if that
		// happens, stop steering toward the old waypoint immediately rather than fighting the
		// scripted teleport that follows.
		var act6Event = GetTree().Root.FindChild("Act6Clearing", true, false) as Act6ClearingEvent;
		bool ExtendedClimbFired() => act6Event?.ExtendedClimbFired ?? false;
		var clearingRoute = BuildRoute();
		Check("clearing route found", clearingRoute.Count > 1, $"{clearingRoute.Count} points");
		for (int i = 0; i < clearingRoute.Count && !ExtendedClimbFired(); i++)
			await GoTo(clearingRoute[i], i == clearingRoute.Count - 1 ? 10f : 2.0f, ExtendedClimbFired);
		if (ExtendedClimbFired())
		{
			// The mini staircases free themselves almost the instant the climb starts, so that's not
			// a useful "it's finished" signal — just wait out the whole scripted sequence (fade, 5 s
			// hold, rebuild, teleport, fade back, captions) before touching player state again.
			GD.Print("[autotest] brushed an optional mini staircase on the way in — waiting out the extended climb before continuing");
			await Wait(15.0);
		}
		double voiceWait = 0;
		while (StoryManager.Instance is { ClearingVoiceHeard: false } && voiceWait < 15) { await Wait(0.5); voiceWait += 0.5; }
		Screenshot("clearing");
		Check("the clearing's voice fires and the post fuses", StoryManager.Instance.ClearingVoiceHeard, $"after {voiceWait:0.1}s");
		Check("newel post consumed by the fusion", !inv.HasNewelPost, "");
		int miniCount = GetTree().GetNodesInGroup("act6_mini_stairs").Count;
		Check("fifteen mini staircases appeared", miniCount == 15, $"{miniCount}");

		// Leave the fifteen stairs alone: night should still fall on its own after the fallback delay.
		var atmosphere = GetTree().Root.FindChild("Atmosphere", true, false) as ForestAtmosphere;
		double nightWait = 0;
		while (atmosphere?.CurrentMood != ForestAtmosphere.Mood.Night && nightWait < 12) { await Wait(0.5); nightWait += 0.5; }
		Check("night falls on its own when the optional stairs are skipped", atmosphere?.CurrentMood == ForestAtmosphere.Mood.Night, $"after {nightWait:0.1}s, mood {atmosphere?.CurrentMood}");

		// Act 7: back to the cabin, now on fire. Same accidental-mini-staircase protection as the
		// walk in (BuildReturnRoute leaves from wherever the player actually is, so it recovers
		// cleanly on its own once the extended climb — if it fires here instead — hands control back).
		bool climbFiredBefore = ExtendedClimbFired();
		var backToCabin = BuildReturnRoute();
		Check("return-to-cabin route found (Act 7)", backToCabin.Count > 3, $"{backToCabin.Count} points");
		for (int i = 0; i < backToCabin.Count && !ExtendedClimbFired(); i++)
			await GoTo(backToCabin[i], i == backToCabin.Count - 1 ? 1.2f : 2.0f, ExtendedClimbFired);
		if (ExtendedClimbFired() && !climbFiredBefore)
		{
			GD.Print("[autotest] brushed an optional mini staircase on the way out — waiting out the extended climb before continuing");
			await Wait(12.0);
			backToCabin = BuildReturnRoute();
			for (int i = 0; i < backToCabin.Count; i++)
				await GoTo(backToCabin[i], i == backToCabin.Count - 1 ? 1.2f : 2.0f);
		}
		double fireWait = 0;
		while (StoryManager.Instance.Current < Checkpoint.Act7CabinBurning && fireWait < 15) { await Wait(0.5); fireWait += 0.5; }
		Screenshot("cabin_burning");
		Check("checkpoint 6 (cabin burning) reached", StoryManager.Instance.Current >= Checkpoint.Act7CabinBurning, $"after {fireWait:0.1}s");
		var save6 = SaveSystem.Load();
		Check("checkpoint 6 saved to disk", save6 != null && save6.Checkpoint >= Checkpoint.Act7CabinBurning, $"{save6?.Checkpoint}");
	}

	/// <summary>
	/// Looks at <paramref name="target"/> (interaction is decided by the centre-screen crosshair) and
	/// holds E through PlayerInput's scripted button for <paramref name="holdSeconds"/> (at least a
	/// few frames; the Pressed edge fires on the first). Hold-to-use interactables need their full
	/// HoldSeconds here.
	/// </summary>
	private async Task InteractAt(Vector3 target, double holdSeconds = 0.08)
	{
		_input.ScriptedMove = Vector2.Zero;
		await SnapLookAt(target);
		await Frame();
		var interaction = _player.GetNodeOrNull<PlayerInteraction>("Interaction");
		GD.Print($"[autotest] INFO interact at {target}: focused '{interaction?.Focused?.GetParent()?.Name}' prompt '{interaction?.PromptText}', " +
			$"eye {_player.CameraRig.Camera.GlobalPosition}, input enabled {_input.Enabled}");
		_input.ScriptedInteract = true;
		for (int i = 0; i < 3; i++) await Frame();
		await Wait(holdSeconds);
		_input.ScriptedInteract = false;
		await Wait(0.1);
	}

	/// <summary>Where to look to focus an object's Interactable: the centre of its pick volume.</summary>
	private static Vector3 AimPoint(Node3D node)
	{
		var use = node.GetNodeOrNull<Interactable>("Interactable") ?? node as Interactable;
		return use != null ? use.ToGlobal(use.PickOffset) : node.GlobalPosition + Vector3.Up * 0.15f;
	}

	/// <summary>
	/// Walks onto the trail waypoint nearest <paramref name="cabin"/> (the cabin and shed sit close
	/// together, which can box in the bot's straight-line steering). Only if the walk genuinely fails
	/// does it place the player there, and it says so in the log.
	/// </summary>
	private async Task ReachTrailNear(Node3D cabin)
	{
		var trail = AllTrailWaypoints();
		if (trail.Count == 0) return;
		Vector3 nearest = trail[0]; float best = float.MaxValue;
		foreach (var p in trail)
		{
			float d = Flat(p).DistanceTo(Flat(cabin.GlobalPosition));
			if (d < best) { best = d; nearest = p; }
		}
		if (await GoTo(nearest, 2.0f)) return;
		GD.Print($"[autotest] INFO could not walk from {_player.GlobalPosition} to the trail at {nearest}; placing the player there");
		_player.GlobalPosition = nearest;
		await Frame();
	}

	private async Task Drive(Vector2 move, bool run, double seconds)
	{
		_input.ScriptedMove = move; _input.ScriptedRun = run;
		await Wait(seconds);
		_input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false;
	}

	/// <summary>Stand still in the living forest: life should come closer.</summary>
	private async Task StillnessTest()
	{
		if (GetTree().Root.FindChild("Director", true, false) is not ForestDirector d) return;
		var birds = GetTree().Root.FindChild("BirdCalls", true, false) as OneShotEmitter;
		_input.ScriptedMove = Vector2.Zero;
		await Wait(27);
		Check("stillness builds when standing still", d.Stillness > 0.95f, $"{d.StillSeconds:0}s still, stillness {d.Stillness:0.00}");
		Check("birds come closer when still", birds != null && birds.DistanceScale < 0.7f && birds.RateScale > 1.2f * d.Activity,
			$"distance x{birds?.DistanceScale:0.00}, rate x{birds?.RateScale:0.00} (activity {d.Activity:0.00})");
		Check("gusts happen", d.GustCount > 0, $"{d.GustCount} gusts");
	}

	/// <summary>At the foot of the stairs, in full silence: hold still until the heartbeat comes.</summary>
	private async Task StillInSilenceTest()
	{
		if (GetTree().Root.FindChild("Director", true, false) is not ForestDirector) return;
		var heart = GetTree().Root.FindChild("Heartbeat", true, false)?.GetNodeOrNull<AmbienceLoop>("Loop");
		var pressure = GetTree().Root.FindChild("Pressure", true, false)?.GetNodeOrNull<AmbienceLoop>("Loop");
		_input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false;
		await Wait(42);
		Screenshot("still_at_stairs");
		Check("heartbeat surfaces when still in silence", heart != null && heart.Gain > 0.8f, $"gain {heart?.Gain:0.00}");
		Check("unnatural layers swell when still in silence", pressure != null && pressure.ExtraDb > 4f, $"+{pressure?.ExtraDb:0.0} dB");
		var room = AudioServer.GetBusEffect(AudioServer.GetBusIndex("Player"), 0) as AudioEffectReverb;
		Check("footsteps take on a room in the silence", room != null && room.Wet > 0.15f, $"wet {room?.Wet:0.00}");
		if (GetTree().Root.FindChild("Director", true, false) is ForestDirector d)
			Check("the silence drops out now and then", d.DropoutCount > 0, $"{d.DropoutCount} dropouts in 42 s");
		var breath = _player.GetNodeOrNull<PlayerBreathing>("Breathing");
		Check("no breathing in the silence at rest", breath == null || breath.Exertion < breath.AudibleFrom, $"exertion {breath?.Exertion:0.00}");
	}

	/// <summary>Stop, make the stalker take cover behind us, turn around, and see what it does.</summary>
	private async Task LookBackTest()
	{
		if (GetTree().Root.FindChild("Stalker", true, false) is not Stalker st) return;
		_input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false;
		await Wait(0.6);
		// DebugForcePeek can fail for two different, both entirely legitimate reasons: it correctly
		// refuses to interrupt an in-progress Vanishing dissolve, and its own tree search is a randomised,
		// player-position-dependent raycast sweep that can simply come up empty if nothing suitable is
		// behind the player at this exact spot on the trail. Neither is a bug — retry a few times so one
		// unlucky roll (or a stalker still finishing a peek from the very active walk in) doesn't fail
		// the check outright when the stalker plainly is capable of taking cover here.
		bool placed = st.DebugForcePeek();
		for (int i = 0; i < 10 && !placed; i++)
		{
			await Wait(0.3);
			placed = st.DebugForcePeek();
		}
		int seenBefore = st.SeenCount;
		for (int i = 0; i < 45; i++) { _input.AddScriptedLook(new Vector2(Mathf.Pi / 45f, 0)); await Frame(); }
		Screenshot("look_back");
		await Wait(2.5);
		Screenshot("look_back_after");
		Check("stalker takes cover behind the player", placed, $"state {st.Current}");
		if (st.SeenCount > seenBefore)
			Check("stalker dissolves when looked at", st.Current != Stalker.State.Peeking && st.LastSeenDuration < 2.0,
				$"{st.LastSeenFraction:P0} visible, gone after {st.LastSeenDuration:0.00}s");
		else
			GD.Print("[autotest] INFO stalker stayed out of line of sight during the look-back");
		for (int i = 0; i < 45; i++) { _input.AddScriptedLook(new Vector2(-Mathf.Pi / 45f, 0)); await Frame(); }
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

	/// <summary>Snaps yaw AND pitch to look exactly at a point in one shot, from the camera's actual eye
	/// position — unlike FaceTarget/SteerCamera (yaw only, and rate-limited, so it can leave a residual
	/// pitch from whatever the player was last looking at, or not fully turn in 30 frames), this is for
	/// establishing shots that need to reliably frame a specific target regardless of where the camera
	/// happened to be pointed beforehand.</summary>
	private async Task SnapLookAt(Vector3 target)
	{
		Vector3 eye = _player.CameraRig.Camera.GlobalPosition;
		Vector3 to = target - eye;
		float wantYaw = Mathf.Atan2(-to.X, -to.Z);
		float horiz = new Vector2(to.X, to.Z).Length();
		float wantPitch = Mathf.Atan2(to.Y, horiz);
		// Straight through the rig, so it works whether or not input is currently enabled.
		_player.CameraRig.SnapBehind(wantYaw);
		_player.CameraRig.SetPitch(wantPitch);
		await Frame();
		await Frame();
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

	/// <summary>
	/// Steers toward target until within radius (true), stuck for 7s (false), or — if given —
	/// giveUpIf() turns true first (true): a scripted story transition (a teleport into another
	/// space entirely) can fire mid-walk from a trigger the bot just crossed, and blindly continuing
	/// to steer toward the now-irrelevant old-world target once input re-enables there would just
	/// drag the player sideways off whatever they landed on.
	/// </summary>
	private async Task<bool> GoTo(Vector3 target, float radius, System.Func<bool> giveUpIf = null)
	{
		double stuckTimer = 0; float bestDist = float.MaxValue;
		double sidestepTimer = 0; float sidestepDir = 1f;
		while (true)
		{
			if (giveUpIf != null && giveUpIf()) { _input.ScriptedMove = Vector2.Zero; return true; }
			float d = Flat(_player.GlobalPosition).DistanceTo(Flat(target));
			if (d < radius) return true;
			if (d < bestDist - 0.3f) { bestDist = d; stuckTimer = 0; }
			stuckTimer += 1.0 / 60;
			if (stuckTimer > 7) return false;
			SteerCamera(target);
			// Run until the forest starts to hush, then walk like a nervous person. Holding run is all
			// the bot does about stamina, like a player: an empty tank drops it to a walk until it refills.
			float s = _amb?.Silence ?? 0;
			_input.ScriptedRun = s < 0.15f;
			// A single tree in the way: sidestep around it rather than push straight into it forever.
			if (stuckTimer > 1.0 && sidestepTimer <= 0) { sidestepTimer = 1.0; sidestepDir = -sidestepDir; }
			if (sidestepTimer > 0) { sidestepTimer -= 1.0 / 60; _input.ScriptedMove = new Vector2(sidestepDir, 0.5f); }
			else _input.ScriptedMove = new Vector2(0, 1);
			await Frame();
		}
	}

	/// <summary>The hidden test route (or the trail, if there is no hidden route) baked into ~6 m waypoints.</summary>
	private List<Vector3> AllTrailWaypoints()
	{
		var all = new List<Vector3>();
		var path = (GetTree().GetFirstNodeInGroup("autotest_route") ?? GetTree().GetFirstNodeInGroup("trail")) as Path3D;
		if (path is not { Curve: not null } trail) return all;
		var pts = trail.Curve.GetBakedPoints();
		float acc = 0; Vector3 prev = pts.Length > 0 ? pts[0] : Vector3.Zero;
		foreach (var p in pts) { acc += p.DistanceTo(prev); prev = p; if (acc >= 6f || all.Count == 0) { all.Add(trail.GlobalTransform * p); acc = 0; } }
		all.Add(trail.GlobalTransform * pts[^1]);
		return all;
	}

	private List<Vector3> BuildRoute()
	{
		var all = AllTrailWaypoints();
		var route = new List<Vector3>();
		if (all.Count > 0)
		{
			// Waypoints every ~6 m, starting from the nearest point ahead of the player.
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

	/// <summary>The same hidden test route, walked back toward the trailhead, ending at the cabin door.</summary>
	private List<Vector3> BuildReturnRoute()
	{
		var all = AllTrailWaypoints();
		var route = new List<Vector3>();
		for (int i = all.Count - 1; i >= 0; i--) route.Add(all[i]);
		if (GetTree().GetFirstNodeInGroup("cabin") is Cabin cabin)
		{
			// Straight in from well out front, so a straight-line walk never clips a side wall.
			route.Add(cabin.WideApproachPoint);
			route.Add(cabin.ApproachPoint);
		}
		return route;
	}

	/// <summary>
	/// Follows the trail from wherever the player currently stands toward <paramref name="target"/>
	/// (which should sit at or near the trail itself, like the bridge), rather than trusting a
	/// single blind straight-line walk across open forest to get there.
	/// </summary>
	private List<Vector3> BuildTrailRouteTo(Vector3 target)
	{
		var all = AllTrailWaypoints();
		var route = new List<Vector3>();
		if (all.Count > 0)
		{
			int nearest = 0; float best = float.MaxValue;
			for (int i = 0; i < all.Count; i++)
			{
				float d = Flat(all[i]).DistanceTo(Flat(_player.GlobalPosition));
				if (d < best) { best = d; nearest = i; }
			}
			// Walk forward (toward the far end of the trail) if the target is that way, otherwise
			// backward toward the trailhead — whichever direction actually closes the distance.
			int step = Flat(all[Mathf.Min(nearest + 1, all.Count - 1)]).DistanceTo(Flat(target))
				< Flat(all[Mathf.Max(nearest - 1, 0)]).DistanceTo(Flat(target)) ? 1 : -1;
			for (int i = nearest; i >= 0 && i < all.Count; i += step)
			{
				route.Add(all[i]);
				if (Flat(all[i]).DistanceTo(Flat(target)) < 15f) break;
			}
		}
		route.Add(target);
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
