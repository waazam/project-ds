using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The breath between the burning cabin and the bunker (2026-10-24, the game-loop pass: Acts 6, 7, 8-10 and 11 ran four
/// hard acts in a row, against docs/pacing-tension-map.md's own rule of never more than two without a quiet beat). Just
/// past the lookout, a step off the path to the bunker: somebody's lean-to, a plank roof on two posts, a fire still going
/// in a burn barrel, the copper kettle on a stump, a log to sit on, a coffee can and a box of matches.
///
/// "Rest by the fire" (held): the fire flares up, the sounds of the woods drop away for a moment, and the game saves here
/// (<see cref="Flag"/>, within checkpoint 6, back here on a load: <see cref="RespawnPoints"/>). Only once; after that it's
/// a warm place on the way. SurveyLot puts it down (and keeps the key's arrow trails clear of it).
/// </summary>
public partial class RestShelter : Node3D
{
	public const string Flag = "act7_shelter_rest";
	public const string MarkerGroup = "respawn_Act7Shelter";

	public Interactable Rest { get; private set; }
	private OmniLight3D _fire;
	private float _flare, _t;

	public override void _Ready()
	{
		var k = new MeshKit();
		// the lean-to: two posts at the front, its roof planks sloping back to the ground behind, a tarp over half of them
		k.Mat(BuildingTextures.LogMat);
		foreach (float x in new[] { -1.3f, 1.3f })
			k.Cylinder(new Vector3(x, -0.3f, 0.6f), new Vector3(x, 1.95f, 0.6f), 0.07f, 0.06f, 8, true, 1f);
		k.Cylinder(new Vector3(-1.5f, 1.9f, 0.6f), new Vector3(1.5f, 1.9f, 0.6f), 0.06f, 0.06f, 8, true, 1f);
		k.Mat(BuildingTextures.TimberAlongMat);
		for (int i = 0; i < 9; i++)
		{
			float x = -1.4f + i * 0.35f;
			k.Beam(new Vector3(x, 1.96f, 0.75f), new Vector3(x + 0.03f * (i % 3 - 1), 0.05f, -1.35f), 0.3f, 0.04f, 1f, Vector3.Up);
		}
		k.Mat(BuildingTextures.CanvasMat);
		k.Beam(new Vector3(-0.6f, 2.0f, 0.8f), new Vector3(-0.6f, 0.12f, -1.38f), 1.6f, 0.01f, 1f, Vector3.Up);
		// a log to sit on, the stump the kettle's on
		k.Mat(BuildingTextures.RoundLogMat);
		k.Cylinder(new Vector3(-0.9f, 0.18f, -0.35f), new Vector3(0.9f, 0.2f, -0.45f), 0.19f, 0.18f, 10, true, 1f);
		k.Cylinder(new Vector3(1.05f, 0f, 1.15f), new Vector3(1.05f, 0.42f, 1.15f), 0.22f, 0.2f, 10, true, 1f);
		k.CommitTo(this, "LeanTo");
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		AddChild(body);
		foreach (float x in new[] { -1.3f, 1.3f }) body.AddChild(new CollisionShape3D { Position = new Vector3(x, 1f, 0.6f), Shape = new BoxShape3D { Size = new Vector3(0.16f, 2f, 0.16f) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0f, 0.2f, -0.4f), Shape = new BoxShape3D { Size = new Vector3(1.9f, 0.4f, 0.4f) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0f, 0.8f, -1.1f), Shape = new BoxShape3D { Size = new Vector3(3f, 1.6f, 0.5f) } });

		// the fire in its barrel, out in front where the smoke can go
		var at = new Vector3(0f, 0f, 1.7f);
		WeatheredProps.Put(this, "BurnBarrel", at, 20f);
		body.AddChild(new CollisionShape3D { Position = at + Vector3.Up * 0.44f, Shape = new BoxShape3D { Size = new Vector3(0.56f, 0.88f, 0.56f) } });
		_fire = new OmniLight3D { Name = "Fire", Position = at + Vector3.Up * 1.1f, LightColor = new Color(1f, 0.62f, 0.32f), LightEnergy = 1.6f, OmniRange = 7.5f, ShadowEnabled = true };
		AddChild(_fire);
		AddChild(Flames(at + Vector3.Up * 0.82f));
		var crackle = new AudioStreamPlayer3D
		{
			Name = "Crackle", Position = at + Vector3.Up * 0.8f, Bus = "Events", VolumeDb = -8f, UnitSize = 2f, MaxDistance = 18f, Autoplay = true,
			Stream = GD.Load<AudioStream>("res://assets/audio/ambient/fire_crackle_loop.wav"),
		};
		if (crackle.Stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		AddChild(crackle);
		WeatheredProps.Put(this, "Kettle_Copper", new Vector3(1.05f, 0.42f, 1.15f), 200f);
		LorePages.Stash(WeatheredProps.Put(this, "CoffeeCan_Red", new Vector3(-0.2f, 0f, -0.75f), 30f), 7, "coffee can", new Vector3(0f, 0.1f, 0f),
			doodle: "cairn");   // (a clue: stones piled under a marked tree, where the key's other half is)
		WeatheredProps.Put(this, "Matchbox_C", new Vector3(-0.55f, 0.37f, -0.4f), 70f, 0f, false);
		WeatheredProps.Put(this, "Cowbell_Rust", new Vector3(1.3f, 1.7f, 0.68f), 0f);

		var marker = new Marker3D { Name = "Respawn", Position = new Vector3(0f, 0.1f, 3.4f), Rotation = new Vector3(0, 0, 0) };
		marker.AddToGroup(MarkerGroup);
		AddChild(marker);

		Rest = new Interactable { Name = "Rest", Prompt = "Rest by the fire", HoldSeconds = 1.6f, PickRadius = 0.8f, MaxDistance = 2.4f, Position = new Vector3(0f, 0.45f, -0.4f) };
		AddChild(Rest);
		Rest.Interacted += OnRest;
		if (StoryManager.Instance?.HasFlag(Flag) == true) Rest.Prompt = "Warm your hands";
	}

	private void OnRest(PlayerController player)
	{
		_flare = 1f;
		Audio.AudioDirector.OneShot(this, "flame_ignite", 2, ToGlobal(new Vector3(0f, 0.9f, 1.7f)), -10f, "Events", 8f, 0.05f);
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(Flag) || s.Current != Checkpoint.Act7CabinBurning) return;
		s.SetFlag(Flag);   // (saves at once: back here on a load)
		Rest.Prompt = "Warm your hands";
		UI.SaveIndicator.Instance?.Play();
		GD.Print("[story] Act 7: rested by the fire in the lean-to - saved");
	}

	public override void _Process(double delta)
	{
		_t += (float)delta;
		_flare = Mathf.MoveToward(_flare, 0f, (float)delta * 0.5f);
		// a fire's flicker, and the flare when they sit to it
		_fire.LightEnergy = 1.6f + 0.3f * Mathf.Sin(_t * 7.1f) * Mathf.Sin(_t * 3.3f + 1f) + 1.6f * _flare;
	}

	private static GpuParticles3D Flames(Vector3 at)
	{
		var ramp = new Gradient { Colors = new[] { new Color(1f, 0.55f, 0.2f, 0.28f), new Color(0.8f, 0.18f, 0.03f, 0f) }, Offsets = new[] { 0f, 1f } };
		return new GpuParticles3D
		{
			Name = "Flames", Amount = 22, Lifetime = 0.7, Position = at,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.12f, Direction = Vector3.Up, Spread = 12f,
				InitialVelocityMin = 0.35f, InitialVelocityMax = 0.8f, Gravity = new Vector3(0, 0.4f, 0), ScaleMin = 0.1f, ScaleMax = 0.22f,
				ColorRamp = new GradientTexture1D { Gradient = ramp },
			},
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One,
				Material = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					VertexColorUseAsAlbedo = true, AlbedoTexture = SoftPuff.Texture, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}
}
