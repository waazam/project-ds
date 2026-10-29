using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.UI;

/// <summary>
/// The album: the pictures the player has taken, in the order taken, as small prints on a dark
/// album page. Tab (Back on a pad) holds it up and puts it away again; the game keeps running
/// and the player can walk with it open. Twelve prints to a page (four across, three down), each
/// the same white-bordered print that slides in after a shot, a little askew as if tucked into
/// photo corners, with its pencil caption when the camera recognised something. The newest is
/// last; the album opens on the last page and the wheel turns the pages. "NN exp. left" and the
/// page number are the only figures. Empty: one quiet line.
///
/// It goes away with the camera: shown only while the inventory has it, so the moment the first
/// stairs take the camera the album and the key go dead for good. Closed by a cutscene taking
/// control, by a note being read, and by the camera coming up to the eye.
///
/// (2026-10-29, the playtest) After the prints, the shot list: a page torn from the photographer's notebook
/// (<see cref="PhotoJournal"/>), ruled, in pencil and the note's hand, the shots the story needs in Acts 1 to 3, each
/// ticked with its print taped beside it once it's taken.
///
/// Drawn in code at 640x360 (352x252 px, centred). Layer 14: over the compass and item list,
/// under the prompt, the viewfinder and the pause menu.
/// </summary>
public partial class PhotoLogPage : CanvasLayer
{
	[Export] public string OpenSound = "res://assets/audio/sfx/cloth_02.wav";
	[Export] public float OpenSoundDb = -16f;
	[Export] public string EmptyLine = "No pictures yet.";

	public const int Columns = 4, Rows = 3, PerPage = Columns * Rows;
	private const float CardW = 74f, CardH = 58f, Border = 4f, BottomBorder = 10f;
	private const float CellW = 82f, CellH = 72f;
	private const float PageW = Columns * CellW + 24f, PageH = Rows * CellH + 36f;

	public bool IsOpen { get; private set; }
	/// <summary>The page on show (0-based), for tests.</summary>
	public int Page { get; private set; }
	public int PhotoPages => Mathf.Max(1, (int)Mathf.Ceil((_log?.RecordedCount ?? 0) / (float)PerPage));
	/// <summary>(2026-10-10) The pages: the prints, the shot list (2026-10-29), and after them the notes, once there's a
	/// line to read.</summary>
	public int PageCount => PhotoPages + 1 + (NotesFound > 0 ? 1 : 0) + (PagesRead > 0 ? 1 : 0);
	/// <summary>(2026-10-29) The shot list, the page after the prints.</summary>
	public bool OnJournal => Page == PhotoPages;
	public bool OnNotes => NotesFound > 0 && Page == PhotoPages + 1;
	/// <summary>(2026-10-10b) R.H.'s pages, the album's last page once one has been read.</summary>
	public bool OnPages => PagesRead > 0 && Page == PhotoPages + 1 + (NotesFound > 0 ? 1 : 0);
	/// <summary>Tests and the preview: open it on the shot list.</summary>
	public void OpenJournal() { if (!IsOpen) Open(); Page = PhotoPages; }
	public int PagesRead => World.LorePages.Read;
	public int NotesFound => _log == null ? 0 : System.Linq.Enumerable.Count(PhotoCatalog.All, e => _log.Has(e.Id) && PhotoCatalog.NoteFor(e.Id) != null);

	private static readonly Color PageColor = new(0.1f, 0.095f, 0.09f, 0.94f);
	private static readonly Color PrintPaper = new(0.94f, 0.93f, 0.89f);
	private static readonly Color Graphite = new(0.3f, 0.3f, 0.32f, 0.85f);

	private Control _draw;
	private PlayerController _player;
	private PlayerInventory _inv;
	private CameraTool _camera;
	private PhotoLog _log;
	private float _alpha;

	public override void _Ready()
	{
		AddToGroup("photo_log_page");
		Layer = 14;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
		_draw.SetAnchorsPreset(Control.LayoutPreset.Center);
		_draw.OffsetLeft = -PageW * 0.5f; _draw.OffsetRight = PageW * 0.5f;
		_draw.OffsetTop = -PageH * 0.5f; _draw.OffsetBottom = PageH * 0.5f;
		_draw.Draw += OnDraw;
		AddChild(_draw);
	}

	public override void _ExitTree()
	{
		if (_log != null && IsInstanceValid(_log)) _log.Taken -= OnTaken;
	}

	private void OnTaken(PhotoLog.Photo p)
	{
		PhotoJournal.NoteShot(p);
		if (IsOpen) { Page = PhotoPages - 1; _draw.QueueRedraw(); }
	}

	public override void _Process(double delta)
	{
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			_inv = _player?.GetNodeOrNull<PlayerInventory>("Inventory");
			_camera = _player?.GetNodeOrNull<CameraTool>("CameraTool");
		}
		if (_log == null || !IsInstanceValid(_log))
		{
			_log = PhotoLog.Instance;
			if (_log != null) { _log.Taken += OnTaken; PhotoJournal.Remember(); }
		}

		bool has = _inv != null && IsInstanceValid(_inv) && _inv.HasCamera;
		if (!has)
		{
			if (IsOpen) Close(false);
		}
		else
		{
			var pin = _player.PlayerInput;
			bool raised = _camera != null && _camera.Raising;
			if (IsOpen && (!pin.Enabled || pin.Modal || raised)) Close(false);
			else if (pin.PhotoLogPressed && !raised)
			{
				if (IsOpen) Close(true); else Open();
			}
			else if (IsOpen && pin.ItemNextPressed) Turn(1);
			else if (IsOpen && pin.ItemPrevPressed) Turn(-1);
		}

		float target = IsOpen ? 1f : 0f;
		_alpha = Mathf.MoveToward(_alpha, target, (float)delta / 0.12f);
		_draw.Visible = _alpha > 0.001f;
		if (_draw.Visible) { _draw.Modulate = new Color(1, 1, 1, _alpha); _draw.QueueRedraw(); }
	}

	private void Open()
	{
		IsOpen = true;
		Page = PhotoPages - 1;   // the newest pictures (the notes are the page after)
		Rustle();
	}

	private void Close(bool byHand)
	{
		IsOpen = false;
		if (byHand) Rustle();
	}

	private void Turn(int by)
	{
		int p = Mathf.Clamp(Page + by, 0, PageCount - 1);
		if (p == Page) return;
		Page = p;
		Rustle();
	}

	private void Rustle()
	{
		if (_player == null || !ResourceLoader.Exists(OpenSound)) return;
		var v = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(OpenSound), Bus = "Player", VolumeDb = OpenSoundDb };
		_player.AddChild(v);
		v.Finished += v.QueueFree;
		v.Play();
	}

	/// <summary>A fixed small tilt per print (degrees), as if each was tucked in by hand.</summary>
	private static float Tilt(int n) => (((n * 7919 + 17) % 7) - 3) * 0.45f;

	private void OnDraw()
	{
		var rect = new Rect2(Vector2.Zero, new Vector2(PageW, PageH));
		_draw.DrawRect(new Rect2(rect.Position + new Vector2(2, 3), rect.Size), new Color(0, 0, 0, 0.45f));
		_draw.DrawRect(rect, PageColor);
		_draw.DrawRect(rect.Grow(-3f), new Color(0.3f, 0.28f, 0.26f, 0.35f), false, 1f);

		var photos = _log?.Photos;
		int count = photos?.Count ?? 0;
		var italic = UiKit.SerifItalic;
		if (OnJournal) { DrawJournal(); return; }
		if (OnNotes) { DrawNotes(); return; }
		if (OnPages) { DrawPages(); return; }
		var footer = new Color(0.62f, 0.6f, 0.56f, 0.8f);
		if (count == 0)
			_draw.DrawString(italic, new Vector2(0, PageH * 0.5f + 4f), EmptyLine, HorizontalAlignment.Center, PageW, 11, footer);
		else
		{
			int first = Mathf.Clamp(Page, 0, PageCount - 1) * PerPage;
			for (int i = first; i < Mathf.Min(count, first + PerPage); i++)
			{
				int slot = i - first;
				int col = slot % Columns, row = slot / Columns;
				var centre = new Vector2(12f + col * CellW + CellW * 0.5f, 12f + row * CellH + CardH * 0.5f + 2f);
				DrawPrint(photos[i], centre);
			}
		}
		// Footer: the film left, and the page when there is more than one.
		int left = _camera != null && IsInstanceValid(_camera) ? _camera.FramesLeft : 0;
		_draw.DrawString(UiKit.Mono, new Vector2(12f, PageH - 9f), $"{left} exp. left", HorizontalAlignment.Left, -1, 9, footer);
		if (_log != null)
			_draw.DrawString(UiKit.Mono, new Vector2(0f, PageH - 9f), $"{_log.SubjectsFound}/{PhotoCatalog.Total} found   {_log.TotalScore} pts", HorizontalAlignment.Center, PageW, 9, footer);
		if (PageCount > 1)
			_draw.DrawString(UiKit.Mono, new Vector2(12f, PageH - 9f), $"{Page + 1} / {PageCount}", HorizontalAlignment.Right, PageW - 24f, 9, footer);
	}

	private static Texture2D _notePaper;
	private static StyleBoxTexture _paperBox;
	private static readonly Color Ink = new(0.17f, 0.15f, 0.2f);
	private static readonly Color Pencil = new(0.3f, 0.3f, 0.33f);

	/// <summary>(2026-10-29) The shot list: a ruled notebook page (the notes' own paper, assets/ui/paper/note.png) held a
	/// little askew on the album, its lines in the note's hand (<see cref="UiKit.Hand"/>) under a heading for each act, a
	/// pencil box ticked for each shot taken and its print taped on beside it; a dashed pencil frame where one is still to
	/// take.</summary>
	private void DrawJournal()
	{
		var paperRect = new Rect2(8f, 6f, PageW - 16f, PageH - 12f);
		_draw.DrawSetTransformMatrix(JournalTransform());
		var r = new Rect2(-paperRect.Size * 0.5f, paperRect.Size);
		_notePaper ??= ResourceLoader.Exists("res://assets/ui/paper/note.png") ? GD.Load<Texture2D>("res://assets/ui/paper/note.png") : null;
		if (_notePaper != null)
		{
			_paperBox ??= new StyleBoxTexture { Texture = _notePaper, TextureMarginLeft = 14, TextureMarginRight = 14, TextureMarginTop = 14, TextureMarginBottom = 14 };
			_draw.DrawRect(new Rect2(r.Position + new Vector2(2, 3), r.Size), new Color(0, 0, 0, 0.4f));
			_draw.DrawStyleBox(_paperBox, r);
		}
		else _draw.DrawRect(r, new Color(0.8f, 0.76f, 0.66f));
		// the ruling, the margin, the punched holes (as on a handwritten note: NoteOverlay)
		const float top = 30f, step = 15f;
		for (float y = r.Position.Y + top; y < r.End.Y - 8f; y += step)
			_draw.DrawLine(new Vector2(r.Position.X + 6f, y + 0.5f), new Vector2(r.End.X - 6f, y + 0.5f), new Color(0.36f, 0.5f, 0.72f, 0.3f), 1f);
		float mx = r.Position.X + 22f;
		_draw.DrawLine(new Vector2(mx, r.Position.Y + 10f), new Vector2(mx, r.End.Y - 8f), new Color(0.72f, 0.24f, 0.22f, 0.38f), 1f);
		foreach (float t in new[] { 0.2f, 0.5f, 0.8f })
		{
			var c = new Vector2(r.Position.X + 9f, Mathf.Round(r.Position.Y + r.Size.Y * t));
			_draw.DrawCircle(c + new Vector2(0.5f, 0.8f), 3.6f, new Color(0.25f, 0.2f, 0.14f, 0.35f));
			_draw.DrawCircle(c, 3.2f, new Color(UiKit.Night, 0.92f));
		}
		var hand = UiKit.Hand;
		_draw.DrawString(hand, new Vector2(mx + 6f, r.Position.Y + top - 6f), "Shots for the story", HorizontalAlignment.Left, -1, 15, Ink);
		_draw.DrawString(hand, new Vector2(mx, r.Position.Y + top - 6f), $"{PhotoJournal.DoneCount} of {PhotoJournal.All.Count}", HorizontalAlignment.Right, r.End.X - mx - 10f, 11, new Color(Pencil, 0.8f));

		// two columns of twelve rules; a line takes two (room for its print), a heading in pencil one, where the act changes
		float colW = (r.End.X - mx - 10f) * 0.5f;
		const int rows = 12;
		int n = 0, act = 0;
		foreach (var e in PhotoJournal.All)
		{
			if (e.Act != act)
			{
				act = e.Act;
				if (n % rows >= rows - 2) n = (n / rows + 1) * rows;   // (a heading never last in a column)
				_draw.DrawString(hand, Slot(n++) + new Vector2(0f, -2f), act switch { 1 => "Act I - the trail", 2 => "Act II - the steps", _ => "Act III - his camp" },
					HorizontalAlignment.Left, -1, 10, new Color(Pencil, 0.75f));
			}
			if (n % rows + 2 > rows) n = (n / rows + 1) * rows;
			DrawJournalLine(e, Slot(n), colW);
			n += 2;
		}
		_draw.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
		_draw.DrawString(UiKit.Mono, new Vector2(12f, PageH - 11f), $"{Page + 1} / {PageCount}", HorizontalAlignment.Right, PageW - 26f, 9, new Color(Pencil, 0.6f));

		Vector2 Slot(int at) => new(mx + 6f + at / rows * colW, r.Position.Y + top + step * (at % rows + 1) - 3f);
	}

	/// <summary>One line of the shot list at <paramref name="p"/> (its baseline): the pencil box, ticked when it's taken,
	/// the words, and the print taped on at the end of the line (or a dashed frame waiting for it).</summary>
	private void DrawJournalLine(PhotoJournal.Entry e, Vector2 p, float colW)
	{
		bool done = PhotoJournal.Done(e);
		var box = new Rect2(p + new Vector2(0f, -8f), new Vector2(8f, 8f));
		_draw.DrawRect(box, new Color(Pencil, 0.75f), false, 1f);
		if (done)
		{
			// a pencil tick, two strokes, over the box's edge
			_draw.DrawPolyline(new[] { box.Position + new Vector2(1.5f, 4f), box.Position + new Vector2(3.8f, 7.5f), box.Position + new Vector2(9.5f, -2.5f) },
				new Color(Pencil, 0.9f), 1.4f, true);
		}
		_draw.DrawString(UiKit.Hand, p + new Vector2(13f, 0f), e.Line, HorizontalAlignment.Left, colW - 64f, 12, done ? Ink : new Color(Ink, 0.75f));
		const float pw = 40f, ph = 29f;
		var c = new Vector2(p.X + colW - pw * 0.5f - 10f, p.Y + 4f);
		var print = done ? PhotoJournal.PrintFor(e) : null;
		if (print?.Texture != null)
		{
			float tilt = Mathf.DegToRad(Tilt(print.Number) * 2.2f);
			_draw.DrawSetTransformMatrix(JournalTransform() * new Transform2D(tilt, c));
			var card = new Rect2(-pw * 0.5f, -ph * 0.5f, pw, ph);
			_draw.DrawRect(new Rect2(card.Position + new Vector2(1, 1.5f), card.Size), new Color(0, 0, 0, 0.35f));
			_draw.DrawRect(card, PrintPaper);
			_draw.DrawTextureRect(print.Texture, new Rect2(card.Position + new Vector2(2f, 2f), new Vector2(pw - 4f, ph - 7f)), false);
			// a strip of tape across its top
			_draw.DrawRect(new Rect2(-7f, card.Position.Y - 3f, 14f, 6f), new Color(0.92f, 0.88f, 0.72f, 0.55f));
			_draw.DrawSetTransformMatrix(JournalTransform());
		}
		else
		{
			// where the print will go: a dashed pencil frame
			var card = new Rect2(c - new Vector2(pw, ph) * 0.5f, new Vector2(pw, ph));
			var dash = new Color(Pencil, 0.35f);
			_draw.DrawDashedLine(card.Position, new Vector2(card.End.X, card.Position.Y), dash, 1f, 2.5f);
			_draw.DrawDashedLine(new Vector2(card.End.X, card.Position.Y), card.End, dash, 1f, 2.5f);
			_draw.DrawDashedLine(card.End, new Vector2(card.Position.X, card.End.Y), dash, 1f, 2.5f);
			_draw.DrawDashedLine(new Vector2(card.Position.X, card.End.Y), card.Position, dash, 1f, 2.5f);
		}
	}

	/// <summary>The shot list's page transform (its paper's centre, a little askew).</summary>
	private static Transform2D JournalTransform() => new(Mathf.DegToRad(-0.8f), new Vector2(PageW * 0.5f, PageH * 0.5f));

	/// <summary>R.H.'s pages, in his order, the ones read so far (the rest a blank line each, where they'd go).</summary>
	private void DrawPages()
	{
		var footer = new Color(0.62f, 0.6f, 0.56f, 0.8f);
		_draw.DrawString(UiKit.SerifItalic, new Vector2(14f, 22f), $"R.H.'s pages  ({PagesRead} of {World.LorePages.All.Count})", HorizontalAlignment.Left, -1, 12, new Color(UiKit.Bone, 0.9f));
		float y = 36f;
		var s = Systems.StoryManager.Instance;
		// (2026-10-24: twelve pages now) each read one gets as many lines as the page can spare: three when few are read,
		// down to one (its opening) when many are
		int read = PagesRead, unread = World.LorePages.All.Count - read;
		float room = PageH - 36f - 18f - unread * 12f;
		int lines = Mathf.Clamp((int)(room / Mathf.Max(1, read) / 11f), 1, 3);
		foreach (var p in World.LorePages.All)
		{
			bool isRead = s != null && s.HasFlag(World.LorePages.Flag(p.N));
			string text = isRead ? p.Text : "...";
			_draw.DrawMultilineString(UiKit.SerifItalic, new Vector2(16f, y), text, HorizontalAlignment.Left, PageW - 32f, 8, isRead ? lines : 1, new Color(UiKit.Bone, isRead ? 0.8f : 0.3f));
			y += isRead ? lines * 11f + 2f : 12f;
		}
		_draw.DrawString(UiKit.Mono, new Vector2(12f, PageH - 9f), $"{Page + 1} / {PageCount}", HorizontalAlignment.Right, PageW - 24f, 9, footer);
	}

	/// <summary>The notes page: ruled lines, a pencil line for each thing found, the newest at the bottom (the earliest
	/// drop off the top once the page is full).</summary>
	private void DrawNotes()
	{
		var footer = new Color(0.62f, 0.6f, 0.56f, 0.8f);
		var lines = new System.Collections.Generic.List<string>();
		foreach (var e in PhotoCatalog.All)
			if (_log.Has(e.Id) && PhotoCatalog.NoteFor(e.Id) is { } n) lines.Add(n);
		const float top = 34f, step = 12f;
		int fit = (int)((PageH - top - 18f) / step);
		_draw.DrawString(UiKit.SerifItalic, new Vector2(14f, 22f), "Notes", HorizontalAlignment.Left, -1, 12, new Color(UiKit.Bone, 0.9f));
		for (int i = 0; i < fit; i++)
			_draw.DrawLine(new Vector2(12f, top + i * step + 3f), new Vector2(PageW - 12f, top + i * step + 3f), new Color(0.45f, 0.42f, 0.4f, 0.18f), 1f);
		int first = Mathf.Max(0, lines.Count - fit);
		for (int i = first; i < lines.Count; i++)
			_draw.DrawString(UiKit.SerifItalic, new Vector2(16f, top + (i - first) * step), lines[i], HorizontalAlignment.Left, PageW - 32f, 9, new Color(UiKit.Bone, 0.8f));
		_draw.DrawString(UiKit.Mono, new Vector2(12f, PageH - 9f), $"{Page + 1} / {PageCount}", HorizontalAlignment.Right, PageW - 24f, 9, footer);
	}

	private void DrawPrint(PhotoLog.Photo p, Vector2 centre)
	{
		_draw.DrawSetTransform(centre, Mathf.DegToRad(Tilt(p.Number)), Vector2.One);
		var card = new Rect2(-CardW * 0.5f, -CardH * 0.5f, CardW, CardH);
		_draw.DrawRect(new Rect2(card.Position + new Vector2(1, 2), card.Size), new Color(0, 0, 0, 0.5f));
		_draw.DrawRect(card, PrintPaper);
		var img = new Rect2(card.Position + new Vector2(Border, Border), new Vector2(CardW - Border * 2, CardH - Border - BottomBorder));
		if (p.Texture != null) _draw.DrawTextureRect(p.Texture, img, false);
		if (p.Scored && !p.Wrong)
			UiKit.DrawStars(_draw, new Vector2(card.End.X - Border - 5.5f * 1.15f * 5f, card.End.Y - 5f), p.Stars, 5.5f, new Color(Graphite, 0.9f), new Color(Graphite, 0.2f));
		else
		{
			string stamp = p.Wrong ? "F--  1/--" : "F2.8  1/60";
			_draw.DrawString(UiKit.Mono, new Vector2(card.Position.X + Border, card.End.Y - 3f), stamp, HorizontalAlignment.Right, CardW - Border * 2, 7, new Color(Graphite, 0.7f));
		}
		_draw.DrawString(UiKit.Mono, new Vector2(card.Position.X + Border, card.End.Y - 3f), p.Number.ToString(), HorizontalAlignment.Left, -1, 7, new Color(Graphite, 0.55f));
		_draw.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
		string caption = p.Caption;
		if (caption.Length > 0)
			_draw.DrawString(UiKit.SerifItalic, new Vector2(centre.X - CardW * 0.5f + 1f, centre.Y + CardH * 0.5f + 10f), caption,
				HorizontalAlignment.Left, CardW + 6f, 9, new Color(UiKit.Bone, 0.85f));
	}
}
