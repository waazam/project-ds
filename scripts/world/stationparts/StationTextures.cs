using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.World.StationParts;

/// <summary>
/// Procedural textures and materials for the forester station (Act 13), low-res like the rest of the
/// game (32-128 px, filtered and mip-mapped): striped wallpaper and its peeled-back state, red brick,
/// a black-and-white marble diamond floor, rusted riveted steel plate, raw meat, and alpha decals —
/// blood splatter, handprints, water stains (the spider webs are photographs now: WebKit). Deterministic; built once and cached.
/// </summary>
public static class StationTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, Material> _mat = new();

	private static float Hash(int x, int y, int seed)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}

	private static float Noise(float x, float y, int period, int seed)
	{
		int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
		float fx = x - x0, fy = y - y0;
		fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
		int W(int v) => ((v % period) + period) % period;
		float a = Hash(W(x0), W(y0), seed), b = Hash(W(x0 + 1), W(y0), seed);
		float c = Hash(W(x0), W(y0 + 1), seed), d = Hash(W(x0 + 1), W(y0 + 1), seed);
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
	}

	/// <summary>Tileable fbm over a w-wide image; u,v in pixels.</summary>
	private static float Fbm(float u, float v, int w, int cells, int oct, int seed)
	{
		float s = 0, a = 0.5f, n = 0;
		for (int o = 0; o < oct; o++)
		{
			int p = cells << o;
			s += a * Noise(u / w * p, v / w * p, p, seed + o * 17);
			n += a; a *= 0.5f;
		}
		return s / n;
	}

	private static Texture2D Make(string key, int w, int h, Func<int, int, Color> f)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var __tg = Systems.TexGen.Start();
		if (Systems.TexCache.Load("StationTextures_" + key) is { } __cached) { t = ImageTexture.CreateFromImage(__cached); _tex[key] = t; return t; }
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		img = TexelBoost.Apply(key, img);
		img.GenerateMipmaps();
		Systems.TexCache.Save("StationTextures_" + key, img);
		t = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
		_tex[key] = t;
		return t;
	}

	private static StandardMaterial3D Std(string key, Texture2D tex, float rough = 0.9f, float spec = 0.25f, bool alpha = false, bool cullOff = false)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex,
			Roughness = rough,
			MetallicSpecular = spec,
			VertexColorUseAsAlbedo = true,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		if (alpha) { s.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; s.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel; }
		if (cullOff) s.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		ProcTextures.AddGrime(s as StandardMaterial3D, key);
		_mat[key] = s;
		return s;
	}

	// ------------------------------------------------------------------ walls and floors

	/// <summary>The lobby's paper: a faded sage ground, a fine double pinstripe every 6 cm and a small leaf sprig between,
	/// all a whisper apart in value (2026-10-14h, the station pass).</summary>
	// (2026-10-13, the look audit: the lobby's cream paper under its lamp and the lantern read near white, a clean bright
	// room; aged a step darker and yellower, the texture as it was)
	// (2026-10-13g, the texture-mood pass: still a clean yellow-cream room under its lamp beside the cabin's worn wood;
	// the paper is old now: greyer, darker, foxed and yellowed in blotches, and the plaster's
	// relief and roughness under it, so the lantern finds a surface instead of a print)
	// (2026-10-14h, the station pass: the look survey still read its stripes and diamonds as a flat print a size too big
	// (a 23 cm motif, stripes a hand wide), and the interiors kit found a loud paper drowns the weathering laid over it.
	// The pattern is a quarter the size now, real paper's scale, and quiet: the room's stains, damp and dark carry it)
	public static StandardMaterial3D WallpaperMat => Aged(WallpaperFresh, new Color(0.62f, 0.6f, 0.53f));
	private static StandardMaterial3D Aged(StandardMaterial3D m, Color c)
	{
		m.AlbedoColor = c;
		if (!m.HasMeta("relief")) { SurfaceKit.Relief(m, "plaster", 0.45f); m.SetMeta("relief", "plaster"); m.Uv1Scale = new Vector3(2f, 2f, 1f); }
		return m;
	}
	/// <summary>A paper pattern 128 px a tile (a tile about 45 cm on the station's walls): <paramref name="ground"/>, a
	/// fine double stripe in <paramref name="stripe"/> every 16 px, a sprig in <paramref name="sprig"/> between, foxed.</summary>
	private static Color Paper(int x, int y, Color ground, Color stripe, Color sprig, int seed, float fox)
	{
		float n = Fbm(x, y, 128, 8, 3, seed);
		int u = x % 16;
		Color c = ground;
		if (u == 0 || u == 3) c = c.Lerp(stripe, 0.8f);
		else if (u == 1 || u == 2) c = c.Lerp(stripe, 0.25f);
		// the sprig: a small leaf pair on a stem, every other column, staggered
		int col = x / 16, sy = (y + (col % 2) * 16) % 32;
		float sx = x % 16 - 9.5f, ly = sy - 16f;
		if (Mathf.Abs(sx) < 0.8f && ly > -4f && ly < 4f) c = c.Lerp(sprig, 0.6f);
		float l1 = (sx + 2.2f) * (sx + 2.2f) / 5f + (ly + 1.5f) * (ly + 1.5f) / 2f, l2 = (sx - 2.2f) * (sx - 2.2f) / 5f + (ly - 1f) * (ly - 1f) / 2f;
		if (l1 < 1f || l2 < 1f) c = c.Lerp(sprig, 0.7f);
		c *= 0.92f + 0.14f * n;
		// foxing: yellow-brown blotches; a few darker specks
		float f = Mathf.SmoothStep(0.55f, 0.8f, Fbm(x + 21, y + 5, 128, 3, 3, seed + 3));
		c = c.Lerp(new Color(0.4f, 0.32f, 0.2f), f * fox);
		if (Hash(x, y, seed + 4) > 0.993f) c *= 0.7f;
		return c;
	}
	private static StandardMaterial3D WallpaperFresh => Std("st_wallpaper2", Make("st_wallpaper2", 128, 128, (x, y) =>
		Paper(x, y, new Color(0.55f, 0.56f, 0.49f), new Color(0.66f, 0.63f, 0.53f), new Color(0.43f, 0.45f, 0.37f), 11, 0.4f)), 0.92f, 0.15f);

	/// <summary>Room 2's paper: an oxblood regency stripe, a broad band and a narrow one, a dull ochre pin between, faded
	/// and mottled. (2026-10-14h, the station pass: it was a saturated blood-red at 23 cm a stripe, the loudest colour in
	/// the game outside the pit; still red, still the red room, but an old dark one at real paper's scale.)</summary>
	public static StandardMaterial3D RedWallpaperMat => Fine(Std("st_redpaper2", Make("st_redpaper2", 128, 128, (x, y) =>
	{
		float n = Fbm(x, y, 128, 8, 3, 111);
		int col = x % 32;
		Color c = col < 18 ? new Color(0.44f, 0.11f, 0.09f) : new Color(0.32f, 0.08f, 0.07f);
		if (col == 20 || col == 29) c = new Color(0.38f, 0.27f, 0.16f);
		if (col < 18)
		{
			// a faint damask in the broad band, a little lighter
			float dx = (col - 9f) / 6f, dy = ((y % 24) - 12f) / 8f;
			float r = Mathf.Sqrt(dx * dx + dy * dy);
			float motif = Mathf.SmoothStep(0.8f, 0.6f, r) * (0.5f + 0.5f * Mathf.Cos(Mathf.Atan2(dy, dx) * 4f));
			c = c.Lerp(new Color(0.5f, 0.15f, 0.12f), motif * 0.45f);
		}
		c *= ((x + y) % 2 == 0 ? 1.02f : 0.98f) * (0.88f + 0.18f * n);
		// faded in broad patches, browned at the foxing
		float fade = Fbm(x + 7, y + 31, 128, 2, 3, 112);
		c = c.Lerp(new Color(0.3f, 0.17f, 0.13f), Mathf.SmoothStep(0.5f, 0.8f, fade) * 0.45f);
		return c;
	}), 0.85f, 0.15f), "plaster", 0.4f);

	/// <summary>The paper's pattern at a quarter-metre-ish scale (twice the walls' own UV), the plaster's relief under it.</summary>
	private static StandardMaterial3D Fine(StandardMaterial3D m, string relief, float strength)
	{
		if (m.HasMeta("fine")) return m;
		m.SetMeta("fine", true);
		SurfaceKit.Relief(m, relief, strength);
		m.Uv1Scale = new Vector3(2f, 2f, 1f);
		return m;
	}

	/// <summary>Heavy velvet gone the colour of old wine: a soft vertical nap, darker in the folds.</summary>
	public static StandardMaterial3D VelvetMat => Std("st_velvet_drape2", Make("st_velvet_drape2", 32, 64, (x, y) =>
	{
		float n = Fbm(x * 2, y * 0.5f, 64, 4, 3, 121);
		return new Color(0.21f, 0.055f, 0.055f) * (0.8f + 0.35f * n);
	}), 0.95f, 0.1f, cullOff: true);

	/// <summary>The same paper gone brown and mottled with damp, a tide line of mould along the bottom.</summary>
	public static StandardMaterial3D WallpaperStainedMat => Fine(Std("st_wallpaper_st2", Make("st_wallpaper_st2", 128, 128, (x, y) =>
	{
		var c = Paper(x, y, new Color(0.47f, 0.46f, 0.37f), new Color(0.55f, 0.51f, 0.4f), new Color(0.36f, 0.35f, 0.27f), 12, 0.6f);
		float blot = Mathf.SmoothStep(0.55f, 0.78f, Fbm(x + 13, y + 7, 128, 2, 3, 13));
		return c.Lerp(new Color(0.24f, 0.2f, 0.13f), blot * 0.55f);
	}), 0.95f, 0.15f), "plaster", 0.45f);

	/// <summary>Bare, dark, rotting boards (what's under the paper once it peels).</summary>
	public static StandardMaterial3D RotBoardsMat => Std("st_rotboards", Make("st_rotboards", 32, 64, (x, y) =>
	{
		float n = Fbm(x * 2, y, 64, 2, 4, 21);
		float grain = 0.5f + 0.5f * Mathf.Sin(y * 0.9f + n * 6f);
		bool gap = x % 16 == 0;
		Color c = new Color(0.22f, 0.17f, 0.12f) * (0.75f + 0.3f * grain);
		return gap ? new Color(0.03f, 0.025f, 0.02f) : c;
	}), 0.95f, 0.1f);

	/// <summary>Old red brick in soot-dark mortar, each brick a slightly different red.</summary>
	public static StandardMaterial3D BrickMat => SurfaceSets.Scan(Std("st_brick", Make("st_brick", 64, 64, (x, y) =>
	{
		int row = y / 8;
		int off = row % 2 == 0 ? 0 : 8;
		int col = (x + off) / 16;
		bool mortar = y % 8 == 0 || (x + off) % 16 == 0;
		float n = Fbm(x, y, 64, 8, 3, 31);
		if (mortar) return new Color(0.15f, 0.14f, 0.125f) * (0.8f + 0.3f * n);
		// old, damp, sooted brick (the interiors pass, 2026-10-03: it was a bright, even orange-red); a burnt one here and there
		float tone = Hash(col, row, 32);
		Color c = new Color(0.27f, 0.12f, 0.085f).Lerp(new Color(0.36f, 0.2f, 0.14f), tone);
		if (Hash(col, row, 33) > 0.82f) c *= 0.55f;
		float soot = Fbm(x, y, 64, 2, 2, 34);
		c = c.Lerp(new Color(0.12f, 0.11f, 0.1f), Mathf.Clamp((soot - 0.45f) * 1.6f, 0f, 0.6f));
		return c * (0.78f + 0.35f * n);
	}), 0.92f, 0.18f), "red_brick_03", 0.5f);

	/// <summary>Black and white marble set on the diagonal (diamonds), veined, one tile per 0.5 m.</summary>
	public static StandardMaterial3D MarbleMat => SurfaceSets.Scan(Std("st_marble", Make("st_marble", 128, 128, (x, y) =>
	{
		// diamonds: checker on the rotated grid
		float u = (x + y) / 64f, v = (x - y + 128) / 64f;
		bool black = ((int)Mathf.Floor(u) + (int)Mathf.Floor(v)) % 2 == 0;
		float vein = Mathf.Abs(Mathf.Sin((x * 0.09f + Fbm(x, y, 128, 4, 4, 41) * 9f)));
		vein = Mathf.SmoothStep(0.94f, 1f, vein);
		float line = Mathf.Min(Mathf.Abs(u - Mathf.Round(u)), Mathf.Abs(v - Mathf.Round(v)));
		Color c = black ? new Color(0.05f, 0.05f, 0.055f) : new Color(0.85f, 0.84f, 0.8f);
		c = c.Lerp(black ? new Color(0.3f, 0.3f, 0.32f) : new Color(0.55f, 0.55f, 0.56f), vein * 0.8f);
		if (line < 0.012f) c = new Color(0.18f, 0.17f, 0.16f);
		return c;
	}), 0.12f, 0.7f), "marble_01", 0.5f);

	/// <summary>Rust-scabbed steel plate, a row of rivets along each seam, weeping orange streaks.</summary>
	public static StandardMaterial3D RustPlateMat => SurfaceSets.Scan(Std("st_rust", Make("st_rust", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 4, 51);
		float streak = Fbm(x * 6, y * 0.4f, 64, 2, 2, 52);
		Color steel = new Color(0.3f, 0.29f, 0.28f), rust = new Color(0.42f, 0.18f, 0.07f);
		Color c = steel.Lerp(rust, Mathf.SmoothStep(0.35f, 0.7f, n * 0.7f + streak * 0.5f));
		bool seam = x % 32 == 0 || y % 32 == 0;
		int rx = x % 32, ry = y % 32;
		bool rivet = (rx == 4 || rx == 28) && (ry == 4 || ry == 28);
		if (seam) c *= 0.4f;
		if (rivet) c = new Color(0.5f, 0.35f, 0.25f);
		return c * (0.85f + 0.25f * n);
	}), 0.75f, 0.45f), "rusty_metal_02", 0.5f);

	/// <summary>Old flesh grown through the walls: a dull, dark liver-brown marbled with yellowed fat and black veins, a
	/// grey bloom of mould over it. (2026-10-14h, the station pass: it was a glossy, saturated wet red that threw hot
	/// glints off the lantern; it reads as rot now, matte, the colour of the damp round it.)</summary>
	public static StandardMaterial3D MeatMat => Std("st_meat2", Make("st_meat2", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 4, 61);
		float fat = Mathf.SmoothStep(0.62f, 0.72f, Fbm(x + 30, y, 64, 3, 3, 62));
		float vein = Mathf.SmoothStep(0.96f, 1f, Mathf.Abs(Mathf.Sin(x * 0.2f + n * 10f)));
		Color c = new Color(0.2f, 0.07f, 0.06f).Lerp(new Color(0.3f, 0.13f, 0.1f), n);
		c = c.Lerp(new Color(0.42f, 0.36f, 0.24f), fat * 0.5f);
		c = c.Lerp(new Color(0.07f, 0.03f, 0.03f), vein * 0.7f);
		float mould = Mathf.SmoothStep(0.58f, 0.78f, Fbm(x + 9, y + 40, 64, 4, 3, 63));
		return c.Lerp(new Color(0.27f, 0.28f, 0.22f), mould * 0.55f);
	}), 0.8f, 0.15f);

	// ------------------------------------------------------------------ decals (alpha)

	/// <summary>A blood splash: a dense core, a ragged rim, flung droplets.</summary>
	public static StandardMaterial3D BloodMat => Std("st_blood", Make("st_blood", 64, 64, (x, y) =>
	{
		float dx = (x - 32) / 32f, dy = (y - 32) / 32f;
		float r = Mathf.Sqrt(dx * dx + dy * dy);
		float edge = 0.55f + 0.25f * Fbm(x, y, 64, 8, 3, 71);
		float a = Mathf.SmoothStep(edge + 0.05f, edge - 0.05f, r);
		if (Hash(x / 3, y / 3, 72) > 0.985f && r < 0.95f) a = 1f;
		return new Color(0.32f, 0.02f, 0.02f, a * 0.92f);
	}), 0.2f, 0.6f, alpha: true);

	/// <summary>A wide pool of blood: a dense, glossy body with a ragged, lobed rim and a few flung drops round
	/// it (Act 13, room 1: what the writing melts into).</summary>
	public static StandardMaterial3D BloodPoolMat => Std("st_bloodpool", Make("st_bloodpool", 128, 128, (x, y) =>
	{
		float dx = (x - 64) / 64f, dy = (y - 64) / 64f;
		float r = Mathf.Sqrt(dx * dx + dy * dy);
		float ang = Mathf.Atan2(dy, dx);
		float edge = 0.62f + 0.14f * Mathf.Sin(ang * 3f + 1.3f) + 0.08f * Mathf.Sin(ang * 7f) + 0.16f * (Fbm(x, y, 128, 6, 3, 171) - 0.5f);
		float a = Mathf.SmoothStep(edge + 0.03f, edge - 0.03f, r);
		if (Hash(x / 4, y / 4, 172) > 0.992f && r < 0.98f) a = 1f;
		float depth = Mathf.Clamp(1f - r / Mathf.Max(edge, 0.1f), 0f, 1f);
		var c = new Color(0.36f, 0.025f, 0.02f).Lerp(new Color(0.2f, 0.005f, 0.01f), depth * 0.8f);
		return new Color(c.R, c.G, c.B, a * 0.95f);
	}), 0.08f, 0.7f, alpha: true);

	/// <summary>A run of blood down a wall: a thin wavering line, heavier toward its foot (the wall writing's
	/// drips). The bead at its end is a separate decal, so the line can grow without stretching it.</summary>
	public static StandardMaterial3D BloodDripMat => Std("st_blooddrip", Make("st_blooddrip", 16, 128, (x, y) =>
	{
		float v = y / 127f;                                  // 0 at the top .. 1 at the foot
		float centre = 7.5f + 1.2f * Mathf.Sin(v * 9f) + 0.6f * Mathf.Sin(v * 23f + 1f);
		float half = Mathf.Lerp(1.4f, 3.2f, v) * (0.85f + 0.3f * Fbm(x, y, 16, 2, 2, 181));
		float a = Mathf.SmoothStep(half + 0.8f, half - 0.4f, Mathf.Abs(x - centre)) * Mathf.SmoothStep(0f, 0.06f, v);
		return new Color(0.5f, 0.02f, 0.015f, a);
	}), 0.15f, 0.6f, alpha: true);

	/// <summary>A smeared bloody handprint (palm, four fingers, a thumb).</summary>
	public static StandardMaterial3D HandprintMat => Std("st_hand", Make("st_hand", 64, 64, (x, y) =>
	{
		float px = (x - 32) / 32f, py = (y - 40) / 32f;
		float a = Mathf.SmoothStep(0.5f, 0.42f, Mathf.Sqrt(px * px * 1.4f + py * py));
		for (int f = 0; f < 4; f++)
		{
			float fx = (x - (20 + f * 8)) / 32f, fy = (y - 14) / 32f;
			if (Mathf.Abs(fx) < 0.08f && fy > -0.35f + f % 2 * 0.05f && fy < 0.2f) a = 1f;
		}
		float tx = (x - 52) / 32f, ty = (y - 34) / 32f;
		if (Mathf.Abs(tx + ty * 0.6f) < 0.08f && ty > -0.2f && ty < 0.15f) a = 1f;
		a *= 0.65f + 0.35f * Fbm(x, y, 64, 8, 2, 81);
		return new Color(0.35f, 0.02f, 0.02f, a * 0.9f);
	}), 0.3f, 0.5f, alpha: true);

	/// <summary>A water stain creeping up from the floor: dark at the bottom, a ragged tide line.</summary>
	public static StandardMaterial3D StainMat => Std("st_stain", Make("st_stain", 64, 64, (x, y) =>
	{
		float line = 0.45f + 0.3f * Fbm(x, 0, 64, 4, 3, 91);
		float v = y / 64f;   // 0 top, 1 bottom
		float a = Mathf.SmoothStep(line - 0.08f, line + 0.12f, v) * 0.75f;
		float rim = Mathf.Exp(-Mathf.Pow((v - line) / 0.03f, 2f)) * 0.35f;
		return new Color(0.16f, 0.12f, 0.06f, Mathf.Clamp(a + rim, 0f, 1f));
	}), 0.9f, 0.1f, alpha: true);

	/// <summary>A smooth unlit colour, cached (for glowing bulbs and the like).</summary>
	public static StandardMaterial3D Glow(string key, Color c, float energy = 1f) => (StandardMaterial3D)(
		_mat.TryGetValue(key, out var m) ? m : (_mat[key] = new StandardMaterial3D
		{
			AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
		}));

	// ------------------------------------------------------------------ the station's own surfaces (2026-10-14b)
	// (the owner: "go through each scene and make everything look better. for example cabin wall and door shouldn't be the
	// same texture") The station was one wood everywhere: the floor, the ceiling, the wainscot, the doors, their frames and
	// the furniture were all the same hinoki, a step lighter or darker. Each part has its own surface now: a plank floor, a
	// plaster ceiling, beaded boards under the chair rail, painted panel doors, dark casings. All projected in the object's
	// own space (MeshKit's meshes carry no tangents; a projection needs none), each its own scan.

	/// <summary>A scan projected three ways in the object's space, <paramref name="perMetre"/> tiles to a metre.</summary>
	private static StandardMaterial3D Projected(string key, string set, Color tint, float perMetre, float normal, float rough, float spec = 0.3f)
	{
		if (_mat.TryGetValue(key, out var have)) return (StandardMaterial3D)have;
		var m = new StandardMaterial3D { ResourceName = key, VertexColorUseAsAlbedo = true, MetallicSpecular = spec };
		SurfaceSets.Apply(m, set, normal);
		m.AlbedoColor = tint;
		m.Roughness = rough;
		m.Uv1Triplanar = true;
		m.Uv1WorldTriplanar = false;
		m.Uv1TriplanarSharpness = 6f;
		m.Uv1Scale = Vector3.One * perMetre;
		ProcTextures.AddGrime(m, key);
		_mat[key] = m;
		return m;
	}

	/// <summary>Paint over a grain: <paramref name="under"/>'s relief and roughness, the plaster's mottle as the paint's
	/// own unevenness, in <paramref name="paint"/>.</summary>
	private static StandardMaterial3D Painted(string key, string under, Color paint, float perMetre, float normal, float rough)
	{
		if (_mat.TryGetValue(key, out var have)) return (StandardMaterial3D)have;
		var m = Projected(key, under, paint, perMetre, normal, rough, 0.35f);
		if (SurfaceSets.Tex("plaster") is { } mottle) m.AlbedoTexture = mottle;
		return m;
	}

	/// <summary>The lobby's and the rooms' floor: worn oak planks, dulled and greyed where they're walked.</summary>
	public static StandardMaterial3D PlankFloorMat => Projected("st_plankfloor", "ph_wood_floor", new Color(0.62f, 0.54f, 0.45f), 0.8f, 1f, 0.82f, 0.28f);
	/// <summary>The ceilings: old distemper on plaster, gone the colour of weak tea.</summary>
	public static StandardMaterial3D CeilingMat => Projected("st_ceiling", "plaster", new Color(0.5f, 0.47f, 0.41f), 1.1f, 0.6f, 0.95f, 0.15f);
	/// <summary>Beaded boards under the chair rail: narrow upright planks, darker and waxed.</summary>
	public static StandardMaterial3D WainscotMat => Projected("st_wainscot", "old_planks", new Color(0.62f, 0.5f, 0.4f), 1.4f, 1f, 0.7f, 0.35f);
	/// <summary>The doors: panelled, in an old institutional green worn to the wood at the edges (the grain under the paint).</summary>
	public static StandardMaterial3D DoorPaintMat => Painted("st_doorpaint", "oak", new Color(0.2f, 0.23f, 0.19f), 1.2f, 0.7f, 0.72f);   // (2026-10-14h: a step darker: under the lantern it read near white)
	/// <summary>The doors' casings and linings: dark stained wood, a shade off the crown mouldings.</summary>
	public static StandardMaterial3D CasingMat => Projected("st_casing", "darkwood", new Color(0.55f, 0.45f, 0.38f), 2f, 0.8f, 0.6f, 0.4f);

	/// <summary>A plain coloured surface (vertex colour tints), cached.</summary>
	public static StandardMaterial3D Flat(string key, Color c, float rough = 0.8f, float spec = 0.3f) => (StandardMaterial3D)(
		_mat.TryGetValue(key, out var m) ? m : (_mat[key] = new StandardMaterial3D
		{
			AlbedoColor = c, Roughness = rough, MetallicSpecular = spec, VertexColorUseAsAlbedo = true,
		}));

	// ------------------------------------------------------------------ the ranger station (2026-10-14h, the station pass)

	/// <summary>Knotty pine boards, varnished and gone amber: the lobby's beadboard wainscot.</summary>
	public static StandardMaterial3D PineMat => Projected("st_pine", "pine", new Color(0.9f, 0.76f, 0.6f), 1.6f, 0.8f, 0.62f, 0.32f);
	/// <summary>Pale paper for notices and forms, a step under the lantern's pool (the survey: the notices went near
	/// white, flat cards that read as screens), with the plaster's mottle for the paper's unevenness.</summary>
	public static StandardMaterial3D NoticeMat => Painted("st_notice", "linen", new Color(0.5f, 0.47f, 0.39f), 4f, 0.3f, 0.95f);
	/// <summary>Cast iron, black-brown and dull (the stove, the radiator).</summary>
	public static StandardMaterial3D CastIronMat => Projected("st_castiron", "ph_rusty_metal_03", new Color(0.3f, 0.27f, 0.25f), 2.5f, 0.9f, 0.7f, 0.35f);
	/// <summary>Olive-drab painted steel, scuffed (the filing cabinets' kin, the radio's case).</summary>
	public static StandardMaterial3D DrabSteelMat => Painted("st_drab", "ph_metal_plate_02", new Color(0.26f, 0.28f, 0.22f), 2f, 0.5f, 0.6f);

	/// <summary>The district's map: a survey sheet on yellowed paper, the woods in green tint, the lake in pale blue,
	/// brown contours, red-dashed trails and a grid, pencil rings round the stations; foxed.</summary>
	public static StandardMaterial3D MapMat => Std("st_map", Make("st_map", 128, 96, (x, y) =>
	{
		float n = Fbm(x, y, 128, 4, 4, 301);
		Color paper = new Color(0.62f, 0.58f, 0.46f);
		float wood = Mathf.SmoothStep(0.45f, 0.55f, Fbm(x, y, 128, 3, 3, 302));
		Color c = paper.Lerp(new Color(0.44f, 0.52f, 0.36f), wood * 0.6f);
		float lake = Fbm(x * 0.8f + 40, y * 0.8f, 128, 2, 3, 303) - Mathf.Sqrt((x - 70f) * (x - 70f) + (y - 52f) * (y - 52f)) / 120f;
		if (lake > 0.28f) c = new Color(0.45f, 0.53f, 0.55f);
		else if (lake > 0.26f) c = new Color(0.25f, 0.3f, 0.35f);
		// contours: where the height crosses a step
		float hgt = Fbm(x, y, 128, 2, 4, 304) * 12f;
		if (lake <= 0.26f && Mathf.Abs(hgt - Mathf.Round(hgt)) < 0.06f) c = c.Lerp(new Color(0.42f, 0.28f, 0.16f), 0.6f);
		// a trail, red-dashed, wandering across
		float ty = 30f + 18f * Mathf.Sin(x * 0.05f) + 6f * Mathf.Sin(x * 0.13f + 1f);
		if (Mathf.Abs(y - ty) < 0.8f && (x / 3) % 2 == 0) c = new Color(0.52f, 0.12f, 0.08f);
		if (x % 32 == 0 || y % 32 == 0) c = c.Lerp(new Color(0.25f, 0.22f, 0.18f), 0.45f);
		// a pencil ring or two
		foreach (var (cx, cy) in new[] { (34f, 22f), (96f, 70f) })
		{
			float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
			if (Mathf.Abs(r - 5f) < 0.6f) c = new Color(0.2f, 0.18f, 0.17f);
		}
		if (x < 2 || y < 2 || x > 125 || y > 93) c = paper * 0.85f;
		float fox = Mathf.SmoothStep(0.55f, 0.8f, Fbm(x + 3, y + 17, 128, 3, 3, 305));
		c = c.Lerp(new Color(0.42f, 0.33f, 0.2f), fox * 0.5f);
		return c * (0.9f + 0.15f * n);
	}), 0.95f, 0.05f);
}
