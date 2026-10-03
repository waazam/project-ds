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
	private PlayerController _player;
	private double _check, _nextBreath = 2.0, _t;
	private bool _cold;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		TopLevel = true;
		_dust = Specks("Dust", 140, 9f, new Vector3(2.2f, 1.4f, 2.2f), 0.012f, new Color(0.72f, 0.68f, 0.6f, 0.55f), 0.025f);
		_spores = Specks("Spores", 70, 12f, new Vector3(5f, 2.2f, 5f), 0.02f, new Color(0.7f, 0.72f, 0.66f, 0.45f), 0.05f);
		_breath = Breath();
	}

	/// <summary>Slow specks in a box round the view (world-space, so walking moves through them).</summary>
	private GpuParticles3D Specks(string name, int amount, float life, Vector3 box, float size, Color colour, float drift)
	{
		var p = new GpuParticles3D
		{
			Name = name, Amount = amount, Lifetime = life, Emitting = false, LocalCoords = false, Preprocess = life,
			VisibilityAabb = new Aabb(-box * 1.5f, box * 3f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = box,
				Direction = Vector3.Up, Spread = 180f, InitialVelocityMin = drift * 0.2f, InitialVelocityMax = drift,
				Gravity = new Vector3(0, -drift * 0.15f, 0),
				TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.15f, TurbulenceNoiseScale = 3f, TurbulenceNoiseSpeedRandom = 0.2f,
				ScaleMin = 0.5f, ScaleMax = 1.3f,
				// in and out softly over its life (never a pop)
				AlphaCurve = new CurveTexture { Curve = Fade() },
			},
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(size, size),
				Material = new StandardMaterial3D
				{
					AlbedoColor = colour, AlbedoTexture = Dot(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
					// lit: a speck shows only where light falls on it
					ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel, Roughness = 1f, MetallicSpecular = 0f,
				},
			},
		};
		AddChild(p);
		return p;
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
			},
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(0.22f, 0.22f),
				Material = new StandardMaterial3D
				{
					AlbedoColor = new Color(0.75f, 0.77f, 0.8f, 0.09f), AlbedoTexture = Dot(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
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
			_dust.Emitting = inside > 0.5f;
			_spores.Emitting = inside < 0.5f && cold < 0.5f && atmo != null;
			_cold = cold > 0.5f;
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
