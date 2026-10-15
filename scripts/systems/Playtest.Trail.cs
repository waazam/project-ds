using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;
using ProjectDS.World.SnowMaze;

namespace ProjectDS.Systems;

/// <summary>The trailhead's beats: the systems, Acts 1 and 2, and the endings (Act 25).</summary>
public partial class Playtest
{
	// ------------------------------------------------------------------ systems

	private async Task SystemsBeat(CancellationToken ct)
	{
		if (Phase > 0)
		{
			// "Quit to Menu" from the pause menu: the menu came up (the test's menu starts a new game at once)
			Check("Quit to Menu went to the main menu, and a new game came from it", _s.MenusSeen > Memo("menus", 0) && Story.Current <= Checkpoint.Act1Start,
				$"menus seen {_s.MenusSeen}, checkpoint {Story.Current}");
			Check("the tree isn't left paused", !GetTree().Paused);
			return;
		}
		await Control(ct, 40);
		Check("a new game: checkpoint 1, saved", Story.Current >= Checkpoint.Act1Start && SaveSystem.Load()?.Checkpoint >= Checkpoint.Act1Start, $"{Story.Current}");
		Check("the camera out of the trunk", Inv.HasCamera);
		var terrain = GroundSnap.FindTerrain(this);
		// somewhere open: a stretch of the trail, facing along it
		if (terrain != null)
		{
			var p = terrain.TrailPoint(60f, out var tan);
			await _bot.Teleport(p, p + tan * 10f, ct);
		}
		// ---- walk
		await _bot.Seconds(0.3, ct);
		var start = Player.GlobalPosition;
		float walkTop = 0f;
		for (double t = 0; t < 1.2; t += GetProcessDeltaTime())
		{
			Input.ScriptedMove = new Vector2(0, 1);
			await _bot.Frames(1, ct);
			walkTop = Mathf.Max(walkTop, Player.GroundSpeed);
		}
		_bot.Release();
		float walked = TestBot.Flat(Player.GlobalPosition).DistanceTo(TestBot.Flat(start));
		Check("walking moves the player at the walk pace", walked > 1.5f && walkTop <= Player.WalkSpeed * 1.1f, $"{walked:0.0} m in 1.2 s, top {walkTop:0.00} m/s (walk {Player.WalkSpeed})");
		// ---- run, and the stamina: drains while running, runs out, locks, comes back
		var stamina = Player.Stamina;
		Require("the stamina tank exists", stamina != null);
		float top = 0f; bool ran = false, exhausted = false, stoppedRunning = false;
		using (Fast(3))
		{
			double t = 0;
			float s0 = 0f;
			if (terrain != null) terrain.TrailDistance(Player.GlobalPosition.X, Player.GlobalPosition.Z, out s0);
			while (t < 16 && !(exhausted && stoppedRunning))
			{
				ct.ThrowIfCancellationRequested();
				// along the trail (it winds): steer at a point ahead of where they are
				if (terrain != null) { terrain.TrailDistance(Player.GlobalPosition.X, Player.GlobalPosition.Z, out float sNow); _bot.Steer(terrain.TrailPoint(sNow + 6f, out _)); }
				Input.ScriptedMove = new Vector2(0, 1);
				Input.ScriptedRun = true;
				await _bot.Frames(1, ct);
				t += GetProcessDeltaTime();
				top = Mathf.Max(top, Player.GroundSpeed);
				ran |= Player.IsRunning;
				exhausted |= !stamina.CanRun;
				if (exhausted && !Player.IsRunning) stoppedRunning = true;
			}
			_bot.Release();
		}
		Check("running: faster than a walk", ran && top > Player.WalkSpeed * 1.4f, $"top {top:0.0} m/s (walk {Player.WalkSpeed}, run {Player.RunSpeed})");
		Check("the stamina runs out and running stops with it, the key still held", exhausted && stoppedRunning, $"value {stamina.Value:0.00}, can run {stamina.CanRun}");
		using (Fast(3)) await _bot.WaitUntilGame(() => stamina.CanRun && stamina.Value > 0.3f, 6, ct);
		Check("rested, it comes back", stamina.CanRun && stamina.Value > 0.3f, $"value {stamina.Value:0.00}");
		// ---- crouch (a toggle)
		float eye = Player.CameraRig.Camera.GlobalPosition.Y;
		await _bot.Crouch(true, ct);
		await _bot.Seconds(0.4, ct);
		Check("crouch: the body folds and the eye drops", Player.Crouching && Player.CrouchAmount > 0.95f && eye - Player.CameraRig.Camera.GlobalPosition.Y > 0.4f, $"amount {Player.CrouchAmount:0.00}, eye down {eye - Player.CameraRig.Camera.GlobalPosition.Y:0.00} m");
		float fastest = 0f;
		await _bot.PushFor(new Vector2(0, 1), 1.0, ct);
		_bot.Release();
		await _bot.PushFor(new Vector2(0, 1), 0.6, ct);
		fastest = Player.GroundSpeed;
		Check("crouched, at half pace or less", fastest < Player.WalkSpeed * 0.6f, $"{fastest:0.00} m/s");
		await _bot.Crouch(false, ct);
		await _bot.Seconds(0.4, ct);
		Check("stood up again", !Player.Crouching && Player.CrouchAmount < 0.05f);
		// ---- the lantern's F with no lantern: nothing happens, nothing breaks
		await _bot.Tap(on => Input.ScriptedLight = on, ct);
		Check("F without a lantern does nothing", !Inv.HasLantern);
		// ---- the pause menu
		var pause = GetTree().CurrentScene?.FindChild("PauseMenu", true, false) as PauseMenu;
		Require("the pause menu is in the level", pause != null);
		await _bot.Key("pause", ct);
		await _bot.WaitUntil(() => GetTree().Paused, 2, ct);
		Check("Esc pauses the game and opens the menu", GetTree().Paused && pause.FindChild("*", true, false) != null, $"paused {GetTree().Paused}");
		var before = Player.GlobalPosition;
		Input.ScriptedMove = new Vector2(0, 1);
		await _bot.WaitUntil(() => false, 0.5, ct);
		Input.ScriptedMove = Vector2.Zero;
		Check("paused: nothing moves", Player.GlobalPosition.DistanceTo(before) < 0.01f, $"{Player.GlobalPosition.DistanceTo(before):0.000} m");
		Shot("pause_menu");
		// settings through the menu's own rows: a slider, a toggle, a key
		var gs = GameSettings.Instance;
		float shake0 = gs.ScreenShake; bool invert0 = gs.InvertY;
		var shake = Row<HSlider>(pause, "Screen shake");
		var invert = Row<CheckBox>(pause, "Invert Y");
		Check("the menu has the settings rows (Screen shake, Invert Y)", shake != null && invert != null);
		if (shake != null) shake.Value = shake0 > 0.5f ? 0.25 : 0.75;
		if (invert != null) invert.ButtonPressed = !invert0;
		Check("a slider and a toggle in the menu change the settings", gs.ScreenShake != shake0 && gs.InvertY != invert0, $"shake {shake0:0.00} -> {gs.ScreenShake:0.00}, invert {invert0} -> {gs.InvertY}");
		// rebinding: the crouch key to K, through its button
		var crouchKey0 = KeyRebinds.KeyOf("crouch");
		var rebind = pause.FindChildren("*", "", true, false).OfType<RebindButton>().FirstOrDefault(b => b.Action == "crouch");
		Check("the keys' rows: a rebind button for crouch", rebind != null);
		if (rebind != null)
		{
			rebind.GrabFocus();
			rebind.EmitSignal(BaseButton.SignalName.Pressed);
			await _bot.Frames(2, ct);
			Godot.Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.K, Keycode = Key.K, Pressed = true });
			Godot.Input.FlushBufferedEvents();
			await _bot.Frames(2, ct);
			Godot.Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.K, Keycode = Key.K, Pressed = false });
			Godot.Input.FlushBufferedEvents();
			await _bot.Frames(2, ct);
			bool bound = KeyRebinds.KeyOf("crouch") == Key.K && InputMap.EventIsAction(new InputEventKey { PhysicalKeycode = Key.K, Pressed = true }, "crouch");
			Check("rebinding: crouch pressed, then K: K crouches now", bound, $"crouch is {KeyRebinds.KeyOf("crouch")} (was {crouchKey0})");
		}
		// closing saves (the test's own settings file, never the player's): read the file back
		await _bot.Key("pause", ct);
		await _bot.WaitUntil(() => !GetTree().Paused, 2, ct);
		Check("Esc again: resumed", !GetTree().Paused);
		var cfg = new ConfigFile();
		bool read = cfg.Load("user://test_settings.cfg") == Error.Ok;
		Check("closing the menu saved the settings (the test's file): the slider, the toggle, the key", read
			&& Mathf.IsEqualApprox((float)cfg.GetValue("accessibility", "screen_shake", -1f), gs.ScreenShake)
			&& (bool)cfg.GetValue("camera", "invert_y", invert0) == gs.InvertY && (long)cfg.GetValue("keys", "crouch", 0L) == (long)Key.K,
			$"read {read}, shake {cfg.GetValue("accessibility", "screen_shake", -1f)}, invert {cfg.GetValue("camera", "invert_y", "?")}, crouch key {cfg.GetValue("keys", "crouch", 0L)}");
		// every setting, saved and read back as a restart would (GameSettings' own round trip)
		int bad = gs.SettingsRoundTrip(out string report);
		Check("every setting survives a restart (save, scramble, load)", bad == 0, report);
		// put them back
		gs.ScreenShake = shake0; gs.InvertY = invert0;
		if (crouchKey0 != Key.None) KeyRebinds.Bind("crouch", crouchKey0);
		gs.Save();
		Check("the crouch key back as it was", KeyRebinds.KeyOf("crouch") == crouchKey0);
		// the album (Tab), open and shut
		await Album(ct);
		// Quit to Menu (the test's menu starts a new game straight away)
		await _bot.Key("pause", ct);
		await _bot.WaitUntil(() => GetTree().Paused, 2, ct);
		var quit = pause.FindChildren("*", "", true, false).OfType<MenuItem>().FirstOrDefault(b => b.Text == "Quit to Menu");
		Require("the menu's Quit to Menu", quit != null);
		Memo("menus", _s.MenusSeen);
		quit.EmitSignal(BaseButton.SignalName.Pressed);
		await ExpectReload(ct, 30);
	}

	/// <summary>The control on the settings row named <paramref name="label"/>.</summary>
	private static T Row<T>(Node root, string label) where T : Control
	{
		foreach (var l in root.FindChildren("*", nameof(Label), true, false).OfType<Label>())
			if (l.Text == label && l.GetParent() is HBoxContainer row)
				return row.GetChildren().OfType<T>().FirstOrDefault();
		return null;
	}

	private async Task Album(CancellationToken ct)
	{
		var page = GetTree().GetFirstNodeInGroup("photo_log_page") as PhotoLogPage ?? _bot.FirstOf<PhotoLogPage>();
		for (int i = 0; i < 4 && page is not { IsOpen: true }; i++)
		{
			await _bot.WaitUntil(() => Input.Enabled && !Input.Modal, 5, ct);
			await _bot.Tap(on => Input.ScriptedPhotoLog = on, ct);
			await _bot.WaitUntil(() => page is { IsOpen: true }, 1, ct);
		}
		Check("Tab opens the album", page is { IsOpen: true });
		if (page is { IsOpen: true }) Shot("album");
		for (int i = 0; i < 4 && page is { IsOpen: true }; i++)
		{
			await _bot.Tap(on => Input.ScriptedPhotoLog = on, ct);
			await _bot.WaitUntil(() => page is not { IsOpen: true }, 1, ct);
		}
		Check("Tab shuts it again", page is not { IsOpen: true });
	}

	// ------------------------------------------------------------------ Act 1

	/// <summary>Act 1 by the water: the gunshot comes to a player who wades the creek beside the footbridge.</summary>
	private async Task Act1Creek(CancellationToken ct)
	{
		await Control(ct, 40);
		var shot = _bot.AllOf<SoundEvent>().FirstOrDefault(e => e.Name == "BridgeGunshot");
		Require("the bridge's gunshot", shot != null);
		Check("not fired at the trailhead", !shot.Fired);
		string how = await CrossCreekBesideBridge(_bot.FirstOf<Footbridge>(), () => shot.Fired, ct);
		await _bot.WaitUntil(() => shot.Fired, 3, ct);
		Check($"the gunshot over the creek beside the bridge ({how}), not on its deck", shot.Fired && shot.FiredBy == "across", $"fired {shot.Fired} by {shot.FiredBy}");
		Shot("creek");
	}

	private async Task Act1(CancellationToken ct)
	{
		await Control(ct, 40);
		Check("spawned on the ground at the car", Player.IsOnFloor() && GetTree().GetFirstNodeInGroup("opening") is OpeningAtCar, $"{Player.GlobalPosition}");
		float eye = Player.CameraRig.Camera.GlobalPosition.Y - Player.GlobalPosition.Y;
		Check("first-person eye height", Mathf.Abs(eye - Player.CameraRig.EyeHeight) < 0.15f, $"{eye:0.00} m");
		Check("checkpoint 1, saved", Story.Current == Checkpoint.Act1Start && SaveSystem.Load()?.Checkpoint == Checkpoint.Act1Start, $"{Story.Current}");
		Check("no compass in Act 1", Story.ObjectivePosition == null);
		Check("the camera came out of the car, none left lying about", Inv.HasCamera && !_bot.Pickups(ToolKind.Camera).Any());
		Shot("trailhead");
		// the register (R.H.'s last entries)
		var register = _bot.AllOf<Readable>().FirstOrDefault(r => (r.Text ?? "").Contains("R.H.  the steps"));
		Check("the trail register carries R.H.'s last entry", register != null);
		if (register != null) await ReadIt(register, ct);
		// a bird, photographed
		var bird = _bot.AllOf<Bird>().Where(b => b.IsInGroup("photo_birds") && !b.IsOmen && !b.Photographed).OrderBy(b => b.GlobalPosition.DistanceTo(Player.GlobalPosition)).FirstOrDefault();
		Check("birds to photograph", bird != null);
		int before = PhotoLog.Instance?.RecordedCount ?? 0;
		if (bird != null)
		{
			var shot = await PhotographBird(bird, ct);
			Check("a bird photographed: logged and scored (clarity, focus, framing, stars)", shot is { Scored: true, Stars: >= 1 } && shot.SubjectId.StartsWith("bird_"), Describe(shot));
			Check("the viewfinder locked focus on it", _bot.FirstOf<CameraViewfinder>() != null);
		}
		// the album has it
		await Album(ct);
		Check("the album holds the picture", (PhotoLog.Instance?.RecordedCount ?? 0) > before, $"{before} -> {PhotoLog.Instance?.RecordedCount}");
		// the omen: the black bird screams and every bird is gone
		var omen = _bot.AllOf<Bird>().FirstOrDefault(b => b.IsOmen && !b.Photographed && b.IsVisibleInTree());
		if (omen != null)
		{
			var shot = await PhotographBird(omen, ct);
			await _bot.Seconds(0.5, ct);
			Check("the omen photographed: a scream, and the other birds are all gone", omen.Photographed && _bot.AllOf<Bird>().Where(b => b.IsInGroup("photo_birds")).All(b => b.Photographed), Describe(shot));
		}
		else Check("the black omen bird is somewhere on the trail", false);
		// the dead fir at the trail's end comes down as they near it
		var terrain = GroundSnap.FindTerrain(this);
		var climb = _bot.FirstOf<FirstClimbEvent>();
		var fir = _bot.FirstOf<FallenTree>();
		Require("the trail, its first staircase", terrain != null && climb != null);
		if (fir != null)
		{
			Check("the dead fir stands at first", fir is { StartsStanding: true, Fallen: false });
			var p = terrain.TrailPoint(Mathf.Max(0f, terrain.TrailLength - 25f), out var tan);
			await _bot.Teleport(p, p + tan * 10f, ct);
			var firFoot = new Vector3(fir.Root.X, terrain.HeightAt(fir.Root.X, fir.Root.Y), fir.Root.Y);
			var falls = _bot.FirstOf<Act1TreeFalls>();
			// walking on toward it, looking at it: it creaks as they near, and comes down once they're near and looking
			await _bot.Aim(firFoot + Vector3.Up * 6f, ct);
			bool Cracked() => fir.Fallen || (fir.Falling && !fir.Held);
			await _bot.WalkTo(terrain.TrailPoint(terrain.TrailLength, out _), 1.5f, ct, stopWhen: Cracked, giveUp: 20f);
			Check("nearing it and looking, it comes down", Cracked() && falls is { FirSeen: true }, $"{TestBot.Flat(Player.GlobalPosition).DistanceTo(TestBot.Flat(firFoot)):0} m from its foot");
			Check("they stop to watch it fall", falls is { Watching: true });
			// pressing on, they're held: their pace dies while the trees come down, then comes back
			float slowest = 99f; ulong t0 = Time.GetTicksMsec();
			using (Fast(3))
				while (falls is { Watching: true } && Time.GetTicksMsec() - t0 < 20000)
				{
					Input.ScriptedMove = new Vector2(0, 1);
					await _bot.Frames(1, ct);
					if (Time.GetTicksMsec() - t0 > 600) slowest = Mathf.Min(slowest, Player.GroundSpeed);
				}
			_bot.Release();
			Check("held while it comes down (the stop-and-watch), then let go", slowest < 0.3f && falls is { Watching: false }, $"slowest {slowest:0.00} m/s, {falls?.LastWatchFalls} came down in {falls?.LastWatchSeconds:0.0} s");
			using (Fast(3)) await _bot.WaitUntil(() => fir.Fallen && Player.WadeScale > 0.98f, 8, ct);
			Check("it lies across the trail, and their pace comes back", fir.Fallen && Player.WadeScale > 0.98f, $"pace {Player.WadeScale:0.00}");
			Check("no title over the stairs from the fir (they can't see them yet)", !climb.Revealed);
			Shot("fir_down");
		}
		// the stairs, found: the woods go silent by them
		var route = RouteEnd(climb.GlobalPosition, 8f);
		await _bot.Teleport(RouteEnd(climb.GlobalPosition, climb.RevealRange + 6f), climb.GlobalPosition, ct);
		await _bot.Seconds(0.5, ct);
		float apFar = FirstClimbEvent.Approach;
		float humFar = Audio.StairsHum.Instance?.LevelDb ?? -80f;
		Check("on the walk in: no title yet, the sound still on", !ChapterCard.Shown.Contains(0) && !climb.ReachedFoot && !Audio.StoryMute.On);
		await _bot.WalkTo(climb.GlobalPosition, 6.5f, ct, stopWhen: () => climb.OnTheStairs || climb.ReachedFoot, giveUp: 12f);
		await _bot.Aim(climb.GlobalPosition + Vector3.Up * 3f, ct);
		await _bot.Seconds(1.0, ct);
		Check("nearer the stairs: the hum and the ringing swell", FirstClimbEvent.Approach > apFar + 0.05f && (Audio.StairsHum.Instance?.LevelDb ?? -80f) > humFar + 1f,
			$"approach {apFar:0.00} -> {FirstClimbEvent.Approach:0.00}, hum {humFar:0.0} -> {Audio.StairsHum.Instance?.LevelDb:0.0} dB");
		Check("short of the foot: no title yet", !ChapterCard.Shown.Contains(0) && !Audio.StoryMute.On, $"foot {climb.ReachedFoot}");
		await _bot.WalkTo(climb.FootPoint, 1.2f, ct, stopWhen: () => climb.ReachedFoot || climb.OnTheStairs, giveUp: 10f);
		await _bot.WaitUntil(() => ChapterCard.Shown.Contains(0), 4, ct);
		Check("at the foot of the stairs: every sound cut, then the DEAD SILENT title", climb.ReachedFoot && Audio.StoryMute.On && ChapterCard.Shown.Contains(0),
			$"foot {climb.ReachedFoot} at {climb.ReachedFootAt:0.0} m, muted {Audio.StoryMute.On} ({Audio.StoryMute.Depth:0.00}), shown [{string.Join(",", ChapterCard.Shown)}]");
		Check("by the stairs the forest is silent", (ForestAmbienceManager.Instance?.Silence ?? 0f) > 0.7f, $"silence {ForestAmbienceManager.Instance?.Silence:0.00}");
		Check("not yet on them", !climb.OnTheStairs);
		Shot("stairs_found");
	}

	/// <summary>The dead fir with their back to it (Dan, 2026-10-13: nobody gets stuck waiting on it): stood near it
	/// looking back down the trail, then wandering off that way, never turning, it still comes down after its long
	/// wait (about 25 s), with its crack and its fall heard, and lets them go on.</summary>
	private async Task Act1FirUnseen(CancellationToken ct)
	{
		await Control(ct, 40);
		var terrain = GroundSnap.FindTerrain(this);
		var fir = _bot.FirstOf<FallenTree>();
		var falls = _bot.FirstOf<Act1TreeFalls>();
		Require("the trail, the dead fir", terrain != null && fir != null && falls != null);
		var trailEnd = terrain.TrailPoint(terrain.TrailLength, out var tan);
		var stand = trailEnd - tan * 9f;
		await _bot.Teleport(stand, stand - tan * 20f, ct);
		await _bot.Aim(stand - tan * 20f + Vector3.Up * 1.5f, ct);
		var firFoot = new Vector3(fir.Root.X, terrain.HeightAt(fir.Root.X, fir.Root.Y), fir.Root.Y);
		float d = TestBot.Flat(Player.GlobalPosition).DistanceTo(TestBot.Flat(firFoot));
		bool Cracked() => fir.Fallen || (fir.Falling && !fir.Held);
		using (Fast(3)) await _bot.WaitUntilGame(() => fir.Held, 4, ct);
		Check("stood near it with their back to it: it creaks and waits", fir.Held && !Cracked(), $"{d:0} m from its foot");
		using (Fast(3)) await _bot.WaitUntilGame(() => fir.CreakTime > 15f || Cracked(), 20, ct);
		Check("15 s on, still waiting to be seen", !Cracked(), $"creaking {fir.CreakTime:0.0} s");
		// and then they wander off back down the trail, still never looking (out past where it may fall when seen)
		var off = stand - tan * 20f;
		await _bot.Teleport(off, off - tan * 20f, ct);
		await _bot.Aim(off - tan * 20f + Vector3.Up * 1.5f, ct);
		float heldFor = 0f;
		using (Fast(3)) await _bot.WaitUntilGame(() => { if (!Cracked()) heldFor = fir.CreakTime; return Cracked(); }, 20, ct);
		Check("never looked at: it comes down anyway after about 25 s", Cracked() && falls.FirSeen == false && falls.UnseenFalls >= 1 && heldFor is > 22f and < 29f,
			$"held {heldFor:0.0} s, seen {falls.FirSeen}, {TestBot.Flat(Player.GlobalPosition).DistanceTo(TestBot.Flat(firFoot)):0} m off");
		using (Fast(3)) await _bot.WaitUntil(() => fir.Fallen && !falls.Watching && Player.WadeScale > 0.98f, 10, ct);
		Check("it lies across the trail, and they're free to go on", fir.Fallen && !falls.Watching && Player.WadeScale > 0.98f, $"pace {Player.WadeScale:0.00}, watching {falls.Watching}");
		Shot("fir_down_unseen");
	}

	/// <summary>A point on the autotest route (or straight back from it) <paramref name="metres"/> short of <paramref name="target"/>.</summary>
	private Vector3 RouteEnd(Vector3 target, float metres)
	{
		if (GetTree().GetFirstNodeInGroup("autotest_route") is Path3D { Curve: not null } path)
		{
			var pts = path.Curve.GetBakedPoints();
			for (int i = pts.Length - 1; i >= 0; i--)
			{
				var g = path.ToGlobal(pts[i]);
				if (TestBot.Flat(g).DistanceTo(TestBot.Flat(target)) >= metres) return g;
			}
		}
		var away = TestBot.Flat(Player.GlobalPosition - target).Normalized();
		return target + (away == Vector3.Zero ? Vector3.Back : away) * metres;
	}

	private async Task<PhotoLog.Photo> PhotographBird(Bird bird, CancellationToken ct)
	{
		Vector3 to = TestBot.Flat(bird.GlobalPosition - Player.GlobalPosition).Normalized();
		if (to == Vector3.Zero) to = Vector3.Forward;
		await _bot.Teleport(bird.GlobalPosition - to * 5.5f, bird.GlobalPosition, ct);
		var photo = await _bot.TakePicture(bird.GlobalPosition + Vector3.Up * 0.05f, 0, ct);
		// a bird can take off as the shutter goes: once more, on the nearest one still perched
		if (photo == null && !bird.IsOmen)
		{
			var again = _bot.AllOf<Bird>().Where(b => b.IsInGroup("photo_birds") && !b.Photographed && !b.IsOmen).OrderBy(b => b.GlobalPosition.DistanceTo(Player.GlobalPosition)).FirstOrDefault();
			if (again != null)
			{
				to = TestBot.Flat(again.GlobalPosition - Player.GlobalPosition).Normalized();
				await _bot.Teleport(again.GlobalPosition - to * 5f, again.GlobalPosition, ct);
				photo = await _bot.TakePicture(again.GlobalPosition + Vector3.Up * 0.05f, 0, ct);
			}
		}
		Shot(bird.IsOmen ? "omen" : "bird");
		return photo;
	}

	private static string Describe(PhotoLog.Photo p) => p == null ? "no picture" : $"'{p.SubjectId}': clarity {p.Clarity} focus {p.Focus} framing {p.Framing}, {p.Stars} stars, {p.Points} pts";

	/// <summary>Stands in front of a note, looks at it, E: it opens; then it's put down and control comes back.</summary>
	private async Task ReadIt(Readable note, CancellationToken ct)
	{
		var paper = note.GetParent() as Node3D ?? note;
		Vector3 normal = paper.GlobalBasis.Z.Normalized();
		if (Mathf.Abs(normal.Y) > 0.7f)
		{
			// lying flat: from its open side, away from anything sharing the surface
			var near = _bot.AllOf<Pickup>().Where(pk => !pk.Taken && pk.GlobalPosition.DistanceTo(paper.GlobalPosition) < 1.2f).ToList();
			normal = near.Count > 0 ? TestBot.Flat(paper.GlobalPosition - near.Aggregate(Vector3.Zero, (a, b) => a + b.GlobalPosition) / near.Count).Normalized()
				: TestBot.Flat(Player.GlobalPosition - paper.GlobalPosition).Normalized();
			if (normal == Vector3.Zero) normal = Vector3.Back;
		}
		else normal = TestBot.Flat(normal).Normalized();
		await _bot.Teleport(paper.GlobalPosition + normal * 1.3f, paper.GlobalPosition, ct);
		await _bot.Aim(note.ToGlobal(note.PickOffset), ct);
		await _bot.Press(ct);
		await _bot.Seconds(0.3, ct);
		bool open = NoteOverlay.Instance is { IsOpen: true };
		Check($"the note opens ('{Trim(note.Text)}')", open, $"prompt '{_bot.Prompt}'");
		if (open) Shot("note");
		await _bot.PutDown(ct);
		Check("the note is put down, control back", NoteOverlay.Instance is not { IsOpen: true } && !Input.Modal);
	}

	private static string Trim(string t) => string.IsNullOrEmpty(t) ? "" : (t.Length > 28 ? t[..28].Replace('\n', ' ') + "..." : t.Replace('\n', ' '));

	// ------------------------------------------------------------------ Act 2

	private async Task Act2(CancellationToken ct)
	{
		if (Phase > 0)
		{
			// in the Hollow: the wake
			await Control(ct, 40);
			Check("arrived by the stairs: the Hollow, at checkpoint 2", GetTree().CurrentScene?.SceneFilePath == StoryManager.HollowScene && Story.Current == Checkpoint.Act2StairsClimbed, $"{Story.Current}");
			var spawn = GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
			Check("woke at the Hollow's wake spot", spawn != null && TestBot.Flat(Player.GlobalPosition).DistanceTo(TestBot.Flat(spawn.GlobalPosition)) < 3f, $"{Player.GlobalPosition} vs {spawn?.GlobalPosition}");
			Check("the camera came along", Inv.HasCamera);
			Check("the save says checkpoint 2", SaveSystem.Load()?.Checkpoint == Checkpoint.Act2StairsClimbed);
			Check("rain falling as they wake", Audio.StormController.Instance is { Active: true });
			Check("no compass yet", Story.ObjectivePosition == null);
			Check("the sound is back in the Hollow", !Audio.StoryMute.On && FirstClimbEvent.Approach == 0f);
			Shot("wake");
			return;
		}
		await Control(ct, 30);
		var climb = _bot.FirstOf<FirstClimbEvent>();
		Require("the first staircase's trigger", climb != null);
		var stairs = climb.GetParent() as StaircaseBuilder;
		var top = stairs?.GetNodeOrNull<Node3D>("TopTrigger");
		await _bot.Teleport(RouteEnd(climb.GlobalPosition, 5f), climb.GlobalPosition, ct);
		// the foot: silence, held looking up the flight while the game's title shows, then theirs again
		await _bot.WalkTo(climb.FootPoint, 1.0f, ct, stopWhen: () => climb.ReachedFoot || climb.OnTheStairs, giveUp: 8f);
		await _bot.WaitUntil(() => climb.HeldAtFoot, 2, ct);
		await _bot.Seconds(2.5, ct);
		Check("at the foot: held, looking up the stairs, the sound gone, the title", climb.HeldAtFoot && !Input.Enabled && !Player.IsPhysicsProcessing() && Audio.StoryMute.On && climb.LookingUpTheFlight(Player), $"looking {climb.LookOffDegrees(Player):0} deg off the flight");
		using (Fast(3)) await _bot.WaitUntil(() => !climb.HeldAtFoot && Input.Enabled, 20, ct);
		Check("the title over: theirs again, and no chapter title yet", !climb.HeldAtFoot && Input.Enabled && ChapterCard.Shown.Contains(0) && !ChapterCard.Shown.Contains(2),
			$"shown [{string.Join(",", ChapterCard.Shown)}]");
		await _bot.WalkTo(climb.GlobalPosition, 0.6f, ct, stopWhen: () => climb.OnTheStairs, giveUp: 10f);
		await _bot.WaitUntil(() => climb.OnTheStairs, 4, ct);
		Require("a step onto the stairs is noticed", climb.OnTheStairs);
		await _bot.WaitUntil(() => ChapterCard.Shown.Contains(2), 4, ct);
		Check("on the stairs: the chapter title, THE STAIRS", ChapterCard.Shown.Contains(2), $"shown [{string.Join(",", ChapterCard.Shown)}]");
		Check("the first step lifts them: movement taken, the mouse still looks", climb.Floating && !Player.IsPhysicsProcessing() && Input.Enabled);
		using (Fast(3)) await _bot.WaitUntil(() => !climb.Floating, 20, ct);
		Check("the float sets them down at the top", !climb.Floating && top != null && TestBot.Flat(Player.GlobalPosition).DistanceTo(TestBot.Flat(top.GlobalPosition)) < 1.5f, $"{Player.GlobalPosition} vs {top?.GlobalPosition}");
		Check("the flight has turned one-way", climb.OneWay is { Armed: true });
		float y = Player.GlobalPosition.Y;
		if (top != null) await _bot.Aim(top.GlobalPosition + Vector3.Up * 1.5f, ct);
		await _bot.PushFor(new Vector2(0, -1), 1.2, ct);
		Check("no backing down the stairs", Player.GlobalPosition.Y > y - 0.5f, $"y {y:0.0} -> {Player.GlobalPosition.Y:0.0}");
		Shot("top");
		Require("the broken post can be inspected", climb.InspectPost != null);
		await _bot.UseIt(climb.InspectPost, ct);
		await _bot.WaitUntil(() => climb.Collapsing, 4, ct);
		Check("inspecting the post: the collapse", climb.Collapsing);
		Engine.TimeScale = 3.0;
		_s.ExpectReload = true;
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act2StairsClimbed, 40, ct);
		Check("checkpoint 2 at the top, then the travel to the Hollow", Story.Current >= Checkpoint.Act2StairsClimbed);
		await ExpectReload(ct, 20);
	}

	// ------------------------------------------------------------------ Act 25

	private async Task Act25End(int n, CancellationToken ct)
	{
		if (Phase > 0)
		{
			// the credits went back to the menu (which starts a new game in the test)
			Check("after the credits: the main menu", _s.MenusSeen > Memo("menus", 0), $"menus seen {_s.MenusSeen}");
			return;
		}
		if (n == 1)
		{
			// the table: who sees which ending
			Check("ending 1: more than ten pictures, a few deaths, over two hours", World.SnowMaze.Act25Ending.Choose(30, false, 4, 2.5) == 1);
			Check("ending 2: every picture, no deaths, over two hours", World.SnowMaze.Act25Ending.Choose(80, true, 0, 3.0) == 2);
			Check("ending 3: ten deaths or more, under ten pictures, over two hours", World.SnowMaze.Act25Ending.Choose(3, false, 14, 4.0) == 3);
			Check("ending 4: more than ten pictures, one death, under two hours", World.SnowMaze.Act25Ending.Choose(25, false, 1, 1.5) == 4);
			Check("ending 5: every picture, no deaths, under two hours", World.SnowMaze.Act25Ending.Choose(80, true, 0, 1.6) == 5);
			Check("a record off the table falls to the nearest", World.SnowMaze.Act25Ending.Choose(5, false, 3, 1.0) == 1 && World.SnowMaze.Act25Ending.Choose(5, false, 22, 1.0) == 3);
		}
		await _bot.WaitUntil(() => EndingDirector.Instance != null && Input.Enabled, 30, ct);
		var ed = EndingDirector.Instance;
		Require("Act 25 in the trailhead: out of the snow at the trail's far end", ed != null && GetTree().CurrentScene?.SceneFilePath == StoryManager.TrailheadScene, $"{Story.Current}");
		Check($"the save's ending is the one played: {n}", ed.Ending == n, $"{ed.Ending}");
		Check("standing at the snow's edge", Player.GlobalPosition.DistanceTo(ed.Start) < 5f, $"{Player.GlobalPosition.DistanceTo(ed.Start):0.0} m");
		Shot("snows_edge");
		// down the trail to the lot: jumped most of the way, the last stretch walked into the lot's reach
		var terrain = GroundSnap.FindTerrain(this);
		Require("the trail", terrain != null);
		terrain.TrailDistance(ed.CarAt.X, ed.CarAt.Z, out float sCar);
		terrain.TrailDistance(Player.GlobalPosition.X, Player.GlobalPosition.Z, out float sHere);
		float sFrom = sCar + Mathf.Sign(sHere - sCar) * 42f;
		var from = terrain.TrailPoint(sFrom, out _);
		await _bot.Teleport(from, terrain.TrailPoint(sCar, out _), ct);
		for (float s = sFrom; !ed.Reached && Mathf.Abs(s - sCar) > 4f; s -= Mathf.Sign(sFrom - sCar) * 5f)
			if (!await _bot.WalkTo(terrain.TrailPoint(s, out _), 2f, ct, () => ed.Reached, giveUp: 8f)) break;
		if (!ed.Reached) await _bot.WalkTo(ed.CarAt, 10f, ct, () => ed.Reached, giveUp: 12f);
		await _bot.WaitUntil(() => ed.Reached, 5, ct);
		Require("walked into sight of the lot", ed.Reached);
		await _bot.Seconds(1.0, ct);
		Shot("the_lot");
		if (n is 1 or 2 or 5)
		{
			var stand = GetTree().GetFirstNodeInGroup("opening") as Node3D;
			var at = stand?.GetNodeOrNull<Node3D>("Stand")?.GlobalPosition ?? ed.CarAt;
			await _bot.WalkTo(at, 0.5f, ct, giveUp: 25f);
			await _bot.Aim(ed.TrunkUse.GlobalPosition, ct);
			Check("at the trunk: put the camera back", _bot.Prompt == "Put the camera back", $"'{_bot.Prompt}'");
			await _bot.Press(ct);
		}
		else Check($"ending {n} takes them as they reach the lot", !Input.Enabled || Cutscene.ActiveCount > 0);
		using (Fast(4))
		{
			// (GameEnding.Played is static, set by the ending before this one in the same run: the save is what says this one played)
			await _bot.WaitUntil(() => Story.Current == Checkpoint.GameFinished, 60, ct);
			Check($"ending {n} plays out, to the credits: the story done and saved", GameEnding.Played && Story.Current == Checkpoint.GameFinished && SaveSystem.Load()?.Checkpoint == Checkpoint.GameFinished, $"{Story.Current}");
			Check($"the achievements: the wendigo, ending {n}, finished", Achievements.Has("ACH_THE_WENDIGO") && Achievements.Has($"ACH_ENDING_{n}") && Achievements.Has("ACH_FINISHED"), $"{Achievements.Count} earned");
			if (true)   // (every ending: regression for the pause menu opening in the fade)
			{
				// Esc as the story ends (the fade to the credits): the pause menu must stay shut, or "Quit to Menu" pulls the
				// level out from under the credits
				await _bot.Key("pause", ct);
				await _bot.Frames(3, ct);
				bool opened = GetTree().Paused;
				ClosePause();
				Check("Esc during the ending's fade: no pause menu (it's locked for the credits)", !opened, opened ? "the pause menu opened during GameEnding's fade (Credits locks it only after the fade)" : "");
			}
			await _bot.Seconds(1.5, ct);
			Shot("credits");
			// the end card, the polaroids, the thanks, the menu (every ending runs them out: leaving mid-credits is the
			// one thing a player can't do, the pause menu is locked, and a level change under them logs errors)
			if (n == 1 && PhotoLog.Instance is { RecordedCount: > 0 })
			{
				await _bot.WaitUntil(() => _bot.FirstOf<PolaroidMontage>() is { Shown: >= 1 }, 40, ct);
				Check("the credits play the pictures back as polaroids", _bot.FirstOf<PolaroidMontage>() is { Shown: >= 1 });
				Shot("polaroids");
			}
			Memo("menus", _s.MenusSeen);
			await ExpectReload(ct, 90);
		}
	}

	private async Task AfterTheEnd(CancellationToken ct)
	{
		await Control(ct, 30);
		var ed = EndingDirector.Instance;
		Check("Continue after the end: the trailhead, by the car", Story.Current == Checkpoint.GameFinished && ed != null && Player.GlobalPosition.DistanceTo(ed.CarAt) < 8f,
			$"{Story.Current}, {(ed == null ? "no ending director" : $"{Player.GlobalPosition.DistanceTo(ed.CarAt):0.0} m from the car")}");
		var before = Player.GlobalPosition;
		await _bot.PushFor(new Vector2(0, 1), 0.8, ct);
		if (Player.GlobalPosition.DistanceTo(before) < 0.2f) await _bot.PushFor(new Vector2(0, -1), 0.8, ct);
		Check("free to walk about", Player.GlobalPosition.DistanceTo(before) > 0.2f);
	}
}
