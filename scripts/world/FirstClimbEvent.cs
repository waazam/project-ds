using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Sits at the foot of the first staircase the player finds. Stepping into it
/// takes movement away from the player entirely: they rise to the top landing
/// in a straight, silent, unnaturally smooth line, with no footsteps and no
/// say in it, then get control back. This is the story's Act 2 trigger; it
/// boards the cabin door and marks the checkpoint once the climb finishes.
/// </summary>
public partial class FirstClimbEvent : Area3D
{
	[Export] public NodePath TopMarkerPath = "../TopTrigger";
	[Export] public float ClimbSeconds = 6.5f;

	private bool _triggered;

	public override void _Ready() => BodyEntered += OnBodyEntered;

	private void OnBodyEntered(Node3D body)
	{
		if (_triggered || body is not PlayerController player) return;
		_triggered = true;
		_ = Climb(player);
	}

	private async Task Climb(PlayerController player)
	{
		var top = GetNode<Node3D>(TopMarkerPath);
		Vector3 dest = top.GlobalPosition;

		player.PlayerInput.SetEnabled(false);
		player.SetPhysicsProcess(false);
		var feet = player.GetNodeOrNull<PlayerFootsteps>("Footsteps");
		feet?.SetPhysicsProcess(false);
		player.Velocity = Vector3.Zero;

		var tween = player.CreateTween();
		tween.TweenProperty(player, "global_position", dest, ClimbSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await player.ToSignal(tween, Tween.SignalName.Finished);

		player.SetPhysicsProcess(true);
		feet?.SetPhysicsProcess(true);
		player.PlayerInput.SetEnabled(true);

		if (GetTree().GetFirstNodeInGroup("cabin") is Cabin cabin) cabin.SetBoarded(true);
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act2StairsClimbed, player.GlobalPosition, player.CameraRig.Yaw);

		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
			await fader.ShowCaption("", "It's getting late. Get back to the cabin.", 1.2f, 3.5f, 1.2f);
	}
}
