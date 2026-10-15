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
		// (2026-10-11, quicker loads: in the hollow, once the story's gone underground for good (past Act 13) the summer
		// woods are never seen again, nor walked: their trees and rocks aren't built on a Continue there. A second off a load.)
		if (GetTree().CurrentScene?.SceneFilePath == Systems.StoryManager.HollowScene && Systems.StoryManager.Instance is { Current: >= Systems.Checkpoint.Act13Finished })
		{
			GD.Print("[build] ForestScatter: skipped (the story is past the summer woods)");
			return;
		}
		using var __timer = Systems.BuildTimer.Time("ForestScatter");
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (_terrain == null) { GD.PushWarning("ForestScatter: no terrain in group 'terrain'"); return; }
		_rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		_clump = new FastNoiseLite { Seed = Seed, Frequency = 0.045f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FractalOctaves = 2 };
		foreach (var n in GetTree().GetNodesInGroup("clear_zones"))
			if (n is ClearZone cz)
				_clear.Add((new Vector2(cz.GlobalPosition.X, cz.GlobalPosition.Z), cz.Radius, cz.ClearFoliage));

		BuildMeshes();
		ScatterTrees();
		ScatterUnderstory();
		ScatterEdgeThicket();
		PlaceBoardPiles();
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
		// (2026-10-15, the repetition pass) a second of each: one stump, one log and one branch were every one of them
		_meshes["stump_b"] = StumpBrokenMesh();
		_meshes["log_b"] = LogBrokenMesh();
		_meshes["branch_b"] = BranchForkMesh();
		_meshes["fern"] = FernMesh();
		_meshes["grass"] = GrassMesh();
		_meshes["litter"] = LitterMesh();
		// the undergrowth pass (2026-10-03): moss-hung variants, saplings, vine mounds
		_meshes["fir_giant_moss"] = FirMesh(11, 33f, 0.52f, 0.42f, 14, 4.3f, 0.10f, 0.55f);
		_meshes["fir_tall_moss"] = FirMesh(12, 26f, 0.42f, 0.36f, 13, 3.7f, 0.12f, 0.55f);
		_meshes["snag_moss"] = SnagMesh(6, 9.5f, 0.6f);
		_meshes["snag_tall_moss"] = SnagMesh(16, 19f, 0.6f);
		_meshes["sapling_a"] = SaplingMesh(1, 5.5f);
		_meshes["sapling_b"] = SaplingMesh(2, 7f);
		_meshes["sapling_c"] = SaplingMesh(3, 4.2f);
		_meshes["vinemound_a"] = VineMoundMesh(1, new Vector3(1.6f, 1.5f, 1.4f));
		_meshes["vinemound_b"] = VineMoundMesh(2, new Vector3(2.2f, 2.1f, 1.8f));
		_meshes["vinemound_c"] = VineMoundMesh(3, new Vector3(2.6f, 1.3f, 1.7f));   // (2026-10-15) a long low one
		_meshes["palmetto_a"] = PalmettoMesh(1);
		_meshes["palmetto_b"] = PalmettoMesh(2);
		_meshes["boardpile"] = BoardPileMesh(1);
	}

	/// <summary>
	/// PS2-style fir: a tall trunk cylinder (flared, slightly bent) with dead
	/// stubs on the bare lower part, then tiers of drooping bough cards that
	/// use an alpha-scissor needle texture, plus a crossed-card leader on top.
	/// crownStart = fraction of the height where live branches begin; ragged =
	/// chance a bough is missing (irregular silhouette).
	/// </summary>
	internal static Mesh FirMesh(int seed, float height, float trunkR, float crownStart, int tiers, float maxR, float ragged, float moss = 0f)
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
		k.Mat(ProcTextures.TreeBarkMat);
		// one continuous trunk from the root flare in the ground up to the top: smooth shading and bark
		// that runs on unbroken, so there is no seam where the flare meets the trunk
		var rings = new System.Collections.Generic.List<(Vector3 c, float r, Color col)>
		{
			(new Vector3(0, -0.5f, 0), trunkR * 1.55f, new Color(0.56f, 0.54f, 0.52f)),
			(new Vector3(0, -0.12f, 0), trunkR * 1.42f, new Color(0.58f, 0.56f, 0.54f)),
			(new Vector3(0, 0.22f, 0), trunkR * 1.2f, new Color(0.6f, 0.58f, 0.56f)),
			(Axis(0.6f), trunkR * 1.06f, new Color(0.62f, 0.6f, 0.58f)),
			(Axis(1.2f), trunkR * 1.0f, new Color(0.62f, 0.6f, 0.58f)),
		};
		foreach (float fy in new[] { 0.3f, 0.46f, 0.62f, 0.78f, 1f })
		{
			float y = topY * fy;
			rings.Add((Axis(y), trunkR * Mathf.Lerp(1.02f, 0.08f, y / topY), new Color(0.62f, 0.6f, 0.58f).Lerp(new Color(0.68f, 0.66f, 0.63f), fy)));   // (was to 0.8 at the top: the pale upper trunks read as paper)
		}
		TrunkLoft(k, rings, 16, 1f, 0.07f, seed);   // (2026-10-15, "too blocky": sixteen sides, not twelve, and lobed)
		Roots(k, trunkR * 1.42f, seed, rings[1].col);
		// dead stubs on the bare trunk
		var mossRng = new RandomNumberGenerator { Seed = (ulong)(seed * 977 + 13) };
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
			// moss hanging off it (its own random stream: the tree's shape is the same with or without)
			if (moss > 0f && mossRng.Randf() < moss)
				MossCard(k, from + dir.Normalized() * len * mossRng.RandfRange(0.5f, 0.95f), mossRng.RandfRange(0.5f, 1.3f), mossRng.RandfRange(0.25f, 0.45f), mossRng);
		}

		// ---- bough tiers (FirCrown: shared with the lake's cypresses)
		FirCrown(k, rng, Axis, height, trunkR, crownStart, tiers, maxR, ragged, ProcTextures.FirSprayMat);
		// leader (2026-10-15, "spikey": two crossed cards tapering to a point made a spearhead on every top): three sprays
		// of needles up from the top whorl, leaning a little apart, the middle one tallest, so the top is a soft tuft
		k.Mat(ProcTextures.FirSprayMat);
		{
			float lh = Mathf.Max(1.4f, height * 0.09f);
			Vector3 b = Axis(height * 0.86f);
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.Tau / 3f + seed;
				Vector3 lean = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (i == 0 ? 0.12f : 0.4f);
				float lum = i == 0 ? 0.9f : 0.8f;
				Spray(k, b + Vector3.Up * (i == 0 ? 0.1f : 0f), Vector3.Up + lean, lh * (i == 0 ? 1f : 0.7f), lh * 0.42f, a, 0f, new Color(lum, lum, lum), i == 0);
			}
		}
		return k.Commit();
	}

	/// <summary>A fir's crown: <paramref name="tiers"/> whorls of boughs from <paramref name="crownStart"/> of the height to the
	/// top, each a dark core skirt (FirBranchMat) and limbs of wood carrying sprays of needles (<paramref name="spray"/>).</summary>
	internal static void FirCrown(MeshKit k, RandomNumberGenerator rng, System.Func<float, Vector3> Axis, float height, float trunkR, float crownStart, int tiers, float maxR, float ragged, Material spray)
	{
		k.Mat(ProcTextures.FirBranchMat);
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
			// dense dark core: a cone skirt so the tier reads as a solid mass, not loose fronds
			// (2026-10-15, the scan: "the lowest fir branches read as flat slabs", the floating slab at the lake: seen from
			// under or beside, a wide shallow cone is a dark disc hung on the trunk.) Low tiers (within reach of the eye:
			// under 3.4 m) have none, their boughs alone; higher ones a narrower, steeper skirt with a ragged rim and a gap
			// or two, so it reads as needles massed round the trunk, not a plate. (No draws on rng: the tree's shape holds.)
			// (2026-10-15, "blocky": on the narrow whorls of a young fir or a crown's top, the skirt was a little dark spinning
			// top threaded on the trunk, a bead under every tier; only the wider whorls have one now)
			if (y > 3.4f && R > 1.4f)
			{
				float cr = R * 0.42f, drop = cr * Mathf.Max(Mathf.Tan(droop * 0.7f), 0.8f);
				const int cs = 9;
				k.Color = new Color(ao * 0.8f, ao * 0.8f, ao * 0.8f);
				Vector3 apex = c + Vector3.Up * 0.3f;
				float H(int s, float salt) => Mathf.Abs(Mathf.Sin((t * 12.9898f + s * 78.233f + salt) * 43.758f) * 137.5f) % 1f;
				var rim = new Vector3[cs];
				for (int s = 0; s < cs; s++)
				{
					float a0 = rot + Mathf.Tau * s / cs, rr = cr * (0.68f + 0.32f * H(s, y));
					rim[s] = c + new Vector3(Mathf.Cos(a0) * rr, -drop * (0.75f + 0.4f * H(s, 3.1f + y)), Mathf.Sin(a0) * rr);
				}
				for (int s = 0; s < cs; s++)
				{
					if (H(s, 7.7f + y) < 0.18f) continue;   // a gap
					Vector3 p0 = rim[s], p1 = rim[(s + 1) % cs];
					Vector3 nn = (p0 + p1 - 2f * c).Normalized() + Vector3.Up;
					k.Tri(apex, p0, p1, nn.Normalized(), new Vector2(0.5f, 0.99f), new Vector2(0.1f, 0.93f), new Vector2(0.9f, 0.93f));
				}
			}
			// (2026-10-13, the owner: "the lake shore firs look bad too": each bough was two huge frond-shaped cards, up to
			// four metres, so against a bright sky a fir read as a palm, a few paper fronds on a pole.) Each bough is now a
			// limb of wood out of the trunk carrying sprays of needles along it, the photographed spray, a fraction of the
			// size: one carrying on from the limb's end, a pair fanning out to its sides, rolled a little each way so the
			// bough has thickness; the tiers stacked closer.
			for (int i = 0; i < n; i++)
			{
				if (rng.Randf() < ragged * 0.5f && t < tiers - 2) continue;
				// (2026-10-15, less of a regular star: the boughs wander further off even spacing round the whorl)
				float a = rot + Mathf.Tau * i / n + rng.RandfRange(-0.25f, 0.25f) * 1.7f;
				float L = R * rng.RandfRange(0.78f, 1.12f);
				Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
				Vector3 side = new(-dir.Z, 0, dir.X);
				float dr = droop * rng.RandfRange(0.75f, 1.3f);
				float pitch = rng.RandfRange(-0.12f, 0.1f);            // some boughs lift, some sag
				Vector3 root = c + Vector3.Up * rng.RandfRange(-0.35f, 0.45f);
				float sag = Mathf.Tan(Mathf.Min(dr * 0.7f + pitch, 0.9f));
				Vector3 P(float u) => root + dir * (trunkR * 0.3f + L * u) + Vector3.Down * (L * u * u * sag * 0.75f);
				Vector3 Dir(float u) => (P(u + 0.05f) - P(u)).Normalized();
				float lum = ao * rng.RandfRange(0.85f, 1.1f);
				// the limb itself, out of the trunk (three-sided: it's mostly hidden in its needles)
				// (the upper crown's short boughs: their limb and their side pair can't be seen, so they're left out)
				if (f < 0.65f)
				{
					k.Mat(ProcTextures.TreeBarkMat);
					k.Color = new Color(0.5f, 0.48f, 0.46f) * Mathf.Lerp(0.8f, 1f, ao);
					k.Cylinder(root - dir * trunkR * 0.2f, P(0.82f), Mathf.Clamp(trunkR * 0.16f, 0.025f, 0.09f), 0.015f, 3, false, 2f);
				}
				k.Mat(spray);
				float w = Mathf.Max(0.5f, L * 0.42f);
				// the leading spray, carrying on from the limb's end, and the pair off its sides
				Spray(k, P(0.42f), Dir(0.5f), L * 0.58f, w * 1.1f, rng.RandfRange(-0.3f, 0.3f), sag, new Color(lum, lum, lum), f < 0.3f && L > 1.6f);   // (crossed only low on the big trees, at eye height: across every spray it cost a quarter of the frame rate in alpha overdraw)
				if (L < 1.1f) continue;
				float lum2 = lum * 0.85f;
				Vector3 d0 = Dir(0.3f);
				Spray(k, P(0.22f), (d0 * 0.6f + side * 0.8f).Normalized(), L * 0.5f, w * 0.85f, rng.RandfRange(0.15f, 0.45f), sag, new Color(lum2, lum2, lum2));
				Spray(k, P(0.22f), (d0 * 0.6f - side * 0.8f).Normalized(), L * 0.5f, w * 0.85f, -rng.RandfRange(0.15f, 0.45f), sag, new Color(lum2, lum2, lum2));
			}
			k.Mat(ProcTextures.FirBranchMat);
		}
	}

	/// <summary>One spray of needles (FirSprayMat): a card from <paramref name="at"/> out along <paramref name="d"/>, its tip
	/// hanging by the bough's sag, rolled <paramref name="roll"/> radians about its length.</summary>
	internal static void Spray(MeshKit k, Vector3 at, Vector3 d, float len, float w, float roll, float sag, Color col, bool crossed = false)
	{
		d = d.Normalized();
		Vector3 s = d.Cross(Vector3.Up);
		s = s.LengthSquared() < 1e-4f ? Vector3.Right : s.Normalized();
		Vector3 up = s.Cross(d).Normalized();
		Vector3 tip = at + d * len + Vector3.Down * len * (0.15f + 0.3f * sag);
		k.Color = col;
		// (2026-10-15, "spikey": the photographed spray's pointed end on a card as wide at its tip as at its root made a
		// spearhead; hundreds of them, level and evenly round each whorl, a star of spikes) the card now fans out, narrower
		// at the root and wider at the tip, so the spray's end spreads and blunts as a bough's does; and the leading spray
		// can carry a second card across it, so edge-on it's a soft tuft and not a hairline against the sky.
		void Card(float r, float wk)
		{
			Vector3 sr = s * Mathf.Cos(r) + up * Mathf.Sin(r);
			Vector3 n = (tip - at).Cross(sr).Normalized();
			if (n.Y < 0) n = -n;
			float wb = w * wk * 0.8f, wt = w * wk * 1.2f;
			k.Quad(at - sr * wb * 0.5f, at + sr * wb * 0.5f, tip + sr * wt * 0.5f, tip - sr * wt * 0.5f, n,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		}
		Card(roll, 1f);
		if (crossed) Card(roll + Mathf.Pi * 0.5f, 0.7f);
	}

	/// <summary>The root flare (the fidelity pass, 2026-10-02): four to six roots spreading from the trunk's foot, each
	/// a tapering tube that rises out of the trunk a little above the ground and dives back into it a metre or so out,
	/// so the trees stand gripping the slope instead of planted in it like posts.</summary>
	internal static void Roots(MeshKit k, float r, int seed, Color col)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 6007 + 17) };
		int n = rng.RandiRange(4, 6);
		float a0 = rng.RandfRange(0f, Mathf.Tau);
		Color dark = col * 0.85f;
		dark.A = 1f;
		for (int i = 0; i < n; i++)
		{
			float a = a0 + Mathf.Tau * i / n + rng.RandfRange(-0.3f, 0.3f);
			Vector3 d = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 side = new(-d.Z, 0, d.X);
			float reach = r * rng.RandfRange(2.2f, 3.4f) + 0.3f;
			float bend = rng.RandfRange(-0.25f, 0.25f);
			float rr = r * rng.RandfRange(0.32f, 0.45f);
			// (2026-10-15, "spikey": each root came to a point lying on the ground, a claw round the foot) it now sinks as it
			// thins, its end well under the ground, so it reads as a root going in, not a spike laid on top
			TrunkLoft(k, new System.Collections.Generic.List<(Vector3, float, Color)>
			{
				(d * r * 0.55f + Vector3.Up * 0.42f, rr * 0.9f, col),
				(d * r * 1.05f + Vector3.Up * 0.16f, rr, col),
				(d * reach * 0.5f + side * bend * reach * 0.3f + Vector3.Down * 0.02f, rr * 0.72f, dark),
				(d * reach * 0.85f + side * bend * reach * 0.5f + Vector3.Down * rr * 0.75f, rr * 0.5f, dark),
				(d * reach + side * bend * reach * 0.6f + Vector3.Down * (rr + 0.25f), rr * 0.3f, dark),
			}, 6, 1f);
		}
	}

	/// <summary>A trunk as one smooth tube through <paramref name="rings"/> (bottom to top): per-vertex
	/// normals round and along it, bark UVs running on unbroken, vertex colours blended ring to ring.</summary>
	internal static void TrunkLoft(MeshKit k, System.Collections.Generic.List<(Vector3 c, float r, Color col)> rings, int sides, float uvScale, float lobes = 0f, int lobeSeed = 0)
	{
		// (2026-10-15, the owner: "fix the line cutting on the tree trunks") Each ring is now worked out once and shared by
		// the segments either side of it. Each segment used to make its own ring frame from its own axis, so wherever the
		// trunk bent the two rings at a joint didn't line up: a hairline crack the sky showed through. Each segment also
		// had its own taper normals and one flat colour, so every joint was a hard line in the shading and the tint. Now
		// the frame is carried up the trunk from ring to ring (it turns only as much as the trunk bends), each ring's
		// normals take the tangent and taper of both segments it joins, and the colour blends from one ring to the next.
		int n = rings.Count;
		if (n < 2) return;
		var tan = new Vector3[n];
		var slope = new float[n];
		var dirs = new Vector3[n][];
		for (int i = 0; i < n; i++)
		{
			var t = (rings[Mathf.Min(i + 1, n - 1)].c - rings[Mathf.Max(i - 1, 0)].c).Normalized();
			tan[i] = t;
			float sum = 0f; int cnt = 0;
			if (i > 0) { sum += (rings[i - 1].r - rings[i].r) / Mathf.Max(rings[i - 1].c.DistanceTo(rings[i].c), 0.001f); cnt++; }
			if (i < n - 1) { sum += (rings[i].r - rings[i + 1].r) / Mathf.Max(rings[i].c.DistanceTo(rings[i + 1].c), 0.001f); cnt++; }
			slope[i] = sum / cnt;
		}
		// the first ring's frame as before (so the bark sits where it did), then carried along: each ring's x is the last
		// one's with its component along this ring's tangent taken out
		Vector3 bx = tan[0].Cross(Mathf.Abs(tan[0].Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
		for (int i = 0; i < n; i++)
		{
			if (i > 0)
			{
				var x = bx - tan[i] * bx.Dot(tan[i]);
				bx = x.LengthSquared() > 1e-8f ? x.Normalized() : tan[i].Cross(Mathf.Abs(tan[i].Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
			}
			Vector3 bz = bx.Cross(tan[i]).Normalized();
			dirs[i] = new Vector3[sides + 1];
			for (int s = 0; s <= sides; s++)
			{
				float a = Mathf.Tau * s / sides;
				dirs[i][s] = bx * Mathf.Cos(a) + bz * Mathf.Sin(a);
			}
		}
		// (2026-10-15, "too blocky": a perfect cylinder) the cross-section made irregular: a few low lobes round the trunk
		// (a buttress, a flat side, a bulge), strongest at the foot and fading up it, the same at every ring so it runs on
		// unbroken. Its normals are then the surface's own (from the neighbouring points round and along), so the lobes
		// shade as the trunk's shape and the taper and flare need no separate term.
		var ph = new float[3];
		{
			var lr = new RandomNumberGenerator { Seed = (ulong)(lobeSeed * 2654435761L + 977) };
			for (int j = 0; j < 3; j++) ph[j] = lr.RandfRange(0f, Mathf.Tau);
		}
		float Lobe(int i, float a)
		{
			if (lobes <= 0f) return 0f;
			float up = n > 1 ? (float)i / (n - 1) : 0f;
			float amp = lobes * (0.3f + 0.7f * Mathf.Pow(1f - up, 2f));
			return amp * (0.5f * Mathf.Sin(3f * a + ph[0]) + 0.3f * Mathf.Sin(5f * a + ph[1]) + 0.35f * Mathf.Sin(2f * a + ph[2]));
		}
		var pts = new Vector3[n, sides + 1];
		for (int i = 0; i < n; i++)
			for (int s = 0; s <= sides; s++)
			{
				float a = Mathf.Tau * (s % sides) / sides;
				pts[i, s] = rings[i].c + dirs[i][s % sides] * rings[i].r * (1f + Lobe(i, a));
			}
		var nrm = new Vector3[n, sides + 1];
		for (int i = 0; i < n; i++)
			for (int s = 0; s <= sides; s++)
			{
				int sm = (s - 1 + sides) % sides, sp = (s + 1) % sides;
				Vector3 round = pts[i, sp] - pts[i, sm];
				Vector3 along = pts[Mathf.Min(i + 1, n - 1), s % sides] - pts[Mathf.Max(i - 1, 0), s % sides];
				Vector3 nn = along.Cross(round);
				Vector3 fallback = (dirs[i][s % sides] + tan[i] * slope[i]).Normalized();
				if (nn.LengthSquared() < 1e-12f) nn = fallback;
				nn = nn.Normalized();
				if (nn.Dot(dirs[i][s % sides]) < 0f) nn = -nn;
				// (a sliver of the old analytic normal keeps a ring that's nearly a point from going wild)
				nrm[i, s] = (nn * 0.85f + fallback * 0.15f).Normalized();
			}
		float v0 = 0f;
		for (int i = 0; i < n - 1; i++)
		{
			var col0 = rings[i].col;
			var col1 = rings[i + 1].col;
			float v1 = v0 + rings[i].c.DistanceTo(rings[i + 1].c) * uvScale * 0.5f;
			for (int s = 0; s < sides; s++)
			{
				Vector3 p00 = pts[i, s], p01 = pts[i, s + 1], p10 = pts[i + 1, s], p11 = pts[i + 1, s + 1];
				Vector3 n00 = nrm[i, s], n01 = nrm[i, s + 1], n10 = nrm[i + 1, s], n11 = nrm[i + 1, s + 1];
				float u0 = (float)s / sides * 2f, u1 = (float)(s + 1) / sides * 2f;
				k.TriC(p00, p10, p11, n00, n10, n11, new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), col0, col1, col1);
				k.TriC(p00, p11, p01, n00, n11, n01, new Vector2(u0, v0), new Vector2(u1, v1), new Vector2(u1, v0), col0, col1, col0);
			}
			v0 = v1;
		}
		k.Color = rings[n - 2].col;   // (as the old loft left it, for whatever is drawn after)
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
		k.Color = new Color(0.6f, 0.58f, 0.55f);   // (2026-10-13: was 0.75, with the bark's tint since lifted)
		float lean = rng.RandfRange(-0.25f, 0.25f);
		Vector3 top = new(lean, 4.2f, rng.RandfRange(-0.2f, 0.2f));
		k.Mat(ProcTextures.TreeBarkMat);
		TrunkLoft(k, new System.Collections.Generic.List<(Vector3, float, Color)>
		{
			(new Vector3(0, -0.4f, 0), 0.34f, k.Color), (new Vector3(0, 0.0f, 0), 0.29f, k.Color), (new Vector3(0, 0.35f, 0), 0.245f, k.Color),
			(top * 0.35f + new Vector3(0, 0.1f, 0), 0.2f, k.Color), (top, 0.13f, k.Color),
		}, 14, 1f, 0.06f, seed);   // (2026-10-15: fourteen sides, lobed)
		Roots(k, 0.29f, seed, k.Color);
		for (int b = 0; b < 3; b++)
		{
			float a = rng.RandfRange(0, Mathf.Tau);
			Vector3 from = top * rng.RandfRange(0.55f, 0.9f);
			Vector3 to = from + new Vector3(Mathf.Cos(a) * 1.6f, rng.RandfRange(1.0f, 1.8f), Mathf.Sin(a) * 1.6f);
			k.Cylinder(from, to, 0.09f, 0.04f, 5, false, 1f);
		}
		k.Cylinder(top, top + new Vector3(0, 1.6f, 0), 0.13f, 0.05f, 5, false);
		k.Mat(ProcTextures.TreeLeafMat);
		// (2026-10-15, the scan: the "floating slab" by the lake was this crown: five broad, flattened blobs fused into one
		// dark cap on a pole, seen through the fog.) Now clumps, smaller and taller than wide, at the ends of the limbs and
		// stacked up the leader at uneven heights, with sky between them, so it reads as a broadleaf's crown, not a disc.
		int blobs = 9;
		for (int i = 0; i < blobs; i++)
		{
			float a = Mathf.Tau * i / blobs * 2.3f + rng.RandfRange(-0.4f, 0.4f);
			float d = i < 2 ? rng.RandfRange(0f, 0.4f) : rng.RandfRange(0.8f, 1.9f);
			Vector3 c = top + new Vector3(Mathf.Cos(a) * d, i < 2 ? 1.2f + i * 1.3f : rng.RandfRange(-0.2f, 2.4f), Mathf.Sin(a) * d);
			float s = rng.RandfRange(0.7f, 1.0f);
			k.Color = new Color(s, s, s * 0.95f);
			float r = rng.RandfRange(0.7f, 1.15f);
			// (2026-10-15, "blocky": each clump was an 80-face ball knocked 34 % out of round, a cluster of faceted rocks
			// against the sky) four times the faces and a gentler knock, so the clumps are round and lumpy, not cut
			k.Blob(c, new Vector3(r, r * rng.RandfRange(0.9f, 1.25f), r * rng.RandfRange(0.8f, 1.1f)), seed * 13 + i, 0.2f, false, 0.6f, 0.6f, 2);
		}
		return k.Commit();
	}

	internal static Mesh SnagMesh(int seed, float h, float moss = 0f)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 31337) };
		var k = new MeshKit();
		float r = 0.2f + h * 0.012f;
		Vector3 top = new(0.02f * h, h, 0.01f * h);
		k.Color = new Color(0.72f, 0.72f, 0.72f);
		k.Mat(ProcTextures.TreeBarkMat);
		// one smooth trunk from its flare in the ground, bleached higher up where the bark is sloughing off
		TrunkLoft(k, new System.Collections.Generic.List<(Vector3, float, Color)>
		{
			// (2026-10-13: darker, 0.7 to 0.9 before: the bleached tops read as pale paper columns in the shade)
			(new Vector3(0, -0.4f, 0), r * 1.5f, new Color(0.56f, 0.55f, 0.54f)), (new Vector3(0, 0.0f, 0), r * 1.3f, new Color(0.58f, 0.57f, 0.56f)),
			(new Vector3(0, 0.4f, 0), r * 1.1f, new Color(0.58f, 0.57f, 0.56f)), (top * 0.5f, r * 0.7f, new Color(0.62f, 0.61f, 0.59f)),
			(top, r * 0.25f, new Color(0.66f, 0.65f, 0.62f)),
		}, 14, 1f, 0.06f, seed);   // (2026-10-15: fourteen sides, lobed)
		Roots(k, r * 1.3f, seed, new Color(0.56f, 0.55f, 0.54f));
		k.Color = new Color(0.62f, 0.6f, 0.56f);
		k.Mat(ProcTextures.TreeEndGrainMat).Cylinder(top, top + new Vector3(0.05f, 0.08f, 0), r * 0.25f, 0.02f, 6, false, 1f);   // broken top
		k.Mat(ProcTextures.TreeBarkMat);
		var mossRng = new RandomNumberGenerator { Seed = (ulong)(seed * 977 + 29) };
		int nb = Mathf.RoundToInt(h * 0.7f);
		for (int b = 0; b < nb; b++)
		{
			float y = rng.RandfRange(h * 0.3f, h * 0.92f);
			float a = rng.RandfRange(0, Mathf.Tau);
			float len = rng.RandfRange(0.5f, 1.8f) * (1.25f - y / h);
			Vector3 from = top * (y / h);
			Vector3 end = from + new Vector3(Mathf.Cos(a) * len, rng.RandfRange(-0.5f, 0.2f) * len, Mathf.Sin(a) * len);
			k.Cylinder(from, end, 0.055f, 0.01f, 4, false);
			if (moss > 0f && mossRng.Randf() < moss)
				MossCard(k, from.Lerp(end, mossRng.RandfRange(0.4f, 0.9f)), mossRng.RandfRange(0.6f, 1.6f), mossRng.RandfRange(0.3f, 0.5f), mossRng);
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

	/// <summary>(2026-10-15) A stump snapped off rather than sawn: taller, leaning a touch, its top a few splintered teeth
	/// round a ragged end.</summary>
	internal static Mesh StumpBrokenMesh()
	{
		var k = new MeshKit();
		var top = new Vector3(0.04f, 0.66f, 0.02f);
		k.Color = new Color(0.72f, 0.7f, 0.66f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(0, -0.3f, 0), top, 0.37f, 0.27f, 8, false, 1f);
		k.Color = new Color(0.62f, 0.56f, 0.48f);
		k.Mat(ProcTextures.EndGrainMat).Cylinder(top - new Vector3(0, 0.02f, 0), top + new Vector3(0, 0.005f, 0), 0.268f, 0.25f, 8, true, 1.7f);
		// the splinters: tapering teeth standing up off the rim, the tallest on one side (where it tore)
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.6f, 0.55f, 0.48f);
		float[] ang = { 0.3f, 1.4f, 2.2f, 3.9f, 5.1f };
		float[] len = { 0.38f, 0.22f, 0.3f, 0.12f, 0.17f };
		for (int i = 0; i < ang.Length; i++)
		{
			var d = new Vector3(Mathf.Cos(ang[i]), 0, Mathf.Sin(ang[i]));
			Vector3 b = top + d * 0.19f;
			k.Cylinder(b, b + Vector3.Up * len[i] + d * 0.03f, 0.075f, 0.008f, 4, false, 2f);
		}
		return k.Commit();
	}

	/// <summary>(2026-10-15) A log as long as <see cref="LogMesh"/> (the same collider fits it), its far end broken off in
	/// splinters rather than sawn, two snags of branch along it, a different taper.</summary>
	internal static Mesh LogBrokenMesh()
	{
		var k = new MeshKit();
		k.Color = new Color(0.58f, 0.55f, 0.5f);
		k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(-2.6f, 0, 0.05f), new Vector3(2.45f, 0.02f, -0.08f), 0.25f, 0.22f, 7, false, 1f);
		k.Mat(ProcTextures.EndGrainMat).Cylinder(new Vector3(-2.62f, 0, 0.05f), new Vector3(-2.6f, 0, 0.05f), 0.245f, 0.245f, 7, true, 1.8f);
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.55f, 0.5f, 0.44f);
		foreach (var (a, l) in new[] { (0.4f, 0.35f), (1.5f, 0.22f), (2.6f, 0.3f), (4.1f, 0.18f), (5.3f, 0.26f) })
		{
			var d = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a)) * 0.15f;
			Vector3 b = new Vector3(2.4f, 0.02f, -0.08f) + d;
			k.Cylinder(b, b + new Vector3(l, d.Y * 0.4f, d.Z * 0.4f), 0.06f, 0.008f, 4, false, 2f);
		}
		k.Cylinder(new Vector3(-0.9f, 0.12f, 0.1f), new Vector3(-1.1f, 0.5f, 0.45f), 0.05f, 0.015f, 4, false);
		k.Cylinder(new Vector3(1.2f, 0.15f, -0.12f), new Vector3(1.45f, 0.3f, -0.55f), 0.04f, 0.012f, 4, false);
		return k.Commit();
	}

	/// <summary>(2026-10-15) A forked branch: two limbs off one butt, twigs off them.</summary>
	internal static Mesh BranchForkMesh()
	{
		var k = new MeshKit();
		k.Color = new Color(0.5f, 0.47f, 0.43f);
		k.Mat(ProcTextures.BarkMat);
		k.Cylinder(new Vector3(-0.8f, 0.04f, 0.05f), new Vector3(0.05f, 0.045f, 0f), 0.05f, 0.035f, 4, false, 2f);
		k.Cylinder(new Vector3(0.05f, 0.045f, 0f), new Vector3(0.85f, 0.035f, 0.32f), 0.035f, 0.012f, 4, false, 2f);
		k.Cylinder(new Vector3(0.05f, 0.045f, 0f), new Vector3(0.75f, 0.03f, -0.36f), 0.03f, 0.01f, 4, false, 2f);
		k.Cylinder(new Vector3(0.45f, 0.04f, 0.16f), new Vector3(0.6f, 0.06f, 0.5f), 0.015f, 0.006f, 4, false, 2f);
		k.Cylinder(new Vector3(-0.35f, 0.045f, 0.03f), new Vector3(-0.5f, 0.05f, -0.25f), 0.018f, 0.007f, 4, false, 2f);
		return k.Commit();
	}

	/// <summary>A number in [0, 1) from a position (the scatter's variety that mustn't draw on its own stream: the trees
	/// and everything after them would move).</summary>
	private static float Hash01(Vector3 p, float salt)
	{
		float h = Mathf.Sin(p.X * 12.9898f + p.Z * 78.233f + salt * 37.719f) * 43758.5453f;
		return h - Mathf.Floor(h);
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
			DetailKit.Hook(m, DetailKit.Kind.Foliage);
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

	/// <summary>A patch of leaf litter and mud (the fidelity pass, 2026-10-02): a flat soft-edged square, a little
	/// domed so its middle never sinks under a bump, drawn with ground_litter.gdshader.</summary>
	internal static Mesh LitterMesh()
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.Cached("litter_mat", () =>
		{
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/ground_litter.gdshader") };
			m.SetShaderParameter("albedo_tex", GD.Load<Texture2D>("res://assets/textures/surfaces/forest_floor_albedo.png"));
			return m;
		}));
		const int N = 4;
		const float S = 1.6f;
		Vector3 P(int i, int j)
		{
			float u = (float)i / N * 2f - 1f, v = (float)j / N * 2f - 1f;
			return new Vector3(u * S * 0.5f, 0.05f * (1f - 0.5f * (u * u + v * v)), v * S * 0.5f);
		}
		k.Color = Colors.White;
		for (int i = 0; i < N; i++)
			for (int j = 0; j < N; j++)
				k.Quad(P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1), Vector3.Up,
					new Vector2((float)i / N, (float)j / N), new Vector2((float)(i + 1) / N, (float)j / N),
					new Vector2((float)(i + 1) / N, (float)(j + 1) / N), new Vector2((float)i / N, (float)(j + 1) / N));
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
				// moss hanging from the dead branches of a share of them, more deep in
				if (mesh is "fir_giant" or "fir_tall" or "snag" or "snag_tall" && _rng.Randf() < 0.3f + 0.35f * deep) mesh += "_moss";
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
					// clear of the trunk and its root flare, so the two never run into each other
					float sa = _rng.RandfRange(0, Mathf.Tau), sd = trunkR * scale * 1.6f + 0.55f + _rng.RandfRange(0f, 1.2f);
					Vector3 sp = pos + new Vector3(Mathf.Cos(sa) * sd, 0, Mathf.Sin(sa) * sd);
					_terrain.SampleFields(sp.X, sp.Z, out float sdT, out _, out float sdS, out _);
					if (sdT > 2.2f && sdS > 3f && _terrain.RouteDistance(sp.X, sp.Z) > 2f && _terrain.SampleBranch(sp.X, sp.Z, out float sbh) > sbh + 1.2f)
					{
						sp.Y = _terrain.HeightAt(sp.X, sp.Z);
						// (2026-10-15) two kinds, each its own size and shade (a hash of the spot: the stream stays as it was)
						float ss = 0.8f + 0.4f * Hash01(sp, 2f), st = 0.86f + 0.22f * Hash01(sp, 3f);
						Add(Hash01(sp, 1f) < 0.45f ? "stump_b" : "stump", TreeChunk, sp, Basis.FromEuler(new Vector3(0, _rng.RandfRange(0, 6.28f), 0)).Scaled(Vector3.One * ss), new Color(st, st * 0.97f, st * 0.93f));
						AddCollider(sp, Cylinder(0.32f * ss, 1f), new Transform3D(Basis.Identity, sp));
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
					// (2026-10-15: deeper on crests, where the ground audit still found three logs hanging)
					float bed = Mathf.Clamp(0.2f * across - 0.05f, 0f, 0.28f) * s;
					var pos = new Vector3(px, (hA + hB) * 0.5f + dip + 0.12f * s - bed, pz);
					float lt = 0.88f + 0.22f * Hash01(pos, 5f);
					Add(Hash01(pos, 4f) < 0.45f ? "log_b" : "log", TreeChunk, pos, basis.Scaled(Vector3.One * s), new Color(lt, lt * 0.98f, lt * 0.94f));
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
					var bp = new Vector3(px, h - 0.01f, pz);
					float bt = 0.85f + 0.25f * Hash01(bp, 7f);
					Add(Hash01(bp, 6f) < 0.5f ? "branch_b" : "branch", TreeChunk, bp, basis.Scaled(Vector3.One * s), new Color(bt, bt * 0.97f, bt * 0.92f));
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

	/// <summary>The litter's own dice (so adding it moved none of the ferns and the grass).</summary>
	private readonly RandomNumberGenerator _litterRng = new() { Seed = 7331 };

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
				// leaf litter and mud under the trees (the fidelity pass): patches of their own, wherever the ferns grow
				if (mesh == "fern" && _litterRng.Randf() < 0.16f + 0.2f * clump)
				{
					Vector3 nUp = _terrain.NormalAt(px, pz);
					if (nUp.Y > 0.8f)
					{
						float ls = _litterRng.RandfRange(0.8f, 1.5f);
						var lb = new Basis(new Quaternion(Vector3.Up, nUp.Normalized())) * Basis.FromEuler(new Vector3(0, _litterRng.RandfRange(0, Mathf.Tau), 0)).Scaled(new Vector3(ls, 1f, ls));
						float lt = _litterRng.RandfRange(0.7f, 1.0f);
						Add("litter", FoliageChunk, new Vector3(px, _terrain.HeightAt(px, pz) - 0.012f, pz), lb, new Color(lt, lt * 0.97f, lt * 0.93f));
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
			bool foliage = meshKey is "fern" or "grass" or "litter" || meshKey.StartsWith("palmetto");
			bool small = foliage || meshKey.StartsWith("branch") || meshKey.StartsWith("stump");
			bool under = meshKey.StartsWith("sapling") || meshKey.StartsWith("vinemound");
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
					VisibilityRangeEnd = (foliage ? FoliageViewDistance : (small ? 70f : under ? 85f : TreeViewDistance)) + chunk * 0.7f,
					// only the trees throw shadows: the branches, stumps and ground cover were half the shadow pass
					// for shadows no one could see under the canopy's (performance pass, 2026-09-27)
					CastShadow = small || meshKey.StartsWith("sapling") ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
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
