using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The chapters' title drops (2026-10-10c, the owner: "add chapter title cards to the game to make it feel like a book
/// ... Big Bold letters on the screen", with Alan Wake 2's in mind: one word or three in huge, heavy, condensed capitals
/// across the middle of the picture, over the game going on; since 2026-10-13 the title alone, no chapter number).
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

	/// <summary>The game's name (the trailer's title), once in the playthrough: at the foot of the first stairs, in the dead
	/// silence there, the player held looking up the flight (2026-10-15, the owner: "when you get to the stairs it shows the
	/// title right after all sound stops ... the character to be frozen looking at the stairs when the title shows then once
	/// he goes on the stairs it will say the chapter title"; <see cref="World.FirstClimbEvent"/>). With
	/// <paramref name="thenChapter"/> the stairs' chapter follows it (for a player already on the flight). Returns the card,
	/// or null when it has been shown already.</summary>
	public static ChapterCard Title(Node from, float delay = 1.0f, bool thenChapter = false)
	{
		var s = StoryManager.Instance;
		if (from == null || !from.IsInsideTree() || (s != null && s.HasFlag(Flag(0)))) return null;
		s?.SetFlag(Flag(0));
		if (thenChapter) s?.SetFlag(Flag(2));
		return Spawn(from, 0, delay, then: thenChapter ? 2 : -1);
	}

	/// <summary>For the stairs' hold: this card is the game's title (0) or chapter n.</summary>
	public int Number => _n;

	private static ChapterCard Spawn(Node from, int n, float delay, int then = -1)
	{
		var card = new ChapterCard { _n = n, _wait = delay, _then = then, Name = n == 0 ? "TitleDrop" : $"Chapter{n}" };
		(from.GetTree().Root).AddChild(card);
		return card;
	}

	private int _n, _then;
	private float _wait;
	private double _t = -1;
	private Control _root;
	private Label _title;
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
		_title.OffsetTop = 0;   // (2026-10-13, Dan: "just say the title of the chapter": no "CHAPTER N" line under it)
		_root.AddChild(_title);
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
			// (2026-10-13: both tuned on the Music bus at -24 dB; it sits at -16 now (GameSettings.MusicBaseDb), so 8 dB back)
			if (_n == 0) AudioDirector.OneShot(this, "title_drop", 1, null, -17f, AudioServer.GetBusIndex("Music") >= 0 ? "Music" : "Master", 3f, 0f);
			else AudioDirector.OneShot(this, "chapter_hit", 1, null, -15f, AudioServer.GetBusIndex("Music") >= 0 ? "Music" : "Master", 3f, 0f);
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
