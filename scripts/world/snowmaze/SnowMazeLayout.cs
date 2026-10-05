using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// The snow maze's plan (Act 24; the owner: "dead ends, looped passages that take you backwards, and only one right way
/// through. It is winding and twisted and weird"): a grid of junctions nine metres apart, the middle of it given over to
/// the great cavern with the trenches; a maze carved through the rest (a spanning tree from a fixed seed: every junction
/// reachable, one way only between any two); then loops added inside the side branches only, so they lead round and back
/// but never open a second way to the cavern; the junctions jittered off the grid so nothing runs straight for long.
/// The wendigo walks the same graph (its nodes and edges are its paths).
/// </summary>
public sealed class SnowMazeLayout
{
	public const int N = 11;
	public const float Spacing = 9f;
	/// <summary>The cavern's grid cells (a 3 x 3 block in the middle) and its size.</summary>
	public const int C0 = 4, C1 = 6;
	public static readonly Vector3 CavernCentre = new((N - 1) * 0.5f * Spacing, 0f, (N - 1) * 0.5f * Spacing);
	public const float CavernRX = 15f, CavernRZ = 14f, CavernH = 8.5f;

	public readonly List<Vector3> Nodes = new();
	public readonly List<(int a, int b)> Edges = new();
	public readonly List<List<int>> Adj = new();
	/// <summary>The node in the cavern (all of the cavern is one node to the maze; its doors are edges).</summary>
	public int Cavern { get; private set; }
	/// <summary>Where the way in arrives (the edge of the grid nearest the lodge), and the solution's path to the cavern.</summary>
	public int Entrance { get; private set; }
	public readonly List<int> Solution = new();
	public readonly HashSet<(int, int)> LoopEdges = new();
	public readonly List<int> DeadEnds = new();
	/// <summary>Grid coordinate of each node (the cavern's: its middle).</summary>
	public readonly List<Vector2I> Grid = new();

	private readonly Dictionary<Vector2I, int> _at = new();

	public SnowMazeLayout(ulong seed = 2424)
	{
		var rng = new RandomNumberGenerator { Seed = seed };
		for (int j = 0; j < N; j++)
			for (int i = 0; i < N; i++)
			{
				bool cav = i >= C0 && i <= C1 && j >= C0 && j <= C1;
				if (cav && !(i == 5 && j == 5)) continue;
				var g = new Vector2I(i, j);
				_at[g] = Nodes.Count;
				Grid.Add(g);
				var p = new Vector3(i * Spacing, 0f, j * Spacing);
				if (!cav) p += new Vector3(rng.RandfRange(-2.2f, 2.2f), 0f, rng.RandfRange(-2.2f, 2.2f));
				Nodes.Add(cav ? CavernCentre : p);
				Adj.Add(new List<int>());
			}
		Cavern = _at[new Vector2I(5, 5)];
		Entrance = _at[new Vector2I(5, 0)];
		// the maze: a randomized depth-first spanning tree over the grid (the cavern a single cell: its neighbours are the
		// cells round its 3x3 block)
		var parent = new Dictionary<int, int>();
		var seen = new HashSet<int> { Entrance };
		var stack = new Stack<int>();
		stack.Push(Entrance);
		while (stack.Count > 0)
		{
			int cur = stack.Peek();
			var nb = Neighbours(cur);
			for (int k = nb.Count - 1; k > 0; k--) { int r = rng.RandiRange(0, k); (nb[k], nb[r]) = (nb[r], nb[k]); }
			int next = -1;
			foreach (int m in nb) if (!seen.Contains(m)) { next = m; break; }
			if (next < 0) { stack.Pop(); continue; }
			seen.Add(next);
			parent[next] = cur;
			Link(cur, next);
			stack.Push(next);
		}
		// the one right way: the tree's path from the entrance to the cavern
		for (int v = Cavern; ; v = parent[v]) { Solution.Insert(0, v); if (v == Entrance) break; }
		// each node's branch: the solution node it hangs off (loops may only join nodes of one branch)
		var onPath = new HashSet<int>(Solution);
		var branch = new Dictionary<int, int>();
		int BranchOf(int v)
		{
			if (branch.TryGetValue(v, out int b)) return b;
			int u = v;
			while (!onPath.Contains(u)) u = parent[u];
			return branch[v] = u;
		}
		var candidates = new List<(int, int)>();
		for (int v = 0; v < Nodes.Count; v++)
			foreach (int m in Neighbours(v))
				if (v < m && !Adj[v].Contains(m) && !onPath.Contains(v) && !onPath.Contains(m) && v != Cavern && m != Cavern && BranchOf(v) == BranchOf(m))
					candidates.Add((v, m));
		for (int k = candidates.Count - 1; k > 0; k--) { int r = rng.RandiRange(0, k); (candidates[k], candidates[r]) = (candidates[r], candidates[k]); }
		for (int k = 0; k < Mathf.Min(8, candidates.Count); k++) { Link(candidates[k].Item1, candidates[k].Item2); LoopEdges.Add(candidates[k]); }
		for (int v = 0; v < Nodes.Count; v++) if (Adj[v].Count == 1 && v != Entrance) DeadEnds.Add(v);
	}

	private void Link(int a, int b)
	{
		Edges.Add((a, b));
		Adj[a].Add(b);
		Adj[b].Add(a);
	}

	/// <summary>The grid neighbours of a node (the cavern's: every cell bordering its block).</summary>
	private List<int> Neighbours(int v)
	{
		var l = new List<int>();
		if (v == Cavern)
		{
			for (int i = C0; i <= C1; i++)
			{
				Try(l, new Vector2I(i, C0 - 1)); Try(l, new Vector2I(i, C1 + 1));
				Try(l, new Vector2I(C0 - 1, i)); Try(l, new Vector2I(C1 + 1, i));
			}
			return l;
		}
		var g = Grid[v];
		foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
		{
			var q = g + d;
			if (q.X >= C0 && q.X <= C1 && q.Y >= C0 && q.Y <= C1) { if (!l.Contains(Cavern)) l.Add(Cavern); continue; }
			Try(l, q);
		}
		return l;
	}

	private void Try(List<int> l, Vector2I g) { if (_at.TryGetValue(g, out int v) && !l.Contains(v)) l.Add(v); }

	/// <summary>Where an edge meets the cavern's wall (its door), for an edge into it; else the far node.</summary>
	public Vector3 EndAt(int from, int to)
	{
		if (to != Cavern) return Nodes[to];
		var d = (Nodes[from] - CavernCentre) with { Y = 0 };
		float sx = CavernRX - 1.5f, sz = CavernRZ - 1.5f;
		float t = 1f / Mathf.Sqrt(d.X * d.X / (sx * sx) + d.Z * d.Z / (sz * sz));
		return CavernCentre + d * t;
	}

	/// <summary>Shortest path (node list) between two nodes, by distance along the edges.</summary>
	public List<int> Path(int from, int to)
	{
		var dist = new float[Nodes.Count];
		var prev = new int[Nodes.Count];
		for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
		dist[from] = 0f;
		var open = new SortedSet<(float, int)> { (0f, from) };
		while (open.Count > 0)
		{
			var (d, u) = open.Min;
			open.Remove(open.Min);
			if (u == to) break;
			foreach (int v in Adj[u])
			{
				float nd = d + Nodes[u].DistanceTo(Nodes[v]);
				if (nd >= dist[v]) continue;
				if (dist[v] < float.MaxValue) open.Remove((dist[v], v));
				dist[v] = nd;
				prev[v] = u;
				open.Add((nd, v));
			}
		}
		var path = new List<int>();
		if (prev[to] < 0 && from != to) return path;
		for (int v = to; v >= 0; v = prev[v]) { path.Insert(0, v); if (v == from) break; }
		return path;
	}

	/// <summary>Distance along the edges between two nodes (the way a sound travels through the tunnels).</summary>
	public float PathLength(List<int> path)
	{
		float s = 0f;
		for (int i = 1; i < path.Count; i++) s += Nodes[path[i - 1]].DistanceTo(Nodes[path[i]]);
		return s;
	}

	/// <summary>The node nearest a point (on the floor plane).</summary>
	public int Nearest(Vector3 p)
	{
		int best = 0;
		float bd = float.MaxValue;
		for (int i = 0; i < Nodes.Count; i++)
		{
			float d = new Vector2(Nodes[i].X - p.X, Nodes[i].Z - p.Z).LengthSquared();
			if (i == Cavern && InCavern(p)) return i;
			if (d < bd) { bd = d; best = i; }
		}
		return best;
	}

	/// <summary>The tunnel (edge) a point is in: the nearest edge on the floor plane, and its distance along it from the
	/// edge's first node. (The nearest node in a straight line can be the next one over, through the snow.)</summary>
	public (int a, int b) EdgeAt(Vector3 p)
	{
		var q = new Vector2(p.X, p.Z);
		(int, int) best = Edges.Count > 0 ? Edges[0] : (0, 0);
		float bd = float.MaxValue;
		foreach (var (ea, eb) in Edges)
		{
			var a2 = new Vector2(Nodes[ea].X, Nodes[ea].Z);
			var b2 = new Vector2(Nodes[eb].X, Nodes[eb].Z);
			var ab = b2 - a2;
			float t = ab.LengthSquared() > 1e-4f ? Mathf.Clamp((q - a2).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
			float d = (a2 + ab * t).DistanceSquaredTo(q);
			if (d < bd) { bd = d; best = (ea, eb); }
		}
		return best;
	}

	/// <summary>The node to start along the tunnels from: the nearer end of the tunnel it's in (the cavern's, in it).</summary>
	public int NearestConnected(Vector3 p)
	{
		if (InCavern(p)) return Cavern;
		var (a, b) = EdgeAt(p);
		return Flat(p).DistanceSquaredTo(Flat(Nodes[a])) <= Flat(p).DistanceSquaredTo(Flat(Nodes[b])) ? a : b;
	}

	/// <summary>Distance between two points the way a sound goes: along the tunnels.</summary>
	public float TunnelDistance(Vector3 p, Vector3 q)
	{
		bool pc = InCavern(p), qc = InCavern(q);
		if (pc && qc) return Flat(p).DistanceTo(Flat(q));
		var ep = pc ? (Cavern, Cavern) : EdgeAt(p);
		var eq = qc ? (Cavern, Cavern) : EdgeAt(q);
		if ((ep.Item1 == eq.Item1 && ep.Item2 == eq.Item2) || (ep.Item1 == eq.Item2 && ep.Item2 == eq.Item1)) return Flat(p).DistanceTo(Flat(q));
		float best = float.MaxValue;
		foreach (int i in new[] { ep.Item1, ep.Item2 })
			foreach (int j in new[] { eq.Item1, eq.Item2 })
			{
				var path = Path(i, j);
				if (path.Count == 0 && i != j) continue;
				float d = Flat(p).DistanceTo(Flat(Nodes[i])) + (i == j ? 0f : PathLength(path)) + Flat(Nodes[j]).DistanceTo(Flat(q));
				best = Mathf.Min(best, d);
			}
		return best;
	}

	private static Vector3 Flat(Vector3 v) => v with { Y = 0 };

	public static bool InCavern(Vector3 p)
	{
		var d = p - CavernCentre;
		return d.X * d.X / (CavernRX * CavernRX) + d.Z * d.Z / (CavernRZ * CavernRZ) < 1f;
	}
}
