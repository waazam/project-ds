using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The look of places left a long time (the look pass, 2026-10-02):
/// <list type="bullet">
/// <item><b>Dust</b> (<c>dust_overlay.gdshader</c>): drawn over furniture and clutter, lying only on what faces up, in
/// patches. <see cref="Dust"/> over a whole subtree (its meshes and multimeshes; one overlay each). The frost of Act 23
/// takes its place where it falls.</item>
/// <item><b>Grime at the walls' feet</b>: where a wall meets its floor, a soft shadow on the floor and a dark band up
/// the wall's foot (the dark gathers in corners, and the scuffs and the wet were always worst low down). Drawn as two
/// thin strips multiplied over what's behind them (<see cref="ContactStrips"/>, called by the walls as they're built):
/// the look of baked occlusion at the price of a few quads.</item>
/// </list>
/// </summary>
public static class Weathering
{
	private static ShaderMaterial _dust;
	public static ShaderMaterial DustMat => _dust ??= MakeDust();

	private static ShaderMaterial MakeDust()
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/dust_overlay.gdshader"), ResourceName = "dust_overlay" };
		m.SetMeta("detail_kind", -1);
		return m;
	}

	/// <summary>Is this overlay the dust (so other overlays, the frost, may take its place)?</summary>
	public static bool IsDust(Material m) => m != null && m == _dust;

	/// <summary>Dust over every mesh and multimesh under <paramref name="root"/> that has no overlay of its own, and only
	/// where every surface is plain and opaque (an overlay is drawn over the whole of a mesh: over cut-out needles or
	/// webs it would show their cards' outlines).</summary>
	public static void Dust(Node root)
	{
		if (root is GeometryInstance3D g and (MeshInstance3D or MultiMeshInstance3D) && g.MaterialOverlay == null && Opaque(g)) g.MaterialOverlay = DustMat;
		foreach (var c in root.GetChildren()) Dust(c);
	}

	/// <summary>Dust over the named children of <paramref name="parent"/> (those it has).</summary>
	public static void Dust(Node parent, params string[] names)
	{
		foreach (var n in names) if (parent.GetNodeOrNull(n) is Node c) Dust(c);
	}

	private static bool Opaque(GeometryInstance3D g)
	{
		var mesh = g switch { MeshInstance3D mi => mi.Mesh, MultiMeshInstance3D mm => mm.Multimesh?.Mesh, _ => null };
		if (mesh == null) return false;
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			var m = (g as MeshInstance3D)?.GetSurfaceOverrideMaterial(s) ?? g.MaterialOverride ?? mesh.SurfaceGetMaterial(s);
			if (m is ShaderMaterial) return false;
			if (m is BaseMaterial3D b && (b.Transparency != BaseMaterial3D.TransparencyEnum.Disabled || b.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded || b.EmissionEnabled)) return false;
		}
		return true;
	}

	// ------------------------------------------------------------------ grime at the walls' feet

	private static StandardMaterial3D _contact;

	/// <summary>The strips' material: a soft gradient multiplied over the floor and the wall (dark at the corner, nothing
	/// at its far edge). Unshaded: it darkens whatever light falls there.</summary>
	public static StandardMaterial3D ContactMat => _contact ??= MakeContact();

	private static StandardMaterial3D MakeContact()
	{
		const int w = 8, h = 64;
		var img = Image.CreateEmpty(w, h, true, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
		{
			float v = y / (float)(h - 1);                  // 0 at the corner, 1 at the strip's far edge
			float k = Mathf.Pow(1f - v, 2.2f);            // a soft falloff, darkest in the corner
			float c = 1f - 0.5f * k;
			for (int x = 0; x < w; x++) img.SetPixel(x, y, new Color(c, c * 0.985f, c * 0.97f, 1f));
		}
		img.GenerateMipmaps();
		var m = new StandardMaterial3D
		{
			ResourceName = "contact_grime", AlbedoTexture = ImageTexture.CreateFromImage(img), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Mul, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureRepeat = false, RenderPriority = -1, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		m.SetMeta("detail_kind", -1);
		return m;
	}

	/// <summary>The grime along a wall's foot, on its <paramref name="side"/> (the face looking into the room): a strip
	/// on the floor (<paramref name="floorY"/>) out from the wall's face, and one up the face itself. <paramref name="a"/>
	/// and <paramref name="b"/> along the wall's face at floor level (any height: the floor's is used).</summary>
	public static void ContactStrips(MeshKit k, Vector3 a, Vector3 b, Vector3 side, float floorY, float reach = 0.42f, float up = 0.32f)
	{
		var n = (side with { Y = 0 }).Normalized();
		if (n.LengthSquared() < 0.5f || a.DistanceTo(b) < 0.2f) return;
		a.Y = b.Y = floorY;
		var lift = Vector3.Up * 0.006f;
		var off = n * 0.004f;
		var mat = k.CurrentMaterial;
		k.Mat(ContactMat);
		var c = k.Color;
		k.Color = Colors.White;
		float len = a.DistanceTo(b);
		// on the floor: from the face outward
		k.Quad(a + lift, a + n * reach + lift, b + n * reach + lift, b + lift, Vector3.Up,
			new Vector2(0, 0), new Vector2(0, 1), new Vector2(len, 1), new Vector2(len, 0));
		// up the wall: from the floor upward
		k.Quad(b + off, b + Vector3.Up * up + off, a + Vector3.Up * up + off, a + off, n,
			new Vector2(len, 0), new Vector2(len, 1), new Vector2(0, 1), new Vector2(0, 0));
		k.Color = c;
		if (mat != null) k.Mat(mat);
	}
}
