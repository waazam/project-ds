using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// The snow cave itself (Act 24; the owner's references: blue ice tunnels, round and scalloped, icicles hanging in curtains,
/// trodden snow underfoot, light coming down from openings far overhead; and, at the heart of it, a cavern with old trenches
/// dug through its floor). One distance field makes it all:
/// <list type="bullet">
/// <item>the tunnels: round and wide (3.8 m across, 3 m high), along the maze's edges (<see cref="SnowMazeLayout"/>), their walls
/// pushed about by noise into lumps and scallops;</item>
/// <item>roomier swellings at the junctions; alcoves dug into the walls; mounds of snow heaped on the floor in front of
/// some of them (the owner: "little alcoves and snow mounds to hide behind");</item>
/// <item>the great cavern in the middle, a low dome thirty metres across, its floor cut through with the trenches;</item>
/// <item>here and there a hole up through the roof, a cold light coming down it.</item>
/// </list>
/// Meshed by <see cref="SurfaceNets"/>, cut into chunks (each its own mesh and collision, culled apart), dressed with
/// icicles. Local space: the maze's grid, the floor at y = 0.
/// </summary>
public partial class SnowMazeCave : Node3D
{
	public const float TunnelRH = 1.9f, TunnelRV = 1.8f, TunnelY = 1.4f;   // (the roof 3.2 m up: room for it, stooped)
	public const float TrenchDepth = 2.0f, TrenchHalfW = 0.75f;
	public const float Step = 0.45f;
	public SnowMazeLayout Layout { get; private set; }
	/// <summary>The trench lines (local, on the cavern floor), for the dressing and the props.</summary>
	public readonly List<(Vector3 a, Vector3 b)> Trenches = new();
	public readonly List<Vector3> Skylights = new();
	public readonly List<(Vector3 at, Vector3 outward)> Alcoves = new();
	public readonly List<Vector3> Mounds = new();
	/// <summary>The way in: the end of the entrance tunnel (where the player arrives) and the direction in.</summary>
	public Vector3 ArriveAt { get; private set; }
	public int Triangles { get; private set; }
	public Material IceMaterial { get; private set; }

	private struct Prim
	{
		public int Kind;          // 0 capsule (tunnel), 1 ellipsoid (junction, alcove), 2 mound (solid), 3 trench box, 4 shaft (vertical capsule)
		public Vector3 A, B, R;   // capsule ends / centre; radii
	}
	private readonly List<Prim> _prims = new();
	private readonly Dictionary<Vector2I, List<int>> _buckets = new();
	private const float Bucket = 6f;
	private FastNoiseLite _noise, _fine;

	/// <summary>Built off the main thread (during play: the meshing takes seconds) and put in place when done; or all at
	/// once (a load, behind the black).</summary>
	public bool Async;
	public bool Built { get; private set; }
	public event System.Action OnBuilt;

	public override void _Ready()
	{
		Layout = new SnowMazeLayout();
		_noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.18f, Seed = 241 };
		_fine = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.9f, Seed = 242, CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance };
		Plan();
		if (Async) System.Threading.Tasks.Task.Run(Compute).ContinueWith(_ => Callable.From(Apply).CallDeferred());
		else { Compute(); Apply(); }
	}

	private List<Transform3D> _icicles;

	// ------------------------------------------------------------------ the plan: primitives

	private void Plan()
	{
		var L = Layout;
		var rng = new RandomNumberGenerator { Seed = 2425 };
		// the tunnels along the edges (into the cavern: to its door), and the way in from the south
		foreach (var (a, b) in L.Edges)
		{
			var pa = L.EndAt(b, a);
			var pb = L.EndAt(a, b);
			Add(new Prim { Kind = 0, A = pa, B = pb, R = new Vector3(TunnelRH * rng.RandfRange(0.92f, 1.1f), TunnelRV * rng.RandfRange(0.95f, 1.12f), 0) });
			// an alcove off it, now and then, and a mound of snow in front of it
			if (rng.Randf() < 0.42f && pa.DistanceTo(pb) > 6f)
			{
				var mid = pa.Lerp(pb, rng.RandfRange(0.35f, 0.65f));
				var along = (pb - pa).Normalized();
				var side = new Vector3(along.Z, 0, -along.X) * (rng.Randf() < 0.5f ? -1f : 1f);
				var c = mid + side * 2.1f;
				Add(new Prim { Kind = 1, A = c + Vector3.Up * 0.9f, R = new Vector3(1.35f, 1.35f, 1.35f) });
				Alcoves.Add((mid + side * 2.2f, side));
				if (rng.Randf() < 0.7f)
				{
					var m = mid + side * 0.9f + along * rng.RandfRange(-0.6f, 0.6f);
					Add(new Prim { Kind = 2, A = m, R = new Vector3(1.0f, 0.95f, 0.85f) });
					Mounds.Add(m);
				}
			}
		}
		var gate = L.Nodes[L.Entrance];
		var outside = gate + new Vector3(0, 0, -15f);
		Add(new Prim { Kind = 0, A = outside, B = gate, R = new Vector3(TunnelRH, TunnelRV, 0) });
		ArriveAt = outside + new Vector3(0, 0.1f, 2f);
		// roomy junctions
		for (int v = 0; v < L.Nodes.Count; v++)
		{
			if (v == L.Cavern) continue;
			float r = L.Adj[v].Count >= 3 ? 2.6f : 2.2f;
			Add(new Prim { Kind = 1, A = L.Nodes[v] + Vector3.Up * 1.1f, R = new Vector3(r, 2.0f, r) });
		}
		// a few mounds out in the open tunnels too (at junctions with three ways or more)
		for (int v = 0; v < L.Nodes.Count; v++)
			if (v != L.Cavern && L.Adj[v].Count >= 3 && rng.Randf() < 0.35f)
			{
				var m = L.Nodes[v] + new Vector3(rng.RandfRange(-1.2f, 1.2f), 0, rng.RandfRange(-1.2f, 1.2f));
				Add(new Prim { Kind = 2, A = m, R = new Vector3(0.9f, 1.0f, 0.9f) });
				Mounds.Add(m);
			}
		// the cavern: a low dome (its floor the maze's), and its trenches
		var cc = SnowMazeLayout.CavernCentre;
		Add(new Prim { Kind = 1, A = cc, R = new Vector3(SnowMazeLayout.CavernRX, SnowMazeLayout.CavernH, SnowMazeLayout.CavernRZ) });
		// the trenches: zig-zagging lines across it, a communication trench joining them
		Vector3 T(float x, float z) => cc + new Vector3(x, 0, z);
		var lines = new[]
		{
			new[] { T(-11f, -5f), T(-6f, -7f), T(-1f, -4.5f), T(4f, -7f), T(9.5f, -5f) },
			new[] { T(-10f, 4.5f), T(-4.5f, 6.5f), T(0.5f, 4f), T(5.5f, 6.5f), T(10f, 4f) },
			new[] { T(-1f, -4.5f), T(-2f, -0.5f), T(1f, 1.5f), T(0.5f, 4f) },
			new[] { T(-6f, -7f), T(-8f, -10f) },
			new[] { T(5.5f, 6.5f), T(7f, 10f) },
		};
		foreach (var line in lines)
			for (int i = 0; i + 1 < line.Length; i++)
			{
				Trenches.Add((line[i], line[i + 1]));
				Add(new Prim { Kind = 3, A = line[i], B = line[i + 1], R = new Vector3(TrenchHalfW, TrenchDepth, 0) });
			}
		// skylights: holes up through the roof at a handful of junctions, a cold light down each
		var picks = new List<int>();
		for (int v = 0; v < L.Nodes.Count; v++) if (v != L.Cavern && rng.Randf() < 0.12f) picks.Add(v);
		foreach (int v in picks)
		{
			var p = L.Nodes[v];
			Add(new Prim { Kind = 4, A = p + Vector3.Up * 2.5f, B = p + Vector3.Up * 11.5f, R = new Vector3(1.0f, 0, 0) });
			Skylights.Add(p);
		}
		Skylights.Add(cc + new Vector3(3f, 0, -2f));
		Add(new Prim { Kind = 4, A = cc + new Vector3(3f, 7f, -2f), B = cc + new Vector3(3f, 11.5f, -2f), R = new Vector3(1.6f, 0, 0) });
	}

	private void Add(Prim p)
	{
		int idx = _prims.Count;
		_prims.Add(p);
		Vector3 lo, hi;
		float m = 3f + Mathf.Max(p.R.X, p.R.Z);
		if (p.Kind == 1 || p.Kind == 2) { lo = p.A - new Vector3(p.R.X + 3f, 0, p.R.Z + 3f); hi = p.A + new Vector3(p.R.X + 3f, 0, p.R.Z + 3f); }
		else { lo = new Vector3(Mathf.Min(p.A.X, p.B.X) - m, 0, Mathf.Min(p.A.Z, p.B.Z) - m); hi = new Vector3(Mathf.Max(p.A.X, p.B.X) + m, 0, Mathf.Max(p.A.Z, p.B.Z) + m); }
		for (int bx = Mathf.FloorToInt(lo.X / Bucket); bx <= Mathf.FloorToInt(hi.X / Bucket); bx++)
			for (int bz = Mathf.FloorToInt(lo.Z / Bucket); bz <= Mathf.FloorToInt(hi.Z / Bucket); bz++)
			{
				var k = new Vector2I(bx, bz);
				if (!_buckets.TryGetValue(k, out var l)) _buckets[k] = l = new List<int>();
				l.Add(idx);
			}
	}

	// ------------------------------------------------------------------ the field

	/// <summary>The cave's distance field at a local point: negative in the open, positive in the snow.</summary>
	public float Field(Vector3 p)
	{
		if (!_buckets.TryGetValue(new Vector2I(Mathf.FloorToInt(p.X / Bucket), Mathf.FloorToInt(p.Z / Bucket)), out var list)) return 4f;
		float air = 6f, trench = 6f, mound = 6f;
		foreach (int i in list)
		{
			var q = _prims[i];
			switch (q.Kind)
			{
				case 0:
				{
					var ab = (q.B - q.A) with { Y = 0 };
					float t = Mathf.Clamp((p - q.A).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-4f), 0f, 1f);
					var c = q.A + ab * t;
					float h = new Vector2(p.X - c.X, p.Z - c.Z).Length() / q.R.X;
					float y = (p.Y - TunnelY) / q.R.Y;
					air = Mathf.Min(air, (Mathf.Sqrt(h * h + y * y) - 1f) * q.R.Y);
					break;
				}
				case 1:
				{
					var d = p - q.A;
					float e = Mathf.Sqrt(d.X * d.X / (q.R.X * q.R.X) + d.Y * d.Y / (q.R.Y * q.R.Y) + d.Z * d.Z / (q.R.Z * q.R.Z)) - 1f;
					air = Mathf.Min(air, e * Mathf.Min(q.R.X, Mathf.Min(q.R.Y, q.R.Z)));
					break;
				}
				case 2:
				{
					var d = p - q.A;
					float e = Mathf.Sqrt(d.X * d.X / (q.R.X * q.R.X) + d.Y * d.Y / (q.R.Y * q.R.Y) + d.Z * d.Z / (q.R.Z * q.R.Z)) - 1f;
					mound = Mathf.Min(mound, e * Mathf.Min(q.R.X, q.R.Y));
					break;
				}
				case 3:
				{
					var ab = q.B - q.A;
					float t = Mathf.Clamp((p - q.A).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-4f), 0f, 1f);
					var c = q.A + ab * t;
					float h = new Vector2(p.X - c.X, p.Z - c.Z).Length() - q.R.X;
					float yb = Mathf.Max(-q.R.Y - p.Y, p.Y - 0.6f);   // from the trench's floor up past the cavern's floor
					trench = Mathf.Min(trench, Mathf.Max(h, yb));
					break;
				}
				case 4:
				{
					if (p.Y < q.A.Y - 2f || p.Y > q.B.Y + 1f) break;
					// a hole straight up through the roof, widening a little as it goes, closed at its top (the sky's disc there)
					float h = new Vector2(p.X - q.A.X, p.Z - q.A.Z).Length() - q.R.X * (1f + 0.25f * (p.Y - q.A.Y) / 9f);
					air = Mathf.Min(air, Mathf.Max(h, Mathf.Max(q.A.Y - 1.5f - p.Y, p.Y - q.B.Y)));
					break;
				}
			}
		}
		// the walls and roof lumpy and scalloped; the floor (y = 0) trodden nearly flat
		float lump = _noise.GetNoise3D(p.X, p.Y * 1.3f, p.Z) * 0.32f;
		float scallop = (_fine.GetNoise3D(p.X, p.Y, p.Z) - 0.5f) * 0.12f;
		float roof = Mathf.Clamp((p.Y - 0.4f) / 1.2f, 0f, 1f);
		air += (lump + scallop) * roof;
		float floor = 0.03f * _noise.GetNoise2D(p.X * 2.3f, p.Z * 2.3f) - p.Y;
		float open = Mathf.Max(air, floor);
		open = Mathf.Min(open, trench);
		return Mathf.Max(open, -mound);
	}

	// ------------------------------------------------------------------ the mesh

	/// <summary>The heavy part (no nodes touched): the field meshed, cut into chunks, the icicles placed.</summary>
	private void Compute()
	{
		if (LoadCache()) return;
		ComputeFresh();
		SaveCache();
	}

	// ---- the cache: the meshed cave kept on disk after the first build (it takes seconds; loaded, a fraction of one).
	// Keyed by this build of the game (any change to the code can change the cave) and the cave's own sizes.

	private static string CachePath => ProjectSettings.GlobalizePath("user://snowmaze_cache.bin");
	private static string CacheKey => $"v1|{typeof(SnowMazeCave).Assembly.ManifestModule.ModuleVersionId}|{Step}|{TunnelRH}|{TunnelRV}|{TunnelY}|{TrenchDepth}|{SnowMazeLayout.N}|{SnowMazeLayout.Spacing}";
	public bool FromCache { get; private set; }

	private bool LoadCache()
	{
		try
		{
			if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-maze-cache") >= 0 || !System.IO.File.Exists(CachePath)) return false;
			ulong t0 = Time.GetTicksMsec();
			using var r = new System.IO.BinaryReader(System.IO.File.OpenRead(CachePath));
			if (r.ReadString() != CacheKey) return false;
			Triangles = r.ReadInt32();
			int chunks = r.ReadInt32();
			var list = new List<Chunk>(chunks);
			for (int c = 0; c < chunks; c++)
			{
				var key = new Vector2I(r.ReadInt32(), r.ReadInt32());
				var v = ReadV(r); var nrm = ReadV(r);
				var idx = new int[r.ReadInt32()];
				for (int i = 0; i < idx.Length; i++) idx[i] = r.ReadInt32();
				var faces = ReadV(r);
				list.Add(new Chunk(key, v, nrm, idx, faces));
			}
			var ice = new List<Transform3D>(r.ReadInt32());
			for (int i = ice.Capacity; i > 0; i--)
			{
				var b = new Basis(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
				ice.Add(new Transform3D(b, new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle())));
			}
			_pending.Clear();
			foreach (var c in list) _pending.Enqueue(c);
			_icicles = ice;
			FromCache = true;
			GD.Print($"[snowmaze] from the cache: {Triangles} triangles in {chunks} chunks, {ice.Count} icicles in {Time.GetTicksMsec() - t0} ms");
			return true;
		}
		catch (System.Exception e) { GD.Print($"[snowmaze] cache unreadable ({e.Message}): building it"); return false; }
	}

	private void SaveCache()
	{
		try
		{
			using var w = new System.IO.BinaryWriter(System.IO.File.Create(CachePath));
			w.Write(CacheKey);
			w.Write(Triangles);
			w.Write(_pending.Count);
			foreach (var c in _pending)
			{
				w.Write(c.Key.X); w.Write(c.Key.Y);
				WriteV(w, c.V); WriteV(w, c.N);
				w.Write(c.I.Length);
				foreach (int i in c.I) w.Write(i);
				WriteV(w, c.Faces);
			}
			w.Write(_icicles.Count);
			foreach (var t in _icicles)
			{
				foreach (var col in new[] { t.Basis.Column0, t.Basis.Column1, t.Basis.Column2, t.Origin }) { w.Write(col.X); w.Write(col.Y); w.Write(col.Z); }
			}
		}
		catch (System.Exception e) { GD.Print($"[snowmaze] couldn't keep the cache ({e.Message})"); }
	}

	private static Vector3[] ReadV(System.IO.BinaryReader r)
	{
		var a = new Vector3[r.ReadInt32()];
		for (int i = 0; i < a.Length; i++) a[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		return a;
	}

	private static void WriteV(System.IO.BinaryWriter w, Vector3[] a)
	{
		w.Write(a.Length);
		foreach (var v in a) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
	}

	private void ComputeFresh()
	{
		Vector3 min = new(-6f, -TrenchDepth - 0.6f, -18f);
		Vector3 max = new((SnowMazeLayout.N - 1) * SnowMazeLayout.Spacing + 6f, 12.4f, (SnowMazeLayout.N - 1) * SnowMazeLayout.Spacing + 6f);
		var n = new Vector3I(Mathf.CeilToInt((max.X - min.X) / Step), Mathf.CeilToInt((max.Y - min.Y) / Step), Mathf.CeilToInt((max.Z - min.Z) / Step));
		ulong t0 = Time.GetTicksMsec();
		var (verts, normals, index) = SurfaceNets.Mesh(Field, min, n, Step);
		Triangles = index.Length / 3;
		// cut into chunks by each triangle's middle (each its own mesh and collision, culled on its own)
		const float chunk = 18f;
		var groups = new Dictionary<Vector2I, List<int>>();
		for (int t = 0; t < index.Length; t += 3)
		{
			var c = (verts[index[t]] + verts[index[t + 1]] + verts[index[t + 2]]) / 3f;
			var k = new Vector2I(Mathf.FloorToInt(c.X / chunk), Mathf.FloorToInt(c.Z / chunk));
			if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<int>();
			l.Add(t);
		}
		_pending.Clear();
		foreach (var (key, tris) in groups)
		{
			var remap = new Dictionary<int, int>();
			var cv = new List<Vector3>();
			var cn = new List<Vector3>();
			var ci = new List<int>();
			var faces = new Vector3[tris.Count * 3];
			int f = 0;
			foreach (int t in tris)
				for (int jj = 0; jj < 3; jj++)
				{
					int o = index[t + jj];
					if (!remap.TryGetValue(o, out int r)) { r = cv.Count; remap[o] = r; cv.Add(verts[o]); cn.Add(normals[o]); }
					ci.Add(r);
					faces[f++] = verts[o];
				}
			_pending.Enqueue(new Chunk(key, cv.ToArray(), cn.ToArray(), ci.ToArray(), faces));
		}
		_icicles = PlaceIcicles();
		GD.Print($"[snowmaze] {verts.Length} vertices, {Triangles} triangles in {_pending.Count} chunks, {_icicles.Count} icicles in {Time.GetTicksMsec() - t0} ms");
	}

	private sealed record Chunk(Vector2I Key, Vector3[] V, Vector3[] N, int[] I, Vector3[] Faces);
	private readonly Queue<Chunk> _pending = new();
	private StaticBody3D _caveBody;
	private bool _applying;

	/// <summary>The nodes (on the main thread): the chunks' meshes and collision, then the icicles and the skylights. During
	/// play, a few milliseconds' worth a frame (all at once, a 0.9 s hitch); loading, all at once behind the black.</summary>
	private void Apply()
	{
		IceMaterial = SnowMazeLook.CaveMaterial();
		_caveBody = new StaticBody3D { Name = "CaveBody", CollisionLayer = 1, CollisionMask = 0 };
		_caveBody.SetMeta("surface", "snow");
		AddChild(_caveBody);
		_applying = true;
		if (!Async) ApplySome(ulong.MaxValue);
	}

	public override void _Process(double delta)
	{
		if (_applying) ApplySome(6000);
	}

	private void ApplySome(ulong budgetUsec)
	{
		ulong t0 = Time.GetTicksUsec();
		while (_pending.Count > 0)
		{
			var c = _pending.Dequeue();
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = c.V;
			arrays[(int)Mesh.ArrayType.Normal] = c.N;
			arrays[(int)Mesh.ArrayType.Index] = c.I;
			var am = new ArrayMesh();
			am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			am.SurfaceSetMaterial(0, IceMaterial);
			AddChild(new MeshInstance3D { Name = $"Cave_{c.Key.X}_{c.Key.Y}", Mesh = am, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
			var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
			shape.SetFaces(c.Faces);
			_caveBody.AddChild(new CollisionShape3D { Name = $"Col_{c.Key.X}_{c.Key.Y}", Shape = shape });
			if (Time.GetTicksUsec() - t0 > budgetUsec) return;
		}
		_applying = false;
		Dress();
		Built = true;
		OnBuilt?.Invoke();
	}

	/// <summary>The roof over a point (local): the first solid going up from just above the floor.</summary>
	public float RoofAbove(Vector3 p, float maxUp = 12f)
	{
		for (float y = 1.2f; y < maxUp; y += 0.12f)
			if (Field(new Vector3(p.X, y, p.Z)) > 0f) return y;
		return float.NaN;
	}

	/// <summary>Icicles in curtains along the roofs, longer in the cavern (where they hang: from the field alone).</summary>
	private List<Transform3D> PlaceIcicles()
	{
		var rng = new RandomNumberGenerator { Seed = 2426 };
		var xf = new List<Transform3D>();
		var L = Layout;
		void Curtain(Vector3 at, int n, float len)
		{
			float roof = RoofAbove(at);
			if (float.IsNaN(roof) || roof > 9.5f) return;
			for (int k = 0; k < n; k++)
			{
				var p = at + new Vector3(rng.RandfRange(-0.35f, 0.35f), 0, rng.RandfRange(-0.35f, 0.35f));
				float r = RoofAbove(p);
				if (float.IsNaN(r)) continue;
				float l = len * rng.RandfRange(0.35f, 1.1f);
				// (the icicle mesh is a metre long, its top at its origin, hanging down)
				xf.Add(new Transform3D(Basis.FromScale(new Vector3(rng.RandfRange(0.7f, 1.6f), l, rng.RandfRange(0.7f, 1.6f))).Rotated(Vector3.Up, rng.Randf() * 6.28f), p with { Y = r + 0.04f }));
			}
		}
		foreach (var (a, b) in L.Edges)
		{
			var pa = L.EndAt(b, a);
			var pb = L.EndAt(a, b);
			float len = pa.DistanceTo(pb);
			var side = new Vector3((pb - pa).Z, 0, -(pb - pa).X).Normalized();
			for (float s = 1f; s < len - 1f; s += rng.RandfRange(1.2f, 2.6f))
				Curtain(pa.Lerp(pb, s / len) + side * rng.RandfRange(-1.1f, 1.1f), rng.RandiRange(2, 6), 0.7f);
		}
		var cc = SnowMazeLayout.CavernCentre;
		for (int k = 0; k < 70; k++)
		{
			float a = rng.Randf() * Mathf.Tau, r = Mathf.Sqrt(rng.Randf()) * 0.85f;
			Curtain(cc + new Vector3(Mathf.Cos(a) * r * SnowMazeLayout.CavernRX, 0, Mathf.Sin(a) * r * SnowMazeLayout.CavernRZ), rng.RandiRange(3, 7), 1.6f);
		}
		return xf;
	}

	/// <summary>The icicles put up; the cold light down the skylights.</summary>
	private void Dress()
	{
		var xf = _icicles;
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = SnowMazeLook.IcicleMesh(), InstanceCount = xf.Count };
		for (int i = 0; i < xf.Count; i++) mm.SetInstanceTransform(i, xf[i]);
		AddChild(new MultiMeshInstance3D { Name = "Icicles", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		Icicles = xf.Count;
		// the skylights: a pale disc of the sky far up each hole, its light shafting down, dust in it
		var shaftMat = WindowShafts.Material(new Color(0.62f, 0.75f, 0.92f), 0.04f);
		foreach (var s in Skylights)
		{
			float top = SnowMazeLayout.InCavern(s) ? 11.3f : 11.2f;
			var sky = new MeshInstance3D
			{
				Name = "SkyHole", Mesh = new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.4f, Height = 0.05f, RadialSegments = 16 },
				MaterialOverride = SnowMazeLook.SkyHole(), Position = s with { Y = top }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild(sky);
			WindowShafts.Add(this, shaftMat, s with { Y = top - 1f }, Vector3.Right, Vector3.Back, 1.6f, 1.6f, new Vector3(0.05f, -1f, 0.08f), 11f, 16);
			AddChild(new OmniLight3D { Name = "SkyGlow", Position = s with { Y = 2.6f }, LightColor = new Color(0.55f, 0.68f, 0.95f), LightEnergy = 0.5f, OmniRange = 9f, ShadowEnabled = false });
		}
	}

	public int Icicles { get; private set; }
}
