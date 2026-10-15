using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The park's props, signs and dressing modelled in Blender (tools/Blender/park_props.py → assets/models/park/*.glb,
/// 2026-10-15: the owner's rule, every model made in Blender). Each model's parts are named for the game material that
/// paints them ("g_post", "g_signbrown", "g_rock"...): <see cref="Load"/> instances a model and paints every part with the
/// game's own material by that key (the props keep the game's textured woods, painted boards, concrete, bark and rock),
/// so the exported materials are only Blender's preview. Returns null when the model's file is missing: the caller then
/// builds its old stand-in in code.
/// </summary>
public static class PropModels
{
	private static readonly Dictionary<string, PackedScene> _scenes = new();

	public static bool Has(string name) => ResourceLoader.Exists(Path(name));

	private static string Path(string name) => $"res://assets/models/park/{name}.glb";

	/// <summary>An instance of model <paramref name="name"/>, painted; <paramref name="extra"/> answers first for keys only its
	/// caller knows (a material built for this one prop), else the shared table below.</summary>
	public static Node3D Load(string name, Func<string, Material> extra = null)
	{
		if (!_scenes.TryGetValue(name, out var scene))
		{
			scene = Has(name) ? GD.Load<PackedScene>(Path(name)) : null;
			_scenes[name] = scene;
		}
		var root = scene?.Instantiate<Node3D>();
		if (root == null) return null;
		Paint(root, extra);
		return root;
	}

	/// <summary>An instance placed at <paramref name="xf"/> under <paramref name="parent"/> (null when the model is missing).</summary>
	public static Node3D Place(Node parent, string name, Transform3D xf, Func<string, Material> extra = null)
	{
		var m = Load(name, extra);
		if (m == null) return null;
		parent.AddChild(m);
		m.Transform = xf;
		return m;
	}

	public static void Paint(Node root, Func<string, Material> extra = null)
	{
		var stack = new Stack<Node>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			var n = stack.Pop();
			foreach (var c in n.GetChildren()) stack.Push(c);
			if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
			{
				string key = Key(mi.Mesh.SurfaceGetMaterial(s)?.ResourceName) ?? Key(mi.Name);
				if (key == null) continue;
				var m = extra?.Invoke(key) ?? Shared(key);
				if (m != null) mi.SetSurfaceOverrideMaterial(s, m);
			}
		}
	}

	private static string Key(string name)
	{
		if (string.IsNullOrEmpty(name)) return null;
		int i = name.IndexOf("g_", StringComparison.Ordinal);
		if (i < 0) return null;
		string k = name[(i + 2)..];
		int dot = k.IndexOf('.');   // (Blender's duplicate suffixes: "g_post.001")
		return dot >= 0 ? k[..dot] : k;
	}

	private static StandardMaterial3D Tint(string key, Color c, float rough = 0.95f) => ProcTextures.Flat(key, c, rough);

	/// <summary>The game material for a model part's key (the same ones the code-built props used).</summary>
	public static Material Shared(string key) => key switch
	{
		"signpost" => BuildingTextures.SignPostBrownMat,
		"signbrown" => BuildingTextures.SignBrownMat,
		"signgreen" => BuildingTextures.SignGreenMat,
		"post" => PropTextures.PostMat,
		"plank" => PropTextures.SignPlankMat,
		"routed" => PropTextures.RoutedMat,
		"whitepaint" => PropTextures.WhitePaintMat,
		"map" => ProcTextures.MapMat,
		"paper" => ProcTextures.PaperMat,
		"metal" => ProcTextures.MetalMat,
		"concrete" or "pad" => ProcTextures.ConcreteMat,
		"rock" => ProcTextures.RockMat,
		"bark" => ProcTextures.BarkMat,
		"endgrain" => ProcTextures.EndGrainMat,
		"shingle" => BuildingTextures.ShingleMat,
		"iron" => BuildingTextures.IronMat,
		"canvas" => BuildingTextures.CanvasMat,
		"boards" => BuildingTextures.BoardsMat,
		"kiosk_roof" => Tint("kiosk_roof", new Color(0.10f, 0.09f, 0.08f)),
		"vent_pipe" => Tint("vent_pipe", new Color(0.08f, 0.08f, 0.08f), 0.6f),
		"dark" => Tint("prop_dark", new Color(0.05f, 0.045f, 0.04f)),
		"blaze" => Tint("blaze_faded", new Color(0.11f, 0.14f, 0.15f)),
		"pack_red" => Tint("pack_red", new Color(0.36f, 0.14f, 0.1f)),
		"pack_dark" => Tint("pack_dark", new Color(0.12f, 0.11f, 0.10f)),
		"pack_pad" => Tint("pack_pad", new Color(0.30f, 0.32f, 0.20f)),
		"boot_leather" => Tint("boot_leather", new Color(0.24f, 0.15f, 0.09f)),
		"boot_sole" => Tint("boot_sole", new Color(0.07f, 0.07f, 0.07f)),
		"boot_collar" => Tint("boot_collar", new Color(0.16f, 0.1f, 0.06f)),
		"boot_lace" => Tint("boot_lace", new Color(0.45f, 0.40f, 0.30f)),
		"stick_wood" => Tint("stick_wood", new Color(0.50f, 0.40f, 0.28f)),
		"stick_break" => Tint("stick_break", new Color(0.72f, 0.64f, 0.50f)),
		"stick_tip" => Tint("stick_tip", new Color(0.06f, 0.06f, 0.06f)),
		"stick_strap" => Tint("stick_strap", new Color(0.18f, 0.2f, 0.26f)),
		"tent_pole" => Tint("tent_pole", new Color(0.35f, 0.33f, 0.30f)),
		"tent_rope" => Tint("tent_rope", new Color(0.52f, 0.5f, 0.42f)),
		"tent_stake" => Tint("tent_stake", new Color(0.3f, 0.28f, 0.25f)),
		"ground_sheet" => Tint("tent_groundsheet", new Color(0.2f, 0.21f, 0.17f)),
		"pencil" => Tint("pencil", new Color(0.55f, 0.45f, 0.15f)),
		"crack" => ProcTextures.Flat("sign_crack", new Color(0.03f, 0.025f, 0.02f)),
		"gate_yellow" => ProcTextures.Flat("th_gate_yellow", new Color(0.74f, 0.57f, 0.13f), 0.75f, 0.3f),
		"reflector" => ProcTextures.Flat("th_reflector", new Color(0.6f, 0.14f, 0.1f), 0.5f, 0.5f),
		"sign_yellow" => ProcTextures.Flat("th_sign_yellow", new Color(0.62f, 0.5f, 0.1f), 0.8f, 0.3f),
		"sign_black" => ProcTextures.Flat("th_sign_black", new Color(0.05f, 0.045f, 0.04f), 0.9f, 0.2f),
		"moss_roof" => Tint("birdhouse_roof", new Color(0.36f, 0.4f, 0.3f)),
		"hole" => ProcTextures.Flat("birdhouse_hole", new Color(0.02f, 0.02f, 0.02f)),
		"pipeline_sign" => ProcTextures.Flat("pipeline_sign", new Color(0.78f, 0.77f, 0.72f), 0.8f),
		"pipeline_rim" => ProcTextures.Flat("pipeline_sign_rim", new Color(0.55f, 0.45f, 0.1f), 0.8f),
		_ => null,
	};
}
