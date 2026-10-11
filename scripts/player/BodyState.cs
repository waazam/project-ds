using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Player;

/// <summary>
/// The body tells the player how it is (2026-10-11, the owner's list: "Show health and fear on the player, not on a bar ...
/// When stamina is low, add heavier breathing, a slight sway and narrower vision. After a near miss, add a heartbeat,
/// shaking hands on the lantern and a brief tunnel-vision pulse").
/// <list type="bullet">
/// <item><b>Spent</b> (stamina low, breath short): the view closes in at its edges, it sways a little with the breath
/// (a frustum offset: the view drifts, it never rolls), the hands shake;</item>
/// <item><b>Afraid</b> (a scare: <see cref="PlayerBreathing.Startle"/>): the heart heard, pounding, its beat pulsing the
/// edges of the view in and out, for a few seconds, settling.</item>
/// </list>
/// All of it eased and gentle (the owner's comfort): no flash, no jolt. The vignette is the finish's (PostGuard eases
/// toward the resting vignette plus <see cref="VignetteExtra"/>).
/// </summary>
public partial class BodyState : Node
{
	public static float VignetteExtra { get; private set; }
	/// <summary>0 calm .. 1 terrified (for tests and others).</summary>
	public static float Fear { get; private set; }
	private PlayerController _p;
	private AudioStreamPlayer _heart;
	private float _lastTremble, _sway;
	private double _clock;

	public override void _Ready()
	{
		_p = GetParent<PlayerController>();
		const string path = "res://assets/audio/ambient/heartbeat_loop.wav";
		if (ResourceLoader.Exists(path))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>(path).Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
			_heart = new AudioStreamPlayer { Name = "Heart", Stream = wav, Bus = "Player", VolumeDb = -80f };
			AddChild(_heart);
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_clock += delta;
		// fear rises with each fright (the tremble's own spikes), and ebbs over about six seconds
		float tr = PlayerBreathing.Tremble;
		if (tr > _lastTremble + 0.05f) Fear = Mathf.Max(Fear, tr);
		_lastTremble = tr;
		Fear = Mathf.Max(0f, Fear - dt / 6f);
		// spent: the stamina low, the breath short
		var st = _p.GetNodeOrNull<PlayerStamina>("Stamina");
		float breath = PlayerBreathing.Of(_p)?.Exertion ?? 0f;
		float spent = Mathf.Clamp(Mathf.Max(st != null ? (0.4f - st.Value) / 0.4f : 0f, (breath - 0.55f) / 0.45f), 0f, 1f);
		// the heart: heard as fear rises, its beat about 1.7 a second
		if (_heart != null)
		{
			float want = Fear > 0.05f ? Mathf.Lerp(-26f, -6f, Fear) : -80f;
			if (want > -79f && !_heart.Playing) _heart.Play();
			_heart.VolumeDb = Mathf.MoveToward(_heart.VolumeDb, want, dt * 30f);
			_heart.PitchScale = Mathf.Lerp(1f, 1.25f, Fear);
			if (_heart.VolumeDb < -79f && _heart.Playing) _heart.Stop();
		}
		float pulse = Fear * (0.5f + 0.5f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin((float)_clock * Mathf.Tau * 1.7f * Mathf.Lerp(1f, 1.25f, Fear))), 6f));
		VignetteExtra = 0.2f * spent + 0.32f * pulse;
		// the sway: the view drifting a hair with the laboured breath (no roll)
		var cam = _p.CameraRig?.Camera;
		if (cam != null)
		{
			bool comfort = !(GameSettings.Instance?.HeadMotion ?? true);
			_sway = Mathf.MoveToward(_sway, comfort ? 0f : spent, dt * 0.8f);
			float t = (float)_clock;
			cam.HOffset = Mathf.Sin(t * 0.9f) * 0.012f * _sway;
			cam.VOffset = Mathf.Sin(t * 1.8f) * 0.01f * _sway;
		}
		// the hands shake when spent too
		if (spent > 0.3f) PlayerBreathing.AddTremble(0.25f * spent * dt);
	}
}
