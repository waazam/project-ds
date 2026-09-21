using Godot;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// Small translucent bottom-right readout of whichever carried item the mouse
/// wheel has landed on, styled like the compass: a thin outline, no solid
/// backing, muted text. Purely informational — cycling never changes what F
/// or E do, only which name this shows.
/// </summary>
public partial class EquippedItemHud : CanvasLayer
{
	[Export] public float BoxWidth = 132f;
	[Export] public float BoxHeight = 22f;

	private Control _draw;
	private PlayerController _player;
	private PlayerInventory _inv;

	public override void _Ready()
	{
		Layer = 13;
		ProcessMode = ProcessModeEnum.Always;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, CustomMinimumSize = new Vector2(BoxWidth, BoxHeight) };
		_draw.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		_draw.OffsetLeft = -BoxWidth - 16; _draw.OffsetRight = -16;
		_draw.OffsetTop = -BoxHeight - 14; _draw.OffsetBottom = -14;
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _Process(double delta)
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;
		_inv ??= _player.GetNodeOrNull<PlayerInventory>("Inventory");
		if (_inv == null) return;

		if (Input.IsActionJustPressed("item_next")) _inv.CycleSelected(1);
		if (Input.IsActionJustPressed("item_prev")) _inv.CycleSelected(-1);

		bool show = _inv.OwnedItems().Count > 0;
		_draw.Visible = show;
		if (show) _draw.QueueRedraw();
	}

	private void OnDraw()
	{
		var size = _draw.Size;
		string label = _inv.SelectedItem?.Label ?? "";
		var line = new Color(0.85f, 0.85f, 0.8f, 0.35f);
		_draw.DrawRect(new Rect2(0, 0, size.X, 1f), line);
		_draw.DrawString(ThemeDB.FallbackFont, new Vector2(0, size.Y * 0.5f + 3f), label,
			HorizontalAlignment.Right, size.X, 10, new Color(0.82f, 0.8f, 0.74f, 0.75f));
	}
}
