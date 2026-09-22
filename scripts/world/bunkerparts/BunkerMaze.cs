using System.Collections.Generic;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// Act 10's maze: what the hallway has become. A 6 x 10 grid of 4 m cells
/// (the story's fixed seed, so it is always the same maze) of low concrete
/// corridors with skirting, cracks, stains and weeping ooze, a few caged lamps
/// whose deep colours drift slowly (never strobing) and sometimes falter,
/// walls that barely breathe, and a handful of landmarks so it can be
/// navigated. Now and then a figure (the stalker's body) is glimpsed for an
/// instant at the end of a corridor. The last cell is the inside of the
/// bunker's own round front door.
/// Built at level load at its own far-off offset and left hidden in plain sight
/// until the flow teleports the player in.
/// </summary>
public partial class BunkerMaze : Node3D
{
	private class Fixture
	{
		public StandardMaterial3D Mat;
		public OmniLight3D Light;
		public Color A, B;
		public float Period, Phase, Base;
		public bool Falters;
	}

	private bool[,] _passRight, _passDown;
	private Vector2I _exitCell;
	private readonly List<Vector2I> _deadEnds = new();
	private readonly List<Fixture> _fixtures = new();
	private StalkerBody _creature;
	private RandomNumberGenerator _glimpseRng;
	private double _nextGlimpse;
	private Area3D _exitTrigger;
	private StaticBody3D _body;

	/// <summary>Glimpses and lamp drift run while true.</summary>
	public bool Active { get; set; }
	public bool Exited { get; private set; }
	/// <summary>The solution path, start to exit, as world points (the autotest walks it).</summary>
	public List<Vector3> SolutionWaypointsWorld { get; private set; }
	/// <summary>Where the player arrives (the first cell), interior-local.</summary>
	public static Vector3 StartLocal => MazeOffset + new Vector3(0, 0, 0.5f);
	/// <summary>The exit cell's centre, interior-local (the bunker's own front door, from inside).</summary>
	public static Vector3 ExitLocal => MazeOffset + new Vector3((MazeCols - 1) * MazeCell, 0, -(MazeRows - 1) * MazeCell);
	/// <summary>Where the dropped walkie-talkie lies, interior-local.</summary>
	public static Vector3 WalkieLocal => ExitLocal + new Vector3(1.2f, 0f, -1.5f);

	/// <summary>Raised once, the first time the player reaches the exit cell.</summary>
	public event System.Action ExitReached;

	public override void _Ready()
	{
		Position = MazeOffset;
		GenerateData();
		_body = new StaticBody3D { Name = "MazeBody", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "stone");
		AddChild(_body);
		BuildShell();
		BuildDamage();
		BuildFixtures();
		BuildLandmarks();
		BuildExitDoor();

		_creature = new StalkerBody { Name = "Creature", Seed = 2077, Size = 1.0f, Visible = false };
		AddChild(_creature);
		_glimpseRng = new RandomNumberGenerator { Seed = 1010 };

		_exitTrigger = StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(MazeCell * 0.8f, 2f, MazeCell * 0.8f) }, ExitLocal - MazeOffset, _ =>
		{
			if (Exited || !Active) return;
			Exited = true;
			Active = false;
			ExitReached?.Invoke();
		}, "ExitTrigger");
	}

	/// <summary>Restore: the exit has already been reached.</summary>
	public void MarkExited() { Exited = true; Active = false; }

	// ------------------------------------------------------------------ the maze itself (story: unchanged)

	private void GenerateData()
	{
		_passRight = new bool[MazeCols, MazeRows];
		_passDown = new bool[MazeCols, MazeRows];
		var visited = new bool[MazeCols, MazeRows];
		var parent = new Dictionary<Vector2I, Vector2I>();
		var rng = new RandomNumberGenerator { Seed = MazeSeed };
		var stack = new Stack<Vector2I>();
		var start = new Vector2I(0, 0);
		visited[0, 0] = true;
		stack.Push(start);
		while (stack.Count > 0)
		{
			var cur = stack.Peek();
			var options = new List<(Vector2I next, int dir)>();
			void Try(int dx, int dy, int dir) { var n = new Vector2I(cur.X + dx, cur.Y + dy); if (n.X >= 0 && n.X < MazeCols && n.Y >= 0 && n.Y < MazeRows && !visited[n.X, n.Y]) options.Add((n, dir)); }
			Try(1, 0, 0); Try(-1, 0, 1); Try(0, 1, 2); Try(0, -1, 3);
			if (options.Count == 0) { stack.Pop(); continue; }
			var (next, dir) = options[rng.RandiRange(0, options.Count - 1)];
			if (dir == 0) _passRight[cur.X, cur.Y] = true;
			else if (dir == 1) _passRight[next.X, next.Y] = true;
			else if (dir == 2) _passDown[cur.X, cur.Y] = true;
			else _passDown[next.X, next.Y] = true;
			visited[next.X, next.Y] = true;
			parent[next] = cur;
			stack.Push(next);
		}

		_exitCell = new Vector2I(MazeCols - 1, MazeRows - 1);
		var path = new List<Vector2I> { _exitCell };
		var walk = _exitCell;
		while (walk != start) { walk = parent[walk]; path.Add(walk); }
		path.Reverse();
		SolutionWaypointsWorld = new List<Vector3>();
		foreach (var cell in path)
			SolutionWaypointsWorld.Add(ToGlobal(new Vector3(cell.X * MazeCell, 0.1f, -cell.Y * MazeCell)));

		for (int c = 0; c < MazeCols; c++)
			for (int r = 0; r < MazeRows; r++)
			{
				var cell = new Vector2I(c, r);
				if (cell == start || cell == _exitCell) continue;
				if (Openings(c, r) == 1) _deadEnds.Add(cell);
			}
	}

	private bool OpenE(int c, int r) => c < MazeCols - 1 && _passRight[c, r];
	private bool OpenW(int c, int r) => c > 0 && _passRight[c - 1, r];
	private bool OpenS(int c, int r) => r < MazeRows - 1 && _passDown[c, r];     // toward -Z
	private bool OpenN(int c, int r) => r > 0 && _passDown[c, r - 1];            // toward +Z
	private int Openings(int c, int r) => (OpenE(c, r) ? 1 : 0) + (OpenW(c, r) ? 1 : 0) + (OpenS(c, r) ? 1 : 0) + (OpenN(c, r) ? 1 : 0);
	private static Vector3 CellCenter(Vector2I c) => new(c.X * MazeCell, 0, -c.Y * MazeCell);

	// ------------------------------------------------------------------ per frame

	/// <summary>Drives the lamps and the glimpses. playerLocal is relative to the interior node.</summary>
	public void Tick(double clock, Vector3? playerLocal, Node3D player)
	{
		AnimateFixtures(clock);
		if (!Active || clock < _nextGlimpse) return;
		_nextGlimpse = clock + _glimpseRng.RandfRange(8f, 18f);
		if (playerLocal is not { } pl || player == null) return;
		Vector3 local = pl - MazeOffset;
		int cc = Mathf.Clamp(Mathf.RoundToInt(local.X / MazeCell) + _glimpseRng.RandiRange(-2, 2), 0, MazeCols - 1);
		int rr = Mathf.Clamp(Mathf.RoundToInt(-local.Z / MazeCell) + _glimpseRng.RandiRange(1, 3), 0, MazeRows - 1);
		ShowGlimpse(new Vector3(cc * MazeCell, 0, -rr * MazeCell), player.GlobalPosition);
	}

	/// <summary>The figure stands at <paramref name="mazeLocal"/> for an instant, facing the player, with the sting.</summary>
	public void ShowGlimpse(Vector3 mazeLocal, Vector3 lookAtWorld)
	{
		_creature.Position = mazeLocal;
		Vector3 to = ToLocal(lookAtWorld) - mazeLocal;
		_creature.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);
		_creature.Visible = true;
		BunkerKit.OneShot(this, "res://assets/audio/sfx/stalker_seen_01.wav", mazeLocal + Vector3.Up * 1.6f, "Unnatural", -6f, 1f, 4f, 30f);
		_ = Systems.Cutscene.Run(this, async ct =>
		{
			await Systems.Cutscene.Wait(this, 0.6, ct);
			_creature.Visible = false;
		});
	}

	private void AnimateFixtures(double clock)
	{
		float t = (float)clock;
		foreach (var f in _fixtures)
		{
			// Slow drift between two deep colours: a half-minute cycle, never a flash.
			float u = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Tau / f.Period + f.Phase);
			Color c = f.A.Lerp(f.B, u);
			float e = f.Base;
			if (f.Falters)
			{
				// Now and then the lamp sags for a moment (a slow dip, not a strobe).
				float s = Mathf.Sin(t * 0.37f + f.Phase) * Mathf.Sin(t * 0.23f + f.Phase * 1.7f);
				if (s > 0.8f) e *= Mathf.Lerp(1f, 0.2f, Mathf.SmoothStep(0.8f, 0.95f, s));
			}
			f.Mat.Emission = c;
			f.Mat.AlbedoColor = c;
			f.Mat.EmissionEnergyMultiplier = 1.4f * e / f.Base;
			f.Light.LightColor = c;
			f.Light.LightEnergy = e;
		}
	}

	// ------------------------------------------------------------------ building

	private void AddBox(Vector3 center, Vector3 size) => _body.AddChild(new CollisionShape3D { Position = center, Shape = new BoxShape3D { Size = size } });

	private void BuildShell()
	{
		var walls = new MeshKit();
		walls.Mat(BunkerTextures.MazeWallMat);
		var skirt = new MeshKit();
		skirt.Mat(BunkerTextures.MazeWallMat);
		var rng = new RandomNumberGenerator { Seed = 5150 };
		float half = MazeCell * 0.5f, th = MazeWallThickness;

		void Wall(Vector3 mid, bool alongZ, float len)
		{
			var size = alongZ ? new Vector3(th, MazeWallHeight, len) : new Vector3(len, MazeWallHeight, th);
			walls.Color = new Color(1f, 1f, 1f) * rng.RandfRange(0.88f, 1.04f);
			walls.Box(mid + Vector3.Up * MazeWallHeight * 0.5f, size);
			// Skirting: a darker plinth standing 3 cm proud of both faces, and a cap band at the top.
			skirt.Color = new Color(0.55f, 0.54f, 0.52f);
			skirt.Box(mid + Vector3.Up * 0.09f, alongZ ? new Vector3(th + 0.06f, 0.18f, len) : new Vector3(len, 0.18f, th + 0.06f));
			skirt.Color = new Color(0.7f, 0.69f, 0.67f);
			skirt.Box(mid + Vector3.Up * (MazeWallHeight - 0.08f), alongZ ? new Vector3(th + 0.04f, 0.1f, len) : new Vector3(len, 0.1f, th + 0.04f));
			AddBox(mid + Vector3.Up * MazeWallHeight * 0.5f, size);
		}

		// Exact-length wall segments plus separate corner posts: overlapping convex shapes meeting at
		// every corner made MoveAndSlide's resolution erratic against this many walls.
		for (int c = 0; c < MazeCols; c++)
			for (int r = 0; r < MazeRows; r++)
			{
				float cx = c * MazeCell, cz = -r * MazeCell;
				if (c == MazeCols - 1 || !_passRight[c, r]) Wall(new Vector3(cx + half, 0, cz), true, MazeCell - th);
				if (r == MazeRows - 1 || !_passDown[c, r]) Wall(new Vector3(cx, 0, cz - half), false, MazeCell - th);
				if (c == 0) Wall(new Vector3(cx - half, 0, cz), true, MazeCell - th);
				if (r == 0) Wall(new Vector3(cx, 0, cz + half), false, MazeCell - th);
			}
		for (int c = 0; c <= MazeCols; c++)
			for (int r = 0; r <= MazeRows; r++)
			{
				var p = new Vector3(c * MazeCell - half, 0, -r * MazeCell + half);
				walls.Color = new Color(0.9f, 0.9f, 0.88f);
				walls.Box(p + Vector3.Up * MazeWallHeight * 0.5f, new Vector3(th + 0.04f, MazeWallHeight, th + 0.04f));
				AddBox(p + Vector3.Up * MazeWallHeight * 0.5f, new Vector3(th, MazeWallHeight, th));
			}
		walls.CommitTo(this, "MazeWalls");
		skirt.CommitTo(this, "MazeSkirting");

		// Floor and a solid ceiling over the whole footprint: closed overhead throughout.
		Vector3 centre = new((MazeCols - 1) * half, 0, -(MazeRows - 1) * half);
		Vector3 extent = new(MazeCols * MazeCell, 0.1f, MazeRows * MazeCell);
		var fl = new MeshKit();
		fl.Mat(BunkerTextures.MazeFloorMat);
		fl.Color = Colors.White;
		fl.Box(centre + Vector3.Down * 0.05f, extent);
		fl.Mat(BunkerTextures.MazeWallMat);
		fl.Color = new Color(0.6f, 0.58f, 0.57f);
		fl.Box(centre + Vector3.Up * (MazeWallHeight + 0.05f), extent);
		fl.CommitTo(this, "MazeFloorCeiling");
		AddBox(centre + Vector3.Down * 0.05f, extent);
		AddBox(centre + Vector3.Up * (MazeWallHeight + 0.05f), extent);

		// A few pipe runs along the ceiling, following open corridors.
		var pipes = new MeshKit();
		pipes.Mat(ProcTextures.MetalMat);
		pipes.Color = new Color(0.55f, 0.55f, 0.5f);
		for (int r = 0; r < MazeRows; r += 3)
			for (int c = 0; c < MazeCols - 1; c++)
				if (_passRight[c, r])
				{
					float z = -r * MazeCell + 0.9f;
					pipes.Cylinder(new Vector3(c * MazeCell - 0.2f, MazeWallHeight - 0.18f, z), new Vector3((c + 1) * MazeCell + 0.2f, MazeWallHeight - 0.18f, z), 0.05f, 0.05f, 6, false);
				}
		pipes.CommitTo(this, "MazePipes");
	}

	/// <summary>Stains, cracks, ooze, chipped corners and exposed rebar.</summary>
	private void BuildDamage()
	{
		var rng = new RandomNumberGenerator { Seed = 5151 };
		float half = MazeCell * 0.5f;
		// Wall faces we can decorate: (point on the face, outward normal).
		var faces = new List<(Vector3 p, Vector3 n)>();
		for (int c = 0; c < MazeCols; c++)
			for (int r = 0; r < MazeRows; r++)
			{
				var cc = CellCenter(new Vector2I(c, r));
				float inset = half - MazeWallThickness * 0.5f;
				if (!OpenE(c, r)) faces.Add((cc + new Vector3(inset, 0, 0), Vector3.Left));
				if (!OpenW(c, r)) faces.Add((cc + new Vector3(-inset, 0, 0), Vector3.Right));
				if (!OpenS(c, r)) faces.Add((cc + new Vector3(0, 0, -inset), Vector3.Back));
				if (!OpenN(c, r)) faces.Add((cc + new Vector3(0, 0, inset), Vector3.Forward));
			}
		Vector3 Along(Vector3 n) => new(n.Z, 0, -n.X);

		for (int i = 0; i < 48; i++)
		{
			var (p, n) = faces[rng.RandiRange(0, faces.Count - 1)];
			Vector3 at = p + Along(n) * rng.RandfRange(-1.2f, 1.2f);
			int kind = rng.RandiRange(0, 3);
			var tex = kind switch { 0 => BunkerTextures.Streak(), 1 => BunkerTextures.Cracks(), 2 => BunkerTextures.Blotch(), _ => BunkerTextures.RustRun() };
			float y = kind == 1 ? rng.RandfRange(0.6f, 2.0f) : MazeWallHeight - 1.1f;
			var size = kind == 1 ? new Vector2(1.2f, 1.2f) : new Vector2(rng.RandfRange(0.6f, 1.2f), 2.2f);
			BunkerKit.AddDecal(this, tex, at + Vector3.Up * y, n, Vector3.Down, size, 0.4f, new Color(1, 1, 1, rng.RandfRange(0.7f, 1f)));
		}
		for (int i = 0; i < 24; i++)
		{
			var cell = new Vector2I(rng.RandiRange(0, MazeCols - 1), rng.RandiRange(0, MazeRows - 1));
			var at = CellCenter(cell) + new Vector3(rng.RandfRange(-1.4f, 1.4f), 0f, rng.RandfRange(-1.4f, 1.4f));
			bool wet = rng.Randf() < 0.5f;
			BunkerKit.AddDecal(this, wet ? BunkerTextures.Puddle() : BunkerTextures.Cracks(), at, Vector3.Up, Vector3.Forward,
				new Vector2(rng.RandfRange(1f, 2f), rng.RandfRange(1f, 2f)), 0.3f, new Color(1, 1, 1, 0.9f), wet ? BunkerTextures.PuddleOrm() : null);
		}

		// Ooze weeping from the ceiling seam down a handful of faces, and chipped damage at the skirting.
		var k = new MeshKit();
		var ooze = (StandardMaterial3D)ProcTextures.Cached("bk_m_ooze", () => new StandardMaterial3D
		{
			AlbedoColor = new Color(0.05f, 0.08f, 0.04f), Roughness = 0.12f, MetallicSpecular = 0.7f, VertexColorUseAsAlbedo = true,
		});
		for (int i = 0; i < 20; i++)
		{
			var (p, n) = faces[rng.RandiRange(0, faces.Count - 1)];
			Vector3 at = p + Along(n) * rng.RandfRange(-1.3f, 1.3f) + n * (0.012f);
			float drop = rng.RandfRange(0.6f, MazeWallHeight - 0.4f);
			k.Mat(ooze);
			k.Color = new Color(1f, 1f, 1f) * rng.RandfRange(0.7f, 1.2f);
			var pts = new List<Vector3>();
			for (int s = 0; s <= 5; s++)
				pts.Add(at + Vector3.Up * (MazeWallHeight - 0.1f - drop * s / 5f) + Along(n) * Mathf.Sin(s * 1.3f + i) * 0.03f);
			BunkerKit.Tube(k, pts, 0.03f, 0.012f, 4);
			k.Blob(pts[^1] + Vector3.Down * 0.02f, new Vector3(0.03f, 0.04f, 0.03f), 300 + i, 0.2f);
		}
		k.Mat(BunkerTextures.MazeWallMat);
		for (int i = 0; i < 18; i++)
		{
			var (p, n) = faces[rng.RandiRange(0, faces.Count - 1)];
			Vector3 at = p + Along(n) * rng.RandfRange(-1.5f, 1.5f) + n * 0.05f;
			k.Color = new Color(0.62f, 0.6f, 0.57f);
			k.Blob(at + Vector3.Up * 0.05f + n * rng.RandfRange(0.05f, 0.3f), new Vector3(0.09f, 0.06f, 0.08f) * rng.RandfRange(0.7f, 1.6f), 400 + i, 0.3f);
			if (i % 3 == 0)
			{
				// A broken patch with rebar poking out.
				k.Mat(ProcTextures.MetalMat);
				k.Color = new Color(0.45f, 0.3f, 0.2f);
				for (int b = 0; b < 3; b++)
				{
					Vector3 r0 = at + Vector3.Up * (0.9f + b * 0.14f) - n * 0.08f;
					k.Cylinder(r0, r0 + n * rng.RandfRange(0.12f, 0.3f) + Vector3.Down * rng.RandfRange(0f, 0.12f), 0.008f, 0.008f, 4, false);
				}
				BunkerKit.AddDecal(this, BunkerTextures.Blotch(), at + Vector3.Up * 1.05f - n * 0.05f, n, Vector3.Down, new Vector2(0.7f, 0.6f), 0.3f, new Color(1, 1, 1, 1));
				k.Mat(BunkerTextures.MazeWallMat);
			}
		}
		k.CommitTo(this, "MazeDamage");
	}

	private void BuildFixtures()
	{
		// Deep colours the lamps drift between; each lamp has its own pair and period.
		Color[] palette =
		{
			new(0.75f, 0.06f, 0.04f),   // deep red
			new(0.3f, 0.55f, 0.18f),    // sickly green
			new(0.15f, 0.25f, 0.7f),    // cold blue
			new(0.8f, 0.45f, 0.1f),     // amber
			new(0.45f, 0.1f, 0.5f),     // bruise violet
		};
		var rng = new RandomNumberGenerator { Seed = 5152 };
		var metal = new MeshKit();
		metal.Mat(ProcTextures.MetalMat);
		var used = new HashSet<Vector2I>();
		int placed = 0, guard = 0;
		while (placed < 10 && guard++ < 200)
		{
			var cell = new Vector2I(rng.RandiRange(0, MazeCols - 1), rng.RandiRange(0, MazeRows - 1));
			if (used.Contains(cell) || (placed < 2 && cell == new Vector2I(0, 0))) continue;
			used.Add(cell);
			var cc = CellCenter(cell);
			var xf = new Transform3D(Basis.FromEuler(new Vector3(0, rng.RandfRange(0, Mathf.Tau), 0)), cc + Vector3.Up * MazeWallHeight);
			BunkerKit.CagedLamp(metal, xf);
			int a = rng.RandiRange(0, palette.Length - 1), b = (a + rng.RandiRange(1, palette.Length - 1)) % palette.Length;
			var mat = BunkerTextures.NewLampGlass(palette[a], 1.4f);
			BunkerKit.LampGlass(this, xf, mat, $"FixtureGlass{placed}");
			var light = new OmniLight3D
			{
				Name = $"Fixture{placed}", LightColor = palette[a], LightEnergy = 1.5f, OmniRange = 7f, OmniAttenuation = 1.1f,
				Position = cc + Vector3.Up * (MazeWallHeight - 0.5f),
			};
			AddChild(light);
			_fixtures.Add(new Fixture
			{
				Mat = mat, Light = light, A = palette[a], B = palette[b], Base = 1.5f,
				Period = rng.RandfRange(24f, 42f), Phase = rng.RandfRange(0, Mathf.Tau), Falters = rng.Randf() < 0.4f,
			});
			placed++;
		}
		metal.CommitTo(this, "Fixtures", false);

		// The old dim fill along the maze's long axis, kept low so the lamps carry the colour.
		for (int i = 0; i < 3; i++)
			AddChild(new OmniLight3D
			{
				Name = $"Fill{i}", LightColor = new Color(0.45f, 0.5f, 0.42f), LightEnergy = 0.7f, OmniRange = 9f,
				Position = new Vector3((MazeCols - 1) * MazeCell * 0.5f, 1.8f, -(MazeRows - 1) * MazeCell * 0.5f * (i / 2f)),
			});
	}

	/// <summary>Three dead ends get something memorable in them, so the maze can be learned.</summary>
	private void BuildLandmarks()
	{
		var rng = new RandomNumberGenerator { Seed = 5153 };
		var picks = new List<Vector2I>(_deadEnds);
		for (int i = picks.Count - 1; i > 0; i--) { int j = rng.RandiRange(0, i); (picks[i], picks[j]) = (picks[j], picks[i]); }
		var k = new MeshKit();
		for (int i = 0; i < Mathf.Min(3, picks.Count); i++)
		{
			var cell = picks[i];
			var cc = CellCenter(cell);
			// Face the dead end's back wall (away from its one opening).
			Vector3 open = OpenE(cell.X, cell.Y) ? Vector3.Right : OpenW(cell.X, cell.Y) ? Vector3.Left : OpenS(cell.X, cell.Y) ? Vector3.Forward : Vector3.Back;
			Vector3 back = -open;
			Vector3 side = new(back.Z, 0, -back.X);
			Vector3 wallPt = cc + back * 1.45f;
			float yaw = Mathf.Atan2(-back.X, -back.Z);
			switch (i)
			{
				case 0:
				{
					// A steel cabinet toppled onto its face, papers spilled.
					k.Mat(BunkerTextures.PaintedMetalMat);
					k.Color = new Color(0.75f, 0.78f, 0.74f);
					var c = wallPt - back * 0.5f + Vector3.Up * 0.25f;
					k.Box(c, new Vector3(0.9f, 0.5f, 1.8f), 1f, Basis.FromEuler(new Vector3(0, yaw + 0.3f, 0)));
					AddBox(c, new Vector3(1.2f, 0.5f, 1.2f));
					k.Mat(ProcTextures.PaperMat);
					for (int p = 0; p < 9; p++)
					{
						k.Color = new Color(0.8f, 0.78f, 0.7f) * rng.RandfRange(0.7f, 1f);
						var pp = c + new Vector3(rng.RandfRange(-1.2f, 1.2f), -0.24f, rng.RandfRange(-1.2f, 1.2f));
						k.Box(pp, new Vector3(0.21f, 0.003f, 0.29f), 1f, Basis.FromEuler(new Vector3(0, rng.RandfRange(0, Mathf.Tau), 0)));
					}
					break;
				}
				case 1:
				{
					// A heap of dead CRT husks, one still faintly glowing.
					var screens = new MeshKit();
					screens.Mat(BunkerTextures.VoidMat);
					for (int t = 0; t < 6; t++)
					{
						var spec = BunkerKit.RandomCrt(rng, rng.RandiRange(0, 2));
						float y = t < 3 ? 0f : 0.45f;
						var pos = wallPt - back * 0.4f + side * ((t % 3) - 1) * 0.7f + Vector3.Up * y;
						var xf = new Transform3D(Basis.FromEuler(new Vector3(rng.RandfRange(-0.2f, 0.1f), yaw + rng.RandfRange(-0.5f, 0.5f) + Mathf.Pi, rng.RandfRange(-0.15f, 0.15f))), pos);
						BunkerKit.Crt(k, screens, xf, spec, Colors.White);
					}
					screens.CommitTo(this, "HuskScreens", false);
					AddBox(wallPt - back * 0.4f + Vector3.Up * 0.45f, new Vector3(2.2f, 0.9f, 2.2f) * new Vector3(Mathf.Abs(side.X) + 0.4f * Mathf.Abs(back.X), 1f, Mathf.Abs(side.Z) + 0.4f * Mathf.Abs(back.Z)));
					break;
				}
				default:
				{
					// The ceiling has come down: a slab of concrete with rebar, and a black hole above.
					k.Mat(BunkerTextures.MazeWallMat);
					k.Color = new Color(0.8f, 0.78f, 0.75f);
					var c = cc + Vector3.Up * 0.3f;
					k.Box(c, new Vector3(1.6f, 0.22f, 1.3f), 1f, Basis.FromEuler(new Vector3(0.35f, yaw + 0.4f, 0.2f)));
					for (int b = 0; b < 8; b++)
						k.Blob(cc + new Vector3(rng.RandfRange(-1f, 1f), 0.08f, rng.RandfRange(-1f, 1f)), new Vector3(0.14f, 0.1f, 0.12f) * rng.RandfRange(0.6f, 1.3f), 500 + b, 0.3f);
					k.Mat(ProcTextures.MetalMat);
					k.Color = new Color(0.45f, 0.3f, 0.2f);
					for (int b = 0; b < 5; b++)
						k.Cylinder(c + new Vector3(rng.RandfRange(-0.6f, 0.6f), 0.1f, rng.RandfRange(-0.5f, 0.5f)), c + new Vector3(rng.RandfRange(-0.9f, 0.9f), rng.RandfRange(0.3f, 0.8f), rng.RandfRange(-0.8f, 0.8f)), 0.01f, 0.01f, 4, false);
					k.Mat(BunkerTextures.VoidMat);
					k.Box(cc + Vector3.Up * (MazeWallHeight - 0.02f), new Vector3(1.4f, 0.02f, 1.1f));
					AddBox(c, new Vector3(1.4f, 0.6f, 1.2f));
					break;
				}
			}
		}
		k.CommitTo(this, "Landmarks");

		// Drag marks smeared along the walls on the last stretch before the exit.
		var smear = BunkerTextures.Smear();
		var ex = CellCenter(_exitCell);
		BunkerKit.AddDecal(this, smear, ex + new Vector3(-MazeCell * 0.5f + 0.1f, 1.2f, 0.5f), Vector3.Right, Vector3.Down, new Vector2(1.6f, 1.4f), 0.4f, new Color(1, 1, 1, 0.9f));
		BunkerKit.AddDecal(this, smear, ex + new Vector3(0.6f, 1.0f, MazeCell * 0.5f - 0.1f), Vector3.Forward, Vector3.Down, new Vector2(1.4f, 1.2f), 0.4f, new Color(1, 1, 1, 0.8f));
	}

	/// <summary>The exit cell's far wall is the inside of the bunker's own round front door: the frame,
	/// the leaf swung back against the wall, and only night beyond.</summary>
	private void BuildExitDoor()
	{
		var ex = CellCenter(_exitCell);
		float wallZ = ex.Z - MazeCell * 0.5f + MazeWallThickness * 0.5f;   // inner face of the far wall
		var ringC = new Vector3(ex.X, 1.25f, wallZ);
		var k = new MeshKit();
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.5f, 0.48f, 0.44f);
		const int segs = 20;
		for (int i = 0; i < segs; i++)
		{
			float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
			Vector3 d0 = new(Mathf.Cos(a0), Mathf.Sin(a0), 0), d1 = new(Mathf.Cos(a1), Mathf.Sin(a1), 0);
			k.Beam(ringC + d0 * 1.2f + Vector3.Back * 0.05f, ringC + d1 * 1.2f + Vector3.Back * 0.05f, 0.18f, 0.1f, 1f, Vector3.Back);
		}
		k.CommitTo(this, "ExitDoorFrame");
		// The night outside, seen through the open door: a disc of deep blue-black.
		var night = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.03f, 0.05f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var nk = new MeshKit();
		nk.Mat(night);
		for (int i = 0; i < segs; i++)
		{
			float a0 = Mathf.Tau * i / segs, a1 = Mathf.Tau * (i + 1) / segs;
			nk.Tri(ringC + Vector3.Back * 0.01f, ringC + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0) * 1.12f + Vector3.Back * 0.01f,
				ringC + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0) * 1.12f + Vector3.Back * 0.01f, Vector3.Back, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		}
		nk.CommitTo(this, "ExitNight", false);
		// The leaf, swung open against the right-hand wall.
		var leaf = new MeshKit { Xf = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(-95f), 0)), ringC + new Vector3(1.1f, -1.25f, 0.1f)) };
		BunkerExterior.VaultDoor(leaf, new Vector3(-BunkerExterior.DoorRadius, 1.25f, 0f), ProcTextures.MetalMat);
		leaf.CommitTo(this, "ExitDoorLeaf");
		AddChild(new OmniLight3D { Name = "Moonlight", LightColor = new Color(0.45f, 0.55f, 0.75f), LightEnergy = 0.6f, OmniRange = 4.5f, Position = ringC + new Vector3(0, 0.6f, 0.8f) });
	}
}
