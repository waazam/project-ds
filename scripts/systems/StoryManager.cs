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

	/// <summary>True for one scene load: GameFlow should place the player from the saved data, not the spawn marker.</summary>
	public bool HasPendingContinue { get; private set; }

	private SaveData _continueData;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public void StartNewGame()
	{
		Current = Checkpoint.None;
		StairsClimbed = false;
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
