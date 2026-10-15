using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.LodgeParts;

/// <summary>
/// (2026-10-15, the props pass) The lodge's own furniture and fittings, drawn to real measures and turned or rounded
/// where the real thing is (the owner: "make sure they're proportioned and not too blocky looking ... the green lamp").
/// Geometry helpers first (<see cref="Loft"/>, <see cref="Lathe"/>, <see cref="RoundRect"/>, <see cref="RoundSlab"/>),
/// then the pieces: lamps (a table lamp on a glazed crock, a banker's lamp), a ceiling dome, a sconce, case furniture
/// (a carcass round flush drawers, a wardrobe's case and its panelled door), a writing desk, the bathroom's tub, basin
/// and lavatory, a bar candle. Each piece is built in its own frame (origin on the floor, its front toward -Z) and put
/// into the room's mesh through <see cref="MeshKit.Xf"/>. PS2-sized: 8-16 sides to a turned piece, a bevel on every
/// edge that catches the lantern.
/// </summary>
public static class LodgeProps
{
	// ---------------------------------------------------------------- geometry

	/// <summary>A surface through closed rings of points (each the same count, wound by increasing angle, as
	/// <see cref="Ring"/> and <see cref="RoundRect"/> make them), in profile order: its normals face out of the profile's
	/// left as it goes (up a wall: outward; across a top toward the middle: up; down into a bowl: into the bowl).
	/// <paramref name="flip"/> turns them round. Smooth across a ring unless the profile bends there by more than
	/// <paramref name="hardDeg"/>. UVs in metres times <paramref name="uv"/>.</summary>
	public static void Loft(MeshKit k, IList<Vector3[]> rings, bool flip = false, float hardDeg = 50f, float uv = 1f)
	{
		int m = rings.Count, n = rings[0].Length;
		if (m < 2 || n < 3) return;
		var sn = new Vector3[m - 1, n];
		for (int s = 0; s < m - 1; s++)
			for (int i = 0; i < n; i++)
			{
				Vector3 a = rings[s][i], b = rings[s + 1][i];
				Vector3 around = rings[s][(i + 1) % n] - rings[s][(i - 1 + n) % n] + rings[s + 1][(i + 1) % n] - rings[s + 1][(i - 1 + n) % n];
				Vector3 nn = (b - a).Cross(around);
				if (nn.LengthSquared() < 1e-12f) nn = s > 0 ? sn[s - 1, i] : Vector3.Up;
				nn = nn.Normalized();
				sn[s, i] = flip ? -nn : nn;
			}
		float cosHard = Mathf.Cos(Mathf.DegToRad(hardDeg));
		Vector3 Nrm(int s, int ring, int i)
		{
			// the normal at ring 'ring' (s or s + 1) of segment s: shared with the neighbouring segment if the bend is soft
			int other = ring == s ? s - 1 : s + 1;
			if (other < 0 || other >= m - 1) return sn[s, i];
			var o = sn[other, i];
			return o.Dot(sn[s, i]) >= cosHard ? (o + sn[s, i]).Normalized() : sn[s, i];
		}
		// UVs: round each ring by its own length, along the profile by the distance up it
		var vAt = new float[m];
		for (int s = 1; s < m; s++)
		{
			float d = 0f;
			for (int i = 0; i < n; i++) d += rings[s][i].DistanceTo(rings[s - 1][i]);
			vAt[s] = vAt[s - 1] + d / n;
		}
		var uAt = new float[m, n + 1];
		for (int s = 0; s < m; s++)
			for (int i = 1; i <= n; i++) uAt[s, i] = uAt[s, i - 1] + rings[s][i - 1].DistanceTo(rings[s][i % n]);
		for (int s = 0; s < m - 1; s++)
			for (int i = 0; i < n; i++)
			{
				int j = (i + 1) % n;
				Vector3 a = rings[s][i], b = rings[s][j], c = rings[s + 1][j], d = rings[s + 1][i];
				Vector3 na = Nrm(s, s, i), nb = Nrm(s, s, j), nc = Nrm(s, s + 1, j), nd = Nrm(s, s + 1, i);
				Vector2 ua = new Vector2(uAt[s, i], vAt[s]) * uv, ub = new Vector2(uAt[s, i + 1], vAt[s]) * uv, uc = new Vector2(uAt[s + 1, i + 1], vAt[s + 1]) * uv, ud = new Vector2(uAt[s + 1, i], vAt[s + 1]) * uv;
				k.Tri(a, b, c, na, nb, nc, ua, ub, uc);
				k.Tri(a, c, d, na, nc, nd, ua, uc, ud);
			}
	}

	/// <summary>A flat fan closing a ring, facing <paramref name="normal"/>.</summary>
	public static void Cap(MeshKit k, Vector3[] ring, Vector3 normal, float uv = 1f)
	{
		var c = Vector3.Zero;
		foreach (var p in ring) c += p;
		c /= ring.Length;
		Vector3 ax = Mathf.Abs(normal.Y) > 0.7f ? Vector3.Right : Vector3.Up, ay = normal.Cross(ax).Normalized();
		ax = ay.Cross(normal).Normalized();
		Vector2 U(Vector3 p) => new Vector2((p - c).Dot(ax), (p - c).Dot(ay)) * uv;
		for (int i = 0; i < ring.Length; i++)
		{
			var a = ring[i];
			var b = ring[(i + 1) % ring.Length];
			k.Tri(c, a, b, normal, U(c), U(a), U(b));
		}
	}

	/// <summary>A flat band between two rings of any counts (both wound by increasing angle round <paramref name="c"/>,
	/// the inner inside the outer, both star-shaped from it), facing <paramref name="normal"/>: zipped by angle.</summary>
	public static void Zip(MeshKit k, Vector3[] outer, Vector3[] inner, Vector3 c, Vector3 normal, float uv = 1f)
	{
		float Ang(Vector3 p) => Mathf.PosMod(Mathf.Atan2(p.Z - c.Z, p.X - c.X), Mathf.Tau);
		float[] Unwrap(Vector3[] r, out int s)
		{
			s = 0;
			for (int i = 1; i < r.Length; i++) if (Ang(r[i]) < Ang(r[s])) s = i;
			var a = new float[r.Length + 1];
			for (int j = 0; j < r.Length; j++)
			{
				a[j] = Ang(r[(s + j) % r.Length]);
				if (j > 0 && a[j] < a[j - 1]) a[j] += Mathf.Tau;
			}
			a[r.Length] = a[0] + Mathf.Tau;
			return a;
		}
		var ao = Unwrap(outer, out int so);
		var ai = Unwrap(inner, out int si);
		int no = outer.Length, ni = inner.Length, io = 0, ii = 0;
		Vector3 O(int j) => outer[(so + j) % no];
		Vector3 I(int j) => inner[(si + j) % ni];
		Vector2 U(Vector3 p) => new Vector2(p.X, p.Z) * uv;
		while (io < no || ii < ni)
		{
			if (ii >= ni || (io < no && ao[io + 1] <= ai[ii + 1]))
			{
				k.Tri(O(io), O(io + 1), I(ii), normal, U(O(io)), U(O(io + 1)), U(I(ii)));
				io++;
			}
			else
			{
				k.Tri(I(ii), I(ii + 1), O(io), normal, U(I(ii)), U(I(ii + 1)), U(O(io)));
				ii++;
			}
		}
	}

	/// <summary>A horizontal ellipse of <paramref name="n"/> points at height <paramref name="y"/> (radii rx, rz).</summary>
	public static Vector3[] Ring(float rx, float rz, float y, int n, float x = 0f, float z = 0f)
	{
		var r = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n;
			r[i] = new Vector3(x + Mathf.Cos(a) * rx, y, z + Mathf.Sin(a) * rz);
		}
		return r;
	}

	/// <summary>A horizontal rounded rectangle (half sizes hx, hz, corner radius r) of 4 x (seg + 1) points, wound as
	/// <see cref="Ring"/> is.</summary>
	public static Vector3[] RoundRect(float hx, float hz, float r, float y, int seg = 3, float x = 0f, float z = 0f)
	{
		r = Mathf.Clamp(r, 0.0015f, Mathf.Min(hx, hz) - 0.0005f);
		var pts = new Vector3[4 * (seg + 1)];
		int p = 0;
		for (int q = 0; q < 4; q++)
		{
			float cx = (q == 0 || q == 3 ? 1f : -1f) * (hx - r), cz = (q < 2 ? 1f : -1f) * (hz - r);
			for (int i = 0; i <= seg; i++)
			{
				float a = Mathf.Pi * 0.5f * (q + i / (float)seg);
				pts[p++] = new Vector3(x + cx + Mathf.Cos(a) * r, y, z + cz + Mathf.Sin(a) * r);
			}
		}
		return pts;
	}

	/// <summary>A turned piece round local +Y at <paramref name="at"/>: the profile's (radius, height) points from the
	/// bottom up; <paramref name="sx"/>, <paramref name="sz"/> stretch it oval. Caps where asked.</summary>
	public static void Lathe(MeshKit k, Vector3 at, IList<Vector2> prof, int sides, bool capBottom = false, bool capTop = false, float sx = 1f, float sz = 1f, bool flip = false, float hardDeg = 50f)
	{
		var rings = new List<Vector3[]>();
		foreach (var p in prof) rings.Add(Ring(p.X * sx, p.X * sz, at.Y + p.Y, sides, at.X, at.Z));
		Loft(k, rings, flip, hardDeg);
		if (capBottom && prof[0].X > 0.0005f) Cap(k, rings[0], flip ? Vector3.Up : Vector3.Down);
		if (capTop && prof[^1].X > 0.0005f) Cap(k, rings[^1], flip ? Vector3.Down : Vector3.Up);
	}

	/// <summary>A slab with rounded corners in plan and its top edge eased (a table top, a counter, a seat): centre of
	/// its top at <paramref name="top"/>, half sizes hx, hz, thickness t, corner radius r, the ease e.</summary>
	public static void RoundSlab(MeshKit k, Vector3 top, float hx, float hz, float t, float r, float e, int seg = 3, bool bottom = true)
	{
		e = Mathf.Min(e, t * 0.6f);
		var rings = new List<Vector3[]>
		{
			RoundRect(hx - e * 0.4f, hz - e * 0.4f, r - e * 0.4f, top.Y - t, seg, top.X, top.Z),
			RoundRect(hx, hz, r, top.Y - t + e * 0.4f, seg, top.X, top.Z),
			RoundRect(hx, hz, r, top.Y - e, seg, top.X, top.Z),
			RoundRect(hx - e * 0.3f, hz - e * 0.3f, r - e * 0.3f, top.Y - e * 0.3f, seg, top.X, top.Z),
			RoundRect(hx - e, hz - e, r - e, top.Y, seg, top.X, top.Z),
		};
		Loft(k, rings, false, 50f);
		Cap(k, rings[^1], Vector3.Up);
		if (bottom) Cap(k, rings[0], Vector3.Down);
	}

	// ---------------------------------------------------------------- the Blender models

	/// <summary>(2026-10-15, the Blender pass) The pieces here are modelled in Blender (tools/Blender/lodge_props.py, to
	/// assets/models/furniture/lp_*.glb), each in the frame its builder draws in, so it goes in with the transform the
	/// builder pushed; its roles take the room's materials. The code under each stays as the fallback for a missing model.</summary>
	public static bool Model(MeshKit k, string model, Transform3D xf, params (string role, Material m)[] roles)
	{
		var d = new Dictionary<string, Material>();
		foreach (var (r, m) in roles) if (m != null) d[r] = m;
		return FurnitureKit.Add(k, model, xf, d);
	}

	private static bool Near(float a, float b) => Mathf.IsEqualApprox(a, b, 0.002f);

	private static Transform3D Push(MeshKit k, Transform3D xf)
	{
		var was = k.Xf;
		k.Xf = was * xf;
		return was;
	}

	// ---------------------------------------------------------------- lamps

	private static StandardMaterial3D _crock, _opal, _shadeLining;
	/// <summary>A 1970s glazed stoneware lamp base: a brown drip glaze, glossy.</summary>
	public static StandardMaterial3D CrockMat => _crock ??= new StandardMaterial3D { ResourceName = "lodge_m_crock", AlbedoColor = new Color(0.34f, 0.19f, 0.1f), Roughness = 0.22f, MetallicSpecular = 0.65f, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true };
	/// <summary>A banker's lamp's opal lining, lit.</summary>
	public static StandardMaterial3D OpalMat => _opal ??= LodgeTextures.Glow("lodge_lamp_opal", new Color(1f, 0.88f, 0.66f), 1.1f);

	/// <summary>A table lamp standing at <paramref name="c"/> (on the furniture's top), <paramref name="h"/> tall to the
	/// shade's top (a real one 55-70 cm): a glazed crock base like a ginger jar, a brass neck and socket, a drum shade
	/// in <paramref name="shade"/> (its glow, which a room's switch turns) about 0.6 of the height across, its trims, a
	/// finial. Returns the bulb (inside the shade).</summary>
	public static Vector3 TableLamp(MeshKit k, Vector3 c, float h, Material shade, Material body = null)
	{
		var col = k.Color;
		k.Color = Colors.White;
		float f = h / 0.62f;   // (drawn at 62 cm)
		if (Model(k, "lp_table_lamp", new Transform3D(Basis.FromScale(Vector3.One * f), c), ("brass", LodgeTextures.BrassMat), ("crock", body ?? CrockMat), ("shade", shade), ("bulb", shade)))
		{
			k.Color = col;
			return c + Vector3.Up * (0.45f * f);
		}
		Vector2 P(float r, float y) => new(r * f, y * f);
		// the base: a foot ring, the jar's belly, its shoulder, the neck
		k.Mat(LodgeTextures.BrassMat);
		Lathe(k, c, new[] { P(0.062f, 0f), P(0.064f, 0.008f), P(0.058f, 0.018f) }, 14, capBottom: true);
		k.Mat(body ?? CrockMat);
		Lathe(k, c, new[] { P(0.05f, 0.016f), P(0.068f, 0.045f), P(0.083f, 0.1f), P(0.085f, 0.14f), P(0.078f, 0.19f), P(0.058f, 0.235f), P(0.036f, 0.258f), P(0.028f, 0.27f) }, 14, hardDeg: 70f);
		k.Mat(LodgeTextures.BrassMat);
		Lathe(k, c, new[] { P(0.036f, 0.266f), P(0.04f, 0.272f), P(0.038f, 0.282f), P(0.014f, 0.29f), P(0.011f, 0.3f), P(0.011f, 0.355f), P(0.02f, 0.362f), P(0.02f, 0.405f), P(0.012f, 0.412f) }, 10, capTop: true, hardDeg: 60f);
		// the harp's two wires up inside the shade to the finial
		foreach (float s in new[] { -1f, 1f })
			k.Cylinder(c + new Vector3(s * 0.02f * f, 0.36f * f, 0), c + new Vector3(s * 0.006f * f, 0.6f * f, 0), 0.0025f * f, 0.0025f * f, 4, false);
		// the shade: a drum, a little narrower at the top, both faces (the inside shows from above), a trim at each rim
		k.Mat(shade);
		var sh = new[] { P(0.19f, 0.33f), P(0.187f, 0.36f), P(0.168f, 0.585f), P(0.165f, 0.6f) };
		Lathe(k, c, sh, 18);
		Lathe(k, c, sh, 18, flip: true);
		k.Mat(LodgeTextures.BrassMat);
		Lathe(k, c, new[] { P(0.191f, 0.328f), P(0.193f, 0.336f) }, 18);
		Lathe(k, c, new[] { P(0.166f, 0.597f), P(0.168f, 0.605f) }, 18);
		Lathe(k, c, new[] { P(0.0f, 0.598f), P(0.012f, 0.6f), P(0.014f, 0.612f), P(0.008f, 0.628f), P(0f, 0.632f) }, 8);   // the finial
		k.Color = col;
		return c + Vector3.Up * (0.45f * f);
	}

	/// <summary>A banker's lamp on a desk at <paramref name="c"/>, turned by <paramref name="yaw"/> (its shade's opening
	/// toward local -Z): an oval stepped brass base, the upright, the cradle, a half-cylinder of cased green glass (an opal
	/// lining lit within), its pull chain. About 40 cm tall, the shade 25 cm long. Returns the bulb.</summary>
	public static Vector3 BankersLamp(MeshKit k, Vector3 c, float yaw, Material glass)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (Model(k, "lp_bankers_lamp", new Transform3D(new Basis(Vector3.Up, yaw), c), ("brass", LodgeTextures.BrassMat), ("glass", glass), ("opal", OpalMat)))
		{
			k.Color = col;
			return c + new Basis(Vector3.Up, yaw) * new Vector3(0, 0.3f, 0f);
		}
		var was = Push(k, new Transform3D(new Basis(Vector3.Up, yaw), c));
		// the base: an oval of stepped brass, 26 x 16 cm
		k.Mat(LodgeTextures.BrassMat);
		Lathe(k, Vector3.Zero, new[] { new Vector2(0.13f, 0f), new Vector2(0.132f, 0.008f), new Vector2(0.126f, 0.016f), new Vector2(0.112f, 0.02f), new Vector2(0.108f, 0.03f), new Vector2(0.09f, 0.036f), new Vector2(0.05f, 0.04f), new Vector2(0.0f, 0.042f) }, 16, capBottom: true, sz: 0.62f, hardDeg: 40f);
		// the upright: a turned column from the back of the base, a knop, a cradle's crossbar
		var up = new Vector3(0, 0, 0.045f);
		Lathe(k, up, new[] { new Vector2(0.018f, 0.035f), new Vector2(0.012f, 0.05f), new Vector2(0.008f, 0.06f), new Vector2(0.008f, 0.2f), new Vector2(0.014f, 0.212f), new Vector2(0.008f, 0.224f), new Vector2(0.008f, 0.3f), new Vector2(0.012f, 0.31f), new Vector2(0f, 0.316f) }, 8, hardDeg: 60f);
		// the cradle: two arms from the column's head out under the shade's ends
		foreach (float s in new[] { -1f, 1f })
		{
			k.Cylinder(up + new Vector3(0, 0.3f, 0), new Vector3(s * 0.085f, 0.305f, 0.02f), 0.0045f, 0.0045f, 5, false);
			k.Cylinder(new Vector3(s * 0.085f, 0.305f, 0.02f), new Vector3(s * 0.095f, 0.322f, 0.005f), 0.0045f, 0.0045f, 5, true);
		}
		// the shade: a half cylinder along X, its open side down and a little toward the reader, closed at its ends
		const int seg = 12;
		float L = 0.125f, ry = 0.072f, rz = 0.09f;
		Vector3 axis = new(0, 0.33f, 0.0f);
		var tilt = new Basis(Vector3.Right, -0.12f);
		Vector3 S(float x, float a, float grow) => axis + tilt * new Vector3(x, Mathf.Sin(a) * (ry + grow), Mathf.Cos(a) * (rz + grow));
		// (a: from the front lip, 0.18 rad below level toward the reader, up and over to the back lip)
		float a0 = -0.22f, a1 = Mathf.Pi + 0.12f;
		var outer = new List<Vector3[]>();
		var inner = new List<Vector3[]>();
		foreach (float x in new[] { -L, -L * 0.5f, 0f, L * 0.5f, L })
		{
			var ro = new Vector3[seg + 1];
			var ri = new Vector3[seg + 1];
			for (int i = 0; i <= seg; i++)
			{
				float a = Mathf.Lerp(a0, a1, i / (float)seg);
				ro[i] = S(x, a, 0.0035f);
				ri[i] = S(x, a, -0.0035f);
			}
			outer.Add(ro);
			inner.Add(ri);
		}
		void Sheet(List<Vector3[]> rows, Material m, bool outward)
		{
			k.Mat(m);
			for (int r = 0; r < rows.Count - 1; r++)
				for (int i = 0; i < seg; i++)
				{
					Vector3 p0 = rows[r][i], p1 = rows[r][i + 1], p2 = rows[r + 1][i + 1], p3 = rows[r + 1][i];
					Vector3 N(Vector3 p) { var d = p - (axis + tilt * new Vector3(p.X - axis.X, 0, 0)); d = tilt.Inverse() * d; d.X = 0; d = (tilt * d).Normalized(); return outward ? d : -d; }
					k.Tri(p0, p1, p2, N(p0), N(p1), N(p2), new Vector2(p0.X, i * 0.02f) * 4f, new Vector2(p1.X, (i + 1) * 0.02f) * 4f, new Vector2(p2.X, (i + 1) * 0.02f) * 4f);
					k.Tri(p0, p2, p3, N(p0), N(p2), N(p3), new Vector2(p0.X, i * 0.02f) * 4f, new Vector2(p2.X, (i + 1) * 0.02f) * 4f, new Vector2(p3.X, i * 0.02f) * 4f);
				}
		}
		Sheet(outer, glass, true);
		Sheet(inner, OpalMat, false);
		// its ends (half discs) and the rolled lips
		k.Mat(glass);
		foreach (int e in new[] { 0, outer.Count - 1 })
		{
			Vector3 n = e == 0 ? -Vector3.Right : Vector3.Right;
			var mid = (outer[e][0] + outer[e][seg]) * 0.5f;
			for (int i = 0; i < seg; i++) k.Tri(mid, outer[e][i], outer[e][i + 1], n, Vector2.Zero, Vector2.Right * 0.05f, Vector2.Up * 0.05f);
		}
		k.Mat(LodgeTextures.BrassMat);
		foreach (int i in new[] { 0, seg })
			k.Cylinder(outer[0][i] + (outer[0][i] - inner[0][i]) * 0f - Vector3.Right * 0.004f, outer[^1][i] + Vector3.Right * 0.004f, 0.0045f, 0.0045f, 5, true);
		// the pull chain from under the front lip, a little bead on its end
		var lip = (outer[2][0] + inner[2][0]) * 0.5f + new Vector3(0.03f, 0, 0);
		k.Cylinder(lip, lip + Vector3.Down * 0.09f, 0.0016f, 0.0016f, 3, false);
		Lathe(k, lip + Vector3.Down * 0.1f, new[] { new Vector2(0f, 0f), new Vector2(0.006f, 0.004f), new Vector2(0.006f, 0.01f), new Vector2(0f, 0.014f) }, 6);
		k.Xf = was;
		k.Color = col;
		return c + new Basis(Vector3.Up, yaw) * new Vector3(0, 0.3f, 0f);
	}

	/// <summary>A flush ceiling light hung from <paramref name="ceiling"/>: a brass canopy, a frosted glass dome in
	/// <paramref name="glass"/> (a room's switched glow), a brass finial under it. 36 cm across.</summary>
	public static void CeilingDome(MeshKit k, Vector3 ceiling, Material glass)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (Model(k, "lp_ceiling_dome", new Transform3D(Basis.Identity, ceiling), ("brass", LodgeTextures.BrassMat), ("glass", glass)))
		{
			k.Color = col;
			return;
		}
		k.Mat(LodgeTextures.BrassMat);
		Lathe(k, ceiling,new[] { new Vector2(0.16f, -0.004f), new Vector2(0.185f, -0.012f), new Vector2(0.186f, -0.03f), new Vector2(0.172f, -0.036f) }, 18, flip: true, hardDeg: 40f);
		k.Mat(glass);
		Lathe(k, ceiling, new[] { new Vector2(0.17f, -0.034f), new Vector2(0.168f, -0.06f), new Vector2(0.15f, -0.1f), new Vector2(0.11f, -0.13f), new Vector2(0.05f, -0.148f), new Vector2(0.0f, -0.152f) }, 18, flip: true);
		k.Mat(LodgeTextures.BrassMat);
		Lathe(k, ceiling + Vector3.Down * 0.15f, new[] { new Vector2(0.018f, 0f), new Vector2(0.02f, -0.012f), new Vector2(0.01f, -0.03f), new Vector2(0.014f, -0.036f), new Vector2(0f, -0.045f) }, 8, capTop: false, flip: true);
		k.Color = col;
	}

	/// <summary>A wall sconce at <paramref name="at"/> on the wall, facing <paramref name="outward"/>: a round brass
	/// back plate, a swan-neck arm up and out, a cup, a little flared shade in <paramref name="shade"/> over the bulb.
	/// Returns the bulb (where the old sconce's was: 16 cm out, 12 up).</summary>
	public static Vector3 Sconce(MeshKit k, Vector3 at, Vector3 outward, Material shade)
	{
		var col = k.Color;
		k.Color = Colors.White;
		var o = outward.Normalized();
		var side = Vector3.Up.Cross(o).Normalized();   // (right-handed: a mirrored frame turns every face inside out)
		if (Model(k, "lp_sconce", new Transform3D(new Basis(side, Vector3.Up, o), at), ("wood", LodgeTextures.DoorWoodMat), ("brass", LodgeTextures.BrassMat), ("shade", shade), ("bulb", shade)))
		{
			k.Color = col;
			return at + o * 0.16f + Vector3.Up * 0.12f;
		}
		var was = Push(k, new Transform3D(new Basis(side, Vector3.Up, o), at));
		// the back plate: a turned wooden pattress, an oval with its edge rounded (brass, square on to the eye, it mirrored
		// the dark and read as a ring), a brass boss where the arm leaves it
		k.Mat(LodgeTextures.DoorWoodMat);
		var plate = new List<Vector3[]>();
		foreach (var (r, z) in new[] { (0.055f, 0f), (0.06f, 0.008f), (0.056f, 0.016f), (0.04f, 0.021f), (0.0f, 0.022f) })
		{
			var ring = new Vector3[12];
			for (int i = 0; i < 12; i++) { float a = Mathf.Tau * i / 12f; ring[i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 1.6f, z); }
			plate.Add(ring);
		}
		Loft(k, plate, true, 50f);
		k.Mat(LodgeTextures.BrassMat);
		k.Cylinder(new Vector3(0, 0, 0.018f), new Vector3(0, 0, 0.03f), 0.02f, 0.015f, 10, true);   // the boss
		// the swan neck: out of the plate, curving up to the cup
		Vector3 prev = new(0, 0, 0.02f);
		for (int i = 1; i <= 6; i++)
		{
			float u = i / 6f;
			var p = new Vector3(0, -0.01f + 0.08f * (1f - Mathf.Cos(u * Mathf.Pi * 0.5f)) * 1.0f, 0.02f + 0.14f * Mathf.Sin(u * Mathf.Pi * 0.5f));
			k.Cylinder(prev, p, 0.0075f, 0.0075f, 6, i == 6);
			prev = p;
		}
		// the cup and the socket
		var cup = prev;
		Lathe(k, cup, new[] { new Vector2(0.008f, 0f), new Vector2(0.03f, 0.012f), new Vector2(0.032f, 0.018f), new Vector2(0.014f, 0.02f), new Vector2(0.014f, 0.055f), new Vector2(0f, 0.058f) }, 10, hardDeg: 60f);
		// the shade, flared, both faces
		k.Mat(shade);
		var sh = new[] { new Vector2(0.078f, 0.055f), new Vector2(0.07f, 0.09f), new Vector2(0.055f, 0.14f), new Vector2(0.046f, 0.165f) };
		Lathe(k, cup, sh, 14);
		Lathe(k, cup, sh, 14, flip: true);
		k.Xf = was;
		k.Color = col;
		return at + o * 0.16f + Vector3.Up * 0.12f;
	}

	/// <summary>A picture frame round a w by h canvas in the local XY plane (its face +Z, its back on the wall at z =
	/// -0.025): a moulding <paramref name="fw"/> wide, swept round the four sides and mitred at the corners: an eased outer
	/// edge, a crest, a cove down to a lip at the canvas.</summary>
	public static void PictureFrame(MeshKit k, float w, float h, float fw, Material m)
	{
		if (FrameModel(k, w, h, fw, m)) return;
		k.Mat(m);
		Vector3[] R(float grow, float z)
		{
			float hx = w * 0.5f + grow, hy = h * 0.5f + grow, e = 0.0015f;
			// eight points (each corner twice, a hair apart, so each side keeps its own normal), by increasing angle
			return new[]
			{
				new Vector3(hx, hy - e, z), new Vector3(hx - e, hy, z), new Vector3(-hx + e, hy, z), new Vector3(-hx, hy - e, z),
				new Vector3(-hx, -hy + e, z), new Vector3(-hx + e, -hy, z), new Vector3(hx - e, -hy, z), new Vector3(hx, -hy + e, z),
			};
		}
		Loft(k, new List<Vector3[]>
		{
			R(fw, -0.025f), R(fw, 0.012f), R(fw * 0.85f, 0.024f), R(fw * 0.55f, 0.032f), R(fw * 0.3f, 0.026f), R(fw * 0.16f, 0.014f), R(0.006f, 0.012f), R(0f, 0.003f),
		}, true, 40f);
	}

	private static readonly Dictionary<Material, Mesh> _frames = new();

	/// <summary>The frame modelled round a unit canvas at a 0.1 moulding, stretched to this one nine-slice fashion: what's
	/// inside the canvas's edge with the canvas, what's outside it with the moulding (each side keeps its profile).</summary>
	private static bool FrameModel(MeshKit k, float w, float h, float fw, Material m)
	{
		if (!_frames.TryGetValue(m, out var unit))
			_frames[m] = unit = FurnitureKit.Mesh("lp_picture_frame", new() { ["wood"] = m, ["black"] = LodgeTextures.BlackMat });
		if (unit == null) return false;
		float kf = fw / 0.1f;
		float Warp(float x, float size) => Mathf.Abs(x) <= 0.5f ? x * size : Mathf.Sign(x) * (size * 0.5f + (Mathf.Abs(x) - 0.5f) * kf);
		var warped = new ArrayMesh();
		for (int s = 0; s < unit.GetSurfaceCount(); s++)
		{
			var arr = unit.SurfaceGetArrays(s);
			var v = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
			var n = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : (Vector3[])arr[(int)Mesh.ArrayType.Normal];
			for (int i = 0; i < v.Length; i++)
			{
				float sx = Mathf.Abs(v[i].X) <= 0.5f ? w : kf, sy = Mathf.Abs(v[i].Y) <= 0.5f ? h : kf;
				if (n != null) n[i] = new Vector3(n[i].X / sx, n[i].Y / sy, n[i].Z).Normalized();
				v[i] = new Vector3(Warp(v[i].X, w), Warp(v[i].Y, h), v[i].Z);
			}
			arr[(int)Mesh.ArrayType.Vertex] = v;
			if (n != null) arr[(int)Mesh.ArrayType.Normal] = n;
			warped.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
			warped.SurfaceSetMaterial(s, unit.SurfaceGetMaterial(s));
		}
		k.AddMesh(warped, Transform3D.Identity);
		return true;
	}

	/// <summary>A split log laid along <paramref name="along"/> (a mantel shelf): its split face flat on top at
	/// <paramref name="top"/>, the round of the log under it, <paramref name="len"/> long, d deep, h from top to belly;
	/// its ends sawn. <paramref name="room"/> points out of its front.</summary>
	public static void SplitLog(MeshKit k, Vector3 top, Vector3 along, Vector3 room, float len, float d, float h)
	{
		var up = Vector3.Up;
		room = (room - along * room.Dot(along)).Normalized();
		var ax = along.Normalized();
		if (ax.Cross(up).Dot(room) < 0f) ax = -ax;   // (a right-handed frame: the log's the same either way along)
		if (Model(k, "lp_split_log", new Transform3D(new Basis(ax * (len / 3.8f), up * (h / 0.24f), room * (d / 0.36f)), top), ("log", k.CurrentMaterial), ("cut", k.CurrentMaterial)))
			return;
		Vector3[] Ring(float x)
		{
			var pts = new List<Vector3>();
			const int n = 12;
			for (int i = 0; i <= n; i++)
			{
				float a = Mathf.Pi + Mathf.Pi * i / n;
				float u = Mathf.Cos(a) * d * 0.5f, v = Mathf.Sin(a) * h;
				if (i == 0) u += 0.004f;
				if (i == n) u -= 0.004f;
				pts.Add(top + along * x + room * u + up * v);
			}
			pts.Add(top + along * x + room * (d * 0.5f - 0.004f) + up * 0.0f + up * 0.0005f);
			pts.Add(top + along * x - room * (d * 0.5f - 0.004f) + up * 0.0005f);
			return pts.ToArray();
		}
		var r0 = Ring(-len * 0.5f);
		var r1 = Ring(len * 0.5f);
		// (which way round the section runs depends on along x room: keep the faces outward)
		bool flip = room.Cross(up).Dot(along) > 0f;
		Loft(k, new List<Vector3[]> { r0, r1 }, flip, 50f, 1f);
		Cap(k, r0, -along);
		Cap(k, r1, along);
	}

	/// <summary>A hearth's companion set standing at <paramref name="at"/>: a dished iron foot, a stem with a ring on
	/// top, a bar with a poker, a shovel and a brush hung on it. 75 cm.</summary>
	public static void FireTools(MeshKit k, Vector3 at, float yaw)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (Model(k, "lp_fire_tools", new Transform3D(new Basis(Vector3.Up, yaw), at), ("iron", LodgeTextures.IronMat), ("black", LodgeTextures.BlackMat)))
		{
			k.Color = col;
			return;
		}
		var was = Push(k, new Transform3D(new Basis(Vector3.Up, yaw), at));
		k.Mat(LodgeTextures.IronMat);
		Lathe(k, Vector3.Zero, new[] { new Vector2(0.11f, 0f), new Vector2(0.115f, 0.01f), new Vector2(0.09f, 0.025f), new Vector2(0.03f, 0.04f), new Vector2(0.012f, 0.05f), new Vector2(0.01f, 0.06f) }, 12, capBottom: true);
		k.Cylinder(new Vector3(0, 0.05f, 0), new Vector3(0, 0.72f, 0), 0.009f, 0.009f, 6, false);
		Lathe(k, new Vector3(0, 0.72f, 0), new[] { new Vector2(0.012f, 0f), new Vector2(0.016f, 0.01f), new Vector2(0.006f, 0.02f), new Vector2(0.006f, 0.03f) }, 8);
		// the ring handle on top
		for (int i = 0; i < 10; i++)
		{
			float a0 = Mathf.Tau * i / 10f, a1 = Mathf.Tau * (i + 1) / 10f;
			k.Cylinder(new Vector3(Mathf.Cos(a0) * 0.035f, 0.785f + Mathf.Sin(a0) * 0.035f, 0), new Vector3(Mathf.Cos(a1) * 0.035f, 0.785f + Mathf.Sin(a1) * 0.035f, 0), 0.005f, 0.005f, 4, false);
		}
		// the hanging bar, and the tools hung from it round the stem
		k.Cylinder(new Vector3(-0.09f, 0.62f, 0), new Vector3(0.09f, 0.62f, 0), 0.006f, 0.006f, 5, true);
		for (int t = 0; t < 3; t++)
		{
			float x = -0.07f + t * 0.07f;
			var hook = new Vector3(x, 0.62f, 0.012f);
			k.Cylinder(hook, hook + new Vector3(0, -0.06f, 0.01f), 0.005f, 0.005f, 4, false);
			var top = hook + new Vector3(0, -0.06f, 0.01f);
			var foot = new Vector3(x * 1.4f, 0.07f, 0.03f);
			k.Cylinder(top, foot + Vector3.Up * 0.1f, 0.007f, 0.007f, 6, false);
			if (t == 0) k.Cylinder(foot + Vector3.Up * 0.1f, foot + new Vector3(0.03f, 0.03f, 0), 0.007f, 0.004f, 5, true);   // the poker's hook
			else if (t == 1) k.Box(foot + Vector3.Up * 0.055f, new Vector3(0.09f, 0.1f, 0.006f), 1f, new Basis(Vector3.Right, -0.1f));   // the shovel
			else
			{
				k.Mat(LodgeTextures.BlackMat);
				k.Cylinder(foot + Vector3.Up * 0.1f, foot + Vector3.Up * 0.02f, 0.03f, 0.035f, 8, true);   // the brush's bristles
				k.Mat(LodgeTextures.IronMat);
			}
		}
		k.Xf = was;
		k.Color = col;
	}

	/// <summary>A net-glass candle holder (a bar table's): a red glass globe glowing in <paramref name="glow"/>, 10 cm.</summary>
	public static void TableCandle(MeshKit k, Vector3 c, Material glow)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (Model(k, "lp_table_candle", new Transform3D(Basis.Identity, c), ("glow", glow), ("black", LodgeTextures.BlackMat)))
		{
			k.Color = col;
			return;
		}
		k.Mat(glow);
		Lathe(k, c, new[] { new Vector2(0.026f, 0f), new Vector2(0.04f, 0.018f), new Vector2(0.048f, 0.045f), new Vector2(0.046f, 0.07f), new Vector2(0.036f, 0.092f), new Vector2(0.03f, 0.1f), new Vector2(0.026f, 0.096f) }, 12, capBottom: true);
		k.Mat(LodgeTextures.BlackMat);
		Lathe(k, c, new[] { new Vector2(0.026f, 0.096f), new Vector2(0.0f, 0.07f) }, 12);   // (the dark of its mouth)
		k.Color = col;
	}

	// ---------------------------------------------------------------- case furniture

	/// <summary>A drawer front (the drawer's own, <see cref="SkiLodge"/>'s MakeDrawer): a board with eased edges, a raised
	/// field on a deep one, a turned brass knob (two on a wide one), centred at <paramref name="c"/>, its face toward +Z.</summary>
	public static void DrawerFront(MeshKit k, Vector3 c, float w, float h, Material wood)
	{
		bool field = h > 0.13f;
		if (!Model(k, field ? "lp_drawer_front_field" : "lp_drawer_front", new Transform3D(Basis.FromScale(new Vector3(w / 0.5f, h / (field ? 0.25f : 0.1f), 1f)), c), ("wood", wood)))
		{
			k.Mat(wood);
			k.BevelBox(c, new Vector3(w, h, 0.022f), 0.006f);
			if (field) k.BevelBox(c + new Vector3(0, 0, 0.011f), new Vector3(w - 0.07f, h - 0.06f, 0.008f), 0.006f);
		}
		k.Mat(LodgeTextures.BrassMat);
		float zf = c.Z + (h > 0.13f ? 0.019f : 0.011f);
		foreach (float x in w > 0.6f ? new[] { -w * 0.28f, w * 0.28f } : new[] { 0f })
			Knob(k, new Vector3(c.X + x, c.Y, zf), Vector3.Back);
	}

	/// <summary>A small turned brass knob on a face at <paramref name="at"/>, sticking out along <paramref name="n"/>.</summary>
	public static void Knob(MeshKit k, Vector3 at, Vector3 n)
	{
		var b = Basis.LookingAt(-n, Mathf.Abs(n.Y) > 0.9f ? Vector3.Forward : Vector3.Up) * new Basis(Vector3.Right, Mathf.Pi * 0.5f);   // (local +Y along n)
		if (Model(k, "lp_knob", new Transform3D(b, at), ("brass", LodgeTextures.BrassMat))) return;
		var was = Push(k, new Transform3D(b, at));
		Lathe(k, Vector3.Zero, new[] { new Vector2(0.011f, 0f), new Vector2(0.006f, 0.006f), new Vector2(0.005f, 0.014f), new Vector2(0.014f, 0.02f), new Vector2(0.015f, 0.026f), new Vector2(0.01f, 0.032f), new Vector2(0f, 0.034f) }, 8, hardDeg: 60f);
		k.Xf = was;
	}

	/// <summary>A piece of case furniture (a nightstand, a chest, a vanity, a cabinet) built round its drawers: standing
	/// at <paramref name="xf"/> (origin on the floor, its front toward local -Z), w wide, h tall, d deep; a top with a
	/// little overhang and its edges eased, the sides, the back, a bottom; on short turned legs or a plinth; the front's
	/// rails between the <paramref name="openings"/> (the drawers' heights, which stand in them flush), a raised panel
	/// where a rail is deep, or a cubby left open. <paramref name="top"/> draws the top in its own material (a counter).</summary>
	public static void Carcass(MeshKit k, Transform3D xf, float w, float h, float d, Material wood, IList<(float y0, float y1)> openings,
		float openW = 0f, float legH = 0.1f, bool legs = true, bool cubby = false, Material top = null, float topThick = 0.026f, float topBack = 0f,
		bool drawTop = true, int bays = 1)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (CaseModel(w, h, d) is { } cm && Model(k, cm, xf, ("wood", wood), ("top", top ?? wood), ("brass", LodgeTextures.BrassMat)))
		{
			k.Color = col;
			return;
		}
		var was = Push(k, xf);
		float t = openW > 0f ? Mathf.Clamp((w - openW - 0.01f) * 0.5f, 0.018f, 0.06f) : 0.022f;
		float yTop = h - topThick;
		k.Mat(top ?? wood);
		// the top: 1.5 cm over at the front and sides, flush at the back (or run back to a wall)
		if (drawTop) k.BevelBox(new Vector3(0, h - topThick * 0.5f, -0.015f * 0.5f + topBack * 0.5f), new Vector3(w + 0.03f, topThick, d + 0.015f + topBack), 0.008f);
		k.Mat(wood);
		foreach (float s in new[] { -1f, 1f })
			k.BevelBox(new Vector3(s * (w * 0.5f - t * 0.5f), (legH + yTop) * 0.5f, 0), new Vector3(t, yTop - legH, d), 0.004f);
		k.Box(new Vector3(0, (legH + yTop) * 0.5f, d * 0.5f - 0.006f), new Vector3(w - t * 2f, yTop - legH, 0.012f), 1f);
		k.BevelBox(new Vector3(0, legH + 0.01f, 0.005f), new Vector3(w - t * 2f, 0.02f, d - 0.01f), 0.003f);
		// the front: rails and panels where there's no drawer
		var spans = new List<(float, float)> { (legH, yTop) };
		foreach (var (o0, o1) in openings)
		{
			var next = new List<(float, float)>();
			foreach (var (s0, s1) in spans)
			{
				if (o1 + 0.004f <= s0 || o0 - 0.004f >= s1) { next.Add((s0, s1)); continue; }
				if (o0 - 0.004f > s0) next.Add((s0, o0 - 0.004f));
				if (o1 + 0.004f < s1) next.Add((o1 + 0.004f, s1));
			}
			spans = next;
		}
		float zf = -d * 0.5f + 0.01f;
		for (int i = 0; i < spans.Count; i++)
		{
			var (s0, s1) = spans[i];
			float sh = s1 - s0;
			if (sh < 0.004f) continue;
			if (cubby && i == 0 && sh > 0.12f)
			{
				// an open cubby: a lip along the bottom, the dark inside
				k.BevelBox(new Vector3(0, s0 + 0.012f, zf), new Vector3(w - t * 2f, 0.024f, 0.02f), 0.004f);
				continue;
			}
			k.BevelBox(new Vector3(0, (s0 + s1) * 0.5f, zf), new Vector3(w - t * 2f, sh, 0.02f), 0.004f);
			if (sh > 0.16f)
			{
				// doors' raised panels and their knobs (a cupboard below the drawers; a long one in bays)
				float bw = (w - t * 2f) / bays;
				for (int b = 0; b < bays; b++)
				{
					float bx = -w * 0.5f + t + bw * (b + 0.5f);
					k.BevelBox(new Vector3(bx, (s0 + s1) * 0.5f, zf - 0.012f), new Vector3(bw - 0.09f, sh - 0.09f, 0.008f), 0.006f);
					if (bays > 1) k.BevelBox(new Vector3(bx + bw * 0.5f, (s0 + s1) * 0.5f, zf - 0.012f), new Vector3(0.012f, sh, 0.01f), 0.003f);   // (the doors' meeting line)
					k.Mat(LodgeTextures.BrassMat);
					Knob(k, new Vector3(bx + (b % 2 == 0 ? 1f : -1f) * (bw * 0.5f - 0.07f), s1 - 0.08f, zf - 0.016f), Vector3.Forward);
					k.Mat(wood);
				}
			}
		}
		// the stand: turned legs under an apron, or a plinth set back
		if (legH > 0.01f)
		{
			if (legs)
			{
				k.BevelBox(new Vector3(0, legH - 0.022f, -d * 0.5f + 0.012f), new Vector3(w - 0.02f, 0.044f, 0.024f), 0.005f);
				foreach (float s in new[] { -1f, 1f })
					k.BevelBox(new Vector3(s * (w * 0.5f - 0.012f), legH - 0.022f, 0), new Vector3(0.024f, 0.044f, d - 0.02f), 0.005f);
				foreach (float sx in new[] { -1f, 1f })
					foreach (float sz in new[] { -1f, 1f })
						Lathe(k, new Vector3(sx * (w * 0.5f - 0.028f), 0, sz * (d * 0.5f - 0.028f)), new[] { new Vector2(0.012f, 0f), new Vector2(0.015f, 0.012f), new Vector2(0.019f, legH * 0.55f), new Vector2(0.021f, legH - 0.004f), new Vector2(0.017f, legH) }, 8, capBottom: true);
			}
			else k.BevelBox(new Vector3(0, legH * 0.5f, 0.012f), new Vector3(w - 0.03f, legH, d - 0.025f), 0.006f);
		}
		k.Xf = was;
		k.Color = col;
	}

	/// <summary>The lodge's case pieces as modelled (each the one call that builds it: the back bar's cupboards, the
	/// pantry's, a nightstand, a vanity, room 204's dresser), by their sizes; null for any other.</summary>
	private static string CaseModel(float w, float h, float d)
	{
		bool Is(float a, float b, float c) => Near(w, a) && Near(h, b) && Near(d, c);
		if (Is(5.0f, 1.0f, 0.6f)) return "lp_case_backbar";
		if (Is(0.93f, 0.94f, 0.6f)) return "lp_case_cabinet";
		if (Is(0.5f, 0.6f, 0.48f)) return "lp_case_nightstand";
		if (Is(0.9f, 0.9f, 0.56f)) return "lp_case_vanity";
		if (Is(1.2f, 1.1f, 0.5f)) return "lp_case_dresser";
		return null;
	}

	/// <summary>A wardrobe's or a closet's case (its door is its own: <see cref="CabinetDoor"/>): at <paramref name="xf"/>
	/// (its front toward local +Z, as SkiLodge's MakeCabinet has it), the sides and the top and the back, a base rail under
	/// the door, a moulded cornice over it, a rail inside and the dark of it.</summary>
	public static void CabinetCase(MeshKit k, Transform3D xf, Vector3 size, Material wood, bool rail)
	{
		var col = k.Color;
		k.Color = Colors.White;
		string cm = !rail || !Near(size.Z, 0.6f) ? null
			: Near(size.X, 1.1f) && Near(size.Y, 2.1f) ? "lp_cabinet_case_110"
			: Near(size.X, 1.2f) && Near(size.Y, 2.3f) ? "lp_cabinet_case_120" : null;
		if (cm != null && Model(k, cm, xf, ("wood", wood), ("back", wood), ("brass", LodgeTextures.BrassMat)))
		{
			k.Color = col;
			return;
		}
		var was = Push(k, xf);
		float w = size.X, h = size.Y, d = size.Z, t = 0.024f;
		k.Mat(wood);
		foreach (float s in new[] { -1f, 1f })
		{
			k.BevelBox(new Vector3(s * (w * 0.5f - t * 0.5f), h * 0.5f, 0), new Vector3(t, h, d), 0.005f);
			// a recessed panel down each side (it reads as joinery from across the room)
			k.BevelBox(new Vector3(s * (w * 0.5f + 0.002f), h * 0.55f, 0), new Vector3(0.006f, h * 0.68f, d - 0.14f), 0.003f);
		}
		k.BevelBox(new Vector3(0, h - 0.012f, 0), new Vector3(w - t * 2f, 0.024f, d), 0.004f);
		// the base: a rail under the door, the bottom inside
		k.BevelBox(new Vector3(0, 0.05f, d * 0.5f - 0.012f), new Vector3(w - t * 2f + 0.002f, 0.1f, 0.026f), 0.006f);
		k.BevelBox(new Vector3(0, 0.1f, 0), new Vector3(w - t * 2f, 0.02f, d - 0.03f), 0.003f);
		// the cornice: stepped out twice, eased
		k.BevelBox(new Vector3(0, h + 0.012f, 0.004f), new Vector3(w + 0.03f, 0.024f, d + 0.022f), 0.006f);
		k.BevelBox(new Vector3(0, h + 0.04f, 0.008f), new Vector3(w + 0.07f, 0.034f, d + 0.04f), 0.01f);
		k.BevelBox(new Vector3(0, h + 0.066f, 0.006f), new Vector3(w + 0.05f, 0.018f, d + 0.032f), 0.006f);
		// the dark inside: the back, a shade darker
		k.Color = new Color(0.32f, 0.3f, 0.28f);
		k.Box(new Vector3(0, h * 0.5f, -d * 0.5f + 0.006f), new Vector3(w - t * 2f, h - 0.02f, 0.012f), 1f);
		k.Color = Colors.White;
		if (rail)
		{
			k.Mat(LodgeTextures.BrassMat);
			k.Cylinder(new Vector3(-w * 0.5f + t, h - 0.24f, 0), new Vector3(w * 0.5f - t, h - 0.24f, 0), 0.011f, 0.011f, 8, false);
		}
		k.Xf = was;
		k.Color = col;
	}

	/// <summary>A cabinet's door (hinged at x = 0, w wide, its face +Z, from <paramref name="y0"/> to <paramref name="y1"/>):
	/// stiles and rails round two raised panels, a turned knob at <paramref name="knob"/>.</summary>
	public static void CabinetDoor(MeshKit k, float w, float y0, float y1, Vector3 knob, Material wood)
	{
		var col = k.Color;
		k.Color = Colors.White;
		float h = y1 - y0, st = 0.085f, rl = 0.1f;
		string dm = !Near(y0, 0.1f) || !Near(knob.Y, 1.1f) || !Near(knob.Z, 0.025f) || !Near(knob.X, w - 0.08f) ? null
			: Near(w, 1.1f) && Near(y1, 2.1f - 0.008f) ? "lp_cabinet_door_110"
			: Near(w, 1.2f) && Near(y1, 2.3f - 0.008f) ? "lp_cabinet_door_120" : null;
		if (dm != null && Model(k, dm, Transform3D.Identity, ("wood", wood), ("brass", LodgeTextures.BrassMat)))
		{
			k.Color = col;
			return;
		}
		k.Mat(wood);
		k.BevelBox(new Vector3(w * 0.5f, (y0 + y1) * 0.5f, 0.0125f), new Vector3(w - 0.004f, h, 0.025f), 0.006f);
		// the frame stands proud of the two panels' fields: rails and stiles as a raised lip, the fields raised again
		float mid = y0 + h * 0.46f;
		foreach (var (p0, p1) in new[] { (y0 + rl, mid - rl * 0.5f), (mid + rl * 0.5f, y1 - rl) })
		{
			k.BevelBox(new Vector3(w * 0.5f, (p0 + p1) * 0.5f, 0.027f), new Vector3(w - st * 2f, p1 - p0, 0.006f), 0.004f);
			k.BevelBox(new Vector3(w * 0.5f, (p0 + p1) * 0.5f, 0.031f), new Vector3(w - st * 2f - 0.06f, p1 - p0 - 0.06f, 0.006f), 0.006f);
		}
		k.Mat(LodgeTextures.BrassMat);
		Knob(k, knob, Vector3.Back);
		// the hinges, two brass knuckles on the hinge side
		foreach (float hy in new[] { y0 + 0.2f, y1 - 0.2f })
			k.Cylinder(new Vector3(0.002f, hy - 0.04f, 0.0f), new Vector3(0.002f, hy + 0.04f, 0.0f), 0.007f, 0.007f, 6, true);
		k.Color = col;
	}

	/// <summary>A writing desk at <paramref name="xf"/> (front toward local -Z, w wide, d deep, its top at h): a top with
	/// a moulded edge, an apron with the drawer's opening in its front (from x = -dw/2 to dw/2, dy0 to dy1), four tapered
	/// legs turned at their feet.</summary>
	public static void WritingDesk(MeshKit k, Transform3D xf, float w, float d, float h, float dw, float dy0, float dy1, Material wood, float inset = 0.04f)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (Near(w, 1.2f) && Near(d, 0.64f) && Near(h, 0.765f) && Near(dw, 0.9f) && Near(dy0, 0.58f) && Near(dy1, 0.7f) && Near(inset, 0.031f)
			&& Model(k, "lp_writing_desk", xf, ("wood", wood)))
		{
			k.Color = col;
			return;
		}
		var was = Push(k, xf);
		k.Mat(wood);
		RoundSlab(k, new Vector3(0, h, 0), w * 0.5f, d * 0.5f, 0.032f, 0.02f, 0.012f, 2);
		float ay1 = h - 0.032f, ay0 = Mathf.Min(dy0 - 0.012f, ay1 - 0.12f), lw = 0.05f;
		float fz = -d * 0.5f + inset + 0.01f, lz0 = -d * 0.5f + inset + lw * 0.5f, lz1 = d * 0.5f - 0.04f;
		// the apron: back and sides whole, the front either side of the drawer and a rail under it
		k.BevelBox(new Vector3(0, (ay0 + ay1) * 0.5f, lz1), new Vector3(w - 0.08f, ay1 - ay0, 0.02f), 0.004f);
		foreach (float s in new[] { -1f, 1f })
		{
			k.BevelBox(new Vector3(s * (w * 0.5f - 0.04f), (ay0 + ay1) * 0.5f, (lz0 + lz1) * 0.5f), new Vector3(0.02f, ay1 - ay0, lz1 - lz0), 0.004f);
			float x0 = dw * 0.5f + 0.004f, x1 = w * 0.5f - 0.04f - lw * 0.5f;
			if (x1 - x0 > 0.01f) k.BevelBox(new Vector3(s * (x0 + x1) * 0.5f, (ay0 + ay1) * 0.5f, fz), new Vector3(x1 - x0, ay1 - ay0, 0.02f), 0.004f);
		}
		if (dy0 - 0.004f > ay0) k.BevelBox(new Vector3(0, (ay0 + dy0 - 0.004f) * 0.5f, fz), new Vector3(dw + 0.01f, dy0 - 0.004f - ay0, 0.02f), 0.003f);
		if (ay1 > dy1 + 0.004f) k.BevelBox(new Vector3(0, (ay1 + dy1 + 0.004f) * 0.5f, fz), new Vector3(dw + 0.01f, ay1 - dy1 - 0.004f, 0.02f), 0.003f);
		// the legs: square where the apron meets them, tapering to a turned foot
		foreach (float sx in new[] { -1f, 1f })
			foreach (float sz in new[] { -1f, 1f })
			{
				var p = new Vector3(sx * (w * 0.5f - 0.04f), 0, sz < 0 ? lz0 : lz1);
				k.BevelBox(p + new Vector3(0, (ay0 + ay1) * 0.5f, 0), new Vector3(lw, ay1 - ay0, lw), 0.005f);
				Lathe(k, p, new[] { new Vector2(0.014f, 0f), new Vector2(0.018f, 0.01f), new Vector2(0.015f, 0.03f), new Vector2(0.017f, 0.045f), new Vector2(0.021f, ay0 * 0.6f), new Vector2(0.026f, ay0 - 0.02f), new Vector2(0.03f, ay0 + 0.002f) }, 8, capBottom: true);
			}
		k.Xf = was;
		k.Color = col;
	}

	// ---------------------------------------------------------------- the bathroom

	/// <summary>A built-in tub at <paramref name="xf"/> (long along local X, half sizes hx, hz, its rim at h): its apron
	/// rounded at the corners, a rolled rim, the basin sloping in to its floor, a drain, the taps and spout at the +X end.</summary>
	public static void Tub(MeshKit k, Transform3D xf, float hx, float hz, float h, Material porcelain)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (Near(hx, 0.9f) && Near(hz, 0.4f) && Near(h, 0.56f) && Model(k, "lp_tub", xf, ("porcelain", porcelain), ("brass", LodgeTextures.BrassMat)))
		{
			k.Color = col;
			return;
		}
		var was = Push(k, xf);
		k.Mat(porcelain);
		var rings = new List<Vector3[]>
		{
			RoundRect(hx - 0.01f, hz - 0.01f, 0.05f, 0f, 3),
			RoundRect(hx, hz, 0.055f, 0.03f, 3),
			RoundRect(hx, hz, 0.055f, h - 0.03f, 3),
			RoundRect(hx + 0.008f, hz + 0.008f, 0.06f, h - 0.012f, 3),
			RoundRect(hx - 0.01f, hz - 0.01f, 0.05f, h, 3),
			RoundRect(hx - 0.07f, hz - 0.07f, 0.2f, h, 3),
			RoundRect(hx - 0.08f, hz - 0.08f, 0.2f, h - 0.02f, 3),
			RoundRect(hx - 0.16f, hz - 0.12f, 0.17f, 0.2f, 3),
			RoundRect(hx - 0.2f, hz - 0.15f, 0.12f, 0.12f, 3),
		};
		Loft(k, rings, false, 40f);
		Cap(k, rings[^1], Vector3.Up);
		k.Mat(LodgeTextures.BrassMat);
		Lathe(k, new Vector3(hx - 0.32f, 0.12f, 0), new[] { new Vector2(0.025f, 0.001f), new Vector2(0.026f, 0.004f), new Vector2(0f, 0.005f) }, 10);
		// the spout and two cross-handled taps on the deck at the +X end
		var deck = new Vector3(hx - 0.04f, h, 0);
		Lathe(k, deck, new[] { new Vector2(0.022f, 0f), new Vector2(0.016f, 0.012f), new Vector2(0.014f, 0.06f), new Vector2(0f, 0.064f) }, 8);
		k.Cylinder(deck + new Vector3(0, 0.05f, 0), deck + new Vector3(-0.12f, 0.06f, 0), 0.012f, 0.01f, 8, false);
		k.Cylinder(deck + new Vector3(-0.12f, 0.06f, 0), deck + new Vector3(-0.15f, 0.03f, 0), 0.01f, 0.009f, 8, true);
		foreach (float s in new[] { -1f, 1f })
		{
			var tp = deck + new Vector3(0, 0, s * 0.1f);
			Lathe(k, tp, new[] { new Vector2(0.02f, 0f), new Vector2(0.012f, 0.012f), new Vector2(0.01f, 0.05f), new Vector2(0f, 0.056f) }, 8);
			k.Box(tp + new Vector3(0, 0.05f, 0), new Vector3(0.07f, 0.01f, 0.012f), 1f);
			k.Box(tp + new Vector3(0, 0.05f, 0), new Vector3(0.012f, 0.01f, 0.07f), 1f);
		}
		k.Xf = was;
		k.Color = col;
	}

	/// <summary>A vanity's top (its front toward local -Z, w by d, its top at h), with an oval basin let into it at
	/// (bx, bz), its taps behind the bowl.</summary>
	public static void BasinTop(MeshKit k, Transform3D xf, float w, float d, float h, Material top, Material bowl, float bx = 0f, float bz = 0f)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (Near(w, 0.94f) && Near(d, 0.6f) && Near(h, 0.9f) && Near(bx, 0f) && Near(bz, -0.04f)
			&& Model(k, "lp_basin_top", xf, ("top", top), ("bowl", bowl), ("brass", LodgeTextures.BrassMat)))
		{
			k.Color = col;
			return;
		}
		var was = Push(k, xf);
		k.Mat(top);
		const int seg = 4, n = 20;
		float t = 0.035f, rx = Mathf.Min(0.22f, w * 0.5f - 0.08f), rz = Mathf.Min(0.16f, d * 0.5f - 0.07f);
		var lip = Ring(rx + 0.02f, rz + 0.02f, h, n, bx, bz);
		var rim = Ring(rx, rz, h, n, bx, bz);
		var low = RoundRect(w * 0.5f, d * 0.5f, 0.02f, h - t, seg);
		var mid = RoundRect(w * 0.5f + 0.004f, d * 0.5f + 0.004f, 0.024f, h - t * 0.5f, seg);
		var hi = RoundRect(w * 0.5f, d * 0.5f, 0.02f, h - 0.006f, seg);
		var top0 = RoundRect(w * 0.5f - 0.006f, d * 0.5f - 0.006f, 0.014f, h, seg);
		Loft(k, new List<Vector3[]> { low, mid, hi, top0 }, false, 50f);
		Cap(k, low, Vector3.Down);
		// the top's upper face: from its outline in to the basin's rim
		Zip(k, top0, lip, new Vector3(bx, h, bz), Vector3.Up);
		k.Mat(bowl);
		var lip2 = Ring(rx + 0.02f, rz + 0.02f, h + 0.006f, n, bx, bz);
		Loft(k, new List<Vector3[]> { lip, lip2, Ring(rx, rz, h + 0.004f, n, bx, bz), rim, Ring(rx * 0.92f, rz * 0.9f, h - 0.06f, n, bx, bz), Ring(rx * 0.62f, rz * 0.58f, h - 0.13f, n, bx, bz), Ring(rx * 0.25f, rz * 0.22f, h - 0.15f, n, bx, bz) }, false, 60f);
		Cap(k, Ring(rx * 0.25f, rz * 0.22f, h - 0.15f, n, bx, bz), Vector3.Up);
		k.Mat(LodgeTextures.BrassMat);
		Lathe(k, new Vector3(bx, h - 0.149f, bz), new[] { new Vector2(0.018f, 0f), new Vector2(0.018f, 0.002f), new Vector2(0f, 0.003f) }, 8);
		// the taps: a spout from behind the bowl, a handle either side
		var back = new Vector3(bx, h, bz + rz + 0.06f);
		Lathe(k, back, new[] { new Vector2(0.02f, 0f), new Vector2(0.014f, 0.012f), new Vector2(0.012f, 0.07f), new Vector2(0f, 0.075f) }, 8);
		k.Cylinder(back + new Vector3(0, 0.065f, 0), back + new Vector3(0, 0.07f, -0.1f), 0.01f, 0.009f, 8, false);
		k.Cylinder(back + new Vector3(0, 0.07f, -0.1f), back + new Vector3(0, 0.04f, -0.12f), 0.009f, 0.008f, 8, true);
		foreach (float s in new[] { -1f, 1f })
		{
			var tp = back + new Vector3(s * 0.11f, 0, 0);
			Lathe(k, tp, new[] { new Vector2(0.018f, 0f), new Vector2(0.01f, 0.01f), new Vector2(0.009f, 0.04f), new Vector2(0f, 0.045f) }, 8);
			k.Box(tp + new Vector3(0, 0.04f, 0), new Vector3(0.06f, 0.009f, 0.011f), 1f);
			k.Box(tp + new Vector3(0, 0.04f, 0), new Vector3(0.011f, 0.009f, 0.06f), 1f);
		}
		k.Xf = was;
		k.Color = col;
	}

	/// <summary>A close-coupled lavatory at <paramref name="xf"/> (its front toward local -Z, its back to the wall at
	/// local +Z): a pedestal flaring to an oval bowl (rim at 40 cm), a seat and lid down, the cistern on its back, its
	/// lid and lever.</summary>
	public static void Lavatory(MeshKit k, Transform3D xf, Material porcelain, Material seat)
	{
		var col = k.Color;
		k.Color = Colors.White;
		if (Model(k, "lp_lavatory", xf, ("porcelain", porcelain), ("seat", seat), ("brass", LodgeTextures.BrassMat)))
		{
			k.Color = col;
			return;
		}
		var was = Push(k, xf);
		k.Mat(porcelain);
		var bowl = new List<Vector3[]>();
		const int n = 16;
		foreach (var (rx, rz, y, zc) in new[] { (0.11f, 0.15f, 0f, 0.0f), (0.105f, 0.14f, 0.03f, 0.0f), (0.09f, 0.12f, 0.14f, -0.01f), (0.13f, 0.19f, 0.28f, -0.04f), (0.175f, 0.23f, 0.37f, -0.06f), (0.18f, 0.235f, 0.395f, -0.06f) })
			bowl.Add(Ring(rx, rz, y, n, 0, zc));
		Loft(k, bowl, false, 60f);
		Cap(k, bowl[^1], Vector3.Up);
		// the seat and its lid, closed: a rounded oval slab, a hinge at the back
		k.Mat(seat);
		var lid = new List<Vector3[]> { Ring(0.178f, 0.232f, 0.398f, n, 0, -0.065f), Ring(0.186f, 0.24f, 0.41f, n, 0, -0.065f), Ring(0.184f, 0.238f, 0.43f, n, 0, -0.065f), Ring(0.17f, 0.222f, 0.438f, n, 0, -0.065f) };
		Loft(k, lid, false, 60f);
		Cap(k, lid[^1], Vector3.Up);
		// the cistern: a rounded box on the bowl's back, its lid stepped out
		k.Mat(porcelain);
		RoundSlab(k, new Vector3(0, 0.78f, 0.13f), 0.21f, 0.085f, 0.4f, 0.03f, 0.02f, 3, false);
		RoundSlab(k, new Vector3(0, 0.81f, 0.13f), 0.225f, 0.095f, 0.035f, 0.035f, 0.012f, 3);
		k.Mat(LodgeTextures.BrassMat);
		k.Cylinder(new Vector3(-0.17f, 0.7f, 0.04f), new Vector3(-0.17f, 0.7f, 0.025f), 0.016f, 0.016f, 8, true);
		k.Cylinder(new Vector3(-0.17f, 0.7f, 0.025f), new Vector3(-0.1f, 0.69f, 0.02f), 0.007f, 0.006f, 6, true);
		k.Xf = was;
		k.Color = col;
	}
}
