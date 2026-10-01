using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The modelled furniture (tools/Blender/furniture.py: assets/models/furniture), put into a room's own mesh. Each piece's
/// faces are grouped by role (upholstery, wood, metal, linen, pillow, cover, top, shade...); the caller gives the room's
/// material for each role, so a chesterfield is in the lodge's own leather or velvet. Each piece has a baked cavity map
/// on its second UV set (the dark in the tufts and creases, where a leg meets the floor): multiplied in over the room's
/// material (as its detail layer) and used as its occlusion. The pieces' first UV set is the game's own box projection
/// (a metre a repeat), so the room's materials lie on them as on everything else.
/// </summary>
public static class FurnitureKit
{
	private static readonly Dictionary<string, (Mesh mesh, Texture2D cavity)> _models = new();
	private static readonly Dictionary<(Material, string), Material> _mats = new();

	private static (Mesh mesh, Texture2D cavity) Load(string name)
	{
		if (_models.TryGetValue(name, out var m)) return m;
		Mesh mesh = null;
		var path = $"res://assets/models/furniture/{name}.glb";
		if (ResourceLoader.Exists(path))
		{
			var scene = GD.Load<PackedScene>(path).Instantiate<Node>();
			mesh = Find(scene)?.Mesh;
			scene.Free();
		}
		var tex = ResourceLoader.Exists($"res://assets/models/furniture/{name}_cavity.png") ? GD.Load<Texture2D>($"res://assets/models/furniture/{name}_cavity.png") : null;
		return _models[name] = (mesh, tex);
	}

	private static MeshInstance3D Find(Node n)
	{
		if (n is MeshInstance3D mi) return mi;
		foreach (var c in n.GetChildren())
		{
			var f = Find(c);
			if (f != null) return f;
		}
		return null;
	}

	/// <summary>The room's material for a role, with this piece's cavity map multiplied in (cached per material and piece).</summary>
	private static Material Dressed(Material room, string model, Texture2D cavity)
	{
		if (cavity == null || room is not StandardMaterial3D sm) return room;
		if (_mats.TryGetValue((room, model), out var m)) return m;
		var d = (StandardMaterial3D)sm.Duplicate();
		d.ResourceName = (sm.ResourceName ?? "m") + "_" + model;
		d.DetailEnabled = true;
		d.DetailAlbedo = cavity;
		d.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mul;
		d.DetailUVLayer = BaseMaterial3D.DetailUV.UV2;
		d.DetailMask = null;
		d.AOEnabled = true;
		d.AOTexture = cavity;
		d.AOOnUV2 = true;
		d.AOLightAffect = 0.35f;
		d.AOTextureChannel = BaseMaterial3D.TextureChannel.Red;
		d.SetMeta("detail_kind", -1);   // (the detail layer is the cavity now: the room sweep mustn't overwrite it)
		_mats[(room, model)] = d;
		return d;
	}

	/// <summary>Whether a piece exists (built and imported).</summary>
	public static bool Has(string model) => Load(model).mesh != null;

	/// <summary>Puts a piece into <paramref name="k"/>: standing at <paramref name="c"/> (on the floor) turned by
	/// <paramref name="yaw"/>, its front toward -Z before the turn, scaled by <paramref name="scale"/>; each role in
	/// <paramref name="roles"/> given the room's material. Returns false if the piece isn't there (the caller builds its
	/// own).</summary>
	public static bool Add(MeshKit k, string model, Vector3 c, float yaw, Dictionary<string, Material> roles, Vector3? scale = null)
	{
		var (mesh, cavity) = Load(model);
		if (mesh == null) return false;
		var map = new Dictionary<Material, Material>();
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			var src = mesh.SurfaceGetMaterial(s);
			if (src == null) continue;
			string role = src.ResourceName ?? "";
			int dot = role.IndexOf('.');
			if (dot > 0) role = role[..dot];
			if (!roles.TryGetValue(role, out var room)) room = roles.TryGetValue("*", out var any) ? any : null;
			if (room != null) map[src] = Dressed(room, model, cavity);
		}
		var xf = new Transform3D(new Basis(Vector3.Up, yaw) * Basis.FromScale(scale ?? Vector3.One), c);
		k.AddMesh(mesh, xf, map);
		return true;
	}
}
