using Godot;
using ProjectDS.Audio;

namespace ProjectDS.Player;

/// <summary>
/// The player's own breath. You only notice it when the world goes quiet, or
/// after running. Silent in a living forest, it becomes audible as
/// ForestAmbienceManager.Silence rises and quickens with exertion.
/// </summary>
public partial class PlayerBreathing : AudioStreamPlayer
{
	[Export] public string SamplePath = "res://assets/audio/ambient/breath_loop.wav";
	[Export] public float MaxVolumeDb = -10f;
	[Export] public Vector2 SilenceFade = new(0.45f, 1f);
	[Export] public float ExertionRise = 0.25f;   // per second while running
	[Export] public float ExertionFall = 0.08f;

	private PlayerController _player;
	private float _exertion;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		Bus = "Player";
		if (Stream == null && ResourceLoader.Exists(SamplePath)) Stream = GD.Load<AudioStream>(SamplePath);
		if (Stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = wav.Data.Length / 2;
			Stream = wav;
		}
		VolumeDb = -80f;
		if (Stream != null) Play();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_exertion = _player.IsRunning
			? Mathf.Min(1f, _exertion + ExertionRise * dt)
			: Mathf.Max(0f, _exertion - ExertionFall * dt);

		float silence = ForestAmbienceManager.Instance?.Silence ?? 0f;
		float audible = Mathf.Max(Mathf.SmoothStep(SilenceFade.X, SilenceFade.Y, silence), _exertion * 0.7f);
		VolumeDb = Mathf.LinearToDb(Mathf.Max(audible, 0.0001f)) + MaxVolumeDb;
		PitchScale = 1f + _exertion * 0.35f;
	}
}
