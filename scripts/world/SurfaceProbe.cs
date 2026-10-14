using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Where a thing set down really rests (2026-10-14d). Placing by a physics ray finds the collision shapes, which are boxes
/// that often stand a little proud of what's drawn (a table's box runs up past its top): things set down by ray floated
/// up to 2 cm over the drawn top. This finds the drawn surface instead: straight down through every mesh under a root, the
/// highest upward-facing triangle under a point. Each mesh's triangles are put once, lazily, into a grid of half-metre
/// columns in world space, so a probe tests only the handful in its column. For build time (setting dressing down), not
/// per frame.
/// </summary>
public sealed class SurfaceProbe
{
	private const float Cell = 0.5f;
	private readonly List<MeshInstance3D> _meshes = new();
	private readonly Dictionary<MeshInstance3D, Dictionary<Vector2I, List<int>>> _grids = new();
	private readonly Dictionary<MeshInstance3D, Vector3[]> _tris = new();
	private readonly List<(MeshInstance3D mi, Aabb box)> _boxes = new();

	/// <summary>Every mesh (shown or not: a room hidden while the player is elsewhere still counts) under <paramref name="root"/> (but those under <paramref name="skip"/>).</summary>
	public SurfaceProbe(Node root, Node skip = null)
	{
		void Walk(Node n)
		{
			if (n == skip) return;
			if (n is MeshInstance3D mi && mi.Mesh != null && mi.IsInsideTree()) { _meshes.Add(mi); _boxes.Add((mi, mi.GlobalTransform * mi.GetAabb())); }
			foreach (var c in n.GetChildren()) Walk(c);
		}
		Walk(root);
	}

	/// <summary>The drawn surface's height under <paramref name="at"/> (world), between <paramref name="below"/> and
	/// <paramref name="above"/> metres of its height; null when nothing drawn is there.</summary>
	public float? TopNear(Vector3 at, float below = 0.15f, float above = 0.05f)
	{
		float? best = null;
		float lo = at.Y - below, hi = at.Y + above;
		foreach (var (mi, box) in _boxes)
		{
			if (at.X < box.Position.X || at.X > box.End.X || at.Z < box.Position.Z || at.Z > box.End.Z) continue;
			if (box.End.Y < lo || box.Position.Y > hi) continue;
			var grid = Grid(mi);
			var tris = _tris[mi];
			if (!grid.TryGetValue(new Vector2I(Mathf.FloorToInt(at.X / Cell), Mathf.FloorToInt(at.Z / Cell)), out var list)) continue;
			foreach (int t in list)
			{
				Vector3 a = tris[t], b = tris[t + 1], c = tris[t + 2];
				var n = (b - a).Cross(c - a);
				if (n.Y < 0f) n = -n;
				if (n.LengthSquared() < 1e-12f || n.Normalized().Y < 0.7f) continue;
				// (where the vertical line through the point crosses the triangle, in XZ by barycentrics)
				float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
				if (Mathf.Abs(d) < 1e-9f) continue;
				float w1 = ((b.Z - c.Z) * (at.X - c.X) + (c.X - b.X) * (at.Z - c.Z)) / d;
				float w2 = ((c.Z - a.Z) * (at.X - c.X) + (a.X - c.X) * (at.Z - c.Z)) / d;
				float w3 = 1f - w1 - w2;
				if (w1 < 0f || w2 < 0f || w3 < 0f) continue;
				float y = w1 * a.Y + w2 * b.Y + w3 * c.Y;
				if (y < lo || y > hi) continue;
				if (best == null || y > best) best = y;
			}
		}
		return best;
	}

	private Dictionary<Vector2I, List<int>> Grid(MeshInstance3D mi)
	{
		if (_grids.TryGetValue(mi, out var g)) return g;
		g = new Dictionary<Vector2I, List<int>>();
		var faces = mi.Mesh.GetFaces();
		var xf = mi.GlobalTransform;
		var tris = new Vector3[faces.Length];
		for (int i = 0; i < faces.Length; i++) tris[i] = xf * faces[i];
		for (int t = 0; t + 2 < tris.Length; t += 3)
		{
			Vector3 a = tris[t], b = tris[t + 1], c = tris[t + 2];
			int x0 = Mathf.FloorToInt(Mathf.Min(a.X, Mathf.Min(b.X, c.X)) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.X, Mathf.Max(b.X, c.X)) / Cell);
			int z0 = Mathf.FloorToInt(Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z)) / Cell);
			if ((x1 - x0 + 1) * (z1 - z0 + 1) > 400) continue;   // (a floor or a ceiling across the whole room: never a table top)
			for (int x = x0; x <= x1; x++)
				for (int z = z0; z <= z1; z++)
				{
					var key = new Vector2I(x, z);
					if (!g.TryGetValue(key, out var list)) g[key] = list = new List<int>();
					list.Add(t);
				}
		}
		_tris[mi] = tris;
		_grids[mi] = g;
		return g;
	}
}
