using System;
using Godot;

namespace ProjectDS.UI;

/// <summary>Tiny shared builders for the placeholder-grade menus (pause, main menu).</summary>
public static class UiKit
{
	public static Button MakeButton(string text, Action onPressed)
	{
		var b = new Button { Text = text };
		b.AddThemeFontSizeOverride("font_size", 10);
		b.Pressed += onPressed;
		return b;
	}

	public static void AddSlider(Container parent, string label, double min, double max, double value, Action<double> onChanged)
	{
		var l = new Label { Text = label };
		l.AddThemeFontSizeOverride("font_size", 9);
		parent.AddChild(l);
		var slider = new HSlider { MinValue = min, MaxValue = max, Step = (max - min) / 100.0, Value = value };
		slider.ValueChanged += v => onChanged(v);
		parent.AddChild(slider);
	}
}
