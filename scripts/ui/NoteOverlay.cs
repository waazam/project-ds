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
/// Sized for 640x360: the card is 300 px wide, the text 12 px serif (italic for
/// handwriting, mono for typed), never smaller. Long texts grow the card down
/// to 300 px and then scroll with the mouse wheel or stick.
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

	private Control _root;
	private ColorRect _wash;
	private PanelContainer _card;
	private StyleBoxFlat _cardStyle;
	private Label _title;
	private RichTextLabel _body;
	private Label _hint;
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
		_card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_card.AddThemeStyleboxOverride("panel", _cardStyle);
		_card.SetAnchorsPreset(Control.LayoutPreset.Center);
		_card.GrowHorizontal = Control.GrowDirection.Both;
		_card.GrowVertical = Control.GrowDirection.Both;
		_root.AddChild(_card);

		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
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

		var (paper, ink, font, size) = note.Style switch
		{
			Readable.NoteStyle.Typed => (new Color(0.78f, 0.75f, 0.63f), new Color(0.13f, 0.12f, 0.13f), UiKit.Mono, 11),
			Readable.NoteStyle.Printed => (new Color(0.84f, 0.83f, 0.78f), new Color(0.12f, 0.12f, 0.14f), UiKit.Serif, 12),
			_ => (new Color(0.80f, 0.76f, 0.66f), new Color(0.17f, 0.15f, 0.20f), UiKit.SerifItalic, 12),
		};
		_cardStyle.BgColor = paper;
		_title.Text = note.Title ?? "";
		_title.Visible = !string.IsNullOrEmpty(note.Title);
		_title.AddThemeColorOverride("font_color", new Color(ink, 0.65f));
		_body.AddThemeFontOverride("normal_font", font);
		_body.AddThemeFontSizeOverride("normal_font_size", size);
		_body.AddThemeColorOverride("default_color", ink);
		_body.Text = note.Text ?? "";
		_body.ScrollToLine(0);

		// A handwritten sheet is held a little askew; cards and logs sit square.
		_card.RotationDegrees = note.Style == Readable.NoteStyle.Handwritten ? -1.2f : 0f;
		var vis = GetViewport().GetVisibleRect().Size;
		_width = Mathf.Min(note.Style == Readable.NoteStyle.Typed ? TypedWidth : CardWidth, vis.X - 32f);
		_body.CustomMinimumSize = new Vector2(_width - Pad * 2, 40f);
		Place(_width, 120f);
		_layoutTries = 0;
		Callable.From(FitCard).CallDeferred();

		_player?.PlayerInput.BeginModal();
		_root.Visible = true;
		_root.Modulate = new Color(1, 1, 1, 0);
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(_root, "modulate:a", 1f, 0.15f);
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
		float chrome = (Pad - 4f) * 2f + titleH + hintH;
		float maxBody = Mathf.Max(60f, vis.Y - ScreenMargin * 2f - chrome);
		float bodyH = Mathf.Min(content + 4f, maxBody);
		bool scrolls = content + 4f > maxBody;
		_hint.Text = scrolls ? "wheel  more      E  put it down" : "E   put it down";
		_body.CustomMinimumSize = new Vector2(_width - Pad * 2f, bodyH);
		Place(_width, chrome + bodyH);
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
