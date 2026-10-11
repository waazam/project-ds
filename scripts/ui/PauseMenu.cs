using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// Esc / Start: pauses the game and shows the settings. The world stays visible
/// under a near-black fog wash; a serif heading, the shared settings rows and
/// plain-text actions, all in the UiKit theme.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	private Control _root;
	private MenuItem _resume;
	public bool Locked;   // e.g. during the ending

	public override void _Ready()
	{
		Layer = 30;
		ProcessMode = ProcessModeEnum.Always;
		Build();
		_root.Visible = false;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e.IsActionPressed("pause") && !Locked)
		{
			SetOpen(!_root.Visible);
			GetViewport().SetInputAsHandled();
		}
		else if (!_root.Visible && e is InputEventMouseButton { Pressed: true } && !Locked
			&& Input.MouseMode != Input.MouseModeEnum.Captured)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
	}

	public void SetOpen(bool open)
	{
		_root.Visible = open;
		GetTree().Paused = open;
		Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
		if (!open) GameSettings.Instance.Save();
	}

	private Label _time;

	private void Build()
	{
		_root = UiKit.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Stop });
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		// Fog wash: darker at the edges, a little of the world still showing through the middle.
		var wash = new ColorRect { Color = new Color(UiKit.Night, 0.86f), MouseFilter = Control.MouseFilterEnum.Ignore };
		wash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(wash);
		_root.AddChild(MenuBackdrop.MakeVignette(0.6f));

		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		box.SetAnchorsPreset(Control.LayoutPreset.Center);
		box.OffsetLeft = -135; box.OffsetRight = 135; box.OffsetTop = -120; box.OffsetBottom = 120;
		box.GrowHorizontal = Control.GrowDirection.Both; box.GrowVertical = Control.GrowDirection.Both;
		box.AddThemeConstantOverride("separation", 5);
		_root.AddChild(box);

		box.AddChild(UiKit.MakeLabel("PAUSED", UiKit.HeadingLabel, HorizontalAlignment.Center));
		// the time played, here only (2026-10-11, the owner: "we need the timer to either be hidden or only viewable in the
		// pause menu"), and the chapter they're in
		_time = UiKit.MakeLabel("", UiKit.DimLabel, HorizontalAlignment.Center);
		box.AddChild(_time);
		box.AddChild(UiKit.Rule(150));
		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

		UiKit.AddSettingsScroll(box, 160);

		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
		_resume = UiKit.MakeButton("Resume", () => SetOpen(false), true);
		box.AddChild(_resume);
		box.AddChild(UiKit.MakeButton("Quit to Menu", () => { GameSettings.Instance.Save(); StoryManager.Instance.ReturnToMenu(); }, true));
		box.AddChild(UiKit.MakeButton("Quit", () => GetTree().Quit(), true));
		_root.VisibilityChanged += () =>
		{
			if (!_root.Visible) return;
			_resume.CallDeferred(Control.MethodName.GrabFocus);
			var sm = StoryManager.Instance;
			double t = sm?.PlaySeconds ?? 0;
			int ch = 0;
			if (sm != null) for (int n = 1; n < ChapterCard.Titles.Length; n++) if (sm.HasFlag(ChapterCard.Flag(n))) ch = n;
			string chapter = ch > 0 ? $"Chapter {ch}: {System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(ChapterCard.Titles[ch].ToLowerInvariant())}   ·   " : "";
			_time.Text = $"{chapter}{(int)(t / 3600)}:{(int)(t / 60) % 60:00}:{(int)t % 60:00} played";
		};
	}
}
