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
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--lodge") >= 0) { await LodgeShots(); GetTree().Quit(); return; }
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
