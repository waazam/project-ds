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
				k.Color = crumb * rng.RandfRange(0.75f, 1.1f);
				k.Box(q + Vector3.Up * (s * 0.32f + 0.002f), new Vector3(s, s * 0.6f, s * rng.RandfRange(0.7f, 1.3f)), 1f,
					Basis.FromEuler(new Vector3(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(0f, Mathf.Tau), rng.RandfRange(-0.3f, 0.3f))));
			}
		}
		for (int i = 0; i < papers; i++)
		{
			var p = new Vector3(rng.RandfRange(x0 + 0.3f, x1 - 0.3f), y + 0.002f, rng.RandfRange(z0 + 0.3f, z1 - 0.3f));
			if (Clear(p)) FurnitureKit.Add(k, "papers", p, rng.RandfRange(0f, Mathf.Tau), PaperRoles);
		}
		k.CommitTo(parent, "Debris", false);
	}
}
