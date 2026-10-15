using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// (the gushy-water pass, 2026-10-15: Dan, "make water flowing more gushy and less flat", and "by water I mean the blood
/// too") The kit every pour, splash and run of water or blood in the game is made from, so they all move the same way:
/// <list type="bullet">
/// <item><see cref="PourMat"/> + <see cref="Tube"/> / <see cref="Sheet"/>: a pour (<c>pour.gdshader</c>): ropey strands,
/// broken edges, surges carried down it, white as it falls (or thick, dark and glossy for blood);</item>
/// <item><see cref="Churn"/>: the boil where a pour lands (<c>churn.gdshader</c>): rings of froth running out;</item>
/// <item><see cref="Spray"/>: droplets thrown up where it lands (stretched along their flight); <see cref="Mist"/>: the
/// haze off a big one; <see cref="Drips"/>: drops that hang, stretch and fall;</item>
/// <item><see cref="BloodMat"/>: wet running blood on a surface (<c>blood_surface.gdshader</c>): pools and runs.</item>
/// </list>
/// Each liquid has its look: <see cref="Liquid.Creek"/> (the woods: olive-dark, white water), <see cref="Liquid.Sewer"/>
/// (murky brown-green, grey froth), <see cref="Liquid.Lake"/> (the lake water in the station), <see cref="Liquid.Blood"/>.
/// Particles are kept to a few dozen each; the shaders are a handful of texture reads, no loops.
/// </summary>
public static class FlowFx
{
	public enum Liquid { Creek, Sewer, Lake, Blood }

	private static Shader _pour, _churn, _blood;
	public static Shader PourShader => _pour ??= GD.Load<Shader>("res://assets/shaders/pour.gdshader");
	public static Shader ChurnShader => _churn ??= GD.Load<Shader>("res://assets/shaders/churn.gdshader");
	public static Shader BloodShader => _blood ??= GD.Load<Shader>("res://assets/shaders/blood_surface.gdshader");

	public static readonly Color BloodTint = new(0.26f, 0.012f, 0.012f), BloodDeep = new(0.07f, 0.0f, 0.004f);

	private static Color Body(Liquid l) => l switch
	{
		Liquid.Creek => new Color(0.11f, 0.13f, 0.1f),
		Liquid.Sewer => new Color(0.16f, 0.15f, 0.1f),
		Liquid.Lake => new Color(0.07f, 0.1f, 0.08f),
		_ => BloodTint,
	};

	private static Color Froth(Liquid l) => l switch
	{
		Liquid.Creek => new Color(0.6f, 0.63f, 0.61f),
		Liquid.Sewer => new Color(0.46f, 0.44f, 0.36f),
		Liquid.Lake => new Color(0.55f, 0.6f, 0.56f),
		_ => new Color(0.4f, 0.03f, 0.03f),
	};

	/// <summary>A pour's material. <paramref name="speed"/> metres a second down it; <paramref name="strands"/> how many
	/// ropes across it.</summary>
	public static ShaderMaterial PourMat(Liquid l, float speed = 2.4f, float strands = 6f, bool tube = false, float wobble = 0.05f, float aerateLen = 1.6f)
	{
		var m = new ShaderMaterial { Shader = PourShader };
		m.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		m.SetShaderParameter("tint", Body(l));
		m.SetShaderParameter("foam_color", Froth(l));
		m.SetShaderParameter("speed", speed);
		m.SetShaderParameter("strands", strands);
		m.SetShaderParameter("tube", tube ? 1f : 0f);
		m.SetShaderParameter("wobble", wobble);
		m.SetShaderParameter("aerate_len", aerateLen);
		m.SetShaderParameter("viscous", l == Liquid.Blood ? 1f : 0f);
		m.SetShaderParameter("foam", l switch { Liquid.Creek => 1f, Liquid.Sewer => 0.85f, Liquid.Lake => 0.8f, _ => 0f });
		m.SetShaderParameter("glow", l switch { Liquid.Creek => 0.035f, Liquid.Sewer => 0.05f, _ => 0.02f });
		return m;
	}

	/// <summary>Wet blood on a surface: a pool (<paramref name="run"/> false: UV across a disc) or a run (UV.x across, UV.y
	/// along in metres).</summary>
	public static ShaderMaterial BloodMat(bool run = false, float spread = 1f, float speed = 0.1f, float scale = 1f)
	{
		var m = new ShaderMaterial { Shader = BloodShader };
		m.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		m.SetShaderParameter("shape", run ? 1f : 0f);
		m.SetShaderParameter("spread", spread);
		m.SetShaderParameter("speed", speed);
		m.SetShaderParameter("scale", scale);
		m.SetShaderParameter("color", BloodTint);
		m.SetShaderParameter("deep", BloodDeep);
		return m;
	}

	/// <summary>A tube along <paramref name="path"/> (in <paramref name="parent"/>'s space), its radius going from
	/// <paramref name="r0"/> to <paramref name="r1"/>: a rope of pouring water or blood. UV.x round it, UV.y metres down it.</summary>
	public static MeshInstance3D Tube(Node3D parent, IList<Vector3> path, float r0, float r1, Material mat, string name, int sides = 7)
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		float along = 0f;
		var rings = new List<(Vector3 c, Vector3 a, Vector3 b, float v, float r)>();
		for (int i = 0; i < path.Count; i++)
		{
			Vector3 t = (i < path.Count - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1]).Normalized();
			if (i > 0) along += path[i].DistanceTo(path[i - 1]);
			Vector3 a = (Mathf.Abs(t.Y) > 0.9f ? Vector3.Right : Vector3.Up).Cross(t).Normalized();
			Vector3 b = t.Cross(a).Normalized();
			rings.Add((path[i], a, b, along, Mathf.Lerp(r0, r1, i / (float)(path.Count - 1))));
		}
		for (int i = 0; i < rings.Count - 1; i++)
			for (int j = 0; j < sides; j++)
			{
				float u0 = j / (float)sides, u1 = (j + 1) / (float)sides;
				Vector3 P(int ri, float u, out Vector3 n)
				{
					var rg = rings[ri];
					float ang = u * Mathf.Tau;
					n = rg.a * Mathf.Cos(ang) + rg.b * Mathf.Sin(ang);
					return rg.c + n * rg.r;
				}
				var p00 = P(i, u0, out var n00); var p10 = P(i, u1, out var n10);
				var p01 = P(i + 1, u0, out var n01); var p11 = P(i + 1, u1, out var n11);
				void V(Vector3 p, Vector3 n, float u, float v) { st.SetNormal(n); st.SetUV(new Vector2(u, v)); st.AddVertex(p); }
				V(p00, n00, u0, rings[i].v); V(p11, n11, u1, rings[i + 1].v); V(p10, n10, u1, rings[i].v);
				V(p00, n00, u0, rings[i].v); V(p01, n01, u0, rings[i + 1].v); V(p11, n11, u1, rings[i + 1].v);
			}
		var mi = new MeshInstance3D { Name = name, Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild(mi);
		return mi;
	}

	/// <summary>A sheet down <paramref name="path"/> (its middle line, in the parent's space), <paramref name="across"/>
	/// (unit, its sideways) times the width going from <paramref name="w0"/> to <paramref name="w1"/>, cut into
	/// <paramref name="cols"/> columns so it can swell. UV.x across 0..1, UV.y metres down it; the vertex alpha fades the
	/// first and last rows in and out by <paramref name="fadeIn"/> / <paramref name="fadeOut"/>.</summary>
	public static MeshInstance3D Sheet(Node3D parent, IList<Vector3> path, Vector3 across, float w0, float w1, Material mat, string name, int cols = 6, float fadeIn = 1f, float fadeOut = 1f)
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		float along = 0f;
		var vs = new float[path.Count];
		for (int i = 1; i < path.Count; i++) { along += path[i].DistanceTo(path[i - 1]); vs[i] = along; }
		for (int i = 0; i < path.Count - 1; i++)
		{
			Vector3 t = (path[i + 1] - path[i]).Normalized();
			Vector3 n = t.Cross(across).Normalized();
			float wa = Mathf.Lerp(w0, w1, i / (float)(path.Count - 1)), wb = Mathf.Lerp(w0, w1, (i + 1) / (float)(path.Count - 1));
			float aa = i == 0 ? fadeIn : 1f, ab = i + 1 == path.Count - 1 ? fadeOut : 1f;
			for (int j = 0; j < cols; j++)
			{
				float u0 = j / (float)cols, u1 = (j + 1) / (float)cols;
				Vector3 Q(int k, float u, float w) => path[k] + across * (u - 0.5f) * w;
				void V(Vector3 p, float u, float v, float a) { st.SetColor(new Color(1, 1, 1, a)); st.SetNormal(n); st.SetUV(new Vector2(u, v)); st.AddVertex(p); }
				V(Q(i, u0, wa), u0, vs[i], aa); V(Q(i, u1, wa), u1, vs[i], aa); V(Q(i + 1, u1, wb), u1, vs[i + 1], ab);
				V(Q(i, u0, wa), u0, vs[i], aa); V(Q(i + 1, u1, wb), u1, vs[i + 1], ab); V(Q(i + 1, u0, wb), u0, vs[i + 1], ab);
			}
		}
		var mi = new MeshInstance3D { Name = name, Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild(mi);
		return mi;
	}

	/// <summary>The boil where a pour lands: a flat disc of churning froth (or rolling blood), <paramref name="radius"/>
	/// metres, at <paramref name="at"/> in the parent's space.</summary>
	public static MeshInstance3D Churn(Node3D parent, Vector3 at, float radius, Liquid l, float strength = 1f, string name = "Churn")
	{
		var m = new ShaderMaterial { Shader = ChurnShader };
		m.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		m.SetShaderParameter("foam_color", Froth(l));
		m.SetShaderParameter("water_color", l == Liquid.Blood ? BloodTint * 0.6f : Body(l));
		m.SetShaderParameter("strength", strength);
		m.SetShaderParameter("viscous", l == Liquid.Blood ? 1f : 0f);
		m.SetShaderParameter("heave", Mathf.Min(0.06f, radius * 0.05f));
		var mi = new MeshInstance3D
		{
			Name = name, Position = at, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Mesh = new PlaneMesh { Size = new Vector2(radius * 2f, radius * 2f), SubdivideWidth = 6, SubdivideDepth = 6 },
		};
		parent.AddChild(mi);
		return mi;
	}

	private static readonly Dictionary<Liquid, StandardMaterial3D> _dropMats = new();
	private static Texture2D _dropTex;

	/// <summary>A falling drop's look: a thin streak, lit (it's wet, it catches the lamps), white-grey water or dark blood.</summary>
	private static StandardMaterial3D DropMat(Liquid l)
	{
		if (_dropMats.TryGetValue(l, out var m)) return m;
		_dropTex ??= RainVfx.StreakTexture();
		var c = l switch
		{
			Liquid.Blood => new Color(0.3f, 0.015f, 0.015f, 0.95f),
			Liquid.Sewer => new Color(0.55f, 0.53f, 0.45f, 0.6f),
			_ => new Color(0.78f, 0.82f, 0.82f, 0.65f),
		};
		m = new StandardMaterial3D
		{
			ResourceName = $"flow_drop_{l}",
			AlbedoColor = c, AlbedoTexture = _dropTex,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY, BillboardKeepScale = true,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			Roughness = 0.1f, MetallicSpecular = 0.8f,
			VertexColorUseAsAlbedo = true,
			DisableReceiveShadows = true,
		};
		// water drops catch what light there is (a faint glint of their own, so the spray reads in the dark at 640x360);
		// blood stays dark
		if (l != Liquid.Blood) { m.EmissionEnabled = true; m.Emission = new Color(c.R, c.G, c.B) * 0.12f; }
		_dropMats[l] = m;
		return m;
	}

	/// <summary>Droplets thrown up and out where a pour lands, stretched along their flight. <paramref name="size"/> is the
	/// spread (metres); <paramref name="power"/> how hard it's thrown (m/s up).</summary>
	public static GpuParticles3D Spray(Node3D parent, Vector3 at, Liquid l, float size = 0.6f, int amount = 40, float power = 2.2f, string name = "Spray")
	{
		bool blood = l == Liquid.Blood;
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(size * 0.5f, 0.02f, size * 0.5f),
			Direction = Vector3.Up, Spread = 50f,
			InitialVelocityMin = power * 0.4f, InitialVelocityMax = power * (blood ? 0.7f : 1f),
			Gravity = new Vector3(0, -9.8f, 0),
			ScaleMin = 0.6f, ScaleMax = 1.3f,
			ParticleFlagAlignY = true,
		};
		var p = new GpuParticles3D
		{
			Name = name, Amount = amount, Lifetime = blood ? 0.45 : 0.6, Preprocess = 0.6, Randomness = 0.6f,
			ProcessMaterial = pm,
			// (big enough to be a pixel or two at 640x360 from a few metres off)
			DrawPass1 = new QuadMesh { Size = blood ? new Vector2(0.03f, 0.08f) : new Vector2(0.028f, 0.12f), Material = DropMat(l) },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = at,
			VisibilityAabb = new Aabb(new Vector3(-2, -1, -2), new Vector3(4, 3, 4)),
		};
		parent.AddChild(p);
		return p;
	}

	private static CurveXyzTexture _stretch;
	/// <summary>A drop's scale over its life (with the particle's Y aligned to its fall): short and fat as it gathers,
	/// long as it lets go and falls.</summary>
	public static CurveXyzTexture StretchCurve()
	{
		if (_stretch != null) return _stretch;
		var sc = new CurveXyzTexture { CurveX = new Curve(), CurveY = new Curve(), CurveZ = new Curve() };
		sc.CurveX.AddPoint(new Vector2(0f, 0.6f)); sc.CurveX.AddPoint(new Vector2(0.25f, 1f)); sc.CurveX.AddPoint(new Vector2(1f, 0.7f));
		sc.CurveY.AddPoint(new Vector2(0f, 0.3f)); sc.CurveY.AddPoint(new Vector2(0.22f, 0.9f)); sc.CurveY.AddPoint(new Vector2(0.32f, 1.8f)); sc.CurveY.AddPoint(new Vector2(1f, 1.4f));
		sc.CurveZ.AddPoint(new Vector2(0f, 1f)); sc.CurveZ.AddPoint(new Vector2(1f, 1f));
		return _stretch = sc;
	}

	/// <summary>A falling drop's mesh: a thin streak of water or blood.</summary>
	public static QuadMesh DropQuad(Liquid l, Vector2 size) => new() { Size = size, Material = DropMat(l) };

	/// <summary>A spray or drips turned to another liquid (Room 2's water turning to blood).</summary>
	public static void SetLiquid(GpuParticles3D p, Liquid l)
	{
		if (p?.DrawPass1 is QuadMesh q) q.Material = DropMat(l);
	}

	/// <summary>A haze of fine spray off a big pour (a few soft puffs drifting up and away).</summary>
	public static GpuParticles3D Mist(Node3D parent, Vector3 at, Vector3 drift, float size = 1.4f, int amount = 10, float alpha = 0.16f, string name = "Mist")
	{
		var ramp = new Gradient();
		ramp.SetColor(0, new Color(1, 1, 1, 0));
		ramp.SetColor(1, new Color(1, 1, 1, 0));
		ramp.AddPoint(0.3f, new Color(1, 1, 1, 1));
		ramp.AddPoint(0.7f, new Color(1, 1, 1, 0.6f));
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(size * 0.5f, 0.1f, size * 0.3f),
			Direction = (drift + Vector3.Up * 1.2f).Normalized(), Spread = 35f,
			InitialVelocityMin = 0.15f, InitialVelocityMax = 0.45f,
			Gravity = new Vector3(0, 0.04f, 0),
			ScaleMin = 0.8f, ScaleMax = 1.6f,
			ColorRamp = new GradientTexture1D { Gradient = ramp },
		};
		var draw = new QuadMesh { Size = new Vector2(1.1f, 1.1f) };
		draw.Material = new StandardMaterial3D
		{
			AlbedoTexture = PropTextures.Puff(),
			AlbedoColor = new Color(0.92f, 0.94f, 0.95f, alpha),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		var p = new GpuParticles3D
		{
			Name = name, Amount = amount, Lifetime = 3.2, Preprocess = 3.0,
			ProcessMaterial = pm, DrawPass1 = draw, Position = at,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			VisibilityAabb = new Aabb(new Vector3(-3, -1, -3), new Vector3(6, 5, 6)),
		};
		parent.AddChild(p);
		return p;
	}

	/// <summary>Drops that gather, stretch long and fall from <paramref name="at"/> (a leak, a lip, a jaw): each one swells
	/// out of the source, hangs as it stretches, then drops. <paramref name="perSecond"/> roughly how many.</summary>
	public static GpuParticles3D Drips(Node3D parent, Vector3 at, Liquid l, float perSecond = 1.5f, float fall = 3f, float spread = 0.02f, string name = "Drips")
	{
		bool blood = l == Liquid.Blood;
		var sc = StretchCurve();
		float life = Mathf.Sqrt(2f * fall / 9.8f) + 0.35f;
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = spread,
			Direction = Vector3.Down, Spread = 2f,
			InitialVelocityMin = 0f, InitialVelocityMax = 0.05f,
			Gravity = new Vector3(0, blood ? -7f : -9.8f, 0),
			ScaleCurve = sc,
			ParticleFlagAlignY = true,
		};
		int amount = Mathf.Max(2, Mathf.CeilToInt(perSecond * life));
		var p = new GpuParticles3D
		{
			Name = name, Amount = amount, Lifetime = life, Preprocess = life, Randomness = 0.8f,
			ProcessMaterial = pm,
			DrawPass1 = new QuadMesh { Size = blood ? new Vector2(0.016f, 0.05f) : new Vector2(0.012f, 0.05f), Material = DropMat(l) },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = at,
			VisibilityAabb = new Aabb(new Vector3(-0.5f, -fall - 0.5f, -0.5f), new Vector3(1f, fall + 1f, 1f)),
		};
		parent.AddChild(p);
		return p;
	}

	/// <summary>One of each of the kit's things, drawn in the level's warm-up (the ones events make later: the bleeding
	/// skull, the bowl, the stairs of blood, a torrent's foot), so none of them stutters as it first shows. Once a level.</summary>
	public static void Prewarm(Node from)
	{
		var kit = new Node3D { Name = "FlowFxWarm" };
		var path = new[] { Vector3.Zero, Vector3.Down * 0.5f, Vector3.Down };
		foreach (var l in new[] { Liquid.Creek, Liquid.Blood })
		{
			Tube(kit, path, 0.05f, 0.06f, PourMat(l, tube: true), $"Tube{l}");
			Sheet(kit, path, Vector3.Right, 0.3f, 0.4f, PourMat(l), $"Sheet{l}");
			Churn(kit, Vector3.Zero, 0.4f, l);
			Spray(kit, Vector3.Zero, l);
			Drips(kit, Vector3.Zero, l);
		}
		var pool = new MeshInstance3D { Name = "Pool", Mesh = new PlaneMesh { Size = Vector2.One * 0.3f }, MaterialOverride = BloodMat() };
		kit.AddChild(pool);
		ShaderWarmup.Prewarm(from, kit);
	}
}
