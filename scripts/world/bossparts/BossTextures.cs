using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.World.BossParts;

/// <summary>
/// Procedural textures for Act 18's boss room, after the owner's references: galvanized steel
/// grating for the catwalk, pipe insulation (canvas-wrapped with banding, crinkled foil), painted and
/// black steel, and pressure-gauge faces.
/// </summary>
public static class BossTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, Material> _mat = new();

	private static float Hash(int x, int y, int seed)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}

	private static float Noise(float x, float y, int period, int seed)
	{
		int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
		float fx = x - x0, fy = y - y0;
		fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
		int W(int v) => ((v % period) + period) % period;
		float a = Hash(W(x0), W(y0), seed), b = Hash(W(x0 + 1), W(y0), seed);
		float c = Hash(W(x0), W(y0 + 1), seed), d = Hash(W(x0 + 1), W(y0 + 1), seed);
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
	}

	private static float Fbm(float u, float v, int w, int cells, int oct, int seed)
	{
		float s = 0, a = 0.5f, n = 0;
		for (int o = 0; o < oct; o++)
		{
			int p = cells << o;
			s += a * Noise(u / w * p, v / w * p, p, seed + o * 17);
			n += a; a *= 0.5f;
		}
		return s / n;
	}

	private static Texture2D Make(string key, int w, int h, Func<int, int, Color> f)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
		_tex[key] = t;
		return t;
	}

	private static StandardMaterial3D Std(string key, Func<StandardMaterial3D> make)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = make();
		ProcTextures.AddGrime(s as StandardMaterial3D);
		_mat[key] = s;
		return s;
	}

	/// <summary>Galvanized bar grating: bearing bars one way, twisted cross bars the other, dark gaps.</summary>
	public static StandardMaterial3D GratingMat => Std("bt_grating", () => new StandardMaterial3D
	{
		AlbedoTexture = Make("bt_grating", 64, 64, (x, y) =>
		{
			bool bear = x % 8 < 2, cross = y % 16 < 2;
			float n = Fbm(x, y, 64, 4, 3, 401);
			if (bear || cross) { float g = 0.55f + 0.2f * n + (cross ? 0.05f : 0f); return new Color(g * 0.95f, g, g * 1.02f); }
			return new Color(0.05f, 0.05f, 0.05f, 0f);   // the gaps: see-through, down to whatever is below
		}),
		Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.9f,
		Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		Roughness = 0.5f, Metallic = 0.6f, MetallicSpecular = 0.6f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	});

	/// <summary>Galvanized tube: dull silver, a little mottled.</summary>
	public static StandardMaterial3D GalvanizedMat => Std("bt_galv", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.62f, 0.64f, 0.66f), Roughness = 0.45f, Metallic = 0.7f, MetallicSpecular = 0.6f,
	});

	/// <summary>Canvas-wrapped insulation, cream to tan, a band every half metre.</summary>
	public static StandardMaterial3D WrapMat(Color tint) => Std($"bt_wrap{tint.ToHtml()}", () => new StandardMaterial3D
	{
		AlbedoTexture = Make("bt_wrap", 32, 64, (x, y) =>
		{
			float n = Fbm(x, y, 32, 4, 3, 411);
			float g = 0.85f + 0.12f * n;
			if (y % 32 < 2) g *= 0.72f;   // a band
			return new Color(g, g, g);
		}),
		AlbedoColor = tint, Roughness = 0.85f, MetallicSpecular = 0.2f, VertexColorUseAsAlbedo = true,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	});

	/// <summary>Crinkled foil insulation.</summary>
	public static StandardMaterial3D FoilMat => Std("bt_foil", () => new StandardMaterial3D
	{
		AlbedoTexture = Make("bt_foil", 64, 64, (x, y) =>
		{
			float n = Fbm(x * 2f, y, 64, 8, 3, 421);
			float g = 0.55f + 0.35f * Mathf.Abs(n - 0.5f) * 2f;
			return new Color(g, g, g * 1.03f);
		}),
		Roughness = 0.3f, Metallic = 0.85f, MetallicSpecular = 0.7f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	});

	public static StandardMaterial3D Painted(Color c, float rough = 0.5f, float metal = 0.3f) => Std($"bt_paint{c.ToHtml()}", () => new StandardMaterial3D
	{
		AlbedoColor = c, Roughness = rough, Metallic = metal, MetallicSpecular = 0.5f,
	});

	/// <summary>A pressure gauge's face: white, black ticks round three quarters of it, a red arc near
	/// the top of the scale.</summary>
	public static Texture2D GaugeFace => Make("bt_gauge", 64, 64, (x, y) =>
	{
		float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
		if (r > 31f) return new Color(0.15f, 0.15f, 0.15f);
		if (r > 29f) return new Color(0.3f, 0.3f, 0.32f);
		float a = Mathf.Atan2(dx, -dy);   // 0 at the top, clockwise
		float frac = (a + 2.36f) / 4.71f;  // the scale runs from -135 to +135 degrees
		Color c = new(0.93f, 0.92f, 0.88f);
		if (frac >= 0f && frac <= 1f)
		{
			if (r > 22f && r < 27f && Mathf.Abs(Mathf.Sin(frac * Mathf.Pi * 10f)) < 0.12f) c = new Color(0.1f, 0.1f, 0.1f);
			if (r > 24f && r < 27f && Mathf.Abs(Mathf.Sin(frac * Mathf.Pi * 50f)) < 0.2f) c = new Color(0.25f, 0.25f, 0.25f);
			if (frac > 0.72f && r > 18f && r < 21f) c = new Color(0.8f, 0.1f, 0.08f);
		}
		return c;
	});
}
