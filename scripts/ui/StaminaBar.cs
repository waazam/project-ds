using Godot;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// A thin translucent stamina line, bottom-left, in the same understated
/// style as the compass. Stays hidden at rest with a full tank and only
/// fades in while running or recovering, so it never sits on screen nagging
/// during ordinary walking.
/// </summary>
public partial class StaminaBar : CanvasLayer
{
	[Export] public float BarWidth = 90f;
	[Export] public float BarHeight = 4f;

	private Control _draw;
	private PlayerController _player;
	private PlayerStamina _stamina;
	private float _alpha;

	public override void _Ready()
	{
		Layer = 13;
		ProcessMode = ProcessModeEnum.Always;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(BarWidth, BarHeight) };
		_draw.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		_draw.OffsetLeft = 16; _draw.OffsetRight = 16 + BarWidth;
		_draw.OffsetTop = -14 - BarHeight; _draw.OffsetBottom = -14;
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _Process(double delta)
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;
		_stamina ??= _player.GetNodeOrNull<PlayerStamina>("Stamina");
		if (_stamina == null) return;

		bool relevant = _player.IsRunning || _stamina.Value < 0.97f;
		_alpha = Mathf.MoveToward(_alpha, relevant ? 1f : 0f, (float)delta * 2f);
		_draw.Modulate = new Color(1, 1, 1, _alpha);
		if (_alpha > 0.01f) _draw.QueueRedraw();
	}

	private void OnDraw()
	{
		var size = _draw.Size;
		_draw.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.85f, 0.85f, 0.8f, 0.25f));
		float w = size.X * Mathf.Clamp(_stamina.Value, 0f, 1f);
		var fill = _stamina.CanRun ? new Color(0.82f, 0.8f, 0.7f, 0.7f) : new Color(0.6f, 0.2f, 0.15f, 0.75f);
		if (w > 0.5f) _draw.DrawRect(new Rect2(0, 0, w, size.Y), fill);
	}
}
