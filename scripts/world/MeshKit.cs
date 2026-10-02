using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Tiny immediate-mode mesh builder for low-poly procedural props. Geometry is
/// grouped by material; Commit() produces one ArrayMesh with a surface per
/// material. Faces are wound so the given normal is the front face
/// (Godot: clockwise front faces).
/// </summary>
public class MeshKit
{
	private class Surf
	{
		public readonly List<Vector3> V = new();
		public readonly List<Vector3> N = new();
		public readonly List<Vector2> UV = new();
		public readonly List<Color> C = new();
		public readonly List<int> I = new();
		/// <summary>Tangents (x, y, z, w per vertex): only for geometry taken from a modelled mesh (<see cref="AddMesh"/>),
		/// whose normal maps need them; a surface is never both.</summary>
		public readonly List<float> T = new();
		/// <summary>The second UV set (a modelled piece's baked maps), likewise only for <see cref="AddMesh"/>'s geometry.</summary>
		public readonly List<Vector2> UV2 = new();
	}

	private readonly Dictionary<Material, Surf> _surfs = new();
	private readonly List<Material> _order = new();
	private Surf _cur;

	/// <summary>Current vertex colour (multiplied into albedo when the material uses vertex colours).</summary>
	public Color Color = Colors.White;
	/// <summary>Transform applied to everything added.</summary>
	public Transform3D Xf = Transform3D.Identity;

	/// <summary>The material being drawn with (null before the first <see cref="Mat"/>).</summary>
	public Material CurrentMaterial { get; private set; }

	public MeshKit Mat(Material m)
	{
		CurrentMaterial = m;
		if (!_surfs.TryGetValue(m, out _cur))
		{
			_cur = new Surf();
			_surfs[m] = _cur;
			_order.Add(m);
		}
		return this;
	}

	private int Vert(Vector3 p, Vector3 n, Vector2 uv)
	{
		_cur.V.Add(Xf * p);
		_cur.N.Add((Xf.Basis * n).Normalized());
		_cur.UV.Add(uv);
		_cur.C.Add(Color);
		return _cur.V.Count - 1;
	}

	/// <summary>Triangle with explicit per-vertex normals; winding fixed so the averaged normal faces front.</summary>
	public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc)
	{
		var face = (b - a).Cross(c - a);
		if (face.LengthSquared() < 4e-14f) return;   // a sliver under 0.1 mm² (a cone's tip, a closed cap's centre): nothing to draw
		if (face.Dot(na + nb + nc) > 0f) { (b, c) = (c, b); (nb, nc) = (nc, nb); (ub, uc) = (uc, ub); }
		int i0 = Vert(a, na, ua), i1 = Vert(b, nb, ub), i2 = Vert(c, nc, uc);
		_cur.I.Add(i0); _cur.I.Add(i1); _cur.I.Add(i2);
	}

	/// <summary>Triangle whose corners carry their own colours (a blend across it: the winter ground's road and ice).</summary>
	public void TriC(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc, Color ca, Color cb, Color cc)
	{
		var face = (b - a).Cross(c - a);
		if (face.LengthSquared() < 4e-14f) return;
		if (face.Dot(na + nb + nc) > 0f) { (b, c) = (c, b); (nb, nc) = (nc, nb); (ub, uc) = (uc, ub); (cb, cc) = (cc, cb); }
		var keep = Color;
		Color = ca; int i0 = Vert(a, na, ua);
		Color = cb; int i1 = Vert(b, nb, ub);
		Color = cc; int i2 = Vert(c, nc, uc);
		Color = keep;
		_cur.I.Add(i0); _cur.I.Add(i1); _cur.I.Add(i2);
	}

	public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc)
		=> Tri(a, b, c, n, n, n, ua, ub, uc);

	/// <summary>Quad a-b-c-d (in order around the edge) facing n.</summary>
	public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
	{
		Tri(a, b, c, n, ua, ub, uc);
		Tri(a, c, d, n, ua, uc, ud);
	}

	public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
		=> Quad(a, b, c, d, n, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));

	/// <summary>Double-sided quad (two faces), e.g. foliage cards.</summary>
	public void Card(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
	{
		Quad(a, b, c, d, n, ua, ub, uc, ud);
		Quad(a, b, c, d, -n, ua, ub, uc, ud);
	}

	/// <summary>
	/// Axis-aligned (in local space) box centred at c with full size s. UVs are
	/// world-scaled: uvScale texture repeats per metre.
	/// </summary>
	public void Box(Vector3 c, Vector3 s, float uvScale = 1f, Basis? rot = null)
	{
		Basis b = rot ?? Basis.Identity;
		Vector3 h = s * 0.5f;
		Vector3 P(float x, float y, float z) => c + b * new Vector3(x * h.X, y * h.Y, z * h.Z);
		Vector3 ax = b.X, ay = b.Y, az = b.Z;
		float u = uvScale;
		// +Y / -Y
		Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), ay,
			new Vector2(0, 0), new Vector2(s.X * u, 0), new Vector2(s.X * u, s.Z * u), new Vector2(0, s.Z * u));
		Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -ay,
			new Vector2(0, 0), new Vector2(s.X * u, 0), new Vector2(s.X * u, s.Z * u), new Vector2(0, s.Z * u));
		// +Z / -Z
		Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), az,
			new Vector2(0, s.Y * u), new Vector2(s.X * u, s.Y * u), new Vector2(s.X * u, 0), new Vector2(0, 0));
		Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -az,
			new Vector2(0, s.Y * u), new Vector2(s.X * u, s.Y * u), new Vector2(s.X * u, 0), new Vector2(0, 0));
		// +X / -X
		Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), ax,
			new Vector2(0, s.Y * u), new Vector2(s.Z * u, s.Y * u), new Vector2(s.Z * u, 0), new Vector2(0, 0));
		Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -ax,
			new Vector2(0, s.Y * u), new Vector2(s.Z * u, s.Y * u), new Vector2(s.Z * u, 0), new Vector2(0, 0));
	}

	/// <summary>Box spanning between two points (a beam), with the given cross-section (width along 'side', height along the rest).</summary>
	public void Beam(Vector3 from, Vector3 to, float width, float height, float uvScale = 1f, Vector3? upHint = null)
	{
		Vector3 dir = to - from;
		float len = dir.Length();
		if (len < 1e-4f) return;
		Vector3 z = dir / len;
		Vector3 up = upHint ?? Vector3.Up;
		if (Mathf.Abs(z.Dot(up)) > 0.98f) up = Vector3.Right;
		Vector3 x = up.Cross(z).Normalized();
		Vector3 y = z.Cross(x).Normalized();
		Box((from + to) * 0.5f, new Vector3(width, height, len), uvScale, new Basis(x, y, z));
	}

	/// <summary>Cylinder / frustum from p0 to p1. Smooth sides, optional caps.</summary>
	public void Cylinder(Vector3 p0, Vector3 p1, float r0, float r1, int sides, bool caps = true, float uvScale = 1f, float twist = 0f)
	{
		Vector3 axis = p1 - p0;
		float len = axis.Length();
		Vector3 z = axis / len;
		Vector3 refv = Mathf.Abs(z.Y) > 0.9f ? Vector3.Right : Vector3.Up;
		Vector3 x = refv.Cross(z).Normalized();
		Vector3 y = z.Cross(x);
		float circ = Mathf.Tau * Mathf.Max(r0, r1);
		float slope = (r0 - r1) / len;
		for (int i = 0; i < sides; i++)
		{
			float a0 = twist + Mathf.Tau * i / sides, a1 = twist + Mathf.Tau * (i + 1) / sides;
			Vector3 d0 = x * Mathf.Cos(a0) + y * Mathf.Sin(a0);
			Vector3 d1 = x * Mathf.Cos(a1) + y * Mathf.Sin(a1);
			Vector3 n0 = (d0 + z * slope).Normalized(), n1 = (d1 + z * slope).Normalized();
			float u0 = (float)i / sides * circ * uvScale, u1 = (float)(i + 1) / sides * circ * uvScale;
			Vector3 a = p0 + d0 * r0, b = p0 + d1 * r0, c = p1 + d1 * r1, d = p1 + d0 * r1;
			Tri(a, b, c, n0, n1, n1, new Vector2(u0, len * uvScale), new Vector2(u1, len * uvScale), new Vector2(u1, 0));
			if (r1 > 1e-4f) Tri(a, c, d, n0, n1, n0, new Vector2(u0, len * uvScale), new Vector2(u1, 0), new Vector2(u0, 0));
			if (caps)
			{
				Vector2 cu(Vector3 dd, float r) => new Vector2(0.5f + dd.Dot(x) * r * uvScale, 0.5f + dd.Dot(y) * r * uvScale);
				Tri(p0, a, b, -z, new Vector2(0.5f, 0.5f), cu(d0, r0), cu(d1, r0));
				if (r1 > 1e-4f) Tri(p1, d, c, z, new Vector2(0.5f, 0.5f), cu(d0, r1), cu(d1, r1));
			}
		}
	}

	/// <summary>Convex polygon (points in the YZ plane, given as (z,y)) extruded along X from x0 to x1.</summary>
	public void ExtrudeX(IList<Vector2> zy, float x0, float x1, float uvScale = 1f)
	{
		int n = zy.Count;
		Vector3 P(float x, int i) => new Vector3(x, zy[i].Y, zy[i].X);
		Vector2 U(int i) => new Vector2(-zy[i].X * uvScale, -zy[i].Y * uvScale);
		for (int i = 1; i < n - 1; i++)
		{
			Tri(P(x1, 0), P(x1, i), P(x1, i + 1), Vector3.Right, U(0), U(i), U(i + 1));
			Tri(P(x0, 0), P(x0, i), P(x0, i + 1), Vector3.Left, U(0), U(i), U(i + 1));
		}
		// Edges: outward normal from polygon centroid
		Vector2 cen = Vector2.Zero;
		foreach (var p in zy) cen += p;
		cen /= n;
		for (int i = 0; i < n; i++)
		{
			int j = (i + 1) % n;
			Vector2 e = zy[j] - zy[i];
			Vector2 nn = new Vector2(e.Y, -e.X).Normalized();
			if (nn.Dot((zy[i] + zy[j]) * 0.5f - cen) < 0) nn = -nn;
			Vector3 nrm = new Vector3(0, nn.Y, nn.X);
			float el = e.Length() * uvScale, w = Mathf.Abs(x1 - x0) * uvScale;
			Quad(P(x0, i), P(x1, i), P(x1, j), P(x0, j), nrm,
				new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, el), new Vector2(0, el));
		}
	}

	/// <summary>Jittered low-poly blob (icosphere, 1 subdivision) with flat-ish shading.</summary>
	public void Blob(Vector3 c, Vector3 radii, int seed, float jitter = 0.18f, bool flat = true, float uvScale = 1f, float shadeBottom = 0f)
	{
		var (verts, tris) = Icosphere(1);
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		var disp = new Vector3[verts.Count];
		for (int i = 0; i < verts.Count; i++)
		{
			float k = 1f + rng.RandfRange(-jitter, jitter);
			disp[i] = c + verts[i] * radii * k;
		}
		Color baseColor = Color;
		foreach (var t in tris)
		{
			Vector3 a = disp[t.X], b = disp[t.Y], d = disp[t.Z];
			if (shadeBottom > 0f)
			{
				float hy = (verts[t.X].Y + verts[t.Y].Y + verts[t.Z].Y) / 3f * 0.5f + 0.5f;
				Color = baseColor * (1f - shadeBottom * (1f - hy));
				Color.A = 1f;
			}
			Vector3 fn = (b - a).Cross(d - a).Normalized();
			if (fn.Dot((a + b + d) / 3f - c) < 0) fn = -fn;
			Vector3 na = flat ? fn : verts[t.X], nb = flat ? fn : verts[t.Y], nd = flat ? fn : verts[t.Z];
			Vector2 U(Vector3 p) => new Vector2(p.X + p.Z * 0.7f, p.Y + p.Z * 0.3f) * uvScale;
			Tri(a, b, d, na, nb, nd, U(a), U(b), U(d));
		}
		Color = baseColor;
	}

	private static (List<Vector3>, List<Vector3I>) _ico1;
	public static (List<Vector3>, List<Vector3I>) Icosphere(int subdiv)
	{
		if (subdiv == 1 && _ico1.Item1 != null) return _ico1;
		float t = (1f + Mathf.Sqrt(5f)) / 2f;
		var v = new List<Vector3>
		{
			new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
			new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
			new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
		};
		for (int i = 0; i < v.Count; i++) v[i] = v[i].Normalized();
		var f = new List<Vector3I>
		{
			new(0, 11, 5), new(0, 5, 1), new(0, 1, 7), new(0, 7, 10), new(0, 10, 11),
			new(1, 5, 9), new(5, 11, 4), new(11, 10, 2), new(10, 7, 6), new(7, 1, 8),
			new(3, 9, 4), new(3, 4, 2), new(3, 2, 6), new(3, 6, 8), new(3, 8, 9),
			new(4, 9, 5), new(2, 4, 11), new(6, 2, 10), new(8, 6, 7), new(9, 8, 1),
		};
		for (int s = 0; s < subdiv; s++)
		{
			var mid = new Dictionary<long, int>();
			int Mid(int a, int b)
			{
				long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
				if (mid.TryGetValue(key, out int idx)) return idx;
				v.Add(((v[a] + v[b]) * 0.5f).Normalized());
				mid[key] = v.Count - 1;
				return v.Count - 1;
			}
			var nf = new List<Vector3I>();
			foreach (var tri in f)
			{
				int a = Mid(tri.X, tri.Y), b = Mid(tri.Y, tri.Z), c = Mid(tri.Z, tri.X);
				nf.Add(new(tri.X, a, c)); nf.Add(new(tri.Y, b, a)); nf.Add(new(tri.Z, c, b)); nf.Add(new(a, b, c));
			}
			f = nf;
		}
		var res = (v, f);
		if (subdiv == 1) _ico1 = res;
		return res;
	}

	public bool IsEmpty => _order.Count == 0;

	/// <summary>Moves a node a hair (1.5 mm by default) along a world direction: for a piece laid flush on
	/// another mesh's surface (a frame on a wall, a threshold on a floor), so the two don't fight over the same
	/// plane (the clip audit). Far below anything visible; the depth buffer tells them apart.</summary>
	/// <summary>Gives a prop a collider (the collision audit: things the player could walk through): a box over
	/// its mesh's bounds, shrunk per axis by <paramref name="shrink"/> (a coat stand's arms, a flag on its pole).
	/// The collider goes and comes with the mesh's visibility (the lobby swaps its decor by hiding groups).</summary>
	public static StaticBody3D Solidify(MeshInstance3D mi, Vector3? shrink = null, string surface = "wood", bool followVisibility = false)
	{
		if (mi?.Mesh == null) return null;
		var aabb = mi.Mesh.GetAabb();
		var body = new StaticBody3D { Name = "Solid", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", surface);
		var shape = new CollisionShape3D { Position = aabb.GetCenter(), Shape = new BoxShape3D { Size = aabb.Size * (shrink ?? Vector3.One * 0.9f) } };
		body.AddChild(shape);
		mi.AddChild(body);
		if (followVisibility)
		{
			void Sync() { if (GodotObject.IsInstanceValid(shape)) shape.Disabled = !mi.IsVisibleInTree(); }
			mi.VisibilityChanged += Sync;
			Callable.From(Sync).CallDeferred();
		}
		return body;
	}

	/// <summary><see cref="Solidify"/> for every mesh under a prop's node.</summary>
	public static void SolidifyAll(Node n, Vector3? shrink = null, string surface = "wood", bool followVisibility = false)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is MeshInstance3D mi) Solidify(mi, shrink, surface, followVisibility);
			else SolidifyAll(c, shrink, surface, followVisibility);
		}
	}

	/// <summary>A lining laid into an opening or on a surface (a door's frame and threshold): shrunk a hair about
	/// its own centre, so its jambs sit just inside the opening's sides and its sill just up off the floor,
	/// instead of sharing their planes (the clip audit).</summary>
	public static void Shrink(MeshInstance3D mi, float f = 0.998f)
	{
		if (mi?.Mesh == null) return;
		Vector3 c = mi.Mesh.GetAabb().GetCenter();
		mi.Transform = mi.Transform * new Transform3D(Basis.FromScale(Vector3.One * f), c * (1f - f));
	}

	/// <summary>A whole shell set a hair aside on every axis (1.5 mm each), so none of its walls, floors or ceilings
	/// share a plane with a neighbouring shell's (the clip audit). The signs pick which side it goes.</summary>
	public static void NudgeAll(Node3D n, Vector3 signs, float d = 0.0015f) => Nudge(n, signs, d * signs.Length());

	public static void Nudge(Node3D n, Vector3 worldDir, float d = 0.0015f)
	{
		if (n == null) return;
		Vector3 local = n.GetParent() is Node3D p && p.IsInsideTree() ? p.GlobalBasis.Inverse() * worldDir.Normalized() : worldDir.Normalized();
		n.Position += local * d;
	}

	/// <summary>
	/// Resolves z-fighting inside the mesh (the clip audit): where a triangle of one surface lies in the plane of a
	/// triangle of another, facing the same way and overlapping it (a painted band on a post, a dial on its face, a
	/// sign's lettering on its board), the overlay is lifted 1.5 mm off along its normal, so it's drawn cleanly on
	/// top instead of flickering through. The overlay is the later-added surface (builders lay the base first),
	/// unless it's much the larger of the two.
	/// </summary>
	private void SeparateCoplanar()
	{
		if (_order.Count < 2) return;
		const float tol = 0.003f, lift = 0.0015f;
		var surfs = new List<Surf>();
		int total = 0;
		foreach (var m in _order) { surfs.Add(_surfs[m]); total += _surfs[m].I.Count / 3; }
		if (total > 150000) return;
		var buckets = new Dictionary<(int, int, int, int), List<(int s, int t, Vector3 n, float area)>>();
		for (int si = 0; si < surfs.Count; si++)
		{
			var sf = surfs[si];
			for (int t = 0; t < sf.I.Count / 3; t++)
			{
				Vector3 a = sf.V[sf.I[t * 3]], b = sf.V[sf.I[t * 3 + 1]], c = sf.V[sf.I[t * 3 + 2]];
				Vector3 cr = (b - a).Cross(c - a);
				float area = cr.Length() * 0.5f;
				if (area < 1e-6f) continue;
				Vector3 n = cr / (area * 2f);
				var key = (Mathf.RoundToInt(n.X * 40f), Mathf.RoundToInt(n.Y * 40f), Mathf.RoundToInt(n.Z * 40f), Mathf.RoundToInt(n.Dot(a) / tol));
				if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new();
				list.Add((si, t, n, area));
			}
		}
		var lifted = new Dictionary<(int s, int v), Vector3>();
		foreach (var (key, list) in buckets)
		{
			var near = list;
			if (buckets.TryGetValue((key.Item1, key.Item2, key.Item3, key.Item4 + 1), out var up)) { near = new List<(int s, int t, Vector3 n, float area)>(list); near.AddRange(up); }
			int first = near[0].s;
			bool mixed = false;
			foreach (var e in near) if (e.s != first) { mixed = true; break; }
			if (!mixed) continue;
			Vector3 n0 = near[0].n;
			Vector3 u = (Mathf.Abs(n0.Y) < 0.9f ? Vector3.Up : Vector3.Right).Cross(n0).Normalized(), w = n0.Cross(u);
			Vector2[] Proj(int si, int t)
			{
				var sf = surfs[si];
				var pts = new Vector2[3];
				for (int k = 0; k < 3; k++) { Vector3 p = sf.V[sf.I[t * 3 + k]]; pts[k] = new Vector2(p.Dot(u), p.Dot(w)); }
				return pts;
			}
			// a sweep over the triangles' boxes along one axis, so a big floor of many triangles stays cheap
			var items = new List<(int s, int t, Vector3 n, float area, Vector2[] pts, Vector2 min, Vector2 max)>(near.Count);
			foreach (var (si, ti, ni, ai) in near)
			{
				var pts = Proj(si, ti);
				items.Add((si, ti, ni, ai, pts, new Vector2(Mathf.Min(pts[0].X, Mathf.Min(pts[1].X, pts[2].X)), Mathf.Min(pts[0].Y, Mathf.Min(pts[1].Y, pts[2].Y))),
					new Vector2(Mathf.Max(pts[0].X, Mathf.Max(pts[1].X, pts[2].X)), Mathf.Max(pts[0].Y, Mathf.Max(pts[1].Y, pts[2].Y)))));
			}
			items.Sort((x, y) => x.min.X.CompareTo(y.min.X));
			for (int i = 0; i < items.Count; i++)
				for (int j = i + 1; j < items.Count && items[j].min.X < items[i].max.X; j++)
				{
					var A = items[i];
					var B = items[j];
					if (A.s == B.s || B.min.Y > A.max.Y || B.max.Y < A.min.Y || A.n.Dot(B.n) < 0.999f) continue;
					Vector3 pi = surfs[A.s].V[surfs[A.s].I[A.t * 3]], pj = surfs[B.s].V[surfs[B.s].I[B.t * 3]];
					if (Mathf.Abs(A.n.Dot(pj - pi)) > tol) continue;
					var inter = Geometry2D.IntersectPolygons(A.pts, B.pts);
					if (inter.Count == 0) continue;
					float area = 0f;
					foreach (var poly in inter)
						for (int k = 0; k < poly.Length; k++) area += poly[k].X * poly[(k + 1) % poly.Length].Y - poly[k].Y * poly[(k + 1) % poly.Length].X;
					if (Mathf.Abs(area) * 0.5f < 1e-5f) continue;
					// the overlay: the later surface, unless it's much bigger than the other
					bool liftB = B.s > A.s ? B.area < A.area * 4f : !(A.area < B.area * 4f);
					var L = liftB ? B : A;
					for (int k = 0; k < 3; k++) lifted[(L.s, surfs[L.s].I[L.t * 3 + k])] = L.n;
				}
		}
		foreach (var ((si, vi), n) in lifted) surfs[si].V[vi] += n * lift;
	}

	/// <summary>Adds a modelled mesh (assets/models: Blender's), transformed by <paramref name="xf"/> (then <see cref="Xf"/>),
	/// each of its surfaces into its own material's surface here, normals and tangents kept (so its baked normal maps
	/// hold), its vertex colour white. The kit's furniture stays one mesh per material, however many pieces.</summary>
	public void AddMesh(Mesh mesh, Transform3D xf, Dictionary<Material, Material> materials = null)
	{
		if (mesh == null) return;
		var full = Xf * xf;
		var nb = full.Basis.Inverse().Transposed();
		for (int si = 0; si < mesh.GetSurfaceCount(); si++)
		{
			var arr = mesh.SurfaceGetArrays(si);
			var mat = mesh.SurfaceGetMaterial(si);
			if (materials != null && mat != null && materials.TryGetValue(mat, out var swap)) mat = swap;
			if (mat == null) continue;
			Mat(mat);
			var v = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
			var n = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : (Vector3[])arr[(int)Mesh.ArrayType.Normal];
			var uv = arr[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? null : (Vector2[])arr[(int)Mesh.ArrayType.TexUV];
			var t = arr[(int)Mesh.ArrayType.Tangent].VariantType == Variant.Type.Nil ? null : (float[])arr[(int)Mesh.ArrayType.Tangent];
			var uv2 = arr[(int)Mesh.ArrayType.TexUV2].VariantType == Variant.Type.Nil ? null : (Vector2[])arr[(int)Mesh.ArrayType.TexUV2];
			var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : (int[])arr[(int)Mesh.ArrayType.Index];
			int b = _cur.V.Count;
			for (int i = 0; i < v.Length; i++)
			{
				_cur.V.Add(full * v[i]);
				_cur.N.Add(n != null ? (nb * n[i]).Normalized() : Vector3.Up);
				_cur.UV.Add(uv != null ? uv[i] : Vector2.Zero);
				_cur.C.Add(Colors.White);
				if (uv2 != null) _cur.UV2.Add(uv2[i]);
				if (t != null)
				{
					var tv = (full.Basis * new Vector3(t[i * 4], t[i * 4 + 1], t[i * 4 + 2])).Normalized();
					_cur.T.Add(tv.X); _cur.T.Add(tv.Y); _cur.T.Add(tv.Z); _cur.T.Add(t[i * 4 + 3] * (full.Basis.Determinant() < 0 ? -1f : 1f));
				}
			}
			if (idx != null) foreach (int i in idx) _cur.I.Add(b + i);
			else for (int i = 0; i < v.Length; i++) _cur.I.Add(b + i);
		}
	}

	public ArrayMesh Commit()
	{
		SeparateCoplanar();
		var mesh = new ArrayMesh();
		foreach (var m in _order)
		{
			var s = _surfs[m];
			if (s.I.Count == 0) continue;
			var arr = new Godot.Collections.Array();
			arr.Resize((int)Mesh.ArrayType.Max);
			arr[(int)Mesh.ArrayType.Vertex] = s.V.ToArray();
			arr[(int)Mesh.ArrayType.Normal] = s.N.ToArray();
			arr[(int)Mesh.ArrayType.TexUV] = s.UV.ToArray();
			arr[(int)Mesh.ArrayType.Color] = s.C.ToArray();
			arr[(int)Mesh.ArrayType.Index] = s.I.ToArray();
			if (s.T.Count == s.V.Count * 4 && s.T.Count > 0) arr[(int)Mesh.ArrayType.Tangent] = s.T.ToArray();
			if (s.UV2.Count == s.V.Count && s.UV2.Count > 0) arr[(int)Mesh.ArrayType.TexUV2] = s.UV2.ToArray();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
			mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, m);
		}
		return mesh;
	}

	// ------------------------------------------------------------------ occluders

	private List<Vector3> _occV;
	private List<int> _occI;

	/// <summary>A solid, opaque panel (a wall's span) that hides what's behind it: committed with the mesh as an
	/// occluder (occlusion culling, the optimization pass 2026-10-02: walls hid rooms from the eye, not from the
	/// renderer). Four corners in order, in the kit's current space.</summary>
	public void Occlude(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
	{
		_occV ??= new List<Vector3>();
		_occI ??= new List<int>();
		int o = _occV.Count;
		_occV.Add(Xf * a); _occV.Add(Xf * b); _occV.Add(Xf * c); _occV.Add(Xf * d);
		_occI.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
	}

	/// <summary>Commit into a new MeshInstance3D added under parent (and its occluders beside it, if it has any).</summary>
	public MeshInstance3D CommitTo(Node parent, string name = "Mesh", bool castShadows = true)
	{
		var mi = new MeshInstance3D { Name = name, Mesh = Commit() };
		mi.CastShadow = castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
		parent.AddChild(mi);
		if (_occV is { Count: > 0 })
		{
			var occ = new ArrayOccluder3D();
			occ.SetArrays(_occV.ToArray(), _occI.ToArray());
			parent.AddChild(new OccluderInstance3D { Name = name + "Occluder", Occluder = occ });
			_occV.Clear(); _occI.Clear();
		}
		return mi;
	}
}
