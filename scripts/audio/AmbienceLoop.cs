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

	private Node _source;
	private bool _useFade;

	public override void _Ready()
	{
		_source = GetParent();
		_useFade = SilenceFade.Y > SilenceFade.X;
		if (_source.Get("stream").VariantType == Variant.Type.Nil && StreamPath != "" && ResourceLoader.Exists(StreamPath))
			_source.Set("stream", GD.Load<AudioStream>(StreamPath));
		if (_source.Get("stream").VariantType == Variant.Type.Nil) { GD.PushWarning($"AmbienceLoop: no stream for {_source.Name}"); return; }
		if (_source.Get("stream").AsGodotObject() is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			wav.LoopEnd = wav.Data.Length / (wav.Format == AudioStreamWav.FormatEnum.Format16Bits ? 2 : 1)
				/ (wav.Stereo ? 2 : 1);
			_source.Set("stream", wav);
		}
		ApplyVolume();
		var length = (_source.Get("stream").AsGodotObject() as AudioStream)?.GetLength() ?? 0;
		_source.Call("play", (float)GD.RandRange(0.0, Mathf.Max(length - 0.1, 0.0)));
	}

	public override void _Process(double delta) => ApplyVolume();

	private void ApplyVolume()
	{
		float db = BaseVolumeDb;
		if (_useFade)
		{
			float s = ForestAmbienceManager.Instance?.Silence ?? 0f;
			float t = Mathf.SmoothStep(SilenceFade.X, SilenceFade.Y, s);
			if (!RisesWithSilence) t = 1f - t;
			db += Mathf.LinearToDb(Mathf.Max(t, 0.0001f));
		}
		_source.Set("volume_db", db);
	}
}
