using System.Collections.Generic;
using Godot;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World;

/// <summary>
/// A big fir blown down across the main trail where it ends: the root plate torn up
/// on one side (with its crater), the trunk lying along the real ground, and the dead
/// crown heaped over the trail beyond, so the way on is plainly blocked. Past the root
/// plate, a rock outcrop and older deadfall run off to the left (<see cref="LeftBarrierLength"/>),
/// so the only way on is round the crown on the right, where the old path's stones
/// start toward the stairs (<see cref="FriendTrail"/>).
///
/// Built in world space from <see cref="Root"/>/<see cref="Tip"/> (world XZ) at
/// runtime; solid (trunk, plate and crown all collide) and keeps the scatter clear
/// around itself (ClearZones, created in _Ready before ForestScatter's deferred build).
/// </summary>
[GlobalClass]
public partial class FallenTree : Node3D
{
	[Export] public Vector2 Root = new(7f, -466f);
	[Export] public Vector2 Tip = new(-16f, -472f);
	[Export] public float TrunkRadius = 0.75f;
	/// <summary>Fraction of the length (from the root) where the dead crown begins.</summary>
	[Export] public float CrownFrom = 0.45f;
	[Export] public int Seed = 19;

	private ForestTerrain _terrain;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		if (_terrain == null) return;
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;

		var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		var axis = Trunk(out Vector3 dir, out Vector3 side);
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		AddChild(body);

		var k = new MeshKit();
		BuildTrunk(k, axis, body);
		BuildRootPlate(k, axis[0], dir, side, rng, body);
		BuildCrown(k, axis, dir, side, rng, body);
		k.Color = Colors.White;
		k.CommitTo(this, "Mesh");
		BuildClearZones(axis);
		if (LeftBarrierLength > 0f) BuildLeftBarrier(axis[0], dir, side);
	}

	/// <summary>
	/// Length (m) of the barrier past the root plate: a rock outcrop and a tangle of older deadfall
	/// running off to the left of the trail's end, solid, so the only way on is round the crown on
	/// the right (where the old path's stones start). 0 = none.
	/// </summary>
	[Export] public float LeftBarrierLength = 26f;

	private void BuildLeftBarrier(Vector3 butt, Vector3 dir, Vector3 side)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 11 + 5) };
		// Away from the crown along the trunk's line, starting just behind the plate; the far end bends
		// forward a little (into the woods), as outcrops follow the lie of the ground.
		Vector3 start = butt - dir * 1.2f;
		Vector3 Along(float d)
		{
			float bend = Mathf.SmoothStep(0.6f, 1f, d / LeftBarrierLength) * 5f;
			Vector3 p = start - dir * d - side * (bend + 0.8f * Mathf.Sin(d * 0.37f));
			p.Y = Ground(p.X, p.Z);
			return p;
		}
		var body = new StaticBody3D { Name = "Barrier", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "rock");
		AddChild(body);
		var k = new MeshKit();
		var rock = ProcTextures.MossRockMat;

		// the outcrop: boulders in a broken chain, big ones with smaller ones heaped against them
		var stones = new List<(Vector3 c, Vector3 r)>();
		for (float d = 0f; d <= LeftBarrierLength; d += rng.RandfRange(1.3f, 2.0f))
		{
			Vector3 p = Along(d) + side * rng.RandfRange(-0.6f, 0.6f);
			float r = rng.RandfRange(0.8f, 1.45f) * (d < 3f ? 1.2f : 1f);
			float sh = rng.RandfRange(1.2f, 1.55f);   // weathered, lichen-pale: it has to read in the gloom
			k.Color = new Color(sh, sh, sh * 0.97f);
			var radii = new Vector3(r * rng.RandfRange(0.9f, 1.3f), r * rng.RandfRange(0.75f, 0.95f), r);
			k.Mat(rock).Blob(p + Vector3.Up * r * 0.45f, radii, Seed * 31 + (int)(d * 10), 0.22f, true, 0.7f, 0.3f);
			stones.Add((p + Vector3.Up * r * 0.45f, radii));
			if (rng.Randf() < 0.6f)
			{
				float r2 = r * rng.RandfRange(0.45f, 0.7f);
				Vector3 q = p + side * rng.RandfRange(-1f, 1f) * r + dir * rng.RandfRange(-0.6f, 0.6f);
				q.Y = Ground(q.X, q.Z);
				k.Blob(q + Vector3.Up * r2 * 0.4f, new Vector3(r2 * 1.2f, r2 * 0.8f, r2), Seed * 37 + (int)(d * 10), 0.22f, true, 0.7f, 0.3f);
				stones.Add((q + Vector3.Up * r2 * 0.4f, new Vector3(r2 * 1.2f, r2 * 0.8f, r2)));
			}
		}

		// the highest thing a log end of radius rr can rest on at p: a boulder's top (sunk into it a little) or the ground
		float RestHeight(Vector3 p, float rr)
		{
			float h = Ground(p.X, p.Z) + rr * 0.75f;
			foreach (var (c, rad) in stones)
			{
				float dx = (p.X - c.X) / rad.X, dz = (p.Z - c.Z) / rad.Z;
				float t = dx * dx + dz * dz;
				if (t >= 0.8f) continue;   // the blobs are jittered: only their solid middle counts
				h = Mathf.Max(h, c.Y + rad.Y * 0.78f * Mathf.Sqrt(1f - t) + rr * 0.5f);
			}
			return h;
		}

		// the deadfall: older trunks, grey and broken, lying across and along the outcrop, propped on it
		k.Mat(ProcTextures.BarkMat);
		for (int i = 0; i < 9; i++)
		{
			float d0 = rng.RandfRange(0f, LeftBarrierLength - 3f);
			float len = rng.RandfRange(4f, 8f);
			Vector3 a = Along(d0) + side * rng.RandfRange(-1.8f, 1.8f);
			float ang = rng.RandfRange(-0.7f, 0.7f);   // mostly along the line, some across it
			Vector3 heading = (-dir * Mathf.Cos(ang) + side * Mathf.Sin(ang)).Normalized();
			Vector3 b = a + heading * len;
			a.Y = Ground(a.X, a.Z) + rng.RandfRange(0.3f, 1.3f);
			b.Y = Ground(b.X, b.Z) + rng.RandfRange(0.2f, 0.7f);
			float r = rng.RandfRange(0.2f, 0.38f);
			// Propped where the outcrop is under it, else lying on the ground: never held up by nothing.
			a.Y = Mathf.Min(a.Y, RestHeight(a, r));
			b.Y = Mathf.Min(b.Y, RestHeight(b, r * 0.55f));
			float g = rng.RandfRange(0.9f, 1.05f);   // silver-grey, like the fir
			k.Color = new Color(g, g * 0.95f, g * 0.9f);
			BunkerKit.Tube(k, new List<Vector3> { a, a.Lerp(b, 0.5f) + Vector3.Up * rng.RandfRange(-0.1f, 0.15f), b }, r, r * 0.55f, 6);
			// snapped limbs sticking up out of it
			for (int j = 0; j < 3; j++)
			{
				Vector3 from = a.Lerp(b, rng.RandfRange(0.15f, 0.9f));
				Vector3 to = from + new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(0.6f, 1.5f), rng.RandfRange(-0.6f, 0.6f));
				BunkerKit.Tube(k, new List<Vector3> { from, to }, rng.RandfRange(0.04f, 0.08f), 0.015f, 4);
			}
		}
		k.Color = Colors.White;
		k.CommitTo(this, "BarrierMesh");

		// Solid: a chain of boxes along the line, too tall to step over, thick enough not to squeeze through.
		const float step = 1.5f;
		for (float d = -0.5f; d < LeftBarrierLength + 1f; d += step)
		{
			Vector3 a = Along(Mathf.Max(0f, d)), b = Along(d + step);
			Vector3 mid = (a + b) * 0.5f;
			Vector3 fwd = b - a; fwd.Y = 0;
			if (fwd.LengthSquared() < 0.001f) continue;
			body.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(2.4f, 3f, fwd.Length() + 0.4f) },
				Transform = new Transform3D(Basis.LookingAt(fwd.Normalized(), Vector3.Up), mid + Vector3.Up * 1.2f),
			});
		}
		// keep the scatter's trees out of the rocks
		for (float d = 0f; d <= LeftBarrierLength; d += 3f)
		{
			var cz = new ClearZone { Radius = 2.2f, ClearFoliage = false, Name = $"ClearBarrier{(int)d}" };
			AddChild(cz);
			cz.GlobalPosition = Along(d);
		}
	}

	private float Ground(float x, float z) => _terrain.HeightAt(x, z);

	/// <summary>Trunk centreline, root to tip, resting on the ground along its length
	/// (propped a little higher at the root by the plate), tapering toward the tip.</summary>
	private List<Vector3> Trunk(out Vector3 dir, out Vector3 side)
	{
		var pts = new List<Vector3>();
		const int n = 12;
		for (int i = 0; i <= n; i++)
		{
			float t = (float)i / n;
			var p = Root.Lerp(Tip, t);
			float r = Mathf.Lerp(TrunkRadius, TrunkRadius * 0.3f, t);
			// Propped off the ground: high at the butt (the plate holds it up), still clear of the
			// ground where it crosses the trail (resting on its own snapped branches), down at the crown.
			float lift = Mathf.Lerp(1.1f, 0.15f, Mathf.SmoothStep(0f, 1f, t));
			pts.Add(new Vector3(p.X, Ground(p.X, p.Y) + r * 0.85f + lift, p.Y));
		}
		// Never float over a dip: ease sharp drops so the trunk sags across them like a real log would.
		for (int pass = 0; pass < 2; pass++)
			for (int i = 1; i < n; i++)
			{
				float mid = (pts[i - 1].Y + pts[i + 1].Y) * 0.5f;
				if (pts[i].Y < mid - 0.3f) pts[i] = new Vector3(pts[i].X, mid - 0.3f, pts[i].Z);
			}
		var d2 = (Tip - Root).Normalized();
		dir = new Vector3(d2.X, 0, d2.Y);
		side = dir.Cross(Vector3.Up).Normalized();
		return pts;
	}

	private void BuildTrunk(MeshKit k, List<Vector3> axis, StaticBody3D body)
	{
		k.Mat(ProcTextures.BarkMat);
		// weathered silver-grey: a long-dead tree, so it reads pale against the dark forest floor
		k.Color = new Color(0.95f, 0.9f, 0.84f);
		BunkerKit.Tube(k, axis, TrunkRadius, TrunkRadius * 0.3f, 9);
		// broken branch stubs all along the trunk, the ones on top sticking up, the lower ones propping it
		var srng = new RandomNumberGenerator { Seed = (ulong)(Seed * 3 + 1) };
		var tdir = (axis[^1] - axis[0]).Normalized();
		var tside = tdir.Cross(Vector3.Up).Normalized();
		for (int i = 1; i < axis.Count - 1; i++)
			for (int s = 0; s < 3; s++)
			{
				float t = (float)i / (axis.Count - 1);
				float r = Mathf.Lerp(TrunkRadius, TrunkRadius * 0.3f, t);
				float a = srng.RandfRange(0f, Mathf.Tau);
				var o = (tside * Mathf.Cos(a) + Vector3.Up * Mathf.Sin(a)).Normalized();
				var p = axis[i].Lerp(axis[i + 1], srng.Randf()) + o * r * 0.8f;
				var end = p + (o + tdir * 0.3f).Normalized() * srng.RandfRange(0.4f, 1.3f);
				float g = Ground(end.X, end.Z);
				if (end.Y < g) end.Y = g;   // a prop into the ground
				BunkerKit.Tube(k, new List<Vector3> { p, end }, srng.RandfRange(0.06f, 0.12f), 0.02f, 4);
			}

		// Collision: a box per segment, tall enough that the trunk can't be stepped over.
		for (int i = 0; i < axis.Count - 1; i++)
		{
			var a = axis[i]; var b = axis[i + 1];
			float r = Mathf.Lerp(TrunkRadius, TrunkRadius * 0.3f, (i + 0.5f) / (axis.Count - 1));
			var mid = (a + b) * 0.5f;
			var fwd = (b - a).Normalized();
			var basis = Basis.LookingAt(fwd, Vector3.Up);
			float gy = Ground(mid.X, mid.Z);
			float top = Mathf.Max(mid.Y + r, gy + 1.3f);
			body.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(r * 2f + 0.2f, top - gy + 0.5f, a.DistanceTo(b) + 0.1f) },
				Transform = new Transform3D(basis, new Vector3(mid.X, (top + gy - 0.5f) * 0.5f, mid.Z)),
			});
		}
	}

	/// <summary>The upturned root plate: a disc of earth and roots standing on edge at the butt,
	/// facing along the trunk, with the crater it tore out of the ground just behind it.</summary>
	private void BuildRootPlate(MeshKit k, Vector3 butt, Vector3 dir, Vector3 side, RandomNumberGenerator rng, StaticBody3D body)
	{
		Vector3 c = butt - dir * 0.4f + Vector3.Up * 0.6f;
		// Blob radii are in the kit's space: build it in a frame whose X is the trunk axis.
		var frame = new Basis(dir, Vector3.Up, side);   // right-handed: dir x up = side
		k.Xf = new Transform3D(frame, c);
		k.Mat(BunkerTextures.EarthMat);
		k.Color = new Color(0.75f, 0.7f, 0.66f);
		k.Blob(Vector3.Zero, new Vector3(0.55f, 2.3f, 2.6f), Seed + 5, 0.28f, false);
		// roots bristling out of the plate
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.5f, 0.44f, 0.38f);
		for (int i = 0; i < 16; i++)
		{
			float a = rng.RandfRange(0f, Mathf.Tau);
			var o = new Vector3(-0.2f, Mathf.Sin(a) * rng.RandfRange(1.2f, 1.9f), Mathf.Cos(a) * rng.RandfRange(1.3f, 2.1f));
			var pts = new List<Vector3> { o * 0.7f, o, o + new Vector3(-rng.RandfRange(0.2f, 0.6f), rng.RandfRange(-0.3f, 0.3f), 0) + o.Normalized() * rng.RandfRange(0.3f, 0.8f) };
			BunkerKit.Tube(k, pts, rng.RandfRange(0.05f, 0.11f), 0.015f, 5);
		}
		k.Xf = Transform3D.Identity;

		// the crater: a dark, shallow scoop of torn earth behind the plate
		Vector3 hole = butt - dir * 1.6f;
		hole.Y = Ground(hole.X, hole.Z) - 0.25f;
		k.Mat(BunkerTextures.EarthMat);
		k.Color = new Color(0.45f, 0.4f, 0.37f);
		k.Xf = new Transform3D(frame, hole);
		k.Blob(Vector3.Zero, new Vector3(1.3f, 0.32f, 1.9f), Seed + 9, 0.2f, true);
		k.Xf = Transform3D.Identity;

		body.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(1.2f, 4f, 5.2f) },
			Transform = new Transform3D(frame, c + Vector3.Up * 0.2f),
		});
	}

	/// <summary>The dead crown: grey, needle-less branches heaped over the trail beyond,
	/// denser and more tangled toward the tip, too thick to push through.</summary>
	private void BuildCrown(MeshKit k, List<Vector3> axis, Vector3 dir, Vector3 side, RandomNumberGenerator rng, StaticBody3D body)
	{
		k.Mat(ProcTextures.BarkMat);
		int n = axis.Count - 1;
		for (int i = Mathf.FloorToInt(n * CrownFrom); i <= n; i++)
		{
			float t = (float)i / n;
			Vector3 p = axis[i];
			int count = 5 + (int)(t * 4f);
			for (int b = 0; b < count; b++)
			{
				float a = rng.RandfRange(0f, Mathf.Tau);
				float len = Mathf.Lerp(3.2f, 1.6f, t) * rng.RandfRange(0.7f, 1.15f);
				// Branches splay round the trunk; those pointing into the ground are bent along it instead.
				var outDir = (side * Mathf.Cos(a) + Vector3.Up * Mathf.Sin(a) * 0.8f + dir * 0.55f).Normalized();
				var end = p + outDir * len;
				float g = Ground(end.X, end.Z) + 0.05f;
				if (end.Y < g) end.Y = g + rng.RandfRange(0f, 0.3f);
				var mid = p.Lerp(end, 0.5f) + Vector3.Up * rng.RandfRange(0.05f, 0.3f);
				float sh = rng.RandfRange(0.8f, 1f);   // silver-grey dead wood
				k.Color = new Color(sh, sh * 0.95f, sh * 0.9f);
				BunkerKit.Tube(k, new List<Vector3> { p, mid, end }, Mathf.Lerp(0.09f, 0.05f, t), 0.012f, 4);
				// a couple of twigs off each branch
				for (int tw = 0; tw < 2; tw++)
				{
					// on the branch as built (it bows up through mid), so the twig grows out of it
					float u = rng.RandfRange(0.45f, 0.85f);
					var from = u < 0.5f ? p.Lerp(mid, u * 2f) : mid.Lerp(end, (u - 0.5f) * 2f);
					var tdir = (outDir + new Vector3(rng.RandfRange(-0.8f, 0.8f), rng.RandfRange(-0.2f, 0.7f), rng.RandfRange(-0.8f, 0.8f))).Normalized();
					BunkerKit.Tube(k, new List<Vector3> { from, from + tdir * rng.RandfRange(0.4f, 0.9f) }, 0.022f, 0.006f, 3);
				}
			}
		}

		// Solid across the crown's spread, so the heap reads (and plays) as a wall of deadfall.
		int from0 = Mathf.FloorToInt(n * CrownFrom);
		for (int i = from0; i < n; i++)
		{
			var mid = (axis[i] + axis[i + 1]) * 0.5f;
			var fwd = (axis[i + 1] - axis[i]).Normalized();
			float gy = Ground(mid.X, mid.Z);
			body.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(4.6f, 2.4f, axis[i].DistanceTo(axis[i + 1]) + 0.2f) },
				Transform = new Transform3D(Basis.LookingAt(fwd, Vector3.Up), new Vector3(mid.X, gy + 0.9f, mid.Z)),
			});
		}
	}

	private void BuildClearZones(List<Vector3> axis)
	{
		for (int i = 0; i < axis.Count; i += 2)
		{
			var cz = new ClearZone { Radius = i == 0 ? 3.6f : 3f, ClearFoliage = false, Name = $"Clear{i}" };
			AddChild(cz);
			cz.GlobalPosition = axis[i];
		}
	}
}
