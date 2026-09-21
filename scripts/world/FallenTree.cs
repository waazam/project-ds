using System.Collections.Generic;
using Godot;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World;

/// <summary>
/// A big fir blown down across the main trail where it ends: the root plate torn up
/// on one side (with its crater), the trunk lying along the real ground, and the dead
/// crown heaped over the trail beyond, so the way on is plainly blocked. The only
/// easy way around is past the root plate, and from there the friend's things lead
/// off into the trackless woods toward the stairs (<see cref="FriendTrail"/>).
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
					var from = p.Lerp(end, rng.RandfRange(0.45f, 0.85f));
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
