using System;
using System.Collections.Generic;
using Godot;

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
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
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
		_mat[key] = s;
		return s;
	}

	// ------------------------------------------------------------------ concrete

	/// <summary>Clean poured concrete: pale grey, a fine dark-and-light speckle, small pits, faint
	/// horizontal formwork seams every half tile.</summary>
	public static Texture2D CleanConcrete => Make("sw_clean", 128, 128, (x, y) =>
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
	public static Texture2D StainedConcrete => Make("sw_stained", 128, 128, (x, y) =>
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
	public static Texture2D GrimeConcrete => Make("sw_grime", 128, 128, (x, y) =>
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
	public static StandardMaterial3D CleanMat => Std("sw_clean_m", CleanConcrete, 0.92f, 0.25f);
	public static StandardMaterial3D GrimeMat => Std("sw_grime_m", GrimeConcrete, 0.6f, 0.5f);

	// ------------------------------------------------------------------ steel

	/// <summary>Diamond-plate steel, dark and worn bright on the raised diamonds.</summary>
	public static StandardMaterial3D TreadMat => Std("sw_tread", Make("sw_tread", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 3, 241);
		float g = 0.2f + 0.08f * n;
		int u = x % 16, v = y % 16;
		bool a = Mathf.Abs(u - 8 + (v - 8)) < 2 && Mathf.Abs(u - 8) < 6;
		int u2 = (x + 8) % 16, v2 = (y + 8) % 16;
		bool b = Mathf.Abs(u2 - 8 - (v2 - 8)) < 2 && Mathf.Abs(u2 - 8) < 6;
		if (a || b) g += 0.14f;
		return new Color(g, g * 1.01f, g * 1.04f);
	}), 0.55f, 0.6f, 0.6f);

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

	/// <summary>Riveted clean steel plate for Room 3's walls: cold blue-grey panels, a rivet row round
	/// each, the seams dark.</summary>
	public static StandardMaterial3D PlateMat => Std("sw_plate", Make("sw_plate", 64, 64, (x, y) =>
	{
		float n = Fbm(x, y, 64, 4, 3, 261);
		float g = 0.36f + 0.1f * n;
		int u = x % 32, v = y % 64;
		if (u == 0 || v == 0) g = 0.12f;
		else if ((u == 3 || u == 29) && v % 6 == 3) g = 0.52f;
		else if ((v == 3 || v == 61) && u % 6 == 3) g = 0.52f;
		return new Color(g * 0.92f, g * 0.97f, g * 1.05f);
	}), 0.5f, 0.55f, 0.55f);

	// ------------------------------------------------------------------ the burnt portraits

	/// <summary>An old oil portrait with the face burnt out of it: dark varnished ground, the sitter's
	/// shoulders and clothes, and where the face was a charred hole with a scorched brown ring and a
	/// ragged edge. <paramref name="i"/> picks the sitter (clothes, background, pose, the burn's shape).</summary>
	public static Texture2D Portrait(int i)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(9000 + i * 31) };
		Color bg = new Color(rng.RandfRange(0.12f, 0.28f), rng.RandfRange(0.1f, 0.2f), rng.RandfRange(0.06f, 0.14f));
		Color cloth = new Color(rng.RandfRange(0.05f, 0.35f), rng.RandfRange(0.04f, 0.2f), rng.RandfRange(0.04f, 0.25f));
		Color skin = new Color(0.72f, 0.56f, 0.44f) * rng.RandfRange(0.8f, 1.05f);
		float cx = 32 + rng.RandfRange(-4f, 4f), headY = 30 + rng.RandfRange(-3f, 3f);
		float headR = rng.RandfRange(10f, 13f);
		return Make($"sw_portrait{i}", 64, 80, (x, y) => PortraitPixel(x, y, i, bg, cloth, skin, cx, headY, headR));
	}

	private static Color PortraitPixel(int x, int y, int i, Color bg, Color cloth, Color skin, float cx, float headY, float headR)
	{
		float n = Fbm(x, y, 64, 4, 3, 271 + i);
		// ground, darker to the edges, a lighter halo behind the head as painters did
		float vign = 1f - 0.5f * Mathf.Clamp(new Vector2((x - 32) / 32f, (y - 40) / 40f).Length(), 0f, 1f);
		Color c = bg * (0.7f + 0.5f * n) * vign;
		c = c.Lerp(bg * 1.8f, Mathf.Clamp(1f - new Vector2(x - cx, y - headY).Length() / 30f, 0f, 1f) * 0.4f);
		// shoulders and body
		float dx = (x - cx) / 26f, dy = (y - (headY + headR + 14)) / 18f;
		if (dy > 0 && dx * dx + dy * dy * 0.25f < 1f || y > headY + headR + 14 && Mathf.Abs(x - cx) < 26) c = cloth * (0.7f + 0.5f * n);
		// neck and the collar
		if (Mathf.Abs(x - cx) < 4 && y > headY + headR - 2 && y < headY + headR + 12) c = skin * 0.8f;
		if (Mathf.Abs(x - cx) < 8 && y > headY + headR + 8 && y < headY + headR + 13) c = new Color(0.8f, 0.78f, 0.7f) * (0.8f + 0.3f * n);
		// the head: hair round a face that is no longer there
		float r = new Vector2((x - cx) / headR, (y - headY) / (headR * 1.25f)).Length();
		if (r < 1.15f) c = new Color(0.1f, 0.07f, 0.05f) * (0.8f + 0.4f * n);
		// the burn: a ragged-edged hole over the face, scorched brown round it, blistered beyond that
		float edge = 0.75f + 0.35f * Fbm(x * 2, y * 2, 128, 6, 3, 281 + i);
		float br = new Vector2((x - cx) / headR, (y - headY - 1) / (headR * 1.1f)).Length() / edge;
		if (br < 1.35f) c = c.Lerp(new Color(0.35f, 0.18f, 0.06f), Mathf.Clamp((1.35f - br) / 0.3f, 0f, 1f) * 0.8f);
		if (br < 1.08f) c = new Color(0.12f, 0.06f, 0.02f);
		if (br < 0.92f) c = new Color(0.015f, 0.012f, 0.01f) * (0.8f + 0.5f * Hash(x, y, 290 + i));
		// old varnish cracking all over
		if (Crack(x, y, 64, 6, 295 + i, 0.02f) > 0.5f) c *= 0.75f;
		return c;
	}
}
