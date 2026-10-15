using System.Collections.Generic;
using Godot;
using ProjectDS.World.ChurchParts;

namespace ProjectDS.World;

/// <summary>
/// The vestry's room (2026-10-14g: the interiors kit's demonstration, the first room built with it). It had been a stone
/// box: bare ashlar edge to edge, a plank slab for a wardrobe, an empty floor, lit flat. Now a sexton's room of about
/// 1900 left forty years: limewashed plaster over the stone (<see cref="RoomSurface"/>: streaked from the ceiling, damp
/// risen at the foot, a wet stain under the window), painted beadboard to the chair rail, a skirting, a picture rail, a
/// cornice under the plank ceiling (<see cref="TrimKit"/>); casings round the pointed door and the window, its sill and
/// apron; the beams chamfered; the wardrobe a panelled one; the dark of its corners, its seams, under its furniture and
/// over its candle baked into the vertices (<see cref="RoomShade"/>); stains, cracks, peeling paint and the ghosts of
/// pictures (<see cref="DecalDresser.Room"/>); its floor dressed by rule (<see cref="ClutterKit"/>).
/// </summary>
public partial class Church
{
	private static StandardMaterial3D _vestryPaint, _vestryLime, _vestryCard;

	private void BuildVestryRoom()
	{
		float x0 = VestryX0, x1 = VestryX1, z0 = VestryZ0, z1 = VestryZ1, h = VestryH;
		float tz = (z0 + z1) * 0.5f;
		const float skin = 0.015f;
		// the room's inside: the plaster's faces
		float ix0 = x0 - skin, ix1 = x1 + skin, iz0 = z0 + skin, iz1 = z1 - skin;
		var k = new MeshKit();
		// the lining (limewash, joinery, floor, ceiling) casts no shadow: for a light inside the room it never stands between
		// the light and anything; only the stone round it (so no light leaks out) and the furniture cast
		var lk = new MeshKit();
		var ash = ChurchTextures.AshlarMat;
		// limewash over the stone (paler than the church's own plaster: the stains have something to show on)
		_vestryLime ??= Lime();
		var lime = RoomSurface.Of(_vestryLime, RoomSurface.Plaster with { Streaks = 0.7f, Tide = 0.6f }, 21);
		var floor = RoomSurface.Of(ChurchTextures.OakMat, RoomSurface.Wood, 21);
		_vestryPaint ??= Painted(new Color(0.25f, 0.27f, 0.2f), "ch_vestry_paint");
		var paint = RoomSurface.Of(_vestryPaint, RoomSurface.Wood with { Streaks = 0.35f, Tide = 0.5f, Mottle = 0.4f }, 21);
		var oak = ChurchTextures.OakMat;

		// ---- the shell: drawn finely enough for the bake to sit on
		lk.Tess = 0.6f;
		lk.Mat(floor);
		lk.Color = Colors.White * 0.9f;
		lk.Quad(new Vector3(x1, 0, z0), new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), Vector3.Up, new Vector2(x1, z0), new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1));
		Collide(_wood, new Vector3((x0 + x1) * 0.5f, -0.3f, (z0 + z1) * 0.5f), new Vector3(x0 - x1, 0.6f, z1 - z0));
		lk.Quad(new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, h, z1), new Vector3(x0, h, z1), Vector3.Down, new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
		k.Tess = 0f;   // (the stone behind the limewash: only its outside shows)
		k.Color = Colors.White;
		// the stone (its outside faces are the church's), the window of clear glass in the far wall
		var window = Hole.Lancet(tz - 0.9f, tz + 0.9f, 1.3f, 3.4f, ChurchTextures.QuarryMat, 0.8f);
		Wall(k, ash, new Vector3(x1, 0, 0), Vector3.Back, Vector3.Right, 0.9f, z0, z1, 0f, _ => h, new List<Hole> { window });
		Wall(k, ash, new Vector3(0, 0, z0), Vector3.Left, Vector3.Back, 0.9f, -x0, -x1 + 0.9f, 0f, _ => h, null);
		Wall(k, ash, new Vector3(0, 0, z1), Vector3.Left, Vector3.Forward, 0.9f, -x0, -x1 + 0.9f, 0f, _ => h, null);
		Collide(_stone, new Vector3(x1 - 0.45f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.9f, h, z1 - z0 + 1.8f));
		Collide(_stone, new Vector3((x0 + x1) * 0.5f, h * 0.5f, z0 - 0.45f), new Vector3(x0 - x1, h, 0.9f));
		Collide(_stone, new Vector3((x0 + x1) * 0.5f, h * 0.5f, z1 + 0.45f), new Vector3(x0 - x1, h, 0.9f));
		// the limewash: a skin on all four inner faces, open at the window and the door (the transept's wall is the
		// fourth)
		var winSkin = Hole.Lancet(tz - 0.9f, tz + 0.9f, 1.3f, 3.4f, null, 0.8f);
		var doorSkin = Hole.Lancet(tz - 1.2f, tz + 1.2f, 0f, 2.7f, null, 0.7f);
		lk.Tess = 0.6f;
		Wall(lk, lime, new Vector3(ix1, 0, 0), Vector3.Back, Vector3.Right, skin, iz0, iz1, 0f, _ => h, new List<Hole> { winSkin });
		Wall(lk, lime, new Vector3(ix0, 0, 0), Vector3.Back, Vector3.Left, skin, iz0, iz1, 0f, _ => h, new List<Hole> { doorSkin });
		Wall(lk, lime, new Vector3(0, 0, iz0), Vector3.Left, Vector3.Back, skin, -ix0, -ix1, 0f, _ => h, null);
		Wall(lk, lime, new Vector3(0, 0, iz1), Vector3.Left, Vector3.Forward, skin, -ix0, -ix1, 0f, _ => h, null);
		lk.Tess = 0f;

		// ---- the joinery
		var corners = new[] { new Vector3(ix0, 0, iz0), new Vector3(ix1, 0, iz0), new Vector3(ix1, 0, iz1), new Vector3(ix0, 0, iz1) };
		var inside = new Vector3((ix0 + ix1) * 0.5f, 0, tz);
		var door = (new Vector3(ix0, 0, tz), 2.4f + 0.2f);
		// painted beadboard to the chair rail, a skirting in front of it (the door's gap left)
		lk.Mat(paint);
		float wFace = 0f;
		foreach (var (run, closed) in TrimKit.Perimeter(corners, new[] { door }))
			wFace = TrimKit.Wainscot(lk, run, 1.02f, true, closed, inside);
		lk.Mat(oak);
		lk.Color = Colors.White * 0.7f;
		foreach (var (run, closed) in TrimKit.Perimeter(corners, new[] { door }))
			TrimKit.Loop(lk, run, TrimKit.SkirtingTall.Out(wFace), closed, null, inside);
		// a picture rail (round the window's casing and the door's), and the cornice under the ceiling
		var up = Vector3.Up * 2.35f;
		var railCorners = System.Array.ConvertAll(corners, c => c + up);
		foreach (var (run, closed) in TrimKit.Perimeter(railCorners, new[] { door, (new Vector3(ix1, 2.35f, tz), 1.8f + 0.2f) }))
			TrimKit.Loop(lk, run, TrimKit.PictureRail, closed, null, inside + up);
		lk.Mat(lime);
		lk.Color = Colors.White;
		var top = System.Array.ConvertAll(corners, c => c + Vector3.Up * h);
		TrimKit.Loop(lk, top, TrimKit.Cornice.Scaled(1.3f, 1.3f), true, Vector3.Down, inside + Vector3.Up * h);
		// the casings: the pointed door's (on the vestry's face) and the window's, its sill and apron
		lk.Mat(oak);
		lk.Color = Colors.White * 0.75f;
		TrimKit.Casing(lk, TrimKit.ArchOutline(new Vector3(ix0 - 0.002f, 0, tz), Vector3.Back, 2.4f, 2.7f, 0.7f, 8), Vector3.Left);
		TrimKit.Casing(lk, TrimKit.ArchOutline(new Vector3(ix1 + 0.002f, 1.3f, tz), Vector3.Back, 1.8f, 3.4f - 1.3f, 0.8f, 7), Vector3.Right);
		TrimKit.Sill(lk, new Vector3(ix1, 1.3f, tz - 0.9f), new Vector3(ix1, 1.3f, tz + 0.9f), Vector3.Right, 0.9f * 0.5f + skin - 0.02f);
		// the beams, chamfered, sitting on the cornice
		lk.Color = Colors.White * 0.85f;
		for (float x = x1 + 1f; x < x0; x += 1.6f)
			TrimKit.Beam(lk, new Vector3(x, h - 0.13f, iz0 + 0.002f), new Vector3(x, h - 0.13f, iz1 - 0.002f), 0.2f, 0.26f, 0.035f);

		// ---- the furniture: a panelled wardrobe for the vestments, the long table, the hook board
		k.Color = Colors.White;
		var wardrobe = new Vector3(ix1 + 3.5f, 0, iz1 - 0.34f);
		Wardrobe(k, wardrobe, 2.2f, 2.45f, 0.66f);
		Collide(_wood, wardrobe + new Vector3(0, 1.225f, 0), new Vector3(2.2f, 2.45f, 0.66f));
		var table = new Vector3(x1 + 1.3f, 0, tz);
		k.Mat(oak);
		k.BevelBox(table + new Vector3(0, 0.78f, 0), new Vector3(0.9f, 0.06f, 3.2f), 0.012f);
		k.BevelBox(table + new Vector3(0, 0.71f, 0), new Vector3(0.78f, 0.1f, 3.05f), 0.006f);   // (the apron rail)
		foreach (float dz in new[] { -1.4f, 1.4f }) foreach (float dx in new[] { -0.35f, 0.35f })
				k.ChamferBox(table + new Vector3(dx, 0.38f, dz), new Vector3(0.07f, 0.76f, 0.07f), 0.01f);
		Collide(_wood, table + new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.8f, 3.2f));
		k.BevelBox(new Vector3(x1 + 5.2f, 1.6f, iz0 + 0.03f), new Vector3(1.6f, 0.3f, 0.04f), 0.012f);
		k.Mat(ChurchTextures.IronMat);
		for (int i = 0; i < 4; i++) k.Cylinder(new Vector3(x1 + 4.6f + i * 0.4f, 1.62f, iz0 + 0.05f), new Vector3(x1 + 4.6f + i * 0.4f, 1.6f, iz0 + 0.17f), 0.012f, 0.012f, 5, false);
		var vroles = new Dictionary<string, Material>
		{
			["wood"] = oak, ["leather"] = new StandardMaterial3D { ResourceName = "ch_binding", AlbedoColor = new Color(0.16f, 0.06f, 0.05f), Roughness = 0.7f },
			["paper"] = new StandardMaterial3D { ResourceName = "ch_pages", AlbedoColor = new Color(0.6f, 0.56f, 0.45f), Roughness = 0.95f },
			["metal"] = ChurchTextures.GoldMat, ["iron"] = ChurchTextures.IronMat, ["upholstery"] = new StandardMaterial3D { ResourceName = "ch_seat", AlbedoColor = new Color(0.2f, 0.12f, 0.09f), Roughness = 0.9f },
			["glass"] = new StandardMaterial3D { ResourceName = "ch_bottle", AlbedoColor = new Color(0.12f, 0.14f, 0.1f), Roughness = 0.25f },
			["liquid"] = new StandardMaterial3D { ResourceName = "ch_wine", AlbedoColor = new Color(0.12f, 0.02f, 0.03f), Roughness = 0.2f },
			["label"] = new StandardMaterial3D { ResourceName = "ch_label", AlbedoColor = new Color(0.5f, 0.45f, 0.35f), Roughness = 0.9f },
		};
		vroles["*"] = oak;
		// on the long table: the registers stacked, an open hymnal's loose pages, a row of service books
		FurnitureKit.Add(k, "books_stack", table + new Vector3(0, 0.81f, -1.1f), 0.3f, vroles);
		FurnitureKit.Add(k, "papers", table + new Vector3(-0.05f, 0.81f, 0.4f), 1.2f, vroles);
		FurnitureKit.Add(k, "books_row_a", table + new Vector3(0.12f, 0.81f, 1.5f), Mathf.Pi * 0.5f, vroles);
		// a chair pulled out from the table, left as whoever got up left it
		FurnitureKit.Add(k, "dining_chair", table + new Vector3(0.95f, 0, -0.3f), -Mathf.Pi * 0.5f + 0.35f, vroles);
		Collide(_wood, table + new Vector3(0.95f, 0.45f, -0.3f), new Vector3(0.5f, 0.9f, 0.5f));

		// ---- the bake: the dark in the room's seams and corners, under the wardrobe and the table, soot over the
		// candle, damp under the window and down the leaking corner, the boards worn from the door to the table and the hooks
		var candle = new Vector3(x1 + 1.3f, 0.93f, tz - 1f);
		var shade = new RoomShade(ix1, ix0, iz0, iz1, 0f, h) { Dim = 0.72f };
		shade.Under(new Aabb(wardrobe - new Vector3(1.1f, 0, 0.33f), new Vector3(2.2f, 2.45f, 0.66f)), 0.55f, 0.6f)
			.Under(new Aabb(table - new Vector3(0.45f, 0, 1.6f), new Vector3(0.9f, 0.8f, 3.2f)), 0.4f, 0.4f)
			.Under(new Aabb(new Vector3(x1 + 4.4f, 1.45f, iz0), new Vector3(1.6f, 0.3f, 0.06f)), 0.35f, 0.25f)
			.Soot(candle, 0.8f, 0.45f)
			.Light(candle, 4.2f).Light(new Vector3(ix1 + 0.5f, 2.2f, tz), 3.4f, 0.8f)
			.Damp(new Vector3(ix1, 1.1f, tz), 1.5f, 0.85f).Damp(new Vector3(ix1, h, iz1), 1.8f, 0.7f)
			.Path(new Vector3(ix0, 0, tz), table + new Vector3(0.7f, 0, 0), 1.0f)
			.Path(new Vector3(ix0, 0, tz), new Vector3(x1 + 5.3f, 0, iz0 + 0.6f), 0.8f, 0.5f);

		// ---- the floor dressed by rule: candle boxes and a crate stacked, bundled hymnals, old frames and a board leant
		// on the walls, the broom and the pail in a corner, a chair knocked over; nothing in the door's way, the window's,
		// or the path to the key. The small things go in a mesh of their own that casts no shadow (a dozen shadow passes
		// for a carton's worth of shade: the bake's dark under each one does that job), all in one material told apart by
		// their vertex colour, as the PS2 would; the chairs and the crates stay in the room's mesh and cast theirs.
		var small = new MeshKit();
		var clutter = new ClutterKit(new Aabb(new Vector3(ix1, 0, iz0), new Vector3(ix0 - ix1, h, iz1 - iz0)), 2121, shade, _wood);
		clutter.Keep(new Aabb(wardrobe - new Vector3(1.15f, 0, 0.4f), new Vector3(2.3f, 2.5f, 0.8f)))
			.Keep(new Aabb(table - new Vector3(0.5f, 0, 1.65f), new Vector3(1.6f, 0.9f, 3.3f)))
			.Keep(new Vector3(ix0 - 0.9f, 0, tz), 1.6f)                       // the door's swing
			.Keep(new Vector3(x1 + 5.3f, 0, iz0 + 0.5f), 0.9f)               // under the key
			.Keep(new Vector3(x1 + 6.6f, 0, iz0 + 0.35f), 0.5f)              // the alb
			.Keep(new Vector3((ix0 + x1 + 2.2f) * 0.5f, 0, tz), 0.7f);       // the way across
		_vestryCard ??= new StandardMaterial3D { ResourceName = "ch_clutter", AlbedoColor = new Color(0.36f, 0.29f, 0.2f), Roughness = 0.95f, VertexColorUseAsAlbedo = true };
		var card = _vestryCard;
		var crate = ClutterKit.Model("crate", vroles, canStackOn: true);
		var chair = ClutterKit.Model("dining_chair", vroles);
		var carton = ClutterKit.Carton(card, new Vector3(0.42f, 0.3f, 0.32f));
		var cartonSmall = ClutterKit.Carton(card, new Vector3(0.3f, 0.2f, 0.25f), new Color(0.85f, 0.8f, 0.75f));
		var hymnals = ClutterKit.Bundle(card, card, new Vector3(0.24f, 0.16f, 0.17f), new Color(0.55f, 0.22f, 0.2f));
		var registers = ClutterKit.Bundle(card, card, new Vector3(0.34f, 0.1f, 0.26f), new Color(0.3f, 0.3f, 0.32f));
		// the corners first (the broom and the pail together), then the walls
		clutter.InCorner(small, ClutterKit.Bucket(ChurchTextures.IronMat), 3);
		clutter.Lean(small, ClutterKit.Broom(oak, card), 2);
		var c1 = clutter.InCorner(k, crate, 0);
		clutter.On(small, c1, carton);
		var c2 = clutter.AgainstWall(k, crate, 2);
		clutter.On(small, c2, hymnals);
		clutter.Lean(small, ClutterKit.Frame(oak, card, 0.55f, 0.72f), 3);
		clutter.Lean(small, ClutterKit.Frame(oak, card, 0.42f, 0.55f), 3);
		clutter.Lean(small, ClutterKit.Board(oak, 1.7f), 0);
		clutter.AgainstWall(k, chair, 3);
		clutter.Scatter(small, new[] { carton, cartonSmall, hymnals, registers, carton }, 16, 0.75f, new[] { cartonSmall, hymnals, registers });
		// and one chair knocked over, a carton spilled
		clutter.Topple(k, chair);
		clutter.Topple(small, cartonSmall);
		GD.Print($"[vestry] {clutter.All.Count} pieces of clutter");

		// ---- baked, committed
		shade.Apply(k);
		shade.Apply(lk);
		shade.Apply(small);
		k.CommitTo(this, "Vestry", true);
		lk.CommitTo(this, "VestryLining", false);
		small.CommitTo(this, "VestryClutter", false);

		// ---- the decals by rule
		var dress = new Node3D { Name = "VestryDress" };
		AddChild(dress);
		DecalDresser.Room(dress, new DecalDresser.RoomSpec
		{
			Box = new Aabb(new Vector3(ix1, 0, iz0), new Vector3(ix0 - ix1, h, iz1 - iz0)),
			Seed = 21,
			Windows = { (new Vector3(ix1, 1.3f, tz), Vector3.Right, 1.8f, 2.9f) },
			Doors = { (new Vector3(ix0, 0, tz), Vector3.Left, 2.4f, 3.9f) },
			Flames = { candle },
			Keep = { (new Vector3(x1 + 5.2f, 1.6f, iz0), 0.9f), (wardrobe + new Vector3(0, 1.2f, 0), 1.3f) },
			Amount = 1.1f,
		});
	}

	/// <summary>A panelled wardrobe standing at <paramref name="foot"/> (its back to +Z): a carcass with eased edges on
	/// a plinth, two panelled doors, a moulded cornice, a pair of knobs.</summary>
	private static void Wardrobe(MeshKit k, Vector3 foot, float w, float h, float d)
	{
		var oak = ChurchTextures.OakMat;
		// (2026-10-15) modelled in Blender at the vestry's size (tools/Blender/fixtures.py fx_vestry_wardrobe: 2.2 x 2.45 x
		// 0.66, its doors to -Z; another size is scaled to it); the code below is its fallback
		if (FurnitureKit.Add(k, "fx_vestry_wardrobe", new Transform3D(Basis.FromScale(new Vector3(w / 2.2f, h / 2.45f, d / 0.66f)), foot), new System.Collections.Generic.Dictionary<string, Material>()
		{
			["oak"] = FixtureModels.Tint(oak, k.Color), ["door"] = FixtureModels.Tint(oak, k.Color * 0.92f), ["iron"] = ChurchTextures.IronMat,
		}))
		{
			k.Mat(oak);
			return;
		}
		k.Mat(oak);
		var col = k.Color;
		k.BevelBox(foot + new Vector3(0, 0.06f, 0.01f), new Vector3(w - 0.04f, 0.12f, d - 0.06f), 0.01f);             // the plinth
		k.BevelBox(foot + new Vector3(0, 0.12f + (h - 0.24f) * 0.5f, 0), new Vector3(w, h - 0.24f, d), 0.02f);      // the carcass
		// the cornice: a cap stepping out over the doors
		k.BevelBox(foot + new Vector3(0, h - 0.09f, -0.01f), new Vector3(w + 0.06f, 0.06f, d + 0.04f), 0.012f);
		k.BevelBox(foot + new Vector3(0, h - 0.03f, -0.02f), new Vector3(w + 0.12f, 0.06f, d + 0.08f), 0.015f);
		// the doors, a hair proud of its front (they face -Z)
		float dw = (w - 0.1f) * 0.5f, dh = h - 0.24f - 0.12f;
		k.Color = col * 0.92f;
		foreach (float s in new[] { -1f, 1f })
			StationKit.PanelDoor(k, foot + new Vector3(s * (dw * 0.5f + 0.01f), 0.12f + 0.06f + dh * 0.5f, -d * 0.5f - 0.012f), dw, dh, 0.03f, Basis.Identity, 1);
		k.Color = col;
		k.Mat(ChurchTextures.IronMat);
		foreach (float s in new[] { -1f, 1f })
			k.Cylinder(foot + new Vector3(s * 0.06f, 1.2f, -d * 0.5f - 0.03f), foot + new Vector3(s * 0.06f, 1.2f, -d * 0.5f - 0.06f), 0.018f, 0.018f, 6, true);
		k.Mat(oak);
	}

	/// <summary>The vestry's limewash: the plain plaster scan (the church's own plaster carries a loud cloudy pattern that
	/// drowned the stains), a lime-white gone grey-brown, projected in world space so it runs on across the walls.</summary>
	private static StandardMaterial3D Lime()
	{
		var m = new StandardMaterial3D
		{
			ResourceName = "ch_vestry_lime", VertexColorUseAsAlbedo = true, Roughness = 0.95f, MetallicSpecular = 0.25f,
			Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1TriplanarSharpness = 4f, Uv1Scale = Vector3.One / 2.4f,
		};
		SurfaceSets.Apply(m, "plaster", 0.7f);
		m.AlbedoColor = new Color(0.62f, 0.58f, 0.5f);
		DetailKit.Apply(m, DetailKit.Kind.Plaster);
		return m;
	}

	/// <summary>Old gloss paint on wood (the beadboard): a flat colour, the wood's grain under it.</summary>
	private static StandardMaterial3D Painted(Color c, string name)
	{
		var m = new StandardMaterial3D { ResourceName = name, AlbedoColor = c, Roughness = 0.62f, MetallicSpecular = 0.4f, VertexColorUseAsAlbedo = true, Uv1Scale = new Vector3(1.4f, 1.4f, 1f) };
		DetailKit.Apply(m, DetailKit.Kind.Wood);
		return m;
	}
}
