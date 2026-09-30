using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The winter woods' bare broadleaves (Act 22 and the church's clearing; the owner, 2026-09-30: most of the trees just
/// trunks and branches, no leaves, like the references). A trunk that leans and wanders, its root flare in the snow, a
/// leader up through the crown; gnarled scaffold limbs off it that fork and fork again, kinking at every fork, down to
/// sprays of fine twigs (crossed alpha-cut cards) at every tip. Poly Haven's bark (tools/Textures/make_winter_forest.py)
/// with the snow laid along the limbs' tops and plastered up the windward side of the trunk by the bark's shader.
/// A MultiMesh of these wants its custom data on (r: the ice, 0..1).
/// </summary>
public static class WinterTreeKit
{
	private static readonly ShaderMaterial[] _bark = new ShaderMaterial[3];
	private static ShaderMaterial _twigs;

	/// <summary>0: grey-brown oak-like bark; 1: pale grey; 2: the firs' pine bark.</summary>
	public static ShaderMaterial Bark(int variant)
	{
		variant = Mathf.Clamp(variant, 0, 2);
		if (_bark[variant] != null) return _bark[variant];
		string name = variant switch { 1 => "bark_grey", 2 => "pine_bark", _ => "bark" };
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/winter_bark.gdshader"), ResourceName = "winter_" + name };
		m.SetShaderParameter("bark_tex", GD.Load<Texture2D>($"res://assets/textures/winter/{name}_albedo.png"));
		m.SetShaderParameter("bark_nor", GD.Load<Texture2D>($"res://assets/textures/winter/{name}_normal.png"));
		m.SetShaderParameter("snow_tex", GD.Load<Texture2D>("res://assets/textures/snow/snow_albedo.png"));
		m.SetMeta("detail_kind", -1);
		return _bark[variant] = m;
	}

	public static ShaderMaterial Twigs
	{
		get
		{
			if (_twigs != null) return _twigs;
			_twigs = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/winter_twigs.gdshader"), ResourceName = "winter_twigs" };
			_twigs.SetShaderParameter("twig_tex", GD.Load<Texture2D>("res://assets/textures/winter/twigs.png"));
			_twigs.SetMeta("detail_kind", -1);
			return _twigs;
		}
	}

	/// <summary>A loft through rings (centre, radius), its frame carried along so it never twists; bark u once or
	/// twice round, v up it at <paramref name="vScale"/> per metre.</summary>
	public static void Loft(MeshKit k, IList<(Vector3 c, float r)> rings, int sides, float uRound, float vScale, float v0 = 0f)
	{
		int n = rings.Count;
		var tan = new Vector3[n];
		for (int i = 0; i < n; i++)
			tan[i] = (rings[Mathf.Min(i + 1, n - 1)].c - rings[Mathf.Max(i - 1, 0)].c).Normalized();
		var bx = new Vector3[n];
		var refv = Mathf.Abs(tan[0].Y) > 0.9f ? Vector3.Right : Vector3.Up;
		bx[0] = refv.Cross(tan[0]).Normalized();
		for (int i = 1; i < n; i++)
		{
			var b = bx[i - 1] - tan[i] * bx[i - 1].Dot(tan[i]);
			bx[i] = b.LengthSquared() < 1e-6f ? (Mathf.Abs(tan[i].Y) > 0.9f ? Vector3.Right : Vector3.Up).Cross(tan[i]).Normalized() : b.Normalized();
		}
		float v = v0;
		for (int i = 0; i < n - 1; i++)
		{
			var (c0, r0) = rings[i];
			var (c1, r1) = rings[i + 1];
			float len = c0.DistanceTo(c1);
			float vn = v + len * vScale;
			Vector3 by0 = tan[i].Cross(bx[i]), by1 = tan[i + 1].Cross(bx[i + 1]);
			float slope = (r0 - r1) / Mathf.Max(len, 0.001f);
			for (int s = 0; s < sides; s++)
			{
				float a0 = Mathf.Tau * s / sides, a1 = Mathf.Tau * (s + 1) / sides;
				Vector3 d00 = bx[i] * Mathf.Cos(a0) + by0 * Mathf.Sin(a0), d01 = bx[i] * Mathf.Cos(a1) + by0 * Mathf.Sin(a1);
				Vector3 d10 = bx[i + 1] * Mathf.Cos(a0) + by1 * Mathf.Sin(a0), d11 = bx[i + 1] * Mathf.Cos(a1) + by1 * Mathf.Sin(a1);
				Vector3 p00 = c0 + d00 * r0, p01 = c0 + d01 * r0, p10 = c1 + d10 * r1, p11 = c1 + d11 * r1;
				Vector3 n00 = (d00 + tan[i] * slope).Normalized(), n01 = (d01 + tan[i] * slope).Normalized();
				Vector3 n10 = (d10 + tan[i + 1] * slope).Normalized(), n11 = (d11 + tan[i + 1] * slope).Normalized();
				float u0 = uRound * s / sides, u1 = uRound * (s + 1) / sides;
				k.Tri(p00, p11, p10, n00, n11, n10, new Vector2(u0, v), new Vector2(u1, vn), new Vector2(u0, vn));
				k.Tri(p00, p01, p11, n00, n01, n11, new Vector2(u0, v), new Vector2(u1, v), new Vector2(u1, vn));
			}
			v = vn;
		}
		// the tip closed (a point, so no hole shows looking up a branch)
		var (ce, re) = rings[n - 1];
		if (re > 0.004f)
		{
			Vector3 tipP = ce + tan[n - 1] * re * 1.5f;
			Vector3 bye = tan[n - 1].Cross(bx[n - 1]);
			for (int s = 0; s < sides; s++)
			{
				float a0 = Mathf.Tau * s / sides, a1 = Mathf.Tau * (s + 1) / sides;
				Vector3 d0 = bx[n - 1] * Mathf.Cos(a0) + bye * Mathf.Sin(a0), d1 = bx[n - 1] * Mathf.Cos(a1) + bye * Mathf.Sin(a1);
				k.Tri(ce + d0 * re, ce + d1 * re, tipP, d0, d1, tan[n - 1], new Vector2(0, v), new Vector2(uRound / sides, v), new Vector2(0, v + 0.05f));
			}
		}
	}

	/// <summary>One tree on its own (a MultiMesh of one, so the bark's shader gets its custom data: no ice).</summary>
	public static MultiMeshInstance3D Single(Mesh mesh, string name, float ice = 0f)
	{
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
		mm.SetInstanceTransform(0, Transform3D.Identity);
		mm.SetInstanceCustomData(0, new Color(ice, 0, 0, 0));
		return new MultiMeshInstance3D { Name = name, Multimesh = mm };
	}

	/// <summary>A bare broadleaf about <paramref name="height"/> tall (its base at the origin, sunk a little).</summary>
	public static Mesh BareTree(int seed, float height, int bark = 0)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 9173 + 17) };
		var k = new MeshKit();
		var twigs = new List<(Vector3 at, Vector3 dir, float size)>();
		k.Mat(Bark(bark));
		float tint = rng.RandfRange(0.82f, 1f);
		k.Color = new Color(tint, tint, tint);
		float trunkR = 0.11f + height * 0.015f * rng.RandfRange(0.85f, 1.2f);
		// ---- the trunk: a lean and a slow wander, the root flare, up through the crown as a thinning leader
		Vector3 lean = new(rng.RandfRange(-0.07f, 0.07f), 1f, rng.RandfRange(-0.07f, 0.07f));
		float ph = rng.RandfRange(0f, Mathf.Tau);
		Vector3 Axis(float y)
		{
			float f = y / height;
			return new Vector3(lean.X * y + Mathf.Sin(f * 3.1f + ph) * 0.25f * f, y, lean.Z * y + Mathf.Cos(f * 2.6f + ph) * 0.22f * f);
		}
		float crown = height * rng.RandfRange(0.3f, 0.45f);
		float leaderTop = height * rng.RandfRange(0.78f, 0.9f);
		var rings = new List<(Vector3, float)>
		{
			(new Vector3(0, -0.5f, 0), trunkR * 1.75f), (new Vector3(0, -0.1f, 0), trunkR * 1.6f), (Axis(0.25f), trunkR * 1.28f),
			(Axis(0.7f), trunkR * 1.08f), (Axis(1.5f), trunkR),
		};
		for (int i = 1; i <= 6; i++)
		{
			float y = Mathf.Lerp(1.5f, leaderTop, i / 6f);
			float f = (y - 1.5f) / (leaderTop - 1.5f);
			rings.Add((Axis(y), trunkR * Mathf.Lerp(0.98f, 0.12f, Mathf.Pow(f, 0.85f))));
		}
		Loft(k, rings, 9, 2f, 0.55f);
		// a root or two showing through the snow
		int roots = rng.RandiRange(3, 5);
		for (int i = 0; i < roots; i++)
		{
			float a = Mathf.Tau * i / roots + rng.RandfRange(-0.3f, 0.3f);
			var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			Loft(k, new List<(Vector3, float)> { (Vector3.Up * 0.35f + d * trunkR * 0.6f, trunkR * 0.55f), (d * trunkR * 2.2f + Vector3.Up * 0.02f, trunkR * 0.35f), (d * trunkR * 4f + Vector3.Down * 0.2f, trunkR * 0.12f) }, 5, 1f, 0.55f);
		}
		// ---- the limbs: scaffold limbs spiralling up the trunk from the crown's start, shorter toward the top
		void Branch(Vector3 from, Vector3 dir, float len, float r, int depth)
		{
			// two kinked steps (gnarled), a little droop at the far end of the long ones
			var pts = new List<(Vector3, float)> { (from - dir * r, r) };
			Vector3 p = from, d = dir;
			int steps = depth >= 2 ? 3 : 2;
			for (int st = 1; st <= steps; st++)
			{
				d = (d + new Vector3(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-0.18f, 0.22f) - (depth >= 2 ? 0.06f : 0f), rng.RandfRange(-0.3f, 0.3f))).Normalized();
				p += d * len / steps;
				pts.Add((p, Mathf.Lerp(r, r * 0.55f, (float)st / steps)));
			}
			Loft(k, pts, depth >= 3 ? 7 : depth == 2 ? 5 : 4, 1f, 0.55f);
			float rEnd = r * 0.55f;
			if (depth == 0 || rEnd < 0.008f)
			{
				twigs.Add((p, d, Mathf.Clamp(len * 1.6f, 0.9f, 2.2f)));
				return;
			}
			// forks: two or three, off the end and off the middle
			int n = depth >= 2 ? rng.RandiRange(2, 3) : 2;
			for (int i = 0; i < n; i++)
			{
				var (at, ar) = i == 0 || steps < 3 ? pts[^1] : pts[rng.RandiRange(1, pts.Count - 2)];
				float spread = rng.RandfRange(0.4f, 0.85f);
				float a = rng.RandfRange(0, Mathf.Tau);
				var side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
				var nd = (d + side * spread + Vector3.Up * rng.RandfRange(0.05f, 0.4f)).Normalized();
				Branch(at, nd, len * rng.RandfRange(0.55f, 0.78f), Mathf.Min(ar * 0.72f, r * 0.7f), depth - 1);
			}
			// and a spray on the fork itself, so the crown is full of fine twigs, not only at its rim
			if (depth <= 2) twigs.Add((p, d, Mathf.Clamp(len * 1.1f, 0.7f, 1.6f)));
		}
		int limbs = rng.RandiRange(5, 7);
		float ang = rng.RandfRange(0, Mathf.Tau);
		for (int i = 0; i < limbs; i++)
		{
			float f = (float)i / limbs;
			float y = Mathf.Lerp(crown, leaderTop * 0.92f, f);
			ang += 2.4f + rng.RandfRange(-0.3f, 0.3f);   // (the golden angle, roughly: they spiral)
			var at = Axis(y);
			float rise = Mathf.Lerp(0.55f, 1.3f, f) + rng.RandfRange(-0.15f, 0.2f);
			var dir = new Vector3(Mathf.Cos(ang), rise, Mathf.Sin(ang)).Normalized();
			float len = height * Mathf.Lerp(0.34f, 0.18f, f) * rng.RandfRange(0.85f, 1.15f);
			float r = trunkR * Mathf.Lerp(0.62f, 0.34f, f);
			Branch(at, dir, len, r, 3);
		}
		// the leader's own top
		Branch(Axis(leaderTop), (Axis(leaderTop) - Axis(leaderTop - 1f)).Normalized(), height * 0.16f, trunkR * 0.14f, 2);
		// ---- the twig sprays: two crossed cards along each tip's direction, the base toward the branch
		k.Mat(Twigs);
		k.Color = new Color(tint, tint, tint);
		foreach (var (at, dir, size) in twigs)
		{
			var d = (dir + Vector3.Up * 0.25f).Normalized();
			var s1 = d.Cross(Mathf.Abs(d.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
			var s2 = d.Cross(s1).Normalized();
			foreach (var s in new[] { s1, s2 })
			{
				Vector3 b0 = at - d * size * 0.12f - s * size * 0.5f, b1 = at - d * size * 0.12f + s * size * 0.5f;
				Vector3 t0 = b0 + d * size, t1 = b1 + d * size;
				var nrm = (d.Cross(s).Normalized() * 0.3f + Vector3.Up).Normalized();
				k.Tri(b0, b1, t1, nrm, nrm, nrm, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
				k.Tri(b0, t1, t0, nrm, nrm, nrm, new Vector2(0, 1), new Vector2(1, 0), new Vector2(0, 0));
			}
		}
		return k.Commit();
	}
}
