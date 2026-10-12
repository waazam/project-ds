using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-12, the model overhaul) One smooth tube along a curve: rings round a Catmull-Rom spline through the given
/// points, each ring its own size (round, or an ellipse), the frame carried along the curve without twisting, the normals
/// smooth all the way. For bodies and limbs that must read as one piece of flesh (a figure made of separate cylinders
/// read as blocks, creased and seamed at every joint).
/// </summary>
public static class Loft
{
	/// <summary>A tube through <paramref name="pts"/>, its half-width and half-depth at each point (<paramref name="size"/>:
	/// x across, y front to back, in the frame of <paramref name="side"/>), <paramref name="sides"/> round, each span split
	/// into <paramref name="per"/> rings. Ends closed with a rounded cap when <paramref name="caps"/>.</summary>
	public static void Tube(MeshKit k, IList<Vector3> pts, IList<Vector2> size, int sides = 12, int per = 4, bool caps = true, Vector3? side = null)
	{
		if (pts.Count < 2) return;
		var c = new List<Vector3>();
		var r = new List<Vector2>();
		for (int i = 0; i < pts.Count - 1; i++)
		{
			Vector3 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
			for (int s = 0; s < per; s++)
			{
				float t = s / (float)per;
				c.Add(CatmullRom(p0, p1, p2, p3, t));
				float e = t * t * (3f - 2f * t);
				r.Add(size[i].Lerp(size[i + 1], e));
			}
		}
		c.Add(pts[^1]);
		r.Add(size[^1]);
		int n = c.Count;
		// the frame: parallel-transported along the curve (no twist), its first "side" from the hint
		var tan = new Vector3[n];
		for (int i = 0; i < n; i++) tan[i] = (c[Mathf.Min(n - 1, i + 1)] - c[Mathf.Max(0, i - 1)]).Normalized();
		Vector3 hint = side ?? Vector3.Right;
		if (Mathf.Abs(hint.Dot(tan[0])) > 0.95f) hint = Mathf.Abs(tan[0].Y) < 0.9f ? Vector3.Up : Vector3.Forward;
		var nx = new Vector3[n];
		nx[0] = (hint - tan[0] * hint.Dot(tan[0])).Normalized();
		for (int i = 1; i < n; i++)
		{
			Vector3 v = nx[i - 1] - tan[i] * nx[i - 1].Dot(tan[i]);
			nx[i] = v.LengthSquared() > 1e-8f ? v.Normalized() : nx[i - 1];
		}
		var ring = new Vector3[n, sides + 1];
		var nrm = new Vector3[n, sides + 1];
		float len = 0f;
		var vCoord = new float[n];
		for (int i = 0; i < n; i++)
		{
			if (i > 0) len += c[i].DistanceTo(c[i - 1]);
			vCoord[i] = len;
			Vector3 ax = nx[i], ay = tan[i].Cross(ax).Normalized();
			for (int j = 0; j <= sides; j++)
			{
				float a = j / (float)sides * Mathf.Tau;
				float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
				ring[i, j] = c[i] + ax * ca * r[i].X + ay * sa * r[i].Y;
				// (the normal of an ellipse: scaled by the other axis)
				nrm[i, j] = (ax * ca * r[i].Y + ay * sa * r[i].X).Normalized();
			}
		}
		for (int i = 0; i < n - 1; i++)
			for (int j = 0; j < sides; j++)
			{
				var uv00 = new Vector2(j / (float)sides, vCoord[i]); var uv01 = new Vector2((j + 1) / (float)sides, vCoord[i]);
				var uv10 = new Vector2(j / (float)sides, vCoord[i + 1]); var uv11 = new Vector2((j + 1) / (float)sides, vCoord[i + 1]);
				k.Tri(ring[i, j], ring[i + 1, j], ring[i + 1, j + 1], nrm[i, j], nrm[i + 1, j], nrm[i + 1, j + 1], uv00, uv10, uv11);
				k.Tri(ring[i, j], ring[i + 1, j + 1], ring[i, j + 1], nrm[i, j], nrm[i + 1, j + 1], nrm[i, j + 1], uv00, uv11, uv01);
			}
		if (!caps) return;
		Cap(k, c[0], -tan[0], ring, nrm, 0, sides, r[0], true);
		Cap(k, c[n - 1], tan[n - 1], ring, nrm, n - 1, sides, r[n - 1], false);
	}

	/// <summary>A round tube (the same size across and front to back).</summary>
	public static void Tube(MeshKit k, IList<Vector3> pts, IList<float> radius, int sides = 12, int per = 4, bool caps = true)
	{
		var s = new Vector2[radius.Count];
		for (int i = 0; i < s.Length; i++) s[i] = new Vector2(radius[i], radius[i]);
		Tube(k, pts, s, sides, per, caps);
	}

	private static void Cap(MeshKit k, Vector3 centre, Vector3 outDir, Vector3[,] ring, Vector3[,] nrm, int i, int sides, Vector2 r, bool start)
	{
		// a low dome, so an end is rounded, not cut off flat
		Vector3 tip = centre + outDir * Mathf.Min(r.X, r.Y) * 0.6f;
		for (int j = 0; j < sides; j++)
		{
			Vector3 a = ring[i, j], b = ring[i, j + 1];
			Vector3 na = (nrm[i, j] + outDir).Normalized(), nb = (nrm[i, j + 1] + outDir).Normalized();
			if (start) k.Tri(a, b, tip, na, nb, outDir, Vector2.Zero, Vector2.Right, new Vector2(0.5f, 0.5f));
			else k.Tri(b, a, tip, nb, na, outDir, Vector2.Zero, Vector2.Right, new Vector2(0.5f, 0.5f));
		}
	}

	public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
	{
		float t2 = t * t, t3 = t2 * t;
		return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}
}
