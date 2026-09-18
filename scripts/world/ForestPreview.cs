using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for forest_world_preview.tscn: flies a camera to a set of
/// viewpoints and saves screenshots to test-output/, then runs physics checks
/// (a capsule walks the whole trail and climbs the stairs) and quits.
/// Not used by the game.
/// </summary>
public partial class ForestPreview : Node3D
{
	[Export] public NodePath CameraPath = "Camera3D";
	[Export] public bool TakeShots = true;
	[Export] public bool WalkTest = true;
	[Export] public bool QuitWhenDone = true;
	[Export] public float WalkTimeScale = 4f;

	private Camera3D _cam;
	private ForestTerrain _terrain;
	private string _out;
	private int _fails;

	public override void _Ready()
	{
		_cam = GetNode<Camera3D>(CameraPath);
		_out = ProjectSettings.GlobalizePath("res://test-output/forest");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		Run();
	}

	private async Task Frames(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task PhysFrame() => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

	private void Log(string s) => GD.Print("[preview] " + s);
	private void Check(bool ok, string what) { Log((ok ? "PASS " : "FAIL ") + what); if (!ok) _fails++; }

	private async void Run()
	{
		await Frames(5);
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (_terrain == null) { GD.PushError("no terrain"); GetTree().Quit(2); return; }
		var scatter = GetTree().Root.FindChild("Forest", true, false) as ForestScatter;
		Log($"trail length {_terrain.TrailLength:0.0} m, trees {scatter?.TreeCount}");

		var spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
		var stairsTrig = GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Area3D;
		var stairs = stairsTrig?.GetParent() as Node3D;
		var approach = stairs?.GetNodeOrNull<Node3D>("AutotestApproach");
		Log($"spawn {spawn?.GlobalPosition} fwd {(spawn != null ? -spawn.GlobalBasis.Z : Vector3.Zero)}");
		Log($"stairs {stairs?.GlobalPosition}  approach {approach?.GlobalPosition}  trigger {stairsTrig?.GlobalPosition}");
		if (_terrain.TryGetStreamCrossing(out var cross, out _, out float crossS)) Log($"bridge/crossing {cross} at s={crossS:0.0}");
		var obstacle = GetTree().GetFirstNodeInGroup("autotest_obstacle") as Node3D;
		if (obstacle != null && spawn != null) Log($"obstacle {obstacle.GlobalPosition} dist {obstacle.GlobalPosition.DistanceTo(spawn.GlobalPosition):0.0}");
		foreach (var n in GetTree().GetNodesInGroup("stream_audio_points")) Log($"stream point {((Node3D)n).Name} {((Node3D)n).GlobalPosition}");
		var vanish = GetTree().Root.FindChild("Vanishing", true, false) as VanishingProp;
		if (vanish != null) Log($"vanishing prop {vanish.GlobalPosition}");
		foreach (var n in GetTree().GetNodesInGroup("trail"))
			if (n is Path3D p) Log($"trail path points {p.Curve.PointCount}, first {p.GlobalTransform * p.Curve.GetPointPosition(0)} last {p.GlobalTransform * p.Curve.GetPointPosition(p.Curve.PointCount - 1)}");

		if (TakeShots)
		{
			await Frames(20);
			var views = new List<(string name, Vector3 from, Vector3 to)>();
			Vector3 Eye(Vector3 p) => p + new Vector3(0, 1.6f, 0);
			Vector3 TP(float s) => _terrain.TrailPoint(s, out _);

			if (spawn != null)
			{
				views.Add(("01_spawn", Eye(spawn.GlobalPosition), Eye(spawn.GlobalPosition) - spawn.GlobalBasis.Z * 10f));
				Vector3 lot = spawn.GlobalPosition + new Vector3(3, 0, -6);
				views.Add(("02_trailhead_back", Eye(lot), spawn.GlobalPosition + new Vector3(-2, 1.0f, 8)));
			}
			foreach (float s in new[] { 40f, 88f, 125f })
				views.Add(($"03_trail_{s:000}", Eye(TP(s)), Eye(TP(s + 12f))));
			if (vanish != null)
				views.Add(("04_fragment_look", Eye(TP(80f)), vanish.GlobalPosition + new Vector3(0, 0.8f, 0)));
			if (vanish != null)
			{
				Vector3 fp = vanish.GlobalPosition;
				Vector3 nearV = fp + new Vector3(8, 0, 10); nearV.Y = _terrain.HeightAt(nearV.X, nearV.Z);
				views.Add(("04b_fragment_near", Eye(nearV), fp + new Vector3(0, 0.6f, 0)));
				Vector3 v0 = TP(80f);
				var sb = new System.Text.StringBuilder("sightline heights:");
				for (int i = 0; i <= 10; i++) { var q = v0.Lerp(fp, i / 10f); sb.Append($" {_terrain.HeightAt(q.X, q.Z) - Mathf.Lerp(v0.Y + 1.6f, fp.Y + 0.6f, i / 10f):0.0}"); }
				Log(sb.ToString());
			}
			views.Add(("05_bridge", Eye(TP(crossS - 11f)), Eye(TP(crossS + 2f)) + new Vector3(0, -1.2f, 0)));
			Vector3 side = cross + new Vector3(7, 0, 4); side.Y = _terrain.HeightAt(side.X, side.Z);
			views.Add(("06_bridge_side", Eye(side), cross + new Vector3(0, 0.3f, 0)));
			views.Add(("07_deep_woods", Eye(TP(215f)), Eye(TP(228f))));
			views.Add(("08_cut_log", Eye(TP(224f)), TP(232f) + new Vector3(0, 0.4f, 0)));
			if (stairs != null)
			{
				views.Add(("09_clearing_entry", Eye(TP(_terrain.TrailLength - 14f)), stairs.GlobalPosition + new Vector3(0, 1.4f, -2f)));
				Vector3 sp = stairs.GlobalPosition;
				Vector3 Near(Vector3 off) { var q = sp + off; q.Y = _terrain.HeightAt(q.X, q.Z); return q; }
				views.Add(("10_stairs_front", Eye(Near(new Vector3(0.8f, 0, 5.5f))), sp + new Vector3(0, 1.3f, -2f)));
				views.Add(("11_stairs_side", Eye(Near(new Vector3(5.5f, 0, -1.5f))), sp + new Vector3(0, 1.2f, -2.2f)));
				views.Add(("12_stairs_close", Eye(Near(new Vector3(-1.4f, 0, 1.6f))) + new Vector3(0, -0.3f, 0), sp + new Vector3(0, 0.5f, -1.2f)));
				views.Add(("13_stairs_landing", sp + new Vector3(0, 2.66f + 1.6f, -3.4f), sp + new Vector3(0, 2.66f, -4.5f)));
				views.Add(("14_stairs_far", Eye(Near(new Vector3(-9f, 0, 16f))), sp + new Vector3(0, 1.5f, -2f)));
			}
			views.Add(("15_overview", new Vector3(40, 45, 30), new Vector3(0, 0, -60)));

			var vpRid = GetViewport().GetViewportRid();
			RenderingServer.ViewportSetMeasureRenderTime(vpRid, true);
			foreach (var v in views)
			{
				_cam.GlobalPosition = v.from;
				_cam.LookAt(v.to, Vector3.Up);
				await Frames(6);
				var img = GetViewport().GetTexture().GetImage();
				img.SavePng($"{_out}/{v.name}.png");
				await Frames(20);
				Log($"shot {v.name}  gpu {RenderingServer.ViewportGetMeasuredRenderTimeGpu(vpRid):0.00} ms  cpu {RenderingServer.ViewportGetMeasuredRenderTimeCpu(vpRid) + RenderingServer.GetFrameSetupTimeCpu():0.00} ms  draws {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}  prims {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)}");
			}
		}

		if (WalkTest) await Walk(spawn, approach, stairsTrig, vanish);

		Log(_fails == 0 ? "ALL CHECKS PASSED" : $"{_fails} CHECK(S) FAILED");
		if (QuitWhenDone) GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	private async Task Walk(Node3D spawn, Node3D approach, Area3D trigger, VanishingProp vanish)
	{
		var path = GetTree().GetFirstNodeInGroup("trail") as Path3D;
		if (path == null || spawn == null) { Check(false, "trail path + spawn exist"); return; }

		var body = new CharacterBody3D { Name = "TestWalker", CollisionLayer = 2, CollisionMask = 1 };
		body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.75f }, Position = new Vector3(0, 0.875f, 0) });
		body.FloorSnapLength = 0.45f;
		body.FloorMaxAngle = Mathf.DegToRad(48f);
		body.AddToGroup("player");
		GetTree().CurrentScene.AddChild(body);
		body.GlobalPosition = spawn.GlobalPosition + new Vector3(0, 0.1f, 0);

		bool reachedTop = false;
		if (trigger != null) trigger.BodyEntered += b => { if (b == body) reachedTop = true; };
		float walked = 0; bool vanished = false; float vanishedAt = -1;
		if (vanish != null) vanish.Vanished += () => { vanished = true; vanishedAt = walked; };

		var targets = new List<Vector3>();
		for (int i = 0; i < path.Curve.PointCount; i++) targets.Add(path.GlobalTransform * path.Curve.GetPointPosition(i));
		if (approach != null) targets.Add(approach.GlobalPosition);
		Vector3 stairsDir = approach != null ? -approach.GetParent<Node3D>().GlobalBasis.Z : Vector3.Forward;

		Engine.TimeScale = WalkTimeScale;
		float gravity = 9.8f * 1.6f;
		int idx = 0; float stall = 0; Vector3 lastCheck = body.GlobalPosition; float t = 0;
		bool climbing = false; float climbT = 0; float maxY = -999;
		Vector3 look = -spawn.GlobalBasis.Z;
		var shotAt = new HashSet<int> { 55, 65, 75, 85, 120 };
		bool seenLogged = false;
		while (true)
		{
			await PhysFrame();
			float dt = (float)GetPhysicsProcessDeltaTime();
			t += dt;
			Vector3 pos = body.GlobalPosition;
			Vector3 dir;
			float speed;
			if (!climbing)
			{
				Vector3 tgt = targets[idx];
				Vector3 flat = new(tgt.X - pos.X, 0, tgt.Z - pos.Z);
				if (flat.Length() < 1.2f)
				{
					idx++;
					if (idx >= targets.Count) { climbing = true; continue; }
					continue;
				}
				dir = flat.Normalized();
				speed = idx < targets.Count - 1 ? 4.6f : 1.9f;
			}
			else
			{
				dir = stairsDir; speed = 1.9f; climbT += dt;
				maxY = Mathf.Max(maxY, pos.Y);
				if (reachedTop || climbT > 8f) break;
			}
			var v = body.Velocity;
			v.X = dir.X * speed; v.Z = dir.Z * speed;
			v.Y = body.IsOnFloor() ? Mathf.Min(v.Y, 0) : v.Y - gravity * dt;
			body.Velocity = v;
			body.MoveAndSlide();
			walked += new Vector2(body.GlobalPosition.X - pos.X, body.GlobalPosition.Z - pos.Z).Length();

			// the "player camera" for vanishing checks: eye height, looking where we walk
			look = look.Lerp(dir, 0.08f).Normalized();
			_cam.GlobalPosition = body.GlobalPosition + new Vector3(0, 1.6f, 0) - look * 2.2f + new Vector3(0, 0.3f, 0);
			_cam.LookAt(body.GlobalPosition + new Vector3(0, 1.4f, 0) + look * 6f, Vector3.Up);

			if (vanish != null && vanish.HasBeenSeen && !seenLogged)
			{
				seenLogged = true;
				Log($"fragment first seen at {walked:0} m walked, camera {_cam.GlobalPosition}");
				Engine.TimeScale = 1;
				await Frames(2);
				GetViewport().GetTexture().GetImage().SavePng($"{_out}/walk_seen.png");
				Engine.TimeScale = WalkTimeScale;
			}
			int wm = (int)walked;
			if (shotAt.Remove(wm))
			{
				Engine.TimeScale = 1;
				await Frames(3);
				GetViewport().GetTexture().GetImage().SavePng($"{_out}/walk_{wm:000}.png");
				Engine.TimeScale = WalkTimeScale;
			}

			if (t > 3f)
			{
				float moved = body.GlobalPosition.DistanceTo(lastCheck);
				if (moved < 1.5f && !climbing) { stall += 1; Log($"walker slow near {body.GlobalPosition} (target {idx}/{targets.Count})"); }
				if (stall >= 2) break;
				lastCheck = body.GlobalPosition; t = 0;
			}
		}
		Engine.TimeScale = 1;
		Check(idx >= targets.Count, $"walker followed trail to the stairs approach (reached {idx}/{targets.Count}, walked {walked:0} m, at {body.GlobalPosition})");
		Check(reachedTop, $"walker climbed the stairs into stairs_top_trigger (max y {maxY:0.00})");
		if (vanish != null) Check(vanished, $"fragment vanished during the walk (at {vanishedAt:0} m walked)");
		body.QueueFree();

		// obstacle check: walk straight at it from spawn
		var obstacle = GetTree().GetFirstNodeInGroup("autotest_obstacle") as Node3D;
		if (obstacle != null)
		{
			var b2 = new CharacterBody3D { CollisionLayer = 2, CollisionMask = 1 };
			b2.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.75f }, Position = new Vector3(0, 0.875f, 0) });
			b2.FloorSnapLength = 0.45f; b2.FloorMaxAngle = Mathf.DegToRad(48f);
			GetTree().CurrentScene.AddChild(b2);
			b2.GlobalPosition = spawn.GlobalPosition + new Vector3(0, 0.1f, 0);
			Vector3 target = obstacle.GlobalPosition;
			for (int i = 0; i < 60 * 8; i++)
			{
				await PhysFrame();
				Vector3 d = new(target.X - b2.GlobalPosition.X, 0, target.Z - b2.GlobalPosition.Z);
				var v = d.Normalized() * 1.9f;
				v.Y = b2.IsOnFloor() ? 0 : b2.Velocity.Y - 15f / 60f;
				b2.Velocity = v;
				b2.MoveAndSlide();
			}
			float dist = new Vector2(target.X - b2.GlobalPosition.X, target.Z - b2.GlobalPosition.Z).Length();
			Check(dist > 0.6f && dist < 2f, $"obstacle blocks the player (stopped {dist:0.00} m from its centre)");
			b2.QueueFree();
		}
	}
}
