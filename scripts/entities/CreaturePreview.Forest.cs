using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

public partial class CreaturePreview
{
	/// <summary>`--forest-models`: each of the woods' scatter meshes as the old code built it (left) and as Blender builds it
	/// (right, tools/Blender/forest.py), side by side under a bright sky, close (forest_&lt;key&gt;.png).</summary>
	private async Task ForestModelShots()
	{
		env.BackgroundColor = new Color(0.82f, 0.86f, 0.9f);
		env.AmbientLightEnergy = 0.7f;
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 400) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.17f, 0.12f) } });
		var shots = new (string key, System.Func<Mesh> code, float size, float lookY)[]
		{
			("fir_young", () => World.ForestScatter.FirMesh(15, 9f, 0.17f, 0.10f, 8, 2.0f, 0.05f), 7f, 3f),
			("fir_mid", () => World.ForestScatter.FirMesh(13, 18f, 0.31f, 0.26f, 11, 3.0f, 0.15f), 11f, 6f),
			("snag", () => World.ForestScatter.SnagMesh(6, 9.5f), 7f, 3.5f),
			("decid_a", () => World.ForestScatter.DeciduousMesh(4), 7f, 3.5f),
			("sapling_a", () => World.ForestScatter.SaplingMesh(1, 5.5f), 4.5f, 2.5f),
			("stump_b", () => World.ForestScatter.StumpBrokenMesh(), 1.6f, 0.4f),
			("log", () => World.ForestScatter.LogMesh(), 4.5f, 0.3f),
			("branch_b", () => World.ForestScatter.BranchForkMesh(), 1.6f, 0.05f),
			("rock_a", () => World.ForestScatter.RockMesh(7), 2.4f, 0.3f),
			("boulder_a", () => World.ForestScatter.BoulderMesh(21), 3.5f, 0.5f),
			("vinemound_b", () => World.ForestScatter.VineMoundMesh(2, new Vector3(2.2f, 2.1f, 1.8f)), 4.5f, 1f),
		};
		float x = 0f;
		foreach (var (key, code, size, lookY) in shots)
		{
			var blender = World.ScatterModels.Load(key);
			float gap = size * 0.75f;
			var left = new MeshInstance3D { Mesh = code(), Position = new Vector3(x - gap, 0, 0) };
			var right = new MeshInstance3D { Mesh = blender, Position = new Vector3(x + gap, 0, 0) };
			AddChild(left);
			AddChild(right);
			await Seconds(0.4);
			await Shot($"forest_{key}", new Vector3(x, lookY + size * 0.25f, size * 1.25f), new Vector3(x, lookY, 0));
			left.QueueFree();
			right.QueueFree();
			x += 60f;
		}
	}
}
