using System.Collections.Generic;
using Godot;

namespace ProjectDS.Player;

/// <summary>
/// The player's breath, built one breath at a time so it never loops. Exertion
/// rises while running and slowly settles after. It sets how fast the breaths
/// come, how hard they are (which set of samples, resting to winded) and how
/// loud they are. At rest or walking calmly you don't hear it at all. Each
/// breath gets its own timing, pitch and level jitter, and the same sample
/// never plays twice in a row.
/// </summary>
public partial class PlayerBreathing : Node
{
	/// <summary>Muted for now (Dan, 2026-09-18) while the breath sound is reworked.</summary>
	[Export] public bool Muted = true;
	[Export] public float MaxVolumeDb = -21f;
	[Export] public float ExertionRise = 0.18f;   // per second while running
	[Export] public float ExertionFall = 0.06f;   // per second otherwise
	/// <summary>Breaths per minute at rest .. fully winded.</summary>
	[Export] public Vector2 BreathsPerMinute = new(14f, 38f);
	/// <summary>Below this exertion the breath is inaudible.</summary>
	[Export] public float AudibleFrom = 0.12f;

	public float Exertion { get; private set; }
	public float CurrentVolumeDb => _voice?.VolumeDb ?? -80f;

	private PlayerController _player;
	private readonly List<AudioStream> _in = new(), _out = new();
	private readonly RandomNumberGenerator _rng = new();
	private AudioStreamPlayer _voice;
	private Audio.SamplePicker _inPick, _outPick;
	private float _timer = 1f;
	private bool _nextIsIn = true;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		for (int i = 1; i <= 5; i++)
		{
			Load($"res://assets/audio/sfx/breath_in_{i:00}.wav", _in);
			Load($"res://assets/audio/sfx/breath_out_{i:00}.wav", _out);
		}
		_voice = new AudioStreamPlayer { Bus = "Player", VolumeDb = -80f };
		AddChild(_voice);
	}

	private static void Load(string path, List<AudioStream> into)
	{
		if (ResourceLoader.Exists(path)) into.Add(GD.Load<AudioStream>(path));
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		Exertion = _player.IsRunning
			? Mathf.Min(1f, Exertion + ExertionRise * dt)
			: Mathf.Max(0f, Exertion - ExertionFall * dt);

		_timer -= dt;
		if (_timer > 0f) return;

		// One breath cycle at this effort; the inhale takes ~40% of it.
		float bpm = Mathf.Lerp(BreathsPerMinute.X, BreathsPerMinute.Y, Exertion) * _rng.RandfRange(0.88f, 1.12f);
		float cycle = 60f / bpm;
		_timer = (_nextIsIn ? 0.42f : 0.58f) * cycle;

		if (!Muted && Exertion >= AudibleFrom) PlayBreath(_nextIsIn);
		_nextIsIn = !_nextIsIn;
	}

	private void PlayBreath(bool inhale)
	{
		var set = inhale ? _in : _out;
		if (set.Count == 0) return;
		// Harder breaths come from the rougher end of the set, with some wander.
		int idx = Mathf.Clamp(Mathf.RoundToInt(Exertion * (set.Count - 1)) + _rng.RandiRange(-1, 1), 0, set.Count - 1);
		if (_rng.Randf() < 0.3f) idx = inhale ? _inPick.Next(_rng, set.Count) : _outPick.Next(_rng, set.Count);

		float audible = Mathf.SmoothStep(AudibleFrom, 1f, Exertion);
		_voice.Stream = set[idx];
		_voice.VolumeDb = MaxVolumeDb + Mathf.LinearToDb(Mathf.Max(audible, 0.0001f)) + _rng.RandfRange(-2f, 1f);
		_voice.PitchScale = _rng.RandfRange(0.94f, 1.06f);
		_voice.Play();
	}
}
