using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The closed vault door before Act 7 (story: "if they find it earlier in the
/// game, it is locked"). Focusable so the prompt can say so, never usable.
/// Disabled once the door stands open.
/// </summary>
public partial class BunkerLockedHatch : Interactable
{
	public override bool CanInteract(PlayerController player) => false;
}
