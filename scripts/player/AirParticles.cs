using Godot;

namespace ProjectDS.Player;

/// <summary>
/// What hangs in the air round the player (the fidelity pass, 2026-10-02), by place (ForestAtmosphere's blend):
/// - indoors and underground, dust: fine specks drifting, lit like anything else, so they only show where a light
///   falls (the lantern's glow round you, a lamp's pool);
/// - in the woods, spores and seed fluff, a little larger, drifting slower;
/// - in the cold (the winter woods, the cold lodge), your breath: a faint puff below the view with each breath,
///   quicker when you've been running.
/// All slow and faint: nothing flickers, nothing crosses the view fast.
/// </summary>
public partial class AirParticles : Node3D
{
	private GpuParticles3D _dust, _spores, _breath;
	private ShaderMaterial _dustPm, _sporePm;
	private bool _dustOn, _sporesOn;
	private PlayerController _player;
	private double _check, _nextBreath = 2.0, _t;
	private bool _cold;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		TopLevel = true;
		// (the weather pass: Weather's wrapping layers, so the air is as thick wherever you walk; they ignore the roofs)
		_dust = World.Weather.MakeLayer(this, "Dust", 220, new Vector3(2.6f, 1.6f, 2.6f), out _dustPm, out var dd);
		_dustPm.SetShaderParameter("settle", 0f);
		_dustPm.SetShaderParameter("wind_carry", 0f);
		_dustPm.SetShaderParameter("fall", new Vector2(-0.015f, 0.02f));
		_dustPm.SetShaderParameter("flutter", 0.035f);
		_dustPm.SetShaderParameter("flutter_rate", 0.35f);
		_dustPm.SetShaderParameter("spin", 0.3f);
		_dustPm.SetShaderParameter("size_range", new Vector2(0.01f, 0.022f));
		_dustPm.SetShaderParameter("frames", new Vector2(14f, 2f));
		dd.SetShaderParameter("near_fade", 0.15f);
		dd.SetShaderParameter("opacity", 0.6f);
		dd.SetShaderParameter("glow", 0f);
		dd.SetShaderParameter("tint", new Color(0.72f, 0.68f, 0.6f));
		_spores = World.Weather.MakeLayer(this, "Spores", 160, new Vector3(6f, 3f, 6f), out _sporePm, out var sd);
		_sporePm.SetShaderParameter("settle", 0f);
		_sporePm.SetShaderParameter("wind_carry", 0.5f);
		_sporePm.SetShaderParameter("fall", new Vector2(0.02f, 0.12f));
		_sporePm.SetShaderParameter("flutter", 0.12f);
		_sporePm.SetShaderParameter("flutter_rate", 0.4f);
		_sporePm.SetShaderParameter("size_range", new Vector2(0.016f, 0.03f));
		_sporePm.SetShaderParameter("frames", new Vector2(12f, 4f));
		sd.SetShaderParameter("near_fade", 0.25f);
		sd.SetShaderParameter("opacity", 0.5f);
		sd.SetShaderParameter("glow", 0f);
		sd.SetShaderParameter("tint", new Color(0.7f, 0.72f, 0.66f));
		_breath = Breath();
	}

	private GpuParticles3D Breath()
	{
		var p = new GpuParticles3D
		{
			Name = "Breath", Amount = 10, Lifetime = 2.4f, Emitting = false, OneShot = true, Explosiveness = 0.75f, LocalCoords = false,
			VisibilityAabb = new Aabb(new Vector3(-2, -2, -2), new Vector3(4, 4, 4)), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.03f,
				Direction = new Vector3(0, 0.25f, -1f), Spread = 18f, InitialVelocityMin = 0.25f, InitialVelocityMax = 0.45f,
				Gravity = new Vector3(0, 0.06f, 0), DampingMin = 0.4f, DampingMax = 0.7f,
				ScaleMin = 0.8f, ScaleMax = 1.4f,
				ScaleCurve = new CurveTexture { Curve = Grow() },
				AlphaCurve = new CurveTexture { Curve = Fade() },
				// (the weather pass: a puff of vapour out of the smoke's flipbook, billowing and thinning as it goes)
				AnimSpeedMin = 1f, AnimSpeedMax = 1f, AngleMin = -180f, AngleMax = 180f,
			},
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(0.24f, 0.24f),
				Material = new StandardMaterial3D
				{
					ParticlesAnimHFrames = 4, ParticlesAnimVFrames = 4, ParticlesAnimLoop = false,
					AlbedoColor = new Color(0.75f, 0.77f, 0.8f, 0.12f), AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/weather/smoke_sheet.png"), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
					ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel, Roughness = 1f, MetallicSpecular = 0f,
				},
			},
		};
		AddChild(p);
		return p;
	}

	private static Curve Fade()
	{
		var c = new Curve();
		c.AddPoint(new Vector2(0f, 0f));
		c.AddPoint(new Vector2(0.2f, 1f));
		c.AddPoint(new Vector2(0.75f, 1f));
		c.AddPoint(new Vector2(1f, 0f));
		return c;
	}

	private static Curve Grow()
	{
		var c = new Curve();
		c.AddPoint(new Vector2(0f, 0.35f));
		c.AddPoint(new Vector2(1f, 1.6f));
		return c;
	}

	private static GradientTexture2D _dot;
	private static GradientTexture2D Dot() => _dot ??= new GradientTexture2D
	{
		Width = 32, Height = 32, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
		Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 1f } },
	};

	public override void _Process(double delta)
	{
		var cam = _player?.CameraRig?.Camera;
		if (cam == null) return;
		_t += delta;
		GlobalPosition = cam.GlobalPosition;
		World.Weather.Follow(_dust, _dustPm, cam.GlobalPosition, _dustOn);
		World.Weather.Follow(_spores, _sporePm, cam.GlobalPosition, _sporesOn);
		if ((_check -= delta) <= 0)
		{
			_check = 0.5;
			var atmo = GetTree().GetFirstNodeInGroup("atmosphere") as World.ForestAtmosphere;
			float inside = atmo == null ? 0f : Mathf.Max(Mathf.Max(atmo.Underground, atmo.Interior), atmo.Lodge * (1f - atmo.LodgeCold * 0.5f));
			// or under a roof: a ceiling over the head, or walls on three sides or more (some ceilings have no colliders)
			if (inside < 0.5f && IsInsideTree())
			{
				var space = GetWorld3D().DirectSpaceState;
				var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, cam.GlobalPosition + Vector3.Up * 8f, 1, new Godot.Collections.Array<Rid> { _player.GetRid() });
				if (space.IntersectRay(q).Count > 0 || World.DecalDresser.WalledIn(space, cam.GlobalPosition, 8f, _player.GetRid()) >= 3) inside = 1f;
			}
			float cold = atmo == null ? 0f : Mathf.Max(Mathf.Max(atmo.Winter, atmo.WinterDusk) * (1f - atmo.Underground), atmo.Lodge * atmo.LodgeCold);
			_dustOn = inside > 0.5f;
			_sporesOn = inside < 0.5f && cold < 0.5f && atmo != null;
			// (and down in Act 24's ice, where it's colder than anywhere: their breath in the lantern light, 2026-10-07)
			_cold = cold > 0.5f || World.SnowMaze.Act24Maze.Instance is { InMaze: true };
		}
		if (_cold && _t >= _nextBreath)
		{
			// a breath: from just under the eye, out ahead
			float exert = Mathf.Clamp(1f - (_player.Stamina?.Value ?? 1f), 0f, 1f);   // (winded: quicker)
			_nextBreath = _t + Mathf.Lerp(3.8f, 1.6f, exert) * GD.RandRange(0.85, 1.15);
			_breath.GlobalTransform = new Transform3D(cam.GlobalBasis, cam.GlobalPosition + cam.GlobalBasis * new Vector3(0, -0.13f, -0.12f));
			_breath.Restart();
		}
	}
}
