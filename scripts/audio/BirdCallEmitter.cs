using System.Collections.Generic;
using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// Scatters one-shot bird calls in 3D around the listener, up in the canopy.
/// Calls get rarer as the forest's Birds gain drops and stop entirely before
/// the Birds bus is fully faded, so the last call you hear is one that
/// happened, not one that faded out mid-song.
/// </summary>
public partial class BirdCallEmitter : Node3D
{
	[Export] public string SamplePattern = "res://assets/audio/sfx/bird_{0:00}.wav";
	[Export] public int SampleCount = 8;
	[Export] public string Bus = "Birds";
	[Export] public Vector2 IntervalSeconds = new(1.2f, 5.5f);
	[Export] public Vector2 DistanceRange = new(12f, 45f);
	[Export] public Vector2 HeightRange = new(4f, 14f);
	[Export] public float VolumeDb = -4f;
	[Export] public int Voices = 5;

	public int CallsPlayed { get; private set; }

	private readonly List<AudioStream> _samples = new();
	private readonly List<AudioStreamPlayer3D> _voices = new();
	private readonly RandomNumberGenerator _rng = new();
	private Node3D _listener;
	private float _timer = 1.5f;
	private int _next;

	public override void _Ready()
	{
		for (int i = 1; i <= SampleCount; i++)
		{
			string path = string.Format(SamplePattern, i);
			if (ResourceLoader.Exists(path)) _samples.Add(GD.Load<AudioStream>(path));
		}
		for (int i = 0; i < Voices; i++)
		{
			var p = new AudioStreamPlayer3D
			{
				Bus = Bus,
				UnitSize = 18f,
				MaxDistance = 120f,
				AttenuationFilterCutoffHz = 9000f,
				AttenuationFilterDb = -12f,
				TopLevel = true,
			};
			AddChild(p);
			_voices.Add(p);
		}
	}

	public override void _Process(double delta)
	{
		if (_samples.Count == 0) return;
		_listener ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (_listener == null) return;

		float life = ForestAmbienceManager.Instance?.CategoryGain(ForestAmbienceManager.Category.Birds) ?? 1f;
		if (life < 0.08f) return;   // birds have stopped; don't even count down

		// Rarer as life drops: at half life, calls come roughly twice as far apart.
		_timer -= (float)delta * life;
		if (_timer > 0f) return;
		_timer = _rng.RandfRange(IntervalSeconds.X, IntervalSeconds.Y);

		var voice = _voices[_next];
		_next = (_next + 1) % _voices.Count;
		float angle = _rng.RandfRange(0f, Mathf.Tau);
		float dist = _rng.RandfRange(DistanceRange.X, DistanceRange.Y);
		voice.GlobalPosition = _listener.GlobalPosition +
			new Vector3(Mathf.Cos(angle) * dist, _rng.RandfRange(HeightRange.X, HeightRange.Y), Mathf.Sin(angle) * dist);
		voice.Stream = _samples[_rng.RandiRange(0, _samples.Count - 1)];
		voice.PitchScale = _rng.RandfRange(0.94f, 1.06f);
		voice.VolumeDb = VolumeDb + _rng.RandfRange(-4f, 1f);
		voice.Play();
		CallsPlayed++;
	}
}
