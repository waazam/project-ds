using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.HallwayParts;

/// <summary>
/// The janitor's closet's stock (Act 16; 2026-10-13c, the look audit: the shelves were rows of plain coloured
/// cylinders, "placeholders"). Each thing is turned on a lathe from its real profile, its rims, chimes, shoulders,
/// necks and caps:
/// <list type="bullet">
/// <item>paint cans, a gallon, a quart and a pint: a rolled chime top and bottom, a lid pressed into its ring, a wire bail
/// and its grip on the gallon; the paper label wrapped round them, paint run down it from the lid, torn back to the tin
/// at the edges; some dented; the tin dull and rusting where the label's gone;</item>
/// <item>plastic jugs (squared off, a handle at the back, a screw cap), trigger spray bottles and brown glass bottles,
/// each with a label on its front;</item>
/// <item>the mop bucket (yellow plastic, a rolled lip, a bail, grey water in it), the mop's head of strands, and a tin
/// on its side in its dried spill.</item>
/// </list>
/// The labels are one atlas drawn here (eight designs, their lettering from a 5x7 font, then aged: yellowed, water-stained
/// up from the foot, scuffed and torn), so no word on them is a real brand.
/// </summary>
public static class JanitorShelf
{
	public enum Kind { Gallon, Quart, Pint, Jug, Spray, Bottle }

	/// <summary>The width a thing takes on a shelf (m).</summary>
	public static float Footprint(Kind k) => k switch
	{
		Kind.Gallon => 0.18f, Kind.Quart => 0.12f, Kind.Pint => 0.095f, Kind.Jug => 0.165f, Kind.Spray => 0.085f, _ => 0.088f,
	};

	/// <summary>Its height (m), handle and trigger included.</summary>
	public static float Height(Kind k) => k switch
	{
		Kind.Gallon => 0.25f, Kind.Quart => 0.125f, Kind.Pint => 0.1f, Kind.Jug => 0.31f, Kind.Spray => 0.255f, _ => 0.215f,
	};

	private const int Rows = 8, AtlasW = 1024, RowH = 128;
	// the atlas's rows
	private const int RowLatex = 0, RowEnamel = 1, RowWax = 2, RowThinner = 3, RowBleach = 4, RowAmmonia = 5, RowSpray = 6, RowLye = 7;

	// ------------------------------------------------------------------ materials

	private static StandardMaterial3D _label, _tin, _plastic, _glass, _paint, _murk;

	private static StandardMaterial3D Label => _label ??= Mark(new StandardMaterial3D
	{
		ResourceName = "closet_labels", AlbedoTexture = Atlas(), Roughness = 0.78f, MetallicSpecular = 0.25f,
		VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
	});

	/// <summary>Tinplate gone dull: a soft grey sheen, rust blooming through it (the rust rough and not metal).</summary>
	private static StandardMaterial3D Tin => _tin ??= MakeTin();

	private static StandardMaterial3D MakeTin()
	{
		var (alb, rough, metal) = TinMaps();
		return Mark(new StandardMaterial3D
		{
			ResourceName = "closet_tin", AlbedoTexture = alb, Roughness = 1f, RoughnessTexture = rough, RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
			Metallic = 1f, MetallicTexture = metal, MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red, MetallicSpecular = 0.5f,
			VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		});
	}

	/// <summary>Old moulded plastic (its colour from the vertex colour): satin, scuffed, a film of grime.</summary>
	private static StandardMaterial3D Plastic => _plastic ??= Mark(new StandardMaterial3D
	{
		ResourceName = "closet_plastic", AlbedoTexture = Grime(), Roughness = 0.6f, MetallicSpecular = 0.32f,
		VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
	});

	private static StandardMaterial3D Glass => _glass ??= Mark(new StandardMaterial3D
	{
		ResourceName = "closet_amber", AlbedoTexture = Grime(), AlbedoColor = new Color(0.3f, 0.14f, 0.05f), Roughness = 0.14f, MetallicSpecular = 0.5f,
		VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
	});

	/// <summary>Dried paint (a spill, its colour from the vertex colour): a skin with a little gloss left.</summary>
	private static StandardMaterial3D Paint => _paint ??= Mark(new StandardMaterial3D
	{
		ResourceName = "closet_paint", AlbedoTexture = Grime(), Roughness = 0.42f, MetallicSpecular = 0.35f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>The bucket's grey water.</summary>
	private static StandardMaterial3D Murk => _murk ??= Mark(new StandardMaterial3D
	{
		ResourceName = "closet_murk", AlbedoColor = new Color(0.09f, 0.085f, 0.07f), Roughness = 0.08f, MetallicSpecular = 0.5f,
	});

	// (their own surfaces drawn here: the detail sweep leaves them alone)
	private static StandardMaterial3D Mark(StandardMaterial3D m) { m.SetMeta("detail_kind", -1); return m; }

	// ------------------------------------------------------------------ the lathe

	private sealed class Spec
	{
		public Vector2[] Prof;              // (radius, y) from the foot's centre, out, up the side, and in across the top
		public int Sides = 24;
		public float Sz = 1f, Sq = 2f;      // the cross-section: squashed front to back, squared off (2 is round)
		public Material Body;
		public Color BodyColor = Colors.White;
		public float CapY = 99f;            // above this: the cap
		public Color CapColor = Colors.White;
		public int LabelRings = int.MaxValue;   // the label only on the first rings (a can's outside, not its inside)
		public int Row = -1;                // the label: its row, from L0 to L1 up, LHalf either side of the front
		public float L0, L1, LHalf = Mathf.Pi, Repeats = 1f, U0;
		public readonly List<(float a, float y, float depth, float aw, float yw)> Dents = new();
	}

	private static Vector3 Pos(Spec s, float a, int i)
	{
		var p = s.Prof[i];
		float c = Mathf.Cos(a), sn = Mathf.Sin(a);
		// squared off only where it's wide (a jug's body, not its neck)
		float t = Mathf.SmoothStep(0.022f, 0.05f, p.X);
		float sf = 1f;
		if (s.Sq != 2f) sf = Mathf.Lerp(1f, Mathf.Pow(Mathf.Pow(Mathf.Abs(c), s.Sq) + Mathf.Pow(Mathf.Abs(sn), s.Sq), -1f / s.Sq), t);
		float sz = Mathf.Lerp(1f, s.Sz, t);
		float d = 1f;
		foreach (var (da, dy, depth, aw, yw) in s.Dents)
		{
			float ax = Mathf.AngleDifference(a, da) / aw, yy = (p.Y - dy) / yw;
			d -= depth * Mathf.Exp(-(ax * ax + yy * yy));
		}
		float r = p.X * sf * d;
		return new Vector3(sn * r, p.Y, -c * r * sz);   // (a = 0 is the front, local -Z)
	}

	private static void Lathe(MeshKit k, Spec s)
	{
		int n = s.Prof.Length, m = s.Sides;
		var P = new Vector3[n, m + 1];
		var N = new Vector3[n, m + 1];
		float A(int j) => -Mathf.Pi + Mathf.Tau * j / m;
		for (int i = 0; i < n; i++)
		{
			var pa = s.Prof[Mathf.Max(i - 1, 0)];
			var pb = s.Prof[Mathf.Min(i + 1, n - 1)];
			float dr = pb.X - pa.X, dy = pb.Y - pa.Y;
			for (int j = 0; j <= m; j++)
			{
				float a = A(j);
				P[i, j] = Pos(s, a, i);
				var radial = new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a));
				var guess = radial * dy - Vector3.Up * dr;
				var around = Pos(s, a + 0.01f, i) - Pos(s, a - 0.01f, i);
				var along = Pos(s, a, Mathf.Min(i + 1, n - 1)) - Pos(s, a, Mathf.Max(i - 1, 0));
				var nrm = around.LengthSquared() < 1e-12f ? guess : around.Cross(along);
				if (nrm.LengthSquared() < 1e-14f) nrm = guess;
				if (nrm.Dot(guess) < 0f) nrm = -nrm;
				N[i, j] = nrm.Normalized();
			}
		}
		var keep = k.Color;
		for (int i = 0; i < n - 1; i++)
			for (int j = 0; j < m; j++)
			{
				float ym = (s.Prof[i].Y + s.Prof[i + 1].Y) * 0.5f, am = (A(j) + A(j + 1)) * 0.5f;
				bool label = s.Row >= 0 && i < s.LabelRings && ym > s.L0 && ym < s.L1 && Mathf.Abs(am) <= s.LHalf;
				bool cap = ym > s.CapY;
				Vector2 Uv(int ii, int jj)
				{
					float a = A(jj), y = s.Prof[ii].Y;
					if (label)
					{
						float u = s.U0 + (s.LHalf - a) / (2f * s.LHalf) * s.Repeats;   // (left to right, seen from the front)
						float v = (s.Row + 0.02f + 0.96f * (1f - Mathf.Clamp((y - s.L0) / (s.L1 - s.L0), 0f, 1f))) / Rows;
						return new Vector2(u, v);
					}
					return new Vector2((a + Mathf.Pi) / Mathf.Tau * 0.6f, y * 3f + s.Prof[ii].X * 2f);
				}
				k.Mat(label ? Label : cap ? Plastic : s.Body);   // (every cap is plastic)
				k.Color = label ? Colors.White : cap ? s.CapColor : s.BodyColor;
				k.Tri(P[i, j], P[i + 1, j], P[i + 1, j + 1], N[i, j], N[i + 1, j], N[i + 1, j + 1], Uv(i, j), Uv(i + 1, j), Uv(i + 1, j + 1));
				k.Tri(P[i, j], P[i + 1, j + 1], P[i, j + 1], N[i, j], N[i + 1, j + 1], N[i, j + 1], Uv(i, j), Uv(i + 1, j + 1), Uv(i, j + 1));
			}
		k.Color = keep;
	}

	private static Vector2[] Scaled(Vector2[] prof, float sr, float sy)
	{
		var o = new Vector2[prof.Length];
		for (int i = 0; i < prof.Length; i++) o[i] = new Vector2(prof[i].X * sr, prof[i].Y * sy);
		return o;
	}

	// a paint can's profile (a gallon; the quart and the pint are it scaled): the foot's chime, the body, the top chime
	// rolled over, the lid pressed down inside it with its groove
	private static readonly Vector2[] CanProfile =
	{
		new(0f, 0.002f), new(0.074f, 0.002f), new(0.081f, 0f), new(0.0855f, 0.004f), new(0.086f, 0.009f), new(0.083f, 0.013f),
		new(0.083f, 0.017f), new(0.083f, 0.07f), new(0.083f, 0.12f), new(0.083f, 0.168f),
		new(0.083f, 0.173f), new(0.0865f, 0.179f), new(0.087f, 0.186f), new(0.0845f, 0.19f), new(0.0795f, 0.189f),
		new(0.077f, 0.184f), new(0.0715f, 0.1825f), new(0.0695f, 0.1865f), new(0.065f, 0.1865f), new(0.062f, 0.1845f), new(0f, 0.1845f),
	};
	private const float CanL0 = 0.017f, CanL1 = 0.168f;

	// ------------------------------------------------------------------ the things

	/// <summary>Puts one thing into <paramref name="k"/>, standing at <paramref name="at"/> (its foot) turned by
	/// <paramref name="yaw"/> (its front, its label, toward local -Z before the turn).</summary>
	public static void Add(MeshKit k, Kind kind, Vector3 at, float yaw, RandomNumberGenerator rng)
		=> Add(k, kind, new Transform3D(new Basis(Vector3.Up, yaw), at), rng);

	public static void Add(MeshKit k, Kind kind, Transform3D xf, RandomNumberGenerator rng)
	{
		var keepXf = k.Xf;
		k.Xf = keepXf * xf;
		float shade = rng.RandfRange(0.82f, 1f);
		switch (kind)
		{
			case Kind.Gallon:
			case Kind.Quart:
			case Kind.Pint:
			{
				float sr = kind == Kind.Gallon ? 1f : kind == Kind.Quart ? 0.66f : 0.53f;
				float sy = kind == Kind.Gallon ? 1f : kind == Kind.Quart ? 0.66f : 0.53f;
				var s = new Spec
				{
					Prof = Scaled(CanProfile, sr, sy), Sides = kind == Kind.Gallon ? 20 : 16, Body = Tin, BodyColor = Colors.White * shade,
					Row = kind == Kind.Gallon ? (rng.Randf() < 0.6f ? RowLatex : RowEnamel) : (rng.Randf() < 0.55f ? RowWax : RowThinner),
					L0 = CanL0 * sy, L1 = CanL1 * sy, U0 = rng.Randf(),
				};
				// knocked about: a dent or two in the body
				int dents = rng.Randf() < 0.45f ? rng.RandiRange(1, 2) : 0;
				for (int i = 0; i < dents; i++)
					s.Dents.Add((rng.RandfRange(-Mathf.Pi, Mathf.Pi), rng.RandfRange(0.05f, 0.14f) * sy, rng.RandfRange(0.05f, 0.1f), rng.RandfRange(0.2f, 0.4f), rng.RandfRange(0.012f, 0.025f) * sy));
				Lathe(k, s);
				if (kind == Kind.Gallon) Bail(k, 0.0875f, 0.158f, rng.RandfRange(-0.5f, 0.5f), Tin, Colors.White * 0.7f, 0.0016f, new Color(0.06f, 0.055f, 0.05f));
				break;
			}
			case Kind.Jug:
			{
				var s = new Spec
				{
					Prof = new Vector2[]
					{
						new(0f, 0.004f), new(0.064f, 0.002f), new(0.072f, 0.006f), new(0.075f, 0.016f), new(0.075f, 0.03f), new(0.075f, 0.08f),
						new(0.075f, 0.13f), new(0.075f, 0.172f), new(0.074f, 0.198f), new(0.067f, 0.222f), new(0.047f, 0.245f),
						new(0.026f, 0.259f), new(0.019f, 0.266f), new(0.019f, 0.276f), new(0.0225f, 0.277f), new(0.0225f, 0.3f),
						new(0.019f, 0.303f), new(0f, 0.303f),
					},
					Sides = 20, Sz = 0.66f, Sq = 3.6f, Body = Plastic, CapY = 0.2765f,
					Row = rng.Randf() < 0.55f ? RowBleach : RowLye, L0 = 0.03f, L1 = 0.172f, LHalf = 0.95f,
				};
				var bodies = new[] { new Color(0.84f, 0.83f, 0.78f), new Color(0.8f, 0.72f, 0.45f), new Color(0.82f, 0.82f, 0.76f) };
				var caps = new[] { new Color(0.12f, 0.24f, 0.5f), new Color(0.6f, 0.08f, 0.06f), new Color(0.75f, 0.74f, 0.7f) };
				s.BodyColor = bodies[rng.RandiRange(0, bodies.Length - 1)] * shade;
				s.CapColor = caps[rng.RandiRange(0, caps.Length - 1)];
				if (rng.Randf() < 0.3f) s.Dents.Add((rng.RandfRange(-2.5f, 2.5f), rng.RandfRange(0.06f, 0.15f), 0.06f, 0.5f, 0.03f));   // (a squeezed side)
				Lathe(k, s);
				// the handle, moulded out of the back of the shoulder
				k.Mat(Plastic);
				k.Color = s.BodyColor;
				Vector3 prev = default;
				for (int i = 0; i <= 8; i++)
				{
					float t = i / 8f;
					var p = new Vector3(0f, Mathf.Lerp(0.14f, 0.236f, t), 0.036f + 0.05f * Mathf.Sin(Mathf.Pi * t) - 0.006f * t);
					if (i > 0) k.Cylinder(prev, p, 0.0105f, 0.0105f, 7, false);
					prev = p;
				}
				break;
			}
			case Kind.Spray:
			{
				var s = new Spec
				{
					Prof = new Vector2[]
					{
						new(0f, 0.003f), new(0.031f, 0.001f), new(0.036f, 0.007f), new(0.036f, 0.03f), new(0.036f, 0.09f), new(0.036f, 0.148f),
						new(0.035f, 0.163f), new(0.026f, 0.182f), new(0.0145f, 0.193f), new(0.0145f, 0.203f), new(0.0165f, 0.2035f),
						new(0.0165f, 0.221f), new(0f, 0.221f),
					},
					Sides = 16, Sz = 0.72f, Sq = 3f, Body = Plastic, CapY = 0.2032f,
					Row = RowSpray, L0 = 0.03f, L1 = 0.148f, LHalf = 1.15f,
				};
				var bodies = new[] { new Color(0.74f, 0.76f, 0.7f), new Color(0.32f, 0.5f, 0.38f), new Color(0.7f, 0.58f, 0.32f) };
				s.BodyColor = bodies[rng.RandiRange(0, bodies.Length - 1)] * shade;
				s.CapColor = rng.Randf() < 0.6f ? new Color(0.07f, 0.07f, 0.07f) : new Color(0.62f, 0.6f, 0.55f);
				Lathe(k, s);
				// the trigger head: its body over the cap, the nozzle, the shroud down its front, the trigger
				k.Mat(Plastic);
				k.Color = s.CapColor;
				k.Box(new Vector3(0f, 0.234f, -0.012f), new Vector3(0.026f, 0.026f, 0.062f), 1f);
				k.Box(new Vector3(0f, 0.236f, -0.047f), new Vector3(0.014f, 0.014f, 0.012f), 1f);
				k.Box(new Vector3(0f, 0.218f, -0.036f), new Vector3(0.02f, 0.03f, 0.01f), 1f, new Basis(Vector3.Right, 0.15f));
				k.Box(new Vector3(0f, 0.2f, -0.038f), new Vector3(0.012f, 0.038f, 0.008f), 1f, new Basis(Vector3.Right, -0.35f));
				break;
			}
			default:
			{
				var s = new Spec
				{
					Prof = new Vector2[]
					{
						new(0f, 0.004f), new(0.034f, 0.001f), new(0.0395f, 0.006f), new(0.04f, 0.03f), new(0.04f, 0.08f), new(0.04f, 0.13f),
						new(0.039f, 0.145f), new(0.031f, 0.164f), new(0.017f, 0.179f), new(0.0128f, 0.188f), new(0.0128f, 0.197f),
						new(0.0145f, 0.1975f), new(0.0145f, 0.214f), new(0f, 0.214f),
					},
					Sides = 16, Body = Glass, BodyColor = Colors.White * shade, CapY = 0.1972f, CapColor = new Color(0.08f, 0.075f, 0.07f),
					Row = RowAmmonia, L0 = 0.03f, L1 = 0.13f, LHalf = 1.25f,
				};
				Lathe(k, s);
				break;
			}
		}
		k.Xf = keepXf;
	}

	private static StandardMaterial3D _proxy;
	/// <summary>A thing's stand-in for its shadow (an eight-sided post its size, drawn only into the shadows: the lathed
	/// things themselves cast none). Its cross-section is its footprint's, a little inside it.</summary>
	public static void ShadowProxy(MeshKit k, Kind kind, Vector3 at)
	{
		k.Mat(_proxy ??= new StandardMaterial3D { ResourceName = "closet_shadow_proxy" });
		float r = Footprint(kind) * 0.44f, h = kind == Kind.Gallon ? 0.19f : Height(kind) * 0.97f;
		k.Cylinder(at, at + Vector3.Up * h, r, kind is Kind.Jug or Kind.Spray or Kind.Bottle ? r * 0.35f : r, 8, true);
	}

	/// <summary>A wire bail: an ear each side, the wire arched over (leant by <paramref name="lean"/>), a grip at its top.</summary>
	private static void Bail(MeshKit k, float r, float earY, float lean, Material wire, Color wireColor, float wireR, Color grip)
	{
		k.Mat(wire);
		k.Color = wireColor;
		foreach (float sx in new[] { -1f, 1f })
			k.Box(new Vector3(sx * (r - 0.001f), earY, 0f), new Vector3(0.004f, 0.016f, 0.012f), 1f);
		var rot = new Basis(Vector3.Right, lean);
		Vector3 P(float th) => new Vector3(0, earY, 0) + rot * new Vector3(r * Mathf.Cos(th), r * 1.05f * Mathf.Sin(th), 0f);
		const int seg = 14;
		for (int i = 0; i < seg; i++)
			k.Cylinder(P(Mathf.Pi * i / seg), P(Mathf.Pi * (i + 1) / seg), wireR, wireR, 4, false);
		k.Mat(Plastic);
		k.Color = grip;
		var top = P(Mathf.Pi * 0.5f);
		k.Cylinder(top + rot * new Vector3(-0.024f, 0, 0), top + rot * new Vector3(0.024f, 0, 0), 0.0055f, 0.0055f, 8, true);
	}

	/// <summary>The mop bucket: yellow plastic, tapered, a rolled lip, its bail, grey water a third up.</summary>
	public static void Bucket(MeshKit k, Vector3 at, float yaw)
	{
		var keepXf = k.Xf;
		k.Xf = keepXf * new Transform3D(new Basis(Vector3.Up, yaw), at);
		var s = new Spec
		{
			Prof = new Vector2[]
			{
				new(0f, 0.004f), new(0.118f, 0.002f), new(0.127f, 0.008f), new(0.131f, 0.03f), new(0.137f, 0.12f), new(0.143f, 0.22f),
				new(0.148f, 0.285f), new(0.157f, 0.292f), new(0.159f, 0.302f), new(0.153f, 0.306f), new(0.146f, 0.298f),
				new(0.139f, 0.22f), new(0.133f, 0.12f), new(0.126f, 0.03f), new(0f, 0.03f),
			},
			Sides = 32, Body = Plastic, BodyColor = new Color(0.62f, 0.5f, 0.12f),
		};
		Lathe(k, s);
		k.Mat(Murk);
		k.Color = Colors.White;
		k.Cylinder(new Vector3(0, 0.1f, 0), new Vector3(0, 0.104f, 0), 0.134f, 0.135f, 32, true);
		Bail(k, 0.15f, 0.27f, 1.25f, Tin, Colors.White * 0.75f, 0.0022f, new Color(0.12f, 0.12f, 0.11f));
		k.Xf = keepXf;
	}

	/// <summary>A mop's head on the floor: its socket and a splay of grey strands, from <paramref name="head"/> away along
	/// <paramref name="dir"/> (flat on the floor).</summary>
	public static void MopHead(MeshKit k, Vector3 head, Vector3 dir, RandomNumberGenerator rng)
	{
		dir = new Vector3(dir.X, 0, dir.Z).Normalized();
		var side = dir.Cross(Vector3.Up).Normalized();
		k.Mat(Tin);
		k.Color = Colors.White * 0.6f;
		k.Cylinder(head - dir * 0.05f + Vector3.Up * 0.03f, head + Vector3.Up * 0.025f, 0.02f, 0.026f, 10, true);
		k.Mat(Plastic);
		// the strands: heavy grey cotton, matted together in a sodden heap, some fallen back over the socket, each one
		// kinked (four lengths, a turn at each)
		for (int i = 0; i < 130; i++)
		{
			float spread = rng.RandfRange(-0.4f, 0.4f) * (i % 5 == 0 ? 1.8f : 1f), len = rng.RandfRange(0.14f, 0.3f);
			var d = (dir * Mathf.Cos(spread) + side * Mathf.Sin(spread)).Normalized();
			if (i % 9 == 0) d = (-dir + side * rng.RandfRange(-0.6f, 0.6f)).Normalized();   // (thrown back)
			var p = head + side * rng.RandfRange(-0.02f, 0.02f) + Vector3.Up * rng.RandfRange(0.025f, 0.045f);
			k.Color = new Color(0.5f, 0.48f, 0.42f) * rng.RandfRange(0.5f, 0.85f);
			float r = rng.RandfRange(0.013f, 0.019f);
			for (int s = 1; s <= 4; s++)
			{
				float t = s / 4f;
				var q = head + d * len * t + side * rng.RandfRange(-0.035f, 0.035f) * t + dir * rng.RandfRange(-0.02f, 0.02f);
				q.Y = head.Y + r + Mathf.Lerp(0.022f, 0f, Mathf.Sqrt(t)) + rng.RandfRange(0f, 0.02f) * (1f - t);   // (lying on the floor, not in it)
				k.Cylinder(p, q, r, r * 0.95f, 5, s == 4);
				p = q;
			}
		}
	}

	/// <summary>A tin on its side on the floor (lid off, rolled to <paramref name="yaw"/>) in the spill it left, dried.</summary>
	public static void Spilled(MeshKit k, Vector3 at, float yaw, Color paint, RandomNumberGenerator rng)
	{
		// the spill: a lobed puddle, thin, its edge ragged
		k.Mat(Paint);
		k.Color = paint;
		var dir = new Basis(Vector3.Up, yaw) * Vector3.Forward;
		var c = at + dir * 0.2f + Vector3.Up * 0.003f;
		const int seg = 28;
		float[] rad = new float[seg];
		for (int i = 0; i < seg; i++) rad[i] = 0.19f * (0.75f + 0.35f * Mathf.Sin(i * 0.9f + 1.3f) * Mathf.Sin(i * 0.37f)) + rng.RandfRange(-0.015f, 0.015f);
		for (int i = 0; i < seg; i++)
		{
			float a0 = Mathf.Tau * i / seg, a1 = Mathf.Tau * (i + 1) / seg;
			var p0 = c + new Vector3(Mathf.Cos(a0) * rad[i] * 1.4f, 0, Mathf.Sin(a0) * rad[i]);
			var p1 = c + new Vector3(Mathf.Cos(a1) * rad[(i + 1) % seg] * 1.4f, 0, Mathf.Sin(a1) * rad[(i + 1) % seg]);
			k.Tri(c, p0, p1, Vector3.Up, new Vector2(0.5f, 0.5f), new Vector2(p0.X, p0.Z), new Vector2(p1.X, p1.Z));
		}
		// the tin, lying along the spill's way, its mouth toward it
		float sr = 0.66f;
		var xf = new Transform3D(new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, -Mathf.Pi * 0.5f), at + Vector3.Up * (0.087f * sr));
		var keepXf = k.Xf;
		k.Xf = keepXf * xf;
		// (its own profile: no lid, the inside of it dark and its rim crusted with the paint)
		var prof = Scaled(CanProfile[..15], sr, sr);
		var open = new List<Vector2>(prof) { new(0.079f * sr, 0.17f * sr), new(0.08f * sr, 0.02f * sr), new(0f, 0.02f * sr) };
		var s = new Spec { Prof = open.ToArray(), Sides = 22, Body = Tin, BodyColor = Colors.White * 0.85f, Row = RowLatex, L0 = CanL0 * sr, L1 = CanL1 * sr, U0 = 0.3f, LabelRings = 14 };
		s.Dents.Add((0.6f, 0.07f * sr, 0.08f, 0.35f, 0.02f));
		Lathe(k, s);
		k.Xf = keepXf;
	}

	// ------------------------------------------------------------------ the textures

	private static float Hash(int x, int y, int seed)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}

	// value noise, wrapping every `period` cells across (the atlas wraps round a can)
	private static float Noise(float x, float y, int period, int seed)
	{
		int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
		float fx = x - x0, fy = y - y0;
		fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
		int W(int v) => ((v % period) + period) % period;
		float a = Hash(W(x0), y0, seed), b = Hash(W(x0 + 1), y0, seed);
		float c = Hash(W(x0), y0 + 1, seed), d = Hash(W(x0 + 1), y0 + 1, seed);
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
	}

	private static float Fbm(float u, float v, int w, int cells, int oct, int seed)
	{
		float s = 0, a = 0.5f, n = 0;
		for (int o = 0; o < oct; o++)
		{
			int p = cells << o;
			s += a * Noise(u / w * p, v / w * p, p, seed + o * 17);
			n += a; a *= 0.5f;
		}
		return s / n;
	}

	private static ImageTexture FromBuffer(string key, int w, int h, Color[] px)
	{
		var t0 = Systems.TexGen.Start();
		Image img = Systems.TexCache.Load("JanitorShelf_" + key);
		if (img == null)
		{
			var data = new byte[w * h * 4];
			for (int i = 0; i < px.Length; i++)
			{
				var c = px[i].Clamp();
				data[i * 4] = (byte)(c.R * 255f); data[i * 4 + 1] = (byte)(c.G * 255f); data[i * 4 + 2] = (byte)(c.B * 255f); data[i * 4 + 3] = 255;
			}
			img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
			img.GenerateMipmaps();
			Systems.TexCache.Save("JanitorShelf_" + key, img);
		}
		Systems.TexGen.Stop(t0);
		return ImageTexture.CreateFromImage(img);
	}

	private static ImageTexture Cached(string key) => Systems.TexCache.Load("JanitorShelf_" + key) is { } img ? ImageTexture.CreateFromImage(img) : null;

	private static Texture2D _grime;
	/// <summary>A film of grime (near white: the colour comes from the vertex colour).</summary>
	private static Texture2D Grime()
	{
		if (_grime != null) return _grime;
		if (Cached("grime") is { } c) return _grime = c;
		const int w = 128;
		var px = new Color[w * w];
		for (int y = 0; y < w; y++)
			for (int x = 0; x < w; x++)
			{
				float g = 0.86f + 0.12f * Fbm(x, y, w, 4, 4, 911) - 0.1f * Mathf.Max(0f, Fbm(x, y, w, 8, 2, 913) - 0.6f) * 3f;
				if (Hash(x, y, 917) > 0.985f) g -= 0.12f;
				px[y * w + x] = new Color(g, g * 0.985f, g * 0.95f);
			}
		return _grime = FromBuffer("grime", w, w, px);
	}

	private static (Texture2D alb, Texture2D rough, Texture2D metal) _tinMaps;
	private static (Texture2D, Texture2D, Texture2D) TinMaps()
	{
		if (_tinMaps.alb != null) return _tinMaps;
		const int w = 256;
		if (Cached("tin_a") is { } ca && Cached("tin_r") is { } cr && Cached("tin_m") is { } cm) return _tinMaps = (ca, cr, cm);
		var a = new Color[w * w];
		var r = new Color[w * w];
		var m = new Color[w * w];
		for (int y = 0; y < w; y++)
			for (int x = 0; x < w; x++)
			{
				// dull tinplate: fine streaks round the can, a grey film over it
				float brushed = Noise(x / 32f, y * 0.7f, w / 32, 701) * 0.06f;
				float film = Fbm(x, y, w, 4, 4, 703);
				float g = 0.5f + brushed + 0.08f * (film - 0.5f);
				var col = new Color(g, g * 0.99f, g * 0.97f);
				// rust: blooms through it, darker at their hearts, pitted
				float rust = Mathf.Clamp((Fbm(x, y, w, 6, 4, 707) - 0.56f) * 6f, 0f, 1f);
				rust = Mathf.Max(rust, Hash(x, y, 709) > 0.992f ? 0.8f : 0f);
				var rc = new Color(0.36f, 0.17f, 0.07f).Lerp(new Color(0.2f, 0.1f, 0.05f), Fbm(x, y, w, 16, 2, 711));
				col = col.Lerp(rc, rust);
				a[y * w + x] = col;
				float rough = Mathf.Lerp(0.52f + 0.2f * film, 0.92f, rust);
				r[y * w + x] = new Color(rough, rough, rough);
				float met = Mathf.Lerp(0.45f, 0f, rust);
				m[y * w + x] = new Color(met, met, met);
			}
		return _tinMaps = (FromBuffer("tin_a", w, w, a), FromBuffer("tin_r", w, w, r), FromBuffer("tin_m", w, w, m));
	}

	// ---- the labels

	// a 5x7 font, only the capitals and the figures the labels use
	private static readonly Dictionary<char, string[]> Glyphs = new()
	{
		['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
		['B'] = new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " },
		['C'] = new[] { " ####", "#    ", "#    ", "#    ", "#    ", "#    ", " ####" },
		['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
		['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
		['F'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " },
		['G'] = new[] { " ####", "#    ", "#    ", "#  ##", "#   #", "#   #", " ####" },
		['H'] = new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
		['I'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "#####" },
		['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
		['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
		['M'] = new[] { "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #" },
		['N'] = new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" },
		['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
		['P'] = new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " },
		['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
		['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
		['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
		['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
		['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
		['V'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " },
		['W'] = new[] { "#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #" },
		['X'] = new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" },
		['Y'] = new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " },
		['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
		['!'] = new[] { "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "     ", "  #  " },
		['.'] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "  #  " },
		['-'] = new[] { "     ", "     ", "     ", "#####", "     ", "     ", "     " },
	};

	private sealed class Canvas
	{
		public readonly Color[] Px = new Color[AtlasW * RowH * Rows];
		public void Rect(int x0, int y0, int w, int h, Color c)
		{
			for (int y = Math.Max(0, y0); y < Math.Min(RowH * Rows, y0 + h); y++)
				for (int x = x0; x < x0 + w; x++)
					Px[y * AtlasW + ((x % AtlasW) + AtlasW) % AtlasW] = c;
		}
		public int TextWidth(string s, int scale) => s.Length * 6 * scale - scale;
		/// <summary>Capitals at (x, y) top left, each font pixel <paramref name="scale"/> square.</summary>
		public void Text(string s, int x, int y, int scale, Color c)
		{
			foreach (char ch in s)
			{
				if (Glyphs.TryGetValue(ch, out var g))
					for (int gy = 0; gy < 7; gy++)
						for (int gx = 0; gx < 5; gx++)
							if (g[gy][gx] == '#') Rect(x + gx * scale, y + gy * scale, scale, scale, c);
				x += 6 * scale;
			}
		}
		public void Centred(string s, int cx, int y, int scale, Color c) => Text(s, cx - TextWidth(s, scale) / 2, y, scale, c);
		/// <summary>Fine print: lines of little dashes (unreadable at any distance it's seen from).</summary>
		public void Print(int x0, int y0, int w, int lines, Color c, int seed)
		{
			for (int l = 0; l < lines; l++)
			{
				int x = x0, end = x0 + (int)(w * (l == lines - 1 ? 0.55f : 0.95f));
				while (x < end)
				{
					int word = 4 + (int)(Hash(x, l, seed) * 14f);
					Rect(x, y0 + l * 5, Math.Min(word, end - x), 2, c);
					x += word + 3;
				}
			}
		}
		/// <summary>A hazard diamond (a square on its point).</summary>
		public void Diamond(int cx, int cy, int r, Color edge, Color fill)
		{
			for (int y = -r; y <= r; y++)
				for (int x = -r; x <= r; x++)
				{
					int d = Math.Abs(x) + Math.Abs(y);
					if (d <= r) Rect(cx + x, cy + y, 1, 1, d > r - 3 ? edge : fill);
				}
		}
	}

	private static Texture2D _atlas;
	private static Texture2D Atlas()
	{
		if (_atlas != null) return _atlas;
		if (Cached("labels") is { } cached) return _atlas = cached;
		var cv = new Canvas();
		var ink = new Color(0.07f, 0.06f, 0.06f);
		var cream = new Color(0.86f, 0.82f, 0.7f);
		var drips = new Dictionary<int, Color>();
		for (int row = 0; row < Rows; row++)
		{
			int y0 = row * RowH;
			switch (row)
			{
				case RowLatex:   // a gallon of house paint: cream, the colour's band, two panels round the can
				{
					var band = new Color(0.45f, 0.12f, 0.08f);
					cv.Rect(0, y0, AtlasW, RowH, cream);
					cv.Rect(0, y0 + 50, AtlasW, 44, band);
					cv.Rect(0, y0 + 46, AtlasW, 3, ink);
					cv.Rect(0, y0 + 96, AtlasW, 3, ink);
					for (int p = 0; p < 2; p++)
					{
						int cx = p * 512 + 256;
						cv.Centred("LATEX", cx, y0 + 12, 4, ink);
						cv.Centred("BARN RED", cx, y0 + 58, 4, cream);
						cv.Centred("FLAT - 1 GAL.", cx, y0 + 104, 2, ink);
						cv.Print(p * 512 + 30, y0 + 14, 110, 5, ink, 31 + p);
						cv.Print(p * 512 + 372, y0 + 14, 110, 5, ink, 37 + p);
					}
					drips[row] = band;
					break;
				}
				case RowEnamel:   // a gallon of enamel: dark green, a gold rule
				{
					var green = new Color(0.1f, 0.22f, 0.14f);
					var gold = new Color(0.72f, 0.58f, 0.26f);
					cv.Rect(0, y0, AtlasW, RowH, green);
					cv.Rect(0, y0 + 8, AtlasW, 3, gold);
					cv.Rect(0, y0 + 116, AtlasW, 3, gold);
					for (int p = 0; p < 2; p++)
					{
						int cx = p * 512 + 256;
						cv.Centred("ENAMEL", cx, y0 + 26, 5, cream);
						cv.Centred("GLOSS GREY", cx, y0 + 74, 3, gold);
						cv.Rect(cx - 80, y0 + 100, 160, 2, gold);
						cv.Print(p * 512 + 24, y0 + 30, 120, 6, gold, 41 + p);
						cv.Print(p * 512 + 370, y0 + 30, 120, 6, gold, 43 + p);
					}
					drips[row] = new Color(0.4f, 0.41f, 0.4f);
					break;
				}
				case RowWax:   // a quart of floor wax: white over red
				{
					var red = new Color(0.55f, 0.08f, 0.06f);
					cv.Rect(0, y0, AtlasW, RowH, new Color(0.84f, 0.82f, 0.76f));
					cv.Rect(0, y0 + 70, AtlasW, RowH - 70, red);
					for (int p = 0; p < 2; p++)
					{
						int cx = p * 512 + 256;
						cv.Centred("FLOOR WAX", cx, y0 + 18, 4, red);
						cv.Centred("PASTE - 1 QT.", cx, y0 + 84, 3, cream);
						cv.Print(cx - 100, y0 + 54, 200, 2, ink, 51 + p);
					}
					break;
				}
				case RowThinner:   // thinner: yellow, black lettering, a hazard diamond
				{
					var yellow = new Color(0.78f, 0.62f, 0.12f);
					cv.Rect(0, y0, AtlasW, RowH, yellow);
					cv.Rect(0, y0 + 6, AtlasW, 6, ink);
					cv.Rect(0, y0 + 116, AtlasW, 6, ink);
					for (int p = 0; p < 2; p++)
					{
						int cx = p * 512 + 256;
						cv.Centred("THINNER", cx - 40, y0 + 26, 4, ink);
						cv.Centred("DANGER", cx - 40, y0 + 70, 3, ink);
						cv.Diamond(cx + 150, y0 + 62, 34, ink, new Color(0.62f, 0.12f, 0.07f));
						cv.Print(cx - 150, y0 + 98, 200, 3, ink, 61 + p);
					}
					break;
				}
				case RowBleach:   // a jug's front: white, blue lettering, a blue wave
				{
					var blue = new Color(0.1f, 0.2f, 0.48f);
					cv.Rect(0, y0, AtlasW, RowH, new Color(0.88f, 0.88f, 0.84f));
					for (int x = 0; x < AtlasW; x++)
					{
						int wy = (int)(86 + 7 * Mathf.Sin(x * 0.012f));
						cv.Rect(x, y0 + wy, 1, RowH - wy, blue);
					}
					cv.Centred("BLEACH", 512, y0 + 16, 8, blue);
					cv.Centred("REGULAR", 512, y0 + 96, 3, new Color(0.88f, 0.88f, 0.84f));
					cv.Print(120, y0 + 90, 200, 4, new Color(0.88f, 0.88f, 0.84f), 71);
					cv.Print(700, y0 + 90, 200, 4, new Color(0.88f, 0.88f, 0.84f), 73);
					break;
				}
				case RowAmmonia:   // a brown bottle's label: pale green, black
				{
					cv.Rect(0, y0, AtlasW, RowH, new Color(0.62f, 0.7f, 0.52f));
					cv.Rect(30, y0 + 8, AtlasW - 60, 3, ink);
					cv.Rect(30, y0 + 117, AtlasW - 60, 3, ink);
					cv.Centred("AMMONIA", 512, y0 + 24, 7, ink);
					cv.Centred("HOUSEHOLD", 512, y0 + 82, 3, ink);
					cv.Print(80, y0 + 100, 860, 2, ink, 81);
					break;
				}
				case RowSpray:   // a spray bottle's: orange and white
				{
					var orange = new Color(0.78f, 0.36f, 0.08f);
					cv.Rect(0, y0, AtlasW, RowH, new Color(0.86f, 0.85f, 0.8f));
					cv.Rect(0, y0, AtlasW, 52, orange);
					cv.Centred("CLEANER", 512, y0 + 10, 5, new Color(0.95f, 0.92f, 0.85f));
					cv.Centred("GLASS - TILE", 512, y0 + 62, 3, orange);
					cv.Print(140, y0 + 92, 740, 5, ink, 91);
					break;
				}
				default:   // RowLye: a jug of drain opener: red, a black band, a hazard diamond
				{
					var red = new Color(0.6f, 0.07f, 0.05f);
					cv.Rect(0, y0, AtlasW, RowH, red);
					cv.Rect(0, y0 + 74, AtlasW, 30, ink);
					cv.Centred("LYE", 400, y0 + 14, 8, cream);
					cv.Centred("DRAIN OPENER", 512, y0 + 80, 3, new Color(0.85f, 0.78f, 0.3f));
					cv.Diamond(780, y0 + 38, 30, ink, new Color(0.85f, 0.78f, 0.3f));
					cv.Print(120, y0 + 108, 780, 3, cream, 101);
					break;
				}
			}
		}
		// paint run down from the lid: drips of the paint's own colour, fat where they stopped
		foreach (var (row, col) in drips)
		{
			int y0 = row * RowH;
			var rng = new RandomNumberGenerator { Seed = (ulong)(5100 + row) };
			for (int i = 0; i < 22; i++)
			{
				int x = rng.RandiRange(0, AtlasW - 1), w = rng.RandiRange(6, 16), len = rng.RandiRange(6, i < 6 ? 90 : 40);
				var c = col * rng.RandfRange(0.85f, 1.05f);
				cv.Rect(x - w, y0, w * 3, 5, c);   // (the lip's smear)
				for (int y = 0; y < len; y++)
				{
					int ww = Math.Max(2, (int)(w * (1f - 0.5f * y / (float)len)));
					cv.Rect(x - ww / 2, y0 + y, ww, 1, c);
				}
				cv.Rect(x - w / 2 - 1, y0 + len - 2, w + 2, Math.Max(3, w - 2), c);   // the bead it stopped in
			}
		}
		// the age on all of them: yellowed, faded, water-stained up from the foot, scuffed; torn on the tins, the tin
		// under the tear
		var px = cv.Px;
		for (int row = 0; row < Rows; row++)
		{
			bool tinUnder = row <= RowThinner;
			for (int y = 0; y < RowH; y++)
				for (int x = 0; x < AtlasW; x++)
				{
					int gy = row * RowH + y, i = gy * AtlasW + x;
					var c = px[i];
					float fade = Fbm(x, gy, AtlasW, 6, 3, 1201 + row);
					c = c.Lerp(new Color(0.78f, 0.72f, 0.58f), 0.12f + 0.16f * fade);   // the paper yellowed and the ink faded
					c *= new Color(0.95f, 0.91f, 0.8f);
					// a tide line of old wet up from the foot, ragged
					float tide = RowH - 18f - 34f * Fbm(x, row * 3, AtlasW, 10, 3, 1301 + row);
					if (y > tide) c *= y < tide + 3 ? new Color(0.58f, 0.5f, 0.38f) : new Color(0.8f, 0.72f, 0.58f);
					// grimy blotches and finger marks
					float blot = Fbm(x, gy, AtlasW, 12, 3, 1401 + row);
					if (blot > 0.6f) c *= 1f - (blot - 0.6f) * 1.3f;
					// scuffs: short pale scratches
					if (Hash(x / 3, gy, 1501) > 0.9975f) c = c.Lerp(new Color(0.8f, 0.78f, 0.72f), 0.5f);
					c *= 0.95f + 0.07f * Hash(x, gy, 1601);
					// torn away at the top and the foot of a tin's label: the tin under it
					if (tinUnder)
					{
						float edge = Mathf.Min(y, RowH - 1 - y);
						float tear = Fbm(x, row, AtlasW, 14, 3, 1701 + row) * 40f - 14f;
						if (edge < tear)
						{
							float rust = Fbm(x, gy, AtlasW, 20, 2, 1801);
							c = new Color(0.42f, 0.41f, 0.4f).Lerp(new Color(0.3f, 0.15f, 0.07f), Mathf.Clamp((rust - 0.45f) * 3f, 0f, 1f));
						}
						else if (edge < tear + 2f) c = new Color(0.82f, 0.78f, 0.68f);   // (the paper's torn white edge)
					}
					px[i] = c;
				}
		}
		return _atlas = FromBuffer("labels", AtlasW, RowH * Rows, px);
	}
}
