using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// A quiet record of how a play-through paces (2026-10-11, the owner's list: "a full play-through pass with a timer, logging
/// any spot where the player is idle for more than 2 minutes or stuck"; and "the timer to either be hidden or only viewable
/// in the pause menu"). Nothing on screen: it writes to user://pacing.txt.
/// <list type="bullet">
/// <item>each checkpoint, with the time since the last one (how long each stretch of the story took);</item>
/// <item>a stall: two minutes with no progress (no checkpoint, no flag) inside a 15 m circle: where, which act;</item>
/// <item>a long stretch: five minutes with no progress at all, wherever they wander.</item>
/// </list>
/// Read it after a play-test to find where a landmark, a light or a sound would help.
///
/// (2026-10-24, the game-loop pass: "chart the time between beats") Every beat also goes to user://pacing_beats.csv,
/// one line each: the play time, the act's checkpoint, what it was (a story flag set: a find, a scare, a puzzle, a
/// read; or a checkpoint) and where. tools/checks/pacing_chart.py turns it into a chart per act and a list of the dead
/// air (over 90 s with nothing happening). A test run writes it too when given "--log-beats".
/// </summary>
public partial class PacingLog : Node
{
	private const string Path = "user://pacing.txt";
	private const string BeatsPath = "user://pacing_beats.csv";
	private double _clock, _lastProgress, _lastCheckpointAt, _anchorAt, _lastWarn = -999;
	private Vector3 _anchor;
	private bool _anchorSet;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Pausable;
		var s = StoryManager.Instance;
		if (s == null) return;
		s.CheckpointReached += OnCheckpoint;
		s.FlagSet += OnFlag;
		Write($"--- session {Time.GetDatetimeStringFromSystem()}  at {s.Current}, {s.PlaySeconds / 60.0:0.0} min played");
		Beat("session", Time.GetDatetimeStringFromSystem().Replace(',', ' '));
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s) { s.CheckpointReached -= OnCheckpoint; s.FlagSet -= OnFlag; }
	}

	private void OnCheckpoint(Checkpoint cp)
	{
		Write($"{Stamp()}  checkpoint {cp}  ({(_clock - _lastCheckpointAt) / 60.0:0.0} min since the last)");
		_lastCheckpointAt = _clock;
		_lastProgress = _clock;
		Beat("checkpoint", cp.ToString());
	}

	private void OnFlag(string f) { _lastProgress = _clock; Beat("flag", f); }

	private static bool? _logBeats;

	/// <summary>One beat to the CSV: seconds played, checkpoint, kind, what, x, y, z.</summary>
	private void Beat(string kind, string what)
	{
		_logBeats ??= GameSettings.Instance?.AutoTest != true || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--log-beats") >= 0;
		if (_logBeats != true) return;
		double t = StoryManager.Instance?.PlaySeconds ?? _clock;
		var at = GetTree()?.GetFirstNodeInGroup("player") is Node3D p ? p.GlobalPosition : Vector3.Zero;
		bool fresh = !FileAccess.FileExists(BeatsPath);
		using var f = fresh ? FileAccess.Open(BeatsPath, FileAccess.ModeFlags.Write) : FileAccess.Open(BeatsPath, FileAccess.ModeFlags.ReadWrite);
		if (f == null) return;
		f.SeekEnd();
		if (fresh) f.StoreLine("seconds,checkpoint,kind,what,x,y,z");
		f.StoreLine(System.FormattableString.Invariant($"{t:0.0},{StoryManager.Instance?.Current},{kind},{what},{at.X:0.0},{at.Y:0.0},{at.Z:0.0}"));
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (GetTree().GetFirstNodeInGroup("player") is not Node3D p) return;
		if (!_anchorSet || p.GlobalPosition.DistanceTo(_anchor) > 15f) { _anchor = p.GlobalPosition; _anchorAt = _clock; _anchorSet = true; }
		double still = _clock - Mathf.Max(_anchorAt, _lastProgress);
		double lost = _clock - _lastProgress;
		if (_clock - _lastWarn < 60) return;
		if (still > 120) { _lastWarn = _clock; Write($"{Stamp()}  STALLED 2 min within 15 m at {p.GlobalPosition.Round()}  ({StoryManager.Instance?.Current})"); }
		else if (lost > 300) { _lastWarn = _clock; Write($"{Stamp()}  5 min without progress, now at {p.GlobalPosition.Round()}  ({StoryManager.Instance?.Current})"); }
	}

	private string Stamp()
	{
		double t = StoryManager.Instance?.PlaySeconds ?? _clock;
		return $"[{(int)(t / 3600)}:{(int)(t / 60) % 60:00}:{(int)t % 60:00}]";
	}

	private static void Write(string line)
	{
		if (GameSettings.Instance?.AutoTest == true) return;   // (the tests aren't play)
		using var f = FileAccess.FileExists(Path) ? FileAccess.Open(Path, FileAccess.ModeFlags.ReadWrite) : FileAccess.Open(Path, FileAccess.ModeFlags.Write);
		if (f == null) return;
		f.SeekEnd();
		f.StoreLine(line);
	}
}
