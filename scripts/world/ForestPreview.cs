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
			// the fallen tree at the trail's end, and the way round it past the root plate
			float tl = _terrain.TrailLength;
			views.Add(("09a_fallen_tree_approach", Eye(TP(tl - 22f)), TP(tl) + new Vector3(0, 0.9f, 0)));
			views.Add(("09b_fallen_tree_close", Eye(TP(tl - 6f)), TP(tl) + new Vector3(3f, 0.8f, -2f)));
			Vector3 G(float x, float z) => new(x, _terrain.HeightAt(x, z), z);
			views.Add(("09c_fallen_tree_side", Eye(G(16f, -458f)), G(0f, -469f) + new Vector3(0, 0.6f, 0)));
			views.Add(("09d_round_the_roots", Eye(G(12f, -463f)), G(15f, -477f) + new Vector3(0, 1.2f, 0)));
			views.Add(("09e_crown_from_beyond", Eye(G(-4f, -482f)), G(-6f, -470f) + new Vector3(0, 0.8f, 0)));
			// branches: the overlook fork, the overlook itself, the split and each faded end
			var branches = new List<(string name, Path3D path)>();
			foreach (var n in GetTree().GetNodesInGroup("trail_branch"))
				if (n is Path3D bp && bp.Curve != null && bp.Curve.PointCount > 1) branches.Add((bp.Name, bp));
			Vector3 BP(Path3D p, int i) => p.GlobalTransform * p.Curve.GetPointPosition(i < 0 ? p.Curve.PointCount + i : i);
			foreach (var (name, bp) in branches)
			{
				Log($"branch {name}: start {BP(bp, 0)} end {BP(bp, -1)} length {bp.Curve.GetBakedLength():0} m");
				if (name == "Overlook")
				{
					views.Add(("16_fork_overlook", Eye(BP(bp, 0)) + new Vector3(-2f, 0, 9f), Eye(BP(bp, 3))));
					Vector3 o = BP(bp, -1);
					var prof = new System.Text.StringBuilder("overlook view profile (ground - eye, every 10 m NW):");
					for (int d = 0; d <= 120; d += 10) { var q = o + new Vector3(-0.6f, 0, -0.8f) * d; prof.Append($" {_terrain.HeightAt(q.X, q.Z) - o.Y - 1.6f:0}"); }
					Log(prof.ToString());
					var ray = PhysicsRayQueryParameters3D.Create(Eye(o), Eye(o) + new Vector3(-0.6f, 0.02f, -0.8f) * 150f, 1);
					var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
					Log($"overlook eye {Eye(o)}; level view ray hits {(hit.Count > 0 ? ((Vector3)hit["position"]).ToString() + " " + ((hit["collider"].AsGodotObject() as Node)?.Name ?? "tree/rock") : "nothing (open view)")}");
					views.Add(("17_overlook_view", Eye(o), Eye(o) + new Vector3(-0.6f, 0.02f, -0.8f) * 20f));
					views.Add(("17b_overlook_north", Eye(o), Eye(o) + new Vector3(-0.15f, 0.02f, -1f) * 20f));
				}
			}
			views.Add(("18_split", Eye(TP(_terrain.TrailLength - 25f)), Eye(TP(_terrain.TrailLength))));
			if (stairs != null)
			{
				Vector3 sp = stairs.GlobalPosition;
				foreach (var (name, bp) in branches)
				{
					if (name == "Overlook") continue;
					Vector3 end = BP(bp, -1), prev = BP(bp, -3);
					views.Add(($"19_{name}_end_ahead", Eye(end), Eye(end) + (end - prev).Normalized() * 10f));
					views.Add(($"19_{name}_end_to_stairs", Eye(end), sp + new Vector3(0, 2f, -5f)));
				}
				Vector3 Near(Vector3 off) { var q = sp + off; q.Y = _terrain.HeightAt(q.X, q.Z); return q; }
				views.Add(("09_clearing_entry", Eye(Near(new Vector3(3f, 0, 16f))), sp + new Vector3(0, 1.4f, -2f)));
				views.Add(("10_stairs_front", Eye(Near(new Vector3(0.8f, 0, 5.5f))), sp + new Vector3(0, 1.3f, -2f)));
				views.Add(("11_stairs_side", Eye(Near(new Vector3(6.5f, 0, -4.5f))), sp + new Vector3(0, 2.2f, -5.5f)));
				views.Add(("12_stairs_close", Eye(Near(new Vector3(-1.4f, 0, 1.6f))) + new Vector3(0, -0.3f, 0), sp + new Vector3(0, 0.5f, -1.2f)));
				{
					var shp = new SphereShape3D { Radius = 0.7f };
					var qp = new PhysicsShapeQueryParameters3D { Shape = shp, CollisionMask = 1 };
					for (int zi = 0; zi < 12; zi++)
					{
						qp.Transform = new Transform3D(Basis.Identity, stairs.GlobalTransform * new Vector3(0, 1.2f + zi * 0.55f, 1.0f - zi));
						foreach (var r in GetWorld3D().DirectSpaceState.IntersectShape(qp, 8))
						{
							var nd = r["collider"].AsGodotObject() as Node;
							if (nd == null || !stairs.IsAncestorOf(nd)) Log($"  stair-path blocker near z{zi}: {nd?.GetPath().ToString() ?? "scatter (tree/rock/log)"}");
						}
					}
				}
				Vector3 top = stairsTrig.GlobalPosition;
				views.Add(("13_stairs_landing", top + new Vector3(0, 1.6f, 1.2f), top + new Vector3(0, 1.0f, -4f)));
				views.Add(("14_stairs_far", Eye(Near(new Vector3(-9f, 0, 16f))), sp + new Vector3(0, 1.5f, -2f)));
				LineOfSightReport(branches.FindAll(b => b.name != "Overlook").ConvertAll(b => (b.name.ToString(), BP(b.path, -1))), stairs);
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

	/// <summary>
	/// From each faded path end, cast rays at eye height to points up the flight and
	/// report how many are unobstructed by terrain or trunks (fog is on top of that).
	/// </summary>
	private void LineOfSightReport(List<(string name, Vector3 end)> ends, Node3D stairs)
	{
		var space = GetWorld3D().DirectSpaceState;
		Vector3 sp = stairs.GlobalPosition;
		foreach (var (name, end) in ends)
		{
			Vector3 eye = end + new Vector3(0, 1.6f, 0);
			int clear = 0, total = 0;
			for (int i = 0; i <= 6; i++)
				for (int side = -1; side <= 1; side++)
				{
					Vector3 target = stairs.GlobalTransform * new Vector3(side * 0.9f, 0.4f + i * 0.95f, -i * 1.8f);
					var q = PhysicsRayQueryParameters3D.Create(eye, target, 1);
					total++;
					var hit = space.IntersectRay(q);
					if (hit.Count == 0 || (hit["collider"].AsGodotObject() is Node hn && stairs.IsAncestorOf(hn))) clear++;
					else if (i == 3 && side == 0) Log($"  mid ray blocked by {(hit["collider"].AsGodotObject() as Node)?.Name} at {(Vector3)hit["position"]}");
				}
			float dist = new Vector2(end.X - sp.X, end.Z - sp.Z).Length();
			float fog = 1f - Mathf.Exp(-0.03f * dist);
			Log($"line of sight {name} end {end} -> stairs: {dist:0} m, {clear}/{total} rays unobstructed, deep-fog cover ~{fog:P0}");
		}
	}

	private async Task Walk(Node3D spawn, Node3D approach, Area3D trigger, VanishingProp vanish)
	{
		var path = (GetTree().GetFirstNodeInGroup("autotest_route") ?? GetTree().GetFirstNodeInGroup("trail")) as Path3D;
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
				{ var to = trigger != null ? new Vector3(trigger.GlobalPosition.X - pos.X, 0, trigger.GlobalPosition.Z - pos.Z) : Vector3.Zero; dir = to.Length() > 0.3f ? to.Normalized() : stairsDir; } speed = 1.9f; climbT += dt;
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
