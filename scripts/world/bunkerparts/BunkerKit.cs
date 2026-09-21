using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Building blocks shared by the bunker's pieces: pipes and sagging cables,
/// tapered boxes, caged lamps, bulged CRT faces and whole CRT sets, and
/// projected decals. All geometry goes into a caller's <see cref="MeshKit"/>,
/// in that kit's current space.
/// </summary>
public static class BunkerKit
{
	/// <summary>A tube along a polyline, radius r0 at the start to r1 at the end.</summary>
	public static void Tube(MeshKit k, IList<Vector3> pts, float r0, float r1, int sides = 5)
	{
		float total = 0f;
		for (int i = 0; i < pts.Count - 1; i++) total += pts[i].DistanceTo(pts[i + 1]);
		float run = 0f;
		for (int i = 0; i < pts.Count - 1; i++)
		{
			float seg = pts[i].DistanceTo(pts[i + 1]);
			if (seg < 1e-4f) continue;
			float ra = Mathf.Lerp(r0, r1, run / total), rb = Mathf.Lerp(r0, r1, (run + seg) / total);
			// Extend each piece a hair into the next so bends don't open gaps.
			Vector3 dir = (pts[i + 1] - pts[i]) / seg;
			k.Cylinder(pts[i] - dir * ra * 0.5f, pts[i + 1], ra, rb, sides, false);
			run += seg;
		}
	}

	/// <summary>A cable hanging between two points with a parabolic sag.</summary>
	public static void Cable(MeshKit k, Vector3 a, Vector3 b, float sag, float r, int segs = 6)
	{
		var pts = new List<Vector3>();
		for (int i = 0; i <= segs; i++)
		{
			float t = (float)i / segs;
			pts.Add(a.Lerp(b, t) + Vector3.Down * sag * 4f * t * (1f - t));
		}
		Tube(k, pts, r, r, 4);
	}

	/// <summary>A tapered box from a front rectangle to a back rectangle (both facing along the axis).</summary>
	public static void Frustum(MeshKit k, Vector3 fc, Vector2 fs, Vector3 bc, Vector2 bs)
	{
		Vector3 F(float sx, float sy) => fc + new Vector3(sx * fs.X * 0.5f, sy * fs.Y * 0.5f, 0);
		Vector3 B(float sx, float sy) => bc + new Vector3(sx * bs.X * 0.5f, sy * bs.Y * 0.5f, 0);
		Vector3 mid = (fc + bc) * 0.5f;
		void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
		{
			Vector3 n = (b - a).Cross(d - a).Normalized();
			if (n.Dot((a + b + c + d) * 0.25f - mid) < 0) n = -n;
			float u = a.DistanceTo(b), v = a.DistanceTo(d);
			k.Quad(a, b, c, d, n, new Vector2(0, v), new Vector2(u, v), new Vector2(u, 0), Vector2.Zero);
		}
		Face(F(-1, 1), F(1, 1), B(1, 1), B(-1, 1));       // top
		Face(F(-1, -1), F(1, -1), B(1, -1), B(-1, -1));   // bottom
		Face(F(-1, -1), F(-1, 1), B(-1, 1), B(-1, -1));   // left
		Face(F(1, -1), F(1, 1), B(1, 1), B(1, -1));       // right
		Face(B(-1, -1), B(1, -1), B(1, 1), B(-1, 1));     // back
	}

	/// <summary>
	/// A slightly bulged tube face (grid), centred at the origin of <paramref name="xf"/>, facing +Z.
	/// UV 0..1 over the picture (v down). Vertex colour = <paramref name="c"/> (the CRT shader reads
	/// r as a per-screen seed and g as the atlas cell).
	/// </summary>
	public static void BulgeScreen(MeshKit k, Transform3D xf, float w, float h, float bulge, Color c, int nx = 4, int ny = 3)
	{
		var old = k.Xf;
		var oldColor = k.Color;
		k.Xf = old * xf;
		k.Color = c;
		Vector3 P(int i, int j, out Vector3 n)
		{
			float u = (float)i / nx * 2f - 1f, v = (float)j / ny * 2f - 1f;
			float z = bulge * (1f - 0.55f * u * u - 0.55f * v * v);
			n = new Vector3(bulge * 1.1f * u / (w * 0.5f), bulge * 1.1f * v / (h * 0.5f), 1f).Normalized();
			return new Vector3(u * w * 0.5f, v * h * 0.5f, z);
		}
		Vector2 U(int i, int j) => new((float)i / nx, 1f - (float)j / ny);
		for (int i = 0; i < nx; i++)
			for (int j = 0; j < ny; j++)
			{
				var a = P(i, j, out var na); var b = P(i + 1, j, out var nb);
				var cc = P(i + 1, j + 1, out var nc); var d = P(i, j + 1, out var nd);
				k.Tri(a, b, cc, na, nb, nc, U(i, j), U(i + 1, j), U(i + 1, j + 1));
				k.Tri(a, cc, d, na, nc, nd, U(i, j), U(i + 1, j + 1), U(i, j + 1));
			}
		k.Xf = old;
		k.Color = oldColor;
	}

	/// <summary>
	/// A caged work lamp hanging along -Y from a mount at the origin of <paramref name="xf"/> (plate,
	/// stem, flared housing, wire cage). The glass bulb is separate (see <see cref="LampGlass"/>) and sits
	/// at xf * (0, -0.27, 0).
	/// </summary>
	public static void CagedLamp(MeshKit metal, Transform3D xf)
	{
		var old = metal.Xf;
		metal.Xf = old * xf;
		metal.Color = new Color(0.55f, 0.55f, 0.52f);
		metal.Box(new Vector3(0, -0.015f, 0), new Vector3(0.2f, 0.03f, 0.2f));
		metal.Cylinder(new Vector3(0, -0.02f, 0), new Vector3(0, -0.13f, 0), 0.018f, 0.018f, 6);
		metal.Color = new Color(0.4f, 0.42f, 0.4f);
		metal.Cylinder(new Vector3(0, -0.12f, 0), new Vector3(0, -0.2f, 0), 0.06f, 0.13f, 8, true);
		// Cage: four bent wires and a bottom ring.
		metal.Color = new Color(0.3f, 0.3f, 0.28f);
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Tau * i / 4f + 0.4f;
			Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 p0 = dir * 0.12f + new Vector3(0, -0.2f, 0);
			Vector3 p1 = dir * 0.105f + new Vector3(0, -0.32f, 0);
			Vector3 p2 = dir * 0.03f + new Vector3(0, -0.39f, 0);
			metal.Beam(p0, p1, 0.012f, 0.012f);
			metal.Beam(p1, p2, 0.012f, 0.012f);
		}
		for (int i = 0; i < 8; i++)
		{
			float a0 = Mathf.Tau * i / 8f, a1 = Mathf.Tau * (i + 1) / 8f;
			metal.Beam(new Vector3(Mathf.Cos(a0) * 0.105f, -0.32f, Mathf.Sin(a0) * 0.105f),
				new Vector3(Mathf.Cos(a1) * 0.105f, -0.32f, Mathf.Sin(a1) * 0.105f), 0.01f, 0.01f);
		}
		metal.Xf = old;
	}

	/// <summary>The glowing bulb of a caged lamp, with its own material (so it can flicker alone).</summary>
	public static MeshInstance3D LampGlass(Node3D parent, Transform3D xf, StandardMaterial3D mat, string name = "Glass")
	{
		var k = new MeshKit();
		k.Mat(mat);
		k.Cylinder(new Vector3(0, -0.19f, 0), new Vector3(0, -0.31f, 0), 0.07f, 0.06f, 8, true);
		k.Blob(new Vector3(0, -0.31f, 0), new Vector3(0.06f, 0.035f, 0.06f), 17, 0f, false);
		var mi = k.CommitTo(parent, name, false);
		mi.Transform = xf;
		return mi;
	}

	public struct CrtSpec
	{
		public float W, H, D;
		public bool KnobsRight;     // TV style (knobs beside the tube) vs monitor style (knobs under it)
		public Color Body;
	}

	private static readonly Color[] CrtColours =
	{
		new(0.46f, 0.42f, 0.33f),   // yellowed beige
		new(0.32f, 0.32f, 0.31f),  // office grey
		new(0.13f, 0.13f, 0.13f),  // black
		new(0.3f, 0.2f, 0.12f),    // woodgrain-brown TV
		new(0.38f, 0.36f, 0.31f),
	};

	/// <summary>A random CRT from a small family of sizes and styles.</summary>
	public static CrtSpec RandomCrt(RandomNumberGenerator rng, int sizeClass)
	{
		(float w, float h, float d) = sizeClass switch
		{
			0 => (0.46f, 0.4f, 0.42f),
			1 => (0.58f, 0.5f, 0.52f),
			_ => (0.72f, 0.6f, 0.6f),
		};
		float jitter = rng.RandfRange(0.94f, 1.06f);
		return new CrtSpec
		{
			W = w * jitter, H = h * jitter, D = d * rng.RandfRange(0.95f, 1.05f),
			KnobsRight = rng.Randf() < 0.5f,
			Body = CrtColours[rng.RandiRange(0, CrtColours.Length - 1)] * rng.RandfRange(0.85f, 1.05f),
		};
	}

	/// <summary>
	/// One CRT set, standing on the origin of <paramref name="xf"/> (bottom at y 0, screen facing +Z):
	/// tapered body, bezel with a raised lip round a bulged tube, knobs, a speaker grille or button row,
	/// feet, and a stub of cable out of the back. Returns the tube's centre (in xf space).
	/// </summary>
	public static Vector3 Crt(MeshKit body, MeshKit screen, Transform3D xf, CrtSpec s, Color screenColor)
	{
		var old = body.Xf;
		body.Xf = old * xf;
		float fz = s.D * 0.5f;
		body.Mat(BunkerTextures.PlasticMat);
		body.Color = s.Body;
		// Bezel slab and the tapered tube housing behind it.
		body.Box(new Vector3(0, s.H * 0.5f, fz - 0.04f), new Vector3(s.W, s.H, 0.08f));
		Frustum(body, new Vector3(0, s.H * 0.5f, fz - 0.08f), new Vector2(s.W * 0.95f, s.H * 0.93f),
			new Vector3(0, s.H * 0.42f, -fz), new Vector2(s.W * 0.52f, s.H * 0.55f));
		// Tube aperture and its raised lip.
		float sw, sh; Vector2 sc;
		if (s.KnobsRight) { sw = s.W * 0.7f; sh = s.H * 0.74f; sc = new Vector2(-s.W * 0.11f, s.H * 0.53f); }
		else { sw = s.W * 0.8f; sh = s.H * 0.66f; sc = new Vector2(0, s.H * 0.58f); }
		float lip = 0.022f, lz = fz + 0.008f;
		body.Color = s.Body * 0.8f;
		body.Box(new Vector3(sc.X, sc.Y + sh * 0.5f + lip * 0.5f, lz), new Vector3(sw + lip * 2f, lip, 0.02f));
		body.Box(new Vector3(sc.X, sc.Y - sh * 0.5f - lip * 0.5f, lz), new Vector3(sw + lip * 2f, lip, 0.02f));
		body.Box(new Vector3(sc.X - sw * 0.5f - lip * 0.5f, sc.Y, lz), new Vector3(lip, sh, 0.02f));
		body.Box(new Vector3(sc.X + sw * 0.5f + lip * 0.5f, sc.Y, lz), new Vector3(lip, sh, 0.02f));
		// Knobs and grille/buttons.
		body.Color = new Color(0.08f, 0.08f, 0.08f);
		if (s.KnobsRight)
		{
			float kx = s.W * 0.39f;
			body.Cylinder(new Vector3(kx, s.H * 0.72f, fz), new Vector3(kx, s.H * 0.72f, fz + 0.03f), 0.028f, 0.024f, 8);
			body.Cylinder(new Vector3(kx, s.H * 0.55f, fz), new Vector3(kx, s.H * 0.55f, fz + 0.025f), 0.022f, 0.02f, 8);
			for (int i = 0; i < 5; i++)
				body.Box(new Vector3(kx, s.H * 0.18f + i * 0.03f, fz + 0.002f), new Vector3(s.W * 0.12f, 0.01f, 0.006f));
		}
		else
		{
			for (int i = 0; i < 3; i++)
			{
				float kx = s.W * (0.1f + i * 0.1f);
				body.Cylinder(new Vector3(kx, s.H * 0.12f, fz), new Vector3(kx, s.H * 0.12f, fz + 0.02f), 0.016f, 0.014f, 6);
			}
			body.Box(new Vector3(-s.W * 0.32f, s.H * 0.12f, fz + 0.004f), new Vector3(0.05f, 0.025f, 0.012f));
		}
		// Feet and the cable stub.
		body.Color = new Color(0.1f, 0.1f, 0.1f);
		body.Box(new Vector3(-s.W * 0.35f, 0.01f, fz - 0.1f), new Vector3(0.06f, 0.02f, 0.08f));
		body.Box(new Vector3(s.W * 0.35f, 0.01f, fz - 0.1f), new Vector3(0.06f, 0.02f, 0.08f));
		body.Mat(BunkerTextures.RubberMat);
		body.Color = Colors.White;
		body.Cylinder(new Vector3(s.W * 0.1f, s.H * 0.3f, -fz + 0.02f), new Vector3(s.W * 0.14f, s.H * 0.26f, -fz - 0.08f), 0.012f, 0.012f, 4, false);
		body.Xf = old;
		body.Color = Colors.White;

		var tube = new Transform3D(Basis.Identity, new Vector3(sc.X, sc.Y, fz + 0.002f));
		BulgeScreen(screen, xf * tube, sw, sh, 0.022f, screenColor);
		return xf * tube.Origin;
	}

	/// <summary>
	/// A projected decal lying on a surface with outward normal <paramref name="normal"/>. The texture's
	/// top edge points away from <paramref name="down"/> (world down for wall streaks).
	/// </summary>
	public static Decal AddDecal(Node3D parent, Texture2D tex, Vector3 pos, Vector3 normal, Vector3 down,
		Vector2 size, float depth, Color modulate, Texture2D orm = null)
	{
		Vector3 y = normal.Normalized();
		Vector3 z = down - y * down.Dot(y);
		if (z.LengthSquared() < 1e-4f) z = Vector3.Forward - y * Vector3.Forward.Dot(y);
		if (z.LengthSquared() < 1e-4f) z = Vector3.Right;
		z = z.Normalized();
		Vector3 x = y.Cross(z).Normalized();
		var d = new Decal
		{
			TextureAlbedo = tex,
			Size = new Vector3(size.X, depth, size.Y),
			Modulate = modulate,
			UpperFade = 0.25f,
			LowerFade = 0.25f,
			DistanceFadeEnabled = true,
			DistanceFadeBegin = 26f,
			DistanceFadeLength = 8f,
			CullMask = 1,
			Transform = new Transform3D(new Basis(x, y, z), pos),
		};
		if (orm != null) d.TextureOrm = orm;
		parent.AddChild(d);
		return d;
	}

	/// <summary>
	/// A (nx+1) x (nz+1) vertex grid with true per-vertex colour (MeshKit colours per triangle), e.g. a
	/// floor with soft wet strips in its vertex alpha, or the earth mound. <paramref name="skip"/> may
	/// drop individual cells. Triangles face along each vertex's normal.
	/// </summary>
	public static ArrayMesh Grid(int nx, int nz, System.Func<int, int, (Vector3 pos, Vector3 n, Color c)> vert,
		Material mat, System.Func<int, int, bool> skip = null)
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		var pos = new Vector3[(nx + 1) * (nz + 1)];
		var nrm = new Vector3[pos.Length];
		for (int j = 0; j <= nz; j++)
			for (int i = 0; i <= nx; i++)
			{
				var (p, n, c) = vert(i, j);
				int idx = j * (nx + 1) + i;
				pos[idx] = p; nrm[idx] = n;
				st.SetColor(c);
				st.SetNormal(n);
				st.SetUV(new Vector2(p.X, p.Z));
				st.AddVertex(p);
			}
		void Tri(int a, int b, int c)
		{
			// Godot's front faces wind clockwise: flip when the geometric normal agrees with the vertex normal.
			var face = (pos[b] - pos[a]).Cross(pos[c] - pos[a]);
			if (face.Dot(nrm[a] + nrm[b] + nrm[c]) > 0f) (b, c) = (c, b);
			st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
		}
		for (int j = 0; j < nz; j++)
			for (int i = 0; i < nx; i++)
			{
				if (skip != null && skip(i, j)) continue;
				int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
				Tri(a, b, d);
				Tri(a, d, c);
			}
		st.SetMaterial(mat);
		return st.Commit();
	}

	/// <summary>A one-shot 3D sound under <paramref name="parent"/> on an explicit bus; frees itself.</summary>
	public static void OneShot(Node3D parent, string path, Vector3 localPos, string bus, float volumeDb = 0f,
		float pitch = 1f, float unitSize = 4f, float maxDistance = 30f)
	{
		if (!ResourceLoader.Exists(path)) return;
		var p = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = volumeDb, PitchScale = pitch,
			UnitSize = unitSize, MaxDistance = maxDistance, Position = localPos,
		};
		parent.AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
	}
}
