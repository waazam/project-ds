using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The sheet of paper held up to read: a <see cref="Readable"/>'s text on a
/// paper-coloured card in the middle of the screen, the world dimmed a little
/// behind it. E, Esc or the gamepad's A/B put it down. Nothing pauses; the
/// player's input is made idle through <see cref="PlayerInput.BeginModal"/>
/// while the page is up, and a cutscene taking control closes it.
///
/// Sized for 640x360: the card is 300 px wide. Long texts grow the card down
/// to 300 px and then scroll with the mouse wheel or stick.
///
/// (2026-10-24, the owner: "retexture the paper for all the notes to make it look more rustic and textured", the font
/// "more like handwriting", "some drawings thrown in") Each style is its own paper (assets/ui/paper, painted by
/// tools/Textures/make_paper.py, laid on as a nine-patch so its torn and scorched edges keep): a page torn from a
/// ruled notebook (its lines, margin and punched holes drawn here, in step with the text) for the handwritten, a
/// stained and scorched sheet for the smudged, yellowed typing paper, card stock. The hands are real fonts
/// (<see cref="UiKit.Hand"/>, Scrawl, Caps, Typewriter), and a handwritten note about something gets a pen doodle of
/// it under the writing (<see cref="NoteDoodles"/>).
/// </summary>
public partial class NoteOverlay : CanvasLayer
{
	public static NoteOverlay Instance { get; private set; }

	public bool IsOpen => _root != null && _root.Visible;
	/// <summary>The readable currently shown (null when closed).</summary>
	public Readable Current { get; private set; }

	private const float CardWidth = 300f;
	/// <summary>Typed logs and registers are wider (monospace lines wrap badly at the note width).</summary>
	private const float TypedWidth = 380f;
	/// <summary>Space kept clear above and below the card.</summary>
	private const float ScreenMargin = 18f;
	private const float Pad = 18f;
	/// <summary>The paper's border kept unstretched (its torn or scorched edge; tools/Textures/make_paper.py EDGE).</summary>
	private const int PaperEdge = 14;
	private static readonly Vector2 DoodleSize = new(96f, 72f);
	private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _papers = new();

	private static Texture2D Paper(string name)
	{
		if (_papers.TryGetValue(name, out var t)) return t;
		string path = $"res://assets/ui/paper/{name}.png";
		t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		_papers[name] = t;
		return t;
	}

	private Control _root;
	private ColorRect _wash;
	private PanelContainer _card;
	private StyleBoxFlat _cardStyle;
	private Label _title;
	private RichTextLabel _body;
	private Label _hint;
	private StyleBoxTexture _paper;
	private HBoxContainer _doodleRow;
	private TextureRect _doodle;
	private VBoxContainer _box;
	private bool _ruled;
	private int _seed;
	private PlayerController _player;
	private Tween _tween;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 18;   // above the subtitle band (16), under the fader (20) and the pause menu (30)
		_root = UiKit.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		_wash = new ColorRect { Color = new Color(UiKit.Night, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_wash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(_wash);

		_cardStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.80f, 0.76f, 0.66f),
			ShadowColor = new Color(0, 0, 0, 0.45f),
			ShadowSize = 6,
			ShadowOffset = new Vector2(2, 3),
			ContentMarginLeft = Pad, ContentMarginRight = Pad, ContentMarginTop = Pad - 4, ContentMarginBottom = Pad - 4,
		};
		_paper = new StyleBoxTexture
		{
			TextureMarginLeft = PaperEdge, TextureMarginRight = PaperEdge, TextureMarginTop = PaperEdge, TextureMarginBottom = PaperEdge,
			ContentMarginLeft = Pad, ContentMarginRight = Pad, ContentMarginTop = Pad - 2, ContentMarginBottom = Pad - 4,
		};
		_card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_card.AddThemeStyleboxOverride("panel", _cardStyle);
		_card.Draw += DrawRuling;
		_card.SetAnchorsPreset(Control.LayoutPreset.Center);
		_card.GrowHorizontal = Control.GrowDirection.Both;
		_card.GrowVertical = Control.GrowDirection.Both;
		_root.AddChild(_card);

		var box = _box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.AddThemeConstantOverride("separation", 6);
		_card.AddChild(box);

		_title = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_title.AddThemeFontOverride("font", UiKit.SerifHeading);
		_title.AddThemeFontSizeOverride("font_size", 9);
		box.AddChild(_title);

		_body = new RichTextLabel
		{
			BbcodeEnabled = false,
			FitContent = false,
			ScrollActive = true,
			ScrollFollowing = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(CardWidth - Pad * 2, 0),
		};
		_body.AddThemeConstantOverride("line_separation", 3);
		box.AddChild(_body);
		_body.GetVScrollBar().ValueChanged += _ => _card.QueueRedraw();

		_doodleRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_doodle = new TextureRect
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = DoodleSize,
		};
		_doodleRow.AddChild(_doodle);
		box.AddChild(_doodleRow);

		_hint = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Right,
			Text = "E   put it down",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_hint.AddThemeFontOverride("font", UiKit.Mono);
		_hint.AddThemeFontSizeOverride("font_size", 8);
		_hint.AddThemeColorOverride("font_color", new Color(0.30f, 0.28f, 0.26f, 0.8f));
		box.AddChild(_hint);
	}

	public void Open(Readable note, PlayerController player)
	{
		if (note == null) return;
		if (IsOpen && Current != null) Close();
		_closing = false;
		Current = note;
		_player = player;
		_page = 0;
		Lay(note);

		_player?.PlayerInput.BeginModal();
		_root.Visible = true;
		_root.Modulate = new Color(1, 1, 1, 0);
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(_root, "modulate:a", 1f, 0.15f);
	}

	/// <summary>(2026-10-16) The page shown of a book read page by page (<see cref="Readable.Pages"/>).</summary>
	private int _page;
	private int PageCount => Current?.Pages?.Length ?? 0;

	/// <summary>Lays the note's text (or its current page) on the card, in its style and ink.</summary>
	private void Lay(Readable note)
	{
		var pg = note.Pages is { Length: > 0 } pages ? pages[Mathf.Clamp(_page, 0, pages.Length - 1)] : null;
		var style = pg?.Style ?? note.Style;
		string text = pg != null ? pg.Text ?? "" : note.Text ?? "";
		var (paper, sheet, ink, font, size) = style switch
		{
			Readable.NoteStyle.Typed => (new Color(0.78f, 0.75f, 0.63f), "typed", new Color(0.13f, 0.12f, 0.13f), UiKit.Typewriter, 11),
			Readable.NoteStyle.Printed => (new Color(0.84f, 0.83f, 0.78f), "card", new Color(0.12f, 0.12f, 0.14f), UiKit.Serif, 12),
			Readable.NoteStyle.Smudged => (new Color(0.74f, 0.7f, 0.6f), "rough", new Color(0.2f, 0.17f, 0.22f), UiKit.Scrawl, 17),
			_ => (new Color(0.80f, 0.76f, 0.66f), "note", new Color(0.17f, 0.15f, 0.20f), UiKit.Hand, 13),
		};
		// a note written all in capitals is in shaky block capitals
		if (style == Readable.NoteStyle.Handwritten && Shouting(text)) (font, size) = (UiKit.Caps, 10);
		if (pg?.Ink is Color own) ink = own;
		_cardStyle.BgColor = paper;
		_seed = (int)(Hash(text) & 0x7fffffff);
		var tex = Paper(sheet);
		if (tex != null)
		{
			_paper.Texture = tex;
			_card.AddThemeStyleboxOverride("panel", _paper);
		}
		else _card.AddThemeStyleboxOverride("panel", _cardStyle);
		_ruled = tex != null && style == Readable.NoteStyle.Handwritten;
		bool hand = style is Readable.NoteStyle.Handwritten or Readable.NoteStyle.Smudged;
		_title.AddThemeFontOverride("font", hand ? UiKit.Hand : UiKit.SerifHeading);
		_title.AddThemeFontSizeOverride("font_size", hand ? 11 : 9);
		// the drawing: under the writing, to one side or the middle, a little askew
		var doodle = NoteDoodles.For(text, note.Title, pg != null ? pg.Doodle : note.Doodle, style);
		_doodleRow.Visible = doodle != null;
		if (doodle != null)
		{
			_doodle.Texture = doodle;
			_doodle.Modulate = new Color(ink, style == Readable.NoteStyle.Smudged ? 0.75f : 0.9f);
			_doodleRow.Alignment = (_seed % 3) switch { 0 => BoxContainer.AlignmentMode.Begin, 1 => BoxContainer.AlignmentMode.Center, _ => BoxContainer.AlignmentMode.End };
			_doodle.PivotOffset = DoodleSize * 0.5f;
			_doodle.RotationDegrees = (_seed % 9) - 4f;
		}
		_title.Text = note.Title ?? "";
		_title.Visible = !string.IsNullOrEmpty(note.Title);
		_title.AddThemeColorOverride("font_color", new Color(ink, 0.65f));
		_body.AddThemeFontOverride("normal_font", font);
		_body.AddThemeFontSizeOverride("normal_font_size", size);
		_body.AddThemeColorOverride("default_color", ink);
		_body.BbcodeEnabled = style == Readable.NoteStyle.Smudged;
		_body.Text = style == Readable.NoteStyle.Smudged ? Smudge(text, ink) : text;
		if (style == Readable.NoteStyle.Smudged)
		{
			// the ink run and rubbed: a soft doubled shadow under every stroke
			_body.AddThemeColorOverride("font_shadow_color", new Color(ink, 0.35f));
			_body.AddThemeConstantOverride("shadow_offset_x", 1);
			_body.AddThemeConstantOverride("shadow_offset_y", 1);
			_body.AddThemeConstantOverride("shadow_outline_size", 2);
		}
		else _body.RemoveThemeColorOverride("font_shadow_color");
		_body.ScrollToLine(0);
		_lineFont = font;
		_lineSize = size;
		_card.QueueRedraw();

		// A handwritten sheet is held a little askew; cards and logs sit square.
		_card.RotationDegrees = style == Readable.NoteStyle.Handwritten ? -1.2f : 0f;
		var vis = GetViewport().GetVisibleRect().Size;
		_width = Mathf.Min(style == Readable.NoteStyle.Typed ? TypedWidth : CardWidth, vis.X - 32f);
		_body.CustomMinimumSize = new Vector2(_width - Pad * 2, 40f);
		Place(_width, 120f);
		_layoutTries = 0;
		Callable.From(FitCard).CallDeferred();
	}

	/// <summary>Smudged handwriting: each word's ink a different strength (some nearly rubbed away), a letter
	/// here and there blotted darker. Deterministic per text, so it reads the same every time.</summary>
	private static string Smudge(string text, Color ink)
	{
		var sb = new System.Text.StringBuilder();
		uint h = 2166136261;
		foreach (char ch in text) h = (h ^ ch) * 16777619;
		var rng = new RandomNumberGenerator { Seed = h };
		foreach (var word in text.Split(' '))
		{
			float a = rng.RandfRange(0.42f, 0.95f);
			var c = new Color(ink, a);
			sb.Append($"[color=#{c.ToHtml(true)}]");
			foreach (char ch in word)
			{
				if (rng.Randf() < 0.08f) sb.Append($"[color=#{new Color(ink * 0.6f, 1f).ToHtml(true)}]{Esc(ch)}[/color]");
				else sb.Append(Esc(ch));
			}
			sb.Append("[/color] ");
		}
		return sb.ToString().TrimEnd();
	}

	private static string Esc(char c) => c == '[' ? "[lb]" : c.ToString();

	private static uint Hash(string text)
	{
		uint h = 2166136261;
		foreach (char ch in text ?? "") h = (h ^ ch) * 16777619;
		return h;
	}

	/// <summary>Mostly capitals (a note printed in block letters, like the refs' "THE MONSTERS WERE NEVER UNDER MY BED").</summary>
	private static bool Shouting(string text)
	{
		int up = 0, low = 0;
		foreach (char c in text ?? "") { if (char.IsUpper(c)) up++; else if (char.IsLower(c)) low++; }
		return up > 12 && up > low * 3;
	}

	private Font _lineFont;
	private int _lineSize = 13;

	/// <summary>The notebook's ruling over the paper (under the writing): pale blue lines in step with the text's lines
	/// (scrolling with it), the red margin down the left, three punched holes.</summary>
	private void DrawRuling()
	{
		if (!_ruled || _lineFont == null) return;
		var size = _card.Size;
		// the text's own line pitch, as laid out (the font's metrics alone came out at half of it)
		float lh = _body.GetLineCount() > 1 ? _body.GetLineOffset(1) - _body.GetLineOffset(0) : _lineFont.GetHeight(_lineSize) + _body.GetThemeConstant("line_separation");
		if (lh < 6f) lh = _lineFont.GetHeight(_lineSize) + _body.GetThemeConstant("line_separation");
		float top = _box.Position.Y + _body.Position.Y + (_body.GetLineCount() > 0 ? _body.GetLineOffset(0) : 0f);
		float scroll = (float)_body.GetVScrollBar().Value;
		// each line sits on a rule: the rule a little under the line's baseline
		float phase = top - scroll + _lineFont.GetAscent(_lineSize) + 2f;
		float y0 = 20f, y1 = size.Y - 10f;
		float first = phase - Mathf.Ceil((phase - y0) / lh) * lh;
		var rule = new Color(0.36f, 0.5f, 0.72f, 0.32f);
		for (float y = first; y < y1; y += lh)
			if (y >= y0) _card.DrawLine(new Vector2(6f, Mathf.Round(y) + 0.5f), new Vector2(size.X - 6f, Mathf.Round(y) + 0.5f), rule, 1f);
		var margin = new Color(0.72f, 0.24f, 0.22f, 0.38f);
		_card.DrawLine(new Vector2(Pad - 4f, 12f), new Vector2(Pad - 4f, size.Y - 8f), margin, 1f);
		// the holes: the dark of what's behind, a lip of shadow under each
		foreach (float t in new[] { 0.2f, 0.5f, 0.8f })
		{
			var c = new Vector2(8f, Mathf.Round(size.Y * t));
			_card.DrawCircle(c + new Vector2(0.5f, 0.8f), 3.6f, new Color(0.25f, 0.2f, 0.14f, 0.35f));
			_card.DrawCircle(c, 3.2f, new Color(UiKit.Night, 0.92f));
		}
	}

	private float _width = CardWidth;
	private int _layoutTries;

	/// <summary>
	/// Sizes the card to its text once the text has been laid out at the card's width: as tall as the
	/// text needs, but never taller than the screen allows. Longer texts keep the card at that height
	/// and scroll (wheel), and the hint says so. The card is centred through its offsets (it is
	/// anchored to the screen's centre), so it stays centred whatever the window's aspect.
	/// </summary>
	private void FitCard()
	{
		if (!IsOpen || Current == null) return;
		float content = _body.GetContentHeight();
		if (content <= 1f && _layoutTries++ < 6) { Callable.From(FitCard).CallDeferred(); return; }
		var vis = GetViewport().GetVisibleRect().Size;
		float titleH = _title.Visible ? _title.GetMinimumSize().Y + 6f : 0f;
		float hintH = _hint.GetMinimumSize().Y + 6f;
		float doodleH = _doodleRow.Visible ? DoodleSize.Y + 6f : 0f;
		float chrome = (Pad - 2f) + (Pad - 4f) + titleH + hintH + doodleH;
		float maxBody = Mathf.Max(60f, vis.Y - ScreenMargin * 2f - chrome);
		// a page long enough to scroll keeps its room for the words: no drawing
		if (_doodleRow.Visible && content + 4f > maxBody)
		{
			_doodleRow.Visible = false;
			chrome -= doodleH;
			maxBody += doodleH;
		}
		float bodyH = Mathf.Min(content + 4f, maxBody);
		bool scrolls = content + 4f > maxBody;
		string put = PageCount > 1 && _page < PageCount - 1 ? "E  turn the page" : "E  put it down";
		if (PageCount > 1) put = $"{_page + 1}/{PageCount}      " + put;
		_hint.Text = scrolls ? "wheel  more      " + put : put;
		_body.CustomMinimumSize = new Vector2(_width - Pad * 2f, bodyH);
		Place(_width, chrome + bodyH);
		_card.QueueRedraw();
	}

	private void Place(float w, float h)
	{
		_card.CustomMinimumSize = new Vector2(w, h);
		_card.OffsetLeft = -w * 0.5f; _card.OffsetRight = w * 0.5f;
		_card.OffsetTop = -h * 0.5f; _card.OffsetBottom = h * 0.5f;
		_card.Size = new Vector2(w, h);
		_card.PivotOffset = new Vector2(w, h) * 0.5f;
	}

	public void Close()
	{
		if (!IsOpen) return;
		Current = null;
		_player?.PlayerInput.EndModal();
		_player = null;
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(_root, "modulate:a", 0f, 0.12f);
		_tween.TweenCallback(Callable.From(() => { if (!IsOpen || Current == null) _root.Visible = false; }));
		_root.Visible = true;   // stays visible while it fades; the callback hides it
		_closing = true;
	}

	private bool _closing;

	public override void _Process(double delta)
	{
		if (_closing && _root.Modulate.A <= 0.01f) { _root.Visible = false; _closing = false; }
		if (!IsOpen || _closing) return;
		// A cutscene taking control, or the camera going, puts the page down.
		if (_player != null && (!IsInstanceValid(_player) || !_player.PlayerInput.Enabled)) Close();
	}

	public override void _Input(InputEvent e)
	{
		if (!IsOpen || _closing) return;
		// (2026-10-16) a book: E (or right) turns to the next page, left back; E on the last puts it down
		int turn = e.IsActionPressed("interact") || e.IsActionPressed("ui_right") ? 1 : e.IsActionPressed("ui_left") ? -1 : 0;
		if (turn != 0 && PageCount > 1 && _page + turn >= 0 && _page + turn < PageCount)
		{
			_page += turn;
			Lay(Current);
			Audio.AudioDirector.OneShot(Current, "paper_rustle", 3, null, -14f, "Player");
			GetViewport().SetInputAsHandled();
			return;
		}
		if (e.IsActionPressed("interact") || e.IsActionPressed("pause") || e.IsActionPressed("ui_cancel"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
		else if (e is InputEventMouseButton { Pressed: true } mb && (mb.ButtonIndex == MouseButton.WheelDown || mb.ButtonIndex == MouseButton.WheelUp))
		{
			var bar = _body.GetVScrollBar();
			bar.Value += mb.ButtonIndex == MouseButton.WheelDown ? 24 : -24;
			GetViewport().SetInputAsHandled();
		}
	}
}
