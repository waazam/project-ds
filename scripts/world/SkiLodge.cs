using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The ski lodge at the end of Act 22's plowed road (the owner's references: a great mountain lodge, stone
/// below and dark timber above, steep snow-heavy roofs, a tall hexagonal hall at its heart with a lantern and a
/// vane on top; Act 23 happens inside). Winter has it: snow deep on every roof, icicles along every eave, the
/// windows black. The front doors are chained shut from inside; round the back, a service door stands open a
/// crack, drifted up and iced into its frame. Forcing it (a short cutscene: three shoves, the ice cracking, the
/// last one splitting it off the frame, the door swinging in on the dark) is Act 22's end.
///
/// Local space: the hall's centre at ground level; the front faces +Z (up the road the player came down).
/// It sits in WinterWoods' space unrotated, so church-local = <see cref="OriginLocal"/> + local.
/// </summary>
public partial class SkiLodge : Node3D
{
	// ---- the plan
	public const float HexR = 12f, HexWall = 9f, HexPeak = 26f, StoneTop = 3.2f;
	public const float WingX0 = 6f, WingX1 = 44f, WingHalfZ = 8f, WingWall = 7.5f, WingRidge = 14.5f;
	/// <summary>The back door: in the west wing's back wall.</summary>
	public const float BackDoorX = -30f, BackDoorW = 1.15f, BackDoorH = 2.25f;
	/// <summary>The mudroom behind it (Act 23 goes on from here).</summary>
	public const float RoomX0 = -33.3f, RoomX1 = -26.7f, RoomZ1 = -1.7f;
	public static float Apothem => HexR * 0.8660254f;

	/// <summary>The lodge's origin, church-local: 36 m past the road's end, at the road's end's height.</summary>
	public static Vector3 OriginLocal
	{
		get
		{
			var e = WinterWoods.RoadEnd;
			return new Vector3(e.X, WinterWoods.RoadEndY, e.Y - 36f);
		}
	}

	public Vector3 FrontDoorWorld => ToGlobal(new Vector3(0, 0.05f, Apothem + 0.5f));
	public Vector3 FrontStandWorld => ToGlobal(new Vector3(0, 0.05f, Apothem + 3.2f));
	public Vector3 BackDoorWorld => ToGlobal(new Vector3(BackDoorX, 1.2f, -WingHalfZ));
	public Vector3 BackStandWorld => ToGlobal(new Vector3(BackDoorX, 0.05f, -WingHalfZ - 1.9f));
	public Vector3 MudroomWorld => ToGlobal(new Vector3(BackDoorX, 0.05f, -WingHalfZ + 3.2f));
	public PickupInteractable FrontUse { get; private set; }
	public PickupInteractable BackUse { get; private set; }
	public bool BackOpen { get; private set; }
	public int Shoves { get; private set; }

	private Node3D _backHinge, _iceSheet, _frontMarker, _backMarker;
	private readonly List<Node3D> _doorIcicles = new();
	private StaticBody3D _body, _doorBody;
	private readonly RandomNumberGenerator _rng = new() { Seed = 2323 };

	// ------------------------------------------------------------------ the ground round it

	/// <summary>The lodge's pad: the snow round it is level (it was built on a shelf).</summary>
	public static float Flatten(float x, float z, float h)
	{
		var o = OriginLocal;
		float lx = x - o.X, lz = z - o.Z;
		float dx = Mathf.Max(Mathf.Abs(lx) - (WingX1 + 12f), 0f);
		float dz = Mathf.Max(Mathf.Max(-WingHalfZ - 14f - lz, lz - (Apothem + 26f)), 0f);
		float w = 1f - Mathf.SmoothStep(0f, 16f, new Vector2(dx, dz).Length());
		return Mathf.Lerp(h, o.Y, w);
	}

	/// <summary>The turning circle in front (plowed).</summary>
	public static float CircleWeight(float x, float z)
	{
		var o = OriginLocal;
		float d = new Vector2(x - o.X, z - (o.Z + Apothem + 13f)).Length();
		return 1f - Mathf.SmoothStep(10.5f, 12.5f, d);
	}

	public static bool NearLodge(float x, float z, float r)
	{
		var o = OriginLocal;
		return new Vector2(x - o.X, z - o.Z).Length() < r;
	}

	/// <summary>Is a world point inside the lodge (its mudroom)?</summary>
	public bool Inside(Vector3 world) => InsideLocal(ToLocal(world));

	/// <summary>In the mudroom (just in from the back door).</summary>
	public bool InMudroom(Vector3 world)
	{
		var l = ToLocal(world);
		return l.X > RoomX0 && l.X < RoomX1 && l.Z > -WingHalfZ + 0.1f && l.Z < RoomZ1 && l.Y > -1f && l.Y < 4f;
	}

	// ------------------------------------------------------------------ building

	public override void _Ready()
	{
		_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "stone");
		AddChild(_body);
		BuildHall();
		BuildWings();
		BuildPorch();
		BuildBackDoor();
		BuildMudroom();
		BuildInterior();
		BuildFurnishings();
		StartAct23();
		BuildIcicles();
		BuildDrifts();
		_frontMarker = new Node3D { Name = "FrontMarker", Position = new Vector3(0, 1.4f, Apothem + 1.2f) };
		AddChild(_frontMarker);
		_frontMarker.AddToGroup("lodge_front_marker");
		_backMarker = new Node3D { Name = "BackMarker", Position = new Vector3(BackDoorX, 1.4f, -WingHalfZ - 0.5f) };
		AddChild(_backMarker);
		_backMarker.AddToGroup("lodge_back_marker");
		if (StoryManager.Instance is { } s && (s.HasFlag(StoryManager.Flag.LodgeBackDoorOpen) || s.Current >= Checkpoint.Act22Finished))
			OpenBackDoor(instant: true);
	}

	private void Col(Vector3 c, Vector3 size, float yaw = 0f)
		=> _body.AddChild(new CollisionShape3D { Position = c, Rotation = new Vector3(0, yaw, 0), Shape = new BoxShape3D { Size = size } });

	/// <summary>A wall from a to b (ground points), stone to <see cref="StoneTop"/>, dark boards to <paramref name="top"/>,
	/// facing <paramref name="outward"/>, with dark small-paned windows along its timber storey.</summary>
	private void Wall(MeshKit k, Vector3 a, Vector3 b, float top, Vector3 outward, float thick, bool windows = true, float windowEvery = 3.2f)
	{
		Vector3 along = (b - a).Normalized();
		float len = a.DistanceTo(b);
		var basis = new Basis(along, Vector3.Up, outward);
		Vector3 mid = (a + b) * 0.5f;
		k.Mat(BuildingTextures.StoneMat);
		k.Color = new Color(0.62f, 0.62f, 0.66f);
		k.Box(mid + Vector3.Up * StoneTop * 0.5f, new Vector3(len, StoneTop, thick), 0.5f, basis);
		k.Mat(LodgeTimber);
		k.Color = Colors.White;
		k.Box(mid + Vector3.Up * (StoneTop + (top - StoneTop) * 0.5f) - outward * 0.05f, new Vector3(len, top - StoneTop, thick - 0.1f), 0.5f, basis);
		// a timber sill between the two (on the outside only: through the whole wall it showed inside the dining
		// hall as a black band through the panelling)
		k.Mat(LodgeTrim);
		k.Box(mid + Vector3.Up * (StoneTop + 0.08f) + outward * (thick * 0.25f + 0.04f), new Vector3(len + 0.1f, 0.16f, thick * 0.5f + 0.08f), 1f, basis);
		if (!windows) return;
		int n = Mathf.FloorToInt(len / windowEvery);
		for (int i = 0; i < n; i++)
		{
			float u = -len * 0.5f + (i + 0.5f) * len / n;
			Vector3 c = mid + along * u + outward * (thick * 0.5f + 0.02f);
			Window(k, c + Vector3.Up * (StoneTop + (top - StoneTop) * 0.52f), along, outward, 1.1f, Mathf.Min(1.7f, (top - StoneTop) * 0.55f));
			Window(k, c + Vector3.Up * 1.7f, along, outward, 0.9f, 1.1f);
		}
		Col(mid + Vector3.Up * top * 0.5f, new Vector3(len, top, thick), Mathf.Atan2(along.Z, along.X) * -1f);
	}

	/// <summary>A window: black glass with a faint frost, a white-painted frame and mullions.</summary>
	private void Window(MeshKit k, Vector3 c, Vector3 along, Vector3 outward, float w, float h)
	{
		k.Mat(WindowGlass);
		k.Color = Colors.White;
		k.Quad(c - along * w * 0.5f - Vector3.Up * h * 0.5f, c + along * w * 0.5f - Vector3.Up * h * 0.5f, c + along * w * 0.5f + Vector3.Up * h * 0.5f, c - along * w * 0.5f + Vector3.Up * h * 0.5f, outward);
		k.Mat(LodgeTrim);
		var basis = new Basis(along, Vector3.Up, outward);
		Vector3 o = outward * 0.04f;
		k.Box(c + o + Vector3.Up * h * 0.5f, new Vector3(w + 0.16f, 0.1f, 0.08f), 1f, basis);
		k.Box(c + o - Vector3.Up * h * 0.5f, new Vector3(w + 0.22f, 0.12f, 0.12f), 1f, basis);
		k.Box(c + o - along * w * 0.5f, new Vector3(0.08f, h, 0.08f), 1f, basis);
		k.Box(c + o + along * w * 0.5f, new Vector3(0.08f, h, 0.08f), 1f, basis);
		k.Box(c + o, new Vector3(0.04f, h, 0.04f), 1f, basis);
		k.Box(c + o, new Vector3(w, 0.04f, 0.04f), 1f, basis);
		// snow on the sill
		k.Mat(RoofSnow);
		k.Box(c + o * 1.5f - Vector3.Up * (h * 0.5f - 0.08f), new Vector3(w + 0.1f, 0.06f, 0.14f), 1f, basis);
	}

	private static Vector3 HexVert(int i) { float a = Mathf.DegToRad(30f + 60f * i); return new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * HexR; }

	private void BuildHall()
	{
		var k = new MeshKit();
		// the six walls, cut for their doors and windows (SkiLodge.Interior.cs: the lobby is inside them)
		BuildHallWalls(k);
		// the great steep roof: six snow-laden faces up to a lantern
		const float eave = 1.3f;
		float lanternR = 1.8f, lanternTop = HexPeak + 3.2f;
		for (int i = 0; i < 6; i++)
		{
			Vector3 a = HexVert(i) * ((HexR + eave) / HexR) + Vector3.Up * (HexWall - 0.5f), b = HexVert(i + 1) * ((HexR + eave) / HexR) + Vector3.Up * (HexWall - 0.5f);
			Vector3 ta = HexVert(i) * (lanternR / HexR) + Vector3.Up * HexPeak, tb = HexVert(i + 1) * (lanternR / HexR) + Vector3.Up * HexPeak;
			var n = (tb - a).Cross(b - a).Normalized();
			if (n.Y < 0) n = -n;
			k.Mat(RoofSnow);
			k.Quad(a, b, tb, ta, n, new Vector2(0, 0), new Vector2(a.DistanceTo(b) * 0.25f, 0), new Vector2(a.DistanceTo(b) * 0.25f * 0.2f, 4f), new Vector2(0, 4f));
			// underside: the shingles' dark
			k.Mat(BuildingTextures.ShingleMat);
			k.Color = new Color(0.35f, 0.33f, 0.32f);
			k.Quad(ta, tb, b - Vector3.Up * 0.25f, a - Vector3.Up * 0.25f, -n);
			// a fascia board along the eave
			k.Mat(LodgeTrim);
			k.Color = Colors.White;
			k.Quad(a - Vector3.Up * 0.28f, b - Vector3.Up * 0.28f, b, a, (a + b).Normalized() with { Y = 0 });
			// the lantern's walls and its own little roof
			Vector3 la = HexVert(i) * (lanternR / HexR) + Vector3.Up * HexPeak, lb = HexVert(i + 1) * (lanternR / HexR) + Vector3.Up * HexPeak;
			Vector3 lo = (((la + lb) * 0.5f) with { Y = 0 }).Normalized();
			k.Mat(LodgeTimber);
			k.Quad(lb, la, la + Vector3.Up * 2.2f, lb + Vector3.Up * 2.2f, lo);
			k.Mat(WindowGlass);
			k.Quad(lb.Lerp(la, 0.2f) + Vector3.Up * 0.5f + lo * 0.02f, lb.Lerp(la, 0.8f) + Vector3.Up * 0.5f + lo * 0.02f, lb.Lerp(la, 0.8f) + Vector3.Up * 1.8f + lo * 0.02f, lb.Lerp(la, 0.2f) + Vector3.Up * 1.8f + lo * 0.02f, lo);
			k.Mat(RoofSnow);
			Vector3 ra = (la * 1.25f) with { Y = HexPeak + 2.2f }, rb = (lb * 1.25f) with { Y = HexPeak + 2.2f };
			var rn = (Vector3.Up * lanternTop - ra).Cross(rb - ra).Normalized();
			if (rn.Y < 0) rn = -rn;
			k.Tri(ra, rb, Vector3.Up * lanternTop, rn, Vector2.Zero, Vector2.Right, Vector2.Down);
		}
		// the spire and its vane: a thin iron rod, a crossbar, an arrow (it points at the church)
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.3f, 0.3f, 0.32f);
		k.Cylinder(Vector3.Up * lanternTop, Vector3.Up * (lanternTop + 3.6f), 0.06f, 0.03f, 6, true);
		k.Beam(new Vector3(-0.7f, lanternTop + 2.2f, 0), new Vector3(0.7f, lanternTop + 2.2f, 0), 0.04f, 0.04f);
		k.Beam(new Vector3(0, lanternTop + 2.2f, -0.7f), new Vector3(0, lanternTop + 2.2f, 0.7f), 0.04f, 0.04f);
		k.Beam(new Vector3(0, lanternTop + 3.0f, -1.1f), new Vector3(0, lanternTop + 3.0f, 1.1f), 0.05f, 0.02f);
		k.Tri(new Vector3(0, lanternTop + 3.0f, 1.3f), new Vector3(0, lanternTop + 3.2f, 0.9f), new Vector3(0, lanternTop + 2.8f, 0.9f), Vector3.Right, Vector2.Zero, Vector2.Right, Vector2.Down);
		// a great stone chimney through the roof
		k.Mat(BuildingTextures.StoneMat);
		k.Color = new Color(0.55f, 0.55f, 0.58f);
		// (over the lobby's fireplace, on the back-east side: it rises out of the roof there)
		// It stands flush over the lobby's chimney breast (the breast's stack runs up inside it): its inner face on the
		// breast's face, 9.24 m out, its foot where the roof crosses that face (10.9 m), so nothing of it shows
		// in the lobby under the roof and no gap shows under it outside.
		Vector3 radial = ((HexVert(1) + HexVert(2)) * 0.5f).Normalized();
		Vector3 chim = radial * 9.94f;
		k.Box(chim + Vector3.Up * 20.45f, new Vector3(1.4f, 19.1f, 2.4f), 0.5f, new Basis(Vector3.Up, Mathf.Pi / 6f));
		k.Mat(RoofSnow);
		k.Box(chim + Vector3.Up * 30.1f, new Vector3(1.6f, 0.25f, 2.6f), 1f, new Basis(Vector3.Up, Mathf.Pi / 6f));
		// one window high over the doors is lit, dimly, as if by a candle: the only light at the road's end (nobody should be here)
		k.Mat(LitWindow);
		{
			Vector3 front = (HexVert(0) + HexVert(5)) * 0.5f, fo = Vector3.Back;
			Vector3 c = front + fo * 0.43f + Vector3.Up * 6.3f + Vector3.Right * 2.6f;
			k.Quad(c + new Vector3(-0.58f, -0.88f, 0), c + new Vector3(0.58f, -0.88f, 0), c + new Vector3(0.58f, 0.88f, 0), c + new Vector3(-0.58f, 0.88f, 0), fo);
		}
		k.CommitTo(this, "Hall", true);
	}

	private void BuildWings()
	{
		var k = new MeshKit();
		foreach (float s in new[] { -1f, 1f })
		{
			// (the walls start where the hall's wall is, not at the wing's nominal start inside the hall: their ends
			// stood into the lobby's corners)
			float x0 = s * WingX0, x1 = s * WingX1, xw = s * (WingX0 + 1.4f);
			float lo = Mathf.Min(xw, x1), hi = Mathf.Max(xw, x1);
			// front and back walls (the west wing's back wall has the back door in it)
			Wall(k, new Vector3(lo, 0, WingHalfZ), new Vector3(hi, 0, WingHalfZ), WingWall, Vector3.Back, 0.7f);
			if (s < 0)
			{
				float d0 = BackDoorX - BackDoorW * 0.5f - 0.15f, d1 = BackDoorX + BackDoorW * 0.5f + 0.15f;
				Wall(k, new Vector3(d0, 0, -WingHalfZ), new Vector3(lo, 0, -WingHalfZ), WingWall, Vector3.Forward, 0.7f);
				Wall(k, new Vector3(hi, 0, -WingHalfZ), new Vector3(d1, 0, -WingHalfZ), WingWall, Vector3.Forward, 0.7f);
				// over the door: stone to the sill, timber above (no windows)
				var mid = new Vector3(BackDoorX, 0, -WingHalfZ);
				k.Mat(BuildingTextures.StoneMat);
				k.Color = new Color(0.62f, 0.62f, 0.66f);
				k.Box(mid + Vector3.Up * ((BackDoorH + StoneTop) * 0.5f), new Vector3(d1 - d0, StoneTop - BackDoorH, 0.7f), 0.5f);
				k.Mat(LodgeTimber);
				k.Color = Colors.White;
				k.Box(mid + Vector3.Up * (StoneTop + (WingWall - StoneTop) * 0.5f) + Vector3.Back * 0.05f, new Vector3(d1 - d0, WingWall - StoneTop, 0.6f), 0.5f);
				Col(mid + Vector3.Up * ((BackDoorH + WingWall) * 0.5f), new Vector3(d1 - d0, WingWall - BackDoorH, 0.7f));
			}
			else Wall(k, new Vector3(hi, 0, -WingHalfZ), new Vector3(lo, 0, -WingHalfZ), WingWall, Vector3.Forward, 0.7f);
			// the far end wall
			Wall(k, new Vector3(x1, 0, s * WingHalfZ), new Vector3(x1, 0, -s * WingHalfZ), WingWall, new Vector3(s, 0, 0), 0.7f, true, 4f);
			// the steep roof: two snow slopes to a ridge along the wing, gables at the end. Its inner end follows the
			// hall's outer wall (the facets either side of the vertex at the ridge): from the wing's nominal start the
			// slopes ran on into the lobby and their dark undersides crossed its upper walls.
			const float over = 1f;
			float ex = x1 + s * over;
			float FacetX(float z) => s * (HexR - Mathf.Abs(z) * (HexR * 0.5f) / Apothem);   // the hall's outer wall at z
			float eaveZ = WingHalfZ + over;
			Vector3 fa = new(FacetX(eaveZ), WingWall - 0.4f, eaveZ), fb = new(ex, WingWall - 0.4f, eaveZ);
			Vector3 ra = new(s * HexR, WingRidge, 0), rb = new(ex, WingRidge, 0);
			Vector3 ba = new(FacetX(eaveZ), WingWall - 0.4f, -eaveZ), bb = new(ex, WingWall - 0.4f, -eaveZ);
			k.Mat(RoofSnow);
			k.Color = Colors.White;
			var nf = new Vector3(0, WingHalfZ + over, WingRidge - WingWall + 0.4f).Normalized();
			var nb = new Vector3(0, WingHalfZ + over, -(WingRidge - WingWall + 0.4f)).Normalized();
			Vector2 U(Vector3 p, float v) => new(Mathf.Abs(p.X) * 0.25f, v);
			k.Quad(fa, fb, rb, ra, nf, U(fa, 0), U(fb, 0), U(rb, 3f), U(ra, 3f));
			k.Quad(bb, ba, ra, rb, nb, U(bb, 0), U(ba, 0), U(ra, 3f), U(rb, 3f));
			k.Mat(BuildingTextures.ShingleMat);
			k.Color = new Color(0.35f, 0.33f, 0.32f);
			k.Quad(ra, rb, fb - Vector3.Up * 0.25f, fa - Vector3.Up * 0.25f, -nf);
			k.Quad(rb, ra, ba - Vector3.Up * 0.25f, bb - Vector3.Up * 0.25f, -nb);
			// where the roof meets the hall: timber filling the wing's section above the hall's wall top, on the hall's
			// two outer facets (the hall's own roof slopes away inward from there)
			{
				k.Mat(LodgeTimber);
				k.Color = Colors.White;
				float zWall = eaveZ * (1f - (HexWall - (WingWall - 0.4f)) / (WingRidge - (WingWall - 0.4f)));   // where the slope passes the wall top
				foreach (float sz in new[] { 1f, -1f })
				{
					Vector3 j0 = new(FacetX(zWall), HexWall, sz * zWall), j1 = new(s * HexR, HexWall, 0), j2 = new(s * HexR, WingRidge, 0);
					Vector3 jn = new Vector3(s * 0.5f, 0, sz * 0.866f);
					float w = j0.DistanceTo(j1) * 0.5f, h = (WingRidge - HexWall) * 0.5f;
					// both faces (it's seen from outside; from the wing's attic side it's never seen)
					k.Tri(j0, j1, j2, jn, Vector2.Zero, new Vector2(w, 0), new Vector2(w, h));
					k.Tri(j0, j2, j1, -jn, Vector2.Zero, new Vector2(w, h), new Vector2(w, 0));
				}
			}
			// the gable: timber, a small window, and the snow's edge over it
			k.Mat(LodgeTimber);
			k.Color = Colors.White;
			var gOut = new Vector3(s, 0, 0);
			Vector3 g0 = new(x1, WingWall, WingHalfZ), g1 = new(x1, WingWall, -WingHalfZ), g2 = new(x1, WingRidge - 0.3f, 0);
			k.Tri(g0, g1, g2, gOut, Vector2.Zero, Vector2.Right, Vector2.Down);
			Window(k, new Vector3(x1 + s * 0.04f, WingWall + 2.3f, 0), new Vector3(0, 0, -s), gOut, 0.9f, 1.3f);
			k.Mat(LodgeTrim);
			k.Beam(new Vector3(ex, WingWall - 0.5f, WingHalfZ + over), new Vector3(ex, WingRidge, 0), 0.3f, 0.12f);
			k.Beam(new Vector3(ex, WingWall - 0.5f, -WingHalfZ - over), new Vector3(ex, WingRidge, 0), 0.3f, 0.12f);
			// dormers on the front slope
			for (int d = 0; d < 3; d++)
			{
				float dx = Mathf.Lerp(x0 + s * 9f, x1 - s * 7f, d / 2f);
				Dormer(k, new Vector3(dx, 0, 0));
			}
			// two stone chimneys
			k.Mat(BuildingTextures.StoneMat);
			k.Color = new Color(0.55f, 0.55f, 0.58f);
			foreach (float cx in new[] { x0 + s * 14f, x1 - s * 4f })
			{
				// only above the rooms' ceilings (the stack runs up through the attic and out of the roof)
				k.Box(new Vector3(cx, (RoomTop + 0.2f + WingRidge + 2f) * 0.5f, -2.5f), new Vector3(1.4f, WingRidge + 2f - RoomTop - 0.2f, 1.4f), 0.5f);
				k.Mat(RoofSnow);
				k.Box(new Vector3(cx, WingRidge + 2.05f, -2.5f), new Vector3(1.55f, 0.18f, 1.55f), 1f);
				k.Mat(BuildingTextures.StoneMat);
			}
		}
		k.CommitTo(this, "Wings", true);
	}

	/// <summary>A small gabled dormer window standing out of the front roof slope.</summary>
	private void Dormer(MeshKit k, Vector3 at)
	{
		float z0 = WingHalfZ - 1.6f, z1 = 2.2f, y0 = WingWall + 1.1f, y1 = y0 + 2.2f, w = 1.3f;
		k.Mat(LodgeTimber);
		k.Color = Colors.White;
		k.Box(new Vector3(at.X, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(w * 2f, y1 - y0, z0 - z1), 0.5f);
		Window(k, new Vector3(at.X, y0 + 1.05f, z0 + 0.02f), Vector3.Right, Vector3.Back, 1.1f, 1.2f);
		k.Mat(RoofSnow);
		Vector3 a = new(at.X - w - 0.3f, y1, z0 + 0.4f), b = new(at.X + w + 0.3f, y1, z0 + 0.4f), r0 = new(at.X, y1 + 1.2f, z0 + 0.4f), r1 = new(at.X, y1 + 1.2f, z1);
		Vector3 a1 = a with { Z = z1 }, b1 = b with { Z = z1 };
		k.Quad(a, r0, r1, a1, new Vector3(-1.2f, w + 0.3f, 0).Normalized());
		k.Quad(r0, b, b1, r1, new Vector3(1.2f, w + 0.3f, 0).Normalized());
		k.Mat(LodgeTrim);
		k.Tri(a, b, r0, Vector3.Back, Vector2.Zero, Vector2.Right, Vector2.Down);
	}

	private void BuildPorch()
	{
		var k = new MeshKit();
		float z0 = Apothem, z1 = Apothem + 5.2f;
		// steps up to a stone landing
		k.Mat(BuildingTextures.StoneMat);
		k.Color = new Color(0.58f, 0.58f, 0.62f);
		k.Box(new Vector3(0, -0.05f, (z0 + z1) * 0.5f), new Vector3(7.4f, 0.3f, z1 - z0), 0.5f);   // a low stone landing, one easy step
		Col(new Vector3(0, -0.05f, (z0 + z1) * 0.5f), new Vector3(7.4f, 0.3f, z1 - z0));
		// two squat stone piers and a heavy timber beam, a steep little snow roof over them
		foreach (float x in new[] { -3.1f, 3.1f })
		{
			k.Mat(BuildingTextures.StoneMat);
			k.Box(new Vector3(x, 2.3f, z1 - 0.5f), new Vector3(0.9f, 4.6f, 0.9f), 0.5f);
			Col(new Vector3(x, 2.3f, z1 - 0.5f), new Vector3(0.9f, 4.6f, 0.9f));
		}
		k.Mat(LodgeTrim);
		k.Box(new Vector3(0, 4.8f, z1 - 0.5f), new Vector3(7.6f, 0.45f, 0.5f), 1f);
		k.Mat(RoofSnow);
		Vector3 l0 = new(-4.2f, 5f, z1 + 0.5f), r0 = new(4.2f, 5f, z1 + 0.5f), p0 = new(0, 8.4f, z1 + 0.5f);
		Vector3 l1 = l0 with { Z = z0 }, r1 = r0 with { Z = z0 }, p1 = p0 with { Z = z0 };
		k.Quad(l0, p0, p1, l1, new Vector3(-3.4f, 4.2f, 0).Normalized());
		k.Quad(p0, r0, r1, p1, new Vector3(3.4f, 4.2f, 0).Normalized());
		k.Mat(LodgeTimber);
		k.Tri(l0 + Vector3.Down * 0.02f, r0 + Vector3.Down * 0.02f, p0 + Vector3.Down * 0.2f, Vector3.Back, Vector2.Zero, Vector2.Right, Vector2.Down);
		// the great doors: two dark leaves, iron straps, a chain wound through their handles from inside the glass
		k.Mat(LodgeDoor);
		k.Color = Colors.White;
		Vector3 dc = new(0, 1.55f, z0 + 0.42f);
		k.Box(dc, new Vector3(2.6f, 3.1f, 0.12f), 0.5f);
		k.Mat(LodgeTrim);
		k.Box(dc + new Vector3(0, 1.62f, 0.04f), new Vector3(3.0f, 0.2f, 0.2f), 1f);
		k.Box(dc + new Vector3(-1.4f, 0, 0.04f), new Vector3(0.2f, 3.2f, 0.2f), 1f);
		k.Box(dc + new Vector3(1.4f, 0, 0.04f), new Vector3(0.2f, 3.2f, 0.2f), 1f);
		k.Box(dc + new Vector3(0, 0, 0.07f), new Vector3(0.05f, 3.1f, 0.03f), 1f);
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.35f, 0.35f, 0.38f);
		foreach (float y in new[] { -0.9f, 0.9f })
			k.Box(dc + new Vector3(0, y, 0.08f), new Vector3(2.5f, 0.08f, 0.03f), 1f);
		foreach (float x in new[] { -0.18f, 0.18f })
			k.Cylinder(dc + new Vector3(x, 0.05f, 0.08f), dc + new Vector3(x, 0.05f, 0.18f), 0.05f, 0.05f, 6, true);
		// the chain through the handles, and its padlock
		for (int i = 0; i < 9; i++)
		{
			float u = (i - 4) / 4f;
			Vector3 p = dc + new Vector3(u * 0.3f, 0.05f - Mathf.Cos(u * 1.2f) * 0.06f + 0.06f, 0.2f);
			k.Cylinder(p - new Vector3(0.03f, 0, 0), p + new Vector3(0.03f, 0, 0), 0.02f, 0.02f, 4, true);
		}
		k.Box(dc + new Vector3(0, -0.12f, 0.21f), new Vector3(0.12f, 0.15f, 0.05f), 1f);
		// the name, carved over the doors: HOLLOW PEAK LODGE
		k.CommitTo(this, "Porch", true);
		SignKit.Text(this, "HOLLOW PEAK LODGE", new Vector3(0, 5.45f, z1 - 0.2f), Basis.Identity, 0.36f, new Color(0.62f, 0.55f, 0.44f));
		FrontUse = new PickupInteractable
		{
			Name = "FrontDoorUse", PickRadius = 1.3f, MaxDistance = 3f, Position = dc + new Vector3(0, 0, 0.3f),
			PromptFor = _ => "Chained shut from inside.", CanUse = _ => true,
		};
		FrontUse.Interacted += OnFrontDoor;
		AddChild(FrontUse);
	}

	private void OnFrontDoor(PlayerController player)
	{
		AudioDirector.OneShot(this, "door_locked", 2, FrontDoorWorld + Vector3.Up * 1.2f, -2f);
		AudioDirector.OneShot(this, "haunt_chain", 2, FrontDoorWorld + Vector3.Up * 1.2f, -8f);
		if (StoryManager.Instance is { } s && !s.HasFlag(StoryManager.Flag.LodgeFrontTried))
		{
			s.SetFlag(StoryManager.Flag.LodgeFrontTried);
			GD.Print("[story] Act 22: the lodge's front doors are chained from inside - round the back");
			_ = StoryBeat.Caption(this, "Chained. From the inside.", 0.4f, 2.6f, 1f);
		}
	}

	// ------------------------------------------------------------------ the back door

	private void BuildBackDoor()
	{
		var k = new MeshKit();
		float z = -WingHalfZ;
		// the frame
		k.Mat(LodgeTrim);
		k.Color = Colors.White;
		k.Box(new Vector3(BackDoorX - BackDoorW * 0.5f - 0.08f, BackDoorH * 0.5f, z - 0.38f), new Vector3(0.16f, BackDoorH, 0.12f), 1f);
		k.Box(new Vector3(BackDoorX + BackDoorW * 0.5f + 0.08f, BackDoorH * 0.5f, z - 0.38f), new Vector3(0.16f, BackDoorH, 0.12f), 1f);
		k.Box(new Vector3(BackDoorX, BackDoorH + 0.08f, z - 0.38f), new Vector3(BackDoorW + 0.32f, 0.16f, 0.12f), 1f);
		// a little snow hood over it
		k.Mat(RoofSnow);
		k.Box(new Vector3(BackDoorX, BackDoorH + 0.45f, z - 0.75f), new Vector3(BackDoorW + 1f, 0.2f, 0.9f), 1f, new Basis(Vector3.Right, -0.25f));
		k.CommitTo(this, "BackDoorFrame", true);
		// the leaf, hung on its left jamb, open a crack (it swings in)
		_backHinge = new Node3D { Name = "BackDoorHinge", Position = new Vector3(BackDoorX - BackDoorW * 0.5f, 0, z - 0.3f), Rotation = new Vector3(0, -0.14f, 0) };
		AddChild(_backHinge);
		var d = new MeshKit();
		d.Mat(LodgeDoor);
		d.Color = new Color(0.8f, 0.8f, 0.85f);
		d.Box(new Vector3(BackDoorW * 0.5f, BackDoorH * 0.5f, 0), new Vector3(BackDoorW - 0.04f, BackDoorH - 0.04f, 0.07f), 0.5f);
		d.Mat(LodgeTrim);
		foreach (float y in new[] { 0.35f, 1.1f, 1.85f })
			d.Box(new Vector3(BackDoorW * 0.5f, y, -0.05f), new Vector3(BackDoorW - 0.1f, 0.1f, 0.03f), 1f);
		d.Mat(BuildingTextures.IronMat);
		d.Cylinder(new Vector3(BackDoorW - 0.12f, 1.05f, -0.05f), new Vector3(BackDoorW - 0.12f, 1.05f, -0.14f), 0.035f, 0.035f, 6, true);
		d.CommitTo(_backHinge, "Leaf", true);
		_doorBody = new StaticBody3D { Name = "DoorBody", CollisionLayer = 1, CollisionMask = 0 };
		_doorBody.SetMeta("surface", "wood");
		_doorBody.AddChild(new CollisionShape3D { Position = new Vector3(BackDoorW * 0.5f, BackDoorH * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(BackDoorW, BackDoorH, 0.1f) } });
		_backHinge.AddChild(_doorBody);
		// the ice: a glazed sheet over the gap and the lower half of the door, and a lip of ice along the sill
		_iceSheet = new Node3D { Name = "Ice" };
		AddChild(_iceSheet);
		var ice = new MeshKit();
		ice.Mat(WinterWoods.IceMat);
		ice.Color = Colors.White;
		// lumps and runs of it, never a clean sheet: thick along the gap and the sill, thinning up the door
		for (int i = 0; i < 9; i++)
		{
			float y = 0.15f + i * 0.2f;
			float w = Mathf.Lerp(0.5f, 0.12f, i / 8f);
			ice.Blob(new Vector3(BackDoorX + BackDoorW * 0.5f - 0.05f + _rng.RandfRange(-0.04f, 0.04f), y, z - 0.44f), new Vector3(w * 0.4f, 0.16f, 0.07f), 700 + i, 0.35f, false, 1f);
			if (i < 5) ice.Blob(new Vector3(BackDoorX + _rng.RandfRange(-0.35f, 0.3f), y * 0.8f, z - 0.45f), new Vector3(w, 0.13f, 0.05f), 720 + i, 0.4f, false, 1f);
		}
		ice.Blob(new Vector3(BackDoorX, 0.06f, z - 0.55f), new Vector3(0.75f, 0.14f, 0.28f), 71, 0.3f, true, 1f);
		ice.CommitTo(_iceSheet, "Sheet", false);
		// icicles off the hood: the ones the door brings down
		for (int i = 0; i < 7; i++)
		{
			var ic = new Node3D { Name = $"DoorIcicle{i}", Position = new Vector3(BackDoorX - 0.8f + i * 0.27f + _rng.RandfRange(-0.05f, 0.05f), BackDoorH + 0.34f - (i % 2) * 0.05f, z - 1.05f + _rng.RandfRange(-0.05f, 0.05f)) };
			AddChild(ic);
			var ik = new MeshKit();
			ik.Mat(WinterWoods.IceMat);
			float len = _rng.RandfRange(0.25f, 0.7f);
			ik.Cylinder(Vector3.Zero, Vector3.Down * len, 0.035f, 0.004f, 5, false);
			ik.CommitTo(ic, "Icicle", false);
			_doorIcicles.Add(ic);
		}
		BackUse = new PickupInteractable
		{
			Name = "BackDoorUse", PickRadius = 1.0f, MaxDistance = 2.6f, Position = new Vector3(BackDoorX, 1.2f, z - 0.6f),
			PromptFor = _ => BackOpen ? "" : "Force the door",
			CanUse = _ => !BackOpen,
		};
		BackUse.Interacted += p => { if (!BackOpen && !_forcing) _ = Cutscene.Run(this, ct => ForceBackDoor(p, ct), lockInput: true); };
		AddChild(BackUse);
	}

	private bool _forcing;

	/// <summary>Act 22's end: three shoves at the iced-in door (the camera pushing in and rocking back, gently),
	/// the ice cracking louder each time; the third splits it off the frame, icicles drop from the hood, the
	/// door swings in on the dark; they step into the mudroom (the save), and the screen goes black.</summary>
	private async Task ForceBackDoor(PlayerController player, CancellationToken ct)
	{
		_forcing = true;
		player.Velocity = Vector3.Zero;
		// into place: square on to the door
		var stand = BackStandWorld;
		var walk = player.CreateTween();
		walk.TweenProperty(player, "global_position", stand, Mathf.Clamp(player.GlobalPosition.DistanceTo(stand) / 1.6f, 0.3f, 1.6f)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await StoryBeat.PanTowards(this, player, BackDoorWorld, 0.8f, ct);
		await Cutscene.Tween(this, walk, ct);
		for (int i = 1; i <= 3; i++)
		{
			await Cutscene.Wait(this, i == 1 ? 0.35 : 0.6, ct);
			Shoves = i;
			await Shove(player, i, ct);
		}
		OpenBackDoor();
		await Cutscene.Wait(this, 1.6, ct);
		// in through the gap
		var inside = MudroomWorld;
		var into = player.CreateTween();
		into.TweenProperty(player, "global_position", inside, 2.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await StoryBeat.PanTowards(this, player, inside + (inside - stand).Normalized() * 4f + Vector3.Up * 1.4f, 1.2f, ct);
		await Cutscene.Tween(this, into, ct);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.LodgeBackDoorOpen);
		GD.Print("[story] Act 22 done: the lodge's back door forced; inside");
		// Act 23 begins: behind them, the roof lets go its snow and buries the way out (SkiLodge.Act23.cs)
		await SnowedIn(player, ct);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act22Finished);
	}

	private async Task Shove(PlayerController player, int i, CancellationToken ct)
	{
		// a lunge in and a rock back (small: the owner gets headaches from strong camera motion)
		var rig = player.CameraRig;
		Vector3 home = player.GlobalPosition;
		Vector3 fwd = (BackDoorWorld - home) with { Y = 0 };
		fwd = fwd.Normalized();
		var tw = player.CreateTween();
		tw.TweenProperty(player, "global_position", home + fwd * (0.16f + 0.04f * i), 0.16f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenProperty(player, "global_position", home, 0.45f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		await Cutscene.Wait(this, 0.16, ct);
		AudioDirector.OneShot(this, "body_thump", 2, BackDoorWorld, -4f + i * 2f);
		AudioDirector.OneShot(this, "ice_crack", 3, BackDoorWorld + Vector3.Down * 0.4f, -10f + i * 5f, "Events", 4f, 0.06f);
		Chips(8 + i * 10);
		// the door gives a little each time
		var give = CreateTween();
		give.TweenProperty(_backHinge, "rotation:y", _backHinge.Rotation.Y - 0.05f * i, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		if (i == 2 && _iceSheet != null) _iceSheet.Scale = new Vector3(1f, 0.92f, 1f);   // it's slumping off the frame
		await Cutscene.Tween(this, tw, ct);
	}

	/// <summary>Ice chips bursting off the frame (a one-shot spray that falls to the snow).</summary>
	private void Chips(int n)
	{
		var p = new GpuParticles3D
		{
			Name = "IceChips", Amount = n, Lifetime = 1.4f, OneShot = true, Explosiveness = 0.95f, Emitting = true,
			Position = new Vector3(BackDoorX + 0.3f, 1.0f, -WingHalfZ - 0.6f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.5f, 0.8f, 0.05f),
				Direction = new Vector3(0, 0.3f, -1f), Spread = 50f, InitialVelocityMin = 0.8f, InitialVelocityMax = 2.4f,
				Gravity = new Vector3(0, -9.8f, 0), ScaleMin = 0.5f, ScaleMax = 1.6f,
			},
			DrawPass1 = new BoxMesh { Size = new Vector3(0.03f, 0.02f, 0.015f), Material = WinterWoods.IceMat },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(p);
		GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(p)) p.QueueFree(); };
	}

	public void OpenBackDoor(bool instant = false)
	{
		if (BackOpen) return;
		BackOpen = true;
		if (BackUse != null) BackUse.Enabled = false;
		foreach (var c in _doorBody.GetChildren()) if (c is CollisionShape3D cs) cs.Disabled = true;
		const float open = -1.75f;
		if (instant)
		{
			_backHinge.Rotation = new Vector3(0, open, 0);
			_iceSheet.Visible = false;
			foreach (var ic in _doorIcicles) ic.Visible = false;
			return;
		}
		AudioDirector.OneShot(this, "ice_crack", 3, BackDoorWorld, 4f, "Events", 5f, 0.04f);
		AudioDirector.OneShot(this, "glass_shatter", 1, BackDoorWorld + Vector3.Down * 0.5f, -12f, "Events", 4f, 0.1f);
		AudioDirector.OneShot(this, "door_creak", 1, BackDoorWorld, -2f);
		Chips(60);
		// the sheet splits away and drops
		var drop = CreateTween().SetParallel();
		drop.TweenProperty(_iceSheet, "position:y", -1.6f, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		drop.TweenProperty(_iceSheet, "position:z", -0.25f, 0.45f);
		drop.Chain().TweenCallback(Callable.From(() => _iceSheet.Visible = false));
		// icicles off the hood, one after another
		int k = 0;
		foreach (var ic in _doorIcicles)
		{
			var t = CreateTween();
			t.TweenInterval(0.1f + k++ * 0.07f + _rng.RandfRange(0f, 0.08f));
			t.TweenProperty(ic, "position:y", -0.1f, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			t.TweenCallback(Callable.From(() => { ic.Visible = false; }));
		}
		GetTree().CreateTimer(0.55).Timeout += () => AudioDirector.OneShot(this, "ice_tinkle", 3, BackDoorWorld + Vector3.Down, -6f);
		var sw = CreateTween();
		sw.TweenProperty(_backHinge, "rotation:y", open, 2.2f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		// snow sliding off the hood with it
		AudioDirector.OneShot(this, "snow_whump", 3, BackDoorWorld + Vector3.Up, -8f);
	}

	// ------------------------------------------------------------------ the mudroom (Act 23 starts in here)

	private void BuildMudroom()
	{
		var k = new MeshKit();
		float x0 = RoomX0 + 0.2f, x1 = RoomX1 - 0.2f, z0 = -WingHalfZ + 0.35f, z1 = RoomZ1, top = 2.9f;
		k.Mat(BuildingTextures.FloorMat);
		k.Color = new Color(0.55f, 0.55f, 0.58f);
		k.Quad(new Vector3(x0, 0.02f, z0), new Vector3(x1, 0.02f, z0), new Vector3(x1, 0.02f, z1), new Vector3(x0, 0.02f, z1), Vector3.Up);
		Col(new Vector3((x0 + x1) * 0.5f, -0.2f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.44f, z1 - z0));
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.42f, 0.4f, 0.4f);
		k.Quad(new Vector3(x0, top, z1), new Vector3(x1, top, z1), new Vector3(x1, top, z0), new Vector3(x0, top, z0), Vector3.Down);
		// the back wall's boards: round the back door's opening (a whole board wall hid the doorway from inside), a
		// hair in front of the stone's inner face (level with it, the two fought)
		{
			float zb = z0 + 0.012f, d0 = BackDoorX - BackDoorW * 0.5f - 0.15f, d1 = BackDoorX + BackDoorW * 0.5f + 0.15f;
			k.Quad(new Vector3(x1, 0, zb), new Vector3(d1, 0, zb), new Vector3(d1, top, zb), new Vector3(x1, top, zb), Vector3.Back);
			k.Quad(new Vector3(d0, 0, zb), new Vector3(x0, 0, zb), new Vector3(x0, top, zb), new Vector3(d0, top, zb), Vector3.Back);
			k.Quad(new Vector3(d1, BackDoorH, zb), new Vector3(d0, BackDoorH, zb), new Vector3(d0, top, zb), new Vector3(d1, top, zb), Vector3.Back);
		}
		k.Quad(new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x0, top, z1), new Vector3(x0, top, z0), Vector3.Right);
		k.Quad(new Vector3(x1, 0, z1), new Vector3(x1, 0, z0), new Vector3(x1, top, z0), new Vector3(x1, top, z1), Vector3.Left);
		// (its inner wall, with the door on into the lodge, is the service corridor's: SkiLodge.Interior.cs)
		// a ski rack along one wall with the skis still in it, a bench, boots under it
		k.Mat(LodgeTrim);
		k.Color = Colors.White;
		k.Box(new Vector3(x0 + 0.2f, 1.6f, (z0 + z1) * 0.5f), new Vector3(0.1f, 0.1f, z1 - z0 - 0.6f), 1f);
		k.Box(new Vector3(x0 + 0.2f, 0.3f, (z0 + z1) * 0.5f), new Vector3(0.1f, 0.1f, z1 - z0 - 0.6f), 1f);
		k.Mat(SkiPaint);
		for (int i = 0; i < 7; i++)
		{
			float zz = z0 + 0.6f + i * 0.5f;
			k.Box(new Vector3(x0 + 0.26f, 1.05f, zz), new Vector3(0.03f, 1.8f, 0.08f), 1f, new Basis(Vector3.Forward, 0.12f));
			k.Box(new Vector3(x0 + 0.26f, 1.05f, zz + 0.1f), new Vector3(0.03f, 1.8f, 0.08f), 1f, new Basis(Vector3.Forward, 0.12f));
		}
		k.Mat(LodgeTrim);
		k.Box(new Vector3(x1 - 0.3f, 0.45f, (z0 + z1) * 0.5f), new Vector3(0.45f, 0.08f, 3.2f), 1f);
		k.Box(new Vector3(x1 - 0.3f, 0.22f, (z0 + z1) * 0.5f - 1.4f), new Vector3(0.4f, 0.45f, 0.08f), 1f);
		k.Box(new Vector3(x1 - 0.3f, 0.22f, (z0 + z1) * 0.5f + 1.4f), new Vector3(0.4f, 0.45f, 0.08f), 1f);
		k.CommitTo(this, "Mudroom", true);
		foreach (var (c, s) in new[] { (new Vector3(x0 - 0.1f, 1.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, 3f, z1 - z0)), (new Vector3(x1 + 0.1f, 1.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, 3f, z1 - z0)) })
			Col(c, s);
	}

	// ------------------------------------------------------------------ winter on it

	/// <summary>Icicles all along the eaves (MultiMesh cones: a few hundred, long and short).</summary>
	private void BuildIcicles()
	{
		var list = new List<Transform3D>();
		void Along(Vector3 a, Vector3 b, float every)
		{
			int n = Mathf.Max(1, Mathf.FloorToInt(a.DistanceTo(b) / every));
			for (int i = 0; i <= n; i++)
			{
				if (_rng.Randf() < 0.25f) continue;
				var p = a.Lerp(b, i / (float)n) + new Vector3(_rng.RandfRange(-0.05f, 0.05f), 0, _rng.RandfRange(-0.05f, 0.05f));
				float len = Mathf.Pow(_rng.Randf(), 2f) * 1.4f + 0.2f;
				list.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1f, len, 1f)), p));
			}
		}
		for (int i = 0; i < 6; i++)
		{
			float r = (HexR + 1.3f) / HexR;
			Along(HexVert(i) * r + Vector3.Up * (HexWall - 0.8f), HexVert(i + 1) * r + Vector3.Up * (HexWall - 0.8f), 0.32f);
		}
		foreach (float s in new[] { -1f, 1f })
		{
			// (from outside the hall's walls: from the wing's own start, the first few hung inside the lobby's corners)
			float x0 = s * (WingX0 + 1.6f), ex = s * (WingX1 + 1f);
			Along(new Vector3(x0, WingWall - 0.7f, WingHalfZ + 1f), new Vector3(ex, WingWall - 0.7f, WingHalfZ + 1f), 0.3f);
			Along(new Vector3(x0, WingWall - 0.7f, -WingHalfZ - 1f), new Vector3(ex, WingWall - 0.7f, -WingHalfZ - 1f), 0.3f);
		}
		Along(new Vector3(-4.2f, 4.75f, Apothem + 5.7f), new Vector3(4.2f, 4.75f, Apothem + 5.7f), 0.25f);
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = WinterWoods.IcicleMesh, InstanceCount = list.Count };
		for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
		AddChild(new MultiMeshInstance3D { Name = "Icicles", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
	}

	/// <summary>Snow drifted against the walls, and piled up to the back door's handle.</summary>
	private void BuildDrifts()
	{
		var k = new MeshKit();
		k.Mat(RoofSnow);
		k.Color = Colors.White;
		foreach (float s in new[] { -1f, 1f })
			for (float x = WingX0 + 3f; x < WingX1; x += 4.5f)
			{
				k.Blob(new Vector3(s * x, 0f, WingHalfZ + 0.9f), new Vector3(2.6f, 0.55f, 1.1f), (int)(x * 7 + s * 3 + 100), 0.2f, true, 1f);
				if (s < 0 && Mathf.Abs(-x - BackDoorX) < 3f) continue;
				k.Blob(new Vector3(s * x, 0f, -WingHalfZ - 0.9f), new Vector3(2.6f, 0.75f, 1.2f), (int)(x * 5 + s * 3 + 200), 0.2f, true, 1f);
			}
		// at the back door: shoulder-deep either side, a trough trodden to it (by whom?)
		k.Blob(new Vector3(BackDoorX - 1.6f, 0f, -WingHalfZ - 1f), new Vector3(1.2f, 1.1f, 1.1f), 311, 0.2f, true, 1f);
		k.Blob(new Vector3(BackDoorX + 1.7f, 0f, -WingHalfZ - 1f), new Vector3(1.3f, 0.9f, 1.1f), 312, 0.2f, true, 1f);
		k.CommitTo(this, "Drifts", false);
	}

	// ------------------------------------------------------------------ materials

	private static StandardMaterial3D _timber, _trim, _door, _glass, _lit, _roofSnow, _ski;
	private static StandardMaterial3D SkiPaint => _ski ??= new StandardMaterial3D { ResourceName = "lodge_ski", AlbedoColor = new Color(0.42f, 0.12f, 0.1f), Roughness = 0.5f };
	/// <summary>Dark-stained board-and-batten, the lodge's upper storeys.</summary>
	public static StandardMaterial3D LodgeTimber => _timber ??= Tinted(BuildingTextures.BoardsMat, new Color(0.36f, 0.3f, 0.27f), "lodge_timber");
	public static StandardMaterial3D LodgeTrim => _trim ??= Tinted(BuildingTextures.BoardsMat, new Color(0.26f, 0.22f, 0.2f), "lodge_trim");
	public static StandardMaterial3D LodgeDoor => _door ??= Tinted(ChurchParts.ChurchTextures.OldPlankMat, new Color(0.3f, 0.23f, 0.18f), "lodge_door");
	public static StandardMaterial3D WindowGlass => _glass ??= new StandardMaterial3D
	{
		ResourceName = "lodge_glass", AlbedoColor = new Color(0.04f, 0.05f, 0.07f), Roughness = 0.15f, MetallicSpecular = 0.7f,
	};
	public static StandardMaterial3D LitWindow => _lit ??= new StandardMaterial3D
	{
		ResourceName = "lodge_lit", AlbedoColor = new Color(0.25f, 0.14f, 0.06f), EmissionEnabled = true, Emission = new Color(0.55f, 0.3f, 0.1f), EmissionEnergyMultiplier = 0.55f,
	};
	/// <summary>Snow lying on the roofs (the fresh-snow texture, a touch brighter than the ground's, still never glaring).</summary>
	public static StandardMaterial3D RoofSnow => _roofSnow ??= WinterWoods.SnowStd("lodge_roof_snow", 0.3f, 0.95f);

	private static StandardMaterial3D Tinted(StandardMaterial3D src, Color c, string name)
	{
		var m = (StandardMaterial3D)src.Duplicate();
		m.ResourceName = name;
		m.AlbedoColor = c;
		DetailKit.Apply(m, DetailKit.Kind.Wood);
		return m;
	}
}
