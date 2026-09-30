using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The plowed road (Act 22): someone has run a snowplow down a snaking road from the church's great door
/// through the winter woods to the ski lodge, about 1.3 km (the owner: a five-minute sprint, a seven-to-ten
/// minute walk). Its centreline, its cross-section (two tyre ruts in the packed snow, a hard edge, the
/// windrow the blade threw up either side, then the deep snow), and the ground's height everywhere round
/// it, in the church's local space.
/// </summary>
public partial class WinterWoods
{
	/// <summary>The road's bends (church-local x, z): out of the great door (z = 0, facing -Z) and away, snaking
	/// left and right round the hills every hundred metres or so (the owner, 2026-09-30: never a straight line to the
	/// lodge; a bend and a rise hide what's ahead, the trees included). The tightest bend is 23 m in radius, still
	/// wider than the ribbon's half (so its cross-sections never fold), and no two legs of it come within 90 m.</summary>
	private static readonly Vector2[] Ctrl =
	{
		new(0f, -1.2f), new(0f, -22f), new(-10f, -52f), new(-34f, -78f), new(-50f, -112f), new(-40f, -150f), new(-6f, -170f),
		new(30f, -192f), new(46f, -228f), new(30f, -262f), new(-4f, -280f), new(-36f, -306f), new(-46f, -344f), new(-26f, -378f),
		new(10f, -398f), new(42f, -424f), new(50f, -462f), new(28f, -494f), new(-8f, -512f), new(-40f, -540f), new(-48f, -580f),
		new(-24f, -612f), new(12f, -630f), new(42f, -660f), new(48f, -700f), new(22f, -730f), new(-14f, -748f), new(-42f, -778f),
		new(-44f, -818f), new(-16f, -848f), new(22f, -866f), new(46f, -898f), new(40f, -940f), new(10f, -966f), new(-8f, -1000f),
		new(-4f, -1040f), new(4f, -1070f), new(4f, -1100f),
	};
	public const float Step = 1.5f, RoadHalf = 3.4f, BermPeak = 4.6f, BermHalf = 5.0f, DeepSnow = 6.8f, BlendOut = 12.5f, RibbonHalf = 14.5f;
	public const float RegionHalf = 125f;

	private static List<Vector2> _pts;
	private static List<Vector2> _tan;
	private static List<float> _py;
	public static float Length { get; private set; }
	public static int Points => _pts?.Count ?? 0;

	/// <summary>The road's end (church-local): where it opens into the turning circle in front of the lodge.</summary>
	public static Vector2 RoadEnd { get { EnsurePath(); return _pts[^1]; } }
	/// <summary>The road's height where it ends (the lodge stands level with it).</summary>
	public static float RoadEndY { get { EnsurePath(); return _py[^1]; } }
	public static Vector2 RoadDir(int i) { EnsurePath(); return _tan[Mathf.Clamp(i, 0, _tan.Count - 1)]; }
	public static Vector2 RoadPoint(int i) { EnsurePath(); return _pts[Mathf.Clamp(i, 0, _pts.Count - 1)]; }

	/// <summary>The road at arc length <paramref name="s"/> (church-local, on its surface).</summary>
	public static Vector3 RoadAt(float s, out Vector2 dir)
	{
		EnsurePath();
		float f = Mathf.Clamp(s / Step, 0, _pts.Count - 1.001f);
		int i = (int)f;
		float t = f - i;
		var p = _pts[i].Lerp(_pts[i + 1], t);
		dir = _tan[i].Lerp(_tan[i + 1], t).Normalized();
		return new Vector3(p.X, Height(p.X, p.Y), p.Y);
	}

	private static void EnsurePath()
	{
		if (_pts != null) return;
		// Catmull-Rom through the bends, then resampled evenly every Step metres
		var dense = new List<Vector2>();
		for (int i = 0; i < Ctrl.Length - 1; i++)
		{
			Vector2 p0 = Ctrl[Mathf.Max(i - 1, 0)], p1 = Ctrl[i], p2 = Ctrl[i + 1], p3 = Ctrl[Mathf.Min(i + 2, Ctrl.Length - 1)];
			for (int k = 0; k < 40; k++)
			{
				float t = k / 40f, t2 = t * t, t3 = t2 * t;
				dense.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
			}
		}
		dense.Add(Ctrl[^1]);
		_pts = new List<Vector2> { dense[0] };
		float carry = 0f;
		for (int i = 1; i < dense.Count; i++)
		{
			Vector2 a = dense[i - 1], b = dense[i];
			float seg = a.DistanceTo(b);
			float at = Step - carry;
			while (at <= seg)
			{
				_pts.Add(a.Lerp(b, at / seg));
				at += Step;
			}
			carry = seg - (at - Step);
		}
		Length = (_pts.Count - 1) * Step;
		_tan = new List<Vector2>();
		for (int i = 0; i < _pts.Count; i++)
			_tan.Add((_pts[Mathf.Min(i + 1, _pts.Count - 1)] - _pts[Mathf.Max(i - 1, 0)]).Normalized());
		// the road's own height: the ground under its centreline, smoothed over 60 m (a plow road doesn't
		// follow every hump), held at the church's step for the first few metres
		var raw = new float[_pts.Count];
		for (int i = 0; i < raw.Length; i++) raw[i] = BaseHeight(_pts[i].X, _pts[i].Y);
		_py = new List<float>();
		const int half = 20;
		for (int i = 0; i < raw.Length; i++)
		{
			float sum = 0f, w = 0f;
			for (int j = -half; j <= half; j++)
			{
				int k = Mathf.Clamp(i + j, 0, raw.Length - 1);
				float wj = 1f - Mathf.Abs(j) / (half + 1f);
				sum += raw[k] * wj; w += wj;
			}
			float y = sum / w;
			float nearDoor = 1f - Mathf.SmoothStep(4f, 18f, i * Step);
			_py.Add(Mathf.Lerp(y, WinterGlade.GroundY, nearDoor));
		}
	}

	/// <summary>The rolling snow before any road is cut through it: flat round the church, low drifts in the
	/// clearing, then long slow swells the road rises and falls over.</summary>
	public static float BaseHeight(float x, float z)
	{
		float d = WinterGlade.OutsideChurch(x, z);
		float swell = Mathf.SmoothStep(8f, 40f, d);
		float n = Mathf.Sin(x * 0.045f + 1.3f) * Mathf.Cos(z * 0.038f - 0.7f) * 2.2f + Mathf.Sin(x * 0.11f + z * 0.07f) * 0.7f;
		float hills = Mathf.Sin(x * 0.011f + 0.4f) * Mathf.Cos(z * 0.0085f + 1.1f) * 6f + Mathf.Sin(z * 0.004f) * 4f;
		return WinterGlade.GroundY + swell * (n + 1.2f) + Mathf.SmoothStep(30f, 140f, d) * hills;
	}

	/// <summary>The hills the road winds between (metres above the rolling snow), church-local: the ground climbs away
	/// from the road on both sides, highest between its legs (a ridge inside every bend, so a bend hides what's round
	/// it), knolls and hollows on the slopes; flat in the church's clearing and on the lodge's shelf.</summary>
	public static float Hills(float x, float z, float d)
	{
		float away = Mathf.SmoothStep(9f, 52f, d);
		if (away <= 0f) return 0f;
		// how high they get here: a slow field, 6-17 m
		float tall = 11.5f + Mathf.Sin(x * 0.019f + 0.6f) * Mathf.Cos(z * 0.014f - 1.2f) * 4f + Mathf.Sin((x + z) * 0.008f) * 1.5f;
		// knolls and hollows on the slopes
		float knolls = Mathf.Sin(x * 0.083f + 1.7f) * Mathf.Cos(z * 0.071f + 0.3f) * 2.4f + Mathf.Sin(x * 0.17f - z * 0.13f) * 0.8f;
		float h = tall * away + knolls * Mathf.SmoothStep(12f, 32f, d);
		// none right round the church (its yard stays level; the hills rise from 40 m out)
		h *= Mathf.SmoothStep(30f, 90f, WinterGlade.OutsideChurch(x, z));
		return h;
	}

	/// <summary>The nearest point of the road: its distance, the arc length there, and which side (+1 right of the
	/// direction of travel, -1 left).</summary>
	public static float Nearest(float x, float z, out float s, out float side)
	{
		EnsurePath();
		var p = new Vector2(x, z);
		int best = 0; float bd = float.MaxValue;
		for (int i = 0; i < _pts.Count; i += 8)
		{
			float d = _pts[i].DistanceSquaredTo(p);
			if (d < bd) { bd = d; best = i; }
		}
		int lo = Mathf.Max(best - 10, 0), hi = Mathf.Min(best + 10, _pts.Count - 2);
		bd = float.MaxValue; s = 0f; side = 1f;
		for (int i = lo; i <= hi; i++)
		{
			Vector2 a = _pts[i], b = _pts[i + 1], ab = b - a;
			float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
			var q = a + ab * t;
			float d = q.DistanceSquaredTo(p);
			if (d < bd)
			{
				bd = d;
				s = (i + t) * Step;
				side = (ab.X * (p.Y - a.Y) - ab.Y * (p.X - a.X)) > 0 ? -1f : 1f;
			}
		}
		// past either end of the road the distance is to its end point (the church's step, the lodge's circle)
		return Mathf.Sqrt(bd);
	}

	private static float RoadY(float s)
	{
		EnsurePath();
		float f = Mathf.Clamp(s / Step, 0, _py.Count - 1.001f);
		int i = (int)f;
		return Mathf.Lerp(_py[i], _py[i + 1], f - i);
	}

	/// <summary>The road's cross-section, height above its centre at <paramref name="d"/> metres out: two tyre
	/// ruts, the packed edge, the windrow the blade threw up, the deep undisturbed snow beyond it.</summary>
	public static float Profile(float d)
	{
		d = Mathf.Abs(d);
		float y = 0f;
		// the ruts: two shallow grooves a car's width apart
		y -= 0.05f * Mathf.Exp(-Mathf.Pow((d - 1.0f) / 0.28f, 2f));
		// the packed edge, then the windrow
		y += Mathf.SmoothStep(RoadHalf - 0.3f, RoadHalf + 0.4f, d) * 0.12f;
		float berm = Mathf.Exp(-Mathf.Pow((d - BermPeak) / 0.75f, 2f));
		y += berm * 0.72f;
		y += Mathf.SmoothStep(BermHalf - 0.4f, DeepSnow, d) * 0.3f;
		return y;
	}

	/// <summary>The ground (what the player walks on), church-local: the road and its banks where the road
	/// is, the rolling snow away from it, the flats round the lodge.</summary>
	public static float Height(float x, float z)
	{
		float d = Nearest(x, z, out float s, out _);
		float basey = BaseHeight(x, z);
		float h;
		if (d <= DeepSnow) h = RoadY(s) + Profile(d);
		else
		{
			float roadSide = RoadY(s) + Profile(DeepSnow);
			h = Mathf.Lerp(roadSide, basey, Mathf.SmoothStep(DeepSnow, BlendOut + 6f, d)) + Hills(x, z, d);
		}
		return Flats(x, z, h);
	}

	/// <summary>Whether a church-local point is on the plowed road (or the lodge's turning circle).</summary>
	public static float PlowedWeight(float x, float z)
	{
		float d = Nearest(x, z, out float s, out _);
		float w = 1f - Mathf.SmoothStep(RoadHalf - 0.2f, RoadHalf + 0.5f, d);
		if (s >= Length - 1f) w *= 1f - Mathf.SmoothStep(0f, 3f, d);   // past the road's end: the circle's own
		return Mathf.Max(w, SkiLodge.CircleWeight(x, z));
	}

	/// <summary>How far toward the lodge (0 at the church, 1 at the lodge): the snow thins and the ice comes.</summary>
	public static float FrozenAt(float s) => Mathf.SmoothStep(0.52f, 0.9f, s / Mathf.Max(Length, 1f));
}
