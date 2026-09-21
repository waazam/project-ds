using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World;

/// <summary>
/// The bunker's outside: a round vault door in a stepped concrete face, set
/// into an earth mound deep in the woods. Locked until the cabin has been seen
/// burning (Act 7); the first time the player then gets close, the door swings
/// open for good. Walking in through the doorway (a trigger just inside the
/// hatch) carries them into the bunker's interior (Act 8) and marks checkpoint 7.
/// Group "bunker_marker" so the compass can find it.
///
/// Restore: in _Ready the door is open (silently) if the story is at Act 8 or
/// later. Entry works every time the open doorway is walked into (a Continue
/// respawns the player outside), until the walkie has been found.
/// Geometry lives in <see cref="BunkerExterior"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class Bunker : Node3D
{
	[Export] public float OpenRadius = 14f;

	public bool IsOpen { get; private set; }

	/// <summary>A spot on the apron in front of the door, on the ground (the Act 8 checkpoint's respawn point).</summary>
	public Vector3 ApproachPointWorld
	{
		get
		{
			var p = ToGlobal(new Vector3(0, 0.3f, 3.2f));
			if (GetTree()?.GetFirstNodeInGroup("terrain") is ForestTerrain t) p.Y = Mathf.Max(p.Y, t.HeightAt(p.X, p.Z) + 0.1f);
			return p;
		}
	}
	/// <summary>Camera yaw that faces the door from the apron.</summary>
	public float ApproachYaw => Mathf.Atan2(GlobalBasis.Z.X, GlobalBasis.Z.Z);
	/// <summary>A point just inside the doorway, in the entry trigger (walk here to enter).</summary>
	public Vector3 EntryPointWorld => ToGlobal(new Vector3(0, BunkerExterior.FloorY, -1.05f));

	private Node3D _gen;
	private Node3D _doorNode;
	private CollisionShape3D _closedCollision, _openCollision;
	private PlayerController _player;

	public override void _Ready()
	{
		AddToGroup("bunker_marker");
		Build();
		if (!Engine.IsEditorHint())
			SetOpen(StoryManager.Instance is { Current: >= Checkpoint.Act8BunkerEntered });
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || IsOpen) return;
		if (StoryManager.Instance is not { Current: >= Checkpoint.Act7CabinBurning }) return;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;
		float d = new Vector2(_player.GlobalPosition.X - GlobalPosition.X, _player.GlobalPosition.Z - GlobalPosition.Z).Length();
		if (d > OpenRadius) return;
		Open();
	}

	/// <summary>Instantly and silently open or shut the door (restore, previews).</summary>
	public void SetOpen(bool open)
	{
		if (_gen == null) return;
		IsOpen = open;
		if (_doorNode != null) { _doorNode.QueueFree(); _doorNode = null; }
		_doorNode = open ? BuildOpenDoor() : BuildClosedDoor();
		if (_closedCollision != null) _closedCollision.Disabled = open;
		if (_openCollision != null) _openCollision.Disabled = !open;
	}

	/// <summary>The story beat: the door grinds open (with its groan) and stays open.</summary>
	private void Open()
	{
		SetOpen(true);
		BunkerKit.OneShot(_gen, "res://assets/audio/sfx/trunk_creak_02.wav", new Vector3(0, 1f, 0.3f), "Events", 3f, 0.6f, 4f, 40f);
		GD.Print("[story] the bunker door stands open");
	}

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);
		_doorNode = null;

		var terrain = Engine.IsEditorHint() ? null : GetTree()?.GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (terrain != null)
		{
			// Sit the door at the level of the ground just in front of it. (GroundSnap uses the lowest point
			// under the footprint, which on a slope left the door half buried by the ground before it.)
			// The mound follows the terrain and the concrete reaches well below grade, so nothing floats.
			var front = ToGlobal(new Vector3(0, 0, 1.2f));
			var p = GlobalPosition;
			p.Y = Mathf.Max(p.Y, terrain.HeightAt(front.X, front.Z) - 0.05f);
			GlobalPosition = p;
		}
		float TerrainY(float x, float z)
		{
			if (terrain == null) return 0f;
			var w = ToGlobal(new Vector3(x, 0, z));
			return terrain.HeightAt(w.X, w.Z) - GlobalPosition.Y;
		}
		var moundTris = BunkerExterior.BuildMound(_gen, TerrainY, true);
		BunkerExterior.BuildConcrete(_gen, _gen);

		if (Engine.IsEditorHint()) return;
		var body = new StaticBody3D { Name = "BunkerBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		_gen.AddChild(body);
		void Box(Vector3 c, Vector3 s) => body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		void BeamBox(Vector3 from, Vector3 to, float width, float height)
		{
			Vector3 z = (to - from).Normalized();
			Vector3 x = Vector3.Up.Cross(z).Normalized();
			Vector3 y = z.Cross(x).Normalized();
			body.AddChild(new CollisionShape3D
			{
				Transform = new Transform3D(new Basis(x, y, z), (from + to) * 0.5f),
				Shape = new BoxShape3D { Size = new Vector3(width, height, from.DistanceTo(to)) },
			});
		}
		const float gap = 0.55f, fy = BunkerExterior.FloorY, back = BunkerExterior.VestibuleBackZ;
		// The face, solid either side of and above the doorway.
		Box(new Vector3(-(gap + 2.75f) * 0.5f, (3.05f + BunkerExterior.Base) * 0.5f, 0.05f), new Vector3(2.75f - gap, 3.05f - BunkerExterior.Base, 0.7f));
		Box(new Vector3((gap + 2.75f) * 0.5f, (3.05f + BunkerExterior.Base) * 0.5f, 0.05f), new Vector3(2.75f - gap, 3.05f - BunkerExterior.Base, 0.7f));
		Box(new Vector3(0, 2.6f, 0.05f), new Vector3(gap * 2f, 0.9f, 0.7f));
		// The vestibule: floor, walls, ceiling and back, a corridor just wider than a person.
		Box(new Vector3(0, fy - 0.26f, (0.5f + back) * 0.5f), new Vector3(1.2f, 0.52f, 0.5f - back));
		Box(new Vector3(-0.65f, 1.2f, (-0.3f + back) * 0.5f), new Vector3(0.3f, 2.0f, -0.3f - back));
		Box(new Vector3(0.65f, 1.2f, (-0.3f + back) * 0.5f), new Vector3(0.3f, 2.0f, -0.3f - back));
		Box(new Vector3(0, 2.3f, (-0.3f + back) * 0.5f), new Vector3(1.6f, 0.3f, -0.3f - back));
		Box(new Vector3(0, 1.2f, back - 0.1f), new Vector3(1.6f, 2.4f, 0.2f));
		// Apron and wing walls.
		BeamBox(BunkerExterior.ApronFrom, BunkerExterior.ApronTo, 4.8f, 1.0f);
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 a = BunkerExterior.WingPoint(s, false), b = BunkerExterior.WingPoint(s, true), m = a.Lerp(b, 0.5f);
			BeamBox(a + Vector3.Up * BunkerExterior.WingMidA, m + Vector3.Up * BunkerExterior.WingMidA, 0.3f, BunkerExterior.WingTopA - BunkerExterior.Base);
			BeamBox(m + Vector3.Up * BunkerExterior.WingMidB, b + Vector3.Up * BunkerExterior.WingMidB, 0.3f, BunkerExterior.WingTopB - BunkerExterior.Base);
		}
		// The mound itself, walkable.
		var mound = new ConcavePolygonShape3D { BackfaceCollision = true };
		mound.SetFaces(moundTris);
		body.AddChild(new CollisionShape3D { Name = "Mound", Shape = mound });
		// The door: closed it fills the gap; open, the swung leaf is solid where it stands.
		_closedCollision = new CollisionShape3D { Position = new Vector3(0, 1.2f, 0.2f), Shape = new BoxShape3D { Size = new Vector3(1.2f, 2.0f, 0.2f) } };
		body.AddChild(_closedCollision);
		Vector3 hinge = new(-BunkerExterior.DoorRadius, BunkerExterior.DoorCenterY, BunkerExterior.DoorZ);
		Vector3 leafDir = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(-100f), 0)) * Vector3.Right;
		_openCollision = new CollisionShape3D
		{
			Transform = new Transform3D(Basis.LookingAt(leafDir, Vector3.Up), hinge + leafDir * BunkerExterior.DoorRadius),
			Shape = new BoxShape3D { Size = new Vector3(0.2f, 2.0f, BunkerExterior.DoorRadius * 2f) },
			Disabled = true,
		};
		body.AddChild(_openCollision);

		// A proper doorway trigger: just inside the hatch, so only walking in through the door counts.
		var trigger = new Area3D { Name = "EntryTrigger", CollisionLayer = 0, CollisionMask = 2, Monitorable = false };
		trigger.Position = new Vector3(0, fy + 0.95f, -1.05f);
		trigger.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 1.7f, 1.1f) } });
		_gen.AddChild(trigger);
		trigger.BodyEntered += OnEntered;

		_doorNode = BuildClosedDoor();
	}

	private Node3D BuildClosedDoor()
	{
		var dk = new MeshKit();
		BunkerExterior.VaultDoor(dk, new Vector3(0, BunkerExterior.DoorCenterY, BunkerExterior.DoorZ), ProcTextures.MetalMat);
		var mesh = dk.CommitTo(_gen, "DoorMesh");
		if (!Engine.IsEditorHint())
		{
			// Story: found before the fire, it is locked. Focusable so the prompt can say so.
			mesh.AddChild(new BunkerLockedHatch
			{
				Name = "Hatch", Prompt = "Locked", MaxDistance = 3f, PickRadius = 1.0f,
				Position = new Vector3(0, BunkerExterior.DoorCenterY, BunkerExterior.DoorZ), PickOffset = new Vector3(0, 0, -0.4f),
			});
		}
		return mesh;
	}

	private Node3D BuildOpenDoor()
	{
		// Swings open about its left edge (the hinge). Building the disc relative to that hinge and
		// letting Xf place and rotate it keeps the hinge edge itself fixed between the two states.
		var leaf = new MeshKit
		{
			Xf = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(-100f), 0)),
				new Vector3(-BunkerExterior.DoorRadius, 0f, BunkerExterior.DoorZ)),
		};
		BunkerExterior.VaultDoor(leaf, new Vector3(BunkerExterior.DoorRadius, BunkerExterior.DoorCenterY, 0f), ProcTextures.MetalMat);
		return leaf.CommitTo(_gen, "DoorOpenMesh");
	}

	private void OnEntered(Node3D body)
	{
		if (!IsOpen || body is not PlayerController player) return;
		var story = StoryManager.Instance;
		// After the walkie the interior has nothing left and no way back out: the doorway stays dark.
		if (story is { Current: >= Checkpoint.Act10WalkieFound }) return;
		var interior = BunkerInterior.Instance;
		if (interior == null || interior.IsAdmitting) return;
		interior.AdmitPlayer(player);
		story?.ReachCheckpoint(Checkpoint.Act8BunkerEntered, ApproachPointWorld, ApproachYaw);
		GD.Print("[story] Act 8: into the bunker");
	}
}
