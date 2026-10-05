using System.Linq;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The item in hand, bottom-right, inside the cinematic bar (the owner): one name, muted serif, and
/// under it, small, the next item the wheel moves to. Everything carried still works at any time (the
/// camera raises on right mouse, the lantern toggles on F, tools are used by walking up to what they're
/// for); this shows one at a time. The mouse wheel (or the left shoulder button) moves through them,
/// except while the camera is up (the wheel zooms it) or the album is open (it turns the pages). A new
/// pickup comes to hand. Redraws on the inventory's ToolChanged signal or a turn of the wheel, not per
/// frame.
/// </summary>
public partial class EquippedItemHud : CanvasLayer
{
	[Export] public float BoxWidth = 150f;

	private Control _draw;
	private PlayerController _player;
	private PlayerInventory _inv;
	private (ToolKind Kind, string Label)[] _items = System.Array.Empty<(ToolKind, string)>();
	private ToolKind _held = ToolKind.None;
	private float _flash;

	/// <summary>For tests: the item shown in hand.</summary>
	public ToolKind Held => _held;

	public override void _Ready()
	{
		AddToGroup("equipped_item_hud");
		Layer = 13;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_draw.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _Process(double delta)
	{
		if (_inv == null || !IsInstanceValid(_inv))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			_inv = _player?.Inventory;
			if (_inv == null) return;
			_inv.ToolChanged += Refresh;
			Refresh();
		}
		// the wheel moves through what's carried, unless it's zooming the camera or turning the album
		var input = _player?.PlayerInput;
		bool cameraUp = CameraViewfinder.Current is { } vf && IsInstanceValid(vf) && vf.Raise > 0.01f;
		bool albumOpen = GetTree().GetFirstNodeInGroup("photo_log_page") is PhotoLogPage { IsOpen: true };
		if (input != null && !cameraUp && !albumOpen && _items.Length > 1)
		{
			if (input.ItemNextPressed) Step(1);
			else if (input.ItemPrevPressed) Step(-1);
		}
		if (_flash > 0f)
		{
			_flash = Mathf.Max(0f, _flash - (float)delta * 2.5f);
			_draw.QueueRedraw();
		}
		// sit inside the bottom bar when it is there (its height follows the window's shape)
		var size = GetViewport().GetVisibleRect().Size;
		float bar = Mathf.Max(0f, (size.Y - size.X / CinemaFrame.Aspect) * 0.5f);
		float h = 26f;
		float bottom = bar > h + 2f ? -(bar - h) * 0.5f : -8f;
		_draw.OffsetLeft = -BoxWidth - 16; _draw.OffsetRight = -16;
		_draw.OffsetTop = bottom - h; _draw.OffsetBottom = bottom;
	}

	public override void _ExitTree()
	{
		if (_inv != null && IsInstanceValid(_inv)) _inv.ToolChanged -= Refresh;
	}

	private void Step(int dir)
	{
		int i = System.Array.FindIndex(_items, it => it.Kind == _held);
		i = ((i < 0 ? 0 : i) + dir + _items.Length) % _items.Length;
		_held = _items[i].Kind;
		_flash = 1f;
		_draw.QueueRedraw();
	}

	private void Refresh()
	{
		if (_inv == null || !IsInstanceValid(_inv)) { _draw.Visible = false; return; }
		var now = _inv.OwnedItems().ToArray();
		// a new pickup comes to hand; if the one in hand is gone, the first takes its place
		var added = now.Where(n => !_items.Any(o => o.Kind == n.Kind)).ToArray();
		if (_items.Length > 0 && added.Length > 0) { _held = added[^1].Kind; _flash = 1f; }
		else if (!now.Any(n => n.Kind == _held)) _held = now.Length > 0 ? now[0].Kind : ToolKind.None;
		_items = now;
		_draw.Visible = now.Length > 0;
		_draw.QueueRedraw();
	}

	private void OnDraw()
	{
		int i = System.Array.FindIndex(_items, it => it.Kind == _held);
		if (i < 0) return;
		var size = _draw.Size;
		// (no rule above it any more: the owner, 2026-10-03, "it serves no purpose")
		var font = UiKit.Serif;
		// the item in hand
		var pos = new Vector2(0, 15f);
		string name = _items[i].Label;
		var bone = new Color(UiKit.Bone, 0.72f + 0.25f * _flash);
		_draw.DrawString(font, pos + new Vector2(1, 1), name, HorizontalAlignment.Right, size.X, 11, new Color(0, 0, 0, 0.55f));
		_draw.DrawString(font, pos, name, HorizontalAlignment.Right, size.X, 11, bone);
		if (_items.Length < 2) return;
		// under it, small: the next one along, and a little scroll mark
		string next = "next: " + _items[(i + 1) % _items.Length].Label;
		var dim = new Color(UiKit.Fog, 0.5f);
		var npos = new Vector2(0, 24f);
		_draw.DrawString(font, npos, next, HorizontalAlignment.Right, size.X, 7, dim);
		float w = font.GetStringSize(next, HorizontalAlignment.Left, -1, 7).X;
		float x = size.X - w - 7f, y = 21.5f;
		_draw.DrawPolyline(new[] { new Vector2(x - 2.5f, y - 1.5f), new Vector2(x, y - 4f), new Vector2(x + 2.5f, y - 1.5f) }, dim, 1f);
		_draw.DrawPolyline(new[] { new Vector2(x - 2.5f, y + 0.5f), new Vector2(x, y + 3f), new Vector2(x + 2.5f, y + 0.5f) }, dim, 1f);
	}
}
