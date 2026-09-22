using Godot;

namespace ProjectDS.World;

/// <summary>
/// A weathered woodpile in the trees near the cabin: split logs stacked between two
/// stakes, a few rounds and chips on the ground around it. Set dressing for where the
/// axe waits on its chopping stump (the axe Pickup builds that stump itself). Grounds
/// itself on the terrain; local +Z is the stack's face. Collision is one box.
/// </summary>
[GlobalClass]
public partial class Woodpile : Node3D
{
	[Export] public float Length = 1.8f;
	[Export] public int Rows = 4;
	[Export] public int Seed = 3;

	public override void _Ready()
	{
		var terrain = GroundSnap.FindTerrain(this);
		float lo = 0f;
		if (terrain != null)
		{
			lo = float.MaxValue;
			foreach (var c in new[] { new Vector3(-Length * 0.5f, 0, -0.3f), new Vector3(Length * 0.5f, 0, -0.3f), new Vector3(-Length * 0.5f, 0, 0.3f), new Vector3(Length * 0.5f, 0, 0.3f) })
			{
				Vector3 w = GlobalTransform * c;
				lo = Mathf.Min(lo, terrain.HeightAt(w.X, w.Z));
			}
			GlobalPosition = GlobalPosition with { Y = lo };
		}
		var k = new MeshKit();
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 613 + 1) };
		const float r = 0.085f, depth = 0.55f;
		// the logs: rows of split halves and rounds, ends toward +Z/-Z
		for (int row = 0; row < Rows; row++)
		{
			int count = Mathf.FloorToInt(Length / (r * 2.1f)) - (row % 2);
			float x0 = -Length * 0.5f + r * (1.05f + (row % 2));
			for (int i = 0; i < count; i++)
			{
				float x = x0 + i * r * 2.1f + rng.RandfRange(-0.01f, 0.01f);
				float y = r + row * r * 1.8f + rng.RandfRange(-0.01f, 0.01f);
				float z0 = -depth * 0.5f + rng.RandfRange(-0.05f, 0.05f), z1 = depth * 0.5f + rng.RandfRange(-0.05f, 0.05f);
				float sh = rng.RandfRange(0.55f, 0.8f);
				k.Mat(ProcTextures.BarkMat);
				k.Color = new Color(sh, sh * 0.95f, sh * 0.9f);
				k.Cylinder(new Vector3(x, y, z0), new Vector3(x, y, z1), r * rng.RandfRange(0.85f, 1.05f), r * rng.RandfRange(0.85f, 1.05f), 6, false, 2f, rng.Randf());
				k.Mat(ProcTextures.EndGrainMat);
				float g = rng.RandfRange(0.45f, 0.62f);   // old, grey end grain
				k.Color = new Color(g, g * 0.95f, g * 0.88f);
				ItemMeshes.Disc(k, new Vector3(x, y, z1), Vector3.Back, r * 0.95f, 6, rng.Randf());
				ItemMeshes.Disc(k, new Vector3(x, y, z0), Vector3.Forward, r * 0.95f, 6, rng.Randf());
			}
		}
		// two stakes holding the ends
		k.Mat(ProcTextures.WoodMat);
		k.Color = new Color(0.45f, 0.4f, 0.35f);
		float top = Rows * r * 1.8f + 0.1f;
		foreach (float sx in new[] { -1f, 1f })
			k.Cylinder(new Vector3(sx * (Length * 0.5f + 0.05f), -0.2f, 0), new Vector3(sx * (Length * 0.5f + 0.06f), top, 0.02f), 0.035f, 0.03f, 5, true, 2f);
		// chips and a couple of loose rounds on the ground in front
		k.Mat(ProcTextures.EndGrainMat);
		for (int i = 0; i < 9; i++)
		{
			float c = rng.RandfRange(0.5f, 0.75f);
			k.Color = new Color(c, c * 0.9f, c * 0.78f);
			var p = new Vector3(rng.RandfRange(-1.2f, 1.4f), 0.01f, rng.RandfRange(0.45f, 1.6f));
			if (terrain != null) { Vector3 w = GlobalTransform * p; p.Y = terrain.HeightAt(w.X, w.Z) - lo + 0.01f; }
			k.Box(p, new Vector3(rng.RandfRange(0.05f, 0.1f), 0.015f, rng.RandfRange(0.03f, 0.05f)), 6f, new Basis(Vector3.Up, rng.RandfRange(0, 3f)));
		}
		k.CommitTo(this, "Mesh");
		if (Engine.IsEditorHint()) return;
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, top * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(Length + 0.15f, top, depth + 0.1f) } });
		AddChild(new ClearZone { Name = "Clear", Radius = 2.6f, ClearFoliage = false });
	}
}
