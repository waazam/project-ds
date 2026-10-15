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
		/// <summary>Its second UVs were written by a <see cref="Shade"/> (not a modelled piece's own).</summary>
		public bool Shaded;
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

	/// <summary>
	/// (2026-10-15, the owner: "a lot of the joints are too cylindrical and polygonal for a lot of the characters and props,
	/// can we clean that up") Round tubes: a <see cref="Cylinder"/> or <see cref="ItemMeshes.Loft"/> of 5 or more sides gets at
	/// least as many as its girth needs to read round (<see cref="SidesFor"/>; 3- and 4-sided ones are left alone: those are
	/// prisms on purpose, posts and beams), and where a cylinder carries straight on from the last one at an angle (an elbow,
	/// a knuckle, a pipe's bend, a branch's kink), a smooth ball fills the joint instead of two cut ends crossing.
	/// On by default; off for the builders whose meshes are instanced by the thousand (the forest scatter, the winter trees)
	/// and the creatures with their own passes, named by the source file that makes the kit (<see cref="RoughCallers"/>).
	/// </summary>
	public bool Smooth = true;

	/// <summary>Source files whose kits stay as they were (see <see cref="Smooth"/>).</summary>
	public static readonly string[] RoughCallers =
	{
		"ForestScatter", "WinterTreeKit", "WinterWoods.Trees", "Deer.cs", "Squirrel.cs", "Crawler.cs", "LakeCreature.cs",
		"Leviathan.cs", "TentacleKit.cs", "WindowTentacle.cs", "StalkerBody", "ShadowMan.cs",
	};

	/// <summary>`--rough-kit`: every kit as it was (for before/after comparisons).</summary>
	public static readonly bool RoughAll = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--rough-kit") >= 0;

	public MeshKit([System.Runtime.CompilerServices.CallerFilePath] string caller = "")
	{
		if (RoughAll) { Smooth = false; return; }
		if (!string.IsNullOrEmpty(caller))
			foreach (var r in RoughCallers)
				if (caller.Replace('\\', '/').Contains(r)) { Smooth = false; break; }
	}

	/// <summary>The fewest sides a round tube of radius <paramref name="r"/> reads round with at the PS2 look's scale
	/// (never fewer than asked; 3-4 sides stay: prisms on purpose).</summary>
	public int SidesFor(int sides, float r)
	{
		if (!Smooth || sides < 5) return sides;
		int min = r < 0.008f ? sides : r < 0.025f ? 6 : r < 0.07f ? 8 : r < 0.2f ? 12 : 16;
		return Mathf.Max(sides, min);
	}

	// the last cylinder, for the joint ball where the next carries on from it
	private bool _hasLast;
	private Vector3 _lastEnd, _lastDir;
	private float _lastR;
	private Material _lastMat;
	private Transform3D _lastXf;

	/// <summary>A smooth ball at a joint (radius <paramref name="r"/>), its normals round, for where two tubes meet.</summary>
	public void JointBall(Vector3 c, float r, int sides)
	{
		int rings = Mathf.Max(3, sides / 2);
		Vector3 P(int i, int j, out Vector3 n)
		{
			float th = Mathf.Pi * i / rings, ph = Mathf.Tau * j / sides;
			n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
			return c + n * r;
		}
		float circ = Mathf.Tau * r;
		for (int i = 0; i < rings; i++)
			for (int j = 0; j < sides; j++)
			{
				var a = P(i, j, out var na); var b = P(i, j + 1, out var nb);
				var cc = P(i + 1, j + 1, out var nc); var d = P(i + 1, j, out var nd);
				Vector2 U(int ii, int jj) => new((float)jj / sides * circ, (float)ii / rings * circ * 0.5f);
				if (i > 0) Tri(a, cc, b, na, nc, nb, U(i, j), U(i + 1, j + 1), U(i, j + 1));
				if (i < rings - 1) Tri(a, d, cc, na, nd, nc, U(i, j), U(i + 1, j), U(i + 1, j + 1));
			}
	}

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
		// (a surface already given second UVs by a Shade keeps them whole: what's added after gets none)
		if (_cur.UV2.Count > 0 && _cur.UV2.Count == _cur.V.Count && _cur.T.Count == 0) _cur.UV2.Add(Vector2.Zero);
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

	/// <summary>(2026-10-14g, the interiors kit) When above zero, every <see cref="Quad"/> (and so every box face,
	/// <see cref="BuildKit.Box"/>, a church wall's strips) longer than this many metres along a side is drawn as a grid of
	/// smaller quads, with an extra row 0.15 m in from each long edge: so a colour baked into the vertices
	/// (<see cref="RoomShade"/>: the dark at a wall's foot, in its corners, under its ceiling) has vertices to sit on. The
	/// PS2 lit its rooms per vertex the same way. 0.6 suits a room's walls, floor and ceiling; set it back to 0 for the
	/// small things after them.</summary>
	public float Tess;

	/// <summary>Where to cut a side <paramref name="len"/> long (fractions 0..1, ends included) for <see cref="Tess"/>.</summary>
	private static List<float> Cuts(float len, float tess)
	{
		var f = new List<float> { 0f, 1f };
		if (tess <= 0f || len <= tess) return f;
		int n = Mathf.CeilToInt(len / tess);
		for (int i = 1; i < n; i++) f.Add(i / (float)n);
		// a row close to each end, so a seam's dark falls off over 15 cm instead of a whole cell
		if (len > 0.8f)
			foreach (float e in new[] { 0.15f / len, 1f - 0.15f / len })
			{
				bool near = false;
				foreach (float x in f) if (Mathf.Abs(x - e) * len < 0.08f) { near = true; break; }
				if (!near) f.Add(e);
			}
		f.Sort();
		return f;
	}

	/// <summary>Quad a-b-c-d (in order around the edge) facing n.</summary>
	public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
	{
		if (Tess > 0f)
		{
			var cu = Cuts(Mathf.Max(a.DistanceTo(b), d.DistanceTo(c)), Tess);
			var cv = Cuts(Mathf.Max(a.DistanceTo(d), b.DistanceTo(c)), Tess);
			if (cu.Count > 2 || cv.Count > 2)
			{
				Vector3 P(float s, float t) => (a.Lerp(b, s)).Lerp(d.Lerp(c, s), t);
				Vector2 U(float s, float t) => (ua.Lerp(ub, s)).Lerp(ud.Lerp(uc, s), t);
				for (int j = 0; j < cv.Count - 1; j++)
					for (int i = 0; i < cu.Count - 1; i++)
					{
						float s0 = cu[i], s1 = cu[i + 1], t0 = cv[j], t1 = cv[j + 1];
						Tri(P(s0, t0), P(s1, t0), P(s1, t1), n, U(s0, t0), U(s1, t0), U(s1, t1));
						Tri(P(s0, t0), P(s1, t1), P(s0, t1), n, U(s0, t0), U(s1, t1), U(s0, t1));
					}
				return;
			}
		}
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

	/// <summary>(2026-10-14b, the graphics pass: "not too boxy") A box with its twelve edges chamfered by
	/// <paramref name="bevel"/> (clamped to under half its smallest side), so an edge catches a line of light instead of
	/// ending in a knife-cut: six faces, twelve chamfers, eight corner facets. UVs are each facet projected along its
	/// main axis, <paramref name="uvScale"/> repeats a metre, like <see cref="Box"/>.</summary>
	public void BevelBox(Vector3 c, Vector3 s, float bevel, float uvScale = 1f, Basis? rot = null)
	{
		Basis b = rot ?? Basis.Identity;
		Vector3 h = s * 0.5f;
		float e = Mathf.Min(bevel, Mathf.Min(h.X, Mathf.Min(h.Y, h.Z)) * 0.9f);
		if (e <= 0.0005f) { Box(c, s, uvScale, rot); return; }
		Vector3 L(int axis, int sx, int sy, int sz)
		{
			// the corner (sx, sy, sz)'s point on the face of the given axis
			return new Vector3(sx * (axis == 0 ? h.X : h.X - e), sy * (axis == 1 ? h.Y : h.Y - e), sz * (axis == 2 ? h.Z : h.Z - e));
		}
		Vector2 Uv(Vector3 p, Vector3 n)
		{
			Vector3 a = n.Abs();
			Vector2 q = a.X >= a.Y && a.X >= a.Z ? new Vector2(p.Z, -p.Y) : a.Y >= a.Z ? new Vector2(p.X, p.Z) : new Vector2(p.X, -p.Y);
			return q * uvScale;
		}
		void Q(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n)
		{
			Vector3 wn = (b * n).Normalized();
			Quad(c + b * p0, c + b * p1, c + b * p2, c + b * p3, wn, Uv(p0, n), Uv(p1, n), Uv(p2, n), Uv(p3, n));
		}
		foreach (int sg in new[] { -1, 1 })
		{
			// the faces
			Q(L(0, sg, -1, -1), L(0, sg, 1, -1), L(0, sg, 1, 1), L(0, sg, -1, 1), new Vector3(sg, 0, 0));
			Q(L(1, -1, sg, -1), L(1, 1, sg, -1), L(1, 1, sg, 1), L(1, -1, sg, 1), new Vector3(0, sg, 0));
			Q(L(2, -1, -1, sg), L(2, 1, -1, sg), L(2, 1, 1, sg), L(2, -1, 1, sg), new Vector3(0, 0, sg));
		}
		foreach (int s1 in new[] { -1, 1 })
			foreach (int s2 in new[] { -1, 1 })
			{
				// the chamfers: X-Y edges run along Z, X-Z along Y, Y-Z along X
				Q(L(0, s1, s2, -1), L(0, s1, s2, 1), L(1, s1, s2, 1), L(1, s1, s2, -1), new Vector3(s1, s2, 0).Normalized());
				Q(L(0, s1, -1, s2), L(0, s1, 1, s2), L(2, s1, 1, s2), L(2, s1, -1, s2), new Vector3(s1, 0, s2).Normalized());
				Q(L(1, -1, s1, s2), L(1, 1, s1, s2), L(2, 1, s1, s2), L(2, -1, s1, s2), new Vector3(0, s1, s2).Normalized());
			}
		foreach (int sx in new[] { -1, 1 })
			foreach (int sy in new[] { -1, 1 })
				foreach (int sz in new[] { -1, 1 })
				{
					Vector3 n = new Vector3(sx, sy, sz).Normalized();
					Vector3 p0 = L(0, sx, sy, sz), p1 = L(1, sx, sy, sz), p2 = L(2, sx, sy, sz);
					Tri(c + b * p0, c + b * p1, c + b * p2, (b * n).Normalized(), Uv(p0, n), Uv(p1, n), Uv(p2, n));
				}
	}

	/// <summary>(2026-10-14g) When above zero, <see cref="BuildKit.Box"/> eases its edges by this much (2-4 cm reads at
	/// PS2 resolution: the lantern catches a line of light along the edge instead of a knife cut). Which edges:
	/// <see cref="BevelAxis"/>.</summary>
	public float Bevel;
	/// <summary>Which of <see cref="BuildKit.Box"/>'s edges <see cref="Bevel"/> eases: -1 all twelve (a prop, a table
	/// top); 0, 1 or 2 only the four running along local X, Y or Z (1: a wall's or a post's upright corners, leaving
	/// the joints with the floor and the ceiling tight).</summary>
	public int BevelAxis = -1;

	/// <summary>(2026-10-14g) A box whose four edges along local <paramref name="axis"/> (0 X, 1 Y, 2 Z) are chamfered by
	/// <paramref name="bevel"/>, the other eight left square: a wall slab's or a jamb's outside corners, a post. Its two
	/// end caps only if asked (a wall standing on a floor needs no bottom). Sides go through <see cref="Quad"/>, so
	/// <see cref="Tess"/> subdivides them. UVs world-scaled per face like <see cref="BevelBox"/>.</summary>
	public void ChamferBox(Vector3 c, Vector3 s, float bevel, float uvScale = 1f, Basis? rot = null, int axis = 1, bool capLow = true, bool capHigh = true)
	{
		Basis b = rot ?? Basis.Identity;
		// a local frame with the chamfered edges running along 'a'
		Vector3 ea = axis == 0 ? Vector3.Right : axis == 1 ? Vector3.Up : Vector3.Back;
		Vector3 eu = axis == 0 ? Vector3.Back : axis == 1 ? Vector3.Right : Vector3.Up;
		Vector3 ev = ea.Cross(eu);
		float ha = s.Dot(ea) * 0.5f, hu = Mathf.Abs(s.Dot(eu)) * 0.5f, hv = Mathf.Abs(s.Dot(ev)) * 0.5f;
		ha = Mathf.Abs(ha);
		float e = Mathf.Min(bevel, Mathf.Min(hu, hv) * 0.9f);
		var ring = e <= 0.0005f
			? new[] { new Vector2(hu, hv), new Vector2(hu, -hv), new Vector2(-hu, -hv), new Vector2(-hu, hv) }
			: new[] { new Vector2(hu - e, hv), new Vector2(hu, hv - e), new Vector2(hu, -hv + e), new Vector2(hu - e, -hv), new Vector2(-hu + e, -hv), new Vector2(-hu, -hv + e), new Vector2(-hu, hv - e), new Vector2(-hu + e, hv) };
		Vector3 L(Vector2 q, float h) => eu * q.X + ev * q.Y + ea * h;
		Vector2 Uv(Vector3 p, Vector3 n)
		{
			Vector3 an = n.Abs();
			Vector2 q = an.X >= an.Y && an.X >= an.Z ? new Vector2(p.Z, -p.Y) : an.Y >= an.Z ? new Vector2(p.X, p.Z) : new Vector2(p.X, -p.Y);
			return q * uvScale;
		}
		Vector3 W(Vector3 p) => c + b * p;
		for (int i = 0; i < ring.Length; i++)
		{
			Vector2 q0 = ring[i], q1 = ring[(i + 1) % ring.Length];
			Vector2 en = new Vector2(q1.Y - q0.Y, -(q1.X - q0.X)).Normalized();
			if (en.Dot((q0 + q1) * 0.5f) < 0) en = -en;
			Vector3 n = (eu * en.X + ev * en.Y).Normalized();
			Vector3 p0 = L(q0, -ha), p1 = L(q1, -ha), p2 = L(q1, ha), p3 = L(q0, ha);
			Quad(W(p0), W(p1), W(p2), W(p3), (b * n).Normalized(), Uv(p0, n), Uv(p1, n), Uv(p2, n), Uv(p3, n));
		}
		foreach (var (h, cap) in new[] { (-ha, capLow), (ha, capHigh) })
		{
			if (!cap) continue;
			Vector3 n = ea * Mathf.Sign(h);
			Vector3 m = L(Vector2.Zero, h);
			for (int i = 0; i < ring.Length; i++)
			{
				Vector3 p0 = L(ring[i], h), p1 = L(ring[(i + 1) % ring.Length], h);
				Tri(W(m), W(p0), W(p1), (b * n).Normalized(), Uv(m, n), Uv(p0, n), Uv(p1, n));
			}
		}
	}

	/// <summary>(2026-10-14g) Visits every vertex drawn so far (in the kit's space, after <see cref="Xf"/>) of the surfaces
	/// <paramref name="which"/> accepts (all if null), to rewrite its colour and its second UV: how <see cref="RoomShade"/>
	/// bakes a room's dark into its walls. Surfaces taken from a modelled mesh (<see cref="AddMesh"/>: their second UV is
	/// their baked cavity map) are left alone.</summary>
	public delegate void VertexShader(Vector3 p, Vector3 n, ref Color c, ref Vector2 uv2);

	public void Shade(VertexShader fn, System.Func<Material, bool> which = null)
	{
		foreach (var m in _order)
		{
			if (which != null && !which(m)) continue;
			var s = _surfs[m];
			if (s.T.Count > 0 || (s.UV2.Count > 0 && !s.Shaded)) continue;
			// (glass and other see-through surfaces keep their alpha)
			if (which == null && m is BaseMaterial3D bm && bm.Transparency != BaseMaterial3D.TransparencyEnum.Disabled) continue;
			if (!s.Shaded) { s.UV2.Clear(); for (int i = 0; i < s.V.Count; i++) s.UV2.Add(Vector2.Zero); s.Shaded = true; }
			for (int i = 0; i < s.V.Count; i++)
			{
				Color c = s.C[i];
				Vector2 uv2 = s.UV2[i];
				fn(s.V[i], s.N[i], ref c, ref uv2);
				s.C[i] = c;
				s.UV2[i] = uv2;
			}
		}
	}

	/// <summary>Every material drawn with so far (to pick which a <see cref="Shade"/> or a weathering swap applies to).</summary>
	public IReadOnlyList<Material> Materials => _order;

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
		if (len < 1e-6f) return;
		Vector3 z = axis / len;
		sides = SidesFor(sides, Mathf.Max(r0, r1));
		// carrying on from the last tube at an angle: a ball fills the joint (no crossed cut ends, no gap on the outside)
		if (Smooth && sides >= 5 && _hasLast && ReferenceEquals(_lastMat, CurrentMaterial) && _lastXf == Xf
			&& p0.DistanceTo(_lastEnd) < Mathf.Max(0.15f * r0, 0.002f) && _lastDir.Dot(z) < 0.985f)
			JointBall(p0, Mathf.Min(r0, _lastR) * 1.0f, sides);
		_hasLast = true; _lastEnd = p1; _lastDir = z; _lastR = r1; _lastMat = CurrentMaterial; _lastXf = Xf;
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

	/// <summary>Jittered low-poly blob (icosphere, 1 subdivision unless asked for more) with flat-ish shading.</summary>
	public void Blob(Vector3 c, Vector3 radii, int seed, float jitter = 0.18f, bool flat = true, float uvScale = 1f, float shadeBottom = 0f, int subdiv = 1)
	{
		var (verts, tris) = Icosphere(subdiv);
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
	/// <param name="tints">(2026-10-15, the interactive props in Blender) per source material: its vertex colour is then the model's own
	/// "Col" shade (a bezel's darker lip) times this tint (a CRT's body colour), for the game's vertex-coloured materials.</param>
	public void AddMesh(Mesh mesh, Transform3D xf, Dictionary<Material, Material> materials = null, Dictionary<Material, Color> tints = null)
	{
		if (mesh == null) return;
		var full = Xf * xf;
		var nb = full.Basis.Inverse().Transposed();
		bool mirrored = full.Basis.Determinant() < 0f;
		for (int si = 0; si < mesh.GetSurfaceCount(); si++)
		{
			var arr = mesh.SurfaceGetArrays(si);
			var mat = mesh.SurfaceGetMaterial(si);
			Color? tint = null;
			if (tints != null && mat != null && tints.TryGetValue(mat, out var tc)) tint = tc;
			if (materials != null && mat != null && materials.TryGetValue(mat, out var swap)) mat = swap;
			if (mat == null) continue;
			var col = tint != null && arr[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil ? (Color[])arr[(int)Mesh.ArrayType.Color] : null;
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
				_cur.C.Add(tint is { } tt ? (col != null ? col[i] * tt : tt) : Colors.White);
				if (uv2 != null) _cur.UV2.Add(uv2[i]);
				if (t != null)
				{
					var tv = (full.Basis * new Vector3(t[i * 4], t[i * 4 + 1], t[i * 4 + 2])).Normalized();
					_cur.T.Add(tv.X); _cur.T.Add(tv.Y); _cur.T.Add(tv.Z); _cur.T.Add(t[i * 4 + 3] * (full.Basis.Determinant() < 0 ? -1f : 1f));
				}
			}
			int count = idx?.Length ?? v.Length;
			for (int i = 0; i + 2 < count; i += 3)
			{
				// (a mirrored placement turns the triangles' winding inside out: put it back)
				int i0 = idx != null ? idx[i] : i, i1 = idx != null ? idx[i + 1] : i + 1, i2 = idx != null ? idx[i + 2] : i + 2;
				if (mirrored) (i1, i2) = (i2, i1);
				_cur.I.Add(b + i0); _cur.I.Add(b + i1); _cur.I.Add(b + i2);
			}
		}
	}

	/// <summary>For the load's log: time spent separating coplanar faces, and committing meshes, so far.</summary>
	public static double CoplanarMs, CommitMs;
	public static int Commits;

	public ArrayMesh Commit()
	{
		ulong t0 = Time.GetTicksUsec();
		SeparateCoplanar();
		ulong t1 = Time.GetTicksUsec();
		CoplanarMs += (t1 - t0) / 1000.0;
		var mesh = CommitArrays();
		CommitMs += (Time.GetTicksUsec() - t1) / 1000.0;
		Commits++;
		Systems.LoadPump.Tick();   // (a long build keeps the window answering)
		return mesh;
	}

	private ArrayMesh CommitArrays()
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
