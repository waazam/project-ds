using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The small pickups and key items modelled in Blender (tools/Blender/items_small.py → assets/models/items/*.glb, 2026-10-15:
/// the owner's rule, every model made in Blender). Each item is a named mesh in its group's file, modelled in the same local
/// frame the old code built it in, so the caller places it with the old code's own transform. Every surface is painted with
/// the game's own material, named in Blender ("brass", "bark", "linen" ...); a "#rrggbb" suffix is the tint the old code gave
/// that part as its vertex colour (linear), applied here as the material's albedo. Returns null when the file or the mesh is
/// missing, and the caller builds its old code version.
/// </summary>
public static class ItemModels
{
	private static readonly Dictionary<string, Dictionary<string, Mesh>> _files = new();
	private static readonly Dictionary<string, Material> _tinted = new();

	/// <summary>The mesh named <paramref name="node"/> in assets/models/items/<paramref name="file"/>.glb, or null.</summary>
	public static Mesh Get(string file, string node)
	{
		if (!_files.TryGetValue(file, out var meshes))
		{
			meshes = new Dictionary<string, Mesh>();
			_files[file] = meshes;
			string path = $"res://assets/models/items/{file}.glb";
			if (ResourceLoader.Exists(path) && GD.Load<PackedScene>(path)?.Instantiate() is Node root)
			{
				foreach (var n in root.FindChildren("*", nameof(MeshInstance3D), true, false))
					if (n is MeshInstance3D mi && mi.Mesh != null) meshes[mi.Name] = mi.Mesh;
				root.Free();
			}
		}
		return meshes.TryGetValue(node, out var m) ? m : null;
	}

	/// <summary>Adds the item <paramref name="node"/> under <paramref name="parent"/> at <paramref name="xf"/>, painted. Null when
	/// it isn't modelled (the caller then builds the old one). <paramref name="paint"/> names materials only its caller knows.</summary>
	public static MeshInstance3D Add(Node3D parent, string file, string node, Transform3D xf, string name = "Mesh",
		Func<string, Material> paint = null, bool shadows = true)
	{
		var mesh = Get(file, node);
		if (mesh == null) return null;
		var mi = new MeshInstance3D
		{
			Name = name, Mesh = mesh, Transform = xf,
			CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
		};
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			string key = mesh.SurfaceGetMaterial(s)?.ResourceName ?? "";
			var m = Paint(key, paint);
			if (m != null) mi.SetSurfaceOverrideMaterial(s, m);
		}
		parent.AddChild(mi);
		return mi;
	}

	/// <summary>The game material for a Blender material name (with its tint), or null to keep the imported one.</summary>
	public static Material Paint(string key, Func<string, Material> extra = null)
	{
		if (string.IsNullOrEmpty(key)) return null;
		if (_tinted.TryGetValue(key, out var done)) return done;
		string baseName = key, hex = null;
		int h = key.IndexOf('#');
		if (h >= 0) { baseName = key[..h]; hex = key[(h + 1)..]; }
		var m = extra?.Invoke(baseName) ?? Base(baseName);
		// (only where the old code's tint reached: a material that takes the vertex colour as its albedo)
		// (and the compass needle, whose red north and white south are nothing but their tint)
		if (m != null && hex != null && hex.Length >= 6 && m is BaseMaterial3D bm && (bm.VertexColorUseAsAlbedo || baseName == "needle_red"))
		{
			var c = Color.FromHtml(hex.Substring(0, 6));
			var t = (BaseMaterial3D)bm.Duplicate();
			// (the old code's vertex colours are linear and multiply the texture; the albedo colour is sRGB)
			t.AlbedoColor = bm.AlbedoColor * c.LinearToSrgb();
			m = t;
		}
		_tinted[key] = m;
		return m;
	}

	private static Material Base(string name) => name switch
	{
		"brass" => ItemTextures.BrassMat,
		"chrome" => ItemTextures.ChromeMat,
		"edge" => ItemTextures.EdgeMat,
		"dial" => ItemTextures.DialMat,
		"glass" => ItemTextures.GlassMat,
		"needle_red" => ItemTextures.Tint("needle_red", Colors.White, 0.5f, 0.5f),
		"paper" => ProcTextures.PaperMat,
		"rock" => ProcTextures.RockMat,
		"bark" => ProcTextures.BarkMat,
		"endgrain" => ProcTextures.EndGrainMat,
		"wood" => ProcTextures.WoodMat,
		"porcelain" => LodgeParts.LodgeTextures.PorcelainMat,
		"gold" => LodgeParts.LodgeTextures.GoldMat,
		"velvet_violet" => LodgeParts.LodgeTextures.VelvetVioletMat,
		"mustard" => LodgeParts.LodgeTextures.MustardMat,
		"leather" => LodgeParts.LodgeTextures.LeatherMat,
		"linen" => LodgeParts.LodgeTextures.LinenMat,
		"lodge_brass" => LodgeParts.LodgeTextures.BrassMat,
		"steel_dark" => StairwellParts.StairwellTextures.SteelMat,
		_ => null,
	};

	public static int Triangles(MeshInstance3D mi) => mi == null ? 0 : ItemMeshes.CountTriangles(mi.Mesh);
}
