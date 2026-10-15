using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The crawlspace as the lodge's junk space (2026-10-11, the owner: "we need to add more clutter in the crawlspace like
/// more cobwebs, christmas wreathes and holly strands, we need random clutter on the floor like pieces of paper or
/// wrapping paper or bows. jazz up that crawlspace. make it look like people just shoved some shit in their overtime and
/// forgot about it"). Years of Christmases stuffed in here and left:
/// <list type="bullet">
/// <item><b>wreaths</b> hung on nails along the walls, some slipped off and lying on the floor against the boards;</item>
/// <item><b>holly strands</b> stapled along the top of the walls, sagging between the staples, berries on them;</item>
/// <item><b>on the floor</b>, kept to the edges so the way stays clear: crumpled paper and loose sheets, torn wrapping
/// paper in three old patterns, bows come off presents, a run of tinsel, a few small gift boxes never given;</item>
/// <item><b>in the dead ends</b>: cardboard boxes shoved in and stacked, one open, its tinsel spilling over.</item>
/// </list>
/// All of it a few merged meshes (one to a material), none of it in the way.
/// </summary>
public partial class SkiLodge
{
	public int CrawlClutterCount { get; private set; }

	private void CrawlClutter(Node3D maze)
	{
		var rng = new RandomNumberGenerator { Seed = 2525 };
		var path = new HashSet<Vector2I>(CrawlPath);
		var armCells = new HashSet<Vector2I>();
		foreach (var (cell, _) in ArmSpots) armCells.Add(cell);
		Vector2I[] sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

		MeshKit Kit(Material m) { var k = new MeshKit(); k.Mat(m); k.Color = Colors.White; return k; }
		var paper = Kit(new StandardMaterial3D { ResourceName = "crawl_paper", AlbedoColor = new Color(0.78f, 0.75f, 0.66f), Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
		var wraps = new[]
		{
			Kit(Wrap("crawl_wrap_candy", new Color(0.62f, 0.08f, 0.07f), new Color(0.85f, 0.82f, 0.75f), 0)),
			Kit(Wrap("crawl_wrap_holly", new Color(0.08f, 0.3f, 0.12f), new Color(0.75f, 0.6f, 0.2f), 1)),
			Kit(Wrap("crawl_wrap_navy", new Color(0.1f, 0.12f, 0.32f), new Color(0.8f, 0.78f, 0.7f), 2)),
		};
		var ribbon = Kit(new StandardMaterial3D { ResourceName = "crawl_ribbon", AlbedoColor = new Color(0.7f, 0.06f, 0.05f), Roughness = 0.35f, MetallicSpecular = 0.7f });
		var gold = Kit(new StandardMaterial3D { ResourceName = "crawl_gold_ribbon", AlbedoColor = new Color(0.75f, 0.55f, 0.15f), Roughness = 0.3f, Metallic = 0.6f });
		var tinsel = Kit(new StandardMaterial3D { ResourceName = "crawl_tinsel", AlbedoColor = new Color(0.75f, 0.75f, 0.78f), Roughness = 0.2f, Metallic = 0.8f });
		var fir = Kit(new StandardMaterial3D { ResourceName = "crawl_fir", AlbedoColor = new Color(0.06f, 0.16f, 0.07f), Roughness = 0.9f });
		var holly = Kit(new StandardMaterial3D { ResourceName = "crawl_holly", AlbedoColor = new Color(0.05f, 0.2f, 0.07f), Roughness = 0.45f, MetallicSpecular = 0.6f });
		var berry = Kit(new StandardMaterial3D { ResourceName = "crawl_berry", AlbedoColor = new Color(0.6f, 0.03f, 0.03f), Roughness = 0.25f });
		var card = Kit(new StandardMaterial3D { ResourceName = "crawl_cardboard", AlbedoColor = new Color(0.42f, 0.3f, 0.18f), Roughness = 1f });
		int count = 0;

		float Floor(Vector2I at, float lx, float lz) => FloorAt(_maze[at], lx, lz) + CrawlDown.Y;
		Vector3 C(Vector2I at) => CellCentre(at);   // (its floor's height from Floor)

		// ---- the floor along the way: the edges of each cell, never its middle
		for (int k = 3; k < CrawlPath.Count - 2; k++)
		{
			var at = CrawlPath[k];
			if (armCells.Contains(at) || !_maze.TryGetValue(at, out var cell) || cell.Stair) continue;
			if (rng.Randf() > 0.72f) continue;
			var walls = new List<Vector2I>();
			foreach (var sd in sides) if (!Opens(_maze, at, sd)) walls.Add(sd);
			if (walls.Count == 0) continue;
			int n = rng.RandiRange(1, 4);
			for (int i = 0; i < n; i++)
			{
				var side = walls[rng.RandiRange(0, walls.Count - 1)];
				var toWall = new Vector3(side.X, 0, side.Y);
				var along = new Vector3(side.Y, 0, -side.X);
				float lx = side.X * rng.RandfRange(0.28f, 0.42f) + along.X * rng.RandfRange(-0.4f, 0.4f);
				float lz = side.Y * rng.RandfRange(0.28f, 0.42f) + along.Z * rng.RandfRange(-0.4f, 0.4f);
				var p = C(at) + new Vector3(lx, Floor(at, lx, lz) + 0.006f, lz);
				float yaw = rng.RandfRange(0, Mathf.Tau);
				float pick = rng.Randf();
				if (pick < 0.22f) Crumple(paper, p, rng.RandfRange(0.035f, 0.06f), rng);
				else if (pick < 0.4f) Sheet(paper, p, new Vector2(0.21f, 0.28f), yaw, rng);
				else if (pick < 0.62f)
				{
					var w = wraps[rng.RandiRange(0, wraps.Length - 1)];
					if (rng.Randf() < 0.5f) Sheet(w, p, new Vector2(rng.RandfRange(0.18f, 0.4f), rng.RandfRange(0.12f, 0.3f)), yaw, rng, 0.03f);
					else Crumple(w, p, rng.RandfRange(0.05f, 0.085f), rng);
				}
				else if (pick < 0.8f) Bow(rng.Randf() < 0.6f ? ribbon : gold, p + Vector3.Up * 0.018f, rng.RandfRange(0.035f, 0.055f), yaw, rng);
				else if (pick < 0.9f) Gift(wraps[rng.RandiRange(0, wraps.Length - 1)], rng.Randf() < 0.5f ? ribbon : gold, p, rng.RandfRange(0.1f, 0.18f), yaw);
				else Tinsel(tinsel, p, along, rng.RandfRange(0.35f, 0.7f), rng);
				count++;
			}
		}

		// ---- the walls: wreaths on nails, a few slipped off; holly strands along the top
		for (int k = 5; k < CrawlPath.Count - 3; k += rng.RandiRange(3, 6))
		{
			var at = CrawlPath[k];
			if (armCells.Contains(at) || !_maze.TryGetValue(at, out var cell) || cell.Stair) continue;
			var walls = new List<Vector2I>();
			foreach (var sd in sides) if (!Opens(_maze, at, sd)) walls.Add(sd);
			if (walls.Count == 0) continue;
			var side = walls[rng.RandiRange(0, walls.Count - 1)];
			var toWall = new Vector3(side.X, 0, side.Y);
			var along = new Vector3(side.Y, 0, -side.X);
			float y0 = Floor(at, 0, 0);
			if (rng.Randf() < 0.55f)
			{
				bool fallen = rng.Randf() < 0.3f;
				var at3 = C(at) + toWall * (fallen ? 0.36f : 0.42f) + along * rng.RandfRange(-0.25f, 0.25f);
				at3.Y = y0 + (fallen ? 0.17f : rng.RandfRange(1.2f, 1.55f));
				// hung flat on the wall, or slipped off and leaning against its foot
				var normal = (-toWall + (fallen ? Vector3.Up * 0.5f : Vector3.Zero)).Normalized();
				Wreath(fir, berry, ribbon, at3, normal, rng.RandfRange(0.15f, 0.2f), rng);
				count++;
			}
			// a holly strand along the top of this wall, across the cell and a little past it
			if (rng.Randf() < 0.6f)
			{
				var a = C(at) + toWall * 0.43f - along * 0.55f + Vector3.Up * (y0 + CrawlH - 0.16f);
				var b = C(at) + toWall * 0.43f + along * 0.55f + Vector3.Up * (y0 + CrawlH - 0.16f);
				Holly(holly, berry, a, b, rng.RandfRange(0.1f, 0.22f), -toWall, rng);
				count++;
			}
		}

		// ---- the dead ends (cells off the way): boxes shoved in and stacked, one open, its tinsel spilling
		var solid = new MeshKit();
		solid.Mat(new StandardMaterial3D());   // (only its shape is used: the boxes' collision)
		foreach (var (at, cell) in _maze)
		{
			if (path.Contains(at) || cell.Stair || at.X < 2) continue;
			if (rng.Randf() > 0.7f) continue;
			int boxes = rng.RandiRange(1, 3);
			float stackY = Floor(at, 0, 0);
			for (int b = 0; b < boxes; b++)
			{
				var s = new Vector3(rng.RandfRange(0.32f, 0.48f), rng.RandfRange(0.22f, 0.36f), rng.RandfRange(0.28f, 0.42f));
				var off = b == 0 ? new Vector3(rng.RandfRange(-0.18f, 0.18f), 0, rng.RandfRange(-0.18f, 0.18f)) : new Vector3(rng.RandfRange(-0.05f, 0.05f), 0, rng.RandfRange(-0.05f, 0.05f));
				var rot = new Basis(Vector3.Up, rng.RandfRange(-0.4f, 0.4f));
				var centre = C(at) + off + Vector3.Up * (stackY + s.Y * 0.5f);
				card.Box(centre, s, 1f, rot);
				// the tape across its top
				card.Color = new Color(0.8f, 0.72f, 0.55f);
				card.Box(centre + Vector3.Up * (s.Y * 0.5f + 0.002f), new Vector3(s.X * 1.01f, 0.003f, 0.05f), 1f, rot);
				card.Color = Colors.White;
				solid.Box(centre, s * 0.96f, 1f, rot);
				if (b == boxes - 1 && rng.Randf() < 0.35f)
					Tinsel(tinsel, centre + Vector3.Up * s.Y * 0.5f, rot * Vector3.Right, 0.6f, rng, down: stackY);
				stackY += s.Y;
				count++;
			}
		}

		foreach (var (kit, name, shadows) in new[] { (paper, "Paper", false), (wraps[0], "WrapCandy", false), (wraps[1], "WrapHolly", false), (wraps[2], "WrapNavy", false),
			(ribbon, "Bows", false), (gold, "GoldBows", false), (tinsel, "Tinsel", false), (fir, "Wreaths", true), (holly, "Holly", true), (berry, "Berries", false), (card, "Boxes", true) })
			if (!kit.IsEmpty) kit.CommitTo(maze, "Clutter" + name, shadows);
		if (!solid.IsEmpty)
		{
			var body = new StaticBody3D { Name = "ClutterBoxes", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "wood");
			maze.AddChild(body);
			body.AddChild(new CollisionShape3D { Shape = solid.Commit().CreateTrimeshShape() });
		}
		CrawlClutterCount = count;
		GD.Print($"[story] Act 23: the crawlspace's clutter: {count} things shoved in and forgotten");
	}

	/// <summary>Wrapping paper: a small tiled pattern (candy stripes; holly-green with gold dots; navy with stars).</summary>
	private static StandardMaterial3D Wrap(string name, Color a, Color b, int kind)
	{
		var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgb8);
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 32; x++)
			{
				bool on = kind switch
				{
					0 => ((x + y) / 4) % 2 == 0,
					1 => (new Vector2(x % 16 - 8, y % 16 - 8)).Length() < 2.6f,
					_ => (Mathf.Abs(x % 16 - 8) + Mathf.Abs(y % 16 - 8)) < 3,
				};
				img.SetPixel(x, y, on ? b : a);
			}
		return new StandardMaterial3D
		{
			ResourceName = name, AlbedoTexture = ImageTexture.CreateFromImage(img), Roughness = 0.6f, MetallicSpecular = 0.4f,
			Uv1Scale = new Vector3(6f, 6f, 1f), TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	/// <summary>(2026-10-15, the Blender pass) A piece modelled in Blender (tools/Blender/lodge_props.py) into one of these
	/// kits, its roles in the kits' own materials; the kit's material back as it was after (each kit draws in its own).
	/// False if the model isn't there (the code builds it).</summary>
	private static bool Put(MeshKit k, string model, Transform3D xf, params (string role, Material m)[] roles)
	{
		var was = k.CurrentMaterial;
		bool ok = LodgeParts.LodgeProps.Model(k, model, xf, roles);
		if (was != null) k.Mat(was);
		return ok;
	}

	/// <summary>A balled-up sheet: a lumpy, creased little ball, resting on the floor.</summary>
	private static void Crumple(MeshKit k, Vector3 at, float r, RandomNumberGenerator rng)
	{
		int seed = rng.RandiRange(1, 99999);
		var b = new Basis(Vector3.Up, seed * 0.37f) * Basis.FromScale(Vector3.One * r);
		if (Put(k, "lp_paper_ball", new Transform3D(b, at), ("paper", k.CurrentMaterial))) return;
		k.Blob(at + Vector3.Up * r * 0.75f, new Vector3(r, r * 0.8f, r * 1.05f), seed, 0.38f, false, 1f, 0f, 1);
	}

	/// <summary>A loose sheet lying on the floor, a little curled and creased (a grid of 4 x 4, lifted here and there).</summary>
	private static void Sheet(MeshKit k, Vector3 at, Vector2 size, float yaw, RandomNumberGenerator rng, float lift = 0.015f)
	{
		var rot = new Basis(Vector3.Up, yaw);
		if (Put(k, "lp_paper_sheet", new Transform3D(rot * Basis.FromScale(new Vector3(size.X, lift / 0.03f, size.Y)), at), ("paper", k.CurrentMaterial))) return;
		const int n = 4;
		var p = new Vector3[n + 1, n + 1];
		for (int i = 0; i <= n; i++)
			for (int j = 0; j <= n; j++)
			{
				float u = i / (float)n - 0.5f, v = j / (float)n - 0.5f;
				float edge = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) * 2f;   // (curling up toward its edges and corners)
				p[i, j] = at + rot * new Vector3(u * size.X, rng.RandfRange(0f, lift) * (0.3f + edge * edge), v * size.Y);
			}
		for (int i = 0; i < n; i++)
			for (int j = 0; j < n; j++)
				k.Quad(p[i, j], p[i + 1, j], p[i + 1, j + 1], p[i, j + 1], Vector3.Up,
					new Vector2(i, j) / n, new Vector2(i + 1, j) / n, new Vector2(i + 1, j + 1) / n, new Vector2(i, j + 1) / n);
	}

	/// <summary>A bow come off a present: two loops, a knot, two tails.</summary>
	private static void Bow(MeshKit k, Vector3 at, float s, float yaw, RandomNumberGenerator rng)
	{
		var rot = new Basis(Vector3.Up, yaw);
		if (Put(k, "lp_bow", new Transform3D(rot * Basis.FromScale(Vector3.One * s * 1.3f), at), ("ribbon", k.CurrentMaterial))) return;
		var x = rot * Vector3.Right;
		foreach (float side in new[] { -1f, 1f })
		{
			var axis = (rot * new Vector3(0.25f * side, 0.3f, 1f)).Normalized();
			ItemMeshes.Torus(k, at + x * side * s * 0.75f, axis, s * 0.62f, s * 0.16f, 10, 4);
			k.Box(at + x * side * s * 0.45f + rot * new Vector3(0, -s * 0.3f, s * 0.9f), new Vector3(s * 0.32f, s * 0.08f, s * 1.3f), 1f, rot * new Basis(Vector3.Up, side * 0.35f));
		}
		k.Blob(at, Vector3.One * s * 0.28f, rng.RandiRange(1, 9999), 0.1f, false);
	}

	/// <summary>A small present, never given: a wrapped box, a ribbon round both ways, a bow on top.</summary>
	private static void Gift(MeshKit wrap, MeshKit ribbon, Vector3 at, float s, float yaw)
	{
		var rot = new Basis(Vector3.Up, yaw);
		if (Put(wrap, "lp_gift", new Transform3D(rot * Basis.FromScale(Vector3.One * s), at), ("wrap", wrap.CurrentMaterial), ("ribbon", ribbon.CurrentMaterial))) return;
		var size = new Vector3(s, s * 0.7f, s * 0.85f);
		var c = at + Vector3.Up * size.Y * 0.5f;
		wrap.Box(c, size, 1f, rot);
		ribbon.Box(c, new Vector3(size.X * 1.02f, size.Y * 1.02f, s * 0.12f), 1f, rot);
		ribbon.Box(c, new Vector3(s * 0.12f, size.Y * 1.02f, size.Z * 1.02f), 1f, rot);
		ItemMeshes.Torus(ribbon, c + Vector3.Up * size.Y * 0.55f + rot * new Vector3(-s * 0.12f, 0, 0), rot * Vector3.Forward, s * 0.13f, s * 0.035f, 8, 4);
		ItemMeshes.Torus(ribbon, c + Vector3.Up * size.Y * 0.55f + rot * new Vector3(s * 0.12f, 0, 0), rot * Vector3.Forward, s * 0.13f, s * 0.035f, 8, 4);
	}

	/// <summary>A run of tinsel: a thin twist of shreds snaking over the floor (or spilling down a box's side).</summary>
	private static void Tinsel(MeshKit k, Vector3 at, Vector3 dir, float len, RandomNumberGenerator rng, float down = float.NaN)
	{
		var side = dir.Cross(Vector3.Up).Normalized();
		// (on the floor: the modelled run, 1 m along its x; spilling down a box's side, the code's)
		var dn = (dir with { Y = 0f }).Normalized();
		if (float.IsNaN(down) && Put(k, "lp_tinsel", new Transform3D(new Basis(dn * len, Vector3.Up, dn.Cross(Vector3.Up)), at), ("tinsel", k.CurrentMaterial))) return;
		var prev = at;
		for (int i = 1; i <= 8; i++)
		{
			float t = i / 8f;
			var p = at + dir.Normalized() * (t - 0.5f) * len + side * Mathf.Sin(t * 7f + rng.Randf()) * 0.06f;
			if (!float.IsNaN(down)) p.Y = Mathf.Lerp(at.Y, down + 0.01f, Mathf.Clamp(t * 1.4f, 0f, 1f));
			else p.Y = at.Y + 0.01f;
			k.Cylinder(prev, p, 0.012f, 0.012f, 5, false);
			for (int s = 0; s < 3; s++)
			{
				var off = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-0.3f, 1f), rng.RandfRange(-1f, 1f)).Normalized() * 0.025f;
				k.Cylinder(p, p + off, 0.003f, 0.001f, 3, false);
			}
			prev = p;
		}
	}

	/// <summary>A wreath: a ring of fir clumps, a few berries in it, a red bow at its foot; flat to <paramref name="normal"/>.</summary>
	private static void Wreath(MeshKit fir, MeshKit berry, MeshKit ribbon, Vector3 at, Vector3 normal, float r, RandomNumberGenerator rng)
	{
		// (the model: flat to its +Z, its bow at its foot, -Y; that turned as near upright as the wreath lies)
		var wy = Vector3.Up - normal * normal.Dot(Vector3.Up);
		wy = wy.LengthSquared() > 1e-4f ? wy.Normalized() : Vector3.Forward;
		if (Put(fir, "lp_wreath_small", new Transform3D(new Basis(wy.Cross(normal), wy, normal) * Basis.FromScale(Vector3.One * r), at),
			("fir", fir.CurrentMaterial), ("berry", berry.CurrentMaterial), ("ribbon", ribbon.CurrentMaterial))) return;
		var u = normal.Cross(Mathf.Abs(normal.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
		var v = normal.Cross(u).Normalized();
		for (int i = 0; i < 22; i++)
		{
			float a = i / 22f * Mathf.Tau;
			var p = at + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
			fir.Blob(p, new Vector3(r * 0.32f, r * 0.32f, r * 0.32f), rng.RandiRange(1, 99999), 0.45f, false);
			if (i % 4 == 1) berry.Blob(p + normal * r * 0.22f, Vector3.One * r * 0.07f, rng.RandiRange(1, 99999), 0.05f, false);
		}
		// the bow at its foot (or what passes for its foot)
		var foot = at - v * r * 1.05f + normal * r * 0.15f;
		ItemMeshes.Torus(ribbon, foot + u * r * 0.18f, normal, r * 0.2f, r * 0.06f, 8, 4);
		ItemMeshes.Torus(ribbon, foot - u * r * 0.18f, normal, r * 0.2f, r * 0.06f, 8, 4);
		ribbon.Box(foot - v * r * 0.25f + u * r * 0.08f, new Vector3(r * 0.09f, r * 0.45f, r * 0.03f), 1f, Basis.LookingAt(-normal, v));
	}

	/// <summary>A strand of holly stapled up at both ends, sagging between: leaf clumps, red berries every few.</summary>
	private static void Holly(MeshKit leaf, MeshKit berry, Vector3 a, Vector3 b, float sag, Vector3 outward, RandomNumberGenerator rng)
	{
		// (the model: 1 m along its x, sagging 0.16, its leaves out toward +Z; x run from b to a so the frame's right-handed)
		var hx = a - b;
		var oz = (outward - hx.Normalized() * outward.Dot(hx.Normalized())).Normalized();
		if (hx.Normalized().Cross(Vector3.Up).Dot(oz) < 0f) hx = -hx;
		if (Put(leaf, "lp_holly", new Transform3D(new Basis(hx, Vector3.Up * (sag / 0.16f), oz), (a + b) * 0.5f), ("holly", leaf.CurrentMaterial), ("berry", berry.CurrentMaterial)))
			return;
		const int n = 14;
		for (int i = 0; i <= n; i++)
		{
			float t = i / (float)n;
			var p = a.Lerp(b, t) + Vector3.Down * sag * 4f * t * (1f - t) + outward * 0.03f;
			// two spiky leaves at each knot, splayed
			for (int l = 0; l < 2; l++)
			{
				var dir = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(-1f, 1f)).Normalized();
				leaf.Blob(p + dir * 0.03f, new Vector3(0.035f, 0.012f, 0.02f), rng.RandiRange(1, 99999), 0.35f, false);
			}
			if (i % 3 == 1)
				for (int bb = 0; bb < 3; bb++)
					berry.Blob(p + outward * 0.02f + new Vector3(rng.RandfRange(-0.015f, 0.015f), -0.01f - 0.008f * bb, rng.RandfRange(-0.015f, 0.015f)), Vector3.One * 0.009f, rng.RandiRange(1, 99999), 0.05f, false);
		}
	}
}
