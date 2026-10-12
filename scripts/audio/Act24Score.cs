using Godot;
using ProjectDS.Entities;
using ProjectDS.Systems;

namespace ProjectDS.Audio;

/// <summary>
/// Act 24's score: the owner's song, "Petty Theft" (the trailer's), for the whole of the wendigo's hunt and the fight
/// (2026-10-11, the owner: "use the song for the whole act 24 wendigo fight"). Cut by the owner into eight-bar loops (85 bpm,
/// a bar 2.82 s), one-shot impacts and a fall; this plays the loop that fits what's happening and changes only on a bar
/// line, so the beat never breaks:
/// <list type="bullet">
/// <item>down in the maze, nothing near: the intro, then the build, low;</item>
/// <item>it's hunting them (it's heard or seen something): the main theme;</item>
/// <item>chased: section B, an impact as it begins;</item>
/// <item>the trenches, the cavern: section C;</item>
/// <item>the flamethrower in hand: the fall, then section D, an impact on its downbeat;</item>
/// <item>each burn an impact; after the second, the final A; after the third, the climax;</item>
/// <item>it dies: the last impact and the song's tail, and silence.</item>
/// </list>
/// A cue (<see cref="SongCues"/>) waits for its next beat. Nothing here is louder than the act's own sounds want: it sits
/// under the storm and the creature, a little louder as it closes in.
/// </summary>
public partial class Act24Score : Node
{
	public static Act24Score Instance { get; private set; }
	private const string Dir = "res://assets/audio/music/petty_theft/";
	public const float Bpm = 85f;
	public const float BeatSec = 60f / Bpm, BarSec = BeatSec * 4f;

	public System.Func<WendigoHunter> Hunter;
	public System.Func<bool> InCavern, InMaze;
	private AudioStreamPlayer _a, _b, _cur;
	private string _playing = "", _want = "";
	private double _clock, _startedAt;
	private int _lastHits;
	private bool _over, _wasChase;
	/// <summary>For tests: the loop playing now.</summary>
	public string Playing => _playing;
	public int Impacts { get; private set; }

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		Duck(0f);
	}

	// (2026-10-11, the owner: "there needs to be a ton of sound balancing in the caves for act 24, the music should be very
	// audible compared to everything else ... the growls can stay the same") while the song plays, the world's beds are
	// ducked under it: the wind, the weather, the far sounds and the nature beds well down, the steps and the events a
	// little; the creature (Unnatural) and the voices left as they are
	// (2026-10-12: the Music bus's base is -16 now, GameSettings.MusicBaseDb, so +10 here lands where +18 on -24 did)
	private static readonly (string bus, float db)[] Ducked =
		{ ("Music", 10f), ("Unnatural", -4f), ("Wind", -9f), ("Weather", -9f), ("Distant", -9f), ("Nature", -9f), ("Water", -6f), ("Events", -3f), ("Player", -3f) };
	private readonly System.Collections.Generic.Dictionary<string, float> _baseDb = new();
	private float _duck;

	private void Duck(float amount)
	{
		foreach (var (bus, db) in Ducked)
		{
			int i = AudioServer.GetBusIndex(bus);
			if (i < 0) continue;
			if (!_baseDb.ContainsKey(bus)) _baseDb[bus] = AudioServer.GetBusVolumeDb(i);
			AudioServer.SetBusVolumeDb(i, _baseDb[bus] + db * amount);
		}
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Pausable;
		string bus = AudioServer.GetBusIndex("Music") >= 0 ? "Music" : "Master";
		_a = new AudioStreamPlayer { Name = "ScoreA", Bus = bus, VolumeDb = -80f };
		_b = new AudioStreamPlayer { Name = "ScoreB", Bus = bus, VolumeDb = -80f };
		AddChild(_a);
		AddChild(_b);
	}

	/// <summary>Seconds to the next beat of what's playing (0 if nothing is).</summary>
	public double SecondsToNextBeat()
	{
		if (_cur == null || !_cur.Playing) return 0.0;
		double pos = _cur.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();
		double into = pos % BeatSec;
		return into < 0.03 ? 0.0 : BeatSec - into;
	}

	private double SecondsToNextBar()
	{
		if (_cur == null || !_cur.Playing) return 0.0;
		double pos = _cur.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix();
		double into = pos % BarSec;
		return into < 0.04 ? 0.0 : BarSec - into;
	}

	private string Choose()
	{
		var h = Hunter?.Invoke();
		var s = StoryManager.Instance;
		if (h == null || s == null) return "";
		if (h.Dead) return "";
		int hits = h.Hits;
		if (hits >= 3) return "loop_10_final_climax_bars85-92";
		if (hits >= 2) return "loop_09_final_A_bars77-84";
		if (s.Current >= Checkpoint.Act24Flamethrower) return "loop_08_section_D_bars65-72";
		if (InCavern?.Invoke() == true || s.Current >= Checkpoint.Act24Trenches && !(InMaze?.Invoke() ?? true)) return "loop_07_section_C_bars57-64";
		bool chase = h.State is WendigoHunter.Mode.Chase or WendigoHunter.Mode.Ambush or WendigoHunter.Mode.Kill;
		// (2026-10-11, the owner: "it should be looping on the soft drum parts, and when the wendigo is near it should ramp
		// up to the intense synth sections") by how near it is, as well as what it's doing:
		// a pair alternates once the one playing has gone all the way round: A, A2, A...; B, B2...; intro, build, intro...
		bool round = _clock - _startedAt > 22.4;
		float near = Near;
		if (chase || near > 0.8f) return Pair("loop_05_section_B_bars37-44", "loop_06_section_B2_bars49-56", round);
		if (near > 0.2f || h.Awareness > 0.45f || h.State is WendigoHunter.Mode.Investigate or WendigoHunter.Mode.Search or WendigoHunter.Mode.Stalk)
			return Pair("loop_03_main_A_bars17-24", "loop_04_main_A2_bars25-32", round);
		// far: the soft drums, round and round
		if (_playing == "") return "loop_01_intro_bars5-8";
		return Pair("loop_01_intro_bars5-8", "loop_02_build_bars9-16", _clock - _startedAt > (_playing.StartsWith("loop_01") ? 11.2 : 22.4));
	}

	/// <summary>How near the wendigo is: 0 at 30 m or more, 1 within 10 m (through the maze's walls: it's heard).</summary>
	private float Near
	{
		get
		{
			var h = Hunter?.Invoke();
			var p = GetTree().GetFirstNodeInGroup("player") as Node3D;
			if (h == null || p == null) return 0f;
			float d = h.GlobalPosition.DistanceTo(p.GlobalPosition);
			return Mathf.Clamp((30f - d) / 20f, 0f, 1f);
		}
	}

	private string Pair(string a, string b, bool round)
	{
		if (_playing == a) return round ? b : a;
		if (_playing == b) return round ? a : b;
		return a;
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (_over) return;
		var h = Hunter?.Invoke();
		bool active = h != null && (InMaze?.Invoke() == true || StoryManager.Instance is { Current: >= Checkpoint.Act24Trenches and < Checkpoint.Act24Finished });
		float duckTo = active && !_over ? 1f : 0f;
		if (!Mathf.IsEqualApprox(_duck, duckTo)) { _duck = Mathf.MoveToward(_duck, duckTo, (float)delta * 0.5f); Duck(_duck); }
		if (!active)
		{
			// out of the maze (a load, the hand-over): the score lets go
			foreach (var p in new[] { _a, _b }) if (p.Playing) p.VolumeDb = Mathf.MoveToward(p.VolumeDb, -80f, (float)delta * 20f);
			if (_cur != null && _cur.VolumeDb <= -79f) { _a.Stop(); _b.Stop(); _cur = null; _playing = ""; }
			return;
		}
		// moments: an impact on a chase's start and each burn; the end
		bool chase = h.State is WendigoHunter.Mode.Chase or WendigoHunter.Mode.Ambush;
		if (chase && !_wasChase) Impact(Impacts % 2 == 0 ? "oneshot_impact_A_bar37" : "oneshot_impact_B_bar41");
		_wasChase = chase;
		if (h.Hits > _lastHits)
		{
			Impact(h.Hits switch { 1 => "oneshot_impact_C_bar63", 2 => "oneshot_impact_E_bar75", 3 => "oneshot_impact_F_bar77", _ => "oneshot_impact_G_bar95" });
			_lastHits = h.Hits;
		}
		if (h.Dead)
		{
			_over = true;
			Impact("oneshot_impact_G_bar95");
			Tail("outro_tail_bar96");
			var tw = CreateTween();
			if (_cur != null) tw.TweenProperty(_cur, "volume_db", -80f, 1.2f);
			return;
		}
		// the loop wanted; changed on the next bar line (the loop's own end, if it's the same one, just goes round)
		_want = Choose();
		// (the loops are quiet in themselves, -15 to -22 dB: at -15 under the storm the song couldn't be heard at all) the
		// soft drums at -4, rising toward +4 as it closes in and hunts
		float level = -4f + 5f * Near + 2f * Mathf.Clamp(h.Awareness, 0f, 1f) + (chase ? 1.5f : 0f);
		if (_cur != null) _cur.VolumeDb = Mathf.MoveToward(_cur.VolumeDb, level, (float)delta * 6f);
		if (_playing == "" && _want != "") StartLoop(_want, level);
		else if (_want != _playing && _want != "" && SecondsToNextBar() < delta * 1.5)
		{
			// the flamethrower's first: the fall first, then D on its downbeat
			if (_want.StartsWith("loop_08") && !_playing.StartsWith("loop_08")) { Tail("transition_fall_bars35-36"); Impact("oneshot_impact_D_bar65"); }
			StartLoop(_want, level);
		}
	}

	private void StartLoop(string name, float level)
	{
		var path = Dir + name + ".ogg";
		if (!ResourceLoader.Exists(path)) return;
		var st = GD.Load<AudioStream>(path);
		if (st is AudioStreamOggVorbis ogg) ogg.Loop = true;
		var next = _cur == _a ? _b : _a;
		var prev = _cur;
		next.Stream = st;
		next.VolumeDb = prev == null ? -80f : level;
		next.Play();
		if (prev == null)
		{
			var tw = CreateTween();
			tw.TweenProperty(next, "volume_db", level, 3.0f);
		}
		else
		{
			var tw = CreateTween();
			tw.TweenProperty(prev, "volume_db", -80f, 0.12f);
			tw.TweenCallback(Callable.From(prev.Stop));
		}
		_cur = next;
		_playing = name;
		_startedAt = _clock;
		GD.Print($"[score] Act 24: {name}");
	}

	private void Impact(string name) => OneShot(name, 0f);
	private void Tail(string name) => OneShot(name, -2f);

	private void OneShot(string name, float db)
	{
		var path = Dir + name + ".ogg";
		if (!ResourceLoader.Exists(path)) return;
		var p = new AudioStreamPlayer { Name = name, Stream = GD.Load<AudioStream>(path), Bus = _a.Bus, VolumeDb = db };
		AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
		Impacts++;
	}
}
