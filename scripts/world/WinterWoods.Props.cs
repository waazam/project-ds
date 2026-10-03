using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// What there is to photograph on the road to the lodge (the owner: "weird shit" along the way), each one a
/// picture for the camera: a snowman built facing back at the church, the snowplow that made the road (pulled
/// over, its door open, nobody in it), a dead tree hung with antlers, ski tracks that leave the road and stop dead
/// at a tree, somebody's arm reaching out of the plowed wall, a deer frozen standing, the lodge's sign.
/// The models are Blender's (2026-09-30, the owner: far too low in detail; tools/Blender/winter_props.py builds them
/// into assets/models/winter); placing them, their colliders and their photographs are here.
/// </summary>
public partial class WinterWoods
{
	/// <summary>Where each thing stands along the road (arc length m, side, metres out). Down on the road between the
	/// plowed walls, against one of them; the antler tree and the ski tracks out in the snow beyond, where the wall on
	/// that side sinks low (<see cref="WallHeight"/>).</summary>
	public static readonly (string id, float s, float side, float d)[] PropSpots =
	{
		("snowman", 150f, 1f, 2.5f), ("plow", 340f, -1f, 2.2f), ("antler_tree", 520f, 1f, 9.5f), ("ski_tracks", 700f, -1f, 6.5f),
		("buried_arm", 880f, 1f, 4.2f), ("frozen_deer", 1090f, -1f, 2.6f), ("lodge_sign", 1370f, 1f, 2.9f),
	};
	public readonly Dictionary<string, Node3D> Props = new();

	private static bool NearProp(Vector2 p, float r)
	{
		foreach (var (_, s, side, d) in PropSpots)
			if (PropXZ(s, side, d).DistanceTo(p) < r + (d > 8f ? 3f : 0f)) return true;
		return false;
	}

	private static Vector2 PropXZ(float s, float side, float d)
	{
		RoadAt(s, out var dir);
		var q = RoadAt(s, out _);
		var right = new Vector2(-dir.Y, dir.X);
		return new Vector2(q.X, q.Z) + right * side * d;
	}

	/// <summary>A prop's node: on the ground at its spot, facing the road (its -Z toward the road).</summary>
	private Node3D Spot(string id, float s, float side, float d)
	{
		var p = PropXZ(s, side, d);
		RoadAt(s, out var dir);
		var right = new Vector3(-dir.Y, 0, dir.X);
		var toRoad = -right * side;
		var n = new Node3D { Name = "Prop_" + id, Position = new Vector3(p.X, Height(p.X, p.Y), p.Y) };
		AddChild(n);
		n.Basis = Basis.LookingAt(toRoad, Vector3.Up);
		Props[id] = n;
		return n;
	}

	private void BuildProps()
	{
		foreach (var (id, s, side, d) in PropSpots)
		{
			var n = Spot(id, s, side, d);
			switch (id)
			{
				case "snowman": Snowman(n); PhotoSubject.Attach(n, "snowman", new Vector3(0, 1.3f, 0), 1.5f, 22f, 16f); break;
				case "plow": Plow(n); PhotoSubject.Attach(n, "plow", new Vector3(0, 1.4f, 0), 2.5f, 35f, 18f); break;
				case "antler_tree": AntlerTree(n); PhotoSubject.Attach(n, "antler_tree", new Vector3(0, 3.2f, 0), 2.5f, 30f, 18f); break;
				case "ski_tracks": SkiTracks(n); break;
				case "buried_arm":
					{
						// set back to where the wall's face is at its height (the model's origin is the face, at road level)
						var q = RoadAt(s, out var dir);
						var right = new Vector2(-dir.Y, dir.X);
						var xz = new Vector2(q.X, q.Z) + right * side * (WallAt(1.25f, s, -side) - 0.05f);
						n.Position = new Vector3(xz.X, q.Y, xz.Y);
						BuriedArm(n);
					}
					PhotoSubject.Attach(n, "buried_arm", new Vector3(0, 1.45f, -0.35f), 0.6f, 16f, 16f); break;
				case "frozen_deer": FrozenDeer(n); PhotoSubject.Attach(n, "frozen_deer", new Vector3(0.3f, 1.1f, 0), 1.5f, 22f, 16f); break;
				case "lodge_sign": LodgeSign(n); PhotoSubject.Attach(n, "lodge_sign", new Vector3(0, 1.6f, 0), 1.2f, 18f, 16f); break;
			}
		}
		PhotoSubject.Attach(Lodge, "the_lodge", new Vector3(0, 9f, 0), 12f, 140f, 22f, false);
	}

	/// <summary>One of the Blender models (assets/models/winter/<paramref name="name"/>.glb), set in a prop's node.</summary>
	private static Node3D Model(Node3D parent, string name)
	{
		var n = GD.Load<PackedScene>($"res://assets/models/winter/{name}.glb").Instantiate<Node3D>();
		n.Name = "Model";
		parent.AddChild(n);
		return n;
	}

	private static IEnumerable<MeshInstance3D> Meshes(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is MeshInstance3D mi) yield return mi;
			foreach (var m in Meshes(c)) yield return m;
		}
	}

	/// <summary>Three hand-rolled balls of snow on a skirt of trodden snow, stick arms up and out, one hand a twig claw;
	/// somebody's red scarf frozen stiff round its neck; its head turned back up the road, seven coal eyes down one side
	/// of its face and a grin of real teeth.</summary>
	private void Snowman(Node3D n)
	{
		Model(n, "snowman");
		Solidify(n, new Vector3(0, 0.9f, 0), new Vector3(1.1f, 1.8f, 1.1f), "snow");
	}

	/// <summary>The snowplow that cut the road: a municipal plow truck pulled over against the wall it threw up, its
	/// blade angled into the snow, the cab door hanging open on an empty seat, the key still in it, the headlights dead.
	/// Its length along the road (the node faces the road: its local X runs along it).</summary>
	private void Plow(Node3D n)
	{
		Model(n, "snowplow");
		Solidify(n, new Vector3(-0.7f, 1.3f, 0), new Vector3(8.6f, 2.6f, 2.4f), "metal");
	}

	/// <summary>A dead tree out in the snow by the road, hung all over with antlers and jawbones on twine; a deer's
	/// skull nailed to its trunk, facing the road.</summary>
	private void AntlerTree(Node3D n)
	{
		Model(n, "antler_tree");
		Solidify(n, new Vector3(0, 2f, 0), new Vector3(0.9f, 4f, 0.9f), "wood");
	}

	/// <summary>An antler: a curved beam with tines, bone-white (also the wendigo's).</summary>
	public static void Antler(MeshKit k, Vector3 root, Basis b, float len, RandomNumberGenerator rng, float thick = 0.03f)
	{
		Vector3 prev = root;
		int segs = 5;
		for (int i = 1; i <= segs; i++)
		{
			float t = i / (float)segs;
			Vector3 p = root + b * new Vector3(Mathf.Sin(t * 1.3f) * len * 0.35f, t * len, -Mathf.Sin(t * 2f) * len * 0.12f);
			k.Cylinder(prev, p, thick * (1.1f - t * 0.6f), thick * (1f - t * 0.6f), 5, false);
			if (i >= 2 && i <= 4)
			{
				Vector3 tine = p + b * new Vector3(rng.RandfRange(-0.1f, 0.25f) * len, 0.22f * len, rng.RandfRange(0.1f, 0.3f) * len);
				k.Cylinder(p, tine, thick * 0.6f, thick * 0.15f, 4, false);
			}
			prev = p;
		}
	}

	/// <summary>Two ski tracks leave the road and run straight into the trees, and stop dead at a trunk; the poles
	/// stand planted there in an X. No skier.</summary>
	private void SkiTracks(Node3D n)
	{
		// the node faces the road (-Z): the tracks run away from it, along local +Z
		var k = new MeshKit();
		k.Mat(TrackMat);
		k.Color = Colors.White;
		const float len = 24f;
		float Gy(Vector3 local) { var w = n.Position + n.Basis * local; return Height(w.X, w.Z) - n.Position.Y; }
		for (float t = -2.5f; t < len; t += 0.5f)
			foreach (float x in new[] { -0.12f, 0.12f })
			{
				float ya = Gy(new Vector3(x, 0, t)) + 0.03f, yb = Gy(new Vector3(x, 0, t + 0.5f)) + 0.03f;
				k.Quad(new Vector3(x - 0.045f, ya, t), new Vector3(x + 0.045f, ya, t), new Vector3(x + 0.045f, yb, t + 0.5f), new Vector3(x - 0.045f, yb, t + 0.5f), Vector3.Up);
			}
		k.CommitTo(n, "Tracks", false);
		var endLocal = new Vector3(0, 0, len + 0.8f);
		float gy = Gy(endLocal);
		var fir = WinterTreeKit.Single(WinterTreeKit.WinterFir(611, 20f, 3.4f), "Tree");
		fir.Position = endLocal with { Y = gy - 0.1f };
		n.AddChild(fir);
		// the poles planted crossed, a ski stuck upright beside them (the Blender model)
		var gear = Model(n, "ski_gear");
		gear.Position = new Vector3(0, gy, len - 0.3f);
		PhotoSubject.Attach(n, "ski_tracks", new Vector3(0, gy + 0.6f, len - 0.3f), 2f, 30f, 16f, false);
		Solidify(n, new Vector3(0, gy + 0.8f, len - 0.3f), new Vector3(0.4f, 1.6f, 0.35f), "wood");   // (the gear: not walked through)
		Solidify(n, endLocal + new Vector3(0, gy + 2f, 0), new Vector3(0.8f, 4f, 0.8f), "wood");
	}

	/// <summary>Somebody inside the plowed wall: a parka's puffy sleeve breaking out of the snow at chest height, bent at
	/// the elbow, reaching up and out toward the road; the bare hand open and clawed, frostbitten, the nails gone dark,
	/// frost along the top of it all; the wall round it broken. (The owner: the man laid out on the road, in the wall
	/// with just his arm out.) Set at the wall's foot, at road level.</summary>
	private void BuriedArm(Node3D n)
	{
		Model(n, "buried_arm");
		Solidify(n, new Vector3(0, 1.45f, -0.25f), new Vector3(0.3f, 0.5f, 0.5f), "snow");
	}

	/// <summary>A young buck frozen where it stood at the road's edge, mid-step, one foreleg lifted, glazed in ice, frost
	/// along its back, icicles off its belly and chin, its eyes gone white. It doesn't bolt. Side-on to the road.</summary>
	private void FrozenDeer(Node3D n)
	{
		Model(n, "frozen_deer");   // (its frost and icicles are the model's: the glassy overlay made it plastic)
		Solidify(n, new Vector3(0.25f, 1f, 0), new Vector3(1.6f, 2f, 0.5f), "wood");
	}

	/// <summary>The lodge's road sign: three routed planks between two log posts on iron brackets, under a little
	/// shingled gable with the snow on it, icicles off its edges; its lettering the game's own.</summary>
	private void LodgeSign(Node3D n)
	{
		Model(n, "lodge_sign");
		SignKit.Text(n, "HOLLOW PEAK LODGE", new Vector3(0, 1.62f, -0.125f), new Basis(Vector3.Up, Mathf.Pi), 0.16f, new Color(0.85f, 0.8f, 0.7f));
		SignKit.Text(n, "1/4 MI  ->   GUESTS CHECK IN AT FRONT", new Vector3(0, 1.35f, -0.125f), new Basis(Vector3.Up, Mathf.Pi), 0.075f, new Color(0.8f, 0.75f, 0.66f));
		Solidify(n, new Vector3(0, 1.1f, 0), new Vector3(2.7f, 2.2f, 0.35f), "wood");
	}

	private void Solidify(Node3D n, Vector3 c, Vector3 size, string surface)
	{
		var b = new StaticBody3D { Name = "Solid", CollisionLayer = 1, CollisionMask = 0 };
		b.SetMeta("surface", surface);
		b.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = size } });
		n.AddChild(b);
	}

	// ------------------------------------------------------------------ their materials

	private static StandardMaterial3D _bone, _track;
	public static StandardMaterial3D Bone => _bone ??= new StandardMaterial3D { ResourceName = "bone", AlbedoColor = new Color(0.66f, 0.62f, 0.54f), Roughness = 0.7f, AlbedoTexture = ProcTextures.Grime() };
	private static StandardMaterial3D TrackMat => _track ??= new StandardMaterial3D { ResourceName = "ski_track", AlbedoColor = new Color(0.4f, 0.45f, 0.55f), Roughness = 1f };
}
