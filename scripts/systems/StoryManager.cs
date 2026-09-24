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

	/// <summary>Act 1: the trailhead, the Blackfern trail and the first staircase.</summary>
	public const string TrailheadScene = "res://scenes/levels/trail_slice.tscn";
	/// <summary>Acts 2-11: the hollow the stairs leave the player in, one forward route from the camp to the last staircase.</summary>
	public const string HollowScene = "res://scenes/levels/hollow.tscn";
	/// <summary>Kept for older callers: the level a new game starts in.</summary>
	public const string LevelScene = TrailheadScene;

	/// <summary>The level a checkpoint belongs to: everything from the first climb on happens in the hollow.</summary>
	public static string LevelFor(Checkpoint cp) => cp >= Checkpoint.Act2StairsClimbed ? HollowScene : TrailheadScene;

	/// <summary>True for one level load: the player got here by the stairs letting go of them (Act 2),
	/// not by Continue. GameFlow plays the slow wake instead of the usual fade-in.</summary>
	public bool ArrivedByTravel { get; private set; }
	public void ConsumeArrival() => ArrivedByTravel = false;
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
		/// <summary>Act 5: the newel post was carried outside: the storm broke (the rain stopped). Old name kept for
		/// saves; there is no dawn: the Hollow is one night (Dan, 2026-09-22).</summary>
		public const string DawnBroke = "dawn_broke";
		/// <summary>Act 5: the boarded door was chopped or pried open.</summary>
		public const string CabinDoorOpen = "cabin_door_open";
		/// <summary>Act 6: the clearing loop's fall is behind them (legacy name: it was "night fell"; the Hollow is
		/// night throughout now, so the lighting never depends on this).</summary>
		public const string Act6NightFell = "act6_night_fell";
		/// <summary>Act 6: the clearing's stair loop ended in the fall (the player woke at night on the trail past the clearing).</summary>
		public const string ClearingLoopDone = "clearing_loop_done";
		/// <summary>Legacy (never set since 2026-09-22): the old optional extended climb. Kept so old saves and the
		/// systems preview still read; <see cref="World.StairsState"/> still honours it.</summary>
		public const string Act6ExtendedClimb = "act6_extended_climb";
		/// <summary>Act 7: the bunker door's dial was opened with the survey stakes' code (the door stays open).</summary>
		public const string BunkerUnlocked = "bunker_unlocked";
		/// <summary>Acts 3-7: the n-th (1-4) survey stake's digit has been read under the lantern (the HUD tracker and the dial pre-fill from these).</summary>
		public static string CodeDigit(int n) => $"code_digit_{n}";
		/// <summary>Act 11: the newel cap was put back at the foot of the last staircase (it is whole; the climb followed).</summary>
		public const string NewelSeated = "newel_seated";
		/// <summary>Act 10: the hallway has turned into the maze (re-entering the bunker lands in the maze).</summary>
		public const string BunkerMazeEntered = "bunker_maze_entered";
		/// <summary>Act 9: the dead walkie-talkie was taken off the CRT room's console (it wakes outside).</summary>
		public const string WalkieTaken = "walkie_taken";
		/// <summary>Act 10: the run out of the rooms ended at the round door (the radio woke outside).</summary>
		public const string BunkerMazeExited = "bunker_maze_exited";
		// World pickups already taken (Pickup.TakenFlag = "pickup_taken_" + kind, lower case):
		// a taken item never reappears on Continue. The newel post uses NewelPostTaken instead.
		public const string PickupTakenLantern = "pickup_taken_lantern";
		public const string PickupTakenCompass = "pickup_taken_compass";
		public const string PickupTakenCamera = "pickup_taken_camera";
		public const string PickupTakenAxe = "pickup_taken_axe";
		public const string PickupTakenKey = "pickup_taken_key";
		public const string PickupTakenHammer = "pickup_taken_hammer";
		public const string PickupTakenKnife = "pickup_taken_knife";
		// Act 1 photo log: one flag per photographed subject ("photo_" + subject id). See PhotoLog.
		public const string PhotoPrefix = "photo_";
		public static string Photo(string subjectId) => PhotoPrefix + subjectId;

		/// <summary>Act 13, the forester station: the basement's taped-shut door has been cut open.</summary>
		public const string StationBasementTapeCut = "station_basement_tape_cut";
		/// <summary>Act 13: the flooded basement has been pumped dry (the wheel turned 3 times).</summary>
		public const string StationBasementDrained = "station_basement_drained";
		/// <summary>Act 13: the grandfather clock has chimed, spat out its key and broken apart.</summary>
		public const string StationClockBroken = "station_clock_broken";
		/// <summary>Act 13, room 1: the cut-open cigar box's button has been pressed (the wall writing melts).</summary>
		public const string StationRoom1Solved = "station_room1_solved";
		/// <summary>Act 13, room 2: the cryptex spelled its word; the flood went back out of the window.</summary>
		public const string StationRoom2Solved = "station_room2_solved";
		/// <summary>Act 13: the basement's lights have struggled and died (they stay dead).</summary>
		public const string StationLightsDead = "station_lights_dead";
		/// <summary>Act 13: Room 1's door has been opened with the clock's key.</summary>
		public const string StationRoom1Open = "station_room1_open";
		/// <summary>Act 13: the web over the door behind the desk has been burned away.</summary>
		public const string StationWebBurned = "station_web_burned";
		/// <summary>Act 13: the iron door's three hollows (LOOK / TOUCH / CLIMB) have been read.</summary>
		public const string StationDoor3Seen = "station_door3_seen";
		/// <summary>Act 13: the scavenger pieces taken (the dead eye, the pale hand, the step).</summary>
		public const string StationEyeTaken = "station_eye_taken";
		public const string StationHandTaken = "station_hand_taken";
		public const string StationStepTaken = "station_step_taken";
		/// <summary>Act 13: each piece set in its hollow in the iron door.</summary>
		public const string StationEyeSet = "station_eye_set";
		public const string StationHandSet = "station_hand_set";
		public const string StationStepSet = "station_step_set";
		/// <summary>Act 13: the iron door has opened onto the last room.</summary>
		public const string StationDoor3Open = "station_door3_open";
		/// <summary>Act 14: how they came down at the bottom of the stairwell (Act 15 starts differently).</summary>
		public const string Act14JumpedAcross = "act14_jumped_across";
		public const string Act14JumpedDown = "act14_jumped_down";
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
	/// Where the compass points, along the hollow's one forward route (Acts 3-11): the cabin from the
	/// camp on (through the storm and the giant), the footbridge once the newel post is in hand, the
	/// clearing once the bridge is crossed, the lit staircase of the clearing's loop once the voice has
	/// spoken (until the loop's fall, or until night falls on its own), then the lookout over the cabin,
	/// the bunker once the cabin has been seen burning, the CRT room's marked screen once inside,
	/// back toward the way out once the screens have shown the stairs, and the last staircase once the
	/// radio has spoken. Nothing before the compass is picked up (Acts 1-2) and nothing after the ending.
	/// </summary>
	public Vector3? ObjectivePosition
	{
		get
		{
			if (Current < Checkpoint.Act3DoorBoarded) return null;
			if (Current < Checkpoint.Act5CabinEntered || !NewelPostTaken) return MarkerPos("cabin");
			if (Current < Checkpoint.Act6BridgeCrossed) return MarkerPos("bridge_marker");
			if (!ClearingVoiceHeard) return MarkerPos("stairs_clearing_marker");
			if (Current < Checkpoint.Act7CabinBurning && !HasFlag(Flag.ClearingLoopDone) && !HasFlag(Flag.Act6NightFell))
				return MarkerPos("clearing_loop_marker") ?? MarkerPos("fire_lookout_marker");   // the lit staircase (null only before it is lit)
			if (Current < Checkpoint.Act7CabinBurning) return MarkerPos("fire_lookout_marker");
			if (Current < Checkpoint.Act8BunkerEntered) return MarkerPos("bunker_marker");
			if (!CrtPuzzleDone) return MarkerPos("crt_target_marker");
			if (Current < Checkpoint.Act10WalkieFound && !HasFlag(Flag.WalkieTaken)) return MarkerPos("walkie_marker") ?? MarkerPos("bunker_entrance_marker");   // the dead walkie on the console first
			if (Current < Checkpoint.Act10WalkieFound) return MarkerPos("bunker_entrance_marker");
			if (Current < Checkpoint.Act11GiantEncounter)
				return MarkerPos("final_stairs_marker");   // from the bunker on, the compass leads to the last staircase (Dan, 2026-09-22)
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
		ArrivedByTravel = false;
		ChangeScene(TrailheadScene);
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
		ArrivedByTravel = false;
		_lastPos = new Vector3(data.PosX, data.PosY, data.PosZ);
		_lastYaw = data.Yaw;
		ChangeScene(LevelFor(data.Checkpoint));
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

	/// <summary>
	/// Moves the player on to the level the current checkpoint belongs to (Act 2: the stairs let go of
	/// them and they come to in the hollow). The story is already saved at the checkpoint; the new level
	/// is booted exactly like a Continue (GameFlow places the player at the checkpoint's respawn marker,
	/// every system restores from the story state, the carried gear comes along) with
	/// <see cref="ArrivedByTravel"/> set so GameFlow plays the wake-up instead of the usual fade-in.
	/// </summary>
	public void TravelToCheckpointLevel()
	{
		var inv = (GetTree().GetFirstNodeInGroup("player") as PlayerController)?.Inventory;
		PendingInventory = inv?.Serialize() ?? PendingInventory ?? "";
		_continueData = new SaveData
		{
			Version = SaveData.CurrentVersion,
			Checkpoint = Current,
			PosX = _lastPos.X, PosY = _lastPos.Y, PosZ = _lastPos.Z, Yaw = _lastYaw,
			Flags = _flags.ToArray(),
			Inventory = PendingInventory,
		};
		ArrivedByTravel = true;
		ChangeScene(LevelFor(Current));
	}

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
