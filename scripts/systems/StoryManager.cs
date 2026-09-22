using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.Systems;

/// <summary>
/// Autoload. Owns the story state and drives the New Game / Continue flow.
///
/// Story state = the checkpoint reached plus a set of named flags (see
/// <see cref="Flag"/>) plus the carried inventory. All of it is saved, so a
/// Continue restores exactly what had happened, not just where the player stood.
///
/// Restore contract: every stateful system (door, fire, storm, bunker, mood,
/// clearing, pickups...) must, in its own _Ready (or deferred from it), read
/// <see cref="Current"/> and <see cref="HasFlag"/> and put itself into the state
/// the story has reached. A fresh game simply reads checkpoint None and no flags.
///
/// Gameplay scripts call <see cref="ReachCheckpoint"/> at story beats and
/// <see cref="SetFlag"/> for finer state; both save immediately.
/// </summary>
public partial class StoryManager : Node
{
	public static StoryManager Instance { get; private set; }

	public const string LevelScene = "res://scenes/levels/trail_slice.tscn";
	public const string MenuScene = "res://scenes/ui/main_menu.tscn";

	/// <summary>Well-known flag names. Add new ones here; never rename one once saves exist.</summary>
	public static class Flag
	{
		public const string StairsClimbed = "stairs_climbed";
		public const string GiantEventDone = "giant_event_done";
		public const string NewelPostTaken = "newel_post_taken";
		public const string ClearingVoiceHeard = "clearing_voice_heard";
		public const string CrtPuzzleDone = "crt_puzzle_done";
		public const string Act11DialogueDone = "act11_dialogue_done";
		/// <summary>Act 3: the player left the cabin's safe zone with lantern + compass and the storm began.</summary>
		public const string StormStarted = "storm_started";
		/// <summary>Act 5: the newel post was carried outside: the storm broke and dawn came up.</summary>
		public const string DawnBroke = "dawn_broke";
		/// <summary>Act 5: the boarded door was chopped or pried open.</summary>
		public const string CabinDoorOpen = "cabin_door_open";
		/// <summary>Act 6: night has fallen (the optional extended climb, or the gradual fallback).</summary>
		public const string Act6NightFell = "act6_night_fell";
		/// <summary>Act 6: the optional extended climb happened (the mini stairs are gone, the original is taller).</summary>
		public const string Act6ExtendedClimb = "act6_extended_climb";
		/// <summary>Act 10: the hallway has turned into the maze (re-entering the bunker lands in the maze).</summary>
		public const string BunkerMazeEntered = "bunker_maze_entered";
		/// <summary>Act 10: the maze's end was reached and the walkie-talkie dropped there.</summary>
		public const string BunkerMazeExited = "bunker_maze_exited";
		// World pickups already taken (Pickup.TakenFlag = "pickup_taken_" + kind, lower case):
		// a taken item never reappears on Continue. The newel post uses NewelPostTaken instead.
		public const string PickupTakenLantern = "pickup_taken_lantern";
		public const string PickupTakenCompass = "pickup_taken_compass";
		public const string PickupTakenCamera = "pickup_taken_camera";
		public const string PickupTakenAxe = "pickup_taken_axe";
		public const string PickupTakenKey = "pickup_taken_key";
		public const string PickupTakenHammer = "pickup_taken_hammer";
		// Act 1 photo log: one flag per photographed subject ("photo_" + subject id). See PhotoLog.
		public const string PhotoPrefix = "photo_";
		public static string Photo(string subjectId) => PhotoPrefix + subjectId;
	}

	/// <summary>Raised after a checkpoint is reached (and saved). Triggers use it to re-check a waiting condition.</summary>
	public event System.Action<Checkpoint> CheckpointReached;
	/// <summary>Raised after a new flag is set (and saved).</summary>
	public event System.Action<string> FlagSet;

	public Checkpoint Current { get; private set; } = Checkpoint.None;

	private readonly HashSet<string> _flags = new();
	public bool HasFlag(string flag) => _flags.Contains(flag);
	public IEnumerable<string> Flags => _flags;

	public bool StairsClimbed => HasFlag(Flag.StairsClimbed);
	public bool GiantEventDone => HasFlag(Flag.GiantEventDone);
	public bool NewelPostTaken => HasFlag(Flag.NewelPostTaken);
	public bool ClearingVoiceHeard => HasFlag(Flag.ClearingVoiceHeard);
	public bool CrtPuzzleDone => HasFlag(Flag.CrtPuzzleDone);
	public bool Act11DialogueDone => HasFlag(Flag.Act11DialogueDone);

	public void MarkGiantEventDone() => SetFlag(Flag.GiantEventDone);
	public void MarkNewelPostTaken() => SetFlag(Flag.NewelPostTaken);
	public void MarkClearingVoiceHeard() => SetFlag(Flag.ClearingVoiceHeard);
	public void MarkCrtPuzzleDone() => SetFlag(Flag.CrtPuzzleDone);
	public void MarkAct11DialogueDone() => SetFlag(Flag.Act11DialogueDone);

	/// <summary>True for the level loaded by Continue (systems may use it to skip intro-only effects).</summary>
	public bool LoadedFromSave { get; private set; }

	/// <summary>Inventory to restore into the player's PlayerInventory on level load (null on a fresh game).</summary>
	public string PendingInventory { get; private set; }

	/// <summary>
	/// Where the compass points: the stairs while still searching for them, the cabin once the giant
	/// has been seen, the bridge once the newel post is in hand, the Act 6 clearing once the bridge is
	/// crossed, back to the cabin once the clearing's voice has spoken, the bunker once the cabin is
	/// seen burning, the CRT room's marked screen once inside the bunker, and back toward the bunker's
	/// own entrance once the screens have shown the stairs.
	/// </summary>
	public Vector3? ObjectivePosition
	{
		get
		{
			if (Current < Checkpoint.Act3DoorBoarded) return null;
			if (Current < Checkpoint.Act5CabinEntered)
				return MarkerPos(GiantEventDone ? "cabin" : "stairs_top_trigger");
			if (!NewelPostTaken) return MarkerPos("cabin");
			if (Current < Checkpoint.Act6BridgeCrossed) return MarkerPos("bridge_marker");
			if (!ClearingVoiceHeard) return MarkerPos("stairs_clearing_marker");
			if (Current < Checkpoint.Act7CabinBurning) return MarkerPos("cabin");
			if (Current < Checkpoint.Act8BunkerEntered) return MarkerPos("bunker_marker");
			if (!CrtPuzzleDone) return MarkerPos("crt_target_marker");
			if (Current < Checkpoint.Act10WalkieFound) return MarkerPos("bunker_entrance_marker");
			if (Current < Checkpoint.Act11GiantEncounter)
				return Act11DialogueDone ? MarkerPos("stairs_clearing_marker") : null;   // mid-exchange outside the bunker
			return null;
		}
	}

	// Marker lookups are cached per group; the cache is dropped whenever the scene changes.
	private readonly Dictionary<string, Node3D> _markers = new();
	private Vector3? MarkerPos(string group)
	{
		if (!_markers.TryGetValue(group, out var n) || !IsInstanceValid(n) || !n.IsInsideTree())
			_markers[group] = n = GetTree().GetFirstNodeInGroup(group) as Node3D;
		return n?.GlobalPosition;
	}

	private SaveData _continueData;
	private Vector3 _lastPos;
	private float _lastYaw;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public void StartNewGame()
	{
		Current = Checkpoint.None;
		_flags.Clear();
		LoadedFromSave = false;
		PendingInventory = null;
		_continueData = null;
		ChangeScene(LevelScene);
	}

	/// <summary>False if there was no valid save to continue from.</summary>
	public bool ContinueGame()
	{
		var data = SaveSystem.Load();
		if (data == null) return false;
		_continueData = data;
		Current = data.Checkpoint;
		_flags.Clear();
		foreach (var f in data.Flags) _flags.Add(f);
		if (data.Checkpoint >= Checkpoint.Act2StairsClimbed) _flags.Add(Flag.StairsClimbed);
		PendingInventory = data.Inventory;
		LoadedFromSave = true;
		_lastPos = new Vector3(data.PosX, data.PosY, data.PosZ);
		_lastYaw = data.Yaw;
		ChangeScene(LevelScene);
		return true;
	}

	/// <summary>True for one scene load: GameFlow should place the player from the saved data, not the spawn marker.</summary>
	public bool HasPendingContinue => _continueData != null;

	/// <summary>GameFlow calls this once at level start; returns null if this is a fresh run.</summary>
	public SaveData ConsumeContinueData()
	{
		var d = _continueData;
		_continueData = null;
		return d;
	}

	/// <summary>PlayerInventory calls this once after restoring from <see cref="PendingInventory"/>.</summary>
	public void ClearPendingInventory() => PendingInventory = null;

	public void ReturnToMenu() => ChangeScene(MenuScene);

	/// <summary>Every scene change goes through here: the pause menu's pause must never survive it.</summary>
	private void ChangeScene(string path)
	{
		GetTree().Paused = false;
		_markers.Clear();
		// Deferred: this is often called from _Ready(), while the tree is still busy adding the caller.
		GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, path);
	}

	/// <summary>Advances the story and checkpoint-saves. Never moves the checkpoint backwards.</summary>
	public void ReachCheckpoint(Checkpoint cp, Vector3 pos, float yaw)
	{
		if (cp <= Current) return;
		Current = cp;
		if (cp >= Checkpoint.Act2StairsClimbed) _flags.Add(Flag.StairsClimbed);
		_lastPos = pos;
		_lastYaw = yaw;
		Save();
		GD.Print($"[story] checkpoint reached: {cp}");
		CheckpointReached?.Invoke(cp);
	}

	/// <summary>Records a story flag and saves (keeping the last checkpoint's position).</summary>
	public void SetFlag(string flag)
	{
		if (!_flags.Add(flag)) return;
		if (Current > Checkpoint.None) Save();
		GD.Print($"[story] flag set: {flag}");
		FlagSet?.Invoke(flag);
	}

	/// <summary>Writes the current story state (checkpoint, flags, inventory) to the save slots.</summary>
	public void Save()
	{
		if (Current == Checkpoint.None) return;
		var inv = (GetTree().GetFirstNodeInGroup("player") as PlayerController)?.Inventory;
		SaveSystem.Save(new SaveData
		{
			Version = SaveData.CurrentVersion,
			Checkpoint = Current,
			PosX = _lastPos.X, PosY = _lastPos.Y, PosZ = _lastPos.Z, Yaw = _lastYaw,
			Flags = _flags.ToArray(),
			Inventory = inv?.Serialize() ?? PendingInventory ?? "",
		});
	}
}
