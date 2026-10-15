using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// A room's light and dirt baked into its vertex colours as it's built (2026-10-14g, the interiors kit; the PS2's way:
/// its rooms carried their occlusion and their grime in the vertices, the light on top). The look survey found the
/// interiors lit edge to edge like a set, with no dark in the corners, at the floor or under the furniture: the SSAO
/// barely shows under the lantern, which is direct light. This darkens, per vertex:
/// <list type="bullet">
/// <item>the seams: within <see cref="Reach"/> of each of the room's planes (its floor, its ceiling, its walls) that a
/// surface meets at an angle, so corners (two or three seams) go darkest;</item>
/// <item>the height: the lower wall a little darker (the dirt kicked up), the ceiling darker still (no light reaches it);</item>
/// <item>under and behind furniture (<see cref="Under"/>) and round frames;</item>
/// <item>soot over a lamp, a candle or a stove (<see cref="Soot"/>);</item>
/// <item>a general dimness lifted only near the room's own lights (<see cref="Light"/>), so its light falls in pools.</item>
/// </list>
/// It also writes, for <see cref="RoomSurface"/>'s weathering shader, each vertex's height above the floor and depth
/// under the ceiling (second UV: the tide line at a wall's foot, the streaks run down from the top) and in its alpha a
/// mask: on walls the wet (<see cref="Damp"/>: under a window, a leak), on floors the wear (<see cref="Path"/>: where
/// feet went). Big flat faces need vertices for any of it to show: draw them with <see cref="MeshKit.Tess"/> set.
/// <para>Space: the kit's (a room's node). Apply it to each kit before its CommitTo; or to a mesh already built with
/// <see cref="ApplyTo"/>. Materials must multiply vertex colour (the game's mostly do; <see cref="RoomSurface"/>'s do).</para>
/// </summary>
public sealed class RoomShade
{
	/// <summary>The room's inside: floor at its bottom, ceiling at its top, walls its sides.</summary>
	public Aabb Box;
	/// <summary>How far a seam's dark reaches out over each surface (m), and how dark it is right in the seam.</summary>
	public float Reach = 1.25f, Seam = 0.58f;
	/// <summary>A wall's foot darker by this much (the bottom metre), its top by this much under the ceiling, and the ceiling itself.</summary>
	public float Foot = 0.25f, Head = 0.22f, Ceiling = 0.35f;
	/// <summary>The dirt's tint: the dark leans toward this (old brown, not grey).</summary>
	public Color Tint = new(0.86f, 0.8f, 0.74f);
	/// <summary>How dim the room is away from its <see cref="Light"/>s (1: no lights given, nothing dimmed).</summary>
	public float Dim = 0.8f;

	private readonly List<(Vector3 p, Vector3 n)> _planes = new();
	private readonly List<(Aabb box, float strength, float reach)> _under = new();
	private readonly List<(Vector3 at, float radius, float strength)> _soot = new();
	private readonly List<(Vector3 at, float radius, float strength)> _lights = new();
	private readonly List<(Vector3 at, float radius, float strength)> _damp = new();
	private readonly List<(Vector3 a, Vector3 b, float width, float strength)> _paths = new();

	public RoomShade(Aabb inside)
	{
		Box = inside;
		Vector3 lo = inside.Position, hi = inside.End;
		_planes.Add((lo, Vector3.Up));
		_planes.Add((hi, Vector3.Down));
		_planes.Add((lo, Vector3.Right));
		_planes.Add((hi, Vector3.Left));
		_planes.Add((lo, Vector3.Back));
		_planes.Add((hi, Vector3.Forward));
	}

	/// <summary>From the room's floor corners and heights: x0..x1, z0..z1, floor y0, ceiling y1.</summary>
	public RoomShade(float x0, float x1, float z0, float z1, float y0, float y1)
		: this(new Aabb(new Vector3(Mathf.Min(x0, x1), y0, Mathf.Min(z0, z1)), new Vector3(Mathf.Abs(x1 - x0), y1 - y0, Mathf.Abs(z1 - z0)))) { }

	/// <summary>Another plane surfaces darken toward (a partition, a chimney breast, a room that isn't a box):
	/// <paramref name="n"/> facing into the room.</summary>
	public RoomShade Plane(Vector3 p, Vector3 n) { _planes.Add((p, n.Normalized())); return this; }

	/// <summary>Dark under and behind something standing in the room (a wardrobe, a table, a crate, a frame on a wall):
	/// the floor under its footprint and round it, the wall behind it. <paramref name="box"/> its bounds.</summary>
	public RoomShade Under(Aabb box, float strength = 0.45f, float reach = 0.45f) { _under.Add((box, strength, reach)); return this; }

	/// <summary><see cref="Under"/> for a box given by its floor centre and size.</summary>
	public RoomShade Under(Vector3 floorCentre, Vector3 size, float strength = 0.45f, float reach = 0.45f)
		=> Under(new Aabb(floorCentre - new Vector3(size.X * 0.5f, 0f, size.Z * 0.5f), size), strength, reach);

	/// <summary>Soot from a flame at <paramref name="at"/>: the wall and ceiling over it blackened, spreading up.</summary>
	public RoomShade Soot(Vector3 at, float radius = 0.9f, float strength = 0.5f) { _soot.Add((at, radius, strength)); return this; }

	/// <summary>A light the room is lit by (a lamp, a window): near it the room keeps its colour; away from every one
	/// it falls to <see cref="Dim"/>.</summary>
	public RoomShade Light(Vector3 at, float radius = 4f, float strength = 1f) { _lights.Add((at, radius, strength)); return this; }

	/// <summary>Wet on the walls round <paramref name="at"/> (below a window, under a leak): the weathering shader runs
	/// more streaks there and darkens it.</summary>
	public RoomShade Damp(Vector3 at, float radius = 1.2f, float strength = 0.8f) { _damp.Add((at, radius, strength)); return this; }

	/// <summary>Where feet went, from <paramref name="a"/> to <paramref name="b"/> on the floor: the boards worn there
	/// (the weathering shader rubs them paler and duller).</summary>
	public RoomShade Path(Vector3 a, Vector3 b, float width = 0.9f, float strength = 0.7f) { _paths.Add((a, b, width, strength)); return this; }

	/// <summary>The shade at a point on a surface facing <paramref name="n"/>: rgb the multiplier, a the mask.</summary>
	public Color At(Vector3 p, Vector3 n)
	{
		float occ = 1f;
		// the seams
		foreach (var (q, m) in _planes)
		{
			if (Mathf.Abs(n.Dot(m)) > 0.5f) continue;       // (the surface's own plane, or the one across from it)
			float d = (p - q).Dot(m);
			if (d < -0.05f || d > Reach) continue;          // (behind it: outside the room; or out of its reach)
			float f = 1f - Mathf.Max(d, 0f) / Reach;
			occ *= 1f - Seam * Mathf.Pow(f, 1.3f);
		}
		float y0 = Box.Position.Y, y1 = Box.End.Y, h = Mathf.Max(0.5f, y1 - y0);
		float up = (p.Y - y0), down = (y1 - p.Y);
		if (n.Y < -0.6f) occ *= 1f - Ceiling;
		else if (Mathf.Abs(n.Y) < 0.6f)
		{
			occ *= 1f - Foot * (1f - Mathf.SmoothStep(0f, 1.5f, up));
			occ *= 1f - Head * (1f - Mathf.SmoothStep(0f, h * 0.4f, down));
		}
		// under and behind things
		foreach (var (box, s, reach) in _under)
		{
			Vector3 c = new(Mathf.Clamp(p.X, box.Position.X, box.End.X), Mathf.Clamp(p.Y, box.Position.Y, box.End.Y), Mathf.Clamp(p.Z, box.Position.Z, box.End.Z));
			Vector3 dv = c - p;
			float dist = dv.Length();
			if (dist > reach) continue;
			Vector3 centre = box.GetCenter();
			if (n.Dot(centre - p) <= 0f) continue;          // (its own faces, and what faces away from it)
			float facing = dist < 1e-3f ? 1f : Mathf.Clamp(n.Dot(dv / dist) * 0.7f + 0.3f, 0f, 1f);
			float f = 1f - dist / reach;
			occ *= 1f - s * facing * f * f;
		}
		// soot over flames
		foreach (var (at, r, s) in _soot)
		{
			float dy = p.Y - at.Y;
			if (dy < 0f) continue;
			float spread = r * (1f + dy * 0.35f);
			float hd = new Vector2(p.X - at.X, p.Z - at.Z).Length();
			if (hd > spread) continue;
			float f = (1f - hd / spread) * Mathf.Clamp(1.2f - dy / (r * 4f), 0f, 1f);
			occ *= 1f - s * f * f;
		}
		// the room's light: dim away from every one of them
		if (_lights.Count > 0 && Dim < 1f)
		{
			float lit = 0f;
			foreach (var (at, r, s) in _lights)
			{
				float d = p.DistanceTo(at);
				if (d < r) lit = Mathf.Max(lit, s * (1f - Mathf.SmoothStep(0f, r, d)));
			}
			occ *= Mathf.Lerp(Dim, 1f, lit);
		}
		occ = Mathf.Clamp(occ, 0.12f, 1f);
		// the mask: wet on walls, wear on floors
		float mask = 0f;
		if (n.Y > 0.6f)
			foreach (var (a, b, w, s) in _paths)
			{
				Vector2 pa = new(a.X, a.Z), pb = new(b.X, b.Z), pp = new(p.X, p.Z);
				Vector2 ab = pb - pa;
				float t = Mathf.Clamp((pp - pa).Dot(ab) / Mathf.Max(1e-4f, ab.LengthSquared()), 0f, 1f);
				float d = pp.DistanceTo(pa + ab * t);
				if (d < w) mask = Mathf.Max(mask, s * (1f - Mathf.SmoothStep(w * 0.25f, w, d)));
			}
		else if (n.Y > -0.6f)
			foreach (var (at, r, s) in _damp)
			{
				// spreads down more than up (water runs)
				Vector3 dv = p - at;
				float d = new Vector3(dv.X, dv.Y > 0f ? dv.Y * 2.2f : dv.Y * 0.45f, dv.Z).Length();
				if (d < r) mask = Mathf.Max(mask, s * (1f - Mathf.SmoothStep(0f, r, d)));
			}
		Color tint = Tint.Lerp(Colors.White, occ);
		return new Color(occ * tint.R, occ * tint.G, occ * tint.B, 1f - mask);
	}

	/// <summary>Bakes the room into <paramref name="k"/>'s vertices (those of the surfaces <paramref name="which"/>
	/// accepts; all but the modelled ones if null): their colours multiplied (whatever tint they had kept), their alpha
	/// the mask, their second UV the heights. Only what lies in the room's box (a little over) is touched.</summary>
	public void Apply(MeshKit k, System.Func<Material, bool> which = null)
	{
		var grown = Box.Grow(0.3f);
		float y0 = Box.Position.Y, y1 = Box.End.Y;
		k.Shade((Vector3 p, Vector3 n, ref Color c, ref Vector2 uv2) =>
		{
			if (!grown.HasPoint(p)) return;
			Color s = At(p, n);
			c = new Color(c.R * s.R, c.G * s.G, c.B * s.B, c.A * s.A);
			uv2 = new Vector2(Mathf.Max(p.Y - y0, 0.001f), Mathf.Max(y1 - p.Y, 0.001f));
		}, which);
	}

	/// <summary>As <see cref="Apply(MeshKit, System.Func{Material, bool})"/>, for a mesh already built (another
	/// builder's): its vertices rewritten in a copy of its mesh, in the room's space (<paramref name="roomSpace"/>, the
	/// node the box is in). Surfaces with a second UV of their own (modelled furniture's cavity maps) are left alone.</summary>
	public void ApplyTo(MeshInstance3D mi, Node3D roomSpace)
	{
		if (mi?.Mesh is not ArrayMesh src) return;
		var toRoom = roomSpace.GlobalTransform.AffineInverse() * mi.GlobalTransform;
		var nb = toRoom.Basis.Inverse().Transposed();
		var grown = Box.Grow(0.3f);
		float y0 = Box.Position.Y, y1 = Box.End.Y;
		var mesh = new ArrayMesh();
		for (int si = 0; si < src.GetSurfaceCount(); si++)
		{
			var arr = src.SurfaceGetArrays(si);
			var mat = src.SurfaceGetMaterial(si);
			bool modelled = arr[(int)Mesh.ArrayType.TexUV2].VariantType != Variant.Type.Nil;
			if (!modelled && src.SurfaceGetPrimitiveType(si) == Mesh.PrimitiveType.Triangles)
			{
				var v = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
				var nrm = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : (Vector3[])arr[(int)Mesh.ArrayType.Normal];
				var col = arr[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? null : (Color[])arr[(int)Mesh.ArrayType.Color];
				if (col == null) { col = new Color[v.Length]; System.Array.Fill(col, Colors.White); }
				var uv2 = new Vector2[v.Length];
				for (int i = 0; i < v.Length; i++)
				{
					Vector3 p = toRoom * v[i];
					if (!grown.HasPoint(p)) continue;
					Vector3 n = nrm != null ? (nb * nrm[i]).Normalized() : Vector3.Up;
					Color s = At(p, n);
					col[i] = new Color(col[i].R * s.R, col[i].G * s.G, col[i].B * s.B, col[i].A * s.A);
					uv2[i] = new Vector2(Mathf.Max(p.Y - y0, 0.001f), Mathf.Max(y1 - p.Y, 0.001f));
				}
				arr[(int)Mesh.ArrayType.Color] = col;
				arr[(int)Mesh.ArrayType.TexUV2] = uv2;
			}
			mesh.AddSurfaceFromArrays(src.SurfaceGetPrimitiveType(si), arr);
			mesh.SurfaceSetMaterial(si, mat);
		}
		mi.Mesh = mesh;
	}
}
