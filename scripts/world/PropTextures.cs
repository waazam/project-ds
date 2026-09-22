using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Low-res procedural textures for the park's wooden props (signs, posts, the
/// footbridge): dark, weathered, rain-soaked wood. Same rules as ProcTextures:
/// 32-64 px, fixed seeds, linear filtering with mipmaps, cached per process.
/// </summary>
public static class PropTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, StandardMaterial3D> _mat = new();

	private static float Hash(int x, int y, int seed)
	{
		unchecked
		{
			uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
			h = (h ^ (h >> 13)) * 1274126177u;
			h ^= h >> 16;
			return (h & 0xFFFFFF) / (float)0xFFFFFF;
		}
	}

	/// <summary>Tileable value noise with separate periods in x and y.</summary>
	private static float Noise(float x, float y, int px, int py, int seed)
	{
		int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
		float fx = x - x0, fy = y - y0;
		fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
		int WX(int v) => ((v % px) + px) % px;
		int WY(int v) => ((v % py) + py) % py;
		float a = Hash(WX(x0), WY(y0), seed), b = Hash(WX(x0 + 1), WY(y0), seed);
		float c = Hash(WX(x0), WY(y0 + 1), seed), d = Hash(WX(x0 + 1), WY(y0 + 1), seed);
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
	}

	/// <summary>Tileable fbm over a w x h image. cx, cy = cells across each axis at the first octave.</summary>
	private static float Fbm(int x, int y, int w, int h, int cx, int cy, int oct, int seed)
	{
		float sum = 0, amp = 0.5f, norm = 0;
		for (int o = 0; o < oct; o++)
		{
			sum += amp * Noise(x / (float)w * cx, y / (float)h * cy, cx, cy, seed + o * 31);
			norm += amp; amp *= 0.5f; cx *= 2; cy *= 2;
		}
		return sum / norm;
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

	private static Color Mix(Color a, Color b, float t) => a.Lerp(b, Mathf.Clamp(t, 0, 1));

	/// <summary>
	/// Weathered board, grain running along U. Streaky grey-brown with dark
	/// checks (cracks) and a soft darker band along both long edges.
	/// </summary>
	private static Color Board(int x, int y, int w, int h, int seed, Color dark, Color light)
	{
		// long streaks: few cells along the grain, many across it
		float grain = Fbm(x, y, w, h, 2, h / 2, 3, seed);
		float blotch = Fbm(x, y, w, h, 4, 2, 2, seed + 7);
		float fine = Hash(x, y, seed + 13);
		var c = Mix(dark, light, grain * 0.9f + (blotch - 0.5f) * 0.5f + (fine - 0.5f) * 0.12f);
		// checks: thin dark cracks that run with the grain for a while
		float crack = Noise(x / (float)w * 3f, y * 0.9f, 3, h, seed + 21);
		if (crack > 0.86f) c *= 0.55f;
		// weathered grey on the surface
		float grey = Fbm(x, y, w, h, 3, 3, 2, seed + 40);
		float lum = (c.R + c.G + c.B) / 3f;
		c = Mix(c, new Color(lum, lum, lum * 0.97f), 0.25f + grey * 0.35f);
		c.A = 1f;
		return c;
	}

	/// <summary>Plank face for sign boards (grain along U, one board tall).</summary>
	public static Texture2D SignPlank() => Make("p_signplank", 64, 32, (x, y) =>
	{
		var c = Board(x, y, 64, 32, 301, new Color(0.13f, 0.11f, 0.09f), new Color(0.30f, 0.26f, 0.21f));
		float edge = Mathf.Min(y, 31 - y);
		if (edge < 2) c *= 0.72f + edge * 0.1f;
		return c;
	});

	/// <summary>Posts: grain along V (vertical on a Box side face).</summary>
	public static Texture2D PostWood() => Make("p_post", 32, 64, (x, y) =>
		Board(y, x, 64, 32, 311, new Color(0.12f, 0.10f, 0.08f), new Color(0.28f, 0.24f, 0.19f)));

	/// <summary>Bridge deck / rail boards: darker, wetter, a hint of green rot.</summary>
	public static Texture2D DeckWood() => Make("p_deck", 64, 32, (x, y) =>
	{
		var c = Board(x, y, 64, 32, 321, new Color(0.12f, 0.11f, 0.09f), new Color(0.32f, 0.29f, 0.24f));
		float moss = Mathf.SmoothStep(0.58f, 0.72f, Fbm(x, y, 64, 32, 4, 2, 3, 329));
		c = Mix(c, new Color(0.10f, 0.13f, 0.07f), moss * 0.6f);
		float edge = Mathf.Min(y, 31 - y);
		if (edge < 2) c *= 0.7f + edge * 0.1f;
		return c;
	});

	/// <summary>Pale routed groove (letters/arrows): the lighter, less weathered wood under the surface.</summary>
	public static Texture2D Routed() => Make("p_routed", 16, 16, (x, y) =>
	{
		float n = Hash(x, y, 341) * 0.12f + Noise(x * 0.25f, y * 0.25f, 4, 4, 342) * 0.1f;
		return new Color(0.58f + n, 0.53f + n, 0.43f + n * 0.8f);
	});

	private static StandardMaterial3D Std(string key, Texture2D tex, float rough, float specular)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D
		{
			AlbedoTexture = tex,
			Roughness = rough,
			MetallicSpecular = specular,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			VertexColorUseAsAlbedo = true,
		};
		_mat[key] = m;
		return m;
	}

	public static StandardMaterial3D SignPlankMat => Std("p_signplank", SignPlank(), 0.9f, 0.25f);
	public static StandardMaterial3D PostMat => Std("p_post", PostWood(), 0.9f, 0.25f);
	/// <summary>Wet deck: a little glossier so it catches the grey sky.</summary>
	public static StandardMaterial3D DeckMat => Std("p_deck", DeckWood(), 0.62f, 0.38f);
	public static StandardMaterial3D WetPostMat => Std("p_wetpost", PostWood(), 0.7f, 0.33f);
	public static StandardMaterial3D RoutedMat => Std("p_routed", Routed(), 0.95f, 0.2f);

	/// <summary>Short-hair hide: near-white with soft darker flecks, so vertex colour does the markings.</summary>
	public static Texture2D Fur() => Make("p_fur", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 8, 3, 2, 351) * 0.18f + Hash(x, y, 352) * 0.1f;
		float v = 0.82f + n;
		return new Color(v, v * 0.98f, v * 0.95f);
	});

	/// <summary>Deer, frog: vertex-coloured, matte, the hide texture over it.</summary>
	public static StandardMaterial3D FurMat => Std("p_fur", Fur(), 1f, 0.15f);
	/// <summary>Frog skin, eyes and wet rock: same hide texture, glossier.</summary>
	public static StandardMaterial3D WetSkinMat => Std("p_wetskin", Fur(), 0.4f, 0.5f);

	/// <summary>Falling water: bright vertical streaks (grain along V) with gaps, alpha in the streaks.</summary>
	public static Texture2D FallStreaks() => Make("p_fall", 32, 64, (x, y) =>
	{
		float streak = Fbm(x, y, 32, 64, 12, 2, 3, 361);
		float broken = Fbm(x, y, 32, 64, 6, 6, 2, 362);
		float a = Mathf.Clamp((streak - 0.32f) * 2.2f, 0f, 1f) * (0.55f + 0.45f * broken);
		float v = 0.8f + 0.2f * streak;
		return new Color(v, v, v, Mathf.Clamp(0.35f + a * 0.65f, 0f, 1f));
	});

	/// <summary>Foam: white blotches with holes, alpha from the blotches.</summary>
	public static Texture2D Foam() => Make("p_foam", 32, 32, (x, y) =>
	{
		float f = Fbm(x, y, 32, 32, 5, 5, 3, 371);
		float a = Mathf.SmoothStep(0.38f, 0.62f, f);
		return new Color(0.95f, 0.96f, 0.95f, a);
	});

	/// <summary>A soft round puff for mist sprites.</summary>
	public static Texture2D Puff() => Make("p_puff", 16, 16, (x, y) =>
	{
		float d = new Vector2(x - 7.5f, y - 7.5f).Length() / 7.5f;
		float a = Mathf.Clamp(1f - d, 0f, 1f);
		return new Color(1f, 1f, 1f, a * a);
	});
}
