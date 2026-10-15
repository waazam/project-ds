using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-15, the props' proportions pass) Turned and shaped small props for the Hollow's rooms (the cabin, the
/// station), built to real measurements instead of from boxes and single cylinders: the owner, "make sure they're
/// proportioned and not too blocky looking. For example the green lamp in the room." Each builder draws into a
/// <see cref="MeshKit"/> in a local frame (its foot at <c>at</c>, turned by <c>yaw</c>) with the materials it is
/// handed, so a room keeps its own surfaces. Round things are lathed at 10-16 sides (the PS2 budget: low, not boxy).
/// </summary>
public static class HollowProps
{
	// ------------------------------------------------------------------ helpers

	/// <summary>A turned profile (x = radius, y = height) stood at <paramref name="at"/> in the kit's current frame.</summary>
	public static void Lathe(MeshKit k, Vector3 at, IList<Vector2> profile, int sides, bool capBottom = false, bool capTop = true, float sx = 1f, float sz = 1f)
	{
		var keep = k.Xf;
		k.Xf = keep * new Transform3D(Basis.Identity, at);
		ItemMeshes.Lathe(k, profile, sides, capBottom, capTop, sx, sz);
		k.Xf = keep;
	}

	/// <summary>Runs <paramref name="draw"/> with the kit's frame moved to <paramref name="xf"/> (after its own).</summary>
	public static void In(MeshKit k, Transform3D xf, System.Action draw)
	{
		var keep = k.Xf;
		k.Xf = keep * xf;
		draw();
		k.Xf = keep;
	}

	/// <summary>A grid of points drawn as a smooth surface, its normals from the grid (pointing away from
	/// <paramref name="inside"/>, or toward it if <paramref name="flip"/>).</summary>
	private static void Sheet(MeshKit k, Vector3[,] p, System.Func<Vector3, Vector3> outward, bool flip)
	{
		int nu = p.GetLength(0), nv = p.GetLength(1);
		var n = new Vector3[nu, nv];
		for (int i = 0; i < nu; i++)
			for (int j = 0; j < nv; j++)
			{
				Vector3 du = p[Mathf.Min(i + 1, nu - 1), j] - p[Mathf.Max(i - 1, 0), j];
				Vector3 dv = p[i, Mathf.Min(j + 1, nv - 1)] - p[i, Mathf.Max(j - 1, 0)];
				Vector3 c = du.Cross(dv);
				if (c.LengthSquared() < 1e-14f) c = outward(p[i, j]);
				c = c.Normalized();
				if (c.Dot(outward(p[i, j])) < 0f) c = -c;
				n[i, j] = flip ? -c : c;
			}
		for (int i = 0; i + 1 < nu; i++)
			for (int j = 0; j + 1 < nv; j++)
			{
				var uv0 = new Vector2(i / (float)(nu - 1), j / (float)(nv - 1));
				var uv1 = new Vector2((i + 1) / (float)(nu - 1), (j + 1) / (float)(nv - 1));
				k.Tri(p[i, j], p[i + 1, j], p[i + 1, j + 1], n[i, j], n[i + 1, j], n[i + 1, j + 1], uv0, new Vector2(uv1.X, uv0.Y), uv1);
				k.Tri(p[i, j], p[i + 1, j + 1], p[i, j + 1], n[i, j], n[i + 1, j + 1], n[i, j + 1], uv0, uv1, new Vector2(uv0.X, uv1.Y));
			}
	}

	/// <summary>A turned profile's inside (a shade seen from under it): the same surface as <see cref="Lathe"/>, its faces
	/// turned in.</summary>
	public static void LatheInside(MeshKit k, Vector3 at, IList<Vector2> profile, int sides)
	{
		var p = new Vector3[profile.Count, sides + 1];
		for (int i = 0; i < profile.Count; i++)
			for (int j = 0; j <= sides; j++)
			{
				float a = Mathf.Tau * j / sides;
				p[i, j] = at + new Vector3(Mathf.Cos(a) * profile[i].X, profile[i].Y, Mathf.Sin(a) * profile[i].X);
			}
		Sheet(k, p, q => new Vector3(q.X - at.X, 0, q.Z - at.Z), true);
	}

	/// <summary>A picture frame's moulding run round an opening <paramref name="iw"/> x <paramref name="ih"/> (centred on
	/// the origin, facing +Z) with mitred corners: <paramref name="profile"/> is its section, (x = out from the opening's
	/// edge, y = height off the wall), from the sight edge to the outer edge; <paramref name="tone"/> colours each
	/// segment of it (a slip, a bead, the gilt). Faceted per segment, so its steps read crisply.</summary>
	public static void Moulding(MeshKit k, float iw, float ih, IList<Vector2> profile, System.Func<int, Color> tone = null)
	{
		var keep = k.Color;
		Vector3 C(int corner, Vector2 p)
		{
			float sx = corner == 0 || corner == 3 ? -1f : 1f, sy = corner < 2 ? -1f : 1f;
			return new Vector3(sx * (iw * 0.5f + p.X), sy * (ih * 0.5f + p.X), p.Y);
		}
		// the four sides: bottom (0-1), right (1-2), top (2-3), left (3-0), each with its outward direction
		var outs = new[] { Vector3.Down, Vector3.Right, Vector3.Up, Vector3.Left };
		for (int s = 0; s < 4; s++)
		{
			int c0 = s, c1 = (s + 1) % 4;
			for (int i = 0; i + 1 < profile.Count; i++)
			{
				if (tone != null) k.Color = tone(i);
				Vector2 a = profile[i], b = profile[i + 1];
				Vector3 n = (Vector3.Back * (b.X - a.X) - outs[s] * (b.Y - a.Y)).Normalized();
				if (n.LengthSquared() < 0.5f) continue;
				float len = Mathf.Max(iw, ih) * 3f;
				k.Quad(C(c0, a), C(c1, a), C(c1, b), C(c0, b), n, new Vector2(0, a.X * 3f), new Vector2(len, a.X * 3f), new Vector2(len, b.X * 3f), new Vector2(0, b.X * 3f));
			}
		}
		k.Color = keep;
	}

	// ------------------------------------------------------------------ the banker's lamp

	/// <summary>
	/// A green-shaded banker's lamp (real ones: about 37 cm high, the shade a 26 cm half-round of cased glass, green
	/// outside and white inside, on a brass stem over a stepped oval foot). It replaces the station desk's old lamp: a
	/// 30 x 18 cm closed green cylinder on a stick. Local frame: the foot on the desk at the origin, the shade's length
	/// along X, its open side down and tipped a little toward +Z (the sitter). Draws the brass, the glass and the dull
	/// inside into <paramref name="k"/>; the inside's glow and the bulb into <paramref name="glow"/> (the caller hangs that
	/// under the lamp's light, so it goes out with it).
	/// </summary>
	public static void BankersLamp(MeshKit k, MeshKit glow, Transform3D at, Material brass, Material shadeOut, Material shadeIn, Material lit)
	{
		// (2026-10-15) the Blender lamp, in this same frame (fixtures.py); the code below is its fallback
		if (FixtureModels.Have("fx_banker_lamp", "fx_banker_lamp_glow"))
		{
			FurnitureKit.Add(k, "fx_banker_lamp", at, new System.Collections.Generic.Dictionary<string, Material>() { ["brass"] = brass, ["shade_out"] = shadeOut, ["shade_in"] = shadeIn });
			FurnitureKit.Add(glow, "fx_banker_lamp_glow", at, new System.Collections.Generic.Dictionary<string, Material>() { ["lit"] = lit });
			return;
		}
		In(k, at, () =>
		{
			k.Mat(brass);
			k.Color = Colors.White;
			// the foot: a stepped oval, weighted, its edge rolled
			Lathe(k, Vector3.Zero, new Vector2[]
			{
				new(0.088f, 0f), new(0.09f, 0.004f), new(0.088f, 0.009f), new(0.078f, 0.013f), new(0.074f, 0.019f),
				new(0.058f, 0.024f), new(0.036f, 0.029f), new(0.022f, 0.036f), new(0.014f, 0.042f),
			}, 16, true, true, 1f, 0.74f);
			// the stem, a knop halfway up, a collar under the yoke
			Lathe(k, Vector3.Zero, new Vector2[]
			{
				new(0.0085f, 0.04f), new(0.0085f, 0.14f), new(0.013f, 0.147f), new(0.016f, 0.158f), new(0.013f, 0.169f),
				new(0.0085f, 0.175f), new(0.0085f, 0.245f), new(0.013f, 0.25f), new(0.013f, 0.258f),
			}, 10, false, true);
			// the yoke: a bar along under the shade, its ends turned up to the shade's pivots
			const float yokeY = 0.256f, pivotY = 0.296f, half = 0.112f;
			k.Cylinder(new Vector3(-half, yokeY, 0), new Vector3(half, yokeY, 0), 0.0055f, 0.0055f, 6, true);
			foreach (int s in new[] { -1, 1 })
			{
				k.Cylinder(new Vector3(s * half, yokeY - 0.004f, 0), new Vector3(s * half, pivotY, 0), 0.005f, 0.005f, 6, true);
				k.Cylinder(new Vector3(s * (half + 0.012f), pivotY, 0), new Vector3(s * (half - 0.004f), pivotY, 0), 0.008f, 0.008f, 8, true);   // the pivot's knurled nut
			}
			// the bulb's socket, under the shade
			Lathe(k, new Vector3(0, yokeY, 0), new Vector2[] { new(0.012f, 0f), new(0.014f, 0.006f), new(0.014f, 0.024f), new(0.011f, 0.03f) }, 10, true, true);
		});
		// the shade: a long half-round, its ends rounded down into quarter-domes, tipped 14 degrees toward the sitter
		var shadeXf = at * new Transform3D(new Basis(Vector3.Right, -0.24f), new Vector3(0, 0.296f, 0));
		const float L = 0.13f, ry = 0.068f, rz = 0.074f;
		float[] xs = { -L, -L + 0.004f, -L + 0.012f, -L + 0.025f, -L + 0.045f, -0.05f, 0.05f, L - 0.045f, L - 0.025f, L - 0.012f, L - 0.004f, L };
		float Taper(float x)
		{
			float d = Mathf.Max(0f, Mathf.Abs(x) - (L - 0.05f)) / 0.05f;   // 0 in the middle, 1 at the very end
			return Mathf.Sqrt(Mathf.Max(0.0f, 1f - d * d)) * 0.82f + 0.18f;
		}
		const int arc = 12;
		Vector3[,] Shell(float grow)
		{
			var p = new Vector3[xs.Length, arc + 1];
			for (int i = 0; i < xs.Length; i++)
			{
				float t = Taper(xs[i]);
				for (int j = 0; j <= arc; j++)
				{
					float a = Mathf.Pi * j / arc;   // 0 at +Z (the sitter's side), over the top, to -Z
					p[i, j] = new Vector3(xs[i] * (1f + grow * 0.6f), Mathf.Sin(a) * (ry * t + grow * ry), Mathf.Cos(a) * (rz * t + grow * rz));
				}
			}
			return p;
		}
		Vector3 Out(Vector3 q) => new Vector3(q.X * 0.6f, q.Y + 0.01f, q.Z).Normalized();
		In(k, shadeXf, () =>
		{
			k.Mat(shadeOut);
			k.Color = Colors.White;
			Sheet(k, Shell(0f), Out, false);
			// its rim: the glass's thickness, a lip round the open side
			k.Mat(shadeIn);
			var outer = Shell(0f);
			var inner = Shell(-0.06f);
			for (int i = 0; i + 1 < xs.Length; i++)
				foreach (int j in new[] { 0, arc })
					k.Quad(outer[i, j], outer[i + 1, j], inner[i + 1, j], inner[i, j], Vector3.Down);
			for (int j = 0; j < arc; j++)
				foreach (int i in new[] { 0, xs.Length - 1 })
					k.Quad(outer[i, j], outer[i, j + 1], inner[i, j + 1], inner[i, j], new Vector3(Mathf.Sign(xs[i]), 0, 0));
			// the white inside of the cased glass, dull
			Sheet(k, inner, Out, true);
			// the brass finial on top and the bands at the shade's ends
			k.Mat(brass);
			Lathe(k, new Vector3(0, ry - 0.004f, 0), new Vector2[] { new(0.011f, 0f), new(0.009f, 0.006f), new(0.004f, 0.01f), new(0.005f, 0.014f), new(0.0f, 0.019f) }, 8, false, false);
			// the pull chain from the socket, a tassel bead at its end
			k.Cylinder(new Vector3(0.03f, -0.012f, 0.005f), new Vector3(0.033f, -0.085f, 0.03f), 0.0013f, 0.0013f, 4, false);
			k.Cylinder(new Vector3(0.033f, -0.085f, 0.03f), new Vector3(0.0335f, -0.1f, 0.033f), 0.0045f, 0.003f, 6, true);
		});
		// what glows: the inside a hair inside the dull one, and the bulb
		In(glow, shadeXf, () =>
		{
			glow.Mat(lit);
			glow.Color = Colors.White;
			Sheet(glow, Shell(-0.075f), Out, true);
			Lathe(glow, new Vector3(0, -0.012f, 0), new Vector2[]
			{
				new(0.008f, -0.002f), new(0.018f, 0.004f), new(0.026f, 0.016f), new(0.027f, 0.03f), new(0.02f, 0.045f), new(0.01f, 0.05f),
			}, 10, false, true);
		});
	}

	// ------------------------------------------------------------------ the hanging oil lamp

	/// <summary>
	/// A hurricane lamp hung from a wire (real ones: 30-35 cm to the top of the cap, a 13 cm globe over a 15 cm fuel
	/// font, wire guards, two air tubes up the sides, a bail): the cabin's lamp, which was a disc, a short cylinder of
	/// glass and a cone. <paramref name="at"/> is the font's foot. Draws the tin frame into <paramref name="frame"/>; the
	/// globe into <paramref name="glassLit"/> (a lit globe and its flame, shown while the lamp burns) and into
	/// <paramref name="glassDark"/> (the same globe dark, while it doesn't). Returns the flame's height above the foot.
	/// </summary>
	public static float HangingLamp(MeshKit frame, MeshKit glassLit, MeshKit glassDark, Vector3 at, Vector3 hangFrom,
		Material tin, Material litGlass, Material darkGlass, Material flame)
	{
		var f = frame;
		f.Mat(tin);
		f.Color = Colors.White;
		// the fuel font: a squat drum, rolled at its shoulder, the burner's collar on it
		Lathe(f, at, new Vector2[]
		{
			new(0.066f, 0f), new(0.072f, 0.006f), new(0.076f, 0.022f), new(0.075f, 0.04f), new(0.066f, 0.054f), new(0.046f, 0.062f),
			new(0.03f, 0.066f), new(0.028f, 0.074f), new(0.046f, 0.078f), new(0.048f, 0.086f),
		}, 14, true, true);
		// the cap over the globe: a vented crown, the wind cap on top
		Lathe(f, at, new Vector2[]
		{
			new(0.04f, 0.228f), new(0.058f, 0.236f), new(0.062f, 0.244f), new(0.052f, 0.258f), new(0.034f, 0.266f), new(0.022f, 0.272f),
			new(0.03f, 0.278f), new(0.03f, 0.284f), new(0.012f, 0.292f),
		}, 14, true, true);
		// the air tubes up both sides, from the font round into the cap
		foreach (int s in new[] { -1, 1 })
		{
			var pts = new[]
			{
				at + new Vector3(s * 0.07f, 0.03f, 0), at + new Vector3(s * 0.092f, 0.05f, 0), at + new Vector3(s * 0.094f, 0.12f, 0),
				at + new Vector3(s * 0.094f, 0.2f, 0), at + new Vector3(s * 0.082f, 0.236f, 0), at + new Vector3(s * 0.058f, 0.248f, 0),
			};
			for (int i = 0; i + 1 < pts.Length; i++) f.Cylinder(pts[i], pts[i + 1], 0.0075f, 0.0075f, 6, i == 0 || i == pts.Length - 2);
		}
		// the guard: three wires down round the globe, two rings
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Pi * 0.25f + Mathf.Pi * 0.5f * i;
			Vector3 d = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			f.Cylinder(at + d * 0.05f + Vector3.Up * 0.088f, at + d * 0.078f + Vector3.Up * 0.14f, 0.0018f, 0.0018f, 4, false);
			f.Cylinder(at + d * 0.078f + Vector3.Up * 0.14f, at + d * 0.056f + Vector3.Up * 0.228f, 0.0018f, 0.0018f, 4, false);
		}
		ItemMeshes.Torus(f, at + Vector3.Up * 0.14f, Vector3.Up, 0.078f, 0.0022f, 16, 4);
		// the bail: a wire hoop from the cap's sides up over it, and the hanging wire from its crown
		Vector3 crown = at + Vector3.Up * 0.36f;
		var bail = new List<Vector3>();
		for (int i = 0; i <= 8; i++)
		{
			float a = Mathf.Pi * i / 8f;
			bail.Add(at + new Vector3(-Mathf.Cos(a) * 0.066f, 0.25f + Mathf.Sin(a) * 0.11f, 0));
		}
		for (int i = 0; i + 1 < bail.Count; i++) f.Cylinder(bail[i], bail[i + 1], 0.0022f, 0.0022f, 4, false);
		f.Cylinder(crown, hangFrom, 0.0035f, 0.0035f, 4, false);
		ItemMeshes.Torus(f, crown + Vector3.Down * 0.006f, Vector3.Forward, 0.009f, 0.0022f, 8, 4);
		// the wick's knob on the burner
		f.Cylinder(at + new Vector3(0, 0.072f, 0.03f), at + new Vector3(0, 0.072f, 0.05f), 0.0025f, 0.0025f, 4, false);
		f.Cylinder(at + new Vector3(0, 0.072f, 0.05f), at + new Vector3(0, 0.072f, 0.056f), 0.009f, 0.009f, 8, true);

		// the globe: a pear of glass from the burner's seat up into the cap
		var globe = new Vector2[]
		{
			new(0.042f, 0.084f), new(0.056f, 0.1f), new(0.066f, 0.124f), new(0.068f, 0.148f), new(0.062f, 0.176f), new(0.05f, 0.204f),
			new(0.042f, 0.222f), new(0.04f, 0.232f),
		};
		glassDark.Mat(darkGlass);
		glassDark.Color = Colors.White;
		Lathe(glassDark, at, globe, 14, false, false);
		glassLit.Mat(litGlass);
		glassLit.Color = Colors.White;
		Lathe(glassLit, at, globe, 14, false, false);
		// the flame: a small blade of light on the wick
		glassLit.Mat(flame);
		Lathe(glassLit, at, new Vector2[] { new(0.006f, 0.084f), new(0.011f, 0.098f), new(0.009f, 0.112f), new(0.0f, 0.13f) }, 8, true, false, 1f, 0.45f);
		return 0.14f;
	}

	// ------------------------------------------------------------------ the bare bulb

	/// <summary>An A-shaped household bulb hanging cap up (real: 6 cm across, 11 cm long), its cap's top at the origin,
	/// scaled by <paramref name="scale"/>: a mesh for a MeshInstance3D that takes its glow from MaterialOverride (it was a
	/// stretched sphere). <see cref="Socket"/> draws the bakelite lampholder above it.</summary>
	public static ArrayMesh BulbMesh(float scale = 1f)
	{
		var k = new MeshKit();
		k.Mat(new StandardMaterial3D());
		var prof = new Vector2[]
		{
			new(0.0f, -0.108f), new(0.012f, -0.106f), new(0.022f, -0.1f), new(0.029f, -0.088f), new(0.031f, -0.074f), new(0.029f, -0.058f),
			new(0.022f, -0.044f), new(0.015f, -0.032f), new(0.013f, -0.022f), new(0.013f, 0f),
		};
		for (int i = 0; i < prof.Length; i++) prof[i] *= scale;
		Lathe(k, Vector3.Zero, prof, 12, false, true);
		return k.Commit();
	}

	/// <summary>The lampholder a bare bulb hangs from: a bakelite cup and its shade ring, the flex's grip on top; its foot
	/// (where the bulb's cap goes in) at <paramref name="at"/>.</summary>
	public static void Socket(MeshKit k, Vector3 at)
	{
		Lathe(k, at, new Vector2[]
		{
			new(0.016f, -0.002f), new(0.02f, 0f), new(0.021f, 0.012f), new(0.019f, 0.045f), new(0.016f, 0.05f), new(0.008f, 0.058f),
			new(0.006f, 0.075f),
		}, 10, true, true);
	}

	/// <summary>A tin can (a coffee can, a tin of nails): rolled rims top and bottom, two beads round its side, its top
	/// open and its inside dark unless <paramref name="lidded"/>. Foot at <paramref name="at"/>.</summary>
	public static void Can(MeshKit k, Vector3 at, float r, float h, bool lidded = false)
	{
		// (2026-10-15) the Blender tin (fixtures.py, 5 cm by 12 cm), scaled to this one
		if (k.CurrentMaterial != null && FurnitureKit.Add(k, lidded ? "fx_tin_can_lidded" : "fx_tin_can",
			new Transform3D(Basis.FromScale(new Vector3(r / 0.05f, h / 0.12f, r / 0.05f)), at), new System.Collections.Generic.Dictionary<string, Material>() { ["tin"] = k.CurrentMaterial, ["inside"] = FixtureModels.Dark }))
			return;
		Lathe(k, at, new Vector2[]
		{
			new(r - 0.003f, 0f), new(r + 0.0015f, 0.003f), new(r, 0.008f), new(r, h * 0.33f - 0.004f), new(r - 0.002f, h * 0.33f), new(r, h * 0.33f + 0.004f),
			new(r, h * 0.67f - 0.004f), new(r - 0.002f, h * 0.67f), new(r, h * 0.67f + 0.004f), new(r, h - 0.006f), new(r + 0.0015f, h - 0.002f), new(r - 0.002f, h),
		}, 12, true, lidded);
		if (!lidded)
		{
			var keep = k.Color;
			k.Color = keep * 0.35f;
			LatheInside(k, at, new Vector2[] { new(r - 0.002f, h), new(r - 0.002f, h * 0.3f) }, 12);
			ItemMeshes.Disc(k, at + Vector3.Up * h * 0.3f, Vector3.Up, r - 0.002f, 12);
			k.Color = keep;
		}
	}

	// ------------------------------------------------------------------ a plain wooden chair

	/// <summary>A plain kitchen chair (real: seat 45 cm, back 90 cm, 42 cm square): an eased seat on its rails, turned
	/// front legs, back posts running up past the seat with a little rake, a bowed top rail and a mid rail, stretchers.
	/// Its back toward -Z, standing on the origin, in the kit's current material and colour.</summary>
	public static void Chair(MeshKit k)
	{
		// (2026-10-15) the Blender chair (fixtures.py), in this frame, in the kit's current material
		if (k.CurrentMaterial != null && FurnitureKit.Add(k, "fx_kitchen_chair", Transform3D.Identity, new System.Collections.Generic.Dictionary<string, Material>() { ["wood"] = k.CurrentMaterial })) return;
		const float seatY = 0.45f;
		k.BevelBox(new Vector3(0, seatY - 0.016f, 0.01f), new Vector3(0.42f, 0.032f, 0.4f), 0.01f, 1.4f);
		k.BevelBox(new Vector3(0, seatY - 0.06f, 0.01f), new Vector3(0.36f, 0.05f, 0.34f), 0.005f, 1.4f);
		var leg = new Vector2[] { new(0.016f, 0f), new(0.018f, 0.02f), new(0.015f, 0.14f), new(0.02f, 0.17f), new(0.016f, 0.2f), new(0.019f, 0.34f), new(0.02f, 0.4f) };
		foreach (float x in new[] { -0.17f, 0.17f })
		{
			Lathe(k, new Vector3(x, 0, 0.165f), leg, 8, false, false);
			k.Cylinder(new Vector3(x, 0, -0.17f), new Vector3(x, seatY, -0.175f), 0.018f, 0.019f, 8, false, 1.4f);
			k.Cylinder(new Vector3(x, seatY, -0.175f), new Vector3(x, 0.9f, -0.22f), 0.019f, 0.016f, 8, true, 1.4f);
			k.Cylinder(new Vector3(x, 0.17f, 0.165f), new Vector3(x, 0.17f, -0.17f), 0.01f, 0.01f, 6, false, 1.4f);
		}
		k.Cylinder(new Vector3(-0.17f, 0.2f, 0f), new Vector3(0.17f, 0.2f, 0f), 0.01f, 0.01f, 6, false, 1.4f);
		for (int i = 0; i < 6; i++)
		{
			float a0 = -1f + i / 3f, a1 = -1f + (i + 1) / 3f;
			Vector3 Top(float a) => new(a * 0.17f, 0.85f, -0.215f - 0.025f * (1f - a * a));
			Vector3 Mid(float a) => new(a * 0.17f, 0.66f, -0.198f - 0.02f * (1f - a * a));
			k.Beam(Top(a0), Top(a1), 0.022f, 0.07f, 1.4f, Vector3.Up);
			k.Beam(Mid(a0), Mid(a1), 0.018f, 0.04f, 1.4f, Vector3.Up);
		}
	}

	// ------------------------------------------------------------------ the kettle

	/// <summary>An enamelled kettle (real: about 18 cm high with its handle, a 20 cm belly): a domed body, a lid with
	/// its knob, a curved spout out of the shoulder, a wire bail with a wooden grip. Foot at <paramref name="at"/>, the
	/// spout toward local -X turned by <paramref name="yaw"/>.</summary>
	public static void Kettle(MeshKit k, Vector3 at, float yaw, Material enamel, Material wood)
	{
		// (2026-10-15) the Blender kettle (fixtures.py), spout to -X as here
		if (FurnitureKit.Add(k, "fx_kettle", new Transform3D(new Basis(Vector3.Up, yaw), at), new System.Collections.Generic.Dictionary<string, Material>() { ["enamel"] = enamel, ["wood"] = wood })) return;
		In(k, new Transform3D(new Basis(Vector3.Up, yaw), at), () =>
		{
			k.Mat(enamel);
			k.Color = Colors.White;
			Lathe(k, Vector3.Zero, new Vector2[]
			{
				new(0.082f, 0f), new(0.094f, 0.008f), new(0.098f, 0.03f), new(0.094f, 0.058f), new(0.078f, 0.084f), new(0.05f, 0.1f),
				new(0.044f, 0.104f), new(0.046f, 0.11f), new(0.03f, 0.118f), new(0.012f, 0.122f), new(0.009f, 0.128f), new(0.013f, 0.134f), new(0.0f, 0.138f),
			}, 14, true, false);
			// the spout: a tapering tube out of the belly, rising to its lip
			var rings = new List<ItemMeshes.Ring>();
			Vector3[] path = { new(-0.07f, 0.035f, 0), new(-0.1f, 0.055f, 0), new(-0.122f, 0.082f, 0), new(-0.138f, 0.112f, 0), new(-0.148f, 0.128f, 0) };
			for (int i = 0; i < path.Length; i++) rings.Add(new ItemMeshes.Ring(path[i], Mathf.Lerp(0.022f, 0.011f, i / (float)(path.Length - 1))));
			ItemMeshes.Loft(k, rings, 8, false, false, Vector3.Forward);
			// the bail's lugs, the wire, the grip
			foreach (int s in new[] { -1, 1 })
				k.Cylinder(new Vector3(0, 0.092f, s * 0.07f), new Vector3(0, 0.104f, s * 0.078f), 0.006f, 0.006f, 6, true);
			var wire = new List<Vector3>();
			for (int i = 0; i <= 10; i++)
			{
				float a = Mathf.Pi * i / 10f;
				wire.Add(new Vector3(0.0f, 0.104f + Mathf.Sin(a) * 0.09f, Mathf.Cos(a) * 0.078f));
			}
			for (int i = 0; i + 1 < wire.Count; i++) k.Cylinder(wire[i], wire[i + 1], 0.0025f, 0.0025f, 4, false);
			k.Mat(wood);
			k.Cylinder(new Vector3(0, 0.194f, -0.035f), new Vector3(0, 0.194f, 0.035f), 0.009f, 0.009f, 8, true);
		});
	}
}
