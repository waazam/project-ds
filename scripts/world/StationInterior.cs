using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 13: the old forester station's interior - a lobby with a desk (the knife), and four ways
/// out: a taped basement door (its own puzzle chain inside <see cref="StationBasement"/>: drain
/// it, the clock chimes and breaks, a key falls out), and three rooms unlocked in order as each
/// solves the next (Room 1's key, then its cut-open cigar box, unlocks Room 2's door; Room 2's
/// keypad unlocks Room 3's door; Room 3's coins finish the act). Solving Room 3 reaches the final
/// checkpoint and rolls the credits.
///
/// Layout (local to this node, floor y=0; every room shares its connecting wall's plane with the
/// lobby, so no connecting hallway is needed - a gap cut into both walls at the same coordinate
/// lines up into one opening):
/// <code>
///   Lobby: 9x8m, walls at x=+-4.5, z=+-4.
///     front (z=-4): entry gap at x=-2, basement gap at x=+2
///     back  (z=+4): desk, Room 3 gap at x=-3
///     left  (x=-4.5): Room 2 gap at z=0
///     right (x=+4.5): Room 1 gap at z=0
///   StationRoom1 at (+7, 0, 0)      - its own -X wall gap at z=0 lines up with the lobby's right wall.
///   StationRoom2 at (-7, 0, 0)      - its own +X wall gap at z=0 lines up with the lobby's left wall.
///   StationRoom3 at (-3, 0, 6.5)    - its own -Z wall gap at x=0 lines up with the lobby's back wall.
///   StationBasement at (0, 0, -6.5) - its own +Z wall gap at x=2 lines up with the lobby's front wall.
/// </code>
/// </summary>
public partial class StationInterior : Node3D
{
	public static StationInterior Instance { get; private set; }

	[Export] public float HalfWidth = 4.5f;
	[Export] public float HalfDepth = 4f;
	[Export] public float Height = 3.4f;
	[Export] public float EntryGapX = -2f;
	[Export] public float BasementGapX = 2f;
	[Export] public float Room3GapX = -3f;

	/// <summary>Just inside the front door, facing into the lobby (world) - where LakeCrossingEvent
	/// hands the player off to when they walk into the station.</summary>
	public Vector3 EntranceMarkerWorld { get; private set; }
	public float EntranceYaw { get; private set; }

	public StationBasement Basement { get; private set; }
	public StationRoom1 Room1 { get; private set; }
	public StationRoom2 Room2 { get; private set; }
	public StationRoom3 Room3 { get; private set; }

	private StationDoor _room1Door, _room2Door, _room3Door;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		AddToGroup("station_marker");
		BuildLobby();
		BuildDesk(new Vector3(0.5f, 0, HalfDepth - 0.4f));
		BuildRoomDoors();

		Basement = new StationBasement { Name = "Basement", Position = new Vector3(0, 0, -HalfDepth - 2.5f), DoorGapX = BasementGapX };
		AddChild(Basement);
		Room1 = new StationRoom1 { Name = "Room1", Position = new Vector3(HalfWidth + 2.5f, 0, 0), DoorGapZ = 0f };
		AddChild(Room1);
		Room2 = new StationRoom2 { Name = "Room2", Position = new Vector3(-HalfWidth - 2.5f, 0, 0), DoorGapZ = 0f };
		AddChild(Room2);
		Room3 = new StationRoom3 { Name = "Room3", Position = new Vector3(Room3GapX, 0, HalfDepth + 2.5f), DoorGapX = 0f };
		AddChild(Room3);

		EntranceMarkerWorld = ToGlobal(new Vector3(EntryGapX, 0.05f, -HalfDepth + 1.0f));
		EntranceYaw = Rotation.Y;   // faces local +Z: into the lobby, toward the desk

		// Continue at checkpoint 10 (Act12LakeCrossed) lands exactly here too - anyone reaching it
		// live is already inside (LakeCrossingEvent.OnArrival teleports them in the same instant
		// the checkpoint is saved), so this is where they'd be regardless of how they got here.
		var entranceMarker = new Node3D { Name = "EntranceMarker" };
		AddChild(entranceMarker);
		entranceMarker.GlobalPosition = EntranceMarkerWorld;
		entranceMarker.GlobalRotation = new Vector3(0, EntranceYaw, 0);
		entranceMarker.AddToGroup("respawn_Act12LakeCrossed");

		SetProcess(true);
	}

	// ------------------------------------------------------------------ lobby shell

	private void BuildLobby()
	{
		var k = new MeshKit();
		var floorK = new MeshKit();
		var ceilK = new MeshKit();
		var body = new StaticBody3D { Name = "LobbyWalls", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);

		var wall = BuildingTextures.BoardsMat;
		k.Mat(wall);
		k.Color = new Color(0.43f, 0.39f, 0.34f);
		// Front wall has two gaps: split it into three pieces by hand (the shared helper only cuts one).
		float e0 = EntryGapX - 1.1f, e1 = EntryGapX + 1.1f, b0 = BasementGapX - 1.1f, b1 = BasementGapX + 1.1f;
		StationKit.WallAlongX(k, body, -HalfDepth, -HalfWidth, e0, Height, 0, null);
		StationKit.WallAlongX(k, body, -HalfDepth, e1, b0, Height, 0, null);
		StationKit.WallAlongX(k, body, -HalfDepth, b1, HalfWidth, Height, 0, null);
		StationKit.WallAlongX(k, body, HalfDepth, -HalfWidth, HalfWidth, Height, 0, (Room3GapX, 2.2f));
		StationKit.WallAlongZ(k, body, -HalfWidth, -HalfDepth, HalfDepth, Height, 0, (0f, 2.2f));
		StationKit.WallAlongZ(k, body, HalfWidth, -HalfDepth, HalfDepth, Height, 0, (0f, 2.2f));
		floorK.Mat(BuildingTextures.FloorMat);
		floorK.Color = new Color(0.52f, 0.48f, 0.42f);
		ceilK.Mat(wall);
		ceilK.Color = new Color(0.32f, 0.3f, 0.27f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, HalfWidth, HalfDepth, Height, 0);
		k.Color = Colors.White;
		k.CommitTo(this, "LobbyWalls");
		floorK.Color = Colors.White;
		floorK.CommitTo(this, "LobbyFloor");
		ceilK.Color = Colors.White;
		ceilK.CommitTo(this, "LobbyCeiling", false);

		AddChild(new OmniLight3D
		{
			Name = "LobbyLamp", LightColor = new Color(1f, 0.68f, 0.4f), LightEnergy = 2.0f,
			OmniRange = 9.5f, OmniAttenuation = 1.1f, Position = new Vector3(0, Height - 0.3f, 0),
		});
	}

	private void BuildDesk(Vector3 at)
	{
		var desk = new Node3D { Name = "Desk", Position = at };
		AddChild(desk);
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.4f, 0.3f, 0.2f);
		BuildKit.Box(k, new Vector3(0, 0.74f, 0), new Vector3(1.3f, 0.06f, 0.65f), 1.2f);
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.32f, 0.24f, 0.16f);
		BuildKit.Box(k, new Vector3(0, 0.37f, -0.15f), new Vector3(1.2f, 0.7f, 0.5f), 1.2f, BuildKit.Face.PY);
		k.Color = Colors.White;
		k.CommitTo(desk, "DeskMesh", true);

		var body = new StaticBody3D { Name = "DeskBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.4f, -0.1f), Shape = new BoxShape3D { Size = new Vector3(1.3f, 0.8f, 0.6f) } });
		desk.AddChild(body);

		// The knife, sticking out of the desktop, blade half-buried.
		var knifeSpot = new Node3D { Name = "KnifeSpot", Position = new Vector3(0.25f, 0.77f, 0.05f), Rotation = new Vector3(Mathf.DegToRad(-60f), 0.3f, 0) };
		desk.AddChild(knifeSpot);
		var pickup = new Pickup { Name = "Knife", Kind = ToolKind.Knife, UseSpot = false, SnapToSurface = false };
		knifeSpot.AddChild(pickup);
	}

	// ------------------------------------------------------------------ the three room doors

	private void BuildRoomDoors()
	{
		_room1Door = MakeDoor("Room1Door", new Vector3(HalfWidth, 0, 0), ToolKind.Key);
		_room2Door = MakeDoor("Room2Door", new Vector3(-HalfWidth, 0, 0), ToolKind.None);
		_room3Door = MakeDoor("Room3Door", new Vector3(Room3GapX, 0, HalfDepth), ToolKind.None);
	}

	private StationDoor MakeDoor(string name, Vector3 hingePos, ToolKind requiredTool)
	{
		var door = new StationDoor { Name = name, Position = hingePos, RequiredTool = requiredTool };
		AddChild(door);
		door.Setup(new Vector3(0, 1.1f, 0), new Vector3(2.1f, 2.2f, 0.3f), new Vector3(0, 1.1f, 0));
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.3f, 0.24f, 0.18f);
		BuildKit.Box(k, new Vector3(1.03f, 1.1f, 0), new Vector3(2.05f, 2.15f, 0.05f), 1.2f);
		k.Color = Colors.White;
		k.CommitTo(door, "Leaf", true);
		return door;
	}

	// ------------------------------------------------------------------ the chain: solve -> unlock

	/// <summary>Room 1's own door unlocks itself (it takes the clock's key directly, via
	/// StationDoor.RequiredTool); these two just need someone to notice the room behind them
	/// solved and let the next one open.</summary>
	public override void _Process(double delta)
	{
		if (Room1 is { Solved: true } && _room2Door is { Locked: true }) _room2Door.Unlock();
		if (Room2 is { Solved: true } && _room3Door is { Locked: true }) _room3Door.Unlock();
	}
}
