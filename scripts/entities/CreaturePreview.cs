using System.Threading.Tasks;
using Godot;
using ProjectDS.World.HallwayParts;

namespace ProjectDS.Entities;

/// <summary>
/// Dev harness for creature_preview.tscn: stands the game's creatures up in a dark, lit void and
/// screenshots them into test-output/creatures/ (the lake's limbs, the pit's leviathan healthy and
/// rotting, the shadow man, the stalker), then quits. Not used by the game.
/// </summary>
public partial class CreaturePreview : Node3D
{
	private Camera3D _cam;
	private Environment env;
	private string _out;

	public override void _Ready()
	{
		_out = ProjectSettings.GlobalizePath("res://test-output/creatures");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		var env = new Environment
		{
			BackgroundMode = Environment.BGMode.Color, BackgroundColor = new Color(0.05f, 0.05f, 0.06f),
			AmbientLightSource = Environment.AmbientSource.Color, AmbientLightColor = new Color(0.35f, 0.33f, 0.36f), AmbientLightEnergy = 0.6f,
			TonemapMode = Environment.ToneMapper.Filmic,
		};
		AddChild(new WorldEnvironment { Environment = env });
		this.env = env;
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0), LightEnergy = 1.2f, ShadowEnabled = true });
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-20, -150, 0), LightEnergy = 0.4f, LightColor = new Color(0.6f, 0.75f, 0.9f) });
		_cam = new Camera3D { Fov = 60f, Near = 0.05f, Far = 800f, Current = true };
		AddChild(_cam);
		Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

	private async Task Shot(string name, Vector3 eye, Vector3 at)
	{
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);
		await Frames(4);
		GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
		GD.Print($"[creature-preview] {name}");
	}

	private async void Run()
	{
		await Frames(10);
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--wendigo") >= 0) { await WendigoShots(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--stalker") >= 0) { await StalkerShots(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--lantern") >= 0) { await LanternShots(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--crawler") >= 0) { await CrawlerShots(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--weather") >= 0) { await WeatherShots(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--shelter") >= 0) { await ShelterTest(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--lodge") >= 0) { await LodgeShots(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--prints") >= 0) { await PrintShots(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--export-bodies") >= 0) { await ExportBodies(); GetTree().Quit(); return; }
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--bench") >= 0) { await Bench(); GetTree().Quit(); return; }
		// the crawler (Act 14), on a floor, walking a few metres so its gait shows
		var floor = new StaticBody3D { Name = "Floor" };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 1, 40) }, Position = new Vector3(0, -0.5f, 0) });
		floor.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40, 40) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.21f) } });
		AddChild(floor);
		var crawler = new Crawler { Name = "Crawler" };
		AddChild(crawler);
		for (int i = 0; i < 90; i++)
		{
			crawler.Place(new Vector3(0, 0, 3f - i * 0.03f), Vector3.Forward);
			await Frames(1);
		}
		await Shot("crawler_side", new Vector3(3.2f, 1.1f, 0.3f), new Vector3(0, 0.4f, 0.3f));
		await Shot("crawler_front", new Vector3(0.6f, 0.9f, -2.6f), new Vector3(0, 0.35f, 0.3f));
		await Shot("crawler_above", new Vector3(1.8f, 3.2f, 1.8f), new Vector3(0, 0.3f, 0.3f));
		crawler.QueueFree();
		floor.QueueFree();
		// the pit's leviathan (its local y=0 is the pit floor; the body sits ~5 m up)
		var lev = new Leviathan { Name = "Leviathan", Position = new Vector3(0, 0, 0) };
		AddChild(lev);
		await Seconds(1.5);
		await Shot("leviathan_whole", new Vector3(0, 18f, 55f), new Vector3(0, 12f, 0));
		await Shot("leviathan_body", new Vector3(8f, 12f, 20f), new Vector3(0, 7f, 0));
		await Shot("leviathan_limb_root", new Vector3(12f, 10f, 14f), new Vector3(8f, 8f, 5f));
		lev.Rot = 0.6f;
		await Shot("leviathan_rot_mid", new Vector3(8f, 12f, 20f), new Vector3(0, 7f, 0));
		lev.Rot = 1f;
		await Shot("leviathan_rot_full", new Vector3(8f, 12f, 20f), new Vector3(0, 7f, 0));
		lev.QueueFree();

		// the lake's limbs
		var lake = new LakeCreature { Name = "LakeCreature", Position = new Vector3(300, 0, 0) };
		AddChild(lake);
		await Frames(2);
		lake.Breach(new Vector3(300, 0, 0), new Vector3(300, 0, 16), 30);
		await Seconds(3.5);
		lake.OpenEyes();
		await Seconds(1.0);
		await Shot("lake_limbs", new Vector3(300, 3f, 22f), new Vector3(300, 4f, 0));
		await Shot("lake_limbs_close", new Vector3(303, 2.5f, 12f), new Vector3(300, 3f, 2f));
		await Shot("lake_limbs_tips", new Vector3(300, 6f, 14f), new Vector3(300, 7.5f, 1f));

		// the shadow man
		var sm = new ShadowMan { Name = "ShadowMan", Position = new Vector3(600, 0, 0) };
		AddChild(sm);
		AddChild(new OmniLight3D { Position = new Vector3(600, 2.8f, 2f), LightColor = new Color(0.9f, 0.2f, 0.15f), LightEnergy = 2f, OmniRange = 8f });
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(8, 5) }, Position = new Vector3(600, 2.5f, -1.2f), Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.62f, 0.66f), CullMode = BaseMaterial3D.CullModeEnum.Disabled } });
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(8, 8) }, Position = new Vector3(600, 0, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.45f, 0.45f) } });
		AddChild(new OmniLight3D { Position = new Vector3(600, 2.5f, 3f), LightEnergy = 2.5f, OmniRange = 12f });
		await Seconds(1.0);
		sm.StandAt(new Vector3(600, 0, 0), new Vector3(600, 0, 10));
		await Shot("shadowman_far", new Vector3(600, 1.6f, 7f), new Vector3(600, 1.3f, 0));
		await Shot("shadowman_close", new Vector3(600, 1.7f, 2.2f), new Vector3(600, 1.6f, 0));
		await Shot("shadowman_side", new Vector3(603, 1.6f, 2.5f), new Vector3(600, 1.3f, 0));

		// the shadow man as he is met: a dark hall, dark walls either side, red light then green, near and far
		var hall = new Node3D { Position = new Vector3(1200, 0, 0) };
		AddChild(hall);
		var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.28f, 0.24f), Roughness = 0.9f };
		foreach (float x in new[] { -2.1f, 2.1f })
			hall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.3f, 8f, 60f) }, Position = new Vector3(x, 4f, 0), MaterialOverride = wallMat });
		hall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(4.2f, 0.2f, 60f) }, Position = new Vector3(0, -0.1f, 0), MaterialOverride = wallMat });
		var hallLight = new OmniLight3D { Position = new Vector3(0, 5f, 3f), LightColor = new Color(1f, 0.08f, 0.05f), LightEnergy = 1.5f, OmniRange = 22f };
		hall.AddChild(hallLight);
		var sm2 = new ShadowMan { Name = "ShadowManHall" };
		hall.AddChild(sm2);
		await Seconds(2.0);
		sm2.StandAt(new Vector3(1200, 0, -2f), new Vector3(1200, 0, 10));
		env.AmbientLightEnergy = 0.05f;
		env.BackgroundColor = Colors.Black;
		await Seconds(1.5);
		await Shot("shadowman_hall_red_far", new Vector3(1200.3f, 1.6f, 9f), new Vector3(1200, 1.4f, -2f));
		await Shot("shadowman_hall_red_near", new Vector3(1200.2f, 1.6f, 1.2f), new Vector3(1200, 1.7f, -2f));
		hallLight.LightColor = new Color(0.1f, 1f, 0.3f);
		await Seconds(0.8);
		await Shot("shadowman_hall_green_mid", new Vector3(1200.4f, 1.6f, 3.5f), new Vector3(1200, 1.5f, -2f));
		sm2.Glare = 1f;
		await Seconds(0.5);
		await Shot("shadowman_hall_taking", new Vector3(1200.1f, 1.65f, -0.8f), new Vector3(1200, 1.95f, -2f));
		sm2.Glare = 0.3f;
		env.AmbientLightEnergy = 0.6f;
		env.BackgroundColor = new Color(0.05f, 0.05f, 0.06f);

		// the stalker
		var st = new StalkerBody { Name = "Stalker", Seed = 2077, Size = 1.08f, Position = new Vector3(900, 0, 0) };
		AddChild(st);
		await Seconds(1.0);
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(8, 5) }, Position = new Vector3(900, 2.5f, -2f), Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.62f, 0.66f), CullMode = BaseMaterial3D.CullModeEnum.Disabled } });
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(8, 5) }, Position = new Vector3(898f, 2.5f, 0f), Rotation = new Vector3(Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.62f, 0.66f), CullMode = BaseMaterial3D.CullModeEnum.Disabled } });
		AddChild(new OmniLight3D { Position = new Vector3(902, 2.5f, 3f), LightEnergy = 2.5f, OmniRange = 12f });
		await Shot("stalker", new Vector3(900, 1.6f, 5f), new Vector3(900, 1.4f, 0));
		await Shot("stalker_side", new Vector3(904.5f, 1.8f, 0.2f), new Vector3(900, 1.6f, 0));
		await Shot("stalker_shoulder", new Vector3(901.6f, 2.3f, 1.4f), new Vector3(900.3f, 2.05f, 0));
		GetTree().Quit();
	}

	/// <summary>The wendigo (Act 22): standing in snow at dusk, lit low and cold, front, side, three-quarter, the head close.</summary>
	/// <summary>A controlled measurement of the optimization pass in the lodge (frame time at fixed views, vsync off, every
	/// variant in the one process so they're compared like for like): the dusty furniture against plain, occlusion
	/// culling on and off.</summary>
	private async Task Bench()
	{
		env.BackgroundColor = new Color(0.3f, 0.32f, 0.36f);
		env.AmbientLightColor = new Color(0.78f, 0.68f, 0.56f);
		env.AmbientLightEnergy = 0.42f;
		World.FurnitureKit.Dusty = true;
		var dusty = new World.SkiLodge { Name = "LodgeDusty" };
		AddChild(dusty);
		await Seconds(2.0);
		World.FurnitureKit.Dusty = false;
		var plain = new World.SkiLodge { Name = "LodgePlain" };
		AddChild(plain);
		await Seconds(2.0);
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		_cam.Fov = 75f;
		var views = new (string, Vector3, Vector3)[]
		{
			("lobby_from_door", new Vector3(0, 1.7f, 8.8f), new Vector3(0, 2.5f, -6f)),
			("lobby_seating", new Vector3(1.0f, 1.7f, 1.0f), new Vector3(7f, 1.2f, -5f)),
			("dining", new Vector3(14f, 1.7f, 0f), new Vector3(40f, 1.2f, 0f)),
			("corridor", new Vector3(-12.2f, 5.9f, 0f), new Vector3(-28f, 5.5f, 0f)),
			("bar", new Vector3(-14f, 1.7f, 4f), new Vector3(-24f, 1.4f, 4f)),
		};
		async Task<double> Ms()
		{
			await Frames(30);
			ulong t0 = Time.GetTicksUsec();
			const int n = 240;
			await Frames(n);
			return (Time.GetTicksUsec() - t0) / 1000.0 / n;
		}
		var vp = GetViewport();
		var totals = new System.Collections.Generic.Dictionary<string, double>();
		foreach (var (name, eye, at) in views)
		{
			_cam.GlobalTransform = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);
			var line = new System.Text.StringBuilder($"[bench] {name,-16}");
			foreach (var (label, useDusty, occ) in new[] { ("dusty+occ", true, true), ("dusty", true, false), ("plain+occ", false, true), ("plain", false, false) })
			{
				dusty.Visible = useDusty; plain.Visible = !useDusty;
				vp.UseOcclusionCulling = occ;
				double ms = await Ms();
				totals[label] = (totals.TryGetValue(label, out var tv) ? tv : 0) + ms;
				line.Append($"  {label} {ms:0.00} ms");
			}
			GD.Print(line.ToString());
		}
		var sum = new System.Text.StringBuilder("[bench] mean          ");
		foreach (var kv in totals) sum.Append($"  {kv.Key} {kv.Value / views.Length:0.00} ms");
		GD.Print(sum.ToString());
	}

	/// <summary>The creatures' bodies as built, written out for the Blender pass (tools/Blender/creatures.py): each part's
	/// mesh in its own pivot's space, with its tone colours, one PLY per part and material (build/bodies).</summary>
	private async Task ExportBodies()
	{
		string dir = ProjectSettings.GlobalizePath("res://build/bodies");
		System.IO.Directory.CreateDirectory(dir);
		CreatureModels.Disabled = true;
		var body = new StalkerBody { Name = "StalkerExport", Size = 1f, Idle = false };
		AddChild(body);
		var crawler = new Crawler { Name = "CrawlerExport" };
		AddChild(crawler);
		await Seconds(0.3);
		int n = 0;
		foreach (var c in body.FindChildren("*", "MeshInstance3D", true, false))
			if (c is MeshInstance3D mi && mi.HasMeta("stalker_generated")) n += WritePly(dir, "stalker_" + mi.Name, mi.Mesh);
		foreach (var name in new[] { "Torso", "Skull", "Hand", "Foot" })
			if (crawler.FindChild(name, true, false) is MeshInstance3D mi) n += WritePly(dir, "crawler_" + name, mi.Mesh);
		GD.Print($"[export] {n} body meshes written to {dir}");
	}

	private static int WritePly(string dir, string name, Mesh mesh)
	{
		int written = 0;
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			var arr = mesh.SurfaceGetArrays(s);
			var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			var nrm = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
			var col = arr[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Color].AsColorArray();
			var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
			int tris = idx != null ? idx.Length / 3 : v.Length / 3;
			var sb = new System.Text.StringBuilder();
			foreach (var h in new[] { $"ply", $"format ascii 1.0", $"element vertex {v.Length}", $"property float x", $"property float y", $"property float z", $"property float nx", $"property float ny", $"property float nz", $"property uchar red", $"property uchar green", $"property uchar blue", $"property uchar alpha", $"element face {tris}", $"property list uchar int vertex_indices", $"end_header" }) sb.Append(h).Append('\n');
			var ci = System.Globalization.CultureInfo.InvariantCulture;
			for (int i = 0; i < v.Length; i++)
			{
				var c = col != null && i < col.Length ? col[i] : Colors.White;
				var nn = i < nrm.Length ? nrm[i] : Vector3.Up;
				sb.Append(string.Format(ci, "{0} {1} {2} {3} {4} {5} {6} {7} {8} {9}\n", v[i].X, v[i].Y, v[i].Z, nn.X, nn.Y, nn.Z,
					(int)Mathf.Clamp(c.R * 255f, 0, 255), (int)Mathf.Clamp(c.G * 255f, 0, 255), (int)Mathf.Clamp(c.B * 255f, 0, 255), (int)Mathf.Clamp(c.A * 255f, 0, 255)));
			}
			for (int t = 0; t < tris; t++)
			{
				int a = idx != null ? idx[t * 3] : t * 3, b = idx != null ? idx[t * 3 + 1] : t * 3 + 1, cc = idx != null ? idx[t * 3 + 2] : t * 3 + 2;
				sb.Append($"3 {a} {b} {cc}\n");
			}
			System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"{name}_s{s}.ply"), sb.ToString());
			written++;
		}
		return written;
	}

	/// <summary>The snow prints (SnowPrints) on the snow: a boot trail and the wendigo's, by lantern light.</summary>
	private async Task PrintShots()
	{
		var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(30, 30) }, MaterialOverride = World.WinterWoods.GroundMat() };
		AddChild(ground);
		var prints = new World.SnowPrints { Name = "Prints" };
		AddChild(prints);
		for (int i = 0; i < 8; i++)
			prints.Lay(new Vector3((i % 2 == 0 ? -0.11f : 0.11f), 0, -i * 0.65f), Vector3.Up, Vector3.Forward, i % 2 == 0, false, 0.5f);
		for (int i = 0; i < 5; i++)
			prints.Lay(new Vector3(1.4f + (i % 2 == 0 ? -0.2f : 0.2f), 0, -i * 1.25f), Vector3.Up, Vector3.Forward, i % 2 == 0, true, 0.7f);
		AddChild(new OmniLight3D { LightColor = new Color(1f, 0.7f, 0.4f), LightEnergy = 1.6f, OmniRange = 9f, Position = new Vector3(0.6f, 1.6f, 1.2f) });
		await Seconds(0.5);
		await Shot("prints_trail", new Vector3(0.6f, 1.65f, 1.6f), new Vector3(0.6f, 0f, -2.5f));
	}

	/// <summary>The remodelled stalker (2026-10-02): its clips, its grip on a trunk, the edge light up close, the
	/// eyeshine, against a dim grey so its black shape reads.</summary>
	private async Task StalkerShots()
	{
		env.BackgroundColor = new Color(0.16f, 0.17f, 0.18f);
		env.AmbientLightEnergy = 0.4f;
		var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(30, 30) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.24f, 0.22f) } };
		AddChild(ground);
		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.026f, 0.028f, 0.034f));
		skin.SetShaderParameter("face_tint", new Color(0.21f, 0.215f, 0.22f));
		var b = new StalkerBody { Name = "Stalker", Size = 1.1f, Skin = skin };
		AddChild(b);
		await Frames(3);
		GD.Print($"[creature-preview] stalker rigged {b.Rigged} triangles {b.TriangleCount} clip {b.Clip}");
		var skel = b.FindChildren("*", "Skeleton3D", true, false)[0] as Skeleton3D;
		// the bones' length runs along their +Y? (the rags and the reach rely on it)
		for (int i = 0; i < skel.GetBoneCount(); i++)
		{
			string n = skel.GetBoneName(i);
			if (n is not ("upper_R" or "shin_L" or "rag3_0" or "neck")) continue;
			int child = -1;
			foreach (int c in skel.GetBoneChildren(i)) { child = c; break; }
			if (child < 0) continue;
			var g = skel.GetBoneGlobalRest(i);
			Vector3 to = (skel.GetBoneGlobalRest(child).Origin - g.Origin).Normalized();
			GD.Print($"[creature-preview] bone {n}: +Y . to-child = {g.Basis.Y.Normalized().Dot(to):0.000}");
		}
		GD.Print($"[creature-preview] eyes at {b.EyesWorld}");
		await Seconds(1.0);
		await Shot("stalker_idle_front", new Vector3(0, 1.6f, 4.2f), new Vector3(0, 1.3f, 0));
		await Shot("stalker_idle_side", new Vector3(4.2f, 1.6f, 0.5f), new Vector3(0, 1.3f, 0));
		await Shot("stalker_face_close", new Vector3(0.15f, 2.05f, 1.5f), b.EyesWorld);
		await Shot("stalker_far", new Vector3(1, 1.6f, 12f), new Vector3(0, 1.3f, 0));
		// the lantern's answer: a light at the camera, and the eyeshine set as the game would
		skin.SetShaderParameter("eyeshine", 1.8f);
		await Shot("stalker_eyeshine", new Vector3(0, 1.8f, 6f), new Vector3(0, 1.7f, 0));
		skin.SetShaderParameter("eyeshine", 0f);
		// peeking round a trunk with a hand on its edge
		var trunk = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.42f, Height = 6f }, Position = new Vector3(-0.25f, 3f, 0.95f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.24f, 0.2f) } };
		AddChild(trunk);
		Vector3 side = Vector3.Right;
		Vector3 grip = new Vector3(-0.25f + 0.36f + 0.035f, 1.85f, 0.95f);
		b.Peek(side, grip);
		await Seconds(0.6);
		await Shot("stalker_peek_front", new Vector3(-0.6f, 1.7f, 6.5f), new Vector3(0, 1.6f, 0.5f));
		await Shot("stalker_peek_close", new Vector3(0.3f, 1.9f, 3.0f), grip);
		GD.Print($"[creature-preview] grip hand {b.GripHandWorld} target {grip} off {b.GripHandWorld.DistanceTo(grip):0.000}");
		// unwatched it creeps (the neck drawn out, the head tipping); watched, its mouth opens
		Vector3 eyeAt = new(-0.6f, 1.7f, 6.5f);
		b.TrackTarget = eyeAt;
		b.Watched = false;
		await Seconds(9.5);
		await Shot("stalker_creep", eyeAt, new Vector3(0, 1.8f, 0.5f));
		await Shot("stalker_creep_close", new Vector3(0.25f, 2.0f, 2.6f), b.EyesWorld);
		b.TrackTarget = new Vector3(0.25f, 2.0f, 2.6f);
		b.Watched = true;
		await Seconds(3.0);
		await Shot("stalker_gape_close", new Vector3(0.25f, 2.0f, 2.6f), b.EyesWorld);
		GD.Print($"[creature-preview] creep {b.Creep:0.00} gape {b.Gape:0.00}");
		b.Watched = false;
		// low, at the trunk's foot
		b.Peek(side, new Vector3(-0.25f + 0.4f + 0.035f, 0.95f, 0.95f), StalkerBody.PeekKind.Low);
		b.TrackTarget = eyeAt;
		await Seconds(1.0);
		await Shot("stalker_low_peek", eyeAt, new Vector3(0, 0.8f, 0.5f));
		// clinging up the trunk, a hand on either edge
		b.Position = new Vector3(0, 1.45f, 0.25f);
		b.Peek(side, new Vector3(-0.25f + 0.36f + 0.035f, 1.45f + 2.1f, 0.95f), StalkerBody.PeekKind.Cling, new Vector3(-0.25f - 0.36f - 0.035f, 1.45f + 2.1f, 0.95f));
		await Seconds(1.0);
		await Shot("stalker_cling", new Vector3(-0.5f, 1.7f, 6.5f), new Vector3(-0.2f, 3.0f, 0.5f));
		// the owl: turned away, its head right round
		b.Position = new Vector3(0, 0, 0);
		b.Rotation = new Vector3(0, Mathf.Pi, 0);
		b.Peek(-side, null, StalkerBody.PeekKind.Owl);
		trunk.Visible = false;
		await Seconds(1.5);
		await Shot("stalker_owl", new Vector3(0.3f, 1.8f, 4.0f), new Vector3(0, 1.7f, 0));
		trunk.Visible = true;
		b.Rotation = Vector3.Zero;
		b.TrackTarget = null;
		b.Peek(side, grip);
		b.Duck();
		await Seconds(0.25);
		await Shot("stalker_duck", new Vector3(-0.6f, 1.7f, 6.5f), new Vector3(0, 1.4f, 0.5f));
		trunk.QueueFree();
		b.Rest();
		// walking
		float ph = 0f;
		for (int i = 0; i < 70; i++)
		{
			ph += Mathf.Pi * 1.6f / 60f;
			b.WalkPhase = ph;
			b.Position += new Vector3(0, 0, 1) * b.StepLength * 1.6f / 60f;
			await Frames(1);
		}
		await Shot("stalker_walk_side", b.Position + new Vector3(4.2f, 0.4f, 0), b.Position + new Vector3(0, 1.1f, 0));
		await Shot("stalker_walk_front", b.Position + new Vector3(0.5f, 1.6f, 4f), b.Position + new Vector3(0, 1.2f, 0));
		b.WalkPhase = -1f;
		b.Play("shove", 0.1f);
		await Seconds(0.3);
		await Shot("stalker_shove", b.Position + new Vector3(2.5f, 1.5f, 2.5f), b.Position + new Vector3(0, 1.4f, 0.3f));
		await Seconds(1.2);
		b.Snap("loom");
		b.GlowEyes(new Color(1f, 0.16f, 0.05f), 8f);
		env.AmbientLightEnergy = 0.05f;
		await Seconds(0.3);
		await Shot("stalker_loom_dark", b.Position + new Vector3(0.1f, 1.7f, 1.8f), b.EyesWorld);
		env.AmbientLightEnergy = 0.4f;
		b.LookTarget = b.Position + new Vector3(0, 6f, 3f);
		b.Play("stare", 0.3f);
		await Seconds(2.0);
		await Shot("stalker_stare_up", b.Position + new Vector3(2.5f, 1.2f, 2.5f), b.Position + new Vector3(0, 1.6f, 0));
	}

	/// <summary>The lantern's cage on the walls of a dark room, and its beam in the fog (the fidelity pass).</summary>
	private async Task LanternShots()
	{
		foreach (var n in GetChildren()) if (n is DirectionalLight3D d) d.Visible = false;
		env.AmbientLightEnergy = 0.02f;
		env.BackgroundColor = Colors.Black;
		var wall = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.48f, 0.45f), Roughness = 0.9f };
		void Box(Vector3 c, Vector3 size) => AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = c, MaterialOverride = wall });
		Box(new Vector3(0, -0.05f, 0), new Vector3(8, 0.1f, 8));
		Box(new Vector3(0, 3.05f, 0), new Vector3(8, 0.1f, 8));
		Box(new Vector3(0, 1.5f, -4), new Vector3(8, 3, 0.1f));
		Box(new Vector3(0, 1.5f, 4), new Vector3(8, 3, 0.1f));
		Box(new Vector3(-4, 1.5f, 0), new Vector3(0.1f, 3, 8));
		Box(new Vector3(4, 1.5f, 0), new Vector3(0.1f, 3, 8));
		var rig = new Node3D { Position = new Vector3(0, 1.6f, 1.5f) };
		AddChild(rig);
		var glow = new OmniLight3D { LightColor = new Color(1f, 0.72f, 0.42f), OmniRange = 14f, OmniAttenuation = 1.3f, LightEnergy = 1.35f, ShadowEnabled = true,
			Position = new Vector3(0.18f, -0.25f, -0.25f), LightVolumetricFogEnergy = 0.05f, LightSize = 0.08f, ShadowBlur = 1.5f };
		rig.AddChild(glow);
		var cage = ProjectDS.Player.Lantern.BuildCage(glow);
		_cam.Reparent(rig, false);
		_cam.Transform = Transform3D.Identity;
		await Seconds(0.5);
		async Task Look(string name, float yaw, float pitch)
		{
			rig.Rotation = new Vector3(Mathf.DegToRad(pitch), Mathf.DegToRad(yaw), 0);
			await Frames(6);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
			GD.Print($"[creature-preview] {name}");
		}
		await Look("lantern_ahead", 0, -5);
		await Look("lantern_left", 50, 0);
		await Look("lantern_up", 0, 50);
		await Look("lantern_down", 20, -55);
		cage.Visible = false;
		await Look("lantern_plain_left", 50, 0);
		cage.Visible = true;
		await Look("lantern_near_wall", 90, 0);
		rig.Position = new Vector3(-3f, 1.6f, 1.5f);
		await Look("lantern_near_wall_close", 90, 0);
		rig.Position = new Vector3(0, 1.6f, 1.5f);
		env.VolumetricFogEnabled = true;
		env.VolumetricFogDensity = 0.022f;
		env.VolumetricFogLength = 48f;
		env.VolumetricFogAmbientInject = 0f;
		var beam = new SpotLight3D { LightColor = new Color(1f, 0.72f, 0.42f), SpotRange = 24f, SpotAngle = 35f, SpotAngleAttenuation = 0.6f, LightEnergy = 2.5f, ShadowEnabled = true, LightVolumetricFogEnergy = 0.45f };
		rig.AddChild(beam);
		AddChild(new OmniLight3D { Position = new Vector3(-2.5f, 2.6f, -3f), LightColor = new Color(1f, 0.85f, 0.6f), LightEnergy = 1.2f, OmniRange = 5f });
		await Look("lantern_fog_beam", 10, -3);
	}

	/// <summary>The remodelled crawler walking a stair-like slope, and close on its head (the horror pass).</summary>
	private async Task CrawlerShots()
	{
		env.BackgroundColor = new Color(0.12f, 0.12f, 0.13f);
		var floor = new StaticBody3D { Name = "Floor" };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 1, 40) }, Position = new Vector3(0, -0.5f, 0) });
		floor.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40, 40) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.24f, 0.23f) } });
		AddChild(floor);
		var crawler = new Crawler { Name = "Crawler" };
		AddChild(crawler);
		GD.Print($"[creature-preview] crawler rigged {crawler.Rigged}");
		for (int i = 0; i < 120; i++)
		{
			crawler.Hurry = i > 60 ? 0.8f : 0.2f;
			crawler.Place(new Vector3(0, 0, 3f - i * 0.03f), Vector3.Forward);
			await Frames(1);
			if (i == 50) await Shot("crawler_walk_side", new Vector3(3.0f, 1.0f, crawler.GlobalPosition.Z), crawler.GlobalPosition + new Vector3(0, -0.2f, 0));
		}
		await Shot("crawler_side", new Vector3(3.2f, 1.1f, crawler.GlobalPosition.Z + 0.3f), crawler.GlobalPosition + new Vector3(0, -0.2f, 0));
		await Shot("crawler_front", crawler.GlobalPosition + new Vector3(0.5f, 0.6f, -2.4f), crawler.GlobalPosition + new Vector3(0, -0.25f, 0));
		await Shot("crawler_above", crawler.GlobalPosition + new Vector3(1.6f, 2.6f, 1.6f), crawler.GlobalPosition);
		var head = crawler.FindChild("Head", true, false) as Node3D;
		Vector3 h = head?.GlobalPosition ?? crawler.GlobalPosition;
		await Shot("crawler_face_close", h + new Vector3(0.15f, -0.25f, -0.75f), h);
	}

	/// <summary>The weather (the weather pass): snow light and heavy, a blizzard, the shelter of a roof and the firs, rain;
	/// by dusk light and by a lantern at night.</summary>
	private async Task WeatherShots()
	{
		foreach (var n in GetChildren()) if (n is DirectionalLight3D d) { d.LightEnergy = 0.35f; d.LightColor = new Color(0.7f, 0.76f, 0.9f); }
		env.BackgroundColor = new Color(0.2f, 0.22f, 0.26f);
		env.AmbientLightColor = new Color(0.5f, 0.55f, 0.65f);
		env.AmbientLightEnergy = 0.5f;
		env.FogEnabled = true;
		env.FogMode = Environment.FogModeEnum.Depth;
		env.FogLightColor = new Color(0.25f, 0.27f, 0.31f);
		env.FogDepthBegin = 8f; env.FogDepthEnd = 70f; env.FogDensity = 1f;
		var ground = new StaticBody3D { Name = "Ground" };
		ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 1, 200) }, Position = new Vector3(0, -0.5f, 0) });
		ground.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(200, 200) }, MaterialOverride = World.WinterWoods.PropSnow });
		AddChild(ground);
		// a lean-to: a roof on four posts (nothing should fall under it)
		var wood = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.2f, 0.16f) };
		AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(5, 0.2f, 4) }, Position = new Vector3(-3, 2.6f, -6), MaterialOverride = wood });
		foreach (var (x, z) in new[] { (-5.3f, -7.8f), (-0.7f, -7.8f), (-5.3f, -4.2f), (-0.7f, -4.2f) })
			AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.15f, 2.6f, 0.15f) }, Position = new Vector3(x, 1.3f, z), MaterialOverride = wood });
		var rng = new RandomNumberGenerator { Seed = 3 };
		for (int i = 0; i < 40; i++)
		{
			var fir = new MeshInstance3D { Mesh = World.ForestScatter.FirMesh(i, rng.RandfRange(9, 16), 0.3f, 0.3f, 7, 2.2f, 0.2f), Position = new Vector3(rng.RandfRange(-40, 40), 0, rng.RandfRange(-60, -12)) };
			if (Mathf.Abs(fir.Position.X) < 4 && fir.Position.Z > -20) continue;
			AddChild(fir);
		}
		var w = World.Weather.Get(this);
		var eye = new Vector3(0, 1.65f, 2f);
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(new Vector3(-0.5f, 1.4f, -10f) - eye, Vector3.Up), eye);
		async Task Snap(string name, double settle)
		{
			await Seconds(settle);
			GetViewport().GetTexture().GetImage().SavePng($"{_out}/{name}.png");
			GD.Print($"[creature-preview] {name}: snow {w.Snow:0.00} blizzard {w.Blizzard:0.00} wind {World.Weather.Wind}");
		}
		w.SetSnow(0.35f, 0.01f);
		await Snap("weather_light", 3);
		w.SetSnow(1f, 0.01f);
		await Snap("weather_heavy", 3);
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(new Vector3(-3f, 1.2f, -6f) - eye, Vector3.Up), eye);
		await Snap("weather_under_the_roof", 2);
		w.SetBlizzard(1f, 0.01f);
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(new Vector3(-0.5f, 1.4f, -10f) - eye, Vector3.Up), eye);
		await Snap("weather_blizzard", 4);
		w.SetBlizzard(0f, 0.01f);
		w.SetSnow(0.8f, 0.01f);
		// night, a lantern
		foreach (var n in GetChildren()) if (n is DirectionalLight3D d) d.LightEnergy = 0.02f;
		env.AmbientLightEnergy = 0.05f;
		env.BackgroundColor = new Color(0.02f, 0.02f, 0.03f);
		env.FogLightColor = new Color(0.02f, 0.02f, 0.03f);
		var lamp = new OmniLight3D { LightColor = new Color(1f, 0.72f, 0.42f), OmniRange = 14f, LightEnergy = 1.35f, OmniAttenuation = 1.3f };
		_cam.AddChild(lamp);
		lamp.Position = new Vector3(0.18f, -0.25f, -0.25f);
		await Snap("weather_night_lantern", 3);
		lamp.QueueFree();
		w.SetSnow(0f, 0.01f);
		foreach (var n in GetChildren()) if (n is DirectionalLight3D d) d.LightEnergy = 0.35f;
		env.AmbientLightEnergy = 0.5f;
		env.BackgroundColor = new Color(0.2f, 0.22f, 0.26f);
		w.Rain = 1f;
		await Snap("weather_rain", 3);
		w.Rain = 0f;
		// a fire: its smoke's flipbook drifting off with the wind, its sparks streaking up
		var fire = new World.FireVfx { Extent = new Vector3(1.2f, 1.1f, 1.2f), Position = new Vector3(2f, 0, -5f) };
		AddChild(fire);
		foreach (var n in GetChildren()) if (n is DirectionalLight3D d) d.LightEnergy = 0.05f;
		env.AmbientLightEnergy = 0.1f;
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(new Vector3(2f, 2.2f, -5f) - eye, Vector3.Up), eye);
		await Snap("weather_fire_smoke", 6);
	}

	/// <summary>Does the roof collider keep the snow off? Under a big slab roof, looking along under it.</summary>
	private async Task ShelterTest()
	{
		env.BackgroundColor = new Color(0.2f, 0.22f, 0.26f);
		var ground = new StaticBody3D();
		ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 1, 200) }, Position = new Vector3(0, -0.5f, 0) });
		ground.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(200, 200) } });
		AddChild(ground);
		AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(30, 0.5f, 30) }, Position = new Vector3(0, 6f, 0) });
		var w = World.Weather.Get(this);
		w.SetSnow(1f, 0.01f);
		var eye = new Vector3(0, 1.6f, 0);
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(new Vector3(0, 1.4f, -10f) - eye, Vector3.Up), eye);
		// a plain emitter, hide-on-contact, beside: does the heightfield reach particles at all?
		var plain = new GpuParticles3D { Amount = 4000, Lifetime = 8, Preprocess = 8, Position = new Vector3(0, 12f, -6f), LocalCoords = false,
			ProcessMaterial = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(6, 1, 6), Gravity = new Vector3(0, -2f, 0),
				CollisionMode = ParticleProcessMaterial.CollisionModeEnum.HideOnContact },
			DrawPass1 = new QuadMesh { Size = new Vector2(0.06f, 0.06f), Material = new StandardMaterial3D { AlbedoColor = new Color(1, 0.3f, 0.2f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
			VisibilityAabb = new Aabb(new Vector3(-20, -20, -20), new Vector3(40, 40, 40)), CollisionBaseSize = 0.02f };
		AddChild(plain);
		await Seconds(4);
		GetViewport().GetTexture().GetImage().SavePng($"{_out}/shelter_under_roof.png");
		var sh = w.FindChild("Shelter", false, false) as GpuParticlesCollisionHeightField3D;
		GD.Print($"[creature-preview] shelter {sh != null} at {sh?.GlobalPosition} size {sh?.Size} follow {sh?.FollowCameraEnabled}");
	}

	private async Task WendigoShots()
	{
		env.BackgroundColor = new Color(0.1f, 0.11f, 0.14f);
		env.AmbientLightColor = new Color(0.45f, 0.5f, 0.62f);
		env.AmbientLightEnergy = 0.35f;
		var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(30, 30) }, MaterialOverride = World.WinterWoods.PropSnow };
		AddChild(ground);
		var w = new Wendigo { Name = "Wendigo" };
		AddChild(w);
		w.StandAt(Vector3.Zero, new Vector3(0, 0, -10));
		await Seconds(1.5);
		await Shot("wendigo_front", new Vector3(0.6f, 2.6f, -8f), new Vector3(0, 2.5f, 0));
		await Shot("wendigo_three_quarter", new Vector3(4.6f, 2.8f, -5.2f), new Vector3(0, 2.6f, 0));
		await Shot("wendigo_side", new Vector3(7f, 2.4f, 0.2f), new Vector3(0, 2.4f, 0));
		await Shot("wendigo_back", new Vector3(-3f, 3f, 6f), new Vector3(0, 2.8f, 0));
		await Shot("wendigo_head", new Vector3(0.9f, 3.7f, -2.6f), w.MouthWorld + new Vector3(0, 0.15f, 0));
		await Shot("wendigo_chest", new Vector3(-0.6f, 3.2f, -1.9f), w.ChestWorld);
		await Shot("wendigo_hand", new Vector3(1.6f, 1.2f, -1.6f), w.ToGlobal(new Vector3(0.68f, 1.0f, -0.6f)));
		await Shot("wendigo_far_dusk", new Vector3(2f, 1.7f, -22f), new Vector3(0, 2.4f, 0));
		// the leap, frozen mid-air; the pounce
		w.Leap(w.GlobalPosition + new Vector3(0, 0, 0.01f), 4f, null);
		await Seconds(0.9);
		w.ProcessMode = ProcessModeEnum.Disabled;
		await Shot("wendigo_leaping", new Vector3(6f, 2.6f, -1f), w.GlobalPosition + new Vector3(0, 2.4f, 0));
		w.ProcessMode = ProcessModeEnum.Inherit;
		w.StandAt(new Vector3(0, 3f, 0), new Vector3(0, 0, -10));
		w.Leap(new Vector3(0, 0, -6f), 4f, null);
		await Seconds(1.2);
		w.ProcessMode = ProcessModeEnum.Disabled;
		await Shot("wendigo_pouncing", new Vector3(6f, 2.6f, -2f), w.GlobalPosition + new Vector3(0, 2.4f, 0));
		w.ProcessMode = ProcessModeEnum.Inherit;
		// its held poses: crouched (on a wall's top), and clinging to a trunk mid-climb
		w.StandAt(Vector3.Zero, new Vector3(0, 0, -10));
		w.Hold("crouch", 1f);
		await Seconds(0.5);
		await Shot("wendigo_crouched", new Vector3(5f, 2.2f, -4f), new Vector3(0, 1.6f, 0));
		AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.4f, Height = 12f }, Position = new Vector3(0, 6f, -3f), MaterialOverride = World.WinterWoods.PropSnow });
		w.StandAt(new Vector3(0, 3.5f, -2.25f), new Vector3(0, 3.5f, -3f));
		w.Hold("air", 0.45f);
		await Seconds(0.5);
		await Shot("wendigo_climbing", new Vector3(6f, 3.5f, 5f), new Vector3(0, 5f, -2.6f));
	}

	/// <summary>The ski lodge's interior (Act 23), standing alone: the lobby, the bar, the pantry, the corridor, the rooms, the dining hall.</summary>
	private async Task LodgeShots()
	{
		env.BackgroundColor = new Color(0.3f, 0.32f, 0.36f);
		env.AmbientLightColor = new Color(0.78f, 0.68f, 0.56f);
		env.AmbientLightEnergy = 0.42f;
		foreach (var c in GetChildren()) if (c is DirectionalLight3D d) d.LightEnergy = 0.15f;
		var lodge = new World.SkiLodge { Name = "Lodge" };
		AddChild(lodge);
		await Seconds(2.0);
		_cam.Fov = 75f;
		await Shot("lodge_lobby_from_door", new Vector3(0, 1.7f, 8.8f), new Vector3(0, 2.5f, -6f));
		await Shot("lodge_lobby_fireplace", new Vector3(-2f, 1.7f, 2f), new Vector3(8f, 2.5f, -4.6f));
		await Shot("lodge_lobby_stairs", new Vector3(4f, 1.7f, 2f), new Vector3(-3f, 2.8f, -9f));
		await Shot("lodge_lobby_from_balcony", new Vector3(-8.5f, 5.9f, -1.5f), new Vector3(6f, 3f, 0f));
		await Shot("lodge_lobby_desk", new Vector3(0f, 1.7f, 3f), new Vector3(5f, 1.5f, 8f));
		await Shot("lodge_lobby_seating", new Vector3(1.5f, 1.6f, 1.5f), new Vector3(6.2f, 0.6f, -3.0f));
		await Shot("lodge_lobby_mantel", new Vector3(3.6f, 2.2f, -1.6f), new Vector3(7.6f, 2.0f, -4.4f));
		await Shot("lodge_mudroom_clutter", new Vector3(-28.2f, 1.5f, -2.4f), new Vector3(-32.6f, 1.0f, -5.4f));
		await Shot("lodge_lobby_ceiling", new Vector3(0, 1.7f, 0), new Vector3(0.1f, 20f, 0.2f));
		await Shot("lodge_bar", new Vector3(-10.8f, 1.7f, 4f), new Vector3(-22f, 1.4f, 4f));
		await Shot("lodge_bar_counter", new Vector3(-15f, 1.6f, 6.8f), new Vector3(-19.4f, 1f, 2.5f));
		await Shot("lodge_service_corridor", new Vector3(-31f, 1.6f, -0.7f), new Vector3(-22f, 1.4f, -0.7f));
		await Shot("lodge_pantry", new Vector3(-9.3f, 1.6f, -6.4f), new Vector3(-22f, 1.2f, -6.4f));
		await Shot("lodge_corridor", new Vector3(-12.2f, 5.9f, 0f), new Vector3(-28f, 5.5f, 0f));
		// the doorway from the gallery into the corridor, looking into its corners (the gaps the owner found)
		await Shot("lodge_corridor_door_n", new Vector3(-10.6f, 5.9f, -0.6f), new Vector3(-12.8f, 5.6f, 1.6f));
		await Shot("lodge_corridor_door_s", new Vector3(-10.6f, 5.9f, 0.6f), new Vector3(-12.8f, 5.6f, -1.6f));
		await Shot("lodge_corridor_door_up", new Vector3(-12.2f, 5.4f, 0f), new Vector3(-11.6f, 7.2f, 0f));
		await Shot("lodge_room202", new Vector3(-13.2f, 5.9f, 1.6f), new Vector3(-17f, 5f, 6.5f));
		await Shot("lodge_room202_bath", new Vector3(-17f, 5.9f, 2.7f), new Vector3(-19.5f, 5f, 3.6f));
		await Shot("lodge_room203", new Vector3(-13.2f, 5.9f, -1.6f), new Vector3(-15f, 5f, -7f));
		await Shot("lodge_room203_hole", new Vector3(-17.8f, 5.9f, -2.4f), new Vector3(-21f, 5.2f, -2.4f));
		await Shot("lodge_room204", new Vector3(-21f, 5.9f, -1.6f), new Vector3(-25f, 5f, -6f));
		await Shot("lodge_dining", new Vector3(13.5f, 1.8f, 0f), new Vector3(40f, 1.5f, 0f));
		await Shot("lodge_exterior_front", new Vector3(0f, 3f, 55f), new Vector3(0f, 10f, 0f));
		await Shot("lodge_room204_hole", new Vector3(-24f, 5.9f, -3.5f), new Vector3(-19f, 5.2f, -2.4f));
		await Shot("lodge_dining_tables", new Vector3(22f, 2.6f, 6.5f), new Vector3(27f, 0.8f, 0f));
		await Shot("lodge_exterior_chimney", new Vector3(26f, 9f, -24f), new Vector3(8f, 13f, -4.6f));
		await Shot("lodge_fireplace_top", new Vector3(-7f, 5.9f, 1f), new Vector3(8.3f, 11f, -4.8f));
		// the audit's sweep: all round the lobby, looking up at the walls and the roof, for anything from outside
		// showing through
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.DegToRad(30f + 60f * i);
			await Shot($"lodge_sweep_{i}", new Vector3(0, 1.7f, 0), new Vector3(Mathf.Sin(a) * 10f, 8f, Mathf.Cos(a) * 10f));
		}
		// Act 23's second half: 201, the crawlspace (lit by a lantern-like light at the eye), the chase, the frozen lodge
		var lamp = new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.55f), LightEnergy = 1.2f, OmniRange = 6f };
		AddChild(lamp);
		async Task Lit(string n, Vector3 at, Vector3 look) { lamp.Position = at; await Shot(n, at, look); }
		lodge.BathSwitches[201].Set(true);
		await Shot("act23_room201", new Vector3(-21.3f, 5.9f, 1.8f), new Vector3(-23f, 4.9f, 6.2f));
		await Shot("act23_201_bath_hole", new Vector3(-25.8f, 5.9f, 2.4f), new Vector3(-28f, 5.2f, 1.95f));
		await Lit("crawl_entry", new Vector3(-28.9f, 5.8f, 1.95f), new Vector3(-31f, 5.6f, 1.95f));
		var dn = World.SkiLodge.CrawlDown;
		await Lit("crawl_maze_pipe", World.SkiLodge.CellCentre(new Vector2I(-4, -3)) + dn + new Vector3(0, 4.2f + 1.6f, 0), World.SkiLodge.CellCentre(new Vector2I(-9, -3)) + dn + new Vector3(0, 4.2f + 1.3f, 0));
		await Lit("crawl_stairs", World.SkiLodge.CellCentre(new Vector2I(3, -8)) + dn + new Vector3(0, 4.2f + 1.6f, 0), World.SkiLodge.CellCentre(new Vector2I(10, -8)) + dn + new Vector3(0, 1.0f, 0));
		lodge.Arms[0].Burst();
		await Seconds(0.6);
		await Lit("crawl_arm", World.SkiLodge.CellCentre(new Vector2I(38, -3)) + dn + new Vector3(0.1f, 1.15f, -0.1f), World.SkiLodge.CellCentre(new Vector2I(39, -3)) + dn + new Vector3(0, 1.5f, 0.2f));
		await Lit("crawl_arm_far", World.SkiLodge.CellCentre(new Vector2I(37, -3)) + dn + new Vector3(0, 1.6f, 0), World.SkiLodge.CellCentre(new Vector2I(39, -3)) + dn + new Vector3(0, 1.5f, 0.2f));
		await Lit("crawl_brick", World.SkiLodge.CellCentre(new Vector2I(16, 1)) + dn + new Vector3(0, 1.6f, 0), World.SkiLodge.CellCentre(new Vector2I(16, -4)) + dn + new Vector3(0, 1.2f, 0));
		await Shot("dining_chase", new Vector3(32f, 1.7f, 1.5f), new Vector3(33.5f, 1.4f, -5.5f));
		await Shot("dining_west", new Vector3(38f, 1.7f, 0f), new Vector3(10f, 2.2f, 0f));
		lodge.FreezeNow();
		await Seconds(1.0);
		await Shot("frozen_dining", new Vector3(13.5f, 1.8f, 0f), new Vector3(40f, 1.5f, 0f));
		await Shot("frozen_dining_west", new Vector3(38f, 1.7f, 0f), new Vector3(10f, 2.2f, 0f));
		await Shot("frozen_dining_window", new Vector3(24f, 1.7f, 3f), new Vector3(24f, 2f, -7.6f));
		await Shot("frozen_lobby", new Vector3(0, 1.7f, -2f), new Vector3(0f, 3f, 10f));
		await Shot("lodge_mudroom", new Vector3(-28f, 1.6f, -5.5f), new Vector3(-30f, 1.2f, -1.7f));
	}
}
