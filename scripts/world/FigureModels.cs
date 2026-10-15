using Godot;

namespace ProjectDS.World;

/// <summary>
/// The minor figures modelled in Blender (tools/Blender/figures.py → assets/models/figures/*.glb, 2026-10-15: the owner's
/// rule, every model made in Blender): the stairwell's figure at the rail, the lodge's standing sheet, the lantern's moths.
/// Loads one, or returns null when its file is missing (the caller then builds its old stand-in).
/// </summary>
public static class FigureModels
{
	public static Node3D Load(string name)
	{
		string path = $"res://assets/models/figures/{name}.glb";
		if (!ResourceLoader.Exists(path)) return null;
		return GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
	}

	/// <summary>The mesh node named <paramref name="name"/> anywhere under <paramref name="root"/>.</summary>
	public static MeshInstance3D Mesh(Node root, string name) => root?.FindChild(name, true, false) as MeshInstance3D;

	/// <summary>Paints a mesh with the game's own material (the model's exported one is only Blender's preview).</summary>
	public static void Paint(MeshInstance3D mi, Material mat, bool shadows = true)
	{
		if (mi == null) return;
		mi.MaterialOverride = mat;
		mi.CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
	}
}
