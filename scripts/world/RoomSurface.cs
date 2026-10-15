using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// A room's own materials, weathered (2026-10-14g, the interiors kit): <see cref="Of"/> turns one of the game's
/// StandardMaterial3Ds (an ashlar, a plaster, a plank floor) into the same surface drawn by
/// <c>assets/shaders/room_surface.gdshader</c>, which lays a world-space breakup over it: broad mottling, water streaks
/// down the walls from the ceiling, a tide line of rising damp at their feet, ring-edged stains on ceilings, dirt and
/// worn paths on floors. One material, one draw, as before (the overlay isn't a second pass); a few noise samples a
/// pixel. It reads <see cref="RoomShade"/>'s bake (vertex colour, alpha mask, second UV) and falls back to plain
/// mottling where there is none. A material the shader can't stand in for (see-through, glowing, unshaded) comes back
/// as it was.
/// <para>Use: <c>k.Mat(RoomSurface.Of(ChurchTextures.PlasterMat, RoomSurface.Plaster))</c> while building, or
/// <see cref="ApplyTo"/> on a mesh already built.</para>
/// </summary>
public static class RoomSurface
{
	/// <summary>How hard a surface has weathered: the shader's knobs.</summary>
	public sealed record Weather(float Grime = 1f, float Mottle = 0.35f, float Streaks = 0.5f, float StreakLen = 1.6f, float Tide = 0.45f,
		float CeilingStains = 0.5f, float FloorDirt = 0.45f, float Wear = 0.6f, float Desaturate = 0.15f, Color? Tint = null);

	/// <summary>Limewash or plaster: streaks and tide lines show strongly.</summary>
	public static readonly Weather Plaster = new(Grime: 1f, Mottle: 0.22f, Streaks: 0.7f, Tide: 0.6f, Desaturate: 0.2f);
	/// <summary>Stone or brick: darker, damper, less streaking (the joints hide it).</summary>
	public static readonly Weather Stone = new(Grime: 0.9f, Mottle: 0.45f, Streaks: 0.4f, Tide: 0.5f, Desaturate: 0.12f);
	/// <summary>Bare or painted concrete: long streaks, strong tide.</summary>
	public static readonly Weather Concrete = new(Grime: 1.1f, Mottle: 0.45f, Streaks: 0.7f, StreakLen: 2.4f, Tide: 0.6f, Desaturate: 0.2f);
	/// <summary>Boards (floors, panelling, ceilings of planks): dirt and wear more than water.</summary>
	public static readonly Weather Wood = new(Grime: 0.85f, Mottle: 0.3f, Streaks: 0.25f, Tide: 0.25f, FloorDirt: 0.5f, Wear: 0.7f, Desaturate: 0.12f);
	/// <summary>Wallpaper: faded, streaked, its colour drained.</summary>
	public static readonly Weather Paper = new(Grime: 1f, Mottle: 0.4f, Streaks: 0.65f, Tide: 0.5f, Desaturate: 0.3f);

	private static Shader _shader;
	private static NoiseTexture2D _noise;
	private static readonly Dictionary<(Material, Weather), Material> _cache = new();

	/// <summary>The noise the weathering is drawn from (shared: one 256 px tile).</summary>
	public static NoiseTexture2D Noise => _noise ??= new NoiseTexture2D
	{
		Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true,
		Noise = new FastNoiseLite { Frequency = 0.018f, FractalOctaves = 5, FractalGain = 0.55f, Seed = 1414 },
	};

	/// <summary><paramref name="src"/> weathered by <paramref name="w"/> (cached: the same pair gives the same
	/// material). <paramref name="seed"/> shifts the pattern (two rooms of one material needn't stain alike).</summary>
	public static Material Of(Material src, Weather w = null, int seed = 0)
	{
		w ??= Plaster;
		if (src is not StandardMaterial3D sm || !Fits(sm)) return src;
		var key = (src, seed == 0 ? w : w with { Grime = w.Grime + seed * 1e-6f });
		if (_cache.TryGetValue(key, out var have)) return have;
		_shader ??= GD.Load<Shader>("res://assets/shaders/room_surface.gdshader");
		// the grain the sweep would have given it, first (it never sweeps a shader material)
		if (!sm.HasMeta("detail_kind")) DetailKit.ApplyByKey(sm, sm.ResourceName);
		var m = new ShaderMaterial { Shader = _shader, ResourceName = (sm.ResourceName ?? "m") + "_weathered" };
		m.SetShaderParameter("albedo", sm.AlbedoColor);
		m.SetShaderParameter("use_albedo_tex", sm.AlbedoTexture != null);
		if (sm.AlbedoTexture != null) m.SetShaderParameter("albedo_tex", sm.AlbedoTexture);
		// (vertex colour always: the bake is in it; white where there's none)
		m.SetShaderParameter("use_vertex_color", true);
		m.SetShaderParameter("uv1_scale", sm.Uv1Scale);
		m.SetShaderParameter("uv1_offset", sm.Uv1Offset);
		m.SetShaderParameter("projection", sm.Uv1Triplanar ? (sm.Uv1WorldTriplanar ? 2 : 1) : 0);
		m.SetShaderParameter("tri_sharpness", sm.Uv1TriplanarSharpness);
		bool nrm = sm.NormalEnabled && sm.NormalTexture != null;
		m.SetShaderParameter("use_normal", nrm);
		if (nrm) m.SetShaderParameter("normal_tex", sm.NormalTexture);
		m.SetShaderParameter("normal_scale", sm.NormalScale);
		m.SetShaderParameter("roughness", sm.Roughness);
		m.SetShaderParameter("use_roughness_tex", sm.RoughnessTexture != null);
		if (sm.RoughnessTexture != null) m.SetShaderParameter("roughness_tex", sm.RoughnessTexture);
		m.SetShaderParameter("metallic", sm.Metallic);
		m.SetShaderParameter("specular", sm.MetallicSpecular);
		bool detail = sm.DetailEnabled && sm.DetailAlbedo != null && sm.DetailUVLayer == BaseMaterial3D.DetailUV.UV1;
		m.SetShaderParameter("use_detail", detail);
		if (detail) m.SetShaderParameter("detail_tex", sm.DetailAlbedo);
		Set(m, w);
		m.SetShaderParameter("seed", (float)seed);
		m.SetMeta("detail_kind", -1);
		_cache[key] = m;
		return m;
	}

	/// <summary>Sets a weathered material's knobs (to tune one in place).</summary>
	public static void Set(ShaderMaterial m, Weather w)
	{
		m.SetShaderParameter("grime_noise", Noise);
		m.SetShaderParameter("grime", w.Grime);
		m.SetShaderParameter("mottle", w.Mottle);
		m.SetShaderParameter("streaks", w.Streaks);
		m.SetShaderParameter("streak_len", w.StreakLen);
		m.SetShaderParameter("tide", w.Tide);
		m.SetShaderParameter("ceiling_stains", w.CeilingStains);
		m.SetShaderParameter("floor_dirt", w.FloorDirt);
		m.SetShaderParameter("wear", w.Wear);
		m.SetShaderParameter("desaturate", w.Desaturate);
		if (w.Tint is { } t) m.SetShaderParameter("grime_tint", new Vector3(t.R, t.G, t.B));
	}

	/// <summary>Whether the shader can stand in for <paramref name="sm"/>: opaque, lit, no glow, no passes of its own.</summary>
	public static bool Fits(StandardMaterial3D sm) =>
		sm.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && !sm.EmissionEnabled && sm.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded
		&& sm.NextPass == null && !sm.RefractionEnabled && !sm.SubsurfScatterEnabled && sm.CullMode == BaseMaterial3D.CullModeEnum.Back
		&& !sm.HeightmapEnabled;

	/// <summary>Weathers a built mesh's surfaces in place (as surface overrides): those <paramref name="which"/> accepts
	/// (all that fit if null).</summary>
	public static void ApplyTo(MeshInstance3D mi, Weather w = null, System.Func<Material, bool> which = null, int seed = 0)
	{
		if (mi?.Mesh == null) return;
		for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
		{
			var src = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
			if (src == null || (which != null && !which(src))) continue;
			var m = Of(src, w, seed);
			if (m != src) mi.SetSurfaceOverrideMaterial(s, m);
		}
	}
}
