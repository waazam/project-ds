using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.StationParts;

namespace ProjectDS.World;

/// <summary>
/// Act 13: the old forester station's interior. It opens as a tidy, kept little ranger station — and
/// every time the player comes back to the lobby from somewhere else, it has rotted a little further,
/// until by the end it is an industrial hell of rust, pipes and flesh (<see cref="LobbyDecor"/>; the
/// stage follows the story's flags, and it only ever changes while the player is out of the room).
///
/// The chain (each link restores from its own flag on Continue):
///  1. The knife stuck in the desk (E). The basement door's tape: cut it (<see cref="StationBasement"/>).
///  2. Red brick stairs down, the lights struggle and die; the flood; the wheel, three times; the
///     drain takes the water — and leaves a dead eye in its grate; the drowned clock chimes, spits a key
///     and bursts.
///  3. The key opens Room 1 (<see cref="StationRoom1"/>): the cigar box, the button, the writing melts.
///     Checkpoint (<see cref="Checkpoint.Act13Room1Solved"/>): Room 2's door opens.
///  4. Room 2 (<see cref="StationRoom2"/>): the flood and the cryptex; the old lighter.
///  5. The web behind the desk burns (<see cref="IronDoor"/>): an iron door with three hollows, LOOK,
///     TOUCH, CLIMB — Room 1's three rules, broken. The dead eye (caught in the dark basement, and it
///     only moves when unwatched), the pale hand (from the right one of Room 1's blood puddles), the
///     step (from a staircase that has grown in the drained Room 2 and must be climbed).
///  6. Behind the iron door (<see cref="StationRoom3"/>): Act 14's gallery of burnt portraits, and
///     the stairwell down.
///
/// Layout (local, floor y=0): lobby x in [-5, 5], z in [-4.5, 4.5], 3.2 m high.
/// <code>
///   front (z=-4.5): the way in at x=-2.4; the basement door at x=+2.6 (stairs go down, -Z)
///   +X wall (left, facing the desk): Room 1 at z=0     -X wall (right): Room 2 at z=0
///   back (z=+4.5): the iron door at x=0, behind the desk (desk at z=+2.8), under the web
/// </code>
/// </summary>
public partial class StationInterior : Node3D
{
	public static StationInterior Instance { get; private set; }

	public const float HalfWidth = 5f, HalfDepth = 4.5f, Height = 3.2f;
	public const float EntryGapX = -2.4f, BasementGapX = 2.6f;
	public static readonly Vector3 DeskAt = new(0f, 0f, 2.8f);

	/// <summary>Just inside the front door, facing into the lobby (world) - where LakeCrossingEvent
	/// hands the player off to when they walk into the station.</summary>
	public Vector3 EntranceMarkerWorld { get; private set; }
	public float EntranceYaw { get; private set; }

	public StationBasement Basement { get; private set; }
	public StationRoom1 Room1 { get; private set; }
	public StationRoom2 Room2 { get; private set; }
	public StationRoom3 Room3 { get; private set; }
	public IronDoor Door3 { get; private set; }
	public LobbyDecor Decor { get; private set; }
	/// <summary>For tests: the lobby's current decay stage (0 kept .. 4 hell).</summary>
	public int Stage => Decor?.Stage ?? 0;

	private StationDoor _room1Door, _room2Door;
	private Node3D _handMark, _stairMark;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		AddToGroup("station_marker");
		var s = StoryManager.Instance;

		Decor = new LobbyDecor { Name = "Decor" };
		AddChild(Decor);
		Decor.Build(this);
		BuildDesk();
		BuildRoomDoors();

		Basement = new StationBasement { Name = "Basement", Position = new Vector3(BasementGapX, 0, -HalfDepth) };
		AddChild(Basement);
		Room1 = new StationRoom1 { Name = "Room1", Position = new Vector3(HalfWidth + StationRoom1.Half, 0, 0) };
		AddChild(Room1);
		Room2 = new StationRoom2 { Name = "Room2", Position = new Vector3(-HalfWidth - StationRoom2.Half, 0, 0) };
		AddChild(Room2);
		Door3 = new IronDoor { Name = "IronDoor", Position = new Vector3(0, 0, HalfDepth) };
		AddChild(Door3);
		Room3 = new StationRoom3 { Name = "Room3", Position = new Vector3(0, 0, HalfDepth) };
		AddChild(Room3);

		EntranceMarkerWorld = ToGlobal(new Vector3(EntryGapX, 0.05f, -HalfDepth + 1.2f));
		EntranceYaw = Rotation.Y + Mathf.Pi;   // facing local +Z: into the lobby, toward the desk
		Marker("EntranceMarker", EntranceMarkerWorld, EntranceYaw, "respawn_Act12LakeCrossed");
		// Room 1 solved: back by Room 2's door, facing it, when the flood takes them.
		Marker("Room2Marker", ToGlobal(new Vector3(-HalfWidth + 1.6f, 0.05f, 0.6f)), Rotation.Y + Mathf.Pi * 0.5f, "respawn_Act13Room1Solved");
		// Act 14's start: just inside Room 3's corridor, facing down it
		Marker("Room3Marker", Room3.EntryWorld, Rotation.Y + Mathf.Pi, "respawn_Act13Finished");
		// Act 14's end (Act 15's start): on the floor of the chamber under the stairwell, facing the way on
		Marker("Act15Marker", Room3.ToGlobal(StationRoom3.StairwellAt + new Vector3(0, Stairwell.BottomYFor(Stairwell.DefaultRevolutions) + 0.05f, 0)),
			Rotation.Y + Mathf.Pi, "respawn_Act14Finished");
		// Act 15's end (Act 16's start): in the janitor's closet at the end of the long hallway, facing its door
		Marker("Act16Marker", Room3.ToGlobal(StationRoom3.StairwellAt + new Vector3(0, Stairwell.BottomYFor(Stairwell.DefaultRevolutions), Stairwell.HallwayZ) + Act15Hallway.ClosetCentre + Vector3.Up * 0.05f),
			Rotation.Y, "respawn_Act15Finished");

		if (CryptexOverlay.Instance == null) Cutscene.SceneRoot(this).AddChild(new CryptexOverlay { Name = "CryptexOverlay" });
		// the scavenger hunt's breadcrumbs: a bloody hand on Room 1's door, a staircase scrawled on Room 2's
		_handMark = new Node3D { Name = "HandMark", Visible = false };
		_room1Door.AddChild(_handMark);
		StationProps.Decal(_handMark, StationTextures.HandprintMat, new Vector3(1.05f, 1.35f, 0.035f), Vector3.Back, new Vector2(0.4f, 0.4f), 0.2f);
		_stairMark = new Node3D { Name = "StairMark", Visible = false };
		_room2Door.AddChild(_stairMark);
		SignKit.Text(_stairMark, "_|\n  _|\n    _|", new Vector3(1.05f, 1.4f, 0.035f), Basis.Identity, 0.16f, new Color(0.35f, 0.02f, 0.02f), shadow: false);
		// Continue: open whatever the story says is open.
		if (s != null && s.HasFlag(StoryManager.Flag.StationRoom1Open)) _room1Door.Unlock();
		if (s != null && s.HasFlag(StoryManager.Flag.StationRoom1Solved)) _room2Door.Unlock();
		SetProcess(true);
	}

	private void Marker(string name, Vector3 at, float yaw, string group)
	{
		var m = new Node3D { Name = name };
		AddChild(m);
		m.GlobalPosition = at;
		m.GlobalRotation = new Vector3(0, yaw, 0);
		m.AddToGroup(group);
	}

	// ------------------------------------------------------------------ the desk and the knife

	private void BuildDesk()
	{
		var desk = new Node3D { Name = "Desk", Position = DeskAt };
		AddChild(desk);
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.46f, 0.32f, 0.2f);
		BuildKit.Box(k, new Vector3(0, 0.76f, 0), new Vector3(1.9f, 0.06f, 0.8f), 1.2f);
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.36f, 0.25f, 0.16f);
		BuildKit.Box(k, new Vector3(0, 0.38f, -0.3f), new Vector3(1.85f, 0.72f, 0.05f), 1.2f);   // the modesty panel, facing the room
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(s * 0.62f, 0.38f, 0.02f), new Vector3(0.55f, 0.72f, 0.7f), 1.2f);   // drawer pedestals
		k.Color = new Color(0.25f, 0.2f, 0.14f);
		foreach (int s in new[] { -1, 1 })
			for (int d = 0; d < 3; d++)
				BuildKit.Box(k, new Vector3(s * 0.62f, 0.15f + d * 0.22f, -0.335f), new Vector3(0.45f, 0.17f, 0.02f), 2f);
		// a blotter, a brass bell, a logbook
		k.Mat(StationTextures.Flat("st_blotter", new Color(0.18f, 0.24f, 0.18f), 0.9f, 0.1f));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(-0.1f, 0.795f, -0.05f), new Vector3(0.7f, 0.01f, 0.45f));
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.9f, 0.75f, 0.45f);
		k.Cylinder(new Vector3(0.62f, 0.79f, -0.15f), new Vector3(0.62f, 0.85f, -0.15f), 0.05f, 0.02f, 10, true);
		k.Mat(StationTextures.Flat("st_logbook", new Color(0.35f, 0.12f, 0.08f), 0.8f, 0.2f));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(-0.55f, 0.81f, 0.05f), new Vector3(0.26f, 0.035f, 0.34f));
		k.Color = Colors.White;
		k.CommitTo(desk, "DeskMesh", true);
		// a green-shaded banker's lamp, still warm
		var lamp = new MeshKit();
		lamp.Mat(ItemTextures.BrassMat);
		lamp.Color = new Color(0.85f, 0.7f, 0.42f);
		lamp.Cylinder(new Vector3(0.55f, 0.79f, 0.2f), new Vector3(0.55f, 0.81f, 0.2f), 0.08f, 0.08f, 10, true);
		lamp.Cylinder(new Vector3(0.55f, 0.81f, 0.2f), new Vector3(0.55f, 1.12f, 0.2f), 0.012f, 0.012f, 6, true);
		lamp.Mat(StationTextures.Flat("st_lampshade", new Color(0.12f, 0.35f, 0.18f), 0.3f, 0.6f));
		lamp.Color = Colors.White;
		lamp.Cylinder(new Vector3(0.4f, 1.12f, 0.2f), new Vector3(0.7f, 1.12f, 0.2f), 0.09f, 0.07f, 10, true);
		lamp.CommitTo(desk, "Lamp", false);
		Decor.DeskLamp = new OmniLight3D
		{
			Name = "DeskLight", LightColor = new Color(1f, 0.78f, 0.5f), LightEnergy = 0.8f, OmniRange = 3f,
			Position = new Vector3(0.55f, 1.0f, 0.1f),
		};
		desk.AddChild(Decor.DeskLamp);

		var body = new StaticBody3D { Name = "DeskBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.42f, 0), Shape = new BoxShape3D { Size = new Vector3(1.9f, 0.84f, 0.8f) } });
		desk.AddChild(body);

		// The knife, driven into the desktop, blade half-buried.
		var knifeSpot = new Node3D { Name = "KnifeSpot", Position = new Vector3(0.2f, 0.8f, -0.2f), Rotation = new Vector3(Mathf.DegToRad(-62f), 0.3f, 0) };
		desk.AddChild(knifeSpot);
		knifeSpot.AddChild(new Pickup { Name = "Knife", Kind = ToolKind.Knife, UseSpot = false, SnapToSurface = false, TakenLine = "It came out of the wood too easily." });
	}

	// ------------------------------------------------------------------ the two room doors

	private void BuildRoomDoors()
	{
		// Room 1 (+X wall): the clock's key opens it. Room 2 (-X): opens itself when Room 1 is solved.
		_room1Door = MakeDoor("Room1Door", new Vector3(HalfWidth, 0, -1.05f), -Mathf.Pi * 0.5f, ToolKind.Key);
		_room1Door.Opened += () => StoryManager.Instance?.SetFlag(StoryManager.Flag.StationRoom1Open);
		_room2Door = MakeDoor("Room2Door", new Vector3(-HalfWidth, 0, 1.05f), Mathf.Pi * 0.5f, ToolKind.None);
	}

	/// <summary>Room 2's door (it slams shut behind the player, then lets them out when they win).</summary>
	public StationDoor Room2Door => _room2Door;

	private StationDoor MakeDoor(string name, Vector3 hingePos, float yaw, ToolKind requiredTool)
	{
		var door = new StationDoor { Name = name, Position = hingePos, Rotation = new Vector3(0, yaw, 0), RequiredTool = requiredTool };
		AddChild(door);
		door.Setup(new Vector3(1.05f, 1.1f, 0), new Vector3(2.1f, 2.2f, 0.3f), new Vector3(1.05f, 1.1f, 0));
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.38f, 0.28f, 0.18f);
		BuildKit.Box(k, new Vector3(1.03f, 1.08f, 0), new Vector3(2.02f, 2.14f, 0.05f), 1.2f);
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.8f, 0.65f, 0.4f);
		foreach (int s in new[] { -1, 1 })
			k.Cylinder(new Vector3(1.85f, 1.02f, 0), new Vector3(1.85f, 1.02f, s * 0.07f), 0.03f, 0.03f, 8, true);
		k.Color = Colors.White;
		k.CommitTo(door, "Leaf", true);
		return door;
	}

	// ------------------------------------------------------------------ the chain

	public override void _Process(double delta)
	{
		var s = StoryManager.Instance;
		if (Room1 is { Solved: true } && _room2Door is { Locked: true } && Room2 is { DoorShut: false }) _room2Door.Unlock();
		if (s != null && _handMark != null)
		{
			bool seen = s.HasFlag(StoryManager.Flag.StationDoor3Seen);
			_handMark.Visible = seen && !s.HasFlag(StoryManager.Flag.StationHandTaken);
			_stairMark.Visible = seen && !s.HasFlag(StoryManager.Flag.StationStepTaken);
		}
		// The lobby rots in the player's absence: never before their eyes.
		var player = StoryBeat.Player(this);
		if (player != null && Decor != null) Decor.Advance(s, !InLobby(player.GlobalPosition));
	}

	/// <summary>True while a world point is inside the lobby's four walls.</summary>
	public bool InLobby(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		return Mathf.Abs(l.X) < HalfWidth + 0.2f && Mathf.Abs(l.Z) < HalfDepth + 0.2f && l.Y > -1f && l.Y < Height + 1f;
	}
}
