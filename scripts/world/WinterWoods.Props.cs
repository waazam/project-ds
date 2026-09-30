using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// What there is to photograph on the road to the lodge (the owner: "weird shit" along the way), each one a
/// picture for the camera: a snowman built facing back at the church, the snowplow that made the road (pulled
/// over, its door open, nobody in it), a dead tree hung with antlers, ski tracks that leave the road and stop dead
/// at a tree, a whole set of winter clothes laid out empty on the snow, a deer frozen standing, the lodge's sign.
/// </summary>
public partial class WinterWoods
{
	/// <summary>Where each thing stands along the road (arc length m, side, metres out).</summary>
	public static readonly (string id, float s, float side, float d)[] PropSpots =
	{
		("snowman", 150f, 1f, 7.2f), ("plow", 340f, -1f, 6.2f), ("antler_tree", 520f, 1f, 9.5f), ("ski_tracks", 700f, -1f, 6.5f),
		("clothes", 880f, 1f, 6.9f), ("frozen_deer", 1090f, -1f, 6.4f), ("lodge_sign", 1370f, 1f, 5.8f),
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
				case "clothes": Clothes(n); PhotoSubject.Attach(n, "empty_clothes", new Vector3(0, 0.1f, 0), 1f, 14f, 16f); break;
				case "frozen_deer": FrozenDeer(n); PhotoSubject.Attach(n, "frozen_deer", new Vector3(0, 1.1f, 0), 1.5f, 22f, 16f); break;
				case "lodge_sign": LodgeSign(n); PhotoSubject.Attach(n, "lodge_sign", new Vector3(0, 1.6f, 0), 1.2f, 18f, 16f); break;
			}
		}
		PhotoSubject.Attach(Lodge, "the_lodge", new Vector3(0, 9f, 0), 12f, 140f, 22f, false);
	}

	/// <summary>Three balls of snow, stick arms up; seven coal eyes down one side of its face and a grin of
	/// real teeth; built facing back up the road at the church, the way the player came.</summary>
	private void Snowman(Node3D n)
	{
		var k = new MeshKit();
		k.Mat(PropSnow);
		k.Color = Colors.White;
		k.Blob(new Vector3(0, 0.45f, 0), new Vector3(0.62f, 0.5f, 0.62f), 501, 0.08f, false, 1f);
		k.Blob(new Vector3(0, 1.2f, 0), new Vector3(0.44f, 0.4f, 0.44f), 502, 0.08f, false, 1f);
		k.Blob(new Vector3(0, 1.78f, 0), new Vector3(0.3f, 0.3f, 0.3f), 503, 0.06f, false, 1f);
		// its head is turned: it looks up the road, back the way the player came
		var face = new Basis(Vector3.Up, Mathf.Pi * 0.85f);
		Vector3 F(float x, float y, float z) => new Vector3(0, 1.78f, 0) + face * new Vector3(x, y, z);
		k.Mat(Coal);
		k.Color = Colors.White;
		for (int i = 0; i < 7; i++)
			k.Blob(F(-0.1f + (i % 2) * 0.05f, 0.14f - i * 0.045f, -0.285f + i * 0.004f), Vector3.One * 0.022f, 510 + i, 0.2f, false, 1f);
		k.Blob(F(0.11f, 0.1f, -0.28f), Vector3.One * 0.025f, 520, 0.2f, false, 1f);
		k.Mat(Tooth);
		for (int i = 0; i < 12; i++)
		{
			float u = (i - 5.5f) / 5.5f;
			k.Box(F(u * 0.16f, -0.1f + u * u * 0.04f, -0.29f + u * u * 0.03f), new Vector3(0.018f, 0.03f + (i % 3) * 0.006f, 0.012f), 1f);
		}
		// stick arms, up and out, and a twig hand with too many fingers
		k.Mat(ProcTextures.TreeBarkMat);
		k.Color = new Color(0.3f, 0.26f, 0.24f);
		k.Cylinder(new Vector3(-0.38f, 1.25f, 0), new Vector3(-0.95f, 1.9f, 0.1f), 0.025f, 0.012f, 4, false);
		k.Cylinder(new Vector3(0.38f, 1.25f, 0), new Vector3(0.9f, 1.95f, -0.1f), 0.025f, 0.012f, 4, false);
		for (int i = 0; i < 6; i++)
		{
			float a = -0.6f + i * 0.24f;
			k.Cylinder(new Vector3(0.9f, 1.95f, -0.1f), new Vector3(0.9f + Mathf.Sin(a) * 0.18f, 1.95f + Mathf.Cos(a) * 0.18f, -0.1f), 0.008f, 0.003f, 3, false);
		}
		k.CommitTo(n, "Snowman", true);
		Solidify(n, new Vector3(0, 0.9f, 0), new Vector3(1.1f, 1.8f, 1.1f), "snow");
	}

	/// <summary>The snowplow that cut the road: an old municipal truck pulled into the windrow, its blade angled,
	/// the cab door hanging open, the headlights dead, the seat empty, the key still in it.</summary>
	private void Plow(Node3D n)
	{
		var k = new MeshKit();
		var body = PlowPaint;
		k.Mat(body);
		k.Color = Colors.White;
		// the truck lies along the road: its length on local X
		k.Box(new Vector3(0.4f, 1.05f, 0), new Vector3(2.4f, 1.0f, 2.2f), 0.5f);           // the hood
		k.Box(new Vector3(-1.2f, 1.7f, 0), new Vector3(1.8f, 1.9f, 2.3f), 0.5f);           // the cab
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.4f, 0.38f, 0.36f);
		k.Box(new Vector3(-3.6f, 1.35f, 0), new Vector3(3.0f, 1.3f, 2.4f), 0.5f);          // the sander bed
		k.Box(new Vector3(-1.2f, 0.45f, 0), new Vector3(6.4f, 0.35f, 1.4f), 0.5f);          // the chassis
		// the windows: black
		k.Mat(SkiLodge.WindowGlass);
		k.Quad(new Vector3(-0.28f, 2.35f, -1.0f), new Vector3(-0.28f, 2.35f, 1.0f), new Vector3(-0.28f, 1.8f, 1.0f), new Vector3(-0.28f, 1.8f, -1.0f), Vector3.Right);
		k.Quad(new Vector3(-2.0f, 2.35f, 1.16f), new Vector3(-0.4f, 2.35f, 1.16f), new Vector3(-0.4f, 1.85f, 1.16f), new Vector3(-2.0f, 1.85f, 1.16f), Vector3.Back);
		// the blade: a curved plate out front, angled
		k.Mat(PlowPaint);
		var bladeB = new Basis(Vector3.Up, 0.35f);
		for (int i = 0; i < 6; i++)
		{
			float a0 = i / 6f * 1.2f, a1 = (i + 1) / 6f * 1.2f;
			Vector3 P(float a, float z) => new Vector3(2.4f, 0.3f, 0) + bladeB * new Vector3(0.25f - Mathf.Cos(a) * 0.4f, Mathf.Sin(a) * 0.6f + 0.15f - 0.15f, z);
			k.Quad(P(a0, -1.7f), P(a0, 1.7f), P(a1, 1.7f), P(a1, -1.7f), bladeB * Vector3.Right);
		}
		// wheels
		k.Mat(Rubber);
		foreach (float x in new[] { 1.0f, -3.0f, -4.4f })
			foreach (float z in new[] { -1.15f, 1.15f })
				k.Cylinder(new Vector3(x, 0.5f, z - 0.15f * Mathf.Sign(z)), new Vector3(x, 0.5f, z + 0.15f * Mathf.Sign(z)), 0.5f, 0.5f, 10, true);
		// snow on the roof, the hood, the bed
		k.Mat(PropSnow);
		k.Box(new Vector3(-1.2f, 2.7f, 0), new Vector3(1.9f, 0.14f, 2.3f), 1f);
		k.Box(new Vector3(0.4f, 1.6f, 0), new Vector3(2.4f, 0.1f, 2.2f), 1f);
		k.Blob(new Vector3(-3.6f, 2.0f, 0), new Vector3(1.5f, 0.3f, 1.2f), 530, 0.2f, true, 1f);
		// the dead amber beacon on the cab
		k.Mat(SkiLodge.LitWindow);
		k.Cylinder(new Vector3(-1.2f, 2.8f, 0), new Vector3(-1.2f, 3.0f, 0), 0.1f, 0.08f, 8, true);
		k.CommitTo(n, "Truck", true);
		// the cab door, hanging open toward the road
		var door = new MeshKit();
		door.Mat(PlowPaint);
		door.Box(new Vector3(0.8f, 0, 0), new Vector3(1.6f, 1.5f, 0.08f), 0.5f);
		door.Mat(SkiLodge.WindowGlass);
		door.Quad(new Vector3(0.1f, 0.3f, -0.05f), new Vector3(1.5f, 0.3f, -0.05f), new Vector3(1.5f, 0.7f, -0.05f), new Vector3(0.1f, 0.7f, -0.05f), Vector3.Forward);
		var hinge = new Node3D { Name = "Door", Position = new Vector3(-0.3f, 1.65f, -1.18f), Rotation = new Vector3(0, 1.2f, 0) };
		n.AddChild(hinge);
		door.CommitTo(hinge, "Leaf", true);
		// its length along the road (the node faces the road)
		Solidify(n, new Vector3(-1.2f, 1.3f, 0), new Vector3(7.8f, 2.6f, 2.6f), "metal");
		n.RotateObjectLocal(Vector3.Up, Mathf.Pi * 0.5f);
	}

	/// <summary>A dead tree by the road, hung all over with antlers and jawbones on twine, turning slowly.</summary>
	private void AntlerTree(Node3D n)
	{
		var k = new MeshKit();
		var tree = WinterGlade.BareTreeMesh(481, 12f);
		n.AddChild(WinterTreeKit.Single(tree, "Tree"));
		var rng = new RandomNumberGenerator { Seed = 4811 };
		k.Mat(Bone);
		k.Color = Colors.White;
		for (int i = 0; i < 22; i++)
		{
			float a = rng.RandfRange(0, Mathf.Tau), r = rng.RandfRange(0.9f, 2.8f), y = rng.RandfRange(3.2f, 7f);
			Vector3 top = new(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
			float hang = rng.RandfRange(0.4f, 1.3f);
			k.Mat(Twine);
			k.Cylinder(top, top + Vector3.Down * hang, 0.006f, 0.006f, 3, false);
			k.Mat(Bone);
			Vector3 at = top + Vector3.Down * hang;
			if (i % 3 == 2) k.Box(at + Vector3.Down * 0.12f, new Vector3(0.05f, 0.24f, 0.18f), 1f, new Basis(Vector3.Up, a));   // a jawbone
			else Antler(k, at, new Basis(Vector3.Up, a) * new Basis(Vector3.Right, Mathf.Pi), 0.45f + rng.Randf() * 0.25f, rng);
		}
		k.CommitTo(n, "Antlers", false);
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
		n.AddChild(new MeshInstance3D { Name = "Tree", Mesh = ForestScatter.FirMesh(611, 20f, 0.38f, 0.3f, 10, 3.2f, 0.16f), Position = endLocal with { Y = gy - 0.1f } });
		var pk = new MeshKit();
		pk.Mat(ProcTextures.MetalMat);
		pk.Color = new Color(0.5f, 0.5f, 0.55f);
		pk.Cylinder(new Vector3(-0.5f, gy - 0.2f, len - 0.2f), new Vector3(0.45f, gy + 1.2f, len - 0.3f), 0.012f, 0.012f, 4, false);
		pk.Cylinder(new Vector3(0.5f, gy - 0.2f, len - 0.2f), new Vector3(-0.45f, gy + 1.2f, len - 0.3f), 0.012f, 0.012f, 4, false);
		pk.CommitTo(n, "Poles", false);
		PhotoSubject.Attach(n, "ski_tracks", new Vector3(0, gy + 0.6f, len - 0.3f), 2f, 30f, 16f, false);
		Solidify(n, endLocal + new Vector3(0, gy + 2f, 0), new Vector3(0.8f, 4f, 0.8f), "wood");
	}

	/// <summary>A whole set of winter clothes laid out on the snow in the shape of a person, arms out: parka,
	/// trousers, boots, mittens, a knitted hat. Flat. Empty. The snow round it undisturbed.</summary>
	private void Clothes(Node3D n)
	{
		var k = new MeshKit();
		float y = 0.04f;
		k.Mat(Parka);
		k.Color = Colors.White;
		k.Box(new Vector3(0, y, 0), new Vector3(0.62f, 0.07f, 0.78f), 1f);                                         // the parka's body
		k.Box(new Vector3(-0.62f, y, -0.25f), new Vector3(0.62f, 0.06f, 0.2f), 1f, new Basis(Vector3.Up, 0.25f));    // arms out
		k.Box(new Vector3(0.62f, y, -0.25f), new Vector3(0.62f, 0.06f, 0.2f), 1f, new Basis(Vector3.Up, -0.25f));
		k.Blob(new Vector3(0, y + 0.02f, -0.46f), new Vector3(0.24f, 0.05f, 0.1f), 740, 0.2f, true, 1f);             // the hood, flat
		k.Mat(Denim);
		k.Box(new Vector3(-0.13f, y - 0.01f, 0.78f), new Vector3(0.22f, 0.05f, 0.8f), 1f);
		k.Box(new Vector3(0.13f, y - 0.01f, 0.78f), new Vector3(0.22f, 0.05f, 0.8f), 1f);
		k.Mat(Rubber);
		k.Box(new Vector3(-0.14f, 0.08f, 1.26f), new Vector3(0.14f, 0.16f, 0.3f), 1f);
		k.Box(new Vector3(0.14f, 0.08f, 1.26f), new Vector3(0.14f, 0.16f, 0.3f), 1f);
		k.Mat(Parka);
		k.Blob(new Vector3(-1.02f, y, -0.36f), new Vector3(0.09f, 0.04f, 0.12f), 741, 0.2f, true, 1f);
		k.Blob(new Vector3(1.02f, y, -0.36f), new Vector3(0.09f, 0.04f, 0.12f), 742, 0.2f, true, 1f);
		k.Mat(Denim);
		k.Blob(new Vector3(0, y + 0.03f, -0.74f), new Vector3(0.12f, 0.06f, 0.12f), 743, 0.2f, false, 1f);           // the hat, where a head would be
		k.CommitTo(n, "Clothes", false);
		// laid lengthwise along the road's edge
		n.RotateObjectLocal(Vector3.Up, Mathf.Pi * 0.5f);
	}

	/// <summary>A deer frozen where it stood at the road's edge, glazed in ice, its eyes white, one foreleg lifted
	/// mid-step. It doesn't bolt.</summary>
	private void FrozenDeer(Node3D n)
	{
		var k = new MeshKit();
		k.Mat(PropTextures.FurMat);
		k.Color = new Color(0.5f, 0.42f, 0.36f);
		k.Blob(new Vector3(0, 1.0f, 0), new Vector3(0.32f, 0.36f, 0.7f), 901, 0.1f, false, 1f);
		k.Cylinder(new Vector3(0, 1.2f, -0.55f), new Vector3(0, 1.7f, -0.8f), 0.14f, 0.1f, 6, false);
		k.Blob(new Vector3(0, 1.78f, -0.95f), new Vector3(0.12f, 0.13f, 0.26f), 902, 0.1f, false, 1f);
		foreach (float x in new[] { -0.15f, 0.15f })
		{
			k.Cylinder(new Vector3(x, 0.9f, -0.45f), new Vector3(x, 0.02f, -0.45f), 0.06f, 0.035f, 5, true);
			k.Cylinder(new Vector3(x, 0.9f, 0.5f), new Vector3(x, 0.02f, 0.5f), 0.07f, 0.035f, 5, true);
		}
		// one foreleg lifted
		k.Cylinder(new Vector3(0.15f, 0.9f, -0.45f), new Vector3(0.15f, 0.45f, -0.6f), 0.06f, 0.04f, 5, true);
		k.Mat(Tooth);
		k.Blob(new Vector3(-0.1f, 1.85f, -1.0f), Vector3.One * 0.025f, 903, 0.1f, false, 1f);
		k.Blob(new Vector3(0.1f, 1.85f, -1.0f), Vector3.One * 0.025f, 904, 0.1f, false, 1f);
		// ears
		k.Mat(PropTextures.FurMat);
		k.Blob(new Vector3(-0.1f, 1.95f, -0.85f), new Vector3(0.04f, 0.09f, 0.03f), 905, 0.1f, false, 1f);
		k.Blob(new Vector3(0.1f, 1.95f, -0.85f), new Vector3(0.04f, 0.09f, 0.03f), 906, 0.1f, false, 1f);
		var mi = k.CommitTo(n, "Deer", true);
		mi.MaterialOverlay = IceOverlay;
		// icicles hanging off its belly and chin
		var ik = new MeshKit();
		ik.Mat(IceMat);
		var rng = new RandomNumberGenerator { Seed = 907 };
		for (int i = 0; i < 12; i++)
			{
			var top = new Vector3(rng.RandfRange(-0.15f, 0.15f), 0.68f, rng.RandfRange(-0.5f, 0.5f));
			ik.Cylinder(top, top + Vector3.Down * rng.RandfRange(0.08f, 0.3f), 0.02f, 0.002f, 4, false);
		}
		ik.CommitTo(n, "Icicles", false);
		// side-on to the road
		n.RotateObjectLocal(Vector3.Up, Mathf.Pi * 0.5f);
		Solidify(n, new Vector3(0, 1f, 0), new Vector3(0.7f, 2f, 1.6f), "wood");
	}

	/// <summary>The lodge's road sign: a routed plank on two posts, snow on its top.</summary>
	private void LodgeSign(Node3D n)
	{
		var k = new MeshKit();
		k.Mat(PropTextures.PostMat);
		k.Color = Colors.White;
		k.Box(new Vector3(-0.9f, 0.9f, 0), new Vector3(0.14f, 1.8f, 0.14f), 1f);
		k.Box(new Vector3(0.9f, 0.9f, 0), new Vector3(0.14f, 1.8f, 0.14f), 1f);
		k.Mat(PropTextures.SignPlankMat);
		k.Box(new Vector3(0, 1.5f, -0.08f), new Vector3(2.3f, 0.75f, 0.06f), 1f);
		k.Mat(PropSnow);
		k.Box(new Vector3(0, 1.9f, -0.08f), new Vector3(2.35f, 0.07f, 0.12f), 1f);
		k.CommitTo(n, "Sign", true);
		SignKit.Text(n, "HOLLOW PEAK LODGE", new Vector3(0, 1.62f, -0.115f), new Basis(Vector3.Up, Mathf.Pi), 0.16f, new Color(0.85f, 0.8f, 0.7f));
		SignKit.Text(n, "1/4 MI  ->   GUESTS CHECK IN AT FRONT", new Vector3(0, 1.35f, -0.115f), new Basis(Vector3.Up, Mathf.Pi), 0.075f, new Color(0.8f, 0.75f, 0.66f));
		Solidify(n, new Vector3(0, 1f, 0), new Vector3(2.4f, 2f, 0.3f), "wood");
	}

	private void Solidify(Node3D n, Vector3 c, Vector3 size, string surface)
	{
		var b = new StaticBody3D { Name = "Solid", CollisionLayer = 1, CollisionMask = 0 };
		b.SetMeta("surface", surface);
		b.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = size } });
		n.AddChild(b);
	}

	// ------------------------------------------------------------------ their materials

	private static StandardMaterial3D _coal, _tooth, _bone, _twine, _plow, _rubber, _track, _parka, _denim;
	private static StandardMaterial3D Coal => _coal ??= new StandardMaterial3D { ResourceName = "coal", AlbedoColor = new Color(0.04f, 0.04f, 0.045f), Roughness = 0.4f };
	private static StandardMaterial3D Tooth => _tooth ??= new StandardMaterial3D { ResourceName = "tooth", AlbedoColor = new Color(0.72f, 0.66f, 0.52f), Roughness = 0.35f };
	public static StandardMaterial3D Bone => _bone ??= new StandardMaterial3D { ResourceName = "bone", AlbedoColor = new Color(0.66f, 0.62f, 0.54f), Roughness = 0.7f, AlbedoTexture = ProcTextures.Grime() };
	private static StandardMaterial3D Twine => _twine ??= new StandardMaterial3D { ResourceName = "twine", AlbedoColor = new Color(0.25f, 0.2f, 0.15f), Roughness = 1f };
	private static StandardMaterial3D PlowPaint => _plow ??= new StandardMaterial3D { ResourceName = "plow_paint", AlbedoColor = new Color(0.52f, 0.36f, 0.08f), Roughness = 0.6f, AlbedoTexture = ProcTextures.Grime(), MetallicSpecular = 0.4f };
	private static StandardMaterial3D Rubber => _rubber ??= new StandardMaterial3D { ResourceName = "rubber", AlbedoColor = new Color(0.06f, 0.06f, 0.06f), Roughness = 0.85f };
	private static StandardMaterial3D TrackMat => _track ??= new StandardMaterial3D { ResourceName = "ski_track", AlbedoColor = new Color(0.4f, 0.45f, 0.55f), Roughness = 1f };
	private static StandardMaterial3D Parka => _parka ??= new StandardMaterial3D { ResourceName = "parka", AlbedoColor = new Color(0.45f, 0.1f, 0.08f), Roughness = 0.8f, AlbedoTexture = ProcTextures.Grime() };
	private static StandardMaterial3D Denim => _denim ??= new StandardMaterial3D { ResourceName = "denim", AlbedoColor = new Color(0.16f, 0.2f, 0.3f), Roughness = 0.9f, AlbedoTexture = ProcTextures.Grime() };
}
