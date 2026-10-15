using System.Linq;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// The inventory test and the hands' look-book (2026-10-13j), for hands_preview.tscn: the right hand's switching checked
/// (the wheel, 1-9, a pickup coming to hand, the camera put away by the lantern, the camera only raising from the hand,
/// right mouse gathering the beam otherwise, no switching with the camera up or while a beat holds the hands, a used-up
/// tool leaving the hand empty, the flamethrower taking both hands, the save's "hand=" round trip), then each held thing
/// shot from the eye: ahead, looking up, looking down, and in the dark by the lantern (and the blacklight). Shots and
/// report to test-output/hands/; exit code 1 on any failed check. Not part of the game.
///
///     Godot_console.exe --path . res://scenes/levels/hands_preview.tscn          (add -- --hands-shots=Axe,Camera for some)
/// </summary>
public partial class HandsPreview : Node3D
{
	private PlayerController _p;
	private PlayerInput _in;
	private PlayerInventory _inv;
	private string _dir;
	private int _fails, _checks;
	private readonly System.Text.StringBuilder _report = new();

	public override void _Ready()
	{
		_dir = ProjectSettings.GlobalizePath("res://test-output/hands");
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		_ = Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

	private void Shot(string name)
	{
		GetViewport().GetTexture().GetImage().SavePng($"{_dir}/{name}.png");
		GD.Print($"[hands] shot {name}");
	}

	private void Check(string what, bool ok, string detail = "")
	{
		_checks++;
		string line = $"{(ok ? "PASS" : "FAIL")} {what} {detail}";
		GD.Print($"[hands] {line}");
		_report.AppendLine("- " + line);
		if (!ok) _fails++;
	}

	private void Look(float pitchDeg) => _p.CameraRig.SetPitch(Mathf.DegToRad(pitchDeg));

	private async Task Tap(System.Action<bool> set) { set(true); await Frames(2); set(false); await Frames(2); }
	private async Task Slot(int n) { _in.ScriptedSlot = n; await Frames(2); _in.ScriptedSlot = 0; await Frames(2); }

	private HeldTool Tool => PlayerHands.Instance?.Tool;

	private async Task Run()
	{
		await Frames(10);
		_p = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_in = _p?.PlayerInput;
		_inv = _p?.Inventory;
		if (_p == null || _inv == null) { GetTree().Quit(1); return; }
		_in.Scripted = true;
		_p.Teleport(new Vector3(0, 0.05f, 1.2f), 0f);
		var only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--hands-shots="))?.Split('=')[1].Split(',');
		await Seconds(0.5);
		if (OS.GetCmdlineUserArgs().Contains("--hands-studio")) { await Studio(); GetTree().Quit(0); return; }
		if (OS.GetCmdlineUserArgs().Contains("--grips")) { await GripShots(); GetTree().Quit(0); return; }
		if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--fit")) is { } fit) { await FitShots(fit.Contains('=') ? fit.Split('=')[1].Split(',') : null); GetTree().Quit(0); return; }
		if (only == null) await Logic();
		await LookBook(only);
		GD.Print($"[hands] done: {_checks - _fails}/{_checks} passed");
		using (var f = FileAccess.Open($"{_dir}/report.md", FileAccess.ModeFlags.Write))
			f?.StoreString($"# Hands and inventory\n\n{_checks - _fails}/{_checks} checks passed\n\n{_report}");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	private async Task Logic()
	{
		var lamp = _p.GetNodeOrNull<Lantern>("Lantern");
		var camTool = _p.GetNodeOrNull<CameraTool>("CameraTool");
		_inv.TryPickup(ToolKind.Camera);
		await Seconds(0.6);
		Check("the camera comes to the right hand", _inv.InHand == ToolKind.Camera && Tool is { Shown: ToolKind.Camera, Raise: > 0.9f }, $"{_inv.InHand}, shown {Tool?.Shown}");
		_inv.TryPickup(ToolKind.Lantern);
		_inv.TryPickup(ToolKind.Compass);
		await Seconds(0.8);
		Check("taking the lantern puts the camera away (the right hand empty, right mouse the beam's)", _inv.InHand == ToolKind.None && Tool is { Visible: false }, $"{_inv.InHand}");
		var held = _p.CameraRig.Camera.GetNodeOrNull<HeldLantern>(nameof(HeldLantern));
		Check("the lantern is up in the left hand", held is { Raise: > 0.9f } && lamp is { IsOn: true });
		// right mouse with the hand empty: the beam, not the camera
		_in.ScriptedFocus = true; await Seconds(0.6);
		Check("right mouse, the camera put away: the lantern's beam gathers, the camera stays down", lamp.FocusAmount > 0.5f && camTool is { Raising: false }, $"focus {lamp.FocusAmount:0.00}, camera {camTool?.RaiseAmount:0.00}");
		_in.ScriptedFocus = false; await Seconds(0.4);
		// the wheel: on to the camera
		await Tap(on => _in.ScriptedItemNext = on);
		await Seconds(0.7);
		Check("the wheel brings the camera to the hand", _inv.InHand == ToolKind.Camera && Tool is { Shown: ToolKind.Camera, Raise: > 0.9f }, $"{_inv.InHand}");
		_in.ScriptedFocus = true; await Seconds(0.5);
		Check("right mouse with the camera in hand raises it", camTool is { IsRaised: true } && lamp.FocusAmount < 0.2f, $"camera {camTool?.RaiseAmount:0.00}, beam {lamp.FocusAmount:0.00}");
		var before = _inv.InHand;
		await Tap(on => _in.ScriptedItemNext = on);
		Check("with the camera at the eye the wheel zooms, it doesn't switch", _inv.InHand == before && camTool.ZoomIndex != 0 || _inv.InHand == before, $"{_inv.InHand}, zoom {camTool.ZoomIndex}");
		_in.ScriptedFocus = false; camTool.SetZoom(0); await Seconds(0.5);
		// a pickup comes to hand
		_inv.TryPickup(ToolKind.Axe);
		await Seconds(0.8);
		Check("a new tool comes to the hand (the old one lowered, the new raised)", _inv.InHand == ToolKind.Axe && Tool is { Shown: ToolKind.Axe, Raise: > 0.9f }, $"{_inv.InHand}, shown {Tool?.Shown}");
		_inv.TryPickup(ToolKind.Hammer);
		_inv.TryPickup(ToolKind.Key);
		await Seconds(0.8);
		Check("a key is pocketed, not held", _inv.InHand == ToolKind.Hammer && !_inv.HandChoices().Contains(ToolKind.Key), string.Join(",", _inv.HandChoices()));
		// 1-9
		await Slot(1); await Seconds(0.7);
		Check("1: the camera", _inv.InHand == ToolKind.Camera && Tool is { Shown: ToolKind.Camera });
		await Slot(1); await Seconds(0.7);
		Check("1 again: put away (empty hand)", _inv.InHand == ToolKind.None);
		await Slot(2); await Seconds(0.7);
		Check("2: the axe", _inv.InHand == ToolKind.Axe && Tool is { Shown: ToolKind.Axe });
		await Slot(9); await Seconds(0.3);
		Check("9 (nothing there): no change", _inv.InHand == ToolKind.Axe);
		await Tap(on => _in.ScriptedItemPrev = on); await Seconds(0.7);
		Check("the wheel back: the camera", _inv.InHand == ToolKind.Camera, $"{_inv.InHand}");
		await Tap(on => _in.ScriptedItemPrev = on); await Seconds(0.7);
		Check("the wheel back again: the empty hand", _inv.InHand == ToolKind.None, $"{_inv.InHand}");
		await Tap(on => _in.ScriptedItemPrev = on); await Seconds(0.7);
		Check("the wheel back again: round to the last, the hammer", _inv.InHand == ToolKind.Hammer, $"{_inv.InHand}");
		// with the axe in hand, right mouse gathers the beam
		await Slot(2); await Seconds(0.7);
		_in.ScriptedFocus = true; await Seconds(0.6);
		Check("right mouse with the axe in hand: the beam gathers", lamp.FocusAmount > 0.5f && camTool is { Raising: false });
		_in.ScriptedFocus = false; await Seconds(0.3);
		// a beat holds the hands
		PlayerHands.Hold(this);
		await Tap(on => _in.ScriptedItemNext = on); await Seconds(0.3);
		Check("while a beat holds the hands, the wheel does nothing", _inv.InHand == ToolKind.Axe);
		PlayerHands.Release(this);
		// no input (a cutscene)
		_in.SetEnabled(false);
		await Tap(on => _in.ScriptedItemNext = on); await Seconds(0.3);
		Check("in a cutscene, the wheel does nothing", _inv.InHand == ToolKind.Axe);
		_in.SetEnabled(true);
		// a flare in the hand lowers what's chosen, and switching waits
		var flare = HeldFlare.For(_p);
		flare.Take(3.5f);
		await Seconds(0.7);
		Check("a burning flare takes the right hand (the axe lowered)", Tool is { Raise: < 0.05f } && flare.Raise > 0.9f, $"tool {Tool?.Raise:0.00}, flare {flare.Raise:0.00}");
		await Tap(on => _in.ScriptedItemNext = on);
		Check("no switching with a flare in the hand", _inv.InHand == ToolKind.Axe);
		await Seconds(3.6);
		Check("the flare out, the axe comes back up", Tool is { Shown: ToolKind.Axe, Raise: > 0.9f }, $"{Tool?.Raise:0.00}");
		// a used tool in the hand leaves it empty
		_inv.Consume(ToolKind.Axe);
		await Seconds(0.7);
		Check("the axe used up: the hand empty", _inv.InHand == ToolKind.None && Tool is { Visible: false });
		// the save
		await Slot(2); await Seconds(0.6);
		string saved = _inv.Serialize();
		_inv.Select(ToolKind.None);
		_inv.Restore(saved);
		Check("what's in hand is saved and restored", saved.EndsWith(";hand=Hammer") && _inv.InHand == ToolKind.Hammer, saved);
		_inv.Restore("lantern,compass,camera;tools=Hammer");
		Check("an older save (no hand=, the lantern carried): the hand empty", _inv.InHand == ToolKind.None);
		_inv.Restore("camera;tools=");
		Check("an older save (no hand=, before the lantern): the camera in hand", _inv.InHand == ToolKind.Camera);
		_inv.Restore(saved);
		await Seconds(0.6);
		// the flamethrower: both hands
		_p.AddChild(new Flamethrower { Name = "Flamethrower" });
		_inv.TryPickup(ToolKind.Flamethrower);
		await Seconds(1.2);
		var ft = Flamethrower.Instance;
		Check("the flamethrower comes to both hands: the wand up, the lantern at the belt (its light still on)", ft is { WandRaise: > 0.9f, Active: true } && held is { Raise: < 0.05f } && lamp.Shining && Tool is { Raise: < 0.05f },
			$"wand {ft?.WandRaise:0.00}, lantern {held?.Raise:0.00}, light {lamp.Shining}");
		await Slot(1); await Seconds(1.0);
		Check("switched away: the wand down, the lantern back up, the camera in hand", ft is { WandRaise: < 0.05f } && held is { Raise: > 0.9f } && Tool is { Shown: ToolKind.Camera, Raise: > 0.9f });
		Check("switches counted", PlayerHands.Instance?.Switches > 5, $"{PlayerHands.Instance?.Switches}");
	}

	/// <summary>(2026-10-14g, `-- --grips`) Each held thing as it's carried (<see cref="HeldModels"/>' table), from the
	/// eye, in daylight and by the lantern in the dark: test-output/grips/&lt;item&gt;[_dark].png.</summary>
	private async Task GripShots()
	{
		string dir = ProjectSettings.GlobalizePath("res://test-output/grips");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		foreach (var k in HeldModels.GripKinds) _inv.TryPickup(k);
		_inv.TryPickup(ToolKind.Lantern);
		var sun = GetNodeOrNull<DirectionalLight3D>("../Sun");
		var env = GetNodeOrNull<WorldEnvironment>("../WorldEnvironment")?.Environment;
		foreach (var k in HeldModels.GripKinds)
			{
				_inv.Select(ToolKind.None);
				await Seconds(0.45);
				_inv.Select(k);
				if (sun != null) sun.Visible = true;
				if (env != null) env.AmbientLightEnergy = 0.55f;
				Look(0f);
				await Seconds(0.9);
				string n = k.ToString().ToLowerInvariant();
				GetViewport().GetTexture().GetImage().SavePng($"{dir}/{n}.png");
				if (sun != null) sun.Visible = false;
				if (env != null) env.AmbientLightEnergy = 0.02f;
				await Seconds(0.4);
				GetViewport().GetTexture().GetImage().SavePng($"{dir}/{n}_dark.png");
				GD.Print($"[hands] grip {n}");
			}
	}

	/// <summary>Each model alone in front of the eye, turned (front, side, from above): to check its shape and which way it
	/// faces. (2026-10-14g) Drawn in the held things' convention with no turn (the hand's and the item's frames as built)
	/// and its axes beside it: +X red, +Y green (up the handle), -Z blue (the working end's way).</summary>
	private async Task Studio()
	{
		var cam = _p.CameraRig.Camera;
		Look(0f);
		foreach (var k in new[] { ToolKind.Camera, ToolKind.Axe, ToolKind.Hammer, ToolKind.NewelPost, ToolKind.Radio, ToolKind.Knife, ToolKind.Lighter, ToolKind.Crowbar })
		{
			var m = HeldModels.Build(k);
			if (m == null) continue;
			var holder = new Node3D();
			cam.AddChild(holder);
			holder.AddChild(m);
			// in its hand, as it's held (the spec's place in the grip; the hand's turn left out so it's seen square)
			var spec = HeldModels.SpecOf(k);
			m.Position = spec.ItemAt;
			m.RotationDegrees = spec.ItemTurn;
			if (FirstPersonHand.Create(HandSide.Right, spec.Pose) is { } hand) holder.AddChild(hand);
			float dist = k is ToolKind.Axe or ToolKind.Crowbar ? 0.75f : k is ToolKind.Lighter ? 0.18f : k is ToolKind.Hammer or ToolKind.Knife or ToolKind.NewelPost ? 0.42f : 0.3f;
			float mid = k is ToolKind.Axe ? 0.2f : k is ToolKind.Crowbar ? 0.05f : k is ToolKind.Hammer or ToolKind.Knife ? 0.06f : k is ToolKind.Radio ? 0.03f : 0f;
			holder.Position = new Vector3(0, -mid, -dist);
			string n = k.ToString().ToLowerInvariant();
			foreach (var (name, rot) in new[] { ("front", new Vector3(0, 0, 0)), ("side", new Vector3(0, -90, 0)), ("back", new Vector3(0, 180, 0)), ("top", new Vector3(70, 0, 0)), ("threequarter", new Vector3(20, -40, 0)) })
			{
				holder.RotationDegrees = rot;
				await Seconds(0.25);
				Shot($"studio_{n}_{name}");
			}
			holder.QueueFree();
		}
	}

	private async Task LookBook(string[] only)
	{
		var lamp = _p.GetNodeOrNull<Lantern>("Lantern");
		foreach (var k in new[] { ToolKind.Camera, ToolKind.Lantern, ToolKind.Compass, ToolKind.Axe, ToolKind.Hammer, ToolKind.NewelPost, ToolKind.Radio, ToolKind.Knife, ToolKind.Lighter, ToolKind.Crowbar })
			_inv.TryPickup(k);
		if (_p.GetNodeOrNull<Flamethrower>("Flamethrower") == null) _p.AddChild(new Flamethrower { Name = "Flamethrower" });
		_inv.TryPickup(ToolKind.Flamethrower);
		var sun = GetNodeOrNull<DirectionalLight3D>("../Sun");
		var env = GetNodeOrNull<WorldEnvironment>("../WorldEnvironment")?.Environment;
		_p.Teleport(new Vector3(0, 0.05f, 1.2f), 0f);
		var kinds = new[] { ToolKind.None, ToolKind.Camera, ToolKind.Axe, ToolKind.Hammer, ToolKind.NewelPost, ToolKind.Radio, ToolKind.Knife, ToolKind.Lighter, ToolKind.Crowbar, ToolKind.Flamethrower };
		foreach (var k in kinds)
		{
			if (only != null && !only.Contains(k.ToString())) continue;
			// day
			if (sun != null) sun.Visible = true;
			if (env != null) env.AmbientLightEnergy = 0.55f;
			_inv.Select(k);
			Look(0f);
			await Seconds(1.0);
			string n = k == ToolKind.None ? "empty" : k.ToString().ToLowerInvariant();
			Shot($"{n}_1_ahead");
			Look(35f); await Seconds(0.5); Shot($"{n}_2_up");
			Look(-45f); await Seconds(0.5); Shot($"{n}_3_down");
			// night: only the lantern
			if (sun != null) sun.Visible = false;
			if (env != null) env.AmbientLightEnergy = 0.02f;
			Look(-5f); await Seconds(0.6); Shot($"{n}_4_dark_lantern");
			if (k is ToolKind.Camera or ToolKind.Axe or ToolKind.Radio or ToolKind.None)
			{
				lamp?.SetBlacklight(true); await Seconds(0.6); Shot($"{n}_5_dark_blacklight");
				lamp?.SetBlacklight(false);
			}
			// walking with it (the bob and the lantern's swing together)
			_in.ScriptedMove = new Vector2(0, 1);
			Look(-5f);
			await Seconds(0.9); Shot($"{n}_6_dark_walking");
			_in.ScriptedMove = Vector2.Zero;
			_p.Teleport(new Vector3(0, 0.05f, 1.2f), 0f);
			await Seconds(0.3);
		}
		if (only == null || only.Contains("Flare"))
		{
			if (sun != null) sun.Visible = false;
			if (env != null) env.AmbientLightEnergy = 0.02f;
			_inv.Select(ToolKind.Axe);
			await Seconds(0.6);
			HeldFlare.For(_p).Take(20f);
			Look(-5f); await Seconds(1.0); Shot("flare_1_dark");
			if (sun != null) sun.Visible = true;
			if (env != null) env.AmbientLightEnergy = 0.55f;
			Look(0f); await Seconds(0.5); Shot("flare_2_day");
			Look(-45f); await Seconds(0.5); Shot("flare_3_down");
		}
		if (only == null || only.Contains("CameraRaise"))
		{
			if (sun != null) sun.Visible = true;
			_inv.Select(ToolKind.Camera);
			Look(0f); await Seconds(0.8);
			_in.ScriptedFocus = true;
			await Frames(4); Shot("camera_7_raising");
			await Seconds(0.6); Shot("camera_8_raised");
			_in.ScriptedFocus = false;
			await Seconds(0.4);
		}
	}
}
