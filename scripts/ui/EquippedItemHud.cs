using System.Linq;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// Small translucent bottom-right list of everything the player carries, styled
/// like the compass: a thin rule, no solid backing, muted serif text, one item
/// per line. There is no selecting: every item works at any time (the camera
/// raises on right mouse, the lantern toggles on F, tools are used by walking up
/// to what they're for), so this only shows what's in the pack.
/// </summary>
public partial class EquippedItemHud : CanvasLayer
{
	[Export] public float BoxWidth = 132f;
	[Export] public float LineHeight = 13f;

	private Control _draw;
	private PlayerController _player;
	private PlayerInventory _inv;
	private string[] _shown = System.Array.Empty<string>();

	public override void _Ready()
	{
		Layer = 13;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_draw.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _Process(double delta)
	{
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			_inv = _player?.GetNodeOrNull<PlayerInventory>("Inventory");
		}
		if (_inv == null || !IsInstanceValid(_inv)) { _draw.Visible = false; return; }

		var labels = _inv.OwnedItems().Select(i => i.Label).ToArray();
		_draw.Visible = labels.Length > 0;
		if (labels.SequenceEqual(_shown)) return;
		_shown = labels;
		// Grow upward from the bottom-right corner, one line per item.
		float h = 6f + LineHeight * labels.Length;
		_draw.OffsetLeft = -BoxWidth - 16; _draw.OffsetRight = -16;
		_draw.OffsetTop = -h - 12; _draw.OffsetBottom = -12;
		_draw.QueueRedraw();
	}

	private void OnDraw()
	{
		var size = _draw.Size;
		_draw.DrawRect(new Rect2(size.X * 0.35f, 0, size.X * 0.65f, 1f), new Color(UiKit.Fog, 0.45f));
		var font = UiKit.Serif;
		const int fontSize = 10;
		for (int i = 0; i < _shown.Length; i++)
		{
			var pos = new Vector2(0, 6f + LineHeight * i + 8f);
			_draw.DrawString(font, pos + new Vector2(1, 1), _shown[i], HorizontalAlignment.Right, size.X, fontSize, new Color(0, 0, 0, 0.55f));
			_draw.DrawString(font, pos, _shown[i], HorizontalAlignment.Right, size.X, fontSize, new Color(UiKit.Bone, 0.72f));
		}
	}
}
