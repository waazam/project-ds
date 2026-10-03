using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The pictures the CRTs show (the CRT room pass, 2026-10-03; the owner: "make the tvs larger and have more of them
/// ... This should allow us to see whats on the tvs better, may have to bump that resolution up a little"). They were
/// 96 x 72 and four cameras, upscaled and blurred on the way out. Now 256 x 192, drawn for it:
/// <list type="bullet">
/// <item>the surveillance: nine cameras (a 3 x 3 atlas), lit like real night-vision footage, the camera's infrared
/// lamp lighting what's near it pale and the woods behind falling away into the dark; each with its camera number and
/// the time burnt in. One watches the bunker's door, one the tunnel inside it, and on one someone is standing in the
/// trees, half behind a trunk, its eyes catching the lamp;</item>
/// <item>the cabin smouldering (the marked set) and the stairs (every set, after), at the new size.</item>
/// </list>
/// </summary>
public static partial class BunkerTextures
{
	private const int PicW = 256, PicH = 192;
	/// <summary>Cameras a side in the surveillance atlas (the CRT shader's <c>atlas_grid</c>).</summary>
	public const int SurvGrid = 3;
	/// <summary>The old pictures' design pixels (96 across) in the new ones: the cabin and the stairs are laid out in them.</summary>
	private const float PicScale = PicW / 96f;

	private static Color Grey(float l) => new(l * 0.93f, l, l * 0.94f);
	/// <summary>The camera's lamp: how bright a surface this far away (m) comes out.</summary>
	private static float Ir(float metres) => 0.06f + 0.8f * Mathf.Exp(-metres * 0.17f);

	// ------------------------------------------------------------------ the stairs

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
		FogWoods(cv, horizon, fog, new Color(0.16f, 0.16f, 0.15f), 34, 501, clearingHalf: 14f * PicScale);

		const float f = 64f * PicScale, eye = 1.55f, z0 = 3.0f, run = 0.34f, rise = 0.2f, halfW = 1.0f;
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
			cv.Rect(Sx(-halfW, zf), Sx(halfW, zf), ny, ny + Mathf.Max(0.6f * PicScale, 0.03f * f / zf), nose);
		}
		Mottle(cv, 504, 0.16f);
		Grain(cv, 503, 0.05f);
		return Store("bk_pic_stairs", cv.ToImage());
	}

	/// <summary>Woods in fog (the stairs' picture): a fogged sky and ground, and a stand of trunks sorted far to near,
	/// darker and wider when near; clearingHalf (pixels) keeps the middle open.</summary>
	private static void FogWoods(Canvas cv, float horizon, Color fog, Color ground, int trunks, int seed, float clearingHalf)
	{
		int w = cv.W, h = cv.H;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				Color c = y < horizon
					? Mix(fog * 0.55f, fog, y / horizon)
					: Mix(fog * 0.8f, ground, (y - horizon) / (h - horizon));
				float n = Fbm(x, y, w, h, 6, 3, seed + 1);
				cv.Set(x, y, c * (0.93f + n * 0.14f));
			}
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		var list = new List<(float x, float d)>();
		for (int i = 0; i < trunks; i++)
		{
			float x = rng.RandfRange(0, w);
			if (Mathf.Abs(x - w * 0.5f) < clearingHalf) x += Mathf.Sign(x - w * 0.5f + 0.01f) * clearingHalf;
			list.Add((x, rng.RandfRange(0.05f, 1f)));
		}
		list.Sort((a, b) => a.d.CompareTo(b.d));
		foreach (var (x, d) in list)
		{
			var col = Mix(fog * 0.9f, new Color(0.05f, 0.055f, 0.05f), 0.25f + d * 0.75f);
			float baseY = horizon + d * (h - horizon) * 0.75f;
			Trunk(cv, x, baseY, Mathf.Lerp(1f, 5f, d * d) * PicScale, col.Luminance * 1.25f, seed + i32(x), 0f, flat: d < 0.4f);
		}
	}

	private static int i32(float x) => (int)(x * 13f);

	// ------------------------------------------------------------------ the cabin

	/// <summary>
	/// The cabin, at night, smouldering: flames licking the roof and pouring from the windows,
	/// smoke billowing above. Alpha carries the flame mask (the shader flickers it). Laid out in the
	/// old 96 x 72 design pixels (fx, fy), drawn over the real ones.
	/// </summary>
	public static Texture2D CabinPicture()
	{
		if (_tex.TryGetValue("bk_pic_cabin", out var t)) return t;
		const float s = PicScale;
		var cv = new Canvas(PicW, PicH, new Color(0, 0, 0, 0));
		float horizon = 48f;
		for (int y = 0; y < PicH; y++)
			for (int x = 0; x < PicW; x++)
			{
				float fx = x / s, fy = y / s;
				float glow = Mathf.Exp(-(Mathf.Pow((fx - 48f) / 30f, 2f) + Mathf.Pow((fy - 34f) / 22f, 2f)));
				Color c = fy < horizon
					? Mix(new Color(0.02f, 0.025f, 0.04f), new Color(0.08f, 0.06f, 0.06f), fy / horizon)
					: Mix(new Color(0.06f, 0.045f, 0.03f), new Color(0.02f, 0.018f, 0.015f), (fy - horizon) / (72f - horizon));
				if (fy >= horizon) c *= 0.75f + 0.5f * Fbm(x, y * 2.5f, PicW, PicH, 24, 3, 518);   // the clearing's grass and litter
				c += new Color(0.42f, 0.16f, 0.04f) * glow * 0.8f;
				cv.Set(x, y, new Color(c.R, c.G, c.B, 0f));
			}
		// Smoke column rising and leaning, lit orange from below.
		for (int y = 0; y < (int)(34 * s); y++)
			for (int x = 0; x < PicW; x++)
			{
				float fx = x / s, fy = y / s;
				float cx = 48f + (34 - fy) * 0.45f;
				float wdt = 9f + (34 - fy) * 0.5f;
				float d = Mathf.Abs(fx - cx) / wdt;
				float n = Fbm(fx * s, fy * 1.4f * s, PicW, PicH, 6, 4, 511);
				float a = Mathf.SmoothStep(1f, 0.3f, d + (0.5f - n) * 0.8f) * 0.85f;
				if (a <= 0f) continue;
				var sm = Mix(new Color(0.2f, 0.16f, 0.13f), new Color(0.1f, 0.09f, 0.09f), (34 - fy) / 34f);
				cv.Blend(x, y, sm, a);
			}
		// Firs behind, rim-lit, their boughs in tiers.
		var rng = new RandomNumberGenerator { Seed = 512 };
		for (int i = 0; i < 16; i++)
		{
			float tx = rng.RandfRange(0, 96), th = rng.RandfRange(14, 30), tw = rng.RandfRange(6, 11);
			if (Mathf.Abs(tx - 48f) < 20f) continue;
			for (int y = (int)((horizon - th) * s); y < (horizon + 2) * s; y++)
			{
				float frac = (y / s - (horizon - th)) / th;
				float tier = frac * 5f - Mathf.Floor(frac * 5f);
				float half = tw * 0.5f * frac * (0.7f + 0.3f * tier) * s;
				for (int x = (int)(tx * s - half); x <= (int)(tx * s + half); x++)
					if (Hash(x, y, 519) > 0.08f || Mathf.Abs(x - tx * s) < half * 0.7f)
						cv.Set(x, y, new Color(0.015f, 0.015f, 0.02f, 0f) + new Color(0.08f, 0.03f, 0.01f, 0f) * Mathf.Exp(-Mathf.Abs(x / s - 48f) / 16f));
			}
		}
		// The cabin: log walls, gable roof, glowing windows, the doorway.
		float wallL = 30f, wallR = 66f, wallT = 37f, wallB = 55f, apexY = 23f;
		for (int y = (int)(apexY * s); y < wallB * s; y++)
			for (int x = (int)((wallL - 4) * s); x < (wallR + 4) * s; x++)
			{
				float fx = x / s, fy = y / s;
				float roofY = apexY + Mathf.Abs(fx - 48f) * (wallT - apexY) / 22f;
				if (fy < roofY) continue;
				if (fy < wallT)
				{
					// shingles, in courses
					float course = (fy - roofY) / 1.6f;
					float sh = (course - Mathf.Floor(course)) < 0.2f ? 0.6f : 1f;
					var roof = new Color(0.06f, 0.045f, 0.04f) * sh * (0.8f + 0.4f * Hash(x / 2 + (int)course * 7, y, 513));
					cv.Set(x, y, new Color(roof.R, roof.G, roof.B, 0f));
				}
				else if (fx >= wallL && fx < wallR)
				{
					// round logs, each lit along its top
					float lp = (fy - wallT) / 3f;
					float round = 0.45f + 0.55f * Mathf.Sin((lp - Mathf.Floor(lp)) * Mathf.Pi);
					var wall = new Color(0.16f, 0.08f, 0.04f) * round * (0.8f + 0.3f * Hash(x, y, 514));
					cv.Set(x, y, new Color(wall.R, wall.G, wall.B, 0f));
				}
			}
		void Window(float x0, float x1, float y0, float y1)
		{
			float mx = (x0 + x1) * 0.5f, my = (y0 + y1) * 0.5f;
			for (int y = (int)(y0 * s); y < y1 * s; y++)
				for (int x = (int)(x0 * s); x < x1 * s; x++)
				{
					float fx = x / s, fy = y / s;
					if (Mathf.Abs(fx - mx) < 0.35f || Mathf.Abs(fy - my) < 0.35f)
					{
						cv.Set(x, y, new Color(0.12f, 0.05f, 0.02f, 0.3f));   // the glazing bars, black against the fire
						continue;
					}
					float f = 0.7f + 0.3f * Hash(x / 2, y / 2, 515);
					cv.Set(x, y, new Color(1f * f, 0.62f * f, 0.16f * f, 0.9f));
				}
		}
		Window(35, 41, 41, 47);
		Window(55, 61, 41, 47);
		cv.Rect(45 * s, 51 * s, 43 * s, 55 * s, new Color(0.35f, 0.12f, 0.03f, 0.6f));
		// Flames: licking up from the roof line and out of the window heads.
		for (int y = (int)(4 * s); y < 47 * s; y++)
			for (int x = (int)(24 * s); x < 72 * s; x++)
			{
				float fx = x / s, fy = y / s;
				float roofY = apexY + Mathf.Abs(fx - 48f) * (wallT - apexY) / 22f;
				float above = roofY - fy;
				bool winHead = (fx >= 34 && fx < 42 || fx >= 54 && fx < 62) && fy >= 33 && fy < 42;
				if (above < -2f && !winHead) continue;
				float n = Fbm(fx * 1.6f * s, fy * 1.1f * s, PicW, PicH, 8, 4, 516);
				float reach = winHead ? 0.55f : 0.9f;
				float heat = n * 1.25f - Mathf.Max(above, 0f) / (14f * reach) - (Mathf.Abs(fx - 48f) > 20f ? 0.25f : 0f);
				if (winHead) heat = n * 1.2f - (41f - fy) / 9f;
				if (heat < 0.32f) continue;
				float k = Mathf.Clamp((heat - 0.32f) / 0.5f, 0f, 1f);
				var fire = k < 0.5f ? Mix(new Color(0.55f, 0.08f, 0.02f), new Color(1f, 0.42f, 0.06f), k * 2f)
					: Mix(new Color(1f, 0.42f, 0.06f), new Color(1f, 0.85f, 0.45f), (k - 0.5f) * 2f);
				cv.Set(x, y, new Color(fire.R, fire.G, fire.B, 0.35f + 0.65f * k));
			}
		// Embers.
		for (int i = 0; i < 40; i++)
			cv.Disc(rng.RandfRange(26, 72) * s, rng.RandfRange(2, 26) * s, rng.RandfRange(0.5f, 1.1f), new Color(1f, 0.55f, 0.15f, 1f));
		Grain(cv, 517, 0.04f);
		return Store("bk_pic_cabin", cv.ToImage());
	}

	// ------------------------------------------------------------------ the surveillance

	/// <summary>
	/// Surveillance: a 3 x 3 atlas of night-vision cameras (a trail between trunks, the footbridge over the stream,
	/// trunks close to the lens, the sign at a fork, someone in the trees, the bunker's door, the tunnel inside, the
	/// lake shore, a fallen tree in a clearing), each with its number and the time burnt in.
	/// </summary>
	public static Texture2D SurveillanceAtlas()
	{
		if (_tex.TryGetValue("bk_pic_surv", out var t)) return t;
		var feeds = new Func<int, Canvas>[]
		{
			FeedTrail, FeedBridge, FeedDense, FeedFork, FeedWatcher, FeedDoor, FeedTunnel, FeedShore, FeedFallen,
		};
		var cv = new Canvas(PicW * SurvGrid, PicH * SurvGrid, Colors.Black);
		for (int i = 0; i < feeds.Length; i++)
		{
			var feed = feeds[i](601 + i * 10);
			Finish(feed, i + 1, 690 + i);
			cv.Blit(feed, (i % SurvGrid) * PicW, (i / SurvGrid) * PicH);
		}
		return Store("bk_pic_surv", cv.ToImage());
	}

	/// <summary>The camera's lamp falling off toward the corners, the grain, and what it burns in: its number, REC,
	/// the date and time (the night of the log's last entry, a few minutes either side of 03:14).</summary>
	private static void Finish(Canvas cv, int cam, int seed)
	{
		for (int y = 0; y < cv.H; y++)
			for (int x = 0; x < cv.W; x++)
			{
				float dx = (x - cv.W * 0.5f) / (cv.W * 0.5f), dy = (y - cv.H * 0.5f) / (cv.H * 0.5f);
				float k = 1f - 0.38f * (dx * dx * 0.8f + dy * dy);
				var c = cv.Px[y * cv.W + x];
				cv.Px[y * cv.W + x] = new Color(c.R * k, c.G * k, c.B * k, c.A);
			}
		Mottle(cv, seed, 0.08f);
		Grain(cv, seed + 1, 0.08f);
		var ink = Grey(0.9f);
		Text(cv, 8, 8, $"CAM {cam:00}", 2, ink);
		cv.Disc(cv.W - 40, 12.5f, 3.4f, Grey(0.95f));
		Text(cv, cv.W - 32, 8, "REC", 2, ink);
		int min = 9 + cam, sec = (int)(Hash(cam, 1, seed) * 59f);
		Text(cv, 8, cv.H - 18, $"09/21 03:{min:00}:{sec:00}", 2, ink);
	}

	/// <summary>Night-vision ground and sky: the sky black, a faint glow of fog at the horizon, the ground lit by the
	/// camera's lamp toward the bottom of the frame, strewn with leaves and twigs.</summary>
	private static void IrBackdrop(Canvas cv, float horizon, int seed)
	{
		int w = cv.W, h = cv.H;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float l;
				if (y < horizon)
				{
					float k = y / horizon;
					l = (0.03f + 0.13f * k * k * k) * (0.8f + 0.4f * Fbm(x, y, w, h, 5, 3, seed));
				}
				else
				{
					float k = (y - horizon) / (h - horizon);
					l = 0.1f + 0.48f * Mathf.Pow(k, 1.4f);
					float litter = Fbm(x, y * (2.6f - 1.4f * k), w, h, 32, 3, seed + 3);
					l *= 0.62f + 0.6f * litter;
					// leaves: small pale and dark flecks, bigger toward the lens
					int cell = 1 + (int)(k * 3f);
					float leaf = Hash(x / cell, y / cell, seed + 4);
					if (leaf > 0.93f) l *= 1.25f;
					else if (leaf < 0.05f) l *= 0.55f;
				}
				cv.Set(x, y, Grey(l));
			}
		// a twig or two
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 5) };
		for (int i = 0; i < 26; i++)
		{
			float y = rng.RandfRange(horizon + 4f, h);
			float k = (y - horizon) / (h - horizon);
			float x = rng.RandfRange(0, w), len = 4f + 22f * k * rng.Randf(), a = rng.RandfRange(-0.5f, 0.5f);
			cv.Line(x, y, x + Mathf.Cos(a) * len, y + Mathf.Sin(a) * len * 0.4f, 0.3f + 0.7f * k, 0.25f, Grey((0.1f + 0.5f * k) * 1.2f));
		}
	}

	/// <summary>One trunk: round (lit down its middle), its bark in furrows, flaring at the root, rising off the top
	/// of the frame. <paramref name="flat"/> leaves the far ones plain (a few pixels wide: no bark to see).</summary>
	private static void Trunk(Canvas cv, float x, float baseY, float width, float lum, int seed, float lean = 0f, bool flat = false)
	{
		float half = width * 0.5f;
		for (int y = 0; y <= Mathf.Min(cv.H - 1, (int)baseY); y++)
		{
			float up = baseY - y;
			float cx = x + lean * up;
			float flare = 1f + 0.8f * Mathf.Pow(Mathf.Max(0f, 1f - up / (width * 1.1f + 1f)), 2f);
			float hw = half * flare;
			int x0 = Mathf.FloorToInt(cx - hw - 1f), x1 = Mathf.CeilToInt(cx + hw + 1f);
			for (int px = x0; px <= x1; px++)
			{
				float off = px + 0.5f - cx;
				float cover = Mathf.Clamp(hw - Mathf.Abs(off) + 0.5f, 0f, 1f);
				if (cover <= 0f) continue;
				float t = off / Mathf.Max(hw, 0.5f);
				float l = lum;
				if (!flat)
				{
					float round = 0.42f + 0.58f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
					// furrows: long and vertical, narrow across the trunk
					float furrow = VNoise(t * 4.5f + 8f, y / Mathf.Max(width * 2.6f, 3f) + 8f, 64, 4096, seed);
					float bark = 0.7f + 0.45f * Mathf.SmoothStep(0.25f, 0.6f, furrow);
					l *= round * bark * (0.9f + 0.2f * Hash(px, y, seed));
				}
				cv.Blend(px, y, Grey(l), cover);
			}
		}
	}

	/// <summary>A stand of trunks, far to near, lit by the lamp (the near pale, the far lost in the dark); the nearer
	/// ones with a dead branch or two. keepOut (x, baseY) leaves a path or a doorway clear; maxD keeps them all beyond
	/// a depth (0 the horizon, 1 the lens).</summary>
	private static void IrWoods(Canvas cv, float horizon, int trunks, int seed, float nearBias = 1f,
		Func<float, float, bool> keepOut = null, float maxD = 1f, float minD = 0f)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		var list = new List<(float x, float d)>();
		for (int i = 0; i < trunks; i++)
			list.Add((rng.RandfRange(-12f, cv.W + 12f), Mathf.Lerp(minD, maxD, Mathf.Pow(rng.Randf(), nearBias))));
		list.Sort((a, b) => a.d.CompareTo(b.d));
		foreach (var (x, d) in list)
		{
			float baseY = horizon + Mathf.Pow(d, 1.3f) * (cv.H - horizon) * 0.97f + 1f;
			if (keepOut != null && keepOut(x, baseY)) continue;
			float width = Mathf.Lerp(1.3f, 36f, d * d);
			float lum = Mathf.Lerp(0.06f, 0.82f, Mathf.Pow(d, 1.5f));
			Trunk(cv, x, baseY, width, lum, seed + i32(x), rng.RandfRange(-0.025f, 0.025f), flat: width < 3f);
			if (d < 0.3f) continue;
			int n = rng.RandiRange(1, 3);
			for (int b = 0; b < n; b++)
			{
				float by = baseY * rng.RandfRange(0.08f, 0.6f);
				float side = rng.Randf() < 0.5f ? -1f : 1f;
				float len = width * rng.RandfRange(1.2f, 3.2f);
				var tip = new Vector2(x + side * len, by - len * rng.RandfRange(0.35f, 1.0f));
				var c = Grey(lum * 0.8f);
				cv.Line(x + side * width * 0.3f, by, tip.X, tip.Y, width * 0.11f + 0.4f, 0.4f, c);
				var mid = new Vector2(Mathf.Lerp(x, tip.X, 0.55f), Mathf.Lerp(by, tip.Y, 0.55f));
				cv.Line(mid.X, mid.Y, mid.X + side * len * 0.35f, mid.Y - len * rng.RandfRange(-0.05f, 0.25f), width * 0.05f + 0.35f, 0.3f, c);
			}
		}
	}

	/// <summary>A fern: fronds arching up and out, leaflets along them.</summary>
	private static void Fern(Canvas cv, float x, float y, float size, float lum, RandomNumberGenerator rng)
	{
		int fronds = rng.RandiRange(5, 8);
		for (int i = 0; i < fronds; i++)
		{
			float a = Mathf.Lerp(-2.7f, -0.45f, (i + rng.Randf() * 0.7f) / fronds);
			float len = size * rng.RandfRange(0.6f, 1f);
			var mid = new Vector2(x + Mathf.Cos(a) * len * 0.55f, y + Mathf.Sin(a) * len * 0.55f);
			var tip = new Vector2(x + Mathf.Cos(a) * len, mid.Y + len * 0.12f);
			var c = Grey(lum * rng.RandfRange(0.8f, 1.1f));
			cv.Line(x, y, mid.X, mid.Y, size * 0.025f + 0.35f, size * 0.02f + 0.3f, c);
			cv.Line(mid.X, mid.Y, tip.X, tip.Y, size * 0.02f + 0.3f, 0.25f, c);
			for (int j = 1; j < 7; j++)
			{
				float u = j / 7f;
				var p = u < 0.55f ? new Vector2(x, y).Lerp(mid, u / 0.55f) : mid.Lerp(tip, (u - 0.55f) / 0.45f);
				float l2 = size * 0.13f * (1f - u * 0.6f);
				cv.Line(p.X, p.Y, p.X + l2 * 0.5f, p.Y - l2 * 0.55f, 0.55f, 0.25f, c);
				cv.Line(p.X, p.Y, p.X - l2 * 0.5f, p.Y - l2 * 0.35f, 0.55f, 0.25f, c);
			}
		}
	}

	/// <summary>A winding path of packed earth: k = 0 at the horizon, 1 at the bottom of the frame.</summary>
	private static void Path(Canvas cv, float horizon, Func<float, float> centre, Func<float, float> half, int seed)
	{
		for (int y = (int)horizon; y < cv.H; y++)
		{
			float k = (y - horizon) / (cv.H - horizon);
			float cx = centre(k), hw = half(k);
			for (int x = Mathf.Max(0, (int)(cx - hw - 3)); x <= Mathf.Min(cv.W - 1, (int)(cx + hw + 3)); x++)
			{
				float edge = 1f - Mathf.SmoothStep(hw * 0.75f, hw * (1.05f + 0.15f * Hash(x / 3, y, seed)), Mathf.Abs(x - cx));
				if (edge <= 0f) continue;
				float l = (0.14f + 0.6f * Mathf.Pow(k, 1.35f)) * (0.82f + 0.3f * Fbm(x, y * 3f, cv.W, cv.H, 40, 2, seed + 1));
				if (Hash(x, y, seed + 2) > 0.985f) l *= 0.6f;   // a stone, a footprint
				cv.Blend(x, y, Grey(l), edge);
			}
		}
	}

	// ---- the cameras

	/// <summary>CAM 01: a trail running away between the trunks.</summary>
	private static Canvas FeedTrail(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.42f;
		IrBackdrop(cv, hz, seed);
		float Cx(float k) => PicW * 0.52f + (1f - k) * (1f - k) * 22f + Mathf.Sin(k * 4f) * 12f * (1f - k);
		float Hw(float k) => 1.5f + Mathf.Pow(k, 1.5f) * 70f;
		Path(cv, hz, Cx, Hw, seed + 1);
		IrWoods(cv, hz, 40, seed + 2, 1.3f, (x, by) =>
		{
			float k = (by - hz) / (PicH - hz);
			return Mathf.Abs(x - Cx(k)) < Hw(k) * 1.2f + 4f;
		});
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 3) };
		for (int i = 0; i < 5; i++)
		{
			float k = rng.RandfRange(0.35f, 0.9f);
			float side = i % 2 == 0 ? -1f : 1f;
			Fern(cv, Cx(k) + side * (Hw(k) * 1.15f + 6f), hz + k * (PicH - hz), 10f + 26f * k, Ir(10f - 8f * k), rng);
		}
		return cv;
	}

	/// <summary>CAM 02: the footbridge over the stream, its deck receding to the far bank.</summary>
	private static Canvas FeedBridge(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.36f, f = PicH * 0.95f, eye = 1.7f, cx = PicW * 0.5f;
		IrBackdrop(cv, hz, seed);
		float Sx(float xw, float z) => cx + f * xw / z;
		float Sy(float yw, float z) => hz + f * (eye - yw) / z;
		const float bankZ = 15f;
		float yBank = Sy(0f, bankZ);
		float maxD = Mathf.Pow((yBank - hz) / ((PicH - hz) * 0.97f), 1f / 1.3f);
		IrWoods(cv, hz, 34, seed + 2, 1f, null, maxD);
		// the water: black, the lamp glinting off the ripples
		for (int y = (int)yBank; y < PicH; y++)
		{
			float z = f * eye / Mathf.Max(y - hz, 0.5f);
			for (int x = 0; x < PicW; x++)
			{
				float l = 0.025f + 0.04f * Fbm(x, y * 4f, PicW, PicH, 20, 2, seed + 3);
				float rip = Fbm(x * 0.4f, y * 6f, PicW, PicH, 16, 2, seed + 4);
				if (rip > 0.62f) l += (rip - 0.62f) * 2.2f * Ir(z);
				cv.Set(x, y, Grey(l));
			}
		}
		// the deck: boards across, far to near, a dark gap between each
		int i = 0;
		for (float z = bankZ; z > 1.0f; z -= 0.24f, i++)
		{
			float l = Ir(z) * (0.8f + 0.35f * Hash(i, 0, seed + 5));
			cv.Rect(Sx(-0.75f, z), Sx(0.75f, z), Sy(0f, z), Sy(0f, z - 0.2f), Grey(l));
			cv.Rect(Sx(-0.75f, z), Sx(0.75f, z), Sy(0f, z - 0.2f), Sy(0f, z - 0.24f), Grey(l * 0.25f));
		}
		// the posts and the rails along them
		for (float side = -1f; side <= 1f; side += 2f)
		{
			Vector2? last = null;
			for (float z = bankZ - 0.4f; z > 1.2f; z -= 1.3f)
			{
				float x = Sx(side * 0.82f, z), r = f * 0.045f / z;
				var top = new Vector2(x, Sy(1.05f, z));
				cv.Line(x, Sy(0f, z), top.X, top.Y, r, r, Grey(Ir(z) * 0.9f));
				if (last is { } l) cv.Line(l.X, l.Y, top.X, top.Y, f * 0.025f / (z + 0.6f), f * 0.025f / z, Grey(Ir(z) * 1.05f));
				last = top;
			}
		}
		return cv;
	}

	/// <summary>CAM 03: trunks close to the lens, the woods behind them black.</summary>
	private static Canvas FeedDense(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.4f;
		IrBackdrop(cv, hz, seed);
		IrWoods(cv, hz, 46, seed + 2, 0.55f);
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 3) };
		for (int i = 0; i < 4; i++)
		{
			float k = rng.RandfRange(0.5f, 0.95f);
			Fern(cv, rng.RandfRange(0, PicW), hz + k * (PicH - hz), 14f + 30f * k, Ir(9f - 8f * k), rng);
		}
		return cv;
	}

	/// <summary>CAM 04: the trail sign at a fork, the paths parting either side of it.</summary>
	private static Canvas FeedFork(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.4f;
		IrBackdrop(cv, hz, seed);
		const float split = 0.5f;
		float Spread(float k) => k >= split ? 0f : Mathf.Pow((split - k) / split, 0.8f) * PicW * 0.32f;
		float Hw(float k) => 1.5f + Mathf.Pow(k, 1.5f) * 64f;
		Path(cv, hz, k => PicW * 0.5f - Spread(k), Hw, seed + 1);
		Path(cv, hz, k => PicW * 0.5f + Spread(k), Hw, seed + 11);
		IrWoods(cv, hz, 36, seed + 2, 1.2f, (x, by) =>
		{
			float k = (by - hz) / (PicH - hz);
			float d = Mathf.Min(Mathf.Abs(x - (PicW * 0.5f - Spread(k))), Mathf.Abs(x - (PicW * 0.5f + Spread(k))));
			return d < Hw(k) * 1.2f + 4f || (k > split - 0.12f && k < split + 0.15f && Mathf.Abs(x - PicW * 0.5f) < 40f);
		});
		// the sign: a post, two boards pointing the two ways, letters cut into them
		float baseY = hz + split * (PicH - hz), px = PicW * 0.5f, l = Ir(6f);
		Trunk(cv, px, baseY, 5f, l * 0.9f, seed + 4);
		for (int b = 0; b < 2; b++)
		{
			float y = baseY - 54f + b * 15f, dir = b == 0 ? -1f : 1f;
			float x0 = px - dir * 6f, x1 = px + dir * 34f;
			cv.Line(x0, y, x1, y, 5f, 5f, Grey(l * 1.05f));
			cv.Line(x1, y, x1 + dir * 7f, y, 5.5f, 0.5f, Grey(l * 1.05f));
			for (int c = 0; c < 6; c++)
			{
				float lx = Mathf.Lerp(x0, x1, 0.12f + c * 0.14f);
				cv.Line(lx, y - 2f, lx + dir * 2f, y + 2f, 0.5f, 0.5f, Grey(l * 0.35f));
			}
		}
		cv.Rect(px - 18f, px + 18f, baseY - 1f, baseY + 1.5f, Grey(l * 0.4f));   // its shadow on the path
		return cv;
	}

	/// <summary>CAM 05: someone in the trees. Tall and thin, half behind a trunk, its arms hanging past its knees,
	/// its head tipped out to look at the lens and its eyes catching the camera's lamp. On a ninth of the sets.</summary>
	private static Canvas FeedWatcher(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.41f;
		IrBackdrop(cv, hz, seed);
		const float fd = 0.56f;
		float fx = PicW * 0.6f, fBase = hz + Mathf.Pow(fd, 1.3f) * (PicH - hz) * 0.97f + 1f;
		IrWoods(cv, hz, 26, seed + 2, 1f, null, fd - 0.04f);
		// it: a man's height and a half (a trunk at this depth is a span across: ~30 px a metre)
		float h = 78f, l = Ir(9f) * 0.75f;
		var c = Grey(l);
		float Y(float u) => fBase - h * u;
		float hip = Y(0.47f), sh = Y(0.8f), neck = Y(0.9f);
		cv.Line(fx - 3f, hip, fx - 4.5f, fBase, 2f, 1.2f, c);                 // its legs, too long
		cv.Line(fx + 3f, hip, fx + 4f, fBase, 2f, 1.2f, c);
		cv.Line(fx, hip, fx - 0.5f, sh, 4f, 5f, c);                           // its body, narrow
		for (int r = 0; r < 5; r++)                                            // rags hanging off it
			cv.Line(fx - 5f + r * 2.4f, sh + 2f, fx - 6f + r * 2.6f + Hash(r, 0, seed) * 2f, hip + 6f + Hash(r, 1, seed) * 8f, 1f, 0.4f, Grey(l * 0.8f));
		cv.Line(fx - 6f, sh + 1f, fx - 9f, Y(0.52f), 1.3f, 1.1f, c);           // the near arm, hanging to its knee and past
		cv.Line(fx - 9f, Y(0.52f), fx - 8f, Y(0.27f), 1.1f, 0.9f, c);
		for (int k = 0; k < 4; k++)                                            // long fingers
			cv.Line(fx - 8f, Y(0.27f), fx - 9.5f + k * 1.1f, Y(0.2f), 0.5f, 0.3f, c);
		// its neck drawn out, the head tipped over toward the trunk's edge
		var head = new Vector2(fx - 8f, Y(0.97f));
		cv.Line(fx - 0.5f, sh, fx - 2.5f, neck, 1.6f, 1.3f, c);
		cv.Line(fx - 2.5f, neck, head.X + 2f, head.Y + 3f, 1.3f, 1.2f, c);
		cv.Line(head.X, head.Y - 2.5f, head.X, head.Y + 3.5f, 3.6f, 3f, Grey(l * 1.35f));   // the pale face
		// a trunk in front of it, hiding its far side
		Trunk(cv, fx + 6f, Y(0f) + 3f, 13f, Ir(8f) * 0.9f, seed + 5);
		IrWoods(cv, hz, 10, seed + 6, 1f, (x, by) => Mathf.Abs(x - fx) < 40f, 1f, fd + 0.05f);
		// its eyes: the lamp's light thrown back, as a deer's are
		cv.Disc(head.X - 1.6f, head.Y - 0.5f, 1.05f, Grey(1.25f));
		cv.Disc(head.X + 1.4f, head.Y - 0.3f, 1.05f, Grey(1.25f));
		return cv;
	}

	/// <summary>CAM 06: the bunker's own door, in its concrete face in the mound, the ivy hanging over it.</summary>
	private static Canvas FeedDoor(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.36f, cx = PicW * 0.48f;
		IrBackdrop(cv, hz, seed);
		IrWoods(cv, hz, 22, seed + 2, 1f, null, 0.22f);
		float faceT = PicH * 0.3f, faceB = PicH * 0.8f, faceL = cx - PicW * 0.23f, faceR = cx + PicW * 0.23f;
		// the mound: earth and leaves heaped over it, trees growing out of its top
		for (int x = 0; x < PicW; x++)
		{
			float u = (x - cx) / (PicW * 0.62f);
			float top = PicH * 0.2f + PicH * 0.2f * u * u + 6f * Fbm(x, 0, PicW, PicH, 12, 2, seed + 3);
			for (int y = (int)top; y < faceB + 4f; y++)
			{
				float k = (y - top) / (faceB - top);
				float l = (0.12f + 0.2f * k) * (0.65f + 0.6f * Fbm(x, y * 1.5f, PicW, PicH, 28, 3, seed + 4));
				cv.Set(x, y, Grey(l));
			}
		}
		// the concrete face: board-formed, streaked down from the top
		for (int y = (int)faceT; y < faceB; y++)
			for (int x = (int)faceL; x < faceR; x++)
			{
				float board = ((int)(y - faceT) % 9) == 0 ? 0.72f : 1f;
				float streak = Fbm(x * 3f, y * 0.25f, PicW, PicH, 24, 2, seed + 5);
				float l = Ir(5f) * board * (0.72f + 0.35f * streak) * (0.9f + 0.15f * Hash(x, y, seed + 6));
				cv.Set(x, y, Grey(l));
			}
		cv.Rect(faceL - 4f, faceR + 4f, faceT - 6f, faceT, Grey(Ir(5f) * 1.1f));   // its lintel slab
		// the door: steel, a wheel, rivets round it; deep in its frame
		float dl = cx - 21f, dr = cx + 21f, dt = PicH * 0.44f;
		cv.Rect(dl - 4f, dr + 4f, dt - 4f, faceB, Grey(0.04f));
		cv.Rect(dl, dr, dt, faceB, Grey(Ir(5.5f) * 0.55f));
		for (int r = 0; r < 8; r++)
		{
			cv.Disc(dl + 3f, dt + 4f + r * 7.5f, 0.9f, Grey(Ir(5f) * 0.9f));
			cv.Disc(dr - 3f, dt + 4f + r * 7.5f, 0.9f, Grey(Ir(5f) * 0.9f));
		}
		var wc = new Vector2(cx, dt + 26f);
		for (int s = 0; s < 3; s++)
		{
			float a = s * Mathf.Pi / 3f;
			cv.Line(wc.X - Mathf.Cos(a) * 8f, wc.Y - Mathf.Sin(a) * 8f, wc.X + Mathf.Cos(a) * 8f, wc.Y + Mathf.Sin(a) * 8f, 0.8f, 0.8f, Grey(Ir(5f) * 0.95f));
		}
		// the ivy, hanging over the face in ropes, leaves all down them
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 7) };
		for (int v = 0; v < 14; v++)
		{
			float x = rng.RandfRange(faceL - 6f, faceR + 6f), len = rng.RandfRange(20f, PicH * 0.45f), sway = rng.RandfRange(-6f, 6f);
			if (x > dl - 2f && x < dr + 2f && rng.Randf() < 0.6f) continue;   // mostly off the door (it was opened)
			var prev = new Vector2(x, faceT - 5f);
			for (int sgm = 1; sgm <= 8; sgm++)
			{
				float u = sgm / 8f;
				var p = new Vector2(x + Mathf.Sin(u * 3f + v) * sway * u, faceT - 5f + len * u);
				cv.Line(prev.X, prev.Y, p.X, p.Y, 0.7f, 0.6f, Grey(0.1f));
				if (rng.Randf() < 0.8f) cv.Disc(p.X + rng.RandfRange(-2f, 2f), p.Y, rng.RandfRange(1.2f, 2.4f), Grey(Ir(5f) * rng.RandfRange(0.3f, 0.6f)));
				prev = p;
			}
		}
		return cv;
	}

	/// <summary>CAM 07: the tunnel, from high on its wall: the vault running away, its ribs, the conduits, the cages
	/// of its lamps, the far end black. Ray-cast through the hallway's own profile.</summary>
	private static Canvas FeedTunnel(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		var eye = new Vector3(1.3f, 2.5f, 0f);
		float f = PicH * 0.85f, pitch = -0.1f;
		float hw = BunkerLayout.HallHalfWidth, kick = BunkerLayout.HallKickHeight;
		for (int y = 0; y < PicH; y++)
			for (int x = 0; x < PicW; x++)
			{
				var d = new Vector3((x + 0.5f - PicW * 0.5f) / f, -(y + 0.5f - PicH * 0.5f) / f, -1f).Normalized();
				d = d.Rotated(Vector3.Right, pitch);
				float tBest = 200f; int what = -1;
				if (d.Y < -1e-4f) { float tt = -eye.Y / d.Y; if (tt < tBest) { tBest = tt; what = 0; } }
				if (Mathf.Abs(d.X) > 1e-4f)
				{
					float tt = (Mathf.Sign(d.X) * hw - eye.X) / d.X;
					if (tt > 0f && eye.Y + d.Y * tt <= kick && tt < tBest) { tBest = tt; what = 1; }
				}
				{
					var o = new Vector2(eye.X, eye.Y - kick); var dd = new Vector2(d.X, d.Y);
					float a = dd.Dot(dd), b = 2f * o.Dot(dd), c = o.Dot(o) - hw * hw;
					float disc = b * b - 4f * a * c;
					if (a > 1e-6f && disc > 0f)
					{
						float tt = (-b + Mathf.Sqrt(disc)) / (2f * a);
						if (tt > 0f && eye.Y + d.Y * tt > kick && tt < tBest) { tBest = tt; what = 2; }
					}
				}
				if (what < 0) { cv.Set(x, y, Grey(0f)); continue; }
				var p = eye + d * tBest;
				float along = -p.Z;
				float around = what == 0 ? p.X : what == 1 ? Mathf.Sign(p.X) * (hw + p.Y) : Mathf.Atan2(p.Y - kick, p.X) * hw + 10f;
				float tex = 0.7f + 0.3f * VNoise(along * 3f + 50f, around * 3f + 50f, 512, 512, seed)
					+ 0.15f * (VNoise(along * 14f + 50f, around * 14f + 50f, 2048, 2048, seed + 1) - 0.5f);
				float rib = along / 2.5f - Mathf.Floor(along / 2.5f);   // a rib every 2.5 m (BunkerHallway's)
				if (what != 0) tex *= rib < 0.06f ? 1.25f : rib < 0.1f ? 0.55f : 1f;
				if (what == 1 && Mathf.Abs(p.Y - 1.0f) < 0.05f) tex *= 0.35f;   // the conduit along the wall
				if (what == 0) tex *= 0.85f + 0.15f * Mathf.Abs(Mathf.Sin(along * 0.9f + p.X));
				float l = Ir(tBest * 0.85f) * tex * (what == 2 ? 0.85f : 1f);
				cv.Set(x, y, Grey(l));
			}
		// the lamps in their cages down the crown of the vault, unlit (the camera sees by its own lamp)
		for (int i = 0; i < 18; i++)
		{
			float z = 2.5f + i * 5f;   // a lamp every 5 m
			var p = new Vector3(0f, kick + hw - 0.25f, -z) - eye;
			p = p.Rotated(Vector3.Right, -pitch);
			if (p.Z > -0.5f) continue;
			float sx = PicW * 0.5f + f * p.X / -p.Z, sy = PicH * 0.5f - f * p.Y / -p.Z, r = f * 0.1f / -p.Z;
			cv.Disc(sx, sy, r, Grey(Ir(z) * 1.2f));
			cv.Line(sx - r, sy, sx + r, sy, 0.4f, 0.4f, Grey(Ir(z) * 0.3f));
		}
		return cv;
	}

	/// <summary>CAM 08: the lake shore: the black water, the far trees mirrored in it, stones and reeds at the lens.</summary>
	private static Canvas FeedShore(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.42f, shore = hz + 6f, nearEdge = PicH * 0.76f;
		IrBackdrop(cv, hz, seed);
		IrWoods(cv, hz, 50, seed + 2, 1f, null, 0.16f);
		// the far firs' tops along the shore, a ragged band
		for (int x = 0; x < PicW; x++)
		{
			float top = hz - 8f - 14f * Fbm(x, 0, PicW, PicH, 18, 3, seed + 3) - 6f * Mathf.Abs(Mathf.Sin(x * 0.45f));
			for (int y = (int)top; y < shore; y++) cv.Set(x, y, Grey(0.12f + 0.05f * Hash(x, y, seed + 4) + 0.04f * (y - top) / (shore - top)));
		}
		// the water, the shore mirrored in it, broken by the ripples
		for (int y = (int)shore; y < nearEdge; y++)
			for (int x = 0; x < PicW; x++)
			{
				float k = (y - shore) / (nearEdge - shore);
				int my = (int)(2f * shore - y + Mathf.Sin(y * 1.7f + x * 0.05f) * 1.5f);
				float refl = my >= 0 ? cv.Px[my * PicW + x].G * 0.7f : 0f;
				float rip = Fbm(x * 0.35f, y * 5f, PicW, PicH, 18, 2, seed + 5);
				float l = 0.03f + refl * (1f - k * 0.6f) + (rip > 0.6f ? (rip - 0.6f) * 2.2f * Ir(9f - 6f * k) : 0f);
				cv.Set(x, y, Grey(l));
			}
		// the near shore: stones lit pale by the lamp, the reeds between them
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 6) };
		for (int y = (int)nearEdge; y < PicH; y++)
			for (int x = 0; x < PicW; x++)
			{
				float k = (y - nearEdge) / (PicH - nearEdge);
				cv.Set(x, y, Grey((0.22f + 0.3f * k) * (0.6f + 0.6f * Fbm(x, y * 2f, PicW, PicH, 30, 3, seed + 7))));
			}
		for (int i = 0; i < 22; i++)
		{
			float y = rng.RandfRange(nearEdge - 2f, PicH + 4f), k = (y - nearEdge) / (PicH - nearEdge);
			float x = rng.RandfRange(0, PicW), r = 3f + 12f * k * rng.RandfRange(0.5f, 1f);
			float l = Ir(4f - 3f * k);
			for (int yy = (int)(y - r); yy <= y + r * 0.5f; yy++)
				for (int xx = (int)(x - r * 1.4f); xx <= x + r * 1.4f; xx++)
				{
					float u = (xx - x) / (r * 1.4f), v = (yy - y) / r;
					float q = u * u + v * v;
					if (q > 1f || yy < 0 || yy >= PicH || xx < 0 || xx >= PicW) continue;
					float lit = 0.55f + 0.45f * Mathf.Clamp(-v * 0.8f - u * 0.3f + 0.4f, 0f, 1f);
					cv.Blend(xx, yy, Grey(l * lit * (0.85f + 0.2f * Hash(xx, yy, seed + 8))), Mathf.Clamp((1f - q) * r, 0f, 1f));
				}
		}
		for (int i = 0; i < 30; i++)
		{
			float x = rng.RandfRange(0, PicW), y = rng.RandfRange(nearEdge - 4f, PicH), k = Mathf.Clamp((y - nearEdge) / (PicH - nearEdge), 0f, 1f);
			float len = 18f + 50f * k, lean = rng.RandfRange(-0.25f, 0.25f);
			cv.Line(x, y, x + lean * len, y - len, 0.6f + 0.8f * k, 0.3f, Grey(Ir(5f - 3f * k) * 0.8f));
		}
		return cv;
	}

	/// <summary>CAM 09: a clearing, a fallen tree lying across it, its roots up, ferns round it.</summary>
	private static Canvas FeedFallen(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.4f;
		IrBackdrop(cv, hz, seed);
		IrWoods(cv, hz, 34, seed + 2, 1.2f, (x, by) => by > PicH * 0.58f && x > PicW * 0.08f && x < PicW * 0.92f);
		// the log: round, its bark in long furrows, lit along its top, thicker at the root end
		var a = new Vector2(PicW * 0.1f, PicH * 0.78f); var b = new Vector2(PicW * 0.92f, PicH * 0.64f);
		var ab = b - a; float len = ab.Length(); var n = new Vector2(-ab.Y, ab.X) / len;
		for (int y = (int)(PicH * 0.5f); y < PicH; y++)
			for (int x = 0; x < PicW; x++)
			{
				var p = new Vector2(x + 0.5f, y + 0.5f) - a;
				float u = p.Dot(ab) / (len * len);
				if (u < 0f || u > 1f) continue;
				float r = Mathf.Lerp(15f, 9f, u), off = p.Dot(n);
				float cover = Mathf.Clamp(r - Mathf.Abs(off) + 0.5f, 0f, 1f);
				if (cover <= 0f) continue;
				float t = off / r;
				float round = 0.35f + 0.65f * Mathf.Clamp(0.6f - t * 0.6f, 0f, 1f) * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
				float furrow = VNoise(u * 40f, t * 3f + 8f, 4096, 64, seed + 3);
				float l = Ir(5.5f - 2f * u) * round * (furrow < 0.3f ? 0.55f : 0.8f + 0.3f * furrow);
				cv.Blend(x, y, Grey(l), cover);
			}
		// the root plate, torn up at the near end, and a broken branch stub
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 4) };
		for (int i = 0; i < 12; i++)
		{
			float ang = rng.RandfRange(-2.6f, 2.6f), rl = rng.RandfRange(12f, 30f);
			cv.Line(a.X, a.Y, a.X - Mathf.Cos(ang) * rl * 0.6f, a.Y + Mathf.Sin(ang) * rl, 2.5f, 0.4f, Grey(Ir(5f) * 0.7f));
		}
		cv.Line(PicW * 0.55f, PicH * 0.69f, PicW * 0.6f, PicH * 0.55f, 3f, 1f, Grey(Ir(6f) * 0.85f));
		for (int i = 0; i < 6; i++)
		{
			float x = rng.RandfRange(PicW * 0.1f, PicW * 0.95f);
			Fern(cv, x, Mathf.Lerp(a.Y, b.Y, (x - a.X) / ab.X) + rng.RandfRange(6f, 30f), rng.RandfRange(16f, 34f), Ir(5f), rng);
		}
		return cv;
	}

	/// <summary>A stencilled word in paint on concrete (the tunnel's markings): white in the alpha, the paint worn
	/// through in places and run a little below the letters; 4 px a letter's pixel by default, so it stays crisp.</summary>
	public static Texture2D Stencil(string text, int scale = 10)
	{
		string key = "bk_pic_stencil_" + text;
		if (_tex.TryGetValue(key, out var t)) return t;
		int w = text.Length * 4 * scale + scale * 2, h = 5 * scale + scale * 3;
		var cv = new Canvas(w, h, new Color(1, 1, 1, 0));
		float x = scale;
		foreach (char ch in text)
		{
			if (Glyphs.TryGetValue(ch, out var g))
				for (int r = 0; r < 5; r++)
					for (int k = 0; k < 3; k++)
					{
						if (g[r * 3 + k] != '1') continue;
						// a stencil's bridges: the middle of each letter's left column left unpainted
						if (k == 0 && r == 2 && g[r * 3 + 1] == '0' && g[r * 3 + 2] == '1') continue;
						for (int yy = 0; yy < scale; yy++)
							for (int xx = 0; xx < scale; xx++)
							{
								int px = (int)x + k * scale + xx, py = scale + r * scale + yy;
								float wear = Fbm(px, py, w, h, Mathf.Max(4, w / 12), 3, 951 + text.Length);
								float a = Mathf.Clamp((wear - 0.3f) * 3.5f, 0f, 1f) * (0.75f + 0.25f * Hash(px, py, 952));
								cv.Set(px, py, new Color(1, 1, 1, a));
							}
						// now and then the paint ran
						if (r == 4 && Hash((int)x + k, r, 953) > 0.6f)
						{
							int dx = (int)x + k * scale + (int)(Hash(k, (int)x, 954) * (scale - 2)), len = (int)(scale * (0.6f + 1.4f * Hash(k, (int)x, 955)));
							for (int d = 0; d < len && 6 * scale + d < h; d++)
								for (int dw = 0; dw < Mathf.Max(1, scale / 5); dw++)
									cv.Set(dx + dw, 6 * scale + d, new Color(1, 1, 1, 0.8f * (1f - d / (float)len)));
						}
					}
			x += 4 * scale;
		}
		return Store(key, cv.ToImage());
	}

	/// <summary>A floor drain's grate: iron bars in a frame, black between them.</summary>
	public static Texture2D Grate() => Make("bk_grate", 64, 64, (x, y) =>
	{
		bool frame = x < 4 || x >= 60 || y < 4 || y >= 60;
		bool bar = (x - 4) % 8 < 4;
		if (!frame && !bar) return new Color(0.02f, 0.02f, 0.02f, 1f);
		float n = 0.75f + 0.35f * Hash(x, y, 961);
		float rust = Fbm(x, y, 64, 64, 6, 3, 962);
		var c = new Color(0.32f, 0.3f, 0.28f) * n;
		return c.Lerp(new Color(0.36f, 0.2f, 0.1f) * n, Mathf.Clamp((rust - 0.45f) * 3f, 0f, 1f));
	});

	/// <summary>Hazard stripes, black and a dull yellow, chipped (the tunnel's far end, low on the walls).</summary>
	public static Texture2D Hazard() => Make("bk_hazard", 128, 32, (x, y) =>
	{
		bool yellow = ((x + y) / 16) % 2 == 0;
		float chip = Fbm(x, y, 128, 32, 16, 3, 971);
		var c = yellow ? new Color(0.4f, 0.33f, 0.1f) : new Color(0.06f, 0.06f, 0.05f);
		float a = Mathf.Clamp((chip - 0.34f) * 3f, 0f, 1f) * 0.85f;
		return new Color(c.R, c.G, c.B, a);
	});

	// ------------------------------------------------------------------ helpers

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

	/// <summary>A soft blotchy unevenness over the whole picture (the lens, the compression of the tape).</summary>
	private static void Mottle(Canvas cv, int seed, float amount)
	{
		for (int y = 0; y < cv.H; y++)
			for (int x = 0; x < cv.W; x++)
			{
				float k = 1f + (Fbm(x, y, cv.W, cv.H, 10, 3, seed) - 0.5f) * amount * 2f;
				var c = cv.Px[y * cv.W + x];
				cv.Px[y * cv.W + x] = new Color(c.R * k, c.G * k, c.B * k, c.A);
			}
	}

	/// <summary>3 x 5 pixel letters: what the cameras burn in, and the tunnel's stencils.</summary>
	private static readonly Dictionary<char, string> Glyphs = new()
	{
		['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111", ['4'] = "101101111001001",
		['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001010010010", ['8'] = "111101111101111", ['9'] = "111101111001111",
		[':'] = "000010000010000", ['/'] = "001001010100100", ['-'] = "000000111000000", ['<'] = "001010100010001", ['>'] = "100010001010100",
		['A'] = "010101111101101", ['B'] = "110101110101110", ['C'] = "111100100100111", ['D'] = "110101101101110", ['E'] = "111100110100111",
		['F'] = "111100110100100", ['G'] = "011100101101011", ['H'] = "101101111101101", ['I'] = "111010010010111", ['J'] = "001001001101010",
		['K'] = "101101110101101", ['L'] = "100100100100111", ['M'] = "101111111101101", ['N'] = "110101101101101", ['O'] = "010101101101010",
		['P'] = "110101110100100", ['Q'] = "010101101110011", ['R'] = "110101110101101", ['S'] = "011100010001110", ['T'] = "111010010010010",
		['U'] = "101101101101111", ['V'] = "101101101101010", ['W'] = "101101111111101", ['X'] = "101101010101101", ['Y'] = "101101010010010",
		['Z'] = "111001010100111",
	};

	private static void Text(Canvas cv, float x, float y, string s, int scale, Color c)
	{
		// a shadow under the whole line first (so it reads on the pale), then the letters
		for (int pass = 0; pass < 2; pass++)
		{
			float cx = x, o = pass == 0 ? 1f : 0f;
			var col = pass == 0 ? Grey(0.02f) : c;
			foreach (char ch in s)
			{
				if (Glyphs.TryGetValue(ch, out var g))
					for (int r = 0; r < 5; r++)
						for (int k = 0; k < 3; k++)
							if (g[r * 3 + k] == '1')
								cv.Rect(cx + k * scale + o, cx + (k + 1) * scale + o, y + r * scale + o, y + (r + 1) * scale + o, col);
				cx += 4 * scale;
			}
		}
	}
}
