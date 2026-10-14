using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;

namespace ProjectDS.World;

/// <summary>
/// Dev harness for gap_audit.tscn (2026-10-13): runs <see cref="GapAudit"/> over the modelled pieces one at a time and the
/// creatures in their poses (walking, lunging, crouched, leaping, peeking), and photographs each near miss up close under
/// a flat light. Reports and shots to test-output/gaps/. Not used by the game.
///
///     Godot --path . --resolution 1280x720 res://scenes/levels/gap_audit.tscn -- [--models] [--creatures] [--lodge] [--shots=N]
///
/// With no flag it runs all three. <c>--shots=N</c> photographs the N biggest near misses of each scan (default 6).
/// </summary>
public partial class GapAuditPreview : Node3D
{
	private Camera3D _cam;
	private OmniLight3D _lamp;
	private string _out;
	private int _shots = 6;
	private readonly List<string> _summary = new();

	public override void _Ready()
	{
		_out = ProjectSettings.GlobalizePath("res://test-output/gaps");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		AddChild(new WorldEnvironment { Environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.32f, 0.34f, 0.37f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.8f, 0.8f, 0.82f), AmbientLightEnergy = 1.0f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
		} });
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), LightEnergy = 1.0f, ShadowEnabled = true });
		_cam = new Camera3D { Fov = 55f, Near = 0.01f, Far = 400f, Current = true };
		AddChild(_cam);
		_lamp = new OmniLight3D { LightEnergy = 1.2f, OmniRange = 6f };
		AddChild(_lamp);
		foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--shots=")) int.TryParse(a[8..], out _shots);
		_ = Run();
	}

	private static bool Has(string flag) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;
	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

	private async Task Look(string name, Vector3 eye, Vector3 at)
	{
		if (DisplayServer.GetName() == "headless") return;
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(at - eye, Mathf.Abs((at - eye).Normalized().Y) > 0.98f ? Vector3.Forward : Vector3.Up), eye);
		_lamp.GlobalPosition = eye;
		await Frames(3);
		GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
	}

	/// <summary>Scans <paramref name="root"/>, photographs its biggest near misses, adds a line to the summary.</summary>
	private async Task Audit(Node root, string tag, bool freeze = false)
	{
		var found = GapAudit.Scan(root, tag);
		_summary.Add($"{tag}: {found.Count}");
		if (freeze && root is Node3D n3) n3.ProcessMode = ProcessModeEnum.Disabled;
		int i = 0;
		foreach (var f in found.Take(_shots))
		{
			var c = f.Piece.Box.GetCenter();
			float r = Mathf.Clamp(f.Piece.Box.Size.Length() * 1.4f, 0.18f, 2.5f);
			await Look($"{tag}_{i++:00}", c + new Vector3(0.55f, 0.35f, 0.75f).Normalized() * r, c);
		}
		if (freeze && root is Node3D n4) n4.ProcessMode = ProcessModeEnum.Inherit;
	}

	private static readonly string[] JointBones = { "head", "neck2", "neck", "clav_L", "upper_L", "fore_L", "hand_L", "hips", "thigh_L", "shin_L", "foot_L", "toe_L" };

	/// <summary>Close on each joint of a creature's skeleton (its left side, the neck, the hips), from the front-side and from
	/// behind-side, <paramref name="dist"/> away: the joints a box test can miss (a limb's box always overlaps its neighbour's).</summary>
	private async Task JointShots(Node root, string tag, float dist)
	{
		if (root.FindChildren("*", "Skeleton3D", true, false).FirstOrDefault() is not Skeleton3D sk) return;
		if (root is Node3D n3) n3.ProcessMode = ProcessModeEnum.Disabled;
		// plain shaded clay, so the forms read (the creatures' own skins are near black)
		var clay = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.6f, 0.58f), Roughness = 0.8f };
		foreach (var m in root.FindChildren("*", "MeshInstance3D", true, false)) if (m is MeshInstance3D mi) mi.MaterialOverride = clay;
		var fwd = (root as Node3D)?.GlobalBasis.Z.Normalized() ?? Vector3.Back;
		var side = fwd.Cross(Vector3.Up).Normalized();
		foreach (var name in JointBones)
		{
			int b = sk.FindBone(name);
			if (b < 0) continue;
			var at = sk.ToGlobal(sk.GetBoneGlobalPose(b).Origin);
			if (Has("--joint-islands"))
				foreach (var isl in GapAudit.Islands(root).Where(i => i.Box.GetCenter().DistanceTo(at) < 0.15f))
					GD.Print($"[gap-audit] {tag} {name}: island {isl.Surface} at {isl.Box.GetCenter() - at:0.000} size {isl.Box.Size:0.000} tris {isl.Tris}");
			at += Vector3.One * 0f;
			await Look($"joint_{tag}_{name}_a", at + (fwd * 0.8f - side * 0.9f + Vector3.Up * 0.25f).Normalized() * dist, at);
			await Look($"joint_{tag}_{name}_b", at + (-fwd * 0.7f - side * 0.9f + Vector3.Up * 0.3f).Normalized() * dist, at);
		}
		if (root is Node3D n4) n4.ProcessMode = ProcessModeEnum.Inherit;
	}

	private async Task Joints()
	{
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40, 40) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.4f, 0.4f) } });
		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.3f, 0.3f, 0.32f));
		skin.SetShaderParameter("face_tint", new Color(0.4f, 0.4f, 0.42f));
		var b = new StalkerBody { Name = "Stalker", Size = 1.1f, Skin = skin };
		AddChild(b);
		await Seconds(1.0);
		await JointShots(b, "stalker_idle", 0.45f);
		float ph = 0f;
		for (int i = 0; i < 48; i++) { ph += Mathf.Pi * 1.6f / 60f; b.WalkPhase = ph; await Frames(1); }
		await JointShots(b, "stalker_walk", 0.45f);
		b.WalkPhase = -1f;
		b.Play("shove", 0.1f);
		await Seconds(0.3);
		await JointShots(b, "stalker_shove", 0.45f);
		b.QueueFree();
		var floor = new StaticBody3D { Name = "Floor" };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 1, 40) }, Position = new Vector3(0, -0.5f, 0) });
		AddChild(floor);
		var crawler = new Crawler { Name = "Crawler" };
		AddChild(crawler);
		for (int i = 0; i < 90; i++) { crawler.Hurry = 0.8f; crawler.Place(new Vector3(0, 0, 3f - i * 0.03f), Vector3.Forward); await Frames(1); }
		await JointShots(crawler, "crawler_walk", 0.4f);
		crawler.QueueFree();
		floor.QueueFree();
		var w = new Wendigo { Name = "Wendigo" };
		AddChild(w);
		w.StandAt(Vector3.Zero, new Vector3(0, 0, -10));
		await Seconds(1.5);
		await JointShots(w, "wendigo_stand", 0.8f);
		w.Hold("crouch", 1f);
		await Seconds(0.5);
		await JointShots(w, "wendigo_crouch", 0.8f);
		w.QueueFree();
	}

	/// <summary>Contact sheets of every shot whose name starts with <paramref name="prefix"/>: twelve to a sheet, labelled by
	/// order (sheet_&lt;prefix&gt;_N.png; the order is the file names', listed in sheet_&lt;prefix&gt;.txt).</summary>
	private void Sheets(string prefix)
	{
		var files = System.IO.Directory.GetFiles(_out, prefix + "*.png").Where(f => !System.IO.Path.GetFileName(f).StartsWith("sheet_")).OrderBy(f => f).ToList();
		System.IO.File.WriteAllLines($"{_out}/sheet_{prefix}.txt", files.Select((f, i) => $"{i / 12}:{i % 12} {System.IO.Path.GetFileName(f)}"));
		for (int s = 0; s * 12 < files.Count; s++)
		{
			var sheet = Image.CreateEmpty(640 * 4, 360 * 3, false, Image.Format.Rgba8);
			for (int k = 0; k < 12 && s * 12 + k < files.Count; k++)
			{
				var img = Image.LoadFromFile(files[s * 12 + k]);
				img.Convert(Image.Format.Rgba8);
				img.Resize(640, 360, Image.Interpolation.Bilinear);
				sheet.BlitRect(img, new Rect2I(0, 0, 640, 360), new Vector2I(k % 4 * 640, k / 4 * 360));
			}
			sheet.SavePng($"{_out}/sheet_{prefix}_{s}.png");
		}
	}

	private async Task Run()
	{
		await Frames(5);
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--sheet=")) { Sheets(a[8..]); GetTree().Quit(); return; }
		if (Has("--joints")) { await Joints(); GetTree().Quit(); return; }
		bool all = !Has("--models") && !Has("--creatures") && !Has("--lodge");
		GapAudit.Precise = Has("--precise");
		if (all || Has("--models")) await Models();
		if (all || Has("--creatures")) await Creatures();
		if (all || Has("--lodge")) await Lodge();
		System.IO.File.WriteAllText($"{_out}/summary.txt", string.Join("\n", _summary) + "\n");
		GD.Print("[gap-audit] done\n" + string.Join("\n", _summary));
		GetTree().Quit();
	}

	/// <summary>Every .glb under assets/models, alone at the origin (the skinned ones in their rest pose).</summary>
	private async Task Models()
	{
		foreach (var dir in new[] { "furniture", "winter", "player", "wendigo", "stalker", "creatures", "crawler" })
		{
			var d = DirAccess.Open($"res://assets/models/{dir}");
			if (d == null) continue;
			foreach (var file in d.GetFiles().Where(f => f.EndsWith(".glb")).OrderBy(f => f))
			{
				var scene = GD.Load<PackedScene>($"res://assets/models/{dir}/{file}")?.Instantiate();
				if (scene == null) continue;
				AddChild(scene);
				await Frames(2);
				await Audit(scene, $"model_{dir}_{file[..^4]}");
				scene.QueueFree();
				await Frames(1);
			}
		}
	}

	private async Task Creatures()
	{
		var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40, 40) }, Position = new Vector3(0, -0.002f, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.4f, 0.4f) } };
		AddChild(ground);
		// the ground isn't a part of anything: kept out of the scans by scanning the creature alone
		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.3f, 0.3f, 0.32f));
		skin.SetShaderParameter("face_tint", new Color(0.4f, 0.4f, 0.42f));
		var b = new StalkerBody { Name = "Stalker", Size = 1.1f, Skin = skin };
		AddChild(b);
		await Seconds(1.0);
		await Audit(b, "stalker_idle");
		await Look("stalker_idle_overview", new Vector3(1.5f, 1.6f, 3.2f), new Vector3(0, 1.1f, 0));
		float ph = 0f;
		for (int i = 0; i < 80; i++)
		{
			ph += Mathf.Pi * 1.6f / 60f;
			b.WalkPhase = ph;
			await Frames(1);
			if (i is 30 or 48 or 66) await Audit(b, $"stalker_walk_{i}", true);
		}
		b.WalkPhase = -1f;
		b.Play("shove", 0.1f);
		await Seconds(0.3);
		await Audit(b, "stalker_shove", true);
		await Seconds(1.2);
		b.Snap("loom");
		await Seconds(0.3);
		await Audit(b, "stalker_loom", true);
		b.Rest();
		b.Peek(Vector3.Right, new Vector3(0.15f, 1.85f, 0.95f));
		await Seconds(0.8);
		await Audit(b, "stalker_peek", true);
		b.Peek(Vector3.Right, new Vector3(0.2f, 0.95f, 0.95f), StalkerBody.PeekKind.Low);
		await Seconds(1.0);
		await Audit(b, "stalker_peek_low", true);
		b.QueueFree();

		var crawler = new Crawler { Name = "Crawler" };
		var floor = new StaticBody3D { Name = "Floor" };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 1, 40) }, Position = new Vector3(0, -0.5f, 0) });
		AddChild(floor);
		AddChild(crawler);
		for (int i = 0; i < 120; i++)
		{
			crawler.Hurry = i > 60 ? 0.8f : 0.2f;
			crawler.Place(new Vector3(0, 0, 3f - i * 0.03f), Vector3.Forward);
			await Frames(1);
			if (i is 40 or 55 or 90 or 105) await Audit(crawler, $"crawler_walk_{i}", true);
		}
		crawler.QueueFree();
		floor.QueueFree();

		var w = new Wendigo { Name = "Wendigo" };
		AddChild(w);
		w.StandAt(Vector3.Zero, new Vector3(0, 0, -10));
		await Seconds(1.5);
		await Audit(w, "wendigo_stand", true);
		await Look("wendigo_overview", new Vector3(3.5f, 2.8f, -4.5f), new Vector3(0, 2.2f, 0));
		w.Leap(w.GlobalPosition + new Vector3(0, 0, 0.01f), 4f, null);
		await Seconds(0.9);
		await Audit(w, "wendigo_leap", true);
		w.StandAt(Vector3.Zero, new Vector3(0, 0, -10));
		w.Hold("crouch", 1f);
		await Seconds(0.5);
		await Audit(w, "wendigo_crouch", true);
		w.Hold("air", 0.45f);
		await Seconds(0.5);
		await Audit(w, "wendigo_air", true);
		w.QueueFree();

		var deer = new Deer { Name = "Deer", SpookRadius = 0f, RunSpookRadius = 0f, ShutterSpookRadius = 0f };
		AddChild(deer);
		await Seconds(1.0);
		await Audit(deer, "deer_graze", true);
		deer.Spook();
		await Seconds(0.9);
		await Audit(deer, "deer_bound", true);
		deer.QueueFree();
		ground.QueueFree();
	}

	private async Task Lodge()
	{
		var lodge = new SkiLodge { Name = "Lodge" };
		AddChild(lodge);
		await Seconds(2.0);
		await Audit(lodge, "lodge");
		// the joints pass's fixes, up close: the chains on the doors, the bare bulbs on their flex, the round table's glasses
		if (lodge.FrontChain != null)
		{
			var c = lodge.FrontChain.GlobalPosition;
			await Look("lodge_fix_front_chain", c + new Vector3(0.25f, 0.15f, -0.9f), c + new Vector3(0, 0.02f, -0.09f));
		}
		if (lodge.PorchDoors != null)
		{
			var c = lodge.PorchDoors.ToGlobal(lodge.PorchDoors.GetAabb().GetCenter());
			var at = new Vector3(0, c.Y + 0.05f, lodge.PorchDoors.ToGlobal(lodge.PorchDoors.GetAabb().End).Z);
			await Look("lodge_fix_porch_chain", at + new Vector3(0.3f, 0.15f, 0.8f), at);
		}
		await Look("lodge_fix_pantry_bulb", lodge.ToGlobal(new Vector3(-14.4f, 2.5f, -5.6f)), lodge.ToGlobal(new Vector3(-15f, 2.9f, -6.4f)));
		await Look("lodge_fix_service_bulb", lodge.ToGlobal(new Vector3(-27.0f, 2.45f, 0.1f)), lodge.ToGlobal(new Vector3(-27.6f, 2.85f, -0.7f)));
		await Look("lodge_fix_round_table", lodge.ToGlobal(new Vector3(2.4f, 1.15f, 4.6f)), lodge.ToGlobal(new Vector3(1.6f, 0.8f, 3.7f)));
		lodge.QueueFree();
	}
}
