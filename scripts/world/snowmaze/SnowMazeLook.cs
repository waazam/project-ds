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
		_cave.SetShaderParameter("cell_tex", new NoiseTexture2D
		{
			Width = 256, Height = 256, Seamless = true,
			Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.035f, CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance, Seed = 7701 },
		});
		_cave.SetShaderParameter("noise_tex", new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.02f, FractalOctaves = 4, Seed = 7702 } });
		_cave.SetShaderParameter("snow_normal", GD.Load<Texture2D>("res://assets/textures/snow/snow_normal.png"));
		return _cave;
	}

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
