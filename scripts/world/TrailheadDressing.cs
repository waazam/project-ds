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
		Firewood(wood, yaw);
	}

	// ---------------------------------------------------------------- the firewood for sale

	/// <summary>(2026-10-29, the owner's friend's playtest: by the green "FIREWOOD $2 A BUNDLE" board, "add actual stacked
	/// firewood bundles ... and a small honour box") Bundles of split logs, each tied round twice with twine, stacked three,
	/// two and one on a pair of skids beside the board (toward the burn barrel, clear of the van's tailgate); and the honour box on its
	/// post at the stack's end, a slot in its lid, HONOR BOX stencilled on it. <paramref name="sign"/> and
	/// <paramref name="yaw"/> are the board's foot and turn (its face +Z).</summary>
	private void Firewood(Vector3 sign, Basis yaw)
	{
		const float len = 0.42f, br = 0.16f;   // a bundle's length (its logs') and girth
		// the stack's middle: along the board's line, past its right end, a little back from its face
		var mid = sign + yaw * new Vector3(1.4f, 0f, -0.05f);
		mid.Y = H(mid.X, mid.Z);
		var stack = yaw;   // the stack runs along the board (its X); the bundles lie across it, their ends to the lot (its Z)
		var bark = BuildingTextures.BarkScanMat;
		var split = BuildingTextures.FreshPlankMat;
		var endGrain = BuildingTextures.LogEndMat;
		var twine = ProcTextures.Flat("th_twine", new Color(0.52f, 0.43f, 0.29f), 0.95f, 0.1f);
		var k = new MeshKit();
		k.Color = Colors.White;
		// the skids: two old 2x4s on the ground, under the bundles' ends
		k.Mat(PropTextures.WetPostMat);
		foreach (float z in new[] { -len * 0.32f, len * 0.32f })
			k.Box(mid + stack * new Vector3(0f, 0.04f, z), new Vector3(1.15f, 0.08f, 0.09f), 1.5f, stack);
		// three, two and one: each in the hollow between two below
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 31 + 5) };
		int n = 0;
		for (int row = 0; row < 3; row++)
			for (int i = 0; i < 3 - row; i++)
			{
				float x = (i - (2 - row) * 0.5f) * br * 2.05f;
				float y = 0.08f + br + row * br * 1.75f;
				var c = mid + stack * new Vector3(x + rng.RandfRange(-0.02f, 0.02f), y, rng.RandfRange(-0.03f, 0.03f));
				var turn = stack * new Basis(Vector3.Back, rng.RandfRange(0f, Mathf.Tau)) * new Basis(Vector3.Up, rng.RandfRange(-0.06f, 0.06f));
				Bundle(k, c, turn, len, br, bark, split, endGrain, twine, rng);
				n++;
			}
		k.CommitTo(_gen, "Firewood", true);
		// the stack, solid (one box round it; its top a slope you can't climb)
		Col(mid + stack * new Vector3(0f, 0.08f + br * 1.9f, 0f), new Vector3(br * 6.2f, br * 3.8f, len), stack);
		Clear(mid, 1.3f);
		HonourBox(mid + stack * new Vector3(br * 3.1f + 0.32f, 0f, 0.1f), yaw);
		GD.Print($"[trailhead] firewood: {n} bundles stacked by the board, and the honour box");
	}

	/// <summary>One bundle: split logs packed round a middle, each a wedge of a round (bark on its curve, pale split faces,
	/// end grain at its ends), a little proud of or short of its neighbours; two bands of twine round it.</summary>
	private static void Bundle(MeshKit k, Vector3 c, Basis b, float len, float r, Material bark, Material split, Material end, Material twine, RandomNumberGenerator rng)
	{
		// the logs: a middle one and a ring of wedges round it, their bark outward
		const int ring = 7;
		for (int i = 0; i < ring; i++)
		{
			float a0 = i * Mathf.Tau / ring;
			float a = a0 + rng.RandfRange(-0.1f, 0.1f);
			float lr = r * rng.RandfRange(0.9f, 1.02f);
			float slide = rng.RandfRange(-0.025f, 0.025f);
			SplitLog(k, c + b * new Vector3(0, 0, slide), b, a, a + Mathf.Tau / ring * rng.RandfRange(0.92f, 1.05f), r * 0.22f, lr, len * rng.RandfRange(0.95f, 1.03f), bark, split, end);
		}
		SplitLog(k, c, b, 0.3f, 0.3f + Mathf.Pi * 0.5f, 0f, r * 0.36f, len * 0.98f, bark, split, end);
		// the twine: two bands round it, a third of the way in from each end
		k.Mat(twine);
		foreach (float z in new[] { -len * 0.27f, len * 0.27f })
			k.Cylinder(c + b * new Vector3(0, 0, z - 0.008f), c + b * new Vector3(0, 0, z + 0.008f), r * 1.035f, r * 1.035f, 12, false);
	}

	/// <summary>A wedge split off a round: the arc from <paramref name="a0"/> to <paramref name="a1"/> (round the bundle's
	/// Z, in <paramref name="b"/>) between radii <paramref name="r0"/> and <paramref name="r1"/>, <paramref name="len"/> long.</summary>
	private static void SplitLog(MeshKit k, Vector3 c, Basis b, float a0, float a1, float r0, float r1, float len, Material bark, Material split, Material end)
	{
		const int seg = 3;
		Vector3 P(float a, float r, float z) => c + b * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
		float hz = len * 0.5f;
		void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, float w, float h)
		{
			if ((p1 - p0).Cross(p2 - p0).Dot(n) < 0f) { (p1, p3) = (p3, p1); }
			k.Quad(p0, p1, p2, p3, n, new Vector2(0, h), new Vector2(w, h), new Vector2(w, 0), new Vector2(0, 0));
		}
		// the bark round its outside
		k.Mat(bark);
		for (int s = 0; s < seg; s++)
		{
			float u0 = Mathf.Lerp(a0, a1, s / (float)seg), u1 = Mathf.Lerp(a0, a1, (s + 1) / (float)seg);
			var n = b * new Vector3(Mathf.Cos((u0 + u1) * 0.5f), Mathf.Sin((u0 + u1) * 0.5f), 0f);
			Face(P(u0, r1, -hz), P(u1, r1, -hz), P(u1, r1, hz), P(u0, r1, hz), n, (u1 - u0) * r1 * 2f, len * 2f);
		}
		// the split faces either side (and its inner face, when it doesn't come to a point)
		k.Mat(split);
		foreach (float a in new[] { a0, a1 })
		{
			var n = b * new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f) * (a == a0 ? -1f : 1f);
			Face(P(a, r0, -hz), P(a, r1, -hz), P(a, r1, hz), P(a, r0, hz), n, (r1 - r0) * 2f, len * 2f);
		}
		if (r0 > 0.001f)
			Face(P(a0, r0, -hz), P(a1, r0, -hz), P(a1, r0, hz), P(a0, r0, hz), b * new Vector3(-Mathf.Cos((a0 + a1) * 0.5f), -Mathf.Sin((a0 + a1) * 0.5f), 0f), (a1 - a0) * r0 * 2f, len * 2f);
		// the ends: the cut grain
		k.Mat(end);
		foreach (float z in new[] { -hz, hz })
		{
			var n = b * new Vector3(0, 0, Mathf.Sign(z));
			for (int s = 0; s < seg; s++)
			{
				float u0 = Mathf.Lerp(a0, a1, s / (float)seg), u1 = Mathf.Lerp(a0, a1, (s + 1) / (float)seg);
				var q0 = P(u0, r0, z); var q1 = P(u0, r1, z); var q2 = P(u1, r1, z); var q3 = P(u1, r0, z);
				Vector2 Uv(Vector3 q) { var l = b.Inverse() * (q - c); return new Vector2(l.X * 2f + 0.5f, l.Y * 2f + 0.5f); }
				if ((q1 - q0).Cross(q2 - q0).Dot(n) < 0f) { (q1, q3) = (q3, q1); }
				k.Quad(q0, q1, q2, q3, n, Uv(q0), Uv(q1), Uv(q2), Uv(q3));
			}
		}
	}

	/// <summary>The honour box: a small slat box on a post, a padlocked hasp, a slot in its sloped lid; HONOR BOX and the
	/// price stencilled on its face to the lot.</summary>
	private void HonourBox(Vector3 at, Basis yaw)
	{
		at.Y = H(at.X, at.Z);
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(PropTextures.PostMat);
		k.Box(at + Vector3.Up * 0.42f, new Vector3(0.09f, 0.9f, 0.09f), 1.5f, yaw);
		var box = at + Vector3.Up * 0.98f;
		k.Mat(BuildingTextures.CabinTrimMat);
		k.BevelBox(box, new Vector3(0.3f, 0.24f, 0.22f), 0.012f, 1.5f, yaw);
		var lid = yaw * new Basis(Vector3.Right, Mathf.DegToRad(-8f));
		k.BevelBox(box + Vector3.Up * 0.135f, new Vector3(0.34f, 0.03f, 0.26f), 0.008f, 1.5f, lid);
		k.Mat(ProcTextures.Flat("th_slot", new Color(0.02f, 0.02f, 0.02f), 1f, 0f));
		k.Box(box + Vector3.Up * 0.152f + yaw.Z * 0.02f, new Vector3(0.12f, 0.004f, 0.016f), 1f, lid);
		// the hasp and its padlock on the front
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.45f, 0.4f, 0.35f);
		k.Box(box + yaw.Z * 0.114f + Vector3.Up * 0.06f, new Vector3(0.03f, 0.08f, 0.008f), 1f, yaw);
		k.Box(box + yaw.Z * 0.125f + Vector3.Up * 0.0f, new Vector3(0.045f, 0.05f, 0.02f), 1f, yaw);
		k.Color = Colors.White;
		k.CommitTo(_gen, "HonourBox", true);
		Col(at + Vector3.Up * 0.55f, new Vector3(0.32f, 1.1f, 0.24f), yaw);
		var face = box + yaw.Z * 0.112f;
		SignKit.Text(_gen, "HONOR BOX", face + Vector3.Up * 0.05f, yaw, 0.045f, SignKit.Paint);
		SignKit.Text(_gen, "$2 a bundle - pay here", face - Vector3.Up * 0.04f, yaw, 0.026f, SignKit.Paint * 0.9f);
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
