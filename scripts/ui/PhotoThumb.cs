using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The print: right after every shot, a small card slides in from the bottom-left
/// edge, sits for two seconds and slides back out. A white border like a print,
/// the frame inside, a tiny exposure stamp on the bottom border (it reads wrong
/// on the stairs' print) and, when the camera recognised something, a short
/// pencil caption under the card. No sound of its own, no pulsing: the shutter
/// already happened. Off entirely with <see cref="ShowPrints"/> (the album still
/// gets every picture).
///
/// Sized for 640x360: a 74x58 card, 16 px in from the left, above the stamina
/// line. Pauses with the tree like the viewfinder.
/// </summary>
public partial class PhotoThumb : CanvasLayer
{
	[Export] public bool ShowPrints = true;
	[Export] public float DelaySeconds = 0.35f;
	[Export] public float SlideInSeconds = 0.35f;
	[Export] public float HoldSeconds = 2.0f;
	[Export] public float SlideOutSeconds = 0.3f;

	private const float CardW = 74f, CardH = 58f, Border = 4f, BottomBorder = 10f;
	private const float ImageW = CardW - Border * 2, ImageH = CardH - Border - BottomBorder;
	private const float CaptionH = 13f;
	private const float Margin = 16f;

	private static readonly Color Paper = new(0.94f, 0.93f, 0.89f);
	private static readonly Color Graphite = new(0.30f, 0.30f, 0.32f, 0.85f);

	private enum Phase { Idle, Delay, In, Hold, Out }

	private readonly Queue<PhotoLog.Photo> _queue = new();
	private PhotoLog.Photo _photo;
	private Phase _phase = Phase.Idle;
	private float _t;
	private float _x;   // card left edge in viewport px
	private Control _draw;
	private PhotoLog _log;

	/// <summary>A print is on screen (for tests).</summary>
	public bool Showing => _phase is Phase.In or Phase.Hold or Phase.Out;
	public string ShowingId => Showing ? _photo?.SubjectId : null;
	/// <summary>The number of the print on screen (for tests), 0 for none.</summary>
	public int ShowingNumber => Showing ? _photo?.Number ?? 0 : 0;

	public override void _Ready()
	{
		Layer = 26;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_draw.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _ExitTree()
	{
		if (_log != null && IsInstanceValid(_log)) _log.Taken -= OnTaken;
	}

	private void OnTaken(PhotoLog.Photo photo)
	{
		if (!ShowPrints) return;
		_queue.Enqueue(photo);
	}

	public override void _Process(double delta)
	{
		if (_log == null || !IsInstanceValid(_log))
		{
			_log = PhotoLog.Instance;
			if (_log != null) _log.Taken += OnTaken;
		}
		float dt = (float)delta;
		_t += dt;
		float hidden = -CardW - 6f;
		switch (_phase)
		{
			case Phase.Idle:
				if (_queue.Count > 0) { _photo = _queue.Dequeue(); _phase = Phase.Delay; _t = 0f; }
				_x = hidden;
				break;
			case Phase.Delay:
				_x = hidden;
				if (_t >= DelaySeconds) { _phase = Phase.In; _t = 0f; }
				break;
			case Phase.In:
			{
				float k = Mathf.Clamp(_t / Mathf.Max(SlideInSeconds, 0.01f), 0f, 1f);
				_x = Mathf.Lerp(hidden, Margin, Mathf.Sin(k * Mathf.Pi * 0.5f));
				if (k >= 1f) { _phase = Phase.Hold; _t = 0f; }
				break;
			}
			case Phase.Hold:
				_x = Margin;
				if (_t >= HoldSeconds) { _phase = Phase.Out; _t = 0f; }
				break;
			case Phase.Out:
			{
				float k = Mathf.Clamp(_t / Mathf.Max(SlideOutSeconds, 0.01f), 0f, 1f);
				_x = Mathf.Lerp(Margin, hidden, 1f - Mathf.Cos(k * Mathf.Pi * 0.5f));
				if (k >= 1f) { _phase = Phase.Idle; _t = 0f; _photo = null; }
				break;
			}
		}
		_draw.Visible = Showing;
		if (_draw.Visible) _draw.QueueRedraw();
	}

	private void OnDraw()
	{
		if (_photo == null) return;
		var size = _draw.Size;
		float x = Mathf.Round(_x);
		// Above the stamina line's corner (bottom-left, 16 px in), the caption clear of the viewfinder's readout line.
		float y = Mathf.Round(size.Y - 46f - CaptionH - CardH);
		var card = new Rect2(x, y, CardW, CardH);

		_draw.DrawRect(new Rect2(card.Position + new Vector2(1, 2), card.Size), new Color(0, 0, 0, 0.4f));
		_draw.DrawRect(card, Paper);
		var img = new Rect2(x + Border, y + Border, ImageW, ImageH);
		if (_photo.Texture != null) _draw.DrawTextureRect(_photo.Texture, img, false);
		else _draw.DrawRect(img, new Color(0.42f, 0.44f, 0.46f));

		// Exposure stamp on the bottom border: a lab's frame print, wrong on the stairs.
		bool wrong = _photo.Wrong;
		string stamp = wrong ? "F--  1/--" : "F2.8  1/60";
		_draw.DrawString(UiKit.Mono, new Vector2(x + Border, y + CardH - 3f), stamp, HorizontalAlignment.Right, ImageW, 7, new Color(Graphite, 0.7f));

		// The pencil caption under the print, bone over the forest with the HUD's soft shadow.
		var font = UiKit.SerifItalic;
		var pos = new Vector2(x + 1f, y + CardH + 10f);
		string caption = _photo.Caption;
		if (caption.Length == 0) return;
		_draw.DrawString(font, pos + new Vector2(1, 1), caption, HorizontalAlignment.Left, -1, 9, new Color(0, 0, 0, 0.6f));
		_draw.DrawString(font, pos, caption, HorizontalAlignment.Left, -1, 9, new Color(UiKit.Bone, 0.85f));
	}
}
