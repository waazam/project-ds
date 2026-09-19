using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 5's payoff, inside the cabin: the player finds their friend at the
/// table, dead — the one who boarded the door, long before help could ever
/// have come from outside. Fires once the player is close, marks the
/// checkpoint, and leaves the newel post on the table for the player to find.
/// </summary>
public partial class FriendReveal : Area3D
{
	private bool _fired;

	public override void _Ready() => BodyEntered += OnEntered;

	private void OnEntered(Node3D body)
	{
		if (_fired || body is not PlayerController player) return;
		_fired = true;
		_ = Reveal(player);
	}

	private async Task Reveal(PlayerController player)
	{
		player.PlayerInput.SetEnabled(false);
		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
		{
			await fader.ShowCaption("", "He's in the chair. He's not moving.", 1.0f, 3.0f, 1.0f);
			await fader.ShowCaption("", "His hand — bandaged, cut clean off. He bled out before he ever boarded that door shut.", 1.2f, 4.0f, 1.2f);
			await fader.ShowCaption("", "Something sits on the table in front of him.", 1.0f, 2.8f, 1.0f);
		}
		player.PlayerInput.SetEnabled(true);
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act5CabinEntered, player.GlobalPosition, player.CameraRig.Yaw);
	}
}
