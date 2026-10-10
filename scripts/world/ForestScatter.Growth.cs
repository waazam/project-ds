using Godot;

namespace ProjectDS.World;

/// <summary>
/// The undergrowth pass (2026-10-03, the owner: "add some more tree/growth density in the opening acts forest", with
/// photos from the woods near them: thin lichen-streaked trunks crowding between the big ones, rotten logs, vines
/// smothering whole shrubs and trees into mounds, moss hanging in grey beards from dead branches):
/// <list type="bullet">
/// <item>saplings: thin trunks, pale with lichen, twigs with loose clusters of leaves (a share yellowing), leaning a
/// little; between the firs;</item>
/// <item>vine mounds: whatever stood there smothered under a shroud of ivy, a lumpy dome of leaves, on the woods'
/// edges by the trail and round the clearings;</item>
/// <item>hanging moss: grey-green beards off the dead stubs of the firs and the snags' branches (a share of the trees,
/// more in the deep woods), swaying a little.</item>
/// </list>
/// Placed with the trees' own rules (the trail, the stream, the test route, the clearings kept clear).
/// </summary>
public partial class ForestScatter
{
	private static ShaderMaterial _moss;
	/// <summary>Hanging moss: crossed cards of strands, rooted along the card's top (its v = 1 edge), swaying below.</summary>
	internal static ShaderMaterial MossMat => _moss ??= MakeMossMat();

	private static ShaderMaterial MakeMossMat()
	{
		const int w = 64, h = 128;
		var __tg = Systems.TexGen.Start();
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var bg = new Color(0.42f, 0.46f, 0.37f, 0f);
		img.Fill(bg);
		var rng = new RandomNumberGenerator { Seed = 4401 };
		for (int s = 0; s < 20; s++)
		{
			// a strand: from the top of the card (image bottom) down a random length, wandering, thinning
			float x = rng.RandfRange(4f, w - 4f), len = rng.RandfRange(0.35f, 1f) * h, ph = rng.RandfRange(0f, 6f);
			float lum = rng.RandfRange(0.75f, 1.12f);
			var col = new Color(0.34f * lum, 0.37f * lum, 0.3f * lum);
			for (int i = 0; i < len; i++)
			{
				int y = h - 1 - i;
				float u = i / len;
				float cx = x + Mathf.Sin(i * 0.11f + ph) * 2.5f * u + Mathf.Sin(i * 0.37f + ph * 2f) * 0.8f;
				float wid = Mathf.Lerp(1.1f, 0.5f, u);
				for (int px = Mathf.FloorToInt(cx - wid); px <= Mathf.CeilToInt(cx + wid); px++)
				{
					if (px < 0 || px >= w) continue;
					float cover = Mathf.Clamp(wid - Mathf.Abs(px + 0.5f - cx) + 0.5f, 0f, 1f);
					if (cover < 0.5f) continue;
					img.SetPixel(px, y, new Color(col.R, col.G, col.B, 1f));
				}
				// the odd tuft off it
				if (rng.Randf() < 0.04f)
					for (int t = 1; t < 5; t++)
					{
						int tx = Mathf.Clamp((int)(cx + t * (rng.Randf() < 0.5f ? -1 : 1)), 0, w - 1), ty = Mathf.Clamp(y - t, 0, h - 1);
						img.SetPixel(tx, ty, new Color(col.R, col.G, col.B, 1f));
					}
			}
		}
		img.GenerateMipmaps();
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
		DetailKit.Hook(m, DetailKit.Kind.Foliage);
		m.SetShaderParameter("albedo_tex", ImageTexture.CreateFromImage(img));
		Systems.TexGen.Stop(__tg);
		m.SetShaderParameter("tint", new Color(0.95f, 1f, 0.92f));
		m.SetShaderParameter("sway", 0.05f);
		m.SetShaderParameter("sway_speed", 0.7f);
		m.SetShaderParameter("normal_up", 0.3f);
		m.SetShaderParameter("alpha_cut", 0.5f);
		m.SetShaderParameter("mip_boost", 0.05f);   // (thin strands: boosted with distance they filled the card in)
		m.SetShaderParameter("tex_size", new Vector2(w, h));
		m.SetShaderParameter("back_shade", 0.2f);
		return m;
	}

	/// <summary>A beard of moss hanging from <paramref name="at"/>: two crossed cards, <paramref name="len"/> long.</summary>
	internal static void MossCard(MeshKit k, Vector3 at, float len, float width, RandomNumberGenerator rng)
	{
		var keep = k.CurrentMaterial;
		var col = k.Color;
		k.Mat(MossMat);
		float s = rng.RandfRange(0.85f, 1.1f);
		k.Color = new Color(s, s, s * 0.95f);
		float a = rng.RandfRange(0f, Mathf.Pi);
		for (int i = 0; i < 2; i++)
		{
			float ai = a + i * Mathf.Pi * 0.5f;
			Vector3 side = new Vector3(Mathf.Cos(ai), 0, Mathf.Sin(ai)) * width * 0.5f;
			Vector3 n = new Vector3(-side.Z, 0, side.X).Normalized();
			Vector3 bottom = at + Vector3.Down * len + new Vector3(rng.RandfRange(-0.08f, 0.08f), 0, rng.RandfRange(-0.08f, 0.08f));
			k.Quad(bottom - side * 0.8f, bottom + side * 0.8f, at + side, at - side, n,
				new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		}
		k.Color = col;
		if (keep != null) k.Mat(keep);
	}

	/// <summary>An understory sapling: a thin trunk pale with lichen, leaning and bending a little, a few twigs and a
	/// small, loose crown of leaves at the top.</summary>
	internal static Mesh SaplingMesh(int seed, float height)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 6007) };
		var k = new MeshKit();
		float r = rng.RandfRange(0.05f, 0.075f);
		Vector3 lean = new(rng.RandfRange(-0.4f, 0.4f), 0, rng.RandfRange(-0.4f, 0.4f));
		Vector3 Axis(float f) => new Vector3(0, f * height, 0) + lean * f * f + new Vector3(Mathf.Sin(f * 5f + seed) * 0.12f, 0, Mathf.Cos(f * 4f + seed) * 0.1f) * f;
		k.Mat(ProcTextures.TreeBarkMat);
		// the lichen: pale grey-green patches up the trunk, the bark dark between them
		var rings = new System.Collections.Generic.List<(Vector3, float, Color)>();
		for (int i = 0; i <= 7; i++)
		{
			float f = i / 7f;
			float lich = rng.Randf() < 0.55f ? rng.RandfRange(1.15f, 1.5f) : rng.RandfRange(0.62f, 0.8f);
			rings.Add((Axis(f) + (i == 0 ? Vector3.Down * 0.3f : Vector3.Zero), r * Mathf.Lerp(1.15f, 0.25f, f), new Color(0.62f * lich, 0.66f * lich, 0.58f * lich)));
		}
		TrunkLoft(k, rings, 6, 1f);
		// twigs, and leaves in loose clusters along their ends and round the top: green, a share yellowing
		var leafMat = BunkerParts.BunkerTextures.IvyMat;
		var tips = new System.Collections.Generic.List<(Vector3 at, Vector3 dir)>();
		for (int b = 0; b < 7; b++)
		{
			float f = rng.RandfRange(0.45f, 0.95f), a = rng.RandfRange(0, Mathf.Tau);
			Vector3 from = Axis(f), dir = new Vector3(Mathf.Cos(a), rng.RandfRange(0.3f, 0.9f), Mathf.Sin(a)).Normalized(), to = from + dir * rng.RandfRange(0.5f, 1.2f);
			k.Mat(ProcTextures.TreeBarkMat);
			k.Color = new Color(0.55f, 0.56f, 0.5f);
			k.Cylinder(from, to, r * 0.35f, 0.006f, 4, false);
			tips.Add((from.Lerp(to, 0.6f), dir));
			tips.Add((to, dir));
		}
		tips.Add((Axis(1f), Vector3.Up));
		k.Mat(leafMat);
		foreach (var (at, dir) in tips)
			for (int i = 0; i < 9; i++)
			{
				Vector3 p = at + new Vector3(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-0.2f, 0.25f), rng.RandfRange(-0.3f, 0.3f));
				Vector3 nrm = (Vector3.Up * 0.6f + new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-0.2f, 0.6f), rng.RandfRange(-1f, 1f))).Normalized();
				Vector3 t = (dir + new Vector3(rng.RandfRange(-0.6f, 0.6f), -0.4f, rng.RandfRange(-0.6f, 0.6f))).Normalized();
				t = (t - nrm * t.Dot(nrm)).Normalized();
				if (!t.IsFinite() || t.LengthSquared() < 0.5f) continue;
				Vector3 side = nrm.Cross(t).Normalized();
				float sz = rng.RandfRange(0.1f, 0.17f), hs = sz * 0.5f;
				float yel = rng.Randf() < 0.3f ? rng.RandfRange(0.4f, 0.9f) : 0f;
				float l = rng.RandfRange(0.85f, 1.15f);
				k.Color = new Color(l, l, l * 0.9f).Lerp(new Color(l * 2.2f, l * 1.6f, l * 0.4f), yel);
				int cell = rng.Randf() < 0.6f ? 3 : rng.Randf() < 0.5f ? 1 : 0;   // mostly the unlobed heart: a plain leaf
				float u0 = (cell % 2) * 0.5f, v0 = (cell / 2) * 0.5f;
				k.Quad(p - side * hs, p + side * hs, p + t * sz + side * hs, p + t * sz - side * hs, nrm,
					new Vector2(u0, v0 + 0.5f), new Vector2(u0 + 0.5f, v0 + 0.5f), new Vector2(u0 + 0.5f, v0), new Vector2(u0, v0));
			}
		return k.Commit();
	}

	/// <summary>A vine mound: whatever stood here (a shrub, a stump, a young tree) smothered under a shroud of ivy
	/// into a lumpy dome, leaves all over it hanging down its sides, a dark mass inside so it never reads hollow.</summary>
	private static StandardMaterial3D _moundCore;

	internal static Mesh VineMoundMesh(int seed, Vector3 size)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 9973) };
		var k = new MeshKit();
		// the dark core: a few overlapping lumps, the deep shade under the leaves
		k.Mat(_moundCore ??= new StandardMaterial3D { ResourceName = "vine_core", AlbedoColor = new Color(0.05f, 0.08f, 0.035f), Roughness = 1f, VertexColorUseAsAlbedo = true });
		k.Color = Colors.White;
		var lumps = new System.Collections.Generic.List<(Vector3 c, Vector3 r)>();
		for (int i = 0; i < 5; i++)
		{
			float a = rng.RandfRange(0, Mathf.Tau), d = i == 0 ? 0f : rng.RandfRange(0.25f, 0.5f);
			var c = new Vector3(Mathf.Cos(a) * size.X * d, size.Y * rng.RandfRange(0.3f, 0.5f) * (i == 0 ? 1.2f : 0.8f), Mathf.Sin(a) * size.Z * d);
			var r = new Vector3(size.X, size.Y, size.Z) * rng.RandfRange(0.45f, 0.62f) * (i == 0 ? 1.15f : 0.85f);
			lumps.Add((c, r));
			k.Blob(c, r * 0.9f, seed * 7 + i, 0.25f, false, 0.6f, 0.6f);
		}
		// the shroud: ivy leaves over every lump's skin, turned out from it, hanging a little
		var leafMat = BunkerParts.BunkerTextures.IvyMat;
		k.Mat(leafMat);
		int n = (int)(260 * size.X * size.Z) + 300;
		for (int i = 0; i < n; i++)
		{
			var (c, r) = lumps[rng.RandiRange(0, lumps.Count - 1)];
			// a point on the lump's upper skin
			float th = rng.RandfRange(0, Mathf.Tau), ph = Mathf.Acos(rng.RandfRange(-0.35f, 1f));
			Vector3 dir = new(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th));
			Vector3 at = c + new Vector3(dir.X * r.X, dir.Y * r.Y, dir.Z * r.Z);
			if (at.Y < 0.05f) continue;
			Vector3 nrm = new Vector3(dir.X / r.X, dir.Y / r.Y, dir.Z / r.Z).Normalized();
			nrm = (nrm + new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(-0.2f, 0.4f), rng.RandfRange(-0.4f, 0.4f))).Normalized();
			// the blade hangs down the slope (on the sides) or lies out (on top)
			Vector3 down = (Vector3.Down - nrm * Vector3.Down.Dot(nrm));
			Vector3 t = down.LengthSquared() > 0.01f ? down.Normalized() : new Vector3(1, 0, 0);
			t = t.Rotated(nrm, rng.RandfRange(-0.8f, 0.8f));
			Vector3 side = nrm.Cross(t).Normalized();
			float sz = rng.RandfRange(0.26f, 0.42f), hs = sz * 0.5f;
			Vector3 b0 = at - t * sz * 0.2f;
			Vector3 top = b0 + t * sz;
			int cell = rng.Randf() < 0.5f ? 0 : rng.Randf() < 0.6f ? 1 : rng.Randf() < 0.7f ? 3 : 2;
			float u0 = (cell % 2) * 0.5f, v0 = (cell / 2) * 0.5f;
			float l = rng.RandfRange(0.75f, 1.08f);
			k.Color = new Color(l, l * rng.RandfRange(0.95f, 1.08f), l * 0.9f);
			k.Quad(b0 - side * hs, b0 + side * hs, top + side * hs + nrm * 0.03f, top - side * hs + nrm * 0.03f, nrm,
				new Vector2(u0, v0 + 0.5f), new Vector2(u0 + 0.5f, v0 + 0.5f), new Vector2(u0 + 0.5f, v0), new Vector2(u0, v0));
		}
		return k.Commit();
	}

	private static StandardMaterial3D _palmMat;

	/// <summary>A saw palmetto (the owner's photo of the woods off the trail): a low clump of stems, each holding up a
	/// fan of stiff, narrow leaflets, the old ones gone brown and drooping.</summary>
	internal static Mesh PalmettoMesh(int seed)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 4421) };
		var k = new MeshKit();
		_palmMat ??= new StandardMaterial3D
		{
			ResourceName = "palmetto", AlbedoColor = new Color(0.3f, 0.38f, 0.2f), VertexColorUseAsAlbedo = true, Roughness = 0.7f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled, MetallicSpecular = 0.35f,
		};
		int fans = rng.RandiRange(6, 10);
		for (int f = 0; f < fans; f++)
		{
			float a = rng.RandfRange(0, Mathf.Tau), tilt = rng.RandfRange(0.25f, 0.95f);
			var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			bool dead = rng.Randf() < 0.2f;
			float stem = rng.RandfRange(0.5f, 1.0f) * (dead ? 0.8f : 1f);
			var top = o * Mathf.Sin(tilt) * stem + Vector3.Up * Mathf.Cos(tilt) * stem * (dead ? 0.6f : 1f);
			k.Mat(ProcTextures.BarkMat);
			k.Color = new Color(0.5f, 0.45f, 0.35f);
			k.Cylinder(Vector3.Zero, top, 0.02f, 0.012f, 4, false);
			// the fan: facing out and up from the stem's end, its leaflets radiating
			var n = (o * 0.6f + Vector3.Up * (dead ? -0.2f : 0.8f)).Normalized();
			var u = n.Cross(Vector3.Up).Normalized();
			if (u.LengthSquared() < 0.1f) u = Vector3.Right;
			var v = n.Cross(u).Normalized();
			k.Mat(_palmMat);
			var col = dead ? new Color(1.5f, 1.05f, 0.6f) : new Color(1f, 1f, 1f) * rng.RandfRange(0.85f, 1.2f);
			int leaflets = 15;
			float r = rng.RandfRange(0.45f, 0.65f);
			for (int i = 0; i < leaflets; i++)
			{
				float t = Mathf.Lerp(-1.45f, 1.45f, i / (leaflets - 1f));
				var dir = (u * Mathf.Sin(t) + v * Mathf.Cos(t)).Normalized();
				var side = n.Cross(dir).Normalized() * 0.022f;
				var tip = top + dir * r - n * r * 0.18f * Mathf.Abs(Mathf.Sin(t)) - Vector3.Up * (dead ? r * 0.4f : 0f);
				k.Color = col * (0.85f + 0.3f * ((i * 7) % 5) / 4f);
				k.Quad(top - side * 0.4f, top + side * 0.4f, tip + side * 0.3f, tip - side * 0.3f, n);
			}
		}
		return k.Commit();
	}

	/// <summary>A heap of old boards someone dumped in the woods (the owner's photo): weathered planks lying askew, a
	/// couple of rusted sheets of tin among them.</summary>
	internal static Mesh BoardPileMesh(int seed)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 3331) };
		var k = new MeshKit();
		float y = 0.03f;
		for (int i = 0; i < 9; i++)
		{
			bool tin = i % 4 == 3;
			k.Mat(tin ? ProcTextures.MetalMat : ProcTextures.WoodMat);
			k.Color = tin ? new Color(0.42f, 0.26f, 0.16f) : new Color(0.62f, 0.58f, 0.52f) * rng.RandfRange(0.7f, 1.05f);
			var size = tin ? new Vector3(1.6f, 0.01f, 0.7f) : new Vector3(rng.RandfRange(1.6f, 2.6f), 0.04f, rng.RandfRange(0.14f, 0.25f));
			var b = Basis.FromEuler(new Vector3(rng.RandfRange(-0.12f, 0.12f), rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(-0.1f, 0.1f)));
			k.Box(new Vector3(rng.RandfRange(-0.3f, 0.3f), y, rng.RandfRange(-0.3f, 0.3f)), size, 1f, b);
			y += tin ? 0.02f : rng.RandfRange(0.03f, 0.06f);
		}
		return k.Commit();
	}

	/// <summary>Three heaps of dumped boards a few metres off the trail's open stretch (solid).</summary>
	private void PlaceBoardPiles()
	{
		if (_terrain.TrailLength < 200f) return;
		foreach (var (sAt, off) in new[] { (62f, 7.5f), (104f, -8.5f), (151f, 9f) })
		{
			var p = _terrain.TrailPoint(sAt, out var tan);
			var right = tan.Cross(Vector3.Up).Normalized();
			var at = p + right * off;
			if (_terrain.RouteDistance(at.X, at.Z) < 4f || Cleared(new Vector2(at.X, at.Z), 1.5f, false)) continue;
			at.Y = _terrain.HeightAt(at.X, at.Z) - 0.02f;
			var b = new Basis(Vector3.Up, Mathf.Atan2(tan.X, tan.Z) + 0.4f);
			Add("boardpile", TreeChunk, at, b, Colors.White);
			AddCollider(at, BoxShape(new Vector3(1.2f, 0.25f, 0.5f)), new Transform3D(b, at + Vector3.Up * 0.2f));
		}
	}

	/// <summary>The understory: saplings between the trees, vine mounds along the woods' edges.</summary>
	private void ScatterUnderstory()
	{
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-understory") >= 0) return;   // (to compare, in the previews)
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 31 + 5) };
		Vector2 min = _terrain.MinXZ, max = _terrain.MaxXZ;
		const float c = 2.6f;
		for (float z = min.Y + c * 0.5f; z < max.Y; z += c)
			for (float x = min.X + c * 0.5f; x < max.X; x += c)
			{
				float px = x + rng.RandfRange(-0.45f, 0.45f) * c, pz = z + rng.RandfRange(-0.45f, 0.45f) * c;
				float roll = rng.Randf(), kind = rng.Randf();
				var p2 = new Vector2(px, pz);
				_terrain.SampleFields(px, pz, out float dT, out float sT, out float dS, out float dR);
				if (dT < 3.0f || dR < 4.5f || dS < 3.2f || ParkDist(p2) < 3.5f) continue;
				float cd = ClearingDist(p2);
				if (cd < 0.5f || Cleared(p2, 1.2f, false)) continue;
				float dB = _terrain.SampleBranch(px, pz, out float bHalf);
				if (bHalf > 0.08f && dB < bHalf + 2.2f) continue;
				float dA = _terrain.RouteDistance(px, pz);
				if (dA < 2.4f) continue;
				if (_terrain.NormalAt(px, pz).Y < 0.78f) continue;
				float clump = _clump.GetNoise2D(px * 1.7f, pz * 1.7f) * 0.5f + 0.5f;
				float deep = Deep(sT);
				float h = _terrain.HeightAt(px, pz);
				float yaw = rng.RandfRange(0, Mathf.Tau);
				// the mounds: on the woods' edge by the trail and round a clearing, where the light gets in
				float edge = Mathf.Max(1f - Mathf.SmoothStep(4f, 11f, dT), 1f - Mathf.SmoothStep(1f, 8f, cd));
				// the open woods (before the deep): saw palmettos in clumps under the trees, as the owner's photo
				if (deep < 0.5f && kind > 0.92f && roll < 0.3f + 0.4f * clump)
				{
					float ps = rng.RandfRange(0.8f, 1.3f);
					Add(rng.Randf() < 0.5f ? "palmetto_a" : "palmetto_b", FoliageChunk, new Vector3(px, h - 0.05f, pz),
						Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(Vector3.One * ps), Colors.White * rng.RandfRange(0.85f, 1.1f));
					continue;
				}
				if (kind < 0.22f)
				{
					if (dT < 4.2f || dA < 3.6f || roll > 0.12f + 0.32f * edge * clump) continue;
					string mesh = kind < 0.11f ? "vinemound_a" : "vinemound_b";
					float s = rng.RandfRange(0.8f, 1.25f);
					var basis = Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(new Vector3(s, s * rng.RandfRange(0.85f, 1.2f), s));
					var pos = new Vector3(px, Mathf.Min(h, Mathf.Min(_terrain.HeightAt(px + 1f, pz), _terrain.HeightAt(px - 1f, pz))) - 0.1f, pz);
					Add(mesh, TreeChunk, pos, basis, Colors.White * rng.RandfRange(0.85f, 1.05f));
					if (TreeCollision) AddCollider(pos, Cylinder(1.0f * s, 2.4f), new Transform3D(Basis.Identity, pos + Vector3.Up * 1.0f));
				}
				else
				{
					// saplings crowd in the clumps, thinner in the deep woods (the big trunks' shade)
					float p = (0.18f + 0.4f * clump) * (1f - 0.4f * deep);
					if (roll > p) continue;
					string mesh = kind < 0.48f ? "sapling_a" : kind < 0.74f ? "sapling_b" : "sapling_c";
					float s = rng.RandfRange(0.75f, 1.2f);
					var basis = Basis.FromEuler(new Vector3(rng.RandfRange(-0.04f, 0.04f), yaw, rng.RandfRange(-0.04f, 0.04f))).Scaled(Vector3.One * s);
					var pos = new Vector3(px, h - 0.05f, pz);
					float t = rng.RandfRange(0.85f, 1.08f);
					Add(mesh, TreeChunk, pos, basis, new Color(t, t, t * 0.97f));
					if (TreeCollision) AddCollider(pos, Cylinder(0.08f * s, 3f), new Transform3D(Basis.Identity, pos + Vector3.Up * 1.4f));
				}
			}
	}
}
