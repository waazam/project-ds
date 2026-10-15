using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// A small board-and-batten tool shed near the cabin: stud-framed walls (the frame
/// shows inside), corner posts, a lean-to roof sloping to the back, a framed
/// doorway whose plank door hangs open (nothing blocks entry; the hammer is gated
/// by its own Pickup), a workbench along the back wall with a vise and a few tools,
/// a pegboard of hanging tools, a shelf of tins and jars, and floor clutter.
///
/// Local frame: front (the doorway) faces +Z; y = 0 is the plank floor top. At
/// runtime it grounds itself: floor just above the highest ground under it,
/// fieldstone piers and skirt boards down to the ground, a flight of plank steps
/// from the sill to the ground in front (<see cref="BuildSteps"/>). It stands on a
/// steep hillside (the ground rises ~0.4 m a metre to the east), so in the Hollow it
/// faces south, across the slope toward the cabin: facing west, down the slope, the
/// sill stood 1.05 m over the ground and the ground kept falling. The hammer spot is the child marker "HammerSpot" (shed.tscn,
/// on the bench top, group "spot_hammer"), also <see cref="BenchSpot"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class Shed : Node3D
{
	[Export] public float Width = 2.4f;
	[Export] public float Depth = 2.1f;
	/// <summary>Front (door side) wall height; the back wall is lower-roofed in the old shed, here the roof falls to the back.</summary>
	[Export] public float WallHeight = 2.15f;
	[Export] public bool BuildCollision = true;
	[Export] public bool AutoGround = true;
	[Export] public float EditorGroundDepth = 0.3f;

	public const float BenchTop = 0.88f;
	private const float DoorW = 0.92f, DoorH = 1.88f, WallT = 0.05f;
	private float Hw => Width * 0.5f;
	private float Hd => Depth * 0.5f;
	private float BackH => WallHeight - 0.4f;
	private float WallTopAt(float z) => Mathf.Lerp(BackH, WallHeight, (z + Hd) / Depth);

	/// <summary>Where the hammer lies on the workbench (world).</summary>
	public Vector3 BenchSpot => GlobalTransform * new Vector3(0.15f, BenchTop, -Hd + 0.36f);

	private ForestTerrain _terrain;
	private bool _grounded;

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
		{
			_terrain = GroundSnap.FindTerrain(this);
			if (AutoGround && !_grounded) GroundSelf();
		}
		Build();
		// (2026-10-13) a room of its own like every other building's: the woods' fog and sounds stay outside
		if (!Engine.IsEditorHint())
			Audio.AudioDirector.Zone(this, new Vector3(0, WallHeight * 0.5f, 0), new Vector3(Width - 0.1f, WallHeight, Depth - 0.1f), Audio.AudioDirector.Space.Room, "ShedVerb");
	}

	private void GroundSelf()
	{
		_grounded = true;
		if (_terrain == null) return;
		if (!GroundSnap.SampleRect(_terrain, GlobalTransform, new Vector2(-Hw, -Hd), new Vector2(Hw, Hd + 0.4f), 5, out var s)) return;
		GlobalPosition = GlobalPosition with { Y = Mathf.Max(s.Max + 0.1f, s.Avg + 0.22f) };
	}

	private float GroundLocal(float x, float z)
	{
		if (_terrain == null || !IsInsideTree()) return -EditorGroundDepth;
		Vector3 w = GlobalTransform * new Vector3(x, 0, z);
		return _terrain.HeightAt(w.X, w.Z) - GlobalPosition.Y;
	}

	private const float StepW = 1.04f, StepRun = 0.27f, MaxRise = 0.2f, StringerH = 0.26f;

	/// <summary>Weathered plank steps from the sill down to the yard: two board stringers (plumb cut at the
	/// sill, level cut where each meets its own ground, so it works on a cross-slope), cleats, two planks a
	/// tread, a flat stone under each stringer's foot. However far the floor stands proud, the risers stay
	/// under <see cref="MaxRise"/>; the collision is one smooth ramp along the nosings, run on past the foot
	/// and into the ground so there is no lip to climb at either end (the capsule has no step-up).</summary>
	private void BuildSteps(MeshKit k, List<(Vector3 c, Vector3 s, Basis b)> cols)
	{
		float ze = Hd + 0.015f;   // the face of the front skirt, under the sill
		// how far down: the flight reaches further out on falling ground, so walk the foot out as it grows
		int n = 1;
		float drop = -GroundLocal(0, ze + 0.5f * StepRun);
		for (int it = 0; it < 6; it++)
		{
			n = Mathf.Clamp(Mathf.CeilToInt(drop / MaxRise), 1, 14);
			float d = -GroundLocal(0, ze + (n - 0.5f) * StepRun);
			if (Mathf.Abs(d - drop) < 0.01f || (it > 0 && d < drop)) break;
			drop = d;
		}
		n = Mathf.Clamp(Mathf.CeilToInt(drop / MaxRise), 1, 14);
		if (drop < 0.06f) return;   // the sill is at the ground: nothing to climb
		float rise = drop / n, slope = rise / StepRun;
		float Nose(float z) => -(z - ze) * slope;   // the line along the nosings
		var rng = new RandomNumberGenerator { Seed = 4423 };
		float sw = StepW * 0.5f;

		// stringers, one each side, each cut level where it meets its own ground
		k.Mat(BuildingTextures.TimberAlongMat);
		foreach (int s in new[] { -1, 1 })
		{
			float x = s * (sw - 0.025f);
			// its own ground at its foot (on a cross-slope one stringer lands sooner than the other)
			float g = GroundLocal(x, ze + (n - 0.5f) * StepRun);
			for (int it = 0; it < 4; it++) g = Mathf.Min(GroundLocal(x, ze - (g + StringerH * 0.5f) / slope), -0.02f);
			g += 0.02f;   // the foot rests on its stone
			float zTop = ze - g / slope;                 // where its top edge meets the ground
			float zBot = ze - (g + StringerH) / slope;   // ... and its bottom edge
			k.Color = new Color(0.56f, 0.52f, 0.47f) * rng.RandfRange(0.92f, 1.04f);
			var zy = new List<Vector2> { new(ze, 0f), new(zTop, g) };
			if (zBot > ze + 0.01f) { zy.Add(new(zBot, g)); zy.Add(new(ze, -StringerH)); }
			else zy.Add(new(ze, g));
			k.ExtrudeX(zy, x - 0.025f, x + 0.025f, 1.4f);
			// a flat stone under its foot
			k.Mat(ProcTextures.RockTriMat);
			k.Color = new Color(0.6f, 0.58f, 0.55f);
			zBot = Mathf.Max(ze, zBot);
			float zf = (zTop + zBot) * 0.5f;
			k.Blob(new Vector3(x, GroundLocal(x, zf) - 0.03f, zf), new Vector3(0.13f, 0.07f, Mathf.Max(0.16f, (zTop - zBot) * 0.5f)), 4431 + s, 0.15f, true, 1.5f, 0.35f);
			k.Mat(BuildingTextures.TimberAlongMat);
		}
		// treads: two weathered planks each, resting on cleats nailed inside the stringers
		float inner = StepW - 0.1f, plankD = (StepRun - 0.012f) * 0.5f;
		for (int i = 1; i < n; i++)
		{
			float top = -i * rise, za = ze + (i - 1) * StepRun;
			for (int p = 0; p < 2; p++)
			{
				float zc = za + 0.004f + plankD * (p + 0.5f) + p * 0.006f;
				k.Mat(BuildingTextures.PorchDeckMat);
				k.Color = new Color(0.78f, 0.74f, 0.69f) * rng.RandfRange(0.85f, 1.05f);
				var tilt = Basis.FromEuler(new Vector3(rng.RandfRange(-0.012f, 0.012f), rng.RandfRange(-0.02f, 0.02f), rng.RandfRange(-0.01f, 0.01f)));
				BuildKit.Box(k, new Vector3(rng.RandfRange(-0.015f, 0.015f), top - 0.02f - rng.RandfRange(0f, 0.006f), zc), new Vector3(inner + rng.RandfRange(-0.01f, 0.02f), 0.04f, plankD), 1f / 0.15f, BuildKit.Face.None, tilt);
			}
			k.Mat(BuildingTextures.TimberAlongMat);
			k.Color = new Color(0.5f, 0.46f, 0.42f);
			foreach (int s in new[] { -1, 1 })
				BuildKit.Box(k, new Vector3(s * (sw - 0.05f - 0.02f), top - 0.065f, za + StepRun * 0.5f), new Vector3(0.04f, 0.05f, StepRun - 0.06f), 1.4f);
			// nail heads
			k.Mat(BuildingTextures.IronMat);
			k.Color = new Color(0.3f, 0.28f, 0.26f);
			foreach (int s in new[] { -1, 1 })
				for (int p = 0; p < 2; p++)
					BuildKit.Box(k, new Vector3(s * (inner * 0.5f - 0.04f), top + 0.001f, za + 0.004f + plankD * (p + 0.5f) + p * 0.006f), new Vector3(0.012f, 0.004f, 0.012f), 3f, BuildKit.Face.NY);
		}
		// collision: one ramp along the nosings from the sill, carried on past the foot into the ground
		float zEnd = ze + n * StepRun + 0.4f;
		float gEnd = Mathf.Min(GroundLocal(0, zEnd), Mathf.Min(GroundLocal(-sw, zEnd), GroundLocal(sw, zEnd))) - 0.03f;
		Vector3 from = new(0, 0, Hd), to = new(0, Mathf.Min(gEnd, Nose(zEnd)), zEnd);
		Vector3 dir = to - from;
		float len = dir.Length();
		Vector3 zAxis = dir / len;
		Vector3 yAxis = zAxis.Cross(Vector3.Right).Normalized();
		if (yAxis.Y < 0) yAxis = -yAxis;
		cols.Add(((from + to) * 0.5f - yAxis * 0.1f, new Vector3(StepW, 0.2f, len), new Basis(Vector3.Right, yAxis, zAxis)));
	}

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		var gen = new Node3D { Name = "Generated" };
		AddChild(gen);

		var k = new MeshKit();
		var cols = new List<(Vector3 c, Vector3 s, Basis b)>();
		var boards = BuildingTextures.ShedWallMat;   // (2026-10-14a: its own boards, once red, not the cabin's wood)
		var frame = BuildingTextures.TimberUpMat;
		var rng = new RandomNumberGenerator { Seed = 4411 };
		float hw = Hw, hd = Hd;

		// ---- piers and skirt down to the ground, floor
		k.Mat(BuildingTextures.StoneMat);   // (2026-10-14a: fieldstone piers like the cabin's footings, not new concrete)
		k.Color = new Color(0.8f, 0.78f, 0.74f);
		foreach (float x in new[] { -hw + 0.15f, hw - 0.15f })
			foreach (float z in new[] { -hd + 0.15f, 0f, hd - 0.15f })
			{
				float g = Mathf.Min(GroundLocal(x, z), -0.12f) - 0.15f;
				BuildKit.Box(k, new Vector3(x, (g - 0.1f) * 0.5f, z), new Vector3(0.3f, -0.1f - g, 0.3f), 2.5f, BuildKit.Face.NY);
			}
		k.Mat(BuildingTextures.GableBoardsMat);
		k.Color = new Color(0.42f, 0.38f, 0.34f);
		foreach (var (a, b) in new[] { (new Vector2(-hw, hd), new Vector2(hw, hd)), (new Vector2(-hw, -hd), new Vector2(hw, -hd)), (new Vector2(-hw, -hd), new Vector2(-hw, hd)), (new Vector2(hw, -hd), new Vector2(hw, hd)) })
		{
			float g = Mathf.Min(Mathf.Min(GroundLocal(a.X, a.Y), GroundLocal(b.X, b.Y)), GroundLocal((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f)) - 0.15f;
			if (g > -0.12f) continue;
			bool alongZ = Mathf.Abs(a.X - b.X) < 0.01f;
			var c = new Vector3((a.X + b.X) * 0.5f, (g - 0.1f) * 0.5f, (a.Y + b.Y) * 0.5f);
			var s = alongZ ? new Vector3(0.03f, -0.1f - g, Depth) : new Vector3(Width, -0.1f - g, 0.03f);
			BuildKit.Box(k, c, s, 1.1f, BuildKit.Face.NY | BuildKit.Face.PY);
		}
		k.Mat(BuildingTextures.CabinFloorMat);
		k.Color = new Color(0.8f, 0.76f, 0.72f);
		BuildKit.Box(k, new Vector3(0, -0.05f, 0), new Vector3(Width, 0.1f, Depth), 1f / 0.6f, BuildKit.Face.NY);
		float gMin = Mathf.Min(Mathf.Min(GroundLocal(-hw, -hd), GroundLocal(hw, -hd)), Mathf.Min(GroundLocal(-hw, hd), GroundLocal(hw, hd))) - 0.2f;
		cols.Add((new Vector3(0, gMin * 0.5f, 0), new Vector3(Width, -gMin, Depth), Basis.Identity));

		BuildSteps(k, cols);

		// ---- walls: vertical boards outside, battens over the seams
		k.Mat(boards);
		k.Color = new Color(0.9f, 0.86f, 0.82f);
		float dw = DoorW * 0.5f;
		// back and sides (sides follow the roof slope with a trapezoid)
		BuildKit.Box(k, new Vector3(0, BackH * 0.5f - 0.05f, -hd), new Vector3(Width, BackH + 0.1f, WallT), 1.1f);
		foreach (int s in new[] { -1, 1 })
		{
			var zy = new List<Vector2> { new(-hd, -0.1f), new(hd, -0.1f), new(hd, WallHeight), new(-hd, BackH) };
			k.ExtrudeX(zy, s * hw - WallT * 0.5f, s * hw + WallT * 0.5f, 1.1f);
		}
		// front: either side of the doorway and the header
		float sideW = hw - dw;
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(s * (dw + sideW * 0.5f), WallHeight * 0.5f - 0.05f, hd), new Vector3(sideW, WallHeight + 0.1f, WallT), 1.1f);
		BuildKit.Box(k, new Vector3(0, (DoorH + WallHeight) * 0.5f, hd), new Vector3(DoorW, WallHeight - DoorH, WallT), 1.1f);
		// battens
		k.Mat(frame);
		k.Color = new Color(0.5f, 0.46f, 0.42f);
		for (float x = -hw + 0.3f; x < hw - 0.1f; x += 0.3f)
		{
			BuildKit.Box(k, new Vector3(x, BackH * 0.5f - 0.05f, -hd - WallT * 0.5f - 0.012f), new Vector3(0.05f, BackH + 0.1f, 0.025f), 1.4f, BuildKit.Face.NY | BuildKit.Face.PZ);
			if (Mathf.Abs(x) > dw + 0.05f)
				BuildKit.Box(k, new Vector3(x, WallHeight * 0.5f - 0.05f, hd + WallT * 0.5f + 0.012f), new Vector3(0.05f, WallHeight + 0.1f, 0.025f), 1.4f, BuildKit.Face.NY | BuildKit.Face.NZ);
		}
		for (float z = -hd + 0.3f; z < hd - 0.1f; z += 0.3f)
			foreach (int s in new[] { -1, 1 })
			{
				float top = WallTopAt(z);
				BuildKit.Box(k, new Vector3(s * (hw + WallT * 0.5f + 0.012f), top * 0.5f - 0.05f, z), new Vector3(0.025f, top + 0.1f, 0.05f), 1.4f, BuildKit.Face.NY);
			}
		// corner boards and the door casing
		k.Color = new Color(0.62f, 0.57f, 0.5f);
		foreach (int cx in new[] { -1, 1 })
			foreach (int sz in new[] { -1, 1 })
			{
				float top = sz > 0 ? WallHeight : BackH;
				BuildKit.Box(k, new Vector3(cx * (hw + 0.01f), top * 0.5f - 0.05f, sz * (hd + 0.01f)), new Vector3(0.12f, top + 0.1f, 0.12f), 1.4f, BuildKit.Face.NY);
			}
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(s * (dw + 0.05f), DoorH * 0.5f, hd + 0.035f), new Vector3(0.1f, DoorH, 0.035f), 1.4f);
		BuildKit.Box(k, new Vector3(0, DoorH + 0.05f, hd + 0.035f), new Vector3(DoorW + 0.2f, 0.1f, 0.035f), 1.4f);

		// frame inside: studs, plates, the door header
		k.Color = new Color(0.66f, 0.6f, 0.52f);
		for (float x = -hw + 0.6f; x < hw - 0.2f; x += 0.6f)
			BuildKit.Box(k, new Vector3(x, BackH * 0.5f, -hd + WallT * 0.5f + 0.045f), new Vector3(0.05f, BackH, 0.09f), 1.4f, BuildKit.Face.NY);
		for (float z = -hd + 0.6f; z < hd - 0.2f; z += 0.6f)
			foreach (int s in new[] { -1, 1 })
				BuildKit.Box(k, new Vector3(s * (hw - WallT * 0.5f - 0.045f), WallTopAt(z) * 0.5f, z), new Vector3(0.09f, WallTopAt(z), 0.05f), 1.4f, BuildKit.Face.NY);
		BuildKit.Box(k, new Vector3(0, BackH - 0.05f, -hd + WallT * 0.5f + 0.045f), new Vector3(Width - 0.1f, 0.09f, 0.09f), 1.4f);
		foreach (int s in new[] { -1, 1 })
		{
			BuildKit.Box(k, new Vector3(s * (dw + 0.025f), DoorH * 0.5f, hd - WallT * 0.5f - 0.045f), new Vector3(0.05f, DoorH, 0.09f), 1.4f, BuildKit.Face.NY);
			// top plates along the sloped side walls
			k.Beam(new Vector3(s * (hw - 0.07f), BackH - 0.05f, -hd), new Vector3(s * (hw - 0.07f), WallHeight - 0.05f, hd), 0.09f, 0.09f, 1.4f);
		}
		BuildKit.Box(k, new Vector3(0, DoorH + 0.05f, hd - WallT * 0.5f - 0.045f), new Vector3(DoorW + 0.1f, 0.1f, 0.09f), 1.4f);

		// ---- lean-to roof, falling to the back, with rafters under it
		float ov = 0.22f;
		Vector3 rf = new(0, WallHeight + 0.02f, hd + ov), rb = new(0, BackH + 0.02f - ov * 0.4f / Depth, -hd - ov);
		Vector3 slope = rf - rb;
		Vector3 n = new Vector3(0, slope.Z, -slope.Y).Normalized();
		float slopeLen = slope.Length();
		float xr = hw + ov;
		Vector3 lift = n * 0.07f;
		k.Mat(BuildingTextures.ShingleMat);
		k.Color = new Color(0.85f, 0.85f, 0.83f);
		k.Quad(new Vector3(-xr, rb.Y, rb.Z) + lift, new Vector3(xr, rb.Y, rb.Z) + lift, new Vector3(xr, rf.Y, rf.Z) + lift, new Vector3(-xr, rf.Y, rf.Z) + lift, n,
			new Vector2(-xr, slopeLen) * 0.9f, new Vector2(xr, slopeLen) * 0.9f, new Vector2(xr * 0.9f, 0), new Vector2(-xr * 0.9f, 0));
		k.Mat(BuildingTextures.GableBoardsMat);
		k.Color = new Color(0.55f, 0.5f, 0.45f);
		k.Quad(new Vector3(-xr, rb.Y, rb.Z), new Vector3(xr, rb.Y, rb.Z), new Vector3(xr, rf.Y, rf.Z), new Vector3(-xr, rf.Y, rf.Z), -n);
		k.Mat(BuildingTextures.TimberAlongMat);
		k.Color = new Color(0.6f, 0.55f, 0.48f);
		// fascia all round
		BuildKit.Box(k, new Vector3(0, rf.Y + 0.02f, rf.Z + 0.02f), new Vector3(xr * 2f + 0.04f, 0.16f, 0.035f), 1.4f);
		BuildKit.Box(k, new Vector3(0, rb.Y + 0.02f, rb.Z - 0.02f), new Vector3(xr * 2f + 0.04f, 0.16f, 0.035f), 1.4f);
		foreach (int s in new[] { -1, 1 })
			k.Beam(new Vector3(s * (xr + 0.02f), rb.Y + 0.03f, rb.Z), new Vector3(s * (xr + 0.02f), rf.Y + 0.03f, rf.Z), 0.035f, 0.16f, 1.4f, n);
		for (float x = -hw + 0.3f; x < hw; x += 0.6f)
			k.Beam(new Vector3(x, rb.Y - 0.05f, rb.Z + 0.05f), new Vector3(x, rf.Y - 0.05f, rf.Z - 0.05f), 0.05f, 0.1f, 1.4f, n);

		// ---- the door, hanging open against the front wall
		k.Mat(BuildingTextures.ShedDoorMat);
		k.Color = new Color(0.9f, 0.86f, 0.82f);
		var doorBasis = new Basis(Vector3.Up, Mathf.DegToRad(105f));
		Vector3 hinge = new(dw, 0.03f, hd + 0.05f);
		BuildKit.Box(k, hinge + doorBasis * new Vector3(-DoorW * 0.5f + 0.02f, DoorH * 0.5f - 0.02f, 0.02f), new Vector3(DoorW - 0.04f, DoorH - 0.06f, 0.04f), 1.3f, BuildKit.Face.None, doorBasis);
		k.Mat(BuildingTextures.ShedDoorMat);
		k.Color = new Color(0.8f, 0.76f, 0.72f);
		foreach (float y in new[] { 0.3f, DoorH - 0.35f })
			BuildKit.Box(k, hinge + doorBasis * new Vector3(-DoorW * 0.5f + 0.02f, y, 0.055f), new Vector3(DoorW - 0.12f, 0.12f, 0.03f), 1.4f, BuildKit.Face.None, doorBasis);
		// hasp with an open padlock hanging from it
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.45f, 0.42f, 0.38f);
		BuildKit.Box(k, new Vector3(-dw - 0.12f, 1.05f, hd + 0.06f), new Vector3(0.1f, 0.04f, 0.015f), 3f);
		BuildKit.Box(k, new Vector3(-dw - 0.17f, 0.99f, hd + 0.07f), new Vector3(0.05f, 0.06f, 0.025f), 3f);
		cols.Add((hinge + doorBasis * new Vector3(-DoorW * 0.5f, DoorH * 0.5f, 0.02f), new Vector3(DoorW, DoorH, 0.06f), doorBasis));

		// ---- workbench along the back wall
		var wood = BuildingTextures.PorchDeckMat;
		float bz = -hd + 0.34f, bw = Width - 0.3f;
		k.Mat(wood);
		k.Color = new Color(0.85f, 0.8f, 0.72f);
		BuildKit.Box(k, new Vector3(0, BenchTop - 0.03f, bz), new Vector3(bw, 0.06f, 0.56f), 1f / 0.15f);
		BuildKit.Box(k, new Vector3(0, 0.25f, bz + 0.02f), new Vector3(bw - 0.1f, 0.03f, 0.46f), 1f / 0.15f);
		k.Mat(frame);
		k.Color = new Color(0.62f, 0.56f, 0.48f);
		foreach (float x in new[] { -bw * 0.5f + 0.06f, bw * 0.5f - 0.06f })
			foreach (float z in new[] { bz - 0.22f, bz + 0.22f })
				BuildKit.Box(k, new Vector3(x, (BenchTop - 0.06f) * 0.5f, z), new Vector3(0.07f, BenchTop - 0.06f, 0.07f), 1.4f, BuildKit.Face.NY);
		BuildKit.Box(k, new Vector3(0, BenchTop - 0.1f, bz + 0.27f), new Vector3(bw, 0.1f, 0.03f), 1.4f);
		cols.Add((new Vector3(0, BenchTop * 0.5f, bz), new Vector3(bw, BenchTop, 0.56f), Basis.Identity));
		// vise on the right end
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.3f, 0.32f, 0.3f);
		float vx = bw * 0.5f - 0.2f;
		BuildKit.Box(k, new Vector3(vx, BenchTop + 0.07f, bz + 0.22f), new Vector3(0.16f, 0.14f, 0.12f), 3f, BuildKit.Face.NY);
		BuildKit.Box(k, new Vector3(vx, BenchTop + 0.08f, bz + 0.33f), new Vector3(0.16f, 0.12f, 0.04f), 3f);
		k.Cylinder(new Vector3(vx - 0.1f, BenchTop + 0.06f, bz + 0.37f), new Vector3(vx + 0.1f, BenchTop + 0.06f, bz + 0.37f), 0.01f, 0.01f, 4, false);
		// a tin of nails, a coffee can, a rag on the bench (left side; the hammer spot is middle-right)
		k.Mat(BuildingTextures.Plain("s_tin", new Color(0.42f, 0.38f, 0.32f), 0.5f));
		k.Color = Colors.White;
		k.Cylinder(new Vector3(-0.55f, BenchTop, bz - 0.1f), new Vector3(-0.55f, BenchTop + 0.14f, bz - 0.1f), 0.06f, 0.06f, 7, true);
		k.Mat(BuildingTextures.Plain("s_can", new Color(0.28f, 0.3f, 0.34f), 0.5f));
		k.Cylinder(new Vector3(-0.38f, BenchTop, bz + 0.05f), new Vector3(-0.38f, BenchTop + 0.1f, bz + 0.05f), 0.045f, 0.045f, 7, true);
		k.Mat(BuildingTextures.Plain("s_rag", new Color(0.45f, 0.4f, 0.32f)));
		BuildKit.Box(k, new Vector3(-0.2f, BenchTop + 0.01f, bz + 0.12f), new Vector3(0.22f, 0.02f, 0.16f), 1f, BuildKit.Face.NY, new Basis(Vector3.Up, 0.4f));

		// ---- pegboard of hanging tools above the bench
		k.Mat(BuildingTextures.Plain("s_pegboard", new Color(0.34f, 0.27f, 0.2f), 0.9f));
		k.Color = new Color(0.5f, 0.44f, 0.38f);
		BuildKit.Box(k, new Vector3(0, 1.35f, -hd + WallT * 0.5f + 0.1f), new Vector3(1.4f, 0.6f, 0.015f), 1.5f);
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.36f, 0.35f, 0.33f);
		float pz = -hd + WallT * 0.5f + 0.12f;
		// saw blade, wrench, pliers, a coil of rope
		BuildKit.Box(k, new Vector3(-0.35f, 1.35f, pz), new Vector3(0.42f, 0.13f, 0.005f), 3f, BuildKit.Face.None, new Basis(Vector3.Back, 0.12f));
		BuildKit.Box(k, new Vector3(0.05f, 1.37f, pz), new Vector3(0.025f, 0.24f, 0.01f), 3f);
		BuildKit.Box(k, new Vector3(0.2f, 1.36f, pz), new Vector3(0.025f, 0.2f, 0.01f), 3f, BuildKit.Face.None, new Basis(Vector3.Back, 0.25f));
		k.Mat(frame);
		k.Color = new Color(0.55f, 0.35f, 0.2f);
		BuildKit.Box(k, new Vector3(-0.6f, 1.33f, pz + 0.005f), new Vector3(0.12f, 0.06f, 0.03f), 3f);
		k.Mat(BuildingTextures.Plain("s_rope", new Color(0.5f, 0.44f, 0.3f)));
		k.Color = Colors.White;
		// (2026-10-14a: was a solid eight-sided disc) a coil of rope: three loops of a round strand hung on a peg
		for (int loop = 0; loop < 3; loop++)
		{
			float r = 0.12f - loop * 0.008f, lz = pz + 0.015f + loop * 0.02f, sag = 0.01f * loop;
			Vector3 Rp(int i) { float a = i * Mathf.Tau / 14f; return new Vector3(0.48f + Mathf.Cos(a) * r, 1.34f - sag + Mathf.Sin(a) * r * 1.08f, lz); }
			for (int i = 0; i < 14; i++) k.Cylinder(Rp(i), Rp(i + 1), 0.016f, 0.016f, 6, false);
		}
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.3f, 0.28f, 0.26f);
		k.Cylinder(new Vector3(0.48f, 1.46f, pz - 0.01f), new Vector3(0.48f, 1.46f, pz + 0.08f), 0.008f, 0.008f, 6, true);

		// ---- shelf on the left wall with jars and tins; long tools leaning in the front-right corner
		k.Mat(wood);
		k.Color = new Color(0.8f, 0.75f, 0.68f);
		float sx = -hw + WallT * 0.5f + 0.17f;
		foreach (float y in new[] { 1.2f, 1.62f })
			BuildKit.Box(k, new Vector3(sx, y, 0.1f), new Vector3(0.26f, 0.03f, 1.1f), 1f / 0.15f);
		for (int i = 0; i < 5; i++)
		{
			k.Mat(BuildingTextures.Plain(i % 2 == 0 ? "s_jar" : "s_tin2", i % 2 == 0 ? new Color(0.3f, 0.34f, 0.28f) : new Color(0.44f, 0.38f, 0.28f), 0.45f));
			k.Color = Colors.White;
			float z = -0.35f + i * 0.2f + rng.RandfRange(-0.03f, 0.03f);
			float y = i < 3 ? 1.215f : 1.635f;
			k.Cylinder(new Vector3(sx, y, z), new Vector3(sx, y + rng.RandfRange(0.1f, 0.18f), z), 0.05f, 0.05f, 6, true);
		}
		// shovel and rake leaning in the corner
		k.Mat(frame);
		k.Color = new Color(0.6f, 0.5f, 0.38f);
		Vector3 c0 = new(hw - 0.2f, 0.02f, hd - 0.3f);
		k.Beam(c0, c0 + new Vector3(-0.28f, 1.25f, -0.12f), 0.035f, 0.035f, 2f);
		k.Beam(c0 + new Vector3(-0.1f, 0, -0.18f), c0 + new Vector3(-0.35f, 1.4f, -0.3f), 0.03f, 0.03f, 2f);
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.3f, 0.3f, 0.29f);
		BuildKit.Box(k, c0 + new Vector3(0.02f, 0.14f, 0.01f), new Vector3(0.22f, 0.28f, 0.02f), 2f, BuildKit.Face.None, Basis.FromEuler(new Vector3(0.1f, 0.4f, 0.2f)));
		BuildKit.Box(k, c0 + new Vector3(-0.35f, 1.42f, -0.3f), new Vector3(0.36f, 0.03f, 0.06f), 2f, BuildKit.Face.None, new Basis(Vector3.Up, 0.5f));

		// ---- floor clutter: a red gas can, a sack, a bucket
		k.Mat(BuildingTextures.Plain("s_gascan", new Color(0.42f, 0.08f, 0.06f), 0.6f));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(-hw + 0.35f, 0.17f, hd - 0.45f), new Vector3(0.2f, 0.34f, 0.3f), 2f, BuildKit.Face.NY, new Basis(Vector3.Up, 0.3f));
		k.Mat(BuildingTextures.CanvasMat);
		k.Color = new Color(0.8f, 0.72f, 0.55f);
		k.Blob(new Vector3(-hw + 0.4f, 0.2f, -0.35f), new Vector3(0.22f, 0.2f, 0.18f), 91, 0.2f, true, 2f);
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.4f, 0.4f, 0.4f);
		k.Cylinder(new Vector3(0.55f, 0.0f, bz + 0.05f), new Vector3(0.55f, 0.24f, bz + 0.05f), 0.11f, 0.14f, 8, false);
		cols.Add((new Vector3(-hw + 0.35f, 0.2f, 0), new Vector3(0.4f, 0.4f, 1.1f), Basis.Identity));

		k.Color = Colors.White;
		k.CommitTo(gen, "ShedMesh");

		// dim bulb-less interior: the shed is lit only from outside (and the lantern)

		if (BuildCollision && !Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "ShedBody", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "wood");
			gen.AddChild(body);
			float cy = WallHeight * 0.5f;
			body.AddChild(new CollisionShape3D { Position = new Vector3(-hw, cy, 0), Shape = new BoxShape3D { Size = new Vector3(WallT + 0.1f, WallHeight, Depth) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(hw, cy, 0), Shape = new BoxShape3D { Size = new Vector3(WallT + 0.1f, WallHeight, Depth) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, cy, -hd), Shape = new BoxShape3D { Size = new Vector3(Width, WallHeight, WallT + 0.1f) } });
			foreach (int s in new[] { -1, 1 })
				body.AddChild(new CollisionShape3D { Position = new Vector3(s * (dw + sideW * 0.5f), cy, hd), Shape = new BoxShape3D { Size = new Vector3(sideW, WallHeight, WallT + 0.1f) } });
			foreach (var (c, s, b) in cols)
				body.AddChild(new CollisionShape3D { Position = c, Basis = b, Shape = new BoxShape3D { Size = s } });
		}
	}
}
