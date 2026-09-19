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

	private Node _source;
	private bool _useFade;
	private double _time;
	private readonly float[] _freq = new float[4];
	private readonly float[] _phase = new float[4];

	public override void _Ready()
	{
		_source = GetParent();
		_useFade = SilenceFade.Y > SilenceFade.X;
		var rng = new RandomNumberGenerator();
		for (int i = 0; i < 4; i++)
		{
			_freq[i] = Mathf.Tau / rng.RandfRange(DriftPeriod.X, DriftPeriod.Y);
			_phase[i] = rng.RandfRange(0f, Mathf.Tau);
		}

		// An empty stream slot reads as a null Object, not Nil, so test the object itself.
		if (_source.Get("stream").AsGodotObject() == null && StreamPath != "" && ResourceLoader.Exists(StreamPath))
			_source.Set("stream", GD.Load<AudioStream>(StreamPath));
		if (_source.Get("stream").AsGodotObject() == null) { GD.PushWarning($"AmbienceLoop: no stream for {_source.Name}"); return; }
		if (_source.Get("stream").AsGodotObject() is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			// In frames, from the duration: imported WAVs are compressed (QOA), so the byte size says nothing.
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_source.Set("stream", wav);
		}
		Apply();
		var length = (_source.Get("stream").AsGodotObject() as AudioStream)?.GetLength() ?? 0;
		_source.Call("play", (float)GD.RandRange(0.0, Mathf.Max(length - 0.1, 0.0)));
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
		_source.Set("volume_db", Mathf.Max(db, -80f));
		if (DriftPitch > 0f) _source.Set("pitch_scale", 1f + DriftPitch * Wander(2, 3));
	}
}
