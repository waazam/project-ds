using Godot;

namespace ProjectDS.UI;

/// <summary>
/// The view through the camera while it's raised (right mouse held with the camera
/// in hand): dimmed edges, the 3:2 frame's corner brackets, a small focus square
/// that turns eye-yellow when something is in focus, a film counter and quiet
/// exposure readouts. Drawn at the game's 640x360; purely presentational, so
/// CameraTool tells it what to show.
/// </summary>
public partial class CameraViewfinder : CanvasLayer
{
	public float Raise { get; set; }          // 0 = lowered, 1 = fully raised (eased by CameraTool)
	public bool FocusLocked { get; set; }
	/// <summary>The autofocus's pull (0..1): a bar beside the focus square fills as it comes in.</summary>
	public float FocusLevel { get; set; }
	public float Zoom { get; set; } = 1f;
	public int FramesLeft { get; set; } = 36;
	/// <summary>CameraTool sets this for the one frame it reads back for the print: no brackets, no focus square, no readouts.</summary>
	public bool HideMarks { get; set; }

	/// <summary>The 3:2 frame rectangle in viewport pixels (valid whether or not the overlay is drawing).</summary>
	public Rect2 FrameRect => FrameFor(_draw?.Size ?? new Vector2(640, 360));

	private static Rect2 FrameFor(Vector2 size)
	{
		// The 3:2 frame, centred, as large as fits with a margin.
		float fh = size.Y * 0.78f, fw = fh * 1.5f;
		if (fw > size.X * 0.86f) { fw = size.X * 0.86f; fh = fw / 1.5f; }
		return new Rect2((size - new Vector2(fw, fh)) * 0.5f, new Vector2(fw, fh));
	}

	private static readonly Color Bone = new(0.81f, 0.80f, 0.75f);
	private static readonly Color Eye = new(0.90f, 0.76f, 0.35f);
	private Control _draw;
	private Font _mono;

	/// <summary>The viewfinder in play, if any (the letterbox bars step aside while it is raised).</summary>
	public static CameraViewfinder Current { get; private set; }

	public override void _ExitTree() { if (Current == this) Current = null; }

	public override void _Ready()
	{
		Current = this;
		Layer = 25;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		_draw.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_draw.Draw += OnDraw;
		AddChild(_draw);
		_mono = new SystemFont { FontNames = new[] { "Consolas", "Courier New", "DejaVu Sans Mono", "monospace" } };
	}

	public override void _Process(double delta)
	{
		Visible = Raise > 0.01f;
		if (Visible) _draw.QueueRedraw();
	}

	private void OnDraw()
	{
		float a = Mathf.SmoothStep(0f, 1f, Raise);
		var size = _draw.Size;

		var frame = FrameFor(size);
		float fh = frame.Size.Y;

		// Dim everything outside the frame (the camera body around the eyepiece).
		var dim = new Color(0.03f, 0.035f, 0.045f, 0.72f * a);
		_draw.DrawRect(new Rect2(0, 0, size.X, frame.Position.Y), dim);
		_draw.DrawRect(new Rect2(0, frame.End.Y, size.X, size.Y - frame.End.Y), dim);
		_draw.DrawRect(new Rect2(0, frame.Position.Y, frame.Position.X, fh), dim);
		_draw.DrawRect(new Rect2(frame.End.X, frame.Position.Y, size.X - frame.End.X, fh), dim);
		if (HideMarks) return;

		// Corner brackets.
		var line = new Color(Bone, 0.55f * a);
		float arm = Mathf.Round(fh * 0.09f);
		foreach (var (corner, dx, dy) in new[]
		{
			(frame.Position, 1f, 1f), (new Vector2(frame.End.X, frame.Position.Y), -1f, 1f),
			(new Vector2(frame.Position.X, frame.End.Y), 1f, -1f), (frame.End, -1f, -1f),
		})
		{
			_draw.DrawLine(corner, corner + new Vector2(arm * dx, 0), line, 1f);
			_draw.DrawLine(corner, corner + new Vector2(0, arm * dy), line, 1f);
		}

		// Focus square in the centre: bone while hunting, eye-yellow when locked.
		var c = size * 0.5f;
		float s = Mathf.Round(fh * 0.07f);
		var focus = FocusLocked ? new Color(Eye, 0.85f * a) : new Color(Bone, 0.45f * a);
		var box = new Rect2(c - new Vector2(s, s * 0.75f), new Vector2(s * 2, s * 1.5f));
		DrawCornerBox(box, s * 0.45f, focus);
		// the focus bar: a thin strip right of the square, filling as the lens comes in
		var bar = new Rect2(box.End.X + 6f, box.Position.Y, 2f, box.Size.Y);
		_draw.DrawRect(bar, new Color(Bone, 0.18f * a));
		float fill = Mathf.Clamp(FocusLevel, 0f, 1f) * bar.Size.Y;
		_draw.DrawRect(new Rect2(bar.Position.X, bar.End.Y - fill, bar.Size.X, fill), FocusLocked ? new Color(Eye, 0.8f * a) : new Color(Bone, 0.5f * a));

		// Readouts under the frame: exposure on the left, frames left on the right.
		var text = new Color(Bone, 0.5f * a);
		float y = frame.End.Y + 11f;
		_draw.DrawString(_mono, new Vector2(frame.Position.X, y), $"{Mathf.RoundToInt(35f * Zoom),3}mm   F2.8   1/60   ISO 800", HorizontalAlignment.Left, -1, 8, text);
		// the zoom's steps, top right inside the frame: the wheel moves along them
		float zx = frame.End.X - 44f, zy = frame.Position.Y + 9f;
		_draw.DrawString(_mono, new Vector2(zx - 18f, zy + 3f), $"x{Zoom:0.#}", HorizontalAlignment.Right, 16, 7, text);
		for (int i = 0; i < Player.CameraTool.ZoomSteps.Length; i++)
		{
			bool on = Mathf.IsEqualApprox(Player.CameraTool.ZoomSteps[i], Zoom);
			_draw.DrawRect(new Rect2(zx + i * 8f, zy - (on ? 2f : 1f), 5f, on ? 4f : 2f), on ? new Color(Eye, 0.7f * a) : new Color(Bone, 0.35f * a));
		}
		_draw.DrawString(_mono, new Vector2(frame.End.X - 60, y), $"{FramesLeft,2} EXP", HorizontalAlignment.Right, 60, 8, FramesLeft <= 3 ? new Color(Eye, 0.6f * a) : text);
	}

	private void DrawCornerBox(Rect2 r, float arm, Color col)
	{
		var p = r.Position; var e = r.End;
		_draw.DrawLine(p, p + new Vector2(arm, 0), col); _draw.DrawLine(p, p + new Vector2(0, arm), col);
		_draw.DrawLine(new Vector2(e.X, p.Y), new Vector2(e.X - arm, p.Y), col); _draw.DrawLine(new Vector2(e.X, p.Y), new Vector2(e.X, p.Y + arm), col);
		_draw.DrawLine(new Vector2(p.X, e.Y), new Vector2(p.X + arm, e.Y), col); _draw.DrawLine(new Vector2(p.X, e.Y), new Vector2(p.X, e.Y - arm), col);
		_draw.DrawLine(e, e - new Vector2(arm, 0), col); _draw.DrawLine(e, e - new Vector2(0, arm), col);
	}
}
