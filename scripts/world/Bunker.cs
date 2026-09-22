using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World;

/// <summary>
/// The bunker's outside: a round vault door in a stepped concrete face, set
/// into an earth mound deep in the woods. Locked until the cabin has been seen
/// burning (Act 7); the first time the player then gets close, the door swings
/// open for good ("It's open."). Walking in through the doorway (a trigger just
/// inside the hatch) carries them into the bunker's interior (Act 8) and marks
/// checkpoint 7. A laminated station card bolted beside the door can be read at
/// any time, locked or not. Group "bunker_marker" so the compass can find it.
///
/// Restore: in _Ready the door is open (silently, no caption) if the story is at
/// Act 8 or later. Entry works every time the open doorway is walked into (a
/// Continue respawns the player outside), until the walkie has been found.
/// Geometry lives in <see cref="BunkerExterior"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class Bunker : Node3D
{
	[Export] public float OpenRadius = 14f;

	/// <summary>The station card beside the hatch (story-gaps P10).</summary>
	public const string StationCardText =
		"OVERLOOK PARK\n" +
		"REMOTE MONITORING STATION 3\n" +
		"AUTHORISED PERSONNEL ONLY\n" +
		"Unstaffed since 09/98.";

	public bool IsOpen { get; private set; }

	/// <summary>A spot on the apron in front of the door, on the ground (the Act 8 checkpoint's respawn point).</summary>
	public Vector3 ApproachPointWorld
	{
		get
		{
			// On the apron's far end (or its ramp down to lower ground), or on the ground if that is higher.
			float y = 0.3f;
			if (_rampEndTop is { } end)
			{
				float t = Mathf.Clamp((3.2f - BunkerExterior.FaceZ) / (end.Z - BunkerExterior.FaceZ), 0f, 1f);
				y = Mathf.Lerp(BunkerExterior.FloorY, end.Y, t) + 0.1f;
			}
			var p = ToGlobal(new Vector3(0, y, 3.2f));
			if (GetTree()?.GetFirstNodeInGroup("terrain") is ForestTerrain terrain) p.Y = Mathf.Max(p.Y, terrain.HeightAt(p.X, p.Z) + 0.1f);
			return p;
		}
	}
	/// <summary>Camera yaw that faces the door from the apron.</summary>
	public float ApproachYaw => Mathf.Atan2(GlobalBasis.Z.X, GlobalBasis.Z.Z);
	/// <summary>A point just inside the doorway, in the entry trigger (walk here to enter).</summary>
	public Vector3 EntryPointWorld => ToGlobal(new Vector3(0, BunkerExterior.FloorY, -1.05f));
	/// <summary>Where the station card hangs (local): right of the frame, at chest height.</summary>
	public static Vector3 StationCardLocal => new(1.62f, 1.42f, BunkerExterior.FaceZ);

	private Node3D _gen;
	private Node3D _doorNode;
	private CollisionShape3D _closedCollision, _openCollision;
	private Area3D _openTrigger;
	/// <summary>Where the apron's ramp meets lower ground in front (top surface, local), or null when the apron is flat.</summary>
	private Vector3? _rampEndTop;

	public override void _Ready()
	{
		AddToGroup("bunker_marker");
		Build();
		if (Engine.IsEditorHint()) return;
		SetOpen(StoryManager.Instance is { Current: >= Checkpoint.Act8BunkerEntered });
		if (StoryManager.Instance is { } s) s.CheckpointReached += OnCheckpoint;
	}

	public override void _ExitTree()
	{
		if (!Engine.IsEditorHint() && StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
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

	private static bool StoryAllowsOpening => StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning };

	/// <summary>The player walked into the opening radius.</summary>
	private void OnApproach(PlayerController _)
	{
		if (IsOpen || !StoryAllowsOpening) return;
		Open();
	}

	/// <summary>Checkpoint 6 could conceivably fire with the player already inside the radius: re-check.</summary>
	private void OnCheckpoint(Checkpoint _)
	{
		if (IsOpen || !StoryAllowsOpening || StoryBeat.PlayerInside(_openTrigger) == null) return;
		Open();
	}

	/// <summary>The story beat: the door grinds open (with its groan) and stays open; two words follow.</summary>
	private void Open()
	{
		SetOpen(true);
		BunkerKit.OneShot(_gen, "res://assets/audio/sfx/trunk_creak_02.wav", new Vector3(0, 1f, 0.3f), "Events", 3f, 0.6f, 4f, 40f);
		GD.Print("[story] the bunker door stands open");
		// Only ever from here (a restore opens it silently through SetOpen), so the line plays once.
		_ = Cutscene.Run(this, async ct =>
		{
			await Cutscene.Wait(this, 1.2, ct);
			await StoryBeat.Caption(this, "It's open.", 0.8f, 1.8f, 0.8f);
		});
	}

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);
		_doorNode = null;
		_openTrigger = null;

		var terrain = Engine.IsEditorHint() ? null : GetTree()?.GetFirstNodeInGroup("terrain") as ForestTerrain;
		if (terrain != null)
		{
			// Sit the door at the level of the ground just in front of it. (GroundSnap uses the lowest point
			// under the footprint, which on a slope left the door half buried by the ground before it.)
			// The mound follows the terrain and the concrete reaches well below grade, so nothing floats.
			var front = ToGlobal(new Vector3(0, 0, 1.2f));
			var p = GlobalPosition;
			p.Y = Mathf.Max(p.Y, terrain.HeightAt(front.X, front.Z) - 0.05f);
			// ...and never so low that the hill runs through the vestibule: the walkway from the sill to
			// the back must clear the ground (the placement follows the trail, so the slope can go either way).
			foreach (var (x, z) in new[] { (0f, 0.5f), (-0.45f, -0.3f), (0.45f, -0.3f), (0f, -1.2f), (0f, -2.2f) })
			{
				var w = ToGlobal(new Vector3(x, 0, z));
				p.Y = Mathf.Max(p.Y, terrain.HeightAt(w.X, w.Z) - BunkerExterior.FloorY + 0.04f);
			}
			GlobalPosition = p;
		}
		float TerrainY(float x, float z)
		{
			if (terrain == null) return 0f;
			var w = ToGlobal(new Vector3(x, 0, z));
			return terrain.HeightAt(w.X, w.Z) - GlobalPosition.Y;
		}
		var moundTris = BunkerExterior.BuildMound(_gen, TerrainY, true);
		// The placement follows the trail, so the ground in front can fall away from the sill: then the
		// apron ramps down to meet it instead of ending in a ledge. Higher ground buries it as before.
		_rampEndTop = null;
		if (terrain != null)
		{
			// Short and steep (about 37 degrees, well under the walkable limit) so it catches a falling
			// hillside within a few metres and ends just under the ground there.
			float farZ = 3.6f;
			for (int i = 0; i < 8; i++)
			{
				float drop = BunkerExterior.FloorY - (TerrainY(0, farZ) + 0.03f);
				float need = BunkerExterior.FaceZ + drop * 1.33f;
				if (need <= farZ + 0.05f) break;
				farZ = Mathf.Min(need, 6f);
			}
			float groundFar = TerrainY(0, farZ);
			if (groundFar < BunkerExterior.ApronTo.Y + 0.5f - 0.15f) _rampEndTop = new Vector3(0, groundFar + 0.03f, farZ);
		}
		BunkerExterior.BuildConcrete(_gen, _gen, _rampEndTop);
		BuildStationCardPlate();

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
		// The lintel and the vestibule ceiling sit at the round opening's top (2.37 m), not below it: a
		// 1.75 m capsule standing a little high on the sill still fits through.
		Box(new Vector3(0, 2.7f, 0.05f), new Vector3(gap * 2f, 0.7f, 0.7f));
		// The vestibule: floor, walls, ceiling and back, a corridor just wider than a person.
		Box(new Vector3(0, fy - 0.26f, (0.5f + back) * 0.5f), new Vector3(1.2f, 0.52f, 0.5f - back));
		Box(new Vector3(-0.65f, 1.2f, (-0.3f + back) * 0.5f), new Vector3(0.3f, 2.0f, -0.3f - back));
		Box(new Vector3(0.65f, 1.2f, (-0.3f + back) * 0.5f), new Vector3(0.3f, 2.0f, -0.3f - back));
		Box(new Vector3(0, 2.5f, (-0.3f + back) * 0.5f), new Vector3(1.6f, 0.3f, -0.3f - back));
		Box(new Vector3(0, 1.2f, back - 0.1f), new Vector3(1.6f, 2.4f, 0.2f));
		// Apron (ramped if the ground in front is lower) and wing walls. A ramp's collider is a flat-ended
		// wedge whose top edge is the sill itself, so the surface runs on from the vestibule floor without a lip.
		if (_rampEndTop is { } rampEnd)
		{
			Vector3 sill = new(0, fy, 0.5f);   // where the vestibule floor box ends
			Vector3 mid = sill.Lerp(rampEnd, (BunkerExterior.ApronTo.Z - sill.Z) / (rampEnd.Z - sill.Z));
			body.AddChild(new CollisionShape3D { Shape = BunkerExterior.RampShape(sill, mid, 2.4f) });
			body.AddChild(new CollisionShape3D { Shape = BunkerExterior.RampShape(mid, rampEnd, 1.4f) });
		}
		else BeamBox(BunkerExterior.ApronFrom, BunkerExterior.ApronTo, 4.8f, 1.0f);
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

		// Coming within OpenRadius after the fire opens the door (a trigger, not a per-frame poll).
		_openTrigger = StoryBeat.MakeTrigger(_gen, new CylinderShape3D { Radius = OpenRadius, Height = 30f }, Vector3.Zero, OnApproach, "OpenTrigger");
		// A proper doorway trigger: just inside the hatch, so only walking in through the door counts.
		StoryBeat.MakeTrigger(_gen, new BoxShape3D { Size = new Vector3(0.9f, 1.7f, 1.1f) }, new Vector3(0, fy + 0.95f, -1.05f), OnEntered, "EntryTrigger");

		// The station card: laminated, bolted to the face right of the frame, readable while the hatch is still locked.
		PaperKit.Pinned(_gen, StationCardLocal + Vector3.Back * 0.012f, Vector3.Back, new Vector2(0.16f, 0.11f), PaperKit.Look.Card,
			"", StationCardText, Readable.NoteStyle.Printed, 1.5f, "Read the card", 7);

		_doorNode = BuildClosedDoor();
	}

	/// <summary>The steel plate and four bolts the station card sits on (built in the editor too; the card itself is runtime).</summary>
	private void BuildStationCardPlate()
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.34f, 0.33f, 0.3f);
		Vector3 c = StationCardLocal;
		k.Box(c + Vector3.Back * 0.006f, new Vector3(0.2f, 0.15f, 0.012f), 1f, Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(1.5f))));
		k.Color = new Color(0.5f, 0.48f, 0.42f);
		foreach (float sx in new[] { -0.085f, 0.085f })
			foreach (float sy in new[] { -0.06f, 0.06f })
			{
				Vector3 p = c + new Vector3(sx, sy, 0.012f);
				k.Cylinder(p, p + Vector3.Back * 0.012f, 0.009f, 0.007f, 6);
			}
		k.CommitTo(_gen, "StationCardPlate");
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

	private void OnEntered(PlayerController player)
	{
		if (!IsOpen) return;
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
