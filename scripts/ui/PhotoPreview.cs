using Godot;

namespace ProjectDS.UI;

/// <summary>
/// Dev harness for the Act 1 photo trip (not used by the game): photo_preview.tscn
/// puts the player, the HUD and a PhotoLog in the forest (no GameFlow,
/// so no checkpoint is reached and no save is written by play), then this parks
/// <see cref="PhotoPreviewDriver"/> on the root so it survives the Continue reload
/// it triggers at the end. Shots go to test-output/photo/.
/// </summary>
public partial class PhotoPreview : Node3D
{
	/// <summary>Before the PhotoLog child is ready: the preview's album lives in its own folder, never the player's.</summary>
	public override void _EnterTree() => Systems.PhotoLog.FolderOverride = "user://preview_photos/";

	public override void _Ready()
	{
		var driver = new PhotoPreviewDriver();
		GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
	}
}
