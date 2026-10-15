using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// A room's small things, put where people leave them (2026-10-14g, the interiors kit; the look survey: "most rooms are
/// 1-5 props in a large lit box"). Rules, after the PS2 horror rooms: about 70% against the walls or in the corners,
/// never floating mid-room for no reason; stacked (a box on a box, books on a crate); leaning (a frame, a board, a
/// broom against the wall); nothing square to the room (2-8 degrees off); nothing in a doorway or a kept-clear spot;
/// the same every time for a seed. Every piece goes into the caller's <see cref="MeshKit"/> (one mesh, no extra draws),
/// its footprint into the room's <see cref="RoomShade"/> (dark under and behind it) and, if big enough to trip over,
/// a box collider into the body given.
/// <para>Pieces are <see cref="Item"/>s: the modelled furniture (<see cref="Model"/>, any FurnitureKit piece in the
/// room's own materials), or the simple ones built here (<see cref="Carton"/>, <see cref="Frame"/>,
/// <see cref="Bucket"/>, <see cref="Broom"/>, <see cref="Board"/>, <see cref="Bundle"/>). Space: the kit's (the room's
/// node).</para>
/// </summary>
public sealed class ClutterKit
{
	/// <summary>Builds a piece standing at <paramref name="xf"/> (its origin the middle of its foot, its front toward
	/// -Z before the turn).</summary>
	public delegate void Builder(MeshKit k, Transform3D xf);

	/// <summary>A kind of piece: how to build it and how big it is (x across, y up, z deep).</summary>
	public sealed record Item(string Name, Builder Build, Vector3 Size, bool CanStackOn = false, bool Leans = false, bool Solid = true);

	/// <summary>Where a piece went (to stack on it).</summary>
	public sealed record Placed(Item Item, Transform3D Xf, Aabb Box);

	public readonly Aabb Room;
	public readonly RandomNumberGenerator Rng;
	public RoomShade Shade;
	public StaticBody3D Body;
	/// <summary>The yaw jitter's range, degrees (2-8: lived in, not tidy).</summary>
	public float JitterMin = 2f, JitterMax = 8f;
	private readonly List<Aabb> _taken = new();
	public readonly List<Placed> All = new();

	public ClutterKit(Aabb room, int seed, RoomShade shade = null, StaticBody3D body = null)
	{
		Room = room;
		Rng = new RandomNumberGenerator { Seed = (ulong)(seed * 104729 + 7) };
		Shade = shade;
		Body = body;
	}

	/// <summary>Keeps a spot clear (a doorway's swing, a path, a puzzle): nothing is put there.</summary>
	public ClutterKit Keep(Vector3 at, float radius) { _taken.Add(new Aabb(at - new Vector3(radius, 0.5f, radius), new Vector3(radius * 2f, 3f, radius * 2f))); return this; }
	/// <summary>Marks what's already standing in the room (the builder's own furniture) as taken.</summary>
	public ClutterKit Keep(Aabb box) { _taken.Add(box); return this; }

	// ------------------------------------------------------------------ the pieces

	/// <summary>A modelled FurnitureKit piece in the room's materials (null if it isn't there).</summary>
	public static Item Model(string model, Dictionary<string, Material> roles, bool canStackOn = false, float scale = 1f, bool solid = true)
	{
		if (FurnitureKit.Bounds(model) is not { } b) return null;
		Vector3 sz = b.Size * scale;
		return new Item(model, (k, xf) => FurnitureKit.Add(k, model, xf * new Transform3D(Basis.FromScale(Vector3.One * scale), Vector3.Zero), roles), sz, canStackOn, false, solid);
	}

	/// <summary>A carton or a wooden box, its lid's seam and its edges eased.</summary>
	public static Item Carton(Material m, Vector3 size, Color? tint = null) => new("carton", (k, xf) =>
	{
		k.Mat(m);
		var keep = k.Color;
		k.Color = tint ?? Colors.White;
		var b = xf.Basis;
		k.BevelBox(xf * new Vector3(0, size.Y * 0.5f, 0), size, Mathf.Min(0.012f, size.Y * 0.1f), 1f, b);
		// the lid's flaps, a hair proud, parted down the middle
		k.Color = (tint ?? Colors.White) * 0.8f;
		foreach (float s in new[] { -1f, 1f })
			k.Box(xf * new Vector3(s * size.X * 0.25f, size.Y + 0.004f, 0), new Vector3(size.X * 0.5f - 0.01f, 0.006f, size.Z - 0.01f), 1f, b);
		k.Color = keep;
	}, size, true);

	/// <summary>A picture frame (back to the room or face out), for leaning against a wall: moulding round a canvas.</summary>
	public static Item Frame(Material wood, Material canvas, float w = 0.6f, float h = 0.8f) => new("frame", (k, xf) =>
	{
		const float t = 0.03f, f = 0.06f;
		var b = xf.Basis;
		Vector3 P(float x, float y) => xf * new Vector3(x, y, 0);
		k.Mat(wood);
		k.BevelBox(P(0, f * 0.5f), new Vector3(w, f, t), 0.008f, 1f, b);
		k.BevelBox(P(0, h - f * 0.5f), new Vector3(w, f, t), 0.008f, 1f, b);
		k.BevelBox(P(-w * 0.5f + f * 0.5f, h * 0.5f), new Vector3(f, h - f * 2f, t), 0.008f, 1f, b);
		k.BevelBox(P(w * 0.5f - f * 0.5f, h * 0.5f), new Vector3(f, h - f * 2f, t), 0.008f, 1f, b);
		k.Mat(canvas);
		var keep = k.Color;
		k.Color = keep * 0.55f;   // (the canvas's back, dark: told apart by colour if it shares a material)
		k.Box(P(0, h * 0.5f), new Vector3(w - f * 1.6f, h - f * 1.6f, 0.008f), 1f, b);
		k.Color = keep;
	}, new Vector3(w, h, 0.03f), false, true, false);

	/// <summary>A board or a plank, for leaning.</summary>
	public static Item Board(Material wood, float len = 1.6f, float w = 0.18f, float t = 0.025f) => new("board", (k, xf) =>
	{
		k.Mat(wood);
		k.BevelBox(xf * new Vector3(0, len * 0.5f, 0), new Vector3(w, len, t), 0.005f, 1f, xf.Basis);
	}, new Vector3(w, len, t), false, true, false);

	/// <summary>A broom, for leaning: a pole and a bristled head.</summary>
	public static Item Broom(Material wood, Material bristle) => new("broom", (k, xf) =>
	{
		// (2026-10-15, the props pass: a round handle with a rounded end, set into a stock with a domed top, the bristles
		// splaying out below it; it was a hexagonal stick on two boxes)
		k.Mat(wood);
		TurnedKit.Lathe(k, xf * new Vector3(0, 0.215f, 0), TurnedKit.P(0f, 0f, 0.0145f, 0f, 0.0145f, 1.12f, 0.011f, 1.13f, 0f, 1.135f), 8);
		k.BevelBox(xf * new Vector3(0, 0.2f, 0), new Vector3(0.28f, 0.045f, 0.06f), 0.016f, 1f, xf.Basis);
		k.Mat(bristle);
		var keep = k.Color;
		k.Color = keep * 0.8f;
		// the bristles: a block flaring from the stock's width to a little wider at the floor, ragged at the tips
		var b = xf.Basis;
		Vector3 P(float x, float y, float z) => xf * new Vector3(x, y, z);
		var top = new[] { P(-0.125f, 0.178f, -0.022f), P(0.125f, 0.178f, -0.022f), P(0.125f, 0.178f, 0.022f), P(-0.125f, 0.178f, 0.022f) };
		var bot = new[] { P(-0.138f, 0.0f, -0.03f), P(0.138f, 0.0f, -0.03f), P(0.138f, 0.0f, 0.03f), P(-0.138f, 0.0f, 0.03f) };
		for (int i = 0; i < 4; i++)
		{
			int j = (i + 1) % 4;
			Vector3 n = ((top[i] + top[j] + bot[i] + bot[j]) * 0.25f - xf * new Vector3(0, 0.09f, 0)).Normalized();
			k.Quad(bot[i], bot[j], top[j], top[i], n);
		}
		k.Quad(bot[0], bot[1], bot[2], bot[3], -b.Y.Normalized());
		k.Color = keep;
	}, new Vector3(0.28f, 1.35f, 0.06f), false, true, false);

	/// <summary>A tin bucket (a pail), its handle down.</summary>
	public static Item Bucket(Material metal) => new("bucket", (k, xf) =>
	{
		// (2026-10-15, the props pass: a pressed pail, its foot ring, two swaged hoops, a rolled rim and its inside down to
		// the bottom, the ears and the wire bail lying down; it was a capped ten-sided cone)
		k.Mat(metal);
		var keep = k.Color;
		TurnedKit.Lathe(k, xf, TurnedKit.P(0f, 0.006f, 0.112f, 0.006f, 0.116f, 0f, 0.122f, 0f, 0.121f, 0.012f, 0.124f, 0.012f, 0.128f, 0.07f, 0.131f, 0.074f, 0.132f, 0.08f,
			0.14f, 0.2f, 0.143f, 0.204f, 0.144f, 0.21f, 0.15f, 0.272f, 0.158f, 0.276f, 0.16f, 0.284f, 0.155f, 0.29f, 0.147f, 0.284f,
			0.143f, 0.27f, 0.118f, 0.025f, 0f, 0.025f), 14, 40f);
		k.Color = keep * 0.85f;
		foreach (float s in new[] { -1f, 1f })
		{
			var ear = xf * new Transform3D(new Basis(Vector3.Back, s * Mathf.Pi * 0.5f), new Vector3(s * 0.149f, 0.245f, 0));
			TurnedKit.Lathe(k, ear, TurnedKit.P(0f, 0f, 0.016f, 0f, 0.013f, 0.008f, 0f, 0.009f), 8);
		}
		// the bail, dropped down against the side
		var bail = new List<Vector3>();
		for (int i = 0; i <= 10; i++)
		{
			float a = Mathf.Pi * i / 10f;
			bail.Add(xf * new Vector3(-Mathf.Cos(a) * 0.158f, 0.245f - Mathf.Sin(a) * 0.04f, -Mathf.Sin(a) * 0.162f));
		}
		TurnedKit.Rod(k, bail, 0.0035f, 4, false);
		k.Color = keep;
	}, new Vector3(0.3f, 0.29f, 0.3f), false, false, true);

	/// <summary>A tied bundle (papers, hymnals, cloth): a soft slab with string round it, <paramref name="tint"/> its
	/// colour (the twine paler), so one material serves a room's small things.</summary>
	public static Item Bundle(Material m, Material twine, Vector3 size, Color? tint = null) => new("bundle", (k, xf) =>
	{
		var keep = k.Color;
		Color c = tint ?? Colors.White;
		k.Mat(m);
		k.Color = c;
		k.BevelBox(xf * new Vector3(0, size.Y * 0.5f, 0), size, Mathf.Min(0.01f, size.Y * 0.2f), 1f, xf.Basis);
		k.Mat(twine);
		k.Color = twine == m ? new Color(1f, 0.95f, 0.85f) : Colors.White;
		k.Box(xf * new Vector3(0, size.Y * 0.5f, 0), new Vector3(0.008f, size.Y + 0.004f, size.Z + 0.004f), 1f, xf.Basis);
		k.Box(xf * new Vector3(0, size.Y * 0.5f, 0), new Vector3(size.X + 0.004f, size.Y + 0.004f, 0.008f), 1f, xf.Basis);
		k.Color = keep;
	}, size, true);

	// ------------------------------------------------------------------ the rules

	private float Jitter() => Mathf.DegToRad(Rng.RandfRange(JitterMin, JitterMax) * (Rng.Randf() < 0.5f ? -1f : 1f));

	/// <summary>The room's walls: (inward normal, a point at one end on the floor, the other end).</summary>
	private (Vector3 n, Vector3 a, Vector3 b)[] Walls()
	{
		Vector3 lo = Room.Position, hi = Room.End;
		float y = lo.Y;
		return new[]
		{
			(Vector3.Right, new Vector3(lo.X, y, lo.Z), new Vector3(lo.X, y, hi.Z)),
			(Vector3.Left, new Vector3(hi.X, y, hi.Z), new Vector3(hi.X, y, lo.Z)),
			(Vector3.Back, new Vector3(hi.X, y, lo.Z), new Vector3(lo.X, y, lo.Z)),
			(Vector3.Forward, new Vector3(lo.X, y, hi.Z), new Vector3(hi.X, y, hi.Z)),
		};
	}

	private static float YawFacing(Vector3 n) => Mathf.Atan2(-n.X, -n.Z);

	private Aabb Footprint(Transform3D xf, Vector3 size)
	{
		var a = new Aabb(new Vector3(-size.X * 0.5f, 0, -size.Z * 0.5f), size);
		return xf * a;
	}

	private bool Fits(Aabb box)
	{
		var shrunk = box.Grow(-0.01f);
		var inRoom = Room.Grow(0.02f);
		if (!inRoom.Encloses(new Aabb(box.Position with { Y = Room.Position.Y }, box.Size with { Y = 0.01f }))) return false;
		foreach (var t in _taken) if (t.Intersects(shrunk)) return false;
		return true;
	}

	private Placed Put(MeshKit k, Item it, Transform3D xf, bool onFloor = true)
	{
		var box = Footprint(xf, it.Size);
		it.Build(k, xf);
		_taken.Add(box);
		var p = new Placed(it, xf, box);
		All.Add(p);
		if (onFloor) Shade?.Under(box, it.Size.Y > 0.5f ? 0.45f : 0.3f, Mathf.Clamp(Mathf.Max(it.Size.X, it.Size.Z) * 0.6f, 0.15f, 0.5f));
		if (Body != null && it.Solid && it.Size.Y > 0.25f)
			Body.AddChild(new CollisionShape3D { Name = "Clutter_" + it.Name, Transform = new Transform3D(xf.Basis.Orthonormalized(), xf * new Vector3(0, it.Size.Y * 0.5f, 0)), Shape = new BoxShape3D { Size = it.Size * 0.95f } });
		return p;
	}

	/// <summary>Against a wall (<paramref name="wall"/> 0 -X, 1 +X, 2 -Z, 3 +Z; any if -1), its back a finger's
	/// breadth off it, facing the room, turned a few degrees. Null if nowhere fits.</summary>
	public Placed AgainstWall(MeshKit k, Item it, int wall = -1, int tries = 14)
	{
		if (it == null) return null;
		var walls = Walls();
		for (int t = 0; t < tries; t++)
		{
			var (n, a, b) = walls[wall >= 0 ? wall : Rng.RandiRange(0, 3)];
			float len = a.DistanceTo(b), half = it.Size.X * 0.5f + 0.05f;
			if (len < half * 2f) continue;
			Vector3 p = a.Lerp(b, Rng.RandfRange(half / len, 1f - half / len)) + n * (it.Size.Z * 0.5f + Rng.RandfRange(0.01f, 0.05f));
			var xf = new Transform3D(new Basis(Vector3.Up, YawFacing(n) + Jitter()), p);
			if (Fits(Footprint(xf, it.Size))) return Put(k, it, xf);
		}
		return null;
	}

	/// <summary>Into a corner (any of the four if <paramref name="corner"/> is -1), square to one wall and a few
	/// degrees off it.</summary>
	public Placed InCorner(MeshKit k, Item it, int corner = -1, int tries = 8)
	{
		if (it == null) return null;
		Vector3 lo = Room.Position, hi = Room.End;
		for (int t = 0; t < tries; t++)
		{
			int c = corner >= 0 ? corner : Rng.RandiRange(0, 3);
			float sx = (c & 1) == 0 ? 1f : -1f, sz = (c & 2) == 0 ? 1f : -1f;
			Vector3 at = new((c & 1) == 0 ? lo.X : hi.X, lo.Y, (c & 2) == 0 ? lo.Z : hi.Z);
			// facing out along one wall or the other
			bool alongX = Rng.Randf() < 0.5f;
			Vector3 face = alongX ? new Vector3(0, 0, sz) : new Vector3(sx, 0, 0);
			float yaw = YawFacing(face) + Jitter();
			float ex = alongX ? it.Size.X : it.Size.Z, ez = alongX ? it.Size.Z : it.Size.X;
			Vector3 p = at + new Vector3(sx * (ex * 0.5f + Rng.RandfRange(0.02f, 0.06f)), 0, sz * (ez * 0.5f + Rng.RandfRange(0.02f, 0.06f)));
			var xf = new Transform3D(new Basis(Vector3.Up, yaw), p);
			if (Fits(Footprint(xf, it.Size))) return Put(k, it, xf);
		}
		return null;
	}

	/// <summary>On top of something already placed (a box on a box, a bundle on a crate): a little off its middle and
	/// turned its own way. Null if it doesn't fit on it.</summary>
	public Placed On(MeshKit k, Placed under, Item it)
	{
		if (under == null || it == null) return null;
		var us = under.Item.Size;
		if (it.Size.X > us.X * 1.1f && it.Size.Z > us.Z * 1.1f) return null;
		float ox = Mathf.Max(0f, (us.X - it.Size.X) * 0.5f), oz = Mathf.Max(0f, (us.Z - it.Size.Z) * 0.5f);
		Vector3 local = new(Rng.RandfRange(-ox, ox), us.Y + 0.002f, Rng.RandfRange(-oz, oz));
		var xf = new Transform3D(under.Xf.Basis * new Basis(Vector3.Up, Jitter() * 1.5f), under.Xf * local);
		var box = Footprint(xf, it.Size);
		it.Build(k, xf);
		var p = new Placed(it, xf, box);
		All.Add(p);
		return p;
	}

	/// <summary>Leaning against a wall: its foot kicked out, its top resting on the wall, 8-16 degrees off upright.</summary>
	public Placed Lean(MeshKit k, Item it, int wall = -1, int tries = 12)
	{
		if (it == null) return null;
		var walls = Walls();
		for (int t = 0; t < tries; t++)
		{
			var (n, a, b) = walls[wall >= 0 ? wall : Rng.RandiRange(0, 3)];
			float len = a.DistanceTo(b), half = it.Size.X * 0.5f + 0.05f;
			if (len < half * 2f) continue;
			float tilt = Mathf.DegToRad(Rng.RandfRange(8f, 16f));
			float foot = it.Size.Y * Mathf.Sin(tilt) + it.Size.Z * 0.5f + 0.01f;
			Vector3 p = a.Lerp(b, Rng.RandfRange(half / len, 1f - half / len)) + n * foot;
			// facing the room, then its top tipped back toward the wall (+Z, the wall side)
			var basis = new Basis(Vector3.Up, YawFacing(n) + Jitter() * 0.5f) * new Basis(Vector3.Right, tilt);
			var xf = new Transform3D(basis, p);
			var fp = new Aabb(p - new Vector3(it.Size.X * 0.5f, 0, it.Size.X * 0.5f), new Vector3(it.Size.X, it.Size.Y, it.Size.X)).Merge(new Aabb(p - n * foot, Vector3.One * 0.02f));
			if (!Fits(fp)) continue;
			it.Build(k, xf);
			_taken.Add(fp);
			var pl = new Placed(it, xf, fp);
			All.Add(pl);
			Shade?.Under(new Aabb(fp.Position, fp.Size with { Y = 0.3f }), 0.2f, 0.25f);
			return pl;
		}
		return null;
	}

	/// <summary>Out from the walls (the other 30%), but never alone in the middle of the floor: set down beside
	/// something already placed (spilled from a stack, pulled out from the wall), a hand's width to a stride off it,
	/// at any angle; somewhere not taken at least <paramref name="fromWall"/> off the walls if nothing's placed yet.</summary>
	public Placed Free(MeshKit k, Item it, float fromWall = 0.6f, int tries = 14)
	{
		if (it == null) return null;
		Vector3 lo = Room.Position, hi = Room.End;
		for (int t = 0; t < tries; t++)
		{
			float m = fromWall + Mathf.Max(it.Size.X, it.Size.Z) * 0.5f;
			Vector3 p;
			if (All.Count > 0)
			{
				var near = All[Rng.RandiRange(0, All.Count - 1)];
				Vector3 c = near.Box.GetCenter();
				float a = Rng.RandfRange(0f, Mathf.Tau), r = Mathf.Max(near.Box.Size.X, near.Box.Size.Z) * 0.5f + Mathf.Max(it.Size.X, it.Size.Z) * 0.5f + Rng.RandfRange(0.08f, 0.5f);
				p = new Vector3(c.X + Mathf.Cos(a) * r, lo.Y, c.Z + Mathf.Sin(a) * r);
			}
			else
			{
				if (hi.X - lo.X < m * 2f || hi.Z - lo.Z < m * 2f) return null;
				p = new(Rng.RandfRange(lo.X + m, hi.X - m), lo.Y, Rng.RandfRange(lo.Z + m, hi.Z - m));
			}
			var xf = new Transform3D(new Basis(Vector3.Up, Rng.RandfRange(0f, Mathf.Tau)), p);
			if (Fits(Footprint(xf, it.Size))) return Put(k, it, xf);
		}
		return null;
	}

	/// <summary>Knocked over: lying on its side (a chair, a pail, a carton spilled), at any angle, beside something
	/// already placed or out from a wall. The PS2 rooms' one sign that something happened here.</summary>
	public Placed Topple(MeshKit k, Item it, int tries = 14)
	{
		if (it == null) return null;
		Vector3 lo = Room.Position, hi = Room.End;
		for (int t = 0; t < tries; t++)
		{
			Vector3 p;
			if (All.Count > 0)
			{
				var near = All[Rng.RandiRange(0, All.Count - 1)];
				Vector3 c = near.Box.GetCenter();
				float a = Rng.RandfRange(0f, Mathf.Tau), r = Mathf.Max(near.Box.Size.X, near.Box.Size.Z) * 0.5f + it.Size.Y * 0.5f + Rng.RandfRange(0.1f, 0.6f);
				p = new Vector3(c.X + Mathf.Cos(a) * r, lo.Y, c.Z + Mathf.Sin(a) * r);
			}
			else p = new Vector3(Rng.RandfRange(lo.X + 1f, hi.X - 1f), lo.Y, Rng.RandfRange(lo.Z + 1f, hi.Z - 1f));
			// on its side: its up turned to lie along the floor, lifted so its side rests on the boards
			var basis = new Basis(Vector3.Up, Rng.RandfRange(0f, Mathf.Tau)) * new Basis(Vector3.Back, Mathf.Pi * 0.5f * (Rng.Randf() < 0.5f ? 1f : -1f));
			var xf = new Transform3D(basis, p + Vector3.Up * (it.Size.X * 0.5f));
			if (Fits(Footprint(xf, it.Size))) return Put(k, it, xf);
		}
		return null;
	}

	/// <summary>The rules in one go: <paramref name="count"/> pieces drawn from <paramref name="items"/>, about
	/// <paramref name="wallShare"/> against the walls and in the corners (leaners leant), the rest out in the room;
	/// a third of what can be stacked on gets something on it. Returns how many went in.</summary>
	public int Scatter(MeshKit k, IList<Item> items, int count, float wallShare = 0.7f, IList<Item> onTop = null)
	{
		int n = 0;
		var pool = new List<Item>();
		foreach (var it in items) if (it != null) pool.Add(it);
		if (pool.Count == 0) return 0;
		for (int i = 0; i < count; i++)
		{
			var it = pool[Rng.RandiRange(0, pool.Count - 1)];
			Placed p;
			if (it.Leans) p = Lean(k, it);
			else if (Rng.Randf() < wallShare) p = Rng.Randf() < 0.3f ? InCorner(k, it) : AgainstWall(k, it);
			else p = Free(k, it);
			if (p == null) continue;
			n++;
			if (it.CanStackOn && onTop is { Count: > 0 } && Rng.Randf() < 0.35f)
			{
				var top = onTop[Rng.RandiRange(0, onTop.Count - 1)];
				if (top != null && On(k, p, top) != null) n++;
			}
		}
		return n;
	}
}
