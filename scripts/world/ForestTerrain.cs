using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The forest ground: a valley heightfield that follows the trail, with a
/// flattened trailhead/parking pad, a clearing at the end of the trail and a
/// stream channel carved across it.
///
/// Inputs are scene nodes so a designer can move them:
///  - TrailPath  (Path3D, group "trail"): trail control points, trailhead → clearing edge.
///    At runtime the curve is rewritten as dense points at ground height.
///  - RoadPath   (Path3D): gravel access road leaving the parking pad.
///  - StreamPath (Node3D): Marker3D children, upstream → downstream. They double as
///    the "stream_audio_points" and are snapped to the water surface.
///
/// API: HeightAt(x, z) matches the collision exactly (same triangles).
/// All generation is deterministic from Seed.
/// </summary>
[GlobalClass]
public partial class ForestTerrain : Node3D
{
	[Export] public int Seed = 4127;
	[Export] public Vector2 MinXZ = new(-120, -360);
	[Export] public Vector2 MaxXZ = new(120, 60);
	[Export] public float CellSize = 1f;
	[Export] public NodePath TrailPath = "Trail";
	[Export] public NodePath RoadPath = "Road";
	[Export] public NodePath StreamPath = "Stream";
	/// <summary>Extra Path3Ds treated as valley floor (low, gentle ground) but not drawn as trail: sight lines, side glades.</summary>
	[Export] public NodePath[] ValleyPaths = System.Array.Empty<NodePath>();

	[ExportGroup("Trail")]
	[Export] public float TrailWidth = 1.8f;
	[Export] public float TrailWidthDeep = 1.2f;
	[Export] public float NarrowStart = 185f;
	[Export] public float NarrowEnd = 230f;
	[Export] public float RoadWidth = 4.5f;
	/// <summary>Spacing of the rewritten runtime Path3D points.</summary>
	[Export] public float PathPointSpacing = 4f;

	[ExportGroup("Branches")]
	/// <summary>
	/// Side paths (Path3D, group "trail_branch"), local to this node. A branch whose first
	/// point lies within 12 m of the main trail is snapped onto it. Each branch is drawn as
	/// a narrower trail cut into the natural slope (it doesn't carve a valley).
	/// </summary>
	[Export] public NodePath[] BranchPaths = System.Array.Empty<NodePath>();
	/// <summary>Full width at the start of each branch (m).</summary>
	[Export] public float[] BranchWidths = System.Array.Empty<float>();
	/// <summary>Fraction of each branch's length where it starts to fade into the forest floor (>= 1: a real dead end, no fade).</summary>
	[Export] public float[] BranchFadeFrom = System.Array.Empty<float>();
	/// <summary>
	/// Hidden test route (Path3D, group "autotest_route"). Its authored points are the
	/// cross-country leg only; at runtime it is rewritten as trail → branch AutotestBranch →
	/// those points, at ground height. Trees/rocks keep clear of the cross-country leg.
	/// </summary>
	[Export] public NodePath AutotestRoutePath = "AutotestRoute";
	[Export] public int AutotestBranch = -1;

	[ExportGroup("Areas")]
	/// <summary>The gap where the staircase stands (flattened; trees kept out of it).</summary>
	[Export] public Vector2 ClearingCenter = new(0, -302);
	[Export] public float ClearingRadius = 20f;
	/// <summary>Paint the clearing as meadow grass (the old mown clearing). Off = natural forest floor.</summary>
	[Export] public bool ClearingGrass = true;
	[Export] public Rect2 ParkingRect = new(-11, 23, 22, 13);
	/// <summary>Broad, gently rolling low forest floor (x, z, radius) where the trail breaks up. Radius 0 = none.</summary>
	[Export] public Vector3 Basin = Vector3.Zero;
	/// <summary>Round hills added on top of everything else: (x, z, height, radius).</summary>
	[Export] public Vector4[] Hills = System.Array.Empty<Vector4>();

	[ExportGroup("Shape")]
	[Export] public float ValleyDepth = 17f;
	[Export] public float ValleyWidth = 34f;
	[Export] public float NorthRise = 0.016f;
	[Export] public float StreamDepth = 1.35f;
	/// <summary>World Z range over which the canopy shade on the ground deepens.</summary>
	[Export] public Vector2 DeepShadeZ = new(-160f, -240f);

	[ExportGroup("Build")]
	[Export] public bool BuildBoundaryWalls = true;
	[Export] public float BoundaryInset = 2f;
	[Export] public int ChunkCells = 48;

	// ---- data ----
	private bool _ready;
	private int _nx, _nz;            // cells
	private float[] _h;              // (nx+1)*(nz+1)
	private float[] _dT, _sT, _dS, _sS, _dR, _dV;
	private Polyline2 _trail, _road, _stream;
	private float[] _bed;            // stream bed per metre of stream arc length
	private FastNoiseLite _nBig, _nMid, _nFine, _nEdge;
	private float _parkH, _clearH;
	private readonly List<Polyline2> _branches = new();
	private float[] _brHalf0 = System.Array.Empty<float>(), _brFade = System.Array.Empty<float>();
	private float[] _dB, _sB, _iB, _dA;   // nearest-branch distance / arc / index, route distance
	private Polyline2 _route;              // cross-country leg of the test route (incl. its lead-in)
	private const float BranchR = 14f, RouteR = 8f;

	public Polyline2 Trail { get { EnsureData(); return _trail; } }
	public int BranchCount { get { EnsureData(); return _branches.Count; } }
	public Polyline2 Branch(int i) { EnsureData(); return _branches[i]; }
	public Polyline2 Stream { get { EnsureData(); return _stream; } }
	public float TrailLength { get { EnsureData(); return _trail.Length; } }

	public override void _EnterTree() => AddToGroup("terrain");

	public override void _Ready()
	{
		EnsureData();
		BuildMesh();
		BuildCollision();
		BuildWater();
		if (BuildBoundaryWalls) BuildWalls();
		UpdateSceneMarkers();
	}

	// =====================================================================
	// Public API
	// =====================================================================

	/// <summary>Ground height at world x,z. Identical to the collision surface.</summary>
	public float HeightAt(float x, float z)
	{
		EnsureData();
		Vector3 o = GlobalPosition;
		float gx = (x - o.X - MinXZ.X) / CellSize, gz = (z - o.Z - MinXZ.Y) / CellSize;
		gx = Mathf.Clamp(gx, 0, _nx - 0.0001f);
		gz = Mathf.Clamp(gz, 0, _nz - 0.0001f);
		int i = (int)gx, j = (int)gz;
		float fx = gx - i, fz = gz - j;
		float h00 = H(i, j), h10 = H(i + 1, j), h01 = H(i, j + 1), h11 = H(i + 1, j + 1);
		float y = fx >= fz
			? h00 + (h10 - h00) * fx + (h11 - h10) * fz
			: h00 + (h11 - h01) * fx + (h01 - h00) * fz;
		return y + o.Y;
	}

	public Vector3 GroundPoint(float x, float z) => new(x, HeightAt(x, z), z);

	/// <summary>Terrain normal (from the triangle-interpolated surface, finite differences).</summary>
	public Vector3 NormalAt(float x, float z)
	{
		const float e = 0.5f;
		float hx = HeightAt(x + e, z) - HeightAt(x - e, z);
		float hz = HeightAt(x, z + e) - HeightAt(x, z - e);
		return new Vector3(-hx, 2 * e, -hz).Normalized();
	}

	/// <summary>Horizontal distance to the trail centreline and arc length of the closest point.</summary>
	public float TrailDistance(float x, float z, out float along)
	{
		EnsureData();
		return _trail.Closest(ToLocal2(x, z), out along);
	}

	/// <summary>Coarse (1 m grid) distance to trail / road / stream, fast enough for scattering.</summary>
	public void SampleFields(float x, float z, out float dTrail, out float sTrail, out float dStream, out float dRoad)
	{
		EnsureData();
		Vector2 l = ToLocal2(x, z);
		int i = Mathf.Clamp(Mathf.RoundToInt((l.X - MinXZ.X) / CellSize), 0, _nx);
		int j = Mathf.Clamp(Mathf.RoundToInt((l.Y - MinXZ.Y) / CellSize), 0, _nz);
		int k = j * (_nx + 1) + i;
		dTrail = _dT[k]; sTrail = _sT[k]; dStream = _dS[k]; dRoad = _dR[k];
	}

	/// <summary>
	/// Coarse distance to the nearest side branch, and that branch's current half-width
	/// there (shrinks to ~0 where a faint path has faded away). Far away: 999 / 0.
	/// </summary>
	public float SampleBranch(float x, float z, out float halfWidth)
	{
		EnsureData();
		halfWidth = 0f;
		if (_branches.Count == 0) return 999f;
		int k = GridIndex(x, z);
		if (_dB[k] >= BranchR) return 999f;
		halfWidth = BranchHalf((int)_iB[k], _sB[k]);
		return _dB[k];
	}

	/// <summary>Coarse distance to the cross-country leg of the hidden test route (999 far away / none).</summary>
	public float RouteDistance(float x, float z)
	{
		EnsureData();
		if (_dA == null) return 999f;
		float d = _dA[GridIndex(x, z)];
		return d >= RouteR ? 999f : d;
	}

	/// <summary>0..1: how much of a path a branch still is at arc length s (1 = full trail, 0 = faded into the forest).</summary>
	public float BranchStrength(int i, float s)
	{
		var b = _branches[i];
		float f = i < _brFade.Length ? _brFade[i] : 1f;
		if (f >= 1f) return 1f - Mathf.SmoothStep(b.Length - 3f, b.Length + 1f, s);
		return 1f - Mathf.SmoothStep(b.Length * f, b.Length, s);
	}

	public float BranchHalf(int i, float s)
	{
		float h0 = 0.5f * (i < _brHalf0.Length ? _brHalf0[i] : 1.2f);
		float st = BranchStrength(i, s);
		return h0 * (0.3f + 0.7f * st) * Mathf.SmoothStep(0f, 0.25f, st + 0.05f);
	}

	private int GridIndex(float x, float z)
	{
		Vector2 l = ToLocal2(x, z);
		int i = Mathf.Clamp(Mathf.RoundToInt((l.X - MinXZ.X) / CellSize), 0, _nx);
		int j = Mathf.Clamp(Mathf.RoundToInt((l.Y - MinXZ.Y) / CellSize), 0, _nz);
		return j * (_nx + 1) + i;
	}

	/// <summary>World-space point on the trail at arc length s (ground height), plus horizontal tangent.</summary>
	public Vector3 TrailPoint(float s, out Vector3 tangent)
	{
		EnsureData();
		Vector2 p = _trail.At(s, out Vector2 t);
		tangent = new Vector3(t.X, 0, t.Y);
		Vector3 o = GlobalPosition;
		return GroundPoint(p.X + o.X, p.Y + o.Z);
	}

	public float TrailHalfWidth(float s)
		=> 0.5f * Mathf.Lerp(TrailWidth, TrailWidthDeep, Mathf.SmoothStep(NarrowStart, NarrowEnd, s))
		   * (1f - Mathf.SmoothStep(_trail.Length - 6f, _trail.Length + 2f, s) * 0.6f);

	/// <summary>Water surface height at stream arc length s.</summary>
	public float WaterLevel(float s)
	{
		EnsureData();
		return Bed(s) + 0.3f + GlobalPosition.Y;
	}

	/// <summary>Where the trail crosses the stream (world, ground level), with the trail's tangent there.</summary>
	public bool TryGetStreamCrossing(out Vector3 pos, out Vector3 trailDir, out float trailS)
	{
		EnsureData();
		pos = Vector3.Zero; trailDir = Vector3.Forward; trailS = 0;
		if (_stream.Points.Count < 2) return false;
		float best = float.MaxValue;
		for (float s = 0; s <= _trail.Length; s += 0.25f)
		{
			Vector2 p = _trail.At(s, out _);
			float d = _stream.Closest(p, out _);
			if (d < best) { best = d; trailS = s; }
		}
		if (best > 3f) return false;
		pos = TrailPoint(trailS, out trailDir);
		return true;
	}

	// =====================================================================
	// Generation
	// =====================================================================

	private float H(int i, int j) => _h[j * (_nx + 1) + i];
	private Vector2 ToLocal2(float x, float z) { var o = GlobalPosition; return new Vector2(x - o.X, z - o.Z); }

	private List<Vector2> ReadPathPoints(NodePath path)
	{
		var list = new List<Vector2>();
		var node = GetNodeOrNull<Node3D>(path);
		if (node is Path3D p3 && p3.Curve != null)
		{
			for (int i = 0; i < p3.Curve.PointCount; i++)
			{
				Vector3 v = p3.Transform * p3.Curve.GetPointPosition(i);
				list.Add(new Vector2(v.X, v.Z));
			}
		}
		else if (node != null)
		{
			foreach (var c in node.GetChildren())
				if (c is Node3D n3)
				{
					Vector3 v = node.Transform * n3.Position;
					list.Add(new Vector2(v.X, v.Z));
				}
		}
		return list;
	}

	private float FloorAt(float z) => -z * NorthRise
		// the trail dips gently toward the stream and climbs out of it
		- 1.2f * Mathf.Exp(-Mathf.Pow((z + 160f) / 30f, 2f));

	private float RectDist(Vector2 p, Rect2 r)
	{
		float dx = Mathf.Max(Mathf.Max(r.Position.X - p.X, p.X - r.End.X), 0);
		float dz = Mathf.Max(Mathf.Max(r.Position.Y - p.Y, p.Y - r.End.Y), 0);
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	/// <summary>Uncarved terrain height at local x,z given precomputed distances.</summary>
	private float H0(float x, float z, float dT, float sT, float dR, float dV = 999f)
	{
		Vector2 p = new(x, z);
		float half = _trail != null ? TrailHalfWidth(sT) : 0.9f;
		float dRoadEff = Mathf.Max(0, dR - (RoadWidth * 0.5f - half));
		float dPark = RectDist(p, ParkingRect);
		float dClear = Mathf.Max(0, p.DistanceTo(ClearingCenter) - ClearingRadius * 0.8f);
		float d = Mathf.Min(Mathf.Min(Mathf.Min(dT, dRoadEff), Mathf.Min(dPark + 1f, dClear + 1f)), dV + 1.5f);
		// the deep woods open out into a broad, gently rolling floor where the trail breaks up
		if (Basin.Z > 0f) d = Mathf.Min(d, 6.5f + Mathf.Max(0f, p.DistanceTo(new Vector2(Basin.X, Basin.Y)) - Basin.Z));

		float dd = Mathf.Max(0, d - 2.5f);
		float rise = ValleyDepth * (1f - Mathf.Exp(-(dd / ValleyWidth) * (dd / ValleyWidth))) + 0.07f * dd;
		float big = _nBig.GetNoise2D(x, z) * 6f * Mathf.SmoothStep(6f, 40f, d);
		float w = 0.22f + 0.78f * Mathf.SmoothStep(1.5f, 10f, d);
		float mid = _nMid.GetNoise2D(x, z) * 1.4f * w;
		float fine = _nFine.GetNoise2D(x, z) * 0.22f * Mathf.SmoothStep(1.2f, 3.5f, d);
		float h = FloorAt(z) + rise + big + mid + fine;
		foreach (var hill in Hills)
		{
			float r2 = (x - hill.X) * (x - hill.X) + (z - hill.Y) * (z - hill.Y);
			h += hill.Z * Mathf.Exp(-r2 / (2f * hill.W * hill.W));
		}

		// worn trail bed
		h -= 0.09f * (1f - Mathf.SmoothStep(half * 0.5f, half + 0.5f, dT));

		// parking pad: flat with a slight crown
		float tp = 1f - Mathf.SmoothStep(0f, 5f, dPark);
		if (tp > 0) h = Mathf.Lerp(h, _parkH, tp);

		// clearing: nearly flat meadow
		float rc = p.DistanceTo(ClearingCenter);
		float tc = 1f - Mathf.SmoothStep(ClearingRadius * 0.7f, ClearingRadius * 1.2f, rc);
		if (tc > 0) h = Mathf.Lerp(h, _clearH + _nMid.GetNoise2D(x * 2f, z * 2f) * 0.18f * Mathf.SmoothStep(3f, 12f, rc), tc);
		return h;
	}

	private float Bed(float s)
	{
		if (_bed == null || _bed.Length == 0) return 0;
		float f = Mathf.Clamp(s, 0, _bed.Length - 1.001f);
		int i = (int)f;
		return Mathf.Lerp(_bed[i], _bed[i + 1], f - i);
	}

	private static float SMin(float a, float b, float k)
	{
		float h = Mathf.Clamp(0.5f + 0.5f * (b - a) / k, 0, 1);
		return Mathf.Lerp(b, a, h) - k * h * (1 - h);
	}

	private float Carve(float h, float dS, float sS)
	{
		if (_stream.Points.Count < 2 || dS > 120f) return h;
		float bed = Bed(sS);
		float carve = bed + 0.95f * Mathf.Clamp(dS - 1.2f, 0, 2.2f) + 0.42f * Mathf.Max(0, dS - 3.4f);
		return SMin(h, carve, 0.7f);
	}

	private void EnsureData()
	{
		if (_ready) return;
		_ready = true;

		_nBig = new FastNoiseLite { Seed = Seed, Frequency = 0.011f, FractalOctaves = 3, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
		_nMid = new FastNoiseLite { Seed = Seed + 1, Frequency = 0.035f, FractalOctaves = 2, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
		_nFine = new FastNoiseLite { Seed = Seed + 2, Frequency = 0.18f, FractalOctaves = 2, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
		_nEdge = new FastNoiseLite { Seed = Seed + 3, Frequency = 0.45f, FractalOctaves = 2, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };

		var tp = ReadPathPoints(TrailPath);
		if (tp.Count < 2) { tp.Clear(); tp.Add(new Vector2(0, 20)); tp.Add(new Vector2(0, -290)); }
		_trail = Polyline2.CatmullRom(tp, 0.5f);
		_road = Polyline2.CatmullRom(ReadPathPoints(RoadPath), 0.5f);
		_stream = Polyline2.CatmullRom(ReadPathPoints(StreamPath), 0.5f);

		_nx = Mathf.CeilToInt((MaxXZ.X - MinXZ.X) / CellSize);
		_nz = Mathf.CeilToInt((MaxXZ.Y - MinXZ.Y) / CellSize);
		int w = _nx + 1, h = _nz + 1, n = w * h;
		_h = new float[n];
		_dT = new float[n]; _sT = new float[n]; _dS = new float[n]; _sS = new float[n]; _dR = new float[n]; _dV = new float[n];
		const float Far = 200f;
		System.Array.Fill(_dT, Far); System.Array.Fill(_dS, Far); System.Array.Fill(_dR, Far); System.Array.Fill(_dV, Far);
		foreach (var vp in ValleyPaths)
		{
			var pl = Polyline2.CatmullRom(ReadPathPoints(vp), 1f);
			if (pl.Points.Count > 1) pl.Raster(MinXZ, CellSize, w, h, 60f, _dV, null);
		}
		_trail.Decimate(4).Raster(MinXZ, CellSize, w, h, Far, _dT, _sT);
		if (_road.Points.Count > 1) _road.Decimate(4).Raster(MinXZ, CellSize, w, h, Far, _dR, null);
		if (_stream.Points.Count > 1) _stream.Decimate(4).Raster(MinXZ, CellSize, w, h, 130f, _dS, _sS);

		// reference heights for the flattened areas
		_parkH = FloorAt(ParkingRect.GetCenter().Y) + 0.05f;
		_clearH = FloorAt(ClearingCenter.Y) + 0.1f;

		// stream bed: follow the terrain downhill, never climbing
		if (_stream.Points.Count > 1)
		{
			int m = Mathf.CeilToInt(_stream.Length) + 2;
			_bed = new float[m];
			float prev = float.MaxValue;
			for (int k = 0; k < m; k++)
			{
				Vector2 p = _stream.At(k, out _);
				float dT = _trail.Closest(p, out float sT);
				float dR = _road.Points.Count > 1 ? _road.Closest(p, out _) : Far;
				float g = H0(p.X, p.Y, dT, sT, dR) - StreamDepth;
				prev = Mathf.Min(prev - 0.004f, g);
				_bed[k] = prev;
			}
		}

		for (int j = 0; j < h; j++)
			for (int i = 0; i < w; i++)
			{
				int k = j * w + i;
				float x = MinXZ.X + i * CellSize, z = MinXZ.Y + j * CellSize;
				float y = H0(x, z, _dT[k], _sT[k], _dR[k], _dV[k]);
				_h[k] = Carve(y, _dS[k], _sS[k]);
			}

		BuildBranches(w, h);
	}

	/// <summary>Read the side branches and the test route, raster their fields, and bench the branches into the slope.</summary>
	private void BuildBranches(int w, int h)
	{
		int n = w * h;
		_dB = new float[n]; _sB = new float[n]; _iB = new float[n];
		System.Array.Fill(_dB, BranchR);
		_brHalf0 = new float[BranchPaths.Length];
		_brFade = new float[BranchPaths.Length];
		var tmpD = new float[n]; var tmpS = new float[n];
		for (int bi = 0; bi < BranchPaths.Length; bi++)
		{
			var pts = ReadPathPoints(BranchPaths[bi]);
			if (pts.Count < 2) { _branches.Add(new Polyline2()); continue; }
			// snap the first point onto the main trail so the fork joins cleanly
			float dj = _trail.Closest(pts[0], out float sj);
			if (dj < 12f) pts[0] = _trail.At(sj, out _);
			var pl = Polyline2.CatmullRom(pts, 0.5f);
			_branches.Add(pl);
			_brHalf0[bi] = bi < BranchWidths.Length ? BranchWidths[bi] : 1.2f;
			_brFade[bi] = bi < BranchFadeFrom.Length ? BranchFadeFrom[bi] : 1f;

			System.Array.Fill(tmpD, BranchR);
			var dec = pl.Decimate(2);
			dec.Raster(MinXZ, CellSize, w, h, BranchR, tmpD, tmpS);
			// smoothed centreline profile of the natural ground, 1 m steps
			int m = Mathf.CeilToInt(pl.Length) + 1;
			var prof = new float[m];
			for (int s = 0; s < m; s++) { Vector2 p = pl.At(s, out _); prof[s] = HeightLocal(p.X, p.Y); }
			for (int pass = 0; pass < 3; pass++)
			{
				var cp = (float[])prof.Clone();
				for (int s = 0; s < m; s++)
				{
					float acc = 0; int cnt = 0;
					for (int o = -5; o <= 5; o++) { int q = Mathf.Clamp(s + o, 0, m - 1); acc += cp[q]; cnt++; }
					prof[s] = acc / cnt;
				}
			}
			// bench: pull the ground toward the smoothed profile across the path (a cut into the slope)
			for (int k = 0; k < n; k++)
			{
				float d = tmpD[k];
				if (d >= BranchR) continue;
				if (d < _dB[k]) { _dB[k] = d; _sB[k] = tmpS[k]; _iB[k] = bi; }
				float s = tmpS[k];
				float half = BranchHalf(bi, s), st = BranchStrength(bi, s);
				float hw = Mathf.Max(half, 0.5f * _brHalf0[bi] * 0.6f);
				float t = 1f - Mathf.SmoothStep(hw * 0.8f, hw + 2.6f, d);
				if (t <= 0f) continue;
				// don't fight the main trail right at the junction
				t *= Mathf.SmoothStep(1.5f, 4f, _dT[k]);
				// let the path run out onto natural ground at its end (no notch cut into a hilltop)
				t *= _brFade[bi] >= 1f ? 1f - Mathf.SmoothStep(pl.Length - 14f, pl.Length - 2f, s) : 0.3f + 0.7f * st;
				float target = SampleProfile(prof, s) - 0.07f * st * (1f - Mathf.SmoothStep(half * 0.5f, half + 0.5f, d));
				_h[k] = Mathf.Lerp(_h[k], target, t);
			}
		}

		// hidden test route: only its cross-country leg matters here
		if (GetNodeOrNull<Path3D>(AutotestRoutePath) != null)
		{
			var rp = ReadPathPoints(AutotestRoutePath);
			Polyline2 lead = AutotestBranch >= 0 && AutotestBranch < _branches.Count && _branches[AutotestBranch].Points.Count > 1 ? _branches[AutotestBranch] : null;
			if (lead != null) rp.Insert(0, lead.Points[^1]);
			if (rp.Count > 1)
			{
				_route = Polyline2.CatmullRom(rp, 0.5f);
				_dA = new float[n];
				System.Array.Fill(_dA, RouteR);
				_route.Decimate(2).Raster(MinXZ, CellSize, w, h, RouteR, _dA, null);
				// the branch it follows fades out, so keep its lane open too (from where it starts to fade)
				if (lead != null) lead.Decimate(2).Raster(MinXZ, CellSize, w, h, RouteR, _dA, null);
			}
		}
	}

	private static float SampleProfile(float[] prof, float s)
	{
		float f = Mathf.Clamp(s, 0, prof.Length - 1.001f);
		int i = (int)f;
		return Mathf.Lerp(prof[i], prof[i + 1], f - i);
	}

	/// <summary>Bilinear height from the grid at local x,z (for generation passes).</summary>
	private float HeightLocal(float x, float z)
	{
		float gx = Mathf.Clamp((x - MinXZ.X) / CellSize, 0, _nx - 0.0001f), gz = Mathf.Clamp((z - MinXZ.Y) / CellSize, 0, _nz - 0.0001f);
		int i = (int)gx, j = (int)gz;
		float fx = gx - i, fz = gz - j;
		return Mathf.Lerp(Mathf.Lerp(H(i, j), H(i + 1, j), fx), Mathf.Lerp(H(i, j + 1), H(i + 1, j + 1), fx), fz);
	}

	// =====================================================================
	// Mesh / collision / water
	// =====================================================================

	private Vector3 GridNormal(int i, int j)
	{
		float hl = H(Mathf.Max(i - 1, 0), j), hr = H(Mathf.Min(i + 1, _nx), j);
		float hd = H(i, Mathf.Max(j - 1, 0)), hu = H(i, Mathf.Min(j + 1, _nz));
		return new Vector3(-(hr - hl), 2f * CellSize, -(hu - hd)).Normalized();
	}

	private Color GroundTint(int i, int j)
	{
		int k = j * (_nx + 1) + i;
		float x = MinXZ.X + i * CellSize, z = MinXZ.Y + j * CellSize;
		float dT = Mathf.Min(_dT[k], _dR[k]);
		if (_dB != null && _dB[k] < BranchR) dT = Mathf.Min(dT, _dB[k] + 2.5f * (1f - BranchStrength((int)_iB[k], _sB[k])));
		float n = _nMid.GetNoise2D(x * 1.7f + 50f, z * 1.7f) * 0.5f + 0.5f;
		float deep = Mathf.SmoothStep(-DeepShadeZ.X, -DeepShadeZ.Y, -z);
		float shade = 1f - 0.38f * Mathf.SmoothStep(3f, 22f, dT) * (0.6f + 0.4f * n) - 0.12f * deep;
		float wet = 1f - Mathf.SmoothStep(1.5f, 5f, _dS[k]);
		shade *= 1f - 0.25f * wet;
		return new Color(shade, shade, shade, 1f);
	}

	private void BuildMesh()
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/terrain.gdshader") };
		mat.SetShaderParameter("tex_litter", ProcTextures.LeafLitter());
		mat.SetShaderParameter("tex_floor", ProcTextures.ForestFloor());
		mat.SetShaderParameter("tex_moss", ProcTextures.Moss());
		mat.SetShaderParameter("tex_noise", ProcTextures.WaterNoise());
		mat.SetShaderParameter("tex_grass", ProcTextures.GrassGround());
		mat.SetShaderParameter("tex_dirt", ProcTextures.Dirt());
		mat.SetShaderParameter("tex_gravel", ProcTextures.Gravel());
		mat.SetShaderParameter("tex_rock", ProcTextures.Rock());
		mat.SetShaderParameter("mask", BuildMask(out Vector2 mOrigin, out Vector2 mSize));
		mat.SetShaderParameter("mask_origin", mOrigin + new Vector2(GlobalPosition.X, GlobalPosition.Z));
		mat.SetShaderParameter("mask_size", mSize);

		var root = new Node3D { Name = "TerrainMesh" };
		AddChild(root);
		int w = _nx + 1;
		for (int cj = 0; cj < _nz; cj += ChunkCells)
			for (int ci = 0; ci < _nx; ci += ChunkCells)
			{
				int ci1 = Mathf.Min(ci + ChunkCells, _nx), cj1 = Mathf.Min(cj + ChunkCells, _nz);
				int cw = ci1 - ci + 1, chh = cj1 - cj + 1;
				var verts = new Vector3[cw * chh];
				var norms = new Vector3[cw * chh];
				var cols = new Color[cw * chh];
				for (int j = 0; j < chh; j++)
					for (int i = 0; i < cw; i++)
					{
						int gi = ci + i, gj = cj + j;
						verts[j * cw + i] = new Vector3(MinXZ.X + gi * CellSize, _h[gj * w + gi], MinXZ.Y + gj * CellSize);
						norms[j * cw + i] = GridNormal(gi, gj);
						cols[j * cw + i] = GroundTint(gi, gj);
					}
				var idx = new List<int>((cw - 1) * (chh - 1) * 6);
				for (int j = 0; j < chh - 1; j++)
					for (int i = 0; i < cw - 1; i++)
					{
						int a = j * cw + i, b = a + 1, c = a + cw, d = c + 1;
						// same split as HeightAt: diagonal (i,j)-(i+1,j+1); clockwise from above
						idx.Add(a); idx.Add(b); idx.Add(d);
						idx.Add(a); idx.Add(d); idx.Add(c);
					}
				var arr = new Godot.Collections.Array();
				arr.Resize((int)Mesh.ArrayType.Max);
				arr[(int)Mesh.ArrayType.Vertex] = verts;
				arr[(int)Mesh.ArrayType.Normal] = norms;
				arr[(int)Mesh.ArrayType.Color] = cols;
				arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
				var mesh = new ArrayMesh();
				mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
				mesh.SurfaceSetMaterial(0, mat);
				root.AddChild(new MeshInstance3D { Name = $"Chunk_{ci}_{cj}", Mesh = mesh });
			}
	}

	private ImageTexture BuildMask(out Vector2 origin, out Vector2 size)
	{
		float step = 0.5f;
		int w = Mathf.CeilToInt((MaxXZ.X - MinXZ.X) / step) + 1;
		int h = Mathf.CeilToInt((MaxXZ.Y - MinXZ.Y) / step) + 1;
		origin = MinXZ;
		size = new Vector2((w - 1) * step, (h - 1) * step);
		var dT = new float[w * h]; var sT = new float[w * h]; var dR = new float[w * h]; var dS = new float[w * h];
		System.Array.Fill(dT, 8f); System.Array.Fill(dR, 8f); System.Array.Fill(dS, 8f);
		_trail.Raster(origin, step, w, h, 8f, dT, sT);
		if (_road.Points.Count > 1) _road.Raster(origin, step, w, h, 8f, dR, null);
		if (_stream.Points.Count > 1) _stream.Raster(origin, step, w, h, 8f, dS, null);
		// side branches: dirt that narrows, breaks up and fades out where a faint path dies away
		var bDirt = new float[w * h];
		{
			var bd = new float[w * h]; var bs = new float[w * h];
			for (int bi = 0; bi < _branches.Count; bi++)
			{
				if (_branches[bi].Points.Count < 2) continue;
				System.Array.Fill(bd, 4f);
				_branches[bi].Raster(origin, step, w, h, 4f, bd, bs);
				for (int k = 0; k < bd.Length; k++)
				{
					if (bd[k] >= 4f) continue;
					float x = origin.X + (k % w) * step, z = origin.Y + (k / w) * step;
					float e = _nEdge.GetNoise2D(x, z);
					float st = BranchStrength(bi, bs[k]), half = BranchHalf(bi, bs[k]);
					float v = 1f - Mathf.SmoothStep(half - 0.2f, half + 0.4f, bd[k] + e * 0.3f);
					// the fading stretch goes patchy before it disappears
					v *= Mathf.Clamp(st * 1.7f - 0.35f + e * 0.6f, 0f, 1f);
					bDirt[k] = Mathf.Max(bDirt[k], v);
				}
			}
		}

		var data = new byte[w * h * 4];
		for (int j = 0; j < h; j++)
			for (int i = 0; i < w; i++)
			{
				int k = j * w + i;
				float x = origin.X + i * step, z = origin.Y + j * step;
				Vector2 p = new(x, z);
				float e = _nEdge.GetNoise2D(x, z);
				float half = TrailHalfWidth(sT[k]);
				float dirt = 1f - Mathf.SmoothStep(half - 0.2f, half + 0.45f, dT[k] + e * 0.35f);
				dirt = Mathf.Max(dirt, bDirt[k]);
				// dirt apron around the trailhead
				float park = RectDist(p, ParkingRect);
				float gravel = 1f - Mathf.SmoothStep(-0.3f, 0.9f, park + e * 0.6f);
				gravel = Mathf.Max(gravel, 1f - Mathf.SmoothStep(RoadWidth * 0.5f - 0.4f, RoadWidth * 0.5f + 0.5f, dR[k] + e * 0.4f));
				float rc = p.DistanceTo(ClearingCenter);
				float grass = ClearingGrass ? 1f - Mathf.SmoothStep(ClearingRadius * 0.8f, ClearingRadius * 1.1f, rc + e * 3f) : 0f;
				// ragged grass verges along the first stretch of trail and the road
				float verge = 0.6f * Mathf.SmoothStep(0.3f, 0.7f, _nMid.GetNoise2D(x * 3f, z * 3f) * 0.5f + 0.5f);
				grass = Mathf.Max(grass, verge * (1f - Mathf.SmoothStep(3f, 5.5f, Mathf.Min(dT[k], dR[k]))) * (1f - Mathf.SmoothStep(40f, 90f, sT[k])) * 0.8f);
				grass = Mathf.Max(grass, (1f - Mathf.SmoothStep(1f, 4f, park)) * verge * 0.35f);
				float mud = 1f - Mathf.SmoothStep(1.4f, 3.4f, dS[k] + e * 0.6f);
				data[k * 4 + 0] = (byte)(Mathf.Clamp(dirt, 0, 1) * 255);
				data[k * 4 + 1] = (byte)(Mathf.Clamp(gravel, 0, 1) * 255);
				data[k * 4 + 2] = (byte)(Mathf.Clamp(grass, 0, 1) * 255);
				data[k * 4 + 3] = (byte)(Mathf.Clamp(mud, 0, 1) * 255);
			}
		var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	private void BuildCollision()
	{
		var ground = new StaticBody3D { Name = "GroundBody", CollisionLayer = 1, CollisionMask = 0 };
		var gravel = new StaticBody3D { Name = "GravelBody", CollisionLayer = 1, CollisionMask = 0 };
		gravel.SetMeta("surface", "gravel");
		AddChild(ground);
		AddChild(gravel);
		int w = _nx + 1;
		var gravelFaces = new List<Vector3>();
		for (int cj = 0; cj < _nz; cj += ChunkCells)
			for (int ci = 0; ci < _nx; ci += ChunkCells)
			{
				int ci1 = Mathf.Min(ci + ChunkCells, _nx), cj1 = Mathf.Min(cj + ChunkCells, _nz);
				var faces = new List<Vector3>((ci1 - ci) * (cj1 - cj) * 6);
				for (int j = cj; j < cj1; j++)
					for (int i = ci; i < ci1; i++)
					{
						Vector3 P(int ii, int jj) => new(MinXZ.X + ii * CellSize, _h[jj * w + ii], MinXZ.Y + jj * CellSize);
						Vector3 a = P(i, j), b = P(i + 1, j), c = P(i, j + 1), d = P(i + 1, j + 1);
						Vector2 cen = new(MinXZ.X + (i + 0.5f) * CellSize, MinXZ.Y + (j + 0.5f) * CellSize);
						bool isGravel = RectDist(cen, ParkingRect) < 0.3f || _dR[j * w + i] < RoadWidth * 0.5f;
						var list = isGravel ? gravelFaces : faces;
						list.Add(a); list.Add(b); list.Add(d);
						list.Add(a); list.Add(d); list.Add(c);
					}
				if (faces.Count == 0) continue;
				var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
				shape.SetFaces(faces.ToArray());
				ground.AddChild(new CollisionShape3D { Name = $"C_{ci}_{cj}", Shape = shape });
			}
		if (gravelFaces.Count > 0)
		{
			var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
			shape.SetFaces(gravelFaces.ToArray());
			gravel.AddChild(new CollisionShape3D { Name = "Gravel", Shape = shape });
		}
	}

	private void BuildWater()
	{
		if (_stream.Points.Count < 2) return;
		var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>(); var idx = new List<int>();
		float half = 1.85f;
		int rows = 0;
		for (float s = 0; s <= _stream.Length + 0.01f; s += 1.5f, rows++)
		{
			Vector2 p = _stream.At(s, out Vector2 t);
			Vector2 nrm = new(-t.Y, t.X);
			float y = Bed(s) + 0.3f;
			for (int side = 0; side < 2; side++)
			{
				Vector2 q = p + nrm * (side == 0 ? -half : half);
				verts.Add(new Vector3(q.X, y, q.Y));
				norms.Add(Vector3.Up);
				uvs.Add(new Vector2(side, s / (half * 2f)));
			}
		}
		for (int r = 0; r < rows - 1; r++)
		{
			int a = r * 2, b = a + 1, c = a + 2, d = a + 3;
			// wind so +Y is the front face
			AddUpTri(verts, idx, a, b, d);
			AddUpTri(verts, idx, a, d, c);
		}
		var arr = new Godot.Collections.Array();
		arr.Resize((int)Mesh.ArrayType.Max);
		arr[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arr[(int)Mesh.ArrayType.Normal] = norms.ToArray();
		arr[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/terrain_water.gdshader") };
		mat.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		mesh.SurfaceSetMaterial(0, mat);
		AddChild(new MeshInstance3D { Name = "Water", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
	}

	private static void AddUpTri(List<Vector3> v, List<int> idx, int a, int b, int c)
	{
		var cr = (v[b] - v[a]).Cross(v[c] - v[a]);
		if (cr.Y > 0) (b, c) = (c, b); // Godot front faces are clockwise: cross points away from the viewer
		idx.Add(a); idx.Add(b); idx.Add(c);
	}

	private void BuildWalls()
	{
		var body = new StaticBody3D { Name = "BoundaryWalls", CollisionLayer = 1, CollisionMask = 0 };
		AddChild(body);
		float x0 = MinXZ.X + BoundaryInset, x1 = MaxXZ.X - BoundaryInset;
		float z0 = MinXZ.Y + BoundaryInset, z1 = MaxXZ.Y - BoundaryInset;
		float cy = 20f, hy = 200f, th = 2f;
		void Wall(Vector3 c, Vector3 s) => body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		Wall(new Vector3(x0 - th / 2, cy, (z0 + z1) / 2), new Vector3(th, hy, z1 - z0 + 2 * th));
		Wall(new Vector3(x1 + th / 2, cy, (z0 + z1) / 2), new Vector3(th, hy, z1 - z0 + 2 * th));
		Wall(new Vector3((x0 + x1) / 2, cy, z0 - th / 2), new Vector3(x1 - x0 + 2 * th, hy, th));
		Wall(new Vector3((x0 + x1) / 2, cy, z1 + th / 2), new Vector3(x1 - x0 + 2 * th, hy, th));
	}

	/// <summary>Snap stream markers to the water surface and rewrite the trail Path3D at ground height.</summary>
	private void UpdateSceneMarkers()
	{
		var streamNode = GetNodeOrNull<Node3D>(StreamPath);
		if (streamNode != null)
			foreach (var c in streamNode.GetChildren())
				if (c is Node3D m)
				{
					Vector3 gp = m.GlobalPosition;
					float d = _stream.Closest(ToLocal2(gp.X, gp.Z), out float s);
					gp.Y = WaterLevel(s);
					m.GlobalPosition = gp;
				}

		if (GetNodeOrNull<Path3D>(TrailPath) is Path3D path)
			path.Curve = GroundCurve(path, _trail);
		for (int bi = 0; bi < BranchPaths.Length && bi < _branches.Count; bi++)
			if (_branches[bi].Points.Count > 1 && GetNodeOrNull<Path3D>(BranchPaths[bi]) is Path3D bp)
				bp.Curve = GroundCurve(bp, _branches[bi]);

		// the test route: main trail → its branch → the cross-country leg
		if (_route != null && GetNodeOrNull<Path3D>(AutotestRoutePath) is Path3D rpath)
		{
			var all = new Polyline2();
			foreach (var p in _trail.Points) all.Add(p);
			if (AutotestBranch >= 0 && AutotestBranch < _branches.Count)
				foreach (var p in _branches[AutotestBranch].Points) all.Add(p);
			foreach (var p in _route.Points) all.Add(p);
			rpath.Curve = GroundCurve(rpath, all);
		}
	}

	/// <summary>A polyline (local x,z) as a Curve3D at ground height, in the given path's space.</summary>
	private Curve3D GroundCurve(Path3D path, Polyline2 pl)
	{
		var curve = new Curve3D();
		int n = Mathf.Max(2, Mathf.CeilToInt(pl.Length / PathPointSpacing));
		Transform3D inv = path.GlobalTransform.AffineInverse();
		Vector3 o = GlobalPosition;
		for (int i = 0; i <= n; i++)
		{
			Vector2 p = pl.At(pl.Length * i / n, out _);
			curve.AddPoint(inv * GroundPoint(p.X + o.X, p.Y + o.Z));
		}
		return curve;
	}
}
