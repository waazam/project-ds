using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// `-- --continue-test`: the Continue round trip, for every checkpoint.
///
/// For each scenario it writes a save (checkpoint + the flags and inventory a real
/// playthrough has at that point, with a deliberately bad raw position inside the cabin),
/// Continues from it (a real scene reload through StoryManager), then checks the world:
/// story state loaded, player placed somewhere safe (not in the cabin, on the ground),
/// control handed back (no softlock), and every restored system in the state the story
/// implies (door, storm, mood, fire, clearing stairs, tall stairs, beat guards, inventory,
/// compass). Progress survives the reloads in static fields; the report and screenshots
/// go to test-output/systems/. Exits non-zero on any failure.
/// </summary>
public partial class ContinueRoundTripTest : Node
{
	private const string OutDir = "res://test-output/systems";

	private record Scenario(string Name, Checkpoint Cp, string[] Flags, string Inventory);

	private static readonly string[] F2 = { StoryManager.Flag.StairsClimbed };
	private static readonly string[] F5 = F2.Concat(new[] { StoryManager.Flag.StormStarted, StoryManager.Flag.GiantEventDone, StoryManager.Flag.CabinDoorOpen, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, StoryManager.Flag.PickupTakenAxe }).ToArray();
	private static readonly string[] F6 = F5.Concat(new[] { StoryManager.Flag.NewelPostTaken, StoryManager.Flag.DawnBroke }).ToArray();
	private static readonly string[] F6Voice = F6.Concat(new[] { StoryManager.Flag.ClearingVoiceHeard }).ToArray();
	private static readonly string[] F7 = F6Voice.Concat(new[] { StoryManager.Flag.Act6NightFell }).ToArray();
	private static readonly string[] F10 = F7.Concat(new[] { StoryManager.Flag.CrtPuzzleDone, StoryManager.Flag.BunkerMazeEntered, StoryManager.Flag.BunkerMazeExited }).ToArray();
	private static readonly string[] F11 = F10.Concat(new[] { StoryManager.Flag.Act11DialogueDone }).ToArray();

	private static readonly Scenario[] Scenarios =
	{
		new("act1_start", Checkpoint.Act1Start, new string[0], "camera;tool=None"),
		new("act2_stairs_climbed", Checkpoint.Act2StairsClimbed, F2, ";tool=None"),
		new("act3_door_boarded", Checkpoint.Act3DoorBoarded, F2, ";tool=None"),
		new("act3_storm", Checkpoint.Act3DoorBoarded, F2.Concat(new[] { StoryManager.Flag.StormStarted, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass }).ToArray(), "lantern,compass;tool=None"),
		new("act5_cabin_entered", Checkpoint.Act5CabinEntered, F5, "lantern,compass;tool=None"),
		new("act5_post_taken", Checkpoint.Act5CabinEntered, F5.Append(StoryManager.Flag.NewelPostTaken).ToArray(), "lantern,compass,newel_post;tool=None"),
		new("act6_bridge_crossed", Checkpoint.Act6BridgeCrossed, F6, "lantern,compass,newel_post;tool=None"),
		new("act6_voice_heard", Checkpoint.Act6BridgeCrossed, F6Voice, "lantern,compass;tool=None"),
		new("act6_extended_climb", Checkpoint.Act6BridgeCrossed, F6Voice.Concat(new[] { StoryManager.Flag.Act6ExtendedClimb, StoryManager.Flag.Act6NightFell }).ToArray(), "lantern,compass;tool=None"),
		new("act7_cabin_burning", Checkpoint.Act7CabinBurning, F7, "lantern,compass;tool=None"),
		new("act8_bunker_entered", Checkpoint.Act8BunkerEntered, F7, "lantern,compass;tool=None"),
		new("act10_walkie_found", Checkpoint.Act10WalkieFound, F10, "lantern,compass;tool=None"),
		new("act10_after_radio", Checkpoint.Act10WalkieFound, F11, "lantern,compass,radio;tool=None"),
		new("act11_giant_encounter", Checkpoint.Act11GiantEncounter, F11, "lantern,compass,radio;tool=None"),
	};

	// Survive the scene reloads between scenarios.
	private static int _index = -1;
	private static readonly List<(string scenario, string name, bool ok, string detail)> _checks = new();
	private static double _startMsec;

	private PlayerController _player;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutDir));
		using (FileAccess.Open("res://test-output/.gdignore", FileAccess.ModeFlags.Write)) { }
		Cutscene.Run(this, async _ =>
		{
			try { await Run(); }
			catch (System.Exception e) when (e is not System.OperationCanceledException)
			{
				Check("round trip ran without an exception", false, e.ToString());
				Finish();
			}
		});
	}

	private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	private async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s, true, true), SceneTreeTimer.SignalName.Timeout);

	private void Check(string name, bool ok, string detail = "")
	{
		string sc = _index >= 0 && _index < Scenarios.Length ? Scenarios[_index].Name : "setup";
		_checks.Add((sc, name, ok, detail));
		GD.Print($"[continue-test] {(ok ? "PASS" : "FAIL")} {sc}: {name} {detail}");
	}

	private async Task Run()
	{
		var flow = GetParent<GameFlow>();
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		while (!flow.Started) await Frame();

		if (_index < 0 || StoryManager.Instance is not { LoadedFromSave: true })
		{
			// First pass: a fresh game just to have the level; start the round trips.
			_index = 0;
			_startMsec = Time.GetTicksMsec();
			ContinueInto(_index);
			return;
		}

		await Verify(Scenarios[_index]);
		_index++;
		if (_index < Scenarios.Length) { ContinueInto(_index); return; }
		Finish();
	}

	/// <summary>Writes the scenario's save (with an unsafe raw position) and Continues from it.</summary>
	private void ContinueInto(int i)
	{
		var s = Scenarios[i];
		// The raw saved position is deliberately inside the cabin: Continue must not use it.
		Vector3 bad = (GetTree().GetFirstNodeInGroup("cabin") as Node3D)?.GlobalPosition + Vector3.Up * 0.4f ?? Vector3.Zero;
		SaveSystem.Save(new SaveData
		{
			Checkpoint = s.Cp,
			PosX = bad.X, PosY = bad.Y, PosZ = bad.Z, Yaw = 0f,
			Flags = s.Flags,
			Inventory = s.Inventory,
		});
		GD.Print($"[continue-test] --- continuing into {s.Name} ({s.Cp})");
		if (!StoryManager.Instance.ContinueGame()) { Check("save loads back", false, "ContinueGame returned false"); Finish(); }
	}

	private async Task Verify(Scenario sc)
	{
		var story = StoryManager.Instance;
		var input = _player.PlayerInput;
		input.Scripted = true;

		Check("checkpoint restored", story.Current == sc.Cp, $"{story.Current}");
		string level = GetTree().CurrentScene?.SceneFilePath ?? "";
		bool hollow = level == StoryManager.HollowScene;
		Check("Continue loads the checkpoint's level", level == StoryManager.LevelFor(sc.Cp), level);
		var missing = sc.Flags.Where(f => !story.HasFlag(f)).ToArray();
		Check("flags restored", missing.Length == 0, missing.Length == 0 ? $"{sc.Flags.Length} flags" : $"missing {string.Join(",", missing)}");

		if (sc.Cp == Checkpoint.Act10WalkieFound && !story.Act11DialogueDone)
		{
			// The radio exchange replays (fade, carry outside, the two lines): let it finish first.
			double w = 0;
			while (!story.Act11DialogueDone && w < 30) { await Wait(0.25); w += 0.25; }
			Check("radio exchange replays and completes", story.Act11DialogueDone, $"after {w:0.0}s");
		}

		// Control must come back (a restored cutscene, e.g. Act 11's radio, may hold it for a while).
		double waited = 0;
		while (!input.Enabled && waited < 25) { await Wait(0.25); waited += 0.25; }
		Check("control handed back (no softlock)", input.Enabled, $"after {waited:0.0}s");
		await Wait(1.0);

		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		if (cabin != null)
		{
			Vector3 l = cabin.ToLocal(_player.GlobalPosition);
			bool inside = Mathf.Abs(l.X) < cabin.Width * 0.5f && Mathf.Abs(l.Z) < cabin.Depth * 0.5f;
			Check("not respawned inside a sealed cabin", !inside || cabin.IsOpen, $"player {_player.GlobalPosition}, inside {inside}, door open {cabin.IsOpen}");
		}
		var terrain = GroundSnap.FindTerrain(this);
		float ground = terrain?.HeightAt(_player.GlobalPosition.X, _player.GlobalPosition.Z) ?? _player.GlobalPosition.Y;
		Check("standing on something", _player.IsOnFloor() && _player.GlobalPosition.Y > ground - 1.5f,
			$"on floor {_player.IsOnFloor()}, y {_player.GlobalPosition.Y:0.0} vs ground {ground:0.0}");

		// Can actually move: forward, or back if forward is blocked.
		Vector3 before = _player.GlobalPosition;
		await Drive(input, new Vector2(0, 1), 0.6);
		if (_player.GlobalPosition.DistanceTo(before) < 0.2f) await Drive(input, new Vector2(0, -1), 0.6);
		Check("player can move", _player.GlobalPosition.DistanceTo(before) > 0.2f, $"{_player.GlobalPosition.DistanceTo(before):0.00} m");

		// Restored systems.
		if (cabin != null)
		{
			bool wantOpen = StoryBeat.CabinDoorOpen(story);
			bool wantBoarded = !wantOpen && story.StairsClimbed;
			Check("cabin door state", cabin.IsOpen == wantOpen && cabin.DoorBoarded == wantBoarded,
				$"open {cabin.IsOpen} (want {wantOpen}), boarded {cabin.DoorBoarded} (want {wantBoarded})");
		}
		bool wantStorm = StoryBeat.StormShouldRage(story);
		Check("storm state", StormController.Instance?.Active == wantStorm, $"active {StormController.Instance?.Active} (want {wantStorm})");
		var atmo = StoryBeat.Atmosphere(this);
		var wantMood = StoryBeat.ExpectedMood(story);
		Check("lighting mood", atmo?.CurrentMood == wantMood, $"{atmo?.CurrentMood} (want {wantMood})");
		if (!hollow)
		{
			// The trailhead holds Act 1 only: none of the Hollow's beats exist here.
			Check("no compass on the trail", story.ObjectivePosition == null, $"{story.ObjectivePosition}");
			input.ScriptedMove = Vector2.Zero;
			Screenshot(sc.Name);
			return;
		}
		var fire = FindFirst<CabinFireEvent>();
		bool wantFire = story.Current >= Checkpoint.Act7CabinBurning;
		Check("cabin fire state", fire != null && fire.Burning == wantFire, $"burning {fire?.Burning} (want {wantFire})");

		var act6 = FindFirst<Act6ClearingEvent>();
		int wantMinis = StoryBeat.Act6Revealed(story) && !story.HasFlag(StoryManager.Flag.Act6ExtendedClimb) ? 15 : 0;
		Check("clearing mini stairs", act6 != null && act6.MiniStairCount == wantMinis, $"{act6?.MiniStairCount} (want {wantMinis})");
		Check("no leftover clearing spotlight", act6 is { SpotlightActive: false }, "");

		var act11 = FindFirst<Act11Ending>();
		Check("Act 11 stairs height", act11 != null && act11.StairsTall == story.Act11DialogueDone, $"tall {act11?.StairsTall} (want {story.Act11DialogueDone})");
		bool wantClimbTrigger = story.Act11DialogueDone && story.Current < Checkpoint.Act11GiantEncounter;
		Check("Act 11 climb trigger", (act11?.ClimbTriggerWorld != null) == wantClimbTrigger, $"present {act11?.ClimbTriggerWorld != null} (want {wantClimbTrigger})");

		// One-shot beats know they already happened.
		CheckFired<FirstClimbEvent>(story.Current >= Checkpoint.Act2StairsClimbed);
		// The boarded-door caption is a once-only line now (no checkpoint): done once seen, or once the door is open.
		CheckFired<CabinReturnEvent>(story.HasFlag(CabinReturnEvent.SeenFlag) || StoryBeat.CabinDoorOpen(story));
		CheckFired<FriendReveal>(story.Current >= Checkpoint.Act5CabinEntered);
		CheckFired<CabinExitLine>(story.HasFlag(StoryManager.Flag.DawnBroke));
		CheckFired<BridgeCrossEvent>(story.Current >= Checkpoint.Act6BridgeCrossed);

		// World pickups already taken in the saved story stay gone.
		var reappeared = new List<string>();
		foreach (var n in GetTree().CurrentScene.FindChildren("*", "", true, false))
			if (n is Pickup p && story.HasFlag(p.TakenFlag) && !p.Taken) reappeared.Add(p.Name);
		Check("taken pickups stay taken", reappeared.Count == 0, reappeared.Count == 0 ? "" : string.Join(",", reappeared));

		var inv = _player.GetNodeOrNull<PlayerInventory>("Inventory");
		Check("inventory restored", inv != null && GearOf(inv.Serialize()) == GearOf(sc.Inventory) || (sc.Cp == Checkpoint.Act10WalkieFound && inv?.HasRadio == true),
			$"{inv?.Serialize()} (saved {sc.Inventory})");

		bool wantObjective = story.Current >= Checkpoint.Act3DoorBoarded && story.Current < Checkpoint.Act11GiantEncounter;
		Check("compass has an objective", (story.ObjectivePosition != null) == wantObjective, $"{story.ObjectivePosition}");

		if (sc.Cp == Checkpoint.Act8BunkerEntered && GetTree().GetFirstNodeInGroup("bunker_marker") is Bunker bunker)
			Check("bunker door open", bunker.IsOpen, "");

		input.ScriptedMove = Vector2.Zero;
		Screenshot(sc.Name);
	}

	private static string GearOf(string inv) => string.Join(",", (inv ?? "").Split(';')[0].Split(',', System.StringSplitOptions.RemoveEmptyEntries).OrderBy(s => s));

	private void CheckFired<T>(bool want) where T : StoryTrigger
	{
		var t = FindFirst<T>();
		if (t == null) { Check($"{typeof(T).Name} present", false, ""); return; }
		Check($"{typeof(T).Name} guard", t.Fired == want, $"fired {t.Fired} (want {want})");
	}

	private T FindFirst<T>() where T : Node
	{
		foreach (var n in GetTree().CurrentScene.FindChildren("*", "", true, false))
			if (n is T t) return t;
		return null;
	}

	private async Task Drive(PlayerInput input, Vector2 move, double seconds)
	{
		input.ScriptedMove = move;
		await Wait(seconds);
		input.ScriptedMove = Vector2.Zero;
		await Wait(0.2);
	}

	private void Screenshot(string label)
	{
		if (DisplayServer.GetName() == "headless") return;   // the dummy renderer has no frame to grab
		try
		{
			var img = GetViewport()?.GetTexture()?.GetImage();
			img?.SavePng(ProjectSettings.GlobalizePath($"{OutDir}/continue_{_index:00}_{label}.png"));
		}
		catch (System.Exception e) { GD.PushWarning($"[continue-test] screenshot failed: {e.Message}"); }
	}

	private void Finish()
	{
		int failed = _checks.Count(c => !c.ok);
		double secs = (Time.GetTicksMsec() - _startMsec) / 1000.0;
		var sb = new StringBuilder($"Project DS continue round-trip test  {Time.GetDatetimeStringFromSystem()}\n" +
			$"{_checks.Count - failed}/{_checks.Count} passed, {Scenarios.Length} scenarios, {secs:0} s\n\n");
		string last = null;
		foreach (var c in _checks)
		{
			if (c.scenario != last) { sb.Append($"\n[{c.scenario}]\n"); last = c.scenario; }
			sb.Append($"{(c.ok ? "PASS" : "FAIL")}  {c.name}  {c.detail}\n");
		}
		using (var f = FileAccess.Open($"{OutDir}/continue_report.txt", FileAccess.ModeFlags.Write)) f.StoreString(sb.ToString());
		GD.Print(sb.ToString());
		GetTree().Quit(failed == 0 ? 0 : 1);
	}
}
