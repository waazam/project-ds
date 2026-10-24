using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 1 as three walks out from the trailhead cabin and back (STORY.md Rev 5, 2026-10-16). Pinning at the cabin's photo
/// board moves the day on (the board sets the flags): no flag is walk 1, <see cref="Stage2Flag"/> walk 2,
/// <see cref="Stage3Flag"/> walk 3. The light and the fog follow the walk (<see cref="ForestAtmosphere"/>).
///
/// The ways (the trail and its branches, all in the terrain's data):
/// - <b>Walk 1, the Overlook Loop</b>: up the trail from the cabin, over the footbridge, right at the fork to the
///   overlook on its hill, and down the Ridge Path back to the trail just past the bridge; back over it to the cabin.
/// - <b>Walk 2, the Falls Path</b>: up the trail to the creek, left before the bridge along the bank to the foot of the
///   falls, up beside them to the rock over them, and down the hillside back to the trail by the mile sign; on down to the
///   cabin. Before walk 2 two old trunks lie across it (at the foot of the falls, and by the mile sign).
/// - <b>Walk 3</b>: the trail on past the overlook's fork to the TRAIL CLOSED board, the dead fir and the stairs. Before walk
///   3 a trunk lies across the trail past the fork, and the woods either side of the way on are walled off
///   (<see cref="RebuildWalls"/>: nobody gets round it through the trees). The trunks that close a walk are cleared
///   while the player is at the board, out of sight.
///
/// On walk 3 the other ways close as he goes (STORY: "fallen trunks close the other paths"): if he turns off toward the
/// cabin, the falls, the ridge or the overlook, a tree beside that way creaks, waits to be seen, and comes down across it
/// in front of him, flat and heavy, never within reach (the rules of <see cref="Act1TreeFalls"/>); then that way is walled
/// off too. The sounds of each walk are in Act1Walks.Voices.cs. Nothing here runs after Act 1 (the level is Act 25's too).
/// </summary>
[GlobalClass]
public partial class Act1Walks : Node3D
{
	public const string Stage2Flag = "act1_stage_2", Stage3Flag = "act1_stage_3";

	/// <summary>Which walk it is now (1-3), from the board's flags.</summary>
	public static int Stage
	{
		get
		{
			var s = StoryManager.Instance;
			if (s == null) return 1;
			return s.HasFlag(Stage3Flag) ? 3 : s.HasFlag(Stage2Flag) ? 2 : 1;
		}
	}

	/// <summary>The story is in Act 1 (before the first climb).</summary>
	public static bool InAct1 => StoryManager.Instance is { } s && s.Current < Checkpoint.Act2StairsClimbed;

	public static Act1Walks Instance { get; private set; }

	[Export] public NodePath LinePath = "../FriendTrail";
	/// <summary>The branches' Path3D names in the terrain's BranchPaths.</summary>
	[Export] public string SpurName = "Overlook", RidgeName = "RidgePath", FallsName = "FallsPath";
	/// <summary>Trail metres: where the trunk lies across the trail before walk 3 (just past the overlook's fork).</summary>
	[Export] public float NorthGateAt = 240f;
	/// <summary>Falls Path metres: where its first stretch (along the creek to the foot of the falls) ends and the climb
	/// beside them begins.</summary>
	[Export] public float FallsSplitAt = 24f;
	/// <summary>Trail metres: on walk 3, the tree that comes down across the way back to the cabin.</summary>
	[Export] public float BackCloserAt = 150f;
	[ExportGroup("Walls")]
	/// <summary>The hidden walls stand on a grid of this size (m), this far (m) into the closed side of the line between
	/// the open ways and the closed ones, this tall.</summary>
	[Export] public float WallCell = 2f, WallMargin = 4f, WallHeight = 7f;
	/// <summary>How far out (m) from the ways the walls go (the edge thicket's walls are the rest).</summary>
	[Export] public float WallReach = 190f;
	[ExportGroup("Closing the ways (walk 3)")]
	/// <summary>How near (m) a closing tree's lie must be, and how fast toward it they must be going (m/s), for it to begin.</summary>
	[Export] public float CloseRange = 24f, CloseSpeed = 0.6f;
	[Export] public float SightRange = 38f, SightCone = 34f;
	/// <summary>Seconds a closing tree waits to be seen before it comes down anyway (if they're clear of it).</summary>
	[Export] public float CloseHoldMax = 14f;

	public enum Way { Back, Mid, North, Spur, Ridge, FallsA, FallsB }
	private const int WayCount = 7;

	private ForestTerrain _terrain;
	private readonly Polyline2[] _ways = new Polyline2[WayCount];
	private Polyline2 _trail, _spur, _ridge, _falls, _friend;
	private readonly bool[] _closed = new bool[WayCount];
	private int _builtStage = -1;
	private static Mesh[] _meshes;
	private bool _active;

	private sealed class Gate { public FallingTree Tree; public int UntilStage; }
	private readonly List<Gate> _gates = new();

	private sealed class Closer
	{
		public FallingTree Tree;
		public Way Closes;
		public Polyline2 Into;      // the way it closes, from its mouth inward
		public float At;            // metres along Into where it lies
		public double HeldAt = -1;
		public bool Done;
	}
	private readonly List<Closer> _closers = new();

	/// <summary>For tests: the trunks lying across the ways now (gates), the hidden wall pieces standing, and the closing
	/// trees of walk 3: how many there are, how many have come down, how many in view.</summary>
	public int GatesDown { get { int n = 0; foreach (var g in _gates) if (IsInstanceValid(g.Tree)) n++; return n; } }
	public int WallPieces { get; private set; }
	public int CloserCount => _closers.Count;
	public int ClosersDown { get; private set; }
	public int ClosersSeen { get; private set; }
	public bool IsClosed(Way w) => _closed[(int)w] || !Open(w, _builtStage < 0 ? Stage : _builtStage);
	/// <summary>For tests: a way's line, world x,z (null when the level hasn't one).</summary>
	public Polyline2 WayLine(Way w) => _ways[(int)w];
	/// <summary>For tests: a closing tree's lie (its foot and 0.95 of its height) and the way it closes.</summary>
	public IEnumerable<(Way way, FallingTree tree)> Closers() { foreach (var c in _closers) yield return (c.Closes, c.Tree); }

	public override void _EnterTree() => Instance = this;

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (StoryManager.Instance is { } s) s.FlagSet -= OnFlag;
	}

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		_active = InAct1 && _terrain != null;
		if (!_active) { SetProcess(false); return; }
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		BuildWays();
		_meshes ??= new[]
		{
			ForestScatter.FirMesh(31, 18f, 0.31f, 0.26f, 11, 3.0f, 0.15f, 0.4f),
			ForestScatter.FirMesh(32, 22f, 0.34f, 0.30f, 15, 2.4f, 0.08f, 0.4f),
			ForestScatter.FirMesh(33, 11f, 0.22f, 0.3f, 8, 2.0f, 0.2f, 0.5f),
		};
		BuildTrees();
		if (StoryManager.Instance is { } story) story.FlagSet += OnFlag;
		ApplyStage(Stage, true);
		ReadySounds();
	}

	// ------------------------------------------------------------------ the ways

	private void BuildWays()
	{
		var o = new Vector2(_terrain.GlobalPosition.X, _terrain.GlobalPosition.Z);
		_trail = Shift(_terrain.Trail, o);
		for (int i = 0; i < _terrain.BranchCount && i < _terrain.BranchPaths.Length; i++)
		{
			string name = _terrain.BranchPaths[i].GetName(_terrain.BranchPaths[i].GetNameCount() - 1);
			var b = Shift(_terrain.Branch(i), o);
			if (b.Points.Count < 2) continue;
			if (name == SpurName) _spur = b;
			else if (name == RidgeName) _ridge = b;
			else if (name == FallsName) _falls = b;
		}
		if (GetNodeOrNull<FriendTrail>(LinePath) is { } line && line.Waypoints.Length > 1)
		{
			_friend = new Polyline2();
			foreach (var w in line.Waypoints) _friend.Add(w);
		}
		float L = _trail.Length;
		_ways[(int)Way.Back] = Sub(_trail, 0f, BackCloserAt);
		_ways[(int)Way.Mid] = Sub(_trail, BackCloserAt, NorthGateAt);
		var north = Sub(_trail, NorthGateAt, L);
		if (_friend != null) foreach (var p in _friend.Points) north.Add(p);
		_ways[(int)Way.North] = north;
		_ways[(int)Way.Spur] = _spur != null ? Sub(_spur, 0f, _spur.Length) : null;
		_ways[(int)Way.Ridge] = _ridge != null ? Sub(_ridge, 0f, _ridge.Length) : null;
		if (_falls != null)
		{
			_ways[(int)Way.FallsA] = Sub(_falls, 0f, FallsSplitAt);
			_ways[(int)Way.FallsB] = Sub(_falls, FallsSplitAt, _falls.Length);
		}
		GD.Print($"[story] Act 1 walks: trail {L:0} m, spur {_spur?.Length ?? 0:0} m, ridge {_ridge?.Length ?? 0:0} m, falls {_falls?.Length ?? 0:0} m");
	}

	private static Polyline2 Shift(Polyline2 pl, Vector2 o)
	{
		var r = new Polyline2();
		foreach (var p in pl.Points) r.Add(p + o);
		return r;
	}

	/// <summary>The stretch of a line from metres a to b, a point every 2 m.</summary>
	private static Polyline2 Sub(Polyline2 pl, float a, float b)
	{
		var r = new Polyline2();
		if (pl == null || pl.Points.Count < 2) return r;
		a = Mathf.Clamp(a, 0f, pl.Length); b = Mathf.Clamp(b, 0f, pl.Length);
		for (float s = a; s < b; s += 2f) r.Add(pl.At(s, out _));
		r.Add(pl.At(b, out _));
		return r;
	}

	/// <summary>A line run backwards (the falls path from its south end).</summary>
	private static Polyline2 Reverse(Polyline2 pl)
	{
		var r = new Polyline2();
		for (int i = pl.Points.Count - 1; i >= 0; i--) r.Add(pl.Points[i]);
		return r;
	}

	/// <summary>Open on this walk (before any closing tree has come down across it).</summary>
	private static bool Open(Way w, int stage) => w switch
	{
		Way.North => stage >= 3,
		Way.FallsB => stage >= 2,
		_ => true,
	};

	// ------------------------------------------------------------------ the trees: the gates, and walk 3's closers

	private void BuildTrees()
	{
		// before walk 3: the trunk across the trail past the fork
		AddGate(_trail, NorthGateAt, 1, 18f, +1, 3, 61);
		if (_falls != null)
		{
			// before walk 2: a short one down across the path at the foot of the falls (the creek is right there: it lies
			// from the bank above to the water's edge), and a big one near the trail by the mile sign
			AddGate(_falls, FallsSplitAt + 1f, 2, 10f, +1, 2, 62);
			AddGate(Reverse(_falls), 10f, 0, 18f, -1, 2, 63);
		}
		// walk 3's: standing beside each way's mouth until he turns into it
		// (each a way in from its mouth: far enough in that they're well off the trail, and still short of it, when it begins)
		AddCloser(Way.Back, Reverse(Sub(_trail, 0f, BackCloserAt + 12f)), 12f, 1, -1, 71);
		if (_falls != null)
		{
			AddCloser(Way.FallsA, _falls, 14f, 0, +1, 72);   // (its foot uphill: the creek is on the other side)
			AddCloser(Way.FallsB, Reverse(_falls), 16f, 1, +1, 73);
		}
		if (_ridge != null) AddCloser(Way.Ridge, _ridge, 14f, 0, +1, 74);
		if (_spur != null) AddCloser(Way.Spur, _spur, 14f, 1, -1, 75);
	}

	/// <summary>A tree's foot beside a way at metres s, on <paramref name="side"/> (+1 left of the way's travel), falling
	/// across it.</summary>
	private (Vector2 foot, Vector3 fall) Across(Polyline2 way, float s, int side, float footOff)
	{
		var p = way.At(s, out Vector2 d);
		var left = new Vector2(d.Y, -d.X);
		var foot = p + left * side * footOff;
		var fall2 = (-left * side + d * 0.12f).Normalized();
		return (foot, new Vector3(fall2.X, 0, fall2.Y));
	}

	private FallingTree MakeTree(Vector2 at, Vector3 fall, int mesh, float h, int seed, string name)
	{
		var foot = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y) - 0.05f, at.Y);
		var t = new FallingTree { Name = name, Mesh = _meshes[mesh], Height = h, TrunkR = mesh == 2 ? 0.26f : 0.36f + mesh * 0.04f, FallDir = fall, Seed = seed };
		t.Reserve(this, _terrain, foot);
		t.Position = foot;   // (top level at the origin: local is world; set before it readies, as in Act7TreeFalls)
		AddChild(t);
		return t;
	}

	private void AddGate(Polyline2 way, float s, int mesh, float h, int side, int untilStage, int seed)
	{
		if (way == null || way.Length < s + 1f) return;
		// (the short one lies from the bank down to the water: its foot nearer the path)
		var (foot, fall) = Across(way, s, side, h < 14f ? 4.5f : 6f);
		var t = MakeTree(foot, fall, mesh, h, seed, $"Gate{seed}");
		t.SetDown();
		_gates.Add(new Gate { Tree = t, UntilStage = untilStage });
	}

	private void AddCloser(Way closes, Polyline2 into, float at, int mesh, int side, int seed)
	{
		if (into == null || into.Length < at + 2f) return;
		var (foot, fall) = Across(into, at, side, 5.2f);
		var t = MakeTree(foot, fall, mesh, mesh == 0 ? 18f : 22f, seed, $"Closer{closes}");
		var mouth = into.At(0f, out _);
		t.KeepToward = new Vector3(mouth.X, t.Position.Y, mouth.Y);   // (anyone caught under it is put out at the way's mouth, not shut in)
		_closers.Add(new Closer { Tree = t, Closes = closes, Into = into, At = at });
	}

	// ------------------------------------------------------------------ the day moving on

	private void OnFlag(string flag)
	{
		if (flag == Stage2Flag || flag == Stage3Flag) ApplyStage(Stage, false);
	}

	private void ApplyStage(int stage, bool first)
	{
		if (stage == _builtStage) return;
		_builtStage = stage;
		// the walk's trunks go while he stands at the board (the cabin is out of sight of all of them)
		foreach (var g in _gates)
			if (stage >= g.UntilStage && IsInstanceValid(g.Tree)) g.Tree.QueueFree();
		_gates.RemoveAll(g => !IsInstanceValid(g.Tree) || g.Tree.IsQueuedForDeletion());
		GD.Print($"[story] Act 1: walk {stage}{(first ? " (loaded)" : "")}; {_gates.Count} trunks across the ways");
		_wallsDirty = true;
		if (_blazed) ShowBlazes(stage);
	}

	public override void _Process(double delta)
	{
		if (!_active) return;
		if (!InAct1) { ClearWalls(); _active = false; return; }
		_clock += delta;
		var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_wallsDirty && _clock >= _nextWallTry) TryRebuildWalls(player);
		UpdateWayfinding(player, (float)delta);
		if (player == null) return;
		if (_builtStage >= 3) UpdateClosers(player);
		UpdateSounds(player, (float)delta);
	}

	// ------------------------------------------------------------------ walk 3: the ways closing

	private double _clock, _nextWallTry;
	private bool _wallsDirty;
	private readonly RandomNumberGenerator _rng = new() { Seed = 4401 };

	private void UpdateClosers(PlayerController player)
	{
		var pp = player.GlobalPosition;
		var q = new Vector2(pp.X, pp.Z);
		var vel = new Vector2(player.Velocity.X, player.Velocity.Z);
		foreach (var c in _closers)
		{
			if (c.Done) continue;
			var t = c.Tree;
			if (t.Down)
			{
				c.Done = true;
				ClosersDown++;
				_closed[(int)c.Closes] = true;
				// (the falls' climb only leads back down to the trail by the cabin: shut with the way back)
				if (c.Closes == Way.Back) _closed[(int)Way.FallsB] = true;
				_wallsDirty = true;
				GD.Print($"[story] Act 1: the way {c.Closes} is closed");
				continue;
			}
			var (la, lb) = t.LieSegment;
			Vector2 a = new(la.X, la.Z), b = new(lb.X, lb.Z);
			float toLie = DistToSeg(q, a, b);
			if (!t.Falling)
			{
				// turning into it: on the near side of where it will lie (not past it), near, and heading for it; off the way
				// on (the trail north), not just passing its mouth. The way back is the trail itself: heading back down it.
				float dInto = c.Into.Closest(q, out float sp);
				var mid = c.Into.At(c.At, out _);
				bool nearSide = sp < c.At - 2f && dInto < 7f;
				bool heading = vel.Length() > 0.3f && vel.Dot((mid - q).Normalized()) > CloseSpeed;
				bool turnedOff;
				if (c.Closes == Way.Back)
				{
					float s = TrailS(q, out float offT);
					_trail.At(s, out Vector2 tan);
					turnedOff = offT < 7f && s > BackCloserAt + 4f && vel.Dot(tan) < -CloseSpeed;
				}
				else { RouteProgress(q, out float offR); turnedOff = offR > 3.2f; }
				if (nearSide && heading && turnedOff && toLie > 6.5f && toLie < CloseRange && !IsClosed(c.Closes))
				{
					t.QuietCreak = false;
					t.Fall(true, 2.0f);
					c.HeldAt = _clock;
					GD.Print($"[story] Act 1: a tree creaks over the way {c.Closes}");
				}
				continue;
			}
			if (!t.Held || t.Motion.CreakTime < t.Motion.CreakSeconds) continue;
			// never on them: clear of where it will lie and of its foot
			if (toLie < 5.5f || q.DistanceTo(a) < 6.5f) continue;
			bool seen = Sees(player, t);
			if (!seen && _clock - c.HeldAt < CloseHoldMax) continue;
			t.Release();
			if (seen) ClosersSeen++;
			GD.Print($"[story] Act 1: it comes down across the way {c.Closes} ({(seen ? "in view" : "unseen, the wait over")})");
		}
	}

	private bool Sees(PlayerController p, FallingTree t) =>
		Act1TreeFalls.InSight(p, t.TrunkPoint(0.15f), SightRange, SightCone, t) || Act1TreeFalls.InSight(p, t.TrunkPoint(0.4f), SightRange, SightCone, t)
		|| Act1TreeFalls.InSight(p, t.TrunkPoint(0.7f), SightRange, SightCone, t);

	private static float DistToSeg(Vector2 q, Vector2 a, Vector2 b)
	{
		var ab = b - a;
		float t = ab.LengthSquared() > 1e-6f ? Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
		return q.DistanceTo(a + ab * t);
	}

	// ------------------------------------------------------------------ the hidden walls

	// (the woods either side of a closed way: the corridor's thickets are hard going, not a wall, and a player who goes round
	// the trunk through the trees must not reach the fir on walk 1. A wall stands along the line where the closed ways are
	// nearer than the open ones, pushed WallMargin into the closed side, so it runs across the way just behind its trunk and
	// out through the woods either side; deep in the trees, never on an open way.)
	private float[][] _field;
	private Vector2 _g0;
	private int _gw, _gh;
	private StaticBody3D _walls;

	private void EnsureFields()
	{
		if (_field != null) return;
		var rect = _terrain.PlayRect;
		_g0 = rect.Position;
		_gw = Mathf.CeilToInt(rect.Size.X / WallCell) + 1;
		_gh = Mathf.CeilToInt(rect.Size.Y / WallCell) + 1;
		_field = new float[WayCount][];
		for (int w = 0; w < WayCount; w++)
		{
			var f = new float[_gw * _gh];
			Array.Fill(f, WallReach);
			if (_ways[w] is { } line && line.Points.Count > 1) line.Raster(_g0, WallCell, _gw, _gh, WallReach, f, null);
			_field[w] = f;
		}
	}

	/// <summary>Walled off: the nearest closed way is nearer than the nearest open one by more than the margin.</summary>
	private bool Walled(int k, int stage)
	{
		float dOpen = WallReach, dShut = WallReach;
		for (int w = 0; w < WayCount; w++)
		{
			if (_ways[w] == null) continue;
			// (before walk 2 the falls' climb is shut by its two trunks only: a wall round it would stand across the falls
			// themselves, in the way of every picture of them from the bridge)
			bool shut = _closed[w] || (w == (int)Way.North && stage < 3);
			float d = _field[w][k];
			if (shut) dShut = Mathf.Min(dShut, d); else dOpen = Mathf.Min(dOpen, d);
		}
		return dShut < WallReach && dShut + WallMargin < dOpen;
	}

	private int Cell(Vector3 p)
	{
		int i = Mathf.Clamp(Mathf.RoundToInt((p.X - _g0.X) / WallCell), 0, _gw - 1);
		int j = Mathf.Clamp(Mathf.RoundToInt((p.Z - _g0.Y) / WallCell), 0, _gh - 1);
		return j * _gw + i;
	}

	private void TryRebuildWalls(PlayerController player)
	{
		EnsureFields();
		int stage = _builtStage;
		// never with the player on the wrong side of one: wait until they're back on the open side
		if (player != null && Walled(Cell(player.GlobalPosition), stage))
		{
			_nextWallTry = _clock + 1.0;
			return;
		}
		_wallsDirty = false;
		RebuildWalls(stage);
	}

	private void ClearWalls()
	{
		if (_walls != null && IsInstanceValid(_walls)) _walls.QueueFree();
		_walls = null;
		WallPieces = 0;
	}

	private void RebuildWalls(int stage)
	{
		ClearWalls();
		var shut = new bool[_gw * _gh];
		bool any = false;
		for (int k = 0; k < shut.Length; k++) { shut[k] = Walled(k, stage); any |= shut[k]; }
		if (!any) { GD.Print("[story] Act 1: no hidden walls (every way open)"); return; }
		_walls = new StaticBody3D { Name = "WalkWalls", CollisionLayer = 1, CollisionMask = 0 };
		AddChild(_walls);
		int n = 0;
		const int maxRun = 4;   // (a piece at most 8 m long: it follows the ground)
		// edges between neighbouring cells, x then z, merged into runs
		for (int i = 0; i < _gw - 1; i++)
			for (int j = 0; j < _gh;)
			{
				if (shut[j * _gw + i] == shut[j * _gw + i + 1]) { j++; continue; }
				int j0 = j;
				while (j < _gh && j - j0 < maxRun && shut[j * _gw + i] != shut[j * _gw + i + 1]) j++;
				float x = _g0.X + (i + 0.5f) * WallCell;
				Piece(new Vector2(x, _g0.Y + (j0 - 0.5f) * WallCell), new Vector2(x, _g0.Y + (j - 0.5f) * WallCell));
				n++;
			}
		for (int j = 0; j < _gh - 1; j++)
			for (int i = 0; i < _gw;)
			{
				if (shut[j * _gw + i] == shut[(j + 1) * _gw + i]) { i++; continue; }
				int i0 = i;
				while (i < _gw && i - i0 < maxRun && shut[j * _gw + i] != shut[(j + 1) * _gw + i]) i++;
				float z = _g0.Y + (j + 0.5f) * WallCell;
				Piece(new Vector2(_g0.X + (i0 - 0.5f) * WallCell, z), new Vector2(_g0.X + (i - 0.5f) * WallCell, z));
				n++;
			}
		WallPieces = n;
		GD.Print($"[story] Act 1: {n} hidden wall pieces round the closed ways");
	}

	private void Piece(Vector2 a, Vector2 b)
	{
		float ha = _terrain.HeightAt(a.X, a.Y), hb = _terrain.HeightAt(b.X, b.Y), hm = _terrain.HeightAt((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
		float lo = Mathf.Min(hm, Mathf.Min(ha, hb)) - 2f, hi = Mathf.Max(hm, Mathf.Max(ha, hb)) + WallHeight;
		var mid = (a + b) * 0.5f;
		float len = a.DistanceTo(b);
		bool alongX = Mathf.Abs(b.X - a.X) > Mathf.Abs(b.Y - a.Y);
		var size = alongX ? new Vector3(len + 0.3f, hi - lo, 0.4f) : new Vector3(0.4f, hi - lo, len + 0.3f);
		_walls.AddChild(new CollisionShape3D { Position = new Vector3(mid.X, (lo + hi) * 0.5f, mid.Y), Shape = new BoxShape3D { Size = size } });
	}

	// ------------------------------------------------------------------ helpers for the walks' sounds

	/// <summary>The walk-3 route: the trail, then the old way's line from the dead fir to the stairs; metres along it.</summary>
	private float RouteLength => _trail.Length + (_friend?.Length ?? 0f);

	private Vector2 RouteAt(float u, out Vector2 dir)
	{
		if (u <= _trail.Length || _friend == null) return _trail.At(u, out dir);
		return _friend.At(u - _trail.Length, out dir);
	}

	/// <summary>How far along the walk-3 route the nearest point to q is, and how far off it q is.</summary>
	private float RouteProgress(Vector2 q, out float off)
	{
		float dT = _trail.Closest(q, out float sT);
		if (_friend != null)
		{
			float dF = _friend.Closest(q, out float sF);
			if (dF < dT) { off = dF; return _trail.Length + sF; }
		}
		off = dT;
		return sT;
	}

	private float TrailS(Vector2 q, out float off) { off = _trail.Closest(q, out float s); return s; }
}
