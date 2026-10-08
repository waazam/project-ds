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
	/// <summary>The head's life in first person: the bob with each step, the roll into a strafe, the head
	/// trailing as the body sets off, and the look easing after the mouse. Off, the view is steady and the
	/// look is direct (for anyone the motion bothers; the body's own weight stays).</summary>
	public bool HeadMotion = true;

	// Display (the owner, from the teaser): a cinematic frame and an old TV's look
	/// <summary>2.2:1 letterbox bars over the game (not the HUD; they slide away while the camera is raised).</summary>
	public bool CinemaBars = true;
	/// <summary>Light in the fog (volumetric fog: the lantern's beam, the lamps' haloes, shafts through the trees).
	/// The heaviest effect there is; off for a slower machine. `--no-volfog` turns it off for a run.</summary>
	public bool FogLighting = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-volfog") < 0;
	/// <summary>The picture's brightness: a gamma over the finished image (1 as designed, 0.7 darker to 1.5 lighter;
	/// it lifts the dark and the mids, not the whites). For a dark monitor or a bright room.</summary>
	public float Brightness = 1f;
	public const float BrightnessMin = 0.7f, BrightnessMax = 1.5f;
	/// <summary>The lantern's strength (1 as designed): its glow and its beam, for a dark monitor (2026-10-07).</summary>
	public float LanternBrightness = 1f;
	public const float LanternMin = 0.7f, LanternMax = 1.6f;

	/// <summary>Fullscreen (borderless, the screen's own resolution: the default) or a window.</summary>
	public bool Windowed
	{
		get => _windowed;
		set { _windowed = value; ApplyWindow(); }
	}
	private bool _windowed;
	/// <summary>The window's size when windowed (one of <see cref="Resolutions"/>; the screen's own size if it's bigger).</summary>
	public Vector2I WindowSize
	{
		get => _windowSize;
		set { _windowSize = value; ApplyWindow(); }
	}
	private Vector2I _windowSize = new(1600, 900);
	/// <summary>The window sizes offered (those that fit the screen).</summary>
	public static readonly Vector2I[] Resolutions =
	{
		new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160),
	};

	/// <summary>The window sizes that fit on this screen.</summary>
	public static Vector2I[] FittingResolutions()
	{
		var screen = DisplayServer.ScreenGetSize();
		var list = new System.Collections.Generic.List<Vector2I>();
		foreach (var r in Resolutions) if (r.X <= screen.X && r.Y <= screen.Y) list.Add(r);
		if (list.Count == 0) list.Add(Resolutions[0]);
		return list.ToArray();
	}

	/// <summary>Puts the window in the chosen mode and size (the game's picture scales to it either way).</summary>
	public void ApplyWindow()
	{
		if (!IsInsideTree() || AutoTest || Trailer || DisplayServer.GetName() == "headless") return;
		if (_windowed)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			var screen = DisplayServer.ScreenGetSize();
			var size = new Vector2I(Mathf.Min(_windowSize.X, screen.X), Mathf.Min(_windowSize.Y, screen.Y));
			DisplayServer.WindowSetSize(size);
			DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition() + (screen - size) / 2);
		}
		else DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
	}
	private bool _crtFilter = true;
	/// <summary>The CRT look: the game's 360 lines drawn as a TV's scanlines, a slight colour fringe and a darker
	/// grade. It draws the 2D and the finish at the window's full resolution, while the 3D still renders at 360
	/// lines (so it costs about the same). Off: the plain 640x360 picture, pixel for pixel.</summary>
	public bool CrtFilter
	{
		get => _crtFilter;
		set { _crtFilter = value; ApplyDisplay(); }
	}

	/// <summary>The sun's and moon's shadows: 0 off, 1 medium (the default: out to 40 m, a 2048 map, a light
	/// filter), 2 high (each level's own reach, a 4096 map, the soft filter). The shadow pass was most of the
	/// forest's frame (performance pass, 2026-09-27).</summary>
	private int _shadows = 1;
	public int Shadows
	{
		get => _shadows;
		set { _shadows = Mathf.Clamp(value, 0, 2); ApplyShadows(); }
	}
	public static readonly string[] ShadowNames = { "off", "medium", "high" };
	public const float MediumShadowDistance = 40f;

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
		if (_windowed) Callable.From(ApplyWindow).CallDeferred();   // (the project opens fullscreen)
	}

	public override void _UnhandledInput(InputEvent e)
	{
		// F11 or Alt+Enter: switch between borderless fullscreen (the default) and a window.
		if (e is InputEventKey { Pressed: true, Echo: false } k && (k.Keycode == Key.F11 || (k.Keycode == Key.Enter && k.AltPressed)))
		{
			Windowed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
			Save();
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
		cfg.SetValue("accessibility", "head_motion", HeadMotion);
		cfg.SetValue("display", "cinema_bars", CinemaBars);
		cfg.SetValue("display", "fog_lighting", FogLighting);
		cfg.SetValue("display", "brightness", Brightness);
		cfg.SetValue("display", "lantern_brightness", LanternBrightness);
		cfg.SetValue("display", "crt_filter", _crtFilter);
		cfg.SetValue("display", "shadows", _shadows);
		cfg.SetValue("display", "windowed", _windowed);
		cfg.SetValue("display", "window_width", _windowSize.X);
		cfg.SetValue("display", "window_height", _windowSize.Y);
		cfg.Save(SavePath);
		EmitSignal(SignalName.Changed);
	}

	private void Load()
	{
		if (AutoTest) return; // tests always run on defaults
		LoadFile();
	}

	private void LoadFile()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(SavePath) != Error.Ok) return;
		MouseSensitivity = (float)cfg.GetValue("camera", "mouse_sensitivity", MouseSensitivity);
		StickSensitivity = (float)cfg.GetValue("camera", "stick_sensitivity", StickSensitivity);
		InvertY = (bool)cfg.GetValue("camera", "invert_y", InvertY);
		CameraDistance = (float)cfg.GetValue("camera", "distance", CameraDistance);
		Camera = (CameraMode)(int)cfg.GetValue("camera", "mode", (int)Camera);
		_masterVolume = (float)cfg.GetValue("audio", "master_volume", _masterVolume);
		ReduceFlashing = (bool)cfg.GetValue("accessibility", "reduce_flashing", ReduceFlashing);
		HeadMotion = (bool)cfg.GetValue("accessibility", "head_motion", HeadMotion);
		CinemaBars = (bool)cfg.GetValue("display", "cinema_bars", CinemaBars);
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-volfog") < 0) FogLighting = (bool)cfg.GetValue("display", "fog_lighting", FogLighting);
		Brightness = Mathf.Clamp((float)cfg.GetValue("display", "brightness", Brightness), BrightnessMin, BrightnessMax);
		LanternBrightness = Mathf.Clamp((float)cfg.GetValue("display", "lantern_brightness", LanternBrightness), LanternMin, LanternMax);
		_crtFilter = (bool)cfg.GetValue("display", "crt_filter", _crtFilter);
		_shadows = Mathf.Clamp((int)cfg.GetValue("display", "shadows", _shadows), 0, 2);
		_windowed = (bool)cfg.GetValue("display", "windowed", _windowed);
		_windowSize = new Vector2I((int)cfg.GetValue("display", "window_width", _windowSize.X), (int)cfg.GetValue("display", "window_height", _windowSize.Y));
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

	/// <summary>Applies the shadow setting to the renderer and to every sun and moon in the tree (and, through
	/// NodeAdded, to each one a level brings in). A light's own reach is remembered, for High.</summary>
	public void ApplyShadows()
	{
		if (!IsInsideTree()) return;
		RenderingServer.DirectionalShadowAtlasSetSize(_shadows >= 2 ? 4096 : 2048, true);
		RenderingServer.DirectionalSoftShadowFilterSetQuality(_shadows >= 2 ? RenderingServer.ShadowQuality.SoftMedium : RenderingServer.ShadowQuality.SoftVeryLow);
		foreach (var n in GetTree().Root.FindChildren("*", "DirectionalLight3D", true, false))
			if (n is DirectionalLight3D d) ApplyShadows(d);
	}

	private void ApplyShadows(DirectionalLight3D d)
	{
		if (!d.HasMeta("own_shadow")) d.SetMeta("own_shadow", d.ShadowEnabled);
		if (!d.HasMeta("own_shadow_reach")) d.SetMeta("own_shadow_reach", d.DirectionalShadowMaxDistance);
		bool own = (bool)d.GetMeta("own_shadow");
		float reach = (float)d.GetMeta("own_shadow_reach");
		d.ShadowEnabled = own && _shadows > 0;
		float want = _shadows >= 2 ? reach : Mathf.Min(reach, MediumShadowDistance);
		if (!Mathf.IsEqualApprox(d.DirectionalShadowMaxDistance, want)) d.DirectionalShadowMaxDistance = want;
	}

	private void OnNodeAdded(Node n)
	{
		if (n is DirectionalLight3D d) Callable.From(() => { if (IsInstanceValid(d)) ApplyShadows(d); }).CallDeferred();
	}

	public override void _Ready()
	{
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--settings-test") >= 0) Callable.From(RunSettingsTest).CallDeferred();
		GetTree().NodeAdded += OnNodeAdded;
		ApplyShadows();
		GetTree().Root.SizeChanged += OnRootSizeChanged;
		ApplyDisplay();
	}

	// ------------------------------------------------------------------ the settings' round trip (a test)

	/// <summary>Every setting the menus save, as it is now (name: value), for the round trip.</summary>
	private System.Collections.Generic.Dictionary<string, string> Snapshot() => new()
	{
		["mouse"] = MouseSensitivity.ToString("0.00000"), ["stick"] = StickSensitivity.ToString("0.000"), ["invert_y"] = InvertY.ToString(),
		["distance"] = CameraDistance.ToString("0.000"), ["camera"] = Camera.ToString(), ["volume"] = _masterVolume.ToString("0.000"),
		["reduce_flashing"] = ReduceFlashing.ToString(), ["head_motion"] = HeadMotion.ToString(), ["bars"] = CinemaBars.ToString(),
		["fog_lighting"] = FogLighting.ToString(), ["brightness"] = Brightness.ToString("0.000"), ["lantern"] = LanternBrightness.ToString("0.000"),
		["crt"] = _crtFilter.ToString(), ["shadows"] = _shadows.ToString(), ["windowed"] = _windowed.ToString(), ["window"] = _windowSize.ToString(),
	};

	private void SetFields(float mouse, float stick, bool inv, float dist, float vol, bool flash, bool head, bool bars, bool fog, float bright, float lantern, bool crt, int shadows, bool windowed, Vector2I size)
	{
		MouseSensitivity = mouse; StickSensitivity = stick; InvertY = inv; CameraDistance = dist; _masterVolume = vol; ReduceFlashing = flash;
		HeadMotion = head; CinemaBars = bars; FogLighting = fog; Brightness = bright; LanternBrightness = lantern; _crtFilter = crt; _shadows = shadows;
		_windowed = windowed; _windowSize = size;
	}

	/// <summary>`--settings-test` (on the built game too, 2026-10-07): every setting set to something other than its
	/// default, saved, scrambled, and read back from the file as a restart would; each compared. The player's own file
	/// is put back afterwards. Prints PASS or FAIL per setting, and quits (exit code 0 if all came back).</summary>
	public void RunSettingsTest()
	{
		string backup = Godot.FileAccess.FileExists(SavePath) ? Godot.FileAccess.GetFileAsString(SavePath) : null;
		var before = Snapshot();
		var keep = (MouseSensitivity, StickSensitivity, InvertY, CameraDistance, _masterVolume, ReduceFlashing, HeadMotion, CinemaBars, FogLighting, Brightness, LanternBrightness, _crtFilter, _shadows, _windowed, _windowSize, Camera);
		SetFields(0.0042f, 3.3f, !InvertY, 3.1f, 0.63f, !ReduceFlashing, !HeadMotion, !CinemaBars, !FogLighting, 1.25f, 1.35f, !_crtFilter, (_shadows + 1) % 3, !_windowed, new Vector2I(1280, 720));
		var want = Snapshot();
		Save();
		SetFields(0.001f, 1f, false, 2f, 1f, false, true, true, true, 1f, 1f, true, 1, false, new Vector2I(1600, 900));
		LoadFile();
		var got = Snapshot();
		int bad = 0;
		foreach (var (k, v) in want)
		{
			bool ok = got.TryGetValue(k, out var g) && g == v;
			if (!ok) bad++;
			GD.Print($"[settings-test] {(ok ? "PASS" : "FAIL")} {k}: saved {v}, read back {g}");
		}
		// the player's own settings back, and their file
		(MouseSensitivity, StickSensitivity, InvertY, CameraDistance, _masterVolume, ReduceFlashing, HeadMotion, CinemaBars, FogLighting, Brightness, LanternBrightness, _crtFilter, _shadows, _windowed, _windowSize, Camera) = keep;
		if (backup != null) { using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write); f?.StoreString(backup); }
		else DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
		GD.Print($"[settings-test] {want.Count - bad}/{want.Count} settings survive a restart; the player's own put back ({(Snapshot()["brightness"] == before["brightness"] ? "unchanged" : "CHANGED")})");
		GetTree().Quit(bad == 0 ? 0 : 1);
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
		AddKeys("lean_left", Key.Q);
		AddKeys("lean_right", Key.R);
		AddKeys("crouch", Key.C, Key.Ctrl);   // a toggle (either Ctrl answers, the left one included)

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
		AddButton("lean_left", JoyButton.DpadLeft);
		AddButton("lean_right", JoyButton.DpadRight);
		AddButton("crouch", JoyButton.RightStick);
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
