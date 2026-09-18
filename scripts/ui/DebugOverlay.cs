using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>F3: developer readout of position, speed, forest silence, and bus levels.</summary>
public partial class DebugOverlay : CanvasLayer
{
	private Label _label;
	private PlayerController _player;
	private static readonly string[] Buses = { "Birds", "Insects", "Wind", "Distant", "Water", "Unnatural" };

	public override void _Ready()
	{
		Layer = 40;
		ProcessMode = ProcessModeEnum.Always;
		_label = new Label { Position = new Vector2(4, 4), Visible = false };
		_label.AddThemeFontSizeOverride("font_size", 8);
		_label.AddThemeColorOverride("font_color", new Color(0.8f, 1f, 0.8f));
		_label.AddThemeColorOverride("font_shadow_color", Colors.Black);
		AddChild(_label);
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
			_label.Visible = !_label.Visible;
	}

	public override void _Process(double delta)
	{
		if (!_label.Visible) return;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		var m = ForestAmbienceManager.Instance;
		var text = $"FPS {Engine.GetFramesPerSecond()}\n";
		if (_player != null)
			text += $"pos {_player.GlobalPosition.X:0.0} {_player.GlobalPosition.Y:0.0} {_player.GlobalPosition.Z:0.0}  speed {_player.GroundSpeed:0.0}{(_player.IsRunning ? " RUN" : "")}\n";
		if (m != null)
		{
			text += $"silence {m.Silence:0.00} (target {m.TargetSilence:0.00})\n";
			foreach (var bus in Buses)
				text += $"{bus,-9} {AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(bus)),6:0.0} dB\n";
		}
		_label.Text = text;
	}
}
