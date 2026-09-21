using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 5's payoff, inside the cabin: the player finds their friend at the
/// table, dead — the one who boarded the door, long before help could ever
/// have come from outside. Fires once the player is close, marks the
/// checkpoint, and leaves the newel post on the table for the player to find.
///
/// Gated so it can only happen as the story intends: after the boarded-door
/// checkpoint (Act 3) and before this one, with the door actually broken open,
/// and with the player standing inside the cabin's walls.
/// </summary>
public partial class FriendReveal : StoryTrigger
{
	/// <summary>How far inside the walls the player must be (metres from each wall).</summary>
	[Export] public float InsideMargin = 0.15f;

	private Cabin _cabin;

	public override void _Ready()
	{
		base._Ready();
		for (Node n = GetParent(); n != null && _cabin == null; n = n.GetParent()) _cabin = n as Cabin;
		_cabin ??= StoryBeat.Cabin(this);
	}

	protected override bool RecheckWhileInside => true;

	protected override bool AlreadyHappened(StoryManager s) => s.Current >= Checkpoint.Act5CabinEntered;

	protected override bool CanFire(StoryManager s, PlayerController p)
		=> s.Current >= Checkpoint.Act3DoorBoarded && s.Current < Checkpoint.Act5CabinEntered
			&& _cabin is { IsOpen: true } && PlayerInsideCabin(p);

	private bool PlayerInsideCabin(PlayerController p)
	{
		Vector3 local = _cabin.ToLocal(p.GlobalPosition);
		return Mathf.Abs(local.X) < _cabin.Width * 0.5f - InsideMargin && Mathf.Abs(local.Z) < _cabin.Depth * 0.5f - InsideMargin;
	}

	protected override void Fire(PlayerController player)
	{
		Cutscene.Run(this, async ct =>
		{
			await StoryBeat.Caption(this, "He's in the chair. He's not moving.", 1.0f, 3.0f, 1.0f);
			await StoryBeat.Caption(this, "His hand — bandaged, cut clean off. He bled out before he ever boarded that door shut.", 1.2f, 4.0f, 1.2f);
			await StoryBeat.Caption(this, "Something sits on the table in front of him.", 1.0f, 2.8f, 1.0f);
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act5CabinEntered);
		}, lockInput: true);
	}
}
