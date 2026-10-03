using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Dev tool (not used by the game): clip_audit.tscn. Loads each level's world on its own (like
/// <see cref="GroundAudit"/>; the save slots are backed up and put back) and audits every static, opaque mesh:
/// <list type="bullet">
/// <item><b>Z-fighting</b>: triangles of two different surfaces lying in the same plane (within 0.1 mm: the reverse-Z depth buffer tells apart anything further),
/// facing the same way, and overlapping (by true 2D polygon clipping): the two fight over every pixel and
/// flicker. Reported per pair of surfaces, with the area and where.</item>
/// <item><b>UV faults</b>: textured triangles whose UVs have collapsed (a texture smeared to a streak), NaN
/// UVs, and surfaces whose texel density varies by more than 12x across their triangles (stretching).</item>
/// <item><b>Degenerate triangles</b>: zero-area slivers (they catch light as sparkles).</item>
/// </list>
/// <item><b>Pierce-through</b> (Act 23's hard check): two meshes that cut through each other where it shows: a thin
/// sliver of something behind a surface poking out through it (the lodge's exterior sill through the dining hall's
/// panelling), or a thing passing right through a wall into the room beyond.</item>
/// Report: test-output/clipping/report.txt (worst first).
/// Run: <c>&lt;godot&gt; --path . res://scenes/levels/clip_audit.tscn --windowed</c>; add <c>-- --winter</c> for the church,
/// the winter woods and the ski lodge alone (Acts 21-23), with the pierce-through check on the church and the lodge.
/// </summary>
public partial class ClipAudit : Node
{
	private const float PlaneTol = 0.0001f, MinOverlap = 0.002f;
	private readonly StringBuilder _log = new();
	/// <summary>The ski lodge's origin (the winter audit), so its entries can say where in the lodge's own space.</summary>
	private Vector3? _lodge;
	private string Where(Vector3 at) => _lodge is { } o ? $"({at.X:0.00},{at.Y:0.00},{at.Z:0.00}) lodge ({at.X - o.X:0.00},{at.Y - o.Y:0.00},{at.Z - o.Z:0.00})" : $"({at.X:0.00},{at.Y:0.00},{at.Z:0.00})";
	private readonly List<string> _backedUp = new();

	private sealed class Tri
	{
		public Vector3 A, B, C, N;
		public int Owner;
	}

	private sealed class Owner
	{
		public string Path, Material;
		public int Tris;
	}

	private readonly List<(string level, string a, string b, float area, Vector3 at, string ex)> _fights = new();
	private readonly List<(string level, string what, string detail)> _uv = new();
	private readonly Dictionary<string, int> _degen = new();
	private readonly List<(string level, string path, Vector3 at, Vector3 size)> _ghosts = new();
	private readonly List<(string level, string kind, string surface, string piercer, float front, float back, Vector3 at)> _pierce = new();

	private static readonly string[] Passable =
	{
		"web", "decal", "glow", "beam", "water", "fog", "light", "flame", "fire", "smoke", "spark", "grass", "fern", "leaf",
		"leaves", "foliage", "moss", "vine", "ivy", "curtain", "drape", "cloth", "sheet", "rope", "wire", "cable", "chain",
		"paper", "note", "photo", "sign", "text", "label", "shadow", "dust", "mist", "rain", "snow", "puddle", "blood",
		"stain", "ink", "uv", "halo", "ring", "shaft", "ray", "sky", "cloud", "outside", "window", "pane", "glass", "bulb",
		"lamp", "candle", "pickup", "item", "hand", "eye", "stalker", "creature", "tentacle", "crawler", "body", "limb",
		"joint", "maw", "fx", "particle", "portrait", "frame", "bird", "frog", "deer", "cobweb", "hanging", "chandelier",
		// checked and meant to be so: mounted flat on a wall, overhead, or walked into on purpose
		"sewer/hole", "act15hallway/closet", "act15hallway/things", "act15hallway/shelves", "bossroom/ladder", "bossroom/fixture",
		"stairwell/door/door", "wheel/spokes", "noticeboard", "wallclock", "exitnight", "bollards",
		// Acts 21-23: a mop leaning (it falls), ski poles stuck in the snow, the vestment hanging, the font's lid on the font
		"skilodge/mop", "ski_tracks/poles", "church/alb", "church/fontlid",
		// room 201's way into the wall cavity, walked through on purpose (the hacked doorway, the connector, the broken
		// brick round both ends, the light leaking out), and the turned-down bed's cover lying on the bed
		"skilodge/hole201", "skilodge/crawlconnector", "crawlentry/brick", "crawlexit/brick", "crawlexit/leaks", "skilodge/perfect201",
	};

	public override void _Ready() => Run();

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task PhysicsFrames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private void Log(string s) { GD.Print("[clip] " + s); _log.AppendLine(s); }
	private static string UserFile(string name) => ProjectSettings.GlobalizePath("user://" + name);

	private async void Run()
	{
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
			if (FileAccess.FileExists(UserFile(slot)) && DirAccess.CopyAbsolute(UserFile(slot), UserFile("clip_audit_backup_" + slot)) == Error.Ok) _backedUp.Add(slot);
		try
		{
			await Frames(5);
			if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--winter") >= 0)
				await AuditWorld("winter", () => { var w = new Node3D { Name = "Winter" }; w.AddChild(new Church { Name = "Church" }); return w; });
			else
			{
				await AuditLevel("trailhead", "res://scenes/levels/forest_world.tscn");
				await AuditLevel("hollow", "res://scenes/levels/hollow_world.tscn");
			}
			WriteReport();
		}
		catch (Exception e) { GD.PushError("[clip] " + e); }
		finally
		{
			foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
			{
				string dst = UserFile(slot), bak = UserFile("clip_audit_backup_" + slot);
				if (_backedUp.Contains(slot) && FileAccess.FileExists(bak)) { DirAccess.CopyAbsolute(bak, dst); DirAccess.RemoveAbsolute(bak); }
				else if (FileAccess.FileExists(dst)) DirAccess.RemoveAbsolute(dst);
			}
		}
		GetTree().Quit();
	}

	private static IEnumerable<Node> Descendants(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			yield return c;
			foreach (var d in Descendants(c)) yield return d;
		}
	}

	private static bool Opaque(Material m) => m switch
	{
		BaseMaterial3D b => b.Transparency == BaseMaterial3D.TransparencyEnum.Disabled,
		ShaderMaterial sm => sm.Shader?.Code is not string code || !(code.Contains("blend_add") || code.Contains("ALPHA")),
		_ => true,
	};

	private Task AuditLevel(string level, string path) => AuditWorld(level, () => GD.Load<PackedScene>(path).Instantiate<Node3D>());

	private async Task AuditWorld(string level, Func<Node3D> make)
	{
		var world = make();
		AddChild(world);
		await PhysicsFrames(90);
		await Frames(10);
		var owners = new List<Owner>();
		var tris = new List<Tri>();
		int meshes = 0, degenerate = 0;
		foreach (var mi in Descendants(world).OfType<MeshInstance3D>())
		{
			if (!mi.IsVisibleInTree() || mi.Mesh == null || mi.Mesh is ImmediateMesh) continue;
			if (mi.GetParent() is Node3D && mi.Skeleton != null && !mi.Skeleton.IsEmpty) continue;
			var xf = mi.GlobalTransform;
			meshes++;
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
			{
				var mat = mi.MaterialOverride ?? mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
				if (!Opaque(mat)) continue;
				if (mi.Mesh is ArrayMesh am && am.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) continue;
				var arr = mi.Mesh.SurfaceGetArrays(s);
				var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
				if (v.Length == 0) continue;
				int[] idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
				Vector2[] uv = arr[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
				int count = idx != null && idx.Length > 0 ? idx.Length / 3 : v.Length / 3;
				int owner = owners.Count;
				string matName = mat == null ? "(none)" : mat is BaseMaterial3D bm && bm.AlbedoTexture != null ? (bm.AlbedoTexture.ResourcePath is { Length: > 0 } rp ? rp : bm.AlbedoTexture.ResourceName) : mat.GetClass();
				owners.Add(new Owner { Path = world.GetPathTo(mi) + $"#{s}", Material = matName, Tris = count });
				bool textured = mat is BaseMaterial3D tb && tb.AlbedoTexture != null && !tb.Uv1Triplanar;
				int collapsed = 0, nan = 0; float dMin = float.MaxValue, dMax = 0f; double worldArea = 0;
				for (int t = 0; t < count; t++)
				{
					int i0 = idx != null && idx.Length > 0 ? idx[t * 3] : t * 3, i1 = idx != null && idx.Length > 0 ? idx[t * 3 + 1] : t * 3 + 1, i2 = idx != null && idx.Length > 0 ? idx[t * 3 + 2] : t * 3 + 2;
					Vector3 a = xf * v[i0], b = xf * v[i1], c = xf * v[i2];
					Vector3 cr = (b - a).Cross(c - a);
					float area = cr.Length() * 0.5f;
					if (area < 1e-7f) { degenerate++; string key = level + " " + world.GetPathTo(mi); _degen[key] = _degen.GetValueOrDefault(key) + 1; continue; }
					worldArea += area;
					tris.Add(new Tri { A = a, B = b, C = c, N = cr / (area * 2f), Owner = owner });
					if (textured && uv != null)
					{
						Vector2 ua = uv[i0], ub = uv[i1], uc = uv[i2];
						if (float.IsNaN(ua.X + ub.X + uc.X + ua.Y + ub.Y + uc.Y)) { nan++; continue; }
						float uvArea = Mathf.Abs((ub - ua).Cross(uc - ua)) * 0.5f;
						if (area > 0.01f && uvArea < 1e-7f) { collapsed++; continue; }
						if (area > 0.005f)
						{
							float density = Mathf.Sqrt(uvArea / area);
							dMin = Mathf.Min(dMin, density);
							dMax = Mathf.Max(dMax, density);
						}
					}
				}
				if (nan > 0) _uv.Add((level, "NaN UVs", $"{owners[owner].Path} [{matName}] {nan} triangles"));
				if (collapsed > 0) _uv.Add((level, "collapsed UVs (smeared)", $"{owners[owner].Path} [{matName}] {collapsed} of {count} triangles"));
				if (dMax > 0f && dMin > 0f && dMax / dMin > 12f) _uv.Add((level, "texel density varies", $"{owners[owner].Path} [{matName}] {dMax / dMin:0}x ({dMin:0.00}..{dMax:0.00} uv/m)"));
			}
		}
		Log($"{level}: {meshes} meshes, {owners.Count} opaque surfaces, {tris.Count} triangles, {degenerate} degenerate");
		if (world.FindChild("SkiLodge", true, false) is Node3D lodge) _lodge = lodge.GlobalPosition;
		FindGhosts(level, world);
		FindFights(level, owners, tris);
		if (level == "winter")
		{
			FindPierce(level, owners, tris, o => o.Path.Contains("SkiLodge"), "lodge");
			FindPierce(level, owners, tris, o => o.Path.StartsWith("Church/") && !o.Path.Contains("WinterWoods") && !o.Path.Contains("WinterGlade"), "church");
		}
		RemoveChild(world);
		world.QueueFree();
		await Frames(5);
	}

	private void FindFights(string level, List<Owner> owners, List<Tri> tris)
	{
		// bucket by plane: quantized normal (both ways kept apart: back-to-back faces don't fight) and offset
		var buckets = new Dictionary<(int, int, int, int), List<Tri>>();
		foreach (var t in tris)
		{
			var nk = (Mathf.RoundToInt(t.N.X * 40f), Mathf.RoundToInt(t.N.Y * 40f), Mathf.RoundToInt(t.N.Z * 40f));
			int d = Mathf.RoundToInt(t.N.Dot(t.A) / PlaneTol);
			var key = (nk.Item1, nk.Item2, nk.Item3, d);
			if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<Tri>();
			list.Add(t);
		}
		var pairArea = new Dictionary<(int, int), (float area, Vector3 at, string ex)>();
		foreach (var (key, list) in buckets)
		{
			// the neighbouring offset bucket too (a plane on the boundary)
			var near = new List<Tri>(list);
			if (buckets.TryGetValue((key.Item1, key.Item2, key.Item3, key.Item4 + 1), out var up)) near.AddRange(up);
			if (near.Select(t => t.Owner).Distinct().Count() < 2) continue;
			Vector3 n = list[0].N;
			Vector3 u = (Mathf.Abs(n.Y) < 0.9f ? Vector3.Up : Vector3.Right).Cross(n).Normalized(), w = n.Cross(u);
			var proj = near.Select(t => (t, a: new Vector2(t.A.Dot(u), t.A.Dot(w)), b: new Vector2(t.B.Dot(u), t.B.Dot(w)), c: new Vector2(t.C.Dot(u), t.C.Dot(w)))).ToList();
			var box = proj.Select(p => (p, min: new Vector2(Mathf.Min(p.a.X, Mathf.Min(p.b.X, p.c.X)), Mathf.Min(p.a.Y, Mathf.Min(p.b.Y, p.c.Y))),
				max: new Vector2(Mathf.Max(p.a.X, Mathf.Max(p.b.X, p.c.X)), Mathf.Max(p.a.Y, Mathf.Max(p.b.Y, p.c.Y))))).OrderBy(x => x.min.X).ToList();
			for (int i = 0; i < box.Count; i++)
				for (int j = i + 1; j < box.Count && box[j].min.X < box[i].max.X; j++)
				{
					var (pi, mi, xi) = box[i];
					var (pj, mj, xj) = box[j];
					if (pi.t.Owner == pj.t.Owner || mj.Y > xi.Y || xj.Y < mi.Y) continue;
					if (pi.t.N.Dot(pj.t.N) < 0.999f || Mathf.Abs(pi.t.N.Dot(pj.t.A - pi.t.A)) > PlaneTol) continue;
					float a = Overlap(new[] { pi.a, pi.b, pi.c }, new[] { pj.a, pj.b, pj.c });
					if (a < 1e-5f) continue;
					var k = pi.t.Owner < pj.t.Owner ? (pi.t.Owner, pj.t.Owner) : (pj.t.Owner, pi.t.Owner);
					pairArea.TryGetValue(k, out var cur);
					pairArea[k] = (cur.area + a, cur.area > 0 ? cur.at : (pi.t.A + pi.t.B + pi.t.C) / 3f, cur.ex ?? $"{pi.t.A} {pi.t.B} {pi.t.C} n{pi.t.N} | {pj.t.A} {pj.t.B} {pj.t.C} n{pj.t.N}");
				}
		}
		foreach (var ((a, b), (area, at, ex)) in pairArea)
			if (area >= MinOverlap) _fights.Add((level, $"{owners[a].Path} [{owners[a].Material}]", $"{owners[b].Path} [{owners[b].Material}]", area, at, ex));
		Log($"{level}: {pairArea.Count(p => p.Value.area >= MinOverlap)} z-fighting pairs over {MinOverlap * 10000:0} cm²");
	}

	/// <summary>Walk-through props: a solid-looking mesh at body height with no collider in most of it (the player
	/// would pass through it). Things meant to be passed through or out of reach are left out by name.</summary>
	private void FindGhosts(string level, Node3D world)
	{
		var space = world.GetWorld3D().DirectSpaceState;
		var box = new BoxShape3D();
		var q = new PhysicsShapeQueryParameters3D { Shape = box, CollisionMask = 1u, CollideWithAreas = false, CollideWithBodies = true };
		int n = 0;
		foreach (var mi in Descendants(world).OfType<MeshInstance3D>())
		{
			if (!mi.IsVisibleInTree() || mi.Mesh == null) continue;
			string path = world.GetPathTo(mi).ToString().ToLowerInvariant();
			bool skip = false;
			foreach (var w in Passable) if (path.Contains(w)) { skip = true; break; }
			if (skip) continue;
			var mat = mi.MaterialOverride ?? mi.Mesh.SurfaceGetMaterial(0);
			if (!Opaque(mat)) continue;
			var aabb = mi.GlobalTransform * mi.GetAabb();
			Vector3 size = aabb.Size;
			// props only: a whole room's shell is hollow in the middle, which a box test there reads as walk-through
			if (size.Y < 0.45f || Mathf.Max(size.X, size.Z) < 0.3f || Mathf.Max(size.X, size.Z) > 3.5f || size.Y > 12f) continue;
			// only what stands at body height over something walkable
			var down = PhysicsRayQueryParameters3D.Create(aabb.GetCenter() with { Y = aabb.Position.Y + 0.05f }, aabb.GetCenter() with { Y = aabb.Position.Y - 3f }, 1u);
			var hit = space.IntersectRay(down);
			float groundY = hit.Count > 0 ? ((Vector3)hit["position"]).Y : aabb.Position.Y;
			if (aabb.Position.Y - groundY > 1.6f) continue;
			box.Size = size * 0.5f;
			q.Transform = new Transform3D(Basis.Identity, aabb.GetCenter());
			if (space.IntersectShape(q, 1).Count > 0) continue;
			_ghosts.Add((level, world.GetPathTo(mi).ToString(), aabb.GetCenter(), size));
			n++;
		}
		Log($"{level}: {n} walk-through props (solid-looking, body height, no collider)");
	}

	/// <summary>Meshes cutting through each other where it shows. For each pair of triangles of two different meshes
	/// that intersect (not coplanar: that's z-fighting), how far the second mesh's triangle reaches in front of the
	/// first's face and behind it. A sliver in front with the bulk behind is something poking out through a
	/// surface; a big reach both ways through a large face is a thing passing through a wall.</summary>
	private void FindPierce(string level, List<Owner> owners, List<Tri> all, Func<Owner, bool> inSet, string set)
	{
		var tris = all.Where(t => inSet(owners[t.Owner])).ToList();
		const float cell = 0.5f;
		var grid = new Dictionary<(int, int, int), List<int>>();
		for (int i = 0; i < tris.Count; i++)
		{
			var t = tris[i];
			Vector3 mn = t.A.Min(t.B).Min(t.C), mx = t.A.Max(t.B).Max(t.C);
			for (int x = Mathf.FloorToInt(mn.X / cell); x <= Mathf.FloorToInt(mx.X / cell); x++)
				for (int y = Mathf.FloorToInt(mn.Y / cell); y <= Mathf.FloorToInt(mx.Y / cell); y++)
					for (int z = Mathf.FloorToInt(mn.Z / cell); z <= Mathf.FloorToInt(mx.Z / cell); z++)
					{
						if (!grid.TryGetValue((x, y, z), out var l)) grid[(x, y, z)] = l = new List<int>();
						l.Add(i);
					}
		}
		var seen = new HashSet<long>();
		// per (surface owner, piercing owner): the largest reach in front of and behind the surface, and where
		var acc = new Dictionary<(int, int), (float front, float back, float area, Vector3 at)>();
		void Note(Tri surf, Tri other)
		{
			// (N is by the vertex winding, which points away from the face's seen side: Godot's front faces wind clockwise)
			float da = -surf.N.Dot(other.A - surf.A), db = -surf.N.Dot(other.B - surf.A), dc = -surf.N.Dot(other.C - surf.A);
			float front = Mathf.Max(0f, Mathf.Max(da, Mathf.Max(db, dc))), back = Mathf.Max(0f, -Mathf.Min(da, Mathf.Min(db, dc)));
			float area = (surf.B - surf.A).Cross(surf.C - surf.A).Length() * 0.5f;
			var k = (surf.Owner, other.Owner);
			acc.TryGetValue(k, out var cur);
			acc[k] = (Mathf.Max(cur.front, front), Mathf.Max(cur.back, back), Mathf.Max(cur.area, area), cur.front > 0 || cur.back > 0 ? cur.at : (other.A + other.B + other.C) / 3f);
		}
		foreach (var l in grid.Values)
			for (int i = 0; i < l.Count; i++)
				for (int j = i + 1; j < l.Count; j++)
				{
					int a = Math.Min(l[i], l[j]), b = Math.Max(l[i], l[j]);
					Tri ta = tris[a], tb = tris[b];
					if (ta.Owner == tb.Owner) continue;
					if (!seen.Add((long)a * 4000000L + b)) continue;
					if (Mathf.Abs(ta.N.Dot(tb.N)) > 0.999f) continue;   // parallel: never a clean cut (coplanar is the z-fight check's)
					if (!TrisIntersect(ta, tb)) continue;
					Note(ta, tb);
					Note(tb, ta);
				}
		int n = 0;
		foreach (var ((so, po), (front, back, area, at)) in acc)
		{
			string kind = null;
			if (front > 0.002f && front < 0.05f && back > 0.08f && area > 0.02f) kind = "pokes out through a surface (a sliver in front, the bulk behind)";
			else if (front > 0.12f && back > 0.5f && area > 0.3f) kind = "passes right through a wall";
			if (kind == null) continue;
			_pierce.Add((level + "/" + set, kind, $"{owners[so].Path} [{owners[so].Material}]", $"{owners[po].Path} [{owners[po].Material}]", front, back, at));
			n++;
		}
		Log($"{level}/{set}: {tris.Count} triangles, {n} pierce-throughs");
	}

	private static bool TrisIntersect(Tri p, Tri q)
	{
		// any edge of one through the other
		return SegTri(p.A, p.B, q) || SegTri(p.B, p.C, q) || SegTri(p.C, p.A, q) || SegTri(q.A, q.B, p) || SegTri(q.B, q.C, p) || SegTri(q.C, q.A, p);
	}

	private static bool SegTri(Vector3 s0, Vector3 s1, Tri t)
	{
		Vector3 dir = s1 - s0, e1 = t.B - t.A, e2 = t.C - t.A;
		Vector3 h = dir.Cross(e2);
		float a = e1.Dot(h);
		if (Mathf.Abs(a) < 1e-9f) return false;
		float f = 1f / a;
		Vector3 s = s0 - t.A;
		float u = f * s.Dot(h);
		if (u < 1e-4f || u > 1f - 1e-4f) return false;
		Vector3 q = s.Cross(e1);
		float v = f * dir.Dot(q);
		if (v < 1e-4f || u + v > 1f - 1e-4f) return false;
		float w = f * e2.Dot(q);
		return w > 1e-4f && w < 1f - 1e-4f;
	}

	/// <summary>The area two triangles share (Sutherland-Hodgman: clip one by the other's edges).</summary>
	private static float Overlap(Vector2[] subject, Vector2[] clip)
	{
		if (Cross2(clip[1] - clip[0], clip[2] - clip[0]) < 0) clip = new[] { clip[0], clip[2], clip[1] };
		var poly = new List<Vector2>(subject);
		for (int e = 0; e < 3 && poly.Count > 0; e++)
		{
			Vector2 p0 = clip[e], p1 = clip[(e + 1) % 3];
			var input = poly;
			poly = new List<Vector2>();
			for (int i = 0; i < input.Count; i++)
			{
				Vector2 cur = input[i], prev = input[(i + input.Count - 1) % input.Count];
				bool cIn = Cross2(p1 - p0, cur - p0) >= 0, pIn = Cross2(p1 - p0, prev - p0) >= 0;
				if (cIn)
				{
					if (!pIn) poly.Add(Intersect(prev, cur, p0, p1));
					poly.Add(cur);
				}
				else if (pIn) poly.Add(Intersect(prev, cur, p0, p1));
			}
		}
		float area = 0;
		for (int i = 0; i < poly.Count; i++) area += Cross2(poly[i], poly[(i + 1) % poly.Count]);
		return Mathf.Abs(area) * 0.5f;
	}

	private static float Cross2(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

	private static Vector2 Intersect(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
	{
		Vector2 r = b - a, s = q - p;
		float d = Cross2(r, s);
		if (Mathf.Abs(d) < 1e-9f) return a;
		float t = Cross2(p - a, s) / d;
		return a + r * t;
	}

	private void WriteReport()
	{
		string dir = ProjectSettings.GlobalizePath("res://test-output/clipping");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		var sb = new StringBuilder();
		sb.AppendLine($"Clip audit {DateTime.Now:s}");
		sb.Append(_log);
		sb.AppendLine();
		sb.AppendLine($"== Z-fighting: {_fights.Count} pairs (coplanar within {PlaneTol * 1000:0.0} mm, same facing, overlapping), worst first ==");
		foreach (var f in _fights.OrderByDescending(f => f.area))
			sb.AppendLine($"{f.area,8:0.000} m²  [{f.level}] at {Where(f.at)}\n    {f.a}\n    {f.b}\n      e.g. {f.ex}");
		sb.AppendLine();
		sb.AppendLine($"== Walk-through props: {_ghosts.Count} ==");
		foreach (var g in _ghosts.OrderByDescending(g => g.size.X * g.size.Y * g.size.Z))
			sb.AppendLine($"[{g.level}] {g.path} at {Where(g.at)} size ({g.size.X:0.00}x{g.size.Y:0.00}x{g.size.Z:0.00})");
		sb.AppendLine();
		sb.AppendLine($"== Pierce-through: {_pierce.Count} ==");
		foreach (var p2 in _pierce.OrderBy(p2 => p2.kind).ThenByDescending(p2 => p2.back))
			sb.AppendLine($"[{p2.level}] {p2.kind}: front {p2.front * 100:0.0} cm, back {p2.back * 100:0.0} cm at {Where(p2.at)}\n    surface {p2.surface}\n    through {p2.piercer}");
		sb.AppendLine();
		sb.AppendLine("== Degenerate triangles, most first ==");
		foreach (var (k, n) in _degen.OrderByDescending(d => d.Value).Take(15)) sb.AppendLine($"{n,7}  {k}");
		sb.AppendLine();
		sb.AppendLine($"== UV faults: {_uv.Count} ==");
		foreach (var u in _uv.OrderBy(u => u.what)) sb.AppendLine($"[{u.level}] {u.what}: {u.detail}");
		using var f2 = FileAccess.Open($"{dir}/report.txt", FileAccess.ModeFlags.Write);
		f2.StoreString(sb.ToString());
		Log($"report: {dir}/report.txt ({_fights.Count} z-fights, {_uv.Count} uv faults)");
	}
}
