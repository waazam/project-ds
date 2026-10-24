using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-14h, the Hollow's interiors pass) The walls' logs drawn round. They had been boxes with a log texture on
/// their faces, and under the lamp and the lantern they read as flat planks. Each log is now half an ellipse each side
/// of the wall (wider than the wall by a few centimetres, so it bulges), smooth-shaded and darkened in its vertices
/// toward the top and bottom where it turns away into the joint; between the courses a band of pale lime chinking set
/// back in the groove, so every course has a dark line over it and a light one under. The outside halves go into the
/// shell (cast shadows), the inside halves and their chinking into the room's lining (baked by its RoomShade).
/// </summary>
public partial class Cabin
{
	/// <summary>How far the chinking sits from the wall's centre line (the logs bulge to LogT/2 + LogBulge).</summary>
	private const float ChinkDepth = 0.072f, LogBulge = 0.03f, ChinkHalf = 0.034f;
	private const int LogSegs = 7;

	/// <summary>One log from <paramref name="a0"/> to <paramref name="a1"/> along the wall, its centre line at
	/// <paramref name="line"/>, the course <paramref name="y0"/>..<paramref name="y1"/>; <paramref name="outward"/> the
	/// world direction of the wall's outside (±X or ±Z). Ends capped with end grain where asked.</summary>
	private void RoundLog(MeshKit shell, MeshKit lining, bool alongZ, float a0, float a1, float line, float y0, float y1, Vector3 outward,
		bool endA, bool endB, bool top, bool bottom, Color tint, float uShift)
	{
		float yc = (y0 + y1) * 0.5f, ry = (y1 - y0) * 0.5f + 0.004f, rd = LogT * 0.5f + LogBulge;
		float thC = Mathf.Acos(Mathf.Clamp((ChinkDepth - 0.006f) / rd, 0f, 1f));
		// (2026-10-24, the art pass) no two logs alike: each its own girth, tapering from butt to top, a little bowed
		// (they were identical straight cylinders, and read as made by machine). The height stays within the course,
		// so the chinking still closes every joint.
		var lr = new RandomNumberGenerator { Seed = (ulong)(Mathf.Abs(a0 * 131.7f + a1 * 17.3f + line * 73.1f + y0 * 977.9f) * 1000f) + (ulong)Seed * 7919UL };
		float girth = lr.RandfRange(0.84f, 1.1f), taper = lr.RandfRange(-0.14f, 0.14f), bow = lr.RandfRange(-0.024f, 0.024f);
		float Grow(float a) { float f = Mathf.Abs(a1 - a0) > 1e-4f ? (a - a0) / (a1 - a0) : 0f; return girth * (1f + taper * (0.5f - f)); }
		float Bow(float a) { float f = Mathf.Abs(a1 - a0) > 1e-4f ? (a - a0) / (a1 - a0) : 0f; return bow * Mathf.Sin(f * Mathf.Pi); }
		Vector3 along = alongZ ? Vector3.Back : Vector3.Right;
		Vector3 Base(float a) => alongZ ? new Vector3(line, 0, a) : new Vector3(a, 0, line);
		const float vLen = 3.2f;   // metres of log to the texture's height (its grain runs along V)
		float circ = Mathf.Tau * Mathf.Sqrt((rd * rd + ry * ry) * 0.5f);
		foreach (int side in new[] { 1, -1 })
		{
			var k = side > 0 ? shell : lining;
			k.Mat(side > 0 ? HollowLook.CabinLogOut : HollowLook.CabinLogIn);
			Vector3 d = outward * side;
			float thLo = bottom ? -Mathf.Pi * 0.5f : -thC, thHi = top ? Mathf.Pi * 0.5f : thC;
			for (int i = 0; i < LogSegs; i++)
			{
				float t0 = Mathf.Lerp(thLo, thHi, i / (float)LogSegs), t1 = Mathf.Lerp(thLo, thHi, (i + 1) / (float)LogSegs);
				Vector3 P(float a, float th) => Base(a) + d * (rd * Grow(a) * Mathf.Cos(th)) + Vector3.Up * (yc + Bow(a) + ry * Mathf.Min(1f, Grow(a)) * Mathf.Sin(th));
				Vector3 N(float th) => (d * (Mathf.Cos(th) / rd) + Vector3.Up * (Mathf.Sin(th) / ry)).Normalized();
				float u0 = uShift + (side > 0 ? 0f : 0.5f) + t0 / Mathf.Tau * circ, u1 = uShift + (side > 0 ? 0f : 0.5f) + t1 / Mathf.Tau * circ;
				// darker where the log turns away into the joint
				Color C(float th) { float f = 1f - 0.55f * Mathf.Pow(Mathf.Abs(Mathf.Sin(th)), 2.2f); return new Color(tint.R * f, tint.G * f, tint.B * f); }
				// one strip: drawn as two triangles per length cell (Quad, so the lining's Tess cuts it along)
				var n0 = N(t0); var n1 = N(t1);
				k.Color = C((t0 + t1) * 0.5f);
				// (Quad takes one normal: draw the strip with per-edge normals through Tri, cut along by hand)
				int cells = k.Tess > 0f ? Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / k.Tess)) : 1;
				for (int c = 0; c < cells; c++)
				{
					float aa = Mathf.Lerp(a0, a1, c / (float)cells), ab = Mathf.Lerp(a0, a1, (c + 1) / (float)cells);
					Vector2 Uv(float u, float a) => new(u, a / vLen);
					var ca = C(t0); var cb = C(t1);
					k.TriC(P(aa, t0), P(ab, t0), P(ab, t1), n0, n0, n1, Uv(u0, aa), Uv(u0, ab), Uv(u1, ab), ca, ca, cb);
					k.TriC(P(aa, t0), P(ab, t1), P(aa, t1), n0, n1, n1, Uv(u0, aa), Uv(u1, ab), Uv(u1, aa), ca, cb, cb);
				}
			}
		}
		// end grain on the cut ends (the corners' crossed ends, the openings' cut logs): an ellipse fan
		shell.Mat(BuildingTextures.LogEndMat);
		shell.Color = new Color(tint.R * 0.9f, tint.G * 0.88f, tint.B * 0.85f);
		foreach (var (at, sgn, want) in new[] { (a0, -1f, endA), (a1, 1f, endB) })
		{
			if (!want) continue;
			Vector3 c0 = Base(at) + Vector3.Up * (yc + Bow(at));
			float er = Grow(at);
			Vector3 nrm = along * sgn;
			const int ring = 14;
			for (int i = 0; i < ring; i++)
			{
				float p0 = Mathf.Tau * i / ring, p1 = Mathf.Tau * (i + 1) / ring;
				Vector3 Q(float p) => c0 + outward * (rd * er * Mathf.Cos(p)) + Vector3.Up * (ry * Mathf.Min(1f, er) * Mathf.Sin(p));
				Vector2 U(float p) => new(0.5f + 0.48f * Mathf.Cos(p), 0.5f + 0.48f * Mathf.Sin(p));
				shell.Tri(c0, Q(p0), Q(p1), nrm, new Vector2(0.5f, 0.5f), U(p0), U(p1));
			}
		}
	}

	/// <summary>The chinking in the joint at height <paramref name="y"/> between <paramref name="a0"/> and
	/// <paramref name="a1"/>: a band set back in the groove each side, a little proud at its middle (trowelled).</summary>
	private void Chink(MeshKit shell, MeshKit lining, bool alongZ, float a0, float a1, float line, float y, Vector3 outward, float shade)
	{
		Vector3 Base(float a) => alongZ ? new Vector3(line, 0, a) : new Vector3(a, 0, line);
		foreach (int side in new[] { 1, -1 })
		{
			var k = side > 0 ? shell : lining;
			k.Mat(side > 0 ? HollowLook.ChinkOut : HollowLook.ChinkIn);
			k.Color = new Color(shade, shade, shade * 0.97f);
			Vector3 d = outward * side;
			// two facets: the upper one tipped down, the lower tipped up (a shallow V: catches light top, shadow under)
			Vector3 mid = d * (ChinkDepth + 0.006f), edge = d * ChinkDepth;
			Vector3 nUp = (d + Vector3.Up * 0.35f).Normalized(), nDn = (d - Vector3.Up * 0.35f).Normalized();
			k.Quad(Base(a0) + mid + Vector3.Up * y, Base(a1) + mid + Vector3.Up * y, Base(a1) + edge + Vector3.Up * (y + ChinkHalf), Base(a0) + edge + Vector3.Up * (y + ChinkHalf), nUp,
				new Vector2(a0, 0), new Vector2(a1, 0), new Vector2(a1, ChinkHalf), new Vector2(a0, ChinkHalf));
			k.Quad(Base(a0) + edge + Vector3.Up * (y - ChinkHalf), Base(a1) + edge + Vector3.Up * (y - ChinkHalf), Base(a1) + mid + Vector3.Up * y, Base(a0) + mid + Vector3.Up * y, nDn,
				new Vector2(a0, -ChinkHalf), new Vector2(a1, -ChinkHalf), new Vector2(a1, 0), new Vector2(a0, 0));
		}
	}
}
