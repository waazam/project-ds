using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for body_preview.tscn (2026-10-11): the player's body (<see cref="PlayerBody"/>) looked at in the engine.
/// From the eye: standing and looking down, walking, running, backing up, sidestepping, crouched, on a stair, turning on
/// the spot; and from outside (a free camera): the whole figure and its shadow mid-stride. Shots to
/// test-output/body/. Not used by the game.
///
///     Godot --path . --resolution 1280x720 res://scenes/levels/body_preview.tscn
///     Godot --path . --resolution 1280x720 res://scenes/levels/body_preview.tscn -- --hand=after   (the lantern's hand only)
/// </summary>
public partial class BodyPreview : Node3D
{
	private PlayerController _p;
	private PlayerInput _in;
	private string _dir;
	private int _fails;

	public override void _Ready()
	{
		// (2026-10-13d) `-- --hand[=tag]`: the lantern's hand alone, from the eye and close up, in light and dark, flame and
		// blacklight, still, walking and running, looking up and down; shots to test-output/hand/<tag>/
		string hand = null;
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a == "--hand") hand = "now";
			else if (a.StartsWith("--hand=")) hand = a.Substring(7);
			else if (a.StartsWith("--stride="))
			{
				// (2026-10-14g) `-- --stride=<tag>`: the lantern and the axe in hand, a run of frames across a stride walking
				// and then running; to test-output/stride/<tag>/
				_dir = ProjectSettings.GlobalizePath($"res://test-output/stride/{a.Substring(9)}");
				DirAccess.MakeDirRecursiveAbsolute(_dir);
				_ = RunStride();
				return;
			}
		_dir = ProjectSettings.GlobalizePath(hand != null ? $"res://test-output/hand/{hand}" : "res://test-output/body");
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		if (hand == "poses") { _ = RunPoses(); return; }
		_ = hand != null ? RunHand() : Run();
	}

	/// <summary>(`-- --hand=poses`) Every <see cref="FirstPersonHand"/> pose, both sides, seen from about where the eye would
	/// be, lit by a warm lamp close by and then in the dark under it alone.</summary>
	private async Task RunPoses()
	{
		await Frames(10);
		if (GetTree().GetFirstNodeInGroup("player") is PlayerController pc) pc.ProcessMode = ProcessModeEnum.Disabled;
		var sun = GetNodeOrNull<DirectionalLight3D>("../Sun");
		var env = GetNodeOrNull<WorldEnvironment>("../WorldEnvironment")?.Environment;
		var cam = new Camera3D { Name = "PoseCam", Fov = 34f, Near = 0.02f };
		AddChild(cam);
		cam.MakeCurrent();
		var lamp = new OmniLight3D { LightColor = new Color(1f, 0.66f, 0.36f), LightEnergy = 0.5f, OmniRange = 1.2f, OmniAttenuation = 1.5f };
		AddChild(lamp);
		var rim = new OmniLight3D { LightColor = new Color(0.58f, 0.66f, 0.82f), LightEnergy = 0.45f, OmniRange = 1.1f };
		AddChild(rim);
		foreach (bool dark in new[] { false, true })
		{
			if (sun != null) sun.Visible = !dark;
			if (env != null) { env.AmbientLightEnergy = dark ? 0.02f : 0.6f; env.BackgroundColor = dark ? new Color(0.01f, 0.01f, 0.012f) : new Color(0.32f, 0.36f, 0.34f); }
			foreach (HandPose pose in System.Enum.GetValues(typeof(HandPose)))
				foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
				{
					var h = FirstPersonHand.Create(side, pose);
					if (h == null) { Check($"{side} {pose} model", false); continue; }
					var at = new Vector3(0, 20f, 0);
					h.Position = at;
					AddChild(h);
					float sx = side == HandSide.Left ? 1f : -1f;
					var eye = pose == HandPose.Handle ? new Vector3(0.23f, 0.07f, 0.64f) : new Vector3(0.22f, 0.13f, 0.55f);
					cam.Position = at + new Vector3(eye.X * sx, eye.Y, eye.Z) * 0.55f;
					cam.LookAt(at + new Vector3(-0.012f * sx, 0.01f, 0.02f));
					lamp.Position = at + new Vector3(0.03f * sx, -0.12f, 0.06f);
					rim.Position = at + new Vector3(0.28f * sx, 0.32f, 0.22f);
					await Frames(4);
					Shot($"pose_{pose}_{side}{(dark ? "_dark" : "")}".ToLowerInvariant());
					h.QueueFree();
					await Frames(1);
				}
		}
		GD.Print($"[body] done: {_fails} failed");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	private static T Find<T>(Node n) where T : class
	{
		if (n is T t) return t;
		foreach (var c in n.GetChildren()) if (Find<T>(c) is { } f) return f;
		return null;
	}

	/// <summary>The lantern's hand: from the eye, and close (a narrow camera at the eye, aimed at the fist).</summary>
	private async Task RunHand()
	{
		await Frames(10);
		_p = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_in = _p?.PlayerInput;
		if (_p == null) { GetTree().Quit(1); return; }
		_in.Scripted = true;
		_p.Teleport(new Vector3(0, 0.05f, 6f), 0f);
		_p.Inventory?.TryPickup(ToolKind.Lantern);
		var sun = GetNodeOrNull<DirectionalLight3D>("../Sun");
		var env = GetNodeOrNull<WorldEnvironment>("../WorldEnvironment")?.Environment;
		if (sun != null) sun.LightEnergy = 0.25f;
		await Seconds(1.2);
		var held = Find<HeldLantern>(_p);
		var lantern = _p.GetNodeOrNull<Lantern>("Lantern");
		Check("the lantern's held up", held != null && held.Up);
		var eye = _p.CameraRig.Camera;
		var close = new Camera3D { Name = "Close", Fov = 16f, Near = 0.02f };
		AddChild(close);
		async Task Both(string name)
		{
			Shot(name);
			var arm = held?.GetNodeOrNull<Node3D>("Arm");
			if (arm == null) return;
			close.GlobalTransform = eye.GlobalTransform;
			close.LookAt(arm.GlobalPosition - eye.GlobalBasis.Y * 0.02f, eye.GlobalBasis.Y);
			close.MakeCurrent();
			await Frames(2);
			Shot(name + "_close");
			eye.MakeCurrent();
			await Frames(1);
		}
		async Task Set(bool dark, bool uv)
		{
			if (sun != null) sun.Visible = !dark;
			if (env != null) { env.AmbientLightEnergy = dark ? 0.02f : 0.6f; env.BackgroundColor = dark ? new Color(0.01f, 0.01f, 0.012f) : new Color(0.32f, 0.36f, 0.34f); }
			lantern?.SetBlacklight(uv);
			await Seconds(0.6);
		}
		await Set(false, false);
		Look(-10f); await Seconds(0.6); await Both("01_light_rest");
		Look(35f); await Seconds(0.6); await Both("02_light_up");
		Look(-55f); await Seconds(0.6); await Both("03_light_down");
		await Set(true, false);
		Look(-10f); await Seconds(0.6); await Both("04_dark_flame");
		await Set(true, true);
		await Both("05_dark_blacklight");
		await Set(false, true);
		await Both("06_light_blacklight");
		await Set(true, false);
		_in.ScriptedFocus = true; await Seconds(1.0); await Both("07_dark_gathered");
		_in.ScriptedFocus = false; await Seconds(0.8);
		await Walk(new Vector2(0, 1), false, 1.7, -10f, "08_dark_walk", 1.5);
		await Walk(new Vector2(0, -1), true, 1.9, -10f, "09_dark_run", 1.6);
		await Set(false, false);
		await Walk(new Vector2(0, 1), true, 1.9, 20f, "10_light_run_up", 1.6);
		await Walk(new Vector2(0, -1), false, 1.7, -45f, "11_light_walk_down", 1.5);
		GD.Print($"[body] done: {_fails} failed");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	/// <summary>(`-- --stride=tag`) Frames across a stride, walking and running, the lantern up and the axe in the right hand:
	/// to see how the arms move (the owner, 2026-10-14g: "the arm itself is too long and unrealistic, especially while
	/// walking").</summary>
	private async Task RunStride()
	{
		await Frames(10);
		_p = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_in = _p?.PlayerInput;
		if (_p == null) { GetTree().Quit(1); return; }
		_in.Scripted = true;
		_p.Inventory?.TryPickup(ToolKind.Lantern);
		_p.Inventory?.TryPickup(ToolKind.Axe);
		_p.Inventory?.Select(ToolKind.Axe);
		if (GetNodeOrNull<DirectionalLight3D>("../Sun") is { } sun) sun.LightEnergy = 0.6f;
		foreach (var (name, move, run, frameGap) in new[] { ("walk", new Vector2(0, 1), false, 0.075), ("run", new Vector2(0, -1), true, 0.05) })
		{
			_p.Teleport(new Vector3(0, 0.05f, name == "walk" ? 9f : -3f), 0f);
			Look(-8f);
			await Seconds(1.0);
			Shot($"{name}_00_still");
			_in.ScriptedMove = move;
			_in.ScriptedRun = run;
			double t = 0;
			while (t < 1.4) { Look(-8f); await Frames(1); t += GetProcessDeltaTime(); }
			for (int i = 1; i <= 10; i++)
			{
				Shot($"{name}_{i:00}");
				double w = 0;
				while (w < frameGap) { Look(-8f); await Frames(1); w += GetProcessDeltaTime(); }
			}
			_in.ScriptedMove = Vector2.Zero;
			_in.ScriptedRun = false;
			await Seconds(0.4);
		}
		GD.Print($"[body] done: {_fails} failed");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

	private void Shot(string name)
	{
		GetViewport().GetTexture().GetImage().SavePng($"{_dir}/{name}.png");
		GD.Print($"[body] shot {name}");
	}

	private void Check(string what, bool ok, string detail = "")
	{
		GD.Print($"[body] {(ok ? "PASS" : "FAIL")} {what} {detail}");
		if (!ok) _fails++;
	}

	private void Look(float pitchDeg) => _p.CameraRig.SetPitch(Mathf.DegToRad(pitchDeg));

	private async Task Walk(Vector2 move, bool run, double seconds, float pitchDeg, string shot = null, double shotAt = -1)
	{
		_in.ScriptedMove = move;
		_in.ScriptedRun = run;
		double t = 0;
		bool shotTaken = false;
		while (t < seconds)
		{
			Look(pitchDeg);
			await Frames(1);
			t += GetProcessDeltaTime();
			if (shot != null && !shotTaken && t >= shotAt) { Shot(shot); shotTaken = true; }
		}
		_in.ScriptedMove = Vector2.Zero;
		_in.ScriptedRun = false;
	}

	private async Task Run()
	{
		await Frames(10);
		_p = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		_in = _p?.PlayerInput;
		if (_p == null) { GetTree().Quit(1); return; }
		_in.Scripted = true;
		_p.Teleport(new Vector3(0, 0.05f, 6f), 0f);
		// the lantern, lit (the light the body's mostly seen by in the game)
		_p.Inventory?.TryPickup(ToolKind.Lantern);
		if (GetNodeOrNull<DirectionalLight3D>("../Sun") is { } sun) sun.LightEnergy = 0.25f;
		await Seconds(1.0);
		var body = PlayerBody.Instance;
		Check("the body's built and shown", body != null && body.Shown);
		// standing: straight ahead (none of it in view), then looking down
		Look(0f); await Seconds(0.5); Shot("01_ahead");
		Look(-45f); await Seconds(0.5); Shot("02_down_45");
		Look(-70f); await Seconds(0.5); Shot("03_down_70");
		Look(-85f); await Seconds(0.5); Shot("04_down_85");
		// the right hand's grip, on a tool being used (a crowbar), and the lantern's hand from ahead
		Look(-10f); await Seconds(0.4);
		HeldUse.Do(_p, ToolKind.Crowbar, _p.GlobalPosition + new Vector3(0, 0.6f, -1.2f), 2.4f);
		await Seconds(0.8); Shot("04b_fist_crowbar");
		await Seconds(1.8);
		Look(-20f); await Seconds(0.5); Shot("04c_lantern_hand");
		// walking, looking down at the feet
		await Walk(new Vector2(0, 1), false, 2.2, -60f, "05_walk_down", 1.6);
		await Walk(new Vector2(0, 1), false, 1.0, -72f, "06_walk_down_72", 0.27);
		await Walk(new Vector2(0, 1), true, 2.0, -55f, "07_run_down", 1.5);
		await Walk(new Vector2(0, -1), false, 1.6, -60f, "08_back_down", 1.2);
		await Walk(new Vector2(1, 0), false, 1.6, -60f, "09_strafe_down", 1.2);
		// crouched
		_in.ScriptedCrouch = true; await Frames(2); _in.ScriptedCrouch = false;
		await Seconds(0.8);
		Look(-60f); await Seconds(0.3); Shot("10_crouch_down");
		await Walk(new Vector2(0, 1), false, 1.6, -60f, "11_crouch_walk", 1.2);
		_in.ScriptedCrouch = true; await Frames(2); _in.ScriptedCrouch = false;
		await Seconds(0.8);
		// turning on the spot: the feet shuffle round
		Look(-60f);
		_in.AddScriptedLook(new Vector2(-1.4f, 0));
		await Seconds(0.25); Shot("12_turn_shuffle");
		await Seconds(1.0);
		// the stairs: one foot up on a step
		_p.Teleport(new Vector3(0, 0.05f, -2.2f), 0f);
		await Seconds(0.6);
		await Walk(new Vector2(0, 1), false, 0.8, -55f, "13_stairs_up", 0.6);
		await Walk(new Vector2(0, 1), false, 1.2, -55f, "14_stairs_up_b", 1.0);
		// outside: a free camera at the figure and its shadow mid-stride
		_p.Teleport(new Vector3(4f, 0.05f, 6f), 0f);
		await Seconds(0.5);
		var free = new Camera3D { Name = "Free", Fov = 50f };
		AddChild(free);
		_in.ScriptedMove = new Vector2(0, 1);
		await Seconds(1.0);
		var at = _p.GlobalPosition;
		free.GlobalPosition = at + new Vector3(3.2f, 1.3f, -1.5f);
		free.LookAt(at + Vector3.Up * 0.9f + new Vector3(0, 0, -1.2f));
		// (the body hides from any camera that isn't the eye: make it count as the eye for this shot)
		free.MakeCurrent();
		await Frames(2);
		Shot("15_outside_hidden");
		Check("from a camera that isn't the eye, it's hidden", body != null && !body.Shown);
		_p.CameraRig.Camera.MakeCurrent();
		_in.ScriptedMove = Vector2.Zero;
		GD.Print($"[body] done: {_fails} failed");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}
}
