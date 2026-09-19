using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Low-res procedural textures (32-128 px) and the shared materials built from
/// them. Everything is generated from fixed seeds, linear-filtered with
/// mipmaps for the soft, muddy PS2 look. Cached for the lifetime of the process.
/// </summary>
public static class ProcTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, Material> _mat = new();

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

	/// <summary>Tileable value noise: period in cells.</summary>
	private static float VNoise(float x, float y, int period, int seed)
	{
		int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
		float fx = x - x0, fy = y - y0;
		fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
		int W(int v) => ((v % period) + period) % period;
		float a = Hash(W(x0), W(y0), seed), b = Hash(W(x0 + 1), W(y0), seed);
		float c = Hash(W(x0), W(y0 + 1), seed), d = Hash(W(x0 + 1), W(y0 + 1), seed);
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
	}

	/// <summary>Tileable fbm over a w x h image, u,v in pixels. baseCells = cells across the width.</summary>
	private static float Fbm(float u, float v, int w, int h, int baseCells, int oct, int seed, float aspectY = 1f)
	{
		float sum = 0, amp = 0.5f, norm = 0;
		int cells = baseCells;
		for (int o = 0; o < oct; o++)
		{
			int cy = Mathf.Max(1, Mathf.RoundToInt(cells * aspectY * h / (float)w));
			float x = u / w * cells, y = v / h * cy;
			// period in both axes: use the x period for x and y period for y via separate scaling
			sum += amp * VNoise2(x, y, cells, cy, seed + o * 17);
			norm += amp;
			amp *= 0.5f;
			cells *= 2;
		}
		return sum / norm;
	}

	private static float VNoise2(float x, float y, int px, int py, int seed)
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

	private static Texture2D Make(string key, int w, int h, Func<int, int, Color> f, bool alpha = false)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		if (alpha) FixAlphaBorder(img);
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
		_tex[key] = t;
		return t;
	}

	/// <summary>Bleed colour into transparent pixels so mipmaps don't pick up black fringes.</summary>
	private static void FixAlphaBorder(Image img)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		for (int pass = 0; pass < 4; pass++)
		{
			var copy = (Image)img.Duplicate();
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					var c = copy.GetPixel(x, y);
					if (c.A > 0.01f) continue;
					Color acc = new(0, 0, 0, 0); int n = 0;
					for (int dy = -1; dy <= 1; dy++)
						for (int dx = -1; dx <= 1; dx++)
						{
							int xx = x + dx, yy = y + dy;
							if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
							var cc = copy.GetPixel(xx, yy);
							if (cc.A > 0.01f || (cc.R + cc.G + cc.B) > 0.001f) { acc += cc; n++; }
						}
					if (n > 0) img.SetPixel(x, y, new Color(acc.R / n, acc.G / n, acc.B / n, 0));
				}
		}
	}

	private static Color Mix(Color a, Color b, float t) => a.Lerp(b, Mathf.Clamp(t, 0, 1));

	// ---------- textures ----------

	public static Texture2D Bark() => Make("bark", 32, 64, (x, y) =>
	{
		// deep vertical furrows, reddish-brown plates with grey weathering
		float streak = Fbm(x, y * 0.12f, 32, 64, 8, 3, 11);
		float crack = Mathf.SmoothStep(0.52f, 0.72f, Fbm(x, y * 0.22f, 32, 64, 6, 2, 12));
		float n = Fbm(x, y, 32, 64, 8, 3, 13);
		float grey = Mathf.SmoothStep(0.45f, 0.75f, Fbm(x, y * 0.5f, 32, 64, 4, 2, 14));
		var c = Mix(new Color(0.17f, 0.10f, 0.07f), new Color(0.31f, 0.20f, 0.14f), streak * 0.9f + n * 0.3f);
		c = Mix(c, new Color(0.27f, 0.25f, 0.23f), grey * 0.55f);
		return Mix(c, new Color(0.05f, 0.035f, 0.03f), crack * 0.85f);
	});

	public static Texture2D Needles() => Make("needles", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 8, 4, 21);
		float s = Fbm(x * 2, y * 0.6f, 64, 64, 16, 2, 22);
		var c = Mix(new Color(0.035f, 0.06f, 0.05f), new Color(0.11f, 0.16f, 0.12f), n);
		return Mix(c, new Color(0.16f, 0.21f, 0.15f), Mathf.SmoothStep(0.6f, 0.85f, s) * 0.5f);
	});

	/// <summary>
	/// A fir bough seen from above, for alpha-scissor branch cards. v=1 is the
	/// trunk end, v=0 the tip; the main stem runs down the middle with side
	/// sprays angled toward the tip and a ragged needle fringe.
	/// </summary>
	public static Texture2D FirBranch() => Make("firbranch", 64, 64, (x, y) =>
	{
		float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 64f;   // v: 0 tip .. 1 trunk
		float t = 1f - v;                                     // 0 trunk .. 1 tip
		float dx = Mathf.Abs(u - 0.5f);
		// envelope: widest a little past the middle, pointed at the tip
		float env = 0.47f * Mathf.Sin(Mathf.Clamp(t * 1.05f, 0f, 1f) * Mathf.Pi * 0.92f + 0.12f) * (0.55f + 0.45f * t);
		env *= 0.8f + 0.35f * Fbm(x, y, 64, 64, 8, 2, 23);
		// side sprays: lines leaving the stem, sweeping toward the tip
		float best = 9f;
		for (int b = 0; b < 9; b++)
		{
			float t0 = 0.04f + b * 0.105f + (Hash(b, 0, 24) - 0.5f) * 0.04f;
			float ty = t0 + dx * 0.9f;
			best = Mathf.Min(best, Mathf.Abs(t - ty) * 0.8f);
		}
		float needle = Fbm(x, y, 64, 64, 16, 2, 25);
		float thick = 0.04f + 0.04f * needle;
		bool stem = dx < 0.022f && t < 0.97f;
		bool spray = best < thick && dx < env;
		bool fill = dx < env * 0.72f && needle > 0.34f;
		bool core = t < 0.09f;                                // solid dark inner foliage (used by the tier cores)
		if (!(stem || spray || fill || core)) return new Color(0, 0, 0, 0);
		if (core) return new Color(0.03f, 0.045f, 0.035f, 1);
		float lit = 0.35f + 0.65f * Mathf.Clamp(dx / Mathf.Max(env, 0.01f), 0, 1);   // tips lighter
		lit *= 0.75f + 0.5f * needle;
		var c = Mix(new Color(0.03f, 0.05f, 0.04f), new Color(0.13f, 0.18f, 0.12f), lit);
		if (stem) c = new Color(0.10f, 0.07f, 0.05f);
		return new Color(c.R, c.G, c.B, 1);
	}, alpha: true);

	public static Texture2D Leaves() => Make("leaves", 64, 64, (x, y) =>
	{
		// late-October foliage: dull green going to rust and ochre
		float n = Fbm(x, y, 64, 64, 10, 4, 31);
		float blot = Mathf.SmoothStep(0.45f, 0.65f, Fbm(x, y, 64, 64, 6, 2, 32));
		var c = Mix(new Color(0.09f, 0.10f, 0.05f), new Color(0.22f, 0.22f, 0.10f), n);
		c = Mix(c, Mix(new Color(0.36f, 0.17f, 0.06f), new Color(0.42f, 0.30f, 0.10f), Hash(x / 3, y / 3, 33)), blot * 0.75f);
		return c;
	});

	/// <summary>Fallen leaves over dark soil (tileable). Rust, ochre, brown and a few faded yellows.</summary>
	public static Texture2D LeafLitter() => Make("leaflitter", 64, 64, (x, y) =>
	{
		float soil = Fbm(x, y, 64, 64, 6, 3, 301);
		Color c = Mix(new Color(0.09f, 0.065f, 0.045f), new Color(0.17f, 0.12f, 0.08f), soil);
		Color[] pal =
		{
			new(0.36f, 0.19f, 0.08f), new(0.40f, 0.28f, 0.12f), new(0.26f, 0.15f, 0.08f),
			new(0.31f, 0.14f, 0.07f), new(0.43f, 0.34f, 0.19f), new(0.20f, 0.13f, 0.07f),
			new(0.33f, 0.23f, 0.11f), new(0.24f, 0.18f, 0.10f), new(0.29f, 0.20f, 0.10f),
			new(0.17f, 0.11f, 0.06f),
		};
		// later leaves lie on top; each pixel keeps the colour of the topmost leaf
		for (int i = 0; i < 320; i++)
		{
			float lx = Hash(i, 1, 302) * 64f, ly = Hash(i, 2, 302) * 64f;
			float ddx = x + 0.5f - lx, ddy = y + 0.5f - ly;
			ddx -= Mathf.Round(ddx / 64f) * 64f; ddy -= Mathf.Round(ddy / 64f) * 64f;   // wrap: tileable
			float a = Hash(i, 3, 302) * Mathf.Tau;
			float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
			float pu = ddx * ca + ddy * sa, pv = -ddx * sa + ddy * ca;
			float len = 2.2f + Hash(i, 4, 302) * 2.8f, wid = len * (0.45f + Hash(i, 5, 302) * 0.25f);
			float e = (pu * pu) / (len * len) + (pv * pv) / (wid * wid);
			if (e > 1f) continue;
			Color lc = pal[(int)(Hash(i, 6, 302) * pal.Length) % pal.Length];
			float shade = 0.75f + 0.35f * (1f - e);
			if (Mathf.Abs(pv) < 0.45f) shade *= 0.8f;       // midrib
			c = lc * shade;                                  // later leaves lie on top
		}
		if (Hash(x, y, 303) > 0.965f) c = c * 0.55f;          // specks / grit
		c.A = 1f;
		return c;
	});

	/// <summary>Dark forest-floor moss (tileable).</summary>
	public static Texture2D Moss() => Make("moss", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 8, 4, 311);
		float fine = Hash(x, y, 312);
		var c = Mix(new Color(0.06f, 0.09f, 0.04f), new Color(0.17f, 0.22f, 0.09f), n);
		if (fine > 0.9f) c = Mix(c, new Color(0.26f, 0.29f, 0.12f), 0.5f);
		else if (fine < 0.08f) c = c * 0.6f;
		return c;
	});

	public static Texture2D ForestFloor() => Make("floor", 64, 64, (x, y) =>
	{
		// darker needle duff with the odd leaf, for under the conifers
		float n = Fbm(x, y, 64, 64, 6, 4, 41);
		float fine = Hash(x, y, 42);
		var c = Mix(new Color(0.09f, 0.065f, 0.045f), new Color(0.20f, 0.14f, 0.09f), n);
		if (fine > 0.9f) c = Mix(c, new Color(0.36f, 0.20f, 0.09f), 0.6f); // needle litter
		else if (fine < 0.06f) c = c * 0.6f;
		return c;
	});

	public static Texture2D GrassGround() => Make("grassground", 64, 64, (x, y) =>
	{
		// autumn meadow: olive, straw and dead brown
		float n = Fbm(x, y, 64, 64, 6, 4, 51);
		float straw = Mathf.SmoothStep(0.45f, 0.7f, Fbm(x, y, 64, 64, 4, 3, 53));
		float fine = Hash(x, y, 52);
		var c = Mix(new Color(0.10f, 0.11f, 0.06f), new Color(0.22f, 0.22f, 0.11f), n);
		c = Mix(c, new Color(0.27f, 0.21f, 0.12f), straw * 0.6f);
		if (fine > 0.9f) c = Mix(c, new Color(0.38f, 0.32f, 0.18f), 0.5f);
		return c;
	});

	public static Texture2D Dirt() => Make("dirt", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 5, 4, 61);
		float peb = Hash(x / 2, y / 2, 62);
		var c = Mix(new Color(0.17f, 0.12f, 0.08f), new Color(0.31f, 0.23f, 0.15f), n);
		if (peb > 0.975f) c = Mix(c, new Color(0.34f, 0.31f, 0.27f), 0.45f);
		return c;
	});

	public static Texture2D Gravel() => Make("gravel", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 8, 3, 71);
		float s = Hash(x, y, 72);
		float g = 0.30f + n * 0.14f + (s - 0.5f) * 0.16f;
		return new Color(g * 1.02f, g, g * 0.95f);
	});

	public static Texture2D Rock() => Make("rock", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 6, 4, 81);
		float moss = Mathf.SmoothStep(0.5f, 0.66f, Fbm(x, y, 64, 64, 4, 3, 82));
		float lich = Hash(x / 2, y / 2, 83);
		var c = Mix(new Color(0.13f, 0.13f, 0.13f), new Color(0.30f, 0.29f, 0.27f), n);
		if (lich > 0.97f) c = Mix(c, new Color(0.42f, 0.42f, 0.36f), 0.5f);
		return Mix(c, new Color(0.10f, 0.14f, 0.06f), moss * 0.8f);
	});

	public static Texture2D WeatheredWood() => Make("wwood", 64, 64, (x, y) =>
	{
		float grain = Fbm(x * 0.15f, y, 64, 64, 4, 3, 91);
		float n = Fbm(x, y, 64, 64, 8, 2, 92);
		float seam = (y % 16) < 1 ? 0.55f : 1f;
		var c = Mix(new Color(0.24f, 0.21f, 0.17f), new Color(0.40f, 0.36f, 0.30f), grain * 0.8f + n * 0.3f);
		return c * seam;
	});

	public static Texture2D SignWood() => Make("swood", 64, 32, (x, y) =>
	{
		float grain = Fbm(x * 0.12f, y, 64, 32, 4, 3, 101);
		return Mix(new Color(0.22f, 0.13f, 0.08f), new Color(0.34f, 0.21f, 0.12f), grain);
	});

	public static Texture2D Paint() => Make("paint", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 4, 2, 111);
		return Mix(new Color(0.80f, 0.78f, 0.72f), new Color(0.86f, 0.84f, 0.78f), n);
	});

	public static Texture2D Tread() => Make("tread", 64, 32, (x, y) =>
	{
		float grain = Fbm(x * 0.1f, y, 64, 32, 3, 3, 121);
		float fine = Fbm(x * 0.3f, y * 2f, 64, 32, 8, 2, 122);
		float seam = (y % 11) == 0 ? 0.7f : 1f;
		return Mix(new Color(0.24f, 0.13f, 0.07f), new Color(0.40f, 0.24f, 0.12f), grain * 0.7f + fine * 0.3f) * seam;
	});

	/// <summary>Stair runner: u runs across the runner width, v along it. Faded, worn in the middle.</summary>
	public static Texture2D Carpet() => Make("carpet", 32, 32, (x, y) =>
	{
		float u = (x + 0.5f) / 32f;
		float n = Fbm(x, y, 32, 32, 8, 2, 131);
		float fine = Hash(x, y, 132);
		var baseC = new Color(0.42f, 0.37f, 0.32f);           // dull taupe
		var border = new Color(0.36f, 0.21f, 0.19f);           // faded oxblood
		float b = (u < 0.12f || u > 0.88f) ? 1f : ((u < 0.16f || u > 0.84f) ? 0.4f : 0f);
		var c = Mix(baseC, border, b);
		// small repeating diamond motif, barely there
		float dx = Mathf.Abs(u - 0.5f) * 32f, dy = Mathf.Abs((y % 16) - 8f);
		if (b < 0.1f && Mathf.Abs(dx + dy - 6f) < 0.8f) c = Mix(c, border, 0.35f);
		float wear = Mathf.Clamp(1f - Mathf.Abs(u - 0.5f) * 3.2f, 0, 1);
		c = Mix(c, new Color(0.50f, 0.46f, 0.40f), wear * 0.35f);
		return c * (0.92f + n * 0.12f + (fine - 0.5f) * 0.05f);
	});

	public static Texture2D Concrete() => Make("concrete", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 4, 3, 141);
		float s = Hash(x, y, 142);
		float g = 0.46f + n * 0.12f + (s - 0.5f) * 0.06f;
		return new Color(g, g * 0.98f, g * 0.94f);
	});

	public static Texture2D Metal() => Make("metal", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 4, 3, 151);
		float rust = Mathf.SmoothStep(0.62f, 0.75f, Fbm(x, y, 32, 32, 6, 3, 152));
		var c = Mix(new Color(0.13f, 0.18f, 0.14f), new Color(0.18f, 0.23f, 0.18f), n);
		return Mix(c, new Color(0.30f, 0.17f, 0.09f), rust * 0.7f);
	});

	public static Texture2D EndGrain() => Make("endgrain", 32, 32, (x, y) =>
	{
		float dx = x - 15.5f, dy = y - 15.5f;
		float r = Mathf.Sqrt(dx * dx + dy * dy);
		float ring = 0.5f + 0.5f * Mathf.Sin(r * 1.6f + Fbm(x, y, 32, 32, 4, 2, 161) * 4f);
		var c = Mix(new Color(0.45f, 0.36f, 0.24f), new Color(0.58f, 0.48f, 0.33f), ring);
		if (r > 14f) c = new Color(0.20f, 0.16f, 0.12f);
		return c;
	});

	public static Texture2D GrassTuft() => Make("grasstuft", 64, 64, (x, y) =>
	{
		// y=0 top, y=63 bottom. Blades are thin tapered lines rising from the bottom.
		float best = 0; float shade = 0;
		for (int b = 0; b < 14; b++)
		{
			float bx = 4 + Hash(b, 0, 171) * 56f;
			float lean = (Hash(b, 1, 171) - 0.5f) * 24f;
			float hgt = 30 + Hash(b, 2, 171) * 32f;
			float t = (63 - y) / hgt;
			if (t < 0 || t > 1) continue;
			float cx = bx + lean * t * t;
			float wdt = 2.2f * (1 - t) + 0.3f;
			float d = Mathf.Abs(x - cx);
			if (d < wdt) { best = 1; shade = Mathf.Max(shade, 0.5f + 0.5f * t); }
		}
		if (best < 0.5f) return new Color(0, 0, 0, 0);
		var c = Mix(new Color(0.11f, 0.12f, 0.06f), new Color(0.36f, 0.33f, 0.17f), shade);
		return new Color(c.R, c.G, c.B, 1);
	}, alpha: true);

	/// <summary>Single fern frond, stem along v (v=0 tip at top), leaflets out to the sides.</summary>
	public static Texture2D Fern() => Make("fern", 32, 64, (x, y) =>
	{
		float t = y / 63f;                 // 0 tip .. 1 base
		float cx = 15.5f;
		float halfW = 14f * Mathf.Sin(Mathf.Min(t, 0.92f) * Mathf.Pi) * (0.3f + 0.7f * t);
		float dx = Mathf.Abs(x - cx);
		bool stem = dx < 0.8f;
		// leaflets: diagonal bands
		float band = Mathf.PosMod(y + dx * 0.9f, 4.5f);
		bool leaf = dx < halfW && band < 3.0f && t < 0.95f;
		if (!stem && !leaf) return new Color(0, 0, 0, 0);
		float s = 0.55f + 0.45f * (1f - dx / 15f) - t * 0.25f;
		var c = Mix(new Color(0.06f, 0.08f, 0.04f), new Color(0.20f, 0.24f, 0.10f), s);
		// leaflet tips browning off
		c = Mix(c, new Color(0.30f, 0.20f, 0.08f), Mathf.SmoothStep(0.6f, 1f, dx / Mathf.Max(halfW, 1f)) * 0.5f);
		return new Color(c.R, c.G, c.B, 1);
	}, alpha: true);

	public static Texture2D TrailMap() => Make("trailmap", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 4, 3, 181);
		var c = Mix(new Color(0.55f, 0.58f, 0.45f), new Color(0.66f, 0.66f, 0.52f), n);
		// contour lines
		float h = Fbm(x, y, 64, 64, 3, 3, 182) * 10f;
		if (Mathf.Abs(h - Mathf.Round(h)) < 0.08f) c = c * 0.8f;
		// dashed trail
		float tx = 32 + Mathf.Sin(y * 0.15f) * 8f;
		if (Mathf.Abs(x - tx) < 1f && (y % 6) < 4) c = new Color(0.45f, 0.12f, 0.10f);
		if (x < 2 || y < 2 || x > 61 || y > 61) c = new Color(0.3f, 0.3f, 0.25f);
		return c;
	});

	public static Texture2D Paper() => Make("paper", 16, 16, (x, y) =>
	{
		float n = Hash(x, y, 191);
		float g = 0.72f + n * 0.06f;
		return new Color(g, g * 0.98f, g * 0.88f);
	});

	public static Texture2D WaterNoise() => Make("waternoise", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 4, 4, 201);
		return new Color(n, n, n);
	});

	// ---------- materials ----------

	public static StandardMaterial3D Std(string key, Texture2D tex, Color? tint = null, float rough = 1f,
		bool vertexColor = false, bool cullOff = false, float specular = 0.25f)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex,
			AlbedoColor = tint ?? Colors.White,
			Roughness = rough,
			MetallicSpecular = specular,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			VertexColorUseAsAlbedo = vertexColor,
		};
		if (cullOff) s.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_mat[key] = s;
		return s;
	}

	public static StandardMaterial3D Flat(string key, Color c, float rough = 1f, float specular = 0.25f)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D { AlbedoColor = c, Roughness = rough, MetallicSpecular = specular };
		_mat[key] = s;
		return s;
	}

	public static Material Cached(string key, Func<Material> make)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = make();
		_mat[key] = m;
		return m;
	}

	// Named materials used across props
	public static StandardMaterial3D BarkMat => Std("bark", Bark(), vertexColor: true);
	public static StandardMaterial3D NeedleMat => Std("needles", Needles(), vertexColor: true);
	public static StandardMaterial3D LeafMat => Std("leaves", Leaves(), vertexColor: true);
	public static StandardMaterial3D RockMat => Std("rock", Rock(), vertexColor: true);
	public static StandardMaterial3D WoodMat => Std("wwood", WeatheredWood(), vertexColor: true);
	public static StandardMaterial3D SignWoodMat => Std("swood", SignWood(), vertexColor: true);
	public static StandardMaterial3D PaintMat => Std("paint", Paint(), specular: 0.35f, rough: 0.85f);
	public static StandardMaterial3D TreadMat => Std("tread", Tread(), rough: 0.6f, specular: 0.4f);
	public static StandardMaterial3D CarpetMat => Std("carpet", Carpet());
	public static StandardMaterial3D ConcreteMat => Std("concrete", Concrete(), vertexColor: true);
	public static StandardMaterial3D MetalMat => Std("metal", Metal(), vertexColor: true, rough: 0.7f, specular: 0.4f);
	public static StandardMaterial3D EndGrainMat => Std("endgrain", EndGrain());
	public static StandardMaterial3D PaperMat => Std("paper", Paper(), vertexColor: true);
	public static StandardMaterial3D MapMat => Std("trailmap", TrailMap());

	/// <summary>Alpha-scissor fir bough cards (foliage shader, slight sway, darker undersides).</summary>
	public static ShaderMaterial FirBranchMat => (ShaderMaterial)Cached("firbranch", () =>
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
		m.SetShaderParameter("albedo_tex", FirBranch());
		m.SetShaderParameter("tint", new Color(1f, 1f, 1f));
		m.SetShaderParameter("sway", 0.06f);
		m.SetShaderParameter("sway_speed", 0.7f);
		m.SetShaderParameter("normal_up", 0.45f);
		m.SetShaderParameter("alpha_cut", 0.5f);
		m.SetShaderParameter("mip_boost", 0.45f);
		m.SetShaderParameter("back_shade", 0.35f);
		return m;
	});

	/// <summary>Dark stone with moss on its upward faces (world-normal blend). Vertex colour tints.</summary>
	public static ShaderMaterial MossRockMat => (ShaderMaterial)Cached("mossrock", () =>
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/rock_moss.gdshader") };
		m.SetShaderParameter("rock_tex", Rock());
		m.SetShaderParameter("moss_tex", Moss());
		m.SetShaderParameter("noise_tex", WaterNoise());
		return m;
	});
}
