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

	public override void _EnterTree() { Instance = this; StoryManager.LoadStage("the level loaded and in the tree"); }
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (StoryManager.Instance is { } sm) sm.CheckpointReached -= LoadAhead;
	}

	private void LoadAhead(Checkpoint _)
	{
		var sm = StoryManager.Instance;
		if (sm == null || !IsInsideTree()) return;
		string here = GetTree().CurrentScene?.SceneFilePath ?? "";
		if (here == StoryManager.TrailheadScene && sm.Current < Checkpoint.Act2StairsClimbed) sm.PreloadLevel(StoryManager.HollowScene);
		else if (here == StoryManager.HollowScene && sm.Current >= Checkpoint.Act24Flamethrower && sm.Current < Checkpoint.Act24Finished) sm.PreloadLevel(StoryManager.TrailheadScene);
	}

	public override void _Ready()
	{
		StoryManager.LoadStage("the level's nodes ready (their own builds done)");
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
		// every shader the level will draw, built while the screen's still black (no first-sight hitches)
		AddChild(new ShaderWarmup { Name = "ShaderWarmup" });
		// Act 25: back in the trailhead after the maze (and after the end)
		if (StoryManager.Instance is { Current: >= Checkpoint.Act24Finished } && GetTree().CurrentScene?.SceneFilePath == StoryManager.TrailheadScene)
			AddChild(new World.SnowMaze.EndingDirector { Name = "EndingDirector" });
		if (GameSettings.Instance.Trailer) AddChild(new TrailerDirector());
		else if (GameSettings.Instance.ContinueTest) AddChild(new ContinueRoundTripTest());
		else if (GameSettings.Instance.PlaytestRun) AddChild(new Playtest { Name = "Playtest" });
		else if (GameSettings.Instance.AutoTest) AddChild(new StoryTest());
		// the song's synth as the cues for finds and progress (Audio.SongCues)
		AddChild(new Audio.SongCues { Name = "SongCues" });
		// where a play-through stalls, written quietly to user://pacing.txt (nothing on screen)
		AddChild(new PacingLog { Name = "PacingLog" });
		AddChild(new UI.SoundCaption { Name = "SoundCaption" });
		AddChild(new Achievements { Name = "Achievements" });
		// the next level read in the background while this one's played (StoryManager.PreloadLevel): the hollow from the
		// trailhead before the stairs; the trailhead from the hollow once the flamethrower's in hand (Act 25 is there)
		if (StoryManager.Instance is { } sm)
		{
			GetTree().CreateTimer(8.0).Timeout += () => { if (IsInstanceValid(this) && IsInsideTree()) LoadAhead(Checkpoint.None); };   // (once the level's own load is done)
			sm.CheckpointReached += LoadAhead;
		}
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
			// (--real-timing: a test run that keeps the real wake's length, for watching the transition)
			bool quickWake = quick && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--real-timing") < 0;
			_ = Cutscene.Run(this, ct => WakeUp(quickWake, ct));
			return;
		}
		// A new game opens on the map of the drive (OpeningAtCar, PrologueMap), dissolving to the trunk: the camera in hand.
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
				UI.ChapterCard.Show(this, 1, 1.0f);
			});
			return;
		}
		_ = Cutscene.Run(this, async ct =>
		{
			await ShaderWarmup.WaitReady(this, ct);
			LogLoad();
			Cutscene.Unlock(_player, input: true);
			Started = true;
			await _fader.Fade(0f, quick ? 0.2f : 1.2f, ct);
			if (!IsInstanceValid(_player) || !_player.IsInsideTree()) return;   // the level is already being left
			bool fresh = StoryManager.Instance is { Current: Checkpoint.None };
			StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act1Start, _player.GlobalPosition, _player.CameraRig.Yaw);
			if (fresh) UI.ChapterCard.Show(this, 1, 2.5f);
		});
	}

	[ExportGroup("Wake-up (arriving from the stairs)")]
	[Export] public float WakeBlackSeconds = 2.5f;
	[Export] public float WakeSeconds = 8f;
	[Export] public float WakePitchDegrees = -60f;
	[Export] public float WakeVignette = 2.4f;
	/// <summary>Black held after the game's title has cleared, before the eyes open (the rain is heard first).</summary>
	[Export] public float WakeAfterTitleSeconds = 1.4f;

	/// <summary>
	/// Act 2's end, in the hollow: the stairs have let go of the player somewhere they have never been.
	/// They come to face down in the dark; their sight comes up out of the black once and clears
	/// slowly while their head lifts. Input stays locked throughout (the reference GameFlow took in
	/// _Ready is released only once they are up), then the player's line (STORY.md Rev 5, PlayerVoice "wake").
	/// The first time, the game's title is up over the load (<see cref="LoadingVeil"/>): the wake waits for it to clear.
	/// </summary>
	/// <summary>How long the level took, from being asked for to ready to fade in (the load overhaul, 2026-10-09).</summary>
	private void LogLoad()
	{
		if (StoryManager.LoadAskedMsec == 0) return;
		GD.Print($"[load] {System.IO.Path.GetFileNameWithoutExtension(StoryManager.LoadAskedFor)} at {StoryManager.Instance?.Current}: {Time.GetTicksMsec() - StoryManager.LoadAskedMsec} ms to play (textures drawn so far: {TexGen.Count}, {TexGen.Ms:0} ms; meshes committed: {World.MeshKit.Commits}, coplanar {World.MeshKit.CoplanarMs:0} ms, commit {World.MeshKit.CommitMs:0} ms)");
	}

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
			await ShaderWarmup.WaitReady(this, ct);
			LogLoad();
			// (2026-10-16, Rev 5) DEAD SILENT over the load: its veil goes first, then a little black, then the eyes open
			if (LoadingVeil.Current is { HasTitle: true })
			{
				while (LoadingVeil.Current is { } v && IsInstanceValid(v)) await Cutscene.Frame(this, ct);
				if (!quick) await Cutscene.Wait(this, WakeAfterTitleSeconds, ct);
			}
			// (2026-10-13, the owner: "it wakes you up then blacks out again. It should just wake up from the black out") one
			// wake, straight out of the black: no blinks (it opened to half, shut again, opened, shut, then cleared, which read
			// as a second blackout). The black lifts slowly at first, then clears as the head comes up.
			float seconds = quick ? 0.4f : WakeSeconds;
			float startAlpha = _fader.BlackAlpha;
			var tween = CreateTween();
			tween.TweenMethod(Callable.From<float>(p =>
			{
				_fader.BlackAlpha = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp(p * 1.15f, 0f, 1f));   // (clear a little before the head's up)
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
		// (STORY.md Rev 5) as they get up, the player's line; it plays on while they're free to look round
		if (StoryManager.Instance is { Current: Checkpoint.Act2StairsClimbed }) _ = PlayerVoice.Say(this, "wake", 4.6f);
		await Cutscene.Wait(this, 1.0, ct);
	}
}
