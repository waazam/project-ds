using Godot;

namespace ProjectDS.World;

/// <summary>
/// Act 6's "the woods turn menacing" set dressing: a handful of oversized firs
/// looming around the clearing, and a faint pulsing vein pattern laid over the
/// ground. Built once, on demand, rather than at scene load, since the effect
/// should only appear once the story reaches it.
/// </summary>
[GlobalClass]
public partial class DeepZoneDressing : Node3D
{
	private bool _built;

	/// <summary>Spawns the giants and the ground overlay centred on <paramref name="center"/> (world space).</summary>
	public void Reveal(Vector3 center, float radius = 34f)
	{
		if (_built) return;
		_built = true;
		var terrain = GroundSnap.FindTerrain(this);

		BuildGiantTrees(terrain, center, radius);
		BuildVeinyGround(terrain, center, radius);
	}

	private void BuildGiantTrees(ForestTerrain terrain, Vector3 center, float radius)
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
			AddChild(tree);
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

	private void BuildVeinyGround(ForestTerrain terrain, Vector3 center, float radius)
	{
		var shader = GD.Load<Shader>("res://assets/shaders/veiny_ground.gdshader");
		var mat = new ShaderMaterial { Shader = shader };
		var mesh = new PlaneMesh { Size = new Vector2(radius * 2.4f, radius * 2.4f), Material = mat };
		float y = (terrain?.HeightAt(center.X, center.Z) ?? center.Y) + 0.035f;
		AddChild(new MeshInstance3D
		{
			Mesh = mesh,
			Position = new Vector3(center.X, y, center.Z),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}
}
