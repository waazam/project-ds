using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;

namespace ProjectDS.UI;

/// <summary>
/// F3: developer readout of position, speed, forest silence and bus levels,
/// plus a frame around the stalker whenever it is out there (so you can check
/// it's working even when you can't spot it).
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
	private Label _label;
	private Control _marks;
	private PlayerController _player;
	private Entities.Stalker _stalker;
	private static readonly string[] Buses = { "Birds", "Insects", "Wind", "Distant", "Water", "Unnatural" };
	private static readonly Color FrameColor = new(1f, 0.25f, 0.2f, 0.9f);

	public override void _Ready()
	{
		Layer = 40;
		ProcessMode = ProcessModeEnum.Always;
		_marks = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_marks.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_marks.Draw += DrawStalkerFrame;
		AddChild(_marks);
		_label = new Label { Position = new Vector2(4, 4), Visible = false };
		_label.AddThemeFontSizeOverride("font_size", 8);
		_label.AddThemeColorOverride("font_color", new Color(0.8f, 1f, 0.8f));
		_label.AddThemeColorOverride("font_shadow_color", Colors.Black);
		AddChild(_label);
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (OS.IsDebugBuild() && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })   // dev builds only
		{
			_label.Visible = !_label.Visible;
			_marks.Visible = _label.Visible;
		}
	}

	public override void _Process(double delta)
	{
		if (!_label.Visible) return;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_stalker ??= GetTree().Root.FindChild("Stalker", true, false) as Entities.Stalker;
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
		if (GetTree().Root.FindChild("Score", true, false) is MusicDirector md)
			text += $"music intensity {md.Intensity:0.00} cut {md.Cut:0.00}\n";
		if (GetTree().Root.FindChild("Director", true, false) is ForestDirector d)
			text += $"still {d.StillSeconds:0}s ({d.Stillness:0.00}) dropout {d.Dropout:0.0} (#{d.DropoutCount})  activity {d.Activity:0.00}  gust {d.GustLevel:0.00} (#{d.GustCount})\n";
		if (_stalker != null)
			text += $"stalker {_stalker.Current}{(_stalker.IsDistant ? " (ahead)" : "")} appearances {_stalker.PeekCount} (ahead {_stalker.DistantCount}) seen {_stalker.SeenCount} sounds {_stalker.SoundCount} tension {_stalker.Tension:0.00}\n";
		_label.Text = text;
		_marks.QueueRedraw();
	}

	/// <summary>A box around the stalker on screen, or an arrow at the screen edge pointing to it.</summary>
	private void DrawStalkerFrame()
	{
		if (_stalker == null || !_stalker.IsPresent) return;
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;

		var size = _marks.Size;
		bool any = false;
		Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
		Vector3 centre = Vector3.Zero; int n = 0;
		foreach (var p in _stalker.WorldSamplePoints())
		{
			centre += p; n++;
			if (cam.IsPositionBehind(p)) continue;
			var s = cam.UnprojectPosition(p);
			min = min.Min(s); max = max.Max(s);
			any = true;
		}
		if (n == 0) return;
		centre /= n;
		float dist = cam.GlobalPosition.DistanceTo(centre);
		string label = $"STALKER {dist:0}m";

		var rect = new Rect2(min - new Vector2(3, 3), max - min + new Vector2(6, 6));
		if (any && rect.Intersects(new Rect2(Vector2.Zero, size)))
		{
			_marks.DrawRect(rect, FrameColor, false, 1f);
			_marks.DrawString(ThemeDB.FallbackFont, rect.Position + new Vector2(0, -2), label,
				HorizontalAlignment.Left, -1, 8, FrameColor);
			return;
		}

		// Off screen: an arrow on the edge pointing toward it.
		Vector3 local = cam.GlobalTransform.AffineInverse() * centre;
		var dir = new Vector2(local.X, -local.Y);
		if (dir.LengthSquared() < 0.0001f) dir = Vector2.Down;
		dir = dir.Normalized();
		var mid = size * 0.5f;
		float scale = Mathf.Min((mid.X - 14f) / Mathf.Max(Mathf.Abs(dir.X), 0.001f), (mid.Y - 14f) / Mathf.Max(Mathf.Abs(dir.Y), 0.001f));
		var tip = mid + dir * scale;
		var side = new Vector2(-dir.Y, dir.X) * 4f;
		_marks.DrawColoredPolygon(new[] { tip, tip - dir * 8f + side, tip - dir * 8f - side }, FrameColor);
		_marks.DrawString(ThemeDB.FallbackFont, tip - dir * 12f + new Vector2(-18, 3), label,
			HorizontalAlignment.Left, -1, 8, FrameColor);
	}
}
