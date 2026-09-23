using System.Collections.Generic;
using Godot;

namespace ProjectDS.Player;

/// <summary>
/// Plays a footstep every stride, with the sample set chosen from the floor's
/// "surface" metadata (defaults to dirt). Adds a quiet clothing rustle on some
/// steps. Everything goes to the Player bus. Sounds are loaded by naming
/// convention, so real recordings can drop in over the placeholders.
/// </summary>
public partial class PlayerFootsteps : Node
{
	[Export] public float WalkStride = 1.3f;   // metres per step while walking
	[Export] public float RunStride = 1.9f;
	[Export] public float StepVolumeDb = -26f;
	[Export] public float ClothVolumeDb = -32f;
	[Export] public int Voices = 4;

	private PlayerController _player;
	private readonly Dictionary<string, AudioStream[]> _sets = new();
	private AudioStream[] _cloth;
	private readonly List<AudioStreamPlayer> _voices = new();
	private int _nextVoice;
	private float _distance;
	private string _lastSurface = "";
	private readonly RandomNumberGenerator _rng = new();
	private Audio.SamplePicker _stepPicker, _clothPicker;

	/// <summary>Fired on every player footstep (the stalker shadows these).</summary>
	[Signal] public delegate void SteppedEventHandler();

	public int StepsPlayed { get; private set; }
	public string LastSurface => _lastSurface;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_sets["dirt"] = LoadSet("res://assets/audio/sfx/step_dirt_{0:00}.wav", 6);
		_sets["wood"] = LoadSet("res://assets/audio/sfx/step_wood_{0:00}.wav", 4);
		_sets["stone"] = LoadSet("res://assets/audio/sfx/step_stone_{0:00}.wav", 6);
		_cloth = LoadSet("res://assets/audio/sfx/cloth_{0:00}.wav", 4);
		for (int i = 0; i < Voices; i++)
		{
			var p = new AudioStreamPlayer { Bus = "Player" };
			AddChild(p);
			_voices.Add(p);
		}
	}

	private static AudioStream[] LoadSet(string pattern, int count)
	{
		var list = new List<AudioStream>();
		for (int i = 1; i <= count; i++)
		{
			string path = string.Format(pattern, i);
			if (ResourceLoader.Exists(path)) list.Add(GD.Load<AudioStream>(path));
		}
		return list.ToArray();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_player.IsOnFloor()) return;
		float speed = _player.GroundSpeed;
		if (speed < 0.3f) { _distance = Mathf.Min(_distance, 0.3f); return; }

		_distance += speed * (float)delta;
		float stride = _player.IsRunning ? RunStride : WalkStride;
		if (_distance < stride) return;
		_distance -= stride;
		PlayStep(speed);
	}

	/// <summary>A footfall on demand, on a named surface, for scripted walks (the stairs pulling the
	/// player up step by step) while the body's own physics is off. <paramref name="loudness"/> in dB.</summary>
	public void StepNow(string surface, float loudness = 0f)
	{
		_lastSurface = surface;
		if (!_sets.TryGetValue(surface, out var set) || set.Length == 0) set = _sets["dirt"];
		if (set.Length == 0) return;
		Play(set[_stepPicker.Next(_rng, set.Length)], StepVolumeDb + loudness, _rng.RandfRange(0.9f, 1.04f));
		if (_cloth.Length > 0 && _rng.Randf() < 0.45f)
			Play(_cloth[_clothPicker.Next(_rng, _cloth.Length)], ClothVolumeDb + loudness, _rng.RandfRange(0.9f, 1.1f));
		StepsPlayed++;
		EmitSignal(SignalName.Stepped);
	}

	private void PlayStep(float speed)
	{
		_lastSurface = SurfaceUnderfoot();
		if (!_sets.TryGetValue(_lastSurface, out var set) || set.Length == 0) set = _sets["dirt"];
		if (set.Length == 0) return;

		float loudness = Mathf.Remap(Mathf.Clamp(speed, 1f, 5f), 1f, 5f, -2f, 3f);
		Play(set[_stepPicker.Next(_rng, set.Length)], StepVolumeDb + loudness, _rng.RandfRange(0.92f, 1.08f));
		if (_cloth.Length > 0 && _rng.Randf() < 0.45f)
			Play(_cloth[_clothPicker.Next(_rng, _cloth.Length)], ClothVolumeDb + loudness, _rng.RandfRange(0.9f, 1.1f));
		StepsPlayed++;
		EmitSignal(SignalName.Stepped);
	}

	private void Play(AudioStream stream, float db, float pitch)
	{
		var p = _voices[_nextVoice];
		_nextVoice = (_nextVoice + 1) % _voices.Count;
		p.Stream = stream;
		p.VolumeDb = db;
		p.PitchScale = pitch;
		p.Play();
	}

	private string SurfaceUnderfoot()
	{
		var space = _player.GetWorld3D().DirectSpaceState;
		var from = _player.GlobalPosition + Vector3.Up * 0.3f;
		var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 1.0f, 1);
		query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var hit = space.IntersectRay(query);
		if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node n && n.HasMeta("surface"))
			return n.GetMeta("surface").AsString();
		return "dirt";
	}
}
