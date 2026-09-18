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

	// Command-line flags (after "--" on the godot command line)
	public bool AutoTest { get; private set; }

	private const string SavePath = "user://settings.cfg";

	public override void _EnterTree()
	{
		Instance = this;
		var args = OS.GetCmdlineUserArgs();
		AutoTest = args.Contains("--autotest");
		RegisterInputActions();
		Load();
		if (args.Contains("--third-person")) Camera = CameraMode.ThirdPerson;
	}

	public void Save()
	{
		var cfg = new ConfigFile();
		cfg.SetValue("camera", "mouse_sensitivity", MouseSensitivity);
		cfg.SetValue("camera", "stick_sensitivity", StickSensitivity);
		cfg.SetValue("camera", "invert_y", InvertY);
		cfg.SetValue("camera", "distance", CameraDistance);
		cfg.SetValue("camera", "mode", (int)Camera);
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

	private static void AddButton(string action, JoyButton button)
	{
		Ensure(action);
		InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
	}
}
