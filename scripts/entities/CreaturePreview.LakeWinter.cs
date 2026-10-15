using System.Threading.Tasks;
using Godot;
using ProjectDS.World;
using ProjectDS.World.LakeParts;

namespace ProjectDS.Entities;

/// <summary>
/// `--dump-lake-winter` (dev): the skeletons of the winter trees and the lake's ground heights, for tools/Blender/winter_trees.py
/// and tools/Blender/lake.py (tools/Blender/data/winter_trees.json, lake.json).
/// `--lake-winter`: the lake's pieces (cypress, reeds, marsh grass, wrack, driftwood, root wad, lily pad, the dock, the rescue
/// station) and the winter trees, photographed. Add `--no-blender-props` for the old code-built ones, to compare.
/// </summary>
public partial class CreaturePreview
{
	private void DumpLakeWinter()
	{
		string dir = ProjectSettings.GlobalizePath("res://tools/Blender/data");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		foreach (var (file, json) in new[] { ("winter_trees.json", WinterTreeKit.DumpRecipes()), ("lake.json", LakeStructures.DumpGround()),
			("cypress.json", Json.Stringify(LakeDressing.DumpCypressCrowns())) })
		{
			using var f = FileAccess.Open($"{dir}/{file}", FileAccess.ModeFlags.Write);
			f.StoreString(json);
			GD.Print($"[creature-preview] wrote {dir}/{file} ({json.Length} chars)");
		}
	}

	private async Task LakeWinterShots()
	{
		env.BackgroundColor = new Color(0.55f, 0.6f, 0.66f);
		env.AmbientLightEnergy = 1.0f;
		var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 400) }, Position = new Vector3(1000, -0.02f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.27f, 0.21f) } };
		AddChild(ground);
		var keys = new[] { "cypress_a", "cypress_b", "reed0", "reed_tall0", "reed_tall1", "marsh_mat", "wrack", "drift", "rootwad", "lily_pad" };
		for (int i = 0; i < keys.Length; i++)
		{
			var m = LakeDressing.PreviewMesh(keys[i]);
			if (m == null) continue;
			var x = new Vector3(1000 + i * 12f, 0, 0);
			AddChild(new MeshInstance3D { Name = keys[i], Mesh = m, Position = x });
		}
		await Frames(3);
		await Shot("lw_cypress", new Vector3(1006, 6f, 22f), new Vector3(1006, 6f, 0));
		await Shot("lw_cypress_foot", new Vector3(1003, 1.8f, 7f), new Vector3(1000, 0.6f, 0));
		await Shot("lw_reeds", new Vector3(1036, 1.6f, 6.5f), new Vector3(1036, 1.0f, 0));
		await Shot("lw_shore_bits", new Vector3(1084, 2.4f, 5.5f), new Vector3(1084, 0.1f, 0));
		await Shot("lw_lily_pad", new Vector3(1108.6f, 1.0f, 0.8f), new Vector3(1108, 0f, 0));

		// the structures (Lake-local), on their own
		var lake = new Node3D { Name = "LakeStub", Position = new Vector3(1500, 0, 0) };
		AddChild(lake);
		var rng = new RandomNumberGenerator { Seed = 77 };
		LakeStructures.BuildDock(lake, rng);
		var station = LakeStructures.BuildStation(lake, rng);
		await Frames(3);
		await Shot("lw_dock", new Vector3(1504f, 2.2f, 9f), new Vector3(1500f, 0.3f, -2f));
		var s = station.GlobalPosition;
		await Shot("lw_station", s + new Vector3(9f, 4.5f, 22f), s + new Vector3(0, 2.5f, 0));
		await Shot("lw_station_porch", s + new Vector3(3f, 1.7f, 10f), s + new Vector3(0, 1.4f, 4.5f));
		await Shot("lw_station_side", s + new Vector3(-20f, 6f, 6f), s + new Vector3(-6f, 3f, 0));

		// the winter trees
		var trees = new (string, Mesh)[]
		{
			("bare_501", WinterTreeKit.BareTree(501, 15f, 0)), ("bare_504", WinterTreeKit.BareTree(504, 22f, 1)),
			("fir_601", WinterTreeKit.WinterFir(601, 20f, 3.6f)), ("fir_602", WinterTreeKit.WinterFir(602, 14f, 2.8f)),
		};
		for (int i = 0; i < trees.Length; i++)
		{
			var t = WinterTreeKit.Single(trees[i].Item2, trees[i].Item1);
			t.Position = new Vector3(2000 + i * 14f, 0, 0);
			AddChild(t);
		}
		await Frames(3);
		await Shot("lw_winter_trees", new Vector3(2021, 9f, 34f), new Vector3(2021, 9f, 0));
		await Shot("lw_bare_close", new Vector3(2004, 3f, 8f), new Vector3(2000, 4.5f, 0));
		await Shot("lw_fir_close", new Vector3(2032, 4f, 9f), new Vector3(2028, 6f, 0));
	}
}
