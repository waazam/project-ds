using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Player;

/// <summary>
/// Collects player intent from keyboard/mouse/gamepad into a few values the
/// rest of the game reads. Nothing else touches Input directly for gameplay
/// actions, so scripted control (autotest, cutscenes) only has to drive this
/// node, and disabling it (cutscenes) or pausing the tree silences every action.
///
/// Buttons come in two forms: *Held* (true while down: hold-to-break the door)
/// and *Pressed* (true for the one frame the button went down: pick up, toggle).
/// Pressed edges are computed here once per frame, so every reader in that frame
/// sees the same edge regardless of processing order.
/// </summary>
public partial class PlayerInput : Node
{
	/// <summary>Movement intent, x = right, y = forward, length &lt;= 1.</summary>
	public Vector2 Move { get; private set; }
	public bool Run { get; private set; }
	/// <summary>Held to focus on something (right mouse / right trigger): a slight zoom.</summary>
	public bool Focus { get; private set; }

	public bool InteractHeld => Live && _held[Interact];
	public bool InteractPressed => Live && Edge(Interact);
	public bool LightPressed => Live && Edge(Light);
	public bool PhotoHeld => Live && _held[Photo];
	public bool PhotoPressed => Live && Edge(Photo);
	public bool ItemNextPressed => Live && Edge(ItemNext);
	public bool ItemPrevPressed => Live && Edge(ItemPrev);

	/// <summary>When true, the fields below replace real input.</summary>
	public bool Scripted;
	public Vector2 ScriptedMove;
	public bool ScriptedRun;
	public bool ScriptedFocus;
	/// <summary>Scripted buttons are "held" while true; the Pressed edge fires on the frame they turn true.</summary>
	public bool ScriptedInteract, ScriptedLight, ScriptedPhoto, ScriptedItemNext, ScriptedItemPrev;

	public bool Enabled => _enabled;

	private const int Interact = 0, Light = 1, Photo = 2, ItemNext = 3, ItemPrev = 4;
	private static readonly string[] Actions = { "interact", "flashlight_toggle", "photo", "item_next", "item_prev" };
	private readonly bool[] _held = new bool[5];
	private readonly bool[] _wasHeld = new bool[5];
	private readonly bool[] _pressed = new bool[5];
	// Physics-frame view of the edges: every press since the last physics tick, so a reader in
	// _PhysicsProcess (PlayerInteraction) never misses a one-frame press when the render rate
	// is higher than the physics rate.
	private readonly bool[] _pendingPhysics = new bool[5];
	private readonly bool[] _pressedPhysics = new bool[5];

	private bool Edge(int i) => Engine.IsInPhysicsFrame() ? _pressedPhysics[i] : _pressed[i];

	/// <summary>Runs before PlayerInteraction (earlier in the player's tree): publishes the edges
	/// gathered since the previous physics tick for this tick's readers.</summary>
	public override void _PhysicsProcess(double delta)
	{
		for (int i = 0; i < _pressed.Length; i++)
		{
			_pressedPhysics[i] = _pendingPhysics[i];
			_pendingPhysics[i] = false;
		}
	}

	private Vector2 _pendingLook;   // radians, consumed once per frame
	private bool _enabled = true;
	private bool _capturedLastFrame;

	private bool Live => _enabled && IsInsideTree() && !GetTree().Paused;

	public void SetEnabled(bool enabled)
	{
		_enabled = enabled;
		if (!enabled)
		{
			Move = Vector2.Zero; Run = false; Focus = false; _pendingLook = Vector2.Zero;
			System.Array.Clear(_held); System.Array.Clear(_pressed);
			System.Array.Clear(_pendingPhysics); System.Array.Clear(_pressedPhysics);
		}
	}

	/// <summary>Look delta in radians (x = yaw, y = pitch) since last call.</summary>
	public Vector2 ConsumeLook()
	{
		var look = _pendingLook;
		_pendingLook = Vector2.Zero;
		return look;
	}

	public void AddScriptedLook(Vector2 radians) => _pendingLook += radians;

	public override void _UnhandledInput(InputEvent e)
	{
		if (!_enabled || Scripted) return;
		if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			var s = GameSettings.Instance;
			float pitchSign = s.InvertY ? 1f : -1f;
			_pendingLook += new Vector2(-motion.Relative.X * s.MouseSensitivity,
				pitchSign * motion.Relative.Y * s.MouseSensitivity);
		}
	}

	public override void _Process(double delta)
	{
		if (!_enabled) return;
		UpdateButtons();
		if (Scripted)
		{
			Move = ScriptedMove.LimitLength(1f);
			Run = ScriptedRun;
			Focus = ScriptedFocus;
			return;
		}

		Move = new Vector2(
			Input.GetAxis("move_left", "move_right"),
			Input.GetAxis("move_back", "move_forward")).LimitLength(1f);
		Run = Input.IsActionPressed("run");
		Focus = Input.IsActionPressed("focus");

		var s = GameSettings.Instance;
		var stick = new Vector2(
			Input.GetAxis("look_left", "look_right"),
			Input.GetAxis("look_down", "look_up"));
		if (stick.LengthSquared() > 0f)
		{
			// Squared response curve: fine aim near centre, fast turns at full tilt.
			stick *= stick.Length();
			float pitchSign = s.InvertY ? -1f : 1f;
			_pendingLook += new Vector2(-stick.X, pitchSign * stick.Y) * s.StickSensitivity * (float)delta;
		}
	}

	private void UpdateButtons()
	{
		bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
		for (int i = 0; i < Actions.Length; i++)
		{
			// Wheel "presses" last no time at all, so they are read as just-pressed events.
			bool wheel = i == ItemNext || i == ItemPrev;
			bool down = Scripted ? ScriptedButton(i)
				: wheel ? Input.IsActionJustPressed(Actions[i]) : Input.IsActionPressed(Actions[i]);
			// Mouse-bound actions only count while the mouse is captured: the click that
			// recaptures it (after pause or alt-tab) must not also take a photo.
			if (!Scripted && (i == Photo || wheel) && !(captured && _capturedLastFrame)) down = false;
			_held[i] = down;
			_pressed[i] = down && !_wasHeld[i];
			if (_pressed[i]) _pendingPhysics[i] = true;
			_wasHeld[i] = down;
		}
		_capturedLastFrame = captured;
	}

	private bool ScriptedButton(int i) => i switch
	{
		Interact => ScriptedInteract,
		Light => ScriptedLight,
		Photo => ScriptedPhoto,
		ItemNext => ScriptedItemNext,
		_ => ScriptedItemPrev,
	};
}
