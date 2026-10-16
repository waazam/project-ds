using System.Collections.Generic;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 7's herding (2026-10-16, STORY.md Rev 5): "From the cabin to the ridge, trees fall where the giant looks, as the
/// walls fell in Act 1 (each creaks first and falls only in the player's view, flat and heavy, never within reach). They
/// close the woods on either side and behind, so the only way is up the ridge to the lookout. It never touches the
/// player." The herding stops at the lookout: the walk from there to the bunker is open woods (the second arrow trail
/// leaves it).
///
/// Act 1's rules (<see cref="Act1TreeFalls"/>, <see cref="FallingTree"/>), on the trail from the burning cabin's front
/// up to the lookout:
/// <list type="bullet">
/// <item><b>Walls.</b> Trees either side of the way (<see cref="SideAt"/>), alternately left and right, begin to creak as
/// the player comes near, and then wait, leaning and groaning, until they're in view; then they come down lengthwise
/// along the way's edge. As each begins, the giant's head and body jerk round to it (<see cref="GiantStalkerEvent.LookToward"/>):
/// it looks, and the tree goes.</item>
/// <item><b>Behind.</b> Once the player is well past, trees come down across the way they came (<see cref="BehindAt"/>;
/// the first, back past the cabin, as soon as they've left its door): heard more than seen.</item>
/// <item><b>Strays.</b> Off the way, a tree out in the woods (<see cref="WardAt"/>) comes down across their path ahead of
/// them, in view.</item>
/// <item><b>Never within reach</b>, never with the player on the wrong side of a wall; nothing goes on without them.</item>
/// </list>
/// Live only at checkpoint 6. Past it (the bunker reached) they all lie where they fell.
/// </summary>
[GlobalClass]
public partial class Act7TreeFalls : Node3D
{
	/// <summary>Along the way (m from the cabin's front): the trees beside it, alternately left and right.</summary>
	[Export] public float[] SideAt = { 7f, 14f, 21f, 28f, 35f, 42f };
	/// <summary>Along the way (m): trees across it behind the player (a negative one stands back past the cabin).</summary>
	[Export] public float[] BehindAt = { -7f, 12f, 30f };
	/// <summary>Along the way (m): trees out in the woods either side, for a stray.</summary>
	[Export] public float[] WardAt = { 8f, 24f, 38f };
	[Export] public float SideOffset = 4.6f, WardOffset = 15f;
	[Export] public float AheadTrigger = 14f, BehindTrigger = 11f;
	/// <summary>How far off the trail (m) still counts as on the way.</summary>
	[Export] public float OnTheWay = 7f;
	[Export] public float SightRange = 40f, SightCone = 34f;
	/// <summary>A wall tree beside them waits this long to be seen, then comes down anyway (they must not walk past it).</summary>
	[Export] public float SideHoldMax = 12f;
	/// <summary>No tree stands within this many metres (along the way) of the lookout: the herding stops short of it.</summary>
	[Export] public float StopShort = 8f;

	private enum Kind { Side, Behind, Ward }
	private sealed class Entry
	{
		public FallingTree Tree;
		public float At;
		public Kind Kind;
		public int Side;
		public double HeldAt = -1;
	}
	private readonly List<Entry> _trees = new();
	private ForestTerrain _terrain;
	private float _sA, _sB;
	private double _clock;
	private readonly RandomNumberGenerator _rng = new() { Seed = 7071 };
	private static Mesh[] _meshes;

	/// <summary>For tests: how many trees, and how many have come down (beside the way, behind, out in the woods).</summary>
	public int SideCount => _trees.FindAll(e => e.Kind == Kind.Side).Count;
	public int SideDown { get; private set; }
	public int BehindDown { get; private set; }
	public int WardDown { get; private set; }
	/// <summary>For tests: falls that cracked in view, and those that came down unseen (behind, or the wait over).</summary>
	public int SeenFalls { get; private set; }
	public int UnseenFalls { get; private set; }
	/// <summary>For tests: the nearest the player ever stood to where a tree came down, as it cracked (metres).</summary>
	public float NearestAtCrack { get; private set; } = float.MaxValue;
	/// <summary>For tests: the player's progress along the way (m from the cabin's front) and its length (to the lookout).</summary>
	public float Progress { get; private set; }
	public float Length => _sB - _sA;
	public bool Live { get; private set; }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		var spot = Cabin.Act7Spot(this);
		var lookout = GetTree().GetFirstNodeInGroup("fire_lookout_marker") as Node3D;
		if (_terrain == null || spot == null || lookout == null) return;
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		// the way: from the trail in front of the cabin's door to the lookout
		Vector3 front = spot.GlobalPosition + spot.GlobalBasis.Z.Normalized() * 10f;
		_terrain.TrailDistance(front.X, front.Z, out _sA);
		_terrain.TrailDistance(lookout.GlobalPosition.X, lookout.GlobalPosition.Z, out _sB);
		if (_sB < _sA + 20f) return;
		_meshes ??= new[]
		{
			ForestScatter.FirMesh(23, 18f, 0.31f, 0.26f, 11, 3.0f, 0.15f, 0.4f),
			ForestScatter.FirMesh(24, 22f, 0.34f, 0.30f, 15, 2.4f, 0.08f, 0.4f),
			ForestScatter.FirMesh(22, 26f, 0.42f, 0.36f, 13, 3.7f, 0.12f, 0.4f),
		};
		Vector2 cabinXZ = new(spot.GlobalPosition.X, spot.GlobalPosition.Z);
		Vector2 lookXZ = new(lookout.GlobalPosition.X, lookout.GlobalPosition.Z);
		int seed = 1;
		for (int i = 0; i < SideAt.Length; i++)
		{
			float s = SideAt[i];
			if (_sA + s > _sB - StopShort) continue;
			var p = _terrain.TrailPoint(_sA + s, out var tan);
			var left = new Vector3(tan.Z, 0, -tan.X).Normalized();
			int side = i % 2 == 0 ? 1 : -1;
			int mi = i % 3;
			float h = mi == 0 ? 18f : mi == 1 ? 22f : 26f;
			// lengthwise along the edge, on toward the ridge, a little outward
			var fall = (tan + left * side * 0.22f).Normalized();
			// (never over the lookout: it falls shorter of it, outward)
			var foot = p + left * side * SideOffset;
			if (Flat(foot + fall * h).DistanceTo(lookXZ) < 10f) fall = (left * side + tan * 0.4f).Normalized();
			Add(foot, fall, mi, h, s, Kind.Side, side, seed++, cabinXZ, lookXZ);
		}
		for (int i = 0; i < BehindAt.Length; i++)
		{
			float s = BehindAt[i];
			var p = _terrain.TrailPoint(_sA + s, out var tan);
			var left = new Vector3(tan.Z, 0, -tan.X).Normalized();
			int side = i % 2 == 0 ? -1 : 1;
			// across the way from one side to the other, slanting back the way they came
			var fall = (-left * side - tan * 0.25f).Normalized();
			Add(p + left * side * (SideOffset + 0.6f), fall, 2, 26f, s, Kind.Behind, side, seed++, cabinXZ, lookXZ);
		}
		for (int i = 0; i < WardAt.Length; i++)
			foreach (int side in new[] { 1, -1 })
			{
				float s = WardAt[i];
				var p = _terrain.TrailPoint(_sA + s, out var tan);
				var left = new Vector3(tan.Z, 0, -tan.X).Normalized();
				var fall = (tan + left * side * 0.12f).Normalized();
				Add(p + left * side * WardOffset, fall, 1, 22f, s, Kind.Ward, side, seed++, cabinXZ, lookXZ);
			}
		Callable.From(Restore).CallDeferred();
		if (StoryManager.Instance is { } sm) sm.CheckpointReached += OnCheckpoint;
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
	}

	private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

	private static float DistToSeg(Vector2 q, Vector2 a, Vector2 b)
	{
		var ab = b - a;
		float t = ab.LengthSquared() > 1e-6f ? Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
		return q.DistanceTo(a + ab * t);
	}

	/// <summary>A tree, unless where it would lie crosses the cabin, the lookout or (a ward) the way itself.</summary>
	private void Add(Vector3 at, Vector3 fall, int mesh, float h, float s, Kind kind, int side, int seed, Vector2 cabin, Vector2 lookout)
	{
		var foot = new Vector3(at.X, _terrain.HeightAt(at.X, at.Z) - 0.05f, at.Z);
		Vector2 a = Flat(foot), b = Flat(foot + fall * h * 0.95f);
		if (DistToSeg(cabin, a, b) < 7f || DistToSeg(lookout, a, b) < 7f) return;
		if (kind == Kind.Ward)
			for (float u = 0f; u <= 1.001f; u += 0.1f)
			{
				var q = a.Lerp(b, u);
				if (_terrain.TrailDistance(q.X, q.Y, out _) < 6f) return;
			}
		var t = new FallingTree { Name = $"Act7{kind}Tree{seed}", Mesh = _meshes[mesh], Height = h, TrunkR = 0.36f + mesh * 0.04f, FallDir = fall, Seed = 60 + seed };
		t.Reserve(this, _terrain, foot);
		t.Position = foot;   // (this node is top level at the origin: local is world; set before it readies, which measures its fall from there)
		AddChild(t);
		_trees.Add(new Entry { Tree = t, At = s, Kind = kind, Side = side });
	}

	private void Restore()
	{
		var s = StoryManager.Instance;
		if (s == null) return;
		if (s.Current > Checkpoint.Act7CabinBurning) { foreach (var e in _trees) e.Tree.SetDown(); return; }
		if (s.Current == Checkpoint.Act7CabinBurning) Begin();
	}

	private void OnCheckpoint(Checkpoint cp)
	{
		if (cp == Checkpoint.Act7CabinBurning) Begin();
		else if (cp > Checkpoint.Act7CabinBurning) { Live = false; SetProcess(false); }
	}

	private void Begin()
	{
		if (Live || _trees.Count == 0) return;
		Live = true;
		SetProcess(true);
		GD.Print($"[story] Act 7: the trees will herd them up to the lookout ({_trees.Count} standing, {Length:0} m of way)");
	}

	public override void _Process(double delta)
	{
		if (!Live || GetTree().GetFirstNodeInGroup("player") is not PlayerController player) return;
		_clock += delta;
		Vector3 pp = player.GlobalPosition;
		float off = _terrain.TrailDistance(pp.X, pp.Z, out float sp);
		float prog = sp - _sA;
		Progress = prog;
		bool onTheWay = off < OnTheWay && prog > -12f && prog < Length + 6f;
		SideDown = BehindDown = WardDown = 0;
		foreach (var e in _trees)
		{
			var t = e.Tree;
			if (t.Down) { if (e.Kind == Kind.Side) SideDown++; else if (e.Kind == Kind.Behind) BehindDown++; else WardDown++; continue; }
			if (t.Falling && !t.Held) continue;
			var (la, lb) = t.LieSegment;
			switch (e.Kind)
			{
				case Kind.Behind:
				{
					// heard more than seen: once they're well past (the one back past the cabin: once they've left its door)
					float trigger = e.At < 0f ? 4f : e.At + BehindTrigger;
					if (onTheWay && prog >= trigger && Clear(pp, la, lb, 6f))
					{
						t.QuietCreak = _rng.Randf() < 0.5f;
						t.Fall();
						Look(t);
						UnseenFalls++;
						Note(e, pp, "behind them");
					}
					break;
				}
				case Kind.Side:
					if (!t.Falling)
					{
						if (onTheWay && prog >= e.At - AheadTrigger && prog <= e.At + 8f) Start(e, 2.3f + 0.1f * (e.At % 3f));
					}
					else TryRelease(e, player, SideHoldMax, prog >= e.At - 4f, off);
					break;
				case Kind.Ward:
					if (!t.Falling)
					{
						// off the way, out toward it: it comes down across their path in front of them
						float toLie = DistToSeg(Flat(pp), Flat(la), Flat(lb));
						if (off > 3f && toLie > 6.5f && toLie < 22f && Mathf.Abs(prog - e.At - 6f) < 16f && SideOf(pp) == e.Side) Start(e, 0.9f);
					}
					else TryRelease(e, player, float.MaxValue, false, off);
					break;
			}
		}
	}

	private GiantStalkerEvent _giant;

	/// <summary>Which side of the trail a point is (+1 left of the way on, -1 right).</summary>
	private int SideOf(Vector3 p)
	{
		_terrain.TrailDistance(p.X, p.Z, out float s);
		var q = _terrain.TrailPoint(s, out var tan);
		var left = new Vector3(tan.Z, 0, -tan.X);
		return (p - q).Dot(left) >= 0f ? 1 : -1;
	}

	private void Start(Entry e, float creak)
	{
		e.Tree.Fall(true, creak);
		e.HeldAt = _clock;
		Look(e.Tree);
	}

	/// <summary>The giant turns to it: the trees fall where it looks.</summary>
	private void Look(FallingTree t)
	{
		_giant ??= FindGiant(GetTree().CurrentScene);
		_giant?.LookToward(t.TrunkPoint(0.7f));
	}

	private static GiantStalkerEvent FindGiant(Node root)
	{
		if (root == null) return null;
		foreach (var n in root.FindChildren("*", "Node", true, false)) if (n is GiantStalkerEvent g) return g;
		return null;
	}

	private void TryRelease(Entry e, PlayerController player, float holdMax, bool fallbackOk, float off)
	{
		var t = e.Tree;
		if (!t.Held || t.Motion.CreakTime < t.Motion.CreakSeconds) return;
		var (la, lb) = t.LieSegment;
		if (!Clear(player.GlobalPosition, la, lb, e.Kind == Kind.Ward ? 6.5f : 4.5f)) return;
		// a wall never comes down with them on its far side, shutting them out of the way
		if (e.Kind == Kind.Side && SideOf(player.GlobalPosition) == e.Side && off > SideOffset - 1f) return;
		bool seen = Sees(player, t);
		if (!seen && !(fallbackOk && _clock - e.HeldAt > holdMax)) return;
		t.Release();
		if (seen) SeenFalls++; else UnseenFalls++;
		Note(e, player.GlobalPosition, seen ? "in view" : "unseen, the wait over");
	}

	private void Note(Entry e, Vector3 pp, string how)
	{
		var (la, lb) = e.Tree.LieSegment;
		NearestAtCrack = Mathf.Min(NearestAtCrack, DistToSeg(Flat(pp), Flat(la), Flat(lb)));
		GD.Print($"[story] Act 7: a tree comes down {(e.Kind == Kind.Behind ? "behind them" : e.Kind == Kind.Side ? "beside the way" : "out in the woods, across a stray's way")} ({e.At:0} m along, {how})");
	}

	private bool Sees(PlayerController p, FallingTree t) =>
		Act1TreeFalls.InSight(p, t.TrunkPoint(0.15f), SightRange, SightCone, t) || Act1TreeFalls.InSight(p, t.TrunkPoint(0.4f), SightRange, SightCone, t)
		|| Act1TreeFalls.InSight(p, t.TrunkPoint(0.7f), SightRange, SightCone, t);

	/// <summary>Never within reach: the player well clear of where it will lie, and of its foot.</summary>
	private static bool Clear(Vector3 player, Vector3 a, Vector3 b, float margin)
	{
		var q = Flat(player);
		return DistToSeg(q, Flat(a), Flat(b)) > margin && q.DistanceTo(Flat(a)) > margin + 1f;
	}
}
