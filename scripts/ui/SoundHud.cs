using System.Collections.Generic;
using Godot;

namespace ProjectDS.UI;

/// <summary>
/// Dev: `--soundhud` lists sounds on screen as they start (the newest on top, fading), with the bus, volume,
/// distance and the node playing it, and under that every sound still playing. For pinning down a sound you
/// hear in play (the owner, 2026-10-15: the buzz near the Act 2 cabin). Same scan as StoryTest's `--soundlog`.
/// </summary>
public partial class SoundHud : CanvasLayer
{
	private const int Keep = 16;
	private readonly HashSet<ulong> _on = new();
	private readonly List<(double t, string line)> _recent = new();
	private Label _label;
	private double _scan;

	public override void _Ready()
	{
		Layer = 41;
		ProcessMode = ProcessModeEnum.Always;
		var bg = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Position = new Vector2(6, 6), Size = new Vector2(620, 10), MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(bg);
		_label = new Label { Position = new Vector2(12, 8), Size = new Vector2(900, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
		_label.AddThemeFontSizeOverride("font_size", 13);
		_label.AddThemeColorOverride("font_shadow_color", Colors.Black);
		_label.Resized += () => bg.Size = new Vector2(Mathf.Max(620, _label.Size.X + 12), _label.Size.Y + 6);
		AddChild(_label);
	}

	/// <summary>Beds and score left off the list (the owner: no water, rain, pressure or music), so the one-shots stand out.</summary>
	private static bool Background(string path, string bus)
	{
		if (string.IsNullOrEmpty(path) || bus is "Music" or "Weather" or "Water") return true;
		string f = System.IO.Path.GetFileNameWithoutExtension(path);
		foreach (var w in new[] { "loop", "rain", "pressure", "water", "stream", "lake", "fall", "score", "synth", "drone", "hum", "ringing", "heartbeat", "undertone", "wind" })
			if (f.Contains(w)) return true;
		return false;
	}

	public override void _Process(double delta)
	{
		_scan -= delta;
		if (_scan > 0) return;
		_scan = 0.05;
		var listener = GetViewport().GetCamera3D();
		double now = Time.GetTicksMsec() / 1000.0;
		var seen = new HashSet<ulong>();
		var playing = new SortedDictionary<string, int>();
		var stack = new Stack<Node>();
		stack.Push(GetTree().Root);
		while (stack.Count > 0)
		{
			var n = stack.Pop();
			foreach (var c in n.GetChildren()) stack.Push(c);
			string path = null, bus = null; bool on = false; float db = 0; Vector3? at = null;
			if (n is AudioStreamPlayer3D p3) { on = p3.Playing; path = p3.Stream?.ResourcePath; bus = p3.Bus; db = p3.VolumeDb; at = p3.GlobalPosition; }
			else if (n is AudioStreamPlayer p2) { on = p2.Playing; path = p2.Stream?.ResourcePath; bus = p2.Bus; db = p2.VolumeDb; }
			if (!on || db <= -60f) continue;
			if (Background(path, bus)) continue;
			string name = string.IsNullOrEmpty(path) ? $"({n.GetParent()?.Name}/{n.Name})" : System.IO.Path.GetFileNameWithoutExtension(path);
			playing[name] = playing.TryGetValue(name, out int k) ? k + 1 : 1;
			ulong id = n.GetInstanceId();
			seen.Add(id);
			if (_on.Contains(id)) continue;
			string dist = at.HasValue && listener != null ? $"{at.Value.DistanceTo(listener.GlobalPosition):0}m" : "flat";
			_recent.Insert(0, (now, $"{name}   {bus} {db:0}dB {dist}   ({n.GetParent()?.Name}/{n.Name})"));
			if (_recent.Count > Keep) _recent.RemoveAt(_recent.Count - 1);
		}
		_on.Clear();
		foreach (var id in seen) _on.Add(id);

		var sb = new System.Text.StringBuilder("SOUNDS (newest first)\n");
		foreach (var (t, line) in _recent) sb.Append($"{now - t,5:0.0}s  {line}\n");
		sb.Append("\nPLAYING NOW: ");
		foreach (var (name, count) in playing) sb.Append(count > 1 ? $"{name} x{count}, " : $"{name}, ");
		_label.Text = sb.ToString();
		_label.AddThemeColorOverride("font_color", new Color(0.85f, 1f, 0.85f));
	}
}
