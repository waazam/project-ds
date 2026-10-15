using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The player's own car: a plain mid-90s Japanese estate in faded burgundy (after the second-generation Subaru Legacy
/// wagon), backed into its bay with the tailgate lifted. Modelled in Blender by tools/Blender/car.py (2026-10-15b, the
/// owner: "make the car look less blocky and more real but keep it PS2 style"; the first pass's procedural skin still
/// read as stacked slabs): one smooth-shaded skin lofted through real sections, creased only at the shoulder, the belt
/// and the windscreen; 195/65 R15 tyres in their arches on steel wheels under plain trims; tinted glass with the cabin
/// inside it; the paint's fade, the mud, the dust and an ambient occlusion baked into its vertex colours, which these
/// materials multiply in (the first pass's material ignored them). About 7 k triangles.
/// Local frame: the tailgate end is +Z (the prop's front), the nose -Z; origin on the ground at the car's middle.
/// What the opening and the endings use is where it was: the load floor at 0.6 to the lip at z 2.3 (the camera lies on
/// it), the driver's eye at (-0.38, 1.15, 0.25) behind the wheel, under a roof at 1.43; the two collision boxes.
/// </summary>
public partial class ParkProp
{
	private const string CarModel = "res://assets/models/car/car.glb";
	private static readonly Dictionary<string, Material> _carMats = new();

	private void Car()
	{
		var model = GD.Load<PackedScene>(CarModel).Instantiate<Node3D>();
		model.Name = "Model";
		_gen.AddChild(model);
		int tris = 0;
		foreach (var n in model.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)n;
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
				mi.SetSurfaceOverrideMaterial(s, CarRole(mi.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? ""));
			tris += ItemMeshes.CountTriangles(mi.Mesh);
		}
		if (!Engine.IsEditorHint()) GD.Print($"[car] {tris} triangles");

		// the ground darkened under it (it sat on the lot's gravel unshadowed in the soft light)
		_gen.AddChild(new Decal
		{
			Name = "Shadow", Size = new Vector3(2.3f, 0.5f, 5.0f), Position = new Vector3(0, 0.05f, 0),
			TextureAlbedo = GD.Load<Texture2D>("res://assets/models/car/car_shadow.png"), Modulate = new Color(1, 1, 1, 0.85f),
			UpperFade = 0.3f, LowerFade = 0.3f, CullMask = 1,
		});

		const float W = 1.74f, L = 4.6f, belt = 0.88f, roof = 1.42f, zWs = -0.95f, zSeat = 1.15f;
		Col(new Vector3(0, (0.15f + belt) * 0.5f, 0), new Vector3(W, belt - 0.15f, L));
		Col(new Vector3(0, (belt + roof) * 0.5f, (zWs + zSeat) * 0.5f), new Vector3(W - 0.1f, roof - belt, zSeat - zWs));
	}

	/// <summary>The game's material for each of the model's roles (its glTF material names). Everything but the glass takes
	/// the vertex colours (the paint, the fade, the mud, the occlusion) times a small texture, and is drawn from inside too
	/// (the fifth ending's seat).</summary>
	private static Material CarRole(string role)
	{
		if (_carMats.TryGetValue(role, out var cached)) return cached;
		StandardMaterial3D Tex(string tex, float rough, float spec, float metal = 0f) => new()
		{
			ResourceName = role, AlbedoColor = Colors.White, VertexColorUseAsAlbedo = true, Roughness = rough, MetallicSpecular = spec,
			Metallic = metal, AlbedoTexture = GD.Load<Texture2D>($"res://assets/models/car/{tex}.png"),
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var m = role switch
		{
			"car_paint" => Tex("car_grain", 0.45f, 0.55f),
			"car_trim" => Tex("car_grain", 0.7f, 0.35f),
			"car_gap" => Tex("car_grain", 0.95f, 0.1f),
			"car_inner" => Tex("car_grain", 0.85f, 0.2f),
			"car_mirror" => Tex("car_grain", 0.12f, 0.9f, 0.85f),
			"car_seat" or "car_carpet" or "car_headliner" => Tex("car_fabric", 1f, 0.05f),
			"car_atlas" => Tex("car_atlas", 0.25f, 0.75f),
			"car_tyre" => Tex("car_tyre", 0.92f, 0.12f),
			"car_hubcap" => Tex("car_hubcap", 0.35f, 0.7f, 0.3f),
			_ => new StandardMaterial3D
			{
				// the glass: dark-tinted, glossy, the cabin behind it seen through
				ResourceName = role, AlbedoColor = new Color(0.03f, 0.04f, 0.045f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				Roughness = 0.04f, MetallicSpecular = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
		};
		_carMats[role] = m;
		return m;
	}
}
