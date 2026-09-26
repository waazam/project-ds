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
}
