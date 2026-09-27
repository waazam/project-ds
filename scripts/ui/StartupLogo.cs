using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The game's first scene, after the engine's own boot splash: the owner's company logo (Flicker Archive,
/// a 5 s video), then the main menu. Any key, click or button skips it. Runs with a command line (the tests,
/// the trailer) go straight to the menu. The candle's blowing out has its sound (`candle_blow_out`).
///
/// The game renders at 640x360 (the project's viewport stretch), which would blur the logo's thin lettering,
/// so for the logo the window draws at its full resolution; the menu gets the usual scaling back.
/// </summary>
public partial class StartupLogo : Control
{
	public const string VideoPath = "res://assets/video/flicker_archive_logo.ogv";
	/// <summary>The candle being blown out (the owner): a breath, then the smoke's wispy draught. The breath
	/// starts here, so it peaks as the flame goes.</summary>
	public const string BlowPath = "res://assets/audio/sfx/candle_blow_out.wav";
	public const double BlowAt = 2.62;
	private AudioStreamPlayer _blow;

	private VideoStreamPlayer _video;
	private Window.ContentScaleModeEnum _scaleMode;
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
		_scaleMode = root.ContentScaleMode;
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
			_blow = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(BlowPath), VolumeDb = -4.5f };
			AddChild(_blow);
		}
	}

	public override void _Process(double delta)
	{
		double was = _t;
		_t += delta;
		if (_blow != null && was < BlowAt && _t >= BlowAt) _blow.Play((float)(_t - BlowAt));
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
			GetTree().Root.ContentScaleMode = _scaleMode;
		}
		GetTree().ChangeSceneToFile(StoryManager.MenuScene);
	}
}
