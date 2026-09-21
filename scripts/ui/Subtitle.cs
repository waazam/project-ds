using System.Threading.Tasks;
using Godot;

namespace ProjectDS.UI;

/// <summary>
/// A single dialogue line, low on screen, that fades in and out on its own —
/// unlike <see cref="ScreenFader"/>'s caption, it never blacks out the world
/// underneath. For voices that speak while play continues, such as the Act 11
/// radio exchange. Pausable: a line freezes with the game under the pause menu.
/// Its band is the bottom of the screen, growing upward, clear of the
/// ScreenFader caption band and the interaction prompt.
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
		_label = new Label
		{
			Theme = UiKit.Theme,
			ThemeTypeVariation = UiKit.CaptionLabel,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Bottom,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Modulate = new Color(1, 1, 1, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_label.AnchorLeft = 0.5f; _label.AnchorRight = 0.5f; _label.AnchorTop = 1f; _label.AnchorBottom = 1f;
		_label.OffsetLeft = -240; _label.OffsetRight = 240; _label.OffsetTop = -36 - 2 * 16; _label.OffsetBottom = -36;
		_label.GrowVertical = Control.GrowDirection.Begin;
		AddChild(_label);
	}

	public async Task Show(string text, float fadeIn, float hold, float fadeOut)
	{
		_label.Text = text;
		var tween = CreateTween();
		tween.TweenProperty(_label, "modulate:a", 1f, fadeIn);
		await ToSignal(tween, Tween.SignalName.Finished);
		await ToSignal(GetTree().CreateTimer(hold, false), SceneTreeTimer.SignalName.Timeout);
		tween = CreateTween();
		tween.TweenProperty(_label, "modulate:a", 0f, fadeOut);
		await ToSignal(tween, Tween.SignalName.Finished);
	}
}
