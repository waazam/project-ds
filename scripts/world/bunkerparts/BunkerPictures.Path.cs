using System;
using Godot;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// (2026-10-16, STORY.md Rev 5, Act 9) The CRT room's new pictures, painted the way the woods' cameras are (night
/// vision, the camera's lamp lighting what's near it, the number and the time burnt in):
/// <list type="bullet">
/// <item><b>his path</b>: fixed shots of the places he passed, in order: R.H.'s camp, the cabin boarded, the footbridge,
/// the clearing of stairs, and the cabin burning (the flames in the alpha, for the tube's flicker). A row of sets
/// replays them in turn (<see cref="CrtRoom"/>); the time stamps run on through his night;</item>
/// <item><b>the arrival</b>: the trailhead lot on the morning he came, in grey daylight: his car with its trunk up and
/// him at the back of it lifting the camera out. He was watched from the start.</item>
/// </list>
/// Fixed shots, not a recording of the player's own route.
/// </summary>
public static partial class BunkerTextures
{
	/// <summary>How many shots his path has (the last is the cabin burning).</summary>
	public const int PathShots = 5;

	/// <summary>Shot <paramref name="i"/> (0 .. <see cref="PathShots"/> - 1) of his path, in order.</summary>
	public static Texture2D PathPicture(int i)
	{
		i = Mathf.Clamp(i, 0, PathShots - 1);
		string key = $"bk_pic_path{i}";
		if (_tex.TryGetValue(key, out var t)) return t;
		var paints = new Func<int, Canvas>[] { FeedCamp, FeedCabinBoarded, FeedFootbridge, FeedClearing, FeedCabinBurning };
		int[] cams = { 11, 12, 13, 14, 12 };
		string[] stamps = { "10/25 21:52:07", "10/25 22:31:40", "10/25 23:08:19", "10/25 23:46:55", "10/26 01:17:33" };
		var cv = paints[i](701 + i * 10);
		Stamp(cv, $"CAM {cams[i]:00}", stamps[i], 790 + i, i == PathShots - 1);
		return Store(key, cv.ToImage());
	}

	/// <summary>The trailhead lot on the morning he arrived: the car, the trunk up, him lifting the camera out.</summary>
	public static Texture2D ArrivalPicture()
	{
		const string key = "bk_pic_arrival";
		if (_tex.TryGetValue(key, out var t)) return t;
		var cv = FeedArrival(771);
		Stamp(cv, "LOT 1", "10/25 09:41:26", 799, false);
		return Store(key, cv.ToImage());
	}

	/// <summary>As the woods' cameras' finish (the lamp's fall-off, the mottle, the grain), with its own label and time;
	/// <paramref name="keepAlpha"/> leaves the flames' mask alone.</summary>
	private static void Stamp(Canvas cv, string label, string stamp, int seed, bool keepAlpha)
	{
		for (int y = 0; y < cv.H; y++)
			for (int x = 0; x < cv.W; x++)
			{
				float dx = (x - cv.W * 0.5f) / (cv.W * 0.5f), dy = (y - cv.H * 0.5f) / (cv.H * 0.5f);
				float k = 1f - 0.34f * (dx * dx * 0.8f + dy * dy);
				var c = cv.Px[y * cv.W + x];
				cv.Px[y * cv.W + x] = new Color(c.R * k, c.G * k, c.B * k, keepAlpha ? c.A : 1f);
			}
		Mottle(cv, seed, 0.08f);
		Grain(cv, seed + 1, 0.08f);
		var ink = Grey(0.9f);
		Text(cv, 8, 8, label, 2, ink);
		cv.Disc(cv.W - 40, 12.5f, 3.4f, Grey(0.95f));
		Text(cv, cv.W - 32, 8, "REC", 2, ink);
		Text(cv, 8, cv.H - 18, stamp, 2, ink);
	}

	/// <summary>A filled polygon (even-odd), blended at <paramref name="a"/>.</summary>
	private static void Poly(Canvas cv, Vector2[] pts, Color c, float a = 1f)
	{
		float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
		foreach (var p in pts) { x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X); y0 = Mathf.Min(y0, p.Y); y1 = Mathf.Max(y1, p.Y); }
		for (int y = Mathf.Max(0, (int)y0); y <= Mathf.Min(cv.H - 1, (int)y1); y++)
			for (int x = Mathf.Max(0, (int)x0); x <= Mathf.Min(cv.W - 1, (int)x1); x++)
			{
				float px = x + 0.5f, py = y + 0.5f;
				bool inside = false;
				for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
					if ((pts[i].Y > py) != (pts[j].Y > py) && px < (pts[j].X - pts[i].X) * (py - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
						inside = !inside;
				if (inside) cv.Blend(x, y, c, a);
			}
	}

	// ---- his path

	/// <summary>R.H.'s camp: his tent pitched under the trunks, the ring of stones round a dead fire, the stump.</summary>
	private static Canvas FeedCamp(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.4f;
		IrBackdrop(cv, hz, seed);
		IrWoods(cv, hz, 34, seed + 2, 1.2f, (x, by) => by > PicH * 0.55f && x > PicW * 0.18f && x < PicW * 0.88f);
		float l = Ir(6f);
		// the tent: an A-frame, its fly pale under the lamp, the door a dark slit with a flap tied back
		var apex = new Vector2(PicW * 0.4f, PicH * 0.44f);
		Poly(cv, new[] { apex, new Vector2(PicW * 0.25f, PicH * 0.76f), new Vector2(PicW * 0.56f, PicH * 0.76f) }, Grey(l * 0.85f));
		Poly(cv, new[] { apex + new Vector2(1f, 6f), new Vector2(PicW * 0.37f, PicH * 0.76f), new Vector2(PicW * 0.45f, PicH * 0.76f) }, Grey(0.03f));
		Poly(cv, new[] { apex + new Vector2(2f, 8f), new Vector2(PicW * 0.45f, PicH * 0.76f), new Vector2(PicW * 0.49f, PicH * 0.7f) }, Grey(l * 0.6f));
		cv.Line(apex.X, apex.Y, apex.X + 26f, apex.Y + 4f, 0.5f, 0.4f, Grey(l * 0.7f));   // a guy line
		cv.Line(PicW * 0.25f, PicH * 0.76f, PicW * 0.56f, PicH * 0.76f, 0.8f, 0.8f, Grey(l * 0.3f));
		// the fire ring: stones in an oval, the dead fire black in it
		var fc = new Vector2(PicW * 0.68f, PicH * 0.82f);
		for (int y = (int)(fc.Y - 8); y <= fc.Y + 8; y++)
			for (int x = (int)(fc.X - 26); x <= fc.X + 26; x++)
			{
				float u = (x - fc.X) / 22f, v = (y - fc.Y) / 7f;
				if (u * u + v * v < 1f) cv.Blend(x, y, Grey(0.04f + 0.04f * Hash(x, y, seed + 5)), 0.9f);
			}
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 6) };
		for (int i = 0; i < 14; i++)
		{
			float a = Mathf.Tau * i / 14f;
			cv.Disc(fc.X + Mathf.Cos(a) * 24f, fc.Y + Mathf.Sin(a) * 8f, rng.RandfRange(2.5f, 4f), Grey(Ir(4.5f) * rng.RandfRange(0.7f, 1f)));
		}
		for (int i = 0; i < 4; i++)   // the charred ends of sticks
			cv.Line(fc.X + rng.RandfRange(-12f, 12f), fc.Y + rng.RandfRange(-3f, 3f), fc.X + rng.RandfRange(-12f, 12f), fc.Y + rng.RandfRange(-3f, 3f), 1.2f, 0.8f, Grey(0.12f));
		// the stump beside it: round, lit down its middle, its sawn top pale
		float sx = PicW * 0.86f, sb = PicH * 0.82f, st = sb - 24f;
		for (int y = (int)st; y < sb; y++)
			for (int x = (int)(sx - 10f); x <= sx + 10f; x++)
			{
				float u = (x - sx) / 10f;
				float round = 0.4f + 0.6f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
				cv.Set(x, y, Grey(Ir(5f) * 0.8f * round * (0.8f + 0.3f * Hash(x, y / 3, seed + 7))));
			}
		for (int y = (int)(st - 3f); y <= st + 3f; y++)
			for (int x = (int)(sx - 10f); x <= sx + 10f; x++)
			{
				float u = (x - sx) / 10f, v = (y - st) / 3f;
				if (u * u + v * v <= 1f) cv.Set(x, y, Grey(Ir(5f) * (1.05f - 0.25f * (u * u + v * v))));
			}
		return cv;
	}

	/// <summary>The front of a log cabin under the lamp (its walls, its gable, the door and two windows). Shared by the
	/// boarded shot and the burning one.</summary>
	private static void CabinFront(Canvas cv, int seed, float lum, bool boarded, bool burning)
	{
		float l = lum;
		float wl = PicW * 0.24f, wr = PicW * 0.76f, wt = PicH * 0.42f, wb = PicH * 0.8f;
		var apex = new Vector2(PicW * 0.5f, PicH * 0.16f);
		// the roof, over the eaves
		Poly(cv, new[] { apex, new Vector2(wl - 12f, wt + 2f), new Vector2(wr + 12f, wt + 2f) }, Grey(l * 0.35f));
		Poly(cv, new[] { apex + new Vector2(0, 6f), new Vector2(wl + 4f, wt), new Vector2(wr - 4f, wt) }, Grey(l * 0.7f));   // the gable's logs
		// the walls, in round logs, each lit along its top
		for (int y = (int)wt; y < wb; y++)
			for (int x = (int)wl; x < wr; x++)
			{
				float lp = (y - wt) / 7f;
				float round = 0.45f + 0.55f * Mathf.Sin((lp - Mathf.Floor(lp)) * Mathf.Pi);
				cv.Set(x, y, Grey(l * round * (0.8f + 0.3f * Hash(x / 2, y, seed + 1))));
			}
		// the door and the windows
		float dl = PicW * 0.45f, dr = PicW * 0.55f, dt = PicH * 0.53f;
		var dark = burning ? Grey(1.0f) : Grey(0.03f);
		cv.Rect(dl, dr, dt, wb, dark);
		var wins = new[] { (PicW * 0.29f, PicW * 0.39f), (PicW * 0.61f, PicW * 0.71f) };
		foreach (var (a, b) in wins) cv.Rect(a, b, PicH * 0.5f, PicH * 0.64f, dark);
		if (burning)
		{
			// blown out: the fire in them burns the picture white, haloed
			foreach (var (a, b) in new[] { (dl, dr), wins[0], wins[1] })
				for (int y = (int)(PicH * 0.36f); y < wb + 10; y++)
					for (int x = (int)(a - 14); x < b + 14; x++)
					{
						float cx = Mathf.Clamp(x, a, b), cy = Mathf.Clamp(y, PicH * 0.5f, wb);
						float d = new Vector2(x - cx, y - cy).Length();
						if (d > 0f) cv.Blend(x, y, Grey(0.95f), Mathf.Exp(-d / 5f) * 0.8f);
					}
			for (int y = (int)dt; y < wb; y++)
				for (int x = (int)dl; x < dr; x++)
					cv.Set(x, y, new Color(1f, 1f, 1f, 0.6f + 0.4f * Hash(x / 3, y / 3, seed + 9)));
			return;
		}
		if (!boarded) return;
		// boards nailed across them, pale and new against the logs, crooked, the nails' heads dark
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 3) };
		void Board(float x0, float x1, float y, float tilt)
		{
			float y0 = y - tilt, y1 = y + tilt;
			cv.Line(x0, y0, x1, y1, 2.6f, 2.6f, Grey(l * 1.35f));
			cv.Line(x0, y0 + 2.4f, x1, y1 + 2.4f, 0.5f, 0.5f, Grey(l * 0.4f));
			cv.Disc(x0 + 3f, y0 + 0.2f, 0.7f, Grey(0.05f));
			cv.Disc(x1 - 3f, y1 + 0.2f, 0.7f, Grey(0.05f));
		}
		foreach (var (a, b) in wins)
			for (int k = 0; k < 3; k++) Board(a - 4f, b + 4f, PicH * 0.52f + k * 9f, rng.RandfRange(-3f, 3f));
		for (int k = 0; k < 4; k++) Board(dl - 5f, dr + 5f, dt + 8f + k * 15f, rng.RandfRange(-4f, 4f));
		Board(dl - 2f, dr + 2f, (dt + wb) * 0.5f, 18f);   // one across the others, corner to corner
	}

	/// <summary>The cabin boarded up (Act 5): its windows and its door nailed over from outside.</summary>
	private static Canvas FeedCabinBoarded(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.44f;
		IrBackdrop(cv, hz, seed);
		IrWoods(cv, hz, 28, seed + 2, 1f, null, 0.3f);
		CabinFront(cv, seed + 4, Ir(8f), true, false);
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 5) };
		for (int i = 0; i < 3; i++)
			Fern(cv, rng.RandfRange(0, PicW), PicH * rng.RandfRange(0.84f, 0.98f), rng.RandfRange(20f, 36f), Ir(3f), rng);
		return cv;
	}

	/// <summary>The footbridge, from the other bank (the woods' camera 02 turned round).</summary>
	private static Canvas FeedFootbridge(int seed)
	{
		var cv = FeedBridge(seed);
		for (int y = 0; y < cv.H; y++)
			for (int x = 0; x < cv.W / 2; x++)
			{
				int a = y * cv.W + x, b = y * cv.W + cv.W - 1 - x;
				(cv.Px[a], cv.Px[b]) = (cv.Px[b], cv.Px[a]);
			}
		return cv;
	}

	/// <summary>The clearing of stairs: stone flights standing in the open among the trunks, going up into the fog.</summary>
	private static Canvas FeedClearing(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.46f;
		IrBackdrop(cv, hz, seed);
		IrWoods(cv, hz, 40, seed + 2, 1f, null, 0.28f);
		// a flight: steps as slabs, each narrower and higher, paler where the lamp reaches, lost in the fog up top
		void Flight(float bx, float by, float w, float stepH, int steps, float lum, float lean)
		{
			for (int s = 0; s < steps; s++)
			{
				float y = by - s * stepH, x = bx + s * lean;
				float fog = Mathf.Clamp((float)s / steps * 1.3f, 0f, 1f);
				float k = lum * (1f - fog * 0.85f) + 0.16f * fog;
				cv.Rect(x - w * 0.5f, x + w * 0.5f, y - stepH, y - stepH * 0.35f, Grey(k * 1.15f));   // the tread's face, lit
				cv.Rect(x - w * 0.5f, x + w * 0.5f, y - stepH * 0.35f, y, Grey(k * 0.55f));          // the riser in shade
				w *= 0.985f;
			}
		}
		Flight(PicW * 0.2f, PicH * 0.66f, 26f, 5f, 22, Ir(16f), 0.3f);
		Flight(PicW * 0.82f, PicH * 0.62f, 20f, 4f, 24, Ir(20f), -0.25f);
		Flight(PicW * 0.52f, PicH * 0.98f, 64f, 9f, 24, Ir(7f), 0f);
		// the fog lying over the clearing, thicker up high
		for (int y = 0; y < PicH; y++)
		{
			float a = 0.3f * Mathf.Clamp(1f - Mathf.Abs(y - hz * 0.7f) / (PicH * 0.4f), 0f, 1f);
			if (a <= 0f) continue;
			for (int x = 0; x < PicW; x++) cv.Blend(x, y, Grey(0.2f + 0.05f * Fbm(x, y, PicW, PicH, 8, 3, seed + 6)), a);
		}
		return cv;
	}

	/// <summary>The cabin burning: the fire blooms white on the night camera, flames up off the roof (the alpha carries
	/// them, for the tube's flicker), the smoke a pale column.</summary>
	private static Canvas FeedCabinBurning(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.44f;
		IrBackdrop(cv, hz, seed);
		IrWoods(cv, hz, 28, seed + 2, 1f, null, 0.3f);
		// the smoke, pale in the glow, leaning off
		for (int y = 0; y < (int)(PicH * 0.3f); y++)
			for (int x = 0; x < PicW; x++)
			{
				float cx = PicW * 0.5f + (PicH * 0.3f - y) * 0.4f, w = 26f + (PicH * 0.3f - y) * 0.5f;
				float n = Fbm(x, y * 1.4f, PicW, PicH, 6, 4, seed + 3);
				float a = Mathf.SmoothStep(1f, 0.3f, Mathf.Abs(x - cx) / w + (0.5f - n) * 0.8f) * 0.7f;
				if (a > 0f) cv.Blend(x, y, Grey(0.45f), a);
			}
		CabinFront(cv, seed + 4, Ir(8f) * 0.6f, false, true);
		// only the fire flickers on the tube: the alpha is its mask (the blown-out door and windows, the flames, the sparks)
		var fire = new float[cv.Px.Length];
		foreach (var (a, b, top, bottom) in new[] { (PicW * 0.45f, PicW * 0.55f, PicH * 0.53f, PicH * 0.8f), (PicW * 0.29f, PicW * 0.39f, PicH * 0.5f, PicH * 0.64f), (PicW * 0.61f, PicW * 0.71f, PicH * 0.5f, PicH * 0.64f) })
			for (int y = (int)top; y < (int)bottom; y++)
				for (int x = (int)a; x < (int)b; x++) fire[y * PicW + x] = cv.Px[y * PicW + x].A;
		// the flames off the roof: white at the heart, the alpha their mask
		float wt = PicH * 0.42f;
		var apex = new Vector2(PicW * 0.5f, PicH * 0.16f);
		for (int y = 0; y < (int)(wt + 4); y++)
			for (int x = (int)(PicW * 0.18f); x < PicW * 0.82f; x++)
			{
				float roofY = apex.Y + Mathf.Abs(x - apex.X) * (wt - apex.Y) / (PicW * 0.26f + 12f);
				float above = roofY - y;
				if (above < -3f) continue;
				float n = Fbm(x * 1.6f, y * 1.1f, PicW, PicH, 8, 4, seed + 5);
				float heat = n * 1.3f - Mathf.Max(above, 0f) / 38f;
				if (heat < 0.3f) continue;
				float k = Mathf.Clamp((heat - 0.3f) / 0.45f, 0f, 1f);
				cv.Set(x, y, Grey(0.72f + 0.28f * k));
				fire[y * PicW + x] = 0.35f + 0.65f * k;
			}
		// sparks going up
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed + 6) };
		for (int i = 0; i < 30; i++)
		{
			float x = rng.RandfRange(PicW * 0.3f, PicW * 0.75f), y = rng.RandfRange(4f, PicH * 0.3f);
			cv.Disc(x, y, rng.RandfRange(0.5f, 1.1f), Grey(1f));
			if (x >= 0 && y >= 0 && x < PicW && y < PicH) fire[(int)y * PicW + (int)x] = 1f;
		}
		for (int i = 0; i < cv.Px.Length; i++) cv.Px[i].A = fire[i];
		return cv;
	}

	// ---- the arrival

	/// <summary>The trailhead lot by day, from a pole camera: the treeline, the gravel, his car side on with its trunk
	/// up, and him at the back of it lifting the camera out.</summary>
	private static Canvas FeedArrival(int seed)
	{
		var cv = new Canvas(PicW, PicH, Colors.Black);
		float hz = PicH * 0.36f;
		// the sky, white and flat; the treeline, dark and ragged; the gravel
		for (int y = 0; y < PicH; y++)
			for (int x = 0; x < PicW; x++)
			{
				float l;
				if (y < hz) l = 0.78f - 0.12f * y / hz;
				else
				{
					float k = (y - hz) / (PicH - hz);
					l = (0.42f + 0.16f * k) * (0.8f + 0.35f * Fbm(x, y * 2.2f, PicW, PicH, 48, 3, seed));
					if (Hash(x, y, seed + 1) > 0.97f) l *= 0.7f;
				}
				cv.Set(x, y, Grey(l));
			}
		for (int x = 0; x < PicW; x++)
		{
			float top = hz - 18f - 22f * Fbm(x, 0, PicW, PicH, 24, 3, seed + 2) - 8f * Mathf.Abs(Mathf.Sin(x * 0.31f));
			for (int y = (int)top; y < hz + 6f; y++) cv.Set(x, y, Grey(0.14f + 0.06f * Hash(x, y, seed + 3)));
		}
		// a log rail along the far side of the lot
		cv.Rect(0, PicW, hz + 10f, hz + 13f, Grey(0.3f));
		for (int i = 0; i < 9; i++) cv.Rect(i * 32f + 6f, i * 32f + 9f, hz + 9f, hz + 20f, Grey(0.25f));
		// the car: side on, nose to the right, its trunk lid up at the back (left)
		float ground = PicH * 0.8f;
		var body = new[]
		{
			new Vector2(PicW * 0.36f, ground - 8f), new Vector2(PicW * 0.36f, ground - 34f), new Vector2(PicW * 0.44f, ground - 37f),
			new Vector2(PicW * 0.5f, ground - 58f), new Vector2(PicW * 0.72f, ground - 58f), new Vector2(PicW * 0.8f, ground - 38f),
			new Vector2(PicW * 0.92f, ground - 34f), new Vector2(PicW * 0.93f, ground - 8f),
		};
		Poly(cv, body, Grey(0.5f));
		Poly(cv, new[] { new Vector2(PicW * 0.51f, ground - 54f), new Vector2(PicW * 0.6f, ground - 54f), new Vector2(PicW * 0.6f, ground - 39f), new Vector2(PicW * 0.46f, ground - 39f) }, Grey(0.2f));
		Poly(cv, new[] { new Vector2(PicW * 0.62f, ground - 54f), new Vector2(PicW * 0.71f, ground - 54f), new Vector2(PicW * 0.77f, ground - 39f), new Vector2(PicW * 0.62f, ground - 39f) }, Grey(0.2f));
		cv.Line(PicW * 0.36f, ground - 22f, PicW * 0.93f, ground - 22f, 0.6f, 0.6f, Grey(0.32f));   // the trim along its side
		cv.Rect(PicW * 0.36f, PicW * 0.93f, ground - 9f, ground - 6f, Grey(0.12f));   // its shadow under the sills
		foreach (float wx in new[] { PicW * 0.45f, PicW * 0.82f })
		{
			cv.Disc(wx, ground - 8f, 10f, Grey(0.06f));
			cv.Disc(wx, ground - 8f, 4.5f, Grey(0.45f));
		}
		// the trunk lid, up and back over the rear
		Poly(cv, new[] { new Vector2(PicW * 0.36f, ground - 36f), new Vector2(PicW * 0.44f, ground - 38f), new Vector2(PicW * 0.38f, ground - 66f), new Vector2(PicW * 0.32f, ground - 62f) }, Grey(0.56f));
		cv.Rect(PicW * 0.36f, PicW * 0.44f, ground - 38f, ground - 33f, Grey(0.08f));   // the dark of the open trunk
		// him: at the back of the car, bent over the trunk, the camera lifted out in both hands
		float fx = PicW * 0.29f;
		var coat = Grey(0.16f);
		cv.Line(fx - 2f, ground - 26f, fx - 4f, ground - 1f, 2.6f, 2.2f, Grey(0.22f));   // legs
		cv.Line(fx + 3f, ground - 26f, fx + 3f, ground - 1f, 2.6f, 2.2f, Grey(0.22f));
		cv.Line(fx, ground - 26f, fx + 5f, ground - 50f, 6f, 6.5f, coat);              // the body, leaning in
		var head = new Vector2(fx + 9f, ground - 57f);
		cv.Disc(head.X, head.Y, 4.6f, Grey(0.55f));
		cv.Line(head.X - 4f, head.Y - 3f, head.X + 4f, head.Y - 4f, 2f, 2f, Grey(0.12f));   // hair, a cap
		var hands = new Vector2(PicW * 0.37f, ground - 44f);
		cv.Line(fx + 6f, ground - 48f, hands.X, hands.Y, 1.8f, 1.5f, coat);              // the arms
		cv.Line(fx + 4f, ground - 45f, hands.X - 1f, hands.Y + 2f, 1.8f, 1.5f, coat);
		cv.Rect(hands.X - 5f, hands.X + 5f, hands.Y - 4f, hands.Y + 3f, Grey(0.07f));    // the camera
		cv.Disc(hands.X + 5.5f, hands.Y - 0.5f, 2.6f, Grey(0.1f));
		cv.Disc(hands.X + 5.5f, hands.Y - 0.5f, 1.1f, Grey(0.5f));
		cv.Rect(hands.X - 2f, hands.X + 1f, hands.Y - 6f, hands.Y - 4f, Grey(0.07f));
		cv.Line(fx - 1f, ground, fx + 26f, ground + 2f, 3f, 1f, Grey(0.28f));             // his shadow on the gravel
		return cv;
	}
}
