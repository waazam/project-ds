using System.Linq;
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
	/// <summary>The scatter (trees, rocks, logs, boulders) keeps this far from the terrain's clearing centre when it is
	/// wider than the terrain's own ClearingRadius (Act 6's stand of stairs and its ring need the whole disc clear;
	/// the boulders and the dense trees then ring it from outside).</summary>
	[Export] public float TreeClearRadius = 0f;
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
		ScatterBoulders();
		ScatterFoliage();
		Commit();
	}

	// =================================================================== meshes

	private void BuildMeshes()
	{
		// tall straight firs: bare lower trunk, drooping bough tiers (alpha cards)
		_meshes["fir_giant"] = FirMesh(11, 33f, 0.52f, 0.42f, 14, 4.3f, 0.10f);
		_meshes["fir_tall"] = FirMesh(12, 26f, 0.42f, 0.36f, 13, 3.7f, 0.12f);
		_meshes["fir_mid"] = FirMesh(13, 18f, 0.31f, 0.26f, 11, 3.0f, 0.15f);
		_meshes["fir_spire"] = FirMesh(14, 22f, 0.34f, 0.30f, 15, 2.4f, 0.08f);
		_meshes["fir_young"] = FirMesh(15, 9f, 0.17f, 0.10f, 8, 2.0f, 0.05f);
		_meshes["decid_a"] = DeciduousMesh(4);
		_meshes["decid_b"] = DeciduousMesh(5);
		_meshes["snag"] = SnagMesh(6, 9.5f);
		_meshes["snag_tall"] = SnagMesh(16, 19f);
		_meshes["stump"] = StumpMesh();
		_meshes["rock_a"] = RockMesh(7);
		_meshes["rock_b"] = RockMesh(8);
		_meshes["boulder_a"] = BoulderMesh(21);
		_meshes["boulder_b"] = BoulderMesh(22);
		_meshes["log"] = LogMesh();
		_meshes["branch"] = BranchMesh();
		_meshes["fern"] = FernMesh();
		_meshes["grass"] = GrassMesh();
	}

	/// <summary>
	/// PS2-style fir: a tall trunk cylinder (flared, slightly bent) with dead
	/// stubs on the bare lower part, then tiers of drooping bough cards that
	/// use an alpha-scissor needle texture, plus a crossed-card leader on top.
	/// crownStart = fraction of the height where live branches begin; ragged =
	/// chance a bough is missing (irregular silhouette).
	/// </summary>
	internal static Mesh FirMesh(int seed, float height, float trunkR, float crownStart, int tiers, float maxR, float ragged)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919) };
		var k = new MeshKit();
		// ---- trunk: three segments with a slight wander
		Vector3 Axis(float y)
		{
			float f = y / height;
			return new Vector3(Mathf.Sin(f * 2.1f + seed) * 0.18f * f, y, Mathf.Cos(f * 1.7f + seed * 2) * 0.14f * f);
		}
		float topY = height * 0.93f;
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.62f, 0.6f, 0.58f);
		k.Cylinder(new Vector3(0, -0.5f, 0), new Vector3(0, 0.6f, 0), trunkR * 1.45f, trunkR * 1.02f, 7, false, 1f);   // root flare
		float[] ys = { 0.6f, height * 0.3f, height * 0.62f, topY };
		for (int s = 0; s < ys.Length - 1; s++)
		{
			float r0 = trunkR * Mathf.Lerp(1.02f, 0.08f, ys[s] / topY), r1 = trunkR * Mathf.Lerp(1.02f, 0.08f, ys[s + 1] / topY);
			k.Color = new Color(0.62f, 0.6f, 0.58f).Lerp(new Color(0.8f, 0.78f, 0.74f), (float)s / 2f);
			k.Cylinder(Axis(ys[s]), Axis(ys[s + 1]), r0, r1, s == 0 ? 7 : 6, false, 1f);
		}
		// dead stubs on the bare trunk
		int stubs = Mathf.RoundToInt(crownStart * height * 0.55f);
		k.Color = new Color(0.5f, 0.48f, 0.46f);
		for (int i = 0; i < stubs; i++)
		{
			float y = rng.RandfRange(Mathf.Min(2.4f, crownStart * height * 0.5f), crownStart * height);
			float a = rng.RandfRange(0, Mathf.Tau);
			Vector3 dir = new(Mathf.Cos(a), rng.RandfRange(-0.5f, 0.05f), Mathf.Sin(a));
			float len = rng.RandfRange(0.35f, 1.1f) * Mathf.Clamp(trunkR * 2.5f, 0.5f, 1.3f);
			Vector3 from = Axis(y);
			k.Cylinder(from, from + dir.Normalized() * len, 0.045f, 0.01f, 3, false, 2f);
		}

		// ---- bough tiers
		k.Mat(ProcTextures.FirBranchMat);
		float y0 = crownStart * height;
		for (int t = 0; t < tiers; t++)
		{
			float f = (float)t / (tiers - 1);
			float y = Mathf.Lerp(y0, height * 0.9f, Mathf.Pow(f, 0.92f)) + rng.RandfRange(-0.25f, 0.25f);
			float profile = Mathf.Lerp(1f, 0.16f, Mathf.Pow(f, 1.1f));
			// lower tiers of tall trees are a bit shorter than the widest ones (crown shape)
			profile *= Mathf.Lerp(0.82f, 1f, Mathf.SmoothStep(0f, 0.25f, f)) * rng.RandfRange(0.82f, 1.15f);
			float R = maxR * profile;
			int n = Mathf.Max(4, Mathf.RoundToInt(Mathf.Lerp(11f, 5f, f)));
			float droop = Mathf.Lerp(0.7f, 0.35f, f);           // radians down at the tip
			float rot = rng.RandfRange(0, Mathf.Tau);
			float ao = Mathf.Lerp(0.55f, 1f, f);                  // lower tiers sit in shade
			Vector3 c = Axis(y);
			// dense dark core: a low cone skirt so the tier reads as a solid mass, not loose fronds
			{
				float cr = R * 0.55f, drop = cr * Mathf.Tan(droop * 0.7f);
				int cs = 7;
				k.Color = new Color(ao * 0.8f, ao * 0.8f, ao * 0.8f);
				Vector3 apex = c + Vector3.Up * 0.35f;
				for (int s = 0; s < cs; s++)
				{
					float a0 = rot + Mathf.Tau * s / cs, a1 = rot + Mathf.Tau * (s + 1) / cs;
					Vector3 p0 = c + new Vector3(Mathf.Cos(a0) * cr, -drop, Mathf.Sin(a0) * cr);
					Vector3 p1 = c + new Vector3(Mathf.Cos(a1) * cr, -drop, Mathf.Sin(a1) * cr);
					Vector3 nn = (p0 + p1 - 2f * c).Normalized() + Vector3.Up;
					k.Tri(apex, p0, p1, nn.Normalized(), new Vector2(0.5f, 0.99f), new Vector2(0.1f, 0.93f), new Vector2(0.9f, 0.93f));
				}
			}
			for (int i = 0; i < n; i++)
			{
				if (rng.Randf() < ragged && t < tiers - 2) continue;
				float a = rot + Mathf.Tau * i / n + rng.RandfRange(-0.25f, 0.25f);
				float L = R * rng.RandfRange(0.78f, 1.12f);
				Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
				Vector3 side = new(-dir.Z, 0, dir.X);
				float dr = droop * rng.RandfRange(0.75f, 1.3f);
				float pitch = rng.RandfRange(-0.12f, 0.1f);            // some boughs lift, some sag
				Vector3 root = c + dir * trunkR * 0.3f + Vector3.Up * rng.RandfRange(-0.35f, 0.45f);
				// out fairly level, then the outer half hangs down
				Vector3 mid = root + dir * L * 0.5f + Vector3.Down * (L * 0.5f * Mathf.Tan(dr * 0.35f + pitch));
				Vector3 tip = mid + dir * L * 0.47f + Vector3.Down * (L * 0.5f * Mathf.Tan(Mathf.Min(dr * 1.15f + pitch, 0.95f)));
				float hw = L * 0.44f;
				Vector3 nrm1 = (mid - root).Cross(side).Normalized(); if (nrm1.Y < 0) nrm1 = -nrm1;
				Vector3 nrm2 = (tip - mid).Cross(side).Normalized(); if (nrm2.Y < 0) nrm2 = -nrm2;
				float lum = ao * rng.RandfRange(0.85f, 1.1f);
				k.Color = new Color(lum * 0.7f, lum * 0.7f, lum * 0.7f);
				k.Quad(root - side * hw * 0.5f, root + side * hw * 0.5f, mid + side * hw, mid - side * hw, nrm1,
					new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(0, 0.5f));
				k.Color = new Color(lum, lum, lum);
				k.Quad(mid - side * hw, mid + side * hw, tip + side * hw * 0.8f, tip - side * hw * 0.8f, nrm2,
					new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0), new Vector2(0, 0));
			}
		}
		// leader: two crossed vertical cards at the very top
		{
			float lh = Mathf.Max(1.4f, height * 0.09f), lw = lh * 0.35f;
			Vector3 b = Axis(height * 0.86f), top = Axis(height * 0.86f) + Vector3.Up * lh;
			k.Color = new Color(0.9f, 0.9f, 0.9f);
			for (int i = 0; i < 2; i++)
			{
				float a = i * Mathf.Pi * 0.5f + seed;
				Vector3 sd = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * lw;
				Vector3 nn = new Vector3(-sd.Z, 0, sd.X).Normalized();
				k.Quad(b - sd, b + sd, top + sd * 0.2f, top - sd * 0.2f, nn,
					new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
			}
		}
		return k.Commit();
	}

	internal static Mesh BoulderMesh(int seed)
	{
		var k = new MeshKit();
		k.Color = new Color(0.8f, 0.8f, 0.78f);
		k.Mat(ProcTextures.MossRockMat);
		k.Blob(Vector3.Zero, new Vector3(1.25f, 0.8f, 1.0f), seed, 0.24f, true, 0.6f);
		k.Blob(new Vector3(0.75f, -0.1f, 0.35f), new Vector3(0.7f, 0.55f, 0.65f), seed + 100, 0.22f, true, 0.6f);
		return k.Commit();
	}

	internal static Mesh DeciduousMesh(int seed)
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

	internal static Mesh SnagMesh(int seed, float h)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 31337) };
		var k = new MeshKit();
		float r = 0.2f + h * 0.012f;
		Vector3 top = new(0.02f * h, h, 0.01f * h);
		k.Color = new Color(0.72f, 0.72f, 0.72f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0, -0.4f, 0), top * 0.5f, r * 1.1f, r * 0.7f, 6, false, 1f);
		k.Color = new Color(0.9f, 0.9f, 0.88f);          // bleached, bark sloughing off higher up
		k.Cylinder(top * 0.5f, top, r * 0.7f, r * 0.25f, 6, false, 1f);
		k.Color = new Color(0.62f, 0.6f, 0.56f);
		k.Mat(ProcTextures.EndGrainMat).Cylinder(top, top + new Vector3(0.05f, 0.08f, 0), r * 0.25f, 0.02f, 6, false, 1f);   // broken top
		k.Mat(ProcTextures.BarkMat);
		int nb = Mathf.RoundToInt(h * 0.7f);
		for (int b = 0; b < nb; b++)
		{
			float y = rng.RandfRange(h * 0.3f, h * 0.92f);
			float a = rng.RandfRange(0, Mathf.Tau);
			float len = rng.RandfRange(0.5f, 1.8f) * (1.25f - y / h);
			Vector3 from = top * (y / h);
			k.Cylinder(from, from + new Vector3(Mathf.Cos(a) * len, rng.RandfRange(-0.5f, 0.2f) * len, Mathf.Sin(a) * len), 0.055f, 0.01f, 4, false);
		}
		return k.Commit();
	}

	internal static Mesh StumpMesh()
	{
		var k = new MeshKit();
		k.Color = new Color(0.8f, 0.78f, 0.74f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0, -0.3f, 0), new Vector3(0, 0.45f, 0), 0.34f, 0.28f, 7, false, 1f);
		k.Color = new Color(0.7f, 0.66f, 0.6f);
		k.Mat(ProcTextures.EndGrainMat).Cylinder(new Vector3(0, 0.44f, 0), new Vector3(0, 0.45f, 0), 0.28f, 0.28f, 7, true, 1.7f);
		return k.Commit();
	}

	internal static Mesh RockMesh(int seed)
	{
		var k = new MeshKit();
		k.Color = new Color(0.85f, 0.85f, 0.82f);
		k.Mat(ProcTextures.MossRockMat).Blob(Vector3.Zero, new Vector3(1f, 0.62f, 0.85f), seed, 0.26f, true, 0.7f);
		return k.Commit();
	}

	internal static Mesh LogMesh()
	{
		var k = new MeshKit();
		k.Color = new Color(0.62f, 0.6f, 0.55f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(-2.6f, 0, 0), new Vector3(2.6f, 0, 0.1f), 0.27f, 0.2f, 7, false, 1f);
		k.Mat(ProcTextures.EndGrainMat).Cylinder(new Vector3(-2.62f, 0, 0), new Vector3(-2.6f, 0, 0), 0.265f, 0.265f, 7, true, 1.8f);
		k.Cylinder(new Vector3(2.6f, 0, 0.1f), new Vector3(2.62f, 0, 0.1f), 0.2f, 0.2f, 7, true, 2.3f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0.5f, 0.1f, 0.05f), new Vector3(0.9f, 0.3f, 0.9f), 0.06f, 0.02f, 4, false);
		return k.Commit();
	}

	internal static Mesh BranchMesh()
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

	internal static Mesh FernMesh()
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

	internal static Mesh GrassMesh()
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

	private float ClearingDist(Vector2 p) => p.DistanceTo(_terrain.ClearingCenter) - Mathf.Max(_terrain.ClearingRadius, TreeClearRadius);

	/// <summary>For tests: how many placed instances of the meshes <paramref name="meshKey"/> accepts stand within
	/// <paramref name="radius"/> of <paramref name="centre"/> (world xz).</summary>
	public int CountInstancesWithin(Vector2 centre, float radius, System.Func<string, bool> meshKey)
	{
		int n = 0;
		var roots = new List<Node3D>();
		if (GetNodeOrNull<Node3D>("Instances") is { } a) roots.Add(a);
		foreach (var child in roots.SelectMany(r => r.GetChildren()))
		{
			if (child is not MultiMeshInstance3D mmi || mmi.Multimesh == null) continue;
			// Names are "{meshKey}_{chunkX}_{chunkY}"; the key itself may carry underscores.
			string[] parts = mmi.Name.ToString().Split('_');
			if (parts.Length < 3) continue;
			string key = string.Join("_", parts[..^2]);
			if (!meshKey(key)) continue;
			var mm = mmi.Multimesh;
			for (int i = 0; i < mm.InstanceCount; i++)
			{
				Vector3 o = mmi.GlobalTransform * mm.GetInstanceTransform(i).Origin;
				if (new Vector2(o.X, o.Z).DistanceTo(centre) < radius) n++;
			}
		}
		return n;
	}

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
				float dB = _terrain.SampleBranch(px, pz, out float bHalf);
				if (bHalf > 0.08f && dB < bHalf + 1.9f) continue;
				float dA = _terrain.RouteDistance(px, pz);
				if (dA < 1.6f) continue;

				float deep = Deep(sT);
				float clump = _clump.GetNoise2D(px, pz) * 0.5f + 0.5f;
				float dP = Mathf.Min(dT, bHalf > 0.08f ? dB + 0.9f - bHalf : 999f);
				float near = Mathf.SmoothStep(2.7f, 8f, dP);
				float p = (0.5f + 0.4f * deep) * (0.45f + 0.9f * clump) * (0.35f + 0.65f * near);
				p *= Mathf.SmoothStep(0f, 7f, cd) * 0.85f + 0.15f;
				p = Mathf.Max(p, Mathf.SmoothStep(25f, 45f, dT) * 0.9f);   // walls of the valley are solid forest
				if (roll > p) continue;

				float h = Mathf.Min(_terrain.HeightAt(px, pz), Mathf.Min(_terrain.HeightAt(px + 0.3f, pz), _terrain.HeightAt(px - 0.3f, pz)));
				float scale = _rng.RandfRange(0.8f, 1.15f) * (1f + 0.1f * deep);
				float yaw = _rng.RandfRange(0, Mathf.Tau);
				var tilt = new Vector3(_rng.RandfRange(-0.025f, 0.025f), yaw, _rng.RandfRange(-0.025f, 0.025f));
				var pos = new Vector3(px, h - 0.05f, pz);
				float tint = _rng.RandfRange(0.8f, 1.08f);

				// mostly tall firs; a few autumn broadleaves early on, more dead snags deeper in
				float decidChance = 0f;   // the reference is all conifer; broadleaves kept only as an option
				float snagChance = 0.035f + 0.06f * deep;
				bool nearPath = dT < 9f;
				string mesh;
				float trunkR;
				if (roll2 < snagChance) { mesh = roll3 < 0.55f ? "snag_tall" : "snag"; trunkR = mesh == "snag" ? 0.31f : 0.43f; }
				else if (roll2 < snagChance + decidChance && dT > 4.5f) { mesh = roll3 < 0.5f ? "decid_a" : "decid_b"; trunkR = 0.2f; }
				else
				{
					// big-trunked giants line the trail so the first-person view is framed by trunks
					float r3 = nearPath ? roll3 * 0.8f : roll3;
					if (r3 < 0.24f) { mesh = "fir_giant"; trunkR = 0.52f; }
					else if (r3 < 0.52f) { mesh = "fir_tall"; trunkR = 0.42f; }
					else if (r3 < 0.72f) { mesh = "fir_spire"; trunkR = 0.34f; }
					else if (r3 < 0.9f || dT < 7f || Cleared(p2, 2.6f, false)) { mesh = "fir_mid"; trunkR = 0.31f; }
					else { mesh = "fir_young"; trunkR = 0.17f; }
				}
				// the hidden test route keeps a thin lane: trunk surface >= 1.2 m from its centre
				if (dA < 1.25f + trunkR * scale * 1.45f) continue;
				var basis = Basis.FromEuler(tilt).Scaled(Vector3.One * scale);

				Color col = mesh.StartsWith("decid") ? new Color(tint, tint * _rng.RandfRange(0.9f, 1.05f), tint * 0.85f) : new Color(tint, tint * 0.98f, tint * 0.97f);
				Add(mesh, TreeChunk, pos, basis, col);
				TreeCount++;
				if (TreeCollision)
					AddCollider(pos, Cylinder(trunkR * scale, 6f), new Transform3D(Basis.Identity, pos + new Vector3(0, 2.6f, 0)));

				// the odd stump beside a tree
				if (_rng.Randf() < 0.035f)
				{
					Vector3 sp = pos + new Vector3(_rng.RandfRange(-1.8f, 1.8f), 0, _rng.RandfRange(-1.8f, 1.8f));
					_terrain.SampleFields(sp.X, sp.Z, out float sdT, out _, out float sdS, out _);
					if (sdT > 2.2f && sdS > 3f && _terrain.RouteDistance(sp.X, sp.Z) > 2f && _terrain.SampleBranch(sp.X, sp.Z, out float sbh) > sbh + 1.2f)
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
				if (_terrain.RouteDistance(px, pz) < 3.6f) continue;
				float rdB = _terrain.SampleBranch(px, pz, out float rbHalf);
				if (rbHalf > 0.08f && rdB < rbHalf + (kind < 0.8f && kind >= 0.55f ? 3f : 1.2f)) continue;

				if (kind < 0.55f)
				{
					// rocks: more along the stream and on the valley walls
					float p = 0.22f + 0.4f * (1f - Mathf.SmoothStep(2f, 9f, dS)) + 0.15f * Mathf.SmoothStep(10f, 30f, dT);
					if (roll > p) continue;
					float s = _rng.RandfRange(0.25f, 0.8f) * (1f + 0.9f * Mathf.SmoothStep(4f, 20f, dT));
					// on a slope, settle toward the low side of its footprint so the downhill flank isn't undercut
					float h = Mathf.Lerp(_terrain.HeightAt(px, pz), RingLow(px, pz, 0.9f * s), 0.7f);
					var basis = Basis.FromEuler(new Vector3(_rng.RandfRange(-0.2f, 0.2f), yaw, _rng.RandfRange(-0.2f, 0.2f)))
						.Scaled(new Vector3(s, s * _rng.RandfRange(0.7f, 1.2f), s));
					var pos = new Vector3(px, h - 0.12f * s, pz);
					Add(kind < 0.3f ? "rock_a" : "rock_b", TreeChunk, pos, basis, new Color(1, 1, 1) * _rng.RandfRange(0.8f, 1.05f));
					if (s > 0.55f) AddCollider(pos, Sphere(0.62f * s), new Transform3D(Basis.Identity, pos));
				}
				else if (kind < 0.8f)
				{
					if (dT < 3.2f || dS < 3f || roll > 0.28f) continue;
					var yb = Basis.FromEuler(new Vector3(0, yaw, 0));
					Vector3 axis = yb.X;
					float s = _rng.RandfRange(0.8f, 1.3f);
					// lying along the ground: pitched to the line between the ground at its two ends (a level
					// 5 m log on a slope leaves its downhill end in the air), lowered where the ground dips mid-way
					float half = 2.6f * s;
					float hA = _terrain.HeightAt(px + axis.X * half, pz + axis.Z * half), hB = _terrain.HeightAt(px - axis.X * half, pz - axis.Z * half);
					float dip = 0f;
					foreach (float t in new[] { -0.5f, 0f, 0.5f })
						dip = Mathf.Min(dip, _terrain.HeightAt(px + axis.X * half * t, pz + axis.Z * half * t) - Mathf.Lerp(hB, hA, t * 0.5f + 0.5f));
					var basis = LieAlong(axis * (2f * half) + Vector3.Up * (hA - hB), yb.Z);
					// across a steep side slope (or a crest) a round log is undercut where the ground falls away beside it: bed it deeper
					float across = 0f;
					foreach (float t in new[] { -0.9f, 0f, 0.9f })
					{
						float cx = px + axis.X * half * t, cz = pz + axis.Z * half * t;
						float hc = _terrain.HeightAt(cx, cz);
						across = Mathf.Max(across, (hc - Mathf.Min(_terrain.HeightAt(cx + yb.Z.X * 0.35f * s, cz + yb.Z.Z * 0.35f * s), _terrain.HeightAt(cx - yb.Z.X * 0.35f * s, cz - yb.Z.Z * 0.35f * s))) / (0.35f * s));
					}
					float bed = Mathf.Clamp(0.165f * across - 0.07f, 0f, 0.2f) * s;
					var pos = new Vector3(px, (hA + hB) * 0.5f + dip + 0.12f * s - bed, pz);
					Add("log", TreeChunk, pos, basis.Scaled(Vector3.One * s), Colors.White);
					AddCollider(pos, BoxShape(new Vector3(2.5f * s, 0.22f * s, 0.22f * s)), new Transform3D(basis, pos));
				}
				else
				{
					if (roll > 0.7f) continue;
					var yb = Basis.FromEuler(new Vector3(0, yaw, 0));
					float s = _rng.RandfRange(0.7f, 1.4f);
					// lying along the ground: pitched to the ground under its ends and rolled to the ground across it
					float half = 0.9f * s;
					float hA = _terrain.HeightAt(px + yb.X.X * half, pz + yb.X.Z * half), hB = _terrain.HeightAt(px - yb.X.X * half, pz - yb.X.Z * half);
					float hL = _terrain.HeightAt(px + yb.Z.X * 0.5f, pz + yb.Z.Z * 0.5f), hR = _terrain.HeightAt(px - yb.Z.X * 0.5f, pz - yb.Z.Z * 0.5f);
					float h = Mathf.Min(_terrain.HeightAt(px, pz), (hA + hB) * 0.5f);
					var basis = LieAlong(yb.X * (2f * half) + Vector3.Up * (hA - hB), yb.Z + Vector3.Up * (hL - hR));
					Add("branch", TreeChunk, new Vector3(px, h - 0.01f, pz), basis.Scaled(Vector3.One * s), Colors.White);
				}
			}
	}

	/// <summary>Lowest ground on a ring of radius r round (x, z).</summary>
	private float RingLow(float x, float z, float r)
	{
		float lo = _terrain.HeightAt(x, z);
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f;
			lo = Mathf.Min(lo, _terrain.HeightAt(x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r));
		}
		return lo;
	}

	/// <summary>An orthonormal basis whose X runs along <paramref name="along"/>, with Z as close to <paramref name="across"/> as it allows (Y is up-ish).</summary>
	private static Basis LieAlong(Vector3 along, Vector3 across)
	{
		Vector3 x = along.Normalized();
		Vector3 y = across.Cross(x).Normalized();
		if (y.Y < 0f) y = -y;
		Vector3 z = x.Cross(y);
		return new Basis(x, y, z);
	}

	/// <summary>Big dark mossy boulders: along the trail, around the clearing and on the valley walls.</summary>
	private void ScatterBoulders()
	{
		Vector2 min = _terrain.MinXZ, max = _terrain.MaxXZ;
		float c = 8.5f;
		for (float z = min.Y + c * 0.5f; z < max.Y; z += c)
			for (float x = min.X + c * 0.5f; x < max.X; x += c)
			{
				float px = x + _rng.RandfRange(-0.5f, 0.5f) * c, pz = z + _rng.RandfRange(-0.5f, 0.5f) * c;
				float roll = _rng.Randf(), kind = _rng.Randf();
				float s = _rng.RandfRange(0.7f, 1.6f);
				var p2 = new Vector2(px, pz);
				_terrain.SampleFields(px, pz, out float dT, out float sT, out float dS, out float dR);
				float reach = 1.45f * s;
				if (dT < reach + 1.4f || dR < reach + 2f || ParkDist(p2) < reach + 2f) continue;
				if (Cleared(p2, reach + 0.5f, false)) continue;
				if (_terrain.RouteDistance(px, pz) < reach + 1.4f) continue;
				float bdB = _terrain.SampleBranch(px, pz, out float bbHalf);
				if (bbHalf > 0.08f && bdB < bbHalf + reach + 0.8f) continue;
				float cd = ClearingDist(p2);
				if (cd < -6f) continue;
				float p = 0.1f + 0.3f * (1f - Mathf.SmoothStep(4f, 14f, dT)) + 0.3f * (1f - Mathf.SmoothStep(0f, 8f, Mathf.Abs(cd + 2f)))
					+ 0.15f * (1f - Mathf.SmoothStep(3f, 10f, dS));
				if (roll > p) continue;
				if (_terrain.NormalAt(px, pz).Y < 0.7f) s *= 0.7f;
				// half sunk, and settled toward the low side of its footprint on a slope
				float h = Mathf.Lerp(_terrain.HeightAt(px, pz), RingLow(px, pz, 1.0f * s), 0.5f);
				var basis = Basis.FromEuler(new Vector3(_rng.RandfRange(-0.15f, 0.15f), _rng.RandfRange(0, Mathf.Tau), _rng.RandfRange(-0.15f, 0.15f)))
					.Scaled(new Vector3(s, s * _rng.RandfRange(0.75f, 1.1f), s));
				var pos = new Vector3(px, h - 0.25f * s, pz);
				float t = _rng.RandfRange(0.75f, 1.0f);
				Add(kind < 0.5f ? "boulder_a" : "boulder_b", TreeChunk, pos, basis, new Color(t, t, t));
				AddCollider(pos, Sphere(0.95f * s), new Transform3D(Basis.Identity, pos + new Vector3(0, 0.1f * s, 0)));
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
				float fdB = _terrain.SampleBranch(px, pz, out float fbHalf);
				if (fbHalf > 0.08f && fdB < fbHalf + 0.15f) continue;
				float cd = ClearingDist(p2);
				float clump = _clump.GetNoise2D(px * 2.3f, pz * 2.3f) * 0.5f + 0.5f;
				string mesh = null; float p = 0;
				if (cd < -1.5f && _terrain.ClearingGrass)
				{
					mesh = "grass"; p = 0.55f + 0.35f * clump;
				}
				else if (cd < -1.5f)
				{
					// the stairs' gap: bare forest floor with the odd fern and tuft
					mesh = _rng.Randf() < 0.6f ? "fern" : "grass"; p = 0.1f + 0.2f * clump;
				}
				else if (dS < 2.8f) continue;
				else
				{
					// grass verges early on, ferns under the trees
					float verge = (1f - Mathf.SmoothStep(3f, 5f, dT)) * (1f - Mathf.SmoothStep(50f, 110f, sT));
					float edgeOfClearing = _terrain.ClearingGrass ? 1f - Mathf.SmoothStep(-1.5f, 4f, cd) : 0f;
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
				// late October: a good share of the bracken has gone rust-brown
				float brown = mesh == "fern" ? Mathf.Clamp(_rng.Randf() * 1.6f - 0.5f + 0.4f * clump, 0f, 1f) : _rng.Randf() * 0.5f;
				var tc = new Color(t, t, t * 0.95f).Lerp(new Color(t * 1.55f, t * 0.95f, t * 0.55f), brown);
				Add(mesh, FoliageChunk, new Vector3(px, h - 0.03f, pz), basis, tc);
			}
	}

	private void Commit()
	{
		CommitInstances(_inst, _colliders, "Instances");
	}

	private static void AddTo(Dictionary<string, Dictionary<Vector2I, List<(Transform3D xf, Color c)>>> inst, string mesh, float chunk, Vector3 pos, Basis basis, Color tint)
	{
		if (!inst.TryGetValue(mesh, out var byChunk)) inst[mesh] = byChunk = new();
		var key = new Vector2I(Mathf.FloorToInt(pos.X / chunk), Mathf.FloorToInt(pos.Z / chunk));
		if (!byChunk.TryGetValue(key, out var list)) byChunk[key] = list = new();
		list.Add((new Transform3D(basis, pos), tint));
	}

	private void AddColliderTo(Dictionary<Vector2I, List<(Rid shape, Transform3D xf)>> cols, Vector3 pos, Rid shape, Transform3D xf)
	{
		var key = new Vector2I(Mathf.FloorToInt(pos.X / TreeChunk), Mathf.FloorToInt(pos.Z / TreeChunk));
		if (!cols.TryGetValue(key, out var list)) cols[key] = list = new();
		list.Add((shape, xf));
	}

	private void CommitInstances(Dictionary<string, Dictionary<Vector2I, List<(Transform3D xf, Color c)>>> inst, Dictionary<Vector2I, List<(Rid shape, Transform3D xf)>> cols, string rootName)
	{
		var root = new Node3D { Name = rootName };
		AddChild(root);
		foreach (var (meshKey, byChunk) in inst)
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
		foreach (var (key, list) in cols)
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
