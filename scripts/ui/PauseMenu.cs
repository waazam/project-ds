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
		box.AddChild(UiKit.Rule(150));
		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

		UiKit.AddSettingsRows(box);

		box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
		_resume = UiKit.MakeButton("Resume", () => SetOpen(false), true);
		box.AddChild(_resume);
		box.AddChild(UiKit.MakeButton("Quit to Menu", () => { GameSettings.Instance.Save(); StoryManager.Instance.ReturnToMenu(); }, true));
		box.AddChild(UiKit.MakeButton("Quit", () => GetTree().Quit(), true));
		_root.VisibilityChanged += () => { if (_root.Visible) _resume.CallDeferred(Control.MethodName.GrabFocus); };
	}
}
