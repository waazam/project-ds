using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

/// <summary>`--window-tentacle`: Act 13's window tentacle reached in and writhing, from the side and close on its tip
/// (2026-10-15: made in Blender). Add `--no-tentacle-model` for the code-built one, to compare.</summary>
public partial class CreaturePreview
{
	private async Task WindowTentacleShots()
	{
		env.BackgroundColor = new Color(0.06f, 0.065f, 0.075f);
		env.AmbientLightEnergy = 0.9f;
		var t = new WindowTentacle { Name = "WindowTentacle", Position = new Vector3(500, 1.2f, 0), Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0) };
		AddChild(t);
		await Frames(2);
		t.Visible = true;
		t.Reach = 1f;
		t.Writhe = 1f;
		await Seconds(1.2);
		await Shot("window_tentacle_side", new Vector3(504.5f, 2.2f, 1.5f), new Vector3(500, 1.2f, 2.0f));
		await Shot("window_tentacle_tip", new Vector3(501.6f, 1.8f, 3.6f), new Vector3(500, 1.2f, 2.3f));
		await Seconds(0.6);
		await Shot("window_tentacle_writhe", new Vector3(500.5f, 3.6f, 1.6f), new Vector3(500, 1.2f, 1.6f));
	}
}
