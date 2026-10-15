using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.World.StairwellParts;

/// <summary>
/// Procedural textures for Act 14 (Room 3 and the stairwell under it), low-res and filtered like the
/// rest of the game, after the owner's concrete references:
/// <list type="bullet">
/// <item>clean poured concrete: pale, finely speckled and pitted, with faint formwork seams;</item>
/// <item>stained concrete: drips running down from hairline cracks, rust-brown blooms, pits in streaks;</item>
/// <item>grimy concrete: dark and blotched, oily, cracked, a blue-black sheen in places;</item>
/// <item>cinder block with pale salt blooms (the hole's lining), diamond-plate steel treads.</item>
/// </list>
/// The stairwell's walls blend clean to stained to grimy with depth (<c>stairwell_concrete.gdshader</c>).
/// The burnt-out portraits for Room 3 are here too. Deterministic, built once and cached.
/// </summary>
public static class StairwellTextures
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

	/// <summary>Tileable "cracks": thin dark lines where a ridged noise crosses its midline.</summary>
	private static float Crack(float u, float v, int w, int cells, int seed, float width)
	{
		float n = Fbm(u, v, w, cells, 3, seed);
		return Mathf.Clamp(1f - Mathf.Abs(n - 0.5f) / width, 0f, 1f);
	}

	private static Texture2D Make(string key, int w, int h, Func<int, int, Color> f)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var __tg = Systems.TexGen.Start();
		if (Systems.TexCache.Load("StairwellTextures_" + key) is { } __cached) { t = ImageTexture.CreateFromImage(__cached); _tex[key] = t; return t; }
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		img = TexelBoost.Apply(key, img);
		img.GenerateMipmaps();
		Systems.TexCache.Save("StairwellTextures_" + key, img);
		t = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(__tg);
		_tex[key] = t;
		return t;
	}

	private static StandardMaterial3D Std(string key, Texture2D tex, float rough = 0.9f, float spec = 0.25f, float metal = 0f)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex, Roughness = rough, MetallicSpecular = spec, Metallic = metal,
			VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		ProcTextures.AddGrime(s as StandardMaterial3D, key);
		_mat[key] = s;
		return s;
	}

	// ------------------------------------------------------------------ concrete

	/// <summary>Clean poured concrete: pale grey, a fine dark-and-light speckle, small pits, faint
	/// horizontal formwork seams every half tile.</summary>
	// (the baked 1024 px sets since 2026-10-09: the owner, "the texture for all the concrete needs to be drastically
	// upscaled to look more real"; the procedural 128 px ones below are only fallbacks now)
	public static Texture2D CleanConcrete => World.SurfaceSets.Albedo("concrete_clean") ?? CleanConcreteOld;
	public static Texture2D StainedConcrete => World.SurfaceSets.Albedo("concrete_b") ?? StainedConcreteOld;
	public static Texture2D GrimeConcrete => World.SurfaceSets.Albedo("concrete_grime") ?? GrimeConcreteOld;

	private static Texture2D CleanConcreteOld => Make("sw_clean", 128, 128, (x, y) =>
	{
		float n = Fbm(x, y, 128, 4, 4, 201);
		float g = 0.6f + 0.14f * (n - 0.5f) * 2f;
		float sp = Hash(x, y, 202);
		if (sp > 0.93f) g -= 0.16f * (sp - 0.93f) / 0.07f;          // dark aggregate specks
		else if (sp < 0.04f) g += 0.08f;                              // pale ones
		if (Hash(x / 2, y / 2, 203) > 0.985f) g -= 0.18f;             // pits
		if (y % 64 == 0) g -= 0.05f;                                  // formwork seam
		if (y % 64 == 1) g += 0.02f;
		return new Color(g * 0.98f, g, g * 1.02f);
	});

	/// <summary>Stained concrete: hairline cracks, grey-brown drips running down from the upper ones,
	/// rust-coloured blooms, pits pulled into vertical streaks.</summary>
	private static Texture2D StainedConcreteOld => Make("sw_stained", 128, 128, (x, y) =>
	{
		float n = Fbm(x, y, 128, 4, 4, 211);
		float g = 0.52f + 0.16f * (n - 0.5f) * 2f;
		Color c = new(g, g * 0.99f, g * 0.96f);
		// drips: columns that run down from a few sources, fading as they go
		float col = Hash(x, 7, 212);
		if (col > 0.82f)
		{
			float src = Hash(x, 9, 213) * 128f;
			float dy = ((y - src) % 128 + 128) % 128;
			float len = 20 + 60 * Hash(x, 11, 214);
			if (dy < len) c = c.Lerp(new Color(0.3f, 0.29f, 0.25f), 0.55f * (1f - dy / len) * (0.6f + 0.4f * col));
		}
		// pitted streaks
		if (Fbm(x * 3, y * 0.35f, 128, 8, 2, 215) > 0.68f && Hash(x, y, 216) > 0.55f) c *= 0.6f;
		// rust-brown blooms
		float bloom = Fbm(x, y, 128, 3, 3, 217);
		if (bloom > 0.64f) c = c.Lerp(new Color(0.3f, 0.22f, 0.12f), Mathf.Clamp((bloom - 0.64f) * 5f, 0f, 0.7f));
		// cracks
		c = c.Lerp(new Color(0.12f, 0.12f, 0.12f), Crack(x, y, 128, 3, 218, 0.012f) * 0.85f);
		return c;
	});

	/// <summary>Grimy concrete: dark and blotched, oil soaked into it, a web of cracks, and here and
	/// there a blue-black oily sheen.</summary>
	private static Texture2D GrimeConcreteOld => Make("sw_grime", 128, 128, (x, y) =>
	{
		float n = Fbm(x, y, 128, 3, 5, 221);
		float m = Fbm(x, y, 128, 6, 3, 222);
		float g = 0.2f + 0.3f * n + 0.12f * (m - 0.5f);
		Color c = new(g, g * 0.97f, g * 0.92f);
		float oil = Fbm(x, y, 128, 2, 3, 223);
		if (oil > 0.6f) c = c.Lerp(new Color(0.05f, 0.05f, 0.07f), Mathf.Clamp((oil - 0.6f) * 4f, 0f, 0.8f));
		float sheen = Fbm(x, y, 128, 5, 2, 224);
		if (sheen > 0.7f) c = c.Lerp(new Color(0.12f, 0.16f, 0.3f), (sheen - 0.7f) * 2.5f);
		if (Hash(x, y, 225) > 0.9f) c *= 1.35f;                       // pale grit in it
		c = c.Lerp(new Color(0.02f, 0.02f, 0.02f), Crack(x, y, 128, 4, 226, 0.018f));
		c = c.Lerp(new Color(0.04f, 0.04f, 0.04f), Crack(x, y, 128, 2, 227, 0.01f));
		return c;
	});

	/// <summary>Cinder block, 2 x 4 courses per tile: coarse pores, and pale salt blooms creeping out
	/// of the mortar.</summary>
	public static StandardMaterial3D BlockMat => Std("sw_block", Make("sw_block", 128, 128, (x, y) =>
	{
		int row = y / 32;
		int bx = (x + (row % 2) * 32) % 128;
		bool mortar = y % 32 < 2 || bx % 64 < 2;
		float n = Fbm(x, y, 128, 8, 3, 231);
		float g = 0.5f + 0.18f * (n - 0.5f) * 2f;
		if (Hash(x, y, 232) > 0.82f) g -= 0.2f;                       // pores
		if (mortar) g = 0.62f;
		float salt = Fbm(x, y, 128, 4, 3, 233);
		if (salt > 0.62f) g = Mathf.Lerp(g, 0.86f, Mathf.Clamp((salt - 0.62f) * 5f, 0f, 0.9f));
		float damp = Fbm(x, y, 128, 2, 2, 234);
		if (damp > 0.55f) g *= 0.8f;
		return new Color(g, g, g * 0.98f);
	}), 0.95f, 0.2f);

	/// <summary>Plain clean concrete as an ordinary material (Room 3's hole, the landing at the top).</summary>
	public static StandardMaterial3D CleanMat => _cleanM ??= World.SurfaceSets.Apply(Std("sw_clean_m", CleanConcrete, 0.92f, 0.25f), "concrete_clean");
	public static StandardMaterial3D GrimeMat => _grimeM ??= World.SurfaceSets.Apply(Std("sw_grime_m", GrimeConcrete, 0.6f, 0.5f), "concrete_grime");
	public static StandardMaterial3D StainedMat => _stainedM ??= World.SurfaceSets.Apply(Std("sw_stained_m", StainedConcrete, 0.85f, 0.3f), "concrete_b");
	private static StandardMaterial3D _cleanM, _grimeM, _stainedM;
	/// <summary>(2026-10-14b) The same concrete projected in the object's space for Act 14's own pieces (the trench, the
	/// shaft's ceiling, the chamber at the bottom): MeshKit's boxes carry no tangents, so on their UVs the scans' relief
	/// came out flat or lit from the wrong side; a projection needs none.</summary>
	public static StandardMaterial3D CleanProjectedMat => _cleanP ??= Dimmed(Projected((StandardMaterial3D)CleanMat.Duplicate(), 0.4f), 0.66f);
	/// <summary>(2026-10-14h) A step darker: the trench's walls and the shaft's ceiling went pale under the lantern.</summary>
	private static StandardMaterial3D Dimmed(StandardMaterial3D m, float v) { m.AlbedoColor = new Color(v, v * 0.97f, v * 0.93f); return m; }
	public static StandardMaterial3D GrimeProjectedMat => _grimeP ??= Projected((StandardMaterial3D)GrimeMat.Duplicate(), 0.45f);
	private static StandardMaterial3D _cleanP, _grimeP;
	private static StandardMaterial3D Projected(StandardMaterial3D m, float perMetre)
	{
		m.Uv1Triplanar = true;
		m.Uv1WorldTriplanar = false;
		m.Uv1TriplanarSharpness = 6f;
		m.Uv1Scale = Vector3.One * perMetre;
		m.NormalScale = 1.2f;
		return m;
	}

	// ------------------------------------------------------------------ steel

	/// <summary>Diamond-plate steel, dark, the raised bars a little brighter where boots wore them, dirt in the hollows.</summary>
	// (2026-10-14h, the station pass: the look survey found it the busiest thing in frame, a high-contrast, regular diamond
	// a hand across; real plate's bars are a few centimetres, and on old plate they barely show through the dirt. Twice the
	// cells to a tile, a third of the contrast, grime and rust blotches over it.)
	public static StandardMaterial3D TreadMat => Std("sw_tread2", Make("sw_tread2", 128, 128, (x, y) =>
	{
		float n = Fbm(x, y, 128, 4, 3, 241);
		float g = 0.22f + 0.06f * n;
		int u = x % 16, v = y % 16;
		bool a = Mathf.Abs(u - 8 + (v - 8)) < 2 && Mathf.Abs(u - 8) < 6;
		int u2 = (x + 8) % 16, v2 = (y + 8) % 16;
		bool b = Mathf.Abs(u2 - 8 - (v2 - 8)) < 2 && Mathf.Abs(u2 - 8) < 6;
		float dirt = Mathf.SmoothStep(0.4f, 0.75f, Fbm(x + 17, y + 3, 128, 3, 3, 242));
		if (a || b) g += 0.045f * (1f - dirt * 0.8f);
		var c = new Color(g, g * 1.0f, g * 1.02f);
		c = c.Lerp(new Color(0.11f, 0.095f, 0.08f), dirt * 0.6f);
		float rust = Mathf.SmoothStep(0.62f, 0.8f, Fbm(x + 50, y + 9, 128, 4, 3, 243));
		return c.Lerp(new Color(0.22f, 0.12f, 0.07f), rust * 0.55f);
	}), 0.62f, 0.5f, 0.45f);

	/// <summary>Painted structural steel (stringers, the frame under the flights): a dull grey-green.</summary>
	public static StandardMaterial3D SteelMat => Std("sw_steel", Make("sw_steel", 32, 32, (x, y) =>
	{
		float n = Fbm(x, y, 32, 4, 3, 251);
		float g = 0.28f + 0.1f * n;
		if (Hash(x, y, 252) > 0.95f) return new Color(0.3f, 0.18f, 0.1f);   // chips of rust
		return new Color(g * 0.9f, g, g * 0.95f);
	}), 0.7f, 0.4f, 0.4f);

	/// <summary>The black railings.</summary>
	public static StandardMaterial3D RailMat => (StandardMaterial3D)(_mat.TryGetValue("sw_rail", out var m) ? m
		: _mat["sw_rail"] = new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.035f, 0.04f), Roughness = 0.45f, Metallic = 0.5f, MetallicSpecular = 0.6f });

	/// <summary>Riveted steel plate for Room 3's walls, painted and flaking: a metre-wide panel to each half tile, a rivet
	/// row down each seam, the seams dark.</summary>
	// (2026-10-13g, the texture-mood pass: the plates read clean, pale and polished, a hot glint under the lantern;
	// now darker and duller, grime and rust weeping from the seams and the rivets, a damp bloom low on each plate)
	// (2026-10-14h, the station pass: the survey read it as smeared, blurry, low-frequency rust, an upscaled photo, under
	// cold "lab" lamps. Painted steel now, the paint a dull institutional grey-green flaking off to rust: sharp-edged flakes,
	// a paler lip where the paint lifts, rivet rows down the seams; at twice the resolution.)
	// (2026-10-15, the scan: one 128 px flake tile printed on every plate, upscaled, its hard threshold stair-stepped. Now
	// 1024 px over a 4 x 4 m tile (Room 3 maps it at uv 0.25): eight plates, 1 x 2 m, each its own paint shade, its own
	// patch of the flake field and its own amount of flaking (some nearly sound, some half gone), under a slow wall-scale
	// drift; the flakes' edges smoothstepped, the rivets round.)
	public const int PlatePx = 1024, PlateW = 256, PlateH = 512;
	public static StandardMaterial3D PlateMat => Std("sw_plate3", Make("sw_plate3", PlatePx, PlatePx, (x, y) =>
	{
		const int W = PlatePx;
		int col = x / PlateW, row = y / PlateH, u = x % PlateW, v = y % PlateH, pid = col + row * 4;
		float shade = 0.84f + 0.26f * Hash(pid, 0, 281), bias = (Hash(pid, 1, 281) - 0.5f) * 0.16f;
		int ox = (int)(Hash(pid, 2, 281) * W), oy = (int)(Hash(pid, 3, 281) * W);
		float n = Fbm(x, y, W, 12, 3, 261);
		var paint = new Color(0.25f, 0.28f, 0.245f).Lerp(new Color(0.27f, 0.27f, 0.23f), Hash(pid, 4, 281)) * (0.88f + 0.18f * n) * shade;
		// the paint chalked and dirtied toward each plate's foot
		paint = paint.Lerp(new Color(0.17f, 0.17f, 0.15f), (v / (float)PlateH) * 0.35f + Mathf.SmoothStep(0.5f, 0.8f, Fbm(x, y, W, 6, 3, 262)) * 0.3f);
		// flaking: where the paint has gone, rust; a lighter lip round each flake. Worse along the seams.
		float dv = Mathf.Min(u, PlateW - u), dh = Mathf.Min(v, PlateH - v), ds = Mathf.Min(dv, dh);
		float f = Fbm(x + ox, y + oy, W, 32, 3, 263) * 0.75f + Fbm(x + ox, y + oy, W, 8, 2, 266) * 0.35f
			+ (Fbm(x, y, W, 2, 2, 267) - 0.5f) * 0.22f + bias + 0.09f * (1f - Mathf.SmoothStep(4f, 26f, ds));
		float gone = Mathf.SmoothStep(0.655f, 0.672f, f), lip = Mathf.SmoothStep(0.625f, 0.648f, f) * (1f - gone);
		var rust = new Color(0.27f, 0.15f, 0.08f) * (0.8f + 0.4f * Fbm(x + ox, y + oy, W, 64, 2, 264));
		Color c = paint.Lerp(paint * 1.22f, lip).Lerp(rust, gone);
		// rust weeping down from the rivets and the flakes
		float run = Mathf.SmoothStep(0.6f, 0.85f, Fbm(x * 4f, y * 0.25f, W, 16, 2, 265));
		c = c.Lerp(new Color(0.24f, 0.13f, 0.07f), run * 0.35f);
		// the rivets: round heads 16 px in from each seam, 40 px apart, lit on top, a shadow under
		float rv = float.MaxValue;
		if (dv is > 8 and < 24) { float ry = ((v + 20) % 40) - 20f; rv = Mathf.Sqrt((dv - 16) * (dv - 16) + ry * ry); }
		if (dh is > 8 and < 24) { float rx = ((u + 20) % 40) - 20f; rv = Mathf.Min(rv, Mathf.Sqrt((dh - 16) * (dh - 16) + rx * rx)); }
		float head = Mathf.SmoothStep(4.8f, 3.4f, rv);
		c = c.Lerp(new Color(0.34f, 0.29f, 0.23f) * shade, head * 0.65f);
		// the seams: a dark gap, soft-edged, a shadowed lip beside it
		c *= Mathf.Lerp(0.2f, 1f, Mathf.SmoothStep(1.2f, 3.2f, ds)) * Mathf.Lerp(0.78f, 1f, Mathf.SmoothStep(3f, 7f, ds));
		return c;
	}), 0.78f, 0.35f, 0.25f);

	/// <summary>The ceilings over Room 3: sooted, dark painted plate, its seams faint, a fine speckle of rust.</summary>
	public static StandardMaterial3D SootPlateMat => Std("sw_sootplate", Make("sw_sootplate", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 3, 271);
		var c = new Color(0.13f, 0.13f, 0.12f) * (0.85f + 0.3f * n);
		if (x % 32 == 0 || y % 64 == 0) c *= 0.55f;
		float rust = Mathf.SmoothStep(0.66f, 0.82f, Fbm(x + 5, y + 21, 64, 8, 3, 272));
		return c.Lerp(new Color(0.2f, 0.12f, 0.07f), rust * 0.6f);
	}), 0.85f, 0.25f, 0.2f);

	// ------------------------------------------------------------------ the burnt portraits

	/// <summary>An old oil portrait with the face burnt out of it: dark varnished ground, the sitter's
	/// shoulders and clothes, and where the face was a charred hole with a scorched brown ring and a
	/// ragged edge. <paramref name="i"/> picks the sitter (clothes, background, pose, the burn's shape).</summary>
	/// <summary>A stable 0..1 number for portrait <paramref name="i"/>'s choice <paramref name="k"/> (its frame, its burn).</summary>
	public static float PortraitHash(int i, int k) => Hash(i * 7 + 3, k * 13 + 1, 9177);

	// (2026-10-15, the scan: forty-odd near-identical sitters with the same round hole and a white collar plate, 64 px and
	// stair-stepped. Now 128 x 160, every edge anti-aliased; four kinds of sitter (a gentleman in a stock, a lady with her
	// hair up, an officer with epaulettes, a cleric in bands), placed and sized each their own way, some before a curtain;
	// the burn its own size and lean, climbing as flame does, its soot scorched up the paint above it.)
	private struct Sitter
	{
		public int Kind;
		public Color Bg, Cloth, Skin, Hair, Trim;
		public float Cx, HeadY, HeadR, Lean, ShoulderW, BurnX, BurnY, BurnR, Climb;
		public bool Curtain, CurtainLeft;
	}

	public static Texture2D Portrait(int i)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(9000 + i * 31) };
		Color[] grounds = { new(0.16f, 0.13f, 0.08f), new(0.1f, 0.13f, 0.1f), new(0.2f, 0.1f, 0.07f), new(0.09f, 0.09f, 0.1f), new(0.22f, 0.17f, 0.1f) };
		Color[] cloths = { new(0.05f, 0.045f, 0.05f), new(0.07f, 0.08f, 0.16f), new(0.3f, 0.06f, 0.05f), new(0.18f, 0.13f, 0.08f), new(0.12f, 0.15f, 0.1f), new(0.24f, 0.2f, 0.22f) };
		var s = new Sitter
		{
			Kind = rng.RandiRange(0, 3),
			Bg = grounds[rng.RandiRange(0, grounds.Length - 1)] * rng.RandfRange(0.8f, 1.2f),
			Cloth = cloths[rng.RandiRange(0, cloths.Length - 1)] * rng.RandfRange(0.8f, 1.2f),
			Skin = new Color(0.72f, 0.56f, 0.44f) * rng.RandfRange(0.75f, 1.05f),
			Hair = new Color(0.1f, 0.07f, 0.05f).Lerp(new Color(0.32f, 0.22f, 0.12f), rng.Randf() * rng.Randf()).Lerp(new Color(0.55f, 0.53f, 0.5f), rng.Randf() < 0.2f ? 0.8f : 0f),
			Cx = 64 + rng.RandfRange(-12f, 12f), HeadY = rng.RandfRange(48f, 70f), HeadR = rng.RandfRange(16f, 24f), Lean = rng.RandfRange(-6f, 6f),
			ShoulderW = rng.RandfRange(44f, 60f), Curtain = rng.Randf() < 0.35f, CurtainLeft = rng.Randf() < 0.5f,
		};
		s.Trim = s.Kind == 2 ? new Color(0.62f, 0.48f, 0.2f) : new Color(0.62f, 0.6f, 0.52f) * rng.RandfRange(0.75f, 1f);
		if (s.Kind == 2) s.Cloth = rng.Randf() < 0.5f ? new Color(0.32f, 0.05f, 0.04f) : new Color(0.06f, 0.08f, 0.2f);
		if (s.Kind == 3) s.Cloth = new Color(0.04f, 0.035f, 0.035f);
		s.BurnX = s.Cx + rng.RandfRange(-5f, 5f);
		s.BurnY = s.HeadY + rng.RandfRange(-4f, 6f);
		s.BurnR = s.HeadR * rng.RandfRange(0.8f, 1.45f);
		s.Climb = rng.RandfRange(0.55f, 0.9f);
		return Make($"sw_portrait2_{i}", 128, 160, (x, y) => PortraitPixel(x, y, i, s));
	}

	/// <summary>How much of a pixel an ellipse covers, from its normalized radius at the pixel and its size in pixels.</summary>
	private static float Cover(float rNorm, float radiusPx) => Mathf.Clamp(0.5f - (rNorm - 1f) * radiusPx, 0f, 1f);

	private static Color PortraitPixel(int x, int y, int i, Sitter s)
	{
		float n = Fbm(x, y, 128, 6, 3, 271 + i);
		float bn = Fbm(x * 3, y * 0.6f, 128, 8, 2, 272 + i);   // brushwork, longer up and down
		// ground, darker to the edges, a lighter halo behind the head as painters did
		float vign = 1f - 0.55f * Mathf.Clamp(new Vector2((x - 64) / 64f, (y - 80) / 80f).Length(), 0f, 1f);
		Color c = s.Bg * (0.75f + 0.45f * n) * vign;
		c = c.Lerp(s.Bg * 1.7f, Mathf.Clamp(1f - new Vector2(x - s.Cx, y - s.HeadY).Length() / 60f, 0f, 1f) * 0.4f);
		if (s.Curtain)
		{
			// a heavy drape down one side, its folds
			float edgeX = s.CurtainLeft ? 26f + 8f * Mathf.Sin(y * 0.05f) : 102f + 8f * Mathf.Sin(y * 0.05f);
			float inC = s.CurtainLeft ? Mathf.Clamp(edgeX - x + 0.5f, 0f, 1f) : Mathf.Clamp(x - edgeX + 0.5f, 0f, 1f);
			var drape = new Color(0.26f, 0.05f, 0.05f) * (0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(x * 0.45f + bn * 3f)));
			c = c.Lerp(drape * vign, inC);
		}
		// shoulders and body: a broad rounded mass from under the head down off the canvas
		float neckY = s.HeadY + s.HeadR * 1.2f;
		float sx = (x - s.Cx - s.Lean * (y - neckY) / 80f) / s.ShoulderW, sy = (y - (neckY + 22f)) / 30f;
		float body = sy < 0 ? Cover(Mathf.Sqrt(sx * sx + sy * sy), s.ShoulderW) : Cover(Mathf.Abs(sx), s.ShoulderW);
		var cloth = s.Cloth * (0.7f + 0.45f * bn) * (1f - 0.25f * Mathf.Clamp(sx, 0f, 1f));
		c = c.Lerp(cloth, body);
		// the neck
		float nx = (x - s.Cx) / (s.HeadR * 0.42f);
		c = c.Lerp(s.Skin * 0.75f * (0.85f + 0.3f * n), Cover(Mathf.Abs(nx), s.HeadR * 0.42f) * (y > s.HeadY && y < neckY + 10 ? 1f : 0f));
		// what's at the throat: a white stock, a lace collar, the officer's high gold collar, the cleric's bands
		float tx = Mathf.Abs(x - s.Cx), ty = y - neckY;
		float trim = s.Kind switch
		{
			0 => Cover(Mathf.Max(tx / (10f + ty * 0.5f), Mathf.Abs(ty - 6f) / 10f), 6f),
			1 => Cover(Mathf.Max(tx / 22f, Mathf.Abs(ty - 2f) / 4f), 4f) * (0.6f + 0.4f * Hash(x / 2, y / 2, 300 + i)),
			2 => Cover(Mathf.Max(tx / 13f, Mathf.Abs(ty - 3f) / 4f), 4f),
			_ => Cover(Mathf.Max(Mathf.Abs(tx - 3.5f) / 3f, Mathf.Abs(ty - 9f) / 9f), 3f),
		};
		c = c.Lerp(s.Trim * (0.75f + 0.35f * n), trim);
		if (s.Kind == 2)
		{
			// epaulettes and a row of buttons
			foreach (float side in new[] { -1f, 1f })
			{
				float ex = (x - (s.Cx + side * s.ShoulderW * 0.72f)) / 13f, ey = (y - (neckY + 14f)) / 5f;
				c = c.Lerp(s.Trim * (0.7f + 0.5f * bn), Cover(Mathf.Sqrt(ex * ex + ey * ey), 6f));
			}
			for (int b = 0; b < 4; b++)
				c = c.Lerp(s.Trim, Cover(new Vector2(x - s.Cx, y - (neckY + 18f + b * 11f)).Length() / 2f, 2f));
		}
		// the head: hair round a face that is no longer there; a lady's piled up wider, an old man's thin on top
		float hw = s.HeadR * (s.Kind == 1 ? 1.32f : 1.08f), hh = s.HeadR * (s.Kind == 1 ? 1.45f : 1.3f);
		float hr = new Vector2((x - s.Cx) / hw, (y - s.HeadY + (s.Kind == 1 ? 6f : 2f)) / hh).Length();
		c = c.Lerp(s.Hair * (0.75f + 0.5f * bn), Cover(hr, hw) * (s.Kind == 3 && y < s.HeadY - s.HeadR * 0.6f ? 0.35f : 1f));
		if (s.Kind == 1)
			c = c.Lerp(s.Hair * 0.9f, Cover(new Vector2((x - s.Cx) / 11f, (y - (s.HeadY - hh - 4f)) / 9f).Length(), 9f));
		float fr = new Vector2((x - s.Cx) / s.HeadR, (y - s.HeadY) / (s.HeadR * 1.25f)).Length();
		c = c.Lerp(s.Skin * (0.8f + 0.3f * n), Cover(fr, s.HeadR));
		// the burn: a ragged hole climbing up from the face (flame goes up), charred black, a crisp brown scorch round it,
		// blistered and browned beyond that
		float dx = x - s.BurnX, dy = y - s.BurnY;
		float bd = new Vector2(dx, dy < 0 ? dy * s.Climb : dy * 1.15f).Length() / s.BurnR;
		float edge = 0.78f + 0.4f * Fbm(x * 2, y * 2, 128, 10, 3, 281 + i);
		float br = bd / edge;
		c = c.Lerp(new Color(0.3f, 0.17f, 0.07f) * (0.8f + 0.4f * n), Mathf.SmoothStep(1.55f, 1.1f, br) * 0.75f);
		c = c.Lerp(new Color(0.1f, 0.05f, 0.02f), Mathf.SmoothStep(1.12f, 1.0f, br));
		c = c.Lerp(new Color(0.012f, 0.01f, 0.008f) * (0.8f + 0.5f * Hash(x, y, 290 + i)), Mathf.SmoothStep(0.97f, 0.9f, br));
		// its soot, scorched up the paint above it: a plume widening as it rises, patchy
		if (dy < 0)
		{
			float up = -dy, pw = s.BurnR * (1f + up / 45f);
			float plume = Mathf.Clamp(1f - Mathf.Abs(dx) / pw, 0f, 1f) * Mathf.Clamp(1f - up / 110f, 0f, 1f);
			c *= 1f - 0.75f * plume * Mathf.SmoothStep(0.25f, 0.7f, Fbm(x + 40, y * 0.7f, 128, 8, 3, 299 + i));
		}
		// old varnish yellowed and cracking all over
		c = c.Lerp(c * new Color(1.05f, 0.95f, 0.7f), 0.3f);
		c *= 1f - 0.25f * Crack(x, y, 128, 10, 295 + i, 0.025f);
		return c;
	}
}
