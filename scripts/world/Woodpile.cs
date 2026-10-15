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
				float sh = rng.RandfRange(0.75f, 1.0f);
				float r0 = r * rng.RandfRange(0.85f, 1.05f), r1 = r * rng.RandfRange(0.85f, 1.05f), u = rng.Randf();
				float g = rng.RandfRange(0.45f, 0.62f);   // old, grey end grain
				float e1 = rng.Randf(), e0 = rng.Randf();
				var bark = new Color(sh, sh * 0.95f, sh * 0.9f);
				var end = new Color(g, g * 0.95f, g * 0.88f);
				// (2026-10-15) the logs modelled in Blender (tools/Blender/kits.py wp_log_0..2: ridged bark, sawn ends with their
				// checks), each sized to its place in the stack and turned about its length; the cylinders are the fallback
				float rr = (r0 + r1) * 0.5f;
				var logXf = new Transform3D(new Basis(Vector3.Back, (row * 2.3f + i * 1.7f) % Mathf.Tau) * Basis.FromScale(new Vector3(rr / 0.085f, rr / 0.085f, (z1 - z0) / 0.55f)),
					new Vector3(x, y, (z0 + z1) * 0.5f));
				if (FurnitureKit.Add(k, "wp_log_" + ((row * 7 + i) % 3), logXf,
					FurnitureKit.Tinted(("bark", BuildingTextures.BarkScanMat, bark), ("end", ProcTextures.EndGrainMat, end), ("grip", ProcTextures.EndGrainMat, end * 0.35f))))
					continue;
				k.Mat(BuildingTextures.BarkScanMat);   // (2026-10-14a: the bark scan, nine-sided: they were six-sided sticks in a flat bark)
				k.Color = bark;
				k.Cylinder(new Vector3(x, y, z0), new Vector3(x, y, z1), r0, r1, 9, false, 1.2f, u);
				k.Mat(ProcTextures.EndGrainMat);
				k.Color = end;
				ItemMeshes.Disc(k, new Vector3(x, y, z1), Vector3.Back, r * 0.84f, 9, e1);
				ItemMeshes.Disc(k, new Vector3(x, y, z0), Vector3.Forward, r * 0.84f, 9, e0);
			}
		}
		// two stakes holding the ends
		k.Mat(ProcTextures.WoodMat);
		k.Color = new Color(0.45f, 0.4f, 0.35f);
		float top = Rows * r * 1.8f + 0.1f;
		foreach (float sx in new[] { -1f, 1f })
		{
			Vector3 a = new(sx * (Length * 0.5f + 0.05f), -0.2f, 0), b = new(sx * (Length * 0.5f + 0.06f), top, 0.02f);
			// (2026-10-15) modelled in Blender (wp_stake, 1 m up from its point, stretched to reach); the cylinder is its fallback
			var stakeXf = new Transform3D(new Basis(new Quaternion(Vector3.Up, (b - a).Normalized())) * Basis.FromScale(new Vector3(1f, (b - a).Length(), 1f)), a);
			if (!FurnitureKit.Add(k, "wp_stake", stakeXf, FurnitureKit.Tinted(("wood", ProcTextures.WoodMat, k.Color))))
			{
				k.Mat(ProcTextures.WoodMat);
				k.Cylinder(a, b, 0.035f, 0.03f, 5, true, 2f);
			}
		}
		// chips and a couple of loose rounds on the ground in front
		k.Mat(ProcTextures.EndGrainMat);
		for (int i = 0; i < 9; i++)
		{
			float c = rng.RandfRange(0.5f, 0.75f);
			k.Color = new Color(c, c * 0.9f, c * 0.78f);
			var p = new Vector3(rng.RandfRange(-1.2f, 1.4f), 0.01f, rng.RandfRange(0.45f, 1.6f));
			if (terrain != null) { Vector3 w = GlobalTransform * p; p.Y = terrain.HeightAt(w.X, w.Z) - lo + 0.01f; }
			var size = new Vector3(rng.RandfRange(0.05f, 0.1f), 0.015f, rng.RandfRange(0.03f, 0.05f));
			var turn = new Basis(Vector3.Up, rng.RandfRange(0, 3f));
			// (2026-10-15) a chip modelled in Blender (wp_chip, 0.075 x 0.015 x 0.04, sized to each); the box is its fallback
			if (FurnitureKit.Add(k, "wp_chip", new Transform3D(turn * Basis.FromScale(size / new Vector3(0.075f, 0.015f, 0.04f)), p),
				FurnitureKit.Tinted(("end", ProcTextures.EndGrainMat, k.Color))))
				continue;
			k.Mat(ProcTextures.EndGrainMat);
			k.Box(p, size, 6f, turn);
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
