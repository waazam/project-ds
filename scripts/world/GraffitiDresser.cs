using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Graffiti on the underground's walls (2026-10-31, the owner, with photos of graffitied tunnels: "implement a bunch of
/// different graffiti in the underground sections all over the walls and in the red light green light hallway ... make it
/// look older and more faded like the paint is chipping and flaking off"). The paintings are tools/Textures/
/// make_graffiti.py's (assets/textures/graffiti: pieces, throw-ups, tags, stencils, characters, all original, the words the
/// owner gave: HUSH, 19, DREAMS, THINNY, EAST IS UP, 83'...). Laid as decals so they take the wall's own shape and
/// shading, and its light (red in the red, green in the green).
///
/// Like <see cref="DecalDresser"/> it finds its walls by casting, a few columns a physics tick: from points through the
/// area's bounds, down to the floor, then out level at spray height in four directions; a near, upright surface becomes a
/// spot, at least <see cref="Spacing"/> from the last on that wall. Big pieces where there's room, tags and stencils
/// between, the odd character. Deterministic from the seed. <see cref="Dress"/> is called with the stains, as each area is
/// first drawn (RenderBudget) and by the bunker.
/// </summary>
public partial class GraffitiDresser : Node3D
{
	private record struct Art(string Name, float Width, float Aspect, float Weight);

	// (name, width in metres, height / width, how often): the pieces big, the tags small
	private static readonly Art[] Arts =
	{
		new("piece_hush", 2.6f, 0.5f, 1f), new("piece_dreams", 2.8f, 0.5f, 1f), new("piece_speakeasy", 3.0f, 0.5f, 0.8f),
		new("piece_thinny", 2.6f, 0.5f, 0.9f), new("piece_redlight", 2.8f, 0.5f, 0.9f), new("piece_greenlight", 2.8f, 0.5f, 0.9f),
		new("piece_19", 1.8f, 0.667f, 1f), new("piece_freesex", 2.6f, 0.5f, 0.7f), new("piece_sleep", 2.6f, 0.5f, 0.9f),
		new("throw_hush", 1.6f, 0.5f, 1f), new("throw_19", 1.4f, 0.5f, 1f), new("throw_quiet", 1.6f, 0.5f, 1f), new("throw_dreams", 1.7f, 0.5f, 0.9f),
		new("tag_83", 0.8f, 0.5f, 1f), new("tag_87", 0.8f, 0.5f, 1f), new("tag_92", 0.8f, 0.5f, 1f), new("tag_81", 0.8f, 0.5f, 1f),
		new("tag_tiptoe", 1.1f, 0.5f, 1f), new("tag_ssh", 1.2f, 0.5f, 1.2f), new("tag_hush", 1.0f, 0.5f, 1f), new("tag_dreams", 1.1f, 0.5f, 0.9f),
		new("tag_quiet", 1.1f, 0.5f, 1f), new("tag_thinny", 1.1f, 0.5f, 0.8f),
		new("sten_eastisup", 1.6f, 0.375f, 0.8f), new("sten_exclusion", 1.9f, 0.375f, 0.8f), new("sten_quiet", 1.4f, 0.375f, 0.8f), new("sten_speakeasy", 1.6f, 0.375f, 0.6f),
		new("char_eyes", 1.3f, 1f, 0.6f), new("char_shush", 1.3f, 1f, 0.6f), new("char_sleep", 1.2f, 1f, 0.5f),
	};

	/// <summary>Metres between two on the same wall (measured between their middles).</summary>
	public float Spacing = 3.4f;
	/// <summary>The share of the found spots that get one.</summary>
	public float Amount = 0.75f;
	public int Placed { get; private set; }
	public bool Finished { get; private set; }

	private Aabb _bounds;
	private int _seed;
	private RandomNumberGenerator _rng;
	private readonly List<Vector2> _columns = new();
	private int _next;
	private readonly List<(Vector3 at, Vector3 n)> _spots = new();
	private int _wait = 4;
	private static readonly Dictionary<string, Texture2D> _tex = new();

	/// <summary>Sprays <paramref name="root"/>'s walls (inside <paramref name="bounds"/>, or its meshes'), once.</summary>
	public static void Dress(Node3D root, int seed, Aabb? bounds = null, float amount = 0.75f, float spacing = 3.4f)
	{
		if (root == null || !IsInstanceValid(root) || root.HasMeta("graffiti_dressed")) return;
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-graffiti") >= 0) return;
		root.SetMeta("graffiti_dressed", true);
		var g = new GraffitiDresser { Name = "Graffiti", _seed = seed, Amount = amount, Spacing = spacing };
		g._bounds = bounds ?? DecalDresserBounds(root);
		root.AddChild(g);
	}

	private static Aabb DecalDresserBounds(Node3D root)
	{
		Aabb box = new(root.GlobalPosition, Vector3.Zero);
		bool any = false;
		foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mi = (MeshInstance3D)n;
			if (mi.Mesh == null) continue;
			var b = mi.GlobalTransform * mi.GetAabb();
			box = any ? box.Merge(b) : b;
			any = true;
		}
		return box;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Finished) { SetPhysicsProcess(false); return; }
		if (_wait-- > 0) return;   // (the area's colliders in the space first)
		if (_rng == null) Plan();
		var space = GetWorld3D().DirectSpaceState;
		for (int n = 0; n < 30 && _next < _columns.Count; n++, _next++) Column(space, _columns[_next]);
		if (_next < _columns.Count) return;
		Finished = true;
		Lay();
		GD.Print($"[graffiti] {GetParent().Name}: {Placed} pieces on {_spots.Count} spots found");
	}

	private void Plan()
	{
		_rng = new RandomNumberGenerator { Seed = (ulong)(_seed * 7349 + 5) };
		const float step = 2.4f;
		var b = _bounds;
		for (float x = b.Position.X + step * 0.5f; x < b.End.X; x += step)
			for (float z = b.Position.Z + step * 0.5f; z < b.End.Z; z += step)
				_columns.Add(new Vector2(x + _rng.RandfRange(-0.8f, 0.8f), z + _rng.RandfRange(-0.8f, 0.8f)));
	}

	private static readonly Vector3[] Dirs = { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back };

	/// <summary>One column: every floor under it, top down (a roof's top is a floor to a ray, the passage under it the one
	/// that matters), and from each, out level at spray height every way to a wall.</summary>
	private void Column(PhysicsDirectSpaceState3D space, Vector2 c)
	{
		var top = new Vector3(c.X, _bounds.End.Y - 0.05f, c.Y);
		float bottom = _bounds.Position.Y - 0.5f;
		for (int level = 0; level < 4 && top.Y > bottom; level++)
		{
			var down = space.IntersectRay(PhysicsRayQueryParameters3D.Create(top, top with { Y = bottom }, 1));
			if (down.Count == 0) return;
			var floor = (Vector3)down["position"];
			top = floor + Vector3.Down * 0.6f;   // (the next ray starts under this surface)
			if (((Vector3)down["normal"]).Y < 0.7f) continue;
			var up = space.IntersectRay(PhysicsRayQueryParameters3D.Create(floor + Vector3.Up * 0.3f, floor + Vector3.Up * 30f, 1));
			float ceil = up.Count > 0 ? ((Vector3)up["position"]).Y - floor.Y : 30f;
			if (ceil < 2.1f) continue;
			float sprayY = Mathf.Min(1.45f, ceil * 0.5f);
			var from = floor + Vector3.Up * sprayY;
			foreach (var d in Dirs)
			{
				var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + d * 5f, 1));
				if (hit.Count == 0) continue;
				var n = (Vector3)hit["normal"];
				if (Mathf.Abs(n.Y) > 0.25f) continue;
				var p = (Vector3)hit["position"];
				if (p.DistanceTo(from) < 0.3f) continue;
				n = (n with { Y = 0 }).Normalized();
				bool near = false;
				foreach (var (at, nn) in _spots) if (nn.Dot(n) > 0.7f && at.DistanceTo(p) < Spacing) { near = true; break; }
				if (!near) _spots.Add((p with { Y = floor.Y + 1.45f }, n));
			}
		}
	}

	private float _total;

	private Art PickArt(float room)
	{
		if (_total == 0) foreach (var a in Arts) _total += a.Weight;
		for (int tries = 0; tries < 6; tries++)
		{
			float r = _rng.Randf() * _total;
			foreach (var a in Arts)
			{
				r -= a.Weight;
				if (r > 0) continue;
				if (a.Width * a.Aspect <= room) return a;
				break;
			}
		}
		return Arts[13];   // (a small tag fits anywhere)
	}

	private void Lay()
	{
		foreach (var (at, n) in _spots)
		{
			if (_rng.Randf() > Amount) continue;
			var art = PickArt(2.4f);
			var tex = Tex(art.Name);
			if (tex == null) continue;
			float scale = _rng.RandfRange(0.85f, 1.15f);
			float w = art.Width * scale, h = w * art.Aspect;
			// (tags anywhere from knee to reach; the big ones centred at eye height)
			float y = art.Width < 1.3f ? _rng.RandfRange(0.7f, 2.0f) : 1.35f + _rng.RandfRange(-0.15f, 0.25f);
			var along = Vector3.Up.Cross(n).Normalized();
			var centre = new Vector3(at.X, at.Y - 1.45f + y, at.Z) + along * _rng.RandfRange(-0.6f, 0.6f);
			// a decal projects down its local -Y: its Y out of the wall, its X along it, its Z down the picture
			var basis = new Basis(along, n, Vector3.Down).Rotated(n, Mathf.DegToRad(_rng.RandfRange(-3f, 3f)));
			var d = new Decal
			{
				Name = $"{art.Name}_{Placed}",
				Size = new Vector3(w, 0.5f, h),
				TextureAlbedo = tex,
				Modulate = new Color(1f, 1f, 1f, _rng.RandfRange(0.72f, 0.95f)),
				AlbedoMix = 1f,
				NormalFade = 0.5f,
				UpperFade = 0.05f,
				LowerFade = 0.05f,
				DistanceFadeEnabled = true,
				DistanceFadeBegin = 30f,
				DistanceFadeLength = 10f,
				CullMask = DecalDresser.DecalMask,
			};
			AddChild(d);
			d.GlobalTransform = new Transform3D(basis, centre);
			Placed++;
		}
	}

	private static Texture2D Tex(string name)
	{
		if (_tex.TryGetValue(name, out var t)) return t;
		string path = $"res://assets/textures/graffiti/{name}.png";
		t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		_tex[name] = t;
		return t;
	}
}
