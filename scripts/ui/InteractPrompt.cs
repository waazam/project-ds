using Godot;

namespace ProjectDS.UI;

/// <summary>Autoload-free singleton label for "Press E to ..." prompts. One line, bottom-centre.</summary>
public partial class InteractPrompt : CanvasLayer
{
	public static InteractPrompt Instance { get; private set; }

	private Label _label;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 15;
		_label = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
		_label.AddThemeFontSizeOverride("font_size", 11);
		_label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.85f));
		_label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
		_label.AddThemeConstantOverride("shadow_offset_x", 1);
		_label.AddThemeConstantOverride("shadow_offset_y", 1);
		_label.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_label.OffsetLeft = -150; _label.OffsetRight = 150; _label.OffsetTop = -60; _label.OffsetBottom = -40;
		AddChild(_label);
	}

	public void ShowPrompt(string text) { _label.Text = text; _label.Visible = true; }
	public void HidePrompt() => _label.Visible = false;
}
