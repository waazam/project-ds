using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Procedural textures + materials for the stone park staircase: poured
/// concrete treads, coursed block cheek walls, a moss decal and a dead-leaf
/// atlas. Low-res (32-64 px), linear + mipmaps, tileable, fixed seeds.
/// Materials use vertex colour as albedo so the builder can paint damp edges,
/// moss tint and wear per vertex.
/// </summary>
public static class StairTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, StandardMaterial3D> _mat = new();

	// ---------- noise ----------

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

	private static float VNoise(float x, float y, int px, int py, int seed)
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

	/// <summary>Tileable fbm; u,v in pixels of a w x h image; cells = cells across the width at the first octave.</summary>
	private static float Fbm(float u, float v, int w, int h, int cells, int oct, int seed)
	{
		float sum = 0, amp = 0.5f, norm = 0;
		for (int o = 0; o < oct; o++)
		{
			int cy = Mathf.Max(1, Mathf.RoundToInt(cells * h / (float)w));
			sum += amp * VNoise(u / w * cells, v / h * cy, cells, cy, seed + o * 31);
			norm += amp; amp *= 0.5f; cells *= 2;
		}
		return sum / norm;
	}

	private static Texture2D Make(string key, int w, int h, Func<int, int, Color> f, bool alpha = false)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		if (alpha) BleedAlpha(img);
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
		_tex[key] = t;
		return t;
	}

	private static void BleedAlpha(Image img)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		for (int pass = 0; pass < 4; pass++)
		{
			var copy = (Image)img.Duplicate();
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					if (copy.GetPixel(x, y).A > 0.01f) continue;
					Color acc = new(0, 0, 0, 0); int n = 0;
					for (int dy = -1; dy <= 1; dy++)
						for (int dx = -1; dx <= 1; dx++)
						{
							int xx = x + dx, yy = y + dy;
							if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
							var cc = copy.GetPixel(xx, yy);
							if (cc.A > 0.01f || cc.R + cc.G + cc.B > 0.001f) { acc += cc; n++; }
						}
					if (n > 0) img.SetPixel(x, y, new Color(acc.R / n, acc.G / n, acc.B / n, 0));
				}
		}
	}

	// ---------- textures ----------

	/// <summary>Weathered poured concrete: mottled, aggregate speckle, pits, a few hairline cracks. 64 px ≈ 1 m.</summary>
	public static Texture2D Concrete() => Make("stair_concrete", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 4, 4, 911);
		float blot = Fbm(x, y, 64, 64, 2, 2, 915);
		float s = Hash(x, y, 917);
		float g = 0.60f + (n - 0.5f) * 0.26f + (blot - 0.5f) * 0.14f;
		if (s > 0.94f) g += 0.07f;            // light aggregate
		else if (s < 0.05f) g -= 0.10f;       // pits
		// hairline cracks: thin valleys of a warped noise
		float cn = Fbm(x + n * 9f, y, 64, 64, 3, 2, 921);
		float crack = (1f - Mathf.Clamp(Mathf.Abs(cn - 0.5f) * 70f, 0f, 1f)) * (blot > 0.52f ? 1f : 0f);
		g -= crack * 0.12f;
		// faint lichen freckles
		float lich = Fbm(x, y, 64, 64, 8, 2, 931);
		var c = new Color(g, g * 0.985f, g * 0.95f);
		if (lich > 0.68f) c = c.Lerp(new Color(0.60f, 0.62f, 0.50f), (lich - 0.68f) * 2.2f);
		return c;
	});

	/// <summary>
	/// Coursed ashlar for the cheek walls. The 64x48 tile maps to 1 m x 1 m: three courses of ~0.33 m,
	/// blocks of irregular length (0.35-0.7 m), weathered arrises, thin dark joints.
	/// </summary>
	public static Texture2D Blocks() => Make("stair_blocks", 64, 48, (x, y) =>
	{
		int course = y / 16, ly = y % 16;
		// joint positions for this course (wrapping at 64 px)
		var joints = new List<int>();
		int start = (int)(Hash(course, 0, 951) * 64);
		int acc = 0;
		while (acc < 64 - 18)
		{
			joints.Add((start + acc) % 64);
			acc += 22 + (int)(Hash(course, joints.Count, 953) * 22f);
		}
		int lx = 64, block = 0;
		for (int j = 0; j < joints.Count; j++)
		{
			int d = ((x - joints[j]) % 64 + 64) % 64;
			if (d < lx) { lx = d; block = j; }
		}
		bool mortar = lx == 0 || ly == 0;
		float tone = 0.9f + (Hash(block, course, 941) - 0.5f) * 0.22f;
		float n = Fbm(x, y, 64, 48, 4, 3, 947);
		float s = Hash(x, y, 949);
		float g = (0.58f + (n - 0.5f) * 0.24f) * tone;
		if (s > 0.95f) g += 0.05f; else if (s < 0.05f) g -= 0.07f;
		if (ly == 1 || lx == 1) g *= 0.88f;          // weathered arrises
		if (ly == 15) g *= 0.93f;
		if (mortar) g = 0.36f + n * 0.08f;
		return new Color(g, g * 0.985f, g * 0.94f);
	});

	/// <summary>Moss patch decal (alpha). Irregular clumps, dark olive to yellow-green.</summary>
	public static Texture2D Moss() => Make("stair_moss", 64, 64, (x, y) =>
	{
		float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f;
		float r = Mathf.Sqrt(dx * dx + dy * dy);
		float n = Fbm(x, y, 64, 64, 4, 4, 961);
		float fine = Fbm(x, y, 64, 64, 16, 2, 963);
		float m = n * 1.1f + fine * 0.35f - r * 0.95f;
		float a = m > 0.36f ? 1f : 0f;
		float tint = Mathf.Clamp((m - 0.36f) * 3f, 0, 1);
		var c = new Color(0.20f, 0.25f, 0.12f).Lerp(new Color(0.36f, 0.40f, 0.18f), tint * 0.7f + fine * 0.3f);
		return new Color(c.R, c.G, c.B, a);
	}, alpha: true);

	/// <summary>Dead-leaf atlas, 2x2 cells of 32 px: two broad leaves, a birch-ish oval and a curled one.</summary>
	public static Texture2D Leaves() => Make("stair_leaves", 64, 64, (x, y) =>
	{
		int cell = (x / 32) + (y / 32) * 2;
		float u = (x % 32 - 15.5f) / 16f, v = (y % 32 - 15.5f) / 16f;
		// rotate each cell a bit so the atlas isn't uniform
		float ang = cell * 0.9f + 0.3f;
		float ru = u * Mathf.Cos(ang) - v * Mathf.Sin(ang), rv = u * Mathf.Sin(ang) + v * Mathf.Cos(ang);
		float inside;
		switch (cell)
		{
			case 0: // lobed maple-ish
			{
				float a = Mathf.Atan2(rv, ru), r = Mathf.Sqrt(ru * ru + rv * rv);
				inside = 0.62f + 0.2f * Mathf.Cos(a * 5f) - r; break;
			}
			case 1: // oval
				inside = 1f - (ru * ru / 0.55f + rv * rv / 0.18f); break;
			case 2: // oak-ish wavy
			{
				float edge = 0.32f + 0.08f * Mathf.Sin(ru * 14f);
				inside = ru * ru < 0.7f ? (edge * (1f - ru * ru / 0.7f) - Mathf.Abs(rv)) : -1f; break;
			}
			default: // curled, narrow
				inside = 1f - (ru * ru / 0.5f + (rv - ru * ru * 0.4f) * (rv - ru * ru * 0.4f) / 0.07f); break;
		}
		if (inside <= 0f) return new Color(0.3f, 0.2f, 0.1f, 0f);
		float n = Fbm(x, y, 64, 64, 8, 2, 971 + cell);
		Color[] bases = { new(0.42f, 0.24f, 0.10f), new(0.48f, 0.36f, 0.14f), new(0.33f, 0.20f, 0.10f), new(0.26f, 0.17f, 0.09f) };
		var c = bases[cell] * (0.8f + n * 0.4f);
		if (Mathf.Abs(rv) < 0.04f && Mathf.Abs(ru) < 0.8f) c *= 0.75f; // midrib
		return new Color(c.R, c.G, c.B, 1f);
	}, alpha: true);

	// ---------- materials ----------

	private static StandardMaterial3D Mat(string key, Texture2D tex, float rough, float spec, bool cutout)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D
		{
			AlbedoTexture = tex,
			VertexColorUseAsAlbedo = true,
			Roughness = rough,
			MetallicSpecular = spec,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		if (cutout)
		{
			m.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
			m.AlphaScissorThreshold = 0.5f;
			m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		}
		_mat[key] = m;
		return m;
	}

	public static StandardMaterial3D ConcreteMat => Mat("stair_concrete", Concrete(), 0.95f, 0.3f, false);
	public static StandardMaterial3D BlockMat => Mat("stair_blocks", Blocks(), 0.97f, 0.25f, false);
	public static StandardMaterial3D MossMat => Mat("stair_moss", Moss(), 1f, 0.1f, true);
	public static StandardMaterial3D LeafMat => Mat("stair_leaves", Leaves(), 1f, 0.15f, true);
}
