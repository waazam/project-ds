using Godot;

namespace ProjectDS.World;

/// <summary>
/// The lodge's small sounds (the detail pass, 2026-10-01): the grandfather clock by the stairs ticking (until the
/// lodge freezes over: then it stops, and the quiet is worse), and now and then a glass on the back bar's shelves
/// touching the next with nobody there.
/// </summary>
public partial class SkiLodge
{
	private AudioStreamPlayer3D _clockTick, _barGlass;
	private AudioStream[] _clinks;
	private double _nextClink = 12;
	/// <summary>For tests: whether the clock is going, and how often the bar's glasses have clinked.</summary>
	public bool ClockTicking => _clockTick != null && _clockTick.Playing && !_clockTick.StreamPaused;
	public int BarClinks { get; private set; }

	private void BuildDetailSounds()
	{
		const string tick = "res://assets/audio/sfx/clock_longcase_loop.wav";
		if (ResourceLoader.Exists(tick))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>(tick).Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			// (in the clock's hood, by its dial)
			_clockTick = new AudioStreamPlayer3D { Name = "ClockTick", Stream = wav, Bus = "Events", VolumeDb = -15f, UnitSize = 2.5f, MaxDistance = 22f, Position = new Vector3(6.4f, FloorY + 1.8f, -6.45f) };
			AddChild(_clockTick);
		}
		var list = new System.Collections.Generic.List<AudioStream>();
		for (int i = 1; i <= 5; i++)
		{
			string p = $"res://assets/audio/sfx/glass_clink_{i:00}.wav";
			if (ResourceLoader.Exists(p)) list.Add(GD.Load<AudioStream>(p));
		}
		_clinks = list.ToArray();
		_barGlass = new AudioStreamPlayer3D { Name = "BarGlass", Bus = "Events", VolumeDb = -19f, UnitSize = 1.6f, MaxDistance = 18f };
		AddChild(_barGlass);
	}

	private void SoundsProcess(Vector3 local, bool inside)
	{
		if (_clockTick != null)
		{
			// (going while they're in the lodge, until it freezes; nothing to hear from the woods)
			bool going = inside && !Frozen;
			if (going && !_clockTick.Playing) _clockTick.Play();
			else if (!going && _clockTick.Playing) _clockTick.Stop();
		}
		if (_clinks.Length == 0 || Frozen || !inside || _clock < _nextClink) return;
		_nextClink = _clock + _rngSounds.RandfRange(9f, 22f);
		// only near the bar (they'd hear it from the lobby, faintly; never from upstairs)
		if (local.Y > UpperY - 1f || new Vector2(local.X - BarX0, local.Z - 4f).Length() > 16f) return;
		_barGlass.Position = new Vector3(BarX0 + 0.25f, _rngSounds.RandiRange(0, 2) * 0.5f + 1.55f, _rngSounds.RandfRange(1.8f, 6.4f));
		_barGlass.Stream = _clinks[_rngSounds.RandiRange(0, _clinks.Length - 1)];
		_barGlass.PitchScale = _rngSounds.RandfRange(0.94f, 1.06f);
		_barGlass.Play();
		BarClinks++;
	}

	private readonly RandomNumberGenerator _rngSounds = new() { Seed = 2026 };
}
