using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// The closed vault door. Before Act 7 it is simply locked (focusable so the prompt can say so,
/// never usable). From the cabin fire on, the dial on it can be tried: E opens the four-wheel
/// code lock (<see cref="UI.CodeLockOverlay"/>); the bunker owns what the right code does.
/// Gone with the closed door once it stands open.
/// </summary>
public partial class BunkerLockedHatch : Interactable
{
	private static bool DialReady => StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning };

	public override bool CanInteract(PlayerController player) => base.CanInteract(player) && DialReady;

	public override string GetPrompt(PlayerController player) => DialReady ? "Try the dial" : "Locked";
}
