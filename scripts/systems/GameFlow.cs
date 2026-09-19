using Godot;
using ProjectDS.Player;
using ProjectDS.UI;

namespace ProjectDS.Systems;

/// <summary>
/// Boots a level: places the player (from the save, if continuing, otherwise
/// the scene's "player_spawn" marker), plays the opening fade, then hands
/// control to the player. Story beats (the stairs, the cabin) advance
/// <see cref="StoryManager"/> directly; this node only gets the game started.
/// </summary>
public partial class GameFlow : Node
{
	[Export] public NodePath PlayerPath = "../Player";
	[Export] public NodePath FaderPath = "../ScreenFader";
	[Export] public NodePath PauseMenuPath = "../PauseMenu";
	[Export] public string OpeningTitle = "";
	[Export] public string OpeningSubtitle = "";

	public bool Started { get; private set; }

	private PlayerController _player;
	private ScreenFader _fader;
	private PauseMenu _pause;

	public override void _Ready()
	{
		_player = GetNode<PlayerController>(PlayerPath);
		_fader = GetNode<ScreenFader>(FaderPath);
		_pause = GetNode<PauseMenu>(PauseMenuPath);
		_fader.SetBlack(true);
		_player.PlayerInput.SetEnabled(false);
		CallDeferred(MethodName.Begin);
		if (GameSettings.Instance.AutoTest) AddChild(new AutoTest());
	}

	private async void Begin()
	{
		var save = StoryManager.Instance?.ConsumeContinueData();
		if (save != null)
		{
			_player.Teleport(new Vector3(save.PosX, save.PosY, save.PosZ), save.Yaw);
		}
		else if (GetTree().GetFirstNodeInGroup("player_spawn") is Node3D spawn)
		{
			var fwd = -spawn.GlobalBasis.Z;
			_player.Teleport(spawn.GlobalPosition + Vector3.Up * 0.1f, Mathf.Atan2(-fwd.X, -fwd.Z));
		}
		else GD.PushWarning("GameFlow: no node in group 'player_spawn'");

		bool quick = GameSettings.Instance.AutoTest;
		if (!quick) Input.MouseMode = Input.MouseModeEnum.Captured;
		if (!quick && OpeningTitle != "")
			await _fader.ShowCaption(OpeningTitle, OpeningSubtitle, 1.5f, 2.5f, 1.2f);
		_player.PlayerInput.SetEnabled(true);
		Started = true;
		await _fader.Fade(0f, quick ? 0.2f : 1.2f);

		StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act1Start, _player.GlobalPosition, _player.CameraRig.Yaw);
	}
}
