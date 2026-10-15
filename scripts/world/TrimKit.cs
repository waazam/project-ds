using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The joinery that turns a box into a built room (2026-10-14g, the interiors kit; the look survey: "clean boxes, one
/// tiled material per plane, no trim"): a 2D moulding profile extruded along a path, with mitred corners and capped
/// ends. Skirting boards, chair rails, picture rails and cornices round a room's walls; casings round a door or a
/// window (any outline: square-headed or a pointed arch); linings and sills through a wall's thickness; beadboard or
/// panelled wainscot; chamfered ceiling beams. Everything goes into the caller's <see cref="MeshKit"/> in its current
/// material and colour, in the kit's space.
/// <para>A profile's points are (d, h): d out from the wall's face, h along its "side": up the wall for a skirting or a
/// rail, down from the ceiling for a cornice, away from the opening for a casing. An open profile runs from one point on
/// or off the wall round to the wall again; its ends are closed against the wall when capped.</para>
/// <para>Typical use, a room with one door: <c>foreach (var (run, closed) in TrimKit.Perimeter(corners, doors))
/// TrimKit.Loop(k, run, TrimKit.Skirting, closed);</c> and <c>TrimKit.Casing(k, TrimKit.DoorOutline(...), faceN,
/// TrimKit.CasingProfile)</c>. See docs/design/interior-kit.md.</para>
/// </summary>
public static class TrimKit
{
	/// <summary>A moulding's cross-section: points (d out of the wall, h along the side), in metres.</summary>
	public sealed class Profile
	{
		public readonly Vector2[] Pts;
		/// <summary>A closed outline (a beam, a lining board) rather than a moulding laid against a wall.</summary>
		public readonly bool Closed;
		/// <summary>Bends of under 50 degrees shaded smooth (a cove, a roll), the rest kept crisp.</summary>
		public readonly bool Smooth;

		public Profile(bool closed, bool smooth, params Vector2[] pts) { Closed = closed; Smooth = smooth; Pts = pts; }
		public Profile(params Vector2[] pts) : this(false, false, pts) { }

		/// <summary>Its height along the side (a skirting's height, a casing's width).</summary>
		public float Size { get { float m = 0; foreach (var p in Pts) m = Mathf.Max(m, p.Y); return m; } }
		/// <summary>How far it stands out of the wall.</summary>
		public float Depth { get { float m = 0; foreach (var p in Pts) m = Mathf.Max(m, p.X); return m; } }

		/// <summary>The same section scaled (<paramref name="d"/> out, <paramref name="h"/> along).</summary>
		public Profile Scaled(float d, float h)
		{
			var q = new Vector2[Pts.Length];
			for (int i = 0; i < q.Length; i++) q[i] = new Vector2(Pts[i].X * d, Pts[i].Y * h);
			return new Profile(Closed, Smooth, q);
		}

		/// <summary>The same section moved out from the wall by <paramref name="d"/> (a skirting in front of wainscot), its
		/// back filled to the wall.</summary>
		public Profile Out(float d)
		{
			if (d <= 0f) return this;
			var q = new List<Vector2>();
			if (!Closed && Pts[0].X == 0f) q.Add(Pts[0]);
			foreach (var p in Pts) q.Add(new Vector2(p.X + d, p.Y));
			if (!Closed && Pts[^1].X == 0f) q.Add(Pts[^1]);
			return new Profile(Closed, Smooth, q.ToArray());
		}
	}

	static Vector2 V(float d, float h) => new(d, h);

	/// <summary>A plain skirting board: 13 cm tall, 1.8 cm proud, a chamfered and rounded top edge.</summary>
	public static readonly Profile Skirting = new(V(0.018f, 0f), V(0.018f, 0.1f), V(0.015f, 0.112f), V(0.009f, 0.122f), V(0.003f, 0.128f), V(0f, 0.13f));
	/// <summary>A tall Victorian skirting: 20 cm, a bead and a step along its top.</summary>
	public static readonly Profile SkirtingTall = new(V(0.022f, 0f), V(0.022f, 0.15f), V(0.013f, 0.156f), V(0.013f, 0.168f), V(0.019f, 0.174f), V(0.019f, 0.186f), V(0.011f, 0.197f), V(0f, 0.2f));
	/// <summary>A door or window casing: 9 cm wide, 2.4 cm proud, stepped and eased toward its outer edge (h = 0 at the opening).</summary>
	public static readonly Profile CasingProfile = new(V(0f, 0f), V(0.02f, 0f), V(0.024f, 0.006f), V(0.024f, 0.062f), V(0.019f, 0.075f), V(0.012f, 0.085f), V(0.004f, 0.09f), V(0f, 0.09f));
	/// <summary>A chair (dado) rail: 6 cm, a rounded nosing (laid at about 0.9 m, or capping a wainscot).</summary>
	public static readonly Profile ChairRail = new(false, true, V(0f, 0f), V(0.01f, 0.004f), V(0.019f, 0.014f), V(0.023f, 0.03f), V(0.019f, 0.046f), V(0.01f, 0.056f), V(0f, 0.06f));
	/// <summary>A picture rail: 3.5 cm, a lip to hang wires from (about 2.1 m up, or a hand under the cornice).</summary>
	public static readonly Profile PictureRail = new(V(0f, 0f), V(0.01f, 0.002f), V(0.019f, 0.012f), V(0.019f, 0.026f), V(0.007f, 0.035f), V(0f, 0.035f));
	/// <summary>A cornice (crown), laid with its side pointing down from the ceiling: a cove 14 cm down the wall and 8 cm
	/// out along the ceiling.</summary>
	public static readonly Profile Cornice = new(false, true, V(0f, 0.14f), V(0.008f, 0.132f), V(0.014f, 0.118f), V(0.022f, 0.098f), V(0.036f, 0.074f), V(0.054f, 0.05f), V(0.068f, 0.03f), V(0.078f, 0.012f), V(0.08f, 0f));
	/// <summary>A thin board (1.8 cm by 1, scaled to fit): a lining, a shelf.</summary>
	public static Profile Board(float thick, float width) => new(true, false, V(0f, 0f), V(thick, 0f), V(thick, width), V(0f, width));

	// ------------------------------------------------------------------ the extrusion

	/// <summary>
	/// The core: <paramref name="p"/> swept along <paramref name="path"/>. Segment i runs path[i] to path[i+1] with its
	/// own frame: <paramref name="outs"/>[i] (the profile's d) and <paramref name="sides"/>[i] (its h), both square to the
	/// segment. Corners are mitred so each profile line meets its neighbour exactly; a closed path joins its last point
	/// to its first. Open ends are capped unless told not to.
	/// </summary>
	public static void Sweep(MeshKit k, IList<Vector3> path, IList<Vector3> outs, IList<Vector3> sides, Profile p, bool closed = false, float uv = 1f, bool capStart = true, bool capEnd = true)
	{
		int nSeg = closed ? path.Count : path.Count - 1;
		if (nSeg < 1 || p.Pts.Length < 2) return;
		var pts = p.Pts;
		int np = pts.Length;
		// the cap polygon (open profiles closed back to the wall) and its winding, for the faces' outward normals
		var cap = CapPolygon(p);
		float area = 0f;
		for (int i = 0; i < cap.Length; i++) area += cap[i].X * cap[(i + 1) % cap.Length].Y - cap[(i + 1) % cap.Length].X * cap[i].Y;
		float wind = area >= 0f ? 1f : -1f;
		int nEdge = p.Closed ? np : np - 1;
		var en = new Vector2[nEdge];
		for (int e = 0; e < nEdge; e++)
		{
			Vector2 d = pts[(e + 1) % np] - pts[e];
			en[e] = (new Vector2(d.Y, -d.X) * wind).Normalized();
		}
		// per profile point, its smooth normal (the edges either side, when the bend is gentle)
		var pn = new Vector2[np];
		for (int i = 0; i < np; i++)
		{
			int ePrev = i - 1 < 0 ? (p.Closed ? nEdge - 1 : -1) : i - 1;
			int eNext = i < nEdge ? i : -1;
			Vector2 a = ePrev >= 0 ? en[ePrev] : en[eNext], b = eNext >= 0 ? en[eNext] : en[ePrev];
			pn[i] = (a + b).Normalized();
		}
		Vector3 Seg(int s) => (path[(s + 1) % path.Count] - path[s]).Normalized();
		// a profile point's place at path point j: on segment s's frame, mitred against the neighbouring segment's
		Vector3 At(int j, int i, int s)
		{
			Vector3 P = path[j % path.Count];
			Vector3 v0 = outs[s] * pts[i].X + sides[s] * pts[i].Y;
			int other = j == s ? s - 1 : s + 1;
			if (!closed && (other < 0 || other >= nSeg)) return P + v0;
			other = (other + nSeg) % nSeg;
			Vector3 t0 = Seg(s), t1 = Seg(other);
			Vector3 v1 = outs[other] * pts[i].X + sides[other] * pts[i].Y;
			float c = t0.Dot(t1);
			if (1f - c * c < 1e-4f) return P + v0;
			Vector3 w = v1 - v0;
			float beta = (c * w.Dot(t0) - w.Dot(t1)) / (1f - c * c);
			float alpha = w.Dot(t0) + beta * c;
			return P + v0 + t0 * alpha;
		}
		float uAt = 0f;
		for (int s = 0; s < nSeg; s++)
		{
			int j0 = s, j1 = s + 1;
			float len = path[j0 % path.Count].DistanceTo(path[j1 % path.Count]);
			float u0 = uAt * uv, u1 = (uAt + len) * uv;
			uAt += len;
			float v = 0f;
			for (int e = 0; e < nEdge; e++)
			{
				int i0 = e, i1 = (e + 1) % np;
				float el = pts[i0].DistanceTo(pts[i1]);
				Vector3 a = At(j0, i0, s), b = At(j0, i1, s), cc = At(j1, i1, s), d = At(j1, i0, s);
				Vector3 fn = (outs[s] * en[e].X + sides[s] * en[e].Y).Normalized();
				Vector3 na = fn, nb = fn;
				if (p.Smooth)
				{
					if (pn[i0].Dot(en[e]) > 0.9f) na = (outs[s] * pn[i0].X + sides[s] * pn[i0].Y).Normalized();
					if (pn[i1].Dot(en[e]) > 0.9f) nb = (outs[s] * pn[i1].X + sides[s] * pn[i1].Y).Normalized();
				}
				Vector2 ua = new(u0, v * uv), ub = new(u0, (v + el) * uv), uc = new(u1, (v + el) * uv), ud = new(u1, v * uv);
				k.Tri(a, b, cc, na, nb, nb, ua, ub, uc);
				k.Tri(a, cc, d, na, nb, na, ua, uc, ud);
				v += el;
			}
		}
		if (closed) return;
		if (capStart) Cap(k, cap, path[0], outs[0], sides[0], -Seg(0), uv);
		if (capEnd) Cap(k, cap, path[path.Count - 1], outs[nSeg - 1], sides[nSeg - 1], Seg(nSeg - 1), uv);
	}

	private static Vector2[] CapPolygon(Profile p)
	{
		if (p.Closed) return p.Pts;
		var c = new List<Vector2>(p.Pts);
		if (p.Pts[^1].X > 1e-5f) c.Add(new Vector2(0f, p.Pts[^1].Y));
		if (p.Pts[0].X > 1e-5f) c.Add(new Vector2(0f, p.Pts[0].Y));
		return c.ToArray();
	}

	private static void Cap(MeshKit k, Vector2[] poly, Vector3 at, Vector3 o, Vector3 s, Vector3 n, float uv)
	{
		var idx = Geometry2D.TriangulatePolygon(poly);
		for (int i = 0; i + 2 < idx.Length; i += 3)
		{
			Vector2 a = poly[idx[i]], b = poly[idx[i + 1]], c = poly[idx[i + 2]];
			k.Tri(at + o * a.X + s * a.Y, at + o * b.X + s * b.Y, at + o * c.X + s * c.Y, n, a * uv, b * uv, c * uv);
		}
	}

	// ------------------------------------------------------------------ runs along walls

	/// <summary>One straight run from <paramref name="a"/> to <paramref name="b"/> along a wall's face (the points on the
	/// face, at the moulding's foot), standing out toward <paramref name="inward"/>, its side up (or
	/// <paramref name="side"/>: Vector3.Down for a cornice).</summary>
	public static void Along(MeshKit k, Vector3 a, Vector3 b, Vector3 inward, Profile p, Vector3? side = null, float uv = 1f, bool caps = true)
	{
		Vector3 t = (b - a).Normalized();
		Vector3 o = (inward - t * inward.Dot(t)).Normalized();
		Vector3 s = side ?? Vector3.Up;
		s = (s - t * s.Dot(t) - o * s.Dot(o)).Normalized();
		Sweep(k, new[] { a, b }, new[] { o }, new[] { s }, p, false, uv, caps, caps);
	}

	/// <summary>A run round a room's walls: <paramref name="pts"/> its corners on the walls' faces at the moulding's
	/// height, in order round the room (either way), open (between two doorways: <see cref="Perimeter"/>) or
	/// <paramref name="closed"/>. Each stretch stands out into the room (the side of the line the room's middle,
	/// <paramref name="inside"/>, is on: the corners' average unless given); <paramref name="side"/> up for skirtings and
	/// rails, down for a cornice.</summary>
	public static void Loop(MeshKit k, IList<Vector3> pts, Profile p, bool closed = false, Vector3? side = null, Vector3? inside = null, float uv = 1f)
	{
		if (pts.Count < 2) return;
		Vector3 sd = side ?? Vector3.Up;
		Vector3 mid = inside ?? Centre(pts);
		int nSeg = closed ? pts.Count : pts.Count - 1;
		var outs = new Vector3[nSeg];
		var sides = new Vector3[nSeg];
		for (int i = 0; i < nSeg; i++)
		{
			Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
			Vector3 t = (b - a).Normalized();
			Vector3 o = sd.Cross(t).Normalized();
			// into the room: toward the middle, judged square to this stretch
			if (o.Dot(mid - (a + b) * 0.5f) < 0f) o = -o;
			outs[i] = o;
			sides[i] = sd;
		}
		Sweep(k, pts, outs, sides, p, closed, uv);
	}

	private static Vector3 Centre(IList<Vector3> pts)
	{
		Vector3 c = Vector3.Zero;
		foreach (var q in pts) c += q;
		return c / pts.Count;
	}

	/// <summary>A room's outline (its walls' inner faces, at any height, in order round it) cut at its doorways: the
	/// stretches a skirting or a rail runs along. Each gap is (a point in the doorway, its width). With no gaps, the
	/// whole loop, closed.</summary>
	public static List<(List<Vector3> run, bool closed)> Perimeter(IList<Vector3> corners, IList<(Vector3 at, float width)> gaps = null)
	{
		var res = new List<(List<Vector3>, bool)>();
		int n = corners.Count;
		// where along the loop each gap falls (distance from corner 0)
		var segStart = new float[n + 1];
		for (int i = 0; i < n; i++) segStart[i + 1] = segStart[i] + corners[i].DistanceTo(corners[(i + 1) % n]);
		float total = segStart[n];
		var cuts = new List<(float a, float b)>();
		if (gaps != null)
			foreach (var (at, w) in gaps)
			{
				int best = 0; float bestD = float.MaxValue, bestU = 0f;
				for (int i = 0; i < n; i++)
				{
					Vector3 a = corners[i], b = corners[(i + 1) % n];
					Vector3 ab = b - a;
					float u = Mathf.Clamp((at - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
					Vector3 q = a + ab * u;
					float d = new Vector2(q.X - at.X, q.Z - at.Z).Length();
					if (d < bestD) { bestD = d; best = i; bestU = u; }
				}
				float c = segStart[best] + bestU * (segStart[best + 1] - segStart[best]);
				cuts.Add((c - w * 0.5f, c + w * 0.5f));
			}
		Vector3 Point(float s)
		{
			s = Mathf.PosMod(s, total);
			for (int i = 0; i < n; i++)
				if (s <= segStart[i + 1] + 1e-5f)
				{
					float f = (s - segStart[i]) / Mathf.Max(1e-5f, segStart[i + 1] - segStart[i]);
					return corners[i].Lerp(corners[(i + 1) % n], f);
				}
			return corners[0];
		}
		if (cuts.Count == 0) { res.Add((new List<Vector3>(corners), true)); return res; }
		cuts.Sort((x, y) => x.a.CompareTo(y.a));
		for (int g = 0; g < cuts.Count; g++)
		{
			float from = cuts[g].b, to = g + 1 < cuts.Count ? cuts[g + 1].a : cuts[0].a + total;
			if (to - from < 0.05f) continue;
			var run = new List<Vector3> { Point(from) };
			// the corners between
			for (int lap = 0; lap < 2; lap++)
				for (int i = 0; i < n; i++)
				{
					float s = segStart[i] + lap * total;
					if (s > from + 1e-4f && s < to - 1e-4f) run.Add(corners[i]);
				}
			run.Add(Point(to));
			res.Add((run, false));
		}
		return res;
	}

	// ------------------------------------------------------------------ openings

	/// <summary>A square-headed opening's outline on a wall's face, for <see cref="Casing"/> and <see cref="Lining"/>:
	/// up one side, across the head, down the other. <paramref name="bottomCentre"/> on the face at the sill (or the
	/// floor), <paramref name="along"/> along the wall.</summary>
	public static Vector3[] RectOutline(Vector3 bottomCentre, Vector3 along, float width, float height)
	{
		along = along.Normalized();
		Vector3 l = bottomCentre - along * (width * 0.5f), r = bottomCentre + along * (width * 0.5f), up = Vector3.Up * height;
		return new[] { l, l + up, r + up, r };
	}

	/// <summary>A pointed (or round, <paramref name="sharp"/> 0.5) arch's outline: straight sides to
	/// <paramref name="spring"/>, then the head in <paramref name="steps"/> facets a side.</summary>
	public static Vector3[] ArchOutline(Vector3 bottomCentre, Vector3 along, float width, float spring, float sharp = 0.7f, int steps = 6)
	{
		along = along.Normalized();
		float half = width * 0.5f, r = width * sharp;
		var pts = new List<Vector3> { bottomCentre - along * half };
		for (int i = 0; i <= steps * 2; i++)
		{
			float x = -half + width * i / (steps * 2f);
			pts.Add(bottomCentre + along * x + Vector3.Up * (spring + Church.Pointed(x, half, r)));
		}
		pts.Add(bottomCentre + along * half);
		return pts.ToArray();
	}

	/// <summary>A casing (architrave) round an opening on a wall's face: <paramref name="outline"/> its edge (open at
	/// the bottom: up, over, down), <paramref name="faceN"/> out of the face; the profile's side points away from the
	/// opening. Its feet stand on the floor (or the sill).</summary>
	public static void Casing(MeshKit k, IList<Vector3> outline, Vector3 faceN, Profile p = null, float uv = 1f)
	{
		p ??= CasingProfile;
		faceN = faceN.Normalized();
		Vector3 mid = Centre(outline);
		int nSeg = outline.Count - 1;
		var outs = new Vector3[nSeg];
		var sides = new Vector3[nSeg];
		for (int i = 0; i < nSeg; i++)
		{
			Vector3 t = (outline[i + 1] - outline[i]).Normalized();
			Vector3 s = faceN.Cross(t).Normalized();
			if (s.Dot((outline[i] + outline[i + 1]) * 0.5f - mid) < 0f) s = -s;
			outs[i] = faceN;
			sides[i] = s;
		}
		Sweep(k, outline, outs, sides, p, false, uv);
	}

	/// <summary>The lining through a wall's thickness round an opening: boards <paramref name="thick"/> thick covering
	/// the reveal from the face (along <paramref name="outline"/>) back <paramref name="depth"/> into the wall, so the
	/// wall shows its thickness as joinery, not as a cut. Pair with <see cref="Casing"/> on each face.</summary>
	public static void Lining(MeshKit k, IList<Vector3> outline, Vector3 faceN, float depth, float thick = 0.022f, float uv = 1f)
	{
		faceN = faceN.Normalized();
		Vector3 mid = Centre(outline);
		int nSeg = outline.Count - 1;
		var outs = new Vector3[nSeg];
		var sides = new Vector3[nSeg];
		for (int i = 0; i < nSeg; i++)
		{
			Vector3 t = (outline[i + 1] - outline[i]).Normalized();
			Vector3 s = faceN.Cross(t).Normalized();
			// the board's thickness into the opening (toward its middle)
			if (s.Dot(mid - (outline[i] + outline[i + 1]) * 0.5f) < 0f) s = -s;
			outs[i] = -faceN;
			sides[i] = s;
		}
		// (2 mm proud of the face, so the casing's back butts onto it instead of sharing the face's plane)
		Sweep(k, outline, outs, sides, new Profile(true, false, V(-0.002f, 0f), V(depth, 0f), V(depth, thick), V(-0.002f, thick)), false, uv);
	}

	/// <summary>A window's sill board and the apron under it: the board across the reveal's foot from the glass
	/// (<paramref name="depth"/> into the wall) to <paramref name="nosing"/> proud of the face, its front edge eased; the
	/// apron a short run of casing below. <paramref name="a"/>, <paramref name="b"/> the opening's bottom corners on the
	/// face.</summary>
	public static void Sill(MeshKit k, Vector3 a, Vector3 b, Vector3 faceN, float depth, float nosing = 0.045f, float thick = 0.035f, float horns = 0.06f, bool apron = true)
	{
		faceN = faceN.Normalized();
		Vector3 t = (b - a).Normalized();
		float w = a.DistanceTo(b) + horns * 2f;
		Vector3 c = (a + b) * 0.5f + faceN * ((nosing - depth) * 0.5f) + Vector3.Down * (thick * 0.5f - 0.004f);
		var basis = new Basis(t, Vector3.Up, faceN);
		k.BevelBox(c, new Vector3(w, thick, nosing + depth), 0.008f, 1f, basis);
		if (apron)
		{
			float y = thick - 0.004f;
			Along(k, a - t * horns * 0.7f + Vector3.Down * y, b + t * horns * 0.7f + Vector3.Down * y, faceN, CasingProfile.Scaled(0.8f, 0.8f), Vector3.Down);
		}
	}

	// ------------------------------------------------------------------ wainscot and beams

	/// <summary>Wainscot along a run of wall (<paramref name="run"/>: points on the walls' faces at floor level, as for
	/// <see cref="Loop"/>), <paramref name="height"/> tall: <paramref name="beadboard"/> narrow tongue-and-groove boards
	/// with a V at every joint (the lantern finds each groove), or else stiles and rails round fielded panels. Capped by
	/// a chair rail; returns how far its face stands out, for the skirting in front of it
	/// (<c>TrimKit.Skirting.Out(thatMuch)</c>).</summary>
	public static float Wainscot(MeshKit k, IList<Vector3> run, float height = 0.95f, bool beadboard = true, bool closed = false, Vector3? inside = null, float board = 0.095f)
	{
		const float face = 0.014f, groove = 0.007f, gw = 0.008f;
		Vector3 mid = inside ?? Centre(run);
		int nSeg = closed ? run.Count : run.Count - 1;
		for (int sIdx = 0; sIdx < nSeg; sIdx++)
		{
			Vector3 a = run[sIdx], b = run[(sIdx + 1) % run.Count];
			float len = a.DistanceTo(b);
			if (len < 0.05f) continue;
			Vector3 t = (b - a) / len;
			Vector3 o = Vector3.Up.Cross(t).Normalized();
			if (o.Dot(mid - (a + b) * 0.5f) < 0f) o = -o;
			Vector3 up = Vector3.Up * height;
			if (beadboard)
			{
				int n = Mathf.Max(1, Mathf.RoundToInt(len / board));
				float bw = len / n;
				for (int i = 0; i < n; i++)
				{
					float u0 = i * bw, u1 = u0 + bw;
					// across a board: down into the groove's V at each edge, flat between
					var xs = new[] { (u0, face - groove), (u0 + gw, face), (u1 - gw, face), (u1, face - groove) };
					for (int j = 0; j < 3; j++)
					{
						var (ua, da) = xs[j];
						var (ub, db) = xs[j + 1];
						Vector3 p0 = a + t * ua + o * da, p1 = a + t * ub + o * db;
						Vector3 nn = (o * (ub - ua) - t * (db - da)).Normalized();
						k.Quad(p0, p1, p1 + up, p0 + up, nn, new Vector2(ua, height), new Vector2(ub, height), new Vector2(ub, 0), new Vector2(ua, 0));
					}
				}
				// its ends (an outside corner, a doorway)
				foreach (var (at, dir) in new[] { (a, -t), (b, t) })
					k.Quad(at, at + o * face, at + o * face + up, at + up, dir);
			}
			else
			{
				// stiles and rails proud of a recessed ground, fielded panels between
				var basis = new Basis(t, Vector3.Up, o);
				Vector3 P(float u, float y, float d) => a + t * u + Vector3.Up * y + o * d;
				BuildKit.Box(k, P(len * 0.5f, height * 0.5f, face * 0.25f), new Vector3(len, height, face * 0.5f), 1f, BuildKit.Face.NZ | BuildKit.Face.NY, basis);
				const float stile = 0.09f, rail = 0.11f;
				int n = Mathf.Max(1, Mathf.RoundToInt((len - stile) / 0.62f));
				float pw = (len - stile * (n + 1)) / n;
				float ph = height - rail * 2f;
				BuildKit.Box(k, P(len * 0.5f, rail * 0.5f, face * 0.5f), new Vector3(len, rail, face), 1f, BuildKit.Face.NZ | BuildKit.Face.NY, basis);
				BuildKit.Box(k, P(len * 0.5f, height - rail * 0.5f, face * 0.5f), new Vector3(len, rail, face), 1f, BuildKit.Face.NZ, basis);
				for (int i = 0; i <= n; i++)
					k.BevelBox(P(stile * 0.5f + i * (pw + stile), height * 0.5f, face * 0.5f), new Vector3(stile, ph + 0.004f, face), 0.004f, 1f, basis);
				for (int i = 0; i < n; i++)
				{
					float cu = stile + pw * 0.5f + i * (pw + stile);
					k.BevelBox(P(cu, height * 0.5f, face * 0.45f), new Vector3(pw - 0.05f, ph - 0.05f, face * 0.9f), 0.016f, 1f, basis);
				}
			}
		}
		// the cap: a chair rail along its top
		var top = new List<Vector3>();
		foreach (var q in run) top.Add(q + Vector3.Up * (height - 0.03f));
		Loop(k, top, ChairRail.Out(face), closed, null, mid + Vector3.Up * (height - 0.03f));
		return face;
	}

	/// <summary>A ceiling beam (or any timber) from <paramref name="a"/> to <paramref name="b"/>, <paramref name="w"/>
	/// wide by <paramref name="h"/> deep, its four long edges chamfered (<paramref name="bevel"/>), its depth along
	/// <paramref name="up"/>. Ends capped.</summary>
	public static void Beam(MeshKit k, Vector3 a, Vector3 b, float w, float h, float bevel = 0.025f, Vector3? up = null)
	{
		Vector3 dir = b - a;
		float len = dir.Length();
		if (len < 1e-3f) return;
		Vector3 z = dir / len;
		Vector3 u = up ?? Vector3.Up;
		if (Mathf.Abs(z.Dot(u)) > 0.98f) u = Vector3.Right;
		Vector3 x = u.Cross(z).Normalized();
		Vector3 y = z.Cross(x).Normalized();
		k.ChamferBox((a + b) * 0.5f, new Vector3(w, h, len), bevel, 1f, new Basis(x, y, z), 2);
	}
}
