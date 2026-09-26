using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.World.StationParts;

/// <summary>
/// Procedural textures and materials for the forester station (Act 13), low-res like the rest of the
/// game (32-128 px, filtered and mip-mapped): striped wallpaper and its peeled-back state, red brick,
/// a black-and-white marble diamond floor, rusted riveted steel plate, raw meat, and alpha decals —
/// blood splatter, handprints, water stains and spider web. Deterministic; built once and cached.
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
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
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
		ProcTextures.AddGrime(s as StandardMaterial3D);
		_mat[key] = s;
		return s;
	}

	// ------------------------------------------------------------------ walls and floors

	/// <summary>Faded sage-and-cream striped wallpaper with a small damask dot between the stripes.</summary>
	public static StandardMaterial3D WallpaperMat => Std("st_wallpaper", Make("st_wallpaper", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 3, 11);
		bool stripe = (x / 8) % 2 == 0;
		Color c = stripe ? new Color(0.62f, 0.66f, 0.55f) : new Color(0.8f, 0.77f, 0.66f);
		int dx = x % 16 - 12, dy = y % 16 - 8;
		if (!stripe && dx * dx + dy * dy < 5) c = new Color(0.6f, 0.52f, 0.42f);
		return c * (0.9f + 0.18f * n);
	}), 0.9f, 0.2f);

	/// <summary>Room 2's paper: deep blood-red stripes, a darker crimson between them, a faint raised
	/// damask pattern pressed into the dark stripes, and a fine linen weave over all of it.</summary>
	public static StandardMaterial3D RedWallpaperMat => Std("st_redpaper", Make("st_redpaper", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 8, 2, 111);
		int col = x % 16;
		bool wide = col < 10;
		bool pin = col == 11 || col == 14;
		Color c = wide ? new Color(0.56f, 0.05f, 0.06f) : new Color(0.36f, 0.02f, 0.03f);
		if (pin) c = new Color(0.62f, 0.42f, 0.22f) * 0.7f;
		if (wide)
		{
			// a small damask motif, embossed: lighter on its upper edge, darker below
			float dx = (col - 5f) / 4f, dy = ((y % 16) - 8f) / 6f;
			float r = Mathf.Sqrt(dx * dx + dy * dy);
			float motif = Mathf.SmoothStep(0.75f, 0.55f, r) * (0.5f + 0.5f * Mathf.Cos(Mathf.Atan2(dy, dx) * 4f));
			c = c.Lerp(new Color(0.46f, 0.03f, 0.05f), motif * 0.6f);
			if (motif > 0.3f && (y % 16) < 8) c *= 1.12f;
		}
		float weave = ((x + y) % 2 == 0 ? 1.03f : 0.97f);
		return c * weave * (0.9f + 0.18f * n);
	}), 0.75f, 0.2f);

	/// <summary>Heavy crimson velvet: a soft vertical nap, darker in the folds.</summary>
	public static StandardMaterial3D VelvetMat => Std("st_velvet_drape", Make("st_velvet_drape", 32, 64, (x, y) =>
	{
		float n = Fbm(x * 2, y * 0.5f, 64, 4, 3, 121);
		return new Color(0.34f, 0.03f, 0.05f) * (0.8f + 0.35f * n);
	}), 0.95f, 0.1f, cullOff: true);

	/// <summary>The same paper gone brown and mottled with damp, a tide line of mould along the bottom.</summary>
	public static StandardMaterial3D WallpaperStainedMat => Std("st_wallpaper_st", Make("st_wallpaper_st", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 4, 12);
		bool stripe = (x / 8) % 2 == 0;
		Color c = stripe ? new Color(0.5f, 0.5f, 0.38f) : new Color(0.62f, 0.56f, 0.42f);
		c *= 0.7f + 0.35f * n;
		float blot = Mathf.SmoothStep(0.55f, 0.75f, Fbm(x + 13, y + 7, 64, 2, 3, 13));
		return c.Lerp(new Color(0.24f, 0.2f, 0.12f), blot * 0.7f);
	}), 0.95f, 0.15f);

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
	public static StandardMaterial3D BrickMat => Std("st_brick", Make("st_brick", 64, 64, (x, y) =>
	{
		int row = y / 8;
		int off = row % 2 == 0 ? 0 : 8;
		int col = (x + off) / 16;
		bool mortar = y % 8 == 0 || (x + off) % 16 == 0;
		float n = Fbm(x, y, 64, 8, 3, 31);
		if (mortar) return new Color(0.2f, 0.18f, 0.16f) * (0.8f + 0.3f * n);
		float tone = Hash(col, row, 32);
		Color c = new Color(0.46f, 0.14f, 0.09f).Lerp(new Color(0.58f, 0.25f, 0.16f), tone);
		return c * (0.78f + 0.35f * n);
	}), 0.92f, 0.18f);

	/// <summary>Black and white marble set on the diagonal (diamonds), veined, one tile per 0.5 m.</summary>
	public static StandardMaterial3D MarbleMat => Std("st_marble", Make("st_marble", 128, 128, (x, y) =>
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
	}), 0.12f, 0.7f);

	/// <summary>Rust-scabbed steel plate, a row of rivets along each seam, weeping orange streaks.</summary>
	public static StandardMaterial3D RustPlateMat => Std("st_rust", Make("st_rust", 64, 64, (x, y) =>
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
	}), 0.75f, 0.45f);

	/// <summary>Raw, wet flesh: dark red marbled with fat and veins.</summary>
	public static StandardMaterial3D MeatMat => Std("st_meat", Make("st_meat", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 4, 61);
		float fat = Mathf.SmoothStep(0.62f, 0.72f, Fbm(x + 30, y, 64, 3, 3, 62));
		float vein = Mathf.SmoothStep(0.96f, 1f, Mathf.Abs(Mathf.Sin(x * 0.2f + n * 10f)));
		Color c = new Color(0.36f, 0.05f, 0.05f).Lerp(new Color(0.55f, 0.16f, 0.14f), n);
		c = c.Lerp(new Color(0.72f, 0.58f, 0.45f), fat * 0.6f);
		return c.Lerp(new Color(0.2f, 0.02f, 0.06f), vein * 0.7f);
	}), 0.25f, 0.6f);

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

	/// <summary>Thick old web: radial threads and a spiral, dusty, dense at the middle.</summary>
	public static StandardMaterial3D WebMat => Std("st_web", Make("st_web", 128, 128, (x, y) =>
	{
		float dx = x - 64f, dy = y - 64f;
		float r = Mathf.Sqrt(dx * dx + dy * dy) / 64f, ang = Mathf.Atan2(dy, dx);
		float spokes = Mathf.SmoothStep(0.9f, 1f, Mathf.Abs(Mathf.Cos(ang * 9f + r * 2f)));
		float spiral = Mathf.SmoothStep(0.86f, 1f, Mathf.Abs(Mathf.Sin(r * 42f + ang * 1.2f)));
		float fluff = Fbm(x, y, 128, 16, 3, 101);
		float a = Mathf.Max(spokes, spiral * 0.8f) * (1f - Mathf.SmoothStep(0.8f, 1f, r)) + fluff * 0.35f * (1f - r);
		return new Color(0.86f, 0.85f, 0.82f, Mathf.Clamp(a, 0f, 1f) * 0.85f);
	}), 1f, 0.05f, alpha: true, cullOff: true);

	/// <summary>A smooth unlit colour, cached (for glowing bulbs and the like).</summary>
	public static StandardMaterial3D Glow(string key, Color c, float energy = 1f) => (StandardMaterial3D)(
		_mat.TryGetValue(key, out var m) ? m : (_mat[key] = new StandardMaterial3D
		{
			AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
		}));

	/// <summary>A plain coloured surface (vertex colour tints), cached.</summary>
	public static StandardMaterial3D Flat(string key, Color c, float rough = 0.8f, float spec = 0.3f) => (StandardMaterial3D)(
		_mat.TryGetValue(key, out var m) ? m : (_mat[key] = new StandardMaterial3D
		{
			AlbedoColor = c, Roughness = rough, MetallicSpecular = spec, VertexColorUseAsAlbedo = true,
		}));
}
