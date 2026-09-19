using Godot;

namespace ProjectDS.World;

/// <summary>
/// A small tool shed near the cabin, doorway always open (nothing blocks
/// entry): three board walls, a lean-to roof, and an open front. Whatever's
/// inside (the hammer) is gated by its own Pickup, not by this structure.
/// Local frame: the open front faces +Z.
/// </summary>
[Tool]
[GlobalClass]
public partial class Shed : Node3D
{
	[Export] public float Width = 2.2f;
	[Export] public float Depth = 2.0f;
	[Export] public float WallHeight = 2.0f;
	[Export] public bool BuildCollision = true;

	public override void _Ready() => Build();

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		var gen = new Node3D { Name = "Generated" };
		AddChild(gen);

		float hw = Width * 0.5f, hd = Depth * 0.5f;
		const float wallT = 0.1f;
		var k = new MeshKit();
		var wood = ProcTextures.WoodMat;
		var roofMat = ProcTextures.Flat("shed_roof", new Color(0.11f, 0.1f, 0.09f), 0.95f);

		k.Color = new Color(0.46f, 0.4f, 0.32f);
		k.Mat(wood);
		k.Box(new Vector3(-hw, WallHeight * 0.5f, 0), new Vector3(wallT, WallHeight, Depth), 1.1f);
		k.Box(new Vector3(hw, WallHeight * 0.5f, 0), new Vector3(wallT, WallHeight, Depth), 1.1f);
		k.Box(new Vector3(0, WallHeight * 0.5f, -hd), new Vector3(Width, WallHeight, wallT), 1.1f);

		// lean-to roof, single slope, low over the door
		float backH = WallHeight + 0.55f, frontH = WallHeight + 0.05f;
		var slope = new Vector3(0, backH - frontH, Depth);
		float ang = Mathf.Atan2(slope.Y, slope.Z);
		k.Color = new Color(0.8f, 0.78f, 0.75f);
		k.Mat(roofMat);
		k.Box(new Vector3(0, (backH + frontH) * 0.5f, 0), new Vector3(Width + 0.3f, 0.05f, slope.Length() + 0.3f), 1f, Basis.FromEuler(new Vector3(-ang, 0, 0)));

		k.CommitTo(gen, "ShedMesh");

		if (BuildCollision && !Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "ShedBody", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "wood");
			gen.AddChild(body);
			body.AddChild(new CollisionShape3D { Position = new Vector3(-hw, WallHeight * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(wallT + 0.08f, WallHeight, Depth) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(hw, WallHeight * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(wallT + 0.08f, WallHeight, Depth) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, WallHeight * 0.5f, -hd), Shape = new BoxShape3D { Size = new Vector3(Width, WallHeight, wallT + 0.08f) } });
		}
	}
}
