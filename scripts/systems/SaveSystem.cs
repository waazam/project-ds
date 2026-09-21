using Godot;

namespace ProjectDS.Systems;

/// <summary>Story checkpoints, in order reached. Never renumber existing values once a save exists.</summary>
public enum Checkpoint
{
	None = 0,
	Act1Start = 1,
	Act2StairsClimbed = 2,
	Act3DoorBoarded = 3,
	Act5CabinEntered = 4,
	Act6BridgeCrossed = 5,
	Act7CabinBurning = 6,
	Act8BunkerEntered = 7,
	Act10WalkieFound = 8,
	Act11GiantEncounter = 9,
}

public class SaveData
{
	public Checkpoint Checkpoint;
	public float PosX, PosY, PosZ, Yaw;
	public string[] Flags = System.Array.Empty<string>();
	public string Inventory = "";
}

/// <summary>
/// Checkpoint saves only (no manual save). Writes alternate between two slots
/// so a crash or power loss mid-write leaves the other slot intact; Load()
/// picks whichever valid slot was written most recently.
/// </summary>
public static class SaveSystem
{
	// Test runs write their own slots so they never overwrite a real save.
	private static string Prefix => GameSettings.Instance?.AutoTest == true ? "user://test_save_" : "user://save_";
	private static string PathA => Prefix + "a.cfg";
	private static string PathB => Prefix + "b.cfg";

	public static bool HasSave() => Load() != null;

	public static void Save(SaveData data)
	{
		string target = NewestSlot() == PathA ? PathB : PathA;
		var cfg = new ConfigFile();
		cfg.SetValue("save", "checkpoint", (int)data.Checkpoint);
		cfg.SetValue("save", "pos_x", data.PosX);
		cfg.SetValue("save", "pos_y", data.PosY);
		cfg.SetValue("save", "pos_z", data.PosZ);
		cfg.SetValue("save", "yaw", data.Yaw);
		cfg.SetValue("save", "flags", string.Join(",", data.Flags));
		cfg.SetValue("save", "inventory", data.Inventory ?? "");
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
			Checkpoint = (Checkpoint)(int)cfg.GetValue("save", "checkpoint", 0),
			PosX = (float)cfg.GetValue("save", "pos_x", 0f),
			PosY = (float)cfg.GetValue("save", "pos_y", 0f),
			PosZ = (float)cfg.GetValue("save", "pos_z", 0f),
			Yaw = (float)cfg.GetValue("save", "yaw", 0f),
			Flags = ((string)cfg.GetValue("save", "flags", "")).Split(',', System.StringSplitOptions.RemoveEmptyEntries),
			Inventory = (string)cfg.GetValue("save", "inventory", ""),
		};
	}
}
