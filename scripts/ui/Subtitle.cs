using System.Threading.Tasks;
using Godot;

namespace ProjectDS.UI;

/// <summary>
/// A single dialogue line, low on screen, that fades in and out on its own —
/// unlike <see cref="ScreenFader"/>'s caption, it never blacks out the world
/// underneath. For voices that speak while play continues, such as the Act 11
/// radio exchange.
/// </summary>
public partial class Subtitle : CanvasLayer
{
	public static Subtitle Instance { get; private set; }

	/// <summary>For the autotest: whether a line is currently faded in (or fading).</summary>
	public bool CurrentlyShowing => _label != null && _label.Modulate.A > 0.02f;

	private Label _label;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 16;
		ProcessMode = ProcessModeEnum.Always;
		_label = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0) };
		_label.AddThemeFontSizeOverride("font_size", 14);
		_label.AddThemeColorOverride("font_color", new Color(0.85f, 0.83f, 0.78f));
		_label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
		_label.AddThemeConstantOverride("shadow_offset_x", 1);
		_label.AddThemeConstantOverride("shadow_offset_y", 1);
		_label.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_label.OffsetLeft = -260; _label.OffsetRight = 260; _label.OffsetTop = -110; _label.OffsetBottom = -80;
		AddChild(_label);
	}

	public async Task Show(string text, float fadeIn, float hold, float fadeOut)
	{
		_label.Text = text;
		var tween = CreateTween();
		tween.TweenProperty(_label, "modulate:a", 1f, fadeIn);
		await ToSignal(tween, Tween.SignalName.Finished);
		await ToSignal(GetTree().CreateTimer(hold), SceneTreeTimer.SignalName.Timeout);
		tween = CreateTween();
		tween.TweenProperty(_label, "modulate:a", 0f, fadeOut);
		await ToSignal(tween, Tween.SignalName.Finished);
	}
}
