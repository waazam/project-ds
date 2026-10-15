using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.LodgeParts;

/// <summary>
/// The ski lodge's interior surfaces (Act 23): the one place in the game that is rich, and a little mad with it
/// (the owner: more eccentric and high society than anything else, a stark contrast; The Shining in mind, made
/// ours, not copied). Small procedural textures in the game's PS2 manner (128-256 px, soft, a little grimy):
/// <list type="bullet">
/// <item><b>The corridor carpet</b>: ours, not the Overlook's hexagons: rows of nested pointed arches, like a
/// procession of doorways, in oxblood, burnt orange and near-black.</item>
/// <item><b>The rooms' carpet</b>: (since 2026-10-14h) tobacco brown with a quiet rust lattice (it was peacock scales in bottle green and bruise violet).</item>
/// <item><b>Wallpaper</b>: cream, a fine gold pinstripe and a wider band (now painted plaster); a darker damask (the bar's, until 2026-10-14h).</item>
/// <item><b>Bathroom tile</b>: mint-green squares, dark grout; a mustard trim.</item>
/// <item><b>Floors</b>: flagstone (the lobby), herringbone parquet (the dining hall), chequered linoleum (the
/// pantry), and rugs: a Navajo-pattern wool (since 2026-10-14h; a red and navy medallion before).</item>
/// <item><b>Knotty pine</b> panelling (the bar), a plaid bedspread and corduroy upholstery in 70s earth tones (2026-10-14h).</item>
/// </list>
/// </summary>
public static class LodgeTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, StandardMaterial3D> _mat = new();

	// ---------------------------------------------------------------- noise

	private static float Hash(int x, int y, int s)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + s * 982451653);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
	}

	/// <summary>Tileable value noise at a period of (px, py) cells.</summary>
	private static float Noise(float u, float v, int px, int py, int s)
	{
		float x = u * px, y = v * py;
		int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
		float fx = x - x0, fy = y - y0;
		fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
		float a = Hash(Mod(x0, px), Mod(y0, py), s), b = Hash(Mod(x0 + 1, px), Mod(y0, py), s);
		float c = Hash(Mod(x0, px), Mod(y0 + 1, py), s), d = Hash(Mod(x0 + 1, px), Mod(y0 + 1, py), s);
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
	}

	private static int Mod(int a, int m) => ((a % m) + m) % m;

	private static float Fbm(float u, float v, int s, int oct = 4, int basePeriod = 4)
	{
		float sum = 0, amp = 1, norm = 0; int p = basePeriod;
		for (int o = 0; o < oct; o++) { sum += amp * Noise(u, v, p, p, s + o * 31); norm += amp; amp *= 0.5f; p *= 2; }
		return sum / norm;
	}

	private static Texture2D Make(string key, int w, int h, Func<float, float, Color> f)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var __tg = Systems.TexGen.Start();
		if (Systems.TexCache.Load("LodgeTextures_" + key) is { } __cached) { t = ImageTexture.CreateFromImage(__cached); _tex[key] = t; return t; }
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = f((x + 0.5f) / w, (y + 0.5f) / h);
				c.A = 1f;
				img.SetPixel(x, y, c);
			}
		img.GenerateMipmaps();
		Systems.TexCache.Save("LodgeTextures_" + key, img);
		t = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
		_tex[key] = t;
		return t;
	}

	/// <summary>Wear: a soft mottle and a few darker specks, so nothing reads new.</summary>
	private static float Wear(float u, float v, int s) => 0.88f + 0.12f * Fbm(u, v, s) - (Hash((int)(u * 256), (int)(v * 256), s) > 0.985f ? 0.12f : 0f);

	// ---------------------------------------------------------------- textures

	/// <summary>The corridor's carpet: nested pointed arches in rows, each row offset by half, in oxblood, burnt
	/// orange and near-black. One tile = two arches across.</summary>
	public static Texture2D CorridorCarpet => Make("lodge_carpet_corridor", 256, 256, (u, v) =>
	{
		Color ox = new(0.36f, 0.07f, 0.06f), orange = new(0.62f, 0.27f, 0.09f), black = new(0.08f, 0.05f, 0.045f), gold = new(0.7f, 0.5f, 0.2f);
		float cu = u * 2f, cv = v * 2f;
		int row = Mathf.FloorToInt(cv);
		if (row % 2 == 1) cu += 0.5f;
		float fu = cu - Mathf.Floor(cu) - 0.5f, fv = cv - Mathf.Floor(cv);
		// a pointed arch: |x| against a curve that narrows to a point at the top
		float archW = 0.46f * Mathf.Pow(Mathf.Clamp(1f - (fv - 0.1f) / 0.9f, 0f, 1f), 0.55f);
		float d = archW - Mathf.Abs(fu);   // inside > 0
		Color c;
		if (d < 0) c = ox;
		else
		{
			float ring = Mathf.PosMod(d * 22f, 3f);
			c = ring < 1f ? orange : ring < 2f ? black : ox;
			if (d < 0.02f) c = gold;
		}
		if (fv < 0.1f) c = black;   // the ground line under each row of arches
		// the pile's weave and wear under the pattern (Poly Haven's dirty carpet)
		return c * Wear(u, v, 11) * (0.93f + 0.07f * Fbm(u * 3f, v * 3f, 12)) * SurfaceKit.Grain("carpet", u, v, 0.55f, 2f);
	});

	/// <summary>The rooms' carpet (2026-10-14h, the lodge's kit pass: it was peacock scales in bottle green and bruise
	/// violet, which fought every bed and rug laid on it): a 70s hotel's low pile in tobacco brown, a quiet lattice of
	/// rust and a harvest-gold dot where its lines cross. Low contrast: it reads as a carpet, not a pattern.</summary>
	public static Texture2D RoomCarpet => Make("lodge_carpet_room_70s", 256, 256, (u, v) =>
	{
		Color brown = new(0.2f, 0.13f, 0.08f), rust = new(0.3f, 0.15f, 0.08f), gold = new(0.38f, 0.28f, 0.12f), dark = new(0.13f, 0.085f, 0.06f);
		// a diagonal lattice, four diamonds to the tile
		float du = u * 4f + v * 4f, dv = u * 4f - v * 4f;
		float fu = du - Mathf.Floor(du), fv = dv - Mathf.Floor(dv);
		float line = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
		Color c = brown;
		if (line < 0.07f) c = rust;
		else if (line < 0.1f) c = dark;
		float cu = Mathf.Abs(fu - 0.5f), cv = Mathf.Abs(fv - 0.5f);
		if (cu > 0.42f && cv > 0.42f) c = gold;                         // where the lines cross
		else if (cu + cv < 0.12f) c = rust.Lerp(brown, 0.4f);            // a small diamond in each
		return c * Wear(u, v, 21) * (0.9f + 0.1f * Fbm(u * 2f, v * 2f, 23)) * SurfaceKit.Grain("carpet", u, v, 0.6f, 2f);
	});

	/// <summary>Cream wallpaper, a fine gold pinstripe and a wider soft band (rooms, the corridor).</summary>
	public static Texture2D Wallpaper => Make("lodge_wallpaper", 128, 128, (u, v) =>
	{
		// (aged and smoke-yellowed: the interiors pass, 2026-10-03; the cream read as a glare round the lamps)
		Color cream = new(0.6f, 0.54f, 0.43f), band = new(0.54f, 0.47f, 0.35f), gold = new(0.56f, 0.43f, 0.22f);
		float x = u * 4f % 1f;
		Color c = x < 0.34f ? band : cream;
		if (Mathf.Abs(x - 0.34f) < 0.012f || Mathf.Abs(x - 0.02f) < 0.01f) c = gold;
		return c * (0.9f + 0.1f * Fbm(u, v, 31)) * (0.95f + 0.05f * Fbm(u * 4f, v * 8f, 32)) * SurfaceKit.Grain("plaster", u, v, 0.18f, 2f);
	});

	/// <summary>The bar's walls: deep red damask with gold (a repeating teardrop medallion).</summary>
	public static Texture2D Damask => Make("lodge_damask", 256, 256, (u, v) =>
	{
		Color red = new(0.28f, 0.06f, 0.05f), deep = new(0.21f, 0.045f, 0.04f), gold = new(0.36f, 0.21f, 0.09f);   // (2026-10-14d: a quieter figure; the gold rings at full strength read as rows of eyes)
		float cu = u * 2f, cv = v * 2f;
		if (Mathf.FloorToInt(cv) % 2 == 1) cu += 0.5f;
		float fu = cu - Mathf.Floor(cu) - 0.5f, fv = cv - Mathf.Floor(cv) - 0.5f;
		float drop = new Vector2(fu * 1.6f, fv + Mathf.Abs(fu) * 0.6f).Length();
		Color c = drop < 0.28f ? (drop > 0.22f ? gold : deep) : red;
		if (Mathf.Abs(drop - 0.38f) < 0.015f) c = gold * 0.8f;
		// woven: a jacquard's figured weave under the medallions
		return c * (0.9f + 0.1f * Fbm(u, v, 41)) * SurfaceKit.Grain("jacquard", u, v, 0.45f, 2f);
	});

	/// <summary>Mint-green square tiles, dark grout (the bathrooms).</summary>
	public static Texture2D GreenTile => Make("lodge_greentile", 128, 128, (u, v) =>
	{
		float fu = u * 8f % 1f, fv = v * 8f % 1f;
		bool grout = fu < 0.06f || fv < 0.06f;
		Color mint = new Color(0.42f, 0.66f, 0.54f) * (0.92f + 0.08f * Hash((int)(u * 8), (int)(v * 8), 51));
		return grout ? new Color(0.16f, 0.2f, 0.18f) * SurfaceKit.Grain("plaster", u, v, 0.5f) : mint * (0.94f + 0.06f * Fbm(u, v, 52)) * SurfaceKit.Grain("plaster", u, v, 0.12f, 2f);
	});

	/// <summary>Flagstones (the lobby floor): Poly Haven's monastery floor, big irregular flags in dark grout, their
	/// brown cooled a little toward grey.</summary>
	public static Texture2D Flagstone => Make("lodge_flag", 256, 256, (u, v) =>
		SurfaceKit.Tinted("flagstone", u, v, new Color(0.98f, 0.95f, 0.92f), 0.4f, 0.6f) * (0.94f + 0.06f * Fbm(u, v, 63, 4, 4)));

	/// <summary>Herringbone parquet (the dining hall): Poly Haven's, in a darker, older honey.</summary>
	public static Texture2D Parquet => Make("lodge_parquet", 256, 256, (u, v) =>
		SurfaceKit.Tinted("parquet", u, v, new Color(1f, 0.86f, 0.7f), 0.3f) * (0.9f + 0.1f * Fbm(u, v, 74)));

	/// <summary>Chequered linoleum, black and cream, scuffed (the servants' pantry).</summary>
	public static Texture2D Checker => Make("lodge_checker", 128, 128, (u, v) =>
	{
		bool w = ((int)(u * 4f) + (int)(v * 4f)) % 2 == 0;
		Color c = w ? new Color(0.72f, 0.68f, 0.58f) : new Color(0.1f, 0.09f, 0.09f);
		return c * (0.88f + 0.12f * Fbm(u, v, 81)) * SurfaceKit.Grain("plaster", u, v, 0.4f);   // scuffed
	});

	/// <summary>The lodge's rugs (2026-10-14h, the kit pass; a red-and-navy medallion before): a Navajo-pattern wool rug, as
	/// every mountain lodge of the 70s had: a worn oatmeal field, terracotta and charcoal stepped diamonds, bands across its
	/// ends, a heart of faded turquoise. UVs 0..1 over the whole rug.</summary>
	public static Texture2D Rug => Make("lodge_rug_navajo", 256, 256, (u, v) =>
	{
		Color field = new(0.5f, 0.44f, 0.35f), terra = new(0.4f, 0.16f, 0.09f), coal = new(0.1f, 0.085f, 0.075f), teal = new(0.2f, 0.29f, 0.27f), cream = new(0.62f, 0.56f, 0.46f);
		// stepped: quantised to the weave's rows and columns
		float qx = Mathf.Floor(Mathf.Abs(u - 0.5f) * 32f) / 32f, qy = Mathf.Floor(Mathf.Abs(v - 0.5f) * 32f) / 32f;
		float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
		float ey = 0.5f - qy;
		Color c = field;
		// the bands across its ends
		if (ey < 0.05f) c = coal;
		else if (ey < 0.07f) c = cream;
		else if (ey < 0.11f) c = terra;
		else if (ey < 0.13f) c = cream;
		else if (ey < 0.15f) c = coal;
		else
		{
			// the stepped diamond in the middle: rings of terracotta and charcoal round a turquoise heart
			float d = qx * 1.5f + qy;
			if (d < 0.06f) c = teal;
			else if (d < 0.1f) c = cream;
			else if (d < 0.17f) c = terra;
			else if (d < 0.2f) c = coal;
			else if (d < 0.23f) c = cream;
			else if (d < 0.3f) c = terra;
			// half-diamonds at the sides, a stepped zigzag inside each end band
			float side = Mathf.Abs(qx - 0.5f) * 1.5f + qy;
			if (side < 0.12f) c = coal;
			else if (side < 0.16f) c = terra;
			float zig = Mathf.PosMod(qx * 8f, 1f);
			if (Mathf.Abs(ey - 0.19f - Mathf.Abs(zig - 0.5f) * 0.06f) < 0.012f) c = coal;
		}
		if (edge < 0.012f) c = coal;
		return c * (0.82f + 0.18f * Fbm(u, v, 91, 5, 16)) * SurfaceKit.Grain("carpet", u, v, 0.55f, 3f);
	});

	/// <summary>Dark stained wood panelling (the bar, the front desk, doors): Poly Haven's dark wood, a shade deeper.</summary>
	public static Texture2D DarkWood => Make("lodge_darkwood", 256, 256, (u, v) =>
		SurfaceKit.Tinted("darkwood", u, v, new Color(1f, 0.9f, 0.82f), 0.2f, 0.5f));   // (its red taken down: mahogany read orange)

	/// <summary>Split-log walls (the lobby's upper walls): rounded courses with dark chinking.</summary>
	public static Texture2D LogWall => Make("lodge_logwall", 256, 256, (u, v) =>
	{
		float f = v * 4f % 1f;
		float round = Mathf.Sin(f * Mathf.Pi);
		float chink = f < 0.06f || f > 0.94f ? 0.35f : 1f;
		float grain = (0.94f + 0.06f * Mathf.Sin(u * 60f + Fbm(u, v, 111) * 10f)) * SurfaceKit.Grain("pine", v * 0.25f, u, 0.6f);   // the grain along the log
		float tone = (0.34f + 0.16f * round) * grain * chink;
		return new Color(tone * 1.05f, tone * 0.82f, tone * 0.62f);   // weathered pine, not orange
	});

	/// <summary>(2026-10-14h, the kit pass) Knotty pine panelling, the 70s lodge's walls: tongue-and-groove boards set
	/// upright, six to the tile, each its own tone, a V-groove between them, dark knots here and there, aged to amber.</summary>
	public static Texture2D KnottyPine => Make("lodge_knottypine", 256, 256, (u, v) =>
	{
		float bu = u * 6f;
		int b = Mathf.FloorToInt(bu);
		float fu = bu - b;
		float tone = 0.8f + 0.25f * Hash(b, 0, 1201);
		float grain = SurfaceKit.Grain("pine", u * 0.5f + b * 0.137f, v * 0.6f, 0.75f);
		grain *= 0.94f + 0.06f * Mathf.Sin((fu * 5f + Fbm(u * 2f, v, 1202) * 6f) * Mathf.Pi);
		Color c = new Color(0.54f, 0.35f, 0.19f) * tone * grain;
		// knots: one or two to a board, in its middle third
		for (int kk = 0; kk < 2; kk++)
		{
			if (Hash(b, kk, 1203) > 0.62f) continue;
			float kv = Hash(b, kk, 1204), ku = 0.3f + 0.4f * Hash(b, kk, 1205);
			float dv = Mathf.PosMod(v - kv + 0.5f, 1f) - 0.5f;
			float r = new Vector2((fu - ku) / 6f * 1.4f, dv * 1.1f).Length() / (0.012f + 0.012f * Hash(b, kk, 1206));
			if (r < 1f) c *= 0.35f + 0.25f * r;
			else if (r < 2.4f) c *= 0.82f + 0.07f * (r - 1f);   // the grain darkened round it
		}
		// the V-groove
		float g = Mathf.Min(fu, 1f - fu);
		if (g < 0.03f) c *= 0.3f + 10f * g;
		return c * (0.92f + 0.08f * Fbm(u, v, 1207));
	});

	/// <summary>(2026-10-14h) The mountain's trail map, a printed poster gone yellow: a painted range under a grey sky, the
	/// runs down it in green, blue and black, the chair lifts' straight lines, the woods' dark below; a paper border.</summary>
	public static Texture2D TrailMap => Make("lodge_trailmap", 256, 192, (u, v) =>
	{
		Color sky = new Color(0.42f, 0.5f, 0.56f).Lerp(new Color(0.62f, 0.64f, 0.62f), v * 2.5f), snow = new(0.8f, 0.79f, 0.74f), rock = new(0.42f, 0.4f, 0.38f);
		Color tree = new(0.13f, 0.2f, 0.15f), valley = new(0.55f, 0.56f, 0.48f), paper = new(0.78f, 0.72f, 0.58f);
		float ridge = 0.2f + 0.13f * Mathf.Abs(Mathf.Sin(u * Mathf.Pi * 1.4f + 0.5f)) + 0.05f * Fbm(u * 2f, 0.3f, 1501);
		Color c;
		if (v < ridge) c = sky;
		else
		{
			float depth = v - ridge;
			float shade = 0.82f + 0.18f * Mathf.Sin(u * 40f + Fbm(u, v, 1502) * 9f);   // the painter's strokes down the slopes
			c = snow * shade;
			if (depth < 0.06f && Fbm(u * 3f, v * 3f, 1503) > 0.55f) c = rock;
			float treeLine = 0.36f + 0.08f * Fbm(u * 2f, 0.7f, 1504);
			if (depth > treeLine - ridge * 0.3f && Hash((int)(u * 128), (int)(v * 96), 1505) < 0.55f + (depth - treeLine) * 2f) c = tree * (0.8f + 0.4f * Hash((int)(u * 64), (int)(v * 48), 1506));
			if (v > 0.88f) c = valley;
			// the runs: winding down from the ridge
			foreach (var (x0, col, k) in new[] { (0.18f, new Color(0.14f, 0.42f, 0.2f), 3f), (0.34f, new Color(0.12f, 0.2f, 0.5f), 5f), (0.5f, new Color(0.04f, 0.04f, 0.04f), 7f), (0.66f, new Color(0.12f, 0.2f, 0.5f), 4f), (0.82f, new Color(0.14f, 0.42f, 0.2f), 6f) })
			{
				float x = x0 + 0.04f * Mathf.Sin(v * k * 3f) + (v - 0.5f) * 0.06f;
				if (Mathf.Abs(u - x) < 0.006f && v < 0.9f) c = col;
			}
			// two lifts, straight up from the valley
			foreach (var (a, b) in new[] { (0.26f, 0.42f), (0.6f, 0.74f) })
			{
				float t = (0.9f - v) / (0.9f - 0.3f), x = Mathf.Lerp(a, b, t);
				if (t > 0f && t < 1f && Mathf.Abs(u - x) < 0.003f) c = new Color(0.1f, 0.08f, 0.08f);
			}
		}
		// the title band and the paper's border
		if (v > 0.93f) c = new Color(0.36f, 0.12f, 0.08f);
		float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
		if (edge < 0.03f) c = paper;
		// yellowed, faded, foxed
		c = c.Lerp(paper, 0.25f) * (0.86f + 0.14f * Fbm(u, v, 1507)) * (Hash((int)(u * 256), (int)(v * 192), 1508) > 0.995f ? 0.7f : 1f);
		return c;
	});
	public static StandardMaterial3D TrailMapMat => Std("lodge_m_trailmap", TrailMap, 0.75f, 0.2f, new Color(0.85f, 0.82f, 0.76f));

	/// <summary>(2026-10-14h) A 70s bedspread: a muted plaid in rust, tobacco and oatmeal, a thread of olive.</summary>
	public static Texture2D Bedspread => Make("lodge_bedspread", 128, 128, (u, v) =>
	{
		static float Band(float t)
		{
			float f = t * 2f % 1f;
			return f < 0.32f ? 1f : f < 0.4f ? 0.2f : f < 0.46f ? 0.6f : f < 0.5f ? 0f : f < 0.56f ? 0.6f : 0f;
		}
		float a = Band(u), b = Band(v);
		Color oat = new(0.48f, 0.4f, 0.3f), rust = new(0.36f, 0.15f, 0.08f), tob = new(0.2f, 0.12f, 0.07f), olive = new(0.26f, 0.27f, 0.14f);
		Color c = oat.Lerp(rust, Mathf.Max(a, b) * 0.85f).Lerp(tob, a * b * 0.8f);
		if (Mathf.Abs(u * 2f % 1f - 0.43f) < 0.012f || Mathf.Abs(v * 2f % 1f - 0.43f) < 0.012f) c = olive;
		return c * (0.92f + 0.08f * Fbm(u, v, 1301)) * SurfaceKit.Grain("linen", u, v, 0.5f, 2f);
	});

	private static readonly Dictionary<string, StandardMaterial3D> _uph = new();
	/// <summary>(2026-10-14h, the kit pass: the violet velvet everywhere read magenta against the green carpet) The lodge's
	/// upholstery: a worn corduroy in one of the 70s' earth tones ("rust", "mustard", "olive", "oxblood", else tobacco).</summary>
	public static StandardMaterial3D Upholstery(string tone)
	{
		if (_uph.TryGetValue(tone, out var m)) return m;
		Color c = tone switch
		{
			"rust" => new Color(0.36f, 0.15f, 0.08f),
			"mustard" => new Color(0.44f, 0.32f, 0.11f),
			"olive" => new Color(0.24f, 0.25f, 0.13f),
			"oxblood" => new Color(0.26f, 0.08f, 0.07f),
			_ => new Color(0.24f, 0.15f, 0.09f),
		};
		var tex = Make("lodge_uph_" + tone, 256, 256, (u, v) => c * SurfaceKit.Grain("velvet", u, v, 0.7f) * (0.86f + 0.14f * Mathf.Abs(Mathf.Sin(u * 48f * Mathf.Pi))) * (0.9f + 0.1f * Fbm(u, v, 1401)));
		m = Std("lodge_m_uph_" + tone, tex, 0.95f, 0.12f, null, DetailKit.Kind.Fabric, "velvet", 0.35f, 0.5f);
		_uph[tone] = m;
		return m;
	}

	// ---------------------------------------------------------------- the crawlspace (Act 23: the cavity between the walls)

	/// <summary>The cavity's walls (the owner's references): rough vertical pine boards a hand wide, warm under the grime,
	/// dark gaps between them, dark spots and knots scattered over them (a 1 m tile).</summary>
	public static Texture2D CrawlBoards => Make("lodge_crawl_boards", 256, 256, (u, v) =>
	{
		float bu = u * 5f;
		int b = Mathf.FloorToInt(bu);
		float fu = bu - b;
		if (fu < 0.035f || fu > 0.975f) return new Color(0.03f, 0.025f, 0.02f);
		float off = Hash(b, 7, 301) * 0.8f;
		// (old, unfinished pine gone grey-brown in the dark, grimy: the interiors pass, 2026-10-03, the owner: the crawlspace's
		// textures mismatched; it was a bright, clean orange beside a red varnish and a grey floor)
		Color c = SurfaceKit.Tinted("old_planks", v * 0.5f + off, (b + fu) * 0.19f, new Color(0.86f, 0.7f, 0.52f), 0.3f, 0.5f);
		c *= 0.78f + 0.3f * Hash(b, 3, 302);
		float grime = Fbm(u * 0.7f, v * 1.3f, 306);
		c = c.Lerp(new Color(0.12f, 0.1f, 0.085f), Mathf.Clamp((grime - 0.42f) * 1.3f, 0f, 0.45f));
		// water stains: tide lines running down a board
		float tide = Mathf.Abs(Mathf.Sin((v + Hash(b, 9, 307)) * 9f + Fbm(u, v, 308) * 3f));
		if (Hash(b, 11, 309) > 0.6f && tide < 0.06f) c *= 0.7f;
		// dark spots and knots (nail holes, rot, the owner's pictures)
		for (int k = 0; k < 3; k++)
		{
			float ku = (b + 0.2f + 0.6f * Hash(b, k, 303)) / 5f, kv = Hash(b, k, 304);
			float d = new Vector2((u - ku) * 5f, Mathf.PosMod(v - kv + 0.5f, 1f) - 0.5f).Length();
			if (d < 0.05f) c *= 0.35f + 0.65f * (d / 0.05f);
		}
		return c * (0.9f + 0.1f * Fbm(u, v, 305));
	});

	/// <summary>Old red brick with smears of plaster over it (a stretch of the cavity's other side).</summary>
	public static Texture2D CrawlBrick => Make("lodge_crawl_brick", 256, 256, (u, v) =>
	{
		Color c = SurfaceKit.Tinted("crypt_brick", u, v, new Color(0.72f, 0.6f, 0.55f), 0.28f, 0.6f);
		// sooted and damp, like the boards round it
		float soot = Fbm(u * 0.8f, v * 0.8f, 312);
		c = c.Lerp(new Color(0.1f, 0.09f, 0.08f), Mathf.Clamp((soot - 0.4f) * 1.4f, 0f, 0.5f));
		float plaster = Fbm(u, v, 311, 4, 3);
		if (plaster > 0.6f) c = c.Lerp(new Color(0.32f, 0.3f, 0.27f) * SurfaceKit.Grain("plaster", u, v, 0.6f), Mathf.Clamp((plaster - 0.6f) * 5f, 0f, 0.85f));
		return c;
	});

	/// <summary>The cavity's floor: rough planks, grey with dust.</summary>
	public static Texture2D CrawlFloor => Make("lodge_crawl_floor", 256, 256, (u, v) =>
	{
		// the same pine as the walls, walked on, dust lying in its grain
		Color c = SurfaceKit.Tinted("old_planks", u, v, new Color(0.8f, 0.66f, 0.5f), 0.26f, 0.5f);
		float dust = Fbm(u, v, 321);
		return c.Lerp(new Color(0.26f, 0.23f, 0.2f), Mathf.Clamp((dust - 0.45f) * 1.6f, 0f, 0.45f));
	});

	public static StandardMaterial3D CrawlBoardsMat => _hCrawlBoardsMat ??= World.SurfaceSets.Hinoki(Std("lodge_m_crawl_boards", CrawlBoards, 0.9f, 0.2f, null, DetailKit.Kind.Wood, null, 0.5f, 1f), new Color(0.72f, 0.62f, 0.52f));
	private static StandardMaterial3D _hCrawlBoardsMat;
	public static StandardMaterial3D CrawlBrickMat => Std("lodge_m_crawl_brick", CrawlBrick, 0.95f, 0.15f, null, DetailKit.Kind.Stone, null, 0.5f, 1f);
	public static StandardMaterial3D CrawlFloorMat => _hCrawlFloorMat ??= World.SurfaceSets.Hinoki(Std("lodge_m_crawl_floor", CrawlFloor, 0.95f, 0.15f, null, DetailKit.Kind.Wood, null, 0.5f, 1f), new Color(0.62f, 0.52f, 0.44f));
	private static StandardMaterial3D _hCrawlFloorMat;
	public static StandardMaterial3D CrawlCeilingMat => _hCrawlCeilingMat ??= World.SurfaceSets.Hinoki(Std("lodge_m_crawl_ceiling", CrawlBoards, 0.95f, 0.1f, new Color(0.55f, 0.52f, 0.48f), DetailKit.Kind.Wood, null, 0.5f, 1f), new Color(0.48f, 0.43f, 0.38f));
	private static StandardMaterial3D _hCrawlCeilingMat;
	/// <summary>The cavity's studs and joists: the same pine as its boards, darker (it was the rooms' red varnish).</summary>
	public static StandardMaterial3D CrawlStudMat => _hCrawlStudMat ??= World.SurfaceSets.Hinoki(Std("lodge_m_crawl_stud", CrawlBoards, 0.95f, 0.1f, new Color(0.62f, 0.56f, 0.5f), DetailKit.Kind.Wood, null, 0.5f, 1f), new Color(0.56f, 0.48f, 0.42f));
	private static StandardMaterial3D _hCrawlStudMat;
	/// <summary>The cavity's pipes: old iron, rusted and matte (the rooms' iron caught the light in a blue line along the top).</summary>
	/// <summary>The crawlspace's wood by its age (0 the lodge's own boards; 1 old grey planks gone soft, Poly Haven's
	/// Weathered Brown Planks; 2 the same black with rot and furred with mould: a mould layer over it as a detail).</summary>
	public static StandardMaterial3D CrawlAged(string part, int tier)
	{
		if (tier <= 0) return part switch { "floor" => CrawlFloorMat, "ceiling" => CrawlCeilingMat, "stud" => CrawlStudMat, _ => CrawlBoardsMat };
		string key = $"lodge_m_crawl_{part}_age{tier}";
		if (_mat.TryGetValue(key, out var m)) return m;
		var tint = tier == 1 ? new Color(0.66f, 0.62f, 0.56f) : new Color(0.38f, 0.37f, 0.32f);
		if (part == "ceiling") tint *= 0.8f;
		if (part == "floor") tint *= 0.85f;
		m = new StandardMaterial3D { ResourceName = key, AlbedoColor = tint, Roughness = 1f, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true,
			Uv1Scale = Vector3.One * (part == "floor" ? 1f : 0.5f), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic };
		World.SurfaceSets.Apply(m, "ph_weathered_brown_planks", 1.1f);
		if (m.AlbedoTexture == null) m.AlbedoTexture = CrawlBoards;
		if (tier >= 2)
		{
			m.DetailEnabled = true;
			m.DetailAlbedo = Mould;
			m.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mix;
			m.DetailUVLayer = BaseMaterial3D.DetailUV.UV1;
		}
		m.SetMeta("detail_kind", -1);
		_mat[key] = m;
		return m;
	}

	private static Texture2D _mould;
	/// <summary>Mould: black-green blooms with grey fur at their edges, water-dark between; its alpha where it's grown.</summary>
	private static Texture2D Mould => _mould ??= MakeMould();

	private static Texture2D MakeMould()
	{
		var ramp = new Gradient();
		ramp.SetColor(0, new Color(0.1f, 0.09f, 0.07f, 0f));
		ramp.SetColor(1, new Color(0.03f, 0.04f, 0.025f, 0.95f));
		ramp.AddPoint(0.48f, new Color(0.1f, 0.09f, 0.07f, 0f));
		ramp.AddPoint(0.56f, new Color(0.34f, 0.36f, 0.3f, 0.55f));
		ramp.AddPoint(0.64f, new Color(0.12f, 0.17f, 0.08f, 0.85f));
		ramp.AddPoint(0.78f, new Color(0.05f, 0.08f, 0.04f, 0.92f));
		return new NoiseTexture2D
		{
			Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true, ColorRamp = ramp,
			Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.02f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 4, Seed = 1408,
				CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Div },
		};
	}

	public static StandardMaterial3D CrawlPipeMat => _plain("lodge_m_crawl_pipe", new Color(0.2f, 0.13f, 0.09f), 0.85f, 0.15f, 0.3f);
	public static StandardMaterial3D CopperMat => _plain("lodge_m_copper", new Color(0.42f, 0.26f, 0.16f), 0.45f, 0.6f, 0.6f);
	/// <summary>The frozen lodge's rime (Act 23): a matte, patchy frost over the rooms' surfaces (the snow's grain, world
	/// mapped, faint and rough: a glassy glaze over everything read as a grey mirror with the lamps glaring in it).</summary>
	public static StandardMaterial3D FrostOverlay => _frost ??= new StandardMaterial3D
	{
		ResourceName = "lodge_frost", AlbedoColor = new Color(0.86f, 0.91f, 1f, 0.16f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		Roughness = 0.95f, MetallicSpecular = 0.1f, Grow = true, GrowAmount = 0.004f,
	};
	private static StandardMaterial3D _frost;

	/// <summary>Light between the boards (a room lit on the other side).</summary>
	public static StandardMaterial3D LeakMat => Glow("lodge_leak", new Color(1f, 0.8f, 0.5f), 1.4f);

	// ---------------------------------------------------------------- materials

	private static StandardMaterial3D Std(string key, Texture2D tex, float rough = 0.85f, float spec = 0.3f, Color? tint = null, DetailKit.Kind? detail = null,
		string relief = null, float reliefStrength = 0.6f, float metresPerTile = 1f)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D
		{
			ResourceName = key, AlbedoTexture = tex, AlbedoColor = tint ?? Colors.White, Roughness = rough, MetallicSpecular = spec,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true,
			Uv1Scale = Vector3.One / metresPerTile,
		};
		// the photo's own relief, at its repeat (before the detail, which keeps a normal map it finds)
		if (relief != null) SurfaceKit.Relief(m, relief, reliefStrength);
		if (detail is { } d) DetailKit.Apply(m, d);
		else m.SetMeta("detail_kind", -1);   // none, on purpose (the sweep would otherwise give it one by its node's name)
		_mat[key] = m;
		return m;
	}

	// (the wallpaper, the plaster and the ceilings take no detail layer: it shares their UVs, a tile of two or three
	// metres, and blown up that much its fine grime showed as metre-wide stains; the photo's grain does its work)
	public static StandardMaterial3D CorridorCarpetMat => Std("lodge_m_carpet_corr", CorridorCarpet, 1f, 0.1f, null, DetailKit.Kind.Fabric);
	public static StandardMaterial3D RoomCarpetMat => Std("lodge_m_carpet_room", RoomCarpet, 1f, 0.1f, null, DetailKit.Kind.Fabric);
	/// <summary>(2026-10-10c, the owner: "that wallpaper of the hotel needs a big tweak") The papers drawn by
	/// tools/textures/make_wallpapers.py, a metre to the tile with a flocked relief: the damask (the corridor, the dining
	/// hall) and a paper of its own in each room.</summary>
	public static StandardMaterial3D WallpaperMat => Paper("damask");
	public static StandardMaterial3D RoomPaperMat(int room) => Paper(room switch { 201 => "stripe201", 202 => "floral", 203 => "trellis", _ => "stripe" });   // (201 its own: the perfect room is left unweathered, SkiLodge.Kit)

	private static StandardMaterial3D Paper(string set)
	{
		string key = "lodge_m_wp_" + set;
		if (_mat.TryGetValue(key, out var m)) return m;
		// (2026-10-12, the owner: "throughout the ski lodge there are walls with the greyish wallpapers ... do a more
		// simplistic design. what we have now looks bad") no pattern any more: plain painted plaster over the wood panelling,
		// a quiet colour to each part of the lodge (warm cream in the corridor and the dining hall, sage in 201, dusty rose in
		// 202, ochre in 203), the plaster's own faint texture all the wall shows
		Color paint = set switch
		{
			"stripe" or "stripe201" => new Color(0.46f, 0.49f, 0.39f),   // (2026-10-13: each a step darker and dirtier: in the lantern's glow the pale paint went flat white)
			"floral" => new Color(0.49f, 0.42f, 0.32f),   // (2026-10-14h: an oatmeal; the dusty rose read mauve beside the violet)
			"trellis" => new Color(0.57f, 0.47f, 0.33f),
			_ => new Color(0.6f, 0.52f, 0.4f),
		};
		m = new StandardMaterial3D { ResourceName = key, Roughness = 0.92f, MetallicSpecular = 0.12f, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Uv1Scale = Vector3.One * 0.8f, AlbedoColor = paint };
		SurfaceSets.Apply(m, "plaster", 0.35f);
		if (m.AlbedoTexture == null) m.AlbedoTexture = Wallpaper;
		m.SetMeta("detail_kind", -1);
		_mat[key] = m;
		return m;
	}
	public static StandardMaterial3D DamaskMat => Std("lodge_m_damask", Damask, 0.8f, 0.25f, null, DetailKit.Kind.Fabric, "jacquard", 0.35f, 0.42f);   // (2026-10-14d: a hand-span figure, not half a metre)
	public static StandardMaterial3D GreenTileMat => Std("lodge_m_greentile", GreenTile, 0.25f, 0.6f);
	public static StandardMaterial3D FlagstoneMat => Std("lodge_m_flag", Flagstone, 0.8f, 0.25f, null, DetailKit.Kind.Stone, "flagstone", 0.8f);
	public static StandardMaterial3D ParquetMat => _hParquetMat ??= World.SurfaceSets.Hinoki(Std("lodge_m_parquet", Parquet, 0.5f, 0.45f, null, DetailKit.Kind.Wood, "parquet", 0.5f), new Color(0.7f, 0.52f, 0.38f));
	private static StandardMaterial3D _hParquetMat;
	// (aged and dimmer: the white tiles blew out under the lantern a step away, 2026-10-10c)
	public static StandardMaterial3D CheckerMat => Std("lodge_m_checker", Checker, 0.7f, 0.3f, new Color(0.68f, 0.64f, 0.56f));
	public static StandardMaterial3D RugMat => Std("lodge_m_rug", Rug, 1f, 0.1f, null, DetailKit.Kind.Fabric);
	// (baked at 1024 px since 2026-10-09: the owner, "the wood in ... the ski lodge looks pretty bad")
	// (2026-10-10b: the hinoki, its woodwork a rich dark-stained tone of it, its walls the honey of the bare wood)
	/// <summary>(2026-10-14d) The guest rooms' doors: the same dark wood, but one smooth varnished leaf, no planks; the rooms'
	/// doors drew the planked dark wood of the wainscot beside them, and read as more of the wall.</summary>
	public static StandardMaterial3D DoorWoodMat => Std("lodge_m_doorwood", DarkWood, 0.42f, 0.42f, new Color(0.5f, 0.36f, 0.27f), DetailKit.Kind.Wood, "darkwood", 0.35f, 1.6f);
	public static StandardMaterial3D DarkWoodMat =>_darkM ??= World.SurfaceSets.Hinoki(Std("lodge_m_darkwood", DarkWood, 0.45f, 0.45f, null, DetailKit.Kind.Wood, "darkwood", 0.4f), new Color(0.6f, 0.42f, 0.3f), 8f);
	/// <summary>(2026-10-14h) Knotty pine panelling (the bar, the mudroom): UVs in metres, 15 cm boards.</summary>
	public static StandardMaterial3D KnottyPineMat => Std("lodge_m_knottypine", KnottyPine, 0.72f, 0.28f, null, DetailKit.Kind.Wood, "pine", 0.4f, 0.9f);
	public static StandardMaterial3D BedspreadMat => Std("lodge_m_bedspread", Bedspread, 0.95f, 0.1f, null, DetailKit.Kind.Fabric, "linen", 0.5f, 0.7f);
	public static StandardMaterial3D LogWallMat => _logwallM ??= World.SurfaceSets.Hinoki(Std("lodge_m_logwall", LogWall, 0.8f, 0.25f, null, DetailKit.Kind.Wood), new Color(0.95f, 0.84f, 0.7f), 4f);
	private static StandardMaterial3D _darkM, _logwallM;

	private static StandardMaterial3D _plain(string key, Color c, float rough, float spec, float metal = 0f)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D { ResourceName = key, AlbedoColor = c, Roughness = rough, MetallicSpecular = spec, Metallic = metal, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true };
		_mat[key] = m;
		return m;
	}

	public static StandardMaterial3D GoldMat => _plain("lodge_m_gold", new Color(0.72f, 0.54f, 0.24f), 0.3f, 0.8f, 0.8f);
	public static StandardMaterial3D BrassMat => _plain("lodge_m_brass", new Color(0.62f, 0.48f, 0.24f), 0.35f, 0.7f, 0.7f);
	/// <summary>(2026-10-14h) Old green enamel (the bar's pendant shades), chipped dull.</summary>
	public static StandardMaterial3D EnamelMat => _plain("lodge_m_enamel_green", new Color(0.13f, 0.2f, 0.14f), 0.4f, 0.45f);
	public static StandardMaterial3D MustardMat => _plain("lodge_m_mustard", new Color(0.72f, 0.54f, 0.2f), 0.6f, 0.3f);
	public static StandardMaterial3D PorcelainMat => _plain("lodge_m_porcelain", new Color(0.84f, 0.86f, 0.82f), 0.15f, 0.6f);
	public static StandardMaterial3D MintPorcelainMat => _plain("lodge_m_mint_porcelain", new Color(0.55f, 0.74f, 0.64f), 0.15f, 0.6f);
	// the upholstery, the linen, the plaster and the fireplace: Poly Haven's photo surfaces (a tile a metre or so)
	public static Texture2D Leather => Make("lodge_leather", 256, 256, (u, v) => SurfaceKit.Tinted("leather", u, v, new Color(1.25f, 0.62f, 0.4f), 0.24f, 0.6f));
	public static Texture2D Velvet => Make("lodge_velvet", 256, 256, (u, v) => new Color(0.36f, 0.14f, 0.4f) * SurfaceKit.Grain("velvet", u, v, 0.8f));
	public static Texture2D Linen => Make("lodge_linen", 256, 256, (u, v) => new Color(0.8f, 0.78f, 0.72f) * SurfaceKit.Grain("linen", u, v, 0.9f));
	public static Texture2D Plaster => Make("lodge_plaster", 256, 256, (u, v) => Colors.White * SurfaceKit.Grain("plaster", u, v, 0.3f, 2f) * (0.96f + 0.04f * Fbm(u, v, 121)));
	public static Texture2D FireStone => Make("lodge_firestone", 256, 256, (u, v) => SurfaceKit.Tinted("fireplace_stone", u, v, new Color(0.96f, 0.94f, 0.92f), 0.46f, 0.55f));
	public static StandardMaterial3D LeatherMat => Std("lodge_m_leather", Leather, 0.55f, 0.35f, null, DetailKit.Kind.Leather, "leather", 0.5f, 0.6f);
	public static StandardMaterial3D VelvetVioletMat => Std("lodge_m_velvet", Velvet, 0.9f, 0.15f, null, DetailKit.Kind.Fabric, "velvet", 0.4f, 0.5f);
	public static StandardMaterial3D LinenMat => Std("lodge_m_linen", Linen, 0.95f, 0.1f, null, DetailKit.Kind.Fabric, "linen", 0.5f, 0.6f);
	/// <summary>The sheet figure's linen (once the dining tables' sheets).</summary>
	public static StandardMaterial3D SheetLinenMat => _sheet ??= SheetFrom(LinenMat);
	/// <summary>The tablecloths (the owner chose it: Poly Haven's Quatrefoil Jacquard Fabric, CC0): a crimson jacquard,
	/// its quatrefoil figure in the weave's relief, a little sheen. Two-sided (a cloth's underside shows as it falls).
	/// UVs in metres.</summary>
	public static StandardMaterial3D TableclothMat => _tablecloth ??= MakeTablecloth();
	private static StandardMaterial3D _tablecloth;
	private static StandardMaterial3D MakeTablecloth()
	{
		var m = new StandardMaterial3D
		{
			ResourceName = "m_tablecloth_jacquard",
			AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/surfaces/tablecloth_albedo.png"),
			NormalEnabled = true, NormalTexture = GD.Load<Texture2D>("res://assets/textures/surfaces/tablecloth_normal.png"), NormalScale = 1.2f,
			// (a shade darker, and little sheen: close under the lantern the crimson washed out to pink)
			AlbedoColor = new Color(0.62f, 0.6f, 0.6f), Roughness = 0.82f, MetallicSpecular = 0.12f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
			Uv1Scale = Vector3.One,   // (the photo's own scale: a metre a repeat)
		};
		m.SetMeta("detail_kind", -1);
		return m;
	}
	private static StandardMaterial3D _sheet;
	private static StandardMaterial3D SheetFrom(StandardMaterial3D m)
	{
		var s = (StandardMaterial3D)m.Duplicate();
		s.ResourceName = "lodge_m_linen_sheet";
		// (2026-10-15, the scan pass: it was 14 x 4, for a sheet whose UVs ran 0..1 across it; its one user now, the sheet
		// figure, has UVs in metres, and the weave stretched 3.5 : 1. The linen's own scale, the same both ways)
		s.Uv1Scale = new Vector3(1f / 0.6f, 1f / 0.6f, 1f);
		return s;
	}
	/// <summary>(2026-10-15, the scan pass) The dust sheets over the dining hall's chairs: the linen, a little dingier,
	/// two-sided (the inside of the hem shows from low down). UVs in metres.</summary>
	public static StandardMaterial3D ChairSheetMat => _chairSheet ??= ChairSheetFrom(LinenMat);
	private static StandardMaterial3D _chairSheet;
	private static StandardMaterial3D ChairSheetFrom(StandardMaterial3D m)
	{
		var s = (StandardMaterial3D)m.Duplicate();
		s.ResourceName = "lodge_m_linen_dust_sheet";
		s.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		s.AlbedoColor = new Color(0.86f, 0.84f, 0.79f);
		return s;
	}
	public static StandardMaterial3D PillowMat => _plain("lodge_m_pillow", new Color(0.8f, 0.78f, 0.72f), 0.95f, 0.1f);
	public static StandardMaterial3D BlackMat => _plain("lodge_m_black", new Color(0.035f, 0.03f, 0.03f), 0.6f, 0.3f);
	public static StandardMaterial3D IronMat => _plain("lodge_m_iron", new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.5f, 0.6f);
	/// <summary>Old silver plate: tarnished, soft-sheened (it catches the lamps without mirroring the dark).</summary>
	public static StandardMaterial3D SilverMat => _plain("lodge_m_silver", new Color(0.62f, 0.6f, 0.56f), 0.32f, 0.7f, 0.85f);
	public static StandardMaterial3D MirrorMat => _plain("lodge_m_mirror", new Color(0.4f, 0.42f, 0.45f), 0.02f, 1f, 1f);
	// (2026-10-13, the look audit: were 0.74, 0.7, 0.62 and 0.62, 0.58, 0.5: by the lantern the back rooms' plaster went flat white)
	// (2026-10-13c, the look audit: the mudroom's inner wall, the service corridor's and the pantry's, read as a glowing
	// white panel: the plaster was a flat, near-even tint with no relief and no roughness of its own, so it took every lamp
	// and the lantern as one even sheet, a step brighter than the boards round it. Now it's painted plaster as the rooms'
	// walls are (the plaster scan's relief and roughness under a paint), in a dirtier back-of-house cream a step darker
	// than the corridor's)
	public static StandardMaterial3D PlasterMat => _plasterM ??= PaintedPlaster("lodge_m_plaster", new Color(0.5f, 0.45f, 0.36f), 0.55f);
	private static StandardMaterial3D _plasterM;

	private static StandardMaterial3D PaintedPlaster(string key, Color paint, float relief)
	{
		var m = new StandardMaterial3D
		{
			ResourceName = key, AlbedoColor = paint, Roughness = 0.95f, MetallicSpecular = 0.1f, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true,
			Uv1Scale = Vector3.One * 0.8f,
		};
		SurfaceSets.Apply(m, "plaster", relief);
		if (m.AlbedoTexture == null) m.AlbedoTexture = Plaster;
		m.SetMeta("detail_kind", -1);
		return m;
	}
	public static StandardMaterial3D CeilingMat => Std("lodge_m_ceiling", Plaster, 0.95f, 0.1f, new Color(0.52f, 0.48f, 0.42f), null, null, 0.3f, 2f);
	/// <summary>The lobby's lower walls inside: Poly Haven's stacked stone, cool and even (the outside's mossy stone
	/// read as dirt in here).</summary>
	public static Texture2D LobbyStone => Make("lodge_lobbystone", 256, 256, (u, v) => SurfaceKit.Tinted("lobby_stone", u, v, new Color(0.98f, 0.96f, 0.94f), 0.36f, 0.5f));
	// (2026-10-10c, the owner: "i also hate this grey drab bland stone color of the room lets change that to feel more
	// modern"): the lobby's lower walls warm honey boards, its chimney breast warm stacked stone (Poly Haven's Rustic Stone
	// Wall), its floor wide warm planks (Poly Haven's Wood Floor)
	private static StandardMaterial3D _lobbyWallM, _riverM, _lobbyFloorM;
	public static StandardMaterial3D LobbyStoneMat => _lobbyWallM ??= World.SurfaceSets.Hinoki(Std("lodge_m_lobbywall", LobbyStone, 0.85f, 0.25f, null, DetailKit.Kind.Wood), new Color(1f, 0.86f, 0.68f), 6f);
	public static StandardMaterial3D RiverStoneMat => _riverM ??= Scan("lodge_m_riverstone", "ph_rustic_stone_wall", 1.6f, new Color(1f, 0.94f, 0.86f)) ?? Std("lodge_m_riverstone_old", FireStone, 0.9f, 0.2f, null, DetailKit.Kind.Stone, "fireplace_stone", 0.9f, 1.6f);
	public static StandardMaterial3D LobbyFloorMat => _lobbyFloorM ??= Scan("lodge_m_lobbyfloor", "ph_wood_floor", 0.5f, new Color(0.95f, 0.85f, 0.72f)) ?? FlagstoneMat;

	/// <summary>A scanned set (tools/textures/polyhaven_sets.py) at <paramref name="uvScale"/> repeats per UV unit, tinted.</summary>
	private static StandardMaterial3D Scan(string key, string set, float uvScale, Color tint)
	{
		if (World.SurfaceSets.Tex(set) == null) return null;
		var m = new StandardMaterial3D { ResourceName = key, AlbedoColor = tint, Roughness = 1f, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Uv1Scale = Vector3.One * uvScale };
		World.SurfaceSets.Apply(m, set, 0.9f);
		m.SetMeta("detail_kind", -1);
		return m;
	}

	/// <summary>Lit from within (lamp shades, the bar's backlit counter, candle glass): warm, never harsh.</summary>
	public static StandardMaterial3D Glow(string key, Color c, float energy)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D { ResourceName = key, AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.6f };
		_mat[key] = m;
		return m;
	}

	/// <summary>Daylight through a window's snow-dusted glass (inside faces): pale and grey-blue, dimmed by the storm.</summary>
	public static StandardMaterial3D DayGlass => _day ??= new StandardMaterial3D
	{
		// (2026-10-14, the interior pass: darker glass, grimed (it caught the lantern as a white sheet); its glow is set by the
		// storm, SkiLodge.Act23)
		ResourceName = "lodge_dayglass", AlbedoColor = new Color(0.2f, 0.22f, 0.26f), EmissionEnabled = true, Emission = new Color(0.46f, 0.52f, 0.62f), EmissionEnergyMultiplier = 0.13f, Roughness = 0.45f,   // (2026-10-14d: a storm's grey light, not a white sheet in the wall)
		AlbedoTexture = PaneTex, EmissionTexture = PaneTex, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	};
	/// <summary>A pane seen from inside: clear (top half) or with the snow drifted up it (bottom half).</summary>
	public static Texture2D PaneTex => ResourceLoader.Exists("res://assets/textures/lodge/window_pane.png") ? GD.Load<Texture2D>("res://assets/textures/lodge/window_pane.png") : null;
	private static StandardMaterial3D _day, _blue, _ice;

	/// <summary>Room 202's window: snow banked right up the glass outside; the light through it blue.</summary>
	public static StandardMaterial3D SnowBlueGlass => _blue ??= new StandardMaterial3D
	{
		ResourceName = "lodge_blueglass", AlbedoTexture = WinterWoods.SnowStd("x", 1f, 1f).AlbedoTexture, AlbedoColor = new Color(0.45f, 0.58f, 0.8f),
		EmissionEnabled = true, Emission = new Color(0.3f, 0.42f, 0.66f), EmissionEnergyMultiplier = 0.28f, Roughness = 0.5f,   // (0.8 until the interior pass: a lit blue screen)
	};

	/// <summary>Room 203's window: frosted over, ice feathering across it.</summary>
	public static StandardMaterial3D IceGlass => _ice ??= new StandardMaterial3D
	{
		ResourceName = "lodge_iceglass", AlbedoTexture = WinterWoods.SnowStd("y", 1f, 1f).AlbedoTexture, AlbedoColor = new Color(0.7f, 0.78f, 0.86f),
		EmissionEnabled = true, Emission = new Color(0.5f, 0.57f, 0.66f), EmissionEnergyMultiplier = 0.2f, Roughness = 0.4f,   // (0.42 until the interior pass)
	};
}
