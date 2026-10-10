using Godot;

namespace ProjectDS.World;

/// <summary>
/// A reusable patch of fire: billboarded flame tongues (noise-scrolled shader
/// cards), rising embers, a column of dark smoke and a flickering warm light.
/// Everything scales with <see cref="Intensity"/> (0 = out, 1 = raging), live.
///
/// Local frame: the flames stand on y = 0 and fill the box <see cref="Extent"/>
/// (x/z centred on the origin, y upward). Place it with AddChild + Position in
/// whatever it is burning (see Cabin.FireSpots / Cabin.SetBurning).
/// <see cref="FlameScale"/> 0 gives a smoke-only source (smoke pouring out of a window);
/// <see cref="SmokeDirection"/> lets smoke leave sideways before it rises.
/// </summary>
[GlobalClass]
public partial class FireVfx : Node3D
{
	private float _intensity = 1f;
	/// <summary>0..1: flame height and opacity, ember rate, smoke rate, light energy.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float Intensity { get => _intensity; set { _intensity = Mathf.Clamp(value, 0f, 1f); Apply(); } }
	/// <summary>Box the flames fill: x/z footprint centred on the origin, y = flame height.</summary>
	[Export] public Vector3 Extent = new(1f, 1.2f, 1f);
	/// <summary>Multiplies the number and size of the flame cards (0 = no flames: smoke-only source).</summary>
	[Export] public float FlameScale = 1f;
	[Export] public bool Smoke = true;
	/// <summary>How thick the smoke is (particle count/opacity multiplier).</summary>
	[Export] public float SmokeAmount = 1f;
	/// <summary>Initial smoke direction (local); it always ends up rising.</summary>
	[Export] public Vector3 SmokeDirection = Vector3.Up;
	[Export] public bool Embers = true;
	[Export] public bool EmitLight = true;
	[Export] public float LightRange = 12f;
	[Export] public float LightEnergy = 3.2f;
	[Export] public bool LightShadows = true;
	[Export] public int Seed = 1;
	/// <summary>The air shimmering over it (heat_haze.gdshader; 2026-10-04).</summary>
	[Export] public bool Haze = true;
	private MeshInstance3D _haze;
	private ShaderMaterial _hazeMat;
	private static NoiseTexture2D _hazeNoise;

	private MultiMeshInstance3D _cards;
	private ShaderMaterial _flameMat;
	private GpuParticles3D _embers, _smoke;
	private OmniLight3D _light;
	private double _clock;
	private bool _built;

	public override void _Ready() => Build();

	public void Build()
	{
		foreach (var c in GetChildren()) if (c.HasMeta("firevfx")) { RemoveChild(c); c.QueueFree(); }
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 7919 + 13) };
		_clock = rng.Randf() * 10f;

		// --- flame cards
		int count = FlameScale <= 0f ? 0 : Mathf.Clamp(Mathf.RoundToInt(Extent.X * Extent.Z * 9f * FlameScale + 4f), 4, 32);
		if (count > 0)
		{
			_flameMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/flame_card.gdshader") };
			var quad = new QuadMesh { Size = new Vector2(1f, 1f), CenterOffset = new Vector3(0, 0.5f, 0), Material = _flameMat };
			var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = quad, InstanceCount = count };
			for (int i = 0; i < count; i++)
			{
				float fx = rng.RandfRange(-0.5f, 0.5f), fz = rng.RandfRange(-0.5f, 0.5f);
				// bigger, taller tongues toward the middle of the patch
				float centre = 1f - Mathf.Clamp(new Vector2(fx, fz).Length() * 1.6f, 0f, 1f);
				float h = Extent.Y * rng.RandfRange(0.75f, 1.25f) * (0.7f + 0.6f * centre) * Mathf.Max(0.5f, FlameScale);
				float w = Mathf.Clamp(Mathf.Min(Extent.X, Extent.Z) * rng.RandfRange(0.5f, 0.85f), 0.35f, 1.6f) * (0.75f + 0.4f * centre);
				var basis = Basis.Identity.Scaled(new Vector3(w, h, 1f));
				var pos = new Vector3(fx * Extent.X, rng.RandfRange(-0.05f, 0.1f) * Extent.Y, fz * Extent.Z);
				mm.SetInstanceTransform(i, new Transform3D(basis, pos));
				mm.SetInstanceCustomData(i, new Color(rng.Randf(), rng.RandfRange(0f, 0.6f), rng.RandfRange(0.02f, 0.08f), 0));
			}
			_cards = new MultiMeshInstance3D { Name = "Flames", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_cards.SetMeta("firevfx", true);
			// generous AABB: the vertex shader billboards and rescales the cards
			_cards.CustomAabb = new Aabb(new Vector3(-Extent.X, -0.5f, -Extent.Z), new Vector3(Extent.X * 2f, Extent.Y * 2f + 1f, Extent.Z * 2f));
			AddChild(_cards);
		}

		// --- embers
		if (Embers && FlameScale > 0f)
		{
			var pm = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
				EmissionBoxExtents = new Vector3(Extent.X * 0.45f, Extent.Y * 0.3f, Extent.Z * 0.45f),
				Direction = Vector3.Up,
				Spread = 25f,
				InitialVelocityMin = 1.2f,
				InitialVelocityMax = 3.2f,
				Gravity = new Vector3(0.3f, 1.2f, 0f),
				DampingMin = 0.4f, DampingMax = 1.2f,
				TurbulenceEnabled = true,
				TurbulenceNoiseStrength = 1.6f,
				TurbulenceNoiseScale = 2.2f,
				TurbulenceInfluenceMin = 0.05f, TurbulenceInfluenceMax = 0.15f,
				ScaleMin = 0.02f, ScaleMax = 0.045f,
				ColorRamp = Ramp(new Color(1f, 0.85f, 0.5f, 1f), new Color(1f, 0.4f, 0.08f, 0.9f), new Color(0.6f, 0.1f, 0.02f, 0f)),
			};
			// (the weather pass: a spark's short streak along its flight, not a square: weather_spark.gdshader)
			pm.ParticleFlagAlignY = true;
			var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/weather_spark.gdshader") };
			mat.SetShaderParameter("atlas", Weather.Atlas);
			mat.SetShaderParameter("box_half", new Vector3(1000f, 1000f, 1000f));
			mat.SetShaderParameter("near_fade", 0.05f);
			mat.SetShaderParameter("stretch", 0.025f);
			_embers = new GpuParticles3D
			{
				Name = "Embers",
				Amount = Mathf.Clamp(Mathf.RoundToInt(18 * Extent.X * Extent.Z + 10), 10, 70),
				Lifetime = 2.4f,
				Randomness = 0.6f,
				ProcessMaterial = pm,
				DrawPass1 = new QuadMesh { Size = new Vector2(1f, 1f), Material = mat },
				Position = new Vector3(0, Extent.Y * 0.35f, 0),
				VisibilityAabb = new Aabb(new Vector3(-4, -1, -4), new Vector3(8, 12, 8)),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				LocalCoords = false,
			};
			_embers.SetMeta("firevfx", true);
			AddChild(_embers);
		}

		// --- smoke
		if (Smoke)
		{
			Vector3 dir = SmokeDirection.LengthSquared() > 0.0001f ? SmokeDirection.Normalized() : Vector3.Up;
			var pm = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
				EmissionBoxExtents = new Vector3(Extent.X * 0.4f, 0.1f, Extent.Z * 0.4f),
				Direction = dir,
				Spread = 25f,
				InitialVelocityMin = 0.5f,
				InitialVelocityMax = 1.0f,
				Gravity = new Vector3(0.2f, 0.55f, 0.08f),
				DampingMin = 0.05f, DampingMax = 0.15f,
				AngleMin = -180f, AngleMax = 180f,
				AngularVelocityMin = -20f, AngularVelocityMax = 20f,
				ScaleMin = 0.8f, ScaleMax = 1.4f,
				ScaleCurve = Curve01(0.4f, 1.8f),
				ColorRamp = Ramp(new Color(0.36f, 0.33f, 0.3f, 0f), new Color(0.27f, 0.26f, 0.25f, 0.85f), new Color(0.33f, 0.33f, 0.33f, 0f), 0.1f),
				TurbulenceEnabled = true,
				TurbulenceNoiseStrength = 0.8f,
				TurbulenceNoiseScale = 4f,
				TurbulenceInfluenceMin = 0.02f, TurbulenceInfluenceMax = 0.06f,
				// (the weather pass: a puff's whole life from the flipbook, billowing from a dense knot to a thin wisp)
				AnimSpeedMin = 1f, AnimSpeedMax = 1f, AnimOffsetMin = 0f, AnimOffsetMax = 0.12f,
			};
			_smokePm = pm;
			_smokeGravity = pm.Gravity;
			var mat = new StandardMaterial3D
			{
				BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				VertexColorUseAsAlbedo = true,
				AlbedoTexture = SmokeSheet,
				ParticlesAnimHFrames = 4, ParticlesAnimVFrames = 4, ParticlesAnimLoop = false,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
				Roughness = 1f,
				MetallicSpecular = 0f,
				ProximityFadeEnabled = true,
				ProximityFadeDistance = 0.6f,
			};
			float size = Mathf.Clamp(Mathf.Max(Extent.X, Extent.Z) * 1.4f, 1.1f, 3.2f);
			_smoke = new GpuParticles3D
			{
				Name = "Smoke",
				Amount = Mathf.Clamp(Mathf.RoundToInt((22 + 8 * Extent.X * Extent.Z) * SmokeAmount), 10, 64),
				Lifetime = 6.5f,
				Randomness = 0.4f,
				ProcessMaterial = pm,
				DrawPass1 = new QuadMesh { Size = new Vector2(size, size), Material = mat },
				Position = new Vector3(0, Mathf.Max(0.2f, Extent.Y * (FlameScale > 0f ? 0.8f : 0.3f)), 0),
				VisibilityAabb = new Aabb(new Vector3(-8, -2, -8), new Vector3(16, 24, 16)),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				LocalCoords = false,
				DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth,
			};
			_smoke.SetMeta("firevfx", true);
			AddChild(_smoke);
		}

		// --- light
		if (EmitLight)
		{
			_light = new OmniLight3D
			{
				Name = "FireLight",
				LightColor = new Color(1f, 0.52f, 0.18f),
				OmniRange = LightRange,
				OmniAttenuation = 1.3f,
				ShadowEnabled = LightShadows,
				Position = new Vector3(0, Mathf.Max(0.4f, Extent.Y * 0.55f), 0),
			};
			_light.SetMeta("firevfx", true);
			AddChild(_light);
		}
		BuildHaze();
		_built = true;
		Apply();
	}

	private ParticleProcessMaterial _smokePm;
	private Vector3 _smokeGravity;
	private static Texture2D _smokeSheet;
	private static Texture2D SmokeSheet => _smokeSheet ??= GD.Load<Texture2D>("res://assets/textures/weather/smoke_sheet.png");

	private static Texture2D _puffAlpha;
	private static Texture2D PuffAlpha()
	{
		if (_puffAlpha != null) return _puffAlpha;
		var src = BuildingTextures.Puff().GetImage();
		var __tg = Systems.TexGen.Start();
		var img = Image.CreateEmpty(src.GetWidth(), src.GetHeight(), false, Image.Format.Rgba8);
		for (int y = 0; y < src.GetHeight(); y++)
			for (int x = 0; x < src.GetWidth(); x++)
				img.SetPixel(x, y, new Color(1, 1, 1, src.GetPixel(x, y).R));
		img.GenerateMipmaps();
		_puffAlpha = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
		return _puffAlpha;
	}

	private static GradientTexture1D Ramp(Color a, Color b, Color c, float mid = 0.25f)
	{
		var g = new Gradient();
		g.SetColor(0, a);
		g.SetColor(1, c);
		g.AddPoint(mid, b);
		return new GradientTexture1D { Gradient = g, Width = 64 };
	}

	private static CurveTexture Curve01(float start, float end)
	{
		var c = new Curve { MaxValue = 2f };
		c.AddPoint(new Vector2(0, start));
		c.AddPoint(new Vector2(1, end));
		return new CurveTexture { Curve = c, Width = 32 };
	}

	private void Apply()
	{
		if (!_built) return;
		float k = _intensity;
		_flameMat?.SetShaderParameter("intensity", k);
		if (_cards != null) _cards.Visible = k > 0.01f;
		if (_embers != null) { _embers.Emitting = k > 0.05f; _embers.AmountRatio = Mathf.Clamp(k, 0.05f, 1f); }
		if (_smoke != null) { _smoke.Emitting = k > 0.02f; _smoke.AmountRatio = Mathf.Clamp(0.3f + k * 0.7f, 0.05f, 1f); }
		if (_light != null) _light.Visible = k > 0.01f;
		if (_haze != null) { _haze.Visible = k > 0.05f; _hazeMat.SetShaderParameter("intensity", k); }
	}

	/// <summary>The shimmer card: from the flames' tips up (the screen it bends is from before the flames are drawn: over
	/// them, it would have rubbed them out), as wide as them and twice their height.</summary>
	private void BuildHaze()
	{
		if (!Haze || FlameScale <= 0f) return;
		_hazeNoise ??= new NoiseTexture2D { Width = 64, Height = 64, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.08f, Seed = 911 } };
		_hazeMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/heat_haze.gdshader"), RenderPriority = -1 };   // (drawn before the smoke and the flames, so it never rubs them out)
		_hazeMat.SetShaderParameter("noise_tex", _hazeNoise);
		float w = Mathf.Max(Extent.X, Extent.Z) * 1.4f, h = Extent.Y * 2.2f;
		_haze = new MeshInstance3D
		{
			Name = "Haze", Mesh = new QuadMesh { Size = new Vector2(w, h), CenterOffset = new Vector3(0, h * 0.5f + Extent.Y * 0.95f, 0) },
			MaterialOverride = _hazeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_haze.SetMeta("firevfx", true);
		AddChild(_haze);
	}

	public override void _Process(double delta)
	{
		// the smoke drifts off with the wind (Weather's: the same air the snow and the trees are in)
		if (_smokePm != null && _smoke != null && _smoke.Emitting)
		{
			Vector3 w = Weather.Wind;   // (world space: the smoke is LocalCoords = false)
			_smokePm.Gravity = _smokeGravity + new Vector3(w.X, 0f, w.Z) * 0.45f;
		}
		if (_light == null || !_light.Visible) return;
		_clock += delta;
		float t = (float)_clock;
		// layered sines + an occasional gust: unstable, never strobing
		float flick = 0.78f + 0.12f * Mathf.Sin(t * 7.3f) + 0.08f * Mathf.Sin(t * 13.1f + 1.3f) + 0.06f * Mathf.Sin(t * 23.7f + 0.4f);
		flick += 0.1f * Mathf.Max(0f, Mathf.Sin(t * 0.9f + Seed));
		_light.LightEnergy = LightEnergy * _intensity * flick;
		_light.Position = new Vector3(Mathf.Sin(t * 3.1f) * 0.08f * Extent.X, Mathf.Max(0.4f, Extent.Y * 0.55f) + Mathf.Sin(t * 5.3f) * 0.06f, Mathf.Cos(t * 2.7f) * 0.08f * Extent.Z);
	}
}
