using System;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.UI;

/// <summary>
/// The bunker door's four-digit dial, held up to the eye: four numbered wheels on a dark
/// plate. A/D (or the arrows) move between wheels, W/S (or the wheel) turn one, E tries the
/// code, Esc lets go. A wrong code clunks and the wheels shake; the right one closes the
/// overlay and calls back to the door. Nothing pauses; the player's input is made idle
/// through <see cref="PlayerInput.BeginModal"/> while it is up, like a note.
/// </summary>
public partial class CodeLockOverlay : CanvasLayer
{
	public static CodeLockOverlay Instance { get; private set; }

	public bool IsOpen => _root != null && _root.Visible && !_closing;
	/// <summary>For tests: the digits shown right now.</summary>
	public string Entered => new(_digits);

	private const int Wheels = 4;
	private readonly char[] _digits = { '0', '0', '0', '0' };
	private int _cursor;
	private string _code = "";
	private Action _onSuccess;
	private PlayerController _player;
	private Control _root;
	private Control _plate;
	private Label[] _cells;
	private Label _hint;
	private Tween _tween;
	private bool _closing;
	private float _shake;
	private AudioStreamPlayer _clunk;
	private Label _tracker;
	private double _trackerPoll;
	/// <summary>For tests: the corner tracker's text ("STATION 3   4 _ 2 _"), empty while hidden.</summary>
	public string TrackerText => _tracker is { Visible: true } ? _tracker.Text : "";

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 18;
		_root = UiKit.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);
		var wash = new ColorRect { Color = new Color(UiKit.Night, 0.5f), MouseFilter = Control.MouseFilterEnum.Ignore };
		wash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(wash);

		_plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.16f, 0.16f, 0.15f),
			BorderColor = new Color(0.32f, 0.31f, 0.28f),
			BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
			ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 6, ShadowOffset = new Vector2(2, 3),
			ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 10,
		});
		// Held up high (about a third of the way down the screen), clear of the caption band at the
		// centre: Act 7's whispers keep coming while the dial is up and must not print over the wheels.
		_plate.SetAnchorsPreset(Control.LayoutPreset.Center);
		_plate.AnchorTop = 0.3f; _plate.AnchorBottom = 0.3f;
		_plate.GrowHorizontal = Control.GrowDirection.Both; _plate.GrowVertical = Control.GrowDirection.Both;
		_root.AddChild(_plate);

		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.AddThemeConstantOverride("separation", 8);
		_plate.AddChild(box);
		var title = new Label { Text = "STATION 3", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		title.AddThemeFontOverride("font", UiKit.MonoSpaced);
		title.AddThemeFontSizeOverride("font_size", 9);
		title.AddThemeColorOverride("font_color", new Color(0.62f, 0.6f, 0.54f));
		box.AddChild(title);

		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
		row.AddThemeConstantOverride("separation", 8);
		box.AddChild(row);
		_cells = new Label[Wheels];
		for (int i = 0; i < Wheels; i++)
		{
			var cell = new Label
			{
				Text = "0", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
				CustomMinimumSize = new Vector2(30, 40), MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			cell.AddThemeFontOverride("font", UiKit.Mono);
			cell.AddThemeFontSizeOverride("font_size", 24);
			cell.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.08f), BorderColor = new Color(0.3f, 0.3f, 0.28f), BorderWidthBottom = 2 });
			row.AddChild(cell);
			_cells[i] = cell;
		}

		_hint = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, Text = "A D  wheel      W S  turn      E  try      Esc  let go" };
		_hint.AddThemeFontOverride("font", UiKit.Mono);
		_hint.AddThemeFontSizeOverride("font_size", 8);
		_hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.53f, 0.48f));
		box.AddChild(_hint);

		_clunk = new AudioStreamPlayer { Bus = "Events", VolumeDb = -14f };
		AddChild(_clunk);
		// The tracker: the digits found so far, top right, from the first one until the door is open (Dan, 2026-09-22).
		_tracker = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_tracker.AddThemeFontOverride("font", UiKit.MonoSpaced);
		_tracker.AddThemeFontSizeOverride("font_size", 10);
		_tracker.AddThemeColorOverride("font_color", new Color(0.72f, 0.7f, 0.62f));
		_tracker.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
		_tracker.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_tracker.OffsetLeft = -260; _tracker.OffsetRight = -14; _tracker.OffsetTop = 12; _tracker.OffsetBottom = 30;
		AddChild(_tracker);
		Paint();
	}

	/// <summary>Holds the dial up. <paramref name="onSuccess"/> runs (once) when the right code is tried.</summary>
	public void Open(string code, PlayerController player, Action onSuccess)
	{
		if (IsOpen) return;
		_code = code ?? "";
		_onSuccess = onSuccess;
		_player = player;
		// Pre-filled with the digits found on the way; the cursor sits on the first unknown wheel.
		string known = (GetTree().GetFirstNodeInGroup("survey_lot") as SurveyLot)?.Known ?? "";
		_cursor = -1;
		for (int i = 0; i < Wheels; i++)
		{
			char c = i < known.Length ? known[i] : '_';
			_digits[i] = char.IsDigit(c) ? c : '0';
			if (_cursor < 0 && !char.IsDigit(c)) _cursor = i;
		}
		if (_cursor < 0) _cursor = 0;
		_closing = false;
		_player?.PlayerInput.BeginModal();
		_root.Visible = true;
		_root.Modulate = new Color(1, 1, 1, 0);
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(_root, "modulate:a", 1f, 0.15f);
		Paint();
	}

	public void Close()
	{
		if (!IsOpen) return;
		_closing = true;
		_player?.PlayerInput.EndModal();
		_player = null;
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(_root, "modulate:a", 0f, 0.12f);
		_tween.TweenCallback(Callable.From(() => { _root.Visible = false; _closing = false; }));
	}

	/// <summary>For tests: sets the wheels and tries the code, as the player would.</summary>
	public void EnterAndSubmit(string digits)
	{
		if (!IsOpen) return;
		for (int i = 0; i < Wheels; i++) _digits[i] = i < digits.Length && char.IsDigit(digits[i]) ? digits[i] : '0';
		Paint();
		Submit();
	}

	private void Submit()
	{
		Click(1.0f);
		GD.Print($"[story] the dial tried {Entered} against {_code}: {(Entered == _code ? "open" : "no")}");
		if (Entered == _code)
		{
			var cb = _onSuccess;
			_onSuccess = null;
			Close();
			cb?.Invoke();
			return;
		}
		_shake = 1f;
	}

	private void Click(float pitch)
	{
		const string path = "res://assets/audio/sfx/step_stone_01.wav";
		if (!ResourceLoader.Exists(path)) return;
		_clunk.Stream = GD.Load<AudioStream>(path);
		_clunk.PitchScale = pitch;
		_clunk.Play();
	}

	private void Paint()
	{
		for (int i = 0; i < Wheels; i++)
		{
			_cells[i].Text = _digits[i].ToString();
			_cells[i].AddThemeColorOverride("font_color", i == _cursor ? UiKit.Eye : UiKit.Bone);
		}
	}

	public override void _Process(double delta)
	{
		if ((_trackerPoll -= delta) <= 0) { _trackerPoll = 0.4; UpdateTracker(); }
		if (!IsOpen) return;
		if (_player != null && (!IsInstanceValid(_player) || !_player.PlayerInput.Enabled)) { Close(); return; }
		if (_shake > 0f)
		{
			_shake = Mathf.MoveToward(_shake, 0f, (float)delta * 2.5f);
			_plate.Position = _plate.Position with { X = Mathf.Sin(_shake * 40f) * 4f * _shake };
			foreach (var c in _cells) c.AddThemeColorOverride("font_color", new Color(0.85f, 0.3f, 0.25f).Lerp(UiKit.Bone, 1f - _shake));
			if (_shake <= 0f) Paint();
		}
	}

	public override void _Input(InputEvent e)
	{
		if (!IsOpen) return;
		bool handled = true;
		if (e.IsActionPressed("interact")) Submit();
		else if (e.IsActionPressed("pause") || e.IsActionPressed("ui_cancel")) Close();
		else if (e.IsActionPressed("move_left")) { _cursor = (_cursor + Wheels - 1) % Wheels; Click(1.3f); }
		else if (e.IsActionPressed("move_right")) { _cursor = (_cursor + 1) % Wheels; Click(1.3f); }
		else if (e.IsActionPressed("move_forward") || e.IsActionPressed("item_prev")) { Turn(1); }
		else if (e.IsActionPressed("move_back") || e.IsActionPressed("item_next")) { Turn(-1); }
		else handled = false;
		if (!handled) return;
		Paint();
		GetViewport().SetInputAsHandled();
	}

	private void Turn(int by)
	{
		_digits[_cursor] = (char)('0' + ((_digits[_cursor] - '0' + by + 10) % 10));
		Click(1.15f);
	}

	/// <summary>"STATION 3   4 _ 2 _": shown once the first digit is found, until the door is open.</summary>
	private void UpdateTracker()
	{
		if (_tracker == null) return;
		var lot = GetTree().GetFirstNodeInGroup("survey_lot") as SurveyLot;
		var s = StoryManager.Instance;
		// From the moment the camp note is read (it says: a bunker, a steel door, four numbers) until the door is open.
		bool show = lot != null && s != null && (lot.ReadCount > 0 || s.HasFlag(Camp.NoteReadFlag)) && !s.HasFlag(StoryManager.Flag.BunkerUnlocked) && s.Current < Checkpoint.Act8BunkerEntered;
		_tracker.Visible = show;
		if (show) _tracker.Text = "CODE  " + string.Join(" ", lot.Known.ToCharArray());
	}
}
