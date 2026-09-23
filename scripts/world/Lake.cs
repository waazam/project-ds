using Godot;
using ProjectDS.Audio;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World;

/// <summary>
/// Act 12's lake: a remote pocket bolted onto the Hollow scene the same way <c>BunkerInterior</c>
/// and <c>FinalStairs</c> are (its own coordinates, far from the main terrain — it does not use
/// <see cref="ForestTerrain"/>'s heightfield). Passive scenery only: a still water plane, two raised
/// shore mounds, a small dock at the near shore, mist, a calm ambient loop, and the dilapidated
/// forest rescue station at the far shore. <see cref="LakeCrossingEvent"/> (a sibling node) owns the
/// rowboat and the whole boarding/paddling/breach story beat; this class just gives it the water
/// material and the two shore points to work with.
/// </summary>
[GlobalClass]
public partial class Lake : Node3D
{
	[Export] public float Width = 34f;
	/// <summary>Open water from the near dock to the far shore.</summary>
	[Export] public float CrossingLength = 64f;
	[Export] public float ShoreDepth = 9f;
	[Export] public float WaterLevel = 0f;
	[Export] public int Seed = 1201;

	/// <summary>The near shore's wake-up spot, facing out across the water (world). Also the
	/// checkpoint-9 respawn point: registered in group "respawn_Act11GiantEncounter".</summary>
	public Vector3 WakeSpotWorld { get; private set; }
	public float WakeYaw { get; private set; }
	/// <summary>Where the boat sits docked at the near shore (world), bow pointing across the lake.</summary>
	public Vector3 NearDockWorld { get; private set; }
	/// <summary>The far shore's landing point (world), where the boat comes ashore.</summary>
	public Vector3 FarDockWorld { get; private set; }
	/// <summary>Just in front of the rescue station's doorway (world), facing it.</summary>
	public Vector3 StationApproachWorld { get; private set; }
	public Vector3 StationDoorWorld { get; private set; }
	/// <summary>The water surface's shader material, so the crossing event can drive <c>wave_intensity</c>.</summary>
	public ShaderMaterial WaterMaterial { get; private set; }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		AddToGroup("lake_marker");
		var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		float hw = Width * 0.5f;
		float farZ = -CrossingLength;

		WakeSpotWorld = ToGlobal(new Vector3(0, 0.05f, ShoreDepth * 0.35f));
		WakeYaw = Rotation.Y;   // faces local -Z: out across the water, where the boat sits
		// A real marker, not just the property above: RespawnPoints.For() finds checkpoint 9's
		// spot by group, so Continue after the giant encounter (before or after this crossing is
		// finished) lands exactly here, facing the boat, the same as the live wake-up does.
		var wake = new Node3D { Name = "WakeMarker" };
		AddChild(wake);
		wake.GlobalPosition = WakeSpotWorld;
		wake.GlobalRotation = new Vector3(0, WakeYaw, 0);
		wake.AddToGroup("respawn_Act11GiantEncounter");
		NearDockWorld = ToGlobal(new Vector3(0, WaterLevel + 0.32f, -1.6f));
		FarDockWorld = ToGlobal(new Vector3(0, 0.05f, farZ - ShoreDepth * 0.3f));
		StationDoorWorld = ToGlobal(new Vector3(0, 0.05f, farZ - ShoreDepth - 2.4f));
		StationApproachWorld = ToGlobal(new Vector3(0, 0.05f, farZ - ShoreDepth - 6.5f));

		BuildShores(hw, farZ, rng);
		BuildWater(hw, farZ);
		BuildDock(hw);
		BuildStation(farZ, rng);
		BuildMist(hw, farZ, rng);
		BuildSound();
	}

	// ------------------------------------------------------------------ ground

	private void BuildShores(float hw, float farZ, RandomNumberGenerator rng)
	{
		var k = new MeshKit();
		k.Mat(BunkerTextures.EarthMat);
		void Mound(float z0, float z1)
		{
			for (int i = 0; i < 5; i++)
			{
				float t = i / 4f;
				k.Color = new Color(0.28f + 0.05f * rng.Randf(), 0.3f + 0.06f * rng.Randf(), 0.2f);
				k.Blob(new Vector3(rng.RandfRange(-hw * 0.6f, hw * 0.6f), -0.35f, Mathf.Lerp(z0, z1, t)),
					new Vector3(hw * 0.9f, 0.5f, ShoreDepth * 0.7f), Seed + i + (int)z0, 0.22f, true, 0.4f);
			}
		}
		Mound(ShoreDepth, 1.5f);
		Mound(farZ - 1.5f, farZ - ShoreDepth);
		k.Color = Colors.White;
		k.CommitTo(this, "Shores");

		var body = new StaticBody3D { Name = "ShoreGround", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "dirt");
		AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.1f, ShoreDepth * 0.5f + 0.2f), Shape = new BoxShape3D { Size = new Vector3(hw * 2f, 0.6f, ShoreDepth + 2f) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.1f, farZ - ShoreDepth * 0.5f - 0.2f), Shape = new BoxShape3D { Size = new Vector3(hw * 2f, 0.6f, ShoreDepth + 4f) } });
	}

	// ------------------------------------------------------------------ water

	private void BuildWater(float hw, float farZ)
	{
		WaterMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/lake_water.gdshader") };
		WaterMaterial.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		WaterMaterial.SetShaderParameter("wave_intensity", 0f);

		float z0 = ShoreDepth * 0.55f, z1 = farZ - ShoreDepth * 0.55f;
		var k = new MeshKit();
		k.Mat(WaterMaterial);
		const int segs = 10;
		for (int i = 0; i < segs; i++)
		{
			float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
			float za = Mathf.Lerp(z0, z1, t0), zb = Mathf.Lerp(z0, z1, t1);
			Vector3 a = new(-hw, WaterLevel, za), b = new(hw, WaterLevel, za), c = new(hw, WaterLevel, zb), d = new(-hw, WaterLevel, zb);
			float v0 = t0 * (z0 - z1) * 0.25f, v1 = t1 * (z0 - z1) * 0.25f;
			k.Quad(a, b, c, d, Vector3.Up, new Vector2(0, v0), new Vector2(hw * 2f * 0.25f, v0), new Vector2(hw * 2f * 0.25f, v1), new Vector2(0, v1));
		}
		var mi = new MeshInstance3D { Name = "Water", Mesh = k.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(mi);

		var body = new StaticBody3D { Name = "WaterBed", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "dirt");
		AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, WaterLevel - 1.2f, (z0 + z1) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(hw * 2f, 1f, z0 - z1) } });
	}

	// ------------------------------------------------------------------ dock

	private void BuildDock(float hw)
	{
		var k = new MeshKit();
		var wood = PropTextures.DeckMat;
		k.Mat(wood);
		k.Color = new Color(0.5f, 0.44f, 0.36f);
		float dockZ0 = 2.2f, dockZ1 = -3.2f, dockW = 2f;
		BuildKit.Box(k, new Vector3(0, WaterLevel + 0.28f, (dockZ0 + dockZ1) * 0.5f), new Vector3(dockW, 0.08f, dockZ0 - dockZ1), 1.2f);
		var frame = PropTextures.PostMat;
		k.Mat(frame);
		k.Color = new Color(0.4f, 0.36f, 0.3f);
		foreach (float z in new[] { dockZ0 - 0.3f, (dockZ0 + dockZ1) * 0.5f, dockZ1 + 0.3f })
			foreach (float x in new[] { -dockW * 0.5f + 0.1f, dockW * 0.5f - 0.1f })
				k.Cylinder(new Vector3(x, WaterLevel - 0.8f, z), new Vector3(x, WaterLevel + 0.5f, z), 0.06f, 0.06f, 6, true);
		k.Color = Colors.White;
		var mi = k.CommitTo(this, "Dock");

		var body = new StaticBody3D { Name = "DockBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, WaterLevel + 0.24f, (dockZ0 + dockZ1) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(dockW, 0.16f, dockZ0 - dockZ1) } });
	}

	// ------------------------------------------------------------------ the rescue station

	/// <summary>A large, dilapidated forest rescue/ranger station: a proper round-log cabin (the
	/// same course-by-course construction as <see cref="Cabin"/>) gone grey and weathered, a
	/// sagging gable roof missing shingles on one slope, boarded and broken windows, a lopsided
	/// porch, and a hand-painted sign gone illegible — nobody has touched this place in years.
	/// Its front door is the way into Act 13's interior (<see cref="StationInterior"/>).</summary>
	private void BuildStation(float farZ, RandomNumberGenerator rng)
	{
		float cz = farZ - ShoreDepth - 4.5f;
		float w = 9f, d = 7f, wallH = 3.4f, ridgeH = 5.6f;
		float hw = w * 0.5f, hd = d * 0.5f;
		var gen = new Node3D { Name = "RescueStation", Position = new Vector3(0, 0, cz) };
		AddChild(gen);

		var k = new MeshKit();
		var boards = BuildingTextures.BoardsMat;
		var frame = PropTextures.PostMat;
		var cols = new System.Collections.Generic.List<(Vector3, Vector3)>();

		// Foundation piers, floor.
		k.Mat(ProcTextures.ConcreteMat);
		k.Color = new Color(0.5f, 0.48f, 0.45f);
		foreach (float x in new[] { -hw + 0.3f, hw - 0.3f })
			foreach (float z in new[] { -hd + 0.3f, 0f, hd - 0.3f })
				BuildKit.Box(k, new Vector3(x, -0.3f, z), new Vector3(0.4f, 0.6f, 0.4f), 2f, BuildKit.Face.NY);
		k.Mat(BuildingTextures.FloorMat);
		k.Color = new Color(0.55f, 0.5f, 0.44f);
		BuildKit.Box(k, new Vector3(0, -0.02f, 0), new Vector3(w, 0.1f, d), 1f / 0.6f, BuildKit.Face.NY);
		cols.Add((new Vector3(0, -0.2f, 0), new Vector3(w, 0.5f, d)));

		// Walls: round-log, weathered and grey, the same course-by-course technique as the cabin
		// (BuildKit.Log; alternating which pair of walls runs long past the corner each course, so
		// the joints read as interlocking without any actual notch geometry) - a proper log cabin,
		// not board-and-batten, just bigger and left to rot.
		// doorW matches the interior doorway widening elsewhere in the station (1.4 -> 2.2 m): a
		// narrower gap here left the test bot (and, more importantly, the player under an autowalk
		// or a tight camera angle) prone to clipping the door-frame collision on the approach.
		const float doorW = 2.2f, doorH = 2.3f, logT = 0.24f, ext = 0.22f;
		const int courses = 12;
		float logH = wallH / courses;
		float uLen = logH * 4f;
		var logMat = BuildingTextures.LogMat;
		var logEnd = BuildingTextures.LogEndMat;
		float sideW = hw - doorW * 0.5f;
		for (int c = 0; c < courses; c++)
		{
			float y0 = c * logH, y1 = y0 + logH;
			bool frontLong = c % 2 == 0;
			float shade = rng.RandfRange(0.62f, 0.82f);   // grey and weathered, darker than a fresh cabin
			float jit = rng.RandfRange(-0.012f, 0.012f);
			k.Color = new Color(shade, shade * 0.98f, shade * 0.95f);

			// front (the door) and back, along X
			float xa = frontLong ? -hw - ext : -hw + logT * 0.5f, xb = -xa;
			bool doorHere = y1 > 0.05f && y0 < doorH - 0.05f;
			if (doorHere)
			{
				float dw = doorW * 0.5f;
				if (xa < -dw) BuildKit.Log(k, logMat, logEnd, false, xa, -dw, hd, y0, y1, logT, uLen, true, true);
				if (dw < xb) BuildKit.Log(k, logMat, logEnd, false, dw, xb, hd, y0, y1, logT, uLen, true, true);
			}
			else BuildKit.Log(k, logMat, logEnd, false, xa, xb, hd, y0, y1, logT, uLen, true, true);
			BuildKit.Log(k, logMat, logEnd, false, xa, xb, -hd, y0, y1, logT, uLen, true, true);
			// the two sides, along Z
			float za = frontLong ? -hd + logT * 0.5f : -hd - ext, zb = -za;
			foreach (int s in new[] { -1, 1 })
			{
				float s2 = rng.RandfRange(0.64f, 0.8f);
				k.Color = new Color(s2, s2 * 0.98f, s2 * 0.95f);
				BuildKit.Log(k, logMat, logEnd, true, za, zb, s * hw + jit * 0.5f, y0, y1, logT, uLen);
			}
		}
		// A few boards knocked loose, hanging askew.
		k.Mat(boards);
		k.Color = new Color(0.34f, 0.33f, 0.32f);
		for (int i = 0; i < 5; i++)
		{
			float x = rng.RandfRange(-hw + 0.5f, hw - 0.5f), y = rng.RandfRange(0.6f, wallH - 0.4f);
			BuildKit.Box(k, new Vector3(x, y, hd + 0.06f), new Vector3(0.22f, 0.9f, 0.03f), 1.4f, BuildKit.Face.None, new Basis(Vector3.Right, rng.RandfRange(-0.3f, 0.3f)));
		}

		// Boarded / broken windows, one on each long wall.
		k.Mat(frame);
		k.Color = new Color(0.3f, 0.28f, 0.25f);
		foreach (float x in new[] { -hw, hw })
		{
			float nx = x > 0 ? -1 : 1;
			Vector3 c = new(x + nx * 0.06f, wallH * 0.6f, hd * 0.3f);
			for (int i = 0; i < 3; i++)
				BuildKit.Box(k, c + new Vector3(0, (i - 1) * 0.32f, 0), new Vector3(0.03f, 0.9f, 1.1f), 1.4f, BuildKit.Face.None, new Basis(Vector3.Up, rng.RandfRange(-0.08f, 0.08f)));
		}

		// Sagging gable roof: one slope still shingled, the other stripped to bare, sagging boards.
		Vector3 ridgeF = new(0, ridgeH, hd + 0.5f), ridgeB = new(0, ridgeH - 0.25f, -hd - 0.5f);
		float ov = 0.4f;
		Vector3 eaveL = new(-hw - ov, wallH, 0), eaveR = new(hw + ov, wallH, 0);
		k.Mat(BuildingTextures.ShingleMat);
		k.Color = new Color(0.4f, 0.38f, 0.36f);
		k.Quad(ridgeB with { X = -hw - ov }, ridgeF with { X = -hw - ov }, eaveL with { Z = hd + ov }, eaveL with { Z = -hd - ov },
			(ridgeF - eaveL).Cross(Vector3.Right).Normalized());
		k.Mat(boards);
		k.Color = new Color(0.3f, 0.29f, 0.27f);
		Vector3 sagR = new(hw + ov, wallH - 0.55f, 0);   // the stripped slope sags at the middle
		k.Quad(ridgeB with { X = hw + ov }, sagR with { Z = -hd - ov }, sagR with { Z = hd + ov }, ridgeF with { X = hw + ov },
			(sagR - ridgeF).Cross(Vector3.Left).Normalized());
		k.Mat(frame);
		k.Color = new Color(0.32f, 0.3f, 0.28f);
		BuildKit.TriPanel(k, new Vector3(-hw, wallH, hd), new Vector3(hw, wallH, hd), ridgeF with { X = 0 }, Vector3.Back, Vector3.Right, 1f, 0.04f);
		BuildKit.TriPanel(k, new Vector3(-hw, wallH, -hd), new Vector3(hw, wallH, -hd), ridgeB with { X = 0 }, Vector3.Forward, Vector3.Right, 1f, 0.04f);

		// Porch: a lopsided little roof over the door, one post half-collapsed.
		k.Mat(boards);
		k.Color = new Color(0.34f, 0.32f, 0.3f);
		BuildKit.Box(k, new Vector3(0, wallH + 0.35f, hd + 1.1f), new Vector3(doorW + 1.2f, 0.06f, 1.6f), 1.2f, BuildKit.Face.None, new Basis(Vector3.Right, 0.1f));
		k.Mat(frame);
		k.Color = new Color(0.3f, 0.28f, 0.24f);
		k.Cylinder(new Vector3(-doorW * 0.5f - 0.5f, 0, hd + 1.7f), new Vector3(-doorW * 0.5f - 0.5f, wallH, hd + 1.7f), 0.08f, 0.08f, 6, true);
		k.Cylinder(new Vector3(doorW * 0.5f + 0.5f, 0, hd + 1.7f), new Vector3(doorW * 0.5f + 0.4f, wallH - 0.5f, hd + 1.65f), 0.08f, 0.06f, 6, true);

		// A hand-painted sign, faded and peeling, leaning by the door.
		k.Mat(ProcTextures.SignWoodMat);
		k.Color = new Color(0.55f, 0.5f, 0.4f);
		BuildKit.Box(k, new Vector3(hd * 0f + 2.2f, 1.1f, hd + 1.4f), new Vector3(1.3f, 0.55f, 0.04f), 1.5f, BuildKit.Face.None, new Basis(Vector3.Right, -0.12f) * new Basis(Vector3.Up, -0.15f));

		k.Color = Colors.White;
		k.CommitTo(gen, "StationMesh");

		var body = new StaticBody3D { Name = "StationBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		gen.AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(-hw, wallH * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(0.16f, wallH, d) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(hw, wallH * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(0.16f, wallH, d) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, wallH * 0.5f, -hd), Shape = new BoxShape3D { Size = new Vector3(w, wallH, 0.16f) } });
		foreach (int s in new[] { -1, 1 })
			body.AddChild(new CollisionShape3D { Position = new Vector3(s * (doorW * 0.5f + sideW * 0.5f), wallH * 0.5f, hd), Shape = new BoxShape3D { Size = new Vector3(sideW, wallH, 0.16f) } });
		foreach (var (c, s) in cols) body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
	}

	// ------------------------------------------------------------------ mist + sound

	private void BuildMist(float hw, float farZ, RandomNumberGenerator rng)
	{
		var ramp = new Gradient();
		ramp.SetColor(0, new Color(1, 1, 1, 0));
		ramp.SetColor(1, new Color(1, 1, 1, 0));
		ramp.AddPoint(0.35f, new Color(1, 1, 1, 0.7f));
		ramp.AddPoint(0.75f, new Color(1, 1, 1, 0.4f));
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(hw * 0.9f, 0.15f, CrossingLength * 0.42f),
			Direction = Vector3.Up,
			Spread = 20f,
			InitialVelocityMin = 0.05f,
			InitialVelocityMax = 0.18f,
			Gravity = Vector3.Zero,
			ScaleMin = 3f,
			ScaleMax = 6f,
			ColorRamp = new GradientTexture1D { Gradient = ramp },
		};
		var draw = new QuadMesh { Size = new Vector2(1f, 1f) };
		draw.Material = new StandardMaterial3D
		{
			AlbedoTexture = PropTextures.Puff(),
			AlbedoColor = new Color(0.85f, 0.87f, 0.88f, 0.1f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		var mist = new GpuParticles3D
		{
			Name = "Mist",
			Amount = 22,
			Lifetime = 9.0,
			Preprocess = 6.0,
			ProcessMaterial = pm,
			DrawPass1 = draw,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			VisibilityAabb = new Aabb(new Vector3(-hw, -1, farZ - 4), new Vector3(hw * 2f, 4, CrossingLength + 8)),
		};
		AddChild(mist);
		mist.Position = new Vector3(0, WaterLevel + 0.2f, farZ * 0.5f);
	}

	private void BuildSound()
	{
		const string path = "res://assets/audio/ambient/lake_loop.wav";
		if (!ResourceLoader.Exists(path)) return;
		var player = new AudioStreamPlayer3D
		{
			Name = "LakeSound",
			Stream = GD.Load<AudioStream>(path),
			Bus = "Water",
			UnitSize = 20f,
			MaxDistance = 90f,
		};
		player.AddChild(new AmbienceLoop { BaseVolumeDb = -4f });
		AddChild(player);
		player.Position = new Vector3(0, WaterLevel + 0.3f, -CrossingLength * 0.5f);
	}
}
