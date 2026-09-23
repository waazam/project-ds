using Godot;

namespace ProjectDS.World;

/// <summary>
/// Small shared helpers for the forester station's rooms (Act 13): a wall with an optional
/// rectangular doorway gap (used on all four sides of the lobby and every room off it), built as
/// both visual geometry and matching collision in one call so a gap is never solid. Every station
/// room is a simple box built from these, so a doorway gap on one room's wall lines up exactly
/// with the matching gap on its neighbour when the two share that wall's plane (no connecting
/// hallway needed - see StationInterior's layout notes for the coordinate scheme).
/// </summary>
public static class StationKit
{
	private static void Slab(MeshKit k, StaticBody3D body, Vector3 c, Vector3 s)
	{
		BuildKit.Box(k, c, s, 1.1f);
		body?.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
	}

	/// <summary>A wall running along X at a fixed Z (what you'd face walking +Z or -Z through it).</summary>
	public static void WallAlongX(MeshKit k, StaticBody3D body, float z, float x0, float x1, float height, float y0,
		(float center, float width)? gap, float doorH = 2.2f, float thick = 0.14f)
	{
		if (gap == null) { Slab(k, body, new Vector3((x0 + x1) * 0.5f, y0 + height * 0.5f, z), new Vector3(x1 - x0, height, thick)); return; }
		var (c, w) = gap.Value;
		float ga = c - w * 0.5f, gb = c + w * 0.5f;
		if (ga > x0) Slab(k, body, new Vector3((x0 + ga) * 0.5f, y0 + height * 0.5f, z), new Vector3(ga - x0, height, thick));
		if (gb < x1) Slab(k, body, new Vector3((gb + x1) * 0.5f, y0 + height * 0.5f, z), new Vector3(x1 - gb, height, thick));
		if (height > doorH) Slab(k, body, new Vector3(c, y0 + (doorH + height) * 0.5f, z), new Vector3(w, height - doorH, thick));
	}

	/// <summary>A wall running along Z at a fixed X (what you'd face walking +X or -X through it).</summary>
	public static void WallAlongZ(MeshKit k, StaticBody3D body, float x, float z0, float z1, float height, float y0,
		(float center, float width)? gap, float doorH = 2.2f, float thick = 0.14f)
	{
		if (gap == null) { Slab(k, body, new Vector3(x, y0 + height * 0.5f, (z0 + z1) * 0.5f), new Vector3(thick, height, z1 - z0)); return; }
		var (c, w) = gap.Value;
		float ga = c - w * 0.5f, gb = c + w * 0.5f;
		if (ga > z0) Slab(k, body, new Vector3(x, y0 + height * 0.5f, (z0 + ga) * 0.5f), new Vector3(thick, height, ga - z0));
		if (gb < z1) Slab(k, body, new Vector3(x, y0 + height * 0.5f, (gb + z1) * 0.5f), new Vector3(thick, height, z1 - gb));
		if (height > doorH) Slab(k, body, new Vector3(x, y0 + (doorH + height) * 0.5f, c), new Vector3(thick, height - doorH, w));
	}

	/// <summary>Flat floor and (optional) ceiling slab spanning the room's footprint, with collision
	/// on the floor only (the ceiling is never walked on).</summary>
	public static void FloorAndCeiling(MeshKit floorK, MeshKit ceilK, StaticBody3D body, float hw, float hd, float height, float y0, bool ceiling = true)
	{
		Slab(floorK, body, new Vector3(0, y0 - 0.05f, 0), new Vector3(hw * 2f, 0.1f, hd * 2f));
		if (ceiling) BuildKit.Box(ceilK, new Vector3(0, y0 + height + 0.05f, 0), new Vector3(hw * 2f, 0.1f, hd * 2f), 1f / 0.5f, BuildKit.Face.PY);
	}
}
