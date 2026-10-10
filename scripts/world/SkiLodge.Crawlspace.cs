using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// Act 23, the crawlspace (the owner's document and references: the cavity between a house's walls, Evil Dead II's):
/// through the hole hacked in 201's bathroom wall and into the dark between the rooms. Rough pine boards on both
/// hands, studs, old brick in places, pipes along the top and now and then across at the height of a head (duck
/// under); dust sifting down, flaking off the walls
/// as they brush past. A maze: turns, a narrow flight of stairs down between the floors, dead ends. It howls,
/// somewhere in the walls, the whole way. About halfway, its arm comes through the boards ahead of them, reaching and
/// grabbing, and the only way on is under it; four times, louder and bloodier each time. At the end, the back of a
/// wardrobe: pushed over, it's the dining hall, and the lodge has frozen over (SkiLodge.Frozen.cs).
///
/// The cavity is laid out on a grid of 1 m cells (lodge-local x = <see cref="CrawlOX"/> + i, z = <see cref="CrawlOZ"/> + j):
/// a cell's walls stand wherever it has no neighbour it opens onto. Only its two ends are where they seem to be: a
/// few metres behind 201's bathroom and a chase boxed into the dining hall's back wall. The rest lies
/// <see cref="CrawlDown"/> below, and the player is carried between the two (a seamless step: the ends are built in
/// both places, and the hand-over happens halfway along a straight between two turns, where what can be seen is the
/// same in both). It's the lodge's "loading screen": while they're in it, the lodge freezes over.
/// </summary>
public partial class SkiLodge
{
	public const float CrawlOX = -28.95f, CrawlOZ = 1.95f, CrawlH = 2.2f;
	/// <summary>Where the maze lies, from where it seems to be.</summary>
	public static readonly Vector3 CrawlDown = new(0, -60f, 0);
	/// <summary>The two hand-overs: across z = <see cref="PortalInZ"/> in column <see cref="PortalInI"/> (after 201's
	/// first turn), across x = <see cref="PortalOutX"/> in row <see cref="PortalOutJ"/> (inside the dining hall's chase).</summary>
	public const int PortalInI = -2, PortalOutJ = -8;
	public static readonly float PortalInZ = CrawlOZ - 1.5f, PortalOutX = CrawlOX + 62.5f;

	private sealed class CCell
	{
		public float Y0, Y1;
		public Vector2I Dir;
		public bool Stair => Dir != Vector2I.Zero;
	}

	public bool InCrawlspace { get; private set; }
	public bool InMaze { get; private set; }
	public int CrawlShifts { get; private set; }
	public readonly List<WendigoArm> Arms = new();
	public int ArmsBurst { get; private set; }
	public int ArmGrabs { get; private set; }
	public int Howls { get; private set; }
	public int DustFalls { get; private set; }
	public Node3D Wardrobe { get; private set; }
	public PickupInteractable WardrobeUse { get; private set; }
	public bool WardrobeDown { get; private set; }
	/// <summary>The cells, for tests: the maze's path in order (its main line, start to the slit).</summary>
	public readonly List<Vector2I> CrawlPath = new();
	public static readonly Vector2I[] CrouchPipes =
	{
		new(-7, -3), new(-13, -3), new(-3, -5), new(10, -5), new(16, -1), new(22, -3), new(28, 3), new(42, 0), new(50, -1),
	};
	/// <summary>The four arms: the cell, and the wall it comes out of.</summary>
	public static readonly (Vector2I cell, Vector2I side)[] ArmSpots =
	{
		(new(39, -3), new(0, -1)), (new(46, 3), new(0, -1)), (new(53, -6), new(0, -1)), (new(59, -4), new(-1, 0)),
	};
	public static readonly Vector2I CrawlSave = new(22, -6);

	private readonly Dictionary<Vector2I, CCell> _maze = new();
	private Vector3 _crawlPrev;
	private bool _crawlPrevSet;
	private GpuParticles3D _motes;
	private readonly GpuParticles3D[] _flakes = new GpuParticles3D[3];
	private int _flakeNext;
	private double _flakeAt, _howlAt;
	private readonly bool[] _armPassed = new bool[4];
	private readonly float[] _armClosest = { 99f, 99f, 99f, 99f };
	private bool _crawlSaved, _frozenSaved;

	public static Vector3 CellCentre(Vector2I c) => new(CrawlOX + c.X, 0, CrawlOZ + c.Y);

	// ------------------------------------------------------------------ the layout

	/// <summary>The maze's main line (i, j, level: U upstairs, L downstairs, s the stair to here), then its dead ends.</summary>
	private static readonly (int i, int j, char lv)[] CrawlLine =
	{
		(0, 0, 'U'), (-2, 0, 'U'), (-2, -3, 'U'), (-10, -3, 'U'), (-10, 2, 'U'), (-13, 2, 'U'), (-13, -8, 'U'), (-6, -8, 'U'),
		(-6, -5, 'U'), (0, -5, 'U'), (0, -8, 'U'), (4, -8, 'U'), (9, -8, 's'), (10, -8, 'L'), (10, -2, 'L'), (6, -2, 'L'),
		(6, 3, 'L'), (16, 3, 'L'), (16, -6, 'L'), (22, -6, 'L'), (22, 1, 'L'), (28, 1, 'L'), (28, 5, 'L'), (36, 5, 'L'),
		(36, -3, 'L'), (42, -3, 'L'), (42, 3, 'L'), (50, 3, 'L'), (50, -6, 'L'), (56, -6, 'L'), (56, 1, 'L'), (59, 1, 'L'),
		(59, -10, 'L'), (64, -10, 'L'), (64, -8, 'L'), (61, -8, 'L'), (61, -7, 'L'),
	};
	private static readonly (int i0, int j0, int i1, int j1, char lv)[] CrawlSpurs =
	{
		(-8, -3, -8, 1, 'U'), (-13, -5, -11, -5, 'U'), (13, 3, 13, -1, 'L'), (22, -2, 19, -2, 'L'), (36, 1, 39, 1, 'L'), (50, -2, 47, -2, 'L'), (59, -3, 61, -3, 'L'),
	};
	/// <summary>The real ends: behind 201's bathroom (upstairs) and the dining hall's chase (downstairs).</summary>
	private static readonly Vector2I[] EntryCells = { new(0, 0), new(-1, 0), new(-2, 0), new(-2, -1), new(-2, -2), new(-2, -3), new(-3, -3), new(-4, -3) };
	private static readonly Vector2I[] ExitCells = { new(64, -9), new(64, -8), new(63, -8), new(62, -8), new(61, -8), new(61, -7) };

	private void LayMaze()
	{
		float U = UpperY, L = FloorY;
		Vector2I prev = new(CrawlLine[0].i, CrawlLine[0].j);
		_maze[prev] = new CCell { Y0 = U, Y1 = U };
		CrawlPath.Add(prev);
		for (int w = 1; w < CrawlLine.Length; w++)
		{
			var (i, j, lv) = CrawlLine[w];
			var to = new Vector2I(i, j);
			var step = new Vector2I(Mathf.Sign(to.X - prev.X), Mathf.Sign(to.Y - prev.Y));
			int n = Mathf.Abs(to.X - prev.X) + Mathf.Abs(to.Y - prev.Y);
			for (int k = 1; k <= n; k++)
			{
				var c = prev + step * k;
				if (lv == 's') _maze[c] = new CCell { Y0 = Mathf.Lerp(U, L, (k - 1f) / n), Y1 = Mathf.Lerp(U, L, (float)k / n), Dir = step };
				else { float y = lv == 'U' ? U : L; _maze[c] = new CCell { Y0 = y, Y1 = y }; }
				CrawlPath.Add(c);
			}
			prev = to;
		}
		foreach (var (i0, j0, i1, j1, lv) in CrawlSpurs)
		{
			float y = lv == 'U' ? U : L;
			var a = new Vector2I(i0, j0);
			var step = new Vector2I(Mathf.Sign(i1 - i0), Mathf.Sign(j1 - j0));
			int n = Mathf.Abs(i1 - i0) + Mathf.Abs(j1 - j0);
			for (int k = 1; k <= n; k++) { var c = a + step * k; if (!_maze.ContainsKey(c)) _maze[c] = new CCell { Y0 = y, Y1 = y }; }
		}
	}

	private static float EdgeY(CCell c, Vector2I side)
	{
		if (!c.Stair) return c.Y0;
		if (side == c.Dir) return c.Y1;
		if (side == -c.Dir) return c.Y0;
		return float.NaN;
	}

	private static bool Opens(Dictionary<Vector2I, CCell> cells, Vector2I at, Vector2I side)
	{
		if (!cells.TryGetValue(at, out var a) || !cells.TryGetValue(at + side, out var b)) return false;
		float ea = EdgeY(a, side), eb = EdgeY(b, -side);
		return !float.IsNaN(ea) && !float.IsNaN(eb) && Mathf.Abs(ea - eb) < 0.05f;
	}

	private static float FloorAt(CCell c, float lx, float lz)
	{
		if (!c.Stair) return c.Y0;
		float t = lx * c.Dir.X + lz * c.Dir.Y;
		return c.Y0 + (t + 0.5f) * (c.Y1 - c.Y0);
	}

	private static float CellHash(Vector2I c, int salt)
	{
		uint h = (uint)(c.X * 73856093 ^ c.Y * 19349663 ^ salt * 83492791);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
	}

	/// <summary>Brick here? (Stretches of it: whole runs of old brick with plaster over it, on one side.)</summary>
	private static bool BrickAt(Vector2I c, Vector2I side) => (side.X + side.Y) > 0 && CellHash(new Vector2I(c.X / 4, c.Y / 4), 11) < 0.35f;

	// ------------------------------------------------------------------ building

	private void BuildCrawlspace()
	{
		if (_maze.Count > 0) return;
		LayMaze();
		// the real ends (their cells' levels as the maze's)
		var entry = new Dictionary<Vector2I, CCell>();
		foreach (var c in EntryCells) entry[c] = _maze[c];
		var exit = new Dictionary<Vector2I, CCell>();
		foreach (var c in ExitCells) exit[c] = _maze[c];
		var entryOpen = new HashSet<(Vector2I, Vector2I)> { (new Vector2I(0, 0), new Vector2I(1, 0)) };
		var exitOpen = new HashSet<(Vector2I, Vector2I)> { (new Vector2I(61, -7), new Vector2I(0, 1)) };
		BuildCells(entry, Vector3.Zero, entryOpen, "CrawlEntry", false);
		BuildCells(exit, Vector3.Zero, exitOpen, "CrawlExit", false);
		BuildCells(_maze, CrawlDown, new HashSet<(Vector2I, Vector2I)>(), "CrawlMaze", true);
		BuildEntryConnector();
		BuildExitChase();
		// the maze's own business: the pipes across (duck), the arms (waiting in the walls), the dust, the echo
		var maze = GetNode<Node3D>("CrawlMaze");
		foreach (var p in CrouchPipes) CrossPipe(maze, p);
		DressCrawlspace(maze);   // (the posters and papers on its walls, and the lights along its ceiling)
		for (int a = 0; a < ArmSpots.Length; a++)
		{
			var (cell, side) = ArmSpots[a];
			var c = CellCentre(cell) + CrawlDown;
			float y = _maze[cell].Y0 + CrawlDown.Y;
			var arm = new WendigoArm { Name = $"Arm{a}", Bloodiness = 0.2f + 0.26f * a, Across = 0.92f };
			AddChild(arm);
			arm.Position = new Vector3(c.X + side.X * 0.46f, y + 1.58f, c.Z + side.Y * 0.46f);   // (its lowest reach above a crouched head)
			var into = new Vector3(-side.X, 0, -side.Y);
			arm.Basis = new Basis(into, Vector3.Up, into.Cross(Vector3.Up));
			int idx = a;
			arm.Prey = () => StoryBeat.Player(this)?.HeadWorld - Vector3.Up * 0.12f;
			arm.Grabbed += _ => ArmGrab(idx);
			Arms.Add(arm);
		}
		_motes = new GpuParticles3D
		{
			Name = "CrawlMotes", Amount = 70, Lifetime = 5f, Emitting = false, LocalCoords = false,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(1.6f, 1.1f, 1.6f),
				Direction = Vector3.Down, Spread = 40f, InitialVelocityMin = 0.02f, InitialVelocityMax = 0.08f, Gravity = new Vector3(0, -0.05f, 0),
				TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.2f, ScaleMin = 0.5f, ScaleMax = 1.2f,
			},
			DrawPass1 = new QuadMesh { Size = new Vector2(0.012f, 0.012f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.58f, 0.5f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_motes);
		for (int i = 0; i < _flakes.Length; i++)
		{
			_flakes[i] = new GpuParticles3D
			{
				Name = $"CrawlFlakes{i}", Amount = 26, Lifetime = 1.6f, OneShot = true, Explosiveness = 0.7f, Emitting = false, LocalCoords = false,
				ProcessMaterial = new ParticleProcessMaterial
				{
					EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.2f, 0.15f, 0.2f),
					Direction = Vector3.Down, Spread = 25f, InitialVelocityMin = 0.1f, InitialVelocityMax = 0.4f, Gravity = new Vector3(0, -2.2f, 0),
					ScaleMin = 0.6f, ScaleMax = 1.6f, Color = new Color(0.5f, 0.45f, 0.38f, 0.8f),
				},
				DrawPass1 = new QuadMesh { Size = new Vector2(0.02f, 0.02f), Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild(_flakes[i]);
		}
		// tight wooden walls: every sound close and dry, running off down the cavity
		var mmin = new Vector3(CrawlOX - 14f, 0, CrawlOZ - 11f) + CrawlDown;
		var mmax = new Vector3(CrawlOX + 65f, 0, CrawlOZ + 6f) + CrawlDown;
		AudioDirector.Zone(this, (mmin + mmax) * 0.5f + Vector3.Up * 1.5f, (mmax - mmin) with { Y = 12f }, AudioDirector.Space.Tunnel, "CrawlVerb");
		if (Has(LodgeFlag.WardrobeDown)) KnockWardrobe(instant: true);
	}

	/// <summary>A run of cells: floors (or a flight of steps), ceilings, walls wherever a cell doesn't open onto its
	/// neighbour, studs, pipes along the top; one body for their floors and walls.</summary>
	private void BuildCells(Dictionary<Vector2I, CCell> cells, Vector3 offset, HashSet<(Vector2I, Vector2I)> open, string name, bool hidden)
	{
		var node = new Node3D { Name = name };
		AddChild(node);
		var boards = new MeshKit(); boards.Mat(LodgeTextures.CrawlBoardsMat); boards.Color = Colors.White;
		var brick = new MeshKit(); brick.Mat(LodgeTextures.CrawlBrickMat); brick.Color = Colors.White;
		var floor = new MeshKit(); floor.Mat(LodgeTextures.CrawlFloorMat); floor.Color = Colors.White;
		var ceil = new MeshKit(); ceil.Mat(LodgeTextures.CrawlCeilingMat); ceil.Color = Colors.White;
		var trim = new MeshKit(); trim.Mat(LodgeTextures.CrawlStudMat); trim.Color = Colors.White;
		var pipe = new MeshKit(); pipe.Mat(LodgeTextures.CrawlPipeMat); pipe.Color = Colors.White;
		var faces = new List<Vector3>();
		Vector2I[] sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
		foreach (var (at, cell) in cells)
		{
			var c = CellCentre(at) + offset;
			float oy = offset.Y;
			Vector3 P(float lx, float lz, float up) => new(c.X + lx, FloorAt(cell, lx, lz) + oy + up, c.Z + lz);
			// the floor
			if (!cell.Stair)
			{
				floor.Quad(P(-0.5f, 0.5f, 0.004f), P(0.5f, 0.5f, 0.004f), P(0.5f, -0.5f, 0.004f), P(-0.5f, -0.5f, 0.004f), Vector3.Up,
					new Vector2(c.X, c.Z + 0.5f) * 0.5f, new Vector2(c.X + 1f, c.Z + 0.5f) * 0.5f, new Vector2(c.X + 1f, c.Z - 0.5f) * 0.5f, new Vector2(c.X, c.Z - 0.5f) * 0.5f);
			}
			else
			{
				// three treads and their risers, stepping down along Dir (the collision's a ramp under them)
				var d = new Vector3(cell.Dir.X, 0, cell.Dir.Y);
				var sd = new Vector3(-cell.Dir.Y, 0, cell.Dir.X);
				float drop = cell.Y1 - cell.Y0;
				for (int s = 0; s < 3; s++)
				{
					float t0 = -0.5f + s / 3f, t1 = -0.5f + (s + 1) / 3f, h = cell.Y0 + drop * (s + 0.5f) / 3f + oy;
					Vector3 a0 = c + d * t0 + sd * 0.5f, a1 = c + d * t1 + sd * 0.5f, b1 = c + d * t1 - sd * 0.5f, b0 = c + d * t0 - sd * 0.5f;
					a0.Y = a1.Y = b1.Y = b0.Y = h;
					floor.Quad(a0, a1, b1, b0, Vector3.Up);
					if (s == 0)
					{
						// the top step's own riser, down from the landing
						Vector3 q0 = c + d * t0 + sd * 0.5f, q1 = c + d * t0 - sd * 0.5f;
						floor.Quad(q0 with { Y = cell.Y0 + oy }, q1 with { Y = cell.Y0 + oy }, q1 with { Y = h }, q0 with { Y = h }, -d);
					}
					float hn = s < 2 ? cell.Y0 + drop * (s + 1.5f) / 3f + oy : cell.Y1 + drop / 6f + oy;
					Vector3 r0 = c + d * t1 + sd * 0.5f, r1 = c + d * t1 - sd * 0.5f;
					floor.Quad(r0 with { Y = hn }, r1 with { Y = hn }, r1 with { Y = h }, r0 with { Y = h }, -d);
				}
			}
			faces.AddRange(new[] { P(-0.5f, -0.5f, 0), P(0.5f, -0.5f, 0), P(0.5f, 0.5f, 0), P(-0.5f, -0.5f, 0), P(0.5f, 0.5f, 0), P(-0.5f, 0.5f, 0) });
			// the ceiling: boards, and a joist across
			ceil.Quad(P(-0.5f, -0.5f, CrawlH), P(0.5f, -0.5f, CrawlH), P(0.5f, 0.5f, CrawlH), P(-0.5f, 0.5f, CrawlH), Vector3.Down,
				new Vector2(c.X, c.Z) * 0.5f, new Vector2(c.X + 1f, c.Z) * 0.5f, new Vector2(c.X + 1f, c.Z + 1f) * 0.5f, new Vector2(c.X, c.Z + 1f) * 0.5f);
			trim.Box(new Vector3(c.X, FloorAt(cell, 0, 0) + oy + CrawlH - 0.06f, c.Z), new Vector3(1f, 0.12f, 0.08f), 1f, CellHash(at, 5) < 0.5f ? Basis.Identity : new Basis(Vector3.Up, Mathf.Pi * 0.5f));
			// the walls
			foreach (var side in sides)
			{
				if (Opens(cells, at, side) || open.Contains((at, side))) continue;
				var across = new Vector2(side.Y, -side.X);   // along the wall
				float ix = side.X * 0.46f, iz = side.Y * 0.46f;
				float sink = cell.Stair ? -0.12f : 0f;   // (a stair's walls go down past its treads' edges)
				// (each panel runs 6 cm past its cell's edge at both ends: inset 4 cm from the edge, two panels meeting at a turn or
				// a branch left a slit between them at the corner, the outside showing through the stud space; the owner saw it)
				// (only where the wall turns: along a straight run the next cell's panel continues it exactly, and an overlap
				// there put boards and brick in one plane, flickering)
				var acr = new Vector2I(Mathf.RoundToInt(across.X), Mathf.RoundToInt(across.Y));
				bool Continues(Vector2I n) => cells.ContainsKey(n) && !Opens(cells, n, side) && !open.Contains((n, side));
				float r0 = Continues(at + acr) ? 0.5f : 0.56f, r1 = Continues(at - acr) ? 0.5f : 0.56f;
				Vector3 w0 = P(ix + across.X * r0, iz + across.Y * r0, sink), w1 = P(ix - across.X * r1, iz - across.Y * r1, sink);
				// (a stair's side walls follow its slope; the floor under a wall's end is the edge's own)
				Vector3 w0t = w0 + Vector3.Up * (CrawlH - sink), w1t = w1 + Vector3.Up * (CrawlH - sink);
				var inward = new Vector3(-side.X, 0, -side.Y);
				var k = BrickAt(at, side) ? brick : boards;
				float u0 = side.X != 0 ? w0.Z : w0.X, u1 = side.X != 0 ? w1.Z : w1.X;
				k.Quad(w0, w1, w1t, w0t, inward, new Vector2(u0, w0.Y - oy), new Vector2(u1, w1.Y - oy), new Vector2(u1, w1t.Y - oy), new Vector2(u0, w0t.Y - oy));
				faces.AddRange(new[] { w0, w1, w1t, w0, w1t, w0t });
				if (k == boards)
				{
					// a stud at the middle of the panel, standing proud of the boards
					var mid = (w0 + w1) * 0.5f;
					trim.Box(mid + Vector3.Up * CrawlH * 0.5f + inward * 0.015f, new Vector3(Mathf.Abs(across.X) > 0 ? 0.06f : 0.035f, CrawlH, Mathf.Abs(across.Y) > 0 ? 0.06f : 0.035f), 1f);
					// (no light slipping between the boards any more: the thin bright strips, a few millimetres off the boards,
					// read as seams in the walls with the daylight through them, and broke up into dashes at a distance; the
					// owner saw them)
				}
				// the pipes along the top, on the walls facing +x or +z (so they run on unbroken down a straight)
				if (side.X + side.Y > 0)
				{
					Vector3 a = w0 + Vector3.Up * (CrawlH - 0.2f) + inward * 0.1f, b = w1 + Vector3.Up * (CrawlH - 0.2f) + inward * 0.1f;
					pipe.Cylinder(a, b, 0.035f, 0.035f, 6, false);
				}
			}
		}
		boards.CommitTo(node, "Boards", false);
		brick.CommitTo(node, "Brick", false);
		floor.CommitTo(node, "Floor", false);
		ceil.CommitTo(node, "Ceiling", false);
		trim.CommitTo(node, "Studs", false);
		pipe.CommitTo(node, "Pipes", false);
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
		shape.SetFaces(faces.ToArray());
		body.AddChild(new CollisionShape3D { Shape = shape });
		node.AddChild(body);
	}

	/// <summary>A pipe across the cavity at the height of a head (1.22 m): the only way under is crouched.</summary>
	private void CrossPipe(Node3D maze, Vector2I at)
	{
		if (!_maze.TryGetValue(at, out var cell)) return;
		var c = CellCentre(at) + CrawlDown;
		float y = cell.Y0 + CrawlDown.Y + 1.24f;
		bool alongX = Opens(_maze, at, new Vector2I(1, 0)) || Opens(_maze, at, new Vector2I(-1, 0));
		Vector3 span = alongX ? new Vector3(0, 0, 0.46f) : new Vector3(0.46f, 0, 0);
		var k = new MeshKit();
		k.Mat(CellHash(at, 31) < 0.5f ? LodgeTextures.CrawlPipeMat : LodgeTextures.CopperMat);
		k.Color = Colors.White;
		k.Cylinder(new Vector3(c.X, y, c.Z) - span, new Vector3(c.X, y, c.Z) + span, 0.06f, 0.06f, 8, false);
		k.Cylinder(new Vector3(c.X, y, c.Z) - span * 0.5f, new Vector3(c.X, y, c.Z) - span * 0.43f, 0.075f, 0.075f, 8, true);
		k.Cylinder(new Vector3(c.X, y, c.Z) + span * 0.43f, new Vector3(c.X, y, c.Z) + span * 0.5f, 0.075f, 0.075f, 8, true);
		// frost on it (a cold pipe) and a rag of insulation hanging
		k.Mat(WinterWoods.SoftSnow);
		k.Blob(new Vector3(c.X, y + 0.05f, c.Z) + span * 0.2f, new Vector3(0.09f, 0.03f, 0.09f), 3000 + at.X * 7 + at.Y, 0.3f, false, 1f);
		k.CommitTo(maze, $"Pipe_{at.X}_{at.Y}", false);
		var body = new StaticBody3D { Name = $"PipeBody_{at.X}_{at.Y}", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "metal");
		body.AddChild(new CollisionShape3D { Position = new Vector3(c.X, y, c.Z), Shape = new BoxShape3D { Size = alongX ? new Vector3(0.16f, 0.16f, 0.94f) : new Vector3(0.94f, 0.16f, 0.16f) } });
		maze.AddChild(body);
	}

	/// <summary>Through the wall's thickness from 201's bathroom into the first cell (the hole's ragged boards are Room201's).</summary>
	private void BuildEntryConnector()
	{
		float x0 = CrawlOX + 0.5f, x1 = CorrX0 - 0.07f, y = UpperY, z0 = Hole201.Z - 0.45f, z1 = Hole201.Z + 0.45f;
		var k = new MeshKit();
		k.Mat(LodgeTextures.CrawlFloorMat); k.Color = Colors.White;
		k.Quad(new Vector3(x0, y + 0.004f, z1 + 0.05f), new Vector3(x1, y + 0.004f, z1 + 0.05f), new Vector3(x1, y + 0.004f, z0 - 0.05f), new Vector3(x0, y + 0.004f, z0 - 0.05f), Vector3.Up);
		k.Mat(LodgeTextures.CrawlBoardsMat);
		k.Quad(new Vector3(x1, y, z0), new Vector3(x0, y, z0), new Vector3(x0, y + CrawlH, z0), new Vector3(x1, y + CrawlH, z0), Vector3.Back);
		k.Quad(new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y + CrawlH, z1), new Vector3(x0, y + CrawlH, z1), Vector3.Forward);
		k.Mat(LodgeTextures.CrawlCeilingMat);
		k.Quad(new Vector3(x0, y + 2.0f, z0), new Vector3(x1, y + 2.0f, z0), new Vector3(x1, y + 2.0f, z1), new Vector3(x0, y + 2.0f, z1), Vector3.Down);
		k.Quad(new Vector3(x0, y + 2.0f, z1), new Vector3(x0, y + CrawlH, z1), new Vector3(x0, y + CrawlH, z0), new Vector3(x0, y + 2.0f, z0), Vector3.Left);
		k.CommitTo(this, "CrawlConnector", false);
		LodgeKit.Solid(_inBody, new Vector3((x0 + x1) * 0.5f, y - 0.1f, Hole201.Z), new Vector3(x1 - x0 + 0.1f, 0.2f, 1.0f));
	}

	/// <summary>The chase boxed into the dining hall's back wall that the cavity ends in (it looks like a built-in: the
	/// room's wainscot and paper round it, up to the ceiling), a slit at the foot of its front, and the wardrobe
	/// standing in front of the slit.</summary>
	private void BuildExitChase()
	{
		float x0 = 31.25f, x1 = 36.05f, z0 = -InnerZ, z1 = -4.5f, top = RoomTop;
		var slit = CellCentre(new Vector2I(61, -7));
		var k = new MeshKit();
		k.Color = Colors.White;
		// its front (+z) and two sides: wainscot to 1.3 m, the room's paper above, a slit at the foot of the front
		var hole = new List<LodgeKit.Hole> { new(slit.X - 0.5f - x0, slit.X + 0.5f - x0, FloorY, 1.25f) };
		LodgeKit.Wall(k, _inBody, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), FloorY, 1.3f, 0.06f, Vector3.Back, LodgeTextures.DarkWoodMat, LodgeTextures.CrawlBoardsMat, LodgeTextures.DarkWoodMat, hole, occlude: false);
		LodgeKit.Wall(k, _inBody, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), 1.3f, top, 0.06f, Vector3.Back, LodgeTextures.WallpaperMat, LodgeTextures.CrawlBoardsMat, LodgeTextures.DarkWoodMat, occlude: false);
		LodgeKit.Wall(k, _inBody, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), FloorY, 1.3f, 0.06f, Vector3.Left, LodgeTextures.DarkWoodMat, LodgeTextures.CrawlBoardsMat, LodgeTextures.DarkWoodMat, occlude: false);
		LodgeKit.Wall(k, _inBody, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), 1.3f, top, 0.06f, Vector3.Left, LodgeTextures.WallpaperMat, LodgeTextures.CrawlBoardsMat, LodgeTextures.DarkWoodMat, occlude: false);
		LodgeKit.Wall(k, _inBody, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), FloorY, 1.3f, 0.06f, Vector3.Right, LodgeTextures.DarkWoodMat, LodgeTextures.CrawlBoardsMat, LodgeTextures.DarkWoodMat, occlude: false);
		LodgeKit.Wall(k, _inBody, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), 1.3f, top, 0.06f, Vector3.Right, LodgeTextures.WallpaperMat, LodgeTextures.CrawlBoardsMat, LodgeTextures.DarkWoodMat, occlude: false);
		// a picture rail and a skirting round it, as the room's walls have
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Box(new Vector3((x0 + x1) * 0.5f, 1.32f, z1 + 0.04f), new Vector3(x1 - x0 + 0.1f, 0.05f, 0.04f), 1f);
		// over the slit, inside, the board the cell's own wall stops short of
		k.Mat(LodgeTextures.CrawlBoardsMat);
		k.Quad(new Vector3(slit.X + 0.5f, 1.25f, z1 - 0.07f), new Vector3(slit.X - 0.5f, 1.25f, z1 - 0.07f), new Vector3(slit.X - 0.5f, FloorY + CrawlH, z1 - 0.07f), new Vector3(slit.X + 0.5f, FloorY + CrawlH, z1 - 0.07f), Vector3.Forward);
		k.CommitTo(this, "ExitChase", true);
		// the wardrobe in front of the slit: its pivot on its front right foot (pushed at its left, it goes over forward and
		// twists as it falls, landing face down along the wall, clear of the slit)
		Wardrobe = new Node3D { Name = "SlitWardrobe", Position = new Vector3(slit.X + 0.6f, FloorY, z1 + 0.62f) };
		AddChild(Wardrobe);
		var wk = new MeshKit();
		wk.Mat(LodgeTextures.DarkWoodMat); wk.Color = Colors.White;
		wk.Box(new Vector3(-0.6f, 1.05f, -0.31f), new Vector3(1.2f, 2.1f, 0.6f), 1f);
		wk.Mat(LodgeTextures.GoldMat);
		wk.Box(new Vector3(-0.6f, 2.12f, -0.31f), new Vector3(1.26f, 0.05f, 0.64f), 1f);
		wk.Box(new Vector3(-0.68f, 1.1f, 0.0f), new Vector3(0.02f, 0.12f, 0.02f), 1f);
		wk.Box(new Vector3(-0.52f, 1.1f, 0.0f), new Vector3(0.02f, 0.12f, 0.02f), 1f);
		wk.Mat(LodgeTextures.BlackMat);
		wk.Box(new Vector3(-0.6f, 1.05f, 0.002f), new Vector3(0.012f, 1.9f, 0.004f), 1f);
		wk.CommitTo(Wardrobe, "Mesh", true);
		var wb = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		wb.SetMeta("surface", "wood");
		wb.AddChild(new CollisionShape3D { Position = new Vector3(-0.6f, 1.05f, -0.31f), Shape = new BoxShape3D { Size = new Vector3(1.2f, 2.1f, 0.6f) } });
		Wardrobe.AddChild(wb);
		// pushed from behind, through the slit
		WardrobeUse = new PickupInteractable
		{
			Name = "WardrobeUse", PickRadius = 0.45f, MaxDistance = 1.8f, Position = new Vector3(slit.X, 0.7f, z1 - 0.1f),
			PromptFor = _ => WardrobeDown ? "" : "Push it over", CanUse = _ => !WardrobeDown,
		};
		WardrobeUse.Interacted += p => { if (!WardrobeDown) _ = Cutscene.Run(this, ct => PushWardrobe(p, ct), lockInput: true); };
		AddChild(WardrobeUse);
	}

	private async Task PushWardrobe(PlayerController player, CancellationToken ct)
	{
		// two shoves: it rocks, then it goes
		var home = player.GlobalPosition;
		for (int i = 0; i < 2; i++)
		{
			var tw = player.CreateTween();
			tw.TweenProperty(player, "global_position", home + GlobalBasis * Vector3.Back * 0.14f, 0.14f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tw.TweenProperty(player, "global_position", home, 0.35f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			AudioDirector.OneShot(this, "body_thump", 2, Wardrobe.GlobalPosition + Vector3.Up, -6f + i * 3f);
			var rock = CreateTween();
			rock.TweenProperty(Wardrobe, "rotation:x", 0.06f * (i + 1), 0.15f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			if (i == 0) rock.TweenProperty(Wardrobe, "rotation:x", 0f, 0.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
			await Cutscene.Tween(this, tw, ct);
			await Cutscene.Wait(this, 0.25, ct);
		}
		KnockWardrobe(instant: false);
		await Cutscene.Wait(this, 1.1, ct);
	}

	/// <summary>Over it goes: forward onto its face with a crash and a cloud of dust, the slit open behind it.</summary>
	private void KnockWardrobe(bool instant)
	{
		if (WardrobeDown && !instant) return;
		WardrobeDown = true;
		StoryManager.Instance?.SetFlag(LodgeFlag.WardrobeDown);
		if (WardrobeUse != null) WardrobeUse.Enabled = false;
		const float down = Mathf.Pi * 0.5f - 0.02f;
		// forward onto its face, twisting a quarter turn about the right foot as it goes (so it lands along the wall)
		void Fall(float u) { if (IsInstanceValid(Wardrobe)) Wardrobe.Basis = new Basis(Vector3.Up, Mathf.Pi * 0.5f * Mathf.SmoothStep(0.15f, 1f, Mathf.Min(u, 1f))) * new Basis(Vector3.Right, down * u); }
		if (instant) { Fall(1f); return; }
		var tw = CreateTween();
		tw.TweenMethod(Callable.From<float>(Fall), Wardrobe.Rotation.X / down, 1f, 0.8f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenCallback(Callable.From(() =>
		{
			AudioDirector.OneShot(this, "wardrobe_fall", 1, Wardrobe.GlobalPosition + Vector3.Up * 0.3f, 6f, "Events", 6f, 0.03f);
			DustBurst(Wardrobe.GlobalPosition + GlobalBasis * new Vector3(1.05f, 0.2f, 0.6f), 60);
		}));
		// a small bounce as it lands
		tw.TweenMethod(Callable.From<float>(Fall), 1f, 0.97f, 0.08f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tw.TweenMethod(Callable.From<float>(Fall), 0.97f, 1f, 0.1f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		GD.Print("[story] Act 23: the wardrobe over, the slit behind it: out into the dining hall");
	}

	private void DustBurst(Vector3 at, int n)
	{
		var p = new GpuParticles3D
		{
			Name = "DustBurst", Amount = n, Lifetime = 2.2f, OneShot = true, Explosiveness = 0.9f, Emitting = true, Position = ToLocal(at),
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.6f, 0.15f, 0.8f),
				Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.8f, Gravity = new Vector3(0, -0.2f, 0),
				ScaleMin = 1.5f, ScaleMax = 4f, Color = new Color(0.4f, 0.37f, 0.33f, 0.18f),
			},
			DrawPass1 = new QuadMesh { Size = new Vector2(0.16f, 0.16f), Material = new StandardMaterial3D { AlbedoTexture = LakeParts.LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(p);
		GetTree().CreateTimer(3.0).Timeout += () => { if (IsInstanceValid(p)) p.QueueFree(); };
	}

	// ------------------------------------------------------------------ every frame (from _Process)

	private bool _crawlSlowed;

	private void CrawlProcess(PlayerController player, Vector3 l, float dt)
	{
		if (_maze.Count == 0) return;
		// the two hand-overs
		if (_crawlPrevSet)
		{
			var p = _crawlPrev;
			float inX = CellCentre(new Vector2I(PortalInI, 0)).X, outZ = CellCentre(new Vector2I(0, PortalOutJ)).Z;
			bool inCol = Mathf.Abs(l.X - inX) < 0.5f, outRow = Mathf.Abs(l.Z - outZ) < 0.5f;
			if (inCol && Mathf.Abs(l.Y - UpperY) < 1.5f && p.Z > PortalInZ && l.Z <= PortalInZ) { Shift(player, CrawlDown); EnteredMaze(player); }
			else if (inCol && Mathf.Abs(l.Y - (UpperY + CrawlDown.Y)) < 1.5f && p.Z < PortalInZ && l.Z >= PortalInZ) Shift(player, -CrawlDown);
			else if (outRow && Mathf.Abs(l.Y - (FloorY + CrawlDown.Y)) < 1.5f && p.X > PortalOutX && l.X <= PortalOutX) Shift(player, -CrawlDown);
			else if (outRow && Mathf.Abs(l.Y - FloorY) < 1.5f && p.X < PortalOutX && l.X >= PortalOutX) Shift(player, CrawlDown);
			l = ToLocal(player.GlobalPosition);
		}
		_crawlPrev = l;
		_crawlPrevSet = true;
		var cell = new Vector2I(Mathf.RoundToInt(l.X - CrawlOX), Mathf.RoundToInt(l.Z - CrawlOZ));
		// (in its footprint, not just that deep: Act 15's hallway is far below the lodge too, and was read as the maze,
		// its pace slowed to the crawlspace's)
		InMaze = l.Y < -40f && l.Y > -80f && _maze.ContainsKey(cell);
		bool realEnd = !InMaze && ((l.Y > UpperY - 0.5f && System.Array.IndexOf(EntryCells, cell) >= 0) || (l.Y < 2f && System.Array.IndexOf(ExitCells, cell) >= 0));
		InCrawlspace = InMaze || realEnd;
		var atmo = StoryBeat.Atmosphere(this);
		if (atmo != null) atmo.Interior = Mathf.MoveToward(atmo.Interior, InCrawlspace ? 0.85f : 0f, dt * 1.5f);
		// cramped: a body goes slower between the walls
		if (InCrawlspace || _crawlSlowed)
		{
			player.WadeScale = Mathf.MoveToward(player.WadeScale, InCrawlspace ? 0.75f : 1f, dt * 2f);
			_crawlSlowed = InCrawlspace || player.WadeScale < 0.999f;
		}
		if (_motes != null)
		{
			_motes.Emitting = InCrawlspace;
			if (InCrawlspace) _motes.GlobalPosition = player.GlobalPosition + Vector3.Up * 1.2f;
		}
		// out of the wall, into the frozen dining hall: a save
		if (!_frozenSaved && WardrobeDown && !InCrawlspace && l.X > 25f && l.Z > -4.3f && l.Y < 2f && l.Y > -1f)
		{
			_frozenSaved = true;
			GD.Print("[story] Act 23: out of the wall into the dining hall - frozen over");
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act23Frozen);
		}
		if (!InMaze) return;
		// dust off the walls as they brush along
		if (player.GroundSpeed > 0.4f && _clock >= _flakeAt)
		{
			_flakeAt = _clock + GD.RandRange(1.1, 2.6);
			var cam = player.CameraRig.Camera;
			Vector3 fwd = (-cam.GlobalBasis.Z) with { Y = 0 };
			Vector3 side = fwd.Cross(Vector3.Up).Normalized() * (GD.Randf() < 0.5f ? 1f : -1f);
			var f = _flakes[_flakeNext++ % _flakes.Length];
			f.GlobalPosition = player.GlobalPosition + fwd.Normalized() * 0.5f + side * 0.35f + Vector3.Up * 1.9f;
			f.Restart();
			DustFalls++;
			AudioDirector.OneShot(this, "dust_trickle", 3, f.GlobalPosition, -16f, "Events", 2f, 0.1f);
		}
		// it howls, somewhere in the walls; more often once its arms have come through
		if (_clock >= _howlAt)
		{
			bool first = _howlAt <= 0.0;
			_howlAt = _clock + (ArmsBurst > 0 ? GD.RandRange(10.0, 17.0) : GD.RandRange(16.0, 28.0));
			if (!first)
			{
				var d = new Vector3((float)GD.RandRange(-1.0, 1.0), 0, (float)GD.RandRange(-1.0, 1.0)).Normalized() * (float)GD.RandRange(7.0, 12.0);
				AudioDirector.OneShot(this, "wendigo_howl_far", 3, player.GlobalPosition + d + Vector3.Up, ArmsBurst > 0 ? -2f : -6f, "Unnatural", 6f, 0.06f);
				Howls++;
			}
		}
		// the save, halfway
		if (!_crawlSaved && cell == CrawlSave)
		{
			_crawlSaved = true;
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act23Crawlspace);
		}
		// the arms
		for (int a = 0; a < Arms.Count; a++)
		{
			var arm = Arms[a];
			var (acell, _) = ArmSpots[a];
			var ac = CellCentre(acell) + CrawlDown;
			float dist = new Vector2(l.X - ac.X, l.Z - ac.Z).Length();
			if (arm.Phase == WendigoArm.State.Waiting && dist < 2.7f && (a == 0 || ArmsBurst >= a))
			{
				arm.Burst();
				ArmsBurst++;
				PlayerBreathing.Startle(1f);
				ArmHole(a);
				AudioDirector.OneShot(this, "wall_burst", 3, arm.GlobalPosition, 2.5f + a * 1.5f, "Events", 5f, 0.05f);
				AudioDirector.OneShot(this, $"wendigo_howl_{a + 1:00}", 1, arm.GlobalPosition + arm.GlobalBasis.X * -1.5f, -2f + a * 3f, "Unnatural", 7f, 0.02f);   // (each worse)
				DustBurst(arm.GlobalPosition + arm.GlobalBasis.X * 0.3f, 40);
				GD.Print($"[story] Act 23: the crawlspace - its arm through the wall ({a + 1} of 4)");
			}
			if (arm.Phase == WendigoArm.State.Reach)
			{
				_armClosest[a] = Mathf.Min(_armClosest[a], dist);
				if (_armClosest[a] < 0.8f && dist > 1.7f && !_armPassed[a]) { _armPassed[a] = true; arm.Withdraw(); ArmScars(a); }
			}
		}
	}

	/// <summary>Carried between the cavity's two places (the same step, the same view).</summary>
	private void Shift(PlayerController player, Vector3 d)
	{
		var w = GlobalBasis * d;
		player.GlobalPosition += w;
		player.CameraRig?.ShiftBy(w);
		if (_motes != null) _motes.GlobalPosition += w;
		CrawlShifts++;
	}

	private void EnteredMaze(PlayerController player)
	{
		if (!Has(LodgeFlag.InCrawlspace))
		{
			StoryManager.Instance?.SetFlag(LodgeFlag.InCrawlspace);
			GD.Print("[story] Act 23: into the wall - the crawlspace");
		}
		_howlAt = _clock + 6.0;
		// while they're in here, the lodge freezes over (SkiLodge.Frozen.cs)
		if (!Frozen && Has(LodgeFlag.Letter201)) Freeze(instant: false);
	}

	/// <summary>It had them: the hand at the face, a howl in the ear, and a shove back down the cavity.</summary>
	private void ArmGrab(int a)
	{
		var player = StoryBeat.Player(this);
		if (player == null) return;
		if (player.CrouchAmount > 0.6f) return;   // (under it: it's grabbing at air above them)
		ArmGrabs++;
		var arm = Arms[a];
		var away = (player.GlobalPosition - arm.GlobalPosition) with { Y = 0 };
		// back along the cavity, the way they came (along its axis, away from the arm's line)
		var axis = Mathf.Abs(arm.GlobalBasis.X.X) > 0.5f ? new Vector3(0, 0, Mathf.Sign(away.Z == 0 ? 1 : away.Z)) : new Vector3(Mathf.Sign(away.X == 0 ? 1 : away.X), 0, 0);
		var tw = player.CreateTween();
		tw.TweenProperty(player, "global_position", player.GlobalPosition + axis * 0.8f, 0.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		AudioDirector.OneShot(this, $"wendigo_howl_{a + 1:00}", 1, arm.GlobalPosition, 1.5f + a * 2f, "Unnatural", 5f, 0.04f);
		AudioDirector.OneShot(this, "body_thump", 2, player.GlobalPosition + Vector3.Up, -4f);
		_ = StoryBeat.Caption(this, "Get down.", 0.2f, 1.2f, 0.6f);
	}

	/// <summary>The boards burst where it comes through: a black hole with the boards splintered in round it.</summary>
	private void ArmHole(int a)
	{
		var arm = Arms[a];
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(LodgeTextures.BlackMat);
		var into = arm.GlobalBasis.X;
		var up = Vector3.Up;
		var along = into.Cross(up).Normalized();
		Vector3 c = ToLocal(arm.GlobalPosition) + (GlobalBasis.Inverse() * into) * 0.003f;
		var lInto = GlobalBasis.Inverse() * into;
		var lAlong = GlobalBasis.Inverse() * along;
		var rng = new RandomNumberGenerator { Seed = (ulong)(4000 + a) };
		int n = 11;
		var ring = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			float ang = Mathf.Tau * i / n, r = rng.RandfRange(0.17f, 0.3f);
			ring[i] = c + lAlong * Mathf.Cos(ang) * r + Vector3.Up * Mathf.Sin(ang) * r * 1.2f;
		}
		for (int i = 0; i < n; i++) k.Tri(c, ring[i], ring[(i + 1) % n], lInto, Vector2.Zero, Vector2.Right, Vector2.Down);
		// splinters bent out into the cavity round it
		k.Mat(BuildingTextures.FreshPlankMat);
		for (int i = 0; i < 9; i++)
		{
			var p = ring[rng.RandiRange(0, n - 1)];
			var tip = p + lInto * rng.RandfRange(0.08f, 0.22f) + (p - c).Normalized() * rng.RandfRange(0.02f, 0.1f);
			k.Beam(p, tip, 0.025f, 0.012f);
		}
		k.CommitTo(this, $"ArmHole{a}", false);
	}

	/// <summary>Where it pulled back through: blood smeared down the boards under the hole and a rime of frost round
	/// it (it tears itself on the boards; more each time).</summary>
	private void ArmScars(int a)
	{
		var arm = Arms[a];
		var k = new MeshKit();
		k.Color = Colors.White;
		var lInto = GlobalBasis.Inverse() * arm.GlobalBasis.X;
		var lAlong = GlobalBasis.Inverse() * arm.GlobalBasis.X.Cross(Vector3.Up).Normalized();
		Vector3 c = ToLocal(arm.GlobalPosition) + lInto * 0.006f;
		var rng = new RandomNumberGenerator { Seed = (ulong)(4100 + a) };
		k.Mat(StationParts.StationTextures.BloodPoolMat);
		for (int i = 0; i < 3 + a * 2; i++)
		{
			float x = rng.RandfRange(-0.18f, 0.18f), len = rng.RandfRange(0.3f, 0.6f + 0.25f * a);
			Vector3 top = c + lAlong * x + Vector3.Down * 0.15f, bot = top + Vector3.Down * len;
			float w = rng.RandfRange(0.012f, 0.03f + 0.01f * a);
			k.Quad(top - lAlong * w, top + lAlong * w, bot + lAlong * w * 0.4f, bot - lAlong * w * 0.4f, lInto);
		}
		k.Mat(WinterWoods.IceMat);
		for (int i = 0; i < 12; i++)
		{
			float ang = Mathf.Tau * i / 12f;
			var p = c + lAlong * Mathf.Cos(ang) * 0.3f + Vector3.Up * Mathf.Sin(ang) * 0.36f + lInto * 0.004f;
			k.Blob(p, new Vector3(0.05f, 0.05f, 0.05f), 4200 + a * 20 + i, 0.4f, false, 1f);
		}
		k.CommitTo(this, $"ArmScars{a}", false);
	}
}
