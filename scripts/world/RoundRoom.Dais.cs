using System.Collections.Generic;
using Godot;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// The dais (2026-10-14h, the interiors pass: "a plain grey disc inside an unshaded pure-red ring"). Worn dressed stone
/// laid as flags: a round centre stone, a ring of eight, a kerb of sixteen, their joints dark, each stone its own shade
/// and its edges grimed; the brass inlay along the joints; a scuffed chalk circle drawn on it, old wax stubs and their
/// runs by the posts. The ring is now a thing: an iron band set into the sloped foot, its red paint chipped back to rust,
/// with sixty-four signal lenses in brass bezels round it, ruby and green by turns, each with a lamp under it
/// (<c>dais_lens.gdshader</c>): the ruby lamps burn while the power is off ("the ring stays red"), and when it comes back
/// the green ones light in a chaser round it until they all burn. The lamps' light on the floor is the ring light; the
/// unshaded glow painted on the floor round it is gone. Four clusters of candles stand on the floor round the dais's
/// foot, cold until every candle in the drum lights itself.
/// </summary>
public partial class RoundRoom
{
	private static Texture2D _bandTex;

	/// <summary>The band's chipped red paint over rusted iron (a strip, repeating round the ring).</summary>
	private static Texture2D BandTex()
	{
		if (_bandTex != null) return _bandTex;
		const int w = 256, h = 16;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
		float H(int x, int y, int s)
		{
			uint q = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
			q = (q ^ (q >> 13)) * 1274126177u;
			return ((q ^ (q >> 16)) & 0xffffff) / 16777215f;
		}
		float N(float x, float y, int s)
		{
			int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
			float fx = x - x0, fy = y - y0;
			fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
			int W(int v) => ((v % 32) + 32) % 32;
			return Mathf.Lerp(Mathf.Lerp(H(W(x0), y0, s), H(W(x0 + 1), y0, s), fx), Mathf.Lerp(H(W(x0), y0 + 1, s), H(W(x0 + 1), y0 + 1, s), fx), fy);
		}
		Color paint = new(0.5f, 0.09f, 0.06f), iron = new(0.11f, 0.095f, 0.085f), rust = new(0.33f, 0.17f, 0.08f);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float n = N(x / 8f, y / 4f, 3) * 0.65f + N(x / 3f, y / 2f, 4) * 0.35f;
				// chipped most along the edges (where feet caught it)
				float edge = Mathf.Abs(y - (h - 1) * 0.5f) / ((h - 1) * 0.5f);
				float chip = n + edge * 0.28f;
				Color c = chip < 0.56f ? paint * (0.82f + 0.3f * N(x / 2f, y, 5)) : chip < 0.62f ? rust : iron.Lerp(rust, N(x / 4f, y / 3f, 6) * 0.7f);
				c *= 0.9f + 0.12f * H(x, y, 9);
				img.SetPixel(x, y, c);
			}
		img.GenerateMipmaps();
		return _bandTex = ImageTexture.CreateFromImage(img);
	}

	/// <summary>The dais: a low round stone step, sloped at its foot, with iron posts round its rim. It moves (an
	/// AnimatableBody) and carries the player up.</summary>
	private void BuildDais()
	{
		Dais = new Node3D { Name = "Dais" };
		AddChild(Dais);
		var rng = new RandomNumberGenerator { Seed = 2027 };
		var k = new MeshKit();
		k.Mat(DaisStone);
		// the sloped foot and the column under it (seen on the way up)
		// the sloped foot: twenty-four dressed blocks, their joints dark (the bed under them), grimed toward the floor
		k.Color = new Color(0.16f, 0.15f, 0.13f);
		k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, DaisTop - 0.004f, 0), DaisFoot - 0.006f, DaisR - 0.006f, 48, false);
		{
			const int blocks = 24, na = 4, ns = 3;
			var sn = new Vector2(DaisTop, DaisFoot - DaisR).Normalized();
			for (int b = 0; b < blocks; b++)
			{
				float shade = rng.RandfRange(0.62f, 0.9f);
				Color tint = new Color(0.84f, 0.8f, 0.72f) * shade;
				float a0 = Mathf.Tau * b / blocks, a1 = Mathf.Tau * (b + 1) / blocks;
				Vector3 Pt(int i, int j)
				{
					float s = (float)i / ns, r = Mathf.Lerp(DaisFoot, DaisR, s), ins = 0.006f / r;
					float a = Mathf.Lerp(a0 + ins, a1 - ins, (float)j / na);
					return new Vector3(Mathf.Sin(a) * r, DaisTop * s, Mathf.Cos(a) * r);
				}
				Vector3 Nm(int j) { float a = Mathf.Lerp(a0, a1, (float)j / na); return new Vector3(Mathf.Sin(a) * sn.X, sn.Y, Mathf.Cos(a) * sn.X); }
				Color Cc(int i, int j) => tint * Mathf.Lerp(0.55f, 0.95f, (float)i / ns) * (j == 0 || j == na || i == ns ? 0.8f : 1f);
				for (int i = 0; i < ns; i++)
					for (int j = 0; j < na; j++)
					{
						Vector3 p0 = Pt(i, j), p1 = Pt(i, j + 1), p2 = Pt(i + 1, j + 1), p3 = Pt(i + 1, j);
						Vector2 U(Vector3 p, int jj) => new(Mathf.Lerp(a0, a1, (float)jj / na) * DaisFoot * 0.7f, p.Y * 2f);
						k.TriC(p0, p1, p2, Nm(j), Nm(j + 1), Nm(j + 1), U(p0, j), U(p1, j + 1), U(p2, j + 1), Cc(i, j), Cc(i, j + 1), Cc(i + 1, j + 1));
						k.TriC(p0, p2, p3, Nm(j), Nm(j + 1), Nm(j), U(p0, j), U(p2, j + 1), U(p3, j), Cc(i, j), Cc(i + 1, j + 1), Cc(i + 1, j));
					}
			}
		}
		k.Color = new Color(0.6f, 0.57f, 0.52f);
		k.Cylinder(new Vector3(0, -4f, 0), new Vector3(0, 0.001f, 0), DaisFoot, DaisFoot, 48, false);
		// the joints: a dark bed under the flags
		k.Color = new Color(0.22f, 0.2f, 0.18f);
		for (int i = 0; i < 48; i++)
		{
			float a0 = Mathf.Tau * i / 48f, a1 = Mathf.Tau * (i + 1) / 48f;
			k.Tri(new Vector3(0, DaisTop - 0.006f, 0), new Vector3(Mathf.Sin(a0) * DaisR, DaisTop - 0.006f, Mathf.Cos(a0) * DaisR), new Vector3(Mathf.Sin(a1) * DaisR, DaisTop - 0.006f, Mathf.Cos(a1) * DaisR), Vector3.Up, Vector2.Zero, new Vector2(1, 0), new Vector2(0, 1));
		}
		// the flags: the centre stone, a ring of eight, a kerb of sixteen
		const float gap = 0.011f;
		void Flag(float r0, float r1, float a0, float a1, int na)
		{
			float y = DaisTop + rng.RandfRange(-0.003f, 0.003f);
			float shade = rng.RandfRange(0.66f, 0.92f);
			Color tint = new Color(0.86f, 0.82f, 0.74f) * shade;
			const int nr = 3;
			Vector3 P(int i, int j)
			{
				float r = Mathf.Lerp(Mathf.Max(r0 + gap, 0f), r1 - gap, (float)i / nr);
				float ins = r > 0.05f ? gap / r : 0f;
				float a = Mathf.Lerp(a0 + ins, a1 - ins, (float)j / na);
				// worn: dished a little toward the middle, where feet stood
				float dish = -0.004f * Mathf.Max(0f, 1f - r / 1.2f);
				return new Vector3(Mathf.Sin(a) * r, y + dish, Mathf.Cos(a) * r);
			}
			Color C(int i, int j) => (i == 0 && r0 > 0.01f) || i == nr || j == 0 || j == na ? tint * 0.8f : tint;
			for (int i = 0; i < nr; i++)
				for (int j = 0; j < na; j++)
				{
					Vector3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
					Vector2 U(Vector3 p) => new(p.X * 0.7f, p.Z * 0.7f);
					k.TriC(a, b, c, Vector3.Up, Vector3.Up, Vector3.Up, U(a), U(b), U(c), C(i, j), C(i + 1, j), C(i + 1, j + 1));
					k.TriC(a, c, d, Vector3.Up, Vector3.Up, Vector3.Up, U(a), U(c), U(d), C(i, j), C(i + 1, j + 1), C(i, j + 1));
				}
		}
		Flag(0f, 0.7f, 0f, Mathf.Tau, 16);
		for (int i = 0; i < 8; i++) Flag(0.7f, 1.75f, Mathf.Tau * i / 8f, Mathf.Tau * (i + 1) / 8f, 4);
		for (int i = 0; i < 16; i++) Flag(1.75f, DaisR + gap, Mathf.Tau * (i + 0.5f) / 16f, Mathf.Tau * (i + 1.5f) / 16f, 3);
		k.CommitTo(Dais, "Stone", true);
		// the brass inlay along the joints: two rings and eight spokes, worn dull
		var inlay = new MeshKit();
		inlay.Mat(new StandardMaterial3D { ResourceName = "dais_inlay", AlbedoColor = new Color(0.42f, 0.33f, 0.2f), Roughness = 0.55f, MetallicSpecular = 0.6f, Metallic = 0.55f, AlbedoTexture = ProcTextures.Grime(), VertexColorUseAsAlbedo = true });
		for (int i = 0; i < 48; i++)
		{
			float a0 = Mathf.Tau * i / 48f, a1 = Mathf.Tau * (i + 1) / 48f;
			inlay.Color = Colors.White * rng.RandfRange(0.75f, 1f);
			foreach (float r in new[] { 1.75f, 0.7f })
				inlay.Beam(new Vector3(Mathf.Sin(a0) * r, DaisTop + 0.002f, Mathf.Cos(a0) * r), new Vector3(Mathf.Sin(a1) * r, DaisTop + 0.002f, Mathf.Cos(a1) * r), 0.04f, 0.006f);
		}
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * i / 8f;
			inlay.Beam(new Vector3(Mathf.Sin(a) * 0.7f, DaisTop + 0.002f, Mathf.Cos(a) * 0.7f), new Vector3(Mathf.Sin(a) * 1.75f, DaisTop + 0.002f, Mathf.Cos(a) * 1.75f), 0.035f, 0.006f);
		}
		inlay.CommitTo(Dais, "Inlay", false);
		// a chalk circle drawn on it once, scuffed half away, and a few strokes inside it
		var chalk = new MeshKit();
		chalk.Mat(new StandardMaterial3D { ResourceName = "dais_chalk", AlbedoColor = new Color(0.6f, 0.58f, 0.53f), Roughness = 1f, VertexColorUseAsAlbedo = true });
		for (int i = 0; i < 90; i++)
		{
			if (rng.Randf() < 0.32f) continue;
			float a0 = Mathf.Tau * i / 90f, a1 = Mathf.Tau * (i + 1) / 90f, r = 1.24f + 0.015f * Mathf.Sin(i * 0.7f);
			chalk.Color = Colors.White * rng.RandfRange(0.55f, 1f);
			chalk.Beam(new Vector3(Mathf.Sin(a0) * r, DaisTop + 0.004f, Mathf.Cos(a0) * r), new Vector3(Mathf.Sin(a1) * r, DaisTop + 0.004f, Mathf.Cos(a1) * r), rng.RandfRange(0.012f, 0.026f), 0.002f);
		}
		for (int i = 0; i < 7; i++)
		{
			float a = rng.RandfRange(0f, Mathf.Tau), r0 = rng.RandfRange(0.85f, 1.05f), r1 = r0 + rng.RandfRange(0.08f, 0.17f), da = rng.RandfRange(-0.15f, 0.15f);
			chalk.Color = Colors.White * rng.RandfRange(0.5f, 0.85f);
			chalk.Beam(new Vector3(Mathf.Sin(a) * r0, DaisTop + 0.004f, Mathf.Cos(a) * r0), new Vector3(Mathf.Sin(a + da) * r1, DaisTop + 0.004f, Mathf.Cos(a + da) * r1), 0.016f, 0.002f);
		}
		chalk.CommitTo(Dais, "Chalk", false);
		// iron posts round the rim (the web's anchors), and old wax stubs at the feet of three of them
		var posts = new MeshKit();
		posts.Mat(StairwellTextures.SteelMat);
		posts.Color = new Color(0.25f, 0.24f, 0.23f);
		var wax = new MeshKit();
		wax.Mat(WaxMaterial.Make(new Color(0.46f, 0.42f, 0.35f), 0.7f, "dais_wax"));
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * (i + 0.5f) / 8f;
			Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
			Vector3 at = d * (DaisR - 0.12f) + Vector3.Up * DaisTop;
			// (2026-10-15, the repetition pass: each its own height and lean, its own rust; they stood as eight identical sticks)
			Vector3 tip = PostTip(i);
			posts.Color = new Color(0.25f, 0.24f, 0.23f) * (0.8f + 0.35f * PostHash(i, 7.7f)) + new Color(0.05f, 0.02f, 0f) * PostHash(i, 3.3f);
			posts.Cylinder(at, tip, 0.035f + 0.006f * PostHash(i, 1.9f), 0.03f, 8, true);
			posts.Blob(tip + (tip - at).Normalized() * 0.03f, Vector3.One * (0.042f + 0.016f * PostHash(i, 5.1f)), i, 0.05f);
			if (i % 3 != 0) continue;
			Vector3 side = d.Cross(Vector3.Up);
			Vector3 c = at - d * 0.2f;
			wax.Color = Colors.White * rng.RandfRange(0.85f, 1f);
			wax.Blob(c + Vector3.Up * 0.004f, new Vector3(0.11f, 0.008f, 0.09f), i + 40, 0.25f);   // the pool
			wax.Blob(c + d * 0.13f + Vector3.Up * 0.004f, new Vector3(0.05f, 0.006f, 0.08f), i + 50, 0.3f);   // a run toward the edge
			for (int s = 0; s < 3; s++)
			{
				Vector3 p = c + side * ((s - 1) * 0.06f) + d * rng.RandfRange(-0.03f, 0.03f);
				float h = rng.RandfRange(0.03f, 0.13f), r = rng.RandfRange(0.022f, 0.034f);
				wax.Cylinder(p + Vector3.Up * DaisTop * 0f, p + Vector3.Up * h, r * 1.15f, r * 0.9f, 8, true);
				wax.Blob(p + Vector3.Up * h, new Vector3(r, 0.012f, r), i * 7 + s, 0.35f);   // the guttered top
			}
		}
		posts.CommitTo(Dais, "Posts", true);
		wax.CommitTo(Dais, "Wax", false);
		_daisBody = new AnimatableBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0, SyncToPhysics = false };
		_daisBody.SetMeta("surface", "stone");
		var pts = new List<Vector3>();
		for (int i = 0; i < 32; i++)
		{
			float a = Mathf.Tau * i / 32f;
			pts.Add(new Vector3(Mathf.Sin(a) * DaisFoot, 0, Mathf.Cos(a) * DaisFoot));
			pts.Add(new Vector3(Mathf.Sin(a) * DaisR, DaisTop, Mathf.Cos(a) * DaisR));
			pts.Add(new Vector3(Mathf.Sin(a) * DaisFoot, -0.4f, Mathf.Cos(a) * DaisFoot));
		}
		_daisBody.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = pts.ToArray() } });
		Dais.AddChild(_daisBody);
	}

	/// <summary>The ring round the dais, on its sloped foot (so it rides up with it): an iron band of chipped red paint,
	/// its ruby and green signal lenses, and their light.</summary>
	private void BuildRing()
	{
		_ringMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/dais_lens.gdshader"), ResourceName = "dais_lens" };
		float Slope(float s, out float y) { y = DaisTop * s; return DaisFoot - (DaisFoot - DaisR) * s; }
		var nrm = new Vector2(DaisTop, DaisFoot - DaisR).Normalized();   // (outward, up)
		const int n = 96;
		const float proud = 0.014f;
		var band = new MeshKit();
		band.Mat(new StandardMaterial3D { ResourceName = "dais_band", AlbedoTexture = BandTex(), Roughness = 0.62f, Metallic = 0.25f, MetallicSpecular = 0.45f, VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps });
		band.Color = Colors.White;
		Vector3 P(float a, float s, float off)
		{
			float r = Slope(s, out float y);
			return new Vector3(Mathf.Sin(a) * (r + nrm.X * off), y + nrm.Y * off, Mathf.Cos(a) * (r + nrm.X * off));
		}
		Vector3 Nr(float a) => new(Mathf.Sin(a) * nrm.X, nrm.Y, Mathf.Cos(a) * nrm.X);
		const float s0 = 0.06f, s1 = 0.25f;
		for (int i = 0; i < n; i++)
		{
			float a0 = Mathf.Tau * i / n, a1 = Mathf.Tau * (i + 1) / n, u0 = 12f * i / n, u1 = 12f * (i + 1) / n;
			band.Quad(P(a0, s0, proud), P(a1, s0, proud), P(a1, s1, proud), P(a0, s1, proud), Nr((a0 + a1) * 0.5f), new Vector2(u0, 1), new Vector2(u1, 1), new Vector2(u1, 0), new Vector2(u0, 0));
			// its two edges, down into the stone
			band.Color = Colors.White * 0.7f;
			Vector3 down = new(Mathf.Sin((a0 + a1) * 0.5f), 0, Mathf.Cos((a0 + a1) * 0.5f));
			band.Quad(P(a0, s0, 0f), P(a1, s0, 0f), P(a1, s0, proud), P(a0, s0, proud), (down - Vector3.Up * 0.3f).Normalized(), new Vector2(u0, 1), new Vector2(u1, 1), new Vector2(u1, 0.9f), new Vector2(u0, 0.9f));
			band.Quad(P(a0, s1, 0f), P(a1, s1, 0f), P(a1, s1, proud), P(a0, s1, proud), (Vector3.Up - down * 0.3f).Normalized(), new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 0.1f), new Vector2(u0, 0.1f));
			band.Color = Colors.White;
		}
		band.CommitTo(Dais, "RingBand", false);
		// the lenses: a low dome of glass in a brass bezel, ruby and green by turns
		var lens = new MeshKit();
		lens.Mat(_ringMat);
		var bezel = new MeshKit();
		bezel.Mat(ItemTextures.BrassMat);
		bezel.Color = new Color(0.6f, 0.48f, 0.3f);
		const int lenses = 64;
		const float sl = (s0 + s1) * 0.5f, lr = 0.034f;
		for (int i = 0; i < lenses; i++)
		{
			float a = Mathf.Tau * (i + 0.5f) / lenses;
			Vector3 axis = Nr(a);
			Vector3 c = P(a, sl, proud);
			Vector3 t1 = new Vector3(Mathf.Cos(a), 0, -Mathf.Sin(a)), t2 = axis.Cross(t1).Normalized();
			bezel.Cylinder(c - axis * 0.002f, c + axis * 0.007f, lr + 0.01f, lr + 0.008f, 10, true);
			lens.Color = i % 2 == 0 ? new Color(1, 0, 0) : new Color(0, 1, 0);
			Vector3 b = c + axis * 0.007f;
			float u = (i + 0.5f) / lenses;
			const int seg = 10;
			// the dome: a crown point and two rings
			float[] ringR = { lr, lr * 0.72f, 0f }, ringH = { 0f, lr * 0.45f, lr * 0.62f };
			for (int ri = 0; ri < 2; ri++)
				for (int j = 0; j < seg; j++)
				{
					float b0 = Mathf.Tau * j / seg, b1 = Mathf.Tau * (j + 1) / seg;
					Vector3 D(float ang) => t1 * Mathf.Cos(ang) + t2 * Mathf.Sin(ang);
					Vector3 p00 = b + D(b0) * ringR[ri] + axis * ringH[ri], p01 = b + D(b1) * ringR[ri] + axis * ringH[ri];
					Vector3 p10 = b + D(b0) * ringR[ri + 1] + axis * ringH[ri + 1], p11 = b + D(b1) * ringR[ri + 1] + axis * ringH[ri + 1];
					Vector3 N(Vector3 p) => (p - (b - axis * lr * 0.6f)).Normalized();
					Vector2 uv0 = new(u, 1f - ri * 0.5f), uv1 = new(u, 0.5f - ri * 0.5f);
					lens.Tri(p00, p01, p11, N(p00), N(p01), N(p11), uv0, uv0, uv1);
					if (ri == 0) lens.Tri(p00, p11, p10, N(p00), N(p11), N(p10), uv0, uv1, uv1);
				}
		}
		bezel.CommitTo(Dais, "RingBezels", false);
		var lensMesh = lens.CommitTo(Dais, "Ring", false);
		if (lensMesh != null) lensMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		// the lamps' light, round the foot of the dais (red now, green once the power's back)
		_ringLight = new OmniLight3D { Name = "RingLight", Position = new Vector3(0, 0.3f, 0), LightColor = Red, LightEnergy = 1.6f, OmniRange = 6.5f, OmniAttenuation = 1.4f, ShadowEnabled = false, LightCullMask = ~WebLayer };
		Dais.AddChild(_ringLight);
		SetRing(0f, 0f, 1f);
	}

	private static float PostHash(int i, float k) => Mathf.PosMod(Mathf.Sin(i * 12.9898f + k * 78.233f) * 43758.5453f, 1f);

	/// <summary>(2026-10-15) Dais post <paramref name="i"/>'s top (the web's anchor): its own height, leant a little.</summary>
	private static Vector3 PostTip(int i)
	{
		float a = Mathf.Tau * (i + 0.5f) / 8f;
		Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a)), side = d.Cross(Vector3.Up);
		float h = 1.05f * (0.86f + 0.22f * PostHash(i, 0.7f));
		Vector3 lean = d * (PostHash(i, 2.3f) - 0.4f) * 0.12f + side * (PostHash(i, 4.6f) - 0.5f) * 0.1f;
		return d * (DaisR - 0.12f) + Vector3.Up * (DaisTop + h) + lean;
	}

	/// <summary>Candles in clusters on the floor round the dais's foot, on their own pools of old wax: cold, until the
	/// power comes back and every candle in the drum lights itself (they're the first tier of <c>_candles</c>).</summary>
	private void BuildFloorCandles()
	{
		var rng = new RandomNumberGenerator { Seed = 2031 };
		var wax = new MeshKit();
		wax.Mat(WaxMaterial.Make(new Color(0.46f, 0.42f, 0.35f), 0.65f, "roundroom_floor_wax"));
		var flameMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.45f), EmissionEnabled = true, Emission = new Color(1f, 0.62f, 0.28f), EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		for (int q = 0; q < 4; q++)
		{
			float a = Mathf.Tau * (q + 0.5f) / 4f;
			Vector3 c = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (DaisFoot + 0.85f);
			wax.Color = Colors.White * 0.9f;
			wax.Blob(c + Vector3.Up * 0.004f, new Vector3(0.26f, 0.01f, 0.22f), q + 70, 0.3f);
			var flames = new MeshKit();
			flames.Mat(flameMat);
			int count = rng.RandiRange(4, 6);
			float topSum = 0f;
			var tops = new List<Vector3>();
			for (int i = 0; i < count; i++)
			{
				float ang = rng.RandfRange(0f, Mathf.Tau), d = i == 0 ? 0f : rng.RandfRange(0.08f, 0.2f);
				Vector3 p = c + new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang)) * d;
				float h = i == 0 ? rng.RandfRange(0.26f, 0.36f) : rng.RandfRange(0.07f, 0.24f), r = rng.RandfRange(0.03f, 0.05f);
				wax.Color = Colors.White * rng.RandfRange(0.82f, 1f);
				// (2026-10-15: turned, its foot spread into the pool, its top burnt down into a cup with a ragged rim; it
				// was a capped cylinder with a lump on it)
				TurnedKit.Lathe(wax, p, TurnedKit.P(0f, 0f, r * 1.35f, 0f, r * 1.15f, 0.012f, r * 1.06f, 0.03f, r, h - 0.01f, r * 1.03f, h + 0.004f, r * 0.82f, h + 0.006f,
					r * 0.5f, h - 0.008f, 0f, h - 0.01f), 9, 40f, 1f, 1f, q * 1.3f + i);
				wax.Blob(p + Vector3.Up * (h * 0.6f) + new Vector3(r, 0, 0), new Vector3(0.012f, h * 0.35f, 0.012f), q * 17 + i, 0.2f);   // a drip down its side
				tops.Add(p + Vector3.Up * (h + 0.035f));
				topSum += h;
			}
			Vector3 mid = c + Vector3.Up * (topSum / count + 0.035f);
			foreach (var t in tops) flames.Blob(t - mid, new Vector3(0.012f, 0.026f, 0.012f), 3, 0f, false);
			var flame = flames.CommitTo(this, $"FloorFlames{q}", false);
			flame.Position = mid;
			flame.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			var light = new OmniLight3D { Name = $"FloorCandle{q}", Position = c + Vector3.Up * 0.45f, LightColor = CandleColour, LightEnergy = 0.9f, OmniRange = 5f, OmniAttenuation = 1.3f, ShadowEnabled = false };
			AddChild(light);
			_candles.Add((flame, light, 0.9f, 0.3f, a));
		}
		wax.CommitTo(this, "FloorCandles", false);
	}
}
