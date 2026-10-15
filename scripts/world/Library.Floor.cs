using System.Collections.Generic;
using Godot;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// The library's floor and what stands on it (2026-10-14h, the interiors pass: "a flat two-tone rug; the middle of the
/// floor empty, one table, one globe"). The rug is an old Persian-pattern carpet gone dull: an indigo border of lozenges,
/// a madder field with a medallion, worn to the warp down the line people walked from the door, stained, rucked up in a
/// fold and curling at one corner, its fringe matted. Round it: a second long table with a chair pushed back and one
/// tipped over, books and papers left on it; a card catalogue, one drawer out on the floor and its cards spilled; a
/// book cart; the piles of books at the shelves' feet (<see cref="FloorBooks"/>). Everything kept out of the ways to
/// the hinged bookcase, the journal's shelf and the sheeted table. The parquet is drawn finely and baked
/// (<see cref="RoomShade"/>): dark at the shelves' feet and under the furniture, the light kept in pools round the lamps
/// and the fire, worn where feet went (<see cref="RoomSurface"/>).
/// </summary>
public partial class Library
{
	private const float RugW = 5.2f, RugL = 7f;
	private static readonly Vector3 RugAt = new(0.6f, 0f, 6.5f);
	private static Texture2D _rugTex, _fringeTex;
	private readonly List<Aabb> _floorUnder = new();

	// ------------------------------------------------------------------ the rug

	/// <summary>The rug's pattern, drawn once: <paramref name="x"/>, <paramref name="z"/> in metres across and along it.</summary>
	private static Color RugPixel(float x, float z, int px, int py)
	{
		Color madder = new(0.36f, 0.12f, 0.09f), indigo = new(0.1f, 0.12f, 0.19f), ivory = new(0.55f, 0.48f, 0.36f),
			ochre = new(0.48f, 0.34f, 0.15f), brown = new(0.15f, 0.1f, 0.07f), rust = new(0.31f, 0.13f, 0.09f), green = new(0.18f, 0.2f, 0.14f);
		float ex = Mathf.Min(x, RugW - x), ez = Mathf.Min(z, RugL - z), e = Mathf.Min(ex, ez);
		Color c;
		if (e < 0.05f) c = brown;
		else if (e < 0.09f) c = ivory * 0.8f;
		else if (e < 0.4f)
		{
			// the border: hooked lozenges on indigo, rosettes at the corners
			float q = (e - 0.09f) / 0.31f;
			if (ex < 0.4f && ez < 0.4f)
			{
				float r = new Vector2(ex - 0.245f, ez - 0.245f).Length();
				c = r < 0.06f ? ochre : r < 0.11f ? madder : r < 0.13f ? ivory * 0.85f : indigo;
			}
			else
			{
				float t = ex < ez ? z : x;
				float ph = Mathf.PosMod(t, 0.46f) / 0.46f;
				float lz = Mathf.Abs(ph - 0.5f) * 2f + Mathf.Abs(q - 0.5f) * 2.2f;
				c = indigo;
				if (lz < 0.9f) c = madder;
				if (lz < 0.62f) c = ochre;
				if (lz < 0.3f) c = ivory * 0.85f;
				// little hooks between
				float hz = Mathf.Abs(ph < 0.5f ? ph : ph - 1f) * 2f;
				if (hz < 0.12f && Mathf.Abs(q - 0.5f) < 0.18f) c = green;
			}
		}
		else if (e < 0.44f) c = ivory * 0.78f;
		else if (e < 0.49f) c = madder * 0.75f;
		else
		{
			// the field: madder with a lattice of small flowers, a medallion in the middle, the corners quartered
			float fx = x - RugW * 0.5f, fz = z - RugL * 0.5f;
			c = rust;
			float u = Mathf.PosMod((fx + fz) / 0.38f, 1f), v = Mathf.PosMod((fx - fz) / 0.38f, 1f);
			if (Mathf.Min(u, 1f - u) < 0.04f || Mathf.Min(v, 1f - v) < 0.04f) c = rust * 0.72f;
			float fl = Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f);
			if (fl < 0.12f) c = indigo * 1.2f;
			if (fl < 0.05f) c = ochre;
			float d = Mathf.Abs(fx) / 1.15f + Mathf.Abs(fz) / 1.75f;
			float pend = Mathf.Abs(fx) / 0.3f + (Mathf.Abs(Mathf.Abs(fz) - 2.0f)) / 0.3f;
			if (d < 1f || pend < 1f)
			{
				c = indigo;
				if (d < 0.86f) c = madder * 1.08f;
				if (d < 0.6f) c = ivory * 0.82f;
				if (d < 0.44f) c = indigo;
				if (d < 0.2f) c = ochre;
				if (pend < 0.5f) c = ochre * 0.9f;
				float ring = Mathf.Abs(d - 0.72f);
				if (ring < 0.025f) c = ochre * 0.85f;
			}
			// the corners: quarter medallions
			float cx = RugW * 0.5f - 0.49f - Mathf.Abs(fx), cz = RugL * 0.5f - 0.49f - Mathf.Abs(fz);
			float cd = cx / 0.9f + cz / 0.9f;
			if (cd < 1f) { c = indigo; if (cd < 0.8f) c = madder; if (cd < 0.45f) c = ivory * 0.8f; if (cd < 0.25f) c = indigo; }
		}
		// the weave: rows a hair darker, a grain of noise
		float n = HashF(px, py, 77);
		c *= 0.94f + 0.08f * n - (py % 2 == 0 ? 0.025f : 0f);
		// faded: drained toward a dull brown, unevenly
		float blot = Smooth(px * 0.05f, py * 0.05f, 31);
		c = c.Lerp(new Color(0.3f, 0.25f, 0.2f), 0.22f + 0.18f * blot);
		// worn to the warp down the way from the door (its line is the rug's x = 2.0) and round the reading table
		float path = Mathf.Exp(-Mathf.Pow((x - 2.0f) / 0.5f, 2f)) * Mathf.SmoothStep(0f, 0.6f, z) * Mathf.SmoothStep(RugL, RugL - 1.5f, z);
		float table = Mathf.Exp(-new Vector2((x - 2.0f) / 1.3f, (z - 4.7f) / 0.9f).LengthSquared());
		float wear = Mathf.Clamp(path * 0.75f + table * 0.45f, 0f, 1f) * (0.55f + 0.6f * Smooth(px * 0.12f, py * 0.12f, 41));
		c = c.Lerp(new Color(0.42f, 0.37f, 0.29f) * (0.9f + 0.15f * n), Mathf.Clamp(wear, 0f, 0.85f));
		// stains: an old spill by the table, a damp tide ring near the back, a scorch toward the fire
		foreach (var (sx, sz, r, k) in new[] { (2.6f, 4.0f, 0.32f, 0.55f), (1.0f, 6.2f, 0.5f, 0.3f), (4.6f, 3.3f, 0.2f, 0.6f) })
		{
			float dd = new Vector2(x - sx, (z - sz) * 1.2f).Length() * (1f + 0.25f * Smooth(px * 0.3f, py * 0.3f, 53));
			if (dd < r) c *= 1f - k * (0.55f + 0.45f * Mathf.SmoothStep(r * 0.6f, r, dd));
		}
		return new Color(c.R, c.G, c.B);
	}

	private static float HashF(int x, int y, int s)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}

	private static float Smooth(float x, float y, int s)
	{
		int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
		float fx = x - x0, fy = y - y0;
		fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
		return Mathf.Lerp(Mathf.Lerp(HashF(x0, y0, s), HashF(x0 + 1, y0, s), fx), Mathf.Lerp(HashF(x0, y0 + 1, s), HashF(x0 + 1, y0 + 1, s), fx), fy);
	}

	private static Texture2D RugTex()
	{
		if (_rugTex != null) return _rugTex;
		const int w = 192, h = 256;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
		for (int py = 0; py < h; py++)
			for (int px = 0; px < w; px++)
				img.SetPixel(px, py, RugPixel((px + 0.5f) / w * RugW, (py + 0.5f) / h * RugL, px, py));
		img.GenerateMipmaps();
		return _rugTex = ImageTexture.CreateFromImage(img);
	}

	/// <summary>The fringe: matted ivory threads, some gone, their ends ragged (alpha).</summary>
	private static Texture2D FringeTex()
	{
		if (_fringeTex != null) return _fringeTex;
		const int w = 128, h = 16;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int x = 0; x < w; x++)
		{
			bool thread = x % 3 != 2 && HashF(x / 3, 0, 5) > 0.12f;
			float len = 0.55f + 0.45f * HashF(x / 3, 1, 6);
			for (int y = 0; y < h; y++)
			{
				float v = 0.75f + 0.25f * HashF(x, y, 7);
				img.SetPixel(x, y, new Color(0.5f * v, 0.45f * v, 0.35f * v, thread && y < h * len ? 1f : 0f));
			}
		}
		img.GenerateMipmaps();
		return _fringeTex = ImageTexture.CreateFromImage(img);
	}

	/// <summary>How high the rug lies at (x, z) in its own metres: flat, a ruck across its near end, its near-left corner
	/// curling up.</summary>
	private static float RugHeight(float x, float z)
	{
		float y = 0.012f;
		// the ruck: a soft fold running a little askew across the near end, dying out before the edges
		float line = z - (1.25f + (x - 1.6f) * 0.18f);
		float along = Mathf.SmoothStep(0.2f, 1.0f, x) * Mathf.SmoothStep(3.6f, 2.6f, x);
		y += 0.045f * Mathf.Exp(-line * line / (2f * 0.07f * 0.07f)) * along;
		// the curl
		float dc = new Vector2(x, z).Length();
		if (dc < 0.5f) y += Mathf.Pow((0.5f - dc) / 0.5f, 2f) * 0.13f;
		return y;
	}

	private void BuildRug()
	{
		var mat = new StandardMaterial3D { ResourceName = "lib_rug", AlbedoTexture = RugTex(), VertexColorUseAsAlbedo = true, Roughness = 1f, MetallicSpecular = 0.15f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps };
		var k = new MeshKit();
		k.Mat(mat);
		k.Color = Colors.White;
		const int nx = 26, nz = 35;
		Vector3 origin = RugAt - new Vector3(RugW * 0.5f, 0, RugL * 0.5f);
		Vector3 P(float x, float z) => origin + new Vector3(x, RugHeight(x, z), z);
		Vector3 N(float x, float z)
		{
			const float e = 0.03f;
			float dx = (RugHeight(x + e, z) - RugHeight(x - e, z)) / (2f * e), dz = (RugHeight(x, z + e) - RugHeight(x, z - e)) / (2f * e);
			return new Vector3(-dx, 1f, -dz).Normalized();
		}
		for (int j = 0; j < nz; j++)
			for (int i = 0; i < nx; i++)
			{
				float x0 = RugW * i / nx, x1 = RugW * (i + 1) / nx, z0 = RugL * j / nz, z1 = RugL * (j + 1) / nz;
				Vector2 U(float x, float z) => new(x / RugW, z / RugL);
				k.Tri(P(x0, z0), P(x1, z0), P(x1, z1), N(x0, z0), N(x1, z0), N(x1, z1), U(x0, z0), U(x1, z0), U(x1, z1));
				k.Tri(P(x0, z0), P(x1, z1), P(x0, z1), N(x0, z0), N(x1, z1), N(x0, z1), U(x0, z0), U(x1, z1), U(x0, z1));
			}
		// its edge, down to the boards (dark: the edge binding)
		k.Color = Colors.White * 0.45f;
		Vector2 edgeUv = new(0.002f, 0.002f);
		void Skirt(float xa, float za, float xb, float zb, Vector3 outward)
		{
			int n = Mathf.CeilToInt(new Vector2(xb - xa, zb - za).Length() / 0.2f);
			for (int i = 0; i < n; i++)
			{
				float t0 = (float)i / n, t1 = (float)(i + 1) / n;
				Vector3 a = P(Mathf.Lerp(xa, xb, t0), Mathf.Lerp(za, zb, t0)), b = P(Mathf.Lerp(xa, xb, t1), Mathf.Lerp(za, zb, t1));
				k.Quad(a, b, b with { Y = 0.001f }, a with { Y = 0.001f }, outward, edgeUv, edgeUv, edgeUv, edgeUv);
			}
		}
		Skirt(0, 0, RugW, 0, Vector3.Forward);
		Skirt(RugW, 0, RugW, RugL, Vector3.Right);
		Skirt(RugW, RugL, 0, RugL, Vector3.Back);
		Skirt(0, RugL, 0, 0, Vector3.Left);
		k.Color = Colors.White;
		_rugKit = k;
		// the fringe at each end: matted threads lying out over the boards, a few kinked
		var fringe = new MeshKit();
		fringe.Mat(new StandardMaterial3D { ResourceName = "lib_fringe", AlbedoTexture = FringeTex(), VertexColorUseAsAlbedo = true, Roughness = 1f, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
		foreach (int end in new[] { 0, 1 })
		{
			float z = end == 0 ? 0f : RugL, dir = end == 0 ? -1f : 1f;
			const int segs = 10;
			for (int i = 0; i < segs; i++)
			{
				float xa = RugW * i / segs, xb = RugW * (i + 1) / segs;
				float kink = 0.012f * Mathf.Sin(i * 2.7f + end);
				Vector3 a = P(xa, z) + Vector3.Up * 0.001f, b = P(xb, z) + Vector3.Up * 0.001f;
				Vector3 c = origin + new Vector3(xb, 0.004f, z + dir * (0.09f + kink)), d = origin + new Vector3(xa, 0.004f, z + dir * (0.09f - kink));
				fringe.Quad(a, b, c, d, Vector3.Up, new Vector2(xa / RugW * 6f, 0), new Vector2(xb / RugW * 6f, 0), new Vector2(xb / RugW * 6f, 1), new Vector2(xa / RugW * 6f, 1));
			}
		}
		fringe.CommitTo(this, "RugFringe", false);
	}

	private MeshKit _rugKit;

	// ------------------------------------------------------------------ what stands in the middle

	private void BuildMiddle()
	{
		var k = new MeshKit();       // furniture: casts its shadows
		var small = new MeshKit();   // the small things: none
		k.Mat(_wood);
		var cardMat = new StandardMaterial3D { ResourceName = "lib_cards", AlbedoColor = new Color(0.56f, 0.51f, 0.41f), Roughness = 0.95f, VertexColorUseAsAlbedo = true };
		var roles = new Dictionary<string, Material>
		{
			["wood"] = _wood, ["paper"] = cardMat, ["metal"] = ItemTextures.BrassMat, ["iron"] = StairwellTextures.RailMat,
			["leather"] = new StandardMaterial3D { ResourceName = "lib_binding", AlbedoColor = new Color(0.2f, 0.09f, 0.06f), Roughness = 0.7f },
			["upholstery"] = new StandardMaterial3D { ResourceName = "lib_seat", AlbedoColor = new Color(0.22f, 0.13f, 0.1f), Roughness = 0.9f },
			["label"] = cardMat, ["*"] = _wood,
		};
		var books = new BookList();

		// ---- the long table by the door, left as whoever was working at it left it
		Vector3 t2 = new(2.6f, 0, 2.2f);
		k.Color = Colors.White * 0.92f;
		k.BevelBox(t2 + new Vector3(0, 0.765f, 0), new Vector3(2.1f, 0.05f, 0.86f), 0.012f);
		k.BevelBox(t2 + new Vector3(0, 0.69f, 0), new Vector3(1.94f, 0.1f, 0.72f), 0.006f);
		foreach (float dx in new[] { -0.97f, 0.97f })
			foreach (float dz in new[] { -0.35f, 0.35f })
			{
				k.ChamferBox(t2 + new Vector3(dx, 0.32f, dz), new Vector3(0.075f, 0.64f, 0.075f), 0.01f);
				k.Cylinder(t2 + new Vector3(dx, 0.02f, dz), t2 + new Vector3(dx, 0.1f, dz), 0.03f, 0.042f, 8, true);   // a turned foot
			}
		k.Color = Colors.White;
		_body.AddChild(new CollisionShape3D { Position = t2 + new Vector3(0, 0.4f, 0), Shape = new BoxShape3D { Size = new Vector3(2.1f, 0.8f, 0.86f) } });
		_floorUnder.Add(new Aabb(t2 - new Vector3(1.05f, 0, 0.43f), new Vector3(2.1f, 0.8f, 0.86f)));
		float top = 0.79f;
		FurnitureKit.Add(k, "books_stack", t2 + new Vector3(-0.62f, top, -0.12f), 0.4f, roles);
		FurnitureKit.Add(k, "papers", t2 + new Vector3(0.3f, top, 0.12f), 1.0f, roles);
		FurnitureKit.Add(k, "candlestick", t2 + new Vector3(0.82f, top, -0.2f), 0f, roles);
		// an open book, its pages fanned up
		OpenBook(small, cardMat, t2 + new Vector3(-0.05f, top, 0.05f), 0.25f);
		// and three more laid flat, one across the others
		for (int i = 0; i < 3; i++)
		{
			float t = BookThick(), h = 0.24f + i * 0.02f;
			int kind = BookKind();
			books.Add(Lie(t2 + new Vector3(-0.85f + i * 0.02f, top + t * 0.5f + i * 0.035f, 0.26f), Vector3.Back, t, h, h * 0.72f, i * 0.4f), BookColor(kind, 0.25f), kind);
		}
		// a chair pushed back, askew; the other knocked over on its back
		FurnitureKit.Add(k, "dining_chair", t2 + new Vector3(-0.45f, 0, -0.85f), 0.38f, roles);
		_body.AddChild(new CollisionShape3D { Position = t2 + new Vector3(-0.45f, 0.45f, -0.85f), Shape = new BoxShape3D { Size = new Vector3(0.5f, 0.9f, 0.5f) } });
		var tipped = new Transform3D(new Basis(Vector3.Up, Mathf.Pi + 0.5f) * new Basis(Vector3.Right, -Mathf.Pi * 0.5f), t2 + new Vector3(0.75f, 0.24f, 1.15f));
		FurnitureKit.Add(k, "dining_chair", tipped, roles);
		_body.AddChild(new CollisionShape3D { Position = t2 + new Vector3(0.75f, 0.25f, 1.25f), Rotation = new Vector3(0, 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(0.5f, 0.5f, 0.95f) } });
		_floorUnder.Add(new Aabb(t2 + new Vector3(0.45f, 0, 0.75f), new Vector3(0.6f, 0.5f, 0.9f)));

		// ---- the card catalogue, half way down the west side, one drawer out and its cards across the floor
		CardCatalogue(k, small, cardMat, new Vector3(-2.6f, 0, 5.15f), Mathf.Pi * 0.5f + 0.12f);

		// ---- a book cart, loaded, by the east shelves
		BookCart(k, small, books, new Vector3(2.35f, 0, 10.45f), 0.55f);

		AddBooks(this, books, "MiddleBooks");
		k.CommitTo(this, "Middle", true);
		small.CommitTo(this, "MiddleSmall", false);
		BuildFloor();
	}

	/// <summary>An open book lying on its back: its two halves of pages bowed up from the gutter, the boards under them.</summary>
	private void OpenBook(MeshKit k, Material pages, Vector3 at, float yaw)
	{
		var xf = new Transform3D(new Basis(Vector3.Up, yaw), at);
		k.Xf = xf;
		k.Mat(pages);
		k.Color = Colors.White * 0.95f;
		foreach (float s in new[] { -1f, 1f })
		{
			// the pages: a block thinning toward the fore-edge, its top bowed (two facets)
			k.Box(new Vector3(s * 0.08f, 0.012f, 0), new Vector3(0.15f, 0.016f, 0.23f), 1f, new Basis(Vector3.Back, s * 0.08f));
			k.Box(new Vector3(s * 0.035f, 0.022f, 0), new Vector3(0.06f, 0.012f, 0.23f), 1f, new Basis(Vector3.Back, s * 0.25f));
		}
		k.Mat(BookMat(false));
		k.Color = new Color(0.25f, 0.12f, 0.09f);
		k.Box(new Vector3(0, 0.003f, 0), new Vector3(0.34f, 0.006f, 0.245f));
		k.Xf = Transform3D.Identity;
		k.Color = Colors.White;
	}

	/// <summary>An oak card catalogue at <paramref name="at"/>, its front turned to <paramref name="yaw"/> (0: +Z): a case
	/// of thirty drawers on legs, brass label frames and pulls; one drawer pulled out, one gone, lying upside down on the
	/// floor in front, its cards spilled.</summary>
	private void CardCatalogue(MeshKit k, MeshKit small, Material cards, Vector3 at, float yaw)
	{
		var rot = new Basis(Vector3.Up, yaw);
		var xf = new Transform3D(rot, at);
		var brass = ItemTextures.BrassMat;
		k.Xf = small.Xf = xf;
		k.Mat(_wood);
		k.Color = Colors.White * 0.85f;
		foreach (float sx in new[] { -0.46f, 0.46f })
			foreach (float sz in new[] { -0.19f, 0.19f })
				k.ChamferBox(new Vector3(sx, 0.17f, sz), new Vector3(0.05f, 0.34f, 0.05f), 0.008f);
		k.BevelBox(new Vector3(0, 0.37f, 0), new Vector3(1.0f, 0.06f, 0.46f), 0.01f);
		k.Color = Colors.White;
		k.BevelBox(new Vector3(0, 0.81f, -0.01f), new Vector3(1.0f, 0.84f, 0.46f), 0.012f);
		k.BevelBox(new Vector3(0, 1.245f, 0), new Vector3(1.08f, 0.04f, 0.53f), 0.012f);
		k.BevelBox(new Vector3(0, 1.275f, 0), new Vector3(1.02f, 0.025f, 0.49f), 0.008f);
		const float dw = 0.18f, dh = 0.122f;
		(int c, int r) pulled = (3, 2), gone = (1, 0), ajar = (0, 4);
		for (int c = 0; c < 5; c++)
			for (int r = 0; r < 6; r++)
			{
				float x = -0.4f + c * 0.2f, y = 0.47f + r * 0.135f;
				if ((c, r) == gone)
				{
					k.Color = Colors.White * 0.12f;   // the dark of the empty slot
					k.Box(new Vector3(x, y, 0.2f), new Vector3(dw, dh, 0.02f));
					k.Color = Colors.White;
					continue;
				}
				float pull = (c, r) == pulled ? 0.24f : (c, r) == ajar ? 0.06f : 0f;
				float z = 0.225f + pull;
				k.Color = Colors.White * (0.86f + 0.14f * _rng.Randf());
				k.BevelBox(new Vector3(x, y, z), new Vector3(dw, dh, 0.02f), 0.005f);
				if (pull > 0.04f)
				{
					// its sides and the cards standing in it
					k.Box(new Vector3(x - dw * 0.45f, y - 0.005f, z - pull * 0.5f - 0.01f), new Vector3(0.01f, dh * 0.75f, pull));
					k.Box(new Vector3(x + dw * 0.45f, y - 0.005f, z - pull * 0.5f - 0.01f), new Vector3(0.01f, dh * 0.75f, pull));
					small.Mat(cards);
					small.Color = Colors.White * 0.9f;
					small.Box(new Vector3(x, y - 0.012f, z - pull * 0.5f - 0.012f), new Vector3(dw - 0.03f, dh * 0.62f, pull - 0.03f));
				}
				k.Color = Colors.White;
				small.Mat(brass);
				small.Color = Colors.White * 0.75f;
				small.Box(new Vector3(x, y + 0.028f, z + 0.012f), new Vector3(0.06f, 0.028f, 0.004f));
				small.Cylinder(new Vector3(x, y - 0.025f, z + 0.01f), new Vector3(x, y - 0.025f, z + 0.03f), 0.009f, 0.012f, 6, true);
				small.Mat(cards);
				small.Color = Colors.White * 0.8f;
				small.Box(new Vector3(x, y + 0.028f, z + 0.0145f), new Vector3(0.05f, 0.02f, 0.002f));
			}
		// the drawer that came out: upside down on the floor in front, its cards fanned out round it
		var drop = new Basis(Vector3.Up, 0.7f) * new Basis(Vector3.Back, Mathf.Pi);
		k.Mat(_wood);
		k.Color = Colors.White * 0.9f;
		k.Box(new Vector3(0.12f, 0.06f, 0.78f), new Vector3(0.18f, 0.11f, 0.42f), 1f, drop);
		k.Color = Colors.White;
		small.Mat(cards);
		var rng = new RandomNumberGenerator { Seed = 1977 };
		for (int i = 0; i < 46; i++)
		{
			float a = rng.RandfRange(-1.4f, 1.4f), d = rng.RandfRange(0.12f, 0.75f) * (1f - 0.4f * Mathf.Abs(a) / 1.4f);
			Vector3 p = new Vector3(0.12f, 0, 0.78f) + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * d;
			small.Color = Colors.White * rng.RandfRange(0.72f, 0.98f);
			small.Box(p + Vector3.Up * (0.002f + (i % 4) * 0.0015f), new Vector3(0.125f, 0.0015f, 0.076f), 1f, new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)) * new Basis(Vector3.Right, rng.RandfRange(-0.06f, 0.06f)));
		}
		k.Xf = small.Xf = Transform3D.Identity;
		k.Color = small.Color = Colors.White;
		_body.AddChild(new CollisionShape3D { Position = at + rot * new Vector3(0, 0.65f, 0), Rotation = new Vector3(0, yaw, 0), Shape = new BoxShape3D { Size = new Vector3(1.08f, 1.3f, 0.53f) } });
		_floorUnder.Add(new Aabb(at - new Vector3(0.45f, 0, 0.45f), new Vector3(0.9f, 1.3f, 0.9f)));
	}

	/// <summary>A library's book cart at <paramref name="at"/>: two shelves between end panels on casters, a push bar, a
	/// divider down the top shelf with books leaning on it both sides, more laid flat below.</summary>
	private void BookCart(MeshKit k, MeshKit small, BookList books, Vector3 at, float yaw)
	{
		var rot = new Basis(Vector3.Up, yaw);
		var xf = new Transform3D(rot, at);
		k.Xf = small.Xf = xf;
		k.Mat(_wood);
		k.Color = Colors.White * 0.9f;
		foreach (float sx in new[] { -0.4f, 0.4f }) k.BevelBox(new Vector3(sx, 0.46f, 0), new Vector3(0.03f, 0.72f, 0.42f), 0.008f);
		k.Color = Colors.White;
		k.BevelBox(new Vector3(0, 0.2f, 0), new Vector3(0.78f, 0.024f, 0.42f), 0.006f);
		k.BevelBox(new Vector3(0, 0.62f, 0), new Vector3(0.78f, 0.024f, 0.42f), 0.006f);
		k.BevelBox(new Vector3(0, 0.72f, 0), new Vector3(0.78f, 0.18f, 0.02f), 0.005f);
		small.Mat(StairwellTextures.RailMat);
		small.Color = Colors.White * 0.6f;
		foreach (float sx in new[] { -0.36f, 0.36f })
			foreach (float sz in new[] { -0.17f, 0.17f })
			{
				small.Cylinder(new Vector3(sx, 0.11f, sz), new Vector3(sx, 0.075f, sz), 0.012f, 0.012f, 5, false);
				small.Cylinder(new Vector3(sx - 0.012f, 0.04f, sz), new Vector3(sx + 0.012f, 0.04f, sz), 0.038f, 0.038f, 8, true);
			}
		small.Cylinder(new Vector3(0.43f, 0.82f, -0.17f), new Vector3(0.43f, 0.82f, 0.17f), 0.014f, 0.014f, 6, true);
		foreach (float sz in new[] { -0.17f, 0.17f }) small.Cylinder(new Vector3(0.41f, 0.82f, sz), new Vector3(0.43f, 0.82f, sz), 0.01f, 0.01f, 5, false);
		k.Xf = small.Xf = Transform3D.Identity;
		k.Color = small.Color = Colors.White;
		Vector3 ax = rot.X, az = rot.Z;
		// the top shelf, both sides of the divider (spines out), the lower one a few piles
		FillShelf(books, at + ax * -0.38f + az * 0.012f, ax, az, 0f, 0.76f, 0.632f, 0.32f, false, false);
		FillShelf(books, at + ax * 0.38f - az * 0.012f, -ax, -az, 0f, 0.76f, 0.632f, 0.32f, false, false);
		for (int p = 0; p < 2; p++)
		{
			float y = 0.212f;
			for (int i = 0; i < 4 - p; i++)
			{
				float t = BookThick(), h = 0.24f + 0.03f * p;
				int kind = BookKind();
				books.Add(Lie(at + ax * (-0.2f + p * 0.38f) + Vector3.Up * (y + t * 0.5f), az, t, h, h * 0.72f, yaw + _rng.RandfRange(-0.15f, 0.15f) - yaw), BookColor(kind, 0.25f), kind);
				y += t;
			}
		}
		_body.AddChild(new CollisionShape3D { Position = at + Vector3.Up * 0.45f, Rotation = new Vector3(0, yaw, 0), Shape = new BoxShape3D { Size = new Vector3(0.86f, 0.9f, 0.44f) } });
		_floorUnder.Add(new Aabb(at - new Vector3(0.45f, 0, 0.3f), new Vector3(0.9f, 0.9f, 0.6f)));
	}

	/// <summary>The parquet, drawn finely, and the room's dark and wear baked into it and into the rug.</summary>
	private void BuildFloor()
	{
		var floorMat = SurfaceSets.Relief(new StandardMaterial3D { ResourceName = "lib_parquet", AlbedoTexture = PropTextures.DeckMat.AlbedoTexture, AlbedoColor = new Color(0.4f, 0.29f, 0.21f), Roughness = 0.75f, MetallicSpecular = 0.35f, VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 1.8f }, "hinoki", 1f);
		var lk = new MeshKit();
		lk.Mat(RoomSurface.Of(floorMat, RoomSurface.Wood with { FloorDirt = 0.6f, Wear = 0.75f }, 19));
		lk.Tess = 0.5f;
		lk.Quad(new Vector3(-HalfW, 0, 0), new Vector3(HalfW, 0, 0), new Vector3(HalfW, 0, Depth), new Vector3(-HalfW, 0, Depth), Vector3.Up,
			new Vector2(-HalfW, 0), new Vector2(HalfW, 0), new Vector2(HalfW, Depth), new Vector2(-HalfW, Depth));
		lk.Tess = 0f;
		var shade = new RoomShade(-HalfW, HalfW, 0f, Depth, 0f, Height) { Dim = 0.78f, Reach = 1.0f };
		// what stands on it
		shade.Under(new Vector3(0, 0, 8.2f), new Vector3(1.6f, 0.75f, 0.9f), 0.4f, 0.4f)                 // the reading table
			.Under(TableAt, new Vector3(0.6f, 0.72f, 1.25f), 0.45f, 0.4f)                                // the sheeted table
			.Under(new Vector3(4.0f, 0, 4.75f), new Vector3(0.85f, 0.8f, 0.85f), 0.55f, 0.35f)           // the chairs by the fire
			.Under(new Vector3(4.0f, 0, 8.25f), new Vector3(0.85f, 0.8f, 0.85f), 0.55f, 0.35f)
			.Under(new Vector3(4.7f, 0, Depth - 0.75f), new Vector3(0.52f, 2.1f, 0.44f), 0.5f, 0.3f)     // the clock
			.Under(new Vector3(-2.8f, 0, 10.8f), new Vector3(0.34f, 1.2f, 0.34f), 0.4f, 0.3f);          // the globe
		foreach (var b in _floorUnder) shade.Under(b, 0.45f, 0.35f);
		// its light: the lamps, the fire, the sconces
		shade.Light(new Vector3(0.4f, 1.1f, 8.2f), 3.6f).Light(new Vector3(-1.6f, 1.6f, 1.0f), 3.4f, 0.9f).Light(new Vector3(HalfW - 0.4f, 0.5f, 6.5f), 4.6f)
			.Light(new Vector3(-HalfW + 0.5f, 2.6f, 3.5f), 3f, 0.7f).Light(new Vector3(HalfW - 0.5f, 2.6f, 10.5f), 3f, 0.7f);
		// where feet went: the door to the back, to the fire, to the west shelves
		shade.Path(new Vector3(0, 0, 0.2f), new Vector3(0, 0, Depth - 1f), 1.2f, 0.8f).Path(new Vector3(0, 0, 3f), new Vector3(3.4f, 0, 6.5f), 0.9f, 0.6f)
			.Path(new Vector3(0, 0, 4f), new Vector3(-4.2f, 0, 7.5f), 0.8f, 0.5f);
		shade.Apply(lk);
		lk.CommitTo(this, "Floor", false);
		if (_rugKit != null)
		{
			var rs = new RoomShade(-HalfW, HalfW, 0f, Depth, 0f, Height) { Dim = 0.8f, Reach = 0.6f, Seam = 0.3f };
			rs.Under(new Vector3(0, 0, 8.2f), new Vector3(1.6f, 0.75f, 0.9f), 0.4f, 0.35f)
				.Light(new Vector3(0.4f, 1.1f, 8.2f), 3.6f).Light(new Vector3(-1.6f, 1.6f, 1.0f), 3.4f, 0.9f).Light(new Vector3(HalfW - 0.4f, 0.5f, 6.5f), 4.6f);
			foreach (var b in _floorUnder) rs.Under(b, 0.4f, 0.3f);
			rs.Apply(_rugKit);
			_rugKit.CommitTo(this, "Rug", false);
			_rugKit = null;
		}
	}
}
