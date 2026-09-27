using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.ChurchParts;

/// <summary>
/// Act 21's textures, after the owner's cathedral and crypt references: pale limestone ashlar for the
/// piers and walls, a warm plaster for the vault webs between the ribs, polished cream marble with grey
/// veins for the nave floor and a red-brown marble for its aisle, orange-brown brick for the crypt's
/// vaults over a black-and-white chequered floor, dark oak for the pews and doors, gilding for the altar's
/// canopy, the stained glass (tall lancets and the rose), weathered planks for the old wooden stair, and
/// snow. Drawn at their native sizes and raised to the game's texel density by <see cref="TexelBoost"/>
/// like every other factory; deterministic and cached.
/// </summary>
public static class ChurchTextures
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

	/// <summary>Tileable fbm over a w-wide image (u,v in pixels).</summary>
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

	private static Texture2D Make(string key, int w, int h, Func<int, int, Color> f, bool boost = true)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				img.SetPixel(x, y, f(x, y));
		if (boost) img = TexelBoost.Apply(key, img);
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
		_tex[key] = t;
		return t;
	}

	/// <summary>A world-triplanar surface (no UV seams on the big stone: <paramref name="metresPerTile"/> a repeat).</summary>
	private static StandardMaterial3D Tri(string key, Texture2D tex, float metresPerTile, float rough, float spec = 0.3f, float metal = 0f)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex, Roughness = rough, MetallicSpecular = spec, Metallic = metal,
			VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
			Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One / metresPerTile, Uv1TriplanarSharpness = 4f,
		};
		_mat[key] = s;
		return s;
	}

	/// <summary>A UV-mapped surface (MeshKit's UVs are in metres: <paramref name="metresPerTile"/> a repeat).</summary>
	private static StandardMaterial3D Uv(string key, Texture2D tex, float metresPerTile, float rough, float spec = 0.3f, float metal = 0f)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex, Roughness = rough, MetallicSpecular = spec, Metallic = metal,
			VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
			Uv1Scale = Vector3.One / metresPerTile,
		};
		_mat[key] = s;
		return s;
	}

	// ------------------------------------------------------------------ stone

	/// <summary>Limestone ashlar: courses half a metre high (a 2 m tile), blocks of varied length in running
	/// bond, each its own shade of cream, fine pale mortar joints, grain and pitting, a little soot low down
	/// on each block's face.</summary>
	public static Texture2D Ashlar => Make("ch_ashlar", 256, 256, (x, y) =>
	{
		int course = y / 64;
		int off = (course % 2) * 37 + course * 11;
		int bx = ((x + off) % 256);
		// block boundaries: a few irregular lengths per course
		int[] cuts = { 0, 71, 150, 203 };
		int bi = 0;
		for (int i = 0; i < cuts.Length; i++) if (bx >= cuts[i]) bi = i;
		int localX = bx - cuts[bi], localY = y % 64;
		int blockW = (bi + 1 < cuts.Length ? cuts[bi + 1] : 256) - cuts[bi];
		float tone = 0.9f + 0.12f * Hash(bi, course, 501) - 0.04f * Hash(bi + 3, course, 502);
		float grain = Fbm(x, y, 256, 8, 4, 503);
		float g = tone * (0.84f + 0.16f * grain);
		if (Hash(x, y, 504) > 0.992f) g -= 0.12f;                        // pits
		// soft edges and a darker band where soot and damp gathered on the lower face
		float edge = Mathf.Min(Mathf.Min(localX, blockW - localX), Mathf.Min(localY, 64 - localY));
		g *= 1f - 0.07f * Mathf.Clamp(1f - edge / 4f, 0f, 1f);
		g *= 1f - 0.05f * Mathf.Clamp((localY - 40f) / 24f, 0f, 1f);
		Color c = new Color(0.86f, 0.82f, 0.74f) * g;
		// the joints: pale mortar, slightly recessed (a shadow line on the upper edge)
		bool joint = localY < 2 || localX < 2;
		if (joint) c = new Color(0.72f, 0.7f, 0.66f) * (0.9f + 0.1f * grain);
		if (localY == 2 || localX == 2) c *= 0.88f;
		return c;
	});

	public static StandardMaterial3D AshlarMat => Tint(Tri("ch_ashlar", Ashlar, 2f, 0.88f, 0.28f), new Color(0.5f, 0.47f, 0.44f));

	/// <summary>Everything in the church is a good deal darker than its texture: old, sooted stone (the owner:
	/// the pale version glared like cloud on a sunny day).</summary>
	private static StandardMaterial3D Tint(StandardMaterial3D m, Color c) { m.AlbedoColor = c; return m; }
	/// <summary>The same ashlar, darker and damp, for the undercroft and the long stair's last stretch.</summary>
	public static StandardMaterial3D AshlarDampMat
	{
		get
		{
			if (_mat.TryGetValue("ch_ashlar_damp", out var m)) return (StandardMaterial3D)m;
			var s = (StandardMaterial3D)AshlarMat.Duplicate();
			s.AlbedoColor = new Color(0.36f, 0.34f, 0.32f);
			s.Roughness = 0.8f;
			_mat["ch_ashlar_damp"] = s;
			return s;
		}
	}

	/// <summary>The vault webs between the ribs: warm pale plaster, faint trowel swirl, hairline cracks and
	/// a few brown water stains.</summary>
	public static Texture2D Plaster => Make("ch_plaster", 256, 256, (x, y) =>
	{
		float n = Fbm(x, y, 256, 4, 4, 511);
		float swirl = Fbm(x + 40 * Mathf.Sin(y * 0.05f), y, 256, 6, 2, 512);
		float g = 0.9f + 0.06f * (n - 0.5f) + 0.04f * (swirl - 0.5f);
		Color c = new Color(0.93f, 0.9f, 0.84f) * g;
		float stain = Fbm(x, y, 256, 3, 3, 513);
		if (stain > 0.66f) c = c.Lerp(new Color(0.66f, 0.58f, 0.46f), Mathf.Clamp((stain - 0.66f) * 3f, 0f, 0.45f));
		float cr = Fbm(x, y, 256, 5, 3, 514);
		if (Mathf.Abs(cr - 0.5f) < 0.006f) c *= 0.8f;
		return c;
	});

	public static StandardMaterial3D PlasterMat => Tint(Tri("ch_plaster", Plaster, 3f, 0.95f, 0.2f), new Color(0.4f, 0.37f, 0.34f));

	/// <summary>Polished cream marble slabs, a metre square (two to the 2 m tile), grey veins wandering
	/// across them, each slab turned so the veins don't line up, fine dark joints.</summary>
	public static Texture2D Marble => Make("ch_marble", 256, 256, (x, y) =>
	{
		int sx = x / 128, sy = y / 128;
		bool turn = ((sx + sy) & 1) == 1;
		float u = turn ? y : x, v = turn ? x : y;
		float warp = Fbm(u, v, 256, 3, 4, 521 + sx * 7 + sy * 13);
		float vein = Mathf.Abs(Mathf.Sin((u * 0.03f + v * 0.012f + warp * 9f)));
		float fine = Fbm(u, v, 256, 12, 3, 522);
		float g = 0.9f + 0.05f * (fine - 0.5f) + 0.04f * Hash(sx, sy, 523);
		Color c = new Color(0.9f, 0.87f, 0.8f) * g;
		c = c.Lerp(new Color(0.48f, 0.47f, 0.47f), Mathf.Clamp(1f - vein / 0.08f, 0f, 1f) * 0.55f);
		c = c.Lerp(new Color(0.62f, 0.6f, 0.58f), Mathf.Clamp(1f - vein / 0.2f, 0f, 1f) * 0.15f);
		if (x % 128 == 0 || y % 128 == 0) c = new Color(0.32f, 0.3f, 0.28f);
		return c;
	});

	public static StandardMaterial3D MarbleMat => Tint(Uv("ch_marble", Marble, 2f, 0.3f, 0.5f), new Color(0.4f, 0.38f, 0.37f));

	/// <summary>Old cobbles (the owner's reference): rounded stones of every size and shade of grey and brown,
	/// worn smooth on top, packed in dark grit, a 2 m tile. Each stone is a cell of a jittered grid, domed
	/// by its distance to the cell's edge.</summary>
	public static Texture2D Cobbles => Make("ch_cobbles", 256, 256, (x, y) =>
	{
		const float cell = 26f;
		float gx = x / cell, gy = y / cell;
		int cx = Mathf.FloorToInt(gx), cy = Mathf.FloorToInt(gy);
		const int period = (int)(256 / cell) + 0;
		float best = 9, second = 9; int bi = 0, bj = 0;
		for (int j = -1; j <= 1; j++)
			for (int i = -1; i <= 1; i++)
			{
				int ii = ((cx + i) % 10 + 10) % 10, jj = ((cy + j) % 10 + 10) % 10;
				float px = cx + i + 0.2f + 0.6f * Hash(ii, jj, 661), py = cy + j + 0.2f + 0.6f * Hash(ii, jj, 662);
				float d = new Vector2((gx - px) * (0.85f + 0.3f * Hash(ii, jj, 663)), gy - py).Length();
				if (d < best) { second = best; best = d; bi = ii; bj = jj; }
				else if (d < second) second = d;
			}
		float edge = second - best;                                   // 0 at the joint, growing into the stone
		float dome = Mathf.Clamp(edge / 0.35f, 0f, 1f);
		float tone = 0.75f + 0.35f * Hash(bi, bj, 664);
		float warm = Hash(bi, bj, 665);
		Color stone = new Color(0.5f + 0.08f * warm, 0.47f + 0.03f * warm, 0.43f - 0.03f * warm) * tone;
		float grain = Fbm(x, y, 256, 16, 3, 666);
		stone *= 0.82f + 0.26f * grain;
		stone *= 0.6f + 0.4f * Mathf.Sqrt(dome);                      // darker down its sides
		if (dome > 0.75f && Hash(x / 2, y / 2, 667) > 0.7f) stone *= 1.08f;   // polished tops catch the light
		Color grit = new Color(0.12f, 0.1f, 0.09f) * (0.8f + 0.4f * Fbm(x, y, 256, 8, 2, 668));
		return edge < 0.06f ? grit : stone.Lerp(grit, Mathf.Clamp(1f - (edge - 0.06f) / 0.05f, 0f, 1f) * 0.6f);
	});

	/// <summary>The church's floor (the owner: more cobblestoned).</summary>
	public static StandardMaterial3D CobbleMat => Tint(Uv("ch_cobbles", Cobbles, 2f, 0.75f, 0.35f), new Color(0.62f, 0.6f, 0.58f));

	/// <summary>Red-brown marble with pale veining, for the nave's aisle runner and the chancel's steps.</summary>
	public static Texture2D RedMarble => Make("ch_redmarble", 256, 256, (x, y) =>
	{
		float warp = Fbm(x, y, 256, 3, 4, 531);
		float vein = Mathf.Abs(Mathf.Sin(x * 0.02f + y * 0.035f + warp * 10f));
		float fine = Fbm(x, y, 256, 10, 3, 532);
		Color c = new Color(0.46f, 0.2f, 0.15f) * (0.85f + 0.2f * fine);
		c = c.Lerp(new Color(0.78f, 0.66f, 0.56f), Mathf.Clamp(1f - vein / 0.06f, 0f, 1f) * 0.6f);
		if (x % 128 == 0 || y % 128 == 0) c = new Color(0.2f, 0.12f, 0.1f);
		return c;
	});

	public static StandardMaterial3D RedMarbleMat => Tint(Uv("ch_redmarble", RedMarble, 2f, 0.3f, 0.5f), new Color(0.62f, 0.5f, 0.48f));

	/// <summary>The medallion set in the aisle before the chancel: an eight-pointed star in a ring of red,
	/// cream and black marble (one texture across the whole disc).</summary>
	public static Texture2D Medallion => Make("ch_medallion", 256, 256, (x, y) =>
	{
		var p = new Vector2(x - 127.5f, y - 127.5f) / 127.5f;
		float r = p.Length(), a = Mathf.Atan2(p.Y, p.X);
		float star = Mathf.Abs(Mathf.Cos(a * 4f));
		float starR = Mathf.Lerp(0.3f, 0.66f, Mathf.Pow(star, 3f));
		float fine = Fbm(x, y, 256, 10, 3, 541);
		Color cream = new Color(0.88f, 0.84f, 0.76f) * (0.92f + 0.1f * fine);
		Color red = new Color(0.5f, 0.18f, 0.12f) * (0.9f + 0.15f * fine);
		Color black = new Color(0.1f, 0.1f, 0.11f) * (0.9f + 0.2f * fine);
		Color gold = new Color(0.72f, 0.56f, 0.26f) * (0.9f + 0.15f * fine);
		if (r > 0.98f) return black;
		if (r > 0.88f) return red;
		if (r > 0.84f) return gold;
		if (r < 0.12f) return gold;
		if (r < starR) return ((int)((a + Mathf.Pi) / (Mathf.Pi / 8f)) % 2 == 0) ? red : black;
		return cream;
	});

	public static StandardMaterial3D MedallionMat
	{
		get
		{
			if (_mat.TryGetValue("ch_medallion", out var m)) return (StandardMaterial3D)m;
			var s = new StandardMaterial3D { AlbedoTexture = Medallion, Roughness = 0.22f, MetallicSpecular = 0.6f, VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic };
			_mat["ch_medallion"] = s;
			return s;
		}
	}

	// ------------------------------------------------------------------ the crypt

	/// <summary>The crypt's vaults: small orange-brown bricks in running bond, dark mortar, some bricks
	/// darker (over-fired), a bloom of damp salt here and there.</summary>
	public static Texture2D CryptBrick => Make("ch_cryptbrick", 256, 256, (x, y) =>
	{
		int row = y / 16;
		int bx = (x + (row % 2) * 16) / 32;
		int lx = (x + (row % 2) * 16) % 32, ly = y % 16;
		float tone = 0.82f + 0.3f * Hash(bx, row, 551);
		if (Hash(bx, row, 552) > 0.9f) tone *= 0.7f;
		float grain = Fbm(x, y, 256, 16, 3, 553);
		Color c = new Color(0.64f, 0.36f, 0.22f) * tone * (0.85f + 0.2f * grain);
		if (lx < 2 || ly < 2) c = new Color(0.22f, 0.19f, 0.16f) * (0.9f + 0.2f * grain);
		float salt = Fbm(x, y, 256, 3, 3, 554);
		if (salt > 0.7f) c = c.Lerp(new Color(0.8f, 0.78f, 0.72f), Mathf.Clamp((salt - 0.7f) * 2.5f, 0f, 0.5f));
		return c;
	});

	public static StandardMaterial3D CryptBrickMat => Tint(Tri("ch_cryptbrick", CryptBrick, 2f, 0.92f, 0.2f), new Color(0.46f, 0.4f, 0.38f));

	/// <summary>The crypt's floor: black and white marble squares, half a metre each, worn dull.</summary>
	public static Texture2D Chequer => Make("ch_chequer", 256, 256, (x, y) =>
	{
		bool white = ((x / 64) + (y / 64)) % 2 == 0;
		float fine = Fbm(x, y, 256, 8, 3, 561);
		float vein = Mathf.Abs(Mathf.Sin(x * 0.04f + y * 0.02f + Fbm(x, y, 256, 3, 3, 562) * 8f));
		Color c = white ? new Color(0.86f, 0.84f, 0.8f) * (0.92f + 0.1f * fine) : new Color(0.09f, 0.09f, 0.1f) * (0.9f + 0.4f * fine);
		c = c.Lerp(white ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.3f, 0.3f, 0.32f), Mathf.Clamp(1f - vein / 0.05f, 0f, 1f) * 0.4f);
		if (x % 64 == 0 || y % 64 == 0) c = new Color(0.4f, 0.38f, 0.35f);
		return c;
	});

	public static StandardMaterial3D ChequerMat => Tint(Uv("ch_chequer", Chequer, 2f, 0.45f, 0.4f), new Color(0.45f, 0.44f, 0.43f));

	// ------------------------------------------------------------------ wood, metal, glass, snow

	/// <summary>Dark stained oak: close grain running along U, open pores, a darker edge to each board.</summary>
	public static Texture2D Oak => Make("ch_oak", 128, 256, (x, y) =>
	{
		float warp = Fbm(x * 0.5f, y, 128, 4, 3, 571);
		float lines = Mathf.Sin((x + warp * 18f) * 0.9f) * 0.5f + 0.5f;
		float fine = Fbm(x, y * 0.2f, 128, 16, 2, 572);
		float g = 0.75f + 0.18f * lines + 0.12f * (fine - 0.5f);
		if (Hash(x, y / 3, 573) > 0.985f) g *= 0.7f;
		Color c = new Color(0.34f, 0.2f, 0.11f) * g;
		if (x % 64 < 1) c *= 0.55f;
		return c;
	});

	public static StandardMaterial3D OakMat => Uv("ch_oak", Oak, 1f, 0.55f, 0.35f);

	/// <summary>Old grey-brown planks, split and weathered, dark gaps between boards, the nail heads rusted.</summary>
	public static Texture2D OldPlank => Make("ch_oldplank", 128, 256, (x, y) =>
	{
		int board = x / 32;
		int lx = x % 32;
		float warp = Fbm(x, y * 0.3f, 128, 3, 3, 581 + board);
		float grain = Mathf.Sin((lx + warp * 12f) * 1.3f) * 0.5f + 0.5f;
		float tone = 0.8f + 0.25f * Hash(board, 0, 582);
		float g = tone * (0.72f + 0.2f * grain + 0.1f * Fbm(x, y, 128, 12, 2, 583));
		Color c = new Color(0.44f, 0.36f, 0.28f) * g;
		c = c.Lerp(new Color(0.46f, 0.46f, 0.44f), 0.35f * Fbm(x, y, 128, 3, 3, 584));    // silvered with age
		if (lx < 2) c = new Color(0.08f, 0.07f, 0.06f);
		if ((lx == 16 || lx == 17) && (y % 128 == 12 || y % 128 == 13)) c = new Color(0.3f, 0.16f, 0.08f);   // nails
		if (Mathf.Abs(Fbm(x, y, 128, 6, 2, 585) - 0.5f) < 0.01f && lx > 3) c *= 0.55f;                           // splits
		return c;
	});

	public static StandardMaterial3D OldPlankMat => Uv("ch_oldplank", OldPlank, 2f, 0.9f, 0.2f);
	public static StandardMaterial3D OldPlankTriMat => Tri("ch_oldplank_tri", OldPlank, 2f, 0.9f, 0.2f);

	/// <summary>Gilding: warm gold leaf, rubbed through to red bole on the high points, dark in the crevices.</summary>
	public static Texture2D Gold => Make("ch_gold", 128, 128, (x, y) =>
	{
		float n = Fbm(x, y, 128, 6, 4, 591);
		Color c = new Color(0.82f, 0.62f, 0.28f) * (0.8f + 0.35f * n);
		if (Fbm(x, y, 128, 10, 2, 592) > 0.7f) c = c.Lerp(new Color(0.45f, 0.18f, 0.1f), 0.5f);
		return c;
	});

	public static StandardMaterial3D GoldMat => Tri("ch_gold", Gold, 1f, 0.35f, 0.8f, 0.85f);

	/// <summary>Black iron: forged, hammer-marked, a little rust.</summary>
	public static Texture2D Iron => Make("ch_iron", 128, 128, (x, y) =>
	{
		float n = Fbm(x, y, 128, 8, 4, 601);
		Color c = new Color(0.12f, 0.12f, 0.13f) * (0.8f + 0.5f * n);
		if (Fbm(x, y, 128, 4, 3, 602) > 0.68f) c = c.Lerp(new Color(0.35f, 0.17f, 0.08f), 0.5f);
		return c;
	});

	public static StandardMaterial3D IronMat => Tri("ch_iron", Iron, 0.5f, 0.6f, 0.5f, 0.6f);

	/// <summary>Snow: blue-white, soft drifts of shade, a glitter of crystals.</summary>
	public static Texture2D Snow => Make("ch_snow", 256, 256, (x, y) =>
	{
		float n = Fbm(x, y, 256, 4, 4, 611);
		float g = 0.86f + 0.1f * n;
		Color c = new Color(0.9f, 0.93f, 0.98f) * g;
		if (Hash(x, y, 612) > 0.985f) c = new Color(1f, 1f, 1f);
		return c;
	});

	public static StandardMaterial3D SnowMat
	{
		get
		{
			if (_mat.TryGetValue("ch_snow", out var m)) return (StandardMaterial3D)m;
			var s = Tri("ch_snow_base", Snow, 4f, 0.7f, 0.5f);
			_mat["ch_snow"] = s;
			return s;
		}
	}

	/// <summary>The red circle round the high altar: rings, a five-pointed star, a band of marks like
	/// writing; red where it glows, transparent between.</summary>
	public static Texture2D Sigil => Make("ch_sigil", 512, 512, (x, y) =>
	{
		var p = new Vector2(x - 255.5f, y - 255.5f) / 255.5f;
		float r = p.Length(), a = Mathf.Atan2(p.Y, p.X);
		float line = 0f;
		// rings
		foreach (var (rr, w) in new[] { (0.97f, 0.012f), (0.9f, 0.008f), (0.74f, 0.01f), (0.7f, 0.005f), (0.3f, 0.008f) })
			line = Mathf.Max(line, Mathf.Clamp(1f - Mathf.Abs(r - rr) / w, 0f, 1f));
		// the star: five lines joining every second point on the 0.7 ring
		for (int i = 0; i < 5; i++)
		{
			float a0 = Mathf.Tau * i / 5f - Mathf.Pi * 0.5f, a1 = Mathf.Tau * (i + 2) / 5f - Mathf.Pi * 0.5f;
			Vector2 q0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.7f, q1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.7f;
			var d = q1 - q0;
			float t = Mathf.Clamp((p - q0).Dot(d) / d.LengthSquared(), 0f, 1f);
			float dist = (p - (q0 + d * t)).Length();
			line = Mathf.Max(line, Mathf.Clamp(1f - dist / 0.009f, 0f, 1f));
		}
		// the writing: short strokes in the band between the outer rings
		if (r > 0.76f && r < 0.88f)
		{
			float seg = (a + Mathf.Pi) / Mathf.Tau * 72f;
			int gi = (int)seg;
			float fu = seg - gi, fv = (r - 0.76f) / 0.12f;
			float h1 = Hash(gi, 1, 651), h2 = Hash(gi, 2, 652), h3 = Hash(gi, 3, 653);
			if (Mathf.Abs(fu - (0.3f + 0.4f * h1)) < 0.08f && fv > 0.15f && fv < 0.85f) line = 1f;
			if (Mathf.Abs(fv - (0.2f + 0.6f * h2)) < 0.07f && fu > 0.2f && fu < 0.8f) line = 1f;
			if (h3 > 0.5f && Mathf.Abs((fu - 0.5f) - (fv - 0.5f)) < 0.07f && fu > 0.25f && fu < 0.75f) line = 1f;
		}
		// a faint wash of red inside the circle, and small breaks in the lines (old, worn)
		float wash = r < 0.97f ? 0.08f : 0f;
		if (Hash(x / 4, y / 4, 654) > 0.93f) line *= 0.3f;
		float alpha = Mathf.Clamp(line + wash, 0f, 1f);
		return new Color(1f, 0.08f + 0.1f * line, 0.05f, alpha);
	}, false);

	// ------------------------------------------------------------------ stained glass

	/// <summary>A tall lancet's glass: small pieces of deep blue, ruby, gold and green in black lead, round a
	/// standing figure (a pale robe, a gold halo, a darker face) with a border of small squares. <paramref name="seed"/>
	/// changes the palette's balance and the figure.</summary>
	public static Texture2D Lancet(int seed) => Make($"ch_lancet{seed}", 96, 288, (x, y) =>
	{
		// cells: a jittered grid of small pieces
		float cw = 12f;
		float gx = x / cw, gy = y / cw;
		int cx = Mathf.FloorToInt(gx), cy = Mathf.FloorToInt(gy);
		float best = 9, second = 9; int bi = 0, bj = 0;
		for (int j = -1; j <= 1; j++)
			for (int i = -1; i <= 1; i++)
			{
				float px = cx + i + Hash(cx + i, cy + j, 620 + seed), py = cy + j + Hash(cx + i, cy + j, 621 + seed);
				float d = new Vector2(gx - px, gy - py).Length();
				if (d < best) { second = best; best = d; bi = cx + i; bj = cy + j; }
				else if (d < second) second = d;
			}
		float lead = second - best;
		Color[] pal = { new(0.08f, 0.16f, 0.62f), new(0.06f, 0.1f, 0.45f), new(0.62f, 0.06f, 0.08f), new(0.8f, 0.58f, 0.12f), new(0.1f, 0.42f, 0.2f), new(0.35f, 0.1f, 0.45f) };
		float h = Hash(bi, bj, 622 + seed);
		Color c = pal[(int)(h * pal.Length) % pal.Length];
		// the border
		bool border = x < 10 || x > 85 || y < 10;
		if (border) c = ((x / 5 + y / 5) % 2 == 0) ? new Color(0.72f, 0.54f, 0.14f) : new Color(0.55f, 0.06f, 0.07f);
		// the figure: a robe (a tapering column), a head and a halo
		float fx = (x - 48f) / 48f, fy = y / 288f;
		float robeW = Mathf.Lerp(0.18f, 0.5f, Mathf.Clamp((fy - 0.3f) / 0.6f, 0f, 1f));
		if (fy > 0.3f && fy < 0.92f && Mathf.Abs(fx) < robeW) c = (seed % 2 == 0 ? new Color(0.78f, 0.74f, 0.62f) : new Color(0.62f, 0.08f, 0.1f)) * (0.85f + 0.2f * h);
		var head = new Vector2(fx, (fy - 0.24f) * 3f);
		if (head.Length() < 0.36f) c = new Color(0.8f, 0.58f, 0.12f);          // the halo
		if (head.Length() < 0.2f) c = new Color(0.62f, 0.5f, 0.42f);           // the face
		// the lead: black cames between the pieces and round the figure
		if (lead < 0.08f && !border) c = new Color(0.02f, 0.02f, 0.02f);
		if (x % 48 == 0 || y % 72 == 0) c = new Color(0.02f, 0.02f, 0.02f);   // the iron saddle bars
		return c;
	}, false);

	/// <summary>The rose window: twelve petals round a gold centre, a ring of small roundels, the lead
	/// tracery in black; outside the circle, black (the stone round it).</summary>
	public static Texture2D Rose => Make("ch_rose", 256, 256, (x, y) =>
	{
		var p = new Vector2(x - 127.5f, y - 127.5f) / 127.5f;
		float r = p.Length(), a = Mathf.Atan2(p.Y, p.X) + Mathf.Pi;
		if (r > 0.98f) return new Color(0, 0, 0);
		float petal = a / (Mathf.Tau / 12f);
		int pi = (int)petal;
		float pf = petal - pi;
		Color c;
		if (r < 0.16f) c = new Color(0.85f, 0.62f, 0.12f);
		else if (r < 0.62f) c = (pi % 2 == 0) ? new Color(0.1f, 0.18f, 0.66f) : new Color(0.64f, 0.08f, 0.1f);
		else if (r < 0.8f) c = (pi % 3 == 0) ? new Color(0.8f, 0.58f, 0.14f) : new Color(0.1f, 0.4f, 0.22f);
		else c = new Color(0.08f, 0.14f, 0.5f);
		float lum = 0.85f + 0.25f * Hash(pi, (int)(r * 12), 631);
		c *= lum;
		// tracery: the petal edges, the rings, spokes
		if (Mathf.Abs(pf - 0.5f) > 0.46f || Mathf.Abs(r - 0.16f) < 0.02f || Mathf.Abs(r - 0.62f) < 0.018f || Mathf.Abs(r - 0.8f) < 0.018f || Mathf.Abs(r - 0.96f) < 0.02f)
			c = new Color(0.02f, 0.02f, 0.02f);
		// small pieces inside each petal
		if (Mathf.Abs(Mathf.Sin(r * 40f)) < 0.08f && r > 0.2f) c *= 0.3f;
		return c;
	}, false);

	/// <summary>Stained glass: lit from outside by the cold daylight, so it glows (and a little light leaks
	/// through dark lead as black).</summary>
	public static StandardMaterial3D GlassMat(Texture2D tex, float energy)
	{
		string key = $"ch_glass_{tex.GetRid()}_{energy}";
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex, EmissionEnabled = true, EmissionTexture = tex, EmissionEnergyMultiplier = energy,
			Roughness = 0.2f, MetallicSpecular = 0.7f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		_mat[key] = s;
		return s;
	}

	/// <summary>Plain leaded glass (the aisles' lower lights): clear diamond quarries, frosted at the edges,
	/// with the snow-light behind them. Transparent, so the winter shows through.</summary>
	public static Texture2D Quarries => Make("ch_quarries", 128, 128, (x, y) =>
	{
		float u = (x + y) % 32, v = (x - y + 256) % 32;
		bool lead = u < 1.5f || v < 1.5f;
		if (lead) return new Color(0.04f, 0.04f, 0.04f, 1f);
		float frost = Mathf.Clamp(1f - Mathf.Min(Mathf.Min(u, 32 - u), Mathf.Min(v, 32 - v)) / 6f, 0f, 1f);
		return new Color(0.82f, 0.88f, 0.92f, 0.18f + 0.45f * frost + 0.1f * Fbm(x, y, 128, 6, 2, 641));
	}, false);

	public static StandardMaterial3D QuarryMat
	{
		get
		{
			if (_mat.TryGetValue("ch_quarries", out var m)) return (StandardMaterial3D)m;
			var s = new StandardMaterial3D
			{
				AlbedoTexture = Quarries, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.15f, MetallicSpecular = 0.8f,
				// the frosted panes glow a little with the snow-light behind them
				EmissionEnabled = true, Emission = new Color(0.5f, 0.56f, 0.66f), EmissionEnergyMultiplier = 0.35f,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, Uv1Scale = new Vector3(2f, 2f, 1f),
			};
			_mat["ch_quarries"] = s;
			return s;
		}
	}
}
