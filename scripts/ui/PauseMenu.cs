using System;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// Esc / Start: pauses the game and shows camera settings. Placeholder-grade
/// UI built in code, to be replaced by the final menu later.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	private Control _root;
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
		_root = new ColorRect { Color = new Color(0, 0, 0, 0.72f) };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		var box = new VBoxContainer();
		box.SetAnchorsPreset(Control.LayoutPreset.Center);
		box.OffsetLeft = -120; box.OffsetRight = 120; box.OffsetTop = -95; box.OffsetBottom = 95;
		box.AddThemeConstantOverride("separation", 5);
		_root.AddChild(box);

		var title = new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 14);
		box.AddChild(title);

		var s = GameSettings.Instance;
		AddSlider(box, "Mouse sensitivity", 0.0005, 0.008, s.MouseSensitivity, v => s.MouseSensitivity = (float)v);
		AddSlider(box, "Stick sensitivity", 0.8, 5.0, s.StickSensitivity, v => s.StickSensitivity = (float)v);
		AddSlider(box, "Camera distance", 1.6, 5.5, s.CameraDistance, v => s.CameraDistance = (float)v);

		var invert = new CheckBox { Text = "Invert Y", ButtonPressed = s.InvertY };
		invert.AddThemeFontSizeOverride("font_size", 9);
		invert.Toggled += on => s.InvertY = on;
		box.AddChild(invert);

		var resume = MakeButton("Resume", () => SetOpen(false));
		box.AddChild(resume);
		box.AddChild(MakeButton("Quit", () => GetTree().Quit()));
		resume.CallDeferred(Control.MethodName.GrabFocus);
		_root.VisibilityChanged += () => { if (_root.Visible) resume.GrabFocus(); };
	}

	private static Button MakeButton(string text, Action onPressed)
	{
		var b = new Button { Text = text };
		b.AddThemeFontSizeOverride("font_size", 10);
		b.Pressed += onPressed;
		return b;
	}

	private static void AddSlider(Container parent, string label, double min, double max, double value, Action<double> onChanged)
	{
		var l = new Label { Text = label };
		l.AddThemeFontSizeOverride("font_size", 9);
		parent.AddChild(l);
		var slider = new HSlider { MinValue = min, MaxValue = max, Step = (max - min) / 100.0, Value = value };
		slider.ValueChanged += v => onChanged(v);
		parent.AddChild(slider);
	}
}
