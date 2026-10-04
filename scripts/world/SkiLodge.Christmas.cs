using System.Collections.Generic;
using Godot;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// The lobby at Christmas, years after the party (the owner, 2026-10-01: the lobby "just has 1 couch in it, we need to
/// populate it with more furniture. We should make it christmas and have a tree and decorations too like it looks
/// like a christmas party happened years ago and the remnants are still around"; with the references: a big tree in
/// the open hall, garlands along the gallery, wreaths, club chairs and games tables, a tufted ottoman).
/// <list type="bullet">
/// <item>The tree: a big fir in the open hall, left standing years after; its needles dried a dull olive and shed in a
/// carpet round its foot, old glass baubles all over it, a tinsel garland, a string of lights almost all dead (the
/// few still alight glow steadily: nothing blinks), the star knocked askew. Presents under it, one torn open.</item>
/// <item>Garland swags along the gallery's railing, a garland and four stockings on the mantel, wreaths either side
/// of the front doors, a MERRY CHRISTMAS banner over them come loose at one end.</item>
/// <item>The party's leavings: the buffet along the front wall under its cloth (the punch bowl dry, the cake grey
/// on its stand, the bottles empty, glasses knocked over, the candles burnt down), party hats and broken baubles on
/// the floor, a strand of tinsel dropped.</item>
/// <item>More furniture: a second seating group on the west side (a chesterfield, two leather club chairs and a
/// tufted ottoman on a rug, a floor lamp), and a chess table by the east wall with a game left half played.</item>
/// </list>
/// All the pieces are modelled in Blender (tools/Blender/christmas.py) and go through FurnitureKit; where a piece
/// is missing it's simply left out.
/// </summary>
public partial class SkiLodge
{
	/// <summary>Where the tree stands (lodge-local, on the floor).</summary>
	public static readonly Vector3 TreeAt = new(-4.0f, FloorY, 5.6f);
	/// <summary>For tests: whether the tree was built.</summary>
	public bool HasTree { get; private set; }

	private static Dictionary<string, Material> _xmas;
	private static StandardMaterial3D Glossy(string name, Color c, float rough = 0.25f, float metal = 0.55f) => new()
	{
		ResourceName = name, AlbedoColor = c, Roughness = rough, Metallic = metal, MetallicSpecular = 0.6f,
	};
	private static StandardMaterial3D Matte(string name, Color c, float rough = 0.85f) => new() { ResourceName = name, AlbedoColor = c, Roughness = rough };

	/// <summary>The Christmas pieces' roles (the baubles and tinsel brightened 2026-10-03: in the hall's low light they read
	/// black): dried needles (the forest firs' spray photo, alpha-cut, faded to olive),
	/// old glass, faded papers, a steady warm glow for the few bulbs still alight.</summary>
	private static Dictionary<string, Material> Xmas => _xmas ??= new Dictionary<string, Material>
	{
		["needles"] = new StandardMaterial3D
		{
			ResourceName = "xmas_needles", AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/winter/fir_bough.png"),
			AlbedoColor = new Color(1.45f, 1.3f, 0.85f), Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.4f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		},
		["bark"] = LodgeTextures.DarkWoodMat,
		["needles_core"] = Matte("xmas_needles_core", new Color(0.09f, 0.12f, 0.06f), 1f),
		["bauble_red"] = Glossy("xmas_red", new Color(0.55f, 0.06f, 0.05f)),
		["bauble_gold"] = Glossy("xmas_gold", new Color(0.7f, 0.52f, 0.2f), 0.3f, 0.75f),
		["bauble_green"] = Glossy("xmas_green", new Color(0.08f, 0.3f, 0.13f)),
		["bauble_blue"] = Glossy("xmas_blue", new Color(0.1f, 0.15f, 0.42f)),
		["bauble_silver"] = Glossy("xmas_silver", new Color(0.62f, 0.62f, 0.6f), 0.35f, 0.8f),
		["bulb_dead"] = Glossy("xmas_bulb_dead", new Color(0.12f, 0.11f, 0.1f), 0.3f, 0.1f),
		["bulb_lit"] = new StandardMaterial3D { ResourceName = "xmas_bulb_lit", AlbedoColor = new Color(1f, 0.8f, 0.5f), EmissionEnabled = true, Emission = new Color(1f, 0.62f, 0.3f), EmissionEnergyMultiplier = 1.4f },
		["tinsel"] = Glossy("xmas_tinsel", new Color(0.6f, 0.48f, 0.2f), 0.45f, 0.8f),
		["star"] = LodgeTextures.GoldMat,
		["paper_red"] = Matte("xmas_paper_red", new Color(0.36f, 0.06f, 0.05f), 0.6f),
		["paper_green"] = Matte("xmas_paper_green", new Color(0.06f, 0.16f, 0.08f), 0.6f),
		["paper_gold"] = Glossy("xmas_paper_gold", new Color(0.42f, 0.32f, 0.13f), 0.5f, 0.5f),
		["paper_white"] = Matte("xmas_paper_white", new Color(0.6f, 0.58f, 0.52f), 0.7f),
		["ribbon"] = Matte("xmas_ribbon", new Color(0.42f, 0.05f, 0.04f), 0.45f),
		["felt"] = Matte("xmas_felt", new Color(0.3f, 0.05f, 0.04f), 1f),
		["fur"] = LodgeTextures.PillowMat,
		["wood"] = LodgeTextures.DarkWoodMat,
		["cloth"] = LodgeTextures.TableclothMat,
		["glass"] = BottleGlass,
		["metal"] = LodgeTextures.BrassMat,
		["card"] = Matte("xmas_card", new Color(0.32f, 0.07f, 0.25f), 0.8f),
		["cake"] = Matte("xmas_cake", new Color(0.36f, 0.35f, 0.3f), 1f),
		["plate"] = LodgeTextures.PorcelainMat,
		["upholstery"] = LodgeTextures.LeatherMat,
		["leather"] = LodgeTextures.LeatherMat,
		["top"] = LodgeTextures.BlackMat,
	};

	private void BuildChristmas()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		var x = Xmas;
		// ---- the tree, and what's under it
		// (its own instance, keeping its levels of detail: 90k triangles up close, a fraction across the hall)
		HasTree = FurnitureKit.Place(this, "xmas_tree", new Transform3D(new Basis(Vector3.Up, 0.4f), TreeAt), x, "ChristmasTree") != null;
		if (HasTree)
		{
			LodgeKit.Solid(_inBody, TreeAt + new Vector3(0, 1.5f, 0), new Vector3(1.6f, 3f, 1.6f), 0.4f);
			// the few bulbs still alight: a low, steady glow in its branches
			Light(TreeAt + new Vector3(0.3f, 2.2f, -0.4f), new Color(1f, 0.66f, 0.36f), 0.45f, 5f, "TreeGlow");
		}
		FurnitureKit.Add(k, "presents_a", TreeAt + new Vector3(1.0f, 0, -1.0f), 0.5f, x);
		FurnitureKit.Add(k, "presents_b", TreeAt + new Vector3(-1.1f, 0, -0.5f), -0.9f, x);
		FurnitureKit.Add(k, "present_open", TreeAt + new Vector3(0.1f, 0, -1.7f), 1.1f, x);
		// ---- the gallery's railing: swags of garland along it, three to a side
		Vector3[] edge = { HexAt(210f, BalconyIn), HexAt(270f, BalconyIn), HexAt(330f, BalconyIn) };
		for (int e = 0; e < 2; e++)
		{
			Vector3 a = edge[e], b = edge[e + 1], along = (b - a).Normalized();
			Vector3 inward = ((-(a + b) * 0.5f) with { Y = 0 }).Normalized();
			float len = a.DistanceTo(b);
			for (int s = 0; s < 3; s++)
			{
				var mid = a + along * len * (s + 0.5f) / 3f + inward * 0.12f + Vector3.Up * (UpperY + 1.0f);
				var basis = new Basis(along, Vector3.Up, -inward) * Basis.FromScale(new Vector3(len / 3f / 2.4f, 1f, 1f));
				FurnitureKit.Add(k, "garland", new Transform3D(basis, mid), x);
			}
		}
		// ---- the mantel: a garland along it, four stockings hung from it
		Vector3 fa = HexVert(1), fb = HexVert(2), fmid = (fa + fb) * 0.5f, fout = fmid.Normalized(), falong = (fb - fa).Normalized();
		Vector3 fc = fmid - fout * (0.4f + 0.75f);
		float fy = Mathf.Atan2(-fout.X, -fout.Z) + Mathf.Pi;
		FurnitureKit.Add(k, "garland", new Transform3D(new Basis(-falong, Vector3.Up, fout) * Basis.FromScale(new Vector3(1.55f, 0.55f, 1f)), fc - fout * 0.34f + Vector3.Up * 2.04f), x);
		for (int i = 0; i < 4; i++)
			FurnitureKit.Add(k, "stocking", fc - fout * 0.34f + falong * (-1.2f + i * 0.8f) + Vector3.Up * 1.78f, fy + (i % 2 == 0 ? 0.08f : -0.06f), x);
		// ---- wreaths either side of the front doors, and the banner over them, come loose at one end
		foreach (float wx in new[] { -2.7f, 2.7f })
			FurnitureKit.Add(k, "wreath", new Vector3(wx, 2.55f, HexIn - 0.03f), 0f, x);
		k.Mat(LodgeTextures.LinenMat);
		var banner = new Basis(Vector3.Back, -0.28f);
		var bc = new Vector3(0.2f, 3.9f, HexIn - 0.07f);
		k.Box(bc, new Vector3(4.6f, 0.5f, 0.012f), 1f, banner);
		SignKit.Text(this, "MERRY CHRISTMAS", bc - new Vector3(0, 0, 0.012f), banner * new Basis(Vector3.Up, Mathf.Pi), 0.26f, new Color(0.45f, 0.06f, 0.05f), shadow: false);
		k.Mat(LodgeTextures.IronMat);
		k.Cylinder(new Vector3(-2.15f, 4.55f, HexIn - 0.05f), new Vector3(-2.15f, 4.55f, HexIn - 0.12f), 0.015f, 0.015f, 5, true);   // its one nail still in
		// ---- the party's leavings: the buffet along the front wall
		var buffet = new Vector3(-4.0f, FloorY, 9.2f);
		if (FurnitureKit.Add(k, "party_table", buffet, 0f, x))
			LodgeKit.Solid(_inBody, buffet + new Vector3(0, 0.42f, 0), new Vector3(2.7f, 0.85f, 0.9f));
		// on the floor: party hats, broken baubles, a strand of tinsel, the needles the tree shed
		var rng = new RandomNumberGenerator { Seed = 1225 };
		foreach (var p in new[] { new Vector3(-2.6f, 0, 7.9f), new Vector3(-5.6f, 0, 7.6f), new Vector3(2.4f, 0, -0.6f), new Vector3(-6.4f, 0, 0.9f) })
			FurnitureKit.Add(k, "party_hat", p with { Y = FloorY }, rng.RandfRange(0f, Mathf.Tau), x);
		foreach (var p in new[] { TreeAt + new Vector3(1.5f, 0, 0.6f), TreeAt + new Vector3(-0.4f, 0, -2.1f), new Vector3(-1.8f, FloorY, 6.9f) })
			FurnitureKit.Add(k, "bauble_floor", p, rng.RandfRange(0f, Mathf.Tau), x);
		FurnitureKit.Add(k, "tinsel_floor", TreeAt + new Vector3(1.8f, 0, -0.4f), 0.7f, x);
		FurnitureKit.Add(k, "tinsel_floor", new Vector3(-6.2f, FloorY, 8.0f), 2.2f, x);
		// ---- a second seating group on the west side: a chesterfield facing in, two club chairs across a tufted ottoman, a rug
		// (clear of the gallery's corner post at the west vertex: first set there, the chesterfield ran into it)
		var w = new Vector3(-6.4f, FloorY, -2.3f);
		k.Mat(LodgeTextures.RugMat);
		k.Quad(w + new Vector3(2.3f, 0.006f, -1.9f), w + new Vector3(-2.3f, 0.006f, -1.9f), w + new Vector3(-2.3f, 0.006f, 1.9f), w + new Vector3(2.3f, 0.006f, 1.9f), Vector3.Up,
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		var seat = new Dictionary<string, Material>(Woodwork) { ["upholstery"] = LodgeTextures.LeatherMat };
		if (FurnitureKit.Add(k, "chesterfield_24", w + new Vector3(-1.8f, 0, 0), -Mathf.Pi * 0.5f, seat))
			LodgeKit.Solid(_inBody, w + new Vector3(-1.8f, 0.45f, 0), new Vector3(0.95f, 0.9f, 2.4f));
		foreach (float dz in new[] { -1.0f, 1.0f })
			if (FurnitureKit.Add(k, "club_chair", w + new Vector3(1.8f, 0, dz), Mathf.Pi * 0.5f + dz * 0.12f, x))
				LodgeKit.Solid(_inBody, w + new Vector3(1.8f, 0.4f, dz), new Vector3(0.9f, 0.8f, 0.9f));
		var velvet = new Dictionary<string, Material>(Woodwork) { ["upholstery"] = LodgeTextures.VelvetVioletMat };
		if (FurnitureKit.Add(k, "ottoman", w, Mathf.Pi * 0.5f, velvet))
			LodgeKit.Solid(_inBody, w + new Vector3(0, 0.22f, 0), new Vector3(0.8f, 0.44f, 1.5f));
		FurnitureKit.Add(k, "magazines", w + new Vector3(0.1f, 0.43f, 0.2f), 0.6f, Woodwork);
		FurnitureKit.Add(k, "wine_glass", w + new Vector3(-0.15f, 0.43f, -0.35f), 0f, Woodwork);
		var bulb = LodgeKit.Lamp(k, w + new Vector3(-2.0f, 0, -1.6f), 1.6f, Shade, true);
		Light(bulb, Warm, 0.6f, 5f);
		// ---- a chess table by the east wall, a game left half played, a club chair either side
		var c = new Vector3(8.6f, FloorY, 0.6f);
		if (FurnitureKit.Add(k, "chess_table", c, 0.15f, x))
			LodgeKit.Solid(_inBody, c + new Vector3(0, 0.37f, 0), new Vector3(0.75f, 0.74f, 0.75f));
		foreach (float dz in new[] { -0.85f, 0.85f })
			if (FurnitureKit.Add(k, "club_chair", c + new Vector3(0, 0, dz), dz > 0 ? 0.1f : Mathf.Pi - 0.15f, x))
				LodgeKit.Solid(_inBody, c + new Vector3(0, 0.4f, dz), new Vector3(0.9f, 0.8f, 0.9f));
		FurnitureKit.Add(k, "tumbler", c + new Vector3(0.28f, 0.745f, 0.25f), 0f, Woodwork);
		// ---- the party's other table, in the middle of the hall: a round table under the jacquard, the plates and
		// glasses left on it, four chairs pushed back from it, one knocked over
		var rt = new Vector3(1.6f, FloorY, 3.7f);
		LodgeKit.Table(k, rt, 0.3f, new Vector2(1.3f, 1.3f), 0.76f, LodgeTextures.DarkWoodMat, LodgeTextures.BrassMat, round: true);
		k.Xf = Transform3D.Identity;
		k.Mat(LodgeTextures.TableclothMat);
		k.Cylinder(rt + new Vector3(0, 0.42f, 0), rt + new Vector3(0, 0.775f, 0), 0.8f, 0.76f, 24, true);   // the cloth, fallen straight to a hand above the floor
		LodgeKit.Solid(_inBody, rt + new Vector3(0, 0.39f, 0), new Vector3(1.4f, 0.78f, 1.4f));
		for (int i = 0; i < 4; i++)
		{
			float a = 0.3f + i * Mathf.Pi * 0.5f + (i == 2 ? 0.35f : 0f);
			var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
			if (i == 3)
			{
				// knocked over backward, lying on its back
				var fallen = new Basis(Vector3.Up, a) * new Basis(Vector3.Right, -Mathf.Pi * 0.5f);
				FurnitureKit.Add(k, "dining_chair", new Transform3D(fallen, rt + d * 1.45f + Vector3.Up * 0.42f), Woodwork);
			}
			else FurnitureKit.Add(k, "dining_chair", rt + d * (1.05f + i * 0.12f), a + (i == 1 ? 0.4f : 0f), Woodwork);
		}
		float top = 0.785f;
		foreach (var (dx, dz, model, yaw) in new[] { (0.3f, 0.2f, "wine_glass", 0f), (-0.25f, 0.3f, "tumbler", 0f), (0.05f, -0.35f, "bottle_wine", 0.4f), (-0.3f, -0.15f, "wine_glass", 0f), (0.35f, -0.2f, "mug", 1.2f) })
			FurnitureKit.Add(k, model, rt + new Vector3(dx, top, dz), yaw, Woodwork);
		FurnitureKit.Add(k, "party_hat", rt + new Vector3(0.1f, top, 0.05f), 2.1f, x);
		FurnitureKit.Add(k, "bauble_floor", rt + new Vector3(-0.9f, 0, 1.1f), 0.5f, x);
		if (!k.IsEmpty) k.CommitTo(this, "Christmas", true);
	}
}
