using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Geometry for the bunker's outside (all local to the Bunker node, door facing
/// +Z, origin at the foot of the concrete face): an earth mound that blends into
/// the terrain through a sunken skirt, with grass, ferns and hanging roots; a
/// stepped concrete surround (face, coping, angled wing walls, a sloped apron);
/// the round steel frame and the vault door itself; a short dark vestibule
/// behind the doorway; lamps, a vent, and projected stains.
/// </summary>
public static class BunkerExterior
{
	public const float DoorRadius = 1.1f;
	public const float DoorCenterY = 1.25f;
	public const float DoorZ = 0.15f;
	public const float FaceZ = 0.3f;
	public const float FloorY = 0.22f;      // vestibule floor / apron top at the sill
	public const float FaceHalf = 2.62f;
	public const float CenterHalf = 1.75f;
	public const float CenterTop = 2.95f;
	public const float OuterTop = 2.6f;
	public const float VestibuleBackZ = -2.4f;
	/// <summary>Bottom of the concrete (well below grade: the ground around it is sloped).</summary>
	public const float Base = -1.3f;
	public const float WingTopA = 1.9f, WingTopB = 1.0f;
	public static float WingMidA => (WingTopA + Base) * 0.5f;
	public static float WingMidB => (WingTopB + Base) * 0.5f;
	private const float RingOuter = 1.3f, Hole = 1.12f;
	private static readonly Vector3 WingA = new(2.75f, 0, FaceZ), WingB = new(4.1f, 0, 1.7f);

	// ------------------------------------------------------------------ the mound

	/// <summary>Height of the earth above the local ground at (x, z); masked in front of the face.</summary>
	private static float MoundHeight(float x, float z, out bool masked)
	{
		masked = z > -0.35f && Mathf.Abs(x) < 2.68f + Mathf.Max(0f, z - FaceZ) * (1.35f / 1.4f);
		float rx = 6.4f, rz = 5.8f, cz = -2.6f;
		float r = Mathf.Sqrt((x / rx) * (x / rx) + ((z - cz) / rz) * ((z - cz) / rz));
		float t = Mathf.Clamp((1f - r) / 0.65f, 0f, 1f);
		float h = 3.45f * t * t * (3f - 2f * t);
		float bump = Mathf.Sin(x * 1.3f + z * 0.7f) * Mathf.Sin(z * 1.1f - x * 0.4f) + 0.5f * Mathf.Sin(x * 2.9f - z * 2.3f);
		// A finer second octave so the heap reads as lumpy piled earth rather than a smooth sheet.
		float fine = Mathf.Sin(x * 4.7f + z * 3.1f) * Mathf.Sin(z * 5.3f - x * 2.2f);
		return h + (bump * 0.2f + fine * 0.07f) * t;
	}

	/// <summary>The earth surface (local Y) at (x, z), or NaN in the cut in front of the face.</summary>
	private static float Surface(float x, float z, Func<float, float, float> terrainY, out float h, out bool masked)
	{
		h = MoundHeight(x, z, out masked);
		float t = terrainY(x, z);
		if (masked) { h = 0f; return t - 0.45f; }
		float y = t + h - 0.3f * (1f - Mathf.SmoothStep(0f, 0.45f, h));
		// Earth heaped right up to the coping behind the face, whatever the slope does, so the face's
		// back is always buried.
		float dx = Mathf.Max(0f, Mathf.Abs(x) - FaceHalf - 0.1f);
		float dz = Mathf.Max(0f, Mathf.Max(z + 0.35f, -2.8f - z));
		float d = Mathf.Sqrt(dx * dx + dz * dz);
		float plateau = Mathf.Lerp(y, CenterTop + 0.14f - d * 0.9f, 1f - Mathf.SmoothStep(2f, 3.6f, d));
		if (plateau > y) { h = Mathf.Max(h, plateau - t); y = plateau; }
		return y;
	}

	/// <summary>
	/// Builds the mound (with a skirt sunk under the terrain at its edge) and its vegetation into
	/// <paramref name="parent"/>. terrainY(x, z) gives the local ground height (0 if unknown).
	/// Returns the mound's triangles for a trimesh collider.
	/// </summary>
	public static Vector3[] BuildMound(Node3D parent, Func<float, float, float> terrainY, bool vegetation)
	{
		const float x0 = -8f, z0 = 2.5f, cell = 0.25f;
		const int nx = 64, nz = 48;   // x: -8..8, z: 2.5..-9.5
		var ys = new float[nx + 1, nz + 1];
		var hs = new float[nx + 1, nz + 1];
		var mask = new bool[nx + 1, nz + 1];
		for (int i = 0; i <= nx; i++)
			for (int j = 0; j <= nz; j++)
			{
				ys[i, j] = Surface(x0 + i * cell, z0 - j * cell, terrainY, out float h, out bool m);
				hs[i, j] = h;
				mask[i, j] = m;
			}
		Vector3 P(int i, int j) => new(x0 + i * cell, ys[i, j], z0 - j * cell);
		Vector3 N(int i, int j)
		{
			float dx = ys[Math.Min(i + 1, nx), j] - ys[Math.Max(i - 1, 0), j];
			float dz = ys[i, Math.Max(j - 1, 0)] - ys[i, Math.Min(j + 1, nz)];
			return new Vector3(-dx, 2f * cell, -dz).Normalized();
		}
		bool Skip(int i, int j)
		{
			bool allMasked = mask[i, j] && mask[i + 1, j] && mask[i, j + 1] && mask[i + 1, j + 1];
			bool allFlat = hs[i, j] < 0.01f && hs[i + 1, j] < 0.01f && hs[i, j + 1] < 0.01f && hs[i + 1, j + 1] < 0.01f;
			// Right behind the face, a cell half in the cut would hang a curtain of earth across the doorway:
			// end the mound at the coping instead.
			bool anyMasked = mask[i, j] || mask[i + 1, j] || mask[i, j + 1] || mask[i + 1, j + 1];
			float cx = x0 + (i + 0.5f) * cell, cz = z0 - (j + 0.5f) * cell;
			bool behindFace = Mathf.Abs(cx) < FaceHalf + 0.1f && cz < FaceZ;
			return allMasked || allFlat || (anyMasked && behindFace);
		}
		var mesh = BunkerKit.Grid(nx, nz, (i, j) =>
		{
			float edge = Mathf.SmoothStep(0f, 0.8f, hs[i, j]);
			float s = Mathf.Lerp(0.42f, 0.56f, edge);
			return (P(i, j), N(i, j), new Color(s, s * 0.98f, s * 0.95f));
		}, BunkerTextures.EarthMat, Skip);
		parent.AddChild(new MeshInstance3D { Name = "Mound", Mesh = mesh });

		var tris = new List<Vector3>();
		for (int i = 0; i < nx; i++)
			for (int j = 0; j < nz; j++)
			{
				if (Skip(i, j)) continue;
				tris.Add(P(i, j)); tris.Add(P(i + 1, j)); tris.Add(P(i + 1, j + 1));
				tris.Add(P(i, j)); tris.Add(P(i + 1, j + 1)); tris.Add(P(i, j + 1));
			}

		if (vegetation) BuildVegetation(parent, (x, z) =>
		{
			float y = Surface(x, z, terrainY, out _, out bool m);
			return m ? float.NaN : y;
		});
		return tris.ToArray();
	}

	private static ShaderMaterial Foliage(string key, Texture2D tex, Color tint, float sway)
		=> (ShaderMaterial)ProcTextures.Cached(key, () =>
		{
			// Same key and settings as ForestScatter's cards, so these share its material.
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
			m.SetShaderParameter("albedo_tex", tex);
			m.SetShaderParameter("tint", tint);
			m.SetShaderParameter("sway", sway);
			return m;
		});

	private const int TuftCount = 520, FernCount = 34;

	private static void BuildVegetation(Node3D parent, Func<float, float, float> surface)
	{
		var rng = new RandomNumberGenerator { Seed = 5507 };
		var k = new MeshKit();
		var grass = Foliage("grass_mat", ProcTextures.GrassTuft(), new Color(0.95f, 0.95f, 0.85f), 0.07f);
		var fern = Foliage("fern_mat", ProcTextures.Fern(), new Color(0.95f, 1f, 0.9f), 0.04f);
		int tufts = 0, ferns = 0, guard = 0;
		while ((tufts < TuftCount || ferns < FernCount) && guard++ < 8000)
		{
			float x = rng.RandfRange(-6f, 6f), z = rng.RandfRange(-8f, 1.2f);
			float y = surface(x, z);
			if (float.IsNaN(y) || MoundHeight(x, z, out _) < 0.15f) continue;
			// Keep the lip over the facade clear enough that the coping still reads.
			if (z > -0.9f && Mathf.Abs(x) < 3f && rng.Randf() < 0.6f) continue;
			bool isFern = ferns < FernCount && rng.Randf() < 0.1f;
			if (!isFern && tufts >= TuftCount) continue;
			// Tufts small and many (a carpet, not a few big paper cutouts); ferns keep their size.
			float s = isFern ? rng.RandfRange(0.7f, 1.2f) : rng.RandfRange(0.45f, 0.8f);
			k.Xf = new Transform3D(Basis.FromEuler(new Vector3(0, rng.RandfRange(0, Mathf.Tau), 0)).Scaled(Vector3.One * s), new Vector3(x, y - 0.03f, z));
			float shade = rng.RandfRange(0.45f, 0.8f);   // darker than the open-forest tufts: shaded, packed earth
			k.Color = new Color(shade, shade, shade);
			if (isFern) { FernCard(k, fern); ferns++; }
			else { GrassCard(k, grass); tufts++; }
		}
		k.Xf = Transform3D.Identity;
		k.CommitTo(parent, "MoundGrowth", false);

		// Roots and dead vines hanging over the coping from the earth above.
		var r = new MeshKit();
		r.Mat(BunkerTextures.BarkMat);
		for (int i = 0; i < 9; i++)
		{
			float x = rng.RandfRange(-CenterHalf + 0.1f, CenterHalf - 0.1f);
			if (i >= 6) x = (i % 2 == 0 ? 1 : -1) * rng.RandfRange(CenterHalf + 0.1f, FaceHalf - 0.1f);
			float top = Mathf.Abs(x) < CenterHalf ? CenterTop + 0.14f : OuterTop + 0.13f;
			var pts = new List<Vector3> { new(x, top + 0.05f, FaceZ - 0.25f), new(x, top + 0.02f, FaceZ + 0.14f) };
			Vector3 p = pts[^1];
			float len = rng.RandfRange(0.25f, 0.7f);
			for (int s = 1; s <= 4; s++)
			{
				p += new Vector3(rng.RandfRange(-0.08f, 0.08f), -len / 4f, rng.RandfRange(-0.01f, 0.03f));
				p.Z = Mathf.Max(p.Z, FaceZ + 0.03f);
				pts.Add(p);
			}
			float sh = rng.RandfRange(0.7f, 1.05f);
			r.Color = new Color(0.5f * sh, 0.44f * sh, 0.36f * sh);
			BunkerKit.Tube(r, pts, rng.RandfRange(0.025f, 0.05f), 0.01f, 5);
		}
		r.CommitTo(parent, "Roots", false);
	}

	private static void GrassCard(MeshKit k, Material mat)
	{
		k.Mat(mat);
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Pi * i / 3f;
			Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.38f;
			Vector3 n = new Vector3(-d.Z, 0.6f, d.X).Normalized();
			k.Quad(-d, d, d + new Vector3(0, 0.6f, 0), -d + new Vector3(0, 0.6f, 0), n,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		}
	}

	private static void FernCard(MeshKit k, Material mat)
	{
		k.Mat(mat);
		const int fronds = 7;
		for (int i = 0; i < fronds; i++)
		{
			float a = Mathf.Tau * i / fronds + (i % 2) * 0.3f;
			Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 side = new Vector3(-dir.Z, 0, dir.X) * 0.17f;
			float len = 0.75f + (i % 3) * 0.12f;
			Vector3 root = dir * 0.05f + new Vector3(0, 0.02f, 0);
			Vector3 mid = dir * len * 0.5f + new Vector3(0, 0.42f, 0);
			Vector3 tip = dir * len + new Vector3(0, 0.18f, 0);
			k.Quad(root - side * 0.4f, root + side * 0.4f, mid + side, mid - side, Vector3.Up,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(0, 0.5f));
			k.Quad(mid - side, mid + side, tip + side * 0.3f, tip - side * 0.3f, Vector3.Up,
				new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0), new Vector2(0, 0));
		}
	}

	// ------------------------------------------------------------------ concrete

	/// <summary>The stepped face with its round hole, coping, wing walls, apron and the vestibule.</summary>
	public static void BuildConcrete(Node3D parent, Node3D lampParent)
	{
		var k = new MeshKit();
		k.Mat(BunkerTextures.ExteriorConcreteMat);
		k.Color = Colors.White;
		// Face: star-shaped around the door centre, so sweep rays from the hole to the stepped outline.
		const int segs = 40;
		var c = new Vector2(0, DoorCenterY);
		float Exit(Vector2 d)
		{
			float RectExit(float hx, float top)
			{
				float t = float.MaxValue;
				if (d.X > 1e-5f) t = Mathf.Min(t, (hx - c.X) / d.X);
				if (d.X < -1e-5f) t = Mathf.Min(t, (-hx - c.X) / d.X);
				if (d.Y > 1e-5f) t = Mathf.Min(t, (top - c.Y) / d.Y);
				if (d.Y < -1e-5f) t = Mathf.Min(t, (Base - c.Y) / d.Y);
				return t;
			}
			return Mathf.Max(RectExit(FaceHalf, OuterTop), RectExit(CenterHalf, CenterTop));
		}
		// Extra rays exactly at the step corners keep the outline crisp.
		var angles = new List<float>();
		for (int i = 0; i < segs; i++) angles.Add(Mathf.Tau * i / segs);
		foreach (var corner in new[] { new Vector2(CenterHalf, CenterTop), new Vector2(-CenterHalf, CenterTop), new Vector2(CenterHalf, OuterTop), new Vector2(-CenterHalf, OuterTop),
			new Vector2(FaceHalf, OuterTop), new Vector2(-FaceHalf, OuterTop), new Vector2(FaceHalf, Base), new Vector2(-FaceHalf, Base) })
		{
			float a = Mathf.Atan2(corner.Y - c.Y, corner.X - c.X);
			angles.Add(a < 0 ? a + Mathf.Tau : a);
		}
		angles.Sort();
		for (int i = 0; i < angles.Count; i++)
		{
			float a0 = angles[i], a1 = angles[(i + 1) % angles.Count] + (i + 1 == angles.Count ? Mathf.Tau : 0f);
			var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
			var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
			var in0 = c + d0 * Hole; var in1 = c + d1 * Hole;
			var out0 = c + d0 * Exit(d0); var out1 = c + d1 * Exit(d1);
			// Outline corners between rays: the far point of a sector must hit the same edge; adding the
			// corner rays above guarantees each sector spans a single edge.
			k.Quad(new Vector3(in0.X, in0.Y, FaceZ), new Vector3(out0.X, out0.Y, FaceZ), new Vector3(out1.X, out1.Y, FaceZ), new Vector3(in1.X, in1.Y, FaceZ), Vector3.Back);
			const float bz = -0.2f;   // the back of the face, for the rare view from on top of the mound
			k.Quad(new Vector3(in0.X, in0.Y, bz), new Vector3(out0.X, out0.Y, bz), new Vector3(out1.X, out1.Y, bz), new Vector3(in1.X, in1.Y, bz), Vector3.Forward);
		}
		// Coping over the centre and the lower steps, and the end pilasters.
		k.Color = new Color(0.92f, 0.92f, 0.9f);
		k.Box(new Vector3(0, CenterTop + 0.065f, 0.02f), new Vector3(CenterHalf * 2f + 0.1f, 0.13f, 0.8f));
		foreach (float s in new[] { -1f, 1f })
		{
			k.Box(new Vector3(s * (CenterHalf + FaceHalf) * 0.5f + s * 0.05f, OuterTop + 0.06f, 0.02f), new Vector3(FaceHalf - CenterHalf + 0.1f, 0.12f, 0.8f));
			k.Box(new Vector3(s * CenterHalf, (OuterTop + CenterTop) * 0.5f, 0.02f), new Vector3(0.1f, CenterTop - OuterTop, 0.64f));
			k.Box(new Vector3(s * (FaceHalf + 0.06f), (OuterTop + Base) * 0.5f, -0.05f), new Vector3(0.14f, OuterTop - Base, 0.7f));
		}
		// Wing walls stepping down as they angle out, each with its coping.
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 a = new(s * WingA.X, 0, WingA.Z), b = new(s * WingB.X, 0, WingB.Z), m = a.Lerp(b, 0.5f);
			k.Color = Colors.White;
			k.Beam(a + Vector3.Up * WingMidA, m + Vector3.Up * WingMidA, 0.3f, WingTopA - Base);
			k.Beam(m + Vector3.Up * WingMidB, b + Vector3.Up * WingMidB, 0.3f, WingTopB - Base);
			k.Color = new Color(0.92f, 0.92f, 0.9f);
			k.Beam(a + Vector3.Up * 1.95f, m + Vector3.Up * 1.95f, 0.38f, 0.1f);
			k.Beam(m + Vector3.Up * 1.05f, b + Vector3.Up * 1.05f, 0.38f, 0.1f);
		}
		// Sloped apron in front of the door.
		k.Color = new Color(0.85f, 0.85f, 0.82f);
		k.Beam(ApronFrom, ApronTo, 4.8f, 1.0f);
		// The vestibule: a round concrete tube behind the doorway (inward-facing), its floor, and the dark.
		k.Color = new Color(0.55f, 0.55f, 0.53f);
		const int ts = 20;
		for (int i = 0; i < ts; i++)
		{
			float a0 = Mathf.Tau * i / ts, a1 = Mathf.Tau * (i + 1) / ts;
			Vector3 d0 = new(Mathf.Cos(a0), Mathf.Sin(a0), 0), d1 = new(Mathf.Cos(a1), Mathf.Sin(a1), 0);
			Vector3 cc = new(0, DoorCenterY, 0);
			k.Tri(cc + d0 * Hole + Vector3.Back * FaceZ, cc + d1 * Hole + Vector3.Back * FaceZ, cc + d1 * Hole + Vector3.Back * VestibuleBackZ, -d0, -d1, -d1, Vector2.Zero, Vector2.Zero, Vector2.Zero);
			k.Tri(cc + d0 * Hole + Vector3.Back * FaceZ, cc + d1 * Hole + Vector3.Back * VestibuleBackZ, cc + d0 * Hole + Vector3.Back * VestibuleBackZ, -d0, -d1, -d0, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		}
		k.Box(new Vector3(0, FloorY - 0.25f, (FaceZ + VestibuleBackZ) * 0.5f), new Vector3(1.5f, 0.5f, FaceZ - VestibuleBackZ));
		k.Mat(BunkerTextures.VoidMat);
		k.Box(new Vector3(0, DoorCenterY, VestibuleBackZ - 0.05f), new Vector3(2.4f, 2.4f, 0.1f));
		k.CommitTo(parent, "Concrete");

		BuildSteel(parent, lampParent);
		BuildStains(parent);
	}

	public static Vector3 ApronFrom => new(0, FloorY - 0.5f, FaceZ);
	public static Vector3 ApronTo => new(0, 0.02f - 0.5f, 2.6f);
	public static Vector3 WingPoint(float side, bool far) => far ? new Vector3(side * WingB.X, 0, WingB.Z) : new Vector3(side * WingA.X, 0, WingA.Z);

	private static void BuildSteel(Node3D parent, Node3D lampParent)
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.MetalMat);
		// The round frame: an annulus standing proud of the face, ringed with bolts.
		k.Color = new Color(0.52f, 0.5f, 0.46f);
		const int segs = 32;
		Vector3 c = new(0, DoorCenterY, FaceZ);
		for (int i = 0; i < segs; i++)
		{
			float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
			Vector3 d0 = new(Mathf.Cos(a0), Mathf.Sin(a0), 0), d1 = new(Mathf.Cos(a1), Mathf.Sin(a1), 0);
			Vector3 f = Vector3.Back * 0.07f;
			k.Quad(c + d0 * Hole + f, c + d0 * RingOuter + f, c + d1 * RingOuter + f, c + d1 * Hole + f, Vector3.Back);
			k.Quad(c + d0 * RingOuter, c + d0 * RingOuter + f, c + d1 * RingOuter + f, c + d1 * RingOuter, (d0 + d1).Normalized());
			k.Quad(c + d0 * Hole, c + d0 * Hole + f, c + d1 * Hole + f, c + d1 * Hole, -(d0 + d1).Normalized());
		}
		k.Color = new Color(0.4f, 0.38f, 0.35f);
		for (int i = 0; i < 20; i++)
		{
			float a = Mathf.Tau * i / 20f;
			Vector3 p = c + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * (Hole + RingOuter) * 0.5f + Vector3.Back * 0.07f;
			k.Cylinder(p, p + Vector3.Back * 0.025f, 0.025f, 0.02f, 6);
		}
		// Hinge knuckles on the left of the frame.
		k.Color = new Color(0.36f, 0.34f, 0.3f);
		foreach (float y in new[] { DoorCenterY + 0.55f, DoorCenterY - 0.55f })
			k.Cylinder(new Vector3(-DoorRadius - 0.08f, y - 0.14f, DoorZ + 0.12f), new Vector3(-DoorRadius - 0.08f, y + 0.14f, DoorZ + 0.12f), 0.07f, 0.07f, 8);
		// A louvered vent on the left step.
		Vector3 v = new(-2.18f, 1.85f, FaceZ);
		k.Color = new Color(0.45f, 0.46f, 0.42f);
		k.Box(v + Vector3.Back * 0.03f, new Vector3(0.5f, 0.42f, 0.06f));
		k.Mat(BunkerTextures.VoidMat);
		k.Box(v + Vector3.Back * 0.061f, new Vector3(0.4f, 0.32f, 0.002f));
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.4f, 0.4f, 0.37f);
		for (int i = 0; i < 5; i++)
			k.Box(v + new Vector3(0, -0.13f + i * 0.065f, 0.08f), new Vector3(0.4f, 0.02f, 0.05f), 1f, Basis.FromEuler(new Vector3(0.6f, 0, 0)));
		// Two caged lamps on arms flanking the door, still (impossibly) burning.
		foreach (float x in new[] { -1.98f, 1.98f })
		{
			k.Color = new Color(0.45f, 0.45f, 0.42f);
			k.Box(new Vector3(x, 2.3f, FaceZ + 0.02f), new Vector3(0.14f, 0.2f, 0.04f));
			k.Beam(new Vector3(x, 2.3f, FaceZ), new Vector3(x, 2.3f, FaceZ + 0.32f), 0.04f, 0.04f);
			var xf = new Transform3D(Basis.Identity, new Vector3(x, 2.3f, FaceZ + 0.32f));
			BunkerKit.CagedLamp(k, xf);
			BunkerKit.LampGlass(lampParent, xf, BunkerTextures.NewLampGlass(new Color(1f, 0.8f, 0.5f), 1.6f), x < 0 ? "LampGlassL" : "LampGlassR");
			lampParent.AddChild(new OmniLight3D
			{
				Name = x < 0 ? "LampL" : "LampR", LightColor = new Color(1f, 0.8f, 0.5f), LightEnergy = 1.6f, OmniRange = 6f,
				Position = new Vector3(x, 1.95f, FaceZ + 0.45f),
			});
		}
		k.CommitTo(parent, "Steel");
	}

	private static void BuildStains(Node3D parent)
	{
		var rng = new RandomNumberGenerator { Seed = 5508 };
		var face = new Vector3(0, 0, FaceZ);
		// Rust weeping from the frame's bolts and the lamp arms.
		for (int i = 0; i < 5; i++)
		{
			float a = Mathf.Pi * (1.15f + i * 0.17f);
			var p = new Vector3(Mathf.Cos(a) * 1.21f, DoorCenterY + Mathf.Sin(a) * 1.21f - 0.35f, FaceZ);
			BunkerKit.AddDecal(parent, BunkerTextures.RustRun(), p, Vector3.Back, Vector3.Down, new Vector2(0.18f, 0.7f), 0.3f, new Color(1, 1, 1, 0.9f));
		}
		foreach (float x in new[] { -1.98f, 1.98f })
			BunkerKit.AddDecal(parent, BunkerTextures.RustRun(), new Vector3(x, 1.75f, FaceZ), Vector3.Back, Vector3.Down, new Vector2(0.3f, 1.1f), 0.3f, new Color(1, 1, 1, 0.85f));
		// Dark water streaks from the coping and moss creeping up from the ground and down from the earth.
		for (int i = 0; i < 9; i++)
		{
			float x = rng.RandfRange(-FaceHalf + 0.2f, FaceHalf - 0.2f);
			if (Mathf.Abs(x) < 1.35f && rng.Randf() < 0.7f) x = Mathf.Sign(x + 0.001f) * rng.RandfRange(1.35f, FaceHalf - 0.2f);
			float top = Mathf.Abs(x) < CenterHalf ? CenterTop : OuterTop;
			BunkerKit.AddDecal(parent, BunkerTextures.Streak(), new Vector3(x, top - 0.8f, FaceZ), Vector3.Back, Vector3.Down,
				new Vector2(rng.RandfRange(0.4f, 0.8f), 1.7f), 0.3f, new Color(1, 1, 1, rng.RandfRange(0.7f, 1f)));
		}
		for (int i = 0; i < 7; i++)
		{
			float x = rng.RandfRange(-FaceHalf, FaceHalf);
			bool low = i < 4;
			float y = low ? rng.RandfRange(-0.1f, 0.4f) : (Mathf.Abs(x) < CenterHalf ? CenterTop : OuterTop) - rng.RandfRange(0.1f, 0.3f);
			BunkerKit.AddDecal(parent, BunkerTextures.MossPatch(), new Vector3(x, y, FaceZ), Vector3.Back, Vector3.Down,
				new Vector2(rng.RandfRange(0.6f, 1.2f), rng.RandfRange(0.5f, 0.9f)), 0.35f, new Color(1, 1, 1, 0.95f));
		}
		// Moss and grime on the wing walls and the apron.
		foreach (float s in new[] { -1f, 1f })
		{
			var a = WingPoint(s, false); var b = WingPoint(s, true);
			Vector3 along = (b - a).Normalized();
			Vector3 n = new Vector3(-along.Z, 0, along.X) * -s;   // the inner (door-side) face
			n = n.Z < 0 ? -n : n;
			BunkerKit.AddDecal(parent, BunkerTextures.MossPatch(), a.Lerp(b, 0.3f) + Vector3.Up * 0.3f + n * 0.15f, n, Vector3.Down, new Vector2(1.2f, 0.8f), 0.4f, new Color(1, 1, 1, 0.9f));
			BunkerKit.AddDecal(parent, BunkerTextures.Streak(), a.Lerp(b, 0.25f) + Vector3.Up * 1.2f + n * 0.15f, n, Vector3.Down, new Vector2(0.6f, 1.4f), 0.4f, new Color(1, 1, 1, 0.8f));
		}
		for (int i = 0; i < 3; i++)
			BunkerKit.AddDecal(parent, BunkerTextures.Blotch(), new Vector3(rng.RandfRange(-1.6f, 1.6f), 0.15f, rng.RandfRange(0.6f, 1.8f)), Vector3.Up, Vector3.Forward,
				new Vector2(1.4f, 1.2f), 0.5f, new Color(1, 1, 1, 0.75f));
		_ = face;
	}

	// ------------------------------------------------------------------ the vault door

	/// <summary>A round riveted vault door with a spoked wheel-lock at its centre, built at
	/// <paramref name="diskCenter"/> in the MeshKit's own (pre-Xf) space: the caller's Xf then
	/// places and, for the open state, swings it about its hinge edge.</summary>
	public static void VaultDoor(MeshKit dk, Vector3 diskCenter, Material metal)
	{
		const float thickness = 0.16f;
		Vector3 front = diskCenter + new Vector3(0, 0, thickness * 0.5f);
		Vector3 back = diskCenter - new Vector3(0, 0, thickness * 0.5f);

		dk.Color = new Color(0.52f, 0.5f, 0.46f);
		dk.Mat(metal);
		dk.Cylinder(back, front, DoorRadius, DoorRadius, 24, true, 1.2f);

		// Rivets ringing the rim.
		dk.Color = new Color(0.6f, 0.57f, 0.52f);
		const int rivets = 18;
		for (int i = 0; i < rivets; i++)
		{
			float a = Mathf.Tau / rivets * i;
			Vector3 p = front + new Vector3(Mathf.Cos(a) * DoorRadius * 0.86f, Mathf.Sin(a) * DoorRadius * 0.86f, 0f);
			dk.Cylinder(p, p + new Vector3(0, 0, 0.025f), 0.035f, 0.03f, 6);
		}

		// A recessed inner ring groove (the stepped face).
		dk.Color = new Color(0.36f, 0.35f, 0.32f);
		dk.Cylinder(front + new Vector3(0, 0, 0.002f), front + new Vector3(0, 0, 0.02f), DoorRadius * 0.62f, DoorRadius * 0.62f, 20, false);

		// Central hub and spoked wheel-lock.
		dk.Color = new Color(0.46f, 0.44f, 0.4f);
		Vector3 hubFront = front + new Vector3(0, 0, 0.1f);
		dk.Cylinder(front, hubFront, 0.18f, 0.15f, 10, true);
		dk.Cylinder(hubFront - new Vector3(0, 0, 0.03f), hubFront + new Vector3(0, 0, 0.03f), 0.38f, 0.38f, 16, false);
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Pi * 0.5f * i + Mathf.Pi * 0.25f;
			Vector3 dir = new(Mathf.Cos(a), Mathf.Sin(a), 0);
			var rot = Basis.FromEuler(new Vector3(0, 0, a));
			dk.Box(hubFront + dir * 0.24f, new Vector3(0.36f, 0.05f, 0.05f), 1f, rot);
		}
		// Locking dogs round the edge.
		dk.Color = new Color(0.42f, 0.4f, 0.36f);
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f + 0.26f;
			Vector3 dir = new(Mathf.Cos(a), Mathf.Sin(a), 0);
			dk.Box(front + dir * DoorRadius * 0.74f + new Vector3(0, 0, 0.03f), new Vector3(0.2f, 0.07f, 0.06f), 1f, Basis.FromEuler(new Vector3(0, 0, a)));
		}
	}
}
