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
///   waits, its steps sometimes shadow yours a beat late and stop when you
///   stop, or a twig snaps at its tree.
/// - It does not follow at all on the Blackfern Trail: it starts in the Hollow,
///   once the first climb has happened (checkpoint 2 on). It never enters the silence around a
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
	[Export] public float VanishSeconds = 0.22f;
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
	[Export] public Vector2 TwigInterval = new(15f, 40f);   // counted only while it is out there
	[Export] public float StepVolumeDb = 3f;
	[Export] public float TwigVolumeDb = 5f;

	public State Current { get; private set; } = State.Dormant;
	public int PeekCount { get; private set; }
	public int DistantCount { get; private set; }
	public bool IsDistant => _ahead;
	public int SeenCount { get; private set; }
	public int StingCount { get; private set; }
	public int ShadowSpells { get; private set; }
	public int SoundCount { get; private set; }
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

	private readonly List<AudioStream> _steps = new();
	private readonly List<AudioStream> _twigs = new();
	private AudioStream _sting;
	private readonly List<AudioStreamPlayer3D> _voices = new();
	private AudioStreamPlayer _stingVoice;
	private int _nextVoice;
	private SamplePicker _stepPicker, _twigPicker;
	private readonly List<(double at, Vector3 pos)> _pendingSteps = new();
	private double _clock;
	private double _nextEchoAllowed;
	private double _nextTwig;
	private double _nextShadow;
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

		for (int i = 1; i <= 6; i++) TryLoad($"res://assets/audio/sfx/step_dirt_{i:00}.wav", _steps);
		for (int i = 1; i <= 4; i++) TryLoad($"res://assets/audio/sfx/twig_snap_{i:00}.wav", _twigs);
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
		_nextTwig = _rng.RandfRange(TwigInterval.X, TwigInterval.Y);
		_nextShadow = _rng.RandfRange(ShadowInterval.X, ShadowInterval.Y);
		_overlap = new PhysicsShapeQueryParameters3D
		{
			Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.9f },
			CollisionMask = 1,
		};
	}

	public override void _ExitTree() => ProjectDS.Player.CameraTool.PhotoTaken -= OnPhotoTaken;

	private static void TryLoad(string path, List<AudioStream> into)
	{
		if (ResourceLoader.Exists(path)) into.Add(GD.Load<AudioStream>(path));
	}

	/// <summary>The player has stood still a long time: appear soon, and let a twig snap.</summary>
	public void NudgeNoise()
	{
		_cooldown = Mathf.Min(_cooldown, _rng.RandfRange(1f, 4f));   // come and stand behind a tree soon
		_nextTwig = Mathf.Min(_nextTwig, _clock + _rng.RandfRange(4f, 9f));
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

	/// <summary>Whether the story lets it follow yet: only in the Hollow, from the first climb on.</summary>
	private static bool ShouldWake() => StoryManager.Instance is not { } s || s.Current >= Checkpoint.Act2StairsClimbed;

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
		PlayPendingSteps();

		// Indoors there is nothing to hide behind: it is simply not there until you come back out.
		if (Indoors)
		{
			if (Current != State.Dormant) { Hide(0f); Current = State.Dormant; _shadowLeft = 0; }
			return;
		}
		if (!_awake)
		{
			if (!ShouldWake()) { Current = State.Dormant; return; }
			_awake = true;
		}
		if (Current == State.Dormant) Current = State.Hidden;
		// Unseen, it grows bolder: its trees creep closer over time.
		Tension = Mathf.Min(1f, Tension + dt / CloseInSeconds);

		bool withdraw = SilenceAt(_player.GlobalPosition) > RetreatAtSilence;
		switch (Current)
		{
			case State.Hidden:
				_cooldown -= dt;
				if (!withdraw && _cooldown <= 0f)
				{
					bool placed = _rng.Randf() < AheadChance && TryPlace(cam, ahead: true);
					if (!placed && !TryPlace(cam, ahead: false)) _cooldown = 3f;
				}
				break;
			case State.Peeking:
				UpdatePeeking(cam, dt, withdraw);
				break;
			case State.Vanishing:
				SetVisibility(_visibility - dt * MaxVisibility / VanishSeconds);
				GlobalPosition += _hideDir * DuckSpeed * dt;
				if (_visibility <= 0f) Hide(_rng.RandfRange(CooldownSeconds.X, CooldownSeconds.Y));
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
			if (_seenTime >= _linger || tooClose || withdraw) { LastSeenDuration = _seenTime; Current = State.Vanishing; }
			return;
		}

		// Glimpsed, then the player looked away: it is simply gone when they look back.
		if (withdraw || _seenThisPeek)
		{
			if (_seenThisPeek) LastSeenDuration = _seenTime;
			Hide(_rng.RandfRange(CooldownSeconds.X, CooldownSeconds.Y));
			return;
		}

		// Unobserved. If you walked on without ever seeing it, it is simply gone.
		if (dist > (_ahead ? MoveOnDistance + 18f : MoveOnDistance))
		{
			Hide(_rng.RandfRange(UnseenCooldown.X, UnseenCooldown.Y));
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
			Current = State.Vanishing;
		}
	}

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
			else dir = (-look).Rotated(Vector3.Up, _rng.RandfRange(-0.9f, 0.9f));

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
			Current = State.Peeking;
			PeekCount++;
			if (ahead) DistantCount++;
			_seenThisPeek = false;
			_stungThisPeek = false;
			_seenTime = 0;
			_stayTimer = ahead ? 25f : _rng.RandfRange(RelocateSeconds.X, RelocateSeconds.Y);
			// Give it a moment at its new tree before it makes any sound.
			_nextTwig = _clock + _rng.RandfRange(TwigInterval.X, TwigInterval.Y);
			_nextShadow = _clock + _rng.RandfRange(3f, 10f);
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

		// Echo steps: you stop, and from behind its tree, one more step.
		if (speed > 1.2f) _walkedFor += (float)GetProcessDeltaTime();
		else if (speed < 0.2f && _walkedFor >= 1.5f)
		{
			_walkedFor = 0f;
			bool wasShadowing = _shadowLeft > 0;
			_shadowLeft = 0;   // you stopped; so did it
			if (Present && _clock >= _nextEchoAllowed && _rng.Randf() < (wasShadowing ? 0.6f : EchoStepChance))
			{
				_nextEchoAllowed = _clock + EchoStepCooldown;
				QueueSteps(1, _rng.RandfRange(0.35f, 0.8f));
			}
		}
		if (!Present) { _shadowLeft = 0; return; }

		// Every so often while you walk, its steps start shadowing yours (see OnPlayerStepped).
		if (speed > 1.2f && _shadowLeft == 0 && _clock >= _nextShadow)
		{
			_nextShadow = _clock + _rng.RandfRange(ShadowInterval.X, ShadowInterval.Y);
			_shadowLeft = _rng.RandiRange(ShadowSteps.X, ShadowSteps.Y);
			ShadowSpells++;
		}

		// A twig snaps where it stands.
		if (_clock >= _nextTwig && _twigs.Count > 0)
		{
			_nextTwig = _clock + _rng.RandfRange(TwigInterval.X, TwigInterval.Y);
			Play(_twigs[_twigPicker.Next(_rng, _twigs.Count)], GlobalPosition + Vector3.Up * 0.1f, TwigVolumeDb, _rng.RandfRange(0.9f, 1.05f));
		}
	}

	/// <summary>While shadowing: each of your footsteps is answered a beat later from its tree.</summary>
	private void OnPlayerStepped()
	{
		if (_shadowLeft <= 0 || !Present) return;
		_shadowLeft--;
		Vector3 p = GlobalPosition + new Vector3(_rng.RandfRange(-0.4f, 0.4f), 0.1f, _rng.RandfRange(-0.4f, 0.4f));
		_pendingSteps.Add((_clock + _rng.RandfRange(0.2f, 0.3f), p));
	}

	private void QueueSteps(int count, float firstDelay)
	{
		double at = _clock + firstDelay;
		for (int i = 0; i < count; i++, at += _rng.RandfRange(0.55f, 0.7f))
			_pendingSteps.Add((at, GlobalPosition + Vector3.Up * 0.1f));
	}

	private void PlayPendingSteps()
	{
		for (int i = _pendingSteps.Count - 1; i >= 0; i--)
		{
			if (_pendingSteps[i].at > _clock || _steps.Count == 0) continue;
			Play(_steps[_stepPicker.Next(_rng, _steps.Count)], _pendingSteps[i].pos, StepVolumeDb, _rng.RandfRange(0.84f, 0.94f));
			_pendingSteps.RemoveAt(i);
		}
	}

	private void Play(AudioStream stream, Vector3 at, float db, float pitch)
	{
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
