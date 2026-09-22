using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Sits at the cabin doorway. The first time the player steps back outside
/// carrying the newel post, the storm breaks, dawn comes up over the forest,
/// and a single line of narration plays — Act 5's last beat before the
/// compass leads them to the bridge. Records <see cref="StoryManager.Flag.DawnBroke"/>,
/// which the storm and the lighting mood restore from on Continue.
///
/// The doorway volume reaches a little way into the room, so leaving it is only
/// "going outside" when the player comes out on the porch side of the front
/// wall (local Z beyond the cabin's half depth); stepping back to the table
/// from the doorway does nothing.
/// </summary>
public partial class CabinExitLine : StoryTrigger
{
	[Export] public NodePath CabinPath = "..";

	private Cabin _cabin;
	private bool _exiting;

	public override void _Ready()
	{
		base._Ready();
		_cabin = GetNodeOrNull<Cabin>(CabinPath);
		BodyExited += body =>
		{
			if (body is not PlayerController p) return;
			if (!LeftTowardPorch(p)) return;
			_exiting = true;
			TryFire(p);
			_exiting = false;
		};
	}

	/// <summary>True when the player's body is now outside the front wall (on the porch side).</summary>
	private bool LeftTowardPorch(PlayerController p)
	{
		if (_cabin == null) return true;   // no cabin geometry to test against: any exit counts
		return _cabin.ToLocal(p.GlobalPosition).Z > _cabin.Depth * 0.5f;
	}

	protected override bool AlreadyHappened(StoryManager s) => s.HasFlag(StoryManager.Flag.DawnBroke);

	protected override bool CanFire(StoryManager s, PlayerController p)
		=> _exiting && p.Inventory is { HasNewelPost: true };

	protected override void Fire(PlayerController player)
	{
		StormController.Instance?.Deactivate();
		StoryBeat.SetMood(this, ForestAtmosphere.Mood.Dawn, 10f);
		StoryManager.Instance.SetFlag(StoryManager.Flag.DawnBroke);
		// The player's one thought as the storm breaks the moment they carry the post out.
		_ = Cutscene.Run(this, async ct =>
		{
			await Cutscene.Wait(this, 1.5f, ct);
			await StoryBeat.Caption(this, Line, 1.2f, 3.4f, 1.2f, ct);
		});
	}

	public const string Line = "The rain stopped. Like it wanted me to take it.";
}
