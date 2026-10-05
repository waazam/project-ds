using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// The end of the story, on its own (2026-10-04, before the last two acts went in): the save that says the story is
/// done (<see cref="Checkpoint.GameFinished"/>), the fade, the credits (the end card, the polaroids, the thanks) and
/// back to the menu. Whichever act is the last calls it once its own end is saved; a Continue afterwards finds them
/// where it ended.
/// </summary>
public static class GameEnding
{
	/// <summary>The last act's own end: the act whose end is the game's (Acts 24 and 25 move it on).</summary>
	public const Checkpoint LastAct = Checkpoint.Act24Finished;

	public static bool Played { get; private set; }

	public static async Task Play(Node from, PlayerController player, CancellationToken ct)
	{
		Played = true;
		GD.Print("[story] the end of the story: the credits");
		if (player != null) StoryBeat.ReachCheckpoint(player, Checkpoint.GameFinished);
		var fader = StoryBeat.Fader(from);
		if (fader != null) await fader.Fade(1f, 3f, ct);
		if (from.GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) await ending.Credits(fader, ct);
	}
}
