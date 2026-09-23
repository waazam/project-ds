using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Drives hollow_preview.tscn; see <see cref="HollowPreview"/>. For every stop along the Hollow's
/// route it writes a save for that point of the story, Continues into hollow.tscn (a real scene
/// load through StoryManager, so every system restores itself as it would for a player), checks
/// the story state there (the compass objective resolves to the right node, the beat's pieces are
/// where they belong), stands the player somewhere telling and screenshots at 640x360.
///
/// "route" (on the first load) samples the path end to end: the slope along and across it at every
/// half metre (fails on anything the player controller cannot climb), a body-sized probe at chest
/// height every metre for anything standing on the path, and the layout's distances (camp, key,
/// cabin, axe, shed, bridge, clearing, lookout, bunker, last staircase) in metres along the path.
/// Quits non-zero on any failed check. Report: test-output/hollow/report.txt.
/// </summary>
public partial class HollowPreviewDriver : Node
{
	private const float Eye = 1.62f;
	/// <summary>PlayerController.FloorMaxAngle is 48 degrees; keep a margin.</summary>
	private const float MaxWalkableDeg = 44f;

	private string _out;
	private int _fails;
	private readonly StringBuilder _report = new();
	private readonly List<string> _backedUp = new();
	private HashSet<string> _only;
	private PlayerController _player;
	private ForestTerrain _terrain;

	private static readonly string[] F2 = { StoryManager.Flag.StairsClimbed };
	private static readonly string[] F3Storm = F2.Concat(new[] { StoryManager.Flag.StormStarted, StoryManager.Flag.PickupTakenLantern, StoryManager.Flag.PickupTakenCompass, SafeZoneWatcher.ThingsLineFlag }).ToArray();
	private static readonly string[] F3Giant = F3Storm.Concat(new[] { StoryManager.Flag.GiantEventDone, StoryManager.Flag.PickupTakenKey }).ToArray();
	private static readonly string[] F5 = F3Giant.Concat(new[] { StoryManager.Flag.CabinDoorOpen, StoryManager.Flag.PickupTakenAxe, CabinReturnEvent.SeenFlag }).ToArray();
	private static readonly string[] F5Post = F5.Concat(new[] { StoryManager.Flag.NewelPostTaken, StoryManager.Flag.DawnBroke }).ToArray();
	private static readonly string[] F6Voice = F5Post.Concat(new[] { StoryManager.Flag.ClearingVoiceHeard }).ToArray();
	private static readonly string[] F7 = F6Voice.Concat(new[] { StoryManager.Flag.Act6NightFell }).ToArray();
	private static readonly string[] F10 = F7.Concat(new[] { StoryManager.Flag.CrtPuzzleDone, StoryManager.Flag.BunkerMazeEntered, StoryManager.Flag.BunkerMazeExited }).ToArray();
	private static readonly string[] F11 = F10.Concat(new[] { StoryManager.Flag.Act11DialogueDone }).ToArray();

	private record Stop(string Name, Checkpoint Cp, string[] Flags, string Inventory, string ObjectiveGroup, Func<Task> Run);

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_out = ProjectSettings.GlobalizePath("res://test-output/hollow");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		using (FileAccess.Open("res://test-output/.gdignore", FileAccess.ModeFlags.Write)) { }
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--only=")) _only = new HashSet<string>(a.Substring(7).Split(','));
		Run();
	}

	// ------------------------------------------------------------------ helpers

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s, true), SceneTreeTimer.SignalName.Timeout);
	private void Log(string s) { GD.Print("[hollow] " + s); _report.AppendLine(s); }
	private void Check(bool ok, string what) { Log($"{(ok ? "PASS" : "FAIL")} {what}"); if (!ok) _fails++; }
	private bool Want(string s) => _only == null || _only.Contains(s);

	private void Shot(string name)
	{
		var img = GetViewport().GetTexture().GetImage();
		img.SavePng($"{_out}/{name}.png");
		Log($"shot {name}.png ({img.GetWidth()}x{img.GetHeight()})");
	}

	private T First<T>(string group) where T : class => GetTree().GetFirstNodeInGroup(group) as T;
	private static IEnumerable<Node> All(Node n) { yield return n; foreach (var c in n.GetChildren()) foreach (var d in All(c)) yield return d; }
	private IEnumerable<T> AllOf<T>() where T : class => All(GetTree().CurrentScene).OfType<T>();
	private Pickup PickupOf(ToolKind k) => AllOf<Pickup>().FirstOrDefault(p => p.Kind == k && IsInstanceValid(p) && !p.IsQueuedForDeletion());
	private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
	private Vector3 Ground(Vector3 p) => new(p.X, _terrain.HeightAt(p.X, p.Z), p.Z);
	private float TrailS(Vector3 p) { _terrain.TrailDistance(p.X, p.Z, out float s); return s; }
	private float TrailD(Vector3 p) => _terrain.TrailDistance(p.X, p.Z, out _);

	/// <summary>Stand the player at a ground point and aim the camera at a world point.</summary>
	private async Task Stand(Vector3 at, Vector3 lookAt)
	{
		Vector3 g = Ground(at) + Vector3.Up * 0.1f;
		Vector3 d0 = lookAt - g;
		float yaw = Mathf.Atan2(-d0.X, -d0.Z);
		_player.Teleport(g, yaw);
		await Frames(4);
		var eye = _player.CameraRig.Camera.GlobalPosition;
		Vector3 d = lookAt - eye;
		_player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
		_player.CameraRig.SetPitch(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
		await Frames(3);
	}

	/// <summary>Stand on the path <paramref name="back"/> metres before arc length <paramref name="s"/>, looking at a point.</summary>
	private Task StandOnTrail(float s, Vector3 lookAt) => Stand(_terrain.TrailPoint(s, out _), lookAt);

	// ------------------------------------------------------------------ saves

	private static string UserFile(string name) => ProjectSettings.GlobalizePath("user://" + name);

	private void BackUpSlots()
	{
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
		{
			string src = UserFile(slot);
			if (!FileAccess.FileExists(src)) continue;
			if (DirAccess.CopyAbsolute(src, UserFile("hollow_preview_backup_" + slot)) == Error.Ok) _backedUp.Add(slot);
		}
		Log($"backed up {_backedUp.Count} save slot(s)");
	}

	private void RestoreSlots()
	{
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
		{
			string dst = UserFile(slot), bak = UserFile("hollow_preview_backup_" + slot);
			if (_backedUp.Contains(slot) && FileAccess.FileExists(bak))
			{
				DirAccess.CopyAbsolute(bak, dst);
				DirAccess.RemoveAbsolute(bak);
			}
			else if (FileAccess.FileExists(dst)) DirAccess.RemoveAbsolute(dst);
		}
		Log("save slots put back");
	}

	/// <summary>Writes a save for the stop and Continues into it (a real level load).</summary>
	private async Task<bool> ContinueInto(Stop st)
	{
		SaveSystem.Save(new SaveData
		{
			Checkpoint = st.Cp,
			PosX = 0, PosY = 0, PosZ = 40, Yaw = 0f,
			Flags = st.Flags,
			Inventory = st.Inventory,
		});
		var before = GetTree().CurrentScene;
		if (!StoryManager.Instance.ContinueGame()) { Check(false, $"{st.Name}: ContinueGame accepted the save"); return false; }
		for (int i = 0; i < 900 && (GetTree().CurrentScene == before || GetTree().CurrentScene == null); i++) await Frames(1);
		for (int i = 0; i < 600 && GameFlow.Instance is not { Started: true }; i++) await Frames(1);
		await Seconds(2.2);   // the fade-in, the scatter's deferred build, the pickups settling
		_player = First<PlayerController>("player");
		_terrain = First<ForestTerrain>("terrain");
		if (_player == null || _terrain == null) { Check(false, $"{st.Name}: level loaded with a player and terrain"); return false; }
		_player.PlayerInput.Scripted = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		Check(StoryManager.Instance.Current == st.Cp, $"{st.Name}: checkpoint {st.Cp} restored ({StoryManager.Instance.Current})");
		Check(GetTree().CurrentScene.SceneFilePath == StoryManager.HollowScene, $"{st.Name}: Continue loads the Hollow ({GetTree().CurrentScene.SceneFilePath})");
		CheckObjective(st);
		return true;
	}

	private void CheckObjective(Stop st)
	{
		var obj = StoryManager.Instance.ObjectivePosition;
		if (st.ObjectiveGroup == null) { Check(obj == null, $"{st.Name}: no compass objective ({obj})"); return; }
		var nodes = GetTree().GetNodesInGroup(st.ObjectiveGroup);
		var node = nodes.Count > 0 ? nodes[0] as Node3D : null;
		Check(nodes.Count == 1, $"{st.Name}: exactly one node in '{st.ObjectiveGroup}' ({nodes.Count})");
		Check(obj.HasValue && node != null && obj.Value.DistanceTo(node.GlobalPosition) < 0.01f,
			$"{st.Name}: the compass points at '{st.ObjectiveGroup}' ({obj} vs {node?.GlobalPosition})");
	}

	// ------------------------------------------------------------------ the run

	private async void Run()
	{
		try
		{
			await Frames(10);
			if (StoryManager.Instance == null) { GD.PushError("[hollow] no StoryManager autoload"); GetTree().Quit(2); return; }
			BackUpSlots();
			var stops = new List<Stop>
			{
				new("wake", Checkpoint.Act2StairsClimbed, F2, ";tools=", null, Wake),
				new("camp", Checkpoint.Act3DoorBoarded, F2, ";tools=", "cabin", Camp),
				new("giant", Checkpoint.Act3DoorBoarded, F3Storm, "lantern,compass;tools=", "cabin", Giant),
				new("cabin", Checkpoint.Act3DoorBoarded, F3Giant, "lantern,compass;tools=", "cabin", CabinStop),
				new("inside", Checkpoint.Act5CabinEntered, F5, "lantern,compass;tools=", "cabin", Inside),
				new("bridge", Checkpoint.Act5CabinEntered, F5Post, "lantern,compass,newel_post;tools=", "bridge_marker", Bridge),
				new("clearing", Checkpoint.Act6BridgeCrossed, F5Post, "lantern,compass,newel_post;tools=", "stairs_clearing_marker", Clearing),
				new("lookout", Checkpoint.Act7CabinBurning, F7, "lantern,compass;tools=", "bunker_marker", Lookout),
				new("bunker", Checkpoint.Act7CabinBurning, F7, "lantern,compass;tools=", "bunker_marker", BunkerStop),
				new("laststairs", Checkpoint.Act10WalkieFound, F11, "lantern,compass,radio;tools=", "final_stairs_marker", LastStairs),
				new("ending", Checkpoint.Act11GiantEncounter, F11, "lantern,compass,radio;tools=", null, Ending),
			};
			bool routeDone = false;
			foreach (var st in stops)
			{
				if (!Want(st.Name) && !(Want("route") && !routeDone)) continue;
				Log($"--- {st.Name} ({st.Cp})");
				if (!await ContinueInto(st)) continue;
				if (!routeDone && Want("route")) { routeDone = true; await Route(); }
				if (Want(st.Name)) await st.Run();
			}
		}
		catch (Exception e)
		{
			Check(false, "preview ran without an exception: " + e);
		}
		RestoreSlots();
		Log($"=== {(_fails == 0 ? "ALL PASS" : $"{_fails} FAILED")}");
		using (var f = FileAccess.Open($"{_out}/report.txt", FileAccess.ModeFlags.Write)) f?.StoreString(_report.ToString());
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	// ------------------------------------------------------------------ the route

	private async Task Route()
	{
		await Frames(2);
		float len = _terrain.TrailLength;
		Log($"route: path length {len:0} m, terrain {_terrain.MaxXZ.X - _terrain.MinXZ.X:0} x {_terrain.MaxXZ.Y - _terrain.MinXZ.Y:0} m (cells {(_terrain.MaxXZ.X - _terrain.MinXZ.X) * (_terrain.MaxXZ.Y - _terrain.MinXZ.Y) / (_terrain.CellSize * _terrain.CellSize):0}; trailhead 241400)");

		// the layout, in metres along the path
		void Where(string name, Node3D n) { if (n == null) { Log($"layout: {name} MISSING"); return; } Log($"layout: {name,-18} s={TrailS(n.GlobalPosition),6:0.0}  off path {TrailD(n.GlobalPosition),5:0.0} m  at {n.GlobalPosition}"); }
		var spawn = First<Node3D>("player_spawn");
		var camp = AllOf<Camp>().FirstOrDefault();
		var key = PickupOf(ToolKind.Key);
		var cabin = First<Cabin>("cabin");
		var axe = PickupOf(ToolKind.Axe);
		var shed = AllOf<Shed>().FirstOrDefault();
		var bridge = First<Node3D>("bridge_marker");
		var clearing = First<Node3D>("stairs_clearing_marker");
		var lookout = First<Node3D>("fire_lookout_marker");
		var bunker = First<Node3D>("bunker_marker");
		var last = First<Node3D>("final_stairs_marker");
		Where("wake spot", spawn); Where("camp", camp); Where("cabin", cabin); Where("axe", axe); Where("shed", shed);
		Where("bridge", bridge); Where("clearing", clearing); Where("lookout", lookout); Where("bunker", bunker); Where("last staircase", last);
		for (int cp = 3; cp <= 9; cp++)
		{
			string g = $"respawn_{(Checkpoint)cp}";
			var m = First<Node3D>(g);
			Check(m != null, $"route: respawn marker {g}");
			if (m == null) continue;
			float h = _terrain.HeightAt(m.GlobalPosition.X, m.GlobalPosition.Z);
			Where(g, m);
			Check(m.GlobalPosition.DistanceTo(bunker?.GlobalPosition ?? Vector3.Inf) < 30f || Mathf.Abs(m.GlobalPosition.Y - h) < 1.5f || m.GlobalPosition.Y < h + 1.5f, $"route: {g} on the ground ({m.GlobalPosition.Y:0.00} vs ground {h:0.00})");
		}
		if (camp != null && cabin != null)
		{
			float sCamp = TrailS(camp.GlobalPosition), sCabin = TrailS(cabin.GlobalPosition);
			Log($"layout: camp to cabin {sCabin - sCamp:0} m along the path; wake spot to camp {spawn?.GlobalPosition.DistanceTo(camp.GlobalPosition):0} m");
			if (key != null) Log($"layout: the key at {(TrailS(key.GlobalPosition) - sCamp) / (sCabin - sCamp) * 100f:0}% of the way from the camp to the cabin");
			if (axe != null) Check(Flat(axe.GlobalPosition).DistanceTo(Flat(cabin.GlobalPosition)) is > 28f and < 45f && TrailD(axe.GlobalPosition) > 8f,
				$"route: the axe is 30-40 m from the cabin, off the path ({Flat(axe.GlobalPosition).DistanceTo(Flat(cabin.GlobalPosition)):0.0} m, {TrailD(axe.GlobalPosition):0.0} m off)");
			if (shed != null) Check(Flat(shed.GlobalPosition).DistanceTo(Flat(cabin.GlobalPosition)) is > 14f and < 26f, $"route: the shed ~20 m from the cabin ({Flat(shed.GlobalPosition).DistanceTo(Flat(cabin.GlobalPosition)):0.0} m)");
		}
		if (lookout != null && cabin != null)
			Check(Flat(lookout.GlobalPosition).DistanceTo(Flat(cabin.GlobalPosition)) is >= 70f and <= 110f, $"route: lookout 70-110 m from the cabin ({Flat(lookout.GlobalPosition).DistanceTo(Flat(cabin.GlobalPosition)):0.0} m)");
		// order along the path
		var order = new (string, Node3D)[] { ("camp", camp), ("cabin", cabin), ("bridge", bridge), ("clearing", clearing), ("lookout", lookout), ("bunker", bunker), ("last staircase", last) };
		float prevS = -1f; string prevName = "start";
		foreach (var (name, n) in order)
		{
			if (n == null) { Check(false, $"route: {name} exists"); continue; }
			float s = TrailS(n.GlobalPosition);
			Check(s > prevS, $"route: {name} comes after {prevName} along the path ({s:0} > {prevS:0})");
			prevS = s; prevName = name;
		}
		Check(_terrain.TryGetStreamCrossing(out Vector3 xing, out _, out float xs) && bridge != null && Flat(xing).DistanceTo(Flat(bridge.GlobalPosition)) < 6f,
			$"route: the path crosses the creek once, at the footbridge (crossing at s={xs:0}, {xing})");

		// walkability: slope along and across the path
		float worstAlong = 0f, worstAcross = 0f, sWorstAlong = 0f, sWorstAcross = 0f;
		int steep = 0;
		Vector3 prev = _terrain.TrailPoint(0f, out _);
		for (float s = 0.5f; s <= len; s += 0.5f)
		{
			Vector3 p = _terrain.TrailPoint(s, out Vector3 t);
			float run = Flat(p).DistanceTo(Flat(prev));
			float along = run > 0.01f ? Mathf.RadToDeg(Mathf.Atan2(Mathf.Abs(p.Y - prev.Y), run)) : 0f;
			float across = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(_terrain.NormalAt(p.X, p.Z).Y, -1f, 1f)));
			if (along > worstAlong) { worstAlong = along; sWorstAlong = s; }
			if (across > worstAcross) { worstAcross = across; sWorstAcross = s; }
			if (along > MaxWalkableDeg || across > MaxWalkableDeg) steep++;
			prev = p;
		}
		Check(steep == 0, $"route: no stretch of path steeper than {MaxWalkableDeg} deg ({steep} samples; worst along {worstAlong:0.0} deg at s={sWorstAlong:0}, worst ground {worstAcross:0.0} deg at s={sWorstAcross:0})");

		// nothing standing on the path: a body-sized probe at chest height every metre
		var blocked = ProbePath(1f, len - 1.5f);
		Check(blocked.Count == 0, $"route: nothing stands on the path ({blocked.Count} hits){(blocked.Count > 0 ? ": " + string.Join("; ", blocked.Take(12)) : "")}");
		var space = _player.GetWorld3D().DirectSpaceState;
		if (bridge != null)
		{
			// the deck carries the path: probe along it from above
			bool deck = false;
			var q = PhysicsRayQueryParameters3D.Create(bridge.GlobalPosition + Vector3.Up * 4f, bridge.GlobalPosition + Vector3.Down * 4f, 1u);
			var hit = space.IntersectRay(q);
			if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node n) deck = GetTree().CurrentScene.GetPathTo(n).ToString().Contains("Footbridge");
			Check(deck, "route: the footbridge deck spans the crossing");
		}
		// what the stalker and the compass see: exactly one of each marker group
		foreach (var g in new[] { "cabin", "bridge_marker", "stairs_clearing_marker", "fire_lookout_marker", "bunker_marker", "crt_target_marker", "bunker_entrance_marker", "final_stairs_marker", "player_spawn" })
			Check(GetTree().GetNodesInGroup(g).Count == 1, $"route: one node in '{g}' ({GetTree().GetNodesInGroup(g).Count})");
		// the two staircases' first-climb triggers: inert in the Hollow (checkpoint 2 on)
		int armed = AllOf<FirstClimbEvent>().Count(f => !f.Fired);
		Check(armed == 0, $"route: both staircases' first-climb triggers are inert ({armed} armed)");
		Log($"route: stairs_top_trigger nodes {GetTree().GetNodesInGroup("stairs_top_trigger").Count} (first: {(GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Node)?.GetParent()?.GetParent()?.Name})");
	}

	/// <summary>Anything (but the ground and the footbridge deck) a walking body would bump into on the path between two arc lengths.</summary>
	private List<string> ProbePath(float s0, float s1)
	{
		var space = _player.GetWorld3D().DirectSpaceState;
		var bodyShape = new SphereShape3D { Radius = 0.3f };
		var ex = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var blocked = new List<string>();
		for (float s = Mathf.Max(s0, 0f); s < Mathf.Min(s1, _terrain.TrailLength - 1.5f); s += 1f)
		{
			Vector3 p = _terrain.TrailPoint(s, out _);
			foreach (float h in new[] { 0.7f, 1.3f })
			{
				var q = new PhysicsShapeQueryParameters3D { Shape = bodyShape, Transform = new Transform3D(Basis.Identity, p + Vector3.Up * h), CollisionMask = 1u, Exclude = ex };
				foreach (var hit in space.IntersectShape(q, 4))
				{
					var col = hit["collider"].AsGodotObject() as Node;
					string path = col != null ? GetTree().CurrentScene.GetPathTo(col).ToString() : "(scatter body)";
					if (path.Contains("Footbridge") || path.StartsWith("HollowWorld/Terrain")) continue;   // the deck and the ground itself
					blocked.Add($"s={s:0} h={h} {path}");
				}
			}
		}
		return blocked.Distinct().ToList();
	}

	// ------------------------------------------------------------------ the stops

	private async Task Wake()
	{
		var spawn = First<Node3D>("player_spawn");
		var camp = AllOf<Camp>().FirstOrDefault();
		Check(Flat(_player.GlobalPosition).DistanceTo(Flat(spawn.GlobalPosition)) < 3f, "wake: Continue at checkpoint 2 puts the player at the wake spot");
		if (camp != null) Log($"wake: camp {Flat(spawn.GlobalPosition).DistanceTo(Flat(camp.GlobalPosition)):0} m ahead");
		await Stand(spawn.GlobalPosition, (camp?.StumpTopWorld ?? spawn.GlobalPosition + Vector3.Forward * 60f) + Vector3.Up * 0.3f);
		await Seconds(0.5);
		Shot("01_wake_view_to_camp");
	}

	private async Task Camp()
	{
		var camp = AllOf<Camp>().FirstOrDefault();
		var lantern = PickupOf(ToolKind.Lantern);
		var compass = PickupOf(ToolKind.Compass);
		Check(camp != null && lantern != null && compass != null && lantern.Visible && compass.Visible, "camp: the lantern and compass wait at the camp");
		if (camp == null || lantern == null) return;
		Check(lantern.GlobalPosition.DistanceTo(camp.StumpTopWorld) < 0.4f && compass != null && compass.GlobalPosition.DistanceTo(camp.StumpTopWorld) < 0.4f,
			$"camp: both on the stump (lantern {lantern.GlobalPosition.DistanceTo(camp.StumpTopWorld):0.00} m, compass {compass?.GlobalPosition.DistanceTo(camp.StumpTopWorld):0.00} m from its top)");
		var note = AllOf<Readable>().OrderBy(r => r.GlobalPosition.DistanceTo(lantern.GlobalPosition)).FirstOrDefault();
		Check(note != null && note.GlobalPosition.DistanceTo(lantern.GlobalPosition) < 6f && note.Text == World.Camp.NoteText, $"camp: his note beside the lantern ({note?.GlobalPosition.DistanceTo(lantern.GlobalPosition):0.00} m)");
		Log($"camp: note text: {note?.Text.Replace("\n", " / ")}");
		if (note?.GetParent() is MeshInstance3D paper) Log($"camp: note paper visible {paper.IsVisibleInTree()} at {paper.GlobalPosition}, normal {paper.GlobalBasis.Z}, stump top {camp.StumpTopWorld}");
		Vector3 stump = camp.StumpTopWorld;
		float s = TrailS(stump);
		await StandOnTrail(s - 9f, stump + Vector3.Up * 0.2f);
		Shot("02_camp_from_path");
		// close: what the player sees on the stump; the crosshair on the note
		Vector3 toPath = Flat(_terrain.TrailPoint(s, out _) - stump) is var d2 ? new Vector3(d2.X, 0, d2.Y).Normalized() : Vector3.Back;
		await Stand(stump + toPath * 1.2f, note?.GlobalPosition ?? stump);
		Shot("02b_camp_stump_close");
		await Stand(stump + toPath * 0.6f, stump);
		Shot("02c_camp_stump_top");
	}

	private async Task Key()
	{
		var key = PickupOf(ToolKind.Key);
		Check(key != null && key.Visible, "key: the key lies waiting (checkpoint 3)");
		if (key == null) return;
		Check(TrailD(key.GlobalPosition) < 1.0f, $"key: on the path ({TrailD(key.GlobalPosition):0.00} m from its centreline)");
		Check(StormController.Instance is { Active: true }, "key: the storm is raging (restored)");
		float s = TrailS(key.GlobalPosition);
		await StandOnTrail(s - 3.2f, key.GlobalPosition);
		await Seconds(1.0);
		Shot("03_key_on_path");
		await StandOnTrail(s - 9f, key.GlobalPosition + Vector3.Up * 0.3f);
		Shot("03b_key_from_9m");
	}

	private async Task Giant()
	{
		// The giant left the storm walk (Dan, 2026-09-22): it is seen from the Act 7 lookout, beyond the burning
		// cabin. Here the stalker has the stretch to itself: nothing big may cross on the way to the cabin.
		var cabin = First<Cabin>("cabin");
		var giant = AllOf<GiantStalkerEvent>().FirstOrDefault();
		Check(giant != null && !StoryManager.Instance.GiantEventDone, "giant: event present, not crossed (it waits for the lookout)");
		float sCabin = TrailS(cabin.GlobalPosition);
		await StandOnTrail(sCabin - 100f, cabin.GlobalPosition + Vector3.Up * 3f);
		await Seconds(1.0);
		await StandOnTrail(sCabin - 40f, cabin.GlobalPosition + Vector3.Up * 6f);
		await Seconds(3.0);
		Check(!StoryManager.Instance.GiantEventDone && FindGiant() == null, "giant: nothing crosses on the storm walk, even 40 m from the cabin");
		Shot("04_storm_walk_no_giant");
	}

	private Node3D FindGiant() => All(GetTree().Root).OfType<Node3D>().FirstOrDefault(n => n.Name == "Act7Giant");

	private async Task CabinStop()
	{
		var cabin = First<Cabin>("cabin");
		Check(cabin.DoorBoarded && !cabin.IsOpen, "cabin: boarded shut");
		Check(!StoryManager.Instance.HasFlag(CabinReturnEvent.SeenFlag), "cabin: the boarded-shut thought not seen yet");
		await Stand(cabin.WideApproachPoint, cabin.DoorCenter);
		Shot("05_cabin_boarded");
		await Stand(cabin.ApproachPoint, cabin.DoorCenter);
		await Seconds(1.6);
		Check(StoryManager.Instance.HasFlag(CabinReturnEvent.SeenFlag), "cabin: arriving at the door shows \"Boarded shut. From the outside.\" once (flag saved)");
		Shot("05b_cabin_door_caption");
		// the shed and the axe
		var shed = AllOf<Shed>().FirstOrDefault();
		if (shed != null)
		{
			await Stand(shed.GlobalTransform * new Vector3(0.6f, 0, 4.2f), shed.GlobalPosition + Vector3.Up * 1.0f);
			Shot("07_shed");
		}
		var hammer = PickupOf(ToolKind.Hammer);
		Check(hammer != null, "cabin: the hammer is in the shed");
		var axe = PickupOf(ToolKind.Axe);
		Check(axe != null && axe.Visible, "cabin: the axe waits (checkpoint 3)");
		if (axe != null)
		{
			float s = TrailS(axe.GlobalPosition);
			await StandOnTrail(s, axe.GlobalPosition + Vector3.Up * 0.4f);
			Shot("06_axe_from_path");
			Vector3 toward = Flat(cabin.GlobalPosition - axe.GlobalPosition) is var d ? new Vector3(d.X, 0, d.Y).Normalized() : Vector3.Back;
			await Stand(axe.GlobalPosition + toward * 3.5f, axe.GlobalPosition + Vector3.Up * 0.3f);
			Shot("06b_axe_spot");
		}
	}

	private async Task Inside()
	{
		var cabin = First<Cabin>("cabin");
		Check(cabin.IsOpen, "inside: the door is open (restored)");
		Check(cabin.GetNodeOrNull("Friend") == null && !All(cabin).Any(n => n.Name == "Man"), "inside: no friend, no body");
		var post = PickupOf(ToolKind.NewelPost);
		Check(post != null && post.Visible, "inside: the newel post on the table (checkpoint 4)");
		var things = AllOf<FriendBody>().FirstOrDefault();
		Check(things?.Page != null && things.Page.IsVisibleInTree(), "inside: his page on the table");
		Vector3 eyeSpot = cabin.GlobalTransform * new Vector3(0.1f, 0f, cabin.Depth * 0.5f - 0.6f);
		Vector3 table = cabin.GlobalTransform * new Vector3(0.3f, 0.7f, -1.1f);
		_player.Teleport(eyeSpot + Vector3.Up * 0.1f, 0f);
		await Frames(4);
		var eye = _player.CameraRig.Camera.GlobalPosition;
		Vector3 d = table - eye;
		_player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
		_player.CameraRig.SetPitch(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
		await Seconds(0.6);
		Shot("08_inside_table");
		Vector3 close = cabin.GlobalTransform * new Vector3(0.9f, 0f, -0.2f);
		_player.Teleport(close + Vector3.Up * 0.1f, 0f);
		await Frames(4);
		eye = _player.CameraRig.Camera.GlobalPosition;
		d = cabin.GlobalTransform * new Vector3(0.3f, 0.6f, -1.2f) - eye;
		_player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
		_player.CameraRig.SetPitch(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
		await Seconds(0.4);
		Shot("08b_inside_table_close");
	}

	private async Task Bridge()
	{
		var bridge = First<Node3D>("bridge_marker");
		Check(StoryManager.Instance.HasFlag(StoryManager.Flag.DawnBroke), "bridge: the rain has stopped (restored)");
		float s = TrailS(bridge.GlobalPosition);
		await StandOnTrail(s - 14f, bridge.GlobalPosition + Vector3.Up * 0.8f);
		await Seconds(0.8);
		Shot("09_bridge");
	}

	private async Task Clearing()
	{
		var clearing = First<Node3D>("stairs_clearing_marker");
		var act6 = AllOf<Act6ClearingEvent>().FirstOrDefault();
		Check(act6 is { Revealed: true, MiniStairCount: 15 }, $"clearing: revealed, fifteen small stairs ({act6?.MiniStairCount})");
		int onPath = 0;
		foreach (var n in GetTree().GetNodesInGroup("act6_mini_stairs"))
			if (n is StaircaseBuilder m && TrailD(m.GlobalPosition) < 3.5f) onPath++;
		Check(onPath == 0, $"clearing: none of them on the path ({onPath})");
		float sc = TrailS(clearing.GlobalPosition);
		await Frames(3);
		var hits = ProbePath(sc - 80f, sc + 80f);
		Check(hits.Count == 0, $"clearing: the revealed clearing (fifteen stairs, giant firs) leaves the path free ({hits.Count} hits){(hits.Count > 0 ? ": " + string.Join("; ", hits.Take(8)) : "")}");
		var stairs = act6?.OriginalStairs;
		Check(stairs != null && stairs.Steps == stairs.BaseSteps, $"clearing: its staircase at its own length ({stairs?.Steps})");
		float s = TrailS(clearing.GlobalPosition);
		await StandOnTrail(s - 22f, (stairs?.GlobalPosition ?? clearing.GlobalPosition) + Vector3.Up * 3f);
		await Seconds(1.0);
		Shot("10_clearing_revealed");
		await StandOnTrail(s - 4f, (stairs?.GlobalPosition ?? clearing.GlobalPosition) + Vector3.Up * 2f);
		Shot("10b_clearing_close");
	}

	private async Task Lookout()
	{
		var lookout = First<Node3D>("fire_lookout_marker");
		var cabin = First<Cabin>("cabin");
		var fire = AllOf<CabinFireEvent>().FirstOrDefault();
		Check(cabin.Burning > 0.5f && fire is { Burning: true }, "lookout: the cabin is burning (restored)");
		Check(fire?.Lookout == lookout, "lookout: the fire's checkpoint belongs to the lookout");
		Check(StoryBeat.Atmosphere(this)?.CurrentMood == ForestAtmosphere.Mood.Night, $"lookout: night ({StoryBeat.Atmosphere(this)?.CurrentMood})");
		var respawn = First<Node3D>("respawn_Act7CabinBurning");
		Check(Flat(_player.GlobalPosition).DistanceTo(Flat(lookout.GlobalPosition)) < 3f, "lookout: Continue at checkpoint 6 stands the player at the lookout");
		await Seconds(3.0);
		Vector3 roof = cabin.GlobalPosition + Vector3.Up * 3.5f;
		await Stand(lookout.GlobalPosition, roof);
		// line of sight from the eye to the cabin
		var eye = _player.CameraRig.Camera.GlobalPosition;
		var q = PhysicsRayQueryParameters3D.Create(eye, roof, 1u);
		q.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(q);
		string by = hit.Count == 0 ? "clear" : hit["collider"].AsGodotObject() is Node n ? GetTree().CurrentScene.GetPathTo(n).ToString() : "(scatter body)";
		Check(hit.Count == 0 || by.Contains("Cabin"), $"lookout: clear line of sight to the burning cabin ({Flat(lookout.GlobalPosition).DistanceTo(Flat(cabin.GlobalPosition)):0} m; ray: {by})");
		await Seconds(1.0);
		Shot("11_lookout_cabin_burning");
		// what the respawn looks at, as the player gets it
		if (respawn != null)
		{
			_player.Teleport(Ground(respawn.GlobalPosition) + Vector3.Up * 0.15f, Mathf.Atan2(respawn.GlobalBasis.Z.X, respawn.GlobalBasis.Z.Z));
			await Frames(4);
			_player.CameraRig.SetPitch(Mathf.DegToRad(-6f));
			await Seconds(0.4);
			Shot("11b_lookout_respawn_view");
		}
	}

	private async Task BunkerStop()
	{
		var bunker = First<Bunker>("bunker_marker");
		Check(bunker != null, "bunker: exists");
		if (bunker == null) return;
		Check(!bunker.IsOpen, "bunker: shut until the player comes near");
		float s = TrailS(bunker.GlobalPosition);
		await StandOnTrail(s - 16f, bunker.GlobalPosition + Vector3.Up * 1.5f);
		await Seconds(0.5);
		Shot("12_bunker_approach");
		await Stand(bunker.ApproachPointWorld, bunker.GlobalPosition + Vector3.Up * 1.2f);
		for (int i = 0; i < 90 && !bunker.IsOpen; i++) await Frames(2);
		Check(bunker.IsOpen, "bunker: the door opens for the player (checkpoint 6)");
		await Seconds(1.5);
		Shot("12b_bunker_open");
	}

	private async Task LastStairs()
	{
		var last = First<Node3D>("final_stairs_marker");
		var act11 = AllOf<Act11Ending>().FirstOrDefault();
		Check(act11 is { StairsTall: true }, $"last stairs: tall ({act11?.StairsTall})");
		Check(act11?.CapSeat != null && act11.ApproachWorld != null, "last stairs: the cap's E-point on the landing and the approach point are there");
		var clearingStairs = AllOf<Act6ClearingEvent>().FirstOrDefault()?.OriginalStairs;
		Check(clearingStairs != null && clearingStairs.Steps == clearingStairs.BaseSteps, $"last stairs: the clearing's staircase keeps its own length ({clearingStairs?.Steps})");
		float s = TrailS(last.GlobalPosition);
		await StandOnTrail(s - 30f, last.GlobalPosition + Vector3.Up * 12f);
		await Seconds(1.5);
		Shot("13_last_staircase_tall");
		await StandOnTrail(s - 8f, last.GlobalPosition + new Vector3(0, 16f, -20f));
		Shot("13b_last_staircase_up");
	}

	private async Task Ending()
	{
		var clearing = First<Node3D>("stairs_clearing_marker");
		Check(Flat(_player.GlobalPosition).DistanceTo(Flat(clearing.GlobalPosition)) < 40f, "ending: Continue at checkpoint 9 wakes in the clearing");
		Check(StoryBeat.Atmosphere(this)?.CurrentMood == ForestAtmosphere.Mood.Night, $"ending: still night ({StoryBeat.Atmosphere(this)?.CurrentMood})");
		await Seconds(1.0);
		_player.CameraRig.SetPitch(Mathf.DegToRad(-4f));
		await Seconds(0.3);
		Shot("14_ending_wake");
		// the Act 11 wake spot search: clear of every staircase in the clearing
		var act11 = AllOf<Act11Ending>().FirstOrDefault();
		var m = typeof(Act11Ending).GetMethod("FindSafeWakeSpot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		if (act11 != null && m != null)
		{
			var spot = (Vector3)m.Invoke(act11, null);
			float nearest = 999f;
			foreach (var st in All(clearing).OfType<StaircaseBuilder>().Concat(GetTree().GetNodesInGroup("act6_mini_stairs").OfType<StaircaseBuilder>()))
				nearest = Mathf.Min(nearest, Flat(st.GlobalPosition).DistanceTo(Flat(spot)));
			Check(nearest > 5f, $"ending: the wake spot is clear of the staircases ({spot}, nearest {nearest:0.0} m)");
			await Stand(spot, Ground(spot + Vector3.Forward * 25f) + Vector3.Up * 1.8f);
			Shot("14b_ending_wake_spot");
		}
	}
}
