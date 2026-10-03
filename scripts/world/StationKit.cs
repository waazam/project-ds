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
	/// <summary>Crown mouldings where the walls meet the ceiling (the interiors pass, 2026-10-03: the owner, "make all the
	/// interiors feel more natural and less boxy, base boards and crown molding can go a long way"). Off for the
	/// basement's brick.</summary>
	private static bool _crown = true;
	/// <summary>Which face gets the crown and the panels: 0 both, +1 the +axis face only, -1 the -axis face only (a wall
	/// with a room's own skin in front of its other face, or the outside behind it).</summary>
	private static int _side;
	private static bool _panels = true;

	private static void Slab(MeshKit k, StaticBody3D body, Vector3 c, Vector3 s, float floorY = float.NaN, float ceilY = float.NaN)
	{
		// the crown along its top, both faces, where it meets the ceiling (over a doorway's lintel too)
		if (_crown && !float.IsNaN(ceilY) && Mathf.Abs(c.Y + s.Y * 0.5f - ceilY) < 0.01f)
		{
			if (s.Z <= 0.5f && s.X >= 0.2f)
			{
				Vector3 a = new(c.X - s.X * 0.5f, ceilY, c.Z), b = new(c.X + s.X * 0.5f, ceilY, c.Z);
				if (_side >= 0) Crown(k, a + Vector3.Back * s.Z * 0.5f, b + Vector3.Back * s.Z * 0.5f, Vector3.Back, 0f);
				if (_side <= 0) Crown(k, b + Vector3.Forward * s.Z * 0.5f, a + Vector3.Forward * s.Z * 0.5f, Vector3.Forward, 0f);
			}
			else if (s.X <= 0.5f && s.Z >= 0.2f)
			{
				Vector3 a = new(c.X, ceilY, c.Z - s.Z * 0.5f), b = new(c.X, ceilY, c.Z + s.Z * 0.5f);
				if (_side >= 0) Crown(k, b + Vector3.Right * s.X * 0.5f, a + Vector3.Right * s.X * 0.5f, Vector3.Right, 0.004f);
				if (_side <= 0) Crown(k, a + Vector3.Left * s.X * 0.5f, b + Vector3.Left * s.X * 0.5f, Vector3.Left, 0.004f);
			}
		}
		// the grime along its feet, both faces, where it stands on the floor (not a lintel over a door)
		if (!float.IsNaN(floorY) && Mathf.Abs(c.Y - s.Y * 0.5f - floorY) < 0.01f)
		{
			if (s.Z <= 0.5f && s.X >= 0.3f)
			{
				Vector3 a = new(c.X - s.X * 0.5f, floorY, c.Z), b = new(c.X + s.X * 0.5f, floorY, c.Z);
				World.Weathering.ContactStrips(k, a + Vector3.Back * s.Z * 0.5f, b + Vector3.Back * s.Z * 0.5f, Vector3.Back, floorY);
				World.Weathering.ContactStrips(k, b + Vector3.Forward * s.Z * 0.5f, a + Vector3.Forward * s.Z * 0.5f, Vector3.Forward, floorY);
				LodgeParts.LodgeKit.Trim(k, a + Vector3.Back * s.Z * 0.5f, b + Vector3.Back * s.Z * 0.5f, Vector3.Back, 0.13f, 0.018f, true);
				LodgeParts.LodgeKit.Trim(k, b + Vector3.Forward * s.Z * 0.5f, a + Vector3.Forward * s.Z * 0.5f, Vector3.Forward, 0.13f, 0.018f, true);
				if (_crown && _panels && s.Y >= 2.4f)
				{
					if (_side >= 0) Panels(k, a + Vector3.Back * s.Z * 0.5f, b + Vector3.Back * s.Z * 0.5f, Vector3.Back, floorY);
					if (_side <= 0) Panels(k, b + Vector3.Forward * s.Z * 0.5f, a + Vector3.Forward * s.Z * 0.5f, Vector3.Forward, floorY);
				}
			}
			else if (s.X <= 0.5f && s.Z >= 0.3f)
			{
				Vector3 a = new(c.X, floorY, c.Z - s.Z * 0.5f), b = new(c.X, floorY, c.Z + s.Z * 0.5f);
				World.Weathering.ContactStrips(k, b + Vector3.Right * s.X * 0.5f, a + Vector3.Right * s.X * 0.5f, Vector3.Right, floorY);
				World.Weathering.ContactStrips(k, a + Vector3.Left * s.X * 0.5f, b + Vector3.Left * s.X * 0.5f, Vector3.Left, floorY);
				LodgeParts.LodgeKit.Trim(k, b + Vector3.Right * s.X * 0.5f, a + Vector3.Right * s.X * 0.5f, Vector3.Right, 0.13f, 0.018f, true);
				LodgeParts.LodgeKit.Trim(k, a + Vector3.Left * s.X * 0.5f, b + Vector3.Left * s.X * 0.5f, Vector3.Left, 0.13f, 0.018f, true);
				if (_crown && _panels && s.Y >= 2.4f)
				{
					if (_side >= 0) Panels(k, b + Vector3.Right * s.X * 0.5f, a + Vector3.Right * s.X * 0.5f, Vector3.Right, floorY);
					if (_side <= 0) Panels(k, a + Vector3.Left * s.X * 0.5f, b + Vector3.Left * s.X * 0.5f, Vector3.Left, floorY);
				}
			}
		}
		BuildKit.Box(k, c, s, 1.1f);
		// (a wall's slab big enough to hide a room: an occluder down its middle)
		if (s.Z <= 0.5f && s.X >= 0.8f && s.Y >= 0.8f)
		{
			float hx = s.X * 0.5f - 0.03f, hy = s.Y * 0.5f - 0.03f;
			k.Occlude(c + new Vector3(-hx, -hy, 0), c + new Vector3(hx, -hy, 0), c + new Vector3(hx, hy, 0), c + new Vector3(-hx, hy, 0));
		}
		else if (s.X <= 0.5f && s.Z >= 0.8f && s.Y >= 0.8f)
		{
			float hz = s.Z * 0.5f - 0.03f, hy = s.Y * 0.5f - 0.03f;
			k.Occlude(c + new Vector3(0, -hy, -hz), c + new Vector3(0, -hy, hz), c + new Vector3(0, hy, hz), c + new Vector3(0, hy, -hz));
		}
		body?.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
	}

	/// <summary>A crown moulding along a wall's face from a to b at the ceiling (n out of the wall): three steps out from a
	/// flat band on the wall to the deepest under the ceiling, so it reads as a cove from below. Dark wood. Runs along Z
	/// sit <paramref name="drop"/> lower than runs along X, so where two meet in a corner their faces never share a
	/// plane.</summary>
	private static void Crown(MeshKit k, Vector3 a, Vector3 b, Vector3 n, float drop)
	{
		float len = a.DistanceTo(b);
		if (len < 0.05f) return;
		Vector3 along = (b - a) / len;
		var basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up).Normalized());
		if (basis.Z.Dot(n) < 0) basis = new Basis(-along, Vector3.Up, -along.Cross(Vector3.Up).Normalized());
		var mat = k.CurrentMaterial;
		var col = k.Color;
		k.Mat(LodgeParts.LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		// (its back 2 mm into the wall, its top 2 mm under the ceiling: out of both their planes)
		Vector3 mid = (a + b) * 0.5f - n * 0.002f + Vector3.Down * (0.002f + drop);
		foreach (var (h, proud) in new[] { (0.22f, 0.018f), (0.13f, 0.042f), (0.06f, 0.07f) })
			k.Box(mid + n * (proud * 0.5f) + Vector3.Down * (h * 0.5f), new Vector3(len - 0.006f, h, proud), 1f, basis);
		k.Color = col;
		if (mat != null) k.Mat(mat);
	}

	/// <summary>The lower wall panelled (the interiors pass, 2026-10-03): a dado rail at hip height and, under it, a row of
	/// moulded frames set on the wallpaper, dark wood like the skirting; on a wall's face from a to b (n out of it).</summary>
	private static void Panels(MeshKit k, Vector3 a, Vector3 b, Vector3 n, float floorY)
	{
		float len = a.DistanceTo(b);
		if (len < 0.5f) return;
		Vector3 along = (b - a) / len;
		var basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up).Normalized());
		if (basis.Z.Dot(n) < 0) basis = new Basis(-along, Vector3.Up, -along.Cross(Vector3.Up).Normalized());
		var mat = k.CurrentMaterial;
		var col = k.Color;
		k.Mat(LodgeParts.LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		Vector3 P(float u, float y, float out_) => a + along * u + Vector3.Up * (floorY + y) + n * out_;
		// the dado rail: a rounded nosing over a flat band
		k.Box(P(len * 0.5f, 0.92f, 0.012f - 0.002f), new Vector3(len - 0.006f, 0.05f, 0.024f), 1f, basis);
		k.Box(P(len * 0.5f, 0.955f, 0.02f - 0.002f), new Vector3(len - 0.006f, 0.022f, 0.04f), 1f, basis);
		// the frames: as many as fit (about 0.8 m each), evenly, a hand's width from the ends
		int count = Mathf.Max(1, Mathf.RoundToInt((len - 0.2f) / 0.92f));
		float gap = 0.13f, pw = (len - 0.2f - (count - 1) * gap) / count;
		if (pw > 0.25f)
		{
			const float w = 0.024f, o = 0.005f, y0 = 0.25f, y1 = 0.79f;   // (their backs 2 mm into the wall, out of its face's plane)
			for (int i = 0; i < count; i++)
			{
				float u0 = 0.1f + i * (pw + gap), u1 = u0 + pw;
				k.Box(P((u0 + u1) * 0.5f, y0, o), new Vector3(pw, w, 0.014f), 1f, basis);
				k.Box(P((u0 + u1) * 0.5f, y1, o), new Vector3(pw, w, 0.014f), 1f, basis);
				k.Box(P(u0 + w * 0.5f, (y0 + y1) * 0.5f, o), new Vector3(w, y1 - y0 - w, 0.014f), 1f, basis);
				k.Box(P(u1 - w * 0.5f, (y0 + y1) * 0.5f, o), new Vector3(w, y1 - y0 - w, 0.014f), 1f, basis);
			}
		}
		k.Color = col;
		if (mat != null) k.Mat(mat);
	}

	/// <summary>A wall running along X at a fixed Z (what you'd face walking +Z or -Z through it).</summary>
	public static void WallAlongX(MeshKit k, StaticBody3D body, float z, float x0, float x1, float height, float y0,
		(float center, float width)? gap, float doorH = 2.2f, float thick = 0.14f, bool crown = true, int trimSide = 0, bool panels = true)
	{
		_crown = crown;
		_side = trimSide;
		_panels = panels;
		float top = y0 + height;
		if (gap == null) { Slab(k, body, new Vector3((x0 + x1) * 0.5f, y0 + height * 0.5f, z), new Vector3(x1 - x0, height, thick), y0, top); _crown = true; _side = 0; _panels = true; return; }
		var (c, w) = gap.Value;
		float ga = c - w * 0.5f, gb = c + w * 0.5f;
		if (ga > x0) Slab(k, body, new Vector3((x0 + ga) * 0.5f, y0 + height * 0.5f, z), new Vector3(ga - x0, height, thick), y0, top);
		if (gb < x1) Slab(k, body, new Vector3((gb + x1) * 0.5f, y0 + height * 0.5f, z), new Vector3(x1 - gb, height, thick), y0, top);
		if (height > doorH) Slab(k, body, new Vector3(c, y0 + (doorH + height) * 0.5f, z), new Vector3(w, height - doorH, thick), y0, top);
		_crown = true;
		_side = 0;
		_panels = true;
	}

	/// <summary>A wall running along Z at a fixed X (what you'd face walking +X or -X through it).</summary>
	public static void WallAlongZ(MeshKit k, StaticBody3D body, float x, float z0, float z1, float height, float y0,
		(float center, float width)? gap, float doorH = 2.2f, float thick = 0.14f, bool crown = true, int trimSide = 0, bool panels = true)
	{
		_crown = crown;
		_side = trimSide;
		_panels = panels;
		float top = y0 + height;
		if (gap == null) { Slab(k, body, new Vector3(x, y0 + height * 0.5f, (z0 + z1) * 0.5f), new Vector3(thick, height, z1 - z0), y0, top); _crown = true; _side = 0; _panels = true; return; }
		var (c, w) = gap.Value;
		float ga = c - w * 0.5f, gb = c + w * 0.5f;
		if (ga > z0) Slab(k, body, new Vector3(x, y0 + height * 0.5f, (z0 + ga) * 0.5f), new Vector3(thick, height, ga - z0), y0, top);
		if (gb < z1) Slab(k, body, new Vector3(x, y0 + height * 0.5f, (gb + z1) * 0.5f), new Vector3(thick, height, z1 - gb), y0, top);
		if (height > doorH) Slab(k, body, new Vector3(x, y0 + (doorH + height) * 0.5f, c), new Vector3(thick, height - doorH, w), y0, top);
		_crown = true;
		_side = 0;
		_panels = true;
	}

	/// <summary>A wooden door frame in a wall gap: linings round the inside of the opening (so the cut ends
	/// of the wall slabs never show, and a closed leaf sits snug) and a casing proud of the wall on both
	/// faces. <paramref name="bottomCenter"/> is the gap's middle at floor level, on the wall's centre
	/// plane; <paramref name="along"/> runs along the wall; <paramref name="depth"/> is how thick a wall
	/// (or pair of walls) the linings span.</summary>
	public static void DoorFrame(MeshKit k, Vector3 bottomCenter, Vector3 along, float gapW, float gapH, float depth = 0.2f)
	{
		along = along.Normalized();
		var b = new Basis(along, Vector3.Up, along.Cross(Vector3.Up));
		void Piece(Vector3 c, Vector3 size) => BuildKit.Box(k, bottomCenter + b * c, size, 1.2f, BuildKit.Face.None, b);
		const float lt = 0.035f, cw = 0.09f, ct = 0.06f;
		float hw = gapW * 0.5f;
		Piece(new Vector3(-hw + lt * 0.5f, gapH * 0.5f, 0), new Vector3(lt, gapH, depth));
		Piece(new Vector3(hw - lt * 0.5f, gapH * 0.5f, 0), new Vector3(lt, gapH, depth));
		Piece(new Vector3(0, gapH - lt * 0.5f, 0), new Vector3(gapW, lt, depth));
		foreach (int s in new[] { -1, 1 })
		{
			float z = s * (depth * 0.5f - 0.005f);
			Piece(new Vector3(-hw - cw * 0.5f + lt, (gapH + cw) * 0.5f, z), new Vector3(cw, gapH + cw, ct));
			Piece(new Vector3(hw + cw * 0.5f - lt, (gapH + cw) * 0.5f, z), new Vector3(cw, gapH + cw, ct));
			Piece(new Vector3(0, gapH + cw * 0.5f, z), new Vector3(gapW + cw * 2f - lt * 2f, cw, ct));
		}
	}

	/// <summary>Flat floor and (optional) ceiling slab spanning the room's footprint, with collision
	/// on the floor only (the ceiling is never walked on).</summary>
	public static void FloorAndCeiling(MeshKit floorK, MeshKit ceilK, StaticBody3D body, float hw, float hd, float height, float y0, bool ceiling = true)
	{
		Slab(floorK, body, new Vector3(0, y0 - 0.05f, 0), new Vector3(hw * 2f, 0.1f, hd * 2f));
		if (ceiling) BuildKit.Box(ceilK, new Vector3(0, y0 + height + 0.05f, 0), new Vector3(hw * 2f, 0.1f, hd * 2f), 1f / 0.5f, BuildKit.Face.PY);
	}
}
