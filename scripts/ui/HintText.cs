using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// (2026-10-15, the owner: "make hints match the inventory font and make it more readable and easy to see without being in
/// the way") The one look of every hint: the interaction prompt, the lantern's and the story's context hints, the thoughts
/// spoken in play, and the modal overlays' control lines. The inventory's face (<see cref="EquippedItemHud"/>: Anton, a hair
/// of letter-spacing, the neon's lit core over a faint halo of its glow, <see cref="NeonKit"/>), in sentence case and a size
/// that reads at 1080p and 1440p (the HUD draws at the window's own resolution with the CRT filter, above the PS2 finish).
/// Under it a soft dark pool, a blurred oval with no edge, so a line reads over snow and over black alike without a box.
/// A key is a small neon key cap (its letter as the player has bound it, or the pad's button once a pad is in use).
/// Where the lines sit, so they never cover the middle of the view or the hands, or stack over each other: the prompt just
/// under the crosshair (<see cref="InteractPrompt"/>), the hints and the thoughts low, in the bottom cinema bar when there is
/// one (<see cref="LowBandTop"/>), a thought stepping up over a hint or a spoken line that's showing.
/// </summary>
public static class HintStyle
{
	private static FontVariation _font;
	/// <summary>The inventory's face: Anton with a hair of letter-spacing.</summary>
	public static Font Font => _font ??= new FontVariation { BaseFont = ChapterCard.Anton ?? UiKit.Serif, SpacingGlyph = 1 };

	/// <summary>The "Hint text" setting's names (accessibility): the size of hints, prompts and spoken lines.</summary>
	public static readonly string[] SizeNames = { "Small", "Normal", "Large" };
	// at the 640x360 canvas (x3 at 1080p, x4 at 1440p)
	private static readonly int[] Sizes = { 9, 10, 12 };
	private static readonly int[] DialogueSizes = { 11, 12, 14 };
	public static int SizeIndex => Mathf.Clamp(GameSettings.Instance?.HintSize ?? 1, 0, Sizes.Length - 1);
	public static int Size => Sizes[SizeIndex];
	/// <summary>A spoken line's size (the serif subtitles) by the same setting.</summary>
	public static int DialogueSize => DialogueSizes[SizeIndex];

	/// <summary>The lit letters: the neon's core, a touch softer than the inventory's (a sentence, not a name).</summary>
	public static Color Ink => new(NeonKit.Core, 0.93f);

	/// <summary>A pad was the last thing used (its buttons are named then, not the keys). Fed by <see cref="Note"/>.</summary>
	public static bool UsingPad { get; set; }

	public static void Note(InputEvent e)
	{
		if (e is InputEventJoypadButton { Pressed: true } || (e is InputEventJoypadMotion m && Mathf.Abs(m.AxisValue) > 0.5f)) UsingPad = true;
		else if (e is InputEventKey { Pressed: true } || e is InputEventMouseButton { Pressed: true }) UsingPad = false;
	}

	// ---- where the low lines sit (one frame behind each other, eased by their owners)

	/// <summary>The top of the spoken line or hint showing in the subtitle band (NaN: none), for the thought and the sound
	/// captions to step up over.</summary>
	public static float SubtitleTop = float.NaN;
	/// <summary>The top of an open overlay's control line in the bar (NaN: none; the choice prompt, the cryptex): the low
	/// lines step up over it.</summary>
	public static float OverlayTop = float.NaN;
	/// <summary>The top of the thought showing (NaN: none), for the sound captions.</summary>
	public static float ThoughtTop = float.NaN;

	/// <summary>The inventory readout's room at the bar's right (its name and icon, right-aligned, about 110 px with the
	/// margin): a low line wraps short of it.</summary>
	public static float LowMaxWidth(Vector2 screen) => Mathf.Clamp(screen.X - 2f * (16f + 110f + 12f), 240f, 380f);

	/// <summary>Where a low block <paramref name="height"/> tall starts: in the middle of the bottom cinema bar when it fits there
	/// (black under it: the best contrast there is, and nothing of the view covered), else just above the bottom edge.</summary>
	/// <summary>The camera's viewfinder is up: its readout fills the bottom bar (2026-10-15, the playtest: the camera's how-to
	/// lay across "1/60 ISO 800"), so the low lines go to the top bar instead.</summary>
	public static bool ViewfinderUp;

	public static float LowBandTop(Vector2 screen, float height, bool overlay = false)
	{
		float bar = CinemaFrame.Bar;
		if (ViewfinderUp) return Mathf.Round(bar >= height + 3f ? bar * 0.5f - height * 0.5f : bar + 8f);
		if (!overlay && !float.IsNaN(OverlayTop)) return Mathf.Round(Mathf.Min(OverlayTop - 3f - height, screen.Y - bar - 8f - height));
		if (bar >= height + 3f) return Mathf.Round(screen.Y - bar * 0.5f - height * 0.5f);
		return Mathf.Round(screen.Y - bar - 8f - height);
	}
}

/// <summary>
/// A hint's lines drawn in <see cref="HintStyle"/>: words, key caps, wrapped to <see cref="MaxWidth"/> and centred on this
/// control's position (its top, or its bottom with <see cref="GrowUp"/>). Reads "B: the blacklight." and "A / D  choose" as
/// keys with what they do; anything else is plain text.
/// </summary>
public partial class HintText : Control
{
	public float MaxWidth = 300f;
	public bool GrowUp;
	/// <summary>How dark the soft pool under the words is (0 none).</summary>
	public float Backing = 0.5f;
	/// <summary>The size relative to <see cref="HintStyle.Size"/>.</summary>
	public float SizeScale = 1f;

	private enum Kind { Word, Key, Small, Gap }
	private readonly record struct Run(Kind Kind, string Text, bool GlueNext = false);

	private readonly List<Run> _runs = new();
	private readonly List<(List<(Run run, float x, float w)> items, float width)> _lines = new();
	private string _text = "";
	private int _laidSize = -1;
	private float _laidWidth = -1f;
	private Vector2 _block;

	private static GradientTexture2D _soft;

	public HintText()
	{
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public string Text => _text;

	/// <summary>The block's size as last laid out (0 when empty).</summary>
	public Vector2 BlockSize { get { Layout(); return _block; } }

	public int FontSize => Mathf.Max(6, Mathf.RoundToInt(HintStyle.Size * SizeScale));

	/// <summary>A line of hint text, parsed for keys.</summary>
	public void SetText(string text)
	{
		text ??= "";
		if (text == _text && _runs.Count > 0) return;
		_text = text;
		_runs.Clear();
		Parse(text, _runs);
		_laidSize = -1;
		QueueRedraw();
	}

	/// <summary>A prompt: its key cap ("HOLD" before it for a held one), then the text as it is. No key: the text alone.</summary>
	public void SetPrompt(string key, bool hold, string text)
	{
		_text = $"{(hold ? "hold " : "")}{key}|{text}";
		_runs.Clear();
		if (!string.IsNullOrEmpty(key))
		{
			if (hold) _runs.Add(new Run(Kind.Small, "HOLD"));
			_runs.Add(new Run(Kind.Key, key, true));
		}
		Words(text, _runs);
		_laidSize = -1;
		QueueRedraw();
	}

	/// <summary>Places itself in the low band (the modal overlays' control lines): centred, in the bottom bar when it fits.</summary>
	public bool LowBand;

	public override void _Process(double delta)
	{
		// the setting or the window can change under it
		if (_laidSize != FontSize) QueueRedraw();
		if (!LowBand) return;
		var screen = GetViewportRect().Size;
		MaxWidth = HintStyle.LowMaxWidth(screen) + 60f;   // (an overlay hides the inventory readout)
		Position = new Vector2(Mathf.Round(screen.X * 0.5f), HintStyle.LowBandTop(screen, BlockSize.Y, overlay: true));
		// an open overlay's controls own the bar: a hint or a thought showing then steps up over them
		if (IsVisibleInTree() && _text.Length > 0) { HintStyle.OverlayTop = Position.Y; _ownsOverlay = true; }
		else if (_ownsOverlay) { HintStyle.OverlayTop = float.NaN; _ownsOverlay = false; }
	}

	private bool _ownsOverlay;

	public override void _ExitTree()
	{
		if (_ownsOverlay) { HintStyle.OverlayTop = float.NaN; _ownsOverlay = false; }
	}

	// ---- parsing

	private static readonly Regex Segments = new(@"\s{3,}");
	// "B: the blacklight."  "Right mouse: gather its beam."  "D-pad down: ..."  "A / D: ..."
	private static readonly Regex KeyColon = new(@"^(?<k>[A-Za-z0-9][A-Za-z0-9\-]*(?: (?:mouse|click|stick|trigger|down|up|left|right))?(?: / [A-Za-z0-9][A-Za-z0-9\-]*)*):\s+(?<r>\S.*)$");
	// the overlays' "A / D  choose", "E  jump"
	private static readonly Regex KeyGap = new(@"^(?<k>[A-Z0-9][A-Za-z0-9\-]*(?: / [A-Z0-9][A-Za-z0-9\-]*)*)\s{2,}(?<r>\S.*)$");

	private static void Parse(string text, List<Run> runs)
	{
		foreach (var line in text.Split('\n'))
		{
			foreach (var seg in Segments.Split(line.Trim()))
			{
				if (seg.Length == 0) continue;
				if (runs.Count > 0) runs.Add(new Run(Kind.Gap, ""));
				var m = KeyColon.Match(seg);
				if (!m.Success) m = KeyGap.Match(seg);
				if (m.Success && m.Groups["k"].Value.Length <= 14)
				{
					var keys = m.Groups["k"].Value.Split(" / ");
					for (int i = 0; i < keys.Length; i++)
					{
						if (i > 0) runs.Add(new Run(Kind.Small, "/", true));
						runs.Add(new Run(Kind.Key, keys[i], true));
					}
					Words(m.Groups["r"].Value, runs);
				}
				else Words(seg, runs);
			}
		}
	}

	private static void Words(string s, List<Run> runs)
	{
		foreach (var w in s.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)) runs.Add(new Run(Kind.Word, w));
	}

	// ---- layout

	private float KeySize(int size) => Mathf.Round(size * 0.82f);
	private static float CapHeight(int size) => Mathf.Round(size * 1.12f);

	private float Measure(Run r, int size)
	{
		var f = HintStyle.Font;
		return r.Kind switch
		{
			Kind.Word => f.GetStringSize(r.Text, HorizontalAlignment.Left, -1, size).X,
			Kind.Key => Mathf.Max(CapHeight(size), f.GetStringSize(r.Text.ToUpperInvariant(), HorizontalAlignment.Left, -1, (int)KeySize(size)).X + size * 0.7f),
			Kind.Small => f.GetStringSize(r.Text, HorizontalAlignment.Left, -1, (int)Mathf.Round(size * 0.78f)).X,
			_ => 0f,
		};
	}

	private void Layout()
	{
		int size = FontSize;
		if (size == _laidSize && Mathf.IsEqualApprox(_laidWidth, MaxWidth)) return;
		_laidSize = size; _laidWidth = MaxWidth;
		_lines.Clear();
		float space = Mathf.Max(HintStyle.Font.GetStringSize(" ", HorizontalAlignment.Left, -1, size).X, size * 0.3f);
		float gap = Mathf.Round(size * 1.4f);
		var cur = new List<(Run, float, float)>();
		float x = 0f;
		bool pendingGap = false;
		for (int i = 0; i < _runs.Count; i++)
		{
			var r = _runs[i];
			if (r.Kind == Kind.Gap) { pendingGap = cur.Count > 0; continue; }
			float w = Measure(r, size);
			// a new segment ("B: its blacklight.") that would split but fits whole on a line of its own starts one
			if (pendingGap)
			{
				float seg = 0f;
				for (int j = i; j < _runs.Count && _runs[j].Kind != Kind.Gap; j++) seg += (j > i ? space : 0f) + Measure(_runs[j], size);
				if (x + gap + seg > MaxWidth && seg <= MaxWidth)
				{
					_lines.Add((new List<(Run, float, float)>(cur), x));
					cur.Clear(); x = 0f; pendingGap = false;
				}
			}
			// a key stays with what follows it (and "HOLD" with its key): the whole glued run must fit
			float need = w;
			for (int j = i; j + 1 < _runs.Count && _runs[j].GlueNext; j++) need += space + Measure(_runs[j + 1], size);
			if (r.Kind == Kind.Small && i + 1 < _runs.Count) need += space + Measure(_runs[i + 1], size);
			float lead = cur.Count == 0 ? 0f : pendingGap ? gap : space;
			bool glued = i > 0 && _runs[i - 1].GlueNext;
			if (cur.Count > 0 && !glued && x + lead + need > MaxWidth)
			{
				_lines.Add((new List<(Run, float, float)>(cur), x));
				cur.Clear(); x = 0f; lead = 0f;
			}
			x += lead;
			cur.Add((r, x, w));
			x += w;
			pendingGap = false;
		}
		if (cur.Count > 0) _lines.Add((cur, x));
		float maxW = 0f;
		foreach (var l in _lines) maxW = Mathf.Max(maxW, l.width);
		_block = _lines.Count == 0 ? Vector2.Zero : new Vector2(maxW, _lines.Count * LineHeight(size));
	}

	private static float LineHeight(int size) => Mathf.Round(size * 1.5f);

	private static Texture2D Soft()
	{
		if (_soft != null) return _soft;
		var g = new Gradient();
		g.Offsets = new[] { 0f, 0.5f, 1f };
		g.Colors = new[] { new Color(0, 0, 0, 1f), new Color(0, 0, 0, 0.7f), new Color(0, 0, 0, 0f) };
		_soft = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 128, Height = 64 };
		return _soft;
	}

	public override void _Draw()
	{
		Layout();
		if (_lines.Count == 0) return;
		int size = FontSize;
		var font = HintStyle.Font;
		float lh = LineHeight(size);
		float top = GrowUp ? -_block.Y : 0f;
		var ink = HintStyle.Ink;
		var glow = NeonKit.Glow;
		if (Backing > 0f)
		{
			float px = size * 2.6f, py = size * 0.75f;
			DrawTextureRect(Soft(), new Rect2(-_block.X * 0.5f - px, top - py, _block.X + 2f * px, _block.Y + 2f * py), false, new Color(1, 1, 1, Backing));
		}
		// Anton's capitals stand about 0.74 of its size: the baseline puts them in the middle of the line
		float capH = size * 0.74f;
		int outline = Mathf.Max(3, Mathf.RoundToInt(size * 0.36f));
		for (int li = 0; li < _lines.Count; li++)
		{
			var (items, width) = _lines[li];
			float x0 = Mathf.Round(-width * 0.5f);
			float baseline = Mathf.Round(top + li * lh + (lh + capH) * 0.5f);
			foreach (var (run, x, w) in items)
			{
				var at = new Vector2(x0 + x, baseline);
				switch (run.Kind)
				{
					case Kind.Word:
						DrawStringOutline(font, at, run.Text, HorizontalAlignment.Left, -1, size, outline, new Color(0, 0, 0, 0.55f));
						DrawStringOutline(font, at, run.Text, HorizontalAlignment.Left, -1, size, 2, new Color(glow, 0.1f));
						DrawString(font, at, run.Text, HorizontalAlignment.Left, -1, size, ink);
						break;
					case Kind.Small:
					{
						int s = Mathf.RoundToInt(size * 0.78f);
						DrawStringOutline(font, at, run.Text, HorizontalAlignment.Left, -1, s, outline, new Color(0, 0, 0, 0.55f));
						DrawString(font, at, run.Text, HorizontalAlignment.Left, -1, s, new Color(ink, ink.A * 0.75f));
						break;
					}
					case Kind.Key:
						DrawKey(at, run.Text, w, size, capH, ink);
						break;
				}
			}
		}
	}

	private static Texture2D _keycap;

	/// <summary>A key cap: (2026-10-31, the owner: "make them look like keyboard key caps and make sure the letter on the key
	/// is centered") a keyboard's key, a dished dark face on a darker skirt (assets/ui/hud/keycap_square.png,
	/// tools/Textures/make_hud.py), drawn in three slices so a long key's name fits, the letter centred on the face by its
	/// glyphs' real height (Anton's capitals stand 0.86 of the size above the baseline: the old 0.74 set them high).
	/// Without the painting, the old dark cap edged in neon.</summary>
	private void DrawKey(Vector2 at, string key, float w, int size, float capH, Color ink)
	{
		float h = CapHeight(size);
		int ks = Mathf.RoundToInt(KeySize(size));
		string label = key.ToUpperInvariant();
		var font = HintStyle.Font;
		// (the font's letter spacing pads each glyph's right: the ink is that much narrower than the advance)
		float tw = font.GetStringSize(label, HorizontalAlignment.Left, -1, ks).X - (font is FontVariation fv ? fv.SpacingGlyph : 0);
		_keycap ??= ResourceLoader.Exists("res://assets/ui/hud/keycap_square.png") ? GD.Load<Texture2D>("res://assets/ui/hud/keycap_square.png") : null;
		if (_keycap != null)
		{
			float kh = h + 5f, kw = Mathf.Max(kh, w + 4f);
			var r = new Rect2(Mathf.Round(at.X + w * 0.5f - kw * 0.5f), Mathf.Round(at.Y - capH * 0.5f - kh * 0.5f), kw, kh);
			float tex = _keycap.GetWidth(), half = tex * 0.5f, endW = kh * 0.5f;
			var tint = new Color(1, 1, 1, ink.A);
			DrawTextureRectRegion(_keycap, new Rect2(r.Position, new Vector2(endW, kh)), new Rect2(0, 0, half, tex), tint);
			if (kw > kh) DrawTextureRectRegion(_keycap, new Rect2(r.Position + new Vector2(endW, 0), new Vector2(kw - kh, kh)), new Rect2(half - 1, 0, 2, tex), tint);
			DrawTextureRectRegion(_keycap, new Rect2(r.Position + new Vector2(kw - endW, 0), new Vector2(endW, kh)), new Rect2(half, 0, half, tex), tint);
			// the face's middle (the painting's face sits a touch above the cap's middle: its skirt is deeper at the front)
			var face = r.GetCenter() + new Vector2(0, -kh * 0.03f);
			var lp = new Vector2(face.X - tw * 0.5f, face.Y + ks * 0.43f);
			DrawString(font, lp, label, HorizontalAlignment.Left, -1, ks, new Color(0.9f, 0.89f, 0.86f, ink.A));
			return;
		}
		var rect = new Rect2(at.X, Mathf.Round(at.Y - capH * 0.5f - h * 0.5f), w, h);
		DrawRect(rect.Grow(1f), new Color(0, 0, 0, 0.45f));
		DrawRect(rect, new Color(0.03f, 0.035f, 0.045f, 0.75f));
		NeonKit.Line(this, NeonKit.Box(rect.Position, rect.Size, Mathf.Min(2.5f, h * 0.2f)), 0.75f, 0.8f, true);
		var p = new Vector2(Mathf.Round(rect.GetCenter().X - tw * 0.5f), Mathf.Round(rect.GetCenter().Y + ks * 0.74f * 0.5f));
		DrawString(font, p, label, HorizontalAlignment.Left, -1, ks, ink);
	}
}
