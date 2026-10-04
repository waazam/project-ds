using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The creatures' cold rim (rim_fresnel.gdshader; 2026-10-04): a faint cold edge on the silhouette, as an overlay on
/// every mesh under a creature, so it stands out of the dark and the fog without being lit up. One material per reach
/// (the giant is seen from forty metres; the rest close).
/// </summary>
public static class CreatureRim
{
	private static ShaderMaterial _near, _far;

	public static ShaderMaterial Material(bool far = false)
	{
		ref var m = ref far ? ref _far : ref _near;
		if (m == null)
		{
			m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/rim_fresnel.gdshader"), ResourceName = far ? "creature_rim_far" : "creature_rim" };
			if (far) { m.SetShaderParameter("far_fade", 180f); m.SetShaderParameter("strength", 0.07f); }
		}
		return m;
	}

	/// <summary>Puts the rim over every mesh under <paramref name="root"/> (deferred: after the creature has built itself).</summary>
	public static void Apply(Node root, bool far = false) => Callable.From(() => ApplyNow(root, far)).CallDeferred();

	/// <summary>Only on what's solid: not on the see-through cards (a breath, a glow, the shadow man's blur), which the rim
	/// would draw out of nothing; nor on a skin that fades itself (the stalker's: its rim is its skin's own).</summary>
	private static bool Solid(MeshInstance3D mi)
	{
		for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
		{
			var m = mi.GetActiveMaterial(s);
			if (m is BaseMaterial3D b && (b.Transparency != BaseMaterial3D.TransparencyEnum.Disabled || b.BillboardMode != BaseMaterial3D.BillboardModeEnum.Disabled)) return false;
			if (m is ShaderMaterial sm && sm.Shader != null && sm.Shader.Code.Contains("visibility")) return false;
		}
		return true;
	}

	private static void ApplyNow(Node n, bool far)
	{
		if (!GodotObject.IsInstanceValid(n)) return;
		if (n is MeshInstance3D mi && mi.MaterialOverlay == null && mi.Mesh != null && Solid(mi)) mi.MaterialOverlay = Material(far);
		foreach (var c in n.GetChildren()) ApplyNow(c, far);
	}
}
