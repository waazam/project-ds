using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Sits on the footbridge deck. Once the newel post is in hand (the Act 5 → 6
/// handoff), walking across advances the story and repoints the compass to
/// the Act 6 clearing. Crossing earlier or later does nothing — it only
/// reacts during that specific window, so the bridge stays a normal part of
/// the trail the rest of the time.
/// </summary>
public partial class BridgeCrossEvent : Area3D
{
	private bool _fired;

	public override void _Ready() => BodyEntered += OnEntered;

	private void OnEntered(Node3D body)
	{
		if (_fired || body is not PlayerController player) return;
		if (StoryManager.Instance is not { NewelPostTaken: true, Current: Checkpoint.Act5CabinEntered }) return;
		_fired = true;
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act6BridgeCrossed, player.GlobalPosition, player.CameraRig.Yaw);
		GD.Print("[story] Act 6: the bridge is crossed");
	}
}
