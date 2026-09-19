using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// Autoload. Owns which checkpoint the story has reached and drives the
/// New Game / Continue flow. Gameplay scripts call <see cref="ReachCheckpoint"/>
/// when the player passes a story beat (finding the stairs, reaching the
/// boarded cabin); GameFlow reads <see cref="ConsumeContinueData"/> once, on
/// scene load, to know where to place the player when continuing a save.
/// </summary>
public partial class StoryManager : Node
{
	public static StoryManager Instance { get; private set; }

	public const string LevelScene = "res://scenes/levels/trail_slice.tscn";
	public const string MenuScene = "res://scenes/ui/main_menu.tscn";

	public Checkpoint Current { get; private set; } = Checkpoint.None;
	public bool StairsClimbed { get; private set; }
	/// <summary>Runtime only, never saved: the Act 4 set-piece fires once per attempt, then the compass repoints home.</summary>
	public bool GiantEventDone { get; private set; }
	/// <summary>Runtime only: the newel post found on the friend's table has been taken (Act 5 → 6 handoff).</summary>
	public bool NewelPostTaken { get; private set; }
	/// <summary>Runtime only: the clearing's voice line has played and the post has fused onto a staircase.</summary>
	public bool ClearingVoiceHeard { get; private set; }

	/// <summary>
	/// Where the compass points: the stairs while still searching for them, the cabin once the giant
	/// has been seen, the bridge once the newel post is in hand, the Act 6 clearing once the bridge is
	/// crossed, and back to the cabin once the clearing's voice has spoken.
	/// </summary>
	public Vector3? ObjectivePosition
	{
		get
		{
			if (Current < Checkpoint.Act3DoorBoarded) return null;
			if (Current < Checkpoint.Act5CabinEntered)
			{
				if (!GiantEventDone)
					return GetTree().GetFirstNodeInGroup("stairs_top_trigger") is Node3D top ? top.GlobalPosition : null;
				return GetTree().GetFirstNodeInGroup("cabin") is Node3D cabin ? cabin.GlobalPosition : null;
			}
			if (!NewelPostTaken)
				return GetTree().GetFirstNodeInGroup("cabin") is Node3D cabinFriend ? cabinFriend.GlobalPosition : null;
			if (Current < Checkpoint.Act6BridgeCrossed)
				return GetTree().GetFirstNodeInGroup("bridge_marker") is Node3D bridge ? bridge.GlobalPosition : null;
			if (!ClearingVoiceHeard)
				return GetTree().GetFirstNodeInGroup("stairs_clearing_marker") is Node3D clearing ? clearing.GlobalPosition : null;
			return GetTree().GetFirstNodeInGroup("cabin") is Node3D cabinReturn ? cabinReturn.GlobalPosition : null;
		}
	}

	public void MarkGiantEventDone() => GiantEventDone = true;
	public void MarkNewelPostTaken() => NewelPostTaken = true;
	public void MarkClearingVoiceHeard() => ClearingVoiceHeard = true;

	/// <summary>True for one scene load: GameFlow should place the player from the saved data, not the spawn marker.</summary>
	public bool HasPendingContinue { get; private set; }

	private SaveData _continueData;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public void StartNewGame()
	{
		Current = Checkpoint.None;
		StairsClimbed = false;
		GiantEventDone = false;
		NewelPostTaken = false;
		ClearingVoiceHeard = false;
		HasPendingContinue = false;
		_continueData = null;
		// Deferred: this is often called from _Ready(), while the tree is still busy adding the caller.
		GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, LevelScene);
	}

	/// <summary>False if there was no valid save to continue from.</summary>
	public bool ContinueGame()
	{
		var data = SaveSystem.Load();
		if (data == null) return false;
		_continueData = data;
		HasPendingContinue = true;
		Current = data.Checkpoint;
		StairsClimbed = data.StairsClimbed;
		GiantEventDone = false;
		NewelPostTaken = false;
		ClearingVoiceHeard = false;
		GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, LevelScene);
		return true;
	}

	/// <summary>GameFlow calls this once at level start; returns null if this is a fresh run.</summary>
	public SaveData ConsumeContinueData()
	{
		HasPendingContinue = false;
		var d = _continueData;
		_continueData = null;
		return d;
	}

	public void ReturnToMenu() => GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MenuScene);

	/// <summary>Advances the story and checkpoint-saves. Never moves the checkpoint backwards.</summary>
	public void ReachCheckpoint(Checkpoint cp, Vector3 pos, float yaw)
	{
		if (cp <= Current) return;
		Current = cp;
		if (cp >= Checkpoint.Act2StairsClimbed) StairsClimbed = true;
		SaveSystem.Save(new SaveData
		{
			Checkpoint = cp,
			PosX = pos.X, PosY = pos.Y, PosZ = pos.Z, Yaw = yaw,
			StairsClimbed = StairsClimbed,
		});
		GD.Print($"[story] checkpoint reached: {cp}");
	}
}
