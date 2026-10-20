using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

public partial class ForestScatter
{
	/// <summary>Act 1's trail as a corridor through the woods (2026-10-15, the owner: "add more trees to act 1 so you can't
	/// wander too far from the path, but don't overdo it, especially in spots where there are objects and other props or
	/// characters"; "you can add trees, shrubs, logs, stumps, whatever is needed"). On top of the woods that are there, the
	/// ground away from the ways (the trail, its live branches, the line from the dead fir to the stairs) fills in with
	/// distance: nothing added within <see cref="CorridorStart"/>, then young firs, saplings, bramble mounds, deadfall,
	/// stumps and brush thickening to <see cref="CorridorFull"/>, past which it's hard going; its inner side wanders and it
	/// leaves gaps, so it never reads as a wall. Kept clear: the road and the lot, the stream and its banks, the footbridge,
	/// the stairs' clearing, the clear zones, every placed thing (props, signs, birds, photo subjects, the doe's glade,
	/// frogs, squirrels, pickups, the waterfall) with a lane from it to the trail so it's still seen from there, and every
	/// falling tree's foot and the ground it will lie on (the walls, the ones behind, the strays' trees).</summary>
	[Export] public bool Corridor = false;
	/// <summary>Distance (m) from the nearest way where the filling starts, and where it's at its thickest; both wander
	/// by up to <see cref="CorridorWander"/>.</summary>
	[Export] public float CorridorStart = 12f, CorridorFull = 28f, CorridorWander = 4f;
	/// <summary>How far out (m) it goes before it leaves the woods as they were.</summary>
	[Export] public float CorridorOuter = 60f;
	/// <summary>How full a cell is at its thickest (on the 1.7 m grid).</summary>
	[Export] public float CorridorDensity = 0.72f;
	/// <summary>(2026-10-19, the level pass: the lookout-to-bunker walk was open woods to drift off in) only along the trail
	/// between the markers of these two groups (each's nearest trail point), easing in and out over
	/// <see cref="CorridorEase"/> metres; empty: the whole trail, as in Act 1.</summary>
	[Export] public string CorridorFromGroup = "", CorridorToGroup = "";
	[Export] public float CorridorEase = 25f;
	public int CorridorCount { get; private set; }
	private float _corA = float.NegativeInfinity, _corB = float.PositiveInfinity;

	/// <summary>The stretch of trail the corridor fills (along, metres), from the marker groups; whole trail if unset.</summary>
	private void CorridorRange()
	{
		_corA = float.NegativeInfinity; _corB = float.PositiveInfinity;
		if (string.IsNullOrEmpty(CorridorFromGroup) || string.IsNullOrEmpty(CorridorToGroup)) return;
		if (GetTree().GetFirstNodeInGroup(CorridorFromGroup) is not Node3D a || GetTree().GetFirstNodeInGroup(CorridorToGroup) is not Node3D b) { _corB = float.NegativeInfinity; return; }
		_terrain.TrailDistance(a.GlobalPosition.X, a.GlobalPosition.Z, out float sa);
		_terrain.TrailDistance(b.GlobalPosition.X, b.GlobalPosition.Z, out float sb);
		_corA = Mathf.Min(sa, sb); _corB = Mathf.Max(sa, sb);
	}

	/// <summary>0..1 how much of the corridor a point at <paramref name="along"/> metres of trail gets.</summary>
	private float CorridorWeight(float along)
	{
		if (float.IsNegativeInfinity(_corA)) return float.IsNegativeInfinity(_corB) ? 0f : 1f;
		float e = Mathf.Max(CorridorEase, 0.01f);
		return Mathf.Clamp(Mathf.Min(along - _corA, _corB - along) / e, 0f, 1f);
	}

	private readonly List<(Vector2 p, float r, bool lane)> _keep = new();
	private readonly List<(Vector2 a, Vector2 b, float w)> _keepSeg = new();
	private readonly List<Vector2> _line = new();
	private readonly List<(Vector2 a, Vector2 b)> _lanes = new();   // (each kept thing to its nearest trail point, found once)

	private void GatherKeepClear()
	{
		_keep.Clear(); _keepSeg.Clear(); _line.Clear(); _lanes.Clear();
		var scene = GetTree().CurrentScene;
		if (scene == null) return;
		var stack = new Stack<Node>();
		stack.Push(scene);
		while (stack.Count > 0)
		{
			var n = stack.Pop();
			foreach (var c in n.GetChildren()) stack.Push(c);
			if (n is not Node3D n3) continue;
			var p = new Vector2(n3.GlobalPosition.X, n3.GlobalPosition.Z);
			switch (n3)
			{
				case ParkProp: _keep.Add((p, 4.5f, true)); break;
				case PhotoSubject: _keep.Add((p, 6f, true)); break;
				case SignPost: _keep.Add((p, 3.5f, true)); break;
				case Bird: _keep.Add((p, 7f, true)); break;
				case Entities.Deer: _keep.Add((p, 16f, true)); break;
				case Entities.Frog: _keep.Add((p, 4.5f, true)); break;
				case Entities.Squirrel: _keep.Add((p, 6f, false)); break;
				case Pickup: _keep.Add((p, 3.5f, true)); break;
				case Waterfall: _keep.Add((p, 12f, true)); break;
				case StaircaseBuilder: _keep.Add((p, 12f, false)); break;
				case FallenTree ft:
				{
					var (a, b) = ft.LieSegment;
					_keep.Add((new Vector2(ft.Foot.X, ft.Foot.Z), 5f, false));
					_keepSeg.Add((new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), 4f));
					break;
				}
				case FallingTree fl:
				{
					var (a, b) = fl.LieSegment;
					_keep.Add((p, 2.5f, false));
					_keepSeg.Add((new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), 3f));
					break;
				}
				case FriendTrail tr: foreach (var w in tr.Waypoints) _line.Add(w); break;
				case SurveyLot lot:
				{
					// (2026-10-19) R.H.'s arrow trails: a lane down each, from the path to the first tree, tree to tree, and on to
					// the tin, so the filling never stands between one arrow and the next
					foreach (int piece in new[] { 1, 2 })
					{
						var trail = lot.Trail(piece);
						if (trail.Count == 0) continue;
						Vector2 F(Vector3 v) => new(v.X, v.Z);
						_terrain.TrailDistance(trail[0].GlobalPosition.X, trail[0].GlobalPosition.Z, out float a0);
						var start = _terrain.TrailPoint(a0, out _);
						_keepSeg.Add((F(start), F(trail[0].GlobalPosition), 4f));
						for (int i = 1; i < trail.Count; i++) _keepSeg.Add((F(trail[i - 1].GlobalPosition), F(trail[i].GlobalPosition), 4f));
						if (lot.Cache(piece) is { } cache) { _keepSeg.Add((F(trail[^1].GlobalPosition), F(cache.TinWorld), 4f)); _keep.Add((F(cache.TinWorld), 5f, false)); }
					}
					break;
				}
			}
			if (n3.IsInGroup("bridge_marker")) _keep.Add((p, 18f, true));
		}
		foreach (var (p, _, lane) in _keep)
		{
			if (!lane) continue;
			_terrain.TrailDistance(p.X, p.Y, out float along);
			var t3 = _terrain.TrailPoint(along, out _);
			var t = new Vector2(t3.X, t3.Z);
			if (p.DistanceTo(t) < 60f) _lanes.Add((p, t));
		}
	}

	private static float SegDist(Vector2 q, Vector2 a, Vector2 b)
	{
		var ab = b - a;
		float t = ab.LengthSquared() > 1e-6f ? Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
		return q.DistanceTo(a + ab * t);
	}

	/// <summary>Distance to the nearest way the player follows: the trail, a branch while it's still a path, the line on
	/// from the dead fir to the stairs.</summary>
	private float WayDistance(float x, float z, float dT)
	{
		float d = dT;
		float dB = _terrain.SampleBranch(x, z, out float hw);
		if (hw > 0.3f) d = Mathf.Min(d, dB);
		var q = new Vector2(x, z);
		for (int i = 1; i < _line.Count; i++) d = Mathf.Min(d, SegDist(q, _line[i - 1], _line[i]));
		return d;
	}

	private bool KeptClear(Vector2 q)
	{
		foreach (var (p, r, _) in _keep)
			if (q.DistanceSquaredTo(p) < r * r) return true;
		// a lane from each to the trail, so it's still seen from there
		foreach (var (a, b) in _lanes)
			if (SegDist(q, a, b) < 2.8f) return true;
		foreach (var (a, b, w) in _keepSeg)
			if (SegDist(q, a, b) < w) return true;
		return false;
	}

	private void ScatterCorridor()
	{
		if (!Corridor) return;
		GatherKeepClear();
		CorridorRange();
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 71 + 13) };
		var wander = new FastNoiseLite { Seed = Seed + 1307, Frequency = 0.03f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FractalOctaves = 2 };
		var gaps = new FastNoiseLite { Seed = Seed + 1931, Frequency = 0.06f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FractalOctaves = 2 };
		const float c = 1.7f;
		var rect = _terrain.PlayRect;
		float inner = EdgeThicket ? EdgeBand * 0.7f : 0f;   // (the edge thicket has the band along the edge)
		for (float z = rect.Position.Y + c * 0.5f; z < rect.End.Y; z += c)
			for (float x = rect.Position.X + c * 0.5f; x < rect.End.X; x += c)
			{
				float px = x + rng.RandfRange(-0.45f, 0.45f) * c, pz = z + rng.RandfRange(-0.45f, 0.45f) * c;
				float roll = rng.Randf(), kind = rng.Randf();
				float e = Mathf.Min(Mathf.Min(px - rect.Position.X, rect.End.X - px), Mathf.Min(pz - rect.Position.Y, rect.End.Y - pz));
				if (e < inner) continue;
				_terrain.SampleFields(px, pz, out float dT, out float sT, out float dS, out float dR);
				if (dT > CorridorOuter + 20f && _line.Count == 0) continue;
				float stretch = CorridorWeight(sT);
				if (stretch <= 0f) continue;
				float d = WayDistance(px, pz, dT);
				if (d > CorridorOuter) continue;
				float wv = wander.GetNoise2D(px, pz);
				float s0 = CorridorStart + CorridorWander * wv, s1 = CorridorFull + CorridorWander * 1.25f * wv;
				if (d < s0) continue;
				float t = Mathf.SmoothStep(s0, s1, d);
				float p = CorridorDensity * Mathf.Pow(t, 1.3f) * stretch;
				// natural gaps: openings in it here and there (never a hedge)
				if (gaps.GetNoise2D(px, pz) > 0.42f) p *= 0.3f;
				if (roll > p) continue;
				var p2 = new Vector2(px, pz);
				if (dR < _terrain.RoadWidth * 0.5f + 4f || dS < 4.5f || ParkDist(p2) < 10f) continue;
				if (ClearingDist(p2) < 6f || Cleared(p2, 1.5f, false)) continue;
				if (_terrain.RouteDistance(px, pz) < 3f) continue;
				if (KeptClear(p2)) continue;
				float h = _terrain.HeightAt(px, pz);
				float yaw = rng.RandfRange(0, Mathf.Tau);
				var pos = new Vector3(px, h - 0.05f, pz);
				float tint = rng.RandfRange(0.82f, 1.05f);
				if (kind < 0.12f)
				{
					// young firs and the odd snag
					string mesh = kind < 0.025f ? "snag" : kind < 0.09f ? "fir_young" : "fir_mid";
					float trunkR = mesh == "fir_young" ? 0.17f : 0.31f;
					float s = rng.RandfRange(0.75f, 1.05f);
					Add(mesh, TreeChunk, pos, Basis.FromEuler(new Vector3(rng.RandfRange(-0.03f, 0.03f), yaw, rng.RandfRange(-0.03f, 0.03f))).Scaled(Vector3.One * s),
						new Color(tint, tint * 0.98f, tint * 0.97f));
					if (TreeCollision) AddCollider(pos, Cylinder(Mathf.Max(trunkR * s, 0.3f), 6f), new Transform3D(Basis.Identity, pos + new Vector3(0, 2.6f, 0)));
				}
				else if (kind < 0.38f)
				{
					// saplings, a few together
					string mesh = kind < 0.21f ? "sapling_a" : kind < 0.3f ? "sapling_b" : "sapling_c";
					float s = rng.RandfRange(0.8f, 1.25f);
					Add(mesh, TreeChunk, pos, Basis.FromEuler(new Vector3(rng.RandfRange(-0.06f, 0.06f), yaw, rng.RandfRange(-0.06f, 0.06f))).Scaled(Vector3.One * s),
						new Color(tint, tint, tint * 0.97f));
					if (TreeCollision) AddCollider(pos, Cylinder(0.45f * s, 3f), new Transform3D(Basis.Identity, pos + Vector3.Up * 1.4f));
				}
				else if (kind < 0.6f)
				{
					// brambles and shrubs grown over whatever's under them (much of the blocking)
					string mesh = kind < 0.46f ? "vinemound_a" : kind < 0.53f ? "vinemound_b" : "vinemound_c";
					float s = rng.RandfRange(0.85f, 1.35f);
					var b = Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(new Vector3(s, s * rng.RandfRange(0.85f, 1.25f), s));
					var mp = new Vector3(px, Mathf.Min(h, Mathf.Min(_terrain.HeightAt(px + 1f, pz), _terrain.HeightAt(px - 1f, pz))) - 0.1f, pz);
					Add(mesh, TreeChunk, mp, b, Colors.White * rng.RandfRange(0.85f, 1.05f));
					if (TreeCollision) AddCollider(mp, Cylinder(1.0f * s, 2.4f), new Transform3D(Basis.Identity, mp + Vector3.Up * 1.0f));
				}
				else if (kind < 0.76f)
				{
					// deadfall: a trunk down, laid along the slope's lie (the other half of the blocking)
					var yb = Basis.FromEuler(new Vector3(0, yaw, 0));
					float s = rng.RandfRange(0.8f, 1.3f), half = 2.3f * s;
					float hA = _terrain.HeightAt(px + yb.X.X * half, pz + yb.X.Z * half), hB = _terrain.HeightAt(px - yb.X.X * half, pz - yb.X.Z * half);
					var basis = LieAlong(yb.X * (2f * half) + Vector3.Up * (hA - hB), yb.Z);
					var lp = new Vector3(px, Mathf.Min((hA + hB) * 0.5f, h) + 0.1f * s, pz);
					Add(rng.Randf() < 0.45f ? "log_b" : "log", TreeChunk, lp, basis.Scaled(Vector3.One * s), new Color(tint, tint * 0.98f, tint * 0.94f));
					if (TreeCollision) AddCollider(lp, BoxShape(new Vector3(2.5f * s, 0.5f * s, 0.3f * s)), new Transform3D(basis, lp + Vector3.Up * 0.2f));
				}
				else if (kind < 0.84f)
				{
					// stumps
					float s = rng.RandfRange(0.8f, 1.2f);
					Add(rng.Randf() < 0.5f ? "stump" : "stump_b", TreeChunk, pos, Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(Vector3.One * s),
						new Color(tint, tint * 0.97f, tint * 0.93f));
					if (TreeCollision) AddCollider(pos, Cylinder(0.45f * s, 1.0f), new Transform3D(Basis.Identity, pos + Vector3.Up * 0.4f));
				}
				else if (kind < 0.9f)
				{
					// fallen branches, brush underfoot (not solid)
					float s = rng.RandfRange(0.8f, 1.3f);
					Add(rng.Randf() < 0.5f ? "branch" : "branch_b", FoliageChunk, pos + Vector3.Up * 0.05f, Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(Vector3.One * s),
						new Color(tint, tint * 0.97f, tint * 0.93f));
				}
				else
				{
					// fern and grass between
					float s = rng.RandfRange(0.9f, 1.4f);
					Add(rng.Randf() < 0.7f ? "fern" : "grass", FoliageChunk, pos, Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(Vector3.One * s), Colors.White * rng.RandfRange(0.85f, 1.05f));
				}
				CorridorCount++;
			}
		GD.Print($"[build] corridor: {CorridorCount} added, {CorridorStart:0}-{CorridorFull:0} m from the ways (kept clear round {_keep.Count} things, {_keepSeg.Count} tree lanes)");
	}
}
