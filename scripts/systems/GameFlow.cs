using System.Threading;
using System.Threading.Tasks;
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
		else if (GameSettings.Instance.AutoTest) AddChild(new StoryTest());
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
		if (StoryManager.Instance is { ArrivedByTravel: true } story)
		{
			story.ConsumeArrival();
			_ = Cutscene.Run(this, ct => WakeUp(quick, ct));
			return;
		}
		// A new game opens at the car: the intro cards, the fade-in at the trunk, the camera in hand.
		if (save == null && GetTree().GetFirstNodeInGroup("opening") is OpeningAtCar opening)
		{
			var (pos, yaw) = opening.StandPose();
			_player.Teleport(pos, yaw);
			_ = Cutscene.Run(this, async ct =>
			{
				Started = true;
				try { await opening.Run(_player, _fader, quick, ct); }
				finally
				{
					if (IsInstanceValid(_fader)) _fader.BlackAlpha = 0f;
					if (IsInstanceValid(_player)) Cutscene.Unlock(_player, input: true);
				}
				if (!IsInstanceValid(_player) || !_player.IsInsideTree()) return;
				StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act1Start, _player.GlobalPosition, _player.CameraRig.Yaw);
				await opening.ShowHowTo(ct);
			});
			return;
		}
		_ = Cutscene.Run(this, async ct =>
		{
			Cutscene.Unlock(_player, input: true);
			Started = true;
			await _fader.Fade(0f, quick ? 0.2f : 1.2f, ct);
			if (!IsInstanceValid(_player) || !_player.IsInsideTree()) return;   // the level is already being left
			StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act1Start, _player.GlobalPosition, _player.CameraRig.Yaw);
		});
	}

	[ExportGroup("Wake-up (arriving from the stairs)")]
	[Export] public float WakeBlackSeconds = 2.5f;
	[Export] public float WakeSeconds = 7f;
	[Export] public float WakePitchDegrees = -60f;
	[Export] public float WakeVignette = 2.4f;
	[Export] public string WakeLine = "";   // was "This isn't the trail." (self-talk removed, Dan 2026-09-22)

	/// <summary>
	/// Act 2's end, in the hollow: the stairs have let go of the player somewhere they have never been.
	/// They come to face down in the dark; their eyes open and fall shut twice, then their sight clears
	/// slowly while their head lifts. Input stays locked throughout (the reference GameFlow took in
	/// _Ready is released only once they are up), then the one line plays.
	/// </summary>
	private async Task WakeUp(bool quick, CancellationToken ct)
	{
		var rig = _player.CameraRig;
		var post = StoryBeat.PostMaterial(this);
		float baseVignette = post != null ? (float)post.GetShaderParameter("vignette") : 0f;
		float levelPitch = rig.Pitch;
		float downPitch = Mathf.DegToRad(WakePitchDegrees);
		rig.SetPitch(downPitch);
		post?.SetShaderParameter("vignette", WakeVignette);
		_fader.SetBlack(true);
		Started = true;
		try
		{
			await Cutscene.Wait(this, quick ? 0.2f : WakeBlackSeconds, ct);
			if (!quick)
			{
				await _fader.Fade(0.55f, 1.4f, ct);
				await _fader.Fade(1f, 1.1f, ct);
				await Cutscene.Wait(this, 0.8f, ct);
				await _fader.Fade(0.35f, 1.6f, ct);
				await _fader.Fade(0.8f, 0.9f, ct);
			}
			float seconds = quick ? 0.4f : WakeSeconds;
			float startAlpha = _fader.BlackAlpha;
			var tween = CreateTween();
			tween.TweenMethod(Callable.From<float>(p =>
			{
				_fader.BlackAlpha = Mathf.Lerp(startAlpha, 0f, p);
				post?.SetShaderParameter("vignette", Mathf.Lerp(WakeVignette, baseVignette, p));
				rig.SetPitch(Mathf.Lerp(downPitch, levelPitch, Mathf.SmoothStep(0.25f, 1f, p)));
			}), 0f, 1f, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			await Cutscene.Tween(this, tween, ct);
		}
		finally
		{
			if (IsInstanceValid(_fader)) _fader.BlackAlpha = 0f;
			post?.SetShaderParameter("vignette", baseVignette);
			if (IsInstanceValid(_player))
			{
				rig.SetPitch(levelPitch);
				Cutscene.Unlock(_player, input: true);
			}
		}
		GD.Print("[story] Act 2: woke in the hollow");
		// No thought on waking (Dan, 2026-09-22: the player does not talk to himself); WakeLine is kept for previews only.
		await Cutscene.Wait(this, 1.0, ct);
	}
}
