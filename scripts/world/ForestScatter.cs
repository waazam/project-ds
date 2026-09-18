using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Procedural forest dressing over the ForestTerrain: conifers, deciduous
/// trees, dead snags, stumps, rocks, fallen logs/branches, ferns and grass.
/// Everything is MultiMesh, chunked for culling, with trunk/rock/log
/// collision on per-chunk physics-server bodies. Deterministic from Seed.
///
/// Respects ClearZone nodes (group "clear_zones"). Builds deferred, after all
/// landmarks have placed themselves.
/// </summary>
[GlobalClass]
public partial class ForestScatter : Node3D
{
	[Export] public int Seed = 77;
	[Export] public float TreeCell = 3.1f;
	[Export] public float TreeChunk = 40f;
	[Export] public float FoliageChunk = 24f;
	[Export] public float TreeViewDistance = 150f;
	[Export] public float FoliageViewDistance = 42f;
	/// <summary>Trail arc length where the woods get deeper/darker.</summary>
	[Export] public float DeepStart = 175f;
	[Export] public float DeepEnd = 235f;
	[Export] public bool TreeCollision = true;

	private ForestTerrain _terrain;
	private readonly List<(Vector2 p, float r, bool foliage)> _clear = new();
	private FastNoiseLite _clump;
	private RandomNumberGenerator _rng;
	private readonly List<Rid> _bodies = new();
	private readonly Dictionary<string, Rid> _shapes = new();

	// per mesh key → per chunk → transforms
	private readonly Dictionary<string, Dictionary<Vector2I, List<(Transform3D xf, Color c)>>> _inst = new();
	private readonly Dictionary<string, Mesh> _meshes = new();
	private readonly Dictionary<Vector2I, List<(Rid shape, Transform3D xf)>> _colliders = new();

	public int TreeCount { get; private set; }

	public override void _Ready() => CallDeferred(MethodName.Build);

	public override void _ExitTree()
	{
		foreach (var b in _bodies) PhysicsServer3D.FreeRid(b);
		foreach (var s in _shapes.Values) PhysicsServer3D.FreeRid(s);
		_bodies.Clear(); _shapes.Clear();
	}

	private void Build()
	{
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (_terrain == null) { GD.PushWarning("ForestScatter: no terrain in group 'terrain'"); return; }
		_rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		_clump = new FastNoiseLite { Seed = Seed, Frequency = 0.045f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FractalOctaves = 2 };
		foreach (var n in GetTree().GetNodesInGroup("clear_zones"))
			if (n is ClearZone cz)
				_clear.Add((new Vector2(cz.GlobalPosition.X, cz.GlobalPosition.Z), cz.Radius, cz.ClearFoliage));

		BuildMeshes();
		ScatterTrees();
		ScatterRocksAndLogs();
		ScatterFoliage();
		Commit();
	}

	// =================================================================== meshes

	private void BuildMeshes()
	{
		_meshes["conifer_a"] = ConiferMesh(1, 7, 13.5f, 2.5f);
		_meshes["conifer_b"] = ConiferMesh(2, 6, 10f, 2.2f);
		_meshes["conifer_c"] = ConiferMesh(3, 8, 17f, 2.8f);
		_meshes["decid_a"] = DeciduousMesh(4);
		_meshes["decid_b"] = DeciduousMesh(5);
		_meshes["snag"] = SnagMesh(6);
		_meshes["stump"] = StumpMesh();
		_meshes["rock_a"] = RockMesh(7);
		_meshes["rock_b"] = RockMesh(8);
		_meshes["log"] = LogMesh();
		_meshes["branch"] = BranchMesh();
		_meshes["fern"] = FernMesh();
		_meshes["grass"] = GrassMesh();
	}

	private static Mesh ConiferMesh(int seed, int tiers, float height, float radius)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919) };
		var k = new MeshKit();
		k.Color = new Color(0.9f, 0.88f, 0.85f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0, -0.4f, 0), new Vector3(0, height * 0.85f, 0), 0.26f, 0.05f, 6, false, 1f);
		k.Mat(ProcTextures.NeedleMat);
		float firstY = 1.6f + rng.RandfRange(0, 0.8f);
		float span = height - firstY;
		for (int t = 0; t < tiers; t++)
		{
			float f = (float)t / (tiers - 1);
			float y0 = firstY + span * f * 0.86f;
			float th = Mathf.Lerp(2.7f, 1.4f, f) * (height / 13f);
			float r = Mathf.Lerp(radius, 0.45f, Mathf.Pow(f, 0.9f)) * rng.RandfRange(0.9f, 1.1f);
			Vector3 apex = new(rng.RandfRange(-0.08f, 0.08f), y0 + th, rng.RandfRange(-0.08f, 0.08f));
			Vector3 under = new(0, y0 + th * 0.28f, 0);
			int sides = 9;
			float rot = rng.RandfRange(0, Mathf.Tau);
			var ring = new Vector3[sides];
			for (int s = 0; s < sides; s++)
			{
				float a = rot + Mathf.Tau * s / sides;
				float rr = r * (s % 2 == 0 ? 1f : 0.74f) * rng.RandfRange(0.9f, 1.1f);
				float droop = (s % 2 == 0 ? -0.35f : -0.1f) * (height / 13f);
				ring[s] = new Vector3(Mathf.Cos(a) * rr, y0 + droop, Mathf.Sin(a) * rr);
			}
			float lo = Mathf.Lerp(0.42f, 0.7f, f), hi = Mathf.Lerp(0.85f, 1.05f, f);
			for (int s = 0; s < sides; s++)
			{
				Vector3 a = ring[s], b = ring[(s + 1) % sides];
				Vector3 na = new Vector3(a.X, r * 0.9f, a.Z).Normalized(), nb = new Vector3(b.X, r * 0.9f, b.Z).Normalized();
				Vector3 nApex = Vector3.Up;
				float circ = Mathf.Tau * r;
				Vector2 ua = new((float)s / sides * circ * 0.5f, th * 0.5f), ub = new((float)(s + 1) / sides * circ * 0.5f, th * 0.5f);
				Vector2 uApex = new((s + 0.5f) / sides * circ * 0.5f, 0);
				// outer skirt: dark at the ring, lighter toward the apex (baked occlusion)
				k.Color = new Color(lo, lo, lo);
				k.Tri(a, b, apex, na, nb, nApex, ua, ub, uApex);
				// underside: very dark, faces down/in
				k.Color = new Color(lo * 0.45f, lo * 0.45f, lo * 0.45f);
				Vector3 nd = new Vector3(0, -1, 0);
				k.Tri(a, b, under, nd, nd, nd, ua, ub, uApex);
			}
			k.Color = new Color(hi, hi, hi);
		}
		return k.Commit();
	}

	private static Mesh DeciduousMesh(int seed)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 104729) };
		var k = new MeshKit();
		k.Color = new Color(0.75f, 0.72f, 0.68f);
		float lean = rng.RandfRange(-0.25f, 0.25f);
		Vector3 top = new(lean, 4.2f, rng.RandfRange(-0.2f, 0.2f));
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0, -0.4f, 0), top, 0.23f, 0.13f, 6, false, 1f);
		for (int b = 0; b < 3; b++)
		{
			float a = rng.RandfRange(0, Mathf.Tau);
			Vector3 from = top * rng.RandfRange(0.55f, 0.9f);
			Vector3 to = from + new Vector3(Mathf.Cos(a) * 1.6f, rng.RandfRange(1.0f, 1.8f), Mathf.Sin(a) * 1.6f);
			k.Cylinder(from, to, 0.09f, 0.04f, 5, false, 1f);
		}
		k.Cylinder(top, top + new Vector3(0, 1.6f, 0), 0.13f, 0.05f, 5, false);
		k.Mat(ProcTextures.LeafMat);
		int blobs = 5;
		for (int i = 0; i < blobs; i++)
		{
			float a = Mathf.Tau * i / blobs + rng.RandfRange(-0.3f, 0.3f);
			float d = i == 0 ? 0 : rng.RandfRange(1.0f, 1.7f);
			Vector3 c = top + new Vector3(Mathf.Cos(a) * d, (i == 0 ? 1.9f : rng.RandfRange(0.4f, 1.6f)), Mathf.Sin(a) * d);
			float s = rng.RandfRange(0.75f, 1.0f);
			k.Color = new Color(s, s, s * 0.95f);
			k.Blob(c, new Vector3(1.7f, 1.3f, 1.7f) * rng.RandfRange(0.8f, 1.15f), seed * 13 + i, 0.3f, false, 0.6f, 0.6f);
		}
		return k.Commit();
	}

	private static Mesh SnagMesh(int seed)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 31337) };
		var k = new MeshKit();
		k.Color = new Color(0.62f, 0.62f, 0.6f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0, -0.4f, 0), new Vector3(0.2f, 9.5f, 0.1f), 0.24f, 0.04f, 6, false, 1f);
		for (int b = 0; b < 6; b++)
		{
			float y = rng.RandfRange(3f, 8.5f);
			float a = rng.RandfRange(0, Mathf.Tau);
			float len = rng.RandfRange(0.5f, 1.6f) * (1.2f - y / 10f);
			Vector3 from = new(0.2f * y / 9.5f, y, 0.1f * y / 9.5f);
			k.Cylinder(from, from + new Vector3(Mathf.Cos(a) * len, rng.RandfRange(-0.3f, 0.3f), Mathf.Sin(a) * len), 0.05f, 0.012f, 4, false);
		}
		return k.Commit();
	}

	private static Mesh StumpMesh()
	{
		var k = new MeshKit();
		k.Color = new Color(0.8f, 0.78f, 0.74f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0, -0.3f, 0), new Vector3(0, 0.45f, 0), 0.34f, 0.28f, 7, false, 1f);
		k.Color = new Color(0.7f, 0.66f, 0.6f);
		k.Mat(ProcTextures.EndGrainMat).Cylinder(new Vector3(0, 0.44f, 0), new Vector3(0, 0.45f, 0), 0.28f, 0.28f, 7, true, 1.7f);
		return k.Commit();
	}

	private static Mesh RockMesh(int seed)
	{
		var k = new MeshKit();
		k.Color = new Color(0.85f, 0.85f, 0.82f);
		k.Mat(ProcTextures.RockMat).Blob(Vector3.Zero, new Vector3(1f, 0.62f, 0.85f), seed, 0.26f, true, 0.7f);
		return k.Commit();
	}

	private static Mesh LogMesh()
	{
		var k = new MeshKit();
		k.Color = new Color(0.62f, 0.6f, 0.55f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(-2.6f, 0, 0), new Vector3(2.6f, 0, 0.1f), 0.27f, 0.2f, 7, false, 1f);
		k.Mat(ProcTextures.EndGrainMat).Cylinder(new Vector3(-2.62f, 0, 0), new Vector3(-2.6f, 0, 0), 0.265f, 0.265f, 7, true, 1.8f);
		k.Cylinder(new Vector3(2.6f, 0, 0.1f), new Vector3(2.62f, 0, 0.1f), 0.2f, 0.2f, 7, true, 2.3f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0.5f, 0.1f, 0.05f), new Vector3(0.9f, 0.3f, 0.9f), 0.06f, 0.02f, 4, false);
		return k.Commit();
	}

	private static Mesh BranchMesh()
	{
		var k = new MeshKit();
		k.Color = new Color(0.55f, 0.52f, 0.48f);
		k.Mat(ProcTextures.BarkMat);
		k.Cylinder(new Vector3(-0.9f, 0.04f, 0), new Vector3(0.9f, 0.04f, 0.1f), 0.045f, 0.02f, 4, false, 2f);
		k.Cylinder(new Vector3(0.1f, 0.04f, 0.04f), new Vector3(0.7f, 0.08f, 0.5f), 0.025f, 0.01f, 4, false, 2f);
		k.Cylinder(new Vector3(-0.4f, 0.04f, 0.01f), new Vector3(-0.8f, 0.05f, -0.35f), 0.02f, 0.008f, 4, false, 2f);
		return k.Commit();
	}

	private static ShaderMaterial FoliageMat(string key, Texture2D tex, Color tint, float sway)
		=> (ShaderMaterial)ProcTextures.Cached(key, () =>
		{
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
			m.SetShaderParameter("albedo_tex", tex);
			m.SetShaderParameter("tint", tint);
			m.SetShaderParameter("sway", sway);
			return m;
		});

	private static Mesh FernMesh()
	{
		var k = new MeshKit();
		k.Mat(FoliageMat("fern_mat", ProcTextures.Fern(), new Color(0.95f, 1f, 0.9f), 0.04f));
		int fronds = 7;
		for (int i = 0; i < fronds; i++)
		{
			float a = Mathf.Tau * i / fronds + (i % 2) * 0.3f;
			Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 side = new Vector3(-dir.Z, 0, dir.X) * 0.17f;
			float len = 0.75f + (i % 3) * 0.12f;
			Vector3 root = dir * 0.05f + new Vector3(0, 0.02f, 0);
			Vector3 mid = dir * len * 0.5f + new Vector3(0, 0.42f, 0);
			Vector3 tip = dir * len + new Vector3(0, 0.18f, 0);
			k.Color = new Color(0.8f, 0.8f, 0.8f);
			// root half (v 1 → 0.5), tip half (v 0.5 → 0)
			k.Quad(root - side * 0.4f, root + side * 0.4f, mid + side, mid - side, Vector3.Up,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(0, 0.5f));
			k.Color = Colors.White;
			k.Quad(mid - side, mid + side, tip + side * 0.3f, tip - side * 0.3f, Vector3.Up,
				new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0), new Vector2(0, 0));
		}
		return k.Commit();
	}

	private static Mesh GrassMesh()
	{
		var k = new MeshKit();
		k.Mat(FoliageMat("grass_mat", ProcTextures.GrassTuft(), new Color(0.95f, 0.95f, 0.85f), 0.07f));
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Pi * i / 3f;
			Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.38f;
			Vector3 n = new Vector3(-d.Z, 0.6f, d.X).Normalized();
			k.Quad(-d, d, d + new Vector3(0, 0.6f, 0), -d + new Vector3(0, 0.6f, 0), n,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		}
		return k.Commit();
	}

	// =================================================================== scatter

	private bool Cleared(Vector2 p, float extra, bool foliage)
	{
		foreach (var c in _clear)
		{
			if (foliage && !c.foliage) continue;
			if (p.DistanceSquaredTo(c.p) < (c.r + extra) * (c.r + extra)) return true;
		}
		return false;
	}

	private float Deep(float sT) => Mathf.SmoothStep(DeepStart, DeepEnd, sT);

	private float ClearingDist(Vector2 p) => p.DistanceTo(_terrain.ClearingCenter) - _terrain.ClearingRadius;

	private float ParkDist(Vector2 p)
	{
		var r = _terrain.ParkingRect;
		float dx = Mathf.Max(Mathf.Max(r.Position.X - p.X, p.X - r.End.X), 0);
		float dz = Mathf.Max(Mathf.Max(r.Position.Y - p.Y, p.Y - r.End.Y), 0);
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	private void Add(string mesh, float chunk, Vector3 pos, Basis basis, Color tint)
	{
		if (!_inst.TryGetValue(mesh, out var byChunk)) _inst[mesh] = byChunk = new();
		var key = new Vector2I(Mathf.FloorToInt(pos.X / chunk), Mathf.FloorToInt(pos.Z / chunk));
		if (!byChunk.TryGetValue(key, out var list)) byChunk[key] = list = new();
		list.Add((new Transform3D(basis, pos), tint));
	}

	private Rid Shape(string key, System.Func<Rid> make)
	{
		if (_shapes.TryGetValue(key, out var r)) return r;
		r = make();
		_shapes[key] = r;
		return r;
	}

	private void AddCollider(Vector3 pos, Rid shape, Transform3D xf)
	{
		var key = new Vector2I(Mathf.FloorToInt(pos.X / TreeChunk), Mathf.FloorToInt(pos.Z / TreeChunk));
		if (!_colliders.TryGetValue(key, out var list)) _colliders[key] = list = new();
		list.Add((shape, xf));
	}

	private Rid Cylinder(float r, float h)
	{
		float rr = Mathf.Snapped(r, 0.04f);
		return Shape($"cyl_{rr}_{h}", () =>
		{
			var s = PhysicsServer3D.CylinderShapeCreate();
			PhysicsServer3D.ShapeSetData(s, new Godot.Collections.Dictionary { { "radius", rr }, { "height", h } });
			return s;
		});
	}

	private Rid Sphere(float r)
	{
		float rr = Mathf.Snapped(r, 0.05f);
		return Shape($"sph_{rr}", () =>
		{
			var s = PhysicsServer3D.SphereShapeCreate();
			PhysicsServer3D.ShapeSetData(s, rr);
			return s;
		});
	}

	private Rid BoxShape(Vector3 half)
	{
		Vector3 h = new(Mathf.Snapped(half.X, 0.05f), Mathf.Snapped(half.Y, 0.05f), Mathf.Snapped(half.Z, 0.05f));
		return Shape($"box_{h}", () =>
		{
			var s = PhysicsServer3D.BoxShapeCreate();
			PhysicsServer3D.ShapeSetData(s, h);
			return s;
		});
	}

	private void ScatterTrees()
	{
		Vector2 min = _terrain.MinXZ, max = _terrain.MaxXZ;
		float c = TreeCell;
		for (float z = min.Y + c * 0.5f; z < max.Y; z += c)
			for (float x = min.X + c * 0.5f; x < max.X; x += c)
			{
				float px = x + _rng.RandfRange(-0.45f, 0.45f) * c, pz = z + _rng.RandfRange(-0.45f, 0.45f) * c;
				float roll = _rng.Randf(), roll2 = _rng.Randf(), roll3 = _rng.Randf();
				var p2 = new Vector2(px, pz);
				_terrain.SampleFields(px, pz, out float dT, out float sT, out float dS, out float dR);
				if (dT < 2.7f || dR < 4.5f || dS < 3.4f) continue;
				if (ParkDist(p2) < 3.5f) continue;
				float cd = ClearingDist(p2);
				if (cd < 0f) continue;
				if (Cleared(p2, 1.0f, false)) continue;

				float deep = Deep(sT);
				float clump = _clump.GetNoise2D(px, pz) * 0.5f + 0.5f;
				float near = Mathf.SmoothStep(2.7f, 8f, dT);
				float p = (0.5f + 0.4f * deep) * (0.45f + 0.9f * clump) * (0.35f + 0.65f * near);
				p *= Mathf.SmoothStep(0f, 7f, cd) * 0.85f + 0.15f;
				p = Mathf.Max(p, Mathf.SmoothStep(25f, 45f, dT) * 0.9f);   // walls of the valley are solid forest
				if (roll > p) continue;

				float h = Mathf.Min(_terrain.HeightAt(px, pz), Mathf.Min(_terrain.HeightAt(px + 0.3f, pz), _terrain.HeightAt(px - 0.3f, pz)));
				float scale = _rng.RandfRange(0.75f, 1.25f) * (1f + 0.15f * deep);
				float yaw = _rng.RandfRange(0, Mathf.Tau);
				var tilt = new Vector3(_rng.RandfRange(-0.04f, 0.04f), yaw, _rng.RandfRange(-0.04f, 0.04f));
				var basis = Basis.FromEuler(tilt).Scaled(Vector3.One * scale);
				var pos = new Vector3(px, h - 0.05f, pz);
				float tint = _rng.RandfRange(0.82f, 1.08f);

				float decidChance = Mathf.Lerp(0.34f, 0.08f, Mathf.SmoothStep(60f, 190f, sT));
				float snagChance = 0.03f + 0.07f * deep;
				string mesh;
				float trunkR;
				if (roll2 < snagChance) { mesh = "snag"; trunkR = 0.22f; }
				else if (roll2 < snagChance + decidChance) { mesh = roll3 < 0.5f ? "decid_a" : "decid_b"; trunkR = 0.2f; }
				else { mesh = roll3 < 0.4f ? "conifer_a" : (roll3 < 0.75f ? "conifer_b" : "conifer_c"); trunkR = 0.24f; }

				Color col = mesh.StartsWith("decid") ? new Color(tint, tint * _rng.RandfRange(0.95f, 1.05f), tint * 0.9f) : new Color(tint, tint, tint);
				Add(mesh, TreeChunk, pos, basis, col);
				TreeCount++;
				if (TreeCollision)
					AddCollider(pos, Cylinder(trunkR * scale, 6f), new Transform3D(Basis.Identity, pos + new Vector3(0, 2.6f, 0)));

				// the odd stump beside a tree
				if (_rng.Randf() < 0.035f)
				{
					Vector3 sp = pos + new Vector3(_rng.RandfRange(-1.8f, 1.8f), 0, _rng.RandfRange(-1.8f, 1.8f));
					_terrain.SampleFields(sp.X, sp.Z, out float sdT, out _, out float sdS, out _);
					if (sdT > 2.2f && sdS > 3f)
					{
						sp.Y = _terrain.HeightAt(sp.X, sp.Z);
						Add("stump", TreeChunk, sp, Basis.FromEuler(new Vector3(0, _rng.RandfRange(0, 6.28f), 0)), Colors.White);
						AddCollider(sp, Cylinder(0.32f, 1f), new Transform3D(Basis.Identity, sp));
					}
				}
			}
	}

	private void ScatterRocksAndLogs()
	{
		Vector2 min = _terrain.MinXZ, max = _terrain.MaxXZ;
		float c = 6.5f;
		for (float z = min.Y + c * 0.5f; z < max.Y; z += c)
			for (float x = min.X + c * 0.5f; x < max.X; x += c)
			{
				float px = x + _rng.RandfRange(-0.5f, 0.5f) * c, pz = z + _rng.RandfRange(-0.5f, 0.5f) * c;
				float roll = _rng.Randf(), kind = _rng.Randf();
				var p2 = new Vector2(px, pz);
				_terrain.SampleFields(px, pz, out float dT, out float sT, out float dS, out float dR);
				if (dT < 1.6f || dR < 3.5f || ParkDist(p2) < 2f || ClearingDist(p2) < -3f) continue;
				if (Cleared(p2, 1.2f, false)) continue;
				float yaw = _rng.RandfRange(0, Mathf.Tau);

				if (kind < 0.55f)
				{
					// rocks: more along the stream and on the valley walls
					float p = 0.22f + 0.4f * (1f - Mathf.SmoothStep(2f, 9f, dS)) + 0.15f * Mathf.SmoothStep(10f, 30f, dT);
					if (roll > p) continue;
					float s = _rng.RandfRange(0.25f, 0.8f) * (1f + 0.9f * Mathf.SmoothStep(4f, 20f, dT));
					float h = _terrain.HeightAt(px, pz);
					var basis = Basis.FromEuler(new Vector3(_rng.RandfRange(-0.2f, 0.2f), yaw, _rng.RandfRange(-0.2f, 0.2f)))
						.Scaled(new Vector3(s, s * _rng.RandfRange(0.7f, 1.2f), s));
					var pos = new Vector3(px, h - 0.12f * s, pz);
					Add(kind < 0.3f ? "rock_a" : "rock_b", TreeChunk, pos, basis, new Color(1, 1, 1) * _rng.RandfRange(0.8f, 1.05f));
					if (s > 0.55f) AddCollider(pos, Sphere(0.62f * s), new Transform3D(Basis.Identity, pos));
				}
				else if (kind < 0.8f)
				{
					if (dT < 3.2f || dS < 3f || roll > 0.28f) continue;
					var basis = Basis.FromEuler(new Vector3(0, yaw, 0));
					Vector3 axis = basis.X;
					// sit on the lower of the two ends so it doesn't float
					float h = Mathf.Min(_terrain.HeightAt(px + axis.X * 2.2f, pz + axis.Z * 2.2f), _terrain.HeightAt(px - axis.X * 2.2f, pz - axis.Z * 2.2f));
					h = Mathf.Min(h, _terrain.HeightAt(px, pz));
					float s = _rng.RandfRange(0.8f, 1.3f);
					var pos = new Vector3(px, h + 0.12f * s, pz);
					Add("log", TreeChunk, pos, basis.Scaled(Vector3.One * s), Colors.White);
					AddCollider(pos, BoxShape(new Vector3(2.5f * s, 0.22f * s, 0.22f * s)), new Transform3D(basis, pos));
				}
				else
				{
					if (roll > 0.7f) continue;
					float h = _terrain.HeightAt(px, pz);
					Add("branch", TreeChunk, new Vector3(px, h, pz), Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(Vector3.One * _rng.RandfRange(0.7f, 1.4f)), Colors.White);
				}
			}
	}

	private void ScatterFoliage()
	{
		Vector2 min = _terrain.MinXZ, max = _terrain.MaxXZ;
		float c = 1.25f;
		for (float z = min.Y + c * 0.5f; z < max.Y; z += c)
			for (float x = min.X + c * 0.5f; x < max.X; x += c)
			{
				float px = x + _rng.RandfRange(-0.5f, 0.5f) * c, pz = z + _rng.RandfRange(-0.5f, 0.5f) * c;
				float roll = _rng.Randf();
				var p2 = new Vector2(px, pz);
				_terrain.SampleFields(px, pz, out float dT, out float sT, out float dS, out float dR);
				float half = 0.9f;
				if (dT < half + 0.35f || dR < 2.6f || ParkDist(p2) < 0.3f) continue;
				if (Cleared(p2, 0.2f, true)) continue;
				float cd = ClearingDist(p2);
				float clump = _clump.GetNoise2D(px * 2.3f, pz * 2.3f) * 0.5f + 0.5f;
				string mesh = null; float p = 0;
				if (cd < -1.5f)
				{
					mesh = "grass"; p = 0.55f + 0.35f * clump;
				}
				else if (dS < 2.8f) continue;
				else
				{
					// grass verges early on, ferns under the trees
					float verge = (1f - Mathf.SmoothStep(3f, 5f, dT)) * (1f - Mathf.SmoothStep(50f, 110f, sT));
					float edgeOfClearing = 1f - Mathf.SmoothStep(-1.5f, 4f, cd);
					if (_rng.Randf() < Mathf.Max(verge * 0.8f, edgeOfClearing)) { mesh = "grass"; p = 0.5f * clump + 0.15f; }
					else
					{
						mesh = "fern";
						float band = Mathf.SmoothStep(1.3f, 3f, dT) * (1f - 0.55f * Mathf.SmoothStep(8f, 25f, dT));
						p = (0.12f + 0.55f * band) * (0.3f + 1.1f * clump) * (1f - 0.35f * Deep(sT));
					}
				}
				if (roll > p) continue;
				// too steep for plants to look right
				if (_terrain.NormalAt(px, pz).Y < 0.72f) continue;
				float h = _terrain.HeightAt(px, pz);
				float s = mesh == "fern" ? _rng.RandfRange(0.7f, 1.35f) : _rng.RandfRange(0.7f, 1.2f);
				var basis = Basis.FromEuler(new Vector3(0, _rng.RandfRange(0, Mathf.Tau), 0)).Scaled(new Vector3(s, s * _rng.RandfRange(0.85f, 1.15f), s));
				float t = _rng.RandfRange(0.75f, 1.05f);
				Add(mesh, FoliageChunk, new Vector3(px, h - 0.03f, pz), basis, new Color(t, t, t * 0.95f));
			}
	}

	private void Commit()
	{
		var root = new Node3D { Name = "Instances" };
		AddChild(root);
		foreach (var (meshKey, byChunk) in _inst)
		{
			bool foliage = meshKey is "fern" or "grass";
			bool small = foliage || meshKey is "branch" or "stump";
			float chunk = foliage ? FoliageChunk : TreeChunk;
			foreach (var (key, list) in byChunk)
			{
				var mm = new MultiMesh
				{
					TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
					UseColors = true,
					Mesh = _meshes[meshKey],
					InstanceCount = list.Count,
				};
				for (int i = 0; i < list.Count; i++)
				{
					mm.SetInstanceTransform(i, list[i].xf);
					mm.SetInstanceColor(i, list[i].c);
				}
				var mmi = new MultiMeshInstance3D
				{
					Name = $"{meshKey}_{key.X}_{key.Y}",
					Multimesh = mm,
					VisibilityRangeEnd = (foliage ? FoliageViewDistance : (small ? 70f : TreeViewDistance)) + chunk * 0.7f,
					CastShadow = foliage ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
				};
				root.AddChild(mmi);
			}
		}

		if (!TreeCollision) return;
		var space = GetWorld3D().Space;
		foreach (var (key, list) in _colliders)
		{
			var body = PhysicsServer3D.BodyCreate();
			PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
			PhysicsServer3D.BodySetCollisionLayer(body, 1);
			PhysicsServer3D.BodySetCollisionMask(body, 0);
			PhysicsServer3D.BodySetState(body, PhysicsServer3D.BodyState.Transform, Transform3D.Identity);
			foreach (var (shape, xf) in list) PhysicsServer3D.BodyAddShape(body, shape, xf);
			PhysicsServer3D.BodySetSpace(body, space);
			_bodies.Add(body);
		}
	}
}
