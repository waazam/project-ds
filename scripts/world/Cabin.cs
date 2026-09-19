using Godot;

namespace ProjectDS.World;

/// <summary>
/// A small rental-cabin exterior: four board walls, a gable roof, a stone
/// chimney, and a closed front door. Local frame: front (the door) faces +Z,
/// same convention as ParkProp. The door itself is always a solid, closed
/// mesh with collision (there is no modelled interior yet); <see cref="SetBoarded"/>
/// only adds or removes the crossed planks nailed over it for the Act 3 beat.
/// </summary>
[Tool]
[GlobalClass]
public partial class Cabin : Node3D
{
	[Export] public float Width = 4.2f;
	[Export] public float Depth = 5.2f;
	[Export] public float WallHeight = 2.3f;
	[Export] public float DoorWidth = 0.95f;
	[Export] public float DoorHeight = 2.0f;
	[Export] public int Seed = 3;
	[Export] public bool BuildCollision = true;

	public bool DoorBoarded { get; private set; }
	/// <summary>World-space centre of the door, for triggers to line up against.</summary>
	public Vector3 DoorCenter => GlobalTransform * new Vector3(0, DoorHeight * 0.5f, Depth * 0.5f);
	/// <summary>A walkable point a few metres out from the door, clear of the walls.</summary>
	public Vector3 ApproachPoint => GlobalTransform * new Vector3(0, 0, Depth * 0.5f + 2.5f);

	private Node3D _gen;
	private Node3D _planks;

	public override void _Ready() => Build();

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);
		_planks = null;

		float hw = Width * 0.5f, hd = Depth * 0.5f, dw = DoorWidth * 0.5f;
		var k = new MeshKit();
		var wood = ProcTextures.WoodMat;
		var roofMat = ProcTextures.Flat("cabin_roof", new Color(0.10f, 0.09f, 0.08f), 0.95f);
		var stone = ProcTextures.RockMat;
		var doorMat = PropTextures.PostMat;

		const float wallT = 0.14f;
		k.Color = new Color(0.55f, 0.47f, 0.36f);
		k.Mat(wood);
		// side walls
		k.Box(new Vector3(-hw, WallHeight * 0.5f, 0), new Vector3(wallT, WallHeight, Depth), 1.2f);
		k.Box(new Vector3(hw, WallHeight * 0.5f, 0), new Vector3(wallT, WallHeight, Depth), 1.2f);
		// back wall
		k.Box(new Vector3(0, WallHeight * 0.5f, -hd), new Vector3(Width, WallHeight, wallT), 1.2f);
		// front wall either side of the doorway, plus the header above it
		float sideW = hw - dw;
		if (sideW > 0.02f)
		{
			k.Box(new Vector3(-(dw + sideW * 0.5f), WallHeight * 0.5f, hd), new Vector3(sideW, WallHeight, wallT), 1.2f);
			k.Box(new Vector3(dw + sideW * 0.5f, WallHeight * 0.5f, hd), new Vector3(sideW, WallHeight, wallT), 1.2f);
		}
		float headerH = WallHeight - DoorHeight;
		if (headerH > 0.02f)
			k.Box(new Vector3(0, DoorHeight + headerH * 0.5f, hd), new Vector3(DoorWidth, headerH, wallT), 1.2f);

		// gable roof
		float overhang = 0.35f, peakRise = 1.05f;
		float eave = WallHeight + 0.1f, peak = eave + peakRise;
		float halfSpan = hw + overhang;
		float slopeLen = new Vector2(halfSpan, peakRise).Length();
		float ang = Mathf.Atan2(peakRise, halfSpan);
		k.Color = new Color(0.85f, 0.83f, 0.8f);
		k.Mat(roofMat);
		foreach (float side in new[] { -1f, 1f })
		{
			var rot = Basis.FromEuler(new Vector3(0, 0, side * -ang));
			Vector3 center = new(side * halfSpan * 0.5f, (eave + peak) * 0.5f, 0);
			k.Box(center, new Vector3(slopeLen, 0.06f, Depth + overhang * 2f), 1f, rot);
		}
		// gable end triangles (front and back), simple boarded infill under the roofline
		k.Color = new Color(0.5f, 0.43f, 0.33f);
		k.Mat(wood);
		foreach (float z in new[] { -hd - wallT * 0.5f, hd + wallT * 0.5f })
		{
			k.Tri(new Vector3(-hw, WallHeight, z), new Vector3(hw, WallHeight, z), new Vector3(0, peak - 0.06f, z),
				new Vector3(0, 0, Mathf.Sign(z)), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1));
		}

		// chimney, stone, on the back-left corner
		k.Color = new Color(0.42f, 0.4f, 0.38f);
		k.Mat(stone);
		float chX = -hw + 0.35f, chZ = -hd - 0.3f;
		k.Box(new Vector3(chX, (WallHeight + 1.6f) * 0.5f, chZ), new Vector3(0.55f, WallHeight + 1.6f, 0.55f), 1.5f);
		k.Color = Colors.White;

		// closed door filling the opening: always solid, so the cabin is never actually enterable in this slice
		k.Color = new Color(0.22f, 0.16f, 0.11f);
		k.Mat(doorMat);
		k.Box(new Vector3(0, DoorHeight * 0.5f, hd), new Vector3(DoorWidth - 0.02f, DoorHeight - 0.02f, 0.06f), 1.3f);
		k.Color = Colors.White;

		k.CommitTo(_gen, "CabinMesh");

		if (BuildCollision && !Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "CabinBody", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "wood");
			_gen.AddChild(body);
			body.AddChild(new CollisionShape3D { Position = new Vector3(-hw, WallHeight * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(wallT + 0.1f, WallHeight, Depth) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(hw, WallHeight * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(wallT + 0.1f, WallHeight, Depth) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, WallHeight * 0.5f, -hd), Shape = new BoxShape3D { Size = new Vector3(Width, WallHeight, wallT + 0.1f) } });
			if (sideW > 0.02f)
			{
				body.AddChild(new CollisionShape3D { Position = new Vector3(-(dw + sideW * 0.5f), WallHeight * 0.5f, hd), Shape = new BoxShape3D { Size = new Vector3(sideW, WallHeight, wallT + 0.1f) } });
				body.AddChild(new CollisionShape3D { Position = new Vector3(dw + sideW * 0.5f, WallHeight * 0.5f, hd), Shape = new BoxShape3D { Size = new Vector3(sideW, WallHeight, wallT + 0.1f) } });
			}
			body.AddChild(new CollisionShape3D { Position = new Vector3(0, DoorHeight * 0.5f, hd), Shape = new BoxShape3D { Size = new Vector3(DoorWidth, DoorHeight, wallT + 0.1f) } });
			body.AddChild(new CollisionShape3D { Position = new Vector3(chX, (WallHeight + 1.6f) * 0.5f, chZ), Shape = new BoxShape3D { Size = new Vector3(0.55f, WallHeight + 1.6f, 0.55f) } });
		}

		if (DoorBoarded) BuildPlanks();
	}

	/// <summary>Nails 3 rough planks across the doorway. Purely visual: the door underneath was already solid.</summary>
	public void SetBoarded(bool boarded)
	{
		if (DoorBoarded == boarded) return;
		DoorBoarded = boarded;
		if (_planks != null) { _planks.QueueFree(); _planks = null; }
		if (boarded) BuildPlanks();
	}

	private void BuildPlanks()
	{
		float hd = Depth * 0.5f, dw = DoorWidth * 0.5f;
		var k = new MeshKit();
		var plankMat = PropTextures.SignPlankMat;
		k.Mat(plankMat);
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 131 + 7) };
		float[] heights = { DoorHeight * 0.22f, DoorHeight * 0.5f, DoorHeight * 0.8f };
		foreach (float h in heights)
		{
			float tilt = rng.RandfRange(-0.05f, 0.05f);
			float shade = rng.RandfRange(0.75f, 1.0f);
			k.Color = new Color(shade * 0.55f, shade * 0.45f, shade * 0.32f);
			k.Box(new Vector3(0, h, hd + 0.03f), new Vector3(DoorWidth + dw * 0.6f, 0.16f, 0.03f), 1.4f, Basis.FromEuler(new Vector3(0, 0, tilt)));
		}
		// two diagonal braces
		foreach (float side in new[] { -1f, 1f })
		{
			k.Color = new Color(0.4f, 0.33f, 0.24f);
			var rot = Basis.FromEuler(new Vector3(0, 0, side * 0.62f));
			k.Box(new Vector3(0, DoorHeight * 0.52f, hd + 0.045f), new Vector3(0.16f, DoorHeight * 1.05f, 0.03f), 1.4f, rot);
		}
		_planks = k.CommitTo(_gen, "Planks");
	}
}
