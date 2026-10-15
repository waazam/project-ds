using System.Linq;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// `-- --rec-frames=<dir>`: a frame every <see cref="Every"/> seconds (wall clock) to res://test-output/<dir>, with a line
/// per frame in its log (the time since start, the scene, the master bus's level and peak), under the tree's root so it
/// runs on through level changes. For looking at transitions frame by frame (2026-10-15: the Act 1 to 2 load).
/// </summary>
public partial class FrameRecorder : Node
{
	public double Every = 0.25;
	private string _dir;
	private ulong _start, _next;
	private int _n;
	private FileAccess _log;

	public static void StartIfAsked(SceneTree tree)
	{
		string dir = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--rec-frames="))?["--rec-frames=".Length..];
		if (string.IsNullOrEmpty(dir) || tree?.Root == null) return;
		var r = new FrameRecorder { Name = "FrameRecorder", _dir = $"res://test-output/{dir}" };
		Callable.From(() => tree.Root.AddChild(r)).CallDeferred();
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		var abs = ProjectSettings.GlobalizePath(_dir);
		DirAccess.MakeDirRecursiveAbsolute(abs);
		_log = FileAccess.Open($"{_dir}/frames.txt", FileAccess.ModeFlags.Write);
		_start = Time.GetTicksMsec();
		_next = _start;
		RenderingServer.FramePostDraw += Grab;
	}

	public override void _ExitTree() { RenderingServer.FramePostDraw -= Grab; _log?.Close(); }

	private ulong _lastFrame;

	private void Grab()
	{
		ulong now = Time.GetTicksMsec();
		// a frame that took long to come (a build held the main thread): logged, so frozen stretches show
		ulong gap = _lastFrame == 0 ? 0 : now - _lastFrame;
		_lastFrame = now;
		if (gap > 150) _log?.StoreLine($"{(now - _start) / 1000.0,8:0.00}  (no frame for {gap} ms)");
		if (now < _next) return;
		_next = now + (ulong)(Every * 1000);
		int master = AudioServer.GetBusIndex("Master");
		float peak = master >= 0 ? Mathf.Max(AudioServer.GetBusPeakVolumeLeftDb(master, 0), AudioServer.GetBusPeakVolumeRightDb(master, 0)) : -80f;
		var scene = GetTree()?.CurrentScene?.Name ?? "-";
		_log?.StoreLine($"{(now - _start) / 1000.0,8:0.00}  #{_n:000}  {scene}  master peak {peak:0.0} dB");
		var img = GetViewport()?.GetTexture()?.GetImage();
		if (img != null)
		{
			img.Resize(img.GetWidth() / 4, img.GetHeight() / 4);
			img.SavePng(ProjectSettings.GlobalizePath($"{_dir}/f{_n:000}.png"));
		}
		_n++;
		_log?.Flush();
	}
}
