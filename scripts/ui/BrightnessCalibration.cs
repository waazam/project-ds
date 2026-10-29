using System;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The brightness calibration (2026-10-29, the friend's playtest: "the brightness slider is bullshit and needs calibrations
/// for HDR and without HDR"). A black screen with three hooded figures drawn just above black, the darkest on the left,
/// each through the same brightness the finish (ps2_post) puts over the game; "adjust until the left one is only just
/// visible". Without HDR that's the brightness (a gamma over the whole picture). With HDR on (offered only where the
/// screen has it) it's the black level (a gamma on the shadows alone), and two more: the paper white (how bright the
/// game's white is, in nits) and the peak (raised until the sun in the bright square stops showing: the display's limit).
/// Shown before the first new game and from the settings ("Brightness..."). Covers whatever it's opened over, and puts it
/// back when closed (Done, Esc or B).
/// </summary>
public partial class BrightnessCalibration : Control
{
	/// <summary>The three figures' levels in the finished picture before the brightness (the darkest shadow detail the game
	/// relies on, then two steps up).</summary>
	public static readonly float[] MarkLevels = { 0.02f, 0.045f, 0.09f };

	private Action _onDone, _onClosed;
	private string _doneText = "Done";
	private Control _host;
	private CanvasLayer _layer;
	private readonly System.Collections.Generic.List<Control> _hidden = new();
	private CalibrationMarks _marks;
	private PeakMark _peak;
	private VBoxContainer _sdrRows, _hdrRows;
	private Label _hint;
	private Control _first;

	/// <summary>Opens the calibration over <paramref name="from"/>'s screen (its topmost control's other children hidden till
	/// it closes). <paramref name="onClosed"/> runs whenever it closes (to put the focus back); <paramref name="onDone"/> only
	/// on Done (after the settings are saved), which <paramref name="doneText"/> names.</summary>
	public static BrightnessCalibration Open(Node from, Action onClosed, Action onDone = null, string doneText = "Done")
	{
		Control host = from as Control;
		for (var n = from; n is Control c; n = n.GetParent()) host = c;
		var cal = new BrightnessCalibration { _onDone = onDone, _onClosed = onClosed, _doneText = doneText, _host = host, Name = "BrightnessCalibration" };
		foreach (var child in host.GetChildren())
			if (child is Control ctl && ctl.Visible) { ctl.Visible = false; cal._hidden.Add(ctl); }
		// (on a layer of its own, over the pause menu's 30: the whole screen whatever the host's size)
		cal._layer = new CanvasLayer { Layer = 40, ProcessMode = ProcessModeEnum.Always };
		host.AddChild(cal._layer);
		cal._layer.AddChild(cal);
		return cal;
	}

	public override void _Ready()
	{
		var s = GameSettings.Instance;
		ProcessMode = ProcessModeEnum.Always;
		MouseFilter = MouseFilterEnum.Stop;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);   // (in the tree already: the offsets too, or it stays 0 x 0)
		UiKit.Apply(this);
		var shade = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
		shade.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(shade);

		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		box.SetAnchorsPreset(LayoutPreset.Center);
		box.OffsetLeft = -160; box.OffsetRight = 160; box.OffsetTop = -172; box.OffsetBottom = 172;
		box.GrowHorizontal = GrowDirection.Both; box.GrowVertical = GrowDirection.Both;
		box.AddThemeConstantOverride("separation", 4);
		AddChild(box);
		box.AddChild(UiKit.MakeLabel("BRIGHTNESS", UiKit.HeadingLabel, HorizontalAlignment.Center));
		box.AddChild(UiKit.Rule(150));
		_marks = new CalibrationMarks();
		box.AddChild(_marks);
		_hint = UiKit.MakeLabel("Adjust until the left figure is only just visible.", null, HorizontalAlignment.Center);
		box.AddChild(_hint);

		bool offerHdr = GameSettings.HdrSupported || ForceHdrLayout;
		CheckBox hdrToggle = null;
		if (offerHdr)
			hdrToggle = UiKit.AddToggle(box, "HDR", s.HdrOutput, on =>
			{
				if (on && !s.HdrOutput) s.HdrFromDisplay();
				s.HdrOutput = on;
				Refresh();
				// (the display takes a moment to switch: the hint looked at again once it has)
				GetTree().CreateTimer(0.8, true).Timeout += () => { if (IsInstanceValid(this)) Refresh(); };
			});

		// without HDR: the brightness
		_sdrRows = new VBoxContainer();
		_sdrRows.AddThemeConstantOverride("separation", 4);
		box.AddChild(_sdrRows);
		var bright = UiKit.AddSlider(_sdrRows, "Brightness", GameSettings.BrightnessMin, GameSettings.BrightnessMax, s.Brightness,
			v => { s.Brightness = (float)v; Refresh(); }, 0.02, Percent);

		// with HDR: the black level, the paper white, the peak
		_hdrRows = new VBoxContainer();
		_hdrRows.AddThemeConstantOverride("separation", 4);
		box.AddChild(_hdrRows);
		var black = UiKit.AddSlider(_hdrRows, "Black level", GameSettings.BlackLevelMin, GameSettings.BlackLevelMax, s.HdrBlackLevel,
			v => { s.HdrBlackLevel = (float)v; Refresh(); }, 0.02, Percent);
		UiKit.AddSlider(_hdrRows, "Paper white", GameSettings.PaperWhiteMin, GameSettings.PaperWhiteMax, s.HdrPaperWhite,
			v => { s.HdrPaperWhite = (float)v; s.ApplyHdr(); Refresh(); }, 10, v => $"{Mathf.RoundToInt((float)v)} nits");
		_hdrRows.AddChild(UiKit.MakeLabel("Paper white: as bright as a sheet of paper in the room, no brighter.", UiKit.DimLabel, HorizontalAlignment.Center));
		UiKit.AddSlider(_hdrRows, "Peak", GameSettings.PeakMin, GameSettings.PeakMax, s.HdrPeak,
			v => { s.HdrPeak = (float)v; s.ApplyHdr(); Refresh(); }, 50, v => $"{Mathf.RoundToInt((float)v)} nits");
		_peak = new PeakMark();
		_hdrRows.AddChild(_peak);

		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		row.AddThemeConstantOverride("separation", 24);
		row.AddChild(UiKit.MakeButton("Default", () =>
		{
			if (s.HdrOutput) { s.HdrBlackLevel = 1f; s.HdrFromDisplay(); s.ApplyHdr(); }
			else s.Brightness = 1f;
			foreach (var c in _hidden) if (IsInstanceValid(c)) c.Visible = true;
			_layer.QueueFree();
			Open(_host, _onClosed, _onDone, _doneText);
		}, true));
		row.AddChild(UiKit.MakeButton(_doneText, () => Close(true), true));
		box.AddChild(row);

		_first = hdrToggle != null ? hdrToggle : (s.HdrOutput ? black : bright);
		if (!offerHdr) _first = bright;
		Refresh();
		_first.CallDeferred(Control.MethodName.GrabFocus);
	}

	/// <summary>Dev (`--calib-hdr`): the HDR rows laid out on a screen without HDR (for the UI preview's shots).</summary>
	public static bool ForceHdrLayout => Array.IndexOf(OS.GetCmdlineUserArgs(), "--calib-hdr") >= 0;

	private static string Percent(double v) => $"{Mathf.RoundToInt((float)v * 100f)}%";

	public void Refresh()
	{
		var s = GameSettings.Instance;
		bool hdr = s.HdrOutput && (GameSettings.HdrSupported || ForceHdrLayout);
		_sdrRows.Visible = !hdr;
		_hdrRows.Visible = hdr;
		_marks.Hdr = hdr;
		// (the screen can, but the system's HDR is off: nothing changes until it's turned on there)
		_hint.Text = s.HdrOutput && !s.HdrActive && !ForceHdrLayout
			? "HDR is off in the system's display settings: turn it on there first."
			: "Adjust until the left figure is only just visible.";
		_marks.QueueRedraw();
		_peak?.QueueRedraw();
	}

	private void Close(bool done)
	{
		var s = GameSettings.Instance;
		if (done) { s.BrightnessCalibrated = true; s.Save(); }
		foreach (var c in _hidden) if (IsInstanceValid(c)) c.Visible = true;
		_layer.QueueFree();
		if (done && _onDone != null) _onDone();
		else _onClosed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e.IsActionPressed("ui_cancel") || e.IsActionPressed("pause"))
		{
			GetViewport().SetInputAsHandled();
			GameSettings.Instance.Save();
			Close(false);
		}
	}

	/// <summary>A level of the finished picture (0..1, sRGB-encoded, before the player's brightness) as it reaches the
	/// screen: through the brightness, or the black level with HDR. The same sums as ps2_post's.</summary>
	public static float Displayed(float v, bool hdr)
	{
		var s = GameSettings.Instance;
		if (!hdr) return Mathf.Pow(v, 1f / Mathf.Max(s.Brightness, 0.1f));
		float lifted = Mathf.Pow(v, 1f / Mathf.Max(s.HdrBlackLevel, 0.1f));
		float t = Mathf.Clamp(v / 0.5f, 0f, 1f);
		return Mathf.Lerp(lifted, v, t * t * (3f - 2f * t));
	}
}

/// <summary>The calibration's three hooded figures on black, at <see cref="BrightnessCalibration.MarkLevels"/> through the
/// picture's brightness (or HDR's black level).</summary>
public partial class CalibrationMarks : Control
{
	public bool Hdr;
	public CalibrationMarks() { CustomMinimumSize = new Vector2(0, 92); MouseFilter = MouseFilterEnum.Ignore; }

	public override void _Draw()
	{
		var levels = BrightnessCalibration.MarkLevels;
		for (int i = 0; i < levels.Length; i++)
		{
			float v = BrightnessCalibration.Displayed(levels[i], Hdr);
			var c = new Color(v, v, v);
			var mid = new Vector2(Size.X * (0.22f + 0.28f * i), 6f);
			DrawCircle(mid + new Vector2(0, 16f), 11f, c);
			DrawColoredPolygon(new[] { mid + new Vector2(-9f, 25f), mid + new Vector2(9f, 25f), mid + new Vector2(25f, 49f), mid + new Vector2(30f, 82f), mid + new Vector2(-30f, 82f), mid + new Vector2(-25f, 49f) }, c);
		}
	}
}

/// <summary>HDR's peak: a square at the chosen peak with a sun inside it at far past any display's limit. While the peak is
/// under what the display can show, the sun shows brighter than the square; raised to the display's limit, it vanishes.
/// (With HDR the 2D runs in linear light, so these colours are given past 1.0, as sRGB, over the paper white.)</summary>
public partial class PeakMark : Control
{
	public PeakMark() { CustomMinimumSize = new Vector2(0, 46); MouseFilter = MouseFilterEnum.Ignore; }

	private static float Encode(float linear) => linear <= 0.0031308f ? linear * 12.92f : 1.055f * Mathf.Pow(linear, 1f / 2.4f) - 0.055f;

	public override void _Draw()
	{
		var s = GameSettings.Instance;
		float square = Encode(s.HdrPeak / Mathf.Max(s.HdrPaperWhite, 1f));
		float sun = Encode(10000f / Mathf.Max(s.HdrPaperWhite, 1f));
		var centre = new Vector2(Size.X * 0.5f, 18f);
		DrawRect(new Rect2(centre - new Vector2(18f, 16f), new Vector2(36f, 32f)), new Color(square, square, square));
		DrawCircle(centre, 6f, new Color(sun, sun, sun));
		for (int i = 0; i < 8; i++)
		{
			float a = i * Mathf.Pi / 4f;
			var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
			DrawLine(centre + d * 8.5f, centre + d * 12.5f, new Color(sun, sun, sun), 1.5f);
		}
		DrawString(ThemeDB.FallbackFont, new Vector2(0, 44f), "Raise the peak until the sun disappears into the square.", HorizontalAlignment.Center, Size.X, 9, new Color(0.6f, 0.6f, 0.62f));
	}
}
