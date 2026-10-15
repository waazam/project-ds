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
public partial class Stalker : Node3D, ShaderWarmup.IWarmUp
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
	/// <summary>(Unused since 2026-10-15: its steps are set relative to the player's own, <see cref="ShadowRelDb"/>.)</summary>
	[Export] public float StepVolumeDb = 3f;
	/// <summary>(2026-10-15, the stalker pass: its steps were ~25 dB over the player's own, "giant" at 15 m) How loud its
	/// steps are heard, in dB relative to the player's own footstep, whatever the distance (Godot's falloff is cancelled
	/// up to 12 dB): later, on the Hollow path walk, and the one step after you stop.</summary>
	[Export] public float ShadowRelDb = -6f, IntroShadowRelDb = -3f, EchoRelDb = -2f;

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
	[Export] public float IntroEchoStepChance = 0.5f;   // (2026-10-15: was 0.8 / 6 s: at almost every stop, learned in a minute)
	[Export] public float IntroEchoStepCooldown = 15f;
	[Export] public float IntroRattleRange = 44f;   // (2026-10-12: heard further off, was 34)
	/// <summary>Seconds after waking before it first stands behind a tree.</summary>
	[Export] public Vector2 IntroFirstAppearance = new(8f, 13f);

	[ExportGroup("Voice")]
	/// <summary>The rattle (a dry clicking croak from its tree) is inaudible beyond this and grows as you close in.</summary>
	[Export] public float RattleRange = 34f;   // (was 26)
	/// <summary>Level of the rattle when it is right on you.</summary>
	[Export] public float RattleMaxDb = 6f;

	public State Current { get; private set; } = State.Dormant;

	/// <summary>How this glimpse goes: gone in a blink when caught (most); a held stare, then a slow slide back behind its
	/// trunk; or, far ahead, standing watching for a few seconds.</summary>
	public enum GlimpseKind { Flick, Stare, Watching }
	public GlimpseKind Glimpse { get; private set; }
	public readonly HashSet<GlimpseKind> GlimpsesSeen = new();
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

	/// <summary>The storm walk's first stretch: it is only heard (its rattle from its tree, its steps answering theirs), never
	/// seen, until they're well on toward the cabin (the owner, 2026-10-07: "he should be hidden until much later"). Its body
	/// is drawn only from <see cref="SeenFromShare"/> of the way there on.</summary>
	public bool SoundOnly
	{
		get
		{
			if (StoryManager.Instance is not { } s || s.Current >= Checkpoint.Act5CabinEntered || _player == null) return false;
			_cabin ??= GetTree().GetFirstNodeInGroup("cabin") as Node3D;
			if (_cabin == null) return false;
			float d = Flat(_player.GlobalPosition).DistanceTo(Flat(_cabin.GlobalPosition));
			if (_walkStartDist < 0f) _walkStartDist = Mathf.Max(d, 1f);
			return d > _walkStartDist * (1f - SeenFromShare);
		}
	}
	/// <summary>The share of the storm walk (camp to cabin) before it may be seen.</summary>
	[Export] public float SeenFromShare = 0.6f;
	private Node3D _cabin;
	private float _walkStartDist = -1f;
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
	/// <summary>How long the lantern has been off (the woods, at night: it comes closer in the dark).</summary>
	public float DarkFor => _darkFor;
	private float _darkFor;
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
	private Vector3 _warmFrom;
	private bool _warmVisible;

	/// <summary>The shader warm-up: drawn once, fully dissolved, in front of the camera (its skinning and shader made
	/// ready, so its first sight never stutters), then put back as it was.</summary>
	public void WarmUp(Camera3D cam, bool on)
	{
		if (on)
		{
			_warmFrom = GlobalPosition; _warmVisible = Visible;
			GlobalPosition = cam.GlobalPosition - cam.GlobalBasis.Z * 4f - Vector3.Up * 1.5f;
			foreach (var m in _skins) m.SetShaderParameter("visibility", 0.001f);
			Visible = true;
		}
		else
		{
			GlobalPosition = _warmFrom;
			SetVisibility(_visibility);
			Visible = _warmVisible && _visibility > 0f;
		}
	}

	/// <summary>Its body (the remodel: StalkerBody's skeleton and clips).</summary>
	public StalkerBody Body => _body as StalkerBody;
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
	private AudioStream[][] _phrases;
	private RecentPicker[] _phrasePick;
	private readonly List<AudioStreamPlayer3D> _rattleVoices = new();
	private int _nextRattleVoice;
	private double _phraseWait;
	private float _phantomBearing = float.NaN, _phantomDist = 7f;
	private Vector3 _soundAt;
	private static AudioEffectLowPassFilter _busLow;
	private static AudioEffectReverb _busVerb;
	public const string BusName = "Stalker";

	/// <summary>Its own bus (2026-10-15, the stalker pass), sent to Unnatural, made at run time if the layout doesn't have
	/// it: a low-pass it sets every frame by where it is relative to where you look (Godot's stereo panner has no front or
	/// back: behind you it dulls toward ~2.4 kHz, the cue the ear uses), and a little reverb, more the further off (the
	/// same night air as the player's own steps).</summary>
	private static void EnsureBus()
	{
		int b = AudioServer.GetBusIndex(BusName);
		if (b < 0)
		{
			AudioServer.AddBus();
			b = AudioServer.BusCount - 1;
			AudioServer.SetBusName(b, BusName);
			AudioServer.SetBusSend(b, "Unnatural");
		}
		_busLow = null; _busVerb = null;
		for (int i = 0; i < AudioServer.GetBusEffectCount(b); i++)
		{
			if (AudioServer.GetBusEffect(b, i) is AudioEffectLowPassFilter lp) _busLow = lp;
			else if (AudioServer.GetBusEffect(b, i) is AudioEffectReverb rv) _busVerb = rv;
		}
		if (_busLow == null) { _busLow = new AudioEffectLowPassFilter { CutoffHz = 20000f, Resonance = 0.5f }; AudioServer.AddBusEffect(b, _busLow); }
		if (_busVerb == null) { _busVerb = new AudioEffectReverb { RoomSize = 0.45f, Damping = 0.6f, Wet = 0.05f, Dry = 1f, PredelayMsec = 20f }; AudioServer.AddBusEffect(b, _busVerb); }
	}

	private void UpdateBus(float dt)
	{
		if (_busLow == null || _player == null) return;
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		Vector3 to = _soundAt - cam.GlobalPosition; to.Y = 0;
		Vector3 fwd = -cam.GlobalBasis.Z; fwd.Y = 0;
		float behind = to.LengthSquared() > 0.01f && fwd.LengthSquared() > 0.01f ? Mathf.SmoothStep(-0.1f, -0.7f, fwd.Normalized().Dot(to.Normalized())) : 0f;
		float want = Mathf.Lerp(20000f, 2400f, behind);
		_busLow.CutoffHz = Mathf.Lerp(_busLow.CutoffHz, want, 1f - Mathf.Exp(-dt / 0.12f));
		float far = Mathf.Clamp(to.Length() / 40f, 0f, 1f);
		_busVerb.Wet = Mathf.Lerp(0.05f, 0.15f, far);
	}
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
		AddToGroup(Audio.DreadDrone.Group);   // (2026-10-10: the dread, while it's out at its tree, unseen)
		AddToGroup("stalker");
		AddToGroup("warm_up_self");
		// the storm walk's rare noises in the dark (beside it, not of it: it makes no sounds but its own)
		Callable.From(() => { var root = GetTree().CurrentScene; if (root != null && root.GetNodeOrNull("StormSurprises") == null) root.AddChild(new World.StormSurprises { Name = "StormSurprises" }); }).CallDeferred();
		World.PhotoSubject.Attach(this, "stalker", new Vector3(0, 1.5f, 0), 1.5f, 70f, 12f, true, new Vector3(0, 2.2f, 0));
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
		_stepSets["mud"] = LoadSet("res://assets/audio/sfx/step_mud_{0:00}.wav", 6);
		_stepSets["leaves"] = LoadSet("res://assets/audio/sfx/step_leaves_{0:00}.wav", 6);
		_stepSets["root"] = LoadSet("res://assets/audio/sfx/step_root_{0:00}.wav", 4);
		_stepSets["leaves_dry"] = LoadSet("res://assets/audio/sfx/step_dryleaves_{0:00}.wav", 6);
		_stepSets["twig"] = LoadSet("res://assets/audio/sfx/step_twig_{0:00}.wav", 4);
		// No twig snaps from it (Dan, 2026-09-22: a dry snap reads as a distant gunshot; the rattle is its sound now).
		if (ResourceLoader.Exists("res://assets/audio/sfx/stalker_seen_01.wav"))
			_sting = GD.Load<AudioStream>("res://assets/audio/sfx/stalker_seen_01.wav");
		EnsureBus();
		for (int i = 0; i < 4; i++)
		{
			var p = new AudioStreamPlayer3D { Bus = BusName, UnitSize = 16f, MaxDistance = 110f, TopLevel = true };   // carries: you should hear it and turn
			// (2026-10-15) its own occlusion, once per step (OcclusionFor), not the pooled voice's eased muffle from wherever it
			// last played: a crisp step then a dull thud read as two different walkers
			p.SetMeta("no_occlusion", true);
			AddChild(p);
			_voices.Add(p);
		}
		_stingVoice = new AudioStreamPlayer { Bus = "Unnatural" };
		AddChild(_stingVoice);
		// Its only voice is the rattle (Dan, 2026-09-22: no growl, snarl or screech, ever; it makes no sound when it goes,
		// it is just gone). (2026-10-15, the stalker pass) As phrases, not the one long loop: 48 takes in four families
		// (creature_rattle_tick/run/long/stutter), each starting on a click, played one at a time from where it is
		// (UpdateRattle), its rate in which phrase comes and the gaps between them, never in pitch.
		_phrases = new[]
		{
			LoadSet("res://assets/audio/sfx/creature_rattle_tick_{0:00}.wav", 12),
			LoadSet("res://assets/audio/sfx/creature_rattle_run_{0:00}.wav", 16),
			LoadSet("res://assets/audio/sfx/creature_rattle_long_{0:00}.wav", 12),
			LoadSet("res://assets/audio/sfx/creature_rattle_stutter_{0:00}.wav", 8),
		};
		_phrasePick = new RecentPicker[_phrases.Length];
		for (int i = 0; i < 2; i++)
		{
			// (2026-10-15, the owner: "the rattle can be louder") less falloff: about +5 dB at 15 m
			var v = new AudioStreamPlayer3D { Bus = BusName, UnitSize = 14f, MaxDistance = 80f, TopLevel = true };
			v.SetMeta("no_occlusion", true);   // its own, once per phrase
			AddChild(v);
			_rattleVoices.Add(v);
		}
		_rattle = _rattleVoices[0];
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
		return PlayerFootsteps.ForestFloorAt(GlobalPosition);   // (the wet floor in the storm, as under theirs)
	}

	private AudioStream PickStep()
	{
		if (!_stepSets.TryGetValue(SurfaceUnderIt(), out var set) || set.Length == 0) set = _stepSets["dirt"];
		if (set.Length == 0) return null;
		// (2026-10-15) never the sample the player's own foot just played: two identical takes a beat apart read as an echo
		var mine = _player?.Footsteps?.LastStep;
		var pick = set[_stepPicker.Next(_rng, set.Length)];
		if (pick == mine && set.Length > 1) pick = set[_stepPicker.Next(_rng, set.Length)];
		return pick;
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
			foreach (var v in _rattleVoices) if (v.Playing) v.Stop();
			return;
		}
		PlayPendingSteps();
		UpdateRattle(dt);
		UpdateBus(dt);
		if (!_awake)
		{
			if (!ShouldWake()) { Current = State.Dormant; return; }
			_awake = true;
			// Its introduction: the first tree within seconds of the lantern and compass being taken.
			_cooldown = _rng.RandfRange(IntroFirstAppearance.X, IntroFirstAppearance.Y);
		}
		if (Current == State.Dormant) Current = State.Hidden;
		TickDirector(dt);
		// Unseen, it grows bolder: its trees creep closer over time. (2026-10-10) And in the dark: the lantern left off out
		// here more than half a minute, it comes on three times as fast, and sooner to its next tree.
		bool lit = _player.GetNodeOrNull<ProjectDS.Player.Lantern>("Lantern") is { IsOn: true };
		_darkFor = lit ? 0f : _darkFor + dt;
		float dark = Mathf.Clamp((_darkFor - 30f) / 20f, 0f, 1f);
		Tension = Mathf.Min(1f, Tension + dt / CloseInNow * (1f + 2f * dark));
		if (dark > 0f && Current == State.Hidden) _cooldown -= dt * 0.8f * dark;

		bool withdraw = SilenceAt(_player.GlobalPosition) > RetreatAtSilence;
		switch (Current)
		{
			case State.Hidden:
				_cooldown -= dt;
				if (!withdraw && _cooldown <= 0f && Pacing != Phase.Relax)
				{
					bool placed = _rng.Randf() < AheadChanceNow && TryPlace(cam, ahead: true);
					if (!placed && !TryPlace(cam, ahead: false)) _cooldown = Intro ? 1.5f : 3f;
				}
				break;
			case State.Peeking:
				UpdatePeeking(cam, dt, withdraw);
				break;
			case State.Vanishing:
				// Gone the instant it is caught: a 0.1 s dissolve, no sound of any kind (a held glimpse goes slowly: it slides
				// back behind its trunk over most of a second, and is gone)
				bool slow = Glimpse != GlimpseKind.Flick;
				SetVisibility(_visibility - dt * MaxVisibility / (slow ? 0.7f : VanishSeconds));
				GlobalPosition += _hideDir * DuckSpeed * (slow ? 0.35f : 1f) * dt;
				if (_visibility <= 0f) Hide(_rng.RandfRange(CooldownNow.X, CooldownNow.Y));
				break;
		}

		if (!withdraw) UpdateSounds(speed);
	}

	private void UpdatePeeking(Camera3D cam, float dt, bool withdraw)
	{
		float dist = Flat(GlobalPosition).DistanceTo(Flat(_player.GlobalPosition));
		float seen = VisibleFraction(cam);
		if (_body is StalkerBody sbw)
		{
			sbw.TrackTarget = cam.GlobalPosition;
			sbw.Watched = seen > 0f;
			Creaks(sbw, dist, seen > 0f, dt);
		}
		if (seen > 0f)
		{
			if (!_seenThisPeek) { _seenThisPeek = true; SeenCount++; Tension = 0.1f; GlimpsesSeen.Add(Glimpse); }
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
		(_body as StalkerBody)?.Duck();   // snatched back behind its trunk as it goes (the remodel's clip)
		OnSighted();   // (2026-10-15: seen, its peak is over and a relax follows; silence is its reaction, not a rattle)
	}

	/// <summary>
	/// The rattle: a dry clicking croak that plays from its tree while it stands there unseen, silent
	/// beyond <see cref="RattleRange"/> and rising (louder, quicker) the closer you come. Seen, or
	/// gone, it drops away at once.
	/// </summary>
	private void UpdateRattle(float dt)
	{
		if (_phrases == null || _rattleVoices.Count == 0 || _player == null) return;
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
			_phantomBearing = float.NaN;
		}
		else if (_awake && !Indoors && urg > 0f)
		{
			// Not at a tree, but not gone either: the clicking comes from behind you. (2026-10-15, the stalker pass: it used
			// to sit at camera.back * 5 every frame, turning with your head, a stereo effect you could never face.) Now a
			// bearing that eases round toward behind you at 40 deg/s, 6-9 m off: turn fast and it's at your side a moment,
			// then creeps back round behind you, as if moving round you.
			target = (hunted ? 0.85f : 0.45f) * urg;
			var cam = GetViewport().GetCamera3D();
			Vector3 back = cam != null ? cam.GlobalBasis.Z : Vector3.Back; back.Y = 0;
			float want = Mathf.Atan2(back.X, back.Z);
			if (float.IsNaN(_phantomBearing)) { _phantomBearing = want + _rng.RandfRange(-0.6f, 0.6f); _phantomDist = _rng.RandfRange(6f, 9f); }
			float diff = Mathf.AngleDifference(_phantomBearing, want);
			_phantomBearing += Mathf.Clamp(diff, -Mathf.DegToRad(40f) * dt, Mathf.DegToRad(40f) * dt);
			at = _player.GlobalPosition + new Vector3(Mathf.Sin(_phantomBearing), 0, Mathf.Cos(_phantomBearing)) * _phantomDist + Vector3.Up * 1.5f;
			intensity = 0.35f + 0.4f * urg;
		}
		if (hunted) intensity = Mathf.Max(intensity, 0.75f);
		_intensity = Mathf.MoveToward(_intensity, intensity, dt * 1f);
		// EPISODES (Dan, 2026-09-22: "it can rattle a lot at certain times but not all the dang time"): long quiet
		// stretches with no rattle at all (25-60 s on the path walk, 20-45 s later; its steps may still come), then an
		// episode of 10-20 s where it rattles a lot, bursts on 1-2.5 s and off 0.5-1.5 s, louder and quicker toward the
		// end, then silence again. Being very close, or having just ducked away from your eyes, starts a short episode.
		_episodeTimer -= dt;
		bool closeNow = Present && !Indoors && intensity > 0.8f;
		// (2026-10-15) episodes only in its peak (TickDirector): none while it builds, nothing at all while it relaxes
		bool peak = Pacing == Phase.Peak;
		if (!_episodeOn && closeNow && peak && _clock >= _episodeAllowedAt) StartEpisode(_rng.RandfRange(6f, 10f));
		_episodeRequested = false;
		if (_episodeOn && !peak) EndEpisode();
		if (_episodeTimer <= 0f)
		{
			if (_episodeOn) EndEpisode();
			else if (peak) StartEpisode(_rng.RandfRange(14f, 26f));   // (2026-10-12: longer, was 10-20 s)
			else _episodeTimer = 1f;
		}
		float episodeFrac = _episodeOn ? Mathf.Clamp(1f - _episodeTimer / Mathf.Max(_episodeLength, 0.1f), 0f, 1f) : 0f;
		// (2026-10-15, the stalker pass) PHRASES, one at a time: the family by how close/urgent (a few far ticks .. runs ..
		// long runs and stutters close), each from where it is at that moment, the gap between them shrinking over the
		// episode (1.2-2.5 s at its start, 0.3-0.9 s at its end). Its steps and its clicks take turns rather than lock each
		// other out (it used to hold the rattle off for every spell of steps, so on the path walk it was hardly ever let in):
		// a phrase never starts within 0.25 s of one of its steps.
		_phraseWait -= dt;
		bool stepSoon = false;
		foreach (var s in _pendingSteps) if (s.shadow && s.at - _clock < 0.25) { stepSoon = true; break; }
		if (_episodeOn && target > 0.03f && _phraseWait <= 0 && !Inert)
		{
			if (stepSoon) _phraseWait = 0.3;
			else
			{
				int fam = _intensity < 0.3f ? 0 : _intensity < 0.7f ? 1 : (_rng.Randf() < 0.65f ? 2 : 3);
				float u = _rng.Randf();
				_burstDb = u < 0.33f ? _rng.RandfRange(0f, 2f) : u < 0.7f ? _rng.RandfRange(-3f, 0f) : _rng.RandfRange(-6f, -3f);
				// one sum with a hard cap (it used to stack to ~+20 dB into the limiter)
				float db = RattleMaxDb - 4f + 4f * _intensity + 2f * urg + _burstDb + 3f * episodeFrac + Mathf.LinearToDb(Mathf.Max(target, 0.001f));
				double len = PlayPhrase(fam, at, db);
				// (the path walk, its introduction: denser; later the gaps open out)
				_phraseWait = len + (Intro ? Mathf.Lerp(_rng.RandfRange(0.5f, 1.2f), _rng.RandfRange(0.15f, 0.45f), episodeFrac) : Mathf.Lerp(_rng.RandfRange(1.2f, 2.5f), _rng.RandfRange(0.3f, 0.9f), episodeFrac));
			}
		}
		// Seen, gone or out of range: what's playing fades out over ~80 ms (silence is its reaction; not a hard cut)
		bool silent = target <= 0.03f || Inert || (!_episodeOn && !Present);
		bool playing = false;
		foreach (var v in _rattleVoices)
		{
			if (!v.Playing) continue;
			if (silent) { v.VolumeDb -= dt * 600f; if (v.VolumeDb < -60f) { v.Stop(); continue; } }
			playing = true;
		}
		RattleLevel = Mathf.MoveToward(RattleLevel, playing ? target : 0f, dt * (playing ? 4f : 6f));
		// For the test: how much of the time the rattle is actually audible.
		_rattleClock += dt;
		if (RattleLevel > 0.05f) _rattleAudible += dt;
	}

	/// <summary>One rattle phrase of family <paramref name="fam"/> (0 tick, 1 run, 2 long, 3 stutter) from <paramref name="at"/>,
	/// at <paramref name="db"/> (capped +8, the audio audit's limit), its own occlusion, the next voice. Returns how long it lasts.</summary>
	private double PlayPhrase(int fam, Vector3 at, float db)
	{
		if (_phrases == null || _rattleVoices.Count == 0 || Inert) return 0;
		var set = _phrases[fam].Length > 0 ? _phrases[fam] : _phrases[1];
		if (set.Length == 0) return 0;
		var stream = set[_phrasePick[fam].Next(_rng, set.Length)];
		RattleBursts++;
		bool blocked = Blocked(at);
		float pitch = _rng.RandfRange(0.95f, 1.05f);
		var v = _rattleVoices[_nextRattleVoice];
		_nextRattleVoice = (_nextRattleVoice + 1) % _rattleVoices.Count;
		v.GlobalPosition = at;
		_soundAt = at;
		v.Stream = stream;
		v.VolumeDb = Mathf.Min(8f, db) - (blocked ? 2f : 0f);
		v.PitchScale = pitch;
		v.AttenuationFilterCutoffHz = blocked ? 3000f : 20500f;
		v.Play();
		_rattle = v;
		Heard();
		return stream.GetLength() / pitch;
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
		// (2026-10-12, the owner: "needs to make more of those grudge clocking noises we originally had in older builds")
		// shorter silences between its episodes (were 25-60 s on the path walk, 20-45 s later)
		_episodeTimer = Intro ? _rng.RandfRange(12f, 26f) : _rng.RandfRange(9f, 20f);
		_episodeAllowedAt = _clock + 8f;   // a close or a sighting cannot restart it at once
	}

	private bool _burstOn = false, _episodeOn = false, _episodeRequested = false;
	private float _intensity;
	private float _burstTimer = 0f, _burstDb;
	private float _episodeTimer = 5f, _episodeLength = 1f;   // the first quiet stretch is short: it is heard early, then goes quiet
	private double _episodeAllowedAt, _rattleClock, _rattleAudible;
	/// <summary>For tests: how many rattle bursts have started.</summary>
	public int RattleBursts { get; private set; }
	/// <summary>For tests: how many rattle episodes (the stretches where it rattles a lot) have started.</summary>
	public int RattleEpisodes { get; private set; }
	/// <summary>For tests: 0..1, the share of its awake time with the rattle audible.</summary>
	public float RattleAudibleFraction => _rattleClock > 1 ? (float)(_rattleAudible / _rattleClock) : 0f;

	/// <summary>Its hand on the bark, heard (the horror pass): now and then, unwatched and near, the bark creaks under
	/// its weight or its nails drag on it, from where the hand is. Never a voice (Dan, 2026-09-22); never as it goes.</summary>
	private void Creaks(StalkerBody sb, float dist, bool seen, float dt)
	{
		if (seen || sb.GripPoint is not { } at || dist > 18f) return;
		if ((_creakIn -= dt) > 0f) return;
		_creakIn = _rng.RandfRange(5f, 12f);
		_creaks ??= new[] { "claw_scrape_01", "claw_scrape_02" };   // (2026-10-15, the owner: no tree creaks; its claws on the bark stay)
		string path = $"res://assets/audio/sfx/{_creaks[_rng.RandiRange(0, _creaks.Length - 1)]}.wav";
		if (!ResourceLoader.Exists(path)) return;
		Play(GD.Load<AudioStream>(path), at, -19f + 6f * (1f - dist / 18f), _rng.RandfRange(0.8f, 0.95f));
		CreakCount++;
	}
	private float _creakIn = 3f;
	private static string[] _creaks;
	/// <summary>For tests: creaks and scrapes from its hand on the bark.</summary>
	public int CreakCount { get; private set; }

	private void Hide(float cooldown)
	{
		if (_body is StalkerBody sbh) { sbh.Watched = false; sbh.TrackTarget = null; }
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
				// (and the sides it hasn't come from lately first: it works its way round you)
				int sector;
				int tries = 0;
				do sector = _rng.RandiRange(0, SectorDegrees.Length - 1);
				while ((sector == _lastSector || (_recentSectors.Contains(sector) && tries < 6)) && SectorDegrees.Length > 1 && tries++ < 12);
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
			// how it's found this time (the horror pass): round the side, folded low at the foot, turned away with its
			// head right round, or up the trunk clinging to it (its introduction always round the side)
			var sb = _body as StalkerBody;
			bool rig = sb is { Rigged: true };
			var kind = rig ? PickKind() : StalkerBody.PeekKind.Side;
			float size = sb?.Size ?? 1.1f;
			// (a cling needs a trunk tall enough to hold at its height: checked first, else round the side)
			if (kind == StalkerBody.PeekKind.Cling)
			{
				Vector3 high = farSide + flatDir * 0.3f + Vector3.Up * (GroundAt(farSide) - farSide.Y + (1.35f + 1.92f) * size);
				var up = Ray(high - flatDir * 1.6f, high + flatDir * 0.2f);
				if (up.Count == 0) kind = StalkerBody.PeekKind.Side;
			}
			Vector3 spot = kind == StalkerBody.PeekKind.Cling
				? farSide + flatDir * 0.3f + lateral * 0.04f
				: farSide + flatDir * 0.45f + lateral * _rng.RandfRange(ExposeOffset.X, ExposeOffset.Y);
			spot.Y = GroundAt(spot);
			if (float.IsNaN(spot.Y) || SilenceAt(spot) > AvoidSilenceAbove || (kind != StalkerBody.PeekKind.Cling && Overlaps(spot))) continue;

			_owl = kind == StalkerBody.PeekKind.Owl;
			GlobalPosition = spot + Vector3.Up * (kind == StalkerBody.PeekKind.Cling ? 1.35f * size : 0f);
			FacePlayer();
			// Lean out from behind the trunk toward the exposed side.
			_body.Rotation = new Vector3(0, 0, Mathf.DegToRad(LeanDegrees) * -lean);
			if (rig)
			{
				// its hands on the bark: the near one at the trunk's edge (round both edges, clinging); the elbow now and
				// then bent the wrong way
				float h = kind switch { StalkerBody.PeekKind.Low => 0.86f, StalkerBody.PeekKind.Cling => 1.92f, _ => 1.72f };
				Vector3? grip = kind == StalkerBody.PeekKind.Owl ? null : FindGrip(lateral, size, h);
				Vector3? far = kind == StalkerBody.PeekKind.Cling ? FindGrip(-lateral, size, h) : null;
				if (kind == StalkerBody.PeekKind.Cling && (grip == null || far == null)) { far = null; kind = StalkerBody.PeekKind.Side; grip = FindGrip(lateral, size, 1.72f); GlobalPosition = spot; }
				sb.Peek(lateral, grip, kind, far, kind == StalkerBody.PeekKind.Side && _rng.Randf() < 0.3f);
			}
			if (AnyPointInFrustum(cam)) continue;   // never pop in on screen
			// Only part of it may show from where you stand: at least a sliver, never more than a few points.
			int exposed = ExposedPoints(cam.GlobalPosition);
			if (exposed == 0 || exposed > MaxExposedPoints) continue;

			_hideDir = -lateral;
			_ahead = ahead;
			_lastSector = ahead ? AheadSector : _trySector;
			if (!ahead) { _recentSectors.Enqueue(_trySector); while (_recentSectors.Count > Mathf.Min(3, SectorDegrees.Length - 2)) _recentSectors.Dequeue(); }
			_sectorsUsed.Add(_lastSector);
			if (rig) KindsUsed.Add(kind);
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
			// now and then, a glimpse that's held (rare, so a run sees one or two): it stares back a moment and then slides
			// behind its trunk, deliberately; or far ahead at the fog's edge it just stands, watching, and doesn't go until
			// they come on
			float g = _rng.Randf();
			Glimpse = ahead && g < 0.35f ? GlimpseKind.Watching : g < 0.12f ? GlimpseKind.Stare : GlimpseKind.Flick;
			if (Glimpse == GlimpseKind.Stare) _linger = _rng.RandfRange(1.2f, 2.0f);
			else if (Glimpse == GlimpseKind.Watching) _linger = _rng.RandfRange(2.5f, 4f);
			SetVisibility(SoundOnly ? 0f : MaxVisibility);
			// (2026-10-10) now and then, birds go up out of the trees over where it's come to
			if (!Intro && GlobalPosition.DistanceTo(_player.GlobalPosition) < 32f) World.AmbientLife.MaybeBirds(this, GlobalPosition, 0.3f);
			return true;
		}
		return false;
	}

	/// <summary>Where on its trunk the near hand goes: the trunk's edge on the side that shows, at the hand's height.
	/// Rays from beside it toward the player, stepping out along <paramref name="side"/>: the last that still meets
	/// bark marks the edge. Null if there's no bark there to hold.</summary>
	private Vector3? FindGrip(Vector3 side, float size, float height = 1.72f)
	{
		Vector3 fwd = (GetViewport()?.GetCamera3D()?.GlobalPosition ?? GlobalPosition) - GlobalPosition; fwd.Y = 0; fwd = fwd.Normalized();
		side.Y = 0; side = side.Normalized();
		Vector3 basePos = GlobalPosition + Vector3.Up * height * size;
		Vector3? edge = null;
		for (float x = -0.3f; x <= 0.8f; x += 0.04f)
		{
			Vector3 from = basePos + side * x + fwd * 0.05f;
			var hit = Ray(from, from + fwd * 1.5f);
			if (hit.Count == 0) { if (edge.HasValue) break; continue; }
			edge = (Vector3)hit["position"];
		}
		return edge.HasValue ? edge.Value + side * 0.035f - fwd * 0.02f : null;
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
	public IEnumerable<Vector3> WorldSamplePoints() => Points();

	/// <summary>Its sample points in the world: the remodel's bones as it stands now (a low peek, a cling, a turned
	/// body all put them elsewhere), else the old figure's fixed points.</summary>
	private Vector3[] Points()
	{
		// (round the side, the tuned fixed points, as its rules were set with; low, clinging or turned, its bones)
		if (_body is StalkerBody { Rigged: true } sb && sb.Kind != StalkerBody.PeekKind.Side && sb.SamplePointsWorld() is { } pts) return pts;
		var xf = _body.GlobalTransform;
		var list = new Vector3[SamplePoints.Length];
		for (int i = 0; i < list.Length; i++) list[i] = xf * SamplePoints[i];
		return list;
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
		foreach (var p in Points())
			if (Ray(from, p).Count == 0) n++;
		return n;
	}

	private float VisibleFraction(Camera3D cam)
	{
		if (_ray == null || !Visible) return 0f;   // (heard, not drawn: nothing to see)
		Vector3 from = cam.GlobalPosition;
		if (from.DistanceTo(GlobalPosition) > 60f) return 0f;
		int visible = 0;
		var pts = Points();
		foreach (var p in pts)
		{
			if (!cam.IsPositionInFrustum(p)) continue;
			if (Ray(from, p).Count == 0) visible++;
		}
		return visible / (float)pts.Length;
	}

	private bool AnyPointInFrustum(Camera3D cam)
	{
		foreach (var p in Points())
			if (cam.IsPositionInFrustum(p)) return true;
		return false;
	}

	private void FacePlayer()
	{
		Vector3 to = _player.GlobalPosition - GlobalPosition;
		// (turned away, the owl: its head does the looking)
		Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z) + (_owl ? Mathf.Pi : 0f), 0);
	}
	private bool _owl;
	private readonly Queue<int> _recentSectors = new();

	/// <summary>The ways it has been found so far (tests).</summary>
	public readonly HashSet<StalkerBody.PeekKind> KindsUsed = new();

	private StalkerBody.PeekKind PickKind()
	{
		// its introduction (the storm walk, until the footbridge) is plain: round the side of a trunk; the rest comes after
		if (Intro) return StalkerBody.PeekKind.Side;
		float r = _rng.Randf();
		return r < 0.5f ? StalkerBody.PeekKind.Side : r < 0.68f ? StalkerBody.PeekKind.Low : r < 0.84f ? StalkerBody.PeekKind.Cling : StalkerBody.PeekKind.Owl;
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
			// (2026-10-15) it stopped on your beat: an answer already falling plays, the rest are dropped
			_pendingSteps.RemoveAll(s => s.shadow && s.at - _clock > 0.15);
			if (Present && wasShadowing && Pacing == Phase.Peak && _beatAt < 0) ArmStopBeat();   // the stop beat (TickDirector)
			else if (Present && Pacing != Phase.Relax && _clock >= _nextEchoAllowed && _rng.Randf() < EchoStepChanceNow)
			{
				_nextEchoAllowed = _clock + EchoStepCooldownNow;
				QueueSteps(1, _rng.RandfRange(0.35f, 0.8f));
			}
		}
		if (!Present) { _shadowLeft = 0; return; }

		// While you walk, its steps shadow yours a beat behind, from its tree (see OnPlayerStepped): on the
		// Hollow path walk almost all the time, later now and then.
		if (speed > 1.2f && _shadowLeft == 0 && _clock >= _nextShadow && Pacing != Phase.Relax)
		{
			// (2026-10-15) building, only a short spell (2-4); the full ones in its peak
			_shadowLeft = Pacing == Phase.Build ? _rng.RandiRange(2, 4) : _rng.RandiRange(ShadowStepsNow.X, ShadowStepsNow.Y);
			// (2026-10-15) the gap counts from the spell's END (one answer per two strides), not its start: a new spell used
			// to begin the moment the last ended, steps all the way, and the rattle never let in
			_nextShadow = _clock + _shadowLeft * 2f * _stride + _rng.RandfRange(ShadowIntervalNow.X, ShadowIntervalNow.Y);
			_spellDb = _rng.RandfRange(-2f, 1f);   // each spell at its own level (a narrow spread: it is one walker)
			ShadowSpells++;
		}
	}

	/// <summary>For tests: the player's steps that fell inside a shadow spell, and its answers to them.</summary>
	public int ShadowStepsHeard { get; private set; }
	public int ShadowStepsAnswered { get; private set; }
	/// <summary>For tests: how many different sides of the player it has stood on (up to 6: five sectors and ahead).</summary>
	public int DirectionsUsed => _sectorsUsed.Count;

	/// <summary>Its steps are following yours now (a shadow spell is on).</summary>
	public bool Shadowing => _shadowLeft > 0 && Present;
	/// <summary>For tests: the closest any of its answers came to one of the player's own footfalls (seconds of game
	/// time; 1 until measured). Under ~0.15 s the two fuse into a doubled "clip-clop" on your own feet.</summary>
	public float MinAnswerGap { get; private set; } = 1f;
	/// <summary>For tests: its last answer's level relative to the player's own footstep (dB, as heard).</summary>
	public float LastStepRelDb { get; private set; } = float.NaN;
	private double _lastPlayerStep = -10, _lastAnswer = -10;
	private float _stride = 0.48f, _drift;

	/// <summary>While shadowing, every second of your footsteps is answered from where it is, a separate walker at half
	/// your cadence, its step landing between your feet (2026-10-15, the stalker pass: it used to come 380-520 ms
	/// after the stride it answered, about one whole step period, so it landed on your next foot: the "hooves" again).
	/// The answer comes at 0.44-0.56 of your stride (measured from your own steps), never within 0.15 s of a footfall of
	/// yours, its phase drifting a little over a spell.</summary>
	private void OnPlayerStepped()
	{
		double gap = _clock - _lastPlayerStep;
		if (gap > 0.2 && gap < 1.2) _stride = Mathf.Lerp(_stride, (float)gap, 0.3f);
		_lastPlayerStep = _clock;
		if (_clock - _lastAnswer < 1.0) MinAnswerGap = Mathf.Min(MinAnswerGap, (float)(_clock - _lastAnswer));
		if (Inert || _shadowLeft <= 0 || !Present) return;
		// Never while the player runs, and only every second step (Dan, 2026-09-22: it sounded like hooves).
		if (_player.IsRunning) return;
		_stepParity = !_stepParity;
		if (!_stepParity) return;
		_shadowLeft--;
		ShadowStepsHeard++;
		var stream = PickStep();
		if (stream == null) return;
		_drift = Mathf.Clamp(_drift + _rng.RandfRange(-0.03f, 0.03f), -0.08f, 0.08f);
		float delay = _stride * _rng.RandfRange(0.44f, 0.56f) * (1f + _drift) + _rng.RandfRange(-0.02f, 0.02f);
		delay = Mathf.Clamp(delay, 0.15f, Mathf.Max(0.15f, _stride - 0.15f));
		Vector3 p = GlobalPosition + new Vector3(_rng.RandfRange(-0.3f, 0.3f), 0.3f, _rng.RandfRange(-0.3f, 0.3f));
		_pendingSteps.Add((_clock + delay, p, stream, true));
	}
	private bool _stepParity;

	private void QueueSteps(int count, float firstDelay)
	{
		double at = _clock + firstDelay;
		for (int i = 0; i < count; i++, at += _rng.RandfRange(0.55f, 0.7f))
		{
			var stream = PickStep();
			if (stream != null) _pendingSteps.Add((at, GlobalPosition + Vector3.Up * 0.3f, stream, false));
		}
	}

	private void PlayPendingSteps()
	{
		for (int i = _pendingSteps.Count - 1; i >= 0; i--)
		{
			var s = _pendingSteps[i];
			if (s.at > _clock) continue;
			// never on one of the player's feet: just after one, or about to come (their pace changes under a scheduled
			// answer), it waits until just past the next
			float toNext = _player.Footsteps?.TimeToNextStep ?? 99f;
			if (s.shadow && (_clock - _lastPlayerStep < 0.15 || toNext < 0.15f))
			{
				_pendingSteps[i] = (_clock + (toNext < 0.15f ? toNext + 0.2f : 0.2 - (_clock - _lastPlayerStep)), s.pos, s.stream, s.shadow);
				continue;
			}
			_pendingSteps.RemoveAt(i);
			if (s.shadow)
			{
				ShadowStepsAnswered++;
				_lastAnswer = _clock;
				Heard();
				MinAnswerGap = Mathf.Min(MinAnswerGap, (float)(_clock - _lastPlayerStep));
			}
			// (2026-10-15) heard relative to the player's own step, the distance's falloff cancelled (up to 12 dB): a set
			// level, not "louder the nearer the 16 m unit size", and once per step its own occlusion: a trunk between, a
			// little quieter and duller (sound bends round a trunk; not the wall's 650 Hz muffle)
			float rel = (s.shadow ? (Intro ? IntroShadowRelDb : ShadowRelDb) + _spellDb : EchoRelDb);
			bool blocked = Blocked(s.pos + Vector3.Up * 0.9f);
			float playerDb = _player.Footsteps?.StepVolumeDb ?? -26f;
			float d = (_player.GlobalPosition - s.pos).Length();
			float comp = Mathf.Clamp(20f * Mathf.Log(Mathf.Max(d, 0.1f) / 16f) / Mathf.Log(10f), 0f, 12f);
			LastStepRelDb = rel + (blocked ? -3f : 0f);
			// (2026-10-15, the owner: "the footsteps still sound fake for the monster") not the player's own step a little
			// lower: a heavier foot (pitched 0.8-0.88), and under it a second, softer layer of another ground (a squelch, a
			// dry scuff) a hair later in the same voice's place: weight from layering, not loudness (RE2's Mr. X)
			float db = playerDb + LastStepRelDb + comp;
			Play(s.stream, s.pos, db, _rng.RandfRange(0.8f, 0.88f), blocked ? 2500f : 20500f);
			var layerSet = _rng.Randf() < 0.5f ? _stepSets["mud"] : _stepSets["leaves_dry"];
			if (layerSet.Length > 0)
			{
				var layer = layerSet[_rng.RandiRange(0, layerSet.Length - 1)];
				if (layer != s.stream) Play(layer, s.pos + new Vector3(0.1f, 0f, 0.1f), db - _rng.RandfRange(7f, 10f), _rng.RandfRange(0.75f, 0.85f), blocked ? 2000f : 9000f);
			}
		}
	}
	private float _spellDb;

	/// <summary>One ray from the eye to <paramref name="point"/> through the world: a trunk, rock or ground between.</summary>
	private bool Blocked(Vector3 point)
	{
		var eye = GetViewport()?.GetCamera3D()?.GlobalPosition ?? _player.GlobalPosition + Vector3.Up * 1.6f;
		var hit = Ray(eye, point);
		return hit.Count > 0 && ((Vector3)hit["position"]).DistanceTo(point) > 0.6f;
	}

	private void Play(AudioStream stream, Vector3 at, float db, float pitch, float cutoffHz = 20500f)
	{
		if (Inert) return;   // nothing of it sounds inside the bunker
		var v = _voices[_nextVoice];
		_nextVoice = (_nextVoice + 1) % _voices.Count;
		v.GlobalPosition = at;
		_soundAt = at;
		v.Stream = stream;
		v.VolumeDb = db;
		v.PitchScale = pitch;
		v.AttenuationFilterCutoffHz = cutoffHz;
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
