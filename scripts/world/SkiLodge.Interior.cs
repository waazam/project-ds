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
	/// <summary>Where the corridor's walls end on the hall's side: inside the corner wall's thickness, at the doorway's cut.</summary>
	private const float DoorwayInX = -11.15f;
	/// <summary>The bar, the service corridor, the pantry (ground floor, west wing).</summary>
	public const float BarX0 = -24f, BarZ0 = 0.3f, SvcX0 = -33.3f, SvcX1 = -22f, SvcZ0 = -1.7f, PantryZ1 = -5.2f;
	/// <summary>The dining hall's far wall (inner face).</summary>
	public const float DiningX1 = 43.63f;

	private StaticBody3D _inBody;
	/// <summary>The hall's openings (doors, arches, windows), as rectangles on its walls: centre, along, width, height.</summary>
	private readonly List<(Vector3 c, Vector3 along, float w, float h)> _hallOpenings = new();

	/// <summary>Is a thing <paramref name="half"/> wide either side and <paramref name="halfH"/> high either side, at
	/// <paramref name="at"/> on one of the hall's walls, clear of its doors and windows (by a hand's width)?</summary>
	private bool ClearOfHallOpenings(Vector3 at, float half, float halfH)
	{
		foreach (var (c, along, w, h) in _hallOpenings)
		{
			var d = at - c;
			var n = along.Cross(Vector3.Up);
			if (Mathf.Abs(d.Dot(n)) > 1.2f) continue;   // (not on this wall)
			if (Mathf.Abs(d.Dot(along)) < w * 0.5f + half + 0.2f && Mathf.Abs(d.Y) < h * 0.5f + halfH + 0.15f) return false;
		}
		return true;
	}
	public LodgeDoor MudroomDoor { get; private set; }
	/// <summary>(2026-10-14d) The back-of-house doors (the mudroom's, the pantry's): old boards under a chipped oxblood paint, not
	/// the guest rooms' dark wood (the mudroom's door read as its boarded walls).</summary>
	private static Material BackDoorPaint => BuildingTextures.PaintedBoards("lodge_backdoor", new Color(0.3f, 0.11f, 0.08f), 0.92f, 0.12f, footHeight: 0.4f) ?? (Material)LodgeTextures.DarkWoodMat;
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
		if (l.Y < -40f && l.Y > -80f) return Mathf.Abs(l.Z - CrawlOZ) < 12f && l.X > CrawlOX - 2f && l.X < CrawlOX + 70f;   // the crawlspace's maze (SkiLodge.Crawlspace.cs: CrawlDown below), in its footprint
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
		k.Tess = 0.6f;   // (2026-10-14h: for the kit pass's bake, SkiLodge.Kit; set back after)
		for (int i = 0; i < 6; i++)
		{
			Vector3 a = HexVert(i), b = HexVert(i + 1);
			Vector3 mid = (a + b) * 0.5f, outward = new Vector3(mid.X, 0, mid.Z).Normalized();
			var holes = new List<Hole>();
			// the windows: (u along the side, bottom, top, width, columns, rows)
			// (2026-10-10c, the owner: "we need to make it feel more open, we need large windows around the room that let more
			// light in"; the reference, a modern lodge's hall: tall gridded windows the height of the room, its stone fire
			// between them) The front: a tall window either side of the doors and a great transom over them (east of the
			// doors, the reception's pigeonholes stand against the wall, so its window starts above them). The back: a tall
			// one east of the stair's landing, two high over the stair's upper flight.
			var windows = new List<(float u, float y0, float y1, float w, int cols, int rows)>();
			switch (i)
			{
				case 0: holes.Add(new Hole(5.6f, 8.0f, 0f, 2.8f)); break;                                   // the dining hall's doors
				case 2: windows.AddRange(new[] { (3.0f, 0.75f, 7.9f, 2.2f, 3, 6), (6.2f, 5.2f, 7.9f, 1.5f, 2, 3), (9.1f, 5.2f, 7.9f, 1.5f, 2, 3) }); break;   // the back
				case 3: holes.Add(new Hole(3.3f, 4.4f, 0f, 2.25f)); holes.Add(new Hole(10.8f, 12f, UpperY, 6.7f)); break;   // the pantry; the corridor
				case 4: holes.Add(new Hole(3.0f, 6.0f, 0f, 3.0f)); holes.Add(new Hole(0f, 1.2f, UpperY, 6.7f)); break;       // the bar's arch; the corridor
				case 5:   // the front: the doorway (inside its casing), the tall windows, the transom
					holes.Add(new Hole(4.75f, 7.25f, 0f, 3.05f));
					windows.AddRange(new[] { (2.5f, 0.75f, 7.9f, 2.2f, 3, 6), (9.5f, 3.35f, 7.9f, 2.2f, 3, 4), (6.0f, 3.75f, 7.9f, 2.3f, 3, 3) });
					break;
			}
			foreach (var (u, y0, y1, w, _, _) in windows) holes.Add(new Hole(u - w * 0.5f, u + w * 0.5f, y0, y1));
			// (every opening in the hall's walls, kept: the sconces and the hangings keep clear of them)
			foreach (var h in holes)
				_hallOpenings.Add((a + (b - a).Normalized() * ((h.U0 + h.U1) * 0.5f) + Vector3.Up * ((h.V0 + h.V1) * 0.5f), (b - a).Normalized(), h.U1 - h.U0, h.V1 - h.V0));
			k.Color = Colors.White;
			LodgeKit.Wall(k, _body, a, b, 0f, StoneTop, 0.8f, outward, BuildingTextures.StoneMat, LodgeTextures.LobbyStoneMat, LodgeTrim, holes, 0.5f, floorY: 0f, grime: -1);
			LodgeKit.Wall(k, _body, a, b, StoneTop, HexWall, 0.8f, outward, LodgeTimber, LodgeTextures.LogWallMat, LodgeTrim, holes, 0.5f);
			Vector3 along = (b - a).Normalized();
			foreach (var (u, y0, y1, w, cols, rows) in windows)
			{
				Vector3 c = a + along * u + Vector3.Up * ((y0 + y1) * 0.5f);
				Window(k, c + outward * 0.42f, along, outward, w, y1 - y0, cols, rows);
				InnerWindow(k, c - outward * 0.405f, along, -outward, w, y1 - y0, LodgeTextures.DayGlass, cols, rows);
				// the reveal: the wall's thickness round it lined in the honey boards
				k.Mat(LodgeTextures.LobbyStoneMat);
				// (a centimetre inside the opening: on the wall's own cut faces exactly, the two fought; the clip audit)
				float hh = (y1 - y0) * 0.5f - 0.012f, hw = w * 0.5f - 0.012f;
				foreach (float sd in new[] { -1f, 1f })
					k.Quad(c + along * (sd * hw) - Vector3.Up * hh - outward * 0.4f, c + along * (sd * hw) - Vector3.Up * hh + outward * 0.4f,
						c + along * (sd * hw) + Vector3.Up * hh + outward * 0.4f, c + along * (sd * hw) + Vector3.Up * hh - outward * 0.4f, -along * sd);
				k.Quad(c - along * hw + Vector3.Up * hh - outward * 0.4f, c - along * hw + Vector3.Up * hh + outward * 0.4f,
					c + along * hw + Vector3.Up * hh + outward * 0.4f, c + along * hw + Vector3.Up * hh - outward * 0.4f, Vector3.Down);
				k.Quad(c - along * hw - Vector3.Up * hh + outward * 0.4f, c - along * hw - Vector3.Up * hh - outward * 0.4f,
					c + along * hw - Vector3.Up * hh - outward * 0.4f, c + along * hw - Vector3.Up * hh + outward * 0.4f, Vector3.Up);
				// light falling in from it: cool, soft, down onto the floor in front of it
				if (y0 < 4f)
				{
					var wl = Light(c - outward * 1.6f + Vector3.Up * 0.4f, Day, 0.6f, 9f, "WindowLight");
					wl.SetMeta("base", 0.6f);
					_dayLights.Add(wl);
				}
			}
			// thresholds through the wall's thickness under the ground-floor openings
			foreach (var h in holes)
				if (h.V0 < 0.1f || h.V0 >= UpperY - 0.01f)
				{
					float y = (h.V0 < 0.1f ? FloorY : UpperY) + 0.003f;   // a hair proud (level, they fought the floors running under the walls)
					k.Mat(h.V0 < 0.1f ? BuildingTextures.StoneMat : LodgeTextures.CorridorCarpetMat);
					Vector3 p0 = a + along * h.U0, p1 = a + along * h.U1;
					k.Quad(p0 - outward * 0.41f + Vector3.Up * y, p1 - outward * 0.41f + Vector3.Up * y, p1 + outward * 0.41f + Vector3.Up * y, p0 + outward * 0.41f + Vector3.Up * y, Vector3.Up,
						new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0.4f), new Vector2(0, 0.4f));
				}
		}
		// the corridor's way through the west corner (both sides meet there): a floor across the corner
		// (dark boards, and the corridor's runner carried on across it to the doorway, its pattern running on)
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Quad(new Vector3(-12.9f, UpperY, CorrHalf + 0.04f), new Vector3(-11.54f, UpperY, CorrHalf + 0.04f), new Vector3(-11.54f, UpperY, -CorrHalf - 0.04f), new Vector3(-12.9f, UpperY, -CorrHalf - 0.04f), Vector3.Up,
			new Vector2(0, 0), new Vector2(1.36f, 0), new Vector2(1.36f, 2.08f), new Vector2(0, 2.08f));
		k.Mat(LodgeTextures.CorridorCarpetMat);
		float u0 = (-12.9f - CorrX0) * 1.14f, u1 = (-11.54f - CorrX0) * 1.14f;
		k.Quad(new Vector3(-12.9f, UpperY + 0.004f, 0.72f), new Vector3(-11.54f, UpperY + 0.004f, 0.72f), new Vector3(-11.54f, UpperY + 0.004f, 0), new Vector3(-12.9f, UpperY + 0.004f, 0), Vector3.Up,
			new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 0.5f), new Vector2(u0, 0.5f));
		k.Quad(new Vector3(-12.9f, UpperY + 0.004f, 0), new Vector3(-11.54f, UpperY + 0.004f, 0), new Vector3(-11.54f, UpperY + 0.004f, -0.72f), new Vector3(-12.9f, UpperY + 0.004f, -0.72f), Vector3.Up,
			new Vector2(u0, 0.5f), new Vector2(u1, 0.5f), new Vector2(u1, 0), new Vector2(u0, 0));
		LodgeKit.Solid(_body, new Vector3(-12.2f, UpperY - 0.12f, 0), new Vector3(1.6f, 0.24f, 2.2f));
		k.Tess = 0f;
	}

	/// <summary>A window's inner face: the daylight glass in a grid of panes, a painted frame and a sill. Each pane is its
	/// own quad on the pane texture (tools/textures/make_window_pane.py): the bottom row's with the snow drifted up the
	/// glass outside, the rest clear but for the frost in their corners (2026-10-10c, the owner: "the windows should be
	/// snowed in a little").</summary>
	private void InnerWindow(MeshKit k, Vector3 c, Vector3 along, Vector3 inward, float w, float h, Material glass, int cols = 2, int rows = 2)
	{
		_winSpots.Add((c, along, inward, w, h));   // (Act 23's freeze breaks the lobby's and the dining hall's)
		k.Mat(glass);
		k.Color = Colors.White;
		Vector3 o0 = c - along * w * 0.5f - Vector3.Up * h * 0.5f;
		float pw = w / cols, ph = h / rows;
		for (int j = 0; j < rows; j++)
			for (int i = 0; i < cols; i++)
			{
				Vector3 a = o0 + along * (pw * i) + Vector3.Up * (ph * j), b = a + along * pw, cc = b + Vector3.Up * ph, d = a + Vector3.Up * ph;
				float vt = j == 0 ? 0.5f : 0f, vb = vt + 0.5f;
				k.Quad(a, b, cc, d, inward, new Vector2(0, vb), new Vector2(1, vb), new Vector2(1, vt), new Vector2(0, vt));
			}
		k.Mat(LodgeTextures.DarkWoodMat);
		var basis = new Basis(along, Vector3.Up, inward);
		Vector3 o = inward * 0.03f;
		k.Box(c + o + Vector3.Up * h * 0.5f, new Vector3(w + 0.14f, 0.08f, 0.06f), 1f, basis);
		k.Box(c + o * 1.5f - Vector3.Up * (h * 0.5f + 0.03f), new Vector3(w + 0.24f, 0.06f, 0.16f), 1f, basis);
		k.Box(c + o - along * w * 0.5f, new Vector3(0.07f, h, 0.06f), 1f, basis);
		k.Box(c + o + along * w * 0.5f, new Vector3(0.07f, h, 0.06f), 1f, basis);
		for (int i = 1; i < cols; i++) k.Box(c + o + along * (pw * i - w * 0.5f), new Vector3(0.035f, h, 0.035f), 1f, basis);
		for (int j = 1; j < rows; j++) k.Box(c + o + Vector3.Up * (ph * j - h * 0.5f), new Vector3(w, 0.035f, 0.035f), 1f, basis);
		// a heavier transom bar a third of the way down a tall window (the big lodge windows have one)
		if (rows >= 4) k.Box(c + o * 1.2f + Vector3.Up * (ph * (rows - 1) - h * 0.5f), new Vector3(w + 0.04f, 0.09f, 0.07f), 1f, basis);
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
			LodgeKit.Wall(k, null, a, b, y0, split, 0.04f, inward, lower, lower, LodgeTextures.DarkWoodMat, holes, floorY: y0, grime: 1, skirt: true);
			LodgeKit.Wall(k, null, a, b, split, y1, 0.04f, inward, upper, upper, LodgeTextures.DarkWoodMat, holes, grime: 1, ceilingY: y1);
			ChairRail(k, a, b, inward, 0.02f, holes, split - 0.03f);   // (2026-10-14h: the wainscot capped)
		}
		else LodgeKit.Wall(k, null, a, b, y0, y1, 0.04f, inward, upper, upper, LodgeTextures.DarkWoodMat, holes, floorY: y0, grime: 1, skirt: true, ceilingY: y1);
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
		var k = new MeshKit { Tess = 0.8f };   // (the kit pass's bake: SkiLodge.Kit)
		// the floor: wide warm planks (it was grey flagstone), a hexagon just under the walls (in a fine fan of
		// triangles, for the bake: the wear from the doors, the dark round the walls and under the furniture)
		k.Mat(LodgeTextures.LobbyFloorMat);
		k.Color = Colors.White;
		for (int i = 0; i < 6; i++)
		{
			Vector3 p = HexVert(i) * (HexIn + 0.05f) / Apothem, q = HexVert(i + 1) * (HexIn + 0.05f) / Apothem;
			TriGrid(k, new Vector3(0, FloorY, 0), p + Vector3.Up * FloorY, q + Vector3.Up * FloorY, Vector3.Up, 0.25f, 16);
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
			k.Mat(BuildingTextures.RoundLogMat);
			k.Cylinder(p * 0.98f + Vector3.Up * (HexWall - 0.3f), pt + Vector3.Up * (top - 0.1f), 0.22f, 0.16f, 8, false, 0.5f);
		}
		// a ring beam round the top of the walls, and a lantern's light well at the peak
		for (int i = 0; i < 6; i++)
		{
			Vector3 p = HexVert(i) * ((HexIn - 0.2f) / Apothem), q = HexVert(i + 1) * ((HexIn - 0.2f) / Apothem);
			k.Mat(BuildingTextures.RoundLogMat);
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
			CeilTri(k, a + d, c + d, b + d);
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
			k.Mat(BuildingTextures.RoundLogMat);
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
		k.Mat(BuildingTextures.RoundLogMat);
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
		k.Mat(BuildingTextures.RoundLogMat);
		k.Color = Colors.White;
		k.Cylinder(at + Vector3.Up * FloorY, at + Vector3.Up * (UpperY + 1.05f), 0.2f, 0.18f, 10, true, 0.5f);
		LodgeKit.Solid(_inBody, at + Vector3.Up * (UpperY * 0.5f), new Vector3(0.36f, UpperY, 0.36f));
	}

	/// <summary>The staircase (2026-10-10c, the owner: "Make the stairs have some character and have a bend around the wall
	/// element to their design so they arent a straight line going up"): a dog-leg round a pier of warm stone. The lower
	/// flight climbs from the lobby straight toward the back wall (14 treads) to a landing beside the tall back window;
	/// there it turns west and the upper flight climbs along the back wall (10 treads) to the balcony. The turn wraps a
	/// stone pier with a lamp on its cap, and under the upper flight a knee wall of the same stone closes the space
	/// beneath it. Honey treads with their nosings proud, the corridor's runner up the middle (both halves mirrored, as
	/// before), log handrails on iron balusters, great log newels at the foot. Collision: two smooth ramps and the
	/// landing's slab.</summary>
	public const float StairXL = -0.6f, StairW = 1.84f, StairZTop = -8.15f, StairLandY = 2.45f;
	public const float StairRunA = 0.32f;
	public static float StairZBot => StairZTop + 14 * StairRunA;
	/// <summary>Lodge-local: the foot of the lower flight, and the landing (for the tests' walks).</summary>
	public static Vector3 StairFoot => new(StairXL + StairW * 0.5f, FloorY, StairZBot + 0.7f);
	public static Vector3 StairLanding => new(StairXL + StairW * 0.5f, StairLandY, (StairZTop - HexIn) * 0.5f);

	private void BuildStairs()
	{
		var k = new MeshKit();
		float xL = StairXL, xR = StairXL + StairW, zTop = StairZTop, zWall = -HexIn + 0.02f, xEnd = -3.4f;
		const int nA = 14, nB = 10;
		float runA = StairRunA, rise = UpperY / (nA + nB), yL = nA * rise;
		float zBot = zTop + nA * runA;
		float runB = (xL - xEnd) / nB;
		var tread = LodgeTextures.LobbyStoneMat;   // (the honey boards)
		float xm = (xL + xR) * 0.5f;
		// ---- the lower flight, toward the back wall
		for (int st = 0; st < nA; st++)
		{
			float za = zBot - st * runA, zb = za - runA, y = (st + 1) * rise;
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Color = Colors.White;
			k.Box(new Vector3(xm, y - rise * 0.5f, (za + zb) * 0.5f), new Vector3(xR - xL, rise, runA), 1f);
			k.Mat(tread);
			k.Box(new Vector3(xm, y - 0.015f, (za + zb) * 0.5f + 0.02f), new Vector3(xR - xL + 0.04f, 0.035f, runA + 0.04f), 1f);   // the tread, its nosing proud
			k.Mat(LodgeTextures.CorridorCarpetMat);
			// (the runner: u along the flight, the two halves mirrored so its black lines run down both outsides)
			k.Quad(new Vector3(xL + 0.32f, y + 0.006f, za), new Vector3(xm, y + 0.006f, za), new Vector3(xm, y + 0.006f, zb), new Vector3(xL + 0.32f, y + 0.006f, zb), Vector3.Up,
				new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(0.37f, 0.5f), new Vector2(0.37f, 0));
			k.Quad(new Vector3(xm, y + 0.006f, za), new Vector3(xR - 0.32f, y + 0.006f, za), new Vector3(xR - 0.32f, y + 0.006f, zb), new Vector3(xm, y + 0.006f, zb), Vector3.Up,
				new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(0.37f, 0), new Vector2(0.37f, 0.5f));
		}
		// its sides: a closed stringer in the honey boards down to the floor (the space under it is shut in), a log along each
		foreach (float x in new[] { xL - 0.05f, xR + 0.05f })
		{
			var n = x < xL ? Vector3.Left : Vector3.Right;
			k.Mat(tread);
			if (n.X < 0)
				k.Quad(new Vector3(x, 0.02f, zTop), new Vector3(x, 0.02f, zBot), new Vector3(x, rise, zBot), new Vector3(x, yL, zTop), n,
					new Vector2(0, 0), new Vector2(zBot - zTop, 0), new Vector2(zBot - zTop, rise), new Vector2(0, yL));
			else
				k.Quad(new Vector3(x, 0.02f, zBot), new Vector3(x, 0.02f, zTop), new Vector3(x, yL, zTop), new Vector3(x, rise, zBot), n,
					new Vector2(0, 0), new Vector2(zBot - zTop, 0), new Vector2(zBot - zTop, yL), new Vector2(0, rise));
			k.Mat(BuildingTextures.RoundLogMat);
			k.Cylinder(new Vector3(x, rise - 0.05f, zBot + 0.1f), new Vector3(x, yL - 0.05f, zTop), 0.07f, 0.07f, 7, true, 0.5f);
		}
		// under the landing, its east face down to the floor
		k.Mat(tread);
		k.Quad(new Vector3(xR + 0.05f, 0.02f, zTop), new Vector3(xR + 0.05f, 0.02f, zWall), new Vector3(xR + 0.05f, yL, zWall), new Vector3(xR + 0.05f, yL, zTop), Vector3.Right,
			new Vector2(0, 0), new Vector2(zTop - zWall, 0), new Vector2(zTop - zWall, yL), new Vector2(0, yL));
		// ---- the landing, a rug on it
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Box(new Vector3(xm, yL - 0.12f, (zTop + zWall) * 0.5f), new Vector3(xR - xL, 0.24f, zTop - zWall), 1f);
		k.Mat(tread);
		k.Box(new Vector3(xm, yL - 0.015f, (zTop + zWall) * 0.5f), new Vector3(xR - xL + 0.04f, 0.03f, zTop - zWall + 0.04f), 1f);
		k.Mat(LodgeTextures.RugMat);
		k.Quad(new Vector3(xL + 0.2f, yL + 0.008f, zTop - 0.2f), new Vector3(xR - 0.2f, yL + 0.008f, zTop - 0.2f), new Vector3(xR - 0.2f, yL + 0.008f, zWall + 0.2f), new Vector3(xL + 0.2f, yL + 0.008f, zWall + 0.2f), Vector3.Up,
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		// ---- the upper flight, along the back wall to the balcony
		for (int st = 0; st < nB; st++)
		{
			float xa = xL - st * runB, xb = xa - runB, y = yL + (st + 1) * rise;
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Color = Colors.White;
			k.Box(new Vector3((xa + xb) * 0.5f, y - rise * 0.5f, (zWall + zTop) * 0.5f), new Vector3(xa - xb, rise, zTop - zWall), 1f);
			k.Mat(tread);
			k.Box(new Vector3((xa + xb) * 0.5f - 0.02f, y - 0.015f, (zWall + zTop) * 0.5f), new Vector3(runB + 0.04f, 0.035f, zTop - zWall + 0.04f), 1f);
			k.Mat(LodgeTextures.CorridorCarpetMat);
			float zm = (zTop - 0.32f + zWall + 0.32f) * 0.5f;
			k.Quad(new Vector3(xa, y + 0.006f, zTop - 0.32f), new Vector3(xb, y + 0.006f, zTop - 0.32f), new Vector3(xb, y + 0.006f, zm), new Vector3(xa, y + 0.006f, zm), Vector3.Up,
				new Vector2(0, 0), new Vector2(0.32f, 0), new Vector2(0.32f, 0.5f), new Vector2(0, 0.5f));
			k.Quad(new Vector3(xa, y + 0.006f, zm), new Vector3(xb, y + 0.006f, zm), new Vector3(xb, y + 0.006f, zWall + 0.32f), new Vector3(xa, y + 0.006f, zWall + 0.32f), Vector3.Up,
				new Vector2(0, 0.5f), new Vector2(0.32f, 0.5f), new Vector2(0.32f, 0), new Vector2(0, 0));
		}
		// ---- the stone: the knee wall under the upper flight (its top following the flight, a log along it), the pier at the turn
		var stone = LodgeTextures.RiverStoneMat;
		float zk = zTop + 0.1f;
		k.Mat(stone);
		k.Quad(new Vector3(xL - 0.68f, 0.02f, zk), new Vector3(xEnd, 0.02f, zk), new Vector3(xEnd, UpperY - 0.32f, zk), new Vector3(xL - 0.68f, yL + 0.5f * rise / runB * 0.68f - 0.3f, zk), Vector3.Back,
			new Vector2(0, 0), new Vector2(xL - 0.68f - xEnd, 0), new Vector2(xL - 0.68f - xEnd, UpperY - 0.32f), new Vector2(0, yL));
		k.Mat(BuildingTextures.RoundLogMat);
		k.Cylinder(new Vector3(xL - 0.68f, yL + 0.68f * rise / runB - 0.3f, zk), new Vector3(xEnd, UpperY - 0.3f, zk), 0.09f, 0.09f, 7, true, 0.5f);
		k.Mat(stone);
		Vector3 pier = new(xL - 0.34f, 0, zTop + 0.34f);
		float pierH = UpperY + 1.1f;
		k.Box(pier + Vector3.Up * (pierH * 0.5f), new Vector3(0.68f, pierH, 0.68f), 1f);
		k.Box(pier + Vector3.Up * (pierH + 0.06f), new Vector3(0.8f, 0.12f, 0.8f), 1f);   // its cap
		k.Box(pier + Vector3.Up * 0.09f, new Vector3(0.8f, 0.18f, 0.8f), 1f);            // its plinth
		LodgeKit.Solid(_inBody, pier + Vector3.Up * (pierH * 0.5f), new Vector3(0.7f, pierH, 0.7f));
		LodgeKit.Solid(_inBody, new Vector3((xL - 0.68f + xEnd) * 0.5f, (UpperY - 0.4f) * 0.5f, zk), new Vector3(xL - 0.68f - xEnd, UpperY - 0.4f, 0.2f));
		// a lamp on the pier's cap, lit (it marks the turn, warm, from all over the hall)
		var lampAt = LodgeKit.Lamp(k, pier + Vector3.Up * (pierH + 0.12f), 0.42f, LodgeTextures.Glow("lodge_pier_shade", new Color(0.95f, 0.72f, 0.45f), 0.9f));
		Light(lampAt, new Color(1f, 0.74f, 0.46f), 0.55f, 5.5f, "Light");
		// ---- the rails
		Railing(k, new Vector3(xR + 0.05f, rise, zBot), new Vector3(xR + 0.05f, yL, zTop));                 // the lower flight's open east side
		Railing(k, new Vector3(xR + 0.05f, yL, zTop), new Vector3(xR + 0.05f, yL, zWall + 0.05f));          // round the landing's east edge
		float yPier = (zBot - (zTop + 0.68f)) / runA * rise;
		Railing(k, new Vector3(xL - 0.05f, rise, zBot), new Vector3(xL - 0.05f, yPier, zTop + 0.68f));       // the west side, to the pier
		Railing(k, new Vector3(xL - 0.68f, yL + 0.68f * rise / runB, zTop + 0.04f), new Vector3(xEnd, UpperY, zTop + 0.04f)); // the upper flight's open side
		// the newels at the foot: great logs, their tops rounded; one at the landing's corner
		k.Mat(BuildingTextures.RoundLogMat);
		foreach (float x in new[] { xL - 0.05f, xR + 0.05f })
		{
			k.Cylinder(new Vector3(x, FloorY, zBot + 0.15f), new Vector3(x, 1.35f, zBot + 0.15f), 0.13f, 0.12f, 10, true, 0.5f);
			k.Blob(new Vector3(x, 1.42f, zBot + 0.15f), Vector3.One * 0.13f, 4300 + (int)(x * 10), 0.05f, false, 0.5f);
			LodgeKit.Solid(_inBody, new Vector3(x, 0.7f, zBot + 0.15f), new Vector3(0.26f, 1.4f, 0.26f));
		}
		k.Cylinder(new Vector3(xR + 0.05f, FloorY, zTop), new Vector3(xR + 0.05f, yL + 1.1f, zTop), 0.11f, 0.1f, 10, true, 0.5f);
		k.CommitTo(this, "Stairs", true);
		// ---- what the feet use: the two flights' ramps and the landing; the space under them shut in
		float lenA = new Vector2(zBot - zTop, yL).Length(), angA = Mathf.Atan2(yL, zBot - zTop);
		_inBody.AddChild(new CollisionShape3D
		{
			Name = "StairRampA", Position = new Vector3(xm, yL * 0.5f - 0.1f, (zBot + zTop) * 0.5f), Basis = new Basis(Vector3.Right, angA),
			Shape = new BoxShape3D { Size = new Vector3(xR - xL, 0.2f, lenA) },
		});
		_inBody.AddChild(new CollisionShape3D { Name = "StairLanding", Position = new Vector3(xm, yL - 0.1f, (zTop + zWall) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(xR - xL, 0.2f, zTop - zWall) } });
		float lenB = new Vector2(xL - xEnd, UpperY - yL).Length(), angB = Mathf.Atan2(UpperY - yL, xL - xEnd);
		_inBody.AddChild(new CollisionShape3D
		{
			Name = "StairRamp", Position = new Vector3((xL + xEnd) * 0.5f, (yL + UpperY) * 0.5f - 0.1f, (zTop + zWall) * 0.5f), Basis = new Basis(Vector3.Forward, angB),
			Shape = new BoxShape3D { Size = new Vector3(lenB, 0.2f, zTop - zWall) },
		});
		LodgeKit.Solid(_inBody, new Vector3(xm, yL * 0.3f, (zTop + zWall) * 0.5f), new Vector3(xR - xL, yL * 0.6f, zTop - zWall));
	}

	// ------------------------------------------------------------------ the west wing, ground floor

	private void BuildWestGround()
	{
		var k = new MeshKit { Tess = 0.6f };   // (the kit pass's bake: SkiLodge.Kit)
		float barX1Front = FacetXAt(270f, 330f, InnerZ + 0.4f), barX1Back = FacetXAt(270f, 330f, BarZ0);
		// ---- the bar: floor (dark boards with a red runner), ceiling, walls (dark wainscot, red damask above)
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		k.Quad(new Vector3(BarX0, FloorY, InnerZ), new Vector3(barX1Front, FloorY, InnerZ), new Vector3(barX1Back, FloorY, BarZ0), new Vector3(BarX0, FloorY, BarZ0), Vector3.Up,
			new Vector2(BarX0, InnerZ) * 0.5f, new Vector2(barX1Front, InnerZ) * 0.5f, new Vector2(barX1Back, BarZ0) * 0.5f, new Vector2(BarX0, BarZ0) * 0.5f);
		k.Mat(LodgeTextures.CeilingMat);
		CeilQuad(k, new Vector3(BarX0, SlabY, BarZ0), new Vector3(barX1Back, SlabY, BarZ0), new Vector3(barX1Front, SlabY, InnerZ), new Vector3(BarX0, SlabY, InnerZ));
		Lining(k, BarX0, barX1Front, InnerZ, FloorY, SlabY, LodgeTextures.DarkWoodMat, LodgeTextures.KnottyPineMat, 1.1f, true);   // (2026-10-14h: knotty pine above the wainscot; it was a red damask)
		// the bar's back wall (west) and its south wall to the service corridor (a door near its west end)
		Split(k, new Vector3(BarX0, 0, BarZ0), new Vector3(BarX0, 0, InnerZ), Vector3.Right, LodgeTextures.KnottyPineMat, LodgeTextures.PlasterMat, null);
		Split(k, new Vector3(SvcX0, 0, BarZ0), new Vector3(barX1Back + 0.3f, 0, BarZ0), Vector3.Back, LodgeTextures.KnottyPineMat, LodgeTextures.PlasterMat,
			new List<Hole> { new(SvcX1 - 1.2f - SvcX0, SvcX1 - 0.2f - SvcX0, 0f, 2.2f) });
		// ---- the service corridor: from the mudroom's inner door to the bar
		k.Mat(LodgeTextures.CheckerMat);
		k.Quad(new Vector3(SvcX0, FloorY, BarZ0), new Vector3(SvcX1, FloorY, BarZ0), new Vector3(SvcX1, FloorY, SvcZ0), new Vector3(SvcX0, FloorY, SvcZ0), Vector3.Up,
			new Vector2(SvcX0, BarZ0) * 0.5f, new Vector2(SvcX1, BarZ0) * 0.5f, new Vector2(SvcX1, SvcZ0) * 0.5f, new Vector2(SvcX0, SvcZ0) * 0.5f);
		k.Mat(LodgeTextures.CeilingMat);
		CeilQuad(k, new Vector3(SvcX0, 3.0f, SvcZ0), new Vector3(SvcX1, 3.0f, SvcZ0), new Vector3(SvcX1, 3.0f, BarZ0), new Vector3(SvcX0, 3.0f, BarZ0));
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
		CeilQuad(k, new Vector3(BarX0, 3.0f, -InnerZ), new Vector3(pX1Back, 3.0f, -InnerZ), new Vector3(pX1Front, 3.0f, PantryZ1), new Vector3(BarX0, 3.0f, PantryZ1));
		LodgeKit.Solid(_inBody, new Vector3((BarX0 + pX1Front) * 0.5f, 3.1f, (PantryZ1 - InnerZ) * 0.5f), new Vector3(pX1Front - BarX0 + 1f, 0.2f, InnerZ + PantryZ1));
		Lining(k, BarX0, pX1Back, -InnerZ, FloorY, 3.0f, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat, 0f, true);
		Split(k, new Vector3(BarX0, 0, PantryZ1), new Vector3(pX1Front + 0.3f, 0, PantryZ1), Vector3.Forward, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat, null, 3.0f);
		Split(k, new Vector3(BarX0, 0, -InnerZ), new Vector3(BarX0, 0, PantryZ1), Vector3.Right, LodgeTextures.PlasterMat, LodgeTextures.PlasterMat, null, 3.0f);
		// the upper floor's slab over all of it (collision; its faces are the rooms' floors and ceilings)
		LodgeKit.Solid(_inBody, new Vector3((-WingX1 + 0.35f + CorrX1) * 0.5f, (SlabY + UpperY) * 0.5f, 0), new Vector3(CorrX1 - (-WingX1 + 0.35f), UpperY - SlabY, InnerZ * 2f));
		k.CommitTo(this, "WestGround", true);
		// the doors: the mudroom's inner door (unlocked), the pantry's (the pantry key)
		MudroomDoor = new LodgeDoor { Name = "MudroomDoor", LeafMat = BackDoorPaint, Position = new Vector3(BackDoorX - 0.5f, 0, SvcZ0), Width = 1.0f, Height = 2.15f, OpenAngle = 1.5f, FrontSign = -1f, OpenPrompt = "Open the door" };
		AddChild(MudroomDoor);
		MudroomDoor.Unlock();
		Vector3 pa = HexVert(3), pb = HexVert(4), palong = (pb - pa).Normalized(), pout = ((pa + pb) * 0.5f).Normalized();
		PantryDoor = new LodgeDoor
		{
			Name = "PantryDoor", LeafMat = BackDoorPaint, WallHalf = 0.4f, Position = pa + palong * 3.3f, Width = 1.1f, Height = 2.25f, Needs = ToolKind.PantryKey, OpenAngle = -1.5f, FrontSign = -1f,
			LockedPrompt = "\"STAFF ONLY - PANTRY\". Locked.",
		};
		PantryDoor.Basis = new Basis(palong, Vector3.Up, pout);
		AddChild(PantryDoor);
		SignKit.Text(this, "PANTRY", pa + palong * 3.85f - pout * 0.42f + Vector3.Up * 2.45f, new Basis(palong, Vector3.Up, pout) * new Basis(Vector3.Up, Mathf.Pi), 0.08f, new Color(0.62f, 0.5f, 0.3f), shadow: false);
	}

	/// <summary>A partition wall on the ground floor (to <paramref name="top"/>, default the slab), with a finish each side.</summary>
	private void Split(MeshKit k, Vector3 a, Vector3 b, Vector3 sideA, Material matA, Material matB, List<Hole> holes, float top = SlabY)
		=> LodgeKit.Wall(k, _inBody, a, b, FloorY, top, 0.15f, sideA, matA, matB, LodgeTextures.DarkWoodMat, holes, floorY: FloorY, grime: 2, skirt: true, ceilingY: top);

	// ------------------------------------------------------------------ the west wing, upstairs

	private void BuildWestUpper()
	{
		// (2026-10-14h, the kit pass: drawn finely, so the rooms' baked dark has vertices to sit on, SkiLodge.Kit)
		var k = new MeshKit { Tess = 0.6f };
		var k201 = new MeshKit { Tess = 0.6f, Color = Colors.White };
		k.Color = Colors.White;
		// the corridor: the carpet, a plaster ceiling, wallpaper over a dark wainscot
		// (2026-10-10c, the owner: "the carpet in the hallway needs to properly aligned like how we have it on the stairs":
		// it was one sheet wall to wall at its own scale, the pattern's rows of arches running off its edges. Now it's laid
		// as the stair's runner is: dark boards wall to wall, and the runner down the middle in two halves, the far one
		// mirrored, so the black ground lines run down both its edges and the arches point in to its middle, at the stair's
		// scale)
		float cx1 = CorrX1 - 0.3f, rw = 0.72f, ru = (cx1 - CorrX0) * 1.14f;
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Quad(new Vector3(CorrX0, UpperY, CorrHalf), new Vector3(cx1, UpperY, CorrHalf), new Vector3(cx1, UpperY, -CorrHalf), new Vector3(CorrX0, UpperY, -CorrHalf), Vector3.Up,
			new Vector2(0, 0), new Vector2(cx1 - CorrX0, 0), new Vector2(cx1 - CorrX0, 2f), new Vector2(0, 2f));
		k.Mat(LodgeTextures.CorridorCarpetMat);
		k.Quad(new Vector3(CorrX0, UpperY + 0.004f, rw), new Vector3(cx1, UpperY + 0.004f, rw), new Vector3(cx1, UpperY + 0.004f, 0), new Vector3(CorrX0, UpperY + 0.004f, 0), Vector3.Up,
			new Vector2(0, 0), new Vector2(ru, 0), new Vector2(ru, 0.5f), new Vector2(0, 0.5f));
		k.Quad(new Vector3(CorrX0, UpperY + 0.004f, 0), new Vector3(cx1, UpperY + 0.004f, 0), new Vector3(cx1, UpperY + 0.004f, -rw), new Vector3(CorrX0, UpperY + 0.004f, -rw), Vector3.Up,
			new Vector2(0, 0.5f), new Vector2(ru, 0.5f), new Vector2(ru, 0), new Vector2(0, 0));
		k.Mat(LodgeTextures.CeilingMat);
		CeilQuad(k, new Vector3(CorrX0, RoomTop, -CorrHalf), new Vector3(CorrX1 - 0.3f, RoomTop, -CorrHalf), new Vector3(CorrX1 - 0.3f, RoomTop, CorrHalf), new Vector3(CorrX0, RoomTop, CorrHalf));
		LodgeKit.Solid(_inBody, new Vector3((CorrX0 + CorrX1) * 0.5f, RoomTop + 0.1f, 0), new Vector3(CorrX1 - CorrX0, 0.2f, InnerZ * 2f));
		// its two walls, the rooms' doors in them (hinges at the doors' west ends on the north wall, east on the south)
		var north = new List<Hole> { new(-14.2f - CorrX0, -13.2f - CorrX0, UpperY, UpperY + 2.2f), new(-21.8f - CorrX0, -20.8f - CorrX0, UpperY, UpperY + 2.2f) };
		var south = new List<Hole> { new(-14.2f - CorrX0, -13.2f - CorrX0, UpperY, UpperY + 2.2f), new(-21.8f - CorrX0, -20.8f - CorrX0, UpperY, UpperY + 2.2f) };
		// (in two lengths each, split at the rooms' party wall, so each room's side takes that room's paper)
		var lo = new List<Hole>(); var hi = new List<Hole>();
		foreach (var h in north) (h.U0 < RoomSplitX - CorrX0 ? lo : hi).Add(h.U0 < RoomSplitX - CorrX0 ? h : new Hole(h.U0 - (RoomSplitX - CorrX0), h.U1 - (RoomSplitX - CorrX0), h.V0, h.V1));
		UpWall(k, new Vector3(CorrX0, 0, CorrHalf), new Vector3(RoomSplitX, 0, CorrHalf), Vector3.Forward, true, lo, matB: LodgeTextures.RoomPaperMat(201));
		UpWall(k, new Vector3(RoomSplitX, 0, CorrHalf), new Vector3(CorrX1, 0, CorrHalf), Vector3.Forward, true, hi, matB: LodgeTextures.RoomPaperMat(202));
		lo = new List<Hole>(); hi = new List<Hole>();
		foreach (var h in south) (h.U0 < RoomSplitX - CorrX0 ? lo : hi).Add(h.U0 < RoomSplitX - CorrX0 ? h : new Hole(h.U0 - (RoomSplitX - CorrX0), h.U1 - (RoomSplitX - CorrX0), h.V0, h.V1));
		UpWall(k, new Vector3(CorrX0, 0, -CorrHalf), new Vector3(RoomSplitX, 0, -CorrHalf), Vector3.Back, true, lo, matB: LodgeTextures.RoomPaperMat(204));
		UpWall(k, new Vector3(RoomSplitX, 0, -CorrHalf), new Vector3(CorrX1, 0, -CorrHalf), Vector3.Back, true, hi, matB: LodgeTextures.RoomPaperMat(203));
		UpWall(k, new Vector3(CorrX0, 0, -CorrHalf), new Vector3(CorrX0, 0, CorrHalf), Vector3.Right, true, null);
		// the corridor's walls carried on through the hall's corner wall to the doorway (the owner, coming up from the
		// lobby: the corner is cut at an angle either side of the doorway, and between the corridor's square end and
		// the angled cut there were open wedges, the outside through them), and a ceiling under the doorway's lintel
		UpWall(k, new Vector3(CorrX1, 0, CorrHalf), new Vector3(DoorwayInX, 0, CorrHalf), Vector3.Forward, true, null);
		UpWall(k, new Vector3(CorrX1, 0, -CorrHalf), new Vector3(DoorwayInX, 0, -CorrHalf), Vector3.Back, true, null);
		k.Mat(LodgeTextures.CeilingMat);
		CeilQuad(k, new Vector3(CorrX1 - 0.35f, 6.68f, -CorrHalf - 0.05f), new Vector3(DoorwayInX, 6.68f, -CorrHalf - 0.05f), new Vector3(DoorwayInX, 6.68f, CorrHalf + 0.05f), new Vector3(CorrX1 - 0.35f, 6.68f, CorrHalf + 0.05f));
		// the rooms: 202 (front, east), 201 (front, west), 203 (back, east), 204 (back, west)
		foreach (var (num, x0, x1, front) in new[] { (202, RoomSplitX, CorrX1, true), (201, CorrX0, RoomSplitX, true), (203, RoomSplitX, CorrX1, false), (204, CorrX0, RoomSplitX, false) })
			RoomShell(num == 201 ? k201 : k, num, x0, x1, front);
		k.CommitTo(this, "WestUpper", true);
		k201.CommitTo(this, "WestUpper201", true);   // (201, the perfect room, its own: the kit pass leaves it clean)
		// webs up in the rooms' and the corridor's ceiling corners (the interiors pass, 2026-10-03: years after the party,
		// nobody has been up here)
		var wr = new RandomNumberGenerator { Seed = 2301 };
		foreach (var (x0, x1, front) in new[] { (RoomSplitX, CorrX1, true), (CorrX0, RoomSplitX, true), (RoomSplitX, CorrX1, false), (CorrX0, RoomSplitX, false) })
		{
			float s = front ? 1f : -1f, zi = s * (CorrHalf + 0.12f), zo = s * (InnerZ - 0.1f);
			foreach (var (x, ax) in new[] { (x0 + 0.1f, 1f), (x1 - 0.1f, -1f) })
				foreach (var (z, az) in new[] { (zi, s), (zo, -s) })
					if (wr.Randf() < 0.8f) WebKit.Corner(this, wr, new Vector3(x, RoomTop - 0.02f, z), Vector3.Right * ax, Vector3.Back * az, wr.RandfRange(0.6f, 0.95f));
		}
		foreach (var (z, az) in new[] { (CorrHalf - 0.06f, -1f), (-CorrHalf + 0.06f, 1f) })
			WebKit.Corner(this, wr, new Vector3(CorrX0 + 0.06f, RoomTop - 0.02f, z), Vector3.Right, Vector3.Back * az, 0.7f);
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
	private void UpWall(MeshKit k, Vector3 a, Vector3 b, Vector3 sideA, bool paperA, List<Hole> holes, bool tileA = false, bool tileB = false, Material matA = null, Material matB = null)
	{
		var wA = tileA ? LodgeTextures.GreenTileMat : matA ?? LodgeTextures.WallpaperMat;
		var wB = tileB ? LodgeTextures.GreenTileMat : matB ?? LodgeTextures.WallpaperMat;
		var lA = tileA ? LodgeTextures.GreenTileMat : LodgeTextures.DarkWoodMat;
		var lB = tileB ? LodgeTextures.GreenTileMat : LodgeTextures.DarkWoodMat;
		LodgeKit.Wall(k, _inBody, a, b, UpperY, UpperY + 1.0f, 0.14f, sideA, lA, lB, LodgeTextures.DarkWoodMat, holes, floorY: UpperY, grime: 2, skirt: true);
		LodgeKit.Wall(k, _inBody, a, b, UpperY + 1.0f, RoomTop, 0.14f, sideA, wA, wB, LodgeTextures.DarkWoodMat, holes, grime: 2, ceilingY: RoomTop);
		// (2026-10-14h, the kit pass) a moulded chair rail capping the wainscot, each side that isn't tiled, broken at
		// the doors
		var n = (sideA - (b - a).Normalized() * sideA.Dot((b - a).Normalized())).Normalized();
		if (!tileA) ChairRail(k, a, b, n, 0.07f, holes);
		if (!tileB) ChairRail(k, a, b, -n, 0.07f, holes);
	}

	/// <summary>(2026-10-14h) A chair rail along a wall's face at the wainscot's top (<see cref="UpperY"/> + 1), from a to b
	/// (floor points on its centre line), <paramref name="half"/> its half-thickness, gaps where holes cross it.</summary>
	private static void ChairRail(MeshKit k, Vector3 a, Vector3 b, Vector3 n, float half, List<Hole> holes, float y = UpperY + 0.97f)
	{
		float len = new Vector2(b.X - a.X, b.Z - a.Z).Length();
		var along = ((b - a) with { Y = 0 }).Normalized();
		var cuts = new List<(float, float)> { (0f, len) };
		if (holes != null)
			foreach (var h in holes)
			{
				if (h.V0 > y + 0.05f || h.V1 < y) continue;
				var next = new List<(float, float)>();
				foreach (var (u0, u1) in cuts)
				{
					if (h.U1 + 0.08f <= u0 || h.U0 - 0.08f >= u1) { next.Add((u0, u1)); continue; }
					if (h.U0 - 0.08f > u0) next.Add((u0, h.U0 - 0.08f));
					if (h.U1 + 0.08f < u1) next.Add((h.U1 + 0.08f, u1));
				}
				cuts = next;
			}
		var keep = k.Tess;
		k.Tess = 0f;
		k.Mat(LodgeTextures.DoorWoodMat);
		foreach (var (u0, u1) in cuts)
			if (u1 - u0 > 0.1f)
				TrimKit.Along(k, (a with { Y = y }) + along * u0 + n * half, (a with { Y = y }) + along * u1 + n * half, n, TrimKit.ChairRail.Scaled(1.3f, 1.2f));
		k.Tess = keep;
	}

	/// <summary>A hotel room's shell: the carpet, the ceiling, the outer wall's lining with its two windows, the walls
	/// between rooms, and a bathroom tiled green (its door; 203's broken through into 204).</summary>
	/// <summary>A ceiling's quad or triangle (facing down), its plaster mapped in metres over the plan (a single tile
	/// stretched over a room showed as a blur).</summary>
	private static void CeilQuad(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
		=> k.Quad(a, b, c, d, Vector3.Down, CeilUv(a), CeilUv(b), CeilUv(c), CeilUv(d));

	/// <summary>(2026-10-14h) A flat triangle cut into <paramref name="n"/>² small ones, its UVs the plan's x, z times
	/// <paramref name="uv"/> (a big fan of a floor drawn finely enough to take the kit pass's bake).</summary>
	private static void TriGrid(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 nrm, float uv, int n)
	{
		Vector3 P(int i, int j) => a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n);
		Vector2 U(Vector3 p) => new Vector2(p.X, p.Z) * uv;
		for (int j = 0; j < n; j++)
			for (int i = 0; i < n - j; i++)
			{
				Vector3 p0 = P(i, j), p1 = P(i + 1, j), p2 = P(i, j + 1);
				k.Tri(p0, p1, p2, nrm, U(p0), U(p1), U(p2));
				if (i + j < n - 1)
				{
					Vector3 p3 = P(i + 1, j + 1);
					k.Tri(p1, p3, p2, nrm, U(p1), U(p3), U(p2));
				}
			}
	}

	private static void CeilTri(MeshKit k, Vector3 a, Vector3 b, Vector3 c)
		=> k.Tri(a, b, c, Vector3.Down, CeilUv(a), CeilUv(b), CeilUv(c));

	private static Vector2 CeilUv(Vector3 p) => new(p.X * 0.5f, p.Z * 0.5f);

	private void RoomShell(MeshKit k, int num, float x0, float x1, bool front)
	{
		float zIn = front ? CorrHalf : -CorrHalf, zOut = front ? InnerZ : -InnerZ, s = front ? 1f : -1f;
		k.Mat(LodgeTextures.RoomCarpetMat);
		k.Color = Colors.White;
		float za = Mathf.Min(zIn, zOut), zb = Mathf.Max(zIn, zOut);
		k.Quad(new Vector3(x0, UpperY, zb), new Vector3(x1, UpperY, zb), new Vector3(x1, UpperY, za), new Vector3(x0, UpperY, za), Vector3.Up,
			new Vector2(x0, zb) / 1.6f, new Vector2(x1, zb) / 1.6f, new Vector2(x1, za) / 1.6f, new Vector2(x0, za) / 1.6f);
		k.Mat(LodgeTextures.CeilingMat);
		CeilQuad(k, new Vector3(x0, RoomTop, za), new Vector3(x1, RoomTop, za), new Vector3(x1, RoomTop, zb), new Vector3(x0, RoomTop, zb));
		// the outer wall: its lining and windows (202's banked with snow, blue; 203's frosted, one left open)
		System.Func<float, float, Material> glass = num switch
		{
			202 => (_, _) => LodgeTextures.SnowBlueGlass,
			203 => (_, _) => LodgeTextures.IceGlass,
			_ => null,
		};
		var paper = LodgeTextures.RoomPaperMat(num);
		Lining(k, x0, x1, zOut, UpperY, RoomTop, LodgeTextures.DarkWoodMat, paper, UpperY + 1.0f, true, glass);
		// the side walls: east of 202/203 against the hall's corner, west of 201/204 at the corridor's end, and the split between
		if (x1 >= CorrX1 - 0.01f) UpWall(k, new Vector3(x1, 0, zIn), new Vector3(x1, 0, zOut), Vector3.Left, true, null, matA: paper);
		// (201's, in its bathroom, is hacked through: the doorway into the wall cavity, SkiLodge.Room201.cs)
		var west = num == 201 ? new List<Hole> { new(Hole201.Z - 0.45f - zIn, Hole201.Z + 0.45f - zIn, UpperY, UpperY + 2.0f) } : null;
		if (x0 <= CorrX0 + 0.01f) UpWall(k, new Vector3(x0, 0, zIn), new Vector3(x0, 0, zOut), Vector3.Right, true, west, matA: paper);
		// the bathroom: in the room's west corner by the corridor, 2.7 x 3.1, green tile
		float bx0 = x0, bx1 = x0 + 2.7f, bz = zIn + s * 3.1f;
		if (num == 202 || num == 203)
		{
			// the split wall between the pairs (202|201, 203|204) is built by the east room; 203's has the hole, in its bathroom
			var holes = num == 203 ? new List<Hole> { new(0.55f, 2.35f, UpperY, UpperY + 2.1f) } : null;   // the hole: within the bathroom's stretch
			// along z from the corridor outward; the hole is within the bathroom's stretch (0..3.1 from the corridor)
			UpWall(k, new Vector3(RoomSplitX, 0, zIn), new Vector3(RoomSplitX, 0, zOut), Vector3.Right, true, holes, tileA: false, tileB: false, matA: paper, matB: LodgeTextures.RoomPaperMat(num == 202 ? 201 : 204));
		}
		k.Mat(LodgeTextures.GreenTileMat);
		k.Quad(new Vector3(bx0 + 0.07f, UpperY + 0.003f, Mathf.Max(zIn, bz)), new Vector3(bx1, UpperY + 0.003f, Mathf.Max(zIn, bz)), new Vector3(bx1, UpperY + 0.003f, Mathf.Min(zIn, bz)), new Vector3(bx0 + 0.07f, UpperY + 0.003f, Mathf.Min(zIn, bz)), Vector3.Up,
			new Vector2(0, 0), new Vector2(2.7f / 1.2f, 0), new Vector2(2.7f / 1.2f, 3.1f / 1.2f), new Vector2(0, 3.1f / 1.2f));
		// its two walls: along the room (x = bx1, a door in it) and across (z = bz)
		UpWall(k, new Vector3(bx1, 0, zIn), new Vector3(bx1, 0, bz), Vector3.Left, true, new List<Hole> { new(1.2f, 2.1f, UpperY, UpperY + 2.1f) }, tileA: true, matB: paper);
		UpWall(k, new Vector3(bx0, 0, bz), new Vector3(bx1, 0, bz), new Vector3(0, 0, -s), true, null, tileA: true, matB: paper);
	}

	// ------------------------------------------------------------------ the east wing: the dining hall

	private ShaderMaterial _diningShafts;
	private const float DiningShaftStrength = 0.035f;

	private void BuildDiningShell()
	{
		var k = new MeshKit { Tess = 0.6f };   // (the kit pass's bake: SkiLodge.Kit)
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
			CeilTri(k, pts[0] with { Y = RoomTop }, pts[i + 1] with { Y = RoomTop }, pts[i] with { Y = RoomTop });
		k.Mat(LodgeTextures.DarkWoodMat);
		for (float x = 12f; x < DiningX1; x += 4f) k.Box(new Vector3(x, RoomTop - 0.14f, 0), new Vector3(0.28f, 0.28f, InnerZ * 2f), 1f);
		k.Box(new Vector3((12f + DiningX1) * 0.5f, RoomTop - 0.14f, 0), new Vector3(DiningX1 - 12f, 0.2f, 0.24f), 1f);
		// the walls: wainscot to 1.3 m, damask-free cream paper above (it's a dining room: pale, grand); both window rows
		Lining(k, fx, DiningX1, InnerZ, FloorY, RoomTop, LodgeTextures.DarkWoodMat, LodgeTextures.WallpaperMat, 1.3f, false);
		Lining(k, fx, DiningX1, -InnerZ, FloorY, RoomTop, LodgeTextures.DarkWoodMat, LodgeTextures.WallpaperMat, 1.3f, false);
		// the noon light through the south row's high windows, slanting down across the tables, the dust in it
		_diningShafts = WindowShafts.Material(new Color(0.86f, 0.84f, 0.78f), DiningShaftStrength);
		float wy = StoneTop + (WingWall - StoneTop) * 0.52f;
		int si = 0;
		foreach (float wx in WingWindows(1f, false))
		{
			if (wx < fx + 0.6f || wx > DiningX1 - 0.6f) continue;
			WindowShafts.Add(this, _diningShafts, new Vector3(wx, wy, InnerZ - 0.05f), Vector3.Right, Vector3.Up, 1.0f, 1.6f, new Vector3(0.18f, -0.62f, -0.76f), 9f, si++ % 2 == 0 ? 18 : 0);
		}
		// the near end: the hall's two east sides, a V pointing into the room; lined as the room is (their outside's stone
		// and dark boards had shown in here as a black end wall), the lobby's doors through the one
		foreach (int side in new[] { 0, 1 })
		{
			Vector3 va = HexVert(side == 0 ? 0 : 2), vb = HexVert(1);
			Vector3 n = (((va + vb) * 0.5f) with { Y = 0 }).Normalized(), along = (vb - va).Normalized();
			Vector3 o = va + n * 0.43f;
			float s0 = (Mathf.Abs(o.Z) - InnerZ) / Mathf.Abs(along.Z), s1 = Mathf.Abs(o.Z) / Mathf.Abs(along.Z);
			Vector3 la = o + along * s0, lb = o + along * s1;
			var doorHole = new List<Hole>();
			if (side == 0) doorHole.Add(new Hole(5.6f - s0, 8.0f - s0, 0f, 2.8f));
			LodgeKit.Wall(k, null, la, lb, FloorY, 1.3f, 0.03f, n, LodgeTextures.DarkWoodMat, LodgeTextures.DarkWoodMat, LodgeTextures.DarkWoodMat, doorHole, floorY: FloorY, grime: 1, skirt: true);
			LodgeKit.Wall(k, null, la, lb, 1.3f, RoomTop, 0.03f, n, LodgeTextures.WallpaperMat, LodgeTextures.WallpaperMat, LodgeTextures.DarkWoodMat, doorHole, grime: 1, ceilingY: RoomTop);
			k.Mat(LodgeTextures.DarkWoodMat);
			var rb = new Basis(along, Vector3.Up, n);
			float len = la.DistanceTo(lb);
			var runs = side == 0 ? new[] { (0f, 5.6f - s0 - 0.06f), (8.0f - s0 + 0.06f, len) } : new[] { (0f, len) };
			foreach (var (r0, r1) in runs)   // the rail (either side of the doors)
				k.Box(la + along * ((r0 + r1) * 0.5f) + Vector3.Up * 1.32f + n * 0.03f, new Vector3(r1 - r0, 0.05f, 0.04f), 1f, rb);
		}
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
		DiningDoorL = new LodgeDoor { Name = "DiningDoorL", Paired = true, WallHalf = 0.4f, Width = 1.2f, Height = 2.8f, Needs = ToolKind.DiningKey, OpenAngle = -1.4f, FrontSign = -1f, LockedPrompt = "\"DINING ROOM\". Locked." };
		DiningDoorL.Position = da + dalong * 5.6f;
		DiningDoorL.Basis = basis;
		AddChild(DiningDoorL);
		DiningDoorR = new LodgeDoor { Name = "DiningDoorR", Paired = true, WallHalf = 0.4f, Width = 1.2f, Height = 2.8f, Needs = ToolKind.DiningKey, OpenAngle = 1.4f, FrontSign = 1f, LockedPrompt = "\"DINING ROOM\". Locked." };
		DiningDoorR.Position = da + dalong * 8.0f;
		DiningDoorR.Basis = basis * new Basis(Vector3.Up, Mathf.Pi);
		AddChild(DiningDoorR);
		// either leaf's key opens both
		DiningDoorL.Opened += p => DiningDoorR.Open(p);
		DiningDoorR.Opened += p => DiningDoorL.Open(p);
		SignKit.Text(this, "DINING ROOM", da + dalong * 6.8f - dout * 0.42f + Vector3.Up * 3.1f, basis * new Basis(Vector3.Up, Mathf.Pi), 0.12f, new Color(0.7f, 0.55f, 0.3f), shadow: false);
	}
}
