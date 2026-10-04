using Godot;

namespace ProjectDS.World;

/// <summary>
/// Shafts of daylight through windows, the dust drifting in them (window_shaft.gdshader; 2026-10-04, the owner: "make
/// everything stand out more"). Each shaft is a few crossed cards from the window along the light's way, and a handful of
/// dust motes turning slowly in it. The church's clerestory and the lodge's dining hall have them; each place keeps one
/// material for its shafts, so the whole set can be dimmed together (a storm coming up, the lodge freezing over).
/// </summary>
public static class WindowShafts
{
	private static NoiseTexture2D _noise;
	private static StandardMaterial3D _moteMat;

	/// <summary>A material for a set of shafts (its colour and strength; dim them together through it).</summary>
	public static ShaderMaterial Material(Color color, float strength)
	{
		_noise ??= new NoiseTexture2D { Width = 128, Height = 128, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.03f, FractalOctaves = 3, Seed = 3301 } };
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/window_shaft.gdshader"), ResourceName = "window_shaft" };
		m.SetShaderParameter("shaft_color", new Vector3(color.R, color.G, color.B));
		m.SetShaderParameter("strength", strength);
		m.SetShaderParameter("noise_tex", _noise);
		return m;
	}

	/// <summary>A shaft from a window (its centre <paramref name="c"/>, its width along <paramref name="along"/>, its
	/// height up <paramref name="up"/>) along <paramref name="dir"/> for <paramref name="length"/> metres; with
	/// <paramref name="motes"/> dust motes in it. Parent-local.</summary>
	public static Node3D Add(Node3D parent, ShaderMaterial mat, Vector3 c, Vector3 along, Vector3 up, float w, float h, Vector3 dir, float length, int motes = 24)
	{
		var node = new Node3D { Name = "Shaft" };
		parent.AddChild(node);
		dir = dir.Normalized();
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		void Card(Vector3 a, Vector3 b)
		{
			Vector3 a2 = a + dir * length, b2 = b + dir * length;
			var n = (b - a).Cross(dir).Normalized();
			void V(Vector3 p, float u, float v) { st.SetNormal(n); st.SetUV(new Vector2(u, v)); st.AddVertex(p); }
			V(a, 0, 0); V(b, 1, 0); V(b2, 1, 1);
			V(a, 0, 0); V(b2, 1, 1); V(a2, 0, 1);
		}
		// across its width at three heights, and up its height at two places across
		foreach (float t in new[] { -0.3f, 0f, 0.3f }) Card(c + up * (h * t) - along * (w * 0.5f), c + up * (h * t) + along * (w * 0.5f));
		foreach (float t in new[] { -0.2f, 0.2f }) Card(c + along * (w * t) - up * (h * 0.5f), c + along * (w * t) + up * (h * 0.5f));
		node.AddChild(new MeshInstance3D { Name = "Cards", Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		if (motes <= 0) return node;
		_moteMat ??= new StandardMaterial3D
		{
			ResourceName = "shaft_mote", AlbedoColor = new Color(0.9f, 0.88f, 0.8f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoTexture = LakeParts.LakeFx.SoftDot(),
		};
		// (the motes' box runs down the shaft's middle stretch, turned to lie along it)
		var basis = new Basis(along.Normalized(), up.Normalized(), dir).Orthonormalized();
		node.AddChild(new GpuParticles3D
		{
			Name = "Dust", Amount = motes, Lifetime = 9f, Preprocess = 9f, Emitting = true,
			Transform = new Transform3D(basis, c + dir * (length * 0.4f)),
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(w * 0.35f, h * 0.35f, length * 0.3f),
				Gravity = new Vector3(0, -0.01f, 0), InitialVelocityMin = 0.01f, InitialVelocityMax = 0.05f, Spread = 180f,
				TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.15f, TurbulenceNoiseSpeedRandom = 0.2f, ScaleMin = 0.6f, ScaleMax = 1.3f,
			},
			DrawPass1 = new QuadMesh { Size = new Vector2(0.012f, 0.012f), Material = _moteMat },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			VisibilityAabb = new Aabb(new Vector3(-w, -h, -length * 0.6f), new Vector3(w * 2f, h * 2f, length * 1.2f)),
		});
		return node;
	}
}
