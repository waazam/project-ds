using System;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Geometry helpers for the buildings, on top of MeshKit: boxes that can skip hidden
/// faces, round-log pieces (log texture on the sides, end grain on the ends),
/// vertical-board panels and a triangle panel. Local UVs are world-scaled so
/// neighbouring pieces continue their texture.
/// </summary>
public static class BuildKit
{
	[Flags]
	public enum Face { None = 0, PX = 1, NX = 2, PY = 4, NY = 8, PZ = 16, NZ = 32, All = 63 }

	/// <summary>MeshKit.Box, minus the faces in <paramref name="skip"/>.</summary>
	public static void Box(MeshKit k, Vector3 c, Vector3 s, float uv = 1f, Face skip = Face.None, Basis? rot = null)
	{
		Basis b = rot ?? Basis.Identity;
		Vector3 h = s * 0.5f;
		Vector3 P(float x, float y, float z) => c + b * new Vector3(x * h.X, y * h.Y, z * h.Z);
		Vector3 ax = b.X.Normalized(), ay = b.Y.Normalized(), az = b.Z.Normalized();
		float u = uv;
		if ((skip & Face.PY) == 0)
			k.Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), ay, new Vector2(0, 0), new Vector2(s.X * u, 0), new Vector2(s.X * u, s.Z * u), new Vector2(0, s.Z * u));
		if ((skip & Face.NY) == 0)
			k.Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -ay, new Vector2(0, 0), new Vector2(s.X * u, 0), new Vector2(s.X * u, s.Z * u), new Vector2(0, s.Z * u));
		if ((skip & Face.PZ) == 0)
			k.Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), az, new Vector2(0, s.Y * u), new Vector2(s.X * u, s.Y * u), new Vector2(s.X * u, 0), new Vector2(0, 0));
		if ((skip & Face.NZ) == 0)
			k.Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -az, new Vector2(0, s.Y * u), new Vector2(s.X * u, s.Y * u), new Vector2(s.X * u, 0), new Vector2(0, 0));
		if ((skip & Face.PX) == 0)
			k.Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), ax, new Vector2(0, s.Y * u), new Vector2(s.Z * u, s.Y * u), new Vector2(s.Z * u, 0), new Vector2(0, 0));
		if ((skip & Face.NX) == 0)
			k.Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -ax, new Vector2(0, s.Y * u), new Vector2(s.Z * u, s.Y * u), new Vector2(s.Z * u, 0), new Vector2(0, 0));
	}

	/// <summary>
	/// A squared-off round log lying along X from x0 to x1 (or along Z when <paramref name="alongZ"/>),
	/// its long axis through (·, y0..y1, line). Side faces take the log texture with V spanning the
	/// course exactly and U continuing along the wall (1 U per <paramref name="uLen"/> metres);
	/// ends take end grain. Bottom face is skipped (always sits on something).
	/// </summary>
	public static void Log(MeshKit k, Material side, Material end, bool alongZ, float a0, float a1, float line, float y0, float y1, float thick, float uLen, bool endA = true, bool endB = true)
	{
		float t = thick * 0.5f;
		Vector3 P(float a, float y, float o) => alongZ ? new Vector3(line + o, y, a) : new Vector3(a, y, line + o);
		Vector3 outN = alongZ ? Vector3.Right : Vector3.Back;   // +X or +Z
		Vector3 alongN = alongZ ? Vector3.Back : Vector3.Right;
		float u0 = a0 / uLen, u1 = a1 / uLen;
		k.Mat(side);
		// the two long sides (face +o and -o)
		k.Quad(P(a0, y1, t), P(a1, y1, t), P(a1, y0, t), P(a0, y0, t), outN, new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), new Vector2(u0, 1));
		k.Quad(P(a1, y1, -t), P(a0, y1, -t), P(a0, y0, -t), P(a1, y0, -t), -outN, new Vector2(u1, 0), new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(u1, 1));
		// top (sills, the top course under the roof)
		k.Quad(P(a0, y1, -t), P(a1, y1, -t), P(a1, y1, t), P(a0, y1, t), Vector3.Up, new Vector2(u0, 0.3f), new Vector2(u1, 0.3f), new Vector2(u1, 0.5f), new Vector2(u0, 0.5f));
		k.Mat(end);
		if (endB) k.Quad(P(a1, y1, -t), P(a1, y1, t), P(a1, y0, t), P(a1, y0, -t), alongN, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		if (endA) k.Quad(P(a0, y1, t), P(a0, y1, -t), P(a0, y0, -t), P(a0, y0, t), -alongN, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
	}

	/// <summary>Double-sided flat triangle (e.g. a gable), UV projected from its plane: u along <paramref name="uAxis"/>, v down Y.</summary>
	public static void TriPanel(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector3 uAxis, float uv, float offset)
	{
		Vector2 U(Vector3 p) => new(p.Dot(uAxis) * uv, -p.Y * uv);
		Vector3 o = n * offset;
		k.Tri(a + o, b + o, c + o, n, U(a), U(b), U(c));
		k.Tri(a - o, b - o, c - o, -n, U(a), U(b), U(c));
	}
}
