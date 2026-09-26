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
using ProjectDS.World.StationParts;
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
///
/// Starting part way: `-- --autotest --story-from=6` skips straight to Act 6 (any act 3..11; 8, 9 and 10
/// all mean the bunker step). It writes the save a full run would have reached at that act (checkpoint,
/// flags, inventory: see <see cref="StateFor"/>), Continues into the Hollow, and runs from there to the end.
/// Acts 1 and 2 have no shortcut (they are the trailhead itself). `--story-to=6` stops after Act 6's
/// steps and writes the report, so one act can be run on its own (`--story-from=6 --story-to=6`).
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
		/// <summary>Act 12 is run twice: once letting the hunter catch the boat (the drowning reloads the
		/// checkpoint and this step starts over), then crossing properly.</summary>
		public bool LakeDeathDone;
		/// <summary>Act 13's Room 2 is flooded to the top once on purpose (the reload restarts the step).</summary>
		public bool Room2DeathDone;
		/// <summary>Act 14 is run twice: jumping across, then (after a Continue) jumping down.</summary>
		public bool Act14AcrossDone;
		/// <summary>Act 15 is run twice: once walking on into the red (taken, reloaded), then properly.</summary>
		public bool Act15DeathDone;
		/// <summary>Act 18 is run twice: once standing under a slam (crushed, reloaded), then fought properly.</summary>
		public bool Act18DeathDone;
		public int Act18Attempts;
	}

	private static Session _s;
	private static readonly string OutDir = OutputDir();
	private static bool _fromApplied;

	internal static string OutputDir()
	{
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--test-out=")) return "res://test-output/" + a["--test-out=".Length..];
		return "res://test-output/story";
	}

	/// <summary>`--story-from=&lt;act&gt;`: the act to start at (3..11), or 0 for the whole story.</summary>
	private static int StoryFromArg() => IntArg("--story-from=");
	/// <summary>`--story-to=&lt;act&gt;`: stop (and report) once the steps of this act are done; 0 = run to the end.</summary>
	private static int StoryToArg() => IntArg("--story-to=");

	private static int IntArg(string prefix)
	{
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith(prefix) && int.TryParse(a[prefix.Length..], out int n)) return n;
		return 0;
	}

	/// <summary>The act number a step belongs to (8 for the bunker step).</summary>
	private static int ActOf(StepDef s) => s.Act.StartsWith("Acts 8-10") ? 8 : int.TryParse(s.Act.Split(' ')[1].TrimEnd(':'), out int n) ? n : 0;

	/// <summary>The save a full run would have written by the start of <paramref name="act"/> (3..11).</summary>
	private static (Checkpoint cp, string[] flags, string inventory) StateFor(int act)
	{
		const string F = "";
		string[] f2 = { StoryManager.Flag.StairsClimbed };
		string[] f3 = f2.Concat(new[] { StoryManager.Flag.StormStarted, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, "read_camp_note" }).ToArray();
		string[] f5 = f3.Concat(new[] { StoryManager.Flag.CabinDoorOpen, StoryManager.Flag.PickupTakenAxe, StoryManager.Flag.NewelPostTaken, StoryManager.Flag.DawnBroke, StoryManager.Flag.CodeDigit(1), StoryManager.Flag.CodeDigit(2) }).ToArray();
		string[] f7 = f5.Concat(new[] { StoryManager.Flag.ClearingVoiceHeard, StoryManager.Flag.ClearingLoopDone, StoryManager.Flag.Act6NightFell }).ToArray();
		string[] f8 = f7.Concat(new[] { StoryManager.Flag.GiantEventDone, StoryManager.Flag.CodeDigit(3), StoryManager.Flag.CodeDigit(4) }).ToArray();
		string[] f11 = f8.Concat(new[] { StoryManager.Flag.BunkerUnlocked, StoryManager.Flag.CrtPuzzleDone, StoryManager.Flag.WalkieTaken, StoryManager.Flag.BunkerMazeEntered, "bunker_rooms_scared", StoryManager.Flag.BunkerMazeExited }).ToArray();
		const string gear3 = "lantern,compass;tool=None", gear5 = "lantern,compass,newel_post;tool=None", gear11 = "lantern,compass,newel_post,radio;tool=None";
		return act switch
		{
			3 => (Checkpoint.Act2StairsClimbed, f2, F + ";tool=None"),
			4 or 5 => (Checkpoint.Act3DoorBoarded, f3, gear3),
			6 => (Checkpoint.Act5CabinEntered, f5, gear5),
			7 => (Checkpoint.Act6BridgeCrossed, f7, gear5),
			8 or 9 or 10 => (Checkpoint.Act7CabinBurning, f8, gear5),
			12 => (Checkpoint.Act11GiantEncounter, f11.Concat(new[] { StoryManager.Flag.Act11DialogueDone, StoryManager.Flag.NewelSeated }).ToArray(), gear11),
			13 => (Checkpoint.Act12LakeCrossed, f11.Concat(new[] { StoryManager.Flag.Act11DialogueDone, StoryManager.Flag.NewelSeated }).ToArray(), gear11),
			14 => (Checkpoint.Act13Finished, f11.Concat(new[] { StoryManager.Flag.Act11DialogueDone, StoryManager.Flag.NewelSeated,
				StoryManager.Flag.StationBasementTapeCut, StoryManager.Flag.StationLightsDead, StoryManager.Flag.StationBasementDrained,
				StoryManager.Flag.StationClockBroken, StoryManager.Flag.StationRoom1Open, StoryManager.Flag.StationRoom1Solved,
				StoryManager.Flag.StationRoom2Solved, StoryManager.Flag.StationWebBurned, StoryManager.Flag.StationDoor3Seen,
				StoryManager.Flag.StationEyeTaken, StoryManager.Flag.StationHandTaken, StoryManager.Flag.StationStepTaken,
				StoryManager.Flag.StationEyeSet, StoryManager.Flag.StationHandSet, StoryManager.Flag.StationStepSet,
				StoryManager.Flag.StationDoor3Open }).ToArray(), "lantern,compass,radio,knife,lighter;tool=None"),
			15 => (Checkpoint.Act14Finished, StateFor(14).flags.Append(StoryManager.Flag.Act14JumpedDown).ToArray(), "lantern,compass,radio;tools=Lighter"),
			16 => (Checkpoint.Act15Finished, StateFor(15).flags, "lantern,compass,radio;tools=Lighter"),
			17 => (Checkpoint.Act16Finished, StateFor(15).flags, "lantern,compass,radio;tools=Lighter"),
			18 => (Checkpoint.Act17Finished, StateFor(15).flags, "lantern,compass,radio;tools=Lighter"),
			19 => (Checkpoint.Act18Finished, StateFor(15).flags.Append(StoryManager.Flag.Act18IntroSeen).ToArray(), "lantern,compass,radio;tools=Lighter"),
			20 => (Checkpoint.Act19Finished, StateFor(19).flags, "lantern,compass,radio;tools=Lighter"),
			_ => (Checkpoint.Act10WalkieFound, f11, gear11),
		};
	}

	/// <summary>On the first (trailhead) run with `--story-from`: write that act's save, aim the session at its
	/// step and Continue into the Hollow. Returns true when a level change is on its way.</summary>
	private bool TryStoryFrom()
	{
		int act = StoryFromArg();
		if (_fromApplied || act < 3 || act > 20) return false;
		_fromApplied = true;
		int index = _steps.FindIndex(s => s.Act.StartsWith($"Act {act}:") || (act is 8 or 9 or 10 && s.Act.StartsWith("Acts 8-10")));
		if (index < 0) return false;
		var (cp, flags, inventory) = StateFor(act);
		SaveSystem.Save(new SaveData { Checkpoint = cp, Flags = flags, Inventory = inventory });
		_s.Step = index;
		_s.CurrentAct = _steps[index].Act;
		GD.Print($"[storytest] --story-from={act}: continuing into '{_steps[index].Act}' at {cp} with {flags.Length} flags, gear '{inventory}'");
		if (StoryManager.Instance?.ContinueGame() == true) return true;
		Check("setup", $"--story-from={act} could Continue into its save", false, "ContinueGame returned false");
		Finish(2);
		return true;
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
			if (TryStoryFrom()) return;   // the Hollow's StoryTest resumes at the chosen act
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
				int to = StoryToArg();
				if (to > 0 && ActOf(step) >= to && (_s.Step >= _steps.Count || ActOf(_steps[_s.Step]) > to)) break;   // --story-to: done
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
		// Quit() only schedules the exit; a frame or two can still land after Finish() nulls the session.
		if (_s == null || _player == null || !IsInstanceValid(_player)) return;
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
			new("Act 4: the storm walk, and the key", hollow, Act4Giant),
			new("Act 5: the cabin", hollow, Act5Cabin),
			new("Act 6: the bridge and the clearing", hollow, Act6Clearing),
			new("Act 7: the lookout", hollow, Act7Lookout),
			new("Acts 8-10: the bunker", hollow, Act8To10Bunker),
			new("Act 11: the last climb", hollow, Act11),
			new("Act 12: the lake crossing", hollow, Act12Lake),
			new("Act 13: the forester station", hollow, Act13Station),
			new("Act 14: the stairwell", hollow, Act14Stairwell),
			new("Act 15: the long hallway", hollow, Act15Hall),
			new("Act 16: the closet", hollow, Act16Closet),
			new("Act 17: the sewer", hollow, Act17Sewer),
			new("Act 18: the pit", hollow, Act18Boss),
			new("Act 19: the library", hollow, Act19Library),
			new("Act 20: the round room", hollow, Act20Round),
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
			Check("the camera stays with you on the stairs", !cameraGone && _inv.HasCamera);
			_s.Step++;
			Engine.TimeScale = 1.0;
		}
		StoryManager.Instance.CheckpointReached += Watch;
		try
		{
			// Stepping onto the stairs anywhere counts: the trigger covers every step, not just the first.
			var flightBox = climb.FlightBox;
			var stairs = climb.GetParent() as StaircaseBuilder;
			bool covers = flightBox != null && stairs != null
				&& flightBox.Size.Y >= stairs.TotalHeight && flightBox.Size.Z >= (stairs.Steps - stairs.PlinthSteps) * stairs.Run;
			Check("the stairs trigger covers the whole flight, not just the first step", covers,
				$"box {flightBox?.Size} vs flight {stairs?.TotalHeight:0.0} m tall, {stairs?.Steps * stairs?.Run:0.0} m long");
			await WalkTo(climb.GlobalPosition, 0.6f, ct, stopWhen: () => climb.OnTheStairs);
			await WaitUntil(() => climb.OnTheStairs, 5, ct);
			await Frames(2, ct);
			Check("stepping on the stairs is noticed", climb.OnTheStairs);
			cameraGone = !_inv.HasCamera;
			// The first step lifts them off their feet: movement is taken away, but the mouse can still look.
			Check("movement is taken away for the float", climb.Floating && !_player.IsPhysicsProcessing());
			Check("input stays live so the mouse can still look around", _input.Enabled);
			var top = stairs?.GetNodeOrNull<Node3D>("TopTrigger");
			Engine.TimeScale = 3.0;
			await WaitUntil(() => !climb.Floating, 20, ct);
			Engine.TimeScale = 1.0;
			Check("the float hands movement back once it lands", !climb.Floating && _player.IsPhysicsProcessing());
			bool landed = top != null && Flat(_player.GlobalPosition).DistanceTo(Flat(top.GlobalPosition)) < 1.5f;
			Check("the float carried the player to the top of the flight", landed, $"{_player.GlobalPosition} vs {top?.GlobalPosition}");
			Screenshot("climb");
			// There is no going back down: the one-way wall is up and a walk toward the foot goes nowhere.
			Check("the flight has turned one-way", climb.OneWay is { Armed: true }, $"progress {climb.OneWay?.MaxProgress:0.0} m");
			float heightBefore = _player.GlobalPosition.Y;
			await WalkTo(climb.GlobalPosition, 0.8f, ct, giveUp: 3f);
			Check("no going back down the stairs", _player.GlobalPosition.Y > heightBefore - 1.5f, $"y {heightBefore:0.0} -> {_player.GlobalPosition.Y:0.0}");
			if (top != null) await WalkTo(top.GlobalPosition, 1.2f, ct, giveUp: 20f);
			// Nor backing down while facing up (Dan, 2026-09-22): the wall blocks whatever the player faces or presses.
			if (top != null)
			{
				await Aim(top.GlobalPosition + Vector3.Up * 1.5f, ct);
				Vector3 before = _player.GlobalPosition;
				await PushFor(new Vector2(0, -1), 1.5, ct);
				Check("cannot back down the stairs", Flat(_player.GlobalPosition).DistanceTo(Flat(before)) < 0.5f, $"backed {Flat(_player.GlobalPosition).DistanceTo(Flat(before)):0.00} m");
				await WalkTo(top.GlobalPosition, 1.2f, ct, giveUp: 10f);
			}
			// The broken post at the top: inspecting it is the collapse.
			Check("the broken post can be inspected", climb.InspectPost != null);
			if (climb.InspectPost != null) await UseIt(climb.InspectPost, ct);
			await WaitUntil(() => climb.Collapsing, 4, ct);
			Check("inspecting the post starts the collapse", climb.Collapsing);
			Engine.TimeScale = 3.0;
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
		Check("the camera came along", _inv.HasCamera);
		Check("no compass yet", StoryManager.Instance.ObjectivePosition == null);
		Check("the save says checkpoint 2", SaveSystem.Load()?.Checkpoint == Checkpoint.Act2StairsClimbed);
		Check("the rain is already falling when they wake", StormController.Instance is { Active: true });
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
		// His note says: a bunker, a steel door, four numbers. The corner tracker starts right there, empty.
		await Seconds(0.6, ct);
		Check("the code tracker starts with the note", StoryManager.Instance.HasFlag(Camp.NoteReadFlag) && (CodeLockOverlay.Instance?.TrackerText ?? "").Contains("_ _ _ _"), $"tracker '{CodeLockOverlay.Instance?.TrackerText}'");
		await UseIt(lantern, ct);
		await UseIt(compass, ct);
		Check("lantern and compass in hand", _inv.HasLantern && _inv.HasCompass);
		Check("the compass shows", FirstOf<Compass>() is { ShowingCompass: true });
		CheckObjective("the compass points at the cabin", "cabin");
		Screenshot("camp");
		// Out of the safe zone along the path (walked, not jumped: the key and the giant are further on). The rain
		// has been falling since the wake; past the camp the forest goes dead quiet under it.
		await WalkTrailFor(30f, ct);
		Check("the rain keeps falling past the camp", StormController.Instance is { Active: true });
		await WaitUntil(() => (ForestAmbienceManager.Instance?.Silence ?? 0f) > 0.8f, 8, ct);   // it hushes over a few seconds
		Check("the storm silences the forest", (ForestAmbienceManager.Instance?.Silence ?? 0f) > 0.8f, $"silence {ForestAmbienceManager.Instance?.Silence:0.00}");
	}

	private async Task Act4Giant(CancellationToken ct)
	{
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		if (cabin == null) { Check("the cabin exists", false); return; }
		// No key any more (Dan, 2026-09-22): the shed's hammer is simply there for the taking.
		Check("no key lies on the way", Pickups(ToolKind.Key).FirstOrDefault() == null);
		// The code's first two digits: R.H.'s notes 1 and 2 on trees beside the path on the way (read in the lantern's light).
		var lot = FirstInGroup<SurveyLot>("survey_lot");
		Check("four notes hang on trees along the path", lot is { Notes.Count: 4 }, $"{lot?.Notes.Count}");
		if (lot != null) for (int i = 0; i < 2; i++) await ReadTreeNote(lot, i, ct);
		await WalkAlongTrail(cabin.GlobalPosition, 60f, ct);
		// The storm walk is the stalker's now (Dan, 2026-09-22): the giant is seen from the Act 7 lookout instead.
		// Only its far footfalls reach the walk: a sparse bout of thuds now and then, never a body.
		await Seconds(1.0, ct);
		Check("no giant on the storm walk", !StoryManager.Instance.GiantEventDone && FindUnder<Node3D>(GetTree().CurrentScene, "Act7Giant") == null);
		var giantEvent = AllOf<GiantStalkerEvent>().FirstOrDefault();
		await WaitUntil(() => giantEvent is { FarFootfallBouts: >= 1 }, 12, ct);   // bouts every 8-15 s in the autotest
		Check("far footfalls on the storm walk", giantEvent is { FarFootfallBouts: >= 1 }, $"bouts {giantEvent?.FarFootfallBouts}");
		Check("still no giant after the footfalls", FindUnder<Node3D>(GetTree().CurrentScene, "Act7Giant") == null);
		// The stalker's introduction (Dan, 2026-09-22): it is behind a tree within seconds of the pickups, its steps
		// answer the player's from where it stands, it moves round the player, and it never makes a sound but the rattle.
		if (FirstInGroup<Stalker>("stalker") is { } st)
		{
			Check("it stalks the Hollow path walk (intro pacing)", st.Intro);
			await WaitUntil(() => st.PeekCount >= 1, 16, ct);
			Check("it appears on the storm walk", st.PeekCount >= 1, $"peeks {st.PeekCount}");
			// Walk on toward the cabin so the spells and the relocations have something to follow.
			await WalkAlongTrail(cabin.GlobalPosition, 40f, ct, stopWhen: () => st.ShadowSpells >= 1 && st.DirectionsUsed >= 3 && st.ShadowStepsHeard >= 6);
			Check("its steps follow", st.ShadowSpells >= 1, $"spells {st.ShadowSpells}");
			// ShadowStepsHeard already only counts every second real stride (the parity check in
			// OnPlayerStepped is the "one answer per two strides" rule); every one of those gets an
			// answer queued a beat later, so the two counts should track each other closely, not by half.
			Check("its steps match the player's, a beat behind", st.ShadowStepsHeard == 0 || Mathf.Abs(st.ShadowStepsHeard - st.ShadowStepsAnswered) <= 3,
				$"heard {st.ShadowStepsHeard}, answered {st.ShadowStepsAnswered}");
			Check("it comes from at least three directions", st.DirectionsUsed >= 3, $"{st.DirectionsUsed} sectors, peeks {st.PeekCount}");
			Check("no snarl ever", st.SnarlCount == 0);
			// Less rattle than steps (Dan, 2026-09-22): the spells of steps are most of what is heard on the walk.
			Check("rattle comes in episodes, not all the time", st.RattleAudibleFraction < 0.35f, $"audible {st.RattleAudibleFraction:0.00} of the walk, episodes {st.RattleEpisodes}, bursts {st.RattleBursts}, spells {st.ShadowSpells}");
		}
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
		// Dan (2026-09-22): easier to find: it leans by the path a dozen metres before the cabin, out from Act 3.
		if (axe != null && GroundSnap.FindTerrain(this) is { } axeTerrain)
		{
			float off = axeTerrain.TrailDistance(axe.GlobalPosition.X, axe.GlobalPosition.Z, out float sAxe);
			axeTerrain.TrailDistance(cabin.GlobalPosition.X, cabin.GlobalPosition.Z, out float sCabin);
			Vector3 axeLocal = cabin.ToLocal(axe.GlobalPosition);
			Check("the axe stands beside the cabin, round the far side", Mathf.Abs(axeLocal.X) > cabin.Width * 0.5f && Mathf.Abs(axeLocal.X) < cabin.Width * 0.5f + 5f && Mathf.Abs(axeLocal.Z) < cabin.Depth * 0.5f + 1.5f, $"cabin-local {axeLocal}, {off:0.0} m off the path");
			// Nothing floats (Dan): the axe and the woodpile sit on the terrain behind the cabin.
			float axeGround = axeTerrain.HeightAt(axe.GlobalPosition.X, axe.GlobalPosition.Z);
			var pile = GetTree().CurrentScene.FindChild("Woodpile", true, false) as Node3D;
			float pileGround = pile != null ? axeTerrain.HeightAt(pile.GlobalPosition.X, pile.GlobalPosition.Z) : 0f;
			Check("the axe and the woodpile sit on the ground", Mathf.Abs(axe.GlobalPosition.Y - axeGround) < 0.35f && (pile == null || Mathf.Abs(pile.GlobalPosition.Y - pileGround) < 0.35f),
				$"axe {axe.GlobalPosition.Y - axeGround:+0.00;-0.00} m, woodpile {(pile != null ? pile.GlobalPosition.Y - pileGround : 0f):+0.00;-0.00} m off the terrain");
		}
		if (axe != null)
		{
			// Approach from the side away from the cabin (a step toward the cabin lands inside its walls now that the axe stands beside it).
			await Teleport(axe.GlobalPosition + Flat(axe.GlobalPosition - cabin.GlobalPosition).Normalized() * 3f, axe.GlobalPosition, ct);
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
		// The moment they are in: three quick knocks on the wall outside, and the door slams shut behind
		// them (Dan, 2026-09-22); E on it swings it open again. No lines anywhere in here.
		var reveal = FirstOf<FriendReveal>();
		await WaitUntil(() => reveal is { Knocked: true }, 2, ct);
		Check("three quick knocks as soon as they walk in", reveal is { Knocked: true });
		await WaitUntil(() => StormController.Instance is { RainMuffled: true }, 2, ct);
		Check("the rain stops while they are inside", StormController.Instance is { RainMuffled: true });
		await WaitUntil(() => cabin.SlammedShut && cabin.DoorReopen != null, 3, ct);
		Check("the door slams shut right behind them", cabin.SlammedShut && cabin.DoorReopen != null);
		await Seconds(0.8, ct);
		Check("the rain is silent inside the cabin", StormController.Instance is { } stormIn && stormIn.RainAudibleDb < -40f, $"rain {StormController.Instance?.RainAudibleDb:0.0} dB, indoor {ForestAmbienceManager.Instance?.IsIndoor}");
		if (post != null) await WalkTo(post.GlobalPosition, 1.1f, ct);
		if (post != null) await UseIt(post, ct);
		Check("newel post in hand", _inv.HasNewelPost);
		await Seconds(0.5, ct);
		Check("the storm breaks with the cap in hand", StormController.Instance is not { Active: true } && StoryManager.Instance.HasFlag(StoryManager.Flag.DawnBroke));
		CheckObjective("the compass points at the footbridge", "bridge_marker");
		await WalkTo(cabin.DoorCenter, 1.5f, ct);
		if (cabin.DoorReopen != null) await UseIt(cabin.DoorReopen, ct);
		await Seconds(0.7, ct);
		Check("the door opens again with E", cabin.DoorReopen == null);
		await WalkTo(cabin.DoorCenter, 0.6f, ct);
		await WalkTo(cabin.GlobalTransform * new Vector3(-6f, 0f, 3.5f), 1f, ct);
		await WaitUntil(() => StoryManager.Instance.HasFlag(StoryManager.Flag.DawnBroke), 8, ct);
		Check("the rain stops on stepping out", StoryManager.Instance.HasFlag(StoryManager.Flag.DawnBroke));
		Check("the storm is over", StormController.Instance is not { Active: true });
		// One night in the Hollow (Dan, 2026-09-22): no dawn comes up with the cap.
		Check("it is still night outside the cabin", StoryBeat.Atmosphere(this)?.CurrentMood is ForestAtmosphere.Mood.Night, $"{StoryBeat.Atmosphere(this)?.CurrentMood}");
		Screenshot("rain_stopped");
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
		// "Come up and see" starts as soon as the creek is crossed (Dan, 2026-09-22), long before the ring.
		await WaitUntil(() => act6 is { LoopVoiceCount: >= 1 }, 12, ct);
		Check("the voice starts calling as soon as the creek is crossed", act6 is { VoiceCalling: true, LoopVoiceCount: >= 1 }, $"calling {act6?.VoiceCalling}, {act6?.LoopVoiceCount} calls");
		Check("fifteen small stairs in the clearing", act6?.MiniStairCount == 15, $"{act6?.MiniStairCount}");
		// Walk in like a player (Dan's live bug, 2026-09-22): along the trail into the clearing's mouth, then round the
		// trail flight (brushing past its side must NOT start the loop: only feet on its treads do) to the centre.
		terrain.TrailDistance(clearing.GlobalPosition.X, clearing.GlobalPosition.Z, out float sClear);
		await WalkAlongTrail(terrain.TrailPoint(sClear - 50f, out _), 25f, ct, stopWhen: () => act6 is { LoopStarted: true });   // the trail runs onto the trail flight: stop short of it
		if (act6?.TrailStair is { } trailStair)
		{
			Vector3 aside = trailStair.GlobalBasis.X.Normalized() * 4.5f;
			Vector3 besideFoot = Act6ClearingEvent.FootOf(trailStair) + aside;
			Vector3 besideTop = trailStair.ToGlobal(new Vector3(0, 0, trailStair.BackZ - 2f)) + aside;
			besideFoot.Y = terrain.HeightAt(besideFoot.X, besideFoot.Z); besideTop.Y = terrain.HeightAt(besideTop.X, besideTop.Z);
			await WalkTo(besideFoot, 1.2f, ct, stopWhen: () => act6.LoopStarted, giveUp: 10f);
			await WalkTo(besideTop, 1.2f, ct, stopWhen: () => act6.LoopStarted, giveUp: 14f);
			Check("walking past the trail flight does not start the loop", act6 is { LoopStarted: false }, $"started {act6?.LoopStarted} on {act6?.LoopTarget?.Name}");
		}
		await WalkTo(clearing.GlobalPosition, 6f, ct, stopWhen: () => act6 is { VoiceSpoken: true }, giveUp: 14f);
		if (act6 is { VoiceSpoken: false }) await Teleport(clearing.GlobalPosition + Vector3.Up * 0.2f, clearing.GlobalPosition + Vector3.Forward, ct, count: false);
		await WaitUntil(() => act6 is { VoiceSpoken: true }, 10, ct);
		Check("the clearing's voice", act6 is { VoiceSpoken: true });
		Check("the spotlight finds the player", act6 is { SpotlightActive: true });
		Check("the voice is the saved beat", StoryManager.Instance.ClearingVoiceHeard);
		Check("the cap stays in hand (it belongs to the last staircase)", _inv.HasNewelPost);
		Check("the clearing's staircase stays broken", act6?.OriginalStairs is { NewelCapped: false });
		await Seconds(2.5, ct);
		Screenshot("clearing");

		// The loop (Dan, 2026-09-22): "stuck walking up different staircases before eventually falling off",
		// continuously up. Nothing is lit until the player chooses; stepping onto any whole flight starts it.
		var atmo = StoryBeat.Atmosphere(this);
		await WaitUntil(() => act6 is { LoopArmed: true }, 12, ct);
		Check("after the voice the clearing waits for a choice: nothing lit", act6 is { LoopArmed: true, LoopStarted: false, LoopTarget: null }, $"armed {act6?.LoopArmed}, target {act6?.LoopTarget?.Name}");
		Check("the voice started calling as they entered the clearing", act6 is { VoiceCalling: true, LoopVoiceCount: >= 1 }, $"calling {act6?.VoiceCalling}, {act6?.LoopVoiceCount} calls so far");
		CheckObjective("the compass rests on the clearing until a flight is chosen", "clearing_loop_marker");
		// Only the whole flights carry the loop: never a broken fragment, never the original in the middle.
		Check("only the whole staircases carry the loop", act6 is { LoopStairs.Count: 10 } && act6.LoopStairs.All(m => !m.Ruined && m != act6.OriginalStairs),
			$"{act6?.LoopStairs.Count} loop stairs, ruined {act6?.LoopStairs.Count(m => m.Ruined)}");
		// Every whole flight starts the loop: two treads up any of them counts as standing on it (Dan found one that did not).
		Check("every whole flight would start the loop", act6 != null && act6.LoopStairs.All(act6.WouldStartOn),
			act6 == null ? "" : string.Join(", ", act6.LoopStairs.Where(m => !act6.WouldStartOn(m)).Select(m => m.Name)) is { Length: > 0 } miss ? "would not: " + miss : "all ten would");
		if (act6 != null && act6.LoopStairs.Count > 0)
		{
			var first = act6.LoopStairs[0];
			Check("the whole staircases are all much the same size", act6.LoopStairs.All(m => Mathf.Abs(m.Steps - first.Steps) <= 3 && Mathf.Abs(m.Scale.X - first.Scale.X) < 0.01f && Mathf.Abs(m.Width - first.Width) < 0.01f),
				$"steps {act6.LoopStairs.Min(m => m.Steps)}-{act6.LoopStairs.Max(m => m.Steps)}, scale {first.Scale.X:0.00}, width {first.Width:0.00}");
			var flights = GetTree().GetNodesInGroup("act6_mini_stairs").OfType<StaircaseBuilder>().ToList();
			if (act6.OriginalStairs != null) flights.Add(act6.OriginalStairs);
			var overlaps = new List<string>();
			for (int i = 0; i < flights.Count; i++)
				for (int j = i + 1; j < flights.Count; j++)
					if (Act6ClearingEvent.Footprint.Overlap(Act6ClearingEvent.FootprintOf(flights[i]), Act6ClearingEvent.FootprintOf(flights[j])))
						overlaps.Add($"{flights[i].Name}/{flights[j].Name}");
			Check("no two clearing staircases overlap", overlaps.Count == 0, overlaps.Count == 0 ? $"{flights.Count} flights" : string.Join(", ", overlaps));
			Check("all whole flights are the same length", act6.LoopStairs.All(m => m.Steps == first.Steps), $"{act6.LoopStairs.Min(m => m.Steps)}-{act6.LoopStairs.Max(m => m.Steps)} steps");
			// Nothing sinks: the ground under the foot, the middle, the top step and the back of the landing is below the treads.
			var sunk = new List<string>();
			foreach (var m in GetTree().GetNodesInGroup("act6_mini_stairs").OfType<StaircaseBuilder>())
			{
				foreach (var lp in new[] { new Vector3(0, 0.02f, 0.3f), new Vector3(0, m.TotalHeight * 0.5f, m.TopFrontZ * 0.5f), new Vector3(0, m.TotalHeight, m.TopFrontZ), new Vector3(0, m.TotalHeight, m.BackZ) })
				{
					Vector3 w = m.ToGlobal(lp);
					float ground = terrain.HeightAt(w.X, w.Z);
					if (ground > w.Y + 0.05f) { sunk.Add($"{m.Name} ({ground - w.Y:0.00} m under at z {lp.Z:0.0})"); break; }
				}
			}
			Check("no staircase sinks into the ground", sunk.Count == 0, sunk.Count == 0 ? "" : string.Join(", ", sunk));
			Check("one whole staircase stands in the trail's mouth", act6.TrailStair is { Ruined: false } && terrain.TrailDistance(act6.TrailStair.GlobalPosition.X, act6.TrailStair.GlobalPosition.Z, out _) < 1.5f,
				$"{act6.TrailStair?.Name}, {(act6.TrailStair == null ? -1f : terrain.TrailDistance(act6.TrailStair.GlobalPosition.X, act6.TrailStair.GlobalPosition.Z, out _)):0.0} m off the trail");
		}
		Check("the clearing is thick with fog", Mathf.Abs(atmo?.HeightFogDensity ?? 0f) > 0.04f, $"height fog {atmo?.HeightFogDensity:0.000}");
		if (act6 != null && FirstOf<ForestScatter>() is { } scatter)
		{
			// Genuinely clear (Dan, 2026-09-22): no scatter trees inside the ring, through the flights.
			int trees = scatter.CountInstancesWithin(new Vector2(act6.GlobalPosition.X, act6.GlobalPosition.Z), act6.FenceRadius, key => key.StartsWith("fir") || key.StartsWith("decid") || key.StartsWith("snag"));
			Check("no trees inside the clearing", trees == 0, $"{trees} trunks within {act6.FenceRadius:0} m");
		}
		Check("the clearing is closed", act6 is { Fenced: true });
		if (act6 != null)
		{
			// One way in: the ring stops a ray everywhere but at the trail's mouth (the ring is centred on the
			// clearing's flight, at ground level; the marker node sits at y 0).
			Vector3 c = act6.GlobalPosition + Vector3.Up * 1.5f;
			Vector3 dirIn = Flat(act6.EntranceWorld - act6.GlobalPosition).Normalized();
			Vector3 side = new(-dirIn.Z, 0, dirIn.X);
			float r = act6.FenceRadius;
			bool inOpen = !await RayHits(c + dirIn * (r - 3f), c + dirIn * (r + 3f), ct);
			bool outBlocked = await RayHits(c - dirIn * (r - 3f), c - dirIn * (r + 3f), ct);
			bool sideBlocked = await RayHits(c + side * (r - 3f), c + side * (r + 3f), ct);
			Check("the clearing is closed but for the way in", inOpen && outBlocked && sideBlocked, $"entrance open {inOpen}, onward blocked {outBlocked}, side blocked {sideBlocked}");
		}

		// EVERY staircase in the clearing starts the loop (Dan, 2026-09-22, "the millionth time"): the ten whole flights,
		// the five ruined stubs and the original. Each is tried in turn from its third tread, walking two steps up.
		if (act6 != null)
		{
			var table = new System.Text.StringBuilder();
			var all = act6.AllFlights.ToList();
			int okCount = 0;
			foreach (var m in all)
			{
				act6.ResetLoopForTest();
				bool whole = !m.Ruined && m != act6.OriginalStairs;
				Vector3 third = m.ToGlobal(new Vector3(0, 3f * m.Rise + 0.05f, -3f * m.Run));
				Vector3 up = m.ToGlobal(new Vector3(0, 6f * m.Rise, -6f * m.Run));
				await Teleport(third + Vector3.Up * 0.15f, up, ct, count: false);
				await WalkFor(up, 0.9, ct);
				await WaitUntil(() => act6.LoopStarted, 1.5, ct);
				bool ok = act6.LoopStarted && (!whole || act6.LoopTarget == m);
				if (ok) okCount++;
				table.Append($"\n  {m.Name,-11} {(m.Ruined ? "ruined  " : m == act6.OriginalStairs ? "original" : "whole   ")} -> {(act6.LoopStarted ? "starts (" + act6.LoopTarget?.Name + ")" : "DOES NOT START")}");
				await WaitUntil(() => !act6.LoopBusy, 4, ct);
			}
			GD.Print("[storytest] loop start per flight:" + table);
			Check("every staircase in the clearing starts the loop", okCount == all.Count && all.Count == 16, $"{okCount}/{all.Count}" + table.ToString().Replace("\n", " |"));
			// Three of them on foot from the ground, not set down on the treads.
			int walked = 0;
			var walkedOn = new List<string>();
			foreach (var m in act6.LoopStairs.Take(3))
			{
				act6.ResetLoopForTest();
				// From the ground just in front of the foot (the flights stand close: 5 m out can be another flight's treads).
				Vector3 approach = m.ToGlobal(new Vector3(0, 0.05f, 2.6f));
				await Teleport(approach, Act6ClearingEvent.FootOf(m), ct, count: false);
				await WalkTo(m.ToGlobal(new Vector3(0, 0.8f, -2.4f)), 0.6f, ct, stopWhen: () => act6.LoopStarted, giveUp: 8f);
				await WaitUntil(() => act6.LoopStarted, 1.5, ct);
				if (act6.LoopStarted) walked++;
				walkedOn.Add($"{m.Name}->{(act6.LoopStarted ? act6.LoopTarget?.Name : "none")}");
				await WaitUntil(() => !act6.LoopBusy, 4, ct);
			}
			Check("walking up three flights from the ground starts the loop on each", walked == 3, string.Join(", ", walkedOn));
			act6.ResetLoopForTest();
			await Teleport(clearing.GlobalPosition + Vector3.Up * 0.2f, clearing.GlobalPosition + Vector3.Forward, ct, count: false);
		}

		// Choose: the nearest whole flight (the trail one, since it stands in the way in).
		var chosen = act6?.LoopStairs.OrderBy(m => Flat(m.GlobalPosition).DistanceTo(Flat(_player.GlobalPosition))).FirstOrDefault();
		if (chosen != null)
		{
			Vector3 foot = Act6ClearingEvent.FootOf(chosen), approach = chosen.ToGlobal(new Vector3(0, 0.05f, 5f));
			if (!await WalkTo(approach, 1.5f, ct, giveUp: 10f)) await Teleport(approach, foot, ct);
			await WalkTo(foot, 1.0f, ct, giveUp: 8f);
			await WalkTo(chosen.ToGlobal(new Vector3(0, 0.6f, -1.6f)), 0.6f, ct, stopWhen: () => act6.LoopStarted, giveUp: 8f);
			await WaitUntil(() => act6.LoopStarted, 2, ct);
		}
		Check("stepping onto a staircase starts the loop", act6 is { LoopStarted: true } && act6.LoopTarget == chosen, $"target {act6?.LoopTarget?.Name}, chosen {chosen?.Name}");
		CheckObjective("the light and the compass follow the chosen flight", "clearing_loop_marker");
		Check("blood begins to rain as the loop starts", act6 is { BloodRaining: true });
		Check("four to six flights are planned, all up", act6 is { LoopLegs: >= 4 and <= 6 }, $"{act6?.LoopLegs} flights");
		int legs = act6?.LoopLegs ?? 0;
		float bloodBefore = act6?.BloodIntensity ?? 0f;
		bool bloodRose = false, landedMidFlight = false;
		var fractions = new List<float>();
		for (int leg = 0; leg < legs && act6 is { LoopStarted: true, LoopDone: false } && act6.LoopTarget != null; leg++)
		{
			// Never start a leg while a hand-off or the fall is playing (the fall ends the loop; a leg begun
			// on top of it would drag the player back onto the stairs).
			await WaitUntil(() => !act6.LoopBusy || act6.LoopDone, 8, ct);
			if (act6.LoopDone || act6.LoopBusy) break;
			var stair = act6.LoopTarget;
			int stageBefore = act6.LoopStage;
			Vector3 goal = act6.LoopTargetPoint;
			if (leg < 2 && act6.LoopStage == stageBefore)
			{
				// A few treads up, there is no backing down, even facing up and pressing back (Dan, 2026-09-22).
				await WalkFor(goal, 0.7, ct);
				if (act6.LoopStage == stageBefore)
				{
					Vector3 beforePush = _player.GlobalPosition;
					await PushFor(new Vector2(0, -1), 1.5, ct);
					if (act6.LoopStage == stageBefore)
						Check($"cannot back down on flight {leg + 1}", act6.OneWay is { Armed: true } && Flat(_player.GlobalPosition).DistanceTo(Flat(beforePush)) < 0.5f,
							$"wall armed {act6.OneWay?.Armed}, backed {Flat(_player.GlobalPosition).DistanceTo(Flat(beforePush)):0.00} m");
				}
			}
			await WalkTo(goal, 0.9f, ct, stopWhen: () => act6.LoopStage > stageBefore, giveUp: 16f);
			await WaitUntil(() => act6.LoopStage > stageBefore, 2, ct);
			bool reached = act6.LoopStage > stageBefore;
			Check($"flight {leg + 1} ({stair.Name}): the top is reached on foot", reached, $"{_player.GlobalPosition} vs {goal}");
			if (!reached)
			{
				// Never sit stuck: put them on the top landing and move on (the failure is already recorded).
				Screenshot($"flight{leg + 1}_stuck");
				await Teleport(goal + Vector3.Up * 0.2f, goal + (goal - Act6ClearingEvent.FootOf(stair)), ct, count: false);
				await WaitUntil(() => act6.LoopStage > stageBefore, 2, ct);
				if (act6.LoopStage == stageBefore) break;
			}
			if (act6.LoopDone || act6.LoopStage >= legs) break;   // the last top ends in the fall, not a hand-off
			await WaitUntil(() => !act6.LoopBusy && act6.LoopTarget != stair, 4, ct);
			var next = act6.LoopTarget;
			float landed = act6.LoopLandFraction;
			fractions.Add(landed);
			if (landed is >= 0.3f and <= 0.75f) landedMidFlight = true;
			// Where the hand-off should have put them: the flight's foot, or that far up its treads.
			Vector3 nextStart = Vector3.Zero;
			if (next != null)
			{
				if (landed <= 0.01f) nextStart = Act6ClearingEvent.FootOf(next);
				else { float treads = next.Steps * landed; nextStart = next.ToGlobal(new Vector3(0, treads * next.Rise, -treads * next.Run)); }
			}
			Check($"flight {leg + 1}: the fog put them on another flight ({landed:0.00} up it), still walking, no cut",
				next != null && next != stair && Flat(_player.GlobalPosition).DistanceTo(Flat(nextStart)) < 2.5f && _input.Enabled && (StoryBeat.Fader(this)?.BlackAlpha ?? 0f) < 0.05f,
				$"{next?.Name} at {_player.GlobalPosition} vs {nextStart}, input {_input.Enabled}, black {StoryBeat.Fader(this)?.BlackAlpha:0.00}");
			if (leg == 0) Screenshot("loop_handoff");
			if (act6.BloodIntensity > bloodBefore + 0.05f) bloodRose = true;
			bloodBefore = act6.BloodIntensity;
			CheckObjective("the compass follows the flight", "clearing_loop_marker");
		}
		Engine.TimeScale = 3.0;
		await WaitUntil(() => act6 is { LoopDone: true } && _input.Enabled, 20, ct);
		Engine.TimeScale = 1.0;
		Check("the last landing ends in the fall", act6 is { LoopDone: true });
		Check("the fall is the saved beat", StoryManager.Instance.HasFlag(StoryManager.Flag.ClearingLoopDone));
		Check("the fall went off the edge, clear of the flight", act6 is { FallClearOfFlight: true });
		Check("the voice kept calling during the loop", act6 is { LoopVoiceCount: >= 1 }, $"{act6?.LoopVoiceCount} calls");
		Check("blood rained harder with every flight", bloodRose);
		Check("every hand-off landed part way along a flight, never near its foot", landedMidFlight && fractions.All(f => f is >= 0.3f and <= 0.75f), $"landed at {string.Join(", ", fractions.Select(f => f.ToString("0.00")))}");
		Check("the blood stops with the fall", act6 is { BloodRaining: false });
		// Act 7 (Dan, 2026-09-22): the stairs were never there. The stand, the ring and the clearing's flight are gone.
		Check("the stairs and the ring are gone at the wake", act6 is { WorldReset: true, Fenced: false, MiniStairCount: 0 } && act6.OriginalStairs is { Visible: false },
			$"reset {act6?.WorldReset}, fenced {act6?.Fenced}, minis {act6?.MiniStairCount}, flight visible {act6?.OriginalStairs?.Visible}");
		Check("it is still the same night when they wake", StoryBeat.Atmosphere(this)?.CurrentMood == ForestAtmosphere.Mood.Night, $"{StoryBeat.Atmosphere(this)?.CurrentMood}");
		terrain.TrailDistance(_player.GlobalPosition.X, _player.GlobalPosition.Z, out float sWake);
		terrain.TrailDistance(clearing.GlobalPosition.X, clearing.GlobalPosition.Z, out float sClearing);
		Check("woke on the trail past the clearing, outside the ring", sWake > sClearing + 10f && (act6 == null || Flat(_player.GlobalPosition).DistanceTo(Flat(act6.GlobalPosition)) > act6.FenceRadius),
			$"trail {sWake:0} m vs clearing {sClearing:0} m, {(act6 == null ? 0f : Flat(_player.GlobalPosition).DistanceTo(Flat(act6.GlobalPosition))):0} m from the centre");
		Check("woke in thick fog", Mathf.Abs(atmo?.HeightFogDensity ?? 0f) > 0.08f, $"height fog {atmo?.HeightFogDensity:0.000}");
		Check("control comes back on waking", _input.Enabled);
		Check("nothing is said on waking", (StoryBeat.Fader(this)?.CaptionAlpha ?? 0f) < 0.05f, $"caption alpha {StoryBeat.Fader(this)?.CaptionAlpha:0.00}");
		Screenshot("clearing_wake");
		CheckObjective("the compass points on to the lookout", "fire_lookout_marker");
	}

	/// <summary>Holds a raw move (e.g. (0,-1): backwards) for a fixed time without steering.</summary>
	private async Task PushFor(Vector2 move, double seconds, CancellationToken ct)
	{
		double t = 0;
		try
		{
			while (t < seconds)
			{
				ct.ThrowIfCancellationRequested();
				if (_input.Enabled) _input.ScriptedMove = move;
				await Frames(1, ct);
				t += GetProcessDeltaTime();
			}
		}
		finally { if (_input != null) _input.ScriptedMove = Vector2.Zero; }
	}

	/// <summary>Walks toward a point for a fixed time (a few treads), whatever happens.</summary>
	private async Task WalkFor(Vector3 target, double seconds, CancellationToken ct)
	{
		double t = 0;
		try
		{
			while (t < seconds)
			{
				ct.ThrowIfCancellationRequested();
				if (_input.Enabled) { Steer(target); _input.ScriptedMove = new Vector2(0, 1); }
				await Frames(1, ct);
				t += GetProcessDeltaTime();
			}
		}
		finally { if (_input != null) _input.ScriptedMove = Vector2.Zero; }
	}

	/// <summary>Whether a ray between two world points hits anything on the world layer (walls, ground, stairs).</summary>
	private async Task<bool> RayHits(Vector3 from, Vector3 to, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		var space = _player?.GetWorld3D()?.DirectSpaceState;
		if (space == null) return false;
		return space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1u)).Count > 0;
	}

	private async Task Act7Lookout(CancellationToken ct)
	{
		var lookout = GetTree().GetFirstNodeInGroup("fire_lookout_marker") as Node3D;
		if (lookout == null) { Check("the lookout exists", false); return; }
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var fire = AllOf<CabinFireEvent>().FirstOrDefault();
		var act6Reset = AllOf<Act6ClearingEvent>().FirstOrDefault();
		var terrainWake = GroundSnap.FindTerrain(this);
		// The world at the wake (Dan, 2026-09-22): the stairs and the ring are gone; the meadow and the trails stay,
		// and the compass leads on (no forest over the way back, no light in the distance).
		if (act6Reset != null && terrainWake != null)
		{
			Check("the stairs are gone at the wake", act6Reset is { WorldReset: true, MiniStairCount: 0 } && act6Reset.OriginalStairs is { Visible: false }, $"reset {act6Reset.WorldReset}, minis {act6Reset.MiniStairCount}");
			terrainWake.TrailDistance(_player.GlobalPosition.X, _player.GlobalPosition.Z, out float sNow);
			Vector3 fwd = terrainWake.TrailPoint(sNow + 2f, out _);
			await Teleport(fwd + Vector3.Up * 0.2f, terrainWake.TrailPoint(sNow + 8f, out _), ct, count: false);
		}
		Check("the cabin already burns at the wake", cabin is { Burning: > 0.5f } && fire is { Struck: false }, $"burning {cabin?.Burning:0.00}, struck {fire?.Struck}");
		// The compass ribbon itself (Dan, 2026-09-22: "the compass seems broken"): the objective is the lookout; look 35
		// degrees to the RIGHT of it and the marker must sit LEFT of centre (yaw grows turning left in Godot), and the
		// shown bearing must settle on the true one.
		if (AllOf<Compass>().FirstOrDefault() is { } compassUi && StoryManager.Instance.ObjectivePosition is { } objAtWake)
		{
			CheckObjective("the compass points at the lookout after the wake", "fire_lookout_marker");
			Vector3 toObj = objAtWake - _player.GlobalPosition; toObj.Y = 0;
			Vector3 rightOfIt = toObj.Normalized().Rotated(Vector3.Up, Mathf.DegToRad(-35f));   // clockwise from above = to the right
			await Aim(_player.CameraRig.Camera.GlobalPosition + rightOfIt * 20f, ct);
			await Seconds(1.5, ct);
			float trueDeg = Compass.BearingDeg(_player.GlobalPosition, objAtWake);
			float shown = compassUi.ShownBearing ?? -1f;
			float px = compassUi.MarkerOffsetPx ?? 0f;
			Check("the ribbon's marker sits on the side the objective really is", px < -8f, $"marker {px:0} px from centre (negative = left), objective 35 deg to the left of view");
			Check("the ribbon's bearing matches the true bearing", Mathf.Abs(Mathf.Wrap(shown - trueDeg, -180f, 180f)) < 3f, $"shown {shown:0.0} vs true {trueDeg:0.0}");
		}
		await WalkAlongTrail(lookout.GlobalPosition, 30f, ct);
		await WalkTo(lookout.GlobalPosition, 2.5f, ct, stopWhen: () => StoryManager.Instance.Current >= Checkpoint.Act7CabinBurning);
		if (cabin != null) await Aim(cabin.GlobalPosition + Vector3.Up * 2f, ct);
		// Lightning takes the cabin as it comes into view (Dan, 2026-09-22): a flash, the bolt, the thunder, and it catches.
		await WaitUntil(() => fire is { Struck: true }, 12, ct);
		Check("seeing the burning cabin is the beat", fire is { Struck: true, Sighted: true }, $"struck {fire?.Struck}, sighted {fire?.Sighted}");
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act7CabinBurning, 4, ct);
		Check("checkpoint 6 on the sight", StoryManager.Instance.Current >= Checkpoint.Act7CabinBurning);
		await WaitUntil(() => cabin is { Burning: > 0.5f }, 6, ct);
		Check("it keeps burning", cabin is { Burning: > 0.5f }, $"{cabin?.Burning:0.00}");
		Check("the player keeps control through the strike", _input.Enabled);
		await Seconds(1.5, ct);
		Screenshot("cabin_burning");
		// The giant: checkpoint 6 arms it; standing at the lookout looking at the fire starts the crossing far beyond
		// the cabin, behind the far trees (neck and head over them).
		var giantEvent = AllOf<GiantStalkerEvent>().FirstOrDefault();
		Check("the giant is already out there once the fire is seen", giantEvent is { Armed: true } or { Done: true } or { Released: true }, $"armed {giantEvent?.Armed}, done {giantEvent?.Done}, released {giantEvent?.Released}");
		await WalkTo(lookout.GlobalPosition, 2.5f, ct);
		if (cabin != null) await Aim(cabin.GlobalPosition + Vector3.Up * 2f, ct);
		await WaitUntil(() => giantEvent?.Body != null, 8, ct);
		var giant = giantEvent?.Body;
		Check("the giant crosses beyond the burning cabin", giant != null);
		// Shots every 2 s from the lookout, looking at its head: the proof that it can be seen from here.
		for (int shot = 0; shot < 5 && giant != null && IsInstanceValid(giant); shot++)
		{
			Vector3 head = (giant as StalkerBody)?.EyesWorld ?? giant.GlobalPosition + Vector3.Up * 50f;
			await Aim(head, ct);
			await Seconds(0.2, ct);
			Screenshot($"giant_t{shot * 2:00}");
			await Seconds(1.8, ct);
		}
		if (giant != null && cabin != null)
		{
			float toCabin = Flat(lookout.GlobalPosition).DistanceTo(Flat(cabin.GlobalPosition));
			float toGiant = Flat(lookout.GlobalPosition).DistanceTo(Flat(giant.GlobalPosition));
			Check("it walks on the far side of the cabin", toGiant > toCabin + 40f, $"giant {toGiant:0} m, cabin {toCabin:0} m from the lookout");
			Check("only its neck and head clear the trees", giant is StalkerBody { Size: >= 18f and <= 34f }, $"size {(giant as StalkerBody)?.Size}");
			// Walking, not floating: its origin (the feet) stays on the terrain, a little sunk, and it keeps a gait.
			var terrain = GroundSnap.FindTerrain(this);
			float groundUnder = terrain?.HeightAt(giant.GlobalPosition.X, giant.GlobalPosition.Z) ?? giant.GlobalPosition.Y;
			float feet = giant.GlobalPosition.Y - groundUnder;
			float size = (giant as StalkerBody)?.Size ?? 22f;
			Check("its feet are on the ground", feet > -0.1f * size && feet < 0.06f * size, $"feet {feet:0.0} m off the terrain (body {size:0})");
			Check("it strides (the gait is running)", giant is StalkerBody { WalkPhase: >= 0f }, $"phase {(giant as StalkerBody)?.WalkPhase:0.00}");
			Check("it is a shadow in the murk", (giant as StalkerBody)?.Skin?.GetShaderParameter("haze").AsSingle() >= 0.7f, $"haze {(giant as StalkerBody)?.Skin?.GetShaderParameter("haze")}");
			// Shoot it mid-sweep, when it is in the sight lane behind the fire (not at the far end, behind the lookout's own trees).
			Vector3 lane = Flat(cabin.GlobalPosition) - Flat(lookout.GlobalPosition);
			Vector3 across = new Vector3(-lane.Z, 0, lane.X).Normalized();
			await WaitUntil(() =>
			{
				if (giant == null || !IsInstanceValid(giant)) return true;
				float off = (Flat(giant.GlobalPosition) - Flat(cabin.GlobalPosition)).Dot(across);
				return Mathf.Abs(off) < 12f;
			}, 10, ct);
			if (giant != null && IsInstanceValid(giant))
			{
				await Aim((giant as StalkerBody)?.EyesWorld ?? giant.GlobalPosition + Vector3.Up * 50f, ct);   // its head
				await Seconds(0.3, ct);
				Screenshot("giant");
				Check("it crosses the sight lane behind the fire", Mathf.Abs((Flat(giant.GlobalPosition) - Flat(cabin.GlobalPosition)).Dot(across)) < 30f, $"{Mathf.Abs((Flat(giant.GlobalPosition) - Flat(cabin.GlobalPosition)).Dot(across)):0} m off the lane");
			}
		}
		Check("the player keeps control while it passes", _input.Enabled);
		await WaitUntil(() => StoryManager.Instance.GiantEventDone, 70, ct);   // it finishes the pass under way (up to 50 s) and dissolves (5 s)
		Check("the giant is gone and the sighting is saved", StoryManager.Instance.GiantEventDone && giantEvent?.Body == null);
		CheckObjective("the compass points at the bunker", "bunker_marker");
		// Nobody goes into a burning house: the doorway is shut and solid (a ray through it hits the door).
		if (cabin != null)
		{
			Vector3 fwd = cabin.GlobalBasis.Z.Normalized();
			bool doorSolid = await RayHits(cabin.DoorCenter + fwd * 1.2f, cabin.DoorCenter - fwd * 1.2f, ct);
			Check("the burning cabin cannot be entered", cabin.Sealed && doorSolid, $"sealed {cabin.Sealed}, door solid {doorSolid}");
		}
		// The way on: the trail runs from the lookout along the creek bank past the fire and on to the bunker,
		// never back toward the clearing (Dan, 2026-09-22).
		var terrainRoute = GroundSnap.FindTerrain(this);
		var bunkerMarker = GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		var clearingMarker = GetTree().GetFirstNodeInGroup("stairs_clearing_marker") as Node3D;
		if (terrainRoute != null && bunkerMarker != null && clearingMarker != null && cabin != null)
		{
			terrainRoute.TrailDistance(lookout.GlobalPosition.X, lookout.GlobalPosition.Z, out float sLook);
			terrainRoute.TrailDistance(bunkerMarker.GlobalPosition.X, bunkerMarker.GlobalPosition.Z, out float sBunk);
			float nearestClearing = float.MaxValue, nearestCabin = float.MaxValue;
			for (float s = sLook; s <= sBunk; s += 4f)
			{
				var p = terrainRoute.TrailPoint(s, out _);
				nearestClearing = Mathf.Min(nearestClearing, Flat(p).DistanceTo(Flat(clearingMarker.GlobalPosition)));
				nearestCabin = Mathf.Min(nearestCabin, Flat(p).DistanceTo(Flat(cabin.GlobalPosition)));
			}
			Check("the trail leads on to the bunker without returning to the clearing", sBunk > sLook + 20f && nearestClearing > 60f, $"lookout s {sLook:0}, bunker s {sBunk:0}, nearest the clearing {nearestClearing:0} m");
			Check("the way on passes by the burning cabin", nearestCabin < 75f, $"nearest the cabin {nearestCabin:0} m");
		}
		// The code's last two digits: notes 3 and 4 on trees beside the path on the way to the bunker.
		var lot = FirstInGroup<SurveyLot>("survey_lot");
		if (lot is { Notes.Count: 4 }) for (int i = 2; i < 4; i++) await ReadTreeNote(lot, i, ct);
		Check("all four digits are known before the bunker", lot != null && lot.ReadCount == 4 && lot.Known == lot.Code, $"known {lot?.Known}");
	}

	private async Task Act8To10Bunker(CancellationToken ct)
	{
		if (GetTree().GetFirstNodeInGroup("bunker_marker") is not Bunker bunker) { Check("the bunker exists", false); return; }
		bool Entered() => StoryManager.Instance.Current >= Checkpoint.Act8BunkerEntered;
		await WalkAlongTrail(bunker.ApproachPointWorld, 40f, ct, Entered);
		if (!Entered()) await WalkTo(bunker.ApproachPointWorld, 0.8f, ct, stopWhen: Entered);
		await Seconds(0.5, ct);
		Check("the hatch stays locked until the code is dialled", !bunker.IsOpen);

		// The code was gathered on the way (the four notes on the trees): all four digits are known before the dial.
		var lot = FirstInGroup<SurveyLot>("survey_lot");
		string read = lot?.Known ?? "";
		Check("the four notes' digits were read on the way", lot != null && lot.ReadCount == 4 && read == lot.Code, $"known {read}, code {lot?.Code}");
		Check("the tracker shows the whole code", (CodeLockOverlay.Instance?.TrackerText ?? "").Replace(" ", "").EndsWith(lot?.Code ?? "?"), $"'{CodeLockOverlay.Instance?.TrackerText}'");

		// The dial: a wrong code keeps it shut, the notes' code opens it.
		await Teleport(bunker.ApproachPointWorld, bunker.HatchWorld, ct);
		await WalkTo(bunker.ToGlobal(new Vector3(0, 0.3f, 2.0f)), 0.6f, ct);
		await Aim(bunker.HatchWorld, ct);
		GD.Print($"[storytest] dial: focused '{_player.Interaction?.Focused?.GetParent()?.Name}' prompt '{_player.Interaction?.PromptText}'");
		await Press(ct);
		await WaitUntil(() => CodeLockOverlay.Instance is { IsOpen: true }, 3, ct);
		Check("E on the dial holds it up", CodeLockOverlay.Instance is { IsOpen: true });
		Screenshot("dial");
		if (CodeLockOverlay.Instance is { IsOpen: true } dial)
		{
			Check("the dial comes up pre-filled with the digits found", dial.Entered == lot?.Code, $"wheels {dial.Entered}");
			// Through the player's own keys (the overlay's input path), not the test shortcut: turn the wheel under the
			// cursor one notch (a wrong code), try it, turn it back, try again.
			string before = dial.Entered;
			await Key("move_forward", ct);
			Check("W turns the wheel under the cursor", dial.Entered != before, $"{before} -> {dial.Entered}");
			await Key("interact", ct);
			await Seconds(0.4, ct);
			Check("a wrong code keeps the hatch shut", !bunker.IsOpen && dial.IsOpen, $"wheels {dial.Entered}");
			await Key("move_back", ct);
			Check("S turns it back to the notes' code", dial.Entered == lot?.Code, $"wheels {dial.Entered}");
			await Key("interact", ct);
		}
		await WaitUntil(() => bunker.IsOpen, 5, ct);
		Check("the notes' code opens the bunker door", bunker.IsOpen);
		Check("the code is saved (the door stays open on Continue)", StoryManager.Instance.HasFlag(StoryManager.Flag.BunkerUnlocked));
		Check("the dial is put down and control is back", CodeLockOverlay.Instance is not { IsOpen: true } && !_input.Modal);
		Screenshot("bunker_open");
		if (!Entered()) await WalkTo(bunker.EntryPointWorld, 0.3f, ct, stopWhen: Entered);
		await WaitUntil(Entered, 12, ct);
		Check("checkpoint 7 inside the bunker", Entered());
		var bi = BunkerInterior.Instance;
		if (bi == null) { Check("bunker interior", false); return; }
		await WaitUntil(() => _player.GlobalPosition.DistanceTo(bi.GlobalPosition) < 500f && _input.Enabled, 8, ct);
		Check("carried into the interior", _player.GlobalPosition.DistanceTo(bi.GlobalPosition) < 500f);
		Check("indoors (forest audio off)", ForestAmbienceManager.Instance?.IsIndoor ?? false);
		if (FirstInGroup<Stalker>("stalker") is { } st)
		{
			Check("the forest stalker stays outside", st.Current == Stalker.State.Dormant, $"{st.Current}");
			// Dan, 2026-09-22: only the bunker's own scares in here; the free-roaming stalker is silent throughout.
			int answersBefore = st.ShadowStepsAnswered, burstsBefore = st.RattleBursts;
			await Seconds(1.5, ct);
			Check("the stalker is silent inside the bunker", Stalker.Inert && st.RattleLevel == 0f && st.ShadowStepsAnswered == answersBefore && st.RattleBursts == burstsBefore,
				$"inert {Stalker.Inert}, rattle {st.RattleLevel:0.00}, answers {st.ShadowStepsAnswered - answersBefore}, bursts {st.RattleBursts - burstsBefore}");
		}

		// Act 8: the hallway.
		bool reached = await WalkTo(bi.VineDoorApproachWorld + new Vector3(0, 0, -4f), 0.6f, ct);
		Check("walked the hallway", reached);
		Check("the lights have gone red", bi.RedTriggered);
		Check("the stalker stepped out in the flickering hallway", bi.JumpscareFired);
		Check("the lamps died for the scare, and came back", bi.Hallway is { BlackoutCount: >= 1, BlackedOut: false }, $"{bi.Hallway?.BlackoutCount}");
		Screenshot("hallway");
		await Aim(bi.VineDoorInteractWorld, ct);
		await Press(ct);
		Check("the vine door opens", bi.VineDoorOpenState);
		await WalkTo(bi.CrtRoomInteriorWorld, 1.2f, ct);
		await Seconds(1.2, ct);
		Check("the vine door swings shut behind them", bi.VineDoor is { ShutBehind: true });
		Check("the door is locked before the screens and the walkie", bi.Flow is { VineDoorUnlocked: false });

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
		// The walkie-talkie lies dead on the console: the compass leads to it first, and the way out waits for it.
		CheckObjective("the compass points at the dead walkie-talkie on the console", "walkie_marker");
		var walkie = AllOf<WalkiePickup>().FirstOrDefault();
		Check("the walkie-talkie lies dead on the console", walkie is { Dead: true, Hissing: false } && walkie.GetParent()?.Name == "CrtRoom");
		if (walkie != null)
		{
			await WalkTo(walkie.GlobalPosition + new Vector3(0, 0, 1.0f), 1.0f, ct);
			await UseIt(walkie, ct);
		}
		await Seconds(0.3, ct);
		Check("the radio is in hand, and silent", _inv.HasRadio && StoryManager.Instance.HasFlag(StoryManager.Flag.WalkieTaken) && StoryManager.Instance.Current < Checkpoint.Act10WalkieFound);
		CheckObjective("the compass points at the way out", "bunker_entrance_marker");
		Check("the door unlocks only once the screens are done and the walkie is in hand", bi.Flow is { VineDoorUnlocked: true });

		// Act 10: the rooms.
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
		Check("the hallway is gone: a room with three doors", bi.MazeActive);
		// The rooms: every door leads to the same room again, until the one with the stalker in it.
		var rooms = bi.Rooms;
		int opened = 0;
		Check("the room has three doors", rooms is { Doors.Count: 3 }, $"{rooms?.Doors.Count}");
		CheckObjective("the compass points at the door ahead while the rooms repeat", "bunker_entrance_marker");
		while (rooms != null && !rooms.JumpscareDone && opened < 8)
		{
			await WaitUntil(() => _input.Enabled && !rooms.Busy, 6, ct);
			bool scaredBefore = rooms.JumpscareDone;
			var door = rooms.Doors[opened % rooms.Doors.Count];
			opened++;
			await UseIt(door, ct);
			await WaitUntil(() => !rooms.Busy && _input.Enabled, 6, ct);
			if (rooms.JumpscareDone && !scaredBefore) { Screenshot("room_scare"); Check("it hangs in the room", rooms.JumpscareDone && rooms.EyesShown, $"room {rooms.RoomsEntered} of a random {rooms.ScareAt}"); }
			if (opened == 1) Screenshot("same_room");
		}
		Check("the rooms repeat until it shows itself", rooms is { JumpscareDone: true }, $"opened {opened}");
		Check("the room went black, only its eyes", rooms is { BlackedOut: true, EyesShown: true });
		Check("the doors ahead are dead", rooms != null && rooms.Doors.All(d => !d.Enabled));
		Check("shoved out into the hall and the door shut behind", rooms is { ShovedOut: true, EntryOpen: false, EyesLit: false }
			&& rooms.ToLocal(_player.GlobalPosition).Z > 0.5f, $"{rooms?.ToLocal(_player.GlobalPosition)}");
		CheckObjective("the compass points back at the round door", "bunker_entrance_marker");
		Screenshot("room_black");
		// The run, with the chase behind: part way down, look back at all and it is right there, once.
		float rattleAtStart = rooms?.RattleGain ?? 0f;
		if (rooms != null)
		{
			await WalkTo(rooms.ToGlobal(new Vector3(0, 0, 14f)), 1.0f, ct, stopWhen: () => rooms.Exited, giveUp: 10f);
			await Aim(rooms.ToGlobal(new Vector3(0, 1.5f, -3f)), ct);
			await WaitUntil(() => rooms.FlashFired, 2, ct);
			Check("it flashes the moment they look back", rooms is { LookedBack: true, FlashFired: true });
			Screenshot("hall_flash");
			await WaitUntil(() => !rooms.FlashShowing, 2, ct);
			Check("and is gone again", rooms is { FlashShowing: false, EyesLit: false });
			await Aim(rooms.ExitWorld + Vector3.Up * 1.5f, ct);
		}
		if (rooms != null) await WalkTo(rooms.ExitWorld, 0.8f, ct, stopWhen: () => rooms.Exited, giveUp: 14f);
		await WaitUntil(() => rooms is { Exited: true }, 4, ct);
		Check("the run ends at the round door", rooms is { Exited: true });
		Check("its clicking rose as they ran", rooms != null && rooms.RattleGain > rattleAtStart + 0.3f, $"{rattleAtStart:0.00} -> {rooms?.RattleGain:0.00}");
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act10WalkieFound, 6, ct);
		Check("checkpoint 8: the walkie-talkie wakes outside", StoryManager.Instance.Current >= Checkpoint.Act10WalkieFound);
		await WaitUntil(() => _player.GlobalPosition.DistanceTo(bi.GlobalPosition) > 500f, 8, ct);
		Check("carried out of the bunker", _player.GlobalPosition.DistanceTo(bi.GlobalPosition) > 500f);
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
		// The climb is the player's own: up the whole flight on foot (a ramp collider), no pull.
		Check("the last staircase is still broken", act11.OriginalStairs is { NewelCapped: false });
		Check("the cap is still in hand at the last staircase", _inv.HasNewelPost);
		var top = act11.OriginalStairs?.GetNodeOrNull<Node3D>("TopTrigger");
		Check("the top landing exists", top != null);
		bool climbed = top != null && await WalkTo(top.GlobalPosition, 1.2f, ct, giveUp: 40f);
		Check("walked up the tall flight to the top landing", climbed, $"progress {act11.Progress:0.00} at {_player.GlobalPosition}");
		Check("the hum rose with the climb", act11.Progress > 0.9f, $"progress {act11.Progress:0.00}");
		Check("the last staircase called once or twice on the way up", act11.CallCount is 1 or 2, $"{act11.CallCount} calls");
		Check("still in control at the top", _input.Enabled);
		Screenshot("top_landing");
		// The broken post is right there: the cap goes back (E), and the ending follows without control returning.
		Check("the broken post's E-point waits on the landing", act11.CapSeat != null);
		if (act11.CapSeat != null) await UseIt(act11.CapSeat, ct);
		await WaitUntil(() => !_inv.HasNewelPost, 6, ct);
		Check("the cap leaves the player's hands", !_inv.HasNewelPost);
		Engine.TimeScale = 3.0;
		await WaitUntil(() => act11.OriginalStairs is { NewelCapped: true }, 12, ct);
		Check("the cap seats on the last staircase's post", act11.OriginalStairs is { NewelCapped: true });
		await WaitUntil(() => StoryManager.Instance.Current >= Checkpoint.Act11GiantEncounter, 60, ct);
		Check("checkpoint 9: the giant's touch", StoryManager.Instance.Current >= Checkpoint.Act11GiantEncounter);
		Check("the seated cap is saved with it", StoryManager.Instance.HasFlag(StoryManager.Flag.NewelSeated));
		Check("the ending is running", act11.EndingStarted);
		Check("they looked up into its eyes", act11.LookedUp);
		Check("trembling, then passed out looking at it", act11.Trembled && act11.PassedOut);
		Check("the hum hummed out over black", act11.HumOut);
		Check("the view never flipped during the ending (up-vector and pitch watched every driven frame)", !act11.ViewFlipped);
		Engine.TimeScale = 1.0;
		// Act 12 picks up from here: they wake at the lake instead of the credits rolling straight away.
		await WaitUntil(() => _input.Enabled, 10, ct);
		Check("control comes back at the lake, not the credits", _input.Enabled);
		Screenshot("giant_touch");
	}

	private async Task Act12Lake(CancellationToken ct)
	{
		var lake = GetTree().GetFirstNodeInGroup("lake_marker") as Lake;
		var crossing = AllOf<LakeCrossingEvent>().FirstOrDefault();
		Check("the lake and its crossing exist", lake != null && crossing != null);
		if (lake == null || crossing == null) return;

		var atmo = StoryBeat.Atmosphere(this);
		Check("dawn light at the lake", atmo == null || atmo.CurrentMood == ForestAtmosphere.Mood.Dawn, $"{atmo?.CurrentMood}");
		Check("woke near the lake", _player.GlobalPosition.DistanceTo(lake.WakeSpotWorld) < 30f, $"{_player.GlobalPosition}");
		if (_s.LakeDeathDone)
			Check("after drowning: back at the lake's shore, at the Act 12 checkpoint", StoryManager.Instance.Current == Checkpoint.Act11GiantEncounter
				&& _player.GlobalPosition.DistanceTo(lake.WakeSpotWorld) < 3f && PlayerDeath.Deaths >= 1, $"{StoryManager.Instance.Current} at {_player.GlobalPosition}");
		Screenshot("lake_wake");

		Check("the boat can be boarded", crossing.BoardPrompt != null);
		if (crossing.BoardPrompt == null) return;
		// onto the dock from its landward end (the beach is fenced off from the water either side of it)
		await WalkTo(lake.ToGlobal(new Vector3(0, 0, ProjectDS.World.LakeParts.LakeShape.DockStartZ + 1.2f)), 0.6f, ct, giveUp: 15f);
		await WalkTo(lake.ToGlobal(new Vector3(0, ProjectDS.World.LakeParts.LakeShape.DockDeck, ProjectDS.World.LakeParts.LakeShape.DockEndZ + 2.5f)), 0.6f, ct, giveUp: 15f);
		// the bot's walking is the unreliable part here, not the game: if it's been fenced off the dock, put it on it
		Vector3 onDock = lake.ToGlobal(new Vector3(0, ProjectDS.World.LakeParts.LakeShape.DockDeck + 0.05f, ProjectDS.World.LakeParts.LakeShape.DockEndZ + 2.5f));
		if (Flat(_player.GlobalPosition - onDock).Length() > 1.5f)
		{
			GD.Print($"[storytest] the bot was fenced off the dock at {_player.GlobalPosition}: placing it on the dock");
			await Inside(onDock, crossing.BoardPrompt.GlobalPosition, ct);
		}
		await WalkTo(crossing.BoardPrompt.GlobalPosition, 1.5f, ct, giveUp: 15f);
		await UseIt(crossing.BoardPrompt, ct);
		await WaitUntil(() => crossing.Boarded, 15, ct);
		Check("boarded the boat", crossing.Boarded);

		Check("sat down in the boat (the eye came down to a seated height)", _player.CameraRig.EyeHeight < 1.2f, $"eye {_player.CameraRig.EyeHeight:0.00}");
		await WaitUntil(() => crossing.Paddling, 8, ct);
		Check("pushed off from the dock and handed the oars over", crossing.Paddling);

		Engine.TimeScale = 3.0;
		await PaddleUntil(() => crossing.Progress > 0.3f || crossing.InBreach, ct, 20);
		Check("rowing makes way: strokes counted, the boat moving", crossing.StrokeCount > 4 && crossing.Speed > 0.5f, $"{crossing.StrokeCount} strokes, {crossing.Speed:0.0} m/s");
		Screenshot("rowing_calm");
		await PaddleUntil(() => !crossing.Paddling || crossing.InBreach, ct, 20);
		Check("paddled to the breach point", crossing.InBreach || crossing.Progress >= crossing.BreachAtFraction - 0.02f, $"progress {crossing.Progress:0.00}");
		await WaitUntil(() => crossing.InBreach, 5, ct);

		// the breach, beat by beat
		await WaitUntil(() => lake.Waves.EyeOpen > 0.9f, 10, ct);
		Check("an eye opens in the deep under the boat", lake.Waves.EyeOpen > 0.9f, $"open {lake.Waves.EyeOpen:0.00}");
		Screenshot("deep_eye");
		await WaitUntil(() => crossing.LastCreature is { Breaching: true }, 10, ct);
		Check("the creature breaches", crossing.LastCreature is { Breaching: true });
		await WaitUntil(() => crossing.LastCreature is { ColossusUp: true }, 8, ct);
		Check("the colossus towers out of the lake", crossing.LastCreature is { ColossusUp: true });
		Check("the world went red for a moment", crossing.RedPeak > 0.2f, $"peak {crossing.RedPeak:0.00}");
		Screenshot("breach_colossus");
		await WaitUntil(() => crossing.LastCreature is { EyesOpen: > 0 }, 8, ct);
		await Seconds(0.9, ct);
		Check("its eyes open, all together", crossing.LastCreature is { EyesOpen: > 20 }, $"{crossing.LastCreature?.EyesOpen} eyes");
		Screenshot("breach_eyes");
		await WaitUntil(() => crossing.LastCreature is { Looming: true }, 8, ct);
		Check("one bends over the boat to look inside", crossing.LastCreature is { Looming: true });
		Screenshot("breach_loom");
		await WaitUntil(() => !crossing.InBreach, 20, ct);
		Check("the water turns rough for the current", crossing.InCurrent);
		Check("white-capped chop", lake.Waves.Intensity > 0.8f, $"intensity {lake.Waves.Intensity:0.00}");
		Check("something follows the boat", crossing.HuntGap < 100f, $"gap {crossing.HuntGap:0.0}");

		if (!_s.LakeDeathDone)
		{
			// First time through: stop rowing. It should catch the boat and drag them under.
			_s.LakeDeathDone = true;
			await WaitUntil(() => crossing.Caught, 60, ct);
			Check("too slow: the hunter catches the boat", crossing.Caught, $"gap {crossing.HuntGap:0.0}");
			await WaitUntil(() => _player.GlobalPosition.Y < lake.GlobalPosition.Y - 2.5f, 15, ct);
			Screenshot("drowning");
			Check("dragged under the water", _player.GlobalPosition.Y < lake.GlobalPosition.Y - 2f, $"{_player.GlobalPosition}");
			Engine.TimeScale = 1.0;
			// The drowning reloads the checkpoint: this step is cancelled and starts over in the reloaded level.
			await WaitUntil(() => false, 40, ct);
			Check("the drowning reloaded the checkpoint", false, "no reload");
			return;
		}
		// Second time: row for it, at full speed, so the hunter falls behind.
		Engine.TimeScale = 1.0;

		await PaddleUntil(() => crossing.Progress > 0.7f || crossing.Landed, ct, 30);
		Screenshot("rowing_current");
		await PaddleUntil(() => crossing.Landed, ct, 30);
		Check("crossed the current and landed", crossing.Landed, $"progress {crossing.Progress:0.00}");
		Check("stood up again on the beach", _player.CameraRig.EyeHeight > 1.5f && _player.GlobalPosition.DistanceTo(lake.FarDockWorld) < 1.5f, $"eye {_player.CameraRig.EyeHeight:0.00} at {_player.GlobalPosition}");
		Screenshot("landed");

		// Subscribed rather than polled for the same reason Act2Climb's checkpoint watch is: the
		// handler runs synchronously inside ReachCheckpoint, so it can never miss the frame it fires.
		bool reached = false;
		void OnCheckpoint(Checkpoint cp)
		{
			if (cp != Checkpoint.Act12LakeCrossed) return;
			reached = true;
			Check("checkpoint 10: reached the rescue station", true);
		}
		StoryManager.Instance.CheckpointReached += OnCheckpoint;
		try
		{
			await WalkTo(lake.StationApproachWorld, 1.5f, ct, giveUp: 20f);
			await WalkTo(lake.StationDoorWorld, 1.0f, ct, giveUp: 15f);
			await WaitUntil(() => reached, 10, ct);
			if (!reached) Check("checkpoint 10: reached the rescue station", false, "trigger never fired");
		}
		finally { StoryManager.Instance.CheckpointReached -= OnCheckpoint; }
		Engine.TimeScale = 1.0;
		// Act 13 happens inside the station now (no credits here any more): wait for the entry
		// fade to hand control back before the next step starts driving the player around.
		await WaitUntil(() => _input.Enabled, 5, ct);
	}

/// <summary>
	/// Act 13, the whole station. Every stage checks whether the story already has it (the Room 2 flood
	/// kills the player once on purpose and the checkpoint reload starts this step over with the first
	/// half already done), so the step reads as "do whatever is left".
	/// </summary>
	private async Task Act13Station(CancellationToken ct)
	{
		var station = StationInterior.Instance;
		Check("the station interior exists", station != null);
		if (station == null) return;
		var s = StoryManager.Instance;
		await WaitUntil(() => _input.Enabled, 10, ct);
		await Frames(5, ct);
		var basement = station.Basement;
		var room1 = station.Room1;
		var room2 = station.Room2;
		var door3 = station.Door3;
		bool afterDeath = _s.Room2DeathDone;

		if (!s.HasFlag(StoryManager.Flag.StationRoom1Solved))
		{
			Check("inside the lobby", station.InLobby(_player.GlobalPosition), $"{_player.GlobalPosition}");
			Check("the lobby starts kept (decay stage 0)", station.Stage == 0, $"stage {station.Stage}");
			Screenshot("station_lobby_kept");
			// a look round the lobby's doorways: frames, the shut front door, no wall fighting another
			await Inside(station.ToGlobal(new Vector3(-1.5f, 0, 1.5f)), station.ToGlobal(new Vector3(StationInterior.HalfWidth, 1.3f, 0)), ct);
			Screenshot("tour_room1_door");
			await Inside(station.ToGlobal(new Vector3(1.5f, 0, -1.0f)), station.ToGlobal(new Vector3(-StationInterior.HalfWidth, 1.3f, 0.3f)), ct);
			Screenshot("tour_room2_door");
			await Inside(station.ToGlobal(new Vector3(0.2f, 0, 2.0f)), station.ToGlobal(new Vector3(0.2f, 1.3f, -StationInterior.HalfDepth)), ct);
			Screenshot("tour_front_wall");
			await Inside(station.ToGlobal(new Vector3(StationInterior.EntryGapX + 0.5f, 0, -StationInterior.HalfDepth + 1.0f)), station.ToGlobal(new Vector3(StationInterior.EntryGapX, 1.2f, -StationInterior.HalfDepth)), ct);
			Check("the front door is shut: the way in is no way out", _player.Interaction?.PromptText == "It won't open." || _player.Interaction?.Focused != null, $"'{_player.Interaction?.PromptText}'");
			Screenshot("tour_front_door");

			var knife = AllOf<Pickup>().FirstOrDefault(p => p.Kind == ToolKind.Knife && !p.Taken);
			Check("the knife is stuck in the desk", knife != null);
			if (knife != null) await UseIt(knife, ct);
			Check("the knife is in hand", _inv.HasTool(ToolKind.Knife));

			// the basement: cut the tape, down the brick stairs, the lights die
			var tapeUse = basement.GetNode<Interactable>("Door/Use");
			await Inside(station.ToGlobal(new Vector3(StationInterior.BasementGapX, 0, -StationInterior.HalfDepth + 1.5f)), tapeUse.GlobalPosition, ct);
			await UseIt(tapeUse, ct);
			Check("the tape-cut close-up opens", TapeCutOverlay.Instance is { IsOpen: true });
			while (TapeCutOverlay.Instance is { IsOpen: true }) { TapeCutOverlay.Instance.TestCompleteCut(); await Frames(2, ct); }
			Check("the basement tape is cut", basement.TapeCut);
			await Seconds(1.0, ct);
			await Inside(basement.ToGlobal(new Vector3(0, -0.2f, -1.6f)), basement.StairFootWorld, ct);
			Screenshot("basement_stairs");
			await WalkTo(basement.StairFootWorld, 0.8f, ct, giveUp: 12f);
			await Aim(station.ToGlobal(new Vector3(StationInterior.BasementGapX, 1.2f, -StationInterior.HalfDepth)), ct);
			Screenshot("tour_basement_stairs_up");
			Check("down the red brick stairs", _player.GlobalPosition.Y < basement.GlobalPosition.Y + StationBasement.Floor + 1f, $"{_player.GlobalPosition}");
			await WaitUntil(() => basement.LightsDead, 16, ct);
			Check("the lights struggle and die", basement.LightsDead);
			Screenshot("basement_dark_flooded");

			var wheel = basement.GetNode<Interactable>("Wheel/Use");
			for (int i = 0; i < 3; i++)
			{
				await UseIt(wheel, ct);
				await Seconds(1.8, ct);
			}
			Check("the wheel was turned three times", basement.Turns >= 3, $"{basement.Turns}");
			await WaitUntil(() => basement.Drained, 14, ct);
			Check("the drain takes the water", basement.Drained);
			Check("a dead eye is stuck in the drain", basement.Eye is { Visible: true });
			await Aim(basement.Eye.GlobalPosition, ct);
			Screenshot("basement_dead_eye");
			// it only moves when it isn't watched: look away, look back, it's facing us
			var eyeModel = basement.Eye.GetNode<Node3D>("DeadEye");
			await Aim(basement.Eye.GlobalPosition + (basement.Eye.GlobalPosition - _player.CameraRig.Camera.GlobalPosition).Normalized() * -8f + Vector3.Up * 3f, ct);
			_player.CameraRig.SnapBehind(_player.CameraRig.Yaw + Mathf.Pi);
			await Seconds(0.5, ct);
			Vector3 toCam = (_player.CameraRig.Camera.GlobalPosition - basement.Eye.GlobalPosition).Normalized();
			Check("the dead eye turned to face us while we looked away", (-eyeModel.GlobalBasis.Z).Dot(toCam) > 0.8f, $"dot {(-eyeModel.GlobalBasis.Z).Dot(toCam):0.00}");
			await WaitUntil(() => basement.ClockBroken, 14, ct);
			Check("the drowned clock chimes, spits a key and bursts", basement.ClockBroken);
			var key = AllOf<Pickup>().FirstOrDefault(p => p.Kind == ToolKind.Key && !p.Taken);
			Check("the clock's key is out on the floor", key != null);
			if (key != null)
			{
				// a step back from it, as anyone would stand to pick something off the floor
				Vector3 kp = key.GlobalPosition;
				Vector3 back = Flat(basement.RoomCentreWorld - kp).Normalized();
				Vector3 stand = kp + back * 1.2f;
				stand.Y = basement.GlobalPosition.Y + StationBasement.Floor + 0.02f;
				await Inside(stand, kp, ct);
				await UseIt(key, ct);
			}
			Check("the key is in hand", _inv.HasTool(ToolKind.Key));

			// Room 1
			await Inside(station.ToGlobal(new Vector3(StationInterior.HalfWidth - 1.6f, 0, 0.3f)), station.ToGlobal(new Vector3(StationInterior.HalfWidth, 1f, 0)), ct);
			Check("the lobby has rotted a stage while we were downstairs", station.Stage == 1, $"stage {station.Stage}");
			var room1Door = station.GetNode<StationDoor>("Room1Door");
			await UseIt(room1Door.GetNode<Interactable>("Use"), ct);
			Check("the key opens room 1", room1Door is { Locked: false, IsOpen: true });
			await Seconds(1.0, ct);
			var boxUse = room1.GetNode<Interactable>("CigarBox/Use");
			await Inside(room1.ToGlobal(new Vector3(1.2f, 0, 0.6f)), station.ToGlobal(new Vector3(StationInterior.HalfWidth, 1.3f, 0)), ct);
			Screenshot("tour_room1_door_inside");
			await Inside(room1.ToGlobal(new Vector3(-1.2f, 0, 0.2f)), boxUse.GlobalPosition, ct);
			Screenshot("room1_writing");
			await UseIt(boxUse, ct);
			while (TapeCutOverlay.Instance is { IsOpen: true }) { TapeCutOverlay.Instance.TestCompleteCut(); await Frames(2, ct); }
			Check("all three sides cut, the box opens on a button", room1.BoxOpen && room1.BoxCutsDone == 3, $"{room1.BoxCutsDone} cuts");
			await Seconds(1.0, ct);
			var button = room1.GetNodeOrNull<Interactable>("CigarBox/Button/Press");
			if (button != null) await UseIt(button, ct);
			Check("the button melts the writing", room1.Solved);
			Check("checkpoint: Room 1 solved, saved before Room 2", s.Current == Checkpoint.Act13Room1Solved, $"{s.Current}");
			await Seconds(5.5, ct);
			Screenshot("room1_melted");
		}

		if (!s.HasFlag(StoryManager.Flag.StationRoom2Solved))
		{
			if (afterDeath)
				Check("after drowning: back at the Room 1 checkpoint, by Room 2's door", s.Current == Checkpoint.Act13Room1Solved && station.InLobby(_player.GlobalPosition) && PlayerDeath.Deaths >= 1, $"{s.Current} at {_player.GlobalPosition}");
			var room2Door = station.Room2Door;
			await WaitUntil(() => !room2Door.Locked, 5, ct);
			Check("Room 2's door stands open", !room2Door.Locked);
			await Inside(station.ToGlobal(new Vector3(-StationInterior.HalfWidth + 1.4f, 0, 0.3f)), room2.GlobalPosition, ct);
			await WalkTo(room2.ToGlobal(new Vector3(1.2f, 0, 0.9f)), 0.6f, ct, giveUp: 8f);
			await WaitUntil(() => room2.DoorShut, 3, ct);
			Check("the door slams shut behind us", room2.DoorShut && room2Door.Locked);
			await Seconds(6.5, ct);
			Check("someone knocks, softly", room2.Knocks > 0, $"{room2.Knocks}");
			Screenshot("room2_red_room");
			await Aim(station.ToGlobal(new Vector3(-StationInterior.HalfWidth, 1.3f, 0)), ct);
			Screenshot("tour_room2_door_inside");
			await UseIt(room2.BoxUse, ct);
			await WaitUntil(() => room2.Flooding, 8, ct);
			Check("using the box breaks the window: the lake comes in", room2.WindowBroken && room2.Flooding);
			await WaitUntil(() => _input.Enabled, 8, ct);
			Screenshot("room2_flooding");

			if (!_s.Room2DeathDone)
			{
				// First time: leave the box alone and let it fill. It should drown us and reload the checkpoint.
				_s.Room2DeathDone = true;
				Engine.TimeScale = 4.0;
				await WaitUntil(() => room2.Blood, 30, ct);
				Check("at the knees, the water turns to blood", room2.Blood, $"level {room2.Level:0.00}");
				Engine.TimeScale = 1.0;
				Screenshot("room2_blood");
				Engine.TimeScale = 4.0;
				await WaitUntil(() => PlayerDeath.Dying, 40, ct);
				Check("the room fills: drowned", PlayerDeath.Dying, $"level {room2.Level:0.00}");
				Engine.TimeScale = 1.0;
				await WaitUntil(() => false, 30, ct);
				Check("the drowning reloaded the checkpoint", false, "no reload");
				return;
			}
			// Second time: work the cryptex. Six rings to STAIRS, one clunky turn at a time.
			await UseIt(room2.BoxUse, ct);
			Check("the cryptex close-up opens", CryptexOverlay.Instance is { IsOpen: true });
			await Frames(3, ct);
			Screenshot("cryptex_closeup");
			// let the blood rise over it: it must stay readable through the surface
			Engine.TimeScale = 4.0;
			await WaitUntil(() => room2.Level > 1.02f, 40, ct);
			Engine.TimeScale = 1.0;
			await Frames(3, ct);
			Screenshot("cryptex_under_blood");
			var box = room2.Box;
			int stuck = 0;
			for (int r = 0; r < Cryptex.Rings && !box.Solved; r++)
			{
				box.Select(r);
				int guard = 0;
				while (box.Letters[r] != Cryptex.Word[r] - 'A' && guard++ < 60)
				{
					int diff = ((Cryptex.Word[r] - 'A') - box.Letters[r] + 26) % 26;
					if (!box.Turn(diff <= 13 ? 1 : -1)) { stuck++; await Seconds(0.35, ct); }
					await Frames(2, ct);
				}
			}
			Check("the cryptex spells STAIRS", box.Solved, box.Reading);
			Check("it stuck at least once on the way (old and clunky)", stuck > 0, $"{stuck} sticks");
			await WaitUntil(() => room2.Solved, 12, ct);
			Check("the blood goes back out of the window; the room is as it was", room2.Solved && room2.Level < 0.05f, $"level {room2.Level:0.00}");
			await WaitUntil(() => _input.Enabled, 5, ct);
			Screenshot("room2_restored");
			var lighter = AllOf<Pickup>().FirstOrDefault(p => p.Kind == ToolKind.Lighter && !p.Taken);
			Check("the cryptex opens on a lighter", lighter != null);
			if (lighter != null) await UseIt(lighter, ct);
			Check("the lighter is in hand", _inv.HasTool(ToolKind.Lighter));
		}

		// The web, and the door behind it.
		if (!s.HasFlag(StoryManager.Flag.StationWebBurned))
		{
			await Inside(station.ToGlobal(new Vector3(-1.4f, 0, 3.8f)), door3.GlobalPosition + Vector3.Up * 1.2f, ct);
			Check("the lobby is industrial now", station.Stage == 3, $"stage {station.Stage}");
			Screenshot("lobby_industrial");
			await UseIt(door3.WebUse, ct);
			await WaitUntil(() => s.HasFlag(StoryManager.Flag.StationWebBurned), 8, ct);
			Check("the lighter burns the web", door3.WebBurned);
			await Seconds(0.5, ct);
		}
		await Inside(station.ToGlobal(new Vector3(0.6f, 0, 3.8f)), door3.GlobalPosition + Vector3.Up * 1.3f, ct);
		await WaitUntil(() => door3.Seen, 3, ct);
		Check("an iron door: LOOK, TOUCH, CLIMB", door3.Seen);
		Screenshot("iron_door");

		// CLIMB: the staircase in Room 2
		if (!s.HasFlag(StoryManager.Flag.StationStepTaken))
		{
			await Inside(station.ToGlobal(new Vector3(-StationInterior.HalfWidth + 1.2f, 0, 0.3f)), room2.GlobalPosition, ct);
			await Seconds(0.3, ct);
			Check("a staircase has grown where the table was", room2.StairsUp);
			await Inside(room2.ToGlobal(new Vector3(1.6f, 0, 1.8f)), room2.StairTopWorld, ct);
			Screenshot("room2_staircase");
			await WalkTo(room2.StairFootWorld, 0.5f, ct, giveUp: 6f);
			await WalkTo(room2.StairTopWorld, 0.4f, ct, stopWhen: () => s.HasFlag(StoryManager.Flag.StationStepTaken), giveUp: 10f);
			await WaitUntil(() => s.HasFlag(StoryManager.Flag.StationStepTaken), 8, ct);
			Check("climbed them: holding one of the steps", _inv.HasTool(ToolKind.StairTread));
			await WaitUntil(() => _input.Enabled, 8, ct);
		}
		// TOUCH: the hand in the right puddle
		if (!s.HasFlag(StoryManager.Flag.StationHandTaken))
		{
			await Inside(room1.ToGlobal(new Vector3(-1.4f, 0, 0f)), room1.Puddles[0].GlobalPosition, ct);
			await UseIt(room1.Puddles[0], ct);
			await Seconds(3.5, ct);
			Check("the wrong puddle: something takes our wrist and lets go", room1.WrongReaches == 1 && !_inv.HasTool(ToolKind.PaleHand));
			await UseIt(room1.Puddles[1], ct);
			await Seconds(1.0, ct);
			Check("under DO NOT TOUCH THEM: the hand", _inv.HasTool(ToolKind.PaleHand));
		}
		// LOOK: the eye, loose in the dark basement
		if (!s.HasFlag(StoryManager.Flag.StationEyeTaken))
		{
			await Inside(basement.StairFootWorld, basement.RoomCentreWorld, ct);
			var eye = basement.Eye;
			Check("the eye is loose now", eye.Wandering);
			// look away from it for a while: it moves
			await Aim(eye.GlobalPosition, ct);
			_player.CameraRig.SnapBehind(_player.CameraRig.Yaw + Mathf.Pi);
			await Seconds(5, ct);
			Check("while unwatched, it moved", eye.Hops > 0, $"{eye.Hops} hops");
			// come at it keeping it in sight
			Vector3 at = eye.GlobalPosition;
			Vector3 near = at + (Flat(basement.RoomCentreWorld - at).Normalized() * 1.4f);
			await Inside(near with { Y = basement.GlobalPosition.Y + StationBasement.Floor + 0.05f }, at, ct);
			await Aim(eye.GlobalPosition, ct);
			Screenshot("basement_eye_hunt");
			var take = eye.GetNode<Interactable>("Take");
			await WaitUntil(() => take.Enabled, 2, ct);
			await UseIt(take, ct);
			Check("caught it looking: the eye is taken", _inv.HasTool(ToolKind.DeadEye), $"hops {eye.Hops}, seen {eye.SeenNow}");
		}
		// set all three
		await Inside(station.ToGlobal(new Vector3(0.6f, 0, 3.8f)), door3.GlobalPosition + Vector3.Up * 1.3f, ct);
		for (int i = 0; i < 3 && !door3.Opened; i++) { await UseIt(door3.DoorUse, ct); await Seconds(0.8, ct); }
		await WaitUntil(() => door3.Opened, 6, ct);
		Check("all three set: the iron door opens", door3.Opened && door3.PiecesSet == 3, $"{door3.PiecesSet} set");
		await Seconds(4, ct);

		// through the iron door: Act 13 done, Act 14's save
		var room3 = station.Room3;
		bool finished = false;
		void OnCp(Checkpoint cp) { if (cp == Checkpoint.Act13Finished) finished = true; }
		s.CheckpointReached += OnCp;
		try
		{
			await Inside(station.ToGlobal(new Vector3(0, 0, StationInterior.HalfDepth - 1.2f)), room3.EntryWorld, ct);
			await WalkTo(room3.ToGlobal(new Vector3(0, 0, 2.2f)), 0.6f, ct, stopWhen: () => finished, giveUp: 8f);
			await WaitUntil(() => finished, 5, ct);
			Check("through the iron door: Act 13's final checkpoint (Act 14 starts here)", finished && room3.Entered);
			Check("the lobby had turned to flesh behind us", station.Stage == 4, $"stage {station.Stage}");
		}
		finally { s.CheckpointReached -= OnCp; }
	}

	// ------------------------------------------------------------------ Act 14

	/// <summary>The stairwell corner the player is nearest to, going down.</summary>
	private int CornerOf(Stairwell sw) =>
		Mathf.Max(0, Mathf.FloorToInt((Stairwell.Y0 - sw.ToLocal(_player.GlobalPosition).Y) / Stairwell.FlightDrop + 0.25f));

	/// <summary>Walks the stairwell corner to corner, never running, from wherever the player is down to
	/// corner <paramref name="toCorner"/>. Returns false if it got stuck.</summary>
	private async Task<bool> DescendTo(Stairwell sw, int toCorner, CancellationToken ct, System.Action<int> atCorner = null)
	{
		int stuck = 0;
		while (true)
		{
			int k = Mathf.Min(CornerOf(sw), toCorner);
			atCorner?.Invoke(k);
			if (k >= toCorner && _player.GlobalPosition.DistanceTo(sw.CornerWorld(toCorner)) < 0.9f) return true;
			int next = Mathf.Min(k + 1, toCorner);
			float before = _player.GlobalPosition.Y;
			await WalkTo(sw.CornerWorld(next), 0.45f, ct, giveUp: 6f);
			if (Mathf.Abs(_player.GlobalPosition.Y - before) < 0.05f && _player.GlobalPosition.DistanceTo(sw.CornerWorld(next)) > 0.9f)
			{
				if (++stuck > 3) { Check("descending", "kept going down", false, $"stuck at corner {k}, {_player.GlobalPosition}"); return false; }
			}
			else stuck = 0;
		}
	}

	private async Task Act14Stairwell(CancellationToken ct)
	{
		var station = StationInterior.Instance;
		var room3 = station?.Room3;
		var sw = room3?.Stairs;
		Check("Room 3 and its stairwell exist", sw != null);
		if (sw == null) return;
		var s = StoryManager.Instance;
		await WaitUntil(() => _input.Enabled, 10, ct);
		await Frames(5, ct);
		var atmo = StoryBeat.Atmosphere(this);

		if (_s.Act14AcrossDone)
		{
			// Second time: Continue from Act 14's end put us on the chamber floor; go back up and jump down instead.
			await Seconds(1.5, ct);
			Check("Continue from Act 14's end: standing in the chamber under the shaft", s.Current == Checkpoint.Act14Finished
				&& _player.GlobalPosition.DistanceTo(sw.LandingSpotWorld) < 2f, $"{s.Current} at {_player.GlobalPosition}");
			Check("it's dark down here", atmo == null || atmo.Underground > 0.5f, $"underground {atmo?.Underground:0.00}");
			Screenshot("act15_start");
			await Inside(sw.CornerWorld(sw.GapCorner - 2), sw.CornerWorld(sw.GapCorner - 1), ct);
			await DescendTo(sw, sw.GapCorner, ct);
			await WalkTo(sw.GapEdgeWorld, 0.3f, ct, stopWhen: () => sw.Choosing, giveUp: 4f);
			await WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
			Check("the question again at the edge", ChoicePrompt.Instance is { IsOpen: true });
			ChoicePrompt.Instance?.TestChoose(1);
			await WaitUntil(() => sw.StoodUp, 20, ct);
			Check("jumped down: landed on their feet in the chamber", sw.Jumped && !sw.JumpedAcross && sw.StoodUp
				&& _player.GlobalPosition.DistanceTo(sw.LandingSpotWorld) < 1.5f, $"at {_player.GlobalPosition}");
			await WaitUntil(() => _input.Enabled, 3, ct);
			Check("ready to run: control straight back, standing tall", _input.Enabled && _player.CameraRig.EyeHeight > 1.5f, $"eye {_player.CameraRig.EyeHeight:0.00}");
			Check("the way down is remembered for Act 15", s.HasFlag(StoryManager.Flag.Act14JumpedDown));
			Screenshot("jumped_down");
			return;
		}

		Check("Act 14 starts at its save: Room 3", s.Current >= Checkpoint.Act13Finished && room3.Entered, $"{s.Current}");
		Check("the walls are hung with burnt-out portraits", room3.Portraits >= 20, $"{room3.Portraits}");
		await Inside(room3.ToGlobal(new Vector3(0, 0, StationRoom3.CorridorEnd + 2f)), room3.ToGlobal(new Vector3(-4.5f, 2.2f, 12f)), ct);
		await Aim(room3.ToGlobal(new Vector3(-4.5f, 2.2f, 12f)), ct);
		Screenshot("room3_portraits");
		await Inside(room3.ToGlobal(new Vector3(-3.2f, 0, 11f)), room3.ToGlobal(new Vector3(-5f, 1.8f, 11f)), ct);
		await Aim(room3.ToGlobal(new Vector3(-5f, 1.75f, 11f)), ct);
		Screenshot("portrait_close");
		await Aim(sw.TrenchTopWorld + Vector3.Down * 0.5f, ct);
		Screenshot("room3_hole");

		// down the trench into the top of the shaft
		await WalkTo(sw.TrenchTopWorld, 0.6f, ct, giveUp: 12f);
		await WalkTo(sw.CornerWorld(0), 0.5f, ct, giveUp: 8f);
		Check("down the concrete steps to the top landing", _player.GlobalPosition.DistanceTo(sw.CornerWorld(0)) < 1f, $"{_player.GlobalPosition}");
		await Seconds(2.5, ct);
		Check("underground: the world's light is gone", atmo == null || atmo.Underground > 0.6f, $"underground {atmo?.Underground:0.00}");
		await Aim(sw.ToGlobal(new Vector3(0, Stairwell.CornerY(12), 0)), ct);
		Screenshot("stairwell_down_the_well");

		Engine.TimeScale = 3.0;
		try
		{
			await DescendTo(sw, 8 * 4, ct);
			Check("walking down: turn 8 reached, never taken back", sw.DeepestRev >= 7 && sw.LoopBacks == 0, $"deepest {sw.DeepestRev}, loops {sw.LoopBacks}");
			Screenshot("stairwell_turn8");
			await Aim(sw.ToGlobal(new Vector3(-Stairwell.H, Stairwell.CornerY(32) + 1.45f, Stairwell.CornerXZ(32).Y)), ct);
			Screenshot("painted_number");

			// run: taken back up, seamlessly
			int revBefore = sw.PlayerRev;
			int k = CornerOf(sw);
			float maxStep = 0f, maxY = 0f;
			double t = 0;
			_input.ScriptedRun = true;
			try
			{
				while (sw.LoopBacks == 0 && t < 12)
				{
					var d = Flat(sw.CornerWorld(k + 1) - _player.GlobalPosition);
					if (d.Length() < 0.5f) { k++; continue; }
					_player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
					_input.ScriptedMove = new Vector2(0, 1);
					Vector3 lp = sw.ToLocal(_player.GlobalPosition);
					await Frames(1, ct);
					t += GetProcessDeltaTime();
					Vector3 now = sw.ToLocal(_player.GlobalPosition);
					if (sw.LoopBacks > 0) { maxStep = new Vector2(now.X - lp.X, now.Z - lp.Z).Length(); maxY = now.Y - lp.Y; }
				}
			}
			finally { _input.ScriptedRun = false; _input.ScriptedMove = Vector2.Zero; }
			Check("sprinting on the stairs: taken back up the shaft", sw.LoopBacks >= 1 && sw.PlayerRev < revBefore, $"turn {revBefore} -> {sw.PlayerRev}");
			float turns = maxY / Stairwell.RevDrop;
			Check("the move is seamless: whole turns, same place on the turn, grime held", Mathf.Abs(turns - Mathf.Round(turns)) < 0.05f && maxStep < 0.5f && sw.GrimeBias > 0f,
				$"{turns:0.00} turns up, xz step {maxStep:0.00}, grime bias {sw.GrimeBias:0.000}");
			Check("back near the top (two to four turns down)", sw.PlayerRev >= Stairwell.Period - 1 && sw.PlayerRev <= Stairwell.Period * 2, $"turn {sw.PlayerRev}");
			await Frames(10, ct);
			Check("still on the stairs after it", sw.InShaft(_player.GlobalPosition), $"{_player.GlobalPosition}");
			Screenshot("after_the_loop");

			// all the way down, walking
			ulong descentStart = Time.GetTicksMsec();
			int descentFrom = sw.PlayerRev;
			bool deepShot = false, midShot = false;
			await DescendTo(sw, sw.GapCorner - 1, ct, k2 =>
			{
				if (!midShot && k2 >= 30 * 4) { midShot = true; Screenshot("stairwell_turn30"); }
				if (!deepShot && k2 >= 60 * 4) { deepShot = true; Screenshot("stairwell_turn60_grimy"); }
			});
			double gameSec = (Time.GetTicksMsec() - descentStart) / 1000.0 * Engine.TimeScale;
			double perTurn = gameSec / Mathf.Max(1, sw.Revolutions - descentFrom);
			Check("walked all the way down", sw.DeepestRev >= sw.Revolutions - 1,
				$"deepest {sw.DeepestRev} of {sw.Revolutions}; {perTurn:0.0} s a turn at a walk, so about {perTurn * sw.Revolutions / 60.0:0.0} min top to bottom");
		}
		finally { Engine.TimeScale = 1.0; _input.ScriptedRun = false; }
		Check("hardly any light down here: most bulbs dead", sw.LitBulbs < sw.Revolutions / Stairwell.Period, $"{sw.LitBulbs} lit");

		// the fallen flight, and the question
		await DescendTo(sw, sw.GapCorner, ct);
		await Aim(sw.GapEdgeWorld + sw.GapDir * 2f, ct);
		Screenshot("the_gap");
		await WalkTo(sw.GapEdgeWorld, 0.3f, ct, stopWhen: () => sw.Choosing, giveUp: 4f);
		await WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		Check("at the edge: jump across? or jump down?", ChoicePrompt.Instance is { IsOpen: true } && sw.Choosing);
		Screenshot("the_choice");
		// stepping back lets go of it; it asks again at the edge
		ChoicePrompt.Instance?.TestChoose(-1);
		await Seconds(1.0, ct);
		Check("stepping back from it lets go of the question", !sw.Choosing && _input.Enabled);
		await Seconds(2.0, ct);
		await WalkTo(sw.GapEdgeWorld, 0.3f, ct, stopWhen: () => sw.Choosing, giveUp: 4f);
		await WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		ChoicePrompt.Instance?.TestChoose(0);
		await WaitUntil(() => sw.AcrossFell, 10, ct);
		Check("jumped across: the far landing holds, then goes", sw.JumpedAcross && sw.AcrossFell);
		await Seconds(0.6, ct);
		Screenshot("falling");
		await WaitUntil(() => sw.Landed, 10, ct);
		await Seconds(4.5, ct);
		Screenshot("on_the_floor");
		await WaitUntil(() => sw.StoodUp, 25, ct);
		Check("came to on the floor and got up, slowly", sw.StoodUp && _player.CameraRig.EyeHeight > 1.5f && _player.GlobalPosition.Y < sw.LandingSpotWorld.Y + 1f, $"at {_player.GlobalPosition}");
		await WaitUntil(() => s.Current == Checkpoint.Act14Finished, 5, ct);
		Check("Act 14's end saved: Act 15 starts here", s.Current == Checkpoint.Act14Finished && s.HasFlag(StoryManager.Flag.Act14JumpedAcross), $"{s.Current}");
		Screenshot("stood_up");

		// Continue from here and take the other way down
		_s.Act14AcrossDone = true;
		await Seconds(0.5, ct);
		StoryManager.Instance.ContinueGame();
		await WaitUntil(() => false, 30, ct);
		Check("Continue reloaded the level", false, "no reload");
	}

	// ------------------------------------------------------------------ Act 15

	/// <summary>Walks straight down the hallway toward local z <paramref name="toZ"/> in short hops (never
	/// running unless asked), obeying the lights if <paramref name="obey"/>: moving only while green.</summary>
	private async Task<bool> HallWalk(Act15Hallway hw, float toZ, bool obey, CancellationToken ct, float giveUp = 600f, bool run = false)
	{
		double t = 0;
		try
		{
			while (hw.PlayerZ < toZ)
			{
				ct.ThrowIfCancellationRequested();
				bool go = _input.Enabled && (!obey || hw.State is Act15Hallway.Phase.Waiting or Act15Hallway.Phase.Green);
				if (go)
				{
					Vector3 target = hw.AlongWorld(Mathf.Min(hw.PlayerZ + 6f, toZ + 0.5f));
					// step round him if he's in the way
					Vector3 sl = hw.ToLocal(hw.Shadow.GlobalPosition);
					if (sl.Z > hw.PlayerZ - 1f && sl.Z < hw.PlayerZ + 6f && Mathf.Abs(sl.X) < 1.2f)
						target = hw.ToGlobal(new Vector3(sl.X > 0 ? -1.2f : 1.2f, 0.05f, Mathf.Min(hw.PlayerZ + 3f, toZ + 0.5f)));
					Steer(target);
					_input.ScriptedMove = new Vector2(0, 1);
					_input.ScriptedRun = run;
				}
				else { _input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false; }
				await Frames(1, ct);
				t += GetProcessDeltaTime();
				if (t > giveUp) { Check("hallway walk", "got there", false, $"stuck at z {hw.PlayerZ:0}"); return false; }
			}
			return true;
		}
		finally { _input.ScriptedMove = Vector2.Zero; _input.ScriptedRun = false; }
	}

	private async Task Act15Hall(CancellationToken ct)
	{
		var sw = StationInterior.Instance?.Room3?.Stairs;
		await WaitUntil(() => sw?.Hallway?.Shadow != null, 10, ct);
		var hw = sw?.Hallway;
		Check("the long hallway exists", hw != null);
		if (hw == null) return;
		var s = StoryManager.Instance;
		await WaitUntil(() => _input.Enabled, 10, ct);
		await Frames(5, ct);
		Check("Act 15 starts at its save: the chamber under the stairwell", s.Current == Checkpoint.Act14Finished
			&& _player.GlobalPosition.DistanceTo(sw.LandingSpotWorld) < 2.5f, $"{s.Current} at {_player.GlobalPosition}");

		// into the passage, and the hallway
		await WalkTo(sw.ChamberExitWorld, 0.6f, ct, giveUp: 10f);
		await WalkTo(hw.AlongWorld(2f), 0.6f, ct, giveUp: 10f);
		await Seconds(2.0, ct);
		var atmo = StoryBeat.Atmosphere(this);
		Check("the hallway is underground, and green", atmo == null || atmo.Underground > 0.9f && atmo.UndergroundFogColor.G > atmo.UndergroundFogColor.R, $"fog {atmo?.UndergroundFogColor}");
		Screenshot("hallway_narrow");
		await Aim(hw.AlongWorld(12f) + Vector3.Up * 16f, ct);
		Screenshot("hallway_looking_up");

		Engine.TimeScale = 3.0;
		try
		{
			ulong t0 = Time.GetTicksMsec();
			float z0 = hw.PlayerZ;
			await HallWalk(hw, Act15Hallway.Part1, false, ct);
			double walked = (Time.GetTicksMsec() - t0) / 1000.0 * Engine.TimeScale;
			double full = walked * Act15Hallway.Part1 / Mathf.Max(1f, Act15Hallway.Part1 - z0);
			Check("the narrow stretch: about two minutes at a walk", full > 100 && full < 150, $"{full:0} s");
			await HallWalk(hw, Act15Hallway.Part1 + Act15Hallway.Taper + 4f, false, ct);
			Engine.TimeScale = 1.0;
			await Aim(hw.Shadow.GlobalPosition + Vector3.Up * 1.8f, ct);
			Screenshot("the_shadow_man");
			Check("he stands frozen in the green", hw.State == ProjectDS.World.Act15Hallway.Phase.Waiting);
			Engine.TimeScale = 3.0;
			// past him
			await HallWalk(hw, Act15Hallway.ShadowZ + 3f, false, ct);
			Check("past him: red light, green light begins", hw.State != ProjectDS.World.Act15Hallway.Phase.Waiting, $"{hw.State}");

			if (!_s.Act15DeathDone)
			{
				// First time: keep walking into the red. He should take them, and it's back to Act 15's start.
				_s.Act15DeathDone = true;
				var walk = HallWalk(hw, Act15Hallway.End - 2f, false, ct, 90f);
				await WaitUntil(() => hw.State == ProjectDS.World.Act15Hallway.Phase.Red, 60, ct);
				Engine.TimeScale = 1.0;
				await Frames(3, ct);
				Vector3 toShadow = hw.Shadow.GlobalPosition - _player.GlobalPosition;
				Vector3 fwd = -_player.CameraRig.GlobalBasis.Z;
				Check("red: the lights change, and he is behind you", toShadow.Length() < 7f && fwd.Dot(toShadow.Normalized()) < 0f, $"{toShadow.Length():0.0} m, behind {fwd.Dot(toShadow.Normalized()):0.00}");
				await Seconds(0.8, ct);
				Screenshot("taken");
				await WaitUntil(() => PlayerDeath.Dying, 5, ct);
				Check("moved in the red: taken", PlayerDeath.Dying);
				await WaitUntil(() => false, 30, ct);
				Check("taken: the checkpoint reloaded", false, "no reload");
				return;
			}
			Check("after being taken: back at Act 15's start", PlayerDeath.Deaths >= 1);

			// play it properly: walk in the green, stand still in the red
			ulong t1 = Time.GetTicksMsec();
			bool redShot = false, turnedShot = false;
			var game = HallWalk(hw, Act15Hallway.End - 1.2f, true, ct, 900f);
			while (!game.IsCompleted)
			{
				await Frames(1, ct);
				if (hw.State == ProjectDS.World.Act15Hallway.Phase.Red && !redShot && hw.Reds >= 2)
				{
					redShot = true;
					Engine.TimeScale = 1.0;
					float firstD = hw.Shadow.GlobalPosition.DistanceTo(_player.GlobalPosition);
					// look round at him (looking is allowed; moving isn't) and watch him come
					await Aim(hw.Shadow.GlobalPosition + Vector3.Up * 1.9f, ct);
					await WaitUntil(() => hw.State != ProjectDS.World.Act15Hallway.Phase.Red || hw.Shadow.GlobalPosition.DistanceTo(_player.GlobalPosition) < 1.8f, 12, ct);
					float lastD = hw.Shadow.GlobalPosition.DistanceTo(_player.GlobalPosition);
					Check("in the red he jumps closer, a step at a time, while you watch", firstD > 3f && lastD < 1.8f, $"{firstD:0.0} m, then {lastD:0.0} m");
					await Seconds(0.3, ct);
					Screenshot("red_light_standing_still");
					// look round at him (looking is allowed; moving isn't)
					await Aim(hw.Shadow.GlobalPosition + Vector3.Up * 1.9f, ct);
					Screenshot("he_is_behind_you");
					turnedShot = true;
					Engine.TimeScale = 3.0;
				}
			}
			await game;
			double minutes = (Time.GetTicksMsec() - t1) / 1000.0 * 3.0 / 60.0;
			Check("red light, green light all the way to the door: never taken", !PlayerDeath.Dying && hw.Reds >= 8, $"{hw.Reds} reds, about {minutes:0.0} min");
			Check("looked round at him in the red and lived", turnedShot);
		}
		finally { Engine.TimeScale = 1.0; }

		// the door, the closet
		while (hw.State is ProjectDS.World.Act15Hallway.Phase.Red or ProjectDS.World.Act15Hallway.Phase.Flicker) await Frames(1, ct);
		var door = hw.GetNode<Interactable>("ClosetDoor/Use");
		await UseIt(door, ct);
		await WaitUntil(() => hw.DoorOpen, 3, ct);
		Check("the closet door at the end opens", hw.DoorOpen);
		await Seconds(1.4, ct);
		Screenshot("closet_door");
		await WalkTo(hw.ClosetWorld, 0.4f, ct, stopWhen: () => hw.InCloset, giveUp: 10f);
		await WaitUntil(() => hw.Finished, 5, ct);
		Check("into the janitor's closet: the door shuts behind them (Act 16's save)", hw.Finished && s.Current == Checkpoint.Act15Finished, $"{s.Current}");
		await Seconds(1.0, ct);
		Screenshot("closet_dark");
	}

	// ------------------------------------------------------------------ Acts 16-17

	private async Task Act16Closet(CancellationToken ct)
	{
		var hw = StationInterior.Instance?.Room3?.Stairs?.Hallway;
		await WaitUntil(() => hw?.SwitchUse != null, 10, ct);
		Check("the closet exists", hw != null);
		if (hw == null) return;
		var s = StoryManager.Instance;
		await WaitUntil(() => _input.Enabled, 10, ct);
		await Frames(5, ct);
		Check("Act 16 starts at its save: shut in the janitor's closet", s.Current == Checkpoint.Act15Finished
			&& _player.GlobalPosition.DistanceTo(hw.ClosetWorld) < 1.5f && hw.Finished, $"{s.Current} at {_player.GlobalPosition}");
		Screenshot("closet_dark");
		// the door first: can't find the handle in the dark
		await UseIt(hw.InsideDoorUse, ct);
		await Seconds(0.5, ct);
		Check("in the dark the door won't open", !hw.ClosetDoorOpen);
		// the switch: red, green, then an ordinary light, in about six seconds
		await UseIt(hw.SwitchUse, ct);
		Check("the light switch", hw.LightsOn);
		await Seconds(1.0, ct);
		Screenshot("closet_red");
		await Seconds(2.3, ct);
		Screenshot("closet_green");
		ulong t0 = Time.GetTicksMsec();
		await WaitUntil(() => hw.LightsSettled, 6, ct);
		double took = 3.3 + (Time.GetTicksMsec() - t0) / 1000.0;
		Check("the light settles, red to green to white, in about six seconds", hw.LightsSettled && took > 5 && took < 7.5, $"{took:0.0} s");
		Screenshot("closet_lit");
		// now the door: pitch black beyond it
		await UseIt(hw.InsideDoorUse, ct);
		Check("with the light on, the door opens", hw.ClosetDoorOpen);
		await Seconds(1.6, ct);
		await Aim(hw.DoorWorld + Vector3.Up * 1.2f, ct);
		Screenshot("pitch_black_threshold");
		var sewer = StationInterior.Instance.Sewer;
		await WalkTo(hw.DoorWorld, 0.3f, ct, stopWhen: () => hw.Through, giveUp: 6f);
		await WaitUntil(() => s.Current == Checkpoint.Act16Finished && _input.Enabled, 8, ct);
		await Frames(10, ct);
		Check("through the black and out into the sewer (Act 17's save)", s.Current == Checkpoint.Act16Finished
			&& _player.GlobalPosition.DistanceTo(sewer.EntranceWorld) < 1.5f, $"{s.Current} at {_player.GlobalPosition}");
	}

	private async Task Act17Sewer(CancellationToken ct)
	{
		var sewer = StationInterior.Instance?.Sewer;
		await WaitUntil(() => sewer?.HoleUse != null, 10, ct);
		Check("the sewer exists", sewer != null);
		if (sewer == null) return;
		var s = StoryManager.Instance;
		await WaitUntil(() => _input.Enabled, 10, ct);
		await Frames(5, ct);
		Check("Act 17 starts at its save: inside the sewer door", s.Current == Checkpoint.Act16Finished
			&& _player.GlobalPosition.DistanceTo(sewer.EntranceWorld) < 2f, $"{s.Current} at {_player.GlobalPosition}");
		Screenshot("sewer_pipe");
		// down the steps into the water
		await WalkTo(sewer.AlongWorld(5f), 0.5f, ct, giveUp: 8f);
		await Seconds(1.0, ct);
		Check("ankle deep: wading", sewer.PlayerWading && _player.WadeScale < 0.75f, $"wading {sewer.PlayerWading}, scale {_player.WadeScale:0.00}");
		// how fast, in the water
		Vector3 a = _player.GlobalPosition;
		ulong t0 = Time.GetTicksMsec();
		await WalkTo(sewer.AlongWorld(12f), 0.5f, ct, giveUp: 10f);
		double secs = (Time.GetTicksMsec() - t0) / 1000.0;
		float speed = (float)(Flat(_player.GlobalPosition - a).Length() / Mathf.Max(0.01, secs));
		Check("the water drags: slower than a walk", speed < _player.WalkSpeed * 0.8f, $"{speed:0.00} m/s");
		Screenshot("sewer_wading");
		// shove against both sides of the pipe: body and eye stay inside the brick
		float worst = 0f, worstEye = 0f;
		float bodyR = _player.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is CapsuleShape3D cap ? cap.Radius : 0.3f;
		foreach (float side in new[] { 1f, -1f })
		{
			_player.CameraRig.SnapBehind(sewer.GlobalRotation.Y + Mathf.Pi);   // facing down the pipe
			double tt = 0;
			while (tt < 1.8)
			{
				_input.ScriptedMove = new Vector2(side, 0.15f);
				await Frames(1, ct);
				tt += GetProcessDeltaTime();
				worstEye = Mathf.Max(worstEye, sewer.PipeAxisDistance(_player.CameraRig.Camera.GlobalPosition));
				// the body at its widest (the capsule's straight middle), out toward the wall
				Vector3 mid = _player.GlobalPosition + Vector3.Up * 0.9f;
				worst = Mathf.Max(worst, sewer.PipeAxisDistance(mid) + bodyR);
			}
			_input.ScriptedMove = Vector2.Zero;
		}
		Check("pushed against the curved sides: nothing clips into the brick", worst < Sewer.PipeR + 0.03f && worstEye < Sewer.PipeR - 0.15f,
			$"body out to {worst:0.00} m, eye {worstEye:0.00} m from the axis (brick at {Sewer.PipeR})");
		Screenshot("sewer_side");

		Engine.TimeScale = 3.0;
		try { await WalkTo(sewer.TunnelEndWorld, 0.8f, ct, giveUp: 90f); }
		finally { Engine.TimeScale = 1.0; }
		Check("down the long pipe to the cistern", _player.GlobalPosition.DistanceTo(sewer.TunnelEndWorld) < 1.5f, $"{_player.GlobalPosition}");
		await Aim(sewer.HoleWorld + Vector3.Up * 2f, ct);
		Screenshot("the_cistern");
		await WalkTo(sewer.PlatformEdgeWorld, 0.5f, ct, giveUp: 20f);
		Check("up onto the platform", _player.GlobalPosition.DistanceTo(sewer.PlatformEdgeWorld) < 1f && !sewer.PlayerWading, $"{_player.GlobalPosition}");
		await Aim(sewer.HoleWorld, ct);
		Screenshot("the_hole");
		Check("aiming at the hole lights up only the hole, not the whole cistern", sewer.GetNode<GeometryInstance3D>("Cistern").MaterialOverlay == null && sewer.GetNode<GeometryInstance3D>("Platform").MaterialOverlay == null);
		_input.ScriptedLight = true; await Frames(3, ct); _input.ScriptedLight = false; await Frames(3, ct);
		// a look round the cistern: pillars, vaults, the water (textured, nothing clipping)
		foreach (var (look, name) in new[] { (new Vector3(5.5f, 3f, Sewer.RoomZ0 + 15f), "tour_pillar"), (new Vector3(-5.5f, 10f, Sewer.RoomZ0 + 29f), "tour_vault"),
			(new Vector3(9f, 0.1f, Sewer.HoleLocal.Z + 6f), "tour_water"), (new Vector3(Sewer.RoomX, 3f, Sewer.HoleLocal.Z), "tour_east_wall"),
			(new Vector3(5.5f, 3f, Sewer.RoomZ0 + 29f), "tour_far_pillar"), (new Vector3(-5.5f, 3f, Sewer.RoomZ0 + 29f), "tour_far_pillar_w"), (new Vector3(3f, 8f, Sewer.RoomZ0 + 38f), "tour_far_vault") })
		{
			await Aim(sewer.ToGlobal(look), ct);
			await Frames(2, ct);
			Screenshot(name);
		}
		// right at the hole's edge, in its smoke, looking across the room (the owner's view: it must not grey out)
		await Inside(sewer.ToGlobal(Sewer.HoleLocal + new Vector3(0.9f, 0.05f, -1.9f)), sewer.ToGlobal(new Vector3(3f, 3f, Sewer.RoomZ0 + 38f)), ct);
		await Seconds(0.3, ct);
		Screenshot("tour_from_the_hole");
		await Aim(sewer.ToGlobal(new Vector3(-8f, 6f, Sewer.RoomZ0 + 36f)), ct);
		await Seconds(0.3, ct);
		Screenshot("tour_from_the_hole_arches");
		await Inside(sewer.PlatformEdgeWorld, sewer.HoleWorld, ct);

		// no, the first time: whispers going round the room
		await UseIt(sewer.HoleUse, ct);
		await WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		Check("the hole asks: drop down into the hole?", ChoicePrompt.Instance is { IsOpen: true } && ChoicePrompt.Instance.Question.StartsWith("Drop down"), ChoicePrompt.Instance?.Question);
		Screenshot("drop_down_question");
		ChoicePrompt.Instance?.TestChoose(1);
		await Seconds(5.0, ct);
		Check("the first no: come and see, whispered from round the room", sewer.Noes == 1 && sewer.Whispers >= 3 && !sewer.Teal, $"{sewer.Whispers} whispers");
		// no again (a yes, then a no at "are you sure?"): the teal haze, and more whispers
		await UseIt(sewer.HoleUse, ct);
		await WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		ChoicePrompt.Instance?.TestChoose(0);
		await Seconds(0.6, ct);
		await WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		Check("a yes asks again: are you sure?", ChoicePrompt.Instance is { IsOpen: true } && ChoicePrompt.Instance.Question.StartsWith("Are you sure"), ChoicePrompt.Instance?.Question);
		ChoicePrompt.Instance?.TestChoose(1);
		int w0 = sewer.Whispers;
		await Seconds(8.0, ct);
		var atmo = StoryBeat.Atmosphere(this);
		Check("the second no: a hazy teal light, and the whispers multiply", sewer.Noes == 2 && sewer.Teal && sewer.Whispers >= w0 + 3
			&& (atmo == null || atmo.UndergroundFogColor.G > atmo.UndergroundFogColor.R * 2f), $"{sewer.Whispers - w0} more whispers, fog {atmo?.UndergroundFogColor}");
		await Aim(sewer.ToGlobal(new Vector3(-12f, 3f, Sewer.RoomZ0 + 10f)), ct);
		Screenshot("teal_haze");
		// yes, and yes
		await Aim(sewer.HoleWorld, ct);
		await UseIt(sewer.HoleUse, ct);
		await WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		ChoicePrompt.Instance?.TestChoose(0);
		await Seconds(0.6, ct);
		await WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		ChoicePrompt.Instance?.TestChoose(0);
		await WaitUntil(() => sewer.Dropped, 3, ct);
		await Seconds(1.2, ct);
		Screenshot("dropping");
		await WaitUntil(() => s.Current == Checkpoint.Act17Finished, 12, ct);
		var boss = StationInterior.Instance?.Boss;
		Check("yes and yes: down the hole, out of a ceiling, onto a catwalk (Act 18's save)", sewer.Dropped && s.Current == Checkpoint.Act17Finished
			&& boss != null && _player.GlobalPosition.DistanceTo(boss.LandingWorld) < 1.5f, $"{s.Current} at {_player.GlobalPosition}");
	}

	// ------------------------------------------------------------------ Act 18

	/// <summary>The bot's fight: round the catwalk to the lit valve, hold E on it, and get out from under
	/// anything about to come down (let go of the valve, move along the catwalk, come back). One frame of it.
	/// Returns what it did, for the log.</summary>
	private string BossStep(BossRoom boss)
	{
		var v = boss.ActiveValve;
		Vector3 pl = boss.ToLocal(_player.GlobalPosition);
		float sp = boss.RingOf(pl), per = boss.Perimeter;
		// danger: standing in (or at the edge of) a circle that's coming down
		var threats = boss.Threats.ToList();
		bool Inside(Vector3 world, float margin, float before)
		{
			foreach (var (w, left, r) in threats)
				if (left < before && Flat(w - world).Length() < r + margin) return true;
			return false;
		}
		if (Inside(_player.GlobalPosition, 0.7f, 99f))
		{
			// plan: the nearest spot along the catwalk that's clear when we get there, by a path that
			// never crosses a circle as it lands
			const float speed = 4.6f;
			float best = float.MaxValue, bestOff = 0f;
			for (float off = -11f; off <= 11.01f; off += 0.5f)
			{
				if (Mathf.Abs(off) < 0.1f) continue;
				Vector3 dest = boss.ToGlobal(boss.RingLocal(sp + off));
				if (Inside(dest, 0.8f, 99f)) continue;
				bool pathOk = true;
				for (float k = 0.5f; k < Mathf.Abs(off); k += 0.5f)
				{
					float tAt = k / speed;
					Vector3 pt = boss.ToGlobal(boss.RingLocal(sp + Mathf.Sign(off) * k));
					foreach (var (w, left, r) in threats)
						if (Mathf.Abs(left - tAt) < 0.45f && Flat(w - pt).Length() < r + 0.5f) pathOk = false;
					if (!pathOk) break;
				}
				if (pathOk && Mathf.Abs(off) < best) { best = Mathf.Abs(off); bestOff = off; }
			}
			if (best == float.MaxValue) bestOff = 6f;
			// in short hops along the catwalk (never a straight line across a corner)
			Vector3 target = boss.ToGlobal(boss.RingLocal(sp + Mathf.Sign(bestOff) * Mathf.Min(Mathf.Abs(bestOff) + 0.8f, 2.2f)));
			_input.ScriptedInteract = false;
			Steer(target);
			_input.ScriptedMove = new Vector2(0, 1);
			_input.ScriptedRun = true;
			return "evade";
		}
		_input.ScriptedRun = false;
		if (v == null) { _input.ScriptedMove = Vector2.Zero; _input.ScriptedInteract = false; return "wait"; }
		Vector3 stand = v.StandWorld;
		float dist = Flat(stand - _player.GlobalPosition).Length();
		if (dist > 0.6f)
		{
			// round the ring toward it (the shorter way), not across the pit
			float sg = boss.RingOf(boss.ToLocal(stand));
			float delta = Mathf.PosMod(sg - sp + per * 0.5f, per) - per * 0.5f;
			Vector3 target = Mathf.Abs(delta) < 2.5f ? stand : boss.ToGlobal(boss.RingLocal(sp + Mathf.Sign(delta) * 2.5f));
			// don't walk into somewhere about to be hit: wait short of it
			foreach (var (w, left, r) in threats)
				if (Flat(w - target).Length() < r + 1.2f) { _input.ScriptedMove = Vector2.Zero; _input.ScriptedInteract = false; return "hold back"; }
			_input.ScriptedInteract = false;
			Steer(target);
			_input.ScriptedMove = new Vector2(0, 1);
			_input.ScriptedRun = Mathf.Abs(delta) > 8f;
			return "walk";
		}
		// at it: face the wheel and turn
		_input.ScriptedMove = Vector2.Zero;
		Vector3 to = v.WheelWorld - _player.CameraRig.Camera.GlobalPosition;
		_player.CameraRig.SnapBehind(Mathf.Atan2(-to.X, -to.Z));
		_player.CameraRig.SetPitch(Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()));
		_input.ScriptedInteract = true;
		return "turn";
	}

	private async Task Act18Boss(CancellationToken ct)
	{
		var boss = StationInterior.Instance?.Boss;
		await WaitUntil(() => boss?.Beast != null, 10, ct);
		Check("the boss room exists", boss != null);
		if (boss == null) return;
		var s = StoryManager.Instance;
		Check("Act 18 starts at its save: on the catwalk", s.Current == Checkpoint.Act17Finished && _player.GlobalPosition.DistanceTo(boss.LandingWorld) < 3f, $"{s.Current} at {_player.GlobalPosition}");
		await WaitUntil(() => boss.State == BossRoom.Phase.Fight, 40, ct);
		Check("the fight begins: the spotlight picks out a valve", boss.State == BossRoom.Phase.Fight && boss.ActiveValve != null, $"{boss.State}");
		bool orderOk = boss.Order.Count == BossRoom.ValvesToTurn && boss.Order.Distinct().Count() == BossRoom.ValvesToTurn;
		for (int i = 1; i < boss.Order.Count; i++)
		{
			int d = Mathf.Abs(boss.Order[i] - boss.Order[i - 1]);
			if (Mathf.Min(d, BossRoom.ValveCount - d) < 4) orderOk = false;
		}
		Check("the valves come in a random, back-and-forth order (never a neighbour next)", orderOk, string.Join(",", boss.Order.Select(i => i + 1)));

		if (!_s.Act18DeathDone)
		{
			Check("at the start the monster is healthy and the blood about five feet under the catwalk", boss.Beast.Rot < 0.01f && boss.BloodY > -2f, $"rot {boss.Beast.Rot:0.00}, blood {boss.BloodY:0.0}");
			await Aim(boss.ToGlobal(new Vector3(0, boss.BloodY, 0)), ct);
			Screenshot("the_blood_pit");
			await Aim(boss.ActiveValve.WheelWorld, ct);
			Screenshot("teal_spotlight_on_a_valve");
			// the first time: stand still under a slam
			_s.Act18DeathDone = true;
			boss.TestSlamAt(_player.GlobalPosition, 1.4f);
			await Seconds(0.9, ct);
			Screenshot("a_limb_rears_over_you");
			await WaitUntil(() => PlayerDeath.Dying, 5, ct);
			Check("stood under a slam: crushed", PlayerDeath.Dying);
			await WaitUntil(() => false, 30, ct);
			Check("crushed: the checkpoint reloaded", false, "no reload");
			return;
		}
		Check("after being crushed: back on the catwalk at Act 18's start, the fight starting over", PlayerDeath.Deaths >= 1 && boss.ValvesTurned == 0);
		if (++_s.Act18Attempts > 5) { Check("the fight", "won within five tries", false, "crushed every time"); return; }

		// the fight, played
		ulong t0 = Time.GetTicksMsec();
		Engine.TimeScale = 2.0;
		var shots = new HashSet<int>();
		string last = "";
		int evades = 0;
		try
		{
			while (boss.ValvesTurned < BossRoom.ValvesToTurn && !PlayerDeath.Dying)
			{
				ct.ThrowIfCancellationRequested();
				string what = _input.Enabled ? BossStep(boss) : "cutscene";
				if (what == "evade" && last != "evade") evades++;
				last = what;
				await Frames(1, ct);
				if ((Time.GetTicksMsec() - t0) / 1000.0 * Engine.TimeScale > 20 * 60) { Check("the fight", "finished in time", false, $"{boss.ValvesTurned} valves"); break; }
				foreach (int at in new[] { 1, 4, 7, 9 })
					if (boss.ValvesTurned == at && !shots.Contains(at) && _input.Enabled)
					{
						shots.Add(at);
						Engine.TimeScale = 1.0;
						_input.ScriptedMove = Vector2.Zero; _input.ScriptedInteract = false;
						// look down through the grating, over the rail, at it
						await Aim(boss.Beast.GlobalPosition + Vector3.Up * 12f, ct);
						await Frames(3, ct);
						Screenshot($"the_monster_rotting_{at}_of_10");
						Engine.TimeScale = 2.0;
					}
			}
		}
		finally { Engine.TimeScale = 1.0; _input.ScriptedMove = Vector2.Zero; _input.ScriptedInteract = false; _input.ScriptedRun = false; }
		if (PlayerDeath.Dying && _s.Act18Attempts < 5)
		{
			// crushed partway (a bot's reflexes): it goes round again, like a player would, up to three tries
			GD.Print($"[storytest] Act 18: crushed on try {_s.Act18Attempts} at valve {boss.ValvesTurned} - trying again");
			await WaitUntil(() => false, 30, ct);
			return;
		}
		Check("all ten valves turned, never crushed", boss.ValvesTurned == BossRoom.ValvesToTurn && !PlayerDeath.Dying,
			$"{boss.ValvesTurned} valves, {boss.Slams} slams, {evades} dodged, {boss.FightSeconds / 60.0:0.0} min of fighting");
		Check("with every valve it rots, fully gone at the end, and the blood drains", boss.Beast.Rot > 0.99f, $"rot {boss.Beast.Rot:0.00}");
		await WaitUntil(() => boss.Beast.EyesBurst > 10, 20, ct);
		Screenshot("its_eyes_bursting");
		await WaitUntil(() => boss.DoorOpen, 45, ct);
		Check("the pit is empty", boss.BloodY <= BossRoom.PitFloor + 0.1f, $"blood {boss.BloodY:0.0}");
		Check("every eye burst, one after another, and it died", boss.Beast.Dead && boss.Beast.EyesBurst == boss.Beast.EyeCount, $"{boss.Beast.EyesBurst} of {boss.Beast.EyeCount}");
		Check("the door on the far side opens", boss.DoorOpen);
		await Aim(boss.Beast.GlobalPosition + Vector3.Up * 8f, ct);
		Screenshot("dead_in_the_empty_pit");
		await WaitUntil(() => _input.Enabled, 10, ct);

		// round to the door, and into the lit room
		Vector3 doorRing = boss.ToGlobal(boss.RingLocal(boss.RingOf(boss.ToLocal(boss.DoorWorld))));
		for (int i = 0; i < 40 && Flat(_player.GlobalPosition - doorRing).Length() > 1.5f; i++)
		{
			float sp = boss.RingOf(boss.ToLocal(_player.GlobalPosition)), sg = boss.RingOf(boss.ToLocal(doorRing)), per = boss.Perimeter;
			float delta = Mathf.PosMod(sg - sp + per * 0.5f, per) - per * 0.5f;
			await WalkTo(Mathf.Abs(delta) < 4f ? doorRing : boss.ToGlobal(boss.RingLocal(sp + Mathf.Sign(delta) * 4f)), 0.8f, ct, giveUp: 6f);
		}
		await Aim(boss.TidyWorld + Vector3.Up * 1.2f, ct);
		Screenshot("the_lit_room_beyond");
		await WalkTo(boss.DoorWorld, 0.5f, ct, giveUp: 6f);
		await WalkTo(boss.TidyWorld, 0.6f, ct, stopWhen: () => boss.Finished, giveUp: 8f);
		await WaitUntil(() => s.Current == Checkpoint.Act18Finished, 5, ct);
		Check("into the clean, lit room: Act 18 done (Act 19's save)", s.Current == Checkpoint.Act18Finished, $"{s.Current}");
		Screenshot("tidy_room");
		Check("the camera is still with you at the end of Act 18", _inv.HasCamera);
	}

	private async Task Act19Library(CancellationToken ct)
	{
		var lib = StationInterior.Instance?.Boss?.Library;
		await WaitUntil(() => lib?.Puzzle != null && lib.SheetUse != null && lib.Round != null, 10, ct);
		Check("the library is there, through the pit's door", lib?.Puzzle != null);
		if (lib?.Puzzle == null) return;
		var s = StoryManager.Instance;
		Check("Act 19 starts at its save, in the library", s.Current == Checkpoint.Act18Finished && lib.PlayerInside(_player.GlobalPosition), $"{s.Current} at {_player.GlobalPosition}");
		if (s.Current != Checkpoint.Act18Finished) return;   // Act 18 wasn't won: nothing here to test
		await WaitUntil(() => _input.Enabled, 10, ct);
		await Seconds(0.5, ct);
		await Aim(lib.ToGlobal(new Vector3(0, 1.5f, Library.Depth)), ct);
		Screenshot("library");
		await Aim(lib.ToGlobal(new Vector3(Library.HalfW, 1.0f, 6.5f)), ct);
		Screenshot("library_fireplace");
		// the book that sticks out, before there's a bookmark
		await WalkTo(lib.BookcaseFrontWorld, 0.6f, ct, giveUp: 10f);
		await Aim(lib.BookUse.GlobalPosition, ct);
		Check("one book on the back wall sticks out (nothing to do with it yet)", _player.Interaction?.PromptText == "One of the books sticks out." && !lib.BookcaseOpen, $"'{_player.Interaction?.PromptText}'");
		Screenshot("the_jutting_book");
		// the sheet
		Check("a sheet covers something square on a side table", !lib.SheetOff && !lib.BoxUse.Enabled);
		await WalkTo(lib.TableWorld, 0.6f, ct, giveUp: 10f);
		await Aim(lib.SheetUse.GlobalPosition, ct);
		Screenshot("the_sheet");
		await UseIt(lib.SheetUse, ct);
		await Seconds(0.5, ct);
		Screenshot("the_sheet_coming_off");
		await WaitUntil(() => lib.BoxUse.Enabled, 8, ct);
		Check("pulled off: a bamboo puzzle box and five loose pieces", lib.SheetOff && lib.BoxUse.Enabled && lib.Puzzle.Pieces.Count == 5 && lib.Puzzle.Pieces.TrueForAll(p => p.At == null));
		await WaitUntil(() => _input.Enabled, 5, ct);
		Screenshot("puzzle_box_revealed");
		// the puzzle
		await UseIt(lib.BoxUse, ct);
		var o = PuzzleOverlay.Instance;
		await WaitUntil(() => o is { IsOpen: true }, 3, ct);
		Check("working the box opens the close-up", o is { IsOpen: true });
		if (o is not { IsOpen: true }) return;
		await Frames(5, ct);
		Screenshot("puzzle_closeup");
		var plan = lib.Puzzle.Solve();
		Check("the box can be filled (a solution exists)", plan.Count == 5, $"{plan.Count} pieces placed by the solver");
		int longIdx = lib.Puzzle.Pieces.FindIndex(p => p.Name == "long");
		o.TestSelect(longIdx);
		o.TestHold(new Vector2I(2, 0), 0);   // three across from the third column: over the edge
		await Frames(3, ct);
		Screenshot("puzzle_holding_a_piece");
		int bumps = o.Bumps;
		bool wrong = o.SetIn();
		Check("a piece that doesn't fit knocks against the rim and stays in hand", !wrong && o.Bumps == bumps + 1 && o.Holding && lib.Puzzle.Pieces[longIdx].At == null);
		// and a key press moves the held piece over the box
		Vector2I before = o.Cursor;
		Input.ParseInputEvent(new InputEventAction { Action = "move_left", Pressed = true });
		await Frames(2, ct);
		Input.ParseInputEvent(new InputEventAction { Action = "move_left", Pressed = false });
		await Frames(2, ct);
		Check("A moves the held piece one cell left", o.Cursor == before + new Vector2I(-1, 0), $"{before} -> {o.Cursor}");
		for (int i = 0; i < plan.Count; i++)
		{
			var (p, rot, at) = plan[i];
			o.TestSelect(lib.Puzzle.Pieces.IndexOf(p));
			o.TestHold(at, rot);
			await Frames(2, ct);
			bool ok = o.SetIn();
			if (!ok) { Check("placing the solution", $"piece {p.Name} at {at} turned {rot}", false, "it didn't fit"); break; }
			if (i == 2) { await Frames(2, ct); Screenshot("puzzle_half_done"); }
			await Seconds(0.2, ct);
		}
		Check("every piece in: the box is solved, the close-up closes", lib.Puzzle.IsSolved && !o.IsOpen);
		await Seconds(1.6, ct);
		Screenshot("the_box_glowing");
		await Seconds(1.6, ct);
		Screenshot("the_box_dissolving");
		await WaitUntil(() => lib.Bookmark != null, 10, ct);
		Check("it glows once, softly, and goes out of the world, leaving a bookmark", lib.PuzzleSolved && !lib.Puzzle.Visible && lib.Bookmark != null);
		await WaitUntil(() => _input.Enabled, 10, ct);
		Screenshot("the_bookmark");
		await UseIt(lib.Bookmark, ct);
		Check("the bookmark taken", _inv.HasTool(ToolKind.Bookmark));
		// the bookcase
		await WalkTo(lib.BookcaseFrontWorld, 0.6f, ct, giveUp: 10f);
		await Aim(lib.BookUse.GlobalPosition, ct);
		Check("with the bookmark: slide it in", _player.Interaction?.PromptText == "Slide the bookmark in", $"'{_player.Interaction?.PromptText}'");
		await Press(ct);
		await WaitUntil(() => lib.BookcaseOpen, 3, ct);
		await Seconds(1.0, ct);
		Screenshot("the_bookmark_slides_in");
		await WaitUntil(() => _input.Enabled, 15, ct);
		Check("the bookmark goes in, the book slides home, the bookcase swings open", lib.BookcaseOpen && !_inv.HasTool(ToolKind.Bookmark));
		Screenshot("the_bookcase_open");
		// through the passage, into the round room
		await WalkTo(lib.PassageWorld, 0.5f, ct, giveUp: 10f);
		Screenshot("the_passage");
		await WalkTo(lib.Round.EntryWorld, 0.6f, ct, stopWhen: () => s.Current == Checkpoint.Act19Finished, giveUp: 10f);
		await WaitUntil(() => s.Current == Checkpoint.Act19Finished, 5, ct);
		Check("through the bookcase into the round room: Act 19 done (Act 20's save)", s.Current == Checkpoint.Act19Finished, $"{s.Current} at {_player.GlobalPosition}");
		Check("the camera is still with you", _inv.HasCamera);
	}

	private async Task Act20Round(CancellationToken ct)
	{
		var rr = StationInterior.Instance?.Boss?.Library?.Round;
		await WaitUntil(() => rr?.Dais != null && rr.WebUse != null, 10, ct);
		Check("the round room is there", rr?.Dais != null);
		if (rr?.Dais == null) return;
		var s = StoryManager.Instance;
		Check("Act 20 starts at its save, inside the round room", s.Current == Checkpoint.Act19Finished && _player.GlobalPosition.DistanceTo(rr.EntryWorld) < 3f, $"{s.Current} at {_player.GlobalPosition}");
		if (s.Current != Checkpoint.Act19Finished) return;
		await Aim(rr.CentreWorld + Vector3.Up * 1f, ct);
		Screenshot("round_room");
		await Aim(rr.ToGlobal(new Vector3(0, 14f, RoundRoom.Radius)), ct);
		Screenshot("the_bricked_windows");
		await Aim(rr.ToGlobal(new Vector3(0, RoundRoom.Height, 0.5f)), ct);
		Screenshot("the_high_ceiling");
		if (!_inv.HasTool(ToolKind.Lighter))
		{
			rr.EnsureLighter(_player);
			await Frames(3, ct);
			await UseIt(rr.GetNodeOrNull<Pickup>("SpareLighter"), ct);
		}
		Check("the lighter is in hand", _inv.HasTool(ToolKind.Lighter));
		// the web keeps you off the dais
		await WalkTo(rr.CentreWorld, 0.5f, ct, giveUp: 4f);
		Check("the web won't let you onto the dais", Flat(_player.GlobalPosition - rr.CentreWorld).Length() > RoundRoom.DaisFoot - 0.4f, $"{Flat(_player.GlobalPosition - rr.CentreWorld).Length():0.00} m from the middle");
		await WalkTo(rr.DaisEdgeWorld, 0.5f, ct, giveUp: 8f);
		await Aim(rr.CentreWorld + Vector3.Up * 0.6f, ct);
		Check("the lighter: burn the webs", _player.Interaction?.PromptText == "Burn the webs", $"'{_player.Interaction?.PromptText}'");
		Screenshot("the_webbed_dais");
		await Press(ct);
		await WaitUntil(() => rr.WebsBurned, 3, ct);
		await Seconds(2.0, ct);
		Screenshot("the_webs_burning");
		await WaitUntil(() => _input.Enabled, 10, ct);
		Check("the webs burn away", rr.WebsBurned && s.HasFlag(StoryManager.Flag.RoundRoomWebBurned));
		// into the middle: twenty seconds up
		await WalkTo(rr.CentreWorld, 0.3f, ct, stopWhen: () => rr.Rising, giveUp: 8f);
		await WaitUntil(() => rr.Rising, 3, ct);
		Check("stepping into the middle of the dais starts it rising, the player held", rr.Rising && !_input.Enabled);
		ulong t0 = Time.GetTicksMsec();
		await Seconds(6, ct);
		Screenshot("rising_past_the_windows");
		await Seconds(6, ct);
		Screenshot("rising_into_the_shaft");
		await WaitUntil(() => rr.Arrived, 20, ct);
		double took = (Time.GetTicksMsec() - t0) / 1000.0;
		Check("a twenty-second ascent", rr.Arrived && took > 17 && took < 24, $"{took:0.0} s");
		Check("up through the ceiling into the room above: Act 20 done", s.Current == Checkpoint.Act20Finished && _player.GlobalPosition.Y > rr.ToGlobal(new Vector3(0, RoundRoom.Rise, 0)).Y, $"{s.Current} at {_player.GlobalPosition}");
		await WaitUntil(() => _input.Enabled, 3, ct);
		await Frames(10, ct);
		Check("standing on the dais at the top", _player.IsOnFloor());
		Screenshot("the_room_above");
		await WalkTo(rr.TopWorld, 0.5f, ct, giveUp: 4f);
		Check("off the dais onto the room's floor", _player.IsOnFloor() && Flat(_player.GlobalPosition - rr.TopWorld).Length() < 1f, $"{_player.GlobalPosition}");
		Check("the camera is still with you at the end of the demo", _inv.HasCamera);
	}

	/// <summary>Teleport inside the station (no terrain snap: the forest's ground means nothing out here).</summary>
	private async Task Inside(Vector3 at, Vector3 faceToward, CancellationToken ct)
	{
		_s.Jumped += Flat(at).DistanceTo(Flat(_player.GlobalPosition));
		var d = Flat(faceToward - at);
		_player.Teleport(at + Vector3.Up * 0.08f, d.LengthSquared() > 0.01f ? Mathf.Atan2(-d.X, -d.Z) : _player.CameraRig.Yaw);
		_lastPos = _player.GlobalPosition;
		await Frames(4, ct);
	}

	/// <summary>Spams the paddle keys in alternation (A, D, A, D...) until <paramref name="done"/>
	/// is true or <paramref name="giveUp"/> seconds pass, mirroring <see cref="PushFor"/> but
	/// alternating sides instead of holding one.</summary>
	private async Task PaddleUntil(Func<bool> done, CancellationToken ct, double giveUp)
	{
		double t = 0, sideTimer = 0; bool left = true;
		try
		{
			while (!done())
			{
				ct.ThrowIfCancellationRequested();
				if (_input.Enabled)
				{
					sideTimer -= GetProcessDeltaTime();
					if (sideTimer <= 0) { left = !left; sideTimer = 0.15; }
					_input.ScriptedMove = new Vector2(left ? -1f : 1f, 0f);
				}
				await Frames(1, ct);
				t += GetProcessDeltaTime();
				if (t > giveUp) { Check("paddling", "made it before giving up", false, $"stuck after {giveUp:0}s"); return; }
			}
		}
		finally { if (_input != null) _input.ScriptedMove = Vector2.Zero; }
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
		if (focus == null)
			GD.Print($"[usedbg] {obj.Name}: player {_player.GlobalPosition} aim {aim} dist {_player.GlobalPosition.DistanceTo(aim):0.00} enabled {_input.Enabled} modal {_input.Modal} use {use?.Enabled} visible {use?.IsVisibleInTree()} focusHeld {_input.Focus} camera {_player.CameraRig.Camera.GlobalPosition} fwd {-_player.CameraRig.Camera.GlobalBasis.Z}");
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

	/// <summary>Comes up the trail to R.H.'s note <paramref name="i"/> on its tree, reads the digit with the lantern on
	/// from a couple of metres (no focused beam needed), and checks the saved flag and the corner tracker.</summary>
	/// <summary>One press and release of an input action through the real input pipeline (what a modal overlay's _Input sees).</summary>
	private async Task Key(string action, CancellationToken ct)
	{
		Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
		await Frames(2, ct);
		Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
		await Frames(2, ct);
	}

	private async Task ReadTreeNote(SurveyLot lot, int i, CancellationToken ct)
	{
		var note = lot.Notes[i];
		var terrain = GroundSnap.FindTerrain(this);
		Vector3 from = note.GlobalPosition + Vector3.Forward * 6f;
		if (terrain != null) { terrain.TrailDistance(note.GlobalPosition.X, note.GlobalPosition.Z, out float s); from = terrain.TrailPoint(s - 6f, out _); }
		await Teleport(from, note.PlateWorld, ct);
		Check($"note {note.Label}'s tree stands beside the path", note.GlobalPosition.DistanceTo(from) < 9f, $"{note.GlobalPosition.DistanceTo(from):0.0} m from the path point");
		await Aim(note.PlateWorld, ct);
		await Seconds(0.4, ct);
		Check($"note {note.Label} is not read from 6 m", !note.Revealed, $"glow {note.Glow:0.00}");
		// Walk in to it: within a few metres, the sheet in view is enough (no lantern needed).
		Vector3 near = note.PlateWorld + (from - note.PlateWorld).Normalized() * 2.2f;
		await WalkTo(near, 0.8f, ct, giveUp: 6f);
		await Aim(note.PlateWorld, ct);
		await WaitUntil(() => note.Revealed, 4, ct);
		Check($"note {note.Label} reads up close, lantern or not", note.Revealed, $"glow {note.Glow:0.00}");
		if (i == 0) Screenshot("tree_note");
		await Seconds(0.6, ct);
		Check($"digit {i + 1} is saved and shown in the tracker", StoryManager.Instance.HasFlag(StoryManager.Flag.CodeDigit(i + 1))
			&& (CodeLockOverlay.Instance?.TrackerText ?? "").Contains(note.Digit.ToString()), $"tracker '{CodeLockOverlay.Instance?.TrackerText}'");
	}

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
