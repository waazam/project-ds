using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 5's payoff, inside the cabin: the friend is not there. What he left is
/// (his chair pushed back, the bandage, the stains, his page, the newel post on
/// the table). Fires once the player is inside, says the one line, marks the
/// checkpoint (the newel post and his page show from it).
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

	public const string Line = "He's not here.";

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
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act5CabinEntered);
			await StoryBeat.Caption(this, Line, 1.0f, 3.0f, 1.2f);
		});
	}
}
