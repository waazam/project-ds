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

	private static Button FindButton(Node root, string text)
	{
		if (root == null) return null;
		foreach (var n in root.FindChildren("*", "Button", true, false))
			if (n is Button b && b.Text == text) return b;
		return null;
	}
}
