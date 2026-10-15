using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// The hand tools and the newel post's cap, modelled in Blender (tools/Blender/held_tools.py → assets/models/tools/*.glb;
/// 2026-10-15, the owner: every model is made in Blender). One model per thing serves the hand (<see cref="HeldModels"/>),
/// the pickup on the ground (<see cref="ItemMeshes"/>) and, for the cap, every staircase's pier
/// (<see cref="StaircaseBuilder.NewelCapMesh"/>). The models are in grip space (the cap in cap space); their materials are
/// named slots, painted here with the game's own (the wear is in the vertex colours). Null when a file is missing: the
/// callers then build their old code version.
/// </summary>
public static class ToolModels
{
	private static string PathOf(string name) => $"res://assets/models/tools/{name}.glb";

	/// <summary>The model <paramref name="name"/> (axe, hammer, knife, lighter, walkie), painted, or null.</summary>
	public static Node3D Load(string name)
	{
		string path = PathOf(name);
		if (!ResourceLoader.Exists(path)) return null;
		var root = GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
		if (root == null) return null;
		root.Name = "Model";
		foreach (var n in root.FindChildren("*", nameof(MeshInstance3D), true, false))
		{
			var mi = (MeshInstance3D)n;
			for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
				if (For(mi.Mesh.SurfaceGetMaterial(i)?.ResourceName) is { } m) mi.SetSurfaceOverrideMaterial(i, m);
		}
		return root;
	}

	/// <summary>The game's material for a slot of the model.</summary>
	private static Material For(string slot)
	{
		if (string.IsNullOrEmpty(slot)) return null;
		int dot = slot.IndexOf('.');
		if (dot > 0) slot = slot[..dot];
		return slot switch
		{
			"hickory" => HeldModels.Hickory,
			"forged" => HeldModels.Forged,
			"honed" => HeldModels.Honed,
			"black" => HeldModels.BlackEnamel,
			"leather" => HeldModels.StackedLeather,
			"chrome" => HeldModels.Chrome,
			"brass" => HeldModels.Brass,
			"radio" => HeldModels.RadioPlastic,
			"rubber" => HeldModels.Rubber,
			"grille" => HeldModels.Grille,
			"label" => HeldModels.Label,
			"stone" => StairTextures.ConcreteMat,
			"break" => StairTextures.BreakMat,
			_ => null,
		};
	}

	/// <summary>The walkie's LED: its own red emissive material on the model's "Led" node (dark until it's lit).</summary>
	public static StandardMaterial3D Led(Node3D model, float energy)
	{
		var led = new StandardMaterial3D
		{
			ResourceName = "walkie_led", AlbedoColor = new Color(0.4f, 0.04f, 0.03f), Roughness = 0.25f,
			EmissionEnabled = true, Emission = new Color(1f, 0.08f, 0.04f), EmissionEnergyMultiplier = energy,
		};
		if (model.FindChild("Led", true, false) is MeshInstance3D mi) mi.MaterialOverride = led;
		return led;
	}

	private static ArrayMesh _cap;
	private static bool _capTried;

	/// <summary>The newel cap as one mesh in cap space, painted (shared by the stairs, the item and the hand), or null.</summary>
	public static ArrayMesh CapMesh
	{
		get
		{
			if (_capTried) return _cap;
			_capTried = true;
			var m = Load("newel_cap");
			if (m?.FindChild("NewelCap", true, false) is MeshInstance3D mi && mi.Mesh is ArrayMesh src)
			{
				_cap = (ArrayMesh)src.Duplicate();
				for (int i = 0; i < _cap.GetSurfaceCount(); i++)
					if (mi.GetSurfaceOverrideMaterial(i) is { } mat) _cap.SurfaceSetMaterial(i, mat);
			}
			m?.Free();
			return _cap;
		}
	}

	/// <summary>The transform of <paramref name="node"/> relative to <paramref name="model"/> (its parents' transforms down from it).</summary>
	public static Transform3D Under(Node3D model, Node3D node)
	{
		var chain = new System.Collections.Generic.List<Node3D>();
		for (Node p = node; p != null && p != model; p = p.GetParent()) if (p is Node3D p3) chain.Add(p3);
		var xf = Transform3D.Identity;
		for (int i = chain.Count - 1; i >= 0; i--) xf *= chain[i].Transform;
		return xf;
	}

	/// <summary>The model's bounds in <paramref name="space"/> (its own nodes' meshes, by their transforms under it).</summary>
	public static Aabb Bounds(Node3D model, Transform3D space)
	{
		var box = new Aabb();
		bool any = false;
		foreach (var n in model.FindChildren("*", nameof(MeshInstance3D), true, false))
		{
			var mi = (MeshInstance3D)n;
			var xf = space;
			var chain = new System.Collections.Generic.List<Node3D>();
			for (Node p = mi; p != null && p != model; p = p.GetParent()) if (p is Node3D p3) chain.Add(p3);
			for (int i = chain.Count - 1; i >= 0; i--) xf *= chain[i].Transform;
			var a = mi.GetAabb();
			for (int c = 0; c < 8; c++)
			{
				var p = xf * a.GetEndpoint(c);
				if (!any) { box = new Aabb(p, Vector3.Zero); any = true; } else box = box.Expand(p);
			}
		}
		return box;
	}
}
