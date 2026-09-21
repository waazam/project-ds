using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Low-res procedural textures and materials for the small carried items
/// (axe, hammer, key, camera, compass, lantern, newel post, walkie-talkie) and
/// the friend's clothes. Same rules as ProcTextures/PropTextures: 16-64 px,
/// fixed seeds, linear filtering with mipmaps, cached per process.
///
/// Values are chosen against the game's real lighting: the leaf litter is a
/// dark red-brown mid tone, so every item carries at least one clearly lighter
/// part (a pale ash handle, a honed edge, a cream dial, a tan strap) and metal
/// gets a low roughness so the lantern beam catches a glint on it.
/// </summary>
public static class ItemTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, Material> _mat = new();

	// ───────────────────────────── noise kit ─────────────────────────────

	public static float Hash(int x, int y, int seed)
	{
		unchecked
		{
			uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
			h = (h ^ (h >> 13)) * 1274126177u;
			h ^= h >> 16;
			return (h & 0xFFFFFF) / (float)0xFFFFFF;
		}
	}

	private static float Noise(float x, float y, int px, int py, int seed)
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

	/// <summary>Tileable fbm over a w x h image; cx, cy = cells across each axis at the first octave.</summary>
	public static float Fbm(int x, int y, int w, int h, int cx, int cy, int oct, int seed)
	{
		float sum = 0, amp = 0.5f, norm = 0;
		for (int o = 0; o < oct; o++)
		{
			sum += amp * Noise(x / (float)w * cx, y / (float)h * cy, cx, cy, seed + o * 31);
			norm += amp; amp *= 0.5f; cx *= 2; cy *= 2;
		}
		return sum / norm;
	}

	public static Texture2D Make(string key, int w, int h, Func<int, int, Color> f)
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

	private static Color Shade(Color c, float k) => new(c.R * k, c.G * k, c.B * k, c.A);

	// ───────────────────────────── textures ─────────────────────────────

	/// <summary>Forged steel: mottled grey with fine scratches running along U.</summary>
	public static Texture2D Steel() => Make("it_steel", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 4, 4, 3, 11);
		float scratch = Hash(x / 6, y, 12) > 0.93f ? 0.12f : 0f;
		float v = 0.72f + (n - 0.5f) * 0.35f + scratch;
		return new Color(v, v, v * 1.02f);
	});

	/// <summary>Pale ash handle wood: long grain along V, a little grime.</summary>
	public static Texture2D AshWood() => Make("it_ash", 16, 64, (x, y) =>
	{
		float grain = Fbm(x, y, 16, 64, 8, 1, 2, 21);
		float n = Fbm(x, y, 16, 64, 2, 4, 2, 22);
		float v = 0.8f + (grain - 0.5f) * 0.35f + (n - 0.5f) * 0.15f;
		return new Color(v, v, v);
	});

	/// <summary>Tarnished brass: warm, patchy.</summary>
	public static Texture2D Brass() => Make("it_brass", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 3, 3, 3, 31);
		float v = 0.7f + (n - 0.5f) * 0.5f;
		return new Color(v, v * 0.97f, v * 0.9f);
	});

	/// <summary>Chipped enamel paint over metal (the lantern font and cap).</summary>
	public static Texture2D ChippedPaint() => Make("it_paint", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 32, 4, 4, 3, 41);
		bool chip = n > 0.66f;
		float v = chip ? 0.45f : 0.9f + (Hash(x, y, 42) - 0.5f) * 0.1f;
		return chip ? new Color(v * 0.9f, v * 0.9f, v * 0.95f) : new Color(v, v, v);
	});

	/// <summary>Leatherette with a fine pebble grain (camera body).</summary>
	public static Texture2D Leatherette() => Make("it_leatherette", 16, 16, (x, y) =>
	{
		float v = 0.8f + (Hash(x, y, 51) - 0.5f) * 0.3f;
		return new Color(v, v, v);
	});

	/// <summary>Compass card: cream face, dark rim, tick ring, a red N wedge and a black cross.</summary>
	public static Texture2D CompassDial() => Make("it_dial", 64, 64, (x, y) =>
	{
		float u = (x + 0.5f) / 32f - 1f, v = (y + 0.5f) / 32f - 1f;
		float r = Mathf.Sqrt(u * u + v * v);
		float a = Mathf.Atan2(v, u);
		var cream = new Color(0.88f, 0.84f, 0.72f);
		if (r > 0.96f) return new Color(0.2f, 0.17f, 0.12f);
		if (r > 0.8f)
		{
			float ticks = Mathf.Abs(Mathf.Sin(a * 18f));
			return ticks > 0.9f ? new Color(0.12f, 0.11f, 0.1f) : cream;
		}
		// cardinal arms
		float arm = Mathf.Min(Mathf.Abs(u), Mathf.Abs(v));
		if (arm < 0.05f && r < 0.72f)
			return v < 0 && Mathf.Abs(u) < 0.05f ? new Color(0.62f, 0.12f, 0.1f) : new Color(0.14f, 0.13f, 0.12f);
		return Shade(cream, 0.95f + (Hash(x, y, 61) - 0.5f) * 0.06f);
	});

	/// <summary>Speaker grille: horizontal slots on dark plastic.</summary>
	public static Texture2D Grille() => Make("it_grille", 32, 32, (x, y) =>
	{
		bool slot = (y % 5) < 2 && x > 3 && x < 28 && y > 2 && y < 29;
		float v = slot ? 0.08f : 0.55f + (Hash(x, y, 71) - 0.5f) * 0.1f;
		return new Color(v, v, v);
	});

	/// <summary>Moulded plastic with a faint scuffed speckle.</summary>
	public static Texture2D Plastic() => Make("it_plastic", 16, 16, (x, y) =>
	{
		float v = 0.85f + (Fbm(x, y, 16, 16, 2, 2, 2, 81) - 0.5f) * 0.25f;
		return new Color(v, v, v);
	});

	/// <summary>Turned hardwood, varnish worn: grain rings along U.</summary>
	public static Texture2D TurnedWood() => Make("it_turned", 32, 32, (x, y) =>
	{
		float grain = Fbm(x, y, 32, 32, 1, 6, 3, 91);
		float v = 0.75f + (grain - 0.5f) * 0.45f;
		return new Color(v, v * 0.96f, v * 0.9f);
	});

	/// <summary>Leather strap: warm tan, stitching down both edges.</summary>
	public static Texture2D Strap() => Make("it_strap", 8, 32, (x, y) =>
	{
		bool stitch = (x == 1 || x == 6) && (y % 4) < 2;
		float v = stitch ? 1.0f : 0.8f + (Hash(x, y, 101) - 0.5f) * 0.15f;
		return new Color(v, v, v);
	});

	/// <summary>Soft overlapping feather streaks (birds); tinted by vertex colour.</summary>
	public static Texture2D Feather() => Make("it_feather", 16, 16, (x, y) =>
	{
		float streak = Fbm(x, y, 16, 16, 4, 1, 2, 111);
		float v = 0.8f + (streak - 0.5f) * 0.4f + ((y + x / 3) % 4 == 0 ? -0.08f : 0f);
		return new Color(v, v, v);
	});

	// ───────────────────────────── materials ─────────────────────────────

	public static StandardMaterial3D FeatherMat => Std("it_feather", Feather(), 0.9f, 0.2f);

	/// <summary>Textured material; vertex colour tints the texture (MeshKit.Color).</summary>
	public static StandardMaterial3D Std(string key, Texture2D tex, float rough = 0.9f, float specular = 0.3f, bool vertexColor = true)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex,
			Roughness = rough,
			MetallicSpecular = specular,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			VertexColorUseAsAlbedo = vertexColor,
		};
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

	/// <summary>Worked steel: low roughness, strong specular: the beam leaves a glint.</summary>
	public static StandardMaterial3D SteelMat => Std("it_steel", Steel(), 0.32f, 0.85f);
	/// <summary>The honed edge / striking face: bright, near-mirror.</summary>
	public static StandardMaterial3D EdgeMat => (StandardMaterial3D)Cached("it_edge", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.86f, 0.87f, 0.88f),
		Roughness = 0.15f,
		MetallicSpecular = 1f,
		Metallic = 0.35f,
	});
	public static StandardMaterial3D AshMat => Std("it_ash", AshWood(), 0.8f, 0.3f);
	public static StandardMaterial3D BrassMat => Std("it_brass", Brass(), 0.35f, 0.8f);
	public static StandardMaterial3D PaintMat => Std("it_paint", ChippedPaint(), 0.55f, 0.45f);
	public static StandardMaterial3D LeatheretteMat => Std("it_leatherette", Leatherette(), 0.7f, 0.35f);
	public static StandardMaterial3D ChromeMat => Std("it_chrome", Steel(), 0.2f, 1f);
	public static StandardMaterial3D DialMat => Std("it_dial", CompassDial(), 0.6f, 0.3f, false);
	public static StandardMaterial3D GrilleMat => Std("it_grille", Grille(), 0.7f, 0.3f);
	public static StandardMaterial3D PlasticMat => Std("it_plastic", Plastic(), 0.55f, 0.4f);
	public static StandardMaterial3D TurnedWoodMat => Std("it_turned", TurnedWood(), 0.6f, 0.4f);
	public static StandardMaterial3D StrapMat => Std("it_strap", Strap(), 0.8f, 0.25f);

	/// <summary>Clear glass with a faint reflection; transparent so what's under it reads.</summary>
	public static StandardMaterial3D GlassMat => (StandardMaterial3D)Cached("it_glass", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.75f, 0.82f, 0.85f, 0.22f),
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		Roughness = 0.05f,
		MetallicSpecular = 1f,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	});

	/// <summary>Lamp glass lit from inside: warm, softly self-lit, still see-through.</summary>
	public static StandardMaterial3D LampGlassMat => (StandardMaterial3D)Cached("it_lampglass", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(1f, 0.82f, 0.55f, 0.35f),
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		Roughness = 0.1f,
		MetallicSpecular = 0.9f,
		EmissionEnabled = true,
		Emission = new Color(1f, 0.6f, 0.28f),
		EmissionEnergyMultiplier = 0.35f,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	});

	/// <summary>A small wick flame: unshaded, warm.</summary>
	public static StandardMaterial3D FlameMat => (StandardMaterial3D)Cached("it_flame", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(1f, 0.72f, 0.32f),
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		EmissionEnabled = true,
		Emission = new Color(1f, 0.55f, 0.2f),
		EmissionEnergyMultiplier = 2f,
	});

	/// <summary>Dark lens glass: near black with a sharp reflection.</summary>
	public static StandardMaterial3D LensMat => (StandardMaterial3D)Cached("it_lens", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.05f, 0.07f, 0.1f),
		Roughness = 0.05f,
		MetallicSpecular = 1f,
	});

	/// <summary>Plain vertex-coloured material, no texture (small details).</summary>
	public static StandardMaterial3D Tint(string key, Color c, float rough = 0.8f, float specular = 0.3f)
		=> (StandardMaterial3D)Cached("it_tint_" + key, () => new StandardMaterial3D { AlbedoColor = c, Roughness = rough, MetallicSpecular = specular });
}
