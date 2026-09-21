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
/// </summary>
public partial class CabinExitLine : StoryTrigger
{
	private bool _exiting;

	public override void _Ready()
	{
		base._Ready();
		BodyExited += body =>
		{
			if (body is not PlayerController p) return;
			_exiting = true;
			TryFire(p);
			_exiting = false;
		};
	}

	protected override bool AlreadyHappened(StoryManager s) => s.HasFlag(StoryManager.Flag.DawnBroke);

	protected override bool CanFire(StoryManager s, PlayerController p)
		=> _exiting && p.GetNodeOrNull<PlayerInventory>("Inventory") is { HasNewelPost: true };

	protected override void Fire(PlayerController player)
	{
		StormController.Instance?.Deactivate();
		StoryBeat.SetMood(this, ForestAtmosphere.Mood.Dawn, 10f);
		StoryManager.Instance.SetFlag(StoryManager.Flag.DawnBroke);
		Cutscene.Run(this, _ => StoryBeat.Caption(this, "\"He thrusts his fists against the posts...\"", 1.2f, 3.2f, 1.2f));
	}
}
