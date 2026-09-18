using System.Threading.Tasks;
using Godot;

namespace ProjectDS.UI;

/// <summary>
/// Black overlay with centred caption lines for title cards and endings.
/// Sits above the post-process layer so text stays clean.
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

		_title = MakeLabel(16, new Color(0.78f, 0.76f, 0.7f));
		_title.OffsetTop = -22; _title.OffsetBottom = 0;
		_subtitle = MakeLabel(9, new Color(0.55f, 0.55f, 0.5f));
		_subtitle.OffsetTop = 6; _subtitle.OffsetBottom = 26;
	}

	private Label MakeLabel(int size, Color color)
	{
		var label = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Modulate = new Color(1, 1, 1, 0),
		};
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		label.AnchorLeft = 0f; label.AnchorRight = 1f;
		label.AnchorTop = 0.5f; label.AnchorBottom = 0.5f;
		AddChild(label);
		return label;
	}

	public bool IsBlack => _black.Color.A > 0.99f;

	public void SetBlack(bool black) => _black.Color = new Color(0, 0, 0, black ? 1 : 0);

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
		await ToSignal(GetTree().CreateTimer(hold), SceneTreeTimer.SignalName.Timeout);
		tween = CreateTween().SetParallel();
		tween.TweenProperty(_title, "modulate:a", 0f, fadeOut);
		tween.TweenProperty(_subtitle, "modulate:a", 0f, fadeOut);
		await ToSignal(tween, Tween.SignalName.Finished);
	}
}
