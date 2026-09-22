using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.UI;

/// <summary>
/// A single dialogue line, low on screen, that fades in and out on its own —
/// unlike <see cref="ScreenFader"/>'s caption, it never blacks out the world
/// underneath. For voices that speak while play continues, such as the Act 11
/// radio exchange. Pausable: a line freezes with the game under the pause menu.
/// Its band is the bottom of the screen, growing upward, clear of the
/// ScreenFader caption band and the interaction prompt. A newer line replaces
/// an older one; a cancelled line is taken down at once.
/// </summary>
public partial class Subtitle : CanvasLayer
{
	public static Subtitle Instance { get; private set; }

	/// <summary>For the autotest: whether a line is currently faded in (or fading).</summary>
	public bool CurrentlyShowing => _label != null && _label.Modulate.A > 0.02f;

	private Label _label;
	private int _gen;
	private Tween _tween;

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

	public async Task Show(string text, float fadeIn, float hold, float fadeOut, CancellationToken ct = default)
	{
		if (ct.IsCancellationRequested) return;
		int gen = ++_gen;
		_tween?.Kill();
		_label.Text = text;
		var tween = _tween = CreateTween();
		tween.TweenProperty(_label, "modulate:a", 1f, fadeIn);
		if (!await Own(gen, tween, ct)) return;
		double t = 0;
		while (t < hold)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!Owns(gen, ct)) return;
			if (CanProcess()) t += GetProcessDeltaTime();
		}
		tween = _tween = CreateTween();
		tween.TweenProperty(_label, "modulate:a", 0f, fadeOut);
		await Own(gen, tween, ct);
	}

	private async Task<bool> Own(int gen, Tween tween, CancellationToken ct)
	{
		while (IsInstanceValid(tween) && tween.IsValid())
		{
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
		return false;
	}
}
