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
	/// <summary>Whether the pieces put in now are dusty (every interior is long abandoned; room 201, kept perfect, isn't).</summary>
	public static bool Dusty = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-dust") < 0;

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
		return _models[name] = (mesh, Lifted(tex));
	}

	/// <summary>The cavity map, lifted where its bake came out too dark (the owner, 2026-10-03: the lodge's Christmas tree
	/// read black, "textures missing"): a dense piece (a tree, a garland, tinsel, a wreath) occludes itself nearly
	/// everywhere, and the map, multiplied into its colour and its light both, took most of its brightness. Below an
	/// average of <see cref="CavityFloor"/> the map is raised toward white until it averages that, keeping its contrast's
	/// shape (the creases still darkest).</summary>
	private static Texture2D Lifted(Texture2D tex)
	{
		if (tex == null) return null;
		var img = tex.GetImage();
		if (img == null) return tex;
		if (img.IsCompressed()) img.Decompress();
		img.ClearMipmaps();
		img.Convert(Image.Format.Rgba8);
		var small = (Image)img.Duplicate();
		small.Resize(64, 64, Image.Interpolation.Bilinear);
		float sum = 0f;
		for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) sum += small.GetPixel(x, y).R;
		float mean = sum / 4096f;
		if (mean >= CavityFloor) return tex;
		float k = (1f - CavityFloor) / Mathf.Max(1f - mean, 0.01f);
		var data = img.GetData();
		for (int i = 0; i < data.Length; i += 4)
			for (int c = 0; c < 3; c++)
				data[i + c] = (byte)Mathf.Clamp(255f - (255f - data[i + c]) * k, 0f, 255f);
		var lifted = Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, data);
		lifted.GenerateMipmaps();
		GD.Print($"[furniture] cavity lifted: {tex.ResourcePath.GetFile()} (average {mean:0.00} -> {CavityFloor:0.00})");
		return ImageTexture.CreateFromImage(lifted);
	}

	/// <summary>The darkest a piece's cavity map may average.</summary>
	public const float CavityFloor = 0.62f;

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
		string key = Dusty ? model : model + "#clean";
		if (_mats.TryGetValue((room, key), out var m)) return m;
		// dusty: one shader that is the dressed material and its dust together (furniture_dusty.gdshader); it had been
		// a second pass over every piece, ~7% of the lodge's frame
		if (Dusty && DustyFits(sm))
		{
			var sh = new ShaderMaterial { Shader = DustyShader, ResourceName = (sm.ResourceName ?? "m") + "_" + model + "_dusty" };
			sh.SetShaderParameter("albedo", sm.AlbedoColor);
			sh.SetShaderParameter("use_albedo_tex", sm.AlbedoTexture != null);
			if (sm.AlbedoTexture != null) sh.SetShaderParameter("albedo_tex", sm.AlbedoTexture);
			sh.SetShaderParameter("use_vertex_color", sm.VertexColorUseAsAlbedo);
			sh.SetShaderParameter("uv1_scale", sm.Uv1Scale);
			sh.SetShaderParameter("uv1_offset", sm.Uv1Offset);
			sh.SetShaderParameter("use_normal", sm.NormalEnabled && sm.NormalTexture != null);
			if (sm.NormalTexture != null) sh.SetShaderParameter("normal_tex", sm.NormalTexture);
			sh.SetShaderParameter("normal_scale", sm.NormalScale);
			sh.SetShaderParameter("roughness", sm.Roughness);
			sh.SetShaderParameter("use_roughness_tex", sm.RoughnessTexture != null);
			if (sm.RoughnessTexture != null) sh.SetShaderParameter("roughness_tex", sm.RoughnessTexture);
			sh.SetShaderParameter("metallic", sm.Metallic);
			sh.SetShaderParameter("specular", sm.MetallicSpecular);
			sh.SetShaderParameter("cavity_tex", cavity);
			sh.SetMeta("detail_kind", -1);
			_mats[(room, key)] = sh;
			return sh;
		}
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
		_mats[(room, key)] = d;
		return d;
	}

	private static Shader _dusty;
	private static Shader DustyShader => _dusty ??= GD.Load<Shader>("res://assets/shaders/furniture_dusty.gdshader");

	/// <summary>Whether a room material is plain enough to be drawn by the dusty furniture shader (opaque, lit, no glow,
	/// no triplanar mapping, no passes of its own): everything else keeps its own material, and no dust.</summary>
	private static bool DustyFits(StandardMaterial3D sm) =>
		sm.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && !sm.EmissionEnabled && sm.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded
		&& !sm.Uv1Triplanar && !sm.SubsurfScatterEnabled && sm.NextPass == null && !sm.RefractionEnabled && !sm.RimEnabled && !sm.ClearcoatEnabled && sm.CullMode == BaseMaterial3D.CullModeEnum.Back;

	/// <summary>Whether a piece exists (built and imported).</summary>
	public static bool Has(string model) => Load(model).mesh != null;

	/// <summary>Puts a piece into <paramref name="k"/>: standing at <paramref name="c"/> (on the floor) turned by
	/// <paramref name="yaw"/>, its front toward -Z before the turn, scaled by <paramref name="scale"/>; each role in
	/// <paramref name="roles"/> given the room's material. Returns false if the piece isn't there (the caller builds its
	/// own).</summary>
	public static bool Add(MeshKit k, string model, Vector3 c, float yaw, Dictionary<string, Material> roles, Vector3? scale = null)
		=> Add(k, model, new Transform3D(new Basis(Vector3.Up, yaw) * Basis.FromScale(scale ?? Vector3.One), c), roles);

	/// <summary>A piece on its own as a mesh in the room's materials (for a MultiMesh: the church's pews), or null.</summary>
	public static Mesh Mesh(string model, Dictionary<string, Material> roles)
	{
		var k = new MeshKit();
		return Add(k, model, Transform3D.Identity, roles) ? k.Commit() : null;
	}

	/// <summary>A heavy piece as its own instance under <paramref name="parent"/> (not merged into the room's mesh): it
	/// keeps the import's levels of detail, so from across the room it's drawn with a fraction of its triangles (the
	/// optimization pass, 2026-10-02: the lodge's Christmas tree is 90k triangles up close). Null if it isn't there.</summary>
	private static readonly HashSet<string> _unmapped = new();
	/// <summary>A surface whose role the caller gave no material for: it keeps the model's plain placeholder (the owner saw
	/// these as missing textures). Said once per model and role, so the previews and tests show it.</summary>
	private static void Unmapped(string model, string role)
	{
		if (_unmapped.Add(model + "/" + role)) GD.Print($"[furniture] {model}: no material for its '{role}' surfaces");
	}

	/// <summary>Every unmapped model/role met so far (tests).</summary>
	public static IReadOnlyCollection<string> UnmappedRoles => _unmapped;

	public static MeshInstance3D Place(Node parent, string model, Transform3D xf, Dictionary<string, Material> roles, string name = null)
	{
		var (mesh, cavity) = Load(model);
		if (mesh == null) return null;
		var mi = new MeshInstance3D { Name = name ?? model, Mesh = mesh, Transform = xf };
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			var src = mesh.SurfaceGetMaterial(s);
			if (src == null) continue;
			string role = src.ResourceName ?? "";
			int dot = role.IndexOf('.');
			if (dot > 0) role = role[..dot];
			if (!roles.TryGetValue(role, out var room)) room = roles.TryGetValue("*", out var any) ? any : null;
			if (room == null) Unmapped(model, role);
			if (room != null) mi.SetSurfaceOverrideMaterial(s, Dressed(room, model, cavity));
		}
		parent.AddChild(mi);
		return mi;
	}

	/// <summary>As <see cref="Add(MeshKit, string, Vector3, float, Dictionary{string, Material}, Vector3?)"/>, with any
	/// transform (a chair knocked over on its side).</summary>
	public static bool Add(MeshKit k, string model, Transform3D xf, Dictionary<string, Material> roles)
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
			if (room == null) Unmapped(model, role);
			if (room != null) map[src] = Dressed(room, model, cavity);
		}
		k.AddMesh(mesh, xf, map);
		return true;
	}
}
