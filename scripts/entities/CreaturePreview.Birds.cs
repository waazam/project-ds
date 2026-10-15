using System.Threading.Tasks;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>`--birds`, `--frog` (2026-10-15): Act 1's photo birds (the four colours, side, head, one in flight) and the creek
/// frog, as modelled in Blender (tools/Blender/birds.py); with `--old`, the code-built ones they replaced, for a
/// before/after. Shots go to test-output/creatures/ as birds_* / frog_* (old_ prefixed with --old).</summary>
public partial class CreaturePreview
{
	private static bool Old => System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--old") >= 0;
	private static string Pre => Old ? "old_" : "";

	private async Task BirdShots()
	{
		Bird.UseModel = !Old;
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(20, 20) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.22f, 0.17f) } });
		var colours = new[] { BirdColor.Red, BirdColor.Blue, BirdColor.Purple, BirdColor.BlackOmen };
		var birds = new Bird[colours.Length];
		for (int i = 0; i < colours.Length; i++)
		{
			birds[i] = new Bird { Name = $"Bird_{colours[i]}", Color = colours[i], Position = new Vector3(-0.75f + i * 0.5f, 0, 0), RotationDegrees = new Vector3(0, -90, 0) };
			AddChild(birds[i]);
		}
		await Frames(3);
		foreach (var b in birds) b.SetProcess(false);   // (held still for the shots)
		await Seconds(0.3);
		await Shot($"{Pre}birds_row", new Vector3(0, 0.45f, 1.7f), new Vector3(0, 0.18f, 0));
		await Shot($"{Pre}birds_red_side", new Vector3(-0.75f, 0.24f, 0.62f), new Vector3(-0.75f, 0.2f, 0));
		await Shot($"{Pre}birds_red_head", new Vector3(-0.75f + 0.22f, 0.3f, 0.2f), new Vector3(-0.75f + 0.08f, 0.26f, 0));
		await Shot($"{Pre}birds_omen", new Vector3(0.75f + 0.3f, 0.28f, 0.45f), new Vector3(0.75f, 0.22f, 0));
		// the blue one in flight, wings up, seen from below and behind
		birds[1].PreviewWings(true, 0.9f);
		await Frames(2);
		await Shot($"{Pre}birds_flight", new Vector3(-0.25f - 0.3f, 0.05f, 0.55f), new Vector3(-0.25f, 0.22f, 0));
		GD.Print($"[creature-preview] bird triangles: {birds[0].TriangleCount}");
	}

	private async Task FrogShots()
	{
		Frog.UseModel = !Old;
		var frog = new Frog { Name = "Frog", Seed = 3 };
		AddChild(frog);
		await Frames(3);
		await Seconds(0.4);
		var at = frog.GlobalTransform * new Vector3(0.02f, 0.11f, 0.01f);
		var side = frog.GlobalBasis.X.Normalized();
		var fwd = frog.GlobalBasis.Z.Normalized();
		await Shot($"{Pre}frog_close", at + side * 0.22f + fwd * 0.06f + Vector3.Up * 0.05f, at);
		await Shot($"{Pre}frog_front", at + fwd * 0.2f + side * 0.08f + Vector3.Up * 0.06f, at);
		await Shot($"{Pre}frog_rock", at + side * 0.6f + fwd * 0.35f + Vector3.Up * 0.3f, at);
	}
}
