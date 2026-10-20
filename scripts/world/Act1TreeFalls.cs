using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// Act 1's way to the stairs, made by the woods themselves (the owner, 2026-10-03: "remove the stone pathway, and have
/// trees fall left and right along that path to the stairs forcing the player to go into that direction ... we can
/// even have trees fall behind them after they pass the first fallen tree to make sure they dont deviate from the
/// path. This will be a more organic way to get the player to the stairs and feel more supernatural and creepy").
///
/// The line is the old path's (<see cref="FriendTrail"/>, its stones gone): from the trail's end round the dead fir's
/// crown to the stairs' foot. Once the fir is down across the trail, trees standing either side of the line come
/// down as the player nears them, lengthwise along its edges, a wall on the left, then on the right, so the way on is
/// the only way. Behind the player, once they're well past, trees come down across the line, closing it. None of them
/// comes down within reach of the player; near the stairs they fall outward, never into the clearing.
///
/// Seen, not just heard (2026-10-13, the owner: "the user sees the trees falling, maybe force the player to stop and
/// watch them all fall but it would need to feel natural, plus need to deal with what we do when the player doesn't go
/// the right way at first"):
/// - Every fall ahead of the player begins with its creak, and then waits, leaning and groaning, until it is in view
///   (on screen, near the middle of it, in range, nothing solid between): the creak is what turns their head, and the
///   crack comes when they're looking. The dead fir directs this too (<see cref="FallenTree.Directed"/>).
/// - The dead fir's crack stops them (<see cref="Watching"/>): their pace dies away as if startled, the view is drawn
///   toward the falling tree wherever they stand (2026-10-15: a firm, eased turn that holds while it falls; it used to
///   be a soft pull they could look away from), and the first trees of the
///   walls come down after it one by one, the view drawn to each, before the pace comes back. A few seconds; nothing
///   is taken from them but their legs, and nothing falls near them.
/// - Once the fir is down and the stop is over, the rest of the walls come down after it one by one, wherever the player
///   is (2026-10-15): nothing waits for them to walk on.
/// - Behind them the falls stay what they were: heard more than seen, a surprise.
/// - The wrong way: nothing goes on without them. The walls come down only while they're on the way (near the line)
///   and looking; if they stray off it, a tree out in the woods ahead of them comes down across their way, when seen;
///   if they turn back or stand still, the trees wait. A player who goes round the standing fir starts the walls all
///   the same.
/// </summary>
[GlobalClass]
public partial class Act1TreeFalls : Node3D
{
	[Export] public NodePath FallenTreePath = "../FallenTree";
	[Export] public NodePath LinePath = "../FriendTrail";
	/// <summary>Along the line (m): where the trees beside it stand, alternately left and right.</summary>
	[Export] public float[] SideAt = { 20f, 27f, 34f, 41f, 48f, 55f, 62f, 69f };
	/// <summary>Along the line (m), and which side (-1 right of travel): the first of the walls, by the trail's end.</summary>
	[Export] public float FirstWallAt = 9f;
	[Export] public int FirstWallSide = -1;
	/// <summary>Along the line (m): where trees come down across it behind the player.</summary>
	[Export] public float[] BehindAt = { 13f, 33f, 54f };
	/// <summary>Along the line (m): where trees stand out in the woods either side (<see cref="WardOffset"/> off it), to
	/// come down across the way of a player who strays.</summary>
	[Export] public float[] WardAt = { 6f, 24f, 42f, 58f };
	/// <summary>How far ahead of the player (m along the line) a side tree comes down; how far past a behind tree the
	/// player has to be before it closes the way.</summary>
	[Export] public float AheadTrigger = 15f, BehindTrigger = 12f;
	[Export] public float SideOffset = 4.4f;
	[Export] public float WardOffset = 16f;
	/// <summary>How far off the line (m) still counts as on the way (the walls come down only then).</summary>
	[Export] public float OnTheWay = 8f;

	[ExportGroup("Seen")]
	/// <summary>How far (m) a tree can be and still count as seen (the fog), and how far off the middle of the view
	/// (degrees).</summary>
	[Export] public float SightRange = 38f, SightCone = 34f;
	/// <summary>The dead fir: how near (m, to its foot) before it begins to creak, and before it may fall.</summary>
	[Export] public float FirCreakRange = 26f, FirFallRange = 22f;
	/// <summary>Seconds a held tree waits to be seen before it comes down anyway: the fir (from its first creak,
	/// wherever the player is by then; Dan, 2026-10-13), and the trees beside the way (only once the player is
	/// alongside them). Wards out in the woods never fall unseen.</summary>
	[Export] public float FirHoldMax = 25f, SideHoldMax = 12f;
	/// <summary>Seconds the dead fir creaks (the stop already on, the view turning to it) before it cracks.</summary>
	[Export] public float FirCreakBeforeFall = 1.8f;

	[ExportGroup("The stop")]
	/// <summary>How many of the walls' first trees come down after the fir while the player stands and watches.</summary>
	[Export] public int CascadeCount = 2;
	/// <summary>Seconds to stop (as if startled), and to get going again.</summary>
	[Export] public float HaltSeconds = 0.6f, ResumeSeconds = 1.3f;
	/// <summary>The look's pull toward the falling tree: how quickly, and its top speed (rad/s).</summary>
	[Export] public float AssistRate = 3.0f, AssistMax = 2.2f;
	/// <summary>The stop never lasts longer than this (s).</summary>
	[Export] public float WatchMax = 13f;

	private FallenTree _fir;
	private FriendTrail _line;
	private ForestTerrain _terrain;
	private static Mesh[] _meshes;

	private enum Kind { Side, Behind, Ward }
	private sealed class Entry
	{
		public FallingTree Tree;
		public float At;
		public Kind Kind;
		public int Side;            // which side of the line it stands (+1 left of travel, -1 right)
		public double HeldAt = -1;  // when its creak began
		public double GoAt = -1;    // (once it has all begun) when it starts to come down, wherever the player is
		public bool Counted;
	}
	private readonly List<Entry> _trees = new();

	/// <summary>For the tests: how many have come down beside the way, and across it behind.</summary>
	public int SideDown { get; private set; }
	public int BehindDown { get; private set; }
	public int SideCount => _trees.FindAll(e => e.Kind == Kind.Side).Count;
	/// <summary>For the tests: how many have started to come down, beside the way and behind.</summary>
	public int SideStarted { get; private set; }
	/// <summary>For tests: the highest any of them lies off the ground (they lie flat on it: the owner saw them propped on
	/// their branches).</summary>
	public float MaxRestTip { get { float m = 0f; foreach (var e in _trees) m = Mathf.Max(m, e.Tree.RestTipHeight); return m; } }
	public int BehindStarted { get; private set; }
	/// <summary>For the tests: falls ahead of the player that cracked while in view, and those that came down unseen
	/// (the long wait ran out).</summary>
	public int SeenFalls { get; private set; }
	public int UnseenFalls { get; private set; }
	/// <summary>For the tests: the trees out in the woods (for a player who strays): how many, and how many came down.</summary>
	public int WardCount { get; private set; }
	public int WardStarted { get; private set; }
	public int WardDown { get; private set; }
	/// <summary>For the tests: the stop is on (their pace taken, the view drawn to the falls); how many times it has
	/// happened, how long the last one lasted, and how many trees came down in it.</summary>
	public bool Watching => _watching;
	public int WatchCount { get; private set; }
	public float LastWatchSeconds { get; private set; }
	public int LastWatchFalls { get; private set; }
	/// <summary>For the tests: the fir cracked while the player had it in view.</summary>
	public bool FirSeen { get; private set; }
	/// <summary>For the tests: the player's progress along the line (m) and how far off it they are.</summary>
	public float Progress { get; private set; }
	public float OffLine { get; private set; }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_fir = GetNodeOrNull<FallenTree>(FallenTreePath);
		_line = GetNodeOrNull<FriendTrail>(LinePath);
		_terrain = GroundSnap.FindTerrain(this);
		if (_line == null || _terrain == null || _line.Length < 10f) return;
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		if (_fir != null) _fir.Directed = true;
		_meshes ??= new[]
		{
			ForestScatter.FirMesh(13, 18f, 0.31f, 0.26f, 11, 3.0f, 0.15f, 0.4f),
			ForestScatter.FirMesh(14, 22f, 0.34f, 0.30f, 15, 2.4f, 0.08f, 0.4f),
			ForestScatter.FirMesh(12, 26f, 0.42f, 0.36f, 13, 3.7f, 0.12f, 0.4f),
		};
		float L = _line.Length;
		for (int i = 0; i < SideAt.Length; i++)
		{
			float s = SideAt[i];
			if (s > L - 6f) continue;
			var p = _line.At(s, out Vector2 d2);
			var tan = new Vector3(d2.X, 0, d2.Y);
			var left = new Vector3(d2.Y, 0, -d2.X);
			float sideSign = i % 2 == 0 ? 1f : -1f;
			Vector3 outward = left * sideSign;
			int mi = i % 3;
			float h = mi == 0 ? 18f : mi == 1 ? 22f : 26f;
			// lengthwise along the edge, forward, a little outward; near the stairs (it would reach the clearing) outward
			Vector3 fall = s + h * 0.95f < L - 4f ? (tan + outward * 0.22f).Normalized() : (outward + tan * 0.35f).Normalized();
			Add(p + new Vector2(outward.X, outward.Z) * SideOffset, fall, mi, h, s, Kind.Side, (int)sideSign, i + 1, p);
		}
		// the first of the walls, near the trail's end on the near side of the way: the one they watch come down after the
		// fir (close enough to read through the fog), along the way's outer edge past the corner, closing the woods there
		if (FirstWallAt > 0f && FirstWallAt < L - 10f)
		{
			var p = _line.At(FirstWallAt, out Vector2 d2);
			var tan = new Vector3(d2.X, 0, d2.Y);
			Vector3 outward = new Vector3(d2.Y, 0, -d2.X) * FirstWallSide;
			Add(p + new Vector2(outward.X, outward.Z) * SideOffset, (tan + outward * 0.22f).Normalized(), 1, 22f, FirstWallAt, Kind.Side, FirstWallSide, 9, p);
		}
		for (int i = 0; i < BehindAt.Length; i++)
		{
			float s = BehindAt[i];
			if (s > L - 10f) continue;
			var p = _line.At(s, out Vector2 d2);
			var left = new Vector3(d2.Y, 0, -d2.X);
			float sideSign = i % 2 == 0 ? -1f : 1f;
			// across the line from one side to the other, slanting back the way they came
			var tan = new Vector3(d2.X, 0, d2.Y);
			Vector3 fall = (-left * sideSign - tan * 0.25f).Normalized();
			Add(p + new Vector2(left.X, left.Z) * sideSign * (SideOffset + 0.6f), fall, 2, 26f, s, Kind.Behind, (int)sideSign, 20 + i, p + d2 * 6f);   // (kept on the side toward the stairs)
		}
		// out in the woods either side: standing well off the way, falling along it (across the way of anyone walking
		// out from it); none where it would lie near the line, the fir, the trail or the stairs
		var stairsXZ = _line.At(L, out _);
		for (int i = 0; i < WardAt.Length; i++)
			for (int side = 1; side >= -1; side -= 2)
			{
				float s = WardAt[i];
				var p = _line.At(s, out Vector2 d2);
				var left = new Vector2(d2.Y, -d2.X);
				var foot = p + left * side * WardOffset;
				var fall2 = (d2 + left * side * 0.12f).Normalized();
				const float h = 22f;
				var tip = foot + fall2 * h * 0.95f;
				if (!WardFits(foot, tip, stairsXZ)) continue;
				Add(foot, new Vector3(fall2.X, 0, fall2.Y), 1, h, s, Kind.Ward, side, 40 + i * 2 + (side > 0 ? 0 : 1), p);
				WardCount++;
			}
	}

	/// <summary>A ward tree's lie keeps clear of the line (5 m), the fir, the trail's last stretch and the stairs.</summary>
	private bool WardFits(Vector2 foot, Vector2 tip, Vector2 stairs)
	{
		for (float u = 0f; u <= 1.001f; u += 0.1f)
		{
			var q = foot.Lerp(tip, u);
			LineNear(q, out float off, out _);
			if (Mathf.Abs(off) < 6f) return false;
			if (q.DistanceTo(stairs) < 16f) return false;
			if (_fir != null && DistToSeg(q, Flat(_fir.LieSegment.a), Flat(_fir.LieSegment.b)) < 5f) return false;
			if (_terrain.TrailDistance(q.X, q.Y, out _) < 6f) return false;
		}
		return true;
	}

	private void Add(Vector2 at, Vector3 fall, int mesh, float h, float s, Kind kind, int side, int seed, Vector2 keep)
	{
		var foot = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y) - 0.05f, at.Y);
		var t = new FallingTree { Name = $"{kind}Tree{seed}", Mesh = _meshes[mesh], Height = h, TrunkR = 0.36f + mesh * 0.04f, FallDir = fall, Seed = seed, KeepToward = new Vector3(keep.X, foot.Y, keep.Y) };
		t.Reserve(this, _terrain, foot);
		t.Position = foot;   // (this node is top level at the origin: local is world; set before it readies, which measures its fall and lays its lying collision from there)
		AddChild(t);
		_trees.Add(new Entry { Tree = t, At = s, Kind = kind, Side = side });
	}

	private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

	private static float DistToSeg(Vector2 q, Vector2 a, Vector2 b)
	{
		var ab = b - a;
		float t = ab.LengthSquared() > 1e-6f ? Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
		return q.DistanceTo(a + ab * t);
	}

	/// <summary>The nearest point of the line to <paramref name="q"/>: how far along it (returned), and how far off it,
	/// signed (+ left of travel).</summary>
	private float LineNear(Vector2 q, out float off, out Vector2 dir)
	{
		float best = float.MaxValue, bestS = 0f;
		for (float s = 0f; s <= _line.Length; s += 0.5f)
		{
			float d = _line.At(s, out _).DistanceSquaredTo(q);
			if (d < best) { best = d; bestS = s; }
		}
		var p = _line.At(bestS, out dir);
		var left = new Vector2(dir.Y, -dir.X);
		off = (q - p).Dot(left);
		// (past either end, the distance itself)
		if (bestS <= 0.01f || bestS >= _line.Length - 0.01f) off = Mathf.Sign(off == 0 ? 1 : off) * Mathf.Sqrt(best);
		return bestS;
	}

	// ------------------------------------------------------------------ seen

	/// <summary>
	/// Whether the player sees <paramref name="point"/> now: in front of the camera and on screen, within
	/// <paramref name="coneDegrees"/> of the middle of the view, within <paramref name="range"/>, and nothing solid
	/// between (a hit on <paramref name="self"/> or anything under it, or right by the point, is the thing itself).
	/// </summary>
	public static bool InSight(PlayerController player, Vector3 point, float range, float coneDegrees, Node self = null)
	{
		var cam = player?.CameraRig?.Camera;
		if (cam == null || !cam.IsInsideTree()) return false;
		Vector3 eye = cam.GlobalPosition;
		Vector3 to = point - eye;
		float d = to.Length();
		if (d > range || d < 0.3f) return false;
		if ((-cam.GlobalBasis.Z).Dot(to / d) < Mathf.Cos(Mathf.DegToRad(coneDegrees))) return false;
		if (!cam.IsPositionInFrustum(point)) return false;
		var space = cam.GetWorld3D()?.DirectSpaceState;
		if (space == null) return true;
		var q = PhysicsRayQueryParameters3D.Create(eye, point, 1u);
		q.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
		if (Clear(space, q, point, self)) return true;
		// a trunk at arm's length hides a thin slice of the view, not a falling tree: seen round it (a little to either side)
		var hit = space.IntersectRay(q);
		if (hit.Count == 0 || ((Vector3)hit["position"]).DistanceTo(eye) > 3f) return false;
		foreach (float side in new[] { -0.6f, 0.6f })
		{
			var q2 = PhysicsRayQueryParameters3D.Create(eye + cam.GlobalBasis.X * side, point, 1u);
			q2.Exclude = q.Exclude;
			if (Clear(space, q2, point, self)) return true;
		}
		return false;
	}

	private static bool Clear(PhysicsDirectSpaceState3D space, PhysicsRayQueryParameters3D q, Vector3 point, Node self)
	{
		var hit = space.IntersectRay(q);
		if (hit.Count == 0) return true;
		if (((Vector3)hit["position"]).DistanceTo(point) < 1.5f) return true;
		var col = hit["collider"].AsGodotObject() as Node;
		return self != null && col != null && (col == self || self.IsAncestorOf(col));
	}

	/// <summary>For the log: why a point isn't seen (off the view by how much, how far, or what's in the way).</summary>
	private static string WhyNot(PlayerController player, Vector3 point)
	{
		var cam = player.CameraRig.Camera;
		Vector3 to = point - cam.GlobalPosition;
		float ang = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp((-cam.GlobalBasis.Z).Dot(to.Normalized()), -1f, 1f)));
		var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, point, 1u);
		q.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
		var hit = cam.GetWorld3D().DirectSpaceState.IntersectRay(q);
		string by = hit.Count == 0 ? "clear" : $"{(hit["collider"].AsGodotObject() as Node)?.GetPath()} at {cam.GlobalPosition.DistanceTo((Vector3)hit["position"]):0.0} m";
		return $"{to.Length():0.0} m, {ang:0} deg off, {by}";
	}

	private bool Sees(PlayerController p, FallingTree t) =>
		InSight(p, t.TrunkPoint(0.12f), SightRange, SightCone, t) || InSight(p, t.TrunkPoint(0.3f), SightRange, SightCone, t) || InSight(p, t.TrunkPoint(0.5f), SightRange, SightCone, t)
		|| InSight(p, t.TrunkPoint(0.7f), SightRange, SightCone, t);

	private bool SeesFir(PlayerController p) =>
		InSight(p, _fir.TrunkPoint(0.3f), SightRange, SightCone, _fir) || InSight(p, _fir.TrunkPoint(0.55f), SightRange, SightCone, _fir)
		|| InSight(p, _fir.TrunkPoint(0.8f), SightRange, SightCone, _fir);

	/// <summary>Never within reach: the player well clear of where it will lie, and of its foot.</summary>
	private static bool Clear(Vector3 player, Vector3 a, Vector3 b, float margin)
	{
		var q = Flat(player);
		return DistToSeg(q, Flat(a), Flat(b)) > margin && q.DistanceTo(Flat(a)) > margin + 1f;
	}

	// ------------------------------------------------------------------ the way

	private double _clock, _nextGroan;
	private int _groans;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1013 };

	public override void _Process(double delta)
	{
		if (_trees.Count == 0 || GetTree().GetFirstNodeInGroup("player") is not PlayerController player) return;
		float dt = (float)delta;
		_clock += delta;
		Vector3 pp = player.GlobalPosition;
		float sp = LineNear(Flat(pp), out float off, out Vector2 lineDir);
		// how fast they are heading off the way (+ out to the left of it, - out to the right)
		float outward = new Vector2(player.Velocity.X, player.Velocity.Z).Dot(new Vector2(lineDir.Y, -lineDir.X));
		Progress = sp; OffLine = off;
		bool onTheWay = Mathf.Abs(off) < OnTheWay;

		UpdateFir(player, sp, onTheWay);
		// it all begins with the dead fir (or with a player who went round it while it stood)
		bool begun = _fir == null || !(_fir.Standing || _fir.Held) || (sp > 18f && onTheWay);

		// (the owner, 2026-10-15: "once the tree falling is triggered they should all fall, instead of waiting for the
		// player to walk further") once the fir is down and the stop is over, every wall tree still standing comes down
		// after it, one after another along the way. The behind trees still wait to close the way behind them.
		// (2026-10-15, second pass: "some trees are still falling late") from the moment the fir lands, not once the stop is over
		if (!_allGo && begun && (_fir == null || _fir.Fallen || (_fir.Standing && !_fir.Held)))
		{
			_allGo = true;
			double at = _clock + 0.4;
			var rest = _trees.FindAll(e => e.Kind == Kind.Side && !e.Tree.Down && (!e.Tree.Falling || e.Tree.Held));
			rest.Sort((a, b) => a.At.CompareTo(b.At));
			foreach (var e in rest) { e.GoAt = at; at += _rng.RandfRange(0.5f, 0.9f); }
			GD.Print($"[story] Act 1: the rest of the walls come down ({rest.Count})");
		}

		SideDown = BehindDown = WardDown = 0;
		Entry groaner = null; float groanerD = 30f;
		foreach (var e in _trees)
		{
			var t = e.Tree;
			if (t.Down) { if (e.Kind == Kind.Behind) BehindDown++; else if (e.Kind == Kind.Side) SideDown++; else WardDown++; continue; }
			if (t.Falling && !t.Held) continue;
			if (!begun || (_watching && e.Kind == Kind.Side && _cascade.Contains(e))) continue;
			var (la, lb) = t.LieSegment;
			switch (e.Kind)
			{
				case Kind.Behind:
					// heard more than seen: once they're well past, on the way
					if (onTheWay && sp >= e.At + BehindTrigger && Clear(pp, la, lb, 5f)) { t.QuietCreak = _rng.Randf() < 0.6f; t.Fall(); BehindStarted++; Log(e, "behind them"); }
					break;
				case Kind.Side:
					if (e.GoAt >= 0)
					{
						// (all of it begun: each in its turn, wherever they are; still never on them, nor shutting them out)
						// (only now and then a creak: the rest come down on their cracks; 2026-10-15, "we dont need to hear every tree creak")
						if (!t.Falling) { if (_clock >= e.GoAt) { t.QuietCreak = _rng.Randf() < 0.75f; Begin(e, 0.5f + 0.1f * (e.At % 3f)); } }
						else if (t.Held && t.Motion.CreakTime >= t.Motion.CreakSeconds && Clear(pp, la, lb, 4.5f) && !Outside(e))
						{
							bool seen = Sees(player, t);
							t.Release();
							if (seen) SeenFalls++; else UnseenFalls++;
							Log(e, seen ? "in view, the rest following" : "the rest following");
						}
						break;
					}
					if (!t.Falling)
					{
						// (not one already well behind them: come in from the side, they never saw it, and it stays standing)
						if (onTheWay && !_watching && sp >= e.At - AheadTrigger && sp <= e.At + 8f)
							Begin(e, 2.3f + 0.1f * (e.At % 3f));
					}
					else TryRelease(e, player, sp, SideHoldMax, sp >= e.At - 4f);
					break;
				case Kind.Ward:
					if (!t.Falling)
					{
						// heading off the way on its side, out toward it (still well short of
						// where it will lie, so it comes down across their way in front of them)
						float toLie = DistToSeg(Flat(pp), Flat(la), Flat(lb));
						if (Mathf.Sign(off) == e.Side && Mathf.Abs(off) > 2f && outward * e.Side > 0.8f && toLie > 7f && toLie < 22f
							&& Mathf.Abs(sp - e.At - 8f) < 16f)
						{
							Begin(e, 0.8f);
							WardStarted++;
						}
					}
					else TryRelease(e, player, sp, float.MaxValue, false);
					break;
			}
			if (t.Held && !_cascade.Contains(e))
			{
				float d = pp.DistanceTo(t.GlobalPosition);
				if (d < groanerD) { groanerD = d; groaner = e; }
			}
		}
		// a held tree groans again now and then while it waits (only the nearest, and not on and on)
		if (groaner != null && _clock >= _nextGroan && _groans < MaxGroans)
		{
			TreeFallMotion.Groan(groaner.Tree, groaner.Tree.GlobalPosition, groaner.Tree.Seed, _groans++);
			_nextGroan = _clock + _rng.RandfRange(6.5f, 11f);
		}
		UpdateWatch(player, dt);
	}

	private void Begin(Entry e, float creak)
	{
		e.Tree.Fall(true, creak);
		e.HeldAt = _clock;
		if (e.Kind == Kind.Side) SideStarted++;
		// (the first groan only once its own creak is over)
		_nextGroan = Math.Max(_nextGroan, _clock + creak + 1.5f);
	}

	/// <summary>A held tree comes down when it's seen and the player is clear of it; or, the trees beside the way, once
	/// the long wait is over and the player is alongside (the wall has to be there).</summary>
	private bool TryRelease(Entry e, PlayerController player, float sp, float holdMax, bool fallbackOk)
	{
		var t = e.Tree;
		if (!t.Held || t.Motion.CreakTime < t.Motion.CreakSeconds) return false;
		var (la, lb) = t.LieSegment;
		if (!Clear(player.GlobalPosition, la, lb, e.Kind == Kind.Ward ? 6.5f : 4.5f)) return false;
		// a wall never comes down with them on its far side, shutting them out of the way
		if (e.Kind == Kind.Side && Outside(e)) return false;
		bool seen = Sees(player, t);
		if (!seen && !(fallbackOk && _clock - e.HeldAt > holdMax)) return false;
		t.Release();
		if (seen) SeenFalls++; else UnseenFalls++;
		Log(e, seen ? "in view" : "unseen, the wait over");
		if (e.Kind == Kind.Ward && !_watching) BeginWatch(player, t);
		return true;
	}

	/// <summary>The player is out past where this wall tree will lie (alongside it, on its side of the way, further off).</summary>
	private bool Outside(Entry e) => Mathf.Sign(OffLine) == e.Side && Mathf.Abs(OffLine) > SideOffset - 1f && Progress > e.At - 3f;

	private void Log(Entry e, string how) => GD.Print($"[story] Act 1: a tree comes down {(e.Kind == Kind.Behind ? "behind them" : e.Kind == Kind.Side ? "beside the way" : "out in the woods, across a stray's way")} ({e.At:0} m along, {how})");

	// ------------------------------------------------------------------ the dead fir

	private double _firHeldAt = -1;
	private bool _allGo;
	/// <summary>How many groans a waiting tree gives in all (it was 14 every ~4 s: the owner heard every one, 2026-10-15).</summary>
	private const int MaxGroans = 4;

	private void UpdateFir(PlayerController player, float sp, bool onTheWay)
	{
		if (_fir == null || !_fir.Directed) return;
		Vector3 pp = player.GlobalPosition;
		float d = Flat(pp).DistanceTo(Flat(_fir.Foot));
		if (_fir.Standing)
		{
			// its creak, as they come near: and (2026-10-15, the owner: "fix tree falling trigger, i was able to run past it")
			// the stop and the turn begin with it, so nobody runs on past before it comes down
			if (d < FirCreakRange)
			{
				_fir.StartCreak(true);
				_firHeldAt = _clock;
				_nextGroan = Math.Max(_nextGroan, _clock + 4.5);
				if (!_watching) BeginWatch(player);
			}
			return;
		}
		if (!_fir.Held || _fir.CreakTime < FirCreakBeforeFall) return;
		var (la, lb) = _fir.LieSegment;
		// a short creak and it goes, wherever they are and wherever they look (the turn brings it into view); only never on
		// top of them (Dan, 2026-10-13)
		if (!Clear(pp, la, lb, 6.5f)) { FirGroan(d); return; }
		bool seen = SeesFir(player);
		_fir.Release();
		FirSeen = seen;
		if (seen) SeenFalls++; else UnseenFalls++;
		GD.Print($"[story] Act 1: the dead fir cracks ({(seen ? "in view" : "turning to it")}, {d:0} m off)");
		if (!_watching) BeginWatch(player);
	}

	private void FirGroan(float d)
	{
		if (d > 34f || _clock < _nextGroan || _groans >= MaxGroans) return;
		TreeFallMotion.Groan(_fir, _fir.Foot, _fir.Seed, _groans++);
		_nextGroan = _clock + _rng.RandfRange(6f, 10f);
	}

	// ------------------------------------------------------------------ the stop

	private bool _watching, _releasing, _assist;
	private double _watchStart, _quietSince;
	private float _halt;           // 0 = their own pace .. 1 = stopped
	private float _assistLeft;     // the look's pull, eased in and out
	private readonly List<Entry> _cascade = new();
	private int _cascadeAt;
	private double _cascadeBegan, _crackAt = -1;
	private float _lastYaw, _pushedYaw;
	private int _fighting;

	/// <summary>A tree out in the woods cracking across a stray's way stops them too, a shorter stop (no more come down
	/// after it): they pull up as it goes, and watch it land in front of them.</summary>
	private FallingTree _watchTree;

	private void BeginWatch(PlayerController player, FallingTree only = null)
	{
		_watching = true; _releasing = false; _assist = true;
		_watchStart = _clock; _quietSince = -1;
		_cascade.Clear(); _cascadeAt = -1; _crackAt = -1;
		LastWatchFalls = 1;
		WatchCount++;
		_lastYaw = player.CameraRig.Yaw; _pushedYaw = 0f; _fighting = 0;
		_watchTree = only;
		if (only != null) { GD.Print("[story] Act 1: they pull up as it comes down across their way"); return; }
		// the first trees of the walls that aren't down yet, in order along the way
		foreach (var e in _trees)
			if (e.Kind == Kind.Side && !e.Tree.Down && (!e.Tree.Falling || e.Tree.Held)
				&& (_fir == null ? e.Tree.GlobalPosition.DistanceTo(player.GlobalPosition) < 27f : Flat(e.Tree.GlobalPosition).DistanceTo(Flat(_fir.Foot)) < 30f) && NearTheFir(player, e.Tree)) _cascade.Add(e);
		_cascade.Sort((a, b) => a.At.CompareTo(b.At));
		if (_cascade.Count > CascadeCount) _cascade.RemoveRange(CascadeCount, _cascade.Count - CascadeCount);
		GD.Print($"[story] Act 1: they stop to watch ({_cascade.Count} more to come down)");
	}

	/// <summary>A tree that comes down in the stop is one near where they're already looking (within 85 degrees, level, of
	/// the fir as they see it): the pull never swings them round.</summary>
	private bool NearTheFir(PlayerController player, FallingTree t)
	{
		if (_fir == null) return true;
		var eye = Flat(player.GlobalPosition);
		var a = Flat(_fir.LieSegment.a.Lerp(_fir.LieSegment.b, 0.5f)) - eye;
		var b = Flat(t.GlobalPosition + t.FallDir * t.Height * 0.3f) - eye;
		return Mathf.Abs(a.AngleTo(b)) < Mathf.DegToRad(85f);
	}

	/// <summary>What the view is drawn to now: the tree coming down (between where it is and where it will lie), or the
	/// one creaking next.</summary>
	private Vector3? Focus()
	{
		if (_watchTree != null)
		{
			var (wa, wb) = _watchTree.LieSegment;
			return _watchTree.TrunkPoint(0.45f).Lerp(wa.Lerp(wb, 0.45f) + Vector3.Up * 1.5f, 0.5f);
		}
		// (2026-10-15, the owner: "only have him look in the direction of the falling trees not at each one") one way: toward
		// the fir coming down across the trail, and the walls beyond it; held there while they fall, never swung tree to tree
		if (_fir == null) return null;
		return _fir.TrunkPoint(0.45f).Lerp(_fir.LieSegment.a.Lerp(_fir.LieSegment.b, 0.5f) + Vector3.Up * 1.5f, 0.5f);
	}

	private void UpdateWatch(PlayerController player, float dt)
	{
		if (!_watching && _halt <= 0f) return;
		var input = player.PlayerInput;
		if (_watching)
		{
			float span = (float)(_clock - _watchStart);
			bool cut = input == null || !input.Enabled || span > WatchMax;
			if (!_releasing && !cut) StepCascade(player);
			if (cut) EndWatch("cut short");
		}
		// their pace: dies away as if startled, comes back slowly
		float goal = _watching && !_releasing ? 1f : 0f;
		_halt = Mathf.MoveToward(_halt, goal, dt / Mathf.Max(goal > _halt ? HaltSeconds : ResumeSeconds, 0.05f));
		player.WadeScale = 1f - Mathf.SmoothStep(0f, 1f, _halt);
		if (_halt <= 0f && !_watching) player.WadeScale = 1f;
		// the view drawn gently to the fall (never a lock: look away and it lets go)
		_assistLeft = Mathf.MoveToward(_assistLeft, _watching && !_releasing && _assist ? 1f : 0f, dt / 0.5f);
		var rig = player.CameraRig;
		if (rig == null) return;
		// what the player turned on their own since the last frame (what moved, less what was pushed)
		float own = Mathf.AngleDifference(_lastYaw, rig.Yaw) - _pushedYaw;
		_pushedYaw = 0f;
		if (_assistLeft > 0f && Focus() is { } f && rig.Camera is { } cam)
		{
			var d = f - cam.GlobalPosition;
			float wantYaw = Mathf.Atan2(-d.X, -d.Z);
			float wantPitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -0.2f, 0.35f);
			float dy = Mathf.AngleDifference(rig.Yaw, wantYaw), dp = wantPitch - rig.Pitch;
			// turning hard away from it: they want to look elsewhere, and it lets them
			if (Mathf.Abs(own) > 1.1f * dt && Mathf.Sign(own) != Mathf.Sign(dy) && Mathf.Abs(dy) > 0.2f) _fighting++;
			else _fighting = Mathf.Max(0, _fighting - 1);
			// (no letting go any more: the first fall turns them to it wherever they stand, 2026-10-15)
			float ang = Mathf.Sqrt(dy * dy + dp * dp);
			if (ang > Mathf.DegToRad(3f))
			{
				float step = Mathf.Min(ang * (1f - Mathf.Exp(-AssistRate * dt)), AssistMax * dt) * _assistLeft;
				var look = new Vector2(dy, dp) / ang * step;
				input?.AddCutsceneLook(look);
				_pushedYaw = look.X;
			}
		}
		_lastYaw = rig.Yaw;
	}

	private void StepCascade(PlayerController player)
	{
		if (_watchTree != null)
		{
			if (!_watchTree.Down) return;
			if (_quietSince < 0) _quietSince = _clock;
			if (_clock - _quietSince > 0.3) EndWatch("it's down");
			return;
		}
		// the fir coming down: a moment in, the next one begins to creak (it goes once the fir has landed)
		if (_cascadeAt < 0)
		{
			if (_fir != null && !_fir.Fallen && _clock - _watchStart < 2.0) return;
			_cascadeAt = 0;
			_cascadeBegan = -1;
		}
		if (_cascadeAt >= _cascade.Count)
		{
			// the last one on its way down: their pace comes back as it lands
			if (_cascade.Count == 0 && _fir != null && !_fir.Fallen) return;
			if (_quietSince < 0) _quietSince = _clock;
			if (_clock - _quietSince > 0.4) EndWatch("the last one coming down");
			return;
		}
		var e = _cascade[_cascadeAt];
		var t = e.Tree;
		if (_cascadeBegan < 0)
		{
			// the next one: a short creak (it's already been given), then it goes as soon as it's in view
			if (!t.Falling) { t.Fall(true, 0.7f); e.HeldAt = _clock; SideStarted++; }
			_cascadeBegan = _clock;
			_quietSince = -1;
		}
		if (t.Held)
		{
			if (_fir != null && !_fir.Fallen) { _cascadeBegan = _clock; return; }
			if (t.Motion.CreakTime < t.Motion.CreakSeconds) return;
			var (la, lb) = t.LieSegment;
			// (2026-10-15, second pass: the view held one way, toward the falls) it goes once its short creak is over, whether
			// or not it's in the middle of the view; never on them, nor shutting them out
			if (Clear(player.GlobalPosition, la, lb, 4.5f) && !Outside(e))
			{
				bool seen = Sees(player, t);
				t.Release();
				_crackAt = _clock;
				if (seen) SeenFalls++; else UnseenFalls++;
				LastWatchFalls++;
				Log(e, seen ? "in view, while they watch" : "while they watch");
			}
			else if (_clock - _cascadeBegan > 3.5)
			{
				// not to be seen from here: the stop ends, and it comes down later, as they go on
				GD.Print($"[story] Act 1: the next tree ({e.At:0} m along) can't be seen from here: low {WhyNot(player, t.TrunkPoint(0.12f))}; mid {WhyNot(player, t.TrunkPoint(0.5f))}");
				_cascade.RemoveRange(_cascadeAt, _cascade.Count - _cascadeAt);
				EndWatch("the next one out of sight");
			}
			return;
		}
		// it's going: the next one creaks a beat after this one's crack
		if (_crackAt < 0) _crackAt = _clock;
		if (_clock - _crackAt > 0.9) { _cascadeAt++; _cascadeBegan = -1; _crackAt = -1; _quietSince = -1; }
	}

	private void EndWatch(string why)
	{
		if (!_watching) return;
		LastWatchSeconds = (float)(_clock - _watchStart);
		_watching = false;
		_watchTree = null;
		_releasing = false;
		_cascade.Clear();
		GD.Print($"[story] Act 1: they go on ({why}, stopped {LastWatchSeconds:0.0} s, {LastWatchFalls} came down)");
	}

	public override void _ExitTree()
	{
		if ((_watching || _halt > 0f) && GetTree()?.GetFirstNodeInGroup("player") is PlayerController p) p.WadeScale = 1f;
	}
}
