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
	public float Spacing = 1.7f;
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
	public static void Dress(Node3D root, int seed, Aabb? bounds = null, float amount = 0.95f, float spacing = 1.7f)
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
		const float step = 1.6f;
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

	/// <summary>(2026-10-31, the owner: "way more graffiti ... way more clustered together and cluttered, way way too sparse")
	/// Each spot a cluster, as walls really get done: one big piece (or a throw-up) and a crowd of tags, throw-ups,
	/// stencils and the odd character round it, overlapping, from the knee to as high as an arm reaches, the newer over
	/// the older.</summary>
	private void Lay()
	{
		foreach (var (at, n) in _spots)
		{
			if (_rng.Randf() > Amount) continue;
			var along = Vector3.Up.Cross(n).Normalized();
			float floor = at.Y - 1.45f;
			// the cluster's centre piece
			Put(PickBig(), at, n, along, floor, _rng.RandfRange(-0.4f, 0.4f), 1.35f + _rng.RandfRange(-0.2f, 0.35f), 0f, 0);
			// one cluster in six: a tag at eye height that will have changed when they look back at it (Changers)
			if (_changers.Count < MaxChangers && _rng.Randf() < 0.17f)
			{
				var tag = Put(Arts[_rng.RandiRange(13, 22)], at, n, along, floor, _rng.RandfRange(-1.4f, 1.4f), _rng.RandfRange(1.3f, 1.8f), _rng.RandfRange(-5f, 5f), 9);
				if (tag != null) _changers.Add(new Changer { Decal = tag, To = Fresh[_changers.Count % Fresh.Length] });
			}
			// and round it: the clutter
			int extra = _rng.RandiRange(5, 10);
			for (int i = 0; i < extra; i++)
			{
				var art = PickSmall();
				float x = _rng.RandfRange(-1.9f, 1.9f);
				float y = _rng.RandfRange(0.35f, 2.75f);
				Put(art, at, n, along, floor, x, y, _rng.RandfRange(-8f, 8f), i + 1);
			}
		}
	}

	private Art PickBig()
	{
		// the pieces and throw-ups (indices 0..12)
		return Arts[_rng.RandiRange(0, 12)];
	}

	private Art PickSmall()
	{
		// tags, stencils, characters, throw-ups (indices 9..)
		return Arts[_rng.RandiRange(9, Arts.Length - 1)];
	}

	private Decal Put(Art art, Vector3 at, Vector3 n, Vector3 along, float floor, float x, float y, float tilt, int layer)
	{
		var tex = Tex(art.Name);
		if (tex == null) return null;
		float scale = _rng.RandfRange(0.8f, 1.15f) * (layer == 0 ? 1f : 0.85f);
		float w = art.Width * scale, h = w * art.Aspect;
		y = Mathf.Max(y, h * 0.5f + 0.05f);
		var centre = new Vector3(at.X, floor + y, at.Z) + along * x;
		// a decal projects down its local -Y: its Y out of the wall, its X along it, its Z down the picture
		var basis = new Basis(along, n, Vector3.Down).Rotated(n, Mathf.DegToRad(tilt + _rng.RandfRange(-3f, 3f)));
		var d = new Decal
		{
			Name = $"{art.Name}_{Placed}",
			Size = new Vector3(w, 0.5f, h),
			TextureAlbedo = tex,
			Modulate = new Color(1f, 1f, 1f, _rng.RandfRange(0.62f, 0.95f)),
			AlbedoMix = 1f,
			NormalFade = 0.5f,
			UpperFade = 0.05f,
			LowerFade = 0.05f,
			DistanceFadeEnabled = true,
			DistanceFadeBegin = 30f,
			DistanceFadeLength = 8f,
			CullMask = DecalDresser.DecalMask,
			SortingOffset = layer * 0.01f,   // (the newer over the older)
		};
		AddChild(d);
		d.GlobalTransform = new Transform3D(basis, centre);
		Placed++;
		return d;
	}

	// ------------------------------------------------------------------ the ones that change behind them

	/// <summary>(2026-10-31, the owner: "Graffiti that changes behind you", and of 81' 83' 87' 92': "those are dates") A tag
	/// they've had in view, and then turned their back on, close by: when they look again it's been painted over, fresh,
	/// still running (HE HEARS YOU, TURN AROUND, the 19 crossed out for a 20, the dates with this year's added in red), and
	/// they heard the can behind them while it was done. One at a time, never two within <see cref="ChangeGap"/> s.</summary>
	private sealed class Changer
	{
		public Decal Decal;
		public (string Name, float Aspect) To;
		public double SeenAt = -1;
		public bool Changed;
	}

	private static readonly (string, float)[] Fresh =
	{
		("fresh_hehears", 0.5f), ("fresh_dates", 0.375f), ("fresh_turn", 0.5f), ("fresh_20", 0.5f), ("fresh_notalone", 0.5f), ("fresh_ssh", 0.5f),
	};
	private const int MaxChangers = 8;
	private const double ChangeGap = 25.0;
	private static double _lastChange = -999;
	private readonly List<Changer> _changers = new();
	private double _look;
	public int Changed { get; private set; }

	public override void _Process(double delta)
	{
		if (!Finished || _changers.Count == 0 || (_look -= delta) > 0) return;
		_look = 0.1;
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		Vector3 eye = cam.GlobalPosition, fwd = -cam.GlobalBasis.Z;
		if (!_bounds.Grow(4f).HasPoint(eye)) return;
		double now = Time.GetTicksMsec() / 1000.0;
		foreach (var c in _changers)
		{
			if (c.Changed || !IsInstanceValid(c.Decal)) continue;
			Vector3 to = c.Decal.GlobalPosition - eye;
			float dist = to.Length();
			if (dist > 16f) continue;
			float facing = fwd.Dot(to / Mathf.Max(dist, 0.01f));
			if (facing > 0.82f)
			{
				// in view, and nothing between
				var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, c.Decal.GlobalPosition + c.Decal.GlobalBasis.Y * 0.1f, 1));
				if (hit.Count == 0 || ((Vector3)hit["position"]).DistanceTo(c.Decal.GlobalPosition) < 0.6f) c.SeenAt = now;
				continue;
			}
			if (c.SeenAt < 0 || now - c.SeenAt < 1.2 || facing > -0.25f || dist > 14f || now - _lastChange < ChangeGap) continue;
			var tex = Tex(c.To.Name);
			if (tex == null) { c.Changed = true; continue; }
			c.Decal.TextureAlbedo = tex;
			var sz = c.Decal.Size;
			float w = c.To.Name == "fresh_dates" ? 2.2f : 1.5f;
			c.Decal.Size = new Vector3(w, sz.Y, w * c.To.Aspect);
			c.Decal.Modulate = new Color(1f, 1f, 1f, 1f);
			c.Decal.SortingOffset = 1f;
			c.Changed = true;
			_lastChange = now;
			Changed++;
			Audio.AudioDirector.OneShot(this, "spray_hiss", 3, c.Decal.GlobalPosition, -9f, "Events", 4f, 0.06f);
			GD.Print($"[graffiti] behind them, {c.To.Name} painted over a tag ({dist:0.0} m back)");
			break;
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
