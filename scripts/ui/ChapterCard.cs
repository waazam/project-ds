using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The chapters' title drops (2026-10-10c, the owner: "add chapter title cards to the game to make it feel like a book
/// ... Big Bold letters on the screen", with Alan Wake 2's in mind: one word or three in huge, heavy, condensed capitals
/// across the middle of the picture, the chapter's number small under them, over the game going on).
///
/// Eight chapters (docs/outline): 1 Back to Nature (Act 1), 2 The Stairs (Act 2), 3 Root of the Problem (Act 8),
/// 4 Dawn is Breaking (Act 12), 5 Ever Upward (Act 19), 6 The Great Door (Act 21), 7 Enjoy Your Stay (Act 23), 8 Return
/// (Act 25). And between the first two, the game's own name, as the trailer has it: when the forest first goes dead silent
/// round the stairs, DEAD SILENT, in the serif, spaced, bone, and then the second chapter.
///
/// Each shows once in a playthrough (a story flag, "chapter_N", saved), waits for the screen to be clear of black, and
/// never stops the game: it fades in over the picture (a dark band behind it so it reads on snow), drifts very slightly
/// larger while it holds, and fades away. All in an off-white taupe (the owner's choice, from Alan Wake 2's Return).
/// The face is Anton (assets/fonts, SIL Open Font License: free to bundle in a game for sale).
/// </summary>
public partial class ChapterCard : CanvasLayer
{
	public static readonly string[] Titles =
	{
		"", "BACK TO NATURE", "THE STAIRS", "ROOT OF THE PROBLEM", "DAWN IS BREAKING", "EVER UPWARD", "THE GREAT DOOR", "ENJOY YOUR STAY", "RETURN",
	};
	private static readonly string[] Numbers = { "", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT" };
	/// <summary>(the owner: "dont make the chapter font red make it like that 3rd picture kind of off white topeish") An old
	/// paper's off-white, a little taupe; the number under it a shade darker.</summary>
	// (2026-10-11, the owner: "a little harsh and needs to be warmed up a little": a warmer, softer old-paper cream)
	public static readonly Color Taupe = new(0.79f, 0.72f, 0.61f);

	/// <summary>The chapters shown this session (for the tests), in order; 0 is the game's title.</summary>
	public static readonly List<int> Shown = new();
	public static ChapterCard Current { get; private set; }

	private static FontFile _anton;
	public static Font Anton => _anton ??= LoadAnton();

	private static FontFile LoadAnton()
	{
		var f = GD.Load<FontFile>("res://assets/fonts/Anton-Regular.ttf");
		if (f != null) { f.Antialiasing = TextServer.FontAntialiasing.Gray; f.GenerateMipmaps = false; }
		return f;
	}

	public static string Flag(int n) => $"chapter_{n}";

	/// <summary>Chapter <paramref name="n"/>'s card, once in the playthrough, <paramref name="delay"/> seconds from now
	/// (and only once the screen is clear of black).</summary>
	public static void Show(Node from, int n, float delay = 1.5f)
	{
		var s = StoryManager.Instance;
		if (from == null || !from.IsInsideTree() || n < 1 || n >= Titles.Length) return;
		if (s != null && s.HasFlag(Flag(n))) return;
		s?.SetFlag(Flag(n));
		Spawn(from, n, delay);
	}

	/// <summary>The game's name (the trailer's title) and then chapter 2: at the stairs, as the forest goes dead silent.</summary>
	public static void TitleThenStairs(Node from)
	{
		var s = StoryManager.Instance;
		if (from == null || !from.IsInsideTree() || (s != null && s.HasFlag(Flag(2)))) return;
		s?.SetFlag(Flag(2));
		Spawn(from, 0, 1.6f, then: 2);
	}

	private static void Spawn(Node from, int n, float delay, int then = -1)
	{
		var card = new ChapterCard { _n = n, _wait = delay, _then = then, Name = n == 0 ? "TitleDrop" : $"Chapter{n}" };
		(from.GetTree().Root).AddChild(card);
	}

	private int _n, _then;
	private float _wait;
	private double _t = -1;
	private Control _root;
	private Label _title, _sub;
	private TextureRect _band;
	private const float FadeIn = 0.9f, Hold = 4.6f, FadeOut = 1.6f;

	public override void _Ready()
	{
		Layer = 21;   // (over the fader's black, under the pause menu)
		ProcessMode = ProcessModeEnum.Always;
		_root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);
		// the band behind: a soft dark stripe across the middle, so the letters read on snow and on fog
		var grad = new Gradient();
		grad.SetColor(0, new Color(0, 0, 0, 0));
		grad.SetColor(1, new Color(0, 0, 0, 0));
		grad.AddPoint(0.5f, new Color(0, 0, 0, 0.32f));
		_band = new TextureRect
		{
			Texture = new GradientTexture2D { Gradient = grad, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1), Width = 4, Height = 64 },
			StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_band.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(_band);
		bool title = _n == 0;
		var col = title ? UiKit.Bone : Taupe;
		_title = new Label
		{
			Text = title ? "DEAD SILENT" : Titles[_n], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		Font font = title ? UiKit.SerifTitle : Anton ?? UiKit.SerifTitle;
		_title.AddThemeFontOverride("font", font);
		int size = title ? 44 : 92;
		// one line, always: a long title is set smaller to fit the width (Root of the Problem)
		if (!title && font != null)
			while (size > 48 && font.GetStringSize(Titles[_n], HorizontalAlignment.Left, -1, size).X > 500) size -= 4;
		_title.AddThemeFontSizeOverride("font_size", size);
		_title.AddThemeColorOverride("font_color", col);
		// (the owner, 2026-10-11: "way way less shadow and be more subtle", as Alan Wake 2's: a faint soft lift, no
		// hard dark rim round the letters)
		_title.AddThemeColorOverride("font_shadow_color", new Color(0.05f, 0.03f, 0.02f, 0.22f));
		_title.AddThemeConstantOverride("shadow_offset_x", 0);
		_title.AddThemeConstantOverride("shadow_offset_y", 1);
		_title.AddThemeConstantOverride("shadow_outline_size", 2);
		_title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_title.OffsetTop = title ? 0 : -14;
		_root.AddChild(_title);
		_sub = new Label
		{
			Text = title ? "" : $"CHAPTER {Numbers[_n]}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_sub.AddThemeFontOverride("font", new FontVariation { BaseFont = Anton ?? UiKit.Serif, SpacingGlyph = 4 });
		_sub.AddThemeFontSizeOverride("font_size", 13);
		_sub.AddThemeColorOverride("font_color", col.Darkened(0.12f));
		_sub.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_sub.OffsetTop = size * 0.5f + 22;
		_sub.OffsetBottom = size * 0.5f + 22;
		_root.AddChild(_sub);
		_root.Modulate = new Color(1, 1, 1, 0);
		Current = this;
	}

	public override void _Process(double delta)
	{
		if (_t < 0)
		{
			_wait -= (float)delta;
			// (wait for the picture: not while the screen is black, nor while paused)
			var fader = StoryBeat.Fader(this);
			if (_wait > 0 || GetTree().Paused || (fader != null && fader.BlackAlpha > 0.35f)) return;
			_t = 0;
			Shown.Add(_n);
			GD.Print(_n == 0 ? "[story] title: DEAD SILENT" : $"[story] chapter {_n}: {Titles[_n]}");
			if (_n == 0) AudioDirector.OneShot(this, "title_drop", 1, null, -9f, AudioServer.GetBusIndex("Music") >= 0 ? "Music" : "Master", 3f, 0f);
			else AudioDirector.OneShot(this, "chapter_hit", 1, null, -7f, AudioServer.GetBusIndex("Music") >= 0 ? "Music" : "Master", 3f, 0f);
		}
		if (GetTree().Paused) return;
		_t += delta;
		float t = (float)_t, a;
		// the title's letters come in a little after the band and the sound's swell (the blow lands on them)
		float lead = _n == 0 ? 0.6f : 1.15f;
		if (t < lead) a = 0f;
		else if (t < lead + FadeIn) a = Mathf.SmoothStep(0f, 1f, (t - lead) / FadeIn);
		else if (t < lead + FadeIn + Hold) a = 1f;
		else a = 1f - Mathf.SmoothStep(0f, 1f, (t - lead - FadeIn - Hold) / FadeOut);
		_root.Modulate = new Color(1, 1, 1, Mathf.Clamp(a, 0f, 1f));
		// a slow push in while it holds (never a jump)
		float grow = 1f + 0.035f * Mathf.Clamp((t - lead) / (FadeIn + Hold + FadeOut), 0f, 1f);
		_title.PivotOffset = _title.Size * 0.5f;
		_title.Scale = new Vector2(grow, grow);
		if (t > lead + FadeIn + Hold + FadeOut)
		{
			if (Current == this) Current = null;
			if (_then > 0) Spawn(this, _then, 2.2f);
			QueueFree();
		}
	}
}
