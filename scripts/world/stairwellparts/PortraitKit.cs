using Godot;

namespace ProjectDS.World.StairwellParts;

/// <summary>
/// The burnt portraits of Room 3 (Act 14): old oil portraits in heavy ornate frames, every one with
/// the sitter's face burnt out of the canvas. Frames vary: gilt with stepped mouldings and corner
/// bosses, black lacquer with a gold slip, dark carved walnut, tarnished silver-leaf. The canvas faces
/// +Z in the parent's space; (0,0) is its middle.
/// </summary>
public static class PortraitKit
{
	private static readonly Color[] Gilt = { new(0.78f, 0.6f, 0.26f), new(0.1f, 0.08f, 0.07f), new(0.3f, 0.19f, 0.11f), new(0.62f, 0.62f, 0.6f) };
	private static readonly Color[] Slip = { new(0.55f, 0.4f, 0.16f), new(0.72f, 0.56f, 0.24f), new(0.6f, 0.46f, 0.2f), new(0.4f, 0.4f, 0.38f) };

	public static void Build(Node3D parent, int which, float w, float h)
	{
		int style = which % 4;
		float fw = 0.07f + 0.03f * ((which * 7) % 3);   // frame width
		var k = new MeshKit();
		k.Mat(ItemTextures.BrassMat);
		k.Color = Gilt[style];
		// the frame: (2026-10-15, the props' proportions pass) one moulding run round the canvas with mitred corners: a
		// sight-edge slip, a bead, a cove, the swelling top round, an ogee down to the wall (it was three rings of square
		// bars butted at the corners); deeper and rounder for the gilt, flatter for the lacquer and the silver
		const float d = 0.055f;
		var prof = style is 0 or 2
			? new Vector2[] { new(0f, 0.25f), new(0.04f, 0.3f), new(0.1f, 0.36f), new(0.16f, 0.3f), new(0.2f, 0.32f), new(0.3f, 0.5f), new(0.45f, 0.82f), new(0.58f, 0.96f), new(0.7f, 1f), new(0.8f, 0.94f), new(0.88f, 0.8f), new(0.94f, 0.62f), new(0.98f, 0.5f), new(1f, 0f) }
			: new Vector2[] { new(0f, 0.25f), new(0.05f, 0.3f), new(0.14f, 0.28f), new(0.2f, 0.26f), new(0.3f, 0.5f), new(0.6f, 0.7f), new(0.8f, 0.72f), new(0.92f, 0.62f), new(1f, 0.55f), new(1f, 0f) };
		for (int i = 0; i < prof.Length; i++) prof[i] = new Vector2(prof[i].X * fw, prof[i].Y * d);
		var slip = Slip[style];
		var gilt = Gilt[style];
		HollowProps.Moulding(k, w, h, prof, i => i < 3 ? slip : (i % 2 == 0 ? gilt : gilt * 0.88f));
		// corner bosses (rosettes) and a cartouche at the top middle for the gilt and walnut ones
		k.Color = Gilt[style] * 1.1f;
		if (style == 0 || style == 2)
		{
			var rosette = new Vector2[] { new(1f, 0f), new(0.95f, 0.35f), new(0.7f, 0.7f), new(0.35f, 0.9f), new(0.15f, 1f), new(0f, 1.05f) };
			foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
			{
				Vector3 c = new(sx * (w * 0.5f + fw * 0.58f), sy * (h * 0.5f + fw * 0.58f), d * 0.85f);
				Rosette(k, c, fw * 0.32f, fw * 0.16f, rosette);
			}
			Rosette(k, new Vector3(0, h * 0.5f + fw * 0.95f, d * 0.7f), fw * 0.75f, fw * 0.3f, rosette, 0.62f);
		}
		k.CommitTo(parent, "Frame", true);
		// the canvas
		var canvas = new MeshInstance3D
		{
			Name = "Canvas", Mesh = new QuadMesh { Size = new Vector2(w, h) }, Position = new Vector3(0, 0, 0.012f),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoTexture = StairwellTextures.Portrait(which), Roughness = 0.55f, MetallicSpecular = 0.4f,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			},
		};
		parent.AddChild(canvas);
		// soot scorched up the canvas and onto the frame above the face
		var soot = new MeshInstance3D
		{
			Name = "Soot", Mesh = new QuadMesh { Size = new Vector2(w * 0.7f, h * 0.55f) }, Position = new Vector3(0, h * 0.32f, 0.016f),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.02f, 0.015f, 0.01f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				AlbedoTexture = LakeParts.LakeFx.SoftDot(), Roughness = 1f,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(soot);
	}

	/// <summary>A domed boss lying on the frame's face (turned about +Z), squashed to <paramref name="sy"/> of its width.</summary>
	private static void Rosette(MeshKit k, Vector3 c, float r, float h, Vector2[] prof, float sy = 1f)
	{
		var pts = new Vector2[prof.Length];
		for (int i = 0; i < prof.Length; i++) pts[i] = new Vector2(prof[i].X * r, prof[i].Y * h);
		HollowProps.In(k, new Transform3D(new Basis(Vector3.Right, Mathf.Pi * 0.5f), c), () => HollowProps.Lathe(k, Vector3.Zero, pts, 10, false, false, 1f, sy));
	}
}
