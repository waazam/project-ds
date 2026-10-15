using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Rain of blood (Dan, 2026-09-22; Act 6's clearing loop, before the fall). A GpuParticles3D
/// emitter (the same kind RainVfx uses for the storm) that follows <see cref="Follow"/> from
/// above: dark red, elongated drops falling at 3-6 m/s under gravity, a box <see cref="HalfWidth"/>
/// x 2 across and a few metres tall so drops are already streaking at every height, plus a small
/// "near" emitter right around the eyes so streaks cross the view up close. The box is centred
/// AHEAD of a moving player (their velocity x <see cref="LeadSeconds"/>, capped) so the rain keeps
/// up when they run (Dan: it was too sparse while running). <see cref="Intensity"/> (0..1, eased)
/// drives the drop count from 12 % of <see cref="Drops"/> to all of them, the fog light's red
/// (ForestAtmosphere.BloodTint, up to <see cref="TintMax"/>, times <see cref="TintPulse"/> for the
/// daze), and the sound: the storm's rain loop, slowed to 0.85 and quiet on the Weather bus, from
/// -34 dB up to <see cref="LevelDb"/>. <see cref="Start"/> builds and begins it, <see cref="Stop"/>
/// fades everything out and frees the emitters. Self-contained: add it anywhere, set Follow,
/// Start(), drive Intensity, Stop().
/// </summary>
public partial class BloodRain : Node3D
{
	/// <summary>Who it falls around (the player).</summary>
	[Export] public Node3D Follow;
	/// <summary>Drops when Intensity is 1 (the emitter's amount; it starts at 12 % of this).</summary>
	[Export] public int Drops = 3600;
	/// <summary>Drops in the near emitter right around the eyes at Intensity 1.</summary>
	[Export] public int NearDrops = 320;
	/// <summary>Half the width of the box it falls from.</summary>
	[Export] public float HalfWidth = 12f;
	/// <summary>Centre height of the box above <see cref="Follow"/> (the box spans +-<see cref="HalfHeight"/> of it).</summary>
	[Export] public float Height = 9f;
	[Export] public float HalfHeight = 5f;
	/// <summary>The box leads a moving player by their velocity times this (metres per m/s), capped at <see cref="LeadMax"/>.</summary>
	[Export] public float LeadSeconds = 1.2f;
	[Export] public float LeadMax = 8f;
	/// <summary>Its rain sound at Intensity 1 (Weather bus; the storm's loop, slowed).</summary>
	[Export] public float LevelDb = -18f;
	/// <summary>How red the fog light goes at Intensity 1 (0..1).</summary>
	[Export] public float TintMax = 0.35f;
	/// <summary>How fast the applied intensity follows the requested one (per second).</summary>
	[Export] public float Ease = 1.2f;

	/// <summary>0..1 how hard it rains: the drop count, the red in the fog and the sound follow it (eased).</summary>
	public float Intensity { get; set; }
	/// <summary>A multiplier on the fog's red (the daze pulses it with the hum); 1 = none.</summary>
	public float TintPulse { get; set; } = 1f;
	/// <summary>How much of it reaches the player where they are (0..1, scales the drops, the sound and the red; 1 by
	/// default). Act 6 starts it at the cabin door at 0 and brings it in along the way to the bridge.</summary>
	public float Presence { get; set; } = 1f;
	/// <summary>It is running (from Start until Stop's fade begins).</summary>
	public bool Active { get; private set; }
	/// <summary>The intensity actually applied right now (lags <see cref="Intensity"/>).</summary>
	public float Applied => _applied;

	private GpuParticles3D _blood, _near, _splash;
	private AudioStreamPlayer _audio;
	private float _applied;
	private Vector3 _lead;
	/// <summary>(the gushy-water pass, 2026-10-15) How red the creek has run with it (0..1): it comes in slowly as it
	/// rains (the creek's water darkening to blood, slower and glossier), and runs clear again when it stops.</summary>
	public float CreekRed => _creekRed;
	private float _creekRed;
	private ForestTerrain _terrain;

	public override void _ExitTree()
	{
		if (StoryBeat.Atmosphere(this) is { } atmo && Active) atmo.BloodTint = 0f;
		_terrain?.SetStreamBlood(0f);
	}

	/// <summary>(the gushy-water pass) The drops landing: little red splashes thrown up off the ground round the player.</summary>
	private GpuParticles3D MakeSplash()
	{
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(5f, 0.05f, 5f),
			Direction = Vector3.Up, Spread = 40f,
			InitialVelocityMin = 0.6f, InitialVelocityMax = 1.4f,
			Gravity = new Vector3(0, -9.8f, 0),
			ScaleMin = 0.5f, ScaleMax = 1.1f,
			ParticleFlagAlignY = true,
		};
		var mat = DropMaterial();
		mat.AlbedoColor = new Color(0.36f, 0.02f, 0.02f, 0.8f);
		var e = new GpuParticles3D
		{
			Name = "Splash", Amount = 260, Lifetime = 0.28, Preprocess = 0.3f, Randomness = 1f,
			ProcessMaterial = pm,
			DrawPass1 = new QuadMesh { Size = new Vector2(0.014f, 0.05f), Material = mat },
			LocalCoords = false, TopLevel = true, Emitting = true,
			VisibilityAabb = new Aabb(new Vector3(-7, -2, -7), new Vector3(14, 4, 14)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			AmountRatio = 0f,
		};
		AddChild(e);
		return e;
	}

	private static StandardMaterial3D DropMaterial() => new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		AlbedoColor = new Color(0.42f, 0.02f, 0.02f, 0.85f),
		AlbedoTexture = RainVfx.StreakTexture(),
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
		BillboardKeepScale = true,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		DisableReceiveShadows = true,
	};

	private GpuParticles3D MakeEmitter(string name, int amount, Vector3 extents, float lifetime, Vector2 speed, Vector2 quad)
	{
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = extents,
			Direction = new Vector3(0.05f, -1f, 0.02f).Normalized(),
			Spread = 3f,
			InitialVelocityMin = speed.X,
			InitialVelocityMax = speed.Y,
			Gravity = new Vector3(0, -4.0f, 0),
			ScaleMin = 0.8f,
			ScaleMax = 1.4f,
			ParticleFlagAlignY = true,
		};
		var e = new GpuParticles3D
		{
			Name = name,
			Amount = amount,
			Lifetime = lifetime,
			Preprocess = 1.5f,   // already falling when it starts, no empty first second
			ProcessMaterial = pm,
			DrawPass1 = new QuadMesh { Size = quad, Material = DropMaterial() },   // thin streaks (Dan: the drops were too thick)
			LocalCoords = false,
			VisibilityAabb = new Aabb(new Vector3(-HalfWidth - 12, -Height - 20, -HalfWidth - 12), new Vector3(HalfWidth * 2 + 24, Height + 26, HalfWidth * 2 + 24)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			TopLevel = true,
			Emitting = true,
			AmountRatio = 0.12f * Mathf.Clamp(Presence, 0f, 1f),
		};
		AddChild(e);
		return e;
	}

	/// <summary>Builds the emitters and the sound and begins (at the current Intensity, eased in from 0).</summary>
	public void Start()
	{
		if (Active) return;
		Active = true;
		_applied = 0f;
		// The main box: tall, so drops are streaking at every height from the moment it starts, falling faster.
		_blood ??= MakeEmitter("Drops", Drops, new Vector3(HalfWidth, HalfHeight, HalfWidth), 2.6f, new Vector2(3.0f, 6.0f), new Vector2(0.012f, 0.30f));
		// The near box: a few metres round the eyes, so streaks cross right in front of the camera.
		_near ??= MakeEmitter("Near", NearDrops, new Vector3(2.2f, 1.6f, 2.2f), 0.9f, new Vector2(3.5f, 6.0f), new Vector2(0.01f, 0.22f));
		_splash ??= MakeSplash();
		_terrain ??= GroundSnap.FindTerrain(this);
		const string rain = "res://assets/audio/ambient/rain_loop.wav";
		if (_audio == null && ResourceLoader.Exists(rain))
		{
			_audio = new AudioStreamPlayer { Name = "Audio", Stream = GD.Load<AudioStream>(rain), Bus = "Weather", VolumeDb = -40f, PitchScale = 0.85f };
			AddChild(_audio);
			_audio.Play();
		}
		Place(0f);
		GD.Print("[story] it is raining blood");
	}

	/// <summary>Fades the drops, the sound and the red out over <paramref name="fadeSeconds"/>, then frees the emitters.</summary>
	public void Stop(float fadeSeconds = 3f)
	{
		if (!Active) return;
		Active = false;
		var blood = _blood; _blood = null;
		var near = _near; _near = null;
		var splash = _splash; _splash = null;
		var audio = _audio; _audio = null;
		var atmo = StoryBeat.Atmosphere(this);
		var tw = CreateTween().SetParallel();
		float f = Mathf.Max(0.05f, fadeSeconds * 0.6f);
		if (blood != null) tw.TweenProperty(blood, "amount_ratio", 0f, f);
		if (near != null) tw.TweenProperty(near, "amount_ratio", 0f, f);
		if (splash != null) tw.TweenProperty(splash, "amount_ratio", 0f, f);
		// the creek runs clear again, slowly
		var terrain = _terrain;
		if (terrain != null && IsInstanceValid(terrain) && _creekRed > 0f)
			terrain.CreateTween().TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(terrain)) terrain.SetStreamBlood(v); }), _creekRed, 0f, Mathf.Max(0.05f, fadeSeconds * 3f));
		_creekRed = 0f;
		if (audio != null) tw.TweenProperty(audio, "volume_db", -60f, Mathf.Max(0.05f, fadeSeconds));
		if (atmo != null) tw.TweenMethod(Callable.From<float>(v => atmo.BloodTint = v), atmo.BloodTint, 0f, Mathf.Max(0.05f, fadeSeconds));
		tw.Chain().TweenCallback(Callable.From(() =>
		{
			foreach (var e in new[] { blood, near, splash })
				if (e != null && IsInstanceValid(e)) { e.Emitting = false; e.QueueFree(); }
			if (audio != null && IsInstanceValid(audio)) audio.QueueFree();
		}));
	}

	public override void _Process(double delta)
	{
		if (!Active || _blood == null) return;
		float want = Mathf.Clamp(Intensity, 0f, 1f);
		_applied = Mathf.MoveToward(_applied, want, (float)delta * Ease);
		float pr = Mathf.Clamp(Presence, 0f, 1f);
		_blood.AmountRatio = Mathf.Lerp(0.12f, 1f, _applied) * pr;
		if (_near != null) _near.AmountRatio = Mathf.Lerp(0.1f, 1f, _applied) * pr;
		if (_splash != null) _splash.AmountRatio = Mathf.Lerp(0.1f, 1f, _applied) * pr;
		// the creek: red coming into it slowly while it rains (never paling again until it stops)
		if (_terrain != null && IsInstanceValid(_terrain))
		{
			float red = Mathf.Max(_creekRed, Mathf.MoveToward(_creekRed, _applied * pr * 0.9f, (float)delta * 0.06f));
			if (Mathf.Abs(red - _creekRed) > 0.002f) { _creekRed = red; _terrain.SetStreamBlood(red); }
		}
		if (_audio != null) _audio.VolumeDb = Mathf.Lerp(-34f, LevelDb, _applied) + (pr > 0.001f ? Mathf.LinearToDb(pr) : -60f);
		if (StoryBeat.Atmosphere(this) is { } atmo) atmo.BloodTint = Mathf.Clamp(TintMax * _applied * TintPulse * pr, 0f, 1f);
		Place((float)delta);
	}

	private void Place(float dt)
	{
		if (_blood == null) return;
		var anchor = Follow != null && IsInstanceValid(Follow) ? Follow.GlobalPosition : GlobalPosition;
		// Lead a moving player so the rain is already falling where they are running to.
		Vector3 want = Vector3.Zero;
		if (Follow is CharacterBody3D body)
		{
			Vector3 v = body.Velocity; v.Y = 0;
			want = v * LeadSeconds;
			if (want.Length() > LeadMax) want = want.Normalized() * LeadMax;
		}
		_lead = dt > 0f ? _lead.Lerp(want, 1f - Mathf.Exp(-3f * dt)) : want;
		_blood.GlobalPosition = anchor + _lead + Vector3.Up * Height;
		if (_near != null) _near.GlobalPosition = anchor + _lead * 0.35f + Vector3.Up * 2.6f;
		if (_splash != null) _splash.GlobalPosition = anchor + _lead * 0.3f + Vector3.Up * 0.03f;
	}
}
