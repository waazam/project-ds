using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.World.SewerParts;

/// <summary>
/// Procedural textures for Act 17's sewer, after the owner's references: small glazed brown-orange
/// bricks gone dark and wet low down (the old brick pipe and the vaulted cistern), and cast concrete
/// pipe, grey-white and water-stained. Low-res and filtered like the rest of the game.
/// </summary>
public static class SewerTextures
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

	/// <summary>Small glazed bricks, running bond, 8 x 4 per tile: brown to burnt orange, a little
	/// sheen on each, dark mortar.</summary>
	public static Texture2D BrickTex => Make("sw_sewerbrick", 128, 128, (x, y) =>
	{
		int row = y / 16;
		int bx = (x + (row % 2) * 16) % 128;
		int brick = bx / 32 + row * 7;
		bool mortar = y % 16 < 2 || bx % 32 < 2;
		float n = Fbm(x, y, 128, 8, 3, 301);
		if (mortar) return new Color(0.16f, 0.13f, 0.1f) * (0.8f + 0.3f * n);
		float tone = Hash(brick, 3, 302);
		Color c = new Color(0.46f, 0.26f, 0.13f).Lerp(new Color(0.62f, 0.36f, 0.16f), tone).Lerp(new Color(0.3f, 0.2f, 0.14f), Hash(brick, 5, 303) * 0.6f);
		float glaze = 1f - Mathf.Abs((y % 16) - 6f) / 8f;   // a highlight across each brick's face
		return c * (0.85f + 0.25f * n + 0.12f * glaze);
	});

	/// <summary>Cast concrete pipe: pale grey, water stains, dark tide marks low down.</summary>
	public static Texture2D PipeTex => Make("sw_pipe", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 4, 311);
		float g = 0.55f + 0.2f * n;
		if (Fbm(x * 2, y * 0.3f, 64, 8, 2, 312) > 0.62f) g *= 0.75f;   // streaks
		return new Color(g, g * 0.98f, g * 0.94f);
	});

	/// <summary>Brick, as a world-projected material (any size of wall or vault tiles the same).</summary>
	public static StandardMaterial3D BrickMat => Tri("sw_brick_m", BrickTex, 0.45f, 0.45f, 0.4f);
	public static StandardMaterial3D PipeMat => Tri("sw_pipe_m", PipeTex, 0.35f, 0.8f, 0.25f);

	private static StandardMaterial3D Tri(string key, Texture2D tex, float scale, float rough, float spec)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * scale,
			Roughness = rough, MetallicSpecular = spec, VertexColorUseAsAlbedo = true,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		ProcTextures.AddGrime(s as StandardMaterial3D);
		_mat[key] = s;
		return s;
	}
}
