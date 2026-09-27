using System.Linq;
using Godot;

namespace ProjectDS.Systems;

public enum CameraMode { FirstPerson, ThirdPerson }

/// <summary>
/// Autoload. Player-facing settings plus input-map registration.
/// Input actions are defined here in code (not in project.godot) so bindings
/// stay readable and diffable. Settings persist to user://settings.cfg.
/// </summary>
public partial class GameSettings : Node
{
	public static GameSettings Instance { get; private set; }

	[Signal] public delegate void ChangedEventHandler();

	// Camera
	public float MouseSensitivity = 0.0025f;   // radians per pixel
	public float StickSensitivity = 2.6f;      // radians per second at full tilt
	public bool InvertY = false;
	public float CameraDistance = 3.2f;        // metres, clamped by the camera rig
	/// <summary>First person is the current design. Third person is kept working for later.</summary>
	public CameraMode Camera = CameraMode.FirstPerson;

	// Accessibility
	/// <summary>Tones down lightning, the camera flash and other full-screen flashes.</summary>
	public bool ReduceFlashing = false;

	// Display (the owner, from the teaser): a cinematic frame and an old TV's look
	/// <summary>2.2:1 letterbox bars over the game (not the HUD; they slide away while the camera is raised).</summary>
	public bool CinemaBars = true;
	private bool _crtFilter = true;
	/// <summary>The CRT look: the game's 360 lines drawn as a TV's scanlines, a slight colour fringe and a darker
	/// grade. It draws the 2D and the finish at the window's full resolution, while the 3D still renders at 360
	/// lines (so it costs about the same). Off: the plain 640x360 picture, pixel for pixel.</summary>
	public bool CrtFilter
	{
		get => _crtFilter;
		set { _crtFilter = value; ApplyDisplay(); }
	}

	// Audio: linear 0..1, applied to the Master bus. Defaults below full — playtesting found the
	// mix considerably louder than expected at 100%.
	private float _masterVolume = 0.6f;
	public float MasterVolume
	{
		get => _masterVolume;
		set { _masterVolume = Mathf.Clamp(value, 0f, 1f); ApplyMasterVolume(); }
	}

	private static void ApplyMasterVolume()
	{
		int bus = AudioServer.GetBusIndex("Master");
		if (bus < 0) return;
		float v = Instance?._masterVolume ?? 0.6f;
		AudioServer.SetBusVolumeDb(bus, v <= 0.0001f ? -80f : Mathf.LinearToDb(v));
	}

	// Command-line flags (after "--" on the godot command line)
	public bool AutoTest { get; private set; }
	/// <summary>Dev-only fast path: the autotest skips straight to the Act 5 → 7 handoff instead
	/// of replaying the whole game first. Cuts a ~20-minute run down to a couple of minutes while
	/// iterating on that stretch specifically.</summary>
	public bool AutoTestSkipToAct5 { get; private set; }
	/// <summary>Dev-only fast path: skips straight to just after the cabin fire, near the bunker, for
	/// fast iteration on Acts 8-10 (the bunker interior) without replaying Acts 1-7 first.</summary>
	public bool AutoTestSkipToAct7 { get; private set; }
	/// <summary>Dev-only fast path: skips straight to just after the walkie-talkie is found, near the
	/// bunker, for fast iteration on Act 11 (the radio, the tall stairs, the giant) without replaying
	/// the bunker's hallway/CRT room/maze first.</summary>
	public bool AutoTestSkipToAct11 { get; private set; }
	/// <summary>`--continue-test`: instead of the walkthrough, write a save for every checkpoint in
	/// turn, Continue from it, and check the restored world (see ContinueRoundTripTest).</summary>
	public bool ContinueTest { get; private set; }
	/// <summary>`--trailer`: straight into the Hollow at an early save (the test slots, never the player's) and
	/// run <see cref="TrailerDirector"/>; record it with Godot's `--write-movie`.</summary>
	public bool Trailer { get; private set; }
	/// <summary>`--trailer-ending`: the teaser's last shot instead (Act 1's first staircase in the fog, then the title).</summary>
	public bool TrailerEnding { get; private set; }
	/// <summary>Dev: `--start-act=2` writes a save at that act's checkpoint and Continues into it from the menu
	/// (only 2 today: the Hollow wake). 0 = normal start. Overwrites the real save slot.</summary>
	public int StartAct { get; private set; }

	private const string SavePath = "user://settings.cfg";

	public override void _EnterTree()
	{
		Instance = this;
		var args = OS.GetCmdlineUserArgs();
		AutoTest = args.Contains("--autotest");
		AutoTestSkipToAct5 = args.Contains("--skip-to-act5");
		AutoTestSkipToAct7 = args.Contains("--skip-to-act7");
		AutoTestSkipToAct11 = args.Contains("--skip-to-act11");
		ContinueTest = args.Contains("--continue-test");
		Trailer = args.Contains("--trailer") || args.Contains("--trailer-ending");
		TrailerEnding = args.Contains("--trailer-ending");
		// the game's title (the owner, 2026-09-27: "Dead Silent" from now on). The project's own name stays
		// "Project DS" so the save and settings folder doesn't move.
		DisplayServer.WindowSetTitle("Dead Silent");
		foreach (var a in args) if (a.StartsWith("--start-act=") && int.TryParse(a["--start-act=".Length..], out int sa)) StartAct = sa;
		if (ContinueTest) AutoTest = true;   // same test save slots and defaults
		RegisterInputActions();
		Load();
		if (args.Contains("--third-person")) Camera = CameraMode.ThirdPerson;
		ApplyMasterVolume();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		// F11 or Alt+Enter: switch between borderless fullscreen (the default) and a window.
		if (e is InputEventKey { Pressed: true, Echo: false } k && (k.Keycode == Key.F11 || (k.Keycode == Key.Enter && k.AltPressed)))
		{
			var mode = DisplayServer.WindowGetMode();
			DisplayServer.WindowSetMode(mode == DisplayServer.WindowMode.Fullscreen
				? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
			GetViewport().SetInputAsHandled();
		}
	}

	public void Save()
	{
		var cfg = new ConfigFile();
		cfg.SetValue("camera", "mouse_sensitivity", MouseSensitivity);
		cfg.SetValue("camera", "stick_sensitivity", StickSensitivity);
		cfg.SetValue("camera", "invert_y", InvertY);
		cfg.SetValue("camera", "distance", CameraDistance);
		cfg.SetValue("camera", "mode", (int)Camera);
		cfg.SetValue("audio", "master_volume", MasterVolume);
		cfg.SetValue("accessibility", "reduce_flashing", ReduceFlashing);
		cfg.SetValue("display", "cinema_bars", CinemaBars);
		cfg.SetValue("display", "crt_filter", _crtFilter);
		cfg.Save(SavePath);
		EmitSignal(SignalName.Changed);
	}

	private void Load()
	{
		if (AutoTest) return; // tests always run on defaults
		var cfg = new ConfigFile();
		if (cfg.Load(SavePath) != Error.Ok) return;
		MouseSensitivity = (float)cfg.GetValue("camera", "mouse_sensitivity", MouseSensitivity);
		StickSensitivity = (float)cfg.GetValue("camera", "stick_sensitivity", StickSensitivity);
		InvertY = (bool)cfg.GetValue("camera", "invert_y", InvertY);
		CameraDistance = (float)cfg.GetValue("camera", "distance", CameraDistance);
		Camera = (CameraMode)(int)cfg.GetValue("camera", "mode", (int)Camera);
		_masterVolume = (float)cfg.GetValue("audio", "master_volume", _masterVolume);
		ReduceFlashing = (bool)cfg.GetValue("accessibility", "reduce_flashing", ReduceFlashing);
		CinemaBars = (bool)cfg.GetValue("display", "cinema_bars", CinemaBars);
		_crtFilter = (bool)cfg.GetValue("display", "crt_filter", _crtFilter);
	}

	/// <summary>Game pixels per window pixel's worth: how many window rows draw one of the game's 360 lines.</summary>
	public float LineScale { get; private set; } = 1f;

	/// <summary>Puts the window's scaling in line with the CRT setting. With the filter the root draws its 2D at the
	/// window's resolution (canvas_items) and renders the 3D at the game's 360 lines (supersampled 1.5x, as
	/// always), scaled up smoothly, like a TV's picture; without it the whole frame is drawn at 640x360 and
	/// scaled up (the project's own setting).</summary>
	public void ApplyDisplay()
	{
		if (!IsInsideTree() || Trailer) return;   // the trailer frames itself
		var root = GetTree().Root;
		if (root.ContentScaleMode == Window.ContentScaleModeEnum.Disabled) return;   // the startup logo, full-size
		// the project renders its 3D supersampled (rendering/scaling_3d/scale, 1.5): keep that either way
		float supersample = (float)ProjectSettings.GetSetting("rendering/scaling_3d/scale", 1.0f);
		var mode = _crtFilter ? Window.ContentScaleModeEnum.CanvasItems : Window.ContentScaleModeEnum.Viewport;
		float lines = Mathf.Max(360f, root.Size.Y);
		float scale = _crtFilter ? Mathf.Clamp(360f * supersample / lines, 0.25f, 2f) : supersample;
		// only touch what changed: setting the window's scaling re-lays it out (and signals a size change)
		if (root.ContentScaleMode != mode) root.ContentScaleMode = mode;
		if (_crtFilter && root.Scaling3DMode != Viewport.Scaling3DModeEnum.Bilinear) root.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
		if (!Mathf.IsEqualApprox(root.Scaling3DScale, scale)) root.Scaling3DScale = scale;
		LineScale = _crtFilter ? lines / 360f : 1f;
	}

	private Vector2I _lastSize;

	private void OnRootSizeChanged()
	{
		var size = GetTree().Root.Size;
		if (size == _lastSize) return;
		_lastSize = size;
		ApplyDisplay();
	}

	public override void _Ready()
	{
		GetTree().Root.SizeChanged += OnRootSizeChanged;
		ApplyDisplay();
	}

	private static void RegisterInputActions()
	{
		AddKeys("move_forward", Key.W, Key.Up);
		AddKeys("move_back", Key.S, Key.Down);
		AddKeys("move_left", Key.A, Key.Left);
		AddKeys("move_right", Key.D, Key.Right);
		AddKeys("run", Key.Shift);
		AddKeys("pause", Key.Escape);
		AddKeys("interact", Key.E);
		AddMouse("focus", MouseButton.Right);
		AddKeys("flashlight_toggle", Key.F);
		AddMouse("photo", MouseButton.Left);
		AddMouse("item_next", MouseButton.WheelDown);
		AddMouse("item_prev", MouseButton.WheelUp);
		AddKeys("photo_log", Key.Tab);
		AddKeys("lantern_mode", Key.B);

		AddAxis("move_forward", JoyAxis.LeftY, -1);
		AddAxis("move_back", JoyAxis.LeftY, 1);
		AddAxis("move_left", JoyAxis.LeftX, -1);
		AddAxis("move_right", JoyAxis.LeftX, 1);
		AddAxis("look_up", JoyAxis.RightY, -1);
		AddAxis("look_down", JoyAxis.RightY, 1);
		AddAxis("look_left", JoyAxis.RightX, -1);
		AddAxis("look_right", JoyAxis.RightX, 1);
		AddAxis("run", JoyAxis.TriggerLeft, 1);
		AddButton("run", JoyButton.LeftStick);
		AddButton("pause", JoyButton.Start);
		AddButton("interact", JoyButton.A);
		AddAxis("focus", JoyAxis.TriggerRight, 1);
		AddButton("flashlight_toggle", JoyButton.Y);
		AddButton("photo", JoyButton.X);
		AddButton("photo_log", JoyButton.Back);
		AddButton("lantern_mode", JoyButton.RightShoulder);
		AddButton("item_next", JoyButton.LeftShoulder);   // the item in hand (the HUD), the camera's zoom, the album's pages
	}

	private static void Ensure(string action)
	{
		if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.2f);
	}

	private static void AddKeys(string action, params Key[] keys)
	{
		Ensure(action);
		foreach (var key in keys)
			InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
	}

	private static void AddAxis(string action, JoyAxis axis, float sign)
	{
		Ensure(action);
		InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = sign });
	}

	private static void AddMouse(string action, MouseButton button)
	{
		Ensure(action);
		InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
	}

	private static void AddButton(string action, JoyButton button)
	{
		Ensure(action);
		InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
	}
}
