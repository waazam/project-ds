using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The storm's visuals, driven by StormController:
/// - <see cref="Intensity"/> 0..1: rain streaks falling around the camera (Weather's, since the weather pass), small
///   splashes on the ground near it, the ground and buildings darkening as they get
///   wet (a camera-following decal), and ForestAtmosphere's overcast/storm layer.
///   Wetness lags behind: it soaks in over ~20 s and dries over ~90 s after the rain.
/// - <see cref="Flash"/>: a lightning strike that actually lights the scene: a brief
///   cold directional light from a random high angle (with shadows), plus a sky, fog
///   and ambient flash through ForestAtmosphere. Honours GameSettings.ReduceFlashing
///   (a dimmer, single, slower pulse).
/// Rain is hidden while the camera is inside a registered shelter (the cabin), so it
/// never falls through a roof.
/// One node lives in forest_world.tscn ("RainVfx", group "rain_vfx"); <see cref="Instance"/>.
/// </summary>
[GlobalClass]
public partial class RainVfx : Node3D
{
	public static RainVfx Instance { get; private set; }

	[Export] public NodePath AtmospherePath = "../Atmosphere";
	[Export] public float WetInSeconds = 20f;
	[Export] public float DryOutSeconds = 90f;
	/// <summary>Peak energy of the lightning directional light at strength 1.</summary>
	[Export] public float LightningEnergy = 4.5f;

	private float _intensity;
	/// <summary>0..1 rain amount (tween it: "Intensity").</summary>
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float Intensity { get => _intensity; set => _intensity = Mathf.Clamp(value, 0f, 1f); }
	/// <summary>0..1, lags Intensity (read-only for others).</summary>
	public float Wetness { get; private set; }

	private float _dripT = 2f, _sinceRain = 999f;
	/// <summary>How long the boughs and the eaves go on dripping once the rain has stopped (seconds).</summary>
	[Export] public float AfterDripSeconds = 12f;
	private GpuParticles3D _splash;
	private ParticleProcessMaterial _splashPm;
	private Image _splashPoints;
	private ImageTexture _splashPointsTex;
	private Decal _wet;
	private DirectionalLight3D _bolt;
	private ForestAtmosphere _atmo;
	private ForestTerrain _terrain;
	private double _splashRefresh;
	private readonly RandomNumberGenerator _rng = new();

	// lightning envelope
	private readonly List<(float t, float e)> _pulse = new();
	private float _pulseClock = -1f, _pulseStrength;

	private static readonly List<(Node3D owner, Aabb box)> _shelters = new();

	/// <summary>Registers a local-space box of <paramref name="owner"/> as a roofed interior: no rain drawn while the camera is in it.</summary>
	public static void RegisterShelter(Node3D owner, Aabb localBox)
	{
		_shelters.RemoveAll(s => s.owner == owner);   // (the streaks' own shelter is Weather's roof collider; this keeps them off indoors at once)
		_shelters.Add((owner, localBox));
		owner.TreeExiting += () => _shelters.RemoveAll(s => s.owner == owner);
		Weather.RegisterShelter(owner, localBox);   // (and no streak shows inside it, wherever the camera is)
	}

	public override void _EnterTree()
	{
		Instance = this;
		AddToGroup("rain_vfx");
	}

	public override void _ExitTree() { if (Instance == this) { Instance = null; RenderingServer.GlobalShaderParameterSet("wet", 0f); } }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_atmo = GetNodeOrNull<ForestAtmosphere>(AtmospherePath) ?? GetTree().GetFirstNodeInGroup("atmosphere") as ForestAtmosphere;
		_terrain = GroundSnap.FindTerrain(this);
		TopLevel = true;
		BuildSplashes();
		BuildWetDecal();
		_bolt = new DirectionalLight3D
		{
			Name = "Lightning",
			LightColor = new Color(0.78f, 0.84f, 1f),
			LightEnergy = 0f,
			ShadowEnabled = true,
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
			DirectionalShadowMaxDistance = 70f,
			Visible = false,
		};
		AddChild(_bolt);
		BuildHorizon();
	}

	// ------------------------------------------------------------------ (2026-10-14) the storm's far light

	/// <summary>Where the storm's far light lies (world xz, from the eye): down the trail, ahead.</summary>
	[Export] public Vector2 HorizonToward = new(0.25f, -1f);
	private MeshInstance3D _horizon;
	private ShaderMaterial _horizonMat;
	private float _farT = 4f, _farClock = -1f, _farStrength, _horizonShown;
	private readonly List<(float t, float e)> _farPulse = new();

	/// <summary>The forest pass (the owner's survey: the storm frames were all but black): a dim cold light low over the
	/// woods one way, the cloud thinner there, and now and then far lightning flaring in it (storm_horizon.gdshader), on
	/// a ring inside the fog's reach so the near trunks stand black against it.</summary>
	private void BuildHorizon()
	{
		_horizonMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/storm_horizon.gdshader") };
		_horizonMat.SetShaderParameter("noise_tex", new NoiseTexture2D
		{
			Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true,
			Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.02f, FractalOctaves = 4, Seed = 3303 },
		});
		_horizonMat.SetShaderParameter("toward", HorizonToward);
		_horizon = new MeshInstance3D
		{
			Name = "StormHorizon",
			Mesh = new CylinderMesh { TopRadius = 66f, BottomRadius = 66f, Height = 70f, RadialSegments = 48, Rings = 4, CapTop = false, CapBottom = false },
			MaterialOverride = _horizonMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
			ExtraCullMargin = 80f, Layers = Weather.CurtainLayer,   // (the weather's own layer: the snow's roof collider never sees it)
		};
		AddChild(_horizon);
	}

	private void UpdateHorizon(Vector3 cp, bool sheltered, float flash, float dt)
	{
		if (_horizon == null) return;
		bool inside = _atmo != null && (_atmo.Interior > 0.3f || _atmo.Underground > 0.3f || _atmo.Enclosed > 0.5f);
		float want = sheltered || inside ? 0f : Mathf.Clamp(_intensity * 1.4f, 0f, 1f);
		_horizonShown = Mathf.MoveToward(_horizonShown, want, dt * 0.5f);
		_horizon.Visible = _horizonShown > 0.005f;
		if (!_horizon.Visible) return;
		_horizon.GlobalPosition = cp + Vector3.Up * 22f;   // (the band's middle well above the eye: the ring reaches from under the horizon to ~40 degrees)
		// far lightning now and then: a flare in the cloud, no light on the scene, no thunder (the near strikes are Flash's)
		_farT -= dt;
		if (_farT <= 0f && _farClock < 0f)
		{
			_farT = _rng.RandfRange(6f, 16f);
			bool gentle = Settings != null && Settings.ReduceFlashing;
			_farStrength = _rng.RandfRange(0.5f, 1f) * (gentle ? 0.35f : 1f);
			_farPulse.Clear();
			if (gentle) { _farPulse.Add((0f, 0f)); _farPulse.Add((0.5f, 1f)); _farPulse.Add((1.6f, 0f)); }
			else
			{
				_farPulse.Add((0f, 0f)); _farPulse.Add((0.06f, 0.6f)); _farPulse.Add((0.14f, 0.15f)); _farPulse.Add((0.22f, 1f));
				if (_rng.Randf() < 0.5f) { _farPulse.Add((0.3f, 0.3f)); _farPulse.Add((0.38f, 0.7f)); }
				_farPulse.Add((0.9f, 0f));
			}
			_farClock = 0f;
		}
		float far = 0f;
		if (_farClock >= 0f)
		{
			_farClock += dt;
			far = -1f;
			for (int i = 1; i < _farPulse.Count; i++)
				if (_farClock <= _farPulse[i].t)
				{
					var (t0, e0) = _farPulse[i - 1]; var (t1, e1) = _farPulse[i];
					far = Mathf.Lerp(e0, e1, (_farClock - t0) / Mathf.Max(0.0001f, t1 - t0)) * _farStrength;
					break;
				}
			if (far < 0f) { far = 0f; _farClock = -1f; }
		}
		_horizonMat.SetShaderParameter("strength", _horizonShown);
		_horizonMat.SetShaderParameter("flicker", far + flash * 0.6f);
	}

	/// <summary>Vertical streak, soft at both ends.</summary>
	/// <summary>A soft vertical streak (shared with Act 11's blood rain).</summary>
	internal static Texture2D StreakTexture()
	{
		var img = Image.CreateEmpty(4, 32, false, Image.Format.Rgba8);
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 4; x++)
			{
				float v = y / 31f;
				float a = Mathf.Min(1f, Mathf.Sin(Mathf.Pi * v) * 1.6f);
				img.SetPixel(x, y, new Color(1, 1, 1, a * (0.55f + 0.45f * v)));
			}
		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	private void BuildSplashes()
	{
		const int points = 96;
		_splashPoints = Image.CreateEmpty(points, 1, false, Image.Format.Rgbf);
		_splashPointsTex = ImageTexture.CreateFromImage(_splashPoints);
		_splashPm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Points,
			EmissionPointTexture = _splashPointsTex,
			EmissionPointCount = points,
			Direction = Vector3.Up,
			Spread = 55f,
			InitialVelocityMin = 0.6f,
			InitialVelocityMax = 1.4f,
			Gravity = new Vector3(0, -9f, 0),
			ScaleMin = 0.6f,
			ScaleMax = 1.3f,
			ColorRamp = SplashRamp(),
		};
		var mat = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			AlbedoColor = new Color(0.7f, 0.74f, 0.8f, 0.3f),
		};
		_splash = new GpuParticles3D
		{
			Name = "Splashes",
			Amount = 700,
			Lifetime = 0.32f,
			Randomness = 0.5f,
			ProcessMaterial = _splashPm,
			DrawPass1 = new QuadMesh { Size = new Vector2(0.03f, 0.03f), Material = mat },
			LocalCoords = false,
			TopLevel = true,
			VisibilityAabb = new Aabb(new Vector3(-2000, -500, -2000), new Vector3(4000, 1000, 4000)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Emitting = false,
		};
		AddChild(_splash);
	}

	private static GradientTexture1D SplashRamp()
	{
		var g = new Gradient();
		g.SetColor(0, new Color(1, 1, 1, 0.9f));
		g.SetColor(1, new Color(1, 1, 1, 0f));
		return new GradientTexture1D { Gradient = g, Width = 16 };
	}

	private void BuildWetDecal()
	{
		// Dark, uneven film (puddly patches a little darker): blends over everything it projects on.
		var __tg = Systems.TexGen.Start();
		var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
		var noise = new FastNoiseLite { Seed = 77, Frequency = 0.08f, FractalOctaves = 3 };
		for (int y = 0; y < 64; y++)
			for (int x = 0; x < 64; x++)
			{
				float n = noise.GetNoise2D(x, y) * 0.5f + 0.5f;
				img.SetPixel(x, y, new Color(0.035f, 0.035f, 0.04f, Mathf.Clamp(0.55f + (n - 0.5f) * 0.9f, 0.2f, 1f)));
			}
		img.GenerateMipmaps();
		_wet = new Decal
		{
			Name = "WetGround",
			Size = new Vector3(56f, 16f, 56f),
			TextureAlbedo = ImageTexture.CreateFromImage(img),
			AlbedoMix = 0f,
			NormalFade = 0.45f,
			UpperFade = 0.2f,
			LowerFade = 0.2f,
			DistanceFadeEnabled = false,
			Visible = false,
		};
		AddChild(_wet);
	}

	private static GameSettings Settings => GameSettings.Instance;

	/// <summary>A lightning strike: lights the whole scene for a fraction of a second. strength 0..1.5.</summary>
	public void Flash(float strength = 1f)
	{
		if (_bolt == null) return;
		bool gentle = Settings != null && Settings.ReduceFlashing;
		_pulseStrength = Mathf.Clamp(strength, 0f, 1.5f) * (gentle ? 0.3f : 1f);
		_pulse.Clear();
		if (gentle)
		{
			// one soft swell and fade, no flicker
			_pulse.Add((0f, 0f)); _pulse.Add((0.25f, 1f)); _pulse.Add((1.1f, 0f));
		}
		else
		{
			// the classic double/triple stutter, then a fading afterglow
			float a = _rng.RandfRange(0.7f, 1f);
			_pulse.Add((0f, 0f)); _pulse.Add((0.03f, a)); _pulse.Add((0.09f, 0.12f));
			_pulse.Add((0.14f, 1f)); _pulse.Add((0.2f, 0.35f));
			if (_rng.Randf() < 0.6f) { _pulse.Add((0.27f, 0.8f)); _pulse.Add((0.33f, 0.3f)); }
			_pulse.Add((0.75f, 0f));
		}
		_pulseClock = 0f;
		// from a random direction, high in the sky
		float yaw = _rng.RandfRange(0f, Mathf.Tau), pitch = Mathf.DegToRad(_rng.RandfRange(-65f, -40f));
		_bolt.GlobalBasis = Basis.FromEuler(new Vector3(pitch, yaw, 0));
		_bolt.Visible = true;
	}

	private float Envelope(float t)
	{
		if (_pulse.Count == 0) return 0f;
		for (int i = 1; i < _pulse.Count; i++)
			if (t <= _pulse[i].t)
			{
				var (t0, e0) = _pulse[i - 1];
				var (t1, e1) = _pulse[i];
				return Mathf.Lerp(e0, e1, (t - t0) / Mathf.Max(0.0001f, t1 - t0));
			}
		return -1f; // finished
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;
		float dt = (float)delta;
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		Vector3 cp = cam.GlobalPosition;

		// wetness lags the rain
		float rate = _intensity > Wetness ? 1f / WetInSeconds : 1f / DryOutSeconds;
		Wetness = Mathf.MoveToward(Wetness, _intensity, rate * dt);

		bool sheltered = false;
		foreach (var (owner, box) in _shelters)
			if (IsInstanceValid(owner) && box.HasPoint(owner.GlobalTransform.AffineInverse() * cp)) { sheltered = true; break; }

		float shown = sheltered ? 0f : _intensity;
		// how long since the rain stopped (the drips after it)
		_sinceRain = _intensity > 0.02f ? 0f : _sinceRain + dt;
		// the boughs dripping: in the rain, out in the woods, fat drops off the branches onto the leaves round them, now
		// near, now a few metres off (2026-10-07: the storm walk's sound, less the same all the way)
		_dripT -= dt;
		// (2026-10-10: and after it, slower, while the woods are still wet)
		// (2026-10-13, Dan: "the rain continues over the cabin ... even when it's supposed to be fully gone": the drips ran on
		// as long as the ground was wet, a minute and more after the storm broke, and read as the rain still falling. Now a
		// short spell of them, thinning out over AfterDripSeconds.)
		float after = Mathf.Clamp(1f - _sinceRain / AfterDripSeconds, 0f, 1f);
		float dripping = Mathf.Max(_intensity, Mathf.Clamp((Wetness - 0.2f) * 1.2f, 0f, 0.6f) * after);
		if (!sheltered && dripping > 0.3f && _dripT <= 0f)
		{
			_dripT = (float)GD.RandRange(1.2, 3.6) / Mathf.Max(dripping, 0.3f) * (_intensity > 0.3f ? 1f : 1.8f);
			float a = (float)GD.RandRange(0.0, Mathf.Tau), r = (float)GD.RandRange(1.5, 7.0);
			var at = cp + new Vector3(Mathf.Cos(a) * r, (float)GD.RandRange(-1.0, 1.5), Mathf.Sin(a) * r);
			Audio.AudioDirector.OneShot(this, "canopy_drip", 6, at, -14f + 4f * _intensity, "Weather", 3f, 0.04f);
		}
		// the streaks are Weather's now (the weather pass): wrapping round the camera, slanting with the shared wind,
		// never under a roof or the trees; a touch fewer than full (Dan, 2026-09-22)
		Weather.Get(this).Rain = shown * 0.85f;

		_splash.Emitting = shown > 0.05f;
		_splash.AmountRatio = Mathf.Clamp(shown, 0f, 1f);
		_splashRefresh -= delta;
		if (_splash.Emitting && _splashRefresh <= 0) { _splashRefresh = 0.2; RefreshSplashPoints(cp); }

		RenderingServer.GlobalShaderParameterSet("wet", Wetness);
		Puddles(cp, dt);
		Eaves(dt);
		_wet.Visible = Wetness > 0.01f;
		_wet.AlbedoMix = Wetness * 0.42f;
		_wet.GlobalPosition = new Vector3(Mathf.Snapped(cp.X, 2f), cp.Y, Mathf.Snapped(cp.Z, 2f));

		// lightning
		float flash = 0f;
		if (_pulseClock >= 0f)
		{
			_pulseClock += dt;
			float e = Envelope(_pulseClock);
			if (e < 0f) { _pulseClock = -1f; _bolt.Visible = false; _bolt.LightEnergy = 0f; }
			else { flash = e * _pulseStrength; _bolt.LightEnergy = flash * LightningEnergy; }
		}

		UpdateHorizon(cp, sheltered, flash, dt);

		if (_atmo != null)
		{
			_atmo.Storm = _intensity;
			_atmo.Wetness = Wetness;
			_atmo.Flash = flash * 0.8f;
		}
	}

	private void RefreshSplashPoints(Vector3 cp)
	{
		int n = _splashPoints.GetWidth();
		for (int i = 0; i < n; i++)
		{
			float a = _rng.Randf() * Mathf.Tau, r = Mathf.Sqrt(_rng.Randf()) * 9f;
			float x = cp.X + Mathf.Cos(a) * r, z = cp.Z + Mathf.Sin(a) * r;
			float y = _terrain?.HeightAt(x, z) ?? cp.Y - 1.6f;
			// skip points under a shelter so it doesn't "rain" on the cabin floor
			var p = new Vector3(x, y + 0.02f, z);
			foreach (var (owner, box) in _shelters)
				if (IsInstanceValid(owner) && box.Grow(0.5f).HasPoint(owner.GlobalTransform.AffineInverse() * (p + Vector3.Up))) { p.Y -= 50f; break; }
			_splashPoints.SetPixel(i, 0, new Color(p.X, p.Y, p.Z));
		}
		_splashPointsTex.Update(_splashPoints);
		_splash.GlobalPosition = Vector3.Zero;
	}
}
