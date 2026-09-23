using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Dev harness for bunker_preview.tscn (not used by the game): stands a
/// first-person camera (FOV 70, with the lantern's glow) at the same viewpoints
/// as the pre-rework review shots and saves "after" shots to test-output/after/
/// (same file names as test-output/review/), plus extra views to
/// test-output/bunker/. Drives the bunker's states directly (door open, lights
/// red, screens off/stairs, maze glimpse, walkie) and the real atmosphere moods.
/// Pass "-- --only=<prefix>" to render a subset.
/// </summary>
public partial class BunkerPreview : Node3D
{
	[Export] public NodePath CameraPath = "../Camera3D";

	private Camera3D _cam;
	private OmniLight3D _lantern;
	private string _only = "";

	private record Shot(string Name, Vector3 Eye, Vector3 Look, string Dir = "after", bool Lantern = true);

	public override void _Ready()
	{
		_cam = GetNode<Camera3D>(CameraPath);
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--only=")) _only = a.Substring(7);
		_lantern = new OmniLight3D
		{
			Name = "LanternGlow", LightColor = new Color(1f, 0.72f, 0.42f), OmniRange = 7.5f, LightEnergy = 1.1f,
			OmniAttenuation = 1.4f, Position = new Vector3(0.18f, -0.25f, -0.25f), ShadowEnabled = true,
		};
		_cam.AddChild(_lantern);
		Run();
	}

	private async Task Frames(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

	private void Log(string s) => GD.Print("[bunker-preview] " + s);

	private async Task Take(Shot s)
	{
		if (_only != "" && !s.Name.StartsWith(_only)) return;
		_cam.GlobalPosition = s.Eye;
		_cam.LookAt(s.Look, Vector3.Up);
		_lantern.Visible = s.Lantern;
		await Frames(8);
		string dir = ProjectSettings.GlobalizePath($"res://test-output/{s.Dir}");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		GetViewport().GetTexture().GetImage().SavePng($"{dir}/{s.Name}.png");
		Log($"shot {s.Dir}/{s.Name}");
	}

	/// <summary>What a first-person look ray (as PlayerInteraction casts it) hits first.</summary>
	private string LookHit(Vector3 eye, Vector3 dir)
	{
		var q = PhysicsRayQueryParameters3D.Create(eye, eye + dir.Normalized() * 4f, 1u | Interactable.PickLayer);
		q.CollideWithAreas = true;
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
		if (hit.Count == 0) return "nothing";
		var n = hit["collider"].AsGodotObject() as Node;
		float d = eye.DistanceTo((Vector3)hit["position"]);
		return n is Area3D a && a.GetParent() is Interactable i ? $"INTERACTABLE '{i.Prompt}' at {d:0.00} m" : $"{n?.Name} at {d:0.00} m";
	}

	/// <summary>Walks a capsule on the player layer from <paramref name="from"/> toward <paramref name="to"/>;
	/// true if it ends up overlapping <paramref name="trigger"/>.</summary>
	private async Task<bool> WalkInto(Vector3 from, Vector3 to, Area3D trigger, float seconds)
	{
		var body = new CharacterBody3D { CollisionLayer = 2, CollisionMask = 1 };
		body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.75f }, Position = new Vector3(0, 0.875f, 0) });
		GetTree().CurrentScene.AddChild(body);
		body.GlobalPosition = from;
		bool inside = false;
		for (double t = 0; t < seconds && !inside; t += 1.0 / 60)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			var d = to - body.GlobalPosition; d.Y = 0;
			var v = d.LimitLength(1f) * 1.9f;
			v.Y = body.IsOnFloor() ? 0 : body.Velocity.Y - 15f / 60f;
			body.Velocity = v;
			body.MoveAndSlide();
			foreach (var b in trigger.GetOverlappingBodies()) if (b == body) inside = true;
		}
		Log($"  walker ended at {body.GlobalPosition} (target {to})");
		body.QueueFree();
		return inside;
	}

	private async Task PhysicsChecks(Bunker bunker, BunkerInterior interior)
	{
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		bunker.SetOpen(false);
		await Frames(3);
		Vector3 B(float x, float y, float z) => bunker.ToGlobal(new Vector3(x, y, z));
		var fwd = -bunker.GlobalBasis.Z;
		Log($"check locked hatch from 2 m, level eye: {LookHit(B(0, 1.62f + 0.22f, 2.2f), fwd)}");
		bunker.SetOpen(true);
		await Frames(3);
		var trigger = bunker.FindChild("EntryTrigger", true, false) as Area3D;
		bool entered = trigger != null && await WalkInto(bunker.ApproachPointWorld, bunker.EntryPointWorld + fwd * 0.3f, trigger, 6);
		Log($"check walk in through the open door reaches the entry trigger: {(entered ? "PASS" : "FAIL")}");
		bool side = trigger != null && await WalkInto(B(4.5f, 3f, -3f), bunker.EntryPointWorld, trigger, 5);
		Log($"check the trigger can't be reached from the mound's side: {(!side ? "PASS" : "FAIL")}");
		var o = interior.GlobalPosition;
		Log($"check vine door, pressed against it: {LookHit(o + new Vector3(0, 1.62f, -89.8f), Vector3.Forward)}");
		Log($"check vine door, from 1.5 m: {LookHit(o + new Vector3(0.2f, 1.62f, -88.5f), Vector3.Forward)}");
		Log($"check vine door, aimed at its middle from 2 m: {LookHit(o + new Vector3(0f, 1.62f, -88f), (interior.VineDoorInteractWorld - (o + new Vector3(0f, 1.62f, -88f))))}");
		float back = BunkerLayout.CrtRoomBackZ;
		Log($"check CRT switch, against the desk, level eye: {LookHit(o + new Vector3(0, 1.62f, back + 2.16f), Vector3.Forward)}");
		Log($"check CRT switch, from 1.2 m, looking at the screen: {LookHit(o + new Vector3(0.2f, 1.62f, back + 2.9f), interior.CrtSwitchWorld - (o + new Vector3(0.2f, 1.62f, back + 2.9f)))}");

		// The papers: each must be the first thing the look ray meets from where a player would stand.
		bunker.SetOpen(false);
		await Frames(3);
		Vector3 card = bunker.ToGlobal(Bunker.StationCardLocal);
		Vector3 cardEye = bunker.ToGlobal(new Vector3(1.3f, 1.62f + 0.22f, 2.0f));
		Log($"check station card from 1.8 m with the hatch locked: {LookHit(cardEye, card - cardEye)}");
		Log($"check the hatch is still 'Locked' beside it: {LookHit(B(0, 1.62f + 0.22f, 2.2f), fwd)}");
		Vector3 ledger = o + new Vector3(-0.57f, 0.95f, BunkerLayout.CrtRoomBackZ + 1.45f + 0.27f);
		Vector3 ledgerEye = o + new Vector3(-0.3f, 1.62f, back + 2.85f);
		Log($"check station log on the console: {LookHit(ledgerEye, ledger - ledgerEye)}");
		Vector3 ledgerEye2 = o + new Vector3(0.1f, 1.62f, back + 2.6f);
		Log($"check station log from in front of the screen: {LookHit(ledgerEye2, ledger - ledgerEye2)}");

		// The compass marker after the screens: the way out, then the maze's end; one node, moved.
		var marker = GetTree().GetFirstNodeInGroup("bunker_entrance_marker") as Node3D;
		bool atHall = marker != null && marker.GlobalPosition.DistanceTo(interior.ToGlobal(BunkerInterior.HallwayEntranceLocal)) < 0.01f;
		Log($"check compass marker starts at the hallway entrance: {(atHall ? "PASS" : "FAIL")} ({marker?.GlobalPosition})");
		interior.PointCompass(BunkerInterior.CompassSpot.MazeExit);
		bool atExit = marker != null && marker.GlobalPosition.DistanceTo(interior.ToGlobal(BunkerRooms.ExitLocal)) < 0.01f;
		Log($"check the same node moves to the maze exit: {(atExit ? "PASS" : "FAIL")} ({marker?.GlobalPosition})");
		interior.PointCompass(BunkerInterior.CompassSpot.Outside);
		bool atHatch = marker != null && marker.GlobalPosition.DistanceTo(bunker.GlobalPosition) < 0.01f;
		Log($"check outdoors it stands at the hatch: {(atHatch ? "PASS" : "FAIL")} ({marker?.GlobalPosition})");
		interior.PointCompass(BunkerInterior.CompassSpot.HallwayEntrance);
	}

	private async void Run()
	{
		await Frames(20);
		foreach (var n in new[] { "ScreenFader", "PauseMenu", "DebugOverlay", "Compass", "StaminaBar", "EquippedItemHud", "InteractPrompt", "Subtitle" })
			if (GetTree().Root.FindChild(n, true, false) is CanvasLayer cl) cl.Visible = false;
		var bunker = GetTree().GetFirstNodeInGroup("bunker_marker") as Bunker;
		var interior = BunkerInterior.Instance;
		var atmo = GetTree().Root.FindChild("Atmosphere", true, false) as ForestAtmosphere;
		var terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (bunker == null || interior == null) { GD.PushError("bunker or interior missing"); GetTree().Quit(2); return; }
		Log($"bunker at {bunker.GlobalPosition} front {bunker.GlobalBasis.Z}");
		if (terrain != null)
		{
			var sb = new System.Text.StringBuilder("terrain - bunker base, local (x,z):");
			foreach (var (x, z) in new[] { (0f, 3f), (0f, 1.5f), (0f, 0.5f), (0f, -1f), (0f, -2f), (-3f, 1f), (3f, 1f), (0f, -3f), (0f, -7f), (-6f, -2f), (6f, -2f), (-4f, 3f), (4f, 3f) })
			{
				var w = bunker.ToGlobal(new Vector3(x, 0, z));
				sb.Append($" ({x},{z})={terrain.HeightAt(w.X, w.Z) - bunker.GlobalPosition.Y:0.00}");
			}
			Log(sb.ToString());
		}
		Vector3 B(float x, float y, float z) => bunker.ToGlobal(new Vector3(x, y, z));
		Vector3 Ground(float x, float z, float eye = 1.62f)
		{
			var w = bunker.ToGlobal(new Vector3(x, 0, z));
			if (terrain != null) w.Y = terrain.HeightAt(w.X, w.Z);
			return w + Vector3.Up * eye;
		}

		await PhysicsChecks(bunker, interior);

		// ---------------------------------------------------------------- outside (review viewpoints)
		bunker.SetOpen(false);
		await Frames(30);
		await Take(new Shot("bunker_01_20m", new(-29.914766f, 22.572077f, -412.73218f), new(-10.5132885f, 9.972254f, -417.9841f)));
		await Take(new Shot("bunker_02_8m", new(-18.159115f, 15.020859f, -415.1928f), new(-10.5132885f, 9.872254f, -417.9841f)));
		await Take(new Shot("bunker_03_3m", new(-13.472694f, 12.598622f, -417.49225f), new(-10.5132885f, 9.772254f, -417.6841f)));
		await Take(new Shot("bunker_04_side", new(-12.325051f, 12.799774f, -410.58698f), new(-10.5132885f, 9.972254f, -418.9841f)));
		await Take(new Shot("bunker_05_back_mound", new(-2.1296911f, 8.7287655f, -416.33627f), new(-10.5132885f, 9.972254f, -418.9841f)));
		// Extra, bunker-relative: the door at eye level from the apron, and the locked hatch up close.
		await Take(new Shot("ext_front_8m", Ground(0.3f, 8f), B(0, 1.4f, 0), "bunker"));
		await Take(new Shot("ext_front_4m", Ground(0.6f, 4f), B(0, 1.3f, 0), "bunker"));
		// The station card beside the locked hatch: from where a player reads it, and in context.
		await Take(new Shot("paper_ext_card_1m", B(1.35f, 1.62f + 0.22f, 1.5f), B(1.62f, 1.42f, 0.3f), "bunker"));
		await Take(new Shot("paper_ext_card_context", Ground(1.4f, 3.2f), B(1.0f, 1.35f, 0.3f), "bunker"));
		await Take(new Shot("ext_threequarter", Ground(5.5f, 6f), B(0, 1.2f, -1f), "bunker"));
		await Take(new Shot("ext_top_mound", Ground(-4f, -9f), B(0, 2.5f, -2f), "bunker"));
		// the mound's crest from behind and from the flanks, where the earth meets the coping
		await Take(new Shot("mound_crest_back", Ground(0f, -8.5f), B(0, 3.2f, 0f), "bunker"));
		await Take(new Shot("mound_crest_back_left", Ground(-5f, -7f), B(-1f, 3.1f, 0f), "bunker"));
		await Take(new Shot("mound_crest_side", Ground(6.5f, -1.5f), B(1.5f, 3.1f, 0f), "bunker"));
		await Take(new Shot("mound_crest_on_top", B(0f, 7.4f, -3.5f), B(0, 2.6f, 1.8f), "bunker"));
		await Take(new Shot("mound_crest_on_top_flank", B(-3.2f, 6.6f, -1.2f), B(1.5f, 2.8f, 0.8f), "bunker"));
		await Take(new Shot("mound_crest_level", B(3.5f, 5.2f, -1.2f), B(-3f, 4.4f, -0.4f), "bunker"));
		bunker.SetOpen(true);
		await Frames(5);
		await Take(new Shot("bunker_06_open_8m", new(-18.159115f, 15.020859f, -415.1928f), new(-10.5132885f, 9.872254f, -417.9841f)));
		await Take(new Shot("bunker_07_open_2m", new(-12.683519f, 12.243738f, -417.6234f), new(-10.5132885f, 9.672255f, -418.4841f)));
		await Take(new Shot("ext_open_4m", Ground(0.4f, 4f), B(0, 1.2f, -1.5f), "bunker"));
		await Take(new Shot("ext_open_doorway_1m", Ground(0f, 1.3f), B(0, 1.3f, -2.4f), "bunker"));
		if (atmo != null)
		{
			atmo.SetMood(ForestAtmosphere.Mood.Night, 0.05f);
			await Frames(20);
			await Take(new Shot("ext_night_8m", Ground(0.3f, 8f), B(0, 1.4f, 0), "bunker"));
			await Take(new Shot("ext_night_open_4m", Ground(0.4f, 4f), B(0, 1.2f, -1.5f), "bunker"));
		}

		// ---------------------------------------------------------------- the hallway (night mood, as on entry)
		atmo?.SetMood(ForestAtmosphere.Mood.Night, 0.05f);
		await Frames(20);
		await Take(new Shot("int_01_hall_entrance", new(1500f, -78.38f, -3000.8f), new(1500f, -78.38f, -3020f)));
		await Take(new Shot("int_02_hall_lookback_cap", new(1500.5f, -78.38f, -3003f), new(1500f, -78.2f, -3000f)));
		await Take(new Shot("int_03_hall_mid", new(1498.8f, -78.38f, -3040f), new(1500f, -78.1f, -3060f)));
		await Take(new Shot("int_04_hall_ceiling", new(1500f, -78.38f, -3030f), new(1501.4f, -76.8f, -3034f)));
		await Take(new Shot("hall_cap_lookback_nolantern", new(1500.5f, -78.38f, -3003f), new(1500f, -78.2f, -3000f), "bunker", false));
		await Take(new Shot("hall_mid_nolantern", new(1498.8f, -78.38f, -3040f), new(1500f, -78.1f, -3060f), "bunker", false));
		interior.Hallway.ForceDepth(83f);
		await Frames(10);
		await Take(new Shot("int_05_hall_red_forward", new(1500f, -78.38f, -3080f), new(1500f, -78.38f, -3090f)));
		await Take(new Shot("int_06_hall_red_back", new(1500f, -78.38f, -3080f), new(1500f, -78.38f, -3060f)));
		await Take(new Shot("int_07_vine_door_2m", new(1500.4f, -78.38f, -3087.5f), new(1500f, -78.4f, -3090f)));
		await Take(new Shot("vine_door_4m_nolantern", new(1500.2f, -78.38f, -3086f), new(1500f, -78.6f, -3090f), "bunker", false));
		interior.VineDoor.Open();
		await Seconds(1.6);
		await Take(new Shot("vine_door_open", new(1500.3f, -78.38f, -3087f), new(1500f, -78.6f, -3092f), "bunker"));

		// ---------------------------------------------------------------- the CRT room
		await Take(new Shot("int_08_crt_room_entry", new(1500f, -78.38f, -3092f), new(1500f, -78.4f, -3122f)));
		await Take(new Shot("int_09_crt_side_wall", new(1501.5f, -78.38f, -3104f), new(1494f, -78.2f, -3108f)));
		await Take(new Shot("int_10_crt_target_close", new(1500.4f, -78.38f, -3119f), new(1500f, -79.1f, -3121.7f)));
		await Take(new Shot("crt_room_nolantern", new(1500f, -78.38f, -3094f), new(1500f, -78.6f, -3122f), "bunker", false));
		await Take(new Shot("crt_target_nolantern", new(1500.3f, -78.38f, -3118.8f), new(1500f, -78.9f, -3121.6f), "bunker", false));
		// The papers in the CRT room: the station log on the console; the open filing drawer (no papers in it).
		var o2 = interior.GlobalPosition;
		float backZ = BunkerLayout.CrtRoomBackZ;
		await Take(new Shot("paper_console_log", o2 + new Vector3(-0.3f, 1.62f, backZ + 2.85f), o2 + new Vector3(-0.57f, 0.95f, backZ + 1.72f), "bunker"));
		await Take(new Shot("paper_console_log_context", o2 + new Vector3(0.1f, 1.62f, backZ + 3.2f), o2 + new Vector3(-0.2f, 1.0f, backZ + 1.5f), "bunker"));
		await Take(new Shot("drawer_open",o2 + new Vector3(-4.35f, 1.62f, BunkerLayout.CrtRoomFrontZ - 2.3f), o2 + new Vector3(-5.14f, 0.915f, BunkerLayout.CrtRoomFrontZ - 2.52f), "bunker"));
		await Take(new Shot("paper_drawer_context", o2 + new Vector3(-3.6f, 1.62f, BunkerLayout.CrtRoomFrontZ - 1.6f), o2 + new Vector3(-5.4f, 0.9f, BunkerLayout.CrtRoomFrontZ - 2.5f), "bunker"));
		interior.Crt.TurnAllOff();
		await Seconds(0.8);
		await Take(new Shot("crt_screens_off", new(1500f, -78.38f, -3100f), new(1500f, -78.4f, -3122f), "bunker", false));
		interior.Crt.TurnOnStairs(3.5f);
		await Seconds(1.2);
		await Take(new Shot("crt_stairs_fuzzy", new(1500.3f, -78.38f, -3118.8f), new(1500f, -78.9f, -3121.6f), "bunker", false));
		await Seconds(4.0);
		await Take(new Shot("int_11_crt_showing_stairs", new(1500f, -78.38f, -3100f), new(1500f, -78.4f, -3122f)));
		await Take(new Shot("crt_stairs_close", new(1500.3f, -78.38f, -3118.8f), new(1500f, -78.9f, -3121.6f), "bunker", false));
		// main-menu candidates: the room in its later-act state, every screen on the stairs, no lantern
		await Take(new Shot("menu_a_wall_straight", new(1500f, -78.38f, -3110f), new(1500f, -78.0f, -3122f), "menu", false));
		await Take(new Shot("menu_b_wall_angled", new(1497.5f, -78.6f, -3113f), new(1501f, -78.2f, -3121f), "menu", false));
		await Take(new Shot("menu_c_low_angle", new(1502.5f, -78.9f, -3114f), new(1498.5f, -78.0f, -3121.5f), "menu", false));
		await Take(new Shot("menu_d_side_wall", new(1498f, -78.38f, -3106f), new(1494f, -78.2f, -3107.5f), "menu", false));
		await Take(new Shot("menu_e_desk_low", new(1500f, -79.3f, -3116.5f), new(1500f, -77.7f, -3122f), "menu", false));
		await Take(new Shot("menu_f_long_room", new(1500f, -78.38f, -3095f), new(1500f, -78.3f, -3122f), "menu", false));

		// ---------------------------------------------------------------- the maze (menacing mood)
		atmo?.SetMood(ForestAtmosphere.Mood.Menacing, 0.05f);
		interior.Rooms.Active = false;
		await Frames(30);
		await Take(new Shot("maze_01_start", new(1800f, -78.38f, -3000f), new(1804f, -78.38f, -3000f)));
		await Frames(3);
		await Take(new Shot("rooms_02_doors", new(1800f, -78.38f, -3001f), new(1800f, -78.6f, -3007f)));
		await Seconds(0.8);
		await Frames(3);
		await Take(new Shot("rooms_03_left_door", new(1800f, -78.38f, -3003.5f), new(1795.5f, -78.9f, -3003.5f)));
		await Seconds(0.8);
		await Take(new Shot("maze_04_mid", new(1808f, -78.38f, -3020f), new(1812f, -78.38f, -3020f)));
		await Take(new Shot("maze_05_mid_down", new(1808f, -78.38f, -3020f), new(1812f, -79.7f, -3020f)));
		await Take(new Shot("maze_mid_nolantern", new(1808f, -78.38f, -3020f), new(1812f, -78.38f, -3020f), "bunker", false));
		// The walkie, as dropped at the exit.
		var walkie = new WalkiePickup { Name = "WalkiePickup", Dead = false, Position = BunkerRooms.ExitLocal - BunkerLayout.MazeOffset + new Vector3(1.0f, -1f, -1.5f), Rotation = new Vector3(0, 0.5f, 0) };
		interior.Rooms.AddChild(walkie);
		walkie.AddChild(new WalkieBeacon { Name = "Beacon", Position = new Vector3(0.022f, 0.158f, 0.024f) });
		await Frames(10);
		var wp = walkie.GlobalPosition;
		Log($"walkie at {wp}");
		await Take(new Shot("maze_06_exit_walkie", new(1816f, -78.38f, -3036f), wp));
		await Take(new Shot("maze_07_walkie_close", new(1820.2999f, -78.399994f, -3036.7f), wp + new Vector3(0, 0.1f, 0)));
		await Take(new Shot("maze_08_walkie_side_level", new(1820f, -79.65f, -3037.5f), wp + new Vector3(0, 0.05f, 0)));
		await Take(new Shot("maze_exit_door_nolantern", new(1820f, -78.38f, -3033f), new(1820f, -78.8f, -3038f), "bunker", false));
		await Take(new Shot("maze_walkie_dark", new(1818.5f, -78.38f, -3035f), wp, "bunker", false));

		// ---------------------------------------------------------------- restore: Continue at checkpoint 7 after the screens
		// The real path (Flow.Restore with the CRT flag) must leave every hallway lamp red at once.
		atmo?.SetMood(ForestAtmosphere.Mood.Night, 0.05f);
		interior.Hallway.ResetLightsForPreview();
		await Frames(2);
		var before = interior.Hallway.LampTally();
		if (StoryManager.Instance is { } story)
		{
			story.SetFlag(StoryManager.Flag.CrtPuzzleDone);   // checkpoint None: nothing is saved
			interior.Flow.Restore();
		}
		await Frames(10);
		var after = interior.Hallway.LampTally();
		Log($"check hallway red on restore with crt_puzzle_done: {(before.red == 0 && after.red == after.total && interior.RedTriggered ? "PASS" : "FAIL")} (before {before.red}/{before.total}, after {after.red}/{after.total})");
		await Take(new Shot("hall_red_on_restore", new(1500f, -78.38f, -3000.8f), new(1500f, -78.38f, -3020f), "bunker"));
		await Take(new Shot("hall_red_on_restore_mid", new(1498.8f, -78.38f, -3040f), new(1500f, -78.1f, -3060f), "bunker"));

		// ---------------------------------------------------------------- the end card
		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
		{
			fader.Visible = true;
			fader.SetBlack(true);
			_ = Act11Ending.ShowEndCard(fader, -1f);
			await Seconds(2.0);
			await Take(new Shot("end_card", _cam.GlobalPosition, _cam.GlobalPosition + Vector3.Forward, "bunker", false));
			fader.Visible = false;
		}

		Log("done");
		GetTree().Quit(0);
	}
}
