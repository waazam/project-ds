using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for stairs_preview.tscn: frames the stone staircase (and the
/// fragment) from first-person eye height (1.62 m, FOV 70), saves screenshots
/// to test-output/stairs/, then walks a player-sized capsule up the flight and
/// tries to walk it off the side. Not used by the game.
/// </summary>
public partial class StairsPreview : Node3D
{
	[Export] public NodePath CameraPath = "Camera3D";
	[Export] public float Eye = 1.62f;

	private Camera3D _cam;
	private string _out;
	private int _fails;

	public override void _Ready()
	{
		_cam = GetNode<Camera3D>(CameraPath);
		_out = ProjectSettings.GlobalizePath("res://test-output/stairs");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Phys() => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	private void Log(string s) => GD.Print("[stairs] " + s);
	private void Check(bool ok, string what) { Log((ok ? "PASS " : "FAIL ") + what); if (!ok) _fails++; }

	private async void Run()
	{
		await Frames(30);
		// keep only the post-process layer of the HUD (the fader starts black without GameFlow)
		foreach (var n in new[] { "ScreenFader", "PauseMenu", "DebugOverlay" })
			if (GetTree().Root.FindChild(n, true, false) is CanvasLayer cl) cl.Visible = false;
		var terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		var trig = GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Area3D;
		var stairs = trig?.GetParent() as StaircaseBuilder;
		if (stairs == null) { GD.PushError("no stairs"); GetTree().Quit(2); return; }
		Log($"stairs at {stairs.GlobalPosition}, {stairs.Steps} steps, height {stairs.TotalHeight:0.00}, trigger {trig.GlobalPosition}");

		Vector3 L(float x, float y, float z) => stairs.GlobalTransform * new Vector3(x, y, z);
		Vector3 G(float x, float z)
		{
			var p = L(x, 0, z);
			if (terrain != null) p.Y = terrain.HeightAt(p.X, p.Z);
			return p + new Vector3(0, Eye, 0);
		}
		float H = stairs.TotalHeight, zb = stairs.BackZ;
		var views = new (string, Vector3, Vector3)[]
		{
			("01_clearing_20m", G(0.9f, 20f), L(0, 2.6f, -6f)),
			("02_approach_10m", G(-0.6f, 10f), L(0, 2.2f, -5f)),
			("03_foot_look_up", G(0.15f, 1.6f), L(0, 5.2f, -9f)),
			("04_on_flight_mid", L(0.1f, 3.0f + Eye, -4.2f), L(0, 5.6f, -11f)),
			("05_top_look_down", L(0f, H + Eye, zb + 0.45f), L(0, 0.2f, 3f)),
			("06_side", G(5.5f, -3f), L(0, 2.0f, -5f)),
			("07_detail_treads", G(0.35f, 0.9f) + new Vector3(0, -0.35f, 0), L(0, 0.6f, -1.4f)),
		};
		foreach (var (name, from, to) in views)
		{
			_cam.GlobalPosition = from;
			_cam.LookAt(to, Vector3.Up);
			await Frames(8);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
		}
		if (GetTree().Root.FindChild("StaircaseFragment", true, false) is StaircaseBuilder frag)
		{
			Vector3 F(float x, float z) { var p = frag.GlobalTransform * new Vector3(x, 0, z); if (terrain != null) p.Y = terrain.HeightAt(p.X, p.Z); return p + new Vector3(0, Eye, 0); }
			_cam.GlobalPosition = F(-3.5f, 5.5f);
			_cam.LookAt(frag.GlobalTransform * new Vector3(0, 0.6f, -1f), Vector3.Up);
			await Frames(8);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/08_fragment.png");
			_cam.GlobalPosition = F(3f, 2.5f);
			_cam.LookAt(frag.GlobalTransform * new Vector3(0, 0.5f, -1f), Vector3.Up);
			await Frames(8);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/09_fragment_near.png");
		}

		await Climb(stairs, trig);
		Log(_fails == 0 ? "ALL CHECKS PASSED" : $"{_fails} CHECK(S) FAILED");
		GetTree().Quit(_fails == 0 ? 0 : 1);
	}

	private CharacterBody3D Capsule(Vector3 at)
	{
		var body = new CharacterBody3D { CollisionLayer = 2, CollisionMask = 1, FloorSnapLength = 0.45f, FloorMaxAngle = Mathf.DegToRad(48f) };
		body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.75f }, Position = new Vector3(0, 0.875f, 0) });
		GetTree().CurrentScene.AddChild(body);
		body.GlobalPosition = at + new Vector3(0, 0.05f, 0);
		return body;
	}

	private async Task Climb(StaircaseBuilder stairs, Area3D trig)
	{
		var approach = stairs.GetNode<Node3D>("AutotestApproach");
		var body = Capsule(approach.GlobalPosition);
		bool reached = false;
		trig.BodyEntered += b => { if (b == body) reached = true; };
		Vector3 fwd = -stairs.GlobalBasis.Z;
		float t = 0, maxY = -999;
		string surface = "";
		while (t < 12f && !reached)
		{
			await Phys();
			float dt = (float)GetPhysicsProcessDeltaTime(); t += dt;
			var v = body.Velocity;
			v.X = fwd.X * 1.9f; v.Z = fwd.Z * 1.9f;
			v.Y = body.IsOnFloor() ? Mathf.Min(v.Y, 0) : v.Y - 15.7f * dt;
			body.Velocity = v;
			body.MoveAndSlide();
			maxY = Mathf.Max(maxY, body.GlobalPosition.Y);
			var q = PhysicsRayQueryParameters3D.Create(body.GlobalPosition + Vector3.Up * 0.3f, body.GlobalPosition + Vector3.Down * 0.7f, 1);
			q.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
			if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node n && n.HasMeta("surface") && t > 2f) surface = n.GetMeta("surface").AsString();
		}
		Check(reached, $"capsule climbed into the top trigger in {t:0.0}s (max y rel {maxY - stairs.GlobalPosition.Y:0.00})");
		Check(surface == "stone", $"surface underfoot on the stairs = '{surface}'");
		// Stand on the top: stay there (the guard should stop overshoot)
		for (int i = 0; i < 90; i++)
		{
			await Phys();
			var v = body.Velocity; v.X = fwd.X * 1.9f; v.Z = fwd.Z * 1.9f; v.Y = body.IsOnFloor() ? 0 : v.Y - 0.26f;
			body.Velocity = v; body.MoveAndSlide();
		}
		var local = stairs.ToLocal(body.GlobalPosition);
		Check(local.Y > stairs.TotalHeight - 0.1f && local.Z > stairs.BackZ, $"walking on at the top keeps you on the landing (local {local})");
		body.QueueFree();

		// Side walls: from mid-flight, walk sideways both ways.
		foreach (int s in new[] { -1, 1 })
		{
			var mid = stairs.GlobalTransform * new Vector3(0, 3.75f, -5.6f);
			var b2 = Capsule(mid);
			Vector3 side = stairs.GlobalBasis.X * s;
			for (int i = 0; i < 150; i++)
			{
				await Phys();
				var v = b2.Velocity; v.X = side.X * 4.5f; v.Z = side.Z * 4.5f; v.Y = b2.IsOnFloor() ? 0 : v.Y - 0.26f;
				b2.Velocity = v; b2.MoveAndSlide();
			}
			var l2 = stairs.ToLocal(b2.GlobalPosition);
			Check(Mathf.Abs(l2.X) < stairs.Width * 0.5f && l2.Y > 1.5f, $"cheek wall {(s < 0 ? "left" : "right")} holds (local {l2})");
			b2.QueueFree();
		}
	}
}
