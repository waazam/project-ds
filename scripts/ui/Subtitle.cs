using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.UI;

/// <summary>
/// A single line, low on screen, that fades in and out on its own —
/// unlike <see cref="ScreenFader"/>'s caption, it never blacks out the world
/// underneath. For voices that speak while play continues, such as the Act 11
/// radio exchange, and for the hints (the lantern's, the story's nudges).
/// Pausable: a line freezes with the game under the pause menu. A newer line
/// replaces an older one; a cancelled line is taken down at once.
///
/// (2026-10-15) Two looks. A spoken line (it opens with a quote) keeps the serif
/// caption, its bottom just above the bottom cinema bar. Anything else is a hint,
/// in the hints' look (<see cref="HintText"/>: the inventory's face, its keys as
/// key caps), on the black of the bottom bar when there is one, wrapped short of
/// the inventory readout at the bar's right. Both follow the "Hint text" size.
/// </summary>
public partial class Subtitle : CanvasLayer
{
	public static Subtitle Instance { get; private set; }

	/// <summary>For the autotest: whether a line is currently faded in (or fading).</summary>
	public bool CurrentlyShowing => (_label != null && _label.Modulate.A > 0.02f) || (_hint != null && _hint.Modulate.A > 0.02f);

	/// <summary>For tests and the preview: whether the line showing is a hint (not a spoken line).</summary>
	public bool ShowingHint => _active == _hint;

	private Label _label;
	private HintText _hint;
	private CanvasItem _active;
	private int _gen;
	private Tween _tween, _outTween;
	private int _labelSize = -1;
	private float _hintTop = float.NaN;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) { Instance = null; HintStyle.SubtitleTop = float.NaN; } }

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
		_hint = new HintText { Modulate = new Color(1, 1, 1, 0) };
		AddChild(_hint);
		_active = _label;
	}

	public override void _Process(double delta)
	{
		var screen = GetViewport().GetVisibleRect().Size;
		if (_labelSize != HintStyle.DialogueSize) { _labelSize = HintStyle.DialogueSize; _label.AddThemeFontSizeOverride("font_size", _labelSize); }
		// the hint: low, in the bar's black when it fits there
		_hint.MaxWidth = HintStyle.LowMaxWidth(screen);
		var block = _hint.BlockSize;
		float top = HintStyle.LowBandTop(screen, block.Y);
		_hintTop = float.IsNaN(_hintTop) || _hint.Modulate.A < 0.01f ? top : Mathf.MoveToward(_hintTop, top, (float)delta * 360f);
		_hint.Position = new Vector2(Mathf.Round(screen.X * 0.5f), Mathf.Round(_hintTop));
		// where the line showing begins, for a thought to step up over it
		if (_hint.Modulate.A > 0.02f) HintStyle.SubtitleTop = _hintTop;
		else if (_label.Modulate.A > 0.02f)
		{
			int lines = Mathf.Max(1, _label.GetVisibleLineCount());
			HintStyle.SubtitleTop = screen.Y - 36f - lines * (_label.GetLineHeight() + 1f);
		}
		else HintStyle.SubtitleTop = float.NaN;
	}

	/// <summary>A spoken line: it opens with a quotation mark.</summary>
	public static bool IsSpoken(string text) => text.Length > 0 && text[0] is '"' or '“' or '\'' or '‘';

	public async Task Show(string text, float fadeIn, float hold, float fadeOut, CancellationToken ct = default)
	{
		if (ct.IsCancellationRequested) return;
		int gen = ++_gen;
		_tween?.Kill();
		CanvasItem target = IsSpoken(text ?? "") ? _label : _hint;
		if (target != _active)
		{
			// the other look was up: it goes quickly, not under the new line
			var old = _active;
			_outTween?.Kill();
			_outTween = CreateTween();
			_outTween.TweenProperty(old, "modulate:a", 0f, Mathf.Min(0.2f, old.Modulate.A * 0.2f + 0.01f));
			_active = target;
		}
		if (target == _label) _label.Text = text;
		else _hint.SetText(text);
		var tween = _tween = CreateTween();
		tween.TweenProperty(target, "modulate:a", 1f, fadeIn);
		if (!await Own(gen, tween, ct)) return;
		double t = 0;
		while (t < hold)
		{
			if (!IsInsideTree()) return;   // (the level changed under it: a reload while a line was showing)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!Owns(gen, ct)) return;
			if (CanProcess()) t += GetProcessDeltaTime();
		}
		tween = _tween = CreateTween();
		tween.TweenProperty(target, "modulate:a", 0f, fadeOut);
		await Own(gen, tween, ct);
	}

	private async Task<bool> Own(int gen, Tween tween, CancellationToken ct)
	{
		while (IsInstanceValid(tween) && tween.IsValid())
		{
			if (!IsInsideTree()) return false;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!Owns(gen, ct)) return false;
		}
		return Owns(gen, ct);
	}

	private bool Owns(int gen, CancellationToken ct)
	{
		if (_gen != gen) return false;
		if (!ct.IsCancellationRequested) return true;
		if (_tween != null && IsInstanceValid(_tween) && _tween.IsValid()) _tween.Kill();
		_label.Modulate = new Color(1, 1, 1, 0);
		_hint.Modulate = new Color(1, 1, 1, 0);
		return false;
	}
}
