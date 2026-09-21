using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;
using Ring = ProjectDS.World.ItemMeshes.Ring;

namespace ProjectDS.Entities;

/// <summary>
/// Builds Act 5's friend procedurally (PS2 budget, ~1.6k triangles), plus the
/// chair he sits in and the table in front of him.
///
/// STORY.md: "He is sitting in a chair at the table, dead, with his hand cut
/// off and bandaged, but he bled out. On the table in front of him is a newel
/// post bulb." So: a slumped seated man in a work jacket and jeans; the head
/// sags forward and to one side; the right arm hangs beside the chair ending in
/// a real hand; the left forearm lies on the table and ends at the wrist in a
/// blood-soaked bandage wrap, over a dark dried pool. Nothing gorier.
///
/// Frame: the chair sits at the origin and he faces +Z, toward the table. The
/// chair and table geometry is generated into the nodes at
/// <see cref="ChairPath"/> / <see cref="TablePath"/> (friend.tscn's "Chair" and
/// "Table", kept because the newel post pickup lives under Table). Collision:
/// one box for the table, one for the chair and body, on layer 1, so the newel
/// post snaps onto the real tabletop and the player can't walk through him.
/// </summary>
[Tool]
public partial class FriendBody : Node3D
{
	[Export] public NodePath ChairPath = "../Chair";
	[Export] public NodePath TablePath = "../Table";
	[Export] public bool BuildCollision = true;

	public int TriangleCount { get; private set; }

	/// <summary>Table top height (friend space), for anything placed on it.</summary>
	public const float TableTop = 0.65f;

	public override void _Ready() => Build();

	// ───────────────────────────── materials ─────────────────────────────

	private static Texture2D Denim() => ItemTextures.Make("fr_denim", 32, 32, (x, y) =>
	{
		float twill = ((x + y) % 4) < 2 ? 0.08f : -0.05f;
		float n = ItemTextures.Fbm(x, y, 32, 32, 3, 3, 3, 301);
		float v = 0.8f + twill + (n - 0.5f) * 0.3f;
		return new Color(v, v, v);
	});

	private static Texture2D Canvas() => ItemTextures.Make("fr_canvas", 32, 32, (x, y) =>
	{
		float weave = ((x & 1) ^ (y & 1)) == 1 ? 0.05f : -0.04f;
		float n = ItemTextures.Fbm(x, y, 32, 32, 4, 4, 3, 311);
		float v = 0.8f + weave + (n - 0.5f) * 0.35f;
		return new Color(v, v, v);
	});

	private static Texture2D Skin() => ItemTextures.Make("fr_skin", 16, 16, (x, y) =>
	{
		float n = ItemTextures.Fbm(x, y, 16, 16, 2, 2, 3, 321);
		float v = 0.86f + (n - 0.5f) * 0.25f;
		return new Color(v, v * 0.98f, v * 0.98f);
	});

	private static Texture2D Gauze() => ItemTextures.Make("fr_gauze", 16, 16, (x, y) =>
	{
		bool thread = x % 3 == 0 || y % 3 == 0;
		float n = ItemTextures.Fbm(x, y, 16, 16, 2, 2, 2, 331);
		float v = (thread ? 0.92f : 0.78f) + (n - 0.5f) * 0.2f;
		return new Color(v, v, v);
	});

	private static Texture2D Hair() => ItemTextures.Make("fr_hair", 16, 32, (x, y) =>
	{
		float strands = ItemTextures.Fbm(x, y, 16, 32, 8, 1, 2, 341);
		float v = 0.7f + (strands - 0.5f) * 0.6f;
		return new Color(v, v, v);
	});

	private static StandardMaterial3D JacketMat => ItemTextures.Std("fr_jacket", Canvas(), 0.95f, 0.15f);
	private static StandardMaterial3D JeansMat => ItemTextures.Std("fr_jeans", Denim(), 0.95f, 0.15f);
	private static StandardMaterial3D SkinMat => ItemTextures.Std("fr_skin", Skin(), 0.7f, 0.3f);
	private static StandardMaterial3D GauzeMat => ItemTextures.Std("fr_gauze", Gauze(), 0.9f, 0.2f);
	private static StandardMaterial3D HairMat => ItemTextures.Std("fr_hair", Hair(), 0.8f, 0.25f);
	private static StandardMaterial3D BootMat => ItemTextures.Std("fr_boot", ItemTextures.Leatherette(), 0.75f, 0.3f);
	private static StandardMaterial3D BloodMat => (StandardMaterial3D)ItemTextures.Cached("fr_blood", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.13f, 0.025f, 0.022f),
		Roughness = 0.45f,
		MetallicSpecular = 0.5f,
	});

	// Tones (vertex colour, multiplied into the textures)
	private static readonly Color Jacket = S(0.3f, 0.28f, 0.21f);      // faded olive-brown work jacket
	private static readonly Color JacketDark = S(0.2f, 0.19f, 0.15f);
	private static readonly Color Jeans = S(0.25f, 0.29f, 0.37f);        // worn denim
	private static readonly Color SkinTone = S(0.62f, 0.58f, 0.55f);     // bloodless, desaturated
	private static readonly Color Socket = S(0.24f, 0.2f, 0.2f);
	private static readonly Color Stubble = S(0.5f, 0.47f, 0.45f);
	private static readonly Color Lips = S(0.46f, 0.4f, 0.41f);
	private static readonly Color HairTone = S(0.2f, 0.15f, 0.11f);
	private static readonly Color Boot = S(0.22f, 0.17f, 0.13f);
	/// <summary>MeshKit vertex colours are linear; author in sRGB and convert.</summary>
	private static Color S(float r, float g, float b) => new Color(r, g, b).SrgbToLinear();

	// ───────────────────────────── build ─────────────────────────────

	public void Build()
	{
		foreach (var c in GetChildren())
			if (c.HasMeta("friend_generated")) { RemoveChild(c); c.QueueFree(); }
		TriangleCount = 0;

		var k = new MeshKit();
		BuildLegs(k);
		BuildTorso(k);
		BuildArms(k);
		BuildHead(k);
		var man = k.CommitTo(this, "Man");
		man.SetMeta("friend_generated", true);
		TriangleCount += ItemMeshes.CountTriangles(man.Mesh);

		if (GetNodeOrNull<Node3D>(ChairPath) is Node3D chair) TriangleCount += BuildChair(chair);
		if (GetNodeOrNull<Node3D>(TablePath) is Node3D table) TriangleCount += BuildTable(table);

		if (BuildCollision && !Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "Collision", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("friend_generated", true);
			body.SetMeta("surface", "wood");
			AddChild(body);
			// Chair + seated body.
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.52f, 0.02f), Shape = new BoxShape3D { Size = new Vector3(0.56f, 1.04f, 0.62f) } });
			if (GetNodeOrNull<Node3D>(TablePath) is Node3D t)
			{
				Vector3 tp = t.Position;
				body.AddChild(new CollisionShape3D { Position = tp + new Vector3(0, TableTop * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(1.1f, TableTop, 0.7f) } });
			}
		}
	}

	private static void BuildTorso(MeshKit k)
	{
		// Jeans at the hips (mostly hidden under the jacket hem).
		k.Mat(JeansMat);
		k.Color = Jeans;
		ItemMeshes.Loft(k, new[]
		{
			new Ring(new Vector3(0, 0.5f, -0.08f), 0.16f, 0.11f),
			new Ring(new Vector3(0, 0.575f, -0.075f), 0.175f, 0.125f),
			new Ring(new Vector3(0.005f, 0.64f, -0.06f), 0.165f, 0.12f),
		}, 8, true, false, Vector3.Right, 3f);

		// Jacket: slumped, chest folding forward, shoulders rolled in, collar up.
		k.Mat(JacketMat);
		var rings = new[]
		{
			new Ring(new Vector3(0.005f, 0.6f, -0.06f), 0.19f, 0.14f),
			new Ring(new Vector3(0.01f, 0.7f, -0.045f), 0.18f, 0.13f),
			new Ring(new Vector3(0.02f, 0.81f, 0f), 0.19f, 0.13f),
			new Ring(new Vector3(0.03f, 0.895f, 0.06f), 0.2f, 0.125f),
			new Ring(new Vector3(0.04f, 0.95f, 0.115f), 0.195f, 0.105f),
			new Ring(new Vector3(0.045f, 0.985f, 0.155f), 0.13f, 0.085f),
			new Ring(new Vector3(0.05f, 1.02f, 0.19f), 0.09f, 0.075f),
		};
		ItemMeshes.Loft(k, rings, 12, false, false, Vector3.Right, 3f,
			(r, a) =>
			{
				// Shoulders square off; the back is rounder than the flat chest.
				float s = Mathf.Sin(a), c = Mathf.Cos(a);
				float m = 1f;
				if (r == 4) m += 0.08f * c * c;
				if (s > 0.6f) m -= 0.04f;
				return m;
			},
			(r, a) =>
			{
				// Front zip line and a darker hem and armpits.
				if (a >= 0 && Mathf.Abs(Mathf.Sin(a)) > 0.97f && Mathf.Sin(a) > 0) return JacketDark;
				if (r == 0) return JacketDark;
				return Jacket;
			});
		// Flap pockets.
		k.Color = JacketDark;
		k.Box(new Vector3(0.105f, 0.69f, 0.085f), new Vector3(0.1f, 0.02f, 0.012f), 4f, new Basis(Vector3.Right, -0.15f));
		k.Box(new Vector3(-0.095f, 0.69f, 0.085f), new Vector3(0.1f, 0.02f, 0.012f), 4f, new Basis(Vector3.Right, -0.15f));
	}

	private static void BuildLegs(MeshKit k)
	{
		k.Mat(JeansMat);
		k.Color = Jeans;
		foreach (float s in new[] { 1f, -1f })
		{
			float sp = s > 0 ? 0f : 0.035f;   // right leg splays a little wider
			ItemMeshes.Loft(k, new[]
			{
				new Ring(new Vector3(s * 0.095f, 0.56f, -0.03f), 0.085f),
				new Ring(new Vector3(s * (0.12f + sp * 0.5f), 0.555f, 0.16f), 0.079f, 0.076f),
				new Ring(new Vector3(s * (0.15f + sp), 0.53f, 0.335f), 0.064f),
				new Ring(new Vector3(s * (0.152f + sp), 0.49f, 0.37f), 0.06f),
				new Ring(new Vector3(s * (0.157f + sp * 1.2f), 0.32f, 0.39f), 0.057f),
				new Ring(new Vector3(s * (0.16f + sp * 1.3f), 0.13f, 0.425f), 0.047f),
			}, 10, false, true, Vector3.Right, 3f);

			// Work boots: a short shaft, then the foot.
			k.Mat(BootMat);
			k.Color = Boot;
			float bx = s * (0.16f + sp * 1.3f);
			ItemMeshes.Loft(k, new[]
			{
				new Ring(new Vector3(bx, 0.15f, 0.425f), 0.05f),
				new Ring(new Vector3(bx, 0.06f, 0.43f), 0.052f, 0.06f),
			}, 8, false, false, Vector3.Right, 4f);
			float tx = s * sp * 0.6f;
			ItemMeshes.Loft(k, new[]
			{
				new Ring(new Vector3(bx, 0.045f, 0.385f), 0.043f, 0.043f),
				new Ring(new Vector3(bx + tx * 0.4f, 0.045f, 0.46f), 0.049f, 0.045f),
				new Ring(new Vector3(bx + tx * 0.8f, 0.036f, 0.53f), 0.05f, 0.036f),
				new Ring(new Vector3(bx + tx, 0.03f, 0.575f), 0.04f, 0.027f),
			}, 8, true, true, Vector3.Right, 4f, null, null, 1f, 0.5f);
			k.Mat(JeansMat);
			k.Color = Jeans;
		}
	}

	private static void BuildArms(MeshKit k)
	{
		// Right arm: hangs straight down beside the chair.
		k.Mat(JacketMat);
		k.Color = Jacket;
		ItemMeshes.Loft(k, new[]
		{
			new Ring(new Vector3(-0.185f, 0.945f, 0.12f), 0.064f, 0.06f),
			new Ring(new Vector3(-0.225f, 0.83f, 0.1f), 0.059f),
			new Ring(new Vector3(-0.25f, 0.71f, 0.085f), 0.053f),
			new Ring(new Vector3(-0.262f, 0.6f, 0.09f), 0.049f),
			new Ring(new Vector3(-0.268f, 0.515f, 0.1f), 0.049f),
			new Ring(new Vector3(-0.27f, 0.5f, 0.102f), 0.046f),
		}, 10, false, true, Vector3.Right, 3f, null,
			(r, a) => r >= 4 ? JacketDark : Jacket);
		BuildHangingHand(k, new Vector3(-0.27f, 0.505f, 0.103f));

		// Left arm: upper arm down to the table edge, forearm lying across the table.
		k.Mat(JacketMat);
		k.Color = Jacket;
		ItemMeshes.Loft(k, new[]
		{
			new Ring(new Vector3(0.2f, 0.945f, 0.15f), 0.064f, 0.06f),
			new Ring(new Vector3(0.235f, 0.84f, 0.26f), 0.059f),
			new Ring(new Vector3(0.262f, 0.735f, 0.37f), 0.055f),
			new Ring(new Vector3(0.268f, 0.705f, 0.43f), 0.052f),
			new Ring(new Vector3(0.262f, 0.7f, 0.5f), 0.05f, 0.046f),
			new Ring(new Vector3(0.257f, 0.699f, 0.55f), 0.05f, 0.046f),
		}, 10, false, true, Vector3.Right, 3f, null,
			(r, a) => r >= 5 ? JacketDark : Jacket);
		// Bare forearm between the pushed-up cuff and the wrap.
		k.Mat(SkinMat);
		k.Color = SkinTone;
		ItemMeshes.Loft(k, new[]
		{
			new Ring(new Vector3(0.257f, 0.697f, 0.545f), 0.04f, 0.035f),
			new Ring(new Vector3(0.254f, 0.694f, 0.6f), 0.035f, 0.03f),
		}, 8, false, false, Vector3.Right, 4f);
		// The wrap: clean and grubby near the elbow end, soaked dark at the stump.
		k.Mat(GauzeMat);
		var bandTones = new[] { S(0.7f, 0.67f, 0.6f), S(0.55f, 0.38f, 0.33f), S(0.28f, 0.07f, 0.06f), S(0.19f, 0.04f, 0.035f) };
		ItemMeshes.Loft(k, new[]
		{
			new Ring(new Vector3(0.254f, 0.695f, 0.59f), 0.04f, 0.035f),
			new Ring(new Vector3(0.252f, 0.696f, 0.625f), 0.043f, 0.038f),
			new Ring(new Vector3(0.25f, 0.695f, 0.66f), 0.042f, 0.037f),
			new Ring(new Vector3(0.249f, 0.692f, 0.685f), 0.034f, 0.03f),
		}, 8, false, true, Vector3.Right, 6f,
			(r, a) => 1f + (r == 1 && Mathf.Cos(a * 3f) > 0.5f ? 0.05f : 0f),
			(r, a) => a < 0 ? bandTones[3] : bandTones[Math.Min(r + 1, 3)], 1f, 0.6f);
		// A loose tail of the wrap trailing onto the table.
		k.Color = bandTones[2];
		ItemMeshes.Ribbon(k, new[]
		{
			new Vector3(0.285f, 0.7f, 0.64f), new Vector3(0.315f, 0.672f, 0.655f),
			new Vector3(0.345f, 0.6525f, 0.68f), new Vector3(0.375f, 0.6522f, 0.715f),
		}, 0.028f, Vector3.Up, true, 6f);
	}

	/// <summary>A limp hand hanging from the wrist: palm facing the thigh, fingers loosely curled.</summary>
	private static void BuildHangingHand(MeshKit k, Vector3 wrist)
	{
		k.Mat(SkinMat);
		k.Color = SkinTone;
		var old = k.Xf;
		k.Xf = new Transform3D(new Basis(Vector3.Right, 0.12f), wrist);
		// Palm: thin across X (the palm faces +X, toward his leg), wide along Z.
		ItemMeshes.Loft(k, new[]
		{
			new Ring(new Vector3(0, 0.01f, 0), 0.018f, 0.025f),
			new Ring(new Vector3(0, -0.045f, 0.003f), 0.016f, 0.039f),
			new Ring(new Vector3(0, -0.085f, 0.005f), 0.013f, 0.04f),
		}, 8, false, true, Vector3.Right, 6f, null, null, 1f, 0.3f);
		// Four fingers from the knuckles, curling toward the palm (+X), then the thumb.
		float[] zs = { -0.029f, -0.01f, 0.01f, 0.029f };
		float[] len = { 0.036f, 0.046f, 0.05f, 0.044f };
		for (int i = 0; i < 4; i++)
		{
			Vector3 a = new(0.002f, -0.085f, zs[i] * 0.95f + 0.005f);
			Vector3 b = a + new Vector3(0.006f, -len[i], 0f);
			Vector3 c = b + new Vector3(0.018f, -len[i] * 0.55f, 0f);
			k.Cylinder(a, b, 0.0088f, 0.0078f, 4, false, 8f);
			k.Cylinder(b, c, 0.0078f, 0.0062f, 4, true, 8f);
		}
		Vector3 t0 = new(0.012f, -0.02f, 0.024f), t1 = new(0.024f, -0.058f, 0.034f), t2 = new(0.03f, -0.084f, 0.03f);
		k.Cylinder(t0, t1, 0.0105f, 0.009f, 4, false, 8f);
		k.Cylinder(t1, t2, 0.009f, 0.007f, 4, true, 8f);
		k.Xf = old;
	}

	private static void BuildHead(MeshKit k)
	{
		// Neck from the collar to the base of the skull.
		Vector3 neckTop = new(0.065f, 1.035f, 0.245f);
		k.Mat(SkinMat);
		k.Color = SkinTone;
		ItemMeshes.Loft(k, new[]
		{
			new Ring(new Vector3(0.05f, 0.985f, 0.17f), 0.05f, 0.048f),
			new Ring(new Vector3(0.058f, 1.012f, 0.21f), 0.047f, 0.045f),
			new Ring(neckTop, 0.046f, 0.044f),
		}, 7, false, false, Vector3.Right, 4f);

		// Head space: upright, facing +Z, origin at the jaw/neck junction. Then it sags:
		// pitched forward onto the chest and rolled toward the table side.
		var sag = new Basis(Vector3.Up, 0.12f) * new Basis(Vector3.Back, Mathf.DegToRad(-20f)) * new Basis(Vector3.Right, Mathf.DegToRad(38f));
		var old = k.Xf;
		k.Xf = new Transform3D(sag, neckTop);

		const float front = Mathf.Pi * 0.5f;
		float Off(float a) { float d = Mathf.Abs(Mathf.Wrap(a - front, -Mathf.Pi, Mathf.Pi)); return d; }
		var head = new[]
		{
			new Ring(new Vector3(0, -0.012f, 0.0f), 0.046f, 0.046f),
			new Ring(new Vector3(0, 0.018f, 0.02f), 0.056f, 0.07f),
			new Ring(new Vector3(0, 0.053f, 0.015f), 0.068f, 0.085f),
			new Ring(new Vector3(0, 0.088f, 0.008f), 0.073f, 0.093f),
			new Ring(new Vector3(0, 0.122f, 0.0f), 0.077f, 0.098f),
			new Ring(new Vector3(0, 0.158f, -0.01f), 0.074f, 0.095f),
			new Ring(new Vector3(0, 0.193f, -0.02f), 0.061f, 0.079f),
			new Ring(new Vector3(0, 0.217f, -0.025f), 0.035f, 0.046f),
		};
		ItemMeshes.Loft(k, head, 14, false, true, Vector3.Right, 5f,
			(r, a) =>
			{
				float d = Off(a);
				float m = 1f;
				if (r == 1 && d < 0.5f) m += 0.08f * (1f - d / 0.5f);              // chin
				if (r == 1 && d > 0.9f && d < 1.6f) m += 0.05f;                    // jaw corners
				if ((r == 3 || r == 4) && Mathf.Abs(d - 0.62f) < 0.26f) m -= 0.07f; // eye sockets
				if (r >= 4 && r <= 6 && d > 2.2f) m += 0.04f;                      // back of the skull
				return m;
			},
			(r, a) =>
			{
				if (a < 0) return SkinTone;
				float d = Off(a);
				if ((r == 3 || r == 4) && Mathf.Abs(d - 0.62f) < 0.3f) return Socket;
				if (r == 2 && d < 0.35f) return Lips;
				if (r <= 2 && d < 1.5f) return Stubble;
				return SkinTone;
			}, 1f, 0.5f);

		// Nose: a small wedge off the face.
		Vector3 nt = new(0, 0.083f, 0.128f), top = new(0, 0.118f, 0.097f), bot = new(0, 0.074f, 0.1f);
		Vector3 l = new(0.015f, 0.085f, 0.098f), rr = new(-0.015f, 0.085f, 0.098f);
		Vector3 nc = new(0, 0.09f, 0.09f);
		k.Color = SkinTone;
		foreach (var (p, q) in new[] { (top, l), (l, bot), (bot, rr), (rr, top) })
		{
			Vector3 n = (q - p).Cross(nt - p).Normalized();
			if (n.Dot((p + q + nt) / 3f - nc) < 0) n = -n;
			k.Tri(p, q, nt, n, Vector2.Zero, Vector2.Right, Vector2.One);
		}
		// Ears.
		foreach (float s in new[] { 1f, -1f })
			k.Card(new Vector3(s * 0.075f, 0.075f, -0.01f), new Vector3(s * 0.083f, 0.08f, -0.03f), new Vector3(s * 0.083f, 0.118f, -0.028f), new Vector3(s * 0.076f, 0.122f, -0.004f),
				new Vector3(s, 0, 0), Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down);

		// Hair: a shell over the top and back, the front pulled in behind the face, hairline at the brow.
		k.Mat(HairMat);
		k.Color = HairTone;
		ItemMeshes.Loft(k, new[]
		{
			new Ring(new Vector3(0, 0.085f, -0.012f), 0.079f, 0.098f),
			new Ring(new Vector3(0, 0.13f, -0.012f), 0.083f, 0.104f),
			new Ring(new Vector3(0, 0.17f, -0.016f), 0.081f, 0.103f),
			new Ring(new Vector3(0, 0.203f, -0.024f), 0.066f, 0.086f),
			new Ring(new Vector3(0, 0.229f, -0.03f), 0.037f, 0.05f),
		}, 14, false, true, Vector3.Right, 6f,
			(r, a) =>
			{
				float d = Off(a);
				if (r <= 1 && d < 1.25f) return 0.78f;   // tucked behind the face
				return 1f + (r == 2 && d < 0.8f ? 0.02f : 0f);
			}, null, 1f, 0.5f);
		k.Xf = old;
	}

	// ───────────────────────────── furniture ─────────────────────────────

	private static int BuildChair(Node3D chair)
	{
		var old = chair.GetNodeOrNull("Generated");
		if (old != null) { chair.RemoveChild(old); old.QueueFree(); }
		var k = new MeshKit();
		k.Mat(ProcTextures.WoodMat);
		k.Color = new Color(0.4f, 0.31f, 0.22f);
		k.Box(new Vector3(0, 0.47f, 0), new Vector3(0.44f, 0.04f, 0.42f), 1.5f);
		foreach (float x in new[] { -1f, 1f })
			foreach (float z in new[] { -1f, 1f })
				k.Cylinder(new Vector3(x * 0.19f, 0f, z * 0.18f), new Vector3(x * 0.18f, 0.45f, z * 0.17f), 0.019f, 0.022f, 5, false, 2f);
		// back posts leaning back, top rail, three spindles
		foreach (float x in new[] { -1f, 1f })
			k.Cylinder(new Vector3(x * 0.19f, 0.45f, -0.19f), new Vector3(x * 0.2f, 0.97f, -0.25f), 0.021f, 0.018f, 5, true, 2f);
		k.Box(new Vector3(0, 0.92f, -0.243f), new Vector3(0.42f, 0.08f, 0.03f), 1.5f, new Basis(Vector3.Right, -0.11f));
		for (int i = -1; i <= 1; i++)
			k.Cylinder(new Vector3(i * 0.08f, 0.49f, -0.2f), new Vector3(i * 0.085f, 0.88f, -0.238f), 0.011f, 0.011f, 4, false, 2f);
		// stretchers
		k.Box(new Vector3(0, 0.16f, 0.175f), new Vector3(0.36f, 0.025f, 0.02f), 1.5f);
		k.Box(new Vector3(0, 0.16f, -0.18f), new Vector3(0.36f, 0.025f, 0.02f), 1.5f);
		var gen = new Node3D { Name = "Generated" };
		chair.AddChild(gen);
		var mi = k.CommitTo(gen, "ChairMesh");
		return ItemMeshes.CountTriangles(mi.Mesh);
	}

	private static int BuildTable(Node3D table)
	{
		var old = table.GetNodeOrNull("Generated");
		if (old != null) { table.RemoveChild(old); old.QueueFree(); }
		var k = new MeshKit();
		k.Mat(ProcTextures.WoodMat);
		k.Color = new Color(0.5f, 0.39f, 0.27f);
		k.Box(new Vector3(0, TableTop - 0.03f, 0), new Vector3(1.1f, 0.06f, 0.7f), 1.2f);
		k.Color = new Color(0.38f, 0.29f, 0.2f);
		k.Box(new Vector3(0, TableTop - 0.1f, 0.3f), new Vector3(0.94f, 0.08f, 0.025f), 1.2f);
		k.Box(new Vector3(0, TableTop - 0.1f, -0.3f), new Vector3(0.94f, 0.08f, 0.025f), 1.2f);
		k.Box(new Vector3(0.46f, TableTop - 0.1f, 0), new Vector3(0.025f, 0.08f, 0.56f), 1.2f);
		k.Box(new Vector3(-0.46f, TableTop - 0.1f, 0), new Vector3(0.025f, 0.08f, 0.56f), 1.2f);
		foreach (float x in new[] { -1f, 1f })
			foreach (float z in new[] { -1f, 1f })
				k.Beam(new Vector3(x * 0.48f, 0f, z * 0.28f), new Vector3(x * 0.48f, TableTop - 0.06f, z * 0.28f), 0.05f, 0.05f, 1.2f, Vector3.Forward);

		// The dried pool under the stump (friend space ~(0.25, top, 0.7)); the table sits at z = Position.Z.
		k.Mat(BloodMat);
		k.Color = Colors.White;
		Vector3 c = new(0.265f, TableTop + 0.0015f, 0.72f - table.Position.Z);
		var rng = new RandomNumberGenerator { Seed = 4417 };
		const int n = 11;
		var ring = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n;
			float r = rng.RandfRange(0.06f, 0.1f);
			ring[i] = c + new Vector3(Mathf.Cos(a) * r * 1.2f, 0, Mathf.Sin(a) * r * 0.85f);
		}
		for (int i = 0; i < n; i++)
			k.Tri(c, ring[i], ring[(i + 1) % n], Vector3.Up, new Vector2(0.5f, 0.5f), Vector2.Zero, Vector2.Right);

		var gen = new Node3D { Name = "Generated" };
		table.AddChild(gen);
		var mi = k.CommitTo(gen, "TableMesh");
		return ItemMeshes.CountTriangles(mi.Mesh);
	}
}
