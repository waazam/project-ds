using Godot;

namespace ProjectDS.UI;

/// <summary>
/// Dev harness for the UI (not used by the game): ui_preview.tscn puts the
/// player and the HUD in the forest (no GameFlow, so no checkpoint is reached
/// and no save is written), then this driver — parked on the root so it
/// survives scene changes — screenshots the HUD with the crosshair, prompt,
/// hold ring, compass, item readout and both caption bands at once, the pause
/// menu, then presses "Quit to Menu" and checks the menu is live (tree
/// unpaused, items focusable, Settings opens). Shots go to test-output/ui/ at
/// 640x360 plus a nearest-scaled 1600x900 view; "-- --after" also writes the
/// before/after set to test-output/after/.
/// </summary>
public partial class UiPreview : Node3D
{
	public override void _Ready()
	{
		var driver = new UiPreviewDriver();
		GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
	}
}

