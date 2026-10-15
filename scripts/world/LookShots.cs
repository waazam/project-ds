using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The look audit's camera (dev only). Run as the main scene with the test save slots:
/// `godot --path . res://scenes/levels/look_shots.tscn -- --autotest --look-act=N [--look-out=dir] [--look-wait=8]
///  [--look-pos="x,y,z,yawDeg,pitchDeg;..."] [--look-turns=4]`.
/// Writes act N's save (the story test's own state for that act), Continues into it (Act 1: a new game),
/// keeps the story test out of it, waits, then photographs the view: from the spawn at --look-turns headings,
/// then from each --look-pos. Shots go to test-output/look/&lt;dir&gt;/. Quits when done.
/// </summary>
public partial class LookShots : Node
{
	private int _act = 1, _turns = 4;
	private float _wait = 8f;
	private string _out = "res://test-output/look";
	private readonly List<(Vector3 p, float yaw, float pitch)> _poses = new();

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		string dir = "";
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			string V(string k) => a[k.Length..];
			if (a.StartsWith("--look-act=")) _act = int.Parse(V("--look-act="));
			else if (a.StartsWith("--look-out=")) dir = V("--look-out=");
			else if (a.StartsWith("--look-wait=")) _wait = float.Parse(V("--look-wait="), CultureInfo.InvariantCulture);
			else if (a.StartsWith("--look-turns=")) _turns = int.Parse(V("--look-turns="));
			else if (a.StartsWith("--look-pos="))
				foreach (var pose in V("--look-pos=").Split(';', StringSplitOptions.RemoveEmptyEntries))
				{
					var f = Array.ConvertAll(pose.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
					_poses.Add((new Vector3(f[0], f[1], f[2]), f.Length > 3 ? f[3] : 0f, f.Length > 4 ? f[4] : 0f));
				}
		}
		_out += "/" + (dir == "" ? $"act{_act:00}" : dir);
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_out));
		using (FileAccess.Open("res://test-output/look/.gdignore", FileAccess.ModeFlags.Write)) { }
		// the test save slots (--autotest) without the story test: GameFlow is told it isn't a test while it sets up
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
		// survive the level change
		var root = GetTree().Root;
		GetParent().RemoveChild(this);
		root.AddChild(this);
		if (_act <= 1) StoryManager.Instance.StartNewGame();
		else
		{
			var m = typeof(StoryTest).GetMethod("StateFor", BindingFlags.NonPublic | BindingFlags.Static);
			var st = ((Checkpoint cp, string[] flags, string inventory))m.Invoke(null, new object[] { _act });
			SaveSystem.Save(new SaveData { Checkpoint = st.cp, Flags = st.flags, Inventory = st.inventory });
			StoryManager.Instance.ContinueGame();
		}
		Run();
	}

	public override void _Process(double delta)
	{
		// the story test isn't wanted here (GameFlow adds it under --autotest)
		foreach (var n in GetTree().Root.FindChildren("*", nameof(StoryTest), true, false)) n.QueueFree();
	}

	private async void Run()
	{
		var tree = GetTree();
		PlayerController p = null;
		double t = 0;
		while (t < 90)
		{
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			t += GetProcessDeltaTime();
			p = tree.GetFirstNodeInGroup("player") as PlayerController;
			if (p != null && GameFlow.Instance is { Started: true }) break;
		}
		if (p == null) { GD.PrintErr("[look] no player"); tree.Quit(2); return; }
		p.PlayerInput.Scripted = true;
		await Wait(_wait);
		var start = p.GlobalPosition;
		float yaw0 = p.CameraRig.Yaw;
		GD.Print($"[look] act {_act} at {start} yaw {Mathf.RadToDeg(yaw0):0}");
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-noshafts") >= 0 && FindAtmo() is { } atS) atS.ShaftScale = 0f;
		if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-nosky") >= 0 && GetViewport().World3D?.Environment is { } envS) { envS.BackgroundMode = Godot.Environment.BGMode.Color; envS.BackgroundColor = Colors.Black; }
		if (GetViewport().World3D?.Environment is { } env0)
			GD.Print($"[look]   sky energy {env0.BackgroundEnergyMultiplier:0.00} exposure {env0.TonemapExposure:0.00} fog {env0.FogMode} sky-fog {env0.FogSkyAffect:0.00} horizon {(env0.Sky?.SkyMaterial as ShaderMaterial)?.GetShaderParameter("horizon_color")}");
		int shot = 0;
		for (int i = 0; i < _turns; i++)
		{
			_pin = null;
			p.Teleport(start, yaw0 + Mathf.Tau * i / _turns);
			p.CameraRig.SetPitch(0f);
			await Hold(p, yaw0 + Mathf.Tau * i / _turns, 0f, 1.2f);
			Save($"{shot++:00}_spawn_{i * 360 / Math.Max(1, _turns):000}");
		}
		// --look-rel=NodeName: the poses are in that node's own frame (a building's interior)
		Transform3D rel = Transform3D.Identity;
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--look-rel=") && tree.Root.FindChild(a["--look-rel=".Length..], true, false) is Node3D rn)
			{
				rel = rn.GlobalTransform.Orthonormalized();
				GD.Print($"[look] relative to {rn.Name} at {rn.GlobalPosition}");
			}
		float relYaw = rel.Basis.GetEuler().Y;
		foreach (var (lpos, lyaw, pitch) in _poses)
		{
			var pos = rel * lpos;
			float yaw = lyaw + Mathf.RadToDeg(relYaw);
			_pin = null;
			p.Teleport(pos, Mathf.DegToRad(yaw));
			p.CameraRig.SetPitch(Mathf.DegToRad(pitch));
			await Hold(p, Mathf.DegToRad(yaw), Mathf.DegToRad(pitch), 2.5f);
			Save($"{shot++:00}_pos_{pos.X:0}_{pos.Z:0}_{yaw:0}");
			if (_ab)
			{
				SetTreeLook(false);
				await Hold(p, Mathf.DegToRad(yaw), Mathf.DegToRad(pitch), 0.4f);
				Save($"{shot - 1:00}_pos_{pos.X:0}_{pos.Z:0}_{yaw:0}_OFF");
				SetTreeLook(true);
			}
			if (FindAtmo() is { } at0)
				GD.Print($"[look]   space {Audio.AudioDirector.CurrentSpace?.ToString() ?? "open"} enclosed {at0.Enclosed:0.00} under {at0.Underground:0.00} interior {at0.Interior:0.00} lodge {at0.Lodge:0.00} banks {at0.FogBanksOn} vol {at0.GetViewport().World3D?.Environment?.VolumetricFogDensity ?? -1f:0.0000}");
			else GD.Print("[look]   no ForestAtmosphere");
			if (_fogAb && FindAtmo() is { } atmo)
			{
				atmo.EnclosedClearsFog = false;
				await Hold(p, Mathf.DegToRad(yaw), Mathf.DegToRad(pitch), 2.5f);
				Save($"{shot - 1:00}_pos_{pos.X:0}_{pos.Z:0}_{yaw:0}_OFF");
				atmo.EnclosedClearsFog = true;
			}
		}
		GD.Print("[look] done");
		tree.Quit(0);
	}

	private Vector3? _pin;
	private bool _fogAb => Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-fogab") >= 0;
	private ForestAtmosphere FindAtmo()
	{
		foreach (var n in GetTree().Root.FindChildren("*", nameof(ForestAtmosphere), true, false)) return n as ForestAtmosphere;
		return null;
	}
	private bool _ab => Array.IndexOf(OS.GetCmdlineUserArgs(), "--look-ab") >= 0;

	/// <summary>A/B: the 2026-10-13 tree shading on or off (the trunks' roundness, the crowns' depth).</summary>
	private static void SetTreeLook(bool on)
	{
		var bark = ProcTextures.TreeBarkMat;
		bark.SetShaderParameter("round_shade", on ? 0.62f : 0f);
		bark.SetShaderParameter("under_shade", on ? 0.35f : 0f);
		bark.SetShaderParameter("cavity", on ? 0.3f : 0f);
		bark.SetShaderParameter("tint", on ? new Color(1f, 0.88f, 0.77f) : new Color(0.6f, 0.53f, 0.47f));
		bark.SetShaderParameter("light_falloff", on ? 1.6f : 1f);
		bark.SetShaderParameter("light_cavity", on ? 0.5f : 0f);
		bark.SetShaderParameter("light_gain", on ? 0.7f : 1f);
		var fir = ProcTextures.FirBranchMat;
		fir.SetShaderParameter("inner_ao", on ? 0.55f : 0f);
		fir.SetShaderParameter("translucency", on ? 0.3f : 0f);
	}

	private async System.Threading.Tasks.Task Hold(PlayerController p, float yaw, float pitch, float s)
	{
		double t = 0;
		while (t < s)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			t += GetProcessDeltaTime();
			// settle on the ground for a moment, then stay put (A/B shots must frame the same thing)
			if (_pin is Vector3 pin) { p.GlobalPosition = pin; p.Velocity = Vector3.Zero; }
			else if (t > 0.8 && p.IsOnFloor()) _pin = p.GlobalPosition;
			p.CameraRig.SnapBehind(yaw);
			p.CameraRig.SetPitch(pitch);
		}
	}

	private async System.Threading.Tasks.Task Wait(float s)
	{
		double t = 0;
		while (t < s) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); t += GetProcessDeltaTime(); }
	}

	private void Save(string label)
	{
		var img = GetViewport().GetTexture().GetImage();
		if (img.GetWidth() > 1280) img.Resize(1280, img.GetHeight() * 1280 / img.GetWidth(), Image.Interpolation.Bilinear);
		img.SavePng(ProjectSettings.GlobalizePath($"{_out}/{label}.png"));
		GD.Print($"[look] {label}  fps {Engine.GetFramesPerSecond():0}  prims {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)}");
	}
}
