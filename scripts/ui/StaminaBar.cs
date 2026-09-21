using Godot;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// A thin translucent stamina line, bottom-left, in the same understated
/// style as the compass. Stays hidden at rest with a full tank and only
/// fades in while running or recovering, so it never sits on screen nagging
/// during ordinary walking. Bone on a fog hairline; a dull rust when spent.
/// </summary>
public partial class StaminaBar : CanvasLayer
{
	[Export] public float BarWidth = 90f;
	[Export] public float BarHeight = 2f;

	private static readonly Color Track = new(UiKit.Fog, 0.35f);
	private static readonly Color Fill = new(UiKit.Bone, 0.6f);
	private static readonly Color Spent = new(0.55f, 0.27f, 0.18f, 0.7f);

	private Control _draw;
	private PlayerController _player;
	private PlayerStamina _stamina;
	private float _alpha;

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

		bool relevant = _player.IsRunning || _stamina.Value < 0.97f;
		_alpha = Mathf.MoveToward(_alpha, relevant ? 1f : 0f, (float)delta * 2f);
		_draw.Modulate = new Color(1, 1, 1, _alpha);
		_draw.Visible = _alpha > 0.01f;
		if (_draw.Visible) _draw.QueueRedraw();
	}

	private void OnDraw()
	{
		if (_stamina == null || !IsInstanceValid(_stamina)) return;   // the first draw can come before the player exists
		var size = _draw.Size;
		_draw.DrawRect(new Rect2(0, size.Y - 1f, size.X, 1f), Track);
		float w = Mathf.Round(size.X * Mathf.Clamp(_stamina.Value, 0f, 1f));
		if (w >= 1f) _draw.DrawRect(new Rect2(0, 0, w, size.Y), _stamina.CanRun ? Fill : Spent);
	}
}
