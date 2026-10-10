using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Low-res procedural textures and shared materials for the bunker (outside
/// and in): board-formed concrete, floor concrete, stains/cracks/moss decals,
/// ivy leaves, plastics and paint (the pictures the CRTs show are in
/// BunkerPictures.cs).
/// Everything is generated from fixed seeds, linear-filtered with mipmaps
/// (the PS2 look), and cached for the lifetime of the process.
/// </summary>
public static partial class BunkerTextures
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
		var __tg = Systems.TexGen.Start();
		if (Systems.TexCache.Load("BunkerTextures_" + key) is { } __cached) { t = ImageTexture.CreateFromImage(__cached); _tex[key] = t; return t; }
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		Systems.TexGen.Stop(__tg);
		return Store(key, img);
	}

	private static Texture2D Store(string key, Image img)
	{
		img = TexelBoost.Apply(key, img);
		img.GenerateMipmaps();
		Systems.TexCache.Save("BunkerTextures_" + key, img);
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
		/// <summary>A soft-edged line, tapering from radius r0 to r1 (a pixel of antialiasing).</summary>
		public void Line(float x0, float y0, float x1, float y1, float r0, float r1, Color c)
		{
			float r = Mathf.Max(r0, r1) + 1f;
			int ax = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - r)), bx = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + r));
			int ay = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1) - r)), by = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(y0, y1) + r));
			var a = new Vector2(x0, y0);
			var ba = new Vector2(x1 - x0, y1 - y0);
			float bb = Mathf.Max(ba.LengthSquared(), 1e-5f);
			for (int y = ay; y <= by; y++)
				for (int x = ax; x <= bx; x++)
				{
					var p = new Vector2(x + 0.5f, y + 0.5f) - a;
					float h = Mathf.Clamp(p.Dot(ba) / bb, 0f, 1f);
					float cover = Mathf.Clamp(Mathf.Lerp(r0, r1, h) - (p - ba * h).Length() + 0.5f, 0f, 1f);
					if (cover > 0f) Blend(x, y, c, cover);
				}
		}
		public void Disc(float cx, float cy, float r, Color c) => Line(cx, cy, cx, cy, r, r, c);
		/// <summary>Copies another canvas in at (ox, oy).</summary>
		public void Blit(Canvas src, int ox, int oy)
		{
			for (int y = 0; y < src.H; y++)
				for (int x = 0; x < src.W; x++)
					Set(ox + x, oy + y, src.Px[y * src.W + x]);
		}
		public Image ToImage()
		{
			var data = new byte[W * H * 4];
			for (int i = 0; i < Px.Length; i++)
			{
				var c = Px[i];
				data[i * 4] = (byte)(Mathf.Clamp(c.R, 0f, 1f) * 255f + 0.5f);
				data[i * 4 + 1] = (byte)(Mathf.Clamp(c.G, 0f, 1f) * 255f + 0.5f);
				data[i * 4 + 2] = (byte)(Mathf.Clamp(c.B, 0f, 1f) * 255f + 0.5f);
				data[i * 4 + 3] = (byte)(Mathf.Clamp(c.A, 0f, 1f) * 255f + 0.5f);
			}
			return Image.CreateFromData(W, H, false, Image.Format.Rgba8, data);
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

	/// <summary>
	/// Ivy leaves (the vine door, 2026-10-03): a 2 x 2 atlas, one leaf a cell, its stalk at the bottom of the cell
	/// and its blade above: 0 the five-lobed leaf, 1 three-lobed, 2 an old one going brown and holed, 3 the unlobed
	/// heart of a mature stem. Dark, waxy green, pale veins fanning from the stalk to each lobe's point.
	/// </summary>
	public static Texture2D IvyLeafAtlas()
	{
		if (_tex.TryGetValue("bk_pic_ivy", out var t)) return t;
		const int cell = 128;
		var cv = new Canvas(cell * 2, cell * 2, new Color(0.1f, 0.16f, 0.06f, 0f));
		// the lobes of each kind: (angle from straight up, length, width), lengths in cells
		var kinds = new (float a, float len, float w)[][]
		{
			new[] { (0f, 0.6f, 0.62f), (0.98f, 0.46f, 0.6f), (-0.98f, 0.46f, 0.6f), (1.95f, 0.3f, 0.6f), (-1.95f, 0.3f, 0.6f) },
			new[] { (0f, 0.6f, 0.75f), (1.1f, 0.44f, 0.7f), (-1.1f, 0.44f, 0.7f) },
			new[] { (0.08f, 0.55f, 0.62f), (1.0f, 0.44f, 0.6f), (-0.92f, 0.4f, 0.6f), (2.0f, 0.27f, 0.6f), (-1.9f, 0.25f, 0.6f) },
			new[] { (0f, 0.6f, 1.1f) },
		};
		for (int k = 0; k < 4; k++)
		{
			int ox = (k % 2) * cell, oy = (k / 2) * cell;
			var lobes = kinds[k];
			const float px = 0.5f, py = 0.74f;   // where the stalk meets the blade (the veins fan from here)
			for (int y = 0; y < cell; y++)
				for (int x = 0; x < cell; x++)
				{
					float u = (x + 0.5f) / cell - px, v = py - (y + 0.5f) / cell;   // v up
					float r = Mathf.Sqrt(u * u + v * v), th = Mathf.Atan2(u, v);       // 0 straight up
					// the outline: a smooth max of the lobes (pointed: each falls off linearly to its sides) and a
					// round core, notched at the bottom where the stalk comes in
					float sum = Mathf.Exp(14f * 0.27f), best = 0f;
					foreach (var (a, len, w) in lobes)
					{
						float d = Mathf.Abs(Mathf.Wrap(th - a, -Mathf.Pi, Mathf.Pi));
						float rl = len * Mathf.Pow(Mathf.Max(0f, 1f - d / w), k == 3 ? 0.6f : 0.85f);
						sum += Mathf.Exp(14f * rl);
						best = Mathf.Max(best, rl);
					}
					float R = Mathf.Log(sum) / 14f;
					R *= Mathf.SmoothStep(Mathf.Pi, Mathf.Pi - 0.5f, Mathf.Abs(th)) * 0.85f + 0.15f;
					if (k == 3) R = Mathf.Max(R, 0.42f * Mathf.SmoothStep(Mathf.Pi, Mathf.Pi - 0.7f, Mathf.Abs(th)) * (1f - 0.25f * Mathf.Abs(th) / Mathf.Pi));
					float blade = Mathf.Clamp((R - r) * cell + 0.5f, 0f, 1f);
					// the stalk, down from the blade to the cell's foot
					float sv = Mathf.Clamp(-v / 0.24f, 0f, 1f);
					float stalk = v < 0.03f && v > -0.25f ? Mathf.Clamp((Mathf.Lerp(0.012f, 0.008f, sv) - Mathf.Abs(u - 0.03f * sv * sv)) * cell + 0.5f, 0f, 1f) : 0f;
					if (blade <= 0f && stalk <= 0f) continue;
					var c = Mix(new Color(0.07f, 0.14f, 0.045f), new Color(0.15f, 0.26f, 0.08f), Fbm(x, y, cell, cell, 6, 3, 431 + k));
					c *= 0.85f + 0.3f * Mathf.Clamp(1f - r / Mathf.Max(R, 0.01f), 0f, 1f);       // a little lighter in the middle
					c = c.Lerp(new Color(0.05f, 0.1f, 0.035f), Mathf.SmoothStep(0.75f, 1f, r / Mathf.Max(R, 0.01f)) * 0.5f);   // the rim darker
					// the veins: to each lobe's point, and finer ones off them
					float vein = 0f;
					foreach (var (a, len, w) in lobes)
					{
						float d = Mathf.Abs(Mathf.Wrap(th - a, -Mathf.Pi, Mathf.Pi)) * r;
						float width = Mathf.Lerp(0.011f, 0.003f, Mathf.Clamp(r / len, 0f, 1f));
						if (r < len * 0.92f) vein = Mathf.Max(vein, Mathf.Clamp((width - d) * cell + 0.5f, 0f, 1f));
					}
					float fine = Mathf.Abs(Mathf.Sin(th * 9f + r * 30f));
					if (fine < 0.05f && r > 0.06f) vein = Mathf.Max(vein, 0.25f);
					c = c.Lerp(new Color(0.3f, 0.38f, 0.2f), vein * 0.45f);
					if (k == 2)
					{
						// going over: brown from the edges in, a hole or two eaten through
						float rot = Fbm(x, y, cell, cell, 5, 3, 441) + r / Mathf.Max(R, 0.01f) * 0.5f;
						c = c.Lerp(new Color(0.24f, 0.15f, 0.06f), Mathf.SmoothStep(0.7f, 0.95f, rot));
						if (Fbm(x, y, cell, cell, 9, 2, 442) > 0.72f) blade = 0f;
					}
					if (stalk > blade) c = new Color(0.2f, 0.2f, 0.09f);
					c *= 0.92f + 0.16f * Hash(x, y, 450 + k);
					cv.Set(ox + x, oy + y, new Color(c.R, c.G, c.B, Mathf.Max(blade, stalk)));
				}
		}
		return Store("bk_pic_ivy", cv.ToImage());
	}

	// ------------------------------------------------------------------ materials

	private static Material Cached(string key, Func<Material> make)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = make();
		ProcTextures.AddGrime(m as StandardMaterial3D, key);
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

	/// <summary>Ivy leaves: the ivy shader (folded cards out of <see cref="IvyLeafAtlas"/>, waxy, trembling).</summary>
	public static ShaderMaterial IvyMat => (ShaderMaterial)Cached("bk_m_ivy", () =>
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/ivy_leaf.gdshader") };
		m.SetShaderParameter("albedo_tex", IvyLeafAtlas());
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
