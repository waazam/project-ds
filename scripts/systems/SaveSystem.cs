using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// Story checkpoints, in order reached. Never renumber existing values once a save exists: the
/// numbers are what the save files and the test logs carry ("checkpoint 4" = Act 5, the friend found).
/// </summary>
public enum Checkpoint
{
	None = 0,
	Act1Start = 1,
	Act2StairsClimbed = 2,
	Act3DoorBoarded = 3,
	Act5CabinEntered = 4,      // the cabin entered, the friend found
	Act6BridgeCrossed = 5,
	Act7CabinBurning = 6,
	Act8BunkerEntered = 7,
	Act10WalkieFound = 8,
	Act11GiantEncounter = 9,
	Act12LakeCrossed = 10,
	/// <summary>The first Act 13's ending (a coin pedestal). Kept so saves that reached it still load;
	/// nothing reaches it any more.</summary>
	Act13StationSolved = 11,
	/// <summary>Act 13: Room 1's button pressed - saved just before the flooding room.</summary>
	Act13Room1Solved = 12,
	/// <summary>Act 13's end, and Act 14's start: through the iron door into Room 3.</summary>
	Act13Finished = 13,
	/// <summary>Act 14's end: down the stairwell, jumped (across or down), on the floor of the chamber
	/// under it, where Act 15 begins.</summary>
	Act14Finished = 14,
	/// <summary>Act 15's end: through the long hallway, into the janitor's closet (Act 16's start).</summary>
	Act15Finished = 15,
	/// <summary>Act 16's end: through the closet door into the black, and out into the sewer (Act 17's start).</summary>
	Act16Finished = 16,
	/// <summary>Act 17's end: yes, and yes, down the square hole in the sewer's great room (Act 18's start).</summary>
	Act17Finished = 17,
	/// <summary>Act 18's end: the thing in the pit dead, through the far door into the clean, lit room (Act 19's start).</summary>
	Act18Finished = 18,
	/// <summary>Act 19's end: the puzzle box solved, through the library's secret bookcase into the round room (Act 20's start).</summary>
	Act19Finished = 19,
	/// <summary>Act 20's end: the webs burned, the dais risen through the ceiling into the room above (the end of the demo).</summary>
	Act20Finished = 20,
	/// <summary>Act 21: up the long stair, through the hatch into the church's crypt, back on the surface (a new save).</summary>
	Act21ChurchReached = 21,
	/// <summary>Act 21's end: the four candles lit, the vestry's key, the font opened, the chalice set in the great door, and
	/// the great door open onto the snow (Act 22's start, outside it).</summary>
	Act21Finished = 22,
	/// <summary>Act 22's end: down the plowed road through the winter woods, round the ski lodge, its iced-in back door forced.</summary>
	Act22Finished = 23,
	/// <summary>Act 23: out of room 202 with the pantry key; its door slammed and jammed behind them.</summary>
	Act23Room202Done = 24,
	/// <summary>Act 23: out of rooms 203 and 204 with the dining room's key and 204's note; 203's door jammed too.</summary>
	Act23Room203Done = 25,
	/// <summary>Act 23: the dining hall's sixth sheet off, 201's keycard taken from the bowl of snow (and the snow gone to blood).</summary>
	Act23Keycard201 = 26,
	/// <summary>Act 23: in room 201, the letter read ("Welcome Back"), the door slammed shut behind them for good.</summary>
	Act23Letter201 = 27,
	/// <summary>Act 23: halfway through the crawlspace between the walls (before the arms come through).</summary>
	Act23Crawlspace = 28,
	/// <summary>Act 23: out of the crawlspace, the wardrobe pushed over, back in the dining hall (the lodge frozen over).</summary>
	Act23Frozen = 29,
	/// <summary>Act 23's end: the front door, the wendigo off the balcony and out through it; at the splintered doorway.</summary>
	Act23Finished = 30,
	/// <summary>Act 24: down the hole in the drift in front of the lodge, into the snow maze.</summary>
	Act24Maze = 31,
	/// <summary>Act 24: through the maze to its heart, the cavern and its trenches.</summary>
	Act24Trenches = 32,
	/// <summary>Act 24: the crate prised open, the flamethrower in hand.</summary>
	Act24Flamethrower = 33,
	/// <summary>Act 24's end: the fourth burn, the wendigo finished; Act 25 (out, and the ending) in the trailhead level.</summary>
	Act24Finished = 34,
	/// <summary>The end of the story: the last act done, the credits rolled (GameEnding). Continue puts them back where it
	/// ended, free to look round. Held far above the acts' own numbers, so the acts still to come number in before it
	/// and every "at least this far" test stays in story order.</summary>
	GameFinished = 1000,
}

public class SaveData
{
	/// <summary>Bump when the layout changes and a loader needs to migrate; saves without the key read as 0.</summary>
	public const int CurrentVersion = 1;
	public int Version = CurrentVersion;
	public Checkpoint Checkpoint;
	public float PosX, PosY, PosZ, Yaw;
	public string[] Flags = System.Array.Empty<string>();
	public string Inventory = "";
	/// <summary>The game's record, for the endings (Act 25): deaths in all, and seconds played.</summary>
	public int Deaths;
	public double PlaySeconds;
}

/// <summary>
/// Checkpoint saves only (no manual save). Writes alternate between two slots
/// so a crash or power loss mid-write leaves the other slot intact; Load()
/// picks whichever valid slot was written most recently.
/// </summary>
public static class SaveSystem
{
	// Test runs write their own slots so they never overwrite a real save.
	private static string Prefix => GameSettings.Instance?.AutoTest == true || GameSettings.Instance?.Trailer == true ? "user://test_save_" : "user://save_";
	private static string PathA => Prefix + "a.cfg";
	private static string PathB => Prefix + "b.cfg";

	public static bool HasSave() => Load() != null;

	public static void Save(SaveData data)
	{
		string target = NewestSlot() == PathA ? PathB : PathA;
		var cfg = new ConfigFile();
		cfg.SetValue("save", "version", data.Version);
		cfg.SetValue("save", "checkpoint", (int)data.Checkpoint);
		cfg.SetValue("save", "pos_x", data.PosX);
		cfg.SetValue("save", "pos_y", data.PosY);
		cfg.SetValue("save", "pos_z", data.PosZ);
		cfg.SetValue("save", "yaw", data.Yaw);
		cfg.SetValue("save", "flags", string.Join(",", data.Flags));
		cfg.SetValue("save", "inventory", data.Inventory ?? "");
		cfg.SetValue("save", "deaths", data.Deaths);
		cfg.SetValue("save", "play_seconds", data.PlaySeconds);
		cfg.SetValue("save", "saved_at", Time.GetUnixTimeFromSystem());
		var err = cfg.Save(target);
		if (err != Error.Ok) GD.PushError($"SaveSystem: failed to write {target}: {err}");
	}

	public static SaveData Load()
	{
		var a = TryLoad(PathA, out double ta);
		var b = TryLoad(PathB, out double tb);
		if (a == null) return b;
		if (b == null) return a;
		return ta >= tb ? a : b;
	}

	/// <summary>Path of whichever slot currently holds the newest valid save (or PathA if neither parses).</summary>
	private static string NewestSlot()
	{
		TryLoad(PathA, out double ta);
		TryLoad(PathB, out double tb);
		return tb > ta ? PathB : PathA;
	}

	private static SaveData TryLoad(string path, out double savedAt)
	{
		savedAt = -1;
		if (!FileAccess.FileExists(path)) return null;
		var cfg = new ConfigFile();
		if (cfg.Load(path) != Error.Ok) return null;
		if (!cfg.HasSectionKey("save", "checkpoint")) return null;
		savedAt = (double)cfg.GetValue("save", "saved_at", -1.0);
		return new SaveData
		{
			Version = (int)cfg.GetValue("save", "version", 0),
			Checkpoint = (Checkpoint)(int)cfg.GetValue("save", "checkpoint", 0),
			PosX = (float)cfg.GetValue("save", "pos_x", 0f),
			PosY = (float)cfg.GetValue("save", "pos_y", 0f),
			PosZ = (float)cfg.GetValue("save", "pos_z", 0f),
			Yaw = (float)cfg.GetValue("save", "yaw", 0f),
			Flags = ((string)cfg.GetValue("save", "flags", "")).Split(',', System.StringSplitOptions.RemoveEmptyEntries),
			Inventory = (string)cfg.GetValue("save", "inventory", ""),
			Deaths = (int)cfg.GetValue("save", "deaths", 0),
			PlaySeconds = (double)cfg.GetValue("save", "play_seconds", 0.0),
		};
	}
}
