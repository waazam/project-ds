using System.Linq;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// F9 anywhere (2026-10-22, for the playtests): a bug report saved where the tester can find it and send it,
/// <c>%APPDATA%\Godot\app_userdata\Project DS\bug_reports\</c>: the picture as it is, and a text file with what's needed to
/// put us exactly there (the checkpoint and the story's flags, where they stand and which way they face, what they carry,
/// the act, the frame rate, the machine, the settings), and a copy of this session's log so far. A line on screen says it
/// was saved and where. Never in the way: it takes no input from the game and works paused, in menus and in cutscenes.
/// </summary>
public partial class BugReport : CanvasLayer
{
	public const Key HotKey = Key.F9;
	private Label _note;
	private float _noteT = -1f;

	/// <summary>For tests: reports saved this session, and the last one's folder.</summary>
	public static int Saved { get; private set; }
	public static string LastPath { get; private set; } = "";

	public override void _Ready()
	{
		Layer = 120;
		ProcessMode = ProcessModeEnum.Always;
		_note = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false,
		};
		_note.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		_note.OffsetTop = 28;
		_note.AddThemeFontSizeOverride("font_size", 11);
		_note.AddThemeColorOverride("font_color", new Color(0.92f, 0.9f, 0.84f));
		_note.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
		_note.AddThemeConstantOverride("shadow_offset_y", 1);
		AddChild(_note);
	}

	public override void _Input(InputEvent e)
	{
		if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode == HotKey) Save();
	}

	public override void _Process(double delta)
	{
		if (_noteT < 0f) return;
		_noteT += (float)delta;
		_note.Modulate = new Color(1, 1, 1, Mathf.Clamp(3.5f - _noteT, 0f, 1f));
		if (_noteT > 3.5f) { _noteT = -1f; _note.Visible = false; }
	}

	/// <summary>Saves a report now (the picture is taken at the end of this frame, without the note in it).</summary>
	public async void Save()
	{
		string stamp = Time.GetDatetimeStringFromSystem().Replace(":", "-").Replace("T", "_");
		string dir = $"user://bug_reports/{stamp}";
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
		_note.Visible = false;
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		GetViewport().GetTexture().GetImage()?.SavePng($"{dir}/screen.png");

		var sb = new System.Text.StringBuilder();
		var story = StoryManager.Instance;
		var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		sb.AppendLine($"Dead Silent bug report, {Time.GetDatetimeStringFromSystem()}");
		sb.AppendLine("What happened (fill in): ");
		sb.AppendLine();
		sb.AppendLine($"scene: {GetTree().CurrentScene?.SceneFilePath}");
		sb.AppendLine($"act: {PerfProbe.Act}");
		sb.AppendLine($"checkpoint: {story?.Current}");
		sb.AppendLine($"deaths: {story?.TotalDeaths}, play time: {story?.PlaySeconds:0} s");
		if (player != null)
		{
			sb.AppendLine($"position: {player.GlobalPosition}, facing yaw {Mathf.RadToDeg(player.CameraRig?.Yaw ?? 0f):0} deg, on floor {player.IsOnFloor()}");
			sb.AppendLine($"carrying: {player.Inventory?.Serialize()}");
			sb.AppendLine($"input enabled: {player.PlayerInput.Enabled}");
		}
		sb.AppendLine($"fps: {Engine.GetFramesPerSecond():0}, paused: {GetTree().Paused}");
		sb.AppendLine($"machine: {OS.GetName()} {OS.GetVersion()}, GPU {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()}), {OS.GetProcessorName()}, {OS.GetProcessorCount()} threads");
		sb.AppendLine($"window: {DisplayServer.WindowGetSize()}, fullscreen {DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen}");
		if (GameSettings.Instance is { } gs) sb.AppendLine($"settings: {string.Join(", ", gs.Snapshot().Select(p => $"{p.Key}={p.Value}"))}");
		sb.AppendLine($"flags: {(story != null ? string.Join(", ", story.Flags.OrderBy(f => f)) : "")}");
		using (var f = FileAccess.Open($"{dir}/report.txt", FileAccess.ModeFlags.Write)) f?.StoreString(sb.ToString());
		// this session's log so far (Godot writes it as the game runs)
		const string log = "user://logs/godot.log";
		if (FileAccess.FileExists(log))
		{
			using var src = FileAccess.Open(log, FileAccess.ModeFlags.Read);
			if (src != null) { using var dst = FileAccess.Open($"{dir}/log.txt", FileAccess.ModeFlags.Write); dst?.StoreString(src.GetAsText()); }
		}
		Saved++;
		LastPath = ProjectSettings.GlobalizePath(dir);
		GD.Print($"[bug-report] saved to {LastPath}");
		_note.Text = $"Bug report saved  ({LastPath})";
		_note.Visible = true;
		_noteT = 0f;
	}
}
