using Godot;

namespace ProjectDS.World;

/// <summary>
/// The board's darkroom (2026-10-16, STORY.md Rev 5, Act 1's photo board): what happens to the player's own prints, done on
/// the real pictures, never a flash. All of it works on a copy; the album's picture is never touched.
/// <list type="bullet">
/// <item><see cref="WithShape"/>: something tall and black, small, at a frame's edge (stage 2), or bigger and nearer (stage 4).
/// The stalker's shape: gaunt, high shoulders, a small head slung forward and low, arms past the knees, only the faint
/// pale plane of a face.</item>
/// <item><see cref="BlurMiddle"/>: a print slightly blurred in the middle, where nothing moved (stage 2).</item>
/// <item><see cref="Aged"/>: a print come out decades old, its colour gone, a little warped, with a figure standing in it
/// (stage 3, the falls).</item>
/// <item><see cref="StairsFromWindow"/>: the picture he doesn't remember taking: the stairs, through the cabin's window
/// (stage 5; Act 7's last print). Drawn here, the same every time.</item>
/// <item><see cref="TwoPersonPhoto"/>: the framed photo on the table, a man and a boy on the rock by the falls, the man
/// turned away; changed, it is old and grey and the man stands there alone.</item>
/// <item><see cref="Card"/>: a print as it comes out of the instant camera, its cream border round it.</item>
/// </list>
/// </summary>
public partial class PhotoBoard
{
	/// <summary>An editable RGBA copy of a texture's picture.</summary>
	public static Image Copy(Texture2D tex)
	{
		var src = tex?.GetImage();
		Image img = src != null && !src.IsEmpty() ? (Image)src.Duplicate() : Image.CreateEmpty(192, 128, false, Image.Format.Rgba8);
		if (img.IsCompressed()) img.Decompress();
		if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
		return img;
	}

	private static float Hash(int x, int y, int s)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + s * 2246822519u);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}

	/// <summary>Smooth value noise 0..1 (for blotches and fog).</summary>
	private static float Noise(float x, float y, int s)
	{
		int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
		float fx = x - ix, fy = y - iy;
		fx = fx * fx * (3f - 2f * fx);
		fy = fy * fy * (3f - 2f * fy);
		float a = Hash(ix, iy, s), b = Hash(ix + 1, iy, s), c = Hash(ix, iy + 1, s), d = Hash(ix + 1, iy + 1, s);
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
	}

	private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
	{
		Vector2 pa = p - a, ba = b - a;
		float t = Mathf.Clamp(pa.Dot(ba) / Mathf.Max(ba.LengthSquared(), 1e-6f), 0f, 1f);
		return (pa - ba * t).Length() - r;
	}

	private static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
	{
		var q = (p - c) / r;
		return (q.Length() - 1f) * Mathf.Min(r.X, r.Y);
	}

	private static void Blend(Image img, int x, int y, Color c, float a)
	{
		if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight() || a <= 0f) return;
		var o = img.GetPixel(x, y);
		img.SetPixel(x, y, new Color(Mathf.Lerp(o.R, c.R, a), Mathf.Lerp(o.G, c.G, a), Mathf.Lerp(o.B, c.B, a), 1f));
	}

	// ------------------------------------------------------------------ the shape

	/// <summary>
	/// The black shape stood in the picture: its feet at <paramref name="feet"/> (0..1 of the frame), <paramref name="height"/>
	/// of the frame tall, leaning toward the middle. <paramref name="fog"/> 0..1 fades it into the picture's own air (far off,
	/// at the trees' edge, more). Only the pale plane of its face shows, faintly.
	/// </summary>
	public static Image WithShape(Image src, Vector2 feet, float height, float fog = 0.2f)
	{
		var img = (Image)src.Duplicate();
		int w = img.GetWidth(), h = img.GetHeight();
		float H = height * h;
		var f = new Vector2(feet.X * w, feet.Y * h);
		float dir = feet.X > 0.5f ? -1f : 1f;   // (it leans in, toward the middle of the frame)
		Vector2 P(float x, float y) => f + new Vector2(x * dir * H, -y * H);
		var hipL = P(-0.05f, 0.48f); var hipR = P(0.05f, 0.48f);
		var kneeL = P(-0.07f, 0.25f); var kneeR = P(0.08f, 0.24f);
		var footL = P(-0.06f, 0f); var footR = P(0.07f, 0f);
		var hip = P(0f, 0.5f); var chest = P(0.04f, 0.72f);
		var shL = P(-0.09f, 0.8f); var shR = P(0.12f, 0.82f);
		var head = P(0.17f, 0.8f);
		var elL = P(-0.13f, 0.52f); var elR = P(0.17f, 0.54f);
		var hdL = P(-0.12f, 0.22f); var hdR = P(0.16f, 0.2f);
		var black = new Color(0.012f, 0.012f, 0.016f);
		var pale = new Color(0.5f, 0.48f, 0.45f);
		int x0 = Mathf.Max(0, Mathf.FloorToInt(f.X - H * 0.4f)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(f.X + H * 0.4f));
		int y0 = Mathf.Max(0, Mathf.FloorToInt(f.Y - H * 1.05f)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(f.Y + 1));
		float px1 = Mathf.Max(0.6f, H * 0.012f);
		for (int y = y0; y <= y1; y++)
			for (int x = x0; x <= x1; x++)
			{
				var p = new Vector2(x + 0.5f, y + 0.5f);
				float d = Capsule(p, hipL, kneeL, 0.03f * H);
				d = Mathf.Min(d, Capsule(p, kneeL, footL, 0.024f * H));
				d = Mathf.Min(d, Capsule(p, hipR, kneeR, 0.03f * H));
				d = Mathf.Min(d, Capsule(p, kneeR, footR, 0.024f * H));
				d = Mathf.Min(d, Capsule(p, hip, chest, 0.065f * H));
				d = Mathf.Min(d, Capsule(p, shL, shR, 0.045f * H));                       // the high shoulders
				d = Mathf.Min(d, Capsule(p, chest, head, 0.022f * H));                    // the thin neck, forward
				d = Mathf.Min(d, Ellipse(p, head, new Vector2(0.04f, 0.05f) * H));        // the small head, slung low
				d = Mathf.Min(d, Capsule(p, shL, elL, 0.02f * H));
				d = Mathf.Min(d, Capsule(p, elL, hdL, 0.017f * H));
				d = Mathf.Min(d, Capsule(p, shR, elR, 0.02f * H));
				d = Mathf.Min(d, Capsule(p, elR, hdR, 0.017f * H));
				float a = Mathf.Clamp(0.5f - d / px1, 0f, 1f);
				if (a <= 0f) continue;
				// the air in front of it: toward the picture's own colour, more at its feet (the ground mist)
				float low = Mathf.Clamp((p.Y - (f.Y - H * 0.3f)) / (H * 0.3f), 0f, 1f);
				float k = a * (1f - fog) * (1f - 0.35f * low);
				Blend(img, x, y, black, k);
				// the faint pale plane of its face
				float fd = Ellipse(p, head + new Vector2(dir * 0.012f * H, 0.004f * H), new Vector2(0.022f, 0.03f) * H);
				if (fd < 0f) Blend(img, x, y, pale, 0.22f * k);
			}
		return img;
	}

	// ------------------------------------------------------------------ the blur

	/// <summary>The middle of the frame blurred a little (a soft oval), the edges sharp.</summary>
	public static Image BlurMiddle(Image src, float radius = 0.024f)
	{
		var img = (Image)src.Duplicate();
		int w = img.GetWidth(), h = img.GetHeight();
		int r = Mathf.Max(1, Mathf.RoundToInt(radius * w));
		var tmp = new Color[w * h];
		var blur = new Color[w * h];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				Color s = default;
				for (int k = -r; k <= r; k++) s += src.GetPixel(Mathf.Clamp(x + k, 0, w - 1), y);
				tmp[y * w + x] = s / (2 * r + 1);
			}
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				Color s = default;
				for (int k = -r; k <= r; k++) s += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
				blur[y * w + x] = s / (2 * r + 1);
			}
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float u = (x + 0.5f) / w - 0.5f, v = (y + 0.5f) / h - 0.5f;
				float m = 1f - Mathf.SmoothStep(0.12f, 0.3f, new Vector2(u, v * 1.3f).Length());
				if (m > 0f) Blend(img, x, y, blur[y * w + x], m);
			}
		return img;
	}

	// ------------------------------------------------------------------ the old print

	/// <summary>
	/// Decades old: the colour gone to a warm grey, the blacks lifted and the whites dulled, blotched and faded at the edges,
	/// grain, a crease, the picture drawn a little sideways like a bad dream of itself. With <paramref name="figure"/>, a
	/// figure stands in it at (0..1) <paramref name="at"/>, <paramref name="height"/> tall: a man, still, dark grey.
	/// </summary>
	public static Image Aged(Image src, int seed, bool figure, Vector2 at = default, float height = 0.3f)
	{
		int w = src.GetWidth(), h = src.GetHeight();
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
		{
			// (the warp: each row drawn a pixel or two aside, slowly)
			float sx = Mathf.Sin(y * 0.11f + seed) * 1.4f + Mathf.Sin(y * 0.031f + seed * 2.3f) * 1.2f;
			for (int x = 0; x < w; x++)
			{
				var c = src.GetPixel(Mathf.Clamp(Mathf.RoundToInt(x + sx), 0, w - 1), y);
				float l = c.R * 0.3f + c.G * 0.55f + c.B * 0.15f;
				l = 0.2f + l * 0.6f;
				float u = (x + 0.5f) / w - 0.5f, v = (y + 0.5f) / h - 0.5f;
				float blot = Noise(x * 0.07f, y * 0.07f, seed) * 0.6f + Noise(x * 0.2f, y * 0.2f, seed + 9) * 0.4f;
				l += (blot - 0.5f) * 0.12f;
				l *= 1f - 0.45f * Mathf.SmoothStep(0.3f, 0.75f, new Vector2(u * 1.1f, v).Length());   // the edges gone dark
				l += (Hash(x, y, seed + 3) - 0.5f) * 0.06f;
				var o = new Color(l * 1.04f, l * 0.97f, l * 0.84f);
				img.SetPixel(x, y, o);
			}
		}
		// a crease across one corner
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7 + 1) };
		Vector2 ca = new(rng.RandfRange(0.55f, 0.8f) * w, 0), cb = new(w, rng.RandfRange(0.3f, 0.6f) * h);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float d = Mathf.Abs(Capsule(new Vector2(x + 0.5f, y + 0.5f), ca, cb, 0f));
				if (d < 1.2f) Blend(img, x, y, new Color(0.78f, 0.75f, 0.68f), 0.35f * (1f - d / 1.2f));
			}
		if (figure) Figure(img, at, height, new Color(0.1f, 0.095f, 0.09f), 0.85f, true);
		return img;
	}

	/// <summary>A plain standing figure (a man: a coat, his arms down), feet at (0..1) <paramref name="feet"/>.
	/// <paramref name="back"/>: turned away (no face).</summary>
	private static void Figure(Image img, Vector2 feet, float height, Color col, float alpha, bool back,
		Color? coat = null, Color? legs = null, Color? hair = null, Color? skin = null)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		float H = height * h;
		var f = new Vector2(feet.X * w, feet.Y * h);
		Vector2 P(float x, float y) => f + new Vector2(x * H, -y * H);
		int x0 = Mathf.Max(0, Mathf.FloorToInt(f.X - H * 0.3f)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(f.X + H * 0.3f));
		int y0 = Mathf.Max(0, Mathf.FloorToInt(f.Y - H * 1.02f)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(f.Y));
		float px1 = Mathf.Max(0.6f, H * 0.012f);
		for (int y = y0; y <= y1; y++)
			for (int x = x0; x <= x1; x++)
			{
				var p = new Vector2(x + 0.5f, y + 0.5f);
				float dLegs = Mathf.Min(Capsule(p, P(-0.05f, 0.46f), P(-0.06f, 0.02f), 0.045f * H), Capsule(p, P(0.05f, 0.46f), P(0.06f, 0.02f), 0.045f * H));
				float dCoat = Mathf.Min(Capsule(p, P(0f, 0.45f), P(0f, 0.78f), 0.11f * H), Capsule(p, P(-0.12f, 0.78f), P(0.12f, 0.78f), 0.05f * H));
				dCoat = Mathf.Min(dCoat, Mathf.Min(Capsule(p, P(-0.15f, 0.77f), P(-0.16f, 0.42f), 0.04f * H), Capsule(p, P(0.15f, 0.77f), P(0.16f, 0.42f), 0.04f * H)));
				float dHead = Ellipse(p, P(0f, 0.9f), new Vector2(0.065f, 0.08f) * H);
				float dNeck = Capsule(p, P(0f, 0.8f), P(0f, 0.86f), 0.035f * H);
				float d = Mathf.Min(Mathf.Min(dLegs, dCoat), Mathf.Min(dHead, dNeck));
				float a = Mathf.Clamp(0.5f - d / px1, 0f, 1f) * alpha;
				if (a <= 0f) continue;
				Color c = col;
				if (coat is { } cc)
				{
					if (dHead <= 0f || dNeck <= 0f) c = back ? hair ?? col : skin ?? col;
					else if (dCoat <= 0f) c = cc;
					else c = legs ?? cc;
					if (!back && dHead <= 0f && p.Y < P(0f, 0.95f).Y) c = hair ?? c;   // (his hair, over the face)
				}
				Blend(img, x, y, c, a);
			}
	}

	// ------------------------------------------------------------------ the stairs, through the window

	private static Image _stairs;

	/// <summary>
	/// The stairs at dusk in the fog, through the cabin's window: the dark window frame round the edge, its cross bar,
	/// the lamp's warm smear on the glass at the top left, trunks going grey into the fog, and in the middle distance the
	/// stone flight going up into nothing. Dark, like the album's misprinted stairs. Drawn once, the same every time.
	/// </summary>
	public static Image StairsFromWindow()
	{
		if (_stairs != null) return (Image)_stairs.Duplicate();
		const int w = 192, h = 128;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var fogCol = new Color(0.42f, 0.44f, 0.46f);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float v = (float)y / h;
				var c = fogCol.Lerp(new Color(0.16f, 0.17f, 0.16f), Mathf.SmoothStep(0.55f, 1f, v));
				c = c.Lerp(new Color(0.3f, 0.32f, 0.33f), (Noise(x * 0.05f, y * 0.12f, 41) - 0.5f) * 0.6f);
				img.SetPixel(x, y, c);
			}
		// trunks: near ones dark, far ones barely there
		var rng = new RandomNumberGenerator { Seed = 1987 };
		for (int i = 0; i < 16; i++)
		{
			float depth = rng.RandfRange(0.2f, 1f);
			float tx = rng.RandfRange(0f, w), tw = Mathf.Lerp(7f, 1.5f, depth);
			if (Mathf.Abs(tx - w * 0.56f) < 26f && depth > 0.4f) continue;   // (the way to the stairs is open)
			var tc = new Color(0.08f, 0.08f, 0.08f).Lerp(fogCol, depth * 0.85f);
			for (int y = 0; y < h; y++)
				for (int x = Mathf.FloorToInt(tx - tw); x <= Mathf.CeilToInt(tx + tw); x++)
				{
					float d = Mathf.Abs(x + 0.5f - tx - (y - h) * 0.01f * (i % 3 - 1)) - tw * 0.5f;
					float a = Mathf.Clamp(0.5f - d, 0f, 1f);
					Blend(img, x, y, tc, a);
				}
		}
		// the stairs: a stone flight between its two low walls, its foot toward us, rising into the fog
		const int steps = 12;
		float cx = w * 0.56f, baseY = h * 0.8f, topY = h * 0.34f;
		for (int s = 0; s < steps; s++)
		{
			float t0 = s / (float)steps, t1 = (s + 1) / (float)steps;
			float yb = Mathf.Lerp(baseY, topY, t0), yt = Mathf.Lerp(baseY, topY, t1);
			float hw0 = Mathf.Lerp(w * 0.13f, w * 0.045f, t0), hw1 = Mathf.Lerp(w * 0.13f, w * 0.045f, t1);
			float air = Mathf.Lerp(0.25f, 0.8f, t1);   // (the top lost in the fog)
			for (int y = Mathf.FloorToInt(yt); y <= Mathf.CeilToInt(yb); y++)
			{
				float k = Mathf.Clamp((yb - y) / Mathf.Max(1f, yb - yt), 0f, 1f);
				float hw = Mathf.Lerp(hw0, hw1, k);
				// the tread (lit from the sky a little) over the riser
				var c = k > 0.62f ? new Color(0.34f, 0.34f, 0.33f) : new Color(0.17f, 0.17f, 0.17f);
				for (int x = Mathf.FloorToInt(cx - hw - 3); x <= Mathf.CeilToInt(cx + hw + 3); x++)
				{
					float e = Mathf.Abs(x + 0.5f - cx);
					var cc = e > hw ? new Color(0.1f, 0.1f, 0.1f) : c;          // the low walls either side
					float a = Mathf.Clamp(hw + 3f - e + 0.5f, 0f, 1f) * (1f - air);
					Blend(img, x, y, cc, a);
				}
			}
		}
		// the window: the frame round the edge, a cross bar, the sill along the bottom, the lamp's smear on the glass
		var wood = new Color(0.05f, 0.04f, 0.035f);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				bool frame = x < 9 || x > w - 10 || y < 7 || y > h - 13 || Mathf.Abs(x - w * 0.27f) < 2.5f || Mathf.Abs(y - h * 0.47f) < 2f && x < w * 0.27f;
				if (frame) { Blend(img, x, y, wood, 0.96f); continue; }
				float g = Mathf.Max(0f, 1f - new Vector2((x - 30f) / 40f, (y - 22f) / 22f).Length());
				Blend(img, x, y, new Color(0.75f, 0.5f, 0.28f), g * 0.22f);
			}
		// dark, like the album's misprint, and its grain
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = img.GetPixel(x, y) * 0.62f;
				float n = (Hash(x, y, 77) - 0.5f) * 0.05f;
				img.SetPixel(x, y, new Color(c.R + n, c.G + n, c.B + n, 1f));
			}
		_stairs = img;
		return (Image)img.Duplicate();
	}

	// ------------------------------------------------------------------ the two of them

	/// <summary>
	/// The framed photo on the table (portrait): the falls behind, a man and a boy on the flat rock in front of them, a warm
	/// faded colour print. The man is turned away, looking at the water; the boy faces the camera. <paramref name="changed"/>:
	/// old and grey like the falls print, and the man stands there alone.
	/// </summary>
	public static Image TwoPersonPhoto(bool changed)
	{
		const int w = 100, h = 140, b = 5;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float u = (float)x / w, v = (float)y / h;
				// woods either side, the rock face behind the falls, the white water down the middle-right
				var c = new Color(0.22f, 0.27f, 0.15f).Lerp(new Color(0.42f, 0.4f, 0.22f), Noise(x * 0.12f, y * 0.12f, 5));
				float water = 1f - Mathf.SmoothStep(0.05f, 0.13f, Mathf.Abs(u - 0.6f - (v - 0.3f) * 0.05f));
				if (v < 0.62f)
				{
					var rock = new Color(0.3f, 0.29f, 0.27f).Lerp(new Color(0.2f, 0.19f, 0.18f), Noise(x * 0.2f, y * 0.08f, 9));
					c = c.Lerp(rock, Mathf.SmoothStep(0.25f, 0.45f, u) * (1f - Mathf.SmoothStep(0.78f, 0.95f, u)));
					c = c.Lerp(new Color(0.86f, 0.88f, 0.86f), water * (0.75f + 0.25f * Noise(x * 0.3f, y * 0.6f, 13)));
				}
				// the mist off the pool, and the flat rock they stand on
				if (v >= 0.62f) c = new Color(0.62f, 0.64f, 0.62f).Lerp(new Color(0.3f, 0.3f, 0.28f), Mathf.SmoothStep(0.62f, 0.72f, v));
				if (v > 0.72f) c = new Color(0.36f, 0.34f, 0.3f).Lerp(new Color(0.25f, 0.23f, 0.2f), Noise(x * 0.15f, y * 0.3f, 21));
				img.SetPixel(x, y, c);
			}
		var coat = new Color(0.5f, 0.16f, 0.12f);   // (a red plaid coat, worn)
		Figure(img, new Vector2(0.4f, 0.9f), 0.56f, coat, 1f, true, coat, new Color(0.2f, 0.24f, 0.32f), new Color(0.15f, 0.1f, 0.07f), new Color(0.75f, 0.58f, 0.46f));
		if (!changed)
			Figure(img, new Vector2(0.64f, 0.9f), 0.33f, coat, 1f, false, new Color(0.72f, 0.58f, 0.16f), new Color(0.22f, 0.25f, 0.32f), new Color(0.32f, 0.2f, 0.1f), new Color(0.82f, 0.64f, 0.52f));
		// the warm fade of a print from the eighties
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = img.GetPixel(x, y);
				img.SetPixel(x, y, c.Lerp(new Color(0.7f, 0.6f, 0.45f), 0.14f));
			}
		if (changed) img = Aged(img, 61, false);
		// the white border
		var paper = changed ? new Color(0.62f, 0.58f, 0.5f) : new Color(0.9f, 0.88f, 0.82f);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				if (x < b || y < b || x >= w - b || y >= h - b) img.SetPixel(x, y, paper);
		return img;
	}

	// ------------------------------------------------------------------ the card

	/// <summary>The instant camera's card round a picture: a cream border, deeper along the bottom.</summary>
	public static Image Card(Image photo)
	{
		int w = photo.GetWidth(), h = photo.GetHeight();
		int side = Mathf.Max(2, w / 18), top = side, bottom = Mathf.Max(4, h / 5);
		var img = Image.CreateEmpty(w + side * 2, h + top + bottom, false, Image.Format.Rgba8);
		var paper = new Color(0.9f, 0.89f, 0.84f);
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
				img.SetPixel(x, y, paper * (0.97f + Hash(x, y, 5) * 0.03f));
		img.BlitRect(photo, new Rect2I(0, 0, w, h), new Vector2I(side, top));
		return img;
	}
}
