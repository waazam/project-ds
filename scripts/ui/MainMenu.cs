using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// First thing the player sees. New Game / Continue / Settings / Quit.
/// There is no manual save: Continue loads whatever checkpoint the story last
/// reached. An old TV in the dark plays a worn tape of the stairs, the title in the
/// VCR's on-screen text (see <see cref="TitleTv"/>), with plain-text items
/// bottom-left; styled by <see cref="UiKit"/>.
/// </summary>
public partial class MainMenu : Node
{
	private Control _menuBox;
	private Control _settingsBox;
	private Control _settingsShade;
	private Control _firstItem;
	private Control _firstSetting;

	public override void _Ready()
	{
		if (GameSettings.Instance.AutoTest) { StoryManager.Instance.StartNewGame(); return; }
		// Coming back from "Quit to Menu": StoryManager unpauses on scene change; make sure regardless.
		GetTree().Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		Build();
	}

	private void Build()
	{
		var layer = new CanvasLayer();
		AddChild(layer);
		var root = UiKit.Apply(new Control());
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(root);

		// The title screen: an old TV in the dark playing a tape of the stairs (the title is on the tape).
		var tv = new TitleTv();
		root.AddChild(tv);
		root.AddChild(new MenuBackdrop { Source = tv.Texture });
		root.AddChild(MenuBackdrop.MakeVignette(0.55f));


		_menuBox = BuildMenu();
		root.AddChild(_menuBox);

		_settingsShade = new ColorRect { Color = new Color(UiKit.Night, 0.62f), Visible = false };
		_settingsShade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(_settingsShade);
		_settingsBox = BuildSettings();
		_settingsBox.Visible = false;
		root.AddChild(_settingsBox);

		_firstItem.CallDeferred(Control.MethodName.GrabFocus);
	}

	private Control BuildMenu()
	{
		var box = new VBoxContainer();
		box.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		box.OffsetLeft = 44; box.OffsetRight = 250; box.OffsetTop = -126; box.OffsetBottom = -40;
		box.GrowVertical = Control.GrowDirection.Begin;
		box.AddThemeConstantOverride("separation", 2);

		var newGame = UiKit.MakeButton("New Game", () => StoryManager.Instance.StartNewGame());
		box.AddChild(newGame);

		bool hasSave = SaveSystem.HasSave();
		var cont = UiKit.MakeButton("Continue", () => StoryManager.Instance.ContinueGame());
		cont.Disabled = !hasSave;
		box.AddChild(cont);

		box.AddChild(UiKit.MakeButton("Settings", () => ShowSettings(true)));
		box.AddChild(UiKit.MakeButton("Quit", () => GetTree().Quit()));

		_firstItem = hasSave ? cont : newGame;
		return box;
	}

	private Control BuildSettings()
	{
		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		box.SetAnchorsPreset(Control.LayoutPreset.Center);
		box.OffsetLeft = -135; box.OffsetRight = 135; box.OffsetTop = -110; box.OffsetBottom = 110;
		box.GrowHorizontal = Control.GrowDirection.Both; box.GrowVertical = Control.GrowDirection.Both;
		box.AddThemeConstantOverride("separation", 5);

		box.AddChild(UiKit.MakeLabel("SETTINGS", UiKit.HeadingLabel, HorizontalAlignment.Center));
		box.AddChild(UiKit.Rule(150));
		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
		int before = box.GetChildCount();
		UiKit.AddSettingsRows(box);
		_firstSetting = FirstFocusable(box.GetChild(before));

		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
		box.AddChild(UiKit.MakeButton("Back", () =>
		{
			GameSettings.Instance.Save();
			ShowSettings(false);
		}, true));
		return box;
	}

	private void ShowSettings(bool on)
	{
		_settingsBox.Visible = on;
		_settingsShade.Visible = on;
		_menuBox.Visible = !on;
		(on ? _firstSetting : _firstItem)?.CallDeferred(Control.MethodName.GrabFocus);
	}

	public override void _UnhandledInput(InputEvent e)
	{
		// Esc / B backs out of settings.
		if (_settingsBox != null && _settingsBox.Visible && e.IsActionPressed("ui_cancel"))
		{
			GameSettings.Instance.Save();
			ShowSettings(false);
			GetViewport().SetInputAsHandled();
		}
	}

	private static Control FirstFocusable(Node n)
	{
		if (n is Control { FocusMode: Control.FocusModeEnum.All } c) return c;
		foreach (var child in n.GetChildren())
			if (FirstFocusable(child) is { } f) return f;
		return null;
	}
}
