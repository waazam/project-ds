using System;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The global texel density target (the owner: make the game pop). Every texture the game generates is
/// written at a small native size (16 to 128 px, its patterns laid out in whole pixels). This raises each
/// to the target density on the way out:
/// <list type="number">
/// <item>upscale to about <see cref="Target"/> px on its long side (at most <see cref="MaxScale"/> times,
/// never above <see cref="MaxSize"/>), bicubic and <b>tile-safe</b> (the edges wrap, so repeating
/// textures stay seamless);</item>
/// <item>a light unsharp mask, so each original texel keeps its edge instead of melting into the next;</item>
/// <item>real detail at the new density: fine multi-octave grain (pores, fibres, grit) added in
/// proportion to how busy the surface already is, so a plank gets grain and a flat paint stays calm;</item>
/// <item>cut-out alpha (foliage, fringes) re-thresholded softly, so the larger edges are crisp, not fuzzy.</item>
/// </list>
/// Colours are kept to the original's palette (the grain moves luminance a few percent, no hue), so every
/// surface looks as it did, only with far more to look at up close.
/// </summary>
public static class TexelBoost
{
	/// <summary>Long-side target in texels.</summary>
	public const int Target = 384;
	public const int MaxScale = 4, MaxSize = 512;
	/// <summary>Off for tests that compare exact generated pixels (none do yet).</summary>
	public static bool Enabled = true;

	/// <summary>Keys left at their native size: data textures read by position (masks, lookup maps).</summary>
	private static readonly string[] Skip = { "trailmap", "waternoise", "uv_", "grime_detail" };

	public static Image Apply(string key, Image src)
	{
		if (!Enabled || src == null) return src;
		foreach (var s in Skip) if (key.StartsWith(s)) return src;
		int w = src.GetWidth(), h = src.GetHeight();
		int scale = Mathf.Clamp(Target / Mathf.Max(w, h), 1, MaxScale);
		while (scale > 1 && Mathf.Max(w, h) * scale > MaxSize) scale--;
		if (scale <= 1) return src;
		int W = w * scale, H = h * scale;

		// the source as floats, for the filtering
		var px = new Color[w * h];
		bool cutout = false;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = src.GetPixel(x, y);
				px[y * w + x] = c;
				cutout |= c.A < 0.5f;
			}
		Color At(int x, int y) => px[((y % h + h) % h) * w + ((x % w + w) % w)];

		// 1. bicubic (Catmull-Rom), wrapping at the edges
		var up = new Color[W * H];
		for (int Y = 0; Y < H; Y++)
		{
			float fy = (Y + 0.5f) / scale - 0.5f;
			int y0 = Mathf.FloorToInt(fy);
			float ty = fy - y0;
			Span<float> wy = stackalloc float[4];
			CatRom(ty, wy);
			for (int X = 0; X < W; X++)
			{
				float fx = (X + 0.5f) / scale - 0.5f;
				int x0 = Mathf.FloorToInt(fx);
				float tx = fx - x0;
				Span<float> wx = stackalloc float[4];
				CatRom(tx, wx);
				float r = 0, g = 0, b = 0, a = 0;
				for (int j = 0; j < 4; j++)
					for (int i = 0; i < 4; i++)
					{
						var c = At(x0 - 1 + i, y0 - 1 + j);
						float k = wx[i] * wy[j];
						r += c.R * k; g += c.G * k; b += c.B * k; a += c.A * k;
					}
				up[Y * W + X] = new Color(r, g, b, a);
			}
		}

		// 2 + 3. unsharp against a small wrapped blur, then grain scaled by the local busyness
		var bytes = new byte[W * H * 4];
		uint seed = (uint)key.GetHashCode();
		Color Up(int x, int y) => up[((y % H + H) % H) * W + ((x % W + W) % W)];
		for (int Y = 0; Y < H; Y++)
			for (int X = 0; X < W; X++)
			{
				var c = up[Y * W + X];
				int s = scale;
				var blur = (Up(X - s, Y) + Up(X + s, Y) + Up(X, Y - s) + Up(X, Y + s)) * 0.25f;
				float busy = Mathf.Clamp((Mathf.Abs(Lum(c) - Lum(blur)) * 6f), 0f, 1f);
				var sharp = c + (c - blur) * 0.35f;
				// grain: three octaves at the new density, hashed (seamless by construction: it wraps on W, H)
				float n = (Hash(X, Y, seed) - 0.5f) * 0.55f + (Hash(X / 2, Y / 2, seed + 11) - 0.5f) * 0.3f + (Hash(X / 4, Y / 4, seed + 23) - 0.5f) * 0.25f;
				float amp = 0.035f + 0.055f * busy;
				float m = 1f + n * amp * 2f;
				var o = new Color(sharp.R * m, sharp.G * m, sharp.B * m, c.A);
				// 4. crisp cut-out edges
				if (cutout) o.A = Mathf.SmoothStep(0.3f, 0.7f, c.A);
				int k = (Y * W + X) * 4;
				bytes[k] = B8(o.R); bytes[k + 1] = B8(o.G); bytes[k + 2] = B8(o.B); bytes[k + 3] = B8(o.A);
			}
		return Image.CreateFromData(W, H, false, Image.Format.Rgba8, bytes);
	}

	private static void CatRom(float t, Span<float> w)
	{
		float t2 = t * t, t3 = t2 * t;
		w[0] = -0.5f * t3 + t2 - 0.5f * t;
		w[1] = 1.5f * t3 - 2.5f * t2 + 1f;
		w[2] = -1.5f * t3 + 2f * t2 + 0.5f * t;
		w[3] = 0.5f * t3 - 0.5f * t2;
	}

	private static byte B8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

	private static float Lum(Color c) => c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;

	private static float Hash(int x, int y, uint s)
	{
		uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ (s * 1442695041u);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}
}
