using Godot;

namespace ProjectDS.World;

/// <summary>
/// The weathered props (2026-10-24, the owner: "populate the game with more weathered objects to make it feel more lived
/// in", with photos of old coffee cans, tins, film canisters, a copper kettle, crocks, a rusted watering can, a burn
/// barrel): modelled and painted by tools/Blender/weathered.py (labels and wear from tools/Textures/make_weathered.py), in
/// assets/models/items/weathered.glb. Each keeps its own painted material. Names: CoffeeCan_Red/_Blue/_Yellow,
/// SpiceTin_Cinnamon, Tin_Tobacco, Tin_Tea, Film_Brown/_Orange/_Green, Matchbox_A/_B/_C, BurnBarrel, Kettle_Copper,
/// Crock_Jug, WateringCan; and the second batch (the owner: "painted signs, a filing cabinet and cowbells"): Sign_Arrow
/// (a hand-lettered arrow board on its post), Sign_Firewood (a roadside board on two stakes), Sign_Tin (an enamelled
/// NO TRESPASSING sign, its back at its origin), FilingCabinet (green steel, its back at its origin, drawers out along
/// +Z), Cowbell_Rust/_Brass (each hung from its strap's top at its origin).
/// </summary>
public static class WeatheredProps
{
	public const string File = "weathered";

	/// <summary>One prop under <paramref name="parent"/>, its base at <paramref name="at"/> (parent space), turned
	/// <paramref name="yawDeg"/>, tipped <paramref name="tiltDeg"/> about its own X (a can on its side: 90). Null if the
	/// model isn't there.</summary>
	public static MeshInstance3D Put(Node3D parent, string name, Vector3 at, float yawDeg = 0f, float tiltDeg = 0f, bool shadows = true)
	{
		var b = new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)) * new Basis(Vector3.Right, Mathf.DegToRad(tiltDeg));
		return ItemModels.Add(parent, File, name, new Transform3D(b, at), name, null, shadows);
	}

	/// <summary>A cabin's room lived in (Cabin.Interior's places, its own frame, the floor at y = 0): coffee cans and a spice
	/// tin on the shelf's lower board, the tea tin and three film canisters (a photographer's cabin) on the upper, the
	/// copper kettle on the stove's top, a jug on the floor by it, matches and a tobacco tin on the table, a rusted watering
	/// can at the porch's corner.</summary>
	public static void DressCabin(Node3D cabin, float ix, float iz, float hd, float sx, float sz, bool porch)
	{
		var root = new Node3D { Name = "Weathered" };
		cabin.AddChild(root);
		float shx = ix - 0.14f;
		// the shelf's lower board (its top at 1.066), past the jars: two cans and the spice tin
		Put(root, "CoffeeCan_Red", new Vector3(shx - 0.03f, 1.066f, 1.2f), 200f, 0f, false);
		Put(root, "CoffeeCan_Blue", new Vector3(shx - 0.05f, 1.066f, 1.35f), 150f, 0f, false);
		Put(root, "SpiceTin_Cinnamon", new Vector3(shx - 0.02f, 1.066f, 1.48f), 260f, 0f, false);
		// the upper board (1.516): the yellow can, the tea tin, the film
		Put(root, "CoffeeCan_Yellow", new Vector3(shx - 0.04f, 1.516f, 0.78f), 230f, 0f, false);
		Put(root, "Tin_Tea", new Vector3(shx - 0.04f, 1.516f, 0.94f), 300f, 0f, false);
		Put(root, "Film_Brown", new Vector3(shx - 0.06f, 1.516f, 1.42f), 250f, 0f, false);
		Put(root, "Film_Orange", new Vector3(shx - 0.02f, 1.516f, 1.46f), 280f, 0f, false);
		Put(root, "Film_Green", new Vector3(shx - 0.07f, 1.516f + 0.0125f, 1.5f), 10f, 90f, false);   // (one lying down)
		// the stove's top plate (0.75); the jug on the floor under the shelf, where the door's light finds it
		Put(root, "Kettle_Copper", new Vector3(sx - 0.04f, 0.75f, sz - 0.14f), 120f);
		Put(root, "Crock_Jug", new Vector3(ix - 0.24f, 0f, 1.32f), 60f);
		// the table (its top at 0.74, by the hub's and the Hollow's same place)
		Put(root, "Matchbox_A", new Vector3(0.66f, 0.741f, -1.12f), 23f, 0f, false);
		Put(root, "Tin_Tobacco", new Vector3(0.08f, 0.741f, -1.13f), 75f, 0f, false);
		if (porch) Put(root, "WateringCan", new Vector3(-(ix + 0.22f) + 0.45f, 0.0f, hd + 0.55f), 35f);
	}

	/// <summary>Two cowbells, the rusted one and the brass, on one nail at <paramref name="nail"/>, their flat sides
	/// turned <paramref name="yawDeg"/> (0: to +Z), the second a little behind and lower, swung a touch.</summary>
	public static void HangBells(Node3D parent, Vector3 nail, float yawDeg)
	{
		var b = new Basis(Vector3.Up, Mathf.DegToRad(yawDeg));
		Put(parent, "Cowbell_Rust", nail, yawDeg, 0f, false);
		var brass = Put(parent, "Cowbell_Brass", nail + b * new Vector3(0.05f, -0.03f, -0.012f), yawDeg + 8f, 0f, false);
		if (brass != null) brass.Basis = brass.Basis * new Basis(Vector3.Forward, 0.12f);
	}
}
