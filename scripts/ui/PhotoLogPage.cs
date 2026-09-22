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
	public int PageCount => Mathf.Max(1, (int)Mathf.Ceil((_log?.RecordedCount ?? 0) / (float)PerPage));

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
		Layer = 14;
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
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

	private void OnTaken(PhotoLog.Photo p) { if (IsOpen) { Page = PageCount - 1; _draw.QueueRedraw(); } }

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
			if (_log != null) _log.Taken += OnTaken;
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
		Page = PageCount - 1;   // the newest pictures
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
		if (PageCount > 1)
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
		string stamp = p.Wrong ? "F--  1/--" : "F2.8  1/60";
		_draw.DrawString(UiKit.Mono, new Vector2(card.Position.X + Border, card.End.Y - 3f), stamp, HorizontalAlignment.Right, CardW - Border * 2, 7, new Color(Graphite, 0.7f));
		_draw.DrawString(UiKit.Mono, new Vector2(card.Position.X + Border, card.End.Y - 3f), p.Number.ToString(), HorizontalAlignment.Left, -1, 7, new Color(Graphite, 0.55f));
		_draw.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
		string caption = p.Caption;
		if (caption.Length > 0)
			_draw.DrawString(UiKit.SerifItalic, new Vector2(centre.X - CardW * 0.5f + 1f, centre.Y + CardH * 0.5f + 10f), caption,
				HorizontalAlignment.Left, CardW + 6f, 9, new Color(UiKit.Bone, 0.85f));
	}
}
