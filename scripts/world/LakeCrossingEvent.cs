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
/// 2. The calm row. Player-paced and on rails: alternate A (left oar) and D (right oar) — a stroke
///    only counts when it switches side, so holding one key does nothing — with the
///    <see cref="RowingPrompt"/> keycaps showing which is next. Each stroke is a real push (the boat
///    has momentum and water drag, it never teleports), swings the bow a touch off the other way,
///    and throws a splash; the boat rides the lake's actual wave surface and the view rocks with it.
///    Birds, a loon. The mouse is free to look around throughout.
/// 3. Midway, it goes wrong: the birds stop, something thuds under the water, the water ahead domes
///    up — and tentacles covered in bloodshot eyes erupt all round (a <see cref="LakeCreature"/>): a
///    foghorn bellow, the world briefly regraded red (once, easing away; Reduce Flashing softens it),
///    a ring wave that tosses the boat, the lake turning white-capped, every eye opening and turning
///    to the player, one limb rearing up and slamming down beside the boat. About eight seconds.
/// 4. The fight back. Most of it sinks; two limbs stay up behind, watching. The chop runs back toward
///    the near shore: a current drags the boat back toward them and swells shove it, so only fast
///    alternating strokes make headway (the prompt's arrow shows which way the boat is really going).
/// 5. Landing: the bow grinds up onto the far beach, the limbs sink, the player stands, steps over the
///    bow onto the gravel, and walks up to the rescue station. Its doorway is the act's end: checkpoint
///    10, then a fade carries them inside (Act 13, <see cref="StationInterior"/>).
/// </summary>
public partial class LakeCrossingEvent : Node3D
{
	[Export] public NodePath LakePath = "../Lake";
	/// <summary>A stroke's push on the calm leg (m/s added to the boat's way, over the drive): an
	/// unhurried row, never rushed.</summary>
	[Export] public float StrokeImpulse = 0.6f;
	/// <summary>The calm leg's top speed however fast the keys go (m/s).</summary>
	[Export] public float CalmMaxSpeed = 2.1f;
	/// <summary>A stroke's push once the creature is up (fear lends strength).</summary>
	[Export] public float RoughStrokeImpulse = 1.0f;
	/// <summary>Water drag on the boat's way, per second.</summary>
	[Export] public float Drag = 0.6f;
	/// <summary>Fraction (0..1) of the crossing where the creature breaches.</summary>
	[Export] public float BreachAtFraction = 0.55f;
	/// <summary>Kept for tests and the changelog's "7-10 seconds": the breach's length, give or take.</summary>
	[Export] public double BreachHoldSeconds = 8.0;
	/// <summary>The current's steady pull back toward the near shore once the water turns (m/s²).</summary>
	[Export] public float CurrentPull = 1.5f;
	/// <summary>Seconds between the big swells in the current phase, and how hard each shoves the boat back.</summary>
	[Export] public Vector2 SwellEvery = new(2.8f, 4.2f);
	[Export] public float SwellShove = 0.7f;
	/// <summary>How far behind the breach point the current may drag the boat (it never pushes it all the way home).</summary>
	[Export] public float MaxFallback = 8f;
	[Export] public float PaddleSideThreshold = 0.45f;
	/// <summary>Kept for compatibility with older tuning; the current is now <see cref="CurrentPull"/>.</summary>
	[Export] public float CurrentPushbackPerSecond = 1.5f;

	// ---- test hooks (mirrors the public bools other story beats expose) ----
	public bool Boarded { get; private set; }
	public bool Paddling { get; private set; }
	public bool InBreach { get; private set; }
	public bool InCurrent { get; private set; }
	public bool Landed { get; private set; }
	public bool Arrived { get; private set; }
	public int StrokeCount { get; private set; }
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
	private AudioStreamPlayer3D _lap, _rough;
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

	// strokes
	private bool? _lastStrokeWasLeft;
	private bool _leftHeld, _rightHeld;
	private double _lastStrokeTime, _lastLeftTime, _lastRightTime;

	public override void _Ready()
	{
		Callable.From(Setup).CallDeferred();
	}

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

		// The trigger sits in the doorway itself: stepping through it (not just onto the porch) ends the act.
		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(2f, 2.6f, 1.6f) },
			ToLocal(_lake.StationDoorWorld) + new Vector3(0, 1.2f, 0), OnArrival, "LakeArrival");
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
		const string heart = "res://assets/audio/ambient/heartbeat_loop.wav";
		if (ResourceLoader.Exists(heart))
		{
			_heart = new AudioStreamPlayer { Name = "Heartbeat", Stream = Looping(heart), Bus = "Player", VolumeDb = -80f };
			AddChild(_heart);
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
		// springs: yaw wobble back to the rail heading, rocking back to level, the dip back up
		Spring(ref _yawOff, ref _yawVel, 0f, 3.2f, 1.6f, dt);
		Spring(ref _pitchOff, ref _pitchVel, 0f, 9f, 2.2f, dt);
		Spring(ref _rollOff, ref _rollVel, 0f, 7f, 1.6f, dt);
		Spring(ref _dip, ref _dipVel, 0f, 16f, 3f, dt);

		Vector2 rail = RailAt(_s, out Vector2 dir);
		Vector2 side = new(-dir.Y, dir.X);
		Vector2 pos = rail + side * (_yawOff * 1.2f);
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
		// every frame, whatever the beat: moored, rowing, stopped dead in the breach, run aground
		PlaceBoat(dt);
		if (_riding != null && GodotObject.IsInstanceValid(_riding))
		{
			// camera jolts decay quickly
			_shake = Mathf.MoveToward(_shake, 0f, dt * 1.8f);
			_riding.CameraRig.Shake = _shake > 0.001f
				? new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), 0) * _shake * 0.045f
				: Vector3.Zero;
		}
		// the rough water's roar follows the chop
		if (_rough != null) _rough.VolumeDb = Mathf.Lerp(-40f, -4f, Mathf.Clamp(_lake.Waves.Intensity, 0f, 1f));
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
			await Board(player, ct);
			Boarded = true;
			Paddling = true;
			_prompt.SetShown(true);
			await Row(player, _pushLen, BreachAtFraction * _total, false, ct);
			Paddling = false;
			await Breach(player, ct);
			Paddling = true;
			InCurrent = true;
			await Row(player, BreachAtFraction * _total, _total - 2.6f, true, ct);
			InCurrent = false;
			Paddling = false;
			await Land(player, ct);
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
			Sfx("boat_creak", 3, _boat.GlobalPosition, "Player", -6f);

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
			Sfx("oarlock_creak", 3, _boat.ToGlobal(Rowboat.OarlockLocal(-1)), "Player", -8f);
			await Cutscene.Wait(this, 0.25, ct);
			Sfx("oarlock_creak", 3, _boat.ToGlobal(Rowboat.OarlockLocal(1)), "Player", -8f);
			_rollVel = 0.4f;
			_yawVel = 0.15f;
			// drift out along the push-off leg on the shove alone
			_v = 1.3f;
			while (_s < _pushLen)
			{
				await Cutscene.Frame(this, ct);
				float dt = (float)GetProcessDeltaTime();
				_v = Mathf.Max(0.35f, _v - _v * 0.45f * dt);
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

	private void ResetStrokeTracking() { _lastStrokeWasLeft = null; _leftHeld = false; _rightHeld = false; }

	/// <summary>Reads one frame of paddling: -1 / +1 when a correctly alternating stroke just went down on
	/// that side, else 0.</summary>
	private int ReadStroke(PlayerController player)
	{
		float mx = player.PlayerInput.Move.X;
		bool leftNow = mx < -PaddleSideThreshold, rightNow = mx > PaddleSideThreshold;
		bool leftEdge = leftNow && !_leftHeld, rightEdge = rightNow && !_rightHeld;
		_leftHeld = leftNow; _rightHeld = rightNow;
		if (leftEdge && _lastStrokeWasLeft != true) { _lastStrokeWasLeft = true; return -1; }
		if (rightEdge && _lastStrokeWasLeft != false) { _lastStrokeWasLeft = false; return 1; }
		return 0;
	}

	/// <summary>Rows from rail distance <paramref name="from"/> to <paramref name="to"/>. In the current
	/// (<paramref name="rough"/>), a steady pull and periodic swells drag the boat back toward the
	/// breach, never more than <see cref="MaxFallback"/> behind where this leg began. On the calm leg,
	/// the lake hushes and something stirs under the water in the last stretch before the breach.</summary>
	private async Task Row(PlayerController player, float from, float to, bool rough, CancellationToken ct)
	{
		ResetStrokeTracking();
		_prompt.Urgent = rough;
		_prompt.ShowDrift = rough;
		_prompt.Next = 0;
		float floor = Mathf.Max(_pushLen, from - (rough ? MaxFallback : 0f));
		double clock = 0, swellAt = _rng.RandfRange(SwellEvery.X * 0.5f, SwellEvery.Y);
		bool hushed = false, stirred = false, shadowed = false;
		_s = Mathf.Max(_s, from);
		while (_s < to)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			clock += dt;
			int side = player.PlayerInput.Enabled ? ReadStroke(player) : 0;
			if (side != 0) Stroke(side, rough, clock);

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
			if (!rough) _v = Mathf.Min(_v, CalmMaxSpeed);
			_s += _v * dt;
			if (_s < floor) { _s = floor; _v = Mathf.Max(_v, 0f); }
			Wake(dt);
			_prompt.Drift = _v;

			if (!rough)
			{
				// the lake goes quiet...
				if (!hushed && _s > to - 9f)
				{
					hushed = true;
					_lake.Hush();
					ForestAmbienceManager.Instance?.RequestSilence(this, 0.62f, 5);
					Sfx("underwater_thoom", 1, _boat.GlobalPosition + Vector3.Down * 6f, "Unnatural", -6f, 20f);
					RingsAround(3, 5f, 9f);
				}
				// ...a vast shape slides under the boat...
				if (!shadowed && _s > to - 7f)
				{
					shadowed = true;
					_ = ShadowPass(ct);
				}
				// ...and something big moves under the boat
				if (!stirred && _s > to - 4f)
				{
					stirred = true;
					Sfx("underwater_thoom", 1, _boat.GlobalPosition + Vector3.Down * 4f, "Unnatural", -1f, 20f);
					_pitchVel += 0.25f;
					_dipVel -= 0.35f;
					RingsAround(5, 3f, 7f);
				}
			}
			if (_heart != null && rough) _heart.VolumeDb = Mathf.Lerp(_heart.VolumeDb, _v < 0.5f ? -6f : -12f, dt);
		}
	}

	private void Stroke(int side, bool rough, double clock)
	{
		StrokeCount++;
		double sinceSame = clock - (side < 0 ? _lastLeftTime : _lastRightTime);
		if (side < 0) _lastLeftTime = clock; else _lastRightTime = clock;
		_lastStrokeTime = clock;
		float duration = Mathf.Clamp((float)sinceSame * 0.95f, 0.28f, 0.62f);
		_boat.Stroke(side, rough ? 1.3f : 1f, duration);
		_thrust += rough ? RoughStrokeImpulse : StrokeImpulse;
		// a left stroke turns the bow right, and the other way round; each pull rocks the boat a little
		_yawVel += -side * 0.07f;
		_pitchVel += 0.02f;
		_rollVel += side * 0.025f;
		_prompt.Pressed(side);
		_prompt.Next = -side;
	}

	private void OnBladeIn(int side, Vector3 at)
	{
		Vector3 w = at with { Y = _lake.WaterHeightAt(at, out _) };
		bool rough = InCurrent;
		LakeFx.Splash(Cutscene.SceneRoot(this), w, rough ? 0.7f : 0.45f, rough ? 12 : 7);
		LakeFx.Ripple(Cutscene.SceneRoot(this), w, 1.3f, 1.4f, 0.35f);
		Sfx("oar_stroke", 4, w, "Player", rough ? -3f : -7f, 5f);
		if (_rng.Randf() < 0.3f) Sfx("oarlock_creak", 3, _boat.ToGlobal(Rowboat.OarlockLocal(side)), "Player", -12f);
	}

	private void OnBladeOut(int side, Vector3 at)
	{
		Vector3 w = at with { Y = _lake.WaterHeightAt(at, out _) };
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
		Sfx("wave_slap", 4, bow, "Weather", -2f, 6f);
		if (_rng.Randf() < 0.5f) Sfx("boat_creak", 3, _boat.GlobalPosition, "Player", -6f);
	}

	// ------------------------------------------------------------------ the breach

	/// <summary>
	/// The breach: the act's centrepiece, about thirteen seconds with the input taken (the view is
	/// steered throughout; the player only watches). Beat by beat:
	/// 1. The lake holds its breath. The boat drifts to a dead stop on water gone still; a heartbeat
	///    comes up; the view is drawn down over the bow, into the water, as the field of view narrows.
	/// 2. An eye opens down there — the size of a house, seen through the peaty water under the
	///    shadow that slid beneath the boat — its pupil blown wide, then snapping to a goat's slit:
	///    it has found the player. Whispering.
	/// 3. It comes up. The eye shuts; the water over it domes up; a foghorn bellow from under the lake.
	/// 4. The breach. First and slowest, one limb as thick as a tree and fifteen metres tall, the view
	///    dragged up it as it towers; then the ring of others: plumes of water, a ring wave that
	///    heaves the boat, the whole world regraded red for a moment (once, easing away), the field of
	///    view punched wide, the lake turned white-capped.
	/// 5. Surrounded: two more come up right beside the boat.
	/// 6. Every eye on every limb opens at once, with a wet chorus, and turns to the player; a beat;
	///    they all blink, together.
	/// 7. The nearest limb bends over the boat and hangs there, dripping, looking down into it; the
	///    view is pulled up into its eyes.
	/// 8. Another rears and slams down beside the boat: the boat is thrown, spray comes over.
	/// 9. It lets go: most of it slides back under, the colossus last; two stay up, watching.
	/// </summary>
	private async Task Breach(PlayerController player, CancellationToken ct)
	{
		InBreach = true;
		_prompt.SetShown(false);
		Cutscene.Lock(player, input: true);
		var rig = player.CameraRig;
		try
		{
			Vector3 fwd = -_boat.GlobalBasis.Z; fwd.Y = 0; fwd = fwd.Normalized();
			float water = _lake.GlobalPosition.Y;
			Vector3 side = _boat.GlobalBasis.X; side.Y = 0; side = side.Normalized();
			// right under the gunwale: the view is taken down over the side, straight into it
			Vector3 eyeW = (_boat.GlobalPosition + side * 4.2f + fwd * 1.5f) with { Y = water };
			Vector3 centerW = (_boat.GlobalPosition + fwd * 10f) with { Y = water };
			Vector3 shoreW = _lake.ToGlobal(new Vector3(_p2.X, 0f, _p2.Y));
			Vector2 eyeL = Local2(eyeW), centerL = Local2(centerW);

			// 1. the lake holds its breath
			var coast = CoastTo(0f, 1.6f, ct);
			if (_heart != null) { _heart.VolumeDb = -24f; _heart.Play(); _heart.CreateTween().TweenProperty(_heart, "volume_db", -11f, 2.5f); }
			Vector2 shadowFrom = _lake.Waves.Shadow > 0.05f ? _lake.Waves.ShadowAt : eyeL;
			_ = RampWaves((ref LakeShape.Waves w, float u) =>
			{
				w.ShadowAt = shadowFrom.Lerp(eyeL, Mathf.SmoothStep(0f, 1f, u));
				w.ShadowRadius = 16f;
				w.Shadow = Mathf.Max(w.Shadow, 0.9f * u);
			}, 1.8f, ct);
			_ = FovTo(rig, -6f, 2.4f, ct);
			await LookAt(player, () => eyeW + Vector3.Down * 2.5f, 1.9f, 2.4f, ct);
			await coast;

			// 2. an eye opens in the deep, and finds them (its pupil turns toward the boat: across, in its own axes)
			Vector2 toBoat = new(0f, -1f);
			_lake.Waves.EyeAt = eyeL;
			_lake.Waves.EyeRadius = 5.5f;
			_lake.Waves.EyeAxis = new Vector2(fwd.X, fwd.Z);
			_lake.Waves.EyePupil = 1f;
			_lake.Waves.EyeLook = Vector2.Zero;
			Sfx("whisper_voice", 3, eyeW + Vector3.Down * 3f, "Unnatural", -12f, 8f);
			_ = LookAt(player, () => eyeW + Vector3.Down * 2.5f, 2.0f, 3f, ct);
			await RampWaves((ref LakeShape.Waves w, float u) => w.EyeOpen = Mathf.SmoothStep(0f, 1f, u), 1.9f, ct);
			await Cutscene.Wait(this, 0.35, ct);
			Sfx("underwater_thoom", 1, eyeW + Vector3.Down * 5f, "Unnatural", -9f, 20f);
			await RampWaves((ref LakeShape.Waves w, float u) =>
			{
				w.EyePupil = Mathf.Lerp(1f, 0.06f, Mathf.SmoothStep(0f, 1f, u));
				w.EyeLook = toBoat * Mathf.SmoothStep(0f, 1f, u);
			}, 0.45f, ct);
			_shake = 0.12f;
			Sfx("whisper_voice", 3, eyeW + Vector3.Down * 2f, "Unnatural", -6f, 8f);
			await Cutscene.Wait(this, 1.0, ct);

			// 3. it comes up: the eye shuts, the water domes, the horn sounds
			_lake.Waves.BulgeAt = centerL;
			PlayFoghorn(centerW);
			var shut = RampWaves((ref LakeShape.Waves w, float u) => { w.EyeOpen = 1f - Mathf.SmoothStep(0f, 1f, u); w.Shadow = Mathf.Lerp(0.9f, 0.35f, u); }, 0.9f, ct);
			var bulge = RampWaves((ref LakeShape.Waves w, float u) => w.Bulge = Mathf.SmoothStep(0f, 1.7f, u), 1.4f, ct);
			_ = FovTo(rig, 0f, 1.2f, ct);
			_dipVel -= 0.3f;
			await LookAt(player, () => centerW + Vector3.Up * 1.5f, 1.4f, 3f, ct);
			await bulge;
			await shut;

			// 4. the breach
			var creature = new LakeCreature { Name = "LakeCreature" };
			Cutscene.SceneRoot(this).AddChild(creature);
			creature.Breach(centerW, _boat.GlobalPosition, BreachHoldSeconds);
			creature.Slammed += OnSlam;
			LastCreature = creature;
			Sfx("breach_erupt", 1, centerW, "Unnatural", 3f, 18f, 160f);
			ForestAmbienceManager.Instance?.Startle(1f, 3f);
			_lake.Waves.RingAt = centerL;
			_ = RampWaves((ref LakeShape.Waves w, float u) => { w.Bulge = Mathf.Lerp(1.7f, 0f, Mathf.SmoothStep(0f, 1f, u)); w.Shadow = Mathf.Lerp(0.35f, 0f, u); }, 0.7f, ct);
			_ = RampWaves((ref LakeShape.Waves w, float u) => { w.RingRadius = Mathf.Lerp(3f, 70f, u); w.RingHeight = Mathf.Lerp(0.55f, 0f, u); }, 9f, ct);
			_ = RampWaves((ref LakeShape.Waves w, float u) => w.Intensity = Mathf.SmoothStep(0f, 1f, u), 3f, ct);
			_shake = 0.7f;
			if (_heart != null) _heart.VolumeDb = -6f;
			_ = RedWash(ct);
			_ = FovPunch(rig, ct);
			// the view is dragged up the colossus as it towers out of the lake
			await LookAt(player, () => creature.ColossusTop() ?? centerW + Vector3.Up * 8f, 2.7f, 2.2f, ct);

			// 5. surrounded: the others come up all round, two right beside the boat
			Sfx("breach_erupt", 1, _boat.GlobalPosition + _boat.GlobalBasis.X * 5f, "Unnatural", -4f, 12f, 120f);
			await LookAt(player, () => _boat.GlobalPosition + _boat.GlobalBasis.X * 5f + Vector3.Up * 3.5f, 1.1f, 3f, ct);
			await LookAt(player, () => _boat.GlobalPosition - _boat.GlobalBasis.X * 5f + Vector3.Up * 3.5f, 1.0f, 3f, ct);

			// 6. every eye opens at once, and turns to them; then they all blink, together
			creature.OpenEyes();
			Sfx("squelch_open", 2, centerW + Vector3.Up * 4f, "Unnatural", -1f, 12f);
			Sfx("squelch_open", 2, _boat.GlobalPosition + _boat.GlobalBasis.X * 4f + Vector3.Up * 3f, "Unnatural", -4f, 8f);
			Sfx("squelch_open", 2, _boat.GlobalPosition - _boat.GlobalBasis.X * 4f + Vector3.Up * 3f, "Unnatural", -4f, 8f);
			Sfx("whisper_voice", 3, centerW + Vector3.Up * 6f, "Unnatural", -4f, 10f);
			await LookAt(player, () => creature.ColossusTop() ?? centerW + Vector3.Up * 8f, 1.5f, 3f, ct);
			creature.Blink();
			Sfx("squelch_close", 2, centerW + Vector3.Up * 5f, "Unnatural", -3f, 12f);
			await Cutscene.Wait(this, 0.8, ct);

			// 7. the nearest one bends over the boat and looks down into it
			if (creature.Loom(_boat.GlobalPosition))
			{
				if (_heart != null) _heart.VolumeDb = -3f;
				Sfx("boat_creak", 3, _boat.GlobalPosition, "Player", -4f);
				_ = DripOnBoat(2.6f, ct);
				await LookAt(player, () => creature.LoomBend() ?? _boat.GlobalPosition + Vector3.Up * 5f, 1.4f, 2.2f, ct);
				await LookAt(player, () => creature.LoomTip() ?? _boat.GlobalPosition + Vector3.Up * 4f, 1.4f, 2.6f, ct);
				Sfx("whisper_voice", 3, _boat.GlobalPosition + Vector3.Up * 3f, "Unnatural", -6f, 4f);
			}

			// 8. another rears up and comes down beside the boat
			if (creature.Slam(_boat.GlobalPosition))
			{
				await LookAt(player, () => centerW + Vector3.Up * 6f, 0.8f, 3f, ct);
				await Cutscene.Wait(this, 1.0, ct);
			}

			// 9. it lets go: most of it slides back under, the colossus last; two stay, watching
			creature.Release();
			_ = FovTo(rig, 0f, 2.5f, ct);
			await LookAt(player, () => creature.ColossusTop() ?? centerW, 1.2f, 2f, ct);
			await LookAt(player, () => shoreW, 1.3f, 3f, ct);
			await LevelPitch(player, 0.3f, ct);
		}
		finally
		{
			InBreach = false;
			rig.FovSwim = 0f;
			Cutscene.Unlock(player, input: true);
		}
		_prompt.SetShown(true);
		_ = StoryBeat.Caption(this, "Row.", 0.3f, 1.4f, 0.8f, ct);
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
	/// behind, passes beneath, and settles out ahead, where the eye will open.</summary>
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
			if (_rng.Randf() < 0.4f) Sfx("oar_stroke", 4, p, "Player", -18f, 3f);
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
		// the slam's wave hits the boat: shoved sideways, tipped, soaked
		Vector3 away = _boat.GlobalPosition - at; away.Y = 0;
		float side = _boat.GlobalBasis.X.Dot(away.Normalized());
		_rollVel += side * 0.7f;
		_pitchVel += 0.25f;
		_dipVel -= 0.4f;
		_yawVel += side * 0.2f;
		_shake = 0.6f;
		Sfx("tentacle_slam", 2, at, "Unnatural", 3f, 16f, 140f);
		LakeFx.Splash(Cutscene.SceneRoot(this), _boat.GlobalPosition + Vector3.Up * 0.6f - away.Normalized() * 0.8f, 2f, 30, away.Normalized() * 0.6f);
		Sfx("boat_creak", 3, _boat.GlobalPosition, "Player", -2f);
	}

	private void PlayFoghorn(Vector3 at)
	{
		Sfx("creature_foghorn", 2, at, "Unnatural", 4f, 12f, 140f);
	}

	// ------------------------------------------------------------------ landing

	/// <summary>The last metres: the bow grinds up the gravel and stops; the limbs behind go under; the
	/// rower ships the oars, stands, and steps over the bow onto the beach.</summary>
	private async Task Land(PlayerController player, CancellationToken ct)
	{
		Cutscene.Lock(player, input: true);
		_prompt.SetShown(false);
		LastCreature?.SinkAll();
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
			Sfx("oarlock_creak", 3, _boat.ToGlobal(Rowboat.OarlockLocal(1)), "Player", -8f);
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
	private void Sfx(string name, int variants, Vector3 at, string bus, float db, float unit = 5f, float maxDistance = 70f)
	{
		string path = $"res://assets/audio/sfx/{name}_{_rng.RandiRange(1, Mathf.Max(1, variants)):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = db,
			UnitSize = unit, MaxDistance = maxDistance, PitchScale = _rng.RandfRange(0.94f, 1.05f),
		};
		Cutscene.SceneRoot(this).AddChild(voice);
		voice.GlobalPosition = at;
		voice.Finished += voice.QueueFree;
		voice.Play();
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
			_heart?.Stop();
			if (_rough != null) _rough.Stop();
			var station = StationInterior.Instance;
			if (station != null) player.Teleport(station.EntranceMarkerWorld, station.EntranceYaw);
			if (fader != null) await fader.Fade(0f, 0.9f, ct);
			GD.Print("[story] Act 13: into the forester station");
		}, lockInput: true, freezeBody: true);
	}
}
