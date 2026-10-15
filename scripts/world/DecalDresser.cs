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

	/// <summary>(2026-10-15) The render layer of things a stain never lands on (furniture built as its own instance, the
	/// pickups, anything that moves): every decal here leaves it out, with the player's hands and body.</summary>
	public const uint NoDecalLayer = 1u << 12;
	/// <summary>What the decals here project onto: everything but <see cref="NoDecalLayer"/>, the held item and the body.</summary>
	public const uint DecalMask = 0xFFFFFu & ~(NoDecalLayer | Player.HeldItem.HeldLayer | Player.PlayerBody.BodyLayer);

	/// <summary>Puts <paramref name="n"/>'s drawn parts (all of them under it) on <see cref="NoDecalLayer"/>: a piece of
	/// furniture or a pickup the stains must stop at. Only those still on the default layer (the held item's, the
	/// weather's, the webs' own layers are left be).</summary>
	public static void Shield(Node n)
	{
		if (n is VisualInstance3D v && n is not Light3D && n is not Decal && v.Layers == 1u) v.Layers = NoDecalLayer;
		foreach (var c in n.GetChildren()) Shield(c);
	}

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
	/// <remarks>(2026-10-15) Nor a piece of furniture: a body or shape marked "furniture" or built by the
	/// <see cref="ClutterKit"/> ("Clutter_" shapes in the room's own body), or a pickup's.</remarks>
	private bool Fixed(Godot.Collections.Dictionary hit)
	{
		if (hit["collider"].AsGodotObject() is not StaticBody3D body) return false;
		string path = body.GetPath();
		foreach (var w in new[] { "Door", "door", "Hatch", "Gate", "Leaf" }) if (path.Contains(w)) return false;
		if (IsFurniture(body)) return false;
		if (hit.TryGetValue("shape", out var si))
		{
			uint owner = body.ShapeFindOwner((int)si);
			if (body.ShapeOwnerGetOwner(owner) is Node shape && (shape.Name.ToString().StartsWith("Clutter_") || shape.HasMeta("furniture"))) return false;
		}
		return true;
	}

	/// <summary>Whether <paramref name="n"/> or one of its parents (up to four) is marked furniture.</summary>
	private static bool IsFurniture(Node n)
	{
		for (int i = 0; i < 5 && n != null; i++, n = n.GetParent())
			if (n.HasMeta("furniture") || n is Pickup) return true;
		return false;
	}

	/// <summary>(2026-10-15) A floor that is really a raised top (a table, a bench, a crate, a shelf: their tops took
	/// "floor" puddles sized for the room): two or more of four probes 0.9 m round it find the ground well below.</summary>
	private bool Raised(PhysicsDirectSpaceState3D space, Vector3 p)
	{
		int lower = 0;
		foreach (var d in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
		{
			var q = p + d * 0.9f;
			var h = Ray(space, q + Vector3.Up * 0.05f, q + Vector3.Down * 1.6f);
			if (h.Count > 0 && ((Vector3)h["normal"]).Y > 0.6f && ((Vector3)h["position"]).Y < p.Y - 0.25f) lower++;
		}
		return lower >= 2;
	}

	/// <summary>(2026-10-15) Whether a wall struck at <paramref name="at"/> (from <paramref name="from"/>, along
	/// <paramref name="dir"/>) runs on up: a ray the same way higher up meets it again about as far off. A wardrobe's or a
	/// cabinet's front doesn't (the wall is behind it), and neither does a counter's side.</summary>
	private bool WallGoesUp(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 dir, Vector3 at, float ceiling)
	{
		float d0 = (at - from).Dot(dir);
		float hy = Mathf.Min(ceiling - 0.25f, from.Y + 1.2f);
		if (hy < from.Y + 0.4f) return true;
		var hi = new Vector3(from.X, hy, from.Z);
		var h = Ray(space, hi, hi + dir * (d0 + 0.6f));
		return h.Count > 0 && ((Vector3)h["position"] - hi).Dot(dir) < d0 + 0.12f;
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
			if (!stair && _rng.Randf() < set.Floor && !Raised(space, p)) FloorDecal(p, set);
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
				if (Mathf.Abs(wn.Y) > 0.25f || !Fixed(w) || !WallGoesUp(space, from, dir, (Vector3)w["position"], ceiling)) continue;
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
		// (2026-10-15) shallow, on the floor's face: it projected 0.3 m from 5 cm up, staining chair legs, crates and pickups
		Add(tex, new Transform3D(basis, p + Vector3.Up * 0.01f), new Vector3(s, 0.1f, s * _rng.RandfRange(0.75f, 1.2f)));
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
		// (2026-10-15) shallow, on the wall's face (it reached 0.25 m out from 4 cm off it), and now and then mirrored
		if (_rng.Randf() < 0.5f) basis = new Basis(-basis.X, basis.Y, basis.Z);
		Add(tex, new Transform3D(basis, c + n * 0.006f), new Vector3(w, 0.08f, h));
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
			CullMask = DecalMask,
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

	// ------------------------------------------------------------------ placed by rule (2026-10-14g, the interiors kit)

	/// <summary>One decal, placed by hand: <paramref name="tex"/> one of assets/textures/decals (tide, streak, mould,
	/// floor, wine, blood, puddle, seep, crack, peel, scuff, ghost, ceiling, soot), at <paramref name="at"/> on a surface
	/// facing <paramref name="normal"/> (into the room), <paramref name="size"/> across and up it (its top toward the
	/// ceiling on a wall; on a floor or ceiling turned by <paramref name="spin"/>). In <paramref name="parent"/>'s space.
	/// It projects <paramref name="depth"/> deep, half each side of the surface: keep it shallow, or it stains the
	/// things in front of the wall too.</summary>
	/// <remarks>(2026-10-15) <paramref name="mirror"/> flips it left for right (one texture a kind: mirrored, it isn't the
	/// same mark twice). Every decal leaves out <see cref="NoDecalLayer"/>, the held item and the body.</remarks>
	public static Decal Place(Node3D parent, string tex, Vector3 at, Vector3 normal, Vector2 size, float alpha = 0.8f, float spin = 0f, float depth = 0.16f, bool mirror = false)
	{
		var (albedo, orm) = Load(tex);
		if (albedo == null || parent == null) return null;
		Vector3 y = normal.Normalized();
		Basis basis;
		if (Mathf.Abs(y.Y) > 0.7f)
		{
			Vector3 x = new Vector3(Mathf.Cos(spin), 0, Mathf.Sin(spin));
			Vector3 z = x.Cross(y).Normalized();
			x = y.Cross(z).Normalized();
			basis = new Basis(x, y, z);
		}
		else
		{
			// the decal's -Y into the wall; its Z running down the wall (the texture's top at the top)
			Vector3 x = Vector3.Up.Cross(y).Normalized(), z = x.Cross(y).Normalized();
			if (z.Y > 0f) { z = -z; x = -x; }
			basis = new Basis(x, y, z);
		}
		if (mirror) basis = new Basis(-basis.X, basis.Y, basis.Z);
		var d = new Decal
		{
			Name = $"{tex}_{parent.GetChildCount()}",
			Size = new Vector3(size.X, depth, size.Y),
			TextureAlbedo = albedo,
			TextureOrm = orm,
			Modulate = new Color(1f, 1f, 1f, alpha),
			NormalFade = 0.5f,
			UpperFade = 0.15f,
			LowerFade = 0.15f,
			DistanceFadeEnabled = true,
			DistanceFadeBegin = 22f,
			DistanceFadeLength = 8f,
			CullMask = DecalMask,
			Transform = new Transform3D(basis, at),
		};
		parent.AddChild(d);
		return d;
	}

	/// <summary>What a room's rule-based dressing needs to know (<see cref="Room"/>), in its node's space.</summary>
	public sealed class RoomSpec
	{
		/// <summary>The room's inside: floor at the bottom, ceiling at the top, walls its sides (axis-aligned).</summary>
		public Aabb Box;
		public int Seed = 1;
		/// <summary>Windows: their middle at the sill, on the wall's face, and the wall's normal into the room; width, height.</summary>
		public List<(Vector3 sill, Vector3 normal, float width, float height)> Windows = new();
		/// <summary>Doorways: their middle at the floor on the wall's face, the wall's normal into the room; width, height.</summary>
		public List<(Vector3 foot, Vector3 normal, float width, float height)> Doors = new();
		/// <summary>Flames (lamps, candles, a stove): soot up the nearest wall within a metre.</summary>
		public List<Vector3> Flames = new();
		/// <summary>Spots kept clean (a puzzle's notice, a mural): centre and radius.</summary>
		public List<(Vector3 at, float r)> Keep = new();
		/// <summary>How much of everything (1 a typical neglected room: about a dozen to twenty).</summary>
		public float Amount = 1f;
		/// <summary>Painted plaster or paper (peeling paint, picture ghosts); off for bare stone, brick, concrete, logs.</summary>
		public bool Painted = true;
		/// <summary>Faces a decal may never land on: a wall (its inward normal) skipped entirely (a wall of shelves, glass).</summary>
		public List<Vector3> SkipWalls = new();
		/// <summary>(2026-10-15) What stands in the room (furniture's boxes, in the same space): no wall stain behind it, no
		/// floor stain under it.</summary>
		public List<Aabb> Furniture = new();
	}

	/// <summary>
	/// Dresses a room by rule rather than by ray (deterministic from <see cref="RoomSpec.Seed"/>, no physics needed,
	/// placed at once): under each window a run of streaks and a seep; water risen in a tide at the walls' feet here and
	/// there; black mould in an upper corner or two; a stain or two on the ceiling; cracks from the corners of the
	/// openings' heads and down from the ceiling; on painted walls, flakes of peeling paint and the clean rectangles
	/// where pictures hung; scuffs low by the doors and on the floor in their way; soot up the wall over every flame; a
	/// dried stain on the floor. Returns the node holding them. About 15-25 decals a room at Amount 1.
	/// </summary>
	public static Node3D Room(Node3D parent, RoomSpec spec)
	{
		var root = new Node3D { Name = "RoomDecals" };
		parent.AddChild(root);
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-decals") >= 0) return root;
		var rng = new RandomNumberGenerator { Seed = (ulong)(spec.Seed * 7919 + 31) };
		Vector3 lo = spec.Box.Position, hi = spec.Box.End;
		float floorY = lo.Y, ceilY = hi.Y, h = ceilY - floorY;
		// the four walls: a point on each face, its normal into the room, its run (along, from, to)
		var walls = new List<(Vector3 n, Vector3 a, Vector3 b)>
		{
			(Vector3.Right, new Vector3(lo.X, floorY, lo.Z), new Vector3(lo.X, floorY, hi.Z)),
			(Vector3.Left, new Vector3(hi.X, floorY, hi.Z), new Vector3(hi.X, floorY, lo.Z)),
			(Vector3.Back, new Vector3(hi.X, floorY, lo.Z), new Vector3(lo.X, floorY, lo.Z)),
			(Vector3.Forward, new Vector3(lo.X, floorY, hi.Z), new Vector3(hi.X, floorY, hi.Z)),
		};
		walls.RemoveAll(w => spec.SkipWalls.Exists(s => s.Normalized().Dot(w.n) > 0.9f));
		const float lift = 0.01f;
		bool Clear(Vector3 p, float r)
		{
			foreach (var (k, kr) in spec.Keep) if (k.DistanceTo(p) < kr + r) return false;
			foreach (var (sill, n, w, wh) in spec.Windows)
				if (Mathf.Abs((p - sill).Dot(n)) < 0.3f && Mathf.Abs(new Vector2(p.X - sill.X, p.Z - sill.Z).Length()) < w * 0.5f + r && p.Y > sill.Y - 0.05f && p.Y < sill.Y + wh) return false;
			foreach (var (foot, n, w, dh) in spec.Doors)
				if (Mathf.Abs((p - foot).Dot(n)) < 0.3f && new Vector2(p.X - foot.X, p.Z - foot.Z).Length() < w * 0.5f + r && p.Y < foot.Y + dh + r) return false;
			// (2026-10-15) not behind or under what stands in the room
			foreach (var f in spec.Furniture)
				if (f.Grow(r * 0.6f + 0.15f).HasPoint(p)) return false;
			return true;
		}
		// (2026-10-15, the repetition pass) one texture a kind: each one a little bigger or smaller, fainter or stronger, and
		// mirrored half the time, so no two windows, doors or corners carry the same mark; shallow (0.08 m, half each side
		// of the face: it stained what stood in front at the old 0.16)
		Decal Put(string tex, Vector3 at, Vector3 n, Vector2 size, float alpha, float spin = 0f)
		{
			bool hangs = tex is "streak" or "seep" or "tide" or "soot";
			float sx = rng.RandfRange(0.8f, 1.2f), sy = hangs ? 1f : sx * rng.RandfRange(0.9f, 1.1f);
			return Place(root, tex, at, n, new Vector2(size.X * sx, size.Y * sy), Mathf.Min(1f, alpha * rng.RandfRange(0.82f, 1.08f)), spin, 0.08f, rng.Randf() < 0.5f);
		}
		void OnWall(string tex, float yCentre, Vector2 size, float alpha, float margin = 0.3f, int tries = 8)
		{
			if (walls.Count == 0) return;
			for (int t = 0; t < tries; t++)
			{
				var (n, a, b) = walls[rng.RandiRange(0, walls.Count - 1)];
				float len = a.DistanceTo(b);
				if (len < size.X + margin * 2f) continue;
				Vector3 p = a.Lerp(b, rng.RandfRange((margin + size.X * 0.5f) / len, 1f - (margin + size.X * 0.5f) / len));
				p.Y = yCentre;
				if (!Clear(p, size.X * 0.5f)) continue;
				Put(tex, p + n * lift, n, size, alpha);
				return;
			}
		}
		float amt = spec.Amount;
		int N(float x) => Mathf.Max(0, Mathf.RoundToInt(x * amt + rng.RandfRange(-0.4f, 0.4f)));
		// under the windows: streaks run down from the sill, a seep
		foreach (var (sill, n, w, wh) in spec.Windows)
		{
			float sh = Mathf.Max(0.4f, sill.Y - floorY);
			if (rng.Randf() < 0.75f) Put("streak", sill + n * lift + Vector3.Down * (sh * 0.5f), n, new Vector2(w * rng.RandfRange(0.8f, 1.2f), sh), 0.75f);
			if (rng.Randf() < 0.7f) Put("seep", sill + n * lift + Vector3.Down * (sh * 0.35f) + Vector3.Up.Cross(n).Normalized() * rng.RandfRange(-w * 0.3f, w * 0.3f), n, new Vector2(0.5f, sh * 0.7f), 0.7f);
			// a crack from a corner of its head
			if (rng.Randf() < 0.6f * amt)
			{
				Vector3 side = Vector3.Up.Cross(n).Normalized() * (rng.Randf() < 0.5f ? -1f : 1f);
				Vector3 c = sill + side * (w * 0.5f + 0.2f);
				c.Y = Mathf.Min(sill.Y + wh + 0.15f, ceilY - 0.4f);
				Put("crack", c + n * lift, n, new Vector2(0.45f, 0.7f), 0.85f);
			}
		}
		// by the doors: cracks off the head, scuffs low on the wall beside, scuffs on the floor in the way
		foreach (var (foot, n, w, dh) in spec.Doors)
		{
			Vector3 side = Vector3.Up.Cross(n).Normalized();
			if (rng.Randf() < 0.6f * amt)
			{
				float s = rng.Randf() < 0.5f ? -1f : 1f;
				Vector3 c = foot + side * s * (w * 0.5f + 0.25f) + Vector3.Up * Mathf.Min(dh + 0.35f, h - 0.35f);
				Put("crack", c + n * lift, n, new Vector2(0.4f, 0.7f), 0.85f);
			}
			foreach (float s in new[] { -1f, 1f })
				if (rng.Randf() < 0.75f) Put("scuff", foot + side * s * (w * 0.5f + rng.RandfRange(0.3f, 0.7f)) + Vector3.Up * rng.RandfRange(0.12f, 0.3f) + n * lift, n, new Vector2(rng.RandfRange(0.6f, 1.1f), 0.35f), 0.7f);
			Put("scuff", foot + n * rng.RandfRange(0.6f, 1.1f) + Vector3.Up * lift, Vector3.Up, new Vector2(rng.RandfRange(0.9f, 1.4f), 0.7f), 0.55f, rng.RandfRange(0f, Mathf.Tau));
		}
		// soot up the wall over every flame within a metre of one
		foreach (var f in spec.Flames)
		{
			(Vector3 n, float d) best = (Vector3.Zero, 99f);
			foreach (var (n, a, _) in walls)
			{
				float d = (f - a).Dot(n);
				if (d >= 0f && d < best.d) best = (n, d);
			}
			if (best.d < 1.1f)
			{
				float top = Mathf.Min(ceilY, f.Y + 1.8f);
				Vector3 at = f - best.n * best.d + best.n * lift;
				at.Y = (f.Y + top) * 0.5f + 0.1f;
				Put("soot", at, best.n, new Vector2(0.9f, top - f.Y), 0.7f);
			}
		}
		// mould in an upper corner or two
		var corners = new List<(Vector3 p, Vector3 n)>();
		foreach (var (n, a, b) in walls) { corners.Add((a, n)); corners.Add((b, n)); }
		for (int i = N(1.6f); i > 0 && corners.Count > 0; i--)
		{
			int ci = rng.RandiRange(0, corners.Count - 1);
			var (p, n) = corners[ci];
			corners.RemoveAt(ci);
			Vector3 into = ((spec.Box.GetCenter() - p) with { Y = 0f }).Normalized();
			Vector3 along = (into - n * into.Dot(n)).Normalized();
			float s = rng.RandfRange(0.5f, 0.9f);
			Vector3 at = p + along * (s * 0.4f) + Vector3.Up * (h - s * 0.4f) + n * lift;
			if (Clear(at, s * 0.5f)) Put("mould", at, n, new Vector2(s, s), 0.8f);
		}
		// water risen at the walls' feet, here and there
		for (int i = N(2f); i > 0; i--) { float th = rng.RandfRange(0.45f, 0.9f); OnWall("tide", floorY + th * 0.5f - 0.02f, new Vector2(rng.RandfRange(1.0f, 2.2f), th), 0.7f); }
		// streaks down from the ceiling
		for (int i = N(1.5f); i > 0; i--) { float sh = rng.RandfRange(0.8f, Mathf.Min(2.2f, h * 0.6f)); OnWall("streak", ceilY - sh * 0.5f + 0.02f, new Vector2(rng.RandfRange(0.6f, 1.3f), sh), 0.7f); }
		// a crack down from the ceiling
		for (int i = N(1f); i > 0; i--) OnWall("crack", ceilY - 0.4f, new Vector2(0.45f, 0.8f), 0.85f);
		if (spec.Painted)
		{
			for (int i = N(2f); i > 0; i--) { float s = rng.RandfRange(0.35f, 0.7f); OnWall("peel", floorY + rng.RandfRange(1.1f, h - 0.5f), new Vector2(s, s * rng.RandfRange(0.8f, 1.2f)), 0.85f); }
			for (int i = N(2f); i > 0; i--) { float w = rng.RandfRange(0.4f, 0.8f); OnWall("ghost", floorY + rng.RandfRange(1.45f, 1.75f), new Vector2(w, w * rng.RandfRange(0.8f, 1.35f)), 0.6f, 0.5f); }
		}
		// the ceiling's stains
		for (int i = N(1.5f); i > 0; i--)
		{
			float s = rng.RandfRange(0.8f, 1.6f);
			var at = new Vector3(rng.RandfRange(lo.X + s * 0.5f, hi.X - s * 0.5f), ceilY - lift, rng.RandfRange(lo.Z + s * 0.5f, hi.Z - s * 0.5f));
			Put("ceiling", at, Vector3.Down, new Vector2(s, s * rng.RandfRange(0.8f, 1.2f)), 0.75f, rng.RandfRange(0f, Mathf.Tau));
		}
		// a stain on the floor
		for (int i = N(1.2f); i > 0; i--)
		{
			float s = rng.RandfRange(0.6f, 1.2f);
			var at = new Vector3(rng.RandfRange(lo.X + 0.5f, hi.X - 0.5f), floorY + lift, rng.RandfRange(lo.Z + 0.5f, hi.Z - 0.5f));
			if (Clear(at, s * 0.5f)) Put("floor", at, Vector3.Up, new Vector2(s, s), 0.65f, rng.RandfRange(0f, Mathf.Tau));
		}
		GD.Print($"[decals] {parent.Name}: {root.GetChildCount()} placed by rule");
		return root;
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
