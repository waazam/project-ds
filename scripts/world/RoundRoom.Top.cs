using System.Collections.Generic;
using Godot;
using ProjectDS.World.ChurchParts;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// The little room at the top of the shaft, where Act 21 starts (2026-10-14h, the interiors pass: "one flat brown
/// material on walls, ceiling and stair, at one mid value, lit evenly"). Now a workings' head room left fifty years: its
/// stone weathered and streaked (<see cref="RoomSurface"/>), standing on a plinth course of bigger blocks, damp at the
/// foot; a flagged floor of its own, worn; a ceiling of old boards on heavy timber joists; the arch to the long stair
/// framed in timber shoring; a lamp hung on its flex over the shaft; and what was left: crates and a sack, a coil of
/// rope, a tipped chair, a lantern on a hook, papers, the dark baked into its foot, its seams and its ceiling
/// (<see cref="RoomShade"/>). Nothing near the shaft's edge, the arch or the way between.
/// </summary>
public partial class RoundRoom
{
	private void BuildTopShell(float y0)
	{
		var rng = new RandomNumberGenerator { Seed = 2121 };
		var k = new MeshKit();    // the outer stone (the shadow casters)
		var lk = new MeshKit();   // the lining, floor, ceiling: no shadows
		var small = new MeshKit();
		var stone = RoomSurface.Of(TopStone, RoomSurface.Stone with { Streaks = 0.6f, Tide = 0.6f }, 23);
		var plinth = RoomSurface.Of(WallConcreteDark, RoomSurface.Stone with { Tide = 0.7f }, 23);
		var flags = RoomSurface.Of(FloorStone, RoomSurface.Stone with { FloorDirt = 0.65f, Wear = 0.7f }, 23);
		var boards = ChurchTextures.OldPlankMat;
		var timber = PropTextures.PostMat;
		const int n = 28;
		float segW = Mathf.Tau * (TopR + 0.3f) / n + 0.05f;
		bool Way(float a) => Mathf.Abs(Mathf.AngleDifference(a, 0f)) < 0.3f;   // a wide arch on +Z: the way on, up the long stair
		// ---- the walls: the stone ring (collision as before), its inner face tessellated for the bake
		k.Mat(stone);
		k.Color = Colors.White;
		lk.Tess = 0.6f;
		lk.Mat(stone);
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n;
			Vector3 dir = new(Mathf.Sin(a), 0, Mathf.Cos(a));
			if (Way(a)) { Seg(k, dir * (TopR + 0.3f) + Vector3.Up * (y0 + (2.4f + TopH + 0.2f) * 0.5f), new Vector3(segW, TopH + 0.2f - 2.4f, 0.6f), a, true); continue; }
			Seg(k, dir * (TopR + 0.3f) + Vector3.Up * (y0 + TopH * 0.5f), new Vector3(segW, TopH + 0.4f, 0.6f), a, true);
			// its face into the room, a block's shade of its own
			float a0 = Mathf.Tau * (i - 0.5f) / n, a1 = Mathf.Tau * (i + 0.5f) / n;
			Vector3 d0 = new(Mathf.Sin(a0), 0, Mathf.Cos(a0)), d1 = new(Mathf.Sin(a1), 0, Mathf.Cos(a1));
			float r = TopR - 0.005f;
			lk.Color = Colors.White * rng.RandfRange(0.8f, 1f);
			lk.Quad(d0 * r + Vector3.Up * y0, d1 * r + Vector3.Up * y0, d1 * r + Vector3.Up * (y0 + TopH), d0 * r + Vector3.Up * (y0 + TopH), -dir,
				new Vector2(a0 * r, TopH), new Vector2(a1 * r, TopH), new Vector2(a1 * r, 0), new Vector2(a0 * r, 0));
		}
		lk.Tess = 0f;
		// the stone cap over it all (it keeps the lamp's light in); the boards and joists hang under it
		k.Color = Colors.White * 0.6f;
		k.Cylinder(new Vector3(0, y0 + TopH, 0), new Vector3(0, y0 + TopH + 0.3f, 0), TopR + 0.6f, TopR + 0.6f, 32, true);
		// ---- a plinth course of bigger, darker blocks round the foot, and a string course under the ceiling
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n;
			if (Way(a)) continue;
			lk.Mat(plinth);
			lk.Color = Colors.White * rng.RandfRange(0.7f, 0.95f);
			lk.ChamferBox(new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (TopR - 0.07f) + Vector3.Up * (y0 + 0.22f), new Vector3(segW - 0.02f, 0.44f, 0.16f), 0.02f, 1f, new Basis(Vector3.Up, a), 1, false, true);
			lk.Mat(stone);
			lk.Color = Colors.White * 0.75f;
			lk.ChamferBox(new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (TopR - 0.05f) + Vector3.Up * (y0 + TopH - 0.42f), new Vector3(segW - 0.01f, 0.12f, 0.12f), 0.02f, 1f, new Basis(Vector3.Up, a), 1);
		}
		// ---- the floor: flags round the shaft, worn
		lk.Mat(flags);
		lk.Tess = 0.6f;
		for (int i = 0; i < 24; i++)
		{
			float a0 = Mathf.Tau * i / 24f, a1 = Mathf.Tau * (i + 1) / 24f;
			Vector3 d0 = new(Mathf.Sin(a0), 0, Mathf.Cos(a0)), d1 = new(Mathf.Sin(a1), 0, Mathf.Cos(a1));
			Vector3 y = Vector3.Up * y0;
			foreach (var (r0, r1) in new[] { (DaisR + 0.02f, 3.2f), (3.2f, TopR + 0.4f) })
			{
				lk.Color = new Color(0.82f, 0.79f, 0.73f) * rng.RandfRange(0.75f, 1f);
				lk.Quad(y + d0 * r0, y + d0 * r1, y + d1 * r1, y + d1 * r0, Vector3.Up, new Vector2(d0.X * r0, d0.Z * r0), new Vector2(d0.X * r1, d0.Z * r1), new Vector2(d1.X * r1, d1.Z * r1), new Vector2(d1.X * r0, d1.Z * r0));
			}
		}
		lk.Tess = 0f;
		// the joints between the flags (dark lines, a hair proud)
		small.Mat(new StandardMaterial3D { ResourceName = "top_joints", AlbedoColor = new Color(0.12f, 0.11f, 0.1f), Roughness = 1f });
		for (int i = 0; i < 24; i++)
		{
			float a = Mathf.Tau * i / 24f;
			Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
			small.Beam(d * (DaisR + 0.05f) + Vector3.Up * (y0 + 0.002f), d * (TopR - 0.1f) + Vector3.Up * (y0 + 0.002f), 0.014f, 0.002f);
		}
		for (int i = 0; i < 64; i++)
		{
			float a0 = Mathf.Tau * i / 64f, a1 = Mathf.Tau * (i + 1) / 64f;
			small.Beam(new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * 3.2f + Vector3.Up * (y0 + 0.002f), new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * 3.2f + Vector3.Up * (y0 + 0.002f), 0.014f, 0.002f);
		}
		// ---- the ceiling: old boards on four heavy joists, a beam across them
		float cy = y0 + TopH;
		lk.Mat(boards);
		lk.Tess = 0.6f;
		lk.Color = Colors.White * 0.7f;
		float R = TopR + 0.05f;
		lk.Quad(new Vector3(-R, cy - 0.01f, -R), new Vector3(R, cy - 0.01f, -R), new Vector3(R, cy - 0.01f, R), new Vector3(-R, cy - 0.01f, R), Vector3.Down,
			new Vector2(-R, -R), new Vector2(R, -R), new Vector2(R, R), new Vector2(-R, R));
		lk.Tess = 0f;
		lk.Mat(timber);
		foreach (float x in new[] { -3.0f, -1.0f, 1.0f, 3.0f })
		{
			float half = Mathf.Sqrt(Mathf.Max(0.1f, TopR * TopR - x * x));
			lk.Color = Colors.White * rng.RandfRange(0.55f, 0.75f);
			TrimKit.Beam(lk, new Vector3(x, cy - 0.12f, -half), new Vector3(x, cy - 0.12f, half), 0.2f, 0.24f, 0.025f);
		}
		lk.Color = Colors.White * 0.6f;
		TrimKit.Beam(lk, new Vector3(-TopR + 0.05f, cy - 0.37f, -0.4f), new Vector3(TopR - 0.05f, cy - 0.37f, -0.4f), 0.26f, 0.26f, 0.03f);
		// ---- the arch to the stair, shored with timber: two posts and a lintel, a sill board over the threshold
		lk.Color = Colors.White * 0.7f;
		foreach (float s in new[] { -1f, 1f })
			lk.ChamferBox(new Vector3(s * 1.08f, y0 + 1.25f, TopR - 0.02f), new Vector3(0.18f, 2.5f, 0.2f), 0.02f);
		lk.ChamferBox(new Vector3(0, y0 + 2.5f, TopR - 0.02f), new Vector3(2.5f, 0.22f, 0.24f), 0.02f, 1f, null, 0);
		lk.Color = Colors.White * 0.55f;
		lk.ChamferBox(new Vector3(0, y0 + 0.02f, TopR + 0.12f), new Vector3(2.0f, 0.04f, 0.34f), 0.01f, 1f, null, 0);
		// ---- the lamp over the shaft: its flex from the beam, a tin shade, the bulb
		var lampAt = new Vector3(0, y0 + 2.8f, 0);
		small.Mat(StairwellTextures.RailMat);
		small.Color = Colors.White * 0.4f;
		small.Cylinder(new Vector3(0, cy - 0.01f, 0), lampAt + Vector3.Up * 0.12f, 0.006f, 0.006f, 4, false);
		small.Color = Colors.White * 0.7f;
		small.Cylinder(lampAt + Vector3.Up * 0.12f, lampAt + Vector3.Up * 0.04f, 0.03f, 0.03f, 8, true);
		small.Cylinder(lampAt + Vector3.Up * 0.06f, lampAt - Vector3.Up * 0.04f, 0.05f, 0.2f, 12, false);
		AddChild(new MeshInstance3D
		{
			Name = "TopBulb", Mesh = new SphereMesh { Radius = 0.035f, Height = 0.08f, RadialSegments = 8, Rings = 4 }, Position = lampAt,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.6f), EmissionEnabled = true, Emission = new Color(1f, 0.78f, 0.5f), EmissionEnergyMultiplier = 2.5f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		// ---- what was left (by the wall, round from the arch; nothing in the way from the shaft to the arch)
		var roles = new Dictionary<string, Material>
		{
			["wood"] = timber, ["paper"] = new StandardMaterial3D { ResourceName = "top_paper", AlbedoColor = new Color(0.5f, 0.46f, 0.37f), Roughness = 0.95f },
			["metal"] = StairwellTextures.RailMat, ["iron"] = StairwellTextures.RailMat, ["label"] = new StandardMaterial3D { ResourceName = "top_label", AlbedoColor = new Color(0.42f, 0.38f, 0.3f), Roughness = 0.9f },
			["upholstery"] = new StandardMaterial3D { ResourceName = "top_cloth", AlbedoColor = new Color(0.22f, 0.18f, 0.14f), Roughness = 1f },
			["leather"] = new StandardMaterial3D { ResourceName = "top_leather", AlbedoColor = new Color(0.2f, 0.11f, 0.07f), Roughness = 0.8f }, ["*"] = timber,
		};
		var shade = new RoomShade(new Aabb(new Vector3(-TopR * 1.6f, y0, -TopR * 1.6f), new Vector3(TopR * 3.2f, TopH, TopR * 3.2f))) { Dim = 0.75f, Reach = 0.9f };
		Vector3 W(float deg, float r, float y = 0f) { float a = Mathf.DegToRad(deg); return new Vector3(Mathf.Sin(a) * r, y0 + y, Mathf.Cos(a) * r); }
		float Face(float deg) => Mathf.DegToRad(deg) + Mathf.Pi;   // turned to face the middle
		void Piece(MeshKit kit, string model, Vector3 at, float yaw, Vector3 size, bool collide = true)
		{
			FurnitureKit.Add(kit, model, at, yaw, roles);
			shade.Under(at, size, 0.5f, 0.35f);
			if (collide) _body.AddChild(new CollisionShape3D { Position = at + Vector3.Up * (size.Y * 0.5f), Rotation = new Vector3(0, yaw, 0), Shape = new BoxShape3D { Size = size } });
		}
		Piece(k, "crate", W(120f, TopR - 0.5f), Face(120f) + 0.15f, new Vector3(0.6f, 0.5f, 0.6f));
		Piece(k, "crate", W(135f, TopR - 0.55f), Face(135f) - 0.3f, new Vector3(0.6f, 0.5f, 0.6f));
		FurnitureKit.Add(k, "crate", new Transform3D(new Basis(Vector3.Up, Face(126f) + 0.5f), W(127f, TopR - 0.55f, 0.5f)), roles);
		Piece(k, "flour_sack", W(150f, TopR - 0.45f), Face(150f), new Vector3(0.45f, 0.5f, 0.35f), false);
		Piece(k, "footlocker", W(205f, TopR - 0.45f), Face(205f), new Vector3(0.9f, 0.45f, 0.5f));
		FurnitureKit.Add(k, "papers", W(205f, TopR - 0.45f, 0.45f), Face(205f) + 0.4f, roles);
		FurnitureKit.Add(k, "books_stack", W(212f, TopR - 0.5f, 0.45f), Face(212f) - 0.2f, roles);
		// a chair tipped on its back, out from the wall
		var chairAt = W(250f, TopR - 1.2f, 0.24f);
		FurnitureKit.Add(k, "dining_chair", new Transform3D(new Basis(Vector3.Up, Face(250f) + 0.6f) * new Basis(Vector3.Right, -Mathf.Pi * 0.5f), chairAt), roles);
		shade.Under(chairAt with { Y = y0 }, new Vector3(0.6f, 0.4f, 0.9f), 0.35f, 0.3f);
		FurnitureKit.Add(small, "papers", W(235f, TopR - 0.9f), 2.1f, roles);
		FurnitureKit.Add(small, "papers", W(290f, TopR - 1.4f), 0.7f, roles);
		// a coil of rope by the arch's side, a lantern on a hook in the wall
		small.Mat(new StandardMaterial3D { ResourceName = "top_rope", AlbedoColor = new Color(0.4f, 0.34f, 0.24f), Roughness = 1f, VertexColorUseAsAlbedo = true });
		var coil = W(300f, TopR - 0.45f);
		for (int t = 0; t < 5; t++)
		{
			float rr = 0.22f - t * 0.012f, yy = 0.025f + t * 0.03f;
			small.Color = Colors.White * rng.RandfRange(0.75f, 1f);
			for (int j = 0; j < 14; j++)
			{
				float b0 = Mathf.Tau * j / 14f, b1 = Mathf.Tau * (j + 1) / 14f;
				small.Cylinder(coil + new Vector3(Mathf.Sin(b0) * rr, yy, Mathf.Cos(b0) * rr), coil + new Vector3(Mathf.Sin(b1) * rr, yy, Mathf.Cos(b1) * rr), 0.02f, 0.02f, 5, false);
			}
		}
		shade.Under(coil, new Vector3(0.5f, 0.2f, 0.5f), 0.4f, 0.25f);
		small.Mat(StairwellTextures.RailMat);
		small.Color = Colors.White * 0.6f;
		var hook = W(215f, TopR - 0.02f, 1.85f);
		small.Cylinder(hook, W(215f, TopR - 0.14f, 1.85f), 0.008f, 0.008f, 4, false);
		FurnitureKit.Add(small, "candlestick", W(205f, TopR - 0.35f, 0.45f), 0.3f, roles);
		// ---- the bake: dark at the foot and in the seams, under the crates, the ceiling's boards dark between the
		// joists, the light round the lamp; damp at the foot by the crates; worn from the shaft to the arch
		shade.Light(lampAt, 4.2f).Damp(W(130f, TopR, 0.5f), 1.6f, 0.8f).Damp(W(250f, TopR, TopH), 1.4f, 0.6f)
			.Path(new Vector3(0, y0, DaisR), new Vector3(0, y0, TopR), 1.4f, 0.8f);
		shade.Apply(k);
		shade.Apply(lk);
		shade.Apply(small);
		k.CommitTo(this, "TopRoom", true);
		lk.CommitTo(this, "TopRoomLining", false);
		small.CommitTo(this, "TopRoomClutter", false);
		var dress = new Node3D { Name = "TopDress" };
		AddChild(dress);
		DecalDresser.Place(dress, "tide", W(130f, TopR - 0.01f, 0.5f), -W(130f, 1f, 0f) with { Y = 0 }, new Vector2(2.2f, 1.0f), 0.7f);
		DecalDresser.Place(dress, "streak", W(250f, TopR - 0.01f, 2.4f), -W(250f, 1f, 0f) with { Y = 0 }, new Vector2(1.2f, 2.2f), 0.7f);
		DecalDresser.Place(dress, "floor", W(180f, 3.4f, 0f), Vector3.Up, new Vector2(1.6f, 1.6f), 0.6f, 0.7f, 0.1f);
	}

	/// <summary>The plinth course's stone: the drum's stained concrete, darker.</summary>
	private static StandardMaterial3D WallConcreteDark => _wallDark ??= DeepConcrete.Stained("roundroom_top_plinth", new Color(0.5f, 0.47f, 0.42f), 0.6f, 1.5f, 0.12f);
	private static StandardMaterial3D _wallDark;
}
