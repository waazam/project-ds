using System.Collections.Generic;
using Godot;

namespace ProjectDS.Player;

/// <summary>
/// The player's breath, one breath at a time so it never loops (2026-10-15 rework, the owner: "do some research on
/// breathing while walking and running and scared from other horror games and fix our breathe").
///
/// What the research gave: Outlast's breath is the player's fear gauge (it quickens at what he sees and while he hides);
/// The Last of Us Part II drives its breathing from one heart-rate value fed by sprinting, damage and nearby enemies,
/// choosing among sets of takes by it; INSIDE locks the boy's breaths to his footsteps; and runners breathe locked to
/// their stride, about two or three steps to each half of a breath. So:
/// <list type="bullet">
/// <item><b>Calm / walking</b>: slow, even breaths through the nose (<c>pbreath_nose_*</c>), just at the edge of hearing:
/// the body's there, nothing more. A little quicker walking than standing.</item>
/// <item><b>Running</b>: through the mouth (<c>pbreath_mouth_*</c>), each half of a breath landing on a footfall, three
/// steps to a half at first, two once it's hard; the harder the run, the rougher takes (<c>pbreath_winded_*</c>).</item>
/// <item><b>After a run</b>: winded, then easing: deep quick breaths that slow and soften over ~15 s back to the nose.</item>
/// <item><b>Afraid</b> (a fright, or the thing close): shallow, quick, uneven and shaking (<c>pbreath_scared_*</c>), with
/// the odd stillness when standing (holding it to listen) and a shaky breath out after. A fright's first moment is a
/// gasp (<c>pbreath_gasp_*</c>).</item>
/// </list>
/// Every set has ten takes per side, picked so none comes round again soon, with each breath's own timing, pitch and
/// level wander. Two voices alternate so one breath's tail can run under the next.
///
/// Still here: <b>holding the breath</b> (V / up on the pad): silence; up to nine seconds (less winded), then a gasp that
/// carries (<see cref="Gasped"/>). And <b>startled</b> (<see cref="Startle"/>): the hand holding the light shakes a while
/// (<see cref="Tremble"/>).
/// </summary>
public partial class PlayerBreathing : Node
{
	[Export] public bool Muted = false;
	/// <summary>The hardest breath's level (winded, right after a long run).</summary>
	[Export] public float MaxVolumeDb = -27.5f;   // (2026-10-15b, the owner: "quieter and deeper": every mode ~5.5 dB down, pitched lower)
	[Export] public float ExertionRise = 0.12f;   // per second while running
	[Export] public float ExertionFall = 0.06f;   // per second otherwise (walking a little slower)

	/// <summary>Holding the breath now.</summary>
	public bool Holding { get; private set; }
	/// <summary>Gasping for air (held too long): for a moment, loud.</summary>
	public bool Gasped => Time.GetTicksMsec() / 1000.0 - _gaspAt < 0.9;
	public float HeldFor { get; private set; }
	public float MaxHold => Mathf.Lerp(9f, 4f, Exertion);
	public int Holds { get; private set; }
	/// <summary>How much the hand shakes (0..1), after a fright.</summary>
	public static float Tremble { get; private set; }
	/// <summary>0 calm .. 1 terrified: frights, and the thing near.</summary>
	public float Fear { get; private set; }
	public float Exertion { get; private set; }
	public float CurrentVolumeDb => _last?.VolumeDb ?? -80f;
	/// <summary>Which set the last breath came from (for tests and the log).</summary>
	public string Mode { get; private set; } = "nose";

	private static PlayerBreathing _instance;
	private double _gaspAt = -10;
	private float _recover;
	private UI.BreathHoldOverlay _overlay;

	/// <summary>A little more shake (the body spent: BodyState), never past a spent body's own.</summary>
	public static void AddTremble(float amount) => Tremble = Mathf.Min(Mathf.Max(Tremble, 0.35f), Tremble + amount);

	public static PlayerBreathing Of(PlayerController p) => p?.GetNodeOrNull<PlayerBreathing>("Breathing");

	/// <summary>A fright: a gasp, then the breath comes quick and shallow a while, and the hand shakes (gently).</summary>
	public static void Startle(float amount = 1f)
	{
		amount = Mathf.Clamp(amount, 0f, 1f);
		Tremble = Mathf.Max(Tremble, amount);
		if (_instance == null || !IsInstanceValid(_instance)) return;
		_instance.Fear = Mathf.Max(_instance.Fear, amount);
		_instance.Gasp(amount);
	}

	public override void _EnterTree() => _instance = this;
	public override void _ExitTree() { if (_instance == this) _instance = null; }

	private sealed class Set
	{
		public readonly List<AudioStream> In = new(), Out = new();
		public Audio.RecentPicker InPick, OutPick;
	}

	private PlayerController _player;
	private PlayerStamina _stamina;
	private readonly Dictionary<string, Set> _sets = new();
	private readonly List<AudioStream> _gasps = new();
	private Audio.RecentPicker _gaspPick;
	private readonly RandomNumberGenerator _rng = new();
	private AudioStreamPlayer _a, _b, _last;
	private float _timer = 1f;
	private bool _nextIsIn = true;
	private int _steps, _stepsPerHalf = 3;
	private double _lastStepAt = -10, _lastGaspAt = -10;
	private float _still;   // (afraid and standing: holding it to listen, for this long)

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_stamina = _player.GetNodeOrNull<PlayerStamina>("Stamina");
		foreach (var tag in new[] { "nose", "mouth", "winded", "scared" })
		{
			var s = new Set();
			for (int i = 1; i <= 10; i++)
			{
				Load($"res://assets/audio/sfx/pbreath_{tag}_in_{i:00}.wav", s.In);
				Load($"res://assets/audio/sfx/pbreath_{tag}_out_{i:00}.wav", s.Out);
			}
			_sets[tag] = s;
		}
		for (int i = 1; i <= 8; i++) Load($"res://assets/audio/sfx/pbreath_gasp_{i:00}.wav", _gasps);
		_a = new AudioStreamPlayer { Bus = "Player", VolumeDb = -80f };
		_b = new AudioStreamPlayer { Bus = "Player", VolumeDb = -80f };
		AddChild(_a);
		AddChild(_b);
		var steps = _player.GetNodeOrNull<PlayerFootsteps>("Footsteps");
		if (steps != null) steps.Stepped += OnStep;
	}

	private static void Load(string path, List<AudioStream> into)
	{
		if (ResourceLoader.Exists(path)) into.Add(GD.Load<AudioStream>(path));
	}

	private static double Now => Time.GetTicksMsec() / 1000.0;
	private bool Running => _player.IsRunning && _player.GroundSpeed > 2.2f;
	private bool Moving => _player.GroundSpeed > 0.4f;

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		// exertion: up while running, down otherwise (slower while still walking); a spent stamina is a spent breath
		Exertion = Running
			? Mathf.Min(1f, Exertion + ExertionRise * dt)
			: Mathf.Max(0f, Exertion - ExertionFall * dt * (Holding ? 0.3f : Moving ? 0.7f : 1f));
		if (_stamina != null) Exertion = Mathf.Max(Exertion, Mathf.Clamp((0.5f - _stamina.Value) / 0.5f, 0f, 1f) * 0.9f);
		// fear: a fright's, ebbing over ~8 s, and the thing's nearness
		Fear = Mathf.Max(Mathf.Max(0f, Fear - dt / 8f), Threat());
		Tremble = Mathf.Max(0f, Tremble - dt / 6f);
		HoldBreath(dt);
		if (Holding) return;

		// running: the breath rides the footfalls (OnStep); the timer only covers a run with no steps heard
		if (Running && Now - _lastStepAt < 0.8) return;

		_timer -= dt;
		if (_timer > 0f) return;
		Breathe();
	}

	/// <summary>The stalker near (awake, within ~20 m): afraid, more the closer.</summary>
	private float Threat()
	{
		float best = 0f;
		foreach (var n in GetTree().GetNodesInGroup("stalker"))
		{
			if (n is not Entities.Stalker { Awake: true, Visible: true } s) continue;
			float d = s.GlobalPosition.DistanceTo(_player.GlobalPosition);
			best = Mathf.Max(best, Mathf.Clamp((20f - d) / 14f, 0f, 1f) * 0.8f);
		}
		return best;
	}

	private void OnStep()
	{
		_lastStepAt = Now;
		if (!Running || Holding) return;
		if (++_steps < _stepsPerHalf) return;
		_steps = 0;
		// three steps to each half while it's easy, two once it's hard (a three now and then: runners drift)
		_stepsPerHalf = Exertion < 0.45f ? 3 : _rng.Randf() < 0.15f ? 3 : 2;
		Breathe();
	}

	/// <summary>The next half-breath, from the set the body's in, and when the one after it is due.</summary>
	private void Breathe()
	{
		bool inhale = _nextIsIn;
		_nextIsIn = !_nextIsIn;
		float ex = Exertion, fear = Fear;
		string mode;
		float db, bpm, jitter;
		if (fear > 0.3f && fear >= ex * 0.8f)
		{
			// afraid: shallow and quick, uneven; standing, a stillness now and then (listening), then a shaky breath out
			if (inhale && !Moving && _still <= 0f && _rng.Randf() < 0.12f * fear)
			{
				_still = _rng.RandfRange(1.5f, 3.5f);
				_timer = _still;
				_nextIsIn = false;
				Mode = "still";
				return;
			}
			_still = 0f;
			mode = "scared";
			bpm = Mathf.Lerp(22f, 40f, fear);
			jitter = 0.3f;
			db = Mathf.Lerp(-35.5f, -27.5f, fear);
		}
		else if (Running)
		{
			mode = ex > 0.7f ? "winded" : "mouth";
			bpm = Mathf.Lerp(26f, 44f, ex);
			jitter = 0.1f;
			db = Mathf.Lerp(-35.5f, MaxVolumeDb + 1f, ex);
		}
		else if (ex > 0.4f)
		{
			mode = "winded";
			bpm = Mathf.Lerp(20f, 40f, (ex - 0.4f) / 0.6f);
			jitter = 0.12f;
			db = Mathf.Lerp(-34.5f, MaxVolumeDb, (ex - 0.4f) / 0.6f);
		}
		else if (ex > 0.15f)
		{
			mode = "mouth";
			bpm = Mathf.Lerp(16f, 22f, (ex - 0.15f) / 0.25f);
			jitter = 0.14f;
			db = Mathf.Lerp(-41.5f, -35.5f, (ex - 0.15f) / 0.25f);
		}
		else
		{
			mode = "nose";
			bpm = Moving ? 16f : 13f;
			jitter = 0.16f;
			db = Moving ? -42.5f : -45.5f;
		}
		db += -5f * _player.CrouchAmount;   // (crouched: quieter, deliberately)
		Mode = mode;
		// in ~40 % of the cycle, out ~60 %
		float cycle = 60f / bpm * _rng.RandfRange(1f - jitter, 1f + jitter);
		_timer = (inhale ? 0.4f : 0.6f) * cycle;
		if (_still > 0f && !inhale) { _still = 0f; db -= 2f; }
		if (!Muted) Play(_sets[mode], inhale, db);
	}

	private void Play(Set set, bool inhale, float db)
	{
		var list = inhale ? set.In : set.Out;
		if (list.Count == 0) return;
		int idx = inhale ? set.InPick.Next(_rng, list.Count) : set.OutPick.Next(_rng, list.Count);
		PlayStream(list[idx], db + _rng.RandfRange(-1.5f, 1f), _rng.RandfRange(0.9f, 0.98f));
	}

	private void PlayStream(AudioStream s, float db, float pitch)
	{
		// alternate voices: the last breath's tail runs on under this one
		var v = _last == _a ? _b : _a;
		v.Stream = s;
		v.VolumeDb = db;
		v.PitchScale = pitch;
		v.Play();
		_last = v;
	}

	/// <summary>A fright's catch of the breath (not twice in a heartbeat), then the scared breath goes on from the out.</summary>
	private void Gasp(float amount)
	{
		if (Holding || Muted || _gasps.Count == 0 || Now - _lastGaspAt < 1.5) return;
		_lastGaspAt = Now;
		PlayStream(_gasps[_gaspPick.Next(_rng, _gasps.Count)], Mathf.Lerp(-27f, -18f, amount), _rng.RandfRange(0.9f, 0.98f));
		Mode = "gasp";
		_nextIsIn = false;
		_timer = _rng.RandfRange(0.45f, 0.7f);
		_still = 0f;
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
			Play(_sets[Exertion > 0.4f ? "winded" : "mouth"], true, -29.5f);
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
					// can't any more: a gasp, loud, and a while before it can be held again; then winded
					_gaspAt = Now;
					_recover = 3f;
					Exertion = Mathf.Max(Exertion, 0.6f);
					if (_gasps.Count > 0) PlayStream(_gasps[_gaspPick.Next(_rng, _gasps.Count)], -14f, 0.94f);
					GD.Print("[breath] held too long - a gasp");
					_nextIsIn = false;
				}
				else { Play(_sets[Fear > 0.3f ? "scared" : "mouth"], false, -33.5f); _nextIsIn = true; }
				_timer = 1.0f;
			}
		}
		if (_overlay == null && IsInsideTree())
		{
			_overlay = new UI.BreathHoldOverlay { Name = "BreathHold" };
			AddChild(_overlay);
		}
		if (_overlay != null) _overlay.Amount = Holding ? Mathf.Clamp(0.25f + 0.75f * HeldFor / MaxHold, 0f, 1f) : 0f;
	}
}
