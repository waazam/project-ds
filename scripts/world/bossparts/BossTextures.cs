using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.World.BossParts;

/// <summary>
/// Procedural textures for Act 18's boss room, after the owner's references: galvanized steel
/// grating for the catwalk, pipe insulation (canvas-wrapped with banding, crinkled foil), painted and
/// black steel, and pressure-gauge faces.
/// </summary>
public static class BossTextures
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
		if (Systems.TexCache.Load("BossTextures_" + key) is { } __cached) { t = ImageTexture.CreateFromImage(__cached); _tex[key] = t; return t; }
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		img = TexelBoost.Apply(key, img);
		img.GenerateMipmaps();
		Systems.TexCache.Save("BossTextures_" + key, img);
		t = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
		_tex[key] = t;
		return t;
	}

	private static StandardMaterial3D Std(string key, Func<StandardMaterial3D> make)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = make();
		ProcTextures.AddGrime(s as StandardMaterial3D, key);
		_mat[key] = s;
		return s;
	}

	/// <summary>Galvanized bar grating: bearing bars one way, twisted cross bars the other, dark gaps.</summary>
	public static StandardMaterial3D GratingMat => Std("bt_grating", () => new StandardMaterial3D
	{
		AlbedoTexture = Make("bt_grating", 64, 64, (x, y) =>
		{
			bool bear = x % 8 < 2, cross = y % 16 < 2;
			float n = Fbm(x, y, 64, 4, 3, 401);
			if (bear || cross) { float g = 0.55f + 0.2f * n + (cross ? 0.05f : 0f); return new Color(g * 0.95f, g, g * 1.02f); }
			return new Color(0.05f, 0.05f, 0.05f, 0f);   // the gaps: see-through, down to whatever is below
		}),
		Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.9f,
		Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		Roughness = 0.5f, Metallic = 0.6f, MetallicSpecular = 0.6f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	});

	/// <summary>Galvanized tube: dull silver, a little mottled.</summary>
	public static StandardMaterial3D GalvanizedMat => SurfaceSets.Scan(Std("bt_galv", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.62f, 0.64f, 0.66f), Roughness = 0.72f, Metallic = 0.55f, MetallicSpecular = 0.5f,
		Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.6f,
	}), "metal_plate_02", 1f, 1f, new Color(0.56f, 0.56f, 0.55f));

	/// <summary>Canvas-wrapped insulation, cream to tan, a band every half metre.</summary>
	public static StandardMaterial3D WrapMat(Color tint) => Std($"bt_wrap{tint.ToHtml()}", () => new StandardMaterial3D
	{
		// (2026-10-13g, the texture-mood pass: clean pale cream tubes that glared under the lamps; now old lagging, darker,
		// water-stained in tide marks and blotches, soot along it, frayed darker at the bands)
		AlbedoTexture = Make("bt_wrap", 32, 64, (x, y) =>
		{
			float n = Fbm(x, y, 32, 4, 3, 411);
			float g = 0.72f + 0.14f * n;
			if (y % 32 < 2) g *= 0.6f;   // a band
			else if (y % 32 < 4 || y % 32 > 29) g *= 0.85f;
			float stain = Mathf.SmoothStep(0.5f, 0.72f, Fbm(x, y, 32, 3, 3, 412));
			float soot = Mathf.SmoothStep(0.4f, 0.8f, Fbm(x * 0.5f, y * 2f, 32, 2, 2, 413));
			var c = new Color(g, g * 0.96f, g * 0.88f);
			c = c.Lerp(new Color(0.36f, 0.28f, 0.18f), stain * 0.55f);
			return c * (1f - 0.3f * soot);
		}),
		AlbedoColor = tint * 0.68f, Roughness = 0.95f, MetallicSpecular = 0.15f, VertexColorUseAsAlbedo = true,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	});

	/// <summary>Crinkled foil insulation.</summary>
	public static StandardMaterial3D FoilMat => Std("bt_foil", () => new StandardMaterial3D
	{
		AlbedoTexture = Make("bt_foil", 64, 64, (x, y) =>
		{
			float n = Fbm(x * 2f, y, 64, 8, 3, 421);
			float g = 0.55f + 0.35f * Mathf.Abs(n - 0.5f) * 2f;
			return new Color(g, g, g * 1.03f);
		}),
		AlbedoColor = new Color(0.62f, 0.6f, 0.56f), Roughness = 0.5f, Metallic = 0.75f, MetallicSpecular = 0.6f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	});

	public static StandardMaterial3D Painted(Color c, float rough = 0.5f, float metal = 0.3f) => Std($"bt_paint{c.ToHtml()}", () => SurfaceSets.Relief(new StandardMaterial3D
	{
		// (2026-10-13g: no fresh gloss down here; every painted pipe is dulled with grime)
		// (2026-10-14h, the old-and-abandoned pass: and old: the paint chipped and flaking back to rust, rust bled down
		// from every chip and seam, scale in the low places; it was a clean flat colour, so the pipes read as new tubes)
		AlbedoTexture = RustyPaint(c), AlbedoColor = Colors.White, Roughness = Mathf.Max(rough, 0.72f), Metallic = metal * 0.6f, MetallicSpecular = 0.4f,
		Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.75f, Uv1TriplanarSharpness = 4f,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, VertexColorUseAsAlbedo = true,
	}, "ph_rusty_metal_02", 0.55f));

	/// <summary>Old paint in colour <paramref name="c"/> on steel: chipped in flakes to rust, rust run down from the
	/// chips (the texture's v is down the world's y on a pipe's side), scale and grime in blotches. 128 px, 1.3 m a repeat.</summary>
	public static Texture2D RustyPaint(Color c)
	{
		var f = RustField;
		return Make($"bt_rustpaint{c.ToHtml(false)}", 128, 128, (x, y) =>
		{
			int i = y * 128 + x;
			float n = f.n[i], fine = f.fine[i], chip = f.chip[i], run = f.run[i];
			var rust = new Color(0.32f, 0.17f, 0.08f).Lerp(new Color(0.48f, 0.27f, 0.12f), f.tone[i]);
			// the paint: its own colour, faded and dirtied in broad blotches
			var paint = c * (0.78f + 0.3f * n);
			paint = paint.Lerp(new Color(0.3f, 0.27f, 0.22f), 0.18f + 0.25f * Mathf.SmoothStep(0.55f, 0.8f, n));
			var col = paint.Lerp(rust * 1.15f, run * 0.55f);
			if (chip > 0.61f) col = rust * (0.8f + 0.4f * fine);
			else if (chip > 0.585f) col *= 0.7f;   // the flaking edge
			return new Color(col.R, col.G, col.B);
		});
	}

	private static (float[] n, float[] fine, float[] chip, float[] tone, float[] run) _rustField;

	/// <summary>The chips, the blotches and the rust runs, the same under every colour (drawn once).</summary>
	private static (float[] n, float[] fine, float[] chip, float[] tone, float[] run) RustField
	{
		get
		{
			if (_rustField.n != null) return _rustField;
			const int S = 128;
			var n = new float[S * S]; var fine = new float[S * S]; var chip = new float[S * S]; var tone = new float[S * S]; var run = new float[S * S];
			for (int y = 0; y < S; y++)
				for (int x = 0; x < S; x++)
				{
					int i = y * S + x;
					n[i] = Fbm(x, y, S, 4, 4, 431);
					fine[i] = Fbm(x, y, S, 16, 2, 432);
					chip[i] = Fbm(x, y, S, 6, 3, 433) * 0.7f + fine[i] * 0.3f;
					tone[i] = Fbm(x, y, S, 8, 3, 434);
				}
			// rust bleeding down from above: a run under every chip, longer here and there
			for (int y = 0; y < S; y++)
				for (int x = 0; x < S; x++)
				{
					float r = 0f;
					if (Hash(x / 2, 0, 435) > 0.35f)
						for (int k = 1; k < 26; k++)
							if (chip[((y - k + S) % S) * S + x] > 0.6f) { r = 1f - k / 26f; break; }
					run[y * S + x] = r;
				}
			return _rustField = (n, fine, chip, tone, run);
		}
	}

	/// <summary>A pressure gauge's face: white, black ticks round three quarters of it, a red arc near
	/// the top of the scale.</summary>
	public static Texture2D GaugeFace => Make("bt_gauge", 64, 64, (x, y) =>
	{
		float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
		if (r > 31f) return new Color(0.15f, 0.15f, 0.15f);
		if (r > 29f) return new Color(0.3f, 0.3f, 0.32f);
		float a = Mathf.Atan2(dx, -dy);   // 0 at the top, clockwise
		float frac = (a + 2.36f) / 4.71f;  // the scale runs from -135 to +135 degrees
		Color c = new(0.93f, 0.92f, 0.88f);
		if (frac >= 0f && frac <= 1f)
		{
			if (r > 22f && r < 27f && Mathf.Abs(Mathf.Sin(frac * Mathf.Pi * 10f)) < 0.12f) c = new Color(0.1f, 0.1f, 0.1f);
			if (r > 24f && r < 27f && Mathf.Abs(Mathf.Sin(frac * Mathf.Pi * 50f)) < 0.2f) c = new Color(0.25f, 0.25f, 0.25f);
			if (frac > 0.72f && r > 18f && r < 21f) c = new Color(0.8f, 0.1f, 0.08f);
		}
		return c;
	});
}
