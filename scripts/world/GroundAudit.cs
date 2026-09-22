using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;

namespace ProjectDS.World;

/// <summary>
/// Dev tool (not used by the game): ground_audit.tscn. Loads each level's world scene on its own
/// (no GameFlow, no player, so nothing saves; the save slots are backed up and put back anyway),
/// waits for everything to build and settle, then for every placed object that should rest on the
/// ground measures how far its underside is above what it stands on:
/// <list type="bullet">
/// <item>the object's meshes are sampled in world space (vertices plus a grid over every triangle), split
/// into pieces (disconnected parts: a stone of a fire ring, a paver, a wheel);</item>
/// <item><c>clear</c> = height of a sample above the ground under it: terrain <see cref="ForestTerrain.HeightAt"/>,
/// or for pickups (and the frog, the camera in the boot) a downward physics ray plus the meshes they rest on
/// (the axe's stump, the key's stone, the frog's rock, the car);</item>
/// <item>FLOAT: the lowest clearance is above 5 cm (no contact anywhere);</item>
/// <item>HANG: the base hangs more than 10 cm over the ground in at least 3 footprint cells. The base is the
/// lowest sample of each cell, on a flat-ish face (not a rock's flank or a roof), of a piece that rests on the
/// ground, near that piece's lowest point. Stairs and buildings (skirted solids) use their outline instead:
/// every outline cell near the ground must reach it. Scatter meshes: only the main body counts (a log's stub,
/// a branch's twigs may stick up). "lip" = one or two cells only (a tilted box's edge), not a float;</item>
/// <item>pieces: parts that rest on neither the ground nor another part of the same object.</item>
/// </list>
/// Covers the trailhead, the Hollow as loaded, and the Hollow's Act 6 clearing (DebugReveal attaches the
/// dressing and the fifteen stairs). Report: test-output/grounding/report.txt (worst first).
/// Needs a window: the headless renderer keeps no MultiMesh buffers, so the scatter is skipped headless.
/// <c>-- --scatter=N</c> audits N random instances per scatter mesh (default 150).
/// <c>-- --shots=level:name,...</c> frames those objects from eye height (1.62 m, FOV 70) at 640x360 into
/// test-output/grounding/: "name@gap" looks at the largest gap (red bead on it, green on the ground under it),
/// "name~deg~m" swings the viewpoint round by deg and stands m away.
/// Run: <c>&lt;godot&gt; --path . res://scenes/levels/ground_audit.tscn --windowed [-- --shots=trailhead:ForgottenTent,hollow:Camp/Tent]</c>
/// </summary>
public partial class GroundAudit : Node
{
	private const float FloatLimit = 0.05f;
	private const float HangLimit = 0.10f;
	private const float Band = 0.3f;

	private enum GroundKind { Terrain, Ray }

	private class Target
	{
		public string Level, Name, Kind;
		public GroundKind Ground;
		public Transform3D RootXf = Transform3D.Identity;
		public Vector3 LocalGap;
		public string GapPiece;
		/// <summary>How far its highest point stands above the ground: near zero means buried.</summary>
		public float Exposed;
		public readonly List<(Mesh mesh, Transform3D xf)> Meshes = new();
		public bool Pieces = true;
		/// <summary>Surfaces it may rest on that are not measured themselves (a pickup's rest, the frog's rock).</summary>
		public readonly List<(Mesh mesh, Transform3D xf)> Support = new();
		public bool NoHang, Perimeter;
		/// <summary>Only the main body (the piece with the most samples) must sit down; its attachments (a log's
		/// broken stub, a branch's twigs) may stick up or out. Scatter meshes.</summary>
		public bool MainPieceOnly;
		// results
		public float MinClear, MaxGap, HangFrac;
		public int HangCells;
		public Vector3 GapAt, Center, Bottom;
		public Aabb Box;
		public readonly List<(float gap, float clear, Vector3 at)> Hovering = new();
		public bool Float => MinClear > FloatLimit;
		/// <summary>A hanging base shows over an area: a single footprint cell (a tilted box's edge, a lid's lip) is not a float.</summary>
		public bool Hang => MaxGap > HangLimit && HangCells >= 3;
		public float Score => Mathf.Max(MinClear, MaxGap);
	}

	private class Sampled
	{
		public Vector3[] P;
		public Vector3[] N;
		public int[] Comp;
		public int CompCount;
		public Aabb Box;
	}

	private readonly Dictionary<Mesh, Sampled> _cache = new();
	private readonly List<Target> _all = new();
	private readonly StringBuilder _log = new();
	private readonly List<string> _backedUp = new();
	private string _out;
	private ForestTerrain _terrain;
	private PhysicsDirectSpaceState3D _space;
	private HashSet<string> _shots;
	private int _scatterPerMesh = 150;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_out = ProjectSettings.GlobalizePath("res://test-output/grounding");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		using (FileAccess.Open("res://test-output/.gdignore", FileAccess.ModeFlags.Write)) { }
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a.StartsWith("--shots=")) _shots = new HashSet<string>(a.Substring(8).Split(',', StringSplitOptions.RemoveEmptyEntries));
			if (a.StartsWith("--scatter=")) _scatterPerMesh = int.Parse(a.Substring(10));
		}
		Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task PhysicsFrames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private void Log(string s) { GD.Print("[ground] " + s); _log.AppendLine(s); }

	private static string UserFile(string name) => ProjectSettings.GlobalizePath("user://" + name);

	private void BackUpSlots()
	{
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
		{
			string src = UserFile(slot);
			if (!FileAccess.FileExists(src)) continue;
			if (DirAccess.CopyAbsolute(src, UserFile("ground_audit_backup_" + slot)) == Error.Ok) _backedUp.Add(slot);
		}
	}

	private void RestoreSlots()
	{
		foreach (var slot in new[] { "save_a.cfg", "save_b.cfg" })
		{
			string dst = UserFile(slot), bak = UserFile("ground_audit_backup_" + slot);
			if (_backedUp.Contains(slot) && FileAccess.FileExists(bak))
			{
				DirAccess.CopyAbsolute(bak, dst);
				DirAccess.RemoveAbsolute(bak);
			}
			else if (FileAccess.FileExists(dst)) DirAccess.RemoveAbsolute(dst);
		}
	}

	private async void Run()
	{
		int code = 0;
		BackUpSlots();
		try
		{
			await Frames(5);
			await AuditLevel("trailhead", "res://scenes/levels/forest_world.tscn", false);
			await AuditLevel("hollow", "res://scenes/levels/hollow_world.tscn", false);
			await AuditLevel("hollow_act6", "res://scenes/levels/hollow_world.tscn", true);
			WriteReport();
		}
		catch (Exception e)
		{
			GD.PushError("[ground] " + e);
			code = 1;
		}
		finally
		{
			RestoreSlots();
		}
		GetTree().Quit(code);
	}

	// ------------------------------------------------------------------ a level

	private async Task AuditLevel(string level, string path, bool act6)
	{
		var world = GD.Load<PackedScene>(path).Instantiate<Node3D>();
		AddChild(world);
		// the scatter builds deferred, pickups settle over ~40 physics frames, buildings ground themselves
		await PhysicsFrames(90);
		await Frames(5);
		if (act6)
		{
			foreach (var ev in Descendants(world).OfType<Act6ClearingEvent>()) ev.DebugReveal();
			await PhysicsFrames(30);
		}
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		_space = world.GetWorld3D().DirectSpaceState;
		var targets = new List<Target>();
		Collect(level, world, world, targets, act6);
		await PhysicsFrames(1);
		foreach (var t in targets) Measure(t);
		_all.AddRange(targets);
		Log($"{level}: {targets.Count} objects, {targets.Count(t => t.Float)} floating, {targets.Count(t => t.Hang)} hanging, {targets.Count(t => t.Hovering.Count > 0)} with hovering pieces");

		if (_shots != null) await Shots(level, targets);

		RemoveChild(world);
		world.QueueFree();
		await Frames(5);
	}

	private static IEnumerable<Node> Descendants(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			yield return c;
			foreach (var d in Descendants(c)) yield return d;
		}
	}

	private static string PathOf(Node root, Node n) => root.GetPathTo(n).ToString();

	// ------------------------------------------------------------------ what gets measured

	private static bool IsTarget(Node n) => n is ParkProp or SignPost or Pickup or Camp or Woodpile or FriendTrail or FallenTree
		or Footbridge or Waterfall or Deer or Frog or Cabin or Shed or Bunker or StaircaseBuilder or DeepZoneDressing;

	private void Collect(string level, Node root, Node n, List<Target> into, bool act6)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is ForestScatter fs) { if (!act6) CollectScatter(level, root, fs, into); continue; }
			if (IsTarget(c))
			{
				string name = PathOf(root, c);
				string kind = c is ParkProp pp ? "ParkProp." + pp.Kind : c.GetType().Name;
				// in the Act 6 pass, only the clearing's pieces are new
				bool inClearing = name.StartsWith("Clearing");
				if (!act6 || inClearing)
				{
					switch (c)
					{
						case Pickup pk:
						{
							// the item rests on its rest (the axe's stump, the key's stone) or on whatever collider is under it
							var item = pk.GetNodeOrNull<Node3D>("Item");
							var rest = pk.GetNodeOrNull<Node3D>("Rest");
							var ti = item != null ? AddTarget(level, name + "/Item", "Pickup." + pk.Kind, GroundKind.Ray, item, into, true) : null;
							if (ti != null && rest != null) GatherMeshes(rest, ti.Support, true);
							if (rest != null) AddTarget(level, name + "/Rest", "PickupRest." + pk.Kind, GroundKind.Terrain, rest, into, true);
							break;
						}
						case ParkProp { Kind: ParkProp.PropKind.CameraItem } cam:
						{
							// in the car's boot: it rests on the car
							var t = AddTarget(level, name, kind, GroundKind.Ray, cam, into, true);
							if (t != null && cam.GetParent()?.GetParent() is ParkProp car) GatherMeshes(car, t.Support, true);
							break;
						}
						case Frog fr:
						{
							var rock = fr.GetNodeOrNull<Node3D>("Rock");
							if (rock != null) AddTarget(level, name + "/Rock", "Frog.Rock", GroundKind.Terrain, rock, into, true);
							var body = fr.GetNodeOrNull<Node3D>("Frog");
							var t = body != null ? AddTarget(level, name + "/Frog", "Frog", GroundKind.Ray, body, into, false) : null;
							if (t != null && rock != null) GatherMeshes(rock, t.Support, true);
							break;
						}
						case Footbridge fb:
						{
							// the deck spans the creek by design: only its posts and ends must meet the ground (pieces check)
							var fgen = fb.GetNodeOrNull<Node3D>("Generated");
							var deck = fgen?.GetNodeOrNull<Node3D>("BridgeMesh");
							var rocks = fgen?.GetNodeOrNull<Node3D>("RockMesh");
							var t = deck != null ? AddTarget(level, name + "/BridgeMesh", kind, GroundKind.Terrain, deck, into, true) : null;
							if (t != null) t.NoHang = true;
							if (rocks != null) AddTarget(level, name + "/RockMesh", "Footbridge.Rocks", GroundKind.Terrain, rocks, into, true);
							break;
						}
						case DeepZoneDressing dz:
							var gen = dz.GetNodeOrNull<Node3D>("Generated");
							if (gen != null)
								foreach (var g in gen.GetChildren().OfType<Node3D>())
									if (g.Name.ToString().StartsWith("Giant")) AddTarget(level, name + "/" + g.Name, "DeepZone.Giant", GroundKind.Terrain, g, into, false);
							break;
						case Cabin or Shed or Bunker or StaircaseBuilder:
						{
							var t = AddTarget(level, name, kind, GroundKind.Terrain, (Node3D)c, into, false);
							if (t != null) t.Perimeter = true;
							break;
						}
						case Waterfall wf:
						{
							// the water's sheets and foam lie on the water by design: only the rocks must rest
							var rocks = wf.GetNodeOrNull<Node3D>("RockMesh");
							if (rocks != null) AddTarget(level, name + "/RockMesh", "Waterfall.Rocks", GroundKind.Terrain, rocks, into, true);
							break;
						}
						default:
							AddTarget(level, name, kind, GroundKind.Terrain, (Node3D)c, into, true);
							break;
					}
				}
			}
			Collect(level, root, c, into, act6);
		}
	}

	/// <summary>Meshes under <paramref name="n"/>, not descending into other targets.</summary>
	private static void GatherMeshes(Node n, List<(Mesh, Transform3D)> into, bool top)
	{
		if (!top && IsTarget(n)) return;
		if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible) into.Add((mi.Mesh, mi.GlobalTransform));
		foreach (var c in n.GetChildren()) GatherMeshes(c, into, false);
	}

	private Target AddTarget(string level, string name, string kind, GroundKind g, Node3D node, List<Target> into, bool pieces)
	{
		var t = new Target { Level = level, Name = name, Kind = kind, Ground = g, Pieces = pieces, RootXf = node.GlobalTransform };
		GatherMeshes(node, t.Meshes, true);
		if (t.Meshes.Count == 0) return null;
		into.Add(t);
		return t;
	}

	private void CollectScatter(string level, Node root, ForestScatter fs, List<Target> into)
	{
		// the headless (dummy) renderer keeps no MultiMesh buffers: every instance would read as the origin
		if (DisplayServer.GetName() == "headless") { Log($"{level}: scatter skipped (run with a window to audit it)"); return; }
		var inst = fs.GetNodeOrNull("Instances");
		if (inst == null) return;
		var byKind = new Dictionary<string, List<(Mesh, Transform3D)>>();
		foreach (var mmi in inst.GetChildren().OfType<MultiMeshInstance3D>())
		{
			string key = mmi.Name.ToString();
			key = key.Substring(0, key.IndexOf('_', key.StartsWith("rock_") || key.StartsWith("boulder_") ? key.IndexOf('_') + 1 : 0));
			if (key is not ("rock_a" or "rock_b" or "boulder_a" or "boulder_b" or "log" or "stump" or "branch")) continue;
			var mm = mmi.Multimesh;
			if (!byKind.TryGetValue(key, out var list)) byKind[key] = list = new();
			for (int i = 0; i < mm.InstanceCount; i++) list.Add((mm.Mesh, mmi.GlobalTransform * mm.GetInstanceTransform(i)));
		}
		var rng = new RandomNumberGenerator { Seed = 4242 };
		foreach (var (key, list) in byKind)
		{
			// a seeded shuffle, then the first N
			for (int i = list.Count - 1; i > 0; i--) { int j = rng.RandiRange(0, i); (list[i], list[j]) = (list[j], list[i]); }
			foreach (var (mesh, xf) in list.Take(_scatterPerMesh))
			{
				var t = new Target { Level = level, Name = $"Scatter/{key}@({xf.Origin.X:0.0},{xf.Origin.Z:0.0})", Kind = "Scatter." + key, Ground = GroundKind.Terrain, Pieces = true, MainPieceOnly = true };
				t.Meshes.Add((mesh, xf));
				into.Add(t);
			}
			Log($"{level}: scatter {key}: {list.Count} instances, auditing {Mathf.Min(list.Count, _scatterPerMesh)}");
		}
	}

	// ------------------------------------------------------------------ sampling

	private Sampled Sample(Mesh mesh)
	{
		if (_cache.TryGetValue(mesh, out var s)) return s;
		var pts = new List<Vector3>();
		var nrm = new List<Vector3>();
		var aabb = mesh.GetAabb();
		float extent = Mathf.Max(aabb.Size.X, aabb.Size.Z);
		float step = Mathf.Clamp(extent / 40f, 0.03f, 0.2f);
		// union-find over quantised positions: disconnected parts of the mesh are separate pieces
		var parent = new List<int>();
		int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
		void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
		var ids = new Dictionary<Vector3I, int>();
		int Id(Vector3 v)
		{
			var q = new Vector3I(Mathf.RoundToInt(v.X * 500), Mathf.RoundToInt(v.Y * 500), Mathf.RoundToInt(v.Z * 500));
			if (!ids.TryGetValue(q, out int id)) { id = parent.Count; parent.Add(id); ids[q] = id; }
			return id;
		}
		var triPts = new List<(int start, int count, int vid)>();
		for (int si = 0; si < mesh.GetSurfaceCount(); si++)
		{
			var arr = mesh.SurfaceGetArrays(si);
			var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			var idxVar = arr[(int)Mesh.ArrayType.Index];
			int[] idx = idxVar.VariantType == Variant.Type.Nil ? null : idxVar.AsInt32Array();
			int triCount = idx != null && idx.Length > 0 ? idx.Length / 3 : v.Length / 3;
			for (int ti = 0; ti < triCount; ti++)
			{
				Vector3 a, b, c;
				if (idx != null && idx.Length > 0) { a = v[idx[ti * 3]]; b = v[idx[ti * 3 + 1]]; c = v[idx[ti * 3 + 2]]; }
				else { a = v[ti * 3]; b = v[ti * 3 + 1]; c = v[ti * 3 + 2]; }
				int ia = Id(a), ib = Id(b), ic = Id(c);
				Union(ia, ib); Union(ib, ic);
				int start = pts.Count;
				Vector3 tn = (b - a).Cross(c - a);
				tn = tn.LengthSquared() > 1e-12f ? tn.Normalized() : Vector3.Up;
				float maxEdge = Mathf.Max(a.DistanceTo(b), Mathf.Max(b.DistanceTo(c), c.DistanceTo(a)));
				int n = Mathf.Clamp(Mathf.CeilToInt(maxEdge / step), 1, 24);
				for (int i = 0; i <= n; i++)
					for (int j = 0; j <= n - i; j++)
					{
						float u = i / (float)n, w = j / (float)n;
						pts.Add(a + (b - a) * u + (c - a) * w);
						nrm.Add(tn);
					}
				triPts.Add((start, pts.Count - start, ia));
			}
		}
		var remap = new Dictionary<int, int>();
		var comp = new int[pts.Count];
		foreach (var (start, count, vid) in triPts)
		{
			int r = Find(vid);
			if (!remap.TryGetValue(r, out int cid)) { cid = remap.Count; remap[r] = cid; }
			for (int i = 0; i < count; i++) comp[start + i] = cid;
		}
		s = new Sampled { P = pts.ToArray(), N = nrm.ToArray(), Comp = comp, CompCount = remap.Count, Box = aabb };
		_cache[mesh] = s;
		return s;
	}

	private float RayGround(Vector3 p)
	{
		var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 0.12f, p + Vector3.Down * 6f, 1u);
		var hit = _space.IntersectRay(q);
		if (hit.Count == 0) return _terrain?.HeightAt(p.X, p.Z) ?? p.Y;
		return Mathf.Max(((Vector3)hit["position"]).Y, _terrain?.HeightAt(p.X, p.Z) ?? float.MinValue);
	}

	private void Measure(Target t)
	{
		// world samples with a global piece id and a "faces up or down" flag
		var pts = new List<Vector3>();
		var flat = new List<bool>();
		var comp = new List<int>();
		int compBase = 0;
		foreach (var (mesh, xf) in t.Meshes)
		{
			var s = Sample(mesh);
			Basis nb = xf.Basis.Inverse().Transposed();
			for (int i = 0; i < s.P.Length; i++)
			{
				pts.Add(xf * s.P[i]);
				flat.Add(Mathf.Abs((nb * s.N[i]).Normalized().Y) > 0.7f);
			}
			foreach (var c in s.Comp) comp.Add(compBase + c);
			compBase += s.CompCount;
		}
		if (pts.Count == 0) return;
		var box = new Aabb(pts[0], Vector3.Zero);
		foreach (var p in pts) box = box.Expand(p);
		t.Box = box;
		t.Center = box.GetCenter();
		float extent = Mathf.Max(box.Size.X, box.Size.Z);
		float cell = Mathf.Clamp(extent / 30f, 0.04f, 0.25f);

		// support surfaces (not measured): highest sample per 4 cm cell
		const float sc = 0.04f;
		var support = new Dictionary<Vector2I, List<float>>();
		foreach (var (mesh, xf) in t.Support)
			foreach (var sp in Sample(mesh).P)
			{
				Vector3 w = xf * sp;
				var key = new Vector2I(Mathf.FloorToInt(w.X / sc), Mathf.FloorToInt(w.Z / sc));
				if (!support.TryGetValue(key, out var l)) support[key] = l = new();
				l.Add(w.Y);
			}
		var ground = new float[pts.Count];
		var rayCache = new Dictionary<Vector2I, float>();
		float minClear = float.MaxValue;
		for (int i = 0; i < pts.Count; i++)
		{
			var p = pts[i];
			if (t.Ground == GroundKind.Ray)
			{
				var key = new Vector2I(Mathf.FloorToInt(p.X / 0.02f), Mathf.FloorToInt(p.Z / 0.02f));
				// cached per 2 cm cell; the ray starts just above the sample so it finds what is right under it
				if (!rayCache.TryGetValue(key, out float g)) { g = RayGround(p); rayCache[key] = g; }
				ground[i] = Mathf.Min(g, p.Y + 0.12f);
			}
			else ground[i] = _terrain.HeightAt(p.X, p.Z);
			if (support.Count > 0)
			{
				var key = new Vector2I(Mathf.FloorToInt(p.X / sc), Mathf.FloorToInt(p.Z / sc));
				for (int dx = -1; dx <= 1; dx++)
					for (int dz = -1; dz <= 1; dz++)
						if (support.TryGetValue(key + new Vector2I(dx, dz), out var l))
							foreach (float y in l)
								if (y <= p.Y + 0.02f && y > ground[i]) ground[i] = y;
			}
			if (p.Y - ground[i] < minClear) { minClear = p.Y - ground[i]; t.Bottom = p; }
		}
		t.MinClear = minClear;
		t.Exposed = float.MinValue;
		for (int i = 0; i < pts.Count; i++) t.Exposed = Mathf.Max(t.Exposed, pts[i].Y - ground[i]);

		// pieces: disconnected parts; one that rests on the ground has its own base
		bool usePieces = t.Pieces && compBase > 1;
		int nComp = usePieces ? compBase : 1;
		int C(int i) => usePieces ? comp[i] : 0;
		int main = -1;
		if (usePieces && t.MainPieceOnly)
		{
			var count = new int[compBase];
			foreach (int c in comp) count[c]++;
			for (int c = 0; c < compBase; c++) if (main < 0 || count[c] > count[main]) main = c;
		}
		var pMinY = new float[nComp];
		var pMinClear = new float[nComp];
		for (int c = 0; c < nComp; c++) { pMinY[c] = float.MaxValue; pMinClear[c] = float.MaxValue; }
		for (int i = 0; i < pts.Count; i++)
		{
			int c = C(i);
			pMinY[c] = Mathf.Min(pMinY[c], pts[i].Y);
			pMinClear[c] = Mathf.Min(pMinClear[c], pts[i].Y - ground[i]);
		}

		// the base: per footprint cell and piece, the lowest sample; it counts when it is near that piece's
		// lowest point and lies on a flat-ish face (a floor, a sole, a cap: not the flank of a rock or a roof)
		var cells = new Dictionary<Vector2I, int>();
		for (int i = 0; i < pts.Count; i++)
		{
			var key = new Vector2I(Mathf.FloorToInt(pts[i].X / cell), Mathf.FloorToInt(pts[i].Z / cell));
			if (!cells.TryGetValue(key, out int j) || pts[i].Y < pts[j].Y) cells[key] = i;
		}
		float maxGap = float.MinValue; int band = 0, hang = 0; int gapPiece = -1;
		foreach (var (key, i0) in cells)
		{
			int i = i0;
			int c = C(i);
			if (t.NoHang) break;
			float clear = pts[i].Y - ground[i];
			if (t.Perimeter)
			{
				// a solid with a skirt (stairs, buildings): only its outline shows; every outline cell near
				// the ground must reach it, whatever the face's angle (a wall's foot, a riser's bottom)
				bool edge = !cells.ContainsKey(key + Vector2I.Left) || !cells.ContainsKey(key + Vector2I.Right)
					|| !cells.ContainsKey(key + Vector2I.Up) || !cells.ContainsKey(key + Vector2I.Down);
				if (!edge) continue;
				// a lip (a coping, an eave, the log ends past a corner) can fill an outline cell on its own: the skirt
				// within ~0.4 m of it counts
				int reach = Mathf.Max(1, Mathf.CeilToInt(0.4f / cell));
				for (int dx = -reach; dx <= reach; dx++)
					for (int dz = -reach; dz <= reach; dz++)
						if (cells.TryGetValue(key + new Vector2I(dx, dz), out int j) && pts[j].Y - ground[j] < clear) { clear = pts[j].Y - ground[j]; i = j; }
				if (clear > 1.2f) continue;
			}
			else
			{
				if (main >= 0 && c != main) continue;
				if (usePieces && pMinClear[c] > FloatLimit) continue;   // not resting on the ground: see the pieces check
				if (pts[i].Y > pMinY[c] + Band && clear > pMinClear[c] + Band) continue;
				if (!flat[i]) continue;
			}
			band++;
			if (clear > HangLimit) hang++;
			if (clear > maxGap) { maxGap = clear; t.GapAt = pts[i]; gapPiece = usePieces ? comp[i] : -1; }
		}
		t.MaxGap = band > 0 ? maxGap : minClear;
		t.HangFrac = band > 0 ? hang / (float)band : 0f;
		t.HangCells = hang;
		t.LocalGap = t.RootXf.AffineInverse() * t.GapAt;
		if (gapPiece >= 0)
		{
			// which part hangs: its size and centre, to find it in the source
			Aabb pb = default; bool first = true;
			for (int i = 0; i < pts.Count; i++)
				if (comp[i] == gapPiece) { if (first) { pb = new Aabb(pts[i], Vector3.Zero); first = false; } else pb = pb.Expand(pts[i]); }
			t.GapPiece = $"part {pb.Size.X:0.00}x{pb.Size.Y:0.00}x{pb.Size.Z:0.00} at ({pb.GetCenter().X:0.00},{pb.GetCenter().Y:0.00},{pb.GetCenter().Z:0.00})";
		}

		if (!usePieces || t.MainPieceOnly) return;
		// a piece that does not rest on the ground must rest on another piece (3x3 cells of 6 cm)
		const float pc = 0.06f;
		var grid = new Dictionary<Vector2I, List<int>>();
		for (int i = 0; i < pts.Count; i++)
		{
			var key = new Vector2I(Mathf.FloorToInt(pts[i].X / pc), Mathf.FloorToInt(pts[i].Z / pc));
			if (!grid.TryGetValue(key, out var l)) grid[key] = l = new();
			l.Add(i);
		}
		var pieceGap = new Dictionary<int, (float gap, float clear, Vector3 at)>();
		for (int i = 0; i < pts.Count; i++)
		{
			int c = comp[i];
			if (pMinClear[c] <= FloatLimit) continue;
			var p = pts[i];
			float under = ground[i];
			var key = new Vector2I(Mathf.FloorToInt(p.X / pc), Mathf.FloorToInt(p.Z / pc));
			for (int dx = -1; dx <= 1; dx++)
				for (int dz = -1; dz <= 1; dz++)
					if (grid.TryGetValue(key + new Vector2I(dx, dz), out var l))
						foreach (int j in l)
							if (comp[j] != c && pts[j].Y <= p.Y + 0.02f && pts[j].Y > under) under = pts[j].Y;
			float gap = p.Y - under, clear = p.Y - ground[i];
			if (!pieceGap.TryGetValue(c, out var cur) || gap < cur.gap) pieceGap[c] = (gap, clear, p);
		}
		foreach (var (_, g) in pieceGap)
			if (g.gap > FloatLimit && g.clear < 1.0f) t.Hovering.Add(g);
		t.Hovering.Sort((a, b) => b.gap.CompareTo(a.gap));
	}

	// ------------------------------------------------------------------ report

	private void WriteReport()
	{
		var sb = new StringBuilder();
		sb.AppendLine("Ground audit " + Time.GetDatetimeStringFromSystem());
		sb.AppendLine($"FLOAT = lowest point more than {FloatLimit * 100:0} cm above the ground (no contact); HANG = the base hangs over {HangLimit * 100:0} cm in 3+ footprint cells; lip = 1-2 cells only (an edge), not a float;");
		sb.AppendLine("PIECE = a disconnected part resting on neither the ground nor another part. exposed = highest point above the ground (near 0 = buried). Units: metres. Ground = terrain, or a ray + the rest mesh (pickups, frog, camera item).");
		sb.AppendLine();
		sb.Append(_log);
		sb.AppendLine();
		int fl = _all.Count(t => t.Float), hg = _all.Count(t => t.Hang && !t.Float), pc = _all.Count(t => t.Hovering.Count > 0);
		sb.AppendLine($"TOTAL {_all.Count} objects: {fl} floating, {hg} hanging (not floating), {pc} with hovering pieces");
		sb.AppendLine();
		sb.AppendLine("== Offenders (worst first) ==");
		foreach (var t in _all.Where(t => t.Float || t.Hang || t.Hovering.Count > 0).OrderByDescending(t => Mathf.Max(t.Score, t.Hovering.Count > 0 ? t.Hovering[0].gap : 0)))
			sb.AppendLine(Line(t));
		sb.AppendLine();
		sb.AppendLine("== Scatter summary ==");
		foreach (var grp in _all.Where(t => t.Kind.StartsWith("Scatter.")).GroupBy(t => t.Level + " " + t.Kind))
		{
			var l = grp.ToList();
			sb.AppendLine($"{grp.Key}: {l.Count} audited, {l.Count(t => t.Float)} floating, {l.Count(t => t.Hang)} hanging, worst min clear {l.Max(t => t.MinClear):0.000}, worst gap {l.Max(t => t.MaxGap):0.000}, least exposed {l.Min(t => t.Exposed):0.00} (median {l.Select(t => t.Exposed).OrderBy(e => e).ElementAt(l.Count / 2):0.00})");
		}
		sb.AppendLine();
		sb.AppendLine("== Everything else (not scatter) ==");
		foreach (var t in _all.Where(t => !t.Kind.StartsWith("Scatter.")).OrderBy(t => t.Level).ThenByDescending(t => t.Score))
			sb.AppendLine(Line(t));
		using var f = FileAccess.Open(_out + "/report.txt", FileAccess.ModeFlags.Write);
		f.StoreString(sb.ToString());
		GD.Print($"[ground] TOTAL {_all.Count} objects: {fl} floating, {hg} hanging, {pc} with hovering pieces -> {_out}/report.txt");
	}

	private static string Line(Target t)
	{
		string flag = t.Float ? "FLOAT" : t.Hang ? "HANG " : t.Hovering.Count > 0 ? "PIECE" : t.MaxGap > HangLimit ? "lip  " : "ok   ";
		var s = $"{flag} [{t.Level}] {t.Name} ({t.Kind}) exposed {t.Exposed:0.00} minClear {t.MinClear:0.000} maxGap {t.MaxGap:0.000} at ({t.GapAt.X:0.00},{t.GapAt.Y:0.00},{t.GapAt.Z:0.00}) local ({t.LocalGap.X:0.00},{t.LocalGap.Y:0.00},{t.LocalGap.Z:0.00}) hang {t.HangFrac * 100:0}% ({t.HangCells} cells) size ({t.Box.Size.X:0.0}x{t.Box.Size.Y:0.0}x{t.Box.Size.Z:0.0})" + (t.GapPiece != null && t.MaxGap > HangLimit ? $" [{t.GapPiece}]" : "");
		if (t.Hovering.Count > 0)
			s += $" | {t.Hovering.Count} piece(s): " + string.Join(", ", t.Hovering.Take(4).Select(h => $"{h.gap:0.000} at ({h.at.X:0.00},{h.at.Y:0.00},{h.at.Z:0.00})"));
		return s;
	}

	// ------------------------------------------------------------------ screenshots

	private async Task Shots(string level, List<Target> targets)
	{
		if (DisplayServer.GetName() == "headless") return;
		var cam = new Camera3D { Fov = 70f, Near = 0.05f, Far = 800f };
		AddChild(cam);
		cam.MakeCurrent();
		foreach (var want in _shots)
		{
			// "level:name" limits a shot to one level
			string wl = null, wn = want;
			// "name~deg~m": swing the viewpoint round the object by deg, and stand m away (a trunk in the way)
			float turn = 0f, far = 0f;
			string tag = want.Replace('/', '_').Replace('@', '_').Replace('~', '_').Replace(':', '_');
			if (want.Contains('~'))
			{
				var parts = want.Split('~');
				wn = parts[0];
				if (parts.Length > 1) float.TryParse(parts[1], out turn);
				if (parts.Length > 2) float.TryParse(parts[2], out far);
			}
			if (wn.Contains(':')) { wl = wn[..wn.IndexOf(':')]; wn = wn[(wn.IndexOf(':') + 1)..]; }
			if (wl != null && wl != level) continue;
			// "name@gap" (or "name@gap6": from 6 m) frames the largest gap from outside the object
			int gi = wn.LastIndexOf("@gap", StringComparison.Ordinal);
			bool atGap = gi >= 0;
			float gapDist = atGap && wn.Length > gi + 4 && float.TryParse(wn[(gi + 4)..], out float gd) ? gd : 3.2f;
			string match = atGap ? wn[..gi] : wn;
			var t = targets.FirstOrDefault(x => x.Name.EndsWith(match)) ?? targets.FirstOrDefault(x => x.Name.Contains(match));
			if (t == null) continue;
			Vector3 c = atGap ? t.GapAt : t.Center;
			float size = Mathf.Max(t.Box.Size.X, Mathf.Max(t.Box.Size.Z, t.Box.Size.Y));
			float dist = atGap ? gapDist : Mathf.Clamp(size * 1.3f + 1.2f, 2.0f, 16f);
			// from the path side: toward the nearest trail point, else from +Z
			Vector3 dir = new(0, 0, 1);
			Vector3 outward = new(t.GapAt.X - t.Center.X, 0, t.GapAt.Z - t.Center.Z);
			if (atGap && outward.Length() > 0.2f) dir = outward.Normalized();
			else if (_terrain != null)
			{
				_terrain.TrailDistance(c.X, c.Z, out float along);
				Vector3 tp = _terrain.TrailPoint(along, out _);
				Vector3 d = new(tp.X - c.X, 0, tp.Z - c.Z);
				if (d.Length() > 0.5f) dir = d.Normalized();
				// look slightly across the slope too, so a hanging downhill edge shows
				dir = dir.Rotated(Vector3.Up, 0.35f);
			}
			dir = dir.Rotated(Vector3.Up, Mathf.DegToRad(turn));
			if (far > 0f) dist = far;
			Vector3 eye = c + dir * dist;
			eye.Y = _terrain.HeightAt(eye.X, eye.Z) + 1.62f;
			Vector3 look = atGap ? c : new(c.X, t.Box.Position.Y + Mathf.Min(t.Box.Size.Y, 1.2f) * 0.3f, c.Z);
			cam.GlobalPosition = eye;
			cam.LookAt(look, Vector3.Up);
			// @gap: a red bead on the gap, a green one on the ground under it
			var marks = new List<Node3D>();
			if (atGap)
				foreach (var (at, col) in new[] { (t.GapAt, new Color(1, 0, 0)), (new Vector3(t.GapAt.X, _terrain.HeightAt(t.GapAt.X, t.GapAt.Z), t.GapAt.Z), new Color(0, 1, 0)) })
				{
					var m = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = col, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true } };
					AddChild(m);
					m.GlobalPosition = at;
					marks.Add(m);
				}
			await Frames(20);
			var img = GetViewport().GetTexture().GetImage();
			string file = $"{level}_{tag[(tag.IndexOf(level) == 0 ? level.Length + 1 : 0)..]}.png";
			img.SavePng($"{_out}/{file}");
			Log($"shot {file} ({img.GetWidth()}x{img.GetHeight()}) of {t.Name}");
			foreach (var m in marks) m.QueueFree();
		}
		cam.QueueFree();
	}
}
