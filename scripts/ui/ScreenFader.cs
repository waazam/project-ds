using System.Threading.Tasks;
using Godot;

namespace ProjectDS.UI;

/// <summary>
/// Black overlay with centred caption lines for title cards and endings.
/// Sits above the post-process layer so text stays clean. Styled by UiKit;
/// its waits are pausable, so a caption holds under the pause menu.
///
/// Caption bands at 640x360: the title sits just above centre, the caption line
/// starts just below it and wraps downward (three lines still end above the
/// InteractPrompt band). <see cref="Subtitle"/> has its own band at the bottom,
/// so the two never collide.
/// </summary>
public partial class ScreenFader : CanvasLayer
{
	private ColorRect _black;
	private Label _title;
	private Label _subtitle;

	public override void _Ready()
	{
		Layer = 20;
		_black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
		_black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_black);

		_title = MakeLabel(UiKit.HeadingLabel, 18, VerticalAlignment.Bottom);
		_title.OffsetLeft = -250; _title.OffsetRight = 250; _title.OffsetTop = -34; _title.OffsetBottom = -4;
		_subtitle = MakeLabel(UiKit.CaptionLabel, UiKit.CaptionSize, VerticalAlignment.Top);
		_subtitle.OffsetLeft = -230; _subtitle.OffsetRight = 230; _subtitle.OffsetTop = 6; _subtitle.OffsetBottom = 6 + 3 * 16;
	}

	private Label MakeLabel(string variation, int size, VerticalAlignment valign)
	{
		var label = new Label
		{
			Theme = UiKit.Theme,
			ThemeTypeVariation = variation,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = valign,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Modulate = new Color(1, 1, 1, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", size);
		label.AnchorLeft = 0.5f; label.AnchorRight = 0.5f;
		label.AnchorTop = 0.5f; label.AnchorBottom = 0.5f;
		AddChild(label);
		return label;
	}

	public bool IsBlack => _black.Color.A > 0.99f;

	public void SetBlack(bool black) => _black.Color = new Color(0, 0, 0, black ? 1 : 0);

	/// <summary>Direct, per-frame control of the black overlay's alpha (0 = clear, 1 = fully black),
	/// for effects driven frame-by-frame elsewhere (a blink, a strobe) rather than a one-shot tween.</summary>
	public float BlackAlpha
	{
		get => _black.Color.A;
		set { var c = _black.Color; c.A = Mathf.Clamp(value, 0f, 1f); _black.Color = c; }
	}

	public async Task Fade(float toAlpha, float seconds)
	{
		var tween = CreateTween();
		tween.TweenProperty(_black, "color:a", toAlpha, seconds);
		await ToSignal(tween, Tween.SignalName.Finished);
	}

	public async Task ShowCaption(string title, string subtitle, float fadeIn, float hold, float fadeOut)
	{
		_title.Text = title;
		_subtitle.Text = subtitle;
		var tween = CreateTween().SetParallel();
		tween.TweenProperty(_title, "modulate:a", 1f, fadeIn);
		tween.TweenProperty(_subtitle, "modulate:a", 1f, fadeIn).SetDelay(fadeIn * 0.6f);
		await ToSignal(tween, Tween.SignalName.Finished);
		if (hold < 0) return;   // stay up
		await ToSignal(GetTree().CreateTimer(hold, false), SceneTreeTimer.SignalName.Timeout);
		tween = CreateTween().SetParallel();
		tween.TweenProperty(_title, "modulate:a", 0f, fadeOut);
		tween.TweenProperty(_subtitle, "modulate:a", 0f, fadeOut);
		await ToSignal(tween, Tween.SignalName.Finished);
	}
}
