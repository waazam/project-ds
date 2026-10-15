using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// The lodge lived in (2026-10-10b, the owner: "up the environmental and clutter detail of all of the buildings, mainly
/// the ski lodge ... explode the dining hall with christmas decorations ... populate the lobby with even more furniture so
/// it looks like a way more cozy place ... varied rugs, circular and oval ... populate every room in the lodge with
/// paintings ... from https://artvee.com/ ... bookshelves with books ... the fire place to be better and have a little
/// smolderings of fire left ... add ski equipment and other winter things like snow shoes to the walls ... we need to make
/// the world feel lived in it is very barren and pristine right now").
/// <list type="bullet">
/// <item><b>Paintings</b>: 24 public-domain paintings from Artvee (assets/textures/paintings, credits.txt): winter and
/// mountain landscapes in the lobby and the dining hall, old portraits down the corridor and in the rooms (the house's own
/// people, watching), hunting still lifes in the bar and by the fire. Each in a frame (gilt, or dark wood), hung on a real
/// stretch of wall: found by rays, so never across a window, a doorway or a wardrobe.</item>
/// <item><b>Rugs</b>: oval and round, in bottle green, powder blue, oxblood and old gold, each with its borders.</item>
/// <item><b>Winter gear on the walls</b>: old wooden skis and poles, crossed or stood side by side; snowshoes; a toboggan.</item>
/// <item><b>Bookshelves</b> against the free walls, full and leaning; <b>clutter</b> on every surface of the rooms
/// (books, magazines, papers, mugs, an ashtray), boots by the beds.</item>
/// <item><b>The fire</b>: what's left of it: charred logs, their cracks glowing and breathing, a bed of embers, a spark
/// now and then going up, a thread of smoke.</item>
/// <item><b>Christmas in the dining hall</b>: trees in its corners, wreaths down its walls, garlands swagged between them,
/// strings of coloured lights along it (a few still alight), presents heaped, candles on the tables.</item>
/// <item><b>Light</b>: sconces down the upstairs corridor, small warm pools between the doors.</item>
/// <item><b>The patio</b>: the porch's piers stood alone like a gate; now a covered patio either side of them, a deck on
/// posts, a lean-to roof heaped with snow, tables and chairs left out under it.</item>
/// </list>
/// </summary>
public partial class SkiLodge
{
	public int PaintingsHung { get; private set; }
	public int WallGear { get; private set; }
	public int Clutter { get; private set; }
	public int RugsLaid { get; private set; }
	public Vector3 HearthAt => _fireLight?.Position ?? Vector3.Zero;
	public readonly List<(Node3D node, MeshInstance3D canvas)> Paintings = new();
	private readonly List<(Vector3 at, Vector3 n, float half)> _hung = new();
	private RandomNumberGenerator _orng;

	private record Art(string Id, string Kind, Vector2I Px);
	private static readonly Art[] Arts =
	{
		new("607101sl", "still", new(500, 631)), new("5000151ld", "dark", new(500, 473)), new("553100ld", "dark", new(500, 406)),
		new("557161ld", "dark", new(500, 354)), new("549265ld", "dark", new(500, 415)), new("549333ld", "dark", new(500, 355)),
		new("553706ld", "dark", new(500, 531)), new("406851mt", "dark", new(500, 367)), new("407207mt", "dark", new(500, 545)),
		new("547223ld", "dark", new(500, 818)), new("553176ld", "dark", new(500, 425)), new("557419ld", "dark", new(500, 321)),
		new("406715mt", "dark", new(500, 369)), new("407224mt", "dark", new(500, 1207)), new("553698ld", "dark", new(500, 366)),
		new("557537ld", "dark", new(500, 430)), new("256249fg", "dark", new(500, 617)), new("550327ld", "dark", new(500, 685)),
		new("5000666ld", "dark", new(500, 391)), new("406841mt", "dark", new(500, 647)), new("407425mt", "dark", new(500, 643)),
		new("550439ld", "dark", new(500, 362)), new("550793ld", "dark", new(500, 729)), new("550990ld", "dark", new(500, 348)),
		new("5000359ld", "dark", new(500, 460)), new("550041ld", "dark", new(500, 405)), new("5000125ld", "dark", new(500, 747)),
		new("555722ld", "dark", new(500, 352)), new("554096ld", "dark", new(500, 578)), new("720098an", "dark", new(500, 337)),
		new("555666ld", "dark", new(500, 445)), new("556485ld", "dark", new(500, 609)), new("556899ld", "land", new(500, 332)),
		new("554098ld", "land", new(500, 376)), new("553430ld", "land", new(500, 416)), new("550783ld", "land", new(500, 641)),
		new("551077ld", "land", new(500, 424)), new("551192ld", "land", new(500, 385)), new("557991ld", "land", new(500, 377)),
		new("5000308ld", "land", new(500, 717)), new("5000412ld", "land", new(500, 386)), new("5000415ld", "land", new(500, 297)),
		new("554197ld", "land", new(500, 283)), new("556644ld", "land", new(500, 387)), new("554147ld", "land", new(500, 636)),
		new("557835ld", "land", new(500, 599)), new("5000159ld", "land", new(500, 237)), new("257456fg", "land", new(500, 340)),
		new("550915ld", "land", new(500, 789)), new("551111ld", "land", new(500, 418)), new("553167ld", "land", new(500, 348)),
		new("549252ld", "land", new(500, 376)), new("548930ld", "land", new(500, 374)), new("554148ld", "land", new(500, 388)),
		new("547048ld", "land", new(500, 742)), new("4000242fg", "face", new(500, 619)), new("257679fg", "face", new(500, 574)),
		new("257706fg", "face", new(500, 640)), new("257682fg", "face", new(500, 631)), new("257680fg", "face", new(500, 581)),
		new("259014fg", "face", new(500, 643)), new("4000266fg", "face", new(500, 605)), new("4000205fg", "face", new(500, 622)),
		new("258966fg", "face", new(500, 648)), new("258858fg", "face", new(500, 746)), new("258833fg", "face", new(500, 622)),
		new("257345fg", "face", new(500, 657)), new("257346fg", "face", new(500, 642)), new("257856fg", "face", new(500, 595)),
		new("257897fg", "face", new(500, 1288)), new("257823fg", "face", new(500, 771)), new("257772fg", "face", new(500, 655)),
		new("256222fg", "face", new(500, 776)), new("257707fg", "face", new(500, 585)), new("257683fg", "face", new(500, 579)),
		new("256544fg", "face", new(500, 623)), new("257774fg", "face", new(500, 791)), new("257660fg", "face", new(500, 629)),
		new("257612fg", "face", new(500, 630)), new("256546fg", "face", new(500, 698)), new("256501fg", "face", new(500, 671)),
		new("256484fg", "face", new(500, 646)), new("256519fg", "face", new(500, 624)), new("256167fg", "face", new(500, 629)),
		new("257781fg", "face", new(500, 593)), new("4000265fg", "face", new(500, 625)), new("607099sl", "still", new(500, 614)),
		new("606761sl", "still", new(500, 629)), new("606738sl", "still", new(500, 374)), new("607007sl", "still", new(500, 383)),
		new("607013sl", "still", new(500, 867)), new("607015sl", "still", new(500, 600)), new("607154sl", "still", new(500, 326)),
		new("607208sl", "still", new(500, 274)), new("607197sl", "still", new(500, 663)),
	};
	private readonly Dictionary<string, int> _artNext = new();

	/// <summary>(Called once the lodge's rooms and their colliders are in: the dressing finds its walls by rays.)</summary>
	private void BuildOverhaul()
	{
		_orng = new RandomNumberGenerator { Seed = 2310 };
		var t = GetTree().CreateTimer(0.3);
		t.Timeout += () =>
		{
			if (!IsInsideTree()) return;
			ulong t0 = Time.GetTicksMsec();
			CollectDoors(this);
			HangWinterGear();
			HangPaintings();
			LayRugs();
			Bookshelves();
			RoomClutter();
			Smoulder();
			ChristmasDining();
			CorridorSconces();
			Patio();
			LodgeHaunts();
			GD.Print($"[lodge] dressed: {PaintingsHung} paintings, {RugsLaid} rugs, {WallGear} pieces of winter gear, {Clutter} things about ({Time.GetTicksMsec() - t0} ms)");
		};
	}

	// ------------------------------------------------------------------ finding a wall

	/// <summary>A flat stretch of wall <paramref name="half"/> wide either side and <paramref name="halfH"/> high either side of
	/// where a ray from <paramref name="from"/> along <paramref name="dir"/> (lodge-local) first meets something: the point and
	/// its outward normal, or null (a window, a doorway, a piece of furniture, a corner).</summary>
	private (Vector3 at, Vector3 n)? WallAt(Vector3 from, Vector3 dir, float maxD, float half, float halfH)
	{
		var space = GetWorld3D().DirectSpaceState;
		dir = dir.Normalized();
		float? Hit(Vector3 o, out Vector3 n)
		{
			n = Vector3.Zero;
			var a = ToGlobal(o);
			var h = space.IntersectRay(PhysicsRayQueryParameters3D.Create(a, ToGlobal(o + dir * maxD), 1));
			if (h.Count == 0) return null;
			n = GlobalBasis.Inverse() * (Vector3)h["normal"];
			return a.DistanceTo((Vector3)h["position"]);
		}
		var d0 = Hit(from, out var n0);
		if (d0 == null || Mathf.Abs(n0.Y) > 0.15f || n0.Dot(dir) > -0.7f) return null;
		var side = new Vector3(-n0.Z, 0, n0.X).Normalized();
		foreach (var o in new[] { side * half, -side * half, Vector3.Up * halfH, Vector3.Down * halfH, side * half + Vector3.Up * halfH, -side * half - Vector3.Up * halfH })
		{
			var d = Hit(from + o, out var n);
			if (d == null || Mathf.Abs(d.Value - d0.Value) > 0.06f || n.Dot(n0) < 0.95f) return null;
		}
		var at = from + dir * d0.Value;
		// (some faces have no collider of their own, only the outer wall's behind them: a window on the same wall gives the
		// face's true plane, and the painting goes onto that)
		float best = 1.5f;
		foreach (var (wc, along, inward, ww, wh) in _winSpots)
		{
			if (inward.Dot(n0) < 0.9f) continue;
			float off = (at - wc).Dot(inward);
			if (Mathf.Abs(off) < Mathf.Abs(best)) best = off;
		}
		if (Mathf.Abs(best) < 1.5f) at -= n0 * best;
		// not over a window
		foreach (var (wc, along, inward, ww, wh) in _winSpots)
		{
			if (inward.Dot(n0) < 0.9f || Mathf.Abs((at - wc).Dot(inward)) > 0.3f) continue;
			float da = Mathf.Abs((at - wc).Dot(along)), dy = Mathf.Abs(at.Y - wc.Y);
			if (da < ww * 0.5f + half + 0.25f && dy < wh * 0.5f + halfH + 0.25f) return null;
		}
		if (!ClearOfDoors(at, n0, half, halfH)) return null;
		// not too near one already hung on that wall
		foreach (var (p, pn, ph) in _hung)
			if (pn.Dot(n0) > 0.9f && Mathf.Abs((p - at).Dot(pn)) < 0.3f && (p - at).Length() < ph + half + 0.35f) return null;
		return (at, n0.Normalized());
	}

	/// <summary>Every door in the lodge, as a rectangle on its wall (its frame's trim included): nothing is hung over one
	/// (the owner: "the pictures you put in are clipping over doors in that hallway").</summary>
	private readonly List<(Vector3 c, Vector3 along, float w, float h)> _doorRects = new();

	private void CollectDoors(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is LodgeParts.LodgeDoor d)
			{
				var along = (GlobalBasis.Inverse() * d.GlobalBasis.X).Normalized();
				var hinge = ToLocal(d.GlobalPosition);
				_doorRects.Add((hinge + along * d.Width * 0.5f + Vector3.Up * d.Height * 0.5f, along, d.Width + 0.4f, d.Height + 0.3f));
			}
			else if (c.GetChildCount() > 0) CollectDoors(c);
		}
	}

	private bool ClearOfDoors(Vector3 at, Vector3 n, float half, float halfH)
	{
		foreach (var (dc, along, w, h) in _doorRects)
		{
			if (Mathf.Abs(along.Dot(n)) > 0.3f || Mathf.Abs((at - dc).Dot(n)) > 0.45f) continue;   // (not on this wall)
			if (Mathf.Abs((at - dc).Dot(along)) < w * 0.5f + half + 0.15f && Mathf.Abs(at.Y - dc.Y) < h * 0.5f + halfH + 0.1f) return false;
		}
		return true;
	}

	// ------------------------------------------------------------------ paintings

	private Art NextArt(string kind)
	{
		var pool = System.Array.FindAll(Arts, a => a.Kind == kind);
		_artNext.TryGetValue(kind, out int i);
		_artNext[kind] = i + 1;
		return pool[(i + kind.Length * 3) % pool.Length];
	}

	private void HangPaintings()
	{
		var rooms = new List<(Vector3 c, string kind, float size, int rays)>
		{
			// the lobby, the ground floor's walls and the gallery's
			(new Vector3(0, FloorY + 2.1f, 0), "land", 1.25f, 18),
			(new Vector3(0, UpperY + 1.9f, 0), "face", 1.0f, 14),
			// the dining hall, down its length
			(new Vector3(14f, FloorY + 2.3f, 0), "land", 1.3f, 8), (new Vector3(24f, FloorY + 2.3f, 0), "dark", 1.2f, 8),
			(new Vector3(34f, FloorY + 2.3f, 0), "still", 1.3f, 8), (new Vector3(41f, FloorY + 2.3f, 0), "face", 1.2f, 8),
			// the bar
			(new Vector3(-20f, FloorY + 2.75f, 3.2f), "still", 0.7f, 16),   // (above the back bar's shelves)
		};
		// the corridor upstairs, and the rooms off it: the house's people, watching
		CorridorPaintings();
		RoomPaintings();
		foreach (var (c, kind, size, rays) in rooms)
		{
			for (int r = 0; r < rays; r++)
			{
				float a = (r + _orng.Randf() * 0.4f) / rays * Mathf.Tau;
				var dir = rays == 4 ? (r % 2 == 0 ? Vector3.Back : Vector3.Forward) : new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
				var art = NextArt(kind);
				float h = size * _orng.RandfRange(0.8f, 1.15f), w = h * art.Px.X / art.Px.Y;
				if (w > size * 1.6f) { w = size * 1.6f; h = w * art.Px.Y / art.Px.X; }
				var from = rays == 4 ? c + new Vector3(_orng.RandfRange(-0.6f, 0.6f), 0, 0) : c;
				if (WallAt(from, dir, 16f, w * 0.5f + 0.12f, h * 0.5f + 0.12f) is not { } spot) { _artNext[kind]--; continue; }
				Hang(art, spot.at, spot.n, w, h);
			}
		}
	}

	/// <summary>The corridor's portraits: in the stretches of wall between its doors, both sides, level and evenly spaced
	/// (they were found by rays, and some hung over the doors' frames).</summary>
	private void CorridorPaintings()
	{
		// the doors (both walls) are at -21.8..-20.8 (201|204) and -14.2..-13.2 (202|203): two portraits in each stretch
		// between them, a sconce between the two (CorridorSconces)
		foreach (float side in new[] { 1f, -1f })
			foreach (float x in CorridorArtX)
			{
				var n = side > 0 ? Vector3.Forward : Vector3.Back;   // (the north wall's face looks south, -z)
				var at = new Vector3(x, UpperY + 1.75f, side * CorrHalf + n.Z * 0.025f);   // (the 14 cm wall's face, as the rooms')
				var art = NextArt("face");
				float h = _orng.RandfRange(0.72f, 0.85f), w = Mathf.Min(h * art.Px.X / art.Px.Y, 1.1f);
				h = w * art.Px.Y / art.Px.X;
				if (!ClearOfDoors(at, n, w * 0.5f, h * 0.5f)) continue;
				Hang(art, at, n, w, h);
			}
	}

	private static readonly float[] CorridorArtX = { -26.4f, -23.5f, -19.1f, -15.9f };
	private static readonly float[] CorridorSconceX = { -24.95f, -17.5f };

	/// <summary>The rooms' own walls have no colliders of their own to find: their planes are known, so a painting goes on
	/// each side wall and over the door's wall, wherever nothing tall stands in front of it (a ray from the room's middle).</summary>
	private void RoomPaintings()
	{
		var space = GetWorld3D().DirectSpaceState;
		var doorX = new Dictionary<(float, bool), float> { [(RoomSplitX, true)] = -14.2f, [(CorrX0, true)] = -21.8f, [(RoomSplitX, false)] = -13.2f, [(CorrX0, false)] = -20.8f };
		int placed = 0;
		foreach (var (x0, x1, front) in new[] { (RoomSplitX, CorrX1, true), (CorrX0, RoomSplitX, true), (RoomSplitX, CorrX1, false), (CorrX0, RoomSplitX, false) })
		{
			float s = front ? 1f : -1f, zIn = s * CorrHalf, y = UpperY + 1.75f;
			float xw = Mathf.Min(x0, x1), xe = Mathf.Max(x0, x1), dx = doorX[(x0, front)];
			var mid = new Vector3((xw + xe) * 0.5f, y, s * 4.6f);
			// candidates on each wall, the first clear one taken: the west wall past the bathroom (its corner by the corridor,
			// 3.1 deep), the east wall anywhere, the corridor's wall either side of the door and clear of the bathroom
			// (the walls are 14 cm thick about their lines: the room's face is 7 cm in, and Hang stands it 7.5 cm off)
			var walls = new[]
			{
				new[] { (new Vector3(xw + 0.025f, y, s * 5.9f), Vector3.Right), (new Vector3(xw + 0.025f, y, s * 5.2f), Vector3.Right) },
				new[] { (new Vector3(xe - 0.025f, y, s * 4.4f), Vector3.Left), (new Vector3(xe - 0.025f, y, s * 3.0f), Vector3.Left), (new Vector3(xe - 0.025f, y, s * 5.9f), Vector3.Left) },
				new[] { (new Vector3(dx - s * 1.5f, y + 0.1f, zIn + s * 0.025f), new Vector3(0, 0, s)), (new Vector3(dx + s * 1.5f, y + 0.1f, zIn + s * 0.025f), new Vector3(0, 0, s)) },
			};
			foreach (var wall in walls)
				foreach (var (at, n) in wall)
				{
					if (at.X < xw + 3.2f && Mathf.Abs(at.Z) < CorrHalf + 3.3f) continue;   // (in the bathroom)
					if (at.X < xw + 0.7f && n.X == 0 || at.X > xe - 0.7f && n.X == 0) continue;   // (too near a corner)
					var q = PhysicsRayQueryParameters3D.Create(ToGlobal(mid), ToGlobal(at + n * 0.3f), 1);
					if (space.IntersectRay(q).Count > 0) continue;   // (something tall in front of it: the wardrobe, the closet)
					var art = NextArt(_orng.Randf() < 0.45f ? "face" : _orng.Randf() < 0.5f ? "dark" : "land");
					float h = _orng.RandfRange(0.7f, 0.9f), w = Mathf.Min(h * art.Px.X / art.Px.Y, 1.3f);
					h = w * art.Px.Y / art.Px.X;
					Hang(art, at, n, w, h);
					placed++;
					break;
				}
		}
		// and one on each room's window wall, in the widest stretch of it between the windows and the corners (that's the
		// wall you face coming in)
		foreach (var (x0, x1, front) in new[] { (RoomSplitX, CorrX1, true), (CorrX0, RoomSplitX, true), (RoomSplitX, CorrX1, false), (CorrX0, RoomSplitX, false) })
		{
			float s = front ? 1f : -1f, xw = Mathf.Min(x0, x1), xe = Mathf.Max(x0, x1);
			var edges = new List<(float a, float b)>();
			foreach (var (wc, along, inward, ww, wh) in _winSpots)
				if (Mathf.Abs(wc.Z - s * InnerZ) < 0.3f && wc.X > xw && wc.X < xe && wc.Y > UpperY) edges.Add((wc.X - ww * 0.5f - 0.15f, wc.X + ww * 0.5f + 0.15f));
			edges.Sort((p, q) => p.a.CompareTo(q.a));
			float cur = xw + 0.3f, bestA = 0, bestB = 0;
			foreach (var (a, b) in edges) { if (a - cur > bestB - bestA) { bestA = cur; bestB = a; } cur = Mathf.Max(cur, b); }
			if (xe - 0.3f - cur > bestB - bestA) { bestA = cur; bestB = xe - 0.3f; }
			if (bestB - bestA < 1.1f) continue;
			var n = new Vector3(0, 0, -s);
			var at = new Vector3((bestA + bestB) * 0.5f, UpperY + 1.95f, s * InnerZ + n.Z * 0.025f);
			var art = NextArt(_orng.Randf() < 0.5f ? "land" : "dark");
			float h = 0.75f, w = Mathf.Min(h * art.Px.X / art.Px.Y, bestB - bestA - 0.4f);
			h = w * art.Px.Y / art.Px.X;
			if (!ClearOfDoors(at, n, w * 0.5f, h * 0.5f)) continue;
			Hang(art, at, n, w, h);
			placed++;
		}
		GD.Print($"[lodge] {placed} paintings in the rooms");
	}

	private void Hang(Art art, Vector3 at, Vector3 n, float w, float h)
	{
		string path = $"res://assets/textures/paintings/av_{art.Id}.jpg";
		if (!ResourceLoader.Exists(path)) return;
		var node = new Node3D { Name = $"Painting_{art.Id}_{PaintingsHung}" };
		AddChild(node);
		node.Position = at + n * 0.075f;   // (the walls' faces stand a little proud of their colliders)
		node.Basis = Basis.LookingAt(-n, Vector3.Up);   // (its -Z into the wall: the canvas faces +Z, out into the room)
		// a hair off level, now and then (somebody brushed past it)
		if (_orng.Randf() < 0.25f) node.Rotate(n, _orng.RandfRange(-0.04f, 0.04f));
		bool gilt = art.Kind is "face" or "still" || _orng.Randf() < 0.4f;
		var k = new MeshKit();
		k.Color = gilt ? new Color(0.8f, 0.72f, 0.6f) : Colors.White;
		float fw = Mathf.Clamp(Mathf.Min(w, h) * 0.08f, 0.04f, 0.1f);
		// (2026-10-15, the props pass: a moulding swept round it and mitred, a crest and a cove; it was four flat battens)
		LodgeProps.PictureFrame(k, w, h, fw, gilt ? LodgeTextures.GoldMat : LodgeTextures.DoorWoodMat);
		// a thin inner lip, darker
		k.Color = Colors.White;
		k.Mat(LodgeTextures.BlackMat);
		k.Box(new Vector3(0, 0, -0.012f), new Vector3(w + 0.01f, h + 0.01f, 0.02f), 1f);
		k.CommitTo(node, "Frame", false);
		var canvas = new MeshInstance3D
		{
			Name = "Canvas", Mesh = new QuadMesh { Size = new Vector2(w, h) }, Position = new Vector3(0, 0, 0.004f),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoTexture = GD.Load<Texture2D>(path), Roughness = 0.55f, MetallicSpecular = 0.35f,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic, AlbedoColor = new Color(0.92f, 0.9f, 0.86f),
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		node.AddChild(canvas);
		Paintings.Add((node, canvas));
		_hung.Add((at, n, w * 0.5f));
		PaintingsHung++;
	}

	// ------------------------------------------------------------------ rugs

	private static readonly (string name, Color field, Color border, Color motif)[] RugPalettes =
	{
		("green", new Color(0.12f, 0.26f, 0.16f), new Color(0.62f, 0.5f, 0.28f), new Color(0.5f, 0.16f, 0.12f)),
		("powderblue", new Color(0.52f, 0.64f, 0.74f), new Color(0.2f, 0.26f, 0.42f), new Color(0.86f, 0.82f, 0.7f)),
		("oxblood", new Color(0.36f, 0.08f, 0.08f), new Color(0.14f, 0.12f, 0.2f), new Color(0.74f, 0.6f, 0.36f)),
		("gold", new Color(0.62f, 0.5f, 0.26f), new Color(0.3f, 0.14f, 0.1f), new Color(0.18f, 0.3f, 0.28f)),
	};
	private static readonly Dictionary<string, StandardMaterial3D> _rugMats = new();

	private static StandardMaterial3D RugMaterial(int palette)
	{
		var (name, field, border, motif) = RugPalettes[palette % RugPalettes.Length];
		if (_rugMats.TryGetValue(name, out var m)) return m;
		// (2026-10-10c, the owner: "Make sure all the new rugs in the lodge have more design details on them and make them
		// more christmas themed") the drawn Christmas rugs, tools/textures/make_rugs.py: holly, snowflakes, pines, candy cane
		string drawn = $"res://assets/textures/lodge/rug_{new[] { "holly", "snowflake", "pines", "candycane" }[palette % 4]}.png";
		if (ResourceLoader.Exists(drawn))
		{
			m = new StandardMaterial3D
			{
				ResourceName = "lodge_rug_" + name, AlbedoTexture = GD.Load<Texture2D>(drawn), Roughness = 1f, MetallicSpecular = 0.05f,
				Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.5f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
			};
			DetailKit.Apply(m, DetailKit.Kind.Fabric);
			_rugMats[name] = m;
			return m;
		}
		const int s = 256;
		var img = Image.CreateEmpty(s, s, true, Image.Format.Rgba8);
		var noise = new FastNoiseLite { Seed = 410 + palette, Frequency = 0.08f };
		for (int y = 0; y < s; y++)
			for (int x = 0; x < s; x++)
			{
				float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
				float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
				Color c = field;
				// the borders: an outer band, a fine line, a band of motifs round it; a medallion in the middle
				if (r > 0.86f) c = border;
				else if (r > 0.8f) c = motif;
				else if (r > 0.66f) c = Mathf.Sin(a * 24f) * Mathf.Sin((r - 0.66f) / 0.14f * Mathf.Pi) > 0.35f ? motif : border.Lerp(field, 0.4f);
				else if (r < 0.22f) c = Mathf.Sin(a * 8f + r * 20f) > 0.2f ? motif : border;
				else if (r < 0.3f) c = border;
				else if (Mathf.Abs(Mathf.Sin(a * 6f) * 0.3f + 0.45f - r) < 0.03f) c = motif.Lerp(field, 0.3f);
				// the wear: worn pale in places, the weave
				float wear = Mathf.Clamp(noise.GetNoise2D(x, y) * 0.5f + 0.5f, 0f, 1f);
				c = c.Lerp(c.Lightened(0.25f), Mathf.Max(0f, wear - 0.7f) * 1.5f) * (0.92f + 0.08f * ((x + y) % 3 == 0 ? 1f : 0f));
				img.SetPixel(x, y, new Color(c.R, c.G, c.B, r < 0.99f ? 1f : 0f));
			}
		img.GenerateMipmaps();
		m = new StandardMaterial3D
		{
			ResourceName = "lodge_rug_" + name, AlbedoTexture = ImageTexture.CreateFromImage(img), Roughness = 1f, MetallicSpecular = 0.05f,
			Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		};
		_rugMats[name] = m;
		return m;
	}

	/// <summary>An oval (or round) rug at <paramref name="c"/> (lodge-local, on the floor), <paramref name="rx"/> by
	/// <paramref name="rz"/>, turned <paramref name="yaw"/>.</summary>
	private void Rug(MeshKit k, Vector3 c, float rx, float rz, float yaw, int palette)
	{
		k.Mat(RugMaterial(palette));
		k.Color = Colors.White;
		var b = new Basis(Vector3.Up, yaw);
		const int n = 40;
		for (int i = 0; i < n; i++)
		{
			float a0 = i / (float)n * Mathf.Tau, a1 = (i + 1) / (float)n * Mathf.Tau;
			Vector3 p0 = c + b * new Vector3(Mathf.Cos(a0) * rx, 0, Mathf.Sin(a0) * rz), p1 = c + b * new Vector3(Mathf.Cos(a1) * rx, 0, Mathf.Sin(a1) * rz);
			k.Tri(c, p1, p0, Vector3.Up, new Vector2(0.5f, 0.5f), new Vector2(0.5f + Mathf.Cos(a1) * 0.5f, 0.5f + Mathf.Sin(a1) * 0.5f), new Vector2(0.5f + Mathf.Cos(a0) * 0.5f, 0.5f + Mathf.Sin(a0) * 0.5f));
		}
		RugsLaid++;
	}

	private void LayRugs()
	{
		var k = new MeshKit();
		float y = FloorY + 0.009f;
		Rug(k, new Vector3(-5.6f, y, -1.2f), 2.1f, 1.4f, 0.4f, 0);    // under the west seating, bottle green
		Rug(k, new Vector3(0.6f, y, 6.2f), 1.25f, 1.25f, 0f, 1);      // inside the doors, powder blue, round
		Rug(k, new Vector3(5.8f, y, -3.6f), 1.1f, 1.1f, 0f, 2);       // under the chess table, oxblood, round
		Rug(k, new Vector3(-1.2f, y, -6.4f), 1.8f, 1.1f, 1.2f, 3);    // by the stair's foot, old gold
		// the dining hall, between the table rows
		for (int i = 0; i < 3; i++) Rug(k, new Vector3(17f + i * 10f, y, 0f), 2.6f, 1.0f, 0f, i % 2 == 0 ? 2 : 0);
		// a rug beside each bed upstairs
		foreach (var (x0, x1, front) in new[] { (RoomSplitX, CorrX1, true), (CorrX0, RoomSplitX, true), (RoomSplitX, CorrX1, false), (CorrX0, RoomSplitX, false) })
		{
			float s = front ? 1f : -1f;
			Rug(k, new Vector3((x0 + x1) * 0.5f - 0.6f, UpperY + 0.012f, s * (InnerZ - 3.2f)), 1.2f, 0.8f, 0f, (int)(x0 * -1) % 4);
		}
		k.CommitTo(this, "Rugs", false);
	}

	// ------------------------------------------------------------------ the winter gear

	private static StandardMaterial3D _skiRed, _skiWood, _strap, _shoeFrame, _shoeLace;

	private void HangWinterGear()
	{
		_skiRed ??= new StandardMaterial3D { ResourceName = "ski_red", AlbedoColor = new Color(0.52f, 0.1f, 0.08f), Roughness = 0.45f };
		_skiWood ??= (StandardMaterial3D)LodgeTextures.DarkWoodMat.Duplicate();
		_strap ??= new StandardMaterial3D { ResourceName = "ski_strap", AlbedoColor = new Color(0.18f, 0.11f, 0.07f), Roughness = 0.8f };
		_shoeFrame ??= new StandardMaterial3D { ResourceName = "snowshoe_frame", AlbedoColor = new Color(0.55f, 0.4f, 0.24f), Roughness = 0.6f };
		_shoeLace ??= new StandardMaterial3D { ResourceName = "snowshoe_lace", AlbedoColor = new Color(0.62f, 0.54f, 0.4f), Roughness = 0.9f };
		var spots = new List<(Vector3 c, int rays, float y)>
		{
			(new Vector3(0, FloorY, 0), 28, 1.9f),          // the lobby
			(new Vector3(-20f, FloorY, 3.2f), 10, 1.8f),    // the bar
			(new Vector3(8.5f, FloorY, 0), 8, 2.0f),         // the dining hall's near end
		};
		// (none in the corridor upstairs: its walls are the portraits')
		int kind = 0;
		foreach (var (c, rays, y) in spots)
			for (int r = 0; r < rays; r++)
			{
				float a = (r + 0.5f) / rays * Mathf.Tau;
				var dir = rays == 2 ? (r == 0 ? Vector3.Back : Vector3.Forward) : new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
				int what = kind % 4;
				var size = what switch { 0 => new Vector2(0.75f, 1.0f), 1 => new Vector2(0.5f, 0.6f), 2 => new Vector2(0.6f, 1.0f), _ => new Vector2(0.4f, 0.5f) };
				if (WallAt(c + Vector3.Up * y, dir, 16f, size.X, size.Y) is not { } spot) continue;
				var node = new Node3D { Name = $"Gear{WallGear}" };
				AddChild(node);
				node.Position = spot.at + spot.n * 0.07f;
				node.Basis = Basis.LookingAt(-spot.n, Vector3.Up);
				var k = new MeshKit();
				switch (what)
				{
					case 0: CrossedSkis(k); break;
					case 1: Snowshoes(k); break;
					case 2: SkisSideBySide(k); break;
					default: Poles(k); break;
				}
				// (2026-10-15, the scan pass: solid; the clip audit walked through them) a box round it, off the wall
				MeshKit.Solidify(k.CommitTo(node, "Gear", true), Vector3.One * 0.95f);
				_hung.Add((spot.at, spot.n, size.X));
				WallGear++;
				kind++;
			}
	}

	/// <summary>One old wooden ski, flat on the wall along local +Y, its tip curled out at the top.</summary>
	private static void Ski(MeshKit k, Basis b, Vector3 o, float len, Material m)
	{
		k.Mat(m);
		k.Color = Colors.White;
		k.BevelBox(o + b * new Vector3(0, 0, 0), new Vector3(0.075f, len, 0.02f), 0.006f, 1f, b);   // (2026-10-15: its edges eased)
		// the tip, bent away from the wall
		for (int i = 0; i < 4; i++)
		{
			float u = i / 4f, u1 = (i + 1) / 4f;
			Vector3 p0 = b * new Vector3(0, len * 0.5f + u * 0.16f, Mathf.Pow(u, 2f) * 0.1f), p1 = b * new Vector3(0, len * 0.5f + u1 * 0.16f, Mathf.Pow(u1, 2f) * 0.1f);
			k.Box(o + (p0 + p1) * 0.5f, new Vector3(0.075f - u * 0.03f, (p1 - p0).Length() + 0.01f, 0.02f), 1f, b * new Basis(Vector3.Right, -Mathf.Atan2(0.1f * (u1 * u1 - u * u), 0.04f)));
		}
		// the binding: a toe iron and a leather strap
		k.Mat(_strap);
		k.Box(o + b * new Vector3(0, -0.05f, 0.025f), new Vector3(0.09f, 0.12f, 0.03f), 1f, b);
	}

	private static void CrossedSkis(MeshKit k)
	{
		Ski(k, new Basis(Vector3.Back, 0.42f), Vector3.Zero, 1.75f, _skiRed);
		Ski(k, new Basis(Vector3.Back, -0.42f), new Vector3(0, 0, 0.03f), 1.75f, _skiRed);
		k.Mat(_strap);
		k.Box(new Vector3(0, 0, 0.06f), new Vector3(0.12f, 0.12f, 0.03f), 1f);   // the peg they hang from
	}

	private static void SkisSideBySide(MeshKit k)
	{
		Ski(k, Basis.Identity, new Vector3(-0.07f, 0, 0), 1.8f, _skiWood);
		Ski(k, Basis.Identity, new Vector3(0.07f, 0, 0), 1.8f, _skiWood);
		k.Mat(_strap);
		k.Box(new Vector3(0, 0.55f, 0.025f), new Vector3(0.26f, 0.05f, 0.03f), 1f);   // the strap round them both (2026-10-15: on the skis' faces; it stood 15 mm off them)
	}

	private static void Poles(MeshKit k)
	{
		foreach (float x in new[] { -0.08f, 0.08f })
		{
			k.Mat(_skiWood);
			k.Color = Colors.White;
			k.Cylinder(new Vector3(x, -0.6f, 0), new Vector3(x * 0.6f, 0.65f, 0), 0.012f, 0.014f, 6, true);
			k.Mat(_strap);
			k.Cylinder(new Vector3(x * 0.6f, 0.5f, 0), new Vector3(x * 0.6f, 0.68f, 0), 0.02f, 0.02f, 6, true);
			// the basket: a ring of cane near the tip
			for (int i = 0; i < 10; i++)
			{
				float a0 = i / 10f * Mathf.Tau, a1 = (i + 1) / 10f * Mathf.Tau;
				k.Cylinder(new Vector3(x + Mathf.Cos(a0) * 0.06f, -0.48f, Mathf.Sin(a0) * 0.06f), new Vector3(x + Mathf.Cos(a1) * 0.06f, -0.48f, Mathf.Sin(a1) * 0.06f), 0.005f, 0.005f, 4, false);
			}
		}
	}

	private static void Snowshoes(MeshKit k)
	{
		foreach (float x in new[] { -0.17f, 0.17f })
		{
			// the frame: a bent-wood teardrop, the tail pointing down
			k.Mat(_shoeFrame);
			k.Color = Colors.White;
			const int n = 22;
			Vector3 P(int i)
			{
				float a = i / (float)n * Mathf.Tau;
				float rx = 0.15f, ry = 0.28f;
				float tail = Mathf.Max(0f, -Mathf.Sin(a)) * 0.18f;
				return new Vector3(x + Mathf.Cos(a) * rx * (1f - tail * 2.2f), Mathf.Sin(a) * ry - tail, 0);
			}
			for (int i = 0; i < n; i++) k.Cylinder(P(i), P(i + 1), 0.012f, 0.012f, 5, false);
			// the lacing: a lattice of rawhide across it
			k.Mat(_shoeLace);
			for (int j = -3; j <= 3; j++)
			{
				k.Cylinder(new Vector3(x - 0.12f, j * 0.06f, 0), new Vector3(x + 0.12f, j * 0.06f + 0.05f, 0), 0.003f, 0.003f, 3, false);
				k.Cylinder(new Vector3(x - 0.12f, j * 0.06f + 0.05f, 0), new Vector3(x + 0.12f, j * 0.06f, 0), 0.003f, 0.003f, 3, false);
			}
			k.Mat(_strap);
			k.Box(new Vector3(x, 0.02f, 0.015f), new Vector3(0.1f, 0.06f, 0.02f), 1f);
		}
	}

	// ------------------------------------------------------------------ bookshelves and clutter

	private void Bookshelves()
	{
		var k = new MeshKit();
		var roles = Woodwork;
		int n = 0;
		// lobby (under the gallery), the corridor's ends, a shelf in each room
		var spots = new List<(Vector3 c, int rays)> { (new Vector3(0, FloorY + 1.0f, 0), 30) };
		foreach (var (x0, x1, front) in new[] { (RoomSplitX, CorrX1, true), (CorrX0, RoomSplitX, true), (RoomSplitX, CorrX1, false), (CorrX0, RoomSplitX, false) })
			spots.Add((new Vector3((x0 + x1) * 0.5f, UpperY + 1.0f, (front ? 1f : -1f) * (CorrHalf + InnerZ) * 0.5f), 10));
		foreach (var (c, rays) in spots)
		{
			int here = 0;
			for (int r = 0; r < rays && here < (rays > 20 ? 4 : 1); r++)
			{
				float a = (r + 0.3f) / rays * Mathf.Tau;
				var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
				if (WallAt(c, dir, 14f, 0.7f, 0.85f) is not { } spot) continue;
				// the floor in front of it free (nothing standing there)
				var front = spot.at + spot.n * 0.45f;
				var q = new PhysicsShapeQueryParameters3D { Shape = new BoxShape3D { Size = new Vector3(1.3f, 1.6f, 0.4f) }, Transform = new Transform3D(Basis.LookingAt(-spot.n, Vector3.Up), ToGlobal(front with { Y = c.Y + 0.05f })) , CollisionMask = 1 };
				if (GetWorld3D().DirectSpaceState.IntersectShape(q, 1).Count > 0) continue;
				Shelf(k, spot.at with { Y = c.Y - 1.0f }, spot.n, roles);
				LodgeKit.Solid(_inBody, (spot.at + spot.n * 0.18f) with { Y = c.Y - 0.05f }, new Vector3(1.3f, 1.9f, 0.34f), Mathf.Atan2(spot.n.X, spot.n.Z));
				_hung.Add((spot.at, spot.n, 0.7f));
				n++; here++;
			}
		}
		k.CommitTo(this, "Bookshelves", true);
		Clutter += n;
	}

	private void Shelf(MeshKit k, Vector3 floor, Vector3 n, Dictionary<string, Material> roles)
	{
		var b = Basis.LookingAt(-n, Vector3.Up);   // its back to the wall (-Z), its face +Z into the room
		Vector3 P(float x, float y, float z) => floor + b * new Vector3(x, y, z) + n * 0.18f;
		// (2026-10-15, the props pass: the furniture's varnish, the edges eased, a plinth and a cornice; it was six planks)
		k.Mat(LodgeTextures.DoorWoodMat);
		k.Color = Colors.White;
		k.Box(P(0, 0.95f, -0.15f), new Vector3(1.3f, 1.9f, 0.03f), 1f, b);
		k.BevelBox(P(-0.635f, 0.95f, 0), new Vector3(0.03f, 1.9f, 0.32f), 0.006f, 1f, b);
		k.BevelBox(P(0.635f, 0.95f, 0), new Vector3(0.03f, 1.9f, 0.32f), 0.006f, 1f, b);
		for (int s = 0; s < 5; s++) k.BevelBox(P(0, 0.06f + s * 0.45f, 0), new Vector3(1.24f, 0.03f, 0.3f), 0.006f, 1f, b);
		k.BevelBox(P(0, 0.04f, 0.145f), new Vector3(1.24f, 0.08f, 0.02f), 0.005f, 1f, b);   // the plinth's face
		k.BevelBox(P(0, 1.92f, 0.01f), new Vector3(1.36f, 0.04f, 0.35f), 0.01f, 1f, b);   // the cornice
		k.BevelBox(P(0, 1.95f, 0.005f), new Vector3(1.32f, 0.025f, 0.33f), 0.008f, 1f, b);
		float yaw = Mathf.Atan2(n.X, n.Z);
		for (int s = 0; s < 4; s++)
		{
			float y = 0.075f + s * 0.45f;
			// books along it (the rows modelled; a gap, a stack lying flat, a few leaning)
			var row = _orng.Randf() < 0.5f ? "books_row_a" : "books_row_b";
			if (!FurnitureKit.Add(k, row, P(-0.3f, y, -0.02f), yaw + Mathf.Pi * 0.5f, roles)) BookBlocks(k, P(-0.3f, y, 0), b);
			if (_orng.Randf() < 0.6f) FurnitureKit.Add(k, row == "books_row_a" ? "books_row_b" : "books_row_a", P(0.3f, y, -0.02f), yaw + Mathf.Pi * 0.5f, roles);
			else FurnitureKit.Add(k, "books_stack", P(0.35f, y, 0), yaw + 0.3f, roles);
		}
	}

	private void BookBlocks(MeshKit k, Vector3 at, Basis b)
	{
		float x = -0.25f;
		while (x < 0.25f)
		{
			float w = _orng.RandfRange(0.025f, 0.05f), h = _orng.RandfRange(0.22f, 0.34f);
			k.Mat(LodgeTextures.LeatherMat);
			k.Color = new Color(_orng.RandfRange(0.4f, 1f), _orng.RandfRange(0.3f, 0.7f), _orng.RandfRange(0.25f, 0.6f));
			k.Box(at + b * new Vector3(x, h * 0.5f, 0), new Vector3(w, h, 0.2f), 1f, b);
			x += w + 0.004f;
		}
		k.Color = Colors.White;
	}

	private void RoomClutter()
	{
		var k = new MeshKit();
		var roles = Woodwork;
		var space = GetWorld3D().DirectSpaceState;
		string[] onTop = { "books_stack", "magazines", "papers", "mug", "ashtray", "tumbler", "wine_glass", "candlestick", "bottle_wine" };
		string[] onFloor = { "boots", "books_stack", "crate", "magazines", "papers" };
		var areas = new List<(Vector3 min, Vector3 max, int count)>
		{
			(new Vector3(-9f, FloorY, -9f), new Vector3(9f, FloorY, 9f), 26),   // the lobby
			(new Vector3(BarX0 + 0.5f, FloorY, BarZ0 + 0.5f), new Vector3(-13f, FloorY, 7f), 10),   // the bar
		};
		foreach (var (x0, x1, front) in new[] { (RoomSplitX, CorrX1, true), (CorrX0, RoomSplitX, true), (RoomSplitX, CorrX1, false), (CorrX0, RoomSplitX, false) })
		{
			float s = front ? 1f : -1f;
			areas.Add((new Vector3(x0 + 0.5f, UpperY, Mathf.Min(s * (CorrHalf + 0.5f), s * (InnerZ - 0.5f))), new Vector3(x1 - 0.5f, UpperY, Mathf.Max(s * (CorrHalf + 0.5f), s * (InnerZ - 0.5f))), 9));
		}
		SurfaceProbe probe = null;
		int visualTops = 0, onFurniture = 0;
		foreach (var (min, max, count) in areas)
		{
			int placed = 0;
			for (int tries = 0; tries < count * 6 && placed < count; tries++)
			{
				var p = new Vector3(_orng.RandfRange(min.X, max.X), min.Y + 2.4f, _orng.RandfRange(min.Z, max.Z));
				var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(ToGlobal(p), ToGlobal(p + Vector3.Down * 2.6f), 1));
				if (hit.Count == 0) continue;
				var at = ToLocal((Vector3)hit["position"]);
				var nrm = GlobalBasis.Inverse() * (Vector3)hit["normal"];
				if (nrm.Y < 0.9f) continue;
				bool floor = at.Y < min.Y + 0.08f;
				// on the floor, not in a walkway's middle: only near something (a wall or a piece of furniture)
				if (floor)
				{
					bool near = false;
					foreach (var d in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
						if (space.IntersectRay(PhysicsRayQueryParameters3D.Create(ToGlobal(at + Vector3.Up * 0.3f), ToGlobal(at + Vector3.Up * 0.3f + d * 0.7f), 1)).Count > 0) { near = true; break; }
					if (!near) continue;
				}
				// (2026-10-14d) on a piece of furniture: set down on its drawn top, not its collision box's (which runs up to
				// 2 cm past it: the things stood on air)
				if (!floor)
				{
					probe ??= new SurfaceProbe(this);
					onFurniture++;
					if (probe.TopNear((Vector3)hit["position"], 0.25f, 0.01f) is float y) { at = ToLocal(((Vector3)hit["position"]) with { Y = y }); visualTops++; }
					else continue;   // (a collision box with nothing drawn on top: an invisible blocker; not on air)
				}
				string model = floor ? onFloor[_orng.RandiRange(0, onFloor.Length - 1)] : onTop[_orng.RandiRange(0, onTop.Length - 1)];
				if (FurnitureKit.Add(k, model, at + Vector3.Up * 0.002f, _orng.RandfRange(0, Mathf.Tau), roles)) { placed++; Clutter++; }
			}
		}
		k.CommitTo(this, "Clutter", true);
		GD.Print($"[lodge] room clutter: {Clutter} placed, {visualTops} of {onFurniture} tries on furniture set on its drawn top");
	}

	// ------------------------------------------------------------------ the fire's last of it

	private void Smoulder()
	{
		if (_fireLight == null) return;
		var at = _fireLight.Position;   // (the fire light sits out in front of the hearth)
		Vector3 fa = HexVert(1), fb = HexVert(2), fmid = (fa + fb) * 0.5f, fout = fmid.Normalized(), falong = (fb - fa).Normalized();
		Vector3 fc = fmid - fout * (0.4f + 0.75f);
		var ember = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/ember.gdshader") };
		ember.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		var k = new MeshKit();
		k.Mat(ember);
		k.Color = Colors.White;
		// charred logs, half burnt through, settled in the grate; a bed of embers under them
		k.Cylinder(fc - fout * 0.32f + falong * -0.55f + Vector3.Up * 0.36f, fc - fout * 0.3f + falong * 0.5f + Vector3.Up * 0.42f, 0.1f, 0.08f, 9, true);
		k.Cylinder(fc - fout * 0.48f + falong * -0.35f + Vector3.Up * 0.34f, fc - fout * 0.18f + falong * 0.42f + Vector3.Up * 0.52f, 0.085f, 0.07f, 9, true);
		k.Cylinder(fc - fout * 0.25f + falong * 0.05f + Vector3.Up * 0.33f, fc - fout * 0.55f + falong * 0.3f + Vector3.Up * 0.36f, 0.07f, 0.06f, 8, true);
		k.Blob(fc - fout * 0.36f + Vector3.Up * 0.31f, new Vector3(0.62f, 0.06f, 0.3f), 951, 0.35f, false, 1f);
		k.CommitTo(this, "Embers", false);
		// sparks: one now and then going up, drifting
		var sparks = new GpuParticles3D
		{
			Name = "FireSparks", Amount = 6, Lifetime = 2.2, Position = fc - fout * 0.35f + Vector3.Up * 0.45f,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.4f, 0.04f, 0.12f),
				Direction = Vector3.Up, Spread = 12f, InitialVelocityMin = 0.3f, InitialVelocityMax = 0.7f, Gravity = new Vector3(0, 0.05f, 0),
				TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.4f, ScaleMin = 0.5f, ScaleMax = 1f, Color = new Color(1f, 0.55f, 0.18f),
			},
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.02f,
				Material = new StandardMaterial3D { AlbedoTexture = LakeParts.LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles },
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(sparks);
		// a thread of smoke up into the flue
		var smoke = new GpuParticles3D
		{
			Name = "FireSmoke", Amount = 14, Lifetime = 3.5, Position = fc - fout * 0.3f + Vector3.Up * 0.6f,
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = Vector3.Up, Spread = 6f, InitialVelocityMin = 0.15f, InitialVelocityMax = 0.3f, Gravity = new Vector3(0, 0.06f, 0),
				ScaleMin = 1f, ScaleMax = 2.2f, Color = new Color(0.3f, 0.29f, 0.28f, 0.18f), TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.3f,
			},
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.3f,
				Material = new StandardMaterial3D { AlbedoTexture = LakeParts.LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles },
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(smoke);
		_ = at;
	}

	// ------------------------------------------------------------------ Christmas in the dining hall

	private void ChristmasDining()
	{
		var k = new MeshKit();
		var roles = Xmas;
		// a tree in each of its four corners
		foreach (var (x, z) in new[] { (WingX0 + 2.2f, WingHalfZ - 2f), (WingX0 + 2.2f, -WingHalfZ + 2f), (DiningX1 - 2f, WingHalfZ - 2f), (DiningX1 - 2f, -WingHalfZ + 2f) })
		{
			FurnitureKit.Add(k, "xmas_tree", new Vector3(x, FloorY, z), _orng.RandfRange(0, Mathf.Tau), roles, Vector3.One * 0.85f);
			// (2026-10-15, the scan pass: four heaps anywhere in a 2 m square, into each other and the trunk. Round the tree
			// now, under its lowest boughs, each a quarter turn or so from the last; the same draws taken from _orng, so
			// what's dressed after this is where it was)
			float pa = (x + z) * 1.7f;
			for (int i = 0; i < 4; i++)
			{
				float r = 0.78f + 0.22f * _orng.Randf();
				pa += 1.25f + 0.35f * _orng.Randf();
				FurnitureKit.Add(k, i % 2 == 0 ? "presents_a" : "presents_b", new Vector3(x + Mathf.Cos(pa) * r, FloorY + i * 0.002f, z + Mathf.Sin(pa) * r), _orng.RandfRange(0, Mathf.Tau), roles);
			}
			LodgeKit.Solid(_inBody, new Vector3(x, FloorY + 1.2f, z), new Vector3(1.6f, 2.4f, 1.6f));
		}
		// wreaths down both walls, between the windows (found by rays), garlands swagged from one to the next
		var wreaths = new List<(Vector3 at, Vector3 n)>();
		for (float x = WingX0 + 4f; x < DiningX1 - 2f; x += 3.2f)
			foreach (var dir in new[] { Vector3.Back, Vector3.Forward })
				if (WallAt(new Vector3(x, FloorY + 2.9f, 0), dir, 12f, 0.35f, 0.35f) is { } spot)
				{
					FurnitureKit.Add(k, "wreath", spot.at + spot.n * 0.06f, Mathf.Atan2(spot.n.X, spot.n.Z), roles);
					wreaths.Add(spot);
					_hung.Add((spot.at, spot.n, 0.4f));
				}
		k.Mat(Xmas.TryGetValue("needles", out var needles) ? needles : LodgeTextures.DarkWoodMat);
		k.Color = new Color(0.32f, 0.42f, 0.24f);
		for (int i = 0; i + 1 < wreaths.Count; i++)
		{
			var (a, an) = wreaths[i];
			var (b, bn) = wreaths[i + 1];
			if (an.Dot(bn) < 0.9f || a.DistanceTo(b) > 4f) continue;
			Vector3 prev = a + an * 0.08f + Vector3.Up * 0.25f;
			for (int s = 1; s <= 10; s++)
			{
				float u = s / 10f;
				var p = a.Lerp(b, u) + an * 0.08f + Vector3.Up * (0.25f - Mathf.Sin(u * Mathf.Pi) * 0.45f);
				k.Cylinder(prev, p, 0.07f, 0.07f, 6, false);
				prev = p;
			}
		}
		k.Color = Colors.White;
		// strings of coloured lights along the hall, dipping between the chandeliers; a few still lit
		var cols = new[] { new Color(1f, 0.2f, 0.15f), new Color(0.2f, 0.8f, 0.3f), new Color(0.25f, 0.45f, 1f), new Color(1f, 0.75f, 0.2f) };
		int lit = 0;
		foreach (float z in new[] { -WingHalfZ + 1.4f, WingHalfZ - 1.4f })
		{
			Vector3 a = new(WingX0 + 1f, RoomTop - 1.2f, z), b = new(DiningX1 - 1f, RoomTop - 1.2f, z);
			int n = 60;
			Vector3 prev = a;
			for (int i = 0; i <= n; i++)
			{
				float u = i / (float)n;
				var p = a.Lerp(b, u) + Vector3.Down * Mathf.Abs(Mathf.Sin(u * Mathf.Pi * 6f)) * 0.5f;
				if (i > 0) { k.Mat(LodgeTextures.BlackMat); k.Cylinder(prev, p, 0.006f, 0.006f, 3, false); }
				var c = cols[i % cols.Length];
				bool on = _orng.Randf() < 0.18f;
				k.Mat(on ? LodgeTextures.Glow($"xmas_bulb_on_{i % cols.Length}", c, 2.4f) : Glossy($"xmas_bulb_off_{i % cols.Length}", c * 0.35f, 0.3f, 0.1f));
				k.Blob(p + Vector3.Down * 0.035f, new Vector3(0.022f, 0.032f, 0.022f), 3000 + i, 0f, false);
				if (on && lit < 6 && i % 9 == 0) { Light(p + Vector3.Down * 0.1f, c, 0.25f, 2.2f, "XmasLight"); lit++; }
				prev = p;
			}
		}
		// tinsel and baubles dropped about the floor, a few party hats
		// (2026-10-15, the scan pass: they fell anywhere, under the sheeted tables and in the chairs' legs, and lay level
		// with each other on the floor (the clip audit's coplanar pieces). Now only on open floor: clear of the tables and
		// their chairs, the corner trees, the chairs gathered at the end wall; no two within 1.4 m; each a hair over the last)
		var placedXmas = new List<Vector3>();
		var frng = new RandomNumberGenerator { Seed = 8180 };
		bool Clear(Vector3 p)
		{
			for (int t = 0; t < 6; t++)
			{
				var c = TableCentre(t);
				if (Mathf.Abs(p.X - c.X) < TableLen * 0.5f + 0.5f && Mathf.Abs(p.Z - c.Z) < TableW * 0.5f + 1.25f) return false;
			}
			foreach (var (x, z) in new[] { (WingX0 + 2.2f, WingHalfZ - 2f), (WingX0 + 2.2f, -WingHalfZ + 2f), (DiningX1 - 2f, WingHalfZ - 2f), (DiningX1 - 2f, -WingHalfZ + 2f) })
				if (new Vector2(p.X - x, p.Z - z).Length() < 1.6f) return false;
			if (p.X > 13.6f && p.X < 16.2f && p.Z < -InnerZ + 1.8f) return false;   // (the chairs by the end wall: DiningChairs)
			if (p.X > DiningX1 - 3.2f && Mathf.Abs(p.Z - 2.1f) < 1.7f) return false;   // (the snow in at the open window)
			foreach (var q in placedXmas) if (q.DistanceTo(p) < 1.4f) return false;
			return true;
		}
		for (int i = 0; i < 12; i++)
		{
			_orng.Randf(); _orng.Randf(); _orng.Randf();   // (the draws they took)
			for (int tries = 0; tries < 40; tries++)
			{
				var p = new Vector3(frng.RandfRange(WingX0 + 2f, DiningX1 - 2f), FloorY + placedXmas.Count * 0.002f, frng.RandfRange(-InnerZ + 0.5f, InnerZ - 0.5f));
				if (!Clear(p)) continue;
				placedXmas.Add(p);
				FurnitureKit.Add(k, i % 3 == 0 ? "tinsel_floor" : i % 3 == 1 ? "bauble_floor" : "party_hat", p, frng.RandfRange(0, Mathf.Tau), roles);
				break;
			}
		}
		k.CommitTo(this, "DiningChristmas", true);
	}

	// ------------------------------------------------------------------ light

	private void CorridorSconces()
	{
		var k = new MeshKit();
		var shade = LodgeTextures.Glow("lodge_sconce_shade", new Color(0.98f, 0.8f, 0.5f), 1.0f);
		int n = 0;
		foreach (float x in CorridorSconceX)
			foreach (float side in new[] { 1f, -1f })
			{
				var dir = side > 0 ? Vector3.Back : Vector3.Forward;
				if (WallAt(new Vector3(x, UpperY + 2.0f, 0), dir, 3f, 0.15f, 0.2f) is not { } spot) continue;
				var bulb = LodgeKit.Sconce(k, spot.at, spot.n, shade);
				Light(bulb + spot.n * 0.1f, Warm, 0.45f, 3.6f, "Sconce");
				n++;
			}
		k.CommitTo(this, "Sconces", false);
	}

	// ------------------------------------------------------------------ the patio

	private void Patio()
	{
		float z0 = Apothem, z1 = Apothem + 5.2f;
		var k = new MeshKit();
		var body = new StaticBody3D { Name = "PatioBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		foreach (float s in new[] { -1f, 1f })
		{
			float xa = s * 3.7f, xb = s * 8.6f, xc = (xa + xb) * 0.5f, w = Mathf.Abs(xb - xa);
			// the deck, a step up, on its joists
			k.Mat(BuildingTextures.FloorMat);
			k.Color = Colors.White;
			k.Box(new Vector3(xc, 0.08f, (z0 + z1) * 0.5f), new Vector3(w, 0.16f, z1 - z0), 1f);
			body.AddChild(new CollisionShape3D { Position = new Vector3(xc, 0.08f, (z0 + z1) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(w, 0.16f, z1 - z0) } });
			// posts at its outer corners, a beam, a lean-to roof off the lodge's wall, snow heaped on it
			k.Mat(BuildingTextures.RoundLogMat);
			foreach (float z in new[] { z1 - 0.3f, z0 + 2.4f })
			{
				k.Cylinder(new Vector3(xb - s * 0.25f, 0.1f, z), new Vector3(xb - s * 0.25f, 3.3f, z), 0.12f, 0.11f, 8, true);
				body.AddChild(new CollisionShape3D { Position = new Vector3(xb - s * 0.25f, 1.7f, z), Shape = new CylinderShape3D { Radius = 0.13f, Height = 3.2f } });
			}
			k.Cylinder(new Vector3(xa, 3.3f, z1 - 0.3f), new Vector3(xb, 3.3f, z1 - 0.3f), 0.13f, 0.13f, 8, true);
			k.Cylinder(new Vector3(xb - s * 0.25f, 3.3f, z0 + 0.1f), new Vector3(xb - s * 0.25f, 3.3f, z1 - 0.1f), 0.12f, 0.12f, 8, true);
			k.Mat(LodgeTimber);
			k.Quad(new Vector3(xa, 4.2f, z0), new Vector3(xb, 4.2f, z0), new Vector3(xb, 3.45f, z1 + 0.3f), new Vector3(xa, 3.45f, z1 + 0.3f), Vector3.Down);
			k.Mat(RoofSnow);
			k.Quad(new Vector3(xa, 3.45f, z1 + 0.3f), new Vector3(xb, 3.45f, z1 + 0.3f), new Vector3(xb, 4.25f, z0), new Vector3(xa, 4.25f, z0), new Vector3(0, 0.96f, 0.28f).Normalized());
			// a rail round its open sides
			k.Mat(BuildingTextures.RoundLogMat);
			k.Cylinder(new Vector3(xa + s * 0.2f, 0.95f, z1 - 0.3f), new Vector3(xb - s * 0.25f, 0.95f, z1 - 0.3f), 0.05f, 0.05f, 6, true);
			k.Cylinder(new Vector3(xb - s * 0.25f, 0.95f, z0 + 0.1f), new Vector3(xb - s * 0.25f, 0.95f, z1 - 0.3f), 0.05f, 0.05f, 6, true);
			body.AddChild(new CollisionShape3D { Position = new Vector3(xc, 0.6f, z1 - 0.3f), Shape = new BoxShape3D { Size = new Vector3(w, 1f, 0.12f) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(xb - s * 0.25f, 0.6f, (z0 + z1) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(0.12f, 1f, z1 - z0) } });
			// a table and chairs left out, snow on them
			var tc = new Vector3(xc, 0.16f, (z0 + z1) * 0.5f + 0.2f);
			FurnitureKit.Add(k, "bar_table", tc, 0f, Woodwork);
			for (int c = 0; c < 3; c++)
				FurnitureKit.Add(k, "dining_chair", tc + new Basis(Vector3.Up, c * 2.1f) * new Vector3(0, 0, 0.75f), c * 2.1f + Mathf.Pi, Woodwork);
			k.Mat(RoofSnow);
			k.Blob(tc + Vector3.Up * 0.78f, new Vector3(0.35f, 0.05f, 0.35f), 2500 + (int)s, 0.2f, false, 1f);
			body.AddChild(new CollisionShape3D { Position = tc + Vector3.Up * 0.4f, Shape = new CylinderShape3D { Radius = 0.45f, Height = 0.8f } });
		}
		k.CommitTo(this, "Patio", true);
	}
}
