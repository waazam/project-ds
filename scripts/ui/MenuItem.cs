using Godot;

namespace ProjectDS.UI;

/// <summary>
/// A plain-text menu entry. No box: when it has focus (mouse hover moves focus
/// here too, so only one entry is ever lit) its text turns bone and a small
/// eye-yellow mark sits to its left, with a faint underline.
/// </summary>
public partial class MenuItem : Button
{
	/// <summary>Where the glyph and underline sit: false = left-aligned column, true = centred.</summary>
	public bool Centered;

	private float _lit;

	public MenuItem()
	{
		Flat = true;
		FocusMode = FocusModeEnum.All;
		MouseDefaultCursorShape = CursorShape.PointingHand;
		Alignment = HorizontalAlignment.Left;
		CustomMinimumSize = new Vector2(0, 17);
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MouseEntered += () => { if (!Disabled) GrabFocus(); };
		FocusEntered += QueueRedraw;
		FocusExited += QueueRedraw;
		if (Centered) Alignment = HorizontalAlignment.Center;
		// Leave room on the left for the glyph.
		AddThemeConstantOverride("h_separation", 0);
		var pad = new StyleBoxEmpty { ContentMarginLeft = Centered ? 0 : 12 };
		foreach (var s in new[] { "normal", "hover", "pressed", "focus", "disabled", "hover_pressed" })
			AddThemeStyleboxOverride(s, pad);
	}

	public override void _Process(double delta)
	{
		float target = HasFocus() && !Disabled ? 1f : 0f;
		if (Mathf.IsEqualApprox(_lit, target)) return;
		_lit = Mathf.MoveToward(_lit, target, (float)delta * 6f);
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_lit <= 0.01f) return;
		var font = GetThemeFont("font");
		int size = GetThemeFontSize("font_size");
		float textW = font.GetStringSize(Text, HorizontalAlignment.Left, -1, size).X;
		float left = Centered ? (Size.X - textW) * 0.5f : 12f;
		float midY = Size.Y * 0.5f;
		var eye = new Color(UiKit.Eye, 0.9f * _lit);
		// A small diamond: the watching eye.
		float gx = left - 8f, gy = midY + 0.5f;
		DrawColoredPolygon(new[] { new Vector2(gx, gy - 2.5f), new Vector2(gx + 2.5f, gy), new Vector2(gx, gy + 2.5f), new Vector2(gx - 2.5f, gy) }, eye);
		// Underline, growing out from the glyph.
		float w = textW * _lit;
		DrawLine(new Vector2(left, Size.Y - 2f), new Vector2(left + w, Size.Y - 2f), new Color(UiKit.Eye, 0.35f * _lit), 1f);
	}
}
