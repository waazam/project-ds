using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-15, the props pass: "make sure they're proportioned and not too blocky looking") Turned and bent shapes for
/// the props built in code: a lamp's base, a candlestick, a chair's leg, a bottle, a pail, a shade; a lamp's harp, a
/// chain. <see cref="Lathe"/> turns a profile round the local Y axis like <see cref="ItemMeshes.Lathe"/>, but takes any
/// profile (flat steps and overhangs too: a stepped base, a drip pan, a lip) and keeps its corners crisp: round about
/// the axis, its profile's sharp turns (over <c>crease</c> degrees) are drawn as hard edges, its gentle ones smooth, so
/// a turned knop reads as round and a step as a step at 640x360. 8-16 sides suit everything a hand's size or more.
/// </summary>
public static class TurnedKit
{
	/// <summary>Turns <paramref name="profile"/> (x the radius, y the height, in order from the bottom's middle round to
	/// the top) round local +Y, placed by <paramref name="xf"/>. A profile starting or ending at radius 0 closes itself
	/// (no caps needed). <paramref name="sx"/>/<paramref name="sz"/> squash it into an oval; <paramref name="a0"/> the
	/// first facet's angle. UVs: u round (metres), v along the profile (metres), both times <paramref name="uv"/>.</summary>
	public static void Lathe(MeshKit k, Transform3D xf, IList<Vector2> profile, int sides, float crease = 38f,
		float sx = 1f, float sz = 1f, float uv = 1f, float a0 = 0f)
	{
		int n = profile.Count;
		if (n < 2 || sides < 3) return;
		// each segment's outward normal in (radial, up)
		var segN = new Vector2[n - 1];
		var segOk = new bool[n - 1];
		for (int j = 0; j < n - 1; j++)
		{
			Vector2 d = profile[j + 1] - profile[j];
			segOk[j] = d.LengthSquared() > 1e-12f;
			segN[j] = segOk[j] ? new Vector2(d.Y, -d.X).Normalized() : Vector2.Up;
		}
		float cosCrease = Mathf.Cos(Mathf.DegToRad(crease));
		Vector2 EndN(int j, bool start)
		{
			// the normal of segment j at its start (shared with j-1) or its end (shared with j+1), smoothed if the turn is gentle
			int o = start ? j - 1 : j + 1;
			while (o >= 0 && o < n - 1 && !segOk[o]) o += start ? -1 : 1;
			if (o < 0 || o >= n - 1) return segN[j];
			return segN[j].Dot(segN[o]) >= cosCrease ? (segN[j] + segN[o]).Normalized() : segN[j];
		}
		float vAcc = 0f;
		float rMax = 0f;
		foreach (var p in profile) rMax = Mathf.Max(rMax, p.X);
		float circ = Mathf.Tau * rMax * Mathf.Max(sx, sz);
		for (int j = 0; j < n - 1; j++)
		{
			if (!segOk[j]) continue;
			Vector2 p0 = profile[j], p1 = profile[j + 1];
			Vector2 n0 = EndN(j, true), n1 = EndN(j, false);
			float v0 = vAcc * uv, v1 = (vAcc + p0.DistanceTo(p1)) * uv;
			vAcc += p0.DistanceTo(p1);
			for (int i = 0; i < sides; i++)
			{
				float t0 = a0 + Mathf.Tau * i / sides, t1 = a0 + Mathf.Tau * (i + 1) / sides;
				Vector3 P(Vector2 p, float t) => xf * new Vector3(Mathf.Cos(t) * p.X * sx, p.Y, Mathf.Sin(t) * p.X * sz);
				Vector3 N(Vector2 nn, float t)
				{
					var rd = new Vector2(Mathf.Cos(t) / sx, Mathf.Sin(t) / sz).Normalized();
					return (xf.Basis * new Vector3(rd.X * nn.X, nn.Y, rd.Y * nn.X)).Normalized();
				}
				float u0 = (float)i / sides * circ * uv, u1 = (float)(i + 1) / sides * circ * uv;
				Vector3 a = P(p0, t0), b = P(p0, t1), c = P(p1, t1), d = P(p1, t0);
				Vector3 na = N(n0, t0), nb = N(n0, t1), nc = N(n1, t1), nd = N(n1, t0);
				k.Tri(a, b, c, na, nb, nc, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1));
				k.Tri(a, c, d, na, nc, nd, new Vector2(u0, v0), new Vector2(u1, v1), new Vector2(u0, v1));
			}
		}
	}

	/// <summary><see cref="Lathe"/> standing at <paramref name="at"/> (upright, turned by <paramref name="yaw"/>).</summary>
	public static void Lathe(MeshKit k, Vector3 at, IList<Vector2> profile, int sides, float crease = 38f, float sx = 1f, float sz = 1f, float yaw = 0f, float uv = 1f)
		=> Lathe(k, new Transform3D(new Basis(Vector3.Up, yaw), at), profile, sides, crease, sx, sz, uv);

	/// <summary>A round rod bent along <paramref name="path"/> (a lamp's harp, an arm, a handle, a pipe's bend), radius
	/// <paramref name="r"/>, its ends capped if asked.</summary>
	public static void Rod(MeshKit k, IList<Vector3> path, float r, int sides = 6, bool caps = true)
	{
		var rings = new List<ItemMeshes.Ring>(path.Count);
		foreach (var p in path) rings.Add(new ItemMeshes.Ring(p, r));
		Vector3 d = path[path.Count - 1] - path[0];
		Vector3 side = Mathf.Abs(d.Normalized().Dot(Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up;
		// a bent rod (a curve in a plane): the plane's normal is square to it all along, so its rings never twist
		if (path.Count > 2)
		{
			Vector3 pn = (path[path.Count / 2] - path[0]).Cross(path[path.Count - 1] - path[0]);
			if (pn.LengthSquared() > 1e-10f) side = pn.Normalized();
		}
		ItemMeshes.Loft(k, rings, sides, caps, caps, side, 1f);
	}

	/// <summary>Points along a quadratic curve from <paramref name="a"/> through the pull of <paramref name="c"/> to
	/// <paramref name="b"/> (for <see cref="Rod"/>).</summary>
	public static List<Vector3> Curve(Vector3 a, Vector3 c, Vector3 b, int steps = 8)
	{
		var l = new List<Vector3>(steps + 1);
		for (int i = 0; i <= steps; i++)
		{
			float t = i / (float)steps;
			l.Add(a * (1 - t) * (1 - t) + c * 2f * (1 - t) * t + b * t * t);
		}
		return l;
	}

	/// <summary>A table's or a chair's turned leg standing at <paramref name="foot"/>, <paramref name="h"/> tall,
	/// <paramref name="w"/> square at its top: a square block for the apron to join (its top <paramref name="block"/>),
	/// turned below it into a collar, a long taper with a ring, and a bun foot.</summary>
	public static void Leg(MeshKit k, Vector3 foot, float h, float w, float block = 0.12f, int sides = 10)
	{
		float r = w * 0.5f, t = h - block;
		k.ChamferBox(foot + Vector3.Up * (t + block * 0.5f), new Vector3(w, block, w), w * 0.12f);
		Lathe(k, foot, P(0f, 0f, r * 0.72f, 0f, r * 0.82f, 0.012f, r * 0.8f, 0.03f, r * 0.55f, 0.05f, r * 0.5f, 0.07f, r * 0.62f, t * 0.16f,
			r * 0.56f, t * 0.2f, r * 0.5f, t * 0.24f, r * 0.68f, t * 0.62f, r * 0.74f, t * 0.72f, r * 0.62f, t * 0.8f, r * 0.84f, t * 0.88f,
			r * 0.86f, t * 0.93f, r * 0.7f, t * 0.97f, r * 0.7f, t + 0.002f, 0f, t + 0.002f), sides, 50f);
	}

	/// <summary>A profile from a list of (radius, height) pairs (shorter to write).</summary>
	public static Vector2[] P(params float[] rh)
	{
		var a = new Vector2[rh.Length / 2];
		for (int i = 0; i < a.Length; i++) a[i] = new Vector2(rh[i * 2], rh[i * 2 + 1]);
		return a;
	}
}
