using System.Collections.Generic;
using System.Linq;
using ProjectDS.World;
using ProjectDS.World.SnowMaze;

namespace ProjectDS.Systems;

public partial class Playtest
{
	/// <summary>The save a run has at the start of <paramref name="act"/> (StoryTest's own table).</summary>
	private static Seed At(int act)
	{
		var (cp, flags, inv) = StoryTest.StateFor(act);
		return new Seed(cp, flags, inv);
	}

	private static Seed At(Checkpoint cp, IEnumerable<string> flags, string inv, int deaths = 0) => new(cp, flags.ToArray(), inv, Deaths: deaths);

	private const string Gear = "lantern,compass,radio";

	private static Seed Ending(int n) => At(Checkpoint.Act24Finished,
		StoryTest.StateFor(24).flags.Concat(new[] { Act24Maze.FlagCrowbar, Act24Maze.FlagCrate, World.SnowMaze.Act25Ending.FlagPrefix + n }),
		Gear + ";tools=Lighter+Keycard201+Crowbar+Flamethrower",
		// the endings read the game's record; the seed's is what makes each one's own table entry sensible
		deaths: n == 3 ? 12 : n == 4 ? 1 : n == 1 ? 3 : 0);

	/// <summary>A Continue into one of ContinueRoundTripTest's saves (the checkpoints no other beat starts from).</summary>
	private Beat ContinueBeat(string id, int act, string scenario)
	{
		var sc = ContinueRoundTripTest.Scenarios.First(x => x.Name == scenario);
		return new Beat(id, act, $"Continue into {sc.Cp} ({scenario})", At(sc.Cp, sc.Flags, sc.Inventory), 60, ct => ContinueInto(sc.Cp, sc.Inventory, ct));
	}

	/// <summary>Continued into <paramref name="cp"/>: the story, the gear, control, the ground underfoot, a step.</summary>
	private async System.Threading.Tasks.Task ContinueInto(Checkpoint cp, string inventory, System.Threading.CancellationToken ct)
	{
		await _bot.WaitUntil(() => Input.Enabled && !Input.Modal, 25, ct);
		Check("control handed back after the Continue (no softlock)", Input.Enabled);
		Check($"the checkpoint restored: {cp}", Story.Current == cp && Story.LoadedFromSave, $"{Story.Current}");
		Check("in the checkpoint's level", GetTree().CurrentScene?.SceneFilePath == StoryManager.LevelFor(cp));
		static string GearOf(string s) => string.Join(",", (s ?? "").Split(';')[0].Split(',', System.StringSplitOptions.RemoveEmptyEntries).Where(g => g != "camera").OrderBy(g => g));
		Check("the gear carried restored", GearOf(Inv.Serialize()) == GearOf(inventory), $"{Inv.Serialize()} (saved {inventory})");
		await _bot.Frames(10, ct);
		await _bot.WaitUntil(() => Player.IsOnFloor(), 2, ct);
		Check("standing on something", Player.IsOnFloor(), $"{Player.GlobalPosition}, velocity {Player.Velocity}");
		var before = Player.GlobalPosition;
		foreach (var dir in new[] { new Godot.Vector2(0, 1), new Godot.Vector2(0, -1), new Godot.Vector2(1, 0), new Godot.Vector2(-1, 0) })
			if (Player.GlobalPosition.DistanceTo(before) < 0.2f) await _bot.PushFor(dir, 0.8, ct);
		Check("free to move", Player.GlobalPosition.DistanceTo(before) > 0.2f, $"{Player.GlobalPosition.DistanceTo(before):0.00} m");
		if (cp == Checkpoint.Act24Flamethrower) Check("the flamethrower in their hands (ready to fire)", Player.GetNodeOrNull<ProjectDS.Player.Flamethrower>("Flamethrower") is { Active: true },
			$"in hand: {(GetTree().GetFirstNodeInGroup("equipped_item_hud") as ProjectDS.UI.EquippedItemHud)?.Held}, carrying {Inv.Serialize()}");
		Shot("continued");
	}

	private List<Beat> BuildBeats()
	{
		var room1Solved = StoryTest.StateFor(13).flags.Concat(new[] { StoryManager.Flag.StationBasementTapeCut, StoryManager.Flag.StationLightsDead,
			StoryManager.Flag.StationBasementDrained, StoryManager.Flag.StationClockBroken, StoryManager.Flag.StationRoom1Open, StoryManager.Flag.StationRoom1Solved });
		var maze = ContinueRoundTripTest.F23f.Append(LodgeFlag.FrontBroken);
		return new List<Beat>
		{
			new("0", 0, "Systems: movement, run and stamina, crouch, the pause menu, settings, key rebinding, quit to menu", Seed.Fresh, 150, SystemsBeat),
			new("1", 1, "Act 1: the opening, the register, the album, photographs (and the omen), the fir, the stairs found", Seed.Fresh, 160, Act1),
			new("1b", 1, "Act 1: the gunshot, for a player who wades the creek beside the footbridge", At(Checkpoint.Act1Start, new string[0], "camera;tool=None"), 60, Act1Creek),
			new("1c", 1, "Act 1: the dead fir with their back to it: stopped, turned, and it comes down", Seed.Fresh, 90, Act1FirUnseen),
			new("2", 2,"Act 2: onto the stairs, the float, no way down, the post, the collapse; the wake in the Hollow", At(Checkpoint.Act1Start, new string[0], "camera;tool=None"), 150, Act2),
			new("3", 3, "Act 3: the camp, the note, lantern and compass, the flame and the blacklight, the inventory, out of the safe zone", At(3), 120, Act3),
			new("4", 4, "Act 4: the storm walk: numbers 1-2 under the blacklight, the stalker, far footfalls", At(4), 120, Act4),
			new("5a", 5, "Act 5: the hammer's pry, inside (knocks, the slam), the cap, out into the dry", At(5), 140, Act5Hammer),
			new("5b", 5, "Act 5: the axe through the boards", At(5), 70, Act5Axe),
			new("6", 6, "Act 6: the footbridge, the voice, the clearing's loop of flights, the fall and the wake", At(6), 200, Act6),
			new("6b", 6, "Act 6: over the creek beside the footbridge (wading): checkpoint 5, the compass, the voice", At(6), 80, Act6Creek),
			new("7", 7,"Act 7: the lookout, the fire, the giant, numbers 3-4", At(7), 160, Act7),
			new("8", 8, "Acts 8-10: the keypad (wrong, right), the hallway's red, the vine door, the CRT room, the walkie, the rooms, the run out", At(8), 220, Act8To10),
			ContinueBeat("8c", 8, "act8_bunker_entered"),
			new("11", 11, "Act 11: the radio, the pursuer, the tall stairs, the cap seated, the giant", At(11), 200, Act11),
			new("12", 12, "Act 12: the boat; drowned by the lake (reload); rowed across; the station door", At(12), 260, Act12),
			new("13a", 13, "Act 13: the knife, the tape, the basement wheel, the clock's key, Room 1's box and button", At(13), 170, Act13a),
			new("13b", 13, "Act 13: Room 2 flooded (reload), the cryptex, the lighter, the web, the step, the hand, the eye, the iron door", At(Checkpoint.Act13Room1Solved, room1Solved, Gear + ";tools=Knife"), 240, Act13b),
			new("14", 14, "Act 14: the stairwell, the flame dies, the gap: across; Continue; down", At(14), 200, Act14),
			new("15", 15, "Act 15: the stalker in the hall, red light green light (taken: reload), the closet", At(15), 260, Act15),
			new("16", 16, "Act 16: the closet's switch and door, into the sewer", At(16), 80, Act16),
			new("17", 17, "Act 17: the sewer: wading, the hole: no, yes-no, yes-yes", At(17), 110, Act17),
			new("18", 18, "Act 18: the pit: crushed by a slam (reload), ten valves, the door", At(18), 300, Act18),
			new("19", 19, "Act 19: the library: the journal by blacklight, the sheet, the puzzle box, the bookmark, the bookcase", At(19), 140, Act19),
			new("20", 20, "Act 20: the round room: the webs burned, six switches, a photo, the dais up", At(20), 150, Act20),
			new("21", 21, "Act 21: the long stair, the hatch, four candles, the vestry key, the font, the chalice, the great door", At(21), 200, Act21),
			ContinueBeat("21c", 21, "act21_church"),
			new("22", 22, "Act 22: the winter woods: the compass back, the wendigo, the front doors chained, the back door forced", At(22), 160, Act22),
			new("23a", 23, "Act 23: the bar (blacklight card), the curio, 202 by keycard, its drawers, the pantry key", At(23), 160, Act23a),
			new("23b", 23, "Act 23: the pantry (the mop), 203's card, 203, the dining key, 204's note", At(Checkpoint.Act23Room202Done, ContinueRoundTripTest.F23a, Gear + ";tools=Lighter+PantryKey"), 170, Act23b),
			new("23c", 23, "Act 23: the dining hall's six sheets, 201's card from the bowl", At(Checkpoint.Act23Room203Done, ContinueRoundTripTest.F23b, Gear + ";tools=Lighter+DiningKey"), 170, Act23c),
			new("23d", 23, "Act 23: 201's letter, the bath's red switch, the crawlspace (arms, pipes), the wardrobe", At(Checkpoint.Act23Keycard201, ContinueRoundTripTest.F23c, Gear + ";tools=Lighter+Keycard201"), 220, Act23d),
			ContinueBeat("23x", 23, "act23_letter201"),
			ContinueBeat("23y", 23, "act23_crawlspace"),
			new("23e", 23, "Act 23: the frozen lodge's front doors, the wendigo off the balcony and through them", At(Checkpoint.Act23Frozen, ContinueRoundTripTest.F23f, Gear + ";tools=Lighter+Keycard201"), 130, Act23e),
			new("24a", 24, "Act 24: the drift's hole, its senses, flares and a hollow, the cavern, crowbar, crate, flamethrower, four burns", At(24), 320, Act24),
			new("24b", 24, "Act 24: caught by the wendigo in the maze (reload)", At(Checkpoint.Act24Maze, maze, Gear + ";tools=Lighter+Keycard201"), 110, Act24Death),
			ContinueBeat("24c", 24, "act24_trenches"),
			ContinueBeat("24d", 24, "act24_flamethrower"),
			new("25-1", 25, "Act 25: the endings' table; ending 1 (the trunk), the credits, the polaroids, the menu", Ending(1), 200, ct => Act25End(1, ct)),
			new("25-2", 25, "Act 25: ending 2 (the trunk, the howl)", Ending(2), 140, ct => Act25End(2, ct)),
			new("25-3", 25, "Act 25: ending 3 (the car crushed, the stalker)", Ending(3), 140, ct => Act25End(3, ct)),
			new("25-4", 25, "Act 25: ending 4 (the stairs where the car was)", Ending(4), 140, ct => Act25End(4, ct)),
			new("25-5", 25, "Act 25: ending 5 (the car, the engine)", Ending(5), 140, ct => Act25End(5, ct)),
			new("25c", 25, "After the end: Continue finds them by the car", At(Checkpoint.GameFinished, StoryTest.StateFor(24).flags.Concat(new[] { Act24Maze.FlagCrowbar, Act24Maze.FlagCrate, "ending_1" }), Gear + ";tools=Lighter+Keycard201+Crowbar+Flamethrower"), 60, AfterTheEnd),
		};
	}
}
