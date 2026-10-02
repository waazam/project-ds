using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.LodgeParts;

/// <summary>
/// Building blocks for the lodge's interior: walls with a finish on each face and holes cut for doors and
/// windows (collision to match), floors and ceilings, and the furniture (sofas, armchairs, beds, nightstands,
/// wardrobes, lamps, chandeliers, tables, chairs).
/// </summary>
public static class LodgeKit
{
	/// <summary>A hole in a wall: along it from u0 to u1 (metres from its start), from v0 to v1 up it.</summary>
	public readonly record struct Hole(float U0, float U1, float V0, float V1);

	/// <summary>A wall from a to b (floor points), from y0 to y1, <paramref name="thick"/> thick. The face toward
	/// <paramref name="side"/> is finished in <paramref name="matA"/>, the other in <paramref name="matB"/>; the
	/// edges round the holes (door and window reveals) and the top in <paramref name="edge"/>. Colliders go on
	/// <paramref name="body"/>. UVs in metres (x along, y up).</summary>
	public static void Wall(MeshKit k, StaticBody3D body, Vector3 a, Vector3 b, float y0, float y1, float thick, Vector3 side,
		Material matA, Material matB, Material edge, IList<Hole> holes = null, float uvScale = 1f, bool occlude = true,
		float floorY = float.NaN, int grime = 0, bool skirt = false, float ceilingY = float.NaN)
	{
		Vector3 along = (b - a).Normalized();
		float len = a.DistanceTo(b);
		Vector3 n = side.Normalized();
		// keep n square to the wall
		n = (n - along * n.Dot(along)).Normalized();
		var hs = new List<Hole>();
		if (holes != null) foreach (var h in holes) hs.Add(new Hole(Mathf.Clamp(h.U0, 0, len), Mathf.Clamp(h.U1, 0, len), Mathf.Max(h.V0, y0), Mathf.Min(h.V1, y1)));
		// vertical strips between hole edges
		var cuts = new SortedSet<float> { 0f, len };
		foreach (var h in hs) { cuts.Add(h.U0); cuts.Add(h.U1); }
		var cl = new List<float>(cuts);
		Vector3 a0 = new(a.X, 0, a.Z);
		Vector3 P(float u, float y, float s) => a0 + along * u + Vector3.Up * y + n * s;
		for (int i = 0; i < cl.Count - 1; i++)
		{
			float u0 = cl[i], u1 = cl[i + 1];
			if (u1 - u0 < 0.001f) continue;
			// the solid spans in this strip
			var spans = new List<(float, float)> { (y0, y1) };
			foreach (var h in hs)
			{
				if (h.U0 >= u1 - 0.0005f || h.U1 <= u0 + 0.0005f) continue;
				var next = new List<(float, float)>();
				foreach (var (s0, s1) in spans)
				{
					if (h.V1 <= s0 || h.V0 >= s1) { next.Add((s0, s1)); continue; }
					if (h.V0 > s0) next.Add((s0, h.V0));
					if (h.V1 < s1) next.Add((h.V1, s1));
				}
				spans = next;
			}
			foreach (var (s0, s1) in spans)
			{
				if (s1 - s0 < 0.001f) continue;
				float t = thick * 0.5f;
				// the grime along its foot where it stands on a floor (grime: 1 the side face, -1 the other, 2 both)
				if (grime != 0 && !float.IsNaN(floorY) && Mathf.Abs(s0 - y0) < 0.001f && Mathf.Abs(y0 - floorY) < 0.1f)
				{
					if (grime > 0) World.Weathering.ContactStrips(k, P(u0, floorY, t), P(u1, floorY, t), n, floorY);
					if (grime < 0 || grime == 2) World.Weathering.ContactStrips(k, P(u1, floorY, -t), P(u0, floorY, -t), -n, floorY);
					// a skirting board along its foot (the look pass, 2026-10-02: the walls met the floor bare)
					if (skirt)
					{
						if (grime > 0) Trim(k, P(u0, floorY, t), P(u1, floorY, t), n, 0.14f, 0.016f, true);
						if (grime < 0 || grime == 2) Trim(k, P(u1, floorY, -t), P(u0, floorY, -t), -n, 0.14f, 0.016f, true);
					}
				}
				// a crown moulding where it meets the ceiling
				if (grime != 0 && !float.IsNaN(ceilingY) && Mathf.Abs(s1 - y1) < 0.001f && Mathf.Abs(y1 - ceilingY) < 0.1f)
				{
					if (grime > 0) Trim(k, P(u0, ceilingY, t), P(u1, ceilingY, t), n, 0.11f, 0.05f, false);
					if (grime < 0 || grime == 2) Trim(k, P(u1, ceilingY, -t), P(u0, ceilingY, -t), -n, 0.11f, 0.05f, false);
				}
				// (a span big enough to hide a room behind it: an occluder in the wall's middle, a hair inside its edges)
				if (occlude && u1 - u0 >= 0.8f && s1 - s0 >= 0.8f)
					k.Occlude(P(u0 + 0.03f, s0 + 0.03f, 0f), P(u1 - 0.03f, s0 + 0.03f, 0f), P(u1 - 0.03f, s1 - 0.03f, 0f), P(u0 + 0.03f, s1 - 0.03f, 0f));
				// the two faces
				k.Mat(matA);
				k.Quad(P(u0, s0, t), P(u1, s0, t), P(u1, s1, t), P(u0, s1, t), n, new Vector2(u0, s0) * uvScale, new Vector2(u1, s0) * uvScale, new Vector2(u1, s1) * uvScale, new Vector2(u0, s1) * uvScale);
				k.Mat(matB);
				k.Quad(P(u1, s0, -t), P(u0, s0, -t), P(u0, s1, -t), P(u1, s1, -t), -n, new Vector2(u1, s0) * uvScale, new Vector2(u0, s0) * uvScale, new Vector2(u0, s1) * uvScale, new Vector2(u1, s1) * uvScale);
				// the edges: where a hole (or the wall's end) bounds this span
				k.Mat(edge);
				bool Faces(float u) => u <= 0.0005f || u >= len - 0.0005f || HoleEdge(hs, u, s0, s1);
				if (Faces(u0)) k.Quad(P(u0, s0, -t), P(u0, s0, t), P(u0, s1, t), P(u0, s1, -t), -along);
				if (Faces(u1)) k.Quad(P(u1, s0, t), P(u1, s0, -t), P(u1, s1, -t), P(u1, s1, t), along);
				k.Quad(P(u0, s1, t), P(u1, s1, t), P(u1, s1, -t), P(u0, s1, -t), Vector3.Up);
				if (s0 > y0 + 0.0005f) k.Quad(P(u0, s0, -t), P(u1, s0, -t), P(u1, s0, t), P(u0, s0, t), Vector3.Down);
				if (body != null)
				{
					var c = a0 + along * ((u0 + u1) * 0.5f) + Vector3.Up * ((s0 + s1) * 0.5f);
					body.AddChild(new CollisionShape3D
					{
						Position = c, Basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up).Normalized()),
						Shape = new BoxShape3D { Size = new Vector3(u1 - u0, s1 - s0, thick) },
					});
				}
			}
		}
	}

	/// <summary>A run of trim along a wall's face from <paramref name="a"/> to <paramref name="b"/> (on the face, at the
	/// floor or the ceiling): a skirting board standing up from the floor (a board, its top edge eased), or a crown moulding
	/// hanging from the ceiling (stepped out in two). Dark wood.</summary>
	public static void Trim(MeshKit k, Vector3 a, Vector3 b, Vector3 n, float height, float proud, bool floor)
	{
		float len = a.DistanceTo(b);
		if (len < 0.05f) return;
		var along = (b - a) / len;
		// (its ends 3 mm short: flush, they lay in the planes of the door casings and the furniture against the walls)
		len -= 0.006f;
		var basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up).Normalized());
		if (basis.Z.Dot(n) < 0) basis = new Basis(-along, Vector3.Up, -along.Cross(Vector3.Up).Normalized());
		// (2 mm off the floor or the ceiling: flush with it, its face lay in the floor's plane and the things standing on
		// it, and fought them; the clip audit found it)
		var mid = (a + b) * 0.5f + Vector3.Up * (floor ? 0.002f : -0.002f) - n * 0.002f;   // (its back 2 mm into the wall, out of the wall's face's plane)
		var mat = k.CurrentMaterial;
		k.Mat(LodgeTextures.DarkWoodMat);
		var c = k.Color;
		k.Color = Colors.White;
		float sg = floor ? 1f : -1f;
		if (floor)
		{
			k.Box(mid + n * (proud * 0.5f) + Vector3.Up * (height * 0.5f - 0.02f), new Vector3(len, height - 0.04f, proud), 1f, basis);
			k.Box(mid + n * (proud * 0.35f) + Vector3.Up * (height - 0.02f), new Vector3(len, 0.04f, proud * 0.7f), 1f, basis);   // the eased top
		}
		else
		{
			k.Box(mid + n * (proud * 0.25f) + Vector3.Up * (sg * height * 0.25f), new Vector3(len, height * 0.5f, proud * 0.5f), 1f, basis);
			k.Box(mid + n * (proud * 0.5f) + Vector3.Up * (sg * height * 0.12f), new Vector3(len, height * 0.24f, proud), 1f, basis);
		}
		k.Color = c;
		if (mat != null) k.Mat(mat);
	}

	private static bool HoleEdge(List<Hole> hs, float u, float s0, float s1)
	{
		foreach (var h in hs)
			if ((Mathf.Abs(h.U0 - u) < 0.0005f || Mathf.Abs(h.U1 - u) < 0.0005f) && h.V0 < s1 && h.V1 > s0) return true;
		return false;
	}

	/// <summary>A horizontal rectangle (floor if <paramref name="up"/>, ceiling otherwise), UVs in metres times <paramref name="uv"/>.</summary>
	public static void Flat(MeshKit k, Material m, float x0, float x1, float z0, float z1, float y, bool up, float uv = 1f)
	{
		k.Mat(m);
		if (up) k.Quad(new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), new Vector3(x0, y, z0), Vector3.Up, new Vector2(x0, z1) * uv, new Vector2(x1, z1) * uv, new Vector2(x1, z0) * uv, new Vector2(x0, z0) * uv);
		else k.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.Down, new Vector2(x0, z0) * uv, new Vector2(x1, z0) * uv, new Vector2(x1, z1) * uv, new Vector2(x0, z1) * uv);
	}

	public static void Solid(StaticBody3D body, Vector3 c, Vector3 size, float yaw = 0f)
		=> body.AddChild(new CollisionShape3D { Position = c, Rotation = new Vector3(0, yaw, 0), Shape = new BoxShape3D { Size = size } });

	// ---------------------------------------------------------------- furniture (in the parent's space; c on the floor)

	/// <summary>A chesterfield sofa facing -Z (turn it with <paramref name="yaw"/>): leather, rolled arms, buttoned back.</summary>
	public static void Sofa(MeshKit k, Vector3 c, float yaw, Material mat, float w = 2.1f)
	{
		// the modelled chesterfield (deep-buttoned, rolled and pleated arms, piped cushions, bun feet), at its nearer width
		string model = w > 2.25f ? "chesterfield_24" : "chesterfield_21";
		float made = w > 2.25f ? 2.4f : 2.1f;
		if (FurnitureKit.Add(k, model, c, yaw, new() { ["upholstery"] = mat, ["wood"] = LodgeTextures.DarkWoodMat }, new Vector3(w / made, 1f, 1f))) return;
		var b = new Basis(Vector3.Up, yaw);
		k.Xf = new Transform3D(b, c);
		k.Mat(mat);
		k.Color = Colors.White;
		k.Box(new Vector3(0, 0.22f, 0), new Vector3(w, 0.26f, 0.9f), 1f);
		k.Box(new Vector3(0, 0.42f, 0.05f), new Vector3(w - 0.36f, 0.14f, 0.72f), 1f);   // the seat cushion
		k.Box(new Vector3(0, 0.62f, 0.36f), new Vector3(w, 0.62f, 0.2f), 1f);            // the back
		foreach (float s in new[] { -1f, 1f })
			k.Cylinder(new Vector3(s * (w * 0.5f - 0.1f), 0.58f, -0.44f), new Vector3(s * (w * 0.5f - 0.1f), 0.58f, 0.44f), 0.13f, 0.13f, 8, true);
		k.Mat(LodgeTextures.DarkWoodMat);
		foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
			k.Box(new Vector3(sx * (w * 0.5f - 0.08f), 0.045f, sz * 0.36f), new Vector3(0.06f, 0.09f, 0.06f), 1f);
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A wing armchair facing -Z.</summary>
	public static void Armchair(MeshKit k, Vector3 c, float yaw, Material mat)
	{
		if (FurnitureKit.Add(k, "wing_chair", c, yaw, new() { ["upholstery"] = mat, ["wood"] = LodgeTextures.DarkWoodMat })) return;
		k.Xf = new Transform3D(new Basis(Vector3.Up, yaw), c);
		k.Mat(mat);
		k.Color = Colors.White;
		k.Box(new Vector3(0, 0.24f, 0), new Vector3(0.82f, 0.28f, 0.8f), 1f);
		k.Box(new Vector3(0, 0.42f, 0.02f), new Vector3(0.6f, 0.1f, 0.66f), 1f);
		k.Box(new Vector3(0, 0.78f, 0.34f), new Vector3(0.82f, 0.9f, 0.16f), 1f);
		foreach (float s in new[] { -1f, 1f })
		{
			k.Box(new Vector3(s * 0.36f, 0.5f, 0f), new Vector3(0.12f, 0.28f, 0.76f), 1f);
			k.Box(new Vector3(s * 0.36f, 0.92f, 0.22f), new Vector3(0.1f, 0.34f, 0.3f), 1f);   // the wings
		}
		k.Mat(LodgeTextures.DarkWoodMat);
		foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
			k.Box(new Vector3(sx * 0.34f, 0.05f, sz * 0.32f), new Vector3(0.05f, 0.1f, 0.05f), 1f);
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A table: a top and four legs (or a pedestal for round ones).</summary>
	public static void Table(MeshKit k, Vector3 c, float yaw, Vector2 size, float h, Material top, Material legs, bool round = false)
	{
		// the modelled ones: the café table, the low table by the fire (scaled to the size asked, near enough)
		if (round && Mathf.Abs(h - 0.74f) < 0.1f
			&& FurnitureKit.Add(k, "bar_table", c, yaw, new() { ["top"] = top, ["metal"] = legs }, new Vector3(size.X / 0.82f, h / 0.74f, size.X / 0.82f))) return;
		if (!round && h < 0.6f
			&& FurnitureKit.Add(k, "coffee_table", c, yaw, new() { ["wood"] = top }, new Vector3(size.X / 1.4f, h / 0.42f, size.Y / 0.8f))) return;
		k.Xf = new Transform3D(new Basis(Vector3.Up, yaw), c);
		k.Mat(top);
		k.Color = Colors.White;
		if (round)
		{
			k.Cylinder(new Vector3(0, h - 0.035f, 0), new Vector3(0, h, 0), size.X * 0.5f, size.X * 0.5f, 16, true);
			k.Mat(legs);
			k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, h - 0.035f, 0), 0.05f, 0.04f, 8, false);
			k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.04f, 0), 0.22f, 0.18f, 10, true);
		}
		else
		{
			k.Box(new Vector3(0, h - 0.03f, 0), new Vector3(size.X, 0.06f, size.Y), 1f);
			k.Mat(legs);
			foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
				k.Box(new Vector3(sx * (size.X * 0.5f - 0.08f), (h - 0.06f) * 0.5f, sz * (size.Y * 0.5f - 0.08f)), new Vector3(0.07f, h - 0.06f, 0.07f), 1f);
		}
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A dining chair facing -Z: a turned frame, an upholstered seat and back.</summary>
	public static void Chair(MeshKit k, Vector3 c, float yaw, Material seat)
	{
		if (FurnitureKit.Add(k, "dining_chair", c, yaw, new() { ["wood"] = LodgeTextures.DarkWoodMat, ["upholstery"] = seat })) return;
		k.Xf = new Transform3D(new Basis(Vector3.Up, yaw), c);
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
			k.Box(new Vector3(sx * 0.2f, 0.22f, sz * 0.2f), new Vector3(0.04f, 0.44f, 0.04f), 1f);
		foreach (float sx in new[] { -1f, 1f })
			k.Box(new Vector3(sx * 0.2f, 0.72f, 0.2f), new Vector3(0.04f, 0.56f, 0.04f), 1f);
		k.Mat(seat);
		k.Box(new Vector3(0, 0.46f, 0), new Vector3(0.46f, 0.06f, 0.46f), 1f);
		k.Box(new Vector3(0, 0.78f, 0.21f), new Vector3(0.38f, 0.36f, 0.04f), 1f);
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A bed facing -Z (headboard at +Z): a tall buttoned headboard, a counterpane, two pillows.</summary>
	public static void Bed(MeshKit k, Vector3 c, float yaw, Material cover, Material head, float w = 1.6f)
	{
		if (FurnitureKit.Add(k, "bed_17", c, yaw, new()
			{
				["wood"] = LodgeTextures.DarkWoodMat, ["upholstery"] = head, ["linen"] = LodgeTextures.LinenMat, ["pillow"] = LodgeTextures.PillowMat, ["cover"] = cover,
			}, new Vector3(w / 1.7f, 1f, 1f))) return;
		k.Xf = new Transform3D(new Basis(Vector3.Up, yaw), c);
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		k.Box(new Vector3(0, 0.2f, 0), new Vector3(w, 0.3f, 2.1f), 1f);
		k.Mat(head);
		k.Box(new Vector3(0, 0.75f, 1.02f), new Vector3(w + 0.1f, 1.4f, 0.1f), 1f);
		k.Mat(LodgeTextures.LinenMat);
		k.Box(new Vector3(0, 0.44f, 0.05f), new Vector3(w - 0.06f, 0.18f, 1.94f), 1f);
		k.Mat(LodgeTextures.PillowMat);   // (plain: a blob's round UVs pinch the linen's weave at its ends)
		foreach (float s in new[] { -1f, 1f }) k.Blob(new Vector3(s * w * 0.24f, 0.58f, 0.78f), new Vector3(w * 0.2f, 0.07f, 0.16f), (int)(s * 7 + 700), 0.1f, false, 1f);
		k.Mat(cover);
		k.Box(new Vector3(0, 0.55f, -0.25f), new Vector3(w + 0.04f, 0.06f, 1.5f), 1f);
		k.Box(new Vector3(0, 0.36f, -1.0f), new Vector3(w + 0.04f, 0.4f, 0.04f), 1f);
		foreach (float s in new[] { -1f, 1f }) k.Box(new Vector3(s * (w * 0.5f + 0.02f), 0.36f, -0.25f), new Vector3(0.04f, 0.4f, 1.5f), 1f);
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A lamp: a turned base, a pleated shade that glows. Returns the bulb's position (parent space).</summary>
	public static Vector3 Lamp(MeshKit k, Vector3 c, float h, Material shade, bool floor = false)
	{
		// the modelled lamps (a stepped, turned base, a reeded column, a pleated drum shade), scaled to the height asked
		float made = floor ? 1.6f : 0.5f;
		if (FurnitureKit.Add(k, floor ? "lamp_floor" : "lamp_table", c, 0f, new() { ["metal"] = LodgeTextures.BrassMat, ["shade"] = shade }, Vector3.One * (h / made)))
			return c + Vector3.Up * (h - 0.1f);
		k.Mat(LodgeTextures.BrassMat);
		k.Color = Colors.White;
		k.Cylinder(c, c + Vector3.Up * 0.03f, floor ? 0.16f : 0.09f, floor ? 0.14f : 0.08f, 10, true);
		k.Cylinder(c + Vector3.Up * 0.03f, c + Vector3.Up * (h - 0.18f), floor ? 0.018f : 0.035f, floor ? 0.018f : 0.03f, 8, false);
		k.Mat(shade);
		k.Cylinder(c + Vector3.Up * (h - 0.24f), c + Vector3.Up * (h + 0.02f), floor ? 0.24f : 0.18f, floor ? 0.14f : 0.1f, 12, false);
		return c + Vector3.Up * (h - 0.1f);
	}

	/// <summary>A chandelier of antlers and iron, candle bulbs on its tips: rings of arms round a hub hung on a chain.
	/// Returns the bulbs' positions.</summary>
	public static List<Vector3> Chandelier(MeshKit k, Vector3 hang, float chain, float r, int arms, Material bulb)
	{
		var bulbs = new List<Vector3>();
		k.Mat(LodgeTextures.IronMat);
		k.Color = Colors.White;
		Vector3 hub = hang + Vector3.Down * chain;
		k.Cylinder(hang, hub, 0.02f, 0.02f, 5, false);
		k.Cylinder(hub + Vector3.Up * 0.15f, hub + Vector3.Down * 0.25f, 0.07f, 0.04f, 8, true);
		foreach (var (ring, rr, dy) in new[] { (0, r, 0f), (1, r * 0.6f, 0.35f) })
			for (int i = 0; i < arms; i++)
			{
				float a = Mathf.Tau * (i + ring * 0.5f) / arms;
				Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
				Vector3 tip = hub + dir * rr + Vector3.Up * dy;
				k.Mat(LodgeTextures.IronMat);
				k.Cylinder(hub + Vector3.Up * dy * 0.5f, hub + dir * rr * 0.5f + Vector3.Down * 0.12f + Vector3.Up * dy, 0.015f, 0.015f, 4, false);
				k.Cylinder(hub + dir * rr * 0.5f + Vector3.Down * 0.12f + Vector3.Up * dy, tip, 0.015f, 0.015f, 4, false);
				k.Mat(LodgeTextures.LinenMat);
				k.Cylinder(tip, tip + Vector3.Up * 0.12f, 0.018f, 0.018f, 6, true);
				k.Mat(bulb);
				k.Blob(tip + Vector3.Up * 0.16f, new Vector3(0.025f, 0.04f, 0.025f), 800 + i + ring * 20, 0f, false, 1f);
				bulbs.Add(tip + Vector3.Up * 0.16f);
			}
		return bulbs;
	}

	/// <summary>A wall sconce facing <paramref name="outward"/>: a brass back plate, an arm, a small shade. Returns the bulb.</summary>
	public static Vector3 Sconce(MeshKit k, Vector3 at, Vector3 outward, Material shade)
	{
		k.Mat(LodgeTextures.BrassMat);
		k.Color = Colors.White;
		var b = new Basis(outward.Cross(Vector3.Up).Normalized(), Vector3.Up, outward);
		k.Box(at + outward * 0.01f, new Vector3(0.08f, 0.16f, 0.02f), 1f, b);
		k.Cylinder(at + outward * 0.02f, at + outward * 0.14f + Vector3.Up * 0.06f, 0.012f, 0.012f, 5, false);
		k.Mat(shade);
		Vector3 s = at + outward * 0.16f + Vector3.Up * 0.12f;
		k.Cylinder(s + Vector3.Down * 0.07f, s + Vector3.Up * 0.05f, 0.08f, 0.05f, 10, false);
		return s;
	}
}
