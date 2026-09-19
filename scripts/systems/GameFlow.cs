using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.UI;

namespace ProjectDS.Systems;

/// <summary>
/// Runs the vertical slice from start to finish: spawn the player, play the
/// opening card, and watch the top of the stairs. Standing up there for a
/// moment ends the slice.
/// </summary>
public partial class GameFlow : Node
{
	[Export] public NodePath PlayerPath = "../Player";
	[Export] public NodePath FaderPath = "../ScreenFader";
	[Export] public NodePath PauseMenuPath = "../PauseMenu";
	[Export] public string OpeningTitle = "";
	[Export] public string OpeningSubtitle = "";
	[Export] public float StairsTopHoldSeconds = 2.5f;

	public bool Started { get; private set; }
	public bool EndReached { get; private set; }

	private PlayerController _player;
	private ScreenFader _fader;
	private PauseMenu _pause;
	private bool _onTop;
	private float _topTimer;
	private bool _waitingForKey;

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
		if (GetTree().GetFirstNodeInGroup("player_spawn") is Node3D spawn)
		{
			var fwd = -spawn.GlobalBasis.Z;
			_player.Teleport(spawn.GlobalPosition + Vector3.Up * 0.1f, Mathf.Atan2(-fwd.X, -fwd.Z));
		}
		else GD.PushWarning("GameFlow: no node in group 'player_spawn'");

		foreach (var node in GetTree().GetNodesInGroup("stairs_top_trigger"))
		{
			if (node is not Area3D area) continue;
			area.BodyEntered += b => { if (b == _player) _onTop = true; };
			area.BodyExited += b => { if (b == _player) { _onTop = false; _topTimer = 0; } };
		}

		bool quick = GameSettings.Instance.AutoTest;
		if (!quick) Input.MouseMode = Input.MouseModeEnum.Captured;
		if (!quick && OpeningTitle != "")
			await _fader.ShowCaption(OpeningTitle, OpeningSubtitle, 1.5f, 2.5f, 1.2f);
		_player.PlayerInput.SetEnabled(true);
		Started = true;
		await _fader.Fade(0f, quick ? 0.2f : 1.2f);
	}

	public override void _Process(double delta)
	{
		if (!Started || EndReached || !_onTop) return;
		_topTimer += (float)delta;
		if (_topTimer >= StairsTopHoldSeconds) _ = End();
	}

	private async Task End()
	{
		EndReached = true;
		_pause.Locked = true;
		_player.PlayerInput.SetEnabled(false);
		if (ForestAmbienceManager.Instance != null) ForestAmbienceManager.Instance.SilenceOverride = 1f;
		await _fader.Fade(1f, 4f);
		await ToSignal(GetTree().CreateTimer(GameSettings.Instance.AutoTest ? 0.5 : 2.5), SceneTreeTimer.SignalName.Timeout);
		await _fader.ShowCaption("PROJECT DS", "end of the first slice  -  press any key", 2f, -1f, 0f);
		_waitingForKey = true;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (!_waitingForKey) return;
		if (e is InputEventKey { Pressed: true } or InputEventJoypadButton { Pressed: true } or InputEventMouseButton { Pressed: true })
			GetTree().ReloadCurrentScene();
	}
}
