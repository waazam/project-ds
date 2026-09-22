using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for stairs_preview.tscn: frames the stone staircase (and the
/// fragment) from first-person eye height (1.62 m, FOV 70), saves screenshots
/// to test-output/stairs/, then walks a player-sized capsule up the flight and
/// tries to walk it off the side. Not used by the game.
///
/// The newel post (test-output/newel/): the trailhead's broken pier from the
/// foot, the landing, close up and in the first climb's look-down, plus a capped
/// comparison and a check that nothing of it stands in the landing's clear space.
/// Then (unless run with <c>-- --trail-only</c>) it tours the Hollow through real
/// Continues, like HollowPreviewDriver (save slots backed up and put back): the
/// cap on R.H.'s table and taking it ("It's warm."), the clearing's staircase
/// before, during and after the fuse, and the last staircase capped.
/// </summary>
public partial class StairsPreview : Node3D
{
	[Export] public NodePath CameraPath = "Camera3D";
	[Export] public float Eye = 1.62f;

	private Camera3D _cam;
	private string _out, _newelOut;
	private int _fails;
	private readonly List<string> _backedUp = new();

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_cam = GetNode<Camera3D>(CameraPath);
		_out = ProjectSettings.GlobalizePath("res://test-output/stairs");
		_newelOut = ProjectSettings.GlobalizePath("res://test-output/newel");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		DirAccess.MakeDirRecursiveAbsolute(_newelOut);
		Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s, true), SceneTreeTimer.SignalName.Timeout);
	private async Task Phys() => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	private async Task WaitUntil(Func<bool> done, double seconds)
	{
		ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (!done() && Time.GetTicksMsec() < end) await Frames(1);
	}
	private void Log(string s) => GD.Print("[stairs] " + s);
	private void Check(bool ok, string what) { Log((ok ? "PASS " : "FAIL ") + what); if (!ok) _fails++; }
	private void Newel(string name) { GetViewport().GetTexture().GetImage().SavePng($"{_newelOut}/{name}.png"); Log($"shot newel/{name}.png"); }

	private async void Run()
	{
		await Frames(30);
		// keep only the post-process layer of the HUD (the fader starts black without GameFlow)
		foreach (var n in new[] { "ScreenFader", "PauseMenu", "DebugOverlay" })
			if (GetTree().Root.FindChild(n, true, false) is CanvasLayer cl) cl.Visible = false;
		var terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		var trig = GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Area3D;
		var stairs = trig?.GetParent() as StaircaseBuilder;
		if (stairs == null) { GD.PushError("no stairs"); GetTree().Quit(2); return; }
		Log($"stairs at {stairs.GlobalPosition}, {stairs.Steps} steps, height {stairs.TotalHeight:0.00}, trigger {trig.GlobalPosition}");

		Vector3 L(float x, float y, float z) => stairs.GlobalTransform * new Vector3(x, y, z);
		Vector3 G(float x, float z)
		{
			var p = L(x, 0, z);
			if (terrain != null) p.Y = terrain.HeightAt(p.X, p.Z);
			return p + new Vector3(0, Eye, 0);
		}
		float H = stairs.TotalHeight, zb = stairs.BackZ;
		var views = new (string, Vector3, Vector3)[]
		{
			("01_clearing_20m", G(0.9f, 20f), L(0, 2.6f, -6f)),
			("02_approach_10m", G(-0.6f, 10f), L(0, 2.2f, -5f)),
			("03_foot_look_up", G(0.15f, 1.6f), L(0, 5.2f, -9f)),
			("04_on_flight_mid", L(0.1f, 3.0f + Eye, -4.2f), L(0, 5.6f, -11f)),
			("05_top_look_down", L(0f, H + Eye, zb + 0.45f), L(0, 0.2f, 3f)),
			("06_side", G(5.5f, -3f), L(0, 2.0f, -5f)),
			("07_detail_treads", G(0.35f, 0.9f) + new Vector3(0, -0.35f, 0), L(0, 0.6f, -1.4f)),
		};
		foreach (var (name, from, to) in views)
		{
			_cam.GlobalPosition = from;
			_cam.LookAt(to, Vector3.Up);
			await Frames(8);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
		}
		if (GetTree().Root.FindChild("StaircaseFragment", true, false) is StaircaseBuilder frag)
		{
			Vector3 F(float x, float z) { var p = frag.GlobalTransform * new Vector3(x, 0, z); if (terrain != null) p.Y = terrain.HeightAt(p.X, p.Z); return p + new Vector3(0, Eye, 0); }
			_cam.GlobalPosition = F(-3.5f, 5.5f);
			_cam.LookAt(frag.GlobalTransform * new Vector3(0, 0.6f, -1f), Vector3.Up);
			await Frames(8);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/08_fragment.png");
			_cam.GlobalPosition = F(3f, 2.5f);
			_cam.LookAt(frag.GlobalTransform * new Vector3(0, 0.5f, -1f), Vector3.Up);
			await Frames(8);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/09_fragment_near.png");
		}

		await TrailheadNewel(stairs, G, L);
		await Climb(stairs, trig);
		bool trailOnly = OS.GetCmdlineUserArgs().Contains("--trail-only");
		if (!trailOnly) await HollowTour();
		Log(_fails == 0 ? "ALL CHECKS PASSED" : $"{_fails} CHECK(S) FAILED");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	// ------------------------------------------------------------------ newel post: the trailhead

	private async Task Shoot(Camera3D cam, Vector3 from, Vector3 to, string name, int frames = 8)
	{
		cam.GlobalPosition = from;
		cam.LookAt(to, Vector3.Up);
		await Frames(frames);
		Newel(name);
	}

	private async Task TrailheadNewel(StaircaseBuilder stairs, Func<float, float, Vector3> G, Func<float, float, float, Vector3> L)
	{
		float H = stairs.TotalHeight, zb = stairs.BackZ, hw = stairs.Width * 0.5f;
		Check(stairs.HasNewel && !stairs.NewelCapped, $"trailhead: the newel post stands uncapped (capped={stairs.NewelCapped})");
		var cap = stairs.GetNodeOrNull<Node3D>("Generated/Newel/Cap");
		var stump = stairs.GetNodeOrNull<Node3D>("Generated/Newel/Stump");
		Check(cap != null && !cap.Visible && stump != null, "trailhead: cap node hidden, stump shown");
		Vector3 seat = stairs.NewelSeatGlobal.Origin;
		Log($"newel seat at local {stairs.NewelSeatLocal}, {stairs.NewelSeatLocal.Y - H:0.00} m above the landing; cap {StaircaseBuilder.NewelCapHeight:0.000} m tall");

		await Shoot(_cam, G(0.15f, 1.6f), seat + Vector3.Down * 2.5f, "01_trail_foot_look_up");
		await Shoot(_cam, G(-0.8f, 6f), L(0.2f, H * 0.6f, zb * 0.7f), "02_trail_approach_6m");
		await Shoot(_cam, L(-0.25f, H + Eye, zb + 1.1f), seat + Vector3.Down * 0.2f, "03_trail_landing_eye");
		await Shoot(_cam, L(0.1f, H + Eye - 0.1f, zb + 0.75f), seat, "04_trail_landing_close");
		// the first climb's look-down: at the top trigger, facing on up the flight (-Z), pitched down 78 degrees
		{
			Vector3 eye = L(0, H + Eye, (stairs.TopFrontZ + zb) * 0.5f);
			Vector3 fwd = -stairs.GlobalBasis.Z;
			float p = Mathf.DegToRad(-78f);
			await Shoot(_cam, eye, eye + fwd * Mathf.Cos(p) + Vector3.Up * Mathf.Sin(p), "05_trail_first_climb_look_down");
		}
		// what it looked like whole (for comparison; the story never shows the trailhead one capped)
		int builds = stairs.BuildCount;
		stairs.NewelCapped = true;
		Check(cap != null && cap.Visible && stairs.BuildCount == builds, "capping shows the cap node without rebuilding the flight");
		await Shoot(_cam, L(-0.25f, H + Eye, zb + 1.1f), seat + Vector3.Down * 0.2f, "06_compare_capped_landing_eye");
		await Shoot(_cam, G(0.15f, 1.6f), seat + Vector3.Down * 2.5f, "07_compare_capped_foot");
		stairs.NewelCapped = false;

		// nothing of the pier in the landing's clear space (where the player stands and the look-down happens)
		await Phys();
		var space = GetWorld3D().DirectSpaceState;
		float depth = stairs.TopFrontZ - (zb + 0.15f);
		var q = new PhysicsShapeQueryParameters3D
		{
			Shape = new BoxShape3D { Size = new Vector3(stairs.Width - 0.04f, 1.8f, depth) },
			Transform = new Transform3D(stairs.GlobalBasis.Orthonormalized(), L(0, H + 0.95f, zb + 0.15f + depth * 0.5f)),
			CollisionMask = 1u,
		};
		var hits = space.IntersectShape(q, 8);
		Check(hits.Count == 0, $"the landing's clear space is free ({hits.Count} hits{(hits.Count > 0 ? ": " + string.Join(", ", hits.Select(h => (h["collider"].AsGodotObject() as Node)?.Name + "/" + h["shape"])) : "")})");
		// the item
		var host = new Node3D();
		AddChild(host);
		var built = ItemMeshes.Build(ToolKind.NewelPost, host);
		var aabb = host.GetNode<MeshInstance3D>("Mesh").GetAabb();
		Log($"item: {built.Triangles} triangles, size {aabb.Size}, bottom {aabb.Position.Y:0.000}");
		Check(aabb.Size.Y is > 0.25f and < 0.32f && aabb.Position.Y > -0.01f && built.Triangles < 400, "the item: 0.25-0.32 m tall, resting on y=0, PS2 budget");
		host.QueueFree();
	}

	// ------------------------------------------------------------------ the climb

	private CharacterBody3D Capsule(Vector3 at)
	{
		var body = new CharacterBody3D { CollisionLayer = 2, CollisionMask = 1, FloorSnapLength = 0.45f, FloorMaxAngle = Mathf.DegToRad(48f) };
		body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.75f }, Position = new Vector3(0, 0.875f, 0) });
		GetTree().CurrentScene.AddChild(body);
		body.GlobalPosition = at + new Vector3(0, 0.05f, 0);
		return body;
	}

	private async Task Climb(StaircaseBuilder stairs, Area3D trig)
	{
		var approach = stairs.GetNode<Node3D>("AutotestApproach");
		var body = Capsule(approach.GlobalPosition);
		bool reached = false;
		trig.BodyEntered += b => { if (b == body) reached = true; };
		Vector3 fwd = -stairs.GlobalBasis.Z;
		float t = 0, maxY = -999;
		string surface = "";
		while (t < 12f && !reached)
		{
			await Phys();
			float dt = (float)GetPhysicsProcessDeltaTime(); t += dt;
			var v = body.Velocity;
			v.X = fwd.X * 1.9f; v.Z = fwd.Z * 1.9f;
			v.Y = body.IsOnFloor() ? Mathf.Min(v.Y, 0) : v.Y - 15.7f * dt;
			body.Velocity = v;
			body.MoveAndSlide();
			maxY = Mathf.Max(maxY, body.GlobalPosition.Y);
			var q = PhysicsRayQueryParameters3D.Create(body.GlobalPosition + Vector3.Up * 0.3f, body.GlobalPosition + Vector3.Down * 0.7f, 1);
			q.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
			if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node n && n.HasMeta("surface") && t > 2f) surface = n.GetMeta("surface").AsString();
		}
		Check(reached, $"capsule climbed into the top trigger in {t:0.0}s (max y rel {maxY - stairs.GlobalPosition.Y:0.00})");
		Check(surface == "stone", $"surface underfoot on the stairs = '{surface}'");
		// Stand on the top: stay there (the guard should stop overshoot)
		for (int i = 0; i < 90; i++)
		{
			await Phys();
			var v = body.Velocity; v.X = fwd.X * 1.9f; v.Z = fwd.Z * 1.9f; v.Y = body.IsOnFloor() ? 0 : v.Y - 0.26f;
			body.Velocity = v; body.MoveAndSlide();
		}
		var local = stairs.ToLocal(body.GlobalPosition);
		Check(local.Y > stairs.TotalHeight - 0.1f && local.Z > stairs.BackZ, $"walking on at the top keeps you on the landing (local {local})");
		// On the landing, walk toward the newel side and back to the middle: the wall stops you, nothing snags.
		Vector3 right = stairs.GlobalBasis.X;
		for (int i = 0; i < 90; i++)
		{
			await Phys();
			var v = body.Velocity; v.X = right.X * 2.5f; v.Z = right.Z * 2.5f; v.Y = body.IsOnFloor() ? 0 : v.Y - 0.26f;
			body.Velocity = v; body.MoveAndSlide();
		}
		var lr = stairs.ToLocal(body.GlobalPosition);
		Check(lr.X < stairs.Width * 0.5f && lr.Y > stairs.TotalHeight - 0.1f, $"on the landing the newel side holds like the wall (local {lr})");
		body.QueueFree();

		// Side walls: from mid-flight, walk sideways both ways.
		foreach (int s in new[] { -1, 1 })
		{
			var mid = stairs.GlobalTransform * new Vector3(0, 3.75f, -5.6f);
			var b2 = Capsule(mid);
			Vector3 side = stairs.GlobalBasis.X * s;
			for (int i = 0; i < 150; i++)
			{
				await Phys();
				var v = b2.Velocity; v.X = side.X * 4.5f; v.Z = side.Z * 4.5f; v.Y = b2.IsOnFloor() ? 0 : v.Y - 0.26f;
				b2.Velocity = v; b2.MoveAndSlide();
			}
			var l2 = stairs.ToLocal(b2.GlobalPosition);
			Check(Mathf.Abs(l2.X) < stairs.Width * 0.5f && l2.Y > 1.5f, $"cheek wall {(s < 0 ? "left" : "right")} holds (local {l2})");
			b2.QueueFree();
		}
	}

	// ------------------------------------------------------------------ the Hollow (real Continues)

	private static readonly string[] F2 = { StoryManager.Flag.StairsClimbed };
	private static readonly string[] F3Storm = F2.Concat(new[] { StoryManager.Flag.StormStarted, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, SafeZoneWatcher.ThingsLineFlag }).ToArray();
	private static readonly string[] F3Giant = F3Storm.Concat(new[] { StoryManager.Flag.GiantEventDone, StoryManager.Flag.PickupTakenKey }).ToArray();
	private static readonly string[] F5 = F3Giant.Concat(new[] { StoryManager.Flag.CabinDoorOpen, StoryManager.Flag.PickupTakenAxe, CabinReturnEvent.SeenFlag }).ToArray();
	private static readonly string[] F5Post = F5.Concat(new[] { StoryManager.Flag.NewelPostTaken, StoryManager.Flag.DawnBroke }).ToArray();
	private static readonly string[] F6Voice = F5Post.Concat(new[] { StoryManager.Flag.ClearingVoiceHeard }).ToArray();
	private static readonly string[] F7 = F6Voice.Concat(new[] { StoryManager.Flag.Act6NightFell }).ToArray();
	private static readonly string[] F10 = F7.Concat(new[] { StoryManager.Flag.CrtPuzzleDone, StoryManager.Flag.BunkerMazeEntered, StoryManager.Flag.BunkerMazeExited }).ToArray();
	private static readonly string[] F11 = F10.Concat(new[] { StoryManager.Flag.Act11DialogueDone }).ToArray();

	private PlayerController _player;
	private ForestTerrain _terrain;
	private Camera3D _detail;

	private static string UserFile(string name) => ProjectSettings.GlobalizePath("user://" + name);

	private async Task HollowTour()
	{
		// Survive the scene changes: hang off the root from here on.
		var root = GetTree().Root;
		GetParent().RemoveChild(this);
		root.AddChild(this);
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
			if (FileAccess.FileExists(UserFile(slot)) && DirAccess.CopyAbsolute(UserFile(slot), UserFile("newel_preview_backup_" + slot)) == Error.Ok) _backedUp.Add(slot);
		try
		{
			if (await ContinueInto(Checkpoint.Act5CabinEntered, F5, "lantern,compass;tools=Key")) await Cabin();
			if (await ContinueInto(Checkpoint.Act6BridgeCrossed, F5Post, "lantern,compass,newel_post;tools=Key")) await ClearingFuse();
			if (await ContinueInto(Checkpoint.Act10WalkieFound, F11, "lantern,compass,radio;tools=Key")) await LastStairs();
		}
		catch (Exception e) { Check(false, "hollow tour ran without an exception: " + e); }
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
		{
			string dst = UserFile(slot), bak = UserFile("newel_preview_backup_" + slot);
			if (_backedUp.Contains(slot) && FileAccess.FileExists(bak)) { DirAccess.CopyAbsolute(bak, dst); DirAccess.RemoveAbsolute(bak); }
			else if (FileAccess.FileExists(dst)) DirAccess.RemoveAbsolute(dst);
		}
		Log($"save slots put back ({_backedUp.Count})");
	}

	private async Task<bool> ContinueInto(Checkpoint cp, string[] flags, string inventory)
	{
		SaveSystem.Save(new SaveData { Checkpoint = cp, PosX = 0, PosY = 0, PosZ = 40, Yaw = 0f, Flags = flags, Inventory = inventory });
		var before = GetTree().CurrentScene;
		if (!StoryManager.Instance.ContinueGame()) { Check(false, $"{cp}: ContinueGame accepted the save"); return false; }
		for (int i = 0; i < 900 && (GetTree().CurrentScene == before || GetTree().CurrentScene == null); i++) await Frames(1);
		for (int i = 0; i < 600 && GameFlow.Instance is not { Started: true }; i++) await Frames(1);
		await Seconds(2.2);
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (_player == null || _terrain == null) { Check(false, $"{cp}: level loaded with a player and terrain"); return false; }
		_player.PlayerInput.Scripted = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_detail = new Camera3D { Fov = 70f, Near = 0.05f, Far = 400f };
		GetTree().CurrentScene.AddChild(_detail);
		Log($"--- {cp}: loaded {GetTree().CurrentScene.SceneFilePath}");
		return true;
	}

	private static IEnumerable<Node> All(Node n) { yield return n; foreach (var c in n.GetChildren()) foreach (var d in All(c)) yield return d; }
	private IEnumerable<T> AllOf<T>() where T : class => All(GetTree().CurrentScene).OfType<T>();

	private async Task StandAndLook(Vector3 at, Vector3 lookAt, bool snapToGround = true)
	{
		Vector3 g = snapToGround ? new Vector3(at.X, _terrain.HeightAt(at.X, at.Z) + 0.1f, at.Z) : at;
		Vector3 d0 = lookAt - g;
		_player.Teleport(g, Mathf.Atan2(-d0.X, -d0.Z));
		await Frames(4);
		var eye = _player.CameraRig.Camera.GlobalPosition;
		Vector3 d = lookAt - eye;
		_player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
		_player.CameraRig.SetPitch(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
		await Frames(3);
	}

	private async Task Detail(Vector3 from, Vector3 to, string name)
	{
		_detail.Current = true;
		await Shoot(_detail, from, to, name, 6);
		_player.CameraRig.Camera.Current = true;
	}

	private async Task Cabin()
	{
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var post = AllOf<Pickup>().FirstOrDefault(p => p.Kind == ToolKind.NewelPost && IsInstanceValid(p));
		var things = AllOf<FriendBody>().FirstOrDefault();
		Check(cabin != null && post != null && post.Visible && things?.Page != null, "cabin: the cap on the table, his page beside it");
		if (cabin == null || post == null) return;
		Check(post.TakenLine == "It's warm.", $"cabin: the post's taken line ('{post.TakenLine}')");
		// the room from the door side, then close at the table (player eye, as HollowPreviewDriver frames them)
		Vector3 eyeSpot = cabin.GlobalTransform * new Vector3(0.1f, 0f, cabin.Depth * 0.5f - 0.6f);
		await StandAndLook(eyeSpot + Vector3.Up * 0.1f, post.GlobalPosition + Vector3.Up * 0.12f, false);
		await Seconds(0.6);
		Newel("10_cabin_room_eye");
		await StandAndLook(cabin.GlobalTransform * new Vector3(0.9f, 0.1f, -0.2f), post.GlobalPosition + Vector3.Up * 0.12f, false);
		await Seconds(0.4);
		Newel("11_cabin_table_eye");
		// the same, the crosshair just off it (no highlight): its own colour in the cabin light
		await StandAndLook(cabin.GlobalTransform * new Vector3(0.9f, 0.1f, -0.2f), post.GlobalPosition + Vector3.Up * 0.12f + (post.GlobalPosition - _player.GlobalPosition).Cross(Vector3.Up).Normalized() * 0.45f, false);
		await Seconds(0.3);
		Newel("11b_cabin_table_eye_unfocused");
		Vector3 pc = post.GlobalPosition + Vector3.Up * 0.14f;
		Vector3 toward = (_player.CameraRig.Camera.GlobalPosition - pc) with { Y = 0 };
		await Detail(pc + toward.Normalized() * 0.55f + Vector3.Up * 0.25f, pc, "12_cabin_cap_close");
		// the crosshair on it: the post is what the pick finds, not the page
		var use = post.Use;
		Check(use != null && use.CanInteract(_player), "cabin: the post can be taken");
		// take it: "It's warm."
		use?.Interact(_player);
		await Seconds(2.0);
		Newel("13_cabin_taken_its_warm");
		Check(_player.Inventory.HasNewelPost && StoryManager.Instance.NewelPostTaken, "cabin: the newel post in hand, flag set");
		await Seconds(3.6);
	}

	private async Task ClearingFuse()
	{
		var act6 = AllOf<Act6ClearingEvent>().FirstOrDefault();
		var stairs = act6?.OriginalStairs;
		Check(stairs != null && stairs.HasNewel && !stairs.NewelCapped, $"clearing: its staircase's newel post is broken before the voice (capped={stairs?.NewelCapped})");
		if (stairs == null) return;
		var fin = (GetTree().GetFirstNodeInGroup("final_stairs_marker") as Node)?.GetNodeOrNull<StaircaseBuilder>("Stairs");
		Check(fin != null && !fin.NewelCapped, $"clearing: the last staircase still broken too (capped={fin?.NewelCapped})");
		float H = stairs.TotalHeight, zb = stairs.BackZ;
		Vector3 L(float x, float y, float z) => stairs.GlobalTransform * new Vector3(x, y, z);
		Vector3 seat = stairs.NewelSeatGlobal.Origin;
		// a spot on the path (clear of the small stairs) well inside the voice radius, in front of the flight; and one outside it
		Vector3 centre = act6.GlobalPosition;
		_terrain.TrailDistance(centre.X, centre.Z, out float sc);
		Vector3 inside = Vector3.Zero, outside = Vector3.Zero;
		float bestIn = 1e9f, bestOut = 1e9f;
		for (float s = sc - 40f; s <= sc + 40f; s += 0.5f)
		{
			Vector3 p = _terrain.TrailPoint(s, out _);
			float d = new Vector2(p.X - centre.X, p.Z - centre.Z).Length();
			Vector3 lp = stairs.ToLocal(p);
			if (lp.Z < 2f || Mathf.Abs(lp.X) > 5f) continue;   // in front of the flight, roughly facing it
			if (Mathf.Abs(d - 11f) < bestIn) { bestIn = Mathf.Abs(d - 11f); inside = p; }
			if (Mathf.Abs(d - 19f) < bestOut) { bestOut = Mathf.Abs(d - 19f); outside = p; }
		}
		if (inside == Vector3.Zero) inside = L(0.5f, 0, 11f);
		if (outside == Vector3.Zero) outside = L(0.5f, 0, 19f);
		await StandAndLook(outside, seat + Vector3.Down * 1.5f);
		await Seconds(1.0);
		Newel("20_clearing_before_eye");
		await Detail(L(-0.25f, H + Eye, zb + 1.1f), seat + Vector3.Down * 0.2f, "21_clearing_before_landing");
		// walk in: the voice, then the cap flies
		await StandAndLook(inside, seat + Vector3.Down * 1.5f);
		await WaitUntil(() => !_player.Inventory.HasNewelPost, 15);
		Check(!_player.Inventory.HasNewelPost && StoryManager.Instance.ClearingVoiceHeard, "clearing: the voice, and the post leaves the player's hands");
		Check(StoryManager.Instance.ObjectivePosition is Vector3 obj && GetTree().GetFirstNodeInGroup("fire_lookout_marker") is Node3D lookout && obj.DistanceTo(lookout.GlobalPosition) < 0.01f,
			"clearing: the compass turns to the lookout");
		await Seconds(0.5);
		Newel("22_clearing_fuse_flying");
		await Seconds(0.8);
		Newel("23_clearing_fuse_flying_late");
		await WaitUntil(() => stairs.NewelCapped, 8);
		Check(stairs.NewelCapped, "clearing: the cap seats on the clearing staircase's newel post");
		Newel("24_clearing_fuse_flash");
		Check(fin != null && fin.NewelCapped, "clearing: the last staircase is whole again too");
		await Seconds(1.2);
		await StandAndLook(inside, seat + Vector3.Down * 1.5f);
		Newel("25_clearing_after_eye");
		await Detail(L(-0.25f, H + Eye, zb + 1.1f), seat + Vector3.Down * 0.2f, "26_clearing_after_landing");
		await Detail(L(0.1f, H + Eye - 0.1f, zb + 0.75f), seat + Vector3.Up * 0.1f, "27_clearing_after_close");
		Check(!AllOf<MeshInstance3D>().Any(m => m.Name == "FlyingNewelCap" && !m.IsQueuedForDeletion()), "clearing: the flying cap is gone");
	}

	private async Task LastStairs()
	{
		var fin = (GetTree().GetFirstNodeInGroup("final_stairs_marker") as Node)?.GetNodeOrNull<StaircaseBuilder>("Stairs");
		var act6 = AllOf<Act6ClearingEvent>().FirstOrDefault();
		Check(fin != null && fin.NewelCapped, $"last staircase: capped on Continue (capped={fin?.NewelCapped})");
		Check(act6?.OriginalStairs is { NewelCapped: true }, $"last staircase stop: the clearing's staircase capped on Continue too ({act6?.OriginalStairs?.NewelCapped})");
		if (fin == null) return;
		float H = fin.TotalHeight, zb = fin.BackZ;
		Vector3 L(float x, float y, float z) => fin.GlobalTransform * new Vector3(x, y, z);
		Vector3 seat = fin.NewelSeatGlobal.Origin;
		Vector3 foot = L(0.15f, 0, 2.2f);
		foot.Y = _terrain.HeightAt(foot.X, foot.Z) + Eye;
		await Detail(foot, seat + Vector3.Down * 2f, "30_last_stairs_foot");
		await Detail(L(-0.25f, H + Eye, zb + 1.1f), seat + Vector3.Down * 0.2f, "31_last_stairs_landing");
	}
}
