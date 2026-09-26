using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.SewerParts;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Act 17: the sewer (its save is <see cref="Checkpoint.Act16Finished"/>). Out of the black of the
/// closet door, a small landing and four steps go down into ankle-deep grey water in a long round
/// brick pipe, junk floating in it. The water drags at every step. At the end the pipe opens into a
/// great flooded cistern: brick pillars in rows, barrel vaults lost in the dark overhead, the round
/// mouths of other pipes all round the walls, heaps of debris in the water. In the middle, a square
/// concrete platform with a clean-cut square hole in it, pitch black, a slow smoke rising out of it,
/// and a single light on it from somewhere far above. The hole asks its question (<c>Sewer.Hole.cs</c>).
///
/// Local space: the floor (the bottom of the water) is y=0; the door is at z=0 and the pipe runs +Z to
/// the cistern, whose middle is the hole.
/// </summary>
public partial class Sewer : Node3D
{
	public const float Water = 0.15f;
	public const float PipeR = 1.8f, PipeCy = 1.4f, TunnelLen = 100f;
	public const float RoomX = 22f, RoomZ0 = TunnelLen, RoomZ1 = TunnelLen + 44f, Spring = 9f;
	public const float PlatHalf = 4.5f, PlatTop = 0.6f, HoleHalf = 1.1f;
	public static readonly Vector3 HoleLocal = new(0, PlatTop, (RoomZ0 + RoomZ1) * 0.5f);
	/// <summary>On the landing inside the door (Act 17's start), facing down the pipe.</summary>
	public static readonly Vector3 EntranceLocal = new(0, 0.85f, 0.7f);
	/// <summary>By the hole, on the platform (Act 17's end, until Act 18 is built).</summary>
	public static readonly Vector3 PlatformLocal = new(0, PlatTop + 0.05f, HoleLocal.Z - 3f);

	public Vector3 EntranceWorld => ToGlobal(EntranceLocal);
	public float EntranceYaw => GlobalRotation.Y + Mathf.Pi;   // facing +Z
	public Vector3 HoleWorld => ToGlobal(HoleLocal);
	public Vector3 TunnelEndWorld => ToGlobal(new Vector3(0, 0.05f, TunnelLen + 2f));
	public Vector3 PlatformEdgeWorld => ToGlobal(PlatformLocal);
	public Vector3 AlongWorld(float z) => ToGlobal(new Vector3(0, 0.05f, z));
	/// <summary>For tests: whether the player is wading, and how much it drags.</summary>
	public bool PlayerWading { get; private set; }

	private StaticBody3D _stone, _wet;
	private ShaderMaterial _waterMat;
	private readonly List<Node3D> _floaters = new();
	private readonly List<OmniLight3D> _lamps = new();
	private SpotLight3D _holeLight;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1717 };
	private float _t;
	private AudioStreamPlayer _drip;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private static void QuadN(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
	{
		if ((b - a).Cross(d - a).Dot(n) > 0) k.Quad(a, b, c, d, n);
		else k.Quad(b, a, d, c, n);
	}

	/// <summary>The inside of a round tube along +Z (or an arc of one: angles from the bottom, round
	/// through the right side), faces inward.</summary>
	private static void Tube(MeshKit k, float cx, float cy, float r, float z0, float z1, float a0, float a1, int seg, float zStep)
	{
		for (int i = 0; i < seg; i++)
		{
			float t0 = Mathf.Lerp(a0, a1, i / (float)seg), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
			Vector3 p0 = new(cx + r * Mathf.Sin(t0), cy - r * Mathf.Cos(t0), 0), p1 = new(cx + r * Mathf.Sin(t1), cy - r * Mathf.Cos(t1), 0);
			Vector3 n = -new Vector3(Mathf.Sin((t0 + t1) * 0.5f), -Mathf.Cos((t0 + t1) * 0.5f), 0);
			for (float z = z0; z < z1 - 0.001f; z += zStep)
			{
				float zb = Mathf.Min(z + zStep, z1);
				QuadN(k, p0 + Vector3.Back * z, p1 + Vector3.Back * z, p1 + Vector3.Back * zb, p0 + Vector3.Back * zb, n);
			}
		}
	}

	private void Build()
	{
		_stone = new StaticBody3D { Name = "Stone", CollisionLayer = 1, CollisionMask = 0 };
		_stone.SetMeta("surface", "stone");
		AddChild(_stone);
		_wet = new StaticBody3D { Name = "WaterFloor", CollisionLayer = 1, CollisionMask = 0 };
		_wet.SetMeta("surface", "water");
		AddChild(_wet);

		BuildTunnel();
		BuildRoom();
		BuildPlatform();
		BuildWater();
		BuildDebris();
		BuildLights();
		BuildHole();

		_drip = new AudioStreamPlayer { Name = "Air", Bus = "Unnatural", VolumeDb = -80f };
		if (ResourceLoader.Exists("res://assets/audio/ambient/sewer_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/sewer_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_drip.Stream = wav;
		}
		AddChild(_drip);
		SetProcess(true);
		GD.Print("[story] Act 17: the sewer - the pipe, the cistern, the hole");
	}

	private void Slab(MeshKit k, StaticBody3D body, Vector3 c, Vector3 s, float tint = 1f)
	{
		k.Color = Colors.White * tint;
		BuildKit.Box(k, c, s, 1f);
		body?.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
	}

	// ------------------------------------------------------------------ the pipe

	private void BuildTunnel()
	{
		var brick = new MeshKit();
		brick.Mat(SewerTextures.BrickMat);
		brick.Color = Colors.White;
		// the round brick pipe
		Tube(brick, 0, PipeCy, PipeR, 0, TunnelLen, 0, Mathf.Tau, 20, 2f);
		// the end at the door: a concrete bulkhead, and the door they came through
		var con = new MeshKit();
		con.Mat(SewerTextures.PipeMat);
		Slab(con, _stone, new Vector3(0, PipeCy, -0.1f), new Vector3(PipeR * 2f + 0.4f, PipeR * 2f + 0.4f, 0.2f), 0.8f);
		// the landing and four steps down into the water
		Slab(con, _stone, new Vector3(0, 0.4f, 0.6f), new Vector3(2.2f, 0.8f, 1.2f), 0.85f);
		for (int i = 0; i < 4; i++)
			Slab(con, null, new Vector3(0, 0.8f - (i + 1) * 0.2f - 0.2f + 0.1f, 1.2f + (i + 0.5f) * 0.3f), new Vector3(2f, 0.4f, 0.3f), 0.8f);
		float len = 1.2f, drop = 0.8f;
		_stone.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(2f, 0.1f, Mathf.Sqrt(len * len + drop * drop)) },
			Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Atan2(drop, len)), new Vector3(0, drop * 0.5f - 0.05f, 1.2f + len * 0.5f)),
		});
		// pipe walls (collision): the floor under the water, and a ring of slabs following the curve of
		// the brick all the way round (so nobody walks, or looks, into the wall where it curves in low down)
		_wet.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.1f, TunnelLen * 0.5f + 1f), Shape = new BoxShape3D { Size = new Vector3(2.6f, 0.2f, TunnelLen) } });
		const int ringSeg = 20;
		float chord = 2f * (PipeR + 0.15f) * Mathf.Tan(Mathf.Pi / ringSeg) + 0.05f;
		for (int i = 0; i < ringSeg; i++)
		{
			float a = (i + 0.5f) / ringSeg * Mathf.Tau;
			Vector3 radial = new(Mathf.Sin(a), -Mathf.Cos(a), 0);
			if (radial.Y < -0.75f) continue;   // the bottom is under the floor
			Vector3 c = new Vector3(0, PipeCy, TunnelLen * 0.5f) + radial * (PipeR + 0.15f - 0.02f);
			var basis = new Basis(Vector3.Back, a);   // local Y along the radius
			_stone.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(chord, 0.3f, TunnelLen) }, Transform = new Transform3D(basis, c) });
		}
		con.CommitTo(this, "Bulkhead", true);
		// the door behind them, shut
		var door = new MeshKit();
		door.Mat(StairwellTextures.SteelMat);
		door.Color = new Color(0.45f, 0.5f, 0.45f);
		BuildKit.Box(door, new Vector3(0, 0.8f + 1.05f, 0.03f), new Vector3(1.0f, 2.1f, 0.06f));
		door.Color = new Color(0.8f, 0.78f, 0.7f);
		BuildKit.Box(door, new Vector3(0.38f, 0.8f + 1.0f, 0.08f), new Vector3(0.1f, 0.03f, 0.04f));
		door.CommitTo(this, "Door", true);
		brick.CommitTo(this, "Pipe", true);
	}

	// ------------------------------------------------------------------ the cistern

	private void BuildRoom()
	{
		var brick = new MeshKit();
		brick.Mat(SewerTextures.BrickMat);
		float zc = (RoomZ0 + RoomZ1) * 0.5f, zl = RoomZ1 - RoomZ0, top = Spring + 5.5f;
		// the walls: the south one with the pipe's round mouth in it
		Slab(brick, _stone, new Vector3(-RoomX - 0.3f, top * 0.5f, zc), new Vector3(0.6f, top, zl), 0.9f);
		Slab(brick, _stone, new Vector3(RoomX + 0.3f, top * 0.5f, zc), new Vector3(0.6f, top, zl), 0.9f);
		Slab(brick, _stone, new Vector3(0, top * 0.5f, RoomZ1 + 0.3f), new Vector3(RoomX * 2f + 1.2f, top, 0.6f), 0.9f);
		float hw = PipeR + 0.2f;
		Slab(brick, _stone, new Vector3(-(RoomX + hw) * 0.5f, top * 0.5f, RoomZ0 - 0.3f), new Vector3(RoomX - hw + 0.6f, top, 0.6f), 0.9f);
		Slab(brick, _stone, new Vector3((RoomX + hw) * 0.5f, top * 0.5f, RoomZ0 - 0.3f), new Vector3(RoomX - hw + 0.6f, top, 0.6f), 0.9f);
		Slab(brick, _stone, new Vector3(0, (top + PipeCy + hw) * 0.5f, RoomZ0 - 0.3f), new Vector3(hw * 2f, top - PipeCy - hw, 0.6f), 0.9f);
		// fill the corners between the round mouth and the square gap
		for (int i = 0; i < 24; i++)
		{
			float a0 = i / 24f * Mathf.Tau, a1 = (i + 1) / 24f * Mathf.Tau;
			Vector3 c0 = new(PipeR * Mathf.Sin(a0), PipeCy - PipeR * Mathf.Cos(a0), RoomZ0), c1 = new(PipeR * Mathf.Sin(a1), PipeCy - PipeR * Mathf.Cos(a1), RoomZ0);
			Vector3 s0 = SquareEdge(a0, hw, PipeCy + 0.4f), s1 = SquareEdge(a1, hw, PipeCy + 0.4f);
			if (s0.Y < 0 && s1.Y < 0) continue;
			QuadN(brick, c0, c1, s1, s0, Vector3.Back);
			QuadN(brick, c0 + Vector3.Forward * 0.6f, c1 + Vector3.Forward * 0.6f, s1 + Vector3.Forward * 0.6f, s0 + Vector3.Forward * 0.6f, Vector3.Forward);
		}
		// the floor under the water
		_wet.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.1f, zc), Shape = new BoxShape3D { Size = new Vector3(RoomX * 2f, 0.2f, zl) } });
		var floor = new MeshKit();
		floor.Mat(SewerTextures.BrickMat);
		floor.Color = new Color(0.45f, 0.42f, 0.4f);
		// (round the platform: the hole goes down through where it stands)
		foreach (var (c, sz) in AroundPlatform(-0.1f, 0.2f)) BuildKit.Box(floor, c, sz, 1f);
		BuildKit.Box(floor, new Vector3(0, -0.4f, TunnelLen * 0.5f), new Vector3(2.4f, 0.2f, TunnelLen), 1f);
		floor.CommitTo(this, "Floor", false);

		// pillars in rows, and barrel vaults along the room between them
		float[] px = { -16.5f, -5.5f, 5.5f, 16.5f }, pz = { RoomZ0 + 6f, RoomZ0 + 15f, RoomZ0 + 29f, RoomZ0 + 38f };
		foreach (float x in px)
			foreach (float z in pz)
			{
				Slab(brick, _stone, new Vector3(x, Spring * 0.5f, z), new Vector3(1.4f, Spring, 1.4f), 0.95f);
				Slab(brick, null, new Vector3(x, 0.25f, z), new Vector3(1.7f, 0.5f, 1.7f), 0.8f);        // a plinth
				Slab(brick, null, new Vector3(x, Spring - 0.2f, z), new Vector3(1.7f, 0.4f, 1.7f), 0.8f); // a capital
			}
		// arches along each row of pillars, pillar to pillar (and to the end walls), for the vaults to rest
		// on: a brick web over each span, its underside a shallow segmental curve up from the capitals
		float[] supports = { RoomZ0, pz[0], pz[1], pz[2], pz[3], RoomZ1 };
		const float archTop = Spring + 1.1f, springY = Spring - 0.4f, crownRise = 0.9f, archHalf = 0.62f;
		foreach (float x in px)
			for (int sp = 0; sp < supports.Length - 1; sp++)
			{
				float za = supports[sp] + (sp == 0 ? 0f : 0.7f), zb = supports[sp + 1] - (sp == supports.Length - 2 ? 0f : 0.7f);
				const int n = 12;
				float Under(float z) { float u = (z - za) / (zb - za) * 2f - 1f; return springY + crownRise * (1f - u * u); }
				brick.Color = Colors.White * 0.9f;
				for (int i = 0; i < n; i++)
				{
					float z0 = Mathf.Lerp(za, zb, i / (float)n), z1 = Mathf.Lerp(za, zb, (i + 1) / (float)n);
					float y0 = Under(z0), y1 = Under(z1);
					foreach (int side in new[] { -1, 1 })
					{
						float fx = x + side * archHalf;
						QuadN(brick, new Vector3(fx, y0, z0), new Vector3(fx, y1, z1), new Vector3(fx, archTop, z1), new Vector3(fx, archTop, z0), new Vector3(side, 0, 0));
					}
					QuadN(brick, new Vector3(x - archHalf, y0, z0), new Vector3(x + archHalf, y0, z0), new Vector3(x + archHalf, y1, z1), new Vector3(x - archHalf, y1, z1), Vector3.Down);
				}
			}
		float[] bays = { -RoomX, -16.5f, -5.5f, 5.5f, 16.5f, RoomX };
		for (int b = 0; b < bays.Length - 1; b++)
		{
			float cx = (bays[b] + bays[b + 1]) * 0.5f, r = (bays[b + 1] - bays[b]) * 0.5f;
			Tube(brick, cx, Spring, r, RoomZ0, RoomZ1, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 10, 2f);
			// ribs across the vault over each row of pillars
			foreach (float z in pz)
				for (int i = 0; i < 10; i++)
				{
					float t0 = Mathf.Pi * 0.5f + i / 10f * Mathf.Pi, t1 = Mathf.Pi * 0.5f + (i + 1) / 10f * Mathf.Pi;
					Vector3 a = new(cx + (r - 0.25f) * Mathf.Sin(t0), Spring - (r - 0.25f) * Mathf.Cos(t0), z);
					Vector3 c = new(cx + (r - 0.25f) * Mathf.Sin(t1), Spring - (r - 0.25f) * Mathf.Cos(t1), z);
					brick.Beam(a, c, 0.7f, 0.45f, 1f, Vector3.Back);
				}
		}
		brick.CommitTo(this, "Cistern", true);

		// the mouths of other pipes, all round the walls: black, a concrete lip, one with a light far down it
		var lip = new MeshKit();
		lip.Mat(SewerTextures.PipeMat);
		lip.Color = Colors.White;
		var black = new MeshKit();
		black.Mat(new StandardMaterial3D { AlbedoColor = Colors.Black, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
		black.Color = Colors.White;
		var mouths = new List<(Vector3 at, Vector3 n, float r)>
		{
			(new Vector3(-RoomX, 1.6f, RoomZ0 + 10f), Vector3.Right, 1.5f), (new Vector3(-RoomX, 2.4f, RoomZ0 + 22f), Vector3.Right, 1.1f),
			(new Vector3(-RoomX, 1.2f, RoomZ0 + 34f), Vector3.Right, 1.2f), (new Vector3(RoomX, 1.4f, RoomZ0 + 11f), Vector3.Left, 1.3f),
			(new Vector3(RoomX, 2.0f, RoomZ0 + 25f), Vector3.Left, 1.6f), (new Vector3(RoomX, 1.3f, RoomZ0 + 36f), Vector3.Left, 1.1f),
			(new Vector3(-11f, 1.8f, RoomZ1), Vector3.Forward, 1.7f), (new Vector3(0f, 3.2f, RoomZ1), Vector3.Forward, 1.0f),
			(new Vector3(11f, 1.5f, RoomZ1), Vector3.Forward, 1.5f), (new Vector3(-12f, 3.5f, RoomZ0), Vector3.Back, 0.9f),
			(new Vector3(12f, 1.3f, RoomZ0), Vector3.Back, 1.2f),
		};
		for (int i = 0; i < mouths.Count; i++)
		{
			var (at, n, r) = mouths[i];
			lip.Cylinder(at - n * 0.05f, at + n * 0.45f, r + 0.15f, r + 0.15f, 20, false);
			black.Cylinder(at + n * 0.01f, at + n * 0.03f, r, r, 20, true);
			if (i == 7)   // far down this one, a light
				AddChild(new MeshInstance3D
				{
					Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f }, Position = at + n * 0.05f,
					MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color(1f, 0.95f, 0.85f), EmissionEnergyMultiplier = 3f },
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				});
		}
		lip.CommitTo(this, "PipeLips", true);
		black.CommitTo(this, "PipeDark", false);
		// graffiti by the pipes, old and meaningless
		string[] tags = { "BRJK", "TOASY", "AM", "NO WAY OUT", "DOWN HERE", "KEEP OUT", "SEE" };
		var font = new SystemFont { FontNames = new[] { "Ink Free", "Segoe Print", "Comic Sans MS" } };
		for (int i = 0; i < tags.Length; i++)
		{
			var (at, n, r) = mouths[(i * 3) % mouths.Count];
			var l = new Label3D
			{
				Text = tags[i], Font = font, FontSize = 96, PixelSize = 0.005f, Shaded = true, OutlineSize = 0,
				Modulate = new[] { new Color(0.8f, 0.2f, 0.15f, 0.85f), new Color(0.2f, 0.35f, 0.8f, 0.85f), new Color(0.15f, 0.15f, 0.15f, 0.85f) }[i % 3],
			};
			AddChild(l);
			l.GlobalPosition = ToGlobal(at + n * 0.02f + Vector3.Up * (r + 0.6f) + new Vector3(n.Z, 0, -n.X) * _rng.RandfRange(-1f, 1f));
			l.LookAt(l.GlobalPosition - (ToGlobal(n) - ToGlobal(Vector3.Zero)), Vector3.Up);
			l.RotateObjectLocal(Vector3.Back, _rng.RandfRange(-0.12f, 0.12f));
		}
	}

	/// <summary>The room's floor area as four pieces round the platform (centre, size), at height <paramref name="y"/>.</summary>
	private static IEnumerable<(Vector3 c, Vector3 s)> AroundPlatform(float y, float thick)
	{
		float zc = HoleLocal.Z, p = PlatHalf + 0.3f;
		yield return (new Vector3(0, y, (RoomZ0 + zc - p) * 0.5f), new Vector3(RoomX * 2f, thick, zc - p - RoomZ0));
		yield return (new Vector3(0, y, (zc + p + RoomZ1) * 0.5f), new Vector3(RoomX * 2f, thick, RoomZ1 - zc - p));
		yield return (new Vector3(-(RoomX + p) * 0.5f, y, zc), new Vector3(RoomX - p, thick, 2f * p));
		yield return (new Vector3((RoomX + p) * 0.5f, y, zc), new Vector3(RoomX - p, thick, 2f * p));
	}

	/// <summary>Where a ray from the pipe's centre at angle <paramref name="a"/> meets the square gap round it.</summary>
	private static Vector3 SquareEdge(float a, float hw, float cy)
	{
		Vector2 d = new(Mathf.Sin(a), -Mathf.Cos(a));
		float s = hw / Mathf.Max(Mathf.Abs(d.X), Mathf.Abs(d.Y));
		Vector2 p = d * s;
		return new Vector3(p.X, PipeCy + p.Y, RoomZ0);
	}

	private void BuildPlatform()
	{
		var con = new MeshKit();
		con.Mat(StairwellTextures.CleanMat);
		float zc = HoleLocal.Z;
		// the platform, as four slabs round the hole, and a step down on every side
		float inner = HoleHalf, outer = PlatHalf;
		Slab(con, _stone, new Vector3(0, PlatTop * 0.5f, zc - (outer + inner) * 0.5f), new Vector3(outer * 2f, PlatTop, outer - inner), 0.9f);
		Slab(con, _stone, new Vector3(0, PlatTop * 0.5f, zc + (outer + inner) * 0.5f), new Vector3(outer * 2f, PlatTop, outer - inner), 0.9f);
		Slab(con, _stone, new Vector3(-(outer + inner) * 0.5f, PlatTop * 0.5f, zc), new Vector3(outer - inner, PlatTop, inner * 2f), 0.9f);
		Slab(con, _stone, new Vector3((outer + inner) * 0.5f, PlatTop * 0.5f, zc), new Vector3(outer - inner, PlatTop, inner * 2f), 0.9f);
		// the step all round: a ring, so nothing is under the hole but the dark
		float so = outer + 0.6f;
		Slab(con, null, new Vector3(0, PlatTop * 0.25f, zc - (so + outer) * 0.5f), new Vector3(so * 2f, PlatTop * 0.5f, so - outer), 0.8f);
		Slab(con, null, new Vector3(0, PlatTop * 0.25f, zc + (so + outer) * 0.5f), new Vector3(so * 2f, PlatTop * 0.5f, so - outer), 0.8f);
		Slab(con, null, new Vector3(-(so + outer) * 0.5f, PlatTop * 0.25f, zc), new Vector3(so - outer, PlatTop * 0.5f, outer * 2f), 0.8f);
		Slab(con, null, new Vector3((so + outer) * 0.5f, PlatTop * 0.25f, zc), new Vector3(so - outer, PlatTop * 0.5f, outer * 2f), 0.8f);
		con.CommitTo(this, "Platform", true);
		// the step all round is for looking at: underfoot, a gentle slope up each side
		const float run = 1.6f;
		float slope = Mathf.Atan2(PlatTop, run), len = Mathf.Sqrt(run * run + PlatTop * PlatTop);
		for (int i = 0; i < 4; i++)
		{
			var face = new Basis(Vector3.Up, i * Mathf.Pi * 0.5f);   // i=0: the ramp on the -Z side, rising toward +Z
			Vector3 c = face * new Vector3(0, PlatTop * 0.5f - 0.05f, -(outer + run * 0.5f));
			_stone.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(outer * 2f, 0.1f, len) },
				Transform = new Transform3D(face * new Basis(Vector3.Right, -slope), c + new Vector3(0, 0, zc)),   // -θ about X raises the +Z end
			});
		}
		// the hole: straight-sided, black all the way down (one box seen from inside, so nothing shows through)
		AddChild(new MeshInstance3D
		{
			Name = "Hole", Mesh = new BoxMesh { Size = new Vector3(inner * 2f - 0.01f, 6f, inner * 2f - 0.01f) },
			Position = new Vector3(0, PlatTop - 3f - 0.002f, zc),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Black, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Front, DisableFog = true },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		// nobody falls in by accident
		_stone.AddChild(new CollisionShape3D { Position = new Vector3(0, PlatTop + 0.6f, zc), Shape = new BoxShape3D { Size = new Vector3(inner * 2f, 1.2f, inner * 2f) } });
		// smoke, rising slowly out of it
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(inner * 0.8f, 0.1f, inner * 0.8f),
			Direction = Vector3.Up, Spread = 25f, InitialVelocityMin = 0.15f, InitialVelocityMax = 0.4f, Gravity = new Vector3(0, 0.05f, 0),
			ScaleMin = 1.2f, ScaleMax = 2.6f, DampingMin = 0.05f, DampingMax = 0.1f,
			ColorRamp = new GradientTexture1D { Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0f), new Color(1, 1, 1, 0.05f), new Color(1, 1, 1, 0f) }, Offsets = new[] { 0f, 0.35f, 1f } } },
		};
		AddChild(new GpuParticles3D
		{
			Name = "Smoke", Amount = 14, Lifetime = 7.0, Position = new Vector3(0, PlatTop + 0.9f, zc), ProcessMaterial = pm, Preprocess = 6.0,
			VisibilityAabb = new Aabb(new Vector3(-4, -1, -4), new Vector3(8, 8, 8)),
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One,
				Material = new StandardMaterial3D
				{
					AlbedoTexture = LakeParts.LakeFx.SoftDot(), AlbedoColor = new Color(0.7f, 0.72f, 0.74f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
					// lit by the light over the hole (not glowing on its own), and faded out close to the eye:
					// standing in it at the hole's edge must not grey out the whole room
					DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha, DistanceFadeMinDistance = 2f, DistanceFadeMaxDistance = 8f,
					ProximityFadeEnabled = true, ProximityFadeDistance = 0.8f,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	private void BuildWater()
	{
		_waterMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/basement_water.gdshader") };
		_waterMat.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		_waterMat.SetShaderParameter("drain", new Vector2(9999f, 9999f));
		_waterMat.SetShaderParameter("water_color", new Color(0.13f, 0.14f, 0.13f));
		_waterMat.SetShaderParameter("scum_color", new Color(0.3f, 0.3f, 0.27f));
		AddChild(new MeshInstance3D
		{
			Name = "TunnelWater", Mesh = new PlaneMesh { Size = new Vector2(2.5f, TunnelLen - 2.3f), SubdivideDepth = 20 }, Position = new Vector3(0, Water, 2.3f + (TunnelLen - 2.3f) * 0.5f),
			MaterialOverride = _waterMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		int n = 0;
		foreach (var (c, sz) in AroundPlatform(Water, 0f))
			AddChild(new MeshInstance3D
			{
				Name = $"RoomWater{n++}", Mesh = new PlaneMesh { Size = new Vector2(sz.X, sz.Z), SubdivideWidth = 6, SubdivideDepth = 6 }, Position = c,
				MaterialOverride = _waterMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
	}

	/// <summary>Junk floating in the water, and heaps of it against the walls and pillars.</summary>
	private void BuildDebris()
	{
		var wood = new StandardMaterial3D { AlbedoColor = new Color(0.28f, 0.22f, 0.16f), Roughness = 0.9f };
		var trash = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.09f), Roughness = 0.35f, MetallicSpecular = 0.6f };
		var pale = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.6f, 0.55f), Roughness = 0.6f };
		Material[] mats = { wood, trash, pale };
		void Floater(Vector3 at, int kind)
		{
			var n = new Node3D { Position = at };
			var mi = new MeshInstance3D { MaterialOverride = mats[kind % 3], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			mi.Mesh = kind switch
			{
				0 => new BoxMesh { Size = new Vector3(_rng.RandfRange(0.4f, 1.1f), 0.04f, _rng.RandfRange(0.08f, 0.16f)) },
				1 => new SphereMesh { Radius = 0.15f, Height = 0.16f },
				_ => new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.04f, Height = 0.22f },
			};
			if (kind == 2) mi.Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0);
			n.AddChild(mi);
			n.Rotation = new Vector3(0, _rng.RandfRange(0, Mathf.Tau), 0);
			n.SetMeta("phase", _rng.RandfRange(0, Mathf.Tau));
			AddChild(n);
			_floaters.Add(n);
		}
		for (float z = 5f; z < TunnelLen; z += _rng.RandfRange(2f, 5f))
			Floater(new Vector3(_rng.RandfRange(-0.9f, 0.9f), Water, z), _rng.RandiRange(0, 2));
		for (int i = 0; i < 70; i++)
		{
			Vector3 at = new(_rng.RandfRange(-RoomX + 1f, RoomX - 1f), Water, _rng.RandfRange(RoomZ0 + 1f, RoomZ1 - 1f));
			if (Mathf.Abs(at.X) < PlatHalf + 1f && Mathf.Abs(at.Z - HoleLocal.Z) < PlatHalf + 1f) continue;
			Floater(at, _rng.RandiRange(0, 2));
		}
		// heaps: broken bricks, planks, bags
		var heap = new MeshKit();
		heap.Mat(SewerTextures.BrickMat);
		var bags = new MeshKit();
		bags.Mat(trash);
		Vector3[] spots = { new(-19f, 0, RoomZ0 + 4f), new(18f, 0, RoomZ0 + 40f), new(-8f, 0, RoomZ1 - 3f), new(20f, 0, RoomZ0 + 18f), new(-20f, 0, RoomZ0 + 30f), new(9f, 0, RoomZ0 + 3f), new(-3f, 0, RoomZ0 + 36f) };
		foreach (var c in spots)
		{
			for (int i = 0; i < 26; i++)
			{
				Vector3 p = c + new Vector3(_rng.RandfRange(-1.8f, 1.8f), 0, _rng.RandfRange(-1.8f, 1.8f));
				float h = Mathf.Max(0f, 0.9f - p.DistanceTo(c) * 0.4f);
				var rot = new Basis(new Vector3(_rng.Randf(), _rng.Randf(), _rng.Randf()).Normalized(), _rng.RandfRange(0, 3f));
				heap.Color = Colors.White * _rng.RandfRange(0.6f, 1f);
				BuildKit.Box(heap, p + Vector3.Up * _rng.RandfRange(0f, h), new Vector3(0.24f, 0.08f, 0.12f), 1f, BuildKit.Face.None, rot);
			}
			for (int i = 0; i < 5; i++)
			{
				Vector3 p = c + new Vector3(_rng.RandfRange(-1.2f, 1.2f), 0.2f, _rng.RandfRange(-1.2f, 1.2f));
				bags.Cylinder(p + Vector3.Down * 0.2f, p + Vector3.Up * 0.25f, 0.3f, 0.12f, 8, true);
			}
			_stone.AddChild(new CollisionShape3D { Position = c + Vector3.Up * 0.3f, Shape = new CylinderShape3D { Radius = 1.4f, Height = 0.6f } });
		}
		heap.CommitTo(this, "Heaps", true);
		bags.CommitTo(this, "Bags", true);
	}

	private void BuildLights()
	{
		// the light on the hole, from somewhere far up in the dark
		_holeLight = new SpotLight3D
		{
			Name = "HoleLight", Position = HoleLocal + Vector3.Up * 12f, Rotation = new Vector3(-Mathf.Pi * 0.5f, 0, 0),
			LightColor = new Color(0.85f, 0.88f, 0.92f), LightEnergy = 3.2f, SpotRange = 16f, SpotAngle = 20f, SpotAttenuation = 0.8f, ShadowEnabled = true,
		};
		AddChild(_holeLight);
		// a few old sodium lamps on the walls, and two in the pipe
		foreach (var at in new[] { new Vector3(-RoomX + 0.4f, 4f, RoomZ0 + 16f), new Vector3(RoomX - 0.4f, 4f, RoomZ0 + 30f), new Vector3(-10f, 5f, RoomZ1 - 0.4f), new Vector3(10f, 4.5f, RoomZ0 + 0.4f) })
			AddLamp(at, 1.3f, 14f);
		foreach (var at in new[] { new Vector3(-RoomX + 0.4f, 4f, RoomZ0 + 36f), new Vector3(RoomX - 0.4f, 4f, RoomZ0 + 8f), new Vector3(10f, 5f, RoomZ1 - 0.4f), new Vector3(-10f, 4.5f, RoomZ0 + 0.4f) })
			AddLamp(at, 1.0f, 12f);
		// dim, high, wide light up in the vaults, so the size of the place can be felt
		foreach (float x in new[] { -11f, 0f, 11f })
			foreach (float z in new[] { RoomZ0 + 10f, RoomZ1 - 10f })
			{
				var l = new OmniLight3D { Position = new Vector3(x, Spring + 2f, z), LightColor = new Color(0.75f, 0.6f, 0.45f), LightEnergy = 0.55f, OmniRange = 22f, OmniAttenuation = 0.9f, ShadowEnabled = false };
				AddChild(l);
				_lamps.Add(l);
			}
		AddLamp(new Vector3(0, PipeCy + 1.3f, 35f), 0.6f, 7f);
		AddLamp(new Vector3(0, PipeCy + 1.3f, 72f), 0.6f, 7f);
		AddChild(new OmniLight3D { Name = "FarPipeGlow", Position = new Vector3(0, 3.2f, RoomZ1 - 1f), LightColor = new Color(1f, 0.95f, 0.85f), LightEnergy = 0.8f, OmniRange = 5f, ShadowEnabled = false });
	}

	private void AddLamp(Vector3 at, float energy, float range)
	{
		var l = new OmniLight3D { Position = at, LightColor = new Color(1f, 0.62f, 0.3f), LightEnergy = energy, OmniRange = range, OmniAttenuation = 1.2f, ShadowEnabled = false };
		AddChild(l);
		_lamps.Add(l);
		AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f }, Position = at,
			MaterialOverride = StationParts.StationTextures.Glow("sw_sodium", new Color(1f, 0.62f, 0.3f), 2f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	// ------------------------------------------------------------------ per frame

	/// <summary>In the water: ankle deep, dragging at the legs.</summary>
	/// <summary>How far a point is from the pipe's axis (tests: nothing should get out past the brick).</summary>
	public float PipeAxisDistance(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		return new Vector2(l.X, l.Y - PipeCy).Length();
	}

	public bool InWater(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		if (l.Y > 0.45f || l.Y < -1f) return false;
		bool tunnel = l.Z > 2.2f && l.Z < TunnelLen && Mathf.Abs(l.X) < PipeR;
		bool room = l.Z >= TunnelLen && l.Z < RoomZ1 && Mathf.Abs(l.X) < RoomX;
		return tunnel || room;
	}

	public bool Inside(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		return l.Z > -1f && l.Z < RoomZ1 + 1f && Mathf.Abs(l.X) < RoomX + 1f && l.Y > -3f && l.Y < 16f;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_t += dt;
		foreach (var f in _floaters)
		{
			float ph = (float)f.GetMeta("phase");
			f.Position = f.Position with { Y = Water + 0.012f * Mathf.Sin(_t * 0.9f + ph) };
			f.RotateY(dt * 0.02f * Mathf.Sin(ph));
		}
		var player = StoryBeat.Player(this);
		if (player == null) return;
		bool inside = Inside(player.GlobalPosition);
		PlayerWading = inside && InWater(player.GlobalPosition);
		player.WadeScale = Mathf.MoveToward(player.WadeScale, PlayerWading ? WadeDrag : 1f, dt * 3f);
		if (inside && StoryBeat.Atmosphere(this) is { } atmo)
		{
			atmo.Underground = Mathf.MoveToward(atmo.Underground, 1f, dt * 2f);
			atmo.UndergroundFogColor = atmo.UndergroundFogColor.Lerp(_teal ? TealFog : new Color(0.03f, 0.028f, 0.024f), Mathf.Min(1f, dt * 0.8f));
			atmo.UndergroundFogDensity = Mathf.MoveToward(atmo.UndergroundFogDensity, _teal ? 0.07f : 0.018f, dt * 0.03f);
		}
		if (_drip?.Stream != null)
		{
			_drip.VolumeDb = Mathf.MoveToward(_drip.VolumeDb, inside ? -12f : -80f, dt * 20f);
			if (_drip.VolumeDb > -79f && !_drip.Playing) _drip.Play();
			else if (_drip.VolumeDb <= -79f && _drip.Playing) _drip.Stop();
		}
		ProcessWhispers(player, dt);
	}

	/// <summary>How much the water slows the player (a fraction of their speed).</summary>
	[Export] public float WadeDrag = 0.55f;
}
