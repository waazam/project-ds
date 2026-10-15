using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The interactive close-ups modelled in Blender (tools/Blender/interactive.py → assets/models/furniture/*.glb, 2026-10-15:
/// the owner's rule, every model made in Blender): the valves, the cryptex, the puzzle box, the knife switches, the caged
/// lamps and bulbs, the CRT sets, the CRT room's shelving and console, the keypad. Each moving part is its own piece with
/// its origin on its pivot, so the builders keep turning the same nodes. Through <see cref="FurnitureKit"/> (each role
/// given the game's material and a tint, the piece's cavity map multiplied in), never dusty: they're handled.
/// Every call returns false / null when its model isn't there, and the caller builds its old stand-in.
/// </summary>
public static class KitModels
{
	public static bool Has(string model) => FurnitureKit.Has(model);

	/// <summary>The piece's mesh as imported (its own material set over it by the caller), or null.</summary>
	public static Mesh Raw(string model) => FurnitureKit.Raw(model);

	public static (string role, Material mat, Color tint) R(string role, Material mat) => (role, mat, Colors.White);
	public static (string role, Material mat, Color tint) R(string role, Material mat, Color tint) => (role, mat, tint);

	private static Dictionary<string, (Material, Color)> Map((string role, Material mat, Color tint)[] roles)
	{
		var d = new Dictionary<string, (Material, Color)>();
		foreach (var (role, mat, tint) in roles) d[role] = (mat, tint);
		return d;
	}

	/// <summary>Puts the piece into <paramref name="k"/> at <paramref name="xf"/> (merged with the rest).</summary>
	public static bool Add(MeshKit k, string model, Transform3D xf, params (string role, Material mat, Color tint)[] roles)
		=> Has(model) && FurnitureKit.Clean(() => FurnitureKit.Add(k, model, xf, Map(roles)));

	/// <summary>The piece as its own mesh under <paramref name="parent"/> (a moving part: a wheel, a ring, a key).</summary>
	public static MeshInstance3D Place(Node parent, string model, Transform3D xf, string name, bool shadows, params (string role, Material mat, Color tint)[] roles)
	{
		if (!Has(model)) return null;
		var k = new MeshKit();
		if (!Add(k, model, Transform3D.Identity, roles)) return null;
		var mi = k.CommitTo(parent, name, shadows);
		mi.Transform = xf;
		return mi;
	}
}
