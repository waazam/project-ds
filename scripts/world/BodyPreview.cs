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
/// </summary>
public partial class BodyPreview : Node3D
{
	private PlayerController _p;
	private PlayerInput _in;
	private string _dir;
	private int _fails;

	public override void _Ready()
	{
		_dir = ProjectSettings.GlobalizePath("res://test-output/body");
		DirAccess.MakeDirRecursiveAbsolute(_dir);
		_ = Run();
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
