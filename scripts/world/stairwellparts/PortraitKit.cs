using Godot;

namespace ProjectDS.World.StairwellParts;

/// <summary>
/// The burnt portraits of Room 3 (Act 14): old oil portraits in heavy ornate frames, every one with
/// the sitter's face burnt out of the canvas. Frames vary: tarnished gilt with stepped mouldings and corner
/// bosses, black lacquer with a gold slip, dark carved walnut, tarnished silver-leaf, plain worn oak, a narrow ebony
/// slip. The canvas faces +Z in the parent's space; (0,0) is its middle.
/// (2026-10-15, the scan: forty-odd near-identical bright gilt frames, each with the same black burnt rectangle. Now each
/// frame's style, width, depth and finish are drawn from its own number; the gilt dulled and tarnished, the woods not
/// metal; the soot scorched into the canvas itself, its own shape on each; a few frames hang empty, their canvas cut out.)
/// </summary>
public static class PortraitKit
{
	// per style: the body colour, the slip (the inner lip) colour, metal or wood
	private static readonly (Color body, Color slip, bool metal)[] Styles =
	{
		(new(0.5f, 0.38f, 0.17f), new(0.36f, 0.27f, 0.12f), true),    // tarnished gilt
		(new(0.06f, 0.05f, 0.045f), new(0.45f, 0.34f, 0.15f), false),  // black lacquer, a gold slip
		(new(0.26f, 0.15f, 0.08f), new(0.2f, 0.12f, 0.06f), false),    // carved walnut
		(new(0.38f, 0.37f, 0.34f), new(0.27f, 0.26f, 0.24f), true),    // tarnished silver-leaf
		(new(0.36f, 0.27f, 0.17f), new(0.3f, 0.22f, 0.14f), false),    // plain worn oak
		(new(0.62f, 0.47f, 0.2f), new(0.42f, 0.32f, 0.14f), true),     // brighter gilt, a few kept well
	};

	private static StandardMaterial3D _wood;
	private static StandardMaterial3D WoodMat => _wood ??= new StandardMaterial3D
	{
		VertexColorUseAsAlbedo = true, Roughness = 0.62f, MetallicSpecular = 0.35f,
		AlbedoTexture = LodgeParts.LodgeTextures.DarkWoodMat.AlbedoTexture, Uv1Scale = new Vector3(3f, 3f, 1f),
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	};

	private static float H(int which, int k) => StairwellTextures.PortraitHash(which, k);

	/// <summary>Portrait <paramref name="which"/>'s frame width (its moulding, past the canvas's edge).</summary>
	/// <summary>How far its moulding stands off the wall.</summary>
	public static float Depth(int which) => 0.03f + 0.04f * H(which, 4);

	public static float Rim(int which, float w, float h) => Mathf.Clamp(Mathf.Min(w, h) * (0.08f + 0.14f * H(which, 3)), 0.04f, 0.17f);

	/// <param name="empty">The canvas cut out of it: a dark backing board behind the frame, nothing for the eyes to open in.</param>
	/// <param name="backed">A board over its back (one that's come down: its back is seen).</param>
	public static void Build(Node3D parent, int which, float w, float h, bool empty = false, bool backed = false)
	{
		int style = (int)(H(which, 1) * Styles.Length) % Styles.Length;
		var (body, slipCol, metal) = Styles[style];
		float tone = 0.8f + 0.35f * H(which, 2);
		body *= tone;
		// frame width and depth: a broad gilt swell to a narrow slip, in proportion to the picture
		float fw = Rim(which, w, h);
		float d = Depth(which);
		var k = new MeshKit();
		k.Mat(metal ? ItemTextures.BrassMat : WoodMat);
		k.Color = body;
		if (!Modelled(k, style, w, h, fw, d, body, slipCol * tone, metal, which))
		{
		// the moulding: (2026-10-15, the props' proportions pass) one run round the canvas with mitred corners; its profile by style
		Vector2[] prof = style switch
		{
			0 or 2 or 5 => new Vector2[] { new(0f, 0.25f), new(0.04f, 0.3f), new(0.1f, 0.36f), new(0.16f, 0.3f), new(0.2f, 0.32f), new(0.3f, 0.5f), new(0.45f, 0.82f), new(0.58f, 0.96f), new(0.7f, 1f), new(0.8f, 0.94f), new(0.88f, 0.8f), new(0.94f, 0.62f), new(0.98f, 0.5f), new(1f, 0f) },
			1 => new Vector2[] { new(0f, 0.25f), new(0.05f, 0.3f), new(0.14f, 0.28f), new(0.2f, 0.26f), new(0.3f, 0.5f), new(0.6f, 0.7f), new(0.8f, 0.72f), new(0.92f, 0.62f), new(1f, 0.55f), new(1f, 0f) },
			3 => new Vector2[] { new(0f, 0.3f), new(0.08f, 0.4f), new(0.2f, 0.42f), new(0.35f, 0.75f), new(0.55f, 0.9f), new(0.8f, 0.85f), new(1f, 0.6f), new(1f, 0f) },
			// plain: a flat reverse slope to a rounded outer lip
			_ => new Vector2[] { new(0f, 0.55f), new(0.1f, 0.6f), new(0.7f, 0.85f), new(0.9f, 1f), new(1f, 0.8f), new(1f, 0f) },
		};
		for (int i = 0; i < prof.Length; i++) prof[i] = new Vector2(prof[i].X * fw, prof[i].Y * d);
		var slip = slipCol * tone;
		HollowProps.Moulding(k, w, h, prof, i => i < 3 ? slip : (i % 2 == 0 ? body : body * 0.86f));
		// corner bosses (rosettes) and a cartouche at the top middle on the carved ones (not every one: a boss or the
		// cartouche long since knocked off some)
		if (style is 0 or 2 or 5)
		{
			k.Color = body * 1.08f;
			var rosette = new Vector2[] { new(1f, 0f), new(0.95f, 0.35f), new(0.7f, 0.7f), new(0.35f, 0.9f), new(0.15f, 1f), new(0f, 1.05f) };
			int lost = H(which, 5) < 0.35f ? (int)(H(which, 6) * 4) : -1;
			int c4 = 0;
			foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
			{
				if (c4++ == lost) continue;
				Vector3 c = new(sx * (w * 0.5f + fw * 0.58f), sy * (h * 0.5f + fw * 0.58f), d * 0.85f);
				Rosette(k, c, fw * 0.32f, fw * 0.16f, rosette);
			}
			if (H(which, 7) < 0.6f) Rosette(k, new Vector3(0, h * 0.5f + fw * 0.95f, d * 0.7f), fw * 0.75f, fw * 0.3f, rosette, 0.62f);
		}
		}
		if (backed)
		{
			k.Mat(WoodMat);
			k.Color = new Color(0.2f, 0.16f, 0.11f);
			BuildKit.Box(k, new Vector3(0, 0, 0.003f), new Vector3(w + fw * 1.6f, h + fw * 1.6f, 0.006f), 2f);
		}
		k.CommitTo(parent, "Frame", true);
		if (empty)
		{
			// the canvas cut out along the slip: the backing board, a few ragged strips of canvas left at the edges
			var b = new MeshKit();
			b.Mat(WoodMat);
			b.Color = new Color(0.12f, 0.1f, 0.08f);
			BuildKit.Box(b, new Vector3(0, 0, 0.004f), new Vector3(w + fw * 0.3f, h + fw * 0.3f, 0.008f), 2f);
			b.Color = new Color(0.2f, 0.15f, 0.1f);
			for (int s = 0; s < 3; s++)
			{
				float sw = 0.04f + 0.05f * H(which, 20 + s), sh = h * (0.2f + 0.5f * H(which, 23 + s));
				float sx = (s % 2 == 0 ? -1f : 1f) * (w * 0.5f - sw * 0.5f);
				BuildKit.Box(b, new Vector3(sx, (H(which, 26 + s) - 0.5f) * (h - sh), 0.011f), new Vector3(sw, sh, 0.002f), 1f);
			}
			b.CommitTo(parent, "Backing", false);
			return;
		}
		// the canvas: its own sitter, its own burn, the soot from it scorched up the paint
		var canvas = new MeshInstance3D
		{
			Name = "Canvas", Mesh = new QuadMesh { Size = new Vector2(w, h) }, Position = new Vector3(0, 0, 0.012f),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoTexture = StairwellTextures.Portrait(which), Roughness = 0.7f, MetallicSpecular = 0.3f,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			},
		};
		parent.AddChild(canvas);
		// on some, the soot carried on up onto the frame's top rail
		if (H(which, 8) < 0.45f)
		{
			var soot = new MeshInstance3D
			{
				Name = "Soot", Mesh = new QuadMesh { Size = new Vector2(w * (0.35f + 0.3f * H(which, 9)), fw * 0.9f) },
				Position = new Vector3((H(which, 10) - 0.5f) * w * 0.2f, h * 0.5f + fw * 0.45f, d + 0.004f),
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = new Color(0.02f, 0.015f, 0.01f, 0.35f + 0.3f * H(which, 11)), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					AlbedoTexture = LakeParts.LakeFx.SoftDot(), Roughness = 1f,
				},
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			parent.AddChild(soot);
		}
	}

	/// <summary>(2026-10-15) The frame from its Blender pieces (tools/Blender/church_props.py pf_*): the style's moulding
	/// nine-sliced, a mitred corner scaled (rim, rim, depth) at each corner of the canvas and a straight rail scaled along
	/// each side, so the mitres stay true whatever the picture's shape; the carved styles' bosses and cartouche the carved
	/// pf_rosette. False if the pieces aren't there (the code's moulding is built instead).</summary>
	private static bool Modelled(MeshKit k, int style, float w, float h, float fw, float d, Color body, Color slip, bool metal, int which)
	{
		string p = style switch { 0 or 2 or 5 => "a", 1 => "b", 3 => "c", _ => "d" };
		string rail = "pf_rail_" + p, corner = "pf_corner_" + p;
		if (!FixtureModels.Have(rail, corner, "pf_rosette")) return false;
		var mat = metal ? (Material)ItemTextures.BrassMat : WoodMat;
		var roles = new System.Collections.Generic.Dictionary<string, (Material, Color)> { ["slip"] = (mat, slip), ["body"] = (mat, body), ["shade"] = (mat, body * 0.86f) };
		foreach (var (ang, pos, len) in new[] { (0f, new Vector3(0, h * 0.5f, 0), w), (Mathf.Pi, new Vector3(0, -h * 0.5f, 0), w), (-Mathf.Pi * 0.5f, new Vector3(w * 0.5f, 0, 0), h), (Mathf.Pi * 0.5f, new Vector3(-w * 0.5f, 0, 0), h) })
			FurnitureKit.Add(k, rail, new Transform3D(new Basis(Vector3.Back, ang) * Basis.FromScale(new Vector3(len, fw, d)), pos), roles);
		foreach (var (ang, pos) in new[] { (0f, new Vector3(w * 0.5f, h * 0.5f, 0)), (Mathf.Pi * 0.5f, new Vector3(-w * 0.5f, h * 0.5f, 0)), (Mathf.Pi, new Vector3(-w * 0.5f, -h * 0.5f, 0)), (-Mathf.Pi * 0.5f, new Vector3(w * 0.5f, -h * 0.5f, 0)) })
			FurnitureKit.Add(k, corner, new Transform3D(new Basis(Vector3.Back, ang) * Basis.FromScale(new Vector3(fw, fw, d)), pos), roles);
		if (style is 0 or 2 or 5)
		{
			var boss = new System.Collections.Generic.Dictionary<string, (Material, Color)> { ["body"] = (mat, body * 1.08f) };
			int lost = H(which, 5) < 0.35f ? (int)(H(which, 6) * 4) : -1;
			int c4 = 0;
			foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
			{
				if (c4++ == lost) continue;
				Vector3 c = new(sx * (w * 0.5f + fw * 0.58f), sy * (h * 0.5f + fw * 0.58f), d * 0.85f);
				FurnitureKit.Add(k, "pf_rosette", new Transform3D(Basis.FromScale(new Vector3(fw * 0.32f, fw * 0.32f, fw * 0.16f)), c), boss);
			}
			if (H(which, 7) < 0.6f)
				FurnitureKit.Add(k, "pf_rosette", new Transform3D(Basis.FromScale(new Vector3(fw * 0.75f, fw * 0.75f * 0.62f, fw * 0.3f)), new Vector3(0, h * 0.5f + fw * 0.95f, d * 0.7f)), boss);
		}
		return true;
	}

	/// <summary>A domed boss lying on the frame's face (turned about +Z), squashed to <paramref name="sy"/> of its width.</summary>
	private static void Rosette(MeshKit k, Vector3 c, float r, float h, Vector2[] prof, float sy = 1f)
	{
		var pts = new Vector2[prof.Length];
		for (int i = 0; i < prof.Length; i++) pts[i] = new Vector2(prof[i].X * r, prof[i].Y * h);
		HollowProps.In(k, new Transform3D(new Basis(Vector3.Right, Mathf.Pi * 0.5f), c), () => HollowProps.Lathe(k, Vector3.Zero, pts, 10, false, false, 1f, sy));
	}
}
