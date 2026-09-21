using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Sits at the cabin door. Does nothing until the player has been made to
/// climb the first staircase; once they come back with the door boarded
/// (done at the moment of the climb, in FirstClimbEvent), this plays the
/// reaction beat once and marks Act 3's checkpoint.
/// </summary>
public partial class CabinReturnEvent : StoryTrigger
{
	protected override bool AlreadyHappened(StoryManager s) => s.Current >= Checkpoint.Act3DoorBoarded;
	protected override bool CanFire(StoryManager s, PlayerController p) => s.StairsClimbed && s.Current < Checkpoint.Act3DoorBoarded;

	protected override void Fire(PlayerController player)
	{
		Cutscene.Run(this, async ct =>
		{
			await StoryBeat.Caption(this, "The door won't budge. Boarded shut, from the outside.", 1.2f, 4f, 1.2f);
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act3DoorBoarded);
		}, lockInput: true);
	}
}
