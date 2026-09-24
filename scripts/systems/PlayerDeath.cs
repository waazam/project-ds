using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// The game's one way to die. A beat that can kill (Act 12's lake, Act 13's flooding room) plays its
/// own death on screen, then calls <see cref="Reload"/>: the screen goes to black, a single line is
/// shown, and the game Continues from the latest checkpoint save — exactly what the menu's Continue
/// does, so every system restores itself the usual way and nothing from the failed attempt survives.
///
/// <see cref="ProjectDS.UI.UnderwaterView"/> is the shared "under the surface" screen layer both drownings use.
/// </summary>
public static class PlayerDeath
{
	/// <summary>True from the moment a death starts until the reload is on its way.</summary>
	public static bool Dying { get; private set; }
	/// <summary>How many deaths this run (tests read it).</summary>
	public static int Deaths { get; private set; }

	/// <summary>Marks a death as begun (so other beats can stand down); call before the death animation.</summary>
	public static void Begin() { Dying = true; Deaths++; }

	/// <summary>Black, the line, and back to the last checkpoint.</summary>
	public static async Task Reload(Node owner, string line, CancellationToken ct)
	{
		Dying = true;
		var fader = StoryBeat.Fader(owner);
		if (fader != null)
		{
			if (!fader.IsBlack) await fader.Fade(1f, 1.2f, ct);
			await Cutscene.Wait(owner, 0.8, ct);
			if (!string.IsNullOrEmpty(line)) await fader.ShowCaption(line, "", 1.2f, 2.2f, 1.2f, ct);
			await Cutscene.Wait(owner, 0.6, ct);
		}
		GD.Print($"[story] death: '{line}' - back to the last checkpoint");
		Dying = false;
		if (StoryManager.Instance?.ContinueGame() != true) StoryManager.Instance?.ReturnToMenu();
	}
}
