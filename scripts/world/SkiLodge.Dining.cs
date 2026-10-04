using System.Collections.Generic;
using Godot;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// The materials for the dining hall's and the pantry's modelled pieces (tools/Blender/dining.py; the owner,
/// 2026-10-04: the things under Act 23's sheets and on the pantry's shelves "look pretty bad now compared to all the
/// enhanced overhauls"): the long tables, the place settings left for weeks, the candelabra and the picked carcass,
/// the headless skeleton, the silver platter and its cloche, the porcelain bowl; the jars, tins, crocks and sacks.
/// </summary>
public partial class SkiLodge
{
	private static Dictionary<string, Material> _dining;
	private static Dictionary<string, Material> Dining => _dining ??= new Dictionary<string, Material>(Woodwork)
	{
		["silver"] = LodgeTextures.SilverMat, ["ceramic"] = LodgeTextures.PorcelainMat, ["linen"] = LodgeTextures.LinenMat,
		["glass"] = JarGlass, ["jarglass"] = JarGlass, ["bone"] = Remains("lodge_frozen_bone", new Color(0.74f, 0.68f, 0.55f), 0.7f, 0f), ["wax"] = Wax,
		["meat"] = Remains("lodge_frozen_meat", new Color(0.26f, 0.08f, 0.05f), 0.5f, 0.45f),
		["sinew"] = Remains("lodge_frozen_sinew", new Color(0.55f, 0.46f, 0.32f), 0.55f, 0.2f),
		["food"] = Plain("lodge_rot", new Color(0.17f, 0.065f, 0.04f), 0.35f, true),
		["mould"] = Plain("lodge_mould", new Color(0.44f, 0.47f, 0.37f), 1f, true),
		["wine"] = Plain("lodge_dried_wine", new Color(0.16f, 0.02f, 0.03f), 0.15f, false),
		["zinc"] = Plain("lodge_zinc", new Color(0.46f, 0.46f, 0.48f), 0.45f, true, 0.6f),
		["preserve"] = Plain("lodge_preserve", new Color(0.24f, 0.03f, 0.06f), 0.2f, false),
		["brine"] = Plain("lodge_brine", new Color(0.42f, 0.4f, 0.2f), 0.15f, false),
		["pickle"] = Plain("lodge_pickle", new Color(0.2f, 0.24f, 0.08f), 0.4f, true),
		["stoneware"] = Plain("lodge_stoneware", new Color(0.6f, 0.53f, 0.43f), 0.6f, true),
		["glaze"] = Plain("lodge_glaze", new Color(0.14f, 0.18f, 0.3f), 0.25f, false),
		["sack"] = new StandardMaterial3D { ResourceName = "lodge_sack", AlbedoColor = new Color(0.5f, 0.4f, 0.27f), Roughness = 1f, AlbedoTexture = LodgeTextures.LinenMat.AlbedoTexture, Uv1Scale = Vector3.One * 3f },
		["twine"] = Plain("lodge_twine", new Color(0.5f, 0.42f, 0.3f), 1f, false),
	};

	private static StandardMaterial3D _jarGlass;
	/// <summary>Old glass you can see into (a jar's fruit, a glass's dregs), greened and a little dirty.</summary>
	private static StandardMaterial3D JarGlass => _jarGlass ??= new StandardMaterial3D
	{
		ResourceName = "lodge_jar_glass", AlbedoColor = new Color(0.62f, 0.7f, 0.66f, 0.32f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		Roughness = 0.12f, MetallicSpecular = 0.8f, RimEnabled = true, Rim = 0.3f,
	};

	private static NoiseTexture2D _remainsNoise;
	/// <summary>Bone, meat or sinew left in the cold (frozen_remains.gdshader): grimed, frosted over on top, a crystal glinting.</summary>
	private static ShaderMaterial Remains(string name, Color c, float rough, float wet)
	{
		_remainsNoise ??= new NoiseTexture2D { Width = 128, Height = 128, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.06f, FractalOctaves = 3, Seed = 4242 } };
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/frozen_remains.gdshader"), ResourceName = name };
		m.SetShaderParameter("base_color", new Vector3(c.R, c.G, c.B));
		m.SetShaderParameter("noise_tex", _remainsNoise);
		m.SetShaderParameter("base_rough", rough);
		m.SetShaderParameter("wet", wet);
		return m;
	}

	private static StandardMaterial3D Plain(string name, Color c, float rough, bool grime, float metal = 0f) => new()
	{
		ResourceName = name, AlbedoColor = c, Roughness = rough, Metallic = metal, AlbedoTexture = grime ? ProcTextures.Grime() : null,
	};

	/// <summary>The pantry's shelves stocked with the modelled pieces (jars, tins, a crock, a bottle here and there), a
	/// row of sacks slumped on the floor under them. False if the pieces aren't there.</summary>
	private bool StockPantry(MeshKit k, float x0, float x1, float z, float[] shelves)
	{
		if (!FurnitureKit.Has("pantry_jars")) return false;
		var rng = new RandomNumberGenerator { Seed = 3132 };
		var face = Mathf.Pi;   // (the shelves face +z: the pieces' fronts are -z)
		foreach (float y in shelves)
		{
			float x = x0;
			while (x < x1)
			{
				// (more bottles and cans in groups, fewer jars: the owner, 2026-10-04)
				float r = rng.Randf();
				var (model, w) = r < 0.3f ? ("pantry_bottles", 0.62f) : r < 0.52f ? ("pantry_cans", 0.46f) : r < 0.66f ? ("pantry_tins", 0.66f)
					: r < 0.78f ? ("pantry_jars", 0.6f) : r < 0.84f ? ("crock", 0.2f) : r < 0.92f ? ("bottle_wine", 0.1f) : ("", 0.18f);
				if (x + w > x1) break;
				if (model != "") FurnitureKit.Add(k, model, new Vector3(x + w * 0.5f, y, z + rng.RandfRange(-0.03f, 0.03f)), face + rng.RandfRange(-0.08f, 0.08f), Dining);
				x += w + rng.RandfRange(0.02f, 0.08f);
			}
		}
		for (float x = x0 + 0.3f; x < x1; x += rng.RandfRange(0.45f, 0.8f))
			FurnitureKit.Add(k, rng.Randf() < 0.7f ? "flour_sack" : "crock", new Vector3(x, FloorY, z + 0.02f), rng.RandfRange(-0.6f, 0.6f), Dining);
		return true;
	}
}
