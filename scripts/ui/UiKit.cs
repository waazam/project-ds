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
	private static Font _serif, _serifTitle, _serifHeading, _mono, _monoSpaced;

	/// <summary>Georgia-style serif for body text, prompts and captions.</summary>
	public static Font Serif => _serif ??= MakeSystemFont(new[] { "Georgia", "Constantia", "Cambria", "Times New Roman", "Liberation Serif", "DejaVu Serif", "serif" }, false);
	/// <summary>The title face: the same serif with wide letter-spacing (website h2: 0.3em).</summary>
	public static Font SerifTitle => _serifTitle ??= Spaced(Serif, 7);
	public static Font SerifHeading => _serifHeading ??= Spaced(Serif, 2);
	/// <summary>Quiet monospace detail (website body font).</summary>
	public static Font Mono => _mono ??= MakeSystemFont(new[] { "Consolas", "Courier New", "DejaVu Sans Mono", "Liberation Mono", "monospace" }, true);
	public static Font MonoSpaced => _monoSpaced ??= Spaced(Mono, 1);

	private static Font MakeSystemFont(string[] names, bool mono)
	{
		return new SystemFont
		{
			FontNames = names,
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
		t.SetIcon("unchecked", "CheckBox", Box(false, Dim));
		t.SetIcon("checked", "CheckBox", Box(true, Eye));
		t.SetIcon("unchecked_disabled", "CheckBox", Box(false, FogSoft));
		t.SetIcon("checked_disabled", "CheckBox", Box(true, FogSoft));

		// Sliders: a 1 px fog line, a bone fill, a small bone tick that turns eye-yellow on focus.
		t.SetStylebox("slider", "HSlider", new StyleBoxLine { Color = new Color(Fog, 0.55f), Thickness = 1, GrowBegin = 0, GrowEnd = 0 });
		t.SetStylebox("grabber_area", "HSlider", new StyleBoxLine { Color = new Color(Bone, 0.55f), Thickness = 1, GrowBegin = 0, GrowEnd = 0 });
		t.SetStylebox("grabber_area_highlight", "HSlider", new StyleBoxLine { Color = new Color(Bone, 0.8f), Thickness = 1, GrowBegin = 0, GrowEnd = 0 });
		t.SetStylebox("focus", "HSlider", empty);
		t.SetIcon("grabber", "HSlider", Tick(new Color(Bone, 0.85f)));
		t.SetIcon("grabber_highlight", "HSlider", Tick(Eye));
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

	private static ImageTexture Box(bool filled, Color c)
	{
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
	}

	private static ImageTexture Tick(Color c)
	{
		var img = Image.CreateEmpty(3, 9, false, Image.Format.Rgba8);
		img.Fill(c);
		return ImageTexture.CreateFromImage(img);
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
	public static HSlider AddSlider(Container parent, string label, double min, double max, double value, Action<double> onChanged)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		var l = MakeLabel(label, DimLabel);
		l.CustomMinimumSize = new Vector2(112, 0);
		row.AddChild(l);
		var slider = new HSlider
		{
			MinValue = min, MaxValue = max, Step = (max - min) / 100.0, Value = value,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(0, 12),
		};
		var readout = MakeLabel("", MonoLabel, HorizontalAlignment.Right);
		readout.AddThemeFontOverride("font", Mono);
		readout.CustomMinimumSize = new Vector2(26, 0);
		void Show(double v) => readout.Text = $"{Mathf.RoundToInt((v - min) / (max - min) * 100.0)}";
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
	}

	/// <summary>A hairline rule in fog grey, like the website's section borders.</summary>
	public static ColorRect Rule(float width) => new()
	{
		Color = new Color(Fog, 0.35f),
		CustomMinimumSize = new Vector2(width, 1),
		SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};
}
