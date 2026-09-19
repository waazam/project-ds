using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 5's payoff, inside the cabin: the player finds their friend, who
/// admits to boarding the door. Fires once the player is close, marks the
/// final checkpoint of this slice.
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
			await fader.ShowCaption("", "\"...You're okay. Thank god.\"", 1.0f, 3.0f, 1.0f);
			await fader.ShowCaption("", "\"I boarded it up. I saw something out there and I panicked.\"", 1.0f, 3.5f, 1.0f);
		}
		player.PlayerInput.SetEnabled(true);
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act5CabinEntered, player.GlobalPosition, player.CameraRig.Yaw);
	}
}
