using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Act 14: the stairwell under Room 3. A concrete-lined trench in Room 3's floor steps down into the
/// top of a square shaft, and steel stairs wind down round an open well, four flights to a turn, the
/// way they do in a tall building's fire stairs. It goes down for about five minutes' walk. It is
/// nearly black: a few caged bulbs, most of them dead, and fog that swallows the well a couple of
/// turns down, so the bottom is never in sight and there is no counting what is left.
///
/// The walls change on the way down: clean poured concrete, then stained and cracked, then dark,
/// oily and grimy and no longer quite flat (<c>stairwell_concrete.gdshader</c>, by depth). The
/// stairs stay the same steel all the way.
///
/// Running on these stairs is punished without comment: a second or so into a sprint, the player is
/// moved back up the shaft, whole turns of it, to near the top. Every turn is built identically and
/// the move is a whole number of turns (a multiple of the bulbs' spacing), keeping their exact place
/// on the turn, their speed and their view, and the walls' grime is held where it was and only
/// allowed to drift back slowly, so nothing on screen changes at the moment it happens. Only the
/// numbers painted on the landings give it away, to someone paying attention.
///
/// The events on the way down are in <c>Stairwell.Events.cs</c>; the fallen flight at the bottom
/// (jump across, or jump down) and the chamber under it in <c>Stairwell.Bottom.cs</c>.
///
/// Local space: y=0 is Room 3's floor; x=z=0 is the middle of the well. Corners of each turn are
/// walked in the order (-a,-a) → (-a,a) → (a,a) → (a,-a); the trench comes in from -Z at the first.
/// </summary>
public partial class Stairwell : Node3D
{
	/// <summary>Half the shaft's inside width; a flight's width; a corner's centre; the well's half-width.</summary>
	public const float H = 2.0f, W = 1.1f, A = H - W * 0.5f, Inner = H - W;
	public const float Rise = 0.19f, Run = 0.36f;
	public const int RisesPerFlight = 6;
	public const float FlightDrop = Rise * RisesPerFlight, FlightLen = 2f * H - 2f * W;
	public const int TrenchSteps = 18;
	public const float TrenchRise = 0.17f, TrenchRun = 0.26f;
	public const float Y0 = -TrenchSteps * TrenchRise;
	public const float TrenchZ0 = -H - TrenchSteps * TrenchRun;
	public const float CeilingY = -0.25f;
	public const float RevDrop = FlightDrop * 4f;
	/// <summary>Turns of the stair. 64 is about five minutes at a walk.</summary>
	[Export] public int Revolutions = DefaultRevolutions;
	/// <summary>How long a sprint lasts on these stairs before it is taken back.</summary>
	[Export] public float SprintGrace = 0.7f;
	/// <summary>Bulbs (and everything else that repeats) every this many turns; a loop-back is a multiple of it.</summary>
	public const int Period = 3;
	private const int ChunkRevs = 4;
	private const float DrawRange = 34f;

	public int Flights => Revolutions * 4;
	/// <summary>The top of the fallen flight: the last landing anyone reaches on foot.</summary>
	public int GapCorner => Flights;
	public float BottomY => BottomYFor(Revolutions);
	public const int DefaultRevolutions = 64;
	/// <summary>The chamber floor under a stairwell of <paramref name="revs"/> turns (Continue's marker needs it before the stairwell is built).</summary>
	public static float BottomYFor(int revs) => CornerY(revs * 4 + 1) - FallDepth;
	public const float FallDepth = 21f;

	// ---- for tests and other beats ----
	public static Stairwell Instance { get; private set; }
	/// <summary>How many times a sprint has been taken back.</summary>
	public int LoopBacks { get; private set; }
	/// <summary>The furthest turn reached.</summary>
	public int DeepestRev { get; private set; }
	public float GrimeBias => _grimeBias;
	public bool PlayerInShaft { get; private set; }
	public int PlayerRev { get; private set; }
	public int LitBulbs => _bulbs.Count;

	private static readonly Vector2[] Seq = { new(-A, -A), new(-A, A), new(A, A), new(A, -A) };
	private ShaderMaterial _wallMat;
	private StaticBody3D _body;
	private readonly List<(OmniLight3D light, int rev)> _bulbs = new();
	private readonly RandomNumberGenerator _rng = new() { Seed = 1414 };
	private float _sprint, _grimeBias;
	private AudioStreamPlayer _drone, _hum;
	private float _silenceUntil = -1f;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }
	public override void _Ready() => Callable.From(Build).CallDeferred();

	public static float CornerY(int k) => Y0 - k * FlightDrop;
	public static Vector2 CornerXZ(int k) => Seq[((k % 4) + 4) % 4];
	public Vector3 CornerWorld(int k) => ToGlobal(new Vector3(CornerXZ(k).X, CornerY(k), CornerXZ(k).Y));
	/// <summary>Standing at the top of the trench, about to go down it.</summary>
	public Vector3 TrenchTopWorld => ToGlobal(new Vector3(-A, 0.05f, TrenchZ0 - 0.6f));
	/// <summary>Where flight k leaves corner k, and its direction.</summary>
	public static (Vector3 start, Vector3 dir) FlightStart(int k)
	{
		Vector2 from = CornerXZ(k), to = CornerXZ(k + 1);
		Vector2 d = (to - from).Normalized();
		Vector2 s = from + d * W * 0.5f;
		return (new Vector3(s.X, CornerY(k), s.Y), new Vector3(d.X, 0, d.Y));
	}

	/// <summary>The flight's outward side (toward the wall it runs along).</summary>
	private static Vector3 WallSide(Vector3 start, Vector3 dir)
	{
		Vector3 perp = new(dir.Z, 0, -dir.X);
		Vector3 mid = start + dir * FlightLen * 0.5f;
		return perp.Dot(new Vector3(mid.X, 0, mid.Z)) < 0 ? -perp : perp;
	}

	private void Build()
	{
		_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "metal");
		AddChild(_body);
		var concrete = new StaticBody3D { Name = "Concrete", CollisionLayer = 1, CollisionMask = 0 };
		concrete.SetMeta("surface", "stone");
		AddChild(concrete);

		_wallMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stairwell_concrete.gdshader") };
		_wallMat.SetShaderParameter("clean_tex", StairwellTextures.CleanConcrete);
		_wallMat.SetShaderParameter("stain_tex", StairwellTextures.StainedConcrete);
		_wallMat.SetShaderParameter("grime_tex", StairwellTextures.GrimeConcrete);
		_wallMat.SetShaderParameter("top_y", GlobalPosition.Y + Y0);
		_wallMat.SetShaderParameter("bottom_y", GlobalPosition.Y + CornerY(GapCorner));
		_wallMat.SetShaderParameter("shaft_centre", new Vector2(GlobalPosition.X, GlobalPosition.Z));
		_wallMat.SetShaderParameter("half_width", H);

		BuildTrench(concrete);
		for (int c = 0; c * ChunkRevs < Revolutions; c++) BuildChunk(c);
		BuildWalls(concrete);
		BuildBottom(concrete);
		BuildEvents();

		_drone = Ambient("res://assets/audio/ambient/stairwell_drone_loop.wav");
		_hum = Ambient("res://assets/audio/ambient/stairs_hum_loop.wav");
		SetProcess(true);
		GD.Print($"[story] Act 14: the stairwell - {Revolutions} turns, {Flights} flights, {-CornerY(GapCorner):0} m down");
	}

	// ------------------------------------------------------------------ geometry

	/// <summary>The rectangular hole in Room 3's floor and the concrete steps down it (like a
	/// service trench): block-lined walls, plain poured treads.</summary>
	private void BuildTrench(StaticBody3D concrete)
	{
		var k = new MeshKit();
		k.Mat(StairwellTextures.CleanMat);
		float x0 = -H, x1 = -Inner, cx = (x0 + x1) * 0.5f;
		for (int i = 0; i < TrenchSteps; i++)
		{
			float top = -(i + 1) * TrenchRise, z = TrenchZ0 + (i + 0.5f) * TrenchRun;
			k.Color = Colors.White * (0.85f + 0.05f * ((i * 7) % 3));
			BuildKit.Box(k, new Vector3(cx, top - 0.5f, z), new Vector3(W, 1f, TrenchRun), 0.8f);
		}
		k.Color = Colors.White;
		k.Mat(StairwellTextures.BlockMat);
		foreach (float x in new[] { x0 - 0.1f, x1 + 0.1f })
		{
			var c = new Vector3(x, Y0 * 0.5f, (TrenchZ0 - H) * 0.5f);
			var s = new Vector3(0.2f, -Y0 + 0.1f, -H - TrenchZ0);
			BuildKit.Box(k, c, s, 0.7f);
			concrete.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		// the end wall at the top of the trench
		{
			var c = new Vector3(cx, Y0 * 0.5f, TrenchZ0 - 0.1f);
			var s = new Vector3(W + 0.4f, -Y0 + 0.1f, 0.2f);
			BuildKit.Box(k, c, s, 0.7f);
			concrete.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		// a painted steel lip round the hole
		k.Mat(StairwellTextures.SteelMat);
		k.Color = new Color(0.7f, 0.62f, 0.2f);
		BuildKit.Box(k, new Vector3(x0 - 0.06f, 0.01f, (TrenchZ0 - H) * 0.5f), new Vector3(0.08f, 0.03f, -H - TrenchZ0 + 0.1f));
		BuildKit.Box(k, new Vector3(x1 + 0.06f, 0.01f, (TrenchZ0 - H) * 0.5f), new Vector3(0.08f, 0.03f, -H - TrenchZ0 + 0.1f));
		BuildKit.Box(k, new Vector3(cx, 0.01f, TrenchZ0 - 0.06f), new Vector3(W + 0.2f, 0.03f, 0.08f));
		k.CommitTo(this, "Trench", true);
		// its ramp
		float len = TrenchSteps * TrenchRun, drop = -Y0;
		concrete.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(W, 0.1f, Mathf.Sqrt(len * len + drop * drop)) },
			// +θ about X lowers the +Z end: the trench goes down toward the shaft
			Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Atan2(drop, len)), new Vector3(cx, Y0 * 0.5f - 0.05f, TrenchZ0 + len * 0.5f)),
		});
	}

	/// <summary>Turns [c*ChunkRevs, (c+1)*ChunkRevs): treads, stringers, landings, railings, and the
	/// bulbs, as a few meshes that only draw near the player.</summary>
	private void BuildChunk(int c)
	{
		int k0 = c * ChunkRevs * 4, k1 = Mathf.Min((c + 1) * ChunkRevs * 4, Flights);
		// each chunk's mesh sits at its own middle, so its draw range is measured from there
		float cy = (CornerY(k0) + CornerY(k1)) * 0.5f;
		var xf = new Transform3D(Basis.Identity, new Vector3(0, -cy, 0));
		var steel = new MeshKit { Xf = xf };
		var tread = new MeshKit { Xf = xf };
		var rail = new MeshKit { Xf = xf };
		steel.Mat(StairwellTextures.SteelMat);
		tread.Mat(StairwellTextures.TreadMat);
		rail.Mat(StairwellTextures.RailMat);
		steel.Color = tread.Color = rail.Color = Colors.White;
		for (int k = k0; k < k1; k++)
		{
			Landing(tread, steel, k);
			Flight(tread, steel, rail, k);
			if (k % 4 == 0) RailPost(rail, k);
		}
		// the last landing anyone reaches on foot (the broken flight after it is the bottom's)
		if (k1 == Flights) Landing(tread, steel, GapCorner);
		// the top landing has no flight coming down onto it: rail its open side, over the drop
		if (c == 0) TopRail(rail);
		Commit(steel, $"Steel{c}", cy);
		Commit(tread, $"Treads{c}", cy);
		Commit(rail, $"Rails{c}", cy);
		// bulbs: one every Period turns, on the wall above a landing, most of them dead
		for (int r = c * ChunkRevs; r < Mathf.Min((c + 1) * ChunkRevs, Revolutions); r++)
			if (r % Period == 1) Bulb(r);
	}

	private void Commit(MeshKit k, string name, float y)
	{
		var mi = k.CommitTo(this, name, true);
		mi.Position = new Vector3(0, y, 0);
		mi.VisibilityRangeEnd = DrawRange;
		mi.VisibilityRangeEndMargin = 4f;
	}

	private void Landing(MeshKit tread, MeshKit steel, int k)
	{
		Vector2 p = CornerXZ(k);
		float y = CornerY(k);
		BuildKit.Box(tread, new Vector3(p.X, y - 0.02f, p.Y), new Vector3(W, 0.04f, W), 2.6f);
		// the frame under it, bolted to the two walls
		BuildKit.Box(steel, new Vector3(p.X, y - 0.1f, p.Y), new Vector3(W - 0.1f, 0.12f, W - 0.1f), 1f);
		_body.AddChild(new CollisionShape3D { Position = new Vector3(p.X, y - 0.05f, p.Y), Shape = new BoxShape3D { Size = new Vector3(W, 0.1f, W) } });
	}

	private void Flight(MeshKit tread, MeshKit steel, MeshKit rail, int k)
	{
		var (start, dir) = FlightStart(k);
		Vector3 perp = WallSide(start, dir);
		bool alongX = Mathf.Abs(dir.X) > 0.5f;
		float y = start.Y;
		for (int i = 0; i < RisesPerFlight - 1; i++)
		{
			Vector3 c = start + dir * (i + 0.5f) * Run;
			c.Y = y - (i + 1) * Rise - 0.02f;
			Vector3 s = alongX ? new Vector3(Run - 0.02f, 0.04f, W - 0.04f) : new Vector3(W - 0.04f, 0.04f, Run - 0.02f);
			BuildKit.Box(tread, c, s, 2.6f);
			// a bent-up lip at the back of each tread (the risers are open)
			Vector3 lip = c + dir * (Run * 0.5f - 0.02f) + Vector3.Up * 0.03f;
			BuildKit.Box(tread, lip, alongX ? new Vector3(0.02f, 0.06f, W - 0.04f) : new Vector3(W - 0.04f, 0.06f, 0.02f), 1.4f);
		}
		// the two stringers
		Vector3 end = start + dir * FlightLen + Vector3.Down * FlightDrop;
		foreach (float side in new[] { -1f, 1f })
		{
			Vector3 off = perp * side * (W * 0.5f - 0.03f) + Vector3.Down * 0.14f;
			steel.Beam(start + off, end + off, 0.05f, 0.24f, 1f, Vector3.Up);
		}
		// the railing on the well side, between the well's corner posts: balusters, a mid rail, the handrail
		Vector3 innerOff = -perp * (W * 0.5f - 0.02f);
		for (int b = 1; b <= 5; b++)
		{
			float s = b / 6f;
			Vector3 foot = start + innerOff + dir * FlightLen * s + Vector3.Down * FlightDrop * s;
			rail.Cylinder(foot + Vector3.Down * 0.05f, foot + Vector3.Up * 0.95f, 0.012f, 0.012f, 4, false);
		}
		Vector3 r0 = start + innerOff + Vector3.Up * 0.95f, r1 = end + innerOff + Vector3.Up * 0.95f;
		rail.Cylinder(r0, r1, 0.022f, 0.022f, 6, false);
		rail.Cylinder(r0 + Vector3.Down * 0.45f, r1 + Vector3.Down * 0.45f, 0.012f, 0.012f, 4, false);
		// the ramp underfoot (the player can't climb risers)
		float slope = Mathf.Atan2(FlightDrop, FlightLen);
		float len = Mathf.Sqrt(FlightLen * FlightLen + FlightDrop * FlightDrop);
		Vector3 rc = start + dir * FlightLen * 0.5f + Vector3.Down * (FlightDrop * 0.5f + 0.05f);
		Basis face = Basis.LookingAt(dir, Vector3.Up);            // -Z along the flight
		Basis tilt = face * new Basis(Vector3.Right, -slope);     // lowers the far (-Z) end
		_body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(W, 0.1f, len) }, Transform = new Transform3D(tilt, rc) });
		// the well-side guard: tall enough that nobody hops the rail, and thick (out over the empty well)
		// so nothing leaning on it is ever pushed through
		const float guard = 0.3f;
		Vector3 gc = start + innerOff - perp * guard * 0.5f + dir * FlightLen * 0.5f + Vector3.Up * (0.8f - FlightDrop * 0.5f);
		_body.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = alongX ? new Vector3(FlightLen + 0.06f, FlightDrop + 1.6f, guard) : new Vector3(guard, FlightDrop + 1.6f, FlightLen + 0.06f) },
			Position = gc,
		});
	}

	/// <summary>The top landing's open side (where, on every other turn, the flight above arrives):
	/// a railing and a guard, so the only way on is down the stairs.</summary>
	private void TopRail(MeshKit rail)
	{
		Vector2 p = CornerXZ(0);
		float y = CornerY(0), sx = -Mathf.Sign(p.X);
		float x = p.X + sx * (W * 0.5f - 0.02f);
		Vector3 a = new(x, y, -H), b = new(x, y, -Inner);
		rail.Cylinder(a + Vector3.Up * 0.95f, b + Vector3.Up * 0.95f, 0.022f, 0.022f, 6, false);
		rail.Cylinder(a + Vector3.Up * 0.5f, b + Vector3.Up * 0.5f, 0.012f, 0.012f, 4, false);
		for (int i = 1; i <= 3; i++)
		{
			Vector3 f = a.Lerp(b, i / 4f);
			rail.Cylinder(f, f + Vector3.Up * 0.95f, 0.012f, 0.012f, 4, false);
		}
		_body.AddChild(new CollisionShape3D { Position = new Vector3(x + sx * 0.15f, y + 0.9f, (-H - Inner) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(0.3f, 1.8f, H - Inner + 0.06f) } });
	}

	/// <summary>A corner post of the well, one per turn at the first corner (the rails meet at the others).</summary>
	private static void RailPost(MeshKit rail, int k)
	{
		Vector2 p = CornerXZ(k);
		Vector3 inner = new(Mathf.Sign(p.X) * Inner, CornerY(k), Mathf.Sign(p.Y) * Inner);
		rail.Cylinder(inner + Vector3.Down * 0.2f, inner + Vector3.Up * 1.0f, 0.03f, 0.03f, 6, true);
	}

	/// <summary>A caged bulb on the wall above turn r's third landing. Fewer than half still work,
	/// and even those are weak; none do past turn 50.</summary>
	private void Bulb(int r)
	{
		int k = r * 4 + 2;
		Vector2 p = CornerXZ(k);
		float sx = Mathf.Sign(p.X);
		Vector3 at = new(p.X + sx * (W * 0.5f - 0.08f), CornerY(k) + 2.1f, p.Y);
		bool lit = r < 50 && _rng.Randf() < 0.4f;
		var cage = new MeshKit { Xf = new Transform3D(Basis.Identity, -at) };
		cage.Mat(StairwellTextures.RailMat);
		cage.Color = Colors.White;
		for (int i = 0; i < 4; i++)
		{
			float ang = i * Mathf.Pi * 0.5f;
			Vector3 o = new(Mathf.Cos(ang) * 0.07f, 0, Mathf.Sin(ang) * 0.07f);
			cage.Cylinder(at + o + Vector3.Down * 0.1f, at + o + Vector3.Up * 0.1f, 0.006f, 0.006f, 3, false);
		}
		BuildKit.Box(cage, at + new Vector3(sx * 0.08f, 0, 0), new Vector3(0.03f, 0.14f, 0.14f));
		var cm = cage.CommitTo(this, $"Cage{r}", false);
		cm.Position = at;
		cm.VisibilityRangeEnd = DrawRange;
		AddChild(new MeshInstance3D
		{
			Name = $"Glass{r}", Mesh = new SphereMesh { Radius = 0.045f, Height = 0.1f }, Position = at,
			MaterialOverride = lit ? StationParts.StationTextures.Glow("sw_bulb", new Color(1f, 0.78f, 0.5f), 1.4f)
				: StationParts.StationTextures.Flat("sw_deadbulb", new Color(0.15f, 0.13f, 0.1f), 0.2f, 0.8f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = DrawRange,
		});
		if (!lit) return;
		var light = new OmniLight3D
		{
			Name = $"Bulb{r}", LightColor = new Color(1f, 0.74f, 0.45f), LightEnergy = 0.55f, OmniRange = 4.5f, OmniAttenuation = 1.6f,
			Position = at - new Vector3(sx * 0.2f, 0.1f, 0), ShadowEnabled = false,
			DistanceFadeEnabled = true, DistanceFadeBegin = 22f, DistanceFadeLength = 6f,
		};
		AddChild(light);
		_bulbs.Add((light, r));
	}

	/// <summary>The four walls, top to bottom, as fine grids (so the deep ones can go wavy), chunked
	/// like the stairs; plus their collision, the ceiling over the top, and the trench's way in.</summary>
	private void BuildWalls(StaticBody3D concrete)
	{
		float top = CeilingY, bottom = BottomY + 6f;
		const float seg = 0.6f, col = 0.5f;
		int chunkRows = Mathf.CeilToInt(ChunkRevs * RevDrop / seg);
		int rows = Mathf.CeilToInt((top - bottom) / seg);
		int cols = Mathf.RoundToInt(2f * H / col);
		for (int r0 = 0, c = 0; r0 < rows; r0 += chunkRows, c++)
		{
			float wy = top - (r0 + chunkRows * 0.5f) * seg;
			var k = new MeshKit { Xf = new Transform3D(Basis.Identity, new Vector3(0, -wy, 0)) };
			k.Mat(_wallMat);
			k.Color = Colors.White;
			for (int w = 0; w < 4; w++)
			{
				// wall w: its inward normal, and the axis it runs along
				Vector3 n = w switch { 0 => Vector3.Right, 1 => Vector3.Left, 2 => Vector3.Back, _ => Vector3.Forward };
				Vector3 along = w < 2 ? Vector3.Back : Vector3.Right;
				Vector3 origin = -n * H;
				for (int r = r0; r < Mathf.Min(r0 + chunkRows, rows); r++)
				{
					float ya = top - r * seg, yb = Mathf.Max(bottom, top - (r + 1) * seg);
					for (int ci = 0; ci < cols; ci++)
					{
						float s0 = -H + ci * col, s1 = s0 + col;
						// the way in from the trench: the near (-Z) wall is open above the top landing
						if (w == 2 && ya > Y0 + 0.01f && s1 <= -Inner + 0.01f) continue;
						Vector3 a = origin + along * s0 + Vector3.Up * yb, b = origin + along * s1 + Vector3.Up * yb;
						Vector3 cc = origin + along * s1 + Vector3.Up * ya, d = origin + along * s0 + Vector3.Up * ya;
						Vector2 ua = new(s0 / 2.5f, -yb / 2.5f), ub = new(s1 / 2.5f, -yb / 2.5f), uc = new(s1 / 2.5f, -ya / 2.5f), ud = new(s0 / 2.5f, -ya / 2.5f);
						// wind so the face points along n
						if ((b - a).Cross(d - a).Dot(n) > 0) k.Quad(a, b, cc, d, n, ua, ub, uc, ud);
						else k.Quad(b, a, d, cc, n, ub, ua, ud, uc);
					}
				}
			}
			var mi = k.CommitTo(this, $"Walls{c}", false);
			mi.Position = new Vector3(0, wy, 0);
			mi.VisibilityRangeEnd = DrawRange;
			mi.VisibilityRangeEndMargin = 4f;
			mi.ExtraCullMargin = 0.2f;
		}
		// collision: four tall slabs (the near one split round the way in)
		float hgt = top - bottom, cy = (top + bottom) * 0.5f;
		concrete.AddChild(new CollisionShape3D { Position = new Vector3(-H - 0.1f, cy, 0), Shape = new BoxShape3D { Size = new Vector3(0.2f, hgt, 2f * H + 0.4f) } });
		concrete.AddChild(new CollisionShape3D { Position = new Vector3(H + 0.1f, cy, 0), Shape = new BoxShape3D { Size = new Vector3(0.2f, hgt, 2f * H + 0.4f) } });
		concrete.AddChild(new CollisionShape3D { Position = new Vector3(0, cy, H + 0.1f), Shape = new BoxShape3D { Size = new Vector3(2f * H + 0.4f, hgt, 0.2f) } });
		concrete.AddChild(new CollisionShape3D { Position = new Vector3(0, (Y0 + bottom) * 0.5f, -H - 0.1f), Shape = new BoxShape3D { Size = new Vector3(2f * H + 0.4f, Y0 - bottom, 0.2f) } });
		concrete.AddChild(new CollisionShape3D { Position = new Vector3((H - Inner) * 0.5f, (top + Y0) * 0.5f, -H - 0.1f), Shape = new BoxShape3D { Size = new Vector3(H + Inner, top - Y0, 0.2f) } });
		// the ceiling over the top turn
		var ceil = new MeshKit();
		ceil.Mat(StairwellTextures.CleanMat);
		ceil.Color = new Color(0.8f, 0.8f, 0.8f);
		BuildKit.Box(ceil, new Vector3(0, top + 0.1f, 0), new Vector3(2f * H, 0.2f, 2f * H), 0.4f);
		ceil.CommitTo(this, "Ceiling", false);
		concrete.AddChild(new CollisionShape3D { Position = new Vector3(0, top + 0.1f, 0), Shape = new BoxShape3D { Size = new Vector3(2f * H, 0.2f, 2f * H) } });
	}

	// ------------------------------------------------------------------ the player in the shaft

	private AudioStreamPlayer Ambient(string path)
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
		var p = new AudioStreamPlayer { Stream = stream, Bus = "Unnatural", VolumeDb = -80f };
		AddChild(p);
		return p;
	}

	/// <summary>Which turn a local height is on (0 at the top landing).</summary>
	public static float RevAt(float localY) => (Y0 - localY) / RevDrop;

	public bool InShaft(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		return Mathf.Abs(l.X) < H + 0.1f && Mathf.Abs(l.Z) < H + 0.1f && l.Y < Y0 + 1.2f && l.Y > BottomY - 2f;
	}

	/// <summary>In the trench or anywhere below Room 3's floor here.</summary>
	public bool Underground(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		bool trench = Mathf.Abs(l.X + A) < W && l.Z < -H + 0.2f && l.Z > TrenchZ0 - 0.5f && l.Y < -0.4f;
		bool chamber = l.Y < BottomY + 9f && Mathf.Abs(l.X) < 8f && l.Z > -8f && l.Z < HallwayZ;
		return trench || InShaft(world) || chamber;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		var player = StoryBeat.Player(this);
		if (player == null) return;
		Vector3 l = ToLocal(player.GlobalPosition);
		PlayerInShaft = InShaft(player.GlobalPosition);
		bool under = Underground(player.GlobalPosition);
		float rev = Mathf.Max(0f, RevAt(l.Y));
		PlayerRev = Mathf.FloorToInt(rev);
		if (PlayerInShaft && PlayerRev > DeepestRev && l.Y > CornerY(GapCorner) - 1f) DeepestRev = PlayerRev;

		// underground: the world's light goes and the fog turns black (past the chamber, the hallway has it)
		float want = under ? Mathf.Clamp(-l.Y / 3f, 0f, 1f) : 0f;
		bool ours = Mathf.Abs(l.X) < 12f && l.Z < HallwayZ && l.Z > -30f;
		if (ours && StoryBeat.Atmosphere(this) is { } atmo)
		{
			atmo.Underground = Mathf.MoveToward(atmo.Underground, want, dt * 0.8f);
			atmo.UndergroundFogDensity = 0.085f;
			atmo.UndergroundFogColor = new Color(0.004f, 0.004f, 0.005f);
		}

		// the shaft's air, and the hum, rising with depth
		float depth = Mathf.Clamp(rev / Revolutions, 0f, 1f);
		bool silent = _silenceUntil > 0 && Time.GetTicksMsec() * 0.001f < _silenceUntil;
		SetLevel(_drone, under && !silent ? Mathf.Lerp(-18f, -8f, depth) : -80f, dt);
		SetLevel(_hum, under ? Mathf.Lerp(-30f, -8f, depth * depth) : -80f, dt);

		// grime held after a loop-back drifts back to the truth, slower than the walls grime on the way down
		if (_grimeBias > 0f)
		{
			_grimeBias = Mathf.MoveToward(_grimeBias, 0f, dt * 0.003f);
			_wallMat.SetShaderParameter("grime_bias", _grimeBias);
		}

		// running: taken back up
		if (PlayerInShaft && player.IsRunning && player.PlayerInput.Enabled && rev >= Period + 1f && PlayerRev < Revolutions - 2)
		{
			_sprint += dt;
			if (_sprint > SprintGrace) LoopBack(player, PlayerRev);
		}
		else _sprint = Mathf.Max(0f, _sprint - dt * 2f);

		ProcessEvents(player, l, dt);
		PollEdge();
	}

	private static void SetLevel(AudioStreamPlayer p, float db, float dt)
	{
		if (p == null) return;
		p.VolumeDb = Mathf.MoveToward(p.VolumeDb, db, dt * 20f);
		if (p.VolumeDb > -79f && !p.Playing) p.Play();
		else if (p.VolumeDb <= -79f && p.Playing) p.Stop();
	}

	/// <summary>Moves the running player up a whole number of turns (a multiple of <see cref="Period"/>)
	/// so they land two to four turns from the top, exactly where they were on the turn, still running,
	/// looking the same way. The walls' grime is held so nothing on screen changes.</summary>
	public void LoopBack(PlayerController player, int rev)
	{
		int target = Period - 1 + ((rev - (Period - 1)) % Period);
		int up = rev - target;
		if (up <= 0) return;
		_sprint = 0f;
		Vector3 d = ToGlobal(new Vector3(0, up * RevDrop, 0)) - ToGlobal(Vector3.Zero);
		player.GlobalPosition += d;
		player.CameraRig?.ShiftBy(d);
		float span = Mathf.Max(0.001f, Y0 - CornerY(GapCorner));
		_grimeBias += up * RevDrop / span;
		_wallMat.SetShaderParameter("grime_bias", _grimeBias);
		LoopBacks++;
		PlayerRev = target;
		GD.Print($"[story] Act 14: ran on the stairs - quietly back up {up} turns (turn {rev} to {target})");
	}
}
