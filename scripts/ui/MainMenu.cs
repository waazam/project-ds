using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// First thing the player sees. New Game / Continue / Settings / Quit.
/// There is no manual save: Continue loads whatever checkpoint the story last
/// reached. Placeholder-grade UI built in code, like PauseMenu.
/// </summary>
public partial class MainMenu : Node
{
	private Control _menuBox;
	private Control _settingsBox;

	public override void _Ready()
	{
		if (GameSettings.Instance.AutoTest) { StoryManager.Instance.StartNewGame(); return; }
		Input.MouseMode = Input.MouseModeEnum.Visible;
		Build();
	}

	private void Build()
	{
		var layer = new CanvasLayer();
		AddChild(layer);

		var bg = new ColorRect { Color = new Color(0.035f, 0.04f, 0.035f) };
		bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(bg);

		_menuBox = BuildMenu();
		layer.AddChild(_menuBox);
		_settingsBox = BuildSettings();
		_settingsBox.Visible = false;
		layer.AddChild(_settingsBox);
	}

	private Control BuildMenu()
	{
		var box = new VBoxContainer();
		box.SetAnchorsPreset(Control.LayoutPreset.Center);
		box.OffsetLeft = -130; box.OffsetRight = 130; box.OffsetTop = -100; box.OffsetBottom = 100;
		box.AddThemeConstantOverride("separation", 8);

		var title = new Label { Text = "PROJECT DS", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 24);
		box.AddChild(title);
		var subtitle = new Label { Text = "working title", HorizontalAlignment = HorizontalAlignment.Center };
		subtitle.AddThemeFontSizeOverride("font_size", 9);
		subtitle.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.5f));
		box.AddChild(subtitle);
		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

		var newGame = UiKit.MakeButton("New Game", () => StoryManager.Instance.StartNewGame());
		box.AddChild(newGame);

		bool hasSave = SaveSystem.HasSave();
		var cont = UiKit.MakeButton("Continue", () => StoryManager.Instance.ContinueGame());
		cont.Disabled = !hasSave;
		box.AddChild(cont);

		box.AddChild(UiKit.MakeButton("Settings", () => { _menuBox.Visible = false; _settingsBox.Visible = true; }));
		box.AddChild(UiKit.MakeButton("Quit", () => GetTree().Quit()));

		(hasSave ? cont : newGame).CallDeferred(Control.MethodName.GrabFocus);
		return box;
	}

	private Control BuildSettings()
	{
		var box = new VBoxContainer();
		box.SetAnchorsPreset(Control.LayoutPreset.Center);
		box.OffsetLeft = -130; box.OffsetRight = 130; box.OffsetTop = -90; box.OffsetBottom = 90;
		box.AddThemeConstantOverride("separation", 6);

		var title = new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 14);
		box.AddChild(title);

		var s = GameSettings.Instance;
		UiKit.AddSlider(box, "Master volume", 0.0, 1.0, s.MasterVolume, v => s.MasterVolume = (float)v);
		UiKit.AddSlider(box, "Mouse sensitivity", 0.0005, 0.008, s.MouseSensitivity, v => s.MouseSensitivity = (float)v);
		UiKit.AddSlider(box, "Stick sensitivity", 0.8, 5.0, s.StickSensitivity, v => s.StickSensitivity = (float)v);

		var invert = new CheckBox { Text = "Invert Y", ButtonPressed = s.InvertY };
		invert.AddThemeFontSizeOverride("font_size", 9);
		invert.Toggled += on => s.InvertY = on;
		box.AddChild(invert);

		box.AddChild(UiKit.MakeButton("Back", () =>
		{
			GameSettings.Instance.Save();
			_settingsBox.Visible = false;
			_menuBox.Visible = true;
		}));
		return box;
	}
}
