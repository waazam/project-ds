using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Room 3, behind the iron door: the end of Act 13 and the start of Act 14. After the rot of the
/// lobby it is shockingly clean: cold, riveted steel plate, a steel floor, pipework and caged lamps
/// under a high ceiling, a machine hum somewhere in the walls. And the walls are hung, floor to
/// ceiling, with old portraits in ornate frames, dozens of them, every one with the face burnt out.
/// At the back, in the floor, a rectangular hole with concrete steps going down into the dark: the
/// stairwell (<see cref="Stairwell"/>).
///
/// Stepping through the door is Act 14's start (<see cref="Checkpoint.Act13Finished"/>).
///
/// Local space: z=0 is the lobby's back wall (the iron door); a short corridor runs +Z to the hall.
/// </summary>
public partial class StationRoom3 : Node3D
{
	public const float CorridorEnd = 6f, HallEnd = 20f, HallHalf = 5f, HallHeight = 7f;
	/// <summary>Where the stairwell sits (the middle of its shaft) in this room's space: its trench comes
	/// down the middle of the hall (x=0) toward the back, and the shaft is under the back wall.</summary>
	public static readonly Vector3 StairwellAt = new(Stairwell.A, 0, HallEnd);
	/// <summary>Crossing into the corridor: Act 13 done, Act 14 begun.</summary>
	public bool Entered { get; private set; }
	public Stairwell Stairs { get; private set; }
	public int Portraits { get; private set; }
	public Vector3 EntryWorld => ToGlobal(new Vector3(0, 0.05f, 1.2f));
	public Vector3 HoleWorld => Stairs?.TrenchTopWorld ?? ToGlobal(new Vector3(0, 0.05f, 13f));

	private AudioStreamPlayer3D _drone;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		var rng = new RandomNumberGenerator { Seed = 1313 };
		var body = new StaticBody3D { Name = "Shell", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "metal");
		AddChild(body);
		var k = new MeshKit();
		k.Mat(StairwellTextures.PlateMat);
		k.Color = Colors.White;
		void Slab(Vector3 c, Vector3 s, bool collide = true)
		{
			BuildKit.Box(k, c, s, 0.5f);
			if (collide) body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		// the corridor
		Slab(new Vector3(-1.3f, 1.3f, CorridorEnd * 0.5f), new Vector3(0.2f, 2.6f, CorridorEnd));
		Slab(new Vector3(1.3f, 1.3f, CorridorEnd * 0.5f), new Vector3(0.2f, 2.6f, CorridorEnd));
		Slab(new Vector3(0, 2.7f, CorridorEnd * 0.5f), new Vector3(2.8f, 0.2f, CorridorEnd), false);
		Slab(new Vector3(0, -0.05f, CorridorEnd * 0.5f), new Vector3(2.8f, 0.1f, CorridorEnd));
		// the hall
		float hz = (HallEnd - CorridorEnd) * 0.5f, hc = CorridorEnd + hz;
		Slab(new Vector3(-HallHalf, HallHeight * 0.5f, hc), new Vector3(0.3f, HallHeight, hz * 2f));
		Slab(new Vector3(HallHalf, HallHeight * 0.5f, hc), new Vector3(0.3f, HallHeight, hz * 2f));
		Slab(new Vector3(0, HallHeight * 0.5f, HallEnd), new Vector3(HallHalf * 2f, HallHeight, 0.3f));
		Slab(new Vector3(-(HallHalf + 1.3f) * 0.5f, HallHeight * 0.5f, CorridorEnd), new Vector3(HallHalf - 1.3f, HallHeight, 0.3f));
		Slab(new Vector3((HallHalf + 1.3f) * 0.5f, HallHeight * 0.5f, CorridorEnd), new Vector3(HallHalf - 1.3f, HallHeight, 0.3f));
		Slab(new Vector3(0, (HallHeight + 2.6f) * 0.5f, CorridorEnd), new Vector3(2.6f, HallHeight - 2.6f, 0.3f));
		Slab(new Vector3(0, HallHeight + 0.1f, hc), new Vector3(HallHalf * 2f, 0.2f, hz * 2f), false);
		k.CommitTo(this, "Plate", true);

		// the floor: steel plate, with the hole cut out of it
		var f = new MeshKit();
		f.Mat(StairwellTextures.TreadMat);
		f.Color = new Color(0.8f, 0.82f, 0.85f);
		float hx0 = StairwellAt.X - Stairwell.H, hx1 = StairwellAt.X - Stairwell.Inner;   // the hole's sides
		float hz0 = StairwellAt.Z + Stairwell.TrenchZ0, hz1 = StairwellAt.Z - Stairwell.H;  // and its ends
		void Floor(float x0, float x1, float z0, float z1)
		{
			if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) return;
			var c = new Vector3((x0 + x1) * 0.5f, -0.05f, (z0 + z1) * 0.5f);
			var s = new Vector3(x1 - x0, 0.1f, z1 - z0);
			BuildKit.Box(f, c, s, 0.6f);
			body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		Floor(-HallHalf, HallHalf, CorridorEnd, hz0);
		Floor(-HallHalf, hx0, hz0, hz1);
		Floor(hx1, HallHalf, hz0, hz1);
		Floor(-HallHalf, HallHalf, hz1, HallEnd);
		f.CommitTo(this, "Floor", true);

		// hazard paint round the hole, and two bollards with a chain sagging between them
		var hz_ = new MeshKit();
		hz_.Mat(StationParts.StationTextures.Flat("r3_hazard", new Color(0.75f, 0.6f, 0.08f), 0.7f, 0.2f));
		hz_.Color = Colors.White;
		for (float z = hz0 - 0.35f; z < hz1 - 0.2f; z += 0.5f)
		{
			BuildKit.Box(hz_, new Vector3(hx0 - 0.2f, 0.003f, z), new Vector3(0.22f, 0.006f, 0.25f));
			BuildKit.Box(hz_, new Vector3(hx1 + 0.2f, 0.003f, z), new Vector3(0.22f, 0.006f, 0.25f));
		}
		for (float x = hx0 - 0.3f; x < hx1 + 0.4f; x += 0.5f)
			BuildKit.Box(hz_, new Vector3(x, 0.003f, hz0 - 0.35f), new Vector3(0.25f, 0.006f, 0.22f));
		hz_.CommitTo(this, "Hazard", false);
		var bol = new MeshKit();
		bol.Mat(StationParts.StationTextures.Flat("r3_bollard", new Color(0.7f, 0.56f, 0.08f), 0.5f, 0.4f));
		bol.Color = Colors.White;
		Vector3 b0 = new(hx0 - 0.45f, 0, hz0 - 0.45f), b1 = new(hx1 + 0.45f, 0, hz0 - 0.45f);
		foreach (var b in new[] { b0, b1 })
		{
			bol.Cylinder(b, b + Vector3.Up * 0.9f, 0.05f, 0.05f, 8, true);
			body.AddChild(new CollisionShape3D { Position = b + Vector3.Up * 0.45f, Shape = new CylinderShape3D { Radius = 0.05f, Height = 0.9f } });
		}
		bol.Mat(StairwellTextures.RailMat);
		Vector3 prev = b0 + Vector3.Up * 0.85f;
		for (int i = 1; i <= 8; i++)
		{
			float u = i / 8f;
			Vector3 p = b0.Lerp(b1, u) + Vector3.Up * (0.85f - 0.3f * 4f * u * (1f - u));
			bol.Cylinder(prev, p, 0.012f, 0.012f, 4, false);
			prev = p;
		}
		bol.CommitTo(this, "Bollards", true);

		// pipes along the top of the walls and across the roof, conduit, a sealed hatch
		var pk = new MeshKit();
		pk.Mat(StairwellTextures.SteelMat);
		pk.Color = new Color(0.75f, 0.78f, 0.8f);
		for (float z = CorridorEnd + 1f; z < HallEnd; z += 2.8f)
			StationParts.StationProps.Pipe(pk, new Vector3(-HallHalf + 0.3f, HallHeight - 0.4f, z), new Vector3(HallHalf - 0.3f, HallHeight - 0.4f, z), 0.1f);
		foreach (float x in new[] { -HallHalf + 0.25f, HallHalf - 0.25f })
		{
			StationParts.StationProps.Pipe(pk, new Vector3(x, HallHeight - 0.9f, CorridorEnd + 0.4f), new Vector3(x, HallHeight - 0.9f, HallEnd - 0.4f), 0.14f);
			StationParts.StationProps.Pipe(pk, new Vector3(x, HallHeight - 1.3f, CorridorEnd + 0.4f), new Vector3(x, HallHeight - 1.3f, HallEnd - 0.4f), 0.06f);
		}
		StationParts.StationProps.Pipe(pk, new Vector3(-1.1f, 2.35f, 0.3f), new Vector3(-1.1f, 2.35f, CorridorEnd - 0.2f), 0.07f);
		pk.CommitTo(this, "Pipes", true);

		// caged lamps: a cold, even, working light, the only clean light in the building
		for (float z = CorridorEnd + 2f; z < HallEnd; z += 4.5f)
			foreach (float x in new[] { -2.4f, 2.4f })
				Lamp(new Vector3(x, HallHeight - 0.2f, z));
		Lamp(new Vector3(0, 2.55f, CorridorEnd * 0.5f), 0.9f);

		// the portraits: salon-hung, crowding the side walls and the back, all their faces burnt out
		HangPortraits(rng);

		// the stairwell under the hole
		Stairs = new Stairwell { Name = "Stairwell", Position = StairwellAt };
		AddChild(Stairs);

		_drone = Loop("res://assets/audio/ambient/industrial_drone_loop.wav", new Vector3(0, 3f, 13f), "Unnatural", -12f, 10f);
		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(2.4f, 2.4f, 1.2f) }, new Vector3(0, 1.2f, 1.4f), OnEntered, "Room3Entry");
		var s = StoryManager.Instance;
		if (s != null && s.Current >= Checkpoint.Act13Finished) Entered = true;
		SetProcess(true);
	}

	private void Lamp(Vector3 at, float energy = 1.4f)
	{
		var k = new MeshKit();
		k.Mat(StairwellTextures.RailMat);
		k.Color = Colors.White;
		k.Cylinder(at + Vector3.Up * 0.2f, at, 0.012f, 0.012f, 4, false);
		k.Cylinder(at, at + Vector3.Down * 0.18f, 0.16f, 0.1f, 8, false);
		k.CommitTo(this, "Lamp", false);
		AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f }, Position = at + Vector3.Down * 0.14f,
			MaterialOverride = StationParts.StationTextures.Glow("r3_lamp", new Color(0.85f, 0.92f, 1f), 2.2f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		AddChild(new OmniLight3D
		{
			Position = at + Vector3.Down * 0.4f, LightColor = new Color(0.82f, 0.9f, 1f), LightEnergy = energy, OmniRange = 7.5f,
			OmniAttenuation = 1.2f, ShadowEnabled = energy > 1f,
		});
	}

	/// <summary>Frames of every size crowded up the walls (as old galleries hung them), some askew.</summary>
	private void HangPortraits(RandomNumberGenerator rng)
	{
		int n = 0;
		void Wall(float x, float z0, float z1, float yaw)
		{
			float z = z0;
			while (z < z1 - 0.5f)
			{
				float w = rng.RandfRange(0.45f, 0.95f), h = w * rng.RandfRange(1.15f, 1.4f);
				if (z + w + 0.3f > z1) break;
				// a column of one to three, stacked
				int stack = rng.RandiRange(1, 3);
				float y = rng.RandfRange(1.0f, 1.6f);
				for (int i = 0; i < stack && y + h < HallHeight - 1.6f; i++)
				{
					float ww = i == 0 ? w : w * rng.RandfRange(0.7f, 1.05f), hh = ww * rng.RandfRange(1.15f, 1.4f);
					var p = new Node3D { Name = $"Portrait{n}", Position = new Vector3(x, y + hh * 0.5f, z + w * 0.5f) };
					AddChild(p);
					p.Rotation = new Vector3(0, yaw, rng.Randf() < 0.2f ? rng.RandfRange(-0.08f, 0.08f) : 0f);
					PortraitKit.Build(p, n, ww, hh);
					n++;
					y += hh + rng.RandfRange(0.35f, 0.6f);
				}
				z += w + rng.RandfRange(0.35f, 0.7f);
			}
		}
		// canvas faces +Z in its own space: turn it to face into the room from each wall
		Wall(-HallHalf + 0.2f, CorridorEnd + 0.6f, HallEnd - 0.4f, Mathf.Pi * 0.5f);
		Wall(HallHalf - 0.2f, CorridorEnd + 0.6f, HallEnd - 0.4f, -Mathf.Pi * 0.5f);
		// the back wall, either side of where the hole leads
		for (int i = 0; i < 4; i++)
		{
			float x = -4.2f + i * 1.1f, w = rng.RandfRange(0.5f, 0.8f), h = w * 1.3f;
			if (x > -1.2f) x += 3.4f;
			var p = new Node3D { Name = $"Portrait{n}", Position = new Vector3(x, rng.RandfRange(2.2f, 3.4f), HallEnd - 0.2f) };
			AddChild(p);
			p.Rotation = new Vector3(0, Mathf.Pi, 0);
			PortraitKit.Build(p, n, w, h);
			n++;
		}
		// a big one over the corridor door, looking down the hall at the hole
		var big = new Node3D { Name = "PortraitBig", Position = new Vector3(0, 4.6f, CorridorEnd + 0.2f) };
		AddChild(big);
		PortraitKit.Build(big, 99, 1.4f, 1.8f);
		Portraits = n + 1;
	}

	private void OnEntered(PlayerController player)
	{
		if (Entered) return;
		Entered = true;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act13Finished);
		GD.Print($"[story] Act 13 done: through the iron door. Act 14: Room 3 ({Portraits} portraits, faces burnt out)");
		if (player.GetNodeOrNull<Lantern>("Lantern") is { IsOn: false } && player.Inventory is { HasLantern: true })
			_ = StoryBeat.Caption(this, "It's dark down there.   [F] lantern", 6f, 2.6f, 1f);
	}

	public override void _Process(double delta)
	{
		if (StoryBeat.Player(this) is not { } p) return;
		Vector3 l = ToLocal(p.GlobalPosition);
		bool here = l.Z > 0.2f && l.Y > -1.5f && Mathf.Abs(l.X) < HallHalf + 0.5f;
		if (_drone != null && here != _drone.Playing) { if (here) _drone.Play(); else _drone.Stop(); }
	}

	private AudioStreamPlayer3D Loop(string path, Vector3 at, string bus, float db, float unit)
	{
		if (!ResourceLoader.Exists(path)) return null;
		var stream = GD.Load<AudioStream>(path);
		if (stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			stream = wav;
		}
		var p = new AudioStreamPlayer3D { Stream = stream, Bus = bus, VolumeDb = db, UnitSize = unit, MaxDistance = 40f, Position = at };
		AddChild(p);
		return p;
	}
}
