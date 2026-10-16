using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

/// <summary>`--squirrel` (2026-10-15): Act 1's grey squirrel (the Blender model, or the code fallback) in each of its
/// clips side on (forage, sit_up, hop, run, climb), close on its head, its legs and its tail; then the doe in its clips
/// side on (graze, walk, trot, bound), grazing from the front (the chest), its head turned toward the camera (and not),
/// close on its head, its hooves, its belly against the dark and its tail; and both models' triangle counts.</summary>
public partial class CreaturePreview
{
	private async Task SquirrelShots()
	{
		var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(20, 20) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.22f, 0.17f) } };
		AddChild(ground);
		// each faces +x (its left side to the camera at +z)
		var poses = new (string name, int pose, float x, float t)[] { ("forage", 0, -1.2f, 0.15f), ("sit_up", 1, -0.6f, 0.15f), ("hop", 2, 0f, 0.1f), ("run", 4, 0.6f, 0.08f), ("climb", 3, 1.2f, 0.1f) };
		var made = new System.Collections.Generic.List<Squirrel>();
		foreach (var (name, pose, x, _) in poses)
		{
			var sq = new Squirrel { Name = $"Squirrel_{name}", Seed = 3, Position = new Vector3(x, 0, 0) };
			AddChild(sq);
			made.Add(sq);
		}
		await Frames(2);
		for (int i = 0; i < made.Count; i++)
		{
			made[i].PreviewPose(poses[i].pose, poses[i].t);
			made[i].RotationDegrees = new Vector3(0, -90, 0);
		}
		await Seconds(0.5);
		await Shot("squirrel_row", new Vector3(0, 0.35f, 1.7f), new Vector3(0, 0.1f, 0));
		await Shot("squirrel_sit_up_close", new Vector3(-0.6f + 0.15f, 0.22f, 0.55f), new Vector3(-0.6f, 0.15f, 0));
		await Shot("squirrel_hop_close", new Vector3(0f, 0.15f, 0.55f), new Vector3(0f, 0.1f, 0));
		await Shot("squirrel_run_close", new Vector3(0.6f, 0.15f, 0.55f), new Vector3(0.6f, 0.1f, 0));
		await Shot("squirrel_climb_close", new Vector3(1.2f, 0.15f, 0.55f), new Vector3(1.2f, 0.1f, 0));
		await Shot("squirrel_head_close", new Vector3(-0.78f, 0.17f, 0.2f), new Vector3(-0.97f, 0.12f, 0));
		await Shot("squirrel_legs_close", new Vector3(-1.15f, 0.07f, 0.42f), new Vector3(-1.18f, 0.05f, 0));
		await Shot("squirrel_tail_close", new Vector3(-0.62f, 0.3f, 0.55f), new Vector3(-0.78f, 0.25f, 0f));
		foreach (var s in made) s.Visible = false;

		var deer = new Deer { Name = "DeerClose", SpookRadius = 0f, RunSpookRadius = 0f, ShutterSpookRadius = 0f, Position = new Vector3(0, 0, 0) };
		AddChild(deer);
		await Seconds(0.5);
		var side = new Vector3(3.4f, 0.9f, -0.15f);
		var mid = new Vector3(0, 0.65f, -0.15f);
		foreach (var (clip, t) in new (string, float)[] { ("graze", 0.4f), ("walk", 0.3f), ("trot", 0.2f), ("bound", 0.1f), ("bound", 0.3f) })
		{
			deer.PreviewClip(clip, t);
			await Frames(2);
			await Shot($"deer_{clip}_side" + (clip == "bound" ? (t < 0.2f ? "_a" : "_b") : ""), side, mid);
		}
		deer.PreviewClip("graze", 0.4f);
		await Frames(2);
		await Shot("deer_graze_front", new Vector3(1.1f, 0.6f, -2.2f), new Vector3(0, 0.55f, -0.6f));
		// the head turned toward the camera, and for comparison not turned
		var eye = new Vector3(1.5f, 1.45f, -1.8f);
		deer.PreviewClip("head_up", 0f, 0f);
		await Frames(2);
		await Shot("deer_look_none", eye, new Vector3(0, 1.25f, -0.85f));
		float yaw = deer.YawToward(eye);
		deer.PreviewClip("head_up", 0f, yaw);
		await Frames(2);
		await Shot("deer_look_camera", eye, new Vector3(0, 1.25f, -0.85f));
		GD.Print($"[creature-preview] doe look yaw toward camera {yaw:0.00}");
		deer.PreviewClip("head_up", 0f, 0f);
		await Frames(2);
		await Shot("deer_close_head", new Vector3(0.75f, 1.35f, -1.55f), new Vector3(0, 1.25f, -0.95f));
		await Shot("deer_close_legs", new Vector3(1.1f, 0.4f, 0.1f), new Vector3(0, 0.32f, 0.05f));
		await Shot("deer_close_hooves", new Vector3(0.45f, 0.12f, -0.75f), new Vector3(0.1f, 0.04f, -0.37f));
		await Shot("deer_belly_low", new Vector3(1.6f, 0.3f, 0.0f), new Vector3(0, 0.72f, 0f));
		await Shot("deer_close_tail", new Vector3(0.7f, 1.0f, 1.5f), new Vector3(0, 0.85f, 0.6f));
		GD.Print($"[creature-preview] triangles: squirrel {made[0].TriangleCount} (rigged {made[0].Rigged}), doe {deer.TriangleCount} (rigged {deer.Rigged})");
	}
}
