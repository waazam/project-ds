using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The joints audit (2026-10-13, the owner: "fix any objects that have disconnects or gaps between joints"). Splits every
/// visible mesh under a node into its islands (the pieces whose triangles share no corner: a leg, a rail, a claw), in world
/// space and as posed (skinned meshes are baked at their current pose, so a gap that opens mid-stride shows), and lists
/// every island that touches nothing: its box clear of every other island's box by more than <see cref="Touch"/> but less
/// than <see cref="Near"/> (a near miss: a part that was meant to meet something and doesn't). Pieces far from everything
/// are left alone (a separate object), and boxes that overlap are taken as joined (so it never cries wolf on a joint, and
/// a gap along a diagonal can slip past it: the screenshots cover those).
///
/// Used by gap_audit.tscn (<see cref="GapAuditPreview"/>: the furniture and winter models, the creatures posed) and by the
/// playtest with <c>--gap-audit</c> (every beat's screenshot scans the level as it stands then). Reports to
/// test-output/gaps/. Not used by the game.
/// </summary>
public static class GapAudit
{
	/// <summary>Closer than this counts as touching (a bevel's tolerance).</summary>
	public static float Touch = 0.004f;
	/// <summary>Farther than this from everything is a separate object, not a broken joint.</summary>
	public static float Near = 0.25f;
	/// <summary>Islands smaller than this across are ignored (crumbs, specks, a single quad of grit).</summary>
	public static float MinSize = 0.012f;
	/// <summary>Meshes bigger than this are skipped (terrain, the forest's merged ground): the ground audit covers them.</summary>
	public static int MaxVerts = 1_500_000;
	/// <summary>The meshes the last scan skipped as too big.</summary>
	public static readonly List<string> Skipped = new();

	public static bool Requested => System.Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--gap-audit");

	public record Island(string Owner, string Surface, Aabb Box, int Tris, Vector3[] Points = null);

	/// <summary>Keep each island's corners and measure corner to corner as well (<see cref="CloseMisses"/>): finds the gaps
	/// boxes miss (a toe beside its foot: their boxes overlap). Heavier; for a creature, not a level.</summary>
	public static bool Precise;
	public record Finding(Island Piece, float Gap, Island Nearest);

	/// <summary>Every island of every visible mesh under <paramref name="root"/>, in world space.</summary>
	public static List<Island> Islands(Node root, System.Func<Node, bool> skip = null)
	{
		var all = new List<Island>();
		foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
		{
			if (n is not MeshInstance3D mi || mi.Mesh == null || !mi.IsVisibleInTree()) continue;
			if (skip != null && skip(mi)) continue;
			if (mi.MaterialOverride is ShaderMaterial { Shader: { } sh } && sh.ResourcePath.Contains("uv_ink")) continue;
			Mesh mesh = mi.Mesh;
			if (mi.Skin != null && mi.GetNodeOrNull<Skeleton3D>(mi.Skeleton) != null)
			{
				try { mesh = mi.BakeMeshFromCurrentSkeletonPose(); } catch { }
				if (mesh == null) mesh = mi.Mesh;
			}
			var xf = mi.GlobalTransform;
			string owner = mi.GetPath().ToString();
			for (int s = 0; s < mesh.GetSurfaceCount(); s++)
			{
				if (mesh is ArrayMesh am && am.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) continue;
				var arr = mesh.SurfaceGetArrays(s);
				var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
				if (v.Length == 0) continue;
				if (v.Length > MaxVerts) { Skipped.Add($"{owner} s{s} ({v.Length} verts)"); continue; }
				var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
				var mat = mi.GetActiveMaterial(s);
				string surf = $"s{s} {mat?.ResourceName}".Trim();
				foreach (var isl in Split(v, idx, xf)) all.Add(new Island(owner, surf, isl.box, isl.tris, isl.pts));
			}
		}
		return all;
	}

	/// <summary>A surface's triangles split into islands (corners welded at half a millimetre), each as a world box.</summary>
	private static IEnumerable<(Aabb box, int tris, Vector3[] pts)> Split(Vector3[] v, int[] idx, Transform3D xf)
	{
		int n = v.Length;
		var weld = new Dictionary<(long, long, long), int>(n);
		var id = new int[n];
		for (int i = 0; i < n; i++)
		{
			var key = ((long)Mathf.Round(v[i].X * 2000f), (long)Mathf.Round(v[i].Y * 2000f), (long)Mathf.Round(v[i].Z * 2000f));
			if (!weld.TryGetValue(key, out int w)) weld[key] = w = i;
			id[i] = w;
		}
		var parent = new int[n];
		for (int i = 0; i < n; i++) parent[i] = i;
		int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
		void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
		int triCount = idx != null ? idx.Length / 3 : n / 3;
		for (int t = 0; t < triCount; t++)
		{
			int a = idx != null ? idx[t * 3] : t * 3, b = idx != null ? idx[t * 3 + 1] : t * 3 + 1, c = idx != null ? idx[t * 3 + 2] : t * 3 + 2;
			Union(id[a], id[b]); Union(id[b], id[c]);
		}
		var boxes = new Dictionary<int, (Aabb box, int tris, bool any)>();
		var pts = Precise ? new Dictionary<int, HashSet<int>>() : null;
		for (int t = 0; t < triCount; t++)
		{
			int a = idx != null ? idx[t * 3] : t * 3;
			int root = Find(id[a]);
			boxes.TryGetValue(root, out var e);
			for (int k = 0; k < 3; k++)
			{
				int vi = idx != null ? idx[t * 3 + k] : t * 3 + k;
				var p = xf * v[vi];
				if (pts != null)
				{
					if (!pts.TryGetValue(root, out var hs)) pts[root] = hs = new HashSet<int>();
					hs.Add(id[vi]);
				}
				if (!e.any) { e.box = new Aabb(p, Vector3.Zero); e.any = true; }
				else e.box = e.box.Expand(p);
			}
			e.tris++;
			boxes[root] = e;
		}
		foreach (var (r, e) in boxes) yield return (e.box, e.tris, pts?[r].Select(i => xf * v[i]).ToArray());
	}

	/// <summary>(<see cref="Precise"/>) The islands whose boxes touch a neighbour's but whose nearest corner to any other
	/// island's is more than <paramref name="minGap"/> away (up to 3 cm is measured; beyond that it says 30 mm). A part
	/// sunk wholly inside another (a claw's root in a finger, an eye in its socket) can show here too: check the shot.</summary>
	public static List<Finding> CloseMisses(List<Island> islands, float minGap)
	{
		const float cell = 0.03f;
		var grid = new Dictionary<(int, int, int), List<(Vector3 p, int isl)>>();
		(int, int, int) C(Vector3 p) => (Mathf.FloorToInt(p.X / cell), Mathf.FloorToInt(p.Y / cell), Mathf.FloorToInt(p.Z / cell));
		for (int i = 0; i < islands.Count; i++)
			if (islands[i].Points != null)
				foreach (var p in islands[i].Points)
				{
					var c = C(p);
					if (!grid.TryGetValue(c, out var l)) grid[c] = l = new List<(Vector3, int)>();
					l.Add((p, i));
				}
		var found = new List<Finding>();
		for (int i = 0; i < islands.Count; i++)
		{
			var a = islands[i];
			if (a.Points == null || a.Box.Size[(int)a.Box.Size.MaxAxisIndex()] < MinSize) continue;
			float best = cell; int bestJ = -1;
			foreach (var p in a.Points)
			{
				var c = C(p);
				for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
					if (grid.TryGetValue((c.Item1 + x, c.Item2 + y, c.Item3 + z), out var l))
						foreach (var (q, j) in l)
						{
							if (j == i) continue;
							float d = p.DistanceTo(q);
							if (d < best) { best = d; bestJ = j; }
						}
				if (best <= minGap) break;
			}
			if (best > minGap) found.Add(new Finding(a, best, bestJ >= 0 ? islands[bestJ] : a));
		}
		return found;
	}

	private static float BoxGap(Aabb a, Aabb b)
	{
		Vector3 amin = a.Position, amax = a.End, bmin = b.Position, bmax = b.End;
		float dx = Mathf.Max(0f, Mathf.Max(amin.X - bmax.X, bmin.X - amax.X));
		float dy = Mathf.Max(0f, Mathf.Max(amin.Y - bmax.Y, bmin.Y - amax.Y));
		float dz = Mathf.Max(0f, Mathf.Max(amin.Z - bmax.Z, bmin.Z - amax.Z));
		return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
	}

	/// <summary>The islands that touch nothing but have a neighbour within <see cref="Near"/>.</summary>
	public static List<Finding> NearMisses(List<Island> islands)
	{
		const float cell = 0.5f;
		var grid = new Dictionary<(int, int, int), List<int>>();
		(int, int, int) C(Vector3 p) => (Mathf.FloorToInt(p.X / cell), Mathf.FloorToInt(p.Y / cell), Mathf.FloorToInt(p.Z / cell));
		for (int i = 0; i < islands.Count; i++)
		{
			var b = islands[i].Box.Grow(Near);
			var lo = C(b.Position); var hi = C(b.End);
			if ((long)(hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1) > 4000) continue;   // huge (a wall, the ground): found from the small side
			for (int x = lo.Item1; x <= hi.Item1; x++) for (int y = lo.Item2; y <= hi.Item2; y++) for (int z = lo.Item3; z <= hi.Item3; z++)
			{
				if (!grid.TryGetValue((x, y, z), out var l)) grid[(x, y, z)] = l = new List<int>();
				l.Add(i);
			}
		}
		var big = Enumerable.Range(0, islands.Count).Where(i =>
		{
			var b = islands[i].Box.Grow(Near);
			var lo = C(b.Position); var hi = C(b.End);
			return (long)(hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1) > 4000;
		}).ToList();
		var found = new List<Finding>();
		for (int i = 0; i < islands.Count; i++)
		{
			var a = islands[i];
			if (a.Tris <= 2 || a.Box.Size[(int)a.Box.Size.MaxAxisIndex()] < MinSize) continue;   // (a lone quad: a foliage or decal card)
			float best = float.MaxValue; int bestJ = -1;
			var seen = new HashSet<int>();
			// every island within Near of this one is listed in a cell this one's own box covers
			var lo = C(a.Box.Position); var hi = C(a.Box.End);
			IEnumerable<int> cand;
			if ((long)(hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1) > 4000) cand = Enumerable.Range(0, islands.Count);
			else
			{
				var list = new List<int>();
				for (int x = lo.Item1; x <= hi.Item1; x++) for (int y = lo.Item2; y <= hi.Item2; y++) for (int z = lo.Item3; z <= hi.Item3; z++)
					if (grid.TryGetValue((x, y, z), out var l)) list.AddRange(l);
				cand = list;
			}
			foreach (int j in cand.Concat(big))
			{
				if (j == i || !seen.Add(j)) continue;
				float g = BoxGap(a.Box, islands[j].Box);
				if (g < best) { best = g; bestJ = j; }
				if (best <= Touch) break;
			}
			if (best > Touch && best < Near && bestJ >= 0) found.Add(new Finding(a, best, islands[bestJ]));
		}
		return found;
	}

	/// <summary>Scans <paramref name="root"/> and writes test-output/gaps/<paramref name="tag"/>.txt; returns the findings.</summary>
	public static List<Finding> Scan(Node root, string tag, System.Func<Node, bool> skip = null)
	{
		Skipped.Clear();
		var islands = Islands(root, skip);
		var found = NearMisses(islands);
		if (Precise)
		{
			var had = found.Select(f => f.Piece).ToHashSet();
			found.AddRange(CloseMisses(islands, Touch).Where(f => !had.Contains(f.Piece)));
		}
		// widest gaps first (hair cards and rag strips, which hang loose by design, last)
		found = found.OrderBy(f => f.Piece.Surface.Contains("hair") || f.Piece.Surface.Contains("rag") ? 1 : 0)
			.ThenByDescending(f => f.Gap).ToList();
		string dir = ProjectSettings.GlobalizePath("res://test-output/gaps");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		var sb = new StringBuilder();
		sb.AppendLine($"# {tag}: {islands.Count} islands, {found.Count} near misses (touch {Touch * 1000:0} mm, near {Near * 100:0} cm)");
		foreach (var sk in Skipped) sb.AppendLine($"skipped (too big): {sk}");
		foreach (var f in found)
		{
			var b = f.Piece.Box;
			sb.AppendLine($"gap {f.Gap * 1000f,6:0.0} mm  size {b.Size.X:0.000}x{b.Size.Y:0.000}x{b.Size.Z:0.000} at {b.GetCenter():0.00}  {f.Piece.Owner} [{f.Piece.Surface}] tris {f.Piece.Tris}");
			sb.AppendLine($"      nearest {f.Nearest.Box.GetCenter():0.00} size {f.Nearest.Box.Size:0.000} {f.Nearest.Owner} [{f.Nearest.Surface}]");
		}
		System.IO.File.WriteAllText($"{dir}/{tag}.txt", sb.ToString());
		GD.Print($"[gap-audit] {tag}: {islands.Count} islands, {found.Count} near misses");
		return found;
	}
}
