using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The credits' montage (the owner: every picture you took fades in and out, polaroid style, in a
/// collage, to relive the trip). Over the black of the credits, in the order they were taken, each print
/// comes up as a polaroid (a white card with its wide bottom border, the pencil caption and stars on it,
/// a soft shadow, tilted as if dropped on a table) somewhere on the screen, a little over the ones still
/// there; each fades in, sits, and fades away while the next ones come. The last handful stay together
/// a moment as a collage, then go. Slow fades only: nothing flashes.
/// </summary>
public partial class PolaroidMontage : CanvasLayer
{
	/// <summary>Seconds between prints appearing (shortened for a big album so it never drags).</summary>
	public const float Every = 1.15f, Life = 5.2f, FadeIn = 0.9f, FadeOut = 1.1f;
	public const float MaxSeconds = 80f;

	public int Shown { get; private set; }
	public bool Playing { get; private set; }

	private sealed class Card
	{
		public PhotoLog.Photo Photo;
		public Vector2 At;
		public float Tilt, Born, Life;
	}

	private readonly List<Card> _cards = new();
	private Control _draw;
	private float _t;
	private readonly RandomNumberGenerator _rng = new() { Seed = 2026 };

	public override void _Ready()
	{
		Layer = 22;   // over the fader's black (20), under the pause menu
		ProcessMode = ProcessModeEnum.Always;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
		_draw.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_draw.Draw += Paint;
		AddChild(_draw);
	}

	/// <summary>Plays every photo in the album, then returns (the credits carry on).</summary>
	public async Task Play(IReadOnlyList<PhotoLog.Photo> photos, CancellationToken ct)
	{
		if (photos == null || photos.Count == 0) return;
		Playing = true;
		float every = Mathf.Min(Every, (MaxSeconds - Life) / Mathf.Max(1, photos.Count));
		float life = Mathf.Max(Life * Mathf.Clamp(every / Every, 0.6f, 1f), FadeIn + FadeOut + 1.2f);
		int next = 0;
		float nextAt = 0.3f;
		var size = _draw.Size.X > 1 ? _draw.Size : new Vector2(640, 360);
		// the last few gather into a collage and stay a little longer
		int collageFrom = Mathf.Max(0, photos.Count - 7);
		float end = float.MaxValue;
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			_t += (float)GetProcessDeltaTime();
			while (next < photos.Count && _t >= nextAt)
			{
				bool collage = next >= collageFrom;
				_cards.Add(new Card
				{
					Photo = photos[next],
					At = Place(size, next),
					Tilt = _rng.RandfRange(-11f, 11f),
					Born = _t,
					// the collage's cards all leave together
					Life = collage ? float.MaxValue : life,
				});
				Shown++;
				next++;
				nextAt += every;
				if (next == photos.Count) end = _t + FadeIn + 3.2f;
			}
			if (_t >= end)
			{
				foreach (var c in _cards) if (c.Life == float.MaxValue) c.Life = _t - c.Born + FadeOut;
				end = float.MaxValue;
			}
			_cards.RemoveAll(c => _t - c.Born > c.Life);
			_draw.QueueRedraw();
			if (next >= photos.Count && _cards.Count == 0) break;
		}
		Playing = false;
	}

	/// <summary>Somewhere on the screen, spread round so each one lands clear of the last couple.</summary>
	private Vector2 Place(Vector2 size, int n)
	{
		// walk round a loose grid of spots in a shuffled order, jittered
		Vector2[] spots =
		{
			new(0.22f, 0.3f), new(0.68f, 0.62f), new(0.45f, 0.35f), new(0.8f, 0.3f), new(0.25f, 0.68f),
			new(0.55f, 0.66f), new(0.35f, 0.5f), new(0.72f, 0.45f), new(0.15f, 0.48f), new(0.86f, 0.62f),
		};
		var s = spots[(n * 3) % spots.Length];
		return size * s + new Vector2(_rng.RandfRange(-24f, 24f), _rng.RandfRange(-16f, 16f));
	}

	private void Paint()
	{
		const float imgW = 132f, imgH = 88f, side = 8f, top = 8f, bottom = 28f;
		foreach (var c in _cards)
		{
			float age = _t - c.Born;
			float a = Mathf.Min(Mathf.Clamp(age / FadeIn, 0f, 1f), Mathf.Clamp((c.Life - age) / FadeOut, 0f, 1f));
			a = a * a * (3f - 2f * a);
			if (a <= 0.001f) continue;
			// it settles a touch as it comes in, like a print dropped onto the pile
			float settle = 1f + 0.06f * (1f - Mathf.Clamp(age / FadeIn, 0f, 1f));
			_draw.DrawSetTransform(c.At, Mathf.DegToRad(c.Tilt), Vector2.One * settle);
			var card = new Rect2(-(imgW * 0.5f + side), -(imgH * 0.5f + top), imgW + side * 2f, imgH + top + bottom);
			_draw.DrawRect(new Rect2(card.Position + new Vector2(3, 4), card.Size), new Color(0, 0, 0, 0.5f * a));
			_draw.DrawRect(card, new Color(0.95f, 0.94f, 0.9f, a));
			var img = new Rect2(-imgW * 0.5f, -imgH * 0.5f, imgW, imgH);
			if (c.Photo.Texture != null) _draw.DrawTextureRect(c.Photo.Texture, img, false, new Color(1, 1, 1, a));
			else _draw.DrawRect(img, new Color(0.3f, 0.3f, 0.32f, a));
			// the pencil on the bottom border: what it is, and how good
			var ink = new Color(0.25f, 0.25f, 0.28f, 0.85f * a);
			string caption = c.Photo.Caption;
			if (caption.Length > 0) _draw.DrawString(UiKit.SerifItalic, new Vector2(card.Position.X + side, imgH * 0.5f + 12f), caption, HorizontalAlignment.Left, imgW, 9, ink);
			if (c.Photo.Scored) UiKit.DrawStars(_draw, new Vector2(card.End.X - side - 5f * 1.15f * 5f, imgH * 0.5f + 19f), c.Photo.Stars, 5f, ink, new Color(ink, 0.15f * a));
			_draw.DrawString(UiKit.Mono, new Vector2(card.Position.X + side, imgH * 0.5f + 22f), $"#{c.Photo.Number}", HorizontalAlignment.Left, -1, 7, new Color(ink, 0.5f * a));
		}
		_draw.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
	}
}
