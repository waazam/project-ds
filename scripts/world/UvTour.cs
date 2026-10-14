using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev harness (not used by the game): `-- --start-act=13 --uv-tour[=name]` walks a camera round the blacklight's
/// secrets with the beam on and screenshots them into test-output/uvtour/ (the Hollow's trees, the camp, the cabin,
/// the bunker, the station, the library's book, the lodge's bar), then audits every ink mark: each is lit alone, head
/// on, and the ink that actually lands on a surface is counted against what the mark should show (a mark on nothing
/// shows nothing). The report goes to test-output/uvtour/audit_[name].txt. Added by <see cref="BlacklightSecrets"/>.
/// </summary>
public partial class UvTour : Node3D
{
	private string _name = "shot";
	private string _out;
	private Camera3D _cam;
	private SpotLight3D _beam;
	private OmniLight3D _glow;
	private bool _uvOn = true;
	private readonly List<string> _report = new();

	public static bool Requested(out string name)
	{
		name = null;
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a == "--uv-tour") { name = "shot"; return true; }
			if (a.StartsWith("--uv-tour=")) { name = a["--uv-tour=".Length..]; return true; }
		}
		return false;
	}

	public override void _Ready()
	{
		Requested(out _name);
		_out = ProjectSettings.GlobalizePath("res://test-output/uvtour");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		ProcessMode = ProcessModeEnum.Always;
		_ = Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

	public override void _Process(double delta)
	{
		if (_cam == null) return;
		RenderingServer.GlobalShaderParameterSet("uv_light_on", _uvOn ? 1f : 0f);
		RenderingServer.GlobalShaderParameterSet("uv_light_pos", _cam.GlobalPosition);
		RenderingServer.GlobalShaderParameterSet("uv_light_dir", -_cam.GlobalBasis.Z);
		RenderingServer.GlobalShaderParameterSet("uv_light_range", 13f);
	}

	private static T Find<T>(Node n) where T : class
	{
		if (n is T t) return t;
		foreach (var c in n.GetChildren()) if (Find<T>(c) is { } f) return f;
		return null;
	}

	private async Task Run()
	{
		await Frames(30);
		// the whole game's ink: the station and the winter woods (with the lodge) built now if they wait
		var st = StationInterior.Instance;
		st?.EnsureBuilt();
		WinterWoods.Instance?.EnsureBuilt();
		for (int i = 0; i < 900; i++)
		{
			await Frames(1);
			if (st == null || st.Boss?.Library?.Round?.Dais != null) break;
		}
		await Frames(240);
		// the player out of it: our own eye and beam (like the lantern's: the violet cone and a little glow round you)
		foreach (var p in GetTree().GetNodesInGroup("player")) if (p is Node pn) pn.ProcessMode = ProcessModeEnum.Disabled;
		_cam = new Camera3D { Fov = 70f, Near = 0.03f, Far = 300f };
		AddChild(_cam);
		_cam.MakeCurrent();
		_beam = new SpotLight3D { LightColor = new Color(0.38f, 0.12f, 1.0f), LightEnergy = 4.2f, SpotRange = 13f, SpotAngle = 24f, ShadowEnabled = false };
		_cam.AddChild(_beam);
		_glow = new OmniLight3D { LightColor = new Color(0.62f, 0.5f, 1.0f), LightEnergy = 1.1f, OmniRange = 10f, ShadowEnabled = false };
		_cam.AddChild(_glow);
		await Frames(20);

		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--uv-no-sites") < 0) await Sites();
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--uv-audit") >= 0) await Audit();
		GD.Print("[uvtour] done");
		GetTree().Quit();
	}

	private async Task Shot(string name, Vector3 eye, Vector3 at, int settle = 6)
	{
		Vector3 d = at - eye;
		Vector3 up = Mathf.Abs(d.Normalized().Y) > 0.97f ? Vector3.Forward : Vector3.Up;
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(d, up), eye);
		await Frames(settle);
		GetViewport().GetTexture().GetImage().SavePng($"{_out}/{_name}_{name}.png");
		GD.Print($"[uvtour] {name}");
	}

	private async Task Sites()
	{
		var scene = GetTree().CurrentScene;
		// the Hollow: the first number's tree and the decoys round it, then one close
		if (Find<SurveyLot>(scene) is { } lot)
		{
			for (int i = 0; i < lot.Notes.Count; i += 2)
			{
				var n = lot.Notes[i];
				Vector3 at = n.GlobalPosition + Vector3.Up * 1.4f;
				Vector3 face = n.GlobalBasis.Z with { Y = 0 };
				await Shot($"tree{i + 1}_wide", at + face.Normalized() * 7f + Vector3.Up * 0.6f, at);
				await Shot($"tree{i + 1}_close", n.PlateWorld + (n.PlateWorld - (n.GlobalPosition + Vector3.Up * n.NoteHeight)).Normalized() * 1.6f, n.PlateWorld);
			}
			if (lot.Decoys.Count > 1)
			{
				var dcy = lot.Decoys[1];
				Vector3 at = dcy.GlobalPosition + Vector3.Up * 1.3f;
				Vector3 face = dcy.GlobalBasis.Z with { Y = 0 };
				await Shot("decoy_close", at + face.Normalized() * 1.8f + Vector3.Up * 0.1f, at);
				await Shot("decoy_close_b", at + (Basis.FromEuler(new Vector3(0, 1.2f, 0)) * face).Normalized() * 1.8f + Vector3.Up * 0.1f, at);
			}
		}
		if (scene?.FindChild("Camp", true, false) is Camp camp)
		{
			Vector3 s = camp.ToGlobal(camp.StumpLocal + new Vector3(0, 0.3f, 0));
			await Shot("camp_stump", s + camp.GlobalBasis.Z * 1.3f + Vector3.Up * 0.4f, s);
		}
		if (GetTree().GetFirstNodeInGroup("cabin") is Cabin cabin)
		{
			Vector3 mid = cabin.ToGlobal(new Vector3(0, 1.5f, 0));
			await Shot("cabin_back", cabin.ToGlobal(new Vector3(0, 1.5f, cabin.Depth * 0.5f - 0.5f)), cabin.ToGlobal(new Vector3(-0.6f, 1.3f, -cabin.Depth * 0.5f)));
			await Shot("cabin_door", cabin.ToGlobal(new Vector3(0, 1.3f, -0.3f)), cabin.ToGlobal(new Vector3(0, 0.85f, cabin.Depth * 0.5f)));
		}
		if (BunkerInterior.Instance is Node3D bi)
			await Shot("bunker_hall", bi.ToGlobal(new Vector3(-0.3f, 1.5f, -38f)), bi.ToGlobal(new Vector3(BunkerParts.BunkerLayout.HallHalfWidth, 1.05f, -47f)));
		if (StationInterior.Instance is { } st)
		{
			if (st.Room1 is { } r1) await Shot("station_room1_ceiling", r1.ToGlobal(new Vector3(0.3f, 1.4f, 1.6f)), r1.ToGlobal(new Vector3(0, StationRoom1.Height, -0.4f)));
			if (st.Room2 is { } r2) await Shot("station_room2_ceiling", r2.ToGlobal(new Vector3(0, 1.4f, 1.8f)), r2.ToGlobal(new Vector3(0, StationRoom2.Height, 0.2f)));
		}
		// the ones on props: the library's book, the lodge's bar, the stairwell's rail (by their marks)
		foreach (var m in UvInk.Marks.Where(m => IsInstanceValid(m.Node)))
		{
			string path = m.Node.GetPath().ToString();
			string tag = path.Contains("JuttingBook") ? "library_book" : path.Contains("LibraryJournal") || path.Contains("Journal") ? "library_journal"
				: path.Contains("SkiLodge") ? "lodge" : path.Contains("Stairwell") ? "stairwell" : null;
			if (tag == null || _shotTags.Contains(tag)) continue;
			_shotTags.Add(tag);
			// a place the story hasn't shown yet is hidden: show it (and everything up to it) for the picture
			for (Node a = m.Node; a != null; a = a.GetParent()) if (a is Node3D a3) a3.Visible = true;
			var (c, n) = m.World();
			await Shot($"{tag}_close", c + n * Mathf.Max(0.45f, m.Size * 3f) + Vector3.Up * 0.05f, c);
			await Shot($"{tag}_wide", c + n * 1.6f + Vector3.Up * 0.35f + n.Cross(Vector3.Up).Normalized() * 0.5f, c);
		}
	}

	private readonly HashSet<string> _shotTags = new();

	/// <summary>Every mark, lit alone and head on: the ink that lands on a surface, against what it should show.</summary>
	private async Task Audit()
	{
		var marks = UvInk.Marks.Where(m => IsInstanceValid(m.Node) && m.Node.IsInsideTree()).ToList();
		var all = marks.Select(m => m.Node).Distinct().ToList();
		_report.Add($"{marks.Count} marks");
		int bad = 0, i = 0;
		foreach (var m in marks)
		{
			i++;
			var (c, n) = m.World();
			float dist = Mathf.Clamp(m.Size * 2.2f, 0.5f, 4f);
			Vector3 eye = c + n * dist;
			Vector3 up = Mathf.Abs(n.Y) > 0.9f ? Vector3.Forward : Vector3.Up;
			_cam.GlobalTransform = new Transform3D(Basis.LookingAt(-n, up), eye);
			foreach (var o in all) o.Visible = o == m.Node;
			for (Node a = m.Node.GetParent(); a != null; a = a.GetParent()) if (a is Node3D a3) a3.Visible = true;
			_uvOn = false;
			await Frames(3);
			var off = GetViewport().GetTexture().GetImage();
			_uvOn = true;
			await Frames(3);
			var on = GetViewport().GetTexture().GetImage();
			float ink = 0f;
			int w = on.GetWidth(), h = on.GetHeight();
			for (int y = 0; y < h; y += 2)
				for (int x = 0; x < w; x += 2)
				{
					Color a = on.GetPixel(x, y), b = off.GetPixel(x, y);
					ink += Mathf.Max(0f, (a.R + a.G + a.B) - (b.R + b.G + b.B));
				}
			string path = m.Node.GetPath().ToString();
			string where = path.Length > 90 ? "..." + path[^90..] : path;
			string verdict = ink < 20f ? "NOTHING" : "ok";
			if (ink < 20f) bad++;
			// every mark, small, for looking through (the ones on nothing among them)
			DirAccess.MakeDirRecursiveAbsolute($"{_out}/{_name}_marks");
			on.Resize(640, 360, Image.Interpolation.Bilinear);
			string site = path.Contains("SurveyLot") ? "tree" : path.Contains("Camp") ? "camp" : path.Contains("Cabin") ? "cabin" : path.Contains("Bunker") ? "bunker"
				: path.Contains("SkiLodge") ? "lodge" : path.Contains("Library") ? "library" : path.Contains("Stairwell") ? "stairwell" : path.Contains("Station") ? "station" : "other";
			on.SavePng($"{_out}/{_name}_marks/{i:000}_{site}_{m.Kind}{(ink < 20f ? "_NOTHING" : "")}.png");
			_report.Add($"{i:000} {verdict,-8} ink {ink,9:0} {m.Kind,-6} at {c.X:0.0},{c.Y:0.0},{c.Z:0.0}  {where}");
		}
		foreach (var o in all) o.Visible = true;
		_report.Insert(1, $"{bad} show nothing");
		System.IO.File.WriteAllLines($"{_out}/audit_{_name}.txt", _report);
		GD.Print($"[uvtour] audit: {marks.Count} marks, {bad} show nothing");
	}
}
