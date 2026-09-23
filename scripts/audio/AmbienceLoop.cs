using System.Collections.Generic;
using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// A looping bed on an AudioStreamPlayer or AudioStreamPlayer3D. It forces WAV
/// looping (so imports don't need hand-edited loop points) and starts at a
/// random offset so stacked beds never phase together.
///
/// If SilenceFade is set (y &gt; x), the player's own volume also follows the
/// forest silence: a positive direction fades in with silence (unnatural
/// layers), and a negative one fades out.
///
/// Drift keeps a bed from sounding static: volume (and optionally pitch)
/// wander slowly on their own random curve, so no two minutes sound the same.
/// ForestDirector adds its own push on top through Gain and ExtraDb.
///
/// The parent player is resolved once to its concrete type and written only
/// when the value actually moves (some thirty of these run every frame).
/// </summary>
public partial class AmbienceLoop : Node
{
	/// <summary>Loaded into the parent player if it has no stream set.</summary>
	[Export] public string StreamPath = "";
	[Export] public float BaseVolumeDb = 0f;
	/// <summary>Silence window (x..y). Leave at (0,0) to ignore silence.</summary>
	[Export] public Vector2 SilenceFade = Vector2.Zero;
	/// <summary>True: silent in a living forest and audible in silence.</summary>
	[Export] public bool RisesWithSilence = true;

	[ExportGroup("Drift")]
	/// <summary>Peak volume wander either side of BaseVolumeDb.</summary>
	[Export] public float DriftDb = 0f;
	/// <summary>Peak pitch wander (0.02 = ±2%).</summary>
	[Export] public float DriftPitch = 0f;
	/// <summary>Seconds per wander cycle (random within the range per component).</summary>
	[Export] public Vector2 DriftPeriod = new(20f, 60f);

	/// <summary>Linear multiplier driven by ForestDirector (0 = silent). Set the starting value here.</summary>
	[Export] public float Gain = 1f;
	/// <summary>dB offset set by ForestDirector (gusts, activity).</summary>
	public float ExtraDb = 0f;

	private const float WriteThresholdDb = 0.05f;
	private const float WriteThresholdPitch = 0.0005f;

	private AudioStreamPlayer _player2D;
	private AudioStreamPlayer3D _player3D;
	private bool _useFade;
	private double _time;
	private readonly float[] _freq = new float[4];
	private readonly float[] _phase = new float[4];
	private float _lastDb = float.NaN, _lastPitch = float.NaN;

	private AudioStream Stream
	{
		get => _player2D != null ? _player2D.Stream : _player3D?.Stream;
		set { if (_player2D != null) _player2D.Stream = value; else if (_player3D != null) _player3D.Stream = value; }
	}

	public override void _Ready()
	{
		_player2D = GetParent() as AudioStreamPlayer;
		_player3D = GetParent() as AudioStreamPlayer3D;
		if (_player2D == null && _player3D == null) { GD.PushWarning($"AmbienceLoop: parent of {Name} is not an audio player"); return; }
		_useFade = SilenceFade.Y > SilenceFade.X;
		var rng = new RandomNumberGenerator();
		for (int i = 0; i < 4; i++)
		{
			_freq[i] = Mathf.Tau / rng.RandfRange(DriftPeriod.X, DriftPeriod.Y);
			_phase[i] = rng.RandfRange(0f, Mathf.Tau);
		}

		if (Stream == null && StreamPath != "" && ResourceLoader.Exists(StreamPath))
			Stream = GD.Load<AudioStream>(StreamPath);
		if (Stream == null) { GD.PushWarning($"AmbienceLoop: no stream for {GetParent().Name}"); return; }
		if (Stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			// In frames, from the duration: imported WAVs are compressed (QOA), so the byte size says nothing.
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			Stream = wav;
		}
		Apply();
		double length = Stream.GetLength();
		float from = (float)GD.RandRange(0.0, Mathf.Max(length - 0.1, 0.0));
		if (_player2D != null) _player2D.Play(from); else _player3D.Play(from);
	}

	/// <summary>Every take named {prefix}_01.wav, _02.wav, ... until one is missing (so new takes are picked up without a list).</summary>
	public static List<AudioStream> LoadTakes(string prefix)
	{
		var list = new List<AudioStream>();
		for (int i = 1; i < 100; i++)
		{
			string p = $"{prefix}_{i:00}.wav";
			if (!ResourceLoader.Exists(p)) break;
			list.Add(GD.Load<AudioStream>(p));
		}
		return list;
	}

	/// <summary>Switch to another loop take (prepared to loop, started at a random point), e.g. between bursts so the beat never repeats.</summary>
	public void SwapStream(AudioStream stream)
	{
		if (stream == null || (_player2D == null && _player3D == null)) return;
		if (stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			stream = wav;
		}
		Stream = stream;
		float from = (float)GD.RandRange(0.0, Mathf.Max(stream.GetLength() - 0.1, 0.0));
		if (_player2D != null) _player2D.Play(from); else _player3D.Play(from);
	}

	public override void _Process(double delta)
	{
		_time += delta;
		Apply();
	}

	/// <summary>Two incommensurate sines per component: smooth, and it never visibly repeats.</summary>
	private float Wander(int a, int b) =>
		0.6f * Mathf.Sin((float)_time * _freq[a] + _phase[a]) + 0.4f * Mathf.Sin((float)_time * _freq[b] * 1.37f + _phase[b]);

	private void Apply()
	{
		float db = BaseVolumeDb + ExtraDb + DriftDb * Wander(0, 1);
		if (_useFade)
		{
			float s = ForestAmbienceManager.Instance?.Silence ?? 0f;
			float t = Mathf.SmoothStep(SilenceFade.X, SilenceFade.Y, s);
			if (!RisesWithSilence) t = 1f - t;
			db += Mathf.LinearToDb(Mathf.Max(t, 0.0001f));
		}
		db += Mathf.LinearToDb(Mathf.Max(Gain, 0.0001f));
		db = Mathf.Max(db, -80f);
		if (float.IsNaN(_lastDb) || Mathf.Abs(db - _lastDb) > WriteThresholdDb)
		{
			_lastDb = db;
			if (_player2D != null) _player2D.VolumeDb = db; else _player3D.VolumeDb = db;
		}
		if (DriftPitch <= 0f) return;
		float pitch = 1f + DriftPitch * Wander(2, 3);
		if (!float.IsNaN(_lastPitch) && Mathf.Abs(pitch - _lastPitch) <= WriteThresholdPitch) return;
		_lastPitch = pitch;
		if (_player2D != null) _player2D.PitchScale = pitch; else _player3D.PitchScale = pitch;
	}
}
