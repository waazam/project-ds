using Godot;

namespace ProjectDS.World.LakeParts;

/// <summary>
/// Throwaway water effects for the lake, each a self-freeing node parented under the level:
/// <see cref="Splash"/> (a burst of spray: an oar biting, a wave slapping the bow, a tentacle
/// breaking the surface), <see cref="Ripple"/> (a ring spreading and fading on the water) and
/// <see cref="Column"/> (a tall plume thrown up as something huge comes out of the lake).
/// Lit warm by the sunrise (unshaded, pale peach) so they never read as grey smoke.
/// </summary>
public static class LakeFx
{
	private static readonly Color SprayTint = new(1f, 0.92f, 0.84f, 0.85f);
	private static Texture2D _ring, _soft;

	/// <summary>A soft round dot whose alpha falls all the way to zero well inside the square, so a big
	/// billboard never shows its edges against the sky (the shared Puff texture does, at lake scale).</summary>
	public static Texture2D SoftDot()
	{
		if (_soft != null) return _soft;
		const int n = 32;
		var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float d = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).Length() / (n * 0.5f);
				float a = Mathf.SmoothStep(0.95f, 0.1f, d);
				img.SetPixel(x, y, new Color(1, 1, 1, a * a));
			}
		img.GenerateMipmaps();
		return _soft = ImageTexture.CreateFromImage(img);
	}

	/// <summary>A burst of spray thrown up and out. <paramref name="size"/> 1 = an oar stroke.</summary>
	public static void Splash(Node parent, Vector3 at, float size = 1f, int amount = 18, Vector3? push = null)
	{
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 0.12f * size,
			Direction = (Vector3.Up + (push ?? Vector3.Zero)).Normalized(),
			Spread = 38f,
			InitialVelocityMin = 1.4f * Mathf.Sqrt(size),
			InitialVelocityMax = 3.2f * Mathf.Sqrt(size),
			Gravity = new Vector3(0, -9.8f, 0),
			DampingMin = 0.4f, DampingMax = 1.2f,
			ScaleMin = 0.5f, ScaleMax = 1.4f,
			ColorRamp = Fade(),
		};
		Burst(parent, "Splash", at, pm, amount, 1.1f, SoftDot(), 0.14f * size);
	}

	/// <summary>A tall plume of water (a tentacle erupting), falling back as rain.</summary>
	public static void Column(Node parent, Vector3 at, float height = 6f)
	{
		float v = Mathf.Sqrt(2f * 9.8f * height);
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 0.6f,
			Direction = Vector3.Up,
			Spread = 14f,
			InitialVelocityMin = v * 0.55f,
			InitialVelocityMax = v,
			Gravity = new Vector3(0, -9.8f, 0),
			ScaleMin = 0.5f, ScaleMax = 1.4f,
			ColorRamp = Fade(),
		};
		Burst(parent, "Column", at, pm, 70, 2.4f, SoftDot(), 0.6f);
		// a heavy skirt of spray at the base
		Splash(parent, at, 3.2f, 40);
		Ripple(parent, at, 7f, 3f);
	}

	/// <summary>A ring spreading out over the water from <paramref name="at"/> and fading.</summary>
	public static void Ripple(Node parent, Vector3 at, float radius = 1.6f, float seconds = 1.6f, float alpha = 0.55f)
	{
		var mat = new StandardMaterial3D
		{
			AlbedoTexture = Ring(),
			AlbedoColor = new Color(1f, 0.93f, 0.86f, alpha),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		var mi = new MeshInstance3D
		{
			Name = "Ripple",
			Mesh = new QuadMesh { Size = Vector2.One * 2f, Orientation = PlaneMesh.OrientationEnum.Y },
			MaterialOverride = mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Scale = Vector3.One * 0.15f,
		};
		parent.AddChild(mi);
		mi.GlobalPosition = at + Vector3.Up * 0.04f;
		var tw = mi.CreateTween().SetParallel();
		tw.TweenProperty(mi, "scale", Vector3.One * radius, seconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		tw.TweenProperty(mat, "albedo_color:a", 0f, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		tw.Chain().TweenCallback(Callable.From(mi.QueueFree));
	}

	private static void Burst(Node parent, string name, Vector3 at, ParticleProcessMaterial pm, int amount, float life, Texture2D tex, float quad)
	{
		var draw = new QuadMesh { Size = Vector2.One * quad };
		draw.Material = new StandardMaterial3D
		{
			AlbedoTexture = tex,
			AlbedoColor = SprayTint,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		var p = new GpuParticles3D
		{
			Name = name, Amount = amount, Lifetime = life, OneShot = true, Explosiveness = 0.92f,
			ProcessMaterial = pm, DrawPass1 = draw, LocalCoords = false,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			VisibilityAabb = new Aabb(new Vector3(-8, -2, -8), new Vector3(16, 30, 16)),
		};
		parent.AddChild(p);
		p.GlobalPosition = at;
		p.Emitting = true;
		p.Finished += p.QueueFree;
	}

	private static GradientTexture1D Fade()
	{
		var g = new Gradient();
		g.SetColor(0, new Color(1, 1, 1, 1));
		g.SetColor(1, new Color(1, 1, 1, 0));
		g.AddPoint(0.6f, new Color(1, 1, 1, 0.7f));
		return new GradientTexture1D { Gradient = g };
	}

	/// <summary>A thin bright ring on transparency, with a fainter inner echo.</summary>
	private static Texture2D Ring()
	{
		if (_ring != null) return _ring;
		const int n = 64;
		var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float r = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).Length() / (n * 0.5f);
				float outer = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.06f, 2f));
				float inner = 0.4f * Mathf.Exp(-Mathf.Pow((r - 0.62f) / 0.05f, 2f));
				img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(outer + inner, 0f, 1f)));
			}
		img.GenerateMipmaps();
		return _ring = ImageTexture.CreateFromImage(img);
	}
}
