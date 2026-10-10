using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The baked high-res surface sets (tools/textures/surfaces.py, 2026-10-09: the cabin's and the lodge's wood, the
/// concrete): an albedo, a normal map and a roughness map each, 1024 px, mipmapped and compressed on import. They replace
/// the small textures that were drawn pixel by pixel in code as the level loaded (blurry, and seconds of the load).
/// </summary>
public static class SurfaceSets
{
	private static readonly Dictionary<string, Texture2D> _tex = new();

	public static Texture2D Tex(string set, string map = "albedo")
	{
		string key = $"{set}_{map}";
		if (_tex.TryGetValue(key, out var t)) return t;
		string path = $"res://assets/textures/surfaces/{key}.png";
		t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		_tex[key] = t;
		return t;
	}

	public static Texture2D Albedo(string set) => Tex(set);

	/// <summary>A set's maps put on a material: its albedo, its normal map and its roughness (from the red channel).</summary>
	public static StandardMaterial3D Apply(StandardMaterial3D m, string set, float normalScale = 1f)
	{
		if (m == null) return null;
		if (Tex(set) is { } a) m.AlbedoTexture = a;
		if (Tex(set, "normal") is { } n) { m.NormalEnabled = true; m.NormalTexture = n; m.NormalScale = normalScale; }
		if (Tex(set, "rough") is { } r) { m.RoughnessTexture = r; m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red; }
		m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		return m;
	}

	private static Shader _concrete;
	private static NoiseTexture2D _noise;

	/// <summary>Concrete that doesn't repeat (concrete_varied.gdshader): the two concrete sets blended over the wall,
	/// pours of different tones, water streaks from <paramref name="topY"/>, grime at <paramref name="floorY"/>'s foot.</summary>
	public static ShaderMaterial VariedConcrete(string name, float scale, float topY, float floorY, Color? tint = null, float streaks = 0.7f, float streakLen = 3f)
	{
		_concrete ??= GD.Load<Shader>("res://assets/shaders/concrete_varied.gdshader");
		_noise ??= new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true, Noise = new FastNoiseLite { Frequency = 0.02f, FractalOctaves = 4, Seed = 909 } };
		var m = new ShaderMaterial { Shader = _concrete, ResourceName = name };
		m.SetShaderParameter("tex_a", Tex("concrete_a"));
		m.SetShaderParameter("tex_b", Tex("concrete_b"));
		m.SetShaderParameter("nor_a", Tex("concrete_a", "normal"));
		m.SetShaderParameter("rough_a", Tex("concrete_a", "rough"));
		m.SetShaderParameter("noise_tex", _noise);
		m.SetShaderParameter("scale", scale);
		m.SetShaderParameter("top_y", topY);
		m.SetShaderParameter("floor_y", floorY);
		m.SetShaderParameter("streaks", streaks);
		m.SetShaderParameter("streak_len", streakLen);
		m.SetShaderParameter("tint", tint ?? Colors.White);
		return m;
	}
}
