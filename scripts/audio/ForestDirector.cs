using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;

namespace ProjectDS.Audio;

/// <summary>
/// Keeps the forest from sounding like a loop. It sits on top of
/// ForestAmbienceManager (which still owns silence) and nudges the
/// ambience layers and emitters in four ways:
///
/// 1. Stillness. Stand still and a living forest comes closer: birds call more
///    and nearer, crickets start up by your feet, and things move in the leaf
///    litter. Walking pushes it back out. Inside a silence it works the other
///    way: the pressure and ear-ringing swell, and after a while you hear your
///    own heartbeat. Stand still long enough in the ordinary woods and
///    whatever follows you makes itself known.
/// 2. Weather. Wind gusts swell the wind and leaf beds, if the scene has them
///    (the current forest mix has none; smooth noise beds read as engines or surf).
/// 3. Activity waves. The birds come in bursts and lulls, not at a steady rate.
/// 4. Inside a silence, your footsteps take on a small dry room, as if you
///    had stepped indoors, and now and then even the low sound drops out
///    completely for a moment, as though something is listening.
///
/// (Each bed also drifts on its own; see AmbienceLoop.Drift.)
/// </summary>
public partial class ForestDirector : Node
{
	[ExportGroup("Stillness")]
	[Export] public float StillStartsAfter = 8f;
	[Export] public float StillFullAfter = 25f;
	/// <summary>The heartbeat needs longer stillness than the rest.</summary>
	[Export] public Vector2 HeartbeatStillWindow = new(20f, 40f);
	[Export] public float StalkerNudgeAfter = 30f;

	[ExportGroup("Inside the silence")]
	[Export] public float RoomWetMax = 0.22f;
	[Export] public Vector2 DropoutIntervalSeconds = new(18f, 40f);
	[Export] public Vector2 DropoutHoldSeconds = new(1.5f, 3.5f);
	[Export] public float DropoutReturnSeconds = 2.5f;

	[ExportGroup("Activity waves")]
	[Export] public Vector2 BurstSeconds = new(15f, 40f);
	[Export] public Vector2 LullSeconds = new(20f, 50f);
	[Export] public Vector2 BurstLevel = new(1.1f, 1.35f);
	[Export] public Vector2 LullLevel = new(0.35f, 0.7f);

	[ExportGroup("Gusts")]
	[Export] public Vector2 GustIntervalSeconds = new(14f, 40f);
	[Export] public Vector2 GustLengthSeconds = new(3f, 7f);
	[Export] public float GustWindDb = 5f;
	[Export] public float GustLeavesDb = 7f;
	[Export] public float GustLeavesDelay = 0.6f;
	[Export] public float GustCreakChance = 0.5f;

	public float StillSeconds { get; private set; }
	public float Stillness { get; private set; }
	public float Activity { get; private set; } = 1f;
	public float GustLevel { get; private set; }
	public int GustCount { get; private set; }
	public float Dropout { get; private set; } = 1f;
	public int DropoutCount { get; private set; }

	private AmbienceLoop _wind, _leaves, _insects, _pressure, _ringing, _heartbeat;
	private OneShotEmitter _birds, _crickets, _critters, _creaks;
	private PlayerController _player;
	private readonly RandomNumberGenerator _rng = new();

	private bool _burst = true;
	private float _waveTimer;
	private float _waveTarget = 1f;
	private float _gustTimer;
	private float _gustLength;
	private float _gustAge = -1f;
	private bool _gustCreaked;
	private bool _nudgedThisStill;
	private AudioEffectReverb _room;
	private float _dropoutTimer = 25f;
	private float _dropoutAge = -1f;
	private float _dropoutHold;

	public override void _Ready()
	{
		var root = GetParent();
		_wind = Loop(root, "Wind");
		_leaves = Loop(root, "Leaves");
		_insects = Loop(root, "Insects");
		_pressure = Loop(root, "Pressure");
		_ringing = Loop(root, "Ringing");
		_heartbeat = Loop(root, "Heartbeat");
		_birds = root.GetNodeOrNull<OneShotEmitter>("BirdCalls");
		_crickets = root.GetNodeOrNull<OneShotEmitter>("Crickets");
		_critters = root.GetNodeOrNull<OneShotEmitter>("Critters");
		_creaks = root.GetNodeOrNull<OneShotEmitter>("Creaks");
		int playerBus = AudioServer.GetBusIndex("Player");
		for (int i = 0; playerBus >= 0 && i < AudioServer.GetBusEffectCount(playerBus); i++)
			if (AudioServer.GetBusEffect(playerBus, i) is AudioEffectReverb rv) _room = rv;
		_waveTimer = _rng.RandfRange(BurstSeconds.X, BurstSeconds.Y);
		_gustTimer = _rng.RandfRange(GustIntervalSeconds.X, GustIntervalSeconds.Y);
	}

	private static AmbienceLoop Loop(Node root, string name) => root.GetNodeOrNull<AmbienceLoop>($"{name}/Loop");

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (_player == null)
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			if (_player == null) return;
		}
		float silence = ForestAmbienceManager.Instance?.Silence ?? 0f;

		UpdateStillness(dt);
		UpdateActivity(dt);
		UpdateGust(dt);
		ApplyLivingForest(silence);
		ApplySilence(silence, dt);
		NudgeStalker(silence);
	}

	private void UpdateStillness(float dt)
	{
		// Walking resets it fast; looking around doesn't count as moving.
		if (_player.GroundSpeed < 0.25f) StillSeconds += dt;
		else { StillSeconds = Mathf.Max(0f, StillSeconds - dt * 6f); _nudgedThisStill = false; }
		Stillness = Mathf.SmoothStep(StillStartsAfter, StillFullAfter, StillSeconds);
	}

	private void UpdateActivity(float dt)
	{
		_waveTimer -= dt;
		if (_waveTimer <= 0f)
		{
			_burst = !_burst;
			var len = _burst ? BurstSeconds : LullSeconds;
			var lvl = _burst ? BurstLevel : LullLevel;
			_waveTimer = _rng.RandfRange(len.X, len.Y);
			_waveTarget = _rng.RandfRange(lvl.X, lvl.Y);
		}
		Activity = Mathf.Lerp(Activity, _waveTarget, 1f - Mathf.Exp(-dt / 4f));
	}

	private void UpdateGust(float dt)
	{
		if (_gustAge < 0f)
		{
			_gustTimer -= dt;
			if (_gustTimer > 0f) return;
			_gustAge = 0f;
			_gustLength = _rng.RandfRange(GustLengthSeconds.X, GustLengthSeconds.Y);
			_gustCreaked = false;
			GustCount++;
		}

		_gustAge += dt;
		float Env(float age) => age <= 0f || age >= _gustLength ? 0f
			: Mathf.Sin(Mathf.Pi * Mathf.Pow(age / _gustLength, 0.7f));   // quick swell, long tail
		GustLevel = Env(_gustAge);
		if (_wind != null) _wind.ExtraDb = GustWindDb * GustLevel;
		if (_leaves != null) _leaves.ExtraDb = GustLeavesDb * Env(_gustAge - GustLeavesDelay);

		if (!_gustCreaked && GustLevel > 0.9f)
		{
			_gustCreaked = true;
			if (_creaks != null && _rng.Randf() < GustCreakChance) _creaks.Trigger(2f);
		}
		if (_gustAge >= _gustLength + GustLeavesDelay)
		{
			_gustAge = -1f;
			_gustTimer = _rng.RandfRange(GustIntervalSeconds.X, GustIntervalSeconds.Y);
		}
	}

	private void ApplyLivingForest(float silence)
	{
		// Stillness only draws life in where there is life left to draw.
		float alive = 1f - Mathf.SmoothStep(0.05f, 0.4f, silence);
		float s = Stillness * alive;
		if (_birds != null)
		{
			_birds.RateScale = Activity * (1f + 1.3f * s);
			_birds.DistanceScale = 1f - 0.45f * s;
		}
		if (_crickets != null)
		{
			_crickets.RateScale = 0.6f + 2.2f * s;
			_crickets.DistanceScale = 1f - 0.4f * s;
		}
		if (_critters != null)
		{
			// Animals keep clear of someone crashing through; they come out when you stop.
			_critters.RateScale = 0.35f + 2.6f * s;
			_critters.DistanceScale = 1f - 0.35f * s;
		}
		if (_insects != null) _insects.ExtraDb = 2f * (Activity - 1f) + 2.5f * s;
	}

	private void ApplySilence(float silence, float dt)
	{
		float inSilence = Mathf.SmoothStep(0.5f, 1f, silence);
		UpdateDropout(silence, dt);

		float swell = 6f * Stillness * inSilence;
		if (_pressure != null) { _pressure.ExtraDb = swell; _pressure.Gain = Dropout; }
		if (_ringing != null) { _ringing.ExtraDb = swell; _ringing.Gain = Dropout; }
		if (_heartbeat != null)
		{
			// Your heartbeat is inside you: it carries on through the dropouts.
			float deep = Mathf.SmoothStep(HeartbeatStillWindow.X, HeartbeatStillWindow.Y, StillSeconds);
			_heartbeat.Gain = deep * Mathf.SmoothStep(0.6f, 1f, silence);
		}
		// Footsteps pick up a small dry room: indoors, in the middle of the woods.
		if (_room != null)
		{
			_room.Wet = Mathf.Lerp(0.02f, RoomWetMax, inSilence);
			_room.RoomSize = Mathf.Lerp(0.35f, 0.18f, inSilence);
			_room.Damping = Mathf.Lerp(0.6f, 0.85f, inSilence);
		}
	}

	/// <summary>Deep in the silence, the low sound occasionally cuts out altogether, then slowly returns.</summary>
	private void UpdateDropout(float silence, float dt)
	{
		if (_dropoutAge < 0f)
		{
			Dropout = 1f;
			if (silence < 0.9f) return;
			_dropoutTimer -= dt;
			if (_dropoutTimer > 0f) return;
			_dropoutAge = 0f;
			_dropoutHold = _rng.RandfRange(DropoutHoldSeconds.X, DropoutHoldSeconds.Y);
			DropoutCount++;
		}
		_dropoutAge += dt;
		const float cut = 0.25f;
		if (_dropoutAge < cut) Dropout = 1f - _dropoutAge / cut;
		else if (_dropoutAge < cut + _dropoutHold) Dropout = 0f;
		else Dropout = Mathf.Min(1f, (_dropoutAge - cut - _dropoutHold) / DropoutReturnSeconds);
		if (_dropoutAge >= cut + _dropoutHold + DropoutReturnSeconds)
		{
			_dropoutAge = -1f;
			_dropoutTimer = _rng.RandfRange(DropoutIntervalSeconds.X, DropoutIntervalSeconds.Y);
		}
	}

	private Stalker _stalker;
	private bool _stalkerSearched;

	private void NudgeStalker(float silence)
	{
		if (_nudgedThisStill || StillSeconds < StalkerNudgeAfter || silence > 0.3f) return;
		if (ForestAmbienceManager.Instance is { IsIndoor: true }) return;   // nothing follows you indoors
		_nudgedThisStill = true;
		if (!_stalkerSearched) { _stalkerSearched = true; _stalker = GetTree().GetFirstNodeInGroup("stalker") as Stalker; }
		if (_stalker != null && IsInstanceValid(_stalker)) _stalker.NudgeNoise();
	}
}
