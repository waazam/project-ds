using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.StationParts;

namespace ProjectDS.World;

/// <summary>
/// Behind the iron door: the end of Act 13. A rusted corridor, then a tall hall that is no part of any
/// ranger station — riveted plate, pipes sweating down the walls, chains, steam, flesh grown over the
/// iron in swollen lumps, and in the flesh, eyes, every one of them turning to follow the player. In the
/// middle of it, plain and wooden and completely out of place, a staircase like the ones in the woods,
/// climbing toward a white light. NEVER GO UP THEM. Going up them is the end: the light takes the
/// screen, the final checkpoint (<see cref="Checkpoint.Act13Finished"/>), the credits.
///
/// Local space: z=0 is the lobby's back wall (the iron door); the corridor runs +Z to the hall.
/// </summary>
public partial class StationRoom3 : Node3D
{
	public const float CorridorEnd = 8f, HallEnd = 20f, HallHalf = 5f, HallHeight = 7f;
	private const float StairFoot = 11f, StairRun = 0.42f, StairRise = 0.3f;
	private const int Steps = 16;
	public static float StairTopZ => StairFoot + Steps * StairRun;
	public bool Solved { get; private set; }
	public Vector3 StairFootWorld => ToGlobal(new Vector3(0, 0.05f, StairFoot - 0.8f));
	public Vector3 StairTopWorld => ToGlobal(new Vector3(0, Steps * StairRise + 0.05f, StairTopZ + 0.6f));

	private readonly List<Node3D> _eyes = new();
	private OmniLight3D _white;
	private AudioStreamPlayer3D _drone, _hum;
	private bool _ending;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		var rng = new RandomNumberGenerator { Seed = 1313 };
		var body = new StaticBody3D { Name = "Shell", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		AddChild(body);
		var k = new MeshKit();
		k.Mat(StationTextures.RustPlateMat);
		k.Color = Colors.White;
		// the corridor
		void Slab(Vector3 c, Vector3 s, bool collide = true)
		{
			BuildKit.Box(k, c, s, 0.8f);
			if (collide) body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
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
		Slab(new Vector3(0, -0.05f, hc), new Vector3(HallHalf * 2f, 0.1f, hz * 2f));
		// grating over the floor, pipes up the walls and across the roof, chains
		k.Mat(StationTextures.RustPlateMat);
		k.Color = new Color(0.55f, 0.48f, 0.42f);
		for (float x = -HallHalf + 0.3f; x < HallHalf; x += 0.22f)
			BuildKit.Box(k, new Vector3(x, 0.02f, hc), new Vector3(0.04f, 0.04f, hz * 2f - 0.4f), 2f);
		for (float x = -HallHalf + 0.6f; x < HallHalf; x += 1.1f)
			StationProps.Pipe(k, new Vector3(x, 0, HallEnd - 0.35f), new Vector3(x, HallHeight, HallEnd - 0.35f), rng.RandfRange(0.07f, 0.16f));
		for (float z = CorridorEnd + 1f; z < HallEnd; z += 2.2f)
		{
			StationProps.Pipe(k, new Vector3(-HallHalf + 0.3f, HallHeight - 0.4f, z), new Vector3(HallHalf - 0.3f, HallHeight - 0.4f, z), 0.12f);
			StationProps.Pipe(k, new Vector3(-HallHalf + 0.3f, 0, z), new Vector3(-HallHalf + 0.3f, HallHeight, z), 0.09f);
			StationProps.Pipe(k, new Vector3(HallHalf - 0.3f, 0, z + 1f), new Vector3(HallHalf - 0.3f, HallHeight, z + 1f), 0.09f);
		}
		StationProps.Pipe(k, new Vector3(-1.1f, 2.3f, 0.3f), new Vector3(-1.1f, 2.3f, CorridorEnd - 0.2f), 0.08f);
		StationProps.Pipe(k, new Vector3(1.1f, 0.3f, 0.3f), new Vector3(1.1f, 0.3f, CorridorEnd - 0.2f), 0.06f);
		for (int i = 0; i < 9; i++)
			StationProps.Chain(k, new Vector3(rng.RandfRange(-HallHalf + 0.8f, HallHalf - 0.8f), HallHeight, rng.RandfRange(CorridorEnd + 1f, HallEnd - 1f)), rng.RandfRange(1.5f, 4.5f));
		k.CommitTo(this, "Hell", true);

		// flesh over the iron, and eyes in the flesh
		var flesh = new MeshKit();
		var spots = new List<(Vector3 at, Vector3 n)>();
		for (int i = 0; i < 16; i++)
		{
			int wall = i % 3;
			Vector3 at, n;
			if (wall == 0) { at = new Vector3(-HallHalf + 0.15f, rng.RandfRange(0.6f, HallHeight - 0.8f), rng.RandfRange(CorridorEnd + 0.8f, HallEnd - 0.8f)); n = Vector3.Right; }
			else if (wall == 1) { at = new Vector3(HallHalf - 0.15f, rng.RandfRange(0.6f, HallHeight - 0.8f), rng.RandfRange(CorridorEnd + 0.8f, HallEnd - 0.8f)); n = Vector3.Left; }
			else { at = new Vector3(rng.RandfRange(-HallHalf + 0.8f, HallHalf - 0.8f), rng.RandfRange(1.5f, HallHeight - 0.8f), HallEnd - 0.15f); n = Vector3.Forward; }
			StationProps.Growth(flesh, at, n, rng.RandfRange(0.7f, 1.3f), i * 13);
			spots.Add((at, n));
		}
		for (int i = 0; i < 5; i++)
		{
			Vector3 at = new(rng.RandfRange(-1f, 1f) * 1.05f, rng.RandfRange(0.4f, 2.2f), rng.RandfRange(1.2f, CorridorEnd - 1f));
			Vector3 n = at.X > 0 ? Vector3.Left : Vector3.Right;
			at.X = at.X > 0 ? 1.18f : -1.18f;
			StationProps.Growth(flesh, at, n, rng.RandfRange(0.4f, 0.7f), 300 + i);
			spots.Add((at, n));
		}
		flesh.CommitTo(this, "Flesh", true);
		foreach (var (at, n) in spots)
		{
			int count = rng.RandiRange(1, 3);
			for (int e = 0; e < count; e++)
			{
				var socket = new Node3D { Name = "WallEye", Position = at + n * 0.35f + new Vector3(rng.RandfRange(-0.25f, 0.25f), rng.RandfRange(-0.25f, 0.25f), rng.RandfRange(-0.25f, 0.25f)) * (1f - Mathf.Abs(n.Z)) };
				AddChild(socket);
				socket.Scale = Vector3.One * rng.RandfRange(0.1f, 0.28f);
				LakeCreature.BuildEye(socket);
				_eyes.Add(socket);
			}
		}

		// the staircase, wooden, out of place, climbing to the light
		var s = new MeshKit();
		s.Mat(PropTextures.DeckMat);
		for (int i = 0; i < Steps; i++)
		{
			float top = (i + 1) * StairRise, z = StairFoot + (i + 0.5f) * StairRun;
			s.Color = new Color(0.52f, 0.5f, 0.46f) * (0.9f + 0.06f * (i % 3));
			BuildKit.Box(s, new Vector3(0, top - 0.03f, z), new Vector3(1.3f, 0.06f, StairRun + 0.02f), 1.4f);
		}
		s.Mat(PropTextures.PostMat);
		s.Color = new Color(0.45f, 0.42f, 0.38f);
		foreach (int side in new[] { -1, 1 })
			s.Cylinder(new Vector3(side * 0.65f, 0, StairFoot), new Vector3(side * 0.65f, Steps * StairRise, StairTopZ), 0.07f, 0.07f, 4, true);
		// the landing at the top, and the opening full of white light beyond it
		BuildKit.Box(s, new Vector3(0, Steps * StairRise - 0.05f, StairTopZ + 0.9f), new Vector3(1.6f, 0.1f, 1.8f), 1.4f);
		s.CommitTo(this, "Stairs", true);
		float h = Steps * StairRise, len = Steps * StairRun;
		body.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(1.3f, 0.1f, Mathf.Sqrt(len * len + h * h)) },
			Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Atan2(h, len)), new Vector3(0, h * 0.5f - 0.05f, StairFoot + len * 0.5f)),
		});
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, h - 0.05f, StairTopZ + 0.9f), Shape = new BoxShape3D { Size = new Vector3(1.6f, 0.1f, 1.8f) } });
		AddChild(new MeshInstance3D
		{
			Name = "TheLight", Mesh = new QuadMesh { Size = new Vector2(1.6f, 2.4f) }, Position = new Vector3(0, h + 1.2f, HallEnd - 0.17f),
			Rotation = new Vector3(0, Mathf.Pi, 0), MaterialOverride = StationTextures.Glow("st_thelight", new Color(1f, 0.97f, 0.9f), 4f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		_white = new OmniLight3D { Name = "White", LightColor = new Color(1f, 0.95f, 0.88f), LightEnergy = 2.5f, OmniRange = 9f, Position = new Vector3(0, h + 1.2f, HallEnd - 1.2f) };
		AddChild(_white);

		// red working light down the corridor and round the hall, steam at the pipe joints
		foreach (var at in new[] { new Vector3(0, 2.4f, 3f), new Vector3(0, 2.4f, 6.5f), new Vector3(-3.5f, 5.5f, 12f), new Vector3(3.5f, 5.5f, 16f), new Vector3(-3f, 1.5f, 18f) })
			AddChild(new OmniLight3D { LightColor = new Color(1f, 0.18f, 0.1f), LightEnergy = 1.4f, OmniRange = 6.5f, Position = at });
		foreach (var at in new[] { new Vector3(-HallHalf + 0.4f, 3.2f, 10f), new Vector3(HallHalf - 0.4f, 5f, 15f), new Vector3(0.9f, 2.3f, 4f) })
			AddChild(Steam(at));
		_drone = Loop("res://assets/audio/ambient/industrial_drone_loop.wav", new Vector3(0, 3f, 13f), "Unnatural", -4f, 12f);
		_hum = Loop("res://assets/audio/ambient/stairs_hum_loop.wav", new Vector3(0, h, StairTopZ), "Unnatural", -10f, 6f);

		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(1.6f, 2f, 1.2f) }, new Vector3(0, h + 1f, StairTopZ + 0.9f), OnTop, "StairTop");
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		// every eye in the walls follows them
		foreach (var e in _eyes)
		{
			Vector3 to = cam.GlobalPosition - e.GlobalPosition;
			if (to.LengthSquared() < 0.01f) continue;
			float s = e.Scale.X;
			e.GlobalBasis = Basis.LookingAt(to, Vector3.Up).Scaled(Vector3.One * s);
		}
		if (StoryBeat.Player(this) is { } p)
		{
			bool here = ToLocal(p.GlobalPosition).Z > 0.2f;
			if (_drone != null && here != _drone.Playing) { if (here) _drone.Play(); else _drone.Stop(); }
			if (_hum != null && here != _hum.Playing) { if (here) _hum.Play(); else _hum.Stop(); }
		}
	}

	private void OnTop(PlayerController player)
	{
		if (_ending) return;
		_ending = true;
		_ = Cutscene.Run(this, ct => End(player, ct), lockInput: true, freezeBody: true);
	}

	/// <summary>Up into the light: it swells until there is nothing else, then black, the last save, and the credits.</summary>
	private async Task End(PlayerController player, CancellationToken ct)
	{
		var rig = player.CameraRig;
		Vector3 light = ToGlobal(new Vector3(0, Steps * StairRise + 1.2f, HallEnd - 0.2f));
		double t = 0;
		while (t < 4.0)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / 4.0);
			_white.LightEnergy = Mathf.Lerp(2.5f, 16f, u * u);
			if (_hum != null) _hum.VolumeDb = Mathf.Lerp(-10f, 2f, u);
			Vector3 to = light - rig.Camera.GlobalPosition;
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * Mathf.Min(1f, dt * 3f), 0));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), Mathf.Min(1f, dt * 3f)));
			rig.FovSwim = Mathf.Lerp(0f, -18f, u);
		}
		var fader = StoryBeat.Fader(this);
		if (fader != null) { fader.SetBlack(true); }
		rig.FovSwim = 0f;
		_hum?.Stop();
		_drone?.Stop();
		Solved = true;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act13Finished);
		GD.Print("[story] Act 13: up the last staircase, into the light - the end");
		await Cutscene.Wait(this, 1.5, ct);
		if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending)
			await ending.Credits(fader, ct);
	}

	private static GpuParticles3D Steam(Vector3 at)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = Vector3.Up, Spread = 25f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1f, Gravity = new Vector3(0, 0.3f, 0),
			ScaleMin = 0.8f, ScaleMax = 1.8f,
			ColorRamp = new GradientTexture1D { Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0.3f), new Color(1, 1, 1, 0f) }, Offsets = new[] { 0f, 1f } } },
		};
		return new GpuParticles3D
		{
			Name = "Steam", Amount = 20, Lifetime = 2.5, Position = at, ProcessMaterial = pm,
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.5f,
				Material = new StandardMaterial3D { AlbedoTexture = LakeParts.LakeFx.SoftDot(), AlbedoColor = new Color(0.8f, 0.6f, 0.55f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true },
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
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
