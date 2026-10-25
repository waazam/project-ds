using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

public partial class CreaturePreview
{
	/// <summary>(2026-10-24) `--deadeye`: Act 13's dead eye on a dark floor under a warm lantern-like light: from the front,
	/// three-quarters, the side, behind and from above (the way it's first seen, in the drain's grate).</summary>
	private async Task DeadEyeShots()
	{
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(6, 6) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.14f, 0.12f) } });
		var holder = new Node3D { Name = "EyeHolder", Position = new Vector3(0, 0.14f, 0) };
		AddChild(holder);
		World.StationParts.StationProps.DeadEye(holder, 0.14f);
		AddChild(new OmniLight3D { Position = new Vector3(-0.5f, 0.9f, -0.9f), LightColor = new Color(1f, 0.78f, 0.5f), LightEnergy = 1.6f, OmniRange = 4f, ShadowEnabled = true });
		await Seconds(0.5);
		var c = holder.GlobalPosition;
		await Shot("deadeye_front", c + new Vector3(0.05f, 0.06f, -0.62f), c);
		await Shot("deadeye_three_quarter", c + new Vector3(0.45f, 0.18f, -0.45f), c);
		await Shot("deadeye_side", c + new Vector3(0.62f, 0.05f, 0f), c);
		await Shot("deadeye_back", c + new Vector3(0.1f, 0.2f, 0.65f), c);
		await Shot("deadeye_above", c + new Vector3(0.3f, 1.0f, -0.6f), c);
	}
}
