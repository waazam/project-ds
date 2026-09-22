using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.UI;

/// <summary>
/// The shot list: a torn spiral-notebook page in the friend's hand, tucked into
/// the camera strap. Tab (Back on a pad) holds it up on the left of the screen
/// and puts it away again; the game keeps running and the player can walk with
/// it open. Nine listed lines with pencil boxes that get ticked, a pencil rule,
/// and under it the lines that write themselves in when something unlisted is
/// photographed. "NN exp. left" in the corner is the only number.
///
/// It goes away with the camera: shown only while the inventory has it, so the
/// moment the first stairs take the camera the page and the key go dead for
/// good. Closed by a cutscene taking control, by a note being read, and by the
/// camera coming up to the eye (you cannot read with the camera at your eye;
/// the toggle is ignored then).
///
/// Drawn in code at 640x360 (184x236 px, 16 px in from the left, vertically
/// centred) on the existing procedural paper texture. Layer 14: over the
/// compass and item list, under the prompt, the viewfinder and the pause menu.
/// </summary>
public partial class PhotoLogPage : CanvasLayer
{
	[Export] public float PageWidth = 184f;
	[Export] public float PageHeight = 236f;
	[Export] public float LineStep = 13f;
	[Export] public string OpenSound = "res://assets/audio/sfx/cloth_02.wav";
	[Export] public float OpenSoundDb = -16f;

	public bool IsOpen { get; private set; }

	private static readonly Color Graphite = new(0.22f, 0.22f, 0.24f, 0.9f);
	private static readonly Color Pencil = new(0.28f, 0.27f, 0.29f, 0.8f);
	private static readonly Color Rule = new(0.5f, 0.56f, 0.68f, 0.32f);
	private static readonly Color MarginLine = new(0.72f, 0.42f, 0.4f, 0.32f);

	private Control _draw;
	private PlayerController _player;
	private PlayerInventory _inv;
	private CameraTool _camera;
	private PhotoLog _log;
	private Texture2D _paper;
	private Vector2[] _outline;
	private Vector2[] _uvs;
	private float _alpha;

	public override void _Ready()
	{
		Layer = 14;
		_draw = new Control
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
			TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		_draw.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
		_draw.OffsetLeft = 16; _draw.OffsetRight = 16 + PageWidth;
		_draw.OffsetTop = -PageHeight * 0.5f; _draw.OffsetBottom = PageHeight * 0.5f;
		_draw.Draw += OnDraw;
		AddChild(_draw);
		_paper = ProcTextures.Paper();
		BuildOutline();
	}

	public override void _ExitTree()
	{
		if (_log != null && IsInstanceValid(_log)) _log.Recorded -= OnRecorded;
	}

	private void OnRecorded(PhotoLog.Entry e, ImageTexture t) { if (IsOpen) _draw.QueueRedraw(); }

	/// <summary>The torn top edge and the page body, with UVs in paper-texture tiles.</summary>
	private void BuildOutline()
	{
		var pts = new System.Collections.Generic.List<Vector2>();
		var rng = new RandomNumberGenerator { Seed = 1937 };
		int tears = 9;
		for (int i = 0; i <= tears; i++)
		{
			float x = PageWidth * i / tears;
			float y = i == 0 || i == tears ? 3f : 3f + rng.RandfRange(-2.5f, 2.5f);
			pts.Add(new Vector2(x, y));
		}
		pts.Add(new Vector2(PageWidth, PageHeight));
		pts.Add(new Vector2(0, PageHeight));
		_outline = pts.ToArray();
		_uvs = new Vector2[_outline.Length];
		for (int i = 0; i < _outline.Length; i++) _uvs[i] = _outline[i] / 16f;
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
			if (_log != null) _log.Recorded += OnRecorded;
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
		}

		float target = IsOpen ? 1f : 0f;
		_alpha = Mathf.MoveToward(_alpha, target, (float)delta / 0.12f);
		_draw.Visible = _alpha > 0.001f;
		if (_draw.Visible) { _draw.Modulate = new Color(1, 1, 1, _alpha); _draw.QueueRedraw(); }
	}

	private void Open()
	{
		IsOpen = true;
		Rustle();
	}

	private void Close(bool byHand)
	{
		IsOpen = false;
		if (byHand) Rustle();
	}

	private void Rustle()
	{
		if (_player == null || !ResourceLoader.Exists(OpenSound)) return;
		var v = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(OpenSound), Bus = "Player", VolumeDb = OpenSoundDb };
		_player.AddChild(v);
		v.Finished += v.QueueFree;
		v.Play();
	}

	private static float Jitter(int line) => ((line * 7919 + 13) % 3) - 1f;   // -1, 0 or +1 px per line, fixed

	private void OnDraw()
	{
		float w = PageWidth, h = PageHeight;
		// Shadow, then the paper (a warm bone tint over the grey paper texture).
		var shadow = new Vector2[_outline.Length];
		for (int i = 0; i < _outline.Length; i++) shadow[i] = _outline[i] + new Vector2(1.5f, 2f);
		_draw.DrawColoredPolygon(shadow, new Color(0, 0, 0, 0.4f));
		var tint = new Color(1.2f, 1.17f, 1.05f, 0.94f);
		var cols = new Color[_outline.Length];
		for (int i = 0; i < cols.Length; i++) cols[i] = tint;
		_draw.DrawPolygon(_outline, cols, _uvs, _paper);

		// Ruled lines, the red margin, the spiral holes.
		for (float y = 32f; y < h - 10f; y += LineStep)
			_draw.DrawLine(new Vector2(14, y + 0.5f), new Vector2(w - 8, y + 0.5f), Rule, 1f);
		_draw.DrawLine(new Vector2(22.5f, 8), new Vector2(22.5f, h - 6), MarginLine, 1f);
		for (int i = 0; i < 6; i++)
		{
			float y = 22f + i * (h - 44f) / 5f;
			_draw.DrawCircle(new Vector2(7f, y), 2.2f, new Color(0.16f, 0.14f, 0.12f, 0.75f));
		}

		var log = _log;
		var italic = UiKit.SerifItalic;
		float x0 = 27f;

		// Header.
		Heavy(italic, new Vector2(x0, 20f), PhotoLog.Header);

		// Listed lines with their boxes.
		float y0 = 32f + LineStep - 3f;
		float yy = y0;
		int line = 0;
		foreach (var e in PhotoLog.Entries)
		{
			if (!e.Listed) continue;
			bool done = log != null && log.Has(e.Id);
			float jy = yy + Jitter(line);
			var box = new Rect2(x0, jy - 7f, 7f, 7f);
			_draw.DrawRect(box, new Color(Pencil, 0.7f), false, 1f);
			if (done) Tick(box);
			string[] parts = e.Caption.Split('\n');
			_draw.DrawString(italic, new Vector2(x0 + 12f, jy), parts[0], HorizontalAlignment.Left, -1, 10, Graphite);
			for (int i = 1; i < parts.Length; i++)
			{
				yy += LineStep - 2f;
				_draw.DrawString(italic, new Vector2(x0 + 18f, yy + Jitter(line)), parts[i], HorizontalAlignment.Left, -1, 10, Graphite);
			}
			yy += LineStep;
			line++;
		}

		// A pencil rule, then the lines that wrote themselves in (a heavier hand).
		float ry = yy - 8f;
		_draw.DrawLine(new Vector2(x0, ry), new Vector2(w - 12f, ry - 1f), new Color(Pencil, 0.6f), 1f);
		yy += 2f;
		foreach (var e in PhotoLog.Entries)
		{
			if (e.Listed || log == null || !log.Has(e.Id)) continue;
			float jy = yy + Jitter(line);
			Heavy(italic, new Vector2(x0, jy), e.Caption);
			yy += LineStep;
			line++;
		}
		if (log != null && log.ListComplete)
			_draw.DrawString(italic, new Vector2(x0, yy + Jitter(line)), PhotoLog.RewardLine, HorizontalAlignment.Left, -1, 10, Graphite);

		// Footer: frames left.
		int left = _camera != null && IsInstanceValid(_camera) ? _camera.FramesLeft : 0;
		_draw.DrawString(UiKit.Mono, new Vector2(x0, h - 7f), $"{left} exp. left", HorizontalAlignment.Right, w - x0 - 10f, 9, new Color(0.3f, 0.3f, 0.32f, 0.8f));
	}

	private void Tick(Rect2 box)
	{
		var c = new Color(Pencil, 0.95f);
		var a = box.Position + new Vector2(1f, 3.5f);
		var b = box.Position + new Vector2(3f, 6.5f);
		var d = box.Position + new Vector2(8.5f, -1.5f);
		_draw.DrawLine(a, b, c, 1.5f);
		_draw.DrawLine(b, d, c, 1.5f);
	}

	private void Heavy(Font font, Vector2 pos, string text)
	{
		_draw.DrawString(font, pos + new Vector2(0.7f, 0), text, HorizontalAlignment.Left, -1, 10, new Color(Graphite, 0.7f));
		_draw.DrawString(font, pos, text, HorizontalAlignment.Left, -1, 10, Graphite);
	}
}
