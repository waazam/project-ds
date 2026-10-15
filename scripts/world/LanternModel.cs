using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The hurricane lantern modelled in Blender (tools/Blender/lantern.py → assets/models/lantern/lantern.glb and
/// lantern_held.glb; 2026-10-15, the owner's rule: every model made in Blender). Same size and layout as the code-built one
/// it replaces (ItemMeshes.Lantern), so the grip, the hang and the flame line up. Each part is painted with the game's own
/// material by the name it carries from Blender: the globe gets ItemTextures.LampGlassMat itself and the flame
/// ItemTextures.FlameMat itself, so HeldLantern still finds them to light (its amber glass, the ember, the blacklight).
/// </summary>
public static class LanternModel
{
	private static readonly Dictionary<string, Material> _mats = new();

	/// <summary>Adds the lantern under <paramref name="parent"/> (held: the bail tall with its wooden grip; else folded).
	/// False when its file is missing (the caller then builds the old one).</summary>
	public static bool Add(Node3D parent, bool held)
	{
		string path = $"res://assets/models/lantern/{(held ? "lantern_held" : "lantern")}.glb";
		if (!ResourceLoader.Exists(path)) return false;
		var scene = GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
		if (scene == null) return false;
		scene.Name = "LanternModel";
		parent.AddChild(scene);
		foreach (var n in scene.FindChildren("*", nameof(MeshInstance3D), true, false))
		{
			if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
			for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
				if (For(mi.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "") is { } m) mi.SetSurfaceOverrideMaterial(i, m);
			if (mi.Name.ToString().StartsWith("Flame")) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		}
		return true;
	}

	/// <summary>The surface's material (an override if set, else the mesh's own): HeldLantern reads them through this.</summary>
	public static Material SurfaceMaterial(MeshInstance3D mi, int i) => mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i);

	private static Material For(string blenderName)
	{
		if (_mats.TryGetValue(blenderName, out var m)) return m;
		m = blenderName switch
		{
			"lantern_paint" => Tint(ItemTextures.PaintMat, new Color(0.42f, 0.08f, 0.05f)),
			"lantern_brass" => Tint(ItemTextures.BrassMat, new Color(0.9f, 0.76f, 0.46f)),
			"lantern_steel" => Tint(ItemTextures.SteelMat, new Color(0.4f, 0.4f, 0.42f)),
			"lantern_wood" => Tint(ItemTextures.TurnedWoodMat, new Color(0.36f, 0.22f, 0.13f)),
			"lantern_glass" => ItemTextures.LampGlassMat,
			"lantern_soot" => Tint(ItemTextures.LampGlassMat, new Color(0.35f, 0.3f, 0.26f, 0.8f)),
			"lantern_flame" => ItemTextures.FlameMat,
			"lantern_vent" => new StandardMaterial3D { ResourceName = "lantern_vent", AlbedoColor = new Color(0.05f, 0.04f, 0.035f), Roughness = 1f },
			_ => null,
		};
		_mats[blenderName] = m;
		return m;
	}

	/// <summary>The game's material in a colour of its own (the code-built parts carried theirs in vertex colours).</summary>
	private static Material Tint(StandardMaterial3D src, Color c)
	{
		var t = (StandardMaterial3D)src.Duplicate();
		var a = src.AlbedoColor;
		t.AlbedoColor = new Color(a.R * c.R, a.G * c.G, a.B * c.B, a.A * c.A);
		t.VertexColorUseAsAlbedo = false;
		return t;
	}
}
