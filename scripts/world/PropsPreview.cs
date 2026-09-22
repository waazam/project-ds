using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for props_preview.tscn (not used by the game): the real trailhead level
/// (trail_slice.tscn, with GameFlow) started as a new game, so the opening at the car plays
/// exactly as it does for the player: the cards over black, the fade-in at the open hatch, the
/// camera lifting out of the trunk, the head turning to the trail, the how-to line. Screenshots
/// at each of those moments and of the car from the trail, into test-output/props/. The album
/// goes to its own folder and the real save slots are the caller's to back up and restore (the
/// opening reaches checkpoint 1, which saves).
/// </summary>
public partial class PropsPreview : Node3D
{
	[Export] public bool QuitWhenDone = true;

	private string _out;
	private int _fails;

	/// <summary>Before the level under this node is ready: keep the player's album out of it.</summary>
	public override void _EnterTree() => PhotoLog.FolderOverride = "user://preview_photos/";

	public override void _Ready()
	{
		_out = ProjectSettings.GlobalizePath("res://test-output/props");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		Run();
	}

	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s, true), SceneTreeTimer.SignalName.Timeout);
	private void Check(bool ok, string what) { GD.Print($"[props] {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) _fails++; }

	private void Shot(string name)
	{
		GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
		GD.Print("[props] shot " + name);
	}

	private async void Run()
	{
		await Seconds(0.5);
		var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		var fader = FindChild("ScreenFader", true, false) as ScreenFader;
		var opening = GetTree().GetFirstNodeInGroup("opening") as OpeningAtCar;
		Check(player != null && fader != null && opening != null, "level, player, fader and the opening are there");
		if (player == null || fader == null || opening == null) { GetTree().Quit(2); return; }
		var inv = player.Inventory;
		Check(!inv.HasCamera, "a new game starts without the camera in hand");

		await Seconds(2.6);
		Shot("opening_00_card");
		for (int i = 0; i < 80 && fader.BlackAlpha > 0.02f; i++) await Seconds(0.25);
		await Seconds(0.2);
		var stand = opening.GetNode<Node3D>("Stand");
		Check(player.GlobalPosition.DistanceTo(stand.GlobalPosition) < 0.6f, $"the player stands at the open hatch ({player.GlobalPosition.DistanceTo(stand.GlobalPosition):0.00} m)");
		GD.Print($"[props] note: pitch at the fade-in {Mathf.RadToDeg(player.CameraRig.Pitch):0.0} deg (OpeningAtCar asks {opening.LookIntoTrunkPitch:0.0})");
		Shot("opening_01_trunk");
		for (int i = 0; i < 40 && !inv.HasCamera; i++) await Seconds(0.25);
		Check(inv.HasCamera, "the camera comes out of the trunk into the player's hands");
		Check(!opening.GetNode<Node3D>("TrunkCamera").Visible, "the trunk is empty afterwards");
		await Seconds(0.5);
		Shot("opening_01b_lifted");
		await Seconds(2.3);
		Shot("opening_02_after_turn");
		await Seconds(1.6);
		Shot("opening_03_howto");
		Check(StoryManager.Instance.Current == Checkpoint.Act1Start, $"checkpoint 1 reached after the opening ({StoryManager.Instance.Current})");

		// The car from the trail, looking back at the lot.
		var terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		var car = opening.GetParent<Node3D>();
		foreach (var (name, s) in new[] { ("car_from_trail", 9f), ("car_from_trail_far", 20f) })
		{
			Vector3 at = terrain.TrailPoint(s, out Vector3 tan);
			at += tan.Cross(Vector3.Up).Normalized() * 1.2f;
			at.Y = terrain.HeightAt(at.X, at.Z) + 0.1f;
			Vector3 d = car.GlobalPosition + Vector3.Up * 0.8f - at;
			player.Teleport(at, Mathf.Atan2(-d.X, -d.Z));
			await Seconds(0.3);
			var eye = player.CameraRig.Camera.GlobalPosition;
			d = car.GlobalPosition + Vector3.Up * 0.8f - eye;
			player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
			player.CameraRig.SetPitch(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
			await Seconds(0.6);
			Shot(name);
		}
		// Round the side of the car, and into the hatch from close.
		Vector3 side = car.GlobalPosition + car.GlobalBasis.X * 4.5f + car.GlobalBasis.Z * 1.5f;
		side.Y = terrain.HeightAt(side.X, side.Z) + 0.1f;
		Vector3 dd = car.GlobalPosition + Vector3.Up * 0.7f - side;
		player.Teleport(side, Mathf.Atan2(-dd.X, -dd.Z));
		await Seconds(0.3);
		player.CameraRig.SnapBehind(Mathf.Atan2(-dd.X, -dd.Z));
		player.CameraRig.SetPitch(-0.12f);
		await Seconds(0.5);
		Shot("car_side");

		GD.Print(_fails == 0 ? "[props] ALL CHECKS PASSED" : $"[props] {_fails} CHECK(S) FAILED");
		if (QuitWhenDone) GetTree().Quit(_fails == 0 ? 0 : 1);
	}
}
