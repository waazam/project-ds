using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.UI;

/// <summary>
/// Drives photo_preview.tscn; see <see cref="PhotoPreview"/>. The Act 1 (trailhead level) harness,
/// everything through PlayerInput.Scripted*:
/// 1. the level holds Act 1 only (nothing of the cabin, shed, bunker or later acts);
/// 2. the open-trail grade, before (the old values) and after, at the lot, the trailhead, 100 m
///    and past the bridge (test-output/trailhead/atmo_*);
/// 3. look shots of the new dressing (signs, papers, pavers, the barrier left of the fallen fir,
///    deer, frog, flowers, waterfall, stone) into test-output/trailhead/;
/// 4. every readable: reachable, highlighted, opens the note overlay with the right text;
/// 5. the fallen fir: walking left of the root plate is stopped; the pavers lead round the crown
///    to the stairs' foot;
/// 6. the photo trip in trail order: each subject locks, shoots, records, prints; the deer bolts
///    at the shutter and is gone; the black bird beat; the stairs; the page;
///    the take-away (test-output/photo/);
/// 7. a real Continue from a save with three photo flags (the real save slots are backed up and put
///    back): restore checks, the deer bolting when walked up to, and the first climb (the log line
///    "[story] checkpoint reached: Act2StairsClimbed" and the travel to the Hollow).
/// Quits non-zero on any failed check.
/// </summary>
public partial class PhotoPreviewDriver : Node
{
	private string _out, _outTh;
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
		_outTh = ProjectSettings.GlobalizePath("res://test-output/trailhead");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		DirAccess.MakeDirRecursiveAbsolute(_outTh);
		Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Physics(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s, true), SceneTreeTimer.SignalName.Timeout);
	private void Check(bool ok, string what) { GD.Print($"[photo-preview] {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) _fails++; }
	private static void Note(string what) => GD.Print($"[photo-preview] note: {what}");

	private void Shot(string name) => Save(_out, name, true);
	private void ShotTh(string name) => Save(_outTh, name, false);

	private void Save(string dir, string name, bool big)
	{
		var img = GetViewport().GetTexture().GetImage();
		img.SavePng($"{dir}/{name}_640x360.png");
		if (big)
		{
			var b = (Image)img.Duplicate();
			b.Resize(1600, 900, Image.Interpolation.Nearest);
			b.SavePng($"{dir}/{name}_1600x900.png");
		}
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
	private Vector3 TrailNear(Vector3 p) { _terrain.TrailDistance(p.X, p.Z, out float s); return Ground(_terrain.TrailPoint(s, out _)); }

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
		Aim(lookAt);
		await Frames(2);
	}

	private void Aim(Vector3 lookAt)
	{
		var eye = _player.CameraRig.Camera.GlobalPosition;
		Vector3 d = lookAt - eye;
		float flat = new Vector2(d.X, d.Z).Length();
		_player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
		_player.CameraRig.SetPitch(Mathf.Atan2(d.Y, flat));
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
	private async Task Shoot(string tag, string expectId, bool? expectLock, bool expectRecord, double settle = 2.2)
	{
		await Raise(true);
		if (expectLock is bool lockOn) Check(_viewfinder.FocusLocked == lockOn, $"{tag}: focus lock {(lockOn ? "on" : "off")}");
		Shot($"viewfinder_{tag}");
		int film = _camera.FramesLeft;
		int before = PhotoLog.Instance.RecordedCount;
		await Press(v => _pin.ScriptedPhoto = v);
		await Seconds(0.9);
		Check(_camera.FramesLeft == film - 1, $"{tag}: one frame of film spent");
		Check(PhotoLog.Instance.RecordedCount == before + 1, $"{tag}: the picture is in the album (#{PhotoLog.Instance.RecordedCount})");
		// (quick runs of shots queue their prints; only a lone shot is checked for its own print)
		if (settle >= 1.0) Check(_thumb.ShowingNumber == before + 1, $"{tag}: its print slides in (#{_thumb.ShowingNumber})");
		if (expectId != null)
		{
			bool has = PhotoLog.Instance.Has(expectId);
			Check(has == expectRecord, $"{tag}: {(expectRecord ? "recognised" : "not recognised")} {expectId}");
			if (expectRecord) Check(_thumb.ShowingId == expectId, $"{tag}: the print is captioned '{PhotoLog.CaptionFor(expectId)}' ({_thumb.ShowingId})");
		}
		Shot($"print_{tag}");
		await Raise(false);
		await Seconds(settle);
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
			_pin.Scripted = true;
			await Frames(20);
			var log = PhotoLog.Instance;
			Check(log != null && log.RecordedCount == 0, "fresh log is empty");
			CheckAct1Only();

			await Atmosphere();
			var only = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--only="))?.Substring(7);
			if (only == "atmo")
			{
				var env = _world.GetNode<WorldEnvironment>("WorldEnvironment").Environment;
				await Stand(Ground(_terrain.TrailPoint(4f, out _)), _terrain.TrailPoint(40f, out _) + Vector3.Up * 1.4f);
				await Seconds(6.0);
				env.FogEnabled = false; await Frames(3); ShotTh("diag_nofog");
				env.FogEnabled = true; env.ReflectedLightSource = Environment.ReflectionSource.Disabled; await Frames(3); ShotTh("diag_noreflect");
				GetTree().Quit(_fails == 0 ? 0 : 1); return;
			}
			if (only == "look") { await LookShots(); GetTree().Quit(_fails == 0 ? 0 : 1); return; }
			await LookShots();
			await Readables();
			await FallenFir();

			_inv.TryPickup(ToolKind.Camera);
			await Frames(10);
			Check(_camera.FramesLeft == 36, $"fresh roll has 36 frames ({_camera.FramesLeft})");
			await PhotoTrip(log);

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

	// ------------------------------------------------------------------ 1. Act 1 only

	private void CheckAct1Only()
	{
		Check(_world.GetNodeOrNull("ForkSign_B") == null, "no sign at the fallen fir");
		Check(_world.GetNodeOrNull("CameraPickup") == null, "no camera on the ground: it comes from the car");
		Check(_world.GetNodeOrNull("Trailhead/Car/OpeningAtCar/Stand") != null && _world.GetNodeOrNull("Trailhead/Car/OpeningAtCar/TrunkCamera") != null, "the car, its opening, the stand and the trunk camera");
		foreach (var path in new[] { "Cabin", "Shed", "KeyPickup", "AxePickup", "Bunker", "BunkerInterior", "Act7Whispers", "ForestVoiceCues",
			"Clearing/Stairs/Dressing", "Clearing/Stairs/Act6Clearing", "Clearing/Stairs/Act11Ending" })
			Check(_world.GetNodeOrNull(path) == null, $"act 1 only: no {path}");
		Check(!_world.GetNode("Clearing").IsInGroup("stairs_clearing_marker"), "act 1 only: the clearing is not a compass marker");
		Check(_world.GetNodeOrNull("Clearing/Stairs/FirstClimbTrigger") != null, "act 1 only: the first climb trigger is there");
		Check(GetTree().CurrentScene.GetNodeOrNull("GiantStalkerEvent") == null, "act 1 only: no giant event");
		Check(GetTree().GetFirstNodeInGroup("stalker") == null, "act 1 only: no stalker");
		Check(PhotoLog.CaptionFor("stalker") == "", "no stalker picture caption");
	}

	// ------------------------------------------------------------------ 2. atmosphere

	private async Task Atmosphere()
	{
		var atmo = _world.GetNode<ForestAtmosphere>("Atmosphere");
		atmo.OpenSmoothing = 0.1f;
		var spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
		float bridgeS = 189f;
		if (_terrain.TryGetStreamCrossing(out _, out _, out float cs)) bridgeS = cs;
		var views = new List<(string name, Vector3 at, Vector3 look)>
		{
			("01_lot", Ground(spawn.GlobalPosition + new Vector3(2f, 0, 12f)), _terrain.TrailPoint(8f, out _) + Vector3.Up * 1.5f),
			("02_trailhead", Ground(_terrain.TrailPoint(4f, out _)), _terrain.TrailPoint(40f, out _) + Vector3.Up * 1.4f),
			("03_100m", Ground(_terrain.TrailPoint(100f, out _)), _terrain.TrailPoint(135f, out _) + Vector3.Up * 1.4f),
			("04_past_bridge", Ground(_terrain.TrailPoint(bridgeS + 25f, out _)), _terrain.TrailPoint(bridgeS + 60f, out _) + Vector3.Up * 1.4f),
		};
		// "before": the old, subtle grade values
		var after = (atmo.OpenSunScale, atmo.OpenSunColor, atmo.OpenFogDensityScale, atmo.OpenFogColor, atmo.OpenAmbientScale, atmo.OpenSkyBoost, atmo.OpenExposureBoost, atmo.OpenHoldMeters);
		foreach (bool now in new[] { false, true })
		{
			if (!now)
			{
				atmo.OpenSunScale = 1.5f; atmo.OpenSunColor = new Color(1f, 0.92f, 0.72f); atmo.OpenFogDensityScale = 0.4f;
				atmo.OpenFogColor = new Color(0.47f, 0.48f, 0.5f); atmo.OpenAmbientScale = 1.2f; atmo.OpenSkyBoost = 0.3f;
				atmo.OpenExposureBoost = 0f; atmo.OpenHoldMeters = 40f; atmo.OpenWarmAmbientAndSky = false;
			}
			else
			{
				(atmo.OpenSunScale, atmo.OpenSunColor, atmo.OpenFogDensityScale, atmo.OpenFogColor, atmo.OpenAmbientScale, atmo.OpenSkyBoost, atmo.OpenExposureBoost, atmo.OpenHoldMeters) = after;
				atmo.OpenWarmAmbientAndSky = true;
			}
			foreach (var (name, at, look) in views)
			{
				await Stand(at, look);
				await Seconds(1.2);
				ShotTh($"atmo_{name}_{(now ? "after" : "before")}");
				var env = _world.GetNode<WorldEnvironment>("WorldEnvironment").Environment;
				var sun = _world.GetNode<DirectionalLight3D>("Sun");
				Note($"atmo {name} {(now ? "after" : "before")}: open {atmo.OpenAmount:0.00} fog {env.FogDensity:0.0000} {env.FogLightColor} amb {env.AmbientLightEnergy:0.00} {env.AmbientLightColor} sun {sun.LightEnergy:0.00} dir {-sun.GlobalBasis.Z} exp {env.TonemapExposure:0.00}");
				if (now && name == "02_trailhead") Check(atmo.OpenAmount > 0.95f, $"trailhead fully open ({atmo.OpenAmount:0.00})");
				if (now && name == "04_past_bridge") Check(atmo.OpenAmount < 0.05f, $"past the bridge the grade is gone ({atmo.OpenAmount:0.00})");
			}
		}
		atmo.OpenSmoothing = 2f;
	}

	// ------------------------------------------------------------------ 3. look shots

	private async Task LookShots()
	{
		Node3D N(string p) => _world.GetNodeOrNull<Node3D>(p);
		Vector3 Front(Node3D n, float dist, float side = 0f)
		{
			Vector3 f = n.GlobalBasis.Z; f.Y = 0; f = f.Normalized();
			Vector3 r = n.GlobalBasis.X; r.Y = 0; r = r.Normalized();
			return Ground(n.GlobalPosition + f * dist + r * side);
		}
		async Task Look(string name, Vector3 at, Vector3 look) { await Stand(at, look); await Seconds(0.6); ShotTh(name); }

		var atmo = _world.GetNode<ForestAtmosphere>("Atmosphere");
		atmo.OpenSmoothing = 0.1f;   // the harness teleports: let the grade settle at once
		var sign = N("Trailhead/TrailheadSign");
		await Look("10_trailhead_sign", Front(sign, 5f, 0.4f), sign.GlobalPosition + Vector3.Up * 1.6f);
		var board = N("Trailhead/InfoBoard");
		await Look("11_info_board", Front(board, 2.6f), board.GlobalPosition + Vector3.Up * 1.45f);
		var reg = N("Trailhead/TrailRegister");
		await Look("12_register", Front(reg, 1.5f, 0.2f), reg.GlobalPosition + Vector3.Up * 1.05f);
		var can = N("Trailhead/TrashCan");
		Vector3 broch = can.GlobalTransform * new Vector3(0.62f, 0, 0.42f);
		await Look("13_brochure", Ground(broch + (can.GlobalBasis.Z + can.GlobalBasis.X).Normalized() * 1.4f), broch);
		var forkA = N("ForkSign_A");
		await Look("14_fork_a", Front(forkA, 4f, -0.5f), forkA.GlobalPosition + Vector3.Up * 1.7f);
		_terrain.TrailDistance(forkA.GlobalPosition.X, forkA.GlobalPosition.Z, out float forkS);
		await Look("15_fork_a_approach", Ground(_terrain.TrailPoint(forkS - 14f, out _)), _terrain.TrailPoint(forkS + 4f, out _) + Vector3.Up * 1.2f);

		var flowers = N("Props/Wildflowers");
		await Look("16_wildflowers_from_trail", TrailNear(flowers.GlobalPosition), flowers.GlobalPosition + Vector3.Up * 0.2f);
		await Look("16b_wildflowers_close", Ground(flowers.GlobalPosition + (TrailNear(flowers.GlobalPosition) - flowers.GlobalPosition).Normalized() * 2.2f), flowers.GlobalPosition);

		var deer = N("Deer");
		await Look("17_deer_from_trail", TrailNear(deer.GlobalPosition), deer.GlobalPosition + Vector3.Up * 0.8f);

		var frog = N("Frog");
		Vector3 fp = frog.GlobalPosition + Vector3.Up * 0.1f;
		await Look("18_frog_bank", Ground(frog.GlobalPosition + frog.GlobalBasis.Z * 1.4f), fp);
		var fall = N("Waterfall") as Waterfall;
		var bridge = N("Footbridge");
		await Look("19_waterfall_from_deck", bridge.GlobalPosition + Vector3.Up * 0.35f, fall.CurtainCenter);
		Vector3 bank = fall.CurtainCenter + (bridge.GlobalPosition - fall.CurtainCenter).Normalized() * 9f;
		await Look("19b_waterfall_bank", Ground(bank), fall.CurtainCenter);

		var stone = N("Props/WeirdStone");
		await Look("20_stone_from_trail", TrailNear(stone.GlobalPosition), stone.GlobalPosition + Vector3.Up * 1.3f);
		await Look("20b_stone_close", Ground(stone.GlobalPosition + stone.GlobalBasis.Z * 4.5f), stone.GlobalPosition + Vector3.Up * 1.4f);

		// the end of the trail, the fir, the sign, the barrier and the stones
		var fir = N("FallenTree") as FallenTree;
		Vector3 root = new(fir.Root.X, 0, fir.Root.Y), tip = new(fir.Tip.X, 0, fir.Tip.Y);
		float len = _terrain.TrailLength;
		Vector3 end = Ground(_terrain.TrailPoint(len - 9f, out _));
		await Look("21_trail_end_fir", end, Ground((root + tip) * 0.5f) + Vector3.Up * 1.0f);
		await Look("23_left_of_root_plate", Ground(_terrain.TrailPoint(len - 3f, out _)), Ground(root + new Vector3(-12f, 0, 2f)) + Vector3.Up * 1.0f);
		await Look("23b_barrier_from_the_side", Ground(root + new Vector3(-14f, 0, 9f)), Ground(root + new Vector3(-14f, 0, 0f)) + Vector3.Up * 1.0f);
		var trail = N("FriendTrail") as FriendTrail;
		Vector3 P(float s) => Ground(new Vector3(trail.At(s, out _).X, 0, trail.At(s, out _).Y));
		await Look("24_round_the_crown", Ground(_terrain.TrailPoint(len - 2f, out _)) + new Vector3(1.5f, 0, 0), P(12f) + Vector3.Up * 0.5f);
		await Look("25_pavers_past_tree", P(16f), P(30f));
		await Look("26_pavers_midway", P(60f), P(75f));
		atmo.OpenSmoothing = 2f;
		await Look("27_pavers_to_stairs", P(trail.Length - 16f), _world.GetNode<Node3D>("Clearing/Stairs").GlobalPosition + Vector3.Up * 1.5f);
	}

	// ------------------------------------------------------------------ 4. readables

	private async Task Readables()
	{
		var readables = new List<Readable>();
		foreach (var n in GetTree().GetNodesInGroup("interactables"))
			if (n is Readable r && _world.IsAncestorOf(r)) readables.Add(r);
		Check(readables.Count == 4, $"four readables in the level ({readables.Count})");
		int i = 0;
		foreach (var r in readables)
		{
			var paper = r.GetParent<Node3D>();
			Vector3 c = paper.GlobalPosition;
			Vector3 nrm = paper.GlobalBasis.Z.Normalized();
			Vector3 at;
			if (Mathf.Abs(nrm.Y) > 0.7f)
			{
				// lying flat: stand a metre off it, toward the trail
				Vector3 toward = TrailNear(c) - c; toward.Y = 0;
				if (toward.LengthSquared() < 0.25f) toward = Vector3.Back;
				at = Ground(c + toward.Normalized() * 1.1f);
			}
			else
			{
				Vector3 f = new Vector3(nrm.X, 0, nrm.Z).Normalized();
				at = Ground(c + f * 1.3f);
			}
			await Stand(at, c);
			await Physics(4);
			var focused = _player.Interaction?.Focused;
			Check(focused == r, $"readable '{r.Prompt}' ({r.Title}{Short(r.Text)}): focused from {at.DistanceTo(c):0.0} m ({focused?.Name ?? "nothing"})");
			ShotTh($"30_readable_{i}_focus");
			await Press(v => _pin.ScriptedInteract = v);
			await Seconds(0.4);
			var overlay = NoteOverlay.Instance;
			Check(overlay != null && overlay.IsOpen && overlay.Current == r, $"readable {i}: overlay open");
			ShotTh($"30_readable_{i}_open");
			// E puts it down (the overlay reads the real input event, not the scripted flag)
			Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = true });
			await Frames(2);
			Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = false });
			await Seconds(0.4);
			Check(overlay != null && !overlay.IsOpen, $"readable {i}: overlay closed again");
			i++;
		}
		Check(!readables.Any(r => r.ReadFlag == "read_friends_note"), "no friend's note at the trailhead (R.H. is a stranger)");
		Check(readables.Any(r => r.Text.Contains("R.H.  the steps")), "the trail register carries R.H.'s last entry");
		Check(!readables.Any(r => r.Text.Contains("Cullen")), "nothing names the park's old name");
	}

	private static string Short(string t) { t = t.Replace("\n", " "); return t.Length > 28 ? " " + t[..28] + "..." : " " + t; }

	// ------------------------------------------------------------------ 5. the fallen fir

	private async Task FallenFir()
	{
		var fir = _world.GetNode<FallenTree>("FallenTree");
		Vector3 root = new(fir.Root.X, 0, fir.Root.Y);
		float len = _terrain.TrailLength;
		// Try to go round the left (root-plate) side three ways; none may get past the tree's line.
		var tries = new (string name, Vector3 from, Vector3 toward)[]
		{
			("north-west from the trail end", _terrain.TrailPoint(len - 2f, out _), root + new Vector3(-6f, 0, -8f)),
			("north, just left of the plate", root + new Vector3(-4f, 0, 6f), root + new Vector3(-4f, 0, -12f)),
			("north, 15 m left", root + new Vector3(-15f, 0, 7f), root + new Vector3(-15f, 0, -12f)),
		};
		foreach (var (name, from, toward) in tries)
		{
			Vector3 start = Ground(from);
			await Stand(start, toward + Vector3.Up * 1.5f);
			float minZ = float.MaxValue;
			for (int f = 0; f < 60 * 8; f++)
			{
				Aim(new Vector3(toward.X, _player.GlobalPosition.Y + 1.5f, toward.Z));
				_pin.ScriptedMove = new Vector2(0, 1);
				await Physics(1);
				minZ = Mathf.Min(minZ, _player.GlobalPosition.Z);
			}
			_pin.ScriptedMove = Vector2.Zero;
			// the fir's line on the left runs at about z = root.z - 1; past it would be z < root.z - 4
			Check(minZ > root.Z - 4f, $"left of the fir, {name}: stopped (furthest z {minZ:0.0}, the tree at {root.Z:0.0})");
			ShotTh($"40_blocked_{name.Replace(' ', '_').Replace(",", "")}");
		}

		// Round the crown and along the stones to the stairs' foot.
		var trail = _world.GetNode<FriendTrail>("FriendTrail");
		Vector3 Pt(float s) { var p = trail.At(s, out _); return new Vector3(p.X, 0, p.Y); }
		await Stand(Ground(_terrain.TrailPoint(len - 3f, out _)), Pt(4f) + Vector3.Up * 1.5f);
		float target = trail.Length - 1f;
		float best = 0f;
		int stuck = 0;
		Vector3 last = _player.GlobalPosition;
		for (int f = 0; f < 60 * 120; f++)
		{
			Vector3 pos = _player.GlobalPosition;
			// steer to the point 3 m ahead of the nearest point on the line
			float sNear = NearestS(trail, pos);
			best = Mathf.Max(best, sNear);
			if (sNear >= target - 0.5f) break;
			Vector3 aim = Pt(Mathf.Min(sNear + 3f, target));
			Aim(new Vector3(aim.X, pos.Y + 1.5f, aim.Z));
			_pin.ScriptedMove = new Vector2(0, 1);
			await Physics(1);
			if (f % 60 == 59)
			{
				if (_player.GlobalPosition.DistanceTo(last) < 0.3f) stuck++; else stuck = 0;
				last = _player.GlobalPosition;
				if (stuck > 5) break;
			}
		}
		_pin.ScriptedMove = Vector2.Zero;
		Check(best >= target - 1f, $"the stones lead round the crown to the stairs' foot (reached {best:0.0} of {trail.Length:0.0} m)");
		ShotTh("41_reached_stairs_foot");
	}

	private static float NearestS(FriendTrail t, Vector3 p)
	{
		float best = 0f, bd = float.MaxValue;
		for (float s = 0; s <= t.Length; s += 0.5f)
		{
			var q = t.At(s, out _);
			float d = new Vector2(p.X - q.X, p.Z - q.Y).LengthSquared();
			if (d < bd) { bd = d; best = s; }
		}
		return best;
	}

	// ------------------------------------------------------------------ 6. the photo trip

	private async Task PhotoTrip(PhotoLog log)
	{
		Node3D N(string p) => _world.GetNode<Node3D>(p);

		// ---- the empty album
		await PulseTab();
		Check(_page.IsOpen && log.RecordedCount == 0, "Tab opens the album, empty");
		Shot("album_empty");
		await PulseTab();
		Check(!_page.IsOpen, "Tab again puts it away");

		// ---- a picture of nothing in particular still goes in the album, uncaptioned
		await StandFacing(Ground(_terrain.TrailPoint(30f, out _)), 200f, 8f);
		await Shoot("trees", null, false, false);
		Check(_thumb.ShowingNumber == 0 || _thumb.ShowingId == "", "trees: no caption");

		// ---- wildflowers from the trail
		var flowers = N("Props/Wildflowers");
		await Stand(TrailNear(flowers.GlobalPosition), flowers.GlobalPosition + Vector3.Up * 0.2f);
		await Shoot("wildflowers", "wildflowers", true, true);

		// ---- the red bird
		var bird1 = _world.GetNode<Bird>("Bird1");
		Vector3 b = bird1.GlobalPosition;
		Vector3 tb = TrailNear(b);
		Vector3 fromTrail = b - tb; fromTrail.Y = 0;
		await Stand(Ground(b - fromTrail.Normalized() * 6f), b + Vector3.Up * 0.2f);
		await Shoot("bird_red", "bird_red", true, true);
		Check(bird1.Photographed, "red bird captured");

		// ---- the deer: grazing when seen from the trail, it bolts at the shutter and is gone
		var deer = _world.GetNode<Entities.Deer>("Deer");
		Vector3 dstand = TrailNear(deer.GlobalPosition);
		Note($"deer {deer.GlobalPosition.DistanceTo(dstand):0.0} m from the trail");
		await Stand(dstand, deer.GlobalPosition + Vector3.Up * 0.8f);
		await Seconds(0.5);
		Check(deer.Current == Entities.Deer.State.Grazing, $"deer still grazing with the player on the trail ({deer.Current})");
		await Raise(true);
		Check(_viewfinder.FocusLocked, "deer: focus lock on");
		Shot("viewfinder_deer");
		await Press(v => _pin.ScriptedPhoto = v);
		await Seconds(0.9);
		Check(log.Has("deer"), "deer: recorded");
		Check(deer.Current is Entities.Deer.State.Alert or Entities.Deer.State.Fleeing, $"deer: the shutter spooks it ({deer.Current})");
		Shot("print_deer");
		await Raise(false);
		await Seconds(0.4);
		ShotTh("50_deer_bolting");
		await Seconds(6.5);
		Check(deer.Current == Entities.Deer.State.Gone && !deer.Visible, $"deer: gone for good ({deer.Current})");
		Check(StoryManager.Instance.HasFlag(Entities.Deer.FledFlag), "deer: deer_fled set");

		// ---- the frog, close, from the bank
		var frog = N("Frog");
		await Stand(Ground(frog.GlobalPosition + frog.GlobalBasis.Z * 1.4f), frog.GlobalPosition + Vector3.Up * 0.1f);
		await Shoot("frog", "frog", true, true);

		// ---- the waterfall from the bridge deck
		var fall = N("Waterfall") as Waterfall;
		var bridge = N("Footbridge");
		await Stand(bridge.GlobalPosition + Vector3.Up * 0.35f, fall.CurtainCenter);
		await Shoot("waterfall", "waterfall", true, true);

		// ---- the weird stone from the trail
		var stone = N("Props/WeirdStone");
		await Stand(TrailNear(stone.GlobalPosition), stone.GlobalPosition + Vector3.Up * 1.4f);
		await Shoot("weird_stone", "weird_stone", true, true);

		// ---- the mushrooms, close and looking down
		var mush = N("Props/Mushrooms");
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

		// ---- the stairs from the clearing (never stepping on)
		var stairs = N("Clearing/Stairs");
		await Stand(Ground(stairs.GlobalTransform * new Vector3(0, 0, 9f)), stairs.GlobalTransform * new Vector3(0, 3.0f, -5.5f));
		await Shoot("stairs", "stairs", true, true);

		// ---- the album: a few more pictures of the clearing to run onto a second page, then the pages
		for (int i = 0; log.RecordedCount < PhotoLogPage.PerPage + 2 && i < 8; i++)
		{
			await StandFacing(Ground(stairs.GlobalTransform * new Vector3(4f, 0, 12f + i)), 20f * i, 5f);
			await Shoot($"extra_{i}", null, null, false, 0.3);
		}
		await Seconds(2.5);
		await PulseTab();
		Check(_page.IsOpen, "Tab opens the album");
		Check(_page.PageCount == 2 && _page.Page == 1, $"the album opens on its last page ({_page.Page + 1} / {_page.PageCount})");
		Shot("album_last_page");
		await Press(v => _pin.ScriptedItemPrev = v);
		await Seconds(0.2);
		Check(_page.Page == 0, "the wheel turns back a page");
		Shot("album_first_page");
		await Raise(true);
		Check(!_page.IsOpen, "raising the camera puts the album away");
		await Raise(false);
		Check(log.Photos[log.RecordedCount - 1].Number == log.RecordedCount, "newest last");
		_lastAlbumCount = log.RecordedCount;

		// ---- the camera stays with the player all game now (it used to be taken at the first stairs)
		await Raise(false);
		_thumb.ShowPrints = true;
	}

	// ------------------------------------------------------------------ 7. continue, the deer, the climb

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
			// Flowers and the red bird shot, the stairs photographed from the clearing; the black bird and the deer NOT.
			Flags = new[] { StoryManager.Flag.PickupTakenCamera, StoryManager.Flag.Photo("wildflowers"), StoryManager.Flag.Photo("bird_red"), StoryManager.Flag.Photo("stairs") },
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
		Check(log != null && log.RecordedCount == _lastAlbumCount, $"continue: the album comes back ({log?.RecordedCount} of {_lastAlbumCount})");
		Check(_inv.HasCamera, "continue: camera in hand");
		Check(_camera.FramesLeft == 36 - _lastAlbumCount, $"continue: film is {36 - _lastAlbumCount} ({_camera.FramesLeft})");
		Check(log.Has("bird_red") && log.Has("wildflowers") && !log.Has("deer"), "continue: the photographed subjects come back from the flags");
		foreach (var (name, gone) in new[] { ("Bird1", true), ("Bird2", false), ("Bird3", false), ("Bird4Omen", false) })
		{
			var bd = _world.GetNodeOrNull<Bird>(name);
			bool hasModel = bd != null && bd.GetNodeOrNull("Model") != null;
			Check(bd != null && hasModel == !gone, $"continue: {name} {(gone ? "gone" : "present")}");
		}
		await PulseTab();
		Check(_page.IsOpen, "continue: the album opens");
		Shot("continue_album");
		await PulseTab();

		// The deer is back (it never fled in this save); walking up to it sends it off for good.
		var deer = _world.GetNode<Entities.Deer>("Deer");
		Check(deer.Visible && deer.Current == Entities.Deer.State.Grazing, $"continue: the deer is there ({deer.Current})");
		Vector3 dp = deer.GlobalPosition;
		Vector3 tr = TrailNear(dp);
		await Stand(Ground(dp + (tr - dp).Normalized() * 14f), dp + Vector3.Up * 0.8f);
		await Seconds(0.3);
		Check(deer.Current == Entities.Deer.State.Grazing, $"deer: at 14 m, walking, it keeps grazing ({deer.Current})");
		ShotTh("51_deer_grazing_14m");
		for (int f = 0; f < 60 * 3 && deer.Current == Entities.Deer.State.Grazing; f++)
		{
			_pin.ScriptedMove = new Vector2(0, 1);
			await Physics(1);
		}
		_pin.ScriptedMove = Vector2.Zero;
		float at = _player.GlobalPosition.DistanceTo(dp);
		Check(deer.Current != Entities.Deer.State.Grazing && at < 12.5f, $"deer: walked up to, it bolts at {at:0.0} m ({deer.Current})");
		await Seconds(0.9);
		ShotTh("52_deer_bounding");
		await Seconds(6f);
		Check(deer.Current == Entities.Deer.State.Gone && StoryManager.Instance.HasFlag(Entities.Deer.FledFlag), "deer: gone, deer_fled set");

		// The first climb: walk onto the first step. The climb ends in the travel to the Hollow.
		var stairs = _world.GetNode<Node3D>("Clearing/Stairs");
		await Stand(Ground(stairs.GlobalTransform * new Vector3(0, 0, 3f)), stairs.GlobalTransform * new Vector3(0, 1.2f, -3f));
		ShotTh("60_stairs_foot");
		bool reached = false;
		void OnCp(Checkpoint cp) { if (cp == Checkpoint.Act2StairsClimbed) reached = true; }
		StoryManager.Instance.CheckpointReached += OnCp;
		var scene = GetTree().CurrentScene;
		for (int f = 0; f < 60 * 4; f++) { _pin.ScriptedMove = new Vector2(0, 1); await Physics(1); }
		_pin.ScriptedMove = Vector2.Zero;
		Check(_inv.HasCamera, "the first step keeps the camera");
		for (int i = 0; i < 120 && !reached; i++) await Seconds(0.5);
		StoryManager.Instance.CheckpointReached -= OnCp;
		Check(reached, "the climb reaches checkpoint Act2StairsClimbed");
		for (int i = 0; i < 20 && GetTree().CurrentScene == scene; i++) await Seconds(0.5);
		Note($"after the climb the current scene is {GetTree().CurrentScene?.SceneFilePath ?? "(none: the Hollow failed to load)"}");
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
	private int _lastAlbumCount;
}
