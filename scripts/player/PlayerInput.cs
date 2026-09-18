using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Player;

/// <summary>
/// Collects player intent from keyboard/mouse/gamepad into a few values the
/// rest of the player reads. Nothing else in the player touches Input directly,
/// so scripted control (autotest, cutscenes) only has to drive this node.
/// </summary>
public partial class PlayerInput : Node
{
	/// <summary>Movement intent, x = right, y = forward, length &lt;= 1.</summary>
	public Vector2 Move { get; private set; }
	public bool Run { get; private set; }

	/// <summary>When true, the fields below replace real input.</summary>
	public bool Scripted;
	public Vector2 ScriptedMove;
	public bool ScriptedRun;

	private Vector2 _pendingLook;   // radians, consumed once per frame
	private bool _enabled = true;

	public void SetEnabled(bool enabled)
	{
		_enabled = enabled;
		if (!enabled) { Move = Vector2.Zero; Run = false; _pendingLook = Vector2.Zero; }
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
		if (Scripted)
		{
			Move = ScriptedMove.LimitLength(1f);
			Run = ScriptedRun;
			return;
		}

		Move = new Vector2(
			Input.GetAxis("move_left", "move_right"),
			Input.GetAxis("move_back", "move_forward")).LimitLength(1f);
		Run = Input.IsActionPressed("run");

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
}
