using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Act 1 wayfinding from the fallen fir that ends the main trail to the first stairs: the old
/// path's stones, pale against the forest floor, a stop every 3-4.5 m (2-3 m over the last
/// stretch) all the way to the stairs' foot, one or two across, some frost-heaved at an edge,
/// some half sunk, and a crumbled low retaining edge along one side near the end. Nothing of
/// the friend's lies here any more (his things are in the Hollow).
///
/// Purely decorative: no collision, no interaction (nothing blocks the autotest route and
/// nothing changes in the story). Each stone keeps ferns off itself (a small ClearZone,
/// created in _Ready, before ForestScatter's deferred build); the forest stays close either
/// side, so the stairs stay hidden until near.
///
/// <see cref="Waypoints"/> are world XZ (x, z), from the trail's end round the fir's crown to
/// the stairs' foot. The retaining edge runs on the <see cref="EdgeSide"/> side (-1 = left of travel).
/// </summary>
[GlobalClass]
public partial class FriendTrail : Node3D
{
	[Export] public Vector2[] Waypoints = System.Array.Empty<Vector2>();
	[Export] public int EdgeSide = -1;
	/// <summary>Radius kept clear of trees round each belonging, so it can be seen (m). No lane along the line.</summary>
	[Export] public float CorridorRadius = 1.2f;
	/// <summary>Distances (m along the line) of spots kept clear of trees (the friend's belongings used to lie there; they are in the Hollow now).</summary>
	[Export] public float[] ItemAt = System.Array.Empty<float>();
	/// <summary>Keep the edge stones this far from any point of these world XZ points (the autotest route).</summary>
	[Export] public NodePath AvoidPath = "";
	[Export] public int Seed = 71;

	private ForestTerrain _terrain;
	private readonly List<Vector2> _pts = new();
	private readonly List<float> _cum = new();
	private readonly List<Vector2> _avoid = new();

	public float Length => _cum.Count > 0 ? _cum[^1] : 0f;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		if (_terrain == null || Waypoints.Length < 2) return;
		_pts.AddRange(Waypoints);
		_cum.Add(0f);
		for (int i = 1; i < _pts.Count; i++) _cum.Add(_cum[^1] + _pts[i].DistanceTo(_pts[i - 1]));
		if (!AvoidPath.IsEmpty && GetNodeOrNull<Path3D>(AvoidPath) is { Curve: not null } ap)
			foreach (var p in ap.Curve.GetBakedPoints()) { var w = ap.GlobalTransform * p; _avoid.Add(new Vector2(w.X, w.Z)); }

		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		BuildCorridor();
		BuildStonework();
		BuildLineTrigger();
	}

	/// <summary>The player's one thought where the stones begin, so there is a reason to leave the trail.</summary>
	public const string StonesLine = "Old stones. Someone's been this way.";
	public const string StonesLineFlag = "line_old_stones";
	[Export] public float StonesLineAt = 5f;

	private void BuildLineTrigger()
	{
		if (Systems.StoryManager.Instance is { } s0 && (s0.HasFlag(StonesLineFlag) || s0.Current >= Systems.Checkpoint.Act2StairsClimbed)) return;
		Vector2 at = _pts[^1];
		for (int i = 1; i < _pts.Count; i++)
			if (_cum[i] >= StonesLineAt) { at = _pts[i - 1].Lerp(_pts[i], (StonesLineAt - _cum[i - 1]) / Mathf.Max(_cum[i] - _cum[i - 1], 0.01f)); break; }
		var pos = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y), at.Y);
		Systems.StoryBeat.MakeTrigger(this, new CylinderShape3D { Radius = 3.5f, Height = 12f }, pos, p =>
		{
			var s = Systems.StoryManager.Instance;
			if (s == null || s.HasFlag(StonesLineFlag) || s.Current >= Systems.Checkpoint.Act2StairsClimbed) return;
			s.SetFlag(StonesLineFlag);
			// The flag still marks the spot for saves; no line (self-talk removed, Dan 2026-09-22).
		}, "StonesLine");
	}

	// ------------------------------------------------------------------ line helpers

	/// <summary>World XZ at distance s along the line, and the unit travel direction there.</summary>
	public Vector2 At(float s, out Vector2 dir)
	{
		s = Mathf.Clamp(s, 0f, Length);
		for (int i = 1; i < _pts.Count; i++)
			if (s <= _cum[i] || i == _pts.Count - 1)
			{
				float seg = _cum[i] - _cum[i - 1];
				float t = seg > 0 ? (s - _cum[i - 1]) / seg : 0f;
				dir = (_pts[i] - _pts[i - 1]).Normalized();
				return _pts[i - 1].Lerp(_pts[i], Mathf.Clamp(t, 0, 1));
			}
		dir = Vector2.Down;
		return _pts[^1];
	}

	private Vector3 Ground(Vector2 p) => new(p.X, _terrain.HeightAt(p.X, p.Y), p.Y);
	private static Vector2 Left(Vector2 dir) => new(dir.Y, -dir.X);   // left of travel, seen from above (x right, z toward viewer)

	private float AvoidDistance(Vector2 p)
	{
		float best = float.MaxValue;
		foreach (var a in _avoid) best = Mathf.Min(best, a.DistanceSquaredTo(p));
		return Mathf.Sqrt(best);
	}

	/// <summary>Basis sitting on the terrain slope at p, yawed to face yaw (radians about Y).</summary>
	private Basis OnSlope(Vector2 p, float yaw)
	{
		Vector3 n = _terrain.NormalAt(p.X, p.Y);
		Basis yawB = new(Vector3.Up, yaw);
		Vector3 fwd = yawB * Vector3.Back;
		Vector3 right = n.Cross(fwd).Normalized();
		fwd = right.Cross(n).Normalized();
		return new Basis(right, n, fwd);
	}

	private void BuildCorridor()
	{
		foreach (float s in ItemAt)
		{
			var p = At(s, out _);
			var cz = new ClearZone { Radius = CorridorRadius, ClearFoliage = true, Name = $"ClearItem{(int)s}" };
			AddChild(cz);
			cz.GlobalPosition = Ground(p);
		}
	}

	// ------------------------------------------------------------------ stonework

	private void BuildStonework()
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		var k = new MeshKit();
		var slab = StairTextures.ConcreteMat;
		var block = StairTextures.BlockMat;
		var moss = StairTextures.MossMat;
		var mossTint = new Color(0.44f, 0.50f, 0.33f);
		float L = Length;

		// pavers: the old path's stones, pale against the litter, a stop every 3-4.5 m from the fallen
		// fir all the way to the stairs' foot (closer together over the last stretch), so the way can be
		// followed stone to stone. One or two across; some tipped up at an edge, some half sunk.
		int stop = 0;
		for (float s = 0.6f; s < L - 0.3f; stop++)
		{
			float t = s / L;
			var c = At(s, out Vector2 dir);
			Vector2 left = Left(dir);
			bool pair = rng.Randf() < 0.35f + 0.55f * t;
			float jog = rng.RandfRange(-0.35f, 0.35f);   // the stones wander a little either side of the line
			for (int lane = 0; lane < (pair ? 2 : 1); lane++)
			{
				bool sunk = rng.Randf() < 0.22f;
				bool tipped = !sunk && rng.Randf() < 0.28f;
				float w = sunk ? rng.RandfRange(0.45f, 0.6f) : rng.RandfRange(0.62f, 0.8f);
				float d = sunk ? rng.RandfRange(0.38f, 0.5f) : rng.RandfRange(0.5f, 0.64f);
				float across = pair ? (lane - 0.5f) * 0.78f : 0f;
				Vector2 pos = c + left * (jog + across + rng.RandfRange(-0.06f, 0.06f)) + dir * rng.RandfRange(-0.12f, 0.12f);
				float yaw = Mathf.Atan2(dir.X, dir.Y) + rng.RandfRange(-0.18f, 0.18f);
				var b = OnSlope(pos, yaw);
				if (tipped)
				{
					// frost-heaved: one edge lifted 8-15 degrees
					float ang = Mathf.DegToRad(rng.RandfRange(8f, 15f)) * (rng.Randf() < 0.5f ? -1f : 1f);
					b *= rng.Randf() < 0.5f ? Basis.FromEuler(new Vector3(ang, 0, 0)) : Basis.FromEuler(new Vector3(0, 0, ang));
				}
				else b *= Basis.FromEuler(new Vector3(rng.RandfRange(-0.03f, 0.03f), 0, rng.RandfRange(-0.03f, 0.03f)));
				// lip above the litter: 4-7 cm, a tipped one more at its high edge, a sunk one barely
				float lip = sunk ? rng.RandfRange(0.004f, 0.015f) : rng.RandfRange(0.04f, 0.07f);
				Vector3 top = Ground(pos) + b.Y * lip;
				float g = rng.RandfRange(0.5f, 0.62f);
				float m = sunk ? rng.RandfRange(0.25f, 0.45f) : rng.RandfRange(0.05f, 0.2f);
				k.Mat(slab);
				k.Color = new Color(g, g * 0.99f, g * 0.95f).Lerp(mossTint * 0.8f, m);
				BuildKit.Box(k, top - b.Y * 0.08f, new Vector3(w, 0.16f, d), 1f, BuildKit.Face.NY, b);
				// a little moss in the cracks of some
				if (rng.Randf() < 0.3f)
				{
					k.Mat(moss);
					k.Color = mossTint * 0.8f;
					Flat(k, top + b.Y * 0.004f, b, w * rng.RandfRange(0.4f, 0.7f), d * rng.RandfRange(0.4f, 0.8f), rng.Randf() < 0.5f);
				}
				// ferns and grass kept off the stone so it shows
				var cz = new ClearZone { Radius = 0.75f, ClearFoliage = true, Name = $"ClearPaver{stop}_{lane}" };
				AddChild(cz);
				cz.GlobalPosition = Ground(pos);
			}
			// 3-4.5 m apart, closing to 2-3 m over the last 20 m
			s += L - s < 20f ? rng.RandfRange(2f, 3f) : rng.RandfRange(3f, 4.5f);
		}

		// crumbled low retaining edge along one side: starts after a third of the way, rising and closing up toward the stairs
		for (float s = L * 0.7f; s < L - 0.5f; s += 0.7f)
		{
			float t = s / L;
			if (rng.Randf() > 0.05f + 0.7f * t * t) continue;          // gaps where it has collapsed
			var c = At(s, out Vector2 dir);
			Vector2 pos = c + Left(dir) * EdgeSide * -1f * 1.05f;
			if (_avoid.Count > 0 && AvoidDistance(pos) < 1.6f) continue;
			float h = Mathf.Lerp(0.12f, 0.42f, t) * rng.RandfRange(0.6f, 1f);
			bool fallen = rng.Randf() > 0.35f + 0.6f * t;
			float yaw = Mathf.Atan2(dir.X, dir.Y);
			var b = new Basis(Vector3.Up, yaw + (fallen ? rng.RandfRange(-0.6f, 0.6f) : rng.RandfRange(-0.04f, 0.04f)));
			if (fallen) b *= Basis.FromEuler(new Vector3(rng.RandfRange(-0.4f, 0.4f), 0, rng.RandfRange(-0.5f, 0.5f)));
			float gy = _terrain.HeightAt(pos.X, pos.Y);
			Vector3 center = new(pos.X, gy + (fallen ? 0.06f : h * 0.5f - 0.1f), pos.Y);
			float g = rng.RandfRange(0.3f, 0.4f);
			k.Mat(block);
			k.Color = new Color(g, g * 0.99f, g * 0.95f).Lerp(new Color(0.30f, 0.36f, 0.24f), Mathf.Clamp(0.45f - 0.2f * t + rng.RandfRange(-0.1f, 0.1f), 0.1f, 0.7f));
			Vector3 size = new(0.3f, fallen ? 0.22f : h + 0.1f, 0.48f);
			BuildKit.Box(k, center, size, 1f, BuildKit.Face.NY, b);
			if (!fallen && rng.Randf() < 0.55f)
			{
				// coping stone on the intact stretches, mossy on top
				k.Mat(slab);
				k.Color = new Color(0.4f, 0.39f, 0.37f).Lerp(mossTint * 0.6f, 0.55f);
				BuildKit.Box(k, center + b.Y * (size.Y * 0.5f + 0.03f), new Vector3(0.36f, 0.06f, 0.5f), 1f, BuildKit.Face.NY, b);
			}
		}
		k.Color = Colors.White;
		var mi = k.CommitTo(this, "Stonework");
		mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
	}

	/// <summary>Flat alpha-scissor decal quad (moss/leaves) in a basis' XZ plane.</summary>
	private static void Flat(MeshKit k, Vector3 c, Basis b, float sx, float sz, bool flip)
	{
		Vector3 ex = b.X * sx * 0.5f, ez = b.Z * sz * 0.5f;
		Vector2 u0 = flip ? new Vector2(1, 0) : new Vector2(0, 0), u1 = flip ? new Vector2(0, 1) : new Vector2(1, 1);
		k.Quad(c - ex - ez, c + ex - ez, c + ex + ez, c - ex + ez, b.Y,
			new Vector2(u0.X, u0.Y), new Vector2(u1.X, u0.Y), new Vector2(u1.X, u1.Y), new Vector2(u0.X, u1.Y));
	}
}
