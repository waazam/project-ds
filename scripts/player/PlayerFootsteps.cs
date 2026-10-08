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
	[Export] public float CrouchStride = 0.8f;   // short, careful steps
	[Export] public float CrouchQuietDb = -6f;
	[Export] public float StepVolumeDb = -26f;
	[Export] public float ClothVolumeDb = -32f;
	[Export] public int Voices = 4;

	private PlayerController _player;
	private readonly Dictionary<string, AudioStream[]> _sets = new();
	private AudioStream[] _cloth;
	private readonly List<AudioStreamPlayer> _voices = new();
	private int _nextVoice;
	private float _distance;
	/// <summary>0 at a footfall, rising to 1 at the next (the camera's bob dips on the footfall).</summary>
	public float StepPhase => Mathf.Clamp(_distance / Stride, 0f, 1f);
	private float Stride => _player == null ? WalkStride : _player.IsRunning ? RunStride : Mathf.Lerp(WalkStride, CrouchStride, _player.CrouchAmount);
	/// <summary>Steps taken (their parity is which foot: the head sways toward it).</summary>
	public int Steps { get; private set; }
	private string _lastSurface = "";
	private readonly RandomNumberGenerator _rng = new();
	private Audio.SamplePicker _stepPicker, _clothPicker;

	/// <summary>Fired on every player footstep (the stalker shadows these).</summary>
	[Signal] public delegate void SteppedEventHandler();

	public int StepsPlayed { get; private set; }
	public string LastSurface => _lastSurface;
	/// <summary>How deep the snow underfoot is, 0..1 (set by the winter woods: off the plowed road it's deep). Deep,
	/// a snow step is the muffled, dragging one.</summary>
	public float SnowDepth { get; set; }
	/// <summary>For tests: deep-snow steps played.</summary>
	public int DeepSteps { get; private set; }

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_sets["dirt"] = LoadSet("res://assets/audio/sfx/step_dirt_{0:00}.wav", 6);
		_sets["wood"] = LoadSet("res://assets/audio/sfx/step_wood_{0:00}.wav", 4);
		_sets["stone"] = LoadSet("res://assets/audio/sfx/step_stone_{0:00}.wav", 6);
		_sets["water"] = LoadSet("res://assets/audio/sfx/wade_{0:00}.wav", 4);
		_sets["metal"] = LoadSet("res://assets/audio/sfx/step_metal_{0:00}.wav", 6);
		_sets["gravel"] = LoadSet("res://assets/audio/sfx/step_gravel_{0:00}.wav", 6);
		_sets["rock"] = _sets["stone"];
		// the wet forest floor in the storm (2026-10-07): mud, sodden leaves, roots underfoot
		_sets["mud"] = LoadSet("res://assets/audio/sfx/step_mud_{0:00}.wav", 6);
		_sets["leaves"] = LoadSet("res://assets/audio/sfx/step_leaves_{0:00}.wav", 6);
		_sets["root"] = LoadSet("res://assets/audio/sfx/step_root_{0:00}.wav", 4);
		// Act 21 on: snow (soft, the owner: softer steps for the winter)
		_sets["snow"] = LoadSet("res://assets/audio/sfx/step_snow_{0:00}.wav", 6);
		// in deep snow: muffled, the leg dragged through (the detail pass)
		_sets["snow_deep"] = LoadSet("res://assets/audio/sfx/step_snow_deep_{0:00}.wav", 6);
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
		float stride = Stride;
		if (_distance < stride) return;
		_distance -= stride;
		Steps++;
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
		bool deep = _lastSurface == "snow" && SnowDepth > 0.45f && _sets["snow_deep"].Length > 0;
		if (deep) { set = _sets["snow_deep"]; DeepSteps++; }
		if (set.Length == 0) return;

		float loudness = Mathf.Remap(Mathf.Clamp(speed, 1f, 5f), 1f, 5f, -2f, 3f) + CrouchQuietDb * _player.CrouchAmount + (deep ? -1.5f : 0f);
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

	private static bool IsGround(Node n)
	{
		for (var p = n; p != null; p = p.GetParent())
			if (p.IsInGroup("terrain") || p.IsInGroup("lake_marker") || p is ProjectDS.World.ForestTerrain) return true;
		return false;
	}

	private string SurfaceUnderfoot()
	{
		var space = _player.GetWorld3D().DirectSpaceState;
		var from = _player.GlobalPosition + Vector3.Up * 0.3f;
		var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 1.0f, 1);
		query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var hit = space.IntersectRay(query);
		if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node n)
		{
			if (n.HasMeta("surface"))
			{
				string s = n.GetMeta("surface").AsString();
				if (!_sets.ContainsKey(s)) Audio.AudioDirector.Instance?.NoteSilentSurface($"{n.Name}: '{s}'");
				return s;
			}
			// the forest floor is dirt; anything else without a surface of its own is a gap in the sweep
			if (!IsGround(n)) Audio.AudioDirector.Instance?.NoteSilentSurface(n.GetPath().ToString());
			else return ForestFloorAt(_player.GlobalPosition);
		}
		return "dirt";
	}

	private static FastNoiseLite _patch, _roots;

	/// <summary>What the forest floor is at a point: in the rain, patches of mud and of sodden leaves, a root here and there
	/// (fixed by place, so the same ground always sounds the same); dry, it's dirt. Shared with what follows them through
	/// the woods, so its steps answer theirs in kind.</summary>
	public static string ForestFloorAt(Vector3 p)
	{
		if ((ProjectDS.World.Weather.Instance?.Rain ?? 0f) < 0.15f) return "dirt";
		_patch ??= new FastNoiseLite { Frequency = 0.09f, Seed = 717 };
		_roots ??= new FastNoiseLite { Frequency = 0.8f, Seed = 718 };
		if (_roots.GetNoise2D(p.X, p.Z) > 0.5f) return "root";
		return _patch.GetNoise2D(p.X, p.Z) > 0.1f ? "leaves" : "mud";
	}
}
