using Godot;

namespace ProjectDS.World;

/// <summary>
/// Act 6's "the woods turn menacing" set dressing: a handful of oversized firs
/// looming around the clearing, and a faint pulsing vein pattern laid over the
/// ground. Deterministically seeded, so it can be built at load
/// (<see cref="Prepare"/>) and kept out of the tree (no rendering, no physics)
/// until the story reaches it, when <see cref="Reveal"/> simply attaches it.
/// Calling Reveal without Prepare still works (it builds on the spot).
/// </summary>
[GlobalClass]
public partial class DeepZoneDressing : Node3D
{
	private Node3D _root;
	private bool _shown;

	/// <summary>Builds the giants and the ground overlay centred on <paramref name="center"/> (world
	/// space) and detaches them, ready to be shown. No-op once built.</summary>
	public void Prepare(Vector3 center, float radius = 34f)
	{
		if (_root != null) return;
		var terrain = GroundSnap.FindTerrain(this);
		_root = new Node3D { Name = "Generated" };
		AddChild(_root);   // in the tree while building: the giants need a resolvable global transform
		BuildGiantTrees(_root, terrain, center, radius);
		BuildVeinyGround(_root, terrain, center, radius);
		RemoveChild(_root);
	}

	/// <summary>Shows the dressing (building it first if <see cref="Prepare"/> never ran).</summary>
	public void Reveal(Vector3 center, float radius = 34f)
	{
		if (_shown) return;
		_shown = true;
		Prepare(center, radius);
		if (_root.GetParent() == null) AddChild(_root);
	}

	public override void _ExitTree()
	{
		// Built but never shown: nothing else owns it, so free it with the level.
		if (_root != null && IsInstanceValid(_root) && _root.GetParent() == null) _root.Free();
		_root = null;
	}

	private static void BuildGiantTrees(Node3D parent, ForestTerrain terrain, Vector3 center, float radius)
	{
		var trunk = ProcTextures.WoodMat;
		var needle = ProcTextures.Flat("giant_fir_needle", new Color(0.06f, 0.1f, 0.07f), 0.95f);
		var rng = new RandomNumberGenerator { Seed = 4177 };
		int count = 9;
		for (int i = 0; i < count; i++)
		{
			float ang = (Mathf.Tau / count) * i + rng.RandfRange(-0.25f, 0.25f);
			float dist = radius * rng.RandfRange(0.7f, 1.15f);
			Vector3 pos = center + new Vector3(Mathf.Cos(ang) * dist, 0, Mathf.Sin(ang) * dist);
			pos.Y = terrain?.HeightAt(pos.X, pos.Z) ?? center.Y;

			float height = rng.RandfRange(38f, 58f);
			float trunkR = height * 0.028f;
			var tree = new Node3D { Name = $"Giant{i}" };
			// Must be parented before GlobalPosition is set, or Godot can't resolve the transform.
			parent.AddChild(tree);
			tree.GlobalPosition = pos;

			var k = new MeshKit();
			k.Color = new Color(0.32f, 0.26f, 0.2f);
			k.Mat(trunk).Cylinder(new Vector3(0, 0, 0), new Vector3(0, height * 0.42f, 0), trunkR, trunkR * 0.6f, 7);
			k.Color = new Color(0.07f, 0.11f, 0.075f);
			k.Mat(needle);
			int tiers = 8;
			float crownStart = height * 0.16f;
			for (int t = 0; t < tiers; t++)
			{
				float f = t / (float)(tiers - 1);
				float y0 = Mathf.Lerp(crownStart, height * 0.98f, f);
				float y1 = y0 + (height - crownStart) / tiers * 1.35f;
				float r = Mathf.Lerp(trunkR * 7f, trunkR * 1.1f, f * f);
				k.Cylinder(new Vector3(0, y0, 0), new Vector3(0, y1, 0), r, r * 0.15f, 7);
			}
			k.Color = Colors.White;
			k.CommitTo(tree, "Mesh", true);

			var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "wood");
			tree.AddChild(body);
			body.AddChild(new CollisionShape3D
			{
				Position = new Vector3(0, height * 0.2f, 0),
				Shape = new CylinderShape3D { Radius = trunkR * 1.4f, Height = height * 0.4f },
			});
		}
	}

	/// <summary>Terrain-conforming, fogged overlay that goes from worms at the edge to veins at the centre.</summary>
	private static void BuildVeinyGround(Node3D parent, ForestTerrain terrain, Vector3 center, float radius)
		=> parent.AddChild(VeinyGround.Create(terrain, center, radius));
}
