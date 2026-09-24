using Godot;
using ProjectDS.Entities;

namespace ProjectDS.World.StationParts;

/// <summary>
/// Shared geometry for Act 13's station: flat decals (blood, handprints, stains), industrial pipe
/// runs and hanging chain, meat growths, and the three pieces the iron door wants — the dead eye,
/// the pale hand and the carved step — built the same wherever they appear (lying in the basement,
/// fished out of the blood, set in the door's hollows).
/// </summary>
public static class StationProps
{
	/// <summary>A flat decal quad facing <paramref name="normal"/>, a hair off the surface.</summary>
	public static MeshInstance3D Decal(Node3D parent, Material mat, Vector3 at, Vector3 normal, Vector2 size, float spin = 0f, string name = "Decal")
	{
		var mi = new MeshInstance3D
		{
			Name = name,
			Mesh = new QuadMesh { Size = size },
			MaterialOverride = mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(mi);
		Vector3 n = normal.Normalized();
		Vector3 up = Mathf.Abs(n.Y) > 0.9f ? Vector3.Forward : Vector3.Up;
		// QuadMesh faces +Z: point +Z along the normal
		var b = Basis.LookingAt(-n, up) * new Basis(Vector3.Back, spin);
		mi.Transform = new Transform3D(b, at + n * 0.006f);
		return mi;
	}

	/// <summary>A run of rusted pipe from a to b, with a flange collar every so often.</summary>
	public static void Pipe(MeshKit k, Vector3 a, Vector3 b, float r)
	{
		k.Mat(StationTextures.RustPlateMat);
		k.Color = new Color(0.7f, 0.62f, 0.55f);
		k.Cylinder(a, b, r, r, 8, true, 2f);
		float len = a.DistanceTo(b);
		for (float t = 0.4f; t < len - 0.2f; t += 1.6f)
		{
			Vector3 p = a.Lerp(b, t / len), d = (b - a).Normalized() * 0.03f;
			k.Cylinder(p - d, p + d, r * 1.4f, r * 1.4f, 8, true, 2f);
		}
	}

	/// <summary>A hanging chain of flat links from <paramref name="top"/> down <paramref name="length"/> metres.</summary>
	public static void Chain(MeshKit k, Vector3 top, float length)
	{
		k.Mat(StationTextures.RustPlateMat);
		k.Color = new Color(0.55f, 0.45f, 0.38f);
		const float link = 0.09f;
		for (int i = 0; i * link < length; i++)
		{
			Vector3 c = top + Vector3.Down * (i * link + link * 0.5f);
			var rot = new Basis(Vector3.Up, i % 2 == 0 ? 0f : Mathf.Pi * 0.5f);
			k.Xf = new Transform3D(rot, c);
			ItemMeshes.Torus(k, Vector3.Zero, Vector3.Forward, 0.035f, 0.009f, 8, 4);
		}
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A swollen lump of flesh bulging out of a surface (normal points out of the surface).</summary>
	public static void Growth(MeshKit k, Vector3 at, Vector3 normal, float size, int seed)
	{
		k.Mat(StationTextures.MeatMat);
		k.Color = Colors.White;
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		for (int i = 0; i < 4; i++)
		{
			Vector3 off = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)) * size * 0.5f;
			off -= normal * off.Dot(normal);
			float s = size * rng.RandfRange(0.45f, 0.8f);
			k.Blob(at + off + normal * s * 0.2f, new Vector3(s, s, s) * rng.RandfRange(0.8f, 1.2f), seed + i, 0.25f, false, 0.8f);
		}
	}

	// ------------------------------------------------------------------ the three pieces

	/// <summary>The dead eye: one of the lake thing's eyeballs, fist-sized, the lid hanging half shut and
	/// slack over a dull, filmed iris, a ragged stump of tentacle behind it. Built facing -Z (its gaze)
	/// under <paramref name="parent"/>, <paramref name="r"/> = the ball's radius.</summary>
	public static Node3D DeadEye(Node3D parent, float r = 0.13f)
	{
		var root = new Node3D { Name = "DeadEye" };
		parent.AddChild(root);
		var ball = new Node3D { Name = "Ball", Scale = Vector3.One * r };
		root.AddChild(ball);
		LakeCreature.BuildEye(ball);
		// a milky film over it: dead
		ball.AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 1.03f, Height = 2.06f, RadialSegments = 12, Rings = 8 },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.8f, 0.8f, 0.72f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				Roughness = 0.3f, MetallicSpecular = 0.6f,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		var k = new MeshKit();
		// the droopy upper lid: a slack fold of skin sagging over the top half of the eye
		k.Mat(StationTextures.MeatMat);
		k.Color = new Color(0.55f, 0.42f, 0.44f);
		const int segs = 10;
		for (int i = 0; i < segs; i++)
		{
			float a0 = Mathf.Pi * i / segs, a1 = Mathf.Pi * (i + 1) / segs;
			// a dome over the top, drooping lower in front (-Z) than behind
			Vector3 P(float a, float droop) => new(Mathf.Cos(a) * r * 1.12f, Mathf.Sin(a) * r * 1.12f * (1f - droop), -r * 0.25f);
			Vector3 lid0 = P(a0, 0f) + new Vector3(0, 0, r * 0.6f), lid1 = P(a1, 0f) + new Vector3(0, 0, r * 0.6f);
			Vector3 edge0 = P(a0, 0.62f) + new Vector3(0, -r * 0.05f, -r * 0.85f), edge1 = P(a1, 0.62f) + new Vector3(0, -r * 0.05f, -r * 0.85f);
			k.Quad(lid0, lid1, edge1, edge0, Vector3.Up);
			k.Quad(edge0, edge1, lid1, lid0, Vector3.Down);
		}
		// the stump: a torn plug of tentacle flesh behind
		k.Cylinder(new Vector3(0, 0, r * 0.4f), new Vector3(0.02f, -0.03f, r * 2.1f), r * 0.75f, r * 0.55f, 8, true, 1.5f);
		k.Blob(new Vector3(0.02f, -0.03f, r * 2.1f), Vector3.One * r * 0.6f, 7, 0.35f, false, 1f);
		k.Color = Colors.White;
		k.CommitTo(root, "LidAndStump", true);
		return root;
	}

	/// <summary>A pale, waxy severed hand, palm up, fingers half curled.</summary>
	public static Node3D PaleHand(Node3D parent)
	{
		var root = new Node3D { Name = "PaleHand" };
		parent.AddChild(root);
		var k = new MeshKit();
		k.Mat(StationTextures.Flat("st_skin_pale", new Color(0.78f, 0.74f, 0.68f), 0.5f, 0.35f));
		k.Color = Colors.White;
		k.Blob(Vector3.Zero, new Vector3(0.05f, 0.018f, 0.055f), 3, 0.08f, false);        // palm
		k.Cylinder(new Vector3(0, 0, 0.05f), new Vector3(0, 0, 0.11f), 0.028f, 0.03f, 7, true);   // wrist
		k.Mat(StationTextures.Flat("st_skin_cut", new Color(0.45f, 0.08f, 0.07f), 0.3f, 0.5f));
		k.Cylinder(new Vector3(0, 0, 0.11f), new Vector3(0, 0, 0.112f), 0.03f, 0.03f, 7, true);    // the cut
		k.Mat(StationTextures.Flat("st_skin_pale", new Color(0.78f, 0.74f, 0.68f), 0.5f, 0.35f));
		for (int f = 0; f < 4; f++)
		{
			float x = -0.034f + f * 0.023f;
			Vector3 a = new(x, 0.004f, -0.045f), b = new(x * 1.05f, 0.02f, -0.085f + f % 2 * 0.006f), c = new(x * 1.08f, 0.045f, -0.1f);
			k.Cylinder(a, b, 0.009f, 0.008f, 5, true);
			k.Cylinder(b, c, 0.008f, 0.006f, 5, true);
		}
		k.Cylinder(new Vector3(0.05f, 0, 0.01f), new Vector3(0.075f, 0.02f, -0.03f), 0.01f, 0.008f, 5, true);    // thumb
		k.CommitTo(root, "Hand", true);
		return root;
	}

	/// <summary>A single stair tread, sawn off: grey weathered wood, nosing worn round, an eye carved into its face.</summary>
	public static Node3D StairTread(Node3D parent)
	{
		var root = new Node3D { Name = "StairTread" };
		parent.AddChild(root);
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.55f, 0.52f, 0.48f);
		BuildKit.Box(k, Vector3.Zero, new Vector3(0.36f, 0.045f, 0.16f), 1.5f);
		k.Cylinder(new Vector3(-0.18f, 0, -0.08f), new Vector3(0.18f, 0, -0.08f), 0.024f, 0.024f, 6, true);
		// the carved eye on its top face
		k.Mat(StationTextures.Flat("st_carve", new Color(0.18f, 0.15f, 0.12f), 0.9f, 0.1f));
		k.Color = Colors.White;
		for (int i = 0; i < 12; i++)
		{
			float a0 = Mathf.Tau * i / 12, a1 = Mathf.Tau * (i + 1) / 12;
			Vector3 p0 = new(Mathf.Cos(a0) * 0.06f, 0.0235f, Mathf.Sin(a0) * 0.025f), p1 = new(Mathf.Cos(a1) * 0.06f, 0.0235f, Mathf.Sin(a1) * 0.025f);
			k.Cylinder(p0, p1, 0.003f, 0.003f, 3, false);
		}
		k.Cylinder(new Vector3(0, 0.0235f, 0), new Vector3(0, 0.024f, 0), 0.012f, 0.012f, 8, true);
		k.CommitTo(root, "Tread", true);
		return root;
	}
}
