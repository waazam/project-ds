using System;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// An <see cref="Interactable"/> whose usability and prompt come from its owner
/// (a Pickup): a blocked pickup stays focusable so the prompt can say why
/// ("Hands full: you're carrying the axe"), but E does nothing.
/// </summary>
public partial class PickupInteractable : Interactable
{
	public Func<PlayerController, bool> CanUse;
	public Func<PlayerController, string> PromptFor;

	public override bool CanInteract(PlayerController player)
		=> base.CanInteract(player) && (CanUse?.Invoke(player) ?? true);

	public override string GetPrompt(PlayerController player)
		=> PromptFor?.Invoke(player) ?? base.GetPrompt(player);
}
