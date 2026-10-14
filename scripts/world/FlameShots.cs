using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.SnowMaze;

namespace ProjectDS.World;

/// <summary>
/// The flamethrower's look-book (dev only, 2026-10-14d). Run as the main scene with the test save slots:
/// `godot --path . res://scenes/levels/flame_shots.tscn -- --autotest [--fs-out=dir] [--fs-only=idle,wall,floor,tunnel,wendigo]`.
/// Seeds Act 24's flamethrower save (in the trenches, the flamethrower in hand), Continues into it, keeps the story test
/// and the wendigo's hunt out of it, then fires in each set-up and photographs the sequence frame by frame: the wand at
/// rest, the first burst, the stream at full length, the splash, the fire clinging after the trigger's let go, the smoke
/// and the scorch; and the wendigo burning. Logs the fps while firing. Shots go to test-output/flame/&lt;dir&gt;/.
/// </summary>
public partial class FlameShots : Node
{
	private string _out = "res://test-output/flame";
	private HashSet<string> _only;
	private readonly List<(string label, double fps)> _fps = new();

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		string dir = "now";
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a.StartsWith("--fs-out=")) dir = a["--fs-out=".Length..];
			else if (a == "--fs-nohaze") NapalmStream.HazeOn = false;
			else if (a.StartsWith("--fs-only=")) _only = new HashSet<string>(a["--fs-only=".Length..].Split(','));
		}
		_out += "/" + dir;
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_out));
		using (FileAccess.Open("res://test-output/flame/.gdignore", FileAccess.ModeFlags.Write)) { }
		GetTree().NodeAdded += n =>
		{
			if (n is not GameFlow gf) return;
			SetAutoTest(false);
			gf.Ready += () => SetAutoTest(true);
		};
		Callable.From(Begin).CallDeferred();
	}

	private static void SetAutoTest(bool v) =>
		typeof(GameSettings).GetProperty(nameof(GameSettings.AutoTest))!.SetValue(GameSettings.Instance, v);

	private void Begin()
	{
		var root = GetTree().Root;
		GetParent().RemoveChild(this);
		root.AddChild(this);
		SaveSystem.Save(new SaveData
		{
			Checkpoint = Checkpoint.Act24Flamethrower,
			Flags = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(ContinueRoundTripTest.F23f, new[] { LodgeFlag.FrontBroken, Act24Maze.FlagCrowbar, Act24Maze.FlagCrate })),
			Inventory = "lantern,compass,radio;tools=Lighter+Keycard201+Crowbar+Flamethrower",
		});
		StoryManager.Instance.ContinueGame();
		Run();
	}

	public override void _Process(double delta)
	{
		foreach (var n in GetTree().Root.FindChildren("*", nameof(StoryTest), true, false)) n.QueueFree();
	}

	private bool Want(string s) => _only == null || _only.Contains(s);

	private PlayerController _p;
	private Flamethrower _ft;

	private async void Run()
	{
		var tree = GetTree();
		double t = 0;
		while (t < 120)
		{
			await Frame();
			t += GetProcessDeltaTime();
			_p = tree.GetFirstNodeInGroup("player") as PlayerController;
			if (_p != null && GameFlow.Instance is { Started: true } && Act24Maze.Instance?.Cave is { Built: true }) break;
		}
		if (_p == null || Act24Maze.Instance == null) { GD.PrintErr("[flame] no player or maze"); tree.Quit(2); return; }
		_p.PlayerInput.Scripted = true;
		await Wait(6f);
		var maze = Act24Maze.Instance;
		var cave = maze.Cave;
		var L = cave.Layout;
		var hunter = maze.Hunter;
		if (hunter != null) { hunter.Paused = true; hunter.Body.GlobalPosition = cave.ToGlobal(L.Nodes[0]) + Vector3.Down * 50f; }
		_ft = _p.GetNodeOrNull<Flamethrower>("Flamethrower");
		if (_ft == null) { GD.PrintErr("[flame] no flamethrower"); tree.Quit(2); return; }
		var start = _p.GlobalPosition;
		GD.Print($"[flame] at {start}, active {_ft.Active}");
		int shot = 0;
		// the cavern: across it to its wall
		var centre = cave.ToGlobal(SnowMazeLayout.CavernCentre);
		var wallDir = OpenDir(start, 9f);
		if (Want("idle"))
		{
			await Pose(start, start + (-_p.CameraRig.GlobalBasis.Z with { Y = 0 }) * 10f, 2.0);
			Save($"{shot++:00}_idle_ahead");
			await Aim(start + (-_p.CameraRig.GlobalBasis.Z with { Y = 0 }) * 3f + Vector3.Down * 1.2f, 1.0);
			Save($"{shot++:00}_idle_down");
		}
		if (Want("wall"))
		{
			var target = WallFrom(start, wallDir, 30f);
			var stand = start;
			await Pose(stand, target + Vector3.Up * 0.8f, 1.5);
			shot = await FireSequence("wall", shot, 2.6, new[] { 0.08, 0.25, 0.6, 1.2, 2.4 }, new[] { 0.3, 1.2, 3.0, 6.0 });
		}
		if (Want("floor"))
		{
			var f = -_p.CameraRig.GlobalBasis.Z with { Y = 0 };
			await Pose(start, start + f.Normalized() * 5f + Vector3.Down * 1.6f, 1.5);
			shot = await FireSequence("floor", shot, 1.8, new[] { 0.3, 1.0, 1.8 }, new[] { 0.5, 2.5, 6.0, 16.0 });
		}
		if (Want("tunnel"))
		{
			// a dark tunnel off the solution's way, looking along it
			int a = L.Solution[Math.Max(1, L.Solution.Count / 3)], b = L.Solution[Math.Max(1, L.Solution.Count / 3) + 1];
			var pa = cave.ToGlobal(L.Nodes[a]); var pb = cave.ToGlobal(L.Nodes[b]);
			await Pose(pa + Vector3.Up * 0.2f, pb + Vector3.Up * 1.4f, 2.5);
			Save($"{shot++:00}_tunnel_dark");
			shot = await FireSequence("tunnel", shot, 2.2, new[] { 0.15, 0.6, 1.6 }, new[] { 0.6, 2.5 });
		}
		if (Want("wendigo") && hunter != null)
		{
			// down a tunnel, it six metres off, facing them
			int a = L.Solution[Math.Max(1, L.Solution.Count / 3)], b = L.Solution[Math.Max(1, L.Solution.Count / 3) + 1];
			var pa = cave.ToGlobal(L.Nodes[a]); var pb = cave.ToGlobal(L.Nodes[b]);
			await Pose(pa + Vector3.Up * 0.2f, pb + Vector3.Up * 1.4f, 1.0);
			var stand = _p.GlobalPosition;
			var f = (pb - pa) with { Y = 0 };
			f = f.Normalized();
			var at = stand + f * 6.5f;
			hunter.Body.GlobalPosition = at with { Y = stand.Y - 0.05f };
			hunter.Body.LookAt(stand with { Y = hunter.Body.GlobalPosition.Y }, Vector3.Up);
			await Pose(stand, at + Vector3.Up * 1.4f, 1.5);
			Save($"{shot++:00}_wendigo_before");
			hunter.HoldStill = true;
			hunter.Paused = false;
			_ft.ScriptedFire = true;
			double ft = 0;
			int k = 0;
			double[] marks = { 0.2, 0.5, 0.9 };
			while (ft < 1.0)
			{
				await Frame();
				ft += GetProcessDeltaTime();
				Keep(stand, at + Vector3.Up * 1.4f);
				if (k < marks.Length && ft >= marks[k]) Save($"{shot++:00}_wendigo_fire_{marks[k++]:0.0}");
			}
			_ft.ScriptedFire = false;
			ft = 0; k = 0;
			double[] after = { 0.4, 1.4 };
			while (ft < 1.5)
			{
				await Frame();
				ft += GetProcessDeltaTime();
				hunter.Body.GlobalPosition = at with { Y = stand.Y - 0.05f };
				Keep(stand, at + Vector3.Up * 1.4f);
				if (k < after.Length && ft >= after[k]) Save($"{shot++:00}_wendigo_burning_{after[k++]:0.0}");
			}
			hunter.Paused = true;
			GD.Print($"[flame] wendigo hits {hunter.Hits}");
		}
		foreach (var (label, fps) in _fps) GD.Print($"[flame] fps {label}: {fps:0.0}");
		GD.Print("[flame] done");
		tree.Quit(0);
	}

	/// <summary>Fire for <paramref name="hold"/> s, photographing at each of <paramref name="marks"/>, then let go and
	/// photograph at each of <paramref name="after"/>. Logs the frames' mean fps while firing.</summary>
	private async System.Threading.Tasks.Task<int> FireSequence(string label, int shot, double hold, double[] marks, double[] after)
	{
		var pos = _p.GlobalPosition;
		var look = _p.CameraRig.Camera.GlobalPosition + (-_p.CameraRig.Camera.GlobalBasis.Z) * 10f;
		_ft.ScriptedFire = true;
		double t = 0;
		int k = 0, frames = 0;
		double ftime = 0;
		while (t < hold)
		{
			await Frame();
			double d = GetProcessDeltaTime();
			t += d;
			if (t > 0.4) { frames++; ftime += d; }
			Keep(pos, look);
			if (k < marks.Length && t >= marks[k]) Save($"{shot++:00}_{label}_fire_{marks[k++]:0.00}");
		}
		_fps.Add((label, frames / Math.Max(ftime, 1e-6)));
		_ft.ScriptedFire = false;
		t = 0; k = 0;
		while (k < after.Length)
		{
			await Frame();
			t += GetProcessDeltaTime();
			Keep(pos, look);
			if (t >= after[k]) Save($"{shot++:00}_{label}_after_{after[k++]:0.0}");
		}
		// (cool it right down for the next)
		await Wait(_ft.Overheated ? 7.5f : 3.5f);
		return shot;
	}

	/// <summary>The first heading round <paramref name="from"/> whose eye-level ray runs clear at least
	/// <paramref name="want"/> m (under 25), else the clearest.</summary>
	private Vector3 OpenDir(Vector3 from, float want)
	{
		Vector3 best = Vector3.Forward;
		float bd = -1f;
		for (int i = 0; i < 16; i++)
		{
			var d = new Vector3(Mathf.Sin(i * Mathf.Tau / 16f), 0, Mathf.Cos(i * Mathf.Tau / 16f));
			var q = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * 1.2f, from + Vector3.Up * 1.2f + d * 40f, 1);
			var hit = _p.GetWorld3D().DirectSpaceState.IntersectRay(q);
			float dist = hit.Count > 0 ? from.DistanceTo((Vector3)hit["position"]) : 40f;
			if (dist >= want && dist < 25f) return d;
			if (dist > bd) { bd = dist; best = d; }
		}
		return best;
	}

	private Vector3 WallFrom(Vector3 from, Vector3 dir, float max)
	{
		var q = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * 1.5f, from + Vector3.Up * 1.5f + dir * max, 1);
		var hit = _p.GetWorld3D().DirectSpaceState.IntersectRay(q);
		return hit.Count > 0 ? (Vector3)hit["position"] - dir * 0.2f : from + dir * 8f;
	}

	private async System.Threading.Tasks.Task Pose(Vector3 at, Vector3 look, double s)
	{
		_p.Teleport(at, 0f);
		double t = 0;
		Vector3? pin = null;
		while (t < s)
		{
			await Frame();
			t += GetProcessDeltaTime();
			if (pin is Vector3 pp) { _p.GlobalPosition = pp; _p.Velocity = Vector3.Zero; }
			else if (t > 0.6 && _p.IsOnFloor()) pin = _p.GlobalPosition;
			Look(look);
		}
	}

	private async System.Threading.Tasks.Task Aim(Vector3 look, double s)
	{
		double t = 0;
		while (t < s) { await Frame(); t += GetProcessDeltaTime(); Look(look); }
	}

	private void Keep(Vector3 pos, Vector3 look) { _p.GlobalPosition = pos; _p.Velocity = Vector3.Zero; Look(look); }

	private void Look(Vector3 at)
	{
		var eye = _p.CameraRig.Camera.GlobalPosition;
		var d = at - eye;
		_p.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
		_p.CameraRig.SetPitch(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
	}

	private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

	private async System.Threading.Tasks.Task Wait(double s)
	{
		double t = 0;
		while (t < s) { await Frame(); t += GetProcessDeltaTime(); }
	}

	private void Save(string label)
	{
		var img = GetViewport().GetTexture().GetImage();
		if (img.GetWidth() > 1280) img.Resize(1280, img.GetHeight() * 1280 / img.GetWidth(), Image.Interpolation.Bilinear);
		img.SavePng(ProjectSettings.GlobalizePath($"{_out}/{label}.png"));
		if (_ft?.Stream != null)
		{
			int vis = 0;
			string first = "";
			foreach (var n in _ft.Stream.GetChildren()) if (n is Decal d && d.Visible) { vis++; if (first == "") first = $"{d.GlobalPosition} a{d.Modulate.A:0.00} e{d.EmissionEnergy:0.00} size {d.Size} up {d.GlobalBasis.Y}"; }
			GD.Print($"[flame]   scorch {vis} visible, burning {_ft.Stream.Burning}; {first}");
		}
		GD.Print($"[flame] {label}  fps {Engine.GetFramesPerSecond():0}  alight {_ft?.Stream?.Alight ?? 0}  prims {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)}");
	}
}
