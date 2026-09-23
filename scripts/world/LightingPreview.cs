using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// Dev harness (not used by the game): lighting_preview_day.tscn / lighting_preview_night.tscn put
/// the player in one of the worlds with no GameFlow, stand at a few points along the trail and
/// save what the camera sees, so the sky-to-forest balance and the light shafts can be judged
/// from screenshots (test-output/lighting/). Night: the Hollow with the Night mood set at once.
/// Quits when done.
/// </summary>
public partial class LightingPreview : Node3D
{
	[Export] public bool Night;
	[Export] public float[] TrailMetres = { 12f, 90f, 220f, 400f };

	public override void _Ready() => _ = Run();

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s, true), SceneTreeTimer.SignalName.Timeout);

	private async Task Run()
	{
		string outDir = ProjectSettings.GlobalizePath("res://test-output/lighting");
		DirAccess.MakeDirRecursiveAbsolute(outDir);
		await Frames(5);
		var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		var terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		var atmo = GetTree().GetFirstNodeInGroup("atmosphere") as ForestAtmosphere;
		if (player == null || terrain == null || atmo == null) { GD.Print("[lighting] missing player/terrain/atmosphere"); GetTree().Quit(1); return; }
		player.PlayerInput.Scripted = true;
		(GetTree().CurrentScene.FindChild("ScreenFader", true, false) as UI.ScreenFader)?.SetBlack(false);   // no GameFlow: lift the opening black
		if (Night) atmo.SetMood(ForestAtmosphere.Mood.Night, 0.05f);
		await Seconds(1.5);
		string tag = Night ? "night" : "day";
		foreach (float s in TrailMetres)
		{
			Vector3 at = terrain.TrailPoint(s, out var tan);
			at.Y = terrain.HeightAt(at.X, at.Z) + 0.1f;
			Vector3 look = terrain.TrailPoint(s + 30f, out _) + Vector3.Up * 1.6f;
			player.Teleport(at, Mathf.Atan2(-(look.X - at.X), -(look.Z - at.Z)));
			await Frames(4);
			var eye = player.CameraRig.Camera.GlobalPosition;
			Vector3 d = look - eye;
			player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
			player.CameraRig.SetPitch(Mathf.Atan2(d.Y + 4f, new Vector2(d.X, d.Z).Length()));   // a little up, so sky and canopy are in frame
			await Seconds(2.0);
			var img = GetViewport().GetTexture().GetImage();
			img.SavePng($"{outDir}/{tag}_{s:000}m.png");
			var env = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
			GD.Print($"[lighting] {tag} {s} m: shafts visible {atmo.Shafts?.VisibleCount} strength {atmo.Shafts?.Strength:0.00}, sky energy {env?.Environment.BackgroundEnergyMultiplier:0.00}, sky fog {env?.Environment.FogSkyAffect:0.00}, ambient {env?.Environment.AmbientLightEnergy:0.00}, mood {atmo.CurrentMood}");
		}
		GD.Print("[lighting] done");
		await Frames(2);
		GetTree().Quit(0);
	}
}
