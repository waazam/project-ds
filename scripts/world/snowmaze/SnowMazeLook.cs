using Godot;

namespace ProjectDS.World.SnowMaze;

/// <summary>The snow cave's materials and small meshes (ice_cave.gdshader and its noises; the icicles; the sky far up a hole).</summary>
public static class SnowMazeLook
{
	private static ShaderMaterial _cave;
	private static StandardMaterial3D _sky;
	private static Mesh _icicle;

	public static ShaderMaterial CaveMaterial()
	{
		if (_cave != null) return _cave;
		_cave = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/ice_cave.gdshader"), ResourceName = "snow_cave" };
		// the baked sets (tools/textures/ice_maze.py, 1024 px, mipmapped and compressed on import)
		foreach (var t in new[] { "ice_albedo", "ice_normal", "ice_rough", "snow_albedo", "snow_normal" })
			_cave.SetShaderParameter(t, GD.Load<Texture2D>($"res://assets/textures/ice/{t}.png"));
		_cave.SetShaderParameter("noise_tex", new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true, Noise = new FastNoiseLite { Frequency = 0.02f, FractalOctaves = 4, Seed = 7702 } });
		return _cave;
	}

	/// <summary>Packed snow, gouged (packed_snow.gdshader): the tunnel out of the lodge's door down to the hole (2026-10-13c:
	/// in the fresh snow it read as white plastic). The maze's own trodden-snow set for its grain.</summary>
	public static ShaderMaterial PackedSnow()
	{
		if (_packed != null) return _packed;
		_packed = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/packed_snow.gdshader"), ResourceName = "packed_snow" };
		_packed.SetShaderParameter("snow_albedo", GD.Load<Texture2D>("res://assets/textures/ice/snow_albedo.png"));
		_packed.SetShaderParameter("snow_normal", GD.Load<Texture2D>("res://assets/textures/ice/snow_normal.png"));
		_packed.SetShaderParameter("noise_tex", new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true, Noise = new FastNoiseLite { Frequency = 0.02f, FractalOctaves = 3, Seed = 2424 } });
		return _packed;
	}
	private static ShaderMaterial _packed;

	/// <summary>An icicle a metre long, its top at the origin, hanging down: a few cones fused (thick at the root, a drip at its point).</summary>
	public static Mesh IcicleMesh()
	{
		if (_icicle != null) return _icicle;
		var k = new MeshKit();
		k.Mat(WinterWoods.IceMat);
		k.Color = Colors.White;
		k.Cylinder(Vector3.Zero, new Vector3(0.01f, -0.35f, 0f), 0.06f, 0.035f, 6, false);
		k.Cylinder(new Vector3(0.01f, -0.35f, 0f), new Vector3(-0.005f, -1f, 0.01f), 0.035f, 0.002f, 6, false);
		return _icicle = k.Commit();
	}

	/// <summary>The sky at the top of a hole through the roof: a cold, pale, flat light (dim: never a glare).</summary>
	public static StandardMaterial3D SkyHole() => _sky ??= new StandardMaterial3D
	{
		ResourceName = "snow_cave_sky", AlbedoColor = new Color(0.5f, 0.6f, 0.74f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
	};
}
