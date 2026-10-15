using System.Collections.Generic;
using Godot;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World.SnowMaze;

/// <summary>The trenches' look and their materials (the owner's references: trenches cut in snow, their walls shored up
/// with log posts and planks behind them, snow on every top; "old ww2 helmets, guns, skeletons in military ww2 helmets and
/// gear, and weapons crates").</summary>
public static class SnowMazeDressing
{
	private static Dictionary<string, Material> _roles;
	private static StandardMaterial3D _post, _plank;
	public static StandardMaterial3D TrenchPostMat => _post ??= Aged(BuildingTextures.LogMat, "trench_post", new Color(0.4f, 0.37f, 0.34f));
	public static StandardMaterial3D TrenchPlankMat => _plank ??= Aged(BuildingTextures.BoardsMat, "trench_plank", new Color(0.36f, 0.34f, 0.31f));
	private static StandardMaterial3D Aged(Material from, string name, Color tint)
	{
		var m = (StandardMaterial3D)((StandardMaterial3D)from).Duplicate();
		m.ResourceName = name;
		m.AlbedoColor = tint;
		m.Roughness = 0.95f;
		m.MetallicSpecular = 0.2f;
		return m;
	}

	public static Dictionary<string, Material> Roles => _roles ??= new Dictionary<string, Material>
	{
		["olive"] = new StandardMaterial3D { ResourceName = "war_olive", AlbedoColor = new Color(0.25f, 0.27f, 0.17f), Roughness = 0.7f, Metallic = 0.2f, AlbedoTexture = ProcTextures.Grime() },
		["steel"] = new StandardMaterial3D { ResourceName = "war_steel", AlbedoColor = new Color(0.2f, 0.2f, 0.21f), Roughness = 0.55f, Metallic = 0.6f, AlbedoTexture = ProcTextures.Grime() },
		["brass"] = LodgeTextures.BrassMat, ["rubber"] = LodgeTextures.BlackMat, ["grip"] = LodgeTextures.BlackMat,
		["soot"] = new StandardMaterial3D { ResourceName = "war_soot", AlbedoColor = new Color(0.05f, 0.045f, 0.04f), Roughness = 0.9f },
		["crate"] = new StandardMaterial3D { ResourceName = "war_crate", AlbedoColor = new Color(0.2f, 0.21f, 0.13f), Roughness = 0.92f, AlbedoTexture = BuildingTextures.BoardsMat.AlbedoTexture },
		["stencil"] = new StandardMaterial3D { ResourceName = "war_stencil", AlbedoColor = new Color(0.7f, 0.6f, 0.2f), Roughness = 0.9f },
		["straw"] = new StandardMaterial3D { ResourceName = "war_straw", AlbedoColor = new Color(0.5f, 0.42f, 0.22f), Roughness = 1f, AlbedoTexture = ProcTextures.Grime() },
		["coat"] = new StandardMaterial3D { ResourceName = "war_coat", AlbedoColor = new Color(0.2f, 0.19f, 0.15f), Roughness = 1f, AlbedoTexture = LodgeTextures.LinenMat.AlbedoTexture, Uv1Scale = Vector3.One * 3f },
		["bone"] = new StandardMaterial3D { ResourceName = "war_bone", AlbedoColor = new Color(0.66f, 0.62f, 0.52f), Roughness = 0.7f, AlbedoTexture = ProcTextures.Grime() },
		["leather"] = LodgeTextures.LeatherMat,
		["wood"] = LodgeTextures.DarkWoodMat,
		["rope"] = new StandardMaterial3D { ResourceName = "war_rope", AlbedoColor = new Color(0.45f, 0.38f, 0.27f), Roughness = 1f },
		["iron"] = LodgeTextures.IronMat,
		["rust"] = new StandardMaterial3D { ResourceName = "war_rust", AlbedoColor = new Color(0.3f, 0.17f, 0.1f), Roughness = 0.9f },
		["sack"] = new StandardMaterial3D { ResourceName = "war_sack", AlbedoColor = new Color(0.42f, 0.38f, 0.29f), Roughness = 1f, AlbedoTexture = LodgeTextures.LinenMat.AlbedoTexture, Uv1Scale = Vector3.One * 2f },
	};
}

public partial class SnowMazeCave
{
	public int Posts { get; private set; }

	/// <summary>The trenches dressed: shored with log posts and planks, duckboards along their floors, sandbags along their
	/// lips, ramps up out of their ends; the war left in them; and where the crowbar lies and where the crate stands.</summary>
	public void DressTrenches(out Vector3 crowbarAt, out Vector3 crateAt, out float crateYaw)
	{
		var rng = new RandomNumberGenerator { Seed = 2427 };
		var dr = new RandomNumberGenerator { Seed = 2428 };   // (the shoring's and the sandbags' own variety)
		// (old wood, eighty winters in the ice: grey and dry, frost in its grain; the lodge's polished wood and the cabin's
		// fresh logs read new and orange under the lantern)
		var logs = new MeshKit(); logs.Mat(SnowMazeDressing.TrenchPostMat); logs.Color = Colors.White;
		var planks = new MeshKit(); planks.Mat(SnowMazeDressing.TrenchPlankMat); planks.Color = Colors.White;
		var snow = new MeshKit(); snow.Mat(WinterWoods.SoftSnow); snow.Color = Colors.White;
		var props = new MeshKit(); props.Color = Colors.White;
		var bags = new MeshKit(); bags.Mat(SnowMazeDressing.Roles["sack"]); bags.Color = Colors.White;
		var body = new StaticBody3D { Name = "TrenchBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		float w = TrenchHalfW - 0.06f, top = 0.25f, floor = -TrenchDepth;
		var spots = new List<(Vector3 at, Vector3 along, Vector3 side)>();
		for (int ti = 0; ti < Trenches.Count; ti++)
		{
			var (a, b) = Trenches[ti];
			var d = (b - a) with { Y = 0 };
			float len = d.Length();
			var along = d / len;
			var side = new Vector3(along.Z, 0, -along.X);
			var basis = Basis.LookingAt(along, Vector3.Up);
			// (nothing where another trench opens off this one: the shoring there would wall the way through)
			bool Open(Vector3 q) => InOtherTrench(q, ti);
			// posts both sides, every metre or so; planks stacked behind them; snow on the tops that stand clear of the lip.
			// (2026-10-15, the scan: posts at an even pitch, all standing a hand over the lip with one cap of snow each, read
			// as a castle's battlements) Spaced as they were driven, not measured; most cut off near the lip, some rotted
			// down below it, a few standing tall; leaning in where the snow behind has pushed; one here and there split, or gone.
			for (float s = 0.3f + dr.RandfRange(0f, 0.4f); s < len - 0.3f; s += dr.RandfRange(0.7f, 1.75f))
				foreach (float sd in new[] { -1f, 1f })
				{
					if (dr.Randf() < 0.12f) continue;   // (rotted away)
					var p = a + along * (s + dr.RandfRange(-0.18f, 0.18f)) + side * sd * w;
					if (Open(p)) continue;
					float r = dr.RandfRange(0.07f, 0.125f);
					float roll = dr.Randf();
					float postTop = roll < 0.28f ? top + dr.RandfRange(-0.5f, -0.18f)
						: roll < 0.86f ? top + dr.RandfRange(-0.14f, 0.07f)
						: top + dr.RandfRange(0.18f, 0.55f);
					var lean = -side * sd * dr.RandfRange(0f, 0.09f) + along * dr.RandfRange(-0.05f, 0.05f);
					var foot = p with { Y = floor };
					var tip = (p with { Y = postTop }) + lean * (postTop - floor);
					logs.Cylinder(foot, tip, r, r * dr.RandfRange(0.82f, 0.97f), 8, true, 0.5f);
					var up = (tip - foot).Normalized();
					if (dr.Randf() < 0.22f)
					{
						// split at the top: a splinter of it standing on past the break
						var off = up.Cross(along).Normalized() * r * 0.45f * (dr.Randf() < 0.5f ? -1f : 1f);
						logs.Cylinder(tip - up * 0.04f + off, tip + up * dr.RandfRange(0.1f, 0.24f) + off + lean * 0.5f, r * 0.42f, r * 0.08f, 5, false, 0.5f);
					}
					// (2026-10-14d: the cap sits on its own post's top, a little down over it) only on the tops out in the open,
					// and not on all of them, never the same size
					if (postTop > top - 0.2f && dr.Randf() < 0.7f)
					{
						float cr = r * dr.RandfRange(0.9f, 1.35f);
						snow.Blob(tip + up * dr.RandfRange(0.012f, 0.025f) + lean.Normalized() * 0.01f, new Vector3(cr, dr.RandfRange(0.03f, 0.07f), cr * dr.RandfRange(0.85f, 1.15f)), (int)(s * 31f) + (sd > 0 ? 7 : 0), 0.32f, true, 1f);
					}
					Posts++;
				}
			// the planks behind the posts, and the walls' collision, a metre at a time
			for (float s = 0f; s < len; s += 1f)
			{
				float l = Mathf.Min(1f, len - s);
				foreach (float sd in new[] { -1f, 1f })
				{
					var c0 = a + along * (s + l * 0.5f) + side * sd * (w + 0.06f);
					if (Open(c0)) continue;
					// (no two bays alike: the top boards gone from some, the rest a little out of true, sprung, warped)
					int rows = dr.Randf() < 0.3f ? dr.RandiRange(5, 6) : 7;
					for (int row = 0; row < rows; row++)
					{
						var tilt = basis * new Basis(Vector3.Right, dr.RandfRange(-0.025f, 0.025f)) * new Basis(Vector3.Up, dr.RandfRange(-0.02f, 0.02f));
						var pc = c0 + side * sd * dr.RandfRange(-0.012f, 0.012f) + Vector3.Up * (floor + 0.17f + row * 0.29f + dr.RandfRange(-0.015f, 0.015f));
						planks.Box(pc, new Vector3(0.05f, dr.RandfRange(0.22f, 0.27f), l - dr.RandfRange(0.02f, 0.08f)), 1f, tilt);
					}
					if (rows < 7 && dr.Randf() < 0.5f)
					{
						// one of the missing boards fallen into the trench, lying along the duckboards' edge
						var lb = basis * new Basis(Vector3.Up, dr.RandfRange(-0.12f, 0.12f)) * new Basis(Vector3.Forward, sd * dr.RandfRange(0.04f, 0.1f));
						planks.Box(c0 - side * sd * 0.36f + Vector3.Up * (floor + 0.13f), new Vector3(0.24f, 0.035f, l * dr.RandfRange(0.6f, 0.9f)), 1f, lb);
					}
					body.AddChild(new CollisionShape3D { Position = c0 + side * sd * 0.04f + Vector3.Up * (floor + 1.1f), Basis = basis, Shape = new BoxShape3D { Size = new Vector3(0.1f, 2.2f, l) } });
				}
			}
			// duckboards: two rails and slats along the floor
			foreach (float sd in new[] { -0.28f, 0.28f })
				planks.Box(a + along * (len * 0.5f) + side * sd + Vector3.Up * (floor + 0.05f), new Vector3(0.06f, 0.06f, len - 0.2f), 1f, basis);
			for (float s = 0.2f; s < len - 0.2f; s += 0.22f)
				planks.Box(a + along * s + Vector3.Up * (floor + 0.1f), new Vector3(0.74f, 0.03f, 0.12f), 1f, basis);
			// the floor's collision (duckboards); the revetments' (so nothing walks into the planks)
			body.AddChild(new CollisionShape3D { Position = a + along * (len * 0.5f) + Vector3.Up * (floor + 0.06f), Basis = basis, Shape = new BoxShape3D { Size = new Vector3(1.2f, 0.12f, len) } });
			// sandbags along the lip, here and there. (2026-10-15: the one model, a neat 4-3-2 pyramid square to the trench every
			// few metres, was the battlement's merlons) Each heap its own: two to seven bags, slumped and frozen into each other,
			// set on the ground where it is, at their own angles, one sometimes tumbled down into the trench; snow on them.
			for (float s = 1f + dr.RandfRange(0f, 1.5f); s < len - 1f; s += dr.RandfRange(2.2f, 5.5f))
			{
				float sd = dr.Randf() < 0.5f ? -1f : 1f;
				var c = a + along * s + side * sd * (w + dr.RandfRange(0.45f, 0.7f));
				if (Open(c) || Open(c - side * sd * 0.6f)) continue;
				SandbagHeap(bags, snow, c, along, side * sd, LipY(c), floor, dr);
			}
			// (2026-10-15) the old shoring's draws, still taken from the trenches' own sequence: the dressing above draws from
			// its own, so where the war's leavings, the crowbar and the crate lie is exactly where it was
			for (float s = 0.3f; s < len - 0.3f; s += rng.RandfRange(0.95f, 1.3f))
				foreach (float sd in new[] { -1f, 1f })
				{
					if (Open(a + along * s + side * sd * w)) continue;
					rng.RandfRange(0.08f, 0.115f);
					rng.RandfRange(-0.05f, 0.12f);
				}
			for (float s = 1f; s < len - 1f; s += rng.RandfRange(2.5f, 4.5f)) rng.Randf();
			for (float s = 1.2f; s < len - 1.2f; s += rng.RandfRange(1.6f, 3f))
				spots.Add((a + along * s + Vector3.Up * (floor + 0.12f), along, side));
		}
		// ramps up out of each line's ends (on the cavern's floor side), so they can be got into and out of
		var ends = new List<(Vector3 at, Vector3 dir)>();
		var counts = new Dictionary<Vector3, int>();
		foreach (var (a, b) in Trenches) { counts[a] = counts.GetValueOrDefault(a) + 1; counts[b] = counts.GetValueOrDefault(b) + 1; }
		foreach (var (a, b) in Trenches)
		{
			if (counts[a] == 1) ends.Add((a, (a - b).Normalized()));
			if (counts[b] == 1) ends.Add((b, (b - a).Normalized()));
		}
		foreach (var (at, dir) in ends)
		{
			if (at.IsEqualApprox(Trenches[^1].b)) continue;   // (the crate's spur: a dead end, the crate across it)
			// a slope from the trench's floor to the cavern's, running on out of the trench's end
			var basis = Basis.LookingAt(dir, Vector3.Up);
			float run = 3.4f, ang = Mathf.Atan2(TrenchDepth, run);
			var mid = at - dir * 0.6f + dir * (run * 0.5f) + Vector3.Up * (floor * 0.5f);
			var tilt = basis * new Basis(Vector3.Right, ang);
			planks.Box(mid, new Vector3(1.2f, 0.08f, run / Mathf.Cos(ang)), 1f, tilt);
			body.AddChild(new CollisionShape3D { Position = mid - tilt.Y * 0.04f, Basis = tilt, Shape = new BoxShape3D { Size = new Vector3(1.4f, 0.12f, run / Mathf.Cos(ang)) } });
		}
		logs.CommitTo(this, "TrenchPosts", false);
		planks.CommitTo(this, "TrenchPlanks", false);
		snow.CommitTo(this, "TrenchSnow", false);
		bags.CommitTo(this, "TrenchSandbags", false);
		// the war's leavings, at the spots along the trenches' floors
		for (int k = spots.Count - 1; k > 0; k--) { int r = rng.RandiRange(0, k); (spots[k], spots[r]) = (spots[r], spots[k]); }
		crowbarAt = spots.Count > 0 ? spots[0].at : CavernCentre();
		// the crate: the far end of the farthest trench from the way in
		var cc = SnowMazeLayout.CavernCentre;
		crateAt = Trenches[^1].b + Vector3.Up * (floor + 0.12f) + (Trenches[^1].a - Trenches[^1].b).Normalized() * 1.2f;
		crateYaw = Mathf.Atan2((Trenches[^1].b - Trenches[^1].a).X, (Trenches[^1].b - Trenches[^1].a).Z) + Mathf.Pi * 0.5f;
		string[] things = { "soldier_remains", "helmet_m1", "rifle", "weapon_crate", "helmet_stahl", "soldier_remains", "rifle", "helmet_m1", "weapon_crate", "soldier_remains", "helmet_stahl", "rifle" };
		for (int k = 1; k < spots.Count && k <= things.Length; k++)
		{
			var (at, along, side) = spots[k];
			string t = things[k - 1];
			float sd = rng.Randf() < 0.5f ? -1f : 1f;
			var yaw = Basis.LookingAt(side * -sd, Vector3.Up);
			var pos = t == "soldier_remains" ? at + side * sd * (w - 0.18f) : t == "weapon_crate" ? at + side * sd * (w - 0.3f) : at + side * sd * rng.RandfRange(0.1f, 0.4f);
			var xf = new Transform3D(yaw * new Basis(Vector3.Up, rng.RandfRange(-0.3f, 0.3f)), pos);
			if (t is "rifle") xf = new Transform3D(new Basis(Vector3.Up, rng.Randf() * 6.28f) * new Basis(Vector3.Forward, 1.57f), pos + Vector3.Up * 0.02f);
			if (t is "helmet_m1" or "helmet_stahl" && rng.Randf() < 0.5f) xf = new Transform3D(new Basis(Vector3.Right, Mathf.Pi * rng.RandfRange(0.4f, 1f)) * new Basis(Vector3.Up, rng.Randf() * 6f), pos + Vector3.Up * 0.08f);
			FurnitureKit.Add(props, t, xf, SnowMazeDressing.Roles);
		}
		// and up on the cavern's floor: a few crates stacked, more of the dead, a helmet on a post
		for (int k = 0; k < 6; k++)
		{
			float a = rng.Randf() * Mathf.Tau, r = rng.RandfRange(0.55f, 0.85f);
			var p = cc + new Vector3(Mathf.Cos(a) * r * SnowMazeLayout.CavernRX, 0f, Mathf.Sin(a) * r * SnowMazeLayout.CavernRZ);
			if (Field(p + Vector3.Up * 0.5f) > 0f || NearTrench(p, 1.6f)) continue;
			FurnitureKit.Add(props, k % 2 == 0 ? "weapon_crate" : "soldier_remains", new Transform3D(new Basis(Vector3.Up, rng.Randf() * 6.28f), p), SnowMazeDressing.Roles);
		}
		props.CommitTo(this, "TrenchProps", false);
	}

	private static Vector3 CavernCentre() => SnowMazeLayout.CavernCentre;

	/// <summary>The snow's surface over a local point (the field's zero going down from a little over it): where a thing set on
	/// the cavern's floor beside a trench actually rests.</summary>
	private float LipY(Vector3 p)
	{
		for (float y = 0.8f; y > -0.8f; y -= 0.04f)
			if (Field(p with { Y = y }) > 0f) return y + 0.02f;
		return 0f;
	}

	/// <summary>A heap of frozen sandbags on a trench's lip (<paramref name="toLip"/> points away from the trench): a bottom row
	/// of two to four, fewer over them, each bag its own size and turn, sunk into the ones under it; now and then one fallen
	/// down onto the duckboards. Snow over the top of the heap.</summary>
	private void SandbagHeap(MeshKit bags, MeshKit snow, Vector3 c, Vector3 along, Vector3 toLip, float ground, float floor, RandomNumberGenerator rng)
	{
		int seed = rng.RandiRange(1, 99999);
		int bottom = rng.RandiRange(2, 4);
		int rowsUp = rng.Randf() < 0.35f ? 0 : rng.Randf() < 0.7f ? 1 : 2;
		float heapYaw = rng.RandfRange(-0.3f, 0.3f);
		var hb = new Basis(Vector3.Up, heapYaw);
		var ax = hb * along;
		var az = hb * toLip;
		float topY = ground;
		for (int row = 0; row <= rowsUp; row++)
		{
			int n = Mathf.Max(1, bottom - row - (rng.Randf() < 0.3f ? 1 : 0));
			float y = ground - 0.03f + row * rng.RandfRange(0.11f, 0.14f);
			for (int i = 0; i < n; i++)
			{
				float x = (i - (n - 1) * 0.5f) * rng.RandfRange(0.42f, 0.52f) + row * rng.RandfRange(-0.08f, 0.08f);
				var at = c + ax * x + az * rng.RandfRange(-0.06f, 0.06f) + Vector3.Up * (y + 0.06f);
				var bb = new Basis(Vector3.Up, rng.RandfRange(-0.25f, 0.25f)) * new Basis(Vector3.Forward, rng.RandfRange(-0.12f, 0.12f));
				var keep = bags.Xf;
				bags.Xf = new Transform3D(new Basis(ax, Vector3.Up, ax.Cross(Vector3.Up)) * bb, at);
				bags.Blob(Vector3.Zero, new Vector3(rng.RandfRange(0.21f, 0.27f), rng.RandfRange(0.065f, 0.09f), rng.RandfRange(0.13f, 0.17f)), seed + row * 10 + i, 0.16f, false, 1f, 0.35f, 2);
				bags.Xf = keep;
				topY = Mathf.Max(topY, y + 0.13f);
			}
		}
		// the snow settled on the heap
		snow.Blob(c + Vector3.Up * (topY - 0.02f) + az * 0.03f, new Vector3(0.24f * bottom * rng.RandfRange(0.8f, 1f), 0.05f, 0.16f), seed + 77, 0.3f, true, 1f);
		if (rng.Randf() < 0.25f)
		{
			// one tumbled into the trench
			var at = c - az * (rng.RandfRange(0.95f, 1.2f)) + ax * rng.RandfRange(-0.4f, 0.4f);
			var keep = bags.Xf;
			bags.Xf = new Transform3D(new Basis(Vector3.Up, rng.Randf() * Mathf.Tau) * new Basis(Vector3.Forward, rng.RandfRange(-0.2f, 0.2f)), at with { Y = floor + 0.115f + 0.07f });
			bags.Blob(Vector3.Zero, new Vector3(0.24f, 0.075f, 0.15f), seed + 99, 0.16f, false, 1f, 0.35f, 2);
			bags.Xf = keep;
		}
	}

	/// <summary>Inside the channel of a trench other than <paramref name="except"/> (where it opens off this one).</summary>
	public bool InOtherTrench(Vector3 p, int except)
	{
		for (int i = 0; i < Trenches.Count; i++)
		{
			if (i == except) continue;
			var (a, b) = Trenches[i];
			var ab = (b - a) with { Y = 0 };
			float t = Mathf.Clamp(((p - a) with { Y = 0 }).Dot(ab) / ab.LengthSquared(), 0f, 1f);
			if (((a + ab * t - p) with { Y = 0 }).Length() < TrenchHalfW + 0.05f) return true;
		}
		return false;
	}

	/// <summary>Within <paramref name="r"/> of a trench's line.</summary>
	public bool NearTrench(Vector3 p, float r)
	{
		foreach (var (a, b) in Trenches)
		{
			var ab = b - a;
			float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
			if ((a + ab * t - p).Length() < r) return true;
		}
		return false;
	}
}
