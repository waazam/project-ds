using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Low-res procedural textures and materials for the park buildings (cabin,
/// shed, tent): round logs with chinking, cedar shingles, mortared fieldstone,
/// vertical boards, floor planks, canvas. Same rules as ProcTextures and
/// PropTextures: 32-128 px, fixed seeds, linear filtering with mipmaps,
/// vertex colour multiplies albedo, cached for the process.
/// Also owns the char overlay (<see cref="NewCharOverlay"/>) used for the fire states.
/// </summary>
public static class BuildingTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();
	private static readonly Dictionary<string, Material> _mat = new();

	// ---------- noise (same hash family as the other texture kits, own seeds) ----------

	private static float Hash(int x, int y, int seed)
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

	/// <summary>Tileable fbm; cx, cy = cells across each axis at the first octave.</summary>
	private static float Fbm(float x, float y, int w, int h, int cx, int cy, int oct, int seed)
	{
		float sum = 0, amp = 0.5f, norm = 0;
		for (int o = 0; o < oct; o++)
		{
			sum += amp * Noise(x / w * cx, y / h * cy, cx, cy, seed + o * 29);
			norm += amp; amp *= 0.5f; cx *= 2; cy *= 2;
		}
		return sum / norm;
	}

	private static Color Mix(Color a, Color b, float t) => a.Lerp(b, Mathf.Clamp(t, 0, 1));

	private static Texture2D Make(string key, int w, int h, Func<int, int, Color> f)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = f(x, y); c.A = 1f;
				img.SetPixel(x, y, c);
			}
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
		_tex[key] = t;
		return t;
	}

	// ---------- textures ----------

	/// <summary>
	/// One round log, lying along U; V 0..1 spans exactly one log course (top to bottom).
	/// Rounded shading across the log, pale chinking at the top and bottom edges,
	/// streaky grain, dark checks and a greyed, weathered surface.
	/// </summary>
	public static Texture2D Log() => Make("b_log", 128, 32, (x, y) =>
	{
		float v = (y + 0.5f) / 32f;
		float grain = Fbm(x, y, 128, 32, 3, 12, 3, 501);
		float blotch = Fbm(x, y, 128, 32, 6, 2, 2, 507);
		var c = Mix(new Color(0.2f, 0.14f, 0.095f), new Color(0.5f, 0.36f, 0.23f), grain * 0.85f + (blotch - 0.5f) * 0.5f);
		// checks: long thin cracks along the grain
		if (Noise(x / 128f * 4f, y * 0.8f, 4, 32, 511) > 0.88f) c *= 0.5f;
		// weathering towards grey on the upper half (rain side)
		float lum = (c.R + c.G + c.B) / 3f;
		c = Mix(c, new Color(lum * 1.05f, lum, lum * 0.95f), 0.2f + (1f - v) * 0.2f);
		// roundness: lit top shoulder, dark underside
		float round = Mathf.Sin(Mathf.Pi * Mathf.Clamp((v - 0.06f) / 0.88f, 0, 1));
		c *= 0.45f + 0.62f * round - 0.12f * v;
		// chinking between courses: pale lime mortar, a bit dirty
		if (v < 0.06f || v > 0.94f)
		{
			float n = Hash(x, y, 519);
			c = Mix(new Color(0.34f, 0.32f, 0.28f), new Color(0.44f, 0.42f, 0.37f), n) * (v < 0.03f || v > 0.97f ? 0.7f : 1f);
		}
		return c;
	});

	/// <summary>Log end grain: rings, a radial check, dark bark rim. Mapped 0..1 across the end face.</summary>
	public static Texture2D LogEnd() => Make("b_logend", 32, 32, (x, y) =>
	{
		float dx = (x + 0.5f) / 32f - 0.5f, dy = (y + 0.5f) / 32f - 0.5f;
		float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
		float rings = 0.5f + 0.5f * Mathf.Sin(r * 34f + Noise(x * 0.3f, y * 0.3f, 10, 10, 531) * 3f);
		var c = Mix(new Color(0.30f, 0.24f, 0.17f), new Color(0.46f, 0.38f, 0.28f), rings * 0.6f + Hash(x, y, 533) * 0.15f);
		float ang = Mathf.Atan2(dy, dx);
		if (Mathf.Abs(ang - 0.7f) < 0.06f && r < 0.85f) c *= 0.4f;
		if (r > 0.82f) c = new Color(0.12f, 0.09f, 0.07f);
		c = Mix(c, new Color(0.22f, 0.22f, 0.21f), 0.3f);
		return c;
	});

	/// <summary>Cedar shingles, rows along U, V down the roof slope; ~4 courses per tile. Dark, mossy in places.</summary>
	public static Texture2D Shingles() => Make("b_shingle", 64, 64, (x, y) =>
	{
		int row = y / 16;
		float vy = (y % 16) / 16f;
		int off = (row % 2) * 7 + (row / 2 % 2) * 3;
		int col = (x + off) / 11;
		float cellH = Hash(col, row, 541);
		float grain = Fbm(x, y, 64, 64, 2, 16, 2, 543);
		var c = Mix(new Color(0.13f, 0.12f, 0.1f), new Color(0.31f, 0.28f, 0.24f), grain * 0.7f + cellH * 0.45f);
		// butt edge (bottom of each shingle) casts a dark line; top of the course is lighter
		c *= 0.62f + 0.5f * vy;
		if (vy > 0.9f) c *= 0.4f;
		// keyways between shingles
		if (((x + off) % 11) == 0) c *= 0.35f;
		float moss = Mathf.SmoothStep(0.55f, 0.72f, Fbm(x, y, 64, 64, 3, 3, 3, 547));
		c = Mix(c, new Color(0.11f, 0.14f, 0.07f), moss * 0.7f);
		return c;
	});

	/// <summary>Mortared fieldstone: irregular rounded stones (cellular), sunk pale mortar between.</summary>
	public static Texture2D Stone() => Make("b_stone", 64, 64, (x, y) =>
	{
		const int cells = 5;
		float fx = x / 64f * cells, fy = y / 64f * cells * 1.25f;
		int cy0 = Mathf.FloorToInt(fy);
		float d1 = 9, d2 = 9; int id = 0;
		for (int j = -1; j <= 1; j++)
			for (int i = -1; i <= 1; i++)
			{
				int cx = Mathf.FloorToInt(fx) + i, cyy = cy0 + j;
				int wx = ((cx % cells) + cells) % cells, wy = ((cyy % 6) + 6) % 6;
				float px = cx + 0.2f + 0.6f * Hash(wx, wy, 551), py = cyy + 0.2f + 0.6f * Hash(wx, wy, 552);
				float ddx = (fx - px) * 1.0f, ddy = (fy - py) * 1.35f;
				float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
				if (d < d1) { d2 = d1; d1 = d; id = wx * 17 + wy; }
				else if (d < d2) d2 = d;
			}
		float edge = d2 - d1;
		float tone = Hash(id, 3, 557);
		float n = Fbm(x, y, 64, 64, 8, 8, 2, 559);
		var stone = Mix(new Color(0.20f, 0.19f, 0.18f), new Color(0.44f, 0.42f, 0.39f), tone * 0.7f + n * 0.4f);
		stone *= 0.75f + 0.35f * Mathf.Clamp(edge * 3f, 0, 1);   // domed stones, darker near their edges
		var mortar = Mix(new Color(0.30f, 0.29f, 0.26f), new Color(0.40f, 0.39f, 0.35f), Hash(x, y, 561));
		float m = Mathf.SmoothStep(0.05f, 0.1f, edge);
		var c = Mix(mortar * 0.8f, stone, m);
		float moss = Mathf.SmoothStep(0.58f, 0.72f, Fbm(x, y, 64, 64, 3, 3, 3, 563));
		return Mix(c, new Color(0.10f, 0.13f, 0.06f), moss * 0.6f);
	});

	/// <summary>Vertical boards (gables, door, shed walls): board seams every 16 px along U, grain along V.</summary>
	public static Texture2D Boards() => Make("b_boards", 64, 64, (x, y) =>
	{
		int board = x / 16;
		float bx = (x % 16) / 16f;
		float grain = Fbm(x, y, 64, 64, 16, 2, 3, 571 + board);
		float tone = Hash(board, 0, 573);
		var c = Mix(new Color(0.14f, 0.11f, 0.08f), new Color(0.34f, 0.28f, 0.21f), grain * 0.8f + tone * 0.35f);
		float lum = (c.R + c.G + c.B) / 3f;
		c = Mix(c, new Color(lum, lum, lum), 0.3f);
		if (bx < 0.07f) c *= 0.3f;
		else if (bx < 0.14f || bx > 0.93f) c *= 0.75f;
		if (Noise(x * 0.8f, y / 64f * 5f, 64, 5, 577) > 0.9f) c *= 0.55f;
		return c;
	});

	/// <summary>Floor planks: seams every 16 px along V, grain along U, staggered butt joints, worn paler centre.</summary>
	public static Texture2D Floor() => Make("b_floor", 64, 64, (x, y) =>
	{
		int row = y / 16;
		float vy = (y % 16) / 16f;
		float grain = Fbm(x, y, 64, 64, 2, 16, 3, 581 + row);
		float tone = Hash(row, 1, 583);
		var c = Mix(new Color(0.13f, 0.09f, 0.06f), new Color(0.36f, 0.26f, 0.17f), grain * 0.75f + tone * 0.35f);
		if (vy < 0.08f) c *= 0.3f;
		int joint = (row * 23 + 9) % 64;
		if (Mathf.Abs(x - joint) < 1) c *= 0.35f;
		float scuff = Fbm(x, y, 64, 64, 2, 2, 2, 587);
		c = Mix(c, new Color(0.32f, 0.27f, 0.21f), scuff * 0.25f);
		return c;
	});

	/// <summary>Fresh-cut pine plank (the friend boarded the door recently): pale, yellowish, a little dirty. Grain along U.</summary>
	public static Texture2D FreshPlank() => Make("b_fresh", 64, 16, (x, y) =>
	{
		float grain = Fbm(x, y, 64, 16, 2, 8, 3, 611);
		var c = Mix(new Color(0.46f, 0.37f, 0.24f), new Color(0.66f, 0.55f, 0.38f), grain);
		if (Noise(x / 64f * 6f, y, 6, 16, 613) > 0.9f) c *= 0.7f;
		float dirt = Fbm(x, y, 64, 16, 3, 2, 2, 617);
		c = Mix(c, new Color(0.3f, 0.26f, 0.2f), Mathf.SmoothStep(0.55f, 0.8f, dirt) * 0.6f);
		if (y == 0 || y == 15) c *= 0.7f;
		return c;
	});

	/// <summary>Olive-grey canvas: a faint weave, water stains and mildew blotches.</summary>
	public static Texture2D Canvas() => Make("b_canvas", 32, 32, (x, y) =>
	{
		float weave = ((x + y) % 2 == 0 ? 0.02f : -0.02f) + Hash(x, y, 591) * 0.03f;
		float stain = Fbm(x, y, 32, 32, 3, 3, 3, 593);
		var c = Mix(new Color(0.2f, 0.21f, 0.165f), new Color(0.27f, 0.28f, 0.22f), stain);
		c += new Color(weave, weave, weave);
		float mildew = Mathf.SmoothStep(0.6f, 0.8f, Fbm(x, y, 32, 32, 8, 8, 2, 597));
		return Mix(c, new Color(0.15f, 0.16f, 0.12f), mildew * 0.4f);
	});

	/// <summary>Soft round puff with noisy edge, white; alpha from luminance in the smoke material.</summary>
	public static Texture2D Puff() => Make("b_puff", 32, 32, (x, y) =>
	{
		float dx = (x + 0.5f) / 32f - 0.5f, dy = (y + 0.5f) / 32f - 0.5f;
		float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
		float n = Fbm(x, y, 32, 32, 4, 4, 3, 601);
		float a = Mathf.Clamp(1f - r - (n - 0.5f) * 0.6f, 0, 1);
		a = Mathf.SmoothStep(0f, 0.7f, a);
		return new Color(a, a, a);
	});

	// ---------- materials ----------

	private static StandardMaterial3D Std(string key, Texture2D tex, float rough = 0.95f, float spec = 0.25f, bool cullOff = false)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = tex,
			Roughness = rough,
			MetallicSpecular = spec,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			VertexColorUseAsAlbedo = true,
		};
		if (cullOff) s.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_mat[key] = s;
		return s;
	}

	public static StandardMaterial3D LogMat => Std("b_log", Log());
	public static StandardMaterial3D LogEndMat => Std("b_logend", LogEnd());
	public static StandardMaterial3D ShingleMat => Std("b_shingle", Shingles(), 0.85f, 0.3f);
	public static StandardMaterial3D StoneMat => Std("b_stone", Stone(), 0.95f, 0.2f);
	public static StandardMaterial3D BoardsMat => Std("b_boards", Boards());
	public static StandardMaterial3D FloorMat => Std("b_floor", Floor(), 0.8f, 0.3f);
	public static StandardMaterial3D FreshPlankMat => Std("b_fresh", FreshPlank(), 0.9f, 0.25f);
	public static StandardMaterial3D CanvasMat => Std("b_canvas", Canvas(), 1f, 0.15f, cullOff: true);

	/// <summary>Cast iron: near-black, a little sheen.</summary>
	public static StandardMaterial3D IronMat => Std("b_iron", ProcTextures.Metal(), 0.6f, 0.45f);

	/// <summary>Old window glass: very dark, glossy, so it only reads when it catches light.</summary>
	public static StandardMaterial3D GlassMat
	{
		get
		{
			if (_mat.TryGetValue("b_glass", out var m)) return (StandardMaterial3D)m;
			var s = new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.035f, 0.04f), Roughness = 0.15f, MetallicSpecular = 0.7f };
			_mat["b_glass"] = s;
			return s;
		}
	}

	/// <summary>Warm lamplight seen through a window (unshaded, faint so it stays a glow, not a lamp).</summary>
	public static StandardMaterial3D LitGlassMat
	{
		get
		{
			if (_mat.TryGetValue("b_litglass", out var m)) return (StandardMaterial3D)m;
			var s = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.55f, 0.33f, 0.12f),
				EmissionEnabled = true,
				Emission = new Color(1f, 0.55f, 0.2f),
				EmissionEnergyMultiplier = 0.55f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			};
			_mat["b_litglass"] = s;
			return s;
		}
	}

	/// <summary>Plain tinted cloth/rope etc. (vertex colour tints).</summary>
	public static StandardMaterial3D Plain(string key, Color c, float rough = 1f)
	{
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D { AlbedoColor = c, Roughness = rough, MetallicSpecular = 0.15f, VertexColorUseAsAlbedo = true };
		_mat[key] = s;
		return s;
	}

	// ---------- the friend's prints (Cabin.BuildPapers): 48 x 32, a 3:2 photo with a paper border ----------

	/// <summary>
	/// 0..2: a red, blue and purple bird on a branch, composed like the viewfinder (subject in the
	/// centre, dim edges); 3: the first staircase at night from its foot, the top step lit from above.
	/// Low-res and a little faded, like a drugstore print. No text anywhere on them.
	/// </summary>
	public static Texture2D Print(int kind) => Make($"b_print{kind}", 48, 32, (x, y) => PrintPixel(kind, x, y));

	public static StandardMaterial3D PrintMat(int kind)
	{
		string key = $"b_print{kind}";
		if (_mat.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = new StandardMaterial3D
		{
			AlbedoTexture = Print(kind),
			Roughness = 0.55f,
			MetallicSpecular = 0.4f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		_mat[key] = s;
		return s;
	}

	private static Color PrintPixel(int kind, int x, int y)
	{
		const int w = 48, h = 32, border = 2;
		const int iw = w - 2 * border, ih = h - 2 * border;   // 44 x 28
		int px = x - border, py = y - border;
		if (px < 0 || py < 0 || px >= iw || py >= ih)
		{
			float g = 0.86f + (Hash(x, y, 701) - 0.5f) * 0.05f;
			return new Color(g, g * 0.97f, g * 0.9f);
		}
		float u = (px + 0.5f) / iw, v = (py + 0.5f) / ih;
		Color c = kind == 3 ? StairsPrint(px, py, u, v) : BirdPrint(kind, px, py, u, v);
		// the viewfinder's dim edges, and the flat warm fade of an old print
		float r = new Vector2((u - 0.5f) * 1.5f, v - 0.5f).Length() / 0.9f;
		c *= 1f - 0.35f * Mathf.SmoothStep(0.62f, 1.02f, r);
		return Mix(c, new Color(0.62f, 0.56f, 0.46f), 0.15f);
	}

	private static Color BirdPrint(int kind, int px, int py, float u, float v)
	{
		// blurred daylit woods behind, paler toward a sky gap top left
		float n = Fbm(px, py, 44, 28, 3, 2, 3, 711 + kind * 7);
		var c = Mix(new Color(0.16f, 0.22f, 0.10f), new Color(0.46f, 0.54f, 0.30f), n);
		float sky = Mathf.SmoothStep(0.5f, 0.85f, Fbm(px, py, 44, 28, 2, 2, 2, 719 + kind)) * (1f - v) * (1f - u * 0.6f);
		c = Mix(c, new Color(0.78f, 0.82f, 0.74f), sky * 0.85f);
		// the branch it sits on, drooping a little to the right
		float yb = 0.70f + 0.10f * (u - 0.5f);
		if (Mathf.Abs(v - yb) < 0.04f + 0.012f * (1f - u))
			c = Mix(new Color(0.11f, 0.08f, 0.05f), new Color(0.2f, 0.15f, 0.1f), Hash(px, py, 723));
		// the bird, filling the middle of the frame, in pixel-space ellipses: tail, body, folded wing, head, crest, beak, eye, feet
		Color body = kind switch
		{
			0 => new Color(0.82f, 0.12f, 0.09f),
			1 => new Color(0.15f, 0.34f, 0.86f),
			_ => new Color(0.52f, 0.2f, 0.62f),
		};
		Color dark = body * 0.55f;
		bool In(float cx, float cy, float rx, float ry) { float dx = (px + 0.5f - cx) / rx, dy = (py + 0.5f - cy) / ry; return dx * dx + dy * dy <= 1f; }
		if (In(14f, 16.2f, 4.2f, 1.6f)) c = dark;
		if (In(22f, 15f, 7f, 4.2f)) c = body * (1.05f - 0.35f * Mathf.Clamp((py + 0.5f - 12f) / 7f, 0f, 1f));
		if (In(20.5f, 14.5f, 4.2f, 2f)) c = dark;
		if (In(27.5f, 9.6f, 3.2f, 2.9f)) c = body * 1.02f;
		if (kind == 0 && In(28.3f, 6.8f, 1.3f, 1.6f)) c = body * 0.9f;
		if (px >= 30 && px <= 31 && (py == 9 || py == 10)) c = kind == 0 ? new Color(0.85f, 0.5f, 0.2f) : new Color(0.35f, 0.33f, 0.3f);
		if (px == 28 && py == 9) c = new Color(0.03f, 0.02f, 0.02f);
		if ((px == 19 || px == 23) && py == 19) c = new Color(0.1f, 0.08f, 0.05f);
		return c;
	}

	private static Color StairsPrint(int px, int py, float u, float v)
	{
		// night: near-black blue with faint trunks
		float trunk = Noise(px * 0.55f, py * 0.08f, 24, 3, 731);
		var c = Mix(new Color(0.02f, 0.03f, 0.05f), new Color(0.09f, 0.1f, 0.14f), Mathf.SmoothStep(0.6f, 0.9f, trunk));
		// the flight as a trapezoid from a wide foot (bottom) up to the narrow top landing
		const float top = 0.16f;
		float t = Mathf.Clamp((v - top) / (1f - top), 0f, 1f);   // 0 at the top step, 1 at the foot
		float half = Mathf.Lerp(0.12f, 0.42f, t);
		float du = Mathf.Abs(u - 0.5f);
		float lightFall = Mathf.Exp(-t * 3.2f);                    // the light from above reaches a few steps down
		if (v >= top && du < half)
		{
			// pale concrete treads, dark risers; the courses tighten toward the top
			bool riser = Mathf.PosMod(Mathf.Sqrt(t) * 10f, 1f) < 0.25f;
			c = new Color(0.72f, 0.72f, 0.7f) * (0.12f + 0.88f * lightFall);
			if (riser) c *= 0.45f;
			if (t < 0.05f) c = new Color(0.92f, 0.94f, 1.0f);     // the top step, lit white
		}
		else if (v >= top && du < half + 0.07f)
		{
			// the stone cheek walls, catching a little of it
			var stone = Mix(new Color(0.18f, 0.16f, 0.13f), new Color(0.34f, 0.3f, 0.25f), Hash(px, py, 737));
			c = stone * (0.3f + 0.7f * lightFall);
		}
		// the beam: a pale wedge widening down from the top edge onto the landing, and a haze around it
		if (v < top + 0.02f)
		{
			float bh = Mathf.Lerp(0.03f, 0.14f, v / top);
			float inBeam = 1f - Mathf.SmoothStep(bh * 0.6f, bh, du);
			c = Mix(c, new Color(0.6f, 0.65f, 0.78f), inBeam * 0.65f);
		}
		float haze = Mathf.Exp(-((u - 0.5f) * (u - 0.5f) * 40f + (v - top) * (v - top) * 60f));
		return Mix(c, new Color(0.45f, 0.5f, 0.6f), haze * 0.5f);
	}

	/// <summary>
	/// A fresh (not shared) char overlay: blackens the surface it is laid over as
	/// "amount" rises (0 = untouched, 1 = fully charred), with glowing ember cracks
	/// scaled by "ember". Set as GeometryInstance3D.MaterialOverlay.
	/// </summary>
	public static ShaderMaterial NewCharOverlay()
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/char_overlay.gdshader") };
		m.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		m.SetShaderParameter("amount", 0f);
		m.SetShaderParameter("ember", 0f);
		return m;
	}
}
