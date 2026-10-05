using Godot;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// The flamethrower's heat (Act 24): a thin gauge low in the middle of the view while the wand is in the hands, filling
/// amber to red as it fires and draining as it rests. Heated all the way it locks, and reads VENTING while the seven
/// seconds run down (a steady red: it never flashes).
/// </summary>
public partial class FlamethrowerGauge : CanvasLayer
{
	public Flamethrower Thrower;
	private Control _draw;
	private float _shown;

	public override void _Ready()
	{
		Layer = 12;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		_draw.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _Process(double delta)
	{
		float want = Thrower != null && Thrower.Active ? 1f : 0f;
		_shown = Mathf.MoveToward(_shown, want, (float)delta * 3f);
		_draw.QueueRedraw();
	}

	private void OnDraw()
	{
		if (_shown <= 0.01f || Thrower == null) return;
		var size = _draw.GetViewportRect().Size;
		float bar = Mathf.Max(0f, (size.Y - size.X / CinemaFrame.Aspect) * 0.5f);
		float w = Mathf.Round(size.X * 0.16f), h = Mathf.Max(5f, Mathf.Round(size.Y * 0.008f));
		var at = new Vector2(Mathf.Round((size.X - w) * 0.5f), size.Y - bar - h - Mathf.Round(size.Y * 0.035f));
		float a = _shown;
		_draw.DrawRect(new Rect2(at - Vector2.One * 2f, new Vector2(w + 4f, h + 4f)), new Color(0f, 0f, 0f, 0.55f * a));
		float heat = Thrower.Heat;
		var fill = Thrower.Overheated ? new Color(0.62f, 0.08f, 0.05f, 0.9f * a) : new Color(0.85f, 0.45f, 0.12f, 0.85f * a).Lerp(new Color(0.8f, 0.12f, 0.06f, 0.9f * a), Mathf.SmoothStep(0.55f, 1f, heat));
		_draw.DrawRect(new Rect2(at, new Vector2(w * heat, h)), fill);
		var font = ThemeDB.FallbackFont;
		int fs = Mathf.Max(10, Mathf.RoundToInt(size.Y * 0.016f));
		string label = Thrower.Overheated ? "VENTING" : "HEAT";
		var ts = font.GetStringSize(label, HorizontalAlignment.Left, -1, fs);
		_draw.DrawString(font, new Vector2(at.X + (w - ts.X) * 0.5f, at.Y - 4f), label, HorizontalAlignment.Left, -1, fs, new Color(0.85f, 0.8f, 0.72f, 0.75f * a));
	}
}
