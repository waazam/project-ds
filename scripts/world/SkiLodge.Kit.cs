using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// The lodge's rooms through the interiors kit (2026-10-14h; <c>docs/design/interior-kit.md</c>, the vestry its worked
/// example). The look survey found Act 23 lit edge to edge like a showroom: clean plaster, no dark in the corners, empty
/// floors. The lodge's builders draw each wing's rooms into a few big meshes, so the kit goes on afterwards, room by room:
/// <list type="bullet">
/// <item><b>the bake</b> (<see cref="RoomShade"/>): every room described by its box (and the hall's slanted sides as
/// planes), its own lamps as the pools it keeps its colour in, damp under its windows, the wear of feet from its doors;
/// the dark under its furniture from the furniture's own colliders. One pass over each mesh, each vertex taking the room
/// it lies in (a partition's two faces belong to the two rooms either side);</item>
/// <item><b>the weathering</b> (<see cref="RoomSurface"/>): the plaster, the paint, the boards, the carpets and the
/// ceilings swapped for their weathered selves, by name (the furniture, the brass, the glass left as they are);</item>
/// <item><b>the decals by rule</b> (<see cref="DecalDresser.Room"/>): stains, cracks, scuffs, soot over the lamps;</item>
/// <item><b>the clutter</b> (<see cref="ClutterKit"/>): wall-hugging, kept clear of every door's swing, every drawer,
/// switch, pickup and thing to look at.</item>
/// </list>
/// Room 201 is the one perfect room in the lodge (STORY.md): its shell is its own mesh, and it takes the bake's shade
/// but no grime, no decals and no clutter.
/// </summary>
public partial class SkiLodge
{
	private sealed class KitRoom
	{
		public string Name;
		public Aabb Box;
		public RoomShade Shade;
		public int Seed;
		public bool Perfect, Painted = true;
		public float Decals = 1f;
		/// <summary>A finer test than the box (the hall's hexagon; the rooms against its slanted sides).</summary>
		public System.Func<Vector3, bool> Inside;
		public readonly List<(Vector3 sill, Vector3 normal, float w, float h)> Windows = new();
		public readonly List<(Vector3 foot, Vector3 normal, float w, float h)> Doors = new();
		public readonly List<Vector3> SkipWalls = new();
		public readonly List<Vector3> Flames = new();
	}

	private readonly List<KitRoom> _kitRooms = new();
	private static StandardMaterial3D _kitCard;

	private KitRoom AddKitRoom(string name, float x0, float x1, float z0, float z1, float y0, float y1, int seed, float dim = 0.78f)
	{
		var r = new KitRoom { Name = name, Seed = seed, Box = new Aabb(new Vector3(Mathf.Min(x0, x1), y0, Mathf.Min(z0, z1)), new Vector3(Mathf.Abs(x1 - x0), y1 - y0, Mathf.Abs(z1 - z0))) };
		r.Shade = new RoomShade(r.Box) { Dim = dim };
		_kitRooms.Add(r);
		return r;
	}

	/// <summary>A wing wall's windows (as <see cref="Lining"/> cuts them), for the decals: sill middle, size.</summary>
	private static IEnumerable<(Vector3 sill, float w, float h)> WingWindowSpots(float x0, float x1, float zFace, float y0, float y1, bool west)
	{
		float side = Mathf.Sign(zFace);
		foreach (float wx in WingWindows(side, west))
		{
			if (wx < Mathf.Min(x0, x1) + 0.6f || wx > Mathf.Max(x0, x1) - 0.6f) continue;
			foreach (var (wy, ww, wh) in new[] { (1.7f, 0.9f, 1.1f), (StoneTop + (WingWall - StoneTop) * 0.52f, 1.1f, 1.7f) })
			{
				if (wy - wh * 0.5f < y0 || wy + wh * 0.5f > y1) continue;
				yield return (new Vector3(wx, wy - wh * 0.5f, zFace), ww, wh);
			}
		}
	}

	private void AddWingWindows(KitRoom r, float x0, float x1, float zFace, bool west)
	{
		var n = new Vector3(0, 0, -Mathf.Sign(zFace));
		foreach (var (sill, w, h) in WingWindowSpots(x0, x1, zFace, r.Box.Position.Y, r.Box.End.Y, west))
		{
			r.Windows.Add((sill, n, w, h));
			r.Shade.Damp(sill + n * 0.05f, 1.3f, 0.7f).Light(sill + Vector3.Up * h * 0.5f + n * 0.5f, 2.2f, 0.35f);
		}
	}

	// ------------------------------------------------------------------ the rooms

	private void DefineKitRooms()
	{
		float rt = RoomTop, uy = UpperY;
		// ---- the mudroom: flagstones, board wainscot, the back door (buried) and the corridor's door
		var mud = AddKitRoom("Mudroom", RoomX0 + 0.2f, RoomX1 - 0.2f, -WingHalfZ + 0.35f, SvcZ0 - 0.075f, FloorY, 2.9f, 2301, 0.86f);
		mud.Doors.Add((new Vector3(BackDoorX, FloorY, -WingHalfZ + 0.35f), Vector3.Back, BackDoorW + 0.3f, BackDoorH));
		mud.Doors.Add((new Vector3(BackDoorX, FloorY, SvcZ0 - 0.075f), Vector3.Forward, 1.0f, 2.15f));
		mud.Shade.Path(new Vector3(BackDoorX, 0, -6.2f), new Vector3(BackDoorX, 0, SvcZ0), 1.0f, 0.8f)
			.Damp(new Vector3(BackDoorX, 0.4f, -WingHalfZ + 0.4f), 2.0f, 0.9f);
		// ---- the service corridor: back-of-house plaster, checker lino
		var svc = AddKitRoom("Service", SvcX0 + 0.075f, SvcX1 - 0.075f, SvcZ0 + 0.075f, BarZ0 - 0.075f, FloorY, 3.0f, 2302, 0.84f);
		svc.Doors.Add((new Vector3(BackDoorX, FloorY, SvcZ0 + 0.075f), Vector3.Back, 1.0f, 2.15f));
		svc.Doors.Add((new Vector3(SvcX1 - 0.7f, FloorY, BarZ0 - 0.075f), Vector3.Forward, 1.0f, 2.2f));
		svc.Shade.Path(new Vector3(BackDoorX, 0, SvcZ0), new Vector3(SvcX1 - 0.7f, 0, BarZ0), 0.9f, 0.8f);
		// ---- the bar: knotty pine over a dark wainscot; its east side the hall's slanted wall, the arch in it
		var bar = AddKitRoom("Bar", BarX0 + 0.075f, -9.2f, BarZ0 + 0.075f, InnerZ - 0.04f, FloorY, SlabY, 2303, 0.84f);
		{
			Vector3 n = new(Mathf.Sin(Mathf.DegToRad(300f)), 0, Mathf.Cos(Mathf.DegToRad(300f)));
			bar.Shade.Plane(n * (Apothem + 0.4f), n);
		}
		bar.Inside = p => !InHex(p, Apothem + 0.3f);
		bar.Painted = false;
		bar.Decals = 0.7f;
		AddWingWindows(bar, BarX0, -10f, InnerZ, true);
		bar.Doors.Add((new Vector3(SvcX1 - 0.7f, FloorY, BarZ0 + 0.075f), Vector3.Back, 1.0f, 2.2f));
		bar.SkipWalls.Add(Vector3.Left);   // (the slanted wall's not the box's face)
		bar.Shade.Path(new Vector3(-10.5f, 0, 4.4f), new Vector3(BarCounterX + 0.6f, 0, 4.2f), 1.4f, 0.7f)
			.Under(new Aabb(new Vector3(BarCounterX - 0.7f, FloorY, 1.6f), new Vector3(0.8f, 1.2f, 5f)), 0.5f, 0.5f);
		// ---- the pantry: a narrow back room, its far end the hall's slanted wall
		float pX1 = FacetXAt(210f, 270f, PantryZ1);
		var pan = AddKitRoom("Pantry", BarX0 + 0.075f, pX1 + 1.2f, -InnerZ + 0.04f, PantryZ1 - 0.075f, FloorY, 3.0f, 2304, 0.82f);
		{
			Vector3 n = new(Mathf.Sin(Mathf.DegToRad(240f)), 0, Mathf.Cos(Mathf.DegToRad(240f)));
			pan.Shade.Plane(n * (Apothem + 0.4f), n);
		}
		pan.Inside = p => !InHex(p, Apothem + 0.3f);
		pan.SkipWalls.Add(Vector3.Left);
		pan.SkipWalls.Add(Vector3.Back);   // (the shelves' wall)
		pan.Decals = 0.6f;
		// ---- upstairs: the corridor and the four rooms
		var corr = AddKitRoom("Corridor", CorrX0 + 0.07f, CorrX1 - 0.3f, -CorrHalf + 0.07f, CorrHalf - 0.07f, uy, rt, 2305, 0.8f);
		corr.Shade.Path(new Vector3(CorrX1, uy, 0), new Vector3(CorrX0 + 0.5f, uy, 0), 0.9f, 0.6f);
		corr.Decals = 0.8f;
		foreach (var (num, x, front) in new[] { (202, -14.2f, true), (201, -21.8f, true), (203, -13.2f, false), (204, -20.8f, false) })
			corr.Doors.Add((new Vector3(x + (front ? 0.5f : -0.5f), uy, front ? CorrHalf - 0.07f : -CorrHalf + 0.07f), front ? Vector3.Forward : Vector3.Back, 1.0f, 2.2f));
		foreach (var (num, x0, x1, front) in new[] { (202, RoomSplitX, CorrX1, true), (201, CorrX0, RoomSplitX, true), (203, RoomSplitX, CorrX1, false), (204, CorrX0, RoomSplitX, false) })
		{
			float zi = front ? CorrHalf + 0.07f : -CorrHalf - 0.07f, zo = front ? InnerZ - 0.04f : -InnerZ + 0.04f;
			var r = AddKitRoom("Room" + num, x0 + 0.07f, x1 - 0.07f, zi, zo, uy, rt, 2310 + num, num == 201 ? 0.9f : 0.82f);
			r.Perfect = num == 201;
			AddWingWindows(r, x0, x1, front ? InnerZ : -InnerZ, true);
			float dx = num switch { 202 => -13.7f, 201 => -21.3f, 203 => -13.7f, _ => -21.3f };
			r.Doors.Add((new Vector3(dx, uy, zi), front ? Vector3.Back : Vector3.Forward, 1.0f, 2.2f));
			// (the bathroom's walls: a room of its own in the corner by the door)
			float bx1 = x0 + 2.7f, bz = (front ? CorrHalf : -CorrHalf) + (front ? 3.1f : -3.1f);
			r.Shade.Plane(new Vector3(bx1 + 0.07f, uy, 0), Vector3.Right).Plane(new Vector3(0, uy, bz + (front ? 0.07f : -0.07f)), front ? Vector3.Back : Vector3.Forward);
			r.Shade.Path(new Vector3(dx, uy, zi), new Vector3((x0 + x1) * 0.5f + 1f, uy, front ? 3.6f : -3.6f), 0.9f, num == 201 ? 0f : 0.6f);
			r.Decals = num == 203 ? 1.2f : 0.85f;
		}
		// ---- the dining hall: the wing's full height; its near end the hall's two slanted sides
		var din = AddKitRoom("Dining", 7.4f, DiningX1 - 0.02f, -InnerZ + 0.04f, InnerZ - 0.04f, FloorY, rt, 2320, 0.84f);
		foreach (float a in new[] { 60f, 120f })
		{
			Vector3 n = new(Mathf.Sin(Mathf.DegToRad(a)), 0, Mathf.Cos(Mathf.DegToRad(a)));
			din.Shade.Plane(n * (Apothem + 0.43f), n);
		}
		din.Inside = p => !InHex(p, Apothem + 0.3f);
		din.SkipWalls.Add(Vector3.Right);
		din.Decals = 1.3f;
		AddWingWindows(din, 12f, DiningX1, InnerZ, false);
		AddWingWindows(din, 12f, DiningX1, -InnerZ, false);
		foreach (float wz in new[] { -6f, -2f, 2f, 6f })
		{
			din.Windows.Add((new Vector3(DiningX1 - 0.02f, 1.15f, wz), Vector3.Left, 0.9f, 1.1f));
			din.Windows.Add((new Vector3(DiningX1 - 0.02f, 4.59f, wz), Vector3.Left, 1.1f, 1.7f));
		}
		din.Windows.RemoveAll(w => w.sill.Y > 3f);   // (the decals under the lower row only: two rows of sixteen windows made a hundred)
		din.Shade.Path(new Vector3(12f, 0, 0), new Vector3(DiningX1 - 2f, 0, 0), 1.6f, 0.6f);
		// ---- the great hall: a hexagon (the box's z faces its front and back walls; the four slanted sides as planes)
		var hall = AddKitRoom("Hall", -HexIn / 0.8660254f, HexIn / 0.8660254f, -HexIn, HexIn, FloorY, 21f, 2330, 0.86f);
		hall.Shade.Reach = 1.6f;
		hall.Shade.Head = 0.3f;
		foreach (float a in new[] { 60f, 120f, 240f, 300f })
		{
			Vector3 n = new(Mathf.Sin(Mathf.DegToRad(a)), 0, Mathf.Cos(Mathf.DegToRad(a)));
			hall.Shade.Plane(n * HexIn, -n);
		}
		hall.Inside = p => InHex(p, HexIn + 0.1f);
		hall.Painted = false;
		hall.Decals = 0.5f;
		hall.SkipWalls.Add(Vector3.Left);
		hall.SkipWalls.Add(Vector3.Right);
		hall.Shade.Path(new Vector3(0, 0, HexIn), new Vector3(0, 0, -2f), 2.2f, 0.6f).Path(new Vector3(0, 0, 2f), StairFoot, 1.4f, 0.6f).Path(new Vector3(0, 0, 0), new Vector3(-HexIn, 0, 4.5f), 1.6f, 0.5f);
		foreach (var (c, along, w, h) in _hallOpenings)
		{
			var n = along.Cross(Vector3.Up).Normalized();
			if (n.Dot(-c with { Y = 0 }) < 0) n = -n;   // into the hall
			Vector3 face = c + n * 0.4f;
			if (c.Y - h * 0.5f < 0.1f) hall.Doors.Add((face - Vector3.Up * h * 0.5f, n, w, h));
			else if (c.Y < 9f) { hall.Windows.Add((face - Vector3.Up * h * 0.5f, n, w, h)); hall.Shade.Damp(face - Vector3.Up * h * 0.5f, 1.4f, 0.6f); }
		}
	}

	// ------------------------------------------------------------------ the pass

	private void BuildKitPass()
	{
		using var __t = BuildTimer.Time("SkiLodge.KitPass");
		DefineKitRooms();
		// the rooms' own lights: each lamp keeps a pool of its colour round it (a ceiling light a wider one)
		foreach (var c in GetChildren())
		{
			if (c is not OmniLight3D l) continue;
			var r = RoomAt(l.Position);
			if (r == null) continue;
			string n = l.Name;
			float reach = Mathf.Clamp(l.OmniRange * 0.6f, 1.8f, 5.5f), s = n.StartsWith("WindowLight") || n.StartsWith("Daylight") || n.StartsWith("Frozen") ? 0.35f : 1f;
			r.Shade.Light(l.Position, reach, s);
			if (!n.StartsWith("Window") && !n.StartsWith("Daylight") && !n.StartsWith("Room") && !n.StartsWith("Snow") && !n.StartsWith("Bath") && !n.StartsWith("Frozen") && !n.StartsWith("Chandelier"))
			{
				r.Flames.Add(l.Position);
				r.Shade.Soot(l.Position + Vector3.Up * 0.15f, 0.5f, 0.25f);
			}
		}
		// the dark under everything standing in a room: its colliders (the furniture's, the counters', the beds')
		var under = new List<Aabb>();
		foreach (var body in new[] { _inBody, _body })
			if (body != null)
				foreach (var c in body.GetChildren())
					if (c is CollisionShape3D { Shape: BoxShape3D bs, Disabled: false } cs)
					{
						var box = cs.Transform * new Aabb(-bs.Size * 0.5f, bs.Size);
						// (furniture, not walls, floors or slabs: something under 2.6 m standing on a floor, under 4 m long)
						if (box.Size.Y > 2.6f || box.Size.Y < 0.2f || Mathf.Max(box.Size.X, box.Size.Z) > 4.2f || Mathf.Min(box.Size.X, box.Size.Z) < 0.2f) continue;
						var r = RoomAt(box.GetCenter() with { Y = box.Position.Y + 0.05f });
						if (r == null || Mathf.Abs(box.Position.Y - r.Box.Position.Y) > 0.15f) continue;
						r.Shade.Under(box, 0.45f, Mathf.Clamp(Mathf.Max(box.Size.X, box.Size.Z) * 0.35f, 0.25f, 0.6f));
						under.Add(box);
					}
		// the clutter (it adds its own dark under itself to the bake)
		ulong __t0 = Time.GetTicksMsec();
		DressKitRooms(under);
		// the bake: one pass over each mesh that reaches into a room
		ulong __t1 = Time.GetTicksMsec();
		int baked = 0;
		// (the rooms' shells and the procedural furniture drawn into them; the modelled furniture, the tree, the pantry's
		// stock and the outside are left: copying their arrays out and back cost most of a second for nothing)
		foreach (var c in GetChildren())
			if (c is MeshInstance3D mi && mi.Visible && (KitWeathers(mi.Name) || mi.Name.ToString().StartsWith("WestUpper") || mi.Name.ToString().StartsWith("KitClutter")) && BakeRooms(mi))
				baked++;
		ulong __t2 = Time.GetTicksMsec();
		// the weathering, room by room (the perfect room's own mesh left clean)
		foreach (var c in GetChildren())
			if (c is MeshInstance3D mi && mi.Mesh != null && !mi.Name.ToString().Contains("201") && KitWeathers(mi.Name))
				WeatherMesh(mi);
		ulong __t3 = Time.GetTicksMsec();
		// the decals by rule
		var dress = new Node3D { Name = "KitDress" };
		AddChild(dress);
		int decals = 0;
		foreach (var r in _kitRooms)
		{
			if (r.Perfect || r.Decals <= 0f) continue;
			var spec = new DecalDresser.RoomSpec { Box = r.Box, Seed = r.Seed, Amount = r.Decals, Painted = r.Painted };
			spec.Windows.AddRange(r.Windows);
			spec.Doors.AddRange(r.Doors);
			spec.Flames.AddRange(r.Flames);
			spec.SkipWalls.AddRange(r.SkipWalls);
			foreach (var k in KeepSpots()) spec.Keep.Add((k, 0.35f));
			var node = new Node3D { Name = r.Name };
			dress.AddChild(node);
			decals += DecalDresser.Room(node, spec).GetChildCount();
		}
		GD.Print($"[lodge kit] {_kitRooms.Count} rooms: {baked} meshes baked, {decals} decals (clutter {__t1 - __t0} ms, bake {__t2 - __t1} ms, weather {__t3 - __t2} ms, decals {Time.GetTicksMsec() - __t3} ms)");
	}

	private KitRoom RoomAt(Vector3 p, float eps = 0.03f) => RoomAmong(_kitRooms, p, eps);

	private static KitRoom RoomAmong(List<KitRoom> rooms, Vector3 p, float eps)
	{
		foreach (var r in rooms)
		{
			var b = r.Box.Grow(eps);
			if (b.HasPoint(p) && (r.Inside == null || r.Inside(p))) return r;
		}
		return null;
	}

	/// <summary>Is <paramref name="p"/> within <paramref name="d"/> of the hall's centre square to every one of its sides?</summary>
	private static bool InHex(Vector3 p, float d)
	{
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.DegToRad(60f * i);
			if (p.X * Mathf.Sin(a) + p.Z * Mathf.Cos(a) > d) return false;
		}
		return true;
	}

	/// <summary>Each vertex of <paramref name="mi"/> takes the shade of the room it lies in (exactly in a room first,
	/// then within a hand of one); vertices in no room are left. Modelled surfaces (their own second UV) are left.</summary>
	private bool BakeRooms(MeshInstance3D mi)
	{
		if (mi.Mesh is not ArrayMesh src) return false;
		var toRoom = GlobalTransform.AffineInverse() * mi.GlobalTransform;
		var aabb = toRoom * mi.GetAabb();
		bool any = false;
		foreach (var r in _kitRooms) if (r.Box.Grow(0.3f).Intersects(aabb)) { any = true; break; }
		if (!any) return false;
		var nb = toRoom.Basis.Inverse().Transposed();
		var mesh = new ArrayMesh();
		bool changed = false;
		for (int si = 0; si < src.GetSurfaceCount(); si++)
		{
			var arr = src.SurfaceGetArrays(si);
			var mat = src.SurfaceGetMaterial(si);
			bool modelled = arr[(int)Mesh.ArrayType.TexUV2].VariantType != Variant.Type.Nil;
			if (!modelled && src.SurfaceGetPrimitiveType(si) == Mesh.PrimitiveType.Triangles)
			{
				var v = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
				var nrm = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : (Vector3[])arr[(int)Mesh.ArrayType.Normal];
				var col = arr[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? null : (Color[])arr[(int)Mesh.ArrayType.Color];
				if (col == null) { col = new Color[v.Length]; System.Array.Fill(col, Colors.White); }
				var uv2 = new Vector2[v.Length];
				// (the surface's own bounds first: most of a wing's surfaces lie in one or two rooms, or none)
				var sb = new Aabb(toRoom * v[0], Vector3.Zero);
				for (int i = 1; i < v.Length; i++) sb = sb.Expand(toRoom * v[i]);
				var near = _kitRooms.FindAll(r => r.Box.Grow(0.15f).Intersects(sb.Grow(0.01f)));
				int hits = 0;
				if (near.Count > 0)
					// (pure maths on structs and the rooms' read-only descriptions: the vertices in parallel; the bake was 1.8 s)
					System.Threading.Tasks.Parallel.For(0, v.Length, i =>
					{
						Vector3 p = toRoom * v[i];
						var r = RoomAmong(near, p, 0.03f) ?? RoomAmong(near, p, 0.12f);
						if (r == null) return;
						Vector3 n = nrm != null ? (nb * nrm[i]).Normalized() : Vector3.Up;
						Color s = r.Shade.At(p, n);
						col[i] = new Color(col[i].R * s.R, col[i].G * s.G, col[i].B * s.B, col[i].A * s.A);
						uv2[i] = new Vector2(Mathf.Max(p.Y - r.Box.Position.Y, 0.001f), Mathf.Max(r.Box.End.Y - p.Y, 0.001f));
						System.Threading.Interlocked.Increment(ref hits);
					});
				bool hit = hits > 0;
				if (hit)
				{
					arr[(int)Mesh.ArrayType.Color] = col;
					arr[(int)Mesh.ArrayType.TexUV2] = uv2;
					changed = true;
				}
			}
			mesh.AddSurfaceFromArrays(src.SurfaceGetPrimitiveType(si), arr);
			mesh.SurfaceSetMaterial(si, mat);
		}
		if (changed) mi.Mesh = mesh;
		return changed;
	}

	/// <summary>Which of the lodge's meshes are rooms' shells (weathered), not furniture or the outside.</summary>
	private static bool KitWeathers(string name) =>
		name is "Mudroom" or "WestGround" or "WestUpper" or "DiningShell" or "LobbyShell" or "Hall" or "Balcony" or "Stairs" or "Bar" || name.StartsWith("Room");

	private static Dictionary<Material, RoomSurface.Weather> _kitWeather;

	/// <summary>The lodge's surfaces and how each weathers.</summary>
	private static Dictionary<Material, RoomSurface.Weather> KitWeather()
	{
		if (_kitWeather != null) return _kitWeather;
		var paint = RoomSurface.Paper with { Streaks = 0.55f, Tide = 0.45f, Mottle = 0.3f, Desaturate = 0.2f };
		var boards = RoomSurface.Wood with { Streaks = 0.15f, Tide = 0.15f, Mottle = 0.25f, Wear = 0.8f };
		var carpet = RoomSurface.Wood with { Streaks = 0f, Tide = 0.1f, Mottle = 0.35f, FloorDirt = 0.7f, Wear = 0.9f, Desaturate = 0.15f };
		var pine = RoomSurface.Wood with { Streaks = 0.3f, Tide = 0.2f, Mottle = 0.3f };
		var ceiling = RoomSurface.Plaster with { CeilingStains = 0.8f, Streaks = 0.4f };
		_kitWeather = new Dictionary<Material, RoomSurface.Weather>
		{
			[LodgeTextures.PlasterMat] = RoomSurface.Plaster with { Streaks = 0.8f, Tide = 0.7f },
			[LodgeTextures.WallpaperMat] = paint,
			[LodgeTextures.RoomPaperMat(202)] = paint,
			[LodgeTextures.RoomPaperMat(203)] = paint with { Streaks = 0.8f, Tide = 0.6f },
			[LodgeTextures.RoomPaperMat(204)] = paint,
			[LodgeTextures.CeilingMat] = ceiling,
			[LodgeTextures.DarkWoodMat] = boards,
			[LodgeTextures.KnottyPineMat] = pine,
			[LodgeTextures.LobbyStoneMat] = pine,
			[LodgeTextures.LogWallMat] = pine with { Streaks = 0.2f },
			[LodgeTextures.LobbyFloorMat] = boards,
			[LodgeTextures.ParquetMat] = boards,
			[LodgeTextures.CheckerMat] = RoomSurface.Concrete with { Streaks = 0f, Tide = 0.2f, FloorDirt = 0.7f, Wear = 0.8f },
			[LodgeTextures.FlagstoneMat] = RoomSurface.Stone with { FloorDirt = 0.8f, Wear = 0.9f },
			[LodgeTextures.CorridorCarpetMat] = carpet,
			[LodgeTextures.RoomCarpetMat] = carpet,
			[LodgeTextures.GreenTileMat] = RoomSurface.Concrete with { Streaks = 0.5f, Tide = 0.6f, Mottle = 0.2f, Grime = 0.8f },
			[LodgeTextures.RiverStoneMat] = RoomSurface.Stone,
			[BuildingTextures.BoardsMat] = boards,
		};
		return _kitWeather;
	}

	private void WeatherMesh(MeshInstance3D mi)
	{
		var table = KitWeather();
		for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
		{
			var src = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
			if (src == null || !table.TryGetValue(src, out var w)) continue;
			var m = RoomSurface.Of(src, w, 23);
			if (m != src) mi.SetSurfaceOverrideMaterial(s, m);
		}
	}

	/// <summary>Everything the player has to reach or see (doors, drawers, cabinets, switches, pickups, curios): lodge-local.</summary>
	private List<Vector3> KeepSpots()
	{
		if (_keepSpots != null) return _keepSpots;
		_keepSpots = new List<Vector3>();
		void Walk(Node n)
		{
			if (n is Node3D n3 && (n is Interactable || n is Searchable || n is LodgeDoor || n is LightSwitch || n is Pickup))
				_keepSpots.Add(ToLocal(n3.GlobalPosition));
			foreach (var c in n.GetChildren()) Walk(c);
		}
		Walk(this);
		// the saves (StationInterior's markers, the lamps and R.H.'s page beside them) and the spawn: fixed, so the clutter
		// lies the same whatever the save
		foreach (var p in new[] { new Vector3(BackDoorX, FloorY, -WingHalfZ + 3.2f), new Vector3(-15.5f, UpperY, 0f), new Vector3(-16.5f, UpperY, 0f), new Vector3(-19f, UpperY, 0f), new Vector3(-20f, UpperY, 0f),
			new Vector3(24f, FloorY, 0f), new Vector3(-24.6f, UpperY, 2.6f), new Vector3(32.05f, FloorY, -2.6f) })
			_keepSpots.Add(p);
		return _keepSpots;
	}
	private List<Vector3> _keepSpots;

	// ------------------------------------------------------------------ the clutter

	private void DressKitRooms(List<Aabb> standing)
	{
		_kitCard ??= new StandardMaterial3D { ResourceName = "lodge_clutter", AlbedoColor = new Color(0.4f, 0.32f, 0.22f), Roughness = 0.95f, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true };
		var card = _kitCard;
		var roles = Woodwork;
		roles["*"] = LodgeTextures.DoorWoodMat;
		var big = new MeshKit();
		var small = new MeshKit();
		big.Color = small.Color = Colors.White;
		var crate = ClutterKit.Model("crate", roles, canStackOn: true);
		var boots = ClutterKit.Model("boots", roles, solid: false);
		var sack = ClutterKit.Model("flour_sack", roles, solid: false);
		var logs = ClutterKit.Model("log_pile", roles);
		var locker = ClutterKit.Model("footlocker", roles, canStackOn: true);
		var mags = ClutterKit.Model("magazines", roles, solid: false);
		var books = ClutterKit.Model("books_stack", roles, solid: false);
		var chair = ClutterKit.Model("dining_chair", new Dictionary<string, Material>(roles) { ["upholstery"] = LodgeTextures.Upholstery("oxblood") });
		var carton = ClutterKit.Carton(card, new Vector3(0.44f, 0.32f, 0.34f));
		var cartonSmall = ClutterKit.Carton(card, new Vector3(0.3f, 0.22f, 0.26f), new Color(0.85f, 0.8f, 0.72f));
		var linen = ClutterKit.Bundle(card, card, new Vector3(0.42f, 0.16f, 0.32f), new Color(1.5f, 1.45f, 1.35f));
		var papers = ClutterKit.Bundle(card, card, new Vector3(0.32f, 0.1f, 0.24f), new Color(1.3f, 1.25f, 1.1f));
		var bucket = ClutterKit.Bucket(LodgeTextures.IronMat);
		var broom = ClutterKit.Broom(LodgeTextures.DoorWoodMat, card);
		var shovel = SnowShovel(card);
		var keep = KeepSpots();
		ClutterKit Kit(KitRoom r, float keepR = 0.9f)
		{
			var c = new ClutterKit(r.Box, r.Seed, r.Shade, _inBody);
			foreach (var b in standing) if (b.Intersects(r.Box.Grow(0.2f))) c.Keep(b.Grow(0.08f));
			foreach (var p in keep) if (r.Box.Grow(1f).HasPoint(p)) c.Keep(p with { Y = r.Box.Position.Y }, keepR);
			foreach (var (foot, n, w, _) in r.Doors) c.Keep(foot + n * 0.7f, Mathf.Max(0.9f, w * 0.75f));
			return c;
		}
		int placed = 0;
		foreach (var r in _kitRooms)
		{
			if (r.Perfect) continue;
			switch (r.Name)
			{
				case "Mudroom":
				{
					// the ski rack's wall and the bench's are the builder's; the snow come in at the back door kept clear,
					// and where they stand when it comes down
					var c = Kit(r).Keep(new Vector3(BackDoorX, FloorY, -6.4f), 1.7f).Keep(new Vector3(BackDoorX, FloorY, -4.6f), 1.1f)
						.Keep(new Aabb(new Vector3(r.Box.Position.X, FloorY, r.Box.Position.Z + 0.5f), new Vector3(0.95f, 2.4f, r.Box.Size.Z - 0.9f)))
						.Keep(new Aabb(new Vector3(r.Box.End.X - 0.6f, FloorY, -6.5f), new Vector3(0.6f, 2.4f, 3.6f)));
					var l = c.InCorner(big, logs, 1);
					c.InCorner(small, bucket, 3);
					c.Lean(small, shovel, 2);
					c.Lean(small, broom, 3);
					var cr = c.AgainstWall(big, crate, 2);
					c.On(small, cr, sack);
					c.AgainstWall(small, boots, 3);
					c.Free(small, boots);
					c.Scatter(small, new[] { cartonSmall, sack }, 3, 0.9f);
					placed += c.All.Count;
					break;
				}
				case "Service":
				{
					// the staff's notice board on the end wall (rotas, a calendar, a notice gone yellow), a fuse box
					Vector3 nb = new(r.Box.Position.X + 0.012f, 1.55f, (SvcZ0 + BarZ0) * 0.5f);
					var nbb = new Basis(Vector3.Back, Vector3.Up, Vector3.Right);
					big.Mat(LodgeTextures.DoorWoodMat);
					big.BevelBox(nb, new Vector3(0.95f, 0.65f, 0.025f), 0.008f, 1f, nbb);
					small.Mat(card);
					small.Color = new Color(0.62f, 0.42f, 0.26f);   // (cork)
					small.Box(nb + Vector3.Right * 0.014f, new Vector3(0.012f, 0.56f, 0.86f), 1f);
					var prng = new RandomNumberGenerator { Seed = 2306 };
					for (int i = 0; i < 6; i++)
					{
						small.Color = new Color(1.6f, 1.55f, 1.4f) * prng.RandfRange(0.75f, 1f);
						small.Box(nb + new Vector3(0.022f + i * 0.001f, prng.RandfRange(-0.15f, 0.15f), prng.RandfRange(-0.3f, 0.3f)), new Vector3(0.003f, prng.RandfRange(0.18f, 0.28f), prng.RandfRange(0.14f, 0.21f)), 1f, new Basis(Vector3.Right, prng.RandfRange(-0.1f, 0.1f)));
					}
					small.Color = Colors.White;
					big.Mat(LodgeTextures.IronMat);
					big.BevelBox(new Vector3(SvcX0 + 2.2f, 1.75f, BarZ0 - 0.075f - 0.07f), new Vector3(0.4f, 0.5f, 0.14f), 0.01f);
					big.Cylinder(new Vector3(SvcX0 + 2.2f, 2.0f, BarZ0 - 0.075f - 0.05f), new Vector3(SvcX0 + 2.2f, 2.98f, BarZ0 - 0.075f - 0.05f), 0.015f, 0.015f, 5, false);   // its conduit up to the ceiling
					var c = Kit(r).Keep(new Aabb(new Vector3(SvcX0 + 1.9f, FloorY, BarZ0 - 0.6f), new Vector3(0.6f, 2f, 0.6f)));
					c.Lean(small, broom, 0);
					var cr = c.InCorner(big, crate, 1);
					c.On(small, cr, carton);
					c.InCorner(small, bucket, 0);
					c.Scatter(small, new[] { carton, cartonSmall, linen }, 5, 0.95f, new[] { cartonSmall, linen });
					placed += c.All.Count;
					break;
				}
				case "Pantry":
				{
					var c = Kit(r, 0.7f);
					var cr = c.InCorner(big, crate, 0);
					c.On(small, cr, sack);
					c.AgainstWall(small, sack, 0);
					c.Scatter(small, new[] { carton, cartonSmall, sack }, 4, 1f, new[] { cartonSmall });
					placed += c.All.Count;
					break;
				}
				case "Bar":
				{
					// behind the counter: cases of bottles stacked; a chair knocked over by the tables
					var c = Kit(r, 0.8f).Keep(new Vector3(BarCounterX + 0.6f, FloorY, 4f), 1.0f);
					var cr = c.InCorner(big, crate, 0);
					c.On(small, cr, cartonSmall);
					c.AgainstWall(small, carton, 2);
					c.Topple(big, chair);
					placed += c.All.Count;
					break;
				}
				case "Corridor":
				{
					// a laundry bundle and a stack of old magazines left by the doors, a carton of linen
					var c = Kit(r, 0.7f);
					c.AgainstWall(small, linen, 3);
					c.AgainstWall(small, mags, 2);
					c.AgainstWall(small, cartonSmall, 2);
					placed += c.All.Count;
					break;
				}
				case "Room202":
				case "Room204":
				case "Room203":
				{
					// a guest's luggage, a stack of books, magazines, boots kicked off
					var c = Kit(r, 1.0f);
					var lk = c.AgainstWall(big, locker, -1);
					c.On(small, lk, papers);
					c.AgainstWall(small, boots, -1);
					c.Free(small, mags);
					if (r.Name != "Room203") c.Free(small, books);
					placed += c.All.Count;
					break;
				}
			}
		}
		if (!big.IsEmpty) big.CommitTo(this, "KitClutter", true);
		if (!small.IsEmpty) small.CommitTo(this, "KitClutterSmall", false);
		GD.Print($"[lodge kit] {placed} pieces of clutter");
	}

	/// <summary>(2026-10-14h) A framed trail map on a wall: <paramref name="c"/> its middle on the wall's face,
	/// <paramref name="n"/> out of the wall.</summary>
	private static void TrailMap(MeshKit k, Vector3 c, Vector3 n, float w, float h)
	{
		var keep = k.Color;
		k.Color = Colors.White;
		var along = Vector3.Up.Cross(n).Normalized();
		var b = new Basis(along, Vector3.Up, n);
		k.Mat(LodgeTextures.TrailMapMat);
		Vector3 o = c + n * 0.012f;
		k.Quad(o - along * w * 0.5f - Vector3.Up * h * 0.5f, o + along * w * 0.5f - Vector3.Up * h * 0.5f, o + along * w * 0.5f + Vector3.Up * h * 0.5f, o - along * w * 0.5f + Vector3.Up * h * 0.5f, n,
			new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		k.Mat(LodgeTextures.DoorWoodMat);
		const float f = 0.05f;
		k.BevelBox(c + n * 0.015f + Vector3.Up * (h * 0.5f + f * 0.5f), new Vector3(w + f * 2f, f, 0.03f), 0.008f, 1f, b);
		k.BevelBox(c + n * 0.015f - Vector3.Up * (h * 0.5f + f * 0.5f), new Vector3(w + f * 2f, f, 0.03f), 0.008f, 1f, b);
		k.BevelBox(c + n * 0.015f + along * (w * 0.5f + f * 0.5f), new Vector3(f, h, 0.03f), 0.008f, 1f, b);
		k.BevelBox(c + n * 0.015f - along * (w * 0.5f + f * 0.5f), new Vector3(f, h, 0.03f), 0.008f, 1f, b);
		k.Color = keep;
	}

	/// <summary>A snow shovel, for leaning: an ash handle, a D-grip, a broad blade.</summary>
	private static ClutterKit.Item SnowShovel(Material card) => new("shovel", (k, xf) =>
	{
		k.Mat(LodgeTextures.DoorWoodMat);
		k.Cylinder(xf * new Vector3(0, 0.32f, 0), xf * new Vector3(0, 1.3f, 0), 0.016f, 0.016f, 6, true);
		k.BevelBox(xf * new Vector3(0, 1.32f, 0), new Vector3(0.13f, 0.03f, 0.03f), 0.006f, 1f, xf.Basis);
		k.Mat(card);
		var keep = k.Color;
		k.Color = new Color(0.55f, 0.18f, 0.12f);   // (a red-painted blade, chipped)
		k.BevelBox(xf * new Vector3(0, 0.18f, 0.01f), new Vector3(0.42f, 0.36f, 0.02f), 0.006f, 1f, xf.Basis * new Basis(Vector3.Right, 0.15f));
		k.Color = keep;
	}, new Vector3(0.42f, 1.34f, 0.08f), false, true, false);
}
