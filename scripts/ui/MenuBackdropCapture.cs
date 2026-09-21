using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.World;

namespace ProjectDS.UI;

/// <summary>
/// Dev tool (not used by the game): loads the forest, frames the stairs in fog
/// from a few spots down the clearing, and saves raw 640x360 frames to
/// test-output/ui/backdrop_*.png. The chosen one is written to
/// res://assets/ui/menu_backdrop.png, which <see cref="MenuBackdrop"/> finishes
/// (grade, fog layers, grain, dither) at runtime. Re-run after the forest's
/// look changes:
///   Godot --path . res://scenes/ui/menu_backdrop_capture.tscn
/// </summary>
public partial class MenuBackdropCapture : Node3D
{
	[Export] public NodePath CameraPath = "Camera3D";
	/// <summary>Index into the candidate views that becomes the menu backdrop.</summary>
	[Export] public int Chosen = 1;

	private async Task Frames(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	public override async void _Ready()
	{
		var cam = GetNode<Camera3D>(CameraPath);
		string outDir = ProjectSettings.GlobalizePath("res://test-output/ui");
		DirAccess.MakeDirRecursiveAbsolute(outDir);
		await Frames(30);

		var terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		var trig = GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Node3D;
		var stairs = trig?.GetParent() as Node3D;
		if (terrain == null || stairs == null) { GD.PushError("[backdrop] no terrain or stairs"); GetTree().Quit(2); return; }
		Vector3 sp = stairs.GlobalPosition;
		Vector3 Near(float x, float z) { var q = sp + new Vector3(x, 0, z); q.Y = terrain.HeightAt(q.X, q.Z); return q + new Vector3(0, 1.62f, 0); }

		var views = new List<(Vector3 from, Vector3 to)>
		{
			(Near(-0.6f, 12f), sp + new Vector3(0, 2.4f, -2f)),
			(Near(0.8f, 11f), sp + new Vector3(0.3f, 2.6f, -2f)),
			(Near(1.8f, 12.5f), sp + new Vector3(0.6f, 2.8f, -2f)),
			(Near(-0.2f, 9.5f), sp + new Vector3(0, 2.8f, -2f)),
			(Near(0.4f, 13.5f), sp + new Vector3(0.2f, 2.5f, -2f)),
			(Near(-1.6f, 11f), sp + new Vector3(-0.2f, 2.6f, -2f)),
		};
		for (int i = 0; i < views.Count; i++)
		{
			cam.GlobalPosition = views[i].from;
			cam.LookAt(views[i].to, Vector3.Up);
			await Frames(12);
			var img = GetViewport().GetTexture().GetImage();
			img.SavePng($"{outDir}/backdrop_{i}.png");
			if (i == Chosen) img.SavePng(ProjectSettings.GlobalizePath(MenuBackdrop.BackdropPath));
			GD.Print($"[backdrop] view {i} saved{(i == Chosen ? " (menu backdrop)" : "")}");
		}
		GetTree().Quit();
	}
}
