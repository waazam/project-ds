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
		_meshes["fir_giant"] = ForestScatter.FirMesh(11, 33f, 0.52f, 0.42f, 14, 4.3f, 0.10f);
		_meshes["fir_tall"] = ForestScatter.FirMesh(12, 26f, 0.42f, 0.36f, 13, 3.7f, 0.12f);
		_meshes["fir_mid"] = ForestScatter.FirMesh(13, 18f, 0.31f, 0.26f, 11, 3.0f, 0.15f);
		_meshes["fir_spire"] = ForestScatter.FirMesh(14, 22f, 0.34f, 0.30f, 15, 2.4f, 0.08f);
		_meshes["fir_young"] = ForestScatter.FirMesh(15, 9f, 0.17f, 0.10f, 8, 2.0f, 0.05f);
		_meshes["decid_a"] = ForestScatter.DeciduousMesh(4);
		_meshes["decid_b"] = ForestScatter.DeciduousMesh(5);
		_meshes["snag"] = ForestScatter.SnagMesh(6, 9.5f);
		_meshes["snag_tall"] = ForestScatter.SnagMesh(16, 19f);
		_meshes["rock_a"] = ForestScatter.RockMesh(7);
		_meshes["rock_b"] = ForestScatter.RockMesh(8);
		_meshes["boulder"] = ForestScatter.BoulderMesh(21);
		_meshes["log"] = ForestScatter.LogMesh();
		_meshes["grass"] = ForestScatter.GrassMesh();
		_meshes["reed"] = ReedMesh();

		ScatterTrees();
		ScatterShoreline();
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
				Add("reed", new Vector3(x, gy - 0.05f, z), new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(s, s * _rng.RandfRange(0.8f, 1.35f), s)));
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

	/// <summary>Tall, thin reed/cattail cards (the grass texture stretched upward), a few seed heads.</summary>
	private static Mesh ReedMesh()
	{
		var k = new MeshKit();
		var mat = (ShaderMaterial)ProcTextures.Cached("lake_reed", () =>
		{
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
			m.SetShaderParameter("albedo_tex", ProcTextures.GrassTuft());
			m.SetShaderParameter("tint", new Color(0.78f, 0.74f, 0.52f));
			m.SetShaderParameter("sway", 0.09f);
			m.SetShaderParameter("sway_speed", 0.9f);
			return m;
		});
		k.Mat(mat);
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Pi * i / 4f + 0.2f;
			Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.3f;
			Vector3 n = new Vector3(-d.Z, 0.4f, d.X).Normalized();
			float h = 1.3f + 0.25f * (i % 2);
			k.Quad(-d, d, d * 1.3f + new Vector3(0, h, 0), -d * 1.3f + new Vector3(0, h, 0), n,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		}
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.35f, 0.24f, 0.16f);
		foreach (var p in new[] { new Vector3(0.08f, 0, 0.05f), new Vector3(-0.12f, 0, -0.06f) })
		{
			k.Cylinder(p, p + new Vector3(0.02f, 1.35f, 0), 0.008f, 0.006f, 3, false);
			k.Cylinder(p + new Vector3(0.02f, 1.35f, 0), p + new Vector3(0.025f, 1.58f, 0), 0.028f, 0.024f, 5, true);
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
			bool small = meshKey is "grass" or "reed";
			foreach (var (key, list) in byChunk)
			{
				var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _meshes[meshKey], InstanceCount = list.Count };
				for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
				AddChild(new MultiMeshInstance3D
				{
					Name = $"{meshKey}_{key.X}_{key.Y}",
					Multimesh = mm,
					VisibilityRangeEnd = small ? 70f : 260f,
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
}
