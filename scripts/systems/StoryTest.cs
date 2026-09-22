using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// The full-story autotest (`-- --autotest`): plays the game from the trailhead to the giant's touch,
/// across both levels, the way a player would, and checks every beat on the way.
///
/// How it drives: only through <see cref="PlayerInput"/>'s Scripted fields (move, run, focus, photo,
/// interact) plus teleports for the long walks between beats. The last stretch into every trigger is
/// always walked, so the trigger itself, the pick volumes and the story gating are exercised for real;
/// the report says how many metres were walked and how many were jumped.
///
/// How it survives the level change: the climb in Act 2 loads the Hollow, which frees this node. All run
/// state lives in statics (<see cref="Session"/>); GameFlow adds a fresh StoryTest to the Hollow, which
/// resumes at the step the climb handed over to.
///
/// Speed: long story sequences (the climb, the giant, the radio, the last climb) run at
/// <see cref="Engine.TimeScale"/> 3; walking runs at 1. The report, one screenshot per beat and the exit
/// code (1 on any failed check, 2 on a harness exception) go to test-output/story/.
/// </summary>
public partial class StoryTest : Node
{
	// ------------------------------------------------------------------ session (survives level changes)

	private sealed class Session
	{
		public int Step;
		public readonly List<(string act, string name, bool ok, string detail)> Checks = new();
		public ulong StartMs = Time.GetTicksMsec();
		public int Shot;
		public float Walked, Jumped;
		public float FpsSum; public int FpsCount; public float FpsMin = 999f;
		public string CurrentAct = "";
	}

	private static Session _s;
	private static readonly string OutDir = OutputDir();

	internal static string OutputDir()
	{
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--test-out=")) return "res://test-output/" + a["--test-out=".Length..];
		return "res://test-output/story";
	}

	private PlayerController _player;
	private PlayerInput _input;
	private PlayerInventory _inv;
	private CancellationTokenSource _cts;
	private Vector3 _lastPos;
	private double _fpsTimer;

	private record StepDef(string Act, string Level, Func<CancellationToken, Task> Run);
	private List<StepDef> _steps;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutDir));
		using (FileAccess.Open($"{OutDir}/.gdignore", FileAccess.ModeFlags.Write)) { }
		_s ??= new Session();
		_cts = new CancellationTokenSource();
		TreeExiting += () => { _cts.Cancel(); Engine.TimeScale = 1.0; };
		_steps = BuildSteps();
		RunAll();
	}

	private async void RunAll()
	{
		try
		{
			await WaitUntil(() => GameFlow.Instance is { Started: true } && StoryBeat.Player(this) != null, 20);
			_player = StoryBeat.Player(this);
			_input = _player.PlayerInput;
			_inv = _player.Inventory;
			_input.Scripted = true;
			_lastPos = _player.GlobalPosition;
			string level = GetTree().CurrentScene?.SceneFilePath ?? "";
			while (_s.Step < _steps.Count)
			{
				var step = _steps[_s.Step];
				if (step.Level != level)
				{
					Check("level", $"the story is in the right level for '{step.Act}'", false, $"expected {step.Level}, loaded {level}");
					Finish();
					return;
				}
				_s.CurrentAct = step.Act;
				GD.Print($"[storytest] ---- {step.Act}");
				var t0 = Time.GetTicksMsec();
				await step.Run(_cts.Token);
				if (_cts.IsCancellationRequested) return;   // the level changed under us: the next level's StoryTest resumes
				GD.Print($"[storytest] {step.Act} took {(Time.GetTicksMsec() - t0) / 1000.0:0.0}s");
				_s.Step++;
			}
			Finish();
		}
		catch (OperationCanceledException) { }
		catch (Exception e)
		{
			Check(_s.CurrentAct, "the run finished without a harness exception", false, e.ToString());
			Finish(2);
		}
	}

	public override void _Process(double delta)
	{
		if (_player == null || !IsInstanceValid(_player)) return;
		// Walked distance (teleports are counted separately by Teleport()).
		float moved = Flat(_player.GlobalPosition).DistanceTo(Flat(_lastPos));
		if (moved < 3f) _s.Walked += moved;
		_lastPos = _player.GlobalPosition;
		_fpsTimer += delta / Math.Max(Engine.TimeScale, 0.01);
		if (_fpsTimer >= 0.5 && Engine.TimeScale == 1.0)
		{
			_fpsTimer = 0;
			float fps = (float)Engine.GetFramesPerSecond();
			_s.FpsSum += fps; _s.FpsCount++; _s.FpsMin = Mathf.Min(_s.FpsMin, fps);
		}
	}

	// ------------------------------------------------------------------ the story, step by step

	private List<StepDef> BuildSteps()
	{
		string trail = StoryManager.TrailheadScene, hollow = StoryManager.HollowScene;
		return new List<StepDef>
		{
			new("Act 1: arrival", trail, Act1Arrival),
			new("Act 1: the trail register", trail, Act1Note),
			new("Act 1: the camera and the album", trail, Act1Camera),
			new("Act 1: a photograph", trail, Act1Photo),
			new("Act 1: to the stairs", trail, Act1ToStairs),
			new("Act 2: the climb", trail, Act2Climb),
			new("Act 2: waking in the hollow", hollow, Act2Wake),
			new("Act 3: the camp", hollow, Act3Camp),
			new("Act 4: the giant, and the key", hollow, Act4Giant),
			new("Act 5: the cabin", hollow, Act5Cabin),
			new("Act 6: the bridge and the clearing", hollow, Act6Clearing),
			new("Act 7: the lookout", hollow, Act7Lookout),
			new("Acts 8-10: the bunker", hollow, Act8To10Bunker),
			new("Act 11: the last climb", hollow, Act11),
		};
	}

	private async Task Act1Arrival(CancellationToken ct)
	{
		await Frames(3, ct);
		Check("the player spawned on the ground", _player.IsOnFloor(), $"{_player.GlobalPosition}");
		float eye = _player.CameraRig.Camera.GlobalPosition.Y - _player.GlobalPosition.Y;
		Check("first-person eye height", Mathf.Abs(eye - _player.CameraRig.EyeHeight) < 0.15f, $"{eye:0.00} m");
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act1Start, 5, ct);
		Check("checkpoint 1 reached", StoryManager.Instance.Current >= Checkpoint.Act1Start);
		Check("checkpoint 1 saved", SaveSystem.Load()?.Checkpoint >= Checkpoint.Act1Start, $"{SaveSystem.Load()?.Checkpoint}");
		Check("the forest is alive at the lot", (ForestAmbienceManager.Instance?.Silence ?? 1f) < 0.1f, $"silence {ForestAmbienceManager.Instance?.Silence:0.00}");
		Check("no compass in Act 1", StoryManager.Instance.ObjectivePosition == null);
		if (FirstInGroup<Stalker>("stalker") is { } stalker)
			Check("the stalker waits (dormant at the trailhead)", stalker.Current == Stalker.State.Dormant, $"{stalker.Current}");
		Screenshot("trailhead");
		// Walk a few metres: movement works under scripted input.
		var start = _player.GlobalPosition;
		_input.ScriptedMove = new Vector2(0, 1);
		await Seconds(1.5, ct);
		_input.ScriptedMove = Vector2.Zero;
		Check("the player can walk", Flat(_player.GlobalPosition).DistanceTo(Flat(start)) > 2f, $"{Flat(_player.GlobalPosition).DistanceTo(Flat(start)):0.0} m");
	}

	private async Task Act1Note(CancellationToken ct)
	{
		// R.H. is a stranger: the only trace of him at the trailhead is his last entry in the register.
		Check("no note from a friend at the trailhead", !AllOf<Readable>().Any(r => r.ReadFlag == "read_friends_note"));
		var register = AllOf<Readable>().FirstOrDefault(r => (r.Text ?? "").Contains("R.H.  the steps"));
		Check("the trail register carries R.H.'s last entry", register != null);
		if (register != null) await ReadIt(register, ct);
	}

	private async Task Act1Camera(CancellationToken ct)
	{
		// The opening hands the player the camera out of the car's trunk; nothing lies on the ground.
		Check("the camera came out of the car", _inv.HasCamera);
		Check("no camera left lying around", !Pickups(ToolKind.Camera).Any());
		Check("the opening ran at the car", GetTree().GetFirstNodeInGroup("opening") is OpeningAtCar);
		var page = FirstOf<PhotoLogPage>();
		_input.ScriptedPhotoLog = true; await Frames(3, ct); _input.ScriptedPhotoLog = false; await Frames(3, ct);
		Check("Tab opens the album", page is { IsOpen: true });
		Screenshot("album");
		_input.ScriptedPhotoLog = true; await Frames(3, ct); _input.ScriptedPhotoLog = false; await Frames(3, ct);
		Check("Tab closes it again", page is not { IsOpen: true });
	}

	private async Task Act1Photo(CancellationToken ct)
	{
		var bird = AllOf<Bird>().Where(b => b.IsInGroup("photo_birds"))
			.OrderBy(b => b.GlobalPosition.DistanceTo(_player.GlobalPosition)).FirstOrDefault();
		Check("a bird to photograph", bird != null);
		if (bird == null || !_inv.HasCamera) return;
		int before = PhotoLog.Instance?.RecordedCount ?? 0;
		Vector3 to = Flat(bird.GlobalPosition - _player.GlobalPosition).Normalized();
		if (to == Vector3.Zero) to = Vector3.Forward;
		await Teleport(bird.GlobalPosition - to * 6f, bird.GlobalPosition, ct);
		_input.ScriptedFocus = true;
		await Seconds(0.6, ct);
		await Aim(bird.GlobalPosition + Vector3.Up * 0.05f, ct);
		await Seconds(0.3, ct);
		var vf = FirstOf<CameraViewfinder>();
		Check("the viewfinder locks focus on the bird", vf is { FocusLocked: true });
		_input.ScriptedPhoto = true; await Frames(4, ct); _input.ScriptedPhoto = false;
		await Seconds(0.8, ct);
		Screenshot("bird_photo");
		_input.ScriptedFocus = false;
		Check("the photo is logged", (PhotoLog.Instance?.RecordedCount ?? 0) > before, $"{before} -> {PhotoLog.Instance?.RecordedCount}");
		await Seconds(0.5, ct);
	}

	private async Task Act1ToStairs(CancellationToken ct)
	{
		var terrain = GroundSnap.FindTerrain(this);
		var climb = AllOf<FirstClimbEvent>().FirstOrDefault();
		Check("the first staircase is in the trailhead level", climb != null);
		if (terrain == null || climb == null) return;
		// Jump to 25 m before the trail's end (the fallen fir), then walk the rest along the
		// test route: around the fir's crown and along the pavers to the foot of the stairs.
		float s = Mathf.Max(0f, terrain.TrailLength - 25f);
		var start = terrain.TrailPoint(s, out var tangent);
		await Teleport(start, start + tangent * 10f, ct);
		var route = RouteFrom(_player.GlobalPosition);
		bool ok = true;
		foreach (var p in route)
		{
			if (Flat(p).DistanceTo(Flat(climb.GlobalPosition)) < 4f) break;
			if (!await WalkTo(p, 2.2f, ct)) { ok = false; break; }
		}
		Check("walked from the fallen fir to the stairs", ok, $"{_player.GlobalPosition}");
		Screenshot("stairs_found");
		Check("the forest falls silent by the stairs", (ForestAmbienceManager.Instance?.Silence ?? 0f) > 0.8f, $"silence {ForestAmbienceManager.Instance?.Silence:0.00}");
	}

	private async Task Act2Climb(CancellationToken ct)
	{
		var climb = AllOf<FirstClimbEvent>().FirstOrDefault();
		if (climb == null) { Check("the climb trigger", false); return; }
		bool cameraGone = false;
		void Watch(Checkpoint cp)
		{
			if (cp != Checkpoint.Act2StairsClimbed) return;
			// The climb hands the story to the Hollow in this same frame: record and move on.
			Check("checkpoint 2 reached at the top", true);
			Check("the camera is gone after the first step", cameraGone || !_inv.HasCamera);
			_s.Step++;
			Engine.TimeScale = 1.0;
		}
		StoryManager.Instance.CheckpointReached += Watch;
		try
		{
			await WalkTo(climb.GlobalPosition, 0.6f, ct, stopWhen: () => !_input.Enabled);
			await WaitUntil(() => !_input.Enabled, 5, ct);
			Check("stepping on the stairs takes control away", !_input.Enabled);
			cameraGone = !_inv.HasCamera;
			Engine.TimeScale = 3.0;
			await Seconds(8, ct);
			Screenshot("climb");
			await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act2StairsClimbed, 40, ct);
			// Normally the level change cancels us before this line.
			if (!ct.IsCancellationRequested) await Seconds(5, ct);
			Check("the climb carried the player to the Hollow", false, "no level change within 5 s of checkpoint 2");
			Finish();
		}
		finally { if (StoryManager.Instance != null) StoryManager.Instance.CheckpointReached -= Watch; }
	}

	private async Task Act2Wake(CancellationToken ct)
	{
		await WaitUntil(() => _input.Enabled, 30, ct);
		Check("control comes back after the wake-up", _input.Enabled);
		Check("checkpoint 2 on arrival", StoryManager.Instance.Current == Checkpoint.Act2StairsClimbed, $"{StoryManager.Instance.Current}");
		var spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
		Check("woke at the Hollow's wake spot", spawn != null && Flat(_player.GlobalPosition).DistanceTo(Flat(spawn.GlobalPosition)) < 3f,
			$"{_player.GlobalPosition} vs {spawn?.GlobalPosition}");
		Check("still no camera", !_inv.HasCamera);
		Check("no compass yet", StoryManager.Instance.ObjectivePosition == null);
		Check("the save says checkpoint 2", SaveSystem.Load()?.Checkpoint == Checkpoint.Act2StairsClimbed);
		Screenshot("wake");
	}

	private async Task Act3Camp(CancellationToken ct)
	{
		var lantern = Pickups(ToolKind.Lantern).FirstOrDefault();
		var compass = Pickups(ToolKind.Compass).FirstOrDefault();
		Check("the lantern and compass wait at the camp", lantern != null && compass != null);
		if (lantern == null || compass == null) return;
		await WalkTo(lantern.GlobalPosition, 1.5f, ct);
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act3DoorBoarded, 5, ct);
		Check("checkpoint 3 at the camp", StoryManager.Instance.Current >= Checkpoint.Act3DoorBoarded, $"{StoryManager.Instance.Current}");
		var campNote = AllOf<Readable>().OrderBy(r => r.GlobalPosition.DistanceTo(lantern.GlobalPosition)).FirstOrDefault();
		if (campNote != null && campNote.GlobalPosition.DistanceTo(lantern.GlobalPosition) < 6f) await ReadIt(campNote, ct);
		else Check("R.H.'s note at the camp", false);
		await UseIt(lantern, ct);
		await UseIt(compass, ct);
		Check("lantern and compass in hand", _inv.HasLantern && _inv.HasCompass);
		Check("the compass shows", FirstOf<Compass>() is { ShowingCompass: true });
		CheckObjective("the compass points at the cabin", "cabin");
		Screenshot("camp");
		// Out of the safe zone along the path (walked, not jumped: the key and the giant are further on): the storm.
		await WalkTrailFor(30f, ct, () => StormController.Instance is { Active: true });
		await WaitUntil(() => StormController.Instance is { Active: true }, 6, ct);
		Check("the storm starts on leaving the camp", StormController.Instance is { Active: true });
		await WaitUntil(() => (ForestAmbienceManager.Instance?.Silence ?? 0f) > 0.8f, 8, ct);   // it hushes over a few seconds
		Check("the storm silences the forest", (ForestAmbienceManager.Instance?.Silence ?? 0f) > 0.8f, $"silence {ForestAmbienceManager.Instance?.Silence:0.00}");
	}

	private async Task Act4Giant(CancellationToken ct)
	{
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		if (cabin == null) { Check("the cabin exists", false); return; }
		var key = Pickups(ToolKind.Key).FirstOrDefault();
		Check("the key lies on the way", key != null);
		if (key != null)
		{
			await WalkAlongTrail(key.GlobalPosition, 60f, ct);
			await WalkTo(key.GlobalPosition, 1.3f, ct);
			await UseIt(key, ct);
			Check("key picked up", _inv.HasTool(ToolKind.Key));
		}
		// The giant fires on its (test-length) fuse during the storm, or at the latest on the approach to the cabin.
		await WaitUntil(() => StoryManager.Instance.GiantEventDone, 12, ct);
		if (!StoryManager.Instance.GiantEventDone) await WalkAlongTrail(cabin.GlobalPosition, 70f, ct, () => StoryManager.Instance.GiantEventDone);
		if (FindUnder<Node3D>(GetTree().CurrentScene, "Act4Giant") != null) Screenshot("giant");
		await WaitUntil(() => StoryManager.Instance.GiantEventDone, 20, ct);
		Check("the giant crossed before the cabin", StoryManager.Instance.GiantEventDone);
		CheckObjective("the compass still points at the cabin", "cabin");
	}

	private async Task Act5Cabin(CancellationToken ct)
	{
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		if (cabin == null) return;
		await WalkAlongTrail(cabin.GlobalPosition, 35f, ct);
		await WalkTo(cabin.WideApproachPoint, 2f, ct);
		await WalkTo(cabin.ApproachPoint, 1f, ct);
		Check("the door is boarded", cabin.DoorBoarded && !cabin.IsOpen);
		Screenshot("cabin_boarded");
		var axe = Pickups(ToolKind.Axe).FirstOrDefault();
		Check("the axe is somewhere near the cabin", axe != null && axe.GlobalPosition.DistanceTo(cabin.GlobalPosition) < 80f, $"{axe?.GlobalPosition}");
		if (axe != null)
		{
			await Teleport(axe.GlobalPosition + Flat(cabin.GlobalPosition - axe.GlobalPosition).Normalized() * 3f, axe.GlobalPosition, ct);
			await WalkTo(axe.GlobalPosition, 1.3f, ct);
			await UseIt(axe, ct);
			Check("axe picked up", _inv.HasTool(ToolKind.Axe));
		}
		await Teleport(cabin.WideApproachPoint, cabin.DoorCenter, ct);
		await WalkTo(cabin.ApproachPoint, 1f, ct);
		await WalkTo(cabin.DoorCenter, 1.6f, ct);
		_input.ScriptedMove = Vector2.Zero;
		await Aim(cabin.DoorCenter, ct);
		await Press(ct, hold: 0.3);
		await WaitUntil(() => cabin.IsOpen, 10, ct);
		Check("the axe breaks the boards", cabin.IsOpen);
		Check("the axe is used up", !_inv.HasTool(ToolKind.Axe));
		Vector3 inside = cabin.ToGlobal(new Vector3(0.3f, 0f, -0.8f));
		bool walkedIn = await WalkTo(cabin.DoorCenter, 0.6f, ct) && await WalkTo(inside, 1f, ct);
		if (!walkedIn) { Check("walked in through the doorway", false, "placed inside"); await Teleport(inside, inside + Vector3.Forward, ct, count: false); }
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act5CabinEntered, 15, ct);
		Check("checkpoint 4 inside the cabin", StoryManager.Instance.Current >= Checkpoint.Act5CabinEntered);
		Check("the friend is not there", FindUnder<Node3D>(cabin, "Friend") is not { Visible: true } || AllOf<FriendBody>().Count == 0);
		Screenshot("cabin_inside");
		var post = Pickups(ToolKind.NewelPost).FirstOrDefault();
		Check("the newel post is on the table", post != null);
		if (post != null)
		{
			await WalkTo(post.GlobalPosition, 1.1f, ct);
			await UseIt(post, ct);
		}
		Check("newel post in hand", _inv.HasNewelPost);
		CheckObjective("the compass points at the footbridge", "bridge_marker");
		await WalkTo(cabin.DoorCenter, 0.6f, ct);
		await WalkTo(cabin.GlobalTransform * new Vector3(-6f, 0f, 3.5f), 1f, ct);
		await WaitUntil(() => StoryManager.Instance.HasFlag(StoryManager.Flag.DawnBroke), 8, ct);
		Check("dawn breaks on stepping out", StoryManager.Instance.HasFlag(StoryManager.Flag.DawnBroke));
		Check("the storm is over", StormController.Instance is not { Active: true });
		Screenshot("dawn");
	}

	private async Task Act6Clearing(CancellationToken ct)
	{
		var bridge = GetTree().GetFirstNodeInGroup("bridge_marker") as Node3D;
		var terrain = GroundSnap.FindTerrain(this);
		if (bridge == null || terrain == null) { Check("the footbridge exists", false); return; }
		terrain.TrailDistance(bridge.GlobalPosition.X, bridge.GlobalPosition.Z, out float sb);
		var before = terrain.TrailPoint(sb - 14f, out _);
		var after = terrain.TrailPoint(sb + 10f, out _);
		await Teleport(before, bridge.GlobalPosition, ct);
		await WalkTo(bridge.GlobalPosition, 1.5f, ct);
		await WalkTo(after, 1.5f, ct);
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act6BridgeCrossed, 6, ct);
		Check("checkpoint 5 on crossing the footbridge", StoryManager.Instance.Current >= Checkpoint.Act6BridgeCrossed);
		CheckObjective("the compass points at the clearing", "stairs_clearing_marker");
		var clearing = GetTree().GetFirstNodeInGroup("stairs_clearing_marker") as Node3D;
		if (clearing == null) return;
		var act6 = AllOf<Act6ClearingEvent>().FirstOrDefault();
		Check("fifteen small stairs in the clearing", act6?.MiniStairCount == 15, $"{act6?.MiniStairCount}");
		await WalkAlongTrail(clearing.GlobalPosition, 40f, ct);
		await WalkTo(clearing.GlobalPosition, 6f, ct, stopWhen: () => StoryManager.Instance.ClearingVoiceHeard);
		await WaitUntil(() => StoryManager.Instance.ClearingVoiceHeard, 10, ct);
		Check("the clearing's voice", StoryManager.Instance.ClearingVoiceHeard);
		await WaitUntil(() => !_inv.HasNewelPost, 10, ct);
		Check("the newel post leaves the player's hands", !_inv.HasNewelPost);
		await WaitUntil(() => act6?.OriginalStairs is { NewelCapped: true }, 8, ct);
		Check("the cap seats back on the clearing staircase's newel post", act6?.OriginalStairs is { NewelCapped: true });
		Screenshot("clearing");
		CheckObjective("the compass points on to the lookout", "fire_lookout_marker");
		Check("the optional climb did not fire by accident", !StoryManager.Instance.HasFlag(StoryManager.Flag.Act6ExtendedClimb));
	}

	private async Task Act7Lookout(CancellationToken ct)
	{
		var lookout = GetTree().GetFirstNodeInGroup("fire_lookout_marker") as Node3D;
		if (lookout == null) { Check("the lookout exists", false); return; }
		await WalkAlongTrail(lookout.GlobalPosition, 30f, ct);
		await WalkTo(lookout.GlobalPosition, 2.5f, ct, stopWhen: () => StoryManager.Instance.Current >= Checkpoint.Act7CabinBurning);
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act7CabinBurning, 8, ct);
		Check("checkpoint 6 at the lookout", StoryManager.Instance.Current >= Checkpoint.Act7CabinBurning);
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		Check("the cabin is burning", cabin is { Burning: > 0.5f }, $"{cabin?.Burning}");
		if (cabin != null) await Aim(cabin.GlobalPosition + Vector3.Up * 2f, ct);
		await Seconds(1.5, ct);
		Screenshot("cabin_burning");
		CheckObjective("the compass points at the bunker", "bunker_marker");
	}

	private async Task Act8To10Bunker(CancellationToken ct)
	{
		if (GetTree().GetFirstNodeInGroup("bunker_marker") is not Bunker bunker) { Check("the bunker exists", false); return; }
		bool Entered() => StoryManager.Instance.Current >= Checkpoint.Act8BunkerEntered;
		await WalkAlongTrail(bunker.ApproachPointWorld, 40f, ct, Entered);
		if (!Entered()) await WalkTo(bunker.ApproachPointWorld, 0.8f, ct, stopWhen: Entered);
		await WaitUntil(() => bunker.IsOpen, 8, ct);
		Check("the bunker door opens", bunker.IsOpen);
		Screenshot("bunker_open");
		if (!Entered()) await WalkTo(bunker.EntryPointWorld, 0.3f, ct, stopWhen: Entered);
		await WaitUntil(Entered, 12, ct);
		Check("checkpoint 7 inside the bunker", Entered());
		var bi = BunkerInterior.Instance;
		if (bi == null) { Check("bunker interior", false); return; }
		await WaitUntil(() => _player.GlobalPosition.DistanceTo(bi.GlobalPosition) < 500f && _input.Enabled, 8, ct);
		Check("carried into the interior", _player.GlobalPosition.DistanceTo(bi.GlobalPosition) < 500f);
		Check("indoors (forest audio off)", ForestAmbienceManager.Instance?.IsIndoor ?? false);
		if (FirstInGroup<Stalker>("stalker") is { } st) Check("the forest stalker stays outside", st.Current == Stalker.State.Dormant, $"{st.Current}");

		// Act 8: the hallway.
		bool reached = await WalkTo(bi.VineDoorApproachWorld + new Vector3(0, 0, -4f), 0.6f, ct);
		Check("walked the hallway", reached);
		Check("the lights have gone red", bi.RedTriggered);
		Screenshot("hallway");
		await Aim(bi.VineDoorInteractWorld, ct);
		await Press(ct);
		Check("the vine door opens", bi.VineDoorOpenState);
		await WalkTo(bi.CrtRoomInteriorWorld, 1.2f, ct);

		// Act 9: the screens.
		await WalkTo(bi.CrtTargetApproachWorld, 1.0f, ct);
		await Aim(bi.CrtSwitchWorld, ct);
		await Press(ct);
		await Seconds(0.3, ct);
		Check("the screens go dark", bi.ScreensOff);
		Engine.TimeScale = 3.0;
		await WaitUntil(() => StoryManager.Instance.CrtPuzzleDone, 10, ct);
		Engine.TimeScale = 1.0;
		Check("the screens come back showing the stairs", StoryManager.Instance.CrtPuzzleDone);
		Screenshot("crt_room");
		CheckObjective("the compass points at the way out", "bunker_entrance_marker");

		// Act 10: the maze.
		double t = 0;
		while (!bi.MazeActive && t < 15)
		{
			ct.ThrowIfCancellationRequested();
			Steer(bi.VineDoorApproachWorld);
			_input.ScriptedMove = new Vector2(0, 1);
			await Frames(1, ct);
			t += GetProcessDeltaTime();
		}
		_input.ScriptedMove = Vector2.Zero;
		Check("the hallway has become a maze", bi.MazeActive);
		if (bi.MazeSolutionWaypointsWorld is { Count: > 0 } maze)
			for (int i = 0; i < maze.Count; i++) await WalkTo(maze[i], i == maze.Count - 1 ? 1.2f : 1.5f, ct);
		Screenshot("maze_exit");
		var walkie = AllOf<WalkiePickup>().FirstOrDefault();
		Check("the walkie-talkie is waiting", walkie != null);
		if (walkie != null)
		{
			await WalkTo(walkie.GlobalPosition, 1.2f, ct);
			await UseIt(walkie, ct);
		}
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act10WalkieFound, 6, ct);
		Check("checkpoint 8 with the radio", StoryManager.Instance.Current >= Checkpoint.Act10WalkieFound);
		Check("the radio is in the saved inventory", (SaveSystem.Load()?.Inventory ?? "").Contains("radio"), SaveSystem.Load()?.Inventory);
	}

	private async Task Act11(CancellationToken ct)
	{
		Engine.TimeScale = 3.0;
		await WaitUntil(() => StoryManager.Instance.Act11DialogueDone, 40, ct);
		Engine.TimeScale = 1.0;
		Check("the radio exchange plays", StoryManager.Instance.Act11DialogueDone);
		CheckObjective("the compass points at the last staircase", "final_stairs_marker");
		var act11 = AllOf<Act11Ending>().FirstOrDefault();
		var target = GetTree().GetFirstNodeInGroup("final_stairs_marker") as Node3D;
		Check("the last staircase exists, tall", act11 is { StairsTall: true }, $"{act11?.StairsTall}");
		if (act11 == null || target == null) return;
		await WalkAlongTrail(target.GlobalPosition, 40f, ct);
		if (act11.ApproachWorld is { } approach) await WalkTo(approach, 1f, ct);
		Screenshot("last_stairs");
		if (act11.ClimbTriggerWorld is { } trig) await WalkTo(trig, 0.8f, ct, stopWhen: () => !_input.Enabled);
		Engine.TimeScale = 3.0;
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act11GiantEncounter, 90, ct);
		Check("checkpoint 9: the giant's touch", StoryManager.Instance.Current >= Checkpoint.Act11GiantEncounter);
		await WaitUntil(() => _input.Enabled, 20, ct);
		Engine.TimeScale = 1.0;
		var clearing = GetTree().GetFirstNodeInGroup("stairs_clearing_marker") as Node3D;
		Check("woke in the clearing", clearing != null && Flat(_player.GlobalPosition).DistanceTo(Flat(clearing.GlobalPosition)) < 40f, $"{_player.GlobalPosition}");
		Check("dawn", StoryBeat.Atmosphere(this)?.CurrentMood == ForestAtmosphere.Mood.Dawn, $"{StoryBeat.Atmosphere(this)?.CurrentMood}");
		Check("control is back for the last walk", _input.Enabled);
		await Seconds(1.5, ct);
		Screenshot("ending");
	}

	// ------------------------------------------------------------------ bot

	private async Task<bool> WalkTo(Vector3 target, float radius, CancellationToken ct, Func<bool> stopWhen = null, float giveUp = 8f)
	{
		double stuck = 0, sidestep = 0; float best = float.MaxValue, dir = 1f;
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				if (stopWhen != null && stopWhen()) return true;
				float d = Flat(_player.GlobalPosition).DistanceTo(Flat(target));
				if (d < radius) return true;
				if (!_input.Enabled) { _input.ScriptedMove = Vector2.Zero; await Frames(1, ct); continue; }   // a beat has control
				if (d < best - 0.3f) { best = d; stuck = 0; }
				stuck += GetProcessDeltaTime();
				if (stuck > giveUp) return false;
				Steer(target);
				_input.ScriptedRun = d > 8f;
				if (stuck > 1.2 && sidestep <= 0) { sidestep = 0.9; dir = -dir; }
				if (sidestep > 0) { sidestep -= GetProcessDeltaTime(); _input.ScriptedMove = new Vector2(dir, 0.4f); }
				else _input.ScriptedMove = new Vector2(0, 1);
				await Frames(1, ct);
			}
		}
		finally { if (_input != null) { _input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false; } }
	}

	/// <summary>Jumps along the trail to <paramref name="lastMetres"/> short of the point nearest the target, then walks the rest along the trail.</summary>
	private async Task WalkAlongTrail(Vector3 target, float lastMetres, CancellationToken ct, Func<bool> stopWhen = null)
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain == null) { await WalkTo(target, 2f, ct, stopWhen); return; }
		terrain.TrailDistance(target.X, target.Z, out float sTarget);
		terrain.TrailDistance(_player.GlobalPosition.X, _player.GlobalPosition.Z, out float sNow);
		float sStart = Mathf.Max(sNow, sTarget - lastMetres);
		if (sStart - sNow > 8f)
		{
			var p = terrain.TrailPoint(sStart, out var tan);
			await Teleport(p, p + tan * 5f, ct);
		}
		for (float s = sStart + 6f; s < sTarget; s += 6f)
		{
			if (stopWhen != null && stopWhen()) return;
			if (!await WalkTo(terrain.TrailPoint(s, out _), 2.5f, ct, stopWhen)) break;
		}
		await WalkTo(target, 2.5f, ct, stopWhen);
	}

	/// <summary>Walks on along the trail from where the player stands, for <paramref name="metres"/>.</summary>
	private async Task WalkTrailFor(float metres, CancellationToken ct, Func<bool> stopWhen = null)
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain == null) return;
		terrain.TrailDistance(_player.GlobalPosition.X, _player.GlobalPosition.Z, out float s0);
		for (float s = s0 + 6f; s <= s0 + metres; s += 6f)
		{
			if (stopWhen != null && stopWhen()) return;
			if (!await WalkTo(terrain.TrailPoint(s, out _), 2.5f, ct, stopWhen)) return;
		}
	}

	private List<Vector3> RouteFrom(Vector3 from)
	{
		var list = new List<Vector3>();
		if (GetTree().GetFirstNodeInGroup("autotest_route") is not Path3D { Curve: not null } path) return list;
		var pts = path.Curve.GetBakedPoints();
		int start = 0; float best = float.MaxValue;
		for (int i = 0; i < pts.Length; i++)
		{
			float d = Flat(path.ToGlobal(pts[i])).DistanceTo(Flat(from));
			if (d < best) { best = d; start = i; }
		}
		Vector3 last = from;
		for (int i = start; i < pts.Length; i++)
		{
			var g = path.ToGlobal(pts[i]);
			if (Flat(g).DistanceTo(Flat(last)) >= 5f) { list.Add(g); last = g; }
		}
		return list;
	}

	private async Task Teleport(Vector3 at, Vector3 faceToward, CancellationToken ct, bool count = true)
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null) at.Y = Mathf.Max(at.Y, terrain.HeightAt(at.X, at.Z));
		if (count) _s.Jumped += Flat(at).DistanceTo(Flat(_player.GlobalPosition));
		var d = Flat(faceToward - at);
		_player.Teleport(at + Vector3.Up * 0.2f, d.LengthSquared() > 0.01f ? Mathf.Atan2(-d.X, -d.Z) : _player.CameraRig.Yaw);
		_lastPos = _player.GlobalPosition;
		await Frames(3, ct);
	}

	private void Steer(Vector3 target)
	{
		var to = Flat(target - _player.GlobalPosition);
		if (to.LengthSquared() < 0.01f) return;
		float want = Mathf.Atan2(-to.X, -to.Z);
		float diff = Mathf.AngleDifference(_player.CameraRig.Yaw, want);
		_input.AddScriptedLook(new Vector2(Mathf.Clamp(diff, -0.09f, 0.09f), 0));
	}

	/// <summary>Points the view straight at a world point (yaw and pitch), like a player lining up the crosshair.</summary>
	private async Task Aim(Vector3 point, CancellationToken ct)
	{
		var eye = _player.CameraRig.Camera.GlobalPosition;
		var d = point - eye;
		float yaw = Mathf.Atan2(-d.X, -d.Z);
		float pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
		_player.CameraRig.SnapBehind(yaw);
		_player.Visual.Rotation = new Vector3(0, yaw, 0);
		_player.CameraRig.SetPitch(pitch);
		await Frames(3, ct);
	}

	private async Task Press(CancellationToken ct, double hold = 0.08)
	{
		_input.ScriptedInteract = true;
		await Frames(3, ct);
		await Seconds(hold, ct);
		_input.ScriptedInteract = false;
		await Frames(3, ct);
	}

	/// <summary>Looks at an object's Interactable and presses (or holds) E, reporting what was focused.</summary>
	private async Task UseIt(Node3D obj, CancellationToken ct)
	{
		if (obj == null || !IsInstanceValid(obj) || !obj.IsInsideTree() || obj is Pickup { Taken: true }) return;
		var use = obj as Interactable ?? obj.FindChildren("*", nameof(Interactable), true, false).OfType<Interactable>().FirstOrDefault()
			?? FindUnder<Interactable>(obj, null);
		var aim = use != null ? use.ToGlobal(use.PickOffset) : obj.GlobalPosition + Vector3.Up * 0.15f;
		if (Flat(_player.GlobalPosition).DistanceTo(Flat(aim)) > 2.2f) await WalkTo(aim, 1.6f, ct);
		await Aim(aim, ct);
		var focus = _player.Interaction?.Focused;
		GD.Print($"[storytest] use {obj.Name}: focused '{focus?.GetParent()?.Name}' prompt '{_player.Interaction?.PromptText}'");
		await Press(ct, hold: use != null && use.HoldSeconds > 0 ? use.HoldSeconds + 0.3 : 0.08);
		await Seconds(0.3, ct);
	}

	private async Task ReadIt(Readable note, CancellationToken ct)
	{
		var paper = note.GetParent() as Node3D ?? note;
		Vector3 normal = paper.GlobalBasis.Z.Normalized();
		if (Mathf.Abs(normal.Y) > 0.7f)
		{
			// Lying flat: come at it from its open side, away from anything sharing the surface (a lantern on the
			// same stump stands between the eye and the page from the far side, exactly as it would for a player).
			var near = AllOf<Pickup>().Where(pk => !pk.Taken && pk.GlobalPosition.DistanceTo(paper.GlobalPosition) < 1.2f).ToList();
			normal = near.Count > 0
				? Flat(paper.GlobalPosition - near.Aggregate(Vector3.Zero, (a, b) => a + b.GlobalPosition) / near.Count).Normalized()
				: Flat(_player.GlobalPosition - paper.GlobalPosition).Normalized();
			if (normal == Vector3.Zero) normal = Flat(_player.GlobalPosition - paper.GlobalPosition).Normalized();
		}
		else normal = Flat(normal).Normalized();
		var stand = paper.GlobalPosition + normal * 1.3f;
		await Teleport(stand, paper.GlobalPosition, ct, count: Flat(stand).DistanceTo(Flat(_player.GlobalPosition)) > 4f);
		await Aim(note.ToGlobal(note.PickOffset), ct);
		await Press(ct);
		await Seconds(0.3, ct);
		bool open = NoteOverlay.Instance is { IsOpen: true };
		Check($"the note opens ('{Trim(note.Text)}')", open);
		if (open) Screenshot("note");
		// The overlay closes on a real key event (E/Esc); scripted input can't send one, so close it directly.
		NoteOverlay.Instance?.Close();
		await Seconds(0.3, ct);
		Check("the note closes and control returns", NoteOverlay.Instance is not { IsOpen: true } && !_input.Modal);
	}

	// ------------------------------------------------------------------ checks and output

	private void CheckObjective(string name, string group)
	{
		var node = GetTree().GetFirstNodeInGroup(group) as Node3D;
		var obj = StoryManager.Instance.ObjectivePosition;
		Check(name, node != null && obj != null && obj.Value.DistanceTo(node.GlobalPosition) < 3f, $"objective {obj}, {group} {node?.GlobalPosition}");
	}

	private void Check(string name, bool ok, string detail = "") => Check(_s.CurrentAct, name, ok, detail);

	private static void Check(string act, string name, bool ok, string detail)
	{
		_s.Checks.Add((act, name, ok, detail ?? ""));
		GD.Print($"[storytest] {(ok ? "PASS" : "FAIL")} {name} {detail}");
	}

	private void Screenshot(string label)
	{
		try
		{
			var img = GetViewport()?.GetTexture()?.GetImage();
			img?.SavePng(ProjectSettings.GlobalizePath($"{OutDir}/{_s.Shot++:00}_{label}.png"));
		}
		catch (Exception e) { GD.PushWarning($"[storytest] screenshot '{label}' failed: {e.Message}"); }
	}

	private void Finish(int code = -1)
	{
		Engine.TimeScale = 1.0;
		int failed = _s.Checks.Count(c => !c.ok);
		double secs = (Time.GetTicksMsec() - _s.StartMs) / 1000.0;
		var sb = new StringBuilder();
		sb.AppendLine($"Project DS story test  {Time.GetDatetimeStringFromSystem()}");
		sb.AppendLine($"{_s.Checks.Count - failed}/{_s.Checks.Count} passed in {secs:0} s  (walked {_s.Walked:0} m, jumped {_s.Jumped:0} m)");
		if (_s.FpsCount > 0) sb.AppendLine($"fps while walking: avg {_s.FpsSum / _s.FpsCount:0}, min {_s.FpsMin:0}");
		string act = null;
		foreach (var c in _s.Checks)
		{
			if (c.act != act) { act = c.act; sb.AppendLine().AppendLine($"== {act}"); }
			sb.AppendLine($"{(c.ok ? "PASS" : "FAIL")}  {c.name}  {c.detail}");
		}
		using (var f = FileAccess.Open($"{OutDir}/report.txt", FileAccess.ModeFlags.Write)) f?.StoreString(sb.ToString());
		GD.Print(sb.ToString());
		int exit = code >= 0 ? code : failed > 0 ? 1 : 0;
		_s = null;
		GetTree().Quit(exit);
	}

	// ------------------------------------------------------------------ small helpers

	private IEnumerable<Pickup> Pickups(ToolKind kind) => AllOf<Pickup>().Where(p => p.Kind == kind && !p.Taken && p.IsVisibleInTree());

	private List<T> AllOf<T>() where T : Node
	{
		var list = new List<T>();
		void Walk(Node n) { if (n is T t) list.Add(t); foreach (var c in n.GetChildren()) Walk(c); }
		if (GetTree().CurrentScene is { } root) Walk(root);
		return list;
	}

	private T FirstOf<T>() where T : Node => AllOf<T>().FirstOrDefault();
	private T FirstInGroup<T>(string group) where T : Node => GetTree().GetFirstNodeInGroup(group) as T;

	private static T FindUnder<T>(Node root, string name) where T : Node
	{
		if (root == null) return null;
		foreach (var c in root.GetChildren())
		{
			if (c is T t && (name == null || c.Name == name)) return t;
			var deeper = FindUnder<T>(c, name);
			if (deeper != null) return deeper;
		}
		return null;
	}

	private async Task WaitUntil(Func<bool> cond, double seconds, CancellationToken ct = default)
	{
		ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (!cond() && Time.GetTicksMsec() < end)
		{
			ct.ThrowIfCancellationRequested();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Game-time seconds (they shrink with the time scale).</summary>
	private async Task Seconds(double s, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		await ToSignal(GetTree().CreateTimer(s, true, false, false), SceneTreeTimer.SignalName.Timeout);
		ct.ThrowIfCancellationRequested();
	}

	private async Task Frames(int n, CancellationToken ct)
	{
		for (int i = 0; i < n; i++)
		{
			ct.ThrowIfCancellationRequested();
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		ct.ThrowIfCancellationRequested();
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
	private static string Trim(string t) => string.IsNullOrEmpty(t) ? "" : (t.Length > 28 ? t[..28].Replace('\n', ' ') + "..." : t.Replace('\n', ' '));
}
