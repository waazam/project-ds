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
		if (from.GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) { await ending.Credits(fader, ct); return; }
		// (the story now ends in the trailhead, Act 25, where there is no Act 11 to run them: the same credits here; the
		// sweep, 2026-10-07, found every ending stopping at the black)
		await Credits(from, fader, ct);
	}

	/// <summary>The credits, wherever the story ends: the end card, every picture played back as a polaroid and what the
	/// roll came to, the studio, the thanks; then the menu. The pause menu is locked meanwhile.</summary>
	public static async Task Credits(Node host, UI.ScreenFader fader, CancellationToken ct)
	{
		var pause = host.GetTree().GetFirstNodeInGroup("pause_menu") as UI.PauseMenu ?? host.GetTree().CurrentScene?.FindChild("PauseMenu", true, false) as UI.PauseMenu;
		if (pause != null) pause.Locked = true;
		try
		{
			if (fader != null)
			{
				await Act11Ending.ShowEndCard(fader, 5f);
				ct.ThrowIfCancellationRequested();
				if (PhotoLog.Instance is { RecordedCount: > 0 } log)
				{
					var montage = new UI.PolaroidMontage { Name = "PolaroidMontage" };
					host.AddChild(montage);
					await montage.Play(log.Photos, ct);
					montage.QueueFree();
					await fader.ShowCaption($"{log.SubjectsFound} of {PhotoCatalog.Total}", $"{log.TotalScore} points", 1.2f, 3f, 1.2f, ct);
				}
				await fader.ShowCaption(Act11Ending.CreditStudio, "", 1.2f, 3f, 1.2f, ct);
				await fader.ShowCaption("", Act11Ending.CreditThanks, 1.2f, 3f, 1.2f, ct);
			}
			ct.ThrowIfCancellationRequested();
			GD.Print("[story] the end: back to the menu");
			StoryManager.Instance?.ReturnToMenu();
		}
		finally
		{
			if (pause != null && GodotObject.IsInstanceValid(pause)) pause.Locked = false;
		}
	}
}
