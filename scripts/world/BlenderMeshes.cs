using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Meshes modelled in Blender (the owner's rule, 2026-10-15: every model is made in Blender; tools/Blender/*.py →
/// assets/models/&lt;group&gt;/&lt;name&gt;.glb) handed to code that wants a plain <see cref="Mesh"/> (a MultiMesh's source, a
/// MeshInstance3D): the model's meshes merged into one, each surface given the game's own material by the name the
/// Blender material carries (the exported material is only Blender's preview). Null when the file is missing (the
/// caller then builds its old code stand-in), or with `--no-blender-props`.
/// </summary>
public static class BlenderMeshes
{
	private static readonly Dictionary<string, ArrayMesh> _cache = new();
	private static readonly bool _off = OS.GetCmdlineUserArgs().Contains("--no-blender-props");

	/// <param name="mats">Blender material name (without Blender's ".001" suffixes) → the game's material; null keeps the
	/// model's own.</param>
	public static ArrayMesh Get(string group, string name, Func<string, Material> mats)
	{
		if (_off) return null;
		string path = $"res://assets/models/{group}/{name}.glb";
		if (_cache.TryGetValue(path, out var hit)) return hit;
		if (!ResourceLoader.Exists(path)) return _cache[path] = null;
		var root = GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
		if (root == null) return _cache[path] = null;
		var mesh = new ArrayMesh { ResourceName = name };
		Collect(root, root, mesh, mats);
		root.Free();
		return _cache[path] = mesh.GetSurfaceCount() > 0 ? mesh : null;
	}

	/// <summary>The node <paramref name="name"/> in the model, instanced (for a prop placed whole); null if missing.</summary>
	public static Node3D Instance(string group, string name)
	{
		if (_off) return null;
		string path = $"res://assets/models/{group}/{name}.glb";
		return ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path)?.Instantiate<Node3D>() : null;
	}

	/// <summary>Repaints every surface of every mesh under <paramref name="root"/> with the game's materials by name.</summary>
	public static void Paint(Node root, Func<string, Material> mats)
	{
		if (root is MeshInstance3D mi && mi.Mesh != null)
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
				if (mats(BaseName(mi.Mesh.SurfaceGetMaterial(s)?.ResourceName)) is { } m) mi.SetSurfaceOverrideMaterial(s, m);
		foreach (var c in root.GetChildren()) Paint(c, mats);
	}

	/// <summary>(dev) A code-built mesh's surfaces, by a name for each material (null skips it), as plain arrays: for a
	/// Blender script that rebuilds part of a model and carries the rest (cards, mostly) over as the code laid it.</summary>
	public static Godot.Collections.Dictionary DumpSurfaces(Mesh m, Func<Material, string> name)
	{
		var all = new Godot.Collections.Dictionary();
		for (int s = 0; s < m.GetSurfaceCount(); s++)
		{
			string key = name(m.SurfaceGetMaterial(s));
			if (key == null) continue;
			var a = m.SurfaceGetArrays(s);
			var v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			var nr = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
			var uv = a[(int)Mesh.ArrayType.TexUV].AsVector2Array();
			var c = a[(int)Mesh.ArrayType.Color].AsColorArray();
			var idx = a[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? Enumerable.Range(0, v.Length).ToArray() : a[(int)Mesh.ArrayType.Index].AsInt32Array();
			all[key] = new Godot.Collections.Dictionary
			{
				["v"] = v.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
				["n"] = nr.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
				["uv"] = uv.SelectMany(p => new[] { p.X, p.Y }).ToArray(),
				["c"] = c.Length == v.Length ? c.SelectMany(p => new[] { p.R, p.G, p.B }).ToArray() : Array.Empty<float>(),
				["i"] = idx,
			};
		}
		return all;
	}

	public static string BaseName(string n)
	{
		if (string.IsNullOrEmpty(n)) return "";
		int dot = n.LastIndexOf('.');
		return dot > 0 && n.Length - dot == 4 && n[(dot + 1)..].All(char.IsDigit) ? n[..dot] : n;
	}

	private static void Collect(Node n, Node3D root, ArrayMesh into, Func<string, Material> mats)
	{
		if (n is MeshInstance3D mi && mi.Mesh != null)
		{
			var xf = Rel(root, mi);
			bool moved = !xf.IsEqualApprox(Transform3D.Identity);
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
			{
				var arr = mi.Mesh.SurfaceGetArrays(s);
				if (moved)
				{
					var v = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
					for (int i = 0; i < v.Length; i++) v[i] = xf * v[i];
					arr[(int)Mesh.ArrayType.Vertex] = v;
					if (arr[(int)Mesh.ArrayType.Normal].Obj is Vector3[] nr)
					{
						for (int i = 0; i < nr.Length; i++) nr[i] = (xf.Basis * nr[i]).Normalized();
						arr[(int)Mesh.ArrayType.Normal] = nr;
					}
				}
				var prim = mi.Mesh is ArrayMesh am ? am.SurfaceGetPrimitiveType(s) : Mesh.PrimitiveType.Triangles;
				into.AddSurfaceFromArrays(prim, arr);
				var own = mi.Mesh.SurfaceGetMaterial(s);
				into.SurfaceSetMaterial(into.GetSurfaceCount() - 1, (mats?.Invoke(BaseName(own?.ResourceName))) ?? own);
			}
		}
		foreach (var c in n.GetChildren()) Collect(c, root, into, mats);
	}

	private static Transform3D Rel(Node3D root, Node3D n)
	{
		var xf = Transform3D.Identity;
		for (Node p = n; p != null && p != root; p = p.GetParent())
			if (p is Node3D p3) xf = p3.Transform * xf;
		return xf;
	}
}
