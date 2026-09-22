using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for the Hollow (not used by the game): hollow_preview.tscn is an empty scene
/// that parks <see cref="HollowPreviewDriver"/> on the root, so it survives the Continue
/// reloads it drives: one real Continue into hollow.tscn per stop, from a synthetic save
/// (checkpoint, flags, inventory). The real save slots are backed up and put back.
/// Shots and the report go to test-output/hollow/.
/// Run with a window: `&lt;godot&gt; --path . res://scenes/levels/hollow_preview.tscn --windowed --resolution 1280x720`
/// (add `-- --only=route,wake,...` to limit the stops).
/// </summary>
public partial class HollowPreview : Node
{
	public override void _Ready()
	{
		var driver = new HollowPreviewDriver { Name = "HollowPreviewDriver" };
		GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
	}
}
