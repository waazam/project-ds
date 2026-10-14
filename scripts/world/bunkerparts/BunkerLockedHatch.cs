using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The closed vault door. It is never used itself (focusable only so the prompt can say it's locked). The code goes
/// in on the keypad beside it (<see cref="BunkerKeypad"/>); once the keypad has woken (from the cabin fire on) the
/// prompt points there. The bunker owns what the right code does. Gone with the closed door once it stands open.
/// </summary>
public partial class BunkerLockedHatch : Interactable
{
	// (Act 7 is reached at the bunker itself if the lookout was walked round: Bunker.OnApproach)
	private static bool KeypadAwake => StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning };

	public override bool CanInteract(PlayerController player) => false;

	public override string GetPrompt(PlayerController player) => KeypadAwake ? "Locked. The keypad's to the right." : "Locked";
}
