using Godot;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// The stamina, bottom-left, shown only while running or getting its breath back. (2026-10-10c, the owner: "the stamina
/// bar also needs to match the other ui color, we should give that more flare and a better animation too") A neon tube
/// in the HUD's colour (NeonKit) in a thin bracket: its lit length the breath left, the drained part a dim ghost of the
/// tube. The fill eases down to its value (the tube "burning down") with a bright head where it ends; a soft pulse runs
/// along it while running; it flickers low and dims to a deep red-dark when spent, until there's enough to run again;
/// filling, a ripple of light runs up it.
/// </summary>
public partial class StaminaBar : CanvasLayer
{
	[Export] public float BarWidth = 96f;
	[Export] public float BarHeight = 8f;

	private static readonly Color Track = new(UiKit.Fog, 0.35f);
	private static readonly Color Fill = new(UiKit.Bone, 0.6f);
	private static readonly Color Spent = new(0.55f, 0.27f, 0.18f, 0.7f);

	private Control _draw;
	private PlayerController _player;
	private PlayerStamina _stamina;
	private float _alpha, _shown = 1f, _ghost = 1f;
	/// <summary>For the UI preview: shown at this value, as if running.</summary>
	public static float? Preview;
	private double _clock;

	public override void _Ready()
	{
		Layer = 13;
		ProcessMode = ProcessModeEnum.Always;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(BarWidth, BarHeight), Modulate = new Color(1, 1, 1, 0) };
		_draw.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		_draw.OffsetLeft = 16; _draw.OffsetRight = 16 + BarWidth;
		_draw.OffsetTop = -16 - BarHeight; _draw.OffsetBottom = -16;
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _Process(double delta)
	{
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			_stamina = _player?.GetNodeOrNull<PlayerStamina>("Stamina");
		}
		if (_stamina == null || !IsInstanceValid(_stamina)) { _draw.Visible = false; return; }

		bool relevant = _player.IsRunning || _stamina.Value < 0.97f || Preview != null;
		_alpha = Mathf.MoveToward(_alpha, relevant ? 1f : 0f, (float)delta * 2f);
		_draw.Modulate = new Color(1, 1, 1, _alpha);
		_draw.Visible = _alpha > 0.01f;
		_clock += delta;
		float v = Mathf.Clamp(Preview ?? _stamina.Value, 0f, 1f), dt = (float)delta;
		_shown = Mathf.Lerp(_shown, v, 1f - Mathf.Exp(-dt * 10f));
		_ghost = v < _ghost ? Mathf.MoveToward(_ghost, v, dt * 0.35f) : v;   // (the drained part lingers a moment, fading)
		if (_draw.Visible) _draw.QueueRedraw();
	}

	private static Texture2D _groove;

	private void OnDraw()
	{
		if (_stamina == null || !IsInstanceValid(_stamina)) return;   // the first draw can come before the player exists
		var size = _draw.Size;
		float t = (float)_clock, cy = size.Y * 0.5f, x0 = 4f, x1 = size.X - 4f, len = x1 - x0;
		bool spent = !_stamina.CanRun;
		bool running = _player.IsRunning || Preview != null;
		float breath = 0.85f + 0.15f * Mathf.Sin(t * (running ? 5.5f : 1.8f));
		// (no bracket round it, 2026-10-11, the owner: the little [ ] "look bad") (2026-10-25, the HUD's reskin) the tube runs
		// in a groove of dark old brass (assets/ui/hud/stamina_groove.png)
		_groove ??= ResourceLoader.Exists("res://assets/ui/hud/stamina_groove.png") ? GD.Load<Texture2D>("res://assets/ui/hud/stamina_groove.png") : null;
		if (_groove != null) _draw.DrawTextureRect(_groove, new Rect2(x0 - 3f, cy - 3.5f, len + 6f, 7f), false);
		// the ghost of the whole tube, and what's just drained lingering
		NeonKit.Seg(_draw, new Vector2(x0, cy), new Vector2(x1, cy), 0.8f, 0.12f);
		if (_ghost > _shown + 0.005f) NeonKit.Seg(_draw, new Vector2(x0 + len * _shown, cy), new Vector2(x0 + len * _ghost, cy), 1.0f, 0.3f);
		float end = x0 + len * _shown;
		if (end <= x0 + 0.3f) return;
		if (spent)
		{
			// spent: low, dim, a slow flicker (never a flash), toward a dark red whatever the colour
			float lo = 0.35f + 0.15f * Mathf.Sin(t * 4.2f) * Mathf.Sin(t * 2.3f + 1f);
			_draw.DrawLine(new Vector2(x0, cy), new Vector2(end, cy), new Color(0.6f, 0.08f, 0.06f, 0.35f * lo), 4f, true);
			_draw.DrawLine(new Vector2(x0, cy), new Vector2(end, cy), new Color(1f, 0.45f, 0.4f, 0.8f * lo), 1.2f, true);
			return;
		}
		// (2026-10-31, the owner: "remove the ball at the far right end and make it pulsate a glowing green when you are
		// sprinting", the green a nebula's) the tube in green, swelling and dimming in a pulse while they run; no head on it,
		// no bead running along it
		float pulse = running ? 0.72f + 0.28f * Mathf.Sin(t * 7.5f) : 0.8f;
		var glow = new Color(0.25f, 0.9f, 0.4f);
		var core = new Color(0.72f, 1f, 0.78f);
		if (running) _draw.DrawLine(new Vector2(x0, cy), new Vector2(end, cy), new Color(glow, 0.22f * pulse), 5f, true);
		_draw.DrawLine(new Vector2(x0, cy), new Vector2(end, cy), new Color(glow, 0.35f * pulse), 2.6f, true);
		_draw.DrawLine(new Vector2(x0, cy), new Vector2(end, cy), new Color(core, 0.95f * pulse), 1.2f, true);
	}
}
