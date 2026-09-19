using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for props_preview.tscn: first-person (1.62 m eye, FOV 70)
/// screenshots of the trail signs and the footbridge into test-output/props/,
/// then a capsule walks across the bridge. Not used by the game.
/// </summary>
public partial class PropsPreview : Node3D
{
	[Export] public NodePath CameraPath = "Camera3D";
	[Export] public bool QuitWhenDone = true;

	private const float Eye = 1.62f;
	private Camera3D _cam;
	private ForestTerrain _terrain;
	private string _out;
	private int _fails;

	public override void _Ready()
	{
		_cam = GetNode<Camera3D>(CameraPath);
		_cam.MakeCurrent();
		_out = ProjectSettings.GlobalizePath("res://test-output/props");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		Run();
	}

	private async Task Frames(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private void Log(string s) => GD.Print("[props] " + s);

	private Vector3 Ground(Vector3 p) { p.Y = _terrain.HeightAt(p.X, p.Z); return p; }
	private Vector3 EyeAt(Vector3 p) => Ground(p) + new Vector3(0, Eye, 0);
	/// <summary>Stand dist metres in front of a prop (its +Z), offset sideways, looking at a point on it.</summary>
	private (Vector3, Vector3) Front(Node3D n, float dist, float side, float lookY)
	{
		Vector3 f = n.GlobalBasis.Z; f.Y = 0; f = f.Normalized();
		Vector3 r = n.GlobalBasis.X; r.Y = 0; r = r.Normalized();
		return (EyeAt(n.GlobalPosition + f * dist + r * side), n.GlobalPosition + new Vector3(0, lookY, 0));
	}

	private async void Run()
	{
		await Frames(8);
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (_terrain == null) { GD.PushError("no terrain"); GetTree().Quit(2); return; }
		var root = GetTree().Root;
		var spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
		var sign = root.FindChild("TrailheadSign", true, false) as Node3D;
		var info = root.FindChild("InfoBoard", true, false) as Node3D;
		var dirSign = root.FindChild("DirectionSign", true, false) as Node3D;
		var bridge = root.FindChild("Footbridge", true, false) as Node3D;
		var bridgeSign = root.FindChild("BridgeSign", true, false) as Node3D;
		var m1 = root.FindChild("Marker_00_1", true, false) as Node3D;
		var gag = root.FindChild("Marker_05_4", true, false) as Node3D;
		var tornMap = root.FindChild("TornMap", true, false) as Node3D;
		var tent = root.FindChild("ForgottenTent", true, false) as Node3D;
		var cabin = root.FindChild("Cabin", true, false) as Node3D;
		var shed = root.FindChild("Shed", true, false) as Node3D;
		_terrain.TryGetStreamCrossing(out var cross, out _, out float crossS);
		Log($"sign {sign?.GlobalPosition} dir {dirSign?.GlobalPosition} bridge {bridge?.GlobalPosition} bridgeSign {bridgeSign?.GlobalPosition} crossS {crossS:0.0}");
		Vector3 TP(float s) => _terrain.TrailPoint(s, out _);

		await Frames(20);
		var views = new List<(string, Vector3, Vector3)>();
		if (spawn != null) views.Add(("01_spawn", spawn.GlobalPosition + new Vector3(0, Eye, 0), spawn.GlobalPosition + new Vector3(0, Eye - 0.3f, 0) - spawn.GlobalBasis.Z * 10f));
		if (sign != null) { var (a, b) = Front(sign, 4.2f, 0.3f, 1.6f); views.Add(("02_trailhead_sign", a, b)); (a, b) = Front(sign, 2.2f, -0.2f, 1.7f); views.Add(("02b_trailhead_close", a, b)); }
		if (dirSign != null)
		{
			var (a, b) = Front(dirSign, 3.2f, 0.4f, 1.55f); views.Add(("03_direction_sign", a, b));
			views.Add(("04_direction_approach", EyeAt(TP(0f)), dirSign.GlobalPosition + new Vector3(1.5f, 1.2f, 0)));
		}
		if (info != null) { var (a, b) = Front(info, 3.0f, -0.2f, 1.6f); views.Add(("05_info_board", a, b)); }
		if (cabin != null)
		{
			Log($"cabin {cabin.GlobalPosition}");
			var (a, b) = Front(cabin, 8f, 0f, 1.2f); views.Add(("05b_cabin_front", a, b));
			views.Add(("05c_cabin_overview", cabin.GlobalPosition + new Vector3(6, 4, 10), cabin.GlobalPosition + new Vector3(0, 1.5f, 0)));
		}
		if (shed != null) { Log($"shed {shed.GlobalPosition}"); var (a, b) = Front(shed, 4f, 0f, 1.0f); views.Add(("05d_shed", a, b)); }
		if (m1 != null) { var (a, b) = Front(m1, 2.2f, 0.2f, 0.8f); views.Add(("06_marker_1", a, b)); }
		if (gag != null) { var (a, b) = Front(gag, 2.2f, 0.2f, 0.8f); views.Add(("07_marker_gag_4", a, b)); }
		if (tornMap != null) { var (a, b) = Front(tornMap, 1.4f, 0f, 0.05f); views.Add(("07b_torn_map", a, b)); }
		if (tent != null)
		{
			var (a, b) = Front(tent, 3.2f, 0.5f, 0.4f); views.Add(("07c_tent_front", a, b));
			Vector3 r = tent.GlobalBasis.X;
			views.Add(("07d_tent_side", EyeAt(tent.GlobalPosition + r * 2.6f), tent.GlobalPosition + new Vector3(0, 0.35f, 0)));
		}
		views.Add(("08_bridge_approach", EyeAt(TP(crossS - 12f)), TP(crossS) + new Vector3(0, 0.6f, 0)));
		if (bridgeSign != null) { var (a, b) = Front(bridgeSign, 2.6f, 0.6f, 0.6f); views.Add(("09_bridge_sign", a, b)); }
		views.Add(("10_bridge_near", EyeAt(TP(crossS - 6.5f)), TP(crossS + 2f) + new Vector3(0, 0.3f, 0)));
		if (bridge != null)
		{
			Vector3 r = bridge.GlobalBasis.X;
			views.Add(("11_bridge_side", EyeAt(cross + r * 7f + bridge.GlobalBasis.Z * 3f), cross + new Vector3(0, 0.2f, 0)));
			views.Add(("12_bridge_other_side", EyeAt(cross - r * 6f - bridge.GlobalBasis.Z * 2f), cross + new Vector3(0, 0.2f, 0)));
			views.Add(("13_on_deck", bridge.GlobalPosition + bridge.GlobalBasis.Z * 3.5f + new Vector3(0, Eye + 0.05f, 0), bridge.GlobalPosition - bridge.GlobalBasis.Z * 6f + new Vector3(0, 1f, 0)));
			views.Add(("14_look_down_stream", bridge.GlobalPosition + new Vector3(0, Eye + 0.05f, 0), bridge.GlobalPosition + r * 5f + new Vector3(0, -1.5f, 0)));
		}
		foreach (var (name, from, to) in views)
		{
			_cam.GlobalPosition = from;
			_cam.LookAt(to, Vector3.Up);
			await Frames(8);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
			Log("shot " + name);
		}

		await WalkBridge(crossS);
		Log(_fails == 0 ? "ALL CHECKS PASSED" : $"{_fails} CHECK(S) FAILED");
		if (QuitWhenDone) GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	/// <summary>A player-sized capsule walks the trail across the bridge and back.</summary>
	private async Task WalkBridge(float crossS)
	{
		foreach (float dirSign in new[] { 1f, -1f })
		{
			var body = new CharacterBody3D { CollisionLayer = 2, CollisionMask = 1 };
			body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.75f }, Position = new Vector3(0, 0.875f, 0) });
			body.FloorSnapLength = 0.45f;
			body.FloorMaxAngle = Mathf.DegToRad(48f);
			GetTree().CurrentScene.AddChild(body);
			float s0 = crossS - dirSign * 10f, s1 = crossS + dirSign * 10f;
			body.GlobalPosition = _terrain.TrailPoint(s0, out _) + new Vector3(0, 0.1f, 0);
			float s = s0; bool woodSeen = false; int frames = 0;
			while (frames++ < 60 * 20)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
				Vector3 pos = body.GlobalPosition;
				_terrain.TrailDistance(pos.X, pos.Z, out float along);
				s = along;
				if (dirSign > 0 ? along >= s1 - 0.5f : along <= s1 + 0.5f) break;
				Vector3 tgt = _terrain.TrailPoint(along + dirSign * 2f, out _);
				Vector3 d = new Vector3(tgt.X - pos.X, 0, tgt.Z - pos.Z).Normalized();
				var v = d * 1.9f;
				v.Y = body.IsOnFloor() ? 0 : body.Velocity.Y - 15f / 60f;
				body.Velocity = v;
				body.MoveAndSlide();
				for (int i = 0; i < body.GetSlideCollisionCount(); i++)
					if (body.GetSlideCollision(i).GetCollider() is Node c && c.HasMeta("surface") && (string)c.GetMeta("surface") == "wood" && body.IsOnFloor())
						woodSeen = true;
				s = along;
			}
			bool ok = dirSign > 0 ? s >= s1 - 0.5f : s <= s1 + 0.5f;
			Log($"{(ok && woodSeen ? "PASS" : "FAIL")} walk across bridge dir {dirSign}: reached s={s:0.0} (target {s1:0.0}) wood {woodSeen}");
			if (!(ok && woodSeen)) _fails++;
			body.QueueFree();
		}
	}
}
