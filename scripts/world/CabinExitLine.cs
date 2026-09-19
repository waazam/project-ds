using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Sits at the cabin doorway. The first time the player steps back outside
/// carrying the newel post, the storm breaks, dawn comes up over the forest,
/// and a single line of narration plays — Act 5's last beat before the
/// compass leads them to the bridge.
/// </summary>
public partial class CabinExitLine : Area3D
{
	private bool _fired;

	public override void _Ready() => BodyExited += OnExited;

	private void OnExited(Node3D body)
	{
		if (_fired || body is not PlayerController player) return;
		var inv = player.GetNodeOrNull<PlayerInventory>("Inventory");
		if (inv is not { HasNewelPost: true }) return;
		_fired = true;
		StormController.Instance?.Deactivate();
		if (GetTree().Root.FindChild("Atmosphere", true, false) is ForestAtmosphere atmo)
			atmo.SetMood(ForestAtmosphere.Mood.Dawn, 10f);
		_ = ShowLine();
	}

	private async Task ShowLine()
	{
		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
			await fader.ShowCaption("", "\"He thrusts his fists against the posts...\"", 1.2f, 3.2f, 1.2f);
	}
}
