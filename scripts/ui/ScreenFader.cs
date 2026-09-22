using System.Threading;
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
///
/// Two slots: the main caption (title + line) and an "echo" line one row under
/// it, so a second voice a beat behind the first (Act 7's whispers) shows as two
/// lines instead of overwriting the first. Each slot is owned by its latest
/// call: an older call that is superseded returns quietly and never fades the
/// newer text out from under it. Every wait honours its CancellationToken (a
/// cancelled caption is taken down at once).
/// </summary>
public partial class ScreenFader : CanvasLayer
{
	private ColorRect _black;
	private Label _title;
	private Label _subtitle;
	private Label _echo;
	private readonly Slot _main = new();
	private readonly Slot _echoSlot = new();
	private Tween _fadeTween;

	private sealed class Slot
	{
		public Label[] Labels;
		public int Gen;
		public Tween Tween;
	}

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
		// The echo line sits one row under a single-line caption, a little quieter than the voice it follows.
		_echo = MakeLabel(UiKit.CaptionLabel, UiKit.CaptionSize, VerticalAlignment.Top);
		_echo.OffsetLeft = -230; _echo.OffsetRight = 230; _echo.OffsetTop = 6 + 18; _echo.OffsetBottom = 6 + 18 + 2 * 16;
		_echo.SelfModulate = new Color(1, 1, 1, 0.8f);
		_main.Labels = new[] { _title, _subtitle };
		_echoSlot.Labels = new[] { _echo };
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

	/// <summary>For tests: alpha of the caption line / the echo line.</summary>
	public float CaptionAlpha => _subtitle.Modulate.A;
	public float EchoAlpha => _echo.Modulate.A;

	public void SetBlack(bool black) => _black.Color = new Color(0, 0, 0, black ? 1 : 0);

	/// <summary>Direct, per-frame control of the black overlay's alpha (0 = clear, 1 = fully black),
	/// for effects driven frame-by-frame elsewhere (a blink, a strobe) rather than a one-shot tween.</summary>
	public float BlackAlpha
	{
		get => _black.Color.A;
		set { var c = _black.Color; c.A = Mathf.Clamp(value, 0f, 1f); _black.Color = c; }
	}

	/// <summary>Fades the black overlay. A newer fade replaces an older one; cancelled, it stops where it is.</summary>
	public async Task Fade(float toAlpha, float seconds, CancellationToken ct = default)
	{
		if (ct.IsCancellationRequested) return;
		_fadeTween?.Kill();
		var tween = _fadeTween = CreateTween();
		tween.TweenProperty(_black, "color:a", toAlpha, seconds);
		await AwaitTween(tween, ct);
	}

	/// <summary>A title card or narration caption on the main band. hold &lt; 0 = stay up.</summary>
	public Task ShowCaption(string title, string subtitle, float fadeIn, float hold, float fadeOut, CancellationToken ct = default)
		=> Play(_main, new[] { title, subtitle }, fadeIn, hold, fadeOut, ct);

	/// <summary>A second line under the caption (an overlapping echo of a voice), independent of the main band.</summary>
	public Task ShowEcho(string text, float fadeIn, float hold, float fadeOut, CancellationToken ct = default)
		=> Play(_echoSlot, new[] { text }, fadeIn, hold, fadeOut, ct);

	private async Task Play(Slot slot, string[] texts, float fadeIn, float hold, float fadeOut, CancellationToken ct)
	{
		if (ct.IsCancellationRequested) return;
		int gen = ++slot.Gen;
		slot.Tween?.Kill();
		for (int i = 0; i < slot.Labels.Length; i++) slot.Labels[i].Text = texts[i];

		var tween = slot.Tween = CreateTween().SetParallel();
		tween.TweenProperty(slot.Labels[0], "modulate:a", 1f, fadeIn);
		for (int i = 1; i < slot.Labels.Length; i++)
			tween.TweenProperty(slot.Labels[i], "modulate:a", 1f, fadeIn).SetDelay(fadeIn * 0.6f);
		if (!await Own(slot, gen, tween, ct)) return;
		if (hold < 0) return;   // stay up

		// Pausable hold that can still be abandoned (superseded or cancelled) mid-way.
		double t = 0;
		while (t < hold)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!Owns(slot, gen, ct)) return;
			if (CanProcess()) t += GetProcessDeltaTime();
		}

		tween = slot.Tween = CreateTween().SetParallel();
		foreach (var l in slot.Labels) tween.TweenProperty(l, "modulate:a", 0f, fadeOut);
		await Own(slot, gen, tween, ct);
	}

	/// <summary>Waits for the slot's tween while this call still owns the slot. Superseded: returns
	/// false and leaves the labels to the newer call. Cancelled: kills the tween, hides the labels, false.</summary>
	private async Task<bool> Own(Slot slot, int gen, Tween tween, CancellationToken ct)
	{
		while (IsInstanceValid(tween) && tween.IsValid())
		{
			// A level change mid-caption frees this layer: stop quietly rather than touch a dead tree.
			if (!IsInsideTree()) return false;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!IsInstanceValid(this) || !IsInsideTree()) return false;
			if (!Owns(slot, gen, ct)) return false;
		}
		return Owns(slot, gen, ct);
	}

	private bool Owns(Slot slot, int gen, CancellationToken ct)
	{
		if (slot.Gen != gen) return false;   // a newer caption took the band
		if (!ct.IsCancellationRequested) return true;
		if (slot.Tween != null && IsInstanceValid(slot.Tween) && slot.Tween.IsValid()) slot.Tween.Kill();
		foreach (var l in slot.Labels) l.Modulate = new Color(1, 1, 1, 0);
		return false;
	}

	private async Task AwaitTween(Tween tween, CancellationToken ct)
	{
		while (IsInstanceValid(tween) && tween.IsValid())
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!ct.IsCancellationRequested) continue;
			if (IsInstanceValid(tween) && tween.IsValid()) tween.Kill();
			return;
		}
	}
}
