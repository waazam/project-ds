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
public partial class BridgeCrossEvent : StoryTrigger
{
	protected override bool AlreadyHappened(StoryManager s) => s.Current >= Checkpoint.Act6BridgeCrossed;
	protected override bool CanFire(StoryManager s, PlayerController p) => s is { NewelPostTaken: true, Current: Checkpoint.Act5CabinEntered };

	protected override void Fire(PlayerController player)
	{
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act6BridgeCrossed);
		GD.Print("[story] Act 6: the bridge is crossed");
	}
}
