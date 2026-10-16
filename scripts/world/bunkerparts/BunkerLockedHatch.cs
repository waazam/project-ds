using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The closed vault door. Before the cabin fire it only says it's locked. From the fire on (2026-10-15, the owner: the code
/// became two halves of R.H.'s key) it can be used: its keyhole takes the key, the two halves joined in the hand first, or,
/// short of them, it says what's missing (<see cref="Bunker.UseKey"/>). The bunker owns what the key does. Gone with the
/// closed door once it stands open.
/// </summary>
public partial class BunkerLockedHatch : Interactable
{
	/// <summary>The bunker this door belongs to.</summary>
	public Bunker Door;

	// (Act 7 is reached at the bunker itself if the lookout was walked round: Bunker.OnApproach)
	private static bool Awake => StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning };
	private static bool Whole => StoryManager.Instance is { } s && s.HasFlag(StoryManager.Flag.KeyPiece(1)) && s.HasFlag(StoryManager.Flag.KeyPiece(2));

	public override bool CanInteract(PlayerController player) => Awake && base.CanInteract(player) && Door is { Unlocking: false };

	public override string GetPrompt(PlayerController player) => !Awake ? "Locked" : Whole ? "Put the key together and unlock it" : "Locked. A keyhole.";

	public override void Interact(PlayerController player)
	{
		base.Interact(player);
		Door?.UseKey(player);
	}
}
