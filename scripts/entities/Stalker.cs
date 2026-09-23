using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.Entities;

/// <summary>
/// Something that follows you through the woods. It should be hard to see and
/// only occasionally spotted.
///
/// Rules it keeps:
/// - It is always behind a tree trunk, never in the open, with only a thin
///   sliver of shoulder and head leaning out.
/// - Usually that trunk is behind the player. Sometimes it's far ahead, just
///   past the edge of the view, so a small turn might catch it.
/// - It only ever moves while outside the camera's view, so it never visibly
///   teleports.
/// - Seen, it holds for a moment, then is gone in a blink. Glimpsed and looked
///   away from, it is simply gone when you look back. Catching it in a photo
///   (Act 1's camera) brings an uneasy sting.
/// - It tails you tree to tree. Walk on past its tree and it is gone, then a
///   few seconds later it is behind another one. Linger and it moves trees.
/// - Unseen, it creeps closer over time; being seen pushes it back.
/// - Seen, it ducks sideways behind its trunk as it dissolves: the movement is
///   the tell.
/// - It is heard more than seen, but only from where it actually is: while it
///   waits, its steps answer yours a beat late (the player's own samples, from where it
///   stands) and stop when you stop, and its rattle clicks from its tree, slower far off and
///   quicker the closer or more urgent it is (one take, creature_rattle_loop_01). It has no other
///   voice: no growl, snarl or screech, and it makes no sound when it goes.
/// - It does not follow at all on the Blackfern Trail: it starts in the Hollow,
///   once the lantern and the compass are in hand. Until the footbridge it stalks hardest
///   (its introduction: see the Intro exports). It never enters the silence around a
///   staircase, and it withdraws entirely while the player is indoors (the
///   cabin, the bunker), where there are no trees to hide behind.
/// </summary>
public partial class Stalker : Node3D
{
	public enum State { Dormant, Hidden, Peeking, Vanishing }

	[ExportGroup("Pacing")]
	[Export] public Vector2 CooldownSeconds = new(20f, 50f);   // after being seen, it keeps its distance a while
	/// <summary>How long it waits at one tree, unseen, before moving to another while you linger nearby.</summary>
	[Export] public Vector2 RelocateSeconds = new(18f, 35f);
	/// <summary>Walk this far past it without ever seeing it and it is gone (it turns up elsewhere later).</summary>
	[Export] public float MoveOnDistance = 32f;
	/// <summary>Wait before reappearing after you walked on without seeing it (shorter than after being seen).</summary>
	[Export] public Vector2 UnseenCooldown = new(2f, 6f);   // it keeps tailing you, tree to tree
	[Export] public float NeverCloserThan = 7f;

	[ExportGroup("Cover")]
	/// <summary>Distance of its trees behind you when it has just been seen (far) .. after a long time unseen (near).</summary>
	[Export] public Vector2 BehindDistanceFar = new(18f, 30f);
	[Export] public Vector2 BehindDistanceNear = new(9f, 16f);
	/// <summary>Seconds of going unseen for it to close all the way in.</summary>
	[Export] public float CloseInSeconds = 120f;
	/// <summary>Share of appearances behind a trunk far AHEAD instead of behind the player.</summary>
	[Export] public float AheadChance = 0.15f;
	[Export] public Vector2 AheadDistance = new(20f, 40f);   // past ~45 m the fog swallows it
	/// <summary>Degrees off your view direction for the ahead spot: just past the edge of the screen.</summary>
	[Export] public Vector2 AheadAngleDegrees = new(50f, 75f);
	/// <summary>How far it leans out past the trunk edge (metres). Small = hard to see.</summary>
	[Export] public Vector2 ExposeOffset = new(0.16f, 0.28f);
	[Export] public float LeanDegrees = 2f;
	/// <summary>Most of its sample points (of 10) you may have a clear line to: only part of it ever shows.</summary>
	[Export] public int MaxExposedPoints = 3;

	[ExportGroup("Being seen")]
	[Export] public Vector2 LingerSeconds = new(0.08f, 0.3f);
	[Export] public float LongLingerChance = 0.1f;
	[Export] public float LongLingerSeconds = 0.6f;
	[Export] public float VanishSeconds = 0.1f;   // gone the instant it is caught (Dan, 2026-09-22)
	/// <summary>Seen, it ducks sideways into cover this fast while dissolving (m/s). The movement is the tell.</summary>
	[Export] public float DuckSpeed = 3.2f;
	/// <summary>Never fully solid: its peak opacity in the screen-door dither (1 = solid).</summary>
	[Export(PropertyHint.Range, "0.1,1")] public float MaxVisibility = 1f;
	/// <summary>Walk closer than this to an ahead sighting and it goes.</summary>
	[Export] public float AheadBreakDistance = 18f;
	[Export] public float StingVolumeDb = -4f;

	[ExportGroup("Silence")]
	/// <summary>Above this player silence it withdraws completely.</summary>
	[Export] public float RetreatAtSilence = 0.5f;
	[Export] public float AvoidSilenceAbove = 0.25f;

	[ExportGroup("Sound")]
	[Export] public float EchoStepChance = 0.4f;
	[Export] public float EchoStepCooldown = 35f;
	/// <summary>Seconds of walking between spells of its steps shadowing yours.</summary>
	[Export] public Vector2 ShadowInterval = new(10f, 25f);
	[Export] public Vector2I ShadowSteps = new(4, 10);
	[Export] public float StepVolumeDb = 3f;

	[ExportGroup("Intro (the Hollow path walk)")]
	/// <summary>Until the footbridge (checkpoint 5) it stalks hard: this is its introduction as a character.
	/// These replace the pacing exports above while <see cref="Intro"/> is true (Dan, 2026-09-22).</summary>
	[Export] public Vector2 IntroCooldownSeconds = new(6f, 14f);
	[Export] public Vector2 IntroRelocateSeconds = new(6f, 12f);   // a new tree, a new side, even while you keep walking
	[Export] public Vector2 IntroUnseenCooldown = new(1f, 3f);
	[Export] public float IntroCloseInSeconds = 45f;
	[Export] public float IntroAheadChance = 0.3f;
	[Export] public Vector2 IntroShadowInterval = new(2f, 5f);   // steps are most of what is heard on the path (Dan, 2026-09-22)
	[Export] public Vector2I IntroShadowSteps = new(4, 10);
	[Export] public float IntroEchoStepChance = 0.8f;
	[Export] public float IntroEchoStepCooldown = 6f;
	[Export] public float IntroRattleRange = 34f;
	/// <summary>Seconds after waking before it first stands behind a tree.</summary>
	[Export] public Vector2 IntroFirstAppearance = new(8f, 13f);

	[ExportGroup("Voice")]
	/// <summary>The rattle (a dry clicking croak from its tree) is inaudible beyond this and grows as you close in.</summary>
	[Export] public float RattleRange = 26f;
	/// <summary>Level of the rattle when it is right on you.</summary>
	[Export] public float RattleMaxDb = 3f;
	/// <summary>Unused since 2026-09-22 (the rate is lerp(0.55, 1, intensity) in UpdateRattle); kept so scene overrides still load.</summary>
	[Export] public Vector2 RattlePitch = new(0.72f, 1f);

	public State Current { get; private set; } = State.Dormant;
	public int PeekCount { get; private set; }
	public int DistantCount { get; private set; }
	public bool IsDistant => _ahead;
	public int SeenCount { get; private set; }
	public int StingCount { get; private set; }
	public int ShadowSpells { get; private set; }
	public int SoundCount { get; private set; }
	/// <summary>Always 0 since 2026-09-22: it has no voice but the rattle (Dan: no snarl, screech or growl, it just disappears).</summary>
	public int SnarlCount => 0;
	/// <summary>The Hollow path walk, from the wake to the footbridge: its introduction, when it stalks hardest.</summary>
	public bool Intro => StoryManager.Instance is { } s && s.Current < Checkpoint.Act6BridgeCrossed;
	/// <summary>0..1 how loud the rattle is right now (for the debug readout and tests).</summary>
	public float RattleLevel { get; private set; }
	/// <summary>0..1, set by the story (the survey lot: a quarter per digit found): the rattle reaches
	/// further, plays louder and quicker, and no longer needs it to be at a tree: it is somewhere behind
	/// you, everywhere, closing. Hurry.</summary>
	public float Urgency { get; set; }
	/// <summary>Out of the bunker (checkpoint 8 on): the clicking is on you for the rest of the game, wherever you are.</summary>
	public bool Hunted => StoryManager.Instance is { Current: >= Checkpoint.Act10WalkieFound };
	public float LastSeenFraction { get; private set; }
	public double LastSeenDuration { get; private set; }
	/// <summary>0 = just seen, keeping back .. 1 = long unseen, as close as it gets.</summary>
	public float Tension { get; private set; }
	/// <summary>True once it has started following (from the first climb on, or a dev key).</summary>
	public bool Awake => _awake;

	/// <summary>Points on the body (local to Body) used to decide whether the player can see it.</summary>
	[Export] public Vector3[] SamplePoints =
	{
		new(0, 2.12f, 0), new(0, 1.92f, 0), new(-0.24f, 1.7f, 0), new(0.24f, 1.7f, 0),
		new(0, 1.4f, 0), new(0, 1.0f, 0), new(-0.1f, 0.5f, 0), new(0.1f, 0.5f, 0),
	};

	private PlayerController _player;
	private Node3D _body;
	private bool _awake;
	private readonly List<ShaderMaterial> _skins = new();
	private readonly RandomNumberGenerator _rng = new();
	private Vector3 _lastPlayerPos;
	private bool _hasLastPos;
	private float _cooldown = 4f;
	private float _stayTimer;
	private float _linger;
	private double _seenTime;
	private bool _seenThisPeek;
	private bool _stungThisPeek;
	private float _visibility;
	private Vector3 _hideDir;
	private bool _ahead;

	// Reused query objects: the sweeps run many rays a frame while it peeks, so nothing is allocated per ray.
	private Godot.Collections.Array<Rid> _exclude;
	private PhysicsRayQueryParameters3D _ray;
	private PhysicsShapeQueryParameters3D _overlap;

	// Its footsteps are the player's own samples (Dan, 2026-09-22: they must SOUND like the player's), picked
	// by the surface under IT, a touch heavier, a beat behind each of the player's steps.
	private readonly Dictionary<string, AudioStream[]> _stepSets = new();
	// Where it stands relative to you: the sectors it picks from (degrees off straight behind, + = to your right).
	private static readonly float[] SectorDegrees = { 0f, -60f, 60f, -105f, 105f };
	private const int AheadSector = 5;
	private int _lastSector = -1, _trySector;
	private readonly HashSet<int> _sectorsUsed = new();
	private AudioStreamPlayer3D _rattle;
	private AmbienceLoop _rattleLoop;
	private AudioStream _sting;
	private readonly List<AudioStreamPlayer3D> _voices = new();
	private AudioStreamPlayer _stingVoice;
	private int _nextVoice;
	private SamplePicker _stepPicker;
	private readonly List<(double at, Vector3 pos, AudioStream stream, bool shadow)> _pendingSteps = new();
	private double _clock;
	private double _nextEchoAllowed;
	private double _nextShadow;

	// The pacing in force: the intro's (the Hollow path walk, until the footbridge) or the exports'.
	private Vector2 CooldownNow => Intro ? IntroCooldownSeconds : CooldownSeconds;
	private Vector2 RelocateNow => Intro ? IntroRelocateSeconds : RelocateSeconds;
	private Vector2 UnseenCooldownNow => Intro ? IntroUnseenCooldown : UnseenCooldown;
	private float CloseInNow => Intro ? IntroCloseInSeconds : CloseInSeconds;
	private float AheadChanceNow => Intro ? IntroAheadChance : AheadChance;
	private Vector2 ShadowIntervalNow => Intro ? IntroShadowInterval : ShadowInterval;
	private Vector2I ShadowStepsNow => Intro ? IntroShadowSteps : ShadowSteps;
	private float EchoStepChanceNow => Intro ? IntroEchoStepChance : EchoStepChance;
	private float EchoStepCooldownNow => Intro ? IntroEchoStepCooldown : EchoStepCooldown;
	private float RattleRangeNow => Intro ? IntroRattleRange : RattleRange;
	private int _shadowLeft;
	private bool _listening;
	private float _walkedFor;

	public override void _Ready()
	{
		AddToGroup("stalker");
		ProjectDS.Player.CameraTool.PhotoTaken += OnPhotoTaken;
		_body = GetNode<Node3D>("Body");
		// Every visible part dissolves together: collect each distinct shader material under Body.
		foreach (var node in _body.FindChildren("*", "GeometryInstance3D", true, false))
			if (((GeometryInstance3D)node).MaterialOverride is ShaderMaterial m && !_skins.Contains(m)) _skins.Add(m);
		TopLevel = true;
		SetVisibility(0f);

		_stepSets["dirt"] = LoadSet("res://assets/audio/sfx/step_dirt_{0:00}.wav", 6);
		_stepSets["wood"] = LoadSet("res://assets/audio/sfx/step_wood_{0:00}.wav", 4);
		_stepSets["stone"] = LoadSet("res://assets/audio/sfx/step_stone_{0:00}.wav", 6);
		// No twig snaps from it (Dan, 2026-09-22: a dry snap reads as a distant gunshot; the rattle is its sound now).
		if (ResourceLoader.Exists("res://assets/audio/sfx/stalker_seen_01.wav"))
			_sting = GD.Load<AudioStream>("res://assets/audio/sfx/stalker_seen_01.wav");
		for (int i = 0; i < 4; i++)
		{
			var p = new AudioStreamPlayer3D { Bus = "Unnatural", UnitSize = 16f, MaxDistance = 110f, TopLevel = true };   // carries: you should hear it and turn
			AddChild(p);
			_voices.Add(p);
		}
		_stingVoice = new AudioStreamPlayer { Bus = "Unnatural" };
		AddChild(_stingVoice);
		// Its only voice is the rattle loop that rises as you close in (Dan, 2026-09-22: no growl, snarl or
		// screech, ever; it makes no sound when it goes, it is just gone). Every rattle take there is, so you
		// very rarely hear the same beat twice.
		const string rattlePath = "res://assets/audio/sfx/creature_rattle_loop_01.wav";   // the one take (the others read as hooves)
		if (ResourceLoader.Exists(rattlePath))
		{
			_rattle = new AudioStreamPlayer3D { Bus = "Unnatural", UnitSize = 8f, MaxDistance = 60f, TopLevel = true };
			AddChild(_rattle);
			_rattleLoop = new AmbienceLoop { Name = "Loop", StreamPath = rattlePath, BaseVolumeDb = RattleMaxDb, Gain = 0f };
			_rattle.AddChild(_rattleLoop);
		}
		_nextShadow = _rng.RandfRange(ShadowInterval.X, ShadowInterval.Y);
		_overlap = new PhysicsShapeQueryParameters3D
		{
			Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.9f },
			CollisionMask = 1,
		};
	}

	public override void _ExitTree() => ProjectDS.Player.CameraTool.PhotoTaken -= OnPhotoTaken;

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

	/// <summary>The surface under its feet, from the collider's "surface" meta like the player's own steps (dirt by default).</summary>
	private string SurfaceUnderIt()
	{
		if (_ray == null) return "dirt";
		var hit = Ray(GlobalPosition + Vector3.Up * 0.5f, GlobalPosition + Vector3.Down * 1.5f);
		if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node n && n.HasMeta("surface")) return n.GetMeta("surface").AsString();
		return "dirt";
	}

	private AudioStream PickStep()
	{
		if (!_stepSets.TryGetValue(SurfaceUnderIt(), out var set) || set.Length == 0) set = _stepSets["dirt"];
		return set.Length == 0 ? null : set[_stepPicker.Next(_rng, set.Length)];
	}

	/// <summary>The player has stood still a long time: appear soon.</summary>
	public void NudgeNoise()
	{
		_cooldown = Mathf.Min(_cooldown, _rng.RandfRange(1f, 4f));   // come and stand behind a tree soon
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (!OS.IsDebugBuild()) return;   // dev keys only in development builds
		// F6: dev key, makes it take cover behind a trunk behind you now (skips the dormant stretch).
		if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F6 })
			GD.Print(DebugForcePeek() ? "[stalker] behind you" : "[stalker] no cover found behind you");
		// F7: dev key, makes it take cover behind a trunk far ahead, just past the edge of your view.
		else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F7 })
			GD.Print(DebugForceDistant() ? "[stalker] out ahead of you" : "[stalker] no cover found ahead");
	}

	/// <summary>Test hook: take cover far ahead now if a trunk is available.</summary>
	public bool DebugForceDistant()
	{
		if (!DebugWake()) return false;
		if (Current != State.Hidden && Current != State.Dormant) Hide(0f);
		return TryPlace(GetViewport().GetCamera3D(), ahead: true);
	}

	/// <summary>Test hook: take cover behind the player now if a trunk is available.</summary>
	public bool DebugForcePeek()
	{
		if (!DebugWake()) return false;
		if (Current is State.Peeking or State.Vanishing) return Current == State.Peeking;
		_cooldown = 0f;
		return TryPlace(GetViewport().GetCamera3D(), ahead: false);
	}

	/// <summary>Dev keys skip the dormant stretch (but not the indoors rule: there is nowhere to hide).</summary>
	private bool DebugWake()
	{
		_awake = true;
		if (Current == State.Dormant) Current = State.Hidden;
		return _player != null && _body != null && !Indoors;
	}

	private static bool Indoors => ForestAmbienceManager.Instance is { IsIndoor: true };

	/// <summary>
	/// The hard gate (Dan, 2026-09-22: "the mini stalker should not stalk you inside the bunker", "only the jumpscares"):
	/// indoors, or anywhere in the bunker's story window (checkpoint 7 until the walkie wakes outside at checkpoint 8),
	/// the free-roaming stalker is completely inert: no placements, no peeks, no rattle, no footsteps of any kind. The
	/// story window covers the admit fade, when IsIndoor is briefly false while the player already stands in the
	/// interior 300 m away. The bunker's own scripted scares (BunkerFlow / BunkerRooms) are the only stalker in there.
	/// </summary>
	public static bool Inert => Indoors
		|| (StoryManager.Instance is { } s && s.Current >= Checkpoint.Act8BunkerEntered && s.Current < Checkpoint.Act10WalkieFound);

	/// <summary>Whether the story lets it follow yet: only in the Hollow, and only once the lantern and the compass
	/// are both in hand (Dan, 2026-09-22: it starts stalking soon after those pickups, not before).</summary>
	private static bool ShouldWake() => StoryManager.Instance is not { } s
		|| (s.HasFlag(StoryManager.Flag.PickupTakenLantern) && s.HasFlag(StoryManager.Flag.PickupTakenCompass));

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_clock += delta;
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			if (_player == null) return;
			_exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
			_ray = new PhysicsRayQueryParameters3D { CollisionMask = 1, Exclude = _exclude };
			_listening = false;
		}
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		if (!_listening && _player.Footsteps is PlayerFootsteps feet)
		{
			feet.Stepped += OnPlayerStepped;
			_listening = true;
		}

		float speed = TrackSpeed(dt);
		// Indoors, or in the bunker, there is nothing of it at all: no sound, no body, until you come back out.
		if (Inert)
		{
			if (Current != State.Dormant) { Hide(0f); Current = State.Dormant; }
			_shadowLeft = 0;
			_pendingSteps.Clear();
			_walkedFor = 0f;
			if (_episodeOn) EndEpisode();
			_burstOn = false;
			RattleLevel = 0f;
			if (_rattleLoop != null) _rattleLoop.Gain = 0f;
			return;
		}
		PlayPendingSteps();
		UpdateRattle(dt);
		if (!_awake)
		{
			if (!ShouldWake()) { Current = State.Dormant; return; }
			_awake = true;
			// Its introduction: the first tree within seconds of the lantern and compass being taken.
			_cooldown = _rng.RandfRange(IntroFirstAppearance.X, IntroFirstAppearance.Y);
		}
		if (Current == State.Dormant) Current = State.Hidden;
		// Unseen, it grows bolder: its trees creep closer over time.
		Tension = Mathf.Min(1f, Tension + dt / CloseInNow);

		bool withdraw = SilenceAt(_player.GlobalPosition) > RetreatAtSilence;
		switch (Current)
		{
			case State.Hidden:
				_cooldown -= dt;
				if (!withdraw && _cooldown <= 0f)
				{
					bool placed = _rng.Randf() < AheadChanceNow && TryPlace(cam, ahead: true);
					if (!placed && !TryPlace(cam, ahead: false)) _cooldown = Intro ? 1.5f : 3f;
				}
				break;
			case State.Peeking:
				UpdatePeeking(cam, dt, withdraw);
				break;
			case State.Vanishing:
				// Gone the instant it is caught: a 0.1 s dissolve, no sound of any kind.
				SetVisibility(_visibility - dt * MaxVisibility / VanishSeconds);
				GlobalPosition += _hideDir * DuckSpeed * dt;
				if (_visibility <= 0f) Hide(_rng.RandfRange(CooldownNow.X, CooldownNow.Y));
				break;
		}

		if (!withdraw) UpdateSounds(speed);
	}

	private void UpdatePeeking(Camera3D cam, float dt, bool withdraw)
	{
		float dist = Flat(GlobalPosition).DistanceTo(Flat(_player.GlobalPosition));
		float seen = VisibleFraction(cam);
		if (seen > 0f)
		{
			if (!_seenThisPeek) { _seenThisPeek = true; SeenCount++; Tension = 0.1f; }
			LastSeenFraction = seen;
			_seenTime += dt;
			bool tooClose = _ahead && dist < AheadBreakDistance;
			if (_seenTime >= _linger || tooClose || withdraw) { LastSeenDuration = _seenTime; StartVanishing(); }
			return;
		}

		// Glimpsed, then the player looked away: it is simply gone when they look back.
		if (withdraw || _seenThisPeek)
		{
			if (_seenThisPeek) LastSeenDuration = _seenTime;
			Hide(_rng.RandfRange(CooldownNow.X, CooldownNow.Y));
			return;
		}

		// Unobserved. If you walked on without ever seeing it, it is simply gone.
		if (dist > (_ahead ? MoveOnDistance + 18f : MoveOnDistance))
		{
			Hide(_rng.RandfRange(UnseenCooldownNow.X, UnseenCooldownNow.Y));
			return;
		}
		// You are lingering near it: after a while it moves to another tree (only while nobody is looking).
		_stayTimer -= dt;
		if (_stayTimer <= 0f || (!_ahead && dist < NeverCloserThan) || ExposedPoints(cam.GlobalPosition) > MaxExposedPoints + 1)
		{
			Hide(0f);
			TryPlace(cam, ahead: false);
			return;
		}
		FacePlayer();
	}

	/// <summary>Raised when a photo catches it (the sting has just played). The photo log records the frame.</summary>
	public event System.Action Photographed;

	/// <summary>
	/// A photo taken with it in frame (visible in the camera's view, not behind cover)
	/// brings the sting, once per appearance, and it is gone as if seen.
	/// </summary>
	private void OnPhotoTaken(Camera3D cam)
	{
		if (!IsPresent || _stungThisPeek || _sting == null || cam == null || !IsInstanceValid(cam)) return;
		if (VisibleFraction(cam) <= 0f) return;
		_stungThisPeek = true;
		StingCount++;
		_stingVoice.Stream = _sting;
		_stingVoice.VolumeDb = StingVolumeDb;
		_stingVoice.Play();
		Photographed?.Invoke();
		if (Current == State.Peeking)
		{
			if (!_seenThisPeek) { _seenThisPeek = true; SeenCount++; Tension = 0.1f; }
			LastSeenDuration = _seenTime;
			StartVanishing();
		}
	}

	/// <summary>Caught looking: it ducks away and is gone. It makes no sound as it goes (Dan, 2026-09-22).</summary>
	private void StartVanishing()
	{
		Current = State.Vanishing;
		_episodeRequested = true;   // it ducked away from your eyes: a short rattle episode follows
	}

	/// <summary>
	/// The rattle: a dry clicking croak that plays from its tree while it stands there unseen, silent
	/// beyond <see cref="RattleRange"/> and rising (louder, quicker) the closer you come. Seen, or
	/// gone, it drops away at once.
	/// </summary>
	private void UpdateRattle(float dt)
	{
		if (_rattleLoop == null || _player == null) return;
		// Out of the bunker (Dan, 2026-09-22: the clicking must be far more present after that): full urgency,
		// twice the reach, and it never lets go of your back.
		bool hunted = Hunted;
		float urg = hunted ? 1f : Mathf.Clamp(Urgency, 0f, 1f);
		float target = 0f, intensity = 0f;
		Vector3 at = GlobalPosition + Vector3.Up * 1.5f;
		if (Present && !Indoors)
		{
			float dist = Flat(GlobalPosition).DistanceTo(Flat(_player.GlobalPosition));
			float close = Mathf.Clamp(1f - dist / (Mathf.Max(RattleRangeNow, 1f) * (1f + 0.6f * urg) * (hunted ? 2f : 1f)), 0f, 1f);
			target = Mathf.Max(close * close * (3f - 2f * close), (hunted ? 0.7f : 0.35f) * urg);
			intensity = close * (0.5f + 0.5f * urg);
		}
		else if (_awake && !Indoors && urg > 0f)
		{
			// Not at a tree, but not gone either: the clicking comes from just behind you, wherever you turn.
			target = (hunted ? 0.85f : 0.45f) * urg;
			var cam = GetViewport().GetCamera3D();
			Vector3 back = cam != null ? cam.GlobalBasis.Z : Vector3.Back; back.Y = 0;
			at = _player.GlobalPosition + back.Normalized() * 5f + Vector3.Up * 1.5f;
			intensity = 0.35f + 0.4f * urg;
		}
		// One take only, creature_rattle_loop_01 (Dan, 2026-09-22: the other beats read as hooves), and its speed is a
		// clean function of intensity: slow far off and calm, quick close in or hunted, gliding over about a second.
		if (hunted) intensity = Mathf.Max(intensity, 0.75f);
		_intensity = Mathf.MoveToward(_intensity, intensity, dt * 1f);
		_rattle.PitchScale = Mathf.Lerp(0.55f, 1f, _intensity);
		// Sporadic (Dan, 2026-09-22): it clicks in bursts with silences between, and no two bursts sit at
		// quite the same level. The burst gate scales the target; the level drift rides on the volume.
		// Less of it, and never over its own footsteps (Dan, 2026-09-22: "too much rattling"): bursts are short
		// (0.8-2.2 s) and rare (3-9 s apart on the path walk, 2-6 s later), a spell of steps holds the rattle off, and
		// every burst sits at its own level, most of them quiet, the odd one loud.
		// EPISODES (Dan, 2026-09-22: "it can rattle a lot at certain times but not all the dang time"): long quiet
		// stretches with no rattle at all (25-60 s on the path walk, 20-45 s later; its steps may still come), then an
		// episode of 10-20 s where it rattles a lot, bursts on 1-2.5 s and off 0.5-1.5 s, louder and quicker toward the
		// end, then silence again. Being very close, or having just ducked away from your eyes, starts a short episode.
		_episodeTimer -= dt;
		bool closeNow = Present && !Indoors && intensity > 0.8f;
		if (!_episodeOn && (closeNow || _episodeRequested) && _clock >= _episodeAllowedAt) StartEpisode(_rng.RandfRange(6f, 10f));
		_episodeRequested = false;
		if (_episodeTimer <= 0f)
		{
			if (_episodeOn) EndEpisode();
			else StartEpisode(_rng.RandfRange(10f, 20f));
		}
		float episodeFrac = _episodeOn ? Mathf.Clamp(1f - _episodeTimer / Mathf.Max(_episodeLength, 0.1f), 0f, 1f) : 0f;
		if (_episodeOn) _rattle.PitchScale = Mathf.Min(1f, _rattle.PitchScale * Mathf.Lerp(1f, 1.18f, episodeFrac));   // quicker toward the end
		_burstTimer -= dt;
		bool stepsRunning = _shadowLeft > 0;
		if (!_episodeOn) { _burstOn = false; _burstTimer = 0f; }
		else
		{
			if (_burstOn && stepsRunning) { _burstOn = false; _burstTimer = _rng.RandfRange(0.5f, 1.5f); }
			if (_burstTimer <= 0f)
			{
				if (!_burstOn && stepsRunning) _burstTimer = 0.3f;   // wait for the steps to end
				else
				{
					_burstOn = !_burstOn;
					_burstTimer = _burstOn ? _rng.RandfRange(1f, 2.5f) : _rng.RandfRange(0.5f, 1.5f);
					if (_burstOn)
					{
						RattleBursts++;
						float u = _rng.Randf();
						_burstDb = u < 0.15f ? _rng.RandfRange(1f, 3f) : u < 0.5f ? _rng.RandfRange(-4f, 0f) : _rng.RandfRange(-9f, -4f);
						_burstDb += 5f * episodeFrac;   // louder toward the end of the episode
					}
				}
			}
		}
		float gate = _burstOn ? 1f : 0f;
		_rattleLoop.BaseVolumeDb = RattleMaxDb + 5f * urg + (hunted ? 3f : 0f) + _burstDb;
		RattleLevel = Mathf.MoveToward(RattleLevel, target * gate, dt * (target * gate > RattleLevel ? 0.9f : 2.5f));
		_rattleLoop.Gain = RattleLevel;
		if (RattleLevel > 0.001f) _rattle.GlobalPosition = at;
		// For the test: how much of the time the rattle is actually audible.
		_rattleClock += dt;
		if (RattleLevel > 0.05f) _rattleAudible += dt;
	}

	private void StartEpisode(float length)
	{
		if (Inert) return;
		_episodeOn = true;
		_episodeLength = length;
		_episodeTimer = length;
		_burstOn = false;
		_burstTimer = 0f;
		RattleEpisodes++;
	}

	private void EndEpisode()
	{
		_episodeOn = false;
		_burstOn = false;
		_episodeTimer = Intro ? _rng.RandfRange(25f, 60f) : _rng.RandfRange(20f, 45f);
		_episodeAllowedAt = _clock + 8f;   // a close or a sighting cannot restart it at once
	}

	private bool _burstOn = false, _episodeOn = false, _episodeRequested = false;
	private float _intensity;
	private float _burstTimer = 0f, _burstDb;
	private float _episodeTimer = 12f, _episodeLength = 1f;   // the first quiet stretch is short: it is heard early, then goes quiet
	private double _episodeAllowedAt, _rattleClock, _rattleAudible;
	/// <summary>For tests: how many rattle bursts have started.</summary>
	public int RattleBursts { get; private set; }
	/// <summary>For tests: how many rattle episodes (the stretches where it rattles a lot) have started.</summary>
	public int RattleEpisodes { get; private set; }
	/// <summary>For tests: 0..1, the share of its awake time with the rattle audible.</summary>
	public float RattleAudibleFraction => _rattleClock > 1 ? (float)(_rattleAudible / _rattleClock) : 0f;

	private void Hide(float cooldown)
	{
		Current = State.Hidden;
		_cooldown = cooldown;
		SetVisibility(0f);
	}

	/// <summary>
	/// Find a tree trunk and stand behind it with a thin sliver exposed: behind the
	/// player, or (ahead = true) far ahead just past the edge of the view.
	/// </summary>
	private bool TryPlace(Camera3D cam, bool ahead)
	{
		if (_player == null || cam == null || _ray == null) return false;
		var space = GetWorld3D().DirectSpaceState;
		Vector3 eye = _player.GlobalPosition + Vector3.Up * 1.5f;
		Vector3 look = -cam.GlobalBasis.Z; look.Y = 0; look = look.Normalized();
		Vector2 range = ahead ? AheadDistance : BehindDistanceFar.Lerp(BehindDistanceNear, Tension);

		for (int attempt = 0; attempt < 16; attempt++)
		{
			Vector3 dir;
			if (ahead)
			{
				float side = _rng.Randf() < 0.5f ? -1f : 1f;
				dir = look.Rotated(Vector3.Up, side * Mathf.DegToRad(_rng.RandfRange(AheadAngleDegrees.X, AheadAngleDegrees.Y)));
			}
			else
			{
				// A different side each time (Dan, 2026-09-22: it moves around, so its sounds come from all round you):
				// behind, behind-left, behind-right, off to the left, off to the right; never the same sector twice running.
				int sector;
				do sector = _rng.RandiRange(0, SectorDegrees.Length - 1); while (sector == _lastSector && SectorDegrees.Length > 1);
				_trySector = sector;
				dir = (-look).Rotated(Vector3.Up, Mathf.DegToRad(SectorDegrees[sector] + _rng.RandfRange(-22f, 22f)));
			}

			var hit = Ray(eye, eye + dir * (range.Y + 4f));
			if (hit.Count == 0) continue;

			Vector3 at = (Vector3)hit["position"], normal = (Vector3)hit["normal"];
			float d = eye.DistanceTo(at);
			if (Mathf.Abs(normal.Y) > 0.5f || d < range.X || d > range.Y) continue;   // a trunk, not the ground

			// Find the trunk's far side (big trees are over a metre thick): cast back toward us from beyond it.
			Vector3 flatDir = new Vector3(dir.X, 0, dir.Z).Normalized();
			var farHit = Ray(at + flatDir * 3f, at + flatDir * 0.05f);
			Vector3 farSide = farHit.Count > 0 ? (Vector3)farHit["position"] : at + flatDir * 0.6f;

			float lean = _rng.Randf() < 0.5f ? -1f : 1f;
			Vector3 lateral = dir.Cross(Vector3.Up).Normalized() * lean;
			Vector3 spot = farSide + flatDir * 0.45f + lateral * _rng.RandfRange(ExposeOffset.X, ExposeOffset.Y);
			spot.Y = GroundAt(spot);
			if (float.IsNaN(spot.Y) || SilenceAt(spot) > AvoidSilenceAbove || Overlaps(spot)) continue;

			GlobalPosition = spot;
			FacePlayer();
			// Lean out from behind the trunk toward the exposed side.
			_body.Rotation = new Vector3(0, 0, Mathf.DegToRad(LeanDegrees) * -lean);
			if (AnyPointInFrustum(cam)) continue;   // never pop in on screen
			// Only part of it may show from where you stand: at least a sliver, never more than a few points.
			int exposed = ExposedPoints(cam.GlobalPosition);
			if (exposed == 0 || exposed > MaxExposedPoints) continue;

			_hideDir = -lateral;
			_ahead = ahead;
			_lastSector = ahead ? AheadSector : _trySector;
			_sectorsUsed.Add(_lastSector);
			Current = State.Peeking;
			PeekCount++;
			if (ahead) DistantCount++;
			_seenThisPeek = false;
			_stungThisPeek = false;
			_seenTime = 0;
			_stayTimer = ahead ? 25f : _rng.RandfRange(RelocateNow.X, RelocateNow.Y);
			// Give it a moment at its new tree before its steps start answering yours.
			_nextShadow = _clock + (Intro ? _rng.RandfRange(0.8f, 3f) : _rng.RandfRange(3f, 10f));
			_linger = _rng.Randf() < LongLingerChance ? LongLingerSeconds : _rng.RandfRange(LingerSeconds.X, LingerSeconds.Y);
			SetVisibility(MaxVisibility);
			return true;
		}
		return false;
	}

	/// <summary>One world-layer ray (excluding the player) through the shared query object.</summary>
	private Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to)
	{
		_ray.From = from;
		_ray.To = to;
		return GetWorld3D().DirectSpaceState.IntersectRay(_ray);
	}

	/// <summary>True while it is out there (peeking or dissolving), for debug display.</summary>
	public bool IsPresent => Current is State.Peeking or State.Vanishing;

	/// <summary>World positions of its sample points (head, shoulders, knees...), for debug display.</summary>
	public IEnumerable<Vector3> WorldSamplePoints()
	{
		foreach (var local in SamplePoints) yield return _body.GlobalTransform * local;
	}

	/// <summary>True if a body standing at <paramref name="feet"/> would be inside a trunk, rock or log.</summary>
	private bool Overlaps(Vector3 feet)
	{
		// Lifted clear of the ground so the terrain itself doesn't count.
		_overlap.Transform = new Transform3D(Basis.Identity, feet + Vector3.Up * 1.3f);
		return GetWorld3D().DirectSpaceState.IntersectShape(_overlap, 1).Count > 0;
	}

	/// <summary>How many of its sample points have a clear line of sight from <paramref name="from"/> (ignoring where you look).</summary>
	private int ExposedPoints(Vector3 from)
	{
		if (_ray == null) return 0;
		int n = 0;
		var xf = _body.GlobalTransform;
		foreach (var local in SamplePoints)
			if (Ray(from, xf * local).Count == 0) n++;
		return n;
	}

	private float VisibleFraction(Camera3D cam)
	{
		if (_ray == null) return 0f;
		Vector3 from = cam.GlobalPosition;
		if (from.DistanceTo(GlobalPosition) > 60f) return 0f;
		int visible = 0;
		var xf = _body.GlobalTransform;
		foreach (var local in SamplePoints)
		{
			Vector3 p = xf * local;
			if (!cam.IsPositionInFrustum(p)) continue;
			if (Ray(from, p).Count == 0) visible++;
		}
		return visible / (float)SamplePoints.Length;
	}

	private bool AnyPointInFrustum(Camera3D cam)
	{
		var xf = _body.GlobalTransform;
		foreach (var local in SamplePoints)
			if (cam.IsPositionInFrustum(xf * local)) return true;
		return false;
	}

	private void FacePlayer()
	{
		Vector3 to = _player.GlobalPosition - GlobalPosition;
		Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);
	}

	private void SetVisibility(float v)
	{
		_visibility = Mathf.Clamp(v, 0f, 1f);
		foreach (var m in _skins) m.SetShaderParameter("visibility", _visibility);
		Visible = _visibility > 0f;
	}

	/// <summary>The player's current ground speed (from their position, so scripted moves count too).</summary>
	private float TrackSpeed(float dt)
	{
		Vector3 p = _player.GlobalPosition;
		float moved = _hasLastPos ? Flat(p).DistanceTo(Flat(_lastPlayerPos)) : 0f;
		_lastPlayerPos = p; _hasLastPos = true;
		if (moved > 5f) return 0f;   // a teleport, not a step
		return moved / Mathf.Max(dt, 0.0001f);
	}

	/// <summary>Its sounds only come from where it actually is, and only while it's out there unseen.</summary>
	private bool Present => Current == State.Peeking && !_seenThisPeek;

	private void UpdateSounds(float speed)
	{
		if (Current == State.Dormant) return;

		// Echo steps: you stop, and from behind its tree, one more step (two, when it was already following).
		if (speed > 1.2f) _walkedFor += (float)GetProcessDeltaTime();
		else if (speed < 0.2f && _walkedFor >= (Intro ? 0.8f : 1.5f))
		{
			_walkedFor = 0f;
			bool wasShadowing = _shadowLeft > 0;
			_shadowLeft = 0;   // you stopped; so did it
			if (Present && _clock >= _nextEchoAllowed && _rng.Randf() < (wasShadowing ? 0.85f : EchoStepChanceNow))
			{
				_nextEchoAllowed = _clock + EchoStepCooldownNow;
				QueueSteps(wasShadowing && _rng.Randf() < 0.5f ? 2 : 1, _rng.RandfRange(0.35f, 0.8f));
			}
		}
		if (!Present) { _shadowLeft = 0; return; }

		// While you walk, its steps shadow yours a beat behind, from its tree (see OnPlayerStepped): on the
		// Hollow path walk almost all the time, later now and then.
		if (speed > 1.2f && _shadowLeft == 0 && _clock >= _nextShadow)
		{
			_nextShadow = _clock + _rng.RandfRange(ShadowIntervalNow.X, ShadowIntervalNow.Y);
			_shadowLeft = _rng.RandiRange(ShadowStepsNow.X, ShadowStepsNow.Y);
			_spellDb = _rng.RandfRange(-4f, 2f);   // each spell at its own level
			ShadowSpells++;
		}
	}

	/// <summary>For tests: the player's steps that fell inside a shadow spell, and its answers to them.</summary>
	public int ShadowStepsHeard { get; private set; }
	public int ShadowStepsAnswered { get; private set; }
	/// <summary>For tests: how many different sides of the player it has stood on (up to 6: five sectors and ahead).</summary>
	public int DirectionsUsed => _sectorsUsed.Count;

	/// <summary>While shadowing: each of your footsteps is answered a beat (120-220 ms) later from its tree, with the
	/// same sample the player would get on the ground under it, so the two sets of steps sound like one pair of feet.</summary>
	private void OnPlayerStepped()
	{
		if (Inert || _shadowLeft <= 0 || !Present) return;
		// Never while the player runs, and only every second step, 380-520 ms behind: a separate walker
		// out of phase with you, not a clip-clop on your own stride (Dan, 2026-09-22: it sounded like hooves).
		if (_player.IsRunning) return;
		_stepParity = !_stepParity;
		if (!_stepParity) return;
		_shadowLeft--;
		ShadowStepsHeard++;
		var stream = PickStep();
		if (stream == null) return;
		Vector3 p = GlobalPosition + new Vector3(_rng.RandfRange(-0.3f, 0.3f), 0.1f, _rng.RandfRange(-0.3f, 0.3f));
		_pendingSteps.Add((_clock + _rng.RandfRange(0.38f, 0.52f), p, stream, true));
	}
	private bool _stepParity;

	private void QueueSteps(int count, float firstDelay)
	{
		double at = _clock + firstDelay;
		for (int i = 0; i < count; i++, at += _rng.RandfRange(0.55f, 0.7f))
		{
			var stream = PickStep();
			if (stream != null) _pendingSteps.Add((at, GlobalPosition + Vector3.Up * 0.1f, stream, false));
		}
	}

	private void PlayPendingSteps()
	{
		for (int i = _pendingSteps.Count - 1; i >= 0; i--)
		{
			if (_pendingSteps[i].at > _clock) continue;
			// The player's sample, a touch heavier: pitched 0.9-0.97 and +1 dB.
			Play(_pendingSteps[i].stream, _pendingSteps[i].pos, StepVolumeDb - 1f + (_pendingSteps[i].shadow ? _spellDb : 0f), _rng.RandfRange(0.9f, 0.97f));
			if (_pendingSteps[i].shadow) ShadowStepsAnswered++;
			_pendingSteps.RemoveAt(i);
		}
	}
	private float _spellDb;

	private void Play(AudioStream stream, Vector3 at, float db, float pitch)
	{
		if (Inert) return;   // nothing of it sounds inside the bunker
		var v = _voices[_nextVoice];
		_nextVoice = (_nextVoice + 1) % _voices.Count;
		v.GlobalPosition = at;
		v.Stream = stream;
		v.VolumeDb = db;
		v.PitchScale = pitch;
		v.Play();
		SoundCount++;
	}

	private ForestTerrainRef _terrain;
	private sealed class ForestTerrainRef { public Node Node; public bool Searched; }

	private float GroundAt(Vector3 p)
	{
		_terrain ??= new ForestTerrainRef();
		if (!_terrain.Searched)
		{
			_terrain.Searched = true;
			if (GetTree().GetFirstNodeInGroup("terrain") is Node t && t.HasMethod("HeightAt")) _terrain.Node = t;
		}
		if (_terrain.Node != null && IsInstanceValid(_terrain.Node))
			return _terrain.Node.Call("HeightAt", p.X, p.Z).AsSingle();
		var hit = Ray(p + Vector3.Up * 30f, p + Vector3.Down * 30f);
		return hit.Count > 0 ? ((Vector3)hit["position"]).Y : float.NaN;
	}

	private static float SilenceAt(Vector3 p)
	{
		float s = 0f;
		var zones = SilenceZone.All;
		for (int i = 0; i < zones.Count; i++) s = Mathf.Max(s, zones[i].SilenceAt(p));
		return s;
	}

	private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
}
