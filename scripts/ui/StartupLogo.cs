using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The game's first scene, after the engine's own boot splash: the owner's company logo (Flicker Archive,
/// a 5 s video), then the main menu. Any key, click or button skips it. Runs with a command line (the tests,
/// the trailer) go straight to the menu. The candle crackles and is blown out (`flicker_logo_candle`).
///
/// The game renders at 640x360 (the project's viewport stretch), which would blur the logo's thin lettering,
/// so for the logo the window draws at its full resolution; the menu gets the usual scaling back.
/// </summary>
public partial class StartupLogo : Control
{
	public const string VideoPath = "res://assets/video/flicker_archive_logo.ogv";
	/// <summary>The candle (the owner's pick, a royalty-free clip cut to the logo): its crackle while it burns, then
	/// the blow as the flame goes (2.82 s in) and the smoke's wisp. It starts with the video.</summary>
	public const string BlowPath = "res://assets/audio/sfx/flicker_logo_candle.wav";
	private AudioStreamPlayer _blow;

	private VideoStreamPlayer _video;
	private double _t;
	private bool _leaving;

	public override void _Ready()
	{
		if (OS.GetCmdlineUserArgs().Length > 0 || !ResourceLoader.Exists(VideoPath))
		{
			Callable.From(ToMenu).CallDeferred();
			return;
		}
		var root = GetTree().Root;
		root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;

		SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(new ColorRect { Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });
		// the video keeps its 16:9, centred, on black whatever the window's shape
		var frame = new AspectRatioContainer { Ratio = 16f / 9f, AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(frame);
		_video = new VideoStreamPlayer { Stream = GD.Load<VideoStream>(VideoPath), Expand = true, Autoplay = false };
		frame.AddChild(_video);
		_video.Finished += ToMenu;
		_video.Play();
		if (ResourceLoader.Exists(BlowPath))
		{
			_blow = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(BlowPath), VolumeDb = -1.5f };
			AddChild(_blow);
			_blow.Play();
		}
	}

	public override void _Process(double delta)
	{
		_t += delta;
		if (_t > 12.0) ToMenu();   // a video that never reports finishing never strands the player here
	}

	public override void _Input(InputEvent e)
	{
		if (_t < 0.4 || _leaving) return;
		if (e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true })
		{
			GetViewport().SetInputAsHandled();
			ToMenu();
		}
	}

	private void ToMenu()
	{
		if (_leaving) return;
		_leaving = true;
		if (_video != null)
		{
			_video.Stop();
			_blow?.Stop();
			GetTree().Root.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
			GameSettings.Instance?.ApplyDisplay();   // the CRT filter's scaling, or the project's own
		}
		GetTree().ChangeSceneToFile(StoryManager.MenuScene);
	}
}
