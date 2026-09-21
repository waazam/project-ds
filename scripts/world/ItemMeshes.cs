using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// Code-built, PS2-budget meshes for the things the player picks up (100-400
/// triangles each), plus the small geometry helpers they share with
/// FriendBody and Bird (lofted tubes, lathes, discs, rings, ribbons).
///
/// Every item is authored in "pickup space": y = 0 is the surface it rests
/// on, so a Pickup only has to snap its origin onto whatever is under it.
/// An item may come with a <em>rest</em>, scenery that stays behind once the
/// item is taken (the axe's chopping stump, the key's flat stone).
/// </summary>
public static class ItemMeshes
{
	/// <summary>What a build produced: where to aim the pick sphere, and any live cue to animate.</summary>
	public struct Built
	{
		public Vector3 PickCenter;
		public float PickRadius;
		public OmniLight3D Glow;              // lantern flame / walkie LED light
		public StandardMaterial3D Led;        // per-instance emissive to pulse
		public int Triangles;
	}

	// ───────────────────────────── geometry helpers ─────────────────────────────

	public struct Ring
	{
		public Vector3 C; public float Rx, Rz;
		public Ring(Vector3 c, float rx, float rz) { C = c; Rx = rx; Rz = rz; }
		public Ring(Vector3 c, float r) { C = c; Rx = r; Rz = r; }
	}

	/// <summary>
	/// Lofted tube through ring centres. Each ring is an ellipse perpendicular to the
	/// path; Rx lies along <paramref name="side"/> (projected), Rz along side x tangent.
	/// For an upright tube with side = +X, angle 0 = +X and 90 deg = +Z (front).
	/// Normals account for taper; UVs run u around (0..uScale), v along (vScale per metre).
	/// </summary>
	public static void Loft(MeshKit k, IList<Ring> rings, int sides, bool capStart, bool capEnd,
		Vector3? side = null, float vScale = 4f, Func<int, float, float> radial = null,
		Func<int, float, Color> tone = null, float uScale = 1f, float dome = 0f)
	{
		int n = rings.Count;
		var pts = new Vector3[n, sides + 1];
		var nrm = new Vector3[n, sides + 1];
		var ts = new Vector3[n];
		var vc = new float[n];
		Vector3 sref = side ?? Vector3.Right;
		for (int r = 0; r < n; r++)
		{
			var ra = rings[Math.Max(r - 1, 0)]; var rb = rings[Math.Min(r + 1, n - 1)];
			Vector3 d = rb.C - ra.C;
			float ds = Mathf.Max(d.Length(), 1e-4f);
			Vector3 t = d / ds;
			ts[r] = t;
			float slope = ((rb.Rx + rb.Rz) - (ra.Rx + ra.Rz)) * 0.5f / ds;
			Vector3 x = (sref - t * sref.Dot(t)).Normalized();
			Vector3 f = x.Cross(t).Normalized();
			var ring = rings[r];
			for (int i = 0; i <= sides; i++)
			{
				float a = Mathf.Tau * i / sides;
				float m = radial?.Invoke(r, a % Mathf.Tau) ?? 1f;
				float c = Mathf.Cos(a), s = Mathf.Sin(a);
				pts[r, i] = ring.C + (x * c * ring.Rx + f * s * ring.Rz) * m;
				Vector3 rn = (x * c / Mathf.Max(ring.Rx, 1e-4f) + f * s / Mathf.Max(ring.Rz, 1e-4f)).Normalized();
				nrm[r, i] = (rn - t * slope).Normalized();
			}
			vc[r] = r == 0 ? 0f : vc[r - 1] + ring.C.DistanceTo(rings[r - 1].C) * vScale;
		}
		Color baseColor = k.Color;
		for (int r = 0; r < n - 1; r++)
			for (int i = 0; i < sides; i++)
			{
				int j = i + 1;
				if (tone != null) k.Color = tone(r, Mathf.Tau * (i + 0.5f) / sides);
				float u0 = (float)i / sides * uScale, u1 = (float)j / sides * uScale;
				k.Tri(pts[r, i], pts[r, j], pts[r + 1, j], nrm[r, i], nrm[r, j], nrm[r + 1, j],
					new Vector2(u0, vc[r]), new Vector2(u1, vc[r]), new Vector2(u1, vc[r + 1]));
				k.Tri(pts[r, i], pts[r + 1, j], pts[r + 1, i], nrm[r, i], nrm[r + 1, j], nrm[r + 1, i],
					new Vector2(u0, vc[r]), new Vector2(u1, vc[r + 1]), new Vector2(u0, vc[r + 1]));
			}
		void Cap(int r, float dir)
		{
			Vector3 cn = ts[r] * dir;
			var ring = rings[r];
			if (tone != null) k.Color = tone(r, -1f);
			Vector3 tip = ring.C + cn * dome * Mathf.Min(ring.Rx, ring.Rz);
			for (int i = 0; i < sides; i++)
			{
				float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
				k.Tri(tip, pts[r, i], pts[r, i + 1], cn, cn, cn, new Vector2(0.5f, 0.5f),
					new Vector2(0.5f + 0.5f * Mathf.Cos(a0), 0.5f + 0.5f * Mathf.Sin(a0)),
					new Vector2(0.5f + 0.5f * Mathf.Cos(a1), 0.5f + 0.5f * Mathf.Sin(a1)));
			}
		}
		if (capStart) Cap(0, -1f);
		if (capEnd) Cap(n - 1, 1f);
		k.Color = baseColor;
	}

	/// <summary>Turned profile around local +Y: each (x = radius, y = height). sx/sz squash it into an oval.</summary>
	public static void Lathe(MeshKit k, IList<Vector2> profile, int sides, bool capBottom, bool capTop,
		float sx = 1f, float sz = 1f, float vScale = 4f, Func<int, float, Color> tone = null)
	{
		var rings = new List<Ring>(profile.Count);
		foreach (var p in profile) rings.Add(new Ring(new Vector3(0, p.Y, 0), p.X * sx, p.X * sz));
		Loft(k, rings, sides, capBottom, capTop, Vector3.Right, vScale, null, tone);
	}

	/// <summary>Flat disc (triangle fan) facing <paramref name="normal"/>; UVs map the disc onto the whole texture.</summary>
	public static void Disc(MeshKit k, Vector3 c, Vector3 normal, float radius, int sides, float angle0 = 0f, bool doubleSided = false)
	{
		Vector3 refv = Mathf.Abs(normal.Y) > 0.9f ? Vector3.Right : Vector3.Up;
		Vector3 x = (refv - normal * refv.Dot(normal)).Normalized();
		Vector3 y = normal.Cross(x);
		for (int i = 0; i < sides; i++)
		{
			float a0 = angle0 + Mathf.Tau * i / sides, a1 = angle0 + Mathf.Tau * (i + 1) / sides;
			Vector3 p0 = c + (x * Mathf.Cos(a0) + y * Mathf.Sin(a0)) * radius;
			Vector3 p1 = c + (x * Mathf.Cos(a1) + y * Mathf.Sin(a1)) * radius;
			var uc = new Vector2(0.5f, 0.5f);
			var u0 = new Vector2(0.5f + 0.5f * Mathf.Cos(a0 - angle0), 0.5f - 0.5f * Mathf.Sin(a0 - angle0));
			var u1 = new Vector2(0.5f + 0.5f * Mathf.Cos(a1 - angle0), 0.5f - 0.5f * Mathf.Sin(a1 - angle0));
			k.Tri(c, p0, p1, normal, uc, u0, u1);
			if (doubleSided) k.Tri(c, p0, p1, -normal, uc, u0, u1);
		}
	}

	/// <summary>Torus around <paramref name="axis"/> (ring radius R, tube radius r).</summary>
	public static void Torus(MeshKit k, Vector3 c, Vector3 axis, float R, float r, int segs, int sides)
	{
		axis = axis.Normalized();
		Vector3 refv = Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : Vector3.Up;
		Vector3 x = (refv - axis * refv.Dot(axis)).Normalized();
		Vector3 y = axis.Cross(x);
		Vector3 P(int s, int t, out Vector3 n)
		{
			float a = Mathf.Tau * s / segs, b = Mathf.Tau * t / sides;
			Vector3 dir = x * Mathf.Cos(a) + y * Mathf.Sin(a);
			n = dir * Mathf.Cos(b) + axis * Mathf.Sin(b);
			return c + dir * R + n * r;
		}
		for (int s = 0; s < segs; s++)
			for (int t = 0; t < sides; t++)
			{
				Vector3 a = P(s, t, out var na), b = P(s + 1, t, out var nb), cc = P(s + 1, t + 1, out var nc), d = P(s, t + 1, out var nd);
				k.Tri(a, b, cc, na, nb, nc, Vector2.Zero, new Vector2(1, 0), new Vector2(1, 1));
				k.Tri(a, cc, d, na, nc, nd, Vector2.Zero, new Vector2(1, 1), new Vector2(0, 1));
			}
	}

	/// <summary>Flat quad with its normal forced to face away from <paramref name="inside"/>.</summary>
	public static void QuadOut(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, float uv = 10f)
	{
		Vector3 n = (b - a).Cross(d - a);
		if (n.LengthSquared() < 1e-12f) n = (c - b).Cross(a - b);
		n = n.Normalized();
		if (n.Dot((a + b + c + d) * 0.25f - inside) < 0) n = -n;
		k.Quad(a, b, c, d, n, new Vector2(0, 0), new Vector2(a.DistanceTo(b) * uv, 0),
			new Vector2(a.DistanceTo(b) * uv, b.DistanceTo(c) * uv), new Vector2(0, b.DistanceTo(c) * uv));
	}

	/// <summary>A flat band along a path (a strap, a loose bandage end), facing roughly <paramref name="up"/>.</summary>
	public static void Ribbon(MeshKit k, IList<Vector3> path, float width, Vector3 up, bool doubleSided = true, float vScale = 8f)
	{
		float v = 0f;
		for (int i = 0; i < path.Count - 1; i++)
		{
			Vector3 a = path[i], b = path[i + 1];
			Vector3 dA = (path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)]).Normalized();
			Vector3 dB = (path[Math.Min(i + 2, path.Count - 1)] - path[i]).Normalized();
			Vector3 sA = dA.Cross(up).Normalized() * width * 0.5f, sB = dB.Cross(up).Normalized() * width * 0.5f;
			Vector3 n = (b - a).Cross(sA).Normalized();
			if (n.Dot(up) < 0) n = -n;
			float v1 = v + a.DistanceTo(b) * vScale;
			var ua = new Vector2(0, v); var ub = new Vector2(1, v); var uc = new Vector2(1, v1); var ud = new Vector2(0, v1);
			if (doubleSided) k.Card(a - sA, a + sA, b + sB, b - sB, n, ua, ub, uc, ud);
			else k.Quad(a - sA, a + sA, b + sB, b - sB, n, ua, ub, uc, ud);
			v = v1;
		}
	}

	public static int CountTriangles(Mesh mesh)
	{
		if (mesh == null) return 0;
		int t = 0;
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
			t += ((int[])mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index]).Length / 3;
		return t;
	}

	private static Vector2 V(float x, float y) => new(x, y);

	// ───────────────────────────── items ─────────────────────────────

	/// <summary>Builds the item under <paramref name="parent"/> (pickup space) and returns where to aim at it.</summary>
	public static Built Build(ToolKind kind, Node3D parent, int seed = 0)
	{
		var k = new MeshKit();
		var b = new Built { PickCenter = new Vector3(0, 0.08f, 0), PickRadius = 0.3f };
		switch (kind)
		{
			case ToolKind.Axe: Axe(k, ref b); break;
			case ToolKind.Hammer: Hammer(k, ref b); break;
			case ToolKind.Key: Key(k, ref b); break;
			case ToolKind.Camera: Camera(k, ref b); break;
			case ToolKind.Compass: Compass(k, ref b); break;
			case ToolKind.Lantern: Lantern(k, parent, ref b); break;
			case ToolKind.NewelPost: NewelPost(k, ref b); break;
			case ToolKind.Radio: Walkie(k, parent, ref b); break;
		}
		if (!k.IsEmpty)
		{
			var mi = k.CommitTo(parent, "Mesh");
			b.Triangles = CountTriangles(mi.Mesh);
		}
		return b;
	}

	/// <summary>Scenery the item rests on, which stays when the item is taken. Returns false if the item has none.</summary>
	public static bool BuildRest(ToolKind kind, Node3D parent, int seed = 0)
	{
		var k = new MeshKit();
		switch (kind)
		{
			case ToolKind.Axe: ChoppingStump(k); break;
			case ToolKind.Key: FlatStone(k, seed); break;
			default: return false;
		}
		k.CommitTo(parent, "RestMesh");
		return true;
	}

	/// <summary>Collision for the rest (added after the pickup has snapped, so it can't catch its own snap ray).</summary>
	public static void AddRestCollision(ToolKind kind, Node3D parent)
	{
		if (kind != ToolKind.Axe) return;
		var body = new StaticBody3D { Name = "RestBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, StumpTop * 0.5f, 0), Shape = new CylinderShape3D { Radius = 0.22f, Height = StumpTop } });
		parent.AddChild(body);
	}

	private const float StumpTop = 0.42f;

	private static void ChoppingStump(MeshKit k)
	{
		k.Color = new Color(0.72f, 0.66f, 0.6f);
		k.Mat(ProcTextures.BarkMat);
		Loft(k, new[]
		{
			new Ring(new Vector3(0, -0.1f, 0), 0.29f, 0.27f),
			new Ring(new Vector3(0, 0.03f, 0), 0.25f, 0.24f),
			new Ring(new Vector3(0, 0.12f, 0), 0.215f, 0.21f),
			new Ring(new Vector3(0, StumpTop, 0), 0.205f, 0.2f),
		}, 9, false, false, Vector3.Right, 2.5f);
		// Fresh-cut top: pale end grain, the brightest thing in this part of the woods.
		k.Color = new Color(0.95f, 0.85f, 0.68f);
		k.Mat(ProcTextures.EndGrainMat);
		Disc(k, new Vector3(0, StumpTop, 0), Vector3.Up, 0.2f, 9);
		// A chop scar and a split-off chip lying at the foot.
		k.Color = new Color(0.9f, 0.78f, 0.6f);
		k.Box(new Vector3(0.26f, 0.015f, 0.12f), new Vector3(0.09f, 0.025f, 0.04f), 6f, Basis.FromEuler(new Vector3(0, 0.6f, 0.2f)));
	}

	private static void FlatStone(MeshKit k, int seed)
	{
		k.Color = new Color(0.86f, 0.86f, 0.82f);
		k.Mat(ProcTextures.RockMat);
		k.Blob(new Vector3(0, -0.012f, 0), new Vector3(0.26f, 0.082f, 0.2f), 8317 + seed, 0.06f, true, 3f, 0.35f);
	}

	private static void Axe(MeshKit k, ref Built b)
	{
		// Axe space: handle up +Y from the butt, head at the top, blade along +X.
		var tilt = new Basis(Vector3.Back, Mathf.DegToRad(-55f));
		var yaw = new Basis(Vector3.Up, 0.35f);
		var basis = yaw * tilt;
		Vector3 edgeMid = new(0.15f, 0.672f, 0f);
		var xf = new Transform3D(basis, Vector3.Zero);
		xf.Origin = new Vector3(0.02f, StumpTop - 0.035f, 0) - xf * edgeMid;
		k.Xf = xf;

		// Handle: oval, swelling to a knob at the butt, ash-pale so it reads against the leaves.
		k.Color = new Color(0.78f, 0.66f, 0.5f);
		k.Mat(ItemTextures.AshMat);
		Lathe(k, new[] { V(0.019f, 0f), V(0.024f, 0.025f), V(0.017f, 0.07f), V(0.015f, 0.35f), V(0.017f, 0.6f), V(0.018f, 0.7f), V(0.016f, 0.74f) },
			6, true, true, 1f, 0.72f, 3f);

		// Head: cross-sections along X (x, yBottom, yTop, half-thickness), poll to edge.
		float hy = 0.68f;
		var secs = new (float x, float y0, float y1, float t)[]
		{
			(-0.05f, -0.04f, 0.04f, 0.021f),
			(0.025f, -0.048f, 0.048f, 0.022f),
			(0.1f, -0.062f, 0.052f, 0.008f),
			(0.13f, -0.078f, 0.064f, 0.0035f),
			(0.152f, -0.092f, 0.074f, 0.0006f),
		};
		Vector3[] Corners(int i)
		{
			var s = secs[i];
			return new[]
			{
				new Vector3(s.x, hy + s.y1, s.t), new Vector3(s.x, hy + s.y1, -s.t),
				new Vector3(s.x, hy + s.y0, -s.t), new Vector3(s.x, hy + s.y0, s.t),
			};
		}
		for (int i = 0; i < secs.Length - 1; i++)
		{
			bool edge = i == secs.Length - 2;
			k.Color = edge ? Colors.White : new Color(0.55f, 0.56f, 0.58f);
			k.Mat(edge ? ItemTextures.EdgeMat : ItemTextures.SteelMat);
			var a = Corners(i); var c = Corners(i + 1);
			Vector3 inside = new((secs[i].x + secs[i + 1].x) * 0.5f, hy - 0.005f, 0);
			for (int e = 0; e < 4; e++)
			{
				int f = (e + 1) % 4;
				QuadOut(k, a[e], a[f], c[f], c[e], inside);
			}
		}
		k.Color = new Color(0.4f, 0.41f, 0.43f);
		k.Mat(ItemTextures.SteelMat);
		var p0 = Corners(0);
		QuadOut(k, p0[0], p0[1], p0[2], p0[3], new Vector3(0, hy, 0));
		k.Xf = Transform3D.Identity;
		b.PickCenter = xf * new Vector3(0.03f, 0.45f, 0f);
		b.PickRadius = 0.42f;
	}

	private static void Hammer(MeshKit k, ref Built b)
	{
		// Hammer space: handle up +Y, face toward +Z, claw toward -Z; then laid on its side.
		var lay = new Basis(Vector3.Back, Mathf.DegToRad(-90f));
		var yaw = new Basis(Vector3.Up, -0.4f);
		var xf = new Transform3D(yaw * lay, Vector3.Zero);
		xf.Origin = new Vector3(-0.16f, 0.017f, 0.02f);
		k.Xf = xf;

		k.Color = new Color(0.86f, 0.74f, 0.56f);
		k.Mat(ItemTextures.AshMat);
		Lathe(k, new[] { V(0.015f, 0f), V(0.017f, 0.02f), V(0.014f, 0.08f), V(0.012f, 0.2f), V(0.0135f, 0.27f), V(0.012f, 0.31f) },
			6, true, true, 1f, 1.3f, 3f);

		float hy = 0.305f;
		k.Color = new Color(0.46f, 0.47f, 0.5f);
		k.Mat(ItemTextures.SteelMat);
		k.Box(new Vector3(0, hy, 0), new Vector3(0.026f, 0.034f, 0.042f), 12f);
		// neck and striking face
		k.Cylinder(new Vector3(0, hy, 0.02f), new Vector3(0, hy, 0.052f), 0.012f, 0.0145f, 8, false, 6f);
		k.Color = Colors.White;
		k.Mat(ItemTextures.EdgeMat);
		k.Cylinder(new Vector3(0, hy, 0.052f), new Vector3(0, hy, 0.06f), 0.0155f, 0.0145f, 8, true, 6f);
		// claw: two prongs curving back toward the handle
		k.Color = new Color(0.42f, 0.43f, 0.46f);
		k.Mat(ItemTextures.SteelMat);
		foreach (float s in new[] { -1f, 1f })
		{
			float x = s * 0.0065f;
			var pts = new[] { new Vector3(x, hy + 0.005f, -0.02f), new Vector3(x, hy + 0.002f, -0.048f), new Vector3(x, hy - 0.012f, -0.072f), new Vector3(x, hy - 0.03f, -0.085f) };
			float[] r = { 0.009f, 0.008f, 0.006f, 0.003f };
			for (int i = 0; i < pts.Length - 1; i++)
				k.Cylinder(pts[i], pts[i + 1], r[i], r[i + 1], 4, false, 6f);
		}
		k.Xf = Transform3D.Identity;
		b.PickCenter = xf * new Vector3(0, 0.18f, 0f) + Vector3.Up * 0.03f;
		b.PickRadius = 0.28f;
	}

	private static void Key(MeshKit k, ref Built b)
	{
		// Lies flat on its stone, long axis along X, bow at -X.
		var xf = new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(0.01f, 0.083f, 0));
		k.Xf = xf;
		float t = 0.0035f;
		k.Color = new Color(0.95f, 0.8f, 0.5f);
		k.Mat(ItemTextures.BrassMat);
		// bow: a flat ring with a real hole
		const int segs = 8;
		Vector3 bc = new(-0.052f, 0, 0);
		float ro = 0.027f, ri = 0.015f;
		for (int i = 0; i < segs; i++)
		{
			float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
			Vector3 d0 = new(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new(Mathf.Cos(a1), 0, Mathf.Sin(a1));
			Vector3 o0 = bc + d0 * ro, o1 = bc + d1 * ro, i0 = bc + d0 * ri, i1 = bc + d1 * ri;
			Vector3 up = Vector3.Up * t, dn = Vector3.Down * t;
			k.Quad(i0 + up, o0 + up, o1 + up, i1 + up, Vector3.Up);
			k.Quad(i0 + dn, o0 + dn, o1 + dn, i1 + dn, Vector3.Down);
			k.Quad(o0 + dn, o1 + dn, o1 + up, o0 + up, (d0 + d1).Normalized());
			k.Quad(i0 + dn, i1 + dn, i1 + up, i0 + up, -(d0 + d1).Normalized());
		}
		// collar, shaft, bit with two teeth
		k.Box(new Vector3(-0.021f, 0, 0), new Vector3(0.01f, 0.011f, 0.013f), 20f);
		k.Cylinder(new Vector3(-0.026f, 0, 0), new Vector3(0.078f, 0, 0), 0.0048f, 0.0045f, 6, true, 20f);
		k.Box(new Vector3(0.062f, 0, 0.013f), new Vector3(0.026f, 0.007f, 0.02f), 20f);
		k.Box(new Vector3(0.054f, 0, 0.027f), new Vector3(0.007f, 0.007f, 0.01f), 20f);
		k.Box(new Vector3(0.069f, 0, 0.027f), new Vector3(0.007f, 0.007f, 0.01f), 20f);
		// split ring and a faded paper tag: the tag is what the eye catches from the trail
		k.Color = new Color(0.7f, 0.72f, 0.74f);
		k.Mat(ItemTextures.ChromeMat);
		Torus(k, new Vector3(-0.083f, 0.001f, 0.004f), Vector3.Up, 0.02f, 0.0022f, 8, 3);
		k.Color = new Color(0.86f, 0.62f, 0.34f);
		k.Mat(ProcTextures.PaperMat);
		k.Box(new Vector3(-0.135f, -0.001f, 0.02f), new Vector3(0.07f, 0.003f, 0.04f), 12f, new Basis(Vector3.Up, -0.35f));
		k.Xf = Transform3D.Identity;
		b.PickCenter = new Vector3(0, 0.1f, 0);
		b.PickRadius = 0.3f;
	}

	private static void Camera(MeshKit k, ref Built b)
	{
		var xf = new Transform3D(new Basis(Vector3.Up, 0.7f), Vector3.Zero);
		k.Xf = xf;
		// body: leatherette wrap between chrome plates
		k.Color = new Color(0.2f, 0.2f, 0.21f);
		k.Mat(ItemTextures.LeatheretteMat);
		k.Box(new Vector3(0, 0.036f, 0), new Vector3(0.13f, 0.062f, 0.04f), 20f);
		k.Color = new Color(0.82f, 0.82f, 0.84f);
		k.Mat(ItemTextures.ChromeMat);
		k.Box(new Vector3(0, 0.076f, 0), new Vector3(0.132f, 0.018f, 0.042f), 20f);
		k.Box(new Vector3(0, 0.0025f, 0), new Vector3(0.132f, 0.005f, 0.042f), 20f);
		// viewfinder hump
		k.Box(new Vector3(-0.02f, 0.091f, -0.002f), new Vector3(0.05f, 0.014f, 0.032f), 20f);
		// shutter button and rewind knob
		k.Cylinder(new Vector3(0.042f, 0.085f, 0.004f), new Vector3(0.042f, 0.092f, 0.004f), 0.0065f, 0.006f, 6, true, 20f);
		k.Cylinder(new Vector3(-0.05f, 0.085f, 0f), new Vector3(-0.05f, 0.094f, 0f), 0.01f, 0.01f, 6, true, 20f);
		// lens: black barrel, chrome focus ring, dark glass
		k.Color = new Color(0.14f, 0.14f, 0.15f);
		k.Mat(ItemTextures.PlasticMat);
		k.Cylinder(new Vector3(0.01f, 0.038f, 0.02f), new Vector3(0.01f, 0.038f, 0.052f), 0.027f, 0.024f, 10, false, 20f);
		k.Color = new Color(0.8f, 0.8f, 0.82f);
		k.Mat(ItemTextures.ChromeMat);
		k.Cylinder(new Vector3(0.01f, 0.038f, 0.028f), new Vector3(0.01f, 0.038f, 0.036f), 0.0285f, 0.0285f, 10, false, 20f);
		k.Cylinder(new Vector3(0.01f, 0.038f, 0.05f), new Vector3(0.01f, 0.038f, 0.054f), 0.025f, 0.024f, 10, false, 20f);
		k.Color = Colors.White;
		k.Mat(ItemTextures.LensMat);
		Disc(k, new Vector3(0.01f, 0.038f, 0.053f), Vector3.Back, 0.021f, 10);
		// viewfinder window
		k.Color = new Color(0.6f, 0.65f, 0.7f);
		k.Mat(ItemTextures.GlassMat);
		k.Box(new Vector3(-0.045f, 0.074f, 0.0215f), new Vector3(0.016f, 0.01f, 0.002f), 20f);
		// strap: lugs, then a tan leather loop lying in the leaves behind it
		k.Color = new Color(0.8f, 0.8f, 0.82f);
		k.Mat(ItemTextures.ChromeMat);
		k.Box(new Vector3(-0.067f, 0.07f, 0), new Vector3(0.006f, 0.01f, 0.008f), 20f);
		k.Box(new Vector3(0.067f, 0.07f, 0), new Vector3(0.006f, 0.01f, 0.008f), 20f);
		k.Color = new Color(0.72f, 0.52f, 0.32f);
		k.Mat(ItemTextures.StrapMat);
		Ribbon(k, new[]
		{
			new Vector3(-0.07f, 0.068f, 0), new Vector3(-0.09f, 0.03f, -0.01f), new Vector3(-0.11f, 0.004f, -0.04f),
			new Vector3(-0.12f, 0.004f, -0.1f), new Vector3(-0.06f, 0.004f, -0.17f), new Vector3(0.03f, 0.004f, -0.19f),
			new Vector3(0.12f, 0.004f, -0.14f), new Vector3(0.13f, 0.004f, -0.06f), new Vector3(0.1f, 0.03f, -0.01f),
			new Vector3(0.07f, 0.068f, 0),
		}, 0.018f, Vector3.Up, true, 6f);
		k.Xf = Transform3D.Identity;
		b.PickCenter = xf * new Vector3(0, 0.05f, -0.04f);
		b.PickRadius = 0.26f;
	}

	private static void Compass(MeshKit k, ref Built b)
	{
		var xf = new Transform3D(new Basis(Vector3.Up, -0.5f), Vector3.Zero);
		k.Xf = xf;
		k.Color = new Color(0.95f, 0.78f, 0.48f);
		k.Mat(ItemTextures.BrassMat);
		Lathe(k, new[] { V(0.044f, 0f), V(0.05f, 0.005f), V(0.05f, 0.017f), V(0.046f, 0.021f), V(0.041f, 0.021f), V(0.041f, 0.012f) },
			12, true, false, 1f, 1f, 8f);
		// hinged lid standing open at the back, a polished inner face
		var lidRot = new Basis(Vector3.Right, Mathf.DegToRad(-105f));
		Vector3 hinge = new(0, 0.019f, -0.05f);
		Vector3 L(Vector3 p) => hinge + lidRot * p;
		k.Cylinder(L(new Vector3(0, 0, 0.05f)), L(new Vector3(0, 0.006f, 0.05f)), 0.05f, 0.048f, 12, true, 8f);
		k.Color = Colors.White;
		k.Mat(ItemTextures.EdgeMat);
		Disc(k, L(new Vector3(0, -0.0008f, 0.05f)), (lidRot * Vector3.Down).Normalized(), 0.04f, 10);
		// hinge knuckle and the thumb ring at the front
		k.Color = new Color(0.9f, 0.74f, 0.45f);
		k.Mat(ItemTextures.BrassMat);
		k.Cylinder(new Vector3(-0.012f, 0.019f, -0.05f), new Vector3(0.012f, 0.019f, -0.05f), 0.004f, 0.004f, 5, true, 8f);
		Torus(k, new Vector3(0, 0.012f, 0.061f), Vector3.Up, 0.011f, 0.0022f, 6, 3);
		// dial card, needle, glass
		k.Color = Colors.White;
		k.Mat(ItemTextures.DialMat);
		Disc(k, new Vector3(0, 0.0125f, 0), Vector3.Up, 0.041f, 12);
		k.Color = new Color(0.72f, 0.14f, 0.1f);
		k.Mat(ItemTextures.Tint("needle_red", Colors.White, 0.5f, 0.5f));
		float ny = 0.0145f;
		k.Tri(new Vector3(-0.004f, ny, 0), new Vector3(0.004f, ny, 0), new Vector3(0.006f, ny, -0.033f), Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.One);
		k.Color = new Color(0.85f, 0.85f, 0.82f);
		k.Tri(new Vector3(-0.004f, ny, 0), new Vector3(0.004f, ny, 0), new Vector3(-0.006f, ny, 0.033f), Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.One);
		k.Color = Colors.White;
		k.Mat(ItemTextures.GlassMat);
		Disc(k, new Vector3(0, 0.0185f, 0), Vector3.Up, 0.0415f, 12);
		k.Xf = Transform3D.Identity;
		b.PickCenter = new Vector3(0, 0.035f, -0.01f);
		b.PickRadius = 0.22f;
	}

	private static void Lantern(MeshKit k, Node3D parent, ref Built b)
	{
		// Hurricane lantern: painted font, wire guards, glass globe, vented cap, bail.
		k.Color = new Color(0.42f, 0.08f, 0.05f);
		k.Mat(ItemTextures.PaintMat);
		Lathe(k, new[] { V(0.07f, 0f), V(0.078f, 0.01f), V(0.078f, 0.045f), V(0.06f, 0.06f), V(0.034f, 0.066f) }, 8, true, false, 1f, 1f, 6f);
		k.Color = new Color(0.9f, 0.76f, 0.46f);
		k.Mat(ItemTextures.BrassMat);
		Lathe(k, new[] { V(0.032f, 0.064f), V(0.03f, 0.08f) }, 8, false, false);
		// wire guards
		k.Color = new Color(0.4f, 0.4f, 0.42f);
		k.Mat(ItemTextures.SteelMat);
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Tau * (i + 0.5f) / 4f;
			Vector3 d = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 p0 = d * 0.066f + Vector3.Up * 0.05f, p1 = d * 0.074f + Vector3.Up * 0.14f, p2 = d * 0.064f + Vector3.Up * 0.222f;
			k.Cylinder(p0, p1, 0.0035f, 0.0035f, 4, false);
			k.Cylinder(p1, p2, 0.0035f, 0.0035f, 4, false);
		}
		// globe (lit from inside)
		k.Color = Colors.White;
		k.Mat(ItemTextures.LampGlassMat);
		Lathe(k, new[] { V(0.028f, 0.078f), V(0.05f, 0.108f), V(0.058f, 0.15f), V(0.05f, 0.19f), V(0.03f, 0.216f) }, 8, false, false);
		// cap with a chimney
		k.Color = new Color(0.4f, 0.075f, 0.05f);
		k.Mat(ItemTextures.PaintMat);
		Lathe(k, new[] { V(0.064f, 0.214f), V(0.068f, 0.222f), V(0.045f, 0.246f), V(0.021f, 0.26f), V(0.021f, 0.278f) }, 8, true, true, 1f, 1f, 6f);
		// bail handle
		k.Color = new Color(0.38f, 0.38f, 0.4f);
		k.Mat(ItemTextures.SteelMat);
		Vector3 prev = default;
		for (int i = 0; i <= 6; i++)
		{
			float a = Mathf.Pi * i / 6f;
			Vector3 p = new(Mathf.Cos(a) * 0.07f, 0.232f + Mathf.Sin(a) * 0.1f, 0);
			if (i > 0) k.Cylinder(prev, p, 0.003f, 0.003f, 4, false);
			prev = p;
		}
		// flame
		k.Color = Colors.White;
		k.Mat(ItemTextures.FlameMat);
		k.Cylinder(new Vector3(0, 0.1f, 0), new Vector3(0, 0.086f, 0), 0.008f, 0f, 5, true);
		k.Cylinder(new Vector3(0, 0.1f, 0), new Vector3(0, 0.132f, 0), 0.008f, 0f, 5, true);

		b.Glow = new OmniLight3D
		{
			Name = "Glow",
			Position = new Vector3(0, 0.13f, 0),
			LightColor = new Color(1f, 0.62f, 0.3f),
			LightEnergy = 0.55f,
			OmniRange = 1.7f,
			OmniAttenuation = 1.4f,
			ShadowEnabled = false,
		};
		parent.AddChild(b.Glow);
		b.PickCenter = new Vector3(0, 0.16f, 0);
		b.PickRadius = 0.26f;
	}

	private static void NewelPost(MeshKit k, ref Built b)
	{
		// The newel post bulb (STORY.md, Act 5): the turned top of a staircase newel post,
		// wrenched off. Stands upright on the table: a square base block showing a pale
		// splintered break, a turned collar and neck, the big bulb, and a small cap. ~0.28 m.
		var xf = new Transform3D(new Basis(Vector3.Up, 0.3f), Vector3.Zero);
		k.Xf = xf;
		k.Color = new Color(0.56f, 0.38f, 0.23f);
		k.Mat(ItemTextures.TurnedWoodMat);
		k.Box(new Vector3(0, 0.035f, 0), new Vector3(0.085f, 0.07f, 0.085f), 6f);
		k.Box(new Vector3(0, 0.075f, 0), new Vector3(0.095f, 0.012f, 0.095f), 6f);
		Lathe(k, new[]
		{
			V(0.036f, 0.081f), V(0.042f, 0.09f), V(0.032f, 0.102f), V(0.022f, 0.118f), V(0.028f, 0.13f),
			V(0.05f, 0.148f), V(0.066f, 0.178f), V(0.066f, 0.2f), V(0.052f, 0.228f), V(0.03f, 0.245f),
			V(0.022f, 0.252f), V(0.03f, 0.262f), V(0.024f, 0.275f), V(0.008f, 0.283f),
		}, 10, false, true, 1f, 1f, 5f);
		// splintered break down one face of the base block, where it was torn from the post
		k.Color = new Color(0.95f, 0.85f, 0.66f);
		k.Mat(ProcTextures.EndGrainMat);
		k.Box(new Vector3(0.012f, 0.02f, 0.0435f), new Vector3(0.05f, 0.03f, 0.002f), 20f, new Basis(Vector3.Back, 0.25f));
		k.Xf = Transform3D.Identity;
		b.PickCenter = new Vector3(0, 0.15f, 0);
		b.PickRadius = 0.3f;
	}

	private static void Walkie(MeshKit k, Node3D parent, ref Built b)
	{
		// Lies on its back, face up (+Y), antenna end toward +Z.
		var xf = new Transform3D(new Basis(Vector3.Up, 0.6f), Vector3.Zero);
		k.Xf = xf;
		float w = 0.033f, h = 0.019f, c = 0.007f, yC = 0.019f;
		var prof = new[] { V(w - c, h), V(w, h - c), V(w, -h + c), V(w - c, -h), V(-w + c, -h), V(-w, -h + c), V(-w, h - c), V(-w + c, h) };
		float z0 = -0.085f, z1 = 0.075f;
		k.Color = new Color(0.19f, 0.2f, 0.19f);
		k.Mat(ItemTextures.PlasticMat);
		Vector3 P(int i, float z) => new(prof[i].X, yC + prof[i].Y, z);
		Vector3 ctr = new(0, yC, (z0 + z1) * 0.5f);
		for (int i = 0; i < prof.Length; i++)
		{
			int j = (i + 1) % prof.Length;
			QuadOut(k, P(i, z0), P(j, z0), P(j, z1), P(i, z1), ctr, 20f);
		}
		for (int i = 1; i < prof.Length - 1; i++)
		{
			k.Tri(P(0, z0), P(i, z0), P(i + 1, z0), Vector3.Forward, Vector2.Zero, Vector2.Right, Vector2.One);
			k.Tri(P(0, z1), P(i, z1), P(i + 1, z1), Vector3.Back, Vector2.Zero, Vector2.Right, Vector2.One);
		}
		// speaker grille and a pale channel sticker on the face
		float top = yC + h + 0.0006f;
		k.Color = Colors.White;
		k.Mat(ItemTextures.GrilleMat);
		k.Quad(new Vector3(-0.024f, top, 0.0f), new Vector3(0.024f, top, 0.0f), new Vector3(0.024f, top, 0.06f), new Vector3(-0.024f, top, 0.06f), Vector3.Up,
			new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		k.Color = new Color(0.78f, 0.75f, 0.64f);
		k.Mat(ProcTextures.PaperMat);
		k.Box(new Vector3(0, top, -0.045f), new Vector3(0.044f, 0.0012f, 0.05f), 12f);
		// rubber stub antenna, two knobs, push-to-talk
		k.Color = new Color(0.1f, 0.1f, 0.1f);
		k.Mat(ItemTextures.PlasticMat);
		k.Cylinder(new Vector3(0.016f, yC, z1), new Vector3(0.017f, yC + 0.002f, z1 + 0.13f), 0.0075f, 0.005f, 6, true, 20f);
		k.Cylinder(new Vector3(-0.014f, yC + 0.004f, z1), new Vector3(-0.014f, yC + 0.004f, z1 + 0.013f), 0.0075f, 0.0075f, 8, true, 20f);
		k.Cylinder(new Vector3(-0.004f, yC + 0.004f, z1), new Vector3(-0.004f, yC + 0.004f, z1 + 0.008f), 0.005f, 0.005f, 6, true, 20f);
		k.Box(new Vector3(-w - 0.002f, yC, 0.01f), new Vector3(0.005f, 0.018f, 0.04f), 20f);
		// the LED: its own material so it can pulse
		b.Led = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.6f, 0.05f, 0.04f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.08f, 0.04f),
			EmissionEnergyMultiplier = 2f,
			Roughness = 0.3f,
		};
		k.Color = Colors.White;
		k.Mat(b.Led);
		k.Box(new Vector3(0.018f, top + 0.001f, 0.066f), new Vector3(0.006f, 0.003f, 0.006f), 20f);
		k.Xf = Transform3D.Identity;

		b.Glow = new OmniLight3D
		{
			Name = "LedGlow",
			Position = xf * new Vector3(0.018f, top + 0.02f, 0.066f),
			LightColor = new Color(1f, 0.1f, 0.05f),
			LightEnergy = 0.3f,
			OmniRange = 0.7f,
			ShadowEnabled = false,
		};
		parent.AddChild(b.Glow);
		b.PickCenter = new Vector3(0, 0.03f, 0);
		b.PickRadius = 0.26f;
	}
}
