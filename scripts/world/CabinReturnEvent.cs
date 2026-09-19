using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Sits at the cabin door. Does nothing until the player has been made to
/// climb the first staircase; once they come back with the door boarded
/// (done at the moment of the climb, in FirstClimbEvent), this plays the
/// reaction beat once and marks Act 3's checkpoint.
/// </summary>
public partial class CabinReturnEvent : Area3D
{
	private bool _triggered;

	public override void _Ready() => BodyEntered += OnBodyEntered;

	private void OnBodyEntered(Node3D body)
	{
		if (_triggered || body is not PlayerController player) return;
		if (StoryManager.Instance is not { StairsClimbed: true }) return;
		_triggered = true;
		_ = Beat(player);
	}

	private async Task Beat(PlayerController player)
	{
		player.PlayerInput.SetEnabled(false);
		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
			await fader.ShowCaption("", "The door won't budge. Boarded shut, from the outside.", 1.2f, 4f, 1.2f);
		player.PlayerInput.SetEnabled(true);
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act3DoorBoarded, player.GlobalPosition, player.CameraRig.Yaw);
	}
}
