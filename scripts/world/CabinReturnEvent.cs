using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Sits on the cabin's porch. The first time the player comes up to the door while it is
/// still boarded (the Hollow: from the wake-up on), their one thought about it shows:
/// "Boarded shut. From the outside." No checkpoint. Once only, saved as
/// <see cref="SeenFlag"/>, so a Continue never repeats it (nor once the door is open).
/// </summary>
public partial class CabinReturnEvent : StoryTrigger
{
	public const string SeenFlag = "line_boarded_shut";
	public const string Line = "Boarded shut. From the outside.";

	protected override bool AlreadyHappened(StoryManager s)
		=> s.HasFlag(SeenFlag) || StoryBeat.CabinDoorOpen(s);

	protected override bool CanFire(StoryManager s, PlayerController p)
		=> s.StairsClimbed && !s.HasFlag(SeenFlag) && !StoryBeat.CabinDoorOpen(s) && StoryBeat.Cabin(this) is { DoorBoarded: true, IsOpen: false };

	protected override void Fire(PlayerController player)
	{
		StoryManager.Instance.SetFlag(SeenFlag);
		_ = Cutscene.Run(this, ct => StoryBeat.Caption(this, Line, 1.0f, 3.2f, 1.2f, ct));
	}
}
