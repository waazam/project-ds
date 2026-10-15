using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// What collects along a neglected room's walls (the interiors pass, 2026-10-03, the owner: "objects, clutter, anything
/// that sticks out ... make all the interiors feel more natural"): crumbs of plaster or concrete fallen from the walls,
/// thickest right at their feet and thinning out across the floor, and a few scraps of paper. Small enough to walk
/// over (no collision); the same every time for a given seed.
/// </summary>
public static class DebrisKit
{
	private static StandardMaterial3D _crumb;
	private static StandardMaterial3D CrumbMat => _crumb ??= new StandardMaterial3D
	{
		ResourceName = "debris_crumb", AlbedoTexture = ProcTextures.ConcreteMat.AlbedoTexture, VertexColorUseAsAlbedo = true, Roughness = 0.95f,
		Uv1Triplanar = true, Uv1Scale = Vector3.One * 4f,
	};
	private static readonly Dictionary<string, Material> PaperRoles = new()
	{
		["paper"] = new StandardMaterial3D { ResourceName = "debris_paper", AlbedoColor = new Color(0.55f, 0.5f, 0.38f), Roughness = 0.95f },
	};

	/// <summary>In a room whose walls' inner faces are at x0, x1, z0, z1, its floor at <paramref name="y"/>: crumbs
	/// (tinted <paramref name="crumb"/>) along every wall's foot, and <paramref name="papers"/> scraps of paper. Spots in
	/// <paramref name="keepClear"/> (centre, radius: doorways, a puzzle) are left bare.</summary>
	public static void Scatter(Node3D parent, RandomNumberGenerator rng, float x0, float x1, float z0, float z1, float y, Color crumb,
		int crumbs = 60, int papers = 3, (Vector3 c, float r)[] keepClear = null)
	{
		var k = new MeshKit();
		k.Mat(CrumbMat);
		bool Clear(Vector3 p)
		{
			if (keepClear == null) return true;
			foreach (var (c, r) in keepClear)
				if (new Vector2(p.X - c.X, p.Z - c.Z).Length() < r) return false;
			return true;
		}
		for (int i = 0; i < crumbs; i++)
		{
			int wall = rng.RandiRange(0, 3);
			float off = 0.02f + Mathf.Pow(rng.Randf(), 2.2f) * 0.5f;   // most right at the wall's foot
			Vector3 p = wall switch
			{
				0 => new Vector3(x0 + off, y, rng.RandfRange(z0, z1)),
				1 => new Vector3(x1 - off, y, rng.RandfRange(z0, z1)),
				2 => new Vector3(rng.RandfRange(x0, x1), y, z0 + off),
				_ => new Vector3(rng.RandfRange(x0, x1), y, z1 - off),
			};
			if (!Clear(p)) continue;
			// a little heap now and then: three or four together
			int n = rng.Randf() < 0.25f ? rng.RandiRange(3, 5) : 1;
			for (int j = 0; j < n; j++)
			{
				float s = rng.RandfRange(0.012f, 0.05f) * (j == 0 ? 1f : 0.7f);
				var q = p + new Vector3(rng.RandfRange(-0.06f, 0.06f), 0, rng.RandfRange(-0.06f, 0.06f)) * (j == 0 ? 0f : 1f);
				var col = crumb * rng.RandfRange(0.75f, 1.1f);
				float zr = rng.RandfRange(0.7f, 1.3f);
				var bs = Basis.FromEuler(new Vector3(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(0f, Mathf.Tau), rng.RandfRange(-0.3f, 0.3f)));
				var at = q + Vector3.Up * (s * 0.32f + 0.002f);
				// (2026-10-15) a broken lump modelled in Blender (tools/Blender/kits.py db_crumb_0..3, about 1 x 0.6 x 1, sized
				// here); the box is its fallback
				if (FurnitureKit.Add(k, "db_crumb_" + ((i * 3 + j) & 3), new Transform3D(bs * Basis.FromScale(new Vector3(s, s, s * zr)), at),
					FurnitureKit.Tinted(("crumb", CrumbMat, col))))
					continue;
				k.Mat(CrumbMat);
				k.Color = col;
				k.Box(at, new Vector3(s, s * 0.6f, s * zr), 1f, bs);
			}
		}
		// (2026-10-15) the scraps keep out from under what stands in the room (they sat inside bed and table legs): the
		// room's standing colliders, as built so far under parent; each scrap its own size and shape (one mesh, 14 times)
		var standing = Standing(parent, y);
		for (int i = 0; i < papers; i++)
		{
			var p = new Vector3(rng.RandfRange(x0 + 0.3f, x1 - 0.3f), y + 0.002f, rng.RandfRange(z0 + 0.3f, z1 - 0.3f));
			float yaw = rng.RandfRange(0f, Mathf.Tau);
			var scale = new Vector3(rng.RandfRange(0.7f, 1.25f), 1f, rng.RandfRange(0.7f, 1.25f));
			bool under = false;
			foreach (var b in standing) if (b.Grow(0.25f).HasPoint(p with { Y = b.GetCenter().Y })) { under = true; break; }
			if (Clear(p) && !under) FurnitureKit.Add(k, "papers", p, yaw, PaperRoles, scale);
		}
		k.CommitTo(parent, "Debris", false);
	}

	/// <summary>The boxes (in <paramref name="parent"/>'s space) of the colliders under it that stand on the floor at
	/// <paramref name="y"/>: furniture, not the room (a box under 3.5 m across, from the floor up).</summary>
	private static List<Aabb> Standing(Node3D parent, float y)
	{
		var list = new List<Aabb>();
		foreach (var n in parent.FindChildren("*", "CollisionShape3D", true, false))
		{
			var cs = (CollisionShape3D)n;
			if (cs.Shape == null || cs.Disabled) continue;
			// its transform relative to parent (it may not be in the tree yet)
			var xf = Transform3D.Identity;
			Node a = cs;
			while (a != null && a != parent) { if (a is Node3D a3) xf = a3.Transform * xf; a = a.GetParent(); }
			if (a != parent) continue;
			Aabb local = cs.Shape switch
			{
				BoxShape3D bs => new Aabb(-bs.Size * 0.5f, bs.Size),
				CylinderShape3D cy => new Aabb(new Vector3(-cy.Radius, -cy.Height * 0.5f, -cy.Radius), new Vector3(cy.Radius * 2f, cy.Height, cy.Radius * 2f)),
				CapsuleShape3D cp => new Aabb(new Vector3(-cp.Radius, -cp.Height * 0.5f, -cp.Radius), new Vector3(cp.Radius * 2f, cp.Height, cp.Radius * 2f)),
				_ => new Aabb(),
			};
			if (local.Size == Vector3.Zero) continue;
			var box = xf * local;
			if (box.Size.X > 3.5f || box.Size.Z > 3.5f || box.Position.Y > y + 0.6f || box.End.Y < y + 0.15f) continue;
			list.Add(box);
		}
		return list;
	}
}
