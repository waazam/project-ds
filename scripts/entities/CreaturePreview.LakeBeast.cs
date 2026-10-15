using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

/// <summary>`--lakebeast`: Act 12's lake creature on a dark water plane, up and staring, close on a limb, its mouths, and a
/// slam caught frame by frame (2026-10-15, the owner: "less robotic and blocky").</summary>
public partial class CreaturePreview
{
	private async Task LakeBeastShots()
	{
		env.BackgroundColor = new Color(0.06f, 0.065f, 0.075f);
		env.AmbientLightEnergy = 0.9f;
		var water = new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(120, 120) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.07f, 0.07f), Roughness = 0.08f, MetallicSpecular = 0.9f },
			Position = new Vector3(300, 0, 0),
		};
		AddChild(water);
		var lake = new LakeCreature { Name = "LakeCreature", Position = new Vector3(300, 0, 0) };
		AddChild(lake);
		await Frames(2);
		lake.Breach(new Vector3(300, 0, 0), new Vector3(300, 0, 16), 30);
		await Seconds(3.5);
		lake.OpenEyes();
		await Seconds(1.0);
		await Shot("lakebeast_wide", new Vector3(300, 3f, 22f), new Vector3(300, 4f, 0));
		await Shot("lakebeast_close", new Vector3(303, 2.5f, 12f), new Vector3(300, 3f, 2f));
		await Shot("lakebeast_tips", new Vector3(300, 6f, 14f), new Vector3(300, 7.5f, 1f));
		await Shot("lakebeast_side", new Vector3(316, 3.5f, 4f), new Vector3(300, 4f, 0));
		// a slam toward the "boat", caught as it goes
		lake.Slam(new Vector3(300, 0, 16));
		for (int i = 0; i < 6; i++)
		{
			await Seconds(0.25);
			await Shot($"lakebeast_slam_{i}", new Vector3(312, 4f, 20f), new Vector3(300, 4f, 4f));
		}
		// the same view a second apart, to see the limbs move (no two in step)
		await Seconds(1.5);
		await Shot("lakebeast_sway_a", new Vector3(300, 3f, 22f), new Vector3(300, 4f, 0));
		await Seconds(0.8);
		await Shot("lakebeast_sway_b", new Vector3(300, 3f, 22f), new Vector3(300, 4f, 0));
	}
}
