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
	}

	private readonly Dictionary<Material, Surf> _surfs = new();
	private readonly List<Material> _order = new();
	private Surf _cur;

	/// <summary>Current vertex colour (multiplied into albedo when the material uses vertex colours).</summary>
	public Color Color = Colors.White;
	/// <summary>Transform applied to everything added.</summary>
	public Transform3D Xf = Transform3D.Identity;

	public MeshKit Mat(Material m)
	{
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
		if (face.Dot(na + nb + nc) > 0f) { (b, c) = (c, b); (nb, nc) = (nc, nb); (ub, uc) = (uc, ub); }
		int i0 = Vert(a, na, ua), i1 = Vert(b, nb, ub), i2 = Vert(c, nc, uc);
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

	public ArrayMesh Commit()
	{
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
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
			mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, m);
		}
		return mesh;
	}

	/// <summary>Commit into a new MeshInstance3D added under parent.</summary>
	public MeshInstance3D CommitTo(Node parent, string name = "Mesh", bool castShadows = true)
	{
		var mi = new MeshInstance3D { Name = name, Mesh = Commit() };
		mi.CastShadow = castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
		parent.AddChild(mi);
		return mi;
	}
}
