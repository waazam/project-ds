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
		// the frame: three stepped mouldings, each a ring of four bars
		for (int step = 0; step < 3; step++)
		{
			float inset = step * fw * 0.33f, depth = 0.05f - step * 0.012f, z = depth * 0.5f;
			float ow = w * 0.5f + fw - inset, oh = h * 0.5f + fw - inset, bw = fw * 0.36f;
			if (step == 2) k.Color = Slip[style];
			Bar(k, new Vector3(0, oh - bw * 0.5f, z), new Vector3(ow * 2f, bw, depth));
			Bar(k, new Vector3(0, -oh + bw * 0.5f, z), new Vector3(ow * 2f, bw, depth));
			Bar(k, new Vector3(-ow + bw * 0.5f, 0, z), new Vector3(bw, oh * 2f, depth));
			Bar(k, new Vector3(ow - bw * 0.5f, 0, z), new Vector3(bw, oh * 2f, depth));
		}
		// corner bosses and a cartouche at the top middle for the gilt and walnut ones
		k.Color = Gilt[style] * 1.1f;
		if (style == 0 || style == 2)
		{
			foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
			{
				Vector3 c = new(sx * (w * 0.5f + fw * 0.6f), sy * (h * 0.5f + fw * 0.6f), 0.055f);
				k.Cylinder(c + Vector3.Back * -0.02f, c + Vector3.Back * 0.02f, fw * 0.45f, fw * 0.3f, 8, true);
			}
			Vector3 top = new(0, h * 0.5f + fw, 0.05f);
			k.Cylinder(top + Vector3.Back * -0.02f, top + Vector3.Back * 0.03f, fw * 0.9f, fw * 0.5f, 10, true);
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

	private static void Bar(MeshKit k, Vector3 c, Vector3 s) => BuildKit.Box(k, c, s, 3f);
}
