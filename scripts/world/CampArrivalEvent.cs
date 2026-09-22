using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 3, in the Hollow: walking into the friend's camp marks checkpoint 3
/// (<see cref="Checkpoint.Act3DoorBoarded"/>: the enum name stayed, it now means "reached
/// the camp"). No caption of its own; the camp's things say it (the safe zone's line comes
/// when the lantern and compass are both in hand). Only from the wake-up on (checkpoint 2),
/// once.
///
/// The trigger volume is built in code: a tall cylinder of <see cref="Radius"/> around this
/// node, so it catches the player from any side of the camp.
/// </summary>
public partial class CampArrivalEvent : StoryTrigger
{
	[Export] public float Radius = 8f;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 2;
		Monitorable = false;
		AddChild(new CollisionShape3D { Name = "Shape", Shape = new CylinderShape3D { Radius = Radius, Height = 60f } });
		base._Ready();
	}

	protected override bool AlreadyHappened(StoryManager s) => s.Current >= Checkpoint.Act3DoorBoarded;
	protected override bool CanFire(StoryManager s, PlayerController p) => s.Current == Checkpoint.Act2StairsClimbed;

	protected override void Fire(PlayerController player)
	{
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act3DoorBoarded);
		GD.Print("[story] Act 3: R.H.'s camp");
	}
}
