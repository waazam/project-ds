using Godot;

namespace ProjectDS.World.HallwayParts;

/// <summary>
/// Act 15's shadow man, after the owner's references (ink-splatter figures, a shape seen through
/// frosted glass, a dripping black silhouette on a wall) and their note that he must be frightening,
/// not a cartoon: a man-shaped absence that is wrong in every proportion. Too tall and starved thin,
/// hunched, shoulders sloping away, a small head hanging forward and to one side off a long neck.
/// His arms bend twice and hang past his knees, ending in splayed fingers as long as forearms. Ink
/// runs off him in strings and spatters round his feet. All of him is the same light-swallowing black
/// (<c>ink_shadow.gdshader</c>): his outline ragged and never quite still, and a dark blur hangs round
/// him as if he were seen through glass. Now and then his head snaps to a new angle. Two pinpricks
/// of light where eyes would be, barely there until he takes someone.
///
/// He never walks: while the lights are green he is frozen where he stands; when they go red he is
/// simply somewhere else (see <see cref="Act15Hallway"/>). He faces -Z in his own space.
/// </summary>
public partial class ShadowMan : Node3D
{
	public const float Height = 2.55f;
	private StandardMaterial3D _eyeMat, _hazeMat;
	private MeshInstance3D _haze, _veil;
	private StandardMaterial3D _veilMat;
	private Node3D _aura;
	private ShaderMaterial _ink;
	private Node3D _head;
	private float _t, _nextTwitch = 2.5f;
	private Vector3 _headTilt, _headTarget;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1515 };
	private static readonly Vector3 Neck = new(0.03f, 2.06f, -0.16f);

	/// <summary>0..1: how hard the eyes burn (they flare when he takes someone).</summary>
	public float Glare { get; set; } = 0.3f;

	public override void _Ready()
	{
		_ink = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/ink_shadow.gdshader") };
		_ink.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		var k = new MeshKit();
		k.Mat(_ink);
		k.Color = Colors.White;

		// legs: long and starved, the knees locked a little too far back, feet smeared into the floor
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 hip = new(s * 0.1f, 1.22f, 0.02f), knee = new(s * 0.11f, 0.64f, 0.07f), ankle = new(s * 0.12f, 0.06f, -0.02f);
			k.Cylinder(hip, knee, 0.075f, 0.05f, 6, true);
			k.Cylinder(knee, ankle, 0.05f, 0.03f, 6, true);
			k.Blob(ankle + new Vector3(0, -0.03f, -0.07f), new Vector3(0.05f, 0.03f, 0.13f), 11 + (int)s, 0.25f, false);
		}
		// the body: a narrow pelvis, a waist you could close a hand round, ribs, and a hunched back
		k.Blob(new Vector3(0, 1.24f, 0.02f), new Vector3(0.15f, 0.1f, 0.1f), 21, 0.2f, false);
		k.Cylinder(new Vector3(0, 1.26f, 0.02f), new Vector3(0, 1.52f, -0.01f), 0.08f, 0.12f, 7, true);
		k.Cylinder(new Vector3(0, 1.5f, -0.01f), new Vector3(0, 1.84f, -0.08f), 0.13f, 0.18f, 7, true);
		k.Cylinder(new Vector3(0, 1.8f, -0.08f), new Vector3(0, 1.99f, -0.1f), 0.18f, 0.1f, 7, true);
		k.Blob(new Vector3(0, 1.9f, 0.06f), new Vector3(0.12f, 0.12f, 0.08f), 25, 0.15f, false);   // the hump of the back
		// shoulders sloping away, too narrow for the arms that hang from them
		foreach (float s in new[] { -1f, 1f })
		{
			k.Cylinder(new Vector3(s * 0.03f, 2.0f, -0.1f), new Vector3(s * 0.2f, 1.84f, -0.07f), 0.07f, 0.05f, 5, true);
			k.Blob(new Vector3(s * 0.1f, 1.93f, -0.06f), new Vector3(0.09f, 0.06f, 0.08f), 27 + (int)s, 0.25f, false);   // a starved trapezius
		}
		// arms: they bend twice, and the hands hang past the knees, fingers as long as a forearm, splayed
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 sh = new(s * 0.21f, 1.83f, -0.06f);
			Vector3 e1 = new(s * 0.27f, 1.46f, -0.03f), e2 = new(s * 0.26f, 1.02f, -0.09f), wr = new(s * 0.29f, 0.6f, -0.05f);
			k.Cylinder(sh, e1, 0.042f, 0.034f, 5, true);
			k.Cylinder(e1, e2, 0.034f, 0.03f, 5, true);
			k.Cylinder(e2, wr, 0.03f, 0.022f, 5, true);
			for (int f = 0; f < 5; f++)
			{
				float spread = (f - 2f) * 0.07f;
				Vector3 fb = wr + new Vector3(0, -0.02f, spread * 0.3f);
				Vector3 knuckle = fb + new Vector3(s * (0.02f + Mathf.Abs(spread) * 0.2f), -0.16f, spread * 0.8f);
				Vector3 tip = knuckle + new Vector3(s * 0.03f, -0.15f - 0.03f * (f % 2), spread * 0.6f - 0.03f);
				k.Cylinder(fb, knuckle, 0.011f, 0.008f, 4, false);
				k.Cylinder(knuckle, tip, 0.008f, 0.002f, 4, false);
			}
		}
		// ink running off him in strings, and spattered round his feet
		foreach (var (from, len) in new[]
		{
			(new Vector3(-0.3f, 0.3f, -0.07f), 0.3f), (new Vector3(0.31f, 0.28f, -0.1f), 0.24f), (new Vector3(-0.27f, 1.0f, -0.08f), 0.35f),
			(new Vector3(0.27f, 1.44f, -0.03f), 0.28f), (new Vector3(-0.08f, 1.16f, -0.04f), 0.5f), (new Vector3(0.1f, 1.14f, 0.07f), 0.62f),
			(new Vector3(0.02f, 1.2f, 0.1f), 0.4f), (new Vector3(-0.12f, 1.75f, 0.08f), 0.45f), (new Vector3(0.14f, 1.7f, 0.1f), 0.38f),
		})
		{
			k.Cylinder(from, from + Vector3.Down * len, 0.014f, 0.003f, 4, false);
			k.Blob(from + Vector3.Down * len, Vector3.One * 0.01f, (int)(len * 100), 0.1f, false);
		}
		for (int i = 0; i < 18; i++)
		{
			float a = _rng.RandfRange(0, Mathf.Tau), r = _rng.RandfRange(0.12f, 0.8f);
			float sz = _rng.RandfRange(0.02f, 0.08f) * (1.1f - r);
			k.Blob(new Vector3(Mathf.Cos(a) * r, 0.004f, Mathf.Sin(a) * r), new Vector3(sz * 1.7f, 0.004f, sz), 60 + i, 0.35f, false);
		}
		k.CommitTo(this, "Body", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

		// the head: small, long-skulled, hung forward and to one side off a long neck
		var neck = new MeshKit();
		neck.Mat(_ink);
		neck.Color = Colors.White;
		neck.Cylinder(new Vector3(0, 1.96f, -0.1f), Neck, 0.045f, 0.035f, 5, false);
		neck.CommitTo(this, "Neck", false);
		_head = new Node3D { Name = "Head", Position = Neck };
		AddChild(_head);
		var h = new MeshKit();
		h.Mat(_ink);
		h.Color = Colors.White;
		h.Blob(new Vector3(0, 0.1f, -0.04f), new Vector3(0.075f, 0.13f, 0.09f), 41, 0.18f, false);
		h.Blob(new Vector3(0, 0.02f, -0.08f), new Vector3(0.05f, 0.06f, 0.05f), 43, 0.2f, false);   // the jaw, hanging
		h.CommitTo(_head, "Skull", false);
		_eyeMat = new StandardMaterial3D
		{
			AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			EmissionEnabled = true, Emission = new Color(0.95f, 0.95f, 1f), EmissionEnergyMultiplier = 0.8f,
		};
		foreach (float s in new[] { -1f, 1f })
			_head.AddChild(new MeshInstance3D
			{
				Name = "Eye", Mesh = new SphereMesh { Radius = 0.006f, Height = 0.012f }, Position = new Vector3(s * 0.03f, 0.12f, -0.125f),
				MaterialOverride = _eyeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		_headTilt = _headTarget = new Vector3(0.35f, 0.1f, 0.42f);
		_head.Rotation = _headTilt;

		// the blur round him, as if seen through frosted glass
		var blur = new GradientTexture2D
		{
			Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
			Gradient = new Gradient { Colors = new[] { new Color(0, 0, 0, 0.85f), new Color(0, 0, 0, 0.45f), new Color(0, 0, 0, 0f) }, Offsets = new[] { 0f, 0.5f, 1f } },
		};
		_haze = new MeshInstance3D
		{
			Name = "Haze", Mesh = new QuadMesh { Size = new Vector2(2.0f, 3.6f) }, Position = new Vector3(0, 1.35f, 0.12f),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoTexture = blur, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_haze);
		_hazeMat = (StandardMaterial3D)_haze.MaterialOverride;
		// and a thinner veil of it in front of him, so the outline never quite resolves
		_veil = new MeshInstance3D
		{
			Name = "Veil", Mesh = new QuadMesh { Size = new Vector2(1.3f, 3.0f) }, Position = new Vector3(0, 1.3f, -0.35f),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoTexture = blur, AlbedoColor = new Color(1, 1, 1, 0.4f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_veil);
		_veilMat = (StandardMaterial3D)_veil.MaterialOverride;
		BuildAura();

		// he is there: you can't walk through him
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.1f, 0), Shape = new CapsuleShape3D { Radius = 0.3f, Height = 2.2f } });
		AddChild(body);
	}


	/// <summary>
	/// The aura (the owner's references): a shadow that never lets his outline settle. Dark smoke rolls
	/// off him and drifts up and away, black flakes tear loose and float off, and deep in the black a few
	/// faint specks of light swim in and out (slowly: nothing twinkles fast). The whole of it, and the
	/// blur behind him, swells and ebbs like slow breathing.
	/// </summary>
	private void BuildAura()
	{
		_aura = new Node3D { Name = "Aura" };
		AddChild(_aura);
		var dot = LakeParts.LakeFx.SoftDot();
		// tendrils: thin black wisps curling off his edges (shoulders, arms, head), so the outline smears
		_aura.AddChild(Particles("Tendrils", 60, 2.6, new Vector3(0.34f, 0.95f, 0.14f), new Vector3(0, 1.35f, 0),
			new ParticleProcessMaterial
			{
				Direction = new Vector3(0, 1f, 0.3f), Spread = 110f, InitialVelocityMin = 0.15f, InitialVelocityMax = 0.45f, Gravity = new Vector3(0, 0.12f, 0),
				ScaleMin = 0.5f, ScaleMax = 1.1f, AngleMin = 0f, AngleMax = 360f, AngularVelocityMin = -30f, AngularVelocityMax = 30f,
				ColorRamp = Ramp(new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.85f), new Color(0, 0, 0, 0f)),
			}, 0.28f, new Color(0.0f, 0.0f, 0.0f), LakeParts.LakeFx.SoftDot(), false));
		// smoke: big soft black puffs off the whole body, rising and spreading, left hanging where he was
		_aura.AddChild(Particles("Smoke", 90, 4.5, new Vector3(0.42f, 1.15f, 0.32f), new Vector3(0, 1.2f, 0),
			new ParticleProcessMaterial
			{
				Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 0.08f, InitialVelocityMax = 0.3f, Gravity = new Vector3(0, 0.04f, 0),
				ScaleMin = 0.7f, ScaleMax = 1.6f, DampingMin = 0.05f, DampingMax = 0.15f,
				AngleMin = 0f, AngleMax = 360f, AngularVelocityMin = -12f, AngularVelocityMax = 12f,
				ColorRamp = Ramp(new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.6f), new Color(0, 0, 0, 0f)),
			}, 1.15f, new Color(0.01f, 0.01f, 0.012f), dot, false));
		// flakes: small black shreds tearing off and drifting away
		_aura.AddChild(Particles("Flakes", 26, 5.0, new Vector3(0.3f, 1.1f, 0.2f), new Vector3(0, 1.2f, 0),
			new ParticleProcessMaterial
			{
				Direction = new Vector3(0, 0.6f, 1f), Spread = 160f, InitialVelocityMin = 0.1f, InitialVelocityMax = 0.35f, Gravity = new Vector3(0, 0.02f, 0),
				ScaleMin = 0.4f, ScaleMax = 1f, AngleMin = 0f, AngleMax = 360f, AngularVelocityMin = -60f, AngularVelocityMax = 60f,
				ColorRamp = Ramp(new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.95f), new Color(0, 0, 0, 0f)),
			}, 0.035f, new Color(0.005f, 0.005f, 0.006f), null, false));
		// specks of light deep in the black, swimming slowly in and out
		_aura.AddChild(Particles("Specks", 34, 3.2, new Vector3(0.2f, 0.9f, 0.12f), new Vector3(0, 1.3f, 0),
			new ParticleProcessMaterial
			{
				Direction = Vector3.Up, Spread = 180f, InitialVelocityMin = 0.0f, InitialVelocityMax = 0.05f, Gravity = Vector3.Zero,
				ScaleMin = 0.5f, ScaleMax = 1.2f,
				ColorRamp = Ramp(new Color(1, 1, 1, 0f), new Color(0.85f, 0.88f, 1f, 0.7f), new Color(1, 1, 1, 0f)),
			}, 0.016f, Colors.White, dot, true));
	}

	private static GradientTexture1D Ramp(Color a, Color b, Color c) =>
		new() { Gradient = new Gradient { Colors = new[] { a, b, c }, Offsets = new[] { 0f, 0.4f, 1f } } };

	private static GpuParticles3D Particles(string name, int amount, double life, Vector3 box, Vector3 at, ParticleProcessMaterial pm, float size, Color col, Texture2D tex, bool glow)
	{
		pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
		pm.EmissionBoxExtents = box;
		var mat = new StandardMaterial3D
		{
			AlbedoColor = col, AlbedoTexture = tex, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
		};
		if (glow) { mat.EmissionEnabled = true; mat.Emission = new Color(0.8f, 0.85f, 1f); mat.EmissionEnergyMultiplier = 1.5f; }
		return new GpuParticles3D
		{
			Name = name, Amount = amount, Lifetime = life, Preprocess = life, Position = at, LocalCoords = false,
			ProcessMaterial = pm, DrawPass1 = new QuadMesh { Size = Vector2.One * size, Material = mat },
			VisibilityAabb = new Aabb(new Vector3(-3, -2, -3), new Vector3(6, 6, 6)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_t += dt;
		// the aura breathes: the blur behind him swells and ebbs, slowly (about every three seconds)
		float breath = 0.5f + 0.5f * Mathf.Sin(_t * 2.1f) * Mathf.Sin(_t * 0.7f + 1f);
		if (_hazeMat != null) _hazeMat.AlbedoColor = new Color(1, 1, 1, Mathf.Lerp(0.55f, 1f, breath));
		if (_haze != null) _haze.Scale = Vector3.One * Mathf.Lerp(0.92f, 1.12f, breath);
		if (_veilMat != null) _veilMat.AlbedoColor = new Color(1, 1, 1, Mathf.Lerp(0.12f, 0.3f, 1f - breath));
		if (_veil != null) _veil.Scale = Vector3.One * Mathf.Lerp(0.95f, 1.1f, 1f - breath);
		if (_aura != null) foreach (var n in _aura.GetChildren()) if (n is GpuParticles3D gp && gp.Name == "Smoke") gp.SpeedScale = Mathf.Lerp(0.7f, 1.3f, breath);
		_eyeMat.EmissionEnergyMultiplier = Mathf.Lerp(0.6f, 12f, Glare);
		_ink.SetShaderParameter("fray", Mathf.Lerp(0.5f, 0.8f, Glare));
		// now and then the head snaps to a new angle (fast, then held): wrong, but never a flicker
		_nextTwitch -= dt;
		if (_nextTwitch <= 0f)
		{
			_nextTwitch = _rng.RandfRange(2.5f, 6f);
			_headTarget = new Vector3(_rng.RandfRange(0.2f, 0.5f), _rng.RandfRange(-0.25f, 0.25f), _rng.RandfRange(-0.6f, 0.6f));
		}
		_headTilt = _headTilt.Lerp(_headTarget, Mathf.Min(1f, dt * 14f));
		if (_head != null) _head.Rotation = _headTilt;
	}

	/// <summary>Puts him at <paramref name="at"/> (feet), facing <paramref name="toward"/>.</summary>
	public void StandAt(Vector3 at, Vector3 toward)
	{
		GlobalPosition = at;
		Vector3 d = toward - at;
		d.Y = 0;
		if (d.LengthSquared() > 0.001f) GlobalBasis = Basis.LookingAt(d.Normalized(), Vector3.Up);
	}
}
