using System.Collections.Generic;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Systems;
using ProjectDS.World.BossParts;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Act 18: the boss room (its save is <see cref="Checkpoint.Act17Finished"/>). Down the hole in the
/// sewer, out of a round shaft in the ceiling, onto a steel catwalk that runs all the way round the top
/// of a great square pit, like an observation deck. Below it, a deep dark ocean of blood, and the thing
/// from the lake (<see cref="Leviathan"/>) in it. The walls above the catwalk are pipes, dozens of them,
/// with gauges, valves and handwheels; trusses and stage lights hang in the dark overhead.
///
/// It can't be fought, only drained: sixteen valves on the pipe walls, turned in the order a teal
/// spotlight picks them out from across the room (randomised, back and forth round the catwalk).
/// Each one drains the blood a little further, hurts it, and rots it; and it slams its limbs down on
/// the catwalk, faster and more of them as it goes. A hit is death, and the fight starts over. The
/// last valve empties the pit. Every eye it has bursts in turn, it shrieks and dies, and a door on the
/// far side opens onto a clean, well-lit room (<c>BossRoom.Fight.cs</c>).
///
/// Local space: the catwalk's deck is y=0; the pit's middle is x=z=0; the player drops in on the south
/// (-Z) side and the door out is in the north wall.
/// </summary>
public partial class BossRoom : Node3D
{
	// the catwalk is 2.6 m wide; the blood starts about five feet under it, the monster just below
	public const float Half = 19f, CatIn = 16.4f, PitFloor = -24f, Ceil = 16f, BloodStart = -1.5f;
	/// <summary>How far a slam reaches: 2 m at first, 3.2 m by the end (the late ones make you think twice).</summary>
	public const float SlamRadiusStart = 2.0f, SlamRadiusEnd = 3.2f;
	public float SlamRadius => Mathf.Lerp(SlamRadiusStart, SlamRadiusEnd, ValvesTurned / (float)ValvesToTurn);
	/// <summary>Valves on the walls, and how many of them the spotlight picks (the owner: ten, about seven minutes).</summary>
	public const int ValveCount = 16, ValvesToTurn = 10;
	/// <summary>Half the gap kept clear of pipes in front of the north door.</summary>
	private const float DoorClear = 1.1f;
	public static readonly Vector3 LandingLocal = new(0, 0.05f, -(Half + CatIn) * 0.5f);
	public static readonly Vector3 TidyLocal = new(0, 0.05f, Half + 3.2f);

	public Leviathan Beast { get; private set; }
	public List<Valve> Valves { get; } = new();
	public float BloodY { get; private set; } = BloodStart;
	public Vector3 LandingWorld => ToGlobal(LandingLocal);
	public Vector3 DoorWorld => ToGlobal(new Vector3(0, 0.05f, Half - 1.0f));
	public Vector3 TidyWorld => ToGlobal(TidyLocal);
	/// <summary>Act 19's library, through the door (Act 20's round room is inside it: <see cref="World.Library.Round"/>).</summary>
	public Library Library { get; private set; }
	public static readonly Vector3 LibraryLocal = new(0, 0, Half + 0.6f);

	private StaticBody3D _body;
	private ShaderMaterial _bloodMat;
	private MeshInstance3D _blood;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1918 };
	private readonly List<(SpotLight3D light, MeshInstance3D beam, ShaderMaterial mat)> _fixtures = new();
	private readonly List<OmniLight3D> _stage = new();
	private Node3D _door;
	private OmniLight3D _tidyLight;
	private AudioStreamPlayer _air;

	/// <summary>A point on the catwalk's middle line, <paramref name="s"/> metres round it from the
	/// south-west corner, going east along the south side first (then north, west, south).</summary>
	public Vector3 RingLocal(float s)
	{
		float c = (Half + CatIn) * 0.5f, side = 2f * c, per = 4f * side;
		s = ((s % per) + per) % per;
		int w = (int)(s / side);
		float u = s - w * side - c;
		return w switch
		{
			0 => new Vector3(u, 0.05f, -c),
			1 => new Vector3(c, 0.05f, u),
			2 => new Vector3(-u, 0.05f, c),
			_ => new Vector3(-c, 0.05f, -u),
		};
	}

	/// <summary>How far round the ring a local point is (inverse of <see cref="RingLocal"/>, by nearest side).</summary>
	public float RingOf(Vector3 local)
	{
		float c = (Half + CatIn) * 0.5f, side = 2f * c;
		float ax = Mathf.Abs(local.X), az = Mathf.Abs(local.Z);
		if (az >= ax) return local.Z < 0 ? Mathf.Clamp(local.X, -c, c) + c : 2f * side + (c - Mathf.Clamp(local.X, -c, c));
		return local.X > 0 ? side + Mathf.Clamp(local.Z, -c, c) + c : 3f * side + (c - Mathf.Clamp(local.Z, -c, c));
	}
	public float Perimeter => 8f * (Half + CatIn) * 0.5f;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "metal");
		AddChild(_body);
		BuildShell();
		BuildCatwalk();
		BuildPipes();
		BuildValves();
		BuildRig();
		BuildBlood();
		BuildDoor();
		Beast = new Leviathan { Name = "Leviathan", Position = new Vector3(0, PitFloor, 0) };
		AddChild(Beast);
		Beast.Level = BloodY - PitFloor;
		Beast.Impact += OnImpact;
		_air = new AudioStreamPlayer { Name = "Air", Bus = "Unnatural", VolumeDb = -80f };
		if (ResourceLoader.Exists("res://assets/audio/ambient/boss_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/boss_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_air.Stream = wav;
		}
		AddChild(_air);
		SetProcess(true);
		RestoreOrWait();
		GD.Print("[story] Act 18: the boss room - sixteen valves, the pit, the blood");
	}

	private void Slab(MeshKit k, Vector3 c, Vector3 s, bool collide = true, float tint = 1f, float uv = 1f)
	{
		k.Color = Colors.White * tint;
		BuildKit.Box(k, c, s, uv);
		if (collide) _body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
	}

	// ------------------------------------------------------------------ the shell

	private void BuildShell()
	{
		var con = new MeshKit();
		con.Mat(new StandardMaterial3D
		{
			AlbedoTexture = StairwellTextures.StainedConcrete, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.2f,
			Roughness = 0.9f, VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		});
		float h = Ceil - PitFloor, cy = (Ceil + PitFloor) * 0.5f;
		Slab(con, new Vector3(-Half - 0.3f, cy, 0), new Vector3(0.6f, h, Half * 2f + 1.2f), true, 0.8f);
		Slab(con, new Vector3(Half + 0.3f, cy, 0), new Vector3(0.6f, h, Half * 2f + 1.2f), true, 0.8f);
		Slab(con, new Vector3(0, cy, -Half - 0.3f), new Vector3(Half * 2f, h, 0.6f), true, 0.8f);
		// the north wall, with the door's gap in it
		const float dw = 1.3f, dh = 2.4f;
		Slab(con, new Vector3(-(Half + dw * 0.5f) * 0.5f, cy, Half + 0.3f), new Vector3(Half - dw * 0.5f, h, 0.6f), true, 0.8f);
		Slab(con, new Vector3((Half + dw * 0.5f) * 0.5f, cy, Half + 0.3f), new Vector3(Half - dw * 0.5f, h, 0.6f), true, 0.8f);
		Slab(con, new Vector3(0, (PitFloor + 0f) * 0.5f, Half + 0.3f), new Vector3(dw, -PitFloor, 0.6f), true, 0.8f);
		Slab(con, new Vector3(0, (dh + Ceil) * 0.5f, Half + 0.3f), new Vector3(dw, Ceil - dh, 0.6f), true, 0.8f);
		Slab(con, new Vector3(0, PitFloor - 0.2f, 0), new Vector3(Half * 2f, 0.4f, Half * 2f), true, 0.6f);
		// the ceiling, with the round shaft they came down
		float hx = 1.5f;
		Vector3 hole = new(LandingLocal.X, Ceil, LandingLocal.Z);
		Slab(con, new Vector3(0, Ceil + 0.2f, (hole.Z + hx + Half) * 0.5f), new Vector3(Half * 2f, 0.4f, Half - hole.Z - hx), false, 0.6f);
		Slab(con, new Vector3(0, Ceil + 0.2f, (hole.Z - hx - Half) * 0.5f), new Vector3(Half * 2f, 0.4f, hole.Z - hx + Half), false, 0.6f);
		Slab(con, new Vector3((hole.X + hx + Half) * 0.5f, Ceil + 0.2f, hole.Z), new Vector3(Half - hole.X - hx, 0.4f, 2f * hx), false, 0.6f);
		Slab(con, new Vector3((hole.X - hx - Half) * 0.5f, Ceil + 0.2f, hole.Z), new Vector3(hole.X - hx + Half, 0.4f, 2f * hx), false, 0.6f);
		const float r = 1.2f;
		for (int i = 0; i < 24; i++)
		{
			float a0 = i / 24f * Mathf.Tau, a1 = (i + 1) / 24f * Mathf.Tau;
			Vector3 c0 = hole + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), c1 = hole + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
			Vector3 s0 = hole + SquareEdge(a0, hx), s1 = hole + SquareEdge(a1, hx);
			Quad(con, c0, c1, s1, s0, Vector3.Down);
			// the shaft going up, inside faces
			Quad(con, c0, c1, c1 + Vector3.Up * 7f, c0 + Vector3.Up * 7f, -new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0, Mathf.Sin((a0 + a1) * 0.5f)));
		}
		con.CommitTo(this, "Shell", true);
		// the ladder up the shaft, and a yellow beam across the ceiling beside it
		var steel = new MeshKit();
		steel.Mat(BossTextures.Painted(new Color(0.85f, 0.66f, 0.08f), 0.5f, 0.3f));
		steel.Color = Colors.White;
		BuildKit.Box(steel, new Vector3(hole.X + 2.2f, Ceil - 0.35f, 0), new Vector3(0.5f, 0.7f, Half * 2f));
		steel.CommitTo(this, "Beam", true);
		var lad = new MeshKit();
		lad.Mat(BossTextures.GalvanizedMat);
		lad.Color = Colors.White;
		foreach (float x in new[] { -0.25f, 0.25f })
			lad.Cylinder(hole + new Vector3(x, -1.5f, r - 0.15f), hole + new Vector3(x, 7f, r - 0.15f), 0.025f, 0.025f, 6, true);
		for (float y = -1.3f; y < 7f; y += 0.3f)
			lad.Cylinder(hole + new Vector3(-0.25f, y, r - 0.15f), hole + new Vector3(0.25f, y, r - 0.15f), 0.015f, 0.015f, 5, false);
		lad.CommitTo(this, "Ladder", false);
	}

	private static Vector3 SquareEdge(float a, float hw)
	{
		Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a));
		float s = hw / Mathf.Max(Mathf.Abs(d.X), Mathf.Abs(d.Y));
		return new Vector3(d.X * s, 0, d.Y * s);
	}

	private static void Quad(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
	{
		if ((b - a).Cross(d - a).Dot(n) > 0) k.Quad(a, b, c, d, n);
		else k.Quad(b, a, d, c, n);
	}

	// ------------------------------------------------------------------ the catwalk

	private void BuildCatwalk()
	{
		var deck = new MeshKit();
		deck.Mat(BossTextures.GratingMat);
		deck.Color = Colors.White;
		var rail = new MeshKit();
		rail.Mat(BossTextures.GalvanizedMat);
		rail.Color = Colors.White;
		float w = Half - CatIn, c = (Half + CatIn) * 0.5f;
		// the deck: four runs round the square
		foreach (var (ctr, size) in new[]
		{
			(new Vector3(0, -0.04f, -c), new Vector3(Half * 2f, 0.08f, w)), (new Vector3(0, -0.04f, c), new Vector3(Half * 2f, 0.08f, w)),
			(new Vector3(-c, -0.04f, 0), new Vector3(w, 0.08f, CatIn * 2f)), (new Vector3(c, -0.04f, 0), new Vector3(w, 0.08f, CatIn * 2f)),
		})
		{
			BuildKit.Box(deck, ctr, size);
			_body.AddChild(new CollisionShape3D { Position = ctr, Shape = new BoxShape3D { Size = size } });
		}
		// the railing along the inner edge: posts, a top rail, a mid rail, a toe board; brackets under it to the wall
		Vector3[] corners = { new(-CatIn, 0, -CatIn), new(CatIn, 0, -CatIn), new(CatIn, 0, CatIn), new(-CatIn, 0, CatIn) };
		for (int i = 0; i < 4; i++)
		{
			Vector3 a = corners[i], b = corners[(i + 1) % 4];
			rail.Cylinder(a + Vector3.Up * 1.1f, b + Vector3.Up * 1.1f, 0.03f, 0.03f, 8, false);
			rail.Cylinder(a + Vector3.Up * 0.55f, b + Vector3.Up * 0.55f, 0.022f, 0.022f, 6, false);
			Vector3 mid = (a + b) * 0.5f, along = (b - a).Normalized(), outw = mid.Normalized() * 0f + new Vector3(mid.X, 0, mid.Z).Normalized();
			BuildKit.Box(rail, mid + Vector3.Up * 0.06f, new Vector3(Mathf.Abs(along.X) * CatIn * 2f + 0.02f, 0.12f, Mathf.Abs(along.Z) * CatIn * 2f + 0.02f));
			for (float s = 0; s <= 1.001f; s += 1f / 16f)
			{
				Vector3 p = a.Lerp(b, s);
				rail.Cylinder(p, p + Vector3.Up * 1.12f, 0.028f, 0.028f, 6, true);
				// a bracket down under the deck and back to the wall
				Vector3 wallP = p + outw * w;
				rail.Cylinder(p + Vector3.Down * 0.08f, wallP + Vector3.Down * 1.4f, 0.04f, 0.04f, 5, false);
			}
			// a tall, thick guard out over the pit: nobody goes over the rail
			Vector3 gc = mid + Vector3.Up * 0.9f - outw * 0.15f;
			_body.AddChild(new CollisionShape3D
			{
				Position = gc,
				Shape = new BoxShape3D { Size = new Vector3(Mathf.Abs(along.X) * CatIn * 2f + 0.3f, 1.8f, Mathf.Abs(along.Z) * CatIn * 2f + 0.3f) + new Vector3(Mathf.Abs(along.Z), 0, Mathf.Abs(along.X)) * 0.3f },
			});
		}
		deck.CommitTo(this, "Deck", true);
		rail.CommitTo(this, "Railing", true);
	}

	// ------------------------------------------------------------------ the pipe walls

	/// <summary>A wall's frame: its middle, the direction along it, and its inward normal.</summary>
	private static (Vector3 origin, Vector3 along, Vector3 inward) Wall(int w) => w switch
	{
		0 => (new Vector3(0, 0, -Half), Vector3.Right, Vector3.Back),
		1 => (new Vector3(Half, 0, 0), Vector3.Back, Vector3.Left),
		2 => (new Vector3(0, 0, Half), Vector3.Left, Vector3.Forward),
		_ => (new Vector3(-Half, 0, 0), Vector3.Forward, Vector3.Right),
	};

	private void BuildPipes()
	{
		Material[] mats =
		{
			BossTextures.WrapMat(new Color(0.85f, 0.72f, 0.6f)), BossTextures.WrapMat(new Color(0.8f, 0.78f, 0.72f)), BossTextures.FoilMat,
			BossTextures.Painted(new Color(0.08f, 0.08f, 0.09f), 0.4f, 0.6f), BossTextures.Painted(new Color(0.55f, 0.08f, 0.06f), 0.45f, 0.4f),
			BossTextures.Painted(new Color(0.5f, 0.3f, 0.16f), 0.3f, 0.85f), BossTextures.Painted(new Color(0.5f, 0.52f, 0.55f), 0.35f, 0.8f),
		};
		var kits = new MeshKit[mats.Length];
		for (int i = 0; i < mats.Length; i++) { kits[i] = new MeshKit(); kits[i].Mat(mats[i]); kits[i].Color = Colors.White; }
		var bracket = new MeshKit();
		bracket.Mat(BossTextures.GalvanizedMat);
		bracket.Color = Colors.White;
		var cable = new MeshKit();
		cable.Mat(BossTextures.Painted(new Color(0.12f, 0.12f, 0.12f), 0.6f, 0.2f));
		cable.Color = Colors.White;
		float[] slots = { -12f, -4f, 4f, 12f };
		float len = Half * 2f - 0.8f;
		for (int w = 0; w < 4; w++)
		{
			var (o, along, inw) = Wall(w);
			// the manifold every valve comes off (on the north wall, broken round the door)
			if (w == 2)
			{
				Run(kits[3], o + inw * 0.46f + Vector3.Up * 0.21f, along, -len * 0.5f, -DoorClear, 0.16f, bracket, inw, 1);
				Run(kits[3], o + inw * 0.46f + Vector3.Up * 0.21f, along, DoorClear, len * 0.5f, 0.16f, bracket, inw, 1);
			}
			else Pipe(kits[3], o + inw * 0.3f + Vector3.Up * 0.05f, along, len, 0.16f, bracket, inw);
			// three depths of runs, floor to ceiling: fine pipes close to the wall, fat ones in front
			(float depth, float rMin, float rMax, float gapMin, float gapMax)[] layers =
				{ (0.12f, 0.04f, 0.13f, 0.03f, 0.12f), (0.42f, 0.07f, 0.22f, 0.06f, 0.35f), (0.74f, 0.12f, 0.3f, 0.3f, 1.3f) };
			for (int li = 0; li < layers.Length; li++)
			{
				var lay = layers[li];
				float y = li == 2 ? 2.8f : 0.45f;
				while (y < Ceil - 0.8f)
				{
					float r = _rng.RandfRange(lay.rMin, lay.rMax);
					int m = _rng.RandiRange(0, mats.Length - 1);
					float yc = y + r;
					// the front ones keep clear of the valves (so the wheels and the spotlight stay on show)
					float s0 = -len * 0.5f;
					var cuts = new List<(float a, float b)>();
					if (li > 0 && yc < 2.7f) foreach (float sl in slots) cuts.Add((sl - 1.0f, sl + 1.0f));
					if (w == 2 && yc < 3.0f) { cuts.Add((-DoorClear, DoorClear)); cuts.Sort((x, z) => x.a.CompareTo(z.a)); }
					cuts.Add((len * 0.5f, len * 0.5f));
					foreach (var (ca, cb) in cuts)
					{
						if (ca - s0 > 0.4f) Run(kits[m], o + inw * (lay.depth + r) + Vector3.Up * yc, along, s0, ca, r, bracket, inw, li);
						s0 = cb;
					}
					// now and then one turns and drops or climbs to another level (elbows, a riser)
					if (li > 0 && _rng.Randf() < 0.35f)
					{
						float s = _rng.RandfRange(-len * 0.45f, len * 0.45f);
						bool nearValve = false;
						foreach (float sl in slots) if (Mathf.Abs(s - sl) < 1.4f) nearValve = true;
						if (w == 2 && Mathf.Abs(s) < DoorClear + 0.5f) nearValve = true;
						if (!nearValve)
						{
							float y2 = Mathf.Clamp(yc + _rng.RandfRange(-3f, 3f), 2.8f, Ceil - 1f);
							Vector3 top = o + along * s + inw * (lay.depth + r + r * 2.2f) + Vector3.Up * yc;
							Vector3 bot = top;
							bot.Y = o.Y + y2;
							kits[m].Cylinder(top, bot, r * 0.8f, r * 0.8f, 12, false);
							Elbow(kits[m], top, r * 0.8f);
							Elbow(kits[m], bot, r * 0.8f);
						}
					}
					y += r * 2f + _rng.RandfRange(lay.gapMin, lay.gapMax);
				}
			}
			// cable bundles sagging between brackets, like the service tunnels in the references
			foreach (float by in new[] { 3.6f, 7.9f, 11.6f })
			{
				int n = _rng.RandiRange(7, 11);
				for (int c = 0; c < n; c++)
				{
					float cy = by + (c % 4) * 0.045f, cd = 0.3f + (c / 4) * 0.05f;
					for (float s = -len * 0.5f; s < len * 0.5f - 0.1f; s += 2.4f)
					{
						float e = Mathf.Min(s + 2.4f, len * 0.5f);
						Vector3 prev = o + along * s + inw * cd + Vector3.Up * cy;
						for (int q = 1; q <= 6; q++)
						{
							float u = q / 6f;
							Vector3 p = o + along * Mathf.Lerp(s, e, u) + inw * cd + Vector3.Up * (cy - 0.18f * 4f * u * (1f - u) * (1f + 0.15f * (c % 3)));
							cable.Cylinder(prev, p, 0.016f, 0.016f, 4, false);
							prev = p;
						}
					}
				}
				for (float s = -len * 0.5f; s <= len * 0.5f; s += 2.4f)
					bracket.Cylinder(o + along * s + inw * 0.05f + Vector3.Up * (by - 0.05f), o + along * s + inw * 0.55f + Vector3.Up * (by - 0.05f), 0.012f, 0.012f, 4, false);
			}
			// gauges and small handwheels scattered over the front pipes
			for (int gi = 0; gi < 9; gi++)
			{
				float s = _rng.RandfRange(-len * 0.45f, len * 0.45f);
				bool nearValve = false;
				foreach (float sl in slots) if (Mathf.Abs(s - sl) < 1.3f) nearValve = true;
				if (nearValve) continue;
				Vector3 at = o + along * s + inw * 1.0f + Vector3.Up * _rng.RandfRange(2.2f, 9f);
				if (gi % 3 == 0) SmallWheel(at, inw);
				else Gauge(at, inw);
			}
			// the pipes are solid: keep the player off them (the north wall leaves the doorway open)
			if (w == 2)
			{
				float side = len * 0.5f - DoorClear;
				foreach (float sg in new[] { -1f, 1f })
					_body.AddChild(new CollisionShape3D { Position = o + along * sg * (DoorClear + side * 0.5f) + inw * 0.4f + Vector3.Up * (Ceil * 0.5f + 0.1f), Shape = new BoxShape3D { Size = along.Abs() * side + inw.Abs() * 0.8f + Vector3.Up * Ceil } });
				_body.AddChild(new CollisionShape3D { Position = o + inw * 0.4f + Vector3.Up * (3.0f + (Ceil - 3.0f) * 0.5f), Shape = new BoxShape3D { Size = along.Abs() * DoorClear * 2f + inw.Abs() * 0.8f + Vector3.Up * (Ceil - 3.0f) } });
			}
			else _body.AddChild(new CollisionShape3D { Position = o + inw * 0.4f + Vector3.Up * (Ceil * 0.5f + 0.1f), Shape = new BoxShape3D { Size = along.Abs() * len + inw.Abs() * 0.8f + Vector3.Up * Ceil } });
		}
		for (int i = 0; i < kits.Length; i++) kits[i].CommitTo(this, $"Pipes{i}", true);
		bracket.CommitTo(this, "Brackets", false);
		cable.CommitTo(this, "Cables", false);
		// red caged emergency lamps on the walls, dim
		for (int w = 0; w < 4; w++)
		{
			var (o, along, inw) = Wall(w);
			foreach (float s in new[] { -8f, 8f })
			{
				Vector3 at = o + along * s + inw * 1.1f + Vector3.Up * 2.4f;
				AddChild(new OmniLight3D { Position = at + inw * 0.3f, LightColor = new Color(1f, 0.2f, 0.12f), LightEnergy = 0.45f, OmniRange = 6f, ShadowEnabled = false });
				AddChild(new MeshInstance3D
				{
					Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f }, Position = at,
					MaterialOverride = StationParts.StationTextures.Glow("bt_redlamp", new Color(1f, 0.2f, 0.12f), 2f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				});
			}
		}
	}

	/// <summary>A run of pipe along a wall from s0 to s1, flanged every few metres, bracketed back to the wall.</summary>
	private void Run(MeshKit k, Vector3 centre, Vector3 along, float s0, float s1, float r, MeshKit bracket, Vector3 inward, int layer)
	{
		Vector3 a = centre + along * s0, b = centre + along * s1;
		k.Cylinder(a, b, r, r, r > 0.15f ? 14 : 10, true);
		for (float s = s0 + 1.5f; s < s1 - 0.5f; s += _rng.RandfRange(3f, 5f))
		{
			Vector3 p = centre + along * s;
			if (r > 0.08f) k.Cylinder(p - along * 0.04f, p + along * 0.04f, r * 1.22f, r * 1.22f, 12, true);
			bracket.Cylinder(p - inward * (r * 0.9f), p - inward * (layer == 0 ? 0.2f : 0.9f), 0.012f, 0.012f, 4, false);
		}
	}

	/// <summary>A pipe's bend, as a ball of the pipe's own radius (reads as an elbow at this resolution).</summary>
	private static void Elbow(MeshKit k, Vector3 at, float r) => k.Blob(at, Vector3.One * r * 1.05f, (int)(at.X * 13 + at.Y * 7), 0.0f, false);

	private void Gauge(Vector3 at, Vector3 inward)
	{
		var g = new Node3D { Name = "Gauge" };
		AddChild(g);
		g.Position = at;
		g.Basis = Basis.LookingAt(inward, Vector3.Up);
		g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.06f, RadialSegments = 24 }, Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0), MaterialOverride = BossTextures.Painted(new Color(0.6f, 0.5f, 0.25f), 0.35f, 0.7f) });
		g.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.21f, 0.21f) }, Position = new Vector3(0, 0, -0.032f), Rotation = new Vector3(0, Mathf.Pi, 0), MaterialOverride = new StandardMaterial3D { AlbedoTexture = BossTextures.GaugeFace, Roughness = 0.2f } });
		g.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.006f, 0.08f, 0.004f) }, Position = new Vector3(0, 0.03f, -0.036f), Rotation = new Vector3(0, 0, _rng.RandfRange(-2f, 2f)), MaterialOverride = BossTextures.Painted(new Color(0.8f, 0.08f, 0.06f), 0.4f, 0.1f) });
	}

	private void SmallWheel(Vector3 at, Vector3 inward)
	{
		var n = new Node3D { Name = "Handwheel" };
		AddChild(n);
		n.Position = at;
		n.Basis = Basis.LookingAt(inward, Vector3.Up);
		var c = _rng.Randf() < 0.5f ? new Color(0.6f, 0.07f, 0.05f) : new Color(0.12f, 0.12f, 0.13f);
		var mat = BossTextures.Painted(c, 0.4f, 0.3f);
		n.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.13f, OuterRadius = 0.16f, Rings = 40, RingSegments = 10 }, Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0), MaterialOverride = mat });
		n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.3f, RadialSegments = 12 }, Rotation = new Vector3(0, 0, Mathf.Pi * 0.5f), MaterialOverride = mat });
		n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.3f, RadialSegments = 12 }, MaterialOverride = mat });
	}

	/// <summary>A horizontal pipe along a wall, with flanges every few metres and brackets back to the wall.</summary>
	private void Pipe(MeshKit k, Vector3 centre, Vector3 along, float len, float r, MeshKit bracket, Vector3 inward)
	{
		Vector3 a = centre - along * len * 0.5f, b = centre + along * len * 0.5f;
		k.Cylinder(a, b, r, r, r > 0.2f ? 14 : 10, false);
		for (float s = 2f; s < len - 1f; s += 4.5f)
		{
			Vector3 p = a + along * s;
			k.Cylinder(p - along * 0.04f, p + along * 0.04f, r * 1.25f, r * 1.25f, 12, true);
			bracket.Cylinder(p - inward * (r * 0.9f), p - inward * 0.8f, 0.015f, 0.015f, 4, false);
		}
	}

	// ------------------------------------------------------------------ the valves

	/// <summary>Valve i's place: four per wall, in order round the room from the south-west.</summary>
	public static (Vector3 at, Vector3 inward) ValveLocal(int i)
	{
		var (o, along, inw) = Wall(i / 4);
		float[] slots = { -12f, -4f, 4f, 12f };
		return (o + along * slots[i % 4] + inw * 0.3f + Vector3.Up * 1.25f, inw);
	}

	private void BuildValves()
	{
		for (int i = 0; i < ValveCount; i++)
		{
			var (at, inw) = ValveLocal(i);
			var v = new Valve { Name = $"Valve{i + 1}", Number = i + 1 };
			AddChild(v);
			v.Position = at;
			v.Basis = Basis.LookingAt(inw, Vector3.Up);   // its -Z toward the catwalk
			Valves.Add(v);
		}
	}

	// ------------------------------------------------------------------ the rig overhead

	private void BuildRig()
	{
		// trusses across the ceiling (box-section lattice), stage lamps hanging from them
		var t = new MeshKit();
		t.Mat(BossTextures.GalvanizedMat);
		t.Color = Colors.White;
		foreach (float z in new[] { -10f, 0f, 10f })
			Truss(t, new Vector3(-Half + 0.5f, Ceil - 1.6f, z), new Vector3(Half - 0.5f, Ceil - 1.6f, z));
		foreach (float x in new[] { -Half + 2.5f, Half - 2.5f })
			Truss(t, new Vector3(x, Ceil - 2.3f, -Half + 0.5f), new Vector3(x, Ceil - 2.3f, Half - 0.5f));
		t.CommitTo(this, "Trusses", true);
		// stage lamps pointing down into the pit, dim and cold
		var lamp = new MeshKit();
		lamp.Mat(BossTextures.Painted(new Color(0.07f, 0.07f, 0.08f), 0.5f, 0.5f));
		lamp.Color = Colors.White;
		foreach (float z in new[] { -10f, 10f })
			foreach (float x in new[] { -9f, -3f, 3f, 9f })
			{
				Vector3 at = new(x, Ceil - 2.1f, z);
				lamp.Cylinder(at, at + Vector3.Down * 0.35f, 0.14f, 0.18f, 10, true);
				var l = new SpotLight3D
				{
					Position = at + Vector3.Down * 0.4f, Rotation = new Vector3(-Mathf.Pi * 0.5f + (z > 0 ? -0.2f : 0.2f), 0, 0),
					LightColor = new Color(0.8f, 0.85f, 0.9f), LightEnergy = 3f, SpotRange = 70f, SpotAngle = 14f, SpotAttenuation = 0.6f, ShadowEnabled = false,
				};
				AddChild(l);
				AddChild(new MeshInstance3D
				{
					Mesh = new SphereMesh { Radius = 0.13f, Height = 0.06f }, Position = at + Vector3.Down * 0.36f,
					MaterialOverride = StationParts.StationTextures.Glow("bt_stage", new Color(0.9f, 0.93f, 1f), 3f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				});
			}
		lamp.CommitTo(this, "StageLamps", false);
		// dim fill so the size of the place reads
		foreach (var at in new[] { new Vector3(0, 6f, 0), new Vector3(-12f, 4f, -12f), new Vector3(12f, 4f, 12f) })
		{
			var l = new OmniLight3D { Position = at, LightColor = new Color(0.55f, 0.6f, 0.65f), LightEnergy = 0.5f, OmniRange = 30f, OmniAttenuation = 0.8f, ShadowEnabled = false };
			AddChild(l);
			_stage.Add(l);
		}
		// the four teal spotlights, one on each side, each across the room from the valves it picks out
		var beamShader = GD.Load<Shader>("res://assets/shaders/light_beam.gdshader");
		for (int w = 0; w < 4; w++)
		{
			var (o, along, inw) = Wall(w);
			Vector3 at = o + inw * 2.6f + Vector3.Up * (Ceil - 3f);
			var housing = new MeshKit();
			housing.Mat(BossTextures.Painted(new Color(0.07f, 0.07f, 0.08f), 0.5f, 0.5f));
			housing.Color = Colors.White;
			housing.Cylinder(at + inw * 0.2f, at - inw * 0.3f, 0.32f, 0.26f, 14, true);
			housing.CommitTo(this, $"Fixture{w}", false);
			var light = new SpotLight3D
			{
				Name = $"Teal{w}", Position = at + inw * 0.35f, LightColor = new Color(0.3f, 1f, 0.88f), LightEnergy = 0f,
				SpotRange = 55f, SpotAngle = 5.5f, SpotAngleAttenuation = 0.4f, SpotAttenuation = 0.2f, ShadowEnabled = true,
			};
			AddChild(light);
			var mat = new ShaderMaterial { Shader = beamShader };
			mat.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
			mat.SetShaderParameter("strength", 0f);
			var beam = new MeshInstance3D { Name = $"Beam{w}", Mesh = new CylinderMesh { TopRadius = 0.28f, BottomRadius = 1f, Height = 1f, RadialSegments = 18, CapTop = false, CapBottom = false }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild(beam);
			_fixtures.Add((light, beam, mat));
		}
	}

	private static void Truss(MeshKit k, Vector3 a, Vector3 b)
	{
		Vector3 d = (b - a).Normalized();
		Vector3 side = d.Cross(Vector3.Up).Normalized() * 0.3f, up = Vector3.Up * 0.3f;
		Vector3[] chords = { side + up, -side + up, side - up, -side - up };
		foreach (var c in chords) k.Cylinder(a + c, b + c, 0.03f, 0.03f, 6, false);
		float len = (b - a).Length();
		for (float s = 0; s < len; s += 0.6f)
		{
			Vector3 p = a + d * s, q = a + d * Mathf.Min(len, s + 0.6f);
			k.Cylinder(p + side + up, q + side - up, 0.012f, 0.012f, 4, false);
			k.Cylinder(p - side - up, q - side + up, 0.012f, 0.012f, 4, false);
			k.Cylinder(p + side + up, q - side + up, 0.012f, 0.012f, 4, false);
		}
	}

	/// <summary>Points fixture w's light and beam at a world point.</summary>
	private void Aim(int w, Vector3 targetLocal, float strength)
	{
		var (light, beam, mat) = _fixtures[w];
		Vector3 from = light.Position;
		Vector3 d = targetLocal - from;
		float len = d.Length();
		light.LookAt(ToGlobal(targetLocal), Vector3.Up);
		light.LightEnergy = 14f * strength;
		// the beam: a cone from the lamp (its top) to a metre or so wide at the valve
		Vector3 y = -d / len;   // the cylinder's top (+Y) at the lamp
		Vector3 x = y.Cross(Vector3.Forward);
		if (x.LengthSquared() < 0.01f) x = y.Cross(Vector3.Right);
		x = x.Normalized();
		Vector3 z = x.Cross(y);
		beam.Basis = new Basis(x * 1f, y * len, z * 1f);
		beam.Position = from + d * 0.5f;
		mat.SetShaderParameter("strength", 0.33f * strength);
	}

	// ------------------------------------------------------------------ the blood

	private void BuildBlood()
	{
		_bloodMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/basement_water.gdshader") };
		_bloodMat.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		_bloodMat.SetShaderParameter("drain", Vector2.Zero);
		_bloodMat.SetShaderParameter("water_color", new Color(0.16f, 0.005f, 0.008f));
		_bloodMat.SetShaderParameter("scum_color", new Color(0.3f, 0.03f, 0.03f));
		_blood = new MeshInstance3D
		{
			Name = "Blood", Mesh = new PlaneMesh { Size = new Vector2(Half * 2f, Half * 2f), SubdivideWidth = 24, SubdivideDepth = 24 },
			Position = new Vector3(0, BloodY, 0), MaterialOverride = _bloodMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_blood);
		// a dull red glow off the blood's surface, lighting the limbs from below (it goes down with the blood)
		foreach (var at in new[] { new Vector3(-9f, 0, -9f), new Vector3(9f, 0, -9f), new Vector3(9f, 0, 9f), new Vector3(-9f, 0, 9f), new Vector3(0, 0, 0) })
		{
			var l = new OmniLight3D { Position = at + Vector3.Up * (BloodY + 1.5f), LightColor = new Color(0.9f, 0.18f, 0.12f), LightEnergy = 1.3f, OmniRange = 22f, OmniAttenuation = 0.9f, ShadowEnabled = false };
			AddChild(l);
			_bloodGlow.Add(l);
		}
	}

	private readonly List<OmniLight3D> _bloodGlow = new();

	private void SetBlood(float y)
	{
		BloodY = y;
		_blood.Position = new Vector3(0, y, 0);
		_blood.Visible = y > PitFloor + 0.05f;
		foreach (var l in _bloodGlow) l.Position = l.Position with { Y = y + 1.5f };
		if (Beast != null) Beast.Level = y - PitFloor;
	}

	// ------------------------------------------------------------------ the way out

	private void BuildDoor()
	{
		// a steel door in the north wall, shut; behind it, a small clean room with the light on
		_door = new Node3D { Name = "Door", Position = new Vector3(0, 0, Half + 0.05f) };
		AddChild(_door);
		var d = new MeshKit();
		d.Mat(StairwellTextures.SteelMat);
		d.Color = new Color(0.55f, 0.58f, 0.55f);
		BuildKit.Box(d, new Vector3(0, 1.2f, 0), new Vector3(1.3f, 2.4f, 0.08f));
		d.CommitTo(_door, "Slab", true);
		var db = new StaticBody3D { Name = "DoorBody", CollisionLayer = 1, CollisionMask = 0 };
		db.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.2f, 0), Shape = new BoxShape3D { Size = new Vector3(1.3f, 2.4f, 0.1f) } });
		_door.AddChild(db);

		// behind it: Act 19's library (and past that, Act 20's round room)
		float z0 = Half + 0.6f;
		Library = new Library { Name = "Library", Position = new Vector3(0, 0, z0), Visible = false };   // unseen (and not drawn) until the door opens
		AddChild(Library);
		_tidyLight = new OmniLight3D { Name = "TidyLight", Position = new Vector3(0, 2.6f, z0 + 3f), LightColor = new Color(1f, 0.93f, 0.8f), LightEnergy = 0f, OmniRange = 9f, ShadowEnabled = false };
		AddChild(_tidyLight);
		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(4f, 2f, 2f) }, new Vector3(0, 1f, z0 + 2.5f), OnTidyRoom, "TidyRoomTrigger");
	}
}
