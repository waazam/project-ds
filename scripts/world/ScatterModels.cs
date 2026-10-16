using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The woods' scatter meshes from Blender (tools/Blender/forest.py → assets/models/forest/&lt;key&gt;.glb; the owner, 2026-10-15:
/// every model is made in Blender). Each .glb's material slots are named for the game's own materials (tree_bark, fir_spray,
/// moss_rock, ...), and this swaps those in by name, so the shaders, their uniforms and the winter, detail and wind hooks are
/// the ones the code-built meshes used. A key whose .glb is missing (or `--code-scatter`, to compare) falls back to the old
/// code builder.
/// </summary>
public static class ScatterModels
{
	public const string Folder = "res://assets/models/forest/";
	private static readonly Dictionary<string, Mesh> _cache = new();
	private static readonly bool _codeOnly = Array.IndexOf(OS.GetCmdlineUserArgs(), "--code-scatter") >= 0;

	/// <summary>The Blender mesh for <paramref name="key"/> with the game's materials, or <paramref name="fallback"/>'s.</summary>
	public static Mesh Get(string key, Func<Mesh> fallback)
	{
		if (_cache.TryGetValue(key, out var m)) return m;
		m = _codeOnly ? null : Load(key);
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--scatter-tris") >= 0)
			GD.Print($"[forest] {key}: blender {Tris(m)} tris, code {Tris(fallback())} tris");
		if (m == null) m = fallback();
		_cache[key] = m;
		return m;
	}

	/// <summary>The .glb's mesh with its slots given the game's materials (null when it isn't there).</summary>
	public static Mesh Load(string key)
	{
		string path = Folder + key + ".glb";
		if (!ResourceLoader.Exists(path)) return null;
		if (GD.Load<PackedScene>(path)?.Instantiate() is not Node root) return null;
		var mi = root as MeshInstance3D ?? root.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().FirstOrDefault();
		var mesh = mi?.Mesh;
		if (mesh != null)
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
			{
				string name = mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
				var mat = Material(name);
				if (mat != null) mesh.SurfaceSetMaterial(i, mat);
				else GD.PushWarning($"[forest] {key}: no game material for slot '{name}'");
			}
		root.Free();
		return mesh;
	}

	private static int Tris(Mesh m)
	{
		if (m == null) return -1;
		int t = 0;
		for (int i = 0; i < m.GetSurfaceCount(); i++)
		{
			var a = m.SurfaceGetArrays(i);
			var idx = a[(int)Mesh.ArrayType.Index].AsInt32Array();
			t += idx.Length > 0 ? idx.Length / 3 : a[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
		}
		return t;
	}

	/// <summary>The game's material for a Blender slot name (forest.py's names).</summary>
	public static Material Material(string name) => name switch
	{
		"tree_bark" => ProcTextures.TreeBarkMat,
		"bark" => ProcTextures.BarkMat,
		"fir_branch" => ProcTextures.FirBranchMat,
		"fir_spray" => ProcTextures.FirSprayMat,
		"endgrain" => ProcTextures.EndGrainMat,
		"tree_endgrain" => ProcTextures.TreeEndGrainMat,
		"moss_rock" => ProcTextures.MossRockMat,
		"tree_leaf" => ProcTextures.TreeLeafMat,
		"ivy" => BunkerParts.BunkerTextures.IvyMat,
		"vine_core" => ForestScatter.MoundCoreMat,
		"moss" => ForestScatter.MossMat,
		"fern" => ForestScatter.FernMat,
		"fern_leaf" => ForestScatter.FernLeafMat,
		"grass" => ForestScatter.GrassMat,
		"litter" => ForestScatter.LitterMat,
		"wood" => ProcTextures.WoodMat,
		"metal" => ProcTextures.MetalMat,
		"palmetto" => ForestScatter.PalmMat,
		_ => null,
	};
}
