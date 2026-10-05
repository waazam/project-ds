using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.World;
using ProjectDS.World.BunkerParts;

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
	private static readonly string[] F5 = F2.Concat(new[] { StoryManager.Flag.StormStarted, StoryManager.Flag.CabinDoorOpen, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, StoryManager.Flag.PickupTakenAxe, StoryManager.Flag.CodeDigit(1), StoryManager.Flag.CodeDigit(2) }).ToArray();
	private static readonly string[] F6 = F5.Concat(new[] { StoryManager.Flag.NewelPostTaken, StoryManager.Flag.DawnBroke }).ToArray();
	private static readonly string[] F6Voice = F6.Concat(new[] { StoryManager.Flag.ClearingVoiceHeard }).ToArray();
	// The giant is seen from the Act 7 lookout (after checkpoint 6): a save at the lookout carries it once it has crossed.
	private static readonly string[] F7 = F6Voice.Concat(new[] { StoryManager.Flag.ClearingLoopDone, StoryManager.Flag.Act6NightFell, StoryManager.Flag.GiantEventDone, StoryManager.Flag.CodeDigit(3), StoryManager.Flag.CodeDigit(4) }).ToArray();
	private static readonly string[] F9 = F7.Concat(new[] { StoryManager.Flag.CrtPuzzleDone, StoryManager.Flag.WalkieTaken }).ToArray();
	private static readonly string[] F10Scared = F9.Concat(new[] { StoryManager.Flag.BunkerMazeEntered, BunkerRooms.ScaredFlag }).ToArray();
	private static readonly string[] F10 = F10Scared.Concat(new[] { StoryManager.Flag.BunkerMazeExited }).ToArray();
	private static readonly string[] F11 = F10.Concat(new[] { StoryManager.Flag.Act11DialogueDone }).ToArray();
	// Act 13 all done: every puzzle in the station solved and the iron door open.
	private static readonly string[] F13 = F11.Concat(new[] { StoryManager.Flag.NewelSeated,
		StoryManager.Flag.StationBasementTapeCut, StoryManager.Flag.StationLightsDead, StoryManager.Flag.StationBasementDrained,
		StoryManager.Flag.StationClockBroken, StoryManager.Flag.StationRoom1Open, StoryManager.Flag.StationRoom1Solved,
		StoryManager.Flag.StationRoom2Solved, StoryManager.Flag.StationWebBurned, StoryManager.Flag.StationDoor3Seen,
		StoryManager.Flag.StationEyeTaken, StoryManager.Flag.StationHandTaken, StoryManager.Flag.StationStepTaken,
		StoryManager.Flag.StationEyeSet, StoryManager.Flag.StationHandSet, StoryManager.Flag.StationStepSet,
		StoryManager.Flag.StationDoor3Open }).ToArray();

	private static readonly string[] F22 = F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen, StoryManager.Flag.RoundRoomWebBurned, StoryManager.Flag.RoundRoomPowered,
		StoryManager.Flag.ChurchCandle(1), StoryManager.Flag.ChurchCandle(2), StoryManager.Flag.ChurchCandle(3), StoryManager.Flag.ChurchCandle(4), StoryManager.Flag.ChurchVestryOpen,
		StoryManager.Flag.ChurchFontOpen, StoryManager.Flag.ChurchChalicePlaced, "pickup_taken_church_font_key", "pickup_taken_church_chalice",
		StoryManager.Flag.LodgeFrontTried, StoryManager.Flag.LodgeBackDoorOpen, LodgeFlag.SnowedIn }).ToArray();
	private static readonly string[] F23a = F22.Concat(new[] { LodgeFlag.Taken(LodgeFlag.Card202), LodgeFlag.Room202Open, LodgeFlag.Taken(LodgeFlag.PantryKey), LodgeFlag.Room202Jammed }).ToArray();
	private static readonly string[] F23b = F23a.Concat(new[] { LodgeFlag.PantryOpen, LodgeFlag.Taken(LodgeFlag.Card203), LodgeFlag.Room203Open, LodgeFlag.Taken(LodgeFlag.DiningKey), LodgeFlag.Note204, LodgeFlag.Room204Open, LodgeFlag.Room203Jammed }).ToArray();
	private static readonly string[] F23c = F23b.Concat(new[] { LodgeFlag.DiningOpen, LodgeFlag.StormUp, LodgeFlag.Table(0, 1), LodgeFlag.Table(3, 2), LodgeFlag.Table(1, 3), LodgeFlag.Table(4, 4), LodgeFlag.Table(2, 5), LodgeFlag.Table(5, 6), LodgeFlag.Taken(LodgeFlag.Card201) }).ToArray();
	private static readonly string[] F23d = F23c.Concat(new[] { LodgeFlag.Room201Open, LodgeFlag.Letter201, LodgeFlag.Room201Jammed }).ToArray();
	private static readonly string[] F23e = F23d.Concat(new[] { LodgeFlag.InCrawlspace, LodgeFlag.Frozen }).ToArray();
	private static readonly string[] F23f = F23e.Concat(new[] { LodgeFlag.WardrobeDown }).ToArray();
	private static readonly Scenario[] Scenarios =
	{
		new("act1_start", Checkpoint.Act1Start, new string[0], "camera;tool=None"),
		new("act2_stairs_climbed", Checkpoint.Act2StairsClimbed, F2, ";tool=None"),
		new("act3_door_boarded", Checkpoint.Act3DoorBoarded, F2, ";tool=None"),
		new("act3_storm", Checkpoint.Act3DoorBoarded, F2.Concat(new[] { StoryManager.Flag.StormStarted, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass }).ToArray(), "lantern,compass;tool=None"),
		new("act5_cabin_entered", Checkpoint.Act5CabinEntered, F5, "lantern,compass;tool=None"),
		new("act5_post_taken", Checkpoint.Act5CabinEntered, F5.Append(StoryManager.Flag.NewelPostTaken).ToArray(), "lantern,compass,newel_post;tool=None"),
		new("act6_bridge_crossed", Checkpoint.Act6BridgeCrossed, F6, "lantern,compass,newel_post;tool=None"),
		// The cap ("newel_post") is carried from the cabin all the way to the foot of the last staircase (Act 11).
		new("act6_voice_heard", Checkpoint.Act6BridgeCrossed, F6Voice, "lantern,compass,newel_post;tool=None"),
		// The loop is never saved mid-way: a save after the voice reloads with the loop lit again; one after the fall is Act 7 in all but checkpoint.
		new("act6_loop_done", Checkpoint.Act6BridgeCrossed, F7, "lantern,compass,newel_post;tool=None"),
		new("act7_cabin_burning", Checkpoint.Act7CabinBurning, F7, "lantern,compass,newel_post;tool=None"),
		new("act8_bunker_entered", Checkpoint.Act8BunkerEntered, F7, "lantern,compass,newel_post;tool=None"),
		// The dead walkie-talkie is taken off the CRT console (Act 9) and wakes only outside; a save after the rooms' scare
		// reloads with the room black, the eyes lit and the way back open.
		new("act9_walkie_taken", Checkpoint.Act8BunkerEntered, F9, "lantern,compass,newel_post,radio;tool=None"),
		new("act10_rooms_scared", Checkpoint.Act8BunkerEntered, F10Scared, "lantern,compass,newel_post,radio;tool=None"),
		new("act10_walkie_found", Checkpoint.Act10WalkieFound, F10, "lantern,compass,newel_post,radio;tool=None"),
		new("act10_after_radio", Checkpoint.Act10WalkieFound, F11, "lantern,compass,newel_post,radio;tool=None"),
		new("act11_giant_encounter", Checkpoint.Act11GiantEncounter, F11.Append(StoryManager.Flag.NewelSeated).ToArray(), "lantern,compass,radio;tool=None"),
		// Act 14's start (Room 3, through the iron door) and end (the chamber under the stairwell)
		new("act13_finished", Checkpoint.Act13Finished, F13, "lantern,compass,radio;tool=None"),
		new("act14_finished", Checkpoint.Act14Finished, F13.Append(StoryManager.Flag.Act14JumpedAcross).ToArray(), "lantern,compass,radio;tool=None"),
		new("act15_finished", Checkpoint.Act15Finished, F13.Append(StoryManager.Flag.Act14JumpedDown).ToArray(), "lantern,compass,radio;tool=None"),
		new("act16_finished", Checkpoint.Act16Finished, F13.Append(StoryManager.Flag.Act14JumpedDown).ToArray(), "lantern,compass,radio;tool=None"),
		new("act17_finished", Checkpoint.Act17Finished, F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen }).ToArray(), "lantern,compass,radio;tool=None"),
		new("act18_finished", Checkpoint.Act18Finished, F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen }).ToArray(), "lantern,compass,radio;tools=Lighter"),
		new("act19_finished", Checkpoint.Act19Finished, F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen }).ToArray(), "lantern,compass,radio;tools=Lighter"),
		new("act20_finished", Checkpoint.Act20Finished, F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen, StoryManager.Flag.RoundRoomWebBurned }).ToArray(), "lantern,compass,radio;tools=Lighter"),
		new("act21_church", Checkpoint.Act21ChurchReached, F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen, StoryManager.Flag.RoundRoomWebBurned, StoryManager.Flag.RoundRoomPowered }).ToArray(), "lantern,compass,radio;tools=Lighter"),
		new("act21_finished", Checkpoint.Act21Finished, F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen, StoryManager.Flag.RoundRoomWebBurned, StoryManager.Flag.RoundRoomPowered,
			StoryManager.Flag.ChurchCandle(1), StoryManager.Flag.ChurchCandle(2), StoryManager.Flag.ChurchCandle(3), StoryManager.Flag.ChurchCandle(4), StoryManager.Flag.ChurchVestryOpen,
			StoryManager.Flag.ChurchFontOpen, StoryManager.Flag.ChurchChalicePlaced, "pickup_taken_church_font_key", "pickup_taken_church_chalice" }).ToArray(), "lantern,compass,radio;tools=Lighter"),
		// Act 22's halfway save (a flag on Act 21's end): on the road, halfway to the lodge
		new("act22_midway", Checkpoint.Act21Finished, F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen, StoryManager.Flag.RoundRoomWebBurned, StoryManager.Flag.RoundRoomPowered,
			StoryManager.Flag.ChurchCandle(1), StoryManager.Flag.ChurchCandle(2), StoryManager.Flag.ChurchCandle(3), StoryManager.Flag.ChurchCandle(4), StoryManager.Flag.ChurchVestryOpen,
			StoryManager.Flag.ChurchFontOpen, StoryManager.Flag.ChurchChalicePlaced, "pickup_taken_church_font_key", "pickup_taken_church_chalice",
			StoryManager.Flag.Act22Midway }).ToArray(), "lantern,compass,radio;tools=Lighter"),
		// Act 22's end: through the lodge's back door, in the mudroom
		new("act22_finished", Checkpoint.Act22Finished, F13.Concat(new[] { StoryManager.Flag.Act14JumpedDown, StoryManager.Flag.Act18IntroSeen, StoryManager.Flag.RoundRoomWebBurned, StoryManager.Flag.RoundRoomPowered,
			StoryManager.Flag.ChurchCandle(1), StoryManager.Flag.ChurchCandle(2), StoryManager.Flag.ChurchCandle(3), StoryManager.Flag.ChurchCandle(4), StoryManager.Flag.ChurchVestryOpen,
			StoryManager.Flag.ChurchFontOpen, StoryManager.Flag.ChurchChalicePlaced, "pickup_taken_church_font_key", "pickup_taken_church_chalice",
			StoryManager.Flag.LodgeFrontTried, StoryManager.Flag.LodgeBackDoorOpen }).ToArray(), "lantern,compass,radio;tools=Lighter"),
		// Act 23's saves: out of 202 (the pantry key), out of 203/204 (the dining key), the bowl (201's card)
		new("act23_room202", Checkpoint.Act23Room202Done, F23a, "lantern,compass,radio;tools=Lighter+PantryKey"),
		new("act23_room203", Checkpoint.Act23Room203Done, F23b, "lantern,compass,radio;tools=Lighter+DiningKey"),
		new("act23_card201", Checkpoint.Act23Keycard201, F23c, "lantern,compass,radio;tools=Lighter+Keycard201"),
		new("act23_letter201", Checkpoint.Act23Letter201, F23d, "lantern,compass,radio;tools=Lighter+Keycard201"),
		new("act23_crawlspace", Checkpoint.Act23Crawlspace, F23e, "lantern,compass,radio;tools=Lighter+Keycard201"),
		new("act23_frozen", Checkpoint.Act23Frozen, F23f, "lantern,compass,radio;tools=Lighter+Keycard201"),
		// Act 23's end (the front doors gone), and the end of the story after the credits (the same place, for now)
		new("act23_finished", Checkpoint.Act23Finished, F23f.Append(LodgeFlag.FrontBroken).ToArray(), "lantern,compass,radio;tools=Lighter+Keycard201"),
		// Act 24's saves: in the maze's way in, at the cavern, the flamethrower in hand
		new("act24_maze", Checkpoint.Act24Maze, F23f.Append(LodgeFlag.FrontBroken).ToArray(), "lantern,compass,radio;tools=Lighter+Keycard201"),
		new("act24_trenches", Checkpoint.Act24Trenches, F23f.Append(LodgeFlag.FrontBroken).ToArray(), "lantern,compass,radio;tools=Lighter+Keycard201"),
		new("act24_flamethrower", Checkpoint.Act24Flamethrower, F23f.Concat(new[] { LodgeFlag.FrontBroken, World.SnowMaze.Act24Maze.FlagCrowbar, World.SnowMaze.Act24Maze.FlagCrate }).ToArray(), "lantern,compass,radio;tools=Lighter+Keycard201+Crowbar+Flamethrower"),
		// Act 25 (the trailhead): out of the snow at the trail's far end; and after the end, at the car
		new("act24_finished", Checkpoint.Act24Finished, F23f.Concat(new[] { LodgeFlag.FrontBroken, "ending_1" }).ToArray(), "lantern,compass,radio;tools=Lighter+Keycard201+Crowbar+Flamethrower"),
		new("game_finished", Checkpoint.GameFinished, F23f.Concat(new[] { LodgeFlag.FrontBroken, "ending_1" }).ToArray(), "lantern,compass,radio;tools=Lighter+Keycard201+Crowbar+Flamethrower"),
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
			// First pass: a fresh game just to have the level; start the round trips (from `--continue-from=NAME`, for a
			// quick look at the last few)
			_index = 0;
			foreach (var arg in OS.GetCmdlineUserArgs())
				if (arg.StartsWith("--continue-from="))
					_index = Mathf.Max(0, System.Array.FindIndex(Scenarios, x => x.Name == arg["--continue-from=".Length..]));
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
		// the lake (Act 12) and the station (Acts 13-14) are built off the forest's heightfield, on their own ground
		bool offForest = GetTree().GetFirstNodeInGroup("lake_marker") is Lake lakeHere && _player.GlobalPosition.DistanceTo(lakeHere.WakeSpotWorld) < 300f
			|| StationInterior.Instance is { } st && _player.GlobalPosition.DistanceTo(st.GlobalPosition) < 2000f;   // the station, the stairwell and the hallway beyond
		if (offForest) ground = _player.GlobalPosition.Y;
		Check("standing on something", _player.IsOnFloor() && _player.GlobalPosition.Y > ground - 1.5f,
			$"on floor {_player.IsOnFloor()}, y {_player.GlobalPosition.Y:0.0} vs ground {ground:0.0}");

		// Can actually move: forward, or back if forward is blocked. (A scene may open with a moment's
		// scripted look, e.g. the boss room's short intro after Continue: wait for control first.)
		for (int i = 0; i < 300 && !input.Enabled; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Vector3 before = _player.GlobalPosition;
		await Drive(input, new Vector2(0, 1), 0.6);
		if (_player.GlobalPosition.DistanceTo(before) < 0.2f) await Drive(input, new Vector2(0, -1), 0.6);
		Check("player can move", _player.GlobalPosition.DistanceTo(before) > 0.2f, $"{_player.GlobalPosition.DistanceTo(before):0.00} m");
		if (sc.Cp == Checkpoint.Act13Finished && StationInterior.Instance?.Room3 is { } r3)
			Check("Act 14's start respawns just inside Room 3", before.DistanceTo(r3.EntryWorld) < 1.5f, $"{before} vs {r3.EntryWorld}");
		if (sc.Cp == Checkpoint.Act14Finished && StationInterior.Instance?.Room3?.Stairs is { } sw)
			Check("Act 14's end respawns on the chamber floor under the stairwell", before.DistanceTo(sw.LandingSpotWorld) < 1.5f, $"{before} vs {sw.LandingSpotWorld}");
		if (sc.Cp == Checkpoint.Act15Finished && StationInterior.Instance?.Room3?.Stairs?.Hallway is { } hw)
			Check("Act 15's end respawns in the janitor's closet, door shut", before.DistanceTo(hw.ClosetWorld) < 1.5f && hw.Finished && !hw.DoorOpen, $"{before} vs {hw.ClosetWorld}");
		if (sc.Cp == Checkpoint.Act16Finished && StationInterior.Instance?.Sewer is { } sewer16)
			Check("Act 16's end respawns inside the sewer door", before.DistanceTo(sewer16.EntranceWorld) < 1.5f, $"{before} vs {sewer16.EntranceWorld}");
		if (sc.Cp == Checkpoint.Act17Finished && StationInterior.Instance?.Boss is { } boss17)
			Check("Act 17's end respawns on the boss room's catwalk, the fight starting", before.DistanceTo(boss17.LandingWorld) < 2f && boss17.State != BossRoom.Phase.Waiting, $"{before} vs {boss17.LandingWorld}, {boss17.State}");
		if (sc.Cp == Checkpoint.Act18Finished && StationInterior.Instance?.Boss is { } boss18)
			Check("Act 18's end respawns in the library, the thing dead, the door open", before.DistanceTo(boss18.TidyWorld) < 3f && boss18.DoorOpen && boss18.Beast.Dead, $"{before} vs {boss18.TidyWorld}");
		if (sc.Cp == Checkpoint.Act18Finished && StationInterior.Instance?.Boss?.Library is { } lib18)
			Check("the library: the sheet on the side table, the bookcase shut", !lib18.SheetOff && !lib18.PuzzleSolved && !lib18.BookcaseOpen && lib18.PlayerInside(before));
		if (sc.Cp == Checkpoint.Act19Finished && StationInterior.Instance?.Boss?.Library is { } lib19)
			Check("Act 19's end respawns in the round room, the bookcase open, the web still on the dais", before.DistanceTo(lib19.Round.EntryWorld) < 1.5f && lib19.BookcaseOpen && lib19.PuzzleSolved && !lib19.Round.WebsBurned, $"{before} vs {lib19.Round.EntryWorld}");
		if (sc.Cp == Checkpoint.Act20Finished && StationInterior.Instance?.Boss?.Library?.Round is { } rr20)
			Check("Act 20's end respawns in the room at the top, the dais up, the web gone", before.DistanceTo(rr20.TopWorld) < 1.5f && rr20.Arrived && rr20.WebsBurned && _player.IsOnFloor(), $"{before} vs {rr20.TopWorld}");
		if (sc.Cp == Checkpoint.Act21ChurchReached && StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church is { } ch21)
			Check("Act 21's save respawns in the church's crypt by the shut hatch, winter, no candles lit", ch21.Inside(before) && before.DistanceTo(ch21.HatchExitWorld) < 1.5f && ch21.CandlesLit == 0 && !ch21.VestryOpen, $"{before} vs {ch21.HatchExitWorld}");
		bool midway = System.Array.IndexOf(sc.Flags, StoryManager.Flag.Act22Midway) >= 0;
		if (midway && StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church?.Woods is { } woods22)
		{
			var ml = woods22.ToLocal(before);
			WinterWoods.Nearest(ml.X, ml.Z, out float ms, out _);
			Check("Act 22's halfway save respawns on the road halfway to the lodge, on the ground", Mathf.Abs(ms - WinterWoods.MidwayS) < 4f && _player.IsOnFloor(), $"s {ms:0} (want {WinterWoods.MidwayS:0})");
		}
		if (!midway && sc.Cp == Checkpoint.Act21Finished && StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church is { } ch22)
			Check("Act 21's end respawns outside the great door, standing open on the snow: four candles burning, the vestry and the font open, the chalice in its niche", !ch22.Inside(before) && before.DistanceTo(ch22.DoorWorld) < 8f && ch22.DoorOpen && ch22.CandlesLit == 4 && ch22.VestryOpen && ch22.FontOpen && ch22.ChalicePlaced && _player.IsOnFloor(), $"{before}");
		if (sc.Cp == Checkpoint.Act22Finished && StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church?.Woods?.Lodge is { } lodge22)
			Check("Act 22's end respawns in the lodge's mudroom, the back door forced open, snowed in", lodge22.InMudroom(before) && lodge22.BackOpen && lodge22.SnowPile.Visible, $"{before} (lodge-local {lodge22.ToLocal(before)})");
		if (sc.Cp >= Checkpoint.Act23Room202Done && StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church?.Woods?.Lodge is { } lodge23)
		{
			var lb = lodge23.ToLocal(before);
			if (sc.Cp == Checkpoint.Act23Room202Done)
				Check("Act 23's first save: in the upstairs corridor, 202 jammed shut, the pantry still locked", lb.Y > SkiLodge.UpperY - 0.3f && Mathf.Abs(lb.Z) < 1f && lodge23.Room202Jammed && lodge23.PantryDoor.Current == ProjectDS.World.LodgeParts.LodgeDoor.State.Locked, $"{lb}");
			if (sc.Cp == Checkpoint.Act23Room203Done)
				Check("Act 23's second save: in the corridor, 202 and 203 jammed, the pantry open, the dining room locked", lb.Y > SkiLodge.UpperY - 0.3f && lodge23.Room202Jammed && lodge23.Room203Jammed && lodge23.PantryDoor.Current == ProjectDS.World.LodgeParts.LodgeDoor.State.Open && lodge23.DiningDoorL.Current == ProjectDS.World.LodgeParts.LodgeDoor.State.Locked, $"{lb}");
			if (sc.Cp == Checkpoint.Act23Keycard201)
				Check("Act 23's third save: in the dining hall, all six sheets off (their things on the tables), the storm up, the fifth table down", SkiLodge.InsideLocal(lb) && lb.X > 10f && lodge23.TablesPulled == 6 && lodge23.PlatterSkull != null && lodge23.Storm > 0.9f, $"{lb}, pulled {lodge23.TablesPulled}, storm {lodge23.Storm:0.00}");
			if (sc.Cp == Checkpoint.Act23Letter201)
				Check("Act 23's letter save: in 201 by its bathroom, the door jammed behind, the envelope gone, the lodge not yet frozen", lb.Y > SkiLodge.UpperY - 0.3f && lb.X < -20f && lodge23.Room201Jammed && lodge23.Envelope201 == null && !lodge23.Frozen, $"{lb}");
			if (sc.Cp == Checkpoint.Act23Crawlspace)
				Check("Act 23's crawlspace save: in the walls (the maze, below), the lodge frozen, the arms still to come", lb.Y < -40f && lodge23.Frozen && lodge23.ArmsBurst == 0, $"{lb}");
			if (sc.Cp == Checkpoint.Act23Finished)
				Check($"{sc.Cp}: at the splintered front doorway, the doors gone (one leaf hanging, one out on the porch), the lodge frozen", Mathf.Abs(lb.X) < 1.5f && lb.Z > SkiLodge.HexIn - 2f && lodge23.FrontBroken && lodge23.Frozen
					&& lodge23.FrontLeafR.Position.Z > SkiLodge.Apothem + 1f, $"{lb}");
			if (sc.Cp == Checkpoint.Act23Frozen)
				Check("Act 23's frozen save: in the dining hall, the wardrobe over, the windows broken, the front door ajar", lb.X > 25f && lb.Y < 2f && lodge23.WardrobeDown && lodge23.WindowsBroken > 10 && lodge23.Frozen && !lodge23.FrontBroken, $"{lb}, windows {lodge23.WindowsBroken}");
		}
		if (sc.Cp is >= Checkpoint.Act24Maze and <= Checkpoint.Act24Flamethrower)
		{
			var maze = World.SnowMaze.Act24Maze.Instance;
			var ml = maze?.Cave != null ? maze.Cave.ToLocal(before) : Vector3.Zero;
			Check($"{sc.Cp}: in the snow maze, where the save was", maze != null && maze.InMaze
				&& (sc.Cp != Checkpoint.Act24Trenches || World.SnowMaze.SnowMazeLayout.InCavern(ml) || ml.DistanceTo(World.SnowMaze.SnowMazeLayout.CavernCentre) < 22f), $"{ml}");
			if (sc.Cp == Checkpoint.Act24Flamethrower)
				Check("the flamethrower's save: it's in their hands", _player.Inventory.HasTool(ToolKind.Flamethrower) && _player.GetNodeOrNull<Flamethrower>("Flamethrower") != null);
		}
		if (sc.Cp >= Checkpoint.Act24Finished)
		{
			var ed = World.SnowMaze.EndingDirector.Instance;
			Check($"{sc.Cp}: Act 25 in the trailhead ({(sc.Cp == Checkpoint.GameFinished ? "by the car" : "at the snow's edge")})", ed != null
				&& (sc.Cp == Checkpoint.GameFinished ? before.DistanceTo(ed.CarAt) < 8f : before.DistanceTo(ed.Start) < 4f), $"{before}");
		}
		if (sc.Cp == Checkpoint.Act11GiantEncounter && GetTree().GetFirstNodeInGroup("lake_marker") is Lake lake)
			Check("checkpoint 9 (Act 12's start) respawns on the lake shore", before.DistanceTo(lake.WakeSpotWorld) < 4f, $"{before} vs {lake.WakeSpotWorld}");

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
		// burning from the wake after the fall (the loop's end), not only from checkpoint 6 on: they wake to it (Dan, 2026-09-22)
		bool wantFire = story.Current >= Checkpoint.Act7CabinBurning || story.HasFlag(StoryManager.Flag.ClearingLoopDone);
		Check("cabin fire state", fire != null && fire.Burning == wantFire, $"burning {fire?.Burning} (want {wantFire})");

		var act6 = FindFirst<Act6ClearingEvent>();
		int wantMinis = StoryBeat.Act6Revealed(story) && !story.HasFlag(StoryManager.Flag.ClearingLoopDone) ? 15 : 0;   // gone for good after the fall (Act 7)
		Check("clearing mini stairs", act6 != null && act6.MiniStairCount == wantMinis, $"{act6?.MiniStairCount} (want {wantMinis})");
		Check("no leftover clearing spotlight", act6 is { SpotlightActive: false }, "");
		// The loop is armed again (nothing lit until a flight is chosen) whenever the voice has spoken but the fall has not come.
		bool wantLoop = story.ClearingVoiceHeard && !story.HasFlag(StoryManager.Flag.ClearingLoopDone) && story.Current < Checkpoint.Act7CabinBurning;
		Check("clearing loop armed", act6 != null && act6.LoopArmed == wantLoop && act6.LoopTarget == null && act6.LoopDone == story.HasFlag(StoryManager.Flag.ClearingLoopDone),
			$"armed {act6?.LoopArmed}, target {act6?.LoopTarget?.Name ?? "none"}, done {act6?.LoopDone} (want armed {wantLoop})");
		if (wantLoop) Check("compass has somewhere to point in the clearing", story.ObjectivePosition != null, $"{story.ObjectivePosition}");

		var act11 = FindFirst<Act11Ending>();
		Check("Act 11 stairs height", act11 != null && act11.StairsTall == story.Act11DialogueDone, $"tall {act11?.StairsTall} (want {story.Act11DialogueDone})");
		bool wantClimbTrigger = story.Act11DialogueDone && story.Current < Checkpoint.Act11GiantEncounter;
		// The way up is open once the radio has spoken: the cap's E-point waits on the top landing until the ending.
		Check("Act 11 climb trigger", (act11?.CapSeat != null) == wantClimbTrigger, $"seat {act11?.CapSeat != null} (want {wantClimbTrigger})");

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
		if (sc.Cp >= Checkpoint.Act2StairsClimbed) Check("the camera is kept all game", inv?.HasCamera == true);
		Check("inventory restored", inv != null && GearOf(inv.Serialize()) == GearOf(sc.Inventory) || (sc.Cp == Checkpoint.Act10WalkieFound && inv?.HasRadio == true),
			$"{inv?.Serialize()} (saved {sc.Inventory})");

		// (dead from Act 14's stairwell through the church; alive again outside it, in Act 22, pointing at the lodge, and in it)
		bool wantObjective = (story.Current >= Checkpoint.Act3DoorBoarded && story.Current < Checkpoint.Act11GiantEncounter)
			|| story.Current >= Checkpoint.Act21Finished;
		Check("compass has an objective", (story.ObjectivePosition != null) == wantObjective, $"{story.ObjectivePosition}");

		if (sc.Cp == Checkpoint.Act8BunkerEntered && GetTree().GetFirstNodeInGroup("bunker_marker") is Bunker bunker)
			Check("bunker door open", bunker.IsOpen, "");
		if (BunkerInterior.Instance is { Rooms: { } rooms })
		{
			bool wantScared = story.HasFlag(BunkerRooms.ScaredFlag);
			// Scared = shoved out into the hall with the door shut behind (no eyes left in the room), the chase on.
			Check("the rooms' scare state", rooms.JumpscareDone == wantScared && rooms.BlackedOut == wantScared && rooms.ShovedOut == wantScared && !rooms.EntryOpen && !rooms.EyesLit,
				$"done {rooms.JumpscareDone} black {rooms.BlackedOut} shoved {rooms.ShovedOut} open {rooms.EntryOpen} eyes {rooms.EyesLit} (want {wantScared})");
			bool walkieGone = story.HasFlag(StoryManager.Flag.WalkieTaken) || story.Current >= Checkpoint.Act10WalkieFound;
			Check("the console walkie-talkie", (FindFirst<WalkiePickup>() == null) == walkieGone, $"present {FindFirst<WalkiePickup>() != null} (want gone {walkieGone})");
		}

		input.ScriptedMove = Vector2.Zero;
		Screenshot(sc.Name);
	}

	// the camera is always given back from Act 2 on (it stays with the player all game), so it isn't compared
	private static string GearOf(string inv) => string.Join(",", (inv ?? "").Split(';')[0].Split(',', System.StringSplitOptions.RemoveEmptyEntries).Where(s => s != "camera").OrderBy(s => s));

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
