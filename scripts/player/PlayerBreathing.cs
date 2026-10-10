using System.Collections.Generic;
using Godot;

namespace ProjectDS.Player;

/// <summary>
/// The player's breath, built one breath at a time so it never loops. Exertion
/// rises while running and slowly settles after. It sets how fast the breaths
/// come, how hard they are (which set of samples, resting to winded) and how
/// loud they are. At rest or walking calmly you don't hear it at all. Each
/// breath gets its own timing, pitch and level jitter, and the same sample
/// never plays twice in a row.
///
/// (2026-10-10) Heard again, softly: only once you've really run (it was muted while it was reworked), and quieter.
/// And now:
/// <list type="bullet">
/// <item><b>Holding the breath</b> (V / up on the pad): silence. The world dims a touch at the edges and the heart is
/// heard; it can be held for up to nine seconds (less the more winded you are), and then it's let go in a gasp for air,
/// which carries (<see cref="Gasped"/>). Let go sooner, it's a quiet breath out. In the snow maze's hollows that's how
/// you keep it from hearing you.</item>
/// <item><b>Startled</b> (<see cref="Startle"/>): the breath catches and comes quick and shallow a while, and the hand
/// holding the light shakes, gently, settling over a few seconds (<see cref="Tremble"/>).</item>
/// </list>
/// </summary>
public partial class PlayerBreathing : Node
{
	/// <summary>Muted for now (Dan, 2026-09-18) while the breath sound is reworked.</summary>
	[Export] public bool Muted = false;
	[Export] public float MaxVolumeDb = -24f;
	[Export] public float ExertionRise = 0.18f;   // per second while running
	[Export] public float ExertionFall = 0.06f;   // per second otherwise
	/// <summary>Breaths per minute at rest .. fully winded.</summary>
	[Export] public Vector2 BreathsPerMinute = new(14f, 38f);
	/// <summary>Below this exertion the breath is inaudible.</summary>
	[Export] public float AudibleFrom = 0.35f;

	/// <summary>Holding the breath now.</summary>
	public bool Holding { get; private set; }
	/// <summary>Gasping for air (held too long): for a moment, loud.</summary>
	public bool Gasped => Time.GetTicksMsec() / 1000.0 - _gaspAt < 0.9;
	public float HeldFor { get; private set; }
	public float MaxHold => Mathf.Lerp(9f, 4f, Exertion);
	public int Holds { get; private set; }
	/// <summary>How much the hand shakes (0..1), after a fright.</summary>
	public static float Tremble { get; private set; }
	private static PlayerBreathing _instance;
	private double _gaspAt = -10;
	private float _recover;
	private UI.BreathHoldOverlay _overlay;

	public static PlayerBreathing Of(PlayerController p) => p?.GetNodeOrNull<PlayerBreathing>("Breathing");

	/// <summary>A fright: the breath catches, the hand shakes a while (gently).</summary>
	public static void Startle(float amount = 1f)
	{
		Tremble = Mathf.Max(Tremble, Mathf.Clamp(amount, 0f, 1f));
		if (_instance != null && IsInstanceValid(_instance)) _instance.Exertion = Mathf.Max(_instance.Exertion, 0.45f * amount);
	}

	public override void _EnterTree() => _instance = this;
	public override void _ExitTree() { if (_instance == this) _instance = null; }

	public float Exertion { get; private set; }
	public float CurrentVolumeDb => _voice?.VolumeDb ?? -80f;

	private PlayerController _player;
	private readonly List<AudioStream> _in = new(), _out = new();
	private readonly RandomNumberGenerator _rng = new();
	private AudioStreamPlayer _voice;
	private Audio.SamplePicker _inPick, _outPick;
	private float _timer = 1f;
	private bool _nextIsIn = true;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		for (int i = 1; i <= 5; i++)
		{
			Load($"res://assets/audio/sfx/breath_in_{i:00}.wav", _in);
			Load($"res://assets/audio/sfx/breath_out_{i:00}.wav", _out);
		}
		_voice = new AudioStreamPlayer { Bus = "Player", VolumeDb = -80f };
		AddChild(_voice);
	}

	private static void Load(string path, List<AudioStream> into)
	{
		if (ResourceLoader.Exists(path)) into.Add(GD.Load<AudioStream>(path));
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		Exertion = _player.IsRunning
			? Mathf.Min(1f, Exertion + ExertionRise * dt)
			: Mathf.Max(0f, Exertion - ExertionFall * dt * (Holding ? 0.3f : 1f));
		Tremble = Mathf.Max(0f, Tremble - dt / 6f);
		HoldBreath(dt);
		if (Holding) return;

		_timer -= dt;
		if (_timer > 0f) return;

		// One breath cycle at this effort; the inhale takes ~40% of it.
		float bpm = Mathf.Lerp(BreathsPerMinute.X, BreathsPerMinute.Y, Exertion) * _rng.RandfRange(0.88f, 1.12f);
		float cycle = 60f / bpm;
		_timer = (_nextIsIn ? 0.42f : 0.58f) * cycle;

		if (!Muted && Exertion >= AudibleFrom) PlayBreath(_nextIsIn);
		_nextIsIn = !_nextIsIn;
	}

	private void PlayBreath(bool inhale)
	{
		var set = inhale ? _in : _out;
		if (set.Count == 0) return;
		// Harder breaths come from the rougher end of the set, with some wander.
		int idx = Mathf.Clamp(Mathf.RoundToInt(Exertion * (set.Count - 1)) + _rng.RandiRange(-1, 1), 0, set.Count - 1);
		if (_rng.Randf() < 0.3f) idx = inhale ? _inPick.Next(_rng, set.Count) : _outPick.Next(_rng, set.Count);

		float audible = Mathf.SmoothStep(AudibleFrom, 1f, Exertion);
		_voice.Stream = set[idx];
		_voice.VolumeDb = MaxVolumeDb + Mathf.LinearToDb(Mathf.Max(audible, 0.0001f)) + _rng.RandfRange(-2f, 1f);
		_voice.PitchScale = _rng.RandfRange(0.94f, 1.06f);
		_voice.Play();
	}

	private void HoldBreath(float dt)
	{
		_recover = Mathf.Max(0f, _recover - dt);
		bool want = _player.PlayerInput is { BreathHeld: true } && _recover <= 0f && !Systems.PlayerDeath.Dying;
		if (want && !Holding)
		{
			Holding = true;
			HeldFor = 0f;
			Holds++;
			Play(_in, -20f, 0.95f);
		}
		if (Holding)
		{
			HeldFor += dt;
			bool tooLong = HeldFor >= MaxHold;
			if (!want || tooLong)
			{
				Holding = false;
				if (tooLong)
				{
					// can't any more: a gasp, loud, and a while before it can be held again
					_gaspAt = Time.GetTicksMsec() / 1000.0;
					_recover = 3f;
					Exertion = Mathf.Max(Exertion, 0.6f);
					Play(_in, -8f, 1.08f);
					GD.Print("[breath] held too long - a gasp");
				}
				else Play(_out, -24f, 0.95f);
				_timer = 1.2f;
			}
		}
		if (_overlay == null && IsInsideTree())
		{
			_overlay = new UI.BreathHoldOverlay { Name = "BreathHold" };
			AddChild(_overlay);
		}
		if (_overlay != null) _overlay.Amount = Holding ? Mathf.Clamp(0.25f + 0.75f * HeldFor / MaxHold, 0f, 1f) : 0f;
	}

	private void Play(List<AudioStream> set, float db, float pitch)
	{
		if (set.Count == 0) return;
		_voice.Stream = set[_rng.RandiRange(0, set.Count - 1)];
		_voice.VolumeDb = db;
		_voice.PitchScale = pitch;
		_voice.Play();
	}
}
