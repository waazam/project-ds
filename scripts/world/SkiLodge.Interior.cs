using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.World.LodgeParts;
using Hole = ProjectDS.World.LodgeParts.LodgeKit.Hole;

namespace ProjectDS.World;

/// <summary>
/// The lodge's interior shell (Act 23): the rooms and how they join.
/// <list type="bullet">
/// <item><b>The lobby</b>: the whole hexagonal hall, open to its great timbered roof. A balcony at 4.2 m runs round
/// its west and back walls, reached by a grand staircase along the back wall. Under the balcony: the front desk by
/// the front doors, the servants' pantry's door. On the east side: the dining hall's double doors. On the west: the
/// bar's archway. A river-stone fireplace on the back-east wall.</item>
/// <item><b>The west wing, ground floor</b>: the bar (front), the service corridor from the mudroom behind it,
/// the narrow servants' pantry along the back wall.</item>
/// <item><b>The west wing, upstairs</b>: off the balcony, a corridor with the four rooms: 202 and 201 on the front
/// side, 203 and 204 on the back. 203's bathroom has a hole broken through into 204.</item>
/// <item><b>The east wing</b>: the dining hall, the full height of the wing.</item>
/// </list>
/// Lodge-local space (the hall's centre at ground level, the front facing +Z).
/// </summary>
public partial class SkiLodge
{
	public const float FloorY = 0.02f, UpperY = 4.2f, SlabY = 3.95f, RoomTop = 7.0f;
	public const float BalconyIn = 7.6f;
	public static float HexIn => Apothem - 0.4f;
	/// <summary>The upstairs corridor and the rooms off it.</summary>
	public const float CorrX0 = -28f, CorrX1 = -12.6f, CorrHalf = 1f, RoomSplitX = -20f, InnerZ = 7.63f;
	/// <summary>The bar, the service corridor, the pantry (ground floor, west wing).</summary>
	public const float BarX0 = -24f, BarZ0 = 0.3f, SvcX0 = -33.3f, SvcX1 = -22f, SvcZ0 = -1.7f, PantryZ1 = -5.2f;
	/// <summary>The dining hall's far wall (inner face).</summary>
	public const float DiningX1 = 43.63f;

	private StaticBody3D _inBody;
	public LodgeDoor MudroomDoor { get; private set; }
	public LodgeDoor PantryDoor { get; private set; }
	public LodgeDoor DiningDoorL { get; private set; }
	public LodgeDoor DiningDoorR { get; private set; }
	public readonly Dictionary<int, LodgeDoor> RoomDoors = new();

	/// <summary>A point on the line a hexagon's side runs along, <paramref name="dist"/> from the centre, at vertex angle deg.</summary>
	private static Vector3 HexAt(float deg, float dist)
	{
		float a = Mathf.DegToRad(deg);
		return new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (dist / 0.8660254f);
	}

	/// <summary>Where the outer face of the hall's side (between vertex angles <paramref name="a0"/> and <paramref name="a1"/>) crosses z.</summary>
	private static float FacetXAt(float a0, float a1, float z)
	{
		Vector3 p = HexVertAt(a0), q = HexVertAt(a1);
		float t = (z - p.Z) / (q.Z - p.Z);
		return Mathf.Lerp(p.X, q.X, t);
	}

	private static Vector3 HexVertAt(float deg) { float a = Mathf.DegToRad(deg); return new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * HexR; }

	/// <summary>Is a lodge-local point inside the building (the hall or either wing), at a height anyone could be?</summary>
	public static bool InsideLocal(Vector3 l)
	{
		if (l.Y < -1f || l.Y > 12f) return false;
		if (Mathf.Abs(l.Z) < WingHalfZ - 0.3f && Mathf.Abs(l.X) > WingX0 && Mathf.Abs(l.X) < WingX1 - 0.3f) return true;
		// the hexagon: within the apothem of every side
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.DegToRad(60f + 60f * i);
			if (l.X * Mathf.Sin(a) + l.Z * Mathf.Cos(a) > Apothem - 0.35f) return false;
		}
		return true;
	}

	// ------------------------------------------------------------------ the hall's walls

	/// <summary>The hexagonal hall's six walls, cut for their doors, archway and windows: stone to the sill course and
	/// timber above outside; inside, stone below and split logs above (the lobby).</summary>
	private void BuildHallWalls(MeshKit k)
	{
		for (int i = 0; i < 6; i++)
		{
			Vector3 a = HexVert(i), b = HexVert(i + 1);
			Vector3 mid = (a + b) * 0.5f, outward = new Vector3(mid.X, 0, mid.Z).Normalized();
			var holes = new List<Hole>();
			var windows = new List<float>();
			switch (i)
			{
				case 0: holes.Add(new Hole(5.6f, 8.0f, 0f, 2.8f)); break;                                   // the dining hall's doors
				case 2: windows.AddRange(new[] { 3f, 6f, 9f }); break;                                        // the back: high windows
				case 3: holes.Add(new Hole(3.3f, 4.4f, 0f, 2.25f)); holes.Add(new Hole(10.8f, 12f, UpperY, 6.7f)); break;   // the pantry; the corridor
				case 4: holes.Add(new Hole(3.0f, 6.0f, 0f, 3.0f)); holes.Add(new Hole(0f, 1.2f, UpperY, 6.7f)); break;       // the bar's arch; the corridor
				case 5: windows.AddRange(new[] { 3.4f, 8.6f }); break;                                        // the front: two high windows over the doors
			}
			foreach (float u in windows) holes.Add(new Hole(u - 0.6f, u + 0.6f, 5.4f, 7.2f));
			k.Color = Colors.White;
			LodgeKit.Wall(k, _body, a, b, 0f, StoneTop, 0.8f, outward, BuildingTextures.StoneMat, BuildingTextures.StoneMat, LodgeTrim, holes, 0.5f);
			LodgeKit.Wall(k, _body, a, b, StoneTop, HexWall, 0.8f, outward, LodgeTimber, LodgeTextures.LogWallMat, LodgeTrim, holes, 0.5f);
			Vector3 along = (b - a).Normalized();
			foreach (float u in windows)
			{
				Vector3 c = a + along * u + Vector3.Up * 6.3f;
				Window(k, c + outward * 0.42f, along, outward, 1.2f, 1.8f);
				InnerWindow(k, c - outward * 0.405f, along, -outward, 1.2f, 1.8f, LodgeTextures.DayGlass);
			}
			// thresholds through the wall's thickness under the ground-floor openings
			foreach (var h in holes)
				if (h.V0 < 0.1f || h.V0 >= UpperY - 0.01f)
				{
					float y = h.V0 < 0.1f ? FloorY : UpperY;
					k.Mat(h.V0 < 0.1f ? BuildingTextures.StoneMat : LodgeTextures.CorridorCarpetMat);
					Vector3 p0 = a + along * h.U0, p1 = a + along * h.U1;
					k.Quad(p0 - outward * 0.41f + Vector3.Up * y, p1 - outward * 0.41f + Vector3.Up * y, p1 + outward * 0.41f + Vector3.Up * y, p0 + outward * 0.41f + Vector3.Up * y, Vector3.Up,
						new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0.4f), new Vector2(0, 0.4f));
				}
		}
		// the corridor's way through the west corner (both sides meet there): a floor across the corner
		k.Mat(LodgeTextures.CorridorCarpetMat);
		k.Quad(new Vector3(-12.9f, UpperY, CorrHalf + 0.04f), new Vector3(-11.54f, UpperY, CorrHalf + 0.04f), new Vector3(-11.54f, UpperY, -CorrHalf - 0.04f), new Vector3(-12.9f, UpperY, -CorrHalf - 0.04f), Vector3.Up,
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		LodgeKit.Solid(_body, new Vector3(-12.2f, UpperY - 0.12f, 0), new Vector3(1.6f, 0.24f, 2.2f));
	}

	/// <summary>A window's inner face: the daylight glass, a painted frame and a sill.</summary>
	private void InnerWindow(MeshKit k, Vector3 c, Vector3 along, Vector3 inward, float w, float h, Material glass)
	{
		k.Mat(glass);
		k.Color = Colors.White;
		k.Quad(c - along * w * 0.5f - Vector3.Up * h * 0.5f, c + along * w * 0.5f - Vector3.Up * h * 0.5f, c + along * w * 0.5f + Vector3.Up * h * 0.5f, c - along * w * 0.5f + Vector3.Up * h * 0.5f, inward);
		k.Mat(LodgeTextures.DarkWoodMat);
		var basis = new Basis(along, Vector3.Up, inward);
		Vector3 o = inward * 0.03f;
		k.Box(c + o + Vector3.Up * h * 0.5f, new Vector3(w + 0.14f, 0.08f, 0.06f), 1f, basis);
		k.Box(c + o * 1.5f - Vector3.Up * (h * 0.5f + 0.03f), new Vector3(w + 0.24f, 0.06f, 0.16f), 1f, basis);
		k.Box(c + o - along * w * 0.5f, new Vector3(0.07f, h, 0.06f), 1f, basis);
		k.Box(c + o + along * w * 0.5f, new Vector3(0.07f, h, 0.06f), 1f, basis);
		k.Box(c + o, new Vector3(0.035f, h, 0.035f), 1f, basis);
		k.Box(c + o, new Vector3(w, 0.035f, 0.035f), 1f, basis);
	}

	/// <summary>The x positions of the wing walls' windows (as the outside builds them: evenly along each wall).</summary>
	private static List<float> WindowXs(float x0, float x1, float every = 3.2f)
	{
		var l = new List<float>();
		float len = Mathf.Abs(x1 - x0);
		int n = Mathf.FloorToInt(len / every);
		for (int i = 0; i < n; i++) l.Add(Mathf.Lerp(x0, x1, (i + 0.5f) / n));
		return l;
	}

	/// <summary>Every window of the wings along one long wall (front z &gt; 0 or back), as (x, lower?) pairs.</summary>
	private static List<float> WingWindows(float side, bool west)
	{
		if (side > 0) return west ? WindowXs(-WingX1, -WingX0) : WindowXs(WingX0, WingX1);
		if (!west) return WindowXs(WingX0, WingX1);
		float d0 = BackDoorX - BackDoorW * 0.5f - 0.15f, d1 = BackDoorX + BackDoorW * 0.5f + 0.15f;
		var l = WindowXs(-WingX1, d0);
		l.AddRange(WindowXs(d1, -WingX0));
		return l;
	}

	/// <summary>A lining just inside an outer wing wall (the room's own finish), cut for that wall's windows, and
	/// the windows' inner glass in the cuts. From x0 to x1 along z = <paramref name="zFace"/>, from y0 to y1.</summary>
	private void Lining(MeshKit k, float x0, float x1, float zFace, float y0, float y1, Material lower, Material upper, float split, bool west, System.Func<float, float, Material> glassFor = null)
	{
		float side = Mathf.Sign(zFace);
		Vector3 a = new(x0, 0, zFace), b = new(x1, 0, zFace);
		Vector3 inward = new(0, 0, -side);
		var holes = new List<Hole>();
		var wins = new List<(float x, float y, float w, float h)>();
		foreach (float wx in WingWindows(side, west))
		{
			if (wx < Mathf.Min(x0, x1) + 0.6f || wx > Mathf.Max(x0, x1) - 0.6f) continue;
			foreach (var (wy, ww, wh) in new[] { (1.7f, 0.9f, 1.1f), (StoneTop + (WingWall - StoneTop) * 0.52f, 1.1f, 1.7f) })
			{
				if (wy - wh * 0.5f < y0 || wy + wh * 0.5f > y1) continue;
				float u = Mathf.Abs(wx - x0);
				holes.Add(new Hole(u - ww * 0.5f, u + ww * 0.5f, wy - wh * 0.5f, wy + wh * 0.5f));
				wins.Add((wx, wy, ww, wh));
			}
		}
		k.Color = Colors.White;
		if (split > y0 && split < y1)
		{
			LodgeKit.Wall(k, null, a, b, y0, split, 0.04f, inward, lower, lower, LodgeTextures.DarkWoodMat, holes);
			LodgeKit.Wall(k, null, a, b, split, y1, 0.04f, inward, upper, upper, LodgeTextures.DarkWoodMat, holes);
		}
		else LodgeKit.Wall(k, null, a, b, y0, y1, 0.04f, inward, upper, upper, LodgeTextures.DarkWoodMat, holes);
		foreach (var (wx, wy, ww, wh) in wins)
			InnerWindow(k, new Vector3(wx, wy, zFace + side * 0.015f), Vector3.Right, inward, ww, wh, glassFor?.Invoke(wx, wy) ?? LodgeTextures.DayGlass);
	}

	// ------------------------------------------------------------------ the interior

	private void BuildInterior()
	{
		_inBody = new StaticBody3D { Name = "InteriorBody", CollisionLayer = 1, CollisionMask = 0 };
		_inBody.SetMeta("surface", "wood");
		AddChild(_inBody);
		BuildLobbyShell();
		BuildWestGround();
		BuildWestUpper();
		BuildDiningShell();
	}

	private void BuildLobbyShell()
	{
		var k = new MeshKit();
		// the floor: flagstone, a hexagon just under the walls
		k.Mat(LodgeTextures.FlagstoneMat);
		k.Color = Colors.White;
		for (int i = 0; i < 6; i++)
		{
			Vector3 p = HexVert(i) * (HexIn + 0.05f) / Apothem, q = HexVert(i + 1) * (HexIn + 0.05f) / Apothem;
			k.Tri(new Vector3(0, FloorY, 0), p + Vector3.Up * FloorY, q + Vector3.Up * FloorY, Vector3.Up, Vector2.Zero, new Vector2(p.X, p.Z) * 0.25f, new Vector2(q.X, q.Z) * 0.25f);
		}
		// the ceiling: the roof's inside, boarded, climbing to the lantern; log rafters on the corners
		float top = 21f, topR = 1.6f;
		for (int i = 0; i < 6; i++)
		{
			Vector3 p = HexVert(i) * (HexIn / Apothem), q = HexVert(i + 1) * (HexIn / Apothem);
			Vector3 pt = HexVert(i) * (topR / HexR), qt = HexVert(i + 1) * (topR / HexR);
			Vector3 a = p + Vector3.Up * HexWall, b = q + Vector3.Up * HexWall, c = qt + Vector3.Up * top, d = pt + Vector3.Up * top;
			var n = (b - a).Cross(d - a).Normalized();
			if (n.Y > 0) n = -n;
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Quad(a, b, c, d, n, new Vector2(0, 0), new Vector2(a.DistanceTo(b) * 0.5f, 0), new Vector2(a.DistanceTo(b) * 0.5f, 6f), new Vector2(0, 6f));
			k.Mat(BuildingTextures.LogMat);
			k.Cylinder(p * 0.98f + Vector3.Up * (HexWall - 0.3f), pt + Vector3.Up * (top - 0.1f), 0.22f, 0.16f, 8, false, 0.5f);
		}
		// a ring beam round the top of the walls, and a lantern's light well at the peak
		for (int i = 0; i < 6; i++)
		{
			Vector3 p = HexVert(i) * ((HexIn - 0.2f) / Apothem), q = HexVert(i + 1) * ((HexIn - 0.2f) / Apothem);
			k.Mat(BuildingTextures.LogMat);
			k.Cylinder(p + Vector3.Up * (HexWall - 0.25f), q + Vector3.Up * (HexWall - 0.25f), 0.2f, 0.2f, 8, false, 0.5f);
		}
		k.Mat(LodgeTextures.DayGlass);
		k.Cylinder(Vector3.Up * top, Vector3.Up * (top + 0.02f), topR, topR, 6, true);
		k.CommitTo(this, "LobbyShell", true);
		LodgeKit.Solid(_inBody, new Vector3(0, FloorY - 0.2f, 0), new Vector3(HexIn * 2f, 0.4f, HexIn * 2f));
		BuildBalcony();
		BuildStairs();
	}

	/// <summary>The balcony round the west and back walls at 4.2 m: a boarded walk with a log fascia, an iron railing
	/// with a log handrail, log posts down to the floor; a landing at the stair's head.</summary>
	private void BuildBalcony()
	{
		var k = new MeshKit();
		var faces = new List<Vector3>();
		void Floor(Vector3 a, Vector3 b, Vector3 c)
		{
			a.Y = b.Y = c.Y = UpperY;
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Color = Colors.White;
			k.Tri(a, b, c, Vector3.Up, new Vector2(a.X, a.Z) * 0.5f, new Vector2(b.X, b.Z) * 0.5f, new Vector2(c.X, c.Z) * 0.5f);
			k.Mat(LodgeTextures.CeilingMat);
			var d = Vector3.Down * 0.28f;
			k.Tri(a + d, c + d, b + d, Vector3.Down, Vector2.Zero, Vector2.Right, Vector2.Up);
			faces.Add(a); faces.Add(b); faces.Add(c);
		}
		// the two sides' walks (west-back 240 degrees, west-front 300 degrees)
		foreach (var (v0, v1) in new[] { (210f, 270f), (270f, 330f) })
		{
			Vector3 o0 = HexAt(v0, HexIn), o1 = HexAt(v1, HexIn), i0 = HexAt(v0, BalconyIn), i1 = HexAt(v1, BalconyIn);
			Floor(o0, o1, i1); Floor(o0, i1, i0);
		}
		// the stair's landing along the back wall
		Vector3 l0 = new(-3.4f, 0, -HexIn), l1 = HexAt(210f, HexIn), l2 = new(-4.71f, 0, -8.15f), l3 = new(-3.4f, 0, -8.15f);
		Floor(l0, l1, l2); Floor(l0, l2, l3);
		// the fascia: logs along the open edges
		var edge = new List<Vector3> { new(-3.4f, 0, -8.15f), new(-4.71f, 0, -8.15f), HexAt(210f, BalconyIn), HexAt(270f, BalconyIn), HexAt(330f, BalconyIn), HexAt(330f, HexIn) };
		for (int i = 0; i < edge.Count - 1; i++)
		{
			Vector3 a = edge[i] + Vector3.Up * (UpperY - 0.15f), b = edge[i + 1] + Vector3.Up * (UpperY - 0.15f);
			k.Mat(BuildingTextures.LogMat);
			k.Cylinder(a, b, 0.16f, 0.16f, 8, true, 0.5f);
			Railing(k, edge[i] + Vector3.Up * UpperY, edge[i + 1] + Vector3.Up * UpperY);
			// posts down to the floor at the corners
			if (i >= 2 && i <= 4) Post(k, edge[i]);
		}
		k.CommitTo(this, "Balcony", true);
		var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
		shape.SetFaces(faces.ToArray());
		_inBody.AddChild(new CollisionShape3D { Name = "BalconyFloor", Shape = shape });
		// a thin slab under it too (so nothing falls through a seam)
		foreach (var (v0, v1) in new[] { (210f, 270f), (270f, 330f) })
		{
			Vector3 m = (HexAt(v0, (HexIn + BalconyIn) * 0.5f) + HexAt(v1, (HexIn + BalconyIn) * 0.5f)) * 0.5f;
			Vector3 along = (HexAt(v1, HexIn) - HexAt(v0, HexIn)).Normalized();
			float len = HexAt(v0, (HexIn + BalconyIn) * 0.5f).DistanceTo(HexAt(v1, (HexIn + BalconyIn) * 0.5f));
			_inBody.AddChild(new CollisionShape3D { Position = m + Vector3.Up * (UpperY - 0.14f), Basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up).Normalized()), Shape = new BoxShape3D { Size = new Vector3(len, 0.24f, HexIn - BalconyIn) } });
		}
	}

	/// <summary>An iron railing between two points (on the walk's surface): balusters, a bottom rail, a log handrail; collision.</summary>
	private void Railing(MeshKit k, Vector3 a, Vector3 b)
	{
		float len = a.DistanceTo(b);
		Vector3 along = (b - a).Normalized();
		int n = Mathf.Max(2, Mathf.FloorToInt(len / 0.14f));
		k.Mat(LodgeTextures.IronMat);
		k.Color = Colors.White;
		for (int i = 1; i < n; i++)
		{
			Vector3 p = a.Lerp(b, i / (float)n);
			k.Box(p + Vector3.Up * 0.5f, new Vector3(0.022f, 0.92f, 0.022f), 1f);
		}
		k.Cylinder(a + Vector3.Up * 0.08f, b + Vector3.Up * 0.08f, 0.018f, 0.018f, 4, false);
		k.Mat(BuildingTextures.LogMat);
		k.Cylinder(a + Vector3.Up * 1.0f, b + Vector3.Up * 1.0f, 0.07f, 0.07f, 7, true, 0.5f);
		var mid = (a + b) * 0.5f;
		float yaw = Mathf.Atan2(-along.Z, along.X);
		var d = b - a;
		float pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
		_inBody.AddChild(new CollisionShape3D
		{
			Position = mid + Vector3.Up * 0.55f, Basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Back, pitch),
			Shape = new BoxShape3D { Size = new Vector3(len, 1.1f, 0.12f) },
		});
	}

	private void Post(MeshKit k, Vector3 at)
	{
		k.Mat(BuildingTextures.LogMat);
		k.Color = Colors.White;
		k.Cylinder(at + Vector3.Up * FloorY, at + Vector3.Up * (UpperY + 1.05f), 0.2f, 0.18f, 10, true, 0.5f);
		LodgeKit.Solid(_inBody, at + Vector3.Up * (UpperY * 0.5f), new Vector3(0.36f, UpperY, 0.36f));
	}

	/// <summary>The grand staircase along the back wall: 24 broad treads rising west to the balcony, a carpet
	/// runner up the middle, a log stringer, the railing on the open side (collision: a smooth ramp).</summary>
	private void BuildStairs()
	{
		var k = new MeshKit();
		const int n = 24;
		float x0 = 5f, x1 = -3.4f, z0 = -HexIn + 0.02f, z1 = -8.15f;
		float run = (x0 - x1) / n, rise = UpperY / n;
		for (int s = 0; s < n; s++)
		{
			float xa = x0 - s * run, xb = x0 - (s + 1) * run, y = (s + 1) * rise;
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Color = Colors.White;
			k.Box(new Vector3((xa + xb) * 0.5f, y - rise * 0.5f, (z0 + z1) * 0.5f), new Vector3(xa - xb, rise, z1 - z0), 1f);
			k.Mat(LodgeTextures.CorridorCarpetMat);
			k.Quad(new Vector3(xa, y + 0.004f, z1 - 0.35f), new Vector3(xb, y + 0.004f, z1 - 0.35f), new Vector3(xb, y + 0.004f, z0 + 0.35f), new Vector3(xa, y + 0.004f, z0 + 0.35f), Vector3.Up,
				new Vector2(0, 0), new Vector2(0.4f, 0), new Vector2(0.4f, 1), new Vector2(0, 1));
		}
		// the underside: a sloped soffit, boarded, and the log stringer on the open side
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Quad(new Vector3(x0, 0.02f, z1), new Vector3(x1, UpperY - 0.3f, z1), new Vector3(x1, UpperY - 0.3f, z0), new Vector3(x0, 0.02f, z0), new Vector3(-rise, -run, 0).Normalized());
		k.Mat(BuildingTextures.LogMat);
		k.Cylinder(new Vector3(x0 + 0.2f, 0.1f, z1 + 0.05f), new Vector3(x1, UpperY - 0.1f, z1 + 0.05f), 0.12f, 0.12f, 8, true, 0.5f);
		Railing(k, new Vector3(x0, rise, z1 + 0.05f), new Vector3(x1, UpperY, z1 + 0.05f));
		Post(k, new Vector3(x0 + 0.1f, 0, z1 + 0.1f));
		k.CommitTo(this, "Stairs", true);
		// the ramp the feet use
		float len = new Vector2(x0 - x1, UpperY).Length();
		float ang = Mathf.Atan2(UpperY, x0 - x1);
		_inBody.AddChild(new CollisionShape3D
		{
			Name = "StairRamp", Position = new Vector3((x0 + x1) * 0.5f, UpperY * 0.5f - 0.1f, (z0 + z1) * 0.5f), Basis = new Basis(Vector3.Forward, ang),
			Shape = new BoxShape3D { Size = new Vector3(len, 0.2f, z1 - z0) },
		});
	}

	// ------------------------------------------------------------------ the west wing, ground floor

	private void BuildWestGround()
	{
		var k = new MeshKit();
		float barX1Front = FacetXAt(270f, 330f, InnerZ + 0.4f), barX1Back = FacetXAt(270f, 330f, BarZ0);
		// ---- the bar: floor (dark boards with a red runner), ceiling, walls (dark wainscot, red damask above)
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		k.Quad(new Vector3(BarX0, FloorY, InnerZ), new Vector3(barX1Front, FloorY, InnerZ), new Vector3(barX1Back, FloorY, BarZ0), new Vector3(BarX0, FloorY, BarZ0), Vector3.Up,
			new Vector2(BarX0, InnerZ) * 0.5f, new Vector2(barX1Front, InnerZ) * 0.5f, new Vector2(barX1Back, BarZ0) * 0.5f, new Vector2(BarX0, BarZ0) * 0.5f);
		k.Mat(LodgeTextures.CeilingMat);
		k.Quad(new Vector3(BarX0, SlabY, BarZ0), new Vector3(barX1Back, SlabY, BarZ0), new Vector3(barX1Front, SlabY, InnerZ), new Vector3(BarX0, SlabY, InnerZ), Vector3.Down);
		Lining(k, BarX0, barX1Front, InnerZ, FloorY, SlabY, LodgeTextures.DarkWoodMat, LodgeTextures.DamaskMat, 1.1f, true);
		// the bar's back wall (west) and its south wall to the service corridor (a door near its west end)
		Split(k, new Vector3(BarX0, 0, BarZ0), new Vector3(BarX0, 0, InnerZ), Vector3.Right, LodgeTextures.DamaskMat, LodgeTextures.PlasterMat, null);
		Split(k, new Vector3(SvcX0, 0, BarZ0), new Vector3(barX1Back + 0.3f, 0, BarZ0), Vector3.Back, LodgeTextures.DamaskMat, LodgeTextures.PlasterMat,
			new List<Hole> { new(SvcX1 - 1.2f - SvcX0, SvcX1 - 0.2f - SvcX0, 0f, 2.2f) });
		// ---- the service corridor: from the mudroom's inner door to the bar
		k.Mat(LodgeTextures.CheckerMat);
		k.Quad(new Vector3(SvcX0, FloorY, BarZ0), new Vector3(SvcX1, FloorY, BarZ0), new Vector3(SvcX1, FloorY, SvcZ0), new Vector3(SvcX0, FloorY, SvcZ0), Vector3.Up,
			new Vector2(SvcX0, BarZ0) * 0.5f, new Vector2(SvcX1, BarZ0) * 0.5f, new Vector2(SvcX1, SvcZ0) * 0.5f, new Vector2(SvcX0, SvcZ0) * 0.5f);
		k.Mat(LodgeTextures.CeilingMat);
		k.Quad(new Vector3(SvcX0, 3.0f, SvcZ0), new Vector3(SvcX1, 3.0f, SvcZ0), new Vector3(SvcX1, 3.0f, BarZ0), new Vector3(SvcX0, 3.0f, BarZ0), Vector3.Down);
		LodgeKit.Solid(_inBody, new Vector3((SvcX0 + SvcX1) * 0.5f, 3.1f, (SvcZ0 + BarZ0) * 0.5f), new Vector3(SvcX1 - SvcX0, 0.2f, BarZ0 - SvcZ0));
		Split(k, new Vector3(SvcX0, 0, SvcZ0), new Vector3(SvcX0, 0, BarZ0), Vector3.Right, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat, null, 3.0f);
		Split(k, new Vector3(SvcX1, 0, SvcZ0), new Vector3(SvcX1, 0, BarZ0), Vector3.Left, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat, null, 3.0f);
		// its south wall: the mudroom's inner door in it, and on east to the hall's corner (the kitchen's wall behind)
		Split(k, new Vector3(SvcX0, 0, SvcZ0), new Vector3(FacetXAt(210f, 270f, SvcZ0) + 0.3f, 0, SvcZ0), Vector3.Back, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat,
			new List<Hole> { new(BackDoorX - 0.5f - SvcX0, BackDoorX + 0.5f - SvcX0, 0f, 2.15f) });
		// ---- the pantry: a long narrow room along the back wall
		float pX1Back = FacetXAt(210f, 270f, -InnerZ - 0.4f), pX1Front = FacetXAt(210f, 270f, PantryZ1);
		k.Mat(LodgeTextures.CheckerMat);
		k.Quad(new Vector3(BarX0, FloorY, PantryZ1), new Vector3(pX1Front, FloorY, PantryZ1), new Vector3(pX1Back, FloorY, -InnerZ), new Vector3(BarX0, FloorY, -InnerZ), Vector3.Up,
			new Vector2(BarX0, PantryZ1) * 0.5f, new Vector2(pX1Front, PantryZ1) * 0.5f, new Vector2(pX1Back, -InnerZ) * 0.5f, new Vector2(BarX0, -InnerZ) * 0.5f);
		k.Mat(LodgeTextures.CeilingMat);
		k.Quad(new Vector3(BarX0, 3.0f, -InnerZ), new Vector3(pX1Back, 3.0f, -InnerZ), new Vector3(pX1Front, 3.0f, PantryZ1), new Vector3(BarX0, 3.0f, PantryZ1), Vector3.Down);
		LodgeKit.Solid(_inBody, new Vector3((BarX0 + pX1Front) * 0.5f, 3.1f, (PantryZ1 - InnerZ) * 0.5f), new Vector3(pX1Front - BarX0 + 1f, 0.2f, InnerZ + PantryZ1));
		Lining(k, BarX0, pX1Back, -InnerZ, FloorY, 3.0f, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat, 0f, true);
		Split(k, new Vector3(BarX0, 0, PantryZ1), new Vector3(pX1Front + 0.3f, 0, PantryZ1), Vector3.Forward, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat, null, 3.0f);
		Split(k, new Vector3(BarX0, 0, -InnerZ), new Vector3(BarX0, 0, PantryZ1), Vector3.Right, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat, null, 3.0f);
		// the upper floor's slab over all of it (collision; its faces are the rooms' floors and ceilings)
		LodgeKit.Solid(_inBody, new Vector3((-WingX1 + 0.35f + CorrX1) * 0.5f, (SlabY + UpperY) * 0.5f, 0), new Vector3(CorrX1 - (-WingX1 + 0.35f), UpperY - SlabY, InnerZ * 2f));
		k.CommitTo(this, "WestGround", true);
		// the doors: the mudroom's inner door (unlocked), the pantry's (the pantry key)
		MudroomDoor = new LodgeDoor { Name = "MudroomDoor", Position = new Vector3(BackDoorX - 0.5f, 0, SvcZ0), Width = 1.0f, Height = 2.15f, OpenAngle = 1.5f, FrontSign = -1f, OpenPrompt = "Open the door" };
		AddChild(MudroomDoor);
		MudroomDoor.Unlock();
		Vector3 pa = HexVert(3), pb = HexVert(4), palong = (pb - pa).Normalized(), pout = ((pa + pb) * 0.5f).Normalized();
		PantryDoor = new LodgeDoor
		{
			Name = "PantryDoor", Position = pa + palong * 3.3f, Width = 1.1f, Height = 2.25f, Needs = ToolKind.PantryKey, OpenAngle = -1.5f, FrontSign = -1f,
			LockedPrompt = "\"STAFF ONLY - PANTRY\". Locked.",
		};
		PantryDoor.Basis = new Basis(palong, Vector3.Up, pout);
		AddChild(PantryDoor);
		SignKit.Text(this, "PANTRY", pa + palong * 3.85f - pout * 0.42f + Vector3.Up * 2.45f, new Basis(palong, Vector3.Up, pout) * new Basis(Vector3.Up, Mathf.Pi), 0.08f, new Color(0.62f, 0.5f, 0.3f), shadow: false);
	}

	/// <summary>A partition wall on the ground floor (to <paramref name="top"/>, default the slab), with a finish each side.</summary>
	private void Split(MeshKit k, Vector3 a, Vector3 b, Vector3 sideA, Material matA, Material matB, List<Hole> holes, float top = SlabY)
		=> LodgeKit.Wall(k, _inBody, a, b, FloorY, top, 0.15f, sideA, matA, matB, LodgeTextures.DarkWoodMat, holes);

	// ------------------------------------------------------------------ the west wing, upstairs

	private void BuildWestUpper()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		// the corridor: the carpet, a plaster ceiling, wallpaper over a dark wainscot
		k.Mat(LodgeTextures.CorridorCarpetMat);
		k.Quad(new Vector3(CorrX0, UpperY, CorrHalf), new Vector3(CorrX1 - 0.3f, UpperY, CorrHalf), new Vector3(CorrX1 - 0.3f, UpperY, -CorrHalf), new Vector3(CorrX0, UpperY, -CorrHalf), Vector3.Up,
			new Vector2(0, 0), new Vector2((CorrX1 - 0.3f - CorrX0) / 1.5f, 0), new Vector2((CorrX1 - 0.3f - CorrX0) / 1.5f, 2f / 1.5f), new Vector2(0, 2f / 1.5f));
		k.Mat(LodgeTextures.CeilingMat);
		k.Quad(new Vector3(CorrX0, RoomTop, -CorrHalf), new Vector3(CorrX1 - 0.3f, RoomTop, -CorrHalf), new Vector3(CorrX1 - 0.3f, RoomTop, CorrHalf), new Vector3(CorrX0, RoomTop, CorrHalf), Vector3.Down);
		LodgeKit.Solid(_inBody, new Vector3((CorrX0 + CorrX1) * 0.5f, RoomTop + 0.1f, 0), new Vector3(CorrX1 - CorrX0, 0.2f, InnerZ * 2f));
		// its two walls, the rooms' doors in them (hinges at the doors' west ends on the north wall, east on the south)
		var north = new List<Hole> { new(-14.2f - CorrX0, -13.2f - CorrX0, UpperY, UpperY + 2.2f), new(-21.8f - CorrX0, -20.8f - CorrX0, UpperY, UpperY + 2.2f) };
		var south = new List<Hole> { new(-14.2f - CorrX0, -13.2f - CorrX0, UpperY, UpperY + 2.2f), new(-21.8f - CorrX0, -20.8f - CorrX0, UpperY, UpperY + 2.2f) };
		UpWall(k, new Vector3(CorrX0, 0, CorrHalf), new Vector3(CorrX1, 0, CorrHalf), Vector3.Forward, true, north);
		UpWall(k, new Vector3(CorrX0, 0, -CorrHalf), new Vector3(CorrX1, 0, -CorrHalf), Vector3.Back, true, south);
		UpWall(k, new Vector3(CorrX0, 0, -CorrHalf), new Vector3(CorrX0, 0, CorrHalf), Vector3.Right, true, null);
		// the rooms: 202 (front, east), 201 (front, west), 203 (back, east), 204 (back, west)
		foreach (var (num, x0, x1, front) in new[] { (202, RoomSplitX, CorrX1, true), (201, CorrX0, RoomSplitX, true), (203, RoomSplitX, CorrX1, false), (204, CorrX0, RoomSplitX, false) })
			RoomShell(k, num, x0, x1, front);
		k.CommitTo(this, "WestUpper", true);
		// the doors
		foreach (var (num, x, front) in new[] { (202, -14.2f, true), (201, -21.8f, true), (203, -13.2f, false), (204, -20.8f, false) })
		{
			var d = new LodgeDoor
			{
				Name = $"Door{num}", Number = num.ToString(), HasReader = true, Width = 1.0f, Height = 2.2f, FrontSign = -1f, OpenAngle = -1.45f,
				Position = new Vector3(x, UpperY, front ? CorrHalf : -CorrHalf), Rotation = new Vector3(0, front ? 0f : Mathf.Pi, 0),
				Needs = num switch { 202 => ToolKind.Keycard202, 203 => ToolKind.Keycard203, 201 => ToolKind.Keycard201, _ => ToolKind.None },
				ConsumeKey = true, LockedPrompt = num == 204 ? "Room 204. Locked; the reader's light is red." : $"Room {num}. The reader wants a keycard.",
			};
			AddChild(d);
			RoomDoors[num] = d;
		}
	}

	/// <summary>An upstairs partition (floor 4.2 to the ceiling 7.0): wallpaper over a dark wainscot, or green tile in a bathroom.</summary>
	private void UpWall(MeshKit k, Vector3 a, Vector3 b, Vector3 sideA, bool paperA, List<Hole> holes, bool tileA = false, bool tileB = false)
	{
		var wA = tileA ? LodgeTextures.GreenTileMat : LodgeTextures.WallpaperMat;
		var wB = tileB ? LodgeTextures.GreenTileMat : LodgeTextures.WallpaperMat;
		var lA = tileA ? LodgeTextures.GreenTileMat : LodgeTextures.DarkWoodMat;
		var lB = tileB ? LodgeTextures.GreenTileMat : LodgeTextures.DarkWoodMat;
		LodgeKit.Wall(k, _inBody, a, b, UpperY, UpperY + 1.0f, 0.14f, sideA, lA, lB, LodgeTextures.DarkWoodMat, holes);
		LodgeKit.Wall(k, _inBody, a, b, UpperY + 1.0f, RoomTop, 0.14f, sideA, wA, wB, LodgeTextures.DarkWoodMat, holes);
	}

	/// <summary>A hotel room's shell: the carpet, the ceiling, the outer wall's lining with its two windows, the walls
	/// between rooms, and a bathroom tiled green (its door; 203's broken through into 204).</summary>
	private void RoomShell(MeshKit k, int num, float x0, float x1, bool front)
	{
		float zIn = front ? CorrHalf : -CorrHalf, zOut = front ? InnerZ : -InnerZ, s = front ? 1f : -1f;
		k.Mat(LodgeTextures.RoomCarpetMat);
		k.Color = Colors.White;
		float za = Mathf.Min(zIn, zOut), zb = Mathf.Max(zIn, zOut);
		k.Quad(new Vector3(x0, UpperY, zb), new Vector3(x1, UpperY, zb), new Vector3(x1, UpperY, za), new Vector3(x0, UpperY, za), Vector3.Up,
			new Vector2(x0, zb) / 1.6f, new Vector2(x1, zb) / 1.6f, new Vector2(x1, za) / 1.6f, new Vector2(x0, za) / 1.6f);
		k.Mat(LodgeTextures.CeilingMat);
		k.Quad(new Vector3(x0, RoomTop, za), new Vector3(x1, RoomTop, za), new Vector3(x1, RoomTop, zb), new Vector3(x0, RoomTop, zb), Vector3.Down);
		// the outer wall: its lining and windows (202's banked with snow, blue; 203's frosted, one left open)
		System.Func<float, float, Material> glass = num switch
		{
			202 => (_, _) => LodgeTextures.SnowBlueGlass,
			203 => (_, _) => LodgeTextures.IceGlass,
			_ => null,
		};
		Lining(k, x0, x1, zOut, UpperY, RoomTop, LodgeTextures.DarkWoodMat, LodgeTextures.WallpaperMat, UpperY + 1.0f, true, glass);
		// the side walls: east of 202/203 against the hall's corner, west of 201/204 at the corridor's end, and the split between
		if (x1 >= CorrX1 - 0.01f) UpWall(k, new Vector3(x1, 0, zIn), new Vector3(x1, 0, zOut), Vector3.Left, true, null);
		if (x0 <= CorrX0 + 0.01f) UpWall(k, new Vector3(x0, 0, zIn), new Vector3(x0, 0, zOut), Vector3.Right, true, null);
		// the bathroom: in the room's west corner by the corridor, 2.7 x 3.1, green tile
		float bx0 = x0, bx1 = x0 + 2.7f, bz = zIn + s * 3.1f;
		if (num == 202 || num == 203)
		{
			// the split wall between the pairs (202|201, 203|204) is built by the east room; 203's has the hole, in its bathroom
			var holes = num == 203 ? new List<Hole> { new(0.55f, 2.35f, UpperY, UpperY + 2.1f) } : null;   // the hole: within the bathroom's stretch
			// along z from the corridor outward; the hole is within the bathroom's stretch (0..3.1 from the corridor)
			UpWall(k, new Vector3(RoomSplitX, 0, zIn), new Vector3(RoomSplitX, 0, zOut), Vector3.Right, true, holes, tileA: false, tileB: false);
		}
		k.Mat(LodgeTextures.GreenTileMat);
		k.Quad(new Vector3(bx0 + 0.07f, UpperY + 0.003f, Mathf.Max(zIn, bz)), new Vector3(bx1, UpperY + 0.003f, Mathf.Max(zIn, bz)), new Vector3(bx1, UpperY + 0.003f, Mathf.Min(zIn, bz)), new Vector3(bx0 + 0.07f, UpperY + 0.003f, Mathf.Min(zIn, bz)), Vector3.Up,
			new Vector2(0, 0), new Vector2(2.7f / 1.2f, 0), new Vector2(2.7f / 1.2f, 3.1f / 1.2f), new Vector2(0, 3.1f / 1.2f));
		// its two walls: along the room (x = bx1, a door in it) and across (z = bz)
		UpWall(k, new Vector3(bx1, 0, zIn), new Vector3(bx1, 0, bz), Vector3.Left, true, new List<Hole> { new(1.2f, 2.1f, UpperY, UpperY + 2.1f) }, tileA: true);
		UpWall(k, new Vector3(bx0, 0, bz), new Vector3(bx1, 0, bz), new Vector3(0, 0, -s), true, null, tileA: true);
	}

	// ------------------------------------------------------------------ the east wing: the dining hall

	private void BuildDiningShell()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		float fx = FacetXAt(30f, 90f, InnerZ + 0.4f);   // where the hall's east sides meet the wing's walls
		// the floor: herringbone parquet, from the hall's corner to the far wall
		k.Mat(LodgeTextures.ParquetMat);
		// (a fan from the hall's corner: the wing's floor is concave there, where the hall's two sides point into it)
		var pts = new[] { new Vector3(HexR, FloorY, 0), new Vector3(fx, FloorY, -InnerZ), new Vector3(DiningX1, FloorY, -InnerZ), new Vector3(DiningX1, FloorY, InnerZ), new Vector3(fx, FloorY, InnerZ) };
		for (int i = 1; i < pts.Length - 1; i++)
			k.Tri(pts[0], pts[i], pts[i + 1], Vector3.Up, new Vector2(pts[0].X, pts[0].Z) * 0.2f, new Vector2(pts[i].X, pts[i].Z) * 0.2f, new Vector2(pts[i + 1].X, pts[i + 1].Z) * 0.2f);
		LodgeKit.Solid(_inBody, new Vector3((fx + DiningX1) * 0.5f + 1f, FloorY - 0.2f, 0), new Vector3(DiningX1 - fx + 2f, 0.4f, InnerZ * 2f));
		// the ceiling, coffered with dark beams
		k.Mat(LodgeTextures.CeilingMat);
		for (int i = 1; i < pts.Length - 1; i++)
			k.Tri(pts[0] with { Y = RoomTop }, pts[i + 1] with { Y = RoomTop }, pts[i] with { Y = RoomTop }, Vector3.Down, Vector2.Zero, Vector2.Right, Vector2.Up);
		k.Mat(LodgeTextures.DarkWoodMat);
		for (float x = 12f; x < DiningX1; x += 4f) k.Box(new Vector3(x, RoomTop - 0.14f, 0), new Vector3(0.28f, 0.28f, InnerZ * 2f), 1f);
		k.Box(new Vector3((12f + DiningX1) * 0.5f, RoomTop - 0.14f, 0), new Vector3(DiningX1 - 12f, 0.2f, 0.24f), 1f);
		// the walls: wainscot to 1.3 m, damask-free cream paper above (it's a dining room: pale, grand); both window rows
		Lining(k, fx, DiningX1, InnerZ, FloorY, RoomTop, LodgeTextures.DarkWoodMat, LodgeTextures.WallpaperMat, 1.3f, false);
		Lining(k, fx, DiningX1, -InnerZ, FloorY, RoomTop, LodgeTextures.DarkWoodMat, LodgeTextures.WallpaperMat, 1.3f, false);
		// the far end: a lining across it with its windows (one left open: the frost comes in there)
		var endHoles = new List<Hole>();
		foreach (float wz in new[] { -6f, -2f, 2f, 6f })
		{
			endHoles.Add(new Hole(InnerZ - wz - 0.45f, InnerZ - wz + 0.45f, 1.15f, 2.25f));
			endHoles.Add(new Hole(InnerZ - wz - 0.55f, InnerZ - wz + 0.55f, 4.59f, 6.29f));
		}
		LodgeKit.Wall(k, null, new Vector3(DiningX1, 0, InnerZ), new Vector3(DiningX1, 0, -InnerZ), FloorY, 1.3f, 0.04f, Vector3.Left, LodgeTextures.DarkWoodMat, LodgeTextures.DarkWoodMat, LodgeTextures.DarkWoodMat, endHoles);
		LodgeKit.Wall(k, null, new Vector3(DiningX1, 0, InnerZ), new Vector3(DiningX1, 0, -InnerZ), 1.3f, RoomTop, 0.04f, Vector3.Left, LodgeTextures.WallpaperMat, LodgeTextures.WallpaperMat, LodgeTextures.DarkWoodMat, endHoles);
		foreach (float wz in new[] { -6f, -2f, 2f, 6f })
		{
			InnerWindow(k, new Vector3(DiningX1 + 0.015f, 1.7f, wz), Vector3.Back, Vector3.Left, 0.9f, 1.1f, LodgeTextures.IceGlass);
			InnerWindow(k, new Vector3(DiningX1 + 0.015f, 5.44f, wz), Vector3.Back, Vector3.Left, 1.1f, 1.7f, LodgeTextures.IceGlass);
		}
		k.CommitTo(this, "DiningShell", true);
		// the double doors from the lobby (the dining hall's key)
		Vector3 da = HexVert(0), db = HexVert(1), dalong = (db - da).Normalized(), dout = ((da + db) * 0.5f).Normalized();
		var basis = new Basis(dalong, Vector3.Up, dout);
		DiningDoorL = new LodgeDoor { Name = "DiningDoorL", Width = 1.2f, Height = 2.8f, Needs = ToolKind.DiningKey, OpenAngle = -1.4f, FrontSign = -1f, LockedPrompt = "\"DINING ROOM\". Locked." };
		DiningDoorL.Position = da + dalong * 5.6f;
		DiningDoorL.Basis = basis;
		AddChild(DiningDoorL);
		DiningDoorR = new LodgeDoor { Name = "DiningDoorR", Width = 1.2f, Height = 2.8f, Needs = ToolKind.DiningKey, OpenAngle = 1.4f, FrontSign = 1f, LockedPrompt = "\"DINING ROOM\". Locked." };
		DiningDoorR.Position = da + dalong * 8.0f;
		DiningDoorR.Basis = basis * new Basis(Vector3.Up, Mathf.Pi);
		AddChild(DiningDoorR);
		// either leaf's key opens both
		DiningDoorL.Opened += p => DiningDoorR.Open(p);
		DiningDoorR.Opened += p => DiningDoorL.Open(p);
		SignKit.Text(this, "DINING ROOM", da + dalong * 6.8f - dout * 0.42f + Vector3.Up * 3.1f, basis * new Basis(Vector3.Up, Mathf.Pi), 0.12f, new Color(0.7f, 0.55f, 0.3f), shadow: false);
	}
}
