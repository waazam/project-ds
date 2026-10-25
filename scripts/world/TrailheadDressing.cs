using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The trailhead lot made a real one, and its road a road that goes somewhere (2026-10-15, the owner: "at the beginning
/// if you turn around it looks like a parking lot then drop off the world ... something that looks like were in the
/// forest in a parking lot at the start of a trailhead").
/// <list type="bullet">
/// <item>Log rails on stubby posts round the lot's sides and back, the gap where the road leaves.</item>
/// <item>The forest road's pipe gate at the lot's mouth, swung open against the verge and chained back.</item>
/// <item>Out along the road: a brown highway sign (the way home) and a yellow curve warning where it bends away.</item>
/// <item>Nothing across the road (the owner: "no fallen trees on road cause how else did we drive in"): it stays open
/// and bends away into the trees and the fog. The world's walls stop the player well past the bend, out of sight of the
/// lot (<see cref="ForestTerrain.WalkMaxZ"/>); the ground and the woods go on past them.</item>
/// </list>
/// Built from the terrain's own road and lot, so it follows edits to either. Everything is grounded on the terrain.
/// </summary>
public partial class TrailheadDressing : Node3D
{
	/// <summary>Along the road (m from the lot) where the gate stands, the highway sign and the curve sign.</summary>
	[Export] public float GateAt = 6.5f, HighwaySignAt = 13f, CurveSignAt = 19f;
	[Export] public int Seed = 7;

	private ForestTerrain _t;
	private MeshKit _k;
	private StaticBody3D _body;
	private Node3D _gen;
	private bool _m;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_t = GroundSnap.FindTerrain(this);
		if (_t == null || _t.Road.Points.Count < 2) return;
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);
		_gen.TopLevel = true;
		_gen.GlobalTransform = Transform3D.Identity;
		_k = new MeshKit();
		// (2026-10-15) the rails, the gate and the signs modelled in Blender (park_props.py), when the models are there
		_m = PropModels.Has("rail_post") && PropModels.Has("rail_log_0") && PropModels.Has("pipe_gate")
			&& PropModels.Has("gate_latch_post") && PropModels.Has("highway_sign") && PropModels.Has("curve_sign");
		LogRails();
		Gate();
		HighwaySign();
		CurveSign();
		BurnBarrel();
		Boards();
		if (!_k.IsEmpty) _k.CommitTo(_gen, "Mesh");	}

	/// <summary>(2026-10-24, the weathered props) An old oil drum by the vault toilet, rusted through its blue paint, used to
	/// burn rubbish: a thin smoke still going up out of it.</summary>
	private void BurnBarrel()
	{
		var at = G(-7.4f, 20.6f, -0.03f);
		var b = WeatheredProps.Put(_gen, "BurnBarrel", at, 37f);
		if (b == null) return;
		Col(at + Vector3.Up * 0.44f, new Vector3(0.56f, 0.88f, 0.56f), Basis.Identity);
		var smoke = new CpuParticles3D
		{
			Name = "BarrelSmoke", Amount = 26, Lifetime = 6.5f, Position = at + Vector3.Up * 0.75f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.16f, Direction = Vector3.Up, Spread = 10f,
			InitialVelocityMin = 0.28f, InitialVelocityMax = 0.45f, Gravity = new Vector3(0.05f, 0.04f, 0.02f),
			ScaleAmountMin = 0.7f, ScaleAmountMax = 1.8f, ScaleAmountCurve = new Curve(), Mesh = new QuadMesh { Size = new Vector2(0.55f, 0.55f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.74f, 0.73f, 0.7f, 0.1f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				AlbedoTexture = SoftPuff.Texture, VertexColorUseAsAlbedo = true,
			},
		};
		smoke.ScaleAmountCurve.AddPoint(new Vector2(0f, 0.4f));
		smoke.ScaleAmountCurve.AddPoint(new Vector2(1f, 2.2f));
		_gen.AddChild(smoke);
	}

	/// <summary>(2026-10-24, the weathered props: "painted signs") Two hand-painted boards older than the park's: a white
	/// arrow, LAKE TRAIL, by the trail's mouth, turned to the lot and pointing up the trail; and by the burn barrel, a
	/// green FIREWOOD board on its stakes (an honour box long gone).</summary>
	private void Boards()
	{
		var arrow = G(-1.6f, 18.0f, -0.05f);
		if (WeatheredProps.Put(_gen, "Sign_Arrow", arrow, 60f) != null)
			Col(arrow + Vector3.Up * 0.66f, new Vector3(0.12f, 1.32f, 0.12f), Basis.Identity);
		var wood = G(-6.5f, 21.6f, -0.05f);
		var yaw = new Basis(Vector3.Up, Mathf.DegToRad(73f));
		if (WeatheredProps.Put(_gen, "Sign_Firewood", wood, 73f) != null)
			Col(wood + yaw * new Vector3(0f, 0.62f, -0.04f), new Vector3(0.86f, 1.24f, 0.08f), yaw);
	}

	// ---------------------------------------------------------------- helpers

	private float H(float x, float z) => _t.HeightAt(x, z);
	private Vector3 G(float x, float z, float up = 0f) => new(x, H(x, z) + up, z);

	/// <summary>A point on the road (world, on the ground), its direction, and its right-hand side (looking out along it).</summary>
	private (Vector3 p, Vector3 dir, Vector3 right) Road(float s)
	{
		var o = _t.GlobalPosition;
		Vector2 p = _t.Road.At(s, out Vector2 tan);
		var dir = new Vector3(tan.X, 0, tan.Y).Normalized();
		var right = dir.Cross(Vector3.Up).Normalized();
		return (G(p.X + o.X, p.Y + o.Z), dir, right);
	}

	private void Col(Vector3 center, Vector3 size, Basis rot)
	{
		if (_body == null)
		{
			_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
			_gen.AddChild(_body);
		}
		_body.AddChild(new CollisionShape3D { Transform = new Transform3D(rot, center), Shape = new BoxShape3D { Size = size } });
	}

	private static Basis Facing(Vector3 dir) => Basis.LookingAt(dir.Normalized(), Vector3.Up);

	private static float Hash(int i) => Mathf.Abs(Mathf.Sin(i * 12.9898f + 78.233f) * 43758.547f) % 1f;

	// ---------------------------------------------------------------- the lot's log rails

	private void LogRails()
	{
		var r = _t.ParkingRect;
		var o = _t.GlobalPosition;
		float x0 = r.Position.X + o.X - 0.7f, x1 = r.End.X + o.X + 0.7f;
		float z0 = r.Position.Y + o.Z + 1.5f, z1 = r.End.Y + o.Z + 0.7f;
		var road = Road(0f).p;
		float gap = _t.RoadWidth * 0.5f + 1.4f;
		// the sides, front to back, and the back either side of the road's mouth
		Rail(new Vector2(x0, z0), new Vector2(x0, z1), 0);
		Rail(new Vector2(x1, z0), new Vector2(x1, z1), 1);
		Rail(new Vector2(x0, z1), new Vector2(road.X - gap, z1), 2);
		Rail(new Vector2(road.X + gap, z1), new Vector2(x1, z1), 3);
	}

	/// <summary>Round logs laid end to end on short posts, a post at every join, each log its own girth and sag.</summary>
	private void Rail(Vector2 a, Vector2 b, int salt)
	{
		float len = a.DistanceTo(b);
		if (len < 1f) return;
		int n = Mathf.Max(1, Mathf.RoundToInt(len / 3.2f));
		var bark = ProcTextures.BarkMat;
		var end = ProcTextures.EndGrainMat;
		for (int i = 0; i <= n; i++)
		{
			Vector2 q = a.Lerp(b, (float)i / n);
			float h = H(q.X, q.Y);
			if (_m)
			{
				PropModels.Place(_gen, "rail_post", new Transform3D(Basis.FromEuler(new Vector3(0, Hash(salt * 13 + i) * Mathf.Tau, 0)), new Vector3(q.X, h, q.Y)));
				continue;
			}
			_k.Color = new Color(0.78f, 0.72f, 0.64f) * (0.9f + 0.15f * Hash(salt * 31 + i));
			_k.Mat(bark).Cylinder(new Vector3(q.X, h - 0.15f, q.Y), new Vector3(q.X, h + 0.42f, q.Y), 0.11f, 0.1f, 8, false, 1f);
			_k.Mat(end).Cylinder(new Vector3(q.X, h + 0.42f, q.Y), new Vector3(q.X, h + 0.425f, q.Y), 0.1f, 0.1f, 8, true, 1f);
		}
		for (int i = 0; i < n; i++)
		{
			Vector2 p0 = a.Lerp(b, (float)i / n), p1 = a.Lerp(b, (float)(i + 1) / n);
			float r = 0.12f + 0.04f * Hash(salt * 17 + i);
			var s = G(p0.X, p0.Y, 0.47f);
			var e = G(p1.X, p1.Y, 0.47f);
			Vector3 mid = (s + e) * 0.5f;
			var dir = e - s;
			Col(mid + Vector3.Down * 0.2f, new Vector3(0.3f, 0.75f, dir.Length()), Facing(dir));
			if (_m)
			{
				// the 1 m log along X, stretched to the span and sized to this one's girth
				var ax = dir.Normalized();
				var az = ax.Cross(Vector3.Up).Normalized();
				var ay = az.Cross(ax);
				float g = r / 0.14f;
				var lb = new Basis(ax * dir.Length(), ay * g, az * g) * Basis.FromEuler(new Vector3(Hash(salt * 5 + i) * Mathf.Tau, 0, 0));
				PropModels.Place(_gen, $"rail_log_{(salt * 3 + i) % 3}", new Transform3D(lb, s));
				continue;
			}
			_k.Color = new Color(0.82f, 0.76f, 0.68f) * (0.88f + 0.16f * Hash(salt * 7 + i));
			_k.Mat(bark).Cylinder(s, e, r, r * 0.92f, 9, false, 1f);
			_k.Color = Colors.White;
			_k.Mat(end).Cylinder(s - (e - s).Normalized() * 0.01f, s, r - 0.004f, r - 0.004f, 9, true, 1.2f);
			_k.Cylinder(e, e + (e - s).Normalized() * 0.01f, r * 0.92f - 0.004f, r * 0.92f - 0.004f, 9, true, 1.2f);
		}
	}

	// ---------------------------------------------------------------- the gate

	private void Gate()
	{
		var (p, dir, right) = Road(GateAt);
		float half = _t.RoadWidth * 0.5f + 0.6f;
		var yellow = ProcTextures.Flat("th_gate_yellow", new Color(0.74f, 0.57f, 0.13f), 0.75f, 0.3f);
		var hinge = p + right * half;
		var latch = p - right * half;
		if (_m)
		{
			// the hinge post, the arm swung back and its chained stake are one model; the latch post its own
			PropModels.Place(_gen, "pipe_gate", new Transform3D(Facing(dir), hinge));
			PropModels.Place(_gen, "gate_latch_post", new Transform3D(Facing(dir), latch));
			Col(hinge + Vector3.Up * 0.5f, new Vector3(0.16f, 1.2f, 0.16f), Facing(dir));
			Col(latch + Vector3.Up * 0.45f, new Vector3(0.14f, 1.1f, 0.14f), Facing(dir));
			var mTip = hinge + dir * 4.6f + right * 0.15f;
			var mA0 = hinge + Vector3.Up * 0.9f;
			var mA1 = mTip + Vector3.Up * 0.86f;
			var mArm = mA1 - mA0;
			Col((mA0 + mA1) * 0.5f + Vector3.Down * 0.2f, new Vector3(0.12f, 0.6f, mArm.Length()), Facing(new Vector3(mArm.X, 0, mArm.Z)));
			return;
		}
		_k.Color = Colors.White;
		// the posts: square steel, set in the verge
		_k.Mat(yellow);
		_k.BevelBox(hinge + Vector3.Up * 0.45f, new Vector3(0.14f, 1.25f, 0.14f), 0.015f, 1f, Facing(dir));
		_k.BevelBox(latch + Vector3.Up * 0.4f, new Vector3(0.12f, 1.1f, 0.12f), 0.015f, 1f, Facing(dir));
		_k.BevelBox(hinge + Vector3.Up * 1.08f, new Vector3(0.2f, 0.04f, 0.2f), 0.01f, 1f, Facing(dir));
		Col(hinge + Vector3.Up * 0.5f, new Vector3(0.16f, 1.2f, 0.16f), Facing(dir));
		Col(latch + Vector3.Up * 0.45f, new Vector3(0.14f, 1.1f, 0.14f), Facing(dir));
		// the arm, swung open: it lies back along the verge, out the way the road goes, chained to a stake
		var tip = hinge + dir * 4.6f + right * 0.15f;
		var a0 = hinge + Vector3.Up * 0.9f;
		var a1 = new Vector3(tip.X, H(tip.X, tip.Z) + 0.86f, tip.Z);
		_k.Cylinder(a0, a1, 0.05f, 0.05f, 8, true, 1f);
		// its lower brace and the uprights between
		var b0 = hinge + Vector3.Up * 0.45f;
		var b1 = new Vector3(tip.X, H(tip.X, tip.Z) + 0.5f, tip.Z) - dir * 0.6f;
		_k.Cylinder(b0, b1, 0.035f, 0.035f, 7, true, 1f);
		for (int i = 1; i <= 3; i++)
		{
			float u = i / 4f;
			_k.Cylinder(a0.Lerp(a1, u), b0.Lerp(b1, u) + (b1 - b0) * 0f, 0.025f, 0.025f, 6, true, 1f);
		}
		// reflector plates, faded red-white, on the arm
		_k.Mat(ProcTextures.Flat("th_reflector", new Color(0.6f, 0.14f, 0.1f), 0.5f, 0.5f)).Box(a0.Lerp(a1, 0.55f) + right * 0.055f, new Vector3(0.01f, 0.12f, 0.35f), 1f, Facing(dir));
		// the stake and the chain looped to it
		var stake = tip + right * 0.25f;
		_k.Color = new Color(0.32f, 0.3f, 0.28f);
		_k.Mat(ProcTextures.MetalMat).Cylinder(G(stake.X, stake.Z, -0.1f), G(stake.X, stake.Z, 0.55f), 0.025f, 0.02f, 6, true, 1f);
		var link = a1 + dir * 0.02f;
		var to = G(stake.X, stake.Z, 0.5f);
		for (int i = 0; i < 6; i++)
		{
			float u0 = i / 6f, u1 = (i + 1) / 6f;
			Vector3 c0 = link.Lerp(to, u0) + Vector3.Down * 0.12f * Mathf.Sin(Mathf.Pi * u0);
			Vector3 c1 = link.Lerp(to, u1) + Vector3.Down * 0.12f * Mathf.Sin(Mathf.Pi * u1);
			_k.Cylinder(c0, c1, 0.008f, 0.008f, 4, false, 1f);
		}
		var armDir = a1 - a0;
		Col((a0 + a1) * 0.5f + Vector3.Down * 0.2f, new Vector3(0.12f, 0.6f, armDir.Length()), Facing(new Vector3(armDir.X, 0, armDir.Z)));
		_k.Color = Colors.White;
	}

	// ---------------------------------------------------------------- the signs

	/// <summary>A sign on two posts at the road's edge, facing back toward the lot (the way the player looks out).</summary>
	private void Signboard(float s, float side, float w, float h, float boardY, Material board, Material post, out Vector3 face, out Basis fb)
	{
		var (p, dir, right) = Road(s);
		var at = p + right * side * (_t.RoadWidth * 0.5f + 1.3f);
		at.Y = H(at.X, at.Z);
		fb = Facing(dir);   // its face (+Z) toward the lot
		face = at + Vector3.Up * boardY + fb.Z * 0.06f;
		Col(at + Vector3.Up * (boardY * 0.5f + 0.2f), new Vector3(w, boardY + h * 0.5f, 0.15f), fb);
		Clear(at, 1.4f);
		if (_m)
		{
			PropModels.Place(_gen, "highway_sign", new Transform3D(fb, at));
			return;
		}
		_k.Color = new Color(0.85f, 0.82f, 0.78f);
		_k.Mat(post);
		foreach (float px in new[] { -w * 0.32f, w * 0.32f })
		{
			var pp = at + fb.X * px;
			pp.Y = H(pp.X, pp.Z);
			_k.Box(pp + Vector3.Up * (boardY + h * 0.5f) * 0.5f, new Vector3(0.09f, boardY + h * 0.5f + 0.3f, 0.09f), 1.5f, fb);
		}
		_k.Color = Colors.White;
		_k.Mat(board).Box(at + Vector3.Up * boardY + fb.Z * 0.02f, new Vector3(w, h, 0.04f), 1.1f, fb);
	}

	private void HighwaySign()
	{
		Signboard(HighwaySignAt, 1f, 1.5f, 0.75f, 1.55f, BuildingTextures.SignBrownMat, BuildingTextures.SignPostBrownMat, out var face, out var fb);
		if (!_m) SignKit.Weather(_k, face - fb.Z * 0.02f, fb, 1.48f, 0.73f, Seed * 5);
		SignKit.Text(_gen, "STATE HWY 9", face + Vector3.Up * 0.17f, fb, 0.17f, SignKit.Paint);
		SignKit.Text(_gen, "ASHFORD  11 >", face - Vector3.Up * 0.12f, fb, 0.14f, SignKit.Paint * 0.9f);
	}

	private void CurveSign()
	{
		// the yellow diamond, a right-hand curve; worn, a little bent back on its post
		var (p, dir, right) = Road(CurveSignAt);
		var at = p - right * (_t.RoadWidth * 0.5f + 1.1f);
		at.Y = H(at.X, at.Z);
		var fb = Facing(dir).Rotated(Vector3.Up, Mathf.DegToRad(-12f));
		if (_m)
		{
			PropModels.Place(_gen, "curve_sign", new Transform3D(fb, at));
			Col(at + Vector3.Up * 1.1f, new Vector3(0.1f, 2.2f, 0.1f), fb);
			Clear(at, 1.2f);
			return;
		}
		_k.Color = new Color(0.55f, 0.55f, 0.55f);
		_k.Mat(ProcTextures.MetalMat).Box(at + Vector3.Up * 1.05f, new Vector3(0.07f, 2.4f, 0.04f), 1f, fb);
		var tilt = fb * new Basis(Vector3.Right, Mathf.DegToRad(-6f));
		var c = at + Vector3.Up * 2.05f + fb.Z * 0.04f;
		var diamond = tilt * new Basis(Vector3.Back, Mathf.Pi * 0.25f);
		_k.Color = Colors.White;
		_k.Mat(ProcTextures.Flat("th_sign_yellow", new Color(0.62f, 0.5f, 0.1f), 0.8f, 0.3f)).Box(c, new Vector3(0.58f, 0.58f, 0.015f), 1f, diamond);
		// the curve's arrow: up the middle, then its head bent off right
		_k.Mat(ProcTextures.Flat("th_sign_black", new Color(0.05f, 0.045f, 0.04f), 0.9f, 0.2f));
		_k.Box(c + tilt.Z * 0.009f - tilt.Y * 0.04f, new Vector3(0.05f, 0.26f, 0.004f), 1f, tilt);
		var head = tilt * new Basis(Vector3.Back, Mathf.DegToRad(-45f));
		_k.Box(c + tilt.Z * 0.01f + tilt.Y * 0.12f + tilt.X * 0.045f, new Vector3(0.05f, 0.16f, 0.004f), 1f, head);
		_k.Color = Colors.White;
		Col(at + Vector3.Up * 1.1f, new Vector3(0.1f, 2.2f, 0.1f), fb);
		Clear(at, 1.2f);
	}


	private void Clear(Vector3 world, float r) =>
		AddChild(new ClearZone { Position = ToLocal(world), Radius = r, ClearFoliage = r > 2f });
}
