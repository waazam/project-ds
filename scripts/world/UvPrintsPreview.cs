using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for uv_prints_preview.tscn (not used by the game): writes every blacklight print mask on one sheet
/// (<see cref="UvInk.PrintSheet"/>: the old handprint first, then each <see cref="HandKind"/> twice, then the foot) to
/// test-output/uvtour/prints_sheet.png and quits. Run with `-- --no-tex-cache` to see generator changes.
/// </summary>
public partial class UvPrintsPreview : Node
{
	public override void _Ready()
	{
		string dir = ProjectSettings.GlobalizePath("res://test-output/uvtour");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		UvInk.PrintSheet().SavePng($"{dir}/prints_sheet.png");
		GD.Print("[uv-prints] wrote prints_sheet.png");
		GetTree().Quit();
	}
}
