using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.LakeParts;

/// <summary>
/// Everything that grows or lies around the lake: a dense fir treeline ringing the whole bowl (so
/// the sky only shows above a real forest edge, never over a bare plane), birches and dead snags at
/// the water's edge, a few snags leaning right out over the water, reeds in the shallows, rocks
/// along the banks, a fallen log, and lily pads that rise and fall with the swell.
/// Reuses <see cref="ForestScatter"/>'s own tree, rock and grass meshes, so it is the same forest.
/// </summary>
public partial class LakeDressing : Node3D
{
	private const float Chunk = 48f;
	private readonly Dictionary<string, Mesh> _meshes = new();
	private readonly Dictionary<string, Dictionary<Vector2I, List<Transform3D>>> _inst = new();
	private readonly List<(Vector3 pos, float r, float h)> _trunks = new();
	private RandomNumberGenerator _rng;

	/// <summary>Lily pads: their MultiMesh and rest transforms, re-seated on the water every frame.</summary>
	private MultiMesh _pads;
	private readonly List<Transform3D> _padRest = new();

	public void Build(int seed)
	{
		_rng = new RandomNumberGenerator { Seed = (ulong)seed };
		_meshes["fir_giant"] = ForestScatter.FirMesh(11, 33f, 0.52f, 0.42f, 14, 4.3f, 0.10f, 0.6f);   // moss hanging off its dead limbs (2026-10-03, the owner's photo)
		_meshes["fir_tall"] = ForestScatter.FirMesh(12, 26f, 0.42f, 0.36f, 13, 3.7f, 0.12f, 0.45f);
		_meshes["fir_mid"] = ForestScatter.FirMesh(13, 18f, 0.31f, 0.26f, 11, 3.0f, 0.15f);
		_meshes["fir_spire"] = ForestScatter.FirMesh(14, 22f, 0.34f, 0.30f, 15, 2.4f, 0.08f);
		_meshes["fir_young"] = ForestScatter.FirMesh(15, 9f, 0.17f, 0.10f, 8, 2.0f, 0.05f);
		_meshes["decid_a"] = ForestScatter.DeciduousMesh(4);
		_meshes["decid_b"] = ForestScatter.DeciduousMesh(5);
		_meshes["snag"] = ForestScatter.SnagMesh(6, 9.5f, 0.7f);
		_meshes["snag_tall"] = ForestScatter.SnagMesh(16, 19f, 0.7f);
		_meshes["rock_a"] = ForestScatter.RockMesh(7);
		_meshes["rock_b"] = ForestScatter.RockMesh(8);
		_meshes["boulder"] = ForestScatter.BoulderMesh(21);
		_meshes["log"] = ForestScatter.LogMesh();
		_meshes["grass"] = ForestScatter.GrassMesh();
		for (int v = 0; v < ReedVariants; v++) _meshes[$"reed{v}"] = ReedMesh(v, false);

		ScatterTrees();
		ScatterShoreline();
		BuildShoreCharacter();
		PlaceFrogs();
		LeaningSnags();
		BuildPads();
		Commit();
		AddTrunkCollision();
	}

	// ------------------------------------------------------------------ trees

	/// <summary>True where nothing tall may stand: the two clearings, the beach-to-station path, the
	/// strip of shore behind the dock, and a sight line across the water to the station.</summary>
	private static bool KeepOpen(float x, float z, float margin)
	{
		var p = new Vector2(x, z);
		if (p.DistanceTo(LakeShape.NearClearing) < 15f + margin) return true;
		if (p.DistanceTo(LakeShape.StationSite) < 17f + margin) return true;
		if (Mathf.Abs(x) < 6f + margin && z < LakeShape.FarShoreZ + 2f && z > LakeShape.StationSite.Y) return true;
		return false;
	}

	private void ScatterTrees()
	{
		var clump = new FastNoiseLite { Seed = 1210, Frequency = 0.035f };
		const float cell = 4.1f;
		for (float z = LakeTerrain.MinZ + 4f; z < LakeTerrain.MaxZ - 4f; z += cell)
			for (float x = LakeTerrain.MinX + 4f; x < LakeTerrain.MaxX - 4f; x += cell)
			{
				float px = x + _rng.RandfRange(-1.6f, 1.6f), pz = z + _rng.RandfRange(-1.6f, 1.6f);
				float d = LakeShape.ShoreDist(px, pz);
				float edge = 7f + 3f * clump.GetNoise2D(px * 2f, pz * 2f);
				if (d < edge || d > 75f || KeepOpen(px, pz, 0f)) continue;
				// dense at the forest edge (it has to read as a wall of trees from the water), thinning far out where the fog has them
				float density = Mathf.Lerp(0.95f, 0.35f, Mathf.SmoothStep(30f, 75f, d)) * (0.65f + 0.35f * clump.GetNoise2D(px, pz) + 0.3f);
				if (_rng.Randf() > density) continue;
				string key = PickTree(d);
				float s = _rng.RandfRange(0.82f, 1.22f);
				var basis = new Basis(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau)).Scaled(new Vector3(s, s * _rng.RandfRange(0.92f, 1.1f), s));
				Vector3 pos = new(px, LakeShape.Ground(px, pz) - 0.2f, pz);
				Add(key, pos, basis);
				if (d < 40f) _trunks.Add((pos, key.StartsWith("fir") ? 0.4f * s : 0.25f, 6f));
			}
	}

	private string PickTree(float d)
	{
		float r = _rng.Randf();
		if (d < 14f)
		{
			// the water's edge: younger firs, pale birch-like broadleaves, the odd dead snag
			if (r < 0.22f) return _rng.Randf() < 0.5f ? "decid_a" : "decid_b";
			if (r < 0.34f) return "snag";
			if (r < 0.55f) return "fir_young";
			return r < 0.8f ? "fir_mid" : "fir_spire";
		}
		if (r < 0.05f) return "snag_tall";
		if (r < 0.28f) return "fir_mid";
		if (r < 0.5f) return "fir_spire";
		if (r < 0.85f) return "fir_tall";
		return "fir_giant";
	}

	// ------------------------------------------------------------------ the water's edge

	private void ScatterShoreline()
	{
		var clump = new FastNoiseLite { Seed = 1211, Frequency = 0.09f };
		for (int i = 0; i < 52000; i++)   // scaled with the doubled lake
		{
			float x = _rng.RandfRange(-LakeShape.SemiX - 12f, LakeShape.SemiX + 12f);
			float z = _rng.RandfRange(LakeShape.FarShoreZ - 12f, LakeShape.NearShoreZ + 12f);
			float d = LakeShape.ShoreDist(x, z);
			bool nearDock = Mathf.Abs(x) < 4.5f && z > LakeShape.DockEndZ - 3f && z < LakeShape.NearShoreZ + 8f;
			bool farLanding = Mathf.Abs(x) < 5f && z < LakeShape.FarShoreZ + 5f && z > LakeShape.FarShoreZ - 8f;
			float c = clump.GetNoise2D(x, z);
			float gy = LakeShape.Ground(x, z);
			// reeds: clumps standing in the shallows and on the wet mud
			if (d > -2.6f && d < 0.8f && c > 0.05f && !nearDock && !farLanding)
			{
				float s = _rng.RandfRange(0.8f, 1.3f);
				Add($"reed{ReedVariant(x, z)}", new Vector3(x, gy - 0.05f, z), new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(s, s * _rng.RandfRange(0.8f, 1.35f), s)));
				continue;
			}
			// grass tufts up the bank
			if (d > 0.5f && d < 12f && c > -0.2f && _rng.Randf() < 0.55f && !nearDock)
			{
				float s = _rng.RandfRange(0.8f, 1.5f);
				Add("grass", new Vector3(x, gy - 0.02f, z), new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Scaled(Vector3.One * s));
				continue;
			}
			// rocks along the banks, a few out in the shallows
			if (d > -3f && d < 9f && _rng.Randf() < 0.035f && !nearDock && !farLanding && !KeepOpen(x, z, -8f))
			{
				string key = _rng.Randf() < 0.2f ? "boulder" : (_rng.Randf() < 0.5f ? "rock_a" : "rock_b");
				float s = key == "boulder" ? _rng.RandfRange(0.9f, 1.6f) : _rng.RandfRange(0.5f, 1.4f);
				Add(key, new Vector3(x, gy - 0.25f * s, z), new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Scaled(Vector3.One * s));
			}
		}

		// A fallen fir half in the water off the near shore's left, the kind of thing you row past.
		Vector3 logAt = new(-11f, LakeShape.Ground(-11f, -2f) + 0.05f, -2f);
		Add("log", logAt, new Basis(Vector3.Up, 0.9f).Scaled(new Vector3(1.6f, 1.2f, 1.2f)) * new Basis(Vector3.Forward, -0.06f));
	}

	/// <summary>Dead trees that have tipped out over the water from the banks, bleached and bare:
	/// the silhouettes the player rows between.</summary>
	private void LeaningSnags()
	{
		float[] angles = { 0.9f, 1.35f, 2.2f, -1.05f, -1.7f, -2.45f };
		foreach (float th in angles)
		{
			// walk out from the centre along th until just onto land
			Vector2 dir = new(Mathf.Sin(th), Mathf.Cos(th));
			Vector2 p = new(0f, LakeShape.CenterZ);
			for (int i = 0; i < 200 && LakeShape.ShoreDist(p.X, p.Y) < 1.5f; i++) p += dir * 0.5f;
			Vector3 root = new(p.X, LakeShape.Ground(p.X, p.Y) - 0.3f, p.Y);
			// lean toward the water (toward the centre), 18-32 degrees
			Vector3 toward = new Vector3(-dir.X, 0f, -dir.Y).Normalized();
			Vector3 axis = toward.Cross(Vector3.Up).Normalized();
			var lean = new Basis(axis, -_rng.RandfRange(0.32f, 0.56f));
			float s = _rng.RandfRange(0.8f, 1.1f);
			Add(_rng.Randf() < 0.5f ? "snag_tall" : "snag", root, lean * new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Scaled(Vector3.One * s));
		}
	}

	// ------------------------------------------------------------------ lily pads

	private void BuildPads()
	{
		var k = new MeshKit();
		var mat = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.3f, 0.14f), Roughness = 0.45f, VertexColorUseAsAlbedo = true };
		k.Mat(mat);
		const int segs = 10;
		float notch = 0.5f;
		for (int i = 0; i < segs; i++)
		{
			float a0 = notch * 0.5f + (Mathf.Tau - notch) * i / segs, a1 = notch * 0.5f + (Mathf.Tau - notch) * (i + 1) / segs;
			k.Color = new Color(0.9f + 0.1f * (i % 2), 1f, 0.85f);
			k.Tri(Vector3.Zero, new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 0.34f, new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 0.34f,
				Vector3.Up, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1, 0));
		}
		var clump = new FastNoiseLite { Seed = 1212, Frequency = 0.12f };
		for (int i = 0; i < 12000 && _padRest.Count < 220; i++)
		{
			float x = _rng.RandfRange(-LakeShape.SemiX, LakeShape.SemiX), z = _rng.RandfRange(LakeShape.FarShoreZ, LakeShape.NearShoreZ);
			float d = LakeShape.ShoreDist(x, z);
			if (d > -1.2f || d < -6f || clump.GetNoise2D(x, z) < 0.25f) continue;
			if (Mathf.Abs(x) < 6f) continue;   // the boat's lanes stay clear
			float s = _rng.RandfRange(0.6f, 1.25f);
			_padRest.Add(new Transform3D(new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(s, 1f, s)), new Vector3(x, 0.03f, z)));
		}
		_pads = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = k.Commit(), InstanceCount = _padRest.Count };
		for (int i = 0; i < _padRest.Count; i++) _pads.SetInstanceTransform(i, _padRest[i]);
		AddChild(new MultiMeshInstance3D { Name = "LilyPads", Multimesh = _pads, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
	}

	/// <summary>Re-seats every lily pad on the current swell (only worth doing once the water moves).</summary>
	public void RideWaves(in LakeShape.Waves w)
	{
		if (_pads == null) return;
		for (int i = 0; i < _padRest.Count; i++)
		{
			var rest = _padRest[i];
			float h = LakeShape.WaveHeight(w, rest.Origin.X, rest.Origin.Z, out Vector2 slope);
			_pads.SetInstanceTransform(i, new Transform3D(LakeShape.SurfaceTilt(slope) * rest.Basis, rest.Origin + Vector3.Up * h));
		}
	}

	// ------------------------------------------------------------------ meshes, commit, collision

	/// <summary>How many shapes of each reed clump there are (picked by place, not by the scatter's dice).</summary>
	public const int ReedVariants = 3;

	/// <summary>Which of the clump shapes stands at (x, z).</summary>
	public static int ReedVariant(float x, float z) => (int)(Mathf.Abs(Mathf.Sin(x * 12.9898f + z * 78.233f) * 43758.547f) % 1f * ReedVariants) % ReedVariants;

	private static readonly Dictionary<bool, Texture2D> _reedTex = new();

	/// <summary>
	/// A reed bed's blades, drawn (2026-10-15, the scan: the reeds were the grass tuft stretched four times upward, black
	/// cut-outs with a jagged, cactus-like edge, one clump everywhere): 128 x 512, thirty-odd thin tapered blades rising
	/// from the foot, each its own height, lean and curve, olive green going to straw at the tips, some dead and pale all
	/// through, a midrib down the wider ones, their edges anti-aliased; dark at the foot where they stand in the wet. The
	/// short reeds carry a few bulrush heads, the tall ones feathery seed plumes. y = 0 is the tips (the shader sways by it).
	/// </summary>
	private static Texture2D ReedTex(bool tall)
	{
		if (_reedTex.TryGetValue(tall, out var t)) return t;
		const int W = 128, H = 512;
		var px = new Color[W * H];
		var rng = new RandomNumberGenerator { Seed = tall ? 1225u : 1224u };
		void Over(int x, int y, Color c, float a)
		{
			if (x < 0 || x >= W || y < 0 || y >= H || a <= 0.003f) return;
			ref var d = ref px[y * W + x];
			float oa = a + d.A * (1f - a);
			if (oa <= 0f) return;
			d = new Color((c.R * a + d.R * d.A * (1f - a)) / oa, (c.G * a + d.G * d.A * (1f - a)) / oa, (c.B * a + d.B * d.A * (1f - a)) / oa, oa);
		}
		int blades = tall ? 30 : 36;
		var heads = new List<(float x, float y, float lean)>();
		for (int b = 0; b < blades; b++)
		{
			float x0 = rng.RandfRange(10f, W - 10f);
			float top = H * (1f - rng.RandfRange(tall ? 0.55f : 0.4f, 0.98f));   // the tip's row
			float lean = rng.RandfRange(-22f, 22f), bow = rng.RandfRange(-10f, 10f);
			float w0 = rng.RandfRange(1.3f, tall ? 2.6f : 3.2f);
			bool dead = rng.Randf() < 0.28f;
			var green = new Color(0.3f, 0.36f, 0.14f).Lerp(new Color(0.4f, 0.42f, 0.18f), rng.Randf());
			var straw = new Color(0.5f, 0.45f, 0.29f) * rng.RandfRange(0.8f, 1.05f);
			var foot = new Color(0.16f, 0.15f, 0.08f);
			float dim = rng.RandfRange(0.75f, 1.05f);   // the ones behind a little darker
			for (int y = (int)top; y < H; y++)
			{
				float u = (y - top) / (H - top);            // 0 at the tip .. 1 at the foot
				float cx = x0 + lean * (1f - u) * (1f - u) + bow * Mathf.Sin(u * Mathf.Pi);
				float hw = w0 * Mathf.Pow(Mathf.Clamp(u * 1.6f, 0f, 1f), 0.6f) * (0.55f + 0.45f * u);
				Color c = dead ? straw.Lerp(foot, Mathf.SmoothStep(0.75f, 1f, u) * 0.6f)
					: straw.Lerp(green, Mathf.SmoothStep(0.02f, 0.22f, u)).Lerp(foot, Mathf.SmoothStep(0.8f, 1f, u) * 0.7f);
				c *= dim;
				for (int x = (int)(cx - hw - 1.5f); x <= (int)(cx + hw + 1.5f); x++)
				{
					float dx = Mathf.Abs(x + 0.5f - cx);
					float a = Mathf.Clamp(hw + 0.5f - dx, 0f, 1f);
					// a lighter midrib down the wide ones, the edges a shade darker
					var cc = hw > 1.6f && dx < 0.6f ? c * 1.15f : (dx > hw - 0.7f ? c * 0.85f : c);
					Over(x, y, cc, a);
				}
			}
			if (b % (tall ? 4 : 7) == 0) heads.Add((x0 + lean, top, lean));
		}
		foreach (var (hx, hy, lean) in heads)
		{
			if (tall)
			{
				// a feathery plume: a loose, drooping spray of fine light-brown seed hairs over the stem's tip
				// (hairs, not dots: each a short drooping stroke off the stem's last hand-span)
				float side = lean >= 0f ? 1f : -1f;
				for (int s = 0; s < 26; s++)
				{
					float a = rng.RandfRange(0f, 1f);
					float sx = hx + side * a * 6f, sy = hy - 6f + a * 40f;
					float len = rng.RandfRange(8f, 18f), ang = rng.RandfRange(0.6f, 1.3f);
					var col = new Color(0.4f, 0.33f, 0.27f) * rng.RandfRange(0.8f, 1.1f);
					for (float q = 0f; q < len; q += 0.5f)
					{
						float qx = sx + side * Mathf.Cos(ang) * q, qy = sy + Mathf.Sin(ang) * q + q * q * 0.02f;
						Over((int)qx, (int)qy, col, 0.75f * (1f - q / len * 0.5f));
					}
				}
			}
			else
			{
				// a bulrush head: a dark brown velvet cylinder a little under the tip, the bare spike above it
				float y0 = hy + 26f, y1 = y0 + rng.RandfRange(26f, 38f);
				for (int y = (int)y0; y < (int)y1; y++)
				{
					float u = (y - y0) / (y1 - y0), hw = 3.4f * Mathf.Sqrt(Mathf.Sin(u * Mathf.Pi));
					float cx = hx - lean * ((y - hy) / H) * 2f;
					for (int x = (int)(cx - hw - 1f); x <= (int)(cx + hw + 1f); x++)
						Over(x, y, new Color(0.24f, 0.15f, 0.08f) * (0.85f + 0.3f * (1f - Mathf.Abs(x + 0.5f - cx) / (hw + 0.5f))), Mathf.Clamp(hw + 0.5f - Mathf.Abs(x + 0.5f - cx), 0f, 1f));
				}
			}
		}
		var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
		for (int y = 0; y < H; y++)
			for (int x = 0; x < W; x++)
			{
				var c = px[y * W + x];
				// the colour bled out under the transparent edge, so the mips don't fringe dark
				img.SetPixel(x, y, c.A > 0f ? c : new Color(0.3f, 0.32f, 0.16f, 0f));
			}
		img.GenerateMipmaps();
		return _reedTex[tall] = ImageTexture.CreateFromImage(img);
	}

	/// <summary>A clump of reed (<paramref name="tall"/>: a bed's tall common reed): five to eight cards of the drawn blades
	/// (<see cref="ReedTex"/>) crossed at uneven angles round the root, each its own width, height, lean and slice of the
	/// texture, so no two clumps (and no two of a clump's faces) read the same. Lit from above, glowing a little when the
	/// sky is behind them instead of going black.</summary>
	public static Mesh ReedMesh(int variant, bool tall)
	{
		var k = new MeshKit();
		var mat = (ShaderMaterial)ProcTextures.Cached(tall ? "lake_reed_tall2" : "lake_reed2", () =>
		{
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
			DetailKit.Hook(m, DetailKit.Kind.Foliage);
			m.SetShaderParameter("albedo_tex", ReedTex(tall));
			m.SetShaderParameter("tex_size", new Vector2(128f, 512f));
			m.SetShaderParameter("tint", new Color(0.92f, 0.95f, 0.85f));
			m.SetShaderParameter("sway", tall ? 0.14f : 0.09f);
			m.SetShaderParameter("sway_speed", tall ? 0.8f : 0.9f);
			m.SetShaderParameter("normal_up", 0.45f);
			m.SetShaderParameter("alpha_cut", 0.4f);
			m.SetShaderParameter("translucency", 0.25f);
			m.SetShaderParameter("inner_ao", 0.35f);
			return m;
		});
		k.Mat(mat);
		var rng = new RandomNumberGenerator { Seed = (ulong)(1240 + variant * 17 + (tall ? 500 : 0)) };
		int cards = rng.RandiRange(5, 8);
		float a = rng.RandfRange(0f, Mathf.Pi);
		for (int i = 0; i < cards; i++)
		{
			a += Mathf.Pi / cards * rng.RandfRange(0.5f, 1.5f);
			float half = (tall ? 0.42f : 0.28f) * rng.RandfRange(0.65f, 1.25f);
			float h = (tall ? 2.6f : 1.45f) * rng.RandfRange(0.7f, 1.15f);
			var off = new Vector3(rng.RandfRange(-0.12f, 0.12f), 0, rng.RandfRange(-0.12f, 0.12f)) * (tall ? 1.6f : 1f);
			Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * half;
			Vector3 n = new Vector3(-d.Z, 0f, d.X).Normalized();
			Vector3 leanTo = n * rng.RandfRange(-0.18f, 0.18f) * h;
			// a slice of the texture's width, at its own place
			float uw = rng.RandfRange(0.45f, 0.8f), u0 = rng.RandfRange(0f, 1f - uw);
			bool flip = rng.Randf() < 0.5f;
			float ua = flip ? u0 + uw : u0, ub = flip ? u0 : u0 + uw;
			float l = rng.RandfRange(0.82f, 1.12f);
			k.Color = new Color(l, l, l * rng.RandfRange(0.92f, 1.05f));
			Vector3 top = Vector3.Up * h + leanTo;
			k.Quad(off - d, off + d, off + d * 1.25f + top, off - d * 1.25f + top, (n + Vector3.Up * 0.3f).Normalized(),
				new Vector2(ua, 1), new Vector2(ub, 1), new Vector2(ub, 0), new Vector2(ua, 0));
		}
		return k.Commit();
	}

	private void Add(string mesh, Vector3 pos, Basis basis)
	{
		if (!_inst.TryGetValue(mesh, out var byChunk)) _inst[mesh] = byChunk = new();
		var key = new Vector2I(Mathf.FloorToInt(pos.X / Chunk), Mathf.FloorToInt(pos.Z / Chunk));
		if (!byChunk.TryGetValue(key, out var list)) byChunk[key] = list = new();
		list.Add(new Transform3D(basis, pos));
	}

	private void Commit()
	{
		foreach (var (meshKey, byChunk) in _inst)
		{
			bool small = meshKey is "grass" or "wrack" or "marsh_mat" or "drift" or "rootwad" || (meshKey.StartsWith("reed") && !meshKey.StartsWith("reed_tall"));
			foreach (var (key, list) in byChunk)
			{
				var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _meshes[meshKey], InstanceCount = list.Count };
				for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
				AddChild(new MultiMeshInstance3D
				{
					Name = $"{meshKey}_{key.X}_{key.Y}",
					Multimesh = mm,
					VisibilityRangeEnd = small ? 70f : 340f,
					CastShadow = small ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
				});
			}
		}
	}

	/// <summary>Trunks near the two walkable areas get real collision (the rest are behind the fences).</summary>
	private void AddTrunkCollision()
	{
		var body = new StaticBody3D { Name = "TrunkBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		foreach (var (pos, r, h) in _trunks)
		{
			var p2 = new Vector2(pos.X, pos.Z);
			if (p2.DistanceTo(LakeShape.NearClearing) > 26f && p2.DistanceTo(LakeShape.StationSite) > 30f) continue;
			body.AddChild(new CollisionShape3D { Position = pos + Vector3.Up * h * 0.5f, Shape = new CylinderShape3D { Radius = r, Height = h } });
		}
	}

	/// <summary>(2026-10-10) Frogs along the near shore, either side of the beach (see <see cref="FrogChorus"/>).</summary>
	private void PlaceFrogs()
	{
		foreach (float x in new[] { -34f, -15f, 13f, 31f })
		{
			// down from the land to the water's edge
			float z = 30f;
			while (z > -40f && LakeShape.ShoreDist(x, z) > 0.6f) z -= 0.5f;
			if (z <= -40f) continue;
			var f = new FrogChorus { Name = $"Frogs{x:0}", Db = -11f };
			AddChild(f);
			f.Position = new Vector3(x, LakeShape.Ground(x, z) + 0.3f, z);
		}
	}
}
