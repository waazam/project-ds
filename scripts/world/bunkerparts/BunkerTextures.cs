using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Low-res procedural textures and shared materials for the bunker (outside
/// and in): board-formed concrete, floor concrete, stains/cracks/moss decals,
/// ivy leaves, plastics and paint, and the pictures the CRTs show (the woods
/// on surveillance, the smouldering cabin, the stairs receding into fog).
/// Everything is generated from fixed seeds, linear-filtered with mipmaps
/// (the PS2 look), and cached for the lifetime of the process.
/// </summary>
public static class BunkerTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, Material> _mat = new();

	// ------------------------------------------------------------------ noise

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

	/// <summary>Tileable value noise with periods px, py (in cells).</summary>
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

	/// <summary>Tileable fbm over a w x h image (pixel coords). cells = base cells across the width.</summary>
	private static float Fbm(float u, float v, int w, int h, int cells, int oct, int seed)
	{
		float sum = 0, amp = 0.5f, norm = 0;
		for (int o = 0; o < oct; o++)
		{
			int cy = Mathf.Max(1, Mathf.RoundToInt(cells * h / (float)w));
			sum += amp * VNoise(u / w * cells, v / h * cy, cells, cy, seed + o * 17);
			norm += amp;
			amp *= 0.5f;
			cells *= 2;
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
		return Store(key, img);
	}

	private static Texture2D Store(string key, Image img)
	{
		img.GenerateMipmaps();
		var t = ImageTexture.CreateFromImage(img);
		_tex[key] = t;
		return t;
	}

	private static Color Mix(Color a, Color b, float t) => a.Lerp(b, Mathf.Clamp(t, 0f, 1f));

	/// <summary>A small software canvas for the CRT pictures and line-drawn decals.</summary>
	private sealed class Canvas
	{
		public readonly int W, H;
		public readonly Color[] Px;
		public Canvas(int w, int h, Color fill) { W = w; H = h; Px = new Color[w * h]; Array.Fill(Px, fill); }
		public Color Get(int x, int y) => Px[Mathf.Clamp(y, 0, H - 1) * W + Mathf.Clamp(x, 0, W - 1)];
		public void Set(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < W && y < H) Px[y * W + x] = c; }
		public void Blend(int x, int y, Color c, float a)
		{
			if (x < 0 || y < 0 || x >= W || y >= H) return;
			var o = Px[y * W + x];
			Px[y * W + x] = new Color(Mathf.Lerp(o.R, c.R, a), Mathf.Lerp(o.G, c.G, a), Mathf.Lerp(o.B, c.B, a), Mathf.Max(o.A, c.A * a));
		}
		public void Rect(float x0, float x1, float y0, float y1, Color c)
		{
			int ax = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1))), bx = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(x0, x1)) - 1);
			int ay = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1))), by = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(y0, y1)) - 1);
			for (int y = ay; y <= by; y++)
				for (int x = ax; x <= bx; x++)
					Px[y * W + x] = c;
		}
		public Image ToImage()
		{
			var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
			for (int y = 0; y < H; y++)
				for (int x = 0; x < W; x++)
					img.SetPixel(x, y, Px[y * W + x]);
			return img;
		}
	}

	// ------------------------------------------------------------------ surfaces

	/// <summary>Board-formed concrete: plank lines, staggered board ends, tie holes, pits. ~2 m per tile.</summary>
	public static Texture2D WallConcrete() => Make("bk_wall", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 4, 4, 301);
		float s = Hash(x, y, 302);
		float g = 0.5f + (n - 0.5f) * 0.24f + (s - 0.5f) * 0.06f;
		int row = y / 16, yy = y % 16;
		g *= 1f - 0.03f * (Hash(row, 0, 303) - 0.5f) * 4f;       // boards differ a little
		if (yy == 0) g *= 0.8f; else if (yy == 1) g *= 0.93f; else if (yy == 15) g *= 0.95f;
		int off = Mathf.FloorToInt(Hash(row, 1, 304) * 32f);
		if ((x + off) % 32 == 16 && yy >= 7 && yy <= 8) g *= 0.5f;   // tie holes
		if (s > 0.987f) g *= 0.7f;
		return new Color(g, g * 0.985f, g * 0.95f);
	});

	/// <summary>Trowelled floor concrete: aggregate speckle and long scuffs.</summary>
	public static Texture2D FloorConcrete() => Make("bk_floor", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 3, 4, 311);
		float s = Hash(x, y, 312);
		float scuff = Fbm(x * 0.35f, y * 2.2f, 64, 64, 8, 2, 313);
		float g = 0.47f + (n - 0.5f) * 0.2f;
		if (s > 0.93f) g += 0.06f; else if (s < 0.06f) g -= 0.07f;
		g *= 1f - Mathf.SmoothStep(0.6f, 0.8f, scuff) * 0.18f;
		return new Color(g, g * 0.99f, g * 0.97f);
	});

	/// <summary>Low-frequency grime/water-staining mask (greyscale).</summary>
	public static Texture2D Grime() => Make("bk_grime", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 64, 3, 4, 321);
		float v = Mathf.Clamp((n - 0.5f) * 1.8f + 0.5f, 0f, 1f);
		return new Color(v, v, v);
	});

	/// <summary>Moulded plastic: nearly flat with a soft mottling (tinted by vertex colour).</summary>
	public static Texture2D Plastic() => Make("bk_plastic", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 4, 3, 331);
		float g = 0.84f + (n - 0.5f) * 0.18f + (Hash(x, y, 332) - 0.5f) * 0.05f;
		return new Color(g, g, g);
	});

	/// <summary>Grey-green enamel with chips showing rust (shelves, cabinets, the desk).</summary>
	public static Texture2D PaintedMetal() => Make("bk_paint", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 4, 3, 341);
		float chip = Mathf.SmoothStep(0.7f, 0.76f, Fbm(x, y, 32, 32, 8, 3, 342)) * 0.7f;
		var c = Mix(new Color(0.34f, 0.37f, 0.34f), new Color(0.4f, 0.43f, 0.4f), n);
		return Mix(c, new Color(0.3f, 0.17f, 0.09f), chip);
	});

	// ------------------------------------------------------------------ decals (alpha)

	/// <summary>Water/grime streaks running down from the top edge.</summary>
	public static Texture2D Streak() => Make("bk_streak", 32, 64, (x, y) =>
	{
		float len = 0.25f + Fbm(x, 0, 32, 64, 8, 2, 351) * 0.9f;
		float cx = 1f - Mathf.Abs(x - 15.5f) / 16f;
		float v = y / 64f;
		float a = Mathf.SmoothStep(len, len - 0.2f, v) * Mathf.SmoothStep(0f, 0.5f, cx);
		a *= 0.55f + 0.45f * Fbm(x, y, 32, 64, 4, 2, 352);
		a *= 1f - v * 0.35f;
		return new Color(0.07f, 0.065f, 0.05f, Mathf.Clamp(a, 0f, 1f));
	});

	/// <summary>Rust runs (orange-brown) from a fixing point.</summary>
	public static Texture2D RustRun() => Make("bk_rust", 32, 64, (x, y) =>
	{
		float len = 0.3f + Fbm(x, 0, 32, 64, 8, 2, 361) * 0.7f;
		float cx = 1f - Mathf.Abs(x - 15.5f) / 10f;
		float v = y / 64f;
		float a = Mathf.SmoothStep(len, len - 0.25f, v) * Mathf.SmoothStep(0f, 0.6f, cx);
		a *= 0.6f + 0.4f * Fbm(x, y, 32, 64, 4, 2, 362);
		return new Color(0.3f, 0.14f, 0.06f, Mathf.Clamp(a, 0f, 1f));
	});

	/// <summary>An irregular damp/dirt blotch with a soft, ragged edge.</summary>
	public static Texture2D Blotch() => Make("bk_blotch", 64, 64, (x, y) =>
	{
		float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f;
		float r = Mathf.Sqrt(dx * dx + dy * dy);
		float edge = 0.62f + 0.3f * Fbm(x, y, 64, 64, 4, 3, 371);
		float a = Mathf.SmoothStep(edge, edge - 0.3f, r) * (0.55f + 0.45f * Fbm(x, y, 64, 64, 8, 2, 372));
		return new Color(0.06f, 0.055f, 0.045f, Mathf.Clamp(a, 0f, 1f));
	});

	/// <summary>Moss patch (green, fuzzy edge).</summary>
	public static Texture2D MossPatch() => Make("bk_moss", 64, 64, (x, y) =>
	{
		float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f;
		float r = Mathf.Sqrt(dx * dx + dy * dy);
		float n = Fbm(x, y, 64, 64, 6, 3, 381);
		float a = Mathf.SmoothStep(0.95f, 0.5f, r + (n - 0.5f) * 0.6f);
		a *= Mathf.SmoothStep(0.3f, 0.55f, Fbm(x, y, 64, 64, 12, 2, 382)) * 0.5f + 0.5f;
		var c = Mix(new Color(0.12f, 0.17f, 0.07f), new Color(0.2f, 0.26f, 0.1f), n);
		return new Color(c.R, c.G, c.B, Mathf.Clamp(a, 0f, 1f));
	});

	/// <summary>A standing-water puddle (dark; pair with <see cref="PuddleOrm"/> for the gloss).</summary>
	public static Texture2D Puddle() => Make("bk_puddle", 64, 64, (x, y) =>
	{
		float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 20f;
		float r = Mathf.Sqrt(dx * dx + dy * dy);
		float edge = 0.7f + 0.25f * Fbm(x, y, 64, 64, 4, 3, 391);
		float a = Mathf.SmoothStep(edge, edge - 0.15f, r);
		return new Color(0.03f, 0.03f, 0.028f, a * 0.75f);
	});

	/// <summary>ORM for puddles: occlusion 1, roughness very low, metal 0.</summary>
	public static Texture2D PuddleOrm() => Make("bk_puddle_orm", 64, 64, (x, y) =>
	{
		float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 20f;
		float r = Mathf.Sqrt(dx * dx + dy * dy);
		float edge = 0.7f + 0.25f * Fbm(x, y, 64, 64, 4, 3, 391);
		float a = Mathf.SmoothStep(edge, edge - 0.15f, r);
		return new Color(1f, 0.08f, 0f, a);
	});

	/// <summary>Branching hairline cracks.</summary>
	public static Texture2D Cracks()
	{
		if (_tex.TryGetValue("bk_cracks", out var t)) return t;
		var cv = new Canvas(64, 64, new Color(0.04f, 0.035f, 0.03f, 0f));
		var rng = new RandomNumberGenerator { Seed = 401 };
		void Walk(float x, float y, float ang, int steps, int depth)
		{
			for (int i = 0; i < steps; i++)
			{
				ang += rng.RandfRange(-0.45f, 0.45f);
				x += Mathf.Cos(ang); y += Mathf.Sin(ang);
				var c = new Color(0.04f, 0.035f, 0.03f, 1f);
				cv.Blend((int)x, (int)y, c, 1f);
				cv.Blend((int)x + 1, (int)y, c, 0.35f);
				if (depth < 2 && rng.Randf() < 0.07f) Walk(x, y, ang + rng.RandfRange(-1.2f, 1.2f), steps / 2, depth + 1);
			}
		}
		for (int i = 0; i < 3; i++) Walk(32, 32, rng.RandfRange(0, Mathf.Tau), 34, 0);
		return Store("bk_cracks", cv.ToImage());
	}

	/// <summary>Dark smeared handprints/drag marks for the maze's worst stretch.</summary>
	public static Texture2D Smear() => Make("bk_smear", 64, 64, (x, y) =>
	{
		float band = Mathf.Abs(Mathf.Sin(x * 0.35f + Fbm(x, y, 64, 64, 4, 2, 411) * 4f));
		float v = y / 64f;
		float a = Mathf.SmoothStep(0.55f, 0.95f, band) * Mathf.SmoothStep(0.9f, 0.4f, v) * Mathf.SmoothStep(0f, 0.2f, v);
		a *= Mathf.SmoothStep(0.05f, 0.25f, x / 64f) * Mathf.SmoothStep(0.95f, 0.75f, x / 64f);
		return new Color(0.16f, 0.03f, 0.02f, Mathf.Clamp(a * 0.9f, 0f, 1f));
	});

	/// <summary>A cluster of three ivy leaves (alpha cut-out card).</summary>
	public static Texture2D IvyLeaves() => Make("bk_ivy", 32, 32, (x, y) =>
	{
		(float cx, float cy, float s, float rot)[] leaves = { (11f, 12f, 8.5f, 0.4f), (22f, 15f, 7.5f, -0.5f), (15f, 23f, 7f, 2.6f) };
		for (int i = 0; i < leaves.Length; i++)
		{
			var (cx, cy, s, rot) = leaves[i];
			float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
			float u = (dx * Mathf.Cos(rot) - dy * Mathf.Sin(rot)) / s;
			float v = (dx * Mathf.Sin(rot) + dy * Mathf.Cos(rot)) / s;
			// Heart-ish ivy leaf: two lobes and a point.
			float r = Mathf.Sqrt(u * u + v * v);
			float ang = Mathf.Atan2(v, u);
			float lobe = 0.75f + 0.25f * Mathf.Cos(ang * 3f);
			if (r < lobe)
			{
				float n = Fbm(x, y, 32, 32, 4, 2, 421 + i);
				var c = Mix(new Color(0.12f, 0.2f, 0.07f), new Color(0.2f, 0.3f, 0.1f), n);
				if (Mathf.Abs(v) < 0.08f || Mathf.Abs(u) < 0.06f) c *= 0.75f;   // veins
				c *= 0.85f + 0.3f * (1f - r / lobe);
				return new Color(c.R, c.G, c.B, 1f);
			}
		}
		return new Color(0.14f, 0.22f, 0.08f, 0f);
	});

	// ------------------------------------------------------------------ CRT pictures (96x72, 4:3)

	private const int PicW = 96, PicH = 72;

	/// <summary>
	/// The stairs in the woods: a long stone flight rising straight away from the camera between
	/// dark trunks, each step greyer than the last until the top dissolves into fog.
	/// </summary>
	public static Texture2D StairsPicture()
	{
		if (_tex.TryGetValue("bk_pic_stairs", out var t)) return t;
		var cv = new Canvas(PicW, PicH, Colors.Black);
		var fog = new Color(0.6f, 0.63f, 0.64f);
		float horizon = PicH * 0.42f;
		DrawWoods(cv, 0, 0, PicW, PicH, horizon, fog, new Color(0.16f, 0.16f, 0.15f), 26, 501, clearingHalf: 14f);

		const float f = 64f, eye = 1.55f, z0 = 3.0f, run = 0.34f, rise = 0.2f, halfW = 1.0f;
		const int steps = 64;
		float Sx(float x, float z) => PicW * 0.5f + f * x / z;
		float Sy(float y, float z) => horizon - f * (y - eye) / z;
		Color Fogged(Color c, float z) => Mix(c, fog, 1f - Mathf.Exp(-z * 0.075f));
		for (int i = steps - 1; i >= 0; i--)
		{
			float zf = z0 + i * run, zb = zf + run;
			float yb = i * rise, yt = (i + 1) * rise;
			float shade = 0.92f + 0.16f * Hash(i, 0, 502);
			// Cheek walls either side, a little higher than the tread.
			var cheek = Fogged(new Color(0.3f, 0.31f, 0.3f) * shade, zf);
			cv.Rect(Sx(-halfW - 0.28f, zf), Sx(-halfW, zf), Sy(yt + 0.4f, zf), Sy(yb, zf), cheek);
			cv.Rect(Sx(halfW, zf), Sx(halfW + 0.28f, zf), Sy(yt + 0.4f, zf), Sy(yb, zf), cheek);
			// Tread (seen from above while below eye height) then riser in front of it.
			var tread = Fogged(new Color(0.58f, 0.58f, 0.55f) * shade, zb);
			if (yt < eye) cv.Rect(Sx(-halfW, zb), Sx(halfW, zb), Sy(yt, zb), Sy(yt, zf), tread);
			var riser = Fogged(new Color(0.33f, 0.34f, 0.33f) * shade, zf);
			cv.Rect(Sx(-halfW, zf), Sx(halfW, zf), Sy(yt, zf), Sy(yb, zf), riser);
			// Nosing highlight.
			var nose = Fogged(new Color(0.7f, 0.7f, 0.67f), zf);
			float ny = Sy(yt, zf);
			cv.Rect(Sx(-halfW, zf), Sx(halfW, zf), ny, ny + Mathf.Max(0.6f, 0.03f * f / zf), nose);
		}
		Grain(cv, 503, 0.05f);
		return Store("bk_pic_stairs", cv.ToImage());
	}

	/// <summary>
	/// The cabin, at night, smouldering: flames licking the roof and pouring from the windows,
	/// smoke billowing above. Alpha carries the flame mask (the shader flickers it).
	/// </summary>
	public static Texture2D CabinPicture()
	{
		if (_tex.TryGetValue("bk_pic_cabin", out var t)) return t;
		var cv = new Canvas(PicW, PicH, new Color(0, 0, 0, 0));
		float horizon = 48f;
		for (int y = 0; y < PicH; y++)
			for (int x = 0; x < PicW; x++)
			{
				float glow = Mathf.Exp(-(Mathf.Pow((x - 48f) / 30f, 2f) + Mathf.Pow((y - 34f) / 22f, 2f)));
				Color c = y < horizon
					? Mix(new Color(0.02f, 0.025f, 0.04f), new Color(0.08f, 0.06f, 0.06f), y / horizon)
					: Mix(new Color(0.06f, 0.045f, 0.03f), new Color(0.02f, 0.018f, 0.015f), (y - horizon) / (PicH - horizon));
				c += new Color(0.42f, 0.16f, 0.04f) * glow * 0.8f;
				cv.Set(x, y, new Color(c.R, c.G, c.B, 0f));
			}
		// Smoke column rising and leaning, lit orange from below.
		for (int y = 0; y < 34; y++)
			for (int x = 0; x < PicW; x++)
			{
				float cx = 48f + (34 - y) * 0.45f;
				float wdt = 9f + (34 - y) * 0.5f;
				float d = Mathf.Abs(x - cx) / wdt;
				float n = Fbm(x, y * 1.4f, PicW, PicH, 6, 3, 511);
				float a = Mathf.SmoothStep(1f, 0.3f, d + (0.5f - n) * 0.8f) * 0.85f;
				if (a <= 0f) continue;
				var sm = Mix(new Color(0.2f, 0.16f, 0.13f), new Color(0.1f, 0.09f, 0.09f), (34 - y) / 34f);
				cv.Blend(x, y, sm, a);
			}
		// Tree silhouettes behind, rim-lit.
		var rng = new RandomNumberGenerator { Seed = 512 };
		for (int i = 0; i < 16; i++)
		{
			float tx = rng.RandfRange(0, PicW), th = rng.RandfRange(14, 30), tw = rng.RandfRange(6, 11);
			if (Mathf.Abs(tx - 48f) < 20f) continue;
			for (int y = (int)(horizon - th); y < horizon + 2; y++)
			{
				float frac = (y - (horizon - th)) / th;
				float half = tw * 0.5f * frac;
				for (int x = (int)(tx - half); x <= (int)(tx + half); x++)
					cv.Set(x, y, new Color(0.015f, 0.015f, 0.02f, 0f) + new Color(0.08f, 0.03f, 0.01f, 0f) * Mathf.Exp(-Mathf.Abs(x - 48f) / 16f));
			}
		}
		// The cabin: log walls, gable roof, glowing windows, the doorway.
		float wallL = 30f, wallR = 66f, wallT = 37f, wallB = 55f, apexY = 23f;
		for (int y = (int)apexY; y < wallB; y++)
			for (int x = (int)wallL - 4; x < wallR + 4; x++)
			{
				float roofY = apexY + Mathf.Abs(x - 48f) * (wallT - apexY) / 22f;
				if (y < roofY) continue;
				if (y < wallT)
				{
					var roof = new Color(0.06f, 0.045f, 0.04f) * (0.8f + 0.4f * Hash(x, y, 513));
					cv.Set(x, y, new Color(roof.R, roof.G, roof.B, 0f));
				}
				else if (x >= wallL && x < wallR)
				{
					bool log = (y - (int)wallT) % 3 == 0;
					var wall = new Color(0.16f, 0.08f, 0.04f) * (log ? 0.6f : 1f) * (0.8f + 0.3f * Hash(x, y, 514));
					cv.Set(x, y, new Color(wall.R, wall.G, wall.B, 0f));
				}
			}
		void Window(float x0, float x1, float y0, float y1)
		{
			for (int y = (int)y0; y < y1; y++)
				for (int x = (int)x0; x < x1; x++)
				{
					float f = 0.7f + 0.3f * Hash(x, y, 515);
					cv.Set(x, y, new Color(1f * f, 0.62f * f, 0.16f * f, 0.9f));
				}
		}
		Window(35, 41, 41, 47);
		Window(55, 61, 41, 47);
		cv.Rect(45, 51, 43, 55, new Color(0.35f, 0.12f, 0.03f, 0.6f));
		// Flames: licking up from the roof line and out of the window heads.
		for (int y = 4; y < 47; y++)
			for (int x = 24; x < 72; x++)
			{
				float roofY = apexY + Mathf.Abs(x - 48f) * (wallT - apexY) / 22f;
				float above = roofY - y;
				bool winHead = (x >= 34 && x < 42 || x >= 54 && x < 62) && y >= 33 && y < 42;
				if (above < -2f && !winHead) continue;
				float n = Fbm(x * 1.6f, y * 1.1f, PicW, PicH, 8, 3, 516);
				float reach = winHead ? 0.55f : 0.9f;
				float heat = n * 1.25f - Mathf.Max(above, 0f) / (14f * reach) - (Mathf.Abs(x - 48f) > 20f ? 0.25f : 0f);
				if (winHead) heat = n * 1.2f - (41f - y) / 9f;
				if (heat < 0.32f) continue;
				float k = Mathf.Clamp((heat - 0.32f) / 0.5f, 0f, 1f);
				var fire = k < 0.5f ? Mix(new Color(0.55f, 0.08f, 0.02f), new Color(1f, 0.42f, 0.06f), k * 2f)
					: Mix(new Color(1f, 0.42f, 0.06f), new Color(1f, 0.85f, 0.45f), (k - 0.5f) * 2f);
				cv.Set(x, y, new Color(fire.R, fire.G, fire.B, 0.35f + 0.65f * k));
			}
		// Embers.
		for (int i = 0; i < 26; i++)
			cv.Set(rng.RandiRange(26, 72), rng.RandiRange(2, 26), new Color(1f, 0.55f, 0.15f, 1f));
		Grain(cv, 517, 0.04f);
		return Store("bk_pic_cabin", cv.ToImage());
	}

	/// <summary>
	/// Surveillance of the woods: a 2x2 atlas of night-camera views (a trail between trunks, the
	/// footbridge over the stream, dense trunks in fog, a trail sign at a fork), grey-green and
	/// grainy, each with a small recording mark and a time bar.
	/// </summary>
	public static Texture2D SurveillanceAtlas()
	{
		if (_tex.TryGetValue("bk_pic_surv", out var t)) return t;
		var cv = new Canvas(PicW * 2, PicH * 2, Colors.Black);
		var fog = new Color(0.44f, 0.5f, 0.45f);
		var ground = new Color(0.13f, 0.15f, 0.13f);

		// 0: a trail running away between trunks.
		DrawWoods(cv, 0, 0, PicW, PicH, 30f, fog, ground, 24, 601, clearingHalf: 10f);
		for (int y = 31; y < PicH; y++)
		{
			float k = (y - 30f) / (PicH - 30f);
			float half = 1.5f + k * 26f, cx = PicW * 0.5f + (1f - k) * 6f;
			for (int x = (int)(cx - half); x <= (int)(cx + half); x++)
				cv.Set(x, y, Mix(new Color(0.3f, 0.34f, 0.3f), fog, (1f - k) * 0.7f) * (0.85f + 0.2f * Hash(x, y, 602)));
		}
		// 1: the footbridge: deck receding, rails, dark water band under it.
		DrawWoods(cv, PicW, 0, PicW, PicH, 26f, fog, ground, 18, 611, clearingHalf: 0f);
		for (int y = 40; y < 56; y++)
			for (int x = PicW; x < PicW * 2; x++)
				cv.Set(x, y, new Color(0.08f, 0.1f, 0.09f) * (0.8f + 0.4f * Hash(x, y, 612)));
		for (int y = 30; y < PicH; y++)
		{
			float k = (y - 30f) / (PicH - 30f);
			float half = 3f + k * 22f, cx = PicW * 1.5f;
			var deck = Mix(new Color(0.34f, 0.36f, 0.32f), fog, (1f - k) * 0.6f) * (((int)(y * (1.5f + k * 2f))) % 3 == 0 ? 0.7f : 1f);
			for (int x = (int)(cx - half); x <= (int)(cx + half); x++) cv.Set(x, y, deck);
			cv.Set((int)(cx - half - 2), y - (int)(k * 10f), new Color(0.5f, 0.55f, 0.5f));
			cv.Set((int)(cx + half + 2), y - (int)(k * 10f), new Color(0.5f, 0.55f, 0.5f));
		}
		// 2: dense trunks close to the lens, fog behind.
		DrawWoods(cv, 0, PicH, PicW, PicH, 34f, fog, ground, 34, 621, clearingHalf: 0f, near: true);
		// 3: a trail sign at a fork.
		DrawWoods(cv, PicW, PicH, PicW, PicH, 32f, fog, ground, 20, 631, clearingHalf: 6f);
		cv.Rect(PicW + 44, PicW + 46, PicH + 26, PicH + 58, new Color(0.22f, 0.24f, 0.21f));
		cv.Rect(PicW + 34, PicW + 58, PicH + 26, PicH + 33, new Color(0.46f, 0.5f, 0.44f));
		cv.Rect(PicW + 38, PicW + 56, PicH + 35, PicH + 41, new Color(0.4f, 0.44f, 0.38f));

		// Overlay marks per cell: a bright recording dot top-left and a time bar bottom-right.
		for (int cell = 0; cell < 4; cell++)
		{
			int ox = (cell % 2) * PicW, oy = (cell / 2) * PicH;
			cv.Rect(ox + 5, ox + 8, oy + 5, oy + 8, new Color(0.85f, 0.9f, 0.85f));
			for (int i = 0; i < 7; i++)
				if (Hash(i, cell, 641) > 0.3f) cv.Rect(ox + 60 + i * 4, ox + 63 + i * 4, oy + 63, oy + 66, new Color(0.75f, 0.8f, 0.75f));
		}
		Grain(cv, 642, 0.07f);
		return Store("bk_pic_surv", cv.ToImage());
	}

	/// <summary>Night woods into a cv rectangle: fogged sky/ground gradient and a stand of trunks
	/// sorted far to near (darker and wider when near). clearingHalf keeps the centre open.</summary>
	private static void DrawWoods(Canvas cv, int ox, int oy, int w, int h, float horizon, Color fog, Color ground,
		int trunks, int seed, float clearingHalf, bool near = false)
	{
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				Color c = y < horizon
					? Mix(fog * 0.55f, fog, y / horizon)
					: Mix(fog * 0.8f, ground, (y - horizon) / (h - horizon));
				float n = Fbm(x, y, w, h, 6, 2, seed + 1);
				cv.Set(ox + x, oy + y, c * (0.93f + n * 0.14f));
			}
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		var list = new List<(float x, float d)>();
		for (int i = 0; i < trunks; i++)
		{
			float x = rng.RandfRange(0, w);
			if (clearingHalf > 0 && Mathf.Abs(x - w * 0.5f) < clearingHalf) x += Mathf.Sign(x - w * 0.5f + 0.01f) * clearingHalf;
			list.Add((x, rng.RandfRange(near ? 0.3f : 0.05f, 1f)));
		}
		list.Sort((a, b) => a.d.CompareTo(b.d));
		foreach (var (x, d) in list)
		{
			float wdt = Mathf.Lerp(1f, near ? 9f : 5f, d * d);
			float baseY = horizon + d * (h - horizon) * 0.75f;
			var col = Mix(fog * 0.9f, new Color(0.05f, 0.055f, 0.05f), 0.25f + d * 0.75f);
			for (int y = 0; y < baseY; y++)
				for (int xx = (int)(x - wdt * 0.5f); xx <= (int)(x + wdt * 0.5f); xx++)
					if (xx >= 0 && xx < w) cv.Set(ox + xx, oy + y, col * (0.9f + 0.2f * Hash(xx, y, seed + 2)));
		}
	}

	private static void Grain(Canvas cv, int seed, float amount)
	{
		for (int y = 0; y < cv.H; y++)
			for (int x = 0; x < cv.W; x++)
			{
				var c = cv.Get(x, y);
				float n = (Hash(x, y, seed) - 0.5f) * amount;
				cv.Set(x, y, new Color(Mathf.Clamp(c.R + n, 0, 1), Mathf.Clamp(c.G + n, 0, 1), Mathf.Clamp(c.B + n, 0, 1), c.A));
			}
	}

	// ------------------------------------------------------------------ materials

	private static Material Cached(string key, Func<Material> make)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = make();
		ProcTextures.AddGrime(m as StandardMaterial3D);
		_mat[key] = m;
		return m;
	}

	private static ShaderMaterial Concrete(string key, Texture2D tex, float tile, float ambient, float grime, float lowGrime, Color tint, float breathe = 0f)
		=> (ShaderMaterial)Cached(key, () =>
		{
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/bunker_concrete.gdshader") };
			m.SetShaderParameter("albedo_tex", tex);
			m.SetShaderParameter("grime_tex", Grime());
			m.SetShaderParameter("tile", tile);
			m.SetShaderParameter("ambient_floor", ambient);
			m.SetShaderParameter("grime_amount", grime);
			m.SetShaderParameter("low_grime", lowGrime);
			m.SetShaderParameter("tint", tint);
			m.SetShaderParameter("breathe", breathe);
			return m;
		});

	public static ShaderMaterial HallWallMat => Concrete("bk_m_hallwall", WallConcrete(), 0.5f, 0.05f, 0.5f, 0.35f, new Color(1f, 1f, 1f));
	public static ShaderMaterial HallFloorMat => Concrete("bk_m_hallfloor", FloorConcrete(), 0.45f, 0.04f, 0.4f, 0f, new Color(0.95f, 0.95f, 0.95f));
	public static ShaderMaterial RoomWallMat => Concrete("bk_m_roomwall", WallConcrete(), 0.5f, 0.06f, 0.65f, 0.45f, new Color(0.78f, 0.78f, 0.8f));
	public static ShaderMaterial RoomFloorMat => Concrete("bk_m_roomfloor", FloorConcrete(), 0.45f, 0.05f, 0.6f, 0f, new Color(0.7f, 0.7f, 0.72f));
	public static ShaderMaterial MazeWallMat => Concrete("bk_m_mazewall", WallConcrete(), 0.5f, 0.09f, 0.6f, 0.4f, new Color(0.85f, 0.83f, 0.8f), 0.035f);
	public static ShaderMaterial MazeFloorMat => Concrete("bk_m_mazefloor", FloorConcrete(), 0.45f, 0.08f, 0.55f, 0f, new Color(0.8f, 0.79f, 0.77f));
	public static ShaderMaterial ExteriorConcreteMat => Concrete("bk_m_ext", WallConcrete(), 0.5f, 0f, 0.75f, 0.55f, new Color(0.6f, 0.61f, 0.57f));

	/// <summary>The earth mound: forest-floor textures in world space (matches the terrain around it).</summary>
	public static ShaderMaterial EarthMat => (ShaderMaterial)Cached("bk_m_earth", () =>
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/bunker_earth.gdshader") };
		m.SetShaderParameter("litter_tex", ProcTextures.LeafLitter());
		m.SetShaderParameter("dirt_tex", ProcTextures.Dirt());
		m.SetShaderParameter("moss_tex", ProcTextures.Moss());
		m.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		return m;
	});

	private static StandardMaterial3D Std(string key, Texture2D tex, float rough, float spec)
		=> (StandardMaterial3D)Cached(key, () => new StandardMaterial3D
		{
			AlbedoTexture = tex,
			VertexColorUseAsAlbedo = true,
			Roughness = rough,
			MetallicSpecular = spec,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		});

	public static StandardMaterial3D PlasticMat => Std("bk_m_plastic", Plastic(), 0.55f, 0.45f);
	public static StandardMaterial3D PaintedMetalMat => Std("bk_m_paint", PaintedMetal(), 0.75f, 0.35f);
	public static StandardMaterial3D BarkMat => Std("bk_m_bark", ProcTextures.Bark(), 1f, 0.15f);

	public static StandardMaterial3D RubberMat => (StandardMaterial3D)Cached("bk_m_rubber", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.035f, 0.035f, 0.035f), Roughness = 0.55f, MetallicSpecular = 0.45f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Unlit near-black, for openings that should read as depth (the vestibule's far end).</summary>
	public static StandardMaterial3D VoidMat => (StandardMaterial3D)Cached("bk_m_void", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.005f, 0.005f, 0.006f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
	});

	/// <summary>Ivy leaf cards: the foliage shader (alpha-scissor, a hint of sway).</summary>
	public static ShaderMaterial IvyMat => (ShaderMaterial)Cached("bk_m_ivy", () =>
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
		m.SetShaderParameter("albedo_tex", IvyLeaves());
		m.SetShaderParameter("tint", new Color(1f, 1f, 1f));
		m.SetShaderParameter("sway", 0.008f);
		m.SetShaderParameter("sway_speed", 0.6f);
		m.SetShaderParameter("normal_up", 0.2f);
		m.SetShaderParameter("alpha_cut", 0.5f);
		m.SetShaderParameter("tex_size", new Vector2(32, 32));
		m.SetShaderParameter("back_shade", 0.3f);
		return m;
	});

	/// <summary>A fresh CRT tube material (each caller owns its own: parameters animate per group).</summary>
	public static ShaderMaterial NewCrtMat(Texture2D picture, bool atlas, float clarity, float fire)
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/bunker_crt.gdshader") };
		m.SetShaderParameter("picture", picture);
		m.SetShaderParameter("use_atlas", atlas ? 1f : 0f);
		m.SetShaderParameter("clarity", clarity);
		m.SetShaderParameter("fire", fire);
		m.SetShaderParameter("power", 1f);
		return m;
	}

	/// <summary>A fresh emissive "glass" material for one lamp (animated individually).</summary>
	public static StandardMaterial3D NewLampGlass(Color c, float energy) => new()
	{
		AlbedoColor = c,
		EmissionEnabled = true,
		Emission = c,
		EmissionEnergyMultiplier = energy,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
	};
}
