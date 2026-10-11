using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Audio;

/// <summary>
/// The song's synth as the game's cues (2026-10-11, the owner: "use some of the synths in the song as sound ques when we are
/// picking up objects/progressing objectives ... The progression of the song for sound ques through the chopped synth should
/// be synth_1_bar93 > synth_2_bar93 > synth_3_bar94 so it sounds in order with the beat. synth_4 > synth_5 > synth_6 can
/// also use this pattern when underground as it has more echo to it").
///
/// Each thing picked up, each note read, each objective reached plays the next of the three in turn, so a run of finds
/// plays the phrase in order: 1, 2, 3, 1, 2... Above ground the dry three; underground (in a tunnel's, a cavern's or the snow
/// maze's space, or under the terrain) the echoing three. While Act 24's score plays, a cue waits for the next beat of it
/// (<see cref="Act24Score"/>), so it lands in time with the song.
/// </summary>
public partial class SongCues : Node
{
	public static SongCues Instance { get; private set; }
	private static readonly string[] Dry = { "synth_1_bar93", "synth_2_bar93p5", "synth_3_bar94" };
	private static readonly string[] Wet = { "synth_4_bar95", "synth_5_bar95p5", "synth_6_bar96" };
	private int _step;
	private double _lastAt = -10, _clock;
	private double _pendingAt = -1;
	private bool _pendingUnder;
	/// <summary>For tests: how many cues have played, and the last one's name.</summary>
	public int Played { get; private set; }
	public string Last { get; private set; } = "";

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (StoryManager.Instance is { } s) { s.FlagSet -= OnFlag; s.CheckpointReached -= OnCheckpoint; }
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		if (StoryManager.Instance is { } s) { s.FlagSet += OnFlag; s.CheckpointReached += OnCheckpoint; }
	}

	private void OnFlag(string flag)
	{
		// a thing taken, a note or page read, a door or lock got past: the flags the story sets for them
		if (flag.StartsWith("pickup_taken_") || flag.StartsWith("note_") || flag.Contains("_note") || flag.StartsWith("lore_page")
			|| flag.EndsWith("_open") || flag.EndsWith("_unlocked") || flag.Contains("solved"))
			Cue();
	}

	private void OnCheckpoint(Checkpoint cp) => Cue();

	/// <summary>The next of the phrase (a moment's grace: two flags at once are one find, one cue).</summary>
	public void Cue()
	{
		if (_clock - _lastAt < 0.6) return;
		_lastAt = _clock;
		_pendingUnder = Underground();
		// in time with Act 24's score, if it's playing
		double wait = Act24Score.Instance?.SecondsToNextBeat() ?? 0.0;
		_pendingAt = _clock + wait;
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (_pendingAt < 0 || _clock < _pendingAt) return;
		_pendingAt = -1;
		var set = _pendingUnder ? Wet : Dry;
		string name = set[_step % 3];
		_step++;
		var path = $"res://assets/audio/music/petty_theft/{name}.ogg";
		if (!ResourceLoader.Exists(path)) return;
		var p = new AudioStreamPlayer { Name = "Cue", Stream = GD.Load<AudioStream>(path), Bus = AudioServer.GetBusIndex("Music") >= 0 ? "Music" : "Master", VolumeDb = _pendingUnder ? -3f : -5f };   // (2026-10-11: up with the score; at -11 they were lost)
		AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
		Played++;
		Last = name;
	}

	private bool Underground()
	{
		if (AudioDirector.CurrentSpace is AudioDirector.Space.Tunnel or AudioDirector.Space.Cavern or AudioDirector.Space.Snow or AudioDirector.Space.IceCavern) return true;
		return ForestAmbienceManager.Instance is { Underground: > 0.5f };
	}
}
