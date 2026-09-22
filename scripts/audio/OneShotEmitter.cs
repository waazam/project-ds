using System.Collections.Generic;
using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// Scatters one-shot sounds in 3D around the listener: birds in the canopy,
/// crickets underfoot, critters in the leaf litter, creaking trunks. Each call
/// comes from a new spot and never repeats the previous sample.
///
/// If LifeCategory is set, calls get rarer as that category's gain drops and
/// stop entirely before its bus is fully faded, so the last one you hear is a
/// whole call, not one that faded out mid-song. Indoors (ForestAmbienceManager)
/// the forest's calls stop altogether: no birds in a bunker. ForestDirector drives
/// RateScale / DistanceScale (stillness, activity waves) and can Trigger()
/// calls directly (gust creaks).
/// </summary>
public partial class OneShotEmitter : Node3D
{
	public enum Life { None, Birds, Insects, Wind, Distant }

	[Export] public string SamplePattern = "res://assets/audio/sfx/bird_{0:00}.wav";
	[Export] public int SampleCount = 8;
	/// <summary>Sample numbers (1-based) to leave out of the rotation.</summary>
	[Export] public int[] SkipSamples = System.Array.Empty<int>();
	[Export] public string Bus = "Birds";
	[Export] public Life LifeCategory = Life.Birds;
	/// <summary>Seconds between calls. Both zero = only plays when triggered.</summary>
	[Export] public Vector2 IntervalSeconds = new(1.2f, 5.5f);
	[Export] public Vector2 DistanceRange = new(12f, 45f);
	[Export] public Vector2 HeightRange = new(4f, 14f);
	[Export] public float VolumeDb = -4f;
	[Export] public Vector2 PitchRange = new(0.94f, 1.06f);
	[Export] public float UnitSize = 18f;
	[Export] public int Voices = 5;

	/// <summary>Multiplies call frequency (set by ForestDirector).</summary>
	public float RateScale = 1f;
	/// <summary>Multiplies call distance (set by ForestDirector; &lt;1 = closer).</summary>
	public float DistanceScale = 1f;

	public int CallsPlayed { get; private set; }

	private readonly List<AudioStream> _samples = new();
	private readonly List<AudioStreamPlayer3D> _voices = new();
	private readonly RandomNumberGenerator _rng = new();
	private SamplePicker _picker;
	private Node3D _listener;
	private float _timer;
	private int _next;

	public override void _Ready()
	{
		for (int i = 1; i <= SampleCount; i++)
		{
			if (System.Array.IndexOf(SkipSamples, i) >= 0) continue;
			string path = string.Format(SamplePattern, i);
			if (ResourceLoader.Exists(path)) _samples.Add(GD.Load<AudioStream>(path));
		}
		for (int i = 0; i < Voices; i++)
		{
			var p = new AudioStreamPlayer3D
			{
				Bus = Bus,
				UnitSize = UnitSize,
				MaxDistance = 140f,
				AttenuationFilterCutoffHz = 9000f,
				AttenuationFilterDb = -12f,
				TopLevel = true,
			};
			AddChild(p);
			_voices.Add(p);
		}
		_timer = _rng.RandfRange(0.5f, Mathf.Max(IntervalSeconds.Y, 1f));
	}

	private float LifeGain()
	{
		var m = ForestAmbienceManager.Instance;
		if (m == null) return 1f;
		float life = LifeCategory switch
		{
			Life.Birds => m.CategoryGain(ForestAmbienceManager.Category.Birds),
			Life.Insects => m.CategoryGain(ForestAmbienceManager.Category.Insects),
			Life.Wind => m.CategoryGain(ForestAmbienceManager.Category.Wind),
			Life.Distant => m.CategoryGain(ForestAmbienceManager.Category.Distant),
			_ => 1f,
		};
		// Through walls the forest is a murmur, not a source of new calls around the listener.
		return life * (1f - m.Indoor);
	}

	public override void _Process(double delta)
	{
		if (_samples.Count == 0 || IntervalSeconds.Y <= 0f) return;
		float life = LifeGain();
		if (life < 0.08f) return;   // this part of the forest has stopped; don't even count down

		// Rarer as life drops: at half life, calls come roughly twice as far apart.
		_timer -= (float)delta * life * RateScale;
		if (_timer > 0f) return;
		_timer = _rng.RandfRange(IntervalSeconds.X, IntervalSeconds.Y);
		Trigger();
	}

	/// <summary>Play one call now from a fresh spot around the listener.</summary>
	public void Trigger(float extraDb = 0f)
	{
		if (_samples.Count == 0 || LifeGain() < 0.08f) return;
		_listener ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (_listener == null) return;

		var voice = _voices[_next];
		_next = (_next + 1) % _voices.Count;
		float angle = _rng.RandfRange(0f, Mathf.Tau);
		float dist = _rng.RandfRange(DistanceRange.X, DistanceRange.Y) * DistanceScale;
		voice.GlobalPosition = _listener.GlobalPosition +
			new Vector3(Mathf.Cos(angle) * dist, _rng.RandfRange(HeightRange.X, HeightRange.Y), Mathf.Sin(angle) * dist);
		voice.Stream = _samples[_picker.Next(_rng, _samples.Count)];
		voice.PitchScale = _rng.RandfRange(PitchRange.X, PitchRange.Y);
		voice.VolumeDb = VolumeDb + extraDb + _rng.RandfRange(-4f, 1f);
		voice.Play();
		CallsPlayed++;
	}
}
