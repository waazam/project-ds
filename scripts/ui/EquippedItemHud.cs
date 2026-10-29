using System.Linq;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// What's in the right hand, bottom-right, inside the cinematic bar (the owner): its icon and its name, and under it,
/// small, the next one the wheel moves to. (2026-10-10c, the owner: "a full overhaul giving it some personality ... a
/// glowing green ... custom animations for the green glow to really make them pop": all of it neon, UI.NeonKit, in the
/// colour chosen in the settings. Its life: the glow breathing slowly; a switch lights the new one like a tube striking (a
/// soft dip, then full); a pickup sends a ring of light out round its icon. Nothing strobes.)
/// (2026-10-13j) It names the right hand's thing (<see cref="PlayerInventory.InHand"/>), which the hands really hold now
/// (<see cref="PlayerHands"/>, <see cref="HeldTool"/>): the wheel, the bumpers and 1-9 switch it there, not here. It shows
/// for a few seconds after a switch or a pickup and fades (with the contextual HUD, the default; off, it stays). Only
/// shown once there's something to hold. Redraws on the inventory's signals, and while its glow lives.
/// </summary>
public partial class EquippedItemHud : CanvasLayer
{
	[Export] public float BoxWidth = 170f;

	private Control _draw;
	private PlayerController _player;
	private PlayerInventory _inv;
	private (ToolKind Kind, string Label)[] _items = System.Array.Empty<(ToolKind, string)>();
	private ToolKind _held = ToolKind.None;
	private float _flash;
	private float _strike = 1f, _ring = 0f;
	private double _clock;
	private int _colour = -1;
	private float _presence = 1f;
	private double _wantedUntil = 5.0;
	private static FontVariation _font;

	/// <summary>For tests: what the right hand holds (None: nothing).</summary>
	public ToolKind Held => _inv != null && IsInstanceValid(_inv) ? _inv.InHand : _held;

	public override void _Ready()
	{
		AddToGroup("equipped_item_hud");
		Layer = 13;
		// (mipmapped: the painted hand is 4x the size it is drawn at, 2026-10-29)
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
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
			_inv.HandChanged += OnHand;
			_held = _inv.InHand;
			Refresh();
		}
		_clock += delta;
		// shown when wanted (HudPresence): a few seconds after a switch or a pickup, or standing still a while
		bool wanted = !HudPresence.Contextual || _strike < 1f || _ring > 0f || _clock < _wantedUntil || HudPresence.StillFor(this, delta) > 2.5f;
		_presence = HudPresence.Ease(_presence, wanted, delta);
		_draw.Modulate = new Color(1, 1, 1, _presence);
		if (_flash > 0f) _flash = Mathf.Max(0f, _flash - (float)delta * 2.5f);
		_strike = Mathf.Min(1f, _strike + (float)delta / 0.55f);
		_ring = Mathf.Max(0f, _ring - (float)delta / 0.9f);
		if (_colour != NeonKit.Index) _colour = NeonKit.Index;
		if (_draw.Visible && _presence > 0.001f) _draw.QueueRedraw();   // (its glow lives: redrawn each frame while it's shown; it's a handful of lines)
		// sit inside the bottom bar when it is there (its height follows the window's shape)
		var size = GetViewport().GetVisibleRect().Size;
		float bar = Mathf.Max(0f, (size.Y - size.X / CinemaFrame.Aspect) * 0.5f);
		float h = 30f;
		float bottom = bar > h + 2f ? -(bar - h) * 0.5f : -8f;
		_draw.OffsetLeft = -BoxWidth - 16; _draw.OffsetRight = -16;
		_draw.OffsetTop = bottom - h; _draw.OffsetBottom = bottom;
	}

	public override void _ExitTree()
	{
		if (_inv != null && IsInstanceValid(_inv)) { _inv.ToolChanged -= Refresh; _inv.HandChanged -= OnHand; }
	}

	/// <summary>The hand's changed: the new thing strikes up in the HUD for a few seconds.</summary>
	private void OnHand()
	{
		if (_inv == null || !IsInstanceValid(_inv)) return;
		bool pickup = _items.Length > 0 && !_items.Any(i => i.Kind == _inv.InHand);
		Refresh();
		if (_inv.InHand == _held) return;
		_held = _inv.InHand;
		_flash = 1f;
		_strike = 0f;
		if (pickup) _ring = 1f;
		_wantedUntil = _clock + (pickup ? 4.5 : 3.0);
		_draw.QueueRedraw();
	}

	/// <summary>Puts <paramref name="kind"/> in the right hand (Act 24 after a Continue or a death: the flamethrower) now if
	/// it's carried, else as soon as it is.</summary>
	public static void Prefer(SceneTree tree, ToolKind kind)
	{
		var inv = (tree?.GetFirstNodeInGroup("player") as PlayerController)?.Inventory;
		if (inv != null && inv.Has(kind)) { inv.Select(kind); return; }
		_preferred = kind;
	}

	private static ToolKind _preferred = ToolKind.None;

	private void Refresh()
	{
		if (_inv == null || !IsInstanceValid(_inv)) { _draw.Visible = false; return; }
		if (_preferred != ToolKind.None && _inv.Has(_preferred)) { var p = _preferred; _preferred = ToolKind.None; _inv.Select(p); }
		_items = _inv.HandChoices().Select(k => (k, PlayerInventory.Label(k))).ToArray();
		_draw.Visible = _items.Length > 1;
		_draw.QueueRedraw();
	}

	private static Texture2D _strip;

	private void OnDraw()
	{
		int i = System.Array.FindIndex(_items, it => it.Kind == _held);
		if (i < 0) return;
		var size = _draw.Size;
		float t = (float)_clock;
		// how lit: breathing slowly; a switch strikes it like a tube (a quick rise, a soft dip, then full: never a flash)
		float breath = 0.8f + 0.1f * Mathf.Sin(t * 1.7f);
		float u = _strike;
		float strike = u >= 1f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp(u / 0.3f, 0f, 1f)) * (1f - 0.35f * Mathf.Exp(-Mathf.Pow((u - 0.45f) / 0.12f, 2f)));
		float lit = breath * strike;
		// (2026-10-25, the HUD's reskin) laid on a strip of old stitched leather, a brass rivet at its end, fading out to
		// the left (assets/ui/hud/item_strip.png): the neon is the lit thing on it, the strip the thing it's set in
		_strip ??= ResourceLoader.Exists("res://assets/ui/hud/item_strip.png") ? GD.Load<Texture2D>("res://assets/ui/hud/item_strip.png") : null;
		if (_strip != null)
		{
			float sw = Mathf.Min(size.X + 6f, 150f), sh = Mathf.Min(size.Y + 4f, 34f);
			_draw.DrawTextureRect(_strip, new Rect2(size.X - sw + 6f, size.Y * 0.5f - sh * 0.5f, sw, sh), false, new Color(1, 1, 1, 0.9f * Mathf.Clamp(strike + 0.3f, 0f, 1f)));
		}
		// the icon at the right (no bracket round it, 2026-10-11, the owner: the corners "look bad")
		float icon = 16f;   // (smaller and quieter, the owner: "shrunk a little to be made more subtle")
		var ic = new Vector2(size.X - icon * 0.5f - 2f, size.Y * 0.5f - 1f);
		NeonKit.Icon(_draw, _held, ic, icon * 0.9f, lit, t);
		// a switch: a short soft glow blooming behind the new item's icon and fading (the owner, 2026-10-10c: no more dot
		// running round the bracket; "a short little glowing animation when you originally change to a new item")
		if (_strike < 1f)
		{
			float g = Mathf.Sin(Mathf.Clamp(_strike, 0f, 1f) * Mathf.Pi);
			_draw.DrawCircle(ic, icon * 0.95f, new Color(NeonKit.Glow, 0.07f * g));
			_draw.DrawCircle(ic, icon * 0.62f, new Color(NeonKit.Glow, 0.12f * g));
			_draw.DrawCircle(ic, icon * 0.32f, new Color(NeonKit.Core, 0.1f * g));
		}
		// a new pickup: a ring of light going out from it
		if (_ring > 0f)
		{
			float r = Mathf.Lerp(icon * 0.45f, icon * 1.3f, 1f - _ring);
			NeonKit.Line(_draw, NeonKit.Arc(ic, r, 0f, Mathf.Tau, 28), 0.8f, _ring * 0.7f);
		}
		// the name, in neon, to the icon's left
		var font = _font ??= new FontVariation { BaseFont = ChapterCard.Anton ?? UiKit.Serif, SpacingGlyph = 1 };
		string name = _items[i].Label.ToUpperInvariant();
		float textRight = size.X - icon - 10f;
		NeonKit.Text(_draw, font, new Vector2(0, 14f), name, HorizontalAlignment.Right, textRight, 9, lit * 0.9f);
		if (_items.Length < 2) return;
		// under it, small and dim: the next one along, its little icon, and a scroll mark
		var next = _items[(i + 1) % _items.Length];
		string nt = next.Label.ToUpperInvariant();
		float dimLit = 0.45f * breath;
		NeonKit.Text(_draw, font, new Vector2(0, 23f), nt, HorizontalAlignment.Right, textRight - 10f, 6, dimLit);
		NeonKit.Icon(_draw, next.Kind, new Vector2(textRight - 4f, 21f), 6.5f, dimLit, t);
		float w = font.GetStringSize(nt, HorizontalAlignment.Left, -1, 6).X;
		float x = textRight - 10f - w - 6f, y = 21f;
		NeonKit.Line(_draw, new[] { new Vector2(x - 2.2f, y - 1.6f), new Vector2(x, y - 3.8f), new Vector2(x + 2.2f, y - 1.6f) }, 0.6f, dimLit);
		NeonKit.Line(_draw, new[] { new Vector2(x - 2.2f, y + 0.6f), new Vector2(x, y + 2.8f), new Vector2(x + 2.2f, y + 0.6f) }, 0.6f, dimLit);
	}
}
