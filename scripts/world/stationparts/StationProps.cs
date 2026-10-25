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
	/// <summary>(2026-10-15, the owner: every model made in Blender) a growth from tools/Blender/figures.py (flesh_growth.glb:
	/// three fused masses of flesh, budding, their roots spread on the wall like a stain), set on the wall at
	/// <paramref name="at"/> growing out along <paramref name="normal"/>, about <paramref name="size"/> across, turned by the
	/// seed. False when the model is missing (then <see cref="Growth"/> builds the old lumps).</summary>
	public static bool GrowthModel(Node3D parent, Vector3 at, Vector3 normal, float size, int seed)
	{
		if (FigureModels.Load("flesh_growth") is not { } model) return false;
		string keep = "Growth" + "ABC"[Mathf.PosMod(seed, 3)];
		foreach (var n in model.FindChildren("*", "MeshInstance3D", true, false))
			if (n is MeshInstance3D mi)
			{
				if (mi.Name != keep) { mi.QueueFree(); continue; }
				FigureModels.Paint(mi, StationTextures.MeatMat);
				mi.Position = Vector3.Zero;   // (each sits on its own root at its origin)
			}
		model.Name = "Growth";
		parent.AddChild(model);
		// its +Y (Blender's +Z: out of the wall) along the wall's normal, a turn of its own about it
		var up = normal.Normalized();
		var side = Mathf.Abs(up.Dot(Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up;
		var basis = Basis.LookingAt(side.Cross(up).Normalized(), up).Rotated(up, Mathf.PosMod(seed * 0.618f, Mathf.Tau));
		model.Transform = new Transform3D(basis.Scaled(Vector3.One * size * 1.6f), at);
		return true;
	}

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
			// (2026-10-14h: rounder, the facets gone: one more subdivision)
			k.Blob(at + off + normal * s * 0.2f, new Vector3(s, s, s) * rng.RandfRange(0.8f, 1.2f), seed + i, 0.22f, false, 0.8f, 0f, 2);
		}
	}

	// ------------------------------------------------------------------ the three pieces

	/// <summary>The dead eye: one of the lake thing's eyeballs, fist-sized, the lid hanging half shut and
	/// slack over a dull, filmed iris, a ragged stump of tentacle behind it. Built facing -Z (its gaze)
	/// under <paramref name="parent"/>, <paramref name="r"/> = the ball's radius.</summary>
	private static StandardMaterial3D _deadEyeMat;

	/// <summary>The dead eye's painted ball: wet and glossy over the front, duller toward the torn back (its roughness
	/// map), a clear coat for the wet. Null if the paintings aren't there.</summary>
	private static StandardMaterial3D DeadEyeMat()
	{
		if (_deadEyeMat != null) return _deadEyeMat;
		const string dir = "res://assets/models/items/deadeye/";
		if (!ResourceLoader.Exists(dir + "deadeye_albedo.png")) return null;
		_deadEyeMat = new StandardMaterial3D
		{
			AlbedoTexture = GD.Load<Texture2D>(dir + "deadeye_albedo.png"),
			RoughnessTexture = ResourceLoader.Exists(dir + "deadeye_rough.png") ? GD.Load<Texture2D>(dir + "deadeye_rough.png") : null,
			Roughness = 1f, MetallicSpecular = 0.6f,
			ClearcoatEnabled = true, Clearcoat = 0.8f, ClearcoatRoughness = 0.08f,
			// (a little light of its own: in the basement's dark it should still be seen looking at them)
			EmissionEnabled = true, EmissionTexture = GD.Load<Texture2D>(dir + "deadeye_albedo.png"), EmissionEnergyMultiplier = 0.03f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		};
		return _deadEyeMat;
	}

	public static Node3D DeadEye(Node3D parent, float r = 0.13f, bool stump = true)
	{
		var root = new Node3D { Name = "DeadEye" };
		parent.AddChild(root);
		var ball = new Node3D { Name = "Ball", Scale = Vector3.One * r };
		root.AddChild(ball);
		// (2026-10-24, the owner: "make the eyeball look way better, you can see it has like a ring around it") its own eye,
		// not the lake thing's stock one (a ball with an iris and a pupil stood on its front, and a torus lid round it that
		// read as a band cut round the ball): the whole eye painted onto one smooth sphere (tools/Textures/make_deadeye.py:
		// the yellowed white, the veins, the clouded iris, the slit), so nothing stands proud of it and nothing rings it
		if (DeadEyeMat() is { } eyeMat)
			ball.AddChild(new MeshInstance3D { Name = "Eye", Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 }, MaterialOverride = eyeMat });
		else LakeCreature.BuildEye(ball);
		bool painted = ball.GetNodeOrNull("Eye") != null;
		if (!painted)
		{
			// a milky film over it: dead (the stock eye's; the painted one has its cataract in it, and a shell round it read
			// as a glass ring at its edge)
			ball.AddChild(new MeshInstance3D
			{
				Mesh = ItemModels.Get("station_items", "DeadEyeFilm") ?? new SphereMesh { Radius = 1.12f, Height = 2.24f, RadialSegments = 16, Rings = 10 },
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = new Color(0.8f, 0.8f, 0.72f, 0.28f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					Roughness = 0.3f, MetallicSpecular = 0.6f,
				},
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
		if (!stump) return root;       // set in the iron door: the stump would come out through the back of it
		if (painted)
		{
			// (2026-10-24) what it was torn out by: a ragged fringe of red tissue clinging round the back of the ball, and
			// the optic nerve, a pale cord as thick as a pencil, trailing off behind and sagging to the floor, its end torn
			// (it was a fat plug of flesh as wide as the eye: a sausage behind it)
			var t = new MeshKit();
			t.Mat(StationTextures.MeatMat);
			t.Color = new Color(0.78f, 0.3f, 0.28f);
			var rr = new RandomNumberGenerator { Seed = 1313 };
			// (the torn back is painted red into the ball itself; here only what still clings round the nerve's root, a few
			// uneven shreds bunched to one side, not a ring)
			for (int i = 0; i < 4; i++)
			{
				float a = 0.6f + i * 1.1f + rr.RandfRange(-0.3f, 0.3f);
				float d = r * rr.RandfRange(0.16f, 0.3f);
				var at = new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, Mathf.Sqrt(Mathf.Max(0f, r * r - d * d)) * 0.98f);
				t.Blob(at, new Vector3(r * rr.RandfRange(0.14f, 0.22f), r * rr.RandfRange(0.1f, 0.16f), r * rr.RandfRange(0.07f, 0.12f)), 40 + i, 0.25f, false, 1f, 0f, 3);
			}
			var nerve = new MeshKit();
			nerve.Mat(StationTextures.Flat("st_nerve", new Color(0.78f, 0.62f, 0.58f), 0.35f, 0.4f));
			nerve.Color = Colors.White;
			Vector3 prev = new(0f, 0f, r * 0.85f);
			float rad = r * 0.17f;
			for (int i = 1; i <= 6; i++)
			{
				float f = i / 6f;
				var next = new Vector3(Mathf.Sin(f * 2.2f) * r * 0.35f, -f * f * r * 0.85f, r * (0.85f + 1.9f * f));
				nerve.Cylinder(prev, next, rad, rad * 0.93f, 8, i == 6, 1f);
				nerve.Blob(next, Vector3.One * rad * 0.93f, 60 + i, 0.05f, false, 1f);   // (the joints rounded over)
				prev = next;
				rad *= 0.93f;
			}
			t.Color = new Color(0.5f, 0.12f, 0.1f);
			t.Blob(prev, new Vector3(rad * 1.3f, rad * 1.3f, rad * 0.7f), 70, 0.45f, false, 1f);   // (the torn end, raw)
			t.Color = Colors.White;
			t.CommitTo(root, "Tissue", true);
			nerve.CommitTo(root, "Nerve", true);
			return root;
		}
		// (modelled for a 0.13 m ball, scaled to this one's)
		if (ItemModels.Add(root, "station_items", "DeadEyeStump", new Transform3D(Basis.Identity.Scaled(Vector3.One * (r / 0.13f)), Vector3.Zero), "LidAndStump",
			n => n == "meat" ? StationTextures.MeatMat : null) != null)
			return root;
		var k = new MeshKit();
		// (it had a droopy upper lid: a sheet of skin whose drooping front edge cut through the eyeball and read as a
		// dark band across it (the owner: the eye should never be obscured). The wet rim round it stays: BuildEye's.)
		k.Mat(StationTextures.MeatMat);
		k.Color = new Color(0.55f, 0.42f, 0.44f);
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
		// (2026-10-15: Blender's, tools/Blender/items_small.py; the code below is the fallback)
		if (ItemModels.Add(root, "station_items", "PaleHand", Transform3D.Identity, "Hand", n => n switch
			{
				"skin_pale" => StationTextures.Flat("st_skin_pale", new Color(0.78f, 0.74f, 0.68f), 0.5f, 0.35f),
				"skin_cut" => StationTextures.Flat("st_skin_cut", new Color(0.45f, 0.08f, 0.07f), 0.3f, 0.5f),
				_ => null,
			}) != null)
			return root;
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
		// (2026-10-15: Blender's, tools/Blender/items_small.py; the code below is the fallback)
		if (ItemModels.Add(root, "station_items", "StairTread", Transform3D.Identity, "Tread", n => n switch
			{
				"deck" => PropTextures.DeckMat,
				"carve" => StationTextures.Flat("st_carve", new Color(0.18f, 0.15f, 0.12f), 0.9f, 0.1f),
				_ => null,
			}) != null)
			return root;
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
