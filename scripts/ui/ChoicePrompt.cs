using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// A two-way choice put to the player in the middle of the screen (Act 14's broken stair: "Jump
/// across?" or "Jump down?"). A / D (or the arrows) moves between the two, E takes the highlighted
/// one, S or Esc steps back from it. Movement is held while it is up, the same modal pattern as
/// <see cref="CryptexOverlay"/>. <see cref="Ask"/> completes with 0 or 1 for the option taken, or
/// -1 for stepping back.
/// </summary>
public partial class ChoicePrompt : CanvasLayer
{
	public static ChoicePrompt Instance { get; private set; }
	public bool IsOpen { get; private set; }
	public int Selected { get; private set; }

	private PlayerController _player;
	private Control _root;
	private Label _a, _b, _hint;
	private string _textA = "", _textB = "";
	private TaskCompletionSource<int> _tcs;
	private float _fade;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 19;
		_root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);
		_a = Option(-1);
		_b = Option(1);
		_hint = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_hint.AddThemeFontOverride("font", UiKit.Mono);
		_hint.AddThemeFontSizeOverride("font_size", 11);
		_hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.68f, 0.62f));
		_hint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
		_hint.AddThemeConstantOverride("shadow_offset_x", 1);
		_hint.AddThemeConstantOverride("shadow_offset_y", 1);
		_hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_hint.OffsetTop = -44; _hint.OffsetBottom = -16;
		_hint.Text = "A / D  choose      E  jump      S  step back";
		_root.AddChild(_hint);
	}

	private Label Option(int side)
	{
		var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontOverride("font", UiKit.Mono);
		l.AddThemeFontSizeOverride("font_size", 22);
		l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
		l.AddThemeConstantOverride("shadow_offset_x", 2);
		l.AddThemeConstantOverride("shadow_offset_y", 2);
		l.AnchorLeft = side < 0 ? 0.18f : 0.52f;
		l.AnchorRight = side < 0 ? 0.48f : 0.82f;
		l.AnchorTop = 0.46f; l.AnchorBottom = 0.56f;
		_root.AddChild(l);
		return l;
	}

	/// <summary>Puts the two options up and waits for the player.</summary>
	public Task<int> Ask(PlayerController player, string a, string b)
	{
		if (IsOpen) Close(-1);
		_player = player;
		_textA = a; _textB = b;
		Selected = 0;
		_fade = 0f;
		IsOpen = true;
		player?.PlayerInput.BeginModal();
		_root.Visible = true;
		_root.Modulate = new Color(1, 1, 1, 0);
		Refresh();
		_tcs = new TaskCompletionSource<int>();
		return _tcs.Task;
	}

	/// <summary>For tests: take option <paramref name="i"/> (or -1 to step back) as if the keys were pressed.</summary>
	public void TestChoose(int i)
	{
		if (!IsOpen) return;
		if (i >= 0) { Selected = i; Refresh(); }
		Close(i);
	}

	private void Close(int result)
	{
		if (!IsOpen) return;
		IsOpen = false;
		_root.Visible = false;
		if (_player != null && GodotObject.IsInstanceValid(_player)) _player.PlayerInput.EndModal();
		_player = null;
		var t = _tcs;
		_tcs = null;
		t?.TrySetResult(result);
	}

	private void Refresh()
	{
		_a.Text = Selected == 0 ? $"[ {_textA} ]" : _textA;
		_b.Text = Selected == 1 ? $"[ {_textB} ]" : _textB;
		_a.AddThemeColorOverride("font_color", Selected == 0 ? new Color(0.95f, 0.9f, 0.8f) : new Color(0.5f, 0.48f, 0.44f));
		_b.AddThemeColorOverride("font_color", Selected == 1 ? new Color(0.95f, 0.9f, 0.8f) : new Color(0.5f, 0.48f, 0.44f));
	}

	public override void _Process(double delta)
	{
		if (!IsOpen) return;
		_fade = Mathf.Min(1f, _fade + (float)delta * 2.5f);
		_root.Modulate = new Color(1, 1, 1, _fade);
	}

	public override void _Input(InputEvent e)
	{
		if (!IsOpen) return;
		if (e.IsActionPressed("ui_cancel") || e.IsActionPressed("pause") || e.IsActionPressed("move_back")) { Close(-1); Handled(); }
		else if (e.IsActionPressed("move_left") || e.IsActionPressed("ui_left")) { Selected = 0; Refresh(); Handled(); }
		else if (e.IsActionPressed("move_right") || e.IsActionPressed("ui_right")) { Selected = 1; Refresh(); Handled(); }
		else if ((e.IsActionPressed("interact") || e.IsActionPressed("ui_accept")) && _fade > 0.4f) { Close(Selected); Handled(); }
	}

	private void Handled() => GetViewport().SetInputAsHandled();
}
