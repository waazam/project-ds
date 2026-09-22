using Godot;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// Boots a level: places the player (at the checkpoint's safe respawn point if
/// continuing, otherwise the scene's "player_spawn" marker), restores the
/// story-wide lighting mood, plays the opening fade, then hands control to the
/// player (through the reference-counted <see cref="Cutscene"/> lock). Story beats (the stairs, the cabin) advance <see cref="StoryManager"/>
/// directly; this node only gets the game started.
///
/// It also registers the shared story nodes into their groups ("screen_fader",
/// "atmosphere", "post_screen") so beats find them without searching the tree.
/// </summary>
public partial class GameFlow : Node
{
	[Export] public NodePath PlayerPath = "../Player";
	[Export] public NodePath FaderPath = "../ScreenFader";
	[Export] public NodePath PauseMenuPath = "../PauseMenu";
	[Export] public NodePath AtmospherePath = "../ForestWorld/Atmosphere";
	[Export] public NodePath PostScreenPath = "../Hud/PostProcess/Screen";

	public bool Started { get; private set; }
	/// <summary>The current level's GameFlow (null between levels).</summary>
	public static GameFlow Instance { get; private set; }

	private PlayerController _player;
	private ScreenFader _fader;
	private PauseMenu _pause;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		_player = GetNode<PlayerController>(PlayerPath);
		_fader = GetNode<ScreenFader>(FaderPath);
		_pause = GetNode<PauseMenu>(PauseMenuPath);
		_fader.AddToGroup("screen_fader");
		GetNodeOrNull(AtmospherePath)?.AddToGroup("atmosphere");
		GetNodeOrNull(PostScreenPath)?.AddToGroup("post_screen");
		_fader.SetBlack(true);
		// One reference on the cutscene lock until the opening fade hands control over (Begin), so
		// any beat that locks during the fade-in still counts correctly.
		Cutscene.Lock(_player, input: true);
		// Deferred: every stateful system restores itself deferred from its own _Ready first
		// (they sit earlier in the tree), then the player is placed into that restored world.
		Callable.From(Begin).CallDeferred();
		if (GameSettings.Instance.ContinueTest) AddChild(new ContinueRoundTripTest());
		else if (GameSettings.Instance.AutoTest) AddChild(new AutoTest());
	}

	private void Begin()
	{
		var save = StoryManager.Instance?.ConsumeContinueData();
		if (save != null)
		{
			var (pos, yaw) = RespawnPoints.For(this, save);
			_player.Teleport(pos, yaw);
			// The lighting the story had reached, applied at once (the fade-in hides the snap).
			var mood = StoryBeat.ExpectedMood(StoryManager.Instance);
			if (mood != ForestAtmosphere.Mood.Auto) StoryBeat.SetMood(this, mood, 0.05f);
		}
		else if (GetTree().GetFirstNodeInGroup("player_spawn") is Node3D spawn)
		{
			var fwd = -spawn.GlobalBasis.Z;
			_player.Teleport(spawn.GlobalPosition + Vector3.Up * 0.1f, Mathf.Atan2(-fwd.X, -fwd.Z));
		}
		else GD.PushWarning("GameFlow: no node in group 'player_spawn'");

		bool quick = GameSettings.Instance.AutoTest;
		if (!quick) Input.MouseMode = Input.MouseModeEnum.Captured;
		_ = Cutscene.Run(this, async ct =>
		{
			Cutscene.Unlock(_player, input: true);
			Started = true;
			await _fader.Fade(0f, quick ? 0.2f : 1.2f, ct);
			StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act1Start, _player.GlobalPosition, _player.CameraRig.Yaw);
		});
	}
}
