using Godot;
using ProjectDS.Audio;
using ProjectDS.World.LakeParts;

namespace ProjectDS.World;

/// <summary>
/// Act 12's lake: a remote pocket bolted onto the Hollow scene the same way <c>BunkerInterior</c>
/// and <c>FinalStairs</c> are (its own coordinates, far from the main terrain — it does not use
/// <see cref="ForestTerrain"/>'s heightfield). A real lake in a wooded bowl at sunrise: an irregular
/// oval of water ringed by a fir treeline, reeds and rocks at its edge, dead snags leaning out over
/// it, a dock and a moored boat on the near beach, and the dilapidated forest rescue station on a
/// rise behind the far one.
///
/// The pieces live in <c>scripts/world/lakeparts</c>: <see cref="LakeShape"/> (the geometry and the
/// wave maths everything shares), <see cref="LakeTerrain"/> (ground and water meshes),
/// <see cref="LakeDressing"/> (trees, reeds, rocks, lily pads), <see cref="LakeStructures"/> (dock,
/// station, fences) and <see cref="LakeSunrise"/> (the sky and sun while the camera is here).
/// <see cref="LakeCrossingEvent"/> (a sibling node) owns the rowboat and the whole crossing, and
/// drives the water through <see cref="Waves"/>.
/// </summary>
[GlobalClass]
public partial class Lake : Node3D
{
	[Export] public int Seed = 1201;

	/// <summary>The near shore's wake-up spot, facing the boat (world). Also the checkpoint-9 respawn
	/// point: registered in group "respawn_Act11GiantEncounter".</summary>
	public Vector3 WakeSpotWorld { get; private set; }
	public float WakeYaw { get; private set; }
	/// <summary>Where the boat sits moored beside the dock's end (world, at the waterline).</summary>
	public Vector3 NearDockWorld { get; private set; }
	/// <summary>Where the boat's hull comes to rest with its bow run up on the far beach (world, waterline).</summary>
	public Vector3 BoatLandingWorld { get; private set; }
	/// <summary>Where the player steps out onto the far beach (world, on the gravel).</summary>
	public Vector3 FarDockWorld { get; private set; }
	/// <summary>On the path, a few metres short of the station's porch (world).</summary>
	public Vector3 StationApproachWorld { get; private set; }
	/// <summary>Just inside the station's open front door (world).</summary>
	public Vector3 StationDoorWorld { get; private set; }
	/// <summary>The station's door threshold itself (world).</summary>
	public Vector3 StationThresholdWorld { get; private set; }
	/// <summary>The water surface's shader material.</summary>
	public ShaderMaterial WaterMaterial { get; private set; }
	public LakeDressing Dressing { get; private set; }
	public LakeSunrise Sunrise { get; private set; }
	public Node3D Station { get; private set; }

	/// <summary>The live water state: the crossing raises <see cref="LakeShape.Waves.Intensity"/>, the
	/// bulge and the ring wave; this node advances the clock and pushes it all to the shader.</summary>
	public ref LakeShape.Waves Waves => ref _waves;
	private LakeShape.Waves _waves;

	private OneShotEmitter _birds, _loons;
	private Node3D _player;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		AddToGroup("lake_marker");
		var rng = new RandomNumberGenerator { Seed = (ulong)Seed };

		Vector2 wake = new(-1.3f, 9.2f);
		WakeSpotWorld = ToGlobal(new Vector3(wake.X, LakeShape.Ground(wake.X, wake.Y) + 0.05f, wake.Y));
		Vector2 toBoat = LakeShape.BoatMooring - wake;
		WakeYaw = Mathf.Atan2(-toBoat.X, -toBoat.Y);
		// A real marker, not just the property above: RespawnPoints.For() finds checkpoint 9's
		// spot by group, so Continue after the giant encounter (before or after this crossing is
		// finished) lands exactly here, facing the boat, the same as the live wake-up does.
		var marker = new Node3D { Name = "WakeMarker" };
		AddChild(marker);
		marker.GlobalPosition = WakeSpotWorld;
		marker.GlobalRotation = new Vector3(0, WakeYaw, 0);
		marker.AddToGroup("respawn_Act11GiantEncounter");

		NearDockWorld = ToGlobal(new Vector3(LakeShape.BoatMooring.X, 0f, LakeShape.BoatMooring.Y));
		BoatLandingWorld = ToGlobal(new Vector3(LakeShape.BoatLanding.X, 0f, LakeShape.BoatLanding.Y));
		float stepZ = LakeShape.FarShoreZ - 1.6f;
		FarDockWorld = ToGlobal(new Vector3(0.2f, LakeShape.Ground(0.2f, stepZ) + 0.05f, stepZ));
		Vector3 door = LakeStructures.StationDoor;
		StationThresholdWorld = ToGlobal(door);
		StationDoorWorld = ToGlobal(door + new Vector3(0, 0.05f, -0.3f));
		float approachZ = door.Z + 5.6f;
		StationApproachWorld = ToGlobal(new Vector3(0, LakeShape.Ground(0f, approachZ) + 0.05f, approachZ));

		LakeTerrain.BuildGround(this);
		WaterMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/lake_water.gdshader") };
		WaterMaterial.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		LakeShape.ApplyTables(WaterMaterial);
		LakeShape.Apply(_waves, WaterMaterial);
		LakeTerrain.BuildWater(this, WaterMaterial);

		Dressing = new LakeDressing { Name = "Dressing" };
		AddChild(Dressing);
		Dressing.Build(Seed + 7);
		LakeStructures.BuildDock(this, rng);
		Station = LakeStructures.BuildStation(this, rng);
		LakeStructures.BuildFences(this);
		BuildMist(rng);
		BuildSound();

		Sunrise = new LakeSunrise { Name = "Sunrise" };
		AddChild(Sunrise);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		// the storm's swell runs a little quicker than the sunrise ripple
		_waves.Time += dt * (1f + 0.08f * _waves.Intensity);
		LakeShape.Apply(_waves, WaterMaterial);
		Dressing?.RideWaves(_waves);
		GateEmitters();
	}

	/// <summary>Water surface height (world Y) under a world point, from the live waves.</summary>
	public float WaterHeightAt(Vector3 world, out Vector2 slope)
	{
		Vector3 l = world - GlobalPosition;
		return GlobalPosition.Y + LakeShape.WaveHeight(_waves, l.X, l.Z, out slope);
	}

	/// <summary>The lake's own dawn birdsong and loons only sing while the player is here (they place
	/// their calls around the player, wherever that is), and stop for good once the lake has been hushed.</summary>
	private void GateEmitters()
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (_player == null || _birds == null) return;
		Vector3 d = _player.GlobalPosition - GlobalPosition;
		bool here = !_hushed && Mathf.Abs(d.X) < 200f && Mathf.Abs(d.Z) < 200f;
		var mode = here ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
		if (_birds.ProcessMode != mode) { _birds.ProcessMode = mode; _loons.ProcessMode = mode; }
	}

	private bool _hushed;

	/// <summary>The creature is up: no more birdsong, no more loons, for the rest of the act.</summary>
	public void Hush() => _hushed = true;

	// ------------------------------------------------------------------ mist + sound

	/// <summary>Low mist lying on the water, lit gold by the sunrise, and a thinner band clinging to
	/// the treeline.</summary>
	private void BuildMist(RandomNumberGenerator rng)
	{
		var ramp = new Gradient();
		ramp.SetColor(0, new Color(1, 1, 1, 0));
		ramp.SetColor(1, new Color(1, 1, 1, 0));
		ramp.AddPoint(0.3f, new Color(1, 1, 1, 0.75f));
		ramp.AddPoint(0.7f, new Color(1, 1, 1, 0.45f));
		GpuParticles3D Emitter(string name, Vector3 extents, Vector3 at, int amount, float scaleMin, float scaleMax, Color tint)
		{
			var pm = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
				EmissionBoxExtents = extents,
				Direction = new Vector3(0.3f, 0.1f, 1f),
				Spread = 40f,
				InitialVelocityMin = 0.05f,
				InitialVelocityMax = 0.22f,
				Gravity = Vector3.Zero,
				ScaleMin = scaleMin,
				ScaleMax = scaleMax,
				ColorRamp = new GradientTexture1D { Gradient = ramp },
			};
			var draw = new QuadMesh { Size = Vector2.One };
			draw.Material = new StandardMaterial3D
			{
				AlbedoTexture = LakeFx.SoftDot(),
				AlbedoColor = tint,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				VertexColorUseAsAlbedo = true,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
				ProximityFadeEnabled = true,
				ProximityFadeDistance = 1.5f,
			};
			var p = new GpuParticles3D
			{
				Name = name, Amount = amount, Lifetime = 14.0, Preprocess = 10.0,
				ProcessMaterial = pm, DrawPass1 = draw,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				VisibilityAabb = new Aabb(-extents - Vector3.One * 8f, extents * 2f + Vector3.One * 16f),
				Position = at,
			};
			AddChild(p);
			return p;
		}
		// low and flat-lying: small enough that none of them ever hangs up in the sky
		Emitter("WaterMist", new Vector3(LakeShape.SemiX * 0.8f, 0.15f, LakeShape.SemiZ * 0.85f), new Vector3(0, 0.3f, LakeShape.CenterZ),
			320, 3f, 6f, new Color(1f, 0.86f, 0.74f, 0.16f));
	}

	private void BuildSound()
	{
		const string path = "res://assets/audio/ambient/lake_loop.wav";
		if (ResourceLoader.Exists(path))
		{
			var player = new AudioStreamPlayer3D
			{
				Name = "LakeSound", Stream = GD.Load<AudioStream>(path), Bus = "Water",
				UnitSize = 40f, MaxDistance = 220f, Position = new Vector3(0, 0.3f, LakeShape.CenterZ),
			};
			player.AddChild(new AmbienceLoop { BaseVolumeDb = -3f });
			AddChild(player);
		}
		// A sparse dawn chorus from the treeline, and now and then a loon somewhere far out on the water.
		_birds = new OneShotEmitter
		{
			Name = "DawnBirds", SamplePattern = "res://assets/audio/sfx/bird_{0:00}.wav", SampleCount = 8,
			Bus = "Birds", LifeCategory = OneShotEmitter.Life.Birds,
			IntervalSeconds = new Vector2(2.5f, 8f), DistanceRange = new Vector2(24f, 60f), HeightRange = new Vector2(5f, 16f),
			VolumeDb = -8f, Voices = 3,
		};
		_loons = new OneShotEmitter
		{
			Name = "Loons", SamplePattern = "res://assets/audio/sfx/loon_{0:00}.wav", SampleCount = 3,
			Bus = "Distant", LifeCategory = OneShotEmitter.Life.Distant,
			IntervalSeconds = new Vector2(13f, 26f), DistanceRange = new Vector2(55f, 95f), HeightRange = new Vector2(0f, 1.5f),
			VolumeDb = -5f, UnitSize = 34f, Voices = 2, PitchRange = new Vector2(0.97f, 1.03f),
		};
		AddChild(_birds);
		AddChild(_loons);
		_birds.ProcessMode = _loons.ProcessMode = ProcessModeEnum.Disabled;
	}
}
