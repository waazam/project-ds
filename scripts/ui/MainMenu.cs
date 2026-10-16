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
	private Control _calibBox;
	private Control _calibFirst;

	public override void _Ready()
	{
		if (GameSettings.Instance.AutoTest) { StoryManager.Instance.StartNewGame(); return; }
		if (GameSettings.Instance.Trailer)
		{
			// the teaser: the Hollow as it is at the cabin (early: summer woods, the cabin boarded), in the test slots
			if (GameSettings.Instance.TrailerEnding)
			{
				// the ending: the trailhead, at the start of Act 1 (the first staircase is at the trail's end)
				SaveSystem.Save(new SaveData { Checkpoint = Checkpoint.Act1Start, Flags = System.Array.Empty<string>(), Inventory = ";tool=None" });
				Callable.From(() => StoryManager.Instance.ContinueGame()).CallDeferred();
				return;
			}
			SaveSystem.Save(new SaveData
			{
				Checkpoint = Checkpoint.Act3DoorBoarded,
				Flags = new[] { StoryManager.Flag.StairsClimbed, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, "read_camp_note" },
				Inventory = "lantern,compass;tool=None",
			});
			Callable.From(() => StoryManager.Instance.ContinueGame()).CallDeferred();
			return;
		}
		if (GameSettings.Instance.StartAct == 2)
		{
			// Dev launch straight into the Hollow wake (Dan, 2026-09-22): a fresh save at checkpoint 2, then Continue.
			SaveSystem.Save(new SaveData { Checkpoint = Checkpoint.Act2StairsClimbed, Flags = new[] { StoryManager.Flag.StairsClimbed }, Inventory = ";tool=None" });
			Callable.From(() => StoryManager.Instance.ContinueGame()).CallDeferred();
			return;
		}
		if (GameSettings.Instance.StartAct == 8)
		{
			// Dev launch at the bunker's door (Dan, 2026-09-22): everything up to the lookout done, the door unlocked, in front of the hatch.
			string[] flags =
			{
				StoryManager.Flag.StairsClimbed, StoryManager.Flag.StormStarted, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, "read_camp_note",
				StoryManager.Flag.CabinDoorOpen, StoryManager.Flag.PickupTakenAxe, StoryManager.Flag.NewelPostTaken, StoryManager.Flag.DawnBroke,
				StoryManager.Flag.KeyPiece(1), StoryManager.Flag.KeyPiece(2), StoryManager.Flag.KeyAssembled,
				StoryManager.Flag.ClearingVoiceHeard, StoryManager.Flag.ClearingLoopDone, StoryManager.Flag.Act6NightFell, StoryManager.Flag.GiantEventDone,
				StoryManager.Flag.BunkerUnlocked, "dev_spawn_bunker",
			};
			SaveSystem.Save(new SaveData { Checkpoint = Checkpoint.Act7CabinBurning, Flags = flags, Inventory = "lantern,compass,newel_post;tool=None" });
			Callable.From(() => StoryManager.Instance.ContinueGame()).CallDeferred();
			return;
		}
		if (GameSettings.Instance.StartAct == 12)
		{
			// Dev launch at the lake (Act 12): everything up to and including the giant done; Continue's
			// respawn for this checkpoint is the lake's own wake marker (see Lake.cs).
			string[] flags =
			{
				StoryManager.Flag.StairsClimbed, StoryManager.Flag.StormStarted, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, "read_camp_note",
				StoryManager.Flag.CabinDoorOpen, StoryManager.Flag.PickupTakenAxe, StoryManager.Flag.NewelPostTaken, StoryManager.Flag.DawnBroke,
				StoryManager.Flag.KeyPiece(1), StoryManager.Flag.KeyPiece(2), StoryManager.Flag.KeyAssembled,
				StoryManager.Flag.ClearingVoiceHeard, StoryManager.Flag.ClearingLoopDone, StoryManager.Flag.Act6NightFell, StoryManager.Flag.GiantEventDone,
				StoryManager.Flag.BunkerUnlocked, StoryManager.Flag.CrtPuzzleDone, StoryManager.Flag.WalkieTaken, StoryManager.Flag.BunkerMazeEntered,
				StoryManager.Flag.BunkerMazeExited, StoryManager.Flag.Act11DialogueDone, StoryManager.Flag.NewelSeated,
			};
			SaveSystem.Save(new SaveData { Checkpoint = Checkpoint.Act11GiantEncounter, Flags = flags, Inventory = "lantern,compass,radio;tool=None" });
			Callable.From(() => StoryManager.Instance.ContinueGame()).CallDeferred();
			return;
		}
		if (GameSettings.Instance.StartAct == 13)
		{
			// Dev launch inside the forester station (Act 13): everything through the lake crossing
			// done; Continue's respawn for this checkpoint is the station's own entrance marker.
			string[] flags =
			{
				StoryManager.Flag.StairsClimbed, StoryManager.Flag.StormStarted, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, "read_camp_note",
				StoryManager.Flag.CabinDoorOpen, StoryManager.Flag.PickupTakenAxe, StoryManager.Flag.NewelPostTaken, StoryManager.Flag.DawnBroke,
				StoryManager.Flag.KeyPiece(1), StoryManager.Flag.KeyPiece(2), StoryManager.Flag.KeyAssembled,
				StoryManager.Flag.ClearingVoiceHeard, StoryManager.Flag.ClearingLoopDone, StoryManager.Flag.Act6NightFell, StoryManager.Flag.GiantEventDone,
				StoryManager.Flag.BunkerUnlocked, StoryManager.Flag.CrtPuzzleDone, StoryManager.Flag.WalkieTaken, StoryManager.Flag.BunkerMazeEntered,
				StoryManager.Flag.BunkerMazeExited, StoryManager.Flag.Act11DialogueDone, StoryManager.Flag.NewelSeated,
			};
			SaveSystem.Save(new SaveData { Checkpoint = Checkpoint.Act12LakeCrossed, Flags = flags, Inventory = "lantern,compass,radio;tool=None" });
			Callable.From(() => StoryManager.Instance.ContinueGame()).CallDeferred();
			return;
		}
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

		_calibBox = BuildCalibration();
		_calibBox.Visible = false;
		root.AddChild(_calibBox);

		_firstItem.CallDeferred(Control.MethodName.GrabFocus);
	}

	/// <summary>The brightness, set before the first game: a black screen, a mark drawn only just above black, and the
	/// slider; "Begin" saves it and starts. (The game is dark by design; on a dark monitor it's unplayably so.)</summary>
	private Control BuildCalibration()
	{
		var s = GameSettings.Instance;
		var shade = new ColorRect { Color = Colors.Black };
		shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		box.SetAnchorsPreset(Control.LayoutPreset.Center);
		box.OffsetLeft = -170; box.OffsetRight = 170; box.OffsetTop = -150; box.OffsetBottom = 150;
		box.GrowHorizontal = Control.GrowDirection.Both; box.GrowVertical = Control.GrowDirection.Both;
		box.AddThemeConstantOverride("separation", 8);
		shade.AddChild(box);
		box.AddChild(UiKit.MakeLabel("BRIGHTNESS", UiKit.HeadingLabel, HorizontalAlignment.Center));
		box.AddChild(UiKit.Rule(150));
		var mark = new CalibrationMark { Gamma = s.Brightness };
		box.AddChild(mark);
		box.AddChild(UiKit.MakeLabel("Raise it until the mark is only just visible.", null, HorizontalAlignment.Center));
		var strip = new BrightnessStrip { Gamma = s.Brightness };
		var slider = UiKit.AddSlider(box, "Brightness", GameSettings.BrightnessMin, GameSettings.BrightnessMax, s.Brightness, v =>
		{
			s.Brightness = (float)v; strip.Gamma = mark.Gamma = (float)v; strip.QueueRedraw(); mark.QueueRedraw();
		}, 0.05, v => $"{Mathf.RoundToInt((float)v * 100f)}%");
		_calibFirst = slider;
		box.AddChild(strip);
		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
		box.AddChild(UiKit.MakeButton("Begin", () =>
		{
			s.BrightnessCalibrated = true;
			s.Save();
			StoryManager.Instance.StartNewGame();
		}, true));
		return shade;
	}

	private void ShowCalibration()
	{
		_calibBox.Visible = true;
		_menuBox.Visible = false;
		_calibFirst?.CallDeferred(Control.MethodName.GrabFocus);
	}

	private Control BuildMenu()
	{
		var box = new VBoxContainer();
		box.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		box.OffsetLeft = 44; box.OffsetRight = 250; box.OffsetTop = -126; box.OffsetBottom = -40;
		box.GrowVertical = Control.GrowDirection.Begin;
		box.AddThemeConstantOverride("separation", 2);

		// (2026-10-12, the owner's suggestion B) the first new game: the brightness set on its own screen first
		var newGame = UiKit.MakeButton("New Game", () =>
		{
			if (GameSettings.Instance.BrightnessCalibrated) StoryManager.Instance.StartNewGame();
			else ShowCalibration();
		});
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
		var rows = UiKit.AddSettingsScroll(box, 170);
		_firstSetting = FirstFocusable(rows.GetChild(0));

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
		// Esc / B backs out of the brightness screen, or the settings.
		if (_calibBox != null && _calibBox.Visible && e.IsActionPressed("ui_cancel"))
		{
			_calibBox.Visible = false;
			_menuBox.Visible = true;
			_firstItem?.CallDeferred(Control.MethodName.GrabFocus);
			GetViewport().SetInputAsHandled();
			return;
		}
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
