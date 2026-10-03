using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The stains and rot of years of nobody (the fidelity pass, 2026-10-02): decals laid over an interior's walls and
/// floors, found rather than placed by hand. Over the interior's bounds, column by column (a few dozen a physics
/// frame, so it never stutters), rays find each floor under a ceiling (outdoors is left alone), then the walls round
/// it at waist height; a share of them get a decal from the place's theme: water risen from the floor and dried in
/// tide lines, drip streaks down from the ceiling, black mould, dried pools on the floors, puddles and wet seeps
/// below ground, the lodge's spilt wine, old blood in the bunker. Never on a door (anything that isn't a static
/// body), never two of a kind side by side. They only ever darken (no glare); they fade out with distance.
/// Deterministic from the seed.
/// </summary>
public partial class DecalDresser : Node3D
{
	public enum Theme { Station, Stair, Hallway, Sewer, Library, Church, Lodge, Bunker }

	private record struct Pick(string Tex, float Weight);
	private sealed class Set
	{
		public float Floor, Wall;
		public Pick[] Floors, Walls;
	}

	private static readonly Dictionary<Theme, Set> Sets = new()
	{
		[Theme.Station] = new() { Floor = 0.1f, Wall = 0.22f, Floors = new Pick[] { new("floor", 0.7f), new("puddle", 0.3f) }, Walls = new Pick[] { new("tide", 0.45f), new("streak", 0.35f), new("mould", 0.2f) } },
		[Theme.Stair] = new() { Floor = 0.05f, Wall = 0.2f, Floors = new Pick[] { new("floor", 1f) }, Walls = new Pick[] { new("streak", 0.6f), new("mould", 0.4f) } },
		[Theme.Hallway] = new() { Floor = 0.06f, Wall = 0.18f, Floors = new Pick[] { new("floor", 0.6f), new("puddle", 0.4f) }, Walls = new Pick[] { new("tide", 0.5f), new("streak", 0.5f) } },
		[Theme.Sewer] = new() { Floor = 0.12f, Wall = 0.3f, Floors = new Pick[] { new("puddle", 1f) }, Walls = new Pick[] { new("seep", 0.4f), new("mould", 0.3f), new("streak", 0.3f) } },
		[Theme.Library] = new() { Floor = 0.06f, Wall = 0.14f, Floors = new Pick[] { new("floor", 1f) }, Walls = new Pick[] { new("tide", 0.5f), new("mould", 0.5f) } },
		[Theme.Church] = new() { Floor = 0.05f, Wall = 0.14f, Floors = new Pick[] { new("floor", 0.5f), new("puddle", 0.5f) }, Walls = new Pick[] { new("tide", 0.4f), new("streak", 0.3f), new("mould", 0.3f) } },
		[Theme.Lodge] = new() { Floor = 0.07f, Wall = 0.12f, Floors = new Pick[] { new("wine", 0.45f), new("floor", 0.55f) }, Walls = new Pick[] { new("tide", 0.5f), new("streak", 0.3f), new("mould", 0.2f) } },
		[Theme.Bunker] = new() { Floor = 0.1f, Wall = 0.25f, Floors = new Pick[] { new("blood", 0.3f), new("puddle", 0.3f), new("floor", 0.4f) }, Walls = new Pick[] { new("blood", 0.2f), new("streak", 0.4f), new("mould", 0.2f), new("tide", 0.2f) } },
	};

	private static readonly Dictionary<string, (Texture2D albedo, Texture2D orm)> _tex = new();

	/// <summary>Dresses <paramref name="root"/>'s interior (its bounds: its own meshes', unless given). Safe to call
	/// more than once: a root is dressed once.</summary>
	public static void Dress(Node3D root, Theme theme, int seed, Aabb? bounds = null, System.Func<Vector3, bool> skipAt = null)
	{
		if (root == null || !IsInstanceValid(root) || root.HasMeta("decals_dressed")) return;
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-decals") >= 0) return;
		root.SetMeta("decals_dressed", true);
		var d = new DecalDresser { Name = "Decals", _theme = theme, _seed = seed, _bounds = bounds, _skipAt = skipAt };
		root.AddChild(d);
	}

	/// <summary>The station's lobby is kept at first (Act 13's decay): its stains come in with its stained wallpaper (stage 2).</summary>
	private Node3D _lobby;
	private double _lobbyCheck;

	public override void _Process(double delta)
	{
		if (_lobby == null || (_lobbyCheck -= delta) > 0) return;
		_lobbyCheck = 0.5;
		_lobby.Visible = (StationInterior.Instance?.Stage ?? 2) >= 2;   // (with its stained wallpaper)
	}

	/// <summary>How many it has laid (tests).</summary>
	public int Placed { get; private set; }
	/// <summary>Why columns found nothing (tests and tuning): no hit, not a floor, not fixed, outdoors, too low a ceiling.</summary>
	private readonly int[] _why = new int[6];
	public bool Finished { get; private set; }

	private Theme _theme;
	private int _seed;
	private Aabb? _bounds;
	private System.Func<Vector3, bool> _skipAt;
	private readonly List<Vector2> _columns = new();
	private int _next;
	private float _top, _bottom;
	private RandomNumberGenerator _rng;
	private readonly List<(Vector3 p, string tex)> _laid = new();
	private PhysicsRayQueryParameters3D _ray;
	private int _wait = 3;

	public override void _PhysicsProcess(double delta)
	{
		if (Finished) { SetPhysicsProcess(false); return; }
		if (_wait-- > 0) return;   // (the interior's colliders in the space first)
		if (_rng == null) Plan();
		var space = GetWorld3D().DirectSpaceState;
		for (int n = 0; n < 40 && _next < _columns.Count; n++, _next++) Column(space, _columns[_next]);
		if (_next >= _columns.Count) { Finished = true; GD.Print($"[decals] {GetParent().Name}: {Placed} laid ({_theme}) over {_columns.Count} columns; floors {_why[5]}, misses {_why[0]}, steep {_why[1]}, not fixed {_why[2]}, open {_why[3]}, low {_why[4]}"); }
	}

	private void Plan()
	{
		_rng = new RandomNumberGenerator { Seed = (ulong)(_seed * 7919 + 13) };
		_ray = new PhysicsRayQueryParameters3D { CollisionMask = 1 };
		var b = _bounds ?? MeshBounds(GetParent<Node3D>());
		_top = b.End.Y + 0.5f;
		_bottom = b.Position.Y - 0.5f;
		const float step = 1.7f;
		for (float x = b.Position.X + step * 0.5f; x < b.End.X; x += step)
			for (float z = b.Position.Z + step * 0.5f; z < b.End.Z; z += step)
				_columns.Add(new Vector2(x + _rng.RandfRange(-0.6f, 0.6f), z + _rng.RandfRange(-0.6f, 0.6f)));
	}

	private static Aabb MeshBounds(Node3D root)
	{
		Aabb box = new(root.GlobalPosition, Vector3.Zero);
		bool any = false;
		foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)n;
			if (mi.Mesh == null) continue;
			var a = mi.GlobalTransform * mi.Mesh.GetAabb();
			if (a.Size.Length() > 400f) continue;
			box = any ? box.Merge(a) : a;
			any = true;
		}
		return box;
	}

	private Godot.Collections.Dictionary Ray(PhysicsDirectSpaceState3D space, Vector3 a, Vector3 b)
	{
		_ray.From = a;
		_ray.To = b;
		return space.IntersectRay(_ray);
	}

	/// <summary>A static, fixed surface (not a door, a hatch, a moving thing, or a place left clean).</summary>
	private bool Fixed(Godot.Collections.Dictionary hit)
	{
		if (hit["collider"].AsGodotObject() is not StaticBody3D body) return false;
		string path = body.GetPath();
		foreach (var w in new[] { "Door", "door", "Hatch", "Gate", "Leaf" }) if (path.Contains(w)) return false;
		return true;
	}

	private void Column(PhysicsDirectSpaceState3D space, Vector2 xz)
	{
		var set = Sets[_theme];
		float y = _top;
		for (int level = 0; level < 5 && y > _bottom; level++)
		{
			var hit = Ray(space, new Vector3(xz.X, y, xz.Y), new Vector3(xz.X, _bottom, xz.Y));
			if (hit.Count == 0)
			{
				if (level == 0) _why[0]++;
				return;
			}
			Vector3 p = (Vector3)hit["position"], n = (Vector3)hit["normal"];
			y = p.Y - 0.3f;
			// a ramp or a flight (steeper than a floor, still underfoot): no stain on it, but the walls beside it count
			bool stair = n.Y < 0.85f && n.Y > 0.5f;
			if (n.Y < 0.85f && !stair) { _why[1]++; continue; }
			if (!Fixed(hit) || (_skipAt?.Invoke(p) ?? false)) { _why[2]++; continue; }
			// under a ceiling (within 6 m): indoors
			// (or walled in on most sides: some ceilings are drawn without colliders, the station's among them)
			var up = Ray(space, p + Vector3.Up * 0.2f, p + Vector3.Up * 6f);
			float ceiling;
			if (up.Count > 0) ceiling = ((Vector3)up["position"]).Y;
			else if (WalledIn(space, p + Vector3.Up * 1.1f, 7f) >= 3 || Corridor(space, p + Vector3.Up * 1.1f)) ceiling = p.Y + 2.8f;
			else
			{
				_why[3]++;
				continue;
			}
			if (ceiling - p.Y < 1.8f) { _why[4]++; continue; }   // (under a table, a stair: not a room's floor)
			_why[5]++;
			if (!stair && _rng.Randf() < set.Floor) FloorDecal(p, set);
			// the walls round it
			float a0 = _rng.RandfRange(0f, Mathf.Tau);
			for (int k = 0; k < 4; k++)
			{
				if (_rng.Randf() >= set.Wall) continue;
				float a = a0 + k * Mathf.Pi * 0.5f;
				Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
				Vector3 from = p + Vector3.Up * 1.1f;
				var w = Ray(space, from, from + dir * 1.3f);
				if (w.Count == 0) continue;
				Vector3 wn = (Vector3)w["normal"];
				if (Mathf.Abs(wn.Y) > 0.25f || !Fixed(w)) continue;
				WallDecal((Vector3)w["position"], wn.Normalized(), p.Y, ceiling, set);
			}
		}
	}

	/// <summary>How many of the four ways round <paramref name="at"/> meet a wall within <paramref name="reach"/>.</summary>
	internal static int WalledIn(PhysicsDirectSpaceState3D space, Vector3 at, float reach, Rid? exclude = null)
	{
		int n = 0;
		var q = new PhysicsRayQueryParameters3D { CollisionMask = 1 };
		if (exclude is { } ex) q.Exclude = new Godot.Collections.Array<Rid> { ex };
		foreach (var d in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
		{
			q.From = at;
			q.To = at + d * reach;
			var h = space.IntersectRay(q);
			if (h.Count > 0 && Mathf.Abs(((Vector3)h["normal"]).Y) < 0.4f) n++;
		}
		return n;
	}

	/// <summary>A corridor or a stairwell's flight: walls close on both sides, one way or the other (its ceiling may have
	/// no collider and its ends may be far off).</summary>
	private static bool Corridor(PhysicsDirectSpaceState3D space, Vector3 at)
	{
		var q = new PhysicsRayQueryParameters3D { CollisionMask = 1 };
		bool Hit(Vector3 d)
		{
			q.From = at;
			q.To = at + d * 3.5f;
			var h = space.IntersectRay(q);
			return h.Count > 0 && Mathf.Abs(((Vector3)h["normal"]).Y) < 0.4f;
		}
		return (Hit(Vector3.Right) && Hit(Vector3.Left)) || (Hit(Vector3.Forward) && Hit(Vector3.Back));
	}

	private string Choose(Pick[] picks)
	{
		float total = 0f;
		foreach (var p in picks) total += p.Weight;
		float r = _rng.Randf() * total;
		foreach (var p in picks) { if ((r -= p.Weight) <= 0f) return p.Tex; }
		return picks[^1].Tex;
	}

	private bool Crowded(Vector3 at, string tex, float r)
	{
		foreach (var (p, t) in _laid) if (t == tex && p.DistanceSquaredTo(at) < r * r) return true;
		return false;
	}

	private void FloorDecal(Vector3 p, Set set)
	{
		string tex = Choose(set.Floors);
		float s = tex switch { "wine" => _rng.RandfRange(0.45f, 0.8f), "puddle" => _rng.RandfRange(0.9f, 2.0f), "blood" => _rng.RandfRange(0.8f, 1.4f), _ => _rng.RandfRange(0.6f, 1.4f) };
		if (Crowded(p, tex, 2.2f)) return;
		var basis = new Basis(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau));
		Add(tex, new Transform3D(basis, p + Vector3.Up * 0.05f), new Vector3(s, 0.3f, s * _rng.RandfRange(0.75f, 1.2f)));
	}

	private void WallDecal(Vector3 at, Vector3 n, float floorY, float ceilY, Set set)
	{
		string tex = Choose(set.Walls);
		if (Crowded(at, tex, 2.0f)) return;
		float w, h, cy;
		switch (tex)
		{
			case "tide": w = _rng.RandfRange(1.0f, 2.2f); h = _rng.RandfRange(0.5f, 1.0f); cy = floorY + h * 0.5f - 0.02f; break;
			case "streak": case "seep":
				w = _rng.RandfRange(0.6f, 1.4f); h = _rng.RandfRange(1.0f, 2.0f);
				float topY = Mathf.Min(ceilY, floorY + 3.2f);
				cy = topY - h * 0.5f + 0.02f; break;
			case "blood": w = _rng.RandfRange(0.7f, 1.3f); h = w * 0.8f; cy = floorY + _rng.RandfRange(0.7f, 1.4f); break;
			default: w = _rng.RandfRange(0.4f, 1.0f); h = w; cy = floorY + (_rng.Randf() < 0.5f ? _rng.RandfRange(0.2f, 0.6f) : Mathf.Min(ceilY - 0.4f, floorY + _rng.RandfRange(1.8f, 2.6f))); break;
		}
		// the decal's -Y into the wall; its Z running up the wall (the texture's top at the top)
		Vector3 y = n, x = Vector3.Up.Cross(n).Normalized(), z = x.Cross(y).Normalized();
		if (z.Y > 0f) { z = -z; x = -x; }
		var basis = new Basis(x, y, z);
		Vector3 c = new(at.X, cy, at.Z);
		// a little along the wall, so they don't all sit where the rays struck
		c += x * _rng.RandfRange(-0.4f, 0.4f);
		Add(tex, new Transform3D(basis, c + n * 0.04f), new Vector3(w, 0.25f, h));
	}

	private void Add(string tex, Transform3D xf, Vector3 size)
	{
		var (albedo, orm) = Load(tex);
		if (albedo == null) return;
		if (_skipAt?.Invoke(xf.Origin) ?? false) return;
		var d = new Decal
		{
			Name = $"{tex}_{Placed}",
			Size = size,
			TextureAlbedo = albedo,
			TextureOrm = orm,
			Modulate = new Color(1f, 1f, 1f, tex is "puddle" or "seep" ? 0.9f : 0.8f),
			NormalFade = 0.4f,
			UpperFade = 0.1f,
			LowerFade = 0.1f,
			DistanceFadeEnabled = true,
			DistanceFadeBegin = 24f,
			DistanceFadeLength = 8f,
		};
		Node3D parent = this;
		if (_theme == Theme.Station && StationInterior.Instance is { } st && st.InLobby(xf.Origin))
		{
			if (_lobby == null) { _lobby = new Node3D { Name = "Lobby" }; AddChild(_lobby); }
			parent = _lobby;
		}
		parent.AddChild(d);
		d.GlobalTransform = xf;
		_laid.Add((xf.Origin, tex));
		Placed++;
	}

	private static (Texture2D, Texture2D) Load(string tex)
	{
		if (_tex.TryGetValue(tex, out var t)) return t;
		string a = $"res://assets/textures/decals/{tex}.png", o = $"res://assets/textures/decals/{tex}_orm.png";
		t = (ResourceLoader.Exists(a) ? GD.Load<Texture2D>(a) : null, ResourceLoader.Exists(o) ? GD.Load<Texture2D>(o) : null);
		_tex[tex] = t;
		return t;
	}
}
