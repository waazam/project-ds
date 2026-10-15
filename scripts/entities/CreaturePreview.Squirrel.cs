using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

/// <summary>`--squirrel` (2026-10-15): Act 1's grey squirrel (the Blender model, or the code fallback): foraging, sat
/// up, mid-bound, close on its head and its tail; then the doe close up (its head, its legs, its tail); and both models'
/// triangle counts.</summary>
public partial class CreaturePreview
{
	private async Task SquirrelShots()
	{
		var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(20, 20) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.22f, 0.17f) } };
		AddChild(ground);
		var poses = new (string name, int pose, float x)[] { ("forage", 0, -0.9f), ("sit_up", 1, 0f), ("bound", 2, 0.9f) };
		var made = new System.Collections.Generic.List<Squirrel>();
		foreach (var (name, pose, x) in poses)
		{
			var sq = new Squirrel { Name = $"Squirrel_{name}", Seed = 3, Position = new Vector3(x, 0, 0) };
			AddChild(sq);
			made.Add(sq);
		}
		await Frames(2);
		for (int i = 0; i < made.Count; i++) made[i].PreviewPose(poses[i].pose, 0.15f);
		made[0].RotationDegrees = new Vector3(0, -60, 0);
		made[1].RotationDegrees = new Vector3(0, -90, 0);
		made[2].RotationDegrees = new Vector3(0, -90, 0);
		await Seconds(0.5);
		await Shot("squirrel_row", new Vector3(0, 0.55f, 1.6f), new Vector3(0, 0.12f, 0));
		await Shot("squirrel_sit_up_close", new Vector3(0.0f, 0.3f, 0.65f), new Vector3(0, 0.16f, 0));
		await Shot("squirrel_bound_side", new Vector3(0.9f, 0.25f, 0.7f), new Vector3(0.9f, 0.1f, 0));
		await Shot("squirrel_forage_close", new Vector3(-0.9f + 0.45f, 0.3f, 0.45f), new Vector3(-0.9f, 0.08f, 0));
		// close: the forager's head (it faces -60 deg) and the sitter's tail
		await Shot("squirrel_head_close", new Vector3(-0.9f - 0.25f, 0.2f, -0.12f), new Vector3(-0.9f - 0.17f, 0.13f, -0.1f));
		await Shot("squirrel_tail_close", new Vector3(0.0f + 0.05f, 0.35f, -0.45f), new Vector3(0.12f, 0.3f, 0f));
		foreach (var s in made) s.Visible = false;

		var deer = new Deer { Name = "DeerClose", SpookRadius = 0f, RunSpookRadius = 0f, ShutterSpookRadius = 0f, Position = new Vector3(0, 0, 0) };
		AddChild(deer);
		await Seconds(1.0);
		await Shot("deer_close_head", new Vector3(0.75f, 1.35f, -1.55f), new Vector3(0, 1.25f, -0.95f));
		await Shot("deer_close_legs", new Vector3(1.1f, 0.4f, 0.1f), new Vector3(0, 0.32f, 0.05f));
		await Shot("deer_close_tail", new Vector3(0.7f, 1.0f, 1.5f), new Vector3(0, 0.85f, 0.6f));
		GD.Print($"[creature-preview] triangles: squirrel {made[0].TriangleCount} (rigged {made[0].Rigged}), doe {deer.TriangleCount} (rigged {deer.Rigged})");
	}
}
