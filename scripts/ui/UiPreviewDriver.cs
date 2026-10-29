using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>Drives ui_preview.tscn; see <see cref="UiPreview"/>.
/// Each shot is saved at 640x360 and as a nearest-scaled 1600x900 view.
/// User arg "-- --after" also writes the before/after set to test-output/after/.</summary>
public partial class UiPreviewDriver : Node
{
	private string _out;
	private string _after;
	private int _fails;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_out = ProjectSettings.GlobalizePath("res://test-output/ui");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg == "--after")
			{
				_after = ProjectSettings.GlobalizePath("res://test-output/after");
				DirAccess.MakeDirRecursiveAbsolute(_after);
			}
		}
		Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s, true), SceneTreeTimer.SignalName.Timeout);
	private void Check(bool ok, string what) { GD.Print($"[ui-preview] {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) _fails++; }

	private void Shot(string name, string afterName = null)
	{
		// The 640x360 internal frame, and what the window actually shows (upscaled).
		var img = GetViewport().GetTexture().GetImage();
		img.SavePng($"{_out}/{name}_640x360.png");
		if (_after != null && afterName != null) img.SavePng($"{_after}/{afterName}");
		// The 1600x900 view: the same frame scaled the way viewport stretch scales it (nearest). A real
		// screen grab proved unreliable (other windows on the desktop cover it).
		var big = (Image)img.Duplicate();
		big.Resize(1600, 900, Image.Interpolation.Nearest);
		big.SavePng($"{_out}/{name}_1600x900.png");
		GD.Print($"[ui-preview] shot {name}");
	}

	private async void Run()
	{
		await Frames(60);
		var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (player == null) { GD.PushError("[ui-preview] no player"); GetTree().Quit(2); return; }
		if (GetTree().GetFirstNodeInGroup("player_spawn") is Node3D spawn)
		{
			var fwd = -spawn.GlobalBasis.Z;
			player.Teleport(spawn.GlobalPosition + Vector3.Up * 0.1f, Mathf.Atan2(-fwd.X, -fwd.Z) + 0.35f);
		}
		(GetTree().CurrentScene.FindChild("ScreenFader", true, false) as ScreenFader)?.SetBlack(false);
		Input.MouseMode = Input.MouseModeEnum.Visible;
		await Frames(20);

		var inv = player.GetNode<PlayerInventory>("Inventory");
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--neon") >= 0)
		{
			// (2026-10-10c) the neon inventory in every colour, and the film canister saving
			inv.TryPickup(ToolKind.Compass);
			inv.TryPickup(ToolKind.Camera);
			inv.TryPickup(ToolKind.Lantern);
			await Seconds(1.2);
			for (int c = 0; c < NeonKit.Names.Length; c++)
			{
				GameSettings.Instance.HudColor = c;
				await Seconds(0.4);
				Shot($"neon_{c}_{NeonKit.Names[c].ToLowerInvariant()}");
			}
			GameSettings.Instance.HudColor = 0;
			// the stamina bar: a run
			StaminaBar.Preview = 1f;
			await Seconds(0.5);
			StaminaBar.Preview = 0.62f;
			await Seconds(0.25);
			Shot("neon_stamina_running");
			StaminaBar.Preview = null;
			inv.TryPickup(ToolKind.Keycard202);
			await Seconds(0.3);
			Shot("neon_pickup_ring");
			SaveIndicator.Instance?.Play();
			await Seconds(1.0);
			Shot("save_canister");
			// a chapter's title drop, held (its shadow and its colour)
			ChapterCard.Show(this, 7, 0.1f);
			await Seconds(2.2);
			Shot("chapter_card");
			GetTree().Quit(0);
			return;
		}
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--hints") >= 0) { await HintShots(player, inv); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--notes") >= 0) { await NoteShots(player); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--camera") >= 0) { await CameraShots(player, inv); return; }
		inv.TryPickup(ToolKind.Compass);
		inv.TryPickup(ToolKind.Lantern);
		await Seconds(1.0);
		Shot("hud_00_crosshair");

		// A short run so the stamina line shows.
		var pin = player.PlayerInput;
		pin.Scripted = true;
		pin.ScriptedMove = new Vector2(0, -1);
		pin.ScriptedRun = true;
		await Seconds(1.6);
		pin.ScriptedMove = Vector2.Zero;
		pin.ScriptedRun = false;
		pin.Scripted = false;
		await Frames(10);

		// A stand-in interactable dead ahead of the camera.
		var cam = player.CameraRig.Camera;
		var prop = new Node3D { Name = "PreviewCrate" };
		GetTree().CurrentScene.AddChild(prop);
		prop.GlobalPosition = cam.GlobalPosition - cam.GlobalBasis.Z * 1.9f + Vector3.Down * 0.45f;
		var mat = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.24f, 0.16f) };
		prop.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.32f, 0.32f), Material = mat } });
		var it = new Interactable { Prompt = "Take Axe", PickRadius = 0.6f, PickOffset = new Vector3(0, 0.35f, 0) };
		prop.AddChild(it);
		_ = Subtitle.Instance?.Show("\"...are you still out there? Say something.\"", 0.2f, 2.5f, 0.2f);
		await Seconds(0.6);
		var interaction = player.GetNode<PlayerInteraction>("Interaction");
		Check(interaction.Focused == it, "crosshair focuses the interactable");
		Shot("hud_01_prompt", "hud_01_all_elements.png");
		await Seconds(2.5);

		it.Prompt = "Pry the boards loose";
		it.HoldSeconds = 3f;
		pin.Scripted = true;
		pin.ScriptedInteract = true;
		await Seconds(1.3);
		Check(it.HoldProgress > 0.2f, $"hold progress advances ({it.HoldProgress:0.00})");
		Shot("hud_02_hold");
		pin.ScriptedInteract = false;
		pin.Scripted = false;
		it.Prompt = "Take Axe"; it.HoldSeconds = 0f;
		await Frames(10);

		// Both caption bands and the prompt together (worst case): they must not overlap.
		var fader = GetTree().CurrentScene.FindChild("ScreenFader", true, false) as ScreenFader;
		_ = fader?.ShowCaption("", "His hand — bandaged, cut clean off. He bled out before he ever boarded that door shut.", 0.2f, 2.5f, 0.2f);
		_ = Subtitle.Instance?.Show("\"...are you still out there? Say something.\"", 0.2f, 2.5f, 0.2f);
		await Seconds(0.8);
		Shot("hud_03_captions");
		await Seconds(2.5);

		// The same state as the old review shot: a caption plus a subtitle, nothing focused.
		prop.Visible = false;
		_ = fader?.ShowCaption("", "Every screen shows the same thing: the stairs, in the woods.", 0.2f, 2.5f, 0.2f);
		_ = Subtitle.Instance?.Show("\"...are you still out there? Say something.\"", 0.2f, 2.5f, 0.2f);
		await Seconds(0.8);
		Shot("hud_04_caption", "hud_02_caption.png");
		await Seconds(2.5);

		var pause = GetTree().CurrentScene.FindChild("PauseMenu", true, false) as PauseMenu;
		pause?.SetOpen(true);
		await Frames(20);
		Shot("pause_01");
		Check(GetTree().Paused, "pause menu pauses the tree");

		// Quit to Menu, then make sure the menu works.
		var quit = FindButton(pause, "Quit to Menu");
		Check(quit != null, "pause menu has Quit to Menu");
		quit?.EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(90);
		Check(!GetTree().Paused, "tree unpaused after Quit to Menu");
		Check(GetTree().CurrentScene is MainMenu, "main menu loaded");
		var focus = GetViewport().GuiGetFocusOwner();
		Check(focus is MenuItem, $"a menu item has focus ({(focus as Button)?.Text})");
		await Seconds(1.5);
		Shot("menu_01", "ui_01_main_menu.png");
		var settings = FindButton(GetTree().CurrentScene, "Settings");
		settings?.EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(20);
		Check(FindButton(GetTree().CurrentScene, "Back") is { } back && back.IsVisibleInTree(), "Settings opens after Quit to Menu");
		Shot("menu_02_settings", "ui_02_settings.png");

		GD.Print($"[ui-preview] done, {_fails} failure(s)");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	/// <summary>`-- --hints [--hints-tag=before]` (2026-10-15): every kind of hint at 1920x1080 and 2560x1440, in the day and in
	/// the dark, the window's real frame (2D at the window's resolution with the CRT filter on). Shots go to
	/// test-output/hints/&lt;tag&gt;/.</summary>
	/// <summary>(2026-10-29) "-- --camera": the camera at the eye, soft before it's focused, the iris working while the
	/// focus button's held, sharp once it's racked in, at x1 and x3; the empty hand on the HUD in two colours; then the
	/// album's shot list with some of it ticked and printed (test-output/ui/camera_*, journal_*).</summary>
	private async Task CameraShots(PlayerController player, PlayerInventory inv)
	{
		inv.TryPickup(ToolKind.Compass);
		inv.TryPickup(ToolKind.Camera);
		await Seconds(0.6);
		inv.Select(ToolKind.None);
		await Seconds(1.2);
		Shot("camera_empty_hand");
		GameSettings.Instance.HudColor = 4;
		await Seconds(0.4);
		Shot("camera_empty_hand_blue");
		GameSettings.Instance.HudColor = 0;
		inv.Select(ToolKind.Camera);
		await Seconds(1.2);
		var pin = player.PlayerInput;
		var cam = player.GetNodeOrNull<CameraTool>("CameraTool");
		pin.Scripted = true;
		pin.ScriptedLens = false;
		pin.ScriptedFocus = true;
		cam?.SetFocusDistance(60f);
		player.CameraRig.SetPitch(Mathf.DegToRad(-8f));
		await Seconds(0.8);
		Shot("camera_01_soft");
		Check(cam != null && cam.FocusLevel < 0.5f, $"the lens is soft before it's focused ({cam?.FocusLevel:0.00}, focus at {cam?.FocusDistance:0.0} m on {cam?.SubjectDistance:0.0} m)");
		pin.ScriptedLens = true;
		await Seconds(0.2);
		Shot("camera_02_racking");
		await Seconds(1.4);
		Shot("camera_03_sharp");
		Check(cam != null && cam.FocusLevel > 0.85f, $"held, the focus racks in ({cam?.FocusLevel:0.00}, focus at {cam?.FocusDistance:0.0} m on {cam?.SubjectDistance:0.0} m)");
		pin.ScriptedLens = false;
		await Seconds(0.5);
		Shot("camera_04_released");
		cam?.SetZoom(3);
		cam?.SetFocusDistance(1.5f);
		await Seconds(0.6);
		Shot("camera_05_zoom_soft");
		pin.ScriptedLens = true;
		await Seconds(0.25);
		Shot("camera_06_zoom_racking");
		await Seconds(1.8);
		Shot("camera_07_zoom_sharp");
		pin.ScriptedLens = false;
		cam?.SetZoom(0);
		pin.ScriptedFocus = false;
		pin.ScriptedLens = null;
		await Seconds(0.6);

		// the shot list: the overlook ticked by a shot (its print the shot that ticked it), three subjects on film
		// (the preview's world has no album of its own: one in a test folder, never the player's photos)
		if (PhotoLog.Instance == null)
		{
			PhotoLog.FolderOverride = "user://test_photos_ui_preview/";
			AddChild(new PhotoLog { Name = "PhotoLog" });
			await Frames(2);
		}
		var log = PhotoLog.Instance;
		var page = GetTree().GetFirstNodeInGroup("photo_log_page") as PhotoLogPage;
		Check(log != null && page != null, "the album and its page are there");
		if (log != null && page != null)
		{
			StoryManager.Instance?.SetFlag(World.Act1Walks.ShotOverlookFlag);
			foreach (var id in new[] { "", "waterfall", "bird_red", "deer" })
			{
				var img = GetViewport().GetTexture().GetImage();
				img.Convert(Image.Format.Rgba8);
				img.Resize(PhotoLog.ThumbWidth, PhotoLog.ThumbHeight, Image.Interpolation.Bilinear);
				log.BeginShot(img, player.CameraRig.Camera, new Rect2(0, 0, 640, 360));
				if (id != "") log.Record(id, null, new PhotoLog.Score { Clarity = 90, Focus = 95, Framing = 80, Zoom = 80 });
				log.EndShot();
				player.Teleport(player.GlobalPosition, player.Rotation.Y + 0.9f);
				await Seconds(0.3);
			}
			page.OpenJournal();
			await Seconds(0.5);
			Shot("journal_01_some_ticked");
			Check(PhotoJournal.DoneCount == 4, $"the shot list has four ticked ({PhotoJournal.DoneCount})");
			Check(PhotoJournal.PrintFor(PhotoJournal.All[0]) != null, "the overlook's line has the shot that ticked it");
			page.OpenJournal();
			await Seconds(0.1);
		}
		pin.Scripted = false;
		GD.Print($"[ui-preview] camera: {(_fails == 0 ? "PASS" : $"{_fails} FAILED")}");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	/// <summary>(2026-10-24) "-- --notes": the note card in each paper and hand, with and without a doodle (R.H.'s pages,
	/// a typed log, a printed card, a smudged note, one in block capitals), each shot as test-output/ui/note_*.</summary>
	private async Task NoteShots(PlayerController player)
	{
		var samples = new System.Collections.Generic.List<(string name, string title, string text, Readable.NoteStyle style)>();
		foreach (var pg in World.LorePages.All) samples.Add(($"rh{pg.N}", "", pg.Text, Readable.NoteStyle.Handwritten));
		samples.Add(("titled", "Blackfern Trail", "Came up the old way. The steps are further than I remembered. Back by dark. - R.H.", Readable.NoteStyle.Handwritten));
		samples.Add(("typed", "Ranger station log", "OCT 9  0600  Two hikers signed in at the trailhead. Did not sign out.\nOCT 9  1900  Search called off for the night. Weather closing.\nOCT 10 0700  Their car is still in the lot. Keys in it.", Readable.NoteStyle.Typed));
		samples.Add(("printed", "Overlook Park", "Trails close at dusk. Stay on marked paths. Do not feed the wildlife.", Readable.NoteStyle.Printed));
		samples.Add(("smudged", "", "It was at the window again last night. I counted the days since it started. Eleven. It knows my name now. It says it in my mother's voice. Don't open the door for anyone. Not even me.", Readable.NoteStyle.Smudged));
		samples.Add(("caps", "", "THE MONSTERS WERE NEVER UNDER MY BED.\nBECAUSE THE MONSTERS WERE INSIDE MY HEAD.\nI FEAR NO MONSTERS, FOR NO MONSTERS I SEE.", Readable.NoteStyle.Handwritten));
		samples.Add(("long", "", string.Join(" ", System.Linq.Enumerable.Repeat("The trail goes on past where the map ends, and the trees close in, and the path forks where it shouldn't.", 6)), Readable.NoteStyle.Handwritten));
		foreach (var (name, title, text, style) in samples)
		{
			var r = new Readable { Name = "PreviewNote", Title = title, Text = text, Style = style, Enabled = false, Visible = false };
			AddChild(r);
			NoteOverlay.Instance?.Open(r, player);
			await Seconds(0.6);
			Shot($"note_{name}");
			NoteOverlay.Instance?.Close();
			await Seconds(0.3);
			r.QueueFree();
		}
		GetTree().Quit(0);
	}

	private async Task HintShots(PlayerController player, PlayerInventory inv)
	{
		string tag = "now";
		foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--hints-tag=")) tag = a["--hints-tag=".Length..];
		string dir = ProjectSettings.GlobalizePath($"res://test-output/hints/{tag}");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		inv.TryPickup(ToolKind.Compass);
		inv.TryPickup(ToolKind.Camera);
		inv.TryPickup(ToolKind.Lantern);
		await Seconds(1.0);
		var cam = player.CameraRig.Camera;
		var prop = new Node3D { Name = "PreviewCrate" };
		GetTree().CurrentScene.AddChild(prop);
		prop.GlobalPosition = cam.GlobalPosition - cam.GlobalBasis.Z * 1.9f + Vector3.Down * 0.45f;
		prop.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.32f, 0.32f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.24f, 0.16f) } } });
		var it = new Interactable { Prompt = "Take the lantern", PickRadius = 0.6f, PickOffset = new Vector3(0, 0.35f, 0) };
		prop.AddChild(it);
		var fader = GetTree().CurrentScene.FindChild("ScreenFader", true, false) as ScreenFader;
		if (ChoicePrompt.Instance == null) GetTree().CurrentScene.AddChild(new ChoicePrompt());
		var win = GetWindow();
		var suns = GetTree().Root.FindChildren("*", "DirectionalLight3D", true, false);
		var env = GetViewport().World3D?.Environment;
		Node atmo = null;
		foreach (var n in GetTree().Root.FindChildren("*", "", true, false)) if (n.GetType().Name == "ForestAtmosphere") { atmo = n; break; }
		float envExp = env?.TonemapExposure ?? 1f;
		foreach (var res in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			win.Mode = Window.ModeEnum.Windowed;
			win.Size = res;
			await Frames(20);
			foreach (bool dark in new[] { false, true })
			{
				// the dark: the atmosphere held still, the sun out and the exposure right down (the lantern still lights the hand)
				if (atmo != null) atmo.ProcessMode = dark ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
				foreach (var n in suns) if (n is DirectionalLight3D d) d.Visible = !dark;
				if (env != null) env.TonemapExposure = dark ? envExp * 0.15f : envExp;
				await Frames(3);
				string k = $"{res.Y}p_{(dark ? "dark" : "light")}";
				async Task Snap(string name)
				{
					var img = GetViewport().GetTexture().GetImage();
					img.SavePng($"{dir}/{k}_{name}.png");
					GD.Print($"[ui-preview] hint shot {k}_{name} ({img.GetWidth()}x{img.GetHeight()})");
					await Frames(1);
				}
				async Task Clear() { prop.Visible = false; await Seconds(1.6); }
				// 1. the interaction prompt, short
				prop.Visible = true; it.Prompt = "Take the lantern"; it.HoldSeconds = 0f;
				await Seconds(0.9);
				await Snap("1_prompt");
				// 2. a hold prompt
				it.Prompt = "Throw the switch"; it.HoldSeconds = 3f;
				await Seconds(0.5);
				await Snap("2_prompt_hold");
				// 3. a long examine line
				it.Prompt = "An empty niche, the shape of a chalice worked in gold at its back."; it.HoldSeconds = 0f;
				await Seconds(0.5);
				await Snap("3_examine");
				// 4. a context hint with its key, and a prompt at once (the overlap case)
				it.Prompt = "Read the note";
				_ = Subtitle.Instance?.Show($"Something's there the ember won't show.   {KeyHint.Blacklight}: the blacklight.", 0.2f, 2.2f, 0.2f);
				await Seconds(0.8);
				await Snap("4_hint_and_prompt");
				await Seconds(2.0);
				// 5. the lantern's how-to (many keys), a thought caption and a prompt: the worst stack
				_ = Subtitle.Instance?.Show("F: lantern on and off.   B: its blacklight.   Right mouse: gather its beam.   Wheel: the camera.", 0.2f, 2.4f, 0.2f);
				_ = fader?.ShowCaption("", "Pitch black. The light switch is by the door.", 0.2f, 2.4f, 0.2f);
				await Seconds(0.8);
				await Snap("5_howto_thought_prompt");
				await Seconds(2.2);
				// 6. a thought alone and a long context hint alone (nothing focused)
				await Clear();
				_ = fader?.ShowCaption("", "Not yet. Something is still calling, back across the creek.", 0.2f, 2.2f, 0.2f);
				await Seconds(0.8);
				await Snap("6_thought");
				await Seconds(2.0);
				_ = Subtitle.Instance?.Show($"The flame won't take. The wick only smoulders.   {KeyHint.Blacklight}: the blacklight still works.", 0.2f, 2.2f, 0.2f);
				await Seconds(0.8);
				await Snap("7_hint_long");
				await Seconds(2.0);
				// 8. a spoken line (dialogue keeps its own look) under a thought
				_ = Subtitle.Instance?.Show("\"...are you still out there? Say something.\"", 0.2f, 2.2f, 0.2f);
				_ = fader?.ShowCaption("", "Snowed in.", 0.2f, 2.2f, 0.2f);
				await Seconds(0.8);
				await Snap("8_dialogue_thought");
				await Seconds(2.0);
				// 9. the choice prompt's control hint
				if (ChoicePrompt.Instance != null)
				{
					// (with a hint and a thought up too: playtest 14 caught the hint under the choice's controls)
					_ = Subtitle.Instance?.Show($"Something's there the ember won't show.   {KeyHint.Blacklight}: the blacklight.", 0.2f, 2.2f, 0.2f);
					_ = fader?.ShowCaption("", "Deeper.", 0.2f, 2.2f, 0.2f);
					var ask = ChoicePrompt.Instance.Ask(player, "Jump across?", "Jump down?");
					await Seconds(0.8);
					await Snap("9_choice");
					ChoicePrompt.Instance.TestChoose(-1);
					await ask;
					await Seconds(0.4);
				}
			}
		}
		foreach (var n in suns) if (n is DirectionalLight3D d) d.Visible = true;
		if (atmo != null) atmo.ProcessMode = ProcessModeEnum.Inherit;
		if (env != null) env.TonemapExposure = envExp;
		// a pad in use: its buttons named, at 1080p in the day; then the large hint size
		win.Size = new Vector2I(1920, 1080);
		await Frames(20);
		async Task Shot2(string name)
		{
			var img = GetViewport().GetTexture().GetImage();
			img.SavePng($"{dir}/1080p_{name}.png");
			GD.Print($"[ui-preview] hint shot 1080p_{name}");
			await Frames(1);
		}
		foreach (var (pad, size, name) in new[] { (true, 1, "pad"), (false, 2, "large"), (false, 0, "small") })
		{
			HintStyle.UsingPad = pad;
			int keepSize = GameSettings.Instance.HintSize;
			GameSettings.Instance.HintSize = size;   // (not saved: the field alone)
			prop.Visible = true; it.Prompt = "Take the lantern"; it.HoldSeconds = 0f;
			_ = Subtitle.Instance?.Show(KeyHint.ForDevice("F: lantern on and off.   B: its blacklight.   Right mouse: gather its beam.   Wheel: the camera."), 0.2f, 2.2f, 0.2f);
			await Seconds(0.9);
			await Shot2($"{name}_1_howto_prompt");
			await Seconds(2.0);
			it.Prompt = "Throw the switch"; it.HoldSeconds = 3f;
			_ = Subtitle.Instance?.Show($"Something's there the ember won't show.   {KeyHint.Blacklight}: the blacklight.", 0.2f, 2.2f, 0.2f);
			_ = fader?.ShowCaption("", "Pitch black. The light switch is by the door.", 0.2f, 2.2f, 0.2f);
			await Seconds(0.9);
			await Shot2($"{name}_2_hold_hint_thought");
			await Seconds(2.0);
			GameSettings.Instance.HintSize = keepSize;
		}
		HintStyle.UsingPad = false;
		GD.Print($"[ui-preview] hint shots done -> {dir}");
		GetTree().Quit(0);
	}

	private static Button FindButton(Node root, string text)
	{
		if (root == null) return null;
		foreach (var n in root.FindChildren("*", "Button", true, false))
			if (n is Button b && b.Text == text) return b;
		return null;
	}
}
