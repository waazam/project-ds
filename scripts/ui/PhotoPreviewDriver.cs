using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.UI;

/// <summary>
/// Drives photo_preview.tscn; see <see cref="PhotoPreview"/>. Everything goes through
/// PlayerInput.Scripted*. In trail order it stands the player in front of each subject,
/// raises the camera, checks the focus lock, shoots, checks the log and screenshots the
/// viewfinder and the print. Negative checks (too far, behind a trunk, wrong bearing,
/// bird over cabin), the black bird beat, the page, the take-away, and finally a real
/// Continue from a save with three photo flags (the real save slots are backed up and put
/// back). Shots: test-output/photo/ at 640x360 plus the nearest-scaled 1600x900 view.
/// Quits non-zero on any failed check.
/// </summary>
public partial class PhotoPreviewDriver : Node
{
	private string _out;
	private int _fails;
	private PlayerController _player;
	private PlayerInput _pin;
	private PlayerInventory _inv;
	private CameraTool _camera;
	private CameraViewfinder _viewfinder;
	private PhotoLogPage _page;
	private PhotoThumb _thumb;
	private ForestTerrain _terrain;
	private Node3D _world;
	private readonly List<string> _backedUp = new();

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_out = ProjectSettings.GlobalizePath("res://test-output/photo");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s, true), SceneTreeTimer.SignalName.Timeout);
	private void Check(bool ok, string what) { GD.Print($"[photo-preview] {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) _fails++; }
	private static void Note(string what) => GD.Print($"[photo-preview] note: {what}");

	private void Shot(string name)
	{
		var img = GetViewport().GetTexture().GetImage();
		img.SavePng($"{_out}/{name}_640x360.png");
		var big = (Image)img.Duplicate();
		big.Resize(1600, 900, Image.Interpolation.Nearest);
		big.SavePng($"{_out}/{name}_1600x900.png");
		GD.Print($"[photo-preview] shot {name}");
	}

	private bool Bind()
	{
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return false;
		_pin = _player.PlayerInput;
		_inv = _player.GetNode<PlayerInventory>("Inventory");
		_camera = _player.GetNode<CameraTool>("CameraTool");
		_viewfinder = null;
		foreach (var c in _camera.GetChildren()) if (c is CameraViewfinder v) _viewfinder = v;
		var hud = GetTree().CurrentScene.GetNodeOrNull("Hud");
		_page = hud?.GetNodeOrNull<PhotoLogPage>("PhotoLogPage");
		_thumb = hud?.GetNodeOrNull<PhotoThumb>("PhotoThumb");
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		_world = GetTree().CurrentScene.GetNodeOrNull<Node3D>("ForestWorld");
		return _viewfinder != null && _page != null && _thumb != null && _terrain != null && _world != null;
	}

	private Vector3 Ground(Vector3 p) => new(p.X, _terrain.HeightAt(p.X, p.Z) + 0.1f, p.Z);

	private static float YawToward(Vector3 from, Vector3 to)
	{
		Vector3 d = to - from;
		return Mathf.Atan2(-d.X, -d.Z);
	}

	/// <summary>Stand at a point and aim the camera at another (yaw from the stand point, pitch from the eye).</summary>
	private async Task Stand(Vector3 at, Vector3 lookAt)
	{
		_player.Teleport(at, YawToward(at, lookAt));
		await Frames(4);
		var eye = _player.CameraRig.Camera.GlobalPosition;
		Vector3 d = lookAt - eye;
		float flat = new Vector2(d.X, d.Z).Length();
		_player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
		_player.CameraRig.SetPitch(Mathf.Atan2(d.Y, flat));
		await Frames(2);
	}

	private async Task StandFacing(Vector3 at, float yawDeg, float pitchDeg)
	{
		_player.Teleport(at, Mathf.DegToRad(yawDeg));
		await Frames(4);
		_player.CameraRig.SnapBehind(Mathf.DegToRad(yawDeg));
		_player.CameraRig.SetPitch(Mathf.DegToRad(pitchDeg));
		await Frames(2);
	}

	private async Task Raise(bool up)
	{
		_pin.ScriptedFocus = up;
		await Seconds(0.4);
	}

	/// <summary>A scripted button held for exactly one processed frame. Timer continuations run after the
	/// nodes' _Process, so the button must stay down across the next full frame to be seen once.</summary>
	private async Task Press(System.Action<bool> set)
	{
		set(true);
		await Frames(2);
		set(false);
	}

	/// <summary>Raise, check the lock, shoot, check the record, screenshot both.</summary>
	private async Task Shoot(string tag, string expectId, bool? expectLock, bool expectRecord)
	{
		await Raise(true);
		if (expectLock is bool lockOn) Check(_viewfinder.FocusLocked == lockOn, $"{tag}: focus lock {(lockOn ? "on" : "off")}");
		Shot($"viewfinder_{tag}");
		int film = _camera.FramesLeft;
		await Press(v => _pin.ScriptedPhoto = v);
		await Seconds(0.9);
		Check(_camera.FramesLeft == film - 1, $"{tag}: one frame of film spent");
		bool has = expectId != null && PhotoLog.Instance.Has(expectId);
		Check(has == expectRecord, $"{tag}: {(expectRecord ? "recorded" : "not recorded")} {expectId}");
		if (expectRecord) Check(_thumb.ShowingId == expectId, $"{tag}: print showing ({_thumb.ShowingId ?? "none"})");
		Shot($"print_{tag}");
		await Raise(false);
		await Seconds(2.2);
	}

	private async Task PulseTab()
	{
		await Press(v => _pin.ScriptedPhotoLog = v);
		await Seconds(0.25);
	}

	private async void Run()
	{
		try
		{
			await Frames(60);
			if (!Bind()) { GD.PushError("[photo-preview] scene is missing something (player, hud page/thumb, terrain, world)"); GetTree().Quit(2); return; }
			(GetTree().CurrentScene.FindChild("ScreenFader", true, false) as ScreenFader)?.SetBlack(false);
			Input.MouseMode = Input.MouseModeEnum.Visible;
			_inv.TryPickup(ToolKind.Camera);
			_pin.Scripted = true;
			await Frames(20);
			var log = PhotoLog.Instance;
			Check(log != null && log.RecordedCount == 0, "fresh log is empty");
			Check(_camera.FramesLeft == 36, $"fresh roll has 36 frames ({_camera.FramesLeft})");
			CheckAct5ToolsHidden();

			// ---- the sign: too far first, then from 6 m
			var sign = _world.GetNode<Node3D>("Trailhead/TrailheadSign");
			Vector3 plank = sign.GlobalTransform * new Vector3(0, 1.62f, 0.13f);
			await Stand(Ground(sign.GlobalTransform * new Vector3(0, 0, 30)), plank);
			await Raise(true);
			Check(!_viewfinder.FocusLocked, "sign from 30 m does not lock");
			await Raise(false);
			await Stand(Ground(sign.GlobalTransform * new Vector3(0, 0, 6)), plank);
			await Shoot("sign", "trailhead_sign", true, true);

			// ---- the red bird with the cabin right behind it: the bird wins
			var bird1 = _world.GetNode<Bird>("Bird1");
			var cabin = _world.GetNode<Cabin>("Cabin");
			Vector3 b = bird1.GlobalPosition;
			Vector3 away = b - cabin.DoorCenter; away.Y = 0; away = away.Normalized();
			// Aim a little above the bird so the door behind it sits near the centre too (the bird's cone is 22 deg).
			await Stand(Ground(b + away * 6f), b + Vector3.Up * 0.8f);
			var cabinSubject = cabin.GetNode<PhotoSubject>("PhotoSubject");
			bool cabinInFrame = cabinSubject.TryScore(_player.CameraRig.Camera, _player, out _);
			{
				var cam = _player.CameraRig.Camera;
				Vector3 door = cabin.DoorCenter;
				Vector3 to = door - cam.GlobalPosition;
				float ang = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp((-cam.GlobalBasis.Z).Dot(to.Normalized()), -1f, 1f)));
				var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, door, 1u);
				q.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
				var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(q);
				string blocker = hit.Count == 0 ? "clear" : (hit["collider"].AsGodotObject() as Node)?.GetPath().ToString();
				Note($"cabin in frame behind the red bird: {cabinInFrame} (door {to.Length():0.0} m, {ang:0.0} deg, ray {blocker})");
			}
			await Shoot("bird_red", "bird_red", true, true);
			Check(!log.Has("cabin"), "bird over cabin: the cabin was not recorded");
			Check(bird1.Photographed, "red bird captured");

			// ---- the cabin from its front
			await Stand(Ground(cabin.GlobalTransform * new Vector3(0, 0, 12)), cabin.GlobalTransform * new Vector3(0, 1.0f, 2.6f));
			await Shoot("cabin", "cabin", true, true);

			// ---- the creek from the deck
			var bridge = _world.GetNode<Node3D>("Footbridge");
			await Stand(bridge.GlobalTransform * new Vector3(0, 0.35f, 0), bridge.GlobalTransform * new Vector3(6, -0.9f, 0));
			await Shoot("creek", "creek", true, true);

			// ---- the overlook: south first (nothing), then the view
			var landing = _world.GetNode<Node3D>("Overlook/Landing");
			await StandFacing(Ground(landing.GlobalPosition), 180f, 0f);
			await Shoot("overlook_south", "overlook", false, false);
			await StandFacing(Ground(landing.GlobalPosition), 37f, 2f);
			await Shoot("overlook", "overlook", true, true);

			// ---- the tent: behind a trunk first (if one can be found), then from the door side
			var tent = _world.GetNode<Node3D>("Props/ForgottenTent");
			Vector3 tentLook = tent.GlobalTransform * new Vector3(-1f, 0.9f, 0);
			Vector3? hidden = FindHiddenSpot(tent, tentLook);
			if (hidden is Vector3 hs)
			{
				await Stand(hs, tentLook);
				await Raise(true);
				Check(!_viewfinder.FocusLocked, "tent behind a trunk does not lock");
				Shot("viewfinder_tent_hidden");
				await Raise(false);
			}
			else Note("no trunk between a stand point and the tent: hidden-tent check skipped");
			await Stand(Ground(tent.GlobalTransform * new Vector3(-4.5f, 0, 3f)), tentLook);
			await Shoot("tent", "tent", true, true);

			// ---- the mushrooms, close and looking down
			var mush = _world.GetNode<Node3D>("Props/Mushrooms");
			await Stand(Ground(mush.GlobalTransform * new Vector3(0, 0, 1.8f)), mush.GlobalTransform * new Vector3(0, 0.12f, 0));
			await Shoot("mushrooms", "mushrooms", true, true);

			// ---- the black bird: the scream, the flock gone, the listed blue and purple never ticked
			var omen = _world.GetNode<Bird>("Bird4Omen");
			Vector3 o = omen.GlobalPosition;
			Vector3 tp = _terrain.TrailPoint(415f, out _);
			Vector3 toward = tp - o; toward.Y = 0; toward = toward.Normalized();
			await Stand(Ground(o + toward * 6f), o + Vector3.Up * 0.1f);
			await Shoot("bird_black", "bird_black", true, true);
			bool allGone = true;
			foreach (var n in new[] { "Bird1", "Bird2", "Bird3", "Bird4Omen" })
				if (_world.GetNodeOrNull<Bird>(n) is { } bd && !bd.Photographed) allGone = false;
			Check(allGone, "after the black bird every bird is Photographed");
			Check(!log.Has("bird_blue") && !log.Has("bird_purple"), "unshot listed birds stay unticked");
			Check(GetTree().GetNodesInGroup("photo_birds").Count == 0, "photo_birds group is empty");

			// ---- the stalker's sting writes "(nothing there)"
			var stalker = GetTree().GetFirstNodeInGroup("stalker") as Entities.Stalker;
			if (stalker != null)
			{
				// Facing up the trail (every bird is gone by now, every prop here is already on the roll), at
				// whichever of a few spots offers it a trunk behind us. Camera already up, then it takes
				// cover; a fast turn and the shutter inside its linger (it vanishes a third of a second
				// after being seen, and the sting needs it in frame).
				bool placed = false;
				foreach (float s in new[] { 330f, 250f, 360f, 300f, 200f })
				{
					Vector3 sp = _terrain.TrailPoint(s, out Vector3 st);
					await StandFacing(Ground(sp), Mathf.RadToDeg(Mathf.Atan2(-st.X, -st.Z)), 0f);
					await Raise(true);
					placed = stalker.DebugForcePeek();
					await Seconds(0.3);
					if (placed) { Note($"stalker took cover at s = {s}"); break; }
					await Raise(false);
				}
				if (placed)
				{
					Vector3 centre = Vector3.Zero; int k = 0;
					foreach (var p in stalker.WorldSamplePoints()) { centre += p; k++; }
					if (k > 0) centre /= k;
					var eye = _player.CameraRig.Camera.GlobalPosition;
					Vector3 d = centre - eye;
					_player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
					_player.CameraRig.SetPitch(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
					await Frames(1);
					int film = _camera.FramesLeft;
					await Press(v => _pin.ScriptedPhoto = v);
					await Seconds(0.9);
					Check(_camera.FramesLeft == film - 1, "stalker: one frame of film spent");
					Check(log.Has("stalker"), $"stalker: recorded (stings {stalker.StingCount}, state {stalker.Current})");
					Check(_thumb.ShowingId == "stalker", $"stalker: print showing ({_thumb.ShowingId ?? "none"})");
					Shot("print_stalker");
					await Raise(false);
					await Seconds(2.2);
				}
				else Note("stalker found no cover at any spot: stalker check skipped");
			}
			else Note("no stalker in the scene");

			// ---- the stairs from the clearing (never stepping on)
			var stairs = _world.GetNode<Node3D>("Clearing/Stairs");
			await Stand(Ground(stairs.GlobalTransform * new Vector3(0, 0, 9f)), stairs.GlobalTransform * new Vector3(0, 3.0f, -5.5f));
			await Shoot("stairs", "stairs", true, true);

			// ---- the page
			await PulseTab();
			Check(_page.IsOpen, "Tab opens the page");
			Shot("page_open");
			await PulseTab();
			Check(!_page.IsOpen, "Tab again closes the page");
			_thumb.ShowPrints = false;
			log.Record("bird_blue");
			log.Record("bird_purple");
			Check(log.ListComplete, "list complete after the last two records");
			await PulseTab();
			await Seconds(0.2);
			Shot("page_complete");
			await PulseTab();
			await Raise(true);
			await PulseTab();
			Check(!_page.IsOpen, "Tab is ignored with the camera raised");
			await Raise(false);
			await PulseTab();
			Check(_page.IsOpen, "page opens again once the camera is down");
			await Raise(true);
			Check(!_page.IsOpen, "raising the camera puts the page away");
			await Raise(false);

			// ---- the stairs take the camera
			_inv.TakeAwayCamera();
			await Frames(5);
			Check(!_page.IsOpen, "take-away: page hidden");
			await Raise(true);
			Check(!_camera.IsRaised, "take-away: focus never raises");
			await Raise(false);
			await PulseTab();
			Check(!_page.IsOpen, "take-away: Tab does nothing");
			Shot("after_stairs");

			// ---- Continue from a save with three photos
			await ContinueCheck();
		}
		catch (System.Exception e)
		{
			Check(false, "ran without an exception: " + e);
		}
		finally
		{
			RestoreSlots();
		}
		GD.Print($"[photo-preview] done, {_fails} failure(s)");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	/// <summary>Fix 1.9: the Act 5 tools (axe, key, hammer) stay hidden until the door is boarded (Act 3).</summary>
	private void CheckAct5ToolsHidden()
	{
		foreach (var path in new[] { "AxePickup", "KeyPickup", "Shed/HammerPickup" })
		{
			var p = _world.GetNodeOrNull<Pickup>(path);
			Check(p != null && !p.Visible && p.RequiredCheckpoint == Checkpoint.Act3DoorBoarded, $"before Act 3: {path} hidden (required checkpoint {p?.RequiredCheckpoint})");
		}
	}

	/// <summary>A stand point 7-9 m from the tent whose line to the look point is blocked by something that is not the tent.</summary>
	private Vector3? FindHiddenSpot(Node3D tent, Vector3 look)
	{
		var space = _player.GetWorld3D().DirectSpaceState;
		foreach (float r in new[] { 7f, 9f, 11f })
			for (int i = 0; i < 24; i++)
			{
				float a = Mathf.Tau * i / 24f;
				Vector3 at = Ground(tent.GlobalPosition + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
				Vector3 eye = at + Vector3.Up * 1.55f;
				var q = PhysicsRayQueryParameters3D.Create(eye, look, 1u);
				var hit = space.IntersectRay(q);
				if (hit.Count == 0) continue;
				// Scattered trunks are PhysicsServer bodies with no node (collider null): exactly the cover wanted.
				var col = hit["collider"].AsGodotObject() as Node;
				if (col != null && (col == tent || tent.IsAncestorOf(col))) continue;
				// something else is in the way, and the spot itself must be clear of it (a metre off the trunk)
				if (((Vector3)hit["position"]).DistanceTo(eye) < 1.0f) continue;
				return at;
			}
		return null;
	}

	private async Task ContinueCheck()
	{
		BackUpSlots();
		_continueStarted = true;
		var spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
		Vector3 sp = spawn?.GlobalPosition ?? Vector3.Zero;
		SaveSystem.Save(new SaveData
		{
			Checkpoint = Checkpoint.Act1Start,
			PosX = sp.X, PosY = sp.Y, PosZ = sp.Z, Yaw = 0f,
			// The red bird shot, the stalker caught; the black bird NOT shot, so the other three birds must still be there.
			Flags = new[] { StoryManager.Flag.PickupTakenCamera, StoryManager.Flag.Photo("trailhead_sign"), StoryManager.Flag.Photo("bird_red"), StoryManager.Flag.Photo("stalker") },
			Inventory = "camera;tools=",
		});
		var before = GetTree().CurrentScene;
		Check(StoryManager.Instance.ContinueGame(), "ContinueGame accepted the save");
		for (int i = 0; i < 600 && GetTree().CurrentScene == before; i++) await Frames(1);
		await Seconds(4.0);
		Check(Bind(), "continued level has the player, hud, terrain");
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_pin.Scripted = true;
		var log = PhotoLog.Instance;
		Check(log != null && log.RecordedCount == 3, $"continue: 3 photos restored ({log?.RecordedCount})");
		Check(_inv.HasCamera, "continue: camera in hand");
		Check(_camera.FramesLeft == 33, $"continue: film is 33 ({_camera.FramesLeft})");
		CheckAct5ToolsHidden();
		foreach (var (name, gone) in new[] { ("Bird1", true), ("Bird2", false), ("Bird3", false), ("Bird4Omen", false) })
		{
			var bd = _world.GetNodeOrNull<Bird>(name);
			bool hasModel = bd != null && bd.GetNodeOrNull("Model") != null;
			bool inGroup = bd != null && bd.IsInGroup("photo_birds");
			Check(bd != null && hasModel == !gone && inGroup == !gone, $"continue: {name} {(gone ? "gone (no model, out of the group)" : "present")}");
			if (gone) Check(bd != null && bd.GetNodeOrNull("Perch") != null, $"continue: {name}'s perch stays");
		}
		var stairsClimbedLater = _world.GetNodeOrNull<Bird>("Bird2");
		Check(stairsClimbedLater != null && !stairsClimbedLater.Gone, "continue at Act 1: birds are not hidden by the Act 2 rule");
		await PulseTab();
		Check(_page.IsOpen, "continue: page opens");
		Check(log.ListedRecorded == 2 && log.RecordedCount - log.ListedRecorded == 1, "continue: two ticks and one unlisted line");
		Shot("continue_page");
		await PulseTab();

		// Fresh play of the Act 2 rule: reaching the checkpoint hides the remaining birds (the slots are restored afterwards).
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act2StairsClimbed, _player.GlobalPosition, 0f);
		await Frames(3);
		foreach (var name in new[] { "Bird2", "Bird3", "Bird4Omen" })
		{
			var bd = _world.GetNodeOrNull<Bird>(name);
			Check(bd != null && bd.Gone && bd.GetNodeOrNull("Model") == null && !bd.IsInGroup("photo_birds"), $"act 2 reached: {name} hidden");
			Check(bd != null && bd.GetNodeOrNull("Perch") != null, $"act 2 reached: {name}'s perch stays");
		}
	}

	private static string UserFile(string name) => ProjectSettings.GlobalizePath("user://" + name);

	private void BackUpSlots()
	{
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
		{
			string src = UserFile(slot);
			if (!FileAccess.FileExists(src)) continue;
			var err = DirAccess.CopyAbsolute(src, UserFile("photo_preview_backup_" + slot));
			if (err == Error.Ok) _backedUp.Add(slot);
			else GD.PushError($"[photo-preview] could not back up {slot}: {err}");
		}
		GD.Print($"[photo-preview] backed up {_backedUp.Count} save slot(s)");
	}

	private void RestoreSlots()
	{
		if (!_continueStarted) return;
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
		{
			string dst = UserFile(slot), bak = UserFile("photo_preview_backup_" + slot);
			if (_backedUp.Contains(slot) && FileAccess.FileExists(bak))
			{
				DirAccess.CopyAbsolute(bak, dst);
				DirAccess.RemoveAbsolute(bak);
			}
			else if (FileAccess.FileExists(dst)) DirAccess.RemoveAbsolute(dst);
		}
		GD.Print("[photo-preview] save slots put back");
	}

	private bool _continueStarted;
}
