using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.LakeParts;

namespace ProjectDS.World;

/// <summary>
/// Act 12: the rowboat crossing. A sibling of <see cref="Lake"/>, which builds the water, the shores
/// and the station; this owns the boat (a <see cref="Rowboat"/>) and the whole beat once the player
/// gets in (E on the boat, <see cref="Interactable"/>).
///
/// 1. Boarding, as one short animation: along the dock to its edge, a step down into the boat (it
///    dips and rocks under the weight), sitting down (the eye drops to a seated height), the line
///    cast off, a push away from the dock.
/// 2. The row out. Player-paced, on a line to the station's beach: W rows (both oars on their own
///    rhythm while it's held), Shift with it rows hard (spending breath), A/D steer off the line and
///    she eases back onto it; A and D alone, alternated, still row the old way (<see cref="ReadOars"/>,
///    the <see cref="RowingPrompt"/> shows all of it). Each stroke is a real push that surges and
///    glides (the boat has momentum and water drag, it never teleports); the boat rides the lake's
///    actual wave surface and the view rocks with it. The mouse is free to look around throughout.
///    The dread starts early (<see cref="Beats"/>): fog closing in, something under the boat, a far
///    foghorn, a limb come up to watch, knocking from below, the hush.
/// 3. Midway, it goes wrong: the birds stop, something thuds under the water, and with no more warning
///    than that tentacles covered in bloodshot eyes erupt all round (a <see cref="LakeCreature"/>): a
///    foghorn bellow, the world briefly regraded red (once, easing away; Reduce Flashing softens it),
///    a ring wave that tosses the boat, the lake turning white-capped, every eye opening and turning
///    to the player, one limb rearing up and slamming down beside the boat. About eight seconds.
/// 4. The fight back. Most of it sinks; two limbs stay up behind, watching. The chop runs back toward
///    the near shore (a current, swells), and a limb hunts the boat (<see cref="Hunt"/>): steady rowing
///    draws away from it, its surges need hard rowing, limbs surface closer and closer, two strike at
///    the boat (<see cref="Strike"/>), and over the last stretch it chases the boat to the beach.
/// 5. Landing: the bow grinds up onto the far beach, the limbs sink, the player stands, steps over the
///    bow onto the gravel, and walks up to the rescue station. Its doorway is the act's end: checkpoint
///    10, then a fade carries them inside (Act 13, <see cref="StationInterior"/>).
/// </summary>
public partial class LakeCrossingEvent : Node3D
{
	[Export] public NodePath LakePath = "../Lake";

	// ---- rowing (2026-10-13, the owner: "way too cumbersome and takes too long". It was ~250 strictly
	// alternating A/D presses over two minutes; now W rows on its own rhythm, Shift rows hard, A/D steer)

	/// <summary>One easy stroke's push (both oars; m/s added to the boat's way over the drive).</summary>
	[Export] public float StrokeImpulse = 1.35f;
	/// <summary>Seconds per stroke while W is held: easy, and hard (Shift).</summary>
	[Export] public float StrokeCycle = 0.9f;
	[Export] public float HardCycle = 0.62f;
	/// <summary>One hard stroke's push.</summary>
	[Export] public float HardImpulse = 1.45f;
	/// <summary>Once the creature is up every stroke is this much stronger (fear lends strength).</summary>
	[Export] public float RoughBoost = 1.1f;
	/// <summary>A single oar's stroke (A or D on their own, the old way), as a share of a full stroke;
	/// alternate them and the boat rows, the same oar twice and it mostly turns.</summary>
	[Export] public float OarShare = 0.5f;
	/// <summary>The boat's top speed (m/s) however hard it's rowed. Easy rowing settles near 3.3 on calm
	/// water and 2.9 in the current; hard near 5 and 4.9.</summary>
	[Export] public float MaxSpeed = 5.2f;
	/// <summary>Water drag on the boat's way, per second (low: each stroke surges, then she glides).</summary>
	[Export] public float Drag = 0.45f;
	/// <summary>Hard rowing spends the rower's breath: seconds from full to spent, and to get it all back.
	/// Spent, there's no rowing hard until it's a third back.</summary>
	[Export] public float BreathSeconds = 6.5f;
	[Export] public float BreathRecover = 7f;
	/// <summary>Steering: how fast the boat slides across (m/s) with A or D held, and how far off the
	/// straight line to the station's beach it may wander. Let go and she eases back onto the line.</summary>
	[Export] public float SteerSpeed = 1.7f;
	[Export] public float MaxSideways = 7f;
	[Export] public float PaddleSideThreshold = 0.45f;

	/// <summary>Fraction (0..1) of the crossing where the creature breaches.</summary>
	[Export] public float BreachAtFraction = 0.45f;
	/// <summary>Kept for tests and the changelog's "7-10 seconds": the breach's length, give or take.</summary>
	[Export] public double BreachHoldSeconds = 8.0;
	/// <summary>The current's steady pull back toward the near shore once the water turns (m/s²).</summary>
	[Export] public float CurrentPull = 0.35f;
	/// <summary>Seconds between the big swells in the current phase, and how hard each shoves the boat back.</summary>
	[Export] public Vector2 SwellEvery = new(3.5f, 5.5f);
	[Export] public float SwellShove = 0.45f;
	/// <summary>How far behind the breach point the current may drag the boat (it never pushes it all the way home).</summary>
	[Export] public float MaxFallback = 8f;

	/// <summary>The hunter: how far behind the boat it starts, its pace (m/s; a little under easy
	/// rowing, so a steady rower draws away), its grace before it moves, and how close is caught.</summary>
	[Export] public float HuntStart = 11f;
	[Export] public float HuntSpeed = 2.3f;
	[Export] public float HuntGrace = 4f;
	[Export] public float CatchGap = 1.8f;
	/// <summary>Every so often it comes for the boat: a surge at <see cref="SurgeSpeed"/> for
	/// <see cref="SurgeSeconds"/>, faster than easy rowing and slower than hard.</summary>
	[Export] public float SurgeSpeed = 3.7f;
	[Export] public float SurgeSeconds = 2.6f;
	[Export] public Vector2 SurgeEvery = new(7f, 9f);
	/// <summary>The last stretch: from this fraction of the crossing it gives chase all the way to the
	/// beach at <see cref="FinalDashSpeed"/>.</summary>
	[Export] public float FinalDashAt = 0.88f;
	[Export] public float FinalDashSpeed = 3.0f;
	/// <summary>Fair warning: inside <see cref="WarnGap"/> the prompt goes red, it knocks on the hull and
	/// rears behind the stern; it only takes the boat after <see cref="WarnSeconds"/> of that.</summary>
	[Export] public float WarnGap = 5f;
	[Export] public float WarnSeconds = 1.5f;
	/// <summary>The near miss (Dan, 2026-10-13: a good rower never let it close in): once on every crossing, from
	/// this fraction of it, the hunter rushes up under the stern, rears right behind the boat (<see cref="NearMissGap"/>
	/// m from it, rail to rail: about a metre and a half off the transom), holds a breath, and sinks away back to
	/// <see cref="HuntStart"/>. Scripted from start to end: it never warns, never takes, and never slows the boat.</summary>
	[Export] public float NearMissAt = 0.655f;
	[Export] public float NearMissGap = 3.4f;
	/// <summary>Kept for compatibility with older tuning; the current is now <see cref="CurrentPull"/>.</summary>
	[Export] public float CurrentPushbackPerSecond = 1.5f;

	// ---- test hooks (mirrors the public bools other story beats expose) ----
	public bool Boarded { get; private set; }
	public bool Paddling { get; private set; }
	public bool InBreach { get; private set; }
	public bool InCurrent { get; private set; }
	public bool Landed { get; private set; }
	public bool Arrived { get; private set; }
	/// <summary>The hunter caught the boat: the drowning is playing and the checkpoint will reload.</summary>
	public bool Caught { get; private set; }
	/// <summary>How far behind the boat the hunter is right now (m); large when there is none.</summary>
	public float HuntGap => _huntOn ? _s - _hunt : 999f;
	public int StrokeCount { get; private set; }
	/// <summary>Rowing hard right now (Shift with W, breath to spend), and the breath left (0..1).</summary>
	public bool RowingHard { get; private set; }
	public float Breath => _breath;
	/// <summary>How far the boat has been steered off the straight line to the station's beach (m).</summary>
	public float Sideways => _lat;
	/// <summary>For tests: the scares as they happen. Something under the boat (bumps), lone limbs
	/// surfacing to stare, limbs striking at the boat (and how many of those hit it), the hunter's
	/// surges, the final dash, and the warnings before a take.</summary>
	public int Bumps { get; private set; }
	public int Glimpses { get; private set; }
	public int Strikes { get; private set; }
	public int StrikeHits { get; private set; }
	public int Surges { get; private set; }
	public bool FinalDash { get; private set; }
	public int Warnings { get; private set; }
	/// <summary>For tests: near misses so far (one a crossing), whether one is playing, and the closest it came (m).</summary>
	public int NearMisses { get; private set; }
	public bool InNearMiss => _nearMissT >= 0f;
	public float NearMissClosest { get; private set; } = 999f;
	/// <summary>What the prompt is saying: how close the hunter is (0..1, red as it nears 1), and which
	/// steer key it lights to get out from under a strike (-1 / +1, 0 none).</summary>
	public float Danger => _prompt?.Danger ?? 0f;
	public int DodgeHint => _prompt?.Next ?? 0;
	/// <summary>For tests (and their screenshots): where the latest lone limb came up, and where the hunter is.</summary>
	public Vector3 LastScare { get; private set; }
	public Vector3 HunterWorld => _huntOn ? RailWorld(_hunt, _huntLat) with { Y = _lake.GlobalPosition.Y } : Vector3.Zero;
	/// <summary>0..1 how thick the fog on the water has got.</summary>
	public float Fog => _fog;
	/// <summary>Game seconds from boarding to the bow grounding on the far beach (0 until then).</summary>
	public double CrossingSeconds { get; private set; }
	/// <summary>0..1 across the whole crossing.</summary>
	public float Progress { get; private set; }
	/// <summary>The boat's way through the water right now (m/s, negative = being pushed back).</summary>
	public float Speed => _v;
	/// <summary>For tests: the largest red-grade amount the breach used.</summary>
	public float RedPeak { get; private set; }
	public LakeCreature LastCreature { get; private set; }
	public Rowboat Boat => _boat;
	/// <summary>For tests: the "get in the boat" prompt.</summary>
	public Interactable BoardPrompt => _boardPrompt;

	private Lake _lake;
	private Rowboat _boat;
	private Interactable _boardPrompt;
	private MeshInstance3D _line;
	private ShaderMaterial _red;
	private RowingPrompt _prompt;
	private AudioStreamPlayer3D _lap, _rough, _glide;
	private AudioStreamPlayer _heart;
	private bool _running, _arrived;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1212 };

	// the rail: moored -> pushed off -> the far beach, in Lake-local XZ
	private Vector2 _p0, _p1, _p2;
	private float _pushLen, _total, _s;
	private float _v, _thrust;
	// the boat's own motion on top of the rail: yaw wobble, rocking, a dip under weight
	private float _yawOff, _yawVel, _pitchOff, _pitchVel, _rollOff, _rollVel, _dip, _dipVel;
	private float _lastYaw;
	private float _groundLift, _groundPitch;
	private PlayerController _riding;
	private float _shake;
	private float _wakeTimer;
	private bool _huntOn;
	private float _hunt, _capsize;
	private double _knockTimer;
	/// <summary>Game seconds since the scene began (for the crossing's timing log).</summary>
	private double _clock;

	// strokes
	private bool? _lastStrokeWasLeft;
	private bool _leftHeld, _rightHeld;
	private double _lastStrokeTime, _lastLeftTime, _lastRightTime;
	private float _strokeClock, _breath = 1f;
	private bool _spent;
	private int _steer;
	// steering: off the rail sideways (m), and how fast
	private float _lat, _latVel;
	// the stroke's surge in the view
	private float _fovKick;
	// the hunt
	private double _surgeAt, _surgeLeft;
	private float _warnT, _huntWakeT;
	private bool _warning;
	private Node3D _huntVoice;
	private AudioStreamPlayer3D _huntRumble;
	// the scares along the way, each once (by fraction of the crossing)
	private readonly System.Collections.Generic.HashSet<string> _beats = new();
	private LakeCreature _loner;
	// the fog closing in on the water
	private ForestAtmosphere _atmo;
	private float _fog, _fogTarget, _fogBase = -1f;
	private Color _fogBaseColor;

	public override void _Ready()
	{
		Callable.From(Setup).CallDeferred();
		// the creature's shaders drawn once in the load's black (the lag pass, 2026-10-13: its breach froze the game 1.6 s)
		if (StoryManager.Instance is { Current: < Checkpoint.Act12LakeCrossed })
		{
			ShaderWarmup.Prewarm(this, new LakeCreature { Name = "LakeCreatureWarmUp", WarmUpOnly = true });
			// (2026-10-14b: a one-off 137-147 ms hitch at the near miss, with no pipeline compiled: its spray and the takes
			// of its sounds were made and read off the disk on the spot) the spray built and the strike's and the near miss's
			// sounds read in the load's black, held for the crossing; nothing about when or how they play changes
			LakeParts.LakeFx.Warm();
			foreach (var (name, takes) in new[] { ("breach_surge", 6), ("creature_foghorn", 2), ("limb_rise_water", 8), ("hull_creak", 10), ("deep_thoom", 6), ("limb_sink", 6) })
				for (int take = 1; take <= takes; take++)
				{
					string path = $"res://assets/audio/sfx/{name}_{take:00}.wav";
					if (ResourceLoader.Exists(path)) _heldSounds.Add(GD.Load<AudioStream>(path));
				}
		}
	}
	private readonly System.Collections.Generic.List<AudioStream> _heldSounds = new();

	private void Setup()
	{
		_lake = GetNodeOrNull<Lake>(LakePath);
		if (_lake == null) { GD.PushWarning("LakeCrossingEvent: no Lake sibling found"); return; }
		_p0 = LakeShape.BoatMooring;
		_p1 = _p0 + new Vector2(-0.35f, -4.2f);
		_p2 = LakeShape.BoatLanding;
		_pushLen = _p0.DistanceTo(_p1);
		_total = _pushLen + _p1.DistanceTo(_p2);

		_boat = new Rowboat { Name = "Rowboat" };
		AddChild(_boat);
		_boat.Stow(true);
		_boat.BladeIn += OnBladeIn;
		_boat.BladeOut += OnBladeOut;
		_s = 0f;
		PlaceBoat(0f, true);
		_lastYaw = _boat.GlobalRotation.Y;

		Vector3 cleat = _lake.ToGlobal(new Vector3(LakeShape.DockHalfWidth - 0.15f, LakeShape.DockDeck + 0.07f, LakeShape.BoatMooring.Y - 1.3f));
		Vector3 bow = _boat.ToGlobal(new Vector3(0, 0.42f, -Rowboat.HalfLength + 0.1f));
		_line = LakeStructures.BuildMooringLine(Cutscene.SceneRoot(this), cleat, bow);
		// (2026-10-16, STORY.md Rev 5: "its rope knotted in a hurry, tight and ugly"; nobody says by whom) the line made fast
		// round the cleat in too many crossed turns, a loop half pulled through, the tail left hanging over the edge
		// (Blender's: tools/Blender/rh_trail.py); it goes with the line when it's cast off
		if (_line != null)
			ItemModels.Add(_line, "rh_trail", "HurriedKnot", new Transform3D(new Basis(Vector3.Up, _lake.GlobalRotation.Y), cleat + _lake.GlobalBasis.X * 0.04f),
				"HurriedKnot", n => n == "rope" ? ProcTextures.Flat("lake_rope", new Color(0.5f, 0.44f, 0.33f)) : null);

		_boardPrompt = new Interactable
		{
			Name = "BoardBoat",
			Prompt = "Get in the boat",
			PickRadius = 1.2f,
			MaxDistance = 3.4f,
			Position = Rowboat.SeatLocal + new Vector3(0, 0.25f, 0),
		};
		_boardPrompt.Interacted += OnBoarded;
		_boat.Hull.AddChild(_boardPrompt);

		BuildRedGrade();
		_prompt = new RowingPrompt { Name = "RowingPrompt" };
		Cutscene.SceneRoot(this).AddChild(_prompt);
		BuildSound();

		// The trigger sits in the doorway itself: stepping through it (not just onto the porch) ends the act. (2026-10-15:
		// behind the door's leaf now, from the threshold in, so only the opened door lets them reach it; the lobby is
		// in the doorway as they step, Lake.FrontDoor.cs.)
		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(2f, 2.6f, 1.0f) },
			ToLocal(_lake.StationDoorWorld) + new Vector3(0, 1.2f, -0.35f), OnArrival, "LakeArrival");
	}

	private void BuildRedGrade()
	{
		var layer = new CanvasLayer { Name = "BreachRed", Layer = 9 };
		Cutscene.SceneRoot(this).AddChild(layer);
		_red = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/red_grade.gdshader") };
		_red.SetShaderParameter("amount", 0f);
		var rect = new ColorRect { Material = _red, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(rect);
	}

	private void SetRed(float amount)
	{
		_red?.SetShaderParameter("amount", amount);
		RedPeak = Mathf.Max(RedPeak, amount);
	}

	private void BuildSound()
	{
		_lap = Loop3D("res://assets/audio/ambient/hull_lap_loop.wav", "Water", -8f, 3f);
		_rough = Loop3D("res://assets/audio/ambient/lake_rough_loop.wav", "Weather", -80f, 10f);
		// the water chuckling along the planks while she makes way (raised with her speed)
		_glide = Loop3D("res://assets/audio/ambient/hull_glide_loop.wav", "Water", -80f, 3f);
		const string heart = "res://assets/audio/ambient/heartbeat_loop.wav";
		if (ResourceLoader.Exists(heart))
		{
			_heart = new AudioStreamPlayer { Name = "Heartbeat", Stream = Looping(heart), Bus = "Player", VolumeDb = -80f };
			AddChild(_heart);
		}
		// the hunter's own sound: a low churning undertone that rides along under it, rising as it closes
		_huntVoice = new Node3D { Name = "HunterVoice" };
		AddChild(_huntVoice);
		const string under = "res://assets/audio/ambient/undertone_loop.wav";
		if (ResourceLoader.Exists(under))
		{
			_huntRumble = new AudioStreamPlayer3D { Stream = Looping(under), Bus = "Unnatural", VolumeDb = -80f, UnitSize = 8f, MaxDistance = 60f };
			_huntVoice.AddChild(_huntRumble);
		}
	}

	private AudioStreamPlayer3D Loop3D(string path, string bus, float db, float unit)
	{
		if (!ResourceLoader.Exists(path)) return null;
		var p = new AudioStreamPlayer3D { Stream = Looping(path), Bus = bus, VolumeDb = db, UnitSize = unit, MaxDistance = 60f, Autoplay = true };
		_boat.AddChild(p);
		return p;
	}

	/// <summary>A WAV forced to loop end to end (the imports carry no loop points), the way
	/// <see cref="AmbienceLoop"/> does it — without taking over the player's volume, which this drives itself.</summary>
	private static AudioStream Looping(string path)
	{
		var stream = GD.Load<AudioStream>(path);
		if (stream is not AudioStreamWav wav) return stream;
		wav = (AudioStreamWav)wav.Duplicate();
		wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		wav.LoopBegin = 0;
		wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
		return wav;
	}

	// ------------------------------------------------------------------ the boat on its rail

	private Vector2 RailAt(float s, out Vector2 dir)
	{
		if (s <= _pushLen)
		{
			dir = (_p1 - _p0).Normalized();
			return _p0.Lerp(_p1, s / Mathf.Max(_pushLen, 0.01f));
		}
		dir = (_p2 - _p1).Normalized();
		// ease the heading change at the push-off corner
		float u = Mathf.Clamp((s - _pushLen) / 4f, 0f, 1f);
		Vector2 d0 = (_p1 - _p0).Normalized();
		dir = d0.Lerp(dir, Mathf.SmoothStep(0f, 1f, u)).Normalized();
		return _p1.Lerp(_p2, (s - _pushLen) / Mathf.Max(_total - _pushLen, 0.01f));
	}

	/// <summary>Seats the boat on the water at rail distance <paramref name="s"/>: on the live waves
	/// (height from the centre, pitch from bow vs stern, roll from side to side) plus its own rocking.</summary>
	private void PlaceBoat(float dt, bool snap = false)
	{
		// springs: yaw wobble back to the heading (the bow points the way she's sliding when steered),
		// rocking back to level, the dip back up
		float heading = -Mathf.Atan2(_latVel, Mathf.Max(Mathf.Abs(_v), 1.2f)) * 0.9f;
		Spring(ref _yawOff, ref _yawVel, heading, 3.2f, 1.6f, dt);
		Spring(ref _pitchOff, ref _pitchVel, 0f, 9f, 2.2f, dt);
		Spring(ref _rollOff, ref _rollVel, 0f, 7f, 1.6f, dt);
		Spring(ref _dip, ref _dipVel, 0f, 16f, 3f, dt);

		Vector2 rail = RailAt(_s, out Vector2 dir);
		Vector2 side = new(-dir.Y, dir.X);
		Vector2 pos = rail + side * (_lat + (_yawOff - heading) * 0.5f);
		float yaw = Mathf.Atan2(-dir.X, -dir.Y) + _yawOff;
		Vector2 fwd = new(-Mathf.Sin(yaw), -Mathf.Cos(yaw)), right = new(-fwd.Y, fwd.X);
		ref var w = ref _lake.Waves;
		float hc = LakeShape.WaveHeight(w, pos.X, pos.Y);
		float hb = LakeShape.WaveHeight(w, pos.X + fwd.X * Rowboat.HalfLength * 0.8f, pos.Y + fwd.Y * Rowboat.HalfLength * 0.8f);
		float hs = LakeShape.WaveHeight(w, pos.X - fwd.X * Rowboat.HalfLength * 0.8f, pos.Y - fwd.Y * Rowboat.HalfLength * 0.8f);
		float hr = LakeShape.WaveHeight(w, pos.X + right.X * Rowboat.Beam * 0.5f, pos.Y + right.Y * Rowboat.Beam * 0.5f);
		float hl = LakeShape.WaveHeight(w, pos.X - right.X * Rowboat.Beam * 0.5f, pos.Y - right.Y * Rowboat.Beam * 0.5f);
		float pitch = Mathf.Atan2(hb - hs, Rowboat.Length * 0.8f) + _pitchOff + _groundPitch;
		float roll = Mathf.Atan2(hr - hl, Rowboat.Beam) + _rollOff;
		float y = (hc + (hb + hs + hr + hl) * 0.25f) * 0.5f + _dip + _groundLift;

		roll += _capsize;
		var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, roll);
		_boat.GlobalTransform = new Transform3D(basis, _lake.ToGlobal(new Vector3(pos.X, y, pos.Y)));
		// cut the boat's own footprint out of the lake surface, so the water never shows inside the hull
		_lake.WaterMaterial?.SetShaderParameter("hull", new Vector4(pos.X, pos.Y, yaw, 1f));

		if (_riding != null && GodotObject.IsInstanceValid(_riding))
		{
			_riding.GlobalPosition = _boat.ToGlobal(Rowboat.SeatLocal);
			var rig = _riding.CameraRig;
			// the view rides the boat (not all of it: a rower's head steadies itself), and turns with the bow
			rig.PitchSwim = pitch * 0.3f;
			rig.RollSwim = roll * 0.3f;
			float dyaw = Mathf.AngleDifference(_lastYaw, yaw);
			if (!snap && Mathf.Abs(dyaw) < 0.3f) _riding.PlayerInput.AddCutsceneLook(new Vector2(dyaw, 0));
		}
		_lastYaw = yaw;
		Progress = Mathf.Clamp(_s / Mathf.Max(_total, 0.01f), 0f, 1f);
	}

	private static void Spring(ref float x, ref float v, float target, float k, float damp, float dt)
	{
		if (dt <= 0f) return;
		v += (-(x - target) * k * k - v * 2f * damp) * dt;
		x += v * dt;
	}

	public override void _Process(double delta)
	{
		if (_boat == null || _lake == null) return;
		float dt = (float)delta;
		_clock += delta;
		// every frame, whatever the beat: moored, rowing, stopped dead in the breach, run aground
		PlaceBoat(dt);
		if (_riding != null && GodotObject.IsInstanceValid(_riding))
		{
			// camera jolts decay quickly
			_shake = Mathf.MoveToward(_shake, 0f, dt * 1.8f);
			_riding.CameraRig.Shake = _shake > 0.001f
				? new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), 0) * _shake * 0.045f
				: Vector3.Zero;
			// each stroke's surge widens the view a touch, easing back as she glides
			_fovKick = Mathf.MoveToward(_fovKick, 0f, dt * 2.2f);
			if (Paddling) _riding.CameraRig.FovSwim = _fovKick;
		}
		DriveFog(dt);
		// the rough water's roar follows the chop
		if (_rough != null) _rough.VolumeDb = Mathf.Lerp(-40f, -4f, Mathf.Clamp(_lake.Waves.Intensity, 0f, 1f));
		// the hull's chuckle follows her way through the water (silent at rest)
		if (_glide != null)
		{
			float way = Mathf.Clamp(Mathf.Abs(_v) / MaxSpeed, 0f, 1f);
			_glide.VolumeDb = way < 0.05f ? -80f : Mathf.Lerp(-30f, -9f, Mathf.Sqrt(way));
		}
	}

	// ------------------------------------------------------------------ boarding -> crossing -> landing

	private void OnBoarded(PlayerController player)
	{
		if (_running) return;
		_running = true;
		_boardPrompt.Enabled = false;
		_ = Cutscene.Run(this, ct => RunSequence(player, ct), freezeBody: true);
	}

	private async Task RunSequence(PlayerController player, CancellationToken ct)
	{
		try
		{
			double t0 = _clock;
			await Board(player, ct);
			double t1 = _clock;
			Boarded = true;
			Paddling = true;
			_prompt.SetShown(true);
			await Row(player, _pushLen, BreachAtFraction * _total, false, ct);
			double t2 = _clock; int calmStrokes = StrokeCount;
			Paddling = false;
			await Breach(player, ct);
			double t3 = _clock;
			Paddling = true;
			InCurrent = true;
			await Row(player, BreachAtFraction * _total, _total - 2.6f, true, ct);
			InCurrent = false;
			Paddling = false;
			if (Caught)
			{
				GD.Print($"[lake] taken after {_clock - t3:0.0} s of the hunt at progress {Progress:0.00} ({Warnings} warnings first)");
				await Drown(player, ct);
				return;
			}
			double t4 = _clock;
			await Land(player, ct);
			CrossingSeconds = _clock - t0;
			GD.Print($"[lake] crossing timings (game s): board {t1 - t0:0.0}, calm row {t2 - t1:0.0} ({calmStrokes} strokes), breach {t3 - t2:0.0}, " +
				$"rough row {t4 - t3:0.0} ({StrokeCount - calmStrokes} strokes), boarding to the bow grounding {t4 - t0:0.0}, to standing on the beach {CrossingSeconds:0.0}, rail {_total:0.0} m; " +
				$"{Bumps} bumps, {Glimpses} limbs surfaced, {Strikes} strikes ({StrikeHits} hit), {Surges} surges, final dash {FinalDash}, {Warnings} warnings, closest {MinGap:0.0} m, fog {_fog:0.00}");
			Landed = true;
			GD.Print("[story] Act 12: crossed the lake; the station waits");
		}
		finally
		{
			Paddling = false;
			InCurrent = false;
			InBreach = false;
			_prompt?.SetShown(false);
			if (GodotObject.IsInstanceValid(player))
			{
				var rig = player.CameraRig;
				rig.PitchSwim = 0f;
				rig.RollSwim = 0f;
				rig.Shake = Vector3.Zero;
				rig.FovSwim = 0f;
				rig.EyeHeight = 1.62f;
			}
			_riding = null;
			SetRed(0f);
		}
	}

	/// <summary>Along the dock to its edge beside the boat, a step down into it, sit, cast off, push away.</summary>
	private async Task Board(PlayerController player, CancellationToken ct)
	{
		Cutscene.Lock(player, input: true);
		try
		{
			player.Velocity = Vector3.Zero;
			var rig = player.CameraRig;
			Vector3 edge = _lake.ToGlobal(new Vector3(LakeShape.DockHalfWidth - 0.3f, LakeShape.DockDeck + 0.02f, LakeShape.BoatMooring.Y + 0.2f));
			float walk = Mathf.Clamp(player.GlobalPosition.DistanceTo(edge) / 1.6f, 0.4f, 2.2f);
			var toEdge = player.CreateTween();
			toEdge.TweenProperty(player, "global_position", edge, walk).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			var steps = StepsWhile(player, "wood", 0.42f, walk, ct);
			await StoryBeat.PanTowards(this, player, _boat.GlobalPosition, Mathf.Min(walk, 1.2f), ct);
			await Cutscene.Tween(this, toEdge, ct);
			await steps;

			// the step down: up and over the gunwale, then down onto the thwart
			Vector3 from = player.GlobalPosition, to = _boat.ToGlobal(Rowboat.SeatLocal);
			double t = 0;
			const double stepSecs = 0.7;
			while (t < stepSecs)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = Mathf.SmoothStep(0f, 1f, (float)(t / stepSecs));
				player.GlobalPosition = from.Lerp(to, u) + Vector3.Up * (Mathf.Sin(u * Mathf.Pi) * 0.22f);
			}
			// the boat takes the weight: it sinks a little and rocks toward the dock side
			_riding = player;
			_dipVel = -0.9f;
			_rollVel = -0.55f;
			player.Footsteps?.StepNow("wood", 2f);
			Sfx("boat_board", 1, _boat.GlobalPosition, "Player", -2f);
			Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", -6f);

			// sit: the eye drops to a seated height while the view comes round to the bow
			var sit = player.CreateTween();
			sit.TweenProperty(rig, "EyeHeight", Rowboat.SitEyeHeight, 0.85f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			Vector3 ahead = _lake.ToGlobal(new Vector3(_p2.X, 0f, _p2.Y));
			await StoryBeat.PanTowards(this, player, ahead, 1.0f, ct);
			await Cutscene.Tween(this, sit, ct);
			await LevelPitch(player, 0.4f, ct);

			// cast off: the line comes free, the oars come out, a shove off the dock
			Sfx("cloth", 4, _boat.GlobalPosition, "Player", -6f);
			if (_line != null && GodotObject.IsInstanceValid(_line)) _line.QueueFree();
			await Cutscene.Wait(this, 0.35, ct);
			_boat.Stow(false);
			Sfx("row_lock_knock", 8, _boat.ToGlobal(Rowboat.OarlockLocal(-1)), "Player", -9f, 2f);
			Sfx("row_lock_creak", 8, _boat.ToGlobal(Rowboat.OarlockLocal(-1)), "Player", -8f);
			await Cutscene.Wait(this, 0.25, ct);
			Sfx("row_lock_knock", 8, _boat.ToGlobal(Rowboat.OarlockLocal(1)), "Player", -9f, 2f);
			Sfx("row_lock_creak", 8, _boat.ToGlobal(Rowboat.OarlockLocal(1)), "Player", -8f);
			_rollVel = 0.4f;
			_yawVel = 0.15f;
			// drift out along the push-off leg on the shove alone
			_v = 2.4f;
			while (_s < _pushLen)
			{
				await Cutscene.Frame(this, ct);
				float dt = (float)GetProcessDeltaTime();
				_v = Mathf.Max(1.1f, _v - _v * 0.35f * dt);
				_s = Mathf.Min(_pushLen, _s + _v * dt);
			}
		}
		finally { Cutscene.Unlock(player, input: true); }
	}

	/// <summary>Plays footsteps on a surface every <paramref name="every"/> seconds for <paramref name="seconds"/>.</summary>
	private async Task StepsWhile(PlayerController player, string surface, float every, float seconds, CancellationToken ct)
	{
		for (float t = every * 0.5f; t < seconds; t += every)
		{
			await Cutscene.Wait(this, every, ct);
			player.Footsteps?.StepNow(surface);
		}
	}

	/// <summary>Eases the view's pitch back to level (after a scripted pan).</summary>
	private async Task LevelPitch(PlayerController player, float seconds, CancellationToken ct)
	{
		var rig = player.CameraRig;
		float from = rig.Pitch;
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			rig.SetPitch(Mathf.Lerp(from, Mathf.DegToRad(-4f), Mathf.SmoothStep(0f, 1f, (float)(t / seconds))));
		}
	}

	// ------------------------------------------------------------------ rowing

	private void ResetStrokeTracking()
	{
		_lastStrokeWasLeft = null; _leftHeld = false; _rightHeld = false;
		_strokeClock = 10f;   // the first W pulls at once
	}

	/// <summary>
	/// One frame at the oars. W (the stick forward) rows: both oars together on their own steady
	/// rhythm for as long as it's held, no timing to keep. Shift with it (the left trigger) rows hard:
	/// quicker, heavier strokes that spend the rower's breath. A and D steer while W rows; on their own
	/// they pull one oar a stroke at a time, the old way (alternate them and she rows, the same one
	/// twice and she mostly turns), so either way of rowing works.
	/// </summary>
	private void ReadOars(PlayerController player, bool rough, float dt)
	{
		bool live = player.PlayerInput.Enabled;
		Vector2 m = live ? player.PlayerInput.Move : Vector2.Zero;
		bool row = m.Y > 0.35f;
		int steer = m.X < -PaddleSideThreshold ? -1 : m.X > PaddleSideThreshold ? 1 : 0;
		bool hard = row && live && player.PlayerInput.Run && !_spent;
		if (hard)
		{
			_breath -= dt / BreathSeconds;
			if (_breath <= 0f)
			{
				_breath = 0f;
				_spent = true;
				Sfx("breath_out", 5, _boat.GlobalPosition + Vector3.Up, "Player", -10f, 3f);
			}
		}
		else
		{
			_breath = Mathf.Min(1f, _breath + dt / BreathRecover);
			if (_spent && _breath >= 0.35f) _spent = false;
		}
		RowingHard = hard;

		float cycle = hard ? HardCycle : StrokeCycle;
		_strokeClock += dt;
		if (row && _strokeClock >= cycle)
		{
			_strokeClock = 0f;
			Pull(0, hard, rough, cycle);
		}

		bool l = steer < 0, r = steer > 0;
		bool lEdge = l && !_leftHeld, rEdge = r && !_rightHeld;
		_leftHeld = l; _rightHeld = r;
		if (!row)
		{
			if (lEdge) Pull(-1, false, rough, 0f);
			if (rEdge) Pull(1, false, rough, 0f);
		}
		_steer = steer;

		_prompt.Rowing = row;
		_prompt.Hard = hard;
		_prompt.Steer = steer;
		_prompt.Breath = _breath;
		_prompt.Spent = _spent;
	}

	/// <summary>A stroke: 0 both oars (W), -1 / +1 one oar (A / D on their own). Each is a real push
	/// that arrives over the drive (the boat has momentum and drag, it never teleports), surges the bow
	/// up and the view a touch wider, and throws a splash.</summary>
	private void Pull(int side, bool hard, bool rough, float cycle)
	{
		StrokeCount++;
		double now = _clock;
		_lastStrokeTime = now;
		float boost = rough ? RoughBoost : 1f;
		if (side == 0)
		{
			float duration = Mathf.Clamp(cycle * 0.95f, 0.4f, 0.86f);
			float power = (hard ? 1.35f : 1f) * (rough ? 1.15f : 1f);
			_strokeDur[0] = _strokeDur[1] = duration;
			_pair = true;
			_boat.Stroke(-1, power, duration);
			_boat.Stroke(1, power, duration);
			_thrust += (hard ? HardImpulse : StrokeImpulse) * boost;
			// she surges: the bow lifts, the rower leans into it, the view widens a touch
			_pitchVel += hard ? 0.08f : 0.05f;
			_fovKick = Mathf.Max(_fovKick, hard ? 2.2f : 1.3f);
			_prompt.Pressed(0);
			return;
		}
		double sinceSame = now - (side < 0 ? _lastLeftTime : _lastRightTime);
		if (side < 0) _lastLeftTime = now; else _lastRightTime = now;
		bool alternated = _lastStrokeWasLeft != (side < 0);
		_lastStrokeWasLeft = side < 0;
		float dur = Mathf.Clamp((float)sinceSame * 0.95f, 0.28f, 0.62f);
		_pair = false;
		_boat.Stroke(side, rough ? 1.3f : 1f, dur);
		_strokeDur[side < 0 ? 0 : 1] = dur;
		_thrust += StrokeImpulse * OarShare * boost * (alternated ? 1f : 0.45f);
		// one oar alone turns the bow off the other way and rocks the boat a little
		_yawVel += -side * 0.07f;
		_latVel += side * 0.35f;
		_pitchVel += 0.02f;
		_rollVel += side * 0.025f;
		_fovKick = Mathf.Max(_fovKick, 0.6f);
		_prompt.Pressed(side);
	}

	private bool _pair;

	/// <summary>Steering: A or D slides the boat across (quicker once she's moving); let go and she
	/// eases back onto the line for the station's beach on her own, and over the last stretch she's
	/// brought onto it whatever the keys say, so the bow always finds the landing.</summary>
	private void Steer(float dt)
	{
		float remaining = _total - _s;
		float room = Mathf.Clamp((_s - _pushLen) * 0.5f, 0f, MaxSideways);
		float want;
		if (remaining < 24f)
			want = -_lat / Mathf.Max(remaining / Mathf.Max(_v, 1.5f), 0.8f);
		else if (_steer != 0)
			want = _steer * SteerSpeed * Mathf.Clamp(0.45f + Mathf.Abs(_v) / 3f, 0.45f, 1f);
		else
			want = Mathf.Clamp(-_lat * 0.3f, -0.8f, 0.8f);
		_latVel = Mathf.MoveToward(_latVel, want, dt * 2.6f);
		_lat += _latVel * dt;
		if (Mathf.Abs(_lat) > room) { _lat = Mathf.Clamp(_lat, -room, room); _latVel = 0f; }
	}

	/// <summary>Rows from rail distance <paramref name="from"/> to <paramref name="to"/>. In the current
	/// (<paramref name="rough"/>), a steady pull and periodic swells drag the boat back toward the
	/// breach, never more than <see cref="MaxFallback"/> behind where this leg began, and the hunter
	/// follows. Along the way, the scares (<see cref="Beats"/>).</summary>
	private async Task Row(PlayerController player, float from, float to, bool rough, CancellationToken ct)
	{
		ResetStrokeTracking();
		_prompt.Urgent = rough;
		_prompt.ShowDrift = rough;
		_prompt.Next = 0;
		_prompt.Danger = 0f;
		float floor = Mathf.Max(_pushLen, from - (rough ? MaxFallback : 0f));
		if (rough) StartHunt();
		double clock = 0, swellAt = _rng.RandfRange(SwellEvery.X * 0.5f, SwellEvery.Y);
		_s = Mathf.Max(_s, from);
		while (_s < to)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			clock += dt;
			ReadOars(player, rough, dt);

			// the stroke's push arrives over its drive, not in one lump
			float take = _thrust * Mathf.Min(1f, dt * 5f);
			_v += take;
			_thrust -= take;
			_v -= _v * Drag * dt;
			if (rough)
			{
				_v -= CurrentPull * (1f + 0.3f * Mathf.Sin((float)clock * 0.9f)) * dt;
				if (clock >= swellAt)
				{
					swellAt = clock + _rng.RandfRange(SwellEvery.X, SwellEvery.Y);
					Swell();
				}
			}
			_v = Mathf.Min(_v, MaxSpeed);
			_s += _v * dt;
			if (_s < floor) { _s = floor; _v = Mathf.Max(_v, 0f); }
			Steer(dt);
			Wake(dt);
			_prompt.Drift = _v;
			Beats(rough, ct);
			if (rough && Hunt(dt, clock)) { Caught = true; break; }
			if (_heart != null && !rough && _heart.Playing) _heart.VolumeDb = Mathf.Lerp(_heart.VolumeDb, -30f, dt * 0.5f);
		}
		RowingHard = false;
		_prompt.Hard = false;
		_prompt.Rowing = false;
		_prompt.Next = 0;
	}

	// ------------------------------------------------------------------ the scares on the way

	/// <summary>True the first time the crossing passes <paramref name="at"/> (a fraction of it) for
	/// the beat <paramref name="key"/>.</summary>
	private bool Passed(string key, float at) => Progress >= at && _beats.Add(key);

	/// <summary>
	/// The dread starts early and keeps closing in (fractions of the crossing; the breach is at
	/// <see cref="BreachAtFraction"/>):
	/// - from the push-off, the fog starts to thicken on the water and the far shore goes grey;
	/// - 0.13: something big passes under the boat: its shadow slides beneath, the hull is bumped from
	///   below and the boat rocks;
	/// - 0.22: far off over the water, the foghorn, low (the first time it's heard);
	/// - 0.27: a single limb comes up out of the fog well off to one side, every eye on it open and turned
	///   on the boat; it watches it go by, and slides back under;
	/// - 0.35: knocking on the hull from below, twice; rings spreading round the boat;
	/// - then the hush, the shadow sliding under, the thud that lurches the boat: the breach.
	/// After it, with the hunter behind: limbs surfacing to stare, each closer than the last (0.52, 0.72,
	/// 0.83); two that strike at the boat (0.60, 0.77: <see cref="Strike"/>); the hunter's near miss right
	/// behind the stern (from <see cref="NearMissAt"/>: <see cref="BeginNearMiss"/>); and the final dash (from
	/// <see cref="FinalDashAt"/>, in <see cref="Hunt"/>).
	/// </summary>
	private void Beats(bool rough, CancellationToken ct)
	{
		float b = BreachAtFraction;
		if (!rough)
		{
			_fogTarget = Mathf.Clamp((Progress - 0.04f) / (b - 0.04f), 0f, 1f) * 0.8f;
			if (Passed("bump", b * 0.29f)) Bump(ct);
			if (Passed("horn", b * 0.49f)) PlayFoghorn(Ahead(95f, 35f), -6f);
			if (Passed("glimpse0", b * 0.6f)) Glimpse(26f, 32f, 4.2f);
			if (Passed("knock", b * 0.78f)) Knock();
			// the lake goes quiet...
			if (Passed("hush", b - 9f / _total))
			{
				_lake.Hush();
				PrepareBreach();
				ForestAmbienceManager.Instance?.RequestSilence(this, 0.62f, 5);
				Sfx("deep_thoom", 6, _boat.GlobalPosition + Vector3.Down * 6f, "Unnatural", -6f, 20f);
				RingsAround(3, 5f, 9f);
			}
			// ...a vast shape slides under the boat...
			if (Passed("shadow", b - 7f / _total)) _ = ShadowPass(ct);
			// ...and something big moves under the boat
			if (Passed("stir", b - 4f / _total))
			{
				Sfx("deep_thoom", 6, _boat.GlobalPosition + Vector3.Down * 4f, "Unnatural", -1f, 20f);
				_pitchVel += 0.25f;
				_dipVel -= 0.35f;
				RingsAround(5, 3f, 7f);
			}
			return;
		}
		_fogTarget = Progress < 0.8f ? 1f : Mathf.Lerp(1f, 0.55f, (Progress - 0.8f) / 0.15f);
		if (Passed("glimpse1", 0.52f)) Glimpse(22f, 18f, 3.5f);
		if (Passed("strike1", 0.60f)) Strike(1);
		// the near miss, once on every crossing, once the first strike is over (not while a strike's key is lit)
		if (Progress >= NearMissAt && Progress < FinalDashAt - 0.04f && _prompt.Next == 0 && _beats.Add("nearmiss")) BeginNearMiss();
		if (Passed("glimpse2", 0.72f)) Glimpse(14f, 14f, 2.4f);
		if (Passed("strike2", 0.77f)) Strike(-1);
		if (Passed("glimpse3", 0.83f)) Glimpse(10f, 9f, 2.6f);
	}

	/// <summary>A world point <paramref name="ahead"/> metres up the route from the boat and
	/// <paramref name="aside"/> metres off it (negative: the other side), on the water.</summary>
	private Vector3 Ahead(float ahead, float aside)
	{
		Vector2 p = RailAt(Mathf.Clamp(_s + ahead, 0f, _total), out Vector2 dir);
		Vector2 side = new(-dir.Y, dir.X);
		p += side * (_lat + aside);
		return _lake.ToGlobal(new Vector3(p.X, 0f, p.Y));
	}

	/// <summary>Something big passes under the boat: a shadow slides beneath, a dull knock on the
	/// planks from below, the bow lifted and let down, rings spreading.</summary>
	private void Bump(CancellationToken ct)
	{
		Bumps++;
		_ = ShadowPass(ct, 3.2f, 0.6f);
		Sfx("deep_thoom", 6, _boat.GlobalPosition + Vector3.Down * 5f, "Unnatural", -8f, 20f);
		_ = Later(1.4f, () =>
		{
			Sfx("hull_bump", 6, _boat.ToGlobal(new Vector3(0.2f, -0.2f, 0.4f)), "Player", -2f, 4f);
			Sfx("bilge_slosh", 8, _boat.GlobalPosition, "Player", -10f, 3f);
			Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", -6f);
			_pitchVel -= 0.22f;
			_rollVel += 0.3f;
			_dipVel += 0.25f;
			_v *= 0.8f;
			_shake = Mathf.Max(_shake, 0.18f);
			RingsAround(4, 2f, 5f);
		});
	}

	/// <summary>Knocking on the hull from under the water: once, and again further aft.</summary>
	private void Knock()
	{
		Bumps++;
		Sfx("hull_knock_below", 8, _boat.ToGlobal(new Vector3(0, -0.2f, -0.6f)), "Unnatural", -5f, 4f);
		_pitchVel -= 0.08f;
		_ = Later(0.75f, () =>
		{
			Sfx("hull_knock_below", 8, _boat.ToGlobal(new Vector3(0, -0.2f, 0.8f)), "Unnatural", -3f, 4f);
			_pitchVel += 0.1f;
			RingsAround(3, 2f, 4f);
		});
	}

	/// <summary>A lone limb comes up out of the fog <paramref name="aside"/> metres off the route
	/// (either side), <paramref name="ahead"/> metres on, every eye on it turned on the boat, and stares
	/// for <paramref name="stare"/> seconds before it slides back under.</summary>
	private void Glimpse(float aside, float ahead, float stare)
	{
		Glimpses++;
		float side = _rng.Randf() < 0.5f ? -1f : 1f;
		Vector3 at = Ahead(ahead, aside * side) with { Y = _lake.GlobalPosition.Y };
		var c = Creature();
		c.Surface(at, _boat.GlobalPosition, _rng.RandfRange(6f, 8f), stare);
		LastScare = at;
		Sfx("limb_rise_water", 8, at, "Unnatural", -3f, 10f, 120f);
		_ = Later(0.5f, () => Sfx("creature_eye_open", 8, at + Vector3.Up * 5f, "Unnatural", -6f, 10f, 90f));
		// while it stares, water still running off it drips back into the lake; then it slides under (rise 1.4 s, then the stare)
		int drips = _rng.RandiRange(2, 4);
		for (int i = 0; i < drips; i++)
		{
			Vector3 d = at + new Vector3(_rng.RandfRange(-0.6f, 0.6f), 0, _rng.RandfRange(-0.6f, 0.6f));
			_ = Later(1.5f + _rng.RandfRange(0f, Mathf.Max(0.3f, stare - 0.2f)), () => Sfx("limb_drip_water", 8, d, "Unnatural", -8f, 5f, 60f));
		}
		_ = Later(1.4f + stare, () => Sfx("limb_sink", 6, at, "Unnatural", -4f, 10f, 110f));
	}

	/// <summary>The creature the lone limbs belong to: the breach's own once it's up, else one of their own.</summary>
	private LakeCreature Creature()
	{
		if (LastCreature != null && GodotObject.IsInstanceValid(LastCreature) && !LastCreature.IsQueuedForDeletion()) return LastCreature;
		if (_loner != null && GodotObject.IsInstanceValid(_loner) && !_loner.IsQueuedForDeletion() && _loner.Breaching) return _loner;
		_loner = new LakeCreature { Name = "LakeLoner" };
		Cutscene.SceneRoot(this).AddChild(_loner);
		_loner.Slammed += OnSlam;
		return _loner;
	}

	/// <summary>
	/// A limb strikes at the boat. It erupts out of the water ahead and off to one side
	/// (<paramref name="side"/>), rears, holds a moment looking down at the boat, and slams down where
	/// the boat will be if it keeps on as it is. About two and a half seconds to answer it: row hard and
	/// it comes down astern, or steer away (the prompt lights the key) and it comes down alongside.
	/// It never takes the boat itself; a hit throws it and all but stops it, and the hunter gains.
	/// </summary>
	private void Strike(int side)
	{
		Strikes++;
		const float rise = 0.85f, hold = 0.7f, windup = 0.98f;
		float when = rise + hold + windup;
		float length = 7.5f;
		Vector2 railAt = RailAt(Mathf.Clamp(_s + Mathf.Max(_v, 2.4f) * when, 0f, _total), out Vector2 dir);
		Vector2 sideV = new(-dir.Y, dir.X);
		Vector2 target = railAt + sideV * _lat;
		Vector2 root = target + sideV * side * LakeCreature.SlamReach(length);
		float water = _lake.GlobalPosition.Y;
		Vector3 rootW = _lake.ToGlobal(new Vector3(root.X, 0f, root.Y)) with { Y = water };
		Vector3 targetW = _lake.ToGlobal(new Vector3(target.X, 0f, target.Y)) with { Y = water };
		Creature().Surface(rootW, targetW, length, -1f, hold);
		LastScare = rootW;
		Sfx("breach_surge", 6, rootW, "Unnatural", -4f, 12f, 120f);
		Sfx("limb_rise_water", 8, rootW, "Unnatural", 0f, 10f, 100f);
		_shake = Mathf.Max(_shake, 0.25f);
		_rollVel += -side * 0.15f;
		// the way out: the prompt lights the steer key away from it
		_prompt.Next = -side;
		_ = Later(when + 0.4f, () => _prompt.Next = 0);
	}

	/// <summary>Runs <paramref name="act"/> after <paramref name="seconds"/> of game time, unless the
	/// crossing has been torn down by then.</summary>
	private async Task Later(float seconds, System.Action act)
	{
		await ToSignal(GetTree().CreateTimer(seconds, false), SceneTreeTimer.SignalName.Timeout);
		if (IsInstanceValid(this) && _boat != null && IsInstanceValid(_boat)) act();
	}

	// ------------------------------------------------------------------ the fog

	/// <summary>The fog thickens on the water as the crossing goes on (the dawn haze turning grey and
	/// close), and thins a little for the last stretch so the station comes out of it. It works the
	/// dawn mood's own fog, so it is put back exactly when the crossing ends or the level goes.</summary>
	private void DriveFog(float dt)
	{
		if (_fogBase < 0f)
		{
			if (_fogTarget <= 0f) return;
			_atmo = StoryBeat.Atmosphere(this);
			if (_atmo == null || _atmo.CurrentMood != ForestAtmosphere.Mood.Dawn) return;
			_fogBase = _atmo.FogDensityDawn;
			_fogBaseColor = _atmo.FogColorDawn;
		}
		if (_atmo == null || !IsInstanceValid(_atmo)) return;
		_fog = Mathf.MoveToward(_fog, _fogTarget, dt * 0.06f);
		_atmo.FogDensityDawn = Mathf.Lerp(_fogBase, FogThick, _fog);
		_atmo.FogColorDawn = _fogBaseColor.Lerp(FogGrey, Mathf.Min(1f, _fog * 1.2f));
	}

	/// <summary>The fog's density and colour at its thickest on the water.</summary>
	[Export] public float FogThick = 0.02f;
	[Export] public Color FogGrey = new(0.36f, 0.35f, 0.35f);

	private void RestoreFog()
	{
		if (_fogBase >= 0f && _atmo != null && IsInstanceValid(_atmo))
		{
			_atmo.FogDensityDawn = _fogBase;
			_atmo.FogColorDawn = _fogBaseColor;
		}
		_fogBase = -1f;
		_fog = _fogTarget = 0f;
	}

	public override void _ExitTree() => RestoreFog();

	private readonly float[] _strokeDur = { 0.62f, 0.62f };
	private readonly System.Collections.Generic.List<AudioStreamPlayer3D> _oarVoices = new();

	/// <summary>The blade goes in: the catch (a plunge), and the pull through the water, pitched to how
	/// fast the stroke is (a hurried stroke is a shorter, sharper rush). Rougher water, heavier strokes.</summary>
	private void OnBladeIn(int side, Vector3 at)
	{
		Vector3 w = at with { Y = _lake.WaterHeightAt(at, out _) };
		bool rough = InCurrent;
		LakeFx.Splash(Cutscene.SceneRoot(this), w, rough ? 0.7f : 0.45f, rough ? 12 : 7);
		LakeFx.Ripple(Cutscene.SceneRoot(this), w, 1.3f, 1.4f, 0.35f);
		float dur = _strokeDur[side < 0 ? 0 : 1];
		Sfx("row_catch", 10, w, "Player", (rough ? -7f : -11f) - (_pair ? 2.5f : 0f), 4f);
		var pull = Sfx("row_pull", 10, w, "Player", (rough ? -6f : -10f) - (_pair ? 2.5f : 0f), 4f);
		// (2026-10-13, the "bouncy" sweep: up to 1.35 a hurried stroke's water went a fifth up, a chipmunk splash; the
		// stroke's speed shows in it, but only a little)
		if (pull != null) pull.PitchScale = Mathf.Clamp(0.62f / Mathf.Max(0.25f, dur), 0.93f, 1.1f) * _rng.RandfRange(0.97f, 1.03f);
		// the loom knocking home in its lock as the oar loads, and turning in it (often, not always)
		Vector3 lockAt = _boat.ToGlobal(Rowboat.OarlockLocal(side));
		if (_rng.Randf() < 0.6f) Sfx("row_lock_knock", 8, lockAt, "Player", (rough ? -14f : -17f) - (_pair ? 2f : 0f), 2f);
		if (_rng.Randf() < 0.35f) Sfx("row_lock_creak", 8, lockAt, "Player", -17f);
	}

	private void OnBladeOut(int side, Vector3 at)
	{
		Vector3 w = at with { Y = _lake.WaterHeightAt(at, out _) };
		Sfx("row_release", 10, w, "Player", (InCurrent ? -9f : -13f) - (_pair ? 2.5f : 0f), 4f);
		// the oar swinging back seats in its lock now and then
		if (_rng.Randf() < 0.3f) Sfx("row_lock_knock", 8, _boat.ToGlobal(Rowboat.OarlockLocal(side)), "Player", -20f, 2f);
		LakeFx.Ripple(Cutscene.SceneRoot(this), w, 0.7f, 1.0f, 0.25f);
	}

	/// <summary>A faint V of ripples spreading behind the boat while it makes way.</summary>
	private void Wake(float dt)
	{
		if (Mathf.Abs(_v) < 0.8f) return;
		_wakeTimer -= dt * Mathf.Abs(_v);
		if (_wakeTimer > 0f) return;
		_wakeTimer = 1.1f;
		Vector3 stern = _boat.ToGlobal(new Vector3(0, 0, Rowboat.HalfLength));
		LakeFx.Ripple(Cutscene.SceneRoot(this), stern with { Y = _lake.WaterHeightAt(stern, out _) }, 2.4f, 2.6f, 0.2f);
	}

	/// <summary>Rings of disturbed water spreading here and there around the boat (something under it).</summary>
	private void RingsAround(int count, float rMin, float rMax)
	{
		for (int i = 0; i < count; i++)
		{
			float a = _rng.RandfRange(0f, Mathf.Tau), r = _rng.RandfRange(rMin, rMax);
			Vector3 p = _boat.GlobalPosition + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
			LakeFx.Ripple(Cutscene.SceneRoot(this), p with { Y = _lake.WaterHeightAt(p, out _) }, _rng.RandfRange(2.5f, 4.5f), 2.5f, 0.4f);
		}
	}

	/// <summary>A big swell hits the bow: it rears, spray comes over, the boat is shoved back.</summary>
	private void Swell()
	{
		_v -= SwellShove;
		_pitchVel += 0.3f;
		_rollVel += _rng.RandfRange(-0.15f, 0.15f);
		_shake = Mathf.Max(_shake, 0.06f);
		Vector3 bow = _boat.ToGlobal(new Vector3(0, 0.3f, -Rowboat.HalfLength));
		LakeFx.Splash(Cutscene.SceneRoot(this), bow, 1.4f, 20, (_boat.GlobalBasis.Z * 1.2f));
		Sfx("bow_slap", 8, bow, "Weather", -2f, 6f);
		if (_rng.Randf() < 0.5f) Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", -6f);
	}

	// ------------------------------------------------------------------ the hunt, and being taken

	/// <summary>
	/// The fight back is a chase: one limb follows the boat from behind, a shadow under the water
	/// with its eyes breaking the surface, its wake churning, a low undertone coming up under the boat
	/// as it closes. It is a little slower than easy rowing, so a steady rower draws away; but every
	/// few seconds it comes for the boat (a surge: it rears higher, the water boils behind, a thud from
	/// under the lake) faster than easy rowing and slower than hard, and over the last stretch it gives
	/// chase all the way to the beach. Never a surprise take: inside <see cref="WarnGap"/> the prompt
	/// goes red, it knocks on the hull and rears over the stern, and only after
	/// <see cref="WarnSeconds"/> of that does it have the boat.
	/// </summary>
	private void StartHunt()
	{
		_huntOn = true;
		_hunt = _s - HuntStart;
		_huntLat = _lat;
		_knockTimer = 0;
		_surgeAt = HuntGrace + _rng.RandfRange(2.5f, 4f);
		_surgeLeft = 0;
		_warnT = 0f;
		if (LastCreature != null && GodotObject.IsInstanceValid(LastCreature))
			LastCreature.AddHunter(RailWorld(_hunt, _huntLat));
		_huntRumble?.Play();
		_ = StoryBeat.Caption(this, "Row. It's following you.", 0.4f, 1.8f, 0.9f);
	}

	private float _huntLat;
	/// <summary>For tests: the closest the hunter came (m).</summary>
	public float MinGap { get; private set; } = 999f;

	private Vector3 RailWorld(float s, float lat = 0f)
	{
		Vector2 p = RailAt(Mathf.Clamp(s, 0f, _total), out Vector2 dir);
		p += new Vector2(-dir.Y, dir.X) * lat;
		return _lake.ToGlobal(new Vector3(p.X, 0f, p.Y));
	}

	/// <summary>Moves the hunter on; true once it has the boat.</summary>
	private bool Hunt(float dt, double clock)
	{
		if (_nearMissT >= 0f) { NearMiss(dt, clock); return false; }
		// its pace: steady, a surge now and then, the dash at the end
		if (!FinalDash && Progress >= FinalDashAt)
		{
			FinalDash = true;
			GD.Print($"[lake] the final dash: it is {_s - _hunt:0.0} m behind, {StrikeHits} strikes hit");
			_surgeLeft = 0;
			Vector3 behind = RailWorld(_hunt, _huntLat) with { Y = _lake.GlobalPosition.Y };
			PlayFoghorn(behind, 2f);
			LakeFx.Column(Cutscene.SceneRoot(this), behind, 5f);
			_shake = Mathf.Max(_shake, 0.35f);
			_ = StoryBeat.Caption(this, "Row!", 0.15f, 1.2f, 0.6f);
		}
		else if (!FinalDash && clock >= _surgeAt && clock > HuntGrace)
		{
			Surges++;
			_surgeLeft = SurgeSeconds;
			_surgeAt = clock + SurgeSeconds + _rng.RandfRange(SurgeEvery.X, SurgeEvery.Y);
			Vector3 at = RailWorld(_hunt, _huntLat) with { Y = _lake.GlobalPosition.Y };
			Sfx("deep_thoom", 6, at + Vector3.Down * 3f, "Unnatural", -2f, 16f);
			Sfx("limb_rise_water", 8, at, "Unnatural", -2f, 10f, 90f);
			LakeFx.Splash(Cutscene.SceneRoot(this), at + Vector3.Up * 0.3f, 1.6f, 24);
			if (Surges == 1) _ = StoryBeat.Caption(this, "It's coming. Row hard.", 0.2f, 1.6f, 0.8f);
		}
		bool surging = _surgeLeft > 0;
		if (surging) _surgeLeft -= dt;
		float pace = FinalDash ? FinalDashSpeed : surging ? SurgeSpeed : HuntSpeed;
		if (clock > HuntGrace) _hunt += pace * dt;
		_huntLat = Mathf.MoveToward(_huntLat, _lat, dt * 1.2f);
		// it never falls hopelessly far behind: a fast rower draws away, but it keeps coming
		_hunt = Mathf.Max(_hunt, _s - HuntStart * 1.5f);
		float gap = _s - _hunt;
		if (clock > HuntGrace) MinGap = Mathf.Min(MinGap, gap);

		// fair warning before the take: inside the warning gap long enough, and only then
		if (gap < WarnGap)
		{
			if (!_warning) { _warning = true; Warnings++; Sfx("hull_knock_below", 8, _boat.ToGlobal(new Vector3(0, -0.1f, Rowboat.HalfLength * 0.8f)), "Unnatural", 0f, 4f); _knockTimer = 0.6; }
			_warnT += dt;
		}
		else { _warning = false; _warnT = Mathf.Max(0f, _warnT - dt * 2f); }
		if (gap <= CatchGap && _warnT < WarnSeconds) { _hunt = _s - CatchGap; gap = CatchGap; }

		bool hot = surging || FinalDash;
		float near = HunterLooks(dt, gap, hot, surging, Mathf.Lerp(0.12f, 0.6f, Near(gap)) + (hot ? 0.2f : 0f) + (_warning ? 0.2f : 0f));
		// close behind: it knocks on the hull
		_knockTimer -= dt;
		if (near > 0.6f && _knockTimer <= 0)
		{
			_knockTimer = _rng.RandfRange(1.0f, 2.0f);
			Sfx("hull_knock_below", 8, _boat.ToGlobal(new Vector3(0, -0.1f, Rowboat.HalfLength * 0.8f)), "Unnatural", -4f, 4f);
			_pitchVel -= 0.1f;
		}
		_prompt.Danger = Mathf.Max(Mathf.Clamp((WarnGap + 4f - gap) / 4f, 0f, 1f), surging || FinalDash ? 0.55f : 0f);
		return gap <= CatchGap;
	}

	/// <summary>0 with the hunter far back, 1 with it at the catch gap.</summary>
	private float Near(float gap) => 1f - Mathf.Clamp((gap - CatchGap) / (HuntStart - CatchGap), 0f, 1f);

	/// <summary>Shows the hunter where it is now, <paramref name="gap"/> metres behind the boat: its limb at
	/// <paramref name="rise"/> (0 a shadow, 1 reared right up), its shadow under the water, the heartbeat and its
	/// undertone, and its wake churning toward the boat (harder when <paramref name="hot"/>). Returns how near it is.</summary>
	private float HunterLooks(float dt, float gap, bool hot, bool bigWake, float rise)
	{
		float near = Near(gap);
		Vector3 hw = RailWorld(_hunt, _huntLat);
		Vector2 l = new Vector2(hw.X - _lake.GlobalPosition.X, hw.Z - _lake.GlobalPosition.Z);
		_lake.Waves.ShadowAt = l;
		_lake.Waves.ShadowRadius = 7f;
		_lake.Waves.Shadow = 0.45f + 0.45f * near;
		if (LastCreature != null && GodotObject.IsInstanceValid(LastCreature))
			LastCreature.SetHunter(hw, _boat.GlobalPosition, rise);
		if (_heart != null) _heart.VolumeDb = Mathf.Lerp(-16f, -2f, near);
		if (_huntVoice != null)
		{
			_huntVoice.GlobalPosition = hw with { Y = _lake.GlobalPosition.Y - 0.5f };
			if (_huntRumble != null) _huntRumble.VolumeDb = Mathf.Lerp(-22f, 0f, near) + (hot ? 4f : 0f);
		}
		// its wake: the water churning where it goes, harder when it comes for the boat
		_huntWakeT -= dt;
		if (_huntWakeT <= 0f)
		{
			_huntWakeT = hot ? 0.16f : 0.4f;
			Vector3 w = hw with { Y = _lake.WaterHeightAt(hw, out _) };
			Vector3 toBoat = _boat.GlobalPosition - hw; toBoat.Y = 0;
			Vector3 front = w + toBoat.Normalized() * 1.6f;
			LakeFx.Ripple(Cutscene.SceneRoot(this), front, bigWake ? 3.2f : 2.2f, 1.8f, 0.35f);
			if (hot || near > 0.5f) LakeFx.Splash(Cutscene.SceneRoot(this), front + Vector3.Up * 0.2f, 0.8f, 8, toBoat.Normalized() * 0.8f);
		}
		return near;
	}

	// ------------------------------------------------------------------ the near miss

	private float _nearMissT = -1f, _nearMissFrom;
	private bool _nearMissUp, _nearMissDown;
	/// <summary>The near miss's beats (s): the rush in under the water, up behind the stern, and back under.</summary>
	private const float NearRush = 0.9f, NearUp = 3.1f, NearGone = 5.2f;

	/// <summary>
	/// The near miss (Dan, 2026-10-13): a good rower had never once let the hunter close in, so on every crossing,
	/// mid-to-late, it gets close anyway. Its wake comes racing up behind, it goes quiet under the stern, then it
	/// breaks the surface right behind the boat with its foghorn bellow and a burst of water, rears there (every eye on the
	/// boat, in plain view for anyone who looks back), the stern dragged down and the boat rocked; then it sinks
	/// away and drops back. Scripted from first to last (<see cref="NearMiss"/>): no warning, no take, the boat
	/// keeps its way, the surges wait for it, and the hunt goes on from <see cref="HuntStart"/> behind.
	/// </summary>
	private void BeginNearMiss()
	{
		if (!_huntOn || FinalDash || Caught) return;
		NearMisses++;
		_nearMissT = 0f;
		_nearMissFrom = Mathf.Max(_s - _hunt, NearMissGap + 0.5f);
		_nearMissUp = _nearMissDown = false;
		_surgeLeft = 0;
		_warning = false; _warnT = 0f;
		Vector3 at = RailWorld(_hunt, _huntLat) with { Y = _lake.GlobalPosition.Y };
		Sfx("deep_thoom", 6, at + Vector3.Down * 3f, "Unnatural", 0f, 16f);
		GD.Print($"[lake] the near miss: it comes for the stern from {_nearMissFrom:0.0} m back");
	}

	/// <summary>Moves the near miss on (in place of <see cref="Hunt"/> while it plays).</summary>
	private void NearMiss(float dt, double clock)
	{
		_nearMissT += dt;
		float t = _nearMissT;
		float gap;
		if (t < NearRush) gap = Mathf.Lerp(_nearMissFrom, NearMissGap, Mathf.SmoothStep(0f, 1f, t / NearRush));
		else if (t < NearUp) gap = NearMissGap;
		else gap = Mathf.Lerp(NearMissGap, HuntStart, Mathf.SmoothStep(0f, 1f, (t - NearUp) / (NearGone - NearUp)));
		_hunt = _s - gap;
		_huntLat = Mathf.MoveToward(_huntLat, _lat, dt * 3f);
		NearMissClosest = Mathf.Min(NearMissClosest, gap);
		MinGap = Mathf.Min(MinGap, gap);
		Vector3 stern = RailWorld(_s - Rowboat.HalfLength - 0.6f, _lat) with { Y = _lake.GlobalPosition.Y };

		if (!_nearMissUp && t >= NearRush)
		{
			// up, right behind them
			_nearMissUp = true;
			Sfx("breach_surge", 6, stern, "Unnatural", 0f, 12f, 120f);
			PlayFoghorn(stern + Vector3.Down * 1f, -1f);   // its own voice, the foghorn, right behind them
			Sfx("limb_rise_water", 8, stern, "Unnatural", 0f, 10f, 90f);
			Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", -1f);
			Sfx("bilge_slosh", 8, _boat.GlobalPosition, "Player", -6f, 3f);
			LakeFx.Column(Cutscene.SceneRoot(this), stern, 3.5f);
			LakeFx.Splash(Cutscene.SceneRoot(this), stern + Vector3.Up * 0.4f, 2.2f, 40, _boat.GlobalBasis.Z * -1.5f);
			for (int i = 0; i < 3; i++) LakeFx.Ripple(Cutscene.SceneRoot(this), stern, 3f + i * 1.6f, 2.4f + i * 0.4f, 0.45f);
			// the stern dragged down, the bow lifted, the boat rolled and shaken (her way kept: it's a scare, not a hit)
			_pitchVel += 0.45f;
			_dipVel -= 0.3f;
			_rollVel += _rng.Randf() < 0.5f ? -0.35f : 0.35f;
			_shake = Mathf.Max(_shake, 0.4f);
			if (_heart != null) _heart.VolumeDb = 0f;
		}
		if (!_nearMissDown && t >= NearUp)
		{
			// and back under
			_nearMissDown = true;
			Sfx("limb_sink", 6, stern, "Unnatural", -2f, 10f, 100f);
			Sfx("deep_thoom", 6, stern + Vector3.Down * 3f, "Unnatural", -6f, 16f);
			LakeFx.Splash(Cutscene.SceneRoot(this), stern + Vector3.Up * 0.2f, 1.2f, 16);
			RingsAround(3, 2f, 5f);
		}
		float rise = t < NearRush ? 0.15f : t < NearUp ? 1f : 0.05f;
		HunterLooks(dt, gap, true, t < NearRush, rise);
		_prompt.Danger = t < NearUp ? 1f : Mathf.Lerp(1f, 0.3f, (t - NearUp) / (NearGone - NearUp));
		if (t >= NearGone)
		{
			_nearMissT = -1f;
			_surgeAt = clock + _rng.RandfRange(SurgeEvery.X, SurgeEvery.Y);
			GD.Print($"[lake] the near miss is over: {NearMissClosest:0.0} m at its closest, back to {_s - _hunt:0.0} m");
		}
	}


	/// <summary>Taken: it rears up over the stern, the boat goes over, and the player is dragged down
	/// through the murk — where the eye from before rises out of the dark to meet them. Then black,
	/// and back to the checkpoint at the lake's shore.</summary>
	private async Task Drown(PlayerController player, CancellationToken ct)
	{
		PlayerDeath.Begin();
		_prompt.SetShown(false);
		Cutscene.Lock(player, input: true);
		var rig = player.CameraRig;
		var under = UnderwaterView.For(this);
		var murk = new Color(0.05f, 0.09f, 0.08f);
		Vector3 behind = _boat.ToGlobal(new Vector3(0, 0, Rowboat.HalfLength + 2.2f));
		LastCreature?.SetHunter(behind with { Y = _lake.GlobalPosition.Y }, _boat.GlobalPosition, 1f);
		LastCreature?.HunterStrike();
		Sfx("breach_surge", 6, behind, "Unnatural", -2f, 12f, 120f);
		LakeFx.Column(Cutscene.SceneRoot(this), behind with { Y = _lake.GlobalPosition.Y }, 6f);
		_shake = 0.6f;
		if (_heart != null) _heart.VolumeDb = 0f;
		if (_huntRumble != null) _huntRumble.VolumeDb = 3f;
		await LookAt(player, () => behind + Vector3.Up * 4f, 1.1f, 3f, ct);
		// it comes down on the boat itself
		Sfx("hull_hit", 6, _boat.GlobalPosition, "Unnatural", 2f, 10f);
		Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", 0f);
		// over she goes
		double t = 0;
		while (t < 0.9)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			_capsize = Mathf.SmoothStep(0f, 1.9f, (float)(t / 0.9));
		}
		// under: dragged down and back, the light going green then black above
		_riding = null;
		rig.PitchSwim = 0f;
		rig.RollSwim = 0f;
		Vector3 from = player.GlobalPosition;
		Vector3 to = from + Vector3.Down * 3.4f + _boat.GlobalBasis.Z * 2.5f;
		Sfx("deep_thoom", 6, from + Vector3.Down * 4f, "Unnatural", 0f, 10f);
		t = 0;
		const double sink = 3.2;
		while (t < sink)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / sink);
			player.GlobalPosition = from.Lerp(to, Mathf.SmoothStep(0f, 1f, u));
			under.Set(Mathf.Clamp(u * 4f, 0f, 1f), murk, 0.35f * u);
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.DegToRad(35f), dt * 1.5f));
		}
		// the eye, rising out of the dark in front of them
		var eye = new Node3D { Name = "DrownEye" };
		Cutscene.SceneRoot(this).AddChild(eye);
		Vector3 fwd = -rig.Camera.GlobalBasis.Z; fwd.Y = 0; fwd = fwd.Normalized();
		Vector3 eyeAt = rig.Camera.GlobalPosition + fwd * 3.2f + Vector3.Down * 2.5f;
		eye.GlobalPosition = eyeAt;
		LakeCreature.BuildEye(eye);
		eye.Scale = Vector3.One * 1.4f;
		Sfx("creature_eye_open", 8, eyeAt, "Unnatural", 0f, 6f);
		Sfx("creature_wet_moan", 6, eyeAt + Vector3.Down * 2f, "Unnatural", -4f, 8f);
		t = 0;
		while (t < 2.6)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / 2.6);
			Vector3 cam = rig.Camera.GlobalPosition;
			eye.GlobalPosition = eyeAt.Lerp(cam + fwd * 2.1f + Vector3.Down * 0.1f, Mathf.SmoothStep(0f, 1f, u));
			eye.GlobalBasis = Basis.LookingAt(cam - eye.GlobalPosition, Vector3.Up).Scaled(Vector3.One * 1.4f);
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.DegToRad(-8f), dt * 2f));
			under.Set(1f, murk, 0.35f + 0.55f * u);
		}
		await PlayerDeath.Reload(this, "The lake took you.", ct);
	}


	// ------------------------------------------------------------------ the breach

	/// <summary>
	/// The breach: the act's centrepiece, about eleven seconds with the input taken (the view is
	/// steered throughout; the player only watches). Beat by beat:
	/// 1-4. The burst (Dan, 2026-10-13: "remove the giant eyeball before the kraken jumps out of the water, just have it
	///    jump out of the water and scare the player"; there was a stop, the view drawn down into the water, a house-sized
	///    eye opening in the deep and a dome of water first, about five seconds of build-up). Now, mid-stroke, out of the
	///    hush: it breaks the surface ten metres ahead with a roar of water and its foghorn bellow, a column of spray, the
	///    boat thrown back, rolled and stopped dead, the view snapped up to it and then dragged up the colossus, one limb
	///    as thick as a tree and fifteen metres tall, as it towers; then the ring of others: a ring wave that heaves the
	///    boat, the whole world regraded red for a moment (once, easing away), the field of view punched wide, the lake
	///    turned white-capped.
	/// 5. Surrounded: two more come up right beside the boat.
	/// 6. Every eye on every limb opens at once, with a wet chorus, and turns to the player; a beat;
	///    they all blink, together.
	/// 7. The nearest limb bends over the boat and hangs there, dripping, looking down into it; the
	///    view is pulled up into its eyes.
	/// 8. Another rears and slams down beside the boat: the boat is thrown, spray comes over.
	/// 9. It lets go: most of it slides back under, the colossus last; two stay up, watching.
	/// </summary>
	private LakeCreature _breachCreature;
	private Task _breachPrep = Task.CompletedTask;

	/// <summary>What breaks the surface, built ahead from the hush on, a limb a frame, unseen (the lag pass, 2026-10-13:
	/// built all at once as it broke, it held that frame a quarter of a second), where the boat will be when it breaks:
	/// 10 m ahead of the breach point, on the boat's line.</summary>
	private void PrepareBreach()
	{
		if (_breachCreature != null) return;
		float bs = BreachAtFraction * _total;
		float water = _lake.GlobalPosition.Y;
		Vector3 boatAt = RailWorld(bs, _lat) with { Y = water };
		Vector3 center = RailWorld(bs + 10f, _lat) with { Y = water };
		_breachCreature = new LakeCreature { Name = "LakeCreature" };
		Cutscene.SceneRoot(this).AddChild(_breachCreature);
		_breachPrep = _breachCreature.PrepareOverFrames(center, boatAt);
	}

	private async Task Breach(PlayerController player, CancellationToken ct)
	{
		InBreach = true;
		_prompt.SetShown(false);
		Cutscene.Lock(player, input: true);
		var rig = player.CameraRig;
		try
		{
			float water = _lake.GlobalPosition.Y;
			Vector3 shoreW = _lake.ToGlobal(new Vector3(_p2.X, 0f, _p2.Y));
			// (built ahead, unseen, from the hush on: PrepareBreach. Dan, 2026-10-13: no giant eye in the deep first, no
			// stop and look down into the water; it just bursts up out of the lake in front of the boat as they row)
			if (_breachCreature == null) PrepareBreach();
			var creature = _breachCreature;
			await _breachPrep;
			Vector3 centerW = creature.GlobalPosition with { Y = water };
			Vector2 centerL = Local2(centerW);

			// 1. the burst: it breaks the surface ahead with no warning, the boat thrown back and stopped dead
			creature.Breach(centerW, _boat.GlobalPosition, BreachHoldSeconds);
			creature.Slammed += OnSlam;
			LastCreature = creature;
			Sfx("breach_surge", 6, centerW, "Unnatural", 4f, 18f, 160f);
			PlayFoghorn(centerW + Vector3.Down * 2f, 3f);
			Sfx("limb_slam_water", 8, centerW, "Unnatural", 0f, 14f, 140f);
			ForestAmbienceManager.Instance?.Startle(1f, 3f);
			LakeFx.Column(Cutscene.SceneRoot(this), centerW, 7f);
			LakeFx.Splash(Cutscene.SceneRoot(this), centerW + Vector3.Up * 0.5f, 3f, 60);
			Vector3 bow = _boat.ToGlobal(new Vector3(0, 0.3f, -Rowboat.HalfLength));
			LakeFx.Splash(Cutscene.SceneRoot(this), bow, 1.8f, 30, _boat.GlobalBasis.Z * 1.6f);
			Sfx("bow_slap", 8, bow, "Weather", 2f, 6f);
			Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", 0f);
			Sfx("bilge_slosh", 8, _boat.GlobalPosition, "Player", -4f, 3f);
			_lake.Waves.RingAt = centerL;
			float shadow0 = _lake.Waves.Shadow;
			_ = RampWaves((ref LakeShape.Waves w, float u) => w.Shadow = Mathf.Lerp(shadow0, 0f, u), 0.7f, ct);
			_ = RampWaves((ref LakeShape.Waves w, float u) => { w.RingRadius = Mathf.Lerp(3f, 70f, u); w.RingHeight = Mathf.Lerp(0.55f, 0f, u); }, 9f, ct);
			_ = RampWaves((ref LakeShape.Waves w, float u) => w.Intensity = Mathf.SmoothStep(0f, 1f, u), 3f, ct);
			var coast = CoastTo(0f, 0.9f, ct);
			// the boat rears back from it and rolls; the view jolts
			_pitchVel += 0.6f;
			_rollVel += _rng.Randf() < 0.5f ? -0.4f : 0.4f;
			_dipVel -= 0.4f;
			_shake = 0.8f;
			if (_heart != null) { _heart.VolumeDb = -4f; _heart.Play(); }
			_ = RedWash(ct);
			_ = FovPunch(rig, ct);
			// the view snapped up to it, then dragged up the colossus as it towers out of the lake
			await LookAt(player, () => centerW + Vector3.Up * 4f, 0.35f, 9f, ct);
			await LookAt(player, () => creature.ColossusTop() ?? centerW + Vector3.Up * 8f, 2.0f, 2.6f, ct);
			await coast;

			// 5. surrounded: the others come up all round, two right beside the boat
			Sfx("breach_surge", 6, _boat.GlobalPosition + _boat.GlobalBasis.X * 5f, "Unnatural", -4f, 12f, 120f);
			await LookAt(player, () => _boat.GlobalPosition + _boat.GlobalBasis.X * 5f + Vector3.Up * 3.5f, 0.8f, 3.4f, ct);
			await LookAt(player, () => _boat.GlobalPosition - _boat.GlobalBasis.X * 5f + Vector3.Up * 3.5f, 0.75f, 3.4f, ct);

			// 6. every eye opens at once, and turns to them; then they all blink, together
			creature.OpenEyes();
			Sfx("creature_eye_open", 8, centerW + Vector3.Up * 4f, "Unnatural", -1f, 12f);
			Sfx("creature_eye_open", 8, _boat.GlobalPosition + _boat.GlobalBasis.X * 4f + Vector3.Up * 3f, "Unnatural", -4f, 8f);
			Sfx("creature_eye_open", 8, _boat.GlobalPosition - _boat.GlobalBasis.X * 4f + Vector3.Up * 3f, "Unnatural", -4f, 8f);
			Sfx("whisper_voice", 3, centerW + Vector3.Up * 6f, "Unnatural", -4f, 10f);
			await LookAt(player, () => creature.ColossusTop() ?? centerW + Vector3.Up * 8f, 1.1f, 3f, ct);
			creature.Blink();
			Sfx("creature_eye_blink", 6, centerW + Vector3.Up * 5f, "Unnatural", -3f, 12f);
			await Cutscene.Wait(this, 0.5, ct);

			// 7. the nearest one bends over the boat and looks down into it
			if (creature.Loom(_boat.GlobalPosition))
			{
				if (_heart != null) _heart.VolumeDb = -3f;
				Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", -4f);
				// its flesh right over them: suckers letting go as it bends, and a deep wet groan from somewhere in it
				Sfx("sucker_pops", 8, _boat.GlobalPosition + Vector3.Up * 4f, "Unnatural", -6f, 5f);
				_ = Later(0.9f, () => Sfx("creature_wet_moan", 6, _boat.GlobalPosition + Vector3.Up * 5f, "Unnatural", -8f, 8f));
				_ = DripOnBoat(2.6f, ct);
				await LookAt(player, () => creature.LoomBend() ?? _boat.GlobalPosition + Vector3.Up * 5f, 1.1f, 2.6f, ct);
				await LookAt(player, () => creature.LoomTip() ?? _boat.GlobalPosition + Vector3.Up * 4f, 1.1f, 3f, ct);
				Sfx("whisper_voice", 3, _boat.GlobalPosition + Vector3.Up * 3f, "Unnatural", -6f, 4f);
			}

			// 8. another rears up and comes down beside the boat
			if (creature.Slam(_boat.GlobalPosition))
			{
				await LookAt(player, () => centerW + Vector3.Up * 6f, 0.8f, 3f, ct);
				await Cutscene.Wait(this, 0.6, ct);
			}

			// 9. it lets go: most of it slides back under, the colossus last; two stay, watching
			creature.Release();
			_ = FovTo(rig, 0f, 2.5f, ct);
			await LookAt(player, () => creature.ColossusTop() ?? centerW, 0.9f, 2.4f, ct);
			await LookAt(player, () => shoreW, 1.0f, 3.4f, ct);
			await LevelPitch(player, 0.3f, ct);
		}
		finally
		{
			InBreach = false;
			rig.FovSwim = 0f;
			Cutscene.Unlock(player, input: true);
		}
		_prompt.SetShown(true);
	}

	private Vector2 Local2(Vector3 world)
	{
		Vector3 l = world - _lake.GlobalPosition;
		return new Vector2(l.X, l.Z);
	}

	/// <summary>Steers the whole view (yaw and pitch, unlike <see cref="StoryBeat.PanTowards"/>) toward a
	/// moving point for <paramref name="seconds"/>, easing in at <paramref name="sharpness"/>.</summary>
	private async Task LookAt(PlayerController player, System.Func<Vector3> target, float seconds, float sharpness, CancellationToken ct)
	{
		var rig = player.CameraRig;
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			Vector3 to = target() - rig.Camera.GlobalPosition;
			float flat = new Vector2(to.X, to.Z).Length();
			if (flat < 0.05f && Mathf.Abs(to.Y) < 0.05f) continue;
			float k = 1f - Mathf.Exp(-sharpness * dt);
			float wantYaw = Mathf.Atan2(-to.X, -to.Z);
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, wantYaw) * k, 0));
			float wantPitch = Mathf.Clamp(Mathf.Atan2(to.Y, flat) - rig.PitchSwim, Mathf.DegToRad(-70f), Mathf.DegToRad(70f));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, wantPitch, k));
		}
	}

	/// <summary>Eases the camera's extra field of view to <paramref name="deg"/>.</summary>
	private async Task FovTo(PlayerCameraRig rig, float deg, float seconds, CancellationToken ct)
	{
		float from = rig.FovSwim;
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			rig.FovSwim = Mathf.Lerp(from, deg, Mathf.SmoothStep(0f, 1f, (float)(t / seconds)));
		}
		rig.FovSwim = deg;
	}

	/// <summary>The breach's lurch in perspective: the view thrown wide in a fifth of a second, then
	/// settling back most of the way over three (the world stays a little too big for comfort).</summary>
	private async Task FovPunch(PlayerCameraRig rig, CancellationToken ct)
	{
		await FovTo(rig, 12f, 0.2f, ct);
		await FovTo(rig, 5f, 3f, ct);
	}

	/// <summary>The vast shadow gliding under the boat on the approach: it comes in from off to one side
	/// behind, passes beneath, and settles out ahead, where it will break the surface.</summary>
	private async Task ShadowPass(CancellationToken ct)
	{
		Vector3 right = _boat.GlobalBasis.X; right.Y = 0;
		Vector3 fwd = -_boat.GlobalBasis.Z; fwd.Y = 0;
		Vector2 from = Local2(_boat.GlobalPosition - fwd.Normalized() * 10f + right.Normalized() * 12f);
		Vector2 to = Local2(_boat.GlobalPosition + fwd.Normalized() * 9f);
		await RampWaves((ref LakeShape.Waves w, float u) =>
		{
			w.ShadowAt = from.Lerp(to, Mathf.SmoothStep(0f, 1f, u));
			w.ShadowRadius = 11f;
			w.Shadow = 0.85f * Mathf.SmoothStep(0f, 0.35f, u);
		}, 5f, ct);
	}

	/// <summary>An early pass, smaller: a dark shape crossing under the boat from one side to the
	/// other (aimed at where the boat will be halfway through), then gone again into the deep.</summary>
	private async Task ShadowPass(CancellationToken ct, float seconds, float strength)
	{
		Vector3 right = _boat.GlobalBasis.X; right.Y = 0; right = right.Normalized();
		Vector3 fwd = -_boat.GlobalBasis.Z; fwd.Y = 0; fwd = fwd.Normalized();
		Vector3 mid = _boat.GlobalPosition + fwd * Mathf.Max(_v, 1f) * seconds * 0.5f;
		Vector2 from = Local2(mid + right * 11f - fwd * 3f);
		Vector2 to = Local2(mid - right * 11f + fwd * 3f);
		await RampWaves((ref LakeShape.Waves w, float u) =>
		{
			w.ShadowAt = from.Lerp(to, u);
			w.ShadowRadius = 6.5f;
			w.Shadow = strength * Mathf.Sin(u * Mathf.Pi);
		}, seconds, ct);
	}

	/// <summary>Water running off the limb hanging over the boat, pattering onto the boards and the water round it.</summary>
	private async Task DripOnBoat(float seconds, CancellationToken ct)
	{
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Wait(this, 0.22, ct);
			t += 0.22;
			Vector3 p = _boat.ToGlobal(new Vector3(_rng.RandfRange(-1f, 1f), 0.35f, _rng.RandfRange(-1.4f, 1.2f)));
			LakeFx.Splash(Cutscene.SceneRoot(this), p, 0.22f, 4);
			// mostly on the boards, now and then over the side into the lake
			if (_rng.Randf() < 0.7f) Sfx(_rng.Randf() < 0.75f ? "limb_drip_wood" : "limb_drip_water", 8, p, "Water", -14f, 3f);
		}
	}

	private delegate void WaveSetter(ref LakeShape.Waves w, float u);

	private async Task RampWaves(WaveSetter set, float seconds, CancellationToken ct)
	{
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			set(ref _lake.Waves, Mathf.Min(1f, (float)(t / seconds)));
		}
		set(ref _lake.Waves, 1f);
	}

	private async Task CoastTo(float speed, float seconds, CancellationToken ct)
	{
		float from = _v;
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			_v = Mathf.Lerp(from, speed, Mathf.SmoothStep(0f, 1f, (float)(t / seconds)));
			_thrust = 0f;
			_s += _v * dt;
		}
		_v = speed;
	}

	/// <summary>The red: eased in once, held a breath, then let go over a couple of seconds. One wash,
	/// never repeated or pulsed (Reduce Flashing makes it gentler still and slower to arrive).</summary>
	private async Task RedWash(CancellationToken ct)
	{
		bool reduce = GameSettings.Instance?.ReduceFlashing ?? false;
		float peak = reduce ? 0.35f : 0.82f, rise = reduce ? 0.6f : 0.18f;
		var rect = _red != null ? FindRedRect() : null;
		if (rect != null) rect.Visible = true;
		double t = 0;
		const double hold = 0.45, fall = 2.4;
		while (t < rise + hold + fall)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float a = t < rise ? Mathf.SmoothStep(0f, 1f, (float)(t / rise))
				: t < rise + hold ? 1f
				: 1f - Mathf.SmoothStep(0f, 1f, (float)((t - rise - hold) / fall));
			SetRed(a * peak);
		}
		SetRed(0f);
		if (rect != null) rect.Visible = false;
	}

	private ColorRect FindRedRect()
	{
		var layer = Cutscene.SceneRoot(this).GetNodeOrNull<CanvasLayer>("BreachRed");
		return layer?.GetChildCount() > 0 ? layer.GetChild(0) as ColorRect : null;
	}

	private void OnSlam(Vector3 at)
	{
		if (!InBreach) { OnStrike(at); return; }
		// the slam's wave hits the boat: shoved sideways, tipped, soaked
		Vector3 away = _boat.GlobalPosition - at; away.Y = 0;
		float side = _boat.GlobalBasis.X.Dot(away.Normalized());
		_rollVel += side * 0.7f;
		_pitchVel += 0.25f;
		_dipVel -= 0.4f;
		_yawVel += side * 0.2f;
		_shake = 0.6f;
		Sfx("limb_slam_water", 8, at, "Unnatural", 3f, 16f, 140f);
		LakeFx.Splash(Cutscene.SceneRoot(this), _boat.GlobalPosition + Vector3.Up * 0.6f - away.Normalized() * 0.8f, 2f, 30, away.Normalized() * 0.6f);
		Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", -2f);
		Sfx("bilge_slosh", 8, _boat.GlobalPosition, "Player", -6f, 3f);
	}

	/// <summary>A limb's strike (<see cref="Strike"/>) comes down. On the boat (the hull and a little
	/// round it): the gunwale's hit, the boat thrown over on its side and all but stopped, water over the
	/// side. Beside it: the near miss, spray across the boat and a hard rock, the closer the harder.</summary>
	private void OnStrike(Vector3 at)
	{
		Vector3 local = _boat.ToLocal(at);
		float dx = Mathf.Max(0f, Mathf.Abs(local.X) - Rowboat.Beam * 0.5f);
		float dz = Mathf.Max(0f, Mathf.Abs(local.Z) - Rowboat.HalfLength);
		float miss = Mathf.Sqrt(dx * dx + dz * dz);
		Vector3 away = _boat.GlobalPosition - at; away.Y = 0;
		away = away.LengthSquared() > 0.01f ? away.Normalized() : _boat.GlobalBasis.X;
		float side = _boat.GlobalBasis.X.Dot(away);
		Sfx("limb_slam_water", 8, at, "Unnatural", 4f, 16f, 140f);
		GD.Print($"[lake] strike came down {miss:0.0} m off the hull");
		if (miss < 0.8f)
		{
			StrikeHits++;
			_v = Mathf.Min(_v, 1.2f);
			_thrust = 0f;
			_rollVel += side * 1.3f;
			_pitchVel += 0.4f;
			_dipVel -= 0.7f;
			_yawVel += side * 0.45f;
			_latVel += side * 1.5f;
			_shake = 0.9f;
			LakeFx.Splash(Cutscene.SceneRoot(this), _boat.GlobalPosition + Vector3.Up * 0.8f, 2.6f, 40, away * 0.8f + Vector3.Up * 0.4f);
			Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", 0f);
			Sfx("hull_hit", 6, _boat.GlobalPosition, "Player", 2f, 4f);
			return;
		}
		float k = 1f - Mathf.Clamp((miss - 0.8f) / 5f, 0f, 1f);
		_rollVel += side * 0.75f * k;
		_pitchVel += 0.2f * k;
		_dipVel -= 0.35f * k;
		_latVel += side * 0.8f * k;
		_shake = Mathf.Max(_shake, 0.25f + 0.4f * k);
		if (k > 0.2f) LakeFx.Splash(Cutscene.SceneRoot(this), _boat.GlobalPosition + Vector3.Up * 0.6f - away * 0.8f, 1.2f + 1.2f * k, 26, away * 0.6f);
		Sfx("hull_creak", 10, _boat.GlobalPosition, "Player", -6f + 4f * k);
		if (k > 0.3f) Sfx("bilge_slosh", 8, _boat.GlobalPosition, "Player", -12f + 6f * k, 3f);
	}

	private void PlayFoghorn(Vector3 at, float db = 4f)
	{
		Sfx("creature_foghorn", 2, at, "Unnatural", db, 12f, 140f);
	}

	// ------------------------------------------------------------------ landing

	/// <summary>The last metres: the bow grinds up the gravel and stops; the limbs behind go under; the
	/// rower ships the oars, stands, and steps over the bow onto the beach.</summary>
	private async Task Land(PlayerController player, CancellationToken ct)
	{
		Cutscene.Lock(player, input: true);
		_prompt.SetShown(false);
		LastCreature?.SinkAll();
		if (_loner != null && GodotObject.IsInstanceValid(_loner)) _loner.SinkAll();
		_huntRumble?.CreateTween().TweenProperty(_huntRumble, "volume_db", -60f, 3f);
		_prompt.Danger = 0f;
		_huntOn = false;
		_ = RampWaves((ref LakeShape.Waves w, float u) => w.Shadow *= 1f - u, 2f, ct);
		try
		{
			// run in on the last of the way, slowing as the keel takes the ground
			float v0 = Mathf.Max(_v, 1.6f);
			bool scraped = false;
			while (_s < _total)
			{
				await Cutscene.Frame(this, ct);
				float dt = (float)GetProcessDeltaTime();
				float left = _total - _s;
				_v = Mathf.Max(0.25f, Mathf.Lerp(0.3f, v0, Mathf.Clamp(left / 2.6f, 0f, 1f)));
				_s = Mathf.Min(_total, _s + _v * dt);
				if (!scraped && left < 1.3f) { scraped = true; Sfx("boat_ground", 1, _boat.ToGlobal(new Vector3(0, 0, -Rowboat.HalfLength)), "Player", -1f); }
				// riding up the beach: the bow lifts, the hull rises out of the water
				float up = Mathf.SmoothStep(1.3f, 0f, left);
				_groundLift = 0.14f * up;
				_groundPitch = 0.06f * up;
			}
			_v = 0f;
			_shake = 0.45f;
			_pitchVel -= 0.4f;
			_ = RampWaves((ref LakeShape.Waves w, float u) => w.Intensity = Mathf.Lerp(1f, 0.35f, u), 6f, ct);
			await Cutscene.Wait(this, 0.6, ct);

			_boat.Stow(true);
			Sfx("row_lock_creak", 8, _boat.ToGlobal(Rowboat.OarlockLocal(1)), "Player", -8f);
			Sfx("row_lock_knock", 8, _boat.ToGlobal(Rowboat.OarlockLocal(-1)), "Player", -10f, 2f);
			await Cutscene.Wait(this, 0.5, ct);

			// stand
			var rig = player.CameraRig;
			var stand = player.CreateTween();
			stand.TweenProperty(rig, "EyeHeight", 1.62f, 0.9f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			Sfx("cloth", 4, _boat.GlobalPosition, "Player", -8f);
			_rollVel += 0.3f;
			await Cutscene.Tween(this, stand, ct);

			// forward along the boat to the bow, then over it and down onto the gravel
			_riding = null;
			rig.PitchSwim = 0f;
			rig.RollSwim = 0f;
			rig.Shake = Vector3.Zero;
			Vector3 bow = _boat.ToGlobal(new Vector3(0, 0.12f, -Rowboat.HalfLength + 0.55f));
			await Glide(player, bow, 0.8f, 0f, ct);
			player.Footsteps?.StepNow("wood");
			_dipVel -= 0.3f;
			await StoryBeat.PanTowards(this, player, _lake.StationApproachWorld, 0.4f, ct);
			await Glide(player, _lake.FarDockWorld, 0.75f, 0.35f, ct);
			player.Footsteps?.StepNow("stone", 2f);
			_rollVel += 0.5f;
			await Cutscene.Wait(this, 0.2, ct);
			player.Footsteps?.StepNow("stone");
			_heart?.CreateTween().TweenProperty(_heart, "volume_db", -60f, 5f);
		}
		finally { Cutscene.Unlock(player, input: true); }
	}

	private async Task Glide(PlayerController player, Vector3 to, float seconds, float hop, CancellationToken ct)
	{
		Vector3 from = player.GlobalPosition;
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = Mathf.SmoothStep(0f, 1f, (float)(t / seconds));
			player.GlobalPosition = from.Lerp(to, u) + Vector3.Up * (Mathf.Sin(u * Mathf.Pi) * hop);
		}
		player.GlobalPosition = to;
	}

	// ------------------------------------------------------------------ sound

	/// <summary>One of <paramref name="variants"/> takes of <c>sfx/{name}_NN.wav</c> (or <c>{name}.wav</c>
	/// / <c>{name}_01.wav</c> for a single take), placed in the world, on a bus.</summary>
	private readonly System.Collections.Generic.Dictionary<string, int> _lastTake = new();

	private AudioStreamPlayer3D Sfx(string name, int variants, Vector3 at, string bus, float db, float unit = 5f, float maxDistance = 70f)
	{
		// never the same take twice running (the owner's rule: no repetitive sounds)
		int n = Mathf.Max(1, variants), take = _rng.RandiRange(1, n);
		if (n > 1 && _lastTake.TryGetValue(name, out int last) && take == last) take = take % n + 1;
		_lastTake[name] = take;
		string path = $"res://assets/audio/sfx/{name}_{take:00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return null;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = db,
			UnitSize = unit, MaxDistance = maxDistance, PitchScale = _rng.RandfRange(0.94f, 1.05f),
		};
		// the oars: at most eight of their sounds at once (the oldest is cut), however hard the rowing
		if (name.StartsWith("row_"))
		{
			_oarVoices.RemoveAll(v => !IsInstanceValid(v));
			while (_oarVoices.Count >= 8) { var old = _oarVoices[0]; _oarVoices.RemoveAt(0); if (IsInstanceValid(old)) old.QueueFree(); }
			_oarVoices.Add(voice);
		}
		Cutscene.SceneRoot(this).AddChild(voice);
		voice.GlobalPosition = at;
		voice.Finished += voice.QueueFree;
		voice.Play();
		SoundCaption.Heard(this, name, at);
		return voice;
	}

	// ------------------------------------------------------------------ arrival: on foot, at the door

	/// <summary>Control is back once <see cref="Landed"/>; the player walks up to the station
	/// themselves. Stepping into its doorway is the act's end: checkpoint 10, then a fade carries the
	/// player into the lobby (<see cref="StationInterior"/>), the same way the bunker's vault door
	/// admits them. The real ending waits on Act 13's own last puzzle.</summary>
	private void OnArrival(PlayerController player)
	{
		if (_arrived || !Landed) return;
		_arrived = true;
		Arrived = true;
		_ = Cutscene.Run(this, async ct =>
		{
			GD.Print("[story] Act 12: reached the rescue station");
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act12LakeCrossed);
			var fader = StoryBeat.Fader(this);
			if (fader != null) await fader.Fade(1f, 0.8f, ct);
			ForestAmbienceManager.Instance?.ReleaseSilence(this);
			RestoreFog();
			_heart?.Stop();
			if (_rough != null) _rough.Stop();
			_glide?.Stop();
			var station = StationInterior.Instance;
			// (a new game left the station unbuilt at load: built now, in this black, with its shaders warmed)
			if (station is { Built: false })
			{
				station.EnsureBuilt();
				await station.WhenBuilt(ct);
			}
			if (station != null) player.Teleport(station.EntranceMarkerWorld, station.EntranceYaw);
			if (fader != null) await fader.Fade(0f, 0.9f, ct);
			GD.Print("[story] Act 13: into the forester station");
		}, lockInput: true, freezeBody: true);
	}
}
