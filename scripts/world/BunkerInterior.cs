using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.World.BunkerParts;
using static ProjectDS.World.BunkerParts.BunkerLayout;

namespace ProjectDS.World;

/// <summary>
/// The bunker's inside. Lives at a fixed offset far from the outdoor terrain
/// (the player is only ever teleported here) and is built at level load from
/// four parts plus the story flow:
/// - "Hallway" (<see cref="BunkerHallway"/>): Act 8's long vaulted tunnel and its failing lights;
/// - "VineDoor" (<see cref="BunkerVineDoor"/>): the overgrown door at its end;
/// - "CrtRoom" (<see cref="CrtRoom"/>): Act 9's wall of screens and the marked set;
/// - "Rooms" (<see cref="BunkerRooms"/>): Act 10's repeating room and the run out, off at its own offset;
/// - "Flow" (<see cref="BunkerFlow"/>): the sequences that tie them to the story, and restore.
/// This node keeps the public surface other code (the entrance, the autotest) uses.
/// Groups provided: "crt_target_marker" (the marked set), "bunker_entrance_marker" (the way out:
/// the hallway's entrance until the maze wakes, then the maze's end; the hatch itself while outdoors).
/// </summary>
[GlobalClass]
public partial class BunkerInterior : Node3D
{
	public static BunkerInterior Instance { get; private set; }

	public BunkerHallway Hallway { get; private set; }
	public BunkerVineDoor VineDoor { get; private set; }
	public CrtRoom Crt { get; private set; }
	public BunkerRooms Rooms { get; private set; }
	public BunkerFlow Flow { get; private set; }

	/// <summary>For the autotest: hallway lights have committed to red for good.</summary>
	public bool RedTriggered => Hallway?.RedTriggered ?? false;
	/// <summary>For the autotest: the vine door at the hallway's end has been pushed open.</summary>
	public bool VineDoorOpenState => VineDoor?.IsOpen ?? false;
	/// <summary>For the autotest: the player is currently standing in the vine door's zone.</summary>
	public bool PlayerAtVineDoor => VineDoor?.PlayerNear ?? false;
	/// <summary>For the autotest: the player is currently in range of the marked CRT.</summary>
	public bool PlayerAtCrtTarget => Crt?.PlayerAtTarget ?? false;
	/// <summary>For the autotest: the CRT room's screens are currently off (mid-sequence).</summary>
	public bool ScreensOff => Crt?.ScreensOff ?? false;

	/// <summary>For the autotest: true from the hallway turning into the maze until its exit is reached.</summary>
	public bool MazeActive => Rooms?.Active ?? false;
	/// <summary>For the autotest: the hallway jumpscare has happened.</summary>
	public bool JumpscareFired => Flow?.JumpscareFired ?? false;
	/// <summary>True while the admit fade/teleport is running (the entrance ignores re-entry meanwhile).</summary>
	public bool IsAdmitting => Flow?.IsAdmitting ?? false;

	public Vector3 VineDoorApproachWorld => ToGlobal(new Vector3(0, 0, -86f));
	public Vector3 CrtRoomInteriorWorld => ToGlobal(new Vector3(0, 0, -95f));
	public Vector3 CrtTargetApproachWorld => Crt?.Target?.GlobalPosition ?? ToGlobal(new Vector3(0, 1.5f, CrtRoomBackZ + 2f));
	/// <summary>Aim here (from 1-2.5 m away) to use the vine door with E.</summary>
	public Vector3 VineDoorInteractWorld => VineDoor?.InteractWorld ?? ToGlobal(new Vector3(0, 1.25f, -HallLength));
	/// <summary>Aim here to use the marked CRT with E.</summary>
	public Vector3 CrtSwitchWorld => Crt?.SwitchWorld ?? CrtTargetApproachWorld;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		// Everything heavy is built here, at level load, and left sitting unseen at this far-off
		// offset: building the maze's walls and shapes mid-transition was a visible stutter.
		var watch = System.Diagnostics.Stopwatch.StartNew();
		Hallway = new BunkerHallway { Name = "Hallway" };
		AddChild(Hallway);
		VineDoor = new BunkerVineDoor { Name = "VineDoor" };
		AddChild(VineDoor);
		Crt = new CrtRoom { Name = "CrtRoom" };
		AddChild(Crt);
		Rooms = new BunkerRooms { Name = "Rooms" };
		AddChild(Rooms);

		// One marker node, moved (never swapped: StoryManager caches the node and reads its position).
		_entranceMarker = new Node3D { Name = "BunkerEntranceMarker", Position = HallwayEntranceLocal };
		AddChild(_entranceMarker);
		_entranceMarker.AddToGroup("bunker_entrance_marker");

		Flow = new BunkerFlow { Name = "Flow" };
		Flow.Setup(this, Hallway, VineDoor, Crt, Rooms);
		AddChild(Flow);
		Flow.Restore();
		GD.Print($"[bunker] interior built in {watch.ElapsedMilliseconds} ms");
	}

	public override void _Process(double delta) => Flow?.Tick(delta);

	// ------------------------------------------------------------------ the compass after the screens

	/// <summary>Where the "bunker_entrance_marker" objective stands.</summary>
	public enum CompassSpot
	{
		/// <summary>The real hatch outside (the player is outdoors: the way back in).</summary>
		Outside,
		/// <summary>The hallway's entrance end: from the CRT room, straight back out through the vine door.</summary>
		HallwayEntrance,
		/// <summary>The maze's exit cell, once the hallway has become the maze.</summary>
		MazeExit,
	}

	/// <summary>Interior-local point the compass leads to from the CRT room: the hallway's open end.</summary>
	public static Vector3 HallwayEntranceLocal => new(0, 1f, -1f);

	public CompassSpot CompassAt { get; private set; } = CompassSpot.HallwayEntrance;
	/// <summary>For tests: the marker's current world position.</summary>
	public Vector3? EntranceMarkerWorld => _entranceMarker?.GlobalPosition;

	private Node3D _entranceMarker;

	/// <summary>Moves the objective marker; see <see cref="CompassSpot"/>.</summary>
	public void PointCompass(CompassSpot spot)
	{
		if (_entranceMarker == null) return;
		CompassAt = spot;
		switch (spot)
		{
			case CompassSpot.Outside:
				if (GetTree().GetFirstNodeInGroup("bunker_marker") is Node3D hatch) _entranceMarker.GlobalPosition = hatch.GlobalPosition;
				else _entranceMarker.Position = HallwayEntranceLocal;
				break;
			case CompassSpot.MazeExit:
				// The far door while the rooms repeat; the round door behind you once it has shown itself.
				_entranceMarker.Position = Rooms is { JumpscareDone: true } ? BunkerRooms.ExitLocal : BunkerRooms.LoopLocal;
				break;
			default:
				_entranceMarker.Position = HallwayEntranceLocal;
				break;
		}
	}

	/// <summary>The entrance calls this when the player walks in through the open hatch.</summary>
	public void AdmitPlayer(PlayerController player) => Flow?.Admit(player);
}
