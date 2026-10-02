using System.Collections.Generic;
using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// The creatures' remodelled parts (tools/Blender/creatures.py: assets/models/creatures/&lt;creature&gt;.glb, each part
/// an object named as the game's part, in its pivot's space, its tones in its vertex colours). Each body asks for its
/// parts by name and builds its old one where there isn't one.
/// </summary>
public static class CreatureModels
{
	private static readonly Dictionary<string, Dictionary<string, Mesh>> _sets = new();
	private static readonly Dictionary<(Mesh, float), Mesh> _scaled = new();

	/// <summary>Off: every body builds its own (the export for the Blender pass wants the originals).</summary>
	public static bool Disabled;

	/// <summary>The part <paramref name="part"/> of <paramref name="creature"/>, scaled by <paramref name="scale"/>; null if there isn't one.</summary>
	public static Mesh Get(string creature, string part, float scale = 1f)
	{
		if (Disabled) return null;
		if (!_sets.TryGetValue(creature, out var set))
		{
			set = new Dictionary<string, Mesh>();
			var path = $"res://assets/models/creatures/{creature}.glb";
			if (ResourceLoader.Exists(path))
			{
				var scene = GD.Load<PackedScene>(path).Instantiate<Node>();
				foreach (var n in scene.FindChildren("*", "MeshInstance3D", true, false))
					if (n is MeshInstance3D mi && mi.Mesh != null) set[mi.Name] = mi.Mesh;
				scene.Free();
			}
			_sets[creature] = set;
		}
		if (!set.TryGetValue(part, out var mesh)) return null;
		if (Mathf.IsEqualApprox(scale, 1f)) return mesh;
		if (_scaled.TryGetValue((mesh, scale), out var s)) return s;
		var am = new ArrayMesh();
		for (int i = 0; i < mesh.GetSurfaceCount(); i++)
		{
			var st = new SurfaceTool();
			st.AppendFrom(mesh, i, Transform3D.Identity.Scaled(Vector3.One * scale));
			st.Commit(am);
			am.SurfaceSetMaterial(am.GetSurfaceCount() - 1, mesh.SurfaceGetMaterial(i));
		}
		return _scaled[(mesh, scale)] = am;
	}

	/// <summary>The name of a part's surface's material (as the model has it: "skin", "dark", "limb").</summary>
	public static string SurfaceName(Mesh mesh, int surface)
	{
		var m = mesh.SurfaceGetMaterial(surface);
		string n = m?.ResourceName ?? "";
		int dot = n.IndexOf('.');
		return dot > 0 ? n[..dot] : n;
	}
}
