using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>Dense 2D (x,z) polyline with arc length, built from Catmull-Rom control points.</summary>
public class Polyline2
{
	public readonly List<Vector2> Points = new();
	public readonly List<float> Cum = new();
	public float Length => Cum.Count > 0 ? Cum[^1] : 0f;

	public static Polyline2 CatmullRom(IList<Vector2> ctrl, float step)
	{
		var pl = new Polyline2();
		if (ctrl.Count == 0) return pl;
		if (ctrl.Count == 1) { pl.Add(ctrl[0]); return pl; }
		for (int i = 0; i < ctrl.Count - 1; i++)
		{
			Vector2 p0 = ctrl[Mathf.Max(i - 1, 0)], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[Mathf.Min(i + 2, ctrl.Count - 1)];
			if (i == 0) p0 = p1 - (p2 - p1);
			if (i + 2 >= ctrl.Count) p3 = p2 + (p2 - p1);
			int n = Mathf.Max(1, Mathf.CeilToInt(p1.DistanceTo(p2) / step));
			for (int k = 0; k < n; k++)
			{
				float t = (float)k / n;
				pl.Add(CR(p0, p1, p2, p3, t));
			}
		}
		pl.Add(ctrl[^1]);
		return pl;
	}

	private static Vector2 CR(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
	{
		float t2 = t * t, t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	public void Add(Vector2 p)
	{
		if (Points.Count > 0 && Points[^1].DistanceSquaredTo(p) < 1e-6f) return;
		Cum.Add(Points.Count == 0 ? 0f : Cum[^1] + Points[^1].DistanceTo(p));
		Points.Add(p);
	}

	public Polyline2 Decimate(int every)
	{
		var pl = new Polyline2();
		for (int i = 0; i < Points.Count; i += every) pl.Add(Points[i]);
		pl.Add(Points[^1]);
		return pl;
	}

	/// <summary>Point and unit tangent at arc length s (clamped).</summary>
	public Vector2 At(float s, out Vector2 tangent)
	{
		tangent = Vector2.Down;
		if (Points.Count < 2) return Points.Count == 1 ? Points[0] : Vector2.Zero;
		s = Mathf.Clamp(s, 0, Length);
		int lo = 0, hi = Cum.Count - 1;
		while (hi - lo > 1)
		{
			int mid = (lo + hi) / 2;
			if (Cum[mid] <= s) lo = mid; else hi = mid;
		}
		float segLen = Cum[hi] - Cum[lo];
		float t = segLen > 1e-5f ? (s - Cum[lo]) / segLen : 0f;
		tangent = (Points[hi] - Points[lo]).Normalized();
		return Points[lo].Lerp(Points[hi], t);
	}

	/// <summary>Brute-force closest point. Returns distance; s = arc length of the closest point.</summary>
	public float Closest(Vector2 p, out float s)
	{
		float best = float.MaxValue; s = 0;
		for (int k = 0; k < Points.Count - 1; k++)
		{
			Vector2 a = Points[k], ab = Points[k + 1] - a;
			float l2 = ab.LengthSquared();
			float t = l2 > 1e-8f ? Mathf.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
			float d = p.DistanceSquaredTo(a + ab * t);
			if (d < best) { best = d; s = Cum[k] + t * Mathf.Sqrt(l2); }
		}
		return Mathf.Sqrt(best);
	}

	/// <summary>
	/// Rasterise distance-to-polyline into a grid (origin, spacing, w x h), limited to radius R.
	/// Cells farther than R keep their initial value (R).
	/// </summary>
	public void Raster(Vector2 origin, float step, int w, int h, float R, float[] dist, float[] along)
	{
		for (int k = 0; k < Points.Count - 1; k++)
		{
			Vector2 a = Points[k], b = Points[k + 1], ab = b - a;
			float l2 = ab.LengthSquared(), len = Mathf.Sqrt(l2);
			int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.X, b.X) - R - origin.X) / step));
			int i1 = Mathf.Min(w - 1, Mathf.CeilToInt((Mathf.Max(a.X, b.X) + R - origin.X) / step));
			int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Y, b.Y) - R - origin.Y) / step));
			int j1 = Mathf.Min(h - 1, Mathf.CeilToInt((Mathf.Max(a.Y, b.Y) + R - origin.Y) / step));
			for (int j = j0; j <= j1; j++)
			{
				float pz = origin.Y + j * step;
				int row = j * w;
				for (int i = i0; i <= i1; i++)
				{
					float px = origin.X + i * step;
					float t = l2 > 1e-8f ? ((px - a.X) * ab.X + (pz - a.Y) * ab.Y) / l2 : 0f;
					t = t < 0 ? 0 : (t > 1 ? 1 : t);
					float dx = px - (a.X + ab.X * t), dz = pz - (a.Y + ab.Y * t);
					float d2 = dx * dx + dz * dz;
					int idx = row + i;
					float cur = dist[idx];
					if (d2 < cur * cur)
					{
						dist[idx] = Mathf.Sqrt(d2);
						if (along != null) along[idx] = Cum[k] + t * len;
					}
				}
			}
		}
	}
}
