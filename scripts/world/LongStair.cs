using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.ChurchParts;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Act 21's first half (the owner's "Real Act 21"): from the little room at the top of the round room's
/// shaft, one long straight staircase up through the rock to the surface: about three minutes at a
/// sprint, longer at a walk (52 flights of 32 steps, a landing between each, about 300 m of climb).
/// It starts as bare poured concrete with caged bulbs and a steel rail; somewhere past a quarter of the
/// way up timber starts to appear (a shoring frame, a patch of planking on the wall, a board laid over a
/// broken tread) and, very slowly, over the middle of the climb, the wood takes over, until the last
/// stretch is an old wooden stair between plank walls, lit by oil lanterns on the frames. At the top, a
/// low landing, a ladder of rungs on its end wall, and a hatch in the ceiling: pushed open, it lets the
/// player up into the church's crypt, and it falls shut behind them (<see cref="Checkpoint.Act21ChurchReached"/>).
///
/// Local space: this node sits on the top room's floor (y 0), its axes the round room's; the stair runs
/// up along +Z from the top room's arch.
/// </summary>
public partial class LongStair : Node3D
{
	public const int Flights = 52, StepsPerFlight = 32;
	public const float Rise = 0.18f, Run = 0.28f, Landing = 2.2f, TopLanding = 3.2f;
	public const float HalfW = 1.0f, Head = 2.6f, WallT = 0.3f;
	public const float StartZ = RoundRoom.TopR + 0.8f;
	public const float FlightLen = StepsPerFlight * Run, FlightRise = StepsPerFlight * Rise;
	public const float Pitch = FlightLen + Landing;
	/// <summary>The top landing's floor (local).</summary>
	public const float TopY = Flights * FlightRise;
	public const float TopZ0 = StartZ + Flights * Pitch - Landing;   // where the top landing starts
	public static readonly Vector3 HatchLocal = new(0, TopY + Head, TopZ0 + TopLanding * 0.5f);
	/// <summary>The crypt's floor over the hatch (local): the church is built round this.</summary>
	public const float CryptFloorAbove = Head + 0.3f;
	/// <summary>Where the church sits (this node's space): its crypt floor's hatch over the top landing's.</summary>
	public static readonly Vector3 ChurchOrigin = HatchLocal + new Vector3(0, 0.3f, 0) - Church.HatchLocal;

	public PickupInteractable HatchUse { get; private set; }
	public bool HatchOpened { get; private set; }
	public Vector3 BottomWorld => ToGlobal(new Vector3(0, 0.05f, StartZ - 0.3f));
	public Vector3 TopLandingWorld => ToGlobal(new Vector3(0, TopY + 0.05f, TopZ0 + TopLanding * 0.5f - 0.6f));
	/// <summary>Where on the stair (0 bottom .. 1 top) a world point is.</summary>
	public float Progress(Vector3 world) => Mathf.Clamp((ToLocal(world).Z - StartZ) / (TopZ0 - StartZ), 0f, 1f);
	/// <summary>A point on the stair's walking line at <paramref name="progress"/> (world, on the treads).</summary>
	public Vector3 PointAt(float progress)
	{
		float z = Mathf.Lerp(StartZ, TopZ0, Mathf.Clamp(progress, 0f, 1f));
		return ToGlobal(new Vector3(0, FloorAt(z) + 0.1f, z));
	}

	public Church Church { get; private set; }
	public bool Inside(Vector3 world)
	{
		var l = ToLocal(world);
		return Mathf.Abs(l.X) < 3f && l.Z > StartZ - 1.2f && l.Z < TopZ0 + TopLanding + 1f && l.Y > -3f && l.Y < TopY + Head + 0.2f;
	}

	private StaticBody3D _concrete, _wood;
	private Node3D _hatch;
	private StaticBody3D _lidBody;
	private readonly RandomNumberGenerator _rng = new() { Seed = 2121 };

	/// <summary>How far the wood has taken over at a flight (0 concrete .. 1 all timber): a long, slow change over the middle.</summary>
	public static float WoodAt(int flight) => Mathf.SmoothStep(0.26f, 0.74f, flight / (float)(Flights - 1));

	/// <summary>The walking line's height at local z (the step nosings, flat on the landings).</summary>
	public static float FloorAt(float z)
	{
		if (z < StartZ) return 0f;
		float rel = z - StartZ;
		int f = Mathf.Min((int)(rel / Pitch), Flights - 1);
		float inF = rel - f * Pitch;
		if (z >= TopZ0) return TopY;
		return f * FlightRise + Mathf.Min(FlightRise, (inF / FlightLen) * FlightRise + Rise * (inF < FlightLen ? 1f : 0f));
	}

	/// <summary>The ceiling's height at local z: a steady headroom over the nosing line.</summary>
	public static float CeilingAt(float z)
	{
		if (z < StartZ) return Head;
		if (z >= TopZ0) return TopY + Head;
		float rel = z - StartZ;
		int f = Mathf.Min((int)(rel / Pitch), Flights - 1);
		float inF = rel - f * Pitch;
		return f * FlightRise + Head + Mathf.Min(FlightRise, inF / FlightLen * FlightRise);
	}

	private static float Hash(int a, int b, int s)
	{
		uint h = (uint)(a * 374761393 + b * 668265263 + s * 1442695041);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}

	public override void _Ready() => Build();

	private void Build()
	{
		_concrete = new StaticBody3D { Name = "Concrete", CollisionLayer = 1, CollisionMask = 0 };
		_concrete.SetMeta("surface", "stone");
		AddChild(_concrete);
		_wood = new StaticBody3D { Name = "Wood", CollisionLayer = 1, CollisionMask = 0 };
		_wood.SetMeta("surface", "wood");
		AddChild(_wood);
		BuildEntry();
		const int perChunk = 5;
		for (int c = 0; c * perChunk < Flights; c++) BuildChunk(c * perChunk, Mathf.Min(Flights, (c + 1) * perChunk));
		BuildTop();
		// the church, at the top: its crypt's hatch right over the top landing's
		Church = new Church { Name = "Church", Position = ChurchOrigin, Stair = this };
		AddChild(Church);
		// the sound of it: a long hollow throat of a stairwell, a cold draught from far above
		AudioDirector.Zone(this, new Vector3(0, TopY * 0.5f, (StartZ + TopZ0) * 0.5f), new Vector3(2.4f, TopY + 6f, TopZ0 - StartZ + 6f), AudioDirector.Space.Tunnel, "StairVerb");
		var along = new List<Vector3>();
		for (int f = 6; f < Flights; f += 9) along.Add(new Vector3(0, f * FlightRise + 3f, StartZ + f * Pitch + 4f));
		AudioDirector.Haunt(this, along.ToArray(), new[] { "haunt_moan", "haunt_boards", "haunt_drip" }, new Vector2(18f, 34f), -12f, 30f,
			() => StoryManager.Instance is { } s && s.Current >= Checkpoint.Act20Finished && s.Current < Checkpoint.Act21ChurchReached);
	}

	private void BuildEntry()
	{
		// out through the top room's arch: jambs through its wall's thickness, a floor to the first step
		var k = new MeshKit();
		k.Mat(StairwellTextures.CleanMat);
		k.Color = new Color(0.8f, 0.8f, 0.8f);
		foreach (float s in new[] { -1f, 1f })
			Solid(k, _concrete, new Vector3(s * (HalfW + 0.45f), Head * 0.5f, (4.5f + StartZ) * 0.5f), new Vector3(0.9f, Head, StartZ - 4.5f));
		Solid(k, _concrete, new Vector3(0, -0.012f - 0.1f, (4.75f + StartZ) * 0.5f), new Vector3(HalfW * 2f, 0.2f, StartZ - 4.75f));
		Solid(k, null, new Vector3(0, Head + 0.2f, (4.5f + StartZ) * 0.5f), new Vector3(HalfW * 2f + 0.2f, 0.4f, StartZ - 4.5f));
		k.CommitTo(this, "Entry", true);
	}

	private static void Solid(MeshKit k, StaticBody3D body, Vector3 c, Vector3 s)
	{
		BuildKit.Box(k, c, s, 1f);
		body?.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
	}

	/// <summary>Flights [f0, f1): their treads and risers, landings, walls, ceiling, frames, rails and lights,
	/// in one mesh node that the renderer can drop when it is far off.</summary>
	private void BuildChunk(int f0, int f1)
	{
		var k = new MeshKit();
		var concrete = StairwellTextures.CleanMat;
		var stained = StairwellTextures.StainedMat;
		var plank = ChurchTextures.OldPlankMat;
		var timber = PropTextures.PostMat;
		var steel = StairwellTextures.SteelMat;
		for (int f = f0; f < f1; f++)
		{
			float wood = WoodAt(f);
			float z0 = StartZ + f * Pitch, y0 = f * FlightRise;
			// ---- the treads and risers (each its own: concrete, or an old board laid over it)
			for (int i = 0; i < StepsPerFlight; i++)
			{
				bool w = Hash(f, i, 11) < wood * 1.1f - 0.05f;
				float za = z0 + i * Run, top = y0 + (i + 1) * Rise;
				k.Mat(w ? plank : (Hash(f, i, 12) < 0.3f ? stained : concrete));
				k.Color = w ? new Color(0.9f, 0.88f, 0.86f) * (0.85f + 0.2f * Hash(f, i, 13)) : Colors.White * (0.8f + 0.12f * Hash(f, i, 14));
				// the tread: a slab under its top, from the riser to the next riser
				BuildKit.Box(k, new Vector3(0, top - 0.06f, za + Run * 0.5f), new Vector3(HalfW * 2f, 0.12f, Run), 1f, BuildKit.Face.NY);
				// the riser under its front edge, down to the tread below
				k.Quad(new Vector3(-HalfW, top - 0.12f - Rise + 0.12f, za), new Vector3(HalfW, top - 0.12f - Rise + 0.12f, za), new Vector3(HalfW, top - 0.12f, za), new Vector3(-HalfW, top - 0.12f, za), Vector3.Back,
					new Vector2(0, Rise), new Vector2(HalfW * 2f, Rise), new Vector2(HalfW * 2f, 0), new Vector2(0, 0));
			}
			// the underside of the flight isn't seen; the landing: a slab at the top of the flight
			float yl = y0 + FlightRise;
			bool last = f == Flights - 1;
			float lz0 = z0 + FlightLen, lz1 = last ? lz0 + TopLanding : lz0 + Landing;
			k.Mat(wood > 0.55f ? plank : concrete);
			k.Color = Colors.White * 0.86f;
			BuildKit.Box(k, new Vector3(0, yl - 0.1f, (lz0 + lz1) * 0.5f), new Vector3(HalfW * 2f, 0.2f, lz1 - lz0), 1f, BuildKit.Face.NY);
			// ---- collision: a ramp under the nosings, the landing, two walls
			var body = wood > 0.5f ? _wood : _concrete;
			// the ramp follows the nosing line, from the landing below (where that line meets it, a run
			// before the first riser) to the last nosing: no lip to trip on at either end
			float len = Mathf.Sqrt(FlightLen * FlightLen + FlightRise * FlightRise), pitch = Mathf.Atan2(FlightRise, FlightLen);
			var rampBasis = new Basis(Vector3.Right, -pitch);
			Vector3 rampTop = new(0, y0 + FlightRise * 0.5f - 0.02f, z0 - Run + FlightLen * 0.5f);
			body.AddChild(new CollisionShape3D { Transform = new Transform3D(rampBasis, rampTop + rampBasis.Y * -0.2f), Shape = new BoxShape3D { Size = new Vector3(HalfW * 2f, 0.4f, len) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, yl - 0.2f, (lz0 + lz1) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(HalfW * 2f, 0.4f, lz1 - lz0 + 0.2f) } });
			foreach (float s in new[] { -1f, 1f })
			{
				// no higher than the ceiling at the flight's top: the last flights run under the church's crypt
				float wb = y0 - 1f, wt = yl + Head - 0.05f;
				body.AddChild(new CollisionShape3D { Position = new Vector3(s * (HalfW + WallT * 0.5f), (wb + wt) * 0.5f, (z0 + lz1) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(WallT, wt - wb, lz1 - z0) } });
			}
			// ---- the walls and the ceiling, a metre at a time
			for (float z = z0; z < lz1 - 0.001f; z += 1f)
			{
				float zb = Mathf.Min(z + 1f, lz1);
				int strip = Mathf.RoundToInt(z * 3f);
				float fa = FloorAt(z) - 0.3f, fb = FloorAt(zb) - 0.3f;
				float ca = CeilingAt(z), cb = CeilingAt(zb - 0.0001f);
				foreach (float s in new[] { -1f, 1f })
				{
					bool w = Hash(strip, s > 0 ? 1 : 2, 21) < wood * 1.15f - 0.08f;
					k.Mat(w ? plank : (Hash(strip, s > 0 ? 3 : 4, 22) < 0.35f + 0.3f * wood ? stained : concrete));
					k.Color = Colors.White * (0.75f + 0.15f * Hash(strip, s > 0 ? 5 : 6, 23));
					float x = s * HalfW;
					Vector3 n = new(-s, 0, 0);
					// quad from low to high along the wall (its inside face), top edge following the ceiling
					Vector3 a = new(x, fa, z), b = new(x, fb, zb), c = new(x, cb, zb), d = new(x, ca, z);
					if (s > 0) k.Quad(b, a, d, c, n, new Vector2(zb, -fb), new Vector2(z, -fa), new Vector2(z, -ca), new Vector2(zb, -cb));
					else k.Quad(a, b, c, d, n, new Vector2(z, -fa), new Vector2(zb, -fb), new Vector2(zb, -cb), new Vector2(z, -ca));
				}
				bool cw = Hash(strip, 7, 24) < wood * 1.1f - 0.05f;
				k.Mat(cw ? plank : concrete);
				k.Color = Colors.White * (0.6f + 0.15f * Hash(strip, 8, 25));
				if (last && z >= TopZ0 - 0.001f) CeilingWithHatch(k, z, zb, ca);
				else
					k.Quad(new Vector3(HalfW, ca, z), new Vector3(-HalfW, ca, z), new Vector3(-HalfW, cb, zb), new Vector3(HalfW, cb, zb), Vector3.Down,
						new Vector2(HalfW * 2f, z), new Vector2(0, z), new Vector2(0, zb), new Vector2(HalfW * 2f, zb));
			}
			// ---- timber: shoring frames (two posts and a cap), more of them as the wood takes over
			for (float z = z0 + 0.7f; z < lz1 - 0.3f; z += 1.6f)
			{
				int set = Mathf.RoundToInt(z * 5f);
				if (Hash(set, 1, 31) > wood * 1.6f - 0.1f) continue;
				float fl = FloorAt(z), ce = CeilingAt(z);
				k.Mat(timber);
				k.Color = new Color(0.62f, 0.56f, 0.5f) * (0.8f + 0.2f * Hash(set, 2, 32));
				foreach (float s in new[] { -1f, 1f })
					BuildKit.Box(k, new Vector3(s * (HalfW - 0.07f), (fl - 0.1f + ce) * 0.5f, z), new Vector3(0.14f, ce - fl + 0.1f, 0.16f), 1f);
				BuildKit.Box(k, new Vector3(0, ce - 0.08f, z), new Vector3(HalfW * 2f, 0.16f, 0.18f), 1f);
			}
			// ---- a rail on the left wall: steel pipe on brackets low down, a wooden rail up top
			{
				float h = 0.9f;
				bool wr = wood > 0.5f;
				k.Mat(wr ? timber : steel);
				k.Color = wr ? new Color(0.55f, 0.45f, 0.36f) : new Color(0.42f, 0.42f, 0.4f);
				Vector3 ra = new(-HalfW + 0.1f, y0 + Rise + h, z0), rb = new(-HalfW + 0.1f, yl + h, z0 + FlightLen);
				if (wr) k.Beam(ra, rb, 0.07f, 0.06f); else k.Cylinder(ra, rb, 0.022f, 0.022f, 6, false);
				for (float t = 0.1f; t < 1f; t += 0.3f)
				{
					Vector3 p = ra.Lerp(rb, t);
					k.Beam(p, p + new Vector3(-0.1f, 0, 0), 0.03f, 0.03f);
				}
			}
			// ---- light on every second landing: a caged bulb on the concrete, a lantern on the timber
			if (f % 2 == 1 && !last)
			{
				bool lamp = wood > 0.5f;
				Vector3 at = new(HalfW - 0.14f, yl + 2.05f, (lz0 + lz1) * 0.5f);
				AddLight(at, lamp, f);
			}
		}
		var mi = k.CommitTo(this, $"Flights{f0:00}", true);
		mi.VisibilityRangeEnd = 140f;
		mi.VisibilityRangeEndMargin = 10f;
		mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
	}

	/// <summary>The top landing's ceiling strip from z to zb, leaving the hatch's square open.</summary>
	private static void CeilingWithHatch(MeshKit k, float z, float zb, float y)
	{
		float h0 = HatchLocal.Z - 0.5f, h1 = HatchLocal.Z + 0.5f;
		void Q(float x0, float x1, float za, float zc)
		{
			if (x1 - x0 < 0.001f || zc - za < 0.001f) return;
			k.Quad(new Vector3(x1, y, za), new Vector3(x0, y, za), new Vector3(x0, y, zc), new Vector3(x1, y, zc), Vector3.Down,
				new Vector2(x1, za), new Vector2(x0, za), new Vector2(x0, zc), new Vector2(x1, zc));
		}
		float a = Mathf.Max(z, h0), b = Mathf.Min(zb, h1);
		if (b <= a) { Q(-HalfW, HalfW, z, zb); return; }
		Q(-HalfW, HalfW, z, a);
		Q(-HalfW, HalfW, b, zb);
		Q(-HalfW, -0.5f, a, b);
		Q(0.5f, HalfW, a, b);
		// the hatch's shaft up through the crypt's floor
		k.Quad(new Vector3(-0.5f, y, a), new Vector3(0.5f, y, a), new Vector3(0.5f, y + 0.3f, a), new Vector3(-0.5f, y + 0.3f, a), Vector3.Back);
		k.Quad(new Vector3(0.5f, y, b), new Vector3(-0.5f, y, b), new Vector3(-0.5f, y + 0.3f, b), new Vector3(0.5f, y + 0.3f, b), Vector3.Forward);
		k.Quad(new Vector3(-0.5f, y, b), new Vector3(-0.5f, y, a), new Vector3(-0.5f, y + 0.3f, a), new Vector3(-0.5f, y + 0.3f, b), Vector3.Right);
		k.Quad(new Vector3(0.5f, y, a), new Vector3(0.5f, y, b), new Vector3(0.5f, y + 0.3f, b), new Vector3(0.5f, y + 0.3f, a), Vector3.Left);
	}

	private void AddLight(Vector3 at, bool lantern, int f)
	{
		var k = new MeshKit();
		if (lantern)
		{
			// an oil lantern on a hook: a tin frame round a glass chimney, the flame low in it
			k.Mat(StairwellTextures.SteelMat);
			k.Color = new Color(0.3f, 0.28f, 0.25f);
			k.Beam(at + new Vector3(0.12f, 0.25f, 0), at + new Vector3(0, 0.25f, 0), 0.02f, 0.02f);
			k.Cylinder(at + new Vector3(0, -0.12f, 0), at + new Vector3(0, -0.1f, 0), 0.07f, 0.07f, 8, true);
			k.Cylinder(at + new Vector3(0, 0.1f, 0), at + new Vector3(0, 0.16f, 0), 0.07f, 0.03f, 8, true);
			for (int i = 0; i < 4; i++)
			{
				float a = Mathf.Tau * i / 4f + 0.4f;
				Vector3 o = new(Mathf.Cos(a) * 0.065f, 0, Mathf.Sin(a) * 0.065f);
				k.Beam(at + o + new Vector3(0, -0.1f, 0), at + o + new Vector3(0, 0.1f, 0), 0.008f, 0.008f);
			}
		}
		else
		{
			// a bulb in a wire cage on a short bracket
			k.Mat(StairwellTextures.SteelMat);
			k.Color = new Color(0.4f, 0.4f, 0.38f);
			k.Beam(at + new Vector3(0.14f, 0.1f, 0), at + new Vector3(0, 0.1f, 0), 0.03f, 0.03f);
			for (int i = 0; i < 4; i++)
			{
				float a = Mathf.Tau * i / 4f;
				Vector3 o = new(Mathf.Cos(a) * 0.06f, 0, Mathf.Sin(a) * 0.06f);
				k.Beam(at + new Vector3(0, 0.1f, 0), at + o - new Vector3(0, 0.03f, 0), 0.006f, 0.006f);
				k.Beam(at + o - new Vector3(0, 0.03f, 0), at + new Vector3(0, -0.1f, 0), 0.006f, 0.006f);
			}
		}
		k.CommitTo(this, $"Lamp{f}", false);
		var warm = lantern ? new Color(1f, 0.66f, 0.34f) : new Color(1f, 0.92f, 0.78f);
		AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = lantern ? 0.025f : 0.035f, Height = lantern ? 0.06f : 0.07f, RadialSegments = 8, Rings = 4 },
			Position = at + (lantern ? new Vector3(0, -0.04f, 0) : Vector3.Zero),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = warm, EmissionEnabled = true, Emission = warm, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		AddChild(new OmniLight3D
		{
			Position = at + new Vector3(-0.2f, -0.1f, 0), LightColor = warm, LightEnergy = lantern ? 1.1f : 0.8f, OmniRange = 7.5f, OmniAttenuation = 1.3f,
			ShadowEnabled = false, DistanceFadeEnabled = true, DistanceFadeBegin = 40f, DistanceFadeLength = 12f,
		});
	}

	/// <summary>The top: an end wall with a ladder of iron rungs up it, and the hatch in the ceiling.</summary>
	private void BuildTop()
	{
		var k = new MeshKit();
		k.Mat(ChurchTextures.OldPlankMat);
		k.Color = Colors.White * 0.8f;
		float zEnd = TopZ0 + TopLanding;
		Solid(k, _wood, new Vector3(0, TopY + Head * 0.5f, zEnd + 0.15f), new Vector3(HalfW * 2f + WallT * 2f, Head + 0.6f, 0.3f));
		// the rungs
		k.Mat(StairwellTextures.SteelMat);
		k.Color = new Color(0.32f, 0.3f, 0.28f);
		for (int i = 0; i < 6; i++)
		{
			float y = TopY + 0.35f + i * 0.38f;
			k.Beam(new Vector3(-0.25f, y, zEnd - 0.12f), new Vector3(0.25f, y, zEnd - 0.12f), 0.03f, 0.03f);
			foreach (float x in new[] { -0.25f, 0.25f }) k.Beam(new Vector3(x, y, zEnd - 0.12f), new Vector3(x, y, zEnd), 0.025f, 0.025f);
		}
		k.CommitTo(this, "Top", true);
		// the hatch: a square of heavy boards with an iron ring, hinged on its far edge
		// flush with the crypt's floor above (the slab is 0.3 thick), hinged on its far edge
		_hatch = new Node3D { Name = "Hatch", Position = HatchLocal + new Vector3(0, 0.25f, 0.5f) };
		AddChild(_hatch);
		var h = new MeshKit();
		h.Mat(ChurchTextures.OldPlankMat);
		h.Color = Colors.White * 0.7f;
		BuildKit.Box(h, new Vector3(0, 0, -0.5f), new Vector3(1.0f, 0.1f, 1.0f), 1f);
		h.Mat(ChurchTextures.IronMat);
		h.Color = Colors.White;
		foreach (float z in new[] { -0.2f, -0.8f }) BuildKit.Box(h, new Vector3(0, -0.055f, z), new Vector3(1.02f, 0.015f, 0.08f), 1f);
		h.Cylinder(new Vector3(0, -0.07f, -0.5f), new Vector3(0, -0.08f, -0.5f), 0.07f, 0.07f, 12, true);
		h.CommitTo(_hatch, "Lid", true);
		// shut, it can be walked over
		_lidBody = new StaticBody3D { Name = "LidBody", CollisionLayer = 1, CollisionMask = 0 };
		_lidBody.SetMeta("surface", "wood");
		_lidBody.AddChild(new CollisionShape3D { Position = new Vector3(0, 0, -0.5f), Shape = new BoxShape3D { Size = new Vector3(1.0f, 0.1f, 1.0f) } });
		_hatch.AddChild(_lidBody);
		HatchUse = new PickupInteractable
		{
			Name = "HatchUse", PickRadius = 0.5f, MaxDistance = 3.2f, Position = HatchLocal + new Vector3(0, -0.15f, 0),
			PromptFor = _ => "Push the hatch open", CanUse = _ => !HatchOpened,
		};
		HatchUse.Interacted += OnHatch;
		AddChild(HatchUse);
		// a thin cold light round the hatch's edges (daylight, far above)
		AddChild(new OmniLight3D { Position = HatchLocal + Vector3.Down * 0.5f, LightColor = new Color(0.7f, 0.78f, 0.9f), LightEnergy = 0.5f, OmniRange = 5f, ShadowEnabled = false });
		if (StoryManager.Instance is { } s && s.Current >= Checkpoint.Act21ChurchReached) { HatchOpened = true; HatchUse.Enabled = false; }
		// from above, a trapdoor in the crypt's floor that won't budge
		var stuck = new PickupInteractable { Name = "HatchFromAbove", PickRadius = 0.5f, MaxDistance = 2.4f, Position = HatchLocal + new Vector3(0, 0.35f, 0), PromptFor = _ => "It won't lift. Shut fast.", CanUse = _ => false };
		stuck.Interacted += _ => { };
		AddChild(stuck);
	}

	public void Attach(Church church) => Church = church;

	/// <summary>The hatch drops shut (a boom through the crypt), and from above it won't lift again.</summary>
	public void CloseHatch(bool instant = false)
	{
		if (_hatch == null) return;
		foreach (var c in _lidBody.GetChildren()) if (c is CollisionShape3D cs) cs.Disabled = false;
		if (instant) { _hatch.Rotation = Vector3.Zero; return; }
		var tw = CreateTween();
		tw.TweenProperty(_hatch, "rotation:x", 0f, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "door_slam", 1, ToGlobal(HatchLocal), 2f)));
	}

	private void OnHatch(PlayerController player)
	{
		if (HatchOpened || player == null) return;
		HatchOpened = true;
		HatchUse.Enabled = false;
		_ = Cutscene.Run(this, ct => ClimbOut(player, ct), lockInput: true, freezeBody: true);
	}

	/// <summary>Up through the hatch: it grinds up and over on its hinge, grit falls, cold air; the player
	/// climbs the rungs and out onto the crypt's floor; behind them it drops shut with a boom, and won't lift.</summary>
	private async Task ClimbOut(PlayerController player, CancellationToken ct)
	{
		GD.Print("[story] Act 21: the hatch at the top of the long stair");
		AudioDirector.OneShot(this, "door_creak", 1, ToGlobal(HatchLocal), 0f);
		var tw = CreateTween();
		foreach (var c in _lidBody.GetChildren()) if (c is CollisionShape3D cs) cs.Disabled = true;
		tw.TweenProperty(_hatch, "rotation:x", 1.75f, 1.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		Vector3 from = player.GlobalPosition;
		Vector3 under = ToGlobal(HatchLocal + new Vector3(0, -Head + 0.05f, -0.3f));
		Vector3 up = Church != null ? Church.HatchExitWorld : ToGlobal(HatchLocal + new Vector3(0, 0.35f, -0.9f));
		double t = 0;
		while (t < 3.4)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)t;
			Vector3 p = u < 0.9f ? from.Lerp(under, Mathf.SmoothStep(0f, 1f, u / 0.9f))
				: under.Lerp(up, Mathf.SmoothStep(0f, 1f, Mathf.Clamp((u - 1.1f) / 2.1f, 0f, 1f)));
			player.GlobalPosition = p;
			var rig = player.CameraRig;
			rig.SetPitch(Mathf.Lerp(rig.Pitch, u < 2.4f ? 0.9f : 0f, Mathf.Min(1f, dt * 2f)));
		}
		player.GlobalPosition = up;
		player.Velocity = Vector3.Zero;
		CloseHatch();
		GD.Print("[story] Act 21: up into the crypt - the hatch falls shut behind");
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act21ChurchReached);
		await Cutscene.Wait(this, 0.6, ct);
	}
}
