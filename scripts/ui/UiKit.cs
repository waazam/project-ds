using System;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The shared look of every menu and HUD element, taken from the game's website:
/// near-black fog backgrounds, bone text, fog-grey secondary text, a serif title
/// with wide letter-spacing, quiet monospace detail, and the faint yellow of
/// something watching for focus and hover.
///
/// Everything is built in code (fonts are <see cref="SystemFont"/>s with
/// fallbacks, so nothing is bundled) and cached. Apply <see cref="Theme"/> to the
/// root Control of each CanvasLayer with <see cref="Apply"/>.
/// </summary>
public static class UiKit
{
	// Palette (website :root).
	public static readonly Color Night = Color.FromHtml("#0b0d10");
	public static readonly Color Deep = Color.FromHtml("#111419");
	public static readonly Color Fog = Color.FromHtml("#5b6170");
	public static readonly Color FogSoft = Color.FromHtml("#2c313b");
	public static readonly Color Bone = Color.FromHtml("#cfcbbf");
	public static readonly Color Muted = Color.FromHtml("#8b8e95");
	public static readonly Color Dim = Color.FromHtml("#5f636b");
	public static readonly Color Eye = Color.FromHtml("#e6c25a");
	public static readonly Color Shadow = new(0f, 0f, 0f, 0.75f);

	// Sizes at the 640x360 internal resolution.
	public const int BodySize = 11;
	public const int CaptionSize = 12;
	public const int SmallSize = 9;
	public const int TitleSize = 26;

	// Theme type variations.
	public const string TitleLabel = "TitleLabel";
	public const string HeadingLabel = "HeadingLabel";
	public const string MonoLabel = "MonoLabel";
	public const string DimLabel = "DimLabel";
	public const string CaptionLabel = "CaptionLabel";

	private static Theme _theme;
	private static Font _serif, _serifItalic, _serifTitle, _serifHeading, _mono, _monoSpaced;

	private static readonly string[] SerifNames = { "Georgia", "Constantia", "Cambria", "Times New Roman", "Liberation Serif", "DejaVu Serif", "serif" };

	/// <summary>Georgia-style serif for body text, prompts and captions.</summary>
	public static Font Serif => _serif ??= MakeSystemFont(SerifNames, false);
	/// <summary>The same serif in italic: handwritten notes and pages.</summary>
	public static Font SerifItalic => _serifItalic ??= MakeSystemFont(SerifNames, false, italic: true);
	/// <summary>The title face: the same serif with wide letter-spacing (website h2: 0.3em).</summary>
	public static Font SerifTitle => _serifTitle ??= Spaced(Serif, 7);
	public static Font SerifHeading => _serifHeading ??= Spaced(Serif, 2);
	/// <summary>Quiet monospace detail (website body font).</summary>
	public static Font Mono => _mono ??= MakeSystemFont(new[] { "Consolas", "Courier New", "DejaVu Sans Mono", "Liberation Mono", "monospace" }, true);
	public static Font MonoSpaced => _monoSpaced ??= Spaced(Mono, 1);

	// (2026-10-24, the owner: the notes' font "to look more like handwriting") the notes' hands, shipped with the game
	// (assets/fonts, OFL / Apache 2.0: their licences beside them)
	private static Font _hand, _scrawl, _caps, _typewriter;
	/// <summary>A clear, quick hand: most handwritten notes (Patrick Hand).</summary>
	public static Font Hand => _hand ??= LoadFont("PatrickHand-Regular.ttf");
	/// <summary>A hurried, slanting scrawl: smudged and frantic notes (Reenie Beanie; needs a size a third bigger).</summary>
	public static Font Scrawl => _scrawl ??= LoadFont("ReenieBeanie.ttf");
	/// <summary>Shaky block capitals: a note written all in caps (Rock Salt; set it small).</summary>
	public static Font Caps => _caps ??= LoadFont("RockSalt-Regular.ttf");
	/// <summary>A worn typewriter, the ribbon uneven: typed logs and reports (Special Elite).</summary>
	public static Font Typewriter => _typewriter ??= LoadFont("SpecialElite-Regular.ttf");

	private static Font LoadFont(string file)
	{
		var f = GD.Load<FontFile>("res://assets/fonts/" + file);
		if (f == null) return Serif;
		f.Antialiasing = TextServer.FontAntialiasing.Gray;
		f.Hinting = TextServer.Hinting.Light;
		f.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
		f.GenerateMipmaps = false;
		return f;
	}

	private static Font MakeSystemFont(string[] names, bool mono, bool italic = false)
	{
		return new SystemFont
		{
			FontNames = names,
			FontItalic = italic,
			Antialiasing = TextServer.FontAntialiasing.Gray,
			Hinting = mono ? TextServer.Hinting.Normal : TextServer.Hinting.Light,
			SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
			GenerateMipmaps = false,
			AllowSystemFallback = true,
		};
	}

	private static Font Spaced(Font baseFont, int glyphSpacing) => new FontVariation { BaseFont = baseFont, SpacingGlyph = glyphSpacing };

	public static Theme Theme => _theme ??= BuildTheme();

	/// <summary>Give a CanvasLayer's root Control the shared theme.</summary>
	public static T Apply<T>(T root) where T : Control { root.Theme = Theme; return root; }

	private static Theme BuildTheme()
	{
		var t = new Theme { DefaultFont = Serif, DefaultFontSize = BodySize };
		var empty = new StyleBoxEmpty();

		// Labels: bone with a soft dark shadow so text reads over fog and dark trunks alike.
		t.SetColor("font_color", "Label", Bone);
		t.SetColor("font_shadow_color", "Label", Shadow);
		t.SetConstant("shadow_offset_x", "Label", 1);
		t.SetConstant("shadow_offset_y", "Label", 1);
		t.SetConstant("shadow_outline_size", "Label", 1);
		t.SetConstant("line_spacing", "Label", 1);

		Variation(t, TitleLabel, "Label", SerifTitle, TitleSize, Bone);
		Variation(t, HeadingLabel, "Label", SerifHeading, 13, Bone);
		Variation(t, MonoLabel, "Label", MonoSpaced, SmallSize, Muted);
		Variation(t, DimLabel, "Label", Serif, 10, Muted);
		Variation(t, CaptionLabel, "Label", Serif, CaptionSize, Bone);
		t.SetConstant("outline_size", CaptionLabel, 3);
		t.SetColor("font_outline_color", CaptionLabel, new Color(0, 0, 0, 0.28f));

		// Buttons: plain text. Focus/hover is bone text plus an eye-yellow glyph drawn by MenuItem, never a box.
		foreach (var s in new[] { "normal", "hover", "pressed", "focus", "disabled", "hover_pressed", "normal_mirrored", "hover_mirrored", "pressed_mirrored", "disabled_mirrored", "hover_pressed_mirrored" })
		{
			t.SetStylebox(s, "Button", empty);
			t.SetStylebox(s, "CheckBox", empty);
		}
		t.SetFont("font", "Button", SerifHeading);
		t.SetFontSize("font_size", "Button", 12);
		t.SetColor("font_color", "Button", Muted);
		t.SetColor("font_hover_color", "Button", Bone);
		t.SetColor("font_focus_color", "Button", Bone);
		t.SetColor("font_pressed_color", "Button", Eye);
		t.SetColor("font_hover_pressed_color", "Button", Eye);
		t.SetColor("font_disabled_color", "Button", new Color(Dim, 0.55f));
		t.SetColor("font_outline_color", "Button", new Color(0, 0, 0, 0.5f));
		t.SetConstant("outline_size", "Button", 2);

		t.SetFont("font", "CheckBox", Serif);
		t.SetFontSize("font_size", "CheckBox", BodySize);
		t.SetColor("font_color", "CheckBox", Muted);
		t.SetColor("font_hover_color", "CheckBox", Bone);
		t.SetColor("font_focus_color", "CheckBox", Bone);
		t.SetColor("font_pressed_color", "CheckBox", Bone);
		t.SetColor("font_hover_pressed_color", "CheckBox", Bone);
		t.SetConstant("h_separation", "CheckBox", 6);
		// (2026-10-25, the HUD's reskin) a little brass-framed leather well, ticked with a red ink cross (tools/Textures/
		// make_hud.py); the drawn boxes when the paintings aren't there
		t.SetIcon("unchecked", "CheckBox", HudIcon("check_off", 10, 10) ?? Box(false, Dim));
		t.SetIcon("checked", "CheckBox", HudIcon("check_on", 10, 10) ?? Box(true, Eye));
		t.SetIcon("unchecked_disabled", "CheckBox", HudIcon("check_off_dim", 10, 10) ?? Box(false, FogSoft));
		t.SetIcon("checked_disabled", "CheckBox", HudIcon("check_on_dim", 10, 10) ?? Box(true, FogSoft));

		// Sliders: a 1 px fog line, a bone fill, a small bone tick that turns eye-yellow on focus.
		// (2026-10-25, the HUD's reskin) a groove in dark old brass, filled with worn gold up to a brass slider-plate
		t.SetStylebox("slider", "HSlider", new StyleBoxFlat
		{
			BgColor = new Color(0.035f, 0.03f, 0.025f, 0.9f), BorderColor = new Color(0.42f, 0.33f, 0.17f, 0.9f),
			BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
			CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
			ContentMarginTop = 2, ContentMarginBottom = 2,
		});
		t.SetStylebox("grabber_area", "HSlider", new StyleBoxFlat
		{
			BgColor = new Color(0.62f, 0.5f, 0.27f, 0.75f), CornerRadiusTopLeft = 2, CornerRadiusBottomLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomRight = 2,
			ContentMarginTop = 2, ContentMarginBottom = 2,
		});
		t.SetStylebox("grabber_area_highlight", "HSlider", new StyleBoxFlat
		{
			BgColor = new Color(0.78f, 0.63f, 0.33f, 0.9f), CornerRadiusTopLeft = 2, CornerRadiusBottomLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomRight = 2,
			ContentMarginTop = 2, ContentMarginBottom = 2,
		});
		t.SetStylebox("focus", "HSlider", empty);
		// the settings' scrollbar: a brass handle in a dark groove, like the sliders
		StyleBoxFlat Bar(Color c) => new() { BgColor = c, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2, ContentMarginLeft = 2, ContentMarginRight = 2 };
		t.SetStylebox("scroll", "VScrollBar", Bar(new Color(0.035f, 0.03f, 0.025f, 0.6f)));
		t.SetStylebox("grabber", "VScrollBar", Bar(new Color(0.55f, 0.43f, 0.22f, 0.8f)));
		t.SetStylebox("grabber_highlight", "VScrollBar", Bar(new Color(0.72f, 0.58f, 0.3f, 0.9f)));
		t.SetStylebox("grabber_pressed", "VScrollBar", Bar(new Color(0.8f, 0.65f, 0.34f, 1f)));
		t.SetIcon("grabber", "HSlider", HudIcon("slider_knob", 6, 11) ?? Tick(new Color(Bone, 0.85f)));
		t.SetIcon("grabber_highlight", "HSlider", HudIcon("slider_knob_hi", 6, 11) ?? Tick(Eye));
		t.SetIcon("grabber_disabled", "HSlider", Tick(Dim));

		t.SetStylebox("panel", "Panel", new StyleBoxFlat { BgColor = new Color(Night, 0.8f) });
		t.SetStylebox("panel", "PanelContainer", empty);
		return t;
	}

	private static void Variation(Theme t, string name, string baseType, Font font, int size, Color color)
	{
		t.SetTypeVariation(name, baseType);
		t.SetFont("font", name, font);
		t.SetFontSize("font_size", name, size);
		t.SetColor("font_color", name, color);
	}

	/// <summary>(2026-10-25) One of the HUD's paintings (assets/ui/hud/<paramref name="name"/>.png, painted at 4x), shown at
	/// <paramref name="w"/> x <paramref name="h"/> (an icon is laid out at its texture's size: the override keeps its
	/// detail at the window's resolution). Null if it isn't there.</summary>
	public static Texture2D HudIcon(string name, int w, int h)
	{
		string path = $"res://assets/ui/hud/{name}.png";
		if (!ResourceLoader.Exists(path) || GD.Load<Texture2D>(path)?.GetImage() is not { } img) return null;
		var tex = ImageTexture.CreateFromImage(img);
		tex.SetSizeOverride(new Vector2I(w, h));
		return tex;
	}

	private static ImageTexture Box(bool filled, Color c)
	{
		var __tg = Systems.TexGen.Start();
		var img = Image.CreateEmpty(9, 9, false, Image.Format.Rgba8);
		img.Fill(Colors.Transparent);
		var edge = filled ? new Color(Bone, 0.7f) : c;
		for (int i = 0; i < 9; i++)
		{
			img.SetPixel(i, 0, edge); img.SetPixel(i, 8, edge);
			img.SetPixel(0, i, edge); img.SetPixel(8, i, edge);
		}
		if (filled) img.FillRect(new Rect2I(3, 3, 3, 3), c);
		return ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
	}

	private static ImageTexture Tick(Color c)
	{
		var __tg = Systems.TexGen.Start();
		var img = Image.CreateEmpty(3, 9, false, Image.Format.Rgba8);
		img.Fill(c);
		return ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
	}

	// ---- Builders shared by the main menu and the pause menu ----

	public static MenuItem MakeButton(string text, Action onPressed, bool centered = false)
	{
		var b = new MenuItem { Text = text, Centered = centered };
		b.Pressed += onPressed;
		return b;
	}

	public static Label MakeLabel(string text, string variation = null, HorizontalAlignment align = HorizontalAlignment.Left)
	{
		var l = new Label { Text = text, HorizontalAlignment = align };
		if (variation != null) l.ThemeTypeVariation = variation;
		return l;
	}

	/// <summary>One settings row: fog-grey name, a thin slider, and its value in quiet monospace.</summary>
	public static HSlider AddSlider(Container parent, string label, double min, double max, double value, Action<double> onChanged, double step = 0, Func<double, string> format = null)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		var l = MakeLabel(label, DimLabel);
		l.CustomMinimumSize = new Vector2(112, 0);
		row.AddChild(l);
		var slider = new HSlider
		{
			MinValue = min, MaxValue = max, Step = step > 0 ? step : (max - min) / 100.0, Value = value,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(0, 12),
		};
		var readout = MakeLabel("", MonoLabel, HorizontalAlignment.Right);
		readout.AddThemeFontOverride("font", Mono);
		readout.CustomMinimumSize = new Vector2(format != null ? 52 : 26, 0);
		void Show(double v) => readout.Text = format != null ? format(v) : $"{Mathf.RoundToInt((v - min) / (max - min) * 100.0)}";
		Show(value);
		slider.ValueChanged += v => { Show(v); onChanged(v); };
		slider.MouseEntered += () => slider.GrabFocus();
		slider.FocusEntered += () => l.AddThemeColorOverride("font_color", Bone);
		slider.FocusExited += () => l.RemoveThemeColorOverride("font_color");
		row.AddChild(slider);
		row.AddChild(readout);
		parent.AddChild(row);
		return slider;
	}

	/// <summary>A settings row with the same columns as a slider row: name, then a small box that fills eye-yellow when on.</summary>
	public static CheckBox AddToggle(Container parent, string label, bool value, Action<bool> onToggled)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		var l = MakeLabel(label, DimLabel);
		l.CustomMinimumSize = new Vector2(112, 0);
		row.AddChild(l);
		var c = new CheckBox { ButtonPressed = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 14) };
		var readout = MakeLabel(value ? "on" : "off", MonoLabel, HorizontalAlignment.Right);
		readout.AddThemeFontOverride("font", Mono);
		readout.CustomMinimumSize = new Vector2(26, 0);
		c.Toggled += on => { readout.Text = on ? "on" : "off"; onToggled(on); };
		c.MouseEntered += () => c.GrabFocus();
		// Focus shows on the name, since the box has no text of its own.
		c.FocusEntered += () => l.AddThemeColorOverride("font_color", Bone);
		c.FocusExited += () => l.RemoveThemeColorOverride("font_color");
		row.AddChild(c);
		row.AddChild(readout);
		parent.AddChild(row);
		return c;
	}

	/// <summary>The settings in a scrolling panel <paramref name="height"/> tall (they outgrew the screen: 2026-10-11),
	/// the wheel and the pad's focus scrolling it. Returns the rows' box.</summary>
	public static VBoxContainer AddSettingsScroll(Container parent, float height)
	{
		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(270, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
		var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		rows.AddThemeConstantOverride("separation", 5);
		scroll.AddChild(rows);
		parent.AddChild(scroll);
		AddSettingsRows(rows);
		return rows;
	}

	/// <summary>The settings shared by the main menu and the pause menu. Camera distance only in third person.</summary>
	public static void AddSettingsRows(Container box)
	{
		var s = GameSettings.Instance;
		AddSlider(box, "Master volume", 0.0, 1.0, s.MasterVolume, v => s.MasterVolume = (float)v);
		AddSlider(box, "Mouse sensitivity", 0.0005, 0.008, s.MouseSensitivity, v => s.MouseSensitivity = (float)v);
		AddSlider(box, "Stick sensitivity", 0.8, 5.0, s.StickSensitivity, v => s.StickSensitivity = (float)v);
		if (s.Camera == CameraMode.ThirdPerson)
			AddSlider(box, "Camera distance", 1.6, 5.5, s.CameraDistance, v => s.CameraDistance = (float)v);
		AddToggle(box, "Invert Y", s.InvertY, on => s.InvertY = on);
		AddToggle(box, "Reduce flashing", s.ReduceFlashing, on => s.ReduceFlashing = on);
		AddSlider(box, "Screen shake", 0.0, 1.0, s.ScreenShake, v => s.ScreenShake = (float)v, 0.05, v => $"{Mathf.RoundToInt((float)v * 100f)}%");
		AddToggle(box, "Sound captions", s.SoundCaptions, on => s.SoundCaptions = on);
		AddSlider(box, "Hint text", 0, HintStyle.SizeNames.Length - 1, s.HintSize, v => s.HintSize = Mathf.RoundToInt(v), 1,
			v => HintStyle.SizeNames[Mathf.Clamp(Mathf.RoundToInt(v), 0, HintStyle.SizeNames.Length - 1)]);
		AddToggle(box, "Hold to crouch", s.HoldToCrouch, on => s.HoldToCrouch = on);
		AddToggle(box, "Body in view", s.BodyInView, on => s.BodyInView = on);
		AddToggle(box, "Head motion", s.HeadMotion, on => s.HeadMotion = on);
		// (2026-10-29, the friend's playtest: "the brightness slider is bullshit and needs calibrations for HDR and without
		// HDR") the brightness set on its own calibration screen, the HDR's paper white and peak there too
		var calibrate = new MenuItem { Text = "Brightness..." };
		calibrate.Pressed += () => BrightnessCalibration.Open(calibrate, () => calibrate.CallDeferred(Control.MethodName.GrabFocus));
		calibrate.MouseEntered += () => calibrate.GrabFocus();
		box.AddChild(calibrate);
		AddSlider(box, "Lantern", GameSettings.LanternMin, GameSettings.LanternMax, s.LanternBrightness, v => s.LanternBrightness = (float)v, 0.05,
			v => $"{Mathf.RoundToInt((float)v * 100f)}%");
		AddSlider(box, "HUD glow", 0, NeonKit.Names.Length - 1, s.HudColor, v => s.HudColor = Mathf.RoundToInt(v), 1,
			v => NeonKit.Names[Mathf.Clamp(Mathf.RoundToInt(v), 0, NeonKit.Names.Length - 1)]);
		AddToggle(box, "HUD only when needed", s.ContextualHud, on => s.ContextualHud = on);
		// (2026-10-29, the friend's playtest: the depth of field "looks horrible") off to subtle, low by default
		AddSlider(box, "Depth of field", 0, GameSettings.DepthOfFieldNames.Length - 1, s.DepthOfField, v => s.DepthOfField = Mathf.RoundToInt(v), 1,
			v => GameSettings.DepthOfFieldNames[Mathf.Clamp(Mathf.RoundToInt(v), 0, GameSettings.DepthOfFieldNames.Length - 1)]);
		AddToggle(box, "Cinematic bars", s.CinemaBars, on => s.CinemaBars = on);
		// the window: fullscreen at the screen's own resolution, or a window of a chosen size
		AddToggle(box, "Windowed", s.Windowed, on => s.Windowed = on);
		var res = GameSettings.FittingResolutions();
		int cur = System.Array.IndexOf(res, s.WindowSize);
		if (cur < 0) cur = res.Length - 1;
		AddSlider(box, "Window size", 0, res.Length - 1, cur, v => s.WindowSize = res[Mathf.Clamp(Mathf.RoundToInt(v), 0, res.Length - 1)], 1,
			v => { var r = res[Mathf.Clamp(Mathf.RoundToInt(v), 0, res.Length - 1)]; return $"{r.X}x{r.Y}"; });
		AddToggle(box, "CRT filter", s.CrtFilter, on => s.CrtFilter = on);
		AddToggle(box, "Light in the fog", s.FogLighting, on => s.FogLighting = on);
		AddToggle(box, "Fine fog", s.FineFog, on => s.FineFog = on);   // (off: a coarser fog, for a slower graphics card)
		AddSlider(box, "Shadows", 0, 2, s.Shadows, v => s.Shadows = Mathf.RoundToInt(v), 1, v => GameSettings.ShadowNames[Mathf.Clamp(Mathf.RoundToInt(v), 0, 2)]);
		// the keys (2026-10-11): each action's key, pressed to change it
		box.AddChild(MakeLabel("KEYS", HeadingLabel, HorizontalAlignment.Center));
		foreach (var (action, label) in KeyRebinds.Actions)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 10);
			var l = MakeLabel(label, DimLabel);
			l.CustomMinimumSize = new Vector2(112, 0);
			row.AddChild(l);
			var b = new RebindButton { Action = action, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 14) };
			b.AddThemeFontOverride("font", Mono);
			b.MouseEntered += () => b.GrabFocus();
			row.AddChild(b);
			box.AddChild(row);
		}
	}

	/// <summary>A hairline rule in fog grey, like the website's section borders.</summary>
	/// <summary>A row of five little stars, <paramref name="lit"/> of them filled (a print's rating), from <paramref name="at"/> (left, middle).</summary>
	public static void DrawStars(CanvasItem c, Vector2 at, int lit, float size, Color on, Color off)
	{
		for (int i = 0; i < 5; i++)
		{
			var centre = at + new Vector2(size * 0.5f + i * size * 1.15f, 0);
			var pts = new Vector2[10];
			for (int k = 0; k < 10; k++)
			{
				float ang = -Mathf.Pi * 0.5f + k * Mathf.Pi / 5f;
				float r = (k % 2 == 0 ? 0.5f : 0.22f) * size;
				pts[k] = centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
			}
			c.DrawColoredPolygon(pts, i < lit ? on : off);
		}
	}

	public static ColorRect Rule(float width) => new()
	{
		Color = new Color(Fog, 0.35f),
		CustomMinimumSize = new Vector2(width, 1),
		SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};
}
