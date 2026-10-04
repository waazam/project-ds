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
/// <item><b>The rooms' carpet</b>: peacock scales, overlapping scallops in bottle green and bruise violet.</item>
/// <item><b>Wallpaper</b>: cream, a fine gold pinstripe and a wider band; a darker damask for the bar.</item>
/// <item><b>Bathroom tile</b>: mint-green squares, dark grout; a mustard trim.</item>
/// <item><b>Floors</b>: flagstone (the lobby), herringbone parquet (the dining hall), chequered linoleum (the
/// pantry), and rugs: a big medallion rug in red and navy.</item>
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
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = f((x + 0.5f) / w, (y + 0.5f) / h);
				c.A = 1f;
				img.SetPixel(x, y, c);
			}
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
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

	/// <summary>The rooms' carpet: peacock scales, overlapping scallops in bottle green and bruise violet.</summary>
	public static Texture2D RoomCarpet => Make("lodge_carpet_room", 256, 256, (u, v) =>
	{
		Color green = new(0.1f, 0.28f, 0.18f), teal = new(0.14f, 0.4f, 0.3f), violet = new(0.3f, 0.12f, 0.36f), dark = new(0.06f, 0.05f, 0.07f);
		float cu = u * 4f, cv = v * 4f;
		int row = Mathf.FloorToInt(cv * 2f);
		if (row % 2 == 1) cu += 0.5f;
		float fu = cu - Mathf.Floor(cu) - 0.5f, fv = cv * 2f - Mathf.Floor(cv * 2f);
		float r = new Vector2(fu, (fv - 1.0f) * 0.5f).Length();   // scales hang from the row above
		float band = r * 10f;
		int ring = (int)band;
		Color c = ring % 3 == 0 ? violet : ring % 3 == 1 ? teal : green;
		if (band - ring < 0.18f) c = dark;
		if (r > 0.52f) c = green * 0.8f;
		return c * Wear(u, v, 21) * SurfaceKit.Grain("carpet", u, v, 0.5f, 2f);
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
		Color red = new(0.3f, 0.05f, 0.05f), deep = new(0.18f, 0.03f, 0.03f), gold = new(0.55f, 0.38f, 0.14f);
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

	/// <summary>A big medallion rug: a red field, a navy border with a lozenge band, a medallion in the middle.</summary>
	public static Texture2D Rug => Make("lodge_rug", 256, 256, (u, v) =>
	{
		Color red = new(0.42f, 0.08f, 0.06f), navy = new(0.1f, 0.12f, 0.25f), ivory = new(0.72f, 0.64f, 0.5f), gold = new(0.62f, 0.44f, 0.16f);
		float bx = Mathf.Min(u, 1 - u), by = Mathf.Min(v, 1 - v), edge = Mathf.Min(bx, by);
		Color c = red;
		if (edge < 0.1f)
		{
			c = navy;
			float t = (u + v) * 20f % 1f;
			if (edge > 0.03f && edge < 0.08f && Mathf.Abs(t - 0.5f) < 0.2f) c = gold;
			if (edge < 0.015f) c = ivory;
		}
		else
		{
			float dx = u - 0.5f, dy = v - 0.5f;
			float lz = Mathf.Abs(dx) * 1.4f + Mathf.Abs(dy);
			if (lz < 0.22f) c = lz < 0.08f ? ivory : lz < 0.12f ? navy : lz > 0.2f ? gold : red * 1.2f;
			float fx = u * 8f % 1f, fy = v * 8f % 1f;
			if (lz > 0.25f && Mathf.Abs(fx - 0.5f) + Mathf.Abs(fy - 0.5f) < 0.12f) c = navy * 1.3f;
		}
		return c * (0.85f + 0.15f * Fbm(u, v, 91, 5, 16)) * SurfaceKit.Grain("carpet", u, v, 0.5f, 3f);
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

	public static StandardMaterial3D CrawlBoardsMat => Std("lodge_m_crawl_boards", CrawlBoards, 0.9f, 0.2f, null, DetailKit.Kind.Wood, null, 0.5f, 1f);
	public static StandardMaterial3D CrawlBrickMat => Std("lodge_m_crawl_brick", CrawlBrick, 0.95f, 0.15f, null, DetailKit.Kind.Stone, null, 0.5f, 1f);
	public static StandardMaterial3D CrawlFloorMat => Std("lodge_m_crawl_floor", CrawlFloor, 0.95f, 0.15f, null, DetailKit.Kind.Wood, null, 0.5f, 1f);
	public static StandardMaterial3D CrawlCeilingMat => Std("lodge_m_crawl_ceiling", CrawlBoards, 0.95f, 0.1f, new Color(0.55f, 0.52f, 0.48f), DetailKit.Kind.Wood, null, 0.5f, 1f);
	/// <summary>The cavity's studs and joists: the same pine as its boards, darker (it was the rooms' red varnish).</summary>
	public static StandardMaterial3D CrawlStudMat => Std("lodge_m_crawl_stud", CrawlBoards, 0.95f, 0.1f, new Color(0.62f, 0.56f, 0.5f), DetailKit.Kind.Wood, null, 0.5f, 1f);
	/// <summary>The cavity's pipes: old iron, rusted and matte (the rooms' iron caught the light in a blue line along the top).</summary>
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
	public static StandardMaterial3D WallpaperMat => Std("lodge_m_wallpaper", Wallpaper, 0.9f, 0.2f, null, null);
	public static StandardMaterial3D DamaskMat => Std("lodge_m_damask", Damask, 0.8f, 0.25f, null, DetailKit.Kind.Fabric, "jacquard", 0.35f);
	public static StandardMaterial3D GreenTileMat => Std("lodge_m_greentile", GreenTile, 0.25f, 0.6f);
	public static StandardMaterial3D FlagstoneMat => Std("lodge_m_flag", Flagstone, 0.8f, 0.25f, null, DetailKit.Kind.Stone, "flagstone", 0.8f);
	public static StandardMaterial3D ParquetMat => Std("lodge_m_parquet", Parquet, 0.5f, 0.45f, null, DetailKit.Kind.Wood, "parquet", 0.5f);
	public static StandardMaterial3D CheckerMat => Std("lodge_m_checker", Checker, 0.45f, 0.4f);
	public static StandardMaterial3D RugMat => Std("lodge_m_rug", Rug, 1f, 0.1f, null, DetailKit.Kind.Fabric);
	public static StandardMaterial3D DarkWoodMat => Std("lodge_m_darkwood", DarkWood, 0.45f, 0.45f, null, DetailKit.Kind.Wood, "darkwood", 0.4f);
	public static StandardMaterial3D LogWallMat => Std("lodge_m_logwall", LogWall, 0.8f, 0.25f, null, DetailKit.Kind.Wood);

	private static StandardMaterial3D _plain(string key, Color c, float rough, float spec, float metal = 0f)
	{
		if (_mat.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D { ResourceName = key, AlbedoColor = c, Roughness = rough, MetallicSpecular = spec, Metallic = metal, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true };
		_mat[key] = m;
		return m;
	}

	public static StandardMaterial3D GoldMat => _plain("lodge_m_gold", new Color(0.72f, 0.54f, 0.24f), 0.3f, 0.8f, 0.8f);
	public static StandardMaterial3D BrassMat => _plain("lodge_m_brass", new Color(0.62f, 0.48f, 0.24f), 0.35f, 0.7f, 0.7f);
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
	/// <summary>The dining tables' sheets (their UVs run 0..1 over the whole sheet: the linen repeats across it).</summary>
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
		s.Uv1Scale = new Vector3(14f, 4f, 1f);
		return s;
	}
	public static StandardMaterial3D PillowMat => _plain("lodge_m_pillow", new Color(0.8f, 0.78f, 0.72f), 0.95f, 0.1f);
	public static StandardMaterial3D BlackMat => _plain("lodge_m_black", new Color(0.035f, 0.03f, 0.03f), 0.6f, 0.3f);
	public static StandardMaterial3D IronMat => _plain("lodge_m_iron", new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.5f, 0.6f);
	/// <summary>Old silver plate: tarnished, soft-sheened (it catches the lamps without mirroring the dark).</summary>
	public static StandardMaterial3D SilverMat => _plain("lodge_m_silver", new Color(0.62f, 0.6f, 0.56f), 0.32f, 0.7f, 0.85f);
	public static StandardMaterial3D MirrorMat => _plain("lodge_m_mirror", new Color(0.4f, 0.42f, 0.45f), 0.02f, 1f, 1f);
	public static StandardMaterial3D PlasterMat => Std("lodge_m_plaster", Plaster, 0.9f, 0.2f, new Color(0.74f, 0.7f, 0.62f), null, null, 0.3f, 1.5f);
	public static StandardMaterial3D CeilingMat => Std("lodge_m_ceiling", Plaster, 0.95f, 0.1f, new Color(0.62f, 0.58f, 0.5f), null, null, 0.3f, 2f);
	/// <summary>The lobby's lower walls inside: Poly Haven's stacked stone, cool and even (the outside's mossy stone
	/// read as dirt in here).</summary>
	public static Texture2D LobbyStone => Make("lodge_lobbystone", 256, 256, (u, v) => SurfaceKit.Tinted("lobby_stone", u, v, new Color(0.98f, 0.96f, 0.94f), 0.36f, 0.5f));
	public static StandardMaterial3D LobbyStoneMat => Std("lodge_m_lobbystone", LobbyStone, 0.9f, 0.2f, null, DetailKit.Kind.Stone, "lobby_stone", 0.9f);
	public static StandardMaterial3D RiverStoneMat => Std("lodge_m_riverstone", FireStone, 0.9f, 0.2f, null, DetailKit.Kind.Stone, "fireplace_stone", 0.9f, 1.6f);

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
		ResourceName = "lodge_dayglass", AlbedoColor = new Color(0.36f, 0.41f, 0.47f), EmissionEnabled = true, Emission = new Color(0.5f, 0.56f, 0.64f), EmissionEnergyMultiplier = 0.38f, Roughness = 0.1f,
	};
	private static StandardMaterial3D _day, _blue, _ice;

	/// <summary>Room 202's window: snow banked right up the glass outside; the light through it blue.</summary>
	public static StandardMaterial3D SnowBlueGlass => _blue ??= new StandardMaterial3D
	{
		ResourceName = "lodge_blueglass", AlbedoTexture = WinterWoods.SnowStd("x", 1f, 1f).AlbedoTexture, AlbedoColor = new Color(0.45f, 0.58f, 0.8f),
		EmissionEnabled = true, Emission = new Color(0.3f, 0.45f, 0.75f), EmissionEnergyMultiplier = 0.8f, Roughness = 0.4f,
	};

	/// <summary>Room 203's window: frosted over, ice feathering across it.</summary>
	public static StandardMaterial3D IceGlass => _ice ??= new StandardMaterial3D
	{
		ResourceName = "lodge_iceglass", AlbedoTexture = WinterWoods.SnowStd("y", 1f, 1f).AlbedoTexture, AlbedoColor = new Color(0.7f, 0.78f, 0.86f),
		EmissionEnabled = true, Emission = new Color(0.5f, 0.57f, 0.66f), EmissionEnergyMultiplier = 0.42f, Roughness = 0.2f,
	};
}
