using System.Collections.Generic;
using Godot;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World.LakeParts;

/// <summary>
/// The lake's built things: the near shore's dock (and the sign at its foot pointing across the
/// water), the far shore's forest rescue station, and the invisible fences that keep the player on
/// the two beaches. All positions are Lake-local; see <see cref="LakeShape"/> for the layout.
/// </summary>
public static class LakeStructures
{
	// ------------------------------------------------------------------ the dock

	/// <summary>A narrow plank dock on posts, grey and gapped, with a cleat and a line out to the
	/// boat's bow (returned so the crossing can cast it off).</summary>
	public static Node3D BuildDock(Node3D parent, RandomNumberGenerator rng)
	{
		var root = new Node3D { Name = "Dock" };
		parent.AddChild(root);
		var k = new MeshKit();
		float z0 = LakeShape.DockStartZ, z1 = LakeShape.DockEndZ, hw = LakeShape.DockHalfWidth, top = LakeShape.DockDeck;

		// planks across the dock, each a little different, a few gaps and one missing near the end
		k.Mat(PropTextures.DeckMat);
		for (float z = z0; z > z1; z -= 0.26f)
		{
			if (z < z1 + 1.6f && z > z1 + 1.3f) continue;
			float shade = rng.RandfRange(0.42f, 0.6f);
			k.Color = new Color(shade, shade * 0.93f, shade * 0.85f);
			float ground = LakeShape.Ground(0f, z);
			float y = Mathf.Max(top, ground + 0.12f) + rng.RandfRange(-0.012f, 0.012f);
			BuildKit.Box(k, new Vector3(rng.RandfRange(-0.03f, 0.03f), y - 0.03f, z - 0.11f), new Vector3(hw * 2f + rng.RandfRange(-0.06f, 0.1f), 0.05f, 0.22f), 1.2f,
				BuildKit.Face.None, new Basis(Vector3.Up, rng.RandfRange(-0.025f, 0.025f)));
		}
		// stringers and posts
		k.Mat(PropTextures.WetPostMat);
		k.Color = new Color(0.36f, 0.32f, 0.27f);
		foreach (float x in new[] { -hw + 0.12f, hw - 0.12f })
			BuildKit.Box(k, new Vector3(x, top - 0.13f, (z0 + z1) * 0.5f), new Vector3(0.1f, 0.14f, z0 - z1), 1.2f);
		for (float z = z0 - 0.4f; z > z1 - 0.1f; z -= 2.4f)
			foreach (float x in new[] { -hw, hw })
			{
				float lean = rng.RandfRange(-0.04f, 0.04f);
				k.Cylinder(new Vector3(x, -1.6f, z), new Vector3(x + lean, top + 0.35f, z), 0.08f, 0.075f, 7, true, 1.5f);
			}
		// the cleat on the boat's side of the dock end, and the line to the bow
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.3f, 0.27f, 0.23f);
		Vector3 cleat = new(hw - 0.15f, top + 0.05f, LakeShape.BoatMooring.Y - 1.3f);
		BuildKit.Box(k, cleat, new Vector3(0.1f, 0.07f, 0.34f));
		k.Color = Colors.White;
		k.CommitTo(root, "DockMesh");

		var body = new StaticBody3D { Name = "DockBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		root.AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, top - 0.1f, (z0 + z1) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(hw * 2f, 0.2f, z0 - z1) } });
		// a ramp up onto the deck from the beach (the body can't climb a vertical edge)
		float gz = z0 + 1.6f, gy = LakeShape.Ground(0f, gz);
		float run = gz - (z0 - 0.4f), rise = top - gy;
		body.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(hw * 2f, 0.1f, Mathf.Sqrt(run * run + rise * rise)) },
			Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Atan2(rise, run)), new Vector3(0, (top + gy) * 0.5f - 0.05f, (gz + z0 - 0.4f) * 0.5f)),
		});

		BuildDockSign(root);
		return root;
	}

	/// <summary>The line from the dock cleat to the boat's bow ring: a thin sagging rope the crossing
	/// frees when the boat pushes off.</summary>
	public static MeshInstance3D BuildMooringLine(Node parent, Vector3 from, Vector3 to)
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.Flat("lake_rope", new Color(0.5f, 0.44f, 0.33f)));
		const int segs = 8;
		Vector3 prev = from;
		for (int i = 1; i <= segs; i++)
		{
			float t = i / (float)segs;
			Vector3 p = from.Lerp(to, t) + Vector3.Down * (Mathf.Sin(t * Mathf.Pi) * 0.25f);
			k.Cylinder(prev, p, 0.014f, 0.014f, 4, false);
			prev = p;
		}
		return k.CommitTo(parent, "MooringLine", false);
	}

	/// <summary>An old routed park sign at the foot of the dock: the only thing on this shore that
	/// says where the player is meant to go.</summary>
	private static void BuildDockSign(Node3D dock)
	{
		var k = new MeshKit();
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.45f, 0.4f, 0.33f);
		Vector3 post = new(-1.9f, LakeShape.Ground(-1.9f, 4.2f) - 0.2f, 4.2f);
		SignKit.Post(k, post, 1.85f, 0.12f);
		var b = new Basis(Vector3.Up, 0.35f);
		Vector3 board = post + new Vector3(0.05f, 1.55f, 0.07f);
		k.Mat(PropTextures.SignPlankMat);
		k.Color = new Color(0.5f, 0.45f, 0.36f);
		SignKit.ArrowBoard(k, board, b * new Basis(Vector3.Forward, 0.05f), 1.3f, 0.3f, 0.05f, 0);
		k.Color = Colors.White;
		k.CommitTo(dock, "DockSign");
		var faded = new Color(0.58f, 0.54f, 0.45f);
		Vector3 face = board + b.Z * 0.03f;
		SignKit.Text(dock, "RESCUE STATION 7", face + Vector3.Up * 0.05f, b * new Basis(Vector3.Forward, 0.05f), 0.11f, faded);
		SignKit.Text(dock, "ACROSS THE LAKE  -  BY BOAT", face + Vector3.Down * 0.08f, b * new Basis(Vector3.Forward, 0.05f), 0.065f, faded);
	}

	// ------------------------------------------------------------------ the station

	public const float StationW = 15f, StationD = 9f, StationWallH = 3.7f;
	public const float StationDoorW = 2.2f, StationDoorH = 2.35f;

	/// <summary>The front door's threshold (Lake-local): the wall's outer face at the door's centre.</summary>
	public static Vector3 StationDoor => new(0f, LakeShape.StationFloor, LakeShape.StationSite.Y + StationD * 0.5f);

	/// <summary>
	/// A large, dilapidated forest rescue station: a round-log main hall gone grey, its gable end to
	/// the lake under a big routed "FOREST RESCUE SERVICE" board, a sagging porch the width of the
	/// front with two life rings still hanging from its posts, boarded and broken windows (one with a
	/// faint lamp still burning somewhere inside), a half-stripped roof, a lattice radio mast with its
	/// guy wires gone slack, a flagpole with a rag of a flag, and an old rescue boat upturned on
	/// sawhorses beside it. Its front door stands a little open onto darkness.
	/// </summary>
	public static Node3D BuildStation(Node3D parent, RandomNumberGenerator rng)
	{
		float w = StationW, d = StationD, wallH = StationWallH, ridgeH = 6.4f;
		float hw = w * 0.5f, hd = d * 0.5f, floor = LakeShape.StationFloor;
		var gen = new Node3D { Name = "RescueStation", Position = new Vector3(LakeShape.StationSite.X, floor, LakeShape.StationSite.Y) };
		parent.AddChild(gen);
		var k = new MeshKit();
		var cols = new List<(Vector3 c, Vector3 s)>();
		var frame = PropTextures.PostMat;
		var boards = BuildingTextures.BoardsMat;

		// piers down to the ground, and the floor
		k.Mat(ProcTextures.ConcreteMat);
		k.Color = new Color(0.5f, 0.48f, 0.45f);
		for (float x = -hw + 0.3f; x <= hw - 0.2f; x += (w - 0.6f) / 4f)
			foreach (float z in new[] { -hd + 0.3f, 0f, hd - 0.3f })
			{
				float gy = LakeShape.Ground(x, LakeShape.StationSite.Y + z) - floor;
				BuildKit.Box(k, new Vector3(x, (gy - 0.4f) * 0.5f, z), new Vector3(0.45f, -gy + 0.4f, 0.45f), 2f, BuildKit.Face.NY);
			}
		k.Mat(BuildingTextures.FloorMat);
		k.Color = new Color(0.5f, 0.46f, 0.4f);
		BuildKit.Box(k, new Vector3(0, -0.05f, 0), new Vector3(w, 0.1f, d), 1f / 0.6f);
		cols.Add((new Vector3(0, -0.25f, 0), new Vector3(w, 0.5f, d)));

		// the log walls, course by course (the same interlocking technique as the cabin)
		const float logT = 0.26f, ext = 0.24f;
		const int courses = 13;
		float logH = wallH / courses, uLen = logH * 4f;
		float doorW = StationDoorW, doorH = StationDoorH;
		var logMat = BuildingTextures.LogMat;
		var logEnd = BuildingTextures.LogEndMat;
		for (int c = 0; c < courses; c++)
		{
			float y0 = c * logH, y1 = y0 + logH;
			bool frontLong = c % 2 == 0;
			float shade = rng.RandfRange(0.58f, 0.78f);
			k.Color = new Color(shade, shade * 0.97f, shade * 0.93f);
			float xa = frontLong ? -hw - ext : -hw + logT * 0.5f, xb = -xa;
			bool doorHere = y1 > 0.05f && y0 < doorH - 0.05f;
			if (doorHere)
			{
				float dw = doorW * 0.5f;
				BuildKit.Log(k, logMat, logEnd, false, xa, -dw, hd, y0, y1, logT, uLen, true, true);
				BuildKit.Log(k, logMat, logEnd, false, dw, xb, hd, y0, y1, logT, uLen, true, true);
			}
			else BuildKit.Log(k, logMat, logEnd, false, xa, xb, hd, y0, y1, logT, uLen, true, true);
			BuildKit.Log(k, logMat, logEnd, false, xa, xb, -hd, y0, y1, logT, uLen, true, true);
			float za = frontLong ? -hd + logT * 0.5f : -hd - ext, zb = -za;
			foreach (int s in new[] { -1, 1 })
				BuildKit.Log(k, logMat, logEnd, true, za, zb, s * hw, y0, y1, logT, uLen);
		}

		// darkness behind the doorway (an inner box, faces inward), and the door hanging open
		k.Mat(ProcTextures.Flat("station_void", new Color(0.01f, 0.01f, 0.012f), 1f, 0f));
		k.Color = Colors.White;
		{
			float vx = doorW * 0.5f + 0.3f, back = hd - 1.6f, front = hd - 0.14f;
			k.Quad(new Vector3(-vx, 0, back), new Vector3(vx, 0, back), new Vector3(vx, doorH, back), new Vector3(-vx, doorH, back), Vector3.Back);
			k.Quad(new Vector3(-vx, 0, back), new Vector3(-vx, 0, front), new Vector3(-vx, doorH, front), new Vector3(-vx, doorH, back), Vector3.Right);
			k.Quad(new Vector3(vx, 0, front), new Vector3(vx, 0, back), new Vector3(vx, doorH, back), new Vector3(vx, doorH, front), Vector3.Left);
			k.Quad(new Vector3(-vx, doorH, back), new Vector3(vx, doorH, back), new Vector3(vx, doorH, front), new Vector3(-vx, doorH, front), Vector3.Down);
		}
		k.Mat(boards);
		k.Color = new Color(0.36f, 0.33f, 0.3f);
		var doorBasis = new Basis(Vector3.Up, 1.15f);
		BuildKit.Box(k, new Vector3(-doorW * 0.5f, 0, hd - logT * 0.5f) + doorBasis * new Vector3(doorW * 0.46f, doorH * 0.5f - 0.02f, -0.03f),
			new Vector3(doorW * 0.92f, doorH - 0.05f, 0.05f), 1.2f, BuildKit.Face.None, doorBasis);
		// the frame
		k.Mat(frame);
		k.Color = new Color(0.3f, 0.27f, 0.23f);
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(s * (doorW * 0.5f + 0.05f), doorH * 0.5f, hd + 0.1f), new Vector3(0.12f, doorH, 0.08f));
		BuildKit.Box(k, new Vector3(0, doorH + 0.05f, hd + 0.1f), new Vector3(doorW + 0.3f, 0.12f, 0.08f));

		// windows: front left boarded over; front right broken, a faint lamp glowing far inside
		Window(k, rng, new Vector3(-4.4f, 1.75f, hd + 0.14f), Vector3.Back, true);
		Vector3 lit = new(4.4f, 1.75f, hd + 0.14f);
		Window(k, rng, lit, Vector3.Back, false);
		foreach (float z in new[] { -2.2f, 2.2f })
		{
			Window(k, rng, new Vector3(-hw - 0.14f, 1.75f, z), Vector3.Left, true);
			Window(k, rng, new Vector3(hw + 0.14f, 1.75f, z), Vector3.Right, rng.Randf() < 0.6f);
		}

		// a few boards knocked loose from the front, hanging askew
		k.Mat(boards);
		k.Color = new Color(0.34f, 0.33f, 0.32f);
		for (int i = 0; i < 6; i++)
		{
			float x = rng.RandfRange(-hw + 0.6f, hw - 0.6f);
			if (Mathf.Abs(x) < doorW * 0.5f + 0.4f) continue;
			BuildKit.Box(k, new Vector3(x, rng.RandfRange(0.5f, wallH - 0.5f), hd + 0.17f), new Vector3(0.2f, 0.95f, 0.03f), 1.4f, BuildKit.Face.None, new Basis(Vector3.Forward, rng.RandfRange(-0.5f, 0.5f)));
		}

		// Roof: gable end to the lake. One slope still shingled; the other stripped to sagging boards
		// with a hole right through (rafters showing).
		Vector3 ridgeF = new(0, ridgeH, hd + 0.6f), ridgeB = new(0, ridgeH - 0.3f, -hd - 0.6f);
		float ov = 0.5f;
		k.Mat(BuildingTextures.ShingleMat);
		k.Color = new Color(0.38f, 0.36f, 0.34f);
		Vector3 eL = new(-hw - ov, wallH - 0.1f, 0);
		k.Quad(ridgeB, ridgeF, eL with { Z = hd + ov + 0.1f }, eL with { Z = -hd - ov - 0.1f }, (ridgeF - eL).Cross(Vector3.Back).Normalized() * -1f);
		k.Mat(boards);
		k.Color = new Color(0.3f, 0.29f, 0.27f);
		Vector3 eR = new(hw + ov, wallH - 0.1f, 0);
		float sag = 0.6f;
		Vector3 midR = ((ridgeF + ridgeB) * 0.5f) with { X = hw * 0.5f, Y = (ridgeH + wallH) * 0.5f - sag };
		// two panels with the sag between them; a gap left open at the back for the hole
		k.Quad(ridgeF, ridgeF.Lerp(ridgeB, 0.45f), midR with { Z = midR.Z + 0f }, eR with { Z = hd + ov + 0.1f }, Vector3.Up);
		k.Tri(ridgeF.Lerp(ridgeB, 0.45f), eR with { Z = -hd * 0.1f }, midR, Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.Down);
		k.Tri(ridgeF.Lerp(ridgeB, 0.8f), ridgeB, eR with { Z = -hd - ov - 0.1f }, Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.Down);
		k.Mat(frame);
		k.Color = new Color(0.28f, 0.26f, 0.23f);
		for (float z = -hd * 0.1f; z > -hd * 0.75f; z -= 0.6f)   // exposed rafters in the hole
			k.Cylinder(new Vector3(0, ridgeH - 0.05f + z * 0.03f, z), new Vector3(hw + ov, wallH - 0.1f, z), 0.06f, 0.06f, 4, false);
		// gables (front one carries the sign)
		k.Color = new Color(0.34f, 0.32f, 0.29f);
		BuildKit.TriPanel(k, new Vector3(-hw, wallH, hd), new Vector3(hw, wallH, hd), new Vector3(0, ridgeH, hd), Vector3.Back, Vector3.Right, 1f, 0.05f);
		BuildKit.TriPanel(k, new Vector3(-hw, wallH, -hd), new Vector3(hw, wallH, -hd), new Vector3(0, ridgeH - 0.3f, -hd), Vector3.Forward, Vector3.Right, 1f, 0.05f);

		// The sign board across the gable
		k.Mat(PropTextures.SignPlankMat);
		k.Color = new Color(0.46f, 0.4f, 0.32f);
		var signB = new Basis(Vector3.Forward, 0.035f);
		Vector3 signAt = new(0, wallH + 0.72f, hd + 0.16f);
		SignKit.ArrowBoard(k, signAt, signB, 7.2f, 0.95f, 0.07f, 0);
		k.Mat(frame);
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(s * 3.1f, wallH + 0.45f, hd + 0.1f), new Vector3(0.1f, 1.3f, 0.08f));

		// the porch: a deck the width of the front, a lean-to roof sagging on four posts (one gone)
		float porchD = 2.4f, porchY = -0.05f;
		k.Mat(PropTextures.DeckMat);
		for (float z = hd + 0.15f; z < hd + porchD; z += 0.24f)
		{
			float sh = rng.RandfRange(0.42f, 0.58f);
			k.Color = new Color(sh, sh * 0.94f, sh * 0.86f);
			if (rng.Randf() < 0.08f) continue;   // a missing plank or two
			BuildKit.Box(k, new Vector3(rng.RandfRange(-0.05f, 0.05f), porchY, z + 0.1f), new Vector3(w + 0.4f, 0.05f, 0.2f), 1.2f);
		}
		cols.Add((new Vector3(0, porchY - 0.15f, hd + porchD * 0.5f + 0.1f), new Vector3(w + 0.4f, 0.3f, porchD)));
		// steps down to the path, with a ramp collider (the player can't climb a vertical riser)
		float gyStep = LakeShape.Ground(0f, LakeShape.StationSite.Y + hd + porchD + 0.6f) - floor;
		k.Color = new Color(0.46f, 0.42f, 0.36f);
		for (int i = 0; i < 3; i++)
		{
			float t = (i + 1) / 4f;
			BuildKit.Box(k, new Vector3(0, Mathf.Lerp(porchY, gyStep, t), hd + porchD + 0.15f + i * 0.3f), new Vector3(2.6f, 0.06f, 0.3f), 1.2f);
		}
		float rampLen = 1.4f, rise = porchY - gyStep;
		var ramp = new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(2.6f, 0.1f, Mathf.Sqrt(rampLen * rampLen + rise * rise)) },
			Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Atan2(rise, rampLen)), new Vector3(0, (porchY + gyStep) * 0.5f - 0.05f, hd + porchD + rampLen * 0.5f)),
		};
		k.Mat(frame);
		k.Color = new Color(0.3f, 0.27f, 0.23f);
		float postZ = hd + porchD - 0.1f;
		var posts = new[] { -hw + 0.3f, -doorW * 0.5f - 0.7f, doorW * 0.5f + 0.7f, hw - 0.3f };
		for (int i = 0; i < posts.Length; i++)
		{
			float x = posts[i];
			if (i == 3)
			{
				// the far right post has given way: snapped, lying on the deck
				k.Cylinder(new Vector3(x, porchY, postZ), new Vector3(x, 0.7f, postZ), 0.09f, 0.08f, 6, true);
				k.Cylinder(new Vector3(x - 0.3f, porchY + 0.1f, postZ - 0.2f), new Vector3(x - 2.2f, porchY + 0.12f, postZ - 0.9f), 0.08f, 0.08f, 6, true);
				continue;
			}
			k.Cylinder(new Vector3(x, porchY, postZ), new Vector3(x + rng.RandfRange(-0.05f, 0.05f), wallH - 0.45f - (i == 2 ? 0.25f : 0f), postZ), 0.09f, 0.08f, 6, true);
		}
		k.Mat(boards);
		k.Color = new Color(0.32f, 0.31f, 0.29f);
		// the porch roof slumps toward the missing post
		Vector3 pa = new(-hw - 0.2f, wallH - 0.2f, hd + 0.1f), pb = new(hw + 0.2f, wallH - 0.2f, hd + 0.1f);
		Vector3 pc = new(hw + 0.2f, wallH - 1.35f, hd + porchD + 0.3f), pd = new(-hw - 0.2f, wallH - 0.55f, hd + porchD + 0.3f);
		k.Quad(pa, pb, pc, pd, (pb - pa).Cross(pd - pa).Normalized() * -1f);
		k.Quad(pd, pc, pb, pa, (pb - pa).Cross(pd - pa).Normalized());

		// life rings hanging on the two middle posts
		foreach (float x in new[] { posts[1], posts[2] })
			LifeRing(k, new Vector3(x, 1.55f, postZ + 0.12f), rng);

		k.Color = Colors.White;
		k.CommitTo(gen, "StationMesh");

		// the sign's lettering, faded almost to the grain
		var letters = new Color(0.72f, 0.66f, 0.54f);
		SignKit.Text(gen, "FOREST RESCUE SERVICE", signAt + signB * new Vector3(0, 0.14f, 0.04f), signB, 0.44f, letters);
		SignKit.Text(gen, "STATION 7", signAt + signB * new Vector3(0, -0.28f, 0.04f), signB, 0.22f, letters);

		// the lamp still burning somewhere at the back of the right-hand room: steady, faint, warm
		var glow = new MeshInstance3D
		{
			Name = "LitWindowGlow",
			Mesh = new QuadMesh { Size = new Vector2(1.0f, 0.8f) },
			Position = lit + new Vector3(0, 0, -0.1f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.1f, 0.06f, 0.03f),
				EmissionEnabled = true,
				Emission = new Color(1f, 0.62f, 0.3f),
				EmissionEnergyMultiplier = 0.9f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
		};
		gen.AddChild(glow);
		gen.AddChild(new OmniLight3D
		{
			Name = "LitWindowSpill",
			Position = lit + new Vector3(0, -0.2f, 0.9f),
			LightColor = new Color(1f, 0.64f, 0.34f),
			LightEnergy = 0.45f,
			OmniRange = 3.6f,
			ShadowEnabled = false,
		});

		var body = new StaticBody3D { Name = "StationBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		gen.AddChild(body);
		float sideW = hw - doorW * 0.5f;
		body.AddChild(new CollisionShape3D { Position = new Vector3(-hw, wallH * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(0.3f, wallH, d) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(hw, wallH * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(0.3f, wallH, d) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, wallH * 0.5f, -hd), Shape = new BoxShape3D { Size = new Vector3(w, wallH, 0.3f) } });
		foreach (int s in new[] { -1, 1 })
			body.AddChild(new CollisionShape3D { Position = new Vector3(s * (doorW * 0.5f + sideW * 0.5f), wallH * 0.5f, hd), Shape = new BoxShape3D { Size = new Vector3(sideW, wallH, 0.3f) } });
		foreach (var (c, s) in cols) body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		body.AddChild(ramp);

		BuildMast(gen, new Vector3(hw + 3.2f, 0f, -1.5f), rng);
		BuildFlagpole(gen, new Vector3(-5.5f, 0f, hd + porchD + 5.5f));
		BuildUpturnedBoat(gen, new Vector3(-hw - 3.4f, 0f, 1.2f), rng);
		return gen;
	}

	/// <summary>A window on a wall facing <paramref name="n"/>: a dark recess in a plank frame,
	/// either boarded over with three crooked boards or broken, a jagged shard left in one corner.</summary>
	private static void Window(MeshKit k, RandomNumberGenerator rng, Vector3 c, Vector3 n, bool boarded)
	{
		Vector3 right = Vector3.Up.Cross(n).Normalized();
		var b = new Basis(right, Vector3.Up, n);
		k.Mat(ProcTextures.Flat("station_void", new Color(0.01f, 0.01f, 0.012f), 1f, 0f));
		k.Color = Colors.White;
		k.Quad(c - right * 0.55f - Vector3.Up * 0.45f, c + right * 0.55f - Vector3.Up * 0.45f, c + right * 0.55f + Vector3.Up * 0.45f, c - right * 0.55f + Vector3.Up * 0.45f, n);
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.3f, 0.27f, 0.23f);
		BuildKit.Box(k, c + Vector3.Up * 0.5f, new Vector3(1.3f, 0.1f, 0.08f), 1f, BuildKit.Face.None, b);
		BuildKit.Box(k, c - Vector3.Up * 0.5f, new Vector3(1.4f, 0.1f, 0.12f), 1f, BuildKit.Face.None, b);
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, c + right * s * 0.6f, new Vector3(0.1f, 1.1f, 0.08f), 1f, BuildKit.Face.None, b);
		if (boarded)
		{
			k.Mat(BuildingTextures.BoardsMat);
			k.Color = new Color(0.4f, 0.37f, 0.33f);
			for (int i = 0; i < 3; i++)
				BuildKit.Box(k, c + n * 0.05f + Vector3.Up * ((i - 1) * 0.3f), new Vector3(1.45f, 0.2f, 0.03f), 1.4f, BuildKit.Face.None,
					b * new Basis(Vector3.Back, rng.RandfRange(-0.15f, 0.15f)));
		}
		else
		{
			k.Mat(ProcTextures.Flat("station_glass", new Color(0.45f, 0.5f, 0.5f), 0.1f, 0.6f));
			k.Color = Colors.White;
			Vector3 corner = c - right * 0.55f + Vector3.Up * 0.45f + n * 0.01f;
			k.Tri(corner, corner + right * 0.42f, corner - Vector3.Up * 0.5f, n, Vector2.Zero, Vector2.Right, Vector2.Down);
		}
	}

	/// <summary>A red-and-white life ring, hung flat against a post.</summary>
	private static void LifeRing(MeshKit k, Vector3 c, RandomNumberGenerator rng)
	{
		var red = ProcTextures.Flat("life_ring_red", new Color(0.55f, 0.1f, 0.07f), 0.8f, 0.3f);
		var white = ProcTextures.Flat("life_ring_white", new Color(0.72f, 0.7f, 0.64f), 0.8f, 0.3f);
		const int segs = 16;
		float r = 0.3f, tube = 0.07f;
		float tilt = rng.RandfRange(-0.25f, 0.25f);
		for (int i = 0; i < segs; i++)
		{
			float a0 = Mathf.Tau * i / segs + tilt, a1 = Mathf.Tau * (i + 1) / segs + tilt;
			k.Mat((i / 2) % 2 == 0 ? red : white);
			k.Color = Colors.White;
			k.Cylinder(c + new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, 0), c + new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, 0), tube, tube, 6, false);
		}
	}

	/// <summary>A tall lattice radio mast, rusted, three guy wires hanging slack.</summary>
	private static void BuildMast(Node3D gen, Vector3 at, RandomNumberGenerator rng)
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.45f, 0.3f, 0.22f);
		float h = 14f, r = 0.32f;
		float gy = LakeShape.Ground(at.X, LakeShape.StationSite.Y + at.Z) - LakeShape.StationFloor;
		Vector3 baseP = at + Vector3.Up * gy;
		var legs = new Vector3[3];
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Tau * i / 3f;
			legs[i] = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
			k.Cylinder(baseP + legs[i], baseP + legs[i] * 0.5f + Vector3.Up * h, 0.035f, 0.025f, 4, true);
		}
		for (float y = 0.8f; y < h - 0.5f; y += 1.1f)
			for (int i = 0; i < 3; i++)
			{
				float f0 = y / h, f1 = (y + 1.1f) / h;
				Vector3 a0 = baseP + legs[i] * (1f - 0.5f * f0) + Vector3.Up * y;
				Vector3 b1 = baseP + legs[(i + 1) % 3] * (1f - 0.5f * f1) + Vector3.Up * (y + 1.1f);
				k.Cylinder(a0, b1, 0.012f, 0.012f, 3, false);
			}
		// a small dish, pointing nowhere
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.55f, 0.52f, 0.48f);
		Vector3 top = baseP + Vector3.Up * (h * 0.78f) + new Vector3(0.3f, 0, 0.2f);
		k.Cylinder(top, top + new Vector3(0.35f, -0.1f, 0.5f), 0.45f, 0.1f, 10, true);
		// slack guy wires to the ground
		k.Mat(ProcTextures.Flat("mast_wire", new Color(0.18f, 0.17f, 0.16f)));
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Tau * i / 3f + 0.6f;
			Vector3 anchor = at + new Vector3(Mathf.Cos(a) * 6f, 0, Mathf.Sin(a) * 6f);
			anchor.Y = LakeShape.Ground(anchor.X, LakeShape.StationSite.Y + anchor.Z) - LakeShape.StationFloor;
			Vector3 from = baseP + Vector3.Up * (h * 0.7f), prev = from;
			for (int s = 1; s <= 10; s++)
			{
				float t = s / 10f;
				Vector3 p = from.Lerp(anchor, t) + Vector3.Down * (Mathf.Sin(t * Mathf.Pi) * 1.4f);
				k.Cylinder(prev, p, 0.008f, 0.008f, 3, false);
				prev = p;
			}
		}
		k.Color = Colors.White;
		k.CommitTo(gen, "RadioMast");
	}

	private static void BuildFlagpole(Node3D gen, Vector3 at)
	{
		var k = new MeshKit();
		float gy = LakeShape.Ground(at.X, LakeShape.StationSite.Y + at.Z) - LakeShape.StationFloor;
		Vector3 b = at + Vector3.Up * gy;
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.5f, 0.48f, 0.45f);
		k.Cylinder(b + Vector3.Down * 0.3f, b + Vector3.Up * 8f, 0.06f, 0.04f, 6, true);
		// a rag of a flag, hanging limp, torn to a ragged edge
		k.Mat(ProcTextures.Flat("flag_rag", new Color(0.4f, 0.3f, 0.25f), 1f, 0.1f));
		k.Color = Colors.White;
		Vector3 top = b + Vector3.Up * 7.8f;
		Vector3[] edge = { new(0.45f, -0.3f, 0.05f), new(0.35f, -0.75f, 0.1f), new(0.5f, -1.05f, 0.02f), new(0.15f, -1.3f, 0.08f) };
		Vector3 prev = top;
		foreach (var e in edge)
		{
			Vector3 inner = top + new Vector3(0.04f, e.Y, 0);
			k.Tri(prev, top + e, inner, Vector3.Back, Vector2.Zero, Vector2.Right, Vector2.Down);
			k.Tri(prev, inner, top + e, Vector3.Forward, Vector2.Zero, Vector2.Down, Vector2.Right);
			prev = top + e;
		}
		k.CommitTo(gen, "Flagpole");
	}

	/// <summary>An old aluminium rescue boat, upturned on two sawhorses, a hole stove in its bottom.</summary>
	private static void BuildUpturnedBoat(Node3D gen, Vector3 at, RandomNumberGenerator rng)
	{
		var k = new MeshKit();
		float gy = LakeShape.Ground(at.X, LakeShape.StationSite.Y + at.Z) - LakeShape.StationFloor;
		Vector3 b = at + Vector3.Up * gy;
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.36f, 0.32f, 0.27f);
		foreach (float z in new[] { -1.2f, 1.2f })
		{
			k.Cylinder(b + new Vector3(-0.5f, 0, z), b + new Vector3(0, 0.75f, z), 0.04f, 0.04f, 4, true);
			k.Cylinder(b + new Vector3(0.5f, 0, z), b + new Vector3(0, 0.75f, z), 0.04f, 0.04f, 4, true);
			BuildKit.Box(k, b + new Vector3(0, 0.77f, z), new Vector3(1.1f, 0.06f, 0.08f));
		}
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.55f, 0.53f, 0.5f);
		var hull = new Basis(Vector3.Forward, Mathf.Pi);   // keel up
		Vector3 c = b + new Vector3(0, 1.15f, 0);
		BuildKit.Box(k, c + Vector3.Up * 0.3f, new Vector3(1.2f, 0.05f, 4.2f), 1f, BuildKit.Face.None, hull);
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, c + new Vector3(s * 0.66f, 0f, 0), new Vector3(0.05f, 0.6f, 4.1f), 1f, BuildKit.Face.None, new Basis(Vector3.Forward, -s * 0.2f));
		BuildKit.Box(k, c + new Vector3(0, 0, 2.1f), new Vector3(1.3f, 0.6f, 0.05f), 1f, BuildKit.Face.None, new Basis(Vector3.Right, 0.35f));
		k.Mat(ProcTextures.Flat("station_void", new Color(0.01f, 0.01f, 0.012f), 1f, 0f));
		k.Color = Colors.White;
		k.Quad(c + new Vector3(-0.25f, 0.34f, -0.8f), c + new Vector3(0.2f, 0.34f, -0.7f), c + new Vector3(0.15f, 0.34f, -0.25f), c + new Vector3(-0.2f, 0.34f, -0.35f), Vector3.Up);
		k.Color = Colors.White;
		k.CommitTo(gen, "UpturnedBoat");
	}

	// ------------------------------------------------------------------ fences

	/// <summary>Invisible walls: around the wake-up clearing and the dock (hip height along the dock,
	/// so the "get in the boat" ray passes over them), and around the far beach and station rise.</summary>
	public static void BuildFences(Node3D parent)
	{
		var body = new StaticBody3D { Name = "Fences", CollisionLayer = 1, CollisionMask = 0 };
		parent.AddChild(body);
		void Wall(Vector2 a, Vector2 b, float h = 3f, float baseY = -1f)
		{
			Vector2 d = b - a;
			float len = d.Length();
			if (len < 0.01f) return;
			Vector2 mid = (a + b) * 0.5f;
			body.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(0.3f, h, len) },
				Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(d.X, d.Y)), new Vector3(mid.X, baseY + h * 0.5f, mid.Y)),
			});
		}
		void Loop(Vector2[] pts, float h = 3f, float baseY = -1f)
		{
			for (int i = 0; i < pts.Length - 1; i++) Wall(pts[i], pts[i + 1], h, baseY);
		}
		float hw = LakeShape.DockHalfWidth + 0.12f, end = LakeShape.DockEndZ - 0.1f, shore = LakeShape.NearShoreZ + 0.4f;
		// the clearing: from the waterline left of the dock, round the back, to the waterline right of it
		Loop(new[]
		{
			new Vector2(-hw, shore), new Vector2(-13f, shore + 0.4f), new Vector2(-16f, 9f), new Vector2(-11f, 20f),
			new Vector2(11f, 20f), new Vector2(16f, 9f), new Vector2(13f, shore + 0.4f), new Vector2(hw, shore),
		}, 4f, -1f);
		// the dock's sides and end: hip height above the deck
		float deck = LakeShape.DockDeck, railTop = deck + 0.6f;
		Wall(new Vector2(-hw, shore), new Vector2(-hw, end), railTop + 2f, -2f);
		Wall(new Vector2(-hw, end), new Vector2(hw, end), railTop + 2f, -2f);
		Wall(new Vector2(hw, end), new Vector2(hw, shore), railTop + 2f, -2f);

		// the far side: the beach where the boat lands, up round the station and back
		float fz = LakeShape.FarShoreZ;
		Loop(new[]
		{
			new Vector2(-9f, fz - 0.2f), new Vector2(9f, fz - 0.2f), new Vector2(14f, fz - 8f), new Vector2(20f, fz - 20f),
			new Vector2(18f, fz - 33f), new Vector2(-18f, fz - 33f), new Vector2(-20f, fz - 20f), new Vector2(-14f, fz - 8f),
			new Vector2(-9f, fz - 0.2f),
		}, 5f, -1f);
	}
}
